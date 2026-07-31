using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BuildOrchestrator.App;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T2 · madde 2.4] <c>"No projects match this filter."</c> — design-v1 §2.4: <i>"Filtre eşleşmezse:
/// <c>No projects match this filter.</c>"</i>
///
/// <para><b>Ölçülen kusur:</b> bu string TÜM ağaçta YOKTU (<c>src</c> + <c>tests</c> grep'i boş);
/// <c>InteractionText</c> yalnız <c>PickRepository*</c> / <c>NoProjectsFound</c> / <c>GraphEmpty</c> /
/// <c>StreamEmpty</c> taşıyordu. Kullanıcı hiçbir şeyi eşleştirmeyen bir filtre uyguladığında BOŞ bir panel
/// görüyordu — "veri yok" ile "filtren çok dar" ayırt edilemiyordu.</para>
///
/// <para><b>2.5 ile ilişkisi:</b> bu metin ancak liste GERÇEKTEN filtrelendiğinde anlamlıdır (2.5). İkisi
/// birlikte: filtre listeyi boşaltır, panel de nedenini söyler.</para>
/// </summary>
[Collection("Console UI (serial)")]
public class NoFilterMatchTests
{
    private const string Copy = "No projects match this filter.";

    /// <summary>[T2 fix-1 · I-F] Ortak fixture (<see cref="MainWindowHost.NewWithProjects"/>).
    /// <paramref name="withProjects"/> false → hiç düğüm akmaz (0-proje workspace'i).</summary>
    private static (MainWindow window, RunViewModel vm) NewShell(TempDir temp, bool withProjects = true)
    {
        var (window, vm, _) = withProjects
            ? MainWindowHost.NewWithProjects(temp, ("Alpha", null), ("Beta", null))
            : MainWindowHost.NewWithProjects(temp);
        return (window, vm);
    }

    /// <summary>Kabukta GÖRÜNÜR durumdaki metin blokları (kullanıcının gerçekten okuduğu şey).
    /// <para><c>IsVisible</c> KULLANILAMAZ: gerçek bir <c>PresentationSource</c> (HWND) ister ve bu testler
    /// pencereyi hiç <c>Show()</c> etmez (bkz. <see cref="MainWindowHost"/>). Bunun yerine öğenin KENDİ ve TÜM
    /// atalarının <see cref="UIElement.Visibility"/>'si denetlenir — boş-durum overlay'leri zaten KAPSAYICI
    /// üzerinden gizlenir, bu yüzden yalnız yaprağa bakmak yanıltıcı olurdu.</para></summary>
    private static IReadOnlyList<string> VisibleTexts(MainWindow window) =>
        [.. DsResources.Descendants(window.Shell).OfType<TextBlock>()
            .Where(t => IsShown(t, window.Shell))
            .Select(t => t.Text)];

    // [A13/T3 fix-2 · 7] Yürüyüşün kendisi DsResources.SelfAndAncestors'ta (kopya YASAK); buradaki KURAL
    // (görünürlük + kökte dur) yerinde kalır — semantik değişmedi.
    private static bool IsShown(DependencyObject node, DependencyObject root)
    {
        foreach (var n in DsResources.SelfAndAncestors(node))
        {
            if (n is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(n, root)) break;
        }
        return true;
    }

    [StaFact]
    public void A_filter_that_matches_nothing_explains_itself_verbatim()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShell(temp);
        Assert.DoesNotContain(Copy, VisibleTexts(window)); // ön-koşul: filtre yokken metin YOK

        vm.ActiveFilter = ProjectFilter.Failed; // hiçbir proje failed değil
        window.Shell.UpdateLayout();

        Assert.Contains(Copy, VisibleTexts(window));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_text_query_that_matches_nothing_shows_the_same_message()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShell(temp);

        vm.ProjectQuery = "zzzz";
        window.Shell.UpdateLayout();

        Assert.Contains(Copy, VisibleTexts(window));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Clearing_the_filter_takes_the_message_away_again()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShell(temp);
        vm.ActiveFilter = ProjectFilter.Failed;
        window.Shell.UpdateLayout();
        Assert.Contains(Copy, VisibleTexts(window)); // ön-koşul

        vm.ToggleFilter(null);
        window.Shell.UpdateLayout();

        Assert.DoesNotContain(Copy, VisibleTexts(window));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// <b>İki boş-durum KARIŞTIRILAMAZ.</b> "Hiç proje yok" (repo Sync'lendi, 0 proje) ile "projeler var ama
    /// filtre hiçbirini eşleştirmiyor" FARKLI şeylerdir ve farklı metin gösterirler — brief'in açık şartı.
    /// </summary>
    [StaFact]
    public void An_empty_workspace_still_shows_the_zero_project_message_not_the_filter_one()
    {
        using var temp = new TempDir();
        var (window, vm) = NewShell(temp, withProjects: false);
        window.Shell.UpdateLayout();

        var texts = VisibleTexts(window);
        Assert.Contains(InteractionText.NoProjectsFound, texts);
        Assert.DoesNotContain(Copy, texts);
        GC.KeepAlive(window);
    }

    /// <summary>Repo hiç seçilmemişken filtre metni ASLA çıkmaz — orada davet (PickRepository) vardır.</summary>
    [StaFact]
    public void With_no_repository_the_invitation_wins_over_the_filter_message()
    {
        using var temp = new TempDir();
        var (window, vm) = MainWindowHost.New(temp);
        MainWindowHost.Realize(window);

        vm.ActiveFilter = ProjectFilter.Failed;
        window.Shell.UpdateLayout();

        var texts = VisibleTexts(window);
        Assert.Contains(InteractionText.PickRepositoryTitle, texts);
        Assert.DoesNotContain(Copy, texts);
        GC.KeepAlive(window);
    }
}
