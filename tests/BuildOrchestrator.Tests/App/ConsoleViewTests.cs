using System.IO;
using System.Windows;
using System.Windows.Markup;
using ICSharpCode.AvalonEdit;
using BuildOrchestrator.App.Console;

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
        Assert.True(view.Document.LineCount <= ConsoleView.RenderSliceLines + 1,
            $"belge satır sayısı ({view.Document.LineCount}) render dilimini aşmamalı");
        Assert.Contains("line249", view.Document.Text); // en yeni korunur
        Assert.DoesNotContain("line0\n", view.Document.Text); // en eski kırpıldı
    }

    // ---------------------------------------------------------------- [3b] kaskat (reduced-motion instant yolu)

    [StaFact]
    public void PlayCascade_shows_all_lines_when_reduced_motion_instant()
    {
        // Headless testte App.Motion null → animationsEnabled=false → kaskat INSTANT (tüm satırlar, fade yok).
        var view = new ConsoleView();

        view.PlayCascade(new[] { "a", "b", "c" }, buildInProgress: false);

        Assert.Equal("a\nb\nc\n", view.Document.Text);
    }

    // ---------------------------------------------------------------- [A13/T3a · a6] "build in progress ▮" (BİREBİR)

    /// <summary>[A13/T3a · a6] design-v1 §2.5: building bir projenin logunda kaskat sonunda amber
    /// <c>build in progress ▮</c> belirir (ConsoleView.xaml:34 <c>BuildProgressText</c>). Metin testsizdi.</summary>
    [StaFact]
    public void PlayCascade_with_building_project_shows_the_verbatim_build_in_progress_overlay()
    {
        var view = new ConsoleView();

        view.PlayCascade(new[] { "log line" }, buildInProgress: true);

        Assert.Equal(Visibility.Visible, view.BuildProgressOverlay.Visibility);
        Assert.Equal("build in progress", view.BuildProgressText.Text);
    }

    // ---------------------------------------------------------------- [3b I-2] chunk loader GERÇEK yolu

    [StaFact]
    public void Chunk_scroll_to_top_prepends_previous_slice_contiguously_and_compensates_offset()
    {
        // GERÇEK yol: PlayCascade render dilimini (son 200) kurar; arm (tepeden uzaklaş) → scroll-to-top →
        // ConsoleView.PrependPreviousChunk contiguous eski dilimi prepend eder + VerticalOffset'i telafi eder.
        var view = new ConsoleView();
        // Layout: TextView.DefaultLineHeight/VerticalOffset gerçek değer alsın (offset telafisi ölçülebilsin).
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));
        view.UpdateLayout();

        var all = Enumerable.Range(0, 250).Select(i => $"line{i}").ToArray();
        view.PlayCascade(all, buildInProgress: false); // instant → son 200 (line50..line249)
        Assert.StartsWith("line50\n", view.Document.Text);
        Assert.DoesNotContain("line49\n", view.Document.Text); // ilk 50 henüz chunk loader'da

        view.EvaluateChunkScroll(100.0); // arm: kullanıcı tepeden uzaklaştı (aşağı kaydırdı)
        view.EvaluateChunkScroll(0.0);   // scroll-to-top → önceki chunk prepend edilir

        // Dikiş: line0..line249 bitişik ve TAM — tekrar YOK, kayıp YOK.
        var expected = string.Concat(all.Select(l => l + "\n"));
        Assert.Equal(expected, view.Document.Text);

        // Offset prepend edilen 50 satırın piksel yüksekliği kadar telafi edildi (viewport zıplamaz).
        Assert.NotNull(view.LastPrepend);
        var (before, delta, applied) = view.LastPrepend!.Value;
        Assert.True(delta > 0, $"prepend edilen 50 satırın piksel yüksekliği > 0 olmalı (delta={delta})");
        Assert.Equal(before + delta, applied, 3); // ChunkStitch.CompensatedOffset wiring

        // Re-arm + tekrar tepe: yüklenecek daha eski satır yok → idempotent (tekrar yükleme/dup YOK).
        view.EvaluateChunkScroll(100.0);
        view.EvaluateChunkScroll(0.0);
        Assert.Equal(expected, view.Document.Text);
    }

    // ---------------------------------------------------------------- [3b M-2] proje modu follow tail-trim

    [StaFact]
    public void Project_mode_following_document_stays_capped_at_the_render_slice()
    {
        // Alta-yapışık (follow) proje logu chatty bir build'de akarken belge render dilimini AŞMAZ.
        var view = new ConsoleView();
        view.PlayCascade(new[] { "seed" }, buildInProgress: true); // _projectMode=true, StickToBottom=true (varsayılan)

        for (int i = 0; i < 400; i++) view.AppendBatch($"live{i}\n");

        Assert.True(view.Document.LineCount <= ConsoleView.RenderSliceLines + 1,
            $"follow'da belge satır sayısı ({view.Document.LineCount}) render dilimini aşmamalı");
        Assert.Contains("live399", view.Document.Text);   // en yeni korunur
        Assert.DoesNotContain("live0\n", view.Document.Text); // en eski düştü
    }

    [StaFact]
    public void Project_mode_scrolled_up_document_is_not_trimmed()
    {
        // Kullanıcı yukarı kaydırıp chunk gezerken (StickToBottom=false) tail-trim YOK — prepend'le çakışmaz.
        var view = new ConsoleView();
        view.PlayCascade(new[] { "seed" }, buildInProgress: true);
        view.StickToBottom = false;

        for (int i = 0; i < 400; i++) view.AppendBatch($"live{i}\n");

        Assert.True(view.Document.LineCount > ConsoleView.RenderSliceLines,
            "scroll-up (browse) durumunda belge kırpılmamalı");
        Assert.Contains("live0\n", view.Document.Text); // eski satırlar korunur (chunk gezme bozulmaz)
    }

    // ---------------------------------------------------------------- [3b C-1] follow-trim + scroll-to-top: delik yok

    [StaFact]
    public void Project_mode_follow_trim_then_scroll_to_top_recovers_backlog_without_a_hole()
    {
        // [C-1 regression] Follow-trim, proje modunda belge tepesinden satır siler; bu, chunk loader'ın
        // _loadedFrom index'ini de ilerletmeli. Aksi halde sonraki scroll-to-top prepend'i STALE index'e karşı
        // YANLIŞ dilimi yükler → kırpılan satırlar KALICI kaybolur (delik) ve _loadedFrom onları "yüklü" sandığı
        // için geri getirilemez. Reviewer repro şekli: _loadedFrom>0 olan bir kaskat + çok sayıda canlı append
        // (follow aktif) + tepeye kaydırma. Layout: offset telafisi ölçülebilsin diye.
        var view = new ConsoleView();
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));
        view.UpdateLayout();

        // 300 satır kaskat → render dilimi son 200 (orig100..orig299), _loadedFrom=100 (backlog: orig0..orig99).
        var all = Enumerable.Range(0, 300).Select(i => $"orig{i}").ToArray();
        view.PlayCascade(all, buildInProgress: true); // instant (headless), _projectMode, StickToBottom=true (varsayılan)
        Assert.StartsWith("orig100\n", view.Document.Text);
        Assert.DoesNotContain("orig99\n", view.Document.Text); // ilk 100 chunk loader backlog'unda

        // Chatty canlı build: follow aktifken 250 satır append → tail-trim TÜM orijinal satırları belgeden atar.
        for (int i = 0; i < 250; i++) view.AppendBatch($"live{i}\n");
        string liveTail = view.Document.Text;              // belgede kalan salt-live kuyruk (orijinaller kırpıldı)
        Assert.Contains("live249\n", liveTail);            // en yeni korunur
        Assert.DoesNotContain("orig", liveTail);           // tüm orijinaller belgeden kırpıldı (backlog'a düştü)

        // Kullanıcı yukarı kaydırır: arm → scroll-to-top → önceki chunk prepend edilir.
        view.EvaluateChunkScroll(100.0); // arm (tepeden uzaklaş)
        view.EvaluateChunkScroll(0.0);   // scroll-to-top → önceki chunk

        // (a) DELİK YOK: prepend, mevcut live kuyruğun ÖNÜNE TAM olarak orig100..orig299'u (kırpılan backlog'un
        // sonu) dikmeli — kuyruk aynen korunur, araya kayıp/tekrar girmez. STALE index bug'ında _loadedFrom=100
        // kalır → from=100-200→0 hesaplanır, orig0..orig99 yüklenir ve orig100..orig299 KALICI kaybolur (delik).
        string expectedAfterFirst = string.Concat(Enumerable.Range(100, 200).Select(i => $"orig{i}\n")) + liveTail;
        Assert.Equal(expectedAfterFirst, view.Document.Text); // bug'da orig0.. yüklenir → eşitlik tutmaz (RED)

        // (b) VerticalOffset prepend edilen dilimin piksel yüksekliği kadar telafi edildi (viewport zıplamaz).
        Assert.NotNull(view.LastPrepend);
        var (before, delta, applied) = view.LastPrepend!.Value;
        Assert.True(delta > 0, $"prepend edilen dilimin piksel yüksekliği > 0 olmalı (delta={delta})");
        Assert.Equal(before + delta, applied, 3); // ChunkStitch.CompensatedOffset wiring

        // Tekrar tepeye kaydır: kalan backlog (orig0..orig99) da geri gelir → HİÇBİR satır kalıcı kayıp değil,
        // belge orig0..orig299 + live kuyruğu olarak TAM ve bitişik (contiguous).
        view.EvaluateChunkScroll(100.0);
        view.EvaluateChunkScroll(0.0);
        string expectedAfterSecond = string.Concat(Enumerable.Range(0, 300).Select(i => $"orig{i}\n")) + liveTail;
        Assert.Equal(expectedAfterSecond, view.Document.Text);
    }

    // ---------------------------------------------------------------- [I-1] gerçek OnScrollOffsetChanged yolu

    [StaFact]
    public void Real_OnScrollOffsetChanged_path_prepends_on_jump_to_top_and_releases_stuck_without_data_loss()
    {
        // [I-1] EvaluateChunkScroll(offset) çağıran testlerin AKSİNE (yukarıdakiler), bu test AvalonEdit'in
        // gerçek ScrollOffsetChanged'ine kablı OLAN, üretimin ta kendisi ConsoleView.OnScrollOffsetChanged'i
        // ÇAĞIRIR (paralel bir kopya yol DEĞİL — internal, EvaluateChunkScroll'daki AYNI gerekçeyle: canlı bir
        // scroll event'i beklemeden GERÇEK metodu tetikleyebilmek). Amaç: bottom-anchor'ın IsStuck yeniden-hesabı
        // ile chunk-loader'ın prepend'i AYNI olayda (kullanıcı dipteyken tek hamlede tepeye/Ctrl+Home) çakıştığında
        // (a) prepend'in delik BIRAKMADIĞINI (backlog bitişik) VE (b) kullanıcının artık dipte OLMADIĞININ doğru
        // yansıtıldığını (StickToBottom=false — CompensatedOffset'in üzerine YANLIŞLIKLA "dibe git" yazılmadığının
        // dolaylı kanıtı: bkz. BottomAnchorBehaviorTests.Growth_arriving_after_IsStuck_was_freshly_released_...)
        // doğrular.
        //
        // [Önemli — dürüstlük notu] Deneysel olarak doğrulandı: AvalonEdit'in ExtentHeight/VerticalOffset'i BU
        // headless/offscreen host'ta document.Insert/ScrollToVerticalOffset'ten SONRA, araya GERÇEK bir layout
        // pass girmeden senkron YANSIMAZ (bu yüzden aşağıda kaydırmanın ardından UpdateLayout çağrılır) — I-1'in
        // tarif ettiği "aynı senkron çağrı içinde post-prepend extent'in stale-true IsStuck'a sızması" tam olarak
        // BU testte yeniden üretilemiyor. Gerçek mekanizma yalnız AvalonEdit'in KENDİ iç re-entrant event'i
        // üzerinden tetiklenebilir, ki bu headless bir StaFact'te DETERMİNİSTİK olarak zorlanamıyor. Bu yüzden bu
        // test — GERÇEK yolu TAMAMEN test DIŞI bırakmamak için — sözleşmeyi (delik yok + doğru un-stick) doğrular;
        // I-1'in SIRA-bağımlı koruması ayrıca BottomAnchorBehaviorTests'teki odaklı guard-logic testiyle kanıtlanır.
        //
        // [DEĞİŞEN ÖN-KOŞUL] Test eskiden panelin geçişten sonra TESADÜFEN tepede (offset≈19) kalmasına
        // dayanıyordu — ölçüldü. O bir kusurdu: design §2.5 mod geçişinde dibe pinlenmeyi ister ve pin artık
        // deterministik (ConsoleView.PinToBottomAfterModeSwitch). Senaryo bu yüzden tepeye zıplamayı GERÇEKTEN
        // yapar: kullanıcının ham jesti + gerçek kaydırma.
        var view = new ConsoleView();
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));
        view.UpdateLayout();

        // 300 satır kaskat → render dilimi son 200 (orig100..orig299), _loadedFrom=100 (backlog: orig0..orig99).
        var all = Enumerable.Range(0, 300).Select(i => $"orig{i}").ToArray();
        view.PlayCascade(all, buildInProgress: true); // _projectMode, StickToBottom=true (varsayılan/forced)
        view.UpdateLayout();

        // Kaskat kullanıcıyı dibe pinler (design §2.5) — chunk latch'i de üretimdeki gibi orada kurulur.
        Assert.Equal(view.Editor.ExtentHeight - view.Editor.ViewportHeight, view.Editor.VerticalOffset, 1);
        view.OnScrollOffsetChanged();
        Assert.True(view.StickToBottom, "senaryo ön-koşulu: kullanıcı dipteyken IsStuck=true");

        // TEK hamlede tepeye (Ctrl+Home): kullanıcının HAM jesti — üretimin dinlediği kanal — VE gerçek
        // kaydırma; ardından üretimin scroll handler'ı senkron tetiklenir. Jest olmadan takip BIRAKILMAZ:
        // takibi yalnız kullanıcı bırakır (bkz. BottomAnchorDecision.OnScrollChanged `userDriven`).
        UserScrollGesture.Raise(view);
        view.Editor.ScrollToVerticalOffset(0);
        view.UpdateLayout(); // bu host'ta kaydırma ancak bir yerleşim geçişinden SONRA offset'e yansır (ölçüldü)
        view.OnScrollOffsetChanged();

        // (a) Delik yok: prepend gerçekleşti, backlog (orig0..orig99) render dilimine (orig100..orig299) bitişik
        // dikildi — tekrar/kayıp yok.
        var expected = string.Concat(all.Select(l => l + "\n"));
        Assert.Equal(expected, view.Document.Text);
        Assert.NotNull(view.LastPrepend);
        var (before, delta, applied) = view.LastPrepend!.Value;
        Assert.True(delta > 0, $"prepend edilen dilimin piksel yüksekliği > 0 olmalı (delta={delta})");
        Assert.Equal(before + delta, applied, 3); // ChunkStitch.CompensatedOffset wiring — CompensatedOffset OTORİTE

        // (b) Kullanıcı artık dipte SAYILMIYOR (StickToBottom=false) — I-1'in ana iddiası: prepend'in kendi
        // büyümesi stale-true bir IsStuck'ı YANLIŞLIKLA "dipte kal" olarak yorumlayıp CompensatedOffset'i ezip
        // dibe YANKLAMAMALI. StickToBottom hâlâ true kalsaydı bu, kullanıcının az önce tepeye kaydırdığı GERÇEĞİYLE
        // ÇELİŞirdi (ve bir sonraki içerik büyümesinde konsol onu TEKRAR dibe fırlatırdı).
        Assert.False(view.StickToBottom);
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

    // ---------------------------------------------------------------- [A13/T3a · a6] "build in progress ▮" imleci

    /// <summary>[A13/T3 fix-1 · C2] a6 kaleminin metni <c>build in progress ▮</c> idi; pinlenen yalnız gövde
    /// metniydi. <c>▮</c> ayrı bir <see cref="Rectangle"/>'dır (<c>ConsoleView.xaml BuildProgressCursor</c>, 7×13,
    /// amber) — silinse ya da başka bir fırçaya bağlansa süit yeşil kalırdı. Ton
    /// (<c>Brush.AmberText</c>) a7'nin <c>ActiveCursor.Fill</c> deseninin birebir kardeşidir.</summary>
    [StaFact]
    public void The_build_in_progress_overlay_ends_with_the_amber_block_cursor()
    {
        var host = DsResources.NewHost();
        var view = new ConsoleView();
        var window = DsResources.Realize(host, view);

        view.PlayCascade(new[] { "log line" }, buildInProgress: true);
        view.UpdateLayout();

        Assert.Equal(Visibility.Visible, view.BuildProgressOverlay.Visibility);
        Assert.Same(view.FindResource("Brush.AmberText"), view.BuildProgressCursor.Fill);
        Assert.Equal(7.0, view.BuildProgressCursor.ActualWidth);
        Assert.Equal(13.0, view.BuildProgressCursor.ActualHeight);
        GC.KeepAlive(window);
    }

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
