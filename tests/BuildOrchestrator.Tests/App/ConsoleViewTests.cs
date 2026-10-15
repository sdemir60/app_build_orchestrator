using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using BuildOrchestrator.App.Console;
using static BuildOrchestrator.Tests.App.ConsoleDocument;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T56/A13.2] ConsoleView: AvalonEdit tabanlı, salt-okunur, batch-append konsol control'ü. Bu iterasyonda
/// YALNIZ batching + append iskeleti test edilir — colorizer/typewriter/cascade/trim/pill It-4'tür (YAGNI).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ConsoleViewTests
{
    [StaFact]
    public void AppendBatch_two_batches_append_in_order_with_correct_line_count()
    {
        var view = new ConsoleView();

        view.AppendBatch("line1\nline2\n");
        view.AppendBatch("line3\n");

        Assert.Equal("line1\nline2\nline3\n", view.Document.Text);
        // AvalonEdit: sondaki '\n' bir sonraki (boş) satırı başlatır -> 4 satır (line1/line2/line3/boş).
        Assert.Equal(4, view.Document.LineCount);
    }

    [StaFact]
    public void AppendBatch_never_replaces_prior_content_single_insert_only()
    {
        var view = new ConsoleView();
        view.AppendBatch("a\n");
        view.AppendBatch("b\n");
        view.AppendBatch("c\n");

        Assert.Equal("a\nb\nc\n", view.Document.Text);
    }

    [StaFact]
    public void Editor_is_read_only_no_wrap_and_uses_embedded_console_font()
    {
        var view = new ConsoleView();
        // [T56/3a] Content artık editör+overlay Grid'i; editöre public Editor erişimcisinden ulaşılır.
        var editor = view.Editor;

        Assert.True(editor.IsReadOnly);
        Assert.False(editor.WordWrap);
        // FontFamily.Source, pack URI baseUri + "./#Aile Adı" ctor'unda tam olarak ikinci argümanı döner.
        Assert.Equal("./#Geist Mono Console", editor.FontFamily.Source);
    }

    [StaFact]
    public void StickToBottom_defaults_to_true()
    {
        var view = new ConsoleView();
        Assert.True(view.StickToBottom);
    }

    [StaFact]
    public void Document_can_be_swapped_for_a_different_TextDocument()
    {
        var view = new ConsoleView();
        var swapped = new ICSharpCode.AvalonEdit.Document.TextDocument("swapped content");

        view.Document = swapped;

        Assert.Same(swapped, view.Document);
        Assert.Equal("swapped content", view.Editor.Document.Text);
    }

    // ---------------------------------------------------------------- [3b] render dilimi (son 200 satır)

    [StaFact]
    public void AppendBatch_caps_the_document_at_the_render_slice_last_lines()
    {
        var view = new ConsoleView();
        for (int i = 0; i < 250; i++) view.AppendBatch($"line{i}\n");

        // Belge son ~200 satıra kırpıldı (baştakiler düştü) — hacim/performans (Ek A #16).
        Assert.True(view.Document.LineCount <= ConsoleView.SeededDocumentLines,
            $"belge satır sayısı ({view.Document.LineCount}) render dilimini aşmamalı");
        Assert.Contains("line249", view.Document.Text); // en yeni korunur
        Assert.DoesNotContain("line0\n", view.Document.Text); // en eski kırpıldı
    }

    /// <summary>
    /// [tail-trim tek kural · dalga 3 · karar 44-45] <b>Proje sayfasında canlı satırlar hiç kırpılmaz</b> — anchor ne derse desin: sayfa açıldığı
    /// gibi (takip yok), okuyucu sayfada gerçekten kaydırmış (direksiyon onda, dipten uzakta) ya da anchor elle dibe zorlanmış
    /// (<c>StickToBottom = true</c>). [B2 fix 2 · madde 5] "scrolled up" satırı gerçek bir okuyucu jestidir — kaydırma çubuğunu tutup sayfanın
    /// sonuna inmek ve biraz yukarı çıkmak (<see cref="UserScrollGesture"/>: <c>UserScrollSignal</c>'in kanalı); eskiden <c>StickToBottom =
    /// false</c> ile durum elle yazılıyordu ve o hâl "opened" satırıyla aynıydı (sayfa tepede açılır, dipte sayılmaz) — jest direksiyonu da
    /// okuyucuya verir, satır artık ayrı bir anchor durumunu sınar. Kırpma
    /// yetkisi TEK yerdedir (<c>BottomAnchorBehavior.ShouldFollow</c>) ve proje sayfasında hiç açılmaz (anchor'ın takip izni): chatty bir
    /// derlemenin her satırı sayfada, sırayla kalır; görünüm yerinden oynamaz; arkada bir şey olmadığından tepeye kaydırma bir şey getirmez.
    /// <para>Kırpmanın takibe bağlı olmasının kendi gerekçesi ayrıdır ve önemlidir: kırpma belgenin BAŞINDAN satır siler, yani kullanıcı
    /// yukarıda okurken metni ayağının altından yukarı kaydırırdı.</para>
    /// <para>[B2 fix 1 · O3] Dört test buraya katlandı, her iddiasıyla: <c>Project_mode_does_not_trim_appended_lines_while_follow_is_off</c>
    /// (açılış: takip kapalı, kırpma yok — eski <c>_trimTail</c> alanının "run modunda daima kırp, proje modunda yalnız dipteyken kırp" ayrımı
    /// tek predicate'te yaşar; karar 45'ten beri "dipteyken" kolu da yok), <c>Project_mode_scrolled_up_document_is_not_trimmed</c> (okuyucu
    /// yukarıda), <c>A_project_page_is_never_capped_at_the_render_slice_and_does_not_move</c> ve
    /// <c>Live_lines_on_a_project_page_are_never_trimmed_even_when_it_is_forced_to_the_bottom</c> (dibe zorlanmış). Son ikisinin eski iddiaları
    /// (karar 44-45'ten önce): izleyen proje logu render dilimine kırpılır, kırpılanlar görünümün listesinden tepeye kaydırınca geri gelir.</para>
    /// </summary>
    [StaTheory]
    [InlineData("opened")]
    [InlineData("scrolled up")]
    [InlineData("forced to the bottom")]
    public void Live_lines_on_a_project_page_are_never_trimmed_whatever_the_anchor_says(string anchor)
    {
        var view = LaidOutView();
        string[] page = [.. Enumerable.Range(0, 250).Select(i => $"line{i}")];
        view.PlayCascade(Text(page));
        if (anchor == "scrolled up")
        {
            UserScrollGesture.Raise(view);                                   // okuyucu çubuğu tuttu, sayfanın sonuna indi...
            view.Editor.ScrollToVerticalOffset(view.Editor.ExtentHeight);
            view.UpdateLayout();
            UserScrollGesture.Raise(view);                                   // ...ve biraz yukarı çıktı
            view.Editor.ScrollToVerticalOffset(view.Editor.VerticalOffset - 200);
            view.UpdateLayout();
            Assert.False(view.StickToBottom); // ön-koşul: anchor jesti gördü — okuyucu dipten uzakta
            Assert.True(view.Editor.VerticalOffset > 0, "test kurgusu: okuyucu sayfanın içinde olmalıydı");
        }
        if (anchor == "forced to the bottom") view.StickToBottom = true;
        double offset = view.Editor.VerticalOffset;

        for (int i = 0; i < 400; i++) view.AppendBatch($"live{i}\n"); // render diliminin iki katı
        view.UpdateLayout();

        string everything = Text(page) + string.Concat(Enumerable.Range(0, 400).Select(i => $"live{i}\n"));
        Assert.False(view.FollowsBottom);
        Assert.Equal(everything, view.Document.Text); // hiçbiri kırpılmadı, sırayla
        Assert.Equal(offset, view.Editor.VerticalOffset);
        ScrollUpToTheTop(view);
        Assert.Null(view.LastPrepend);                // arkada bir şey yok
        Assert.Equal(everything, view.Document.Text);
    }

    // ---------------------------------------------------------------- [3b] kaskat (reduced-motion instant yolu)

    [StaFact]
    public void PlayCascade_shows_all_lines_when_reduced_motion_instant()
    {
        // Headless testte App.Motion null → animationsEnabled=false → kaskat INSTANT (tüm satırlar, fade yok).
        var view = new ConsoleView();

        view.PlayCascade("a\nb\nc\n");

        Assert.Equal("a\nb\nc\n", view.Document.Text);
    }

    /// <summary>
    /// [B2 fix 1 · O1] Sayfa metni '\n' ile bitmese de sonradan gelen canlı satır YENİ satırda başlar (<see cref="ConsoleLines.Terminated"/>):
    /// append sözleşmesi metnin '\n' ile bittiğini varsayar.
    /// <para>PİN — O1'den önce de yeşildi (görünüm satır listesini birleştirirken '\n' zaten ekleniyordu); metin olduğu gibi alınınca garanti
    /// görünümde tek yerde durur (bozma: garanti kaldırılınca canlı satır son satıra yapışır).</para>
    /// </summary>
    [StaFact]
    public void A_project_page_text_without_a_final_newline_still_takes_live_lines_on_their_own_line()
    {
        var view = new ConsoleView();

        view.PlayCascade("first\nlast");
        view.AppendBatch("live\n");

        Assert.Equal("first\nlast\nlive\n", view.Document.Text);
    }

    // ---------------------------------------------------------------- proje-log sayfasının sonu

    /// <summary>
    /// <b>[DEĞİŞEN KURAL] Proje-log sayfasının sonunda "build in progress ▮" işareti YOKTUR.</b>
    ///
    /// <para><b>Eski iddia</b> (design-v1 §2.5, a6 kalemi): derlenen bir projenin logu açıldığında sona amber
    /// bir <c>build in progress</c> metni + yanıp sönen 7×13 blok imleç konur.</para>
    ///
    /// <para><b>Değişme gerekçesi (kullanıcı, sahada):</b> işaret açılış ANINDA kurulup bir daha
    /// güncellenmiyordu — proje, kullanıcı loguna bakarken bitince ekranda "build in progress" KALIYORDU.
    /// Söylediği şeyi zaten iki senkron yüzey söylüyor: başlıktaki statü glyph'i/adı ve listedeki satır.
    /// Üçüncü ve senkronu olmayan bir kanal, doğru olmadığı anlarda ekranı yalancı yapıyordu; senkronlamak
    /// yerine kaldırıldı.</para>
    ///
    /// <para>Test yüzeyin YOKLUĞUNU görsel ağaçtan doğrular, alanın yokluğundan değil: derleyici zaten alanı
    /// koruyamaz, ama biri metni başka bir öğeye geri koyarsa bu test kırılır.</para>
    /// </summary>
    [StaFact]
    public void The_project_log_page_has_no_build_in_progress_marker()
    {
        var view = new ConsoleView();
        var window = DsResources.Realize(DsResources.NewHost(), view);

        view.PlayCascade("log line\n");
        view.UpdateLayout();

        Assert.DoesNotContain(Descendants(view).OfType<TextBlock>(),
            t => t.Text.Contains("build in progress", StringComparison.OrdinalIgnoreCase));
        GC.KeepAlive(window);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    // ---------------------------------------------------------------- [3b I-2] chunk loader GERÇEK yolu

    /// <summary>
    /// Chunk yükleyicinin GERÇEK yolu: arm (tepeden uzaklaş) → tepeye kaydırma → <c>PrependPreviousChunk</c> bitişik eski dilimi ekler ve
    /// <c>VerticalOffset</c>'i telafi eder; yüklenecek dilim kalmayınca ikinci tepeye kaydırma bir şey yapmaz. Run anlatısının geçmişi de
    /// böyle geri gelir (aşağıdaki bölümün ölçülen kusuru): ← Back belgesi son 200 satırdır, öncesi tepeye kaydırınca anlatı store'undan,
    /// bitişik ve TAM — tekrar yok, kayıp yok.
    /// <para>[DEĞİŞEN KURULUM — dalga 3 · karar 44] Eskiden proje sayfasıyla kuruluyordu (<c>PlayCascade</c>, 250 satır: belge son 200,
    /// <c>line50</c>'den başlar). Proje sayfası artık logun tamamını taşır ve arkasında dilim yoktur; yükleyici yalnız anlatıda iş görür.
    /// Aynı iddia anlatı üzerinde (← Back akışı: son 200 belgede, öncesi store'da) sınanır.</para>
    /// <para>[B2 fix 1 · O3] <c>Run_narrative_scroll_to_top_prepends_older_history</c> buraya katlandı: aynı kurulum (← Back, 250 satır) ve
    /// aynı iddialar (son dilim belgede, öncesi değil; tepeye kaydırınca bitişik ve tam); bu test telafiyi ve ikinci kaydırmayı da sınar.</para>
    /// </summary>
    [StaFact]
    public void Chunk_scroll_to_top_prepends_previous_slice_contiguously_and_compensates_offset()
    {
        // Layout: TextView.DefaultLineHeight/VerticalOffset gerçek değer alsın (offset telafisi ölçülebilsin).
        var view = LaidOutView();

        var all = Enumerable.Range(0, 250).Select(i => $"line{i}").ToArray();
        var store = new NarrativeStore();
        foreach (var line in all) store.Append(line);
        view.ShowRunDocument(NarrativeSnapshots.Of(store)); // instant → son 200 (line50..line249), öncesi store'da
        Assert.StartsWith("line50\n", view.Document.Text);
        Assert.DoesNotContain("line49\n", view.Document.Text); // ilk 50 henüz chunk loader'da

        ScrollUpToTheTop(view); // scroll-to-top → önceki chunk prepend edilir

        // Dikiş: line0..line249 bitişik ve TAM — tekrar YOK, kayıp YOK.
        var expected = string.Concat(all.Select(l => l + "\n"));
        Assert.Equal(expected, view.Document.Text);

        // Offset prepend edilen 50 satırın piksel yüksekliği kadar telafi edildi (viewport zıplamaz).
        Assert.NotNull(view.LastPrepend);
        var (before, delta, applied) = view.LastPrepend!.Value;
        Assert.True(delta > 0, $"prepend edilen 50 satırın piksel yüksekliği > 0 olmalı (delta={delta})");
        Assert.Equal(before + delta, applied, 3); // ChunkStitch.CompensatedOffset wiring

        // Re-arm + tekrar tepe: yüklenecek daha eski satır yok → idempotent (tekrar yükleme/dup YOK).
        ScrollUpToTheTop(view);
        Assert.Equal(expected, view.Document.Text);
    }

    // ---------------------------------------------------------------- run anlatısının geçmişi de geri gelir
    //
    // Ölçülen kusur (kullanıcı, sahada): "konsola bir sürü şey basılıyor ama en sonda bakıyorum azıcık şey
    // kalmış". Veri kaybolmuyordu — her projenin tam logu ayrı tutulur ve run anlatısının tamamı VM'in
    // tamponundadır — kaybolan GÖRÜNÜRLÜKTÜ: canlı belge son 200 satırla sınırlıdır (render dilimi) ve chunk
    // loader YALNIZ proje modunda çalışıyordu, yani run anlatısında 200 satırdan öncesine hiçbir jestle
    // erişilemiyordu. Paralel bir build saniyede yüzlerce satır aktığı için o pencere anında doluyor.
    //
    // Anlatıda render diliminden öncesi tepeye kaydırınca VM'in anlatı store'undan geri gelir (görünüm kopya tutmaz — [DEĞİŞEN KURAL],
    // bkz. Lines_trimmed_from_the_live_run_document_stay_reachable_by_scrolling_up); temel dikiş iddiası Chunk_scroll_to_top_…'tadır
    // (B2 fix 1 · O3: Run_narrative_scroll_to_top_prepends_older_history oraya katlandı). [DEĞİŞEN KURAL — dalga 3 · karar 44] Bu not
    // eskiden "backlog moddan bağımsızdır … proje logunda görünümün kendi listesi; orada belgeden kırpılan canlı satırlar listeye eklenir"
    // diyordu: proje sayfası artık logun tamamını taşır, dilimi ve backlog'u yoktur.

    /// <summary>
    /// [idle-memory · dalga 2 · spec §6.3] <b>2.000 satırın üstündeki anlatıda tepeye kaydırma doğru dilimleri DİSKTEN getirir.</b>
    /// Store anlatıyı koşu klasörüne yayar ve bellekte yalnız son <see cref="NarrativeStore.MemoryWindowLines"/> satırı tutar; görünüm
    /// tepeye kaydıkça önceki dilimleri yine store'dan ister — en eskileri dosyadan, biri tahliye sınırını keserek (baş diskten, kuyruk
    /// bellekten). Sonuç bitişik ve TAMDIR: tekrar yok, delik yok — kaydırılan hiçbir satır erişilmez olmaz.
    /// </summary>
    [StaFact]
    public void Scrolling_up_a_spooled_narrative_brings_the_older_slices_back_from_disk()
    {
        var store = new NarrativeStore();
        using var logs = new SpoolDir(store);
        Assert.True(store.BeginSpool(logs.FilePath));
        var all = Enumerable.Range(0, NarrativeStore.MemoryWindowLines + 1000).Select(i => $"spooled{i}").ToArray();
        foreach (var line in all) store.Append(line);
        Assert.True(store.Count - store.LinesInMemory > ConsoleView.RenderSliceLines, "ön-koşul: en eski dilimler yalnız diskte");
        var view = LaidOutView();

        view.ShowRunDocument(NarrativeSnapshots.Of(store)); // son 200 belgeye; öncesi store'da, en eskisi diskte
        ScrollUpToTheTop(view, attempts: all.Length / ConsoleView.RenderSliceLines + 1);

        Assert.Equal(string.Concat(all.Select(l => l + "\n")), view.Document.Text); // bitişik ve TAM
    }

    /// <summary>
    /// [idle-memory · dalga 2 · inceleme M1] <b>Okunamayan bir geçmiş dilimi delik bırakmaz.</b> Backlog'un içindeki bir aralık ya tam
    /// gelir ya <c>""</c> (<see cref="IConsoleBacklog.Join"/>) — store'un dosyası okunamazsa ikincisi. Görünüm boş dilimde yerini
    /// KORUR: o satırları atlamaz, sonraki tepeye kaydırma aynı dilimi yeniden ister. Eskiden indeks dilimin başına iniyordu; o 200
    /// satır o belgede bir daha hiç gelmiyordu — dosya yeniden okunabilse de.
    /// </summary>
    [StaFact]
    public void An_unreadable_history_slice_leaves_no_hole_and_the_next_scroll_to_the_top_asks_again()
    {
        var view = LaidOutView();
        var all = Enumerable.Range(0, 1000).Select(i => $"history{i}").ToArray();
        var backlog = new PartlyUnreadableBacklog(all) { ReadableFrom = 500 }; // 500'ün altı okunamıyor
        view.ShowRunDocument(new NarrativeSnapshot(backlog, all.Length));       // son 200 belgede: history800..999
        ScrollUpToTheTop(view, attempts: 6);
        Assert.StartsWith("history600\n", view.Document.Text); // ön-koşul: okunabilen dilim geldi, okunamayanda durdu

        backlog.ReadableFrom = 0; // dosya yeniden okunabiliyor
        ScrollUpToTheTop(view, attempts: 6);

        Assert.Equal(string.Concat(all.Select(l => l + "\n")), view.Document.Text); // KIRMIZI: 400..599 atlanmıştı — delik
    }

    /// <summary>
    /// [idle-memory · dalga 2 · inceleme M1] <b>Son dilimi okunamayan bir anlatı belgesi anlatının SONUNDAN başlar.</b> Belge boş kurulur
    /// ve ilk satırı anlatının sonudur: indeks gösterilmeyen 200 satırı belgede sanmaz, tepeye kaydırma o dilimi yeniden ister. Eskiden
    /// indeks dilimin başında kalıyordu — ilk kaydırma bir ÖNCEKİ dilimi getirir, son 200 satır hiç gelmezdi; canlı satırlar da 200
    /// satır geride sayılırdı.
    /// </summary>
    [StaFact]
    public void A_run_document_whose_last_slice_cannot_be_read_starts_at_the_end_and_asks_for_it_again()
    {
        var view = LaidOutView();
        var all = Enumerable.Range(0, 1000).Select(i => $"history{i}").ToArray();
        var backlog = new PartlyUnreadableBacklog(all) { ReadableFrom = all.Length }; // hiçbir dilim okunamıyor
        view.ShowRunDocument(new NarrativeSnapshot(backlog, all.Length));
        Assert.Equal("", view.Document.Text); // ön-koşul: dilim okunamadı, belge boş

        backlog.ReadableFrom = 0; // dosya yeniden okunabiliyor
        ScrollUpToTheTop(view, attempts: 6);

        Assert.Equal(string.Concat(all.Select(l => l + "\n")), view.Document.Text); // KIRMIZI: son 200 satır hiç gelmezdi
    }

    /// <summary>
    /// [idle-memory · dalga 2 · beşinci inceleme — ölçüldü] Görünüme sığan KISA bir belge, arkasında geçmiş dururken takipte gelen satırla
    /// önceki dilimi kendiliğinden yüklemez — kimse tepeye kaydırmadı. Böyle bir belge son dilimi okunamayan anlatıdır: boş kurulur, satırlar
    /// sonradan gelir.
    /// <para>KIRMIZI (ölçüldü): takip <c>ScrollToEnd</c> ister ve TextView o isteği tek bir olayda +∞ ofsetle bildirir — yükleyici "tepeden
    /// uzaklaştı" diye silahlanıyordu (her takip batch'inde yeniden); hemen ardından kenetlenen konum (belge sığdığı için 0) "tepeye ulaştı"
    /// sayılıyor ve önceki 200 satır isteyen olmadan ekleniyordu.</para>
    /// </summary>
    [StaFact]
    public void A_short_document_with_history_behind_it_does_not_load_it_unasked_while_following()
    {
        var view = LaidOutView();
        var all = Enumerable.Range(0, 1000).Select(i => $"history{i}").ToArray();
        var backlog = new PartlyUnreadableBacklog(all) { ReadableFrom = all.Length }; // son dilim okunamadı: belge boş, geçmiş arkada
        view.ShowRunDocument(new NarrativeSnapshot(backlog, all.Length));
        backlog.ReadableFrom = 0; // dosya yeniden okunabiliyor
        Assert.True(view.FollowsBottom); // ön-koşul: anlatı dibi izliyor

        view.AppendBatch("live\n"); // takipte bir satır: ScrollToEnd
        view.UpdateLayout();

        Assert.Null(view.LastPrepend);
        Assert.Equal("live\n", view.Document.Text);
    }

    /// <summary>
    /// [idle-memory · dalga 2 son inceleme · defter 2] <b>Boş dilim kuralının istisnası: store belgenin GERİSİNDEyse yüklenecek bir şey
    /// yoktur ve indeks iner.</b> Pencere gizliyken yeni bir işlem anlatıyı temizler; belge dönüşe dek kurulmaz (bayat belge — kabuk onu
    /// dönüşte modelden bir kez kurar). Bu sırada gelen bir tepeye kaydırma boş dilim alır: "aynı dilimi yeniden iste" kuralı burada
    /// indeksi yerinde tutsaydı, yeni bölümün satırları gelince aynı indeks onların ortasını bayat belgenin tepesine dikerdi. İndeks
    /// iner, belge bayat kalır ve dönüşte yeniden kurulur.
    /// <para>KIRMIZI (<c>ConsoleView.PrependPreviousChunk</c>'ın "backlog belgenin gerisinde" dalı geçici olarak silindi): indeks eski
    /// bölümün yerinde kalıyordu — yeni bölümün <c>new0..new299</c> satırları eski bölümün belgesine dikiliyordu.</para>
    /// </summary>
    [StaFact]
    public void A_document_left_over_a_narrative_cleared_while_hidden_takes_nothing_of_the_new_section_until_it_is_rebuilt()
    {
        var view = LaidOutView();
        var store = new NarrativeStore();
        foreach (int i in Enumerable.Range(0, 500)) store.Append($"old{i}");
        view.ShowRunDocument(NarrativeSnapshots.Of(store)); // son 200 belgede: old300..old499
        string stale = view.Document.Text;

        store.Clear();                         // gizliyken yeni işlem: anlatı temizlendi, belge kurulmadı
        ScrollUpToTheTop(view, attempts: 3);   // store belgenin gerisinde: yüklenecek yok, indeks iner
        foreach (int i in Enumerable.Range(0, 400)) store.Append($"new{i}"); // yeni bölümün satırları (pencere hâlâ gizli)
        ScrollUpToTheTop(view, attempts: 3);

        Assert.Equal(stale, view.Document.Text); // KIRMIZI: new0..new299 bayat belgenin tepesine dikiliyordu

        view.ReplaceRunDocument(NarrativeSnapshots.Of(store)); // dönüş: belge modelden bir kez kurulur (ResyncAfterShow)
        Assert.Equal(string.Concat(Enumerable.Range(200, 200).Select(i => $"new{i}\n")), view.Document.Text); // yeni bölümün son dilimi
    }

    /// <summary>
    /// [idle-memory · dalga 2 · takip — ölçüldü] <b>Belge takası bir okuyucu jesti değildir.</b> Anlatı belgesi yeniden kurulurken (← Back,
    /// tepsiden dönüş) görünüm yalnız store'un son dilimini yükler — taze bir görünümün kurduğu belgenin aynısı; önceki dilim okuyucunun
    /// ilk tepeye kaydırmasıyla gelir. Önceki belge dipte olsa da (anlatı canlı akışı izliyordu, proje sayfası sonuna kaydırılmıştı).
    /// <para>KIRMIZI: takasın kendi kaydırma olayları chunk yükleyiciye gidiyordu — eski belgenin ofsetini taşıyan ilk olay yükleyiciyi
    /// "tepeden uzaklaştı" diye yeniden silahlandırıyor (kurulumun başındaki silahsızlandırma boşa gidiyordu), yeni belgenin ilk yerleşimi
    /// ofseti sıfıra çekince "tepeye ulaştı" sayılıp önceki 200 satır kendiliğinden ekleniyor, ardından takip kırpması fazlayı atıyordu:
    /// belge son 199 satırdı (<c>line201..</c>), UI thread'inde boşuna bir dilim okuması, ekleme ve kırpma. Satır kaybı yoktu (indeks
    /// kırpmayla ilerliyor, tepeye kaydırma geri getiriyordu) ve "N lines" store'u sayar.</para>
    /// </summary>
    [StaTheory]
    [InlineData("narrative")]
    [InlineData("project page")]
    public void Rebuilding_the_narrative_over_a_document_at_its_end_loads_only_the_last_slice(string previous)
    {
        var view = LaidOutView();
        if (previous == "narrative")
        {
            var earlier = new NarrativeStore();
            foreach (int i in Enumerable.Range(0, 500)) earlier.Append($"old{i}");
            view.ShowRunDocument(NarrativeSnapshots.Of(earlier)); // dipte, canlı akışı izleyen anlatı
        }
        else
        {
            view.PlayCascade(Text([.. Enumerable.Range(0, 300).Select(i => $"page{i}")]));
            view.Editor.ScrollToEnd(); // okuyucu sayfanın sonuna indi
            view.Editor.UpdateLayout();
        }
        var store = new NarrativeStore();
        foreach (int i in Enumerable.Range(0, 400)) store.Append($"line{i}");

        view.ReplaceRunDocument(NarrativeSnapshots.Of(store)); // ← Back / tepsiden dönüş: belge yeniden kurulur

        Assert.Null(view.LastPrepend); // KIRMIZI: takas önceki dilimi kendiliğinden ekliyordu
        Assert.Equal(string.Concat(Enumerable.Range(200, 200).Select(i => $"line{i}\n")), view.Document.Text);
        ScrollUpToTheTop(view);        // okuyucunun ilk tepeye kaydırması önceki dilimi getirir
        Assert.Equal(string.Concat(Enumerable.Range(0, 400).Select(i => $"line{i}\n")), view.Document.Text);
    }

    /// <summary>
    /// [idle-memory · dalga 2 · ikinci inceleme — C4 gerilemesi] Yeniden kurulan anlatı dibe sabitlenir ve chunk yükleyici o konumdan
    /// silahlı durur: okuyucunun TEK tepeye sıçraması (Ctrl+Home, kaydırma çubuğunun tek hamlesi — önce aşağı inmeden) önceki dilimi
    /// getirir; daha eskisi de böyle, diskten (spec §10.3). Takasın kendi geçici olayları yine yok sayılır — takas dilimi kendiliğinden
    /// eklemez (<see cref="Rebuilding_the_narrative_over_a_document_at_its_end_loads_only_the_last_slice"/>).
    /// <para>KIRMIZI: takas yükleyiciyi koşulsuz silahsız bırakıyordu ve takasın olayları (sabitlemenin dibe kaydırması dahil) yok
    /// sayıldığından yükleyiciyi hiçbir şey silahlandırmıyordu: ← Back ya da tepsiden dönüşten sonraki tek sıçrama dilimin tepesinde
    /// kalıyor, eski satırlar hiç gelmiyordu — okuyucu önce aşağı inip yeniden çıkmadıkça.</para>
    /// <para>Sıçrama üretimin karar yolundan, tepedeki ofsetle sürülür (<see cref="ConsoleView.EvaluateChunkScroll"/>): böylece yalnız takasın
    /// bıraktığı durum sınanır. Kullanıcının gerçek tuş yolu — gerçek pencere, editörün kendi Ctrl+Home bağlaması, boşa kadar pompalanan
    /// dispatcher — <see cref="A_real_ctrl_home_in_a_rebuilt_narrative_loads_the_previous_slice"/>'tadır.</para>
    /// </summary>
    [StaFact]
    public void A_rebuilt_narrative_brings_the_previous_slice_on_a_single_jump_to_the_top()
    {
        var view = LaidOutView();
        var store = new NarrativeStore();
        foreach (int i in Enumerable.Range(0, 400)) store.Append($"line{i}");
        view.ReplaceRunDocument(NarrativeSnapshots.Of(store)); // ← Back / tepsiden dönüş: dibe sabitlenmiş son dilim

        view.EvaluateChunkScroll(0.0); // TEK sıçrama (Ctrl+Home) — öncesinde silahlandıran bir jest yok

        Assert.Equal(string.Concat(Enumerable.Range(0, 400).Select(i => $"line{i}\n")), view.Document.Text);
    }

    /// <summary>
    /// [idle-memory · dalga 2 · üçüncü inceleme — ölçüldü] <b>Gerçek bir Ctrl+Home yükleyiciye ulaşır.</b> Gerçek (ekran dışı) pencerede
    /// yeniden kurulan, dibe sabitlenmiş 400 satırlık anlatı; okuyucunun tuşu — <c>UserScrollSignal</c>'in gördüğü jest ve editörün KENDİ
    /// Ctrl+Home bağlaması (<c>MoveToDocumentStart</c>: imleç belgenin başına, görünüm tepeye) — ve dispatcher boşa kadar pompalanır.
    /// Önceki dilim yüklenir: spec §10.3 "tepeye kaydırınca tüm geçmiş gelir".
    /// <para>KIRMIZI (ölçüldü): tuş TEK bir kaydırma olayı üretir ve o olayda ScrollViewer'ın ofseti henüz eskidir (3173,4) — TextView'in kendi
    /// ofseti 0'dır. İşleyici ScrollViewer'ı okuyordu: "tepeden uzakta" deyip hiçbir şey yüklemiyordu; ScrollViewer sonraki yerleşimde 0'a
    /// iner ama yeni olay gelmez — okuyucu dilimin tepesinde kalıyordu, en tepede tekerlek de olay üretmez.</para>
    /// <para>Kurulum ve tuş <see cref="RealizedNarrative"/>'tedir (konsolun gerçek gezinme testleri de onu kullanır); jest kanalı
    /// kaydırma çubuğu yerine tuşun kendi önizlemesidir — ikisi de aynı <c>NotifyUserScroll</c>'a bağlı.</para>
    /// <para>[I-1 · B2 fix 1 O3] <c>Real_OnScrollOffsetChanged_path_prepends_on_jump_to_top_and_releases_stuck_without_data_loss</c> buraya
    /// katlandı. İddiası: bottom-anchor'ın takip kararı ile yükleyicinin prepend'i AYNI gerçek kaydırma olayında çakıştığında (a) prepend
    /// delik bırakmaz ve ofseti TAM telafi eder (<c>ChunkStitch.CompensatedOffset</c> otoritedir — telafinin üzerine yanlışlıkla "dibe git"
    /// yazılmaz), (b) okuyucu artık dipte sayılmaz (<c>StickToBottom</c> false: prepend'in kendi büyümesi bayat bir "dipte" kararını
    /// tutmaz; tutsaydı sonraki içerik büyümesi okuyucuyu yeniden dibe fırlatırdı). O test aynı olayı ham jest + programatik tepeye kaydırma
    /// ile kuruyordu (geçmişi: dalga 2 dördüncü incelemede fazladan doğrudan çağrıları kaldırılmış, dalga 3'te proje sayfasından anlatıya
    /// taşınmıştı); gerçek tuş aynı yolu sürer — telafi iddiası başka hiçbir yerde gerçek olay yolunda sınanmıyordu, takip iddiası
    /// <c>ConsoleReaderNavigationTests.A_real_ctrl_home_releases_the_console_from_the_bottom</c>'dadır ve burada da tutulur.</para>
    /// </summary>
    [StaFact]
    public void A_real_ctrl_home_in_a_rebuilt_narrative_loads_the_previous_slice()
    {
        using var narrative = new RealizedNarrative(lines: 400); // ← Back / tepsiden dönüş

        narrative.PressCtrlHome();

        Assert.NotNull(narrative.View.LastPrepend);
        var (before, delta, applied) = narrative.View.LastPrepend!.Value;
        Assert.True(delta > 0, $"prepend edilen dilimin piksel yüksekliği > 0 olmalı (delta={delta})");
        Assert.Equal(before + delta, applied, 3); // (a) telafi — ChunkStitch.CompensatedOffset otorite
        Assert.False(narrative.View.StickToBottom); // (b) prepend'in büyümesi okuyucuyu dipte saydırmaz

        Assert.Equal(string.Concat(Enumerable.Range(0, 400).Select(i => $"line{i}\n")), narrative.View.Document.Text);
    }

    /// <summary>
    /// [idle-memory · dalga 2 · altıncı inceleme] İki ardışık GERÇEK Ctrl+Home iki önceki dilimi getirir: ilk prepend'in telafi olayı
    /// yükleyiciyi yeniden kurar, ikinci sıçrama bir önceki dilimi yükler — tek dilimle kalmaz.
    /// <para>PİN — yeşil doğar.</para>
    /// </summary>
    [StaFact]
    public void Two_real_ctrl_home_presses_load_two_previous_slices()
    {
        using var narrative = new RealizedNarrative(lines: 600);

        narrative.PressCtrlHome();
        Assert.StartsWith("line200\n", narrative.View.Document.Text); // ilk dilim geldi
        narrative.PressCtrlHome();

        Assert.Equal(string.Concat(Enumerable.Range(0, 600).Select(i => $"line{i}\n")), narrative.View.Document.Text);
    }

    /// <summary>
    /// [dalga 3 · karar 44] Proje sayfası logun TAMAMINI taşır ve arkasında yüklenecek dilim yoktur: tepedeki bir olay da (yerleşim,
    /// programın kendi kaydırması), okuyucunun aşağı inip yeniden tepeye çıkması da hiçbir şey eklemez.
    /// <para>[DEĞİŞEN KURAL] Eski iddia (dalga 2 üçüncü inceleme): sayfa son 200 satırla (<c>page100</c>'den) açılır ve chunk yükleyici
    /// silahsızdır — tepede duran okuyucuya gelen ilk olay önceki dilimi kendiliğinden eklemez, önceki dilim okuyucu aşağı inip tepeye
    /// döndüğünde gelir (kırmızısı: takasın sonunda yükleyici koşulsuz silahlıyken ilk olay ekliyordu). Değişme gerekçesi (sahip kararı 44):
    /// proje sayfası logun ilk satırından açılır ve logun tamamı belgededir (ölçüm: 10 000 satırlık log ~21 ms'de açılır, sayfa açıkken
    /// yığında ~1,8 MB fazla, kapanınca bırakılır) — yüklenecek bir şey olmadığından "silahsız açılış" sorusu kalmadı.</para>
    /// </summary>
    [StaFact]
    public void A_project_page_holds_its_whole_log_and_a_scroll_to_the_top_loads_nothing()
    {
        var view = LaidOutView();
        string[] page = [.. Enumerable.Range(0, 300).Select(i => $"page{i}")];
        view.PlayCascade(Text(page)); // sayfa tepede

        view.EvaluateChunkScroll(0.0); // tepede bir olay — okuyucu kaydırmadı
        ScrollUpToTheTop(view);        // okuyucu aşağı inip tepeye döner

        Assert.Null(view.LastPrepend);
        Assert.Equal(string.Concat(page.Select(line => line + "\n")), view.Document.Text);
    }

    /// <summary>[idle-memory · dalga 2 · inceleme M1] Bir kısmı okunamayan backlog — anlatı store'unun dosyası okunamadığında olduğu
    /// gibi: <see cref="ReadableFrom"/>'un altına uzanan her aralık <c>""</c> döner (asla kısmi metin — <see cref="IConsoleBacklog.Join"/>
    /// sözleşmesi); sınır inince aynı aralık tam gelir. Store gibi kırpılan satırları saklamaz.</summary>
    private sealed class PartlyUnreadableBacklog(IReadOnlyList<string> lines) : IConsoleBacklog
    {
        public int ReadableFrom { get; set; }

        public int Count => lines.Count;

        public bool KeepsTrimmedLines => false;

        public string Join(int from, int to) => from < ReadableFrom ? "" : ConsoleLines.Join(lines, from, to);

        public void AcceptTrimmed(IReadOnlyList<string> trimmed) { }
    }

    /// <summary>
    /// Canlı anlatıda belgeden kırpılan satırlar tepeye kaydırınca DELİKSİZ geri gelir.
    ///
    /// <para><b>[DEĞİŞEN KURAL — idle-memory dalga 1]</b> Eski iddia: kırpılan canlı satırlar görünümün backlog'una
    /// TAŞINIR — görünüm anlatının kendi kopyasını tutar (test boş metinle tohumlayıp satırları doğrudan belgeye basıyordu;
    /// geri gelen satırlar görünümün listesindendi). Değişme gerekçesi (ölçüldü, boşta bellek analizi §3): o liste VM
    /// tamponunun ikinci kopyasıydı — 83 k satırlık bir koşuda 59 MB. Artık her anlatı satırı önce VM'in store'una yazılır,
    /// sonra pompayla belgeye gelir; kırpılan satır zaten store'dadır ve geri oradan gelir, görünüm kopya tutmaz. Test bu
    /// yüzden üretim düzenini kurar: satır önce store'a, sonra batch olarak görünüme.</para>
    /// </summary>
    [StaFact]
    public void Lines_trimmed_from_the_live_run_document_stay_reachable_by_scrolling_up()
    {
        var view = LaidOutView();
        var store = new NarrativeStore();
        view.ShowRunDocument(NarrativeSnapshots.Of(store)); // anlatı modu, takip AÇIK (canlı build gibi)

        for (int i = 0; i < 260; i++) NarrativeSnapshots.Deliver(store, view, [$"live{i}"]);
        Assert.DoesNotContain("live0\n", view.Document.Text); // ön-koşul: kırpıldı (belge 200 ile sınırlı)

        ScrollUpToTheTop(view);

        Assert.Contains("live0\n", view.Document.Text);          // store'dan DELİKSİZ geri geldi
        Assert.Contains("live59\nlive60\n", view.Document.Text); // dikiş yerinde boşluk yok
        Assert.False(view.BacklogOwnsCopy);                      // kırpılan satırlar görünüme kopyalanmadı
    }

    // ---------------------------------------------------------------- [3b C-1] follow-trim + scroll-to-top: delik yok

    /// <summary>
    /// [C-1 regression] Follow-trim belge tepesinden satır siler; bu, chunk loader'ın <c>_loadedFrom</c>
    /// index'ini de ilerletmeli. Aksi halde sonraki scroll-to-top prepend'i STALE index'e karşı YANLIŞ dilimi
    /// yükler → kırpılan satırlar KALICI kaybolur (delik). Repro: <c>_loadedFrom&gt;0</c> olan bir kaskat +
    /// çok sayıda canlı append (follow aktif) + tepeye kaydırma.
    ///
    /// <para><b>[DEĞİŞEN KURAL] Delik kavramı GENİŞLEDİ.</b> Eski test ilk prepend'in <c>orig100..orig299</c>'u
    /// getirmesini bekliyordu; o beklenti, canlı kırpılan satırların (<c>live0..live50</c>) KALICI KAYBINI
    /// normal sayıyordu — backlog yalnız kaskattan gelen disk satırlarıydı ve <c>_loadedFrom</c> onun uzunluğuna
    /// clamp'leniyordu ("index'i yok"). Yani kapatılan delik, açık bırakılan bir başkasının üstüne kuruluydu:
    /// belgeden düşen canlı satırlar ne belgede ne backlog'da kalıyordu.</para>
    ///
    /// <para>Backlog artık canlı büyüdüğü için kural sadeleşti: <b>belgeden düşen HİÇBİR satır kaybolmaz</b>.
    /// Bu yüzden iddia da sadeleşti — ara dilimin sınırını (hangi <c>orig</c>'den başladığı) pinlemek yerine
    /// asıl sözleşme pinlenir: geçmiş, kronolojik sırada ve EKSİKSİZ geri gelir.</para>
    ///
    /// <para>[DEĞİŞEN KURULUM — dalga 3 · karar 44-45] Eskiden proje sayfasıyla kuruluyordu: 300 satırlık kaskat (son 200 belgede,
    /// backlog görünümün listesi), "kullanıcı dibe indi → takip açık" (<c>StickToBottom = true</c>) ve ham <c>AppendBatch</c>'ler. Proje
    /// sayfası artık logun tamamını taşır ve hiç izlemez — orada kırpma da indeks de yoktur (yeni kuralın pini:
    /// <see cref="Live_lines_on_a_project_page_are_never_trimmed_whatever_the_anchor_says"/>). C-1 iddiası takip kırpmasının
    /// yaşadığı yerde, anlatıda sınanır: dibe sabitlenmiş, izleyen ← Back belgesi (son 200 belgede, öncesi store'da) ve üretim düzeniyle
    /// gelen canlı satırlar (önce store, sonra batch). Dikiş, telafi ve eksiksizlik iddiaları aynı.</para>
    /// </summary>
    [StaFact]
    public void Follow_trim_then_scroll_to_top_recovers_the_backlog_without_a_hole()
    {
        var view = LaidOutView();

        // 300 satırlık anlatı → render dilimi son 200 (orig100..orig299), _loadedFrom=100 (öncesi store'da).
        var all = Enumerable.Range(0, 300).Select(i => $"orig{i}").ToArray();
        var store = new NarrativeStore();
        foreach (var line in all) store.Append(line);
        view.ShowRunDocument(NarrativeSnapshots.Of(store)); // ← Back: dibe sabitlenmiş, akışı izliyor
        Assert.StartsWith("orig100\n", view.Document.Text);
        Assert.DoesNotContain("orig99\n", view.Document.Text); // ilk 100 store'da

        // Chatty canlı build: takipteyken 250 satır → tail-trim TÜM orijinal satırları belgeden atar.
        for (int i = 0; i < 250; i++) NarrativeSnapshots.Deliver(store, view, [$"live{i}"]);
        string liveTail = view.Document.Text;              // belgede kalan salt-live kuyruk (orijinaller kırpıldı)
        Assert.Contains("live249\n", liveTail);            // en yeni korunur
        Assert.DoesNotContain("orig", liveTail);           // tüm orijinaller belgeden kırpıldı (store'da duruyor)

        // Kullanıcı yukarı kaydırır: arm → scroll-to-top → önceki chunk prepend edilir.
        ScrollUpToTheTop(view); // scroll-to-top → önceki chunk

        // (a) DELİK YOK: prepend, mevcut live kuyruğun ÖNÜNE kırpılan backlog'un SONUNU dikmeli — kuyruk
        // aynen korunur, araya kayıp/tekrar girmez. STALE index bug'ında _loadedFrom ilerlemez, eski dilim
        // (orig0..) yüklenir ve aradaki satırlar KALICI kaybolur.
        string afterFirst = view.Document.Text;
        Assert.EndsWith(liveTail, afterFirst);              // canlı kuyruk olduğu gibi durur
        Assert.Contains("orig299\nlive0\n", afterFirst);    // GERÇEK kronolojik dikiş: son orig'i ilk live izler
        Assert.DoesNotContain("orig0\n", afterFirst);       // en eski dilim henüz backlog'da (tek hamlede gelmez)

        // (b) VerticalOffset prepend edilen dilimin piksel yüksekliği kadar telafi edildi (viewport zıplamaz).
        Assert.NotNull(view.LastPrepend);
        var (before, delta, applied) = view.LastPrepend!.Value;
        Assert.True(delta > 0, $"prepend edilen dilimin piksel yüksekliği > 0 olmalı (delta={delta})");
        Assert.Equal(before + delta, applied, 3); // ChunkStitch.CompensatedOffset wiring

        // Tekrar tepeye kaydır: kalan en eski dilim de geri gelir → HİÇBİR satır kalıcı kayıp değildir.
        // Belge artık koşunun TAMAMIdır: orig0..orig299 ve ardından live0..live249, bitişik (contiguous).
        // Canlı kırpılan live0..live50 de buradadır — eski kuralda onlar kalıcı olarak kaybolmuştu.
        ScrollUpToTheTop(view);
        string everything = string.Concat(Enumerable.Range(0, 300).Select(i => $"orig{i}\n"))
                          + string.Concat(Enumerable.Range(0, 250).Select(i => $"live{i}\n"));
        Assert.Equal(everything, view.Document.Text);
    }

    // ---------------------------------------------------------------- [D4] anlatı batch'i + idle "ready"

    [StaFact]
    public void AppendNarrativeBatch_commits_every_line_including_the_newest_when_reduced_motion()
    {
        // Headless: App.Motion null → animationsEnabled=false → en yeni satır daktilosu INSTANT'a düşer, yani
        // batch'in TÜM satırları (en yeni dahil) dokümana girer — hiçbir satır overlay'de asılı kalmaz/kaybolmaz.
        var view = new ConsoleView();

        view.AppendNarrativeBatch("git fetch origin main\nSync complete — 7 changed projects\n");

        Assert.Contains("git fetch origin main", view.Document.Text);
        Assert.Contains("Sync complete — 7 changed projects", view.Document.Text);
    }

    /// <summary>
    /// design v1.7.0 §2.5: boşta/boot prompt satırı imleç + <c>ready</c> (dim) taşır; içerik gelince YALNIZ
    /// METİN boşalır.
    ///
    /// <para>[DEĞİŞEN KURAL] Eski iddia "içerik gelince prompt <b>Collapsed</b> olur"du — ilk çıktıdan sonra
    /// konsolda hiç imleç kalmıyordu. Otorite bunun tersini söylüyor: prototipte prompt satırı KOŞULSUZ render
    /// edilir (<c>BuildApp.jsx:766-771</c>), yalnız faz idle/boot değilken metni boşalır; imleç durur ve yeni
    /// satırlar onun ÜSTÜNE birikir. Kullanıcı da bunu istedi ("sadece kursor vardı, hep alt satıra iner,
    /// arkasından konsol yazısı basılıyor").</para>
    /// </summary>
    [StaFact]
    public void The_prompt_cursor_stays_after_content_arrives_and_only_the_ready_text_clears()
    {
        var view = new ConsoleView();

        view.ShowReady();
        Assert.Equal("ready", view.ActiveLineText.Text);
        Assert.Equal(Visibility.Visible, view.ActiveLineOverlay.Visibility);

        view.AppendNarrativeBatch("Sync complete — 0 changed projects\n");

        Assert.Equal(Visibility.Visible, view.ActiveLineOverlay.Visibility); // imleç DURUR
        Assert.Equal("", view.ActiveLineText.Text);                          // yalnız "ready" düştü
        Assert.Contains("Sync complete", view.Document.Text);
    }

    /// <summary>
    /// [design v1.8.0 §3.1 · kullanıcı kararı 2026-09-29] Workspace yokken prompt satırı imleç + <c>Waiting for a
    /// workspace</c> (dim, <c>ready</c> ile aynı renk) taşır ve gelen içerik onu SİLMEZ — ilk açılışta motor hemen
    /// kendi satırını yazar, <c>ready</c> gibi silinseydi bekleme metni hiç görünmezdi. Workspace gelince metin
    /// kalkar (içerik zaten var — <c>ready</c> geri gelmez).
    /// </summary>
    [StaFact]
    public void Without_a_workspace_the_prompt_waits_for_one_and_content_does_not_clear_it()
    {
        var view = new ConsoleView();
        view.ShowReady();
        var readyTone = view.ActiveLineText.Foreground;

        view.SetHasWorkspace(false);
        Assert.Equal("Waiting for a workspace", view.ActiveLineText.Text);
        Assert.Same(readyTone, view.ActiveLineText.Foreground);

        view.AppendNarrativeBatch("Engine ready — v1.0.0\n");
        Assert.Equal("Waiting for a workspace", view.ActiveLineText.Text);
        Assert.Equal(Visibility.Visible, view.ActiveLineOverlay.Visibility);

        view.SetHasWorkspace(true);
        Assert.Equal("", view.ActiveLineText.Text);
    }

    /// <summary>
    /// Prompt satırı BELGENİN SONUNDADIR: her yeni satır imleci bir satır aşağı iter, yazı hep onun üstüne
    /// birikir.
    ///
    /// <para>[DEĞİŞEN KURAL] İlk çözüm imleci panelin DİBİNE yaslıyor ve editörün altında bir satır boyu yer
    /// ayırıyordu. Yanlıştı: AvalonEdit içeriği yukarıdan aşağı dizer, yani üç satırlık bir konsolda metin
    /// tepede kalır ve dibe yaslı imleç metinden kopup sol altta tek başına yanardı. İmlecin yeri belgenin
    /// kendi son satırıdır.</para>
    /// </summary>
    [StaFact]
    public void The_prompt_sits_on_the_documents_last_line_and_moves_down_as_lines_arrive()
    {
        var view = new ConsoleView();
        var window = DsResources.Realize(DsResources.NewHost(), view);
        view.ShowReady();
        window.UpdateLayout();
        double lineHeight = view.EditorControl.TextArea.TextView.DefaultLineHeight;
        Assert.True(lineHeight > 0, "ön-koşul: editör ölçülmedi");

        double empty = view.ActiveLineOverlay.Margin.Top; // boş belgede: ilk satır

        view.AppendNarrativeBatch("first\n");
        window.UpdateLayout();
        double afterOne = view.ActiveLineOverlay.Margin.Top;

        view.AppendNarrativeBatch("second\n");
        window.UpdateLayout();
        double afterTwo = view.ActiveLineOverlay.Margin.Top;

        // Her satır imleci TAM bir satır boyu aşağı iter.
        Assert.Equal(lineHeight, afterOne - empty, precision: 1);
        Assert.Equal(lineHeight, afterTwo - afterOne, precision: 1);
        GC.KeepAlive(window);
    }

    // ---------------------------------------------------------------- [A13/T3a · a7 + fix-1 · P3] ready satırı

    // [A13/T3 fix-1 · B5] Sözlük yükleme ARTIK tek yerde (DsResources.Load) — buradaki kopya beşincisiydi ve
    // ham XamlReader.Load(stream) kullandığı için clr-namespace tamamlamasını atlıyordu.
    private static ResourceDictionary LoadTokens() => DsResources.Load("Tokens.xaml");

    /// <summary>Otoritedeki örnek damga (README §2.5 <c>12:04:07 ▮ ready</c>) — <c>RunViewModelTests</c>/
    /// <c>RunViewModelStateTests</c>'in <c>WallClock</c> tohumuyla aynı an.</summary>
    private static readonly DateTimeOffset IdleInstant = new(2026, 7, 23, 12, 4, 7, TimeSpan.Zero);



    // [KALDIRILDI — design v1.7.0 §2.5] Konsolun daktilosu, saat sütunu ve satır-bazlı kaskadı kaldırıldı;
    // bu iddiaların konusu artık yok. Yerlerine gelen davranış: satırlar anında basılır, prompt satırı yalnız
    // imleç + "ready" taşır, panel geçişi tek parça tilt-in'dir.

    private static double LeftEdge(ConsoleView view, FrameworkElement element) =>
        element.TranslatePoint(new Point(0, 0), view).X;

    // [A13/T3a · a6] "build in progress ▮" imlecinin ölçü/ton testi KALDIRILDI: işaretin kendisi kaldırıldı
    // (gerekçe: The_project_log_page_has_no_build_in_progress_marker). Kalan tek blok imleç prompt satırınınkidir
    // ve o kendi testleriyle (a7 deseni) pinlidir.

    // ---------------------------------------------------------------- [A13/T3b · b9] ölçü/geometri

    /// <summary>[A13/T3b · b9] design-v1 README §2.5: "padding 8×12" (BuildApp.jsx:617 <c>padding: '8px 12px'</c>
    /// — CSS shorthand: dikey(top/bottom)=8, yatay(left/right)=12). WPF karşılığı iki-değerli
    /// <c>Thickness</c>: <c>"12,8"</c> = sol/sağ 12, üst/alt 8 (ConsoleView.xaml) — BİREBİR aynı bütçe,
    /// farklı yazım sırası. Testsizdi.
    ///
    /// <para>[A13/T3 fix-1 · C1] Realize eklendi (brief kural 5): salt DP okuması bir stil/şablonun Padding'i
    /// ezmesini GÖRMEZDİ. Gerçek yerleşimde bütçe, editörün metin alanının sol/üst kenarını kontrolün kendi
    /// kenarından TAM 12/8 dip içeri almış olmalıdır.</para></summary>
    [StaFact]
    public void Editor_padding_matches_the_design_v1_eight_by_twelve_budget()
    {
        var host = DsResources.NewHost();
        var view = new ConsoleView();
        var window = DsResources.Realize(host, view);
        view.AppendBatch("line1\n");
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));
        view.UpdateLayout();

        // [DEĞİŞEN KURAL] Sol/üst/sağ bütçe 12/8'dir ve DEĞİŞMEDİ. ALT kenar daha geniştir: prompt imleci
        // belgenin son satırında durur ve yatay kaydırma çubuğu çıktığında ona bitişik kalıyordu — imlecin
        // altında pay bırakılır.
        Assert.Equal(new Thickness(12, 8, 12, 14), view.Editor.Padding);

        // GERÇEK yerleşim: TextView'in sol/üst kenarı editörün kenarından padding kadar içeride.
        var textView = view.Editor.TextArea.TextView;
        Assert.True(textView.ActualWidth > 0, "AvalonEdit TextView hiç yerleşmedi — ölçüm önemsiz olurdu");
        var offset = textView.TranslatePoint(new Point(0, 0), view.Editor);
        Assert.Equal(12.0, offset.X, precision: 1);
        Assert.Equal(8.0, offset.Y, precision: 1);
        GC.KeepAlive(window);
    }
}
