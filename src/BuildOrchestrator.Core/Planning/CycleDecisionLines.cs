using System.Globalization;

namespace BuildOrchestrator.Core.Planning;

/// <summary>
/// [PERF Faz E1] Resolve (SCC tur döngüsü) satırlarının TEK sahibi: <c>decision.log</c>'a — ve aynı olayı başka
/// bir kanala taşıyacak her yere — giden metin YALNIZ buradan üretilir. Supervisor ölçüleri toplar (kim bayat,
/// hangi dosya kaydı, ne kadar sürdü) ve bu metotları çağırır; biçimi bilmez. Her satır biçimi ayrı, saf bir
/// metottur: yeni bir satır (ör. üye düzeyi atlama) yeni bir metot olarak eklenir, mevcut biçimlere dokunmaz.
/// Sayılar <see cref="CultureInfo.InvariantCulture"/>'dır.
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

    /// <summary>Tur kararının terimi.</summary>
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
        : string.Format(CultureInfo.InvariantCulture, "{0} [{1}]", staleNames.Count, string.Join(", ", staleNames));

    /// <summary>Kayan dosyalar: <c>X:\a.dll X:\b.dll</c> · sınırı aşınca <c>… (+2 more)</c> · <c>none</c> · <c>n/a</c>.</summary>
    public static string MovedTerm(IReadOnlyList<string>? movedFiles)
    {
        if (movedFiles is null) return NotApplicable;
        if (movedFiles.Count == 0) return NoneMoved;
        string shown = string.Join(" ", movedFiles.Take(MovedFileLimit));
        return movedFiles.Count <= MovedFileLimit
            ? shown
            : string.Format(CultureInfo.InvariantCulture, "{0} … (+{1} more)", shown, movedFiles.Count - MovedFileLimit);
    }
}
