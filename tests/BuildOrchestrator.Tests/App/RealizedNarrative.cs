using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Gerçek (ekran dışı) bir pencerede, dibe sabitlenmiş YENİDEN KURULAN anlatı (← Back / tepsiden dönüş) ve okuyucunun GERÇEK
/// gezinmesi: tuşlar — <c>UserScrollSignal</c>'in dinlediği ham girdi ve editörün KENDİ bağlaması —, tekerlek ve <c>⌄ latest</c>
/// tıklaması; her adımdan sonra dispatcher boşa kadar pompalanır.
///
/// <para>Neden gerçek pencere: kaydırma olayının içindeki geometri (hangi ofsetin o anda henüz eski olduğu) yalnız yerleşimi
/// üretimdeki gibi koşan bir host'ta üretimle aynıdır; tuş ve tekerlek olayları da bir <see cref="PresentationSource"/> ister.</para>
///
/// <para>Bottom-anchor'ın zamanlayıcısı (bekleme dönüşü, atlama penceresi) konsolun seam'inden yakalanır
/// (<c>ConsoleView.AnchorScheduleOnce</c>): hiçbir şey duvar saatiyle kendiliğinden dolmaz, test <see cref="ElapseAnchorTimers"/>
/// ile doldurur.</para>
/// </summary>
internal sealed class RealizedNarrative : IDisposable
{
    private readonly Window _window;
    private readonly List<Action> _anchorTimers = [];

    public RealizedNarrative(int lines)
    {
        View = new ConsoleView { AnchorScheduleOnce = (_, callback) => _anchorTimers.Add(callback) };
        _window = DsResources.Realize(DsResources.NewHost(), View, 800, 600);
        Store = new NarrativeStore();
        foreach (int i in Enumerable.Range(0, lines)) Store.Append($"line{i}");
        View.ReplaceRunDocument(NarrativeSnapshots.Of(Store));
        DispatcherPump.DrainToIdle();
    }

    public ConsoleView View { get; }

    /// <summary>Anlatının sahibi — canlı satırlar önce buraya yazılır (<see cref="NarrativeSnapshots.Deliver"/>).</summary>
    public NarrativeStore Store { get; }

    /// <summary>Okuyucunun dibe uzaklığı (px), yerleşim oturmuşken.</summary>
    public double DistanceFromBottom =>
        View.Editor.ExtentHeight - View.Editor.VerticalOffset - View.Editor.ViewportHeight;

    /// <summary>Ctrl+Home: ham tuş ve editörün KENDİ Ctrl+Home bağlaması (<c>MoveToDocumentStart</c>). Bağlama komutundan
    /// çalıştırılır: testte Ctrl basılı olamaz ve <c>KeyGesture</c> klavyenin gerçek durumunu okur — KeyDown onu eşleştirmezdi.</summary>
    public void PressCtrlHome()
    {
        var textArea = View.Editor.TextArea;
        var binding = textArea.InputBindings.OfType<KeyBinding>()
            .Single(k => k.Key == Key.Home && k.Modifiers == ModifierKeys.Control);
        RaiseKey(Key.Home, Keyboard.PreviewKeyDownEvent);
        ((RoutedCommand)binding.Command).Execute(binding.CommandParameter, textArea);
        DispatcherPump.DrainToIdle();
    }

    /// <summary>Değiştiricisiz bir tuş: önizleme, ardından gerçek KeyDown — editörün bağlamasını <c>CommandManager</c> eşleştirir.</summary>
    public void Press(Key key)
    {
        if (!RaiseKey(key, Keyboard.PreviewKeyDownEvent)) RaiseKey(key, Keyboard.KeyDownEvent);
        DispatcherPump.DrainToIdle();
    }

    /// <summary>Tekerleğin bir çentiği yukarı, imlecin altındaki metin alanında (<see cref="MouseInput.Wheel"/>).</summary>
    public void WheelUp()
    {
        MouseInput.Wheel(View.Editor.TextArea.TextView, Mouse.MouseWheelDeltaForOneLine);
        DispatcherPump.DrainToIdle();
    }

    /// <summary><c>⌄ latest</c> tıklaması — pill'in kendi düğmesinden.</summary>
    public void ClickLatest()
    {
        View.Pill.PillButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        DispatcherPump.DrainToIdle();
    }

    /// <summary>Okuyucu elini çekti ve süre geçti: anchor'ın o ana dek kurduğu zamanlayıcılar KURULDUKLARI sırayla, gecikmeleri yok
    /// sayılarak çalışır (eskimiş bir bekleme kendi kuşak sayacıyla hiçbir şey yapmaz); bir geri çağırmanın kurduğu zamanlayıcı bu
    /// çağrıda çalışmaz, sonrakine kalır.</summary>
    public void ElapseAnchorTimers()
    {
        var due = _anchorTimers.ToList();
        _anchorTimers.Clear();
        foreach (var callback in due) callback();
        DispatcherPump.DrainToIdle();
    }

    public void Dispose() => _window.Close();

    private bool RaiseKey(Key key, RoutedEvent routedEvent)
    {
        var textArea = View.Editor.TextArea;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(textArea)!, 0, key) { RoutedEvent = routedEvent };
        textArea.RaiseEvent(args);
        return args.Handled;
    }
}
