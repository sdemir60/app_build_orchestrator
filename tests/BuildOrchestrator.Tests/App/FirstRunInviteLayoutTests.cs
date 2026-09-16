using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [design v1.19.0 · Task 5] İlk açılış (ayar yokken) daveti — <c>PART_ListInvite</c> ölçü/renk hizalaması
/// (prototip <c>BuildApp.jsx</c> ~921-950). Metinler ZATEN birebirdi (bkz. <see cref="InteractionStateTests"/>);
/// burada yalnız ölçü/renk sapmaları pinlenir. Kartın kendisi realize edilmiş <c>window.Content</c> üzerinden
/// okunur (headless <c>Measure/Arrange</c> render-only özellikleri okumaz — <see cref="MainWindowHost.Realize"/>).
/// </summary>
[Collection("Console UI (serial)")]
public class FirstRunInviteLayoutTests
{
    /// <summary>Repo hiç seçilmemiş bir kabuk — davet görünür durumda.</summary>
    private static ShellRoot NewInviteShell(TempDir temp)
    {
        var (window, _) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);
        return window.Shell;
    }

    /// <summary><c>PART_ListInvite</c> beş çocuğu SIRAYLA taşır: başlık, açıklama, kart, buton satırı, not.
    /// Sıra XAML'in kendisidir (ShellRoot.xaml PART_ListInvite) — burada YENİDEN TANIMLANMAZ.</summary>
    private static StackPanel InvitePanel(ShellRoot shell) => (StackPanel)shell.ListInviteOverlay;

    [StaFact]
    public void Title_is_14px_semibold()
    {
        using var temp = new TempDir();
        var shell = NewInviteShell(temp);
        var title = (TextBlock)InvitePanel(shell).Children[0];

        Assert.Equal((double)shell.FindResource("FontSize.Md"), title.FontSize);
        Assert.Equal((FontWeight)shell.FindResource("FontWeight.Heading"), title.FontWeight);
        Assert.Same(shell.FindResource("Ds.Heading.Md"), title.Style); // dialog başlıklarıyla ortak stil (kopya YASAK)
        GC.KeepAlive(shell);
    }

    [StaFact]
    public void Description_is_dim_with_a_310_max_width_and_snug_line_height()
    {
        using var temp = new TempDir();
        var shell = NewInviteShell(temp);
        var description = (TextBlock)InvitePanel(shell).Children[1];

        Assert.Equal(DsResources.ColorOf((Brush)shell.FindResource("Brush.TextDim")), DsResources.ColorOf(description.Foreground));
        Assert.Equal(310d, description.MaxWidth);
        Assert.Equal((double)shell.FindResource("LineHeight.Snug12"), description.LineHeight);
        Assert.Same(shell.FindResource("Ds.Text.Description"), description.Style); // Settings sayfa açıklamasıyla ortak stil
        GC.KeepAlive(shell);
    }

    [StaFact]
    public void Vertical_rhythm_matches_the_prototype_gaps()
    {
        using var temp = new TempDir();
        var shell = NewInviteShell(temp);
        var children = InvitePanel(shell).Children;
        var description = (FrameworkElement)children[1];
        var card = (FrameworkElement)children[2];
        var buttons = (FrameworkElement)children[3];
        var note = (FrameworkElement)children[4];

        Assert.Equal(10d, description.Margin.Top);  // başlık → açıklama
        Assert.Equal(14d, card.Margin.Top);          // açıklama → kart
        Assert.Equal(16d, buttons.Margin.Top);        // kart → butonlar
        Assert.Equal(12d, note.Margin.Top);           // butonlar → not
        GC.KeepAlive(shell);
    }

    [StaFact]
    public void Card_row_labels_are_text_secondary()
    {
        using var temp = new TempDir();
        var shell = NewInviteShell(temp);

        var labels = DsResources.Descendants(InvitePanel(shell))
            .OfType<TextBlock>()
            .Where(t => t.Text is "Repository root" or "Layers")
            .ToList();
        Assert.Equal(2, labels.Count);
        foreach (var label in labels)
            Assert.Equal(
                DsResources.ColorOf((Brush)shell.FindResource("Brush.TextSecondary")),
                DsResources.ColorOf(label.Foreground));
        GC.KeepAlive(shell);
    }

    [StaFact]
    public void Card_row_values_are_11px_mono()
    {
        using var temp = new TempDir();
        var shell = NewInviteShell(temp);

        Assert.Equal((double)shell.FindResource("FontSize.2xs"), shell.PART_SetupRootValue.FontSize);
        Assert.Equal((double)shell.FindResource("FontSize.2xs"), shell.PART_SetupLayersValue.FontSize);
        GC.KeepAlive(shell);
    }

    [StaFact]
    public void Card_is_292_wide()
    {
        using var temp = new TempDir();
        var shell = NewInviteShell(temp);
        var card = (Border)InvitePanel(shell).Children[2];

        Assert.Equal(292d, card.Width);
        GC.KeepAlive(shell);
    }
}
