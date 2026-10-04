using System.Globalization;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D6/T40+T12+T43-UI] <see cref="RunViewModel"/>'in <b>aksiyon-barı yüzeyi</b>: statü chip'i filtre toggle'ı,
/// branch popover seçimi ve perf profili seed'i. Ayrı bir partial dosyada, çünkü ana dosya run/log/ETA yüzeyini, Workspace.cs Sync/
/// topoloji yüzeyini taşır; bu üçüncü sorumluluk (alt bar) onlardan ayrı durur. SAF mantık: view'siz test edilir.
/// </summary>
public sealed partial class RunViewModel
{
    // ---------------------------------------------------------------- [T40] statü chip'i filtre toggle'ı

    /// <summary>[T40] Sayaç chip'i toggle'ı: aynı chip'e ikinci tık onu KÜMEDEN ÇIKARIR; <c>null</c> (Σ) TÜM
    /// kümeyi temizler.
    /// <para>[design v1.7.0 — Filtreleme] Filtreye basmak SEÇİMİ DE DÜŞÜRÜR: seçim graf kamerasını bir düğüme
    /// kilitler ve konsolu o projenin loguna alır; filtre ise "bu kümeye bak" der. İkisi aynı anda açıkken
    /// kullanıcı filtrelenmiş listeye bakarken graf ilgisiz bir düğüme odaklı kalıyordu.</para>
    /// <para><b>[DEĞİŞEN KURAL — design v1.11.0 §2.7-4]</b> Chip'ler ARTIK birbirini düşürmez: küme çoklu ve
    /// VEYA'lıdır (design v1.20.0 §2.7: durum filtreleri — ✓ + ✗ = "güncel ya da bozuk"). Yeni küme her seferinde YENİ bir örnektir — yerinde
    /// mutasyon <c>PropertyChanged</c> yaymaz ve liste bayat kalırdı.</para></summary>
    public void ToggleFilter(string? filter)
    {
        SelectedProjectId = null;
        if (filter is null) { ActiveFilters = ProjectFilter.None; return; }

        var next = new HashSet<string>(ActiveFilters, StringComparer.Ordinal);
        if (!next.Remove(filter)) next.Add(filter);
        ActiveFilters = next.Count == 0 ? ProjectFilter.None : next;
    }

    // ---------------------------------------------------------------- branch (checkout edilmiş olan)

    /// <summary>Aktif branch'in adı (<see cref="Branches"/> içinde <c>IsActive</c> olan) — <see cref="Branch"/>
    /// değeri envanterden buradan okunur. Envanter henüz gelmediyse (IPC öncesi) ya da HEAD detached ise
    /// <c>null</c>.</summary>
    public string? ActiveBranchName => Branches.FirstOrDefault(b => b.IsActive)?.Name;

    /// <summary>
    /// [spec 2026-09-18 §6.3] Branch popover'ından seçim = çalışma ağacında GERÇEK bir checkout. Aktif branch'i
    /// seçmek ya da kilitliyken seçmek hiçbir şey yapmaz. Aksi hâlde <see cref="CheckoutBranchCommand"/> gider
    /// (uzak bir hedef <c>origin/x</c> olarak; izleyen yerel branch'i motor kurar) ve kirli ağaçta ne
    /// yapılacağını Settings → General'ın <see cref="StashOnBranchSwitch"/> ayarı söyler.
    ///
    /// <para>Ekran motorun cevabına kadar DEĞİŞMEZ: <see cref="Branch"/> yine yalnız envanterden yazılır
    /// (<see cref="OnBranchList"/>), konsol TIKLAMADA temizlenmez — bölümü yalnız başarılı bir checkout'un
    /// cevabı açar (<see cref="OnCheckoutCompletedAsync"/>).</para>
    ///
    /// <para><b>[DEĞİŞEN KURAL — spec 2026-09-18 §1-1 → §6.3]</b> Worktree döneminde seçim onu worktree'de
    /// derlenecek HEDEF yapardı; worktree kalkınca (Task 4) seçim bir süre HİÇBİR ŞEY yapmadı. Artık checkout
    /// eder.</para>
    /// </summary>
    public async Task SelectBranch(BranchRef branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        if (branch.IsActive) return;
        if (!RefreshGitWritesAllowed() || !CanSwitchBranch) return; // [spec §6.4] yoklama bayat olabilir

        CurrentOperation = OperationLabel.Checkout;
        SetCheckoutBusy(true); // kapı GÖNDERİMDEN ÖNCE kapanır — ikinci tık ikinci bir checkout kuyruklatırdı
        ArmEngineWatchdog();
        bool sent = await TrySendAsync(
            new CheckoutBranchCommand(RootPath, branch.Name, branch.IsRemoteTracking, StashOnBranchSwitch), "checkoutBranch");
        // Gönderim SENKRON düştüyse (motor hazır değil/ölü) hiçbir cevap GELMEYECEK — kilit burada açılmazsa
        // chip kalıcı pasif kalırdı.
        if (!sent)
        {
            SetCheckoutBusy(false);
            CurrentOperation = null; // pill "SWITCHING BRANCH"ta asılı kalmasın
        }
    }

    /// <summary>
    /// [spec 2026-09-18 §6.3] Branch chip'inin TEK kapısı — chip'in <c>IsEnabled</c>'ı bunu okur. Koşu
    /// uçuştayken (derlenen ağaç altından değişmemeli), bir Sync/Clean/Optimize sürerken (onlar ağacı okur ya da
    /// değiştirir), motor erişilemezken ve bir checkout zaten uçuştayken kapalıdır.
    /// <para>BİLDİRİMLİDİR: meşgul yüzeylerin geçişleri <see cref="NotifySyncGatedCommands"/>'dan, checkout'unki
    /// <see cref="SetCheckoutBusy"/>'den duyurulur; koşu ve motor durumunu bar kendi abonelikleriyle izler.</para>
    /// <para>[spec 2026-09-18 §6.4] Git dizininde yarıda bir işlem (merge, rebase, cherry-pick, revert, çalışan bir git
    /// komutu) varken de kapalıdır; nedeni <see cref="GitOperationTooltip"/> söyler.</para>
    /// </summary>
    public bool CanSwitchBranch => HasWorkspace && WorkspaceGateOpen && GitWritesAllowed;

    /// <summary>
    /// [kullanıcı kararı 2026-09-29] Debug|Release segment'inin TEK kapısı — segment'in <c>IsEnabled</c>'ı ve
    /// <see cref="SetConfiguration"/> bunu okur. Geçiş kendi Sync'ini başlatır, bu yüzden kapı Sync düğmesininkidir
    /// (<see cref="CanSync"/>) + bir workspace: koşu ya da planlaması, bir Sync (pencereye dönüşün sessiz Sync'i dahil —
    /// İSTEK anından itibaren), Clean, Optimize, checkout, pull uçuştayken ve motor erişilemezken kapalıdır. Uçuştaki
    /// bir Sync başladığı configuration'ı taşır; o sırada kabul edilen bir geçişte eski configuration'ın cevabı yeni
    /// configuration'ın satırlarını boyardı. Yarıdaki bir git işlemi kapatmaz: Sync düğmesi gibi o durumda da koşar.
    /// <para>Bildirimi Sync komutununkidir (<c>SyncCommand.CanExecuteChanged</c>) — bar kapıyı yeniden türetmez.</para>
    /// <para><b>[DEĞİŞEN KURAL]</b> Eskiden segment yalnız koşu sırasında kilitliydi (<see cref="IsMidRunLocked"/>).</para>
    /// </summary>
    public bool CanSwitchConfiguration => HasWorkspace && CanSync();

    /// <summary>Branch popover'daki mono SHA için 7-haneli kısaltma (uzunsa kırp, zaten kısaysa olduğu gibi) —
    /// brief 7-hane pinler.</summary>
    internal static string Short7(string sha) => sha.Length > 7 ? sha[..7] : sha;

    /// <summary>
    /// [Harici projeler] Bir REVİZYON KİMLİĞİNİ kısaltır. Kısaltma yalnız gerçek bir git sha'sına (40 hex)
    /// uygulanır; başka her değer (zaten kısaltılmış bir sha, bir etiket, bir kayıt boşluğu) olduğu gibi
    /// gösterilir — 7 haneye kırpmak yalnız tam sha için anlamlıdır.
    /// </summary>
    internal static string ShortSha(string? revision) => Core.Git.RevisionText.Short(revision);

    // ---------------------------------------------------------------- [D6 persistence] perf seed

    /// <summary>[D6 persistence] Kalıcı (UiState) PerfMode'u uygular — <see cref="PerfMode"/> + <see cref="Parallelism"/>
    /// BİRLİKTE (tek tablo: Core'un <c>PerfProfile</c>'ı, <see cref="CyclePerfAsync"/> ile aynı otorite).
    /// <see cref="CyclePerfAsync"/>'ten farkı: döngü YOK, konsol notu YOK ve IPC YOK (bu bir seed'dir, kullanıcı
    /// aksiyonu değil — ayrıca seed anında koşan bir run da olamaz). Geçersiz değer no-op (varsayılan korunur).</summary>
    public void SetPerfMode(string mode)
    {
        // [Fix round 1 — minor 3] İki ayrı soru, iki ayrı kapı: GEÇERLİLİK burada (tanınmayan seed = no-op,
        // bkz. yukarıdaki not — bir seed sessizce BAŞKA bir profile kaymamalı), TÜRETME ise tek yerde
        // (<see cref="RunViewModel.ProfileFor"/>). Bu yüzden ProfileFor'un Balanced fallback'i BURADA İSTENMEZ.
        if (PerfProfile.TryParse(mode) is null) return;
        PerfMode = mode;
        Parallelism = ProfileFor(mode).Parallelism;
    }

    // ---------------------------------------------------------------- [D7/T66] Settings — layers + repository

    /// <summary>[D7] Settings Save: yeni katman pattern'lerini uygular. <see cref="LayerPatterns"/> set edilir
    /// (sonraki Sync/Build komutlarıyla motora gider — A1/A5) ve konsola BİREBİR dim not yazılır
    /// (BuildApp.jsx:1423): katman kaldıysa <c>Layer definitions updated — {n} layers</c>, liste boşaltıldıysa
    /// <c>Layers removed — single project list</c>. Yeniden gruplama Core'dan <c>LayerName</c> olarak geri döner
    /// (App'te regex YOK — mimari kural).
    /// <para>Dışarıya AÇIK DEĞİLDİR: Settings'in tek giriş noktası <see cref="ApplySettingsAsync"/>'tir —
    /// katmanları kökten/Sync'ten ayrı uygulayan ikinci bir yol olmamalıdır.</para></summary>
    private void ApplyLayerPatterns(IReadOnlyList<LayerPattern> patterns)
    {
        LayerPatterns = patterns;
        AppendRunLine(patterns.Count > 0
            ? $"Layer definitions updated — {patterns.Count} layers"
            : "Layers removed — single project list");
    }

    /// <summary>[design v1.14.0/§9 · externals] Settings Save: harici proje listesini uygular. <see cref="ExternalProjects"/>
    /// set edilir ve konsola not düşülür; motora AYRI bir komut gitmez — liste, Save'in gönderdiği tek Sync'le
    /// (ve sonraki her Build'le) taşınır, bu yüzden bu metot o Sync'ten ÖNCE çağrılmak zorundadır.
    ///
    /// <para><b>[DEĞİŞEN KURAL — katman notundan FARKLI]</b> <see cref="ApplyLayerPatterns"/> HER Save'de
    /// KOŞULSUZ bir not yazar (sayı değişmese bile); harici projeler notu ise YALNIZ SAYI DEĞİŞTİYSE yazılır —
    /// brief'in birebir cümlesi: <i>"sayı değiştiyse ... yazılır ... Değişmediyse not yok."</i> Katman-only bir
    /// Save'de (harici liste aynı kaldıysa) gürültü OLMASIN diye.</para></summary>
    private void ApplyExternalProjects(IReadOnlyList<ExternalProject> externals)
    {
        int previousCount = ExternalProjects.Count;
        ExternalProjects = externals;
        if (externals.Count == previousCount) return; // değişmedi → not YOK
        AppendRunLine(externals.Count > 0
            ? $"External projects → {externals.Count} — built before the repository projects"
            : "External projects cleared");
    }

    /// <summary>
    /// [design v1.15.0 §2.9] Settings Save: "Pull before build" switch'ini uygular. Değer bir sonraki
    /// <see cref="StartRunCommand"/> ile motora gider (Sync'i ilgilendirmez — Sync zaten hiçbir çalışma
    /// kopyasına dokunmaz).
    ///
    /// <para>Not YALNIZ İKİ koşul birden sağlanınca yazılır: değer GERÇEKTEN değişti VE tanımlı harici proje
    /// var. Harici projesi olmayan bir kurulumda bu bayrak hiçbir şey yapmaz; orada not yazmak, olmayan bir
    /// işi anlatmak olurdu.</para>
    /// </summary>
    private void ApplyPullExternals(bool pull)
    {
        bool changed = pull != UpdateExternals;
        UpdateExternals = pull;
        if (!changed || ExternalProjects.Count == 0) return;
        AppendRunLine(pull
            ? "Pull before build on — external working copies update first"
            : "Pull before build off — external working copies are used as they are");
    }

    /// <summary>
    /// [spec 2026-09-18 §6.3] Settings Save: "Stash and switch branches" switch'ini uygular. Değer bir sonraki
    /// <see cref="CheckoutBranchCommand"/> ile motora gider. Not yalnız değer GERÇEKTEN değiştiyse yazılır
    /// (<see cref="ApplyPullExternals"/> deseni) — değişmeyen bir ayar her Save'de gürültü olurdu. Harici proje
    /// koşulunun karşılığı yoktur: bu ayar her repoda anlamlıdır.
    /// </summary>
    private void ApplyStashOnBranchSwitch(bool stash)
    {
        if (stash == StashOnBranchSwitch) return;
        StashOnBranchSwitch = stash;
        AppendRunLine(stash
            ? "Stash and switch branches on — uncommitted changes are stashed before a branch switch"
            : "Stash and switch branches off — a branch switch stops while there are uncommitted changes");
    }

    /// <summary>
    /// [RESOLVE Faz 4 / karar 11] Settings Save: "Resolve cycles at full priority" switch'ini uygular. Değer bir sonraki
    /// <see cref="StartRunCommand"/> ile motora gider. Not yalnız değer GERÇEKTEN değiştiyse yazılır
    /// (<see cref="ApplyStashOnBranchSwitch"/> deseni).
    /// </summary>
    private void ApplyResolveAtFullPriority(bool fullPriority)
    {
        if (fullPriority == ResolveAtFullPriority) return;
        ResolveAtFullPriority = fullPriority;
        AppendRunLine(fullPriority
            ? "Resolve cycles at full priority on — Resolve cycles runs at normal priority with no CPU cap"
            : "Resolve cycles at full priority off — Resolve cycles follows the performance mode");
    }

    /// <summary>[Settings] Save'in TEK giriş noktası: katman pattern'lerini uygular, gerekirse repo kökünü
    /// değiştirir ve TEK bir Sync gönderir.
    ///
    /// <para><b>Sıra ZORUNLUdur:</b> katmanlar Sync'ten ÖNCE uygulanır — <see cref="SyncWorkspaceCommand"/>
    /// <see cref="LayerPatterns"/>'i TAŞIR, ters sırada komut ESKİ pattern'lerle giderdi.</para>
    ///
    /// <para><b>Sync KOŞULSUZdur:</b> "repo mu katman mı değişti" ayrımı YAPILMAZ — Save'e basmak
    /// "senkronize et" demektir ve Sync salt-okurdur, tekrarı zararsızdır. ÜÇ kapı vardır:</para>
    ///
    /// <para>(a) <b>Koşu ya da workspace işi uçuşta</b> (<see cref="WorkspaceIdle"/> değil): katmanlar yine uygulanır
    /// ama kök DEĞİŞMEZ ve Sync GİTMEZ — koşan bir build'in ya da uçuştaki bir Sync/Clean/Optimize/checkout/pull'un
    /// kökünü altından çekmek doğru değildir, ikinci bir Sync de çift Sync olurdu. Bekleyen GERÇEK bir kök değişimi
    /// varsa konsola TEK satır düşer (<see cref="RepositoryChangeDeferredLine"/>): diyaloğun yol etiketi seçimi
    /// "Change…" anında ONAYLAMIŞ olur (etiket taslaktan okur), dolayısıyla sessiz bir düşürme kullanıcıya yalan
    /// söylerdi. Değişim yoksa satır YAZILMAZ — katman-only bir Save'de gürültü olurdu. [final review M3] Eskiden kapı
    /// yalnız koşuyu soruyordu: pull uçuşken Save ikinci bir Sync gönderirdi.</para>
    ///
    /// <para>(b) <b>Kök yok</b>: gidecek bir kök yoksa Sync anlamsızdır. Bu kapı <see cref="ApplyRepositoryRoot"/>
    /// çağrısından SONRA gelmek ZORUNDADIR — ilk repo Settings'ten seçildiğinde <see cref="RootPath"/> tam da
    /// orada dolar; kapı yukarıda olsaydı (ya da <paramref name="repositoryRoot"/> yerine <c>RootPath</c>'in
    /// ESKİ değerine bakılsaydı) yeni kullanıcının manşet yolculuğu — kökü seç, Save — Sync'siz kalır ve
    /// açıklamasız Boot'ta takılırdı.</para>
    ///
    /// <para><b>[kullanıcı kararı 2026-09-29] Boş kök = workspace yok.</b> Açık bir workspace'te kök boş kaydedilirse
    /// (Clear + Save ya da girdinin silinmesi) workspace KAPANIR (<see cref="CloseWorkspace"/>) ve uygulama ilk açılış
    /// görünümüne döner. Kapanış notlardan ÖNCE gelir: eski workspace'in konsolu ve akışı gider, bu Save'in kendi
    /// notları (katman, harici liste, General anahtarları) yeni sayfaya yazılır — kaybolmaz. Kapı (a) kapanışı da
    /// kapsar: koşu ya da workspace işi uçuştayken silme de diğer kök değişimleri gibi ertelenir. Yalnız boşluktan
    /// oluşan kök boştur (<see cref="RootOf"/>).</para>
    ///
    /// <para>(c) <b>Motor erişilemez</b> (<see cref="IsEngineUnavailable"/>): Sync GİTMEZ. Gerekçe orada
    /// yazılıdır — gönderim zaten hataya düşer ve şeritteki KALICI mesajla çelişen ikinci bir hata satırı
    /// üretirdi; Sync/Build/Rebuild düğmelerinin o durumda devre dışı kalmasıyla AYNI mantık.
    /// Save bir düğme DEĞİLDİR (CanExecute'la kapatılamaz), bu yüzden kapı metodun İÇİNDE durur. Katmanlar ve
    /// kök yine de uygulanır: ikisi de motora dokunmaz, kök kalıcı duruma yazılır (UiState.RepositoryRoot) ve
    /// motor geri geldiğinde ilk Sync onu taşır — motorun yokluğu bir kök seçimini YANLIŞ yapmaz.</para></summary>
    /// <param name="patterns">Taslağın katman tanımları.</param>
    /// <param name="repositoryRoot">Bekleyen repo kökü (değişmediyse mevcut kökün aynısı).</param>
    /// <param name="externals">[K5] Taslağın harici proje listesi — katmanlarla AYNI koşulsuz adımda uygulanır
    /// (motora dokunmaz, mid-run kilidinden ETKİLENMEZ — <see cref="ApplyLayerPatterns"/> ile AYNI gerekçe:
    /// ikisi de yalnız App içi durumdur, koşan bir build'i etkilemez).</param>
    /// <param name="pullExternalsBeforeBuild">[design v1.15.0] Bölümün "Pull before build" switch'i.</param>
    /// <param name="stashOnBranchSwitch">[spec 2026-09-18 §6.3] General'ın "Stash and switch branches" switch'i.</param>
    /// <param name="settingNotes">[P3 · P4] <see cref="BuildOrchestrator.App.Shell.ShellSwitches"/>'in <c>Commit</c>'inin
    /// ürettiği, DEĞİŞEN kabuk anahtarlarının (Start with Windows, Start minimized to tray, Close to tray, Show
    /// notifications) konsol notları —
    /// <see cref="ApplyStashOnBranchSwitch"/>'ten HEMEN SONRA, idle kapısından ÖNCE <see cref="AppendRunLine"/> ile
    /// sırayla yazılır (bu yüzden motor/workspace durumundan ETKİLENMEZ — <see cref="ApplyPullExternals"/>/
    /// <see cref="ApplyStashOnBranchSwitch"/>'in kendi notlarıyla AYNI konum).</param>
    /// <param name="resolveAtFullPriority">[RESOLVE Faz 4 / karar 11] General'ın "Resolve cycles at full priority"
    /// switch'i.</param>
    public async Task ApplySettingsAsync(IReadOnlyList<LayerPattern> patterns, string? repositoryRoot,
        IReadOnlyList<ExternalProject> externals, bool pullExternalsBeforeBuild = true, bool stashOnBranchSwitch = false,
        IReadOnlyList<string>? settingNotes = null, bool resolveAtFullPriority = true)
    {
        string root = RootOf(repositoryRoot);
        if (WorkspaceIdle && root.Length == 0 && HasWorkspace) CloseWorkspace();
        ApplyLayerPatterns(patterns);
        // SIRA: bayrağın notu listeyi TANIMLI görmeli — "harici proje varsa yaz" kuralı yeni listeye bakar.
        ApplyExternalProjects(externals);
        ApplyPullExternals(pullExternalsBeforeBuild);
        ApplyStashOnBranchSwitch(stashOnBranchSwitch);
        ApplyResolveAtFullPriority(resolveAtFullPriority);
        if (settingNotes is not null)
            foreach (var note in settingNotes) AppendRunLine(note);
        if (!WorkspaceIdle)
        {
            if (IsRepositoryChange(root)) AppendRunLine(RepositoryChangeDeferredLine(runInFlight: IsMidRunLocked));
            return;
        }
        bool rootChanged = ApplyRepositoryRoot(root);
        if (RootPath.Length == 0) return;
        if (IsEngineUnavailable) return;
        // [D3/T5 · design v1.13.2] Appended — ApplyLayerPatterns/ApplyRepositoryRoot bu Sync'ten HEMEN ÖNCE
        // KENDİ notunu yazdı (bkz. SyncCoreAsync XML doc'u); bir temizlik onu da silerdi.
        await SyncAfterRootChangeAsync(rootChanged);
    }

    /// <summary>[spec 2026-09-18 §6.2] Settings Save'in Sync'i (<see cref="SyncMode.Appended"/>). Kök GERÇEKTEN
    /// değiştiyse plan yüzeyi önce boşaltılır (<see cref="ClearPlanSurface"/>): Sync artık listeyi kendisi
    /// boşaltmaz ve eski reponun (kararsız) satırları yeni reponun topolojisi gelene dek ekranda kalırdı.
    /// <para>Tek çağıran <see cref="ApplySettingsAsync"/>'tir: bayrağı <see cref="ApplyRepositoryRoot"/>'un
    /// sonucundan geçer (katman-only bir Save'de <c>false</c> — yüzey boşalmaz) ve motor erişilemezken buraya
    /// hiç gelmez: o yolda satırlar yalnız kararları düşmüş hâlde kalır (<see cref="ResetRowsToHollow"/>), çünkü
    /// onları geri getirecek bir topoloji gelmeyecektir.</para></summary>
    private Task SyncAfterRootChangeAsync(bool rootChanged)
    {
        if (rootChanged) ClearPlanSurface();
        return SyncCoreAsync(SyncMode.Appended);
    }

    /// <summary>[Settings · K10] YENİ bir repo kökünü UYGULAR: kök değişir (<see cref="OnRootPathChanged"/> Empty→Boot
    /// geçişini sürer), satırlar hollow'a sıfırlanır, willBuild kümesi temizlenir ve run yüzeyi tazelenir.
    /// Sync GÖNDERMEZ — Sync'i çağıran (<see cref="ApplySettingsAsync"/>) Save'in TEK Sync'i içinde gönderir.
    /// <para>Boş kök ya da AYNI kökün yeniden seçilmesi NO-OP'tur ve <c>false</c> döner — aksi halde her satır
    /// boşuna hollow'a sıfırlanır ve gereksiz bir Sync gönderilirdi. Boş kökün kendi yolu vardır: açık bir
    /// workspace'i <see cref="CloseWorkspace"/> kapatır, notlardan ÖNCE.</para></summary>
    private bool ApplyRepositoryRoot(string root)
    {
        if (root.Length == 0 || !IsRepositoryChange(root)) return false;
        // [design v1.8.0 §2.9] Kök SONRADAN değiştiğinde konsola dim bir not düşer; eski reponun durumu da gider:
        // satırlar hollow'a döner, son Sync HEAD'i unutulur ve Save'in TEK Sync'i yeni kökte başlar (plan
        // yüzeyini SyncAfterRootChangeAsync boşaltır). (İlk kurulumda — Empty'den çıkarken — not YAZILMAZ: orada
        // zaten otomatik bir Sync akışı başlar ve not gürültü olurdu. Kapanmış bir workspace'ten sonra girilen kök
        // de ilk kurulumdur.)
        if (RootPath.Length > 0) AppendRunLine(RepositoryRootChangedLine(root));
        RootPath = root;
        ResetRowsToHollow();
        ForgetLastSync(); // [spec 2026-09-18 §6.1] eski kökün HEAD'i yeni kökte kıyas tabanı olamaz
        return true;
    }

    /// <summary>[Settings · K10] Verilen kök (<see cref="RootOf"/> ile normalize) GERÇEKTEN bir kök değişimi mi: AYNI
    /// kökün yeniden seçilmesi DEĞİLDİR (Windows yolları case-insensitive). [kullanıcı kararı 2026-09-29] Açık bir
    /// workspace'te boş kök bir değişimdir (workspace'i kapatır); workspace yokken boş kök değişim değildir.
    /// <see cref="ApplyRepositoryRoot"/>'un kapısı ile mid-run erteleme notunun koşulu (<see cref="ApplySettingsAsync"/>)
    /// AYNI soruyu sorar; soru TEK yerde durur (kopya YASAK) — aksi halde iki karşılaştırma zamanla ayrışır ve UI,
    /// motorun yaptığından başka bir şey anlatırdı.</summary>
    private bool IsRepositoryChange(string root) =>
        !string.Equals(root, RootPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>[kullanıcı kararı 2026-09-29] Settings'ten gelen kökün normal biçimi: boş ya da yalnız boşluk → boş kök
    /// ("workspace yok"), aksi hâlde olduğu gibi. Save'in kök kararlarının (kapanış, değişim, erteleme notu) TEK girdisi —
    /// yalnız boşluktan oluşan bir kök eskiden Save kapısında durduğu için buraya hiç ulaşmazdı.</summary>
    private static string RootOf(string? path) => string.IsNullOrWhiteSpace(path) ? "" : path;

    /// <summary>
    /// [kullanıcı kararı 2026-09-29] Workspace'i KAPATIR — Settings'te kök boş kaydedildi. Uygulama ilk açılış
    /// görünümüne döner: kök boşalır (faz <see cref="AppPhase.Empty"/>, HEAD izleyicisi bırakılır, boş kök kalıcı
    /// duruma yazılır), plan yüzeyi (satırlar, topoloji → graf) boşalır, git yüzeyi (branch, <c>N behind</c>) ve son
    /// Sync unutulur, seçim ve filtre düşer; konsol ve akış yeni bir sayfa açar. Motora hiçbir şey gitmez — çağıran
    /// <see cref="WorkspaceIdle"/> kapısının içindedir, kapanacak bir koşu ya da iş yoktur.
    /// <para>SIRA <see cref="BeginRunAsync"/>'inkiyle aynıdır: seçim konsol temizliğinden SONRA düşer — seçim düşünce
    /// kabuk anlatı belgesini yeniden kurar; temizlik ondan sonra gelseydi eski metin bir an geri gelirdi. Kök plan
    /// yüzeyinden ÖNCE boşalır: <see cref="ClearPlanSurface"/> fazı yalnız workspace varken Boot'a alır.</para>
    /// </summary>
    private void CloseWorkspace()
    {
        ActiveProjectId = null;
        ClearConsoleForNewOperation();
        ClearStreamForNewOperation();
        SelectedProjectId = null;
        HoveredProjectId = null;
        ActiveFilters = ProjectFilter.None;
        ProjectQuery = "";
        CurrentOperation = null;
        SyncErrorMessage = null;
        RunErrorMessage = null;
        RootPath = "";
        ClearPlanSurface();
        ForgetLastSync();
        ForgetGitSurface();
    }

    /// <summary>[final review M3] Save'in ertelediği kök değişiminin konsol notu — TEK yer. Koşu uçuştayken
    /// <c>run in flight</c>, bir workspace işi (Sync, Clean, Optimize, checkout, pull) uçuştayken
    /// <c>operation in flight</c>.</summary>
    internal static string RepositoryChangeDeferredLine(bool runInFlight) =>
        "Repository change deferred — " + (runInFlight ? "run in flight" : "operation in flight");

    /// <summary>[design v1.8.0 §2.9] Kök değişiminin konsol notu — BİREBİR metin, TEK yer.</summary>
    internal static string RepositoryRootChangedLine(string path) =>
        string.Format(CultureInfo.InvariantCulture, "Repository root → {0} — Sync required", path);

    /// <summary>[D7] Plan yüzeyini yeni bir taban için "hollow"a sıfırlar: satırlar (durum Pending, will
    /// bilinmiyor, süre/dep temizli), önizleme kümeleri (<c>ClearPreviewSets</c>) ve onlardan türeyen şerit
    /// yüzeyi (<c>wb</c>/<c>fin</c>/<c>allClean</c>).
    ///
    /// <para><b>Liste BOŞALTILMAZ, kararları boşaltılır.</b> Satırların varlığı topolojidendir ve bu reset'i
    /// tetikleyen olayların hiçbiri topolojiyi geçersizleştirmez; koleksiyon gerçekten boşalsa panel
    /// "<c>No projects found under this folder.</c>" derdi ve bu YANLIŞ olurdu.</para>
    ///
    /// <para>İki çağıranı vardır ve ikisinde de elimizdeki kararlar artık geçerli değildir: repo değişimi
    /// (<see cref="ApplyRepositoryRoot"/> — kararlar eski köke aittir) ve configuration geçişinin Sync'inin başlangıcı
    /// (<see cref="OnSyncStarted"/>, <see cref="SyncModeRules.DropsDecisions"/> — kararlar eski configuration'a aittir).
    /// <b>Clean bundan DAHA İLERİ gider</b> — <see cref="ClearPlanSurface"/>: orada satırlar da düğümler de
    /// kalkar.</para></summary>
    private void ResetRowsToHollow()
    {
        foreach (var row in Projects)
        {
            row.State = ProjectRowState.Pending;
            row.WillBuild = null;
            row.Conditional = false; // plan bayrağının çifti (WillBuild'le birlikte): bilinmeyen satır koşullu söz vermez
            // [spec 2026-09-18 §1-15] Karar düşünce defter notu da düşer: üçgen artık gerekçeden de okunur
            // (ProjectRowViewModel.WarningRoots) ve bilinmiyor modundaki bir satır bekleyen bir bağımlılık
            // iddia edemez. Gerekçe + kökler kararla birlikte gider.
            row.WillBuildReason = null;
            row.DependencyRoots = null;
            row.DepIssues = null;
            row.DurationMs = 0;
        }
        ClearPreviewSets();     // kümeler ADD-ONLY'dir: temizlenmezse şeritteki wb sayacı bayat kalır
        RefreshRunSurface();    // sayaç/görünür-liste + willBuild yüzeyi
        RaiseRowDecisionsChanged(); // [Task 4 review I-1] graf da başlangıç moduna AYNI anda düşer
    }

    /// <summary>
    /// [clean · kullanıcı kararı 2026-09-12] Plan yüzeyini TAMAMEN boşaltır: satırlar, topoloji (yani graf),
    /// döngü haritası ve will-build kümesi. Çağıranlar Clean ve Optimize'ın tıklama anı (çıktılar siliniyor/
    /// onarılıyor, ekranda duran hiçbir şey artık diskte bir şeye karşılık gelmiyor) ve Sync'e giden gerçek bir
    /// kök değişimidir (<see cref="SyncAfterRootChangeAsync"/>). Sync'in kendisi VM'i boşaltmaz (spec 2026-09-18
    /// §1-13); Sync düğmesi ve branch değişimi yalnız EKRANI baştan başlatır (<see cref="BeginPlanSurfaceRestart"/>).
    /// Liste yeniden Sync'in yayınladığı topolojiyle — imza unutulduğu için reveal'le — dolar.
    ///
    /// <para><b>Faz <see cref="AppPhase.Boot"/>'a alınır</b> ve bu kozmetik değildir: davet kararı
    /// (<c>ListInvite.Resolve</c>) boş listeyi <c>Idle</c> fazında "klasörde proje yok" diye okur ve bu YANLIŞ
    /// olurdu. Boot, "henüz bilinmiyor" demenin mevcut yoludur. Ardından gelen Sync fazı zaten
    /// <c>Syncing</c>'e taşır. [kullanıcı kararı 2026-09-29] Workspace yokken (<see cref="CloseWorkspace"/> kökü
    /// önce boşaltır) faz <see cref="AppPhase.Empty"/>'de kalır — onu <see cref="OnRootPathChanged"/> kurar.</para>
    ///
    /// <para><see cref="TopologyChanged"/> AÇIKÇA ateşlenir: grafı kuran tek sinyal odur, yoksa liste boşalırken
    /// düğümler ekranda kalırdı.</para>
    /// </summary>
    private void ClearPlanSurface()
    {
        Projects.Clear();
        Topology = [];
        Solutions = [];
        _cycleGroups = null;
        _cycleMemberCount = 0;
        OnPropertyChanged(nameof(HasCycles));
        OnPropertyChanged(nameof(HasTopology));
        if (HasWorkspace) Phase = AppPhase.Boot;
        ClearPreviewSets();
        // [spec 2026-09-18 §6.2] İmza da unutulur: boşalan liste, zincirlenen Sync AYNI yapıyı getirse de
        // reveal'le dolmalıdır (OnWorkspaceTopology yalnız imza değişince ateşler).
        _lastTopologySignature = null;
        TopologyChanged?.Invoke(this, EventArgs.Empty);
        RefreshRunSurface();
    }

    /// <summary>
    /// [task 3 · kullanıcı kararı 2026-09-19] Plan yüzeyi EKRANDA baştan başlıyor: liste ve graf görünümde boştur,
    /// bir sonraki <see cref="TopologyChanged"/> onları standart açılışla (reveal, graf fit) geri getirir. Yazıcısı
    /// yalnız <see cref="BeginPlanSurfaceRestart"/> (kurar) ve <see cref="ReplayPlanSurface"/> (bırakır).
    ///
    /// <para><b>Neden <see cref="ClearPlanSurface"/> DEĞİL:</b> o VM'i boşaltır (satırlar, topoloji, faz
    /// <see cref="AppPhase.Boot"/>) ve boş liste davetini, grafın "Sync'ten sonra" etiketini getirir; burada ise
    /// yalnız EKRAN boşalır — satırlar, kararları, filtre ve topoloji VM'de durur. Sync düşerse geri getirilecek
    /// yüzey budur ve ekran hiçbir yolda boş kalmaz.</para>
    /// </summary>
    public bool PlanSurfaceRestarting { get; private set; }

    /// <summary>[task 3] Sync isteği anında (<see cref="SyncCoreAsync"/>, kip <see cref="SyncModeRules.RestartsPlanSurface"/>)
    /// ekranı boşaltır — konsol ve akışın temizlendiği AYNI anda. Kabuk bayrağı görünce liste ve grafı boş gösterir;
    /// VM'e dokunulmaz.</summary>
    private void BeginPlanSurfaceRestart()
    {
        PlanSurfaceRestarting = true;
        OnPropertyChanged(nameof(PlanSurfaceRestarting));
    }

    /// <summary>[task 3] Yüzeyi yeniden kurdurur: bayrak düşer ve <see cref="TopologyChanged"/> ateşlenir — liste ve
    /// graf reveal'le, graf fit hâlde gelir. İki çağıranı var: yeni topoloji (<see cref="OnWorkspaceTopology"/>) ve
    /// topolojisiz biten Sync (<see cref="EndPlanSurfaceRestart"/>). Bayrak ateşlemeden ÖNCE düşer: aboneler
    /// (liste tazelemesi) bayrak açıkken kendini susturur.</summary>
    private void ReplayPlanSurface()
    {
        if (PlanSurfaceRestarting)
        {
            PlanSurfaceRestarting = false;
            OnPropertyChanged(nameof(PlanSurfaceRestarting));
        }
        TopologyChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>[task 3] Sync topoloji getirmeden bitti (gönderim düştü, <c>planFailed</c>, motor kaybı, topolojisiz
    /// tamamlanma): ekran boş KALMAZ — önceki liste ve graf geri gelir. Yüzey zaten yeniden kurulduysa no-op.
    /// Çağıranlar Sync'i serbest bırakan geçişlerdir: <see cref="ReleaseSyncRequest"/>, <see cref="ReleaseSyncPhase"/>,
    /// <see cref="TryConsumeSyncFailure"/>.</summary>
    private void EndPlanSurfaceRestart()
    {
        if (PlanSurfaceRestarting) ReplayPlanSurface();
    }
}
