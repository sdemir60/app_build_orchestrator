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
/// <item><b>Paralellik + mod</b> uygulama durumudur, örneklenmez, ANINDA güncellenir: pay <see cref="RunCounters.Building"/> (o an derlenen
/// proje sayısı), payda ve mod sözcüğü <i>hücrenin modu</i>.</item>
/// </list>
///
/// <para><b>Hücrenin modu (R2-R4).</b> Hücre koşu sürerken (<c>runStarted</c> → koşu sonu, <see cref="IsRunning"/>) KOŞUYU anlatır: tam
/// koşuda koşu BAŞINDAKİ mod — komutla giden (<see cref="CaptureStripRunMode"/>) — ve o modun tablo paralelliği; çip koşu ortasında dönse hücre
/// kıpırdamaz (işçi sayısı koşu başında sabittir), koşu bitince çipi izler. Boştayken ve <b>tek proje koşusunda</b> (<see cref="RunTargetId"/>
/// dolu — <c>WorkersReducedNote</c>'un kapısı) hücre çipi CANLI izler. Tooltip koşuda pay 0 iken de koşu metnidir (<c>0 of 4 …</c>);
/// boşta metni yalnız koşu yokken. Motorun koşu başındaki kırpma gerekçesi (<c>runStarted.WorkersReducedReason</c>) tam koşuda tooltip'e
/// <c>— reduced: …</c> olarak girer; payda yine modun tablo değeridir (pay zaten kırpılmış sayıyı aşamaz). CPU tooltip'inin cap'i
/// hücrenin moduyla aynıdır (koşuda koşu başındaki mod): koşu ortası değişim şeritte gösterilmez, konsol notu zaten yazar.</para>
///
/// <para><b>Pay koşu yokken 0'dır</b> — <see cref="Counters"/> orada yalan söyleyebilir: motor koşu ortasında ölürse satırlar hiç
/// bitirilmez, <c>Started</c> kalır. Kimse derlemiyorken "3/4" yazmak şeridi yanıltırdı.</para>
///
/// <para><b>Maliyet.</b> Hücre, girdileri değişmedikçe yeniden yazılmaz (<see cref="RefreshStrip"/>): <see cref="Counters"/> her satır
/// geçişinde değişir ama pay yalnız derleyen sayısı değişince değişir; metinler ve tooltip'ler o zaman yeniden kurulur.</para>
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

    /// <summary>Paralellik hücresinin paydası: <c>/4</c> — hücrenin modunun tablo paralelliği.</summary>
    [ObservableProperty] private string _stripAllowedText = "";

    /// <summary>Paralellik hücresinin mod sözcüğü: <c>Full</c> / <c>Balanced</c> / <c>Light</c> — hücrenin modu.</summary>
    [ObservableProperty] private string _stripModeText = "";

    /// <summary>Paralellik hücresinin tooltip'i: boşta / koşuda / koşuda ve kısılmışken üç hâl (<see cref="ResourceStripText.ParallelTooltip"/>).</summary>
    [ObservableProperty] private string _stripParallelTooltip = "";

    // ---------------------------------------------------------------- koşu hücresi

    /// <summary>Hücrenin son yazılan girdileri — aynısı yeniden gelirse metinler yeniden kurulmaz.</summary>
    private readonly record struct StripCell(CorePerfMode Mode, bool Running, int Building, string? ReducedReason);

    private StripCell? _stripCell;

    /// <summary><see cref="StartRunCommand"/>'a giden perf modu (yalnız TAM koşu): komut kurulduğu anda yazılır, <c>runStarted</c> alınca
    /// koşu hücresine devredilir. Satırdan tetiklenen (kapsamlı) koşuda <c>null</c> — o koşuda hücre çipi izler.</summary>
    private string? _stripSentMode;

    /// <summary>Koşu sürerken hücrenin modu: tam koşuda koşu başındaki mod; <see cref="IsRunning"/> düşünce bırakılır.</summary>
    private CorePerfMode? _stripRunMode;

    /// <summary>Motorun koşu başında bildirdiği işçi kırpma gerekçesi (<c>runStarted.WorkersReducedReason</c>); koşu bitince bırakılır.</summary>
    private string? _stripReducedReason;

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
    /// Koşu komutu kurulduğu anda çağrılır (<see cref="BeginRunAsync"/>): TAM koşunun perf modunu — komutla motora giden metni — yakalar;
    /// satırdan tetiklenen (kapsamlı) koşuda yakalanan yoktur. Komut koreografiden SONRA kurulur, yani yakalanan, motorun koşuyu başlattığı
    /// moddur: komut gittikten sonra (planlama penceresi) çip dönse de koşu yakalanan modla başlar.
    /// </summary>
    private void CaptureStripRunMode(StartRunCommand command) =>
        _stripSentMode = command.ScopeProjectId is null ? command.PerfMode : null;

    /// <summary>
    /// <c>runStarted</c> alındı (<see cref="OnRunStarted"/>, <see cref="IsRunning"/> yazılmadan ÖNCE): koşu hücresi kurulur — yakalanan mod
    /// (komutsuz başlayan bir koşuda çipin o anki modu; üretimde her koşu VM'in komutuyla başlar) ve motorun kırpma gerekçesi. Yakalanan mod
    /// tüketilir: bir sonraki koşu kendi komutuyla yeniden yakalar.
    /// </summary>
    private void BeginStripRun(RunStartedEvent e)
    {
        _stripRunMode = ModeFor(_stripSentMode ?? PerfMode);
        _stripSentMode = null;
        _stripReducedReason = e.WorkersReducedReason;
        RefreshStrip();
    }

    /// <summary><see cref="IsRunning"/> değişti: koşu bitince (her bitiş yolu — tamamlandı, durduruldu, hata, motor ölümü) koşuya ait durum
    /// bırakılır ve hücre çipi izler. Bırakmak gözlemlenebilir bir fark yaratmaz — alanlar yalnız <see cref="IsRunning"/> iken okunur ve her
    /// <c>runStarted</c>'ta (<see cref="BeginStripRun"/>) yeniden yazılır —; koşuya ait durumun koşuyla birlikte bitmesi yaşam döngüsünü açık tutar,
    /// koşu dışında okunabilir bayat bir mod bırakmaz.</summary>
    private void OnStripRunStateChanged(bool running)
    {
        if (!running)
        {
            _stripRunMode = null;
            _stripReducedReason = null;
        }
        RefreshStrip();
    }

    partial void OnPerfModeChanged(string value) => RefreshStrip();

    partial void OnCountersChanged(RunCounters value) => RefreshStrip();

    /// <summary>
    /// Paralellik hücresini ve CPU tooltip'ini girdilerinden yeniden kurar; girdiler son yazılanla aynıysa hiçbir şey yapmaz. Girdiler:
    /// koşu var mı (<see cref="IsRunning"/>), tam koşu mu (<see cref="RunTargetId"/> boş), koşu başındaki mod ve kırpma gerekçesi, çipin modu
    /// (<see cref="PerfMode"/>) ve derleyen sayısı (<see cref="Counters"/>). Hücrenin modu: tam koşuda koşu başındaki mod, aksi halde çip.
    /// </summary>
    private void RefreshStrip()
    {
        bool running = IsRunning;
        bool fullRun = running && RunTargetId is null;
        CorePerfMode mode = fullRun && _stripRunMode is { } started ? started : ModeFor(PerfMode);
        var cell = new StripCell(mode, running, running ? Counters.Building : 0, fullRun ? _stripReducedReason : null);
        if (_stripCell == cell) return;
        _stripCell = cell;

        StripBuildingText = cell.Building.ToString(CultureInfo.InvariantCulture);
        StripAllowedText = ResourceStripText.ParallelAllowed(mode);
        StripModeText = mode.ToString();
        StripParallelTooltip = ResourceStripText.ParallelTooltip(mode, cell.Building, running, cell.ReducedReason);
        StripCpuTooltip = ResourceStripText.CpuTooltip(AppIdentity.Product, mode);
    }
}
