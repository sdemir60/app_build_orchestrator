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
    /// <summary>Aktif satır metni "<c>{name} building…</c>" ya da null (satır gizli).</summary>
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
                        // [cycles] Bu koşu bir build DEĞİLDİR ve paralellik onu tarif etmez: bir SCC'nin üyeleri
                        // sıralı derlenir. Kullanıcıyı bekleten sayı tur tavanıdır, satır onu söyler.
                        RunMode.Cycles => StreamText.CyclesStarted(_willBuildIds.Count),
                        _ => StreamText.BuildStarted(_willBuildIds.Count, parallelism),
                    });
                    _pendingRunStartMode = null;
                }
                break;

            case ProjectStartedEvent e:
                _stream.StartBuilding(e.ProjectId, e.Name, _nowMs()); // building → yalnız aktif satırda görünür (tampon satırı YOK)
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
                PushStream(StreamKind.Skip, e.ProjectId, StreamText.Skipped(ResolveName(e.ProjectId)));
                break;

            // [cycle rounds/Task 8] Bir SCC'nin turu başladı — grubun tek ilerleme sinyali. ProjectId LİDERİN
            // id'sidir (satır ona bağlı/tıklanabilir, ok/fail/skip satırlarıyla AYNI desen). Kind=Info: ne
            // başarı ne hata, BuildStarted/Continue satırlarıyla AYNI amber ▸ anlatı tonu.
            case CycleRoundStartedEvent e:
                PushStream(StreamKind.Info, e.ProjectId,
                    StreamText.CycleRound(e.Round, e.RoundCap, ResolveName(e.ProjectId), e.MemberCount));
                break;

            case SyncCompletedEvent e:
                PushStream(StreamKind.Sync, null, StreamText.Sync(e.ToBuildCount, e.UpToDateCount));
                break;

            case RunCompletedEvent e:
                if (e.Outcome == RunOutcome.Stopped)
                    PushStream(StreamKind.Info, null, StreamText.Stopped(e.Queued)); // stopped → info (parıltı YOK)
                else
                    PushStream(StreamKind.Done, null,
                        StreamText.Completed(e.Failed, e.Succeeded, e.Skipped, e.DepIssueCount, e.DurationMs));
                _stream.EndRun();
                SyncActiveLine();
                break;
        }
    }

    private void PushStream(StreamKind kind, string? projectId, string text)
    {
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
