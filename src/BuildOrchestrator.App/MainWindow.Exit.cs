using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App;

/// <summary>
/// [Exit] Title bar'ın <b>Exit butonu</b>, Ctrl+Q ve <b>onay kartı</b> (<see cref="Views.ExitCard"/>): uygulamayı tamamen kapatır. Uçuşta iş
/// yoksa onay sormadan hemen kapatır (<see cref="RunViewModel.RequestExit"/> → <see cref="RunViewModel.ExitReady"/> → <see cref="ExitNow"/>);
/// iş varsa (Build, Rebuild, Resolve, Clean, Deep Clean, Optimize, Sync, checkout, pull) kapatmaz, butonun altında kartı açar. Kararın sorusu
/// VM'in <see cref="RunViewModel.ExitWork"/>'üdür — <see cref="ExitWork.None"/> "uçuşta iş yok"un tek cevabıdır (<c>WorkspaceIdle</c>).
///
/// <para><b>Kartın kapanış yolları:</b> <c>Cancel</c> ve kartın içindeki Esc (<see cref="Views.PopoverBase.CloseRequested"/> → odak butona
/// döner), butona ikinci basış ve Ctrl+Q (<see cref="OnExitRequested"/>; dışarı tıkın aynı basışı yeniden açamaz — <see cref="PopoverToggle"/>),
/// dışarı tık (<c>StaysOpen=False</c>) ve pencerenin Esc zinciri — kart zincirin EN ÜSTÜDÜR, açık bir dialogdan önce kapanır
/// (<see cref="KeyboardShortcuts.ResolveEsc"/>). Bir dialogun açılışı kartı KAPATMAZ: kart dialogların da üstünde durur.</para>
///
/// <para><b>Süreç listesi</b> (<c>ENDS ON EXIT</c>) yalnız kart açıkken sayılır: açılışta bir kez, sonra <see cref="ExitCardRecount"/>'ta bir
/// (<see cref="ExitCardTimer"/>); kart kapanınca sayım durur — boşta hiçbir şey sorgulanmaz. Uçuşta iş yokken sayılmaz.</para>
///
/// <para><b>Birincil düğme</b> (<see cref="StopAndExit"/>) kartı kapatır ve çıkışı ister; iş sürüyorsa VM onu durdurur (koşu hard stop'la,
/// git yazımı beklenir — <c>RunViewModel.Exit.cs</c>). Karar düğmeye basıldığı ANDAKİ işe göre verilir, kartın o an gösterdiğine göre değil.</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Kartın üst kenarı ile butonun alt kenarı arasındaki boşluk — tek yer; kart bunun kadar aşağıda, sağ kenarı butonun sağ
    /// kenarında durur (<see cref="PopoverPlacement.BelowRightEdge"/>). Güncelleme kartıyla aynı sayı (<see cref="UpdateCardGap"/>).</summary>
    internal const double ExitCardGap = UpdateCardGap;

    /// <summary>Kart açıkken süreç listesinin yeniden sayılma aralığı — derlemeler başlar ve biter.</summary>
    internal static readonly TimeSpan ExitCardRecount = TimeSpan.FromSeconds(1);

    /// <summary>[test dikişi] Kart açıkken süreçleri yeniden sayan zamanlayıcı — üretimde UI thread'inde tık atan
    /// <see cref="DispatcherPollTimer"/>, testte elle tıklatılan sahtesi.</summary>
    internal IPollTimer ExitCardTimer { get; set; } = null!;

    /// <summary>[test dikişi] Kartın süreç sayımı — üretimde outer job'un sayımı (<see cref="JobProcessCensus.Take"/>), testte sahte bir
    /// liste (gerçek job yok).</summary>
    internal Func<IReadOnlyList<ProcessTally>> TakeProcessCensus { get; set; } = null!;

    /// <summary>Ctor'dan bir kez: tooltip kataloğun cümlesi + jesti ("Exit — quit and end every process (Ctrl+Q)"), kartın VM'i, yerleşimi,
    /// ikinci basış kapısı ve iki isteği; üretimin zamanlayıcısı ve süreç sayımı.</summary>
    private void SetupExitButton()
    {
        ExitButton.ToolTip = CatalogTooltip(ShortcutId.Exit);
        // Popup içerikleri (görsel ağaç dışı) DataContext'i güvenilir MİRAS ALMAZ → açıkça bağla (güncelleme kartının deseni).
        ExitCardView.DataContext = _vm;
        ExitPopup.CustomPopupPlacementCallback = PopoverPlacement.BelowRightEdgeCallback(ExitCardGap);
        PopoverToggle.Bind(ExitButton, ExitPopup);
        ExitCardView.CloseRequested += () => CloseExitCard(returnFocusToButton: true);
        ExitCardView.ActionRequested += StopAndExit;
        ExitCardTimer = new DispatcherPollTimer(Dispatcher);
        TakeProcessCensus = new JobProcessCensus(_engine.OuterJob, () => _engine.EnginePid).Take;
    }

    /// <summary>Kart açık mı — durumun kaynağı butonun <see cref="DsChrome.IsActiveProperty"/>'sidir (popup'a iki yönlü bağlı).</summary>
    private bool IsExitCardOpen => DsChrome.GetIsActive(ExitButton);

    private void OnExitButtonClick(object sender, RoutedEventArgs e) => OnExitRequested();

    /// <summary>Exit butonu ve Ctrl+Q — AYNI yol: kart açıksa kapatır, değilse çıkışı ister.</summary>
    private void OnExitRequested()
    {
        if (IsExitCardOpen)
        {
            CloseExitCard(returnFocusToButton: true);
            return;
        }
        RequestExitFromUser();
    }

    /// <summary>Kullanıcı uygulamayı kapatmak istedi: uçuşta iş yoksa hemen kapanır, varsa onay kartı açılır.</summary>
    private void RequestExitFromUser()
    {
        if (_vm.ExitWork == ExitWork.None)
        {
            _vm.RequestExit(); // uçuşta iş yok: ExitReady → ExitNow bu çağrının içinden gelir
            return;
        }
        OpenExitCard();
    }

    /// <summary>Kartı açar: süreçleri sayar, butonu açık hâle alır (popup bağlı) ve kart açık kaldıkça yeniden saymayı başlatır.</summary>
    private void OpenExitCard()
    {
        RecountExitProcesses();
        DsChrome.SetIsActive(ExitButton, true);
        ExitCardTimer.Start(ExitCardRecount, OnExitCardTick);
    }

    /// <summary>Kartı kapatır (açık değilse yalnız sayımı durdurur). Kullanıcının kartta başlattığı kapanışta odak butona döner (popover
    /// deseni: return-to-trigger).</summary>
    private void CloseExitCard(bool returnFocusToButton)
    {
        ExitCardTimer.Stop();
        if (!IsExitCardOpen) return;
        DsChrome.SetIsActive(ExitButton, false);
        if (returnFocusToButton) ExitButton.Focus();
    }

    /// <summary>Sayımın tıkı: kart başka bir yoldan kapandıysa (dışarı tık) sayım durur; açıksa süreçler yeniden sayılır.</summary>
    private void OnExitCardTick()
    {
        if (!IsExitCardOpen)
        {
            ExitCardTimer.Stop();
            return;
        }
        RecountExitProcesses();
    }

    /// <summary>Kartın süreç listesini tazeler — uçuşta iş yokken sayılmaz (kart o hâlde listeyi çizmez).</summary>
    private void RecountExitProcesses() =>
        ExitCardView.Processes = _vm.ExitWork == ExitWork.None ? [] : TakeProcessCensus();

    /// <summary>Kartın birincil düğmesi: kart kapanır ve çıkış istenir — uçuşta iş varsa VM onu durdurur, yoksa uygulama hemen kapanır.</summary>
    private void StopAndExit()
    {
        CloseExitCard(returnFocusToButton: false);
        _vm.RequestExit();
    }
}
