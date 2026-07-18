namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// [T72/Task 14] csproj'un ham <c>TargetFrameworkVersion</c> (legacy) / <c>TargetFramework</c> (SDK-style)
/// değerinden <see cref="StaleObjDetector.Inspect"/>'in beklediği TAM moniker'ı türetir — bu, project.assets.json
/// "targets" anahtar biçimiyle AYNI olmalıdır (<c>Inspect</c> substring-Contains ile karşılaştırır, bkz. o
/// metodun <c>expectedTfm</c> doc'u).
///
/// <para><b>Legacy</b> (<c>&lt;TargetFrameworkVersion&gt;vX.Y&lt;/TargetFrameworkVersion&gt;</c>, OSYS
/// csproj'larının biçimi) → <c>.NETFramework,Version=vX.Y</c>.</para>
///
/// <para><b>Tüm SDK-style TFM'ler</b> (<c>netstandardX.Y</c> dahil, ör. <c>net10.0</c>, <c>netstandard2.0</c>,
/// <c>net46</c>) OLDUĞU GİBİ (ham kısa TFM) döner: SDK-style projelerde project.assets.json "targets" anahtarı
/// UZUN moniker DEĞİL, KISA TFM'nin kendisidir — bu repoda doğrulandı: <c>net10.0-windows</c>, <c>net46</c> için
/// olduğu gibi, <c>netstandardX.Y</c> için de NuGet restore aynı kuralı uygular ("targets" anahtarı asla
/// <c>.NETStandard,Version=vX.Y</c> uzun biçimini üretmez). Eskiden buradaki kod netstandard'ı özel olarak uzun
/// biçime çeviriyordu — bu, TEMİZ bir SDK-style netstandard projesinde <c>Inspect</c>'in kendi meşru "targets"
/// anahtarıyla (kısa <c>netstandardX.Y</c>) hiç eşleşmemesine, ardından yabancı-TFM regex'inin AYNI anahtarı
/// yanlışlıkla yakalayıp sahte "stale" uyarısı üretmesine yol açıyordu (bkz. StaleObjDetectorTests /
/// StaleObjRunStartWarnerTests'teki round-trip testi). <c>Inspect</c>'in substring-Contains karşılaştırması ham
/// kısa TFM ile doğru eşleşir (beklenen "net10.0", gerçek anahtar "net10.0-windows" İÇERİR) — uzun
/// ".NETCoreApp,Version=vX.Y" / ".NETStandard,Version=vX.Y" biçimine ÇEVRİLMEZ.</para>
/// </summary>
public static class TargetFrameworkMonikerDeriver
{
    public static string? FromRaw(string? targetFrameworkVersion, string? targetFramework)
    {
        if (!string.IsNullOrWhiteSpace(targetFrameworkVersion))
            return $".NETFramework,Version={targetFrameworkVersion.Trim()}";
        if (string.IsNullOrWhiteSpace(targetFramework)) return null;
        return targetFramework.Trim();
    }
}
