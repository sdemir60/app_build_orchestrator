using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using BuildOrchestrator.App;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [L1/It-5 perf] <see cref="ProjectRow"/>'un hover eylem bloğu: folder + VS ikon butonları ve Open-in-VS seçim
/// popover'ı. Kendi başına DAVRANIŞ taşımaz — tıklama/popover kablajı ve içerik üretimi <see cref="ProjectRow"/>'da
/// kalır (mantık dağıtılmaz); bu kök yalnız markup'ı taşır ki satır onu İLK HOVER'da bir kez kurabilsin.
/// </summary>
public partial class ProjectRowActions : UserControl
{
    public ProjectRowActions()
    {
        InitializeComponent();
        // [design v1.11.0 §2.4-4 · §9-13] Satırda DS tooltip'i taşıyan TEK öğe uyarı üçgenidir. İkon
        // butonları tooltip'lerini korur ama uygulama genelindeki GECİKMESİZ kipten çıkarılır: fare satır
        // boyunca gezerken arka arkaya balon açılmaz. Gerekçe: Controls/AppTooltipDefaults.NativeDelayMs.
        foreach (var button in new DependencyObject[] { PART_BuildButton, PART_MoreButton, PART_RevealButton, PART_VsButton })
            Controls.AppTooltipDefaults.UseNativeDelay(button);

        // [design v1.11.0 §3.8] Tek-proje koşusunun arka ucu henüz yazılmadı: play PASİFTİR ve tooltip
        // nedenini söyler (bakım kutusundaki Clean/Optimize ile AYNI karar). ⋯ AÇIK kalır — menü açılmazsa
        // tasarımın akışı (sağ tık → Build/Rebuild/Clean) hiç görünmezdi; maddeleri de pasiftir.
        PART_BuildButton.IsEnabled = false;
        PART_BuildButton.ToolTip = AccessibilityNames.RowActionsTooltip;
        ToolTipService.SetShowOnDisabled(PART_BuildButton, true);
        PART_MoreButton.ToolTip = "More — Rebuild, Clean";
        PART_RowMenu.Opened += (_, _) => PART_RowMenuContent.PlayPopIn();
    }

    internal FrameworkElement HoverIcons => PART_HoverIcons;
    /// <summary>[design v1.11.0 §2.4-4] Satırın birincil eylemi — yalnız o projeyi derler (§3.8).</summary>
    internal Button BuildButton => PART_BuildButton;
    /// <summary>[design v1.11.0 §9-6] Satır menüsünü açan ⋯ (satıra sağ tık da aynı menüyü açar).</summary>
    internal ToggleButton MoreButton => PART_MoreButton;
    internal Popup RowMenu => PART_RowMenu;
    internal ProjectRowMenu RowMenuContent => PART_RowMenuContent;
    internal Button RevealButton => PART_RevealButton;
    internal Button VsButton => PART_VsButton;
    internal Popup VsChooser => PART_VsChooser;
    internal FrameworkElement VsChooserContent => PART_VsChooserContent;
    internal Panel VsChooserRows => PART_VsChooserRows;
}
