using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// [PERF Faz E1] Resolve satırlarının biçimi — tek sahibi <see cref="CycleDecisionLines"/>. Koordinatörün bu
/// satırları doğru anda yazdığı <c>CycleDecisionLogTests</c>'te pinlidir; burada pinlenen metnin kendisidir
/// (süreler sabit verilir, gerçek saat yok).
/// </summary>
public class CycleDecisionLinesTests
{
    [Fact]
    public void the_group_header_names_members_producers_evidence_and_the_hash_time()
    {
        Assert.Equal("cycle Core: 17 members, 12 producers, evidence on, hash 5930 ms",
            CycleDecisionLines.GroupStarted("Core", 17, 12, evidence: true, hashMs: 5930));
        Assert.Equal("cycle Core: 2 members, 2 producers, evidence off, hash 0 ms",
            CycleDecisionLines.GroupStarted("Core", 2, 2, evidence: false, hashMs: 0));
    }

    [Fact]
    public void an_evidence_loss_names_the_producer_the_file_and_the_reason()
    {
        Assert.Equal(@"cycle Core: surface evidence unavailable — producer Data, file X:\bin\Data.dll: unreadable (locked or corrupt) — full rounds",
            CycleDecisionLines.EvidenceUnavailable("Core", "Data", @"X:\bin\Data.dll", CycleDecisionLines.UnreadableReason));
        Assert.Equal("cycle Core: surface evidence unavailable — producer Data, file (none): evidence path could not be derived — full rounds",
            CycleDecisionLines.EvidenceUnavailable("Core", "Data", CycleDecisionLines.NoFile, CycleDecisionLines.NoEvidencePathReason));
    }

    [Fact]
    public void a_round_line_carries_the_decision_the_stale_members_the_moved_files_and_the_timings()
    {
        Assert.Equal(@"cycle Core round 1: continue; stale=2 [Ui, Web]; moved=X:\bin\Data.dll; levels=3; round 40 ms; hash 2 ms",
            CycleDecisionLines.RoundEnded("Core", 1, CycleRoundDecision.Continue, ["Ui", "Web"], [@"X:\bin\Data.dll"],
                levels: 3, roundMs: 40, hashMs: 2));
        Assert.Equal("cycle Core round 2: converged; stale=0 []; moved=none; levels=1; round 9 ms; hash 1 ms",
            CycleDecisionLines.RoundEnded("Core", 2, CycleRoundDecision.Converged, [], [], levels: 1, roundMs: 9, hashMs: 1));
    }

    [Fact] // kanıt yoksa (tam-tur kipi) bayat küme ve kayan dosya bilinmez — sıfır DEĞİL, "n/a"
    public void without_evidence_the_stale_and_moved_fields_read_not_applicable()
    {
        Assert.Equal("cycle Core round 1: no progress; stale=n/a; moved=n/a; levels=2; round 5 ms; hash 0 ms",
            CycleDecisionLines.RoundEnded("Core", 1, CycleRoundDecision.NoProgress, null, null, levels: 2, roundMs: 5, hashMs: 0));
    }

    [Theory]
    [InlineData(CycleRoundDecision.Continue, "continue")]
    [InlineData(CycleRoundDecision.Converged, "converged")]
    [InlineData(CycleRoundDecision.NoProgress, "no progress")]
    [InlineData(CycleRoundDecision.CapReached, "cap reached")]
    public void every_decision_has_its_own_term(CycleRoundDecision decision, string term) =>
        Assert.Equal(term, CycleDecisionLines.DecisionTerm(decision));

    [Fact] // [Fix round 1 — I1] karar satırı Supervisor'dan TAŞINDI: metin bayt bayt aynı kalır
    public void the_verdict_names_the_outcome_the_member_count_and_the_remembered_signature()
    {
        Assert.Equal("cycle A: converged (2 members)",
            CycleDecisionLines.Verdict("A", CycleRoundDecision.Converged, 2, rememberedAt: null));
        Assert.Equal("cycle A: no progress — another round could not change the result (2 members); non-convergence remembered at sig",
            CycleDecisionLines.Verdict("A", CycleRoundDecision.NoProgress, 2, rememberedAt: "sig"));
        Assert.Equal($"cycle A: round cap reached ({CycleRoundPolicy.RoundCap} rounds) — output may be one generation behind (3 members)",
            CycleDecisionLines.Verdict("A", CycleRoundDecision.CapReached, 3, rememberedAt: null));
    }

    [Theory] // tek eşleme: karar satırının açıklaması tur satırındaki terimi taşır
    [InlineData(CycleRoundDecision.Converged)]
    [InlineData(CycleRoundDecision.NoProgress)]
    [InlineData(CycleRoundDecision.CapReached)]
    public void the_verdict_text_carries_the_round_decision_term(CycleRoundDecision decision) =>
        Assert.Contains(CycleDecisionLines.DecisionTerm(decision), CycleDecisionLines.OutcomeText(decision), StringComparison.Ordinal);

    [Fact] // [Fix round 1 — I1] retry satırı Supervisor'dan TAŞINDI
    public void the_retry_line_names_the_group_and_the_remembered_signature() =>
        Assert.Equal("cycle A: retrying — did not converge at this signature (sig) on an earlier run",
            CycleDecisionLines.Retrying("A", "sig"));

    [Fact] // kayan dosya listesi sınırda kesilir; kalan SAYI olarak yazılır, satır uzayıp gitmez
    public void the_moved_files_beyond_the_limit_are_counted_not_listed()
    {
        string[] files = [.. Enumerable.Range(1, CycleDecisionLines.MovedFileLimit + 2).Select(i => $"f{i}.dll")];
        string listed = string.Join(", ", files.Take(CycleDecisionLines.MovedFileLimit));

        Assert.Equal(listed + " … (+2 more)", CycleDecisionLines.MovedTerm(files));
        Assert.Equal(listed, CycleDecisionLines.MovedTerm([.. files.Take(CycleDecisionLines.MovedFileLimit)]));
        // [Fix round 1 — M4] ayırıcı bayat kümeninkiyle aynı: boşluklu tam yolun sınırı belirsizleşmez
        Assert.Equal(@"X:\my libs\a.dll, X:\bin\b.dll", CycleDecisionLines.MovedTerm([@"X:\my libs\a.dll", @"X:\bin\b.dll"]));
    }
}
