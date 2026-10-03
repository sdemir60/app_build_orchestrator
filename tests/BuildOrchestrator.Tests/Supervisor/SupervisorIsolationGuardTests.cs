using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [spec 2026-09-18 §5.5 · Task 10 fix I1] Gerçek bir Supervisor başlatan her test İZOLE bir önbellekle başlatır
/// (<see cref="SupervisorSandbox"/> ya da kendi <c>--logs</c> klasörü). Motor açılışta uçuş defterini kurtarır;
/// argümansız başlayan bir test kullanıcının gerçek <c>run-inflight.json</c>'ını işlerdi. Başlatılacak motor YALNIZ
/// sandbox'tan gelir (<see cref="SupervisorSandbox.IsolatedEngineHost"/>); <c>TestPaths.SupervisorExe</c> üzerinde
/// kurulan konak yalnızca başlatılmayan VM konağıdır: <c>new EngineHost(TestPaths.SupervisorExe)</c>.
///
/// <para><b>Taranan biçimler (kaynak metni, yorum satırları hariç):</b></para>
/// <list type="number">
/// <item><c>TestPaths.SupervisorExe</c> geçen her satır ya <c>new EngineHost(TestPaths.SupervisorExe</c> (başlatılmayan
/// VM konağı) ya da <c>"--logs"</c> taşır. İzole yardımcıların dosyaları (TestPaths, sandbox) ve bu guard hariç.</item>
/// <item><c>EngineHost(TestPaths.SupervisorExe</c>'nin hemen ardından <c>)</c> gelir: zaman aşımı, argüman listesi ya da
/// kill stratejisi taşıyan her biçim BAŞLATILMAK için kurulmuş bir konaktır ve satır <c>"--logs"</c> taşısa bile
/// ihlaldir (başlatılan motor sandbox'tan gelir).</item>
/// <item>Bir üye (erişim belirteçli bir metot bildiriminden bir sonrakine kadar olan metin)
/// <c>new EngineHost(TestPaths.SupervisorExe</c> kuruyor VE <c>.StartAsync(</c> / <c>.RestartAsync(</c> /
/// <c>RestartEngineCommand</c> çağırıyorsa ihlaldir: başlatılan motor sandbox'tan gelmelidir
/// (<see cref="SupervisorSandbox.IsolatedEngineHost"/>).</item>
/// <item>Konağı kuran üye ile başlatan üye ayrışırsa üye bazlı kural başlatmayı göremez; bu yüzden konağı başka bir
/// üyeye DAĞITAN yapı da ihlaldir. Dönüş tipi <c>EngineHost</c> / <c>Task&lt;EngineHost&gt;</c> /
/// <c>ValueTask&lt;EngineHost&gt;</c> olan ve gövdesi <c>new EngineHost(TestPaths.SupervisorExe</c> kuran metot. Bu
/// ifadeyle başlatılan <c>public</c> / <c>internal</c> / <c>protected</c> alan ya da özellik: başka bir dosyadaki test onu
/// başlatabilir, dosya bazlı tarama göremez — her zaman ihlaldir. <c>private</c> alan ya da özellik yalnız AYNI dosya onu
/// adıyla başlatıyorsa ihlaldir (<c>ad.StartAsync(</c>, <c>ad.RestartAsync(</c> ya da adın geçtiği bir üyede
/// <c>RestartEngineCommand</c>); kurulup VM'e verilen ve dispose edilen ama hiç başlatılmayan alan serbesttir
/// (<c>StartWithWindowsTests.SaveBench</c>).</item>
/// </list>
/// <para>Her kural <see cref="Scan"/> üzerinde bellek içi satırlarla da sınanır (ihlal örneği + izinli biçim): depo
/// taraması yalnız bugünkü kaynağı gördüğünden bir kuralın ihlali gerçekten yakaladığını tek başına göstermez.</para>
/// <para><b>Kör noktalar (bilinçli):</b> private alanın bir yerel değişkene/takma ada kopyalanıp ya da onu taşıyan bir
/// nesne (ör. VM'in <c>RestartEngineCommand</c>'ı) üzerinden başlatılması; başka dosyadaki bir partial sınıf parçasının
/// başlatması; konağın alana başlatıcı dışında (ör. yapıcıda) atanması ya da başlatıcısının sonraki satıra taşması;
/// exe yolunu başka bir ifadeyle türetmek (ör. bir değişkene alıp oradan kurmak); <c>MainWindow</c>'un <c>Loaded</c>'da
/// motoru kendiliğinden başlatması (bugün <c>MainWindowHost</c> var olmayan bir exe verir); <c>Process.Start</c>'a elle
/// kurulmuş bir <c>ProcessStartInfo</c> vermek. Guard çağrının BİÇİMİNE bakar, niyetine değil — gerisi review'ın işidir.
/// <c>TestPaths.Psi</c> ise <c>logsDir</c>'i ZORUNLU alır; argümansız biçim derlenmez.</para>
/// </summary>
public sealed class SupervisorIsolationGuardTests
{
    private static readonly string[] AllowedFiles =
    [
        Path.Combine("BuildOrchestrator.Tests", "Supervisor", "SupervisorIpcTests.cs"),   // TestPaths tanımı
        Path.Combine("BuildOrchestrator.Tests", "Supervisor", "SupervisorSandbox.cs"),
        Path.Combine("BuildOrchestrator.Tests", "Supervisor", "SupervisorIsolationGuardTests.cs"),
    ];

    private static readonly Regex UnstartedHost = new(@"EngineHost\(TestPaths\.SupervisorExe");
    /// <summary>Bir üye bildiriminin başı (erişim belirteciyle başlayan metot/özellik) — taramanın birimi. Erişim
    /// belirteci taşımayan yerel fonksiyonlar bölmez, yani test gövdesindeki yardımcılar testin parçası sayılır.</summary>
    private static readonly Regex Member = new(@"^\s*(public|private|internal|protected)\b[^=;]*\(");
    private const string StartCalls = @"(?:StartAsync|RestartAsync)\(";
    private const string RestartCommand = "RestartEngineCommand";
    private static readonly Regex StartsEngine = new($@"\.{StartCalls}|{RestartCommand}");
    /// <summary>Konak kurulurken exe yolunun hemen ardından <c>)</c> gelmiyor: ek argüman (zaman aşımı, argüman listesi,
    /// kill stratejisi) taşıyan konak BAŞLATILMAK için kurulmuştur.</summary>
    private static readonly Regex ExtraArgsHost = new(@"EngineHost\(TestPaths\.SupervisorExe(?!\))");
    /// <summary>Üye bildiriminin dönüş tipi bir konak: <c>EngineHost</c>, <c>Task&lt;EngineHost&gt;</c> ya da
    /// <c>ValueTask&lt;EngineHost&gt;</c>. Tip, metot adının ve '(' nin hemen önündedir; parametre tipi sayılmaz.</summary>
    private static readonly Regex ReturnsHost = new(@"\bEngineHost>?\s+\w+\s*\(");
    /// <summary>Alan ya da özellik bildirimi, başlatıcısı <c>new EngineHost(TestPaths.SupervisorExe</c>:
    /// <c>[erişim] Tip ad = new ...</c>, <c>Tip ad =&gt; new ...</c> ya da <c>Tip ad { get; } = new ...</c>. Adın önünde '('
    /// yoktur (metot değil) ve en az bir belirteç (<c>mods</c>) taşır: belirteçsiz <c>EngineHost x = new ...</c> bir yerel de
    /// olabilir, onu üye kuralı görür.</summary>
    private static readonly Regex HostField = new(
        @"^\s*(?<mods>(?:(?:public|private|internal|protected|static|readonly)\s+)+)[^;(=]*?\b(?<name>\w+)\s*(?:\{[^}]*\}\s*)?=>?\s*new\s+(?:\w+\.)*EngineHost\(TestPaths\.SupervisorExe");
    /// <summary>Alan/özelliğin sınıf dışına çıkabilen erişimi: başka dosyadaki bir test onu başlatabilir.</summary>
    private static readonly Regex ExposedAccess = new(@"\b(?:public|internal|protected)\b");

    [Fact]
    public void Every_test_that_starts_a_real_supervisor_isolates_its_cache()
    {
        var offenders = new List<string>();
        foreach (string file in RepoPaths.TestSourceFiles("*.cs"))
        {
            string relative = Path.GetRelativePath(RepoPaths.TestsRoot, file);
            if (AllowedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
            offenders.AddRange(Scan(relative, File.ReadAllLines(file)));
        }

        Assert.True(offenders.Count == 0,
            "a real supervisor is started without an isolated cache (use SupervisorSandbox):\n  "
            + string.Join("\n  ", offenders));
    }

    // ---------------------------------------------------------------- bellek içi tarama (Scan): kural başına ihlal örneği + izinli biçimler

    private const string PrivateHostField = "    private readonly EngineHost _engine = new EngineHost(TestPaths.SupervisorExe);";

    private static List<string> Offenders(params string[] lines) => [.. Scan("Fake.cs", lines)];

    private static void AssertFlagged(params string[] lines) =>
        Assert.True(Offenders(lines).Count > 0, "the guard misses a violation:\n  " + string.Join("\n  ", lines));

    private static void AssertAllowed(params string[] lines)
    {
        var offenders = Offenders(lines);
        Assert.True(offenders.Count == 0,
            "the guard rejects an allowed form:\n  " + string.Join("\n  ", lines)
            + "\nit reported:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Konak yalnız <c>EngineHost(TestPaths.SupervisorExe)</c> biçiminde kurulur. Zaman aşımı, argüman listesi
    /// ya da kill stratejisi taşıyan her biçim BAŞLATILMAK için kurulmuştur — satır <c>"--logs"</c> taşısa bile ihlaldir
    /// (başlatılan motor sandbox'tan gelir).</summary>
    [Theory]
    [InlineData("await using var engine = new EngineHost(TestPaths.SupervisorExe, TimeSpan.FromSeconds(5));")]
    [InlineData("await using var engine = new EngineHost(TestPaths.SupervisorExe, null, [\"--verbose\"]);")]
    [InlineData("await using var engine = new EngineHost(TestPaths.SupervisorExe, null, [\"--logs\", dir]);")]
    [InlineData("await using var engine = new EngineHost(TestPaths.SupervisorExe, killStrategy: p => p.Kill());")]
    public void A_host_on_the_real_exe_with_extra_arguments_is_flagged_even_when_it_carries_logs(string line) =>
        AssertFlagged(line);

    /// <summary>İzinli biçimler: başlatılmayan tek argümanlı konak (yerel ya da kurucu argümanı), sandbox'ın izole
    /// konağı ve <c>"--logs"</c> taşıyan elle kurulmuş süreç satırı.</summary>
    [Theory]
    [InlineData("await using var engine = new EngineHost(TestPaths.SupervisorExe);")]
    [InlineData("var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => \"r1\");")]
    [InlineData("await using var engine = sandbox.IsolatedEngineHost(TimeSpan.FromSeconds(5));")]
    [InlineData("var psi = new ProcessStartInfo(TestPaths.SupervisorExe) { ArgumentList = { \"--logs\", dir } };")]
    public void The_unstarted_host_the_sandbox_host_and_a_logs_line_are_allowed(string line) =>
        AssertAllowed(line);

    /// <summary>Konağı kurup başka bir üyenin başlatmasına bırakan yardımcı (dönüş tipi konak) ihlaldir: kuran üye
    /// başlatandan ayrılınca üye bazlı kural başlatmayı göremez.</summary>
    [Theory]
    [InlineData("EngineHost")]
    [InlineData("Task<EngineHost>")]
    [InlineData("ValueTask<EngineHost>")]
    public void A_helper_that_returns_a_host_built_on_the_real_exe_is_flagged(string returnType) =>
        AssertFlagged($"    private static {returnType} NewHost()", "        => Wrap(new EngineHost(TestPaths.SupervisorExe));");

    /// <summary>Sandbox konağı dönen yardımcı serbesttir; parametre tipi dönüş tipi sayılmaz (bu üye bir VM döner,
    /// konak yalnız bir argümandır).</summary>
    [Fact]
    public void A_helper_that_returns_a_sandbox_host_and_a_member_that_only_takes_a_host_are_allowed()
    {
        AssertAllowed(
            "    private static async Task<EngineHost> StartedEngineAsync(SupervisorSandbox sandbox)",
            "    {",
            "        var engine = sandbox.IsolatedEngineHost();",
            "        await engine.StartAsync();",
            "        return engine;",
            "    }");
        AssertAllowed(
            "    private static RunViewModel VmOver(EngineHost other) =>",
            "        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => \"r1\");");
    }

    /// <summary><c>public</c> / <c>internal</c> / <c>protected</c> bir alan ya da özellik başlatıcısı konağı başka
    /// dosyadaki bir testin başlatabileceği yere koyar (dosya bazlı tarama görmez): her zaman ihlaldir.</summary>
    [Theory]
    [InlineData("    public EngineHost Host { get; } = new EngineHost(TestPaths.SupervisorExe);")]
    [InlineData("    internal static readonly EngineHost Shared = new EngineHost(TestPaths.SupervisorExe);")]
    [InlineData("    protected EngineHost Host => new EngineHost(TestPaths.SupervisorExe);")]
    public void A_non_private_field_or_property_holding_a_host_on_the_real_exe_is_flagged(string declaration) =>
        AssertFlagged(declaration);

    /// <summary><c>private</c> alan, alanı kurup bir test üyesinde başlatmak (kör nokta) yalnız AYNI dosya onu adıyla
    /// başlatıyorsa ihlaldir: <c>ad.StartAsync(</c>, <c>ad.RestartAsync(</c> ya da adın geçtiği bir üyede
    /// <c>RestartEngineCommand</c>.</summary>
    [Theory]
    [InlineData("        await _engine.StartAsync();")]
    [InlineData("        await _engine.RestartAsync();")]
    public void A_private_host_field_is_flagged_when_the_file_starts_it_by_name(string start) =>
        AssertFlagged(PrivateHostField, "    public async Task A_test()", "    {", start, "    }");

    [Fact]
    public void A_private_host_field_is_flagged_when_a_member_that_names_it_restarts_the_engine() =>
        AssertFlagged(
            PrivateHostField,
            "    public async Task A_test()",
            "    {",
            "        var vm = new RunViewModel(_engine, NeverTickingBatcher(), () => \"r1\");",
            "        await vm.RestartEngineCommand.ExecuteAsync(null);",
            "    }");

    /// <summary>Kurulup VM'e verilen ve dispose edilen ama hiç başlatılmayan private alan serbesttir
    /// (<c>StartWithWindowsTests.SaveBench</c> biçimi); adını anmayan bir üyedeki restart komutu onu başlatmış sayılmaz.</summary>
    [Fact]
    public void A_private_host_field_nobody_starts_is_allowed()
    {
        AssertAllowed(
            "    private sealed class SaveBench : IAsyncDisposable",
            "    {",
            PrivateHostField,
            "        public SaveBench() => Run = new RunViewModel(_engine, NeverTickingBatcher(), () => \"r1\");",
            "        public ValueTask DisposeAsync() => _engine.DisposeAsync();",
            "    }");
        AssertAllowed(
            PrivateHostField,
            "    public async Task Other_member()",
            "    {",
            "        await sandboxVm.RestartEngineCommand.ExecuteAsync(null);",
            "    }");
    }

    /// <summary>Bir dosyanın satırlarını tarar, ihlal başına bir satır döner. Dosya sistemine dokunmaz: bellek içi
    /// kaynakla da sınanır (yukarıdaki testler).</summary>
    internal static IEnumerable<string> Scan(string file, string[] lines)
    {
        int memberStart = 0;
        bool constructs = false, starts = false, returnsHost = false, restarts = false;
        var restartMembers = new List<(int From, int To)>();   // RestartEngineCommand geçen üyelerin satır aralığı
        var privateHosts = new List<(string Name, int At)>();   // private alan/özellik konakları: ad geçişine sonda bakılır
        for (int i = 0; i <= lines.Length; i++)
        {
            bool atEnd = i == lines.Length;
            if (atEnd || Member.IsMatch(lines[i]))
            {
                if (constructs && starts) yield return $"{file}:{memberStart + 1} (a member starts an EngineHost built on TestPaths.SupervisorExe)";
                if (constructs && returnsHost) yield return $"{file}:{memberStart + 1} (a member returns an EngineHost built on TestPaths.SupervisorExe - use SupervisorSandbox.IsolatedEngineHost)";
                if (restarts) restartMembers.Add((memberStart, i));
                if (atEnd) break;
                memberStart = i;
                returnsHost = ReturnsHost.IsMatch(lines[i]);
                constructs = starts = restarts = false;
            }
            string line = lines[i];
            if (IsComment(line)) continue;

            if (UnstartedHost.IsMatch(line))
            {
                if (ExtraArgsHost.IsMatch(line))
                    yield return $"{file}:{i + 1} (an EngineHost built on TestPaths.SupervisorExe takes extra arguments - use SupervisorSandbox.IsolatedEngineHost)";
                Match field = HostField.Match(line);
                if (!field.Success) constructs = true;   // yerel ya da argüman: üyenin parçası
                else if (ExposedAccess.IsMatch(field.Groups["mods"].Value))
                    yield return $"{file}:{i + 1} (a non-private field or property holds an EngineHost built on TestPaths.SupervisorExe - use SupervisorSandbox.IsolatedEngineHost)";
                else privateHosts.Add((field.Groups["name"].Value, i));
            }
            else if (line.Contains("TestPaths.SupervisorExe", StringComparison.Ordinal)
                     && !line.Contains("\"--logs\"", StringComparison.Ordinal))
                yield return $"{file}:{i + 1} (TestPaths.SupervisorExe without --logs)";
            if (StartsEngine.IsMatch(line)) starts = true;
            if (line.Contains(RestartCommand, StringComparison.Ordinal)) restarts = true;
        }

        foreach ((string name, int at) in privateHosts)
            if (StartedByName(name, at, lines, restartMembers))
                yield return $"{file}:{at + 1} (the file starts {name}, an EngineHost field or property built on TestPaths.SupervisorExe - use SupervisorSandbox.IsolatedEngineHost)";
    }

    private static bool IsComment(string line) => line.TrimStart().StartsWith("//", StringComparison.Ordinal);

    /// <summary>Alan/özellik konağı bu dosyada ADIYLA başlatılıyor mu: <c>ad.StartAsync(</c> / <c>ad.RestartAsync(</c> ya da
    /// adın geçtiği bir üyede <c>RestartEngineCommand</c>. Ad dosya genelinde aranır (başka kapsamdaki aynı adlı bir yerel
    /// de sayılır, bilerek muhafazakâr); bildirim satırının kendisi sayılmaz.</summary>
    private static bool StartedByName(string name, int declaredAt, string[] lines, List<(int From, int To)> restartMembers)
    {
        string id = Regex.Escape(name);
        var call = new Regex($@"\b{id}\??\.{StartCalls}");
        var reference = new Regex($@"\b{id}\b");
        var code = Enumerable.Range(0, lines.Length).Where(i => i != declaredAt && !IsComment(lines[i])).ToList();
        return code.Any(i => call.IsMatch(lines[i]))
            || restartMembers.Any(m => code.Any(i => i >= m.From && i < m.To && reference.IsMatch(lines[i])));
    }
}
