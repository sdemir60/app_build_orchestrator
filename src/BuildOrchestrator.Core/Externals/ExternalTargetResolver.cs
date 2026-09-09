namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// Harici proje eklenirken derlenecek hedefin önerilmesi. Öneri yalnız TEK bir aday varken verilir —
/// birden çok solution arasında tahmin yürütmek yanlış projeyi derlemeye yol açardı; o durumda seçim
/// kullanıcıya kalır.
/// </summary>
public static class ExternalTargetResolver
{
    /// <summary>
    /// <paramref name="projectPath"/> dizininde (alt dizinlere İNMEDEN) tam bir <c>*.sln</c> varsa tam
    /// yolunu, yoksa ya da birden çoksa <c>null</c> döner.
    /// </summary>
    public static string? AutoTarget(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath)) return null;

        try
        {
            string[] solutions = Directory.GetFiles(projectPath, "*.sln", SearchOption.TopDirectoryOnly);
            return solutions.Length == 1 ? solutions[0] : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // okunamayan dizin = öneri yok, kullanıcı elle seçer
        }
    }
}
