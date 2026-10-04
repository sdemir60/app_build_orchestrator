using System.IO;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.State;
using Xunit;

namespace BuildOrchestrator.Tests.State;

/// <summary>
/// [PERF Faz C · son toparlama B1 · madde 8] Atomik yazımın rename retry gecikmesinin ÜRETİM varsayılanı, iki büyük
/// defterde (<c>evaluation-cache.json</c>, <c>source-hash-cache.json</c>) ve uçuş defterinde de pinli —
/// <c>BuildStateStoreTests.The_production_rename_retry_delay_is_the_default_and_it_really_waits</c>'in deseni: her test
/// dikişi (<c>RenameRetryDelay</c>) kuruyor, bu yüzden varsayılanı no-op'a çeviren bir mutasyon aksi halde TÜM süiti
/// yeşil bırakırdı (üretimde ise retry bütçesi mikrosaniyelerde tükenip atomik yazımı gereksiz yere düşürürdü). Burada
/// iki şey pinlenir: (1) üretimde dikiş KURULMAZ, (2) dikiş yokken koşan şey ÜRETİM varsayılanıdır. Varsayılanın kendisinin
/// gerçekten beklediği (alt sınır iddiası) <c>BuildStateStoreTests</c>'te pinli — TEK sahibi orası.
/// </summary>
public sealed class LedgerRetryDefaultTests
{
    // Hiçbir şey diske yazılmaz: yol yalnız kurucuya verilir (olmayan dosya boş defterdir).
    private static string NewDir() => Path.Combine(Path.GetTempPath(), "bo-retry-default-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void EvaluationCache_runs_the_production_retry_default_when_no_seam_is_set()
    {
        var cache = new EvaluationCache(Path.Combine(NewDir(), "evaluation-cache.json"));

        Assert.Null(cache.RenameRetryDelay);                                                                 // (1)
        Assert.Equal((Action<int>)BuildStateStore.DefaultRenameRetryDelay, cache.EffectiveRenameRetryDelay); // (2)
    }

    [Fact]
    public void SourceHashCache_runs_the_production_retry_default_when_no_seam_is_set()
    {
        var cache = new SourceHashCache(Path.Combine(NewDir(), "source-hash-cache.json"));

        Assert.Null(cache.RenameRetryDelay);                                                                 // (1)
        Assert.Equal((Action<int>)BuildStateStore.DefaultRenameRetryDelay, cache.EffectiveRenameRetryDelay); // (2)
    }

    [Fact]
    public void InFlightLedger_runs_the_production_retry_default_when_no_seam_is_set()
    {
        var ledger = new InFlightLedger(NewDir());

        Assert.Null(ledger.RenameRetryDelay);                                                                 // (1)
        Assert.Equal((Action<int>)BuildStateStore.DefaultRenameRetryDelay, ledger.EffectiveRenameRetryDelay); // (2)
    }

    [Fact]
    public void A_seam_that_is_set_wins_over_the_production_default()
    {
        Action<int> seam = _ => { };

        var cache = new EvaluationCache(Path.Combine(NewDir(), "evaluation-cache.json")) { RenameRetryDelay = seam };

        Assert.Equal(seam, cache.EffectiveRenameRetryDelay);
    }
}
