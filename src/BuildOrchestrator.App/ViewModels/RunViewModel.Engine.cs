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
    /// <item><see cref="EngineState.Sleeping"/> → <see cref="EngineState.Asleep"/>: boşta kuralının uykusu (<see cref="PutEngineToSleepAsync"/>);</item>
    /// <item><see cref="EngineState.Waking"/>: motor kapısının tek uçuşlu uyanması (<see cref="WakeEngineAsync"/>);</item>
    /// <item><see cref="EngineState.Dead"/>: motor kaybı — kendiliğinden ölüm (<see cref="OnEngineExited"/>) ya da düşen uyku; ikisi de
    /// <see cref="ReportEngineDead"/>'den geçer;</item>
    /// <item><see cref="EngineState.Unavailable"/>: motor hiç doğamadı (<see cref="OnEngineUnavailable"/> — açılışta, yeniden başlatmada ya da
    /// uyanmada).</item>
    /// </list>
    /// Şeridin bayrakları (<see cref="EngineDiedMessage"/>, <see cref="EngineRestartable"/>, <see cref="IsEngineUnavailable"/>) UI'ın bayrakları
    /// olarak kalır ve AYNI yerlerde yazılır: Dead bir ölüm metniyle (yeniden başlatılabilir), Unavailable kalıcı bir eksik metniyle gelir;
    /// yeniden başlatmanın Ready'si metnin temizliğinden sonradır. Bilinen iki ayrışma, ikisi de güvenli yönde: (1) <see cref="OnRunStarted"/>
    /// ölüm metnini temizler ama durumu Ready'ye çekmez (Ready yalnız hazır yolundan gelir) — durum en kötü "ölü" kalır ve kural uyutmaz;
    /// (2) [T20 incelemesi M3] ölüm sinyali gelmeden düşen bir yeniden başlatma (çıkışta dispose öne geçti ya da eski motorun öldürülmesi
    /// fırlattı) durumu <see cref="EngineState.Starting"/>'te bırakır — kural uyutmaz, motor kapısı Starting'te ne uyandırır ne bekler, gönderim
    /// bugünkü gibi gider (gerekçe <see cref="RestartEngineAsync"/>'te). Bilinen sınır (<see cref="WakeEngineAsync"/>): uyanmak için doğan motorun
    /// ölüm sinyali hiç gelmezse (konakta öldürmenin kendisi fırlattı) durum <see cref="EngineState.Waking"/>'de kalır.
    /// </summary>
    internal EngineState EngineState { get; private set; } = EngineState.Starting;

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
    /// motor etkinliği (<see cref="_lastEngineSignalMs"/>). Doğruysa motor uyutulur ve uykunun görevi saklanır: motor kapısı uyku sürerken gelen
    /// komutu onun bitişine bekletir (<see cref="_sleepInFlight"/>).</summary>
    private void EvaluateEngineIdle()
    {
        if (EngineIdleRule.ShouldSleep(nowMs: _nowMs(), lastActivityMs: Volatile.Read(ref _lastEngineSignalMs),
                settingOn: StopEngineWhenIdle, operationInFlight: IsOperationInFlight, pendingLogLoad: HasPendingLogLoad,
                engine: EngineState))
            _sleepInFlight = PutEngineToSleepAsync();
    }

    /// <summary>
    /// Boştaki motoru uyutur. Durum ÖNCE ve senkron <see cref="EngineState.Sleeping"/> olur: konağın uykusu (shutdown, en çok
    /// <see cref="EngineHost.SleepExitWait"/> zarif çıkış, gerekirse öldürme) sürerken gelen tik bir geçiş görür ve ikinci uykuyu başlatmaz
    /// (tek uçuş — C3). Uyku sürerken başka bir sinyal durumu değiştirdiyse — motor kendiliğinden öldü: konakta ölüm hakkını bekçi aldı, uyku
    /// hiçbir şey yapmadan döner — o sinyal kazanır ve bitişte dokunulmaz. Değiştirmediyse bitiş üç yoldan biridir:
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
    /// </summary>
    private async Task PutEngineToSleepAsync()
    {
        EngineState = EngineState.Sleeping;
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

    /// <summary>Uykusu düşen motorun ölüm satırındaki nedeni (<see cref="ReportEngineDead"/>): "Engine stopped unexpectedly (idle shutdown
    /// failed)" — kendiliğinden ölümün çıkış kodu ya da protokol hatası yerine.</summary>
    private const string IdleShutdownFailedCause = "idle shutdown failed";

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

    /// <summary>[Task 21 · karar 32] Uçuştaki (ya da son) uyku — motor kapısının bekleme tutamağı; <see cref="EvaluateEngineIdle"/> yazar.
    /// Durumun tek kaynağı yine <see cref="EngineState"/>'tir: kapı bu göreve yalnız durum <see cref="EngineState.Sleeping"/> iken bakar. Görev
    /// düşmez (<see cref="PutEngineToSleepAsync"/> her hatayı kendisi karşılar) ve bitişinde durum her yolda Sleeping'den çıkmıştır.</summary>
    private Task _sleepInFlight = Task.CompletedTask;

    /// <summary>[Task 21 · karar 32] Uçuştaki (ya da son) uyanma — TEK uçuş: durum <see cref="EngineState.Waking"/> iken kapıya gelen her yol bu
    /// göreve katılır. Sonucu uyanan motorun hazır olup olmadığıdır (<see cref="WakeEngineAsync"/>).</summary>
    private Task<bool> _wakeInFlight = Task.FromResult(false);

    /// <summary>
    /// [idle-memory · dalga 3 · spec §6.4 · Task 21 · karar 17/18/19/32/33] <b>Motor kapısı</b> — motora giden HER gönderim önce buradan geçer:
    /// gönderimin tek yolu (<see cref="TrySendAsync"/>), proje log isteği (<see cref="LoadProjectLogAsync"/>) ve komutunu kurmadan önce bekleyen-koşu
    /// penceresinde soran koşu (<see cref="BeginRunAsync"/>). Yalnız VM'in motor durumunu okur (<see cref="EngineState"/> — ölüm metnini ya da
    /// konağın uykusunu değil) ve "bu komut uyandırır mı" sorusunu TEK tabloya sorar (<see cref="EngineWakeRule"/>):
    /// <list type="bullet">
    /// <item><see cref="EngineState.Ready"/> → gönder.</item>
    /// <item><see cref="EngineState.Starting"/> → uyandırma da bekleme de yok: gönder — bugünkü gibi; açılışta ve yeniden başlatmada doğmakta olan
    /// motorun borusu komutu tutar ya da konak bugünkü "Engine is not running." hatasını verir. Starting kalıcı da olabilir
    /// (<see cref="EngineState"/>'in ikinci ayrışması); beklemek onu sonsuza dek bekletirdi.</item>
    /// <item><see cref="EngineState.Sleeping"/> → uyandıran komut uykunun bitmesini bekler (ölmekte olan sürece yazılmaz) ve durumu yeniden okur.</item>
    /// <item><see cref="EngineState.Asleep"/> → uyandıran komut TEK uyanmayı başlatır (<see cref="WakeEngineAsync"/>).</item>
    /// <item><see cref="EngineState.Waking"/> → uyandıran komut uçuştaki uyanmaya katılır; uyandıysa durum yeniden okunur, uyanamadıysa gönderilmez.</item>
    /// <item><see cref="EngineState.Dead"/> / <see cref="EngineState.Unavailable"/> → uyandırma yok, gönderme yok: kaybı ya da yokluğu şerit söyler
    /// (tek sinyal); toparlamak "Restart engine"in ya da kurulumun işidir. Düşen bir uyanmadan sonra yeniden deneme de burada kesilir — Sync'in
    /// ardından giden branch envanteri dahil (C23).</item>
    /// <item>Uyandırmayan komut (Stop, perf chip) motor uyanık değilken — uyku sürerken, uyurken, uyanırken — hiç gönderilmez (karar 33).</item>
    /// </list>
    /// <c>false</c> "gönderme" demektir ve kapı konsola satır YAZMAZ: düşen uyanmanın tek sinyali kendi yolundadır (kurulum hatası
    /// <see cref="OnEngineUnavailable"/>, ölüm <see cref="ReportEngineDead"/>); her çağıran bugünkü "gönderim düştü" geri alımını çalıştırır.
    /// Bütün çağıranlar UI thread'indedir ve durum senkron yazılır — alanlar kilitsizdir. Motor hazırken kapı askıya girmez: gönderim bugünkü
    /// gibi çağıranın ilk await'inden önce, senkron gider.
    /// </summary>
    /// <param name="command">Gönderilecek komutun türü — tablonun sorusu.</param>
    /// <returns>Gönder (<c>true</c>) ya da gönderme (<c>false</c>).</returns>
    private async Task<bool> EnsureEngineAwakeAsync(Type command)
    {
        bool wakes = EngineWakeRule.Wakes(command);
        while (true)
        {
            var state = EngineState;
            if (state is EngineState.Ready or EngineState.Starting) return true;
            if (!wakes) return false;
            if (state == EngineState.Sleeping)
            {
                await _sleepInFlight; // uyku her yolda Sleeping'den çıkar (Asleep ya da Dead) — sonra yeniden oku
                continue;
            }
            if (state == EngineState.Asleep) _wakeInFlight = WakeEngineAsync();
            else if (state != EngineState.Waking) return false; // Dead, Unavailable
            if (!await _wakeInFlight) return false;
            // Uyandı: durum yeniden okunur — hazırsa gönder; arada başka bir sinyal geldiyse (motor öldü) onun kuralı geçerlidir.
        }
    }

    /// <summary>
    /// [Task 21 · karar 19/20/32 · C5] Uyuyan motoru uyandırır — TEK uçuş: durum ÖNCE ve senkron <see cref="EngineState.Waking"/> olur, kapıya bu
    /// arada gelen her yol bu göreve katılır (konakta da kapı tektir: iki eşzamanlı uyanma tek süreç doğurur). Dönen değer uyanan motorun hazır
    /// olup olmadığıdır. Uyanma sürerken başka bir sinyal durumu değiştirdiyse (uyanan motor öldü) o kazanır: bitişte dokunulmaz, "hazır değil"
    /// döner. Değiştirmediyse bitiş dört yoldan biridir:
    /// <list type="bullet">
    /// <item><b>Uyandı</b> → <see cref="OnEngineResumed"/>: kimlik ve hazır durumu; konsol satırı ve açılış Sync'i YOK.</item>
    /// <item><b>Motor bulunamadı / başlatılamadı</b> (<see cref="EngineUnavailableException"/>) → bugünkü erişilemezlik yolu
    /// (<see cref="OnEngineUnavailable"/>: TEK konsol satırı, kalıcı şerit, komutlar kapalı).</item>
    /// <item><b>Çıkışta vazgeçildi</b> — dispose uyanmanın önüne geçti (<see cref="OperationCanceledException"/>) ya da konak çoktan dispose
    /// edilmişti (<see cref="ObjectDisposedException"/>) → <see cref="EngineState.Asleep"/>: süreci çıkışın kapanışı alıyor; satır yok, ölüm yok.</item>
    /// <item><b>Doğan motor hazır olamadı</b> (zaman aşımı, erken ölüm, çerçeve hatası) ya da konakta uyandırılacak motor yoktu (ölümü
    /// bildirilmişti) → satır YOK ve durum burada yazılmaz: konak o motoru öldürür ve ölümünü <see cref="EngineHost.EngineExited"/> TEK sinyal olarak
    /// bildirir (<see cref="OnEngineExited"/> → <see cref="EngineState.Dead"/>, şerit ve "Restart engine"). O sinyal gelene dek durum
    /// <see cref="EngineState.Waking"/> kalır: kapıya gelen yol düşmüş uyanmaya katılır ve hemen "gönderme" alır — ikinci bir uyandırma denemesi
    /// yoktur (C23). Bilinen sınır: ölüm sinyali hiç gelmezse (konakta öldürmenin kendisi fırlattı — T18'in uyku için de belgelediği boşluk)
    /// durum <see cref="EngineState.Waking"/>'de kalır.</item>
    /// </list>
    /// </summary>
    private async Task<bool> WakeEngineAsync()
    {
        EngineState = EngineState.Waking;
        try
        {
            var ready = await WakeEngine();
            if (EngineState != EngineState.Waking) return false;
            OnEngineResumed(ready.EngineVersion, ready.Pid);
            return true;
        }
        catch (Services.EngineUnavailableException ex)
        {
            if (EngineState == EngineState.Waking) OnEngineUnavailable(ex.ExePath, ex.Reason);
            return false;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            if (EngineState == EngineState.Waking) EngineState = EngineState.Asleep;
            return false;
        }
        catch (Exception)
        {
            return false; // ölüm sinyali gelir — gerekçe özette
        }
    }

    /// <summary>[Task 21 · karar 20 · Q8] Uyuyan motor uyandı: kimlik alanları ve hazır durumu — hazır yolunun ÇEKİRDEĞİ
    /// (<see cref="EnterEngineReady"/>), açılışa özgü işleri OLMADAN. Konsola "Engine ready" satırı yazılmaz, eski havuz ipucu yazılmaz, açılış
    /// Sync'i tekrarlanmaz: uyanan motor komutlar arası bir durum kaybetmedi (her komut kökünü ve ayarını taşır) ve Sync ekranın ihtiyacıdır,
    /// motorun değil — ekran uyku boyunca değişmedi. Tek istisna motorun kendi bilgisidir (C21): uyanan motor açılışta yarım kalmış bir koşu
    /// bulduysa hazır olayı <c>InterruptedProjects</c> taşır ve satırı olay akışından bugünkü gibi düşer (<see cref="OnEngineRecovered"/>).</summary>
    private void OnEngineResumed(string engineVersion, int pid) => EnterEngineReady(engineVersion, pid);

    /// <summary>[idle-memory · dalga 3] Motor öldü — kullanıcıya bildirmenin TEK yolu: kendiliğinden ölüm (<see cref="OnEngineExited"/>) ve
    /// uykusu düşüp kaybedilen motor (<see cref="PutEngineToSleepAsync"/>). Durum <see cref="EngineState.Dead"/>, şeritte kırmızı ölüm satırı
    /// ve "Restart engine"; uçuştaki pencereler bırakılır (<see cref="ReleaseAfterEngineLoss"/>). Konsola satır yazmaz (boşta bir motor
    /// kaybı hiçbir işlemi bitirmez).
    /// <para>[Task 21 · karar 34] <b>Bir geçişin ortasındaki kayıp hiçbir pencereyi bırakmaz.</b> Motor uyku ya da uyanma sürerken
    /// kaybedildiyse (düşen uyku; uyanmak için doğan motorun ölümü) ona son hazır olduğu andan beri hiçbir şey GİTMEDİ: uyku yalnız uçuşta işlem
    /// yokken başlar ve geçiş boyunca motor kapısı (<see cref="EnsureEngineAwakeAsync"/>) hiçbir komutu göndermez. O an açık görünen her istek
    /// penceresi kapıda bekleyen — ya da açılış koreografisinde kapıya yürüyen — bir isteğindir; motor kaybı onu bırakmaz (hiç başlamamış bir
    /// işlem bitmiş sayılmaz: ne Sync'in <c>EndSync</c>/<c>MarkOperationEnded</c>'ı, ne koşunun faz/<see cref="IsStarting"/> sıfırlaması). Kapı
    /// "gönderme" deyince isteğin kendi "gönderim düştü" geri alımı bırakır — geri alım TEK yerde ve tek sırayla olur.</para></summary>
    /// <param name="cause">Ölüm satırının parantezindeki neden (İngilizce): <c>exit N</c>, <c>protocol error</c> ya da uykunun düşüşü.</param>
    private void ReportEngineDead(string cause)
    {
        bool lostInTransition = EngineState is EngineState.Sleeping or EngineState.Waking;
        EngineState = EngineState.Dead;
        // [E2/T37 · İngilizce sweep] Şerit kalıcı-hata modu bu metni GÖSTERİR → İngilizce (tüm UI/konsol metni).
        // [D1] Doğmuş bir motorun ölümü YENİDEN BAŞLATILABİLİR — şerit "Restart engine" aksiyonunu gösterir
        // (EngineDiedMessage yazan her yol bu bayrağı da yazar; bkz. EngineRestartable).
        EngineRestartable = true;
        EngineDiedMessage = $"Engine stopped unexpectedly ({cause})";
        if (!lostInTransition) ReleaseAfterEngineLoss();
    }
}
