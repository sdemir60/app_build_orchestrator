using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/B2 fix-1 · I3] <see cref="SourceLiterals"/> tokenizer'ının <b>SINIR TESPİTİ</b> testleri.
///
/// <para><b>Kök neden:</b> <c>NoTurkishUserTextTests</c> guard'ın KURALINI (Türkçe karakter/kelime) sınıyordu
/// ama guard'ın <b>girdisini üreten</b> tokenizer'ı hiç sınamıyordu. Tokenizer bir literalin sınırını sessizce
/// kaçırırsa o metin taramaya HİÇ girmez ve guard sonsuza dek yeşil kalır — yani <b>guard'ın kendisi vakum
/// olur</b>. Asıl koruma bu dosyadır: aşağıdaki testler, guard'ın "gördüğünü sandığı" metni gerçekten
/// gördüğünü kanıtlar.</para>
///
/// <para>Her test, tokenizer'ın çıkardığı metnin <b>sızıntı sayılacak parçayı İÇERDİĞİNİ</b> doğrular —
/// literalin tam sınırını byte-byte pinlemek yerine (kırılgan olurdu) "bu metin taramaya girdi mi"
/// sorusunu sorar; guard'ın gerçekten ihtiyaç duyduğu şey budur.</para>
/// </summary>
public class SourceLiteralsTests
{
    private static bool Sees(IEnumerable<SourceLiteral> literals, string needle) =>
        literals.Any(l => l.Text.Contains(needle, StringComparison.Ordinal));

    // ============================================================ C#

    [Fact] // [fix-1 · C1] iç içe tırnaklı interpolated string — CANLI kör nokta
    public void An_interpolated_string_with_a_nested_quoted_literal_is_fully_scanned()
    {
        // GitService.cs:207'nin (mevcut kod) birebir şekli: `??` sağındaki fallback metni İÇ literaldir.
        const string code = """
            return Fail($"ref okunamadi: {tracking.Error ?? "beklenmeyen bos sonuc"}");
            """;

        var literals = SourceLiterals.FromCSharp(code);

        // Dış metin zaten görülüyordu; asıl mesele İÇ literal.
        Assert.True(Sees(literals, "ref okunamadi"), "dış metin taramaya girmedi");
        Assert.True(Sees(literals, "beklenmeyen bos sonuc"),
            "İÇ literal taramaya HİÇ girmedi — bu metne konan Türkçe guard'ı yeşil bırakır.");
    }

    [Fact] // [fix-1 · C1] hole'un İÇİNDEKİ ifade de metin taşıyabilir (iç içe interpolation dahil)
    public void Literals_inside_interpolation_holes_are_scanned_at_every_nesting_level()
    {
        const string code = """"
            var a = $"dis: {Fmt("ic literal")} son";
            var b = $"dis2: {(ok ? $"ic interpolated {x}" : "digeri")} son";
            """";

        var literals = SourceLiterals.FromCSharp(code);

        Assert.True(Sees(literals, "ic literal"), "hole içindeki literal görülmedi");
        Assert.True(Sees(literals, "ic interpolated"), "iç içe interpolated string görülmedi");
        Assert.True(Sees(literals, "digeri"), "hole'daki ikinci literal görülmedi");
        Assert.True(Sees(literals, "son"), "interpolated string'in sonu yanlış yerde bitti");
    }

    [Fact] // interpolated string'in SONU doğru bulunuyor mu — sonrası KOD, literal değil
    public void The_end_of_an_interpolated_string_is_found_so_following_code_is_not_treated_as_text()
    {
        const string code = """
            var a = $"metin {x ?? "ic"} bitti"; DoSomething(sadeceKod);
            """;

        var literals = SourceLiterals.FromCSharp(code);

        Assert.True(Sees(literals, "bitti"), "kapanış tırnağı yanlış yerde bulundu");
        Assert.False(Sees(literals, "sadeceKod"), "literal sınırı kaçtı — KOD metin sanıldı");
    }

    [Fact] // verbatim / raw / kaçış biçimleri
    public void Verbatim_and_raw_and_escaped_string_forms_are_scanned()
    {
        // Girdi, C# kaçışlarının kaçışıyla okunmaz hâle gelmesin diye ham string içinde kurulur;
        // """" çiti sayesinde içeride üç tırnak (ham string örneği) serbestçe yazılabilir.
        const string code = """"
            var a = @"verbatim ""kacisli"" metin1";
            var b = $@"verbatim interpolated {x} metin2";
            var c = "ters bolu \" ile kacisli metin3";
            var d = """
                    ham string metin4
                    """;
            """";

        var literals = SourceLiterals.FromCSharp(code);

        Assert.True(Sees(literals, "metin1"), "verbatim \"\" kaçışı literali böldü");
        Assert.True(Sees(literals, "metin2"), "verbatim interpolated görülmedi");
        Assert.True(Sees(literals, "metin3"), "\\\" kaçışı literali böldü");
        Assert.True(Sees(literals, "metin4"), "ham string görülmedi");
    }

    [Fact] // yorumlar taramaya GİRMEZ (guard'ın temel varsayımı)
    public void Comments_never_enter_the_scan_even_when_they_trail_code()
    {
        const string code = """
            var a = "gorunur";        // satir sonu yorumu: gizli1
            /// <summary>xml yorumu: gizli2</summary>
            /* blok yorumu: gizli3 */
            var b = 'x';              // char literali metin degil
            """;

        var literals = SourceLiterals.FromCSharp(code);

        Assert.True(Sees(literals, "gorunur"));
        Assert.False(Sees(literals, "gizli1"), "satır SONU yorumu taramaya girdi");
        Assert.False(Sees(literals, "gizli2"), "xml doküman yorumu taramaya girdi");
        Assert.False(Sees(literals, "gizli3"), "blok yorumu taramaya girdi");
    }

    // ============================================================ PowerShell

    // [fix-1 · C2] Here-string testlerinin ayırt ediciliği "metin görüldü mü" ile ÖLÇÜLEMEZ: bozuk tokenizer
    // sınırı kaçırdığında metni yine de (yanlış) bir literalin içinde bırakabiliyor. Ayırt edici sinyal,
    // KODUN metin sanılmasıdır — `Write-Host` bir komut token'ıdır ve hiçbir literalin içinde olamaz.

    [Fact] // [fix-1 · C2] çift tırnaklı here-string
    public void A_double_quoted_here_string_is_one_literal_and_does_not_swallow_the_following_code()
    {
        const string script = """
            $a = @"
            metin1 icinde tek " tirnak
            "@
            Write-Host 'metin3'
            """;

        var literals = SourceLiterals.FromPowerShell(script);

        Assert.True(Sees(literals, "metin1"), "here-string gövdesi taramaya girmedi");
        Assert.True(Sees(literals, "metin3"), "here-string sonrası literal kayboldu");
        Assert.False(Sees(literals, "Write-Host"),
            "here-string sınırı kaçtı — KOD metin sanıldı (sonraki literaller de kayar).");
    }

    [Fact] // [fix-1 · C2] tek tırnaklı here-string
    public void A_single_quoted_here_string_is_one_literal_and_does_not_swallow_the_following_code()
    {
        const string script = """
            $b = @'
            metin2 icinde tek ' tirnak
            '@
            Write-Host 'metin4'
            """;

        var literals = SourceLiterals.FromPowerShell(script);

        Assert.True(Sees(literals, "metin2"), "here-string gövdesi taramaya girmedi");
        Assert.True(Sees(literals, "metin4"), "here-string sonrası literal kayboldu");
        Assert.False(Sees(literals, "Write-Host"),
            "here-string sınırı kaçtı — KOD metin sanıldı.");
    }

    [Fact] // PowerShell yorumları ve kaçışları
    public void PowerShell_comments_are_skipped_and_escapes_are_honoured()
    {
        const string script = """
            # satir yorumu: gizli1
            <# blok yorumu: gizli2 #>
            Write-Host "gorunur1"
            Write-Host 'iki tirnak '' kacisi gorunur2'
            """;

        var literals = SourceLiterals.FromPowerShell(script);

        Assert.True(Sees(literals, "gorunur1"));
        Assert.True(Sees(literals, "gorunur2"), "'' kaçışı literali böldü");
        Assert.False(Sees(literals, "gizli1"), "# yorumu taramaya girdi");
        Assert.False(Sees(literals, "gizli2"), "<# #> yorumu taramaya girdi");
    }

    // ============================================================ XML / XAML / csproj

    [Fact] // [fix-1 · I2] tek tırnaklı öznitelik değeri
    public void Single_quoted_XML_attribute_values_are_scanned()
    {
        const string xml = """
            <Error Condition="'$(X)' == ''" Text='tek tirnakli mesaj metin1' />
            <TextBlock Text="cift tirnakli metin2" />
            """;

        var literals = SourceLiterals.FromXml(xml);

        Assert.True(Sees(literals, "metin1"), "tek tırnaklı öznitelik değeri taramaya girmedi");
        Assert.True(Sees(literals, "metin2"));
    }

    [Fact] // [fix-1 · I1] CDATA
    public void CDATA_blocks_are_scanned()
    {
        const string xml = """
            <Message><![CDATA[cdata icindeki metin1]]></Message>
            """;

        Assert.True(Sees(SourceLiterals.FromXml(xml), "cdata icindeki metin1"),
            "CDATA bloğu taramaya girmedi");
    }

    [Fact] // XML yorumları taramaya GİRMEZ + eleman metni girer
    public void XML_comments_are_skipped_but_element_text_is_scanned()
    {
        const string xml = """
            <!-- yorum: gizli1 -->
            <TextBlock>eleman metni gorunur1</TextBlock>
            """;

        var literals = SourceLiterals.FromXml(xml);

        Assert.True(Sees(literals, "gorunur1"));
        Assert.False(Sees(literals, "gizli1"), "XML yorumu taramaya girdi");
    }

    // ============================================================ satır numarası

    [Fact] // rapor edilen satır no doğru mu (ihlal listesi buna göre okunur)
    public void The_reported_line_number_points_at_the_start_of_the_literal()
    {
        const string code = """
            var a = 1;
            var b = 2;
            var c = "ucuncu satirdaki metin";
            """;

        var literal = Assert.Single(SourceLiterals.FromCSharp(code));
        Assert.Equal(3, literal.Line);
    }

    // ============================================================ uzantı yönlendirmesi

    [Fact] // From(...) doğru çıkarıcıya yönlendiriyor mu — bilinmeyen uzantı SESSİZCE boş dönmemeli mi?
    public void The_extension_router_covers_exactly_the_documented_surfaces()
    {
        Assert.NotEmpty(SourceLiterals.From("var a = \"x\";", ".cs"));
        Assert.NotEmpty(SourceLiterals.From("<T A=\"x\" />", ".xaml"));
        Assert.NotEmpty(SourceLiterals.From("<T A=\"x\" />", ".csproj"));
        Assert.NotEmpty(SourceLiterals.From("<T A=\"x\" />", ".props"));
        Assert.NotEmpty(SourceLiterals.From("<T A=\"x\" />", ".targets"));
        Assert.NotEmpty(SourceLiterals.From("Write-Host 'x'", ".ps1"));
        Assert.NotEmpty(SourceLiterals.From("var a = \"x\";", ".CS"));   // uzantı büyük/küçük harf duyarsız

        // Kapsanmayan uzantı BOŞ döner — guard bu yüzden hangi uzantıları taradığını AÇIKÇA seçer.
        Assert.Empty(SourceLiterals.From("anything \"x\"", ".txt"));
    }

    // ============================================================ gerçek üretim dosyası üzerinde

    [Fact] // canlı kanıt: GitService.cs'in iç içe tırnaklı satırı GERÇEKTEN taranıyor
    public void The_live_nested_quote_site_in_GitService_is_really_scanned()
    {
        string path = System.IO.Path.Combine(RepoPaths.SrcRoot, "BuildOrchestrator.Core", "Git", "GitService.cs");
        var literals = SourceLiterals.FromCSharp(System.IO.File.ReadAllText(path));

        // Bu metin `$"... {tracking.Error ?? "unexpected empty result"}"` içindeki İÇ literaldir.
        Assert.True(Sees(literals, "unexpected empty result"),
            "GitService'in iç içe tırnaklı fallback metni taramaya girmiyor — oraya konan Türkçe kaçardı.");
    }

    [Fact] // tokenizer'ın kendisi ihlali GÖRÜYOR mu: sınır testleri guard kuralıyla birlikte anlamlı
    public void A_Turkish_string_hidden_in_a_nested_interpolation_is_visible_to_a_rule()
    {
        const string code = """
            emit($"fetch failed: {err ?? "beklenmeyen bos sonuc"}");
            """;
        var rule = new Regex(@"\bbeklenmeyen\b");

        var offenders = SourceGuard.ScanLiteralText("Fake.cs", code, ".cs", rule);
        Assert.NotEmpty(offenders);
    }
}
