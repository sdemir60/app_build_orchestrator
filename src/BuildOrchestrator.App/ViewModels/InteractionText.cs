namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [E2/T10] Etkileşim/boş-durum davet metinlerinin TEK KAYNAĞI (design-v1 README §"empty" + BuildApp.jsx:455-459
/// birebir). Şerit faz-metinleri <see cref="RibbonText"/>'te kalır; burada YALNIZ panel-içi davet/boş-durum
/// kopyaları toplanır — hem XAML (x:Static) hem testler AYNI sabiti okur (verbatim, byte-exact). Tüm metin
/// İngilizce (Global Constraint).
/// </summary>
public static class InteractionText
{
    // ---- Proje listesi: repo seçilmemiş (empty) daveti ----
    /// <summary>Boş-durum daveti başlığı (14px/600) — BuildApp.jsx:455.</summary>
    public const string PickRepositoryTitle = "Pick a repository to get started";

    /// <summary>Boş-durum daveti açıklaması — BuildApp.jsx:457.</summary>
    public const string PickRepositorySubtitle =
        "Point to the OSYS solution root — projects and the dependency graph are discovered automatically.";

    /// <summary>Boş-durum primary butonu (klasör ikonu) — BuildApp.jsx:459.</summary>
    public const string ChooseFolderButton = "Choose Folder";

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

    /// <summary>[sinema] Takip askıdayken graf başlığında görünen pil (GraphView) — tıklanınca takip döner.</summary>
    public const string GraphFollowPaused = "FOLLOW PAUSED";

    /// <summary>Event stream paneli boş-durum etiketi (EventStreamView, BuildApp.jsx:705-707).</summary>
    public const string StreamEmpty = "No events yet.";
}

/// <summary>[E2/T10] Proje listesi boş-durum overlay'inin hangi davetin gösterileceği kararı — SAF, test edilebilir.
/// Görünürlük/metin seçimi kontrolde kopyalanmaz (tek eşleme yeri).</summary>
public enum ListInviteState
{
    /// <summary>Liste dolu (satır var) ya da Sync uçuşta/Boot — hiçbir davet gösterilmez.</summary>
    None,
    /// <summary>Repo seçilmemiş → "Pick a repository…" daveti + Choose Folder.</summary>
    PickRepository,
    /// <summary>Repo Sync'lendi (Idle) ama 0 proje → "No projects found under this folder."</summary>
    NoProjects,
    /// <summary>[A13/T2 · 2.4] Projeler VAR ama aktif filtre/sorgu hiçbirini eşleştirmiyor →
    /// "No projects match this filter." <see cref="NoProjects"/>'ten AYRI durumdur.</summary>
    NoFilterMatch,
}

/// <summary>[E2/T10] <see cref="ListInviteState"/> kararının TEK yeri.</summary>
public static class ListInvite
{
    /// <summary>
    /// Repo yoksa PickRepository; repo Sync'lendiyse (Idle) ve hiç proje yoksa NoProjects; projeler VARKEN
    /// filtre hiçbirini geçirmiyorsa NoFilterMatch; aksi None (dolu liste, ya da Boot/Syncing gibi "henüz
    /// bilinmiyor" fazları — o zaman davet gösterme, boş liste bırak).
    /// </summary>
    /// <param name="projectCount">TOPLAM satır sayısı (filtresiz) — "veri var mı" sorusu.</param>
    /// <param name="visibleCount">[A13/T2 · 2.4] Aktif filtre/sorgu altında GÖRÜNEN satır sayısı — "veri
    /// süzüldü mü" sorusu. Sıra önemlidir: "hiç proje yok" (veri yok) kararı, "filtre eşleşmedi" (veri var ama
    /// gizli) kararından ÖNCE gelir — 0 projeli bir workspace'te açık bir filtre varsa kullanıcıya filtreyi
    /// suçlamak YANLIŞ olurdu.</param>
    public static ListInviteState Resolve(bool hasWorkspace, AppPhase phase, int projectCount, int visibleCount)
    {
        if (!hasWorkspace) return ListInviteState.PickRepository;
        if (projectCount == 0 && phase == AppPhase.Idle) return ListInviteState.NoProjects;
        if (projectCount > 0 && visibleCount == 0) return ListInviteState.NoFilterMatch;
        return ListInviteState.None;
    }
}
