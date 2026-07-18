using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

[Trait("Category", "MsBuild")]
public class MsBuildInvokerTests
{
    // MSBuild yoksa (VS/Build Tools kurulu değil) SkippableFact ile atla — mevcut MsBuildResolverTests deseni.
    private static async Task<string> ResolveMsBuildExeOrSkipAsync()
    {
        try
        {
            var loc = await new MsBuildResolver(new ProcessRunner()).ResolveAsync();
            return loc.MsBuildExePath;
        }
        catch (MsBuildResolveException ex)
        {
            Skip.If(true, ex.Message);
            throw; // ulaşılmaz — Skip.If(true, …) her zaman fırlatır
        }
    }

    private static string NewTempDir([System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        string dir = Path.Combine(Path.GetTempPath(), "boi-msbuild-tests", caller, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [SkippableFact] // (a) LegacyFixture v4.6 classlib → exit 0 + satır aktı + DLL üretildi
    public async Task Legacy_classlib_builds_green_streams_lines_and_produces_dll()
    {
        string exe = await ResolveMsBuildExeOrSkipAsync();
        string dir = NewTempDir();
        string csproj = LegacyFixture.CreateClassLib(dir, "SampleLib");

        using var job = JobObject.CreateKillOnClose();
        var invoker = new MsBuildInvoker(job, exe);
        var lines = new List<string>();

        var result = await invoker.InvokeAsync(
            new MsBuildInvokeRequest(csproj, "Debug", dir, NeedsRestore: false),
            line => { lock (lines) lines.Add(line); },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.Killed);
        Assert.NotEmpty(lines);
        Assert.True(File.Exists(Path.Combine(dir, "bin", "Debug", "SampleLib.dll")), "beklenen DLL üretilmedi");
    }

    [SkippableFact] // (b) derleme hatası olan fixture → exit≠0 + satırlarda "error CS"
    public async Task Compile_error_fixture_fails_with_error_CS_in_lines()
    {
        string exe = await ResolveMsBuildExeOrSkipAsync();
        string dir = NewTempDir();
        string csproj = LegacyFixture.CreateClassLib(dir, "BrokenLib");
        // Class1.cs'in üzerine kasıtlı sözdizimi hatası yaz (LegacyFixture'ın kendisi hep geçerli üretir).
        File.WriteAllText(Path.Combine(dir, "Class1.cs"), "public class Class1 { this is not valid C# ][ }");

        using var job = JobObject.CreateKillOnClose();
        var invoker = new MsBuildInvoker(job, exe);
        var lines = new List<string>();

        var result = await invoker.InvokeAsync(
            new MsBuildInvokeRequest(csproj, "Debug", dir, NeedsRestore: false),
            line => { lock (lines) lines.Add(line); },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(lines, l => l.Contains("error CS", StringComparison.Ordinal));
    }

    [SkippableFact] // (c) ct iptali → Killed=true + process gerçekten yok (deterministik: IOCP ile pid izlenir)
    public async Task Cancellation_kills_child_and_process_is_gone()
    {
        string exe = await ResolveMsBuildExeOrSkipAsync();
        string dir = NewTempDir();
        string csproj = LegacyFixture.CreateClassLib(dir, "CancelLib");

        using var job = JobObject.CreateKillOnClose();
        using var iocp = job.AttachCompletionPort(); // Launch'tan ÖNCE bağlanmalı — kaçırılan bildirim olmasın
        var invoker = new MsBuildInvoker(job, exe);
        using var cts = new CancellationTokenSource();

        var invokeTask = invoker.InvokeAsync(
            new MsBuildInvokeRequest(csproj, "Debug", dir, NeedsRestore: false),
            _ => { }, cts.Token);

        // İlk NEW_PROCESS bildirimi = doğan MSBuild.exe child (NeedsRestore:false → tek child). Doğar doğmaz iptal et.
        var born = iocp.WaitNext(TimeSpan.FromSeconds(30)) ?? throw new TimeoutException("child doğum bildirimi gelmedi");
        Assert.Equal(NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS, born.MessageId);
        int pid = born.Pid;
        cts.Cancel();

        var result = await invokeTask.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(result.Killed);
        Assert.False(result.TimedOut); // caller iptali — PerProjectTimeout değil

        // pid'in job'dan çıktığını IOCP bildirimiyle doğrula (sleep-poll YOK — bloklu bekleme).
        while (true)
        {
            var n = iocp.WaitNext(TimeSpan.FromSeconds(10)) ?? throw new TimeoutException("exit bildirimi gelmedi");
            if (n.Pid == pid && n.MessageId is NativeMethods.JOB_OBJECT_MSG_EXIT_PROCESS
                                             or NativeMethods.JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS)
                break;
        }
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Fact] // (d) — gerçek MSBuild gerektirmez: saf encoding round-trip
    public void OutputEncoding_is_utf8_and_round_trips_turkish_text()
    {
        // [Task 15 / It-2 devir §5] Eski varsayım (sistem ANSI CP'si, GetACP()) bu toolchain'de (VS18/Roslyn
        // redirected pipe UTF-8 yazıyor) mojibake üretiyordu — bkz. MsBuildOutputEncodingTests.cs +
        // task-15-report.md. Value artık pure UTF-8 (BOM'suz, replacement fallback); GetACP()/NativeMethods
        // bağımlılığı kaldırıldı.
        Assert.Equal(Encoding.UTF8.CodePage, MsBuildOutputEncoding.Value.CodePage);

        const string turkish = "İstanbul: Özgün Çözüm Üretiliyor — Şükrü Iğdır";
        byte[] encoded = MsBuildOutputEncoding.Value.GetBytes(turkish);
        string roundTripped = MsBuildOutputEncoding.Value.GetString(encoded);
        Assert.Equal(turkish, roundTripped);
    }

    [SkippableFact] // Fix wave 1 / Finding 1 regresyon: post-build event MSBuild.exe'den sonra da yaşayan
                     // detached bir grandchild (ping.exe) bırakır — stdout/stderr pipe'ının write-end kopyasını
                     // MSBuild.exe çıktıktan SONRA da tutar. Fix ÖNCESİ RunChildAsync'in başarı yolundaki
                     // unbounded `Task.WhenAll(stdoutTask, stderrTask)` bu EOF'u hiç göremez (RED — dış
                     // WaitAsync(20s) TimeoutException fırlatır); fix SONRASI WaitPumpsBoundedAsync
                     // PostKillWait(5s) içinde döner (GREEN). Bkz. task-5-report.md "Fix wave 1" RED/GREEN kanıtı.
    public async Task LingeringPostBuildGrandchild_does_not_stall_success_path()
    {
        string exe = await ResolveMsBuildExeOrSkipAsync();
        string dir = NewTempDir();
        string csproj = LegacyFixture.CreateClassLibWithLingeringPostBuild(dir, "LingerLib", sleepSeconds: 60);

        using var job = JobObject.CreateKillOnClose(); // grandchild breakaway YAPAMAZ — test sonunda Dispose kaskadıyla temizlenir
        var invoker = new MsBuildInvoker(job, exe);
        var lines = new List<string>();

        var sw = Stopwatch.StartNew();
        var result = await invoker.InvokeAsync(
            new MsBuildInvokeRequest(csproj, "Debug", dir, NeedsRestore: false),
            line => { lock (lines) lines.Add(line); },
            CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20)); // hang'i sonsuza kadar değil, teste çevirir
        sw.Stop();

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.Killed);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15),
            $"başarı yolu sınırlı sürede dönmedi: {sw.Elapsed} (grandchild hâlâ ayakta olabilir — fix eksik/bozuk)");
    }

    [SkippableFact] // Fix wave 2 / Finding 1 regresyon: WaitPumpsBoundedAsync 5sn sonra pes eder ama pump GÖREVİ
                     // öldürülmez — grandchild MSBuild.exe'nin miras verdiği stdout pipe ucunu tutmaya devam
                     // eder; bir sonraki satırı yazdığında abandoned pump'ın ReadLineAsync'i tamamlanır ve
                     // onLine'ı InvokeAsync DÖNDÜKTEN SONRA çağırır. Task 9 bu anda ilişkili ProjectLogFile'ı
                     // zaten dispose etmiş olabilir (Task 4 fix'i: dispose sonrası AppendLine artık
                     // ObjectDisposedException fırlatıyor) — thread-pool thread'inde yakalayan olmayan bir
                     // exception Supervisor'ı düşürür.
                     // NOT: brief'in işaret ettiği ping.exe tabanlı fixture (CreateClassLibWithLingeringPostBuild)
                     // BU MAKİNEDE görünür bir "geç satır" ÜRETMİYOR — deneysel olarak doğrulandı: ping.exe
                     // pipe UCUNU açık tutuyor (WaitPumpsBoundedAsync'in 5sn sınırına gerçekten çarpılıyor,
                     // pump EOF'u ping çıkana kadar gecikiyor) FAKAT kendisi o pipe'a HİÇBİR BAYT yazmıyor
                     // (muhtemelen yönlendirilmiş/konsolsuz handle'da WriteConsole tabanlı I/O'su sessizce
                     // başarısız oluyor) — yalnız sessiz/geç EOF gözlenir, onLine hiç tetiklenmez, bu da
                     // Finding 1'in asıl iddiasını (geç bir SATIR yakalanması) bu fixture ile KANITLANAMAZ hale
                     // getirir. Bkz. task-5-report.md "Fix wave 2" — RED/GREEN kanıtı bu notta detaylandırıldı.
                     // Bunun yerine CreateClassLibWithLingeringPostBuildTextWriter kullanılır: grandchild
                     // (powershell.exe) kendi stdout'una DEĞİL, MSBuild.exe İÇİNDEN <c>GetStdHandle</c> ile
                     // okunan TAM handle DEĞERİNE argüman olarak alıp doğrudan <c>WriteFile</c> ile yazar —
                     // inherited handle DEĞERLERİ child'ta AYNI sayı kaldığından bizim pipe'ımıza garantili
                     // teslimat sağlar (bkz. LegacyFixture.cs XML doc'u). Grandchild'ın tam ne zaman öldüğü,
                     // aynı `job`'a bağlı IOCP ile DETERMİNİSTİK izlenir
                     // (D8 — sleep-poll YOK, test (c) ile aynı teknik): NEW_PROCESS bildirimleri PID İSMİNE göre
                     // süzülür ("powershell" — MSBuild.exe'nin kendisi ve CoreCompile'ın kısa ömürlü derleyici
                     // child'ı (csc.exe/VBCSCompiler) da job üyesi olabildiği için doğum SIRASINA güvenilmez).
                     // Fix ÖNCESİ (RED): en az bir satır AfterReturn=true kaydedilir. Fix SONRASI (GREEN): onLine,
                     // RunChildAsync dönmeden HEMEN ÖNCE aynı onLineLock altında latch edilir — geç çağrı YOK.
    public async Task LingeringPostBuildGrandchild_no_onLine_after_invoke_returns()
    {
        string exe = await ResolveMsBuildExeOrSkipAsync();
        string dir = NewTempDir();
        string csproj = LegacyFixture.CreateClassLibWithLingeringPostBuildTextWriter(dir, "LatchLib", seconds: 8);

        using var job = JobObject.CreateKillOnClose();
        using var iocp = job.AttachCompletionPort(); // Launch'tan ÖNCE bağlanmalı — kaçırılan bildirim olmasın
        var invoker = new MsBuildInvoker(job, exe);

        var recordedLock = new object();
        var recorded = new List<(string Line, bool AfterReturn)>();
        bool returned = false;

        var invokeTask = invoker.InvokeAsync(
            new MsBuildInvokeRequest(csproj, "Debug", dir, NeedsRestore: false),
            line => { lock (recordedLock) recorded.Add((line, returned)); },
            CancellationToken.None);

        // job'a katılan NEW_PROCESS bildirimlerini isme göre süz — MSBuild.exe'nin kendisi ve CoreCompile'ın
        // kısa ömürlü derleyici child'ı (csc.exe/VBCSCompiler, breakaway YAPMAZSA o da job üyesi olur)
        // "powershell" DEĞİLDİR, atlanır. Kısa ömürlü job üyeleri isim sorgusundan ÖNCE çıkmış olabilir
        // (ArgumentException) — bunlar da "powershell değil" sayılıp atlanır.
        int writerPid = -1;
        while (writerPid < 0)
        {
            var born = iocp.WaitNext(TimeSpan.FromSeconds(30)) ?? throw new TimeoutException("grandchild (powershell.exe) doğum bildirimi gelmedi");
            if (born.MessageId != NativeMethods.JOB_OBJECT_MSG_NEW_PROCESS) continue;
            try
            {
                if (string.Equals(Process.GetProcessById(born.Pid).ProcessName, "powershell", StringComparison.OrdinalIgnoreCase))
                    writerPid = born.Pid;
            }
            catch (ArgumentException) { /* zaten çıkmış kısa ömürlü job üyesi — powershell değil, atla */ }
        }

        var result = await invokeTask.WaitAsync(TimeSpan.FromSeconds(20));

        // InvokeAsync az önce döndü — bu andan sonraki HERHANGİ bir onLine çağrısı "geç" sayılır.
        lock (recordedLock) returned = true;

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.False(result.Killed);

        // grandchild'ın GERÇEKTEN çıktığını (son satırı yazıp pipe'ı kapattığı an) IOCP bildirimiyle bloklu
        // bekle — MSBuild.exe'nin kendi exit bildirimi de gelebilir, o pid'i ATLA, yalnız writerPid'i eşleştir.
        while (true)
        {
            var n = iocp.WaitNext(TimeSpan.FromSeconds(30)) ?? throw new TimeoutException("grandchild exit bildirimi gelmedi");
            if (n.Pid == writerPid && n.MessageId is NativeMethods.JOB_OBJECT_MSG_EXIT_PROCESS
                                                   or NativeMethods.JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS)
                break;
        }

        bool anyLateLine;
        lock (recordedLock) anyLateLine = recorded.Any(r => r.AfterReturn);
        Assert.False(anyLateLine, "InvokeAsync döndükten SONRA onLine çağrıldı — abandoned pump latch edilmedi");
    }
}
