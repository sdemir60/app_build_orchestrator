using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Core.Git;

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
/// (<see cref="Controls.StatusGlyph.LabelFor"/>). İlk satır NEDEN öyle olduğunu, ikinci satır elde ne olduğunu söyler
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
    /// <param name="now">Şimdi — göreli yaş için; testler sabit bir an verir (D8).</param>
    public static IReadOnlyList<string> ForEmptyLog(ProjectRowViewModel row, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(row);
        // Derleniyor: kanıt henüz yok, akış birazdan gelir.
        if (row.State == ProjectRowState.Started) return [NoLog];
        var at = now ?? DateTimeOffset.Now;
        string reason = Reason(row);
        return RepeatsReason(row) ? [reason] : [reason, Evidence(row, at)];
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
            SkipReasons.OutOfCycleScope => OutOfCycleScopeText,
            SkipReasons.CycleNonConvergent => "The dependency cycle did not converge at this signature.",
            // [final review — I1] Motor bu satırı GERÇEKTEN koşullu değerlendirdiği için atladı: sayfanın açılma
            // nedeni TAM OLARAK "hangi bağımlılık" sorusudur, genel "Skipped in this run." onu yutuyordu.
            // Cümle bekleyen satırınkiyle (aşağıdaki Pending dalı) AYNI kaynaktan gelir (kopya YASAK).
            // [Task 6 review round 1 — DÜZELTME] `row.Conditional` guard'ı KALDIRILDI: bu SkipReason'ı motor
            // yalnız `ConditionalRebuild.AppliesTo`nun (Core) o proje için TRUE dediği projeler için üretir —
            // Supervisor tarafında `TrySkipWhileDependencyStillFails` sadece `run.ConditionalIds` içindeki
            // projeler için çağrılır (`RunCoordinator.cs`), ve o küme AYNI `AppliesTo` çağrısından gelir — App'in
            // bu run'ın kendi önizlemesinden yazdığı `row.Conditional`'ın kaynağıyla BİREBİR aynı yer. Yani bu
            // dal her tetiklendiğinde `row.Conditional` zaten `true`'dur; guard hiçbir zaman farklı bir cevap
            // vermiyordu, yalnız Pending dalıyla tutarsız görünüyordu.
            SkipReasons.DependencyStillFailing => WaitingForDependencyReason(row),
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
        // [Task 2 review fix I-1] Resolve cycles'ta kapsam dışı bir satır motorun pre-skip'ini State'e TAŞIMAZ
        // (bkz. RunViewModel.OnProjectSkipped) — Pending kalır ama SkipReason'ı yine de taşır, tam da bu yüzden.
        // Bu kontrol İLK sırada: aksi halde satırın önizleme anında ZORLANMIŞ WillBuild=false'u (RunCoordinator.cs
        // — "amber 'derlenecek' noktası hemen ardından 'skipped' geçen satırda yalan söylemesin", tüm pre-skip
        // edilenler için, kapsam dışı da güncel de aynı yoldan geçer) aşağıdaki "Up to date" dalına düşer ve
        // GERÇEKTEN kirli ama kapsam dışı bir proje için yanlış konuşurdu. Metin Skipped dalındakiyle AYNI
        // sabiti okur (kopya YASAK) — motor konuşsa da konuşmasa da kullanıcı aynı cümleyi görür.
        if (row.SkipReason == SkipReasons.OutOfCycleScope) return OutOfCycleScopeText;
        // Döngü üyeliği plandan ÖNCE gelir: Sync bir SCC üyesine her zaman WillBuild=false verir (Build bir
        // döngüyü asla derlemez, ARCHITECTURE §7.4) — o "false"u "güncel" diye okumak yanlış olurdu.
        if (row.InCycle) return InCycleText;
        if (row.WillBuild is not { } willBuild)
            return "Not analysed yet — run Sync to see what this project will do.";
        if (!willBuild) return "Up to date — nothing to compile.";

        // Bir koşu uçuştaysa VE bu satır BU koşunun kendi kuyruğundaysa KUYRUKTADIR; değilse yalnız bir plandır.
        // [Task 1 review fix — I-2] Eskiden yalnız row.IsRunActive okurdu — genel plan bayrağının (WillBuild)
        // ait olduğu koşuyu bilmediği aynı kusur (kök neden A): tek proje koşusunda bayat bir komşu satır
        // grafta/listede Discovered iken burada "Queued" yazardı. Tek doğruluk kaynağı Status'tur (kopya YASAK) —
        // o zaten Pending dalında IsRunActive && InRunQueue'yu okur.
        string head = row.Status == GraphStatus.Queued ? "Queued" : "Will build";
        return row.WillBuildReason switch
        {
            WillBuildReason.NeverBuilt => $"{head} — this tool has never built it.",
            WillBuildReason.LastFailed => $"{head} — it failed at this source.",
            WillBuildReason.DepIssue => $"{head} — its last success was linked against a failed dependency.",
            WillBuildReason.SignatureChanged => $"{head} — the signature changed since the last successful build.",
            // [DEĞİŞEN KURAL — Task 6, design v1.20.0 §2.4] Eskiden bu dal yalnız row.Conditional iken (motor bu
            // koşuyu GERÇEKTEN bekletirken) devreye girerdi; Conditional=false (zorlanmış kapsam) genel "Will
            // build in this run." dalına düşerdi. DecisionLabel artık conditional'ı hiç okumadığı için (bkz. o
            // dosyanın sınıf özeti) burada da aynı ayrımı korumanın gerekçesi kalmadı: WaitingForDependency bir
            // disk olgusudur, kapsamın zorlayıp zorlamadığından bağımsız aynı cümleyi söyler. Metin artık
            // DecisionLabel'in Title'ından DEĞİL, RowWarning.WaitingForDependencyText'ten gelir (tek kaynak,
            // kopya YASAK — bu, uyarı üçgeninin TOOLTIP'i ile AYNI değildir, bkz. WaitingForDependencyReason'ın
            // kendi özeti).
            WillBuildReason.WaitingForDependency => WaitingForDependencyReason(row),
            _ => $"{head} in this run.",
        };
    }

    /// <summary>[Task 6 — design v1.20.0 §2.4] Bu satırın hem <c>Pending</c> hem <c>Skipped</c> dalı için TEK
    /// kaynak: <see cref="RowWarning.WaitingForDependencyText"/>, burada yalnız çağrılır ve konsol
    /// cümlelerinin ortak kuralı gereği sonuna nokta eklenir.
    ///
    /// <para><b>[Task 6 review round 1 — DÜZELTME]</b> Bu cümle uyarı üçgeninin tooltip'iyle (<see
    /// cref="RowWarning.For"/>) AYNI DEĞİLDİR — üçgen daraltılmış slot için kısaltır (<c>Dependency issue:
    /// Sales.Core +2</c>); bu metin tüm kökleri virgülle yazıp bekleme kuyruğunu ekler. Paylaştıkları TEK şey
    /// kök adlandırma dili (<see cref="RowWarning.DepIssuePrefix"/> + kısaltma) — ayrıntı
    /// <see cref="RowWarning.WaitingForDependencyText"/>'in kendi özetinde.</para></summary>
    private static string WaitingForDependencyReason(ProjectRowViewModel row)
    {
        string text = RowWarning.WaitingForDependencyText(row.DependencyRoots, row.NamePrefix);
        return text.EndsWith('.') ? text : text + ".";
    }

    /// <summary>
    /// İkinci satır: elde ne var — son BAŞARILI derlemenin zamanı ve o çıktıyı üreten revizyon. Kaynaklar
    /// <see cref="ProjectRowViewModel.LastBuiltAt"/> ve <see cref="ProjectRowViewModel.CurrentSha"/> (yani
    /// <c>BuildState</c>'in kendisi). Kısaltma ve yaş biçimi tek yerden gelir (kopya YASAK).
    /// </summary>
    private static string Evidence(ProjectRowViewModel row, DateTimeOffset now)
    {
        if (row.CurrentSha is not { Length: > 0 } revision) return NeverBuilt;

        string sha = RevisionText.Short(revision);
        return AgeFormat.Age(row.LastBuiltAt, now) is { } age
            ? $"Last successful build: {age} ago ({sha})"
            : $"Last successful build: {sha}";
    }

    /// <summary>Döngü üyeliği İKİ yoldan da aynı cümleyi verir (atlanmış üye / koşu öncesi üye) — kopya YASAK.</summary>
    private const string InCycleText = "In a dependency cycle — Build never compiles one; use Resolve cycles.";

    /// <summary>[Task 2 review fix I-1] Kapsam dışı bir satır İKİ yoldan da (motor konuştu / konuşmadı, bkz.
    /// <see cref="Reason"/>'ın Skipped ve Pending dalları) aynı cümleyi verir — kopya YASAK.</summary>
    private const string OutOfCycleScopeText = "Not needed by a dependency cycle — outside this run's scope.";
}
