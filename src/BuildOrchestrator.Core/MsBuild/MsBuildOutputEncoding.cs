using System.Globalization;
using System.Text;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// MSBuild.exe (.NET Framework program) redirected stdout/stderr'ı UTF-8 DEĞİL, sistemin ANSI codepage'inde
/// yazar. UTF-8 varsayımıyla okumak Türkçe çıktıyı mojibake eder (bu makinenin toolchain'i TR) — [It-2 bilinen
/// risk]. Okuma tarafında (MsBuildInvoker) tek noktadan bu encoding kullanılır.
/// </summary>
public static class MsBuildOutputEncoding
{
    // Alan initializer'ları [C# spec 15.5.6.2] bildirim SIRASINA göre, herhangi bir explicit static ctor'dan
    // ÖNCE çalışır — bu yüzden registration, Value'nun GetEncoding çağrısından önce garanti tamamlanmış olur.
    // CodePagesEncodingProvider .NET Core'da (Framework'ün aksine) varsayılan olarak kayıtlı DEĞİLDİR;
    // kayıtsız 1254 gibi bir ANSI CP'yi GetEncoding ile istemek NotSupportedException fırlatır.
    private static readonly bool s_providerRegistered = RegisterProvider();

    /// <summary>Sistemin ANSI codepage'i (Encoding.Default'un .NET Core'da artık UTF-8'e sabitlenmiş olmasının yerini tutar).</summary>
    public static Encoding Value { get; } = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);

    private static bool RegisterProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return true;
    }
}
