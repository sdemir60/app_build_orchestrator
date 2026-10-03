using System.Windows.Threading;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App;

/// <summary>
/// [perf Faz A · A1] Pencerenin <b>"yüzey gizli" sinyali</b>: tek yazar, tek okuma noktası.
///
/// <para>Pencere tepsideyken (ya da hiç gösterilmemişken) kimsenin görmediği ekran işi yapılmaz. Bunun için TEK
/// sinyal vardır: kalıtsal attached DP <see cref="HiddenSurface.IsHiddenProperty"/>. Yalnız
/// <see cref="SetSurfaceHidden"/> yazar (pencerenin kendisine); torunlar miras alır, görünümler
/// <see cref="HiddenSurface.GetIsHidden"/> okur. Bu sinyali okuyan yüzeyler: açılış koreografisi ve adım
/// bekletmesi (<see cref="ChoreographyMayPlay"/>), bitiş finali ve konsol belgesi (batch'ler gizliyken yazılmaz;
/// dönüşte <see cref="ResyncAfterShow"/> kurar).</para>
///
/// <para><b>Üretim kablajı:</b> <c>IsVisibleChanged</c> (ctor'da tek abonelik) ve <see cref="StartInTray"/>
/// (pencere hiç gösterilmediği için olay ateşlenmez). Testler <see cref="SetSurfaceHidden"/>'ı doğrudan çağırır
/// (<see cref="OnGlobalHotkey"/> deseni: headless'ta pencere gösterilemez).</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>Yüzey şu an gizli mi — pencerenin KENDİ değeri (<see cref="HiddenSurface.IsHiddenProperty"/>).</summary>
    internal bool IsSurfaceHidden => HiddenSurface.GetIsHidden(this);

    /// <summary>[test yüzeyi] Koreografi ve adım bekletmesinin motion girdisini ÖRNEK BAŞINA zorlar
    /// (<c>null</c> ⇒ üretim sinyali). Headless'ta <c>App.Motion</c> null'dur (= reduced-motion) ve koreografi hiç
    /// oynamaz; "gizli pencerede oynamaz" kuralı ancak koreografinin OYNAYABİLDİĞİ bir pencerede sınanabilir.
    /// <b>Neden <c>MotionScope</c> değil:</b> o statik <c>App.Motion</c>'ı değiştirir ve paralel koşan test
    /// sınıflarına sızar; örnek-başına seam sızmaz. Bu seam YALNIZ <c>_choreographer</c>/<c>_stepHold</c>
    /// girdisidir: grafın kendi kapısı (<c>GraphView.AnimationsEnabledProvider</c>) ve tepsi göstergesi (ham
    /// <see cref="MotionGate.StaticAnimationsEnabled"/> okur, gizli modda DURMAZ) etkilenmez.</summary>
    internal bool? AnimationsForTest { get; set; }

    /// <summary>Koreografinin motion girdisi: test seam'i, yoksa üretimdeki statik sinyal. Statik sinyalin TEK
    /// ifadesi <see cref="MotionGate.StaticAnimationsEnabled"/>'tır; ikinci bir kopyası yazılmaz.</summary>
    private bool AnimationsEnabledForChoreography() => AnimationsForTest ?? MotionGate.StaticAnimationsEnabled;

    /// <summary>Açılış koreografisinin VE adım bekletmesinin TEK kapısı (ikisi de bu delegeyi alır): hareket açık
    /// ve yüzey görünür. Kapalıyken koreografi hiç oynamaz — kapsam tek adımda işaretlenir ve koşu komutu hemen
    /// gider (azaltılmış-hareket ile AYNI dal); bekletme anında biter.</summary>
    private bool ChoreographyMayPlay() => AnimationsEnabledForChoreography() && !IsSurfaceHidden;

    /// <summary>
    /// Sinyali yazar. Üretimde <c>IsVisibleChanged</c> ve <see cref="StartInTray"/> çağırır; testler doğrudan
    /// çağırır. Aynı değere ikinci yazım no-op'tur.
    ///
    /// <para><b>Gizlenince</b> oynayan açılış koreografisi kesilir — bekleyen koşu komutu
    /// (<c>OperationChoreographer.Finish</c>) o anda serbest kalır, işaretler (<c>Marked</c>) KORUNUR, koşu başlayınca
    /// statü kanalı onları devralır — ve oynayan bitiş finali kesilir. Finali yalnız OYNUYORSA keser:
    /// <c>CancelEndFinale</c> filtre askısını da kaldırır ve askı <c>BeginOperation</c>'dan koşunun bitişine dek
    /// sürmelidir; koşu ortasında çağrılsaydı pencere geri geldiğinde graf filtreyi koşu sürerken uygulamış olurdu.</para>
    ///
    /// <para><b>Görününce</b> dönüş kurulumu (<see cref="ResyncAfterShow"/>) ilk layout turundan SONRA koşar.</para>
    /// </summary>
    internal void SetSurfaceHidden(bool hidden)
    {
        if (IsSurfaceHidden == hidden) return;
        HiddenSurface.SetIsHidden(this, hidden);
        if (!hidden)
        {
            Dispatcher.InvokeAsync(ResyncAfterShow, DispatcherPriority.Loaded);
            return;
        }
        _choreographer.Cancel(_vm.Projects);
        if (Shell.GraphHost.IsEndFinalePlaying) Shell.GraphHost.CancelEndFinale();
    }

    /// <summary>[test yüzeyi] Konsol pompasının şu anki reseed nesli: pompa tick etmeyen bir fixture'da testler
    /// bir batch'i <see cref="AppendConsoleBatch"/>'e pompanın yaptığı gibi bu damgayla verir.</summary>
    internal long ConsoleReseedGen => _console.CurrentReseedGen;

    /// <summary>Gizliyken konsol belgesine yazılmayan bir batch (ya da temizlik) oldu: ekrandaki belge modelin
    /// (<c>RunViewModel</c> tamponu) gerisinde. <see cref="ResyncAfterShow"/> sıfırlar.</summary>
    private bool _consoleStaleWhileHidden;

    /// <summary>Pencere gizlilikten dönünce, ilk layout turundan SONRA bir kez koşar
    /// (<see cref="DispatcherPriority.Loaded"/>). Gizliyken biriken ekran işi burada tek seferde kurulur;
    /// koreografi ve final gizliyken zaten oynamadığı ve görününce yeniden başlamadığı için onlardan kurulacak bir
    /// şey yoktur.
    ///
    /// <para><b>Konsol:</b> gizliyken batch'ler ve temizlik belgeye yazılmadı (<see cref="_consoleStaleWhileHidden"/>);
    /// belge modelin TAM metninden bir kez, <b>tilt'siz</b> kurulur — anlatı için <c>RunViewModel.SeedRunDocument</c>,
    /// proje logu açıksa <c>RunViewModel.SeedProjectDocument</c>. İkisi de reseed-drop sentinel'ini yazar: uçuştaki bayat
    /// batch'ler <c>ConsoleBatchRouter</c> kararıyla düşer, YENİ bir tampon yolu açılmaz. Model boşsa idle "ready"
    /// satırı geri gelir.</para>
    ///
    /// <para><b>Gizliyken koşmaz:</b> göster → gizle, bu Loaded-öncelikli çağrıdan ÖNCE gelmiş olabilir (kullanıcı
    /// pencereyi hemen geri indirir). O durumda burası hiçbir şey yapmaz ve "ekran bayat" bayrakları yerinde kalır:
    /// gizli bir ağaca kurmak boşa iş olur ve bayrakları erken silerdi — sonraki gerçek gösterim kendi çağrısını
    /// kuyruklar.</para></summary>
    internal void ResyncAfterShow()
    {
        // Göster → gizle, bu Loaded-öncelikli çağrıdan ÖNCE gelmiş olabilir: gizli bir ağaca kurmak boşa iş olur ve
        // "ekran bayat" bayraklarını erken siler (sonraki gerçek gösterim bayat bir ekranla açılırdı).
        if (IsSurfaceHidden) return;
        if (_consoleStaleWhileHidden)
        {
            _consoleStaleWhileHidden = false;
            var activeProjectId = _vm.ActiveProjectId;
            if (activeProjectId is null)
                _vm.SeedRunDocument(text => Shell.ConsoleViewControl.ReplaceRunDocument(text));
            else
                _vm.SeedProjectDocument(activeProjectId, text => Shell.ConsoleViewControl.ReplaceProjectDocument(SplitLogLines(text)));
            if (_vm.GetActiveLineCount() == 0) Shell.ConsoleViewControl.ShowReady();
        }
    }
}
