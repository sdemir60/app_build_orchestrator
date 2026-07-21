namespace BuildOrchestrator.App.Services;

/// <summary>
/// [It-4a Foundation] Downstream animasyon task'larının (typewriter/kaskat/scroll/graf) tükettiği reduced-motion
/// arayüzü: canlı AnimationsEnabled bayrağı + bir token süreyi etkin süreye çeviren saf sorgu.
///
/// <para><b>TÜKETİM SÖZLEŞMESİ (downstream 6 task için zorunlu — bkz. Task 1 fix wave, Important #2):</b>
/// <see cref="MotionSettings.Attach"/> yalnız <c>ResourceDictionary</c>'deki <c>Duration.*</c> girdilerini
/// CANLI günceller; WPF <c>{StaticResource}</c> bağlamaları bir kez, kurulum anında çözülür ve bu canlı
/// güncellemeyi GÖRMEZ. Buna göre:</para>
/// <list type="bullet">
/// <item>Kod-tarafı animasyonlar (typewriter, ScrollAnimator, graf dash-clock, kamera — It-4a'daki çoğunluk):
/// animasyonu BAŞLATTIKLARI ANDA <see cref="Effective"/>/<see cref="AnimationsEnabled"/>'ı TAZE okumalı
/// (constructor'da bir kere değil) — böylece canlı sinyal doğal olarak yansır.</item>
/// <item>Saf-XAML Storyboard'lar bir duration token'ı tüketiyorsa <c>{DynamicResource Duration.X}</c>
/// kullanmalı (ASLA <c>{StaticResource}</c> değil) — yalnız <c>DynamicResource</c>, <c>Attach</c>'in
/// yaptığı canlı sıfırlamayı yeni tetiklenen animasyonlara ulaştırır.</item>
/// <item><b>[T60 — ölçülmüş kısıt]</b> Bir <c>ControlTemplate.Triggers</c> Storyboard'u bu maddeyi
/// UYGULAYAMAZ: şablon mühürlenirken (Seal) zaman çizelgesi ağacı DONDURULMAK zorundadır ve
/// <c>{DynamicResource}</c> taşıyan bir <c>Freezable</c> dondurulamaz — XAML yükleme anında
/// <c>XamlParseException</c> alınır. Yani şablon durum geçişleri için saf-XAML yolu YOKTUR; kod-tarafı
/// yazılmalıdır (<c>Controls/MotionTokens.TransitionColor</c>). Kanıt: <c>MotionResourcesTests</c>'teki
/// iki spike testi; kararın kaydı <c>Resources/Controls.xaml</c> başındadır.</item>
/// </list>
/// </summary>
public interface IMotionSettings
{
    bool AnimationsEnabled { get; }

    /// <summary>AnimationsEnabled canlı değiştiğinde tetiklenir.</summary>
    event EventHandler? AnimationsEnabledChanged;

    /// <summary>Reduced-motion kapalıyken TimeSpan.Zero, açıkken verilen token süresini döner. Canlılığı
    /// garanti etmek için çağıran taraf bunu animasyonu BAŞLATTIĞI anda çağırmalı — sonucu cache'leyip
    /// tekrar kullanmamalı (bkz. tip düzeyi TÜKETİM SÖZLEŞMESİ notu).</summary>
    TimeSpan Effective(TimeSpan token);
}
