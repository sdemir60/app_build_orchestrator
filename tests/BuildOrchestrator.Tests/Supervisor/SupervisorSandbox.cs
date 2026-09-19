using System.Diagnostics;
using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Supervisor;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [spec 2026-09-18 §5.5 · Task 10 fix I1] Gerçek bir Supervisor başlatan her testin TEK izolasyon yardımcısı: kendi
/// geçici önbellek kökü (<c>&lt;sandbox&gt;</c>) ve <c>--logs &lt;sandbox&gt;\logs</c> argümanı. Supervisor önbelleği
/// logs'un üstünde tutar (<c>Program.Main</c>), yani build-state, evaluation cache ve <c>run-inflight.json</c> hep
/// sandbox'tadır.
///
/// <para><b>Neden zorunlu:</b> motor açılışta uçuş defterini kurtarır (<c>InFlightLedger.Recover</c>). Argümansız
/// başlatılan bir test motoru kullanıcının GERÇEK <c>%LOCALAPPDATA%\BuildOrchestrator\run-inflight.json</c>'ını
/// işler — süit, kullanıcının koşusu uçuştayken çalışırsa o projeleri geçersizlerdi. Kural kaynak guard'ıyla
/// pinlidir: <c>SupervisorIsolationGuardTests</c>.</para>
///
/// <para>Ömür: <c>using var sandbox = new SupervisorSandbox();</c> motordan/process'ten ÖNCE bildirilir, böylece
/// ters sırada önce motor, sonra klasör gider. Silme best-effort'tur (çıkmakta olan process bir dosyayı kısa süre
/// tutabilir) — sızıntı testin sonucunu etkilemez.</para>
/// </summary>
public sealed class SupervisorSandbox : IDisposable
{
    public string CacheRoot { get; } = Directory.CreateTempSubdirectory("bo-sup-").FullName;

    /// <summary>Supervisor'a <c>--logs</c> ile verilen klasör; önbellek onun üstündedir (<see cref="CacheRoot"/>).</summary>
    public string LogsDir => Path.Combine(CacheRoot, "logs");

    /// <summary>Supervisor'ı bu sandbox'a bağlayan argümanlar.</summary>
    public IReadOnlyList<string> Args => ["--logs", LogsDir];

    /// <summary>Stdio yönlendirmeli <see cref="ProcessStartInfo"/> (bkz. <see cref="TestPaths.Psi"/>).</summary>
    public ProcessStartInfo Psi(bool debugHooks = false) => TestPaths.Psi(LogsDir, debugHooks);

    /// <summary><c>JobProcessLauncher</c> yolunun ham komut satırı; kanca bayrağı tek sabitten
    /// (<see cref="SupervisorHost.DebugHooksArg"/>).</summary>
    public string CommandLine(bool debugHooks = false) =>
        WindowsCommandLine.Build(TestPaths.SupervisorExe,
            [.. Args, .. debugHooks ? new[] { SupervisorHost.DebugHooksArg } : []]);

    /// <summary>Bu sandbox'ta başlayan bir <see cref="EngineHost"/> — gerçek motoru BAŞLATAN/YENİDEN BAŞLATAN her App
    /// testi bunu kullanır.</summary>
    public EngineHost IsolatedEngineHost(TimeSpan? startupTimeout = null) =>
        new(TestPaths.SupervisorExe, startupTimeout, Args);

    public void Dispose()
    {
        try { Directory.Delete(CacheRoot, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort */ }
    }
}
