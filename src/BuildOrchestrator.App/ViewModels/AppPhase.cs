namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design-v1 §3.1] Uygulamanın faz makinesi: <c>empty → boot → syncing → idle → running → done | stopped</c>.
/// <para>
/// <b>Sahiplik notu:</b> bu enum'un KANONİK tanımı task C2'nindir (dosya yolu ve üye kümesi oradan gelir);
/// A5/T69 Sync event'lerini uçtan uca bağlarken <c>Syncing → Idle</c> geçişine ihtiyaç duyduğu için burada
/// ÖNCEDEN oluşturulmuştur. C2 bu dosyayı hazır bulur — üye kümesi C2'nin tanımıyla BİREBİR aynıdır,
/// genişletme (RunCounters/ProjectFilter vb.) C2'ye aittir.
/// </para>
/// </summary>
public enum AppPhase
{
    /// <summary>Repo seçilmemiş — liste/graf/konsol davet metinlerini gösterir.</summary>
    Empty,

    /// <summary>Repo var ama Sync yapılmamış: tüm will-dot'lar hollow (bilinmiyor).</summary>
    Boot,

    /// <summary>Sync koşuyor (ref-only fetch + tarama + plan + will-build).</summary>
    Syncing,

    /// <summary>Sync bitti, derleme başlamadı: proje durumları bilinir (dirty/clean).</summary>
    Idle,

    Running,
    Done,
    Stopped,
}
