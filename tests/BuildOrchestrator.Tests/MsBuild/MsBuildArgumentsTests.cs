using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.MsBuild;

public class MsBuildArgumentsTests
{
    /// <summary>
    /// [tek proje · design §3.8] Derleme hedefi SEÇİLEBİLİR: varsayılan <c>-t:Build</c>, satır menüsünün
    /// <b>Rebuild</b>'i <c>-t:Rebuild</c> (MSBuild'in kendi Clean+Build'i). Hedef TEK yerde yazılır — komut
    /// satırının geri kalanı (v1 bayrakları, BuildProjectReferences=false, obj izolasyonu) DEĞİŞMEZ, yani
    /// iki hedef arasında sözleşme farkı yoktur.
    /// </summary>
    [Theory]
    [InlineData(MsBuildTarget.Build, "-t:Build")]
    [InlineData(MsBuildTarget.Rebuild, "-t:Rebuild")]
    [InlineData(MsBuildTarget.Clean, "-t:Clean")]
    public void The_build_target_is_selectable_and_nothing_else_changes(MsBuildTarget target, string expected)
    {
        var args = MsBuildArguments.Build(@"c:
\p.csproj", "Debug", target: target);

        Assert.Contains(expected, args);
        Assert.Single(args, a => a.StartsWith("-t:", StringComparison.Ordinal)); // tek hedef, çelişen ikinci YOK
        Assert.Contains("-p:BuildProjectReferences=false", args);
        Assert.Contains("-p:UseSharedCompilation=false", args);
    }

    /// <summary>Hedef verilmezse <c>-t:Build</c>'dir: bu alandan ÖNCE yazılmış her çağrı yeri (tam koşu,
    /// SCC turları) birebir aynı komut satırını üretmeye devam eder.</summary>
    [Fact]
    public void The_default_target_is_build()
        => Assert.Contains("-t:Build", MsBuildArguments.Build(@"c:
\p.csproj", "Debug"));

    [Fact]
    public void Build_contains_v1_flags_and_BuildProjectReferences_false() // [SPIKE S2 şart-3 + D9]
    {
        var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug");
        Assert.Contains("-p:UseSharedCompilation=false", args);
        Assert.Contains("-nodeReuse:false", args);
        Assert.Contains("-p:BuildProjectReferences=false", args);
        Assert.Contains("-p:Configuration=Debug", args);
        Assert.Equal(@"c:\r\p.csproj", args[0]);
    }

    /// <summary>[T33 KARAR PİNİ] Shared compilation KAPALI kalır — karar ve gerekçe:
    /// <c>.claude/outputs/2026-07-26-07-38-t33-decision.md</c>. Yukarıdaki test bayrakların VARLIĞINI pinler;
    /// bu test ters yönü kapatır: hiçbir yol (build ya da restore) shared compilation'ı ya da node reuse'u
    /// GERİ AÇAMAZ. Açılırsa emit, inner Job'a üye OLMAYAN kalıcı <c>VBCSCompiler</c>'a taşınır ve §3'ün
    /// "torn DLL yok" garantisi kill anında kırılır (bkz. KillMidBuildTests: bayraklar → VBCSCompiler yok →
    /// her writer Job üyesi).</summary>
    [Fact]
    public void Shared_compilation_and_node_reuse_can_not_be_re_enabled_on_the_build_path() // [T33]
    {
        string[] reEnabling = ["UseSharedCompilation=true", "nodeReuse:true", "-m:", "MSBUILDDISABLENODEREUSE=0"];
        var build = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug");

        foreach (string flag in reEnabling)
            Assert.DoesNotContain(build, a => a.Contains(flag, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, build.Count(a => a == "-p:UseSharedCompilation=false")); // tek kez, çelişen ikinci değer YOK
        Assert.Equal(1, build.Count(a => a == "-nodeReuse:false"));
    }

    /// <summary>[T33 · fix round 1 · Important 5] Restore yolu neden bu bayrakları TAŞIMIYOR: restore DERLEMEZ,
    /// yalnız <c>-t:restore</c> hedefini koşar — compiler server (<c>VBCSCompiler</c>) hiç devreye girmez, emit
    /// olmaz, dolayısıyla torn-DLL yüzeyi yoktur. Bu testin önceki hâli restore'da "shared compilation açılmamış"
    /// diye assert ediyordu; restore o bayrağı ZATEN hiç geçirmediği için o assert KIRILAMAZDI (sahte kapsama).
    /// Gerçek ve kırılabilir pin budur: restore'un hedef kümesi Build'i İÇERMEZ. İçerecek şekilde değişirse
    /// (ör. <c>-t:restore;Build</c>) burada RED verir ve o yolun da flag'lenmesi gerektiği anlaşılır.</summary>
    [Fact]
    public void The_restore_path_compiles_nothing_which_is_why_it_carries_no_compiler_flags() // [T33]
    {
        var restore = MsBuildArguments.RestorePackagesConfig(@"c:\r\p.csproj", @"c:\r\slnDir");

        var targets = restore.Where(a => a.StartsWith("-t:", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(["-t:restore"], targets);                       // TEK hedef: restore
        Assert.DoesNotContain(restore, a => a.Contains("Build", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>[T33 · fix round 1 · Important 6] Bayrakların TEK KAYNAK iddiasının pini. Child process'ler
    /// parent ortamını miras alır (<c>JobProcessLauncher</c> env'i devreder), yani bu ayarlar komut satırı DIŞINDA
    /// da (env değişkeni, <c>Directory.Build.props</c>) doğabilir. Komut satırındaki <c>-p:</c> global property'si
    /// MSBuild önceliğinde environment property'sini EZER — asıl risk budur ve kapalıdır; buradaki guard ise
    /// "bu değerleri üretimde başka bir yerin yazmadığını" kilitler: <c>UseSharedCompilation</c> / <c>nodeReuse</c>
    /// / <c>MSBUILDDISABLENODEREUSE</c> literalleri üretim kodunda (<c>src/**.cs</c>) ve MSBuild yapılandırma
    /// dosyalarında (<b>TÜM repo</b>: <c>*.csproj</c> · <c>*.props</c> · <c>*.targets</c>) yalnız
    /// <see cref="MsBuildArguments"/>'ta geçebilir. Kalan yüzey (kullanıcı makinesinden MİRAS gelen env) karar
    /// kaydında açık sınırlama olarak yazılıdır.
    ///
    /// <para>[fix round 2 · Important 2] Yapılandırma kolu artık <c>src/</c>'den DEĞİL repo kökünden taranır:
    /// kökteki <c>Directory.Build.props</c> <c>src/</c> ağacının DIŞINDADIR, yani eski hâlde props/targets kolu
    /// <b>sıfır dosya</b> tarıyordu — flag oraya eklense pin hiç fark etmezdi. Ayrıca her kol için taramanın
    /// dosya GÖRDÜĞÜ ayrıca assert edilir: sıfır-dosya taraması artık sessizce yeşil kalmaz, RED verir.</para></summary>
    [Fact]
    public void The_compiler_server_switches_have_exactly_one_source_in_the_repo() // [T33]
    {
        var rule = new Regex("UseSharedCompilation|nodeReuse|MSBUILDDISABLENODEREUSE", RegexOptions.IgnoreCase);
        string srcOwner = Path.Combine("BuildOrchestrator.Core", "MsBuild", "MsBuildArguments.cs");
        string repoOwner = Path.Combine("src", srcOwner);

        var offenders = new List<string>();

        // (a) Üretim KODU — src ağacı (testler hariç: onlar bu literalleri anlatmak için kullanır).
        Assert.Contains(srcOwner, SourceGuard.ScannedSrcFiles("*.cs")); // tarama dosya görüyor mu
        offenders.AddRange(SourceGuard.ScanSrc("*.cs", rule, [srcOwner], skipCommentLines: true));

        // (b) MSBuild YAPILANDIRMASI — TÜM repo (kökteki Directory.Build.props dahil).
        foreach (string pattern in new[] { "*.csproj", "*.props", "*.targets" })
        {
            var scanned = SourceGuard.ScannedRepoFiles(pattern);
            if (pattern != "*.targets")                                 // *.targets bugün repoda YOK — kol boş olabilir
                Assert.NotEmpty(scanned);                               // …ama csproj/props kolları GERÇEKTEN dosya görmeli
            offenders.AddRange(SourceGuard.ScanRepo(pattern, rule, [repoOwner], skipCommentLines: true));
        }
        Assert.Contains("Directory.Build.props", SourceGuard.ScannedRepoFiles("*.props")); // kök props GERÇEKTEN taranıyor

        Assert.Empty(offenders);
        Assert.Matches(rule, File.ReadAllText(Path.Combine(RepoPaths.SrcRoot, srcOwner))); // muaf dosya GERÇEKTEN eşleşiyor
    }

    /// <summary>
    /// [tek proje · design §3.8] Build yolunun argüman seçimi YALNIZ <see cref="MsBuildArguments.PlanFor"/>'dan
    /// gelir — invoker çalıştırır, seçmez.
    ///
    /// <para>Guard bilinçlidir: invoker'ın gövdesi hedefi kendisi seçmeye kalkarsa satır menüsünün
    /// <b>Rebuild</b>/<b>Clean</b> maddeleri SESSİZCE <c>-t:Build</c> koşar ve hiçbir davranış testi kırmızı
    /// olmaz — tek proje koşusunun iddiaları kaydedilen request + <c>PlanFor</c> üzerinden kurulur, invoker'ın
    /// ürettiği komut satırını gözleyen bir dikiş YOKTUR. Bu boşluk Optimize'ın restore ucu taşınırken
    /// gerçekten açıldı (eski gövde hedefi tanımayan bir çağrı taşıyordu), mutasyonla doğrulandı.</para>
    /// </summary>
    [Fact]
    public void The_invoker_never_picks_the_build_target_itself()
    {
        string text = File.ReadAllText(
            Path.Combine(RepoPaths.SrcRoot, "BuildOrchestrator.Core", "MsBuild", "MsBuildInvoker.cs"));

        Assert.Contains("MsBuildArguments.PlanFor(", text);      // build yolu tek kaynaktan geçiyor
        Assert.DoesNotContain("MsBuildArguments.Build(", text);  // ...ve hedefi kendisi seçmiyor
    }

    /// <summary>
    /// [VS-parity] Proje daima kendi varsayılan obj'inde derlenir: argüman listesi obj'i hiçbir yere
    /// yönlendirmez.
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-1]</b> Eski kural iki testle pinliydi: "obj izolasyonu
    /// verilirse <c>BaseIntermediateOutputPath</c> sonda ters bölüyle yazılır" ve "verilmezse yazılmaz"
    /// (<c>Build_with_obj_isolation_has_trailing_backslash</c>,
    /// <c>Build_with_null_obj_isolation_emits_no_BaseIntermediateOutputPath_arg</c>). İzolasyon yalnız worktree
    /// havuzu içindi; worktree modu kalkınca parametre de kalktı — geriye yalnız "yönlendirme yok" kalır.</para>
    /// </summary>
    [Fact]
    public void Build_never_redirects_the_obj_folder()
    {
        var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug");
        Assert.DoesNotContain(args, a => a.Contains("IntermediateOutputPath", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Restore_requires_solutionDir_with_trailing_backslash_and_no_nuget_exe() // [SPIKE S2 şart-1 + S1]
    {
        var args = MsBuildArguments.RestorePackagesConfig(@"c:\r\p.csproj", @"c:\r\slnDir");
        Assert.Contains("-t:restore", args);
        Assert.Contains("-p:RestorePackagesConfig=true", args);
        Assert.Contains(@"-p:SolutionDir=c:\r\slnDir\", args);
        Assert.DoesNotContain(args, a => a.Contains("nuget", StringComparison.OrdinalIgnoreCase)); // nuget.exe bağımlılığı YOK
    }

    [Theory]
    [InlineData(@"c:\x", @"c:\x\")] [InlineData(@"c:\x\", @"c:\x\")] [InlineData("c:/x/", "c:/x/")]
    public void EnsureTrailingBackslash(string input, string expected)
        => Assert.Equal(expected, MsBuildArguments.EnsureTrailingBackslash(input));

    /// <summary>[Faz 2 · WPF geçici assembly] Aşağıdaki testlerin ortak targets yolu (diskte var olması gerekmez).</summary>
    private const string TargetsPath = @"C:\state\msbuild\wpf-temporary-assembly.targets";

    /// <summary>
    /// [Faz 2 · WPF geçici assembly] Build listesi, verilirse WPF geçici assembly targets'ını TEK bir
    /// <c>-p:CustomBeforeMicrosoftCommonTargets=</c> argümanıyla taşır; yol tırnaklanmaz (liste
    /// <c>ArgumentList</c> üzerinden geçer, <c>WindowsCommandLine</c> kaçışlar).
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Eskiden Build listesi YALNIZ sabit bayraklardan oluşurdu; hiçbir çağrı
    /// derleme davranışını bir targets dosyasıyla değiştirmezdi (yukarıdaki testler bayrakların varlığını
    /// <c>Contains</c> ile pinler, listenin tamamını eşitlikle pinleyen test yoktu — o yüzden hiçbiri
    /// gevşetilmedi ya da silinmedi). Artık motor, WPF'in yerel tipli XAML için derlediği geçici assembly'yi
    /// gövdesiz derleyen küçük bir targets dosyasını Build listesine ekler. Gerekçe ölçümdür: WPF geçici assembly
    /// derlemesi −%18…−24, BAML ve DLL çıktısı eşdeğer. Sabit bayrakların hiçbiri DEĞİŞMEZ.</para>
    /// </summary>
    [Fact]
    public void the_build_list_carries_the_wpf_temporary_assembly_targets_when_given()
    {
        var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", customBeforeTargets: TargetsPath);

        Assert.Contains("-p:CustomBeforeMicrosoftCommonTargets=" + TargetsPath, args);
        Assert.Equal(1, args.Count(a => a.StartsWith("-p:CustomBeforeMicrosoftCommonTargets=", StringComparison.Ordinal)));
    }

    /// <summary>
    /// Karar 9: targets argümanı Build, Rebuild ve Clean'in ÜÇÜNDE de girer — Clean'de geçici proje doğmaz, zararsızdır
    /// ve sözleşme hedef başına ayrı bir liste olmadan tek kalır.
    /// </summary>
    [Theory]
    [InlineData(MsBuildTarget.Build)]
    [InlineData(MsBuildTarget.Rebuild)]
    [InlineData(MsBuildTarget.Clean)]
    public void every_build_target_carries_the_wpf_temporary_assembly_targets(MsBuildTarget target)
        => Assert.Contains("-p:CustomBeforeMicrosoftCommonTargets=" + TargetsPath,
            MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", target, TargetsPath));

    /// <summary>
    /// Build listesi SALT-OKUNUR döner (targets argümanı eklenmeden önce de böyleydi): argüman sözleşmesini çağıran
    /// yerinde değiştiremez.
    /// </summary>
    [Fact]
    public void the_build_list_is_read_only()
        => Assert.Throws<NotSupportedException>(() =>
            ((IList<string>)MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", customBeforeTargets: TargetsPath)).Add("-x"));

    /// <summary>
    /// Boşluklu bir kullanıcı profili yolu (<c>C:\Users\Ad Soyad\...</c>) komut satırına TEK tırnaklı token olarak
    /// çıkar: MSBuild <c>-p:Ad=Değer</c> değerini bölünmeden alır.
    /// </summary>
    [Fact]
    public void a_targets_path_with_a_space_is_one_quoted_token_on_the_command_line()
    {
        const string spaced = @"C:\Users\Ad Soyad\AppData\Local\BuildOrchestrator\msbuild\wpf-temporary-assembly.targets";

        string line = BuildOrchestrator.Core.Processes.WindowsCommandLine.Build(@"C:\msbuild\MSBuild.exe",
            [.. MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", customBeforeTargets: spaced)]);

        Assert.Contains("\"-p:CustomBeforeMicrosoftCommonTargets=" + spaced + "\"", line);
    }

    /// <summary>Yol verilmezse liste targets'sız hâliyle aynıdır: yalıtılmış/sahte motorlar ve targets'ı
    /// yazılamayan motor derlemeyi bugünkü komut satırıyla sürdürür.</summary>
    [Fact]
    public void without_a_targets_path_the_list_is_unchanged()
        => Assert.DoesNotContain(MsBuildArguments.Build(@"c:\r\p.csproj", "Debug"),
            a => a.Contains("CustomBeforeMicrosoftCommonTargets"));

    /// <summary>Restore derlemez, geçici WPF assembly'si orada doğmaz: targets restore listesine HİÇ girmez
    /// (yalnız Build listesine — bkz. <see cref="MsBuildArguments.PlanFor"/>).</summary>
    [Fact]
    public void restore_never_carries_the_targets()
        => Assert.DoesNotContain(MsBuildArguments.RestorePackagesConfig(@"c:\r\p.csproj", @"c:\r\"),
            a => a.Contains("CustomBeforeMicrosoftCommonTargets"));

    /// <summary>
    /// [Faz 2 · WPF geçici assembly] Argüman seçiminin TEK kaynağı <see cref="MsBuildArguments.PlanFor"/>'dur:
    /// istekteki targets yolu Build listesine girer, aynı istekten çıkan Restore listesine girmez.
    /// </summary>
    [Fact]
    public void the_plan_gives_the_targets_to_the_build_list_only()
    {
        var request = new MsBuildInvokeRequest(@"c:\r\p.csproj", "Debug", @"c:\r\", NeedsRestore: true,
            CustomBeforeTargets: TargetsPath);

        var (restore, build) = MsBuildArguments.PlanFor(request);

        Assert.Contains("-p:CustomBeforeMicrosoftCommonTargets=" + TargetsPath, build);
        Assert.NotNull(restore); // NeedsRestore: Restore listesi var — ve targets'sız
        Assert.DoesNotContain(restore!, a => a.Contains("CustomBeforeMicrosoftCommonTargets"));
    }

    /// <summary>
    /// [Faz 2 · WPF geçici assembly] Koordinatör komut satırının argümanlarını KENDİSİ seçmez: proje logunun ilk satırı
    /// invoker'ın koşturduğu listenin AYNISI olsun diye liste <see cref="MsBuildArguments.PlanFor"/>'dan gelir,
    /// <c>Build</c>/<c>RestorePackagesConfig</c> doğrudan çağrılmaz (ikinci bir seçim yeri, yeni bir argümanda log ile
    /// gerçek komutu sessizce ayrıştırırdı).
    ///
    /// <para>Guard bilinçlidir ve <see cref="The_invoker_never_picks_the_build_target_itself"/> ile aynı kalıptadır.
    /// Targets yolunun koşudan isteğe ve log satırına TAŞINMASI ise metin taramasıyla değil davranışla pinlidir
    /// (<c>RunCoordinatorTests.the_toolset_targets_path_reaches_every_request_and_the_first_log_line</c>).</para>
    /// </summary>
    [Fact]
    public void The_run_coordinator_never_picks_the_command_line_arguments_itself()
    {
        string text = File.ReadAllText(
            Path.Combine(RepoPaths.SrcRoot, "BuildOrchestrator.Supervisor", "RunCoordinator.cs"));

        Assert.Contains("MsBuildArguments.PlanFor(", text);                     // log satırı tek kaynaktan
        Assert.DoesNotContain("MsBuildArguments.Build(", text);                 // ...build listesini kendisi seçmiyor
        Assert.DoesNotContain("MsBuildArguments.RestorePackagesConfig(", text); // ...restore listesini de
    }
}
