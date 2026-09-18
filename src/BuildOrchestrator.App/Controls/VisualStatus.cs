namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.20.0 §2.3 · §2.4 · §5] <b>Görsel durum = tek statü kanalı: çıktının durumu + koşu
/// bindirmesi.</b> Satırın sol şeridi, adın solundaki nokta, statü glyph'i ve graf node'unun border'ı AYNI
/// durumdan beslenir; ayrı bir "plan" (will-build) ya da "cycle" renk kanalı YOKTUR. Taban katman projenin
/// çıktı durumudur (<see cref="StandingStatus"/>); koşu (kuyruk, derleme, sonuç) ve işaretleme dalgası onun
/// üstüne biner.
///
/// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3]</b> v1.11.0 ilkesi "renk yalnız son işlemin hikâyesini
/// anlatır" idi: Sync hiçbir şeyi renklendirmez (<c>Fresh</c>), bir işlem başlayınca kapsam dışı her satır
/// nötr griye (<c>Discovered</c>) düşerdi. Ölçülen bedel (kullanıcı): Sync sonrası neyin güncel olduğu
/// renkten okunmuyordu, toplam görünmüyordu. Yeni ilke: renk çıktının KÜMÜLATİF durumudur — sonuç bir sonraki
/// koşuya kadar durumda yazılı kalır. <c>Fresh</c> → <see cref="Unknown"/> (yalnız Sync yokken),
/// <c>Discovered</c> kalktı (yerini çıktı durumu aldı), <c>Cycle</c>/<c>CycleSkipped</c> kalktı (döngü küpü
/// artık durumdan bağımsız bir parametredir, bkz. <see cref="VisualStatuses.NodeCoreBrushKey"/>).</para>
/// </summary>
public enum VisualStatus
{
    /// <summary>Bilinmiyor — başlangıç modu; karar yok (hiç Sync yapılmadı). Kesikli node border, satırda
    /// 4 yaylı halka nokta.</summary>
    Unknown,
    /// <summary>Güncel — yeşil.</summary>
    Current,
    /// <summary>Derlenecek — nötr gri (bugünkü düz gri; başlangıç modu DEĞİL).</summary>
    Stale,
    /// <summary>Kırmızı — kanıtlı bozuk çıktı ile koşunun <c>failed</c> sonucu AYNI üyedir (aynı fırçalar).</summary>
    Failed,
    /// <summary>Bu işlemin KAPSAMI — açılış koreografisinin dalgasında amber'a yanan küme (§9-4).</summary>
    Marked,
    Queued,
    Building,
    /// <summary>Koşu içinde "az önce derlendi" — <see cref="Current"/> ile aynı fırçalar, ayrı tutulur.</summary>
    Succeeded,
    /// <summary>[design v1.20.0 §1.4 · §5] <b>YALNIZ run-story yüzeyleri</b> için: "bu koşu onu derlemedi"
    /// (event stream'in atlama satırı, konsol başlığının koşu sonucu). Durum yüzeyleri (satır, node, sayaç)
    /// bunu HİÇ almaz — <see cref="VisualStatuses.For"/> atlanan projeyi kendi çıktı durumuna düşürür.</summary>
    Skipped,
}

/// <summary>
/// [design v1.11.0 §9-2 · v1.20.0 §2.3] Görsel durumun TEK eşleme yeri: hangi durum hangi token'ı boyar. Satır
/// (<c>ProjectRow</c>) ve graf (<c>GraphView</c>) AYNI tablodan okur — iki yüzey kendi eşlemesini yazsaydı
/// sessizce ayrışırlardı (kopya YASAK, CLAUDE.md).
/// </summary>
public static class VisualStatuses
{
    /// <summary>[design v1.20.0 §2.3] DURUM yüzeyleri (satır, node) için: koşu statüsü + çıktı durumu +
    /// işaretlilik → görsel durum.
    /// <para>Sıra ÖNEMLİ: motor bu proje hakkında bir şey söylediyse (queued/building/sonuç) o kazanır.
    /// <b>Atlanmak bir renk değildir</b>: <c>Skipped</c> çıktı durumuna düşer. Motor bir şey söylemediyse
    /// (discovered) önce işaretlilik, sonra çıktı durumu okunur. Sonuç <see cref="VisualStatus.Skipped"/>
    /// ASLA değildir.</para>
    /// <para><b>İstisna — <c>Failed</c> (R-M4b · design v1.20.0 §5 "bozuk (kanıtlı)"):</b> durum yüzeyinde
    /// kırmızı YALNIZ çıktı durumundan gelir. Koşu kanıtlı hatayı çıktı durumuna zaten yazar
    /// (<c>LastFailed</c> → <see cref="StandingStatus.Failed"/>); kanıt olmayan hata (timeout · Stop · invoke
    /// hatası · yakınsamayan SCC üyesi) bayat bırakır ve satır o griyi gösterir. Karar hiç yoksa
    /// (<see cref="StandingStatus.Unknown"/>) koşunun sonucu tek bilgidir ve kırmızı kalır. Run-story yüzeyleri
    /// (<see cref="OfRun"/>) bundan etkilenmez: orada <c>Failed</c> her zaman Failed'dır.</para></summary>
    public static VisualStatus For(GraphStatus status, StandingStatus standing, bool marked) => status switch
    {
        GraphStatus.Skipped => Of(standing),
        GraphStatus.Failed => standing is StandingStatus.Unknown ? VisualStatus.Failed : Of(standing),
        GraphStatus.Discovered or GraphStatus.Cycle => marked ? VisualStatus.Marked : Of(standing),
        _ => OfRun(status),
    };

    /// <summary>[design v1.20.0 §1.4] RUN-STORY yüzeyleri (konsol başlığının koşu sonucu) için koşu statüsü →
    /// görsel durum — <see cref="GraphStatus"/>'tan <see cref="VisualStatus"/>'a TEK eşleme yeri (kopya
    /// YASAK; <see cref="For"/> da koşu dallarını buradan okur). Burada <c>Skipped</c> kendisidir (—);
    /// motorun hakkında konuşmadığı proje <see cref="VisualStatus.Unknown"/>'dır.</summary>
    public static VisualStatus OfRun(GraphStatus status) => status switch
    {
        GraphStatus.Queued => VisualStatus.Queued,
        GraphStatus.Building => VisualStatus.Building,
        GraphStatus.Succeeded => VisualStatus.Succeeded,
        GraphStatus.Failed => VisualStatus.Failed,
        GraphStatus.Skipped => VisualStatus.Skipped,
        _ => VisualStatus.Unknown,
    };

    private static VisualStatus Of(StandingStatus standing) => standing switch
    {
        StandingStatus.Current => VisualStatus.Current,
        StandingStatus.Stale => VisualStatus.Stale,
        StandingStatus.Failed => VisualStatus.Failed,
        _ => VisualStatus.Unknown,
    };

    /// <summary>Satırın sol şeridi VE adın solundaki nokta (ikisi AYNI rengi taşır — §2.4-2).
    /// <para><b>[DEĞİŞEN KURAL]</b> <c>Queued</c> eskiden kendi grisini (<c>Brush.StatusQueued</c>) taşıyordu;
    /// v1.11.0'da kuyruk da işlemin kapsamıdır ve amber kalır — dalga ile yanan renk koşu başlayınca
    /// SÖNMEZ.</para></summary>
    public static string StripeBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.Amber",
        VisualStatus.Current or VisualStatus.Succeeded => "Brush.StatusSuccess",
        VisualStatus.Failed => "Brush.StatusFail",
        // unknown · stale AYNI gri. Döngü üyeliği LİSTEDE renk taşımaz: orada tek amber uyarı üçgeni
        // konuşur (§2.4), amber küp yalnız grafın dilidir.
        _ => "Brush.StatusSkippedBorder",
    };

    /// <summary>Graf node'unun çerçevesi (prototip <c>GTONE[].bd</c>).</summary>
    public static string NodeBorderBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.Amber",
        VisualStatus.Current or VisualStatus.Succeeded => "Brush.StatusSuccess",
        VisualStatus.Failed => "Brush.StatusFail",
        _ => "Brush.BorderStrong", // unknown · stale
    };

    /// <summary>Graf node'unun zemini (prototip <c>GTONE[].bg</c>).</summary>
    public static string NodeBackgroundBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.AmberSoft",
        VisualStatus.Current or VisualStatus.Succeeded => "Brush.StatusSuccessSoft",
        VisualStatus.Failed => "Brush.StatusFailSoft",
        _ => "Brush.SurfaceRaised", // unknown · stale
    };

    /// <summary>Graf node'unun İÇİNDEKİ küp (prototip <c>GCORE</c>) — border ile AYNI durumdan beslenir.
    /// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3]</b> Döngü üyesinde küp <b>her durumda</b> AMBER'dır:
    /// çerçeve kendi durumunu (bilinmiyor/derlenecek/derleniyor/sonuç) taşır, küp döngü üyeliğini sürekli
    /// işaretler. Eski kural (v1.12.0): amber küp yalnız "bu işlemde derlenmeyen" üyede yanardı
    /// (<c>Cycle</c>/<c>CycleSkipped</c> durumları), başlangıç modu ve üyeyi gerçekten derleyen işlem onu
    /// söndürürdü. Değişme gerekçesi: üyelik yapısaldır, bir koşunun sonucu değildir — Sync de, Resolve ile
    /// derleme de onu değiştirmez; küp satırdaki uyarı üçgeninin grafik vekilidir.</para></summary>
    public static string NodeCoreBrushKey(VisualStatus state, bool inCycle) => inCycle ? "Brush.AmberText" : state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.AmberText",
        VisualStatus.Current or VisualStatus.Succeeded => "Brush.StatusSuccessText",
        VisualStatus.Failed => "Brush.StatusFailText",
        _ => "Brush.TextFaint", // unknown · stale
    };

    /// <summary>Bu durum <b>başlangıç modu</b> mu — karar yok, hiç Sync yapılmadı. Üç yüzey onu farklı çizer:
    /// graf node'unun çerçevesi KESİKLİDİR, satırın şeridi DÜZ VE TAM OPAKTIR (v1.13.2) ve statü noktası
    /// dolu daire yerine dört yaylı bir HALKA gösterir (<see cref="StartMode"/>).
    /// <para><b>[DEĞİŞEN KURAL — v1.12.0]</b> Yüklem eskiden <c>IsDashed</c> adındaydı ve üç yüzeyin de
    /// kesikli çizildiğini söylüyordu. Kesiklilik artık YALNIZ node'da kaldı (satırda tırtık yapıyordu), bu
    /// yüzden yüklem taşıdığı bilgiyle adlandırıldı: "başlangıç modu mu", "kesikli mi" değil.</para>
    /// <para><b>[DEĞİŞEN KURAL — design v1.20.0 §2.3 · §5]</b> Başlangıç modu eskiden Sync sonrası da
    /// sürüyordu (Sync hiçbir şeyi renklendirmezdi). Artık yalnız <see cref="VisualStatus.Unknown"/>'dır —
    /// Sync sonrası her satır kendi çıktı durumundadır; <c>stale</c> düz, tam opak gridir.</para></summary>
    public static bool IsStartMode(VisualStatus state) => state == VisualStatus.Unknown;

    /// <summary>[§2.4-3] Adın vurgusu: bu işlemde İŞİ OLAN satır <c>text-primary</c>, geri kalanı
    /// <c>text-secondary</c> (prototip <c>emph</c>, BuildApp.jsx:636).</summary>
    public static bool NameIsEmphasised(VisualStatus state) => state
        is VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building
        or VisualStatus.Succeeded or VisualStatus.Failed;
}
