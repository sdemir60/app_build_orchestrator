namespace BuildOrchestrator.App.Console;

/// <summary>
/// [D4/T34 — DD2] Konsol anlatı satırlarının "typing degradation" motorunun SAF karar çekirdeği (v7 plan §230:
/// <b>drop-to-latest · throughput-suspend · hatalar anında · ham MSBuild asla harf-harf</b>). Yalnız "en yeni
/// satır daktilolanmalı mı?" kararını verir; tempo/daktilo <see cref="TypewriterScheduler"/>'dadır. Stopwatch/
/// tick'ten bağımsız: çağıran monoton <c>nowMs</c> verir (üretimde <c>Environment.TickCount64</c>, testte
/// deterministik enjekte edilir — D8). Event stream'in <see cref="ViewModels.StreamComposer"/>'ının fırtına
/// (<c>burst</c>) kuralıyla AYNI 340ms penceresini paylaşır (kopya karar mantığı DEĞİL — konsola özgü ek iki
/// kural: throughput-suspend + ham-MSBuild).
/// </summary>
public sealed class ConsoleTypingGate
{
    /// <summary>Ardışık satır varışları bu pencereden yakınsa (<c>&lt; 340ms</c>) daktilodan hızlı gelmiş
    /// sayılır → "en yeniye düş" (instant). Event stream burst penceresiyle (build-data.js:257) birebir.</summary>
    public const double BurstWindowMs = 340.0;

    /// <summary>Tek bir flush bu satır sayısını AŞARSA (fırtına/yüksek throughput) daktilo TAMAMEN askıya alınır —
    /// tüm batch anında basılır (build çıktısı akarken konsol kilitlenmesin).</summary>
    public const int ThroughputSuspendLines = 8;

    private long? _lastArrivalMs;

    /// <summary>En yeni satır daktilolanmalı mı? Herhangi bir degradation kuralı tetiklenirse <c>false</c>
    /// (anında bas). Varış saati her çağrıda kaydedilir (burst penceresi TÜM varışlardan ölçülür — prototip
    /// <c>lastEmit</c>).</summary>
    /// <param name="isRawProjectLog">Satır ham MSBuild çıktısı mı (anlatı değil — zaman damgası yok).</param>
    /// <param name="newestType">En yeni satırın görsel tipi (<see cref="ConsoleLineClassifier"/>'dan).</param>
    /// <param name="batchLineCount">Bu flush'taki toplam satır sayısı (throughput ölçüsü).</param>
    /// <param name="nowMs">Monoton varış saati (ms).</param>
    public bool ShouldType(bool isRawProjectLog, ConsoleLineType newestType, int batchLineCount, long nowMs)
    {
        bool burst = _lastArrivalMs is { } last && (nowMs - last) < BurstWindowMs;
        _lastArrivalMs = nowMs;

        if (isRawProjectLog) return false;                          // ham MSBuild ASLA harf-harf
        if (newestType is ConsoleLineType.Error) return false;      // hatalar ANINDA
        if (batchLineCount > ThroughputSuspendLines) return false;  // throughput üstünde daktilo TAMAMEN askıda
        if (burst) return false;                                    // daktilodan hızlı → en yeniye düş (instant)
        return true;
    }
}
