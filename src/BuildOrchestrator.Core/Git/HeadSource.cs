using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [reftable] Kendiliğinden Sync'in HEAD okumasının TEK kararı: önce dosyadan (<see cref="HeadReader.Read"/> — process yok,
/// her tetikte bir dosya okuması kadar ucuz); <c>HEAD</c> dosyası reftable'ın yer tutucusuysa git'e sorarak
/// (<see cref="GitService.GetHeadStateAsync"/>). Normal (files) bir repoda yol değişmez: hiçbir process açılmaz.
/// <para>Git, git dizininin İÇİNDEN çağrılır (<see cref="GitService.InGitDirectory"/>). Process'ler thread-pool'da başlar:
/// çağıran kendiliğinden Sync'in UI thread'idir ve bir process'in başlatılması bile o thread'i bekletirdi.</para>
/// </summary>
public static class HeadSource
{
    /// <summary>HEAD'in branch'i ve commit'i; okunamazsa <c>null</c>. Dosya okumasının IO istisnaları çağırana sızar
    /// (<see cref="HeadReader.Read"/> ile aynı sözleşme).</summary>
    /// <param name="runner">Test dikişi; verilmezse gerçek <see cref="ProcessRunner"/>.</param>
    public static async Task<HeadState?> ReadAsync(string gitDir, IProcessRunner? runner = null, CancellationToken ct = default)
    {
        var fromFile = HeadReader.Read(gitDir);
        if (fromFile is not null || !HeadReader.IsReftable(gitDir)) return fromFile;
        return await Task.Run(() => GitService.InGitDirectory(gitDir, runner).GetHeadStateAsync(ct), ct).ConfigureAwait(false);
    }
}
