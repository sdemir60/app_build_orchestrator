using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [idle-memory · dalga 3 · spec §6.4] <see cref="RunViewModel"/>'in <b>motor yaşam döngüsü</b> yüzeyi: VM'in motor durumu
/// (<see cref="EngineState"/>), boşta kuralının uykusu (<see cref="EvaluateEngineIdle"/>), motor kapısı ve uyanma
/// (<see cref="EnsureEngineAwakeAsync"/>), TEK hazır geçişi (<see cref="EnterEngineReady"/>) ve motor kaybının bildirimi
/// (<see cref="ReportEngineDead"/>). Motor olaylarının girişleri (<see cref="OnEngineReady"/>, <see cref="OnEngineExited"/>,
/// <see cref="OnEngineUnavailable"/>) ve kayıp sonrası bırakma (<see cref="ReleaseAfterEngineLoss"/>) ana dosyadadır.
/// </summary>
public sealed partial class RunViewModel
{
    // ---------------------------------------------------------------- motor uykusu (boşta kuralı)

    /// <summary>
    /// [idle-memory · dalga 3 · spec §6.4 · karar 13-14 · 32] VM'in motor durumu — TEK alan: boşta kuralının motor girdisi ve motor kapısının
    /// (<see cref="EnsureEngineAwakeAsync"/>) okuduğu tek şey. Yalnız UI thread'inde ve durumu değiştiren olayla SENKRON yazılır:
    /// <list type="bullet">
    /// <item><see cref="EngineState.Starting"/>: VM bu durumda doğar (kabuk motoru <c>Loaded</c>'da başlatır) ve "Restart engine" başlarken
    /// yeniden girer (<see cref="RestartEngineAsync"/>);</item>
    /// <item><see cref="EngineState.Ready"/>: hazır yolu (<see cref="OnEngineReady"/>) ya da uyanma (<see cref="OnEngineResumed"/>) — ikisi de
    /// <see cref="EnterEngineReady"/>'den geçer;</item>
    /// <item><see cref="EngineState.Sleeping"/> → <see cref="EngineState.Asleep"/>: boşta kuralının uykusu (<see cref="StartSleep"/>);</item>
    /// <item><see cref="EngineState.Waking"/>: motor kapısının tek uçuşlu uyanması (<see cref="StartWake"/>);</item>
    /// <item><see cref="EngineState.Dead"/>: motor kaybı — kendiliğinden ölüm (<see cref="OnEngineExited"/>), düşen uyku ya da ölüm sinyali hiç
    /// gelmeyen düşmüş uyanma; hepsi <see cref="ReportEngineDead"/>'den geçer;</item>
    /// <item><see cref="EngineState.Unavailable"/>: motor hiç doğamadı (<see cref="OnEngineUnavailable"/> — açılışta, yeniden başlatmada ya da
    /// uyanmada).</item>
    /// </list>
    /// Uyku, uyuma ve uyanma birlikte <b>uyku döngüsüdür</b> (<see cref="InSleepCycle"/>): motor uyanık değildir ve ona hiçbir şey gitmez — gönderim
    /// kuralının tek ifadesi kapının özetindedir. Şeridin bayrakları (<see cref="EngineDiedMessage"/>, <see cref="EngineRestartable"/>,
    /// <see cref="IsEngineUnavailable"/>) UI'ın bayrakları olarak kalır ve AYNI yerlerde yazılır: Dead bir ölüm metniyle (yeniden başlatılabilir),
    /// Unavailable kalıcı bir eksik metniyle gelir; yeniden başlatmanın Ready'si metnin temizliğinden sonradır. Bilinen iki ayrışma, ikisi de
    /// güvenli yönde: (1) <see cref="OnRunStarted"/> ölüm metnini temizler ama durumu Ready'ye çekmez (Ready yalnız hazır yolundan gelir) — durum
    /// en kötü "ölü" kalır ve kural uyutmaz; (2) [T20 incelemesi M3] ölüm sinyali gelmeden düşen bir yeniden başlatma (çıkışta dispose öne geçti ya
    /// da eski motorun öldürülmesi fırlattı) durumu <see cref="EngineState.Starting"/>'te bırakır — kural uyutmaz, kapı Starting'i bugünkü gibi
    /// geçirir (gerekçe <see cref="RestartEngineAsync"/>'te).
    /// </summary>
    internal EngineState EngineState { get; private set; } = EngineState.Starting;

    /// <summary>[Fix round 1 · R2 · Mi2] Uyku döngüsü: uyku sürüyor, uyuyor ya da uyanıyor — motor uyanık değil ve ona hiçbir şey gönderilmez.
    /// Motor kapısının işi yalnız bu durumlardır (<see cref="EnsureEngineAwakeAsync"/>); bu durumlarda kaybedilen motor hiçbir pencereyi bırakmaz
    /// (<see cref="ReportEngineDead"/>). Tanımın TEK yeri.</summary>
    private static bool InSleepCycle(EngineState state) =>
        state is EngineState.Sleeping or EngineState.Asleep or EngineState.Waking;

    /// <summary>[test dikişi] Motoru uyutan çağrı — üretimde konağın uykusu (<see cref="EngineHost.SleepAsync"/>, ctor'da bağlanır). Testler
    /// sahtesini verir: sayan, bekletilen (TCS: uyku sürüyor) ya da vazgeçen bir görev. Karar ve VM'in durumu dikişin önündedir.</summary>
    internal Func<Task> SleepEngine { get; set; }

    /// <summary>[test dikişi · Task 21] Uyuyan motoru uyandıran çağrı — üretimde konağın uyanması (<see cref="EngineHost.WakeAsync"/>, ctor'da
    /// bağlanır). Testler sahtesini verir: sayan, bekletilen (TCS: uyanma sürüyor) ya da düşen bir görev. Karar ve VM'in durumu dikişin
    /// önündedir.</summary>
    internal Func<Task<EngineReadyEvent>> WakeEngine { get; set; }

    /// <summary>[spec §6.4 · karar 15] UI thread'inde, <see cref="TickElapsed"/> ile 200 ms'de bir — pencere gizliyken de (sessizlik
    /// bekçisinin yanında). Karar saf kuraldadır (<see cref="EngineIdleRule.ShouldSleep"/>); girdileri VM'in tek tanımlarıdır: ayar
    /// (<see cref="StopEngineWhenIdle"/>), <see cref="IsOperationInFlight"/>, <see cref="HasPendingLogLoad"/>, <see cref="EngineState"/> ve son
    /// motor etkinliği (<see cref="_lastEngineSignalMs"/>). Doğruysa motor uyutulur (<see cref="StartSleep"/>).</summary>
    private void EvaluateEngineIdle()
    {
        if (EngineIdleRule.ShouldSleep(nowMs: _nowMs(), lastActivityMs: Volatile.Read(ref _lastEngineSignalMs),
                settingOn: StopEngineWhenIdle, operationInFlight: IsOperationInFlight, pendingLogLoad: HasPendingLogLoad,
                engine: EngineState))
            StartSleep();
    }

    /// <summary>
    /// Boştaki motoru uyutur — sırası bilerek: önce uykunun tutamağı (<see cref="_sleepInFlight"/>), sonra durum <see cref="EngineState.Sleeping"/>,
    /// en son iş (<see cref="PutEngineToSleepAsync"/>). Durum ÖNCE ve senkron yazılır: konağın uykusu sürerken gelen tik bir geçiş görür ve ikinci
    /// uykuyu başlatmaz (tek uçuş — C3). [Fix round 1 · Mi5] Tutamak durumdan ÖNCE yazılır: durumu gören (yarın gözlemlenebilir olursa senkron
    /// bir tepkiyle kapıya giren) her yol bu uykunun tutamağını bekler — bitmiş, eski bir tutamağı değil; kapının döngüsü böylece hiç boşa dönmez.
    /// </summary>
    private void StartSleep()
    {
        var settled = new TaskCompletionSource();
        _sleepInFlight = settled.Task;
        EngineState = EngineState.Sleeping;
        _ = PutEngineToSleepAsync(settled);
    }

    /// <summary>
    /// Uykunun işi (<see cref="StartSleep"/> başlatır). Uyku sürerken başka bir sinyal durumu değiştirdiyse — motor kendiliğinden öldü: konakta ölüm
    /// hakkını bekçi aldı, uyku hiçbir şey yapmadan döner — o sinyal kazanır ve bitişte dokunulmaz. Değiştirmediyse bitiş üç yoldan biridir:
    /// <list type="bullet">
    /// <item><b>Uyku bitti</b> → <see cref="EngineState.Asleep"/>. Uyuyan motorun PID'i tutulmaz (<see cref="EnginePid"/> null: ölü bir süreç
    /// canlıymış gibi görünmez); sürüm kalır (uyanan motor aynı ikilidir).</item>
    /// <item><b>Çıkışta vazgeçildi</b> — dispose uçuştaki uykunun önüne geçti (<see cref="OperationCanceledException"/>) → yine
    /// <see cref="EngineState.Asleep"/>: motorun nesli emekliye ayrıldı ve süreci çıkışın kapanışı alıyor — "hazır" demek ölü bir süreci canlı
    /// saymak, uykuyu her tikte yeniden denemek olurdu. Konsola satır YAZILMAZ, ölüm de bildirilmez: ölen yok, uygulama kapanıyor.
    /// <see cref="ObjectDisposedException"/> aynı dalda yalnız SAVUNMA için yakalanır: konak dispose'tan sonra gelen bir uykuyu fırlatmadan
    /// no-op yapar (<see cref="EngineHost.SleepAsync"/>) — o çağrı normal biter ve "uyku bitti" dalına düşer, sonuç aynıdır.</item>
    /// <item><b>Uyku başka bir nedenle düştü</b> (ör. öldürme fırlattı) → motor KAYBEDİLDİ: konak nesli emekliye ayırdıktan sonra düştü, ölüm
    /// sinyali hiç gelmeyecek ve konak ne uyuyor ne canlı (uyandırma ve gönderim "Engine is not running." ile düşer). Bugünkü ölüm yolu
    /// bildirir (<see cref="ReportEngineDead"/>: kırmızı ölüm satırı + "Restart engine", durum <see cref="EngineState.Dead"/>) — ikinci bir
    /// kopyası değil; o yol boştayken konsola satır yazmaz.</item>
    /// </list>
    /// Tutamak bitiş durumu yazıldıktan SONRA tamamlanır: uykuyu bekleyen yol uyandığında durumu oturmuş bulur.
    /// </summary>
    private async Task PutEngineToSleepAsync(TaskCompletionSource settled)
    {
        bool lost = false;
        try
        {
            await SleepEngine();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Çıkış: motoru dispose'un kendi kapanışı alıyor — sessiz; ObjectDisposedException savunmadır (gerekçe özette).
        }
        catch (Exception)
        {
            lost = true;
        }
        try
        {
            // [T20 incelemesi O2] Durumun TEK kontrolü: uyku sürerken başka bir sinyal (kendiliğinden ölüm) durumu değiştirdiyse o kazanır.
            if (EngineState != EngineState.Sleeping) return;
            if (lost)
            {
                ReportEngineDead(IdleShutdownFailedCause);
                return;
            }
            EngineState = EngineState.Asleep;
            EnginePid = null;
        }
        finally
        {
            settled.SetResult();
        }
    }

    /// <summary>Uykusu düşen motorun ölüm satırındaki nedeni (<see cref="ReportEngineDead"/>): "Engine stopped unexpectedly (idle shutdown
    /// failed)" — kendiliğinden ölümün çıkış kodu ya da protokol hatası yerine.</summary>
    private const string IdleShutdownFailedCause = "idle shutdown failed";

    /// <summary>[Fix round 1 · Mi1] Ölüm sinyali hiç gelmeyen düşmüş uyanmanın ölüm satırındaki nedeni: "Engine stopped unexpectedly (wake failed)"
    /// (<see cref="EnsureEngineAwakeAsync"/>). Geç gelen bir ölüm sinyali metni kendi nedeniyle yeniler.</summary>
    private const string WakeFailedCause = "wake failed";

    /// <summary>Motor hazır — TEK Ready geçişi: motorun kimliği (sürüm, PID — About bunları okur), durum <see cref="EngineState.Ready"/> ve son
    /// motor etkinliği ŞİMDİ. İki çağıran: hazır yolu (<see cref="OnEngineReady"/> — açılış ve yeniden başlatma) ve uyanma
    /// (<see cref="OnEngineResumed"/>). Hazır bildirimi motorun konuştuğu andır; damga hazır olayının olay pompasından geçişini beklemez — kabuğun
    /// hazır yolu ile pompanın sırası garanti değildir ve tik ikisinin arasına düşerse boşta kuralı hiç yazılmamış (0) ya da açılıştan beri bayat
    /// damgayı okur, motoru hazır olur olmaz uyuturdu (C1). Uyanmada aynı damga sessizlik bekçisinin penceresini de uyanmanın sonundan başlatır:
    /// uyanmanın süresi sessizlik sayılmaz. Aynı tek damga (<see cref="_lastEngineSignalMs"/>) — ikinci bir etkinlik damgası yoktur.</summary>
    private void EnterEngineReady(string engineVersion, int pid)
    {
        EngineVersion = engineVersion;
        EnginePid = pid;
        StampEngineActivity();
        EngineState = EngineState.Ready;
    }

    // ---------------------------------------------------------------- motor kapısı (uyandırma)

    /// <summary>[Task 21 · karar 32] Uçuştaki (ya da son) uyku — motor kapısının bekleme tutamağı; <see cref="StartSleep"/> durumdan ÖNCE yazar.
    /// Durumun tek kaynağı yine <see cref="EngineState"/>'tir: kapı bu göreve yalnız durum <see cref="EngineState.Sleeping"/> iken bakar. Görev
    /// düşmez ve bitişinde durum her yolda Sleeping'den çıkmıştır.</summary>
    private Task _sleepInFlight = Task.CompletedTask;

    /// <summary>[Task 21 · karar 32] Uçuştaki (ya da son) uyanma — TEK uçuş: durum <see cref="EngineState.Waking"/> iken kapıya gelen her yol bu
    /// göreve katılır; <see cref="StartWake"/> durumdan ÖNCE yazar. Sonucu uyanan motorun hazır olup olmadığıdır.</summary>
    private Task<bool> _wakeInFlight = Task.FromResult(false);

    /// <summary>
    /// [idle-memory · dalga 3 · spec §6.4 · Task 21 · karar 17/18/19/32/33/34 · R2 · R3] <b>Motor kapısı — gönderim kuralının TEK ifadesi.</b> Motora
    /// giden her istek gönderilmeden önce buradan geçer: gönderimin varsayılan yolu (<see cref="TrySendAsync"/>), Sync (<see cref="SyncCoreAsync"/> —
    /// Sync ve ardındaki branch envanteri için TEK soru), proje log isteği (<see cref="LoadProjectLogAsync"/>) ve komutunu kurmadan önce
    /// bekleyen-koşu penceresinde soran koşu (<see cref="BeginRunAsync"/>). Yalnız VM'in motor durumunu okur (<see cref="EngineState"/> — ölüm metnini
    /// ya da konağın uykusunu değil) ve "bu komut uyandırır mı" sorusunu TEK tabloya sorar (<see cref="EngineWakeRule"/>).
    /// <list type="number">
    /// <item><b>Girişte durum karar verir.</b> Hazır, başlıyor, ölü ya da erişilemez motorla gelen istek kapının işi DEĞİLDİR: her komut türü
    /// bugünkü gibi gönderilir — ölü ya da erişilemez motorda gönderim düşer ve bugünkü satırını yazar ("[error] failed to send …", "[error] could
    /// not load project log: …"). Başlayan motorun borusu komutu tutar. Kapının işi yalnız uyku döngüsüdür (<see cref="InSleepCycle"/>).</item>
    /// <item><b>Uyku döngüsünde uyandırmayan komut</b> (Stop, perf chip) gönderilmez (karar 33). <b>Uyandıran komut</b> bekler: uyku sürüyorsa
    /// bitmesini (ölmekte olan sürece yazılmaz), motor uyuyorsa TEK uyanmayı başlatır (<see cref="StartWake"/>), uyanma sürüyorsa ona katılır.
    /// Bekleyiş bitince durum yeniden okunur: hazır ya da başlıyorsa gönder.</item>
    /// <item><b>Bekleyen reddedilir, satırsız:</b> beklediği geçiş motoru kaybettiyse (düşen uyku, uyanamayan motor, ölüm ya da erişilemezlik) istek
    /// gitmez — o geçişin TEK sinyali kendi yolundadır (karar 19, 34: <see cref="OnEngineUnavailable"/> ya da <see cref="ReportEngineDead"/>). Kapıda
    /// bekleyen yeniden denemez (C23).</item>
    /// <item><b>Çıkış ve geri alınan istek (R3, Mi4):</b> her bekleyişin ardından ve bir uyanmayı başlatmadan önce sorulur — çıkış istendiyse ya da
    /// çağıranın isteği artık istenmiyorsa (<paramref name="stillWanted"/>: bekleyen-koşu penceresi geri alındı, proje logu yüklemesinin yerini
    /// yenisi aldı) istek gitmez ve yeni uyanma başlamaz. Bayrak "çıkış istendi"dir, "çıkış bekliyor" değil: yalnız proje logu bekleyen bir
    /// kapıda çıkış hiç beklemez, o istek de gitmemelidir.</item>
    /// <item><b>Askıda uyanma (Mi1):</b> uyanma düşmüş ve ölüm sinyali hiç gelmemişse (konakta öldürmenin kendisi fırladı) kapıya YENİ giren ilk yol —
    /// uçuştaki bir uyanmaya katılan değil — motoru kayıp bildirir (<see cref="ReportEngineDead"/>, neden <see cref="WakeFailedCause"/>: şerit +
    /// "Restart engine"); o yolun isteği gitmez. Geç gelen bir ölüm sinyali yalnız şeridin metnini yeniler. Çıkmaz sokak yoktur.</item>
    /// </list>
    /// <c>false</c> "gönderme" demektir ve kapı konsola satır YAZMAZ; her çağıran bugünkü "gönderim düştü" geri alımını çalıştırır. Bütün
    /// çağıranlar UI thread'indedir ve durum senkron yazılır — alanlar kilitsizdir. Kapının işi olmayan girişte kapı askıya girmez: gönderim
    /// bugünkü gibi çağıranın ilk await'inden önce, senkron gider. Uyku ve uyanmanın tutamağı durumdan ÖNCE yazıldığı için (Mi5) kapının
    /// döngüsü her turda yeni bir geçişi bekler — hiç boşa dönmez.
    /// </summary>
    /// <param name="command">Gönderilecek komutun türü — tablonun sorusu.</param>
    /// <param name="stillWanted">İsteğin hâlâ istenip istenmediği (verilmezse: istenir). Yalnız bekleyişten sonra ve uyanma başlamadan önce sorulur.</param>
    /// <returns>Gönder (<c>true</c>) ya da gönderme (<c>false</c>).</returns>
    private async Task<bool> EnsureEngineAwakeAsync(Type command, Func<bool>? stillWanted = null)
    {
        var state = EngineState;
        if (!InSleepCycle(state)) return true;
        if (state == EngineState.Waking && _wakeInFlight is { IsCompletedSuccessfully: true, Result: false })
        {
            ReportEngineDead(WakeFailedCause);
            return false;
        }
        if (!EngineWakeRule.Wakes(command)) return false;
        while (true)
        {
            if (state == EngineState.Sleeping)
            {
                await _sleepInFlight;
            }
            else
            {
                if (state == EngineState.Asleep)
                {
                    if (!StillWanted(stillWanted)) return false;
                    StartWake();
                }
                if (!await _wakeInFlight) return false;
            }
            if (!StillWanted(stillWanted)) return false;
            state = EngineState;
            if (state is EngineState.Ready or EngineState.Starting) return true;
            if (!InSleepCycle(state)) return false;
        }
    }

    /// <summary>Kapıda bekleyen istek hâlâ isteniyor mu: çıkış istenmedi ve çağıranın kendi koşulu (varsa) doğru (<see cref="EnsureEngineAwakeAsync"/>,
    /// madde 4).</summary>
    private bool StillWanted(Func<bool>? stillWanted) => !_exitRequested && (stillWanted?.Invoke() ?? true);

    /// <summary>
    /// [Task 21 · karar 19/20/32 · C5] Uyuyan motoru uyandırır — TEK uçuş, sırası bilerek: önce tutamak (<see cref="_wakeInFlight"/>), sonra durum
    /// <see cref="EngineState.Waking"/>, en son iş (<see cref="WakeEngineAsync"/>) — durumu gören her yol BU uyanmaya katılır (Mi5;
    /// <see cref="StartSleep"/> ile aynı gerekçe). Konakta da kapı tektir: iki eşzamanlı uyanma tek süreç doğurur.
    /// </summary>
    private void StartWake()
    {
        var woke = new TaskCompletionSource<bool>();
        _wakeInFlight = woke.Task;
        EngineState = EngineState.Waking;
        _ = WakeEngineAsync(woke);
    }

    /// <summary>
    /// Uyanmanın işi (<see cref="StartWake"/> başlatır); tutamağın sonucu uyanan motorun hazır olup olmadığıdır ve bitiş durumu yazıldıktan SONRA
    /// verilir. Uyanma sürerken başka bir sinyal durumu değiştirdiyse (uyanan motor öldü) o kazanır: bitişte dokunulmaz, "hazır değil". Değiştirmediyse
    /// bitiş dört yoldan biridir:
    /// <list type="bullet">
    /// <item><b>Uyandı</b> → <see cref="OnEngineResumed"/>: kimlik ve hazır durumu; konsol satırı ve açılış Sync'i YOK.</item>
    /// <item><b>Motor bulunamadı / başlatılamadı</b> (<see cref="EngineUnavailableException"/>) → bugünkü erişilemezlik yolu
    /// (<see cref="OnEngineUnavailable"/>: TEK konsol satırı, kalıcı şerit, komutlar kapalı).</item>
    /// <item><b>Çıkışta vazgeçildi</b> — dispose uyanmanın önüne geçti (<see cref="OperationCanceledException"/>) ya da konak çoktan dispose
    /// edilmişti (<see cref="ObjectDisposedException"/>) → <see cref="EngineState.Asleep"/>: süreci çıkışın kapanışı alıyor; satır yok, ölüm yok.</item>
    /// <item><b>Doğan motor hazır olamadı</b> (zaman aşımı, erken ölüm, çerçeve hatası) ya da konakta uyandırılacak motor yoktu (ölümü
    /// bildirilmişti) → satır YOK ve durum burada yazılmaz: konak o motoru öldürür ve ölümünü <see cref="EngineHost.EngineExited"/> TEK sinyal olarak
    /// bildirir (<see cref="OnEngineExited"/> → <see cref="EngineState.Dead"/>, şerit ve "Restart engine"). O sinyal gelene dek durum
    /// <see cref="EngineState.Waking"/> kalır ve uyanmaya katılan yollar hemen "gönderme" alır; sinyal hiç gelmezse (konakta öldürmenin kendisi
    /// fırladı) kapıya yeni giren ilk yol motoru kayıp bildirir (<see cref="EnsureEngineAwakeAsync"/>, madde 5).</item>
    /// </list>
    /// </summary>
    private async Task WakeEngineAsync(TaskCompletionSource<bool> woke)
    {
        bool ready = false;
        try
        {
            var readyEvent = await WakeEngine();
            if (EngineState == EngineState.Waking)
            {
                OnEngineResumed(readyEvent.EngineVersion, readyEvent.Pid);
                ready = true;
            }
        }
        catch (Services.EngineUnavailableException ex)
        {
            if (EngineState == EngineState.Waking) OnEngineUnavailable(ex.ExePath, ex.Reason);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            if (EngineState == EngineState.Waking) EngineState = EngineState.Asleep;
        }
        catch (Exception)
        {
            // Satır yok, durum yazılmaz: ölüm sinyali gelir; gelmezse kapıya yeni giren ilk yol bildirir (gerekçe özette).
        }
        finally
        {
            woke.SetResult(ready);
        }
    }

    /// <summary>[Task 21 · karar 20 · Q8] Uyuyan motor uyandı: kimlik alanları ve hazır durumu — hazır yolunun ÇEKİRDEĞİ
    /// (<see cref="EnterEngineReady"/>), açılışa özgü işleri OLMADAN. Konsola "Engine ready" satırı yazılmaz, eski havuz ipucu yazılmaz, açılış
    /// Sync'i tekrarlanmaz: uyanan motor komutlar arası bir durum kaybetmedi (her komut kökünü ve ayarını taşır) ve Sync ekranın ihtiyacıdır,
    /// motorun değil — ekran uyku boyunca değişmedi. Tek istisna motorun kendi bilgisidir (C21): uyanan motor açılışta yarım kalmış bir koşu
    /// bulduysa hazır olayı <c>InterruptedProjects</c> taşır ve satırı olay akışından bugünkü gibi düşer (<see cref="OnEngineRecovered"/>).</summary>
    private void OnEngineResumed(string engineVersion, int pid) => EnterEngineReady(engineVersion, pid);

    /// <summary>[idle-memory · dalga 3] Motor öldü — kullanıcıya bildirmenin TEK yolu: kendiliğinden ölüm (<see cref="OnEngineExited"/>), uykusu
    /// düşüp kaybedilen motor (<see cref="PutEngineToSleepAsync"/>) ve ölüm sinyali hiç gelmeyen düşmüş uyanma (<see cref="EnsureEngineAwakeAsync"/>,
    /// madde 5). Durum <see cref="EngineState.Dead"/>, şeritte kırmızı ölüm satırı ve "Restart engine"; uçuştaki pencereler bırakılır
    /// (<see cref="ReleaseAfterEngineLoss"/>). Konsola satır yazmaz (boşta bir motor kaybı hiçbir işlemi bitirmez).
    /// <para>[Task 21 · karar 34 · Fix round 1 · Mi2] <b>Uyku döngüsündeki kayıp hiçbir pencereyi bırakmaz.</b> Motor uyku sürerken, uyurken ya da
    /// uyanırken kaybedildiyse (düşen uyku; uyutulan motorun geç gelen ölüm sinyali; uyanmak için doğan motorun ölümü) ona son hazır olduğu andan
    /// beri hiçbir şey GİTMEDİ: uyku yalnız uçuşta işlem yokken başlar ve uyku döngüsünde gönderim yoktur (<see cref="EnsureEngineAwakeAsync"/>).
    /// O an açık görünen her istek penceresi kapıda bekleyen — ya da açılış koreografisinde kapıya yürüyen — bir isteğindir; motor kaybı onu
    /// bırakmaz (hiç başlamamış bir işlem bitmiş sayılmaz: ne Sync'in <c>EndSync</c>/<c>MarkOperationEnded</c>'ı, ne koşunun faz/<see cref="IsStarting"/>
    /// sıfırlaması). İsteğin kendi "gönderim düştü" geri alımı bırakır — geri alım TEK yerde ve tek sırayla olur.</para></summary>
    /// <param name="cause">Ölüm satırının parantezindeki neden (İngilizce): <c>exit N</c>, <c>protocol error</c>, uykunun ya da uyanmanın düşüşü.</param>
    private void ReportEngineDead(string cause)
    {
        bool lostInSleepCycle = InSleepCycle(EngineState);
        EngineState = EngineState.Dead;
        // [E2/T37 · İngilizce sweep] Şerit kalıcı-hata modu bu metni GÖSTERİR → İngilizce (tüm UI/konsol metni).
        // [D1] Doğmuş bir motorun ölümü YENİDEN BAŞLATILABİLİR — şerit "Restart engine" aksiyonunu gösterir
        // (EngineDiedMessage yazan her yol bu bayrağı da yazar; bkz. EngineRestartable).
        EngineRestartable = true;
        EngineDiedMessage = $"Engine stopped unexpectedly ({cause})";
        if (!lostInSleepCycle) ReleaseAfterEngineLoss();
    }
}
