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
    /// <summary>Aktif satır SON değiştiğinde fırtına penceresindeydiyse true — görünüm daktiloyu instant kurar.</summary>
    [ObservableProperty] private bool _activeLineBurst;
    /// <summary>Aktif proje her DEĞİŞTİĞİNDE artar — görünüm daktiloyu yeniden başlatır (prototip activeLine.id).</summary>
    [ObservableProperty] private long _activeLineGeneration;

    /// <summary>[OnEvent kablajı] Bir UI-thread IPC event'inden stream tampon satırı + aktif satır türetir.
    /// <see cref="OnEvent"/>'in SONUNDA çağrılır — proje satırları/sayaçlar (Counters) o an zaten güncellenmiştir,
    /// bu yüzden ad çözümü ve done-glyph'in yeşil/kırmızı kararı (Counters.Failed) doğru okunur.</summary>
    private void AppendStreamFor(IpcEvent ev)
    {
        switch (ev)
        {
            case RunStartedEvent e:
                _stream.EndRun(); // yeni koşu/segment: aktif + building sıfırlanır (tampon sayacı KORUNUR)
                SyncActiveLine();
                PushStream(StreamKind.Info, null,
                    e.Mode == RunMode.Continue
                        ? StreamText.Continue(e.TotalProjects, e.Parallelism)
                        : StreamText.BuildStarted(e.TotalProjects, e.Parallelism));
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
        string time = WallClock().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
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
        // Metin/burst'ü generation'DAN ÖNCE yaz — görünüm generation değişimini izleyip taze metinle daktilo koşar.
        ActiveLineProjectId = _stream.ActiveProjectId;
        ActiveLineText = _stream.ActiveText;
        ActiveLineBurst = _stream.ActiveBurst;
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
