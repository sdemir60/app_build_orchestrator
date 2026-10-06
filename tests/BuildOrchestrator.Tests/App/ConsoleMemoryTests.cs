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
/// metin o an serbest kalır; anlatının tek kalıcı kopyası modelde (<c>RunViewModel</c>) ve geçmiş dilimi için görünümün
/// satır listesindedir.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ConsoleMemoryTests
{
    private static string Batch(int lines, int seed) =>
        string.Concat(Enumerable.Range(0, lines).Select(i => $"line {seed * lines + i}: a narrative line long enough to carry some text\n"));

    [StaFact]
    public void The_narrative_document_keeps_no_undo_history_so_trimmed_text_is_released()
    {
        var view = new ConsoleView { AnimationsEnabledProvider = () => false };
        var window = DsResources.Realize(DsResources.NewHost(), view);
        view.ReplaceRunDocument(""); // anlatı modu: takip açık, kırpma devrede

        for (int batch = 0; batch < 20; batch++) view.AppendNarrativeBatch(Batch(50, batch)); // 1000 satır

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

        view.ReplaceProjectDocument(lines);          // proje logu: baştan okunur, takip kapalı
        view.AppendBatch(Batch(50, 9));              // canlı satırlar da aynı belgeye akar

        var document = view.Document;
        Assert.Equal(0, document.UndoStack.SizeLimit);
        Assert.False(document.UndoStack.CanUndo);
        GC.KeepAlive(window);
    }
}
