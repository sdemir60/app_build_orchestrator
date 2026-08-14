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

        /// <summary>
        /// Kullanıcı yukarı kaydırdı: host HAM GİRDİYİ bildirir (tekerlek) ve ardından scroll olayı gelir.
        /// Üretimdeki sıra budur — "kullanıcı kaydırdı" sinyali hesaplanmaz, girdiden gelir.
        /// </summary>
        public BottomAnchorBehavior ScrolledAway()
        {
            var behavior = New();
            behavior.NotifyUserScroll();
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

        behavior.NotifyUserScroll();
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

        behavior.NotifyUserScroll(); // kullanıcı kaydırmayı sürdürdü → yeni bekleme
        stale();                     // eski bekleme şimdi doldu

        Assert.Null(f.SmoothTarget); // eskimiş kuşak iş yapmadı
    }

    /// <summary>
    /// AYIRT EDİCİ — kullanıcı kaydırdıktan sonra AKAN İÇERİK onu dibe çekmez.
    ///
    /// <para>Sahada görülen kusur buydu: derleme sürerken küçük bir kaydırma (48px eşiğinin İÇİNDE kalan)
    /// yapıldığında bir sonraki satır gelir gelmez içerik-büyümesi yakalaması kullanıcıyı dibe fırlatıyordu.
    /// Eşik tek başına yetmiyor; kullanıcı kaydırdıysa bekleme dolana kadar direksiyon ondadır.</para>
    /// </summary>
    [Fact]
    public void Content_arriving_after_a_small_scroll_does_not_yank_the_reader_down()
    {
        var f = new Fake();
        var behavior = f.New();
        f.Offset = f.Extent - f.Viewport - 10; // dipten 10px — EŞİĞİN İÇİNDE, hâlâ "stuck"
        behavior.NotifyUserScroll();           // kullanıcı kaydırdı (ham girdi)
        behavior.OnScrollChanged(0);
        Assert.True(behavior.IsStuck);         // ön-koşul: eşik hâlâ yapışık diyor
        double afterScroll = f.Offset;

        f.Extent += 40;              // yeni satır geldi
        behavior.OnScrollChanged(40);

        Assert.Equal(afterScroll, f.Offset); // okuyucu yerinde kaldı
    }

    /// <summary>Bekleme dolunca takip geri alınır — dipteyken de akış yeniden izlenmeye başlar.</summary>
    [Fact]
    public void When_the_wait_expires_following_resumes_even_from_inside_the_threshold()
    {
        var f = new Fake();
        var behavior = f.New();
        f.Offset = f.Extent - f.Viewport - 10;
        behavior.NotifyUserScroll();
        behavior.OnScrollChanged(0);

        f.PendingSchedule!(); // kullanıcı elini çekti

        f.Extent += 40;
        behavior.OnScrollChanged(40);
        Assert.Equal(f.Extent, f.Offset); // içerik büyümesi yakalaması yeniden devrede
    }

    /// <summary>Dipte otururken ve HİÇ kaydırılmamışken bekleme kurulmaz — dönülecek bir yer yok.</summary>
    [Fact]
    public void Content_growth_alone_arms_nothing()
    {
        var f = new Fake();
        var behavior = f.New();

        f.Extent += 40;
        behavior.OnScrollChanged(40); // yalnız içerik büyüdü, kullanıcı dokunmadı

        Assert.Null(f.PendingSchedule);
    }
}
