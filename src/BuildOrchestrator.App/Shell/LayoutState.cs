namespace BuildOrchestrator.App.Shell;

/// <summary>[T35] Pencere gövdesinin görünüm modu (design-v1 BuildApp.jsx:1410-1417).</summary>
public enum LayoutMode { Quad, List, Focus }

/// <summary>
/// [T35] 2×2 yerleşimin SAF durumu: aktif mod + üç split yüzdesi (kolon · sol-kolon satırı · sağ-kolon satırı).
/// UI/WPF bağımsız, test edilebilir. Değerler design-v1'den BİREBİR:
/// varsayılan <c>{quad, 50, 50, 50}</c> (BuildApp.jsx:1143); preset'ler (:1410-1417); clamp'ler
/// kolon 28..72 (:1394), satırlar 18..82 (:1399/:1404).
/// </summary>
public sealed record LayoutState(LayoutMode Mode, double ColPct, double LeftPct, double RightPct)
{
    public static LayoutState Default => new(LayoutMode.Quad, 50, 50, 50);

    /// <summary>Mod değiştir + o modun preset'ini uygula (BuildApp.jsx:1411): quad → 50/50/50; list → right 50
    /// (col/left korunur); focus → right 76 (col/left korunur).</summary>
    public LayoutState WithMode(LayoutMode m) => m switch
    {
        LayoutMode.Quad => this with { Mode = m, ColPct = 50, LeftPct = 50, RightPct = 50 },
        LayoutMode.List => this with { Mode = m, RightPct = 50 },
        LayoutMode.Focus => this with { Mode = m, RightPct = 76 },
        _ => this with { Mode = m },
    };

    public LayoutState WithCol(double pct) => this with { ColPct = Math.Clamp(pct, 28, 72) };
    public LayoutState WithLeft(double pct) => this with { LeftPct = Math.Clamp(pct, 18, 82) };
    public LayoutState WithRight(double pct) => this with { RightPct = Math.Clamp(pct, 18, 82) };
}
