using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Core.Externals;

/// <summary>tf.exe bulunamadı — TFVC harici projeler bu makinede güncellenemez.</summary>
public sealed class TfResolveException(string message) : Exception(message);

/// <summary>
/// [D8] TFVC komut satırı aracını (tf.exe) Visual Studio kurulumundan çözer — MSBuild.exe ile AYNI
/// mekanizma (vswhere), yalnız arama deseni farklı.
///
/// <para>tf.exe Team Explorer bileşeniyle gelir; hatanın kullanıcıya söylediği şey budur, çünkü çare
/// "Visual Studio'yu Team Explorer ile kur" cümlesidir.</para>
/// </summary>
public sealed class TfResolver(IProcessRunner runner)
{
    private static readonly string[] FindArguments =
        ["-latest", "-products", "*", "-find", @"Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\TF.exe"];

    /// <summary>tf.exe'nin tam yolu. Bulunamazsa <see cref="TfResolveException"/> fırlatır — TFVC harici
    /// varken bu, koşuyu hiç başlatmadan durduran bir plan hatasıdır.</summary>
    public async Task<string> ResolveAsync(string? vswherePath = null, CancellationToken ct = default)
    {
        var found = await new VsWhereLocator(runner).FindAsync(FindArguments, vswherePath, ct);

        return found.Outcome switch
        {
            VsWhereOutcome.Found => found.Path!,
            VsWhereOutcome.LocatorMissing => throw new TfResolveException(
                $"vswhere was not found: {found.Detail} — install Visual Studio with Team Explorer to build TFVC external projects."),
            VsWhereOutcome.LocatorFailed => throw new TfResolveException(
                $"vswhere error: {found.Detail} — TF.exe could not be located."),
            _ => throw new TfResolveException(
                "TF.exe was not found in this Visual Studio installation — install Visual Studio with Team Explorer to build TFVC external projects."),
        };
    }
}
