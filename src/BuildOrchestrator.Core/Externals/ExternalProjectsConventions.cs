namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// Harici projelerin tel üzerindeki ve UI'daki sabitleri — TEK tanım yeri. Katman adı ve indeksi node
/// üretiminden liste gruplamasına ve graf bandına kadar aynı iki değerdir; ikinci bir yerde yeniden
/// yazılmaz.
/// </summary>
public static class ExternalProjectsConventions
{
    /// <summary>Harici projelerin toplandığı sanal katmanın adı (UI metni — İngilizce).</summary>
    public const string LayerName = "External";

    /// <summary>
    /// Harici katmanın indeksi. NEGATİF olması kasıtlıdır: hem liste gruplaması hem graf bandı katmanları
    /// indekse göre sıraladığı için hariciler ana repo katmanlarının HEP üstünde kalır ve gerçek bir katman
    /// numarasıyla asla çakışmaz.
    /// </summary>
    public const int LayerIndex = -1;
}
