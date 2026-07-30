using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Graph;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App;

/// <summary>
/// [T35] Pencere gövdesi: 2×2 panel yerleşimi (graf · proje listesi · konsol · event stream), görünüm modları
/// (graf gizleme) ve <see cref="DsSplitter"/> ile split. SAF layout — VM/engine/console kablajı MainWindow'da
/// kalır ve buradaki parçalara (<see cref="ConsoleHeaderControl"/> vb.) erişir. Yerleşim durumu
/// <see cref="ApplyLayout"/> ile sürülür; kullanıcı etkileşimi (mod düğmesi / split sürükleme)
/// <see cref="LayoutChanged"/> ile dışarı (persist için) bildirilir.
/// </summary>
public partial class ShellRoot : UserControl
{
    // [E5/T46] Proje filtre input'u — PanelHeader.RightContent alt-namescope'unda olduğundan XAML'de adlanamaz;
    // referans RightContent DP'sinden alınır, Esc handler'ı kod-tarafı bağlanır.
    private readonly TextBox _projectFilter;

    // [A13/T2 · 2.3] PROJECTS başlığındaki kaldırılabilir filtre chip'i — LeftContent alt-namescope'unda
    // olduğundan XAML'de adlanamaz/handler bağlanamaz; kod-tarafı kurulur (filtre TextBox'ıyla AYNI desen).
    private readonly ToggleButton _filterChip;
    private readonly TextBlock _filterChipLabel;

    public ShellRoot()
    {
        InitializeComponent();
        _projectFilter = (TextBox)PART_ProjectsHeader.RightContent!; // XAML'de sabit atanır (InitializeComponent'te kurulur)
        _projectFilter.PreviewKeyDown += OnFilterKeyDown;
        (_filterChip, _filterChipLabel) = BuildFilterChip();
        SetFilterChip(null); // başlangıç durumu da TEK yoldan kurulur (gizli + aktif görünüme hazır)
        PART_ColumnSplitter.DragCompleted += (_, _) => OnColumnDragCompleted();
        PART_LeftSplitter.DragCompleted += (_, _) => OnLeftDragCompleted();
        PART_RightSplitter.DragCompleted += (_, _) => OnRightDragCompleted();
        // [E5/T47] Ayraçlar bir "resize separator"dır — ekran okuyucuya işlevleriyle adlanır (klavye ile
        // odaklanıp ok tuşlarıyla resize edilirler; bkz. DsSplitter a11y kararı). Ad, semantiği BİLEN katmanda
        // (burada) verilir — DsSplitter kendi rolünü bilmez.
        AutomationProperties.SetName(PART_ColumnSplitter, AccessibilityNames.ColumnSplitter);
        AutomationProperties.SetName(PART_LeftSplitter, AccessibilityNames.GraphListSplitter);
        AutomationProperties.SetName(PART_RightSplitter, AccessibilityNames.ConsoleStreamSplitter);
        ApplyLayout(LayoutState.Default);
    }

    /// <summary>En son uygulanan yerleşim durumu.</summary>
    public LayoutState Layout { get; private set; } = LayoutState.Default;

    /// <summary>Kullanıcı yerleşimi değiştirdiğinde (mod düğmesi ya da split sürükleme sonu) tetiklenir —
    /// MainWindow bunu <see cref="Shell.UiStateStore"/>'a yazar. <see cref="ApplyLayout"/> bunu TETİKLEMEZ
    /// (programatik/persist geri-yükleme sonsuz döngü yapmasın).</summary>
    public event EventHandler<LayoutState>? LayoutChanged;

    // ---- MainWindow'un VM/engine/console kablajı için açtığı parçalar (test de GraphHost/LeftSplitter okur) ----
    public GraphView GraphHost => PART_Graph;
    public DsSplitter LeftSplitter => PART_LeftSplitter;
    public DsSplitter RightSplitter => PART_RightSplitter;
    public DsSplitter ColumnSplitter => PART_ColumnSplitter;
    public ConsoleHeader ConsoleHeaderControl => PART_ConsoleHeader;
    public ConsoleView ConsoleViewControl => PART_ConsoleView;
    public Views.EventStreamView EventStreamControl => PART_EventStream; // [E4/T48] arbiter kablajı
    public StickyLayerList ProjectsList => PART_Projects;

    // ---- [E5/T46] Klavye kısayolları: filtre odağı (Ctrl+F) + popover Esc katmanı (MainWindow'un Esc zinciri) ----
    /// <summary>[test yüzeyi] Proje filtre input'u (Ctrl+F bunu odaklar; içindeki Esc yereldir).</summary>
    internal TextBox ProjectFilterBox => _projectFilter;

    /// <summary>[E5/T46] Ctrl+F → proje filtre input'una odak (BuildApp.jsx:1306-1309). Odak başarılıysa true.</summary>
    public bool FocusProjectFilter() => _projectFilter.Focus();

    /// <summary>[E5/T46] Açık bir branch/worktree popover'ı ya da build menüsü var mı — Esc zincirinin popover
    /// katmanı (MainWindow bunu <see cref="Shell.KeyboardShortcuts.ResolveEsc"/>'e verir).</summary>
    public bool AnyPopoverOpen => PART_ActionBar.AnyPopoverOpen;

    /// <summary>[E5/T46] Açık tüm popover/menüleri kapatır (Esc'in popover katmanı; BuildApp.jsx:1313).</summary>
    public void CloseAllPopovers() => PART_ActionBar.CloseAllPopovers();

    /// <summary>[E5/T46] Filtre input'undaki Esc YALNIZ sorguyu temizler + blur eder (BuildApp.jsx:1487
    /// <c>setQuery(''); blur(); stopPropagation()</c>) — global Esc zincirine SIZMAZ (handled). PreviewKeyDown'da
    /// yakalanır ki tuş hiç bubble etmesin.</summary>
    private void OnFilterKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        _projectFilter.Clear();  // Text="" → iki-yönlü binding ProjectQuery'yi temizler
        Keyboard.ClearFocus();   // blur — sonraki Esc artık global zincire (seçim temizleme) düşebilir
        e.Handled = true;        // stopPropagation: bu Esc dialog/popover/seçim katmanına ULAŞMAZ
    }

    // ---- [A13/T2 · 2.3] PROJECTS başlığındaki kaldırılabilir filtre chip'i ----

    /// <summary>
    /// design-v1 §2.4: <i>"…aktif filtre varsa kaldırılabilir chip (ör. <c>Failed ✕</c>)"</i>
    /// (<c>BuildApp.jsx:1492</c> — <c>DS.Chip active label={FILTER_LABELS[filter]} onRemove={…}</c>).
    ///
    /// <para>Taban <c>Ds.Chip</c>'tir (<see cref="DsChipFactory.Small"/> ile küçük ölçüler) — yeni bir chip
    /// stili İCAT EDİLMEZ. <c>IsChecked</c> KALICI olarak açıktır: chip'in tek anlamı "şu an bir filtre
    /// uygulanıyor"dur, yani DS'in <c>active</c> (amber) görünümü onun DOĞAL hâlidir; şeritteki momentary
    /// chip'lerin aksine tıklamada sıfırlanmaz — zaten tıklanınca chip tamamen KAYBOLUR.</para>
    ///
    /// <para><b>Kaldırma göstergesi</b> çizilmiş bir geometridir (<c>Icon.ChipRemove</c>), ham <c>✕</c>
    /// karakteri DEĞİL: repo'nun tek ikon stratejisi budur (T64) ve ham karakter emoji taramasına takılırdı.
    /// Tüm chip tıklanabilir olduğu için ✕'e tıklamak da filtreyi kaldırır — chip'in BAŞKA bir eylemi yoktur,
    /// bu yüzden iç içe ikinci bir buton (ve onun hit-test/odak karmaşası) GEREKSİZDİR.</para>
    /// </summary>
    private (ToggleButton Chip, TextBlock Label) BuildFilterChip()
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(label);

        var chip = DsChipFactory.Small(this, content);
        // [T2 fix-1 · I-B] ✕ chip'in ANİMASYONLU Foreground'unu izler (aktifken amber) — sabit bir token'a
        // bağlanmış bir ✕, chip'in aktif durumunu yalanlardı. Görsel IconVisual.BoundToForeground'dan gelir
        // (ActionBar'ın chip ikonlarıyla AYNI deyim; inline kopya YASAK) ve viewBox 16'dır — DS'in çizimi
        // (_ds_bundle.js:210-215) 16'lık ızgarada 10px'e ölçeklenir.
        var glyph = IconVisual.BoundToForeground(chip, "Icon.ChipRemove", RemoveGlyphSize, viewBox: 16);
        glyph.Margin = new Thickness(ChipContentGap, 0, 0, 0); // _ds_bundle.js:164 chip gap 6
        content.Children.Add(glyph);

        chip.Visibility = Visibility.Collapsed;                  // filtre yokken YOK
        chip.Margin = new Thickness(ChipContentGap, 0, 0, 0);    // `build-order` etiketinden sonra
        chip.ToolTip = AccessibilityNames.ClearFilterChip;
        AutomationProperties.SetName(chip, AccessibilityNames.ClearFilterChip);
        // LeftContent alt-namescope'unda ADLANAMAZ (MC3093) — referans, RightContent'teki filtre TextBox'ıyla
        // AYNI şekilde DP üzerinden alınır (XAML'de sabit atanır, InitializeComponent'te kurulur).
        ((StackPanel)PART_ProjectsHeader.LeftContent!).Children.Add(chip);
        return (chip, label);
    }

    private const double RemoveGlyphSize = 10; // _ds_bundle.js:210-211 — svg 10x10
    private const double ChipContentGap = 6;   // _ds_bundle.js:164 — chip gap 6

    /// <summary>[test yüzeyi + MainWindow kablajı] Başlıktaki filtre chip'i — tıklaması filtreyi kaldırır.</summary>
    internal ToggleButton ProjectFilterChip => _filterChip;

    /// <summary>[A13/T2 · 2.3] Chip'i aktif filtreye göre sürer; <paramref name="label"/> null ise chip gizlenir.
    /// Karar (hangi etiket) çağıranındır — <see cref="ViewModels.ProjectFilter.Label"/> TEK etiket kaynağıdır,
    /// burada yeni bir eşleme tablosu KOPYALANMAZ.</summary>
    public void SetFilterChip(string? label)
    {
        _filterChipLabel.Text = label ?? "";
        _filterChip.Visibility = label is null ? Visibility.Collapsed : Visibility.Visible;
        // [T2 fix-1 · I-A] `IsChecked` HER tazelemede geri kurulur, yalnız ctor'da BİR KEZ değil. Chip bir
        // ToggleButton'dır: GERÇEK bir tıklama ButtonBase.OnClick → ToggleButton.OnToggle zincirini koşturur ve
        // IsChecked'i false'a çevirir. Ds.Chip'in amber görünümü TAMAMEN IsChecked=True trigger'ına bağlı
        // olduğundan (Controls.xaml:328-332), chip ilk gerçek tıklamadan sonra kalıcı olarak pasif-griye
        // düşüyordu. Bu chip'in "kapalı" bir durumu YOKTUR — göründüğü her an aktiftir.
        _filterChip.IsChecked = true;
    }

    // ---- [E2/T10] Proje listesi boş-durum davetleri (görünürlük + Choose Folder kablajı MainWindow'da) ----
    /// <summary>[E2/T10] "Pick a repository…" daveti (title + subtitle + Choose Folder) — repo seçilmemişken.</summary>
    public UIElement ListInviteOverlay => PART_ListInvite;
    /// <summary>[E2/T10] "No projects found under this folder." — repo Sync'lendi ama 0 proje.</summary>
    public UIElement NoProjectsOverlay => PART_NoProjects;
    /// <summary>[E2/T10] Boş-durum daveti primary butonu — MainWindow klasör seçiciyi buna bağlar.</summary>
    public Button ChooseFolderButton => PART_ChooseFolder;

    /// <summary>[E2/T10] Liste boş-durum davetinin görünürlüğünü uygular (karar <see cref="ViewModels.ListInvite"/>'te
    /// verilir — SAF; burada YALNIZ uygulanır). PickRepository → invite paneli; NoProjects → 0-proje metni;
    /// [A13/T2 · 2.4] NoFilterMatch → "filtre eşleşmedi" metni; None → hepsi gizli. Durumlar birbirini DIŞLAR.</summary>
    public void SetListInvite(ViewModels.ListInviteState state)
    {
        PART_ListInvite.Visibility = state == ViewModels.ListInviteState.PickRepository ? Visibility.Visible : Visibility.Collapsed;
        PART_NoProjects.Visibility = state == ViewModels.ListInviteState.NoProjects ? Visibility.Visible : Visibility.Collapsed;
        PART_NoFilterMatch.Visibility = state == ViewModels.ListInviteState.NoFilterMatch ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Yerleşim durumunu görsele uygular: kolon/satır star oranları + graf/ayraç görünürlüğü.
    /// list/focus modunda graf VE onun altındaki yatay ayraç gizlenir (satırları 0'a çöker).</summary>
    public void ApplyLayout(LayoutState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Layout = state;

        LeftColumn.Width = Star(state.ColPct);
        RightColumn.Width = Star(100 - state.ColPct);

        ConsoleRow.Height = Star(state.RightPct);
        StreamRow.Height = Star(100 - state.RightPct);

        bool showGraph = state.Mode == LayoutMode.Quad;
        GraphHost.Visibility = showGraph ? Visibility.Visible : Visibility.Collapsed;
        LeftSplitter.Visibility = showGraph ? Visibility.Visible : Visibility.Collapsed;
        if (showGraph)
        {
            GraphRow.Height = Star(state.LeftPct);
            LeftSplitterRow.Height = GridLength.Auto;
            ProjectsRow.Height = Star(100 - state.LeftPct);
        }
        else
        {
            GraphRow.Height = new GridLength(0);
            LeftSplitterRow.Height = new GridLength(0);
            ProjectsRow.Height = Star(100);
        }
    }

    /// <summary>Görünüm modunu değiştirir (preset uygular) ve değişimi dışarı bildirir.</summary>
    public void SetMode(LayoutMode mode) => Commit(Layout.WithMode(mode));

    private void Commit(LayoutState state)
    {
        ApplyLayout(state);
        LayoutChanged?.Invoke(this, state);
    }

    private void OnColumnDragCompleted()
    {
        double left = LeftColumn.ActualWidth, right = RightColumn.ActualWidth;
        if (left + right <= 0) return;
        Commit(Layout.WithCol(left / (left + right) * 100));
    }

    private void OnLeftDragCompleted()
    {
        double top = GraphRow.ActualHeight, bottom = ProjectsRow.ActualHeight;
        if (top + bottom <= 0) return;
        Commit(Layout.WithLeft(top / (top + bottom) * 100));
    }

    private void OnRightDragCompleted()
    {
        double top = ConsoleRow.ActualHeight, bottom = StreamRow.ActualHeight;
        if (top + bottom <= 0) return;
        Commit(Layout.WithRight(top / (top + bottom) * 100));
    }

    private static GridLength Star(double weight) => new(Math.Max(0, weight), GridUnitType.Star);
}
