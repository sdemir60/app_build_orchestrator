using System.IO;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using Xunit;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [Faz 3/Task 3 — spec 2026-09-18 §5] Çıktı kanıtı ve iki kip: derleme çıktısı aracın mı (defter kipi) yoksa
/// başkasının mı (zaman kipi), ve zaman kipinde kanıt taze mi. Her test spec §7'deki bir kaçak satırını adıyla
/// taşır. Zamanlar gerçek geçici dosyalara AÇIKÇA yazılır (<see cref="File.SetLastWriteTimeUtc"/>) — sleep yok,
/// duvar saatine bağlı eşik yok (D8).
///
/// <para><b>Düzen.</b> Proje <c>P</c>: tek girdi dosyası <c>P\P.cs</c>, taranan klasör <c>P</c>, derleme kanıtı
/// <c>P\bin\Debug\P.dll</c>, havuzdaki beslenen kopya <c>lib\P.dll</c>, bağımlılık çıktısı (HintPath hedefi)
/// <c>lib\D.dll</c>. Aracın son koşusu <see cref="ToolRun"/>'dadır; aracın kendi çıktısı her zaman ondan
/// eskidir (§5.2).</para>
/// </summary>
public sealed class OutputEvidenceTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ToolBuilt = T0.AddMinutes(10);
    private static readonly DateTime ToolRun = T0.AddMinutes(11);
    private static readonly DateTime Later = T0.AddMinutes(20);
    private static readonly DateTime ElsewhereBuilt = T0.AddMinutes(30);
    private static readonly DateTime Latest = T0.AddMinutes(40);
    private const int Size = 10;

    private const string InputRel = @"P\P.cs";
    private const string FolderRel = "P";
    private const string EvidenceRel = @"P\bin\Debug\P.dll";
    private const string FedRel = @"lib\P.dll";
    private const string HintRel = @"lib\D.dll";

    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private string Full(string relative) => Path.Combine(_dir.Path, relative);

    /// <summary>Dosyayı <paramref name="size"/> baytla yazar ve zamanını açıkça verir.</summary>
    private string Touch(string relative, DateTime at, int size = Size)
    {
        string full = Full(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[size]);
        File.SetLastWriteTimeUtc(full, at);
        return full;
    }

    /// <summary>Klasör zamanını açıkça verir — dosya OLUŞTURMAK klasör zamanını ilerletir, bu yüzden en sonda.</summary>
    private string Stamp(string relative, DateTime at)
    {
        string full = Full(relative);
        Directory.CreateDirectory(full);
        Directory.SetLastWriteTimeUtc(full, at);
        return full;
    }

    private ProjectOutputs Outputs() => new(Full(EvidenceRel), [Full(FedRel)]);

    private BuildState Record(DateTime? lastRun, BuildResult result = BuildResult.Succeeded, string? fed = null) =>
        new(Full(@"P\P.csproj"), "sig", LastResult: result,
            LastRunAt: lastRun is null ? null : new DateTimeOffset(lastRun.Value),
            FedOutputs: fed is null ? null : [Full(fed)]);

    /// <summary>P'nin kontrolü: girdi <c>P\P.cs</c>, klasör <c>P</c> (zamanı <paramref name="folderAt"/>, varsayılan
    /// <see cref="T0"/>), HintPath hedefleri <paramref name="hints"/>.</summary>
    private OutputCheck Check(BuildState? state, DateTime? folderAt = null, params string[] hints)
    {
        string folder = Stamp(FolderRel, folderAt ?? T0);
        return OutputEvidence.Inspect(Outputs(), state, [Full(InputRel)], [folder], [.. hints.Select(Full)]);
    }

    private static DateTimeOffset At(DateTime utc) => new(utc);

    // ------------------------------------------------------------------ Inspect / TimeCheck

    /// <summary>§7-1: araçla derledim, dokunmadım — kanıt <c>LastRunAt</c>'tan eski ⇒ defter kipi; zaman
    /// hükmü yok, cevap defterden (imza).</summary>
    [Fact]
    public void Built_by_the_tool_and_untouched_is_ledger_mode()
    {
        Touch(InputRel, T0);
        Touch(EvidenceRel, ToolBuilt);
        Touch(FedRel, ToolBuilt);

        var check = Check(Record(ToolRun, fed: FedRel));

        Assert.Equal(new OutputCheck(EvidenceMode.Ledger, false, true, null, At(ToolBuilt)), check);
        Assert.False(OutputEvidence.OwnFilesChanged(check, ledgerAnswer: false));
        Assert.True(OutputEvidence.OwnFilesChanged(check, ledgerAnswer: true));
        Assert.Null(OutputEvidence.OutputBuiltAt(check));
    }

    /// <summary>§7-3, §7-4: değiştirip (ya da değiştirmeden) VS'de derledim — kanıt defterden ve girdilerden yeni,
    /// havuz kopyası da yeni ⇒ zaman kipi, taze; "built outside" yaşı kanıtın zamanından.</summary>
    [Fact]
    public void Built_elsewhere_after_the_tool_is_time_mode_and_fresh()
    {
        Touch(InputRel, Later);
        Touch(EvidenceRel, ElsewhereBuilt);
        Touch(FedRel, ElsewhereBuilt);

        var check = Check(Record(ToolRun, fed: FedRel), folderAt: Later);

        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, true, TimeVerdict.Fresh, At(ElsewhereBuilt)), check);
        Assert.Equal(At(ElsewhereBuilt), OutputEvidence.OutputBuiltAt(check));
        // Zaman kipinde "kendi dosyası değişti mi" kanıttan gelir — defterin cevabı okunmaz.
        Assert.False(OutputEvidence.OwnFilesChanged(check, ledgerAnswer: true));
    }

    /// <summary>§7-5: değiştirdim, VS'de derledim, geri aldım, derlemedim — geri alınan dosya çıktıdan yeni ⇒
    /// zaman kipi, <c>OwnNewer</c> (<c>modified</c>).</summary>
    [Fact]
    public void A_reverted_file_newer_than_the_output_is_own_newer()
    {
        Touch(InputRel, Latest);
        Touch(EvidenceRel, ElsewhereBuilt);

        var check = Check(Record(ToolRun));

        Assert.Equal(EvidenceMode.Time, check.Mode);
        Assert.Equal(TimeVerdict.OwnNewer, check.Time);
        Assert.True(OutputEvidence.OwnFilesChanged(check, ledgerAnswer: false));
        Assert.Null(OutputEvidence.OutputBuiltAt(check));
    }

    /// <summary>§7-15: VS'de derleme patladı — csc çıktı üretmez, eski çıktı değişen girdiden eski kalır ⇒
    /// <c>OwnNewer</c>. (Kayıt yok: VS'nin önceki başarılı çıktısı.)</summary>
    [Fact]
    public void A_failed_build_elsewhere_leaves_the_old_output_stale()
    {
        Touch(EvidenceRel, ToolBuilt);
        Touch(InputRel, Later);

        var check = Check(state: null);

        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, true, TimeVerdict.OwnNewer, At(ToolBuilt)), check);
    }

    /// <summary>§7-16: VS'de derleme geçti, havuza kopya patladı — beslenen kopya derleme kanıtından eski ⇒
    /// zaman kipi, <c>FedBroken</c> (<c>affected</c>). Kopya hiç yoksa da bozuktur.</summary>
    [Fact]
    public void A_failed_copy_to_the_shared_folder_breaks_the_fed_output()
    {
        Touch(InputRel, T0);
        Touch(FedRel, ToolBuilt);
        Touch(EvidenceRel, ElsewhereBuilt);

        var stale = Check(Record(ToolRun, fed: FedRel));

        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, false, TimeVerdict.FedBroken, At(ElsewhereBuilt)), stale);

        File.Delete(Full(FedRel));
        var missing = Check(Record(ToolRun, fed: FedRel));

        Assert.False(missing.FedIntact);
        Assert.Equal(TimeVerdict.FedBroken, missing.Time);
    }

    /// <summary>§7-17, §7-18: araç Debug derledi, VS Release derleyip havuza kopyaladı — <c>bin\Debug</c> kanıtı
    /// <c>LastRunAt</c>'tan eski (defter kipi) ama havuzdaki kopyanın boyutu farklı ⇒ beslenen çıktı bozuk.
    /// Karşıt: boyutu aynı ve zamanı YENİ bir kopya bozuk sayılmaz.</summary>
    [Fact]
    public void A_release_build_in_the_shared_folder_breaks_the_fed_output()
    {
        Touch(InputRel, T0);
        Touch(EvidenceRel, ToolBuilt);
        Touch(FedRel, ElsewhereBuilt, size: Size + 3);

        var replaced = Check(Record(ToolRun, fed: FedRel));

        Assert.Equal(new OutputCheck(EvidenceMode.Ledger, false, false, null, At(ToolBuilt)), replaced);

        Touch(FedRel, ElsewhereBuilt);
        var sameSizeNewer = Check(Record(ToolRun, fed: FedRel));

        Assert.True(sameSizeNewer.FedIntact);
    }

    /// <summary>§7-21, §7-22: yeni <c>.cs</c> eklendi ya da silindi — taranan klasörün zamanı ilerler; girdi
    /// dosyalarının hiçbiri çıktıdan yeni olmasa da zaman kipinde <c>OwnNewer</c>.</summary>
    [Fact]
    public void A_new_file_touches_the_folder()
    {
        Touch(InputRel, T0);
        Touch(EvidenceRel, ToolBuilt);

        var check = Check(state: null, folderAt: Later);

        Assert.Equal(TimeVerdict.OwnNewer, check.Time);
    }

    /// <summary>§7-23 (zaman kipi): bağımlılık D değişti ve derlendi, P'ye dokunulmadı — D'nin HintPath hedefi
    /// P'nin çıktısından yeni ⇒ <c>DependencyNewer</c> (<c>affected</c>, kendi dosyaları değişmedi). Olmayan
    /// HintPath hedefi yok sayılır.</summary>
    [Fact]
    public void A_newer_dependency_output_is_dependency_newer()
    {
        Touch(InputRel, T0);
        Touch(EvidenceRel, ToolBuilt);
        Touch(HintRel, Later);

        var check = Check(state: null, folderAt: null, HintRel, @"lib\Missing.dll");

        Assert.Equal(TimeVerdict.DependencyNewer, check.Time);
        Assert.False(OutputEvidence.OwnFilesChanged(check, ledgerAnswer: true));
    }

    /// <summary>§7-32, §7-33: motor derlerken çöktü / zaman aşımı — kurtarma <c>LastResult=Failed,
    /// LastRunAt=şimdi</c> yazar; yarım yazılmış çıktı ondan eski (ya da eşit) kalır ⇒ zaman kipine GİREMEZ,
    /// defter kipi.</summary>
    [Fact]
    public void An_output_written_before_the_crash_recovery_stays_ledger_mode()
    {
        Touch(InputRel, T0);
        Touch(EvidenceRel, ToolBuilt);

        Assert.Equal(EvidenceMode.Ledger, Check(Record(ToolRun, BuildResult.Failed)).Mode);
        // "Yeni" kesin büyüktür: LastRunAt'a eşit kanıt aracındır.
        Assert.Equal(EvidenceMode.Ledger, Check(Record(ToolBuilt, BuildResult.Failed)).Mode);
    }

    /// <summary>§7-39, §7-40: araç ilk kez kuruldu ya da defter silindi — kayıt yok ⇒ zaman kipi; VS'nin
    /// çıktısı taze ⇒ yeşil.</summary>
    [Fact]
    public void No_record_is_time_mode()
    {
        Touch(InputRel, T0);
        Touch(EvidenceRel, ToolBuilt);

        var check = Check(state: null);

        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, true, TimeVerdict.Fresh, At(ToolBuilt)), check);
    }

    /// <summary>§5.3/§5.4: derleme kanıtı yok — defter kipinde <c>EvidenceMissing</c> (never built); zaman
    /// kipinde (kayıt yok) hüküm <c>Missing</c>. Beslenen kopya denetimi anlamsızdır, sağlam raporlanır.</summary>
    [Fact]
    public void A_missing_output_under_a_record_is_missing()
    {
        Touch(InputRel, T0);
        Touch(FedRel, ToolBuilt);

        var ledger = Check(Record(ToolRun, fed: FedRel));
        var time = Check(state: null);

        Assert.Equal(new OutputCheck(EvidenceMode.Ledger, true, true, null, null), ledger);
        Assert.Equal(new OutputCheck(EvidenceMode.Time, true, true, TimeVerdict.Missing, null), time);
    }

    /// <summary>§5.1: kanıt yolu türetilemiyor (SDK-style) ⇒ kanıtsız: hiçbir veto yok, cevap defterden.</summary>
    [Fact]
    public void An_unknown_evidence_path_is_mode_none()
    {
        var sdk = new EvaluatedProject(Full(@"P\P.csproj"), "P", [], [], [], IsSdkStyle: true) { OutputType = "Library" };
        var outputs = OutputEvidence.Locate(sdk, "Debug", []);

        var check = OutputEvidence.Inspect(outputs, state: null, [], [], []);

        Assert.Null(outputs);
        Assert.Equal(new OutputCheck(EvidenceMode.None, false, true, null, null), check);
        Assert.True(OutputEvidence.OwnFilesChanged(check, ledgerAnswer: true));
        Assert.Null(OutputEvidence.OwnFilesChanged(check, ledgerAnswer: null));
        Assert.Null(OutputEvidence.OutputBuiltAt(check));
    }

    /// <summary>§5.2: kaydın <c>LastRunAt</c>'ı yok ⇒ aracın derlediği bilinmiyor ⇒ zaman kipi.</summary>
    [Fact]
    public void A_null_last_run_is_time_mode()
    {
        Touch(InputRel, T0);
        Touch(EvidenceRel, ToolBuilt);

        var check = Check(Record(lastRun: null));

        Assert.Equal(EvidenceMode.Time, check.Mode);
        Assert.Equal(TimeVerdict.Fresh, check.Time);
    }

    /// <summary>§5.4: "yeni" kesin büyüktür — girdi dosyası, klasör, HintPath hedefi ve beslenen kopya kanıta
    /// EŞİT zamandaysa kanıt tazedir.</summary>
    [Fact]
    public void Equal_times_are_fresh()
    {
        Touch(InputRel, ToolBuilt);
        Touch(EvidenceRel, ToolBuilt);
        Touch(FedRel, ToolBuilt);
        Touch(HintRel, ToolBuilt);

        var check = Check(Record(lastRun: null, fed: FedRel), folderAt: ToolBuilt, HintRel);

        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, true, TimeVerdict.Fresh, At(ToolBuilt)), check);
    }

    // ------------------------------------------------------------------ ApplyCycleGroups

    /// <summary>Döngü üyesi <paramref name="name"/>: girdi <c>{name}\{name}.cs</c>, klasör, kanıt
    /// <c>{name}\bin\Debug\{name}.dll</c> — hepsi verilen zamanlarla.</summary>
    private (string Id, Func<BuildState?, OutputCheck> Inspect, Func<OutputCheck> Time) Member(
        string name, DateTime inputAt, DateTime evidenceAt)
    {
        string input = Touch($@"{name}\{name}.cs", inputAt);
        var outputs = new ProjectOutputs(Touch($@"{name}\bin\Debug\{name}.dll", evidenceAt), []);
        string folder = Stamp(name, inputAt);
        return (name,
            state => OutputEvidence.Inspect(outputs, state, [input], [folder], []),
            () => OutputEvidence.TimeCheck(outputs, Record(ToolRun), [input], [folder], []));
    }

    /// <summary>§7-43 (§5.6): döngü üyesi B VS'de tek başına derlendi, A derlenmedi — B zaman kipinde, dolayısıyla
    /// grup zaman kipinde; A kendi hükmünü (<c>OwnNewer</c>) alır, kendi kontrolünü geçen B ise
    /// <c>DependencyNewer</c> — grup bayat kalır. Döngü dışı giriş dokunulmadan geçer.</summary>
    [Fact]
    public void One_member_built_elsewhere_puts_the_group_in_time_mode()
    {
        var a = Member("A", inputAt: Later, evidenceAt: ToolBuilt);
        var b = Member("B", inputAt: Later, evidenceAt: ElsewhereBuilt);
        var outside = new OutputCheck(EvidenceMode.Ledger, false, true, null, At(ToolBuilt));
        var checks = new Dictionary<string, OutputCheck>
        {
            ["A"] = a.Inspect(Record(ToolRun)),
            ["B"] = b.Inspect(Record(ToolRun)),
            ["C"] = outside,
        };
        var timeOf = new Dictionary<string, Func<OutputCheck>> { ["A"] = a.Time, ["B"] = b.Time };

        Assert.Equal(EvidenceMode.Ledger, checks["A"].Mode);
        Assert.Equal(TimeVerdict.Fresh, checks["B"].Time);

        var grouped = OutputEvidence.ApplyCycleGroups(checks, [["A", "B"]], id => timeOf[id]());

        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, true, TimeVerdict.OwnNewer, At(ToolBuilt)), grouped["A"]);
        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, true, TimeVerdict.DependencyNewer, At(ElsewhereBuilt)), grouped["B"]);
        Assert.Same(outside, grouped["C"]);
    }

    /// <summary>§5.6: grubun her üyesinin kanıtı tazeyse grup tazedir — hepsi kendi (taze) zaman kontrolünü alır.</summary>
    [Fact]
    public void A_fully_fresh_group_is_fresh()
    {
        var a = Member("A", inputAt: T0, evidenceAt: ElsewhereBuilt);
        var b = Member("B", inputAt: T0, evidenceAt: ToolBuilt);
        var checks = new Dictionary<string, OutputCheck>
        {
            ["A"] = a.Inspect(Record(ToolRun)),
            ["B"] = b.Inspect(Record(ToolRun)),
        };
        var timeOf = new Dictionary<string, Func<OutputCheck>> { ["A"] = a.Time, ["B"] = b.Time };

        Assert.Equal(EvidenceMode.Ledger, checks["B"].Mode);

        var grouped = OutputEvidence.ApplyCycleGroups(checks, [["A", "B"]], id => timeOf[id]());

        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, true, TimeVerdict.Fresh, At(ElsewhereBuilt)), grouped["A"]);
        Assert.Equal(new OutputCheck(EvidenceMode.Time, false, true, TimeVerdict.Fresh, At(ToolBuilt)), grouped["B"]);
    }

    /// <summary>§5.6: grubun hiçbir üyesi zaman kipinde değilse üyeler tek tek defter kipinde kalır — zaman
    /// kontrolü hiç çağrılmaz (maliyet: defter kipinde girdi zamanı okunmaz).</summary>
    [Fact]
    public void A_group_with_no_time_member_is_left_alone()
    {
        var a = Member("A", inputAt: Later, evidenceAt: ToolBuilt);
        var b = Member("B", inputAt: T0, evidenceAt: ToolBuilt);
        var checks = new Dictionary<string, OutputCheck>
        {
            ["A"] = a.Inspect(Record(ToolRun)),
            ["B"] = b.Inspect(Record(ToolRun)),
        };

        var grouped = OutputEvidence.ApplyCycleGroups(
            checks, [["A", "B"]], id => throw new InvalidOperationException("time check must not run: " + id));

        Assert.Equal(EvidenceMode.Ledger, checks["A"].Mode);
        Assert.Same(checks["A"], grouped["A"]);
        Assert.Same(checks["B"], grouped["B"]);
    }

    // ------------------------------------------------------------------ Locate

    private EvaluatedProject Project(string name, params string[] hintPaths) =>
        new(Full($@"{name}\{name}.csproj"), name, [],
            [.. hintPaths.Select(h => new RawHintPath(h, Path.GetFileNameWithoutExtension(h)))], [], IsSdkStyle: false)
        { OutputType = "Library" };

    /// <summary>§5.1: beslenen çıktı adayları = bağımlıların, dosya adı derleme kanıtınınkiyle aynı (büyük/küçük
    /// harf duyarsız) HintPath hedefleri; tekil ve sıralı.</summary>
    [Fact]
    public void Candidates_come_from_dependents_hint_paths_with_the_same_file_name()
    {
        var p = Project("P");
        var q = Project("Q", @"..\lib\P.dll", @"..\lib\Other.dll");
        var r = Project("R", @"..\lib\p.DLL", @"..\shared\P.dll");

        var outputs = OutputEvidence.Locate(p, "Debug", [q, r]);

        Assert.NotNull(outputs);
        Assert.Equal(Full(EvidenceRel), outputs.Evidence);
        Assert.Equal(new[] { Full(FedRel), Full(@"shared\P.dll") }, outputs.FedCandidates);
    }

    /// <summary>§7-44: aynı DLL adını iki proje üretiyor — graf kenarı düşer, dolayısıyla bağımlı yoktur ve aday
    /// da yoktur: yalnız derleme kanıtı konuşur.</summary>
    [Fact]
    public void An_ambiguous_producer_has_no_candidates()
    {
        var outputs = OutputEvidence.Locate(Project("P"), "Debug", []);

        Assert.NotNull(outputs);
        Assert.Equal(Full(EvidenceRel), outputs.Evidence);
        Assert.Empty(outputs.FedCandidates);
        Assert.Null(OutputEvidence.Locate(null, "Debug", []));
    }

    // ------------------------------------------------------------------ LearnFedOutputs

    /// <summary>§5.1: başarılı derlemeden sonra aday var, boyutu kanıta eşit ve zamanı ondan en çok
    /// <see cref="OutputEvidence.FedOutputWindow"/> farklı ⇒ öğrenilir (sınır dahil, iki yönde).</summary>
    [Fact]
    public void Same_size_and_time_within_two_seconds_is_learned()
    {
        string evidence = Touch(EvidenceRel, ToolBuilt);
        string after = Touch(FedRel, ToolBuilt + OutputEvidence.FedOutputWindow);
        string before = Touch(@"lib2\P.dll", ToolBuilt - OutputEvidence.FedOutputWindow);

        var learned = OutputEvidence.LearnFedOutputs(new ProjectOutputs(evidence, [after, before]));

        Assert.Equal(new[] { after, before }, learned);
    }

    /// <summary>§5.1: boyutu farklı, zamanı pencere dışında ya da hiç olmayan aday öğrenilmez — kanıt varken
    /// sonuç boş listedir (null değil).</summary>
    [Fact]
    public void A_different_size_or_time_is_not_learned()
    {
        string evidence = Touch(EvidenceRel, ToolBuilt);
        string otherSize = Touch(FedRel, ToolBuilt, size: Size + 1);
        string outside = Touch(@"lib2\P.dll", ToolBuilt + OutputEvidence.FedOutputWindow + TimeSpan.FromSeconds(1));
        string checkedIn = Touch(@"lib3\P.dll", ToolBuilt - OutputEvidence.FedOutputWindow - TimeSpan.FromSeconds(1));

        var learned = OutputEvidence.LearnFedOutputs(
            new ProjectOutputs(evidence, [otherSize, outside, checkedIn, Full(@"lib4\P.dll")]));

        Assert.NotNull(learned);
        Assert.Empty(learned);
    }

    /// <summary>§5.1: derleme kanıtı yoksa (ya da yolu türetilemiyorsa) hiçbir şey öğrenilmez — liste null.</summary>
    [Fact]
    public void No_evidence_learns_nothing()
    {
        string fed = Touch(FedRel, ToolBuilt);

        Assert.Null(OutputEvidence.LearnFedOutputs(new ProjectOutputs(Full(EvidenceRel), [fed])));
        Assert.Null(OutputEvidence.LearnFedOutputs(null));
    }
}
