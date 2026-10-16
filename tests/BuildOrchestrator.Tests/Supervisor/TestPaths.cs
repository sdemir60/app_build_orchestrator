using System.Diagnostics;
using System.IO;
using BuildOrchestrator.Supervisor;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [B1 fix 3 · kendi dosyasında] Gerçek Supervisor'ı süreç olarak başlatan ya da onun yerine konan testlerin ORTAK yolları, süzgeci ve süre
/// sınırları — tek yer (kopya YASAK): ikilinin yolu ve yokluğun yolu, yeni doğan motorun süzgeci, geniş başlatma/koşu sınırları, yetim bütçesi,
/// eski havuzun test kökü ve stdio yönlendirmeli başlatma. Kullananları hem Supervisor hem App testleridir.
/// </summary>
public static class TestPaths
{
    public static string SupervisorExe => Path.Combine(AppContext.BaseDirectory, "BuildOrchestrator.Supervisor.exe");

    /// <summary>[idle-memory · dalga 3 · B1 fix 1 · m-3] Motoru erişilemez gösteren testlerin Supervisor yolu — erişilemezlik satırının ve
    /// istisnanın taşıdığı TEK değer. Var olması gerekmez: konak sahtedir ya da hiç başlamaz.</summary>
    public const string AbsentSupervisorExe = @"C:\app\supervisor\BuildOrchestrator.Supervisor.exe";

    /// <summary>[idle-memory · dalga 3 · B1 fix 1 · I-3 · B1 fix 2 · 4c] YENİ doğan motorun süzgeci: gerçek bir Supervisor (imaj adı
    /// <see cref="SupervisorExe"/>'inki) ve <paramref name="oldPid"/> değil. Job'a doğan süreçlerden motoru seçen testler bunu kullanır
    /// (<c>JobBirths.WaitForProcess</c>): job'da başka üyeler de vardır — motorun conhost'u ve, henüz çıkmadıysa, uyutulan ya da öldürülen eski motor
    /// (imajı aynıdır; port bağlandığında o da doğum olarak bildirilir). Okunamayan ad (süreç çıktı) <see cref="InvalidOperationException"/> atar;
    /// yardımcı onu "uymuyor" sayar.</summary>
    public static Func<Process, bool> IsSupervisorOtherThan(int oldPid) => process =>
        process.Id != oldPid
        && string.Equals(process.ProcessName, Path.GetFileNameWithoutExtension(SupervisorExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>[B1/F1 · fix-1] GERÇEK bir Supervisor process'i başlatan her testin <see cref="SupervisorExe"/>
    /// ile birlikte <c>EngineHost</c>'a ENJEKTE ETTİĞİ startup timeout. Üretim varsayılanı (5 sn,
    /// <c>EngineHost.StartupTimeout</c>) yük altında yetmiyor: taze bir process doğup <c>engineReady</c>
    /// yazana kadar 5 sn'yi aşabiliyor ve test SEBEPSİZ kırmızı veriyor (ölçüm: task-B1-report.md İŞ 4,
    /// yük altındaki koşum). <b>Üretim varsayılanı DEĞİŞMEZ</b> — donmuş bir supervisor'da uygulamanın
    /// vazgeçmesi şart; genişleyen yalnız TEST beklemesidir.
    /// <para>Tek yer: aksi halde aynı sabit <c>EngineHostTests</c>/<c>RunViewModelTests</c>/
    /// <c>AppShutdownTests</c>'te üç ayrı kopya olarak yaşardı ve bir sonraki start-eden test yine
    /// yamasız kalırdı (fix-1 öncesi tam olarak bu oldu — 8 start noktasının yalnız 3'ü yamalıydı).</para></summary>
    public static readonly TimeSpan WideStartupTimeout = TimeSpan.FromSeconds(60);

    /// <summary>[cycle rounds] GERÇEK bir run'ın TAMAMLANMASINI bekleyen e2e testlerinin hang-guard'ı — bir
    /// PERF BÜTÇESİ DEĞİL, sonsuz beklemeyi test hatasına çeviren üst sınır (iddiaların hiçbiri süreye
    /// bakmaz). Tek kullanıcısı <c>RunViewModelTests.Rebuild_wires_through_the_real_engine_and_populates_rows</c>.
    /// <b>Bu testte <c>MSBuild.exe</c> child'ları GERÇEKTEN doğar:</b> Rebuild döngü gruplarını da turlarla derler
    /// (<c>CycleCompilation</c>), testin X↔Y cycle fixture'ındaki iki üye 2 tur × 2 invoke koşar (testin kendi yorumu
    /// aynı gerekçeyi taşır). Guard bu yüzden geniş tutulur: test GERÇEK bir
    /// Supervisor process'ini uçtan uca çalıştırır (spawn + <c>engineReady</c> el sıkışması + IPC + MSBuild
    /// toolset çözümü) ve TÜM süit paralel koşarken bu adımların kendisi yavaşlayabilir. Aynı iş yükünü
    /// bekleyen kardeş e2e testi (<c>RunCoordinatorTests</c>) zaten 30 sn'lik bir guard kullanıyor ve yük
    /// altında geçiyor; buradaki pay <see cref="WideStartupTimeout"/> ile aynı tutuldu — bu test AYRICA gerçek
    /// bir EngineHost başlatır.</summary>
    public static readonly TimeSpan WideRunTimeout = TimeSpan.FromSeconds(60);

    /// <summary>[§3/D8 kabul] "App ölür → ≤2s, orphan yok": tetikten (outer Job'un kapanışı ya da uygulamanın çıkışı)
    /// sonra process ağacının TAMAMEN ölmesi için tanınan bütçe. Yukarıdaki iki geniş sınırın aksine bir hang-guard
    /// DEĞİL, kabul ölçütünün kendisidir — gevşetilmez. Tek sahibi burası; ölçümü
    /// <see cref="ProcessTree.AssertNoOrphansAsync"/> yapar. <c>AppShutdown.DisposalTimeout</c>'tan AYRIDIR: o,
    /// OnExit'in disposal'ı bekleme tavanıdır; bu, o bekleyiş döndükten sonra ağacın ölme süresi.</summary>
    public static readonly TimeSpan OrphanBudget = TimeSpan.FromSeconds(2);

    /// <summary>[final review M8] Eski worktree havuzunun TEST kökü: diskte OLMAYAN, süreç başına tek bir geçici yol.
    /// Motorun hazır oluşunu (<c>RunViewModel.OnEngineReady</c>) yaşayan her VM testi <c>LegacyWorktreePoolRoot</c>'u
    /// buna bağlar — aksi hâlde ipucu kararı kullanıcının GERÇEK <c>%LOCALAPPDATA%</c> klasörüne bakar ve test
    /// makineye göre değişirdi.</summary>
    public static string MissingLegacyPoolRoot { get; } =
        Path.Combine(Path.GetTempPath(), "bo-no-legacy-pool-" + Guid.NewGuid().ToString("N"));

    /// <summary>Gerçek Supervisor process'ini stdio yönlendirmeli başlatır (RunCoordinatorTests da kullanır).
    /// <paramref name="logsDir"/> ZORUNLUDUR: argümansız bir Supervisor kullanıcının gerçek önbelleğini
    /// (<c>%LOCALAPPDATA%</c>) kullanır ve açılışta onun <c>run-inflight.json</c>'ını kurtarırdı (spec 2026-09-18
    /// §5.5). Kendi klasörü olmayan testler <see cref="SupervisorSandbox"/> kullanır.</summary>
    /// <param name="debugHooks">[A13/B4] <c>debugSpawnChildren</c> kancasını açar. Varsayılan <c>false</c> =
    /// ÜRETİM yolu: kanca kapalıdır ve komut <c>error(debugHooksDisabled)</c> ile reddedilir.</param>
    public static ProcessStartInfo Psi(string logsDir, bool debugHooks = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(logsDir);
        var psi = new ProcessStartInfo(SupervisorExe)
        { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("--logs");
        psi.ArgumentList.Add(logsDir);
        if (debugHooks) psi.ArgumentList.Add(SupervisorHost.DebugHooksArg);
        return psi;
    }

}
