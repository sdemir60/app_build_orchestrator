using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>Kullanıcı dokümanları iki dillidir: İngilizce esas, Türkçe aynası aynı klasörde <c>.tr.md</c> ekiyle durur
/// (README, docs/FEATURES, docs/WHY). Ayna sessizce geri kalmasın diye bölüm sayıları eşit tutulur; WHY, FEATURES'ın
/// kazanım olan maddelerini aynı başlık ve aynı sırayla taşır; dokümanlar arası göreli linkler var olan dosyaya gider.
/// <para>Bölüm = kod bloğu dışındaki <c>## </c> satırı. FEATURES ve WHY'da her bölüm bir maddedir.</para></summary>
public class DocumentationTests
{
    public static TheoryData<string, string> MirroredPairs => new()
    {
        { "README.md", "README.tr.md" },
        { "docs/FEATURES.md", "docs/FEATURES.tr.md" },
        { "docs/WHY.md", "docs/WHY.tr.md" },
    };

    /// <summary>Göreli linkleri denetlenen dokümanlar: iki dilli çiftler + yalnız İngilizce kılavuz.</summary>
    public static TheoryData<string> LinkedDocuments => new()
    {
        "README.md", "README.tr.md",
        "docs/FEATURES.md", "docs/FEATURES.tr.md",
        "docs/WHY.md", "docs/WHY.tr.md",
        "docs/USER-GUIDE.md",
    };

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

    /// <summary>WHY, FEATURES'ın alt kümesidir: her WHY maddesi FEATURES'ta aynı başlıkla vardır ve sıra korunur
    /// (iki doküman yan yana okunur). Her dil kendi çiftinde denetlenir.</summary>
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

    private static string Read(string document) =>
        File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, document.Replace('/', Path.DirectorySeparatorChar)));

    private static List<string> Sections(string document) =>
        WithoutCodeBlocks(Read(document)).Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim())
            .ToList();

    private static string WithoutCodeBlocks(string markdown) =>
        Regex.Replace(markdown, @"(?ms)^```.*?^```[ \t]*$", string.Empty);
}
