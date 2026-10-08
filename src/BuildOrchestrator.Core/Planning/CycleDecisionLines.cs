using System.Globalization;

namespace BuildOrchestrator.Core.Planning;

/// <summary>
/// [PERF Faz E1] Resolve (SCC tur döngüsü) satırlarının TEK sahibi: <c>decision.log</c>'a giden metin YALNIZ
/// buradan üretilir. App'in olay akışı metni (<c>StreamText</c>) ayrı tanımlıdır — bu sahiplik ona uzanmaz.
/// Supervisor ölçüleri toplar (kim bayat, hangi dosya kaydı, ne kadar sürdü) ve bu metotları çağırır; biçimi
/// bilmez. Her satır biçimi ayrı, saf bir metottur: yeni bir satır (ör. üye düzeyi atlama) yeni bir metot olarak
/// eklenir, mevcut biçimlere dokunmaz. Sayılar <see cref="CultureInfo.InvariantCulture"/>'dır.
/// </summary>
public static class CycleDecisionLines
{
    /// <summary>Okunamayan yüzey dosyasının nedeni. Yüzey özeti kilitli ve bozuk dosyayı AYIRT ETMEDEN null
    /// döner; neden bu yüzden ikisini birden adlandırır.</summary>
    public const string UnreadableReason = "unreadable (locked or corrupt)";

    /// <summary>Üreticinin kanıt yolu türetilemedi: artımlı planın çıktı haritasında kaydı yok.</summary>
    public const string NoEvidencePathReason = "evidence path could not be derived";

    /// <summary>Dosyası olmayan kanıt kaybında (yol türetilemedi) dosya yerine yazılan terim.</summary>
    public const string NoFile = "(none)";

    /// <summary>Kanıt yokken (tam-tur kipi) bayat küme ve kayan dosya alanlarının terimi.</summary>
    public const string NotApplicable = "n/a";

    /// <summary>Kanıt varken hiçbir dosya kaymadıysa kayan dosya alanının terimi.</summary>
    public const string NoneMoved = "none";

    /// <summary>Tur satırındaki listelerin (bayat üyeler, kayan dosyalar) ortak ayırıcısı.</summary>
    private const string ListSeparator = ", ";

    /// <summary>Tur satırında adıyla yazılan kayan dosya sayısının üst sınırı; fazlası sayı olarak eklenir.</summary>
    public const int MovedFileLimit = 3;

    /// <summary>Tur 1'den ÖNCE, grup başı hash'inden sonra:
    /// <c>cycle A: 3 members, 3 producers, evidence on, hash 12 ms</c>.</summary>
    public static string GroupStarted(string group, int members, int producers, bool evidence, long hashMs) =>
        string.Format(CultureInfo.InvariantCulture, "cycle {0}: {1} members, {2} producers, evidence {3}, hash {4} ms",
            group, members, producers, evidence ? "on" : "off", hashMs);

    /// <summary>Kanıt kaybı — koşu başında ya da derleme sonrasında, grubun İLK kaybı:
    /// <c>cycle A: surface evidence unavailable — producer B, file X:\bin\B.dll: unreadable (locked or corrupt) — full rounds</c>.</summary>
    public static string EvidenceUnavailable(string group, string producer, string file, string reason) =>
        string.Format(CultureInfo.InvariantCulture,
            "cycle {0}: surface evidence unavailable — producer {1}, file {2}: {3} — full rounds",
            group, producer, file, reason);

    /// <summary>Her tur sonunda, karar verildikten sonra:
    /// <c>cycle A round 1: continue; stale=1 [A]; moved=X:\bin\B.dll; levels=3; round 40 ms; hash 2 ms</c>.
    /// <paramref name="staleNames"/> ve <paramref name="movedFiles"/> null ⇒ kanıt yok (tam-tur kipi).
    /// <paramref name="hashMs"/> turun derleme sonrası hash sürelerinin TOPLAMIDIR.</summary>
    public static string RoundEnded(string group, int round, CycleRoundDecision decision,
        IReadOnlyList<string>? staleNames, IReadOnlyList<string>? movedFiles, int levels, long roundMs, long hashMs) =>
        string.Format(CultureInfo.InvariantCulture,
            "cycle {0} round {1}: {2}; stale={3}; moved={4}; levels={5}; round {6} ms; hash {7} ms",
            group, round, DecisionTerm(decision), StaleTerm(staleNames), MovedTerm(movedFiles), levels, roundMs, hashMs);

    /// <summary>[Fix round 1 — I1] Grubun nihai kararı (Supervisor'dan taşındı) — TEK biçim, koşunun sonunda bir kez:
    /// <c>cycle A: converged (2 members, 1 compiled)</c> · <c>cycle A: no progress — … (2 members)</c> · NoProgress
    /// hafızaya yazıldıysa <c>…; non-convergence remembered at sig</c>. <paramref name="compiled"/> bu koşuda DERLENEN üye
    /// sayısıdır (tur 1'de gerekmeyen, sonra da bayatlamayan taşınan üye girmez; kanıtsız grupta herkes derlendiği için
    /// üye sayısına eşittir) ve YALNIZ yakınsama satırında yazılır — diğer kararların satırı sayıyı taşımaz.</summary>
    public static string Verdict(string group, CycleRoundDecision decision, int members, int compiled, string? rememberedAt) =>
        string.Format(CultureInfo.InvariantCulture, "cycle {0}: {1} ({2} members{3}){4}",
            group, OutcomeText(decision), members,
            decision == CycleRoundDecision.Converged
                ? string.Format(CultureInfo.InvariantCulture, ", {0} compiled", compiled)
                : "",
            rememberedAt is null ? "" : "; non-convergence remembered at " + rememberedAt);

    /// <summary>[RESOLVE 3.4] Tur 1'de derlenecek üyenin nedeni — grup başlığından sonra, build order'da:
    /// <c>A: round 1 — own inputs changed</c>. Neden metni <see cref="CycleMemberNeed"/>'in kararıdır (metinleri oradaki
    /// const'lar); taşınan üye bu satırı almaz, grup sonunda <see cref="CarriedDetail"/> ile raporlanır.</summary>
    public static string RoundOneNeed(string member, string reason) =>
        string.Format(CultureInfo.InvariantCulture, "{0}: round 1 — {1}", member, reason);

    /// <summary>[RESOLVE 3.4] Taşınan üyenin atlama satırındaki ayrıntı. Satırın kendisi tekil projeyle ORTAK atlama
    /// biçimidir (<c>{ad}: skipped — up to date ({ayrıntı})</c>); bu sınıf yalnız ayrıntının sahibidir:
    /// <c>B: skipped — up to date (carried: own inputs and read surfaces unchanged)</c>.</summary>
    public const string CarriedDetail = "carried: own inputs and read surfaces unchanged";

    /// <summary>Kararın kullanıcıya dönük açıklaması — baş terimi <see cref="DecisionTerm"/>'dür (enum→metin eşlemesi
    /// tek yerde); tavan sayısı literal DEĞİL, tek kaynak <see cref="CycleRoundPolicy.RoundCap"/>.</summary>
    public static string OutcomeText(CycleRoundDecision decision) => decision switch
    {
        CycleRoundDecision.Converged => DecisionTerm(decision),
        // [suçlu kırmızı/metin] "the same members failed twice" idi; yüzey kanıtı NoProgress'i TEK turda da
        // verebildiği için "twice" yanlışlanabilir bir iddiaya dönüştü — metin iki kanıt yolunu da kapsar.
        CycleRoundDecision.NoProgress => DecisionTerm(decision) + " — another round could not change the result",
        CycleRoundDecision.CapReached => string.Format(CultureInfo.InvariantCulture,
            "round {0} ({1} rounds) — output may be one generation behind", DecisionTerm(decision), CycleRoundPolicy.RoundCap),
        _ => "interrupted",
    };

    /// <summary>[Fix round 1 — I1] Bu imzada daha önce yakınsamamış grubun açık Resolve'da yeniden denenmesi
    /// (Supervisor'dan taşındı, metin aynı): <c>cycle A: retrying — did not converge at this signature (sig) on an earlier run</c>.</summary>
    public static string Retrying(string group, string signature) =>
        string.Format(CultureInfo.InvariantCulture,
            "cycle {0}: retrying — did not converge at this signature ({1}) on an earlier run", group, signature);

    /// <summary>[Build cycle derler] Rebuild önbelleği yok sayar: grubun her üyesi tur 1'de derlenir, tur 1'in üye
    /// ihtiyacı (<see cref="CycleMemberNeed"/>) sorulmaz; yüzey kanıtı yalnız sonraki turların kararı için okunur:
    /// <c>cycle A: rebuild — every member compiles in round one</c>.</summary>
    public static string RebuildCompilesEveryMember(string group) =>
        string.Format(CultureInfo.InvariantCulture, "cycle {0}: rebuild — every member compiles in round one", group);

    /// <summary>Tur kararının terimi — enum→metin eşlemesinin TEK yeri; karar açıklaması (<see cref="OutcomeText"/>)
    /// bunun üstüne kurulur.</summary>
    public static string DecisionTerm(CycleRoundDecision decision) => decision switch
    {
        CycleRoundDecision.Continue => "continue",
        CycleRoundDecision.Converged => "converged",
        CycleRoundDecision.NoProgress => "no progress",
        CycleRoundDecision.CapReached => "cap reached",
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, null),
    };

    /// <summary>Bayat küme: <c>2 [A, C]</c> · <c>0 []</c> · kanıt yoksa <c>n/a</c>.</summary>
    public static string StaleTerm(IReadOnlyList<string>? staleNames) => staleNames is null
        ? NotApplicable
        : string.Format(CultureInfo.InvariantCulture, "{0} [{1}]", staleNames.Count, string.Join(ListSeparator, staleNames));

    /// <summary>Kayan dosyalar: <c>X:\a.dll, X:\b.dll</c> · sınırı aşınca <c>… (+2 more)</c> · <c>none</c> · <c>n/a</c>.
    /// Ayırıcı bayat kümeninkiyle aynıdır: tam yol boşluk taşıyabilir, düz boşluk yol sınırını bulanıklaştırırdı.</summary>
    public static string MovedTerm(IReadOnlyList<string>? movedFiles)
    {
        if (movedFiles is null) return NotApplicable;
        if (movedFiles.Count == 0) return NoneMoved;
        string shown = string.Join(ListSeparator, movedFiles.Take(MovedFileLimit));
        return movedFiles.Count <= MovedFileLimit
            ? shown
            : string.Format(CultureInfo.InvariantCulture, "{0} … (+{1} more)", shown, movedFiles.Count - MovedFileLimit);
    }
}
