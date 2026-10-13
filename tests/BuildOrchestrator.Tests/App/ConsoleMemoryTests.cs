using BuildOrchestrator.App.Console;

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
/// ister. Proje logu sayfası kendi listesini tutmaya devam eder (<see cref="ListBacklog"/>).</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ConsoleMemoryTests
{
    private static List<string> Lines(int count, int seed) =>
        [.. Enumerable.Range(0, count).Select(i => $"line {seed * count + i}: a narrative line long enough to carry some text")];

    [StaFact]
    public void The_narrative_document_keeps_no_undo_history_so_trimmed_text_is_released()
    {
        var store = new NarrativeStore();
        var view = new ConsoleView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(DsResources.NewHost(), view);
        view.ReplaceRunDocument(NarrativeSnapshots.Of(store)); // anlatı modu: takip açık, kırpma devrede

        for (int batch = 0; batch < 20; batch++) NarrativeSnapshots.Deliver(store, view, Lines(50, batch)); // 1000 satır

        var document = view.Document;
        Assert.True(document.LineCount <= ConsoleView.RenderSliceLines + 1,
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
        var store = new NarrativeStore();
        var view = new ConsoleView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(DsResources.NewHost(), view);
        view.ReplaceRunDocument(NarrativeSnapshots.Of(store));

        for (int batch = 0; batch < 20; batch++) NarrativeSnapshots.Deliver(store, view, Lines(50, batch));

        Assert.False(view.BacklogOwnsCopy);                           // KIRMIZI eski kodda: _backlogLines büyür
        Assert.Equal(1000, store.Count);
        Assert.True(view.Document.LineCount <= ConsoleView.RenderSliceLines + 1);
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

        Assert.Equal(ConsoleView.RenderSliceLines + 1, view.Document.LineCount);
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
}
