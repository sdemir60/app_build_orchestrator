using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// Bir projenin sayfası açıldı ama LOGU YOK — o sayfanın gövdesine yazılan metin.
///
/// <para><b>Her projenin sayfası vardır.</b> Kart tıklaması artık koşulsuz proje moduna geçer: log yoksa
/// mod kurulmuyor ve kullanıcı run anlatısına bakmaya devam ediyordu, yani tıklama "hiçbir şey yapmıyor" gibi
/// görünüyordu. Oysa log olmasa da elde HER ZAMAN bir şey vardır — proje bu koşuda atlandı, kuyrukta, bir
/// döngüde, ya da hiç derlenmedi.</para>
///
/// <para><b>Metin İKİ satırdır: gerekçe + kanıt.</b> Statüyü tekrar etmez — onu başlık zaten söyler
/// (<see cref="ConsoleStatus.Name"/>). İlk satır NEDEN öyle olduğunu, ikinci satır elde ne olduğunu söyler
/// (son başarıyla derlendiği commit, ya da hiç derlenmediği). Derlenmekte olan bir projenin tek satırı vardır:
/// orada kanıt henüz oluşmamıştır, akış birazdan gelecektir.</para>
///
/// <para><b>[DEĞİŞEN KURAL]</b> Bu sınıf eskiden design-v1'in ÖRNEK metinlerini birebir taşıyordu
/// (<c>Skipped(sha)</c> / <c>Queued(deps)</c>) ve içlerinde uydurma veri vardı — "Last successful build:
/// yesterday 18:42". İkisi de üretimde HİÇ ÇAĞRILMIYORDU: yüzey kurulmuş ama hiçbir yere bağlanmamıştı, yani
/// pinlenen tek şey kullanılmayan bir literaldi. Yerine gerçek satır durumundan türeyen bu tablo geldi;
/// uydurma tarih/saat kaldırıldı, çünkü o veri (son başarılı build'in ZAMANI) bu tarafta yok — elimizde
/// commit var (<see cref="ProjectRowViewModel.CurrentSha"/>) ve söylenen odur.</para>
/// </summary>
public static class ConsoleEmptyState
{
    /// <summary>Derlenmekte olan ama henüz tek satır üretmemiş proje — kanıt satırı YOKTUR.</summary>
    public const string NoLog = "No log yet — output streams here once the build starts.";

    /// <summary>Anlatı modunda boşta/boot tek satırı: <c>▮ ready</c>'nin metin kısmı (dim).</summary>
    public const string Idle = "ready";

    /// <summary>Kanıt satırının "hiç" hâli — proje bu araçla bir kez bile başarıyla derlenmedi.</summary>
    public const string NeverBuilt = "Never built by this tool";

    /// <summary>Kart tıklandı, logu yok: gövdeye yazılacak satırlar (bir ya da iki).</summary>
    public static IReadOnlyList<string> ForEmptyLog(ProjectRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        // Derleniyor: kanıt henüz yok, akış birazdan gelir.
        if (row.State == ProjectRowState.Started) return [NoLog];
        string reason = Reason(row);
        return RepeatsReason(row) ? [reason] : [reason, Evidence(row)];
    }

    /// <summary>Kanıt satırı gerekçeyi TEKRAR ediyorsa yazılmaz: "hiç derlenmedi" iki kez söylenmez.</summary>
    private static bool RepeatsReason(ProjectRowViewModel row) =>
        string.IsNullOrEmpty(row.CurrentSha)
        && row.State == ProjectRowState.Pending
        && row.WillBuildReason == WillBuildReason.NeverBuilt;

    /// <summary>İlk satır: bu proje NEDEN bu durumda.</summary>
    private static string Reason(ProjectRowViewModel row) => row.State switch
    {
        // Motor bu koşuda bu projeyi atladı ve gerekçesini SÖYLEDİ (SkipReasons — tek doğruluk kaynağı).
        ProjectRowState.Skipped => row.SkipReason switch
        {
            SkipReasons.UpToDate => "Up to date — nothing to compile in this run.",
            SkipReasons.InDependencyCycle => InCycleText,
            SkipReasons.OutOfCycleScope => "Not needed by a dependency cycle — outside this run's scope.",
            SkipReasons.CycleNonConvergent => "The dependency cycle did not converge at this signature.",
            _ => "Skipped in this run.",
        },
        // Bunlar SAVUNMACIdır: derlenen bir proje her zaman log yazar. Log yine de yoksa (disk hatası, run
        // dizini silindi) sayfa boş kalmaz — ne olduğu söylenir.
        ProjectRowState.Succeeded => "Built in this run — its log is no longer on disk.",
        ProjectRowState.Failed => "Failed in this run — its log is no longer on disk.",
        _ => Pending(row),
    };

    /// <summary>Henüz bu koşuda konuşulmamış satır: elde plan vardır (will-build üç durumlu).</summary>
    private static string Pending(ProjectRowViewModel row)
    {
        // Döngü üyeliği plandan ÖNCE gelir: Sync bir SCC üyesine her zaman WillBuild=false verir (Build bir
        // döngüyü asla derlemez, ARCHITECTURE §7.4) — o "false"u "güncel" diye okumak yanlış olurdu.
        if (row.InCycle) return InCycleText;
        if (row.WillBuild is not { } willBuild)
            return "Not analysed yet — run Sync to see what this project will do.";
        if (!willBuild) return "Up to date — nothing to compile.";

        // Bir koşu uçuştaysa bu satır KUYRUKTADIR; değilse yalnız bir plandır.
        string head = row.IsRunActive ? "Queued" : "Will build";
        return row.WillBuildReason switch
        {
            WillBuildReason.NeverBuilt => $"{head} — this tool has never built it.",
            WillBuildReason.LastFailed => $"{head} — its last build failed.",
            WillBuildReason.DepIssue => $"{head} — its last success was linked against a failed dependency.",
            WillBuildReason.SignatureChanged => $"{head} — the signature changed since the last successful build.",
            _ => $"{head} in this run.",
        };
    }

    /// <summary>İkinci satır: elde ne var. Kaynak <see cref="ProjectRowViewModel.CurrentSha"/> — yani
    /// <c>BuildState.BuiltCommit</c>, projenin son BAŞARIYLA derlendiği commit. Kısaltma bir GÖRÜNTÜ kararıdır
    /// ve kartla aynı 7 haneyi kullanır.</summary>
    private static string Evidence(ProjectRowViewModel row) =>
        row.CurrentSha is { Length: > 0 } sha ? $"Last built {Short(sha)}" : NeverBuilt;

    private static string Short(string sha) => sha.Length <= 7 ? sha : sha[..7];

    /// <summary>Döngü üyeliği İKİ yoldan da aynı cümleyi verir (atlanmış üye / koşu öncesi üye) — kopya YASAK.</summary>
    private const string InCycleText = "In a dependency cycle — Build never compiles one; use Resolve cycles.";
}

/// <summary>
/// [T56/3a] Proje-log modu panel başlığındaki statü glyph'i + statü adı + statü rengi eşlemesi — design-v1
/// EN_STATUS ile birebir (Started→Building, Pending→Queued). Renkler token ANAHTARLARIdır (hardcode YASAK) —
/// başlık kontrolü DynamicResource ile çözer.
/// </summary>
public static class ConsoleStatus
{
    public static string Glyph(ProjectRowState state) => state switch
    {
        ProjectRowState.Succeeded => "✓",
        ProjectRowState.Failed => "✗",
        ProjectRowState.Skipped => "—",
        ProjectRowState.Started => "▸",
        _ => "•",
    };

    public static string Name(ProjectRowState state) => state switch
    {
        ProjectRowState.Succeeded => "Succeeded",
        ProjectRowState.Failed => "Failed",
        ProjectRowState.Skipped => "Skipped",
        ProjectRowState.Started => "Building",
        ProjectRowState.Pending => "Queued",
        _ => state.ToString(),
    };

    public static string BrushKey(ProjectRowState state) => state switch
    {
        ProjectRowState.Succeeded => "Brush.StatusSuccessText",
        ProjectRowState.Failed => "Brush.StatusFailText",
        ProjectRowState.Skipped => "Brush.StatusSkippedText",
        ProjectRowState.Started => "Brush.AmberText",
        ProjectRowState.Pending => "Brush.StatusQueuedText",
        _ => "Brush.TextSecondary",
    };
}
