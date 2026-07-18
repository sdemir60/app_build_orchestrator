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
/// <para><b>SDK-style <c>netstandardX.Y</c></b> → <c>.NETStandard,Version=vX.Y</c> (assets dosyasında hem uzun
/// hem kısa biçimde görülebilir — <see cref="StaleObjDetector"/>'ın yabancı-TFM regex'i ikisini de tanır).</para>
///
/// <para><b>Diğer SDK-style TFM'ler</b> (ör. <c>net10.0</c>) OLDUĞU GİBİ (ham kısa TFM) döner: net5.0+ SDK
/// projelerinde project.assets.json "targets" anahtarı UZUN moniker DEĞİL, KISA TFM'nin kendisidir — bu repoda
/// doğrulandı: <c>src/BuildOrchestrator.Supervisor/obj/project.assets.json</c> → <c>"targets": { "net10.0-windows": ... }</c>.
/// <c>Inspect</c>'in substring-Contains karşılaştırması bu yüzden ham kısa TFM ile de doğru eşleşir (beklenen
/// "net10.0", gerçek anahtar "net10.0-windows" İÇERİR) — uzun ".NETCoreApp,Version=vX.Y" biçimine ÇEVRİLMEZ.</para>
/// </summary>
public static class TargetFrameworkMonikerDeriver
{
    public static string? FromRaw(string? targetFrameworkVersion, string? targetFramework)
    {
        if (!string.IsNullOrWhiteSpace(targetFrameworkVersion))
            return $".NETFramework,Version={targetFrameworkVersion.Trim()}";
        if (string.IsNullOrWhiteSpace(targetFramework)) return null;
        string tfm = targetFramework.Trim();
        return tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
            ? $".NETStandard,Version=v{tfm["netstandard".Length..]}"
            : tfm;
    }
}
