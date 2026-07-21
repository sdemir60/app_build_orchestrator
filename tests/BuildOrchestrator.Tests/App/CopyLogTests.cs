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

    [Fact]
    public void TrySet_does_not_retry_or_silently_swallow_a_non_lock_com_exception()
    {
        // [M-6] Kilit-DIŞI bir COMException (ör. E_FAIL) retry EDİLMEMELİ ve N deneme sonunda sessizce
        // yutulup false DÖNMEMELİ — olduğu gibi yayılmalı (aksi halde gerçek bir hata gizlenir).
        const int EFail = unchecked((int)0x80004005);
        int calls = 0;
        var ex = Assert.Throws<COMException>(() =>
            ClipboardRetry.TrySet(
                set: () => { calls++; throw new COMException("some other COM failure", EFail); },
                attempts: 5,
                wait: _ => { }));

        Assert.Equal(EFail, ex.ErrorCode);
        Assert.Equal(1, calls); // ilk denemede yayıldı — retry YOK, yutma YOK
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
    public void CopyLog_button_appears_when_lines_stream_in_after_an_empty_project_log()
    {
        // [M-3] Seçim anında log boş → buton gizli; ~200ms sayaç tazelemesi (SetLineCount) satır gelince
        // görünürlüğü YENİDEN değerlendirir → buton belirir (yalnız ShowProjectLog anında bir kez değil).
        var header = new ConsoleHeader();
        header.ShowProjectLog("OSYS.Server.Api", ProjectRowState.Started, hasDepIssue: false, lineCount: 0);
        Assert.Equal(Visibility.Collapsed, header.CopyLogButton.Visibility);

        header.SetLineCount(7); // build başladı, satırlar akmaya başladı
        Assert.Equal(Visibility.Visible, header.CopyLogButton.Visibility);

        // Narrative modda sayaç tazelense de copy butonu gizli kalır (mod duyarlı).
        header.ShowNarrative(0);
        header.SetLineCount(120);
        Assert.Equal(Visibility.Collapsed, header.CopyLogButton.Visibility);
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
