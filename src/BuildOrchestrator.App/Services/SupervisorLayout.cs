using System.IO;
using System.Reflection;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [D1] Supervisor çıktısının uygulamanın YANINDA durduğu alt klasörün TEK KAYNAĞI.
/// <para>
/// Ad, MSBuild property'si <c>$(SupervisorFolderName)</c> ile <c>BuildOrchestrator.App.csproj</c>'da TANIMLANIR:
/// hem build/publish kopyalama target'ları hem de bu sınıf ondan beslenir — klasör adı derleme sırasında
/// <see cref="AssemblyMetadataAttribute"/> olarak assembly'ye gömülür ve burada geri okunur. Böylece ad C#'ta
/// AYRICA yazılmaz; iki taraf ayrışıp runtime'da <b>sessizce</b> kırılamaz (eski hal: csproj'daki hedef klasör
/// ile <c>App.xaml.cs</c>'teki yol birbirinden bağımsız iki string'di).
/// </para>
/// </summary>
public static class SupervisorLayout
{
    /// <summary>csproj'daki <c>AssemblyMetadata</c> anahtarı — MSBuild tarafıyla birebir eşleşmek zorundadır.
    /// Eşleşmezse <see cref="FolderName"/> ilk okunuşta GÜRÜLTÜLÜ patlar (sessiz kırılma yok).</summary>
    public const string FolderNameMetadataKey = "SupervisorFolderName";

    /// <summary>Supervisor'ın apphost'u — Supervisor projesinin assembly adı + <c>.exe</c>.</summary>
    public const string SupervisorExeFileName = "BuildOrchestrator.Supervisor.exe";

    /// <summary>Uygulamanın yanındaki supervisor alt klasörünün adı (csproj'dan gömülür).</summary>
    public static string FolderName { get; } = ReadFolderName(typeof(SupervisorLayout).Assembly);

    /// <summary>Verilen uygulama dizini için Supervisor exe'sinin tam yolu (üretimde
    /// <see cref="AppContext.BaseDirectory"/>; publish çıktısında da aynı yerleşim geçerlidir).</summary>
    public static string ResolveExePath(string baseDirectory) =>
        Path.Combine(baseDirectory, FolderName, SupervisorExeFileName);

    /// <summary>Testlerin de kullandığı okuma kapısı — assembly'ye gömülü <c>$(SupervisorFolderName)</c>.</summary>
    public static string ReadFolderName(Assembly assembly)
    {
        string? name = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, FolderNameMetadataKey, StringComparison.Ordinal))?.Value;

        return string.IsNullOrEmpty(name)
            ? throw new InvalidOperationException(
                $"'{FolderNameMetadataKey}' AssemblyMetadata'sı yok: BuildOrchestrator.App.csproj'daki " +
                "$(SupervisorFolderName) property'si / AssemblyAttribute kaydı kaldırılmış olmalı.")
            : name;
    }
}
