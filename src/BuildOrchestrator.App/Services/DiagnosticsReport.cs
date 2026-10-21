using System.Globalization;
using System.Text;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Core.Formatting;

namespace BuildOrchestrator.App.Services;

/// <summary>[About] Tanı tablosunun bir satırı: etiket + gösterilecek değer.</summary>
public readonly record struct DiagnosticsLine(string Label, string Value);

/// <summary>
/// [About] Tanı raporunun ham girdisi — TOPLAMAYI çağıran yapar (<see cref="AppIdentity"/>,
/// <c>RuntimeInformation</c>, <c>MsBuildResolver</c>, <see cref="MemoryLine.TryRead()"/>, uygulamanın kendi yol static'leri), bu tip
/// yalnız taşır. Böylece rapor mantığı WPF'siz ve process'siz test edilir. <see cref="Memory"/> App sürecinin bellek okumasıdır
/// (bayt) — raporun kurulduğu anın görüntüsü, canlı değil; kopyalanan metindeki üç bellek satırının kaynağıdır (ekranda çizilmez);
/// okunamadıysa <c>null</c> (üç satır <see cref="DiagnosticsReport.Unknown"/> yazar, tanı ekranı bozulmaz). <see cref="EngineState"/> [idle-memory · dalga 3 · karar 39]
/// VM'in motor durumudur (<c>RunViewModel.EngineState</c>) — PID satırının uyku değeri ondan okunur.
/// </summary>
public sealed record DiagnosticsInput(
    string Product,
    string Version,
    string Copyright,
    string? EngineVersion,
    int? EnginePid,
    EngineState EngineState,
    string Runtime,
    string Os,
    MemoryReading? Memory,
    string MsBuild,
    string RepositoryRoot,
    string StateFile,
    string LogsRoot);

/// <summary>
/// [design v1.19.0 §2.10] Tanı modelinin gruplu hâli. About sekmesi <see cref="Identity"/>'yi, Environment sekmesi
/// <see cref="Runtime"/> ile <see cref="Paths"/>'i çizer; "Copy diagnostics" <see cref="DiagnosticsReport.ToText"/>
/// ile AYNI nesneden metin üretir. Bir değer iki grupta geçmez.
///
/// <para><b>[kaynak şeridi · karar K7] Görünür/yalnız-kopya ayrımı BURADADIR:</b> <see cref="CopyOnly"/> hiçbir sekmede çizilmez, yalnız
/// <see cref="DiagnosticsReport.ToText"/>'e girer. View yalnız <see cref="Identity"/>, <see cref="Runtime"/> ve <see cref="Paths"/>'i
/// bağlar; hangi satırın çizildiğini ayrıca bir süzgeçle söylemez — bir satırı ekrandan almak ya da kopyaya katmak onu bir gruptan
/// ötekine taşımaktır.</para>
/// </summary>
public sealed record DiagnosticsSnapshot(
    string Product,
    DiagnosticsLine Version,
    DiagnosticsLine Engine,
    DiagnosticsLine Copyright,
    IReadOnlyList<DiagnosticsLine> Runtime,
    IReadOnlyList<DiagnosticsLine> CopyOnly,
    IReadOnlyList<DiagnosticsLine> Paths)
{
    /// <summary>About sekmesinin satırları: Version · Engine · Copyright.</summary>
    public IReadOnlyList<DiagnosticsLine> Identity => [Version, Engine, Copyright];
}

/// <summary>
/// [About] About'un kimlik satırları, Environment sekmesinin iki grubu ve "Copy diagnostics"in panoya yazdığı
/// metin — TEK modelden. Ayrı listelerden üretilselerdi biri güncellenip diğeri unutulurdu.
///
/// <para>SAF: hiçbir şey okumaz, hiçbir process başlatmaz. Etiket ve grup başlığı metinleri BURADA tanımlanır;
/// XAML onları tekrar yazmaz (satırlar <c>ItemsControl</c>'lerle çizilir).</para>
///
/// <para><b>[DEĞİŞEN KURAL — design v1.19.0 §2.10]</b> ESKİ MODEL tek düz listeydi (<c>App version</c>,
/// <c>Engine version</c>, <c>Engine PID</c> … <c>Worktree pool</c>) ve Environment sekmesi onun tamamını çizerdi.
/// Sürümler About sekmesine taşındı (Version · Engine · Copyright); Environment RUNTIME ve PATHS gruplarıdır.</para>
///
/// <para><b>[idle-memory · spec §6.6] App'in belleği tanı raporundadır:</b> <c>App private</c> · <c>GC committed</c> ·
/// <c>Managed heap</c> — motorun bellek satırının (<c>memory: private=… committed=… heap=…</c>) App'teki eşi. Okuma çağıranındır
/// (<see cref="DiagnosticsInput.Memory"/>; okunamadıysa <c>null</c> ve üç satır <see cref="Unknown"/> yazar), MB sayısı satırınkiyle
/// AYNI kuraldan (<see cref="MemoryLine.Megabytes"/>); burada yalnız birim eklenir.</para>
///
/// <para><b>[DEĞİŞEN KURAL — kaynak şeridi · karar K7]</b> ESKİ MODEL bu üç satırı RUNTIME grubuna koyardı ve Environment sekmesi onları
/// çizerdi. Title bar'a kaynak şeridi geldi ve belleği gösterir; satırlar ekrandan ÇIKTI, kopyalanan tanı metninde (hata bildirimi için)
/// KALDI: <see cref="DiagnosticsSnapshot.CopyOnly"/> grubudur ve <see cref="ToText"/> onları eskisi gibi Runtime satırlarından sonra,
/// Paths'ten önce yazar — kopyalanan metin DEĞİŞMEDİ. Etiketler yine yalnız <see cref="Compose"/>'dadır.</para>
/// </summary>
public static class DiagnosticsReport
{
    /// <summary>Motor henüz doğmadı.</summary>
    public const string NotStarted = "not started";
    /// <summary>Değer bu koşulda YOK (ör. motor doğmadığı için PID). İşaret Core'un tek tanımıdır (<see cref="MissingValue.Mark"/>).</summary>
    public const string Unknown = MissingValue.Mark;
    /// <summary>Henüz bir repo kökü seçilmemiş.</summary>
    public const string NoRepository = "no repository";
    /// <summary>MSBuild çözümü sürüyor (vswhere child process'i).</summary>
    public const string Resolving = "resolving…";
    /// <summary>[idle-memory · dalga 3 · karar 39] Motor boşta uyuyor (VM'in motor durumu <see cref="EngineState.Asleep"/>): süreci yok, PID'i
    /// yoktur — sonraki iş onu yeniden başlatır.</summary>
    public const string Asleep = "asleep";

    /// <summary>[idle-memory · dalga 3 · B1 fix 1 · m-3] Motor PID satırının etiketi — değeri durum kuralına bağlı tek satır (<see cref="Asleep"/>,
    /// <see cref="Unknown"/>); testler satırı bu adla bulur.</summary>
    public const string EnginePidLabel = "Engine PID";

    /// <summary>Environment sekmesinin grup başlıkları — caps olarak çizilir.</summary>
    public const string RuntimeTitle = "Runtime";
    public const string PathsTitle = "Paths";

    /// <summary>Etiket ile değer arasındaki boşluk (pano metni hizalaması).</summary>
    private const string Gutter = "  ";

    public static DiagnosticsSnapshot Compose(DiagnosticsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new DiagnosticsSnapshot(
            input.Product,
            Version: new("Version", input.Version),
            Engine: new("Engine", Or(input.EngineVersion, NotStarted)),
            Copyright: new("Copyright", input.Copyright),
            Runtime:
            [
                new(EnginePidLabel, EnginePidValue(input)),
                new(".NET runtime", input.Runtime),
                new("OS", input.Os),
            ],
            CopyOnly:
            [
                new("App private", MemoryValue(input.Memory?.PrivateBytes)),
                new("GC committed", MemoryValue(input.Memory?.CommittedBytes)),
                new("Managed heap", MemoryValue(input.Memory?.HeapBytes)),
            ],
            Paths:
            [
                new("MSBuild", input.MsBuild),
                new("Repository root", Or(input.RepositoryRoot, NoRepository)),
                new("State file", input.StateFile),
                new("Logs", input.LogsRoot),
            ]);
    }

    /// <summary>
    /// Panoya gidecek düz metin (bir destek talebine yapıştırılır): ilk satır <c>{ürün} {sürüm}</c> — çıktının
    /// nereden geldiğini söyler — ardından <c>Engine</c> + Runtime + yalnız-kopya (bellek) + Paths satırları, değerler TEK kolonda hizalı.
    /// Telif panoya GİTMEZ: bir destek talebinde bilgi taşımaz. Satır ayracı
    /// <see cref="StringBuilder.AppendLine()"/>'ın platform ayracıdır.
    /// </summary>
    public static string ToText(DiagnosticsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<DiagnosticsLine> lines = [snapshot.Engine, .. snapshot.Runtime, .. snapshot.CopyOnly, .. snapshot.Paths];
        int width = lines.Max(l => l.Label.Length);

        var text = new StringBuilder()
            .Append(snapshot.Product).Append(' ').AppendLine(snapshot.Version.Value);
        foreach (var line in lines)
            text.Append(line.Label.PadRight(width)).Append(Gutter).AppendLine(line.Value);
        return text.ToString();
    }

    private static string Or(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    /// <summary>PID satırının değeri: motor boşta uyuyorsa (<see cref="EngineState.Asleep"/>) <see cref="Asleep"/> — süreci yok, ama bilinmiyor
    /// da değil [idle-memory · dalga 3 · karar 39]; başka her durumda motorun PID'i, yoksa <see cref="Unknown"/>.</summary>
    private static string EnginePidValue(DiagnosticsInput input) =>
        input.EngineState == EngineState.Asleep ? Asleep
        : input.EnginePid is { } pid ? pid.ToString(CultureInfo.InvariantCulture)
        : Unknown;

    /// <summary>Bellek satırının değeri: sayı motorun bellek satırıyla AYNI kuraldan (<see cref="MemoryLine.Megabytes"/>), birim
    /// burada — <c>"309 MB"</c>. Okunamayan bellek (<c>null</c>) satırı kaybetmez, <see cref="Unknown"/> yazar.</summary>
    private static string MemoryValue(long? bytes) => bytes is { } value ? $"{MemoryLine.Megabytes(value)} MB" : Unknown;
}
