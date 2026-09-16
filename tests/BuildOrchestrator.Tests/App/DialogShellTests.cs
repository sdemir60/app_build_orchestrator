using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.19.0 §2.11 · ortak kabuk] Üç modal (Settings · About · What's new) AYNI kabuğu paylaşır:
/// full-bleed scrim, ortada <c>Ds.Dialog</c> çerçevesi, köşelerde KIRPILAN içerik, <c>12px 18px</c> padding'li
/// ve üstte 1px <c>border-subtle</c> çizgili footer, host yüksekliğine göre <c>min(tasarım, host − 48)</c>
/// kelepçesi ve 180ms/6px giriş. Kabuk TEK yerdedir (<c>Controls/ModalDialog</c>); bu dosya onun üç
/// tüketicideki GERÇEK realize kanıtıdır — her XAML kökü ayrı bir gerçekliktir (CLAUDE.md realize kuralı),
/// bu yüzden her dialog kendi assertion'ını taşır.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class DialogShellTests
{
    private static Border Frame(Grid scrim) => (Border)VisualTreeHelper.GetChild(scrim, 0);

    // ---------------------------------------------------------------- saf kelepçe

    /// <summary>Prototipin <c>maxHeight: calc(100% - 48px)</c>'i: tasarım ölçüsü host'a sığıyorsa aynen kalır,
    /// sığmıyorsa host − 48'e iner; tasarım ölçüsü yoksa (Auto, NaN) üst sınır doğrudan host − 48'dir.</summary>
    [Fact]
    public void The_clamp_is_the_smaller_of_the_design_size_and_the_host_minus_48()
    {
        Assert.Equal(452.0, DialogSize.Clamp(600, 500));
        Assert.Equal(600.0, DialogSize.Clamp(600, 1000));
        Assert.Equal(452.0, DialogSize.Clamp(double.NaN, 500));
        Assert.Equal(0.0, DialogSize.Clamp(600, 20)); // host oluktan küçükse negatif ölçü üretilmez
    }

    // ---------------------------------------------------------------- realize: ölçü + köşe + footer

    /// <summary>Kabuk çerçevesinin çocuğu köşelerde KIRPILIR: düz <c>ClipToBounds</c> dikdörtgendir ve
    /// Settings rayının/footer'ın zemini yuvarlak köşeden taşar — clip, çerçevenin iç yarıçapını
    /// (radius − border) taşıyan bir <see cref="RectangleGeometry"/> olmalıdır ve içeriğin GERÇEK boyutunu
    /// izler.</summary>
    private static void AssertRoundedClip(Border frame)
    {
        var content = (FrameworkElement)frame.Child;
        var clip = Assert.IsType<RectangleGeometry>(content.Clip);
        double inner = frame.CornerRadius.TopLeft - frame.BorderThickness.Left;
        Assert.True(inner > 0, "Ds.Dialog köşe yarıçapı çözülmedi");
        Assert.Equal(inner, clip.RadiusX);
        Assert.Equal(inner, clip.RadiusY);
        Assert.Equal(new Rect(0, 0, content.ActualWidth, content.ActualHeight), clip.Rect);
    }

    /// <summary>Footer şeridi kabuğundur: padding <c>12px 18px</c>, üstte 1px <c>border-subtle</c>.</summary>
    private static void AssertShellFooter(FrameworkElement dialog, Grid scrim, Button footerButton)
    {
        var strip = DsResources.Ancestors(footerButton).OfType<Border>()
            .First(b => b.BorderThickness == new Thickness(0, 1, 0, 0));
        Assert.Equal(new Thickness(18, 12, 18, 12), strip.Padding);
        Assert.Equal(DsResources.TokenColor(dialog, "Brush.BorderSubtle"), DsResources.ColorOf(strip.BorderBrush));
        Assert.True(DsResources.IsSelfOrDescendantOf(strip, Frame(scrim)));
    }

    private static Button ButtonWithContent(FrameworkElement root, string content) =>
        DsResources.Descendants(root).OfType<Button>().First(b => Equals(b.Content, content));

    [StaFact]
    public void Whats_new_uses_the_shell_at_720_by_600_with_rounded_clip_and_shell_footer()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var frame = Frame(dialog.Scrim);
            Assert.Equal(720.0, frame.ActualWidth);
            Assert.Equal(600.0, frame.ActualHeight);
            AssertRoundedClip(frame);
            AssertShellFooter(dialog, dialog.Scrim, ButtonWithContent(dialog, "Close"));
        }
    }

    [StaFact]
    public void About_uses_the_shell_with_rounded_clip_and_shell_footer()
    {
        var (dialog, _, scope) = AboutDialogHost.OpenRealized();
        using (scope)
        {
            var frame = Frame(dialog.Scrim);
            Assert.Equal(660.0, frame.ActualWidth);
            AssertRoundedClip(frame);
            AssertShellFooter(dialog, dialog.Scrim, ButtonWithContent(dialog, "Close"));
        }
    }

    [StaFact]
    public void Settings_uses_the_shell_with_rounded_clip_and_shell_footer()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using (scope)
        {
            var frame = Frame(dialog.Scrim);
            Assert.Equal(760.0, frame.ActualWidth);
            AssertRoundedClip(frame);
            AssertShellFooter(dialog, dialog.Scrim, ButtonWithContent(dialog, "Cancel"));
        }
    }

    /// <summary>Host 500px iken 600px'lik What's new 452'ye (500 − 48) kelepçelenir; pencere büyüyünce
    /// kelepçe YENİDEN uygulanır ve dialog tasarım yüksekliğine döner.
    /// <para>Host = dialogun KENDİ alanıdır (scrim pencerenin istemci alanını kaplar), pencerenin dış ölçüsü
    /// DEĞİL: ekran dışı test penceresinin çerçevesi birkaç piksel yer (ölçüldü: 500'lük pencerede istemci
    /// 486). Bu yüzden pencere, istemci alanı TAM 500 olacak şekilde çerçeve payı eklenerek boyutlanır.</para></summary>
    [StaFact]
    public void The_dialog_height_clamps_to_the_host_minus_48_and_follows_a_resize()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
        {
            var window = Window.GetWindow(dialog)!;
            double chrome = window.ActualHeight - dialog.ActualHeight;

            window.Height = 500 + chrome;
            dialog.UpdateLayout();
            Assert.Equal(500.0, dialog.ActualHeight, precision: 3);
            Assert.Equal(452.0, Frame(dialog.Scrim).ActualHeight, precision: 3);

            window.Height = 900 + chrome;
            dialog.UpdateLayout();
            Assert.Equal(600.0, Frame(dialog.Scrim).ActualHeight, precision: 3);
        }
    }

    /// <summary>Yuvalara (head · gövde · footer) verilen içerik dialogun tipografisini (<c>AppFonts.Ui</c>,
    /// <c>text-primary</c>) MİRAS ALIR. <para><b>Ölçüldü (kırmızı):</b> yuva içerikleri mantıksal olarak
    /// dialogun kendisine bağlıdır ve WPF değer mirası MANTIKSAL ebeveyni izler — metin ayarları yalnız şablondaki
    /// çerçevede dururken başlık Segoe UI ile çiziliyordu ve Settings gövdesindeki açıklamalar farklı sarılıp gövdeyi
    /// 3px taşırıyordu.</para></summary>
    [StaFact]
    public void Slot_content_inherits_the_dialog_typography()
    {
        var (notes, notesScope) = NotesDialogHost.OpenRealized();
        using (notesScope)
        {
            var title = DsResources.Descendants(notes).OfType<TextBlock>().Single(t => t.Text == "What's new");
            Assert.Equal(AppFonts.Ui, title.FontFamily);
            var close = ButtonWithContent(notes, "Close");
            Assert.Equal(AppFonts.Ui, TextElement.GetFontFamily((DependencyObject)VisualTreeHelper.GetParent(close)));
        }

        var (settings, _, _, settingsScope) = SettingsDialogHost.OpenRealized();
        using (settingsScope)
        {
            var title = DsResources.Descendants(settings).OfType<TextBlock>().First(t => t.Text == "Settings");
            Assert.Equal(AppFonts.Ui, title.FontFamily);
            Assert.Equal(AppFonts.Ui, TextElement.GetFontFamily(settings.Body));
            Assert.Equal(DsResources.TokenColor(settings, "Brush.TextPrimary"),
                DsResources.ColorOf(TextElement.GetForeground(settings.Body)));
        }
    }

    // ---------------------------------------------------------------- ortak davranış (üç dialog)

    /// <summary>Kabuğun davranışı üç dialogda AYNIDIR: dialog içine basış scrim'e ULAŞMAZ (handled, açık
    /// kalır), scrim'e basış kapatır, Esc kapatır ve handled döner (MainWindow'un güvenlik ağına sızmaz).</summary>
    private static void AssertShellDismissal(FrameworkElement dialog, Grid scrim, Action reopen)
    {
        var inside = MouseInput.PressLeft((UIElement)Frame(scrim).Child);
        Assert.True(inside.Handled, "dialog içi basış scrim'e ulaştı");
        Assert.Equal(Visibility.Visible, dialog.Visibility);

        MouseInput.PressLeft(scrim);
        Assert.Equal(Visibility.Collapsed, dialog.Visibility);

        reopen();
        dialog.UpdateLayout();
        Assert.Equal(Visibility.Visible, dialog.Visibility);
        var esc = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog)!, 0, Key.Escape)
            { RoutedEvent = Keyboard.KeyDownEvent };
        ((UIElement)Frame(scrim).Child).RaiseEvent(esc);
        Assert.True(esc.Handled, "Esc handled dönmedi");
        Assert.Equal(Visibility.Collapsed, dialog.Visibility);
    }

    [StaFact]
    public void Whats_new_closes_on_scrim_and_esc_but_not_on_a_click_inside()
    {
        var (dialog, scope) = NotesDialogHost.OpenRealized();
        using (scope)
            AssertShellDismissal(dialog, dialog.Scrim, dialog.Open);
    }

    [StaFact]
    public void About_closes_on_scrim_and_esc_but_not_on_a_click_inside()
    {
        var (dialog, run, scope) = AboutDialogHost.OpenRealized();
        using (scope)
            AssertShellDismissal(dialog, dialog.Scrim,
                () => dialog.Open(run, true, () => Task.FromResult(AboutDialogHost.FakeMsBuild)));
    }

    [StaFact]
    public void Settings_closes_on_scrim_and_esc_but_not_on_a_click_inside()
    {
        var (dialog, run, store, scope) = SettingsDialogHost.OpenRealized();
        using (scope)
            AssertShellDismissal(dialog, dialog.Scrim, () => dialog.Open(run, store, () => null));
    }

    /// <summary>Klavye odağı scrim'in GÖRSEL alt ağacındaki bir kontroldedir (dialogun kendisinde değil).
    /// <para><b>Ölçüldü (kırmızı):</b> eski <c>Open()</c>'lar <c>Visibility = Visible</c>'ın hemen ardından
    /// <c>Scrim.MoveFocus(First)</c> çağırıyordu; dialog o anda henüz hiç yerleşmediği için gezinme hiçbir aday
    /// bulamıyor ve odak UserControl'ün KENDİSİNDE kalıyordu (<c>Keyboard.FocusedElement</c> = dialog). Kabuk
    /// bu yüzden odağı taşımadan önce yerleşimi tamamlar.</para></summary>
    private static void AssertFocusInside(Grid scrim)
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        Assert.True(focused is not null && !ReferenceEquals(focused, scrim) && DsResources.IsSelfOrDescendantOf(focused, scrim),
            $"odak dialogun içinde değil: {focused?.GetType().Name ?? "null"}");
    }

    /// <summary>Açılışta odak dialogun İÇİNE taşınır (Tab tuzağının başlangıç noktası) — üç dialogda da.</summary>
    [StaFact]
    public void Opening_any_dialog_moves_keyboard_focus_inside_it()
    {
        var (notes, notesScope) = NotesDialogHost.OpenRealized();
        using (notesScope)
            AssertFocusInside(notes.Scrim);

        var (about, _, aboutScope) = AboutDialogHost.OpenRealized();
        using (aboutScope)
            AssertFocusInside(about.Scrim);

        var (settings, _, _, settingsScope) = SettingsDialogHost.OpenRealized();
        using (settingsScope)
            AssertFocusInside(settings.Scrim);
    }

    // ---------------------------------------------------------------- giriş animasyonu (Settings)

    /// <summary><b>[DEĞİŞEN KURAL — design v1.19.0 ortak kabuk]</b> ESKİ İDDİA (ARCHITECTURE §13.3, About
    /// paragrafı): "It adds an entrance the Settings dialog does not have" — giriş animasyonu yalnız About ve
    /// What's new'deydi, Settings anında beliriyordu. v1.19.0 üç dialogu tek kabuğa topladı ve prototipin
    /// <c>DialogShell</c>'i <c>ds-dialog-in</c> sınıfını ÜÇÜNE de takar: Settings de 180ms fade + 6px yükselir.</summary>
    [StaFact]
    public void Settings_now_plays_the_dialog_entrance()
    {
        using var motion = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using (scope)
        {
            var rise = Assert.IsType<TranslateTransform>(Frame(dialog.Scrim).RenderTransform);
            Assert.True(rise.HasAnimatedProperties, "yükselme transform'u takıldı ama animasyon kurulmadı");
        }
    }

    [StaFact]
    public void Settings_entrance_snaps_under_reduced_motion()
    {
        using var motion = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = false }));
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Equal(1.0, Frame(dialog.Scrim).Opacity);
            Assert.Equal(Transform.Identity, Frame(dialog.Scrim).RenderTransform);
            Assert.False(Frame(dialog.Scrim).HasAnimatedProperties);
        }
    }

    // ---------------------------------------------------------------- kaynak guard'ı (kopya YASAK)

    /// <summary>Kabuk davranışı (scrim tıklaması, dialog içi tıklamanın yutulması, Esc, odak tuzağı, giriş
    /// animasyonu, açılışta odak taşıma) üç dialogun dosyalarında YENİDEN yazılmaz — tek evi
    /// <c>ModalDialog</c>'dur. Bu desenlerden biri bir dialog dosyasında belirirse kopya geri
    /// dönmüş demektir.</summary>
    private static readonly Regex ShellBehaviour = new(
        @"OnScrimClick|OnDialogClick|override\s+void\s+OnKeyDown|KeyboardNavigation\.(?:Control)?TabNavigation"
        + @"|FocusManager\.IsFocusScope|Brush\.Scrim|PopIn\.PlayDialog|MoveFocus|Ds\.Dialog\b",
        RegexOptions.Compiled);

    [Theory]
    [InlineData("NotesDialog.xaml*")]
    [InlineData("AboutDialog.xaml*")]
    [InlineData("SettingsDialog.xaml*")]
    public void Dialog_files_do_not_carry_their_own_copy_of_the_shell(string pattern)
    {
        Assert.Equal(2, SourceGuard.ScannedAppFiles(pattern).Count); // .xaml + .xaml.cs gerçekten tarandı
        Assert.Empty(SourceGuard.ScanApp(pattern, ShellBehaviour, skipCommentLines: true));
    }
}
