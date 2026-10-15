using System.Globalization;
using System.Windows;
using BuildOrchestrator.App.Console;
using ICSharpCode.AvalonEdit.Document;
using static BuildOrchestrator.Tests.App.ConsoleDocument;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [perf G3 · ÖLÇÜLDÜ] Görünür bir Resolve koşusunun ardından boşta alınan yığın dökümünde canlı yönetilen verinin
/// yaklaşık yarısı konsol belgesinin ip (rope) düğümleri ve karakter dizileriydi — oysa belge takip açıkken son
/// <see cref="ConsoleView.RenderSliceLines"/> satıra kırpılır. Kırpılan metin belgeden çıkıyor ama ÖLMÜYORDU: AvalonEdit her
/// <c>Insert</c>/<c>Remove</c>'u geri-alma yığınına kaydeder ve kaldırılan metni ip dilimi olarak elinde tutar; sınırsız
/// geri-alma geçmişi koşunun tüm anlatısını belgenin arkasında canlı bırakıyordu.
///
/// <para><b>Kural:</b> konsol salt-okunurdur; belgeleri geri-alma geçmişi TUTMAZ (<c>UndoStack.SizeLimit = 0</c>). Kırpılan
/// metin o an serbest kalır.</para>
///
/// <para><b>[DEĞİŞEN KURAL — idle-memory dalga 1] Anlatının görünümde kopyası YOKTUR.</b> Eski iddia: "anlatının tek kalıcı
/// kopyası modelde (<c>RunViewModel</c>) ve geçmiş dilimi için görünümün satır listesindedir" — fiilen İKİ kopya: VM'in metin
/// tamponu ve görünümün satır listesi (belgeden kırpılan canlı satırlar da listeye eklenirdi). Değişme gerekçesi (ölçüldü,
/// boşta bellek analizi §3): 83 k satırlık bir koşudan sonra görünümün listesi tek başına 59 MB tutuyordu, VM tamponu ayrıca
/// 55 MB. Artık anlatının bellekteki TEK sahibi VM'in store'udur (<see cref="NarrativeStore"/>): görünüm geçmiş dilimini ondan
/// ister. [dalga 3 · karar 44] Proje logu sayfası logun tamamını belgesinde taşır, arkasında satır listesi tutmaz (eskiden logun
/// kopyası görünümün <see cref="ListBacklog"/>'undaydı, belge son 200 satırdı).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ConsoleMemoryTests
{
    /// <summary>Anlatı modunda, takip açık, BOŞ bir store'a bağlı gerçek bir görünüm — üretim düzeniyle (backlog store'dur): bu
    /// dosyadaki anlatı testlerinin ortak kurulumu (dört satır beş yerde yazılmıştı). Dönen pencere canlı tutulmalıdır (çağıran
    /// <c>GC.KeepAlive</c> der).</summary>
    private static (ConsoleView View, NarrativeStore Store, Window Host) NewNarrativeView()
    {
        var store = new NarrativeStore();
        var view = new ConsoleView { AnimationsEnabledProvider = () => false };
        var host = DsResources.Realize(DsResources.NewHost(), view);
        view.ReplaceRunDocument(NarrativeSnapshots.Of(store));
        return (view, store, host);
    }

    /// <summary>Anlatı satırları; <paramref name="padding"/> satırı o kadar karakter uzatır (satır başına sabit maliyeti metne
    /// göre küçültmek isteyen ölçümler için).</summary>
    private static List<string> Lines(int count, int seed, int padding = 0) =>
        [.. Enumerable.Range(0, count)
            .Select(i => $"line {seed * count + i}: a narrative line long enough to carry some text{new string('.', padding)}")];

    [StaFact]
    public void The_narrative_document_keeps_no_undo_history_so_trimmed_text_is_released()
    {
        var (view, store, window) = NewNarrativeView(); // anlatı modu: takip açık, kırpma devrede

        for (int batch = 0; batch < 20; batch++) NarrativeSnapshots.Deliver(store, view, Lines(50, batch)); // 1000 satır

        var document = view.Document;
        Assert.True(document.LineCount <= ConsoleView.SeededDocumentLines,
            $"ön-koşul: belge {document.LineCount} satır — kırpma çalışmadı (vakum)");
        Assert.Equal(0, document.UndoStack.SizeLimit);
        Assert.False(document.UndoStack.CanUndo, "kırpılan satırlar geri-alma geçmişinde canlı tutuluyor");
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_project_log_document_keeps_no_undo_history_either()
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(DsResources.NewHost(), view);
        var lines = Enumerable.Range(0, 400).Select(i => $"raw msbuild line {i}").ToList();

        view.ReplaceProjectDocument(lines);                     // proje logu: baştan okunur, takip kapalı
        view.AppendBatch(ConsoleLines.Join(Lines(50, 9), 0, 50)); // canlı satırlar da aynı belgeye akar

        var document = view.Document;
        Assert.Equal(0, document.UndoStack.SizeLimit);
        Assert.False(document.UndoStack.CanUndo);
        GC.KeepAlive(window);
    }

    /// <summary>[idle-memory · dalga 1 · spec §6.2] Canlı anlatı aktıkça görünüm kendi satır listesini BÜYÜTMEZ: belge render
    /// dilimine kırpılır, kırpılan satırlar zaten store'dadır ve tepeye kaydırınca geçmiş oradan gelir.</summary>
    [StaFact]
    public void The_narrative_view_keeps_no_copy_of_the_lines()
    {
        var (view, store, window) = NewNarrativeView();

        for (int batch = 0; batch < 20; batch++) NarrativeSnapshots.Deliver(store, view, Lines(50, batch));

        // Eski kodda bu iddia DERLENMEZDİ (kırmızısı derleme hatasıydı, başarısız bir assert değil): yüzey yoktu, çünkü görünüm
        // anlatıda da HER ZAMAN kendi listesini (_backlogLines) tutuyordu ve kırpılan canlı satırlar ona eklenirdi.
        Assert.False(view.BacklogOwnsCopy);
        Assert.Equal(1000, store.Count);
        Assert.True(view.Document.LineCount <= ConsoleView.SeededDocumentLines);
        view.PrependPreviousChunk();                                  // geçmiş store'dan gelir
        Assert.True(view.Document.LineCount > ConsoleView.RenderSliceLines);
        int shown = view.Document.LineCount - 1;                      // son satır: '\n' sonrası boş satır
        Assert.Equal(store.Join(store.Count - shown, store.Count), view.Document.Text); // store'un son dilimi, bitişik
        GC.KeepAlive(window);
    }

    /// <summary>[idle-memory · dalga 1 · spec §6.2] Tohumlama tam metni değil (store, sayı) snapshot'ını alır; belge store'un SON
    /// diliminden kurulur.</summary>
    [StaFact]
    public void Seeding_the_narrative_renders_the_last_slice_from_the_store()
    {
        var store = new NarrativeStore();
        for (int i = 0; i < 500; i++) store.Append($"line {i}");
        var view = new ConsoleView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(DsResources.NewHost(), view);

        view.ReplaceRunDocument(NarrativeSnapshots.Of(store));

        Assert.Equal(ConsoleView.SeededDocumentLines, view.Document.LineCount);
        Assert.StartsWith("line 300", view.Document.GetText(view.Document.GetLineByNumber(1)));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [idle-memory · dalga 1] Kabuk konsolu KURULUMDA VM'in anlatı store'una bağlar — belgeyi yeniden kurmadan. Bağ olmasaydı
    /// görünüm ilk tohumlamaya (mod geçişi, işlem temizliği, tepsiden dönüş) kadar kendi boş listesinde kalır ve o arada canlı
    /// akıştan kırpılan her satırı KOPYALARDI. Bağ bir yeniden kurulum sayılmaz: tepsiden dönüş ve temizlik testleri kurulum
    /// sayısını mutlak sayar (<c>RunDocumentReplacedCount</c>).
    /// </summary>
    [StaFact]
    public void The_shell_binds_the_console_to_the_narrative_store_without_rebuilding_the_document()
    {
        using var dir = new TempDir();
        var (window, vm) = MainWindowHost.NewRealized(dir);
        var console = window.Shell.ConsoleViewControl;
        Assert.Equal(0, vm.Narrative.Count); // ön-koşul: pompaya teslim edilmemiş açılış satırı yok (pompa bu fixture'da tick etmez)

        Assert.False(console.BacklogOwnsCopy);             // KIRMIZI bağ yokken: görünüm kendi (kopya sahibi) listesinde
        Assert.Equal(0, console.RunDocumentReplacedCount); // bağ belgeyi kurmadı

        // Canlı akış, üretim sırasıyla: satır önce modele (store), sonra pompanın batch'i belgeye (batch'i test verir).
        for (int i = 0; i < 300; i++)
        {
            MainWindowHost.LogLine(vm, "A", i + 1, $"line {i}");
            window.AppendConsoleBatch($"line {i}\n", window.ConsoleReseedGen);
        }
        Assert.DoesNotContain("line 0\n", console.Document.Text);  // ön-koşul: belge render dilimine kırpıldı

        console.PrependPreviousChunk();

        Assert.StartsWith("line 0\n", console.Document.Text);      // geçmiş store'dan, bitişik
        Assert.False(console.BacklogOwnsCopy);                     // kırpılan satırlar görünüme kopyalanmadı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [idle-memory · final review 92] Anlatı bağı yalnız BOŞ bir anlatı belgesine kurulur: bağ belgenin ilk satırını store'un 0.
    /// satırı sayar — store'un her satırı pompadan sırayla gelecektir. Store'un hiç görmediği satırları taşıyan bir belgeye kurulan
    /// bağ o satırları store indeksleriyle eşlerdi ve tepeye kaydırma yanlış dilimi getirirdi; bağ sessizce kurulmaz, hemen fırlatır.
    /// </summary>
    [StaFact]
    public void Binding_the_narrative_to_a_document_that_already_shows_lines_fails_fast()
    {
        var view = new ConsoleView();
        view.AppendBatch("a line the store never saw\n");

        Assert.Throws<InvalidOperationException>(() => view.BindNarrative(new NarrativeStore())); // KIRMIZI: bugün sessizce bağlar
    }

    /// <summary>[idle-memory · final review 92] Proje logu açıkken anlatı bağı kurulmaz: belge o sayfanındır (logun tamamı). Anlatıya
    /// dönüş belgeyi store'dan kurar (<c>ReplaceRunDocument</c>); bağ yalnız kabuğun açılışındaki boş anlatı belgesi içindir.</summary>
    [StaFact]
    public void Binding_the_narrative_while_a_project_log_is_shown_fails_fast()
    {
        var view = new ConsoleView();
        view.ReplaceProjectDocument([]); // proje logu sayfası — belgesi boş

        Assert.Throws<InvalidOperationException>(() => view.BindNarrative(new NarrativeStore())); // KIRMIZI: bugün sessizce bağlar
    }

    /// <summary>
    /// [idle-memory · final review m1] <b>Takip geri gelince anlatı belgesi render dilimine döner.</b> Kırpma yalnız takip açıkken
    /// yapılır — okuyucunun altından metin kaydırılmaz — ve kullanıcı yukarıdayken gelen satırlar ile tepeye kaydırınca yüklenen
    /// dilimler belgede birikir. Eskiden belge ancak SONRAKİ batch'te render dilimine dönüyordu; koşu bittiyse sonraki batch yoktur:
    /// o satırlar bir sonraki işleme dek AvalonEdit belgesinde (ip, satır ağacı, yükseklik ağacı) store'un ikinci, kısmi kopyası
    /// olarak kalıyordu (spec §5 bellek bütçesi). Kırpılan satırlar store'dadır: tepeye kaydırma onları yine bitişik getirir.
    /// </summary>
    [StaFact]
    public void The_narrative_document_returns_to_the_render_slice_when_following_resumes()
    {
        var (view, store, window) = NewNarrativeView();
        view.StickToBottom = false; // kullanıcı yukarıda okuyor: takip kapalı, belge kırpılmaz

        for (int batch = 0; batch < 20; batch++) NarrativeSnapshots.Deliver(store, view, Lines(50, batch)); // 1000 satır
        Assert.True(view.Document.LineCount > 1000, "ön-koşul: takip kapalıyken belge kırpılmadı");

        view.StickToBottom = true; // takip geri geldi (pill, bekleme sonu) — koşu bitmiş, ardından batch gelmeyecek

        Assert.True(view.Document.LineCount <= ConsoleView.SeededDocumentLines, // KIRMIZI: bugün belge 1001 satırda kalır
            $"the narrative document still holds {view.Document.LineCount} lines after following resumed");
        ScrollUpToTheTop(view); // tepeye kaydırma: önceki dilim store'dan
        int shown = view.Document.LineCount - 1;                                        // son satır: '\n' sonrası boş satır
        Assert.Equal(store.Join(store.Count - shown, store.Count), view.Document.Text); // store'un son dilimi, bitişik
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [idle-memory · group B re-review · ÇÖKME YOLU] <b>Store'u görünümün arkasından boşalan anlatı belgesi kırpılırken fırlatılmaz.</b>
    /// m1 kırpmayı bottom-anchor zamanlayıcılarına (kaydırma sonrası 5 sn'lik bekleme, pill atlama penceresi) bağladı; zamanlayıcı
    /// pencere GİZLİYKEN de ateşlenir. Gizli pencerede temizlik (<c>ConsoleCleared</c>) belgeye dokunmaz, yalnız "ekran bayat"
    /// bayrağını kaldırır — oysa VM anlatıyı çoktan boşaltmıştır: store <c>Count</c> 0, görünüm hâlâ eski <c>_loadedFrom</c>'unda. Kırpma
    /// "kaydedilmemiş kuyruğu" <c>excess − (Count − _loadedFrom)</c> ile hesaplıyordu; <c>Count &lt; _loadedFrom</c> iken kuyruğun
    /// başlangıç satırı sıfır ya da negatif çıkar ve <c>GetLineByNumber</c> <c>ArgumentOutOfRangeException</c> atar. Dispatcher'da
    /// <c>DispatcherUnhandledException</c> işleyicisi yoktur: süreç düşer.
    /// <para>Kural: backlog belgenin gerisinde kalabilir (bayat belge); kırpılan satırların hiçbiri backlog'da sayılmaz ve eski
    /// <c>LastLines</c> anlamı geçerlidir — hepsi kuyruktur (store bunu yok sayar). Belge render dilimine döner, hiçbir şey fırlamaz.</para>
    /// </summary>
    [StaFact]
    public void Trimming_a_narrative_document_whose_store_was_cleared_behind_the_view_does_not_throw()
    {
        var (view, store, window) = NewNarrativeView();
        for (int batch = 0; batch < 6; batch++) NarrativeSnapshots.Deliver(store, view, Lines(50, batch)); // 300 satır: kırpma koştu, _loadedFrom > 0
        view.StickToBottom = false; // kullanıcı yukarıda okuyor: takip kapalı, belge kırpılmaz
        for (int batch = 6; batch < 12; batch++) NarrativeSnapshots.Deliver(store, view, Lines(50, batch)); // belge render diliminin üstüne büyür
        Assert.True(view.Document.LineCount > ConsoleView.SeededDocumentLines, "ön-koşul: takip kapalıyken belge kırpılmadı");
        store.Clear(); // gizliyken yeni işlem: VM anlatıyı boşalttı, görünüm yeniden bağlanmadı (kabuk yalnız bayat bayrağını kaldırdı)

        var thrown = Record.Exception(() => view.StickToBottom = true); // bekleme doldu: takip geri geldi → kırpma sorulur

        Assert.Null(thrown); // KIRMIZI: bugün ArgumentOutOfRangeException (GetLineByNumber(<= 0))
        Assert.True(view.Document.LineCount <= ConsoleView.SeededDocumentLines,
            $"the stale document still holds {view.Document.LineCount} lines after following resumed");
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [idle-memory · final review m2 + 91] <b>Büyük bir anlatı batch'inin görünümde kopyası üretilmez.</b> Batch belgeye eklenip
    /// render dilimine kırpılırken görünümün ayırdığı bellek, belgenin AYNI ekleme ve kırpma için kendi ayırdığını yarım kopyadan
    /// fazla aşmaz: ekleme metni ipe (rope) bir kez yazar, kırpma kesilen metni okumaz — anlatıda backlog store'dur, kırpılan satır
    /// zaten oradadır (yalnız liste backlog'unun hiç kaydetmediği canlı satırlar okunur).
    /// <para>Ölçülen kusur (2026-10-14; 2.000 satır × 560 karakter, batch 2,24 MB UTF-16): görünüm 14,1 MB ayırıyordu (batch'in 6,3
    /// katı), belgenin kendi işi 5,5 MB'tı (2,5 katı). Fark ≈ 3,8 kopya: daktilo kaldırılırken ölü kalan iki kopya
    /// (<c>AppendNarrativeBatch</c>'in hiç kullanılmayan <c>body</c>/<c>prefix</c>'i) ve kırpılan metnin tamamı (store'un hiç
    /// okumadığı <c>GetText</c>). ~42 k karakterin üstündeki her batch'te bunlar LOH çöpüydü — dalga 1'in koşu sonunda topladığı
    /// sıcak yolun üstünde.</para>
    /// <para>Kâhin belgenin kendisidir: aynı durumdaki ikiz bir görünümün belgesinde aynı ekleme ve kırpma doğrudan yapılır
    /// (<see cref="InsertAndTrim"/>). Sayım <c>GC.GetAllocatedBytesForCurrentThread</c> iledir — test thread'inin ayırdığı bayt,
    /// süreç geneli değildir.</para>
    /// </summary>
    [StaFact]
    public void A_large_narrative_batch_is_appended_and_trimmed_without_a_copy_of_it()
    {
        var (view, store, window) = NarrativeViewAtTheSlice();
        var (twin, _, twinWindow) = NarrativeViewAtTheSlice();
        int from = store.Count;
        foreach (var line in Lines(2000, seed: 7, padding: 500)) store.Append(line); // üretim sırası: önce store, sonra batch
        string batch = store.Join(from, store.Count);

        long viewCost = PerfMeasure.AllocatedBy(() => view.AppendNarrativeBatch(batch));
        long documentCost = PerfMeasure.AllocatedBy(() => InsertAndTrim(twin.Document, batch));

        Assert.True(view.Document.LineCount <= ConsoleView.SeededDocumentLines, "ön-koşul: belge render dilimine kırpılmadı (vakum)");
        long copy = batch.Length * (long)sizeof(char);
        Assert.True(viewCost - documentCost < copy / 2, string.Create(CultureInfo.InvariantCulture, // KIRMIZI: bugün ≈ 3,8 kopya fazla
            $"the view allocated {viewCost} bytes for a {copy}-byte batch, the document's own insert and trim {documentCost}"));
        GC.KeepAlive(window);
        GC.KeepAlive(twinWindow);
    }

    /// <summary>
    /// [idle-memory · dalga 1 · docs review M1] <b>Bayat anlatı belgesinin kırpılması atılacak metni ÜRETMEZ.</b> Gizli pencerede yeni
    /// bir işlem anlatı store'unu boşaltır (<c>ConsoleCleared</c> belgeye dokunmaz); bottom-anchor zamanlayıcıları gizliyken de
    /// ateşlendiği için kırpma yine bu bayat belgede koşar. Store belgenin gerisindedir (<c>Count</c> 0, görünüm hâlâ eski
    /// <c>_loadedFrom</c>'unda): kırpılan satırların hiçbiri backlog'da sayılmaz, hepsi "kaydedilmemiş kuyruk" olur — ama store kuyruğu
    /// yok sayar (<see cref="IConsoleBacklog.KeepsTrimmedLines"/> false). Kuyruğu belgeden <c>GetText</c> + <c>Split</c> ile okumak
    /// atılacak bir metni boşuna iki kez kopyalamak olurdu; belge dönüşte zaten modelden yeniden kurulur.
    /// <para>Okuma kapısı (<c>TrimToRenderSlice</c>'taki <c>&amp;&amp; _backlog.KeepsTrimmedLines</c>) bu yolu tek başına korur: kapı
    /// kalkınca bu sınıfın diğer testleri yeşil kalıyordu, çünkü store kuyruğu yok sayar ve kırpmanın sonucu, belge ve fırlatma
    /// davranışı AYNI kalır — fark yalnız ayrılan bellektir. Bu yüzden pin tahsisi sayar.</para>
    /// <para>Kâhin ikiz görünümdür: aynı satırlar, aynı kırpma, ama store'u DOLU — backlog kırpılanları zaten sayar, kuyruk boştur,
    /// okuma kapısına hiç girilmez. Bayat görünümün tahsisi ikizinkinden kırpılan metnin bir kopyasının yarısından fazla büyük
    /// olamaz. Sayım <c>GC.GetAllocatedBytesForCurrentThread</c> iledir — test thread'inin ayırdığı bayt, süreç geneli değildir.</para>
    /// <para>Ölçülen kusur (kapı geçici kaldırıldı, 2026-10-14; 1000 uzun satırlık kırpma, kırpılan metin 757.202 bayt): bayat görünüm
    /// 2.315.848 bayt ayırdı, aynı kırpma dolu store'da 4.864 — fark ≈ 3 kopya (kapısız kod kuyruğu <c>GetText</c> + <c>Split</c> ile
    /// okur ve sonucunu store'a verir, store atar). Kapı yerindeyken ikisi aynı işi yapar; eşik yarım kopyadır.</para>
    /// </summary>
    [StaFact]
    public void Trimming_a_stale_narrative_document_does_not_read_the_trimmed_text()
    {
        var (stale, staleStore, staleWindow) = NarrativeViewReadingAboveTheSlice();
        var (twin, _, twinWindow) = NarrativeViewReadingAboveTheSlice(); // ikiz: store'u dolu kalır
        Assert.True(stale.Document.LineCount > 1000, "ön-koşul: takip kapalıyken belge kırpılmadı");
        int textBefore = stale.Document.TextLength;
        staleStore.Clear(); // gizliyken yeni işlem: VM anlatıyı boşalttı, görünüm yeniden bağlanmadı (kabuk yalnız bayat bayrağını kaldırdı)

        long twinCost = PerfMeasure.AllocatedBy(() => twin.StickToBottom = true);   // takip geri geldi → kırpma sorulur; kuyruk boş, okuma yok
        long staleCost = PerfMeasure.AllocatedBy(() => stale.StickToBottom = true); // aynı kırpma; kuyruk kırpılanların TAMAMI, okuma kapısı kapalı

        Assert.True(stale.Document.LineCount <= ConsoleView.SeededDocumentLines, "ön-koşul: bayat belge render dilimine kırpılmadı (vakum)");
        Assert.Equal(twin.Document.TextLength, stale.Document.TextLength); // ikisi de aynı kırpmayı yaptı
        long copy = (textBefore - stale.Document.TextLength) * (long)sizeof(char); // kırpılan metnin UTF-16 bedeli
        Assert.True(staleCost - twinCost < copy / 2, string.Create(CultureInfo.InvariantCulture, // KIRMIZI: kapı kalkınca kırpılan metin okunur
            $"the stale trim allocated {staleCost} bytes, the same trim over a full store {twinCost}, for {copy} bytes of trimmed text"));
        GC.KeepAlive(staleWindow);
        GC.KeepAlive(twinWindow);
    }

    /// <summary>Anlatı modunda, takip açık ve belgesi render diliminde duran bir görünüm — üretim düzeniyle: backlog store'dur,
    /// satırlar önce store'a sonra batch'le gelir. Kırpma ve ekleme yolu ölçümden önce bir kez koşmuştur.</summary>
    private static (ConsoleView View, NarrativeStore Store, Window Host) NarrativeViewAtTheSlice()
    {
        var (view, store, host) = NewNarrativeView();
        for (int batch = 0; batch < 8; batch++) NarrativeSnapshots.Deliver(store, view, Lines(50, batch)); // 400 satır: kırpma koştu
        return (view, store, host);
    }

    /// <summary>Anlatı modunda, belgesi render diliminin ÜSTÜNE büyümüş bir görünüm: kullanıcı yukarıda okurken (takip kapalı, kırpma
    /// bekler) 1000 uzun satır gelmiş. Takibin geri gelmesi (<c>StickToBottom = true</c>) kırpmayı tek hamlede yaptırır.</summary>
    private static (ConsoleView View, NarrativeStore Store, Window Host) NarrativeViewReadingAboveTheSlice()
    {
        var (view, store, host) = NarrativeViewAtTheSlice();
        view.StickToBottom = false; // kullanıcı yukarıda okuyor: takip kapalı, belge kırpılmaz
        for (int batch = 8; batch < 28; batch++) NarrativeSnapshots.Deliver(store, view, Lines(50, batch, padding: 400)); // 1000 uzun satır
        return (view, store, host);
    }

    /// <summary>Belgenin bir batch için KENDİ işi — ölçümün kâhini: metni sona ekler, render diliminin üstündeki satırları tek
    /// <c>Remove</c>'la siler.</summary>
    private static void InsertAndTrim(TextDocument document, string batch)
    {
        document.BeginUpdate();
        document.Insert(document.TextLength, batch);
        var lastAbove = document.GetLineByNumber(document.LineCount - ConsoleView.RenderSliceLines);
        document.Remove(0, lastAbove.EndOffset + lastAbove.DelimiterLength);
        document.EndUpdate();
    }
}
