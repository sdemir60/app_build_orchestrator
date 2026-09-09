using System.Text;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Core.Externals;

/// <summary>
/// [D4] Bir harici projenin byte-stable imzası: <c>configuration</c> + <c>vcs</c> + <c>revision</c>.
///
/// <para>Ana repo imzasından (<see cref="BuildSignature"/>) çok daha dar olmasının sebebi kapıdır: dirty bir
/// harici çalışma kopyası build'i baştan iptal ettirir, yani derlenen her harici TEMİZDİR ve kaynak durumu
/// tam olarak revizyon kimliğiyle belirlidir. Dosya içeriği taranmaz, upstream yoktur.</para>
///
/// <para><c>revision == null</c> (bilinmeyen revizyon) ayırt edici bir işaretle imzaya girer ve gerçek bir
/// revizyon değeriyle ASLA çakışmaz — böyle bir proje hiçbir zaman "up to date" görünmez.</para>
///
/// <para>§4 kaynak-sinyali kuralı burada da geçerlidir: DLL/bin/obj veya herhangi bir timestamp okunmaz.</para>
/// </summary>
public static class ExternalSignature
{
    // Ana repo imzasıyla AYNI ayraç ailesi — ikisi de aynı hash primitifini kullanır (kopya yasak).
    private const char FieldSeparator = (char)0x1F;

    /// <summary>Bu harici projenin imzasını hesaplar (SHA256 hex, 64 karakter).</summary>
    /// <param name="configuration">"Debug"/"Release" — aynen (case-sensitive) imzaya girer.</param>
    /// <param name="vcs">Çalışma kopyasının sürüm kontrol türü; tür değişimi imzayı değiştirir.</param>
    /// <param name="revision">git HEAD sha'sı / TFVC changeset numarası. <c>null</c> = bilinmiyor.</param>
    public static string Compute(string configuration, VcsKind vcs, string? revision)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var sb = new StringBuilder();
        sb.Append("cfg=").Append(configuration).Append(FieldSeparator);
        sb.Append("vcs=").Append(vcs).Append(FieldSeparator);
        sb.Append("rev=").Append(revision ?? BuildSignature.NullMarker);

        return BuildSignature.HashText(sb.ToString());
    }
}
