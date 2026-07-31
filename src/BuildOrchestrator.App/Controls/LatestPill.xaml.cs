using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T59] `⌄ latest` pill görsel kontrolü — konsol + (ileride) event stream ORTAK kullanır (design-v1 §2.5/§2.6).
/// Görünürlük/tıklama davranışı host'a (ör. ConsoleView) aittir; bu control yalnız görsel + <see cref="Click"/>.
/// </summary>
public partial class LatestPill : UserControl
{
    // [feasibility §3.7] Paylaşılan/frozen token brush'ı DOĞRUDAN animate etmek YASAK (Storyboard tüm tüketicileri
    // etkiler) — hover geçişi için template-lokal KOPYA brush'lar üstünde ColorAnimation.
    private SolidColorBrush? _bg;
    private SolidColorBrush? _fg;

    public event RoutedEventHandler? Click;

    public LatestPill()
    {
        InitializeComponent();
        // ConsoleView.EnsureColorizer ile AYNI desen: template/brush kurulumu Loaded'a ERTELENİR — headless test
        // host'ta (Application yok, Loaded hiç ateşlenmez) ApplyTemplate/FindResource asla tetiklenmez, mevcut
        // ConsoleView testlerini (artık her ConsoleView bir LatestPill barındırıyor) riske atmaz.
        Loaded += (_, _) => EnsureLocalBrushes();
        PillButton.MouseEnter += (_, _) => SetHover(true);
        PillButton.MouseLeave += (_, _) => SetHover(false);
    }

    /// <summary>
    /// [A13/T5] Ekran-okuyucu adı. Pill'in ROLÜNÜ ("en sona git") kontrol bilir, ama HANGİ akışın sonuna
    /// gidildiğini (projeler / konsol / event stream) yalnız host bilir — bu yüzden metni host verir; desen,
    /// <c>ShellRoot</c>'un <c>DsSplitter</c>'lara ad vermesiyle AYNIdır. Metinler
    /// <see cref="AccessibilityNames"/>'tedir (çağırma yerinde literal YAZILMAZ).
    ///
    /// <para>Ad, kabuğa değil TIKLANAN öğeye (<see cref="PillButton"/>) konur: UIA ağacında buton öğesi odur,
    /// dıştaki <c>UserControl</c>'e verilen bir ad ekran okuyucuya butonun adı olarak ulaşmaz.</para>
    /// </summary>
    public string AccessibleName
    {
        get => AutomationProperties.GetName(PillButton);
        set => AutomationProperties.SetName(PillButton, value);
    }

    // ConsoleView.EnsureColorizer ile AYNI desen: headless test host'ta (Application/merged Tokens.xaml yok)
    // TryFindResource null döner — sessizce atlanır (kaynaklar üretimde Loaded'da hazırdır).
    private Brush? TryBrush(string key) => TryFindResource(key) as Brush;

    private void EnsureLocalBrushes()
    {
        if (_bg is not null) return;
        PillButton.ApplyTemplate();
        var root = PillButton.Template?.FindName("Root", PillButton) as Border;
        var label = PillButton.Template?.FindName("Label", PillButton) as TextBlock;
        var glyph = PillButton.Template?.FindName("Glyph", PillButton) as TextBlock;
        if (root is null || label is null || glyph is null) return; // template henüz uygulanmadı — SetHover'da tekrar denenir

        if (TryBrush("Brush.SurfaceOverlay") is not SolidColorBrush surfaceOverlay) return; // token kaynağı yok (headless) — no-op
        if (TryBrush("Brush.TextSecondary") is not SolidColorBrush textSecondary) return;

        _bg = new SolidColorBrush(surfaceOverlay.Color);
        _fg = new SolidColorBrush(textSecondary.Color);
        root.Background = _bg;
        label.Foreground = _fg;
        glyph.Foreground = _fg; // ikisi AYNI instance'ı paylaşır — birlikte animate olurlar
    }

    private void SetHover(bool hover)
    {
        EnsureLocalBrushes();
        if (_bg is null || _fg is null) return;
        if (TryBrush(hover ? "Brush.SurfaceRaised" : "Brush.SurfaceOverlay") is not SolidColorBrush bgBrush) return;
        if (TryBrush(hover ? "Brush.TextPrimary" : "Brush.TextSecondary") is not SolidColorBrush fgBrush) return;

        // [T60] Süre/eğri/AnimationsEnabled'ı TAZE okuyan tek geçiş yolu MotionTokens.TransitionColor'dır —
        // T59'da buradaki private AnimateOrSetColor onun BİREBİR aynısıydı; T60 DS kütüphanesi aynı şekle
        // ihtiyaç duyunca ORTAKLAŞTIRILDI (kopya YASAK, CLAUDE.md). Davranış değişmedi.
        MotionTokens.TransitionColor(this, _bg, bgBrush.Color);
        MotionTokens.TransitionColor(this, _fg, fgBrush.Color);
    }

    private void OnClick(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);
}
