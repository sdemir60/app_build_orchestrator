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
    public SettingsDraftViewModel(IReadOnlyList<LayerPattern>? initial, string? repositoryRoot)
    {
        _repositoryRoot = repositoryRoot;
        Layers.CollectionChanged += OnLayersChanged;
        if (initial is { Count: > 0 })
            foreach (var p in initial.OrderBy(p => p.Order))
                AddRow(new LayerRowViewModel(p.Name, p.Regex));
        else
            AddDefaultRows();
    }

    /// <summary>[design v1.8.0 §2.9] Save iki koşulda bloklanır: (a) bir katmanın adı BOŞ (trim sonrası) ya da
    /// regex'i DERLENEMEZ — boş regex GEÇERLİdir; (b) <b>repository root BOŞ</b>. İkincisi v1.8.0'ın kuralıdır:
    /// <i>"Root boşken Save disabled — uygulamanın çalışması için zorunlu tek ayar budur."</i>
    /// Regex compile-check LayerEngine'in EKLEDİĞİ sınırlı-matchTimeout ctor'uyla AYNI
    /// (bkz. <see cref="LayerRowViewModel.RegexInvalid"/>).</summary>
    public bool CanSave =>
        !string.IsNullOrWhiteSpace(RepositoryRoot) && Layers.All(r => r.Name.Trim().Length > 0 && !r.RegexInvalid);

    // Root, CanSave'in ikinci koşuludur — değiştiğinde düğmenin de haberi olmalı.
    partial void OnRepositoryRootChanged(string? value) => OnPropertyChanged(nameof(CanSave));

    /// <summary>[design v1.8.0/§2.9 "Load sample layers"] Taslağı örnek katmanlarla (<see cref="LayerDefaults"/>)
    /// doldurur. A13.2 reset yasağı: <c>Clear()</c> yerine sondan sil + ekle (yalnız Remove/Add bildirimleri).</summary>
    public void LoadSampleLayers()
    {
        for (int i = Layers.Count - 1; i >= 0; i--) RemoveLayer(Layers[i]);
        AddDefaultRows();
    }

    // ---------------------------------------------------------------- [design v1.10.0 §2.9] Export / Import / Clear

    /// <summary>Taslağın o anki hâlini dosya biçimine çevirir — diyalog onu diske yazar.</summary>
    public SettingsFile ToFile() => SettingsFile.From(RepositoryRoot, BuildPatterns());

    /// <summary>Bir ayar dosyasını <b>FORMA</b> yükler. Hiçbir şey UYGULANMAZ: Save'e kadar ne
    /// <see cref="RunViewModel"/> ne UiState değişir (§2.9 — onay dialogu da yoktur).
    /// <para>Kök dosyada yoksa mevcut kök KORUNUR: bir katman dosyası kökü sıfırlamamalıdır.</para></summary>
    public void LoadFrom(SettingsFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!string.IsNullOrWhiteSpace(file.RepositoryRoot)) RepositoryRoot = file.RepositoryRoot;
        for (int i = Layers.Count - 1; i >= 0; i--) RemoveLayer(Layers[i]);
        foreach (var layer in file.Layers) AddRow(new LayerRowViewModel(layer.Name, layer.Pattern));
    }

    /// <summary>[§2.9] Clear: kökü ve TÜM katmanları boşaltır. İki aşamalı onay ve geri bildirim diyalogdadır —
    /// taslak yalnız boşalmayı bilir.</summary>
    public void ClearAll()
    {
        RepositoryRoot = null;
        for (int i = Layers.Count - 1; i >= 0; i--) RemoveLayer(Layers[i]);
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
