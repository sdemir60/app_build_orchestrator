using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Bir <see cref="UserControl"/>'ün UI Automation peer'ı: adı/sınıfı WPF'in kendi <see cref="UserControlAutomationPeer"/>'ı
/// gibi, denetim TÜRÜ ise sahibinin bildirdiği gerçek rol (satır → ListItem, panel → Pane, menü → Menu …).
///
/// <para><b>Neden (ÖLÇÜLDÜ):</b> bir UIA istemcisi bağlıyken WPF her yerleşim turundan sonra peer ağacını yürür
/// (<c>ContextLayoutManager.fireAutomationEvents</c> → <c>AutomationPeer.UpdateSubtree</c>). Yürüyüş yalnız geçersizlenen yola
/// iner — tek istisna <see cref="AutomationControlType.Custom"/>: o türdeki peer'ın alt ağacı HER turda baştan sayılır
/// (<c>UpdateSubtree</c>: <c>ControlType.Custom == GetControlType()</c>). <c>UserControl</c>'ün varsayılan peer'ı tam olarak
/// Custom'dır; 30 s'lik görünür koşu izinde UI thread'inin meşgul süresinin %23'ü (2,0 s) bu yürüyüştü ve en büyük payı
/// kırk görsel düğümlü proje satırlarının her karede yeniden sayılması aldı. Gerçek bir rol bildiren peer yalnız kendi
/// alt ağacında bir değişiklik olduğunda yürünür; ekran okuyucu da "özel denetim" yerine anlamlı rolü duyar.</para>
///
/// <para>Uygulamanın her <c>UserControl</c>'ü <c>OnCreateAutomationPeer</c>'da bu peer'ı rolüyle döndürür
/// (kaynak guard'ı: <c>AutomationRoleTests</c>).</para>
/// </summary>
internal sealed class UserControlRolePeer(UserControl owner, AutomationControlType role) : UserControlAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => role;
}
