using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Animation;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [It-4a Foundation] App/Resources/Motion.xaml: design-v1 tokens/effects.css birebir Duration + KeySpline
/// kaynakları. Ayrıca MotionSettings.Attach ile bu ResourceDictionary'nin Duration.* girdilerinin reduced-motion
/// sinyaline göre topluca 0'a çevrildiği/geri yüklendiği doğrulanır (uygulama düzeyinde toplu swap mekanizması).
/// </summary>
public class MotionResourcesTests
{
    // FakeMotionSignal: bkz. FakeMotionSignal.cs (ReducedMotionTests ile paylaşılan tek tanım).

    // [T60] pack:// URI'ler gerçek bir Application olmadan (headless test host) çözülmez — sözlükler
    // TestAssets kopyasından okunur; yükleme mekaniğinin TEK yeri DsResources'tır (kopya YASAK).
    private static string MotionAssetPath() => DsResources.AssetPath("Motion.xaml");

    private static ResourceDictionary LoadMotionDictionary() => DsResources.Load("Motion.xaml");

    [StaFact]
    public void Duration_tokens_match_design_v1_effects_css_exactly()
    {
        var resources = LoadMotionDictionary();

        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(80)), resources["Duration.Instant"]);
        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(120)), resources["Duration.Fast"]);
        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(180)), resources["Duration.Base"]);
        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(280)), resources["Duration.Slow"]);
    }

    [StaFact]
    public void KeySpline_tokens_match_design_v1_effects_css_control_points_exactly()
    {
        var resources = LoadMotionDictionary();

        var easeOut = Assert.IsType<KeySpline>(resources["KeySpline.EaseOut"]);
        Assert.Equal(new Point(0.22, 1), easeOut.ControlPoint1);
        Assert.Equal(new Point(0.36, 1), easeOut.ControlPoint2);

        var easeStandard = Assert.IsType<KeySpline>(resources["KeySpline.EaseStandard"]);
        Assert.Equal(new Point(0.4, 0), easeStandard.ControlPoint1);
        Assert.Equal(new Point(0.2, 1), easeStandard.ControlPoint2);

        var easeInOut = Assert.IsType<KeySpline>(resources["KeySpline.EaseInOut"]);
        Assert.Equal(new Point(0.65, 0), easeInOut.ControlPoint1);
        Assert.Equal(new Point(0.35, 1), easeInOut.ControlPoint2);
    }

    [StaFact]
    public void Attach_collapses_all_duration_resources_to_zero_when_signal_is_off()
    {
        var resources = LoadMotionDictionary();
        var signal = new FakeMotionSignal { AnimationsEnabled = false };
        var settings = new MotionSettings(signal);

        settings.Attach(resources);

        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Instant"]);
        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Fast"]);
        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Base"]);
        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Slow"]);
    }

    [StaFact]
    public void Attach_restores_token_durations_live_when_signal_turns_back_on()
    {
        var resources = LoadMotionDictionary();
        var signal = new FakeMotionSignal { AnimationsEnabled = false };
        var settings = new MotionSettings(signal);
        settings.Attach(resources);
        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Base"]);

        signal.AnimationsEnabled = true;
        signal.Raise();

        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(180)), resources["Duration.Base"]);
        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(280)), resources["Duration.Slow"]);
    }

    [StaFact]
    public void Attach_restores_the_durations_declared_in_the_dictionary_not_a_duplicated_table()
    {
        // [Final review I-1] Süre otoritesi TEK olmalı: Motion.xaml. Drift guard'ı: gerçek asset'in METNİNİ
        // alıp bir token'ı KASTEN kaydırırız (280ms → 320ms) ve restore'un DICTIONARY'nin dediği değere
        // döndüğünü doğrularız. MotionSettings kendi (kopya) süre tablosunu taşırsa 280ms'e döner → KIRMIZI.
        // Beklenen değer testte sabit DEĞİL, dictionary'den okunur — testin kendisi ikinci bir otorite olmasın.
        string xaml = File.ReadAllText(MotionAssetPath()).Replace("0:0:0.28", "0:0:0.32", StringComparison.Ordinal);
        var resources = (ResourceDictionary)XamlReader.Parse(xaml);
        var declaredSlow = (Duration)resources["Duration.Slow"];
        var declaredBase = (Duration)resources["Duration.Base"];
        Assert.Equal(new Duration(TimeSpan.FromMilliseconds(320)), declaredSlow); // drift gerçekten kuruldu

        var signal = new FakeMotionSignal { AnimationsEnabled = false };
        var settings = new MotionSettings(signal);
        settings.Attach(resources);
        Assert.Equal(new Duration(TimeSpan.Zero), resources["Duration.Slow"]);

        signal.AnimationsEnabled = true;
        signal.Raise();

        Assert.Equal(declaredSlow, resources["Duration.Slow"]); // dictionary ne diyorsa o (320ms)
        Assert.Equal(declaredBase, resources["Duration.Base"]);
    }

    /// <summary>
    /// [T60 Step 1 — MOTION SPIKE, 1/2] Bir Storyboard'un animasyonuna <c>{DynamicResource Duration.Fast}</c>
    /// bağlanabilir mi ve reduced-motion'ın canlı sıfırlamasını görür mü? BAŞIBOŞ bir Storyboard (bir
    /// <see cref="FrameworkElement"/>'in Resources'ında duran) için CEVAP EVET'tir — bu test onu pinler.
    ///
    /// <para>Storyboard, host'un kaynak KAPSAMINDA olmalıdır: <c>XamlReader.Parse</c> ile tek başına
    /// üretilmiş, ağaca hiç bağlanmamış bir Storyboard'da DynamicResource zaten çözülecek bir sözlük
    /// bulamazdı (<c>Duration.Automatic</c> kalır) — o yüzden burada host'un Resources'ında tanımlıdır.</para>
    ///
    /// <para><b>Ama bu, 120ms geçişleri saf-XAML yazmaya YETMEZ:</b> ihtiyaç duyulan biçim şablon
    /// trigger'ıdır ve o yol kapalıdır — bkz. eşlik eden test
    /// <see cref="A_control_template_trigger_storyboard_cannot_carry_a_DynamicResource_duration"/>.</para>
    /// </summary>
    [StaFact]
    public void A_xaml_storyboard_duration_bound_with_DynamicResource_follows_a_live_dictionary_change()
    {
        string xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Border.Resources>
                <Storyboard x:Key="Sb">
                  <DoubleAnimation Storyboard.TargetProperty="Opacity" To="0"
                                   Duration="{DynamicResource Duration.Fast}" />
                </Storyboard>
              </Border.Resources>
            </Border>
            """;
        var host = (Border)XamlReader.Parse(xaml);
        host.Resources.MergedDictionaries.Add(LoadMotionDictionary());
        var sb = (Storyboard)host.Resources["Sb"];

        host.Resources["Duration.Fast"] = new Duration(TimeSpan.Zero);

        var anim = (DoubleAnimation)sb.Children[0];
        Assert.Equal(TimeSpan.Zero, anim.Duration.TimeSpan); // reduced-motion canlı yansımalı
    }

    /// <summary>
    /// [T60 Step 1 — MOTION SPIKE, 2/2 · KARAR KAPISI] DS'in 120ms geçişleri için gereken biçim
    /// <c>ControlTemplate.Triggers</c> içindeki bir Storyboard'dur. O yol KAPALIDIR ve bu test kısıtı
    /// BELGELER (atlanmaz, tersine çevrilmiş beklentiyle pinlenir).
    ///
    /// <para><b>Neden:</b> bir <see cref="FrameworkTemplate"/> mühürlenirken (Seal) trigger'larındaki
    /// <c>BeginStoryboard</c> de mühürlenir ve zaman çizelgesi ağacı DONDURULMAK ZORUNDADIR (iş parçacıkları
    /// arası paylaşım için). <c>{DynamicResource}</c> taşıyan bir <see cref="Freezable"/> dondurulamaz →
    /// hata XAML YÜKLEME anında gelir, çalışma anında değil.</para>
    ///
    /// <para><b>Sonuç (Controls.xaml'in başında da kayıtlı):</b> tüm 120ms geçişler kod-tarafı yazılır
    /// (<c>MotionTokens.TransitionColor</c> + <c>HandoffBehavior.SnapshotAndReplace</c>), çünkü tek
    /// alternatif olan <c>{StaticResource Duration.Fast}</c> BİR KEZ çözülür ve reduced-motion'ın canlı
    /// sıfırlamasını hiç görmezdi.</para>
    /// </summary>
    [StaFact]
    public void A_control_template_trigger_storyboard_cannot_carry_a_DynamicResource_duration()
    {
        // Mühürleme, şablon bir Style üzerinden BİR ÖĞEYE uygulanınca olur — bu yüzden şablon tek başına
        // değil, gerçek kullanım biçiminde kurulur.
        string xaml = """
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <Border.Resources>
                <Style x:Key="S" TargetType="Button">
                  <Setter Property="Template">
                    <Setter.Value>
                      <ControlTemplate TargetType="Button">
                        <Border x:Name="Bg" />
                        <ControlTemplate.Triggers>
                          <EventTrigger RoutedEvent="Button.MouseEnter">
                            <BeginStoryboard>
                              <Storyboard>
                                <DoubleAnimation Storyboard.TargetName="Bg" Storyboard.TargetProperty="Opacity"
                                                 To="0" Duration="{DynamicResource Duration.Fast}" />
                              </Storyboard>
                            </BeginStoryboard>
                          </EventTrigger>
                        </ControlTemplate.Triggers>
                      </ControlTemplate>
                    </Setter.Value>
                  </Setter>
                </Style>
              </Border.Resources>
              <Button Style="{StaticResource S}" />
            </Border>
            """;

        var ex = Assert.Throws<XamlParseException>(() => XamlReader.Parse(xaml));

        // "Bu Storyboard zaman çizelgesi ağacı iş parçacıkları arasında kullanılmak üzere dondurulamıyor."
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [StaFact]
    public void Attach_twice_while_reduced_does_not_capture_the_zeroed_values_as_the_baseline()
    {
        // [Final review I-1] Baseline dictionary'den okunduğu için: kapalıyken Attach → girdiler 0 olur;
        // AYNI dictionary yeniden Attach edilirse 0'lar baseline sanılmamalı (aksi halde sinyal açılınca
        // süreler kalıcı 0 kalırdı). Beklenen değerler yine dictionary'nin TAZE bir kopyasından okunur.
        var expected = LoadMotionDictionary();
        var resources = LoadMotionDictionary();
        var signal = new FakeMotionSignal { AnimationsEnabled = false };
        var settings = new MotionSettings(signal);

        settings.Attach(resources);
        settings.Attach(resources); // ikinci Attach — 0'lar baseline OLMAMALI

        signal.AnimationsEnabled = true;
        signal.Raise();

        Assert.Equal(expected["Duration.Base"], resources["Duration.Base"]);
        Assert.Equal(expected["Duration.Slow"], resources["Duration.Slow"]);
    }
}
