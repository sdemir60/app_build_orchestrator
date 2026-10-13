namespace BuildOrchestrator.Core.Git;

/// <summary>[Faz 2/T1] Gitdir'de o an sürmekte olan bir işlemin işareti — bkz. <see cref="GitOperationProbe"/>.</summary>
public enum GitOperation
{
    None,
    CommandRunning,
    Merge,
    Rebase,
    CherryPick,
    Revert,
}

/// <summary>
/// [Faz 2/T1] Gitdir altındaki işlem işaretlerini (dosya/klasör varlığı) okur — process YOK. Öncelik:
/// isim taşıyan işlem işaretleri (<c>MERGE_HEAD</c>, <c>rebase-merge</c>/<c>rebase-apply</c>,
/// <c>CHERRY_PICK_HEAD</c>, <c>REVERT_HEAD</c>) <c>index.lock</c>'tan ÖNCE kontrol edilir — bir merge
/// sırasında da kısa süreliğine <c>index.lock</c> görülebilir, bu durumda asıl işlem (Merge) raporlanmalı,
/// "sadece bir komut çalışıyor" (CommandRunning) değil.
/// <para>[reftable] Ref'leri tablolarda tutan repoda (<see cref="HeadReader.IsReftable"/>) <c>CHERRY_PICK_HEAD</c> ve
/// <c>REVERT_HEAD</c> tablodadır (<c>MERGE_HEAD</c>, rebase klasörleri ve <c>index.lock</c> orada da dosyadır). Yarıda kalan
/// bir cherry-pick ya da revert diske yalnız <c>MERGE_MSG</c>'yi bırakır — ama o dosya commit'siz bir cherry-pick'ten
/// (<c>-n</c>) ve çakışmalı bir <c>merge --squash</c>'tan da kalır, değişiklikler atılsa da durur. Bu yüzden dosya yalnız bir
/// İPUCUDUR: görülünce iki ref git'e sorulur (<see cref="GitService.HasRefAsync"/>) ve cevap normal repodakinin aynısıdır.
/// Cevap, <c>MERGE_MSG</c> değişmedikçe yeniden sorulmaz (<see cref="_answer"/>): bir yarım işlem boyunca git bir kez çalışır
/// — bu yol UI thread'inden çağrılır. Normal repoda tek başına bir <c>MERGE_MSG</c>'ye hiç bakılmaz.</para>
/// </summary>
public static class GitOperationProbe
{
    /// <summary>[reftable] Son sorulan <c>MERGE_MSG</c>'nin damgası (yol, yazım zamanı, uzunluk) ve git'in cevabı.</summary>
    private static (string Path, DateTime WrittenUtc, long Length, GitOperation? Operation)? _answer;

    private static readonly object AnswerGate = new();

    public static GitOperation Inspect(string gitDir)
    {
        if (string.IsNullOrWhiteSpace(gitDir)) return GitOperation.None;

        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))) return GitOperation.Merge;
        if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) ||
            Directory.Exists(Path.Combine(gitDir, "rebase-apply"))) return GitOperation.Rebase;
        if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))) return GitOperation.CherryPick;
        if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD"))) return GitOperation.Revert;
        if (ReftablePickOrRevert(gitDir) is { } pickOrRevert) return pickOrRevert;
        if (File.Exists(Path.Combine(gitDir, "index.lock"))) return GitOperation.CommandRunning;

        return GitOperation.None;
    }

    /// <summary>[reftable] Yarıda kalan cherry-pick ya da revert: <c>MERGE_MSG</c> duruyor (<c>MERGE_HEAD</c> yukarıda elendi)
    /// ve git'in tablosunda <c>CHERRY_PICK_HEAD</c> ya da <c>REVERT_HEAD</c> var. Normal repoda ve işaretsiz durumda
    /// <c>null</c>; IO istisnaları çağırana sızar (<see cref="Inspect"/>'in sözleşmesi).</summary>
    private static GitOperation? ReftablePickOrRevert(string gitDir)
    {
        string message = Path.Combine(gitDir, "MERGE_MSG");
        if (!File.Exists(message) || !HeadReader.IsReftable(gitDir)) return null;

        var stamp = new FileInfo(message);
        (string, DateTime, long) key = (message, stamp.LastWriteTimeUtc, stamp.Length);
        lock (AnswerGate)
        {
            if (_answer is { } known && (known.Path, known.WrittenUtc, known.Length) == key) return known.Operation;
        }

        GitOperation? operation = AskGit(gitDir);
        lock (AnswerGate) _answer = (key.Item1, key.Item2, key.Item3, operation);
        return operation;
    }

    /// <summary>İki ref'i git'e sorar — git dizininin içinden, thread-pool'da (çağıran UI thread'i bir senkronizasyon bağlamı
    /// taşır; onu async zincirle bekletmek kilitlenme olurdu). Git cevap veremezse işlem yok sayılır.</summary>
    private static GitOperation? AskGit(string gitDir) => Task.Run(async () =>
    {
        var git = GitService.InGitDirectory(gitDir);
        if (await git.HasRefAsync("CHERRY_PICK_HEAD").ConfigureAwait(false) is { Success: true, Value: true }) return GitOperation.CherryPick;
        if (await git.HasRefAsync("REVERT_HEAD").ConfigureAwait(false) is { Success: true, Value: true }) return GitOperation.Revert;
        return (GitOperation?)null;
    }).GetAwaiter().GetResult();
}
