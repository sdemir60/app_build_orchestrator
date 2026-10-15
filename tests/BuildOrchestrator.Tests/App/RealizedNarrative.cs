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
/// </summary>
internal sealed class RealizedNarrative : IDisposable
{
    private readonly Window _window;

    public RealizedNarrative(int lines)
    {
        View = new ConsoleView();
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

    /// <summary>Tekerleğin bir çentiği yukarı, imlecin altındaki metin alanında: önizleme, ardından ScrollViewer'ın kendi işlediği olay.</summary>
    public void WheelUp()
    {
        var textView = View.Editor.TextArea.TextView;
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        textView.RaiseEvent(preview);
        if (!preview.Handled)
            textView.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.MouseWheelEvent });
        DispatcherPump.DrainToIdle();
    }

    /// <summary><c>⌄ latest</c> tıklaması — pill'in kendi düğmesinden.</summary>
    public void ClickLatest()
    {
        View.Pill.PillButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
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
