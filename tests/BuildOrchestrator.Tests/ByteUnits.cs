namespace BuildOrchestrator.Tests;

/// <summary>
/// [temizlik madde 4] Testlerin bayt birimleri — TEK tanım. Sekiz test dosyası <c>Mb</c>'ı, iki dosya <c>Gb</c>'ı ayrı ayrı yazıyordu; üç dosya
/// da aynı çarpımı satır içinde (<c>8L * 1024 * 1024</c> gibi) taşıyordu (kopya YASAK). Kullanan dosya adları olduğu gibi okur
/// (<c>using static BuildOrchestrator.Tests.ByteUnits;</c>) ya da tek bir yerde nitelikli yazar (<c>ByteUnits.Gb</c>).
/// <para>Birim testin kendi tarafındadır: beklenen sayı üretimin sabitinden TÜRETİLMEZ (türetilseydi test kendini doğrulardı).</para>
/// </summary>
internal static class ByteUnits
{
    public const long Kb = 1024;
    public const long Mb = 1024 * Kb;
    public const long Gb = 1024 * Mb;
}
