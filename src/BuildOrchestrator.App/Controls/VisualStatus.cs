namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.11.0 §9-2 · §2.3 · §2.4] <b>Görsel durum = tek statü kanalı.</b> Satırın sol şeridi, adın
/// solundaki nokta, statü glyph'i, graf node'unun border'ı ve içindeki küp AYNI durumdan beslenir; ayrı bir
/// "plan" (will-build) ya da "cycle" kanalı YOKTUR.
///
/// <para><b>[DEĞİŞEN KURAL — v1.7.0'dan]</b> Önceki model ÜÇ ortogonal kanal taşıyordu: A "bu koşuda ne oldu"
/// (statü), B "sıradaki Build buna dokunacak mı" (amber/gri nokta + node çekirdeği), C "kodda döngü var mı"
/// (turuncu). Ölçülen bedel: aynı yüzeyde üç anlam yarışıyordu ve turuncu ile amber yan yana ayırt
/// edilemiyordu. v1.11.0 ilkesi: <b>renk yalnız son işlemin hikâyesini anlatır.</b> B kanalı satırın çift SHA
/// metnine (<c>a3f81c2 → b7e91d4</c>), C kanalı tek amber uyarı üçgenine indi.</para>
/// </summary>
public enum VisualStatus
{
    /// <summary>Başlangıç modu — Sync sonrası ve uygulama açılışı. Hiçbir şey renklendirilmez: hangi işlemin
    /// geleceği belli değildir, bu yüzden plan da gösterilmez. Kesikli şerit + kesikli nokta + kesikli node
    /// border.</summary>
    Fresh,
    /// <summary>Nötr gri — bir işlem başladı (<c>_neutralize</c>) ama bu proje o işlemin kapsamında değil.</summary>
    Discovered,
    /// <summary>[design v1.12.0 §2.3] <b>Bu işlemde derlenmeyen döngü üyesi:</b> node gri, içindeki küp AMBER.
    /// Satırdaki amber uyarı üçgeninin grafik vekilidir — işlemin nötr anında yanar, koşuda ve finalde durur,
    /// bir sonraki Sync'te (başlangıç modu) ya da üyeyi gerçekten derleyen bir işlemde düşer.
    /// <para>Liste tarafında karşılığı yoktur: satırın şeridi ve noktası <see cref="Discovered"/> ile AYNI
    /// nötr gridir (döngü orada zaten tek üçgenle anlatılır).</para></summary>
    Cycle,
    /// <summary>[design v1.12.0 §2.3] Atlanmış döngü üyesi: çerçeve/zemin <see cref="Skipped"/>'ın, küp yine
    /// AMBER.</summary>
    CycleSkipped,
    /// <summary>Bu işlemin KAPSAMI — açılış koreografisinin dalgasında amber'a yanan küme (§9-4).</summary>
    Marked,
    Queued,
    Building,
    Succeeded,
    Failed,
    Skipped,
}

/// <summary>
/// [design v1.11.0 §9-2] Görsel durumun TEK eşleme yeri: hangi durum hangi token'ı boyar. Satır (<c>ProjectRow</c>)
/// ve graf (<c>GraphView</c>) AYNI tablodan okur — iki yüzey kendi eşlemesini yazsaydı sessizce ayrışırlardı
/// (kopya YASAK, CLAUDE.md).
/// </summary>
public static class VisualStatuses
{
    /// <summary>Statü + başlangıç modu + işaretlilik → görsel durum (prototip <c>vstate()</c>, BuildApp.jsx:293).
    /// <para>Sıra ÖNEMLİ: motor bu proje hakkında bir şey söylediyse (queued/building/sonuç) o kazanır;
    /// söylemediyse (discovered) önce işaretlilik, sonra başlangıç modu okunur.</para></summary>
    /// <param name="inCycle">[design v1.12.0] Proje kalıcı bir bağımlılık döngüsünün üyesi mi. Bu bir STATÜ
    /// DEĞİLDİR (yapısal bir özelliktir) ve statüyü asla ezmez: yalnız motorun bu proje hakkında bir şey
    /// SÖYLEMEDİĞİ (ya da "atladım" dediği) durumda görünür — orada da renk değil, tek başına KÜP amber olur.</param>
    public static VisualStatus For(GraphStatus status, bool fresh, bool marked, bool inCycle = false) => status switch
    {
        GraphStatus.Queued => VisualStatus.Queued,
        GraphStatus.Building => VisualStatus.Building,
        GraphStatus.Succeeded => VisualStatus.Succeeded,
        GraphStatus.Failed => VisualStatus.Failed,
        // Atlanmış üye: "derlenmedi" bilgisi çerçevede, "çünkü döngüde" bilgisi küpte.
        GraphStatus.Skipped => inCycle ? VisualStatus.CycleSkipped : VisualStatus.Skipped,
        // Sıra ÖNEMLİ: dalga (marked) her şeyi ezer — kapsamdaki bir üye TAM amberdir, "gri node + amber küp"
        // değil. Başlangıç modu döngü üyeliğini de ezer: Sync hiçbir şeyi renklendirmez (§2.3).
        _ => marked ? VisualStatus.Marked
            : fresh ? VisualStatus.Fresh
            : inCycle ? VisualStatus.Cycle
            : VisualStatus.Discovered,
    };

    /// <summary>Satırın sol şeridi VE adın solundaki nokta (ikisi AYNI rengi taşır — §2.4-2).
    /// <para><b>[DEĞİŞEN KURAL]</b> <c>Queued</c> eskiden kendi grisini (<c>Brush.StatusQueued</c>) taşıyordu;
    /// v1.11.0'da kuyruk da işlemin kapsamıdır ve amber kalır — dalga ile yanan renk koşu başlayınca
    /// SÖNMEZ.</para></summary>
    public static string StripeBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.Amber",
        VisualStatus.Succeeded => "Brush.StatusSuccess",
        VisualStatus.Failed => "Brush.StatusFail",
        // fresh · discovered · skipped · cycle · cycskip AYNI gri. Döngü üyeliği LİSTEDE renk taşımaz:
        // orada tek amber uyarı üçgeni konuşur (§2.4), amber küp yalnız grafın dilidir.
        _ => "Brush.StatusSkippedBorder",
    };

    /// <summary>Graf node'unun çerçevesi (prototip <c>GTONE[].bd</c>).</summary>
    public static string NodeBorderBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.Amber",
        VisualStatus.Succeeded => "Brush.StatusSuccess",
        VisualStatus.Failed => "Brush.StatusFail",
        VisualStatus.Skipped or VisualStatus.CycleSkipped => "Brush.StatusSkippedBorder",
        _ => "Brush.BorderStrong", // fresh · discovered · cycle
    };

    /// <summary>Graf node'unun zemini (prototip <c>GTONE[].bg</c>).</summary>
    public static string NodeBackgroundBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.AmberSoft",
        VisualStatus.Succeeded => "Brush.StatusSuccessSoft",
        VisualStatus.Failed => "Brush.StatusFailSoft",
        VisualStatus.Skipped or VisualStatus.CycleSkipped => "Brush.StatusSkippedSoft",
        _ => "Brush.SurfaceRaised", // fresh · discovered · cycle
    };

    /// <summary>Graf node'unun İÇİNDEKİ küp (prototip <c>GCORE</c>) — border ile AYNI durumdan beslenir; ayrı
    /// bir plan/cycle KANALI yoktur.
    /// <para><b>[design v1.12.0] TEK istisna:</b> bu işlemde derlenmeyen döngü üyesinde küp AMBER, çerçeve
    /// grisini korur (<see cref="VisualStatus.Cycle"/> / <see cref="VisualStatus.CycleSkipped"/>). Bu, geri
    /// gelen bir renk kanalı değildir: kullanılan ton satırdaki uyarı üçgeninin kendi amberidir ve başka
    /// hiçbir durumda çerçeve ile küp ayrışmaz.</para></summary>
    public static string NodeCoreBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building
            or VisualStatus.Cycle or VisualStatus.CycleSkipped => "Brush.AmberText",
        VisualStatus.Succeeded => "Brush.StatusSuccessText",
        VisualStatus.Failed => "Brush.StatusFailText",
        VisualStatus.Skipped => "Brush.StatusSkippedText",
        _ => "Brush.TextFaint", // fresh · discovered
    };

    /// <summary>Bu durum <b>başlangıç modu</b> mu — Sync sonrası ve açılış hâli. Üç yüzey onu farklı çizer:
    /// graf node'unun çerçevesi KESİKLİDİR, satırın şeridi DÜZ VE TAM OPAKTIR (v1.13.2) ve statü noktası
    /// dolu daire yerine dört yaylı bir HALKA gösterir (<see cref="StartMode"/>).
    /// <para><b>[DEĞİŞEN KURAL — v1.12.0]</b> Yüklem eskiden <c>IsDashed</c> adındaydı ve üç yüzeyin de
    /// kesikli çizildiğini söylüyordu. Kesiklilik artık YALNIZ node'da kaldı (satırda tırtık yapıyordu), bu
    /// yüzden yüklem taşıdığı bilgiyle adlandırıldı: "başlangıç modu mu", "kesikli mi" değil.</para>
    /// <para><c>discovered</c> başlangıç modu DEĞİLDİR — o, bir işlemin başladığını ama bu projenin kapsamda
    /// olmadığını söyler ve düz, tam opak gridir.</para></summary>
    public static bool IsStartMode(VisualStatus state) => state == VisualStatus.Fresh;

    /// <summary>[§2.4-3] Adın vurgusu: bu işlemde İŞİ OLAN satır <c>text-primary</c>, geri kalanı
    /// <c>text-secondary</c> (prototip <c>emph</c>, BuildApp.jsx:636).</summary>
    public static bool NameIsEmphasised(VisualStatus state) => state
        is VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building
        or VisualStatus.Succeeded or VisualStatus.Failed;
}
