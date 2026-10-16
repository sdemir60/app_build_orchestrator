using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;
using static BuildOrchestrator.Tests.App.MainWindowHost;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory dalga 3 / spec §6.4 — Task 19] Settings → General → ENGINE: <c>Stop the engine when idle</c>. Açıkken (varsayılan)
/// beş dakika işsiz kalan motor süreci çıkar ve sonraki iş onu yeniden başlatır; kapalıyken motor kendiliğinden hiç durdurulmaz.
///
/// <para>Burada yalnız bu ayara özgü kurallar durur: (a) "beş dakika"nın TEK sahibi <see cref="EngineIdleRule.IdleAfter"/>'dır — katalog
/// açıklaması (spec'in birebir cümlesi) ile "on" notu sayıyı sözcükle yazar; sabitin literal pini kuralın testindedir
/// (<see cref="EngineIdleRuleTests"/>, karar 29), iki metnin sabitle ayrışmasını bu sınıfın testi yakalar; (b) etiket yalnız katalogda
/// literaldir; (c) anahtarın ayar dosyasındaki yeri; (d) fixture ayarı varsayılan KAPALI açar (<see cref="MainWindowHost.New"/>'in
/// <c>stopEngineWhenIdle</c> parametresi) — saati ileri alan testler fixture motorunu uyutmasın. Ayarın zinciri — katalog → taslak → ayar
/// dosyası → Save (UiState + <see cref="RunViewModel.StopEngineWhenIdle"/> aynı commit'te) → kabuğun açılış tohumu ve kalıcılığı — öteki
/// General ayarlarıyla birlikte <see cref="GeneralSettingChainTests"/>'tedir. Uykunun KENDİSİ bu sınıfın konusu değildir: boşta kuralı ve
/// tik <see cref="EngineSleepTests"/>'te, uyandırma kapısı <see cref="EngineWakeTests"/>'te.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class StopEngineWhenIdleSettingsTests
{
    private static readonly string Label = GeneralSettingChains.Of(GeneralSetting.StopEngineWhenIdle).Label;

    /// <summary>
    /// Ayarın etiketi kataloğun satırında BİR KEZ yazılır; not satırı (<c>RunViewModel</c>) ve başka hiçbir üretim kaynağı onu literal
    /// olarak tekrarlamaz — etiket değişirse not kendiliğinden izler. Tarama yalnız string literallerine bakar (yorumlar hariç); vakum karşıtı
    /// olarak katalogdaki literalin bulunduğu ayrıca doğrulanır.
    /// <para>KIRMIZI (geçici bozma M7: nottaki etiket literal yazıldı): ihlal dosya:satırıyla raporlandı (<c>RunViewModel.ActionBar.cs:241</c>).</para>
    /// </summary>
    [Fact]
    public void The_label_is_a_literal_only_in_the_catalog()
    {
        var rule = new Regex(Regex.Escape(Label), RegexOptions.CultureInvariant);
        string catalogFile = Path.Combine("BuildOrchestrator.App", "ViewModels", "GeneralSettings.cs");

        Assert.NotEmpty(SourceGuard.ScanSrcLiterals("GeneralSettings.cs", rule)); // tarama kataloğun literalini GÖRÜYOR
        var offenders = SourceGuard.ScanSrcLiterals("*.cs", rule, allowedFiles: [catalogFile]);

        Assert.True(offenders.Count == 0,
            "etiket kataloğun dışında literal olarak yazılmış (kopya YASAK):" + Environment.NewLine + "  "
            + string.Join(Environment.NewLine + "  ", offenders));
    }

    // ---------------------------------------------------------------- "beş dakika" tek sahip

    // [karar 29] Sabitin literal pini (5 dk) kuralın sınıfıyla birlikte durur: EngineIdleRuleTests.The_idle_window_stays_five_minutes.
    // Burada kalan, iki kullanıcı metninin o sabitle uyuşmasıdır.

    /// <summary>
    /// [karar 11 · C18] Katalog açıklaması (spec'in birebir cümlesi) ve "on" notu süreyi SÖZCÜKLE yazar; ikisi de sabitin bugünkü değeriyle
    /// aynı sözcüğü söylemelidir. Sabit değişirse (ör. 10 dk) bu test, iki metin ve aşağıdaki sözcük tablosu birlikte güncellenene dek
    /// kırmızıdır — metin sessizce yalan olamaz. "Beş" sözcüğü yalnız bu tabloda (ve iki metinde) yazılıdır.
    /// <para>KIRMIZI (iskelet, RED-B — not yazılmıyordu): <c>Not found: "the engine exits after five idle minutes"</c>; geçici bozma M4
    /// (<c>IdleAfter</c> 6 dk): <c>IdleAfter 6 dakika oldu: … birlikte güncellenmeli</c>.</para>
    /// </summary>
    [Fact]
    public async Task The_catalog_text_and_the_on_note_say_the_window_the_rule_uses()
    {
        string words = EngineIdleRule.IdleAfter.TotalMinutes switch
        {
            5 => "five",
            double minutes => throw new Xunit.Sdk.XunitException(
                $"EngineIdleRule.IdleAfter {minutes} dakika oldu: katalog açıklaması, \"on\" notu ve bu tablo birlikte güncellenmeli."),
        };

        Assert.Contains($"After {words} minutes without work", SettingsDialogHost.CatalogRow(Label).Description, StringComparison.Ordinal);

        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var run = new RunViewModel(engine, NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo", StopEngineWhenIdle = false };
        await run.ApplySettingsAsync([], @"D:\repo", [], stopEngineWhenIdle: true);
        Assert.Contains($"the engine exits after {words} idle minutes", run.GetRunDocumentText(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- dosya biçimi

    /// <summary>Anahtarın ayar dosyasındaki yeri pinlidir: System.Text.Json alanları BİLDİRİM sırasıyla yazar — <c>compactConsole</c>'dan
    /// hemen sonra, kabuk anahtarlarının ilki olan <c>startWithWindows</c>'ten önce (<c>SettingsFile</c>'ın sınıf özeti bu sırayı söyler).
    /// <para>[B3 · katlama] Eski <c>The_setting_round_trips_through_the_file</c>'ın gidiş-dönüş yarısı (Export anahtarı yazar, Import geri
    /// okur) öteki ayarlarla birlikte <see cref="GeneralSettingChainTests.Every_setting_round_trips_through_the_settings_file"/>'a katlandı;
    /// burada bu ayara özgü yarısı — anahtarın yeri — kalır.</para></summary>
    [Fact]
    public void The_setting_is_written_after_compact_console_and_before_the_shell_switches()
    {
        string json = new SettingsDraftViewModel(null, @"D:\repo", stopEngineWhenIdle: false).ToFile().ToJson();

        int compactIndex = json.IndexOf("\"compactConsole\"", StringComparison.Ordinal);
        int engineIndex = json.IndexOf("\"stopEngineWhenIdle\"", StringComparison.Ordinal);
        int shellIndex = json.IndexOf("\"startWithWindows\"", StringComparison.Ordinal);
        Assert.True(compactIndex >= 0 && compactIndex < engineIndex,
            $"stopEngineWhenIdle ({engineIndex}) compactConsole'dan ({compactIndex}) SONRA gelmeli");
        Assert.True(engineIndex < shellIndex, $"stopEngineWhenIdle ({engineIndex}) startWithWindows'tan ({shellIndex}) ÖNCE gelmeli");
    }

    // ---------------------------------------------------------------- fixture

    /// <summary>
    /// [karar 9] Fixture sözleşmesi: <see cref="MainWindowHost.New"/> ayarı KAPALI açar (üretimin varsayılanı açıktır) — saati beş
    /// dakikadan fazla ileri alan testlerin fixture motorunu uyutmaması buna bağlıdır; uyku yalnız bunu açıkça isteyen testte
    /// (<c>stopEngineWhenIdle: true</c>) işler. Saatli topolojili kurulum (<see cref="MainWindowHost.NewWithProjectsAndClock"/>) da
    /// aynı varsayılandan başlar: uyku testleri o yoldan gelir.
    /// <para>KIRMIZI (geçici bozma M5: fikstürün ataması silindi): fikstür ayarı açık açtı.</para>
    /// </summary>
    [StaFact]
    public void The_fixture_opens_with_the_setting_off_unless_the_test_opts_in()
    {
        using var defaultDir = new TempDir();
        var (offWindow, offVm) = New(defaultDir);
        Assert.False(offVm.StopEngineWhenIdle);

        using var optInDir = new TempDir();
        var (onWindow, onVm) = New(optInDir, stopEngineWhenIdle: true);
        Assert.True(onVm.StopEngineWhenIdle);

        using var clockedDir = new TempDir();
        var (clockedWindow, clockedVm, _) = NewWithProjectsAndClock(clockedDir, () => 0L, ("A", null));
        Assert.False(clockedVm.StopEngineWhenIdle);

        GC.KeepAlive(offWindow);
        GC.KeepAlive(onWindow);
        GC.KeepAlive(clockedWindow);
    }
}
