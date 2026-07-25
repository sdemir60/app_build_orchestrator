namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [T20-b/P3] Post-build copy fazının CPU cap altında AÇ KALMAMASI için gereken iki bilgiyi taşıyan ince seam.
///
/// <para><b>Neden geriye dönük bir tetikleyici:</b> copy, <c>MSBuild.exe</c> child'ının İÇİNDE olur —
/// orchestrator hiçbir şey kopyalamaz [§4] ve elinde "copy başlıyor" sinyali YOKTUR. Elde olan tek sinyal
/// geriye dönüktür (<see cref="MsBuild.CopyContention"/>: MSB3021/3026/3027). Bu yüzden taban, ayrı bir "copy
/// fazı" olarak değil, CONTENTION PENCERESİNE bağlanır: o pencere tam olarak copy'nin sıkıştığı andır ve
/// gevşetme kalıcı bir davranış değişikliği yaratmaz.</para>
///
/// <para><b>Tek üretim implementasyonu Supervisor'ın koordinatörüdür</b> (<c>RunCoordinator</c>): cap'in TEK
/// yazıcısı odur ve tüm yazımları kendi merkezî kilidi altında serileştirir. İkinci bir yazıcı, canlı
/// <c>setPerfMode</c> ile pencere kapanışını yarıştırır ve cap'i yanlış değere düşürürdü. Bu arayüz, Core'daki
/// retry decorator'ının Supervisor'a bağımlı OLMADAN o yazıcıyı tetikleyebilmesi için vardır.</para>
/// </summary>
public interface ICopyPhaseCpuFloor
{
    /// <summary>
    /// Copy-contention penceresi AÇAR: yürürlükteki cap <see cref="PerfProfile.CopyPhaseFloorPercent"/>
    /// tabanının altındaysa oraya yükseltilir. Dönen handle pencereyi KAPATIR (cap run profiline döner) ve
    /// çağıran onu HER yoldan (başarı, retry'ların tükenmesi, iptal/exception) dispose etmelidir — aksi halde
    /// tek bir contention, Light bir run'ı kalıcı olarak tabana çıkarır.
    /// <para>Yükseltilecek bir şey yoksa (cap hiç yok ya da zaten tabanın üstünde) <c>null</c> döner ve
    /// job'a HİÇBİR yazım yapılmaz.</para>
    /// <para>Eşzamanlı pencereler REF-COUNT'ludur: paralel build'de birden çok worker aynı anda contention
    /// görebilir; cap ilk girişte yükselir ve ancak SON çıkışta geri konur.</para>
    /// </summary>
    IDisposable? Enter();

    /// <summary>
    /// Şu an gerçekten bir CPU cap yürürlükte mi — cap-farkındalı backoff'un girdisi. Açık bir pencere
    /// sırasında da <c>true</c>'dur (taban da bir cap'tir); graceful drain'den sonra <c>false</c>'a düşer.
    /// </summary>
    bool IsCapActive { get; }
}
