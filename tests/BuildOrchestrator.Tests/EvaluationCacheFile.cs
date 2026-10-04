using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests;

/// <summary>
/// [PERF Faz C/C2] Şemayı BİLEREK seçerek bir <c>evaluation-cache.json</c> yazar ve diskteki anahtarları okur —
/// "eski şemalı girdi" testlerinin ORTAK yardımcısı (kopya YASAK). Şema <c>null</c> ise <c>Schema</c> alanı HİÇ
/// yazılmaz: alandan önceki sürümlerin yazdığı biçim. Girdinin parmak izi uydurmadır — bu dosyayı kuran test
/// girdileri <c>GetOrEvaluate</c> ile sormaz, yalnız budamanın ne yaptığına bakar.
/// </summary>
internal static class EvaluationCacheFile
{
    public static void Write(string cachePath, params (string Csproj, int? Schema)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var map = new Dictionary<string, Dictionary<string, object>>();
        foreach (var (csproj, schema) in entries)
        {
            var entry = new Dictionary<string, object>
            {
                ["MtimeTicks"] = 1L,
                ["Length"] = 1L,
                ["Hash"] = "deadbeef",
                ["Project"] = new EvaluatedProject(csproj, "Fake", [], [], [], false),
            };
            if (schema is { } s) entry["Schema"] = s;
            map[csproj] = entry;
        }
        File.WriteAllText(cachePath, JsonSerializer.Serialize(map));
    }

    /// <summary>Diskteki girdi anahtarları (csproj yolları), sıralı.</summary>
    public static string[] Keys(string cachePath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllBytes(cachePath));
        return doc.RootElement.EnumerateObject()
            .Select(p => p.Name)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
