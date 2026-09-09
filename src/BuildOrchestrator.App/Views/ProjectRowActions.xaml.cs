using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using BuildOrchestrator.App;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [L1/It-5 perf] <see cref="ProjectRow"/>'un hover eylem bloğu: play/Stop, ⋯, folder + VS ikon butonları ve
/// Open-in-VS seçim popover'ı. Kendi başına DAVRANIŞ taşımaz — tıklama/popover kablajı ve içerik üretimi
/// <see cref="ProjectRow"/>'da kalır (mantık dağıtılmaz); bu kök yalnız markup'ı taşır ki satır onu İLK HOVER'da
/// (ya da koşunun hedefi olduğunda) bir kez kurabilsin.
/// </summary>
public partial class ProjectRowActions : UserControl
{
    public ProjectRowActions()
    {
        InitializeComponent();
        // [design v1.11.0 §2.4-4 · §9-13] Satırda DS tooltip'i taşıyan TEK öğe uyarı üçgenidir. İkon
        // butonları tooltip'lerini korur ama uygulama genelindeki GECİKMESİZ kipten çıkarılır: fare satır
        // boyunca gezerken arka arkaya balon açılmaz. Gerekçe: Controls/AppTooltipDefaults.NativeDelayMs.
        foreach (var button in new DependencyObject[] { PART_BuildButton, PART_StopButton, PART_MoreButton, PART_RevealButton, PART_VsButton })
            Controls.AppTooltipDefaults.UseNativeDelay(button);

        // [design §3.8] Play'in tooltip'i kilide göre değişir (boşta "Build this project", koşarken "Build in
        // progress — …") ve pasif düğme de nedenini söylemelidir — yazıcı ProjectRow.ApplyActionState'tir,
        // burada yalnız boştaki metin ve pasif-tooltip kapısı kurulur. Stop'un adı/tooltip'i sabittir.
        PART_BuildButton.ToolTip = AccessibilityNames.BuildThisProject;
        ToolTipService.SetShowOnDisabled(PART_BuildButton, true);
        AutomationProperties.SetName(PART_StopButton, AccessibilityNames.StopThisBuild);
        PART_StopButton.ToolTip = AccessibilityNames.StopBuildTooltip;
        PART_MoreButton.ToolTip = "More — Rebuild, Clean";
        PART_RowMenu.Opened += (_, _) => PART_RowMenuContent.PlayPopIn();
        // [design v1.11.0 §9-6] Açık menünün ⋯'sine basmak onu KAPATIR (BuildApp.jsx:657
        // `if (menuOpen) { setMenu(null); return; }`); VS seçicisi de aynı kapıdan geçer.
        Controls.PopoverToggle.Bind(PART_MoreButton, PART_RowMenu);
        Controls.PopoverToggle.Bind(PART_VsButton, PART_VsChooser);
    }

    internal FrameworkElement HoverIcons => PART_HoverIcons;
    /// <summary>[design v1.11.0 §2.4-4] Satırın birincil eylemi — yalnız o projeyi derler (§3.8).</summary>
    internal Button BuildButton => PART_BuildButton;
    /// <summary>[design §3.8] Koşunun hedefi olan satırda play'in yerini alan kırmızı Stop — ana Stop ile AYNI komut.</summary>
    internal Button StopButton => PART_StopButton;
    /// <summary>[test yüzeyi] Stop ikonunun yolu — kırmızı dolgu buradan okunur.</summary>
    internal Path StopIcon => PART_StopIcon;
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
