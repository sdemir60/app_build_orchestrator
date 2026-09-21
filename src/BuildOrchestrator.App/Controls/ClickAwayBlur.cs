using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Odaktaki bir metin kutusunun DIŞINA tıklanınca odağı kutudan bırakır.
///
/// <para><b>Neden gerekli:</b> WPF'te odağı yalnız odak ALABİLEN bir öğe değiştirir. Panel zeminleri,
/// <c>Focusable=False</c> düğmeler ve satırlar tıklamayı alır ama odağı taşımaz — proje filtresine yazıp
/// graf panelinin zeminine tıklamak odağı (caret + amber halka) kutuda bırakıyordu.</para>
///
/// <para><b>Nasıl:</b> <c>MouseDown</c> pencerede, handled olanlar DAHİL, rotanın SONUNDA dinlenir — tıklanan
/// öğe odağı kendisi almışsa (başka bir kutu, odaklanabilir bir düğme) iş bitmiştir ve dokunulmaz. Odak hâlâ
/// kutudaysa ve tıklama kutunun İÇİNDE değilse (kendi ✕'i dahil) odak, tıklanan yerin odak alabilen en yakın
/// atasına verilir; bir modal kendi içine odak tuzağı kurduğunda (<see cref="ModalDialog"/> odaklanabilir)
/// odak böylece tuzaktan dışarı düşmez. Böyle bir ata yoksa odak temizlenir. Popup içindeki tıklamalara
/// karışılmaz (bkz. <see cref="OnMouseDown"/>).</para>
/// </summary>
public static class ClickAwayBlur
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ClickAwayBlur), new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

    private static readonly MouseButtonEventHandler Handler = OnMouseDown;

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.RemoveHandler(Mouse.MouseDownEvent, Handler);
        if ((bool)e.NewValue) element.AddHandler(Mouse.MouseDownEvent, Handler, handledEventsToo: true);
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBoxBase box) return;              // odak bir metin kutusunda değil
        if (e.OriginalSource is not DependencyObject source) return;
        var chain = SelfAndAncestors(source).ToList();
        // Popup içeriği ayrı bir görsel ağaçtır ve zinciri pencereye ULAŞMAZ: orada odağı ana pencereye taşımak
        // açık popover'ın (StaysOpen=False) kapanma kurallarına karışırdı — popup kendi odağını yönetir.
        if (!chain.Contains((DependencyObject)sender)) return;
        if (chain.Contains(box)) return;                                          // kutunun içine tıklandı (✕ dahil)

        var target = chain.OfType<UIElement>().FirstOrDefault(el => el.Focusable && el.IsEnabled && el.IsVisible);
        if (target is null || !target.Focus()) Keyboard.ClearFocus();
    }

    /// <summary>Görsel ağaçta yukarı. <c>Run</c> gibi görsel OLMAYAN içerik öğelerinden (tıklamanın özgün
    /// kaynağı olabilirler) mantıksal ebeveyn üzerinden görsel ağaca çıkılır.</summary>
    private static IEnumerable<DependencyObject> SelfAndAncestors(DependencyObject node)
    {
        for (DependencyObject? n = node; n is not null;
             n = n is Visual or Visual3D ? VisualTreeHelper.GetParent(n) : LogicalTreeHelper.GetParent(n))
            yield return n;
    }
}
