namespace BuildOrchestrator.Tests.Planning;

using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using Xunit;

// [RESOLVE Faz 3 / Task 3.3] Tur 1 üye gereklilik kararı (karar 2). Yanlış "gerekmez" derlenmesi gereken üyeyi
// atlar ve eski çıktı yayar; bu yüzden eksik, boş ya da şüpheli her kanıt "gerekli" yönünde sınanır ve kuralın
// her dalı kendi testiyle pinlenir.
public class CycleMemberNeedTests
{
    private const string Engine = "engine-1";
    private const string B1 = @"X:\bin\B1.dll";
    private const string B2 = @"X:\bin\B2.dll";

    // Kanıtı tam ve sağlam çıktı: defter kipi, derleme kanıtı yerinde, beslenen kopyalar sağlam.
    private static readonly OutputCheck Intact =
        new(EvidenceMode.Ledger, EvidenceMissing: false, FedIntact: true, Time: null, EvidenceAt: null);

    private static CycleReadSurface Read(string producer, string file, string hash) => new(producer, file, hash);

    // Tipik üye B'nin iki dosyasını okur (kanonik sıra: önce üretici, sonra dosya).
    private static readonly CycleReadSurface[] Reads = [Read("B", B1, "h1"), Read("B", B2, "h2")];

    // Defterdeki güvenilir kayıt: Succeeded, üç döngü alanı dolu, motor parmak izi bu koşununkiyle aynı.
    private static BuildState Ledger(string term, params CycleReadSurface[] reads) => new(
        ProjectId: "A", BuiltSignature: "composite", LastResult: BuildResult.Succeeded,
        CycleMemberTerm: term, CycleReadSurfaces: reads, CycleEngineFingerprint: Engine);

    // Değişmemiş üye: kayıttaki terim bugünkü terimle aynı. Fark `with` ile açılır (CurrentTerm, Record, Output).
    private static CycleMemberNeed.MemberEvidence Member(string term, CycleReadSurface[] surfaces, OutputCheck? output) =>
        new(Ledger(term, surfaces), term, output);

    // Grup başında diskten okunan yüzeyler: üretici → dosya → özet.
    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Disk(params CycleReadSurface[] surfaces) =>
        surfaces.GroupBy(s => s.Producer, StringComparer.OrdinalIgnoreCase).ToDictionary(
            group => group.Key,
            group => (IReadOnlyDictionary<string, string>)group.ToDictionary(
                s => s.File, s => s.Hash, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

    // Üyeleri verilen sırayla (build order) karara sokar; Decide'a her çağrı buradan geçer.
    private static CycleMemberNeed.Decision Decide(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> disk,
        params (string Id, CycleMemberNeed.MemberEvidence Evidence)[] members)
    {
        var byId = members.ToDictionary(m => m.Id, m => m.Evidence, StringComparer.OrdinalIgnoreCase);
        return CycleMemberNeed.Decide([.. members.Select(m => m.Id)], id => byId[id], disk, Engine);
    }

    // Gerekli üye: derleme listesinde, nedeni yazılı, taşınanlar arasında DEĞİL.
    private static void AssertNeeded(CycleMemberNeed.Decision decision, string id, string reason)
    {
        Assert.Contains(id, decision.ToBuild);
        Assert.Equal(reason, decision.Reasons[id]);
        Assert.DoesNotContain(id, decision.CarriedReadStates.Keys);
    }

    // Taşınan üye: derleme listesinde ve nedenler arasında YOK, okuma durumu var.
    private static void AssertCarried(CycleMemberNeed.Decision decision, string id)
    {
        Assert.DoesNotContain(id, decision.ToBuild);
        Assert.DoesNotContain(id, decision.Reasons.Keys);
        Assert.Contains(id, decision.CarriedReadStates.Keys);
    }

    // ---------------------------------------------------------------- taşınan üye

    [Fact]
    public void a_member_whose_term_is_unchanged_and_surfaces_match_is_carried()
    {
        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, Intact)));

        AssertCarried(decision, "A");
        Assert.Empty(decision.ToBuild);
        Assert.Empty(decision.Reasons);
        // taşınan üyenin okuma durumu kayıttan kurulur: üretici → dosya → özet
        var read = Assert.Single(decision.CarriedReadStates["A"]);
        Assert.Equal("B", read.Key);
        Assert.Equal(2, read.Value.Count);
        Assert.Equal("h1", read.Value[B1]);
        Assert.Equal("h2", read.Value[B2]);
    }

    [Fact] // Yalnız KAYITTAKİ dosyalara bakılır: üyenin okumadığı bir kopya ya da üretici oynasa da üye bayat olmaz.
    public void a_surface_the_member_never_read_does_not_make_it_needed()
    {
        var disk = Disk(Read("B", B1, "h1"), Read("B", B2, "h2"),
                        Read("B", @"X:\bin\B3.dll", "never-read-moved"), Read("C", @"X:\bin\C.dll", "other-producer"));

        var decision = Decide(disk, ("A", Member("t1", Reads, Intact)));

        AssertCarried(decision, "A");
    }

    [Fact] // null ≠ boş: boş liste "hiçbir kardeş yüzeyi okumadı" demektir ve GÜVENİLİR kanıttır.
    public void an_empty_surface_record_is_trusted_evidence_of_reading_nothing()
    {
        var decision = Decide(Disk(), ("A", Member("t1", [], Intact)));

        AssertCarried(decision, "A");
        Assert.Empty(decision.CarriedReadStates["A"]);
    }

    // ---------------------------------------------------------------- (i) kendi girdisi

    [Fact]
    public void a_changed_term_makes_the_member_needed()
    {
        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, Intact) with { CurrentTerm = "t2" }));

        AssertNeeded(decision, "A", "own inputs changed");
    }

    // Fast kipi: planlayıcı bileşik kurmaz, MemberTermById boştur ⇒ bugünkü terim yok. Kayıt tam ve terimi dolu;
    // "own inputs changed" demek yanlış olurdu (terim hiç hesaplanmadı). Üye yine de gerekli — güvenli taraf.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void a_member_without_a_current_term_is_needed_for_its_own_reason(string? currentTerm)
    {
        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, Intact) with { CurrentTerm = currentTerm }));

        AssertNeeded(decision, "A", "no member term");
    }

    // ---------------------------------------------------------------- (ii) okunan yüzeyler

    [Fact]
    public void a_moved_read_surface_makes_the_reader_needed()
    {
        var disk = Disk(Read("B", B1, "h1-moved"), Read("B", B2, "h2"));

        var decision = Decide(disk, ("A", Member("t1", Reads, Intact)));

        AssertNeeded(decision, "A", "read surface moved: " + B1);
    }

    [Fact] // Üretici tanınıyor ama kayıttaki dosya artık yok: "farklı" ile aynı yönde — gerekli.
    public void a_recorded_file_no_longer_known_makes_the_reader_needed()
    {
        var decision = Decide(Disk(Read("B", B1, "h1")), ("A", Member("t1", Reads, Intact)));

        AssertNeeded(decision, "A", "read surface moved: " + B2);
    }

    [Fact] // Üreticinin diskten okunmuş hiçbir yüzeyi yok: kayıttaki her dosya taşınmış sayılır.
    public void a_recorded_producer_no_longer_known_makes_the_reader_needed()
    {
        var decision = Decide(Disk(), ("A", Member("t1", Reads, Intact)));

        AssertNeeded(decision, "A", "read surface moved: " + CycleDecisionLines.MovedTerm([B1, B2]));
    }

    [Fact] // Neden satırı tur satırının moved alanıyla AYNI terimi kullanır: ad sayısı sınırlıdır, fazlası sayı olur.
    public void the_moved_surface_reason_names_at_most_the_moved_file_limit()
    {
        string[] files = [.. Enumerable.Range(1, CycleDecisionLines.MovedFileLimit + 1).Select(i => $@"X:\bin\B{i}.dll")];
        var surfaces = files.Select(file => Read("B", file, "h")).ToArray();

        var decision = Decide(Disk(), ("A", Member("t1", surfaces, Intact)));

        AssertNeeded(decision, "A", "read surface moved: " + CycleDecisionLines.MovedTerm(files));
        Assert.DoesNotContain(files[^1], decision.Reasons["A"]);
    }

    [Fact] // Ortak "taşındı" ilkeli: kayıtlı dosya yoksa ya da özeti (büyük/küçük harf DAHİL) farklıysa taşınmıştır.
    public void moved_files_lists_only_recorded_files_that_differ_or_vanished()
    {
        var seen = new Dictionary<string, string>
        {
            [B1] = "h1", [B2] = "h2", [@"X:\bin\B3.dll"] = "h3", [@"X:\bin\B4.dll"] = "h4",
        };
        var now = new Dictionary<string, string>
        {
            [B1] = "h1",                       // aynı
            [B2] = "moved",                    // farklı
            [@"X:\bin\B4.dll"] = "H4",         // yalnız harf farkı: Ordinal ⇒ farklı
            [@"X:\bin\B9.dll"] = "unrelated",  // kayıtta yok ⇒ yok sayılır
        };

        var moved = CycleMemberNeed.MovedFiles(seen, now).Order(StringComparer.Ordinal);

        Assert.Equal(new[] { B2, @"X:\bin\B3.dll", @"X:\bin\B4.dll" }, moved);
    }

    // ---------------------------------------------------------------- (iii) güvenilir kayıt

    [Fact]
    public void a_member_without_a_ledger_record_is_needed()
    {
        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, Intact) with { Record = null }));

        AssertNeeded(decision, "A", "no trusted record");
    }

    [Theory] // Başarısız, atlanmış ya da sonucu hiç yazılmamış kayıt güvenilmez.
    [InlineData(BuildResult.Failed)]
    [InlineData(BuildResult.Skipped)]
    [InlineData(null)]
    public void a_record_whose_last_result_is_not_succeeded_makes_the_member_needed(BuildResult? lastResult)
    {
        var record = Ledger("t1", Reads) with { LastResult = lastResult };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, Intact) with { Record = record }));

        AssertNeeded(decision, "A", "no trusted record");
    }

    [Fact] // Eski defter: üç döngü alanı da yok (null) ⇒ kanıt yok.
    public void a_record_without_cycle_fields_makes_the_member_needed()
    {
        var oldRecord = new BuildState("A", "composite", LastResult: BuildResult.Succeeded);

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, Intact) with { Record = oldRecord }));

        AssertNeeded(decision, "A", "no trusted record");
    }

    [Theory] // Üye terimi kayıtlı değil (null ya da boş): "terim değişti" demek yanlış olurdu, kanıt yok.
    [InlineData(null)]
    [InlineData("")]
    public void a_record_without_a_member_term_makes_the_member_needed(string? recordedTerm)
    {
        var record = Ledger("t1", Reads) with { CycleMemberTerm = recordedTerm };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, Intact) with { Record = record }));

        AssertNeeded(decision, "A", "no trusted record");
    }

    [Fact] // Yüzey kanıtı yok (null): üyenin neyi okuduğu bilinmez.
    public void a_record_without_surface_evidence_makes_the_member_needed()
    {
        var record = Ledger("t1", Reads) with { CycleReadSurfaces = null };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, Intact) with { Record = record }));

        AssertNeeded(decision, "A", "no trusted record");
    }

    [Theory] // Kaydı yazan motor bilinmiyor (null ya da boş): "motor değişti" diyemeyiz, kanıt yok.
    [InlineData(null)]
    [InlineData("")]
    public void a_record_without_an_engine_fingerprint_makes_the_member_needed(string? recordedEngine)
    {
        var record = Ledger("t1", Reads) with { CycleEngineFingerprint = recordedEngine };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, Intact) with { Record = record }));

        AssertNeeded(decision, "A", "no trusted record");
    }

    [Fact] // İki taraf da boşsa "eşit" okunmaz: boş parmak izi hiçbir motoru temsil etmez.
    public void an_empty_engine_fingerprint_never_vouches_for_an_empty_record_fingerprint()
    {
        var member = Member("t1", Reads, Intact) with { Record = Ledger("t1", Reads) with { CycleEngineFingerprint = "" } };

        var decision = CycleMemberNeed.Decide(["A"], _ => member, Disk(Reads), engineFingerprint: "");

        AssertNeeded(decision, "A", "no trusted record");
    }

    [Fact] // Aynı (Producer, File) iki kez: kayıt şüpheli. Her şey diskle eşleşse BİLE üye gerekli.
    public void a_duplicated_producer_file_record_makes_the_member_needed()
    {
        CycleReadSurface[] twice = [Read("B", B1, "h1"), Read("B", B1, "h1")];

        var decision = Decide(Disk(Read("B", B1, "h1")), ("A", Member("t1", twice, Intact)));

        AssertNeeded(decision, "A", "no trusted record");
    }

    [Fact] // Tekrar harf-duyarsız aranır (kanonik sıra OrdinalIgnoreCase) ve çelişen özetle gelse bile ATMAZ.
    public void a_duplicate_that_differs_only_in_case_makes_the_member_needed_without_throwing()
    {
        CycleReadSurface[] twice = [Read("B", B1, "h1"), Read("b", B1.ToLowerInvariant(), "conflicting")];

        var decision = Decide(Disk(Read("B", B1, "h1")), ("A", Member("t1", twice, Intact)));

        AssertNeeded(decision, "A", "no trusted record");
    }

    [Theory] // Bozuk giriş (alanı null): sözlük anahtarı olamaz; çökmek yerine kayıt güvenilmez sayılır.
    [InlineData(null, B1, "h1")]
    [InlineData("B", null, "h1")]
    [InlineData("B", B1, null)]
    public void a_malformed_surface_entry_makes_the_member_needed(string? producer, string? file, string? hash)
    {
        CycleReadSurface[] malformed = [.. Reads, new CycleReadSurface(producer!, file!, hash!)];

        var decision = Decide(Disk(Reads), ("A", Member("t1", malformed, Intact)));

        AssertNeeded(decision, "A", "no trusted record");
    }

    // ---------------------------------------------------------------- (iv)/(v) çıktı

    [Fact] // (iv) Derleme kanıtı (projenin kendi çıktısı) diskte yok — terim aynı olsa da çıktı ortada değil.
    public void missing_output_evidence_makes_the_member_needed()
    {
        var output = Intact with { EvidenceMissing = true };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, output)));

        AssertNeeded(decision, "A", "output evidence missing");
    }

    [Fact] // Çıktı kontrolü hiç yapılmamış (kanıt haritasında kayıt yok) ⇒ kanıt yok.
    public void a_member_without_an_output_check_is_needed()
    {
        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, null)));

        AssertNeeded(decision, "A", "output evidence missing");
    }

    [Fact] // Kanıt yolu türetilemedi (EvidenceMode.None): çıktıya dair hiçbir şey bilinmez.
    public void an_output_without_a_derivable_evidence_path_makes_the_member_needed()
    {
        var output = Intact with { Mode = EvidenceMode.None };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, output)));

        AssertNeeded(decision, "A", "output evidence missing");
    }

    [Fact] // (v) Zaman kipi: çıktı bu araç dışında (Visual Studio, satır menüsü) derlenmiş.
    public void an_output_built_outside_this_tool_makes_the_member_needed()
    {
        var output = Intact with { Mode = EvidenceMode.Time, Time = TimeVerdict.Fresh };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, output)));

        AssertNeeded(decision, "A", "output built outside this tool");
    }

    [Fact] // Beslenen kopyalar (paylaşılan klasördeki DLL) bozuk: bağımlılar başka bir çıktıya link'lenir.
    public void broken_fed_copies_make_the_member_needed()
    {
        var output = Intact with { FedIntact = false };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, output)));

        AssertNeeded(decision, "A", "output evidence missing");
    }

    // ---------------------------------------------------------------- (vi) motor

    [Fact] // Parmak izi farklıysa grupta HERKES gerekli: kayıtlar başka bir motorun (toolset/argümanlar) çıktısı.
    public void a_different_engine_fingerprint_makes_every_member_needed()
    {
        var otherEngine = Member("t1", Reads, Intact)
            with { Record = Ledger("t1", Reads) with { CycleEngineFingerprint = "engine-0" } };

        var decision = Decide(Disk(Reads), ("A", otherEngine), ("C", otherEngine));

        AssertNeeded(decision, "A", "engine changed");
        AssertNeeded(decision, "C", "engine changed");
        Assert.Empty(decision.CarriedReadStates);
    }

    // ---------------------------------------------------------------- sıra ve neden

    [Fact] // İlk eşleşen kural nedeni yazar: kayıt → motor → kendi girdisi → çıktı → okunan yüzey.
    public void the_first_matching_rule_names_the_reason()
    {
        var moved = Disk(Read("B", B1, "moved"), Read("B", B2, "h2"));
        var outside = Intact with { Mode = EvidenceMode.Time, Time = TimeVerdict.OwnNewer };
        string ReasonOf(CycleMemberNeed.MemberEvidence member) => Decide(moved, ("A", member)).Reasons["A"];

        // her şey yanlış ama kayıt güvenilmez ⇒ başka hiçbir neden söylenmez
        var untrusted = Ledger("t1", Reads) with { LastResult = BuildResult.Failed, CycleEngineFingerprint = "engine-0" };
        Assert.Equal("no trusted record",
            ReasonOf(Member("t1", Reads, outside) with { CurrentTerm = "t2", Record = untrusted }));

        // kayıt güvenilir; motor, terim, çıktı ve yüzey yanlış ⇒ motor (grup çapındaki neden önde)
        var otherEngine = Ledger("t1", Reads) with { CycleEngineFingerprint = "engine-0" };
        Assert.Equal("engine changed",
            ReasonOf(Member("t1", Reads, outside) with { CurrentTerm = "t2", Record = otherEngine }));

        // motor aynı; terim, çıktı ve yüzey yanlış ⇒ kendi girdisi
        Assert.Equal("own inputs changed", ReasonOf(Member("t1", Reads, outside) with { CurrentTerm = "t2" }));

        // terim aynı; çıktı (araç dışı) ve yüzey yanlış ⇒ çıktı; beslenen kopya da bozuksa "araç dışı" önde kalır
        Assert.Equal("output built outside this tool", ReasonOf(Member("t1", Reads, outside)));
        Assert.Equal("output built outside this tool", ReasonOf(Member("t1", Reads, outside with { FedIntact = false })));

        // zaman kipi ama kanıt dosyası yok ⇒ "araç dışında derlendi" yanlış olurdu: kanıt eksik
        Assert.Equal("output evidence missing", ReasonOf(Member("t1", Reads, outside with { EvidenceMissing = true })));

        // çıktı sağlam; yalnız yüzey yanlış ⇒ yüzey
        Assert.Equal("read surface moved: " + B1, ReasonOf(Member("t1", Reads, Intact)));
    }

    [Fact] // Üyeler birbirinden bağımsız karara girer; ToBuild build order'ı (verilen sırayı) korur.
    public void to_build_keeps_build_order()
    {
        var carried = Member("t1", Reads, Intact);
        var changed = carried with { CurrentTerm = "t2" };

        var decision = Decide(Disk(Reads), ("D", changed), ("B", carried), ("A", changed), ("C", carried));

        Assert.Equal(new[] { "D", "A" }, decision.ToBuild);
        AssertNeeded(decision, "D", "own inputs changed");
        AssertNeeded(decision, "A", "own inputs changed");
        AssertCarried(decision, "B");
        AssertCarried(decision, "C");
        Assert.Equal(2, decision.Reasons.Count);              // yalnız gerekli üyeler
        Assert.Equal(2, decision.CarriedReadStates.Count);    // yalnız taşınan üyeler
    }

    [Fact]
    public void an_empty_group_needs_nothing()
    {
        var decision = CycleMemberNeed.Decide([], _ => throw new InvalidOperationException("no member to ask about"),
            Disk(), Engine);

        Assert.Empty(decision.ToBuild);
        Assert.Empty(decision.CarriedReadStates);
        Assert.Empty(decision.Reasons);
    }
}
