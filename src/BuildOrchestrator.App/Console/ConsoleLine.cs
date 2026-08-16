namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3a] Anlatı (narrative) konsol satırının görsel tipi — design §2.5 NARR_COLORS ile birebir:
/// cmd=text-primary, info=text-secondary, dim=text-faint, success/warn/error=ilgili <c>-text</c> tonu.
/// Renk eşlemesi <see cref="ConsolePalette"/>'tedir (token brush'ları; hardcode YASAK).
/// </summary>
public enum ConsoleLineType { Cmd, Info, Dim, Warn, Error }

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

    /// <summary>
    /// <b>[DEĞİŞEN KURAL] Yalnız KAYNAĞI belli satırlar renklenir; metin tahmini kaldırıldı.</b>
    ///
    /// <para><b>Eski iddia:</b> satır içinde <c>failed</c>/<c>succeeded</c>/<c>✓</c>/<c>✗</c> geçiyorsa kırmızı
    /// ya da yeşil boyanır ve <c>warning</c> kelimesi nerede geçerse geçsin turuncu yapar.</para>
    ///
    /// <para><b>Değişme gerekçesi (kullanıcı):</b> "kesin bir ayrım yoksa tek renk olabilir, tahmine göre
    /// yapıyorsak". Haklıydı: o taramalar bir PROJE ADININ içindeki kelimeye de takılırdı ve renk o noktada
    /// bilgi değil gürültüdür. Geriye yalnız formatı BELLİ olan iki kaynak kalır — MSBuild'in kendi tanı
    /// satırları (<c>… : error CS0103: …</c>, <c>… : warning MSB3277: …</c>) ve uygulamanın KENDİ bastığı
    /// önekler (<c>[error]</c>, <c>warning:</c>, komut satırları). Bunlar tahmin değildir.</para>
    ///
    /// <para><c>Success</c> tipi bu kararla tamamen kalktı: onu döndüren tek yol metin taramasıydı. Bir
    /// koşunun başarısı zaten şeritte, listede ve event stream'de üç ayrı yerde söyleniyor — konsolun ham
    /// çıktısında ayrıca renklenmesi gerekmiyor.</para>
    /// </summary>
    public static ConsoleLineType Classify(string? text)
    {
        if (string.IsNullOrEmpty(text)) return ConsoleLineType.Info;
        string content = text.TrimStart();

        foreach (string head in CommandHeads)
            if (content.StartsWith(head, StringComparison.OrdinalIgnoreCase))
                return ConsoleLineType.Cmd;

        // Uygulamanın KENDİ önekleri — kaynağı biziz, tahmin yok.
        if (content.StartsWith("[hata]", StringComparison.Ordinal) ||
            content.StartsWith("[error]", StringComparison.OrdinalIgnoreCase))
            return ConsoleLineType.Error;
        if (content.StartsWith("warning:", StringComparison.OrdinalIgnoreCase))
            return ConsoleLineType.Warn;

        // MSBuild'in tanı satırı formatı: "<köken>: error <KOD>: <metin>" (kökensiz hâlde satır başında).
        // "warning" ÖNCE bakılır: bağımlılık uyarısı ("warning: … failed in this run") ikisini de içerir ve
        // turuncu olmalıdır.
        if (HasDiagnostic(content, "warning")) return ConsoleLineType.Warn;
        if (HasDiagnostic(content, "error")) return ConsoleLineType.Error;

        return ConsoleLineType.Info;
    }

    /// <summary>MSBuild tanı satırı mı — <c>": error "</c> biçiminde ya da satırın en başında.</summary>
    private static bool HasDiagnostic(string content, string kind) =>
        content.Contains(": " + kind + " ", StringComparison.OrdinalIgnoreCase)
        || content.StartsWith(kind + " ", StringComparison.OrdinalIgnoreCase);
}
