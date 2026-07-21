using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Core.Formatting;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T53] design-v1 proje kartı (Views/ProjectRow, BuildApp.jsx:355-416): 7 slot + geometri. Kart GERÇEKTEN
/// kurulur (ekran dışı pencere + merge zinciri) — bir setter'ı okumak değeri şablona ulaştırdığını kanıtlamaz.
/// Headless'ta <c>App.Motion</c> null → animasyonlar INSTANT (nihai değerler sleep/poll olmadan görünür, D8).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ProjectRowTests
{
    private static (ProjectRow row, Window window, Border host) Realize(ProjectRowViewModel vm)
    {
        var host = DsResources.NewHost();
        var row = new ProjectRow { DataContext = vm };
        var window = DsResources.Realize(host, row);
        return (row, window, host);
    }

    [StaFact]
    public void Row_is_thirtysix_pixels_with_a_two_pixel_status_stripe_that_becomes_three_when_selected()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending);
        var (row, window, _) = Realize(vm);

        Assert.Equal(LayoutMetrics.DefaultRowHeight, ((Border)row.Content).Height); // 36 (sticky aritmetiği varsayar)
        Assert.Equal(2.0, row.Stripe.Width);

        vm.IsSelected = true;
        row.UpdateLayout();
        Assert.Equal(3.0, row.Stripe.Width);

        vm.IsSelected = false;
        row.UpdateLayout();
        Assert.Equal(2.0, row.Stripe.Width);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Dep_issue_slot_is_fourteen_pixels_even_when_empty_so_columns_never_shift()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded);
        var (row, window, _) = Realize(vm);

        // Boşken: slot 14px durur, ikon gizli.
        Assert.Equal(14.0, row.DepSlot.Width);
        Assert.Equal(Visibility.Collapsed, row.DepIcon.Visibility);

        // Doluyken: slot HÂLÂ 14px (hiza kaymaz), ikon görünür.
        vm.DepIssues = new[] { "OSYS.Sales.Core" };
        row.UpdateLayout();
        Assert.Equal(14.0, row.DepSlot.Width);
        Assert.Equal(Visibility.Visible, row.DepIcon.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Will_build_dot_is_amber_when_dirty_grey_when_clean_and_a_hollow_ring_when_unknown()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending) { WillBuild = true };
        var (row, window, host) = Realize(vm);
        var dot = DsResources.Descendants(row.Dot).OfType<Ellipse>().Single();

        // dirty → dolu amber (DS WillBuildDot, olduğu gibi tüketilir).
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotDirty"), DsResources.ColorOf(dot.Fill));
        Assert.Null(dot.Stroke);

        // clean → dolu gri, kontursuz.
        vm.WillBuild = false;
        row.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotClean"), DsResources.ColorOf(dot.Fill));
        Assert.Null(dot.Stroke);

        // unknown(null) → içi boş + halka. Halka fırçası kontrolün KENDİ kararıdır (Brush.DotOutline, hakemlik
        // bekleyen Ç-1) — kart onu EZMEZ, olduğu gibi tüketir.
        vm.WillBuild = null;
        row.UpdateLayout();
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotUnknown"), DsResources.ColorOf(dot.Fill));
        Assert.Equal(DsResources.TokenColor(host, "Brush.DotOutline"), DsResources.ColorOf(dot.Stroke));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Sha_pair_is_shown_only_for_dirty_rows_and_is_replaced_by_the_two_hover_icons()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Pending) { WillBuild = true, CurrentSha = "a3f81c2" };
        var (row, window, _) = Realize(vm);

        // dirty + hover yok → sha çifti görünür, aç-ikonları gizli.
        Assert.Equal(Visibility.Visible, row.ShaText.Visibility);
        Assert.Equal(Visibility.Collapsed, row.HoverIcons.Visibility);

        // hover → sha yerini folder + VS ikonlarına bırakır (aynı 118px blok).
        row.SimulateHover(true);
        Assert.Equal(Visibility.Collapsed, row.ShaText.Visibility);
        Assert.Equal(Visibility.Visible, row.HoverIcons.Visibility);

        // hover biter → yine sha.
        row.SimulateHover(false);
        Assert.Equal(Visibility.Visible, row.ShaText.Visibility);

        // clean/unknown satır → sha ASLA gösterilmez (yalnız WillBuild==true).
        vm.WillBuild = false;
        row.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, row.ShaText.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Duration_column_uses_the_shared_formatter_and_turns_red_on_failure()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded) { DurationMs = 4200 };
        var (row, window, host) = Realize(vm);

        // Paylaşılan DurationFormat (C2) — kart kendi biçimlemesini uydurmaz.
        Assert.Equal(DurationFormat.Duration(4200), row.DurationText.Text); // "4.2s"
        Assert.Equal(DsResources.TokenColor(host, "Brush.TextDim"), DsResources.ColorOf(row.DurationText.Foreground));

        // Failed → kırmızı (Brush.StatusFailText), metin yine paylaşılan biçimleyiciden.
        vm.State = ProjectRowState.Failed;
        row.UpdateLayout();
        Assert.Equal(DurationFormat.Duration(4200), row.DurationText.Text);
        Assert.Equal(DsResources.TokenColor(host, "Brush.StatusFailText"), DsResources.ColorOf(row.DurationText.Foreground));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Breathing_layer_only_exists_while_building_and_is_capped_at_thirty_fps()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Started);
        var (row, window, _) = Realize(vm);

        // Yalnız building iken katman VAR (görünür); durum building'i terk edince yok olur.
        Assert.Equal(Visibility.Visible, row.BreathLayer.Visibility);
        vm.State = ProjectRowState.Succeeded;
        row.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, row.BreathLayer.Visibility);

        // 30fps sınırı + 3.8s süre — kontrolün kullandığı AYNI fabrika.
        var anim = ProjectRow.BuildBreathingAnimation(row);
        Assert.Equal(30, Timeline.GetDesiredFrameRate(anim));
        Assert.Equal(TimeSpan.FromMilliseconds(3800), anim.KeyFrames[^1].KeyTime.TimeSpan);
        GC.KeepAlive(window);
    }
}
