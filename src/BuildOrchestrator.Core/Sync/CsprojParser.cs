using System.Xml.Linq;

namespace BuildOrchestrator.Core.Sync;

/// <summary>
/// Parses a .csproj file (SDK-style or legacy) to extract its <c>ProjectReference</c> targets.
/// Pure text/XML parsing — no MSBuild evaluation — so it is fast and cross-platform.
/// </summary>
public static class CsprojParser
{
    /// <summary>
    /// Returns absolute, normalized paths of referenced projects. Relative includes are resolved
    /// against the directory of <paramref name="projectPath"/>. Missing files are still returned
    /// (resolved by path) so dangling references can be reported.
    /// </summary>
    public static IReadOnlyList<string> GetProjectReferences(string projectPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(projectPath);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException)
        {
            return Array.Empty<string>();
        }

        var projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? ".";
        var results = new List<string>();

        // Handle both namespaced (legacy MSBuild) and SDK-style (no namespace) documents.
        foreach (var element in doc.Descendants())
        {
            if (!string.Equals(element.Name.LocalName, "ProjectReference", StringComparison.Ordinal))
            {
                continue;
            }

            var include = element.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }

            var normalized = include.Replace('\\', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(projectDir, normalized));
            results.Add(PathUtil.Normalize(full));
        }

        return results;
    }
}
