using BuildOrchestrator.Core.Diagnostics;

namespace BuildOrchestrator.Tests.Diagnostics;

/// <summary>
/// [son temizlik · madde 13 · O-2] Tanı satırının tek güvenli yazımı (<see cref="DiagnosticLine.Write"/>). Kanalın patlaması iki kullanıcısının
/// pinlerinde sınanır — motorun bellek satırı (<c>MemoryLineTests.Report_swallows_a_failing_sink_because_a_diagnostic_must_never_break_the_work</c>)
/// ve koordinatörün iki uyarısı (<c>RunCoordinatorMemoryTests.A_console_that_fails_while_the_trim_failure_is_reported_never_breaks_the_work</c>,
/// <c>RunCoordinatorTests.A_console_that_fails_while_a_read_failure_is_reported_never_breaks_the_answer</c>); burada yalnız primitifin kendi
/// sözleşmesi durur: kurulan satır kanala gider, satırı kuramayan okuma da yutulur.
/// </summary>
public class DiagnosticLineTests
{
    [Fact]
    public void The_built_line_reaches_the_sink()
    {
        var lines = new List<string>();

        DiagnosticLine.Write(lines.Add, () => "warning: something");

        Assert.Equal(["warning: something"], lines);
    }

    /// <summary>Satırı kuran okuma patlarsa (bellek satırında süreç sorgusu reddedildi) hata yutulur ve kanala hiçbir şey yazılmaz — eskiden
    /// bellek satırının kendi yakalamasındaydı ve hiç pinlenmemişti.</summary>
    [Fact]
    public void A_line_that_cannot_be_built_is_swallowed_and_nothing_is_written()
    {
        var lines = new List<string>();

        DiagnosticLine.Write(lines.Add, () => throw new InvalidOperationException("access denied"));

        Assert.Empty(lines);
    }
}
