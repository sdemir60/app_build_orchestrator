using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Kullanıcı kaydırıp elini çekince panel dibe DÖNER ve akışı yeniden izlemeye başlar.
///
/// <para>Kural listenin frontier takibinde zaten vardı ("listeye bir süre hiç dokunulmazsa takip geri açılır");
/// konsol ve event stream'de yoktu — kullanıcı bir kez yukarı kaydırınca panel sonsuza dek orada kalıyordu.
/// Süre üç panelde de TEK sabittir (<see cref="BottomAnchorDecision.IdleResumeMs"/>), aksi hâlde farklı
/// zamanlarda canlanırlardı.</para>
///
/// <para>Zamanlayıcı enjekte edilir (<c>scheduleOnce</c>) — gerçek bekleme yok, pencere senkron tetiklenir.</para>
/// </summary>
public class BottomAnchorIdleResumeTests
{
    private sealed class Fake
    {
        public double Offset;
        public double Extent = 1000;
        public double Viewport = 200;
        public double? SmoothTarget;
        public Action? PendingSchedule;
        public TimeSpan? PendingDelay;
        public bool AutoResumeAllowed = true;

        public BottomAnchorBehavior New() => new(
            getOffset: () => Offset,
            getExtent: () => Extent,
            getViewport: () => Viewport,
            scrollInstant: v => Offset = v,
            scrollSmooth: target => { SmoothTarget = target; return true; },
            scheduleOnce: (delay, cb) => { PendingDelay = delay; PendingSchedule = cb; },
            autoResumeAllowed: () => AutoResumeAllowed);

        /// <summary>Kullanıcı yukarı kaydırdı (dipten uzaklaştı) ve host olayı iletti.</summary>
        public BottomAnchorBehavior ScrolledAway()
        {
            var behavior = New();
            Offset = 100; // dipten 700px uzakta
            behavior.OnScrollChanged(0);
            Assert.False(behavior.IsStuck); // ön-koşul
            return behavior;
        }
    }

    [Fact]
    public void Letting_go_after_a_scroll_brings_the_panel_back_to_the_bottom()
    {
        var f = new Fake();
        var behavior = f.ScrolledAway();

        Assert.Equal(TimeSpan.FromMilliseconds(BottomAnchorDecision.IdleResumeMs), f.PendingDelay);
        f.PendingSchedule!(); // bekleme doldu, kullanıcı hiç dokunmadı

        Assert.Equal(f.Extent - f.Viewport, f.SmoothTarget); // dibe dönüldü
    }

    /// <summary>Host izin vermiyorsa (konsolun proje-log modu) dönüş HİÇ kurulmaz.</summary>
    [Fact]
    public void A_panel_that_forbids_it_never_returns_on_its_own()
    {
        var f = new Fake { AutoResumeAllowed = false };
        var behavior = f.New();
        f.Offset = 100;

        behavior.OnScrollChanged(0);

        Assert.Null(f.PendingSchedule);
        Assert.Null(f.SmoothTarget);
    }

    /// <summary>
    /// Sayaç her scroll'da BAŞTAN kurulur: kullanıcı kaydırmayı sürdürürken eski bekleme dolsa bile iş
    /// yapmaz. Aksi hâlde okumaya devam eden kullanıcı dibe yollanırdı.
    /// </summary>
    [Fact]
    public void A_later_scroll_cancels_the_earlier_wait()
    {
        var f = new Fake();
        var behavior = f.ScrolledAway();
        var stale = f.PendingSchedule!;

        behavior.OnScrollChanged(0); // kullanıcı kaydırmayı sürdürdü → yeni bekleme
        stale();                     // eski bekleme şimdi doldu

        Assert.Null(f.SmoothTarget); // eskimiş kuşak iş yapmadı
    }

    /// <summary>Dipteyken bekleme kurulmaz — dönülecek bir yer yok.</summary>
    [Fact]
    public void Sitting_at_the_bottom_arms_nothing()
    {
        var f = new Fake();
        var behavior = f.New();

        behavior.OnScrollChanged(0); // dipte (Offset 0 ama extent-viewport... aşağıda dibe alınır)
        f.Offset = f.Extent - f.Viewport;
        behavior.OnScrollChanged(0);

        Assert.True(behavior.IsStuck);
        Assert.Null(f.SmoothTarget);
    }
}
