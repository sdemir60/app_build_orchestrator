using System.IO;
using System.Text.Json;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62] Pencere kabuğunun KALICI kullanıcı durumu (küçük ve sürüm-toleranslı bir JSON). Şimdilik yalnız iki
/// alan: ilk-X balloon bayrağı (K5) ve ayarlanabilir global kısayol (v7Δ-5). Tam Settings/config yüzeyi A10/T49
/// kapsamıdır — burada YAGNI.
/// </summary>
public sealed class UiState
{
    /// <summary>[K5] "X kapatmaz, tepsiye küçültür" bilgilendirmesi bir kez gösterildi mi.</summary>
    public bool TrayBalloonShown { get; set; }

    /// <summary>[v7Δ-5] Global kısayol jesti; ayrıştırılamazsa varsayılana düşülür.</summary>
    public string Hotkey { get; set; } = HotkeyBinding.DefaultGesture;
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
/// </summary>
public sealed class FirstCloseBalloonGate(IUiStateStore store)
{
    public bool ClaimShow()
    {
        var state = store.Load();
        if (state.TrayBalloonShown) return false;
        state.TrayBalloonShown = true;
        store.Save(state);
        return true;
    }
}
