using System.Windows.Controls;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [tray menu look] İkonlu menü maddesi: Icons.xaml'deki bir geometriyi (<see cref="IconKey"/>) bir token fırçasıyla
/// (<see cref="IconBrushKey"/>) <c>Ds.ContextMenu.Item</c> şablonunun ikon yuvasına koyar. Görseli <see cref="IconVisual.Make"/> kurar;
/// boya ikonun kendi kuralıyla çözülür — konturlu ikon konturla, dolu ikon dolguyla (<see cref="IconPaint"/>). İkinci bir ikon deyimi
/// YAZILMAZ.
///
/// <para><b>İkon şablon uygulanırken kurulur, kurucuda değil:</b> madde o anda menünün ağacındadır ve boya oradaki kaynaklardan çözülür.
/// Kurucuda madde henüz hiçbir kaynak kapsamında değildir; üretimde uygulama sözlükleri bunu örterdi, ama menüyü kendi kaynak
/// kapsamında realize eden test (<c>TrayMenuLookTests</c>) ikonu boyasız görürdü.</para>
/// </summary>
internal sealed class IconMenuItem : MenuItem
{
    /// <summary>İkonun Icons.xaml anahtarı (ör. <c>Icon.Stop</c>).</summary>
    public required string IconKey { get; init; }

    /// <summary>İkonun fırça token'ı (ör. <c>Brush.TextDim</c>).</summary>
    public required string IconBrushKey { get; init; }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Icon ??= IconVisual.Make(this, IconKey, IconBrushKey, MenuRowMetrics.IconSlot);
    }
}
