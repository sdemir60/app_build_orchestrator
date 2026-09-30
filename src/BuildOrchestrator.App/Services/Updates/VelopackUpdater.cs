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
public sealed class VelopackUpdater(IUpdateSource source) : IAppUpdater
{
    private readonly Lazy<UpdateManager> _lazyManager = new(() => new UpdateManager(source));
    private UpdateInfo? _lastInfo; // DownloadAsync'in indireceği paket (CheckAsync'in bulduğu)

    private UpdateManager Manager => _lazyManager.Value;

    public bool IsInstalled => VelopackLocator.IsCurrentSet && Manager.IsInstalled;

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

    public void ApplyOnExit(UpdateCandidate candidate, bool restart)
    {
        var asset = Manager.UpdatePendingRestart ?? _lastInfo?.TargetFullRelease;
        Manager.WaitExitThenApplyUpdates(asset, silent: true, restart: restart);
    }

    /// <summary>İndirilecek olan: delta zinciri varsa toplamı, yoksa tam paket.</summary>
    internal static long DownloadBytes(long full, IReadOnlyList<long> deltas) => deltas.Count > 0 ? deltas.Sum() : full;

    /// <summary>Feed varlığını teklife çevirir. <c>SemanticVersion.ToString()</c> normalize biçimdir ("1.8.0"; ön sürüm
    /// etiketi varsa "1.9.0-beta.1"), notu olmayan paket boş nota döner.</summary>
    internal static UpdateCandidate ToCandidate(VelopackAsset asset, long bytes) =>
        new(asset.Version.ToString(), bytes, asset.NotesMarkdown ?? "");
}
