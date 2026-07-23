namespace BuildOrchestrator.App.ViewModels;

/// <summary>[D3/T?] Event stream olay türü — glyph/renk eşlemesi (<see cref="StreamEventViewModel"/>) ve daktilo/
/// instant kararı bundan türer. Prototip <c>ev.kind</c> (BuildApp.jsx:631-638) birebir: ok/fail/skip/done +
/// sync/info (ikisi de amber <c>▸</c> glyph).</summary>
public enum StreamKind { Ok, Fail, Skip, Sync, Info, Done }

/// <summary>
/// [D3/T?] Event stream'in SAF (WPF'siz, InvariantCulture-nötr) çekirdeği — design-v1 prototip motorundaki
/// (<c>build-data.js</c> <c>emit</c>/<c>activeLine</c>) fırtına/daktilo/aktif-satır mantığının birebir portu.
/// UI (<see cref="Views.EventStreamView"/>) yalnız bunu tüketir; karar mantığı görünümde kopyalanmaz.
///
/// <para><b>Fırtına (burst) kararı</b> (build-data.js:256-260): son emit'ten bu yana &lt;340ms geçtiyse
/// <c>burst</c>; olay <c>burst || kind==fail</c> ise ANINDA basılır (daktilo yok). Reduced-motion GÖRÜNÜMDE
/// (<c>animationsEnabled=false</c>) ayrıca instant'a çevirir — bu bayrağa katılmaz (prototipte de
/// <c>TypingLine</c> REDUCED'ı ayrı ele alır).</para>
///
/// <para><b>Aktif satır</b> (build-data.js:448-454): bir proje build'e başlayınca aktif satır O projeye kurulur;
/// aktif projenin build'i bitince aktif satır EN SON BAŞLAYAN hâlâ building projeye ATLAR (yeni id →
/// <see cref="ActiveGeneration"/> artar → daktilo yeniden koşar). Hiç building kalmazsa aktif satır boşalır.</para>
///
/// <para><b>Tampon sayacı</b> (build-data.js:261): tam tampon 260'ta doyar — <see cref="Count"/> "{n} events"
/// sayacıdır (render dilimi 150 DEĞİL; Ek A #23 ile aynı ilke: sayaç tam tampon).</para>
/// </summary>
public sealed class StreamComposer
{
    /// <summary>build-data.js:257 — <c>(simT - lastEmitT) &lt; 340</c>.</summary>
    public const double BurstWindowMs = 340.0;
    /// <summary>build-data.js:261 — <c>stream.length &gt; 260</c> doyumu (tam tampon = "{n} events").</summary>
    public const int BufferCap = 260;
    /// <summary>BuildApp.jsx:666 — <c>eng.stream.slice(-150)</c> (görünen dilim).</summary>
    public const int RenderSlice = 150;

    private long? _lastEmitMs;
    private long _nextId;
    private int _count;

    // Aktif satır: building projeler BAŞLAMA sırasında; en son eleman = en son başlayan.
    private readonly List<(string Id, string Name)> _building = [];
    private string? _activeId;
    private string? _activeName;
    private long _activeGeneration;

    /// <summary>Tam tampon uzunluğu (≤260) — "{n} events" sayacı.</summary>
    public int Count => _count;

    public string? ActiveProjectId => _activeId;
    public string? ActiveName => _activeName;
    /// <summary>Aktif satır metni "<c>{name} building…</c>" ya da hiç building yoksa <c>null</c>.</summary>
    public string? ActiveText => _activeName is null ? null : _activeName + " building…";
    /// <summary>Aktif proje her DEĞİŞTİĞİNDE artar — görünüm bunu izleyip daktiloyu yeniden başlatır
    /// (prototip <c>activeLine.id</c> anahtarı).</summary>
    public long ActiveGeneration => _activeGeneration;

    /// <summary>Bir emit'in çıktısı: kalıcı id + instant kararı (<c>burst || isFail</c>).</summary>
    public readonly record struct Emission(long Id, bool Instant);

    /// <summary>Bir stream olayı yayınla (build-data.js <c>emit</c>): fırtına kararını verir, tam tampon sayacını
    /// (≤260) artırır ve kalıcı id üretir. Metin bileşimi ÇAĞIRANDA (VM) — bu çekirdek yalnız zamanlama kararını
    /// verir.</summary>
    public Emission Push(bool isFail, long nowMs)
    {
        bool burst = _lastEmitMs is { } last && (nowMs - last) < BurstWindowMs;
        _lastEmitMs = nowMs;
        _count = Math.Min(_count + 1, BufferCap);
        return new Emission(_nextId++, burst || isFail);
    }

    /// <summary>Bir proje build'e başladı — aktif satır O projeye kurulur (BuildApp/build-data.js:448-449).</summary>
    public void StartBuilding(string id, string name, long nowMs)
    {
        if (!_building.Any(b => IdEq(b.Id, id))) _building.Add((id, name));
        SetActive(id, name, nowMs);
    }

    /// <summary>Bir projenin build'i bitti (succeeded/failed). Aktif proje BUYSA en son başlayan hâlâ building
    /// projeye atlar; hiç kalmazsa aktif satır boşalır (build-data.js:451-454 + 483).</summary>
    public void FinishBuilding(string id, long nowMs)
    {
        _building.RemoveAll(b => IdEq(b.Id, id));
        if (_activeId is not null && IdEq(_activeId, id))
        {
            if (_building.Count > 0) { var nw = _building[^1]; SetActive(nw.Id, nw.Name, nowMs); }
            else ClearActive();
        }
    }

    /// <summary>Koşu bitti/durdu — building kümesi + aktif satır sıfırlanır (build-data.js:319/483). Tampon
    /// sayacı KORUNUR (anlatı koşular boyu kümülatiftir).</summary>
    public void EndRun()
    {
        _building.Clear();
        ClearActive();
    }

    // [D3 §1] nowMs artık kullanılmıyor (fırtına aktif satırı GATE ETMEZ — yalnız tampon satırları, bkz. Push);
    // parametre StartBuilding/FinishBuilding'in zaman API sözleşmesiyle uyum için korunur.
    private void SetActive(string id, string name, long nowMs)
    {
        _ = nowMs;
        // Aktif proje DEĞİŞMİYORSA (aynı id+ad) generation artırma — daktilo boş yere yeniden koşmasın.
        if (_activeId is not null && IdEq(_activeId, id) && _activeName == name) return;
        _activeId = id;
        _activeName = name;
        _activeGeneration++;
    }

    private void ClearActive()
    {
        if (_activeId is null) return;
        _activeId = null;
        _activeName = null;
        _activeGeneration++;
    }

    private static bool IdEq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
