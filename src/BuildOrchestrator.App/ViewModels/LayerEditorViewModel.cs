using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>[D7/T66] Settings diyaloğundaki LAYERS editörünün tek satırı — düzenlenebilir ad + regex ve
/// regex'in geçersizliği (input'un kırmızı durumu). <see cref="IDragReorderItem"/>: sürüklenirken kartın
/// "kalkık" görselini taşır (davranış öğe tipini bilmeden bu bayrağı set eder).</summary>
public sealed partial class LayerRowViewModel : ObservableObject, IDragReorderItem
{
    [ObservableProperty] private string _name;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegexInvalid))]
    private string _regex;

    /// <summary>[D7] Sürüklenen kart mı (grip'ten tutulmuş) — kart şablonu zemin/kenarı bundan sürer (A13.2
    /// template-local trigger; sürüklenirken <c>Brush.SurfaceRaised</c> + <c>Brush.BorderStrong</c>).</summary>
    [ObservableProperty] private bool _isDragging;

    public LayerRowViewModel(string name, string regex)
    {
        _name = name;
        _regex = regex;
    }

    /// <summary>[D7] Regex derlenemiyor mu — input'un kırmızı (invalid) durumu. LayerEngine'in EKLEDİĞİ
    /// sınırlı-matchTimeout ctor'uyla AYNI compile-check (boş regex GEÇERLİdir → invalid DEĞİL).</summary>
    public bool RegexInvalid => !LayerEngine.IsPatternCompilable(Regex);
}

/// <summary>
/// [D7/T66] Settings diyaloğunun LAYERS editör VM'i — <b>saf, WPF'siz</b> (testler Window olmadan sürer).
/// Canlı katman pattern'lerinin bir TASLAK kopyası üzerinde çalışır: <see cref="Commit"/> = kaydet
/// (RunViewModel + UiState'e yazılır), Cancel = taslağı at (kopya olduğu için canlı pattern'lere dokunulmaz).
/// </summary>
public sealed partial class LayerEditorViewModel : ObservableObject
{
    /// <summary>[D7] "Load sample layers" — 6 örnek katman (BuildApp.jsx:965-972'den BİREBİR: ad + regex).</summary>
    public static readonly IReadOnlyList<(string Name, string Regex)> SampleLayers =
    [
        ("Layer 0 — Core", @"^OSYS\.(Base$|Common\.)"),
        ("Layer 1 — Infrastructure", @"^OSYS\.(Data\.|Security$|Shared\.UI$|Integration\.Core$)"),
        ("Layer 2 — Domain", @"^OSYS\.Domain\."),
        ("Layer 3 — Services", @"\.(Scheduling|Workshop|Catalog|Invoicing|Accounting|Inventory)$|^OSYS\.(Sales|UsedCars|Reporting)\.Core$"),
        ("Layer 4 — API", @"^OSYS\.(?!Mobile\.).*\.Api$"),
        ("Layer 5 — Client", @"^OSYS\.(Web|Client|Mobile)\."),
    ];

    public ObservableCollection<LayerRowViewModel> Layers { get; } = [];

    public LayerEditorViewModel(IReadOnlyList<LayerPattern>? initial)
    {
        Layers.CollectionChanged += OnLayersChanged;
        // [D7] Taslak = canlı pattern'lerin DERİN kopyası (Order'a göre, editör sırası = katman sırası).
        if (initial is not null)
            foreach (var p in initial.OrderBy(p => p.Order))
                AddRow(new LayerRowViewModel(p.Name, p.Regex));
    }

    /// <summary>[D7] Save yalnız bir katmanın adı BOŞ (trim sonrası) ya da regex'i DERLENEMEZ iken bloklanır;
    /// boş regex GEÇERLİdir (bloklamaz). BuildApp.jsx:1017 <c>valid = draft.every(name.trim() &amp;&amp; !invalid)</c>.
    /// Regex compile-check LayerEngine'in EKLEDİĞİ sınırlı-matchTimeout ctor'uyla AYNI (bkz. <see cref="LayerRowViewModel.RegexInvalid"/>).</summary>
    public bool CanSave => Layers.All(r => r.Name.Trim().Length > 0 && !r.RegexInvalid);

    /// <summary>[D7] "Load sample layers" — taslağı 6 örnek katmanla değiştirir. A13.2 reset yasağı: <c>Clear()</c>
    /// yerine sondan sil + ekle (yalnız Remove/Add bildirimleri — Reset yok).</summary>
    public void LoadSampleLayers()
    {
        for (int i = Layers.Count - 1; i >= 0; i--) RemoveLayer(Layers[i]);
        foreach (var (name, regex) in SampleLayers) AddRow(new LayerRowViewModel(name, regex));
    }

    public void AddLayer() =>
        AddRow(new LayerRowViewModel($"Layer {Layers.Count + 1}", ""));

    public void RemoveLayer(LayerRowViewModel row) => Layers.Remove(row);

    /// <summary>[D7] Taslağı Contracts pattern'lerine çevirir: Order = satır indeksi (üstten alta), ad trim'li
    /// (BuildApp.jsx:1025), regex olduğu gibi.</summary>
    public IReadOnlyList<LayerPattern> BuildPatterns() =>
        Layers.Select((r, i) => new LayerPattern(i, r.Regex, r.Name.Trim())).ToList();

    /// <summary>[D7] Kaydet (commit): pattern'leri <see cref="RunViewModel.ApplyLayerPatterns"/> ile uygular
    /// (BİREBİR konsol notu + <see cref="RunViewModel.LayerPatterns"/>) ve <see cref="UiState.LayerPatterns"/>'a
    /// persist eder. Cancel bu metodu ÇAĞIRMAZ → taslak (kopya) atılır, canlı pattern'lere dokunulmaz.</summary>
    public void Commit(RunViewModel run, IUiStateStore store)
    {
        var patterns = BuildPatterns();
        run.ApplyLayerPatterns(patterns);
        var state = store.Load();
        state.LayerPatterns = patterns.ToList();
        store.Save(state);
    }

    private void AddRow(LayerRowViewModel row) => Layers.Add(row);

    // [D7] CanSave tüm satırların ad/regex'ine bağlıdır — satır ekleme/çıkarmada ve her satır değişiminde tazelenir.
    private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (LayerRowViewModel row in e.OldItems) row.PropertyChanged -= OnRowChanged;
        if (e.NewItems is not null)
            foreach (LayerRowViewModel row in e.NewItems) row.PropertyChanged += OnRowChanged;
        OnPropertyChanged(nameof(CanSave));
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayerRowViewModel.Name) or nameof(LayerRowViewModel.RegexInvalid))
            OnPropertyChanged(nameof(CanSave));
    }
}
