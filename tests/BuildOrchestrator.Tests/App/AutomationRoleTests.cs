using System.Reflection;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf G2 · ÖLÇÜLDÜ] Bir UI Automation istemcisi bağlıyken WPF her yerleşim turundan sonra peer ağacını yürür
/// (<c>ContextLayoutManager.fireAutomationEvents</c> → <c>AutomationPeer.UpdateSubtree</c>). Yürüyüş yalnız geçersizlenen
/// yola iner — TEK istisna: denetim türü <see cref="AutomationControlType.Custom"/> olan peer'ın alt ağacı HER turda
/// baştan sayılır (WPF kaynağı, <c>UpdateSubtree</c>: <c>ControlType.Custom == GetControlType()</c>). <c>UserControl</c>'ün
/// varsayılan peer'ı tam olarak Custom'dır. 30 s'lik görünür koşu izinde UI thread'inin meşgul süresinin %23'ü
/// (2,0 s) bu yürüyüştü; en büyük payı kırk görsel düğümlü proje satırlarının her karede yeniden sayılması aldı.
///
/// <para><b>Kural:</b> uygulamanın her <c>UserControl</c>'ü gerçek rolünü bildirir (proje satırı → ListItem, paneller →
/// Pane, …): yürüyüş değişmeyen satırlara uğramaz ve ekran okuyucu "özel denetim" yerine anlamlı bir rol duyar. Ad
/// yine <c>AutomationProperties.Name</c>'den ulaşır (<see cref="AccessibilityTests"/> bunu ayrıca tarar).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class AutomationRoleTests
{
    [StaFact]
    public void A_project_row_is_a_list_item_to_UI_Automation_and_still_carries_its_name()
    {
        var vm = new ProjectRowViewModel(@"C:\p\Foo.csproj", "Foo", ProjectRowState.Pending);
        var row = new ProjectRow { AnimationsEnabledProvider = () => false, DataContext = vm };
        var window = DsResources.Realize(DsResources.NewHost(), row);

        var peer = UIElementAutomationPeer.CreatePeerForElement(row);
        Assert.NotNull(peer);
        Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
        Assert.Equal("Foo", peer.GetName());
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_graph_panel_is_a_pane_to_UI_Automation()
    {
        var view = new GraphView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(DsResources.NewHost(), view);

        var peer = UIElementAutomationPeer.CreatePeerForElement(view);
        Assert.NotNull(peer);
        Assert.Equal(AutomationControlType.Pane, peer.GetAutomationControlType());
        GC.KeepAlive(window);
    }

    /// <summary>Kaynak guard'ı: yeni bir UserControl sessizce Custom'a (her yerleşimde baştan sayılan peer) düşemez.
    /// Rol, uygulama derlemesindeki bir <c>OnCreateAutomationPeer</c> override'ından gelmelidir — doğrudan ya da bir
    /// uygulama taban sınıfından.</summary>
    [Fact]
    public void Every_user_control_of_the_app_declares_its_automation_role()
    {
        var app = typeof(ProjectRow).Assembly;
        var offenders = app.GetTypes()
            .Where(t => typeof(UserControl).IsAssignableFrom(t))
            .Where(t => t.GetMethod("OnCreateAutomationPeer", BindingFlags.Instance | BindingFlags.NonPublic)?.DeclaringType?.Assembly != app)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0,
            "Rolünü bildirmeyen UserControl'ler (varsayılan peer Custom'dır ve her yerleşimde baştan sayılır): " +
            string.Join(", ", offenders));
    }
}
