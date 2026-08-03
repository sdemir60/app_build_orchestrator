using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [A13/T2 · 2.3] design-v1'in <b>KÜÇÜK</b> chip'i (height 20 · padding <c>0 6</c> · text-2xs) — <c>Ds.Chip</c>
/// stilinin ölçü override'ları. Prototipte AYNI üç override iki yerde geçer: şerit hata chip'leri
/// (<c>BuildApp.jsx:786</c>) ve <c>PROJECTS</c> başlığındaki filtre chip'i (<c>:1492</c>).
///
/// <para><b>Neden ayrı bir yer:</b> <see cref="Views.StickyRibbon"/> bu üçlüyü zaten taşıyordu ve 2.3 ikinci
/// bir kopyasını yazacaktı (kopya YASAK, CLAUDE.md). Yeni bir chip STİLİ İCAT EDİLMEZ — taban her zaman
/// <c>Ds.Chip</c>'tir (<see cref="Tests"/>: <c>DsControlTemplateTests</c> aktif görünümü pinler).</para>
/// </summary>
internal static class DsChipFactory
{
    /// <summary>Küçük chip yüksekliği — BuildApp.jsx:786 / :1492 <c>height: 20</c>.</summary>
    public const double SmallHeight = 20;

    /// <summary>Küçük chip yatay dolgusu — BuildApp.jsx:786 / :1492 <c>padding: '0 6px'</c>.</summary>
    public const double SmallPadding = 6;

    /// <summary>
    /// <c>Ds.Chip</c> tabanlı küçük bir chip. Stil <paramref name="owner"/>'ın kaynak zincirinden çözülür
    /// (headless testte de üretimdeki merge zinciriyle aynı yoldan gelir).
    /// </summary>
    /// <param name="foregroundKey">Verilirse chip metninin fırça token'ı (ör. şeritteki "+N more"un
    /// <c>Brush.StatusFailText</c>'i); null ise stilin kendi rengi korunur.</param>
    public static ToggleButton Small(FrameworkElement owner, object content, string? foregroundKey = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var chip = new ToggleButton
        {
            Content = content,
            Height = SmallHeight,
            Padding = new Thickness(SmallPadding, 0, SmallPadding, 0),
        };
        // [A13/T2 · 2.3] Stil DynamicResource olarak bağlanır, `TryFindResource` ile ANINDA çözülerek DEĞİL.
        // Ölçülen fark: chip bir kontrolün CTOR'unda kuruluyorsa öğe henüz hiçbir ağaçta değildir ve
        // `TryFindResource` yalnız `Application.Resources`'a düşerek çalışır — headless realize testinde
        // Application YOKTUR, stil sessizce UYGULANMAZ ve chip WPF'in varsayılan buton kromuyla çizilir
        // (#FFDDDDDD). SetResourceReference öğe ağaca girdiğinde çözer → iki ortamda da AYNI (T49 dersi).
        chip.SetResourceReference(FrameworkElement.StyleProperty, "Ds.Chip");
        chip.SetResourceReference(Control.FontSizeProperty, "FontSize.2xs");
        if (foregroundKey is not null) chip.SetResourceReference(Control.ForegroundProperty, foregroundKey);
        return chip;
    }
}
