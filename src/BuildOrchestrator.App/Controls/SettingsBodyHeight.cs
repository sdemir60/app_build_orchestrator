namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.14.0 §2.9 · ruling task-D6] Settings gövdesinin (kaydırılan <c>ScrollViewer</c>, bkz.
/// <see cref="Views.SettingsDialog"/>) üst yükseklik sınırının SAF (WPF'siz, test edilebilir) hesabı —
/// tasarımın <c>maxHeight: min(56vh, 460px)</c> kuralının WPF karşılığı.
///
/// <para><b>Ruling (task-D6 brief):</b> WPF'te <c>vh</c> (tarayıcı viewport yüksekliği) yoktur. En yakın
/// karşılık dialogu barındıran PENCERENİN yüksekliğidir (<see cref="System.Windows.Window.ActualHeight"/>) —
/// dialog o pencerenin içinde yaşar, bağlayıcı sınır ekran değil penceredir. Hesap TEK bir yerde (burada)
/// yapılır (kopya YASAK, CLAUDE.md); çağıran taraf (<see cref="Views.SettingsDialog"/>) pencere yeniden
/// boyutlandığında bunu YENİDEN çağırır.</para>
///
/// <para><b>Alt sınır (300px) bu sınıfta YOKTUR</b> — <c>ScrollViewer.MinHeight</c> SABİT bir değerdir
/// (<see cref="MinFloor"/>, XAML'de <c>{x:Static controls:SettingsBodyHeight.MinFloor}</c> ile okunur) ve
/// WPF'in kendi Min/Max çözümü (bir elemanın <c>MinHeight</c>'ı <c>MaxHeight</c>'ıyla çelişirse Min KAZANIR)
/// çok küçük pencerede tabanı otomatik uygular — <see cref="MaxHeightFor"/> bu yüzden tabanı AYRICA
/// kelepçelemez (tek sorumluluk): pencere küçükken 300'ün ALTINDA bir üst sınır dönebilir, bu bilerektir.</para>
/// </summary>
public static class SettingsBodyHeight
{
    /// <summary>Tasarımın <c>56vh</c> oranı.</summary>
    public const double WindowHeightFraction = 0.56;

    /// <summary>Üst tavan — pencere ne kadar büyürse büyüsün gövde bunu aşmaz (tasarımın <c>460px</c>'i).</summary>
    public const double MaxCap = 460.0;

    /// <summary>Alt taban — katman listesi boşken dialog çökmesin (tasarımın <c>300px</c>'i). Bu sınıfın
    /// HESAPLADIĞI bir şey değildir; <c>ScrollViewer.MinHeight</c>'a doğrudan bağlanır (bkz. sınıf özeti).</summary>
    public const double MinFloor = 300.0;

    /// <summary>Gövdenin <c>MaxHeight</c>'ı: pencere yüksekliğinin <see cref="WindowHeightFraction"/>'ı ile
    /// <see cref="MaxCap"/>'ten KÜÇÜK olanı. <paramref name="windowHeight"/> çok küçükse sonuç
    /// <see cref="MinFloor"/>'un altına da inebilir — o durumda tabanı uygulamak çağıranın
    /// <c>MinHeight</c>'ının (WPF'in kendi Min/Max önceliğiyle) işidir, burada AYRICA kelepçelenmez.</summary>
    public static double MaxHeightFor(double windowHeight) => Math.Min(windowHeight * WindowHeightFraction, MaxCap);
}
