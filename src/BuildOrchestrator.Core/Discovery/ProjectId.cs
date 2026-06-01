using System.Security.Cryptography;
using System.Text;

namespace BuildOrchestrator.Core.Discovery;

/// <summary>Helpers to produce stable project ids from file paths.</summary>
public static class ProjectId
{
    /// <summary>
    /// Stable, case-insensitive id derived from the normalized absolute project path.
    /// </summary>
    public static string FromPath(string projectPath)
    {
        var normalized = Normalize(projectPath);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        // 16 hex chars are plenty to avoid collisions for hundreds of projects.
        var sb = new StringBuilder(16);
        for (int i = 0; i < 8; i++)
            sb.Append(bytes[i].ToString("x2"));
        return sb.ToString();
    }

    public static string Normalize(string path)
        => Path.GetFullPath(path).TrimEnd('\\', '/').ToLowerInvariant();
}
