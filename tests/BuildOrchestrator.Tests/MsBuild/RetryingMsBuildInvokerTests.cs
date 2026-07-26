using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.ProcessControl;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

// [T8] CopyContention: MSB3021/MSB3026/MSB3027 satır tanıma — gerçek MSBuild gerektirmez, saf metin eşleşmesi.
[Trait("Category", "MsBuild")]
public class CopyContentionTests
{
    [Theory]
    [InlineData("Foo.csproj : error MSB3021: Unable to copy file \"obj\\Debug\\Foo.dll\" to \"bin\\Debug\\Foo.dll\". The process cannot access the file because it is being used by another process.")]
    [InlineData("Foo.csproj : warning MSB3026: Could not copy \"obj\\Debug\\Foo.dll\" to \"bin\\Debug\\Foo.dll\". Beginning retry 1 in 1000ms.")]
    [InlineData("Foo.csproj : error MSB3027: Could not copy \"obj\\Debug\\Foo.dll\" to \"bin\\Debug\\Foo.dll\". Exceeded retry count of 10. Failed.")]
    public void IsContention_true_only_for_real_MSB3021_MSB3026_MSB3027_lines(string line)
    {
        Assert.True(CopyContention.IsContention(line));
    }

    [Fact]
    public void IsContention_false_for_unrelated_copy_line_without_error_code()
    {
        Assert.False(CopyContention.IsContention("obj\\Debug\\Foo.dll -> bin\\Debug\\Foo.dll"));
    }

    [Fact]
    public void IsContention_false_for_compile_error()
    {
        Assert.False(CopyContention.IsContention("Foo.cs(12,5): error CS0103: The name 'Bar' does not exist in the current context"));
    }

    [Fact]
    public void IsContention_false_for_code_embedded_in_unrelated_word()
    {
        // "MSB3021" bir dosya/etiket adının PARÇASI olarak geçiyorsa (kelime sınırı yok) yanlış-pozitif olmamalı.
        Assert.False(CopyContention.IsContention("see notes/MSB30210-workaround.txt for details"));
    }

    [Fact]
    public void IsContention_null_is_false()
    {
        Assert.False(CopyContention.IsContention(null!));
    }
}

// [T8] RetryingMsBuildInvoker: contention'da enjekte backoff ile retry, gerçek MSBuild/zaman YOK — sahte
// (scripted) IMsBuildInvoker + sahte delay callback (D8: sleep-poll yasak, testler beklemez).
[Trait("Category", "MsBuild")]
public class RetryingMsBuildInvokerTests
{
    private static readonly MsBuildInvokeRequest Req = new("C:\\Repo\\Foo.csproj", "Debug", "C:\\Repo", NeedsRestore: false);

    private sealed class ScriptedInvoker : IMsBuildInvoker
    {
        private readonly List<(MsBuildInvokeResult Result, string[] Lines)> _script;
        private int _index;
        public int CallCount { get; private set; }
        public List<CancellationToken> ObservedTokens { get; } = new();

        public ScriptedInvoker(params (MsBuildInvokeResult Result, string[] Lines)[] script) => _script = script.ToList();

        public Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct)
        {
            CallCount++;
            ObservedTokens.Add(ct);
            var (result, lines) = _script[Math.Min(_index, _script.Count - 1)];
            _index++;
            foreach (var line in lines) onLine(line);
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingDelay
    {
        public List<TimeSpan> Calls { get; } = new();
        public Func<TimeSpan, CancellationToken, Task>? ThrowOnCall;

        public Task Delay(TimeSpan wait, CancellationToken ct)
        {
            Calls.Add(wait);
            return ThrowOnCall is null ? Task.CompletedTask : ThrowOnCall(wait, ct);
        }
    }

    /// <summary>
    /// [T20-b/P3] Copy-contention penceresini KAYDEDEN sahte floor. Gerçek cap yazımı koordinatörün işidir
    /// (bkz. <c>RunCoordinatorTests</c>'in <c>RecordingGovernor</c>'ı); burada yalnız decorator'ın SIRASI
    /// doğrulanır: pencere retry'dan ÖNCE açılır ve invoke hangi yoldan dönerse dönsün kapanır.
    /// </summary>
    private sealed class RecordingFloor : ICopyPhaseCpuFloor
    {
        public List<string> Calls { get; } = new();

        /// <summary>Run'da gerçekten bir cap yürürlükte mi — cap-farkındalı backoff'un girdisi.</summary>
        public bool IsCapActive { get; init; } = true;

        public IDisposable? Enter()
        {
            // Cap yoksa yükseltilecek bir taban da yoktur: üretim implementasyonu da null döner (NO-OP).
            if (!IsCapActive) return null;
            Calls.Add("enter");
            return new Window(this);
        }

        private sealed class Window(RecordingFloor owner) : IDisposable
        {
            private int _closed;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref _closed, 1) == 0) owner.Calls.Add("exit");
            }
        }
    }

    private static readonly MsBuildInvokeResult Contention = new(ExitCode: 1, DurationMs: 10, TimedOut: false, Killed: false);
    private static readonly MsBuildInvokeResult Success = new(ExitCode: 0, DurationMs: 10, TimedOut: false, Killed: false);
    private static readonly MsBuildInvokeResult CompileError = new(ExitCode: 1, DurationMs: 10, TimedOut: false, Killed: false);

    private static readonly string[] ContentionLines = ["Foo.csproj : error MSB3021: Unable to copy file \"a\" to \"b\". being used by another process."];
    private static readonly string[] CompileErrorLines = ["Foo.cs(1,1): error CS0103: The name 'Bar' does not exist in the current context"];
    private static readonly string[] SuccessLines = ["Build succeeded."];

    [Fact]
    public async Task Contention_twice_then_success_retries_twice_with_exact_backoff()
    {
        var scripted = new ScriptedInvoker(
            (Contention, ContentionLines),
            (Contention, ContentionLines),
            (Success, SuccessLines));
        var recordingDelay = new RecordingDelay();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff, recordingDelay.Delay);

        var lines = new List<string>();
        var result = await invoker.InvokeAsync(Req, lines.Add, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(3, scripted.CallCount);
        Assert.Equal([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(600)], recordingDelay.Calls);
    }

    [Fact]
    public async Task Compile_error_runs_exactly_once_no_retry()
    {
        var scripted = new ScriptedInvoker((CompileError, CompileErrorLines));
        var recordingDelay = new RecordingDelay();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff, recordingDelay.Delay);

        var result = await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(1, scripted.CallCount);
        Assert.Empty(recordingDelay.Calls);
    }

    [Fact]
    public async Task Contention_every_attempt_returns_failing_result_after_exactly_three_attempts()
    {
        var scripted = new ScriptedInvoker(
            (Contention, ContentionLines),
            (Contention, ContentionLines),
            (Contention, ContentionLines));
        var recordingDelay = new RecordingDelay();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff, recordingDelay.Delay);

        var result = await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(3, scripted.CallCount);
        Assert.Equal([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(600)], recordingDelay.Calls);
    }

    [Fact]
    public async Task OnLine_lines_flow_on_every_attempt_in_order()
    {
        var scripted = new ScriptedInvoker(
            (Contention, ["attempt-1-line: error MSB3021: locked"]),
            (Success, ["attempt-2-line"]));
        var recordingDelay = new RecordingDelay();
        var lines = new List<string>();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff, recordingDelay.Delay);

        await invoker.InvokeAsync(Req, lines.Add, CancellationToken.None);

        Assert.Equal(["attempt-1-line: error MSB3021: locked", "attempt-2-line"], lines);
    }

    [Fact]
    public async Task OnRetry_invoked_once_per_retry_not_on_final_attempt()
    {
        var scripted = new ScriptedInvoker(
            (Contention, ContentionLines),
            (Success, SuccessLines));
        var recordingDelay = new RecordingDelay();
        var onRetryCalls = new List<string>();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff, recordingDelay.Delay, onRetryCalls.Add);

        await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Single(onRetryCalls);
        // İçerik doğrulaması: proje id, deneme numarası, bekleme ms bulunmalı
        var message = onRetryCalls[0];
        Assert.Contains(Req.ProjectId, message);
        Assert.Contains("1", message);  // attempt number
        Assert.Contains("200", message); // first backoff in ms
    }

    [Fact]
    public async Task TimedOut_result_with_contention_like_line_is_not_retried()
    {
        // Killed/TimedOut = teardown/timeout yolundan dönüş — contention satırı görünse bile retry teardown ile
        // YARIŞMAMALI. Tek deneme, sonuç aynen döner.
        var timedOut = new MsBuildInvokeResult(ExitCode: -1, DurationMs: 10, TimedOut: true, Killed: true);
        var scripted = new ScriptedInvoker((timedOut, ContentionLines));
        var recordingDelay = new RecordingDelay();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff, recordingDelay.Delay);

        var result = await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Equal(1, scripted.CallCount);
        Assert.True(result.TimedOut);
        Assert.Empty(recordingDelay.Calls);
    }

    [Fact]
    public async Task Killed_result_with_contention_like_line_is_not_retried()
    {
        // ct iptali sonucu Killed=true, TimedOut=false — caller iptal etti, retry teardown ile YARIŞMAMALI.
        var killed = new MsBuildInvokeResult(ExitCode: -1, DurationMs: 10, TimedOut: false, Killed: true);
        var scripted = new ScriptedInvoker((killed, ContentionLines));
        var recordingDelay = new RecordingDelay();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff, recordingDelay.Delay);

        var result = await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Equal(1, scripted.CallCount);
        Assert.True(result.Killed);
        Assert.Empty(recordingDelay.Calls);
    }

    [Fact]
    public async Task Cancellation_during_backoff_aborts_retry_loop_without_further_attempts()
    {
        var scripted = new ScriptedInvoker(
            (Contention, ContentionLines),
            (Success, SuccessLines)); // ikinci deneme HİÇ çağrılmamalı — delay iptalde fırlatır
        using var cts = new CancellationTokenSource();
        var recordingDelay = new RecordingDelay { ThrowOnCall = (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; } };
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff, recordingDelay.Delay);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoker.InvokeAsync(Req, _ => { }, cts.Token));

        Assert.Equal(1, scripted.CallCount);
        Assert.Single(recordingDelay.Calls);
    }

    [Fact]
    public async Task Success_with_contention_warning_line_does_not_retry()
    {
        // Başarılı bir deneme (ExitCode: 0) bir MSB3026 uyarı satırı içerse bile (MSBuild kendi internal
        // copy-retry kendini iyileştirdi), retry EDİLMEZ — retry edecek bir şey yok, build başarılı.
        var scripted = new ScriptedInvoker(
            (Success, ["Foo.csproj : warning MSB3026: Copy retrying due to contention. Succeeded on retry."]));
        var recordingDelay = new RecordingDelay();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff, recordingDelay.Delay);

        var result = await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, scripted.CallCount);
        Assert.Empty(recordingDelay.Calls);
    }

    [Fact]
    public void DefaultBackoff_is_200ms_then_600ms()
    {
        Assert.Equal([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(600)], RetryingMsBuildInvoker.DefaultBackoff);
    }

    // ---------------------------------------------------------------- [T20-b/P3] copy fazı: cap taban penceresi

    [Fact]
    public async Task Copy_contention_opens_exactly_one_cpu_floor_window_that_closes_when_the_invoke_returns()
    {
        // İKİ retry, TEK pencere: her retry'da yeniden Enter etmek ref-count'u şişirir ve pencere run'ın
        // sonuna kadar kapanmazdı (tek bir contention Light bir run'ı kalıcı olarak tabana çıkarırdı).
        var scripted = new ScriptedInvoker(
            (Contention, ContentionLines),
            (Contention, ContentionLines),
            (Success, SuccessLines));
        var floor = new RecordingFloor();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff,
            new RecordingDelay().Delay, cpuFloor: floor);

        await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Equal(["enter", "exit"], floor.Calls);
    }

    [Fact]
    public async Task A_failure_without_copy_contention_never_opens_the_cpu_floor_window()
    {
        // Gerçek derleme hatası retry EDİLMEZ — dolayısıyla gevşetilecek bir copy penceresi de yoktur.
        var scripted = new ScriptedInvoker((CompileError, CompileErrorLines));
        var floor = new RecordingFloor();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff,
            new RecordingDelay().Delay, cpuFloor: floor);

        await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Empty(floor.Calls);
    }

    [Fact]
    public async Task The_cpu_floor_window_closes_even_when_every_retry_is_exhausted()
    {
        var scripted = new ScriptedInvoker(
            (Contention, ContentionLines),
            (Contention, ContentionLines),
            (Contention, ContentionLines));
        var floor = new RecordingFloor();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff,
            new RecordingDelay().Delay, cpuFloor: floor);

        var result = await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["enter", "exit"], floor.Calls); // BAŞARISIZ yolda da geri konur
    }

    [Fact]
    public async Task The_cpu_floor_window_closes_when_the_backoff_is_cancelled()
    {
        // İptal (teardown/Stop) yolundan çıkan bir invoke da cap'i geri koymalı: aksi halde Supervisor ömrü
        // boyunca yaşayan inner job'da sahibi olmayan bir taban kalırdı.
        var scripted = new ScriptedInvoker((Contention, ContentionLines), (Success, SuccessLines));
        using var cts = new CancellationTokenSource();
        var recordingDelay = new RecordingDelay { ThrowOnCall = (_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; } };
        var floor = new RecordingFloor();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff,
            recordingDelay.Delay, cpuFloor: floor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoker.InvokeAsync(Req, _ => { }, cts.Token));

        Assert.Equal(["enter", "exit"], floor.Calls);
    }

    [Fact]
    public async Task Backoff_stretches_while_a_cpu_cap_is_active()
    {
        // Cap altında copy'nin tamamlanma penceresi uzar; sabit backoff MSB3027'yi KALICI bir Failed'a çevirir.
        var scripted = new ScriptedInvoker(
            (Contention, ContentionLines),
            (Contention, ContentionLines),
            (Success, SuccessLines));
        var recordingDelay = new RecordingDelay();
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff,
            recordingDelay.Delay, cpuFloor: new RecordingFloor { IsCapActive = true });

        await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Equal([TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(900)], recordingDelay.Calls);
    }

    [Fact]
    public async Task Backoff_is_unchanged_when_no_cpu_cap_is_active()
    {
        // Full profili (cap yok): dizi AYNEN korunur ve governor'a HİÇ dokunulmaz.
        var scripted = new ScriptedInvoker(
            (Contention, ContentionLines),
            (Contention, ContentionLines),
            (Success, SuccessLines));
        var recordingDelay = new RecordingDelay();
        var floor = new RecordingFloor { IsCapActive = false };
        var invoker = new RetryingMsBuildInvoker(scripted, RetryingMsBuildInvoker.DefaultBackoff,
            recordingDelay.Delay, cpuFloor: floor);

        await invoker.InvokeAsync(Req, _ => { }, CancellationToken.None);

        Assert.Equal([TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(600)], recordingDelay.Calls);
        Assert.Empty(floor.Calls);
    }
}
