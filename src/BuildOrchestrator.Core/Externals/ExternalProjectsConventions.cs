namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// Harici köklerden gelen projelerin toplandığı AYRILMIŞ katmanın adı ve indeksi — TEK tanım yeri. Aynı iki
/// değer katman atamasından liste gruplamasına ve graf bandına kadar taşınır; ikinci bir yerde yazılmaz.
/// </summary>
public static class ExternalProjectsConventions
{
    /// <summary>Harici projelerin katmanının adı (UI metni — İngilizce). Liste bu adı grup başlığı olarak
    /// gösterir.</summary>
    public const string LayerName = "External";

    /// <summary>
    /// Harici katmanın indeksi. NEGATİF olması kasıtlıdır: hem liste gruplaması hem graf bandı katmanları
    /// indekse göre sıralar, dolayısıyla hariciler kullanıcının yapılandırdığı HER katmanın (ve
    /// <c>Other</c>'ın) üstünde kalır ve gerçek bir katman numarasıyla asla çakışmaz — kullanıcı pattern'leri
    /// <c>Order</c>'ı negatif veremez.
    /// </summary>
    public const int LayerIndex = -1;
}
