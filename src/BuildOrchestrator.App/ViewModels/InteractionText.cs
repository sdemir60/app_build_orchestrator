namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [E2/T10] Etkileşim/boş-durum davet metinlerinin TEK KAYNAĞI (design-v1 README §"empty" + BuildApp.jsx:455-459
/// birebir). Şerit faz-metinleri <see cref="RibbonText"/>'te kalır; burada YALNIZ panel-içi davet/boş-durum
/// kopyaları toplanır — hem XAML (x:Static) hem testler AYNI sabiti okur (verbatim, byte-exact). Tüm metin
/// İngilizce (Global Constraint).
/// </summary>
public static class InteractionText
{
    /// <summary>
    /// [design v1.16.0 §2.7-6a] <c>N behind</c> chip'inin tooltip'i. İkinci cümle tek tıkla ne OLMADIĞINI
    /// söyler: korkulan şey merge/rebase'tir ve bu chip ikisini de yapmaz.
    /// </summary>
    public static string BehindChipTooltip(int behind, string branch) =>
        $"Fetch found {behind} new commit{(behind == 1 ? "" : "s")} on origin/{branch}. "
        + "Click to fast-forward; nothing is merged or rewritten.";

    // ---- [design v1.8.0 §2.4] Proje listesi: FIRST RUN kurulum daveti ----
    // [DEĞİŞEN KURAL] Davet eskiden tek bir klasör seçiciye açılıyordu: "Pick a repository to get started" +
    // "Point to the OSYS solution root…" + bir `Choose Folder` düğmesi. v1.8.0 onu KALDIRDI — gereken ayar
    // sayısı arttığından (repository root + katman tanımları) boş durum artık Settings'e yönlendiriyor ve
    // repository root da Settings'in bir parçası oldu (§2.9).

    /// <summary>Kurulum davetinin başlığı (14px/600).</summary>
    public const string ConfigureWorkspaceTitle = "Configure the workspace";

    /// <summary>Kurulum davetinin açıklaması.</summary>
    public const string ConfigureWorkspaceSubtitle =
        "Set the repository root — and, if projects should be grouped, the layers. Discovery starts right after.";

    /// <summary>Kurulum listesinin iki satırının etiketleri.</summary>
    public const string SetupRepositoryRootLabel = "Repository root";
    public const string SetupLayersLabel = "Layers";

    /// <summary>Kök henüz girilmemişken kurulum listesinde görünen mono değer.</summary>
    public const string SetupRootNotSet = "Not set";
    /// <summary>Katman tanımı yokken: katmanlar zorunlu DEĞİLDİR (varsayılan tek liste).</summary>
    public const string SetupLayersOptional = "Optional";
    /// <summary>N katman tanımlıyken.</summary>
    public static string SetupLayersDefined(int count) =>
        string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} defined", count);

    /// <summary>Primary buton (dişli) — Settings'i açar.</summary>
    public const string OpenSettingsButton = "Open settings";

    /// <summary>[design v1.10.0 §2.4] Secondary buton (upload) — Settings'i açıp dosya seçiciyi HEMEN
    /// tetikler; hazır ayar dosyası olan developer tek adımda başlar.</summary>
    public const string ImportSettingsButton = "Import settings…";

    /// <summary>Butonların altındaki 11px not — import'un bir şeyi UYGULAMADIĞINI söyler.</summary>
    public const string ImportSettingsNote =
        "Import fills the form from a settings file — nothing is applied until you save.";

    // ---- Proje listesi: repo var ama proje yok (0-proje) ----
    /// <summary>Repo Sync'lendi ama altında hiç proje bulunamadı.</summary>
    public const string NoProjectsFound = "No projects found under this folder.";

    // ---- [A13/T2 · 2.4] Proje listesi: projeler VAR ama filtre hiçbirini eşleştirmiyor ----
    /// <summary>Aktif filtre/sorgu altında görünür satır kalmadı — design-v1 §2.4 (BuildApp.jsx:511).
    /// <see cref="NoProjectsFound"/>'dan AYRI: orada veri YOKTUR, burada veri var ama SÜZÜLMÜŞTÜR.</summary>
    public const string NoProjectsMatchFilter = "No projects match this filter.";

    // ---- Graf / event stream Sync-öncesi boş-durum ----
    /// <summary>Graf paneli Sync öncesi boş-durum etiketi (GraphView).</summary>
    public const string GraphEmpty = "Graph appears after Sync";

    // ---- [design v1.24.0 §2.3 · §2.4 · §9] Sync keşfi sürerken iki panel boş kalmaz ----
    /// <summary>Graf paneli keşif bloğunun etiketi. First run'ın <see cref="GraphEmpty"/>'inden AYRIDIR: orada
    /// workspace yoktur, burada Sync proje kümesini keşfediyor.</summary>
    public const string GraphDiscovering = "Graph appears once projects are discovered";

    /// <summary>Proje listesi keşif bloğunun başlığı.</summary>
    public const string DiscoveringProjects = "Discovering projects";

    /// <summary>
    /// Keşif bloğunun sayaç satırı: <c>{toplam} found</c>, harici tanım varsa ardından
    /// <c> · {repo} repository · {ext} external</c> (ayraç U+00B7, iki yanında boşluk). Payda ve kaynak adı YOKTUR.
    /// Toplam bir ton parlak (text-dim), geri kalanı text-faint çizildiği için satır İKİ parça döner — sayı
    /// (<c>Total</c>) ve ondan sonrası (<c>Tail</c>); satırın tamamı ikisinin birleşimidir.
    /// </summary>
    /// <param name="breakdown">Kırılım gösterilsin mi (Sync isteği anında en az bir harici tanım vardı).</param>
    public static (string Total, string Tail) DiscoveryCounter(int repository, int external, bool breakdown)
    {
        string total = (repository + external).ToString(System.Globalization.CultureInfo.InvariantCulture);
        string tail = breakdown
            ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                " found · {0} repository · {1} external", repository, external)
            : " found";
        return (total, tail);
    }

    // Event stream'in boş-durum etiketi YOKTUR (kullanıcı kararı): panel boşken de alttaki bekleme satırı
    // (saat + amber imleç) konuşur — bkz. EventStreamView.xaml.

    // ---- [v1.17.0 §2.4] Proje listesi: katman başlığı artık bir gezinme kontrolü ----
    /// <summary>Yapışık katman başlığının native tooltip'i (<c>title</c>) — tıklamanın ne yapacağını söyler.
    /// TEK KAYNAK: hem in-flow hem yapışık overlay başlığı AYNI şablonu (dolayısıyla aynı binding'i) paylaşır.</summary>
    public static string JumpToLayer(string layerName) => $"Jump to {layerName}";
}

/// <summary>[E2/T10] Proje listesi boş-durum overlay'inin hangi davetin gösterileceği kararı — SAF, test edilebilir.
/// Görünürlük/metin seçimi kontrolde kopyalanmaz (tek eşleme yeri).</summary>
public enum ListInviteState
{
    /// <summary>Liste dolu (satır var) ya da keşfin dışında kalan bir bekleyiş (Clean/Optimize'ın kendi penceresi,
    /// Sync'siz biten bir istek) — hiçbir davet gösterilmez.</summary>
    None,
    /// <summary>[design v1.8.0 §2.4] Repo seçilmemiş → "Configure the workspace" kurulum daveti.</summary>
    PickRepository,
    /// <summary>Repo Sync'lendi (Idle) ama 0 proje → "No projects found under this folder."</summary>
    NoProjects,
    /// <summary>[A13/T2 · 2.4] Projeler VAR ama aktif filtre/sorgu hiçbirini eşleştirmiyor →
    /// "No projects match this filter." <see cref="NoProjects"/>'ten AYRI durumdur.</summary>
    NoFilterMatch,
    /// <summary>[design v1.24.0 §2.4] Sync proje kümesini keşfediyor ve ekrandaki liste boş → "Discovering
    /// projects" + bulunan proje sayacı (<see cref="RunViewModel.IsDiscovering"/>).</summary>
    Discovering,
}

/// <summary>[E2/T10] <see cref="ListInviteState"/> kararının TEK yeri.</summary>
public static class ListInvite
{
    /// <summary>
    /// Repo yoksa PickRepository; [design v1.24.0] keşif sürüyorsa Discovering; repo Sync'lendiyse (Idle) ve hiç
    /// proje yoksa NoProjects; projeler VARKEN filtre hiçbirini geçirmiyorsa NoFilterMatch; aksi None (dolu liste, ya
    /// da keşfin dışında kalan Boot/Syncing bekleyişi — o zaman davet gösterme, boş liste bırak).
    /// </summary>
    /// <param name="discovering">[design v1.24.0 · plan K6] Keşif bloğu açık mı (<see cref="RunViewModel.IsDiscovering"/>).
    /// Kurulum davetinden SONRA, "proje yok" ve "filtre eşleşmedi" kararlarından ÖNCE gelir: proje kümesi henüz
    /// bilinmiyorken ne klasör boş denebilir ne de filtre suçlanabilir (Sync düğmesi ekranı baştan başlattığında VM'de
    /// satırlar ve filtre durur).</param>
    /// <param name="projectCount">TOPLAM satır sayısı (filtresiz) — "veri var mı" sorusu.</param>
    /// <param name="visibleCount">[A13/T2 · 2.4] Aktif filtre/sorgu altında GÖRÜNEN satır sayısı — "veri
    /// süzüldü mü" sorusu. Sıra önemlidir: "hiç proje yok" (veri yok) kararı, "filtre eşleşmedi" (veri var ama
    /// gizli) kararından ÖNCE gelir — 0 projeli bir workspace'te açık bir filtre varsa kullanıcıya filtreyi
    /// suçlamak YANLIŞ olurdu.</param>
    public static ListInviteState Resolve(bool hasWorkspace, bool discovering, AppPhase phase, int projectCount, int visibleCount)
    {
        if (!hasWorkspace) return ListInviteState.PickRepository;
        if (discovering) return ListInviteState.Discovering;
        if (projectCount == 0 && phase == AppPhase.Idle) return ListInviteState.NoProjects;
        if (projectCount > 0 && visibleCount == 0) return ListInviteState.NoFilterMatch;
        return ListInviteState.None;
    }
}
