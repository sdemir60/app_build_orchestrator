using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/B3 · E6] <see cref="SystemParametersMotionSignal"/> — OS'a dokunan TEK sınıf ve B3'e kadar SIFIR testliydi.
///
/// <para><b>KAPSAM SINIRI (kullanıcı kararı, tartışmaya kapalı):</b> makine-global erişilebilirlik ayarını
/// (<c>SystemParameters.ClientAreaAnimation</c> ya da onu besleyen herhangi bir OS/registry ayarı) ÇEVİREN test
/// YAZILMAZ. Bu dosya yalnız RİSKSİZ olanı sınar: (a) saf değişim filtresi, (b) salt-okur getter, (c) sinyalin
/// uygulamada GERÇEKTEN kablolu olduğu. Kapsam dışında kalanlar task-B3-report.md <c>## Concerns</c>'te
/// "artık liste" olarak yazılıdır.</para>
/// </summary>
public class SystemParametersMotionSignalTests
{
    /// <summary>WPF'in property ADI — <b>otorite literali</b>. <c>nameof(SystemParameters.ClientAreaAnimation)</c>
    /// yazmak totoloji olurdu: üretim de tam olarak onu kullanıyor, yani test kendi kendini doğrulardı
    /// (A13/T4'ün <c>Assert.Equal(140.0, PopIn.DurationMs)</c> deseni: beklenen değer ÜRETİMDEN OKUNMAZ).</summary>
    private const string ClientAreaAnimationPropertyName = "ClientAreaAnimation";

    // ---------------------------------------------------------------- (a) saf değişim filtresi

    [Fact]
    public void The_change_filter_accepts_the_client_area_animation_property()
    {
        Assert.True(SystemParametersMotionSignal.IsMotionProperty(ClientAreaAnimationPropertyName));
    }

    /// <summary><see cref="SystemParameters"/> ONLARCA static property için AYNI <c>StaticPropertyChanged</c>
    /// event'ini yayar; filtre bunların HİÇBİRİNDE tetiklememelidir (aksi halde her ekran/tema/DPI değişimi bir
    /// motion tazelemesi sürerdi). Aşağıdakiler gerçek <see cref="SystemParameters"/> üyeleridir — uydurma değil.
    /// <para><c>null</c>/boş ad da yasaktır: <c>PropertyChangedEventArgs.PropertyName</c> "hepsi değişti"
    /// anlamında bunlarla gelebilir ve o durumda da bizim sinyalimiz değiştiği İDDİA EDİLEMEZ.</para></summary>
    [Fact]
    public void The_change_filter_rejects_every_other_static_property_notification()
    {
        string?[] others =
        [
            nameof(SystemParameters.WorkArea),
            nameof(SystemParameters.PrimaryScreenWidth),
            nameof(SystemParameters.HighContrast),
            nameof(SystemParameters.MenuShowDelay),
            nameof(SystemParameters.MinimizeAnimation),   // ADI benzer ama BAŞKA bir ayar — en yakın tuzak
            nameof(SystemParameters.DropShadow),
            null,
            "",
            "clientareaanimation",                        // WPF adları büyük/küçük harfe DUYARLIDIR
            ClientAreaAnimationPropertyName + " ",
        ];
        Assert.NotEmpty(others); // vakum yasak: küme boşsa Assert.All trivial yeşil geçerdi

        Assert.All(others, name => Assert.False(SystemParametersMotionSignal.IsMotionProperty(name),
            $"'{name ?? "<null>"}' motion sinyali SAYILDI — filtre sızdırıyor."));
    }

    // ---------------------------------------------------------------- (b) salt-okur getter

    /// <summary>Getter SALT OKURDUR: art arda okumak aynı değeri verir ve OS ayarına HİÇ yazılmaz.
    ///
    /// <para><b>NE PİNLEMEZ (fix round 1 — ölçüldü):</b> getter'ın hangi OS property'sini okuduğunu. Üç assert de
    /// <c>SystemParameters.ClientAreaAnimation</c>'ı kendisiyle karşılaştırır; getter başka bir OS property'sine
    /// çevrilse (ör. <c>MinimizeAnimation</c>) ve o property makinede AYNI değere sahipse bu test bunu GÖREMEZ
    /// (review lens3 M-D bunu bizzat ölçtü). Bu kısıt kalıcıdır: makine-global erişilebilirlik ayarını çevirmek
    /// YASAK olduğundan aynalama davranışsal olarak sınanamaz. <b>Aynalamayı koruyan tek şey</b>
    /// <see cref="Only_one_class_touches_the_os_animation_setting_and_only_twice"/>'in kaynak guard'ıdır —
    /// bu ayrım task-B3-report.md <c>## Concerns</c> C1'de de kayıtlıdır.</para></summary>
    [StaFact]
    public void The_getter_reads_the_os_setting_without_writing_to_it()
    {
        bool osValueBefore = SystemParameters.ClientAreaAnimation;

        var signal = new SystemParametersMotionSignal();

        Assert.Equal(signal.AnimationsEnabled, signal.AnimationsEnabled);   // okuma yan etkisiz (idempotent)
        Assert.Equal(osValueBefore, SystemParameters.ClientAreaAnimation);  // ...ve OS ayarına HİÇ yazılmadı
    }

    // ---------------------------------------------------------------- (c) KABLO + tek-kopya

    private static string ReadAppSource(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, relativePath));

    /// <summary>Üretim yolu SEAM'İ KULLANIR — filtrenin ikinci bir (elle yazılmış) kopyası yoktur. Kopya olsaydı
    /// seam'i test etmek üretimi test etmek olmazdı: seam yeşil kalırken handler'daki kopya sürüklenebilirdi.</summary>
    [Fact]
    public void The_production_handler_routes_the_notification_through_the_pure_filter()
    {
        string source = ReadAppSource(Path.Combine("Services", "SystemParametersMotionSignal.cs"));
        Assert.Contains("if (IsMotionProperty(e.PropertyName))", source, StringComparison.Ordinal);
    }

    /// <summary><c>ClientAreaAnimation</c>'a (yani OS'a) dokunan TEK yer bu sınıftır ve orada da yalnız İKİ
    /// yerde geçer: getter + saf filtre. Üçüncü bir geçiş = ya seam'in kopyası ya da ikinci bir OS okuma yolu —
    /// ikisi de "OS'a dokunan tek sınıf" değişmezini bozar.
    ///
    /// <para><b>[fix round 1] Bu test AYNI ZAMANDA aynalamanın TEK koruyucusudur</b> (bkz.
    /// <see cref="The_getter_reads_the_os_setting_without_writing_to_it"/>: davranışsal aynalama testi, makine-global
    /// ayar çevrilemediği için getter'ın HANGİ property'yi okuduğunu göremez). Bu yüzden yalnız SAYMAK yetmez —
    /// getter'ın gövdesinin gerçekten <c>ClientAreaAnimation</c>'a bağlı olduğu ayrıca assert edilir. Regex
    /// boşluğa toleranslıdır; biçim değişikliği testi kırmaz, <b>bağlantının kopması</b> kırar.</para></summary>
    [Fact]
    public void Only_one_class_touches_the_os_animation_setting_and_only_twice()
    {
        var rule = new Regex(@"SystemParameters\.ClientAreaAnimation", RegexOptions.Compiled);
        var hits = SourceGuard.ScanSrc("*.cs", rule, skipCommentLines: true);

        Assert.NotEmpty(hits); // vakum yasak: tarama dosya görmediyse bu test hiçbir şey pinlemez
        Assert.All(hits, hit => Assert.StartsWith(
            Path.Combine("BuildOrchestrator.App", "Services", "SystemParametersMotionSignal.cs"), hit, StringComparison.Ordinal));

        // İki geçişten biri GETTER'ın KENDİSİDİR — aynalamayı koruyan tek assert budur, bu yüzden SAYIMDAN ÖNCE
        // gelir: getter başka bir OS property'sine çevrildiğinde kırılan ilk (ve tanılayıcı) assert bu olsun.
        string source = ReadAppSource(Path.Combine("Services", "SystemParametersMotionSignal.cs"));
        Assert.Matches(@"AnimationsEnabled\s*=>\s*SystemParameters\.ClientAreaAnimation\s*;", source);

        Assert.Equal(2, hits.Count); // getter + IsMotionProperty — üçüncü geçiş = kopya ya da ikinci OS okuma yolu
    }

    /// <summary>
    /// <b>KABLO:</b> sınıf doğru ama hiçbir yere bağlı değilse değeri sıfırdır. Zincir:
    /// <c>SystemParametersMotionSignal</c> → <c>MotionSettings</c> (<see cref="IMotionSettings"/>) →
    /// <c>App.Motion</c> → <c>MotionGate.StaticAnimationsEnabled</c> → sahiplerin
    /// <c>AnimationsEnabledProvider</c>'ı.
    ///
    /// <para>İlk halka <c>App.OnStartup</c>'tadır ve headless koşulamaz (<see cref="Application"/> kurulamaz —
    /// <c>Shell.StartupArgs</c>/<c>SecondInstanceGate</c> ile AYNI kısıt), bu yüzden kaynaktan pinlenir. Zincirin
    /// geri kalanı DAVRANIŞSAL olarak zaten pinlidir: <c>ReducedMotionTests</c> (IMotionSignal → MotionSettings
    /// canlı yayılım) ve <c>MotionOwnerHygieneTests</c> (MotionGate abonelik kipleri).</para>
    /// </summary>
    [Fact]
    public void The_app_actually_feeds_its_motion_signal_from_this_class()
    {
        string startup = ReadAppSource("App.xaml.cs");
        Assert.Contains("new MotionSettings(new SystemParametersMotionSignal())", startup, StringComparison.Ordinal);
        Assert.Contains("Motion = motion;", startup, StringComparison.Ordinal);
    }
}
