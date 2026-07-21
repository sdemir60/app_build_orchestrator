using System.IO;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T49] Uygulama XAML'lerinde ham renk literali kalmadığını pinler (token zorunluluğu). TEK istisna
/// <c>Resources/Tokens.xaml</c>'dir — renk literalleri oraya AİTTİR, bu yüzden listede yer almaz.
/// </summary>
public sealed class NoHardcodedColorTests
{
    private static readonly Regex HexLiteral = new(@"""#[0-9a-fA-F]{3,8}""", RegexOptions.Compiled);

    [Theory]
    [InlineData("MainWindow.xaml")]
    [InlineData("Console/ConsoleView.xaml")]
    [InlineData("Console/ConsoleHeader.xaml")]
    [InlineData("Controls/StickyLayerList.xaml")]
    [InlineData("Controls/LatestPill.xaml")]
    [InlineData("Graph/GraphView.xaml")]
    public void No_xaml_outside_the_token_dictionary_declares_a_raw_colour(string relative)
    {
        string path = Path.Combine(RepoPaths.RepoRoot, "src", "BuildOrchestrator.App", relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.DoesNotMatch(HexLiteral, File.ReadAllText(path));
    }
}
