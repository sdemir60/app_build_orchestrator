using System.IO;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [tray indicator/T1] Tepsi göstergesinin GÖRÜNÜRLÜK ve BİTİŞ kararları — saf mantık, WPF'siz.
///
/// <para>Karar kümesi tek yerde toplanır çünkü üç ayrı sinyalin (pencere görünürlüğü · faz · reduced-motion)
/// kesişimidir ve hiçbiri tek başına yeterli değildir: gösterge YALNIZ "pencere gizli VE bir derleme koşuyor"
/// iken görünür. Bu sınıf o kesişimi ve bitiş koreografisinin SIRASINI (çıkış evresi → gizlenme → nefes →
/// balloon) pinler.</para>
///
/// <para><b>Neden view/notifier dikişli:</b> gerçek yüzeyler bir top-level <c>Window</c> ve bir OS tray
/// balloon'udur — ikisi de headless süitte kurulamaz. Controller onlara yalnız iki dar arayüzden konuşur,
/// böylece karar mantığı pencere/HWND olmadan sınanır.</para>
///
/// <para><b>[KALDIRILAN PİN] <c>Counter_updates_flow_only_while_the_overlay_is_shown</c>:</b> gösterge kapalıyken
/// sayacın view'a itilmediğini, açılışta son değerin bir kez aktığını pinliyordu. Gösterge artık sayaç
/// TAŞIMIYOR (kullanıcının görsel testi: overlay ölçüsünde okunmuyordu), <c>ITrayBuildIndicatorView</c>'da
/// <c>UpdateCounter</c> diye bir fiil de yok — pinin konusu ortadan kalktı.</para>
/// </summary>
public sealed class TrayBuildIndicatorControllerTests
{
    // ---------------------------------------------------------------- dikişler

    /// <summary>View + notifier ORTAK bir log'a yazar: bitiş koreografisinin tek değerli iddiası SIRA'dır
    /// ("gizlendikten sonra bildir"), ve sıra ancak iki yüzey aynı şeride yazarsa kanıtlanabilir.</summary>
    private sealed class Recorder
    {
        public readonly List<string> Log = [];
    }

    private sealed class FakeView(Recorder r) : ITrayBuildIndicatorView
    {
        public Action? PendingExit;
        public void ShowLoop() => r.Log.Add("ShowLoop");
        public void ShowStatic() => r.Log.Add("ShowStatic");
        public void BeginExit(Action onFinished) { r.Log.Add("BeginExit"); PendingExit = onFinished; }
        public void HideNow() => r.Log.Add("HideNow");

        /// <summary>Çıkış evresinin bittiği kareyi taklit eder — gerçek view bunu storyboard'un
        /// <c>Completed</c>'ında çağırır.</summary>
        public void FinishExit()
        {
            var callback = PendingExit ?? throw new InvalidOperationException("BeginExit hiç çağrılmadı");
            PendingExit = null;
            callback();
        }
    }

    private sealed class FakeNotifier(Recorder r) : ITrayRunNotifier
    {
        public int Count;
        public RibbonLine? LastLine;

        public void ShowRunFinished(RibbonLine line)
        {
            Count++;
            LastLine = line;
            r.Log.Add($"Notify:{line.Text}");
        }
    }

    /// <summary>Şeridin BAŞARILI biten bir satırını taklit eder — statü ayrı bir bayrak değil, satırın kendi
    /// glyph'idir (<c>RibbonText.Compose</c> da öyle yazar).</summary>
    private static RibbonLine Succeeded(string text) => new(text, "Brush.StatusSuccessText", "succeeded");

    /// <summary>Şeridin BAŞARISIZ biten bir satırı. Bkz. <see cref="Succeeded"/>.</summary>
    private static RibbonLine Failed(string text) => new(text, "Brush.StatusFailText", "failed");

    /// <summary>Ortak kurulum: controller + iki dikiş + paylaşılan log. Varsayılan durum üretimin açılışıdır —
    /// pencere GÖRÜNÜR, faz <see cref="AppPhase.Empty"/>, animasyonlar AÇIK.</summary>
    private sealed class Fixture
    {
        public readonly Recorder Recorder = new();
        public readonly FakeView View;
        public readonly FakeNotifier Notifier;
        public readonly TrayBuildIndicatorController Controller;

        public Fixture()
        {
            View = new FakeView(Recorder);
            Notifier = new FakeNotifier(Recorder);
            Controller = new TrayBuildIndicatorController(View, Notifier);
        }

        public List<string> Log => Recorder.Log;

        /// <summary>Koşan bir derleme, pencere tepside — testlerin çoğunun başlangıç noktası.</summary>
        public Fixture RunningInTray(string terminalText = "Completed — 24 succeeded · 9 skipped · 1m 12s")
        {
            Controller.SetTerminalLine(Succeeded(terminalText));
            Controller.SetMainWindowVisible(false);
            Controller.SetPhase(AppPhase.Running);
            Log.Clear();
            return this;
        }
    }

    // ---------------------------------------------------------------- görünürlük kuralı

    [Fact]
    public void Overlay_shows_when_a_run_is_active_and_the_window_is_hidden()
    {
        var f = new Fixture();

        f.Controller.SetMainWindowVisible(false);
        f.Controller.SetPhase(AppPhase.Running);

        Assert.Equal(1, f.Log.Count(l => l == "ShowLoop"));
    }

    [Fact]
    public void Overlay_appears_when_the_window_hides_mid_run()
    {
        var f = new Fixture();

        f.Controller.SetPhase(AppPhase.Running);        // pencere hâlâ görünür → gösterge YOK
        Assert.DoesNotContain("ShowLoop", f.Log);

        f.Controller.SetMainWindowVisible(false);       // koşu ortasında `X` ile tepsiye

        Assert.Equal(1, f.Log.Count(l => l == "ShowLoop"));
    }

    [Fact]
    public void Overlay_hides_instantly_when_the_window_returns()
    {
        var f = new Fixture().RunningInTray();

        f.Controller.SetMainWindowVisible(true);

        // Kullanıcı zaten ekrana döndü: çıkış evresi OYNATILMAZ, bildirim de üretilmez.
        Assert.Equal(["HideNow"], f.Log);
        Assert.Equal(0, f.Notifier.Count);
    }

    [Theory]
    [InlineData(AppPhase.Empty)]
    [InlineData(AppPhase.Boot)]
    [InlineData(AppPhase.Syncing)]   // [K-4] Sync KAPSAM DIŞI — gösterge yalnız DERLEME koşularınındır
    [InlineData(AppPhase.Idle)]
    [InlineData(AppPhase.Done)]
    [InlineData(AppPhase.Stopped)]
    public void Syncing_and_idle_phases_never_show_the_overlay(AppPhase phase)
    {
        var f = new Fixture();

        f.Controller.SetMainWindowVisible(false);
        f.Controller.SetPhase(phase);

        Assert.Empty(f.Log);
    }

    [Theory]
    [InlineData(AppPhase.Starting)]
    [InlineData(AppPhase.Running)]
    [InlineData(AppPhase.Stopping)]
    public void Every_active_phase_puts_the_overlay_up(AppPhase phase)
    {
        var f = new Fixture();

        f.Controller.SetMainWindowVisible(false);
        f.Controller.SetPhase(phase);

        Assert.Contains("ShowLoop", f.Log);
    }

    // ---------------------------------------------------------------- bitiş koreografisi

    [Fact]
    public void Terminal_while_hidden_plays_the_exit_then_notifies()
    {
        var f = new Fixture().RunningInTray("Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s");

        f.Controller.SetPhase(AppPhase.Done);
        Assert.Equal(["BeginExit"], f.Log);   // döngü YARIM kesilmez: yalnız "yeni tur başlatma" işareti
        Assert.Equal(0, f.Notifier.Count);    // ve balloon çıkış evresinden ÖNCE patlamaz

        f.View.FinishExit();                  // 3.000 s karesi: son şerit yok oldu

        Assert.Equal(
            ["BeginExit", "HideNow", "Notify:Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s"],
            f.Log);
    }

    [Fact]
    public void The_notification_waits_for_the_exit_breath()
    {
        // [K-14] Kaybolma ile bildirim ÜST ÜSTE BİNMEZ: araya kısa bir nefes girer. Nefes enjekte edilebilir
        // bir dikiştir (D8: testte GERÇEK bekleme yok — sahte dikiş senkron tamamlanır).
        var f = new Fixture().RunningInTray();
        int breaths = 0;
        f.Controller.ExitBreath = () => { breaths++; f.Log.Add("Breath"); return Task.CompletedTask; };

        f.Controller.SetPhase(AppPhase.Done);
        f.View.FinishExit();

        Assert.Equal(1, breaths);
        Assert.Equal(["BeginExit", "HideNow", "Breath", "Notify:Completed — 24 succeeded · 9 skipped · 1m 12s"], f.Log);
    }

    [Fact]
    public void The_exit_breath_runs_on_the_reduced_motion_branch_too()
    {
        // [K-14] Reduced-motion'da SIRA aynıdır; değişen yalnız nefesin süresidir (token 0'a düşer) — dikiş
        // yine tam bir kez çağrılır, yani sıra tek bir dalda pinlenmiş olmaz.
        var f = new Fixture();
        f.Controller.SetAnimationsEnabled(false);
        f.RunningInTray();
        int breaths = 0;
        f.Controller.ExitBreath = () => { breaths++; f.Log.Add("Breath"); return Task.CompletedTask; };

        f.Controller.SetPhase(AppPhase.Done);

        Assert.Equal(1, breaths);
        Assert.Equal(["HideNow", "Breath", "Notify:Completed — 24 succeeded · 9 skipped · 1m 12s"], f.Log);
    }

    [Fact]
    public void Terminal_while_visible_shows_no_balloon()
    {
        // Pencere GÖRÜNÜRKEN biten koşu balloon üretmez — şerit zaten oradadır (mevcut davranış değişmez).
        var f = new Fixture();
        f.Controller.SetTerminalLine(Succeeded("Completed — 24 succeeded · 9 skipped · 1m 12s"));
        f.Controller.SetPhase(AppPhase.Running);   // pencere görünür
        f.Log.Clear();

        f.Controller.SetPhase(AppPhase.Done);

        Assert.Empty(f.Log);
        Assert.Equal(0, f.Notifier.Count);
    }

    [Fact]
    public void Window_restore_during_exit_still_notifies_exactly_once()
    {
        // Çıkış evresi koşarken kullanıcı pencereyi geri getirdi: gösterge ANINDA gizlenir ama BALLOON TAAHHÜDÜ
        // korunur — terminal anında tepsideydik. Çift balloon da YASAK.
        var f = new Fixture().RunningInTray();

        f.Controller.SetPhase(AppPhase.Done);       // BeginExit uçuşta
        f.Controller.SetMainWindowVisible(true);    // pencere geri geldi
        f.View.FinishExit();                        // çıkış evresi yine de kendi bitişine koştu

        Assert.Equal(1, f.Notifier.Count);
    }

    [Fact]
    public void A_second_terminal_event_never_produces_a_second_balloon()
    {
        var f = new Fixture().RunningInTray();

        f.Controller.SetPhase(AppPhase.Done);
        f.View.FinishExit();
        f.Controller.SetPhase(AppPhase.Stopped);    // motorun ikinci bir terminal sinyali (bilinen davranış değil)

        Assert.Equal(1, f.Notifier.Count);
    }

    [Fact]
    public void Starting_reverting_to_idle_closes_the_overlay_and_reports()
    {
        // [K-4] BeginRunAsync yerel bir hatayla Starting → Idle'a döner. Bu da AKTİF KÜMEDEN ÇIKIŞTIR: özel dal
        // YAZILMAZ, balloon o anki şerit metnini (bir hata satırıdır) taşır.
        var f = new Fixture();
        f.Controller.SetTerminalLine(Failed("Run failed — engine did not start"));
        f.Controller.SetMainWindowVisible(false);
        f.Controller.SetPhase(AppPhase.Starting);
        f.Log.Clear();

        f.Controller.SetPhase(AppPhase.Idle);
        f.View.FinishExit();

        Assert.Equal(["BeginExit", "HideNow", "Notify:Run failed — engine did not start"], f.Log);
    }

    /// <summary>
    /// Bildirimciye SATIRIN KENDİSİ ulaşır — metni ve statü glyph'i bir arada, bozulmadan.
    ///
    /// <para><b>[DEĞİŞEN KURAL]</b> Bu pin eskiden <c>Healthy_flag_reaches_the_notifier</c> idi: iddiası
    /// satırın SAĞLIK BAYRAĞININ (<c>ShowRunFinished(string, bool)</c>'ın ikinci parametresi) bildirimciye
    /// ulaştığı ve orada Info/Error ikonunu seçtiğiydi. İkisi de artık yok. Bildirim her sonuçta ürünün kendi
    /// ikonunu taşıyor, yani sağlık hiçbir şeyi sürmüyor; <c>RibbonLine.Healthy</c> üretimde tüketicisiz
    /// kaldığı için kaldırıldı (gerekirse tek satırda geri gelir). Geriye kalan — ve pinlenmeye DEVAM eden —
    /// şey, tepsiye giden şeyin bir metin+bayrak çifti değil satırın ta kendisi olduğudur: statü de şeridin
    /// TEK statü sinyalinde, yani <c>Glyph</c>'te durur.</para></summary>
    [Fact]
    public void The_terminal_line_itself_reaches_the_notifier()
    {
        const string text = "Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s";
        var f = new Fixture();
        f.Controller.SetTerminalLine(Failed(text));
        f.Controller.SetMainWindowVisible(false);
        f.Controller.SetPhase(AppPhase.Running);

        f.Controller.SetPhase(AppPhase.Done);
        f.View.FinishExit();

        Assert.Equal(text, f.Notifier.LastLine?.Text);
        Assert.Equal("failed", f.Notifier.LastLine?.Glyph);
    }

    // ---------------------------------------------------------------- reduced motion

    [Fact]
    public void Reduced_motion_shows_the_static_frame_and_skips_the_exit_animation()
    {
        var f = new Fixture();
        f.Controller.SetAnimationsEnabled(false);
        f.Controller.SetTerminalLine(Succeeded("Completed — 24 succeeded · 9 skipped · 1m 12s"));
        f.Controller.SetMainWindowVisible(false);

        f.Controller.SetPhase(AppPhase.Running);
        Assert.Contains("ShowStatic", f.Log);
        Assert.DoesNotContain("ShowLoop", f.Log);

        f.Log.Clear();
        f.Controller.SetPhase(AppPhase.Done);

        // Animasyon istemeyen kullanıcı bekletilmez: çıkış evresi HİÇ oynatılmaz, gizlenme anında olur.
        Assert.DoesNotContain("BeginExit", f.Log);
        Assert.Equal(["HideNow", "Notify:Completed — 24 succeeded · 9 skipped · 1m 12s"], f.Log);
    }

    [Fact]
    public void Motion_setting_flips_live_while_the_overlay_is_up()
    {
        // [K-11] OS ayarı gösterge EKRANDAYKEN değişebilir; döngü kurulup bırakılmaz.
        var f = new Fixture().RunningInTray();

        f.Controller.SetAnimationsEnabled(false);
        Assert.Equal(["ShowStatic"], f.Log);

        f.Log.Clear();
        f.Controller.SetAnimationsEnabled(true);
        Assert.Equal(["ShowLoop"], f.Log);
    }

    [Fact]
    public void A_motion_flip_while_the_overlay_is_down_touches_no_surface()
    {
        var f = new Fixture();   // pencere görünür → gösterge yok

        f.Controller.SetAnimationsEnabled(false);
        f.Controller.SetAnimationsEnabled(true);

        Assert.Empty(f.Log);
    }

    // ---------------------------------------------------------------- saflık

    [Fact]
    public void The_controller_carries_no_wpf_type()
    {
        // Kabul ölçütü: karar mantığı pencere/HWND'den BAĞIMSIZ kalır (headless süitte tam kapsam). Bir
        // `using System.Windows…` sızarsa bu sınıf artık saf değildir ve testleri bir STA host'u ister.
        string source = File.ReadAllText(
            Path.Combine(RepoPaths.AppSrcRoot, "Services", "TrayBuildIndicatorController.cs"));

        Assert.DoesNotContain("using System.Windows", source, StringComparison.Ordinal);
    }
}
