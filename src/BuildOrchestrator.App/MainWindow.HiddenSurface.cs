using System.Windows.Threading;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.App;

/// <summary>
/// [perf Faz A · A1] Pencerenin <b>"yüzey gizli" sinyali</b>: tek tanım (<see cref="HiddenSurface"/>), pencere için tek yazar,
/// tek okuma kuralı.
///
/// <para>Pencere tepsideyken (ya da hiç gösterilmemişken) kimsenin görmediği ekran işi yapılmaz. Sinyal kalıtsal attached DP
/// <see cref="HiddenSurface.IsHiddenProperty"/>'dir. Pencerenin KENDİSİNE yalnız <see cref="SetSurfaceHidden"/> yazar; torunlar
/// miras alır, görünümler <see cref="HiddenSurface.GetIsHidden"/> okur. Tek aktarım: Build menüsü ActionBar'ın popup'ında durur
/// ve popup içeriği görsel ağacın parçası DEĞİLDİR (kalıtım ona inmez) — <c>ActionBar</c> değişimi menüye açıkça yazar.</para>
///
/// <para><b>Sinyali okuyan yüzeyler:</b> açılış koreografisi ve adım bekletmesi (<see cref="ChoreographyMayPlay"/>) ile bitiş
/// finali · grafa statü/faz/seçim itişleri ve filtre yenilemesi (<see cref="_graphStaleWhileHidden"/>) · konsol belgesi
/// (batch'ler gizliyken yazılmaz; dönüşte <see cref="ResyncAfterShow"/> kurar) · proje listesi
/// (<see cref="_listStaleWhileHidden"/>) · 200 ms'lik tikin gövdesi (<see cref="_tickStaleWhileHidden"/>; motor sessizlik
/// bekçisi hariç — o gizliyken de koşar) · sticky şerit, Build menüsü, alt çubuğun sayaç chip'leri, proje satırları ve olay
/// akışı satırları (her biri kendi bayrağıyla, kalıtsal sinyalin değişiminde <c>OnPropertyChanged</c> ile dönüşte tek geçişte
/// yetişir) · tepside biten koşunun bellek toplaması (<see cref="CollectAfterRunWhenDue"/>: yalnız gizliyken).</para>
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

    /// <summary>
    /// [perf Faz C · C4] <b>Tepside biten koşunun ardından tek seferlik bellek toplaması.</b> Koşu tepsideyken bitti ve tepsi
    /// göstergesi çıkışını tamamladı: ekranda bu koşuya ait hiçbir şey kalmadı ve koşunun atıkları (canlı satır tamponu
    /// bırakılmıştır — <c>RunViewModel.LiveLineCount</c>) ölü yük olarak durur; toplama onları geri verir.
    ///
    /// <para><b>İki sinyalin birleşimi; hangisi sonra gelirse toplamayı o ister</b> (<see cref="CollectAfterRunWhenDue"/>): koşu
    /// bitti (<c>RunViewModel.EndedRunSerial</c> — HER bitiş yolunda, tampon bırakıldıktan sonra; ctor aboneliği) VE gösterge
    /// çıkışını bitirdi (<see cref="OnTrayIndicatorExitFinished"/> — gizlenme ve sonuç balonundan sonra). Sıra koşuya göre değişir:
    /// normalde koşu önce biter; reduced-motion'da tepside Stop'ta gösterge <c>runStopped</c> anında çıkar, koşu ise
    /// <c>runCompleted</c> ile sonra biter.</para>
    ///
    /// <para><b>Koşu başına bir kez, koşu kimliğine bağlı</b> (<c>RunViewModel.RunSerial</c>): yeni koşu yeni kimlik getirir, aynı
    /// koşunun ikinci sinyali yeniden toplamaz. Pencere görünürken hiç toplanmaz.</para>
    /// </summary>
    private int _collectedRun;

    /// <summary>Göstergesi çıkışını bitirmiş koşunun kimliği — bildirim anındaki <c>RunViewModel.RunSerial</c> (koşu sürüyorsa o
    /// koşu, bittiyse biten koşu: kimlik yalnız yeni bir koşu başlarken artar).</summary>
    private int _indicatorExitedRun;

    /// <summary>[perf Faz C · C4] Toplamanın TEK gerçek çağrısı; ne zaman çağrılacağına <see cref="CollectAfterRunWhenDue"/> karar
    /// verir. Testler sayaçlı bir sahteyle değiştirir; gerçek toplamayı yalnız üretim toplayıcısının geçerliliğini sınayan test
    /// bir kez koşturur. <b>Bloklayan</b> agresif toplama: <see cref="GCCollectionMode.Aggressive"/> yalnız <c>blocking: true</c> ile
    /// geçerlidir (<c>blocking: false</c> her çağrıda <see cref="ArgumentException"/> fırlatır); nesil 2 sıkıştırılır ve boşalan
    /// bellek işletim sistemine geri verilir. Toplama süresince UI thread'i durur — bu yüzden yalnız pencere gizliyken, sonuç
    /// balonu gösterildikten sonra ve uygulama boştayken yapılır.</summary>
    internal Action MemoryCollector { get; set; } = CollectMemory;

    private static void CollectMemory() => GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

    /// <summary>
    /// [perf Faz C · C4] Tepsi göstergesi çıkış sırasını tamamladı (<c>TrayBuildIndicatorController.ExitCompleted</c>: gösterge
    /// gizlendi, sonuç balonu gösterildi) — toplamanın gösterge sinyali. Çıkış sırasında pencere geri geldiyse bildirim yine gelir
    /// ama kullanıcı ekrandadır: o yolda toplanmaz. <c>internal</c>: test yüzeyi — headless'ta gösterge hiç kurulmaz
    /// (<c>OnSourceInitialized</c> koşmaz), testler bildirimi doğrudan verir.
    /// </summary>
    internal void OnTrayIndicatorExitFinished()
    {
        _indicatorExitedRun = _vm.RunSerial;
        CollectAfterRunWhenDue();
    }

    /// <summary>
    /// İki sinyal de buraya gelir. Toplama yalnız biten koşunun göstergesi de çıktıysa, bu koşu için henüz toplanmadıysa ve pencere
    /// GİZLİYSE istenir. Toplamanın kendisi <see cref="DispatcherPriority.ApplicationIdle"/> ile ertelenir: reduced-motion'da iki
    /// sinyal de koşu bitişinin içinde eşzamanlı gelebilir ve bloklayan toplama koşu bitişinin kalan işini (yüzey yenilemesi,
    /// bekleyen Sync) bekletmemelidir. Ertelenen iş koşulu yeniden sorar (<see cref="CollectWhileHidden"/>).
    /// </summary>
    private void CollectAfterRunWhenDue()
    {
        int run = _vm.EndedRunSerial;
        if (run != _indicatorExitedRun || run == _collectedRun || !IsSurfaceHidden) return;
        _collectedRun = run;
        Dispatcher.InvokeAsync(CollectWhileHidden, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Boşta ertelenen toplama: arada pencere geri geldiyse ya da yeni bir koşu başladıysa toplamaz.</summary>
    private void CollectWhileHidden()
    {
        if (IsSurfaceHidden && !_vm.IsRunInFlight) MemoryCollector();
    }

    /// <summary>Gizliyken konsol belgesine yazılmayan bir batch, temizlik ya da mod geçişi oldu (seçim yolu:
    /// <c>ShowRunConsole</c>, <c>OnSelectedProjectChangedAsync</c>): ekrandaki belge modelin (<c>RunViewModel</c> tamponu)
    /// gerisinde. Başlık ve VM tarafı yine güncellenir; belgeyi <see cref="ResyncAfterShow"/> kurar ve bayrağı sıfırlar.
    ///
    /// <para>Bayrak kalkık kaldıkça batch'ler belgeye YAZILMAZ: gösterim ile Loaded-öncelikli dönüş kurulumu arasındaki aralık
    /// dahil (<c>AppendConsoleBatch</c> kapısı: gizli YA DA bayrak kalkık) — bayat belgeye basılan bir batch, dönüş kurulumu
    /// belgeyi tam metinden yeniden kurunca boşa giderdi. Belgeyi modelin tam metninden kuran görünür yollar, mod geçişi
    /// (<c>ShowRunConsole</c>) ve proje belgesi (<c>OnSelectedProjectChangedAsync</c>), bayrağı da düşürür: dönüş kurulumu o
    /// belgeyi ikinci kez kurmaz. Temizlik (<c>ConsoleCleared</c>) düşürmez: belgeyi boşaltır ama boş anlatının "ready"
    /// satırını kurmaz, onu dönüş kurulumu kurar.</para></summary>
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
    /// <para><b>Konsol:</b> gizliyken batch'ler, temizlik ve mod geçişi (kart seçimi: <c>ShowRunConsole</c>,
    /// <c>OnSelectedProjectChangedAsync</c>) belgeye yazılmadı (<see cref="_consoleStaleWhileHidden"/>); başlık ve VM tarafı
    /// yine güncellendi. Belge modelin TAM metninden bir kez, <b>tilt'siz</b> kurulur — hangi belgenin kurulacağını o anki
    /// <c>ActiveProjectId</c> söyler: anlatı için <c>RunViewModel.SeedRunDocument</c>, proje logu açıksa
    /// <c>RunViewModel.SeedProjectDocument</c>. İkisi de reseed-drop sentinel'ini yazar: uçuştaki bayat batch'ler
    /// <c>ConsoleBatchRouter</c> kararıyla düşer, YENİ bir tampon yolu açılmaz. Anlatı boşsa idle "ready" satırı geri gelir;
    /// boş bir proje logu, kart seçimiyle AYNI kuralla (<c>ProjectDocumentLines</c>) o projenin boş-durum metnini gösterir.</para>
    ///
    /// <para><b>Graf:</b> gizliyken statü, faz, seçim itişleri ve filtre yenilemesi yalnız bayrağı kaldırdı
    /// (<see cref="_graphStaleWhileHidden"/>); dönüşte dördü TEK seferde, üretimdeki sırayla uygulanır: koşu fazı,
    /// statüler, seçim, filtre. Topoloji bu yola girmez: gizliyken de kurulmuştu ve dönüşte tekrarlanmaz. Dönüşte oynayan iki
    /// kısa geçiş KABUL edilmiştir: koşu gizliyken başladıysa graf soluklaşma geçişi (<c>GraphView.HoldStatusesUntilDimmed</c>)
    /// ve etkin bir filtre varsa filtre yeniden uygulanırken düğümlerin süzülme geçişi (<c>GraphView.FilterMatches</c>).</para>
    ///
    /// <para><b>Liste:</b> gizliyken topoloji ve görünür-küme değişimleri listeye yazılmadı (<see cref="_listStaleWhileHidden"/>);
    /// dönüşte liste modelden TEK geçişte, <b>reveal'siz</b> kurulur (<c>ApplyProjectGroups(reveal: false)</c>) — gizlilikte olan bir
    /// değişimin kademeli belirişi geriye dönük oynanmaz. İmza <c>ApplyProjectGroups</c>'ta yazıldığı için ardından
    /// <c>RefreshVisibleRows</c> çağırmak boş bir karşılaştırma olurdu. Sticky şerit, Build menüsü, alt çubuğun sayaç chip'leri ve
    /// satırlar burada DEĞİL: kalıtsal sinyalin kendi değişiminde (<c>OnPropertyChanged</c>) her biri kendi bayrağıyla yetişir.</para>
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
        if (IsSurfaceHidden) return; // gerekçe: özetteki "Gizliyken koşmaz" paragrafı
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
                var row = _vm.FindRow(activeProjectId);
                _vm.SeedProjectDocument(activeProjectId,
                    text => Shell.ConsoleViewControl.ReplaceProjectDocument(ProjectDocumentLines(row, text)));
            }
        }
        if (_listStaleWhileHidden)
        {
            // Reveal OYNAMAZ (gizlilikte olan bir değişim geriye dönük oynanmaz). Graf gizliyken zaten yeniden kurulmuştu
            // (RebuildGraph kapısızdır) ve burada tekrarlanmaz — tekrarı dönüşte görünür bir reveal oynatırdı.
            // Diğer blokların aksine bayrak burada elle sıfırlanmaz: onu listeyi yazan ApplyProjectGroups düşürür (BlankPlanSurface de düşürür).
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
