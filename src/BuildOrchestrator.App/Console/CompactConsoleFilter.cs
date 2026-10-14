using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 2 · spec §6.5 — K4] Kompakt konsolun SAF süzgeci: bir işlemde projelerin MSBuild çıktısından
/// (<c>projectLog</c>) koşu anlatısına ne gideceğine karar verir. Yalnız proje çıktısına uygulanır — uygulamanın kendi anlatı
/// satırları ve motor notları süzülmez. Proje logu sayfası ve disk her zaman tamdır: süzgeç yalnız anlatıyı küçültür.
///
/// <para><b>Kurallar</b> (spec §6.5 tablosu): projenin işlemdeki ilk satırından önce tek başlık (<see cref="ProjectHeader"/>:
/// <c>building &lt;ad&gt;</c>); hata satırı aynen; uyarı satırı projenin ilk <see cref="ShownWarningsPerProject"/> TEKİL uyarısıysa
/// aynen, sonrası gizlenir ve koduna göre sayılır; diğer her satır (bilgi, komut, boş) gizlenir; proje bitince (her sonuç) gizlenen
/// uyarı varsa tek özet satırı (<see cref="ProjectDone"/>) — sonucu hiç gelmeyen projeninki koşunun sonunda
/// (<see cref="DrainPending"/>). Satırın türü ve tanı kodu <see cref="ConsoleLineClassifier"/>'ın TEK ayrıştırmasındandır — ikinci
/// bir tanım yoktur.</para>
///
/// <para><b>Motor notları süzülmez</b> (spec §6.5 · son inceleme N5): motorun projenin loguna derleme çıktısından ÖNCE yazdığı kendi
/// <c>warning:</c> satırları (bağımlılık notları — bir bağımlılık bu koşuda düştü, döngüde ya da bekleyen değişiklikle derlenmedi,
/// zincirde hata var) her zaman aynen geçer: ne sayılır ne "ilk 3" kotasını harcar. Tanıma sınıflandırıcının öneki tanımıdır
/// (<see cref="ConsoleLineClassifier.IsOwnWarning"/>).</para>
///
/// <para><b>Tekil sayım.</b> MSBuild her tanıyı bitiş özetinde bir kez daha yazar: girintili ve çoğu zaman sonunda
/// <c>[…\Proje.csproj]</c> ekiyle (çok hedefli projede <c>[…\Proje.csproj::TargetFramework=net8.0]</c>). Ham satır sayımı "N more
/// warnings"ı gerçek sayının katlarına şişirirdi (son altı gerçek koşuda toplamda 3,4×). Bu yüzden bir uyarının kimliği kırpılmış ve
/// proje eki atılmış metnidir; görülmüş bir uyarının tekrarı ne yeniden gösterilir ne sayılır — "ilk 3" ilk üç TEKİL uyarıdır. Hata
/// hiç gizlenmez: tekrarı da geçer.</para>
///
/// <para><b>Bellek.</b> Bir uyarının kimliği metnin kendisi değil 64 bitlik parmak izidir (<see cref="Fingerprint"/>): binlerce
/// uyarılı bir projede metinleri tutmak log hacmiyle orantılı bellek olurdu (spec §5 ilke 1). Projenin sayacı özetiyle birlikte düşer;
/// özeti olmayan projede en çok <see cref="ShownWarningsPerProject"/> iz kalır, bekleyen özeti boşaltılan projede (<see cref="DrainPending"/>)
/// görülenlerin izi kalır — geç gelen tekrarlar için — ve yeni işlemde (<see cref="Reset"/>) hepsi gider.
/// [fix 1 · M4] Sıcak yol ayırma yapmaz: gizlenen satır ve görülmüş uyarının tekrarı 0 bayttır; kod string'i yalnız projenin sayacına
/// YENİ bir kod girerken üretilir.</para>
///
/// <para><b>Kilit yok:</b> süzgeç kendi başına thread-safe değildir; VM her çağrıyı kendi <c>_gate</c>'i altında yapar (satırlar IPC
/// thread'inden, proje sonuçları UI thread'inden gelir). Proje kimlikleri harf duyarsızdır (Windows yolu — VM'in bütün sözlükleri
/// gibi).</para>
/// </summary>
public sealed class CompactConsoleFilter
{
    /// <summary>Bir projenin anlatıda gösterilen en çok tekil uyarısı.</summary>
    public const int ShownWarningsPerProject = 3;

    /// <summary>Özet satırının adını verdiği en çok kod (çoktan aza, eşitlikte alfabetik); toplam sayı hepsini içerir.</summary>
    public const int SummaryCodes = 3;

    /// <summary>Kodu olmayan uyarının — MSBuild'in kodsuz biçimi (<c>…: warning : …</c>, kodu verilmemiş bir <c>&lt;Warning&gt;</c>
    /// görevi) — özet kodu.</summary>
    private const string OtherCode = "other";

    /// <summary>Projenin başlığının öneki (<c>building &lt;ad&gt;</c>).</summary>
    private const string HeaderPrefix = "building ";

    // [fix 1 · M6] Özet satırının sabit parçaları: biçim (Summary) ve tanıma (IsSummary — sınıflandırıcının rengi) AYNI sabitlerden
    // kurulur; ayraçlar da dahil — ikinci bir literal yoktur.
    private const string NameSeparator = ": ";
    private const string PartSeparator = " — ";
    private const string CodeSeparator = ", ";
    private const string MoreWarnings = "more warning";
    private const string SummaryTail = PartSeparator + "see the project log";

    // [son inceleme F3/O3] Tanımanın okuduğu sayılı ad: StreamText.Counted'ın "<n> <ad>[s]" çekimi — sayı ile ad arasındaki ayraç ve
    // çoğul eki Counted'ın KENDİ parçalarıdır (StreamText.CountSeparator, StreamText.PluralSuffix): ikinci bir tanım yok, çekim
    // değişirse tanıma onunla değişir.
    private const string CountedNoun = StreamText.CountSeparator + MoreWarnings;

    private readonly Dictionary<string, ProjectWarnings> _projects = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Bu işlemde başlığı yazılmış projeler — başlık işlem başına bir kezdir (döngü turları yinelemez); sayaçtan ayrı tutulur
    /// çünkü sayaç özetle birlikte düşer.</summary>
    private readonly HashSet<string> _headed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sayacı açılan projelerin sırası — bekleyen özetler (<see cref="DrainPending"/>) projelerin ilk uyarı sırasıyla yazılır.</summary>
    private int _nextOrder;

    /// <summary>Projenin başlığı (<c>building &lt;ad&gt;</c>) — bu işlemde ilk soruluşunda; sonrasında <c>null</c>. VM onu projenin ilk
    /// satırıyla AYNI kilit bölümünde, satırdan hemen önce yazar: başlık kendi projesinin satırlarından sonra düşemez.
    /// [fix 1 · M2b] Ad tembel sorulur (<paramref name="nameOf"/>): yalnız projenin İLK satırında — sonraki satırlar ad aramaz.</summary>
    public string? ProjectHeader(string projectId, Func<string, string> nameOf) =>
        _headed.Add(projectId) ? HeaderPrefix + nameOf(projectId) : null;

    /// <summary>Bir proje satırının anlatıdaki karşılığı: satırın kendisi ya da <c>null</c> (gizle). Motorun kendi notu
    /// (<see cref="ConsoleLineClassifier.IsOwnWarning"/>) her zaman geçer ve sayılmaz.</summary>
    public string? LogLine(string projectId, string text)
    {
        switch (ConsoleLineClassifier.Classify(text, out ReadOnlySpan<char> code))
        {
            case ConsoleLineType.Error: return text;
            case ConsoleLineType.Warn: return ConsoleLineClassifier.IsOwnWarning(text) ? text : Warning(projectId, text, code);
            default: return null;
        }
    }

    /// <summary>Proje bitti (başarı, hata ya da atlama): gizlenen uyarı varsa özet satırı, yoksa <c>null</c>. Özet üretilince projenin
    /// sayacı düşer — aynı sonucun tekrarı özeti ikilemez.
    /// <para>[DEĞİŞEN KURULUM — dalga 2 son inceleme O7] Ad artık bir çözücüyle verilir ve YALNIZ özet üretilirken sorulur
    /// (<see cref="ProjectHeader"/> ve <see cref="DrainPending"/> gibi); eskiden çağıran her proje sonucunda adı önceden çözüyordu — özet
    /// düşmese de.</para></summary>
    public string? ProjectDone(string projectId, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        if (!_projects.TryGetValue(projectId, out var warnings) || warnings.Hidden == 0) return null;
        _projects.Remove(projectId);
        return Summary(nameOf(projectId), warnings);
    }

    /// <summary>
    /// [fix 1 · M2] Koşu bitti: sonucu HİÇ gelmeyen projelerin (motor kaybı — motor uçuştaki projeler için sonuç yazmadan gitti) bekleyen
    /// özetleri, projelerin ilk uyarı sırasıyla. Normal bitişte her proje sonucunu almıştır: liste boştur. Adlar yalnız özet üretilirken
    /// sorulur.
    /// <para>[DEĞİŞEN KURAL — dalga 2 son inceleme · defter 8] Boşaltılan projenin yalnız gizlenen sayacı sıfırlanır (özetin sayısı ve
    /// kodları); görülen uyarıların izi ve gösterilenlerin sayısı KALIR. Eski hâl projenin bütün durumunu atıyordu: okuma thread'i ölü
    /// motorun stdout'unda kalan satırları motor kaybından SONRA da teslim edebilir — o geç tekrarlar yeni uyarı sayılıp "ilk 3" olarak
    /// yeniden gösteriliyor, geç gelen bir sonuç da boşaltılmış uyarıları ikinci bir özette yeniden sayıyordu.</para>
    /// </summary>
    public IReadOnlyList<string> DrainPending(Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        var pending = _projects.Where(p => p.Value.Hidden > 0).OrderBy(p => p.Value.Order).ToList();
        string[] summaries = [.. pending.Select(p => Summary(nameOf(p.Key), p.Value))];
        foreach (var (_, warnings) in pending) warnings.ForgetHidden();
        return summaries;
    }

    /// <summary>Yeni işlem: bütün sayaçlar ve başlıklar sıfırlanır.</summary>
    public void Reset()
    {
        _projects.Clear();
        _headed.Clear();
        _nextOrder = 0;
    }

    /// <summary>[tek tanım] Metin bu süzgecin özet satırı mı — <see cref="ConsoleLineClassifier"/> özet satırını bununla tanır ve uyarı
    /// rengini verir; biçimin ikinci bir literali yoktur: şekil <see cref="Summary"/>'nin AYNI sabitlerinden okunur —
    /// <c>&lt;ad&gt;: &lt;n&gt; more warning(s) — &lt;kodlar&gt; — see the project log</c>. Önce ucuz kuyruk kontrolü: sınıflandırıcı her satır
    /// için sorar.
    /// <para>[DEĞİŞEN KURAL — fix 1 · M4/M6] Şekil eskiden aynı sabitlerden kurulan bir kaynak-üretimli regex'ti; ölçüldü (2026-10-15):
    /// <c>IsMatch(span)</c> her özet satırında 96 bayt ayırıyordu, sınıflandırmanın geri kalanı 0. Elle ayrıştırma ayırma yapmaz ve şekli
    /// aynı sabitlerden okur (ayraçlar dahil).</para></summary>
    public static bool IsSummary(ReadOnlySpan<char> content)
    {
        if (!content.EndsWith(SummaryTail, StringComparison.Ordinal)) return false;
        ReadOnlySpan<char> body = content[..^SummaryTail.Length]; // "<ad>: <n> more warning(s) — <kodlar>"
        // Adın kendisi ": " içerebilir (regex'in açgözlü ".+"'sının karşılığı): her aday denenir; ad en az bir karakterdir.
        for (int at = body.IndexOf(NameSeparator, StringComparison.Ordinal); at >= 0; )
        {
            if (at > 0 && IsCountedPart(body[(at + NameSeparator.Length)..])) return true;
            int next = body[(at + 1)..].IndexOf(NameSeparator, StringComparison.Ordinal);
            at = next < 0 ? -1 : at + 1 + next;
        }
        return false;
    }

    /// <summary>Özetin adından sonraki bölümü mü: <c>&lt;n&gt; more warning(s)</c> (<see cref="StreamText.Counted"/>'ın çekimi), ayraç ve
    /// en az bir karakterlik kod listesi.</summary>
    private static bool IsCountedPart(ReadOnlySpan<char> rest)
    {
        int digits = 0;
        while (digits < rest.Length && char.IsAsciiDigit(rest[digits])) digits++;
        if (digits == 0 || !rest[digits..].StartsWith(CountedNoun, StringComparison.Ordinal)) return false;
        rest = rest[(digits + CountedNoun.Length)..];
        if (rest.StartsWith(StreamText.PluralSuffix, StringComparison.Ordinal)) rest = rest[StreamText.PluralSuffix.Length..];
        return rest.Length > PartSeparator.Length && rest.StartsWith(PartSeparator, StringComparison.Ordinal);
    }

    private string? Warning(string projectId, string text, ReadOnlySpan<char> code)
    {
        if (!_projects.TryGetValue(projectId, out var warnings))
            _projects[projectId] = warnings = new ProjectWarnings { Order = _nextOrder++ };
        if (!warnings.Seen.Add(Fingerprint(text))) return null; // tekrar (bitiş özeti): ne gösterilir ne sayılır
        if (warnings.Shown < ShownWarningsPerProject)
        {
            warnings.Shown++;
            return text;
        }
        // [fix 1 · M4] Kod satırın bir dilimidir: sayaçta varsa yerinde artar (ayırma yok), string'e yalnız YENİ bir kod girerken çevrilir.
        var byCode = warnings.HiddenByCode.GetAlternateLookup<ReadOnlySpan<char>>();
        CollectionsMarshal.GetValueRefOrAddDefault(byCode, code.IsEmpty ? OtherCode : code, out _)++;
        warnings.Hidden++;
        return null;
    }

    /// <summary><c>&lt;ad&gt;: &lt;n&gt; more warnings — CS1591 ×200, MSB3277 ×90 — see the project log</c> — tekil/çoğul ortak çekim
    /// kuralıyla (<see cref="StreamText.Counted"/>); sayılar InvariantCulture.</summary>
    private static string Summary(string name, ProjectWarnings warnings)
    {
        var codes = warnings.HiddenByCode
            .OrderByDescending(c => c.Value)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .Take(SummaryCodes)
            .Select(c => string.Format(CultureInfo.InvariantCulture, "{0} ×{1}", c.Key, c.Value));
        return string.Concat(name, NameSeparator, StreamText.Counted(warnings.Hidden, MoreWarnings), PartSeparator,
            string.Join(CodeSeparator, codes), SummaryTail);
    }

    /// <summary>Bir uyarının tekil kimliği: kırpılmış metin, sonundaki proje eki atılmış (<see cref="WithoutProjectSuffix"/>);
    /// SHA-256'nın ilk 8 baytı — ayırma yok, çakışma olasılığı ihmal edilir.</summary>
    private static ulong Fingerprint(string text)
    {
        ReadOnlySpan<char> line = WithoutProjectSuffix(text.AsSpan().Trim());
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(MemoryMarshal.AsBytes(line), digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

    /// <summary>[fix 1 · M3] MSBuild'in satıra eklediği proje eki — <c> [&lt;yol&gt;proj]</c> ya da çok hedefli projede
    /// <c> [&lt;yol&gt;proj::&lt;özellikler&gt;]</c> (<c>::TargetFramework=net8.0</c>) — atılmış satır; ek yoksa satırın kendisi. Bitiş
    /// özetinin kopyası satır içi kopyada eki taşımayabilir; ikisi tek uyarıdır.</summary>
    private static ReadOnlySpan<char> WithoutProjectSuffix(ReadOnlySpan<char> line)
    {
        if (line.IsEmpty || line[^1] != ']') return line;
        int open = line.LastIndexOf(" [", StringComparison.Ordinal);
        if (open < 0) return line;
        ReadOnlySpan<char> inside = line[(open + 2)..^1];
        int properties = inside.IndexOf("::", StringComparison.Ordinal);
        ReadOnlySpan<char> project = properties >= 0 ? inside[..properties] : inside;
        return project.EndsWith("proj", StringComparison.OrdinalIgnoreCase) ? line[..open].TrimEnd() : line;
    }

    /// <summary>Bir projenin bu işlemdeki uyarı sayacı.</summary>
    private sealed class ProjectWarnings
    {
        /// <summary>Sayacın açılış sırası (<see cref="DrainPending"/>'in sırası).</summary>
        public int Order { get; init; }

        /// <summary>Görülen tekil uyarıların parmak izleri (gösterilen ve gizlenen).</summary>
        public HashSet<ulong> Seen { get; } = [];

        /// <summary>Gösterilen tekil uyarı sayısı (≤ <see cref="ShownWarningsPerProject"/>).</summary>
        public int Shown { get; set; }

        /// <summary>Gizlenen tekil uyarı sayısı — özetin <c>n</c>'i.</summary>
        public int Hidden { get; set; }

        /// <summary>Gizlenen tekil uyarılar, koda göre.</summary>
        public Dictionary<string, int> HiddenByCode { get; } = new(StringComparer.Ordinal);

        /// <summary>Özetlenen gizli uyarıları unutur (<see cref="DrainPending"/>): görülenlerin izi ve gösterilenlerin sayısı kalır.</summary>
        public void ForgetHidden()
        {
            Hidden = 0;
            HiddenByCode.Clear();
        }
    }
}
