using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BuildOrchestrator.App;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using static BuildOrchestrator.Tests.App.DsResources;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.23.0 §2.12 · §9 "Uygulama sayıları — güncelleme" · plan B4/U2] Hapa basınca açılan güncelleme kartı.
/// Kart mevcut popover altyapısıdır (<see cref="PopoverBase"/> + WPF <see cref="Popup"/> + <see cref="PopoverToggle"/>
/// + Esc zinciri + <c>Ds.Popover</c> kabuğu, dolgu 0): 344px, hapın 9px altında, sol kenarı hapla hizalı; 140ms
/// drop-in (4px yukarıdan + ölçek .985, merkez sol üst). Üç blok, aralarında 1px <c>border</c>:
/// (1) kimlik <c>13 16 15</c> — caps <c>UPDATE READY</c> + sağda mono boyut, 11px altında kurulu → ok → gelen sürüm
/// (gap 9); (2) öne çıkanlar <c>13 16 14</c> — What's new'in blok dili, kompakt ölçülerle (blok 12, başlık altı 6,
/// girinti 13, madde 5, 12px <c>text-secondary</c> satır 18); (3) karar <c>12 16 14</c> — açıklama ya da kilit nedeni,
/// 12px altında sağa yaslı <c>Later</c> + <c>Restart to update</c> (aralarında 8).
///
/// <para><b>Kapanış:</b> <c>Later</c>, hapa ikinci basış, dışarı tık (<c>StaysOpen=False</c>), Esc (kartın içinde ve
/// pencerenin Esc zincirinin popover katmanında) ve bir dialog açılınca (plan U2: Popup ayrı bir HWND'dir, dialogun
/// altında kalamaz — tasarımın "dialog kartın üstüne açılır"ının en yakın karşılığı). <c>Later</c> hapı gizlemez.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class UpdateCardTests
{
    /// <summary>Tasarımın kart genişliği — tek başına realize edilen kart üretimdeki kabuğun iç alanına sarılır.</summary>
    private const double CardWidth = 344;

    /// <summary>Kartı üretimdeki kabuğunun (<c>Ds.Popover</c>, 344px, dolgu 0) içinde, AÇIK ve ekran dışı gerçek bir
    /// pencerede realize eder.</summary>
    private static (UpdateCard card, Window window) OpenRealized(RunViewModel vm)
    {
        var host = DsResources.NewHost();
        var shell = new Border { Width = CardWidth };
        shell.SetResourceReference(FrameworkElement.StyleProperty, "Ds.Popover");
        shell.Padding = new Thickness(0);
        var card = new UpdateCard { DataContext = vm };
        shell.Child = card;
        var window = DsResources.Realize(host, shell, 500, 600);
        card.IsOpen = true;
        card.UpdateLayout();
        return (card, window);
    }

    private static Color Token(FrameworkElement host, string key) => DsResources.TokenColor(host, key);

    // ================================================================ realize + ölçüler

    /// <summary>Kimlik bloğu: caps başlık + sağa yaslı mono boyut; 11px altında kurulu (mono 13 <c>text-dim</c>) → ok
    /// (12px <c>text-faint</c>) → gelen (mono 16/500 <c>text-primary</c>), aralarında 9px.</summary>
    [StaFact]
    public void The_identity_block_reads_installed_to_incoming_with_the_package_size()
    {
        var vm = UpdateRestartLockTests.NewVm();
        var (card, window) = OpenRealized(vm);

        Assert.Equal(new Thickness(16, 13, 16, 15), card.PART_Identity.Padding);
        Assert.Equal(new Thickness(0), card.PART_Identity.BorderThickness);

        var heading = card.PART_Heading;
        Assert.Equal(UpdateText.CardHeading, heading.Text);
        Assert.True(heading.Uppercase);
        Assert.Equal(11.0, heading.FontSize);
        Assert.Equal(FontWeights.Medium, heading.FontWeight);
        Assert.Equal(Token(card, "Brush.TextFaint"), DsResources.ColorOf(heading.Foreground));

        var size = card.PART_Size;
        Assert.Equal(vm.AvailableUpdate!.Size, size.Text);
        Assert.Equal(AppFonts.Mono, size.FontFamily);
        Assert.Equal(11.0, size.FontSize);
        Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(size));
        Assert.Equal(Token(card, "Brush.TextFaint"), DsResources.ColorOf(size.Foreground));
        var identity = card.PART_Identity;
        Assert.Equal(identity.ActualWidth - 16, BoundsIn(size, identity).Right, precision: 1); // sağa yaslı

        var installed = card.PART_Installed;
        Assert.Equal(AppIdentity.Version, installed.Text);
        Assert.Equal(AppFonts.Mono, installed.FontFamily);
        Assert.Equal(13.0, installed.FontSize);
        Assert.Equal(FontWeights.Normal, installed.FontWeight);
        Assert.Equal(Token(card, "Brush.TextDim"), DsResources.ColorOf(installed.Foreground));

        var arrow = card.PART_Arrow;
        Assert.Equal((12.0, 12.0), (arrow.Width, arrow.Height));
        var glyph = DsResources.Descendants(arrow).OfType<Path>().Single();
        Assert.Same(card.FindResource("Icon.ArrowRight"), glyph.Data);
        Assert.Equal(1.8, glyph.StrokeThickness);
        Assert.Equal(Token(card, "Brush.TextFaint"), DsResources.ColorOf(glyph.Stroke));

        var incoming = card.PART_Incoming;
        Assert.Equal(vm.AvailableUpdate.Version, incoming.Text);
        Assert.Equal(AppFonts.Mono, incoming.FontFamily);
        Assert.Equal(16.0, incoming.FontSize);
        Assert.Equal(FontWeights.Medium, incoming.FontWeight);
        Assert.Equal(Token(card, "Brush.TextPrimary"), DsResources.ColorOf(incoming.Foreground));

        Assert.Equal(9.0, BoundsIn(arrow, identity).Left - BoundsIn(installed, identity).Right, precision: 1);
        Assert.Equal(9.0, BoundsIn(incoming, identity).Left - BoundsIn(arrow, identity).Right, precision: 1);
        Assert.Equal(11.0, BoundsIn(card.PART_Transition, identity).Top - BoundsIn(card.PART_HeadingRow, identity).Bottom,
            precision: 1);

        Assert.Empty(DsResources.DynamicResourceTypeMismatches(card));
        GC.KeepAlive(window);
    }

    /// <summary>Öne çıkanlar What's new'in blok dilini kompakt ölçülerle konuşur ve kategoriler
    /// <see cref="ReleaseNotes.KindOrder"/> sırasıyla çizilir: örnek teklifte önce FIXED (2 madde), sonra PERFORMANCE
    /// (1 madde). Kare renkleri <see cref="ReleaseNotes.SwatchBrushKey"/>'den.</summary>
    [StaFact]
    public void The_highlights_speak_the_whats_new_block_language_at_the_compact_card_size()
    {
        var vm = UpdateRestartLockTests.NewVm();
        var (card, window) = OpenRealized(vm);
        var block = card.PART_HighlightsBlock;
        var list = card.PART_Highlights;

        Assert.Equal(new Thickness(16, 13, 16, 14), block.Padding);
        Assert.Equal(new Thickness(0, 1, 0, 0), block.BorderThickness);
        Assert.Equal(Token(card, "Brush.Border"), DsResources.ColorOf(block.BorderBrush));

        var categories = list.Children.Cast<FrameworkElement>().ToList();
        Assert.Equal(["FIXED", "PERFORMANCE"],
            categories.Select(c => DsResources.Descendants(c).OfType<TrackedTextBlock>().Single().Text));
        Assert.Equal([2, 1], categories.Select(c => DsResources.Descendants(c).OfType<TextBlock>().Count()));
        Assert.Equal([Token(card, ReleaseNotes.SwatchBrushKey(NoteKind.Fixed)), Token(card, ReleaseNotes.SwatchBrushKey(NoteKind.Performance))],
            categories.Select(c => DsResources.ColorOf(DsResources.Descendants(c).OfType<Rectangle>().Single().Fill)));

        // blok arası 12
        Assert.Equal(12.0, BoundsIn(categories[1], list).Top - BoundsIn(categories[0], list).Bottom, precision: 1);
        var fixedItems = DsResources.Descendants(categories[0]).OfType<TextBlock>().ToList();
        var headingRow = (FrameworkElement)VisualTreeHelper.GetParent(DsResources.Descendants(categories[0]).OfType<TrackedTextBlock>().Single());
        Assert.Equal(6.0, BoundsIn(fixedItems[0], list).Top - BoundsIn(headingRow, list).Bottom, precision: 1);  // başlık altı 6
        Assert.Equal(5.0, BoundsIn(fixedItems[1], list).Top - BoundsIn(fixedItems[0], list).Bottom, precision: 1); // madde arası 5
        Assert.All(DsResources.Descendants(list).OfType<TextBlock>(), item =>
        {
            Assert.Equal(13.0, BoundsIn(item, list).Left, precision: 1); // girinti 13
            Assert.Equal(12.0, item.FontSize);
            Assert.Equal(18.0, item.LineHeight);
            Assert.Equal(double.PositiveInfinity, item.MaxWidth);
            Assert.Equal(TextWrapping.Wrap, item.TextWrapping);
            Assert.Equal(Token(card, "Brush.TextSecondary"), DsResources.ColorOf(item.Foreground));
        });
        GC.KeepAlive(window);
    }

    /// <summary>Karar bloğu: açıklama (12px <c>text-dim</c>, satır 18) · 12px altında sağa yaslı <c>Later</c>
    /// (Secondary.Sm) + <c>Restart to update</c> (Primary.Sm, rotate-cw ikonu), aralarında 8. İlk odak <c>Later</c>'dadır.</summary>
    [StaFact]
    public void The_decision_block_explains_the_restart_and_offers_later_and_restart()
    {
        var vm = UpdateRestartLockTests.NewVm();
        var (card, window) = OpenRealized(vm);
        var decision = card.PART_Decision;

        Assert.Equal(new Thickness(16, 12, 16, 14), decision.Padding);
        Assert.Equal(new Thickness(0, 1, 0, 0), decision.BorderThickness);
        Assert.Equal(Token(card, "Brush.Border"), DsResources.ColorOf(decision.BorderBrush));

        var note = card.PART_Note;
        Assert.Equal(UpdateText.RestartNote, note.Text);
        Assert.Equal(12.0, note.FontSize);
        Assert.Equal(18.0, note.LineHeight);
        Assert.Equal(TextWrapping.Wrap, note.TextWrapping);
        Assert.Equal(Token(card, "Brush.TextDim"), DsResources.ColorOf(note.Foreground));

        var later = card.PART_Later;
        var restart = card.PART_Restart;
        Assert.Same(card.FindResource("Ds.Button.Secondary.Sm"), later.Style);
        Assert.Same(card.FindResource("Ds.Button.Primary.Sm"), restart.Style);
        Assert.Equal(UpdateText.Later, later.Content);
        Assert.Equal(UpdateText.RestartToUpdate, System.Windows.Automation.AutomationProperties.GetName(restart));
        Assert.Contains(UpdateText.RestartToUpdate, DsResources.ShownTexts(restart));
        var rotate = DsResources.Descendants(restart).OfType<Path>().Single();
        Assert.Same(card.FindResource("Icon.Rebuild"), rotate.Data);
        Assert.Equal(Token(card, "Brush.TextOnAccent"), DsResources.ColorOf(rotate.Stroke));

        Assert.Equal(12.0, BoundsIn(card.PART_Actions, decision).Top - BoundsIn(note, decision).Bottom, precision: 1);
        Assert.Equal(8.0, BoundsIn(restart, decision).Left - BoundsIn(later, decision).Right, precision: 1);
        Assert.Equal(decision.ActualWidth - 16, BoundsIn(restart, decision).Right, precision: 1); // sağa yaslı

        DispatcherPump.PumpUntil(() => later.IsKeyboardFocused, TimeSpan.FromSeconds(2));
        Assert.True(later.IsKeyboardFocused, "açılışta odak Later'a gitmedi");
        GC.KeepAlive(window);
    }

    // ================================================================ kilit

    /// <summary>Bir iş sürerken Restart kapalıdır ve açıklama satırı nedeni söyler; iş bitince düğme kendiliğinden
    /// açılır ve satır açıklamaya döner (kart açıkken, canlı).</summary>
    [StaFact]
    public void A_locked_restart_shows_the_reason_and_opens_again_when_the_work_ends()
    {
        var vm = UpdateRestartLockTests.NewVm();
        var (card, window) = OpenRealized(vm);
        Assert.True(card.PART_Restart.IsEnabled); // ön-koşul

        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 1, "Debug", 0));
        Assert.Equal(UpdateText.WaitForBuild, card.PART_Note.Text);
        Assert.False(card.PART_Restart.IsEnabled);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Completed, 1, 0, 0, 0, 1_000));
        Assert.Equal(UpdateText.RestartNote, card.PART_Note.Text);
        Assert.True(card.PART_Restart.IsEnabled);
        GC.KeepAlive(window);
    }

    /// <summary>Restart'a basmak VM'in isteğini yükseltir (kabuk kartı kapatır; restart ekranı ona bağlanır).</summary>
    [StaFact]
    public void The_restart_button_raises_the_restart_request()
    {
        var vm = UpdateRestartLockTests.NewVm();
        var (card, window) = OpenRealized(vm);
        int requests = 0;
        vm.RestartToUpdateRequested += (_, _) => requests++;

        CommandPress.Invoke(card.PART_Restart);
        DispatcherPump.PumpUntil(() => requests > 0, TimeSpan.FromSeconds(2));

        Assert.Equal(1, requests);
        GC.KeepAlive(window);
    }

    /// <summary><c>Later</c> kartın kapanmasını ister (hap kalır — kabuk testi aşağıda).</summary>
    [StaFact]
    public void Later_asks_the_card_to_close()
    {
        var vm = UpdateRestartLockTests.NewVm();
        var (card, window) = OpenRealized(vm);
        bool closeRequested = false;
        card.CloseRequested += () => closeRequested = true;

        CommandPress.Click(card.PART_Later);

        Assert.True(closeRequested);
        GC.KeepAlive(window);
    }

    // ================================================================ drop-in

    /// <summary><c>bo-drop-in</c>: opaklık 0 + 4px YUKARIDAN + ölçek .985 → düz, 140ms, ölçeğin merkezi SOL ÜST köşe
    /// (kart hapın altından sarkar). Popover'ın pop-in'iyle aynı gövdedir; fark yön ve merkezdir.</summary>
    [StaFact]
    public void The_card_drops_in_from_4px_above_scaling_from_its_top_left_corner()
    {
        using var _ = MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true }));
        var vm = UpdateRestartLockTests.NewVm();
        var host = DsResources.NewHost();
        var card = new UpdateCard { DataContext = vm };
        var window = DsResources.Realize(host, card, 400, 500);

        card.IsOpen = true;

        Assert.Equal(new Point(0, 0), card.RenderTransformOrigin);
        var group = Assert.IsType<TransformGroup>(card.RenderTransform);
        var scale = Assert.IsType<ScaleTransform>(group.Children[0]);
        var translate = Assert.IsType<TranslateTransform>(group.Children[1]);
        Assert.Equal((0.985, 0.985), (scale.ScaleX, scale.ScaleY));
        Assert.Equal(-4.0, translate.Y);
        Assert.True(card.HasAnimatedProperties, "drop-in GERÇEKTEN animasyonlu değil");

        DispatcherPump.PumpUntil(() => card.Opacity >= 1.0 && translate.Y >= 0.0, TimeSpan.FromSeconds(3));
        Assert.Equal(1.0, card.Opacity, precision: 3);
        Assert.Equal(0.0, translate.Y, precision: 3);
        GC.KeepAlive(window);
    }

    // ================================================================ kabuk: yer ve kapanış

    /// <summary>Kart hapın 9px altında, SOL kenarı hapın sol kenarında (§9 <c>top: calc(100% + 9px); left: 0</c>) ve
    /// 344px'lik <c>Ds.Popover</c> kabuğunda, dolgusuz durur; dışarı tık onu kapatır (<c>StaysOpen=False</c>). Kartın VM'i
    /// pencereninkidir (popup içeriği DataContext'i güvenilir miras almaz) ve açık/kapalı durumu hapın işaretini izler —
    /// bağlar yapısal sorulur: gösterilmeyen bir pencerede <see cref="Popup.IsOpen"/> yüklenmeye dek false'a zorlanır
    /// (ölçüldü), kartın açıkken davranışı yukarıda gerçek bir pencerede sürülür.
    /// <para><b>Yer, yerleşimin KENDİSİYLE sorulur</b> (<c>RowMenuPlacementTests</c> gerekçesi: süitin pencereleri ekran
    /// dışındadır ve WPF popup'ı görünür alana geri kelepçeler — mutlak konum kararı değil kelepçeyi ölçerdi): WPF'in
    /// çağıracağı geri çağrı, hapın gerçek ölçüsüyle çağrılır.</para>
    /// <para><b>[DEĞİŞEN KURAL]</b> Eski iddia: yerleşim <c>Placement=Bottom</c> + <c>VerticalOffset=9</c>'dur. WPF'in
    /// <see cref="PlacementMode.Bottom"/>'u yatay hizayı <see cref="SystemParameters.MenuDropAlignment"/>'a bırakır;
    /// Windows'un el tercihi "sağ el" olan makinede bu değer <c>true</c>'dur (ölçüldü: geliştirme makinesinde True) ve
    /// kartın SAĞ kenarı hapın sağ kenarına hizalanıyor, 344px'lik kart hapın SOLUNA sarkıyordu. Kural artık yerleşimi
    /// makinenin ayarından bağımsız pinler: <see cref="PlacementMode.Custom"/>, sol üst köşe (0, hap yüksekliği + 9);
    /// boşluk tek yerden (geri çağrıdan) gelir, popup'ın offset'leri sıfırdır.</para></summary>
    [StaFact]
    public void The_card_hangs_9px_below_the_pill_left_edge_on_the_pills_in_a_344px_popover_shell()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        var popup = window.UpdatePopup;

        Assert.Same(window.UpdatePill, popup.PlacementTarget);
        Assert.Equal(PlacementMode.Custom, popup.Placement);
        Assert.Equal((0.0, 0.0), (popup.HorizontalOffset, popup.VerticalOffset));
        var pill = new Size(window.UpdatePill.ActualWidth, window.UpdatePill.ActualHeight);
        Assert.True(pill.Width > 0 && pill.Height > 0, "ön-koşul: hap ölçülmedi");
        var placement = Assert.Single(popup.CustomPopupPlacementCallback(new Size(CardWidth, 350), pill, default));
        Assert.Equal(new Point(0, pill.Height + 9), placement.Point);
        Assert.False(popup.StaysOpen);
        Assert.True(popup.AllowsTransparency);
        Assert.Equal(PopupAnimation.None, popup.PopupAnimation);

        var shell = Assert.IsType<Border>(popup.Child);
        Assert.Same(window.FindResource("Ds.Popover"), shell.Style);
        Assert.Equal(CardWidth, shell.Width);
        Assert.Equal(new Thickness(0), shell.Padding);
        Assert.Same(window.UpdateCardView, shell.Child);
        Assert.Same(vm, window.UpdateCardView.DataContext);

        var popupOpen = BindingOperations.GetBinding(popup, Popup.IsOpenProperty)!;
        Assert.Equal((nameof(window.UpdatePill), nameof(ToggleButton.IsChecked), BindingMode.TwoWay),
            (popupOpen.ElementName, popupOpen.Path.Path, popupOpen.Mode));
        var cardOpen = BindingOperations.GetBinding(window.UpdateCardView, PopoverBase.IsOpenProperty)!;
        Assert.Equal((nameof(window.UpdatePopup), nameof(Popup.IsOpen)), (cardOpen.ElementName, cardOpen.Path.Path));
        GC.KeepAlive(window);
    }

    /// <summary>Hapa İKİNCİ basış kartı kapatır: kartı dışarı tıkla kapatan aynı basış hapı yeniden işaretleyemez —
    /// kapı (<see cref="PopoverToggle"/>) hapa bağlıdır. Kapının davranışı gerçek bir popup'la
    /// <c>PopoverToggleTests</c>'te sürülür.</summary>
    [StaFact]
    public void A_second_press_on_the_pill_closes_the_card()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);

        Assert.True(PopoverToggle.IsBound(window.UpdatePill));
        GC.KeepAlive(window);
    }

    /// <summary><c>Later</c> kartı kapatır, hap yerinde kalır (kurulum yapılana dek).</summary>
    [StaFact]
    public void Later_closes_the_card_and_the_pill_stays()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);
        window.UpdatePill.IsChecked = true;

        CommandPress.Click(window.UpdateCardView.PART_Later);

        Assert.False(window.UpdatePill.IsChecked);
        Assert.Equal(Visibility.Visible, window.UpdatePillSlot.Visibility);
        GC.KeepAlive(window);
    }

    // Kartın İÇİNDEKİ Esc (popup ayrı bir HWND'dir, pencerenin Esc zinciri oraya ulaşmaz) PopoverBase'in ortak
    // davranışıdır: CloseRequested + handled — PopoverTests.Escape_inside_a_popover_requests_close_and_is_handled
    // UpdateCard için de koşar; kabuğun CloseRequested'e verdiği cevap (kartı kapat) yukarıdaki Later testindedir.

    /// <summary>Odak pencerede kalmışken de Esc kartı kapatır: kart, Esc zincirinin popover katmanındadır — seçimden
    /// ÖNCE kapanır, seçim ikinci Esc'e kalır.</summary>
    [StaFact]
    public void The_window_escape_closes_the_card_before_the_selection()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        vm.SelectProject(@"C:\p\a.csproj");
        window.UpdatePill.IsChecked = true;

        MainWindowHost.PressEscape(window);
        Assert.False(window.UpdatePill.IsChecked);
        Assert.NotNull(vm.SelectedProjectId);

        MainWindowHost.PressEscape(window);
        Assert.Null(vm.SelectedProjectId);
        GC.KeepAlive(window);
    }

    /// <summary>Bir dialog açılınca kart kapanır (plan U2): Settings (dişli), About (ⓘ), What's new (✦) ve first run'ın
    /// Import kısayolu — dördü de ortak modal kabuğun açılışından geçer.</summary>
    [StaTheory]
    [InlineData("settings")]
    [InlineData("about")]
    [InlineData("notes")]
    [InlineData("import")]
    public void Opening_a_dialog_closes_the_card(string dialog)
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);
        window.SettingsOverlay.PickImportPath = () => null; // gerçek dosya seçici açılmasın
        window.SettingsOverlay.ImportHold = _ => Task.CompletedTask;
        window.UpdatePill.IsChecked = true;

        switch (dialog)
        {
            case "settings": CommandPress.Click(window.GearButton); break;
            case "about": CommandPress.Click(window.InfoButton); break;
            case "notes": CommandPress.Click(window.NotesButton); break;
            case "import": CommandPress.Click(window.Shell.ImportSettingsButton); break;
        }

        Assert.True(window.SettingsOverlay.Visibility == Visibility.Visible
                    || window.AboutOverlay.Visibility == Visibility.Visible
                    || window.NotesOverlay.Visibility == Visibility.Visible, "ön-koşul: dialog açılmadı");
        Assert.False(window.UpdatePill.IsChecked);
        GC.KeepAlive(window);
    }

    /// <summary>Restart'a basmak kartı kapatır; aynı istek restart ekranını da oynatır (ekranın kendi testleri
    /// <see cref="UpdateRestartScreenTests"/>'tedir — burada zamanlayıcısı sahtedir, gerçek bir saat kurulmaz).</summary>
    [StaFact]
    public void Restart_to_update_closes_the_card()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        window.UpdateRestartOverlay.Timer = new FakePollTimer();
        window.UpdatePill.IsChecked = true;

        Assert.True(CommandPress.Press(vm.RestartToUpdateCommand));

        Assert.False(window.UpdatePill.IsChecked);
        GC.KeepAlive(window);
    }

    /// <summary>Teklif kalkarsa kart da hap da kalkar.</summary>
    [StaFact]
    public void Withdrawing_the_offer_closes_the_card_and_hides_the_pill()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(temp);
        window.UpdatePill.IsChecked = true;

        vm.AvailableUpdate = null;

        Assert.False(window.UpdatePill.IsChecked);
        Assert.Equal(Visibility.Collapsed, window.UpdatePillSlot.Visibility);
        GC.KeepAlive(window);
    }

    /// <summary>[realize] Kart, pencerenin içinde de token tipleri doğru çözülerek kurulur (Popup çocuğu
    /// <see cref="DsResources.RealizedObjects"/> ile taranır).</summary>
    [StaFact]
    public void The_card_inside_the_window_resolves_its_tokens()
    {
        using var temp = new TempDir();
        var (window, _) = MainWindowHost.NewRealized(temp);

        Assert.Contains(window.UpdateCardView, DsResources.RealizedObjects((FrameworkElement)window.Content));
        Assert.Empty(DsResources.DynamicResourceTypeMismatches(window.UpdatePopup.Child));
        GC.KeepAlive(window);
    }
}
