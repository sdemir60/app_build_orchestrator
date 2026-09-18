namespace BuildOrchestrator.Core.State;

/// <summary>
/// [spec 2026-09-18 §1-14] Bir başarısızlık nedeninin DERLEYİCİ KANITI sayılıp sayılmayacağını sınıflandırır —
/// yalnız derleyicinin/MSBuild'in kendi sıfır-dışı çıkışı kanıttır; timeout, durdurma (stopped), invoke hatası
/// ya da yakınsamayan bir SCC'nin (bkz. §8.8) "yeşil" üyesi DEĞİLDİR (bunlarda çıktı güvenilmez ama kaynağın
/// bozuk olduğu KANITLI değildir).
///
/// <para>Sınıflandırıcı Core'da durur (Supervisor'a özel değil): <c>RunCoordinator.ReasonFor</c> "exit {kod}"
/// biçimini BURADAKİ <see cref="ExitPrefix"/>'ten üretir — aynı literal iki yerde tanımlanmaz (kopya YASAK,
/// CLAUDE.md). Bu sınıflandırma kanıt kapısının YALNIZ bir parçasıdır: kapının tamamı (arkasında durulabilir
/// sonuç + bu sınıflandırma + bilinen imza) Supervisor'daki <c>RunCoordinator.FailureEvidenceSignature</c>'dadır
/// ve kararı hem deftere hem App'e giden olaya (<c>ProjectFailedEvent.Evidence</c>) yazar. App bu sınıfı
/// OKUMAZ — reason metnini yeniden sınıflandırmak, yakınsamayan bir SCC üyesinde defterle ayrışırdı.</para>
/// </summary>
public static class FailureClassification
{
    /// <summary>Derleyicinin/MSBuild'in kendi sıfır-dışı çıkışını taşıyan reason önekinin TEK kaynağı —
    /// <c>"exit {ExitCode}"</c> biçiminin başı.</summary>
    public const string ExitPrefix = "exit ";

    /// <summary><paramref name="reason"/> bir derleyici kanıtı mı (MSBuild'in kendi sıfır-dışı çıkışı, <see
    /// cref="ExitPrefix"/> ile başlar). <c>null</c> (başarı) dahil her başka reason (<c>"timeout"</c>,
    /// <c>"stopped"</c>, <c>"invoke error: …"</c>) kanıt DEĞİLDİR.</summary>
    public static bool IsCompilerFailure(string? reason) =>
        reason is not null && reason.StartsWith(ExitPrefix, StringComparison.Ordinal);
}
