namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design-v1 §3.1] Uygulamanın faz makinesi:
/// <c>empty → boot → syncing → idle → starting → running → stopping → done | stopped</c>.
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

    /// <summary>Run istendi ama motor henüz <c>runStarted</c> yazmadı: taze bir segmentte bu pencerede
    /// planlama koşar (worktree hazırlığı → tarama → graf → topo → incremental) ve 177 projelik bir
    /// workspace'te saniyeler sürer. Tıklamanın kaydedildiğini gösteren TEK yüzey budur — pencere eskiden
    /// fazsızdı: konsol <c>BeginRunAsync</c> tarafından temizleniyor, şerit önceki metinde donuyordu.
    /// <para>Adı "Planning" DEĞİL: pencerenin işi tıklamanın kaydedildiğini göstermektir, planlamanın
    /// kendisini anlatmak değil. İlerlemenin ayrıntısı konsola akan <c>planProgress</c> satırlarındadır;
    /// faz yalnız "başlıyor" der.</para></summary>
    Starting,

    Running,

    /// <summary>Stop istendi ama run HENÜZ bitmedi: yeni proje dispatch edilmez, uçuştaki <c>MSBuild.exe</c>
    /// child'ları (post-build copy dahil) kendi tamamlanmalarını yapar. Graceful stop'un gözlenebilir
    /// penceresi — tıklamanın kaydedildiğini gösteren tek yüzey budur. <see cref="Running"/>'den ayrıdır
    /// (şerit "Building" demez, Stop butonu pasifleşir) ve <see cref="Stopped"/>'dan da ayrıdır (henüz
    /// durmadı, Continue erişilebilir değil).</summary>
    Stopping,

    Done,
    Stopped,
}
