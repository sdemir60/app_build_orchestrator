using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T59] BottomAnchorBehavior — <see cref="BottomAnchorDecision"/>'ı delege'lerle (WPF türü YOK) gerçek bir scroll
/// host'a bağlayan orkestratör. Tamamı delege-tabanlı olduğundan (ScrollViewer/TextEditor'a dokunmaz) D8'e uygun
/// PLAIN <c>[Fact]</c> — <c>scheduleOnce</c> enjekte edilerek 560ms'lik pencere SENKRON tetiklenir (gerçek bekleme yok).
/// </summary>
public class BottomAnchorBehaviorTests
{
    private sealed class Fake
    {
        public double Offset;
        public double Extent = 1000;
        public double Viewport = 200;
        public readonly List<double> InstantScrolls = [];
        public double? SmoothTarget;
        public bool SmoothAnimates = true; // scrollSmooth'un dönüş değeri (host: ScrollAnimator.AnimateTo sonucu)
        public Action? PendingSchedule;
        public TimeSpan? PendingDelay;

        public BottomAnchorBehavior New(double thresholdPx = BottomAnchorDecision.DefaultThresholdPx) => new(
            getOffset: () => Offset,
            getExtent: () => Extent,
            getViewport: () => Viewport,
            scrollInstant: v => { InstantScrolls.Add(v); Offset = v; },
            scrollSmooth: target => { SmoothTarget = target; return SmoothAnimates; },
            scheduleOnce: (delay, cb) => { PendingDelay = delay; PendingSchedule = cb; },
            thresholdPx: thresholdPx);
    }

    [Fact]
    public void Initial_state_is_stuck_matching_ConsoleView_previous_default()
    {
        var f = new Fake();
        var behavior = f.New();

        Assert.True(behavior.IsStuck); // eski `StickToBottom = true` varsayılanıyla BİREBİR
    }

    [Fact]
    public void DistanceFromBottom_is_extent_minus_offset_minus_viewport()
    {
        var f = new Fake { Extent = 1000, Offset = 300, Viewport = 200 };
        var behavior = f.New();

        Assert.Equal(500, behavior.DistanceFromBottom);
    }

    [Fact]
    public void OnScrollChanged_with_content_growth_while_stuck_performs_an_instant_catch_up()
    {
        var f = new Fake { Extent = 1000, Offset = 800, Viewport = 200 }; // dipte (distance=0)
        var behavior = f.New();
        Assert.True(behavior.IsStuck);

        f.Extent = 1050; // içerik büyüdü (+50)
        behavior.OnScrollChanged(extentHeightChange: 50);

        Assert.Single(f.InstantScrolls);
        Assert.Equal(1050, f.InstantScrolls[0]); // ANINDA yeni dibe (AppendBatch/ScrollToEnd deseni — animasyon YOK)
    }

    [Fact]
    public void OnScrollChanged_with_content_growth_while_free_does_not_scroll()
    {
        var f = new Fake { Extent = 1000, Offset = 100, Viewport = 200 }; // dipten uzak
        var behavior = f.New();
        behavior.ForceStuck(false);

        f.Extent = 1050;
        behavior.OnScrollChanged(extentHeightChange: 50);

        Assert.Empty(f.InstantScrolls); // serbestken içerik büyümesi TAKİP ETMEZ
    }

    [Fact]
    public void OnScrollChanged_user_scroll_beyond_threshold_releases_and_raises_Changed()
    {
        var f = new Fake { Extent = 1000, Offset = 0, Viewport = 200 }; // distance=800
        var behavior = f.New();
        int changedCount = 0;
        behavior.Changed += (_, _) => changedCount++;

        behavior.OnScrollChanged(extentHeightChange: 0);

        Assert.False(behavior.IsStuck);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void ShowPill_true_when_far_and_not_jumping()
    {
        var f = new Fake { Extent = 1000, Offset = 0, Viewport = 200 };
        var behavior = f.New();

        behavior.OnScrollChanged(extentHeightChange: 0); // → free, distance=800

        Assert.True(behavior.ShowPill);
    }

    [Fact]
    public void JumpToBottom_when_animated_enters_jumping_and_hides_pill_until_window_elapses()
    {
        var f = new Fake { Extent = 1000, Offset = 0, Viewport = 200 };
        var behavior = f.New();
        behavior.OnScrollChanged(extentHeightChange: 0); // free + pill visible
        Assert.True(behavior.ShowPill);

        behavior.JumpToBottom();

        Assert.Equal(800, f.SmoothTarget); // extent - viewport
        Assert.True(behavior.IsJumping);
        Assert.False(behavior.ShowPill); // Ek A-15: jumping penceresinde pill gizli
        Assert.Equal(TimeSpan.FromMilliseconds(BottomAnchorDecision.JumpingWindowMs), f.PendingDelay);

        f.PendingSchedule!(); // 560ms pencere doldu (enjekte edilen scheduleOnce senkron tetiklenir — D8)

        Assert.False(behavior.IsJumping);
        Assert.True(behavior.IsStuck);
    }

    [Fact]
    public void JumpToBottom_when_instant_reduced_motion_sticks_immediately_without_a_jumping_window()
    {
        var f = new Fake { Extent = 1000, Offset = 0, Viewport = 200, SmoothAnimates = false };
        var behavior = f.New();

        behavior.JumpToBottom();

        Assert.True(behavior.IsStuck);
        Assert.False(behavior.IsJumping);
        Assert.Null(f.PendingSchedule); // reduced-motion'da jumping penceresi hiç KURULMAZ
    }

    [Fact]
    public void A_second_JumpToBottom_invalidates_the_first_scheduled_window_generation()
    {
        var f = new Fake { Extent = 1000, Offset = 0, Viewport = 200 };
        var behavior = f.New();

        behavior.JumpToBottom();
        var staleCallback = f.PendingSchedule!;
        behavior.JumpToBottom(); // yeni jump — eskisinin zamanlanmış callback'i artık BAYAT

        staleCallback(); // eski (bayat) callback'in fire olması yeni jump'ı KESMEMELİ
        Assert.True(behavior.IsJumping); // hâlâ (2.) jump'ın penceresindeyiz
    }

    [Fact]
    public void ForceStuck_bypasses_distance_recompute()
    {
        var f = new Fake { Extent = 1000, Offset = 0, Viewport = 200 }; // distance=800 (uzak)
        var behavior = f.New();

        behavior.ForceStuck(true); // 3b'nin `view.StickToBottom = true` elle atamasıyla BİREBİR

        Assert.True(behavior.IsStuck);
        Assert.False(behavior.IsJumping);
    }

    // ---------------------------------------------------------------- [I-1] chunk-prepend guard-logic

    [Fact]
    public void Growth_arriving_after_IsStuck_was_freshly_released_in_the_same_dispatch_does_not_re_stick()
    {
        // [I-1 guard-logic] ConsoleView.OnScrollOffsetChanged bir chunk-prepend'i (EvaluateChunkScroll) tetiklemeden
        // ÖNCE bottom-anchor'ın IsStuck'ını tazeler (bkz. ConsoleView.xaml.cs "[I-1 fix]" yorumu) — TAM OLARAK bu
        // yüzden: kullanıcı dipteyken (IsStuck=true) TEK bir olayda tepeye zıplarsa (Ctrl+Home), o olayın kendi
        // extentHeightChange==0 (saf offset değişimi) recompute'u ÖNCE çalışıp IsStuck'ı false'a çevirir; prepend'in
        // KENDİ içerik-büyümesi (extentHeightChange>0) — ister AYNI olayda senkron gözlemlensin ister AvalonEdit'in
        // layout'u geciktirmesiyle SONRAKİ bir olayda — artık STALE-true bir IsStuck'a çarpmaz, CompensatedOffset'i
        // ezen bir "dibe git" YANKI tetiklemez. Bu, ConsoleView seviyesinde gerçek AvalonEdit scroll geometrisiyle
        // güvenilir biçimde yeniden üretilemeyen (bkz. ConsoleViewTests.Real_OnScrollOffsetChanged_path_... dürüstlük
        // notu) SIRA korumasının SAF çekirdeğidir — WPF'siz, deterministik olarak burada kanıtlanır.
        var f = new Fake { Extent = 1000, Offset = 900, Viewport = 200 }; // dipte (distance=0) → IsStuck kalır true
        var behavior = f.New();
        Assert.True(behavior.IsStuck);

        // Aynı "olay": önce kullanıcı-scroll payı (extentHeightChange==0) tepeye zıplamayı yansıtır → IsStuck taze
        // false olur (ConsoleView'ın chunk-scroll'dan ÖNCE çalışan recompute'unun karşılığı).
        f.Offset = 0; // tepeye zıpladı
        behavior.OnScrollChanged(extentHeightChange: 0);
        Assert.False(behavior.IsStuck, "tepeye zıplama sonrası IsStuck taze false OLMALI (prepend'den ÖNCE)");

        // ...HEMEN ARDINDAN prepend'in kendi büyümesi gelir (chunk loader tepeye içerik ekledi) — ister aynı ister
        // sonraki bir "olayda" gözlemlensin, artık STALE-true bir IsStuck'a çarpmıyor.
        f.Extent = 1300; // +300px prepend edilen dilimin piksel yüksekliği
        behavior.OnScrollChanged(extentHeightChange: 300);

        Assert.False(behavior.IsStuck, "prepend büyümesi, ZATEN taze-false olan IsStuck'ı YANLIŞLIKLA true'ya döndürmemeli");
        Assert.Empty(f.InstantScrolls); // CompensatedOffset'i ezen bir "dibe git" YOK — I-1'in önlediği tam da bu
    }

    [Fact]
    public void Growth_arriving_while_IsStuck_is_still_stale_true_does_yank_to_bottom_illustrating_the_I1_risk()
    {
        // [I-1 guard-logic — KONTRAST] Bunun TERSİ: eğer IsStuck recompute'u prepend'den ÖNCE çalışmasaydı (ESKİ
        // — düzeltilmemiş sıra), tepeye zıplama sonrası hâlâ stale-true bir IsStuck, prepend'in extentHeightChange
        // >0'ıyla karşılaşır — davranış (bu sınıfın kendisi, ConsoleView'dan bağımsız SAF) BUNU YAKALAR: dibe anında
        // bir kaydırma TETİKLENİR. Bu test, I-1'in düzeltmediği (SIRA yanlışken) durumda gerçekten neyin ters
        // gideceğini SOMUT gösterir — ConsoleView'ın düzeltmesi tam olarak yukarıdaki testteki SIRAYI garanti eder.
        var f = new Fake { Extent = 1000, Offset = 900, Viewport = 200 }; // dipte, IsStuck=true
        var behavior = f.New();

        // Prepend'in büyümesi, IsStuck HENÜZ taze-false OLMADAN (yanlış/eski sıra) gözlemlenirse:
        f.Extent = 1300;
        behavior.OnScrollChanged(extentHeightChange: 300); // extentHeightChange>0 → IsStuck DEĞİŞMEZ (hâlâ stale-true)

        Assert.True(behavior.IsStuck); // stale — kullanıcı aslında tepeye zıplamıştı, ama bu henüz yansımadı
        Assert.Single(f.InstantScrolls);
        Assert.Equal(1300, f.InstantScrolls[0]); // YANK: CompensatedOffset'i ezip dibe (extent) kaydırır — I-1 riski
    }
}
