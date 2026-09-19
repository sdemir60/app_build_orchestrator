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
    public VisualStatus? GlyphStatus { get; }

    /// <summary>Metin rengi token anahtarı (BuildApp.jsx:635-638).</summary>
    public string TextBrushKey { get; }

    /// <summary>Bu satır YAZILIRKEN imlecin alacağı ton — satırın kendi glyph rengi. Glyph'i olmayan
    /// (sync/info) satırlarda <c>null</c>; imleç o zaman görünümün dinlenme tonunda (amber) kalır.</summary>
    public string? IconBrushKey { get; }

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
        IconBrushKey = GlyphStatus is { } g ? Controls.StatusGlyph.BrushKeyFor(g) : null;
        // [imleç tonu] Satır YAZILIRKEN event stream'in imleci bu rengi alır (yazım bitince amber'a döner).
        // Kaynak satırın GLYPH'iyle AYNIdir (StatusGlyph.BrushKeyFor) — ikinci bir renk tablosu YAZILMAZ.
        // Glyph'i olmayan (sync/info → amber ▸) satırlarda null: görünüm kendi dinlenme tonuna düşer.
        GlowEligible = kind == StreamKind.Done && !anyFailed;
    }

    /// <summary>BuildApp.jsx:631-632 — ok→succeeded, fail→failed, skip→skipped, done→(failed?failed:succeeded),
    /// sync|info→null (amber ▸).</summary>
    /// <para>[design v1.20.0 §1.4] Event stream bir RUN-STORY yüzeyidir: atlama satırının — glyph'i
    /// (<see cref="VisualStatus.Skipped"/>) burada yaşamaya devam eder.</para>
    /// <para>[Task 7] <c>warn</c> da glyph'sizdir (sync/info'yla AYNI amber ▸) — bir git reddi ne "başarı" ne
    /// "hata" glyph'i taşır, yalnız metin rengi onu ayırır (bkz. <see cref="BrushKeyFor"/>).</para>
    private static VisualStatus? GlyphFor(StreamKind kind, bool anyFailed) => kind switch
    {
        StreamKind.Ok => VisualStatus.Succeeded,
        StreamKind.Fail => VisualStatus.Failed,
        StreamKind.Skip => VisualStatus.Skipped,
        StreamKind.Done => anyFailed ? VisualStatus.Failed : VisualStatus.Succeeded,
        _ => null, // sync | info | warn → ▸
    };

    /// <summary>
    /// BuildApp.jsx:635-638 — fail→status-fail-text, done→(failed?fail:success)-text, sync|info→text-dim,
    /// ok→text-secondary.
    /// <para><b>[DEĞİŞEN KURAL]</b> <c>skip</c> prototipte <c>text-faint</c> (#54545c) idi; artık
    /// <c>text-dim</c> (#76767e). Gerekçe (kullanıcı): atlananlar geri planda kalmalı ama OKUNABİLMELİ — "bu proje
    /// neden derlenmedi" en sık sorulan sorudur. Hiyerarşi korunur: skip hâlâ <c>ok</c>'un (text-secondary)
    /// altındadır.</para>
    /// <para><b>[Task 7] <c>warn</c> prototipte YOK.</b> Bir git reddi (kirli ağaç branch-switch reddi, pull
    /// reddi) <c>Brush.AmberText</c> alır — konsolun <c>warning:</c> önekiyle boyadığı AYNI token (tek kaynak,
    /// yeni renk İCAT EDİLMEZ); <c>sync</c>/<c>info</c>'nun dim tonundan bilerek AYRIŞIR, çünkü bir ret sıradan
    /// bir ilerleme notu değildir.</para></summary>
    private static string BrushKeyFor(StreamKind kind, bool anyFailed) => kind switch
    {
        StreamKind.Fail => "Brush.StatusFailText",
        StreamKind.Skip => "Brush.TextDim",
        StreamKind.Done => anyFailed ? "Brush.StatusFailText" : "Brush.StatusSuccessText",
        StreamKind.Sync or StreamKind.Info => "Brush.TextDim",
        StreamKind.Warn => "Brush.AmberText",
        _ => "Brush.TextSecondary", // ok
    };
}
