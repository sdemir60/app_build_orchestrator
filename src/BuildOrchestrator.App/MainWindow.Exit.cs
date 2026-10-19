using System.Windows;
using System.Windows.Threading;
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
        ConfirmExitInFront = () => Dispatcher.BeginInvoke(ConfirmExitInFrontNow);
    }

    /// <summary>[test dikişi] × (Close to tray kapalıyken) ya da tepsi → Exit, iş varken: onay için pencereyi öne getirip kartı açan
    /// çağrı. Üretim değeri işini kendi dispatcher turuna ERTELER (<see cref="ConfirmExitInFrontNow"/>): × yolunda çağrı
    /// <see cref="OnClosing"/>'in içinden gelir ve WPF, kapanış sürerken <c>Show</c>'u InvalidOperationException ile reddeder; kartın
    /// popup'ı da yerleşmiş, görünür bir hedef ister. Testler pencere GÖSTERMEZ, sayaçlı bir sahteyle değiştirir.</summary>
    internal Action ConfirmExitInFront { get; set; } = null!;

    /// <summary>[P3 · Task 3 · Exit] Tepsi → Exit: Close to tray'e bakılmaz — menüdeki Exit'in anlamı zaten "tamamen kapat"tır. × ile
    /// AYNI yol (<see cref="RequestExitFromOutside"/>).</summary>
    internal void ExitFromTray() => RequestExitFromOutside();

    /// <summary>[Exit] Pencerenin dışından gelen tam çıkış isteği — tepsi → Exit ve Close to tray kapalıyken ×: Exit butonunun kararı.
    /// Uçuşta iş yoksa uygulama pencere gösterilmeden kapanır; varsa hiçbir şey durdurulmaz, pencere öne gelir ve onay kartı açılır
    /// (<see cref="ConfirmExitInFront"/>) — gizli (tepsideki) ya da küçültülmüş (görev çubuğundan kapatılan) bir pencere kartı
    /// gösteremezdi. Çıkış zaten durduruyorsa (kartın "Stop and exit"i) ikinci istek hiçbir şey yapmaz.</summary>
    private void RequestExitFromOutside()
    {
        if (_vm.ExitPending) return;
        if (_vm.ExitWork == ExitWork.None)
        {
            _vm.RequestExit(); // uçuşta iş yok: ExitReady → ExitNow bu çağrının içinden gelir
            return;
        }
        ConfirmExitInFront();
    }

    /// <summary>[Exit] <see cref="ConfirmExitInFront"/>'in ertelenen gövdesi — kararı kendi anında yeniden sorar: arada gerçek kapanış
    /// ya da çıkış başladıysa hiçbir şey yapmaz (kapanmakta olan pencere bir kare için öne gelmez); iş arada bittiyse Exit butonunun
    /// kuralı geçerlidir — uygulama kartsız, pencere gösterilmeden kapanır; aksi hâlde pencere öne gelir ve kart, pencere yerleştikten
    /// sonra açılır. internal: test yüzeyi — ertelenen tur sahte dikişle hiç koşmaz.</summary>
    internal void ConfirmExitInFrontNow()
    {
        if (_exiting || _vm.ExitPending) return;
        if (_vm.ExitWork == ExitWork.None)
        {
            _vm.RequestExit();
            return;
        }
        ShowFromTray();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!_exiting && !_vm.ExitPending && !IsExitCardOpen) RequestExitFromUser();
        });
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

    /// <summary>Kartın birincil düğmesi: kart kapanır ve çıkış istenir. Uçuşta iş varsa kapanış ekranı açılır — işin adı ve ilerlemesi,
    /// "Stopping …" adımı ve ikinci adımda kartın saydığı süreçler — ve VM işi durdurur; yoksa uygulama ekransız, hemen kapanır. Ekran çıkış
    /// isteğinden ÖNCE açılır: kesilen bir işte <c>ExitReady</c> isteğin içinden gelir ve ekranı bulmalıdır (<see cref="OnExitReady"/>).</summary>
    private void StopAndExit()
    {
        var work = _vm.ExitWork;
        int processes = ExitCardView.Processes.Sum(p => p.Count);
        CloseExitCard(returnFocusToButton: false);
        if (work != ExitWork.None)
            ClosingOverlay.PlayExit(ExitText.StoppingStep(work), ExitText.ClosingContext(work, _vm.ExitProgress),
                ExitText.EndingStep(processes));
        _vm.RequestExit();
    }

    /// <summary>VM çıkışın hazır olduğunu söyledi (<see cref="RunViewModel.ExitReady"/>, bir kez): Exit'in kapanış ekranı oynuyorsa ikinci
    /// adımına geçer ve uygulamayı çubuğu dolunca kapatır (<see cref="OnClosingScreenFilled"/>); oynamıyorsa — uçuşta iş yoktu, restart
    /// ekranının çubuğu dolmuştu, ya da × / tepsi yolu — uygulama hemen kapanır.</summary>
    private void OnExitReady()
    {
        if (ClosingOverlay.IsPlayingExit) ClosingOverlay.MarkStopped();
        else ExitNow();
    }
}
