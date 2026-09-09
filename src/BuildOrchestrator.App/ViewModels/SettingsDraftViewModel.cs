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

/// <summary>[K5] Settings diyaloğundaki EXTERNAL PROJECTS editörünün tek satırı — düzenlenebilir yol + sürüm
/// kontrol seçimi. <see cref="IDragReorderItem"/>: katman kartıyla AYNI sürükle-bırak mekanizmasını paylaşır
/// (<see cref="DragReorderBehavior"/> öğe tipini bilmeden bu bayrağı set eder) — katman listesinden BAĞIMSIZ
/// sıralanır (ayrı <see cref="SettingsDraftViewModel.Externals"/> koleksiyonu, ayrı sürükleme oturumu).</summary>
public sealed partial class ExternalRowViewModel : ObservableObject, IDragReorderItem
{
    [ObservableProperty] private string _path;
    [ObservableProperty] private VcsKind _vcs;

    /// <summary>[D7 deseninin eşi] Sürüklenen kart mı — kart şablonu zemin/kenarı bundan sürer (Layer kartıyla
    /// AYNI paylaşılan <c>Ds.Settings.Card</c> stili/trigger'ı, kopya YOK).</summary>
    [ObservableProperty] private bool _isDragging;

    public ExternalRowViewModel(string path, VcsKind vcs)
    {
        _path = path;
        _vcs = vcs;
    }
}

/// <summary>
/// [D7/T66 · K5] Settings diyaloğunun WORKSPACE + EXTERNAL PROJECTS + LAYERS taslak VM'i — <b>saf, WPF'siz</b>
/// (testler Window olmadan sürer). Canlı katman pattern'lerinin, harici proje listesinin ve bekleyen repo
/// kökünün bir TASLAK kopyası üzerinde çalışır: <see cref="CommitAsync"/> = kaydet (RunViewModel + UiState'e
/// yazılır), Cancel = taslağı at (kopya olduğu için canlı duruma dokunulmaz).
/// </summary>
public sealed partial class SettingsDraftViewModel : ObservableObject
{
    public ObservableCollection<LayerRowViewModel> Layers { get; } = [];

    /// <summary>[K5] Harici proje kartlarının taslağı — katmanlardan tamamen BAĞIMSIZ bir koleksiyon (kendi
    /// sürükle-bırak oturumu, kendi CanSave kapısı). Kayıtlı harici proje YOKSA (null ya da boş) BOŞ kalır —
    /// katmanların aksine bir "varsayılan doldur" kavramı yoktur (§9 v1.14.0: harici projeler kuruluma özeldir,
    /// tek gerçek varsayılan boş listedir).</summary>
    public ObservableCollection<ExternalRowViewModel> Externals { get; } = [];

    /// <summary>Seçilmiş ama HENÜZ UYGULANMAMIŞ repo kökü. "Change…" yalnız burayı yazar; kök değişimi,
    /// satır reset'i ve Sync Save'e ertelenir — Cancel/Esc taslağı atar ve hiçbir iz kalmaz. Diyalog
    /// açılırken canlı <see cref="RunViewModel.RootPath"/> ile başlar.</summary>
    [ObservableProperty] private string? _repositoryRoot;

    /// <summary>Taslak = kayıtlı pattern'lerin DERİN kopyası (Order'a göre; editör sırası = katman sırası).
    /// Kayıtlı katman YOKSA (null ya da boş) taslak <see cref="LayerDefaults"/> ile DOLU kurulur — araç
    /// paylaşıldığında kimse katmanları elle yazmasın. Bu YALNIZ taslaktır: Save'e basılmadıkça ne
    /// <see cref="RunViewModel.LayerPatterns"/> ne UiState değişir; uygulama açılışında seed YOKtur.
    /// <paramref name="initialExternals"/> harici proje listesinin AYNI kuralla gelen taslağıdır (K5) — boşsa
    /// taslak da boş kalır.</summary>
    public SettingsDraftViewModel(IReadOnlyList<LayerPattern>? initial, string? repositoryRoot,
        IReadOnlyList<ExternalProject>? initialExternals = null)
    {
        _repositoryRoot = repositoryRoot;
        Layers.CollectionChanged += OnLayersChanged;
        Externals.CollectionChanged += OnExternalsChanged;
        if (initial is { Count: > 0 })
            foreach (var p in initial.OrderBy(p => p.Order))
                AddRow(new LayerRowViewModel(p.Name, p.Regex));
        else
            AddDefaultRows();

        if (initialExternals is { Count: > 0 })
            foreach (var e in initialExternals)
                AddExternalRow(new ExternalRowViewModel(e.Path, e.Vcs));
    }

    /// <summary>[design v1.8.0 §2.9 · K5] Save ÜÇ koşulda bloklanır: (a) bir katmanın adı BOŞ (trim sonrası) ya
    /// da regex'i DERLENEMEZ — boş regex GEÇERLİdir; (b) <b>repository root BOŞ</b> — v1.8.0'ın kuralıdır:
    /// <i>"Root boşken Save disabled — uygulamanın çalışması için zorunlu tek ayar budur."</i>; (c) [K5, design
    /// v1.14.0/§9] bir harici projenin path'i BOŞ (trim sonrası) — katman adı kuralıyla AYNI sertlik.
    /// Regex compile-check LayerEngine'in EKLEDİĞİ sınırlı-matchTimeout ctor'uyla AYNI
    /// (bkz. <see cref="LayerRowViewModel.RegexInvalid"/>).</summary>
    public bool CanSave =>
        !string.IsNullOrWhiteSpace(RepositoryRoot)
        && Layers.All(r => r.Name.Trim().Length > 0 && !r.RegexInvalid)
        && Externals.All(x => x.Path.Trim().Length > 0);

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
    public SettingsFile ToFile() => SettingsFile.From(RepositoryRoot, BuildPatterns(), BuildExternals());

    /// <summary>Bir ayar dosyasını <b>FORMA</b> yükler. Hiçbir şey UYGULANMAZ: Save'e kadar ne
    /// <see cref="RunViewModel"/> ne UiState değişir (§2.9 — onay dialogu da yoktur).
    /// <para>Kök dosyada yoksa mevcut kök KORUNUR: bir katman dosyası kökü sıfırlamamalıdır.</para>
    /// <para>[K5, design v1.14.0/§9] Harici projeler İÇİN kural DAHA SIKI: dosyada <c>externalProjects</c>
    /// anahtarı HİÇ yoksa (<see cref="SettingsFile.ExternalProjects"/> null) mevcut harici liste KORUNUR — bir
    /// eski/yalnız-katman dosyası harici listeyi SIFIRLAMAMALIDIR. Anahtar varsa (boş dizi DAHİL) taslak o
    /// listeYLE DEĞİŞTİRİLİR.</para></summary>
    public void LoadFrom(SettingsFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!string.IsNullOrWhiteSpace(file.RepositoryRoot)) RepositoryRoot = file.RepositoryRoot;
        for (int i = Layers.Count - 1; i >= 0; i--) RemoveLayer(Layers[i]);
        foreach (var layer in file.Layers) AddRow(new LayerRowViewModel(layer.Name, layer.Pattern));

        if (file.ExternalProjects is { } externals)
        {
            for (int i = Externals.Count - 1; i >= 0; i--) RemoveExternal(Externals[i]);
            foreach (var ext in externals)
                AddExternalRow(new ExternalRowViewModel(ext.Path, VcsKinds.Parse(ext.Vcs)));
        }
        // else: anahtar dosyada yok — mevcut harici liste KORUNUR (yukarıdaki XML doc).
    }

    /// <summary>[§2.9 · K5] Clear: kökü, TÜM katmanları VE TÜM harici projeleri boşaltır. İki aşamalı onay ve
    /// geri bildirim diyalogdadır — taslak yalnız boşalmayı bilir.</summary>
    public void ClearAll()
    {
        RepositoryRoot = null;
        for (int i = Layers.Count - 1; i >= 0; i--) RemoveLayer(Layers[i]);
        for (int i = Externals.Count - 1; i >= 0; i--) RemoveExternal(Externals[i]);
    }

    private void AddDefaultRows()
    {
        foreach (var (name, regex) in LayerDefaults.Layers) AddRow(new LayerRowViewModel(name, regex));
    }

    public void AddLayer() =>
        AddRow(new LayerRowViewModel($"Layer {Layers.Count + 1}", ""));

    public void RemoveLayer(LayerRowViewModel row) => Layers.Remove(row);

    /// <summary>[K5] "Add external project": boş path'li, Git kaynaklı bir kart ekler (§9 v1.14.0 birebir).</summary>
    public void AddExternal() => AddExternalRow(new ExternalRowViewModel("", VcsKind.Git));

    public void RemoveExternal(ExternalRowViewModel row) => Externals.Remove(row);

    /// <summary>[D7] Taslağı Contracts pattern'lerine çevirir: Order = satır indeksi (üstten alta), ad trim'li
    /// (BuildApp.jsx:1025), regex olduğu gibi.</summary>
    public IReadOnlyList<LayerPattern> BuildPatterns() =>
        Layers.Select((r, i) => new LayerPattern(i, r.Regex, r.Name.Trim())).ToList();

    /// <summary>[K5] Taslağı <see cref="ExternalProject"/> listesine çevirir: path TRIM'li, BOŞ path'ler
    /// DÜŞER (prototip <c>ext.filter((x) =&gt; x.path)</c> — hem Export hem Save bu TEK metodu paylaşır, kopya
    /// YASAK). Save'e kadar zaten <see cref="CanSave"/> boş path bırakmaz; filtre Export'un kendi güvenlik
    /// ağıdır (Export, CanSave'e bakmadan her zaman etkindir).</summary>
    public IReadOnlyList<ExternalProject> BuildExternals() =>
        [.. Externals.Select(x => new ExternalProject(x.Path.Trim(), x.Vcs)).Where(x => x.Path.Length > 0)];

    /// <summary>Kaydet (commit): taslağı <see cref="UiState.LayerPatterns"/> VE <see cref="UiState.ExternalProjects"/>'e
    /// (K5) AYNI commit'te persist eder ve TEK yoldan uygular — <see cref="RunViewModel.ApplySettingsAsync"/>
    /// katmanları, harici projeleri, bekleyen repo kökünü ve TEK Sync'i birlikte sürer. Cancel bu metodu
    /// ÇAĞIRMAZ → taslak (kopya) atılır, canlı duruma dokunulmaz.</summary>
    public async Task CommitAsync(RunViewModel run, IUiStateStore store)
    {
        var patterns = BuildPatterns();
        var externals = BuildExternals();
        var state = store.Load();
        state.LayerPatterns = patterns.ToList();
        state.ExternalProjects = externals.ToList();
        store.Save(state);
        await run.ApplySettingsAsync(patterns, RepositoryRoot, externals);
    }

    private void AddRow(LayerRowViewModel row) => Layers.Add(row);
    private void AddExternalRow(ExternalRowViewModel row) => Externals.Add(row);

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

    // [K5] Layers'ın OnLayersChanged/OnRowChanged ikizi — CanSave'in üçüncü koşulu (boş harici path) burada tazelenir.
    private void OnExternalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ExternalRowViewModel row in e.OldItems) row.PropertyChanged -= OnExternalRowChanged;
        if (e.NewItems is not null)
            foreach (ExternalRowViewModel row in e.NewItems) row.PropertyChanged += OnExternalRowChanged;
        OnPropertyChanged(nameof(CanSave));
    }

    private void OnExternalRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExternalRowViewModel.Path)) OnPropertyChanged(nameof(CanSave));
    }
}
