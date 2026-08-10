using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D3/T?] <see cref="RunViewModel"/>'in event-stream yüzeyi (ayrı partial — Workspace.cs deseni). IPC
/// event'lerinden (UI-thread <see cref="RunViewModel.OnEvent"/> dalı — marshal-free ProjectLogEvent hot-path'e
/// DOKUNULMAZ) tampon anlatı satırları + canlı aktif satır türetir. Karar mantığı SAF çekirdekte
/// (<see cref="StreamComposer"/>/<see cref="StreamText"/>); burada yalnız kablaj + gözlemlenebilir yüzey.
/// </summary>
public sealed partial class RunViewModel
{
    private readonly StreamComposer _stream = new();
    // BuildApp.jsx:677 — ilk newest satır daktilo ETMEZ (prevNewest==null); sonrakiler (fırtına/hata değilse) eder.
    private bool _streamHadNewest;
    // [D3 §2] "Build started"/"Continue" anlatı satırı RunStarted'dan BuildPreview'a ERTELENİR — will-build sayısı
    // (RunStartedEvent.TotalProjects DEĞİL, o skip'leri de sayar) ancak BuildPreview işlendikten SONRA hazırdır.
    // RunStarted mode'u burada tutulur; BuildPreview satırı yayıp bunu TEMİZLER (Continue re-emit'te çift satır olmaz).
    private RunMode? _pendingRunStartMode;

    // [Task 2/cycles] _pendingRunStartMode BuildPreview'da TEMİZLENİYOR (yukarıdaki alan) — kapsam-dışı
    // toplayıcı ise RunCompleted'a kadar (koşunun SONUNA kadar) hangi modda olduğumuzu bilmek zorunda,
    // bu yüzden AYRI bir alan: RunStartedEvent'te set edilir, EndRun'da sıfırlanmaz.
    private RunMode? _streamRunMode;

    // [Task 2/cycles] Cycles koşusunda SkipReasons.OutOfCycleScope gerekçeli skip'ler burada BİRİKİR (satır
    // YAZILMAZ) — PushStream'in başında tek Info satırına flush edilir (bkz. PushStream).
    private int _outOfScopeSkips;

    // [Task 4] Cycle round ilerleme takibi — aktif satırdaki "member i/N · round r/cap" detayının kaynağı.
    // _cycleRoundCap == 0 ⇒ bu run'da henüz bir CycleRoundStartedEvent gelmedi (round AKTİF DEĞİL) — upstream/
    // prerequisite projeler bu run'ın ilk aşamasında builds eder ve detay almaz. RunStarted/RunCompleted'ta
    // sıfırlanır (bir sonraki run/segment temiz başlasın).
    private int _cycleRound;
    private int _cycleRoundCap;
    private int _cycleRoundMemberCount;
    private int _cycleMemberIndex;

    /// <summary>[D8] Duvar-saati zaman damgası kaynağı (stream satırı "HH:mm:ss") — testte deterministik enjekte
    /// edilebilir; üretimde <see cref="DateTimeOffset.Now"/>. Fırtına/elapsed saati (<c>_nowMs</c>) AYRIDIR
    /// (monoton ms; duvar-saati DEĞİL).</summary>
    internal Func<DateTimeOffset> WallClock { get; set; } = () => DateTimeOffset.Now;

    /// <summary>Görünen dilim (≤150) — <see cref="Views.EventStreamView"/> bunu bağlar. Tam tampon sayacı
    /// <see cref="StreamEventCount"/> AYRIDIR (Ek A #23 ile aynı ilke).</summary>
    public ObservableCollection<StreamEventViewModel> StreamEvents { get; } = [];

    /// <summary>"{n} events" sayacı — TAM tampon (≤260), render dilimi DEĞİL.</summary>
    [ObservableProperty] private int _streamEventCount;

    /// <summary>Aktif satırın projesi (tıklama → <see cref="SelectProject"/>); hiç building yoksa null.</summary>
    [ObservableProperty] private string? _activeLineProjectId;
    /// <summary>Aktif satır metni "<c>{name} building…</c>" ya da null (satır gizli). [Task 4] Proje bir cycle
    /// round üyesiyse (round aktifken) sona "<c>· member {i}/{N} · round {r}/{cap}</c>" eki eklenir — bkz.
    /// <see cref="StreamText.CycleMemberDetail"/>; upstream/prerequisite projelerde ek YOKTUR.</summary>
    [ObservableProperty] private string? _activeLineText;
    /// <summary>Aktif proje her DEĞİŞTİĞİNDE artar — görünüm daktiloyu yeniden başlatır (prototip activeLine.id).</summary>
    [ObservableProperty] private long _activeLineGeneration;

    /// <summary>[OnEvent kablajı] Bir UI-thread IPC event'inden stream tampon satırı + aktif satır türetir.
    /// <see cref="OnEvent"/>'in SONUNDA çağrılır — proje satırları/sayaçlar (Counters) o an zaten güncellenmiştir,
    /// bu yüzden ad çözümü ve done-glyph'in yeşil/kırmızı kararı (Counters.Failed) doğru okunur.</summary>
    private void AppendStreamFor(IpcEvent ev)
    {
        // [D3 §4] Marshal-free ProjectLogEvent (saniyede binlerce) + ProjectLogChunkEvent akışı stream'e HİÇBİR
        // satır katmaz — 7-yollu type-switch'i boşuna koşturup no-op'a düşme; switch'ten ÖNCE erken dön.
        if (ev is ProjectLogEvent or ProjectLogChunkEvent) return;

        switch (ev)
        {
            case RunStartedEvent e:
                _stream.EndRun(); // yeni koşu/segment: aktif + building sıfırlanır (tampon sayacı KORUNUR)
                SyncActiveLine();
                // [D3 §2] "Build started"/"Continue" satırını BuildPreviewEvent'e ERTELE — will-build sayısı orada
                // hazır (BuildPreview deterministik olarak RunStarted'ı hemen izler, RunCoordinator.cs:456). Burada
                // YAYMA; yalnız mode'u işaretle.
                _pendingRunStartMode = e.Mode;
                _streamRunMode = e.Mode;
                // [Task 4] Yeni run/segment: önceki koşunun round ilerlemesi bu run'ı ETKİLEMEZ.
                (_cycleRound, _cycleRoundCap, _cycleRoundMemberCount, _cycleMemberIndex) = (0, 0, 0, 0);
                break;

            case BuildPreviewEvent:
                // [D3 §2] Ertelenen run-start satırını burada yay — OnBuildPreview (OnEvent'te BUNDAN ÖNCE) hem
                // _willBuildIds'i doldurdu hem RefreshRunSurface ile FinishedOfWillBuild'i tazeledi. Build → will-build
                // sayısı; Continue → kalan (prototip build-data.js:327 `remain = willBuild.size - finishedWB`). Pending'i
                // TEMİZLE ki Continue segmentlerinde re-emit edilen BuildPreview çift satır yaymasın.
                if (_pendingRunStartMode is { } mode)
                {
                    int parallelism = _runParallelism ?? Parallelism;
                    PushStream(StreamKind.Info, null, mode switch
                    {
                        RunMode.Continue => StreamText.Continue(_willBuildIds.Count - FinishedOfWillBuild, parallelism),
                        // [cycles/Task 4] Bu koşu bir build DEĞİLDİR ve paralellik onu tarif etmez: bir SCC'nin
                        // üyeleri sıralı derlenir. Kırılım will-build ∩ üyelik'ten (_cycleGroups.IsMember) — kalan
                        // upstream/prerequisite'tir; kullanıcı "neden bu kadar proje derleniyor"u burada okur.
                        RunMode.Cycles => StreamText.CyclesStarted(
                            members: _willBuildIds.Count(id => _cycleGroups?.IsMember(id) == true),
                            prerequisites: _willBuildIds.Count(id => _cycleGroups?.IsMember(id) != true)),
                        _ => StreamText.BuildStarted(_willBuildIds.Count, parallelism),
                    });
                    _pendingRunStartMode = null;
                }
                break;

            case ProjectStartedEvent e:
                // [Task 4] Bu proje bir cycle round üyesiyse (round AKTİFKEN — upstream projeler round
                // başlamadan ÖNCE build eder, bkz. _cycleRoundCap) sayaç ilerler ve aktif satır "member i/N ·
                // round r/cap" detayını taşır; değilse (upstream/prerequisite) detay YOK — düz "{name} building…".
                string? detail = null;
                if (_cycleRoundCap > 0 && _cycleGroups?.IsMember(e.ProjectId) == true)
                {
                    _cycleMemberIndex++;
                    detail = StreamText.CycleMemberDetail(_cycleMemberIndex, _cycleRoundMemberCount, _cycleRound, _cycleRoundCap);
                }
                _stream.StartBuilding(e.ProjectId, e.Name, _nowMs(), detail); // building → yalnız aktif satırda görünür (tampon satırı YOK)
                SyncActiveLine();
                break;

            case ProjectSucceededEvent e:
                PushStream(StreamKind.Ok, e.ProjectId,
                    e.DepIssues is { Count: > 0 }
                        ? StreamText.BuiltDependencyIssue(ResolveName(e.ProjectId), e.DurationMs)
                        : StreamText.Built(ResolveName(e.ProjectId), e.DurationMs));
                _stream.FinishBuilding(e.ProjectId, _nowMs());
                SyncActiveLine();
                break;

            case ProjectFailedEvent e:
                PushStream(StreamKind.Fail, e.ProjectId, StreamText.Failed(ResolveName(e.ProjectId), e.Reason, e.DurationMs));
                _stream.FinishBuilding(e.ProjectId, _nowMs());
                SyncActiveLine();
                break;

            case ProjectSkippedEvent e:
                // [Task 2/cycles] Kapsam-dışı skip proje başına satır YAZMAZ — sayaç birikir, sonraki
                // PushStream'in başında tek toplu satıra flush edilir (ör. bir sonraki skip/built/completed).
                if (_streamRunMode == RunMode.Cycles && e.Reason == SkipReasons.OutOfCycleScope)
                {
                    _outOfScopeSkips++;
                    break;
                }
                PushStream(StreamKind.Skip, e.ProjectId, StreamText.Skipped(ResolveName(e.ProjectId), e.Reason));
                break;

            // [cycle rounds/Task 8] Bir SCC'nin turu başladı — grubun tek ilerleme sinyali. ProjectId LİDERİN
            // id'sidir (satır ona bağlı/tıklanabilir, ok/fail/skip satırlarıyla AYNI desen). Kind=Info: ne
            // başarı ne hata, BuildStarted/Continue satırlarıyla AYNI amber ▸ anlatı tonu.
            case CycleRoundStartedEvent e:
                // [Task 4] Yeni turun ilerleme takibi kurulur — sayaç 0'dan başlar, grubun İLK ProjectStartedEvent'i
                // (round-order'daki ilk üye) onu 1'e taşır.
                (_cycleRound, _cycleRoundCap, _cycleRoundMemberCount, _cycleMemberIndex) = (e.Round, e.RoundCap, e.MemberCount, 0);
                PushStream(StreamKind.Info, e.ProjectId, StreamText.CycleRound(e.Round, e.RoundCap, e.MemberCount));
                break;

            // [Task 3/cycles] Grubun NİHAİ kararı — decision.log'un ekrandaki karşılığı. ProjectId LİDERİN
            // id'sidir (CycleRoundStartedEvent ile AYNI desen) — satır tıklanabilir kalır. Kind outcome'a göre
            // değişir: Converged yeşil, NoProgress kırmızı, CapReached amber/info (bilgi, hata DEĞİL).
            case CycleCompletedEvent e:
                PushStream(e.Outcome switch
                {
                    CycleOutcome.Converged => StreamKind.Ok,
                    CycleOutcome.NoProgress => StreamKind.Fail,
                    _ => StreamKind.Info, // CapReached
                }, e.ProjectId, StreamText.CycleCompleted(e.Outcome, e.MemberCount, e.Rounds, e.FailedCount, e.DurationMs));
                break;

            case SyncCompletedEvent e:
                PushStream(StreamKind.Sync, null, StreamText.Sync(e.ToBuildCount, e.UpToDateCount));
                break;

            case RunCompletedEvent e:
                if (e.Outcome == RunOutcome.Stopped)
                    PushStream(StreamKind.Info, null, StreamText.Stopped(e.Queued)); // stopped → info (parıltı YOK)
                else
                {
                    PushStream(StreamKind.Done, null,
                        StreamText.Completed(e.Failed, e.Succeeded, e.Skipped, e.DepIssueCount, e.DurationMs));
                    // [Task 6] Bu dal yalnız e.Outcome != Stopped iken koşar (yukarıdaki if'in AKSİ) — Cycles
                    // koşusunun KENDİSİ bu satırı yaymaz (zaten o modda, ipucu anlamsız). Sayaç Projects'ten
                    // OKUNUR: WillBuild bir Cycles koşusuyla temizlenmediği sürece (döngü üyesi normal Build'de
                    // pre-skip edilir, üye asla invoke edilmez) InCycle&&WillBuild==true satırlar "hâlâ kirli
                    // döngü üyesi" demektir.
                    if (_streamRunMode != RunMode.Cycles)
                    {
                        int n = Projects.Count(p => p.InCycle && p.WillBuild == true);
                        if (n > 0) PushStream(StreamKind.Info, null, StreamText.CyclesHint(n));
                    }
                }
                _stream.EndRun();
                SyncActiveLine();
                // [Task 4] Koşu bitti — round ilerleme takibi bir sonraki run için sıfırlanır.
                (_cycleRound, _cycleRoundCap, _cycleRoundMemberCount, _cycleMemberIndex) = (0, 0, 0, 0);
                break;
        }
    }

    private void PushStream(StreamKind kind, string? projectId, string text)
    {
        // [Task 2/cycles] Toplu kapsam-dışı satırı BURADA flush et — sayaç ÖNCE sıfırlanır (recursion guard'ı:
        // aşağıdaki yinelenen PushStream çağrısı 0 görüp tekrar girmez). RunCompletedEvent her koşuda gelir,
        // bu yüzden sayaç asla asılı kalmaz.
        if (_outOfScopeSkips > 0)
        {
            int n = _outOfScopeSkips;
            _outOfScopeSkips = 0;
            PushStream(StreamKind.Info, null, StreamText.OutsideCycleScope(n));
        }

        bool anyFailed = Counters.Failed > 0; // done glyph/renk yeşil↔kırmızı (prototip c.failed)
        var emission = _stream.Push(isFail: kind == StreamKind.Fail, _nowMs());
        string time = Console.WallClockFormat.Of(WallClock());
        bool shouldType = _streamHadNewest && !emission.Instant; // ilk-satır type etmez (prevNewest==null)
        _streamHadNewest = true;
        bool isSelected = projectId is not null &&
            string.Equals(projectId, SelectedProjectId, StringComparison.OrdinalIgnoreCase);

        StreamEvents.Add(new StreamEventViewModel(emission, time, kind, projectId, text, anyFailed, shouldType, isSelected));
        while (StreamEvents.Count > StreamComposer.RenderSlice) StreamEvents.RemoveAt(0); // render dilimi 150 (front-trim)
        StreamEventCount = _stream.Count; // "{n} events" = tam tampon (≤260)
    }

    private void SyncActiveLine()
    {
        // Metni generation'DAN ÖNCE yaz — görünüm generation değişimini izleyip taze metinle daktilo koşar.
        ActiveLineProjectId = _stream.ActiveProjectId;
        ActiveLineText = _stream.ActiveText;
        ActiveLineGeneration = _stream.ActiveGeneration;
    }

    private string ResolveName(string projectId) =>
        Projects.FirstOrDefault(p => string.Equals(p.Id, projectId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? Path.GetFileNameWithoutExtension(projectId);

    /// <summary>[Selection deseni] Seçim değişince her stream satırının <see cref="StreamEventViewModel.IsSelected"/>'ını
    /// tazeler (ProjectRow/Projects akışının eşi — TEK seçim kaynağı <see cref="SelectedProjectId"/>).</summary>
    private void PropagateSelectionToStream(string? value)
    {
        foreach (var s in StreamEvents)
            s.IsSelected = s.ProjectId is not null &&
                string.Equals(s.ProjectId, value, StringComparison.OrdinalIgnoreCase);
    }
}
