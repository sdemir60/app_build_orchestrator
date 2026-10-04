using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests;

/// <summary>
/// [PERF Faz C/C2] Şemayı BİLEREK seçerek bir <c>evaluation-cache.json</c> yazar ve diskteki anahtarları okur —
/// "eski şemalı girdi" testlerinin ORTAK yardımcısı (kopya YASAK). Şema <c>null</c> ise <c>Schema</c> alanı HİÇ
/// yazılmaz: alandan önceki sürümlerin yazdığı biçim. Girdinin parmak izi varsayılan olarak uydurmadır (mtime 1,
/// uzunluk 1): bu dosyayı kuran testler girdileri <c>GetOrEvaluate</c> ile sormaz, yalnız budamanın ne yaptığına bakar.
/// Parmak izinin GERÇEK dosyayla eşleşmesi gereken test (isabet şartlarından yalnız şema tutmuyor) onu
/// <see cref="Entry"/> içinde verir.
/// </summary>
internal static class EvaluationCacheFile
{
    /// <summary>Bir girdi: csproj yolu, yazıldığı şema (<c>null</c> = <c>Schema</c> alanı hiç yazılmaz) ve parmak izi.</summary>
    public readonly record struct Entry(string Csproj, int? Schema, long MtimeTicks = 1, long Length = 1);

    public static void Write(string cachePath, params (string Csproj, int? Schema)[] entries) =>
        WriteEntries(cachePath, entries.Select(e => new Entry(e.Csproj, e.Schema)).ToArray());

    public static void WriteEntries(string cachePath, params Entry[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        var map = new Dictionary<string, Dictionary<string, object>>();
        foreach (var e in entries)
        {
            var entry = new Dictionary<string, object>
            {
                ["MtimeTicks"] = e.MtimeTicks,
                ["Length"] = e.Length,
                ["Hash"] = "deadbeef",
                ["Project"] = new EvaluatedProject(e.Csproj, "Fake", [], [], [], false),
            };
            if (e.Schema is { } s) entry["Schema"] = s;
            map[e.Csproj] = entry;
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
