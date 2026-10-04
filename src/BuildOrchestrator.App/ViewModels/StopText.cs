namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [perf Faz B · B3 takip] Stop'un üç aşaması — Stop düğmesinin, tepsi menüsündeki Stop maddesinin ve satırdaki Stop
/// ikonunun ORTAK durumu. Tek yazıcısı <see cref="RunViewModel.StopStage"/>'dir (fazdan ve hard bayrağından türer); "Stop zaten
/// istendi mi" sorusunun tek cevabı odur: komutun ne göndereceğini (<c>StopAsync</c>), Esc zincirinin girdisini, çıkış
/// isteğinin Stop'a basıp basmayacağını ve üç yüzün etiketini aynı değer sürer.
/// </summary>
public enum StopStage
{
    /// <summary>Durdurma istenmedi (ya da koşu bitti): basış graceful stop gönderir.</summary>
    Stop,

    /// <summary>Graceful stop gitti, uçuştakiler bitiyor: basış hard stop gönderir ("Stop now"); kapı hâlâ açıktır.</summary>
    StopNow,

    /// <summary>Hard stop gitti, uçuştakiler sonlandırılıyor: başka basış gerekmez; kapı kapalıdır.</summary>
    Terminating,
}

/// <summary>
/// [perf Faz B · B3 takip] Stop aşamalarının metinlerinin TEK KAYNAĞI (<see cref="InteractionText"/> deseni: yüzler de testler
/// de AYNI yerden okur). Aşama "Stop" iken adlar/tooltip'ler eskiden olduğu gibi <see cref="AccessibilityNames"/>'in
/// sabitleridir — burada yeniden yazılmaz; sonraki aşamalarda etiketin kendisi (ya da onu açıklayan cümle) okunur.
/// </summary>
public static class StopText
{
    /// <summary>Görünür etiket: Stop düğmesi ve tepsi maddesi. Stopping'deki basış yıkıcıdır (uçuştakiler öldürülür) — "Stop"
    /// demek bunu gizlerdi.</summary>
    public static string Label(StopStage stage) => stage switch
    {
        StopStage.Stop => "Stop",
        StopStage.StopNow => "Stop now",
        StopStage.Terminating => "Terminating…",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>Action bar Stop düğmesinin UIA adı: aşama "Stop" iken eski sabit ad, sonrasında etiketin kendisi — ekran
    /// okuyucu da yıkıcı basışı ("Stop now") duyar.</summary>
    public static string ActionBarName(StopStage stage) =>
        stage == StopStage.Stop ? AccessibilityNames.StopButton : Label(stage);

    /// <summary>Satırdaki Stop ikonunun UIA adı (ikonun görünür etiketi yoktur): aşama "Stop" iken eski sabit ad, sonrasında
    /// etiketin kendisi.</summary>
    public static string RowName(StopStage stage) =>
        stage == StopStage.Stop ? AccessibilityNames.StopThisBuild : Label(stage);

    /// <summary>Satırdaki Stop ikonunun tooltip'i: etiket + ne yaptığı (aşama "Stop" iken eski sabit metin).</summary>
    public static string RowTooltip(StopStage stage) => stage switch
    {
        StopStage.Stop => AccessibilityNames.StopBuildTooltip,
        StopStage.StopNow => Label(stage) + " — terminate the in-flight compiles",
        StopStage.Terminating => "Terminating the in-flight compiles…",
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };
}
