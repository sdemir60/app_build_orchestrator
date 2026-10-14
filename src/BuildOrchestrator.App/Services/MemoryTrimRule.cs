namespace BuildOrchestrator.App.Services;

/// <summary>
/// İşlem sonu bellek triminin SAF kararı (WPF'siz, saatsiz; <c>MainWindow</c> uygular). Bir işlem (koşu, Sync, Clean,
/// Optimize — <c>RunViewModel.EndedOperationSerial</c>) bitince, bir sonraki sakin anda <b>bir kez</b> toplanır:
/// <list type="bullet">
/// <item>İşlem başına EN FAZLA bir trim (<see cref="MarkTrimmed"/>).</item>
/// <item>Uçuşta işlem ya da bekleyen log yüklemesi varken HİÇ: o işlemin kendi bitişi (ya da yüklemenin sonu) yeniden ister.</item>
/// <item>Pencere görünürse bitiş finali oynamıyor olmalı: bloklayan toplama animasyonu ortasında kesmesin.</item>
/// <item>Pencere gizliyse tepside biten koşunun gösterge çıkışı (ve sonuç balonu) tamamlanmış olmalı — bugünkü tepsi kuralı.</item>
/// </list>
/// <para><b>Değişen kural:</b> eski kural "görünür pencerede hiç toplanmaz"dı (toplama UI'yi durdurur diye). Ölçüm
/// (<c>2026-10-13-13-38-idle-memory-analysis.md</c> §2/§6): koşu sonrası boştaki App görünür yolda 798 MB, tepsi yolunda
/// 444 MB — fark yalnız toplamadan. Görünürde de bir kez toplanır; duraklama finalden sonra, boştayken ve payla ertelenerek
/// kullanıcının önüne çıkmaz.</para>
/// </summary>
public sealed class MemoryTrimRule
{
    /// <summary>Görünür pencerede trim, finalin (ya da işlemin) bitişinden bu kadar sonra yürütülür: bloklayıcı toplama bitiş
    /// anının hemen ardına denk gelmesin (spec §6.1: 1,5 s). Pay TEK yerdedir — onu kullanan kabuk kodu kendi sayısını yazmaz,
    /// buradan okur.</summary>
    public static readonly TimeSpan GraceAfterFinale = TimeSpan.FromMilliseconds(1500);

    private int _trimmed;

    /// <summary>Bu işlem için şimdi trim istenebilir mi. Soru TÜKETMEZ — yalnız <see cref="MarkTrimmed"/> tüketir; çağıran
    /// toplamayı zamanladıktan sonra yürütme anında koşulları yeniden sorabilir (arada yeni işlem başladıysa vazgeçer ve işlem
    /// sıradaki sinyale kadar uygun kalır).</summary>
    /// <param name="endedOperation">Bitmiş son işlemin serisi (<c>EndedOperationSerial</c>); 0 = hiç işlem bitmedi.</param>
    /// <param name="operationInFlight">Koşu, Sync, Clean, Optimize, checkout ya da pull uçuşta mı.</param>
    /// <param name="pendingLogLoad">Cevabı bekleyen bir proje log yüklemesi var mı.</param>
    /// <param name="surfaceHidden">Pencere gizli (tepside) mi.</param>
    /// <param name="finalePlaying">Grafın bitiş finali şu an oynuyor mu (yalnız GÖRÜNÜRKEN sayılır).</param>
    /// <param name="awaitingTrayIndicator">Tepside biten koşunun gösterge çıkışı henüz tamamlanmadı mı (yalnız GİZLİYKEN sayılır).</param>
    public bool ShouldTrim(int endedOperation, bool operationInFlight, bool pendingLogLoad,
                           bool surfaceHidden, bool finalePlaying, bool awaitingTrayIndicator)
    {
        if (endedOperation == 0 || endedOperation == _trimmed) return false;
        if (operationInFlight || pendingLogLoad) return false;
        return surfaceHidden ? !awaitingTrayIndicator : !finalePlaying;
    }

    /// <summary>İşlemi trim edilmiş say. <b>Toplama GERÇEKTEN yürütüldüğü anda çağrılır — zamanlandığında DEĞİL:</b> zamanlanıp
    /// yürütme anında vazgeçilen (arada yeni işlem başladı, log yüklemesi geldi) bir toplama işlemi tüketmemiş olmalıdır; aksi
    /// halde o işlemin trimi sessizce kaybolurdu.</summary>
    /// <param name="endedOperation">Trim edilen işlemin serisi.</param>
    public void MarkTrimmed(int endedOperation) => _trimmed = endedOperation;
}
