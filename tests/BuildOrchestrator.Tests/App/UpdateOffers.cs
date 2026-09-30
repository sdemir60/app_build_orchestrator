using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 8] Testlerin örnek güncelleme teklifinin TEK yeri. Uygulama artık teklifsiz açılır (hap yalnız
/// gerçek bir feed kaydı indirilince görünür); hapı, kartı ya da restart ekranını sınayan testler teklifi buradan alır.
/// İçerik prototipin örnek kaydıdır (<c>UPDATE_FEED</c>, BuildApp.jsx:1650-1658): boyut ve üç madde (1 Performance,
/// 2 Fixed), yazıldıkları sırayla — çizim sırası kartın işidir (<see cref="ReleaseNotes.KindOrder"/>).</summary>
internal static class UpdateOffers
{
    public static UpdateOffer Sample(string version = "9.9.0") => new(version, "18.4 MB",
        [ new(NoteKind.Performance, "Sync reads project files in parallel — about twice as fast on large solutions."),
          new(NoteKind.Fixed, "Copy log keeps its line breaks when pasted into Teams or Outlook."),
          new(NoteKind.Fixed, "A project renamed on disk is picked up by the next Sync.") ], MoreCount: 0);
}
