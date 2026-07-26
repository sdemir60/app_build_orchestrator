using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [L1/It-5 perf] <see cref="ProjectRow"/>'un hover eylem bloğu: folder + VS ikon butonları ve Open-in-VS seçim
/// popover'ı. Kendi başına DAVRANIŞ taşımaz — tıklama/popover kablajı ve içerik üretimi <see cref="ProjectRow"/>'da
/// kalır (mantık dağıtılmaz); bu kök yalnız markup'ı taşır ki satır onu İLK HOVER'da bir kez kurabilsin.
/// </summary>
public partial class ProjectRowActions : UserControl
{
    public ProjectRowActions() => InitializeComponent();

    internal FrameworkElement HoverIcons => PART_HoverIcons;
    internal Button RevealButton => PART_RevealButton;
    internal Button VsButton => PART_VsButton;
    internal Popup VsChooser => PART_VsChooser;
    internal FrameworkElement VsChooserContent => PART_VsChooserContent;
    internal Panel VsChooserRows => PART_VsChooserRows;
}
