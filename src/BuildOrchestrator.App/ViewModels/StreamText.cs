using System.Globalization;
using BuildOrchestrator.Contracts.Ipc;
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

    /// <summary>build-data.js:417 — <c>{name} skipped — {reason}</c>. [Task 2] <paramref name="reason"/> artık
    /// <see cref="ProjectSkippedEvent.Reason"/>'dan GELİR (yalın — tek kaynak <see cref="SkipReasons"/>);
    /// "up to date" hardcode edilmiş SABİT değildi, önceki sürüm reason'ı yok sayardı.</summary>
    public static string Skipped(string name, string reason) =>
        string.Format(CultureInfo.InvariantCulture, "{0} skipped — {1}", name, reason);

    /// <summary>[Task 2/cycles] Cycles koşusunda <see cref="SkipReasons.OutOfCycleScope"/> gerekçeli skip'ler
    /// proje başına satır YAZMAZ — 150+ kapsam-dışı proje ekranı fırtınaya boğardı. Toplanıp TEK Info satırında
    /// raporlanır: <c>{n} outside cycle scope — skipped</c>.</summary>
    public static string OutsideCycleScope(int count) =>
        string.Format(CultureInfo.InvariantCulture, "{0} outside cycle scope — skipped", count);

    /// <summary>build-data.js:284 — <c>Sync — {n} to build, {m} up to date</c>.</summary>
    public static string Sync(int toBuild, int upToDate) =>
        string.Format(CultureInfo.InvariantCulture, "Sync — {0} to build, {1} up to date", toBuild, upToDate);

    /// <summary>build-data.js:309 — <c>Build started — {n} projects, parallelism {p}</c>.</summary>
    public static string BuildStarted(int projects, int parallelism) =>
        string.Format(CultureInfo.InvariantCulture, "Build started — {0} projects, parallelism {1}", projects, parallelism);

    /// <summary>[cycles] Bir <c>RunMode.Cycles</c> koşusunun açılış satırı. "Build started"ı yeniden
    /// kullanmaz: bu koşu bir build DEĞİLDİR ve kullanıcıyı bekleten şey de proje sayısı değil, TUR sayısıdır —
    /// satır tam olarak ne satın alındığını söyler. Tavan literal DEĞİL, tek kaynağı
    /// <see cref="Core.Planning.CycleRoundPolicy.RoundCap"/>'tir.
    /// <para><b>[DEĞİŞEN KURAL — Task 4]</b> Eski iddia: satır TEK toplam proje sayısı taşırdı (<c>CyclesStarted(int
    /// projects)</c>). Gerekçe: toplamın çoğu upstream (bağımlılık ön koşulu) olabiliyordu ve kullanıcı "neden bu
    /// kadar proje derleniyor" sorusunu ekrandan okuyamıyordu — satır artık gerçek döngü üyesi/prerequisite
    /// kırılımını ayrı ayrı söyler (kırılım çağıranda — <c>RunViewModel.Stream</c> — will-build ∩ üyelik'ten
    /// hesaplanır).</para></summary>
    public static string CyclesStarted(int members, int prerequisites) =>
        string.Format(CultureInfo.InvariantCulture, "Cycles started — {0} cycle members · {1} prerequisites · up to {2} rounds",
            members, prerequisites, Core.Planning.CycleRoundPolicy.RoundCap);

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

    /// <summary>[cycle rounds/Task 8] Tur göstergesi — <c>CycleRoundStartedEvent</c>'in TEK metin kaynağı:
    /// <c>cycle round {round}/{cap} — {memberCount} members</c>.
    /// <para><b>[DEĞİŞEN KURAL — Task 4]</b> Eski iddia: <c>{leaderName} (+{memberCount-1} more)</c> — tek lider
    /// adı grubu temsil etmiyordu; koşunun maliyetini üye sayısı anlatır. <c>CycleRoundStartedEvent.ProjectId</c>
    /// (lider) event'te durduğu için satır hâlâ lidere tıklatır — yalnız METİN lider adını bırakır.</para></summary>
    public static string CycleRound(int round, int cap, int memberCount) =>
        string.Format(CultureInfo.InvariantCulture, "cycle round {0}/{1} — {2} members", round, cap, memberCount);

    /// <summary>[Task 4] Aktif satırın grup-ilerleme detayı — <c>StreamComposer.StartBuilding</c>'in <c>detail</c>
    /// parametresinin TEK metin kaynağı: <c>member {index}/{count} · round {round}/{cap}</c>. Kopya YASAK
    /// (CLAUDE.md) — <c>RunViewModel.Stream</c> bu metni inline BİLEŞTİRMEZ, yalnız çağırır.</summary>
    public static string CycleMemberDetail(int index, int count, int round, int cap) =>
        string.Format(CultureInfo.InvariantCulture, "member {0}/{1} · round {2}/{3}", index, count, round, cap);

    /// <summary>[Task 3/cycles] <c>CycleCompletedEvent</c>'in TEK metin kaynağı — grubun neden öyle bittiğini
    /// (yakınsadı / ilerleme yok / tavana dayandı) ekrana taşır. Kind eşlemesi (Converged→Ok, NoProgress→Fail,
    /// CapReached→Info) çağıranda (<c>RunViewModel.Stream</c>) yapılır — burada yalnız METİN.</summary>
    public static string CycleCompleted(CycleOutcome outcome, int members, int rounds, int failed, long durationMs) =>
        outcome switch
        {
            CycleOutcome.Converged => string.Format(CultureInfo.InvariantCulture,
                "cycle converged — {0} members · {1} rounds · {2}", members, rounds, DurationFormat.Duration(durationMs)),
            CycleOutcome.NoProgress => string.Format(CultureInfo.InvariantCulture,
                "cycle failed — same {0} members failing twice · {1} rounds", failed, rounds),
            CycleOutcome.CapReached => string.Format(CultureInfo.InvariantCulture,
                "cycle round cap reached — output may be one generation behind · {0} rounds", rounds),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "unknown cycle outcome"),
        };
}
