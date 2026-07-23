using BuildOrchestrator.App.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D3/T?] Event stream'in tek satırı (design-v1 <c>StreamRow</c>, BuildApp.jsx:627-659). Saf gözlemlenebilir
/// VM-state; glyph/renk eşlemesi burada TEK yerde (görünüm kopyalamaz). Aktif satır AYRI bir yapıdır — bu tip
/// yalnız tampon (anlatı) satırlarını temsil eder.
/// </summary>
public sealed partial class StreamEventViewModel : ObservableObject
{
    public long Id { get; }
    public string Time { get; }
    public StreamKind Kind { get; }
    /// <summary>Tıklanabilir/seçilebilir satırlar bir projeye bağlıdır (ok/fail/skip); sync/info/done <c>null</c>.</summary>
    public string? ProjectId { get; }
    public string Text { get; }
    /// <summary>Fırtına ya da hata nedeniyle ANINDA basılmalı mı (daktilo yok). Reduced-motion görünümde ayrıca
    /// zorlar — bu bayrağa dahil değildir (prototip <c>TypingLine</c> REDUCED'ı ayrı ele alır).</summary>
    public bool Instant { get; }

    /// <summary>Yalnız <c>done</c> + hatasız satır parlamaya UYGUNDUR (BuildApp.jsx:643): <c>Brush.StatusSuccessSoft</c>
    /// → şeffaf, 1.1s, BİR KEZ.</summary>
    public bool GlowEligible { get; }

    /// <summary>[A13.2] Parıltı BİR KEZ oynanır — container recycle/yeniden-bağlanma onu TEKRAR oynatmasın diye
    /// görünüm bunu oynattıktan sonra true yapar ve her denemede kontrol eder. Gözlemlenebilir DEĞİL (görünümün
    /// tek-yönlü guard'ı; binding tetiklemez).</summary>
    public bool GlowPlayed { get; set; }

    /// <summary>En yeni satır daktiloyla mı yazılmalı (BuildApp.jsx:677 <c>pendingId</c>): fırtına/hata değilse ve
    /// bu ilk-satır DEĞİLSE. Reduced-motion görünümde ayrıca bastırır.</summary>
    public bool ShouldType { get; }
    /// <summary>Daktilo BİR KEZ oynanır — recycle tekrar oynatmasın (GlowPlayed deseni).</summary>
    public bool TypePlayed { get; set; }

    public bool IsClickable => ProjectId is not null;

    /// <summary>Seçili mi — <see cref="RunViewModel.SelectedProjectId"/> değişince tazelenir (ProjectRow deseni).
    /// Sol 2px amber şerit + <c>Brush.SurfaceRaised</c> zemini bundan akar.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Statü glyph'i (12px) — <c>null</c> ise amber <c>▸</c> çizilir (sync/info). BuildApp.jsx:631-632/653.</summary>
    public GraphStatus? GlyphStatus { get; }

    /// <summary>Metin rengi token anahtarı (BuildApp.jsx:635-638).</summary>
    public string TextBrushKey { get; }

    public StreamEventViewModel(StreamComposer.Emission emission, string time, StreamKind kind, string? projectId,
        string text, bool anyFailed, bool shouldType, bool isSelected)
    {
        Id = emission.Id;
        Instant = emission.Instant;
        Time = time;
        Kind = kind;
        ProjectId = projectId;
        Text = text;
        ShouldType = shouldType;
        _isSelected = isSelected;
        GlyphStatus = GlyphFor(kind, anyFailed);
        TextBrushKey = BrushKeyFor(kind, anyFailed);
        GlowEligible = kind == StreamKind.Done && !anyFailed;
    }

    /// <summary>BuildApp.jsx:631-632 — ok→succeeded, fail→failed, skip→skipped, done→(failed?failed:succeeded),
    /// sync|info→null (amber ▸).</summary>
    private static GraphStatus? GlyphFor(StreamKind kind, bool anyFailed) => kind switch
    {
        StreamKind.Ok => GraphStatus.Succeeded,
        StreamKind.Fail => GraphStatus.Failed,
        StreamKind.Skip => GraphStatus.Skipped,
        StreamKind.Done => anyFailed ? GraphStatus.Failed : GraphStatus.Succeeded,
        _ => null, // sync | info → ▸
    };

    /// <summary>BuildApp.jsx:635-638 — fail→status-fail-text, skip→text-faint, done→(failed?fail:success)-text,
    /// sync|info→text-dim, ok→text-secondary.</summary>
    private static string BrushKeyFor(StreamKind kind, bool anyFailed) => kind switch
    {
        StreamKind.Fail => "Brush.StatusFailText",
        StreamKind.Skip => "Brush.TextFaint",
        StreamKind.Done => anyFailed ? "Brush.StatusFailText" : "Brush.StatusSuccessText",
        StreamKind.Sync or StreamKind.Info => "Brush.TextDim",
        _ => "Brush.TextSecondary", // ok
    };
}
