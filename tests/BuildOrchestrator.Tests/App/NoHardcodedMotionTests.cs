using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T49 FINAL PASS] <see cref="NoHardcodedColorTests"/>'in SÜRE kardeşi. Motion sözleşmesi (Global Constraints)
/// "hardcoded hex/ms YASAK" der ama bugüne dek YALNIZ hex tarafı korunuyordu — bu sınıf ms tarafını kapatır.
///
/// <para>Guard İKİ kalıba bakar, ikisi de "bir animasyon süresini kaynağından koparıp koda gömme" hareketidir:</para>
/// <list type="number">
/// <item><b>XAML:</b> bir Storyboard süresinin literal yazılması (<c>Duration="0:0:0.18"</c>, <c>BeginTime</c>,
/// <c>KeyTime</c>). Süre <c>{DynamicResource Duration.X}</c> olmalıdır — <c>{StaticResource}</c> bile YASAK,
/// çünkü reduced-motion sinyali <see cref="BuildOrchestrator.App.Services.MotionSettings"/> ile Duration
/// kaynaklarını CANLI 0'a çeker; statik bağ o mutasyonu görmez.</item>
/// <item><b>C#:</b> bir animasyon süresinin/keyframe zamanının ÇAĞRI YERİNDE sayı olarak doğması
/// (<c>new Duration(TimeSpan.FromSeconds(0.55))</c>). Süreler adlandırılmış bir sabitten ya da token'dan
/// gelmelidir — <c>StatusGlyph.PulseMs</c> / <c>BuildingSpinner.RotationMs</c> deseni.</item>
/// </list>
///
/// <para><b>KAPSAM DIŞI (bilinçli):</b> animasyon OLMAYAN ms literalleri — <c>DispatcherTimer.Interval</c>
/// (kare bütçesi), IPC/shutdown timeout'ları, pipe retry gecikmesi. Bunlar tasarım token'ı değildir; hepsini
/// yasaklamak guard'ı gürültüye boğar ve sinyali öldürürdü. Aynı şekilde <c>TimeSpan.Zero</c> ve
/// <c>MotionTokens.ResolveDuration</c>'ın <c>fallbackMs</c> PARAMETRESİ (literal değil, çağıranın verdiği
/// adlandırılmış sabit) kapsam dışıdır.</para>
///
/// <para><b>YAKALAYAMADIĞI:</b> adlandırılmış bir sabitin YANLIŞ değer taşıması (ör. <c>PulseMs = 1500</c>) —
/// onu ancak prototip referansıyla karşılaştıran bir insan/inceleme yakalar; bu guard yalnız "değer nereye
/// yazılmış" sorusunu denetler, "değer doğru mu" sorusunu değil.</para>
/// </summary>
public sealed class NoHardcodedMotionTests
{
    /// <summary>XAML'de literal zaman değeri: <c>Duration="0:0:0.18"</c> / <c>BeginTime="…"</c> /
    /// <c>KeyTime="…"</c>. Değer <c>{</c> ile başlıyorsa markup extension'dır (DynamicResource) → temizdir.</summary>
    private static readonly Regex XamlTimeLiteral = new(
        "\\b(?:Duration|BeginTime|KeyTime|RepeatBehavior)\\s*=\\s*\"(?!\\s*\\{)[^\"]*[0-9][^\"]*\"", RegexOptions.Compiled);

    /// <summary>
    /// C#'ta animasyon süresinin/keyframe zamanının literal doğması. Yalnız <c>Duration</c>/<c>BeginTime</c>/
    /// <c>KeyTime</c> BAĞLAMI taranır — çıplak <c>TimeSpan.FromMilliseconds(15)</c> (timer interval'i) değil.
    ///
    /// <para>[fix round 1 · A2] İKİNCİ biçim eklendi: <c>Duration = TimeSpan.FromMilliseconds(140)</c>. WPF'te
    /// <c>TimeSpan</c>'den <c>Duration</c>'a ÖRTÜK dönüşüm vardır — yani <c>new Duration(...)</c> yazmadan da
    /// süre atanabilir; önceki sürüm bu yolu HİÇ görmüyordu ve kaçak tam oradan olurdu.</para>
    /// </summary>
    private static readonly Regex CodeTimeLiteral = new(
        "(?:new\\s+Duration|KeyTime\\.FromTimeSpan)\\(\\s*TimeSpan\\.From(?:Milli)?[Ss]econds\\(\\s*[0-9]" +
        "|(?:Duration|BeginTime|KeyTime)\\s*=\\s*TimeSpan\\.From(?:Milli)?[Ss]econds\\(\\s*[0-9]",
        RegexOptions.Compiled);

    [Fact]
    public void No_xaml_declares_a_literal_animation_time_instead_of_a_duration_token()
        => Assert.Empty(SourceGuard.ScanApp("*.xaml", XamlTimeLiteral));

    [Fact]
    public void No_cs_file_builds_an_animation_duration_from_a_literal_at_the_call_site()
        => Assert.Empty(SourceGuard.ScanApp("*.cs", CodeTimeLiteral, skipCommentLines: true));

    [Fact]
    public void The_guard_actually_scans_the_files_it_claims_to()
    {
        // Tarama boş dönerse iki test de SESSİZCE yeşil kalırdı (yol/filtre bozulması) — NoHardcodedColorTests'teki
        // meta-test ile aynı gerekçe. Motion'ın gerçekten koda yazıldığı iki dosyanın taramaya girdiği pinlenir.
        var xaml = SourceGuard.ScannedAppFiles("*.xaml");
        var cs = SourceGuard.ScannedAppFiles("*.cs");

        Assert.Contains(Path.Combine("Resources", "Controls.xaml"), xaml);
        Assert.Contains(Path.Combine("Controls", "MotionTokens.cs"), cs);
        Assert.Contains(Path.Combine("Controls", "StatusGlyph.cs"), cs);
    }

    [Theory]
    [InlineData("<DoubleAnimation Duration=\"0:0:0.18\" />", true)]
    [InlineData("<DoubleAnimation BeginTime=\"0:0:0.05\" />", true)]
    [InlineData("<SplineDoubleKeyFrame KeyTime=\"0:0:0.9\" />", true)]
    [InlineData("<DoubleAnimation Duration=\"{DynamicResource Duration.Base}\" />", false)]
    [InlineData("<DoubleAnimation Duration=\" {DynamicResource Duration.Fast}\" />", false)]
    [InlineData("<!-- süre 180ms (effects.css:10) -->", false)]     // yorumdaki ms değeri
    [InlineData("<Rectangle Width=\"1\" Height=\"14\" />", false)]  // ölçü attribute'u — süre değil
    public void Xaml_regex_separates_literal_times_from_duration_tokens(string sample, bool isLiteral)
        => Assert.Equal(isLiteral, XamlTimeLiteral.IsMatch(sample));

    [Theory]
    [InlineData("new Duration(TimeSpan.FromSeconds(0.55))", true)]
    [InlineData("KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(140))", true)]
    [InlineData("new Duration(TimeSpan.FromMilliseconds(RotationMs))", false)]     // adlandırılmış sabit
    [InlineData("new Duration(TimeSpan.FromMilliseconds(fallbackMs))", false)]     // çağıranın verdiği sabit
    [InlineData("KeyTime.FromTimeSpan(TimeSpan.Zero)", false)]                     // sıfır — süre değil, çıpa
    [InlineData("KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(PulseMs / 2))", false)]
    [InlineData("new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) }", false)] // kare bütçesi, kapsam dışı
    [InlineData("anim.Duration = TimeSpan.FromMilliseconds(140);", true)]        // [A2] örtük TimeSpan→Duration
    [InlineData("anim.BeginTime = TimeSpan.FromSeconds(0.08);", true)]           // [A2] örtük atama
    [InlineData("anim.Duration = TimeSpan.FromMilliseconds(RevealMs);", false)]  // adlandırılmış sabit
    public void Code_regex_only_fires_inside_a_duration_or_keytime_context(string sample, bool isLiteral)
        => Assert.Equal(isLiteral, CodeTimeLiteral.IsMatch(sample));

    /// <summary>
    /// [fix round 1 · A2] <b>Guard'ın kendisinin kanıtı.</b> Tarama testleri BUGÜN yeşil — yani "ihlal yok" ile
    /// "guard hiç ateşlemiyor" ayırt edilemez. Sahte bir kaynakta İKİ ihlal kurulur (biri örtük dönüşüm biçimi)
    /// ve İKİSİNİN de doğru satırla raporlandığı doğrulanır; tekli <c>Match</c> ile ikincisi görünmezdi.
    /// </summary>
    [Fact]
    public void The_guard_reports_every_violation_in_a_file_not_just_the_first()
    {
        const string fake = """
            internal static class Fake
            {
                private static readonly Duration A = new Duration(TimeSpan.FromSeconds(0.55));
                // yorumdaki new Duration(TimeSpan.FromSeconds(0.2)) sayılmaz
                private static void Set(DoubleAnimation a) => a.Duration = TimeSpan.FromMilliseconds(140);
            }
            """;

        var offenders = SourceGuard.ScanText("Fake.cs", fake, CodeTimeLiteral, skipCommentLines: true);

        Assert.Equal(2, offenders.Count);
        Assert.StartsWith("Fake.cs:3: ", offenders[0]);
        Assert.StartsWith("Fake.cs:5: ", offenders[1]);
    }

    /// <summary>
    /// [fix round 2] ÇOK SATIRLI ihlal de yakalanmalı — round 1'in satır-bazlı taraması, argümanı bir alt
    /// satıra taşınmış bir süreyi HİÇ görmüyordu (aynı kör nokta üç guard'da birdendi).
    /// </summary>
    [Fact]
    public void The_guard_catches_a_duration_literal_that_is_split_across_lines()
    {
        const string fake = """
            internal static class Fake
            {
                private static readonly Duration A = new Duration(
                    TimeSpan.FromSeconds(0.55));
            }
            """;

        var offenders = SourceGuard.ScanText("Fake.cs", fake, CodeTimeLiteral, skipCommentLines: true);

        Assert.Single(offenders);
        Assert.StartsWith("Fake.cs:3: ", offenders[0]);
    }
}
