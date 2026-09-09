using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.10.0 §2.9] Settings'in <b>dışa/içe aktarılan</b> dosya biçimi:
/// <c>{ app, version, repositoryRoot, layers[{ name, pattern }] }</c>. Dosyanın adı
/// <see cref="FileName"/>'dir.
///
/// <para><b>Yalnız FORMU taşır.</b> Import bir ayarı UYGULAMAZ — değerleri diyaloğun taslağına yükler; hiçbir
/// şey <c>Save</c>'e basılana kadar yürürlüğe girmez (onay dialogu da yoktur). Bu yüzden tip Contracts'ta
/// değil App'tedir: bir IPC sözleşmesi değil, bir kullanıcı dosyasıdır.</para>
/// </summary>
public sealed class SettingsFile
{
    /// <summary>Dışa aktarılan dosyanın varsayılan adı (§2.9).</summary>
    public const string FileName = "build-orchestrator-settings.json";

    /// <summary>Dosya seçicinin filtresi — tek biçim, tek uzantı.</summary>
    public const string FileFilter = "Build Orchestrator settings (*.json)|*.json";

    /// <summary>Ürün kimliği — dosyanın hangi araca ait olduğunu söyler. Literal DEĞİL: tek kaynak
    /// <see cref="AppIdentity.Product"/> (kopya YASAK).</summary>
    [JsonPropertyName("app")] public string App { get; set; } = AppIdentity.Product;

    /// <summary>Yazan sürüm — ileride biçim değişirse okuyanın elinde bir tarih kalsın diye.</summary>
    [JsonPropertyName("version")] public string Version { get; set; } = AppIdentity.Version;

    [JsonPropertyName("repositoryRoot")] public string? RepositoryRoot { get; set; }

    [JsonPropertyName("layers")] public List<SettingsFileLayer> Layers { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Taslaktan dosyaya.</summary>
    public static SettingsFile From(string? repositoryRoot, IReadOnlyList<LayerPattern> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return new SettingsFile
        {
            RepositoryRoot = repositoryRoot,
            Layers = [.. layers.OrderBy(l => l.Order).Select(l => new SettingsFileLayer { Name = l.Name, Pattern = l.Regex })],
        };
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>Dosyadan taslağa. <b>Geçersiz dosya bir HATA DEĞİL bir sonuçtur</b>: kullanıcı yanlış dosyayı
    /// seçmiş olabilir ve diyalog bunu tek satırlık bir geri bildirimle söyler (fırlatmaz).</summary>
    public static SettingsFile? TryParse(string json)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<SettingsFile>(json, Options);
            // Boş bir JSON ("null"/"{}") biçim olarak geçerlidir ama İÇERİK taşımaz — kullanıcı için geçersizdir.
            if (parsed is null) return null;
            parsed.Layers ??= [];
            return parsed;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Taslağa yüklenen dosyanın geri bildirimi (§2.9) — yeşil, 2.4 saniye.</summary>
    public string ImportedMessage() => string.Format(CultureInfo.InvariantCulture,
        "Imported — {0} layers{1}", Layers.Count,
        string.IsNullOrWhiteSpace(RepositoryRoot) ? "" : " · root set");

    /// <summary>Export'un geri bildirimi (§2.9) — yeşil, 2.4 saniye. Diyalog (ince view) bu metni doğrudan
    /// kullanır, kendi başına kurmaz — dosya biçimiyle ilgili tüm kullanıcı metni burada toplanır
    /// (<see cref="ImportedMessage"/> ile aynı ilke, kopya YASAK, CLAUDE.md).
    ///
    /// <para><b>[DEĞİŞEN KURAL — design v1.13.1]</b> ESKİ metin kullanıcının SEÇTİĞİ gerçek dosya adını
    /// taşıyordu (ör. "Exported build-orchestrator-settings.json" — dosya seçicide adı değiştirirse metin de
    /// değişirdi). YENİ metin SABİTTİR ve dosya adından bağımsızdır: geri bildirim metinleri v1.13.1'de genel
    /// olarak kısaldı (aynı gerekçeyle <c>SettingsDialog</c>'daki Clear metinleri de kısaldı).</para></summary>
    public const string ExportedMessage = "Exported — settings JSON";
}

/// <summary>[design v1.10.0 §2.9] Dosyadaki tek katman: <c>{ name, pattern }</c>. Sıra dizinin KENDİ
/// sırasıdır — ayrı bir <c>order</c> alanı yazılmaz (iki doğruluk kaynağı olurdu).</summary>
public sealed class SettingsFileLayer
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("pattern")] public string Pattern { get; set; } = "";
}
