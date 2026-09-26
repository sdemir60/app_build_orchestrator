using BuildOrchestrator.App;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [fatura görünürlüğü] Resolve cycles tooltip'inin upstream faturası: bir Cycles koşusunun kapsamı üyelerle
/// bitmez (ARCHITECTURE §8.1 — üyeler + transitif upstream) ve KİRLİ upstream gruptan önce derlenir. Düğme
/// bunu söylemezse kullanıcı "cycle çözüyorum" sanırken görünmez bir Build faturası öder — yavaşlık şikâyetinin
/// ölçülen kaynaklarından biri. Saf string fonksiyonu; WPF YOK (tooltip'in pencerede yenilenmesi
/// <c>MaintenanceBoxTests</c>'in işidir).
/// </summary>
public class ResolveCyclesTooltipTests
{
    [Fact] // Kirli upstream VARKEN fatura tooltip'in sonuna " · N upstream to build first" olarak eklenir.
    public void the_tooltip_names_the_dirty_upstream_bill()
    {
        string text = AccessibilityNames.ResolveCyclesTooltip(2, 5, upstreamToBuild: 18);

        Assert.EndsWith(" · 18 upstream to build first", text, StringComparison.Ordinal);
        // İki sayılı hâlin gövdesi AYNEN korunur — fatura yeni bir cümle değil, mevcut cümlenin kuyruğudur.
        Assert.StartsWith(AccessibilityNames.ResolveCyclesTooltip(2, 5), text, StringComparison.Ordinal);
    }

    [Fact] // Upstream temizken (0) metin iki sayılı halin BİREBİR aynısıdır — sıfırlık fatura yazılmaz.
    public void a_clean_upstream_leaves_the_tooltip_unchanged()
    {
        Assert.Equal(
            AccessibilityNames.ResolveCyclesTooltip(1, 2),
            AccessibilityNames.ResolveCyclesTooltip(1, 2, upstreamToBuild: 0));
    }

    [Fact] // Döngü yokken düğme pasiftir ve gerekçesini söyler — upstream sayısı o cümleye bulaşmaz.
    public void no_cycles_keeps_the_disabled_wording_even_with_a_positive_upstream_count()
    {
        Assert.Equal(
            AccessibilityNames.ResolveCyclesTooltip(0, 0),
            AccessibilityNames.ResolveCyclesTooltip(0, 0, upstreamToBuild: 7));
    }
}
