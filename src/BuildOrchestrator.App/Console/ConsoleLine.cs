namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3a] Anlatı (narrative) konsol satırının görsel tipi — design §2.5 NARR_COLORS ile birebir:
/// cmd=text-primary, info=text-secondary, dim=text-faint, success/warn/error=ilgili <c>-text</c> tonu.
/// Renk eşlemesi <see cref="ConsolePalette"/>'tedir (token brush'ları; hardcode YASAK).
/// </summary>
public enum ConsoleLineType { Cmd, Info, Dim, Success, Warn, Error }

/// <summary>
/// [T56/3a] Bir konsol satırının DÜZ METNİNDEN görsel tipini türeten SAF sınıflandırıcı.
///
/// <para><b>Line-type metadata kararı (task 3a):</b> VM düz string post ediyor; colorizer için satır TİPİ
/// gerek. Seçim: tipi <b>satır metninden türet</b> (batch/marshal sözleşmesine DOKUNMA). Böylece
/// <see cref="ConsoleBatcher.Post"/> imzası ve marshal-free <c>OnProjectLog</c> yolu değişmez; her belge
/// (doküman swap / reseed sonrası bile) KENDİNİ tanımlar — offset'lerle senkron tutulan paralel bir tip
/// dizisi gerekmez.</para>
///
/// <para><b>[design v1.7.0 §2.5] Saat ve <c>▸</c> kolonu YOK.</b> Satır artık yalnız metindir ve TAMAMI tek
/// bir tip rengiyle boyanır — önceden satırın başında sabit genişlikte bir duvar saati (faint) ve komut
/// satırlarında amber bir <c>▸</c> vardı, ikisi de ayrı aralık olarak renklendiriliyordu. Kaldırıldılar:
/// gerçek bir koşuda saniyede yüzlerce satır akar, damga bilgi taşımaz ve tüm satırların imleçle aynı sol
/// hizada başlaması yığını taramayı kolaylaştırır.</para>
/// </summary>
public static class ConsoleLineClassifier
{
    /// <summary>
    /// Uygulamanın KOMUT olarak bastığı satırların baş sözcükleri. <c>▸</c> öneki kaldırıldığı için komut
    /// satırının tek işareti budur; liste uygulamanın gerçekten çağırdığı araçlarla sınırlıdır
    /// (<c>SyncWorkspaceService</c>'in git satırı, MSBuild/NuGet çağrı satırları) ve kullanıcı metni değildir.
    /// </summary>
    private static readonly string[] CommandHeads = ["git ", "msbuild ", "nuget ", "dotnet "];

    public static ConsoleLineType Classify(string? text)
    {
        if (string.IsNullOrEmpty(text)) return ConsoleLineType.Info;
        string content = text.TrimStart();

        foreach (string head in CommandHeads)
            if (content.StartsWith(head, StringComparison.OrdinalIgnoreCase))
                return ConsoleLineType.Cmd;

        if (content.StartsWith("[hata]", StringComparison.Ordinal) ||
            content.StartsWith("[error]", StringComparison.OrdinalIgnoreCase))
            return ConsoleLineType.Error;

        // "warning" ÖNCE gelir: design'ın "warning: OSYS.Sales.Core failed in this run …" bağımlılık uyarısı
        // hem "warning" hem "failed" içerir ve warn (turuncu) olmalı — error (kırmızı) değil.
        if (content.Contains("warning", StringComparison.OrdinalIgnoreCase))
            return ConsoleLineType.Warn;

        if (content.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            content.Contains(": error", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("error CS", StringComparison.OrdinalIgnoreCase) ||
            content.Contains('✗')) // ✗
            return ConsoleLineType.Error;

        if (content.Contains("succeeded", StringComparison.OrdinalIgnoreCase) || content.Contains('✓')) // ✓
            return ConsoleLineType.Success;

        return ConsoleLineType.Info;
    }
}
