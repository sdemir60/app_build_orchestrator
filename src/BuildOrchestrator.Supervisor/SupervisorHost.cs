using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Git;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Core.Workspace;

namespace BuildOrchestrator.Supervisor;

/// <summary>
/// [A5/T69] Kök-yola (<c>RootPath</c>) bağlı Core servislerinin fabrikaları. Bu tipler kök başına kurulur
/// çünkü komutlar kökü BERABERİNDE taşır — Supervisor tek bir repo'ya sabitlenmiş değildir. Kompozisyon kökü
/// (<see cref="Program"/>) doldurur; testler izole cache/havuz kökleriyle kendi örneğini verir.
/// </summary>
public sealed record WorkspaceServices(
    Func<string, SyncWorkspaceService> Sync,
    Func<string, GitService> Git,
    Func<string, WorktreeManager> Worktree)
{
    /// <summary>Üretim bağlaması: gerçek <see cref="ProcessRunner"/>, <paramref name="cacheRoot"/>'taki
    /// evaluation-cache + build-state, <paramref name="poolRoot"/>'taki worktree havuzu.</summary>
    public static WorkspaceServices Default(string cacheRoot, string poolRoot) => new(
        root => new SyncWorkspaceService(
            new WorkspaceScanner(), new CsprojEvaluator(),
            new EvaluationCache(Path.Combine(cacheRoot, "evaluation-cache.json")),
            new GitService(new ProcessRunner(), root), new BuildStateStore(cacheRoot)),
        root => new GitService(new ProcessRunner(), root),
        root => new WorktreeManager(new ProcessRunner(), root, poolRoot));
}

public sealed class SupervisorHost(NdjsonWriter writer, NdjsonReader reader, JobObject innerJob,
    RunCoordinator coordinator, WorkspaceServices workspace)
{
    private bool _running = true;

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        // [D1 review · B2] TEK sürüm kimliği: Directory.Build.props → InformationalVersion (teslim etiketiyle,
        // ör. "1.0.0+it5"). Yalın assembly Version'ı SDK varsayılanından (1.0.0) ayrılamazdı; App bu değeri
        // konsolun boot satırında gösterir. Attribute yoksa assembly sürümüne düşülür.
        string version =
            typeof(SupervisorHost).Assembly
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(SupervisorHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        await writer.WriteAsync(new EngineReadyEvent(Environment.ProcessId, version), ct);
        while (_running)
        {
            IpcCommand? cmd;
            try { cmd = await reader.ReadAsync<IpcCommand>(ct); }
            catch (IpcFramingException ex)
            { await writer.WriteAsync(new ErrorEvent("framing", ex.Message), ct); return 2; } // kurtarılamaz
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            { await writer.WriteAsync(new ErrorEvent("badCommand", ex.Message), ct); continue; } // satır tüketildi, devam
            if (cmd is null) return 0; // stdin EOF → App kapandı (outer Job zaten süpürür; bu düzenli çıkış)
            await DispatchAsync(cmd, ct);
        }
        return 0;
    }

    private async Task DispatchAsync(IpcCommand cmd, CancellationToken ct)
    {
        switch (cmd)
        {
            case PingCommand p:
                await writer.WriteAsync(new PongEvent(p.Seq), ct); break;
            case ShutdownCommand:
                _running = false; break;
            case StartRunCommand s:
                await coordinator.StartAsync(s, ct); break; // hemen döner — run arka planda koşar, loop komut almaya devam eder
            case StopRunCommand s:
                await StopRunAsync(s, ct); break;
            case GetProjectLogCommand g:
                await SendProjectLogAsync(g, ct); break;
            case DebugSpawnChildrenCommand d:
                await SpawnDebugChildrenAsync(d, ct); break;
            case SyncWorkspaceCommand s:
                await SyncWorkspaceAsync(s, ct); break;
            case ListBranchesCommand b:
                await ListBranchesAsync(b, ct); break;
            case ListWorktreesCommand w:
                await WriteWorktreeListAsync(w.RootPath, ct); break;
            case DeleteWorktreeCommand d:
                await DeleteWorktreeAsync(d, ct); break;
            case SetPerfModeCommand p:
                await ApplyPerfModeAsync(p, ct); break;
            default:
                await writer.WriteAsync(new ErrorEvent("unknownCommand", cmd.GetType().Name), ct); break;
        }
    }

    /// <summary>
    /// [A5/T69] Sync akışı: iş TAMAMEN Core'da (<see cref="SyncWorkspaceService"/>, D3), burada yalnız
    /// event'lerin tek NdjsonWriter'a aktarılması var. Aynı writer koordinatörle PAYLAŞILIR — satır bütünlüğü
    /// writer'ın kendi kilidiyle korunur, yani stdout YALNIZ NDJSON kalır [D4].
    /// <para>
    /// Bu çağrı, <c>startRun</c>'ın aksine, komut döngüsünü Sync BİTENE KADAR BLOKLAR: Sync salt-okurdur ve
    /// saniyeler mertebesindedir; ayrı bir arka plan task'ına almak, komut sırasını (ör. hemen ardından gelen
    /// bir <c>startRun</c>) Sync'in event akışıyla YARIŞTIRIRDI.
    /// </para>
    /// <para>
    /// <b>[Fix wave 1 — Finding 1] Event'ler TAMPONLANMAZ:</b> her event üretildiği ANDA yazılır. Eskiden
    /// hepsi bir listede toplanıp Sync bittikten SONRA yazılıyordu — bu, <c>SyncProgressEvent</c>'i
    /// "progress" olmaktan çıkarıyor (konsol tüm süre boyunca boş kalıyor), <c>Syncing</c> fazını da tek bir
    /// event-pump turunda <c>Empty → Syncing → Idle</c> yapıp şeridin hiç render edilememesine yol açıyordu.
    /// Tamponlama sıra için GEREKLİ DEĞİLDİR: bu komut zaten komut döngüsünü bloklar ve writer paylaşılan tek
    /// örnektir (satır bütünlüğü onun kendi semaforunda).
    /// </para>
    /// </summary>
    private async Task SyncWorkspaceAsync(SyncWorkspaceCommand cmd, CancellationToken ct)
    {
        // Servisin `emit`i SENKRON (Core, IPC yazımını/async'i bilmez), writer ise async'tir: bu köprüde
        // bloklamak GÜVENLİDİR — çağıran zaten bu komut boyunca bloklanmış bir Supervisor thread'idir,
        // SynchronizationContext yoktur (console host), ve writer'ın semaforu koordinatörle sırayı korur.
        void Emit(IpcEvent ev) => writer.WriteAsync(ev, ct).GetAwaiter().GetResult();
        try
        {
            await workspace.Sync(cmd.RootPath).RunAsync(cmd, Emit, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sync'in kendi kapıları bozuk girdiyi zaten planFailed'a çevirir; buraya yalnız GERÇEKTEN
            // beklenmeyen bir hata düşer. IPC sınırını exception ASLA geçmemeli — tanımlı bir event'e çevrilir.
            await writer.WriteAsync(new ErrorEvent("planFailed", ex.Message), ct);
        }
    }

    /// <summary>[A5/T69] Yerel + remote-tracking branch listesi (SALT-OKUR) → <see cref="BranchListEvent"/>.</summary>
    private async Task ListBranchesAsync(ListBranchesCommand cmd, CancellationToken ct)
    {
        var result = await workspace.Git(cmd.RootPath).ListBranchesAsync(ct);
        if (!result.Success)
        { await writer.WriteAsync(new ErrorEvent("branchListFailed", result.Error!), ct); return; }

        var branches = result.Value!
            .Select(b => new BranchRef(b.Name, b.Sha, b.IsActive, b.IsRemote))
            .ToList();
        await writer.WriteAsync(new BranchListEvent(branches), ct);
    }

    /// <summary>[A5/T69] Havuz envanteri → <see cref="WorktreeListEvent"/>. <c>deleteWorktree</c> de silme
    /// sonrası BUNU yeniden yayınlar, böylece App'in listesi ek bir komut gerekmeden tazelenir.</summary>
    private async Task WriteWorktreeListAsync(string rootPath, CancellationToken ct)
    {
        var result = await workspace.Worktree(rootPath).ListWorktreesAsync(ct);
        if (!result.Success)
        { await writer.WriteAsync(new ErrorEvent("worktreeListFailed", result.Error!), ct); return; }

        var worktrees = result.Value!
            .Select(w => new Worktree(w.Name, w.Branch ?? "", w.Path, w.IsActive, w.SizeBytes))
            .ToList();
        await writer.WriteAsync(new WorktreeListEvent(worktrees), ct);
    }

    private async Task DeleteWorktreeAsync(DeleteWorktreeCommand cmd, CancellationToken ct)
    {
        // Ad doğrulaması (path traversal) Core'da: WorktreeManager.DeleteAsync → PathSanitizer.IsSafeSegment.
        var result = await workspace.Worktree(cmd.RootPath).DeleteAsync(cmd.Name, ct);
        if (!result.Success)
        { await writer.WriteAsync(new ErrorEvent("worktreeDeleteFailed", result.Error!), ct); return; }

        await WriteWorktreeListAsync(cmd.RootPath, ct);
    }

    /// <summary>
    /// [T20-b/K11] Koşan run'ın perf profilini değiştirir. Profil tablosu Core'un (<see cref="PerfProfile"/>) —
    /// burada yalnız metin çözülür ve koordinatöre verilir. <b>Yalnız CPU cap + priority canlı değişir</b>;
    /// paralellik bir sonraki run'da geçerli olur (bkz. <see cref="RunCoordinator.ApplyPerfMode"/>).
    /// Başarıda event YOKTUR (App zaten kendi konsol notunu yazar); çözülemeyen mod ise sessizce yutulmaz —
    /// <c>error(badPerfMode)</c> döner, böylece "komut tanınmadı" (<c>unknownCommand</c>) ile karışmaz.
    /// </summary>
    private async Task ApplyPerfModeAsync(SetPerfModeCommand cmd, CancellationToken ct)
    {
        if (PerfProfile.TryParse(cmd.PerfMode) is not { } profile)
        { await writer.WriteAsync(new ErrorEvent("badPerfMode", cmd.PerfMode), ct); return; }
        coordinator.ApplyPerfMode(profile);
    }

    /// <summary>
    /// [I2-K1] Aktif bir run varsa Stop'un SAHİBİ koordinatördür: hard'da inner Job'ı O terminate eder ve
    /// <c>runStopped</c>'ı in-flight projelerin sonuçları raporlandıktan SONRA O yazar (kısıt: "öldürüldü" ≠
    /// "raporlandı"). Aktif run yoksa T4-base davranışı korunur: hard → job terminate + anında ack.
    /// TryRequestStop ATOMİKTİR — "aktif mi?" kontrolü ile sahiplenme arasında yarış penceresi yoktur, bu yüzden
    /// runStopped tam olarak bir kez (ya koordinatörden ya buradan) yazılır.
    /// </summary>
    private async Task StopRunAsync(StopRunCommand s, CancellationToken ct)
    {
        if (coordinator.TryRequestStop(s.Kind)) return;
        if (s.Kind == StopKind.Hard) innerJob.Terminate();
        await writer.WriteAsync(new RunStoppedEvent(s.RunId, WasHard: s.Kind == StopKind.Hard), ct);
    }

    /// <summary>
    /// [T28] Log, AKTİF (ya da en son biten) run'ın dizininden okunur — statik <c>logsRoot</c> DEĞİL (koşan bir
    /// run sırasında logsRoot'un kendisinde hiçbir şey bulunmaz, yalnız run-alt-dizininde). Kaynak koordinatördür
    /// (<see cref="RunCoordinator.TryGetProjectLogSnapshot"/>): atomik metin + o ana kadar diske yazılmış satır
    /// sayısı (<c>ThroughLineNumber</c>) birlikte gelir — App bunu canlı <c>projectLog</c> akışıyla dikiş
    /// noktası olarak kullanır (<c>LineNumber &lt;= ThroughLineNumber</c> olanlar zaten bu chunk'larda vardır).
    /// Her chunk AYNI ThroughLineNumber'ı taşır — o, chunk'ı değil SNAPSHOT'ı tanımlar.
    /// </summary>
    private async Task SendProjectLogAsync(GetProjectLogCommand g, CancellationToken ct)
    {
        if (!coordinator.TryGetProjectLogSnapshot(g.ProjectId, out string text, out int throughLineNumber))
        { await writer.WriteAsync(new ErrorEvent("logNotFound", g.ProjectId), ct); return; }
        foreach (var c in LogChunker.Chunk(text))
            await writer.WriteAsync(new ProjectLogChunkEvent(g.ProjectId, c.Sequence, c.Text, c.IsLast, throughLineNumber), ct);
    }

    private async Task SpawnDebugChildrenAsync(DebugSpawnChildrenCommand d, CancellationToken ct)
    {
        var pids = new List<int>();
        for (int i = 0; i < d.Count; i++)
        {
            try
            {
                string cmdLine = WindowsCommandLine.Build(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    "/c", "powershell -NoProfile -Command Start-Sleep -Seconds 300");
                var child = JobProcessLauncher.Launch(innerJob, cmdLine, new LaunchOptions(Breakaway: d.Breakaway));
                pids.Add(child.Pid);
            }
            catch (Win32Exception ex) // breakaway probe: NativeErrorCode==5 (ERROR_ACCESS_DENIED) beklenir
            { await writer.WriteAsync(new ErrorEvent("spawnFailed", $"win32={ex.NativeErrorCode}"), ct); return; }
        }
        await writer.WriteAsync(new DebugChildrenSpawnedEvent(pids.ToArray()), ct);
    }
}
