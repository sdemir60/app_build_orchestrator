using System.Globalization;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [T20-b/K11] Perf profilinin KOPYA METNİ — App'in konsol notu ile Supervisor'ın run-başı satırları AYNI
/// sözlüğü kullanmak zorundadır. İki assembly'de iki formatlayıcı tutmak, T49'un token-drift'inin motor
/// karşılığıdır: biri "cpu cap 70%" derken diğeri "cpu 70" demeye başlar ve kimse fark etmez.
/// <para>Sayı formatlaması <see cref="CultureInfo.InvariantCulture"/>'dır (yüzde ayracı locale'e göre kaymaz).</para>
/// </summary>
public static class PerfNoteText
{
    /// <summary>Cap'i olmayan (Full) profilin değer terimi.</summary>
    public const string CapValueOff = "off";

    /// <summary>Perf modu HİÇ bildirilmemiş run'ların değer terimi. "off" DEĞİLDİR: tanıda "kapatıldı" ile
    /// "hiç istenmedi" aynı şey değildir (yalnız Supervisor'ın run-başı satırlarında görülür).</summary>
    public const string CapValueUnset = "unset";

    /// <summary>Cap'in DEĞER yarısı: <c>"70%"</c> · <c>"off"</c>.</summary>
    public static string CapValue(int? capPercent) => capPercent is { } percent
        ? string.Format(CultureInfo.InvariantCulture, "{0}%", percent)
        : CapValueOff;

    /// <summary>Cap terimi (prose): <c>"cpu cap 70%"</c> · <c>"cpu cap off"</c>.</summary>
    public static string CapText(int? capPercent) => "cpu cap " + CapValue(capPercent);

    /// <summary>Perf modu bildirilmemiş run'ın cap terimi: <c>"cpu cap unset"</c>.</summary>
    public static string CapTextUnset => "cpu cap " + CapValueUnset;

    /// <summary>
    /// [K11 BİREBİR] Perf chip'inin konsol notu: <c>parallelism: 4 · cpu cap 70%</c> · cap'siz (Full) profilde
    /// <c>parallelism: 6 · cpu cap off</c>. Ayraç U+00B7 (boşluklu).
    /// </summary>
    public static string Note(PerfProfile profile) => string.Format(CultureInfo.InvariantCulture,
        "parallelism: {0} · {1}", profile.Parallelism, CapText(profile.CpuCapPercent));

    /// <summary>
    /// [RESOLVE Faz 4 / karar 11] Resolve cycles'ın tam öncelik notu — chip notunun ailesinde, priority ve koşu adı
    /// eklenmiş TEK satır: <c>parallelism: 4 · cpu cap off · priority normal (Resolve cycles)</c>. Dönüşüm
    /// (<see cref="PerfProfile.ForRun"/>) profili DEĞİŞTİRMEDİYSE <c>null</c>: Build/Rebuild/Clean, kapalı anahtar ve
    /// zaten tam öncelikli Full için söylenecek ek bir şey yoktur. Supervisor decision.log satırını, App kullanıcının
    /// konsol satırını (<c>runStarted</c>) yalnız bu dolu iken yazar.
    /// </summary>
    public static string? ResolveNote(RunMode mode, PerfProfile profile, bool resolveAtFullPriority)
    {
        var run = PerfProfile.ForRun(mode, profile, resolveAtFullPriority);
        return run == profile ? null : string.Format(CultureInfo.InvariantCulture,
            "{0} · priority {1} (Resolve cycles)", Note(run), PriorityValue(run.Priority));
    }

    /// <summary>Priority'nin değer terimi (<c>"normal"</c>) — perf konsol metninin sözlüğü bu sınıftadır. Her sınıf AÇIKÇA
    /// yazılıdır: tanımsız bir değer (ileride eklenen bir enum üyesi) sessizce yanlış etiketlenmez, fırlatır.</summary>
    public static string PriorityValue(ProcessPriorityClassKind kind) => kind switch
    {
        ProcessPriorityClassKind.Normal => "normal",
        ProcessPriorityClassKind.BelowNormal => "below normal",
        ProcessPriorityClassKind.Idle => "idle",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>[RESOLVE Faz 4 / karar 11] Koşu içindeki chip notu: Resolve tam öncelikteyse <see cref="ResolveNote"/>,
    /// değilse profilin kendi notu (<see cref="Note(PerfProfile)"/>). App koşu komutunun modu + anahtarıyla çağırır —
    /// not, motorun o profile uyguladığını söyler.</summary>
    public static string Note(RunMode mode, PerfProfile profile, bool resolveAtFullPriority) =>
        ResolveNote(mode, profile, resolveAtFullPriority) ?? Note(profile);

    /// <summary>
    /// [PERF Faz D / karar 10] Motorun, profilin istediği işçi sayısını makineye göre KIRPTIĞINI söyleyen satır:
    /// <c>workers reduced to 2 (1 logical processor)</c>. Konsola ve decision.log'a AYNI metin yazılır; gerekçe
    /// (<paramref name="reason"/>) <see cref="WorkerBudgetDecision.Reason"/>'dan gelir.
    /// </summary>
    public static string WorkersReduced(int workers, string reason) => string.Format(
        CultureInfo.InvariantCulture, "workers reduced to {0} ({1})", workers, reason);

    /// <summary>
    /// [PERF Faz D / karar 10] Kırpmanın ÇEKİRDEK gerekçesi: <c>"2 logical processors"</c> (tek işlemcide tekil:
    /// <c>"1 logical processor"</c>). <see cref="WorkerBudgetDecision.Reason"/> bunu taşır; çerçeve cümle
    /// <see cref="WorkersReduced"/>'tadır — kullanıcıya görünen kırpma metninin TEK sahibi bu sınıftır.
    /// </summary>
    public static string LogicalProcessorsLimit(int logicalProcessors) => string.Format(
        CultureInfo.InvariantCulture,
        logicalProcessors == 1 ? "{0} logical processor" : "{0} logical processors",
        logicalProcessors);

    /// <summary>
    /// [PERF Faz D / karar 10] Kırpmanın BELLEK gerekçesi: <c>"3 GB free memory"</c>; sayı tam gigabayta aşağı
    /// yuvarlanmış boş bellektir (<see cref="WorkerBudgetDecision.Reason"/>).
    /// </summary>
    public static string FreeMemoryLimit(long freeGigabytes) => string.Format(
        CultureInfo.InvariantCulture, "{0} GB free memory", freeGigabytes);
}
