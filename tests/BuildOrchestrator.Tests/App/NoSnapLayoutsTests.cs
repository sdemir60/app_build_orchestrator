using System.IO;
using System.Text.RegularExpressions;
using IoFile = System.IO.File;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Win11 <b>Snap Layouts</b> uçbirimi bu uygulamada <b>YOKTUR</b> — maximize butonu üzerinde açılan yerleştirme
/// paneli istenmez.
///
/// <para><b>Bu dosya, KURAL DEĞİŞTİĞİ İÇİN yeniden yazılmıştır</b> (CLAUDE.md: davranış değişince onu pinleyen
/// test sessizce silinmez, YENİ kuralı pinleyecek şekilde yeniden yazılır). Yerine geçtiği testler
/// <c>SnapLayoutHitTestTests</c> ve <c>SnapLayoutHookWndProcTests</c> idi ve TERSİNİ iddia ediyorlardı:</para>
/// <list type="bullet">
///   <item><b>Eski iddia [T62]:</b> pencere <c>WM_NCHITTEST</c>'te maximize butonunun üstündeyken
///   <c>HTMAXBUTTON</c> (9) DÖNDÜRMELİ — <c>WindowChrome</c> bunu kendiliğinden vermez (dotnet/wpf#4825) ve
///   Windows snap uçbirimini YALNIZ bu yanıtla açar. Buna bağlı olarak hit-test aritmetiği (<c>SnapLayout</c>)
///   ve mesaj pompası hook'u (<c>SnapLayoutHook</c>: elle hover, <c>WM_NCLBUTTONDOWN/UP</c> çiftinden toggle,
///   stale-press temizliği) test ediliyordu.</item>
///   <item><b>Değişme gerekçesi:</b> uçbirim İSTENMİYOR. Ölçülen tek anahtar: Windows panelini yalnız o
///   hit-test yanıtına bağlar, "panel kapalı ama hit-test dursun" diye bir ara nokta YOKTUR — dolayısıyla
///   kararı geri almak <c>HTMAXBUTTON</c> yolunun tamamını (ve onun bedeli olan non-client kablajı) sökmek
///   demektir.</item>
///   <item><b>Yeni kural:</b> App kaynak ağacında <c>HTMAXBUTTON</c>/<c>WM_NCHITTEST</c> yolu ve Snap Layouts
///   dosyaları BULUNMAZ; maximize butonu sıradan bir WPF butonudur (tıklama <c>Click</c>'ten, hover şablonun
///   <c>IsMouseOver</c> trigger'ından). Davranış tarafı
///   <see cref="StartupWindowStateTests.Invoking_the_max_button_restores_the_maximized_window"/>'da.</item>
/// </list>
/// </summary>
public sealed class NoSnapLayoutsTests
{
    /// <summary>Snap Layouts'u AYAKTA TUTAN tek şey bu yanıttır: uçbirim <c>WM_NCHITTEST</c> → <c>HTMAXBUTTON</c>
    /// zincirine bağlıdır. Kimlik olarak sabit adı da (<c>HTMAXBUTTON</c>) aranır — çıplak <c>9</c> sayısı
    /// ayırt edici olmazdı.</summary>
    private static readonly Regex SnapLayoutSurface =
        new(@"\bHTMAXBUTTON\b|\bWM_NCHITTEST\b|\bSnapLayoutHook\b|\bSnapLayout\b", RegexOptions.Compiled);

    [Fact]
    public void No_app_source_answers_the_maximize_button_hit_test()
        => Assert.Empty(SourceGuard.ScanApp("*.cs", SnapLayoutSurface));

    [Fact]
    public void No_app_markup_mentions_the_snap_layout_wiring()
        => Assert.Empty(SourceGuard.ScanApp("*.xaml", SnapLayoutSurface));

    [Fact]
    public void The_snap_layout_files_are_gone()
    {
        var scanned = SourceGuard.ScannedAppFiles("*.cs");

        Assert.DoesNotContain(Path.Combine("Shell", "SnapLayout.cs"), scanned);
        Assert.DoesNotContain(Path.Combine("Shell", "SnapLayoutHook.cs"), scanned);
    }

    [Fact]
    public void The_guard_actually_scans_the_files_it_claims_to()
    {
        // Tarama boş dönerse yukarıdakiler SESSİZCE yeşil kalırdı (yol/filtre bozulması) — NoHardcodedColorTests
        // ile aynı vakum kapatma deseni. Hook'un yaşadığı iki dosya taramaya girmeli.
        var cs = SourceGuard.ScannedAppFiles("*.cs");
        var xaml = SourceGuard.ScannedAppFiles("*.xaml");

        Assert.Contains("MainWindow.xaml.cs", cs);
        Assert.Contains(Path.Combine("Shell", "Win32.cs"), cs);
        Assert.Contains("MainWindow.xaml", xaml);
    }

    /// <summary>Hook kalkınca hover'ı SÜREN tek şey şablonun kendi trigger'ıdır; T62'de bu görsel elle
    /// (<c>SetMaxButtonHover</c> → <c>MaxButton.Background</c>) sürülüyordu çünkü non-client bölgede WPF'in
    /// <c>IsMouseOver</c>'ı hiç tetiklenmiyordu. Trigger düşerse üç caption butonu da hover'sız kalır.</summary>
    [Fact]
    public void The_caption_button_template_still_carries_its_own_hover_trigger()
    {
        string markup = IoFile.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "MainWindow.xaml"));

        Assert.Contains("<Trigger Property=\"IsMouseOver\" Value=\"True\">", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("SetMaxButtonHover", markup, StringComparison.Ordinal);
    }
}
