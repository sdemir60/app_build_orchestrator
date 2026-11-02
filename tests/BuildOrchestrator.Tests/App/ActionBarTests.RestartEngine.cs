using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [kullanıcı kararı 2026-11-02] "Restart engine" alt barda, Build'in (koşarken Stop'un) HEMEN SOLUNDA durur ve yalnız gerektiğinde
/// görünür: motor öldüğünde ya da sustuğunda, yeniden başlatmanın anlamı varken. Kurtarma eylemi kullanıcının baktığı yerde — az önce
/// bastığı Build'in yanındadır; şerit açıklama satırını tutar. Düğme etiketsiz ve karedir (en dar pencerede etiketli hâli sığmıyordu —
/// <see cref="With_restart_engine_showing_the_bar_still_fits_the_narrowest_window"/>): anlamı tooltip'te, ikonu şeridin satırının
/// renginde.
/// </summary>
public partial class ActionBarTests
{
    /// <summary>Barın Restart engine düğmesi — komutuyla bulunur (barda o komuta bağlı başka düğme yoktur).</summary>
    private static Button? RestartButton(ActionBar bar, RunViewModel vm) =>
        DsResources.RealizedObjects(bar).OfType<Button>().SingleOrDefault(b => ReferenceEquals(b.Command, vm.RestartEngineCommand));

    /// <summary>Düğmenin tek ikonunun konturu.</summary>
    private static Brush IconStroke(Button restart) =>
        DsResources.RealizedObjects(restart).OfType<System.Windows.Shapes.Path>().Single().Stroke;

    [StaFact]
    public void Restart_engine_appears_just_left_of_build_when_the_engine_dies()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm, atMinimumWindowWidth: true);
        var restart = RestartButton(bar, vm);
        Assert.NotNull(restart);
        Assert.Equal(Visibility.Collapsed, restart.Visibility); // motor sağlıklıyken yer kaplamaz

        vm.OnEngineExited(2);
        bar.UpdateLayout();

        Assert.Equal(Visibility.Visible, restart.Visibility);
        // Build alanının (Build split-button + koşarken Stop) HEMEN solunda, aynı grupta.
        var buildArea = (FrameworkElement)bar.Split.Parent;
        var group = Assert.IsType<StackPanel>(buildArea.Parent);
        var children = group.Children.Cast<UIElement>().ToList();
        Assert.Equal(children.IndexOf(buildArea) - 1, children.IndexOf(restart));
        // Etiketsiz: ekran okuyucu adı ve tooltip ne olduğunu söyler; ikon şeridin ölüm satırının kırmızısında.
        Assert.Equal("Restart engine", AutomationProperties.GetName(restart));
        Assert.Equal("Restart engine — start a fresh build engine; the current one stopped or is not answering", restart.ToolTip);
        Assert.Same(bar.FindResource("Brush.StatusFailText"), IconStroke(restart));
        GC.KeepAlive(window);
    }

    /// <summary>Kapı şeritteki iki satırla aynıdır: motor öldü ya da sustu — ve yeniden başlatmanın anlamı var. Supervisor dosyası hiç
    /// yoksa yeniden başlatmak dosyayı geri getirmez: düğme gizlenir, şerit "reinstall required" der. Sessizlik satırı amberdir, ikon da.</summary>
    [StaFact]
    public void Restart_engine_shows_while_the_engine_is_silent_and_never_when_a_restart_cannot_help()
    {
        var vm = NewVm();
        var (bar, window) = Realize(vm, atMinimumWindowWidth: true);
        var restart = RestartButton(bar, vm);
        Assert.NotNull(restart);

        vm.EngineOverdueMessage = RunViewModel.EngineSilentMessage;
        Assert.Equal(Visibility.Visible, restart.Visibility);
        Assert.Same(bar.FindResource("Brush.AmberText"), IconStroke(restart));

        vm.EngineOverdueMessage = null; // motor yeniden konuştu
        Assert.Equal(Visibility.Collapsed, restart.Visibility);

        vm.OnEngineUnavailable(@"C:\missing\BuildOrchestrator.Supervisor.exe");
        Assert.Equal(RunViewModel.EngineMissingMessage, vm.EngineDiedMessage); // ön-koşul: ölüm satırı var…
        Assert.Equal(Visibility.Collapsed, restart.Visibility);                 // …ama düğme yok
        GC.KeepAlive(window);
    }

    /// <summary>
    /// Düğme görünürken bar en dar pencerede (1240) yine sığar: sol grubun son öğesi sağ grubun başlangıcına taşmaz. Durumlar barın bugün
    /// sığan dolu hâlleridir — üç basamaklı sayaçlar ve görünen uyarı chip'i, yanında ya bir iş branch'inin adı (bu işin kendi branch'i)
    /// ya da <c>develop</c> ile <c>N behind</c> chip'i.
    /// <para>Neden ölçülüyor: barın sağ grubu genişliğini önce alır, sol grup kalanla yetinir — taşma sol grubu (sayaç chip'lerini)
    /// kırpar; üç etiketli bakım düğmesi bu yüzden barı taşırıyordu (ARCHITECTURE §13.2, bakım kutusu). Ölçüldü (bar 1226 px): etiketli
    /// "Restart engine" 132 px tutuyordu ve iş branch'iyle barı 50–86 px taşırıyordu; kare düğme 32 px tutar ve iki durum da sığar. Branch
    /// chip'inin bir genişlik sınırı yoktur: uzun bir branch adı ile <c>N behind</c> birlikte barı bu düğme olmadan da taşırır — o, bu
    /// testin değil chip'in konusudur.</para>
    /// </summary>
    [StaTheory]
    [InlineData("fix/triage-tray-engine-notice", 0)]
    [InlineData("develop", 12)]
    public void With_restart_engine_showing_the_bar_still_fits_the_narrowest_window(string branch, int behind)
    {
        var vm = NewVm();
        VmTopology.SeedCycle(vm, [.. Enumerable.Range(0, 150).Select(i => $@"C:\p\project{i}.csproj")]);
        vm.OnEvent(new BranchListEvent([new BranchRef(branch, "aaa", IsActive: true, IsRemoteTracking: false)]));
        vm.OnEvent(new SyncCompletedEvent(branch, "b7e91d4", FetchDegraded: false, 150, 0, Behind: behind));
        var (bar, window) = Realize(vm, atMinimumWindowWidth: true);
        // Bar en dar pencerenin genişliğine KENDİSİ sabitlenir: ekran dışı pencere masaüstünden geniş açılamaz ve CI runner'ının
        // küçük masaüstünde (1024) 1240 isteği kırpılıyordu — ölçüldü, bar 196 px dar kaldı. Üretimde en dar pencerede bar pencerenin
        // tam genişliğidir (çerçeve dolgusu yok — TitleBarClusterTests/ResourceStripRealizeTests aynı 1240'ı ölçer).
        bar.Width = (double)bar.FindResource("Size.WindowMinWidth");

        vm.OnEngineExited(2);
        bar.UpdateLayout();

        var restart = RestartButton(bar, vm);
        Assert.NotNull(restart);
        Assert.Equal(Visibility.Visible, restart.Visibility);
        Assert.Equal(bar.Width, bar.ActualWidth); // ön-koşul: ölçüm gerçekten en dar genişlikte
        Assert.Equal(behind > 0 ? Visibility.Visible : Visibility.Collapsed, bar.BehindChip.Visibility);
        Assert.Equal(Visibility.Visible, bar.WarnChip.Visibility);
        var rightGroup = (FrameworkElement)((FrameworkElement)bar.Split.Parent).Parent;
        var leftGroup = (Panel)((FrameworkElement)bar.SyncButton.Parent).Parent;
        var counters = (Panel)leftGroup.Children[^1];
        var lastChip = counters.Children.Cast<FrameworkElement>().Last(c => c.Visibility == Visibility.Visible);
        double leftEnd = lastChip.TransformToAncestor(bar).Transform(new Point(lastChip.ActualWidth, 0)).X;
        double rightStart = rightGroup.TransformToAncestor(bar).Transform(new Point(0, 0)).X;
        Assert.True(leftEnd <= rightStart, $"the counter chips end at {leftEnd:0.#} but the right group starts at {rightStart:0.#}");
        GC.KeepAlive(window);
    }
}
