using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T41/DD9] <see cref="MotionCoordinator"/> hero-mutex: aynı anda EN FAZLA 1 hero. Aynı anahtar (graf+liste
/// frontier AYNI hero) re-entrant kabul edilir; farklı bir anahtar aktif hero sürerken REDDEDİLİR. Saf/WPF'siz
/// (plain [Fact]).
/// </summary>
public class MotionCoordinatorTests
{
    [Fact]
    public void The_first_hero_request_is_always_accepted()
    {
        var c = new MotionCoordinator();

        Assert.True(c.TryBeginHero("reveal"));
        Assert.True(c.IsHeroActive);
        Assert.Equal("reveal", c.CurrentHeroKey);
    }

    [Fact]
    public void A_second_hero_with_a_different_key_is_rejected_while_one_is_running()
    {
        var c = new MotionCoordinator();
        Assert.True(c.TryBeginHero("reveal"));

        // DD9: aynı anda tek hero — farklı bir hero reddedilir (çağıran ani sonuca gider).
        Assert.False(c.TryBeginHero("frontier"));
        Assert.Equal("reveal", c.CurrentHeroKey); // aktif hero DEĞİŞMEZ
    }

    [Fact]
    public void The_same_key_is_re_entrant_because_graph_and_list_frontier_are_the_same_hero()
    {
        var c = new MotionCoordinator();

        // Graf reveal + liste reveal AYNI anahtarla co-tetiklenir → ikisi de kabul (tek hero, birlikte oynar).
        Assert.True(c.TryBeginHero("sync-reveal"));
        Assert.True(c.TryBeginHero("sync-reveal"));
        Assert.Equal("sync-reveal", c.CurrentHeroKey);
    }

    [Fact]
    public void The_hero_releases_only_after_every_re_entrant_begin_has_ended()
    {
        var c = new MotionCoordinator();
        c.TryBeginHero("sync-reveal");
        c.TryBeginHero("sync-reveal"); // depth = 2

        c.EndHero("sync-reveal");
        Assert.True(c.IsHeroActive); // hâlâ bir giriş açık — hero sürüyor

        c.EndHero("sync-reveal");
        Assert.False(c.IsHeroActive); // son giriş de çıktı → serbest
        Assert.Null(c.CurrentHeroKey);
    }

    [Fact]
    public void A_new_different_hero_can_begin_once_the_previous_one_is_fully_ended()
    {
        var c = new MotionCoordinator();
        c.TryBeginHero("reveal");
        Assert.False(c.TryBeginHero("frontier")); // reddedildi

        c.EndHero("reveal");

        Assert.True(c.TryBeginHero("frontier")); // artık serbest — farklı hero girebilir
        Assert.Equal("frontier", c.CurrentHeroKey);
    }

    [Fact]
    public void EndHero_with_a_non_matching_key_is_a_no_op_and_never_frees_the_active_hero()
    {
        var c = new MotionCoordinator();
        c.TryBeginHero("reveal");

        c.EndHero("frontier"); // yanlış anahtar — savunmacı no-op
        c.EndHero("nope");

        Assert.True(c.IsHeroActive);
        Assert.Equal("reveal", c.CurrentHeroKey);
    }

    [Fact]
    public void EndHero_when_no_hero_is_active_is_a_harmless_no_op()
    {
        var c = new MotionCoordinator();

        c.EndHero("reveal"); // hiç hero yok — patlamamalı

        Assert.False(c.IsHeroActive);
    }

    [Fact]
    public void Hero_returns_a_scope_that_ends_the_hero_on_dispose()
    {
        var c = new MotionCoordinator();

        using (var scope = c.Hero("reveal"))
        {
            Assert.NotNull(scope);
            Assert.True(c.IsHeroActive);
        }

        Assert.False(c.IsHeroActive); // Dispose → EndHero
    }

    [Fact]
    public void Hero_returns_null_when_a_different_hero_is_already_running()
    {
        var c = new MotionCoordinator();
        using var first = c.Hero("reveal");

        var rejected = c.Hero("frontier");

        Assert.Null(rejected); // çağıran dekoratif yolu atlar
        Assert.Equal("reveal", c.CurrentHeroKey);
    }

    [Fact]
    public void Disposing_a_hero_scope_twice_only_ends_the_hero_once()
    {
        var c = new MotionCoordinator();
        c.TryBeginHero("sync-reveal"); // depth = 1 (manuel giriş)
        var scope = c.Hero("sync-reveal"); // depth = 2 (aynı hero, re-entrant)
        Assert.NotNull(scope);

        scope!.Dispose();
        scope.Dispose(); // idempotent — ikinci Dispose ref-count'u BOZMAMALI

        Assert.True(c.IsHeroActive); // manuel giriş hâlâ açık (depth 1)
        c.EndHero("sync-reveal");
        Assert.False(c.IsHeroActive);
    }
}
