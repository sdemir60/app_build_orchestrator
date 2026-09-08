namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Satır menüsünün (Build · Rebuild · Clean) nereye oturacağının SAF kararı — WPF'siz, ölçülebilir.
///
/// <para>Prototipte menü ⋯ düğmesine DEĞİL, listenin kendisine göre konumlanır: yatayda panelin sağ
/// kenarından 8px içeride (<c>BuildApp.jsx:609</c> <c>right: 8</c>), dikeyde satırın altına 3px binerek
/// (<c>:659</c> <c>wrap.offsetTop + wrap.offsetHeight - 3</c>) ve panelin görünür alanına 4px payla
/// kelepçelenerek (<c>:661-664</c>).</para>
///
/// <para><b>Neden ayrı bir sınıf:</b> "ne karar verildi" ile "nasıl çizildi" ayrıdır (ARCHITECTURE §22'nin
/// genel kuralı) — kelepçe aritmetiği bir <c>Popup</c> geri çağrısının içine gömülseydi ancak gerçek bir
/// pencere açarak sınanabilirdi.</para>
/// </summary>
internal static class RowMenuPlacement
{
    /// <summary>Menünün sağ kenarı ile panelin sağ kenarı arasındaki boşluk (BuildApp.jsx:609 `right: 8`).</summary>
    internal const double EdgeInset = 8;

    /// <summary>Menünün satırın alt kenarına BİNDİĞİ pay (BuildApp.jsx:659 `- 3`) — menü satıra bağlı görünür.</summary>
    internal const double RowOverlap = 3;

    /// <summary>Menü ile panelin görünür alanının kenarı arasında bırakılan pay (BuildApp.jsx:662-663).</summary>
    internal const double ViewportMargin = 4;

    /// <summary>Menünün SOL kenarının, satırın sol kenarına göre konumu — sağ kenarlar hizalanır.</summary>
    internal static double LeftInRow(double rowWidth, double menuWidth) => rowWidth - EdgeInset - menuWidth;

    /// <summary>
    /// Menünün ÜST kenarının, panelin görünür alanının üstüne göre konumu.
    /// <paramref name="rowTop"/> da aynı başlangıç noktasına göredir (satırın görünür alandaki yeri).
    ///
    /// <para>Panel menüden kısaysa kelepçenin iki ucu ters döner; prototip o durumda ÜST payı kazandırır
    /// (<c>Math.max(minTop, …)</c>) — menü aşağı taşar ama başlığı görünür kalır.</para>
    /// </summary>
    internal static double TopInViewport(double rowTop, double rowHeight, double menuHeight, double viewportHeight)
    {
        double wanted = rowTop + rowHeight - RowOverlap;
        double min = ViewportMargin;
        double max = Math.Max(min, viewportHeight - menuHeight - ViewportMargin);
        return Math.Max(min, Math.Min(wanted, max));
    }
}
