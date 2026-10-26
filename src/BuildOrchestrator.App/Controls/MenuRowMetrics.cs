using System.Windows;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Menü satırlarının ölçüleri — TEK kaynak. Proje satırının menüsü (<c>Views/ProjectRowMenu</c>: Build · Rebuild · Clean) ve tepsinin
/// sağ tık menüsü (<c>Ds.ContextMenu</c>, Controls.xaml) AYNI satırı çizer; iki yerde ayrı yazılan bir ölçü, biri düzeltilip öteki
/// unutulunca iki menüyü ayrıştırırdı (kopya YASAK).
///
/// <para>Değerler satır menüsünün kaynağındandır (design-v1.11.0 BuildApp.jsx:599-614) ve token DEĞİLDİR — bileşenin kendi ölçüleri
/// (ARCHITECTURE §14). Sınıf ve üyeleri public'tir: Controls.xaml onları <c>x:Static</c> ile okur ve test yükleyicisi o sözlüğü gevşek
/// XAML olarak açar, internal'a erişemez (<see cref="AppTooltip"/> / <see cref="StartMode"/> deseni).</para>
/// </summary>
public static class MenuRowMetrics
{
    /// <summary>Menünün genişliği.</summary>
    public const double MenuWidth = 154;

    /// <summary>Kabuğun iç payı: satırlar kabuğun kenarından bu kadar içeride durur.</summary>
    public static readonly Thickness MenuPadding = new(4);

    /// <summary>Satırın yüksekliği.</summary>
    public const double RowHeight = 26;

    /// <summary>Satırın iç payı: ikon ve metin satırın kenarından bu kadar içeride durur.</summary>
    public static readonly Thickness RowPadding = new(7, 0, 7, 0);

    /// <summary>Kare ikon yuvası: ikon bu kutuya ortalanır.</summary>
    public const double IconSlot = 13;

    /// <summary>İkon yuvası ile metin arasındaki boşluk.</summary>
    public static readonly Thickness LabelMargin = new(8, 0, 0, 0);

    /// <summary>Pasif maddenin opaklığı — prototipin <c>busy</c> hâli (BuildApp.jsx:609 <c>opacity: busy ? 0.45 : 1</c>).</summary>
    public const double DisabledOpacity = 0.45;
}
