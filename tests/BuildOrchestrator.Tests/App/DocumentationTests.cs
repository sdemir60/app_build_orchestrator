using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>Kullanıcı dokümanları iki dillidir: İngilizce esas, Türkçe aynası aynı klasörde <c>.tr.md</c> ekiyle durur
/// (README, docs/FEATURES, docs/WHY). Ayna sessizce geri kalmasın diye bölüm sayıları eşit tutulur; WHY, FEATURES'ın
/// kazanım olan maddelerini aynı başlık ve aynı sırayla taşır; dokümanlar arası göreli linkler var olan dosyaya gider.
/// <para>Bölüm = kod bloğu dışındaki <c>## </c> ya da <c>### </c> satırı. FEATURES ve WHY grup başlığı olmayan düz
/// listelerdir, her <c>##</c> bir maddedir; <c>###</c> de sayılır ki bir gün eklenen alt başlık ayna ve sıra denetiminden
/// kaçmasın. WHY başlığı özelliğin adını ve <c> — </c> sonrasında kazanımını taşır (<c>Builds only what changed — less
/// waiting</c>); eşleştirme tireden önceki adla yapılır, kazanımın varlığı ve kısalığı ayrıca pinlidir.</para>
/// <para>Repo herkese açıktır: dokümanlar aracın ilk yazıldığı iç ürünün adını taşımaz, örnekler nötrdür (Acme).</para></summary>
public class DocumentationTests
{
    public static TheoryData<string, string> MirroredPairs => new()
    {
        { "README.md", "README.tr.md" },
        { "docs/FEATURES.md", "docs/FEATURES.tr.md" },
        { "docs/WHY.md", "docs/WHY.tr.md" },
    };

    /// <summary>Kullanıcı dokümanları: iki dilli çiftler + yalnız İngilizce kılavuz.</summary>
    private static readonly string[] UserDocuments =
    [
        "README.md", "README.tr.md",
        "docs/FEATURES.md", "docs/FEATURES.tr.md",
        "docs/WHY.md", "docs/WHY.tr.md",
        "docs/USER-GUIDE.md",
    ];

    /// <summary>Göreli linkleri denetlenen dokümanlar.</summary>
    public static TheoryData<string> LinkedDocuments => Data(UserDocuments);

    /// <summary>Repo'da herkese açık duran bütün dokümanlar: kullanıcı dokümanları + teknik referans, sürüm notları
    /// (uygulamaya da gömülür) ve çalışma kuralları. <c>.claude/</c> kayıtları tarihseldir, burada değildir.</summary>
    public static TheoryData<string> PublicDocuments => Data([.. UserDocuments, "ARCHITECTURE.md", "CHANGELOG.md", "CLAUDE.md"]);

    /// <summary>Herkese açık dokümanlarda iç ürünün adı geçmez; örnek gerekiyorsa nötr bir ad kullanılır. Desen, kodu
    /// bağlayan guard'ınkiyle aynıdır (<see cref="NoProductNameInCodeTests.ProductNameInIdentifier"/>).</summary>
    [Theory, MemberData(nameof(PublicDocuments))]
    public void Public_documents_do_not_name_the_internal_product(string document)
    {
        var hits = Read(document).Split('\n')
            .Select((line, index) => (line: line.TrimEnd('\r'), number: index + 1))
            .Where(x => NoProductNameInCodeTests.ProductNameInIdentifier.IsMatch(x.line))
            .Select(x => $"{document}:{x.number}: {x.line.Trim()}")
            .ToList();
        Assert.Empty(hits);
    }

    [Theory, MemberData(nameof(MirroredPairs))]
    public void The_Turkish_mirror_has_as_many_sections_as_the_English_document(string english, string turkish)
    {
        Assert.Equal(Sections(english).Count, Sections(turkish).Count);
    }

    [Theory, MemberData(nameof(MirroredPairs))]
    public void Each_language_links_to_the_other(string english, string turkish)
    {
        Assert.Contains($"]({Path.GetFileName(turkish)})", Read(english), StringComparison.Ordinal);
        Assert.Contains($"]({Path.GetFileName(english)})", Read(turkish), StringComparison.Ordinal);
    }

    /// <summary>WHY, FEATURES'ın alt kümesidir: her WHY maddesi FEATURES'ta aynı adla (kazanım tiresinden önceki başlık)
    /// vardır ve sıra korunur (iki doküman yan yana okunur). Her dil kendi çiftinde denetlenir.</summary>
    [Theory]
    [InlineData("docs/FEATURES.md", "docs/WHY.md")]
    [InlineData("docs/FEATURES.tr.md", "docs/WHY.tr.md")]
    public void WHY_takes_its_items_from_FEATURES_with_the_same_titles_in_the_same_order(string features, string why)
    {
        List<string> all = Sections(features);
        List<string> gains = Sections(why);
        Assert.NotEmpty(gains);
        Assert.All(gains, title => Assert.Contains(title, all));
        List<int> positions = gains.Select(title => all.IndexOf(title)).ToList();
        Assert.Equal(positions.Order().ToList(), positions);
    }

    /// <summary>WHY her başlıkta özelliğin adını ve tireden sonra kazanımını söyler — "neden kullanayım" diye okuyan,
    /// cevabı başlıkları tarayarak alır — ve kazanım kısa kalır ki başlık taranabilir olsun. FEATURES başlıkları yalındır:
    /// kazanım WHY'ın işidir, aynı metin iki yerde yaşamaz.</summary>
    [Theory]
    [InlineData("docs/FEATURES.md", "docs/WHY.md")]
    [InlineData("docs/FEATURES.tr.md", "docs/WHY.tr.md")]
    public void WHY_titles_carry_a_short_gain_after_the_dash_and_FEATURES_titles_do_not(string features, string why)
    {
        Assert.All(Headings(features), heading => Assert.DoesNotContain(GainSeparator, heading, StringComparison.Ordinal));
        Assert.All(Headings(why), heading =>
        {
            int at = heading.IndexOf(GainSeparator, StringComparison.Ordinal);
            Assert.True(at > 0, $"no gain after the dash: {heading}");
            int words = heading[(at + GainSeparator.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.InRange(words, 1, 6);
        });
    }

    [Theory, MemberData(nameof(LinkedDocuments))]
    public void Relative_links_point_to_files_that_exist(string document)
    {
        string folder = Path.GetDirectoryName(Path.Combine(RepoPaths.RepoRoot, document))!;
        var broken = Regex.Matches(WithoutCodeBlocks(Read(document)), @"\]\((?<target>[^)\s#]+)(#[^)]*)?\)")
            .Select(m => m.Groups["target"].Value)
            .Where(target => !target.Contains("://", StringComparison.Ordinal) && !target.StartsWith("mailto:", StringComparison.Ordinal))
            .Where(target => !File.Exists(Path.Combine(folder, target)) && !Directory.Exists(Path.Combine(folder, target)))
            .ToList();
        Assert.Empty(broken);
    }

    private static TheoryData<string> Data(IEnumerable<string> documents)
    {
        var data = new TheoryData<string>();
        foreach (string document in documents) data.Add(document);
        return data;
    }

    private static string Read(string document) =>
        File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, document.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>WHY başlığında özelliğin adını kazanımdan ayıran tire: <c>Builds only what changed — less waiting</c>.</summary>
    internal const string GainSeparator = " — ";

    /// <summary>Kod bloğu dışındaki <c>## </c> ve <c>### </c> başlıkları, işaretsiz, olduğu gibi.</summary>
    private static List<string> Headings(string document) =>
        WithoutCodeBlocks(Read(document)).Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf(' ') + 1)..].Trim())
            .ToList();

    /// <summary>Bölüm adı = başlığın kazanım tiresinden önceki kısmı; FEATURES başlığında tire yoktur, WHY'da vardır.</summary>
    private static List<string> Sections(string document) =>
        Headings(document).Select(TitleOf).ToList();

    private static string TitleOf(string heading)
    {
        int at = heading.IndexOf(GainSeparator, StringComparison.Ordinal);
        return at < 0 ? heading : heading[..at].Trim();
    }

    private static string WithoutCodeBlocks(string markdown) =>
        Regex.Replace(markdown, @"(?ms)^```.*?^```[ \t]*$", string.Empty);
}
