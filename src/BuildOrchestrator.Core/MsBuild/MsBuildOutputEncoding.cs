using System.Text;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// MSBuild.exe (.NET Framework program) + Roslyn csc.exe redirected stdout/stderr'ının encoding'i.
///
/// [Task 15 / It-2 devir §5] Eski varsayım (sistem ANSI codepage'i, `GetACP()`) bu toolchain'de (VS "18"
/// Enterprise, `…\MSBuild\Current\Bin\MSBuild.exe` + Roslyn `csc.exe`) YANLIŞ çıktı — redirected pipe fiilen
/// **UTF-8** yazıyor. ANSI-CP (bu makinede CP1254 Türkçe) varsayımıyla okumak klasik
/// UTF-8-baytları-ANSI-olarak-çözüldü mojibake'i üretiyordu: `başlatıldı`→`baÅŸlatÄ±ldÄ±`, `dosyası`→`dosyasÄ±`,
/// `içermiyor`→`iÃ§ermiyor`. Bu bozulma U+FFFD ÜRETMEZ (single-byte ANSI decode her bayt için başarılı olur) —
/// imza `Ã`/`Ä±`/`ÅŸ`/`ÄŸ` bigram'larıdır, bu yüzden yalnız U+FFFD arayan bir detektör bunu KAÇIRIR.
///
/// **Karar (D-Task15): pure UTF-8, detect-and-ANSI-fallback DEĞİL.** Gerekçe: (1) bu toolchain'de gözlemlenen
/// tek gerçek UTF-8 — genuine-ANSI üreten bir toolchain bu ortamda mevcut/test edilebilir değil; (2) ANSI
/// codepage tespiti (invalid-UTF-8 sinyaliyle) `MsBuildInvoker.PumpLinesAsync`'in satır-satır, gerçek-zamanlı
/// `StreamReader.ReadLineAsync()` akışını (kill/timeout/IOCP ile sıkı bağlı, kırılgan) byte-seviyesinde yeniden
/// yazmayı gerektirirdi — kazanç (hiç doğrulanamayan bir senaryo) riske değmedi. Bu yüzden `GetACP()`/
/// `NativeMethods` bağımlılığı ve `CodePagesEncodingProvider` kaydı (yalnız ANSI CP'ler için gerekliydi)
/// KALDIRILDI — UTF-8, .NET Core'da yerleşiktir, provider gerektirmez.
///
/// `throwOnInvalidBytes: false` (replacement fallback, U+FFFD) BİLEREK seçildi: `MsBuildInvoker.PumpLinesAsync`
/// yalnız `IOException`/`ObjectDisposedException` yakalar — `throwOnInvalidBytes: true` olsaydı, gerçekten
/// geçersiz bir UTF-8 baytı bir `DecoderFallbackException`'ı pump thread'inde YAKALANMADAN bırakır, bu da
/// Supervisor'ı düşürebilirdi. `encoderShouldEmitUTF8Identifier: false` — bu sınıf yalnız DECODE için kullanılır
/// (MsBuildInvoker `StreamReader`'a `Encoding` olarak verir), BOM emit etmez.
/// </summary>
public static class MsBuildOutputEncoding
{
    public static Encoding Value { get; } =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);
}
