namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [A13/B2] Git katmanının KULLANICIYA ULAŞAN ortak metinleri — tek yer, tek metin (kopya YASAK, CLAUDE.md;
/// <c>AccessibilityNames</c> deseni). Uygulama İngilizce-only'dir: buradaki metinler exception mesajı ya da
/// <c>GitResult.Fail</c> gerekçesi olarak yüzeye çıkar, bu yüzden Türkçe olamaz.
///
/// <para>Gerekçe: boş/whitespace argüman reddi <see cref="GitService"/> ve <see cref="WorktreeManager"/>
/// içinde altı ayrı çağrı noktasında aynı cümleyi kuruyordu (ikisi birebir aynı literaldi). Şablon tek
/// yerde durursa metin ile parametre adı bir daha ayrışamaz.</para>
/// </summary>
internal static class GitMessages
{
    /// <summary>Boş/whitespace verilen bir argümanın reddi — <paramref name="parameterName"/> çağıranın
    /// <c>nameof(...)</c>'ı olduğundan mesaj hangi argümanın hatalı olduğunu TAŞIR (tanı bilgisi düşmez).</summary>
    public static string MustNotBeEmpty(string parameterName) => $"{parameterName} must not be empty.";
}
