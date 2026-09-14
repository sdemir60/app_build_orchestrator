using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using BuildOrchestrator.Core.ProcessControl;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/3. tur — performans] ÖLÇÜM — bir pin DEĞİL. Kullanıcı derleme sırasında makinenin "aşırı
/// donduğunu" bildirdi. Soru: perf profili (paralellik + öncelik + CPU tavanı) arayüz thread'inin tepki
/// süresini ne kadar etkiliyor?
///
/// <para><b>Model:</b> gerçek bir rebuild kullanıcının reposunun çıktılarını yeniden yazar, bu yüzden
/// kullanılmaz. Onun yerine derleyicinin iş yükü modellenir: <c>UseSharedCompilation=false</c> ile her MSBuild
/// projesi için taze bir derleyici süreci başlatır. İlk ölçüm bunu tek thread'li bir CPU yakıcıyla kaba biçimde
/// modeller (gerçek derleyiciyle ölçüm ikinci testtedir). Sonda
/// profilin paralelliği kadar CPU yakan süreç başlatır, onları ürünün KENDİ iç job'ına (aynı
/// <see cref="JobObject.SetPriorityClass"/> / <see cref="JobObject.SetCpuRate"/> yolu, aynı
/// <see cref="PerfProfile.For"/> tablosu) koyar ve bu sırada Normal öncelikli bir dispatcher thread'inin
/// (uygulamanın UI thread'i gibi) iş sıraya girdikten ne kadar sonra koşabildiğini ölçer. Bellek etkisi bu
/// modelde YOKTUR; o ayrıca raporlanır.</para>
///
/// <para>Varsayılan koşuda ÇALIŞMAZ: <c>BO_MEASURE_PERF=1</c> yoksa <c>Skip</c> (dakikalarca CPU yakar).</para>
/// </summary>
[Trait("Category", "Measurement")]
[Collection("Console UI (serial)")]
public sealed class PerfProfileUiLatencyMeasurementTests(ITestOutputHelper output)
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

    [SkippableFact]
    public async Task How_long_the_ui_thread_waits_under_each_profile()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_PERF") == "1",
            "Burns every core for over a minute — opt in with BO_MEASURE_PERF=1.");

        output.WriteLine(string.Format(CultureInfo.InvariantCulture,
            "logical processors: {0}", Environment.ProcessorCount));
        output.WriteLine("profile   | workers | priority    | cap  | ui lag p50 | p95     | max      | cpu total");

        output.WriteLine(await Sample("baseline", null));
        foreach (var mode in new[] { PerfMode.Full, PerfMode.Balanced, PerfMode.Light })
            output.WriteLine(await Sample(mode.ToString(), PerfProfile.For(mode)));
    }

    /// <summary>
    /// Aynı soru, bu kez GERÇEK derleyiciyle. Tek thread'li CPU yakıcıyla ölçüm FullPower'da bile gecikme
    /// göstermedi (altı yük sekiz çekirdeğe sığar) — ama <c>csc</c> çok thread'lidir ve derleme başına GB'larca
    /// bellek tutar. Burada profilin paralelliği kadar işçi, üretilmiş büyük bir C# kaynağını gerçek
    /// <c>csc.exe</c> ile döngüde derler; işçiler ürünün iç job'ındadır. UI gecikmesinin yanında boş fiziksel
    /// bellek ve commit de örneklenir — donmanın bellek baskısından mı CPU'dan mı geldiğini ayırmak için.
    /// </summary>
    [SkippableFact]
    public async Task How_long_the_ui_thread_waits_while_real_compilers_run()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_PERF") == "1",
            "Runs parallel real compilers for minutes — opt in with BO_MEASURE_PERF=1.");

        string msbuild = (await new BuildOrchestrator.Core.MsBuild.MsBuildResolver(new BuildOrchestrator.Core.Processes.ProcessRunner()).ResolveAsync()).MsBuildExePath;
        string csc = Path.Combine(Path.GetDirectoryName(msbuild)!, "Roslyn", "csc.exe");
        string work = Directory.CreateTempSubdirectory("bo-perf-csc-").FullName;
        string source = Path.Combine(work, "gen.cs");
        File.WriteAllText(source, GeneratedSource());

        output.WriteLine("profile   | workers | ui lag p50 | p95      | max        | cpu   | min free RAM | peak commit");
        output.WriteLine(await SampleCompilers("baseline", null, csc, source, work));
        foreach (var mode in new[] { PerfMode.Light, PerfMode.Balanced, PerfMode.Full })
            output.WriteLine(await SampleCompilers(mode.ToString(), PerfProfile.For(mode), csc, source, work));
    }

    private static async Task<string> SampleCompilers(string label, PerfProfile? profile, string csc, string source, string work)
    {
        using var job = JobObject.CreateKillOnClose();
        if (profile is { } p)
        {
            job.SetPriorityClass(p.Priority);
            if (p.CpuCapPercent is { } cap) job.SetCpuRate(cap);
            for (int i = 0; i < p.Parallelism; i++)
            {
                string loop = string.Format(CultureInfo.InvariantCulture,
                    "/c for /l %i in (1,1,1000) do @\"{0}\" -nologo -t:library -out:\"{1}\" \"{2}\"",
                    csc, Path.Combine(work, FormattableString.Invariant($"w{i}.dll")), source);
                using var worker = Process.Start(new ProcessStartInfo("cmd.exe", loop)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
                job.Assign(worker.Handle);
                _ = worker.StandardOutput.ReadToEndAsync();
            }
            await Task.Delay(TimeSpan.FromSeconds(8)); // derleyiciler belleğe otursun
        }

        double minFreeGb = double.MaxValue, peakCommitGb = 0;
        using var stop = new CancellationTokenSource();
        var memory = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                GlobalMemoryStatusEx(ref m);
                minFreeGb = Math.Min(minFreeGb, m.ullAvailPhys / 1e9);
                peakCommitGb = Math.Max(peakCommitGb, (m.ullTotalPageFile - m.ullAvailPageFile) / 1e9);
                try { await Task.Delay(500, stop.Token); } catch (OperationCanceledException) { }
            }
        });

        var cpuStart = CpuTimes();
        var lags = await StaThread.RunAsync(MeasureLag, "perf-ui-latency-csc");
        double cpu = CpuPercentSince(cpuStart);
        stop.Cancel();
        await memory;
        job.Terminate();

        lags.Sort();
        double P(double q) => lags[(int)Math.Min(lags.Count - 1, Math.Floor(q * lags.Count))];
        return string.Format(CultureInfo.InvariantCulture,
            "{0,-9} | {1,7} | {2,7:0.0} ms | {3,6:0.0} ms | {4,8:0.0} ms | {5,4:0} % | {6,9:0.0} GB | {7,8:0.0} GB",
            label, profile?.Parallelism ?? 0, P(0.5), P(0.95), lags[^1], cpu, minFreeGb, peakCommitGb);
    }

    /// <summary>Bağlayıcıyı (generic + LINQ) zorlayan, büyük bir projeyi temsil eden kaynak.</summary>
    private static string GeneratedSource()
    {
        var sb = new System.Text.StringBuilder("using System; using System.Collections.Generic; using System.Linq; namespace Gen {\n");
        for (int c = 0; c < 1500; c++)
        {
            sb.Append("public sealed class C").Append(c).Append("<T> where T : IComparable<T> {\n");
            for (int m = 0; m < 12; m++)
                sb.Append("  public IEnumerable<string> M").Append(m)
                  .Append("(IEnumerable<T> xs, int k) => xs.Where(x => x.CompareTo(default(T)) != 0).Select((x, i) => new { x, i, s = x.ToString() + i.ToString() }).GroupBy(a => a.i % (k + ")
                  .Append(m).Append(" + 1)).OrderBy(g => g.Key).SelectMany(g => g.Select(a => string.Concat(a.s, g.Key, \"")
                  .Append(c).Append("\")));\n");
            sb.Append("}\n");
        }
        return sb.Append("}\n").ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    private static async Task<string> Sample(string label, PerfProfile? profile)
    {
        using var job = JobObject.CreateKillOnClose();
        var workers = new List<Process>();
        if (profile is { } p)
        {
            job.SetPriorityClass(p.Priority);
            // Taze bir job'da kaldırılacak tavan yoktur (ClearCpuRate orada ERROR_INVALID_PARAMETER döner;
            // üretim aynı çağrıyı RunCoordinator'da try içinde yapar) — Full için hiçbir şey yazılmaz.
            if (p.CpuCapPercent is { } cap) job.SetCpuRate(cap);
            for (int i = 0; i < p.Parallelism; i++)
            {
                var burner = Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -Command \"while($true){}\"")
                { UseShellExecute = false, CreateNoWindow = true })!;
                job.Assign(burner.Handle);
                workers.Add(burner);
            }
            await Task.Delay(TimeSpan.FromSeconds(2)); // yük otursun
        }

        var cpuStart = CpuTimes();
        var lags = await StaThread.RunAsync(MeasureLag, "perf-ui-latency");
        double cpu = CpuPercentSince(cpuStart);

        foreach (var w in workers) { try { w.Kill(); } catch (InvalidOperationException) { } w.Dispose(); }

        lags.Sort();
        double P(double q) => lags[(int)Math.Min(lags.Count - 1, Math.Floor(q * lags.Count))];
        return string.Format(CultureInfo.InvariantCulture,
            "{0,-9} | {1,7} | {2,-11} | {3,4} | {4,7:0.0} ms | {5,5:0.0} ms | {6,6:0.0} ms | {7,5:0.0} %",
            label, profile?.Parallelism ?? 0, profile?.Priority.ToString() ?? "-",
            profile?.CpuCapPercent is { } c ? c + "%" : "-", P(0.5), P(0.95), lags[^1], cpu);
    }

    /// <summary>Normal öncelikli bir dispatcher thread'ine her <see cref="Tick"/>'te iş sıraya koyar ve o işin
    /// koşmaya başlamasına kadar geçen süreyi toplar — kullanıcının "donma" diye hissettiği gecikme.</summary>
    private static List<double> MeasureLag()
    {
        var lags = new List<double>();
        var dispatcher = Dispatcher.CurrentDispatcher;
        var frame = new DispatcherFrame();
        var clock = Stopwatch.StartNew();
        var end = clock.Elapsed + Window;

        using var timer = new Timer(_ =>
        {
            if (clock.Elapsed >= end) { dispatcher.BeginInvoke(() => frame.Continue = false); return; }
            var queuedAt = clock.Elapsed;
            dispatcher.BeginInvoke(DispatcherPriority.Input,
                () => lags.Add((clock.Elapsed - queuedAt).TotalMilliseconds));
        }, null, Tick, Tick);

        Dispatcher.PushFrame(frame);
        return lags;
    }

    /// <summary>İki <c>GetSystemTimes</c> örneği arasındaki sistem geneli işlemci doluluğu. Örnekler ölçüm
    /// penceresinin başında ve sonunda alınır — araya bekleme konmaz (D8: testte gerçek zamanlı bloklama yok).</summary>
    private static (long Idle, long Total) CpuTimes()
    {
        GetSystemTimes(out var idle, out var kernel, out var user);
        return (idle, kernel + user);
    }

    private static double CpuPercentSince((long Idle, long Total) start)
    {
        var end = CpuTimes();
        long idle = end.Idle - start.Idle, total = end.Total - start.Total;
        return total == 0 ? 0 : 100.0 * (total - idle) / total;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
}
