using System.IO;
using System.IO.Pipes;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Motorun <b>stderr</b>'i de yönlendirilmiş bir anonim pipe'tır (<c>JobProcessLauncher.Launch</c>,
/// <c>RedirectStdio: true</c> — stdin/stdout/stderr için ÜÇ ayrı pipe kurulur). App bugüne dek yalnız
/// stdout'u okuyordu; stderr'in okuyan tarafı hiç tüketilmiyordu. Pipe tamponu dolduğu anda motorun bir
/// sonraki <c>Console.Error.WriteLine</c> çağrısı SONSUZA DEK bloklanır.
///
/// <para><b>Ölçülen üretim vakası (kullanıcı bildirimi + disk kanıtı):</b> 177 projelik bir Build
/// (<c>run-20260804-141003-755</c>) Stop ile durduruldu. Hemen ardından başlatılan ikinci Build
/// (<c>run-20260804-141025-277</c>) planlama sırasında bayat-obj uyarılarını yazarken DONDU:
/// <c>decision.log</c>'a 4 satır düştü ve dosya bir daha hiç büyümedi, <c>run … started</c> satırı hiç
/// yazılmadı. Sonuç zinciri: <c>runStarted</c> gelmez → App <c>IsStarting</c> penceresinde asılı kalır
/// ("Build'e bastım hiçbir şey olmadı") → kullanıcı Stop'a basar → <c>TryRequestStop</c> run'ı
/// SAHİPLENİR (<c>_runActive</c> true, <c>_finishing</c> false) ve ack borcunu
/// <c>ExecuteRunAsync</c>'in finally'sine bırakır → o finally planlama bloklandığı için ASLA çalışmaz →
/// <c>runStopped</c> hiç yazılmaz → faz sonsuza dek <c>Stopping</c>. İki semptom, TEK kök neden.
///
/// <para><b>Neden sentetik pipe ile test ediliyor:</b> mekanizma "yazan taraf dolu bir tamponda bloklanır"
/// olgusudur ve motorun kendisine ihtiyaç duymaz. Gerçek motoru yeterince konuşturmak (300 projelik
/// sentetik workspace ~52 KB stderr üretiyor) MSBuild çözümlemesi + yüzlerce gerçek child process demektir;
/// o, Acceptance ağırlığıdır. Burada mekanizma pipe seviyesinde, kablaj ise gerçek motorla pinlenir.</para>
/// </summary>
public sealed class EngineStderrDrainTests
{
    /// <summary>Dolu bir pipe'ın yazan tarafını serbest bırakan TEK şey okunmasıdır. 1 MiB, herhangi bir
    /// pipe tamponundan (Windows varsayılanı birkaç KB) kat kat büyüktür: drain koşmuyorsa bu yazım ASLA
    /// tamamlanmaz — testin kırmızısı budur.</summary>
    [Fact]
    public async Task Draining_the_stderr_pipe_keeps_a_chatty_engine_from_blocking_forever()
    {
        using var readEnd = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
        var writeEnd = new AnonymousPipeClientStream(PipeDirection.Out, readEnd.GetClientHandleAsString());

        var chatter = Task.Run(() =>
        {
            writeEnd.Write(new byte[1024 * 1024]); // motorun tanı seli — tamponun kat kat üstü
            writeEnd.Dispose();
        });

        var drain = EngineHost.DrainEngineStderrAsync(readEnd);

        // Drain koşmuyorsa tampon dolar ve bu yazım ASLA tamamlanmaz — testin kırmızısı burasıdır.
        await chatter.WaitAsync(TimeSpan.FromSeconds(10));
        readEnd.DisposeLocalCopyOfClientHandle(); // yazan uçların SONUNCUSU da kapandı → EOF
        await drain.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>Mekanizma doğru olsa da KABLOLANMAMIŞSA kusur durur: <see cref="EngineHost.StartAsync"/>
    /// stderr drain'ini stdout okuma döngüsüyle BİRLİKTE başlatmalıdır. Gerçek Supervisor ile pinlenir;
    /// drain, child yaşadığı sürece koşmaya devam eder (EOF beklediği için tamamlanmaz).</summary>
    [Fact]
    public async Task Starting_the_engine_also_starts_the_stderr_drain()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe, TimeSpan.FromSeconds(30));
        await engine.StartAsync();

        Assert.NotNull(engine.StderrDrain);
        Assert.False(engine.StderrDrain!.IsCompleted); // motor canlı → pipe akışta tutuluyor
    }
}
