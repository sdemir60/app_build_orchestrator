using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.23.0 §2.12] Bir popover'ı açıp kapatan <see cref="ToggleButton"/> — işaretliyken popover açıktır.
/// Tek farkı ekran okuyucuya söyledikleridir: tasarımın <c>aria-expanded</c>'ının UIA karşılığı olan
/// <b>ExpandCollapse</b> desenini de taşır (durumu <see cref="ToggleButton.IsChecked"/>'tir) ve durum değişince
/// bunu duyurur. Düz bir <see cref="ToggleButton"/> yalnız "basılı / basılı değil" der; "açtığı şey açık mı"
/// sorusunu cevaplamaz.
/// </summary>
public class PopupToggleButton : ToggleButton
{
    protected override AutomationPeer OnCreateAutomationPeer() => new PopupToggleButtonAutomationPeer(this);

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        AnnounceExpandCollapse(ExpandCollapseState.Collapsed, ExpandCollapseState.Expanded);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        AnnounceExpandCollapse(ExpandCollapseState.Expanded, ExpandCollapseState.Collapsed);
    }

    private void AnnounceExpandCollapse(ExpandCollapseState was, ExpandCollapseState now)
    {
        if (UIElementAutomationPeer.FromElement(this) is PopupToggleButtonAutomationPeer peer)
            peer.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty, was, now);
    }

    private sealed class PopupToggleButtonAutomationPeer(PopupToggleButton owner)
        : ToggleButtonAutomationPeer(owner), IExpandCollapseProvider
    {
        public override object GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.ExpandCollapse ? this : base.GetPattern(patternInterface);

        public ExpandCollapseState ExpandCollapseState =>
            owner.IsChecked == true ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

        public void Expand() => owner.IsChecked = true;

        public void Collapse() => owner.IsChecked = false;
    }
}
