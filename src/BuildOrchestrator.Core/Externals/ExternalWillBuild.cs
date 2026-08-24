using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// Bir harici projenin bu koşuda derlenip derlenmeyeceği ve gerekçesi.
/// </summary>
public sealed record ExternalBuildDecision(bool WillBuild, WillBuildReason Reason);

/// <summary>
/// Harici projeler için "derlenecek mi" kararı — <c>WillBuildEvaluator</c>'ın bilinçli mini aynası.
///
/// <para>Ana değerlendiriciye YENİ BİR KOL EKLENMEZ: haricilerin bağımlılığı yoktur, dolayısıyla cycle
/// üyeliği ve <see cref="BuildState.DepIssue"/> kolları burada anlamsızdır; hollow (imza yok) hâli de
/// oluşmaz — imza her zaman hesaplanabilir, bilinmeyen revizyon bile ayırt edici bir terimle imzaya girer
/// (bkz. <see cref="ExternalSignature"/>). O yüzden karar dört koldan birine düşer ve gerekçeler ana repo
/// ile AYNI sözlükten konuşur.</para>
/// </summary>
public static class ExternalWillBuild
{
    /// <param name="state">Bu harici projenin defterdeki kaydı (anahtar = TargetPath); hiç derlenmemişse null.</param>
    /// <param name="signature">Bu koşu için hesaplanmış güncel imza.</param>
    public static ExternalBuildDecision Decide(BuildState? state, string signature)
    {
        ArgumentNullException.ThrowIfNull(signature);

        if (state?.BuiltSignature is null) return new ExternalBuildDecision(true, WillBuildReason.NeverBuilt);
        if (state.LastResult != BuildResult.Succeeded) return new ExternalBuildDecision(true, WillBuildReason.LastFailed);

        return string.Equals(signature, state.BuiltSignature, StringComparison.Ordinal)
            ? new ExternalBuildDecision(false, WillBuildReason.UpToDate)
            : new ExternalBuildDecision(true, WillBuildReason.SignatureChanged);
    }
}
