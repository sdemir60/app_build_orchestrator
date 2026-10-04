using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// [okunan dosya kanıtı] <see cref="CycleReadFiles"/>: bir döngü üyesi tur sonunda bir kardeşin HANGİ
/// dosyalarıyla yargılanır. Kesin bilgi varsa (derleyici kardeşin bilinen kopyalarından birini okudu) yalnız o
/// dosya; şüphenin her biçiminde kardeşin TÜM bilinen dosyaları — eski kural. Saf fonksiyon.
/// </summary>
public class CycleReadFilesTests
{
    private const string Own = @"D:\repo\B\bin\Debug\B.dll";
    private const string Shared = @"C:\OSYS\Client\Bin\B.dll";
    private static readonly string[] Known = [Shared, Own];

    [Fact]
    public void a_reader_whose_compiler_read_the_shared_copy_is_judged_on_that_copy_alone()
    {
        Assert.Equal([Shared], CycleReadFiles.Tracked(Known, [@"C:\lib\Other.dll", Shared], succeeded: true));
    }

    [Fact] // Windows yolu: büyük/küçük harf farkı eşleşmeyi bozmaz; dönen yol bilinen dosyanın yazımıdır.
    public void the_match_ignores_case_and_answers_with_the_known_path()
    {
        Assert.Equal([Shared], CycleReadFiles.Tracked(Known, [@"c:\osys\client\bin\b.dll"], succeeded: true));
    }

    [Fact]
    public void a_failed_reader_is_judged_on_every_copy()
    {
        Assert.Equal(Known, CycleReadFiles.Tracked(Known, [Shared], succeeded: false));
    }

    [Fact] // Derleyici koşmadıysa ya da satırı okunamadıysa hangi dosyanın okunduğu bilinmez.
    public void without_compiler_references_every_copy_counts()
    {
        Assert.Equal(Known, CycleReadFiles.Tracked(Known, null, succeeded: true));
        Assert.Equal(Known, CycleReadFiles.Tracked(Known, [], succeeded: true));
    }

    [Fact] // Derleyici kardeşin adını hiç okumadıysa referans bulunamamıştır: dosya sonradan gelebilir.
    public void a_compiler_that_did_not_read_the_sibling_leaves_every_copy_counting()
    {
        Assert.Equal(Known, CycleReadFiles.Tracked(Known, [@"C:\lib\Other.dll"], succeeded: true));
    }

    [Fact] // Okunan dosya kardeşin adını taşıyor ama bilinen kopyalardan değil: turun onu yazıp yazmadığı bilinmez.
    public void a_reference_outside_the_known_copies_leaves_every_copy_counting()
    {
        Assert.Equal(Known, CycleReadFiles.Tracked(Known, [@"X:\elsewhere\B.dll"], succeeded: true));
    }

    /// <summary>"Taşındı"nın TEK tanımı (tur 1 kararı ve tur sonu bayatlığı aynı ilkeyi okur): kayıtlı dosya yoksa ya da özeti
    /// (büyük/küçük harf DAHİL) farklıysa taşınmıştır. Eskiden <c>CycleMemberNeed</c>'deydi; yuvası, hangi dosyaların
    /// yargılanacağını söyleyen <see cref="CycleReadFiles.Tracked"/>'ın yanıdır.</summary>
    [Fact]
    public void moved_files_lists_only_recorded_files_that_differ_or_vanished()
    {
        const string b1 = @"X:\bin\B1.dll", b2 = @"X:\bin\B2.dll", b3 = @"X:\bin\B3.dll", b4 = @"X:\bin\B4.dll";
        var seen = new Dictionary<string, string> { [b1] = "h1", [b2] = "h2", [b3] = "h3", [b4] = "h4" };
        var now = new Dictionary<string, string>
        {
            [b1] = "h1",                       // aynı
            [b2] = "moved",                    // farklı
            [b4] = "H4",                       // yalnız harf farkı: Ordinal ⇒ farklı
            [@"X:\bin\B9.dll"] = "unrelated",  // kayıtta yok ⇒ yok sayılır
        };

        var moved = CycleReadFiles.MovedFiles(seen, now).Order(StringComparer.Ordinal);

        Assert.Equal(new[] { b2, b3, b4 }, moved);
    }
}
