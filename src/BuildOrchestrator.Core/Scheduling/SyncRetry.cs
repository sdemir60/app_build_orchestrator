namespace BuildOrchestrator.Core.Scheduling;

/// <summary>
/// [T49 fix round 1 · B2] SENKRON, sınırlı, GEÇİCİ-HATA retry döngüsünün TEK tanımı.
///
/// <para><b>Neden burada:</b> aynı döngü iki yerde BİREBİR kopyalanmıştı — <c>BuildStateStore.MoveAtomicWithRetry</c>
/// (atomik rename'in sharing-violation penceresi) ve <c>ClipboardRetry.TrySet</c> (pano kilidi, dotnet/wpf#9901).
/// İkisi de "N deneme · yalnız BELLİ bir istisna sınıfını retry et · denemeler arasında ENJEKTE EDİLMİŞ bir
/// gecikme (D8) · başkasını olduğu gibi yay" diyordu; yalnız bütçe tükenince ne olacağı ayrışıyordu. Kopya YASAK
/// (CLAUDE.md) — davranış farkı bir PARAMETREye indirildi.</para>
///
/// <para><b>Asenkron kardeşi:</b> <c>RetryingMsBuildInvoker</c> (MSB302x contention) — o yol <c>Task</c> tabanlı
/// olduğu için ayrı kalır; ortak olan tasarım kuralıdır: gecikme ASLA gömülmez, çağırandan gelir.</para>
/// </summary>
public static class SyncRetry
{
    /// <summary>
    /// <paramref name="action"/>'ı en çok <paramref name="attempts"/> kez dener.
    ///
    /// <list type="bullet">
    /// <item><paramref name="isTransient"/> <c>false</c> derse istisna OLDUĞU GİBİ yayılır (retry YOK, yutma YOK).</item>
    /// <item>Geçici hatada, SON deneme değilse <paramref name="delay"/> çağrılır (1-based deneme no ile) —
    /// yani <c>attempts</c> deneme, <c>attempts-1</c> gecikme. Gecikmenin ne olduğu ÇAĞIRANIN kararıdır
    /// (üretimde kısa bir backoff, testte anında dönen bir dikiş — D8).</item>
    /// <item>Bütçe tükenirse: <paramref name="rethrowWhenExhausted"/> ise SON istisna orijinal stack'iyle yayılır,
    /// değilse <c>false</c> dönülür (çağıranın "sessizce başarısız ol, UI'ı çökertme" sözleşmesi).</item>
    /// </list>
    /// </summary>
    public static bool Run(
        Action action, int attempts, Func<Exception, bool> isTransient, Action<int> delay, bool rethrowWhenExhausted)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(isTransient);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex) when (attempt < attempts && isTransient(ex))
            {
                delay(attempt);
            }
            catch (Exception ex) when (!rethrowWhenExhausted && isTransient(ex))
            {
                return false; // bütçe tükendi; çağıran sessiz başarısızlık istiyor
            }
            // Geçici OLMAYAN istisna ya da (rethrowWhenExhausted ile) tükenmiş bütçe: filtreler tutmaz → yayılır.
        }
    }
}
