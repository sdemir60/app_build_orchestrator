using System.Windows.Threading;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Core.Diagnostics;

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
/// yetişir) · işlem sonu bellek trimi (<see cref="RequestTrimWhenDue"/>: gizliyken pay beklenmez ve tepside biten koşu gösterge
/// çıkışını bekler; görünürken bitiş finali ve pay beklenir).</para>
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
    ///
    /// <para><b>İşlem sonu bellek trimi</b> iki yönde de yeniden sorulur (<see cref="RequestTrimWhenDue"/>). Gizlenince görünür
    /// yolun payı durur: trim edilmemiş bitmiş bir işlem varsa pay beklenmeden boşta toplanır — gizlide duraklama görünmez.
    /// Görününce tepside biten koşunun kaydı silinir (<see cref="_runEndedHidden"/>): görünür pencerede gösterge yoktur, o
    /// çıkış trimin önünde beklenmez; trim görünür yoldan (final ve pay) yapılır.</para>
    /// </summary>
    internal void SetSurfaceHidden(bool hidden)
    {
        if (IsSurfaceHidden == hidden) return;
        HiddenSurface.SetIsHidden(this, hidden);
        if (!hidden)
        {
            Dispatcher.InvokeAsync(ResyncAfterShow, DispatcherPriority.Loaded);
            _runEndedHidden = 0;
            RequestTrimWhenDue();
            return;
        }
        _choreographer.Cancel(_vm.Projects);
        if (Shell.GraphHost.IsEndFinalePlaying) Shell.GraphHost.CancelEndFinale();
        _trimGrace?.Stop();
        RequestTrimWhenDue();
    }

    // ---------------------------------------------------------------- [idle-memory · dalga 1] işlem sonu bellek trimi

    /// <summary>İşlem sonu bellek triminin saf kararı (<see cref="RequestTrimWhenDue"/>): işlem başına en fazla bir trim ve
    /// hangi koşullarda.</summary>
    private readonly MemoryTrimRule _trimRule = new();

    /// <summary>Göstergesi çıkışını bitirmiş koşunun kimliği — bildirimin taşıdığı değer (çıkış BAŞLADIĞI andaki <c>RunViewModel.RunSerial</c>; koşu sürüyorsa o
    /// koşu, bittiyse biten koşu: kimlik yalnız yeni bir koşu başlarken artar).</summary>
    private int _indicatorExitedRun;

    /// <summary>Yüzey gizliyken (tepsideyken) BİTEN son koşunun kimliği (<c>RunViewModel.EndedRunSerial</c>); 0 = yok. Gösterge
    /// çıkışını YALNIZ böyle bir koşu bekler: gösterge tepside biten koşunundur, görünürde biten koşunun çıkışı hiç gelmez. Kayıt
    /// koşunun bitiş bildiriminde alınır — VM onu işlemin bitişinden ÖNCE bildirir, trim kararı kaydı hazır bulur. Pencere görününce
    /// silinir (<see cref="SetSurfaceHidden"/>).</summary>
    private int _runEndedHidden;

    /// <summary>[test yüzeyi] Görünür pencerede trimin, en son sinyalden (tipik olarak bitiş finalinin sonundan) sonraki payı.
    /// Sayının TEK tanımı <see cref="MemoryTrimRule.GraceAfterFinale"/>'dır. Sıfır ya da negatif ⇒ zamanlayıcı yok: yürütme hemen
    /// boşta kuyruğa düşer (testler <c>DrainToIdle</c> ile boşaltır — boşaltma zamanlayıcı ateşlemez).</summary>
    internal TimeSpan TrimGraceAfterFinale { get; set; } = MemoryTrimRule.GraceAfterFinale;

    /// <summary>Görünür yolun pay zamanlayıcısı — ilk görünür istekte kurulur; her istek baştan başlatır, dolunca yürütmeyi
    /// kuyruklar. Gizlenme ve pencerenin kapanışı durdurur.</summary>
    private DispatcherTimer? _trimGrace;

    /// <summary>Boşta kuyrukta bekleyen yürütme (<see cref="TrimIfStillDue"/>) — kuyrukta aynı anda tek yürütme bulunur.</summary>
    private DispatcherOperation? _queuedTrim;

    /// <summary>[perf Faz C · C4] Toplamanın TEK gerçek çağrısı: <see cref="MemoryTrim.CollectAndRelease"/>'e devreder (ne yaptığı ve
    /// neden bloklayıcı olduğu orada yazılıdır). Ne zaman çağrılacağına <see cref="RequestTrimWhenDue"/> karar verir. Testler sahteyle
    /// değiştirir.</summary>
    internal Action MemoryCollector { get; set; } = CollectMemory;

    private static void CollectMemory() => MemoryTrim.CollectAndRelease();

    /// <summary>
    /// [perf Faz C · C4] Tepsi göstergesi çıkış sırasını tamamladı (<c>TrayBuildIndicatorController.ExitCompleted</c>: gösterge
    /// gizlendi, sonuç balonu gösterildi) — trimin gösterge sinyali: tepside biten koşunun trimi bunu bekler. Çıkış sırasında
    /// pencere geri geldiyse bildirim yine gelir; o zaman trim görünür yoldan (final ve pay) yapılır. <c>internal</c>: test yüzeyi —
    /// headless'ta gösterge hiç kurulmaz (<c>OnSourceInitialized</c> koşmaz), testler bildirimi doğrudan verir.
    /// </summary>
    /// <param name="run">Göstergenin çıkışı BAŞLADIĞI andaki koşu kimliği (<c>TrayBuildIndicatorController</c> taşır). Burada
    /// <c>RunSerial</c> okunmaz: bildirim nefesten sonra gelir ve o ana kadar yeni bir koşu başlamış olabilir.</param>
    internal void OnTrayIndicatorExitFinished(int run)
    {
        _indicatorExitedRun = run;
        RequestTrimWhenDue();
    }

    /// <summary>
    /// [idle-memory · dalga 1] <b>İşlem sonu bellek trimi.</b> Bir işlem (koşu, Sync, Clean, Optimize) bitince, uygulama sakinken
    /// BİR kez toplanır (<see cref="MemoryCollector"/>): işlemin atıkları (bırakılmış canlı satır tamponu, koşu boyunca biriken
    /// nesneler) ölü yük olarak kalmaz. Kararı saf kural verir (<see cref="MemoryTrimRule"/>); burası sinyalleri toplar, zamanlar
    /// ve yürütür.
    ///
    /// <para><b>Altı sinyal buraya gelir; hangisi en son gelirse trimi o ister:</b> işlem bitti
    /// (<c>RunViewModel.EndedOperationSerial</c>), grafın bitiş finali bitti (<c>GraphView.EndFinaleFinished</c>), tepsi göstergesi
    /// çıkışını bitirdi (<see cref="OnTrayIndicatorExitFinished"/>), yüzey gizlendi ya da göründü (<see cref="SetSurfaceHidden"/>),
    /// proje log yüklemesinin cevabı artık beklenmiyor — geldi ya da gelmeyecek (<c>RunViewModel.HasPendingLogLoad</c>) — ve
    /// uçuştaki iş değişti (<c>RunViewModel.IsOperationInFlight</c>: işlem sayılmayan bir iş de trimi bekletir ve bitince ister).</para>
    ///
    /// <para><b>Zamanlama:</b> gizliyken pay beklenmez (duraklama görünmez): yürütme boşta kuyruğa düşer
    /// (<see cref="DispatcherPriority.ApplicationIdle"/>) — reduced-motion'da sinyaller koşu bitişinin içinde eşzamanlı gelebilir ve
    /// bloklayan toplama bitişin kalan işini (yüzey yenilemesi, bekleyen Sync) bekletmemelidir. Görünürken pay
    /// (<see cref="TrimGraceAfterFinale"/>) beklenir ve her yeni istek payı baştan başlatır; pay dolunca yürütme yine boşta kuyruğa
    /// düşer. Kuyrukta aynı anda tek yürütme bulunur.</para>
    ///
    /// <para><b>Yürütme koşulları yeniden sorar</b> (<see cref="ShouldTrimNow"/>, istekteki soru) ve işlemi ancak toplama GERÇEKTEN
    /// yürütülünce tüketir (<see cref="MemoryTrimRule.MarkTrimmed"/>): arada yeni bir işlem başladıysa, final oynuyorsa ya da bir log
    /// yüklemesi bekliyorsa vazgeçer ve işlem sıradaki sinyale kadar uygun kalır. Bu, koşunun bitiş sırasını da güvenli kılar: bitiş
    /// sinyali finalden ÖNCE gelir (<c>MarkRunEnded</c>, ardından <c>Phase = Done</c> finali başlatır), ilk pay finalin ortasında
    /// dolabilir — yürütme finali oynarken bulup vazgeçer, trimi finalin bitişi yeniden ister.</para>
    /// </summary>
    internal void RequestTrimWhenDue()
    {
        if (!ShouldTrimNow()) return;
        if (IsSurfaceHidden || TrimGraceAfterFinale <= TimeSpan.Zero)
        {
            _trimGrace?.Stop();
            QueueTrim();
            return;
        }
        // Görünür yol: pay en son sinyalden sayılır. Kuyruktaki bir yürütme (gizliyken ya da dolmuş bir payla kuyruklanmış) payı
        // atlamasın diye geri alınır; yürütmeyi payın sonu yeniden kuyruklar.
        _queuedTrim?.Abort();
        _queuedTrim = null;
        _trimGrace ??= NewTrimGraceTimer();
        _trimGrace.Stop();
        _trimGrace.Interval = TrimGraceAfterFinale;
        _trimGrace.Start();
    }

    private DispatcherTimer NewTrimGraceTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            QueueTrim();
        };
        return timer;
    }

    /// <summary>Yürütmeyi boşta kuyruğa alır — kuyrukta bekleyen varsa ikincisi alınmaz.</summary>
    private void QueueTrim()
    {
        if (_queuedTrim is { Status: DispatcherOperationStatus.Pending }) return;
        _queuedTrim = Dispatcher.InvokeAsync(TrimIfStillDue, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Boşta ertelenen yürütme: koşullar yeniden sorulur; tutuyorsa işlem trim edilmiş sayılır ve toplanır.</summary>
    private void TrimIfStillDue()
    {
        _queuedTrim = null;
        if (!ShouldTrimNow()) return;
        _trimRule.MarkTrimmed(_vm.EndedOperationSerial);
        MemoryCollector();
    }

    /// <summary>İstek ve yürütme AYNI soruyu sorar. Gösterge çıkışı yalnız tepside biten son koşu için ve onun çıkışı henüz
    /// gelmediyse beklenir (<see cref="_runEndedHidden"/>); Sync/Clean/Optimize bitişi de son koşu tepside bitip çıkışı hâlâ
    /// beklenirken bekler — çıkıştan sonraki tek trim ikisini de kapsar.</summary>
    private bool ShouldTrimNow() => _trimRule.ShouldTrim(
        endedOperation: _vm.EndedOperationSerial,
        operationInFlight: _vm.IsOperationInFlight,
        pendingLogLoad: _vm.HasPendingLogLoad,
        surfaceHidden: IsSurfaceHidden,
        finalePlaying: Shell.GraphHost.IsEndFinalePlaying,
        awaitingTrayIndicator: _runEndedHidden != 0 && _runEndedHidden == _vm.EndedRunSerial
                               && _indicatorExitedRun != _runEndedHidden);

    /// <summary>VM'in trim sinyalleri (ctor aboneliği): koşu bittiyse yüzey o an gizli miydi kaydedilir; işlemin bitişi, log
    /// yüklemesinin değişimi ve uçuştaki işin değişimi trimi yeniden sorar. Sonuncusu işlem SAYILMAYAN işler içindir (reddedilen
    /// pull/checkout, reddedilen başlatma): onların yüzünden atlanan trim, bitişleri <c>EndedOperationSerial</c>'ı değiştirmese de
    /// onların sonunda istenir. İş başlarken gelen bildirim zararsızdır — istek uçuştaki işi görüp hiçbir şey zamanlamaz.</summary>
    private void OnVmPropertyChangedForTrim(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(RunViewModel.EndedRunSerial):
                if (IsSurfaceHidden) _runEndedHidden = _vm.EndedRunSerial;
                break;
            case nameof(RunViewModel.EndedOperationSerial):
            case nameof(RunViewModel.HasPendingLogLoad):
            case nameof(RunViewModel.IsOperationInFlight):
                RequestTrimWhenDue();
                break;
        }
    }

    /// <summary>Pencere kapandı: pay zamanlayıcısı durur, kuyruktaki yürütme geri alınır.</summary>
    private void StopMemoryTrim()
    {
        _trimGrace?.Stop();
        _queuedTrim?.Abort();
        _queuedTrim = null;
    }

    /// <summary>Gizliyken konsol belgesine yazılmayan bir batch, temizlik ya da mod geçişi oldu (seçim yolu:
    /// <c>ShowRunConsole</c>, <c>OnSelectedProjectChangedAsync</c>): ekrandaki belge modelin (<c>RunViewModel</c>'in anlatısı
    /// ya da proje tamponu) gerisinde. Başlık ve VM tarafı yine güncellenir; belgeyi <see cref="ResyncAfterShow"/> kurar ve
    /// bayrağı sıfırlar.
    ///
    /// <para>Bayrak kalkık kaldıkça batch'ler belgeye YAZILMAZ: gösterim ile Loaded-öncelikli dönüş kurulumu arasındaki aralık
    /// dahil (<c>AppendConsoleBatch</c> kapısı: gizli YA DA bayrak kalkık) — bayat belgeye basılan bir batch, dönüş kurulumu
    /// belgeyi modelden yeniden kurunca boşa giderdi. Belgeyi modelden kuran görünür yollar, mod geçişi
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
    /// yine güncellendi. Belge modelden bir kez, <b>tilt'siz</b> kurulur — hangi belgenin kurulacağını o anki
    /// <c>ActiveProjectId</c> söyler: anlatı için <c>RunViewModel.SeedRunDocument</c> (store'un snapshot'ı: son dilim belgeye,
    /// öncesi store'da kalır), proje logu açıksa <c>RunViewModel.SeedProjectDocument</c> (dikilmiş tam log). İkisi de
    /// reseed-drop sentinel'ini yazar: uçuştaki bayat batch'ler
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
                _vm.SeedRunDocument(snapshot => Shell.ConsoleViewControl.ReplaceRunDocument(snapshot));
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
