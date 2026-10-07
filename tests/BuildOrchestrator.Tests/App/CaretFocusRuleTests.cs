using System.IO;
using BuildOrchestrator.App.Controls;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// <b>İmleçler pencerenin ODAĞINI izler, klavyeyi değil.</b> İki imleç (konsol prompt'u, akışın aktif satırı) pencere
/// görünür ve aktifken kırpar; arkadayken, simge durumundayken ve tepsideyken durur (<see cref="CursorClockTests"/>,
/// <see cref="HiddenCursorClockTests"/>). Girdi yokluğu bir kapı DEĞİLDİR.
///
/// <para><b>Eski kural (perf B4 · karar 5, 2026-10-07 sabahı):</b> üçüncü bir kapı, Windows'un imleç zaman aşımı
/// (<c>SPI_GETCARETTIMEOUT</c>, 5 s) boyunca klavye/fare girdisi gelmeyince saat çiftini söküyordu; ölçülen gerekçesi ön
/// planda boşta 181 Mdöngü/s'in imleç saatlerinden gelmesi, imleçler durunca 17'ye inmesiydi. <b>Değişme gerekçesi
/// (kullanıcı kararı, 2026-10-07):</b> önde duran uygulamanın imleci yanıp sönmeli — durma yalnız odak başka yerdeyken.
/// Bedel bilerek kabul edildi: aktif pencere boştayken imleçler çalıştıkça render döngüsü ayakta kalır (~180 M/s);
/// pencere arkada ya da tepsideyken maliyet yine ~17'dir.</para>
///
/// <para>Bu guard, girdi kapısının geri sızmasını kaynak düzeyinde yakalar: pencerenin girdi akışı (<c>InputManager</c>
/// ön-işleme kancası) imleç saatine bağlanmaz ve saatin "girdi yok" diye bir kapısı yoktur.</para>
/// </summary>
public class CaretFocusRuleTests
{
    [Fact]
    public void The_carets_follow_the_window_focus_not_the_keyboard()
    {
        string mainWindow = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "MainWindow.xaml.cs"));
        string cursorClock = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "Controls", "CursorClock.cs"));

        Assert.DoesNotContain("PreProcessInput", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("InputIdle", cursorClock, StringComparison.Ordinal);
        Assert.Null(typeof(CursorClock).GetMethod("SetInputIdle",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public));
    }
}
