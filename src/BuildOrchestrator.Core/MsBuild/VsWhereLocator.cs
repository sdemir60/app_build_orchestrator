using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>Bir vswhere aramasının nasıl bittiği.</summary>
public enum VsWhereOutcome
{
    /// <summary>Aranan yürütülebilir bulundu ve diskte var.</summary>
    Found,
    /// <summary>vswhere.exe'nin kendisi yok — Visual Studio / Build Tools kurulu değil.</summary>
    LocatorMissing,
    /// <summary>vswhere çalıştı ama hata verdi (ya da zaman aşımına uğradı).</summary>
    LocatorFailed,
    /// <summary>vswhere çalıştı, aranan yürütülebilir kurulumda yok.</summary>
    NotFound,
}

/// <param name="Outcome">Aramanın sonucu.</param>
/// <param name="Path">Bulunan yürütülebilirin tam yolu; bulunamadıysa null.</param>
/// <param name="Detail">Hata durumunda çağıranın mesajına gömeceği ayrıntı (exit kodu + stderr); yoksa null.</param>
public sealed record VsWhereResult(VsWhereOutcome Outcome, string? Path, string? Detail);

/// <summary>
/// Visual Studio kurulumunda bir yürütülebilir arayan TEK yüzey. MSBuild.exe ve tf.exe aynı aramayı
/// yapar — arama yalnız <c>-find</c> desenlerinde ayrışır, o yüzden çağrı mantığı ikinci bir yerde
/// yazılmaz; çağıranlar sonucu kendi alanlarının diliyle bir hataya çevirir.
/// </summary>
public sealed class VsWhereLocator(IProcessRunner runner)
{
    private static readonly TimeSpan LocatorTimeout = TimeSpan.FromSeconds(30);

    /// <summary>vswhere.exe'nin standart kurulum yolu (VS Installer ile birlikte gelir).</summary>
    public static string DefaultVswherePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        "Microsoft Visual Studio", "Installer", "vswhere.exe");

    /// <summary>
    /// vswhere'i verilen argümanlarla çalıştırır ve çıktının İLK dolu satırını — diskte gerçekten varsa —
    /// döner. Exception fırlatmaz; her sonuç tipli veri olarak döner.
    /// </summary>
    /// <param name="arguments">vswhere argümanları (arama deseni dahil).</param>
    /// <param name="vswherePath">vswhere.exe yolu; null ise <see cref="DefaultVswherePath"/>.</param>
    public async Task<VsWhereResult> FindAsync(
        IReadOnlyList<string> arguments, string? vswherePath = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        string locator = vswherePath ?? DefaultVswherePath;
        if (!File.Exists(locator)) return new VsWhereResult(VsWhereOutcome.LocatorMissing, null, locator);

        var result = await runner.RunAsync(new ProcessSpec(locator, arguments, Timeout: LocatorTimeout), ct);
        if (!result.Success)
            return new VsWhereResult(VsWhereOutcome.LocatorFailed, null,
                $"exit={result.ExitCode} stderr={result.StandardError}");

        string? path = result.StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        return path is not null && File.Exists(path)
            ? new VsWhereResult(VsWhereOutcome.Found, path, null)
            : new VsWhereResult(VsWhereOutcome.NotFound, null, null);
    }
}
