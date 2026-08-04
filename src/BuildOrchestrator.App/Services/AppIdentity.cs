using System.Reflection;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [About] Uygulamanın kendi kimliği — title bar başlığının, About hero'sunun ve tanı raporunun okuduğu TEK
/// yer. Değerler <c>Directory.Build.props</c>'tan assembly attribute'larına, oradan buraya akar; UI'da ürün
/// adı, sürüm ya da telif metni YENİDEN YAZILMAZ (AppIdentityTests kaynak guard'ı pinler).
/// </summary>
public static class AppIdentity
{
    private static readonly Assembly Self = typeof(AppIdentity).Assembly;

    /// <summary><c>Directory.Build.props</c> → <c>&lt;Product&gt;</c>.</summary>
    public static string Product { get; } =
        Self.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? Self.GetName().Name ?? "";

    /// <summary><c>Directory.Build.props</c> → <c>&lt;InformationalVersion&gt;</c> (teslim etiketi dahil,
    /// ör. <c>1.0.0+it5</c>).</summary>
    public static string Version { get; } =
        Self.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Self.GetName().Version?.ToString(3) ?? "";

    /// <summary><c>Directory.Build.props</c> → <c>&lt;Copyright&gt;</c>. TEK PARÇA okunur: yıl ve şirket adı
    /// UI'da birleştirilmez ve <c>DateTime.Now.Year</c> kullanılmaz (telif yılı bir çalışma-zamanı değeri
    /// değildir).</summary>
    public static string Copyright { get; } =
        Self.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>About hero'sundaki tek cümlelik ürün tanımı. Bunun bir assembly attribute karşılığı YOKTUR
    /// (<c>AssemblyDescription</c> MSBuild'de <c>&lt;Description&gt;</c> ile kurulur ve paket açıklamasıdır) —
    /// metnin tek yeri burasıdır.</summary>
    public const string Tagline = "Ordered, incremental builds for a multi-project .NET solution.";
}
