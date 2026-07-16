using BuildOrchestrator.Core.Processes;
using Xunit;

namespace BuildOrchestrator.Tests.Processes;

public class WindowsCommandLineTests
{
    [Fact] public void Plain_args_join() => Assert.Equal("a.exe b c", WindowsCommandLine.Build("a.exe", "b", "c"));
    [Fact] public void Space_arg_quoted() => Assert.Equal("\"c:\\my dir\\a.exe\" x", WindowsCommandLine.Build(@"c:\my dir\a.exe", "x"));
    [Fact] public void Trailing_backslash_doubled_inside_quotes() => Assert.Equal("a.exe \"c:\\dir with space\\\\\"", WindowsCommandLine.Build("a.exe", @"c:\dir with space\"));
    [Fact] public void Embedded_quote_escaped() => Assert.Equal("a.exe \"he said \\\"hi\\\"\"", WindowsCommandLine.Build("a.exe", "he said \"hi\""));
}
