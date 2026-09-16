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

    /// <summary>
    /// Uygulama ikonunun (çok boyutlu <c>app-icon.ico</c>) gömülü kaynak adresi — ürün adı gibi TEK yerde.
    ///
    /// <para>İki tüketicisi vardır: pencere/taskbar ikonu (<c>MainWindow.xaml.cs</c>) ve tepsi bildiriminin büyük
    /// ikonu (<c>Shell/AppTrayIcon</c>). Adres ikinci kez yazılsaydı dosya adı ya da klasör değiştiğinde biri
    /// düzelir, diğeri çalışma zamanında çözülemeyen bir kaynağa dönerdi — bedeli ctor'da atılan bir exception.
    /// Burada durur çünkü ikon da ürün kimliğidir (<see cref="Product"/>'ın kardeşi).</para></summary>
    public const string AppIconUri = "pack://application:,,,/BuildOrchestrator.App;component/Assets/app-icon.ico";

    /// <summary>[design v1.19.0 §2.10] About sekmesinin tanım paragrafı — metnin tek yeri. Ürün adı YENİDEN
    /// YAZILMAZ, <see cref="Product"/>'tan okunur (paragraf onunla başlar). <see cref="Tagline"/> gibi bunun da
    /// bir assembly attribute karşılığı yoktur.</summary>
    public static string Overview { get; } =
        Product + " discovers the projects under the repository root, works out the dependency graph and builds in "
        + "that order — only what changed, in parallel where the graph allows. The plan, the running build and its "
        + "result stay visible while it works.";

    /// <summary>About hero'sundaki tek cümlelik ürün tanımı. Bunun bir assembly attribute karşılığı YOKTUR
    /// (<c>AssemblyDescription</c> MSBuild'de <c>&lt;Description&gt;</c> ile kurulur ve paket açıklamasıdır) —
    /// metnin tek yeri burasıdır.</summary>
    public const string Tagline = "Ordered, incremental builds for a multi-project .NET solution.";
}
