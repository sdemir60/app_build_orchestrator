using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [C2] <see cref="RunCounters.From"/>: satır durumlarından sayaç özeti. <c>DepAffected</c> yalnız
/// <b>succeeded</b> + dep-issue taşıyan satırları sayar (build-data.js:524-528) — chip/filtre "dep"inden
/// (herhangi statü) kasıtlı olarak FARKLIDIR.
/// </summary>
public class RunCountersTests
{
    private static ProjectRowViewModel Row(string name, ProjectRowState state, bool dep = false, bool stuck = false,
                                           bool cycleWaiting = false)
    {
        var r = new ProjectRowViewModel($@"C:\p\{name}.csproj", name, state);
        if (dep) r.DepIssues = ["X"];
        if (stuck) r.CycleUnconverged = true;
        if (cycleWaiting) { r.InCycle = true; r.CycleWaiting = true; }
        return r;
    }

    [Fact]
    public void Dep_counter_counts_every_row_that_carries_a_dep_issue_whatever_its_status()
    {
        var rows = new[]
        {
            Row("A", ProjectRowState.Succeeded, dep: true),  // sayılır
            // [DEĞİŞEN KURAL — design v1.5.1] Eskiden yalnız succeeded satırlar sayılıyordu ve chip listeyle
            // AYRIŞIYORDU: kendi derlemesi de patlamış bir satırın dep uyarısı listede üçgen olarak duruyor,
            // `dep` filtresi onu getiriyor ama chip 0 kalıyordu. Artık statüden bağımsız sayılır.
            Row("B", ProjectRowState.Failed, dep: true),     // sayılır
            Row("C", ProjectRowState.Succeeded, dep: false), // dep yok → sayılmaz
        };

        var c = RunCounters.From(rows);

        Assert.Equal(2, c.DepAffected);
        Assert.Equal(2, c.Succeeded);
        Assert.Equal(1, c.Failed);
        Assert.Equal(3, c.Total);
    }

    [Fact]
    public void Every_row_state_is_tallied_into_its_own_bucket()
    {
        var rows = new[]
        {
            Row("A", ProjectRowState.Started),
            Row("B", ProjectRowState.Pending),
            Row("C", ProjectRowState.Pending),
            Row("D", ProjectRowState.Succeeded),
            Row("E", ProjectRowState.Failed),
            Row("F", ProjectRowState.Skipped),
        };

        var c = RunCounters.From(rows);

        Assert.Equal(6, c.Total);
        Assert.Equal(1, c.Building);   // Started
        Assert.Equal(2, c.Queued);     // Pending
        Assert.Equal(1, c.Succeeded);
        Assert.Equal(1, c.Failed);
        Assert.Equal(1, c.Skipped);
        Assert.Equal(0, c.DepAffected);
        Assert.Equal(0, c.StuckCycles);
    }

    // [cycle rounds/Task 8] StuckCycles: yakınsamayan bir SCC'nin üyeleri — koşu turlarını harcadı ama grup
    // güncel hâle GELMEDİ. Sayaç ayrımı satır durumundan TÜRETİR (ProjectRowViewModel.CycleUnconverged), yığmaz.
    //
    // [DEĞİŞEN KURAL] Eski iddia: "yalnız SKIPPED + bayraklı satırlar sayılır; Failed+bayraklı satır savunmacı
    // olarak SAYILMAZ". O kapı, bayrağın tek kaynağının motorun pre-skip'i olduğu düzene aitti (grup hiç
    // denenmeden atlanırdı, üyeler Skipped'tı). Pre-skip kalktı — açık Resolve basışı artık her zaman taze bir
    // deneme yapar — ve bayrak koşunun KENDİ yakınsamama kararından geliyor: o üyeler Failed ya da Succeeded
    // olarak biter. Statü kapısı korunsaydı sayaç tam da anlamlı olduğu durumda sessizce sıfır okurdu.
    [Fact]
    public void Stuck_counter_counts_unconverged_rows_whatever_their_status()
    {
        var rows = new[]
        {
            Row("A", ProjectRowState.Failed, stuck: true),      // yakınsamayan grubun patlayan üyesi
            Row("B", ProjectRowState.Succeeded, stuck: true),   // aynı grubun yeşil üyesi — çıktısı yine bayat
            Row("C", ProjectRowState.Skipped, stuck: false),    // sıradan "güncel" skip → sayılmaz
        };

        var c = RunCounters.From(rows);

        Assert.Equal(2, c.StuckCycles);
        Assert.Equal(1, c.Skipped);
        Assert.Equal(3, c.Total);
    }

    // [cycle rounds/I2] Bir SCC'nin üyeleri motor tarafında SIRALI derlenir ve ara tur sonuçları YAYILMADIĞI
    // için grup bitene kadar HEPSİ Started'ta kalır — ama o anda gerçekten derlenen tek bir üye vardır.
    // Started'ı olduğu gibi saymak "32 building" gibi düpedüz yanlış bir sayı üretiyordu (şerit: "finishing 32
    // in flight"). Bekleyen üye Started'ı KORUR (görsel durumu değişmez) ama sayaçta Queued'a düşer: henüz
    // bitmemiştir, yalnız ŞU AN derlenmiyordur. Bölme değil taşımadır — toplam korunur.
    [Fact]
    public void A_cycle_member_waiting_for_its_turn_counts_as_queued_not_building()
    {
        var rows = new[]
        {
            Row("A", ProjectRowState.Started, cycleWaiting: true),  // grup içinde, sırasını bekliyor
            Row("B", ProjectRowState.Started, cycleWaiting: true),
            Row("C", ProjectRowState.Started),                      // GERÇEKTEN derlenen üye
            Row("D", ProjectRowState.Pending),
        };

        var c = RunCounters.From(rows);

        Assert.Equal(1, c.Building);
        Assert.Equal(3, c.Queued);   // A + B + D
        Assert.Equal(4, c.Total);    // hiçbir satır kaybolmaz
    }
}
