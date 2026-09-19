namespace BuildOrchestrator.Core.Paths;

/// <summary>
/// [spec 2026-09-18 §1-1] Eski worktree havuzunun kökü. Worktree modu kalktı; araç bu klasörü artık ne yaratır
/// ne siler. Yolun TEK tanımı burada yaşar: klasör hâlâ diskteyse konsol bunu oturum başına bir kez söyler
/// (<see cref="Hint"/>); temizlik kullanıcınındır.
/// </summary>
public static class LegacyWorktreePool
{
    /// <summary><c>%LOCALAPPDATA%\BuildOrchestrator\worktrees</c> — eski sürümlerin havuzu kurduğu yer.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator", "worktrees");

    /// <summary>[Task 11] <paramref name="root"/> hâlâ diskteyse tek satırlık bir ipucu döner; yoksa
    /// <c>null</c> — App bunu motorun bu oturumdaki İLK hazır oluşunda konsola yazar (bkz.
    /// <c>RunViewModel.OnEngineReady</c>). Araç klasörü SİLMEZ, yalnız hatırlatır.</summary>
    public static string? Hint(string root) => Directory.Exists(root)
        ? $"The old worktree pool at {root} is no longer used — delete it to reclaim space, then run " +
          "`git worktree prune` in the repository"
        : null;
}
