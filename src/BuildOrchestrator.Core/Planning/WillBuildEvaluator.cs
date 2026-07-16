namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T53][A6][v7Δ-8] Pre-run willBuild karar mantığı: dirty=true, güncel=false, imza-yok/pre-Sync=null.
/// Saf karar fonksiyonu — imza hesaplama (BuildSignature, T25) It-3'te; burada yalnız enjekte edilen
/// currentSignature/state üzerinden karar verilir.
/// </summary>
public static class WillBuildEvaluator
{
    public static bool? Evaluate(bool inCycle, string? currentSignature, BuildState? state)
    {
        if (inCycle) return false;                                     // cycle projesi derlenmez, rozet taşır [A6]
        if (currentSignature is null) return null;                     // hollow: imza hesaplanamadı / Sync öncesi
        if (state?.BuiltSignature is null) return true;                // hiç başarıyla derlenmemiş
        if (state.LastResult != BuildResult.Succeeded) return true;    // son koşu başarısız/skip
        return !string.Equals(currentSignature, state.BuiltSignature, StringComparison.Ordinal); // dirty=true, güncel=false
    }
}
