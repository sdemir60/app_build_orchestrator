using System.Runtime.InteropServices;
using System.Windows;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/3b/Ek A #3] Copy-log: satırlar '\n' ile panoya; <see cref="ClipboardRetry"/> CLIPBRD_E_CANT_OPEN
/// (dotnet/wpf#9901) kilidinde yeniden dener; <see cref="CopyLogFeedback"/> başarıda 1400ms ✓ + "Copied".
/// Retry ve feedback SAF/enjekte-saatli test edilir; buton görsel toggle'ı [StaFact].
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class CopyLogTests
{
    private const int ClipboardCantOpen = unchecked((int)0x800401D0); // CLIPBRD_E_CANT_OPEN

    // ---------------------------------------------------------------- ClipboardRetry (retry sarmalayıcı)

    [Fact]
    public void TrySet_retries_on_clipboard_lock_then_succeeds()
    {
        int calls = 0, waits = 0;
        bool ok = ClipboardRetry.TrySet(
            set: () =>
            {
                calls++;
                if (calls <= 2) throw new COMException("clipboard locked", ClipboardCantOpen);
                // 3. denemede başarılı
            },
            attempts: 10,
            wait: _ => waits++);

        Assert.True(ok);
        Assert.Equal(3, calls);  // 2 fail + 1 success
        Assert.Equal(2, waits);  // her fail'den sonra bir bekleme
    }

    [Fact]
    public void TrySet_returns_false_when_the_clipboard_stays_locked_and_does_not_throw()
    {
        int calls = 0;
        bool ok = ClipboardRetry.TrySet(
            set: () => { calls++; throw new COMException("locked", ClipboardCantOpen); },
            attempts: 5,
            wait: _ => { });

        Assert.False(ok);       // sessizce başarısız — UI çökmez
        Assert.Equal(5, calls); // tam attempts kadar denendi
    }

    [Fact]
    public void TrySet_does_not_retry_a_non_clipboard_exception()
    {
        int calls = 0;
        Assert.Throws<ArgumentException>(() =>
            ClipboardRetry.TrySet(set: () => { calls++; throw new ArgumentException("bug"); }, attempts: 5));
        Assert.Equal(1, calls); // pano-kilit sınıfı DIŞI istisna retry EDİLMEZ, yayılır
    }

    // ---------------------------------------------------------------- CopyLogFeedback (1400ms ✓ enjekte-saat)

    [Fact]
    public void CopyLogFeedback_stays_copied_for_1400ms_then_reverts()
    {
        var fb = new CopyLogFeedback();
        fb.MarkCopied(TimeSpan.Zero);

        Assert.True(fb.Copied);
        Assert.False(fb.ShouldRevert(TimeSpan.FromMilliseconds(1399)));
        Assert.True(fb.ShouldRevert(TimeSpan.FromMilliseconds(1400)));

        fb.Revert();
        Assert.False(fb.Copied);
    }

    // ---------------------------------------------------------------- buton görsel toggle ([StaFact])

    [StaFact]
    public void CopyLog_button_visible_only_in_project_log_mode_with_lines()
    {
        var header = new ConsoleHeader();

        header.ShowNarrative(12);
        Assert.Equal(Visibility.Collapsed, header.CopyLogButton.Visibility);

        header.ShowProjectLog("OSYS.Base", ProjectRowState.Succeeded, hasDepIssue: false, lineCount: 0);
        Assert.Equal(Visibility.Collapsed, header.CopyLogButton.Visibility); // log yok → copy yok

        header.ShowProjectLog("OSYS.Sales.Core", ProjectRowState.Started, hasDepIssue: false, lineCount: 42);
        Assert.Equal(Visibility.Visible, header.CopyLogButton.Visibility);
    }

    [StaFact]
    public void CopyLog_joins_lines_and_toggles_to_check_and_Copied_on_success()
    {
        var header = new ConsoleHeader();
        header.ShowProjectLog("A", ProjectRowState.Started, false, 5);
        Assert.False(header.IsShowingCopied);

        string? captured = null;
        header.LogTextProvider = () => "line1\nline2";
        header.ClipboardWriter = t => { captured = t; return true; };

        header.CopyLog();

        Assert.Equal("line1\nline2", captured);            // satırlar '\n' ile
        Assert.True(header.IsShowingCopied);               // ✓ görseli
        Assert.Equal("Copied", header.CopyLogButton.ToolTip);

        header.ShowNarrative(0);                            // mod değişimi görseli sıfırlar
        Assert.False(header.IsShowingCopied);
    }

    [StaFact]
    public void CopyLog_failed_clipboard_does_not_enter_copied_state()
    {
        var header = new ConsoleHeader();
        header.ShowProjectLog("A", ProjectRowState.Started, false, 5);
        header.LogTextProvider = () => "x";
        header.ClipboardWriter = _ => false; // kalıcı kilit

        header.CopyLog();

        Assert.False(header.IsShowingCopied); // başarısız kopya ✓ durumuna GEÇMEZ
        Assert.Equal("Copy log", header.CopyLogButton.ToolTip);
    }
}
