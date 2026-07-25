using System.Windows;
using System.Windows.Controls;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T35] design-v1 PanelHead (BuildApp.jsx:224-236) — panellerin 28px caps başlığı. <see cref="Text"/> caps
/// etiketi (<see cref="TrackedTextBlock"/> varsayılanları: 11px/text-faint/uppercase/tracking-caps), etiketin
/// yanına <see cref="LeftContent"/>, sağa dayalı <see cref="RightContent"/> (mono mod etiketi, filtre inputu…).
/// </summary>
public partial class PanelHeader : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(PanelHeader),
        new PropertyMetadata(string.Empty, (d, e) => ((PanelHeader)d).LabelText.Text = (string)e.NewValue));

    public static readonly DependencyProperty LeftContentProperty = DependencyProperty.Register(
        nameof(LeftContent), typeof(object), typeof(PanelHeader),
        new PropertyMetadata(null, (d, e) => ((PanelHeader)d).LeftSlot.Content = e.NewValue));

    public static readonly DependencyProperty RightContentProperty = DependencyProperty.Register(
        nameof(RightContent), typeof(object), typeof(PanelHeader),
        new PropertyMetadata(null, (d, e) => ((PanelHeader)d).RightSlot.Content = e.NewValue));

    public PanelHeader() => InitializeComponent();

    /// <summary>Caps etiket metni (ör. <c>PROJECTS</c>, <c>EVENT STREAM</c>).</summary>
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    /// <summary>Etiketin hemen yanındaki içerik (ör. mono <c>build-order</c> mod etiketi).</summary>
    public object? LeftContent { get => GetValue(LeftContentProperty); set => SetValue(LeftContentProperty, value); }

    /// <summary>Sağa dayalı içerik (ör. <c>Filter…</c> inputu).</summary>
    public object? RightContent { get => GetValue(RightContentProperty); set => SetValue(RightContentProperty, value); }
}
