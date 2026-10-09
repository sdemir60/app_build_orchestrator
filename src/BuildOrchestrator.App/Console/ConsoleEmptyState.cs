using BuildOrchestrator.App.Controls;
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
/// <para><b>Metin en çok İKİ satırdır: gerekçe + kanıt.</b> Statüyü tekrar etmez — onu başlık zaten söyler
/// (<see cref="Controls.StatusGlyph.RunLabelFor"/>). İlk satır NEDEN öyle olduğunu söyler; ikinci satır yalnız
/// çıktı bu aracın eseri değilse gelir (hiç derlenmedi / dışarıda derlendi). Derlenmekte olan bir projenin tek
/// satırı vardır: orada kanıt henüz oluşmamıştır, akış birazdan gelecektir.</para>
///
/// <para><b>[DEĞİŞEN KURAL]</b> Bu sınıf eskiden design-v1'in ÖRNEK metinlerini birebir taşıyordu
/// (<c>Skipped(sha)</c> / <c>Queued(deps)</c>) ve içlerinde uydurma veri vardı — "Last successful build:
/// yesterday 18:42". İkisi de üretimde HİÇ ÇAĞRILMIYORDU: yüzey kurulmuş ama hiçbir yere bağlanmamıştı, yani
/// pinlenen tek şey kullanılmayan bir literaldi. Yerine gerçek satır durumundan türeyen bu tablo geldi;
/// uydurma tarih/saat kaldırıldı (bkz. <see cref="Evidence"/>).</para>
/// </summary>
public static class ConsoleEmptyState
{
    /// <summary>Derlenmekte olan ama henüz tek satır üretmemiş proje — kanıt satırı YOKTUR.</summary>
    public const string NoLog = "No log yet — output streams here once the build starts.";

    /// <summary>Anlatı modunda boşta/boot tek satırı: <c>▮ ready</c>'nin metin kısmı (dim).</summary>
    public const string Idle = "ready";

    /// <summary>[design v1.8.0 §3.1] Workspace yokken prompt satırının metni (dim) — graf ve liste panelinin bekleme
    /// metinleriyle aynı dil (prototip <c>BuildApp.jsx</c> ConsolePanel, <c>!workspace</c> dalı).</summary>
    public const string NoWorkspace = "Waiting for a workspace";

    /// <summary>Kanıt satırının "hiç" hâli — proje bu araçla bir kez bile başarıyla derlenmedi.</summary>
    public const string NeverBuilt = "Never built by this tool";

    /// <summary>Kanıt satırının "bu araç derlemedi" hâli — çıktı var ve güncel, ama başka bir derlemenin
    /// eseri (<see cref="WillBuildReason.BuiltOutside"/>). Yalnız gerekçe satırı bunu SÖYLEMEDİĞİNDE yazılır
    /// (motor satırı güncel diye atladıysa, satır bir döngüdeyse ya da kapsam dışıysa); gerekçenin kendisi
    /// <see cref="BuiltOutsideReason"/> ise tekrar olurdu ve yazılmaz.</summary>
    private const string BuiltOutside = "Built outside this tool";

    /// <summary>Bekleyen bir satırın "derlenmeyecek, çünkü çıktısı dışarıda üretilmiş" gerekçesi — kanıt
    /// satırının tekrar kontrolü (<see cref="RepeatsReason"/>) bu cümleyi ADIYLA tanır.</summary>
    private const string BuiltOutsideReason = "Up to date — built outside this tool.";

    /// <summary>Kart tıklandı, logu yok: gövdeye yazılacak satırlar (bir ya da iki).</summary>
    public static IReadOnlyList<string> ForEmptyLog(ProjectRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        // Derleniyor: kanıt henüz yok, akış birazdan gelir.
        if (row.State == ProjectRowState.Started) return [NoLog];
        string reason = Reason(row);
        return RepeatsReason(row, reason) || Evidence(row) is not { } evidence ? [reason] : [reason, evidence];
    }

    /// <summary>Kanıt satırı gerekçeyi TEKRAR ediyorsa yazılmaz: "hiç derlenmedi" iki kez söylenmez.
    /// [Faz 3 — Task 7] <see cref="WillBuildReason.OutputMissing"/> de kapsanır — o da "bu araç bu projeye ait
    /// bir çıktı bilmiyor" der, kanıt satırı aynı şeyi tekrar eder.
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Kural <see cref="WillBuildReason.BuiltOutside"/>
    /// satırını da kapsar. Gerekçe cümlesinden yaş kalkınca ("built outside this tool: 5m ago") gerekçe ile
    /// kanıt kelimesi kelimesine aynı şeyi söyler oldu. Bu satırda kanıtın İKİNCİ bir hâli de yoktur:
    /// defterdeki kendi son başarısı (<see cref="ProjectRowViewModel.CurrentSha"/>) o çıktının kanıtı DEĞİLDİR
    /// — bu araç onu üretmedi — bu yüzden sha'ya bakılmaz.</para>
    ///
    /// <para>Kontrol gerekçe METNİ üzerinden yapılır, satır olgularından yeniden türetilmez: gerekçe satırının
    /// hangi dala düştüğünü (döngü üyeliği, kapsam dışılık, motorun atlama gerekçesi) yalnız
    /// <see cref="Reason"/> bilir; burada aynı koşulları kopyalamak iki yerin sessizce ayrışması demekti.</para></summary>
    private static bool RepeatsReason(ProjectRowViewModel row, string reason) =>
        reason == BuiltOutsideReason
        || (string.IsNullOrEmpty(row.CurrentSha)
            && row.State == ProjectRowState.Pending
            && row.WillBuildReason is WillBuildReason.NeverBuilt or WillBuildReason.OutputMissing);

    /// <summary>İlk satır: bu proje NEDEN bu durumda.</summary>
    private static string Reason(ProjectRowViewModel row) => row.State switch
    {
        // Motor bu koşuda bu projeyi atladı ve gerekçesini SÖYLEDİ (SkipReasons — tek doğruluk kaynağı).
        ProjectRowState.Skipped => row.SkipReason switch
        {
            SkipReasons.UpToDate => "Up to date — nothing to compile in this run.",
            SkipReasons.InDependencyCycle => InCycleText,
            SkipReasons.OutOfCycleScope => OutOfCycleScopeText,
            // [B1] Motor bu gerekçeyi hükmü verilmiş grupta bayat kalan TAŞINAN üyeye verir: derlenmedi, kaydı atıldı.
            SkipReasons.CycleNonConvergent =>
                "The dependency cycle did not converge at this signature; this project was not compiled and its record was discarded.",
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
        // Bu kontrol İLK sırada: aşağıdaki dallar satırı plan bayrağından (WillBuild) anlatır ve o bayrak bir
        // sonraki DÜZ Build'in cevabıdır — Resolve'un kendi önizlemesi onu yazmaz (RunViewModel.OnBuildPreview).
        // Kontrol olmasa sayfa kapsam dışı kirli bir projeye "Will build", güncel olana "Up to date" derdi; ikisi
        // de bu koşu hakkında değildir. Metin Skipped dalındakiyle AYNI sabiti okur (kopya YASAK) — motor konuşsa
        // da konuşmasa da kullanıcı aynı cümleyi görür.
        if (row.SkipReason == SkipReasons.OutOfCycleScope) return OutOfCycleScopeText;
        // [DEĞİŞEN KURAL — Build cycle derler] Döngü üyesi de plandan konuşur. Eskiden üyelik plandan ÖNCE gelirdi:
        // Sync bir SCC üyesine her zaman WillBuild=false verirdi ve o "false"u "güncel" diye okumak yanlış olurdu.
        // Sync artık üyeye Build'in kararıyla gerçek bir WillBuild verir (ARCHITECTURE §7.4, §8.1).
        if (row.WillBuild is not { } willBuild)
            return "Not analysed yet — run Sync to see what this project will do.";
        // [Faz 3 — spec 2026-09-18 §5.4, Task 7] BuiltOutside de "derlenmeyecek" bir disk olgusudur, ama genel
        // "nothing to compile" cümlesi NEDENİ söylemez (Sync'in kendi kararı mı, yoksa çıktı zaten dışarıdan mı
        // güncellendi?) — kullanıcı bu sayfayı tam da bunu sormak için açar.
        if (!willBuild)
            return row.WillBuildReason == WillBuildReason.BuiltOutside
                ? BuiltOutsideReason
                : "Up to date — nothing to compile.";

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
            // [Faz 3 — spec 2026-09-18 §5.3/§5.4, Task 7] Üç yeni gerekçe: çıktının kendisi diskte yok / bayat /
            // öğrenilmiş kopyası bozuk. Metinler claude-decisions.md'nin "Proje sayfası" kararlarıyla birebir.
            WillBuildReason.OutputMissing => $"{head} — its build output is missing.",
            WillBuildReason.OutputStale => row.OwnFilesChanged == true
                ? $"{head} — its files are newer than its build output."
                : $"{head} — a dependency's output is newer than its build output.",
            WillBuildReason.OutputReplaced => $"{head} — its copy in the shared folder does not match its build output.",
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
    /// İkinci satır: çıktı bu aracın eseri DEĞİLSE söylenir — hiç derlenmedi ya da dışarıda derlendi. Araç
    /// derlediyse satır yoktur (<c>null</c>).
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-21]</b> Araç derlediyse satır eskiden o çıktıyı
    /// üreten revizyonu yazardı (<c>Last successful build: a3f81c2</c>). Karar içerikten verilir, commit karara
    /// girmez; sha satırda durunca kararın commit'e bağlı olduğu sanıldı. <see cref="ProjectRowViewModel.CurrentSha"/>
    /// artık yalnız "bu araç hiç derledi mi" sorusu için okunur, değeri gösterilmez.</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-20]</b> Satır eskiden yaşı da söylerdi
    /// (<c>Last successful build: 2h ago (a3f81c2)</c>, <c>Built outside this tool: 5m ago</c>) — satırın karar
    /// etiketiyle AYNI yaş, iki yerde. Yaş her iki cümleden de kalktı (bkz. <see cref="DecisionLabel"/>'in sınıf
    /// özeti): biri yanıltıyordu, ikisi de gürültüsüne değmiyordu. Cümleler kaldı, zaman gitti — bu yüzden
    /// <c>BuiltOutside</c> dalı artık bir zaman damgası ARAMAZ (uydurma yaş riski kalmadı), gerekçe yeter.</para>
    /// </summary>
    private static string? Evidence(ProjectRowViewModel row)
    {
        // [Faz 3 — spec 2026-09-18 §5, P8, Task 7] BuiltOutside'ın kanıtı aracın KENDİ başarısı değil, dışarıdaki
        // derlemedir (row.CurrentSha bu satırda boş kalabilir — araç o çıktıyı üretmedi). Gerekçe satırı bunu
        // ZATEN söylediyse buraya hiç gelinmez (RepeatsReason); bu dal gerekçenin başka bir şey dediği satırlar
        // içindir — motor güncel diye atladı, satır bir döngüde ya da kapsam dışı.
        if (row.WillBuildReason == WillBuildReason.BuiltOutside) return BuiltOutside;

        return string.IsNullOrEmpty(row.CurrentSha) ? NeverBuilt : null;
    }

    /// <summary>Yalnız motorun <c>in dependency cycle</c> ile atladığı satır için (grup haritası olmayan koşu — üretimde
    /// erişilmez, bkz. <c>ReadySetScheduler</c>); plandan konuşan satır normal dallara düşer.
    /// <para>[DEĞİŞEN KURAL — Build cycle derler] Eski metin "Build never compiles one; use Resolve cycles." idi; Build
    /// kirli grubu derlediği için kalktı.</para></summary>
    private const string InCycleText = "In a dependency cycle — this run did not compile it.";

    /// <summary>[Task 2 review fix I-1] Kapsam dışı bir satır İKİ yoldan da (motor konuştu / konuşmadı, bkz.
    /// <see cref="Reason"/>'ın Skipped ve Pending dalları) aynı cümleyi verir — kopya YASAK.</summary>
    private const string OutOfCycleScopeText = "Not needed by a dependency cycle — outside this run's scope.";
}
