using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [spec 2026-09-18 §5.5 · Task 10 fix I1] Gerçek bir Supervisor başlatan her test İZOLE bir önbellekle başlatır
/// (<see cref="SupervisorSandbox"/> ya da kendi <c>--logs</c> klasörü). Motor açılışta uçuş defterini kurtarır;
/// argümansız başlayan bir test kullanıcının gerçek <c>run-inflight.json</c>'ını işlerdi.
///
/// <para><b>Taranan biçimler (kaynak metni, yorum satırları hariç):</b></para>
/// <list type="number">
/// <item><c>TestPaths.SupervisorExe</c> geçen her satır ya <c>new EngineHost(TestPaths.SupervisorExe</c> (başlatılmayan
/// VM konağı) ya da <c>"--logs"</c> taşır. İzole yardımcıların dosyaları (TestPaths, sandbox) ve bu guard hariç.</item>
/// <item>Bir üye (erişim belirteçli bir metot bildiriminden bir sonrakine kadar olan metin)
/// <c>new EngineHost(TestPaths.SupervisorExe</c> kuruyor VE <c>.StartAsync(</c> / <c>.RestartAsync(</c> /
/// <c>RestartEngineCommand</c> çağırıyorsa ihlaldir: başlatılan motor sandbox'tan gelmelidir
/// (<see cref="SupervisorSandbox.IsolatedEngineHost"/>).</item>
/// </list>
/// <para><b>Kör noktalar (bilinçli):</b> motoru bir yardımcı metotta kurup başka bir test üyesinde başlatmak;
/// exe yolunu bir değişkene alıp oradan başlatmak; <c>MainWindow</c>'un <c>Loaded</c>'da motoru kendiliğinden
/// başlatması (bugün <c>MainWindowHost</c> var olmayan bir exe verir); <c>Process.Start</c>'a elle kurulmuş
/// bir <c>ProcessStartInfo</c> vermek. Guard çağrının BİÇİMİNE bakar, niyetine değil — gerisi review'ın işidir.
/// <c>TestPaths.Psi</c> ise <c>logsDir</c>'i ZORUNLU alır; argümansız biçim derlenmez.</para>
/// </summary>
public sealed class SupervisorIsolationGuardTests
{
    private static readonly string[] AllowedFiles =
    [
        Path.Combine("BuildOrchestrator.Tests", "Supervisor", "SupervisorIpcTests.cs"),   // TestPaths tanımı
        Path.Combine("BuildOrchestrator.Tests", "Supervisor", "SupervisorSandbox.cs"),
        Path.Combine("BuildOrchestrator.Tests", "Supervisor", "SupervisorIsolationGuardTests.cs"),
    ];

    private static readonly Regex UnstartedHost = new(@"EngineHost\(TestPaths\.SupervisorExe");
    /// <summary>Bir üye bildiriminin başı (erişim belirteciyle başlayan metot/özellik) — taramanın birimi. Erişim
    /// belirteci taşımayan yerel fonksiyonlar bölmez, yani test gövdesindeki yardımcılar testin parçası sayılır.</summary>
    private static readonly Regex Member = new(@"^\s*(public|private|internal|protected)\b[^=;]*\(");
    private static readonly Regex StartsEngine = new(@"\.StartAsync\(|\.RestartAsync\(|RestartEngineCommand");

    [Fact]
    public void Every_test_that_starts_a_real_supervisor_isolates_its_cache()
    {
        var offenders = new List<string>();
        foreach (string file in RepoPaths.TestSourceFiles("*.cs"))
        {
            string relative = Path.GetRelativePath(RepoPaths.TestsRoot, file);
            if (AllowedFiles.Contains(relative, StringComparer.OrdinalIgnoreCase)) continue;
            offenders.AddRange(Scan(relative, File.ReadAllLines(file)));
        }

        Assert.True(offenders.Count == 0,
            "a real supervisor is started without an isolated cache (use SupervisorSandbox):\n  "
            + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> Scan(string file, string[] lines)
    {
        int memberStart = -1;
        bool constructs = false, starts = false;
        for (int i = 0; i <= lines.Length; i++)
        {
            bool atEnd = i == lines.Length;
            if (atEnd || Member.IsMatch(lines[i]))
            {
                if (constructs && starts) yield return $"{file}:{memberStart + 1} (a member starts an EngineHost built on TestPaths.SupervisorExe)";
                if (atEnd) yield break;
                memberStart = i;
                constructs = starts = false;
            }
            string line = lines[i];
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)) continue;

            if (UnstartedHost.IsMatch(line)) constructs = true;
            else if (line.Contains("TestPaths.SupervisorExe", StringComparison.Ordinal)
                     && !line.Contains("\"--logs\"", StringComparison.Ordinal))
                yield return $"{file}:{i + 1} (TestPaths.SupervisorExe without --logs)";
            if (StartsEngine.IsMatch(line)) starts = true;
        }
    }
}
