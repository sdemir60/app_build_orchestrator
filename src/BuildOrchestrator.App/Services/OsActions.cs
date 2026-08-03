using System.Diagnostics;
using System.IO;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [E1/T67] Satır hover ikonlarının OS eylemleri: Reveal in Explorer / Open in Visual Studio / PickFolder.
/// İki dış dünya dokunuşu TEST SEAM'İ arkasındadır — gerçek Explorer/VS/dialog testte AÇILMAZ:
/// <list type="bullet">
/// <item><see cref="IProcessLauncher"/>: fire-and-forget başlatma; <c>explorer.exe /select,"&lt;yol&gt;"</c> ve
/// <c>devenv "&lt;sln&gt;"</c> tırnak kaçışını gerçek process olmadan doğrulatır.</item>
/// <item><see cref="IProcessRunner"/> (Core.Processes, mevcut): vswhere sorgusu — <see cref="MsBuildResolver"/>
/// deseni (<c>-property productPath</c>). İkinci bir runner seam'i açılmaz.</item>
/// </list>
/// 0/1/N solution dalları SAF mantıktır ve gerçek bir VS kurulumuna bağlı DEĞİLDİR.
/// </summary>
public interface IOsActions
{
    /// <summary><c>explorer.exe /select,"&lt;path&gt;"</c> — tırnak ŞART (boşluklu yol iki argümana bölünmesin;
    /// güvenlik/parse). Verbatim konsol notu VM'in sorumluluğudur, burada değil.</summary>
    void RevealInExplorer(string path);

    /// <summary>0 aday → <see cref="OpenInVsOutcome.NoSolution"/>; &gt;1 → <see cref="OpenInVsOutcome.NeedsChoice"/>
    /// (launch/vswhere YOK, seçim üst katmanda); tam 1 → vswhere ile devenv çözülür ve <c>devenv "&lt;sln&gt;"</c>
    /// başlatılır (bulunamazsa <see cref="OpenInVsOutcome.VisualStudioNotFound"/>).
    ///
    /// <para><b>ASENKRONDUR ve öyle KALMALIDIR.</b> Bu yol bir satırın hover ikonundan, yani UI thread'inden
    /// çağrılır; içindeki <c>vswhere</c> sorgusu soğuk makinede saniyeler sürebilir (spec timeout'u 30 s).
    /// Eskiden <c>Task.Run(...).GetAwaiter().GetResult()</c> ile senkron bekleniyordu ve tek bir tıklama
    /// pencereyi 30 saniyeye kadar ölü bırakabiliyordu.</para></summary>
    Task<OpenInVsResult> OpenInVisualStudioAsync(IReadOnlyList<SolutionRef> candidates);

    /// <summary><see cref="Microsoft.Win32.OpenFolderDialog"/> — iptal edilirse null.</summary>
    string? PickFolder(string? initial);
}

/// <summary>[E1/T67] Fire-and-forget process başlatma seam'i. Gerçek impl <see cref="ProcessLauncher"/> =
/// <see cref="Process.Start(ProcessStartInfo)"/>; testler <c>CaptureLauncher</c> ile FileName+Arguments'ı gerçek
/// process AÇMADAN yakalar.</summary>
public interface IProcessLauncher
{
    void Launch(ProcessStartInfo startInfo);
}

/// <summary>Gerçek başlatıcı: <see cref="Process.Start(ProcessStartInfo)"/>. Yalnız composition root'ta enjekte
/// edilir; testlerde CaptureLauncher kullanılır.</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    public void Launch(ProcessStartInfo startInfo) => Process.Start(startInfo);
}

/// <summary>[E1/T67] Open-in-VS dal sonucu.</summary>
public enum OpenInVsOutcome { NoSolution, Opened, NeedsChoice, VisualStudioNotFound }

/// <summary>[E1/T67] <see cref="IOsActions.OpenInVisualStudio"/> sonucu: dal (<see cref="Outcome"/>) + (NeedsChoice
/// dalında) seçtirilecek <see cref="Candidates"/>. Factory'lerle kurulur.</summary>
public sealed class OpenInVsResult
{
    public OpenInVsOutcome Outcome { get; }
    public IReadOnlyList<SolutionRef> Candidates { get; }

    private OpenInVsResult(OpenInVsOutcome outcome, IReadOnlyList<SolutionRef> candidates)
    {
        Outcome = outcome;
        Candidates = candidates;
    }

    /// <summary>Projeye bağlı solution yok — launch/vswhere'e dokunulmaz.</summary>
    public static OpenInVsResult NoSolution { get; } = new(OpenInVsOutcome.NoSolution, []);

    /// <summary>vswhere devenv'i çözemedi (VS/Build-Tools-only) — opened notu YAZILMAZ.</summary>
    public static OpenInVsResult VisualStudioNotFound { get; } = new(OpenInVsOutcome.VisualStudioNotFound, []);

    /// <summary>Tam 1 solution çözülüp devenv başlatıldı.</summary>
    public static OpenInVsResult Opened(SolutionRef solution) => new(OpenInVsOutcome.Opened, [solution]);

    /// <summary>&gt;1 solution — seçim üst katmana (VM/ProjectRow chooser) taşınır.</summary>
    public static OpenInVsResult Choose(IReadOnlyList<SolutionRef> candidates) =>
        new(OpenInVsOutcome.NeedsChoice, candidates);
}

/// <inheritdoc cref="IOsActions"/>
public sealed class OsActions : IOsActions
{
    // [E1] vswhere devenv sorgusu YALNIZ tam VS IDE kurulumlarını (Enterprise/Professional/Community) hedefler —
    // Build Tools ürünü BİLEREK dışarıda (onun productPath'i devenv.exe DEĞİLDİR): Build-Tools-only makinede sorgu
    // boş döner → VisualStudioNotFound. Sürüm-bağımsız (VS 2022/Preview/… aynı ürün Id'leri). MsBuildResolver'ın
    // "-property" + vswhere deseniyle hizalı; çıktı = productPath (devenv.exe tam yolu).
    private static readonly string[] VsProducts =
    [
        "Microsoft.VisualStudio.Product.Enterprise",
        "Microsoft.VisualStudio.Product.Professional",
        "Microsoft.VisualStudio.Product.Community",
    ];

    private readonly IProcessLauncher _launcher;
    private readonly IProcessRunner _runner;
    private readonly string _vswherePath;

    public OsActions(IProcessLauncher launcher, IProcessRunner runner, string? vswherePath = null)
    {
        _launcher = launcher;
        _runner = runner;
        _vswherePath = vswherePath ?? MsBuildResolver.DefaultVswherePath;
    }

    public void RevealInExplorer(string path) =>
        _launcher.Launch(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            // /select TEK argüman + tırnaklı yol — elle birleştirme ZORUNLU (explorer'ın kendi parse'ı; ArgumentList
            // "/select," ile yolu ayrı token yapıp bozardı). Tırnak boşluklu yol için ŞART.
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = false,
        });

    public async Task<OpenInVsResult> OpenInVisualStudioAsync(IReadOnlyList<SolutionRef> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0) return OpenInVsResult.NoSolution;      // launcher/vswhere'e DOKUNMA
        if (candidates.Count > 1) return OpenInVsResult.Choose(candidates); // seçim üst katmanda — launch/vswhere YOK

        var solution = candidates[0];
        string? devenv = await ResolveDevenvAsync().ConfigureAwait(true); // devam UI thread'inde (launcher oradan çağrılır)
        if (string.IsNullOrEmpty(devenv)) return OpenInVsResult.VisualStudioNotFound;

        _launcher.Launch(new ProcessStartInfo
        {
            FileName = devenv,
            Arguments = $"\"{solution.Path}\"", // devenv "<sln yolu>" — tırnaklı
            UseShellExecute = false,
        });
        return OpenInVsResult.Opened(solution);
    }

    public string? PickFolder(string? initial)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select repository root" };
        if (!string.IsNullOrEmpty(initial)) dialog.InitialDirectory = initial;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    /// <summary>vswhere ile devenv.exe tam yolunu çözer; vswhere yoksa / sorgu boş dönerse null
    /// (= <see cref="OpenInVsOutcome.VisualStudioNotFound"/>).
    ///
    /// <para><b>Oturum başına BİR KEZ:</b> kurulu Visual Studio'nun yolu uygulama çalışırken değişmez, bu yüzden
    /// sonuç (başarısızlık dahil) saklanır — ikinci bir "Open in VS" tıklaması <c>vswhere</c>'i yeniden
    /// çalıştırmaz. Saklanan şey <see cref="Task{TResult}"/>'in KENDİSİDİR: uçuştaki bir sorgu sırasında gelen
    /// ikinci çağrı yeni bir process başlatmaz, aynı sonucu bekler.</para></summary>
    private Task<string?>? _devenvPath; // yalnız UI thread'inden okunup yazılır (satır hover eylemleri)

    private Task<string?> ResolveDevenvAsync() => _devenvPath ??= QueryDevenvAsync();

    private async Task<string?> QueryDevenvAsync()
    {
        if (!File.Exists(_vswherePath)) return null; // vswhere yok → sorgu KOŞMAZ

        var args = new List<string> { "-latest", "-products" };
        args.AddRange(VsProducts);
        args.Add("-property");
        args.Add("productPath");
        var spec = new ProcessSpec(_vswherePath, args, Timeout: TimeSpan.FromSeconds(30));

        ProcessResult result;
        try { result = await _runner.RunAsync(spec).ConfigureAwait(false); }
        catch { return null; } // vswhere başlatılamadı/çöktü → VisualStudioNotFound olarak ele al

        if (!result.Success) return null;
        string? path = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
