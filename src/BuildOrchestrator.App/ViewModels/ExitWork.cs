namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [Exit] Exit'in durduracağı (ya da bekleyeceği) iş — <see cref="RunViewModel.ExitWork"/>'ün cevabı, onay kartının ve kapanış
/// ekranının konusu. <see cref="None"/> uçuşta iş yok demektir (<c>WorkspaceIdle</c>): Exit kartsız, hemen kapatır.
/// </summary>
public enum ExitWork
{
    None,
    Build,
    Rebuild,
    /// <summary>Bakım kutusunun <c>Resolve cycles</c> koşusu (<c>RunMode.Cycles</c>).</summary>
    Resolve,
    /// <summary>Build menüsünün ya da satır menüsünün <c>-t:Clean</c> koşusu.</summary>
    Clean,
    /// <summary>Bakım kutusunun Clean'i — workspace sıfırlaması.</summary>
    DeepClean,
    Optimize,
    Sync,
    /// <summary>Branch chip'inden checkout — git çalışma ağacına yazar, beklenir.</summary>
    BranchSwitch,
    /// <summary><c>N behind</c> chip'inin pull'u — git çalışma ağacına yazar, beklenir.</summary>
    Pull,
}

/// <summary>
/// [Exit · inceleme Ö3] Çıkışın bir işe ne yaptığının TEK kuralı: kesmek ya da beklemek. VM'in bekleyişi
/// (<c>RunViewModel.EvaluateExit</c>) ve kartın cümlesi, düğmesi ve kapanış ekranının adımı (<see cref="ExitText"/>) bunu okur — kural metin
/// sınıfında ya da VM'de ikinci kez yazılmaz.
/// </summary>
public static class ExitWorkRules
{
    /// <summary>Çıkış bu işi kesmez, BEKLER: git çalışma ağacına ve <c>.git/index</c>'e yazıyor — yarıda kesilen bir checkout ağacı yarı
    /// değiştirilmiş ve <c>index.lock</c>'lu bırakırdı (kullanıcı kararı 2026-10-19). Diğer her iş kesilir.</summary>
    public static bool Waits(this ExitWork work) => work is ExitWork.BranchSwitch or ExitWork.Pull;
}
