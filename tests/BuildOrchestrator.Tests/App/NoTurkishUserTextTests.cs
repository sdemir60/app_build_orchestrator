using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/B2 · E3] <b>Uygulama İngilizce-only'dir.</b> Bu guard, kullanıcıya ulaşan bir metinde Türkçe kalırsa
/// süiti KIRAR — UI metinleri · git/worktree mesajları · planlama mesajları · run/decision.log satırları ·
/// exception mesajları · MSBuild build-error metinleri · script çıktıları.
///
/// <para><b>Neden kalıcı bir guard:</b> It-5'te bir tur süpürme yapılmış (77 Türkçe metin) ama guard
/// konmamıştı, sızıntı geri geldi. Tek seferlik süpürme bu sınıfı kapatmaz; kapatan şey buradaki testtir.</para>
///
/// <para><b>İKİ EKSEN — biri tek başına YETMEZ.</b>
/// (a) <see cref="TurkishCharacters"/>: Türkçeye özgü harfler. Yakalayamadığı sınıf: saf-ASCII yazılmış
/// Türkçe (<c>hata</c>, <c>bitti</c>, <c>belirlenemedi</c>) — B2 süpürmesinde bu sınıftan <b>4</b> sızıntı
/// vardı ve karakter ekseni ÜÇÜNÜ DE görmedi (<c>ConsoleLine</c>, <c>MsBuildResolver</c>,
/// <c>RunCoordinator</c>, ayrıca <c>.csproj</c>'deki üç transliterasyon).
/// (b) <see cref="TurkishWords"/>: Türkçe kelime listesi. Yakalayamadığı sınıf: listede olmayan kelimeler —
/// bu yüzden karakter ekseni de gerekir. İki eksen birbirinin kör noktasını kapatır.</para>
///
/// <para><b>Yorumlar taramaya GİRMEZ</b> — bu projenin yorumları tasarım gereği Türkçedir (CLAUDE.md).
/// Bu ayrım satır-başı <c>//</c> kontrolüyle DEĞİL, <see cref="SourceLiterals"/> tokenizer'ıyla yapılır;
/// gerekçesi orada. Aynı sebeple <c>—</c> <c>▸</c> <c>✓</c> <c>…</c> <c>“ ”</c> gibi Türkçe OLMAYAN çok
/// baytlı karakterler karakter sınıfının DIŞINDADIR: içeride olsalardı guard gürültüden kullanılamaz hâle
/// gelir ve birileri onu devre dışı bırakırdı.</para>
/// </summary>
public class NoTurkishUserTextTests
{
    // ---------------------------------------------------------------- eksen (a): Türkçeye özgü harfler
    /// <summary>YALNIZ Türkçeye özgü harfler. <c>—</c>/<c>▸</c>/<c>✓</c>/<c>…</c>/<c>“ ”</c> KASITLI olarak
    /// yoktur: onlar çok baytlıdır ama Türkçe değildir (mevcut İngilizce metinler onları KULLANIR).</summary>
    private static readonly Regex TurkishCharacters = new("[çğıöşüÇĞİÖŞÜ]", RegexOptions.Compiled);

    // ---------------------------------------------------------------- eksen (b): saf-ASCII Türkçe kelimeler
    /// <summary>
    /// Saf-ASCII yazılabilen Türkçe — karakter ekseninin kör noktası. İki parçadan oluşur:
    ///
    /// <para><b>1) Kelime gövdeleri.</b> Kaynağı: (a) B2 süpürmesinde ÜRETİMDE fiilen bulunanlar, (b) sızması
    /// muhtemel yüksek frekanslı Türkçe kelimeler. İngilizce eş-yazımlılar KASITLI olarak DIŞARIDA — ör.
    /// <c>once</c> ("önce") İngilizce bir kelimedir ve üretimdeki <c>"…once the build starts"</c> metnini
    /// yanlış-pozitif yapardı; aynı sebeple <c>var</c>, <c>bin</c>, <c>int</c>, <c>an</c> yoktur. Aynı şekilde
    /// <c>\w+ler</c> gibi bir çoğul eki DE yoktur: <c>compiler</c>/<c>handler</c>/<c>installer</c> ile
    /// çakışırdı.</para>
    ///
    /// <para><b>2) Üretken son ekler.</b> Salt liste kırılgandır: Türkçe morfolojisi üretkendir, bu yüzden bir
    /// sonraki sızıntı listede OLMAYAN bir çekim olur (bu testin kendi regresyon vakası tam olarak bunu
    /// gösterdi: <c>cozulemedi</c> ve <c>yazildi</c> liste-yalnız bir eksende görünmüyordu). Aşağıdaki ekler
    /// Türkçeye özgüdür ve İngilizce ile çakışma riski düşüktür; her biri <c>\b</c> ile sınırlıdır.</para>
    ///
    /// <para><b>Bu eksen neden <c>.ps1</c>/<c>.csproj</c>'de KRİTİK:</b> o dosyalar zorunlu olarak ASCII'dir
    /// (<c>verify-publish.ps1</c>:249-250 — BOM'suz dosyada PS 5.1 ANSI çözümü çok baytlı karakteri bozar), yani
    /// oradaki Türkçe HER ZAMAN transliteredir ve karakter ekseni orada HİÇ çalışmaz. Tüm yük bu eksendedir.</para>
    ///
    /// <para><b>Bilinen sınır:</b> bu eksen bir sezgiseldir — listede olmayan ve tanıdık bir ek taşımayan
    /// transliteredilmiş bir kelime (ör. tek başına <c>yerlesim</c>) kaçabilir. Kapsamı genişletmek kolaydır
    /// (buraya bir gövde eklemek), daraltmak ise yanlış-pozitif üretir; bu denge bilinçlidir.</para>
    /// </summary>
    private static readonly Regex TurkishWords = new(
        // --- 1) gövdeler
        @"\b(?:adim|aktivasyon|ama|aynen|ayar(?:lar)?|basari(?:li|siz)|beklen(?:en|medik|meyen)|bilinmeyen"
        + @"|bitti|bos|cikis|cikti|deger(?:i)?|deneme|devam|dizin(?:i)?|dogrulama|dolu|dosya(?:si)?|eksik"
        + @"|erisim|gecerli|gecersiz|gecici|gerek(?:li)?|giris|gizle|goster|hata(?:si)?|havuz|hazir|hepsi"
        + @"|icerigi|iptal|izin|kapanis|kare(?:ler)?|klasor(?:u)?|kodu|komutu|konsol|kontrolu|kosul|kullanici"
        + @"|kurulu|listesi|makine|modunda|olamaz|olcum|ornek|ornegi|paleti|proje(?:ler|si)?|punto|sayisi"
        + @"|sec(?:ili|im|enek)|sirasinda|sonra|sonuc|sorgusu|surum|tamam|varsayilan|yerel|yerlesim(?:i)?"
        + @"|yeniden|yolu|zaten|zorunlu)\b"
        // --- 2) üretken son ekler (Türkçeye özgü çekimler)
        + @"|\w{2,}m[ae]di\b"                                    // olumsuz geçmiş: bulunamadi, cozulemedi, olculmedi
        + @"|\w{2,}(?:ildi|uldu|andi|endi|indi|ondu|undu)\b"      // edilgen/geçmiş: yazildi, silindi, olusturuldu
        + @"|\w{2,}(?:iyor|uyor)\b"                              // şimdiki zaman: calisiyor, bekleniyor
        + @"|\w{2,}(?:ecek|acak)\b"                              // gelecek: denenecek, derlenecek
        + @"|\w{2,}(?:lari|leri)\b"                              // çoğul iyelik: dosyalari, projeleri
        + @"|\w{3,}(?:lik|ligi|lugu)\b"                          // ad yapan ek: guvenlik, temizlik, dogrulugu
        + @"|\w{3,}(?:masi|mesi)\b"                              // ad-fiil: olusturulmasi, dogrulanmasi
        + @"|\w{3,}(?:abilir|ebilir)\b",                         // yeterlilik: kosulabilir, kirilabilir
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ---------------------------------------------------------------- İSTİSNALAR (açık liste, gerekçeli)
    /// <summary>Argümanları taranmayan çağrılar. <b>Sessiz istisna YASAK</b> — her biri burada, gerekçesiyle.</summary>
    private static readonly string[] IgnoredCallers =
    [
        // Debug.WriteLine kullanıcıya ULAŞMAZ: Release'te [Conditional("DEBUG")] ile derlenip çıkar, çıktısı
        // yalnız debugger'a gider. Kullanıcı kararıyla B2 kapsamının DIŞINDA bırakıldı.
        "Debug.WriteLine",
    ];

    /// <summary>Taramadan muaf dosyalar — <c>src/</c> köküne göreli. Her biri TEK SATIR gerekçeyle.</summary>
    private static readonly (string File, string Reason)[] FileExceptions =
    [
        // ConsoleLine.cs, "[hata]" önekini StartsWith ile TANIR (girdi eşleştirme) — bu metin kullanıcıya
        // YAZILMAZ, okunur; VM'in kendisi "[error]" üretir. Kaldırmak davranış değişikliği olurdu (Concerns).
        (Path.Combine("BuildOrchestrator.App", "Console", "ConsoleLine.cs"),
         "\"[hata]\" bir GİRDİ token'ıdır (StartsWith comparand), kullanıcıya yazılan bir metin değil."),
    ];

    private static string[] ExceptedFiles => [.. FileExceptions.Select(e => e.File)];

    // ================================================================================================
    // ANA GUARD
    // ================================================================================================

    [Fact] // eksen (a) — src ağacı: C# + XAML
    public void No_Turkish_characters_reach_the_user_from_the_src_tree()
    {
        var offenders = SourceGuard.ScanSrcLiterals("*.cs", TurkishCharacters, ExceptedFiles, IgnoredCallers)
            .Concat(SourceGuard.ScanSrcLiterals("*.xaml", TurkishCharacters, ExceptedFiles, IgnoredCallers))
            .ToList();
        Assert.True(offenders.Count == 0, Report("Türkçe KARAKTER", offenders));
    }

    [Fact] // eksen (b) — src ağacı: C# + XAML
    public void No_Turkish_words_reach_the_user_from_the_src_tree()
    {
        var offenders = SourceGuard.ScanSrcLiterals("*.cs", TurkishWords, ExceptedFiles, IgnoredCallers)
            .Concat(SourceGuard.ScanSrcLiterals("*.xaml", TurkishWords, ExceptedFiles, IgnoredCallers))
            .ToList();
        Assert.True(offenders.Count == 0, Report("Türkçe KELİME", offenders));
    }

    // ---------------------------------------------------------------- src/ DIŞI taranan yüzeyler
    /// <summary>
    /// [fix-1 · V1] <c>src/</c> dışındaki kullanıcıya görünür yüzeyler ve her birinin <b>asgari</b> dosya/
    /// literal sayısı. Eşik yoksa bir yüzey sessizce boşalabilir ve o eksen sonsuza dek yeşil kalır; bu tablo
    /// taranan kümenin boş OLMADIĞINI ayrıca assert edilebilir kılar (brief kural 4).
    /// Eşikler bugünkü gerçek ölçüme yakındır (bkz. <see cref="The_scan_really_reads_the_scripts_and_the_msbuild_files"/>).
    /// </summary>
    private static readonly (string Pattern, int MinFiles, int MinLiterals)[] RepoScanSurfaces =
    [
        ("*.ps1",    2, 180),   // ölçüm: 2 dosya / 208 literal
        ("*.csproj", 4,  70),   // ölçüm: 4 dosya /  84 metin (tests/ hariç)
        ("*.props",  1,   6),   // ölçüm: 1 dosya /   8 metin (kökteki Directory.Build.props)
    ];

    /// <summary>[fix-1 · S1] Repo taramasından çıkarılan ağaç. <c>tests/</c> GELİŞTİRİCİYE bakar — assert
    /// mesajları ve fixture'ları projenin konvansiyonu gereği Türkçedir (CLAUDE.md). Guard oraya bakarsa er
    /// geç meşru bir Türkçe dev-script metninde kırmızı verir ve birileri toptan bir istisna ekler — It-5
    /// süpürmesini öldüren tam olarak bu kalıptır. Belge ile kod bu kararda AYNI hizada.</summary>
    private static readonly string[] ExcludedRepoRoots = ["tests"];

    [Fact] // src/ DIŞI kullanıcıya görünen yüzeyler: script çıktıları + MSBuild build-error metinleri
    public void No_Turkish_reaches_the_user_from_scripts_and_msbuild_messages()
    {
        var offenders = new List<string>();
        foreach (var (pattern, _, _) in RepoScanSurfaces)
            foreach (var rule in new[] { TurkishCharacters, TurkishWords })
                offenders.AddRange(SourceGuard.ScanRepoLiterals(
                    pattern, rule, ignoredCallers: IgnoredCallers, excludedRootFolders: ExcludedRepoRoots));

        Assert.True(offenders.Count == 0, Report("Türkçe (script/MSBuild)", offenders));
    }

    // ================================================================================================
    // VAKUM KARŞITI: tarama GERÇEKTEN bir şey gördü mü?
    // Bir tarama testinin en büyük riski sessizce hiçbir şeyi taramamaktır — o hâlde sonsuza dek yeşildir.
    // Dosya sayısı TEK BAŞINA yetmez: dosyalar görülüp literal çıkarılamamış olabilir, o yüzden literal
    // sayısı AYRICA assert edilir.
    // ================================================================================================

    [Fact]
    public void The_scan_really_reads_the_src_tree_and_extracts_literals()
    {
        var csFiles = SourceGuard.ScannedSrcFiles("*.cs");
        var xamlFiles = SourceGuard.ScannedSrcFiles("*.xaml");
        // [fix-1 · V2] Eşikler bugünkü GERÇEK ölçüme yakındır (176 .cs · 21 .xaml · 1056 · 2555); ~%85'e
        // konumlandırıldı: sessiz bir çöküşü (dosya bulunamadı / tokenizer boş döndü) yakalar ama normal
        // dosya ekleme-çıkarmada kırılmaz.
        Assert.True(csFiles.Count >= 150, $"yalnız {csFiles.Count} .cs dosyası tarandı — ağaç kurulmamış olabilir.");
        Assert.True(xamlFiles.Count >= 18, $"yalnız {xamlFiles.Count} .xaml dosyası tarandı.");

        // Her üretim projesi GERÇEKTEN kapsamda mı (guard yalnız App'e bakıyor olmasın).
        Assert.Contains(Path.Combine("BuildOrchestrator.Core", "Git", "GitService.cs"), csFiles);
        Assert.Contains(Path.Combine("BuildOrchestrator.Supervisor", "RunCoordinator.cs"), csFiles);
        Assert.Contains(Path.Combine("BuildOrchestrator.Contracts", "Ipc", "IpcMessages.cs"), csFiles);
        Assert.Contains(Path.Combine("BuildOrchestrator.App", "ViewModels", "RibbonText.cs"), csFiles);

        int csLiterals = SourceGuard.CountSrcLiterals("*.cs");
        int xamlLiterals = SourceGuard.CountSrcLiterals("*.xaml");
        Assert.True(csLiterals >= 900, $"yalnız {csLiterals} C# literali çıkarıldı — tokenizer sessizce boş dönüyor olabilir.");
        Assert.True(xamlLiterals >= 2200, $"yalnız {xamlLiterals} XAML metni çıkarıldı.");
    }

    [Fact] // [fix-1 · V1] her yüzey için AYRI AYRI: dosya var mı VE literal çıktı mı
    public void The_scan_really_reads_the_scripts_and_the_msbuild_files()
    {
        Assert.Contains(Path.Combine("scripts", "verify-publish.ps1"),
                        SourceGuard.ScannedRepoFiles("*.ps1", ExcludedRepoRoots));
        Assert.Contains(Path.Combine("src", "BuildOrchestrator.App", "BuildOrchestrator.App.csproj"),
                        SourceGuard.ScannedRepoFiles("*.csproj", ExcludedRepoRoots));

        foreach (var (pattern, minFiles, minLiterals) in RepoScanSurfaces)
        {
            int files = SourceGuard.ScannedRepoFiles(pattern, ExcludedRepoRoots).Count;
            int literals = SourceGuard.CountRepoLiterals(pattern, ExcludedRepoRoots);
            Assert.True(files >= minFiles, $"{pattern}: yalnız {files} dosya tarandı (asgari {minFiles}).");
            Assert.True(literals >= minLiterals, $"{pattern}: yalnız {literals} metin çıkarıldı (asgari {minLiterals}).");
        }
    }

    [Fact] // [fix-1 · V1] .targets yüzeyi SESSİZCE vakum olmasın — bugün 0 dosya, bu AÇIKÇA pinlenir
    public void The_targets_surface_is_pinned_as_empty_rather_than_silently_vacuous()
    {
        var targets = SourceGuard.ScannedRepoFiles("*.targets", ExcludedRepoRoots);
        Assert.True(targets.Count == 0,
            $"Artık {targets.Count} adet .targets dosyası var ({string.Join(", ", targets)}) — bu yüzey "
            + "taranmıyor. RepoScanSurfaces'e asgari sayılarıyla EKLE, yoksa MSBuild mesajları guard'sız kalır.");
    }

    [Fact] // İstisnalar ÖLÜ kalmasın: muaf tutulan dosya hâlâ var ve hâlâ muafiyeti hak ediyor mu?
    public void Every_exception_is_still_live_and_still_needed()
    {
        foreach (var (file, reason) in FileExceptions)
        {
            string full = Path.Combine(RepoPaths.SrcRoot, file);
            Assert.True(File.Exists(full), $"karşılığı kalmamış istisna: {file} ({reason})");

            // Muafiyet gerçekten GEREKLİ mi — dosya muaf tutulmasa guard kırmızı düşer mi? Düşmüyorsa
            // istisna gereksizdir ve kaldırılmalıdır (ölü istisna, guard'ın deliğidir).
            // [fix-1 · V3] HER İKİ eksen de sorulur: yalnız kelime eksenine bakan bir kontrol, ileride
            // eklenecek KARAKTER-eksenli bir istisnayı haksız yere "gereksiz" damgalayıp attırırdı.
            string extension = Path.GetExtension(full);
            string content = File.ReadAllText(full);
            int hits = SourceGuard.ScanLiteralText(file, content, extension, TurkishWords, IgnoredCallers).Count
                     + SourceGuard.ScanLiteralText(file, content, extension, TurkishCharacters, IgnoredCallers).Count;
            Assert.True(hits > 0, $"istisna ARTIK GEREKSİZ, kaldır: {file} ({reason})");
        }
    }

    // ================================================================================================
    // AYIRT EDİCİLİK — sahte girdi ile: guard ihlali GERÇEKTEN görüyor mu?
    // Her iki eksen AYRI AYRI kanıtlanır (biri çalışıp diğeri çalışmıyor olabilir).
    // ================================================================================================

    [Fact] // eksen (a) ayırt edici mi
    public void The_character_axis_catches_Turkish_letters_in_a_literal_but_not_in_a_comment()
    {
        const string fake = """
            // Türkçe bir yorum: burada 'çıktı' geçiyor ve bu SORUN DEĞİL.
            /// <summary>Doküman satırı — 'başarısız' kelimesi burada serbesttir.</summary>
            public static string A = "islem basarisiz oldu: çıktı okunamadı";   // ← İHLAL
            public static string B = "Build succeeded — 0 errors";              // em dash: Türkçe DEĞİL
            public static string C = "▸ git fetch origin main … ✓";             // ▸ … ✓: Türkçe DEĞİL
            """;

        var offenders = SourceGuard.ScanLiteralText("Fake.cs", fake, ".cs", TurkishCharacters);
        string only = Assert.Single(offenders);
        Assert.Contains("çıktı okunamadı", only);
        Assert.Contains("Fake.cs:3", only);          // satır numarası doğru raporlanıyor
    }

    [Fact] // eksen (b) ayırt edici mi — ve karakter ekseninin GÖREMEDİĞİ sınıfı yakalıyor mu
    public void The_word_axis_catches_pure_ASCII_Turkish_that_the_character_axis_cannot_see()
    {
        const string fake = """
            public static string A = "run 7 bitti: outcome=completed";       // ← İHLAL (saf ASCII)
            public static string B = "vswhere hata: exit=1";                 // ← İHLAL (saf ASCII)
            public static string C = "No log yet - output streams here once the build starts.";
            """;

        // Karakter ekseni bu satırların HİÇBİRİNİ göremez — yanlış-negatif tuzağının kanıtı.
        Assert.Empty(SourceGuard.ScanLiteralText("Fake.cs", fake, ".cs", TurkishCharacters));

        var offenders = SourceGuard.ScanLiteralText("Fake.cs", fake, ".cs", TurkishWords);
        Assert.Equal(2, offenders.Count);
        Assert.Contains(offenders, o => o.Contains("bitti") && o.Contains("Fake.cs:1"));
        Assert.Contains(offenders, o => o.Contains("hata") && o.Contains("Fake.cs:2"));
        // İngilizce "once" (= "önce") yanlış-pozitif ÜRETMEZ: üretimdeki gerçek bir metindir.
        Assert.DoesNotContain(offenders, o => o.Contains("No log yet"));
    }

    [Fact] // istisna mekanizması ayırt edici mi
    public void Debug_WriteLine_arguments_are_exempt_but_the_same_text_elsewhere_is_not()
    {
        const string fake = """
            Debug.WriteLine($"[console pump] gözlenmeyen hata: {ex}");   // muaf
            AppendRunLine($"[console pump] gözlenmeyen hata: {ex}");     // ← İHLAL (aynı metin, kullanıcıya gider)
            """;

        var exempt = SourceGuard.ScanLiteralText("Fake.cs", fake, ".cs", TurkishCharacters, IgnoredCallers);
        string only = Assert.Single(exempt);
        Assert.Contains("Fake.cs:2", only);          // yalnız 2. satır — 1. satır muaf

        // Muafiyet OLMADAN iki satır da yakalanır: eleme gerçekten IgnoredCallers'tan geliyor.
        Assert.Equal(2, SourceGuard.ScanLiteralText("Fake.cs", fake, ".cs", TurkishCharacters).Count);
    }

    [Fact] // XAML / csproj / ps1 yolları da ayırt edici mi (yalnız .cs çalışıyor olmasın)
    public void The_guard_is_discriminating_on_XAML_and_csproj_and_PowerShell_too()
    {
        const string fakeXaml = """
            <!-- Türkçe yorum: 'başlık' burada serbesttir -->
            <TextBlock Text="Derleme başarısız" ToolTip="Build failed" />
            """;
        string xamlOnly = Assert.Single(SourceGuard.ScanLiteralText("F.xaml", fakeXaml, ".xaml", TurkishCharacters));
        Assert.Contains("Derleme başarısız", xamlOnly);

        const string fakeCsproj = """
            <!-- Türkçe yorum: 'cozulemedi' burada serbesttir -->
            <Error Text="Supervisor cikti dizini cozulemedi" />
            """;
        // Saf-ASCII transliterasyon: yalnız KELİME ekseni görür (karakter ekseni göremez).
        Assert.Empty(SourceGuard.ScanLiteralText("F.csproj", fakeCsproj, ".csproj", TurkishCharacters));
        Assert.Contains("cikti dizini", Assert.Single(
            SourceGuard.ScanLiteralText("F.csproj", fakeCsproj, ".csproj", TurkishWords)));

        const string fakePs1 = """
            # Türkçe yorum: 'bulunamadi' burada serbesttir
            Write-Host 'git bulunamadi - adim olculmedi'
            """;
        Assert.Contains("bulunamadi", Assert.Single(
            SourceGuard.ScanLiteralText("f.ps1", fakePs1, ".ps1", TurkishWords)));
    }

    [Fact] // regresyon: B2'de FİİLEN bulunan sızıntıların her biri en az bir eksende yakalanıyor mu
    public void Every_leak_this_task_removed_would_be_caught_by_at_least_one_axis()
    {
        string[] removedLeaks =
        [
            "gitExecutable boş olamaz.",                                  // char
            "beklenmeyen 'git rev-parse HEAD' çıktısı: '{sha}'",          // char
            "HEAD detached — branch belirlenemedi.",                      // word (saf ASCII!)
            "run {0} bitti: outcome={1}",                                 // word (saf ASCII!)
            "vswhere hata: exit={0}",                                     // word (saf ASCII!)
            "Supervisor cikti dosyasi bulunamadi",                        // word (saf ASCII!)
            "Copy contention algılandı ({0}), deneme {1}/{2} başarısız",  // char
            "Konsol paleti: '{key}' brush kaynağı bulunamadı.",           // char
            "yazildi: $out (kareler: $sizes)",                            // word (saf ASCII!)
        ];

        foreach (string leak in removedLeaks)
            Assert.True(TurkishCharacters.IsMatch(leak) || TurkishWords.IsMatch(leak),
                $"guard bu sızıntıyı HİÇBİR eksende yakalamıyor: {leak}");
    }

    private static string Report(string axis, IReadOnlyList<string> offenders) =>
        $"{offenders.Count} yerde {axis} kullanıcıya ulaşıyor (uygulama İngilizce-only):{Environment.NewLine}"
        + string.Join(Environment.NewLine, offenders.Select(o => "  " + o));
}
