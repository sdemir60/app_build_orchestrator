using System.Windows;
using System.Windows.Media;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T1 fix-1 · S1] Headless bir <see cref="GraphView"/> kurmanın TEK yeri.
///
/// <para><b>Neden var:</b> AYNI altı satırlık blok (üç <c>TestAssets</c> sözlüğünü ham
/// <c>XamlReader.Load</c> ile <c>view.Resources</c>'a merge etmek) altı ayrı test sınıfında kopyalanmıştı:
/// <c>GraphRenderTests</c>, <c>GraphCullTests</c>, <c>GraphRealizationPerfTests</c>,
/// <c>ReducedMotionCoverageTests</c>, <c>SuccessFlourishTests</c> ve (bu task'ta) <c>GraphClickTests</c>.
/// Kopya YASAK (CLAUDE.md) — ve <see cref="DsResources"/> bu kuralı zaten kendi özetinde yazıyor.</para>
///
/// <para><b>Yükleme neden <see cref="DsResources.Load"/> üzerinden:</b> ham <c>XamlReader.Load</c>, gevşek XAML'de
/// App'in KENDİ tiplerine yapılan <c>clr-namespace:</c> atıflarının assembly ile nitelenmesi düzeltmesini
/// ATLAR (bkz. <see cref="DsResources"/> özeti). Bugünkü üç sözlük o atıfları içermese de tek doğru yükleme
/// yolundan geçmek bu tuzağı kapatır.</para>
///
/// <para><b>Merge sırası</b> üretimdeki <c>App.xaml</c> zinciriyle hizalandı (Motion → Tokens → Icons).
/// Kopyalar iki farklı sıra kullanıyordu; ölçüldü: üç sözlüğün anahtar kümeleri AYRIK (7 / 103 / 79 anahtar,
/// sıfır kesişim), dolayısıyla sıra hiçbir değeri değiştirmez.</para>
/// </summary>
internal static class GraphTestView
{
    /// <summary>Üretimdeki <c>App.xaml</c> zincirinin graf için gereken bölümü (Controls.xaml graf tarafından
    /// tüketilmez — düğüm/kenar görselleri kod-tarafı kurulur).</summary>
    private static readonly string[] GraphMergeChain = ["Motion.xaml", "Tokens.xaml", "Icons.xaml"];

    /// <summary>
    /// Token/motion/ikon sözlükleri merge edilmiş bir <see cref="GraphView"/>. <c>pack://</c> ve
    /// <c>Application.Resources</c> headless'ta yoktur → <c>SetResourceReference</c> ile bağlanan fırçalar ve
    /// <c>Duration</c>/<c>KeySpline</c> token'ları ancak bu merge ile GERÇEKTEN çözülür.
    /// </summary>
    /// <param name="animationsEnabled">Motion sinyalinin taze okuma kapısı; verilmezse KAPALI (headless varsayılanı).</param>
    /// <param name="motion">Canlı <c>AnimationsEnabledChanged</c> kaynağı (latch-first — <c>Loaded</c>'dan ÖNCE atanır).</param>
    /// <param name="labelFontFamily">Etiket ailesi: <c>pack://</c> aileler headless'ta çözülmez, LOD/ölçüm
    /// isteyen testler <c>file://</c> tabanlı aileyi enjekte eder (üretimde bu seam ASLA set edilmez).</param>
    public static GraphView New(
        Func<bool>? animationsEnabled = null,
        IMotionSettings? motion = null,
        FontFamily? labelFontFamily = null)
    {
        var view = new GraphView
        {
            MotionSettings = motion,
            AnimationsEnabledProvider = animationsEnabled ?? (() => false),
        };
        if (labelFontFamily is not null) view.LabelFontFamily = labelFontFamily;
        foreach (string name in GraphMergeChain)
            view.Resources.MergedDictionaries.Add(DsResources.Load(name));
        return view;
    }

    /// <summary>Kurar + verilen boyutta ölçer/yerleştirir (HWND'siz ölçüm host'u — animasyon kapalıyken
    /// compositor saati gerekmez).</summary>
    public static GraphView Sized(
        Size size,
        Func<bool>? animationsEnabled = null,
        IMotionSettings? motion = null,
        FontFamily? labelFontFamily = null)
    {
        var view = New(animationsEnabled, motion, labelFontFamily);
        view.Measure(size);
        view.Arrange(new Rect(new Point(0, 0), size));
        return view;
    }

    /// <summary>Sized + <c>UpdateLayout</c>: cull/etiket/kamera kablajını gerçek yerleşimle test eden
    /// STA testlerinin kurulumu (GraphCullTests'in yerel Layout deseni; artık ortak).</summary>
    public static GraphView Realized(
        Size size,
        Func<bool>? animationsEnabled = null,
        IMotionSettings? motion = null,
        FontFamily? labelFontFamily = null)
    {
        var view = Sized(size, animationsEnabled, motion, labelFontFamily);
        view.UpdateLayout();
        return view;
    }
}
