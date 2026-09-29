using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62/T35] Pencere kabuğunun KALICI kullanıcı durumu (küçük ve sürüm-toleranslı bir JSON). Kabuk bayrakları
/// (K5 balloon, v7Δ-5 hotkey) + [T35] 2×2 yerleşim (mod + üç split) ve iş akışı tercihleri (repo/config/perf/
/// layer patterns/autostart). Eksik alan → varsayılan; tanınmayan alan (eski dosyalardaki <c>Branch</c>,
/// <c>UseWorktree</c>, <c>WorktreeName</c>) okunurken yok sayılır ve bir sonraki kayıtta yazılmaz.
/// </summary>
public sealed class UiState
{
    /// <summary>[K5] "X kapatmaz, tepsiye küçültür" bilgilendirmesi bir kez gösterildi mi.</summary>
    public bool TrayBalloonShown { get; set; }

    /// <summary>[v7Δ-5] Global kısayol jesti; ayrıştırılamazsa varsayılana düşülür.</summary>
    public string Hotkey { get; set; } = HotkeyBinding.DefaultGesture;

    // ---- [T35] 2×2 yerleşim (design-v1 BuildApp.jsx:1143 varsayılanları) ----
    /// <summary>[T35] Son görünüm modu (quad/list/focus).</summary>
    public LayoutMode LayoutMode { get; set; } = LayoutMode.Quad;
    /// <summary>[T35] Kolon split yüzdesi (sol kolon genişliği).</summary>
    public double ColPct { get; set; } = 50;
    /// <summary>[T35] Sol kolon satır split'i (graf/liste).</summary>
    public double LeftPct { get; set; } = 50;
    /// <summary>[T35] Sağ kolon satır split'i (konsol/stream).</summary>
    public double RightPct { get; set; } = 50;

    // ---- İş akışı tercihleri — Settings ve action bar yazar (SeenVersion'ı What's new yazar) ----
    public string? RepositoryRoot { get; set; }
    public string? Configuration { get; set; }

    /// <summary>[D6 fold — C2] Perf profili ("Full"/"Balanced"/"Light"); <c>null</c> ⇒ VM varsayılanı (Balanced/4).
    /// <b>Şema göçü:</b> bu alan eskiden <c>bool</c>'du. Diskte kalmış eski bir <c>bool</c> token'ı
    /// (<c>"PerfMode": false</c>) tüm <see cref="UiState.Load"/>'u DEVİRMEMELİ (aksi halde kalıcı yerleşim de bir
    /// kez sıfırlanırdı — startup wipe) → <see cref="LegacyTolerantStringConverter"/> onu sessizce <c>null</c>'a
    /// çözer; kalan alanlar korunur ve bir sonraki Save yeni (string) şemayı yazar.</summary>
    [JsonConverter(typeof(LegacyTolerantStringConverter))]
    public string? PerfMode { get; set; }

    /// <summary>[design v1.13.0] Kullanıcının What's new diyaloğunda (<c>NotesDialog</c>) EN SON gördüğü sürüm.
    /// Çalışan sürümden farklıysa başlık çubuğundaki ✨ What's new düğmesinde 5px amber bir nokta durur; diyalog
    /// açıldığı anda bu değer yazılır ve nokta söner. Prototipteki <c>localStorage delta-bo-seen-version-v1</c>'in
    /// karşılığıdır — kalıcı durumun tek yeri burasıdır.</summary>
    public string? SeenVersion { get; set; }

    /// <summary>[D7] Settings editörünün katman tanımları (Order/Regex/Name) — Save'de yazılır, startup'ta
    /// <see cref="ViewModels.RunViewModel.LayerPatterns"/>'a seed edilir.
    /// <para><b>Şema göçü:</b> bu alan eskiden <c>List&lt;string&gt;</c>'ti. D7, bu alanın İLK yazıcısıdır —
    /// diskteki değer BUGÜNE DEK hep <c>[]</c> (boş) kalmıştır (hiçbir kod yazmadı), bu yüzden şekil değişimi
    /// güvenlidir: boş bir JSON dizisi <c>[]</c> eleman tipinden bağımsız olarak boş
    /// <see cref="List{LayerPattern}"/>'e round-trip eder (startup wipe YOK). Diskte boş-OLMAYAN eski bir
    /// <c>List&lt;string&gt;</c> hiç var olmadı (PerfMode'daki gibi bir toleranslı converter GEREKMEZ).</para></summary>
    public List<LayerPattern> LayerPatterns { get; set; } = [];

    /// <summary>[design v1.14.0 §9] Settings editörünün EXTERNAL PROJECTS listesi (yalnız yol) — Save'de
    /// katmanlarla AYNI commit'te yazılır, startup'ta <see cref="ViewModels.RunViewModel.ExternalProjects"/>'e
    /// seed edilir ve her Sync/Build komutuyla motora gider. SIRA ANLAMLIDIR: çalışma kopyalarının güncellenme sırası tam olarak budur (build sırası
    /// bağımlılık kenarlarından gelir) —
    /// <see cref="LayerPatterns"/>'ın deseniyle AYNI, ayrı bir <c>order</c> alanı yazılmaz.
    /// <para>Setter null'ı boş listeye çevirir: diskte açıkça <c>"ExternalProjects": null</c> yazan bir dosya
    /// (elle düzenleme, yarım yazım) aksi halde koleksiyonu null bırakır ve ilk okuma tüm yerleşimi
    /// sıfırlardı — <see cref="LegacyTolerantStringConverter"/> ile aynı gerekçe.</para>
    /// <para><b>Şema göçü:</b> eleman eskiden bir <c>Vcs</c> alanı da taşıyordu (0=Git, 1=TFVC). TFVC kolu
    /// kaldırıldı; alan artık YAZILMAZ ve eski dosyalarda görülürse okunurken SESSİZCE yok sayılır (System.Text.Json
    /// varsayılanı: eşlenmeyen üye atlanır) — eski bir TFVC kartı sıradan bir git kartı olarak yüklenir ve
    /// çalışma kopyası bulunamazsa uyarı satırıyla olduğu gibi derlenir.</para></summary>
    public List<ExternalProject> ExternalProjects
    {
        get => _externalProjects;
        set => _externalProjects = value ?? [];
    }

    private List<ExternalProject> _externalProjects = [];

    /// <summary>[design v1.14.0 §9] Build, harici çalışma kopyalarını derlemeden ÖNCE kendi klonundan
    /// güncellesin mi (ff-only).
    /// <para><b>Alan NULLABLE ve varsayılanı "güncelle"dir.</b> <c>bool</c> olsaydı diskteki açık bir
    /// <c>null</c> token'ı (elle düzenleme, yarım yazım) <see cref="JsonUiStateStore.Load"/>'u düşürür ve TÜM
    /// yerleşimi sıfırlardı — <see cref="LegacyTolerantStringConverter"/> ile aynı gerekçe. Ayrıca "hiç
    /// yazılmamış" ile "false yazılmış" ayrımı burada taşınmak zorundadır: bayrak öncesi kaydedilmiş bir
    /// dosya, özelliğin bugünkü davranışını (güncelle) korumalıdır.</para></summary>
    public bool? UpdateExternals { get; set; }

    /// <summary>[spec 2026-09-18 §6.3] Branch chip'inden checkout'ta kirli ağaç stash'lenip geçilsin mi.
    /// <see cref="UpdateExternals"/> ile AYNI gerekçeyle NULLABLE (bayat bir <c>null</c> token'ı yerleşimi
    /// sıfırlamasın); yok ⇒ kapalı (MainWindow seed'i).</summary>
    public bool? StashOnBranchSwitch { get; set; }

    /// <summary>[P4] Settings → General → STARTUP → <c>Start with Windows</c>: uygulamanın tercihi — Windows oturumu
    /// açılınca başlasın mı. Her açılış bu tercihi Windows'un başlangıç kaydıyla hizalar
    /// (<see cref="Services.AutostartService.Apply"/>); Save kaydı anında yazar. <see cref="UpdateExternals"/> ile AYNI
    /// gerekçeyle NULLABLE (bayat bir <c>null</c> token'ı yerleşimi sıfırlamasın); yok ⇒ katalog varsayılanı (kapalı).
    /// Okuma yalnız <see cref="ShellSwitches.StartWithWindows"/> üzerindendir — bu alan doğrudan okunmaz.
    /// <para><b>Şema göçü:</b> alan eskiden <c>bool</c>'du; diskteki <c>true</c>/<c>false</c> aynen okunur.</para></summary>
    public bool? Autostart { get; set; }

    /// <summary>[P4] Settings → General → STARTUP → <c>Start minimized to tray</c>: Windows ile açılışta pencere
    /// gösterilmeden tepside mi başlansın. <see cref="Autostart"/> ile AYNI gerekçeyle NULLABLE; yok ⇒ katalog
    /// varsayılanı (kapalı: pencere açılır). Okuma yalnız <see cref="ShellSwitches.StartMinimizedToTray"/>
    /// üzerindendir.</summary>
    public bool? StartMinimizedToTray { get; set; }

    /// <summary>[P3] Settings → General → STARTUP → <c>Close to tray</c>: pencere kapatılınca uygulama tepsiye küçülüp
    /// arka planda mı kalsın (açık) yoksa tamamen mi kapansın (kapalı). <see cref="Autostart"/> ile AYNI gerekçeyle
    /// NULLABLE; yok ⇒ katalog varsayılanı (açık). Okuma yalnız <see cref="ShellSwitches.CloseToTray"/> üzerindendir —
    /// bu alan doğrudan okunmaz. Davranışı (pencere kapanışının kendisi) <c>MainWindow.OnClosing</c> bağlar
    /// (<see cref="WindowCloseRule"/>).</summary>
    public bool? CloseToTray { get; set; }

    /// <summary>[P3] Settings → General → NOTIFICATIONS → <c>Show notifications</c>: tepsinin OS balonları gösterilsin
    /// mi — koşu bitişi, ilk-× bilgilendirmesi ve ikinci-instance uyarısı; kapalıyken hiçbiri gösterilmez.
    /// <see cref="Autostart"/> ile AYNI gerekçeyle NULLABLE; yok ⇒ katalog varsayılanı (açık). Okuma yalnız
    /// <see cref="ShellSwitches.ShowNotifications"/> üzerindendir; kararı üç balon yolu verir.</summary>
    public bool? ShowNotifications { get; set; }
}

/// <summary>
/// [D6 fold] Bir <c>string?</c> alanı, eski şemadan kalan farklı-tipli bir token'a KARŞI toleranslı okur:
/// String → değer; Null/True/False/Number → <c>null</c> (eski <c>bool</c> PerfMode buradan geçer). Amaç: tek bir
/// bayat token'ın (ör. <c>"PerfMode": false</c>) <see cref="JsonUiStateStore.Load"/>'un TAMAMINI devirip kalıcı
/// yerleşimi de sıfırlamasını önlemek. Yazımda düz string/null davranır.
/// </summary>
internal sealed class LegacyTolerantStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.Null:
            case JsonTokenType.True:
            case JsonTokenType.False:
            case JsonTokenType.Number: return null; // eski bool / beklenmeyen skaler → null
            default: reader.Skip(); return null;    // (savunmacı) obje/dizi gelirse tüket ve null
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

public interface IUiStateStore
{
    UiState Load();
    void Save(UiState state);
}

/// <summary>
/// <c>%LOCALAPPDATA%\BuildOrchestrator\ui-state.json</c>. Okuma HER KOŞULDA bir durum döndürür: dosya yoksa,
/// bozuksa ya da okunamıyorsa varsayılanlar (uygulama başlatması bir tercih dosyası yüzünden ASLA patlamaz).
/// </summary>
public sealed class JsonUiStateStore(string path) : IUiStateStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BuildOrchestrator", "ui-state.json");

    public UiState Load()
    {
        try
        {
            if (!File.Exists(path)) return new UiState();
            return JsonSerializer.Deserialize<UiState>(File.ReadAllText(path), Options) ?? new UiState();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UiState();
        }
    }

    public void Save(UiState state)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(state, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tercih yazılamadı — balloon bir kez daha görünebilir; kapanış akışı bundan ETKİLENMEZ.
        }
    }
}

/// <summary>
/// [T62/K5] "X pencereyi kapatmaz, tepsiye küçültür" bilgilendirmesi YALNIZ ilk kapatmada gösterilir
/// (uygulama içi toast design §8'de yasak — OS tray balloon'u). Kapı: ilk çağrıda <c>true</c> döner ve bayrağı
/// KALICI olarak işaretler, sonraki her çağrıda <c>false</c>.
/// <para>[P3 · Task 4] Show notifications kapalıyken ilk kapatmada bile balon YOK — kapı bunu da sorar. Bu dalda
/// bayrak KAYDEDİLMEZ: switch sonradan açılırsa kullanıcı bilgilendirmeyi yine bir kez görür.</para>
/// </summary>
public sealed class FirstCloseBalloonGate(IUiStateStore store)
{
    public bool ClaimShow()
    {
        var state = store.Load();
        if (state.TrayBalloonShown) return false;
        if (!ShellSwitches.ShowNotifications(state)) return false;
        state.TrayBalloonShown = true;
        store.Save(state);
        return true;
    }
}
