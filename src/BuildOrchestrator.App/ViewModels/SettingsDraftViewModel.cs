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

    /// <summary>[design v1.19.0 §2.9] Satırın taslak listesindeki indeksi — placeholder'lar buradan türer. Sahibi
    /// <see cref="SettingsDraftViewModel"/>'dir: her ekleme/silme/taşımada yeniden yazar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NamePlaceholder), nameof(PatternPlaceholder))]
    private int _index;

    public LayerRowViewModel(string name, string regex)
    {
        _name = name;
        _regex = regex;
    }

    /// <summary>[D7] Regex derlenemiyor mu — input'un kırmızı (invalid) durumu. LayerEngine'in EKLEDİĞİ
    /// sınırlı-matchTimeout ctor'uyla AYNI compile-check (boş regex GEÇERLİdir → invalid DEĞİL).</summary>
    public bool RegexInvalid => !LayerEngine.IsPatternCompilable(Regex);

    /// <summary>[design v1.19.0 §2.9] Ad input'unun placeholder'ı — satır indeksinin çifti (<see cref="LayerPlaceholders"/>).</summary>
    public string NamePlaceholder => LayerPlaceholders.For(Index).Name;

    /// <summary>[design v1.19.0 §2.9] Desen input'unun placeholder'ı — satır indeksinin çifti.</summary>
    public string PatternPlaceholder => LayerPlaceholders.For(Index).Pattern;
}

/// <summary>[K5] Settings diyaloğundaki EXTERNAL PROJECTS editörünün tek satırı — düzenlenebilir bir yol.
/// <para>[DEĞİŞEN KURAL] Satır eskiden bir kaynak (Git/TFVC) seçimi de taşıyordu; TFVC kolu kaldırıldığı için
/// seçim yüzeyi de kalktı (bkz. <see cref="ExternalProject"/>).</para>
/// <see cref="IDragReorderItem"/>: katman kartıyla AYNI sürükle-bırak mekanizmasını paylaşır
/// (<see cref="DragReorderBehavior"/> öğe tipini bilmeden bu bayrağı set eder) — katman listesinden BAĞIMSIZ
/// sıralanır (ayrı <see cref="SettingsDraftViewModel.Externals"/> koleksiyonu, ayrı sürükleme oturumu).</summary>
public sealed partial class ExternalRowViewModel : ObservableObject, IDragReorderItem
{
    [ObservableProperty] private string _path;

    /// <summary>[D7 deseninin eşi] Sürüklenen kart mı — kart şablonu zemin/kenarı bundan sürer (Layer kartıyla
    /// AYNI paylaşılan <c>Ds.Settings.Card</c> stili/trigger'ı, kopya YOK).</summary>
    [ObservableProperty] private bool _isDragging;

    public ExternalRowViewModel(string path) => _path = path;
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

    /// <summary>[design v1.19.0 §2.9] General sayfasının grupları ve satırları — <see cref="GeneralSettingsCatalog"/>'tan
    /// doğar (sıra/metin burada yeniden yazılmaz).
    /// <para><b>Henüz davranışa bağlı DEĞİL (kullanıcı kararı 1):</b> <c>Start with Windows</c>, <c>Start minimized
    /// to tray</c>, <c>Close to tray</c> ve <c>Show notifications</c> yalnız bu taslakta yaşar — <see cref="CommitAsync"/>
    /// onları yazmaz, <see cref="ToFile"/>/<see cref="LoadFrom"/> taşımaz, konsola not düşmez; diyalog her açılışta
    /// yeni bir taslak kurduğu için varsayılana dönerler. <see cref="ClearAll"/> onları da varsayılanına döndürür
    /// (prototip parity). Yalnız <c>Pull before build</c> (<see cref="PullExternalsBeforeBuild"/>) ve <c>Stash and switch
    /// branches</c> (<see cref="StashOnBranchSwitch"/>) gerçektir.</para></summary>
    public IReadOnlyList<GeneralSettingGroupViewModel> GeneralGroups { get; }

    private readonly Dictionary<GeneralSetting, GeneralSettingRowViewModel> _generalRows = [];

    /// <summary>Bir General anahtarının taslak satırı.</summary>
    public GeneralSettingRowViewModel GeneralRow(GeneralSetting setting) => _generalRows[setting];

    /// <summary>[design v1.15.0 → v1.19.0 §2.9] Harici çalışma kopyaları her build'den ÖNCE güncellensin mi — General
    /// sayfasının BUILD grubundaki <c>Pull before build</c> satırının KENDİSİ (iki yüz tek değer). Varsayılan AÇIK;
    /// Save'e kadar yalnız taslaktır.</summary>
    public bool PullExternalsBeforeBuild
    {
        get => GeneralRow(GeneralSetting.PullBeforeBuild).IsOn;
        set => GeneralRow(GeneralSetting.PullBeforeBuild).IsOn = value;
    }

    /// <summary>[spec 2026-09-18 §6.3] Branch chip'inden checkout'ta kirli ağaç stash'lenip geçilsin mi — General
    /// sayfasının BRANCHES grubundaki <c>Stash and switch branches</c> satırının KENDİSİ (iki yüz tek değer,
    /// <see cref="PullExternalsBeforeBuild"/> deseni). Varsayılan KAPALI; Save'e kadar yalnız taslaktır.</summary>
    public bool StashOnBranchSwitch
    {
        get => GeneralRow(GeneralSetting.StashOnBranchSwitch).IsOn;
        set => GeneralRow(GeneralSetting.StashOnBranchSwitch).IsOn = value;
    }

    /// <summary>Seçilmiş ama HENÜZ UYGULANMAMIŞ repo kökü. "Change…" yalnız burayı yazar; kök değişimi,
    /// satır reset'i ve Sync Save'e ertelenir — Cancel/Esc taslağı atar ve hiçbir iz kalmaz. Diyalog
    /// açılırken canlı <see cref="RunViewModel.RootPath"/> ile başlar.</summary>
    [ObservableProperty] private string? _repositoryRoot;

    /// <summary>Taslak = kayıtlı pattern'lerin DERİN kopyası (Order'a göre; editör sırası = katman sırası).
    /// Kayıtlı katman YOKSA (null ya da boş) taslak BOŞ kurulur — [design v1.19.0 §2.9] ürüne özel bir ön-dolum
    /// yoktur; yeni satırlar yalnız placeholder gösterir (<see cref="LayerPlaceholders"/>). Save'e basılmadıkça ne
    /// <see cref="RunViewModel.LayerPatterns"/> ne UiState değişir.
    /// <paramref name="initialExternals"/> harici proje listesinin AYNI kuralla gelen taslağıdır (K5) — boşsa
    /// taslak da boş kalır.</summary>
    /// <param name="pullExternalsBeforeBuild">Canlı bayrağın taslak kopyası (varsayılan açık).</param>
    /// <param name="stashOnBranchSwitch">Canlı stash ayarının taslak kopyası (varsayılan kapalı).</param>
    public SettingsDraftViewModel(IReadOnlyList<LayerPattern>? initial, string? repositoryRoot,
        IReadOnlyList<ExternalProject>? initialExternals = null, bool pullExternalsBeforeBuild = true,
        bool stashOnBranchSwitch = false)
    {
        _repositoryRoot = repositoryRoot;
        GeneralGroups = BuildGeneralGroups();
        PullExternalsBeforeBuild = pullExternalsBeforeBuild;
        StashOnBranchSwitch = stashOnBranchSwitch;
        Layers.CollectionChanged += OnLayersChanged;
        Externals.CollectionChanged += OnExternalsChanged;
        if (initial is { Count: > 0 })
            foreach (var p in initial.OrderBy(p => p.Order))
                AddRow(new LayerRowViewModel(p.Name, p.Regex));

        if (initialExternals is { Count: > 0 })
            foreach (var e in initialExternals)
                AddExternalRow(new ExternalRowViewModel(e.Path));
    }

    /// <summary>[design v1.8.0 §2.9 · K5] Save ÜÇ koşulda bloklanır: (a) bir katmanın adı BOŞ (trim sonrası) ya
    /// da regex'i DERLENEMEZ — boş regex GEÇERLİdir; (b) <b>repository root BOŞ</b> — v1.8.0'ın kuralıdır:
    /// <i>"Root boşken Save disabled — uygulamanın çalışması için zorunlu tek ayar budur."</i>; (c) [K5, design
    /// v1.14.0/§9] bir harici projenin path'i BOŞ (trim sonrası) — katman adı kuralıyla AYNI sertlik.
    /// Regex compile-check LayerEngine'in EKLEDİĞİ sınırlı-matchTimeout ctor'uyla AYNI
    /// (bkz. <see cref="LayerRowViewModel.RegexInvalid"/>).</summary>
    public bool CanSave => SaveBlockedReason is null;

    /// <summary>[design v1.19.0 §2.9] Save kapalıyken footer'ın okuduğu TEK satırlık neden; Save açıkken <c>null</c>.
    /// <see cref="CanSave"/> bundan türer — iki özellik aynı koşulları iki kez yazmaz. Öncelik tasarımın sırasıdır:
    /// kök → harici path → katman adı → desen (kullanıcı hangi sayfada olursa olsun ilk engeli okur).</summary>
    public string? SaveBlockedReason =>
        string.IsNullOrWhiteSpace(RepositoryRoot) ? RootRequiredReason
        : Externals.Any(x => x.Path.Trim().Length == 0) ? ExternalPathRequiredReason
        : Layers.Any(r => r.Name.Trim().Length == 0) ? LayerNameRequiredReason
        : Layers.Any(r => r.RegexInvalid) ? InvalidPatternReason
        : null;

    private const string RootRequiredReason = "Repository root is required";
    private const string ExternalPathRequiredReason = "Every external project needs a path";
    private const string LayerNameRequiredReason = "Every layer needs a name";
    private const string InvalidPatternReason = "Check the highlighted pattern";

    /// <summary>Save kapısının iki yüzü (<see cref="CanSave"/> düğmeyi, <see cref="SaveBlockedReason"/> footer
    /// satırını sürer) her tetikleyicide BİRLİKTE bildirilir.</summary>
    private void NotifySaveGate()
    {
        OnPropertyChanged(nameof(SaveBlockedReason));
        OnPropertyChanged(nameof(CanSave));
    }

    // Root, Save kapısının ilk koşuludur — değiştiğinde düğmenin de haberi olmalı.
    partial void OnRepositoryRootChanged(string? value) => NotifySaveGate();

    // ---------------------------------------------------------------- [design v1.10.0 §2.9] Export / Import / Clear

    /// <summary>Taslağın o anki hâlini dosya biçimine çevirir — diyalog onu diske yazar.</summary>
    public SettingsFile ToFile() =>
        SettingsFile.From(RepositoryRoot, BuildPatterns(), BuildExternals(), PullExternalsBeforeBuild, StashOnBranchSwitch);

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
                AddExternalRow(new ExternalRowViewModel(ext.Path));
        }

        // [design v1.15.0] Bayrak dosyada YOKSA (eski/yalnız-katman dosyası) taslaktaki değer KORUNUR — harici
        // liste kuralının aynısı: bir dosyanın taşımadığı ayarı sıfırlamak sessiz bir karar olurdu.
        if (file.PullExternalBeforeBuild is { } pull) PullExternalsBeforeBuild = pull;
        // [spec 2026-09-18 §6.3] Stash ayarı AYNI kural: anahtar yoksa taslaktaki değer korunur.
        if (file.StashOnBranchSwitch is { } stash) StashOnBranchSwitch = stash;
        // else: anahtar dosyada yok — mevcut harici liste KORUNUR (yukarıdaki XML doc).
    }

    /// <summary>[§2.9 · K5] Clear: kökü, TÜM katmanları VE TÜM harici projeleri boşaltır. İki aşamalı onay ve
    /// geri bildirim diyalogdadır — taslak yalnız boşalmayı bilir.</summary>
    public void ClearAll()
    {
        RepositoryRoot = null;
        // [§2.9] General anahtarları (pull dahil) VARSAYILANINA döner — kapanmaz.
        foreach (var row in _generalRows.Values) row.IsOn = row.Definition.Default;
        for (int i = Layers.Count - 1; i >= 0; i--) RemoveLayer(Layers[i]);
        for (int i = Externals.Count - 1; i >= 0; i--) RemoveExternal(Externals[i]);
    }

    /// <summary>[design v1.19.0 §2.9] <c>Add layer</c>: BOŞ bir satır (ad ve desen boş) — input'lar satır indeksinin
    /// placeholder'ını gösterir.</summary>
    public void AddLayer() => AddRow(new LayerRowViewModel("", ""));

    public void RemoveLayer(LayerRowViewModel row) => Layers.Remove(row);

    /// <summary>[K5] "Add external project": boş path'li bir kart ekler (§9 v1.14.0 birebir).</summary>
    public void AddExternal() => AddExternalRow(new ExternalRowViewModel(""));

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
        [.. Externals.Select(x => new ExternalProject(x.Path.Trim())).Where(x => x.Path.Length > 0)];

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
        state.UpdateExternals = PullExternalsBeforeBuild;
        state.StashOnBranchSwitch = StashOnBranchSwitch;
        store.Save(state);
        await run.ApplySettingsAsync(patterns, RepositoryRoot, externals, PullExternalsBeforeBuild, StashOnBranchSwitch);
    }

    /// <summary>Katalogdan satırları kurar; bağımlı satırın etkinliğini üst anahtara, pull satırını
    /// <see cref="PullExternalsBeforeBuild"/> bildirimine bağlar.</summary>
    private List<GeneralSettingGroupViewModel> BuildGeneralGroups()
    {
        var groups = GeneralSettingsCatalog.Groups.Select((g, gi) => new GeneralSettingGroupViewModel(
            g.Title, gi == 0, [.. g.Rows.Select((d, ri) => new GeneralSettingRowViewModel(d, ri == 0))])).ToList();
        foreach (var row in groups.SelectMany(g => g.Rows)) _generalRows.Add(row.Definition.Setting, row);

        foreach (var row in _generalRows.Values)
        {
            if (row.Definition.DependsOn is not { } parentKey) continue;
            var parent = _generalRows[parentKey];
            row.IsEnabled = parent.IsOn;
            parent.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GeneralSettingRowViewModel.IsOn)) row.IsEnabled = parent.IsOn;
            };
        }

        _generalRows[GeneralSetting.PullBeforeBuild].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneralSettingRowViewModel.IsOn)) OnPropertyChanged(nameof(PullExternalsBeforeBuild));
        };
        _generalRows[GeneralSetting.StashOnBranchSwitch].PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneralSettingRowViewModel.IsOn)) OnPropertyChanged(nameof(StashOnBranchSwitch));
        };
        return groups;
    }

    private void AddRow(LayerRowViewModel row) => Layers.Add(row);
    private void AddExternalRow(ExternalRowViewModel row) => Externals.Add(row);

    // [D7] Save kapısı tüm satırların ad/regex'ine bağlıdır — satır ekleme/çıkarmada ve her satır değişiminde
    // tazelenir. [design v1.19.0] Her koleksiyon değişiminde (Move dahil) satır indeksleri de yeniden yazılır:
    // placeholder satıra değil indekse aittir.
    private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (LayerRowViewModel row in e.OldItems) row.PropertyChanged -= OnRowChanged;
        if (e.NewItems is not null)
            foreach (LayerRowViewModel row in e.NewItems) row.PropertyChanged += OnRowChanged;
        for (int i = 0; i < Layers.Count; i++) Layers[i].Index = i;
        NotifySaveGate();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LayerRowViewModel.Name) or nameof(LayerRowViewModel.RegexInvalid))
            NotifySaveGate();
    }

    // [K5] Layers'ın OnLayersChanged/OnRowChanged ikizi — Save kapısının boş harici path koşulu burada tazelenir.
    private void OnExternalsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ExternalRowViewModel row in e.OldItems) row.PropertyChanged -= OnExternalRowChanged;
        if (e.NewItems is not null)
            foreach (ExternalRowViewModel row in e.NewItems) row.PropertyChanged += OnExternalRowChanged;
        NotifySaveGate();
    }

    private void OnExternalRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExternalRowViewModel.Path)) NotifySaveGate();
    }
}
