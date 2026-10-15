using System.Globalization;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Core.Paths;
using BuildOrchestrator.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [idle-memory · dalga 3 · spec §6.4] <see cref="RunViewModel"/>'in <b>motor yaşam döngüsü</b> yüzeyi — tamamı bu dosyadadır: VM'in motor
/// durumu (<see cref="EngineState"/>), boşta kuralının uykusu (<see cref="EvaluateEngineIdle"/>), motor kapısı ve uyanma
/// (<see cref="EnsureEngineAwakeAsync"/>), TEK hazır geçişi (<see cref="EnterEngineReady"/>), motor kaybının bildirimi
/// (<see cref="ReportEngineDead"/>), şeridin motor bayrakları ve son motor etkinliği (<see cref="EngineDiedMessage"/>,
/// <see cref="EngineOverdueMessage"/>, <see cref="_lastEngineSignalMs"/>), gönderim (<see cref="TrySendAsync"/>,
/// <see cref="SendWithFailureLineAsync"/>, <see cref="SendToEngineAsync"/>), sessizlik bekçisi (<see cref="EvaluateEngineSilence"/>), motor
/// olaylarının girişleri (<see cref="OnEngineReady"/>, <see cref="OnEngineExited"/>, <see cref="OnEngineUnavailable"/>), kayıp sonrası
/// bırakma (<see cref="ReleaseAfterEngineLoss"/>) ve "Restart engine" (<see cref="RestartEngineAsync"/>). Ana dosyada kalan iki dokunuş:
/// 200 ms'lik tik (<see cref="TickElapsed"/> — koşu süresi, ETA ve spool uyarısının da tikidir; motorun iki işini buradan çağırır) ve olay
/// pompası (<see cref="OnEvent"/> — motor etkinliğini damgalar, <c>engineReady</c>'yi <see cref="OnEngineRecovered"/>'a verir).
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
    /// hakkını bekçi aldı; uyku ya kontrolünde durur ya da kontrolünden sonra sahiplenmeyi kaybedip süreci toparlar ve UYUMADAN döner (konağın
    /// ölüm–durdurma kuralı) — o sinyal kazanır ve bitişte dokunulmaz. Değiştirmediyse bitiş üç yoldan biridir:
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
    /// Tutamak bitiş durumu yazıldıktan SONRA tamamlanır: uykuyu bekleyen yol uyandığında durumu oturmuş bulur. [T20 M5] Bitiş UI thread'inde
    /// yazılır: dikişin görevi bağlam yakalanarak beklenir — konağın uykusu havuzda biter, devam UI'ın dispatcher bağlamına post edilir
    /// (<c>EngineWakeTests.The_sleep_and_the_wake_write_the_engine_state_on_the_ui_thread</c>).
    /// </summary>
    private async Task PutEngineToSleepAsync(TaskCompletionSource settled)
    {
        bool lost = false;
        try
        {
            // ConfigureAwait YOK (bilerek): devam UI thread'inde koşar — durum ve şeridin bayrakları yalnız orada yazılır.
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
    /// failed)" — kendiliğinden ölümün çıkış kodu ya da protokol hatası yerine. <c>internal</c>: testler metni <see cref="EngineDiedText"/> ile
    /// buradan kurar, literali bir kez pinler (M-5).</summary>
    internal const string IdleShutdownFailedCause = "idle shutdown failed";

    /// <summary>[Fix round 1 · Mi1] Ölüm sinyali hiç gelmeyen düşmüş uyanmanın ölüm satırındaki nedeni: "Engine stopped unexpectedly (wake failed)"
    /// (<see cref="EnsureEngineAwakeAsync"/>). Aynı motorun geç gelen ölüm sinyali yalnız metni kendi nedeniyle yeniler — ölü motorun ikinci
    /// bildirimi pencere bırakmaz (<see cref="ReportEngineDead"/>, M-3). <c>internal</c>: <see cref="IdleShutdownFailedCause"/> ile aynı gerekçe.</summary>
    internal const string WakeFailedCause = "wake failed";

    /// <summary>Şeridin ölüm satırı — biçimin TEK yeri (<see cref="ReportEngineDead"/>): <c>Engine stopped unexpectedly ({neden})</c>. Testler
    /// bekleyen nedenlerin metnini bundan kurar; her metnin literal pini tektir (M-5).</summary>
    internal static string EngineDiedText(string cause) => $"Engine stopped unexpectedly ({cause})";

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
    /// bekleyen yeniden denemez (C23). Bekleyen istek kayıtlıdır (<see cref="_waitingAtGate"/>) ve kayıt kapının geçişiyle çağırana gider
    /// (<see cref="GatePass"/>): istek motora teslim edilene ya da geçiş bırakılana dek motor kaybı onun penceresini bırakmaz — uyanma başarılı olup
    /// motor hemen öldüyse, isteğin devamı henüz koşmadıysa ya da kapının kararıyla teslim arasındaki dispatcher sıçrayışında da; geri alımı tek
    /// yerde, kendisinindir (karar 34, T21 OOS-1).</item>
    /// <item><b>Çıkış ve geri alınan istek (R3, Mi4):</b> her bekleyişin ardından ve bir uyanmayı başlatmadan önce sorulur — çıkış istendiyse ya da
    /// çağıranın isteği artık istenmiyorsa (<paramref name="stillWanted"/>: bekleyen-koşu penceresi geri alındı, proje logu yüklemesinin yerini
    /// yenisi aldı) istek gitmez ve yeni uyanma başlamaz. Bayrak "çıkış istendi"dir, "çıkış bekliyor" değil: yalnız proje logu bekleyen bir
    /// kapıda çıkış hiç beklemez, o istek de gitmemelidir.</item>
    /// <item><b>Askıda uyanma (Mi1):</b> uyanma düşmüş ve ölüm sinyali hiç gelmemişse (konakta öldürmenin kendisi fırladı) kapıya YENİ giren ilk yol —
    /// uçuştaki bir uyanmaya katılan değil — motoru kayıp bildirir (<see cref="ReportEngineDead"/>, neden <see cref="WakeFailedCause"/>: şerit +
    /// "Restart engine"); o yolun isteği gitmez. Aynı motorun geç gelen ölüm sinyali yalnız şeridin metnini yeniler (ölü motorun ikinci bildirimi
    /// — <see cref="ReportEngineDead"/>). Çıkmaz sokak yoktur.</item>
    /// </list>
    /// Kararı "gönderme" olan geçiş kapının satır YAZMADIĞI anlamına gelir; her çağıran bugünkü "gönderim düştü" geri alımını çalıştırır. Bütün
    /// çağıranlar UI thread'indedir ve durum senkron yazılır — alanlar kilitsizdir. Kapının işi olmayan girişte kapı askıya girmez: gönderim
    /// bugünkü gibi çağıranın ilk await'inden önce, senkron gider. Uyku ve uyanmanın tutamağı durumdan ÖNCE yazıldığı için (Mi5) kapının
    /// döngüsü her turda yeni bir geçişi bekler — hiç boşa dönmez.
    /// </summary>
    /// <param name="command">Gönderilecek komutun türü — tablonun sorusu.</param>
    /// <param name="stillWanted">İsteğin hâlâ istenip istenmediği (verilmezse: istenir). Yalnız bekleyişten sonra ve uyanma başlamadan önce sorulur.</param>
    /// <returns>Kapının geçişi: kararı (<see cref="GatePass.Send"/>) ve — istek kapıda beklediyse — kaydı. Çağıran geçişi <c>using</c> ile tutar
    /// ve gönderimine verir (<see cref="SendWithFailureLineAsync"/>); kaynak guard'ı her çağrının bir <c>using</c> olduğunu denetler
    /// (<c>EngineGateGuardTests</c>).</returns>
    private async Task<GatePass> EnsureEngineAwakeAsync(Type command, Func<bool>? stillWanted = null)
    {
        var state = EngineState;
        if (!InSleepCycle(state)) return GatePass.Go;
        if (state == EngineState.Waking && _wakeInFlight is { IsCompletedSuccessfully: true, Result: false })
        {
            ReportEngineDead(WakeFailedCause);
            return GatePass.Refused;
        }
        if (!EngineWakeRule.Wakes(command)) return GatePass.Refused;
        int ticket = EnterGate(command);
        bool handedToCaller = false;
        try
        {
            bool send = false;
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
                        if (!StillWanted(stillWanted)) break;
                        StartWake();
                    }
                    if (!await _wakeInFlight) break;
                }
                if (!StillWanted(stillWanted)) break;
                state = EngineState;
                if (state is EngineState.Ready or EngineState.Starting)
                {
                    send = true;
                    break;
                }
                if (!InSleepCycle(state)) break;
            }
            handedToCaller = true; // kayıt geçişle çağırana gider; teslim ya da geçişin bırakılması siler
            return GatePass.Held(send, this, ticket);
        }
        finally
        {
            if (!handedToCaller) LeaveGate(ticket); // savunma: geçiş çağırana hiç ulaşmazsa kayıt burada biter
        }
    }

    /// <summary>Kapıda bekleyen istek hâlâ isteniyor mu: çıkış istenmedi ve çağıranın kendi koşulu (varsa) doğru (<see cref="EnsureEngineAwakeAsync"/>,
    /// madde 4).</summary>
    private bool StillWanted(Func<bool>? stillWanted) => !_exitRequested && (stillWanted?.Invoke() ?? true);

    /// <summary>
    /// [T21 OOS-1 · round 2] Motor kapısının geçişi: kapının kararı (<see cref="Send"/>) ve kapıda BEKLEMİŞ bir isteğin kaydı
    /// (<see cref="_waitingAtGate"/>). Kayıt, istek motora teslim edilene (<see cref="SendWithFailureLineAsync"/> teslimden hemen önce bırakır) ya da
    /// geçiş dispose edilene dek yaşar; çağıran geçişi <c>using</c> ile tutar — kayıt hiçbir yolda sızamaz. Böylece kapının "gönder" kararıyla teslim
    /// arasındaki dispatcher sıçrayışında (her <c>await</c> devamını post eder) gelen bir motor kaybı isteği bitmiş saymaz: istek teslim edilmedi,
    /// ölü motora gönderimi bugünkü satırıyla düşer ve geri alımı kendisinindir. Kayıt teslimde biter, gönderimin sonunda DEĞİL: teslimden sonra gelen
    /// kayıp isteği gönderilmiş sayar (dalga öncesi anlam) — aksi hâlde yazımı başarıyla biten ama motoru ölmüş bir isteğin penceresini kimse
    /// bırakmazdı. Beklemeyen isteğin (motor uyku döngüsünde değildi) geçişi kayıt taşımaz, Dispose'u no-op'tur. Kopyalar aynı kaydı gösterir;
    /// Dispose tekrarlanabilir.
    /// </summary>
    private readonly struct GatePass : IDisposable
    {
        private readonly RunViewModel? _owner;
        private readonly int _ticket;

        private GatePass(bool send, RunViewModel? owner, int ticket)
        {
            Send = send;
            _owner = owner;
            _ticket = ticket;
        }

        /// <summary>Beklemeden "gönder": motor uyku döngüsünde değildi — kayıt yok.</summary>
        public static GatePass Go => new(true, null, 0);

        /// <summary>Beklemeden "gönderme": uyandırmayan komut ya da askıdaki uyanma — kayıt yok (<see cref="None"/>).</summary>
        public static GatePass Refused => None;

        /// <summary>[round 3] Kayıt yok — gönderime "devredilecek kayıt yok" demenin AÇIK yolu: <see cref="SendWithFailureLineAsync"/>'in geçişi
        /// zorunludur, varsayılanı yoktur. Tek kullanıcısı kapıdan geçmiş Sync'in ikinci parçası, branch envanteridir: kapının kararı ve kaydı
        /// Sync'in geçişindeydi ve Sync'in tesliminde bitti. Kararı "gönderme"dir; gönderim kararı okumaz.</summary>
        public static GatePass None => default;

        /// <summary>Kapıda beklemiş isteğin geçişi — kararı ve kaydı.</summary>
        public static GatePass Held(bool send, RunViewModel owner, int ticket) => new(send, owner, ticket);

        /// <summary>Kapının kararı: gönder (<c>true</c>) ya da gönderme.</summary>
        public bool Send { get; }

        /// <summary>Kaydı bırakır — kayıt yoksa ya da zaten bırakıldıysa hiçbir şey yapmaz.</summary>
        public void Dispose() => _owner?.LeaveGate(_ticket);
    }

    /// <summary>[T21 OOS-1 · karar 34 · round 2] Kapıda beklemiş, motora henüz teslim edilmemiş isteklerin kayıtları (bilet, komut türü): kapı beklemeye
    /// başlarken yazar (<see cref="EnterGate"/>), geçiş teslimde ya da dispose'ta siler (<see cref="LeaveGate"/>). Motor kaybı kayıtlı isteklerin
    /// pencerelerini bırakmaz (<see cref="ReleaseAfterEngineLoss"/>). Bir türün penceresi aynı anda tek isteğindir — kapılar ikincisini açmaz —; kayıt
    /// yine de biletle tutulur, böylece her geçiş yalnız kendi kaydını bırakır.</summary>
    private readonly List<(int Ticket, Type Command)> _waitingAtGate = [];

    /// <summary>Son verilen bilet (<see cref="_waitingAtGate"/>).</summary>
    private int _lastGateTicket;

    /// <summary>Kapıda beklemeye başlayan isteği kaydeder; biletini döner.</summary>
    private int EnterGate(Type command)
    {
        int ticket = ++_lastGateTicket;
        _waitingAtGate.Add((ticket, command));
        return ticket;
    }

    /// <summary>Bileti kayıttan siler — yoksa hiçbir şey yapmaz (teslim ve dispose ikisi de çağırır).</summary>
    private void LeaveGate(int ticket)
    {
        int index = _waitingAtGate.FindIndex(entry => entry.Ticket == ticket);
        if (index >= 0) _waitingAtGate.RemoveAt(index);
    }

    /// <summary>Bu türde bir istek kapıdan geçişini tutuyor mu — beklemiş ve motora henüz teslim edilmemiş (<see cref="_waitingAtGate"/>).</summary>
    private bool WaitsAtTheGate(Type command) => _waitingAtGate.Exists(entry => entry.Command == command);

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
    /// [T20 M5] Bitiş UI thread'inde yazılır: dikişin görevi bağlam yakalanarak beklenir — konağın uyanması havuzda biter, devam UI'ın dispatcher
    /// bağlamına post edilir (<c>EngineWakeTests.The_sleep_and_the_wake_write_the_engine_state_on_the_ui_thread</c>).
    /// </summary>
    private async Task WakeEngineAsync(TaskCompletionSource<bool> woke)
    {
        bool ready = false;
        try
        {
            // ConfigureAwait YOK (bilerek): devam UI thread'inde koşar — durum, kimlik ve şeridin bayrakları yalnız orada yazılır.
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
    /// (<see cref="ReleaseAfterEngineLoss"/> — kapıdan geçişini tutan isteklerinkiler hariç). Konsola satır yazmaz (boşta bir motor kaybı hiçbir işlemi
    /// bitirmez).
    /// <para>[Task 21 · karar 34 · Fix round 1 · Mi2] <b>Uyku döngüsündeki kayıp hiçbir pencereyi bırakmaz.</b> Motor uyku sürerken, uyurken ya da
    /// uyanırken kaybedildiyse (düşen uyku; uyutulan motorun geç gelen ölüm sinyali; uyanmak için doğan motorun ölümü) ona son hazır olduğu andan
    /// beri hiçbir şey GİTMEDİ: uyku yalnız uçuşta işlem yokken başlar ve uyku döngüsünde gönderim yoktur (<see cref="EnsureEngineAwakeAsync"/>).
    /// O an açık görünen her istek penceresi kapıda bekleyen bir isteğindir (koşu da kapıya tıklamada, koreografisiyle örtüşerek sorar); motor kaybı onu
    /// bırakmaz (hiç başlamamış bir işlem bitmiş sayılmaz: ne Sync'in <c>EndSync</c>/<c>MarkOperationEnded</c>'ı, ne koşunun faz/<see cref="IsStarting"/>
    /// sıfırlaması). İsteğin kendi "gönderim düştü" geri alımı bırakır — geri alım TEK yerde ve tek sırayla olur.</para>
    /// <para>[M-3 · T21 N1] <b>Ölü motorun ikinci bildirimi yalnız metni yeniler.</b> Dead'e iki kez yalnız aynı kayıp ulaşır: ölüm sinyali hiç gelmeyen
    /// düşmüş uyanmanın raporu (<see cref="WakeFailedCause"/>) ve aynı motorun sonradan gelen gerçek ölüm sinyali — başka hiçbir yol ölü bir motoru
    /// yeniden ölü bildirmez (yeni bir motor ancak "Restart engine" ya da uyanmayla doğar ve ikisi de durumu önce değiştirir). İlk bildirim
    /// pencereleri kendi kuralıyla zaten ele aldı; ikincisi bırakırsa arada açılan bir işin açılışını (koşunun Starting'i ve fazı) altından sıfırlar
    /// ve o iş kendi geri alımını ikinci kez çalıştırırdı.</para></summary>
    /// <param name="cause">Ölüm satırının parantezindeki neden (İngilizce): <c>exit N</c>, <c>protocol error</c>, uykunun ya da uyanmanın düşüşü.</param>
    private void ReportEngineDead(string cause)
    {
        var lost = EngineState;
        EngineState = EngineState.Dead;
        // [E2/T37 · İngilizce sweep] Şerit kalıcı-hata modu bu metni GÖSTERİR → İngilizce (tüm UI/konsol metni).
        // [D1] Doğmuş bir motorun ölümü YENİDEN BAŞLATILABİLİR — şerit "Restart engine" aksiyonunu gösterir
        // (EngineDiedMessage yazan her yol bu bayrağı da yazar; bkz. EngineRestartable).
        EngineRestartable = true;
        EngineDiedMessage = EngineDiedText(cause);
        if (lost != EngineState.Dead && !InSleepCycle(lost)) ReleaseAfterEngineLoss();
    }

    // ---------------------------------------------------------------- şeridin motor bayrakları ve son motor etkinliği

    /// <summary>[Task 16 — It-2 devir §8] Engine process öldüğünde (<see cref="OnEngineExited"/>) kullanıcıya
    /// gösterilecek metin — sticky şerit kalıcı hata modunun PIXEL karşılığı It-4'te; burada yalnız VM-state.
    /// [Review fix] Kalıcı DEĞİLDİR: bir sonraki run'ın <see cref="OnRunStarted"/>'ı (engine'in CANLI ve IPC
    /// round-trip yaptığının ilk somut kanıtı) bu mesajı temizler — aksi halde tek bir ölümden sonra sonsuza
    /// dek stale kalıp, tamamen başarılı sonraki run'larda bile güncel engine sağlığını yanlış yansıtırdı.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEngineUnavailable))]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizeCommand))]
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))] // [§6.3] chip kapısının TEK bildirim kaynağı (bkz. CanSwitchBranch)
    private string? _engineDiedMessage;

    /// <summary>[D1] Şeridin kalıcı hata modundaki "Restart engine" aksiyonu ANLAMLI mı? Normal bir motor ölümü
    /// yeniden başlatılabilir (true); Supervisor çıktısı hiç bulunamadığında (<see cref="OnEngineUnavailable"/>)
    /// yeniden başlatmak eksik dosyayı geri getirmeyeceği için aksiyon GİZLENİR ve kullanıcı yalnız ne yapması
    /// gerektiğini anlatan metni görür. <b>Değişmez:</b> <see cref="EngineDiedMessage"/>'ı yazan HER yol bunu da
    /// yazar (ölüm → true, kurulum eksik → false); mesaj temizlendiğinde değerin önemi kalmaz.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEngineUnavailable))]
    [NotifyCanExecuteChangedFor(nameof(RebuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(RebuildProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanProjectCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanAllCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BuildCyclesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanCommand))]
    [NotifyCanExecuteChangedFor(nameof(OptimizeCommand))]
    [NotifyPropertyChangedFor(nameof(CanSwitchBranch))] // [§6.3] chip kapısının TEK bildirim kaynağı (bkz. CanSwitchBranch)
    private bool _engineRestartable = true;

    /// <summary>[D1 review · A3] Motor ERİŞİLEMEZ: hiç doğamadı (supervisor yok ya da başlatılamıyor) —
    /// <see cref="OnEngineUnavailable"/> bu durumu kurar. Sync/Build/Rebuild bu durumda
    /// ANLAMSIZDIR: gönderim zaten hataya düşer ve şeritteki kalıcı mesajla ÇELİŞEN ikinci bir hata satırı
    /// üretirdi — bu yüzden komutlar devre dışıdır ("Restart engine"in gizlenmesiyle aynı mantık).
    /// <para>Normal (doğmuş) motor ölümü BU DURUM DEĞİLDİR: orada "Restart engine" sunulur ve komutlar açık
    /// kalır — E2/T37 davranışı korunur.</para></summary>
    public bool IsEngineUnavailable => EngineDiedMessage is { Length: > 0 } && !EngineRestartable;

    /// <summary>[D1] Supervisor çıktısı uygulamanın yanında bulunamadığında şeritte gösterilen KALICI satır.
    /// design-v1 §"Ton" (sakin, kesin, mühendisçe; ünlem yok) ve mevcut hata satırlarının em-dash/`·` dili.
    /// Ham exception dump'ı DEĞİL — tam yol konsol anlatısına düşer.</summary>
    public const string EngineMissingMessage =
        "Engine missing — supervisor was not found next to the app · reinstall required";

    /// <summary>[D1 review · A2] Supervisor dosyası VAR ama başlatılamadı (bozuk/geçersiz exe, erişim reddi,
    /// TOCTOU). Nedeni <see cref="EngineMissingMessage"/>'dan AYIRT EDER; ham exception metni gösterilmez.</summary>
    public const string EngineCannotStartMessage =
        "Engine could not start — the supervisor next to the app would not launch · reinstall required";

    /// <summary>Motorun bir GEÇİŞ beklenirken susabileceği en uzun süre. Aşılırsa kullanıcıya çıkış kapısı
    /// gösterilir (<see cref="EngineOverdueMessage"/> + şeritteki "Restart engine").
    /// <para><b>Neden bu kadar uzun:</b> planlama 177 projelik bir workspace'te on saniyelerce sürebilir ve
    /// graceful drain uçuştaki en uzun <c>MSBuild.exe</c> kadar sürer; ikisi de MEŞRUdur. Eşik "yavaş"ı
    /// değil "hiç konuşmuyor"u yakalamalıdır — motorun HERHANGİ bir event'i saati sıfırlar
    /// (<see cref="OnEvent"/>), yani bu süre boyunca TEK satır bile gelmemiş olması gerekir.</para></summary>
    internal const long EngineSilenceThresholdMs = 90_000;

    /// <summary>Motor bir geçiş beklenirken <see cref="EngineSilenceThresholdMs"/> boyunca hiç konuşmadı.
    /// Şerit bunu AMBER gösterir (kırmızı değil: bu bir başarısızlık değil, bir bekleyiştir) ve "Restart
    /// engine" aksiyonunu açar.
    ///
    /// <para><b>Neden gerekiyor:</b> ölçülen üretim vakasında motor planlamanın ortasında dondu — App
    /// <c>IsStarting</c>'te, ardından <c>Stopping</c>'te SONSUZA DEK kilitli kaldı ve tek çıkış uygulamayı
    /// kapatmaktı. "Restart engine" aksiyonu ZATEN vardı ama görünürlüğü <see cref="EngineDiedMessage"/>'a,
    /// yani process'in GERÇEKTEN ölmesine bağlıydı; yaşayan-ama-donmuş motorda hiç görünmüyordu.</para>
    ///
    /// <para><b>Neden ping/pong değil:</b> o vakada motorun komut döngüsü canlıydı (run arka plan task'ında
    /// koşar), yani bir ping'e pong dönerdi. Doğru sinyal "yaşıyor mu" değil "beklenen cevabı veriyor mu".</para>
    ///
    /// <para><b>Otomatik kurtarma YOK:</b> bu bayrak hiçbir kilidi kendiliğinden açmaz — graceful drain
    /// dakikalarca sürebilir ve kilidi açmak, hâlâ koşan bir motora ikinci bir run başlatmaya izin vermek
    /// olurdu. Yalnız kapı gösterilir; açıp açmamak kullanıcının kararıdır.</para></summary>
    [ObservableProperty] private string? _engineOverdueMessage;

    /// <summary>SON MOTOR ETKİNLİĞİNİN zamanı (monotonik) — motorla konuşmanın TEK damgası [idle-memory · dalga 3 · karar 13 · C8: ikinci bir
    /// etkinlik damgası yoktur]. Yazımı tek yerdedir (<see cref="StampEngineActivity"/>). Üç yazan: motordan gelen her olay (<see cref="OnEvent"/>), motordan cevap beklenen her bekleyişin başı — yani
    /// komut gönderimi (<see cref="ArmEngineWatchdog"/>: koşu isteği, Sync, Clean, Optimize, checkout, pull, Stop/Sync fazı) — ve motorun hazır
    /// bildirimi (<see cref="EnterEngineReady"/>). İki okuyan, ikisi de UI thread'indeki tikte: sessizlik bekçisi
    /// (<see cref="EvaluateEngineSilence"/> — bir bekleyişte motor ne zamandır susuyor) ve boşta kuralı (<see cref="EvaluateEngineIdle"/> —
    /// motor ne zamandır işsiz). Cevabı bekleyişe sayılmayan gönderimler damgayı yazmaz: proje log isteği (cevabı beklenirken boşta kuralını
    /// <see cref="HasPendingLogLoad"/> tutar, cevabı olay olarak damgalanır) ve branch envanteri (Sync'in hemen ardından gider, cevabı
    /// damgalanır). <c>ProjectLogEvent</c> dalı UI thread'ine marshal EDİLMEDİĞİ için erişim <see cref="Volatile"/>'dir; değerlendirme (ve
    /// gözlemlenebilir alanların yazımı) yalnız tikte yapılır.</summary>
    private long _lastEngineSignalMs;

    // ---------------------------------------------------------------- gönderim

    /// <summary>Gönderimin varsayılan yolu — [idle-memory · dalga 3 · Task 21 · Fix round 1 · Go2] motor kapısı ve ardından tek gönderim:
    /// kapı "gönderme" derse (kural <see cref="EnsureEngineAwakeAsync"/>'te) satır yazılmaz ve <c>false</c> döner; gönderim düşerse
    /// (<see cref="SendWithFailureLineAsync"/>) bugünkü <c>[error] failed to send {what}: …</c> satırı yazılır ve yine <c>false</c> döner. Dönen
    /// <c>bool</c>, çağıranın gönderim BAŞARISIZ olduğunda kendi "starting" durumunu geri açabilmesi içindir — bu metot kendi başına hiçbir
    /// bound-state'e dokunmaz. Kapıyı isteği için kendisi soran çağıranlar (koşu, Sync, proje logu) gönderimi doğrudan
    /// <see cref="SendWithFailureLineAsync"/> ile yapar — istek başına TEK kapı sorusu. [round 2] Kapının geçişi gönderim boyunca tutulur
    /// (<see cref="GatePass"/>).</summary>
    private async Task<bool> TrySendAsync(IpcCommand cmd, string what)
    {
        using var pass = await EnsureEngineAwakeAsync(cmd.GetType());
        return pass.Send && await SendWithFailureLineAsync(cmd, SendFailedLine(what), pass);
    }

    /// <summary>[Fix round 1 · Go2] Kapıdan geçmiş bir isteğin TEK gönderimi: komutu gönderir (<see cref="SendToEngineAsync"/>); gönderim düşerse —
    /// motor hazır değilken (henüz başlamadı, öldü, erişilemez) <c>SendAsync</c>'in SENKRON fırlaması dahil — UI tıklaması çökmez, hata
    /// <c>{failureLine}: {hatanın metni}</c> olarak konsola TEK satır düşer, sessizce yutulmaz. Kapıyı çağıran sorar.</summary>
    /// <param name="failureLine">Düşen gönderimin satırının başı: <see cref="SendFailedLine"/> ya da isteğin kendi metni (proje logu).</param>
    /// <param name="pass">[round 2 · round 3] Kapının geçişi — ZORUNLU, varsayılanı yok: geçişi <c>using</c> ile tutup gönderime vermeyi unutan
    /// bir çağıran derlenmez, kaydı yazımdan uzun yaşayamaz. Kapıda beklemiş isteğin kaydı komut motora teslim edilmeden hemen ÖNCE biter
    /// (gerekçe <see cref="GatePass"/>'te); çağıran geçişi yine <c>using</c> ile tutar. Kapıya ayrıca sormayan ikinci parça (Sync'in branch
    /// envanteri) bunu <see cref="GatePass.None"/> ile açıkça söyler.</param>
    /// <returns>Gönderim kabul edildi mi.</returns>
    private async Task<bool> SendWithFailureLineAsync(IpcCommand cmd, string failureLine, GatePass pass)
    {
        pass.Dispose(); // teslim: bundan sonra gelen bir motor kaybı isteği gönderilmiş sayar
        var send = SendToEngineAsync(cmd);
        try { await send; return true; }
        catch (Exception ex) { AppendRunLine($"{failureLine}: {ex.Message}"); return false; }
    }

    /// <summary>Düşen bir komut gönderiminin satırının başı — <c>[error] failed to send {what}</c>; metnin TEK yeri.</summary>
    private static string SendFailedLine(string what) => $"[error] failed to send {what}";

    /// <summary>Bir komutu motora göndermenin TEK yolu — gönderimler buraya <see cref="SendWithFailureLineAsync"/> üzerinden, motor kapısından
    /// (<see cref="EnsureEngineAwakeAsync"/>) geçerek gelir: önce gözlemciye bildirir (<see cref="DebugOnCommandSent"/>), sonra
    /// gönderimi test dikişine (<see cref="DebugSendOverride"/>) ya da motora yönlendirir. Gönderimin hatası — motor hazır değilken
    /// <c>SendAsync</c>'in SENKRON fırlaması dahil — dönen görevde taşınır; çağıran onu kendi hata satırıyla karşılar ve görev
    /// zaten düşmüşse <c>await</c> askıya almadan fırlatır. Gözlemcinin kendi istisnası bir gönderim hatası DEĞİLDİR, çağırana
    /// yükselir: testin gözlemcideki bir iddiası konsol satırına dönüşüp yutulmaz.</summary>
    private Task SendToEngineAsync(IpcCommand cmd)
    {
        DebugOnCommandSent?.Invoke(cmd);
        try { return DebugSendOverride?.Invoke(cmd) ?? _engine.SendAsync(cmd); }
        catch (Exception ex) { return Task.FromException(ex); }
    }

    /// <summary>[C2 testleri] YALNIZ testler ayarlar (bkz. <see cref="DebugAfterStitchLockExited"/> deseni):
    /// bir komut gönderilmeden hemen ÖNCE senkron tetiklenir (<see cref="SendToEngineAsync"/> — proje log isteği dahil her
    /// gönderim); gönderilen <see cref="StartRunCommand"/>'ın workspace argümanlarını (Mode/RootPath/Configuration/LayerPatterns)
    /// gerçek Supervisor'a ihtiyaç duymadan gözlemlemeye yarar. Üretimde hep null — sıfır maliyet.</summary>
    internal Action<IpcCommand>? DebugOnCommandSent;

    /// <summary>[task 3 testleri] YALNIZ testler ayarlar: gönderimi motorun yerine bu yapar. Başlatılmamış bir
    /// motorla gönderim hep düşer; gerçek bir Supervisor ise kendi cevabını pencereye ASENKRON akıtıp testle
    /// yarışır. Bu seam "gönderim başarılı, cevabı test verir" yolunu motorsuz kurar. Üretimde hep null.
    /// <para>Her gönderim bu dikişten geçer (<see cref="SendToEngineAsync"/>) — proje log isteği de (<see cref="LoadProjectLogAsync"/>,
    /// kendi hata satırıyla): cevabı beklenen bir yükleme (<see cref="HasPendingLogLoad"/>) ancak böyle kurulur.</para></summary>
    internal Func<IpcCommand, Task>? DebugSendOverride;

    // ---------------------------------------------------------------- motor sessizlik watchdog'u

    /// <summary>Bir GEÇİŞ bekleniyor mu: run istendi ama <c>runStarted</c> gelmedi (<see cref="IsStarting"/>),
    /// stop istendi ama <c>runStopped</c> gelmedi (faz <see cref="AppPhase.Stopping"/>), ya da Sync istendi/
    /// koşuyor ama <c>syncCompleted</c> gelmedi (<see cref="SyncBusy"/> — istek penceresi ve
    /// <see cref="AppPhase.Syncing"/> fazının ikisi de). Watchdog YALNIZ bu bekleyiş pencerelerinde
    /// kuruludur — koşan bir run'da tek bir projenin sessizce dakikalarca derlenmesi meşrudur ve orada
    /// uyarmak kullanıcıyı sağlıklı bir build'i öldürmeye davet ederdi.
    ///
    /// <para><b>Sync neden dahil:</b> o da tam olarak bir bekleyiştir ve motor orada donduğunda şerit
    /// sonsuza dek "▸ Sync — git fetch origin…" gösteriyor, "Restart engine" kapısı HİÇ görünmüyordu — tek
    /// çıkış uygulamayı kapatmaktı. Üstelik Sync düğmesi artık uçuşta bir Sync varken kilitli olduğundan
    /// (<see cref="CanSync"/>) donmuş bir Sync'ten çıkışın TEK yolu bu kapıdır. Yanlış alarm riski yok:
    /// Sync'in git çağrıları 30 sn'de zaman aşımına uğrar (<c>GitService.CommandTimeout</c>), yani
    /// <see cref="EngineSilenceThresholdMs"/>'e takılan motor "yavaş" değil GERÇEKTEN susmuştur — ve motorun
    /// HERHANGİ bir event'i (<c>syncProgress</c> dahil) saati sıfırlar.</para></summary>
    /// <para>[clean/optimize] İki bakım işi de aynı gerekçeyle dahildir: uçuşta biri varken düğmeleri açan
    /// başka kapı yoktur (bkz. <see cref="CanClean"/>, <see cref="CanOptimize"/>), yani donmuş bir motordan
    /// çıkışın TEK yolu "Restart engine"dir. Optimize'da bu daha da keskindir: onun bir iptal komutu YOKTUR,
    /// uzun bir restore dizisinden tek kaçış budur.</para>
    private bool WaitingOnEngine =>
        IsStarting || Phase is AppPhase.Stopping or AppPhase.Syncing || WorkspaceBusy;

    /// <summary>Sessizlik saatini şimdiye alır: bekleyiş TAM BURADA başlar. Kurulmasaydı, uzun süre boşta
    /// duran bir uygulamada basılan ilk Build anında "cevap vermiyor" derdi.</summary>
    private void ArmEngineWatchdog() => StampEngineActivity();

    /// <summary>[idle-memory · dalga 3 · T20 incelemesi O1] Son motor etkinliğini ŞİMDİ'ye alır — <see cref="_lastEngineSignalMs"/>'in TEK
    /// yazımı. Üç çağıran: motorun her olayı (<see cref="OnEvent"/> — IPC thread'inden de, erişim bu yüzden <see cref="Volatile"/>'dir),
    /// motordan cevap beklenen her bekleyişin başı (<see cref="ArmEngineWatchdog"/>) ve motorun hazır bildirimi
    /// (<see cref="EnterEngineReady"/>).</summary>
    private void StampEngineActivity() => Volatile.Write(ref _lastEngineSignalMs, _nowMs());

    /// <summary>UI thread'inde, <see cref="TickElapsed"/> ile 200ms'de bir. Gözlemlenebilir alanın TEK yazıcısı
    /// burasıdır: <see cref="OnEvent"/> (arka plan thread'inde de koşabilir) yalnız zaman damgasını yazar.
    /// Bekleyiş bittiğinde ya da motor yeniden konuştuğunda uyarı KENDİLİĞİNDEN kalkar.</summary>
    private void EvaluateEngineSilence()
    {
        bool overdue = WaitingOnEngine
            && _nowMs() - Volatile.Read(ref _lastEngineSignalMs) >= EngineSilenceThresholdMs;
        EngineOverdueMessage = overdue ? EngineSilentMessage : null;
    }

    /// <summary>Şeridin amber satırı. Süre TEK kaynaktan (<see cref="EngineSilenceThresholdMs"/>) biçimlenir —
    /// eşik değişirse metin de değişir. Dil, mevcut kalıcı satırların (<see cref="EngineMissingMessage"/>)
    /// em-dash + <c>·</c> idiomuyla aynı; sakin ve kesin (design-v1 §"Ton").</summary>
    internal static string EngineSilentMessage { get; } = string.Format(CultureInfo.InvariantCulture,
        "Engine has stopped responding — no reply for {0} · you can restart it",
        DurationFormat.Elapsed(EngineSilenceThresholdMs));

    // ---------------------------------------------------------------- motor olayları: ölüm, erişilemezlik, hazır

    /// <summary>[Task 16 — It-2 devir §8, kama düzeltmesi] <see cref="EngineHost.EngineExited"/> eskiden VM'e
    /// hiç BAĞLI DEĞİLDİ: engine process startRun sonrası runStarted'dan ÖNCE ya da run ORTASINDA ölürse,
    /// hiçbir IPC event'i asla gelmeyeceğinden (ne runCompleted ne runStopped ne run-bitiren ErrorEvent)
    /// IsStarting/IsRunning SONSUZA DEK kilitli kalırdı — "Restart Engine" MainWindow'daki
    /// banner'ı güncelliyordu ama VM'e hiç dokunmadığından butonlar açılmıyordu. <see cref="RunEndingErrorCodes"/>
    /// deseniyle TUTARLI: aynı run-state alanları sıfırlanır; ayrıca bir sonraki run/Restart'ın
    /// <c>_currentRunId</c> bakiyesiyle karışmaması için o bakiye de temizlenir (StopAsync zaten CanStop=false
    /// olduğundan tıklanamaz, ama temiz başlangıç için bilerek sıfırlanır).
    ///
    /// <para><b>Idempotent:</b> [ObservableProperty] setter'ları CommunityToolkit'in eşitlik kontrolüyle
    /// çalışır (false→false / null→null hiçbir PropertyChanged/CanExecuteChanged YAYINLAMAZ) — bu yüzden
    /// hiçbir run aktif değilken (zaten temiz durum) ya da normal <c>runCompleted</c> SONRASI çağrılırsa
    /// no-op'tur, ayrı bir guard GEREKMEZ.</para>
    ///
    /// <para><b>Thread/marshal:</b> <see cref="EngineHost.EngineExited"/> arka plan thread'inde (exit-watcher
    /// ya da framing-hatası dalı) ateşlenir; bu metot ObservableProperty/CanExecuteChanged'a dokunduğundan
    /// <see cref="OnEvent"/>'in Dispatcher-gerektiren dalları GİBİ UI thread'ine marshal edilerek çağrılmalıdır
    /// — çağıran (MainWindow) bu sorumluluğu taşır, VM'in kendisi Dispatcher TÜRÜ TAŞIMAZ (test edilebilirlik).</para></summary>
    public void OnEngineExited(int? exitCode) =>
        // Exit kodu (varsa) KORUNUR — test bu sayıyı pinler.
        ReportEngineDead(exitCode is { } code ? $"exit {code}" : "protocol error");

    /// <summary>Motor GİTTİ — beklenmedik ölümle (<see cref="OnEngineExited"/>) ya da kullanıcının kasıtlı
    /// yeniden başlatmasıyla (<see cref="RestartEngineAsync"/>). Her iki yolda da uçuştaki run/Sync pencereleri
    /// serbest bırakılır: o motorun asla göndermeyeceği event'leri bekleyen hiçbir durum kalmamalıdır. [Task 21 · karar 34] Uyku döngüsündeki
    /// bir kayıpta ve ölü motorun ikinci bildiriminde çağrılmaz (gerekçe <see cref="ReportEngineDead"/>'de).
    ///
    /// <para><b>[T21 OOS-1 · karar 34 · round 2] Kapıdan geçişini tutan isteklerin pencerelerine dokunmaz</b> (<see cref="_waitingAtGate"/>,
    /// <see cref="GatePass"/>) — koşu, Sync, Clean, Optimize, checkout, pull: istek kapıda bekledi ve motora henüz teslim edilmedi; kendi "gönderim
    /// düştü" geri alımını çalıştırır (ya da ölü motora gönderimi bugünkü satırıyla düşer). Bırakılsaydı hiç başlamamış bir işlem bitmiş sayılırdı
    /// (Sync'in <c>EndSync</c>'i, Clean/Optimize'ın işlem sonu) ya da koşunun açılışı kendi geri alımından önce sıfırlanırdı. Bu kayıp uyku döngüsünün
    /// dışında da gelebilir: uyanma başarılı olur, motor hemen ölür ve isteğin devamı — kapıdaki ya da kapının "gönder" kararından sonraki — henüz
    /// koşmamıştır. Proje logunun cevap beklentisi yine bırakılır: geçişini tutan bir yüklemenin geri alımı aynısını yapar ve kapıda kalmış eski bir
    /// yükleme, gönderilmiş yenisinin beklentisini tutmamalıdır.</para>
    ///
    /// <para><b>Neden iki çağıran:</b> <see cref="EngineHost.RestartAsync"/> önce <c>_generation</c>'ı artırır
    /// (eski exit-watcher susturulur) ve ancak sonra child'ı öldürür — yani YAŞAYAN bir motoru yeniden
    /// başlatmak <c>EngineExited</c> ATEŞLEMEZ. Serbest bırakma yalnız ölüm yolunda kalsaydı, donmuş (ama
    /// yaşayan) bir motorda Restart'a basmak şeridi temizler, kilidi AÇMAZDI — kapının varlık sebebi de
    /// tam olarak o kilidi açmaktır.</para>
    ///
    /// <para><b>Idempotent:</b> [ObservableProperty] setter'ları eşitlik kontrolüyle çalışır, yani hiçbir run
    /// aktif değilken çağrılırsa no-op'tur.</para>
    ///
    /// <para><b>[idle-memory] Proje log yüklemesi bırakılmaz, cevabı artık beklenmez</b> (<see cref="StopAwaitingReply"/>): gönderimi
    /// düşen yükleme gibi silahlı kalır — motorun ölmeden yazdığı gecikmiş bir chunk hâlâ dikilir — ama işlem sonu trimini tutmaz.
    /// Tutsaydı başka bir log açılana dek her trimi (yeniden başlatmanın Sync'i dahil) kilitlerdi. Silahlı kalış en geç yeni işleme
    /// dektir: <see cref="ClearConsoleForNewOperation"/> onu bitirir (awaiter serbest kalır, tampon bırakılır).</para></summary>
    private void ReleaseAfterEngineLoss()
    {
        // [T21 OOS-1 · karar 34 · round 2] Kapıdan geçişini tutan (teslim edilmemiş) isteğin penceresi BIRAKILMAZ — geri alımı kendisinindir (gerekçe özette).
        if (!WaitsAtTheGate(typeof(StartRunCommand)))
        {
            // [E2/F3 fold] Terminal Phase kararı: engine run ORTASINDA (Phase=Running) ölürse Phase Running'de asılı
            // kalıp IsRunning=false ile çelişik bir resting state bırakıyordu. Şerit kalıcı-hata modu EngineDiedMessage
            // != null ÖNCELİĞİYLE Phase'i YOK SAYAR (bkz. RibbonText.Compose), yani terminal Phase seçimi KOZMETİKtir;
            // yine de tutarlılık için Running → Stopped'a çekilir. YALNIZ Running'e dokunulur: Idle/Boot/Empty gibi
            // resting fazlar (engine repo seçili değilken de ölebilir) Stopped'a çekilmez — yanıltıcı olurdu.
            // [Stopping] Stop penceresi de aynı gerekçeye girer: motor öldüyse drain'i bitirecek kimse kalmadı,
            // faz Stopping'de asılı bırakılamaz. Kullanıcı zaten durmayı istemişti — terminal faz Stopped'tır.
            if (Phase is AppPhase.Running or AppPhase.Stopping) Phase = AppPhase.Stopped;
            // [planlama görünürlüğü] Starting AYRI: orada hiçbir proje derlenmedi, "▸ Stopped — 0/0 · 0 not built"
            // olmayan bir koşuyu anlatırdı. Dinlenme fazı dürüst tabandır (şerit zaten engine-died önceliğiyle
            // kırmızı metni gösterir; bu, o metin temizlendikten SONRA görülecek durumdur).
            else if (Phase == AppPhase.Starting) Phase = RestingPhase;
            _awaitingRunCompleted = false; // motor gitti — runCompleted gelmeyecek
            IsRunning = false;
            IsStarting = false;
            _currentRunId = null;
            MarkRunEnded(); // [perf Faz C · C4] motor gitti: koşunun bitişi burasıdır (sinyal)
        }
        // [C2 fold — A5 review] Engine Sync ortasında ölürse hiçbir syncCompleted/Sync-hatası gelmez; faz
        // Syncing'de asılı kalır ve _syncInFlight sızardı. RunEndingErrorCodes deseniyle simetrik olarak burada
        // da uçuştaki Sync serbest bırakılır. [EndedOperationSerial] Bu yol boştaki motorda da koşar (Restart): bitişi
        // yalnız uçuşta bir Sync varsa bildirilir (EndSync), yoksa yalnız bırakılır — bayraklar silinmeden ÖNCE okunur.
        if (!WaitsAtTheGate(typeof(SyncWorkspaceCommand)))
        {
            if (SyncBusy) EndSync();
            else ReleaseSyncPhase();
        }
        // [clean] Aynı gerekçe: motor Clean ORTASINDA ölürse hiçbir cleanCompleted/clean-hatası gelmez ve
        // bayrak sızarsa yeniden başlatılan motorda da düğmeler kilitli kalırdı.
        if (!WaitsAtTheGate(typeof(CleanWorkspaceCommand))) ReleaseCleanSurface();
        if (!WaitsAtTheGate(typeof(OptimizeWorkspaceCommand))) ReleaseOptimizeSurface();
        // [§6.3] motor checkout ortasında öldüyse cevap gelmez — chip kilidi sızmaz
        if (!WaitsAtTheGate(typeof(CheckoutBranchCommand))) SetCheckoutBusy(false);
        // [§6.1] motor pull ortasında öldüyse pullCompleted gelmez — kapı sızmaz
        if (!WaitsAtTheGate(typeof(PullRepositoryCommand))) SetPullBusy(false);
        if (_pendingLoad is { } pendingLoad) StopAwaitingReply(pendingLoad); // [idle-memory] log isteğinin cevabı gelmez — gerekçe özette
        // Beklenen geçiş kalmadı → sessizlik uyarısının konusu da kalmadı. (Tick zaten aynı sonuca varırdı;
        // burada YAZILMASININ sebebi, kullanıcının Restart'a bastığı KAREde amber satırın kalkmasıdır.)
        EngineOverdueMessage = null;
    }

    /// <summary>
    /// [D1] Motor HİÇ başlatılamadı: Supervisor çalıştırılabiliri uygulamanın yanında yok (eksik/bozuk kurulum —
    /// tipik olarak publish çıktısına <c>supervisor\</c> klasörü girmemiş). Kullanıcı SESSİZ kalmaz: şerit
    /// kalıcı hata moduna girer (engine-died ile AYNI görsel yol) ama "Restart engine" GİZLENİR — yeniden
    /// başlatmak eksik dosyayı geri getirmez. Tam yol yalnız konsol anlatısına düşer (şerit tek satır kalır).
    /// <para><b>Tek sinyal:</b> child process hiç doğmadığı için <see cref="EngineHost.EngineExited"/> ateşlenmez;
    /// bu yol <see cref="OnEngineExited"/> ile ASLA çakışmaz (çağıran yalnız
    /// <see cref="Services.EngineUnavailableException"/> dalında buraya girer).</para>
    /// </summary>
    /// <param name="exePath">Aranan Supervisor exe yolu (konsol satırında gösterilir).</param>
    /// <param name="reason">[D1 review · A2] Dosya yok mu, yoksa var ama başlatılamadı mı — şerit metnini ayırır.</param>
    public void OnEngineUnavailable(string exePath,
        Services.EngineUnavailableReason reason = Services.EngineUnavailableReason.NotFound)
    {
        EngineState = EngineState.Unavailable; // [idle-memory · dalga 3] IsEngineUnavailable ile aynı an
        EngineRestartable = false;
        EngineDiedMessage = reason == Services.EngineUnavailableReason.NotFound
            ? EngineMissingMessage
            : EngineCannotStartMessage;
        // Konsola tam yol + kültürden bağımsız bir tanı kodu; OS'in (yerelleştirilmiş) Win32 metni ASLA
        // gösterilmez — uygulama İngilizce-only [A3].
        AppendRunLine(reason == Services.EngineUnavailableReason.NotFound
            ? $"[error] engine not found: {exePath}"
            : $"[error] engine could not start: {exePath}");
    }

    /// <summary>[About] Motorun bildirdiği sürüm — About'un Environment sekmesi bunu gösterir. Motor
    /// doğmadan önce <c>null</c>'dır.</summary>
    public string? EngineVersion { get; private set; }

    /// <summary>[About] Motor process'inin PID'i (<c>EngineReadyEvent.Pid</c>). Bu değer olaydan okunup
    /// ATILIYORDU; artık tanı raporunda görünür.</summary>
    public int? EnginePid { get; private set; }

    /// <summary>[D1 review · C5] Motor hazır: konsolun boot satırında sürüm gösterilir (design-v1 §2.5 anlatı
    /// dili — "Build started — 14 projects, parallelism 4" ile aynı kalıp). Sürüm kimliği TEK kaynaktan gelir:
    /// <c>Directory.Build.props</c> → Supervisor assembly'sinin InformationalVersion'ı → <c>engineReady</c>.
    /// <para>[About] Sürüm ve PID ayrıca SAKLANIR (Environment sekmesi okur); boot satırı DEĞİŞMEDİ.</para>
    /// <para>[spec 2026-09-18 §6.2 "Uygulama açılışı"] Motor hazır olduğunda, bir workspace varsa ve Sync'e izin
    /// varsa, fetch'li bir Sync başlar (<see cref="SyncMode.Appended"/>: boot satırları kalır, transkript altına akar).
    /// İki çağıran: kabuğun ilk açılışı (<c>MainWindow.StartEngineAsync</c>) ve <see cref="RestartEngineAsync"/>.
    /// Yalnız eski havuz ipucu oturum başına BİR kez yazılır (<see cref="_engineWasReady"/>). [idle-memory · dalga 3 · Task 21] Uyuyan motorun
    /// uyanması bu yolu KULLANMAZ: yalnız ortak çekirdeği alır (<see cref="OnEngineResumed"/> — satır yok, açılış Sync'i yok).</para>
    /// <para><b>[DEĞİŞEN KURAL — final review I1]</b> Eskiden Sync yalnız ilk hazır oluşta giderdi ("restart dünyayı
    /// değiştirmez"). Oysa çökmeden sonra yeniden başlayan motor uçuştaki projeleri kurtarır (spec §5.5): ekranın
    /// kararları artık yanlıştır ve kurtarılan satırlar ancak bir Sync'le gri "never built" okunur.</para>
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §6.2]</b> Eskiden açılış "seed-but-idle"dı (kayıtlı repo bilinir
    /// ama Sync kullanıcıya kalır — <c>MainWindow</c> kök seed'i). Seed'in kendisi hâlâ komut göndermez
    /// (<c>RunViewModelStateTests.Seeding_the_root_path_directly_lands_in_boot_without_starting_a_sync</c>);
    /// açılışın Sync'i motor hazır olunca buradan gider.</para></summary>
    public void OnEngineReady(string engineVersion, int pid)
    {
        // [idle-memory · dalga 3] Kimlik + motor durumu + son motor etkinliği — uyanmayla (OnEngineResumed) ORTAK çekirdek; aşağıdakiler
        // (hazır satırı, eski havuz ipucu, açılış Sync'i) yalnız açılışın ve yeniden başlatmanındır.
        EnterEngineReady(engineVersion, pid);
        AppendRunLine($"Engine ready — v{engineVersion}");
        if (!_engineWasReady)
        {
            _engineWasReady = true;
            string? legacyPoolHint = LegacyWorktreePool.Hint(LegacyWorktreePoolRoot);
            if (legacyPoolHint is not null) AppendRunLine(legacyPoolHint);
        }
        if (HasWorkspace && CanSync()) _ = SyncCoreAsync(SyncMode.Appended);
    }

    /// <summary>[Task 11 · test injection] Eski worktree havuzunun kökü — üretimde
    /// <see cref="LegacyWorktreePool.DefaultRoot"/>, testlerde gerçek %LOCALAPPDATA%'a bakmasın diye
    /// override edilir.</summary>
    internal string LegacyWorktreePoolRoot { get; set; } = LegacyWorktreePool.DefaultRoot;

    /// <summary>[spec 2026-09-18 §5.5 · karar 12] Motor açılışta kesilmiş bir koşu kurtardı: konsola kaç projenin
    /// yeniden derleneceği yazılır (metin Core'daki tek kaynaktan). Olay akışından (<see cref="OnEvent"/>) gelir,
    /// <see cref="OnEngineReady"/>'nin çağıranından DEĞİL: <c>engineReady</c> hem ilk açılışta hem
    /// <see cref="RestartEngineAsync"/>'te <c>EventReceived</c>'dan geçer — çökmeden sonra yeniden başlatılan motor
    /// tam da kurtarmanın gerektiği durumdur, ve tek dal iki yolu birden kapsar.</summary>
    private void OnEngineRecovered(int interruptedProjects)
    {
        if (interruptedProjects > 0) AppendRunLine(PlanProgressLines.PreviousRunInterrupted(interruptedProjects));
    }

    /// <summary>Motor bu oturumda en az bir kez hazır oldu mu — eski havuz ipucu yalnız ilk hazır oluşta yazılır.</summary>
    private bool _engineWasReady;

    // ---------------------------------------------------------------- Restart engine

    /// <summary>[E2/T37] Şeridin kalıcı hata modundaki "Restart engine" aksiyonu: ölmüş engine process'ini yeniden
    /// başlatır (<see cref="EngineHost.RestartAsync"/>). Başarılıysa <see cref="EngineDiedMessage"/> temizlenir
    /// (engine geri geldi — bir sonraki runStarted'ı beklemeden, çünkü Restart tek başına da engine sağlığını
    /// kanıtlar). MainWindow'un <c>_engine.EventReceived</c>/<c>EngineExited</c> abonelikleri AYNI EngineHost
    /// instance'ında kaldığından yeniden kablolama gerekmez. Gönderim başarısız olursa gerekçe konsola düşer ve
    /// hata modu KALIR (kullanıcı tekrar deneyebilir).
    /// <para>[final review I-2] <see cref="Services.EngineUnavailableException"/> AYRI yakalanır: yeniden
    /// başlatma preflight'ta (dosya yok / başlatılamıyor) düşerse bu bir "tekrar dene" hatası DEĞİLDİR —
    /// <see cref="OnEngineUnavailable"/> ile D1'in "motor erişilemez" durumuna geçilir (aksiyon gizlenir,
    /// komutlar kapanır, şeritte TEK ve doğru mesaj kalır). Aksi halde generic catch bu türü ayırt etmediği
    /// için <see cref="EngineRestartable"/> true kalır ve <see cref="EngineDiedMessage"/> eski "unexpectedly
    /// stopped" metniyle donar: kullanıcıya sonsuza dek "Restart engine" sunulur, komutlar açık kalır ve her
    /// tıklama şeritteki mesajla ÇELİŞEN ikinci bir hata satırı üretir — <see cref="EngineRestartable"/>'ın
    /// değişmezi de ("EngineDiedMessage'ı yazan HER yol bunu da yazar") bozulurdu.</para>
    /// <para>[final review I1] Başarılı yeniden başlatma ilk açılışla AYNI hazır yolundan geçer
    /// (<see cref="OnEngineReady"/>): "Engine ready" satırı, PID/sürüm ve — bir workspace açıkken — tek bir Appended
    /// Sync. Sıra ZORUNLUdur: önce eski motorun pencereleri bırakılır (<see cref="ReleaseAfterEngineLoss"/>), SONRA
    /// hazır yolu — ters sırada bırakma yeni Sync'in istek bayrağını da silerdi.</para></summary>
    [RelayCommand]
    private async Task RestartEngineAsync()
    {
        // Eski process (ve tüm MSBuild child'ları) her koşulda gitti — o motorun asla göndermeyeceği event'leri
        // bekleyen hiçbir durum kalmamalı (ReleaseAfterEngineLoss). Yeni motor başlatılamadıysa da geçerlidir: orada
        // da bekleyecek bir şey yoktur (bkz. OnEngineUnavailable, komutlar zaten kapanır).
        // [idle-memory · dalga 3 · karar 13] Yeniden başlatma bir başlatmadır: yeni motor hazır olana dek durum Starting — boşta kuralı
        // uyutmaz. Başarı hazır yolundan Ready'ye, kurulum hatası Unavailable'a gider. Başka bir hatada durum burada ayrıca yazılmaz: yeni
        // süreç hazır olamadan öldürüldüyse ya da öldüyse (zaman aşımı, erken ölüm — en sık hata) ölümünü EngineExited TEK sinyal olarak
        // bildirir (Dead). [T20 incelemesi M3 · ikinci bilinen ayrışma] Ölüm sinyali gelmeyen iki hata Starting'i KALICI bırakır: çıkışta
        // dispose'un öne geçmesi (uygulama kapanıyor, ölen yok) ve eski motorun öldürülmesinin fırlatması (konakta konuşulacak süreç kalmaz).
        // Catch bu ikisini zaman aşımından ayıramaz: burada Dead yazmak en sık hatanın ölümünü EngineExited'in yanında İKİNCİ kez bildirirdi.
        // Kalıcı Starting güvenli yöndedir: kural uyutmaz, motor kapısı Starting'te ne uyandırır ne bekler — gönderim bugünkü gibi gider ve
        // konağın "Engine is not running." hatasıyla tek satır düşer; ölüm metni duruyorsa "Restart engine" görünür kalır (dalga öncesi davranış).
        EngineState = EngineState.Starting;
        EngineReadyEvent ready;
        try
        {
            ready = await _engine.RestartAsync();
            EngineDiedMessage = null;
        }
        catch (Services.EngineUnavailableException ex)
        {
            OnEngineUnavailable(ex.ExePath, ex.Reason); // [final review I-2] D1'in "engine yok" durumu
            ReleaseAfterEngineLoss();
            return;
        }
        catch (Exception ex)
        {
            AppendRunLine($"[error] engine restart failed: {ex.Message}");
            ReleaseAfterEngineLoss();
            return;
        }
        ReleaseAfterEngineLoss();
        OnEngineReady(ready.EngineVersion, ready.Pid);
    }
}
