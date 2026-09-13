using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.10.0 §2.9 · K5] Settings'in <b>dışa/içe aktarılan</b> dosya biçimi:
/// <c>{ app, version, repositoryRoot, externalProjects[{ path, vcs }], layers[{ name, pattern }] }</c> —
/// <c>externalProjects</c> BİLEREK <c>repositoryRoot</c> ile <c>layers</c> ARASINDADIR (design v1.14.0/§9),
/// hem burada hem sınıf içindeki alan bildirim sırasında (JSON çıktısını o sıra belirler). Dosyanın adı
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

    /// <summary>[K5 · design v1.14.0 §9] Harici proje listesi. <b>BİLDİRİM SIRASI BİLE İNÇTİR:</b> brief
    /// "repositoryRoot ile layers ARASINA externalProjects" der — System.Text.Json alanları BİLDİRİM
    /// sırasıyla yazar, bu yüzden bu özellik <see cref="Layers"/>'ın ÜSTÜNDE durmak ZORUNDADIR (aksi, dosyada
    /// yanlış sıra üretir; round-trip testi sırayı ayrıca pinler).
    /// <para><b>KASITLI OLARAK <c>null</c> BAŞLAR</b> (Layers'ın aksine bir <c>= []</c> başlatıcısı YOK):
    /// "dosyada anahtar hiç yok" (null) ile "anahtar var ama dizi BOŞ" (<c>[]</c>) ayrımı taşınmak ZORUNDADIR —
    /// <see cref="SettingsDraftViewModel.LoadFrom"/> yalnız BİRİNCİSİNDE mevcut taslağı korur. Eleman biçimi
    /// TOLERANSLIDIR: nesne (<c>{path}</c>) YA DA düz bir string (yalnız path) — bkz.
    /// <see cref="ExternalProjectListConverter"/>.</para></summary>
    [JsonPropertyName("externalProjects")]
    [JsonConverter(typeof(ExternalProjectListConverter))]
    public List<SettingsFileExternal>? ExternalProjects { get; set; }

    /// <summary>[design v1.15.0 §2.9] Harici çalışma kopyaları build'den önce güncellensin mi.
    /// <b>KASITLI OLARAK nullable:</b> "dosyada anahtar hiç yok" (null) ile "false yazılmış" ayrımı taşınmak
    /// ZORUNDADIR — <see cref="SettingsDraftViewModel.LoadFrom"/> yalnız BİRİNCİSİNDE taslağı korur.</summary>
    [JsonPropertyName("pullExternalBeforeBuild")] public bool? PullExternalBeforeBuild { get; set; }

    [JsonPropertyName("layers")] public List<SettingsFileLayer> Layers { get; set; } = [];

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Taslaktan dosyaya. <paramref name="externals"/> <c>null</c> geçilirse (eski 2-parametreli
    /// çağıranlar) <see cref="ExternalProjects"/> de <c>null</c> kalır — dışa aktarılan dosyada anahtar hiç
    /// YAZILMAZ (K5 ÖNCESİ davranışla birebir aynı, geriye dönük uyumlu). <see cref="SettingsDraftViewModel.ToFile"/>
    /// HER ZAMAN gerçek (boş olabilir ama null OLMAYAN) bir liste geçer — bu yüzden GERÇEK bir Export anahtarı
    /// hiç eksik BIRAKMAZ (§9: "yalnız boş olmayan path'ler").</summary>
    public static SettingsFile From(string? repositoryRoot, IReadOnlyList<LayerPattern> layers,
        IReadOnlyList<ExternalProject>? externals = null, bool? pullExternalBeforeBuild = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        return new SettingsFile
        {
            RepositoryRoot = repositoryRoot,
            PullExternalBeforeBuild = pullExternalBeforeBuild,
            // Sıra BİLEREK budur (RepositoryRoot → ExternalProjects → Layers): nesne başlatıcısının kendi
            // sırası JSON çıktısını ETKİLEMEZ (System.Text.Json BİLDİRİM sırasını yazar), ama okunurluk için
            // sınıftaki alan sırasıyla AYNI tutulur — iki sıra sessizce ayrışmasın.
            ExternalProjects = externals is null ? null
                : [.. externals.Select(e => new SettingsFileExternal { Path = e.Path })],
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

    /// <summary>Taslağa yüklenen dosyanın geri bildirimi (§2.9 · K5) — yeşil, 2.4 saniye. <c>· N external</c>
    /// parçası YALNIZ dosya <c>externalProjects</c> anahtarını TAŞIYORSA eklenir (<see cref="ExternalProjects"/>
    /// null DEĞİLSE — boş dizi DAHİL, prototip <c>BuildApp.jsx:1776</c>: <c>exts ? '· ' + exts.length + ' external' : ''</c>);
    /// anahtar hiç yoksa (eski/yalnız-katman dosyası) bu parça HİÇ görünmez.</summary>
    public string ImportedMessage()
    {
        string externalClause = ExternalProjects is { } ext
            ? string.Format(CultureInfo.InvariantCulture, " · {0} external", ext.Count)
            : "";
        return string.Format(CultureInfo.InvariantCulture, "Imported — {0} layers{1}{2}", Layers.Count,
            externalClause, string.IsNullOrWhiteSpace(RepositoryRoot) ? "" : " · root set");
    }

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

/// <summary>[K5 · design v1.14.0 §9] Dosyadaki tek harici proje: <c>{ path }</c>. Sıra dizinin KENDİ sırasıdır
/// (Layer'ın deseniyle AYNI, kopya YASAK — ayrı bir <c>order</c> alanı yazılmaz).
/// <para>[DEĞİŞEN KURAL] Eleman eskiden bir <c>vcs</c> alanı da taşıyordu (<c>"git"</c>/<c>"tfvc"</c>). TFVC
/// kolu kaldırıldı: anahtar artık YAZILMAZ, eski dosyalarda görülürse OKUNURKEN YOK SAYILIR — böylece dışa
/// aktarılmış eski bir ayar dosyası hâlâ yüklenebilir.</para></summary>
public sealed class SettingsFileExternal
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
}

/// <summary>[K5 · design v1.14.0 §9] <c>externalProjects</c> dizisinin TOLERANSLI okuyucusu — prototipin
/// <c>onFile</c>'ının (BuildApp.jsx:1765-1770) birebir portu: her eleman ya bir NESNE (<c>{path}</c>) ya da
/// DÜZ bir STRING (yalnız path) olabilir; boş (trim sonrası) path'ler ATLANIR. Nesnedeki tanınmayan alanlar
/// (eski dosyaların <c>vcs</c>'i dahil) yok sayılır.
///
/// <para><b>Neden düz POCO deserileştirme YETMEZ:</b> System.Text.Json bir dizi elemanı STRING iken hedef tip
/// bir SINIFSA <see cref="JsonException"/> fırlatır — <see cref="SettingsFile.TryParse"/> bunu yutar ve
/// GEÇERLİ bir dosya (§9'un açıkça izin verdiği düz-string biçimi) "Invalid settings file" olarak
/// reddedilirdi.</para></summary>
internal sealed class ExternalProjectListConverter : JsonConverter<List<SettingsFileExternal>>
{
    public override List<SettingsFileExternal>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType != JsonTokenType.StartArray) { reader.Skip(); return null; }

        var result = new List<SettingsFileExternal>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            string path = "";
            if (reader.TokenType == JsonTokenType.String)
            {
                path = reader.GetString() ?? ""; // düz string eleman — yalnız path (§9)
            }
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var element = JsonDocument.ParseValue(ref reader);
                var root = element.RootElement;
                if (root.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String) path = p.GetString() ?? "";
            }
            else
            {
                reader.Skip(); // beklenmeyen eleman biçimi (sayı/bool/null/dizi) — sessizce atla
                continue;
            }

            if (string.IsNullOrWhiteSpace(path)) continue; // boş path'ler düşer (§9)
            result.Add(new SettingsFileExternal { Path = path });
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<SettingsFileExternal>? value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value ?? [])
        {
            writer.WriteStartObject();
            writer.WriteString("path", item.Path);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
