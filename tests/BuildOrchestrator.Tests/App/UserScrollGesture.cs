using System.Windows;
using System.Windows.Controls.Primitives;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Testlerin "kullanıcı GERÇEKTEN kaydırdı" jestini üretme yolu — üretimin dinlediği ham girdi kanalının
/// (<c>UserScrollSignal</c>) ta kendisi tetiklenir, panelin iç durumuna elle dokunulmaz.
///
/// <para>Kaydırma çubuğu olayı seçildi çünkü sentetik olarak güvenilir biçimde yükseltilebilen tek kanal odur:
/// tekerlek olayı bir <c>MouseDevice</c>, klavye bir <c>KeyboardDevice</c> ister ve ikisi de headless bir
/// STA host'ta kırılgandır. Üçü de AYNI <c>NotifyUserScroll</c>'a bağlandığı için hangi kanalın sürüldüğü
/// sözleşme açısından fark etmez.</para>
/// </summary>
internal static class UserScrollGesture
{
    /// <summary>Kullanıcının kaydırma çubuğunu sürüklemesi — <paramref name="host"/> üretimde bu olayı dinler.</summary>
    public static void Raise(FrameworkElement host) =>
        host.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, 0) { RoutedEvent = ScrollBar.ScrollEvent });
}
