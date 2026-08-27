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
    public static VisualStatus For(GraphStatus status, bool fresh, bool marked) => status switch
    {
        GraphStatus.Queued => VisualStatus.Queued,
        GraphStatus.Building => VisualStatus.Building,
        GraphStatus.Succeeded => VisualStatus.Succeeded,
        GraphStatus.Failed => VisualStatus.Failed,
        GraphStatus.Skipped => VisualStatus.Skipped,
        _ => marked ? VisualStatus.Marked : fresh ? VisualStatus.Fresh : VisualStatus.Discovered,
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
        _ => "Brush.StatusSkippedBorder", // fresh · discovered · skipped AYNI gri (fresh'te KESİKLİ çizilir)
    };

    /// <summary>Graf node'unun çerçevesi (prototip <c>GTONE[].bd</c>).</summary>
    public static string NodeBorderBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.Amber",
        VisualStatus.Succeeded => "Brush.StatusSuccess",
        VisualStatus.Failed => "Brush.StatusFail",
        VisualStatus.Skipped => "Brush.StatusSkippedBorder",
        _ => "Brush.BorderStrong", // fresh · discovered
    };

    /// <summary>Graf node'unun zemini (prototip <c>GTONE[].bg</c>).</summary>
    public static string NodeBackgroundBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.AmberSoft",
        VisualStatus.Succeeded => "Brush.StatusSuccessSoft",
        VisualStatus.Failed => "Brush.StatusFailSoft",
        VisualStatus.Skipped => "Brush.StatusSkippedSoft",
        _ => "Brush.SurfaceRaised", // fresh · discovered
    };

    /// <summary>Graf node'unun İÇİNDEKİ küp (prototip <c>GCORE</c>) — border ile AYNI durumdan beslenir; ayrı
    /// bir plan/cycle çekirdeği YOKTUR.</summary>
    public static string NodeCoreBrushKey(VisualStatus state) => state switch
    {
        VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building => "Brush.AmberText",
        VisualStatus.Succeeded => "Brush.StatusSuccessText",
        VisualStatus.Failed => "Brush.StatusFailText",
        VisualStatus.Skipped => "Brush.StatusSkippedText",
        _ => "Brush.TextFaint", // fresh · discovered
    };

    /// <summary>Kesikli çizilen TEK durum başlangıç modudur (§2.3 "Renk kuralı"): şerit, nokta ve node
    /// çerçevesi orada kesiklidir. <c>discovered</c> DÜZ gridir — o, bir işlemin başladığını ama bu projenin
    /// kapsamda olmadığını söyler.</summary>
    public static bool IsDashed(VisualStatus state) => state == VisualStatus.Fresh;

    /// <summary>[§2.4-3] Adın vurgusu: bu işlemde İŞİ OLAN satır <c>text-primary</c>, geri kalanı
    /// <c>text-secondary</c> (prototip <c>emph</c>, BuildApp.jsx:636).</summary>
    public static bool NameIsEmphasised(VisualStatus state) => state
        is VisualStatus.Marked or VisualStatus.Queued or VisualStatus.Building
        or VisualStatus.Succeeded or VisualStatus.Failed;
}
