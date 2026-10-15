using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 2 · spec §6.5 · son inceleme O1/O7] Kompakt konsolun VM tarafındaki TEK durumu: süzgeç
/// (<see cref="CompactConsoleFilter"/>), etkin kip ve projelerin adları. VM proje çıktısını, proje sonuçlarını ve işlemin sınırlarını
/// buraya verir, anlatıya yazılacak satırları geri alır; yazımın kendisi (store, pompa, spool uyarısı) VM'in tek yolundadır.
///
/// <para><b>Etkin kip</b> (<see cref="InEffect"/>) konsolun BUNDAN SONRA yazacağı kiptir — tek kural: uçuşta işlem yokken ayarı izler,
/// uçuştaki bir işlem yakaladığı kipi tutar. İşlemin başında ayardan yakalanır (<see cref="BeginOperation"/>) ve o uçuştayken
/// değişmez — koşunun anlatısı tek kiple yazılır, "ilk 3" ortada bozulmaz; uçuşta işlem kalmayınca ayarın kendisidir
/// (<see cref="Recapture"/>). Başlıktaki <c>compact</c> etiketi bunu gösterir. Kip yalnız YENİ satırların nasıl yazılacağını seçer
/// (<see cref="Line"/>).</para>
///
/// <para><b>İşlemin kendi bekleyen çıktısı kipe bağlı değildir</b> (<see cref="Summary"/>, <see cref="Drain"/>): süzgeç gizlenen
/// sayaçları yalnız işlem kompakt yazarken tutar — tam kipte <see cref="Line"/> süzgece hiç uğramaz, <see cref="BeginOperation"/> da
/// sıfırlar —, yani sayaç varsa onu kompakt yazan işlem bırakmıştır ve özeti yazılır. Bu yüzden işlemin bitişinde kipin ayara dönüşü
/// ile bekleyen özetlerin yazımı arasındaki sıra önemsizdir (motor kaybında kip, bitişin başında dönebilir).</para>
///
/// <para><b>Adlar:</b> başlık ve özet projeyi koşunun adıyla anar — işlem başında satır listesinden, sonra koşunun önizlemesinden;
/// ikisinde de yoksa projenin yoldan türeyen görünen adı (<see cref="ProjectDisplayName.FromPath"/>). Ad yalnız gerçekten bir satır
/// yazılacakken sorulur — başlıkta, özette, boşaltmada aynı çözücüyle (bir kez kurulur): satır başına ne arama ne ayırma.</para>
///
/// <para><b>Kilit yok:</b> kendi başına thread-safe değildir. VM her çağrıyı kendi <c>_gate</c>'i altında yapar (satırlar IPC
/// thread'inden, sonuçlar ve işlem sınırları UI thread'inden gelir); buradan dışarı hiçbir çağrı, olay ya da bildirim çıkmaz — kip
/// değişimini çağıran kilit dışında bildirir.</para>
/// </summary>
internal sealed class CompactNarrative
{
    private readonly CompactConsoleFilter _filter = new();

    /// <summary>Kimlik → ad (harf duyarsız — Windows yolu, VM'in bütün sözlükleri gibi).</summary>
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary><see cref="NameOf"/>'un süzgece verilen çözücüsü — BİR kez kurulur: çağrı başına kapanış ayrılmaz.</summary>
    private readonly Func<string, string> _nameOf;

    /// <param name="compact">Açılıştaki etkin kip — ayarın o anki değeri.</param>
    public CompactNarrative(bool compact)
    {
        InEffect = compact;
        _nameOf = NameOf;
    }

    /// <summary>Etkin kip: konsol proje çıktısını bundan sonra kompakt mı yazar.</summary>
    public bool InEffect { get; private set; }

    /// <summary>Yeni işlem: kip ayardan yakalanır, süzgecin sayaçları ve başlıkları sıfırlanır, adlar satır listesinden alınır — koşunun
    /// satırları önizlemesini de geçebilir (ikisi de pompadan gelir). Kip değiştiyse <c>true</c>.</summary>
    public bool BeginOperation(bool compact, IEnumerable<(string Id, string Name)> rows)
    {
        _filter.Reset();
        _names.Clear();
        Name(rows);
        return Recapture(compact);
    }

    /// <summary>Projelerin koşudaki adları (önizleme) — satır listesinde henüz olmayan yeni bir proje de.</summary>
    public void Name(IEnumerable<(string Id, string Name)> projects)
    {
        foreach (var (id, name) in projects) _names[id] = name;
    }

    /// <summary>Uçuşta işlem yok: etkin kip ayarın kendisidir (çağıran: ayarın boştaki değişimi, uçuştaki işlerin bittiği an, işlem
    /// bitişi). Değiştiyse <c>true</c>.</summary>
    public bool Recapture(bool compact)
    {
        if (InEffect == compact) return false;
        InEffect = compact;
        return true;
    }

    /// <summary>Bir proje satırının anlatıya yazılacak hâli. Kompakt kipte: projenin işlemdeki İLK satırıyla başlığı (<c>building
    /// &lt;ad&gt;</c> — satır gizlense de; aynı kilit bölümünde, satırdan önce yazılır: kendi satırlarından sonra düşemez) ve satırın
    /// kendisi ya da <c>null</c> (gizle). Tam kipte başlık yoktur, satır olduğu gibidir.</summary>
    public (string? Header, string? Line) Line(string projectId, string text) =>
        InEffect ? (_filter.ProjectHeader(projectId, _nameOf), _filter.LogLine(projectId, text)) : (null, text);

    /// <summary>Projenin sonucu geldi (başarı, hata ya da atlama — "her sonuç"): gizlenen uyarı varsa TEK özet satırı. Kipe bağlı değildir
    /// (sınıf özeti): sayaç ancak işlem kompakt yazarken dolar.</summary>
    public string? Summary(string projectId) => _filter.ProjectDone(projectId, _nameOf);

    /// <summary>Koşu bitti: sonucu hiç gelmeyen projelerin (motor kaybı) bekleyen özetleri, projelerin ilk uyarı sırasıyla. Kipe bağlı
    /// değildir: kip bitişin başında ayara dönmüş olsa da koşunun kendi özetleri yazılır.
    /// <para>[DEĞİŞEN KURAL — F1 sınırı] Eskiden boşaltma (ve <see cref="Summary"/>) etkin kip kompakt değilse hiçbir şey vermiyordu: o
    /// zaman kip yalnız işlem bitişinde, boşaltmadan SONRA dönüyordu. Kip artık uçuştaki işler bittiği an da döner — motor kaybında bu an
    /// boşaltmadan öncedir —; kapı kalsaydı koşu ortasında tam kipe geçen bir Save bekleyen özetleri düşürürdü.</para></summary>
    public IReadOnlyList<string> Drain() => _filter.DrainPending(_nameOf);

    private string NameOf(string projectId) =>
        _names.TryGetValue(projectId, out var name) ? name : ProjectDisplayName.FromPath(projectId);
}
