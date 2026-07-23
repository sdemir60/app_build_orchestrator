using System.Globalization;
using BuildOrchestrator.Core.Formatting;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D3/T?] Event stream anlatı satırlarının SAF, statik metin bileşimi — design-v1 (<c>build-data.js</c>
/// <c>emit(...)</c> çağrıları) birebir. Tüm sayı/süre biçimlemesi <see cref="CultureInfo.InvariantCulture"/>
/// (Türkçe Windows'ta <c>4,2s</c>/binlik-ayraç tuzağı VM'e sızmasın). Süreler <see cref="DurationFormat.Duration"/>
/// (prototip <c>fmtDur</c>). Görünüm bu metinleri OKUR, mantığı kopyalamaz (RibbonText deseni).
/// </summary>
public static class StreamText
{
    /// <summary>build-data.js:402 — <c>{name} built ({dur})</c>.</summary>
    public static string Built(string name, long durationMs) =>
        string.Format(CultureInfo.InvariantCulture, "{0} built ({1})", name, DurationFormat.Duration(durationMs));

    /// <summary>build-data.js:399 — <c>{name} built — dependency issue ({dur})</c>.</summary>
    public static string BuiltDependencyIssue(string name, long durationMs) =>
        string.Format(CultureInfo.InvariantCulture, "{0} built — dependency issue ({1})", name, DurationFormat.Duration(durationMs));

    /// <summary>build-data.js:392 — prototipte <c>{name} failed — {n} errors ({dur})</c>. <b>Wire-gap:</b> IPC
    /// (<c>ProjectFailedEvent</c>) bir hata SAYISI taşımaz; yalnız bir <c>Reason</c> ("exit 1"/"timeout"/"stopped")
    /// taşır — uydurmak yerine gerçek sebebi yüzeye çıkarırız: <c>{name} failed — {reason} ({dur})</c>. Bkz. report.</summary>
    public static string Failed(string name, string reason, long durationMs) =>
        string.Format(CultureInfo.InvariantCulture, "{0} failed — {1} ({2})", name, reason, DurationFormat.Duration(durationMs));

    /// <summary>build-data.js:417 — <c>{name} skipped — up to date</c>.</summary>
    public static string Skipped(string name) =>
        string.Format(CultureInfo.InvariantCulture, "{0} skipped — up to date", name);

    /// <summary>build-data.js:284 — <c>Sync — {n} to build, {m} up to date</c>.</summary>
    public static string Sync(int toBuild, int upToDate) =>
        string.Format(CultureInfo.InvariantCulture, "Sync — {0} to build, {1} up to date", toBuild, upToDate);

    /// <summary>build-data.js:309 — <c>Build started — {n} projects, parallelism {p}</c>.</summary>
    public static string BuildStarted(int projects, int parallelism) =>
        string.Format(CultureInfo.InvariantCulture, "Build started — {0} projects, parallelism {1}", projects, parallelism);

    /// <summary>build-data.js:321 — <c>Stopped — {n} remaining projects queued</c>.</summary>
    public static string Stopped(int remaining) =>
        string.Format(CultureInfo.InvariantCulture, "Stopped — {0} remaining projects queued", remaining);

    /// <summary>build-data.js:330 — <c>Continue — {n} remaining, parallelism {p}</c>.</summary>
    public static string Continue(int remaining, int parallelism) =>
        string.Format(CultureInfo.InvariantCulture, "Continue — {0} remaining, parallelism {1}", remaining, parallelism);

    /// <summary>build-data.js:496/499 — <c>Completed — …</c>. Hatalı: <c>{f} failed · {s} succeeded · {k} skipped ·
    /// {dur}[ · {di} dependency-affected]</c>; temiz: <c>{s} succeeded · {k} skipped · {dur}</c>. <b>Wire-gap:</b>
    /// prototipin <c>· {w} warnings</c> eki yok — App derleyici-warning sayısını izlemez (RunCompletedEvent'te yok).</summary>
    public static string Completed(int failed, int succeeded, int skipped, int depAffected, long durationMs)
    {
        string dur = DurationFormat.Duration(durationMs);
        if (failed > 0)
        {
            string dep = depAffected > 0
                ? string.Format(CultureInfo.InvariantCulture, " · {0} dependency-affected", depAffected)
                : "";
            return string.Format(CultureInfo.InvariantCulture,
                "Completed — {0} failed · {1} succeeded · {2} skipped · {3}{4}", failed, succeeded, skipped, dur, dep);
        }
        return string.Format(CultureInfo.InvariantCulture,
            "Completed — {0} succeeded · {1} skipped · {2}", succeeded, skipped, dur);
    }
}
