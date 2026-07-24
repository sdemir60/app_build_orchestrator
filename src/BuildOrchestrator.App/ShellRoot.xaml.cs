using System.Windows;
using System.Windows.Controls;
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
    public ShellRoot()
    {
        InitializeComponent();
        PART_ColumnSplitter.DragCompleted += (_, _) => OnColumnDragCompleted();
        PART_LeftSplitter.DragCompleted += (_, _) => OnLeftDragCompleted();
        PART_RightSplitter.DragCompleted += (_, _) => OnRightDragCompleted();
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

    // ---- [E2/T10] Proje listesi boş-durum davetleri (görünürlük + Choose Folder kablajı MainWindow'da) ----
    /// <summary>[E2/T10] "Pick a repository…" daveti (title + subtitle + Choose Folder) — repo seçilmemişken.</summary>
    public UIElement ListInviteOverlay => PART_ListInvite;
    /// <summary>[E2/T10] "No projects found under this folder." — repo Sync'lendi ama 0 proje.</summary>
    public UIElement NoProjectsOverlay => PART_NoProjects;
    /// <summary>[E2/T10] Boş-durum daveti primary butonu — MainWindow klasör seçiciyi buna bağlar.</summary>
    public Button ChooseFolderButton => PART_ChooseFolder;

    /// <summary>[E2/T10] Liste boş-durum davetinin görünürlüğünü uygular (karar <see cref="ViewModels.ListInvite"/>'te
    /// verilir — SAF; burada YALNIZ uygulanır). PickRepository → invite paneli; NoProjects → 0-proje metni; None → ikisi de gizli.</summary>
    public void SetListInvite(ViewModels.ListInviteState state)
    {
        PART_ListInvite.Visibility = state == ViewModels.ListInviteState.PickRepository ? Visibility.Visible : Visibility.Collapsed;
        PART_NoProjects.Visibility = state == ViewModels.ListInviteState.NoProjects ? Visibility.Visible : Visibility.Collapsed;
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
