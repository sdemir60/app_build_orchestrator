using System.IO;
using BuildOrchestrator.Core.Paths;
using Xunit;

namespace BuildOrchestrator.Tests.Paths;

/// <summary>
/// [PERF Faz C · son toparlama B1 · madde 1] <see cref="LinkSafeTree"/>: bağlantıyı izlemeyen özyinelemeli silmenin TEK
/// gezinmesi. İki çağıranın politikaları kendi testlerinde pinli (Clean: <c>CleanWorkspaceServiceTests</c>, koşu logu
/// saklaması: <c>RunLogRetentionTests</c>); burada gezinmenin kendisi sınanır — özellikle KÖKÜN bir bağlantı olması — ve
/// iki politikanın dayandığı sözleşme (sıkı kip fırlatır, toleranslı kip numaralandırma hatasını yutar ama çağıranın
/// delegelerinin hatasını yutmaz). Bağlantı gerçek bir junction'la kurulur (<see cref="TestJunction"/>): kurulamazsa test
/// atlanır. Fixture kurduğu junction'ları <see cref="Dispose"/>'da <see cref="TestJunction.Remove"/> ile kaldırır:
/// <c>Directory.Delete(recursive)</c> içinde junction olan klasörde bu makinede fırlatır.
/// </summary>
public sealed class LinkSafeTreeTests : IDisposable
{
    private const string NoJunction = "junctions cannot be created here (mklink /J failed)";

    private readonly string _root = Directory.CreateTempSubdirectory("bo-linksafe-").FullName;
    private readonly List<string> _links = [];

    public void Dispose()
    {
        foreach (string link in _links) TestJunction.Remove(link);
        try { Directory.Delete(_root, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* temizlik iddia taşımaz */ }
    }

    private bool TryLink(string link, string target)
    {
        _links.Add(link); // kurulamasa da kayıt zararsız: Remove var olmayan bağlantıyı sessizce geçer
        return TestJunction.TryCreate(link, target);
    }

    /// <summary>Silinmemesi gereken dosya taşıyan, silinecek ağacın DIŞINDA bir bağlantı hedefi.</summary>
    private string MakeTarget()
    {
        string precious = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "target")).FullName, "precious.txt");
        File.WriteAllText(precious, "must survive");
        return precious;
    }

    [SkippableFact]
    public void A_root_that_is_a_link_is_removed_as_the_link_and_its_target_is_never_entered()
    {
        string precious = MakeTarget();
        string link = Path.Combine(_root, "link");
        Skip.IfNot(TryLink(link, Path.GetDirectoryName(precious)!), NoJunction);

        foreach (bool tolerate in new[] { false, true }) // kök kontrolü her iki kipte de aynı
        {
            var deleted = new List<string>();
            var removed = new List<string>();

            LinkSafeTree.Delete(link, deleted.Add, removed.Add, tolerate);

            Assert.Empty(deleted);                                  // hiçbir dosyaya dokunulmadı: içine inilmedi
            Assert.Equal([link], removed);                          // yalnız bağlantının KENDİSİ kaldırıldı
            Assert.True(File.Exists(precious));
        }
    }

    [SkippableFact]
    public void A_link_inside_a_real_tree_is_removed_as_the_link_while_the_tree_goes_with_it()
    {
        string precious = MakeTarget();
        string tree = Directory.CreateDirectory(Path.Combine(_root, "tree")).FullName;
        string sub = Directory.CreateDirectory(Path.Combine(tree, "sub")).FullName;
        File.WriteAllText(Path.Combine(tree, "a.txt"), "a");
        File.WriteAllText(Path.Combine(sub, "b.txt"), "b");
        Skip.IfNot(TryLink(Path.Combine(sub, "linked"), Path.GetDirectoryName(precious)!), NoJunction);

        // Sıkı politika (koşu logu saklamasının ki): BCL'in Directory.Delete(recursive)'i bu ağaçta fırlatırdı.
        LinkSafeTree.Delete(tree, File.Delete, d => Directory.Delete(d, recursive: false));

        Assert.False(Directory.Exists(tree));                       // gerçek ağaç, içindeki bağlantıyla birlikte tamamen gitti
        Assert.True(File.Exists(precious));                         // hedefe inilmedi
    }

    [Fact]
    public void A_missing_root_throws_in_the_strict_mode_and_is_carried_past_in_the_tolerant_mode()
    {
        string missing = Path.Combine(_root, "missing");

        // Sıkı: kaybolan klasör fırlatır — RunLogRetention.Prune bunu "zaten gitmiş" sayar (DirectoryNotFoundException).
        Assert.Throws<DirectoryNotFoundException>(() => LinkSafeTree.Delete(missing, File.Delete, _ => { }));

        // Toleranslı: numaralandırma hatası yutulur, akış durmaz; çağıranın kendi politikası (Clean: yut) sonucu belirler.
        var removed = new List<string>();
        LinkSafeTree.Delete(missing, File.Delete, removed.Add, tolerateListingErrors: true);
        Assert.Equal([missing], removed);
    }

    [Fact]
    public void The_tolerant_mode_covers_only_the_listing_never_the_errors_of_the_callers_delegates()
    {
        string tree = Directory.CreateDirectory(Path.Combine(_root, "tree")).FullName;
        File.WriteAllText(Path.Combine(tree, "locked.txt"), "x");

        // Dosya başı hata politikası ÇAĞIRANINDIR (Clean: yutar ve sayar): yardımcı onu sessizce yutmaz.
        Assert.Throws<IOException>(() =>
            LinkSafeTree.Delete(tree, _ => throw new IOException("locked"), _ => { }, tolerateListingErrors: true));
    }
}
