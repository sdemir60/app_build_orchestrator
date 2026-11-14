using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.Core.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [files-in-use · Task 1] Bir projenin post-build kopya komutunun MSBuild çıktısından okunması. İki kaynak vardır: normal
/// verbosity'de hedefin başlığı altında (<c>PostBuildEvent:</c>) yankılanan komut satırı ve — yankı yokken de duran — hatanın
/// <c>MSB3073</c> satırı. Satır biçimleri gerçek bir koşu logundan alınıp nötr adlara (Acme) çevrildi; Türkçe kuyruk
/// MSBuild'in yerelleştirilmiş metnidir ve okunmaz. Kapsam yalnız <c>cmd</c>'nin <c>copy</c>/<c>xcopy</c>'sidir: MSBuild'in
/// Copy görevi (<c>Copying file from …</c>) komut sayılmaz.
/// </summary>
[Trait("Category", "MsBuild")]
public sealed class CopyCommandsTests
{
    private const string Source = @"C:\src\Acme.Types\bin\Release\Acme.Types.*";
    private const string Client = @"C:\Acme\Client\bin\";
    private const string Server = @"C:\Acme\Server\bin\";
    private const string Project = @"C:\src\Acme.Types\Acme.Types.csproj";
    private const string Targets =
        @"C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\Microsoft.Common.CurrentVersion.targets(6155,5)";

    [Fact]
    public void An_echoed_copy_command_names_its_sources_and_its_destination()
    {
        var command = CopyCommandReader.FromEcho($"  copy /y \"{Source}\" \"{Client}\"");

        Assert.NotNull(command);
        Assert.Equal([Source], command.Sources);
        Assert.Equal(Client, command.Destination);
    }

    [Theory]
    [InlineData(@"xcopy /y /i ""C:\a\x.dll"" ""C:\b\""")]
    [InlineData(@"COPY /Y ""C:\a\x.dll"" ""C:\b\""")]
    [InlineData(@"copy /y C:\a\x.dll C:\b\")]
    [InlineData("\tcopy  /y  \"C:\\a\\x.dll\"   \"C:\\b\\\"")]
    public void Xcopy_unquoted_paths_upper_case_and_extra_whitespace_read_the_same(string line)
    {
        var command = CopyCommandReader.FromEcho(line);

        Assert.NotNull(command);
        Assert.Equal([@"C:\a\x.dll"], command.Sources);
        Assert.Equal(@"C:\b\", command.Destination);
    }

    [Fact]
    public void A_unc_destination_is_read()
    {
        var command = CopyCommandReader.FromEcho($"copy /y \"{Source}\" \"\\\\files\\share\\bin\\\"");

        Assert.NotNull(command);
        Assert.Equal(@"\\files\share\bin\", command.Destination);
    }

    [Fact]
    public void A_relative_or_variable_path_is_kept_as_written()
    {
        // Göreli yol ve %DEĞİŞKEN% çözülmez: o, hedefleri çözenin işidir (çalışma klasörünü o bilir).
        var command = CopyCommandReader.FromEcho(@"copy /y ""bin\Release\x.dll"" ""%ACME_BIN%\""");

        Assert.NotNull(command);
        Assert.Equal([@"bin\Release\x.dll"], command.Sources);
        Assert.Equal(@"%ACME_BIN%\", command.Destination);
    }

    [Theory]
    [InlineData("Copying file from \"C:\\a\\x.dll\" to \"C:\\b\\x.dll\".")]
    [InlineData("  Dosya \"C:\\a\\x.dll\" konumundan \"C:\\b\\x.dll\" konumuna kopyalanıyor.")]
    [InlineData("        1 file(s) copied.")]
    [InlineData(@"  C:\src\Acme.Types\bin\Release\Acme.Types.dll")]
    [InlineData("copy /y \"C:\\a\\x.dll\"+\"C:\\a\\y.dll\" \"C:\\b\\z.dll\"")]
    [InlineData("copy /y \"C:\\a\\x.dll\"")]
    [InlineData("robocopy \"C:\\a\" \"C:\\b\" x.dll")]
    [InlineData("copyright 2026 Acme")]
    [InlineData("PostBuildEvent:")]
    [InlineData("")]
    public void A_line_that_is_not_a_copy_command_reads_as_none(string line)
    {
        Assert.Null(CopyCommandReader.FromEcho(line));
    }

    [Fact]
    public void An_english_msb3073_line_names_its_command()
    {
        var command = CopyCommandReader.FromMsb3073(
            $"{Targets}: error MSB3073: The command \"copy /y \"{Source}\" \"{Client}\"\" exited with code 1. [{Project}]");

        Assert.NotNull(command);
        Assert.Equal([Source], command.Sources);
        Assert.Equal(Client, command.Destination);
    }

    [Fact]
    public void A_localized_msb3073_wrapper_and_tail_are_not_read()
    {
        var command = CopyCommandReader.FromMsb3073(
            $"{Targets}: error MSB3073: \"copy /y \"{Source}\" \"{Client}\"\" komutundan 1 koduyla çıkıldı. [{Project}]");

        Assert.NotNull(command);
        Assert.Equal([Source], command.Sources);
        Assert.Equal(Client, command.Destination);
    }

    [Fact]
    public void Each_line_of_a_two_line_event_reads_on_its_own()
    {
        // İki satırlı post-build olayı MSB3073'ü iki satıra böler: ilk satır sarmalayıcının açılış tırnağını, son satır kapanışını
        // ve yerelleştirilmiş kuyruğu taşır.
        var first = CopyCommandReader.FromMsb3073(
            $"{Targets}: error MSB3073: \"copy /y \"{Source}\" \"{Server}\" [{Project}]");
        var last = CopyCommandReader.FromMsb3073(
            $"{Targets}: error MSB3073: copy /y \"{Source}\" \"{Client}\"\" komutundan 1 koduyla çıkıldı. [{Project}]");

        Assert.Equal(Server, first?.Destination);
        Assert.Equal(Client, last?.Destination);
    }

    [Theory]
    [InlineData("error MSB3073: The command \"copy /y C:\\a\\x.dll C:\\b\\\" exited with code 1.")]
    [InlineData("error MSB3021: Unable to copy file \"C:\\a\\x.dll\" to \"C:\\b\\x.dll\".")]
    [InlineData("error MSB3073: The command \"del /q \"C:\\copy\\x.dll\" \"C:\\b\\y.dll\"\" exited with code 1.")]
    [InlineData("error MSB3073: The command \"copy /y \"C:\\a\\x.dll\"\" exited with code 1.")]
    public void An_msb3073_line_without_a_quoted_copy_reads_as_none(string line)
    {
        Assert.Null(CopyCommandReader.FromMsb3073(line));
    }

    [Fact]
    public void The_collector_keeps_each_command_once_in_the_order_it_was_seen()
    {
        var collector = new CopyCommandCollector();
        foreach (string line in new[]
        {
            "PostBuildEvent:",
            $"  copy /y \"{Source}\" \"{Server}\"",
            $"  copy /y \"{Source}\" \"{Client}\"",
            "  " + Source.Replace("*", "dll"),
            "  The process cannot access the file because it is being used by another process.",
            "          2 file(s) copied.",
            $"{Targets}: error MSB3073: \"copy /y \"{Source}\" \"{Server}\" [{Project}]",
            $"{Targets}: error MSB3073: copy /y \"{Source}\" \"{Client.ToUpperInvariant()}\"\" komutundan 1 koduyla çıkıldı. [{Project}]",
        })
        {
            collector.Observe(line);
        }

        Assert.Equal([Server, Client], collector.Commands.Select(c => c.Destination));
    }

    [Fact]
    public void The_collector_takes_lines_from_two_pumps_at_once()
    {
        var collector = new CopyCommandCollector();

        Parallel.For(0, 200, i => collector.Observe($"copy /y \"C:\\a\\x{i % 50}.dll\" \"{Client}\""));

        Assert.Equal(50, collector.Commands.Count);
    }
}
