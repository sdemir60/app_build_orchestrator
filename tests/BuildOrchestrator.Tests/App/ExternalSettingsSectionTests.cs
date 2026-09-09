using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Tests.Git;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Ayarlar'daki EXTERNAL PROJECTS bölümünün GERÇEKTEN çözümlenip çizildiği: headless süit XAML runtime
/// çözümlemesini görmez, o yüzden yeni bölüm bir realize testiyle pinlenir.
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ExternalSettingsSectionTests
{
    private static readonly ExternalProject Mail = new("Mail", @"D:\ext\mail", @"D:\ext\mail\Mail.sln");

    [StaFact]
    public void The_section_realizes_with_its_caption_and_seeded_rows()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(run => run.ExternalProjects = [Mail]);
        using (scope)
        {
            var captions = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains("EXTERNAL PROJECTS", captions);
            Assert.Contains(captions, t => t.Contains("updated from version control and built first", StringComparison.Ordinal));
        }
    }

    [StaFact]
    public void A_seeded_row_shows_its_target_and_version_control_badge()
    {
        using var repo = new GitTestRepo();
        string target = Path.Combine(repo.RootPath, "Mail.sln");
        File.WriteAllText(target, "");
        var external = new ExternalProject("Mail", repo.RootPath, target);

        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(run => run.ExternalProjects = [external]);
        using (scope)
        {
            var texts = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains(target, texts);
            Assert.Contains("git", texts); // rozet yalnız metindir — yeni renk/ikon yok
            Assert.Contains(Descendants<TextBox>(dialog), b => b.Text == "Mail");
        }
    }

    [StaFact]
    public void The_row_actions_carry_accessible_names()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized(run => run.ExternalProjects = [Mail]);
        using (scope)
        {
            var names = Descendants<Button>(dialog)
                .Select(AutomationProperties.GetName)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            Assert.Contains(AccessibilityNames.AddExternal, names);
            Assert.Contains(AccessibilityNames.DeleteExternal, names);
            Assert.Contains(AccessibilityNames.ExternalRoot, names);
            Assert.Contains(AccessibilityNames.ExternalTarget, names);
            Assert.Contains(AccessibilityNames.ExternalName,
                Descendants<TextBox>(dialog).Select(AutomationProperties.GetName));
        }
    }

    [StaFact]
    public void With_no_externals_the_section_still_realizes_with_only_the_add_button()
    {
        var (dialog, _, _, scope) = SettingsDialogHost.OpenRealized();
        using (scope)
        {
            Assert.Contains("EXTERNAL PROJECTS", Descendants<TextBlock>(dialog).Select(t => t.Text));
            Assert.DoesNotContain(AccessibilityNames.DeleteExternal,
                Descendants<Button>(dialog).Select(AutomationProperties.GetName));
        }
    }

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
