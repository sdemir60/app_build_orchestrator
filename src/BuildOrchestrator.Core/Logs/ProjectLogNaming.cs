using System.Security.Cryptography;
using System.Text;

namespace BuildOrchestrator.Core.Logs;

public static class ProjectLogNaming
{
    /// projectId = csproj tam yolu (§4: Id anahtarı). Dosya adı: SHA256'nın ilk 16 hex'i.
    public static string FileNameFor(string projectId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(projectId.ToLowerInvariant()));
        return Convert.ToHexString(hash.AsSpan(0, 8)) + ".log";
    }
}
