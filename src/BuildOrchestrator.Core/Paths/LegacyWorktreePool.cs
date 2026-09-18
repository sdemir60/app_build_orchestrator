namespace BuildOrchestrator.Core.Paths;

/// <summary>
/// [spec 2026-09-18 §1-1] Eski worktree havuzunun kökü. Worktree modu kalktı; araç bu klasörü artık ne yaratır
/// ne siler. Yolun TEK tanımı burada yaşar: tanı ekranı (About → Paths) onu gösterir, kullanıcı elle
/// temizleyebilir.
/// </summary>
public static class LegacyWorktreePool
{
    /// <summary><c>%LOCALAPPDATA%\BuildOrchestrator\worktrees</c> — eski sürümlerin havuzu kurduğu yer.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator", "worktrees");
}
