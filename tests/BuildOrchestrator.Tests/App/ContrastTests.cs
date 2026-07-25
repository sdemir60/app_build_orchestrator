using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E5/T68] WCAG kontrast denetimi: token renk çiftlerinin kontrast oranı hesaplanır (WCAG 2.1 §1.4.3
/// göreli-parlaklık formülü) ve eşik <b>≥4.5:1</b> pinlenir. Renkler TEK token sözlüğünden (Tokens.xaml)
/// OKUNUR — drift ederlerse test kırılır.
///
/// <para><b>GÖVDE metni (anlamlı bilgi taşıyan tonlar) ≥4.5:1</b> her uygulama yüzeyinde geçer: TextPrimary/
/// TextSecondary + statü-text tonları + accent buton metni.</para>
///
/// <para><b>Brush.TextDim/TextFaint = design-v1'in KASTEN SÖNÜK (de-emphasized) tonları</b> — <c>colors.css:22</c>
/// <c>--text-dim:#76767e</c> / <c>--text-faint:#54545c</c>. Bunlar "görünmez/incidental dekorasyon" DEĞİLdir:
/// design-v1 bunları LOW-EMPHASIS / DİNLENME-DURUMU metninde bilinçle kullanır — şeridin boot/stopped faz satırları
/// (<c>BuildApp.jsx:754/765</c> AÇIKÇA <c>text-dim</c>), sönük proje <c>sln</c>/<c>sha</c>, "no repository"/watermark
/// etiketleri. Aktif/önemli durumlar (Building, hata, birincil metin) ≥4.5 gövde tonlarını kullanır. Bu iki ton
/// design-v1 "renk BİREBİR" (bağlayıcı görsel otorite) gereği TAM bu değerlerde sabittir → 4.5 çubuğuna TABİ DEĞİL.
/// <b>Kullanıcı kararı (2026-07-25, E6 batch — RATIFY):</b> bu sönük dinlenme-durumu tonları için design fidelity
/// WCAG-AA'nın önünde gelir (SurfaceBase üstünde TextDim=4.28 / TextFaint=2.57 bilinçli, dokümanlı sub-AA istisna).
/// Değerler VEYA rol drift ederse aşağıdaki pinler kırılır ve a11y kararı BİLİNÇLİ olarak yeniden gözden geçirilir.
/// Token DÜZELTİLMEDİ: bir GÖVDE çifti eşiğin altına düşseydi düzeltilirdi — düşmedi.</para>
/// </summary>
public sealed class ContrastTests
{
    private const double AaNormalText = 4.5;

    private static readonly IReadOnlyDictionary<string, (double R, double G, double B)> Tokens = LoadOpaqueBrushes();

    // Uygulama yüzeyleri (metin bunların üstüne düşer).
    private static readonly string[] Surfaces =
        ["Brush.ConsoleBg", "Brush.SurfaceSunken", "Brush.SurfaceBase", "Brush.Surface", "Brush.SurfaceRaised", "Brush.SurfaceOverlay"];

    // GÖVDE / anlamlı metin tonları — her yüzeyde ≥4.5:1 olmalı.
    private static readonly string[] BodyTextTones =
    [
        "Brush.TextPrimary", "Brush.TextSecondary",
        "Brush.AmberText", "Brush.StatusSuccessText", "Brush.StatusFailText",
        "Brush.StatusCycleText", "Brush.StatusQueuedText", "Brush.StatusSkippedText",
    ];

    // design-v1'in KASTEN SÖNÜK (de-emphasized, dinlenme-durumu) tonları — 4.5 çubuğuna tabi DEĞİL (design-v1 renk
    // BİREBİR + kullanıcı-ratify 2026-07-25). "Dekoratif/incidental" DEĞİL: gerçek low-emphasis metinde kullanılır
    // (bkz. sınıf <summary>: şerit boot/stopped, sönük sln/sha, watermark).
    private static readonly string[] MutedTones = ["Brush.TextDim", "Brush.TextFaint"];

    [Fact]
    public void Body_and_status_text_meets_wcag_aa_on_every_app_surface()
    {
        var offenders = new List<string>();
        foreach (string text in BodyTextTones)
            foreach (string surface in Surfaces)
            {
                double ratio = Contrast(text, surface);
                if (ratio < AaNormalText)
                    offenders.Add($"{text} on {surface} = {ratio.ToString("N2", CultureInfo.InvariantCulture)}:1");
            }
        Assert.Empty(offenders);
    }

    [Fact]
    public void Accent_button_text_meets_wcag_aa_on_the_amber_fill()
    {
        // Amber butonların koyu metni (Brush.TextOnAccent) amber dolgu üstünde ≥4.5:1.
        Assert.True(Contrast("Brush.TextOnAccent", "Brush.Amber") >= AaNormalText);
        Assert.True(Contrast("Brush.TextOnAccent", "Brush.AmberBright") >= AaNormalText);
    }

    [Fact]
    public void The_known_faint_pair_is_a_documented_sub_threshold_decorative_exception()
    {
        // Brief'in "bilinen risk çifti"ni AÇIKÇA hesapla ve pinle: TextFaint-üstünde-SurfaceBase 4.5'in ALTINDA
        // (kasıtlı, de-emphasized). Bir gün gövde metnine terfi edilirse ya da token açılırsa bu pin kırılır
        // ve a11y kararı BİLİNÇLİ olarak yeniden gözden geçirilir.
        double faint = Contrast("Brush.TextFaint", "Brush.SurfaceBase");
        Assert.True(faint < AaNormalText,
            $"TextFaint-on-SurfaceBase artık {faint:N2}:1 — sönük-ton istisnası varsayımı geçersizleşti, a11y kararını gözden geçir.");
    }

    [Fact]
    public void The_muted_resting_state_tones_stay_below_the_body_bar_by_design()
    {
        // design-v1'in kasten-sönük tonları (TextDim şeridin boot/stopped status metninde — BuildApp.jsx:754/765;
        // TextFaint watermark/sönük sha) SurfaceBase üstünde gövde çubuğunun ALTINDADIR — design-v1 renk-birebir
        // gereği bilinçli (kullanıcı-ratify). Biri 4.5'i geçseydi aslında bir gövde tonu olurdu → pin kırılır.
        foreach (string tone in MutedTones)
            Assert.True(Contrast(tone, "Brush.SurfaceBase") < AaNormalText, $"{tone} beklenmedik şekilde gövde eşiğini geçti");
    }

    [Fact]
    public void The_guard_actually_loaded_the_token_colours_it_claims_to()
    {
        // Parse boş dönerse yukarıdaki testler SESSİZCE yeşil kalırdı (yol/regex bozulması).
        foreach (string key in BodyTextTones.Concat(MutedTones).Concat(Surfaces).Append("Brush.TextOnAccent").Append("Brush.Amber"))
            Assert.True(Tokens.ContainsKey(key), $"token bulunamadı: {key}");
    }

    // ---------------------------------------------------------------- WCAG hesabı
    private static double Contrast(string fgKey, string bgKey)
    {
        double l1 = RelativeLuminance(Tokens[fgKey]);
        double l2 = RelativeLuminance(Tokens[bgKey]);
        double hi = Math.Max(l1, l2), lo = Math.Min(l1, l2);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance((double R, double G, double B) c) =>
        0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(double srgb)
    {
        double c = srgb / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    private static IReadOnlyDictionary<string, (double, double, double)> LoadOpaqueBrushes()
    {
        string tokensXaml = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "Resources", "Tokens.xaml"));
        // Yalnız OPAK 6-hane SolidColorBrush'lar (metin/yüzey tonları hep opak) — alfa-önekli soft/border tonlar atlanır.
        var rx = new Regex("x:Key=\"(Brush\\.[^\"]+)\"\\s+Color=\"#([0-9a-fA-F]{6})\"", RegexOptions.Compiled);
        var map = new Dictionary<string, (double, double, double)>(StringComparer.Ordinal);
        foreach (Match m in rx.Matches(tokensXaml))
        {
            string hex = m.Groups[2].Value;
            map[m.Groups[1].Value] = (
                int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        return map;
    }
}
