using System.Windows;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [It-4a Foundation / Global Constraints — reduced-motion] IMotionSignal'i (SystemParameters.ClientAreaAnimation
/// soyutlaması) tüketir, AnimationsEnabled'ı canlı yayar ve Attach edilen ResourceDictionary'deki Duration.*
/// kaynaklarını uygulama düzeyinde TOPLU olarak 0'a çevirir / token değerine geri yükler (App/Resources/Motion.xaml
/// ile birebir eşleşen anahtarlar).
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
    // App/Resources/Motion.xaml'deki token süreleriyle BİREBİR — reduced-motion kapanınca bu değerlere döner.
    private static readonly (string Key, TimeSpan Token)[] DurationKeys =
    [
        ("Duration.Instant", TimeSpan.FromMilliseconds(80)),
        ("Duration.Fast", TimeSpan.FromMilliseconds(120)),
        ("Duration.Base", TimeSpan.FromMilliseconds(180)),
        ("Duration.Slow", TimeSpan.FromMilliseconds(280)),
    ];

    private readonly IMotionSignal _signal;
    private ResourceDictionary? _resources;

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
    /// ve her sinyal değişiminde günceller. Aynı instance'a birden fazla kez Attach edilirse son çağrı geçerli olur.
    /// Yalnız <c>{DynamicResource}</c> tüketicilerine (ve Attach'ten SONRA inşa edilen <c>{StaticResource}</c>
    /// tüketicilerine) canlı ulaşır — bkz. tip düzeyi TÜKETİM SÖZLEŞMESİ notu.</summary>
    public void Attach(ResourceDictionary resources)
    {
        _resources = resources;
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
        foreach (var (key, token) in DurationKeys)
            _resources[key] = new Duration(enabled ? token : TimeSpan.Zero);
    }
}
