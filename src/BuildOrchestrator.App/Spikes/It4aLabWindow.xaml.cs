using System.Windows;

namespace BuildOrchestrator.App.Spikes;

/// <summary>
/// [It-4a Foundation] Dev-only lab penceresi kabuğu (--it4a-lab bayrağı, FontAbWindow deseni): DI/EngineHost
/// kurulmaz, Supervisor spawn edilmez. Sonraki task'lar (T57 TrackedTextBlock, T58 sticky header, T59 scroll,
/// T63 graf) primitiflerini burada SampleGraphData'nın (36-proje temsili OSYS grafı) tükettiği bir sekme/panel
/// iskeletiyle gözle doğrular. Bu task yalnız boş kabuğu kurar.
/// </summary>
public partial class It4aLabWindow : Window
{
    public It4aLabWindow()
    {
        InitializeComponent();
    }
}
