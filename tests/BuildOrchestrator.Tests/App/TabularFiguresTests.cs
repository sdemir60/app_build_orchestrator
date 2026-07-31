using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T4 · n6] design-v1 §1.2 (README:48): <i>"makine çıktısı (console, süre, SHA, sayaç, yol) = Geist Mono,
/// <b>DAİMA tabular rakam</b>."</i> — üretimde <c>Typography.NumeralAlignment="Tabular"</c> (XAML) /
/// <c>Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular)</c> (kod-tarafı) dört yerde SET edilir
/// (brief'in ölçtüğü envanter): <c>ProjectRow.xaml:74</c> (sha) · <c>:107</c> (süre) ·
/// <c>EventStreamView.xaml:41</c> (aktif satır metni) · <c>StickyRibbon.xaml:38</c> (faz metni) ·
/// <c>ActionBar.xaml.cs:254</c> (sayaç chip değeri). Hiçbiri bugüne kadar RUNTIME'da doğrulanmıyordu (XAML/kod
/// satırı doğru olsa bile bir stil/şablon onu SONRADAN ezebilirdi — bu test gerçekten realize edilmiş kontrolde
/// <see cref="Typography.NumeralAlignmentProperty"/>'yi okur, kaynağı elle taramaz).
///
/// <para><b>Kapsam (bilinçli, dar):</b> yalnız brief'in listelediği BEŞ üretim yeri. Uygulamada mono taşıyan
/// başka bir yeni sayısal alan eklenirse bu test onu OTOMATİK kapsamaz — yeni bir satır eklemek gerekir (kasıtlı:
/// "her mono = tabular olmalı" genel kuralı statik bir kaynak taramasıyla değil, kod incelemesiyle korunur).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class TabularFiguresTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    [StaFact]
    public void The_project_row_sha_and_duration_columns_are_tabular()
    {
        var vm = new ProjectRowViewModel("id", "Foo", ProjectRowState.Succeeded);
        var host = DsResources.NewHost();
        var row = new ProjectRow { DataContext = vm };
        var window = DsResources.Realize(host, row);

        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(row.ShaText));
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(row.DurationText));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_event_stream_active_line_text_is_tabular()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var view = new EventStreamView { DataContext = vm };
        var window = DsResources.Realize(host, view);

        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(view.ActiveText));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_sticky_ribbon_phase_text_is_tabular()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var ribbon = new StickyRibbon { DataContext = vm };
        var window = DsResources.Realize(host, ribbon);

        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(ribbon.PhaseText));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_action_bar_counter_chip_values_are_tabular()
    {
        var vm = NewVm();
        var host = DsResources.NewHost();
        var bar = new ActionBar { DataContext = vm };
        var window = DsResources.Realize(host, bar);

        var sigmaValue = (TextBlock)((StackPanel)bar.SigmaChip.Content).Children[1];
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(sigmaValue));
        GC.KeepAlive(window);
    }
}
