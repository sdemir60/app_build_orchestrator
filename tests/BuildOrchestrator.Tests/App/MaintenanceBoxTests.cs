using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.7.0 §2.7-2] Action bar'ın BAKIM KUTUSU: chip ağırlığında TEK kutu (24px, surface-raised zemin,
/// 1px border, radius-xs, overflow hidden) ve içinde üç 28×22 ikon buton — Clean · Optimize · Resolve cycles —
/// aralarında 1px×14 ayraçla. Düğmelerde ETİKET YOKTUR: üç etiketli düğme barı 1240px minimumda taşırıyor
/// ve Build split-button'ı eziyordu (§2.7-2 gerekçesi); anlam tooltip'tedir.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class MaintenanceBoxTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    private static ProjectNode Node(string id, string name, int buildOrder) =>
        new(id, name, id, ["Osys"], [], buildOrder, null, null, false, null);

    private static (MaintenanceBox box, Window window) Realize(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var box = new MaintenanceBox { DataContext = vm };
        return (box, DsResources.Realize(host, box));
    }

    [StaFact]
    public void The_box_is_a_raised_bordered_strip_that_clips_its_children()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        var root = Assert.IsType<Border>(box.Content);
        Assert.Same(box.FindResource("Brush.SurfaceRaised"), root.Background);
        Assert.Same(box.FindResource("Brush.Border"), root.BorderBrush);
        Assert.Equal(new Thickness(1), root.BorderThickness);
        Assert.Equal(box.FindResource("Radius.Xs"), root.CornerRadius);
        Assert.Equal(24d, root.Height);
        // overflow:hidden — kutunun köşe yarıçapı içerideki düğmeleri KESMELİ (BuildApp.jsx:1931).
        Assert.True(root.ClipToBounds);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_box_orders_clean_then_optimize_then_resolve_with_hairline_separators_between_them()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        var strip = Assert.IsType<StackPanel>(Assert.IsType<Border>(box.Content).Child);
        var children = strip.Children.Cast<UIElement>().ToList();
        Assert.Equal(5, children.Count);
        Assert.Same(box.CleanButton, children[0]);
        Assert.Same(box.OptimizeButton, children[2]);
        Assert.Same(box.ResolveButton, children[4]);

        foreach (int i in new[] { 1, 3 })
        {
            var separator = Assert.IsType<Border>(children[i]);
            Assert.Equal(1d, separator.Width);
            Assert.Equal(14d, separator.Height);
            Assert.Same(box.FindResource("Brush.Border"), separator.Background);
        }

        foreach (var button in new[] { box.CleanButton, box.OptimizeButton, box.ResolveButton })
        {
            Assert.Equal(28d, button.Width);
            Assert.Equal(22d, button.Height);
        }
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL — clean] Eski iddia: "Clean ve Optimize'ın arka ucu yok, İKİSİ de kalıcı disabled"
    /// (karar 2026-08-13). Clean'in motoru artık VAR (<c>cleanWorkspace</c>) — düğme gerçek komuta bağlıdır
    /// ve enable'ı komutun <c>CanExecute</c>'undan gelir. Pin bu yüzden ikiye bölündü; Optimize'ınki de
    /// motoru yazılınca aynı şekilde yeniden yazıldı (bir sonraki test).
    /// <para>Repo kapısı KOMUTTADIR (ActionBar/Resolve deseni): kutu kendi enable hâlini yazmaz, iki yerden
    /// yazılan bir enable olmaz. ShowOnDisabled KORUNUR — düğme mid-run/mid-sync pasiftir ve kullanıcı
    /// NEDEN pasif olduğunu ancak tooltip'ten okuyabilir.</para></summary>
    [StaFact]
    public void Clean_is_wired_to_the_clean_command_and_its_tooltip_names_the_job()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.Same(vm.CleanCommand, box.CleanButton.Command);
        Assert.True(box.CleanButton.IsEnabled); // repo seçili (NewVm) → komut açık
        Assert.Equal("Clean — remove every project's bin/ and obj/ and reset the build state; "
                     + "the next build compiles everything from scratch", box.CleanButton.ToolTip);
        Assert.True(ToolTipService.GetShowOnDisabled(box.CleanButton));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL — optimize] Eski iddia: "Optimize'ın arka ucu yok, düğme KALICI pasiftir ve tooltip
    /// 'not available yet' der" (karar 2026-08-13). Motor yazıldı: düğme artık <c>OptimizeCommand</c>'e
    /// bağlıdır ve enable'ı komutun <c>CanExecute</c>'undan gelir — kutu kendi enable hâlini YAZMAZ.
    /// Tooltip de kapsamı anlatır; "rebuild the dependency index" vaadi DÜŞTÜ çünkü öyle bir adım yok.
    /// <para>Pasif kontrolde tooltip WPF'te varsayılan olarak GÖSTERİLMEZ; düğme mid-run/mid-sync pasif
    /// olacağı için ShowOnDisabled hâlâ pinlenir.</para>
    /// </summary>
    [StaFact]
    public void Optimize_is_wired_to_the_optimize_command_and_its_tooltip_names_the_job()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.Same(vm.OptimizeCommand, box.OptimizeButton.Command);
        Assert.True(box.OptimizeButton.IsEnabled); // repo seçili + motor sağlıklı → açık
        Assert.Equal("Optimize — restore missing NuGet packages, report references that restore cannot fix, "
                     + "clean stale obj leftovers and prune dead cache entries",
                     box.OptimizeButton.ToolTip);
        Assert.DoesNotContain("not available yet", (string)box.OptimizeButton.ToolTip, StringComparison.Ordinal);
        Assert.True(ToolTipService.GetShowOnDisabled(box.OptimizeButton));
        GC.KeepAlive(window);
    }

    /// <summary>[optimize] Düğmenin pasifliği komuttan gelir: repo yokken basılamaz, uçuşta bir Clean varken de.</summary>
    [StaFact]
    public void Optimize_is_disabled_without_a_repository_and_while_a_clean_is_in_flight()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1");
        var (box, window) = Realize(vm);
        Assert.False(box.OptimizeButton.IsEnabled); // repo yok

        vm.RootPath = @"D:\repo";
        Assert.True(box.OptimizeButton.IsEnabled);

        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));
        Assert.False(box.OptimizeButton.IsEnabled);
        GC.KeepAlive(window);
    }

    /// <summary>[clean] Düğmenin pasifliği komuttan gelir: repo yokken basılamaz, uçuşta bir Sync varken de.</summary>
    [StaFact]
    public void Clean_is_disabled_without_a_repository_and_while_a_sync_is_in_flight()
    {
        var vm = new RunViewModel(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1");
        var (box, window) = Realize(vm);
        Assert.False(box.CleanButton.IsEnabled); // repo yok

        vm.RootPath = @"D:\repo";
        Assert.True(box.CleanButton.IsEnabled);

        vm.OnEvent(new SyncStartedEvent(@"D:\repo", "main"));
        Assert.False(box.CleanButton.IsEnabled);

        GC.KeepAlive(window);
    }

    /// <summary>[design v1.7.0 §2.7-2] Resolve'un tooltip'i döngü VARKEN ne yapacağını üye sayısıyla anlatır,
    /// yokken neden pasif olduğunu söyler.
    /// <para><b>Tasarımdan bilinçli SAPMA (karar 2026-08-13):</b> prototip metni "in two passes" der; gerçek
    /// motor tur sayısını kendi belirler (<c>CycleRoundPolicy</c>: en az iki ardışık yeşil tur, tavan üç).
    /// Tooltip bu yüzden sabit bir sayı VAAT ETMEZ — "repeated passes" der, gerisi aynıdır.</para></summary>
    [StaFact]
    public void Resolve_tooltip_explains_the_run_when_cycles_exist_and_the_absence_otherwise()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.Equal("Resolve cycles — no dependency cycles detected", box.ResolveButton.ToolTip);

        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1), Node(@"C:\p\c.csproj", "C", 2)],
            [[@"C:\p\a.csproj", @"C:\p\b.csproj", @"C:\p\c.csproj"]], [], []));

        Assert.Equal("Resolve cycles — build the 3 cycle projects in repeated rounds: stale references first, "
                     + "then rebuild until they converge", box.ResolveButton.ToolTip);

        // [Task 6 · korunan kural] UIA adı HER İKİ durumda SABİTTİR — ekran okuyucu kontrolün İŞLEVİNİ
        // duyurur, gövde sayılarını değil.
        Assert.Equal(AccessibilityNames.ResolveCyclesButton, AutomationProperties.GetName(box.ResolveButton));
        GC.KeepAlive(window);
    }

    /// <summary>[Task 6 · korunan geliştirme] Kod tarafında sonradan eklenen grup sayısı bilgisi KORUNUR:
    /// tasarımın tek-sayılı cümlesi birden çok ayrı döngü olduğunda eksik kalıyordu ("5 proje" beş projelik
    /// TEK bir döngü sanılabilir). Ek yalnız gerçekten birden çok grup varken çıkar — tek gruplu (yaygın)
    /// durumda cümle tasarımdaki hâliyle kalır.</summary>
    [StaFact]
    public void Resolve_tooltip_names_the_group_count_only_when_there_is_more_than_one_cycle()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1),
             Node(@"C:\p\c.csproj", "C", 2), Node(@"C:\p\d.csproj", "D", 3)],
            [[@"C:\p\a.csproj", @"C:\p\b.csproj"], [@"C:\p\c.csproj", @"C:\p\d.csproj"]],
            [], []));

        Assert.Equal("Resolve cycles — build the 4 cycle projects in repeated rounds: stale references first, "
                     + "then rebuild until they converge (2 separate cycles)", box.ResolveButton.ToolTip);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.7.0 §2.7-2 · §3.7] Resolve düğmesi MEVCUT döngü koşusunu tetikler — yüzey yer
    /// değiştirdi, iş değişmedi. Repo kapısı düğmede, geri kalan her koşul (topoloji, döngü var mı, mid-run,
    /// motor sağlığı) komutun CanExecute'undadır: iki yerden yazılan bir enable hâli olmaz (ActionBar deseni).</summary>
    [StaFact]
    public void Resolve_is_wired_to_the_existing_cycle_run_command()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.Same(vm.BuildCyclesCommand, box.ResolveButton.Command);
        GC.KeepAlive(window);
    }

    /// <summary>[design v1.11.0 §2.7-2 · §3.7] Resolve'un ikonu HER ZAMAN NÖTRDÜR.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> v1.7.0'da ikon döngü varken cycle TURUNCUSUNA dönerdi ve gerekçesi
    /// "düğme tam da listede ve grafta turuncuyla işaretlenmiş projeleri derler, bağ görsel olarak kurulur"
    /// idi. v1.11.0 turuncuyu UI'dan tamamen çıkardı: o işaretin karşılığı artık yok (satırda tek amber üçgen
    /// kaldı), yani bağ kuracak bir renk de kalmadı. Döngünün varlığını düğmenin ENABLE durumu ve tooltip'i
    /// söyler — ikisi de aşağıda ve komşu testlerde pinli.</para></summary>
    [StaFact]
    public void The_resolve_icon_stays_neutral_because_orange_left_the_ui()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        Assert.Same(box.FindResource("Brush.TextSecondary"), box.ResolveIconBrush);

        vm.OnEvent(new WorkspaceTopologyEvent(
            [Node(@"C:\p\a.csproj", "A", 0), Node(@"C:\p\b.csproj", "B", 1)],
            [[@"C:\p\a.csproj", @"C:\p\b.csproj"]], [], []));

        Assert.Same(box.FindResource("Brush.TextSecondary"), box.ResolveIconBrush);
        // ...ama düğme ARTIK anlamlıdır: döngü var, tooltip de onu söylüyor.
        Assert.Equal(BuildOrchestrator.App.AccessibilityNames.ResolveCyclesTooltip(1, 2), box.ResolveButton.ToolTip);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- koşan iş: amber + spinner

    /// <summary>[design — BuildApp.jsx:2619-2622] Bir bakım işi koşarken KENDİ düğmesi DS'in <c>active</c>
    /// hâline geçer (amber-soft zemin) ve ikonun yerini dönen spinner alır. Düğme aynı anda <b>disabled</b>'dır
    /// (uçuşta ikinci bir Clean anlamsız), ama disabled'ın 0.45 sönüklüğü BASTIRILIR: koşan iş sönük değil
    /// CANLI görünmelidir — prototipte de koşan düğme disabled listesinin DIŞINDADIR.</summary>
    [StaFact]
    public void The_clean_button_spins_in_amber_while_its_own_work_runs()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);
        Assert.IsType<Viewbox>(box.CleanButton.Content); // ön-koşul: silgi ikonu

        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        var spinner = Assert.IsType<BuildOrchestrator.App.Controls.BuildingSpinner>(box.CleanButton.Content);
        Assert.Equal(12d, spinner.Size); // ikonla AYNI kutu (BuildApp.jsx:2622 size={12})
        Assert.Same(box.FindResource("Brush.AmberSoft"),
            BuildOrchestrator.App.Controls.DsTransition.GetAnimatedBackground(box.CleanButton));
        Assert.Equal(1d, box.CleanButton.Opacity);
        Assert.False(box.CleanButton.IsEnabled); // komut kapısı DEĞİŞMEZ, yalnız boyama değişir

        vm.OnEvent(new CleanCompletedEvent(1, 2, 3, 0, 1));

        Assert.IsType<Viewbox>(box.CleanButton.Content); // ikon geri gelir
        // Amber KALKAR: yerel deger temizlenince stil kendi varsayilanini (Transparent) yeniden uygular.
        Assert.NotSame(box.FindResource("Brush.AmberSoft"),
            BuildOrchestrator.App.Controls.DsTransition.GetAnimatedBackground(box.CleanButton));
        GC.KeepAlive(window);
    }

    /// <summary>Aynı muamele Resolve cycles için de geçerlidir (BuildApp.jsx:2639-2641) — kutunun içinde iki
    /// farklı davranış olmaz. Sinyal koşunun MODUDUR: sıradan bir Build spinner GÖSTERMEZ.</summary>
    [StaFact]
    public void The_resolve_button_spins_in_amber_while_a_cycles_run_is_in_flight()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.IsType<Viewbox>(box.ResolveButton.Content); // Build, Resolve'un işi DEĞİL
        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 10));

        vm.OnEvent(new RunStartedEvent("r2", RunMode.Cycles, 1, 1, "Debug", 0));

        Assert.IsType<BuildOrchestrator.App.Controls.BuildingSpinner>(box.ResolveButton.Content);
        Assert.Same(box.FindResource("Brush.AmberSoft"),
            BuildOrchestrator.App.Controls.DsTransition.GetAnimatedBackground(box.ResolveButton));

        vm.OnEvent(new RunCompletedEvent("r2", RunOutcome.Completed, 1, 0, 0, 0, 10));

        Assert.IsType<Viewbox>(box.ResolveButton.Content);
        GC.KeepAlive(window);
    }

    /// <summary>Amber YALNIZ işi koşan düğmededir: kutudaki öteki düğmeler sıradan disabled görünümünde kalır
    /// (sönük, ikonlu). Aksi halde "hangi iş koşuyor" sorusu kutuya bakılarak cevaplanamazdı.</summary>
    [StaFact]
    public void Only_the_button_whose_work_runs_goes_amber()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);

        vm.OnEvent(new CleanStartedEvent(@"D:\repo"));

        Assert.IsType<Viewbox>(box.ResolveButton.Content);
        Assert.NotSame(box.FindResource("Brush.AmberSoft"),
            BuildOrchestrator.App.Controls.DsTransition.GetAnimatedBackground(box.ResolveButton));
        Assert.IsType<Viewbox>(box.OptimizeButton.Content);
        Assert.NotSame(box.FindResource("Brush.AmberSoft"),
            BuildOrchestrator.App.Controls.DsTransition.GetAnimatedBackground(box.OptimizeButton));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [DEĞİŞEN KURAL — optimize] Kutunun doc'u eskiden "koşan iş amber olur, Optimize'ın gösterecek bir işi
    /// YOKTUR" diyordu; motoru yazıldığına göre artık onun da işi var ve aynı kurala tabidir. Bu test
    /// <see cref="Only_the_button_whose_work_runs_goes_amber"/>'in Optimize ayağıdır: koşan Optimize amber
    /// yanar, komşuları sönük kalır.
    /// </summary>
    [StaFact]
    public void The_optimize_button_spins_in_amber_while_its_own_work_runs()
    {
        var vm = NewVm();
        var (box, window) = Realize(vm);
        Assert.IsType<Viewbox>(box.OptimizeButton.Content); // boşta: ikon

        vm.OnEvent(new OptimizeStartedEvent(@"D:\repo"));

        Assert.IsType<BuildOrchestrator.App.Controls.BuildingSpinner>(box.OptimizeButton.Content);
        Assert.Same(box.FindResource("Brush.AmberSoft"),
            BuildOrchestrator.App.Controls.DsTransition.GetAnimatedBackground(box.OptimizeButton));
        Assert.Equal(1d, box.OptimizeButton.Opacity); // koşan iş SÖNÜK görünmez
        // Komşular sönük kalır.
        Assert.IsType<Viewbox>(box.CleanButton.Content);
        Assert.IsType<Viewbox>(box.ResolveButton.Content);

        vm.OnEvent(new OptimizeCompletedEvent(ProjectCount: 1));
        Assert.IsType<Viewbox>(box.OptimizeButton.Content); // iş bitti: ikon geri
        GC.KeepAlive(window);
    }
}
