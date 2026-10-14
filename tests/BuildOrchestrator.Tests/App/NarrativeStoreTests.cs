using BuildOrchestrator.App.Console;
using ICSharpCode.AvalonEdit.Document;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 1 · spec §6.2] <see cref="NarrativeStore"/>: koşu anlatısının bellekteki TEK sahibi (saf, WPF'siz).
/// VM satırı ekler (IPC thread'i), konsol görünümü dilim ister (UI thread'i); görünüm kendi kopyasını tutmaz.
///
/// <para><b>Bir giriş = belgede bir satır.</b> Görünümün chunk loader'ı belgenin ilk satırını store indeksiyle izler ve
/// kırpılan satır sayısını (belge satırı) o indekse ekler; ikisi ayrışırsa yukarı kaydırma yanlış dilimi getirir (tekrar ya
/// da delik). Bu yüzden çok satırlı bir ekleme (çok satırlı hata mesajı, git stderr) belgenin göreceği satırlara bölünür —
/// AvalonEdit'in ayraç kuralıyla. Kuralın kâhini AvalonEdit'in kendi belgesidir.</para>
/// </summary>
public class NarrativeStoreTests
{
    [Fact]
    public void Append_count_and_join_follow_the_console_line_contract()
    {
        var store = new NarrativeStore();
        store.Append("a"); store.Append("b"); store.Append("c");
        Assert.Equal(3, store.Count);
        Assert.Equal(3, store.CharCount);
        Assert.Equal("b\nc\n", store.Join(1, 3));
        Assert.Equal("", store.Join(3, 3));
        Assert.Equal(["a", "b"], store.Slice(0, 2));
    }

    [Fact]
    public void Clear_empties_everything()
    {
        var store = new NarrativeStore();
        store.Append("a");
        store.Clear();
        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.CharCount);
        Assert.Equal("", store.Join(0, 1));
    }

    /// <summary>[idle-memory · final review m3] Temizlik satır dizisini de bırakır: <c>List.Clear</c> diziyi en büyük işlemin
    /// boyunda tutar — 100 k satırlık bir koşudan sonra 8 B/satır (≈ 1 MB, LOH'ta), daha küçük bir işlem onu silse bile uygulama
    /// ömrü boyunca. Yeni işlem diziyi yeniden büyütür; büyüme işlem sonu trimiyle ucuzdur.</summary>
    [Fact]
    public void Clear_also_releases_the_line_array_of_the_largest_operation()
    {
        var store = new NarrativeStore();
        for (int i = 0; i < 100_000; i++) store.Append("x");

        store.Clear();

        Assert.Equal(0, store.Capacity); // KIRMIZI: bugün 131 072 — dizi en büyük işlemin boyunda kalır
    }

    [Fact]
    public void Trimmed_lines_are_not_stored_twice()
    {
        var store = new NarrativeStore();
        store.Append("a");
        store.AcceptTrimmed(["a"]);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void A_list_backlog_grows_with_trimmed_live_lines()
    {
        var list = new ListBacklog(["disk1", "disk2"]);
        list.AcceptTrimmed(["live1"]);
        Assert.Equal(3, list.Count);
        Assert.Equal("disk2\nlive1\n", list.Join(1, 3));
    }

    /// <summary>Okuyucu (UI thread'i) yazar (IPC thread'i) eklerken dilim ister. Store yalnız büyür: okunan sayıya kadarki dilim,
    /// yazar o sırada ne yaparsa yapsın, AYNI satırlardır — sırayla ve eksiksiz. Yırtık bir okuma (yarım liste, kayan indeks)
    /// metni bozar ya da fırlatır.
    /// <para>[idle-memory · final review 93 — DEĞİŞEN KURULUM] Okumalar yazımla örtüşür: yazar ilk eklemesinden sonra bir
    /// <c>TaskCompletionSource</c> ile haber verir, okuyucu onu bekler ve yazar bitene dek okur. Eski kurulum sabit 200 okuma
    /// yapıyordu ve yazar daha başlamadan bitebilirdi — o zaman boş bir store'u okuyup eşzamanlılık hiç sınanmadan yeşil kalırdı.
    /// Bekleme bir uyku/sorgu değildir (D8) ve tek çekirdekte de biter: okuyucu döngüsü yazarın tamamlanmasına bağlıdır.</para>
    /// </summary>
    [Fact]
    public async Task Appends_from_another_thread_never_tear_a_join()
    {
        var store = new NarrativeStore();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writer = Task.Run(() =>
        {
            for (int i = 0; i < 20000; i++)
            {
                store.Append("line " + i);
                if (i == 0) started.SetResult(); // okuyucu yazar BAŞLADIKTAN sonra okur
            }
        });

        await started.Task;
        while (!writer.IsCompleted)
        {
            int n = store.Count;
            int from = Math.Max(0, n - 50);
            Assert.Equal(string.Concat(Enumerable.Range(from, n - from).Select(k => $"line {k}\n")), store.Join(from, n));
        }
        await writer;
        Assert.Equal(20000, store.Count);
    }

    /// <summary>
    /// Çok satırlı bir ekleme, belgenin onu gösterdiği satırlar olarak saklanır. Kâhin AvalonEdit'in belgesidir: pompa her
    /// satırın ardına '\n' koyar (<see cref="ConsoleBatcher"/>), yani belge eklemeyi <c>metin + "\n"</c> olarak görür —
    /// <c>"\r\n"</c> TEK ayraçtır, tek başına <c>'\r'</c> ve <c>'\n'</c> birer ayraçtır; sondaki tek <c>'\r'</c> pompanın
    /// <c>'\n'</c>'iyle birleşip tek ayraç olur. Komşu satırlar eklemeden etkilenmez.
    /// </summary>
    [Theory]
    [InlineData("a\nb")]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    [InlineData("a\n")]
    [InlineData("a\r\n")]
    [InlineData("a\r")]
    [InlineData("\n")]
    [InlineData("\r\n\r\n")]
    [InlineData("a\n\rb")]
    [InlineData("a\r\r\nb")]
    [InlineData("[error] runFailed: first line\r\n   at Second.Line()\r\n")]
    public void A_multi_line_append_is_stored_as_the_lines_the_document_shows(string text)
    {
        var store = new NarrativeStore();
        store.Append("before");
        store.Append(text);
        store.Append("after");

        var document = new TextDocument("before\n" + text + "\n" + "after\n");
        var shown = ConsoleDocument.LinesOf(document);

        Assert.Equal(shown, store.Slice(0, store.Count));
        Assert.Equal(shown.Sum(l => l.Length), store.CharCount);
    }

    [Fact]
    public void A_crlf_is_one_separator_and_leaves_no_empty_line_between()
    {
        var store = new NarrativeStore();
        store.Append("x\r\ny");
        Assert.Equal(["x", "y"], store.Slice(0, store.Count));
        Assert.Equal("x\ny\n", store.Join(0, store.Count));
    }

    /// <summary>Şema <c>text</c>'i zorunlu kılmaz; boş bir olay metni null bağlanabilir. Eski tampon onu sessizce boş satır yapıyordu
    /// (<c>StringBuilder.Append(null)</c>) ve pompa da öyle basar — IPC thread'inde fırlatmak koşunun olay akışını keserdi.</summary>
    [Fact]
    public void A_null_line_is_stored_as_an_empty_line()
    {
        var store = new NarrativeStore();
        store.Append(null);
        Assert.Equal(1, store.Count);
        Assert.Equal([""], store.Slice(0, 1));
        Assert.Equal("\n", store.Join(0, 1));
    }
}
