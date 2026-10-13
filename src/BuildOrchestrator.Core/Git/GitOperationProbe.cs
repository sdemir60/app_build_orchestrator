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
/// <c>REVERT_HEAD</c> tablodadır; yarıda kalan bir cherry-pick ya da revert diske yalnız <c>MERGE_MSG</c>'yi bırakır
/// (<c>MERGE_HEAD</c>, rebase klasörleri ve <c>index.lock</c> orada da dosyadır). Hangisi olduğunu mesaj söyler: git'in revert
/// mesajı <see cref="RevertMarker"/> taşır. Commit'siz bir cherry-pick'in (<c>-n</c>) de bıraktığı iz budur — reftable'da o
/// da commit'lenene dek yarıda sayılır (normal repoda onun işareti yoktur). Kural yalnız reftable'da uygulanır: normal
/// repoda tek başına bir <c>MERGE_MSG</c> işlem değildir.</para>
/// </summary>
public static class GitOperationProbe
{
    /// <summary>[reftable] Git'in revert mesajının satırı — yarıdaki işlemi cherry-pick'ten ayırır.</summary>
    private const string RevertMarker = "This reverts commit ";

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

    /// <summary>[reftable] Yarıda kalan cherry-pick ya da revert: <c>MERGE_MSG</c> duruyor (ve <c>MERGE_HEAD</c> yok —
    /// yukarıda elendi). Normal repoda <c>null</c>; IO istisnaları çağırana sızar (<see cref="Inspect"/>'in sözleşmesi).</summary>
    private static GitOperation? ReftablePickOrRevert(string gitDir)
    {
        string message = Path.Combine(gitDir, "MERGE_MSG");
        if (!File.Exists(message) || !HeadReader.IsReftable(gitDir)) return null;
        return File.ReadAllText(message).Contains(RevertMarker, StringComparison.Ordinal)
            ? GitOperation.Revert
            : GitOperation.CherryPick;
    }
}
