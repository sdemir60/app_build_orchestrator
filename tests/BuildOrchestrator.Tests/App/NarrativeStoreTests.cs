using System.IO;
using System.Text;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.Core.Logs;
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
///
/// <para><b>[idle-memory · dalga 2 · spec §6.3] Spool:</b> koşu başlayınca store anlatıyı koşu klasöründeki dosyaya yayar
/// (<see cref="NarrativeStore.BeginSpool"/>); bellekte yalnız son <see cref="NarrativeStore.MemoryWindowLines"/> satır kalır, öncesi
/// dosyadan ofsetle okunur. Dosya açılamaz ya da yazılamazsa store bellekte devam eder ve TEK bir uyarı bırakır — spool bir
/// optimizasyondur, kaybı doğruluğu bozmaz. Spool açan her test klasörünü <see cref="SpoolDir"/> ile alır: dispose önce spool'u kapatır.</para>
/// </summary>
public class NarrativeStoreTests
{
    /// <summary><c>line from</c> … <c>line (to−1)</c>, '\n' sonekli — testlerin "line N" satırlarının beklenen dilimi.</summary>
    private static string Lines(int from, int to) =>
        string.Concat(Enumerable.Range(from, to - from).Select(k => $"line {k}\n"));

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
    /// ömrü boyunca. Yeni işlem diziyi yeniden büyütür; büyüme işlem sonu trimiyle ucuzdur.
    /// <para>[idle-memory · dalga 2 · I5 — GENİŞLETİLDİ] Spool'un ofset indeksi de bırakılır: o da satır başına 8 B'tır ve
    /// <c>List.Clear</c> onu da en büyük işlemin boyunda tutardı. Eski ad (<c>Clear_also_releases_the_line_array_of_the_largest_operation</c>)
    /// ve eski iddia — spool'suz 100 k satırdan sonra satır dizisi bırakılır — aynen ilk yarıdır; ikinci yarı aynı kuralı spool'lu
    /// koşunun ofset indeksine taşır.</para></summary>
    [Fact]
    public void Clear_also_releases_the_line_array_and_the_offset_index_of_the_largest_operation()
    {
        var store = new NarrativeStore();
        for (int i = 0; i < 100_000; i++) store.Append("x");

        store.Clear();

        Assert.Equal(0, store.Capacity); // KIRMIZI: bugün 131 072 — dizi en büyük işlemin boyunda kalır

        var spooled = new NarrativeStore();
        using var logs = new SpoolDir(spooled);
        Assert.True(spooled.BeginSpool(logs.FilePath));
        for (int i = 0; i < 100_000; i++) spooled.Append("x");

        spooled.Clear();

        Assert.Equal(0, spooled.OffsetCapacity); // KIRMIZI (dalga 2): List.Clear indeksi 131 072 girişte tutardı
        Assert.Equal(0, spooled.Capacity);
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
            Assert.Equal(Lines(from, n), store.Join(from, n));
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

    // ---------------------------------------------------------------- [idle-memory · dalga 2 · spec §6.3] spool

    /// <summary>Bellek penceresinin ötesindeki satırlar spool dosyasından okunur: <c>Count</c> toplam satırdır, bellekte yalnız pencere
    /// kalır (<c>CharCount</c> onu ölçer), eski satırlar diskten, yeniler bellekten gelir. Temizlik spool'u kapatır.</summary>
    [Fact]
    public void Lines_beyond_the_memory_window_are_read_back_from_the_spool()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        Assert.True(store.BeginSpool(logs.FilePath));
        for (int i = 0; i < NarrativeStore.MemoryWindowLines + 500; i++) store.Append("line " + i);

        Assert.Equal(NarrativeStore.MemoryWindowLines + 500, store.Count);
        Assert.True(store.CharCount <= NarrativeStore.MemoryWindowLines * "line 99999".Length); // bellekte yalnız pencere
        Assert.Equal("line 0\nline 1\n", store.Join(0, 2));                                 // diskten
        Assert.Equal(["line 0", "line 1"], store.Slice(0, 2));                               // diskten, satır satır
        Assert.Equal(["line 2499"], store.Slice(2499, 2500));                                // bellekten
        store.Clear();
        Assert.False(store.IsSpooling);
    }

    /// <summary>Spool açılmadan önce biriken satırlar (istek ve planlama satırları, <c>runStarted</c>'tan önce gelen ilk proje satırları)
    /// dosyaya ÖNCE yazılır, sonrakiler arkasından. Dosya BOM'suz UTF-8'dir ve her satır yalnız '\n' ile biter: ofsetler bayt sayar —
    /// bir BOM ya da <c>WriteLine</c>'ın "\r\n"'i her ofseti kaydırırdı. <see cref="NarrativeStore.Flush"/> tamponu diske indirir.</summary>
    [Fact]
    public void Lines_appended_before_the_spool_opens_are_written_first()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        store.Append("early");
        Assert.True(store.BeginSpool(logs.FilePath));
        store.Append("late");
        store.Flush();

        Assert.Equal("early\nlate\n", logs.ReadText());
    }

    /// <summary>[K1 · K2 · I6] Açılamayan spool (klasörü yok) store'u bellekte bırakır: tahliye olmaz, hiçbir satır kaybolmaz, uyarı TEK
    /// kezdir ve klasör YARATILMAZ — klasörün yokluğu "yazılamıyor" demektir; olmayan bir yolu yaratmak makineyi kirletirdi. Uyarı
    /// İngilizcedir ve istisnanın TÜR adını taşır, yerelleştirilmiş OS iletisini değil.</summary>
    [Fact]
    public void A_failed_spool_keeps_everything_in_memory_and_warns_once()
    {
        using var dir = new TempDir();
        var store = new NarrativeStore();
        string folder = Path.Combine(dir.Path, "missing");
        string path = Path.Combine(folder, RunLogPaths.NarrativeFileName);

        Assert.False(store.BeginSpool(path));
        for (int i = 0; i < NarrativeStore.MemoryWindowLines + 10; i++) store.Append("line " + i);

        Assert.False(store.IsSpooling);
        Assert.Equal("line 0\n", store.Join(0, 1)); // tahliye yok: en eski satır bellekte
        Assert.False(Directory.Exists(folder));      // klasör yaratılmadı
        Assert.Equal($"warning: console history could not be written to {path} — kept in memory (DirectoryNotFoundException)",
            store.TakeWarning());
        Assert.Null(store.TakeWarning()); // bir kez
    }

    /// <summary>[K3] Spool yalnız tam nitelikli bir yola açılır: göreli bir yol sürecin çalışma klasörüne yazardı (motorun boş bir
    /// <c>LogDirectory</c>'si dosya adının kendisine dönüşür). Açılamayan her spool gibi reddedilir: dosya yaratılmaz, tek uyarı.</summary>
    [Fact]
    public void A_relative_spool_path_is_refused()
    {
        var store = new NarrativeStore();
        string relative = "bo-relative-" + Guid.NewGuid().ToString("N") + ".log";
        try
        {
            Assert.False(store.BeginSpool(relative));
            Assert.False(store.IsSpooling);
            Assert.False(File.Exists(relative));
            Assert.Equal($"warning: console history could not be written to {relative} — kept in memory (ArgumentException)",
                store.TakeWarning());
        }
        finally
        {
            store.Clear();
            if (File.Exists(relative)) File.Delete(relative);
        }
    }

    /// <summary>[Q2 · B1] Çok satırlı girişler spool'da da belgenin gösterdiği satırlar olarak durur: yazım, ofset ve tahliye SAKLANAN
    /// parça başınadır — ham giriş yazılsaydı dosyaya '\r' girer, satır sayısı ile ofset sayısı ayrışır ve sonraki her okuma bir satır
    /// kayardı. Kâhin AvalonEdit'in belgesidir. Girişler pencereyi defalarca aşar (eskiler diskten okunur); çok baytlı UTF-8 karakterler
    /// ofsetlerin karakter değil BAYT saydığını sınar. Dosya store'un satırlarının '\n' sonekli birleşimidir — başka hiçbir şey.</summary>
    [Fact]
    public void Multi_line_appends_on_a_spool_read_back_as_the_lines_the_document_shows()
    {
        string[] entries =
        [
            "a\nb", "a\r\nb", "a\rb", "a\n", "a\r\n", "a\r", "\n", "\r\n\r\n", "a\n\rb", "a\r\r\nb",
            "[error] runFailed: first line\r\n   at Second.Line()\r\n", "çğıöşü — warning € kept", "astral \U0001D11E line", "",
        ];
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        Assert.True(store.BeginSpool(logs.FilePath));
        var appended = new StringBuilder();
        for (int round = 0; round < 300; round++)
        {
            foreach (string entry in entries)
            {
                store.Append(entry);
                appended.Append(entry).Append('\n'); // pompanın yaptığı gibi: her girişin ardına '\n'
            }
        }

        var shown = ConsoleDocument.LinesOf(new TextDocument(appended.ToString()));
        Assert.True(store.LinesInMemory < store.Count, "ön-koşul: pencere aşıldı, eski satırlar yalnız diskte");
        Assert.Equal(shown, store.Slice(0, store.Count));
        string expected = ConsoleLines.Join(shown, 0, shown.Count);
        Assert.Equal(expected, store.Join(0, store.Count));
        store.Flush();
        Assert.Equal(expected, logs.ReadText());
    }

    /// <summary>Tahliye sınırını kesen bir dilim: diskten gelen baş ile bellekten gelen kuyruk dikişsiz birleşir — tekrar yok, delik yok.</summary>
    [Fact]
    public void A_join_across_the_eviction_boundary_is_contiguous()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        Assert.True(store.BeginSpool(logs.FilePath));
        for (int i = 0; i < NarrativeStore.MemoryWindowLines + 500; i++) store.Append("line " + i);
        int boundary = store.Count - store.LinesInMemory; // bellekteki ilk satır
        Assert.True(boundary > 10, "ön-koşul: tahliye oldu");

        Assert.Equal(Lines(boundary - 10, boundary + 10), store.Join(boundary - 10, boundary + 10));
        Assert.Equal(Lines(boundary - 1, boundary + 1), store.Join(boundary - 1, boundary + 1));
        Assert.Equal([.. Enumerable.Range(boundary - 3, 6).Select(k => "line " + k)], store.Slice(boundary - 3, boundary + 3));
    }

    /// <summary>[K7 · K8 · I2] Tahliyeden SONRA yazım hatası: spool kapanır, yeni satırlar bellekte kalır ve tahliye durur; tahliye
    /// edilmiş satırlar hâlâ dosyadan okunur — her blok tahliyeden önce diske inmişti ve okuma <c>IsSpooling</c>'e değil tahliye sayısına
    /// bağlıdır. Hata dışarı sızmaz (<c>Append</c> IPC okuma döngüsünde koşar: fırlatsa motorun olayları sessizce kesilirdi). Uyarı TEK
    /// kezdir. Bölümün spool'u bir kez açılır: hatadan sonra yeniden açılış dosyayı kesmez.</summary>
    [Fact]
    public void A_write_failure_after_eviction_keeps_the_evicted_lines_readable_and_the_new_ones_in_memory()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        FailingSpoolStream? stream = null;
        store.OpenSpoolStream = path => stream = new FailingSpoolStream(NarrativeStore.OpenSpoolFile(path));
        Assert.True(store.BeginSpool(logs.FilePath));
        int before = NarrativeStore.MemoryWindowLines + 500;
        for (int i = 0; i < before; i++) store.Append("line " + i);
        int evicted = store.Count - store.LinesInMemory;
        Assert.True(evicted > 5, "ön-koşul: tahliye oldu");

        stream!.Fail = true; // disk doldu
        int total = before + NarrativeStore.MemoryWindowLines;
        for (int i = before; i < total; i++) store.Append("line " + i);

        Assert.False(store.IsSpooling);
        Assert.Equal(total, store.Count);
        Assert.Equal(evicted, store.Count - store.LinesInMemory);                // tahliye durdu: yeni satırlar bellekte
        Assert.Equal(Lines(0, 2), store.Join(0, 2));                           // tahliye edilenler diskten
        Assert.Equal(Lines(evicted - 5, evicted + 5), store.Join(evicted - 5, evicted + 5));
        Assert.Equal(Lines(total - 2, total), store.Join(total - 2, total));   // en yeniler bellekten
        Assert.Equal($"warning: console history could not be written to {logs.FilePath} — kept in memory (IOException)",
            store.TakeWarning());
        Assert.Null(store.TakeWarning());
        Assert.False(store.BeginSpool(logs.FilePath)); // bölümün spool'u bir kez açılır: dosya kesilmez
        Assert.Equal(Lines(0, 2), store.Join(0, 2));
    }

    /// <summary>[I3] Okunamayan geçmiş: dilim ya TAM aralıktır ya <c>""</c> — asla kısmi metin. Görünüm dikişini indekslerden hesaplar;
    /// eksik bir parça sonraki her satırı kaydırırdı. Sınırı kesen dilim de bütünüyle boş döner; bellekteki aralık etkilenmez. Okuma
    /// hatası TEK uyarı bırakır. Burada spool akışı diske hiç inmez (bellek akışı): dosya yoktur.</summary>
    [Fact]
    public void An_unreadable_spool_yields_no_partial_text_and_warns_once()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        store.OpenSpoolStream = _ => new MemoryStream(); // yazım başarılı, dosya hiç yok
        Assert.True(store.BeginSpool(logs.FilePath));
        for (int i = 0; i < NarrativeStore.MemoryWindowLines + 500; i++) store.Append("line " + i);
        int boundary = store.Count - store.LinesInMemory;
        Assert.True(boundary > 1, "ön-koşul: tahliye oldu");

        Assert.Equal("", store.Join(0, 2));                       // diskten okunamadı
        Assert.Equal("", store.Join(boundary - 1, boundary + 1)); // sınırı kesen dilim de kısmi değil
        Assert.Empty(store.Slice(0, 2));
        Assert.Equal(Lines(boundary, boundary + 2), store.Join(boundary, boundary + 2)); // bellek etkilenmez
        Assert.Equal($"warning: console history could not be read back from {logs.FilePath} (FileNotFoundException)",
            store.TakeWarning());
        Assert.Null(store.TakeWarning()); // tekrar eden okuma hatası yeniden uyarmaz
    }

    /// <summary>[idle-memory · dalga 2 · inceleme M4] Geri okuma kilit DIŞINDADIR; o arada yeni bir bölüm başlarsa (temizlik — yeni
    /// işlem) okumanın hatası ESKİ bölümündür ve yenisine uyarı olarak kaydedilmez: yeni bölümün konsoluna önceki koşunun dosyasından
    /// söz eden bir satır düşerdi. Okuma dikişi temizliği tam o arada yapar ve okumayı düşürür.</summary>
    [Fact]
    public void A_read_failure_that_outlives_its_section_warns_nothing_in_the_next_one()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        Assert.True(store.BeginSpool(logs.FilePath));
        for (int i = 0; i < NarrativeStore.MemoryWindowLines + 500; i++) store.Append("line " + i);
        store.OpenSpoolReader = _ =>
        {
            store.Clear(); // okuma sürerken yeni bölüm başladı
            throw new IOException("The device is not ready.");
        };

        Assert.Equal("", store.Join(0, 2));
        Assert.Null(store.TakeWarning()); // KIRMIZI: eski bölümün okuma hatası yeni bölüme kaydedilirdi
    }

    /// <summary>[I10] Bölümün spool'u açıkken ikinci açılış hiçbir şey yapmaz: <c>FileMode.Create</c> dosyayı keserdi — tahliye edilmiş
    /// geçmiş ve ofset indeksi boşa düşerdi. Başka bir yol da açılmaz.</summary>
    [Fact]
    public void Beginning_the_spool_again_is_a_no_op()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        Assert.True(store.BeginSpool(logs.FilePath));
        for (int i = 0; i < NarrativeStore.MemoryWindowLines + 500; i++) store.Append("line " + i);
        string other = Path.Combine(logs.Path, "other.log");

        Assert.True(store.BeginSpool(logs.FilePath)); // zaten yayılıyor
        Assert.True(store.BeginSpool(other));
        Assert.False(File.Exists(other));
        Assert.Equal(Lines(0, 2), store.Join(0, 2)); // dosya kesilmedi: tahliye edilen satırlar yerinde
        store.Flush();
        Assert.Equal(Lines(0, store.Count), logs.ReadText());
    }

    /// <summary>[spec §6.3] Temizlik (yeni işlem) spool'u kapatır ve dosyayı SİLMEZ: dosya koşu klasöründe kalır, klasörle birlikte
    /// budanır (ARCHITECTURE §8.5). Kapalıdır — paylaşımsız açılabilir; açık bir tutamaç klasörün budanmasını da engellerdi. Kuyruğu
    /// tamdır: kapatma önce boşaltır.</summary>
    [Fact]
    public void Clear_closes_the_spool_and_leaves_the_file_on_disk()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        Assert.True(store.BeginSpool(logs.FilePath));
        store.Append("a");
        store.Append("b");

        store.Clear();

        Assert.False(store.IsSpooling);
        Assert.Equal(0, store.Count);
        using (new FileStream(logs.FilePath, FileMode.Open, FileAccess.Read, FileShare.None)) { } // kapalı: başka tutamaç yok
        Assert.Equal("a\nb\n", logs.ReadText());
    }

    /// <summary>[idle-memory · dalga 2] Yırtılmazlık spool açıkken de geçerlidir: yazar (IPC thread'i) satır ekler, pencereyi aşan
    /// satırları tahliye eder ve dosyaya yazar; okuyucu (UI thread'i) bellekteki kuyruğu, diske inmiş başı ve ortayı ister. Okunan
    /// sayıya kadarki her dilim AYNI satırlardır. Kurulum yukarıdaki el sıkışmadır (TCS — uyku/sorgu yok, D8).</summary>
    [Fact]
    public async Task Appends_with_the_spool_open_never_tear_a_join_from_memory_or_disk()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        Assert.True(store.BeginSpool(logs.FilePath));
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
            int middle = n / 2;
            foreach (var (from, to) in new[] { (0, Math.Min(50, n)), (Math.Max(0, middle - 25), Math.Min(n, middle + 25)), (Math.Max(0, n - 50), n) })
                Assert.Equal(Lines(from, to), store.Join(from, to));
        }
        await writer;
        Assert.Equal(20000, store.Count);
        Assert.True(store.LinesInMemory <= NarrativeStore.MemoryWindowLines, $"{store.LinesInMemory} lines in memory");
        Assert.Equal(Lines(0, 20000), store.Join(0, 20000));
        Assert.Null(store.TakeWarning());
    }
}
