namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3a] Anlatı (narrative) konsol satırının görsel tipi — design §2.5 NARR_COLORS ile birebir:
/// cmd=text-primary, info=text-secondary, dim=text-faint, success/warn/error=ilgili <c>-text</c> tonu.
/// Renk eşlemesi <see cref="ConsolePalette"/>'tedir (token brush'ları; hardcode YASAK).
/// </summary>
public enum ConsoleLineType { Cmd, Info, Dim, Warn, Error }

/// <summary>
/// [T56/3a] Bir konsol satırının DÜZ METNİNDEN görsel tipini türeten SAF sınıflandırıcı.
///
/// <para><b>Line-type metadata kararı (task 3a):</b> VM düz string post ediyor; colorizer için satır TİPİ
/// gerek. Seçim: tipi <b>satır metninden türet</b> (batch/marshal sözleşmesine DOKUNMA). Böylece
/// <see cref="ConsoleBatcher.Post"/> imzası ve marshal-free <c>OnProjectLog</c> yolu değişmez; her belge
/// (doküman swap / reseed sonrası bile) KENDİNİ tanımlar — offset'lerle senkron tutulan paralel bir tip
/// dizisi gerekmez.</para>
///
/// <para><b>[design v1.7.0 §2.5] Saat ve <c>▸</c> kolonu YOK.</b> Satır artık yalnız metindir ve TAMAMI tek
/// bir tip rengiyle boyanır — önceden satırın başında sabit genişlikte bir duvar saati (faint) ve komut
/// satırlarında amber bir <c>▸</c> vardı, ikisi de ayrı aralık olarak renklendiriliyordu. Kaldırıldılar:
/// gerçek bir koşuda saniyede yüzlerce satır akar, damga bilgi taşımaz ve tüm satırların imleçle aynı sol
/// hizada başlaması yığını taramayı kolaylaştırır.</para>
///
/// <para><b>[idle-memory · dalga 2 · spec §6.5] Kompakt konsolun da TEK sınıflandırıcısıdır.</b>
/// <see cref="CompactConsoleFilter"/> bir proje satırının anlatıya gidip gitmeyeceğine buradaki tiple karar verir (hata, uyarı,
/// gerisi) ve gizlediği uyarıyı AYNI ayrıştırmanın verdiği tanı koduyla sayar (<see cref="Classify(ReadOnlySpan{char}, out ReadOnlySpan{char})"/>)
/// — "uyarı nedir" sorusunun ikinci bir tanımı yoktur; uygulamanın ve motorun kendi uyarısını (<see cref="IsOwnWarning"/>) süzmez.
/// Süzgecin kendi özet satırını ise süzgeç tanımlar; burası onu süzgece sorarak tanır.</para>
///
/// <para><b>[fix 1 · M4] Ayırma yapmaz.</b> Kompakt konsol sınıflandırmayı IPC thread'inde, VM'in kilidi altında her proje satırında
/// sorar (OSYS koşusunda ~390 k satır), renklendirici her çizilen satırda: satır bir span olarak okunur, kırpılmış kopyası ya da
/// birleştirilmiş arama metni üretilmez.</para>
/// </summary>
public static class ConsoleLineClassifier
{
    /// <summary>
    /// Uygulamanın KOMUT olarak bastığı satırların baş sözcükleri. <c>▸</c> öneki kaldırıldığı için komut
    /// satırının tek işareti budur; liste uygulamanın gerçekten çağırdığı araçlarla sınırlıdır
    /// (<c>SyncWorkspaceService</c>'in git satırı, MSBuild/NuGet çağrı satırları) ve kullanıcı metni değildir.
    /// </summary>
    private static readonly string[] CommandHeads = ["git ", "msbuild ", "nuget ", "dotnet "];

    /// <summary>
    /// <b>[DEĞİŞEN KURAL] Yalnız KAYNAĞI belli satırlar renklenir; metin tahmini kaldırıldı.</b>
    ///
    /// <para><b>Eski iddia:</b> satır içinde <c>failed</c>/<c>succeeded</c>/<c>✓</c>/<c>✗</c> geçiyorsa kırmızı
    /// ya da yeşil boyanır ve <c>warning</c> kelimesi nerede geçerse geçsin turuncu yapar.</para>
    ///
    /// <para><b>Değişme gerekçesi (kullanıcı):</b> "kesin bir ayrım yoksa tek renk olabilir, tahmine göre
    /// yapıyorsak". Haklıydı: o taramalar bir PROJE ADININ içindeki kelimeye de takılırdı ve renk o noktada
    /// bilgi değil gürültüdür. Geriye yalnız formatı BELLİ olan iki kaynak kalır — MSBuild'in kendi tanı
    /// satırları (<c>… : error CS0103: …</c>, <c>… : warning MSB3277: …</c>) ve uygulamanın KENDİ bastığı
    /// önekler (<c>[error]</c>, <c>warning:</c>, komut satırları) — ve kompakt konsolun kendi özet satırı (biçimi
    /// <see cref="CompactConsoleFilter.IsSummary"/>'de). Bunlar tahmin değildir.</para>
    ///
    /// <para><c>Success</c> tipi bu kararla tamamen kalktı: onu döndüren tek yol metin taramasıydı. Bir
    /// koşunun başarısı zaten şeritte, listede ve event stream'de üç ayrı yerde söyleniyor — konsolun ham
    /// çıktısında ayrıca renklenmesi gerekmiyor.</para>
    /// </summary>
    public static ConsoleLineType Classify(string? text) => Classify(text.AsSpan(), out _);

    /// <summary>
    /// <see cref="Classify(string?)"/>'nin span hâli; satır MSBuild'in tanı satırıysa onun kodunu da verir (<c>CS1591</c>,
    /// <c>MSB3277</c>, <c>NU1903</c>… — yoksa boş): kompakt konsol gizlediği uyarıyı bununla sayar. Kod satırın bir dilimidir —
    /// string'e yalnız sayaca YENİ bir kod girerken çevrilir.
    /// <para><b>[DEĞİŞEN KURAL — fix 1 · I1] Sıra:</b> uygulamanın kendi önekleri (<c>[error]</c>, <c>warning:</c>) ve kompakt özet
    /// satırı, sonra MSBuild'in KANONİK tanı biçimi (<see cref="TryParseDiagnostic"/>), EN SONDA komut sözcükleri. <b>Eski sıra:</b>
    /// komut sözcükleri en baştaydı ve tanı araması <c>": warning "</c>'ı <c>": error "</c>'dan önce, satırın her yerinde arıyordu.
    /// <b>Değişme gerekçesi (inceleme I1):</b> kompakt kipte üç gerçek hata biçimi kayboluyordu — <c>MSBUILD : error MSB4166: …</c>
    /// komut sözcüğüyle başladığı için komut; <c>… : error NU1605: Warning As Error: …</c> (SDK projelerinde varsayılan
    /// warning-as-error) metindeki "Warning" yüzünden uyarı; alt kategorili <c>… : XamlCompiler error WMC1006: …</c> bilgi
    /// sayılıyordu. Kanonik biçim türü İLK tanı sözcüğünden alır; metindeki sözcükler sayılmaz.</para>
    /// </summary>
    public static ConsoleLineType Classify(ReadOnlySpan<char> text, out ReadOnlySpan<char> code)
    {
        code = default;
        ReadOnlySpan<char> content = text.TrimStart();
        if (content.IsEmpty) return ConsoleLineType.Info;

        // Uygulamanın KENDİ önekleri — kaynağı biziz, tahmin yok.
        if (content.StartsWith("[hata]", StringComparison.Ordinal) ||
            content.StartsWith("[error]", StringComparison.OrdinalIgnoreCase))
            return ConsoleLineType.Error;
        if (IsOwnWarning(content))
            return ConsoleLineType.Warn;
        // [spec §6.5] Kompakt konsolun özet satırı ("A: 7 more warnings — CS1591 ×7 — see the project log") — o da bizim
        // satırımızdır ve uyarı rengindedir. Biçimin TEK tanımı süzgeçtedir; burada ikinci bir literal yok.
        if (CompactConsoleFilter.IsSummary(content))
            return ConsoleLineType.Warn;

        // MSBuild'in kanonik tanı satırı — komut sözcüklerinden ÖNCE: "MSBUILD : error MSB1009: …" bir hatadır, komut değil.
        if (TryParseDiagnostic(content, out var category, out code))
            return category;

        foreach (string head in CommandHeads)
            if (content.StartsWith(head, StringComparison.OrdinalIgnoreCase))
                return ConsoleLineType.Cmd;

        return ConsoleLineType.Info;
    }

    /// <summary>
    /// [tek tanım · dalga 2 son inceleme N5] Satır uygulamanın — motor dahil — KENDİ uyarısı mı: <c>warning:</c> önekiyle başlar (baştaki
    /// boşluk sayılmaz, büyük-küçük harf duyarsız). Sınıflandırıcı bu satırı uyarı renginde boyar; kompakt konsol onu süzmez ve saymaz
    /// (motorun projenin loguna yazdığı bağımlılık notları — spec §6.5 "motor notları süzülmez"). Önekin tek tanımı burasıdır.
    /// </summary>
    public static bool IsOwnWarning(ReadOnlySpan<char> text) =>
        text.TrimStart().StartsWith(OwnWarningPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Uygulamanın ve motorun uyarı öneki (<see cref="IsOwnWarning"/>).</summary>
    private const string OwnWarningPrefix = "warning:";

    /// <summary>
    /// [fix 1 · I1] MSBuild'in kanonik tanı biçimi (<c>Microsoft.Build.Shared.CanonicalError</c>):
    /// <c>köken: [alt kategori ]error|warning[ KOD]: metin</c>. Köken bir dosya yolu (sürücü harfi ve isteğe bağlı <c>N&gt;</c> düğüm
    /// öneki dahil), bir araç (<c>MSBUILD</c>, <c>csc</c>, <c>EXEC</c>) ya da proje dosyasıdır ve bir sonraki iki noktada biter; tür o
    /// bölümün (bir sonraki iki noktaya kadar) son ya da sondan bir önceki sözcüğüdür — ardında en çok tek bir kod; önündeki serbest
    /// sözcükler alt kategoridir (<c>XamlCompiler</c>). Kökensiz biçimde tür satırın EN BAŞINDADIR (<c>warning NU1701: …</c>) ve alt
    /// kategori alınmaz: serbest metindeki "the error is: x" tanı değildir. Tür ilk tanı sözcüğüdür — kökenden sonraki bölümde durur,
    /// metindeki "Warning As Error" sayılmaz.
    /// <para>[DEĞİŞEN SIRA — dalga 2 son inceleme · defter 6] Satır bir tür sözcüğüyle başlıyorsa kökensiz biçim ÖNCE denenir: tür satır
    /// başındaki İLK tanı sözcüğüdür (<c>error X1: see the warning list: y</c> bir hatadır, kodu <c>X1</c>). Eski sırada düz köken
    /// araması önce koşuyordu: satırın ilk iki noktaya kadarki bölümü ("error X1") köken sayılıyor, türü metnin ilerisindeki sözcük
    /// veriyordu (uyarı, kodu "list"). Kökensiz biçim tutmazsa kökenli biçimler eskisi gibi denenir.</para>
    /// </summary>
    private static bool TryParseDiagnostic(ReadOnlySpan<char> content, out ConsoleLineType category, out ReadOnlySpan<char> code)
    {
        // Kökensiz biçim satırın başındaki tür sözcüğüyle — ilk tanı sözcüğü karar verir.
        bool categoryFirst = StartsWithCategory(content);
        if (categoryFirst && TryParseCategory(content, allowSubcategory: false, out category, out code))
            return true;
        // Sürücü harfli köken (C:\…\A.cs(1,1)): sürücünün iki noktası kökene aittir, köken bir sonraki iki noktada biter.
        int start = NodePrefixLength(content);
        if (content.Length > start + 1 && char.IsAsciiLetter(content[start]) && content[start + 1] == ':')
        {
            int end = content[(start + 2)..].IndexOf(':');
            if (end >= 0 && TryParseCategory(content[(start + 2 + end + 1)..], allowSubcategory: true, out category, out code))
                return true;
        }
        // Düz köken (MSBUILD, csc, proje dosyası): ilk iki noktaya kadar.
        int colon = content.IndexOf(':');
        if (colon >= 0 && TryParseCategory(content[(colon + 1)..], allowSubcategory: true, out category, out code))
            return true;
        // Kökensiz: tür satırın başında (tür sözcüğüyle başlayan satırda zaten denendi).
        if (!categoryFirst) return TryParseCategory(content, allowSubcategory: false, out category, out code);
        category = ConsoleLineType.Info;
        code = default;
        return false;
    }

    /// <summary>Satırın ilk sözcüğü (boşluğa ya da iki noktaya kadar) bir tanı türü mü — kökensiz biçimin önce denendiği satır.</summary>
    private static bool StartsWithCategory(ReadOnlySpan<char> content)
    {
        int end = content.IndexOfAny(' ', '\t', ':');
        return TryCategory(end < 0 ? content : content[..end], out _);
    }

    /// <summary>MSBuild'in çok düğümlü konsol çıktısındaki düğüm öneki (<c>12&gt;</c>): uzunluğu, yoksa 0.</summary>
    private static int NodePrefixLength(ReadOnlySpan<char> content)
    {
        int i = 0;
        while (i < content.Length && char.IsAsciiDigit(content[i])) i++;
        return i > 0 && i < content.Length && content[i] == '>' ? i + 1 : 0;
    }

    /// <summary>Kökenden sonraki bölüm (ilk iki noktaya kadar): <c>[alt kategori ]tür[ KOD]</c>. Tür son sözcükse kod yoktur; değilse
    /// son sözcük koddur ve tür ondan önceki sözcüktür. Türün önünde sözcük kalırsa o alt kategoridir — yalnız kökenli biçimde.</summary>
    private static bool TryParseCategory(ReadOnlySpan<char> rest, bool allowSubcategory, out ConsoleLineType category,
        out ReadOnlySpan<char> code)
    {
        code = default;
        category = ConsoleLineType.Info;
        int colon = rest.IndexOf(':');
        if (colon < 0) return false;
        ReadOnlySpan<char> part = rest[..colon].TrimEnd();
        int split = part.LastIndexOfAny(' ', '\t');
        ReadOnlySpan<char> codeToken = default;
        if (!TryCategory(part[(split + 1)..], out category))
        {
            if (split < 0) return false;
            codeToken = part[(split + 1)..];
            part = part[..split].TrimEnd();
            split = part.LastIndexOfAny(' ', '\t');
            if (!TryCategory(part[(split + 1)..], out category)) return false;
        }
        if (split >= 0 && !allowSubcategory) // kökensiz biçimde tür satırın başındadır
        {
            category = ConsoleLineType.Info;
            return false;
        }
        code = codeToken;
        return true;
    }

    /// <summary>Bir sözcük MSBuild'in tanı türü mü (<c>error</c> / <c>warning</c>; büyük-küçük harf duyarsız — MSBuild'de yerelleşmez,
    /// Türkçe MSBuild de <c>warning</c> yazar).</summary>
    private static bool TryCategory(ReadOnlySpan<char> word, out ConsoleLineType category)
    {
        if (word.Equals("error", StringComparison.OrdinalIgnoreCase)) { category = ConsoleLineType.Error; return true; }
        if (word.Equals("warning", StringComparison.OrdinalIgnoreCase)) { category = ConsoleLineType.Warn; return true; }
        category = ConsoleLineType.Info;
        return false;
    }
}
