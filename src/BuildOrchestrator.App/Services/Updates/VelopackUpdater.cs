using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace BuildOrchestrator.App.Services.Updates;

/// <summary>[motor] <see cref="IAppUpdater"/>'ın Velopack uygulaması. <see cref="UpdateManager"/> feed'i okur, paketi
/// boyut + SHA ile doğrulayıp indirir (<c>ChecksumFailedException</c> → çağıran sessizce geçer) ve çıkışta
/// <c>Update.exe</c>'yi başlatır: <see cref="UpdateManager.WaitExitThenApplyUpdates"/> process'in düzgün çıkışını bekler
/// (60 s), <c>silent</c> — Velopack'in kendi penceresi gösterilmez (K6). <c>ApplyUpdatesAndRestart</c> KULLANILMAZ:
/// anında çıkar, <c>App.OnExit</c>'i (motorun düzgün kapanışını) atlar.
/// <para><see cref="UpdateManager"/> tembel kurulur: kurucusu process'in Velopack konumlayıcısını
/// (<c>VelopackApp.Build().Run()</c>, <c>Program.Main</c>'in ilk ifadesi) ister ve yoksa fırlatır. Test host'unda
/// <c>Main</c> hiç koşmadığından konumlayıcı yoktur; kurucu yerine <see cref="IsInstalled"/> ona bakıp <c>false</c> döner
/// — böylece kurulmamış kopya sarmalayıcıyı fırlatmadan kurar ve <c>UpdateService</c> kapısı çalışır.</para></summary>
public sealed class VelopackUpdater : IAppUpdater
{
    private readonly Lazy<UpdateManager> _lazyManager;
    private readonly bool _hasInjectedLocator;
    private UpdateInfo? _lastInfo; // DownloadAsync'in indireceği paket (CheckAsync'in bulduğu)

    public VelopackUpdater(IUpdateSource source) : this(source, locator: null) { }

    /// <param name="locator">Test dikişi: verilirse <see cref="UpdateManager"/> process-global konumlayıcı yerine bunu
    /// kullanır (Velopack'in kendi <c>TestVelopackLocator</c> önerisi) ve <c>IsCurrentSet</c> kapısı atlanır — enjekte
    /// konumlayıcı zaten vardır, kararı yalnız <c>Manager.IsInstalled</c> verir (üretimdeki durum). Üretimde <c>null</c>.</param>
    internal VelopackUpdater(IUpdateSource source, IVelopackLocator? locator)
    {
        _hasInjectedLocator = locator is not null;
        _lazyManager = new(() => new UpdateManager(source, null, locator));
    }

    private UpdateManager Manager => _lazyManager.Value;

    public bool IsInstalled => (_hasInjectedLocator || VelopackLocator.IsCurrentSet) && Manager.IsInstalled;

    public UpdateCandidate? PendingRestart =>
        IsInstalled && Manager.UpdatePendingRestart is { } asset ? ToCandidate(asset, asset.Size) : null;

    public async Task<UpdateCandidate?> CheckAsync(CancellationToken ct)
    {
        var info = await Manager.CheckForUpdatesAsync().ConfigureAwait(false);
        _lastInfo = info;
        if (info is null) return null;
        long bytes = DownloadBytes(info.TargetFullRelease.Size, info.DeltasToTarget.Select(d => d.Size).ToArray());
        return ToCandidate(info.TargetFullRelease, bytes);
    }

    public Task DownloadAsync(UpdateCandidate candidate, CancellationToken ct)
    {
        var info = _lastInfo ?? throw new InvalidOperationException("Download requested before a check found an update.");
        return Manager.DownloadUpdatesAsync(info, progress: null, ct);
    }

    /// <summary>Yalnız <paramref name="candidate"/> olan sürüm kurulur (<see cref="AssetToApply"/>); eşleşme yoksa hiçbir
    /// şey başlatılmaz. <c>WaitExitThenApplyUpdates(null)</c> "klasördeki en yeni paketi kur" demektir — UpdateService'in
    /// "hiç gösterilmemiş paket çıkışta kurulmaz" garantisi (yayım atan tur) aday yok sayılırsa bozulurdu.</summary>
    public void ApplyOnExit(UpdateCandidate candidate, bool restart)
    {
        if (AssetToApply(candidate, Manager.UpdatePendingRestart, _lastInfo?.TargetFullRelease) is not { } asset) return;
        Manager.WaitExitThenApplyUpdates(asset, silent: true, restart: restart);
    }

    /// <summary>Kurulacak varlık: önce diskteki (indirilmiş) paket, yoksa son kontrolün hedefi — ve yalnız o
    /// <paramref name="candidate"/>'in sürümüyse. Diskteki en yeni paket başka bir sürümse <paramref name="lastCheckTarget"/>
    /// aday olsa bile <c>null</c>: o paketin dosyası temizlenmiştir, Update.exe <c>--package</c> bulamayıp klasördeki en
    /// yeniyi (gösterilmemişi) kurardı.</summary>
    internal static VelopackAsset? AssetToApply(UpdateCandidate candidate, VelopackAsset? downloaded, VelopackAsset? lastCheckTarget)
    {
        var asset = downloaded ?? lastCheckTarget;
        return asset is not null && VersionOf(asset) == candidate.Version ? asset : null;
    }

    /// <summary>İndirilecek olan: delta zinciri varsa toplamı, yoksa tam paket.</summary>
    internal static long DownloadBytes(long full, IReadOnlyList<long> deltas) => deltas.Count > 0 ? deltas.Sum() : full;

    /// <summary>Feed varlığını teklife çevirir; notu olmayan paket boş nota döner.</summary>
    internal static UpdateCandidate ToCandidate(VelopackAsset asset, long bytes) =>
        new(VersionOf(asset), bytes, asset.NotesMarkdown ?? "");

    /// <summary>Varlığın sürüm dizgesinin TEK üretildiği yer (teklif ve kurulacak varlığın eşlenmesi aynı biçimi kullanır).
    /// <c>SemanticVersion.ToString()</c> normalize biçimdir: "1.8.0" (sıfır revizyon yazılmaz), ön sürüm etiketi varsa
    /// "1.9.0-beta.1", metadata atılır.</summary>
    private static string VersionOf(VelopackAsset asset) => asset.Version.ToString();
}
