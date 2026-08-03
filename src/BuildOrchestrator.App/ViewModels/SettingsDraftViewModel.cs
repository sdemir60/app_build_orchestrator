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
/// [D7/T66] Settings diyaloğunun LAYERS + REPOSITORY taslak VM'i — <b>saf, WPF'siz</b> (testler Window
/// olmadan sürer). Canlı katman pattern'lerinin ve bekleyen repo kökünün bir TASLAK kopyası üzerinde çalışır:
/// <see cref="CommitAsync"/> = kaydet (RunViewModel + UiState'e yazılır), Cancel = taslağı at (kopya olduğu
/// için canlı duruma dokunulmaz).
/// </summary>
public sealed partial class SettingsDraftViewModel : ObservableObject
{
    public ObservableCollection<LayerRowViewModel> Layers { get; } = [];

    /// <summary>Seçilmiş ama HENÜZ UYGULANMAMIŞ repo kökü. "Change…" yalnız burayı yazar; kök değişimi,
    /// satır reset'i ve Sync Save'e ertelenir — Cancel/Esc taslağı atar ve hiçbir iz kalmaz. Diyalog
    /// açılırken canlı <see cref="RunViewModel.RootPath"/> ile başlar.</summary>
    [ObservableProperty] private string? _repositoryRoot;

    /// <summary>Taslak = kayıtlı pattern'lerin DERİN kopyası (Order'a göre; editör sırası = katman sırası).
    /// Kayıtlı katman YOKSA (null ya da boş) taslak <see cref="LayerDefaults"/> ile DOLU kurulur — araç
    /// paylaşıldığında kimse katmanları elle yazmasın. Bu YALNIZ taslaktır: Save'e basılmadıkça ne
    /// <see cref="RunViewModel.LayerPatterns"/> ne UiState değişir; uygulama açılışında seed YOKtur.</summary>
    public SettingsDraftViewModel(IReadOnlyList<LayerPattern>? initial, string? repositoryRoot = null)
    {
        _repositoryRoot = repositoryRoot;
        Layers.CollectionChanged += OnLayersChanged;
        if (initial is { Count: > 0 })
            foreach (var p in initial.OrderBy(p => p.Order))
                AddRow(new LayerRowViewModel(p.Name, p.Regex));
        else
            AddDefaultRows();
    }

    /// <summary>[D7] Save yalnız bir katmanın adı BOŞ (trim sonrası) ya da regex'i DERLENEMEZ iken bloklanır;
    /// boş regex GEÇERLİdir (bloklamaz). BuildApp.jsx:1017 <c>valid = draft.every(name.trim() &amp;&amp; !invalid)</c>.
    /// Regex compile-check LayerEngine'in EKLEDİĞİ sınırlı-matchTimeout ctor'uyla AYNI (bkz. <see cref="LayerRowViewModel.RegexInvalid"/>).</summary>
    public bool CanSave => Layers.All(r => r.Name.Trim().Length > 0 && !r.RegexInvalid);

    /// <summary>"Restore default layers" — taslağı <see cref="LayerDefaults"/> ile değiştirir. A13.2 reset
    /// yasağı: <c>Clear()</c> yerine sondan sil + ekle (yalnız Remove/Add bildirimleri — Reset yok).</summary>
    public void RestoreDefaults()
    {
        for (int i = Layers.Count - 1; i >= 0; i--) RemoveLayer(Layers[i]);
        AddDefaultRows();
    }

    private void AddDefaultRows()
    {
        foreach (var (name, regex) in LayerDefaults.Layers) AddRow(new LayerRowViewModel(name, regex));
    }

    public void AddLayer() =>
        AddRow(new LayerRowViewModel($"Layer {Layers.Count + 1}", ""));

    public void RemoveLayer(LayerRowViewModel row) => Layers.Remove(row);

    /// <summary>[D7] Taslağı Contracts pattern'lerine çevirir: Order = satır indeksi (üstten alta), ad trim'li
    /// (BuildApp.jsx:1025), regex olduğu gibi.</summary>
    public IReadOnlyList<LayerPattern> BuildPatterns() =>
        Layers.Select((r, i) => new LayerPattern(i, r.Regex, r.Name.Trim())).ToList();

    /// <summary>Kaydet (commit): taslağı <see cref="UiState.LayerPatterns"/>'a persist eder ve TEK yoldan
    /// uygular — <see cref="RunViewModel.ApplySettingsAsync"/> katmanları, bekleyen repo kökünü ve TEK Sync'i
    /// birlikte sürer. Cancel bu metodu ÇAĞIRMAZ → taslak (kopya) atılır, canlı duruma dokunulmaz.</summary>
    public async Task CommitAsync(RunViewModel run, IUiStateStore store)
    {
        var patterns = BuildPatterns();
        var state = store.Load();
        state.LayerPatterns = patterns.ToList();
        store.Save(state);
        await run.ApplySettingsAsync(patterns, RepositoryRoot);
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
