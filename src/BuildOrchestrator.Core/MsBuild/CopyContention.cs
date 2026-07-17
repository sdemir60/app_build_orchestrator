using System.Text.RegularExpressions;

namespace BuildOrchestrator.Core.MsBuild;

/// <summary>
/// [T8] Ortak bin dizinine paralel post-build copy event'lerinin çarpışması ("sharing violation") ile
/// başarısız olan MSBuild satırlarını tanır: MSB3021 (dosya kopyalanamadı — kilitli), MSB3026 (yeniden
/// deneme başlıyor), MSB3027 (yeniden deneme sayısı aşıldı, başarısız). Orchestrator kendisi HİÇBİR ŞEY
/// kopyalamaz [§4] — bu yalnız PROJENİN KENDİ post-build copy event'inin başka bir projeyle aynı hedef
/// dosyada yarıştığını, MSBuild'in ürettiği satırlardan tespit eder.
/// </summary>
public static partial class CopyContention
{
    // Kelime sınırı ("\b") kasıtlı: "MSB3021" bir dosya/etiket adının PARÇASI olarak geçerse (ör.
    // "MSB30210-workaround.txt") yanlış-pozitif OLMAMALI — gerçek MSBuild tanı satırları kodu her zaman
    // bağımsız bir belirteç olarak yazar ("error MSB3021: ...", "warning MSB3026: ...").
    [GeneratedRegex(@"\bMSB302[167]\b")]
    private static partial Regex ContentionCodePattern();

    public static bool IsContention(string line) =>
        line is not null && ContentionCodePattern().IsMatch(line);
}
