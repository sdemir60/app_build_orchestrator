using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T49 fix round 1 · A3 · D8] Sleep yasağının kaynak-tarayan guard'ı — <see cref="NoHardcodedColorTests"/> ve
/// <see cref="NoHardcodedMotionTests"/> ile AYNI kalıp, ama TÜM üretim projelerini tarar (App + Core +
/// Supervisor + Contracts), çünkü D8 bir UI kuralı değil proje kuralıdır.
///
/// <para><b>Neden var:</b> D8 ("sleep-poll / sabit <c>Thread.Sleep</c> YASAK — deterministik seam + IOCP/TCS
/// randevusu") bu projenin en sık ihlal edilen bağlayıcı yasağıdır; bu iterasyonda bir ihlali
/// (<c>BuildStateStore.MoveAtomicWithRetry</c>'daki sabit <c>Thread.Sleep(5)</c>) gerçekten bulundu ve
/// düzeltildi. Renk ve süre için guard eklenip D8 için eklenmemesi, en pahalı yasağı en zayıf korumada
/// bırakırdı.</para>
///
/// <para><b>İzin listesi DAR ve GEREKÇELİ:</b> dosya + o dosyada BEKLENEN adet. Hiçbiri sleep-poll değildir;
/// hepsi ya enjekte edilebilir bir dikişin ÜRETİM VARSAYILANI ya da bir tick/backoff'tur. Adet de pinlenir:
/// izinli bir dosyaya İKİNCİ bir sleep eklemek de guard'ı kırmızıya çeker — izin "bu dosya serbest" demek
/// değildir, "bu BİR kullanım gerekçeli" demektir.</para>
///
/// <para>[fix round 2] <b>Test ağacı da taranır.</b> D8 "testte gerçek zaman beklenmez" der — yasağın en sık
/// ihlal edildiği yer testlerdir. Test tarafında yalnız BLOKLAYAN biçimler yasaklanır (<c>Thread.Sleep</c>,
/// <c>SpinWait.SpinUntil</c>, <c>Start-Sleep</c>); <c>Task.Delay</c> testlerde meşru bir araçtır
/// (<c>Task.Delay(Timeout.Infinite)</c> = hiç tick atmayan sahte batcher, <c>WaitAsync</c> zaman aşımı tavanı)
/// ve yasaklanması guard'ı gürültüye boğardı — o yüzden test kuralı üretim kuralından DAR tutulur.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınırlar):</b></para>
/// <list type="bullet">
/// <item>Senkron sarmalanmış async gecikme: <c>Task.Delay(50).Wait()</c> / <c>.GetAwaiter().GetResult()</c> —
/// üretim ağacında <c>Task.Delay</c> zaten yakalanır ama test ağacında bu biçim GÖRÜNMEZ.</item>
/// <item>Handle üzerinden zaman aşımıyla bekleme: <c>ManualResetEvent(Slim).Wait(ms)</c>,
/// <c>WaitHandle.WaitOne(ms)</c>, <c>Monitor.Wait(ms)</c>, <c>Task.WhenAny(..., Task.Delay(...))</c>.</item>
/// <item><c>new SpinWait().SpinOnce()</c> (yalnız <c>SpinWait.SpinUntil</c> aranır) ve <c>Thread.Yield()</c>
/// döngüsü.</item>
/// <item>Gecikmeyi bir yardımcının ARDINA saklamak (<c>await Pause(50)</c>) — guard çağrı biçimine bakar.</item>
/// <item><b>En önemlisi:</b> izinli/meşru bir gecikmeyi bir DÖNGÜYE koyup gerçek sleep-poll'e çevirmek. Guard
/// "uyudun mu" der, "neden uyudun" demez; niyetin denetimi review'ın işidir.</item>
/// </list>
/// </summary>
public sealed class NoSleepPollTests
{
    /// <summary>Gecikme üreten çağrı biçimleri. Yorum satırları taramada ELENİR (bkz.
    /// <see cref="SourceGuard.ScanText"/>): bir yasağı ANLATAN doküman satırı onu ihlal etmez.</summary>
    private static readonly Regex SleepCall = new(
        "(?<![A-Za-z0-9_.])(?:System\\.Threading\\.)?Thread\\.Sleep\\s*\\(" +
        "|(?<![A-Za-z0-9_.])Task\\.Delay\\s*\\(" +
        "|(?<![A-Za-z0-9_.])SpinWait\\.SpinUntil\\s*\\(" +
        "|Start-Sleep",
        RegexOptions.Compiled);

    /// <summary>
    /// Üretimde gecikmesi MEŞRU olan yollar: göreli yol → o dosyada beklenen kullanım ADEDİ.
    /// Her satır bir gerekçe taşır; gerekçesi olmayan bir sleep buraya YAZILMAZ, kaldırılır.
    /// </summary>
    private static readonly Dictionary<string, int> AllowedSleeps = new(StringComparer.OrdinalIgnoreCase)
    {
        // Rename retry backoff'unun ÜRETİM VARSAYILANI (T49). Beklenen olay başka bir process'in okuma
        // handle'ını kapatmasıdır — beklenecek handle/TCS yok; gecikme enjekte edilebilir (RenameRetryDelay).
        [@"BuildOrchestrator.Core\State\BuildStateStore.cs"] = 1,
        // Clipboard contention retry'ının üretim varsayılanı: WPF Clipboard UI thread'inde kilitlenir,
        // beklenecek bir handle yoktur; gecikme yine enjekte edilebilir (ClipboardRetry.Try imzası).
        [@"BuildOrchestrator.App\Console\ClipboardRetry.cs"] = 1,
        // Named-pipe yeniden bağlanma backoff'unun üretim varsayılanı — StartListening'e dikiş olarak verilir.
        [@"BuildOrchestrator.App\Shell\SingleInstance.cs"] = 1,
        // ConsoleBatcher tick'inin üretim varsayılanı (~50ms toplu boşaltma penceresi) — DI'da dikiş.
        [@"BuildOrchestrator.App\App.xaml.cs"] = 1,
        // MSB302x contention retry'ının üretim varsayılanı — RunCoordinator'ın retryDelay dikişi.
        [@"BuildOrchestrator.Supervisor\RunCoordinator.cs"] = 1,
        // Test fixture'ı olarak SPAWN EDİLEN child process'in KENDİ komutu (powershell Start-Sleep); bizim
        // kodumuz uyumaz — uzun yaşayan bir çocuk gerektiğinde onu ayakta tutan şeydir.
        [@"BuildOrchestrator.Supervisor\SupervisorHost.cs"] = 1,
    };

    [Fact]
    public void No_production_source_introduces_a_sleep_outside_the_documented_allow_list()
    {
        var offenders = SourceGuard.ScanSrc("*.cs", SleepCall, skipCommentLines: true)
            .GroupBy(o => o[..o.IndexOf(':')], StringComparer.OrdinalIgnoreCase)
            .Where(g => !AllowedSleeps.TryGetValue(g.Key, out int allowed) || g.Count() != allowed)
            .SelectMany(g => g)
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>Test ağacında yasaklanan BLOKLAYAN biçimler — <c>Task.Delay</c> BİLEREK yok (bkz. sınıf özeti).</summary>
    private static readonly Regex BlockingSleepCall = new(
        "(?<![A-Za-z0-9_.])(?:System\\.Threading\\.)?Thread\\.Sleep\\s*\\(" +
        "|(?<![A-Za-z0-9_.])SpinWait\\.SpinUntil\\s*\\(" +
        "|Start-Sleep",
        RegexOptions.Compiled);

    /// <summary>
    /// Test ağacındaki meşru yollar. İkisi de "bizim thread'imiz uyumuyor" kategorisindedir, bu yüzden üretim
    /// listesinden farklı olarak ADET pinlenmez (sayının sinyal değeri yok):
    /// <list type="number">
    /// <item>guard'ın KENDİ dosyası — yasakladığı kalıpların katalogunu (regex + <c>[InlineData]</c> + sahte
    /// kaynak) zorunlu olarak içerir; <c>Resources/Tokens.xaml</c>'ın renk guard'ındaki istisnasıyla aynı
    /// gerekçe: literaller oraya AİTTİR;</item>
    /// <item>SPAWN EDİLEN child process'lerin KENDİ komut metinleri (<c>powershell … Start-Sleep</c>) — uzun
    /// yaşayan bir çocuk gerektiren fixture'lar (job-object kaskadı, process timeout/cancel testleri).</item>
    /// </list>
    /// </summary>
    private static readonly string[] AllowedTestFiles =
    [
        Path.Combine("BuildOrchestrator.Tests", "App", "NoSleepPollTests.cs"),          // guard'ın kendi kataloğu
        Path.Combine("BuildOrchestrator.Tests", "MsBuild", "LegacyFixture.cs"),         // sahte MSBuild hedefi (child)
        Path.Combine("BuildOrchestrator.Tests", "ProcessControl", "JobTestChildren.cs"),// job kaskadı için uzun yaşayan child
        Path.Combine("BuildOrchestrator.Tests", "Processes", "ProcessRunnerTests.cs"),  // timeout/cancel için uzun yaşayan child
    ];

    [Fact]
    public void No_test_blocks_the_thread_to_wait_for_real_time()
        => Assert.Empty(SourceGuard.ScanTests("*.cs", BlockingSleepCall, AllowedTestFiles, skipCommentLines: true));

    [Fact]
    public void The_guard_actually_scans_every_production_project()
    {
        // Tarama boş/eksik dönerse yukarıdaki test SESSİZCE yeşil kalırdı. D8 App'e özgü OLMADIĞI için
        // dört projenin de taramaya girdiği ayrıca pinlenir.
        var scanned = SourceGuard.ScannedSrcFiles("*.cs");

        Assert.Contains(@"BuildOrchestrator.Core\State\BuildStateStore.cs", scanned);
        Assert.Contains(@"BuildOrchestrator.Supervisor\RunCoordinator.cs", scanned);
        Assert.Contains(@"BuildOrchestrator.App\MainWindow.xaml.cs", scanned);
        Assert.Contains(@"BuildOrchestrator.Contracts\Ipc\NdjsonFraming.cs", scanned);
        Assert.All(AllowedSleeps.Keys, path => Assert.Contains(path, scanned));

        // [fix round 2] Test ağacı taraması da gerçekten dosya görüyor mu?
        var scannedTests = SourceGuard.ScannedTestFiles("*.cs");
        Assert.Contains(Path.Combine("BuildOrchestrator.Tests", "State", "BuildStateStoreTests.cs"), scannedTests);
        Assert.All(AllowedTestFiles, path => Assert.Contains(path, scannedTests));
    }

    [Theory]
    [InlineData("Thread.Sleep(5);", true)]
    [InlineData("System.Threading.Thread.Sleep(DefaultDelayMs);", true)]
    [InlineData("await Task.Delay(50, ct);", true)]
    [InlineData("SpinWait.SpinUntil(() => done);", true)]
    [InlineData("\"powershell -NoProfile -Command Start-Sleep -Seconds 300\"", true)]
    [InlineData("await _ready.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);", false)] // TCS randevusu — D8'in İSTEDİĞİ
    [InlineData("_writeGate.Wait();", false)]                                        // semafor — uyku değil
    [InlineData("retryDelay(delay, token)", false)]                                  // enjekte edilmiş dikiş
    public void Regex_separates_sleeps_from_deterministic_rendezvous(string sample, bool isSleep)
        => Assert.Equal(isSleep, SleepCall.IsMatch(sample));

    /// <summary>
    /// [fix round 1 · A3] <b>Guard'ın kendisinin kanıtı.</b> Sahte bir kaynakta iki sleep kurulur; ikisi de
    /// (yalnız ilki değil) doğru satırla raporlanır ve yorum satırındaki anlatım sayılmaz.
    /// </summary>
    [Fact]
    public void The_guard_reports_every_sleep_and_ignores_prose_about_sleeping()
    {
        const string fake = """
            internal static class Fake
            {
                // D8: Thread.Sleep(5) YASAK — bu satır yalnızca yasağı ANLATIYOR
                private static void A() => Thread.Sleep(5);
                private static Task B() => Task.Delay(40);
            }
            """;

        var offenders = SourceGuard.ScanText("Fake.cs", fake, SleepCall, skipCommentLines: true);

        Assert.Equal(2, offenders.Count);
        Assert.StartsWith("Fake.cs:4: ", offenders[0]);
        Assert.StartsWith("Fake.cs:5: ", offenders[1]);
    }
}
