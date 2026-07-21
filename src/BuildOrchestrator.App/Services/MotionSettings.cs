using System.Windows;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [It-4a Foundation / Global Constraints — reduced-motion] IMotionSignal'i (SystemParameters.ClientAreaAnimation
/// soyutlaması) tüketir, AnimationsEnabled'ı canlı yayar ve Attach edilen ResourceDictionary'deki Duration.*
/// kaynaklarını uygulama düzeyinde TOPLU olarak 0'a çevirir / token değerine geri yükler. Token (tam) süreler
/// BURADA TANIMLI DEĞİLDİR: <see cref="Attach"/> anında ResourceDictionary'den okunur — süre/eğri otoritesi tek
/// bir yerdir (App/Resources/Motion.xaml), bu sınıf yalnız anahtar ADLARINI bilir.
///
/// <para><b>ÖNEMLİ — {StaticResource} CANLI DEĞİLDİR (Task 1 fix wave, Important #2):</b> WPF
/// <c>{StaticResource}</c> bağlamaları YALNIZ BİR KEZ, kurulum (construction) anında çözülür. Bir Storyboard
/// <c>Duration="{StaticResource Duration.Base}"</c> ile inşa edildiyse, <see cref="Attach"/>'in sonradan yaptığı
/// canlı sıfırlamayı GÖRMEZ. "Doğrudan {StaticResource Duration.*} kullanır, reduced iken zaten 0 görür" varsayımı
/// yalnız ilk kurulumda (Attach'ten SONRA inşa edilen Storyboard'lar için) doğrudur — sinyal SONRADAN değişirse
/// (kullanıcı OS ayarını runtime'da değiştirirse) StaticResource tüketen ÖNCEDEN inşa edilmiş Storyboard'lar eski
/// süreyle kalır. Bu yüzden downstream tüketim SÖZLEŞMESİ (bkz. <see cref="IMotionSettings"/> tip dokümanı):
/// kod-tarafı animasyonlar <see cref="Effective"/>'i başlatma anında TAZE okumalı; saf-XAML Storyboard'lar
/// <c>{DynamicResource Duration.X}</c> kullanmalı ({StaticResource} DEĞİL).</para>
/// </summary>
public sealed class MotionSettings : IMotionSettings
{
    // YÖNETİLEN anahtarlar — SÜRE DEĞERİ BURADA YOK (final review I-1). Süre otoritesi TEK bir yerdir:
    // App/Resources/Motion.xaml. Baseline'lar Attach anında dictionary'nin KENDİSİNDEN okunur; burada bir
    // kopya tablo tutulsaydı Motion.xaml'i değiştirmek davranışı değiştirmezdi (sessiz drift).
    private static readonly string[] DurationKeys =
    [
        "Duration.Instant",
        "Duration.Fast",
        "Duration.Base",
        "Duration.Slow",
    ];

    private readonly IMotionSignal _signal;
    private ResourceDictionary? _resources;
    // Attach anında dictionary'den okunan token süreleri (yalnız GERÇEKTEN tanımlı anahtarlar için).
    private (string Key, TimeSpan Token)[] _baselines = [];

    public MotionSettings(IMotionSignal signal)
    {
        _signal = signal;
        _signal.Changed += OnSignalChanged;
    }

    public bool AnimationsEnabled => _signal.AnimationsEnabled;

    public event EventHandler? AnimationsEnabledChanged;

    /// <inheritdoc/>
    /// <remarks>Sözleşme gereği çağıran taraf bunu her animasyon başlatımında TAZE çağırmalı — bkz.
    /// <see cref="IMotionSettings"/> tip dokümanındaki TÜKETİM SÖZLEŞMESİ.</remarks>
    public TimeSpan Effective(TimeSpan token) => AnimationsEnabled ? token : TimeSpan.Zero;

    /// <summary>Verilen ResourceDictionary'nin Duration.* girdilerini şu anki sinyale göre uygular (ilk çağrıda)
    /// ve her sinyal değişiminde günceller. Token (tam) süreler dictionary'nin KENDİSİNDEN okunur — süre otoritesi
    /// tek: App/Resources/Motion.xaml. Aynı instance'a birden fazla kez Attach edilirse son çağrı geçerli olur;
    /// önceki dictionary bırakılmadan önce token değerlerine geri yüklenir (bu sayede aynı dictionary yeniden
    /// Attach edilse bile 0'lanmış değerler baseline sanılmaz). Yalnız <c>{DynamicResource}</c> tüketicilerine
    /// (ve Attach'ten SONRA inşa edilen <c>{StaticResource}</c> tüketicilerine) canlı ulaşır — bkz. tip düzeyi
    /// TÜKETİM SÖZLEŞMESİ notu.</summary>
    public void Attach(ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        RestoreBaselines(); // önceki (veya aynı) dictionary'yi token değerlerine geri ver
        _resources = resources;
        _baselines = [.. DurationKeys
            .Select(key => (Key: key, Token: resources[key] as Duration?))
            .Where(x => x.Token is { HasTimeSpan: true })
            .Select(x => (x.Key, x.Token!.Value.TimeSpan))];
        Apply();
    }

    private void OnSignalChanged(object? sender, EventArgs e)
    {
        Apply();
        AnimationsEnabledChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply()
    {
        if (_resources is null) return;
        bool enabled = AnimationsEnabled;
        foreach (var (key, token) in _baselines)
            _resources[key] = new Duration(enabled ? token : TimeSpan.Zero);
    }

    private void RestoreBaselines()
    {
        if (_resources is null) return;
        foreach (var (key, token) in _baselines)
            _resources[key] = new Duration(token);
        _baselines = [];
    }
}
