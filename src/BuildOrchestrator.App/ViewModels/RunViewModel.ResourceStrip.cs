using System.Globalization;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
// VM'in KENDİ `PerfMode` string property'si, Core'daki aynı adlı enum'u basit-ad çözümlemesinde gölgeler (RunViewModel.cs'teki alias ile aynı gerekçe).
using CorePerfMode = BuildOrchestrator.Core.ProcessControl.PerfMode;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1, §3.6 · karar K3-K8 · plan R2-R5] <see cref="RunViewModel"/>'in <b>title bar kaynak şeridi</b> yüzeyi:
/// CPU · RAM · paralellik + mod. Şerit yalnız okur; bu dosya hücrelerin metinlerini üretir, görünümü (<c>ResourceStrip.xaml</c>) ve
/// örnekleyicinin kurulumu kabuktadır (MainWindow).
///
/// <list type="bullet">
/// <item><b>CPU ve RAM</b> örnekleyiciden gelir (<see cref="ApplyResourceSample"/>, 3 sn'de bir); sayıdan metne Core'dadır
/// (<see cref="ResourceStripText"/>). İlk örnek gelene dek hücreler okunamayan değerdir (<c>—</c>). CPU tooltip'i örnekten bağımsızdır, hücrenin
/// moduna bağlıdır.</item>
/// <item><b>Paralellik + mod</b> uygulama durumudur, örneklenmez, ANINDA güncellenir: <c>3/4/4 Balanced</c> — pay
/// <see cref="RunCounters.Building"/> (o an derlenen proje sayısı), ortadaki sayı motorun koşu başında belirlediği paralellik
/// (<c>runStarted.Parallelism</c> — modun sayısı, makineye göre kırpılmış olabilir; koşu yokken <c>/0</c>), sınır ve mod sözcüğü
/// <i>hücrenin modu</i>.</item>
/// </list>
///
/// <para><b>Hücre koşuyu anlatır (R2-R4).</b> Koşu sürerken (<c>runStarted</c> → koşu sonu, <see cref="IsRunning"/>) hücre motorun koşu
/// başındaki cevabını gösterir: koşu BAŞINDAKİ mod — komutla giden (<see cref="CaptureStripRunMode"/>) —, koşunun paralelliği ve kırpma
/// gerekçesi (<c>runStarted.WorkersReducedReason</c>, tooltip'e <c>— reduced: …</c> olarak girer). Çip koşu ortasında dönse hücre kıpırdamaz
/// (işçi sayısı koşu başında sabittir), koşu bitince çipi izler. Tam koşu ile satırdan başlatılan tek proje koşusu arasında fark YOKTUR.
/// Tooltip koşuda pay 0 iken de koşu metnidir (<c>0 of 4 …</c>, <c>of N</c> koşunun paralelliği); boşta metni yalnız koşu yokken. CPU
/// tooltip'inin cap'i hücrenin moduyla aynıdır (koşuda koşu başındaki mod): koşu ortası değişim şeritte gösterilmez, konsol notu zaten yazar.</para>
///
/// <para><b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-19]</b> ESKİ: hücre <c>pay/payda</c>'ydı, payda modun tablo değeriydi (kırpılmış koşu
/// <c>2/4</c>, tooltip <c>2 of 4 — reduced: …</c>) ve tek proje koşusu (R3, <see cref="RunTargetId"/> dolu) çipi CANLI izleyip "reduced"
/// demezdi. YENİ kural yukarıda; gerekçe: şeridin amacı koşu başında makineye göre belirlenen sayıyı göstermekti — <c>2/4</c> "makine kırptı"
/// ile "o an yalnız 2 proje hazırdı"yı ayırt edemiyordu. Ortadaki sayı koşu boyunca değişmediği için tek proje koşusu da koşu başında donar:
/// çipi izleseydi Light'a dönüşte <c>1/4/2 Light</c> okunurdu (koşunun sayısı sınırın üstünde).</para>
///
/// <para><b>Pay koşu yokken 0'dır</b> — <see cref="Counters"/> orada yalan söyleyebilir: motor koşu ortasında ölürse satırlar hiç
/// bitirilmez, <c>Started</c> kalır. Kimse derlemiyorken "3/4" yazmak şeridi yanıltırdı.</para>
///
/// <para><b>Maliyet.</b> Hücre, girdileri değişmedikçe yeniden yazılmaz (<see cref="RefreshStrip"/>): <see cref="Counters"/> her satır
/// geçişinde değişir ama pay yalnız derleyen sayısı değişince değişir; metinler ve tooltip'ler o zaman yeniden kurulur. Ortadaki sayı için
/// hiçbir şey örneklenmez ya da sorgulanmaz: <c>runStarted</c>'ın taşıdığı sayıdır.</para>
/// </summary>
public sealed partial class RunViewModel
{
    // ---------------------------------------------------------------- örnekten gelenler: CPU ve RAM

    /// <summary>CPU hücresinin metni (<c>69%</c> / <c>—</c>). İlk örneğe dek <c>—</c>.</summary>
    [ObservableProperty] private string _stripCpuText = ResourceStripText.Cpu(null);

    /// <summary>RAM hücresinin metni: App + motor + derleyiciler (<c>310 MB</c> / <c>4.8 GB</c> / <c>—</c>). İlk örneğe dek <c>—</c>.</summary>
    [ObservableProperty] private string _stripMemoryText = ResourceStripText.Memory(null);

    /// <summary>RAM hücresinin tooltip'i: üç parça ayrı ayrı; motor uyurken <c>engine 0 MB · compilers 0 MB</c>, okunamayan parça <c>—</c>.</summary>
    [ObservableProperty] private string _stripMemoryTooltip = ResourceStripText.MemoryTooltip(AppIdentity.Product, null, null, null);

    // ---------------------------------------------------------------- uygulama durumundan gelenler: paralellik hücresi ve CPU tooltip'i

    /// <summary>CPU hücresinin tooltip'i: ürün adı + <c>cap</c> (hücrenin modunun cap'i; Full'de ek yok).</summary>
    [ObservableProperty] private string _stripCpuTooltip = "";

    /// <summary>Paralellik hücresinin payı: o an derlenen proje sayısı (koşu yokken <c>0</c>).</summary>
    [ObservableProperty] private string _stripBuildingText = "";

    /// <summary>Paralellik hücresinin ortadaki sayısı: <c>/4</c> — motorun koşu başında belirlediği paralellik; koşu yokken <c>/0</c>.</summary>
    [ObservableProperty] private string _stripRunParallelismText = "";

    /// <summary>Paralellik hücresinin sınırı: <c>/4</c> — hücrenin modunun tablo paralelliği.</summary>
    [ObservableProperty] private string _stripAllowedText = "";

    /// <summary>Paralellik hücresinin mod sözcüğü: <c>Full</c> / <c>Balanced</c> / <c>Light</c> — hücrenin modu.</summary>
    [ObservableProperty] private string _stripModeText = "";

    /// <summary>Paralellik hücresinin tooltip'i: boşta / koşuda / koşuda ve kısılmışken üç hâl (<see cref="ResourceStripText.ParallelTooltip"/>).</summary>
    [ObservableProperty] private string _stripParallelTooltip = "";

    // ---------------------------------------------------------------- koşu hücresi

    /// <summary>Hücrenin son yazılan girdileri — aynısı yeniden gelirse metinler yeniden kurulmaz.</summary>
    private readonly record struct StripCell(CorePerfMode Mode, int Building, int? RunParallelism, string? ReducedReason);

    private StripCell? _stripCell;

    /// <summary><see cref="StartRunCommand"/>'a giden perf modu: komut kurulduğu anda yazılır, <c>runStarted</c> alınca koşu hücresine
    /// devredilir.</summary>
    private string? _stripSentMode;

    /// <summary>Koşunun şeritteki karşılığı: koşu başındaki mod, motorun koşu başında belirlediği paralellik (<c>runStarted.Parallelism</c>) ve
    /// kırpma gerekçesi (<c>runStarted.WorkersReducedReason</c>). <c>runStarted</c>'ta kurulur, koşu bitince bırakılır.</summary>
    private readonly record struct StripRun(CorePerfMode Mode, int Parallelism, string? ReducedReason);

    private StripRun? _stripRun;

    /// <summary>
    /// Örnekleyicinin yeni örneğini CPU ve RAM hücrelerine yazar (kabuk <c>ResourceSampler</c>'ın <c>publish</c>'ine bağlar). Okunamayan
    /// değer (<c>null</c>) <c>—</c> olur; RAM hücresi üç parçanın toplamıdır, biri okunamadıysa toplam da okunamamıştır
    /// (<see cref="ResourceSample.TotalBytes"/>).
    /// </summary>
    internal void ApplyResourceSample(ResourceSample sample)
    {
        StripCpuText = ResourceStripText.Cpu(sample.CpuPercent);
        StripMemoryText = ResourceStripText.Memory(sample.TotalBytes);
        StripMemoryTooltip = ResourceStripText.MemoryTooltip(AppIdentity.Product, sample.AppBytes, sample.EngineBytes, sample.CompilersBytes);
    }

    /// <summary>
    /// Koşu komutu kurulduğu anda çağrılır (<see cref="BeginRunAsync"/>): koşunun — tam ya da tek proje — perf modunu, komutla motora giden
    /// metni yakalar. Komut koreografiden SONRA kurulur, yani yakalanan, motorun koşuyu başlattığı moddur: komut gittikten sonra (planlama
    /// penceresi) çip dönse de koşu yakalanan modla başlar.
    /// </summary>
    private void CaptureStripRunMode(StartRunCommand command) => _stripSentMode = command.PerfMode;

    /// <summary>
    /// <c>runStarted</c> alındı (<see cref="OnRunStarted"/>, <see cref="IsRunning"/> yazılmadan ÖNCE): koşu hücresi kurulur — yakalanan mod
    /// (komutsuz başlayan bir koşuda çipin o anki modu; üretimde her koşu VM'in komutuyla başlar), motorun belirlediği paralellik ve kırpma
    /// gerekçesi. Yakalanan mod tüketilir: bir sonraki koşu kendi komutuyla yeniden yakalar.
    /// </summary>
    private void BeginStripRun(RunStartedEvent e)
    {
        _stripRun = new StripRun(ModeFor(_stripSentMode ?? PerfMode), e.Parallelism, e.WorkersReducedReason);
        _stripSentMode = null;
        RefreshStrip();
    }

    /// <summary><see cref="IsRunning"/> değişti: koşu bitince (her bitiş yolu — tamamlandı, durduruldu, hata, motor ölümü) koşuya ait durum
    /// bırakılır ve hücre çipi izler. Bırakmak gözlemlenebilir bir fark yaratmaz — alan yalnız <see cref="IsRunning"/> iken okunur ve her
    /// <c>runStarted</c>'ta (<see cref="BeginStripRun"/>) yeniden yazılır —; koşuya ait durumun koşuyla birlikte bitmesi yaşam döngüsünü açık tutar,
    /// koşu dışında okunabilir bayat bir mod ya da sayı bırakmaz.</summary>
    private void OnStripRunStateChanged(bool running)
    {
        if (!running) _stripRun = null;
        RefreshStrip();
    }

    partial void OnPerfModeChanged(string value) => RefreshStrip();

    partial void OnCountersChanged(RunCounters value) => RefreshStrip();

    /// <summary>
    /// Paralellik hücresini ve CPU tooltip'ini girdilerinden yeniden kurar; girdiler son yazılanla aynıysa hiçbir şey yapmaz. Girdiler:
    /// koşu var mı (<see cref="IsRunning"/>) ve koşunun karşılığı (mod, paralellik, kırpma gerekçesi), çipin modu (<see cref="PerfMode"/>) ve
    /// derleyen sayısı (<see cref="Counters"/>). Hücrenin modu: koşuda koşu başındaki mod, aksi halde çip.
    /// </summary>
    private void RefreshStrip()
    {
        StripRun? run = IsRunning ? _stripRun : null;
        CorePerfMode mode = run?.Mode ?? ModeFor(PerfMode);
        var cell = new StripCell(mode, run is null ? 0 : Counters.Building, run?.Parallelism, run?.ReducedReason);
        if (_stripCell == cell) return;
        _stripCell = cell;

        StripBuildingText = cell.Building.ToString(CultureInfo.InvariantCulture);
        StripRunParallelismText = ResourceStripText.ParallelRun(cell.RunParallelism);
        StripAllowedText = ResourceStripText.ParallelAllowed(mode);
        StripModeText = mode.ToString();
        StripParallelTooltip = ResourceStripText.ParallelTooltip(mode, cell.Building, cell.RunParallelism, cell.ReducedReason);
        StripCpuTooltip = ResourceStripText.CpuTooltip(AppIdentity.Product, mode);
    }
}
