using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Services.Updates;

namespace BuildOrchestrator.Tests.App;

/// <summary>[motor · Task 8] Testlerin örnek güncelleme teklifinin TEK yeri. Uygulama artık teklifsiz açılır (hap yalnız
/// gerçek bir feed kaydı indirilince görünür); hapı, kartı ya da restart ekranını sınayan testler teklifi buradan alır.
/// İçerik prototipin örnek kaydıdır (<c>UPDATE_FEED</c>, BuildApp.jsx:1650-1658): boyut ve üç madde (1 Performance,
/// 2 Fixed), yazıldıkları sırayla — çizim sırası kartın işidir (<see cref="ReleaseNotes.KindOrder"/>).</summary>
internal static class UpdateOffers
{
    /// <summary>Örnek paketin bayt sayısı ve <see cref="UpdateOffer.FormatSize"/>'ın onu yazdığı metin — bu çiftin TEK yeri.
    /// Boyut testleri (<c>UpdateOfferTests</c>, <c>UpdateServiceTests</c>) ve <see cref="Sample"/> literali buradan alır;
    /// biçimleme kuralını <c>UpdateOfferTests.The_size_is_megabytes_with_one_decimal</c> bu çift üzerinden pinler.</summary>
    public const long SampleBytes = 19_293_798;
    public const string SampleSize = "18.4 MB";

    /// <summary>Tek bölümlü, tek maddeli (<c>Fixed</c>) geçerli feed notu — sürüm başlığı ve gövde biçiminin TEK yeri.</summary>
    public static UpdateCandidate Candidate(string version) =>
        new(version, SampleBytes, $"## [{version}] - 2026-10-01\n### Fixed\n- D\n");

    public static UpdateOffer Sample(string version = "9.9.0") => new(version, SampleSize,
        [ new(NoteKind.Performance, "Sync reads project files in parallel — about twice as fast on large solutions."),
          new(NoteKind.Fixed, "Copy log keeps its line breaks when pasted into Teams or Outlook."),
          new(NoteKind.Fixed, "A project renamed on disk is picked up by the next Sync.") ], MoreCount: 0);
}
