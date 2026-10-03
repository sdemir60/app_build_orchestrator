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
/// bekletmesi (<see cref="ChoreographyMayPlay"/>), bitiş finali, grafa statü/faz/seçim itişleri
/// (<see cref="_graphStaleWhileHidden"/>), olay akışı satırları (<c>EventStreamView</c> sinyali kendisi okur) ve konsol
/// belgesi (batch'ler gizliyken yazılmaz; dönüşte <see cref="ResyncAfterShow"/> kurar), sticky şerit, Build menüsü ve proje
/// satırları (her biri kendi bayrağıyla dönüşte tek geçişte yetişir), proje listesi (<see cref="_listStaleWhileHidden"/>) ve 200 ms'lik tikin gövdesi (<see cref="_tickStaleWhileHidden"/>; motor sessizlik bekçisi hariç — o gizliyken de koşar).</para>
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

    /// <summary>Gizliyken konsol belgesine yazılmayan bir batch (ya da temizlik) oldu: ekrandaki belge modelin
    /// (<c>RunViewModel</c> tamponu) gerisinde. <see cref="ResyncAfterShow"/> sıfırlar.</summary>
    private bool _consoleStaleWhileHidden;

    /// <summary>Gizliyken grafa itilmeyen bir statü, koşu fazı, seçim ya da filtre yenilemesi oldu
    /// (<c>PushGraphStatuses</c>, <c>PushGraphRunPhase</c>, <c>PushGraphSelection</c> ve <c>RefreshGraphFilter</c> gizliyken
    /// yalnız bunu kaldırır): graf modelin gerisinde. <see cref="ResyncAfterShow"/> dördünü tek seferde yapar ve sıfırlar.
    /// Topoloji (<c>RebuildGraph</c>) bu bayrağa bağlı değildir: gizliyken de kurulur ve dönüşte tekrarlanmaz. Grafın kendi
    /// bekletmesi (<c>GraphView.SetGraph</c> / <c>UpdateStatuses</c>) PANELİN <c>Visibility</c>'sine bağlıdır (yerleşim kipi
    /// graf panelini gizlediğinde), pencerenin gizliliğine değil.</summary>
    private bool _graphStaleWhileHidden;

    /// <summary>Gizliyken proje listesi kurulmadı ve kurulması GEREKİYORDU: topoloji değişti (<c>RefreshProjectGroups</c>) ya da
    /// görünür satır kümesinin imzası değişti (<c>RefreshVisibleRows</c>; imza aynıysa bayrak kalkmaz — koşu olayları listeyi
    /// kirletmez). <see cref="ResyncAfterShow"/> listeyi TEK geçişte, reveal'siz kurar. Satırların kendi görünümü bundan
    /// bağımsızdır: her <c>ProjectRow</c> kendi bayrağıyla yetişir.
    ///
    /// <para>Bayrağı listeyi YAZAN her yol düşürür (<c>ApplyProjectGroups</c>, <c>BlankPlanSurface</c>), yalnız
    /// <see cref="ResyncAfterShow"/> değil: dönüş ile Loaded-öncelikli kurulum arasında görünür bir topoloji kurulumu olduysa dönüş
    /// kurulumu listeyi ikinci kez (reveal'siz, ilkinin belirişini keserek) kurmaz; Sync ekranı baştan başlatırken de boşaltılmış
    /// listeye eski topoloji geri yazılmaz.</para></summary>
    private bool _listStaleWhileHidden;

    /// <summary>Gizliyken 200 ms'lik tikin gövdesi atlandı (<c>OnElapsedTick</c>): canlı süreler (koşu süresi, building
    /// satırların süresi, ETA) ve konsol başlığının satır sayacı modelin gerisinde. Motor sessizlik bekçisi bu bayrağa bağlı
    /// DEĞİLDİR: gizliyken de her tikte koşar. <see cref="ResyncAfterShow"/> sıfırlar.</summary>
    private bool _tickStaleWhileHidden;

    /// <summary>Pencere gizlilikten dönünce, ilk layout turundan SONRA bir kez koşar
    /// (<see cref="DispatcherPriority.Loaded"/>). Gizliyken biriken ekran işi burada tek seferde kurulur;
    /// koreografi ve final gizliyken zaten oynamadığı ve görününce yeniden başlamadığı için onlardan kurulacak bir
    /// şey yoktur.
    ///
    /// <para><b>Konsol:</b> gizliyken batch'ler ve temizlik belgeye yazılmadı (<see cref="_consoleStaleWhileHidden"/>);
    /// belge modelin TAM metninden bir kez, <b>tilt'siz</b> kurulur — anlatı için <c>RunViewModel.SeedRunDocument</c>,
    /// proje logu açıksa <c>RunViewModel.SeedProjectDocument</c>. İkisi de reseed-drop sentinel'ini yazar: uçuştaki bayat
    /// batch'ler <c>ConsoleBatchRouter</c> kararıyla düşer, YENİ bir tampon yolu açılmaz. Anlatı boşsa idle "ready"
    /// satırı geri gelir; boş bir proje logu, kart seçimiyle AYNI kuralla (<c>ProjectDocumentLines</c>) o projenin
    /// boş-durum metnini gösterir.</para>
    ///
    /// <para><b>Graf:</b> gizliyken statü, faz, seçim itişleri ve filtre yenilemesi yalnız bayrağı kaldırdı
    /// (<see cref="_graphStaleWhileHidden"/>); dönüşte dördü TEK seferde, üretimdeki sırayla uygulanır: koşu fazı,
    /// statüler, seçim, filtre. Topoloji bu yola girmez: gizliyken de kurulmuştu ve dönüşte tekrarlanmaz. Koşu gizliyken
    /// başladıysa graf soluklaşma geçişini (<c>GraphView.HoldStatusesUntilDimmed</c>) dönüşte oynar — kabul edilen tek,
    /// kısa geçiş.</para>
    ///
    /// <para><b>Liste:</b> gizliyken topoloji ve görünür-küme değişimleri listeye yazılmadı (<see cref="_listStaleWhileHidden"/>);
    /// dönüşte liste modelden TEK geçişte, <b>reveal'siz</b> kurulur (<c>ApplyProjectGroups(reveal: false)</c>) — gizlilikte olan bir
    /// değişimin kademeli belirişi geriye dönük oynanmaz. İmza <c>ApplyProjectGroups</c>'ta yazıldığı için ardından
    /// <c>RefreshVisibleRows</c> çağırmak boş bir karşılaştırma olurdu. Sticky şerit, Build menüsü ve satırlar burada DEĞİL:
    /// kalıtsal sinyalin kendi değişiminde (<c>OnPropertyChanged</c>) her biri kendi bayrağıyla yetişir.</para>
    ///
    /// <para><b>Tik:</b> gizliyken 200 ms'lik tik yalnız motor sessizlik bekçisini koşturdu
    /// (<c>RunViewModel.TickElapsed(false)</c>); canlı süreler ve konsol başlığının satır sayacı yazılmadı
    /// (<see cref="_tickStaleWhileHidden"/>). Dönüşte tek <c>TickElapsed(true)</c> hepsini yetiştirir ve sayaç yenilenir —
    /// kullanıcı bir sonraki tiki beklemeden güncel süreyi görür.</para>
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
            {
                _vm.SeedRunDocument(text => Shell.ConsoleViewControl.ReplaceRunDocument(text));
                if (_vm.GetActiveLineCount() == 0) Shell.ConsoleViewControl.ShowReady();
            }
            else
            {
                var row = _vm.Projects.FirstOrDefault(
                    p => string.Equals(p.Id, activeProjectId, StringComparison.OrdinalIgnoreCase));
                _vm.SeedProjectDocument(activeProjectId,
                    text => Shell.ConsoleViewControl.ReplaceProjectDocument(ProjectDocumentLines(row, text)));
            }
        }
        if (_listStaleWhileHidden)
        {
            // Reveal OYNAMAZ (gizlilikte olan bir değişim geriye dönük oynanmaz). Graf gizliyken zaten yeniden kurulmuştu
            // (RebuildGraph kapısızdır) ve burada tekrarlanmaz — tekrarı dönüşte görünür bir reveal oynatırdı.
            ApplyProjectGroups(reveal: false);
        }
        if (_graphStaleWhileHidden)
        {
            _graphStaleWhileHidden = false;
            // Sıra üretimdekiyle aynıdır (OnVmPropertyChangedForGraph, RebuildGraph): koşu fazı, statüler, seçim; filtre en sonda.
            PushGraphRunPhase();
            PushGraphStatuses();
            PushGraphSelection();
            RefreshGraphFilter();
        }
        if (_tickStaleWhileHidden)
        {
            _tickStaleWhileHidden = false;
            // Gizliyken tik yalnız bekçiyi koşturdu: canlı süreler ve satır sayacı bir sonraki tiki beklemeden modele yetişir.
            _vm.TickElapsed(true);
            Shell.ConsoleHeaderControl.SetLineCount(_vm.GetActiveLineCount());
        }
    }
}
