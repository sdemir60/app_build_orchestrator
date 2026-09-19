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
/// </summary>
public static class GitOperationProbe
{
    public static GitOperation Inspect(string gitDir)
    {
        if (string.IsNullOrWhiteSpace(gitDir)) return GitOperation.None;

        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD"))) return GitOperation.Merge;
        if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) ||
            Directory.Exists(Path.Combine(gitDir, "rebase-apply"))) return GitOperation.Rebase;
        if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD"))) return GitOperation.CherryPick;
        if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD"))) return GitOperation.Revert;
        if (File.Exists(Path.Combine(gitDir, "index.lock"))) return GitOperation.CommandRunning;

        return GitOperation.None;
    }
}
