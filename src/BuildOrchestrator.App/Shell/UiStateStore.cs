using System.IO;
using System.Text.Json;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [T62/T35] Pencere kabuğunun KALICI kullanıcı durumu (küçük ve sürüm-toleranslı bir JSON). Kabuk bayrakları
/// (K5 balloon, v7Δ-5 hotkey) + [T35] 2×2 yerleşim (mod + üç split) ve iş akışı tercihleri (repo/config/branch/
/// worktree/layer patterns/autostart). Eksik alan → varsayılan (JSON sürüm-toleranslıdır).
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

    // ---- İş akışı tercihleri (ileride Settings/action-bar task'larının bağlayacağı yüzey) ----
    public string? RepositoryRoot { get; set; }
    public string? Configuration { get; set; }
    public bool PerfMode { get; set; }
    public string? Branch { get; set; }
    public bool UseWorktree { get; set; }
    public string? WorktreeName { get; set; }
    public List<string> LayerPatterns { get; set; } = [];
    public bool Autostart { get; set; }
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
