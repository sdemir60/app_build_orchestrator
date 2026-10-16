using BuildOrchestrator.App.Services;
using BuildOrchestrator.Contracts.Ipc;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 3 · B1 fix 3] Motor kapısının durumu (<see cref="EngineGate"/>) — WPF'siz birim testleri: kayıt, bilet ve boşalmanın
/// değişmezi (kayıt yokken boşalma tamamlanmış; kayıt varken güncel ve tamamlanmamış; boşalmadan sonraki ilk kayıt onu yeniden kurar; son kaydın
/// silinmesi hiçbir devamı satır içi koşturmaz). Kapının kuralı ve sırası (uyandırma, bekleme, kurtaran uyanmanın tazelemesi) <c>EngineWakeTests</c>'te,
/// kapının çağrı sınırı <c>EngineGateGuardTests</c>'tedir.
/// </summary>
public sealed class EngineGateTests
{
    /// <summary>Asenkron devamın gelmesi için üst sınır — aşılırsa test asılmaz, düşer.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    /// <summary>Boş kapı boşalmıştır: hiç kayıt yokken sayı 0 ve <see cref="EngineGate.Drained"/> tamamlanmış — kurtaran uyanmanın tazelemesi, kapıda
    /// bekleyen yoksa beklemez.
    /// <para>Mutasyon (boş kapının boşalması tamamlanmamış) kırmızı.</para></summary>
    [Fact]
    public void An_empty_gate_is_drained()
    {
        var gate = new EngineGate();

        Assert.Equal(0, gate.Count);
        Assert.True(gate.Drained.IsCompletedSuccessfully);
    }

    /// <summary>Boşalma SON kayıtla gelir: iki kayıttan biri silinince kapı hâlâ bekler, ikincisi silinince boşalır.
    /// <para>Mutasyon (her silişte boşalma) kırmızı.</para></summary>
    [Fact]
    public void The_gate_drains_when_its_last_entry_leaves()
    {
        var gate = new EngineGate();
        int log = gate.Enter(typeof(GetProjectLogCommand));
        int sync = gate.Enter(typeof(SyncWorkspaceCommand));

        gate.Leave(log);
        Assert.Equal(1, gate.Count);
        Assert.False(gate.Drained.IsCompleted);

        gate.Leave(sync);
        Assert.Equal(0, gate.Count);
        Assert.True(gate.Drained.IsCompletedSuccessfully);
    }

    /// <summary>Boşalmadan sonraki ilk kayıt boşalmayı YENİDEN kurar: yeni, tamamlanmamış bir görev — eski, tamamlanmış görev geri gelmez. Aksi
    /// hâlde ikinci kurtaran uyanmanın tazelemesi beklemeden koşar ve sessiz Sync uyandıran istekten önce giderdi (EngineWakeTests'in iki döngülü pini).
    /// <para>Mutasyon (<c>??=</c> — eski boşalma tutulur) kırmızı.</para></summary>
    [Fact]
    public void The_first_entry_after_a_drain_rearms_it()
    {
        var gate = new EngineGate();
        gate.Leave(gate.Enter(typeof(GetProjectLogCommand)));
        var drained = gate.Drained;
        Assert.True(drained.IsCompletedSuccessfully); // ön-koşul: kapı bir kez boşaldı

        gate.Enter(typeof(GetProjectLogCommand));

        Assert.NotSame(drained, gate.Drained);
        Assert.False(gate.Drained.IsCompleted);
    }

    /// <summary><see cref="EngineGate.Leave"/> tekrarlanabilir ve yalnız kendi kaydını siler: aynı bilet ikinci kez ya da bilinmeyen bir bilet hiçbir
    /// şey yapmaz — başka bir kaydı silmez, boşalmayı erken tamamlamaz, fırlatmaz (istek teslim edilirken ve geçişin dispose'unda iki kez çağrılır).
    /// <para>Mutasyon (bilinmeyen bileti aramadan silmek) kırmızı.</para></summary>
    [Fact]
    public void Leave_is_idempotent_and_leaves_only_its_own_entry()
    {
        var gate = new EngineGate();
        int log = gate.Enter(typeof(GetProjectLogCommand));
        int sync = gate.Enter(typeof(SyncWorkspaceCommand));

        gate.Leave(log);
        gate.Leave(log);
        gate.Leave(log + sync); // hiç verilmemiş bir bilet

        Assert.Equal(1, gate.Count);
        Assert.False(gate.Holds(typeof(GetProjectLogCommand)));
        Assert.True(gate.Holds(typeof(SyncWorkspaceCommand)));
        Assert.False(gate.Drained.IsCompleted);
    }

    /// <summary>Son kaydı silen <see cref="EngineGate.Leave"/> hiçbir devamı satır içi koşturmaz — eşzamanlı devam isteyen bir bekleyen
    /// (<see cref="TaskContinuationOptions.ExecuteSynchronously"/>) bile: boşalmayı bekleyen tazeleme, son teslimin gönderiminden ÖNCE koşamaz.
    /// Devamın <see cref="EngineGate.Leave"/>'in içinde koşup koşmadığı thread'e özgü bir bayrakla okunur — zamanlamaya bağlı değildir.
    /// <para>Mutasyon (<see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> yok) kırmızı.</para></summary>
    [Fact]
    public async Task Leave_runs_no_continuation_inline()
    {
        var gate = new EngineGate();
        int ticket = gate.Enter(typeof(GetProjectLogCommand));
        using var insideLeave = new ThreadLocal<bool>();
        var ranInsideLeave = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = gate.Drained.ContinueWith(_ => ranInsideLeave.TrySetResult(insideLeave.Value), TaskContinuationOptions.ExecuteSynchronously);

        insideLeave.Value = true;
        gate.Leave(ticket);
        insideLeave.Value = false;

        Assert.False(await ranInsideLeave.Task.WaitAsync(Limit));
    }
}
