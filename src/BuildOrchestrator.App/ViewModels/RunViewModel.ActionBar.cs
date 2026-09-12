using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [D6/T40+T12+T43-UI] <see cref="RunViewModel"/>'in <b>aksiyon-barı yüzeyi</b>: statü chip'i filtre toggle'ı,
/// K3 branch seçimi (worktree zorlama + niyet satırları — <c>git switch</c> DEĞİL), worktree auto-ad üretimi/silme
/// ve perf profili seed'i. Ayrı bir partial dosyada, çünkü ana dosya run/log/ETA yüzeyini, Workspace.cs Sync/
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
    /// VEYA'lıdır (✓ + ✗ = "bu koşuda derlenenler"). Yeni küme her seferinde YENİ bir örnektir — yerinde
    /// mutasyon <c>PropertyChanged</c> yaymaz ve liste bayat kalırdı.</para></summary>
    public void ToggleFilter(string? filter)
    {
        SelectedProjectId = null;
        if (filter is null) { ActiveFilters = ProjectFilter.None; return; }

        var next = new HashSet<string>(ActiveFilters, StringComparer.Ordinal);
        if (!next.Remove(filter)) next.Add(filter);
        ActiveFilters = next.Count == 0 ? ProjectFilter.None : next;
    }

    // ---------------------------------------------------------------- [K3] branch seçimi + worktree "forced"

    /// <summary>Aktif branch'in adı (<see cref="Branches"/> içinde <c>IsActive</c> olan) — <see cref="IsWorktreeForced"/>
    /// türetimi bunu kullanır. Envanter henüz gelmediyse (IPC öncesi) <c>null</c>.</summary>
    public string? ActiveBranchName => Branches.FirstOrDefault(b => b.IsActive)?.Name;

    /// <summary>[T40 · K3] Aktif-OLMAYAN bir branch seçili mi → worktree ZORUNLUdur (worktree popover'ında switch
    /// disabled + on; branch chip'in worktree değeri de zorunlu olarak worktree adını gösterir). Aktif branch
    /// (ya da envanter yokken) <c>false</c>. Branch adları git'te büyük/küçük harfe DUYARLIdır → <c>Ordinal</c>.</summary>
    public bool IsWorktreeForced =>
        ActiveBranchName is { } active && !string.Equals(Branch, active, StringComparison.Ordinal);

    /// <summary>[v1.16.0] <see cref="CanShowBehind"/> <see cref="IsWorktreeForced"/>'a bağlıdır ve o da
    /// türetilmiş bir özelliktir — branch seçimi ya da envanter değiştiğinde chip'in tazelenmesi için bildirim
    /// ELLE atılır (türetilmiş özellikler kendiliğinden PropertyChanged üretmez).</summary>
    private void NotifyBehindChip()
    {
        OnPropertyChanged(nameof(CanShowBehind));
        PullRepositoryCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- [T2 fix-1 · C1] açık seçim ↔ bayat seed

    /// <summary>
    /// [T2 fix-1 · C1] Kullanıcı branch'i POPOVER'DAN AÇIKÇA seçti mi. <see cref="Branch"/> tek başına bunu
    /// SÖYLEYEMEZ: aynı alan hem açık seçimle hem diskteki <c>UiState</c> seed'iyle hem de envanter seed'iyle
    /// (<see cref="OnBranchList"/>) dolar.
    ///
    /// <para><b>Neden ayrım ZORUNLU (ölçülen kusur):</b> T2'nin ilk hâlinde seed YALNIZ <c>Branch</c> boşken
    /// koşuyordu, yani ilk Sync <c>"main"</c> yazıp diske persist ediyordu. Kullanıcı terminalde
    /// <c>git checkout feature/y</c> yapınca uygulama kendini ASLA düzeltemiyordu: <c>Branch</c> <c>"main"</c>
    /// kalıyor, <see cref="IsWorktreeForced"/> true oluyor ve build <c>main</c>'in committed HEAD'ini
    /// derliyordu — kullanıcı <c>feature/y</c> üzerindeyken. Ayrım sayesinde AÇIK OLMAYAN her değer
    /// envanterle birlikte TAZELENİR.</para>
    /// </summary>
    private bool _branchChosenByUser;

    /// <summary>[test yüzeyi] Bkz. <see cref="_branchChosenByUser"/>.</summary>
    internal bool BranchChosenByUser => _branchChosenByUser;

    /// <summary>
    /// [T2 fix-1 · C1/I4] <see cref="StartRunCommand.Branch"/>'e giden değer — <see cref="Branch"/>'ten
    /// KASITLI olarak farklıdır.
    ///
    /// <para><b>Karar:</b> <c>Branch</c> bir <b>görüntüleme</b> değeridir (chip · title bar · popover source
    /// satırı). Supervisor içinse bu alan bir <b>NİYET</b>tir: dolu gelmesi (a) worktree'yi ZORUNLU kılan
    /// 3-durum matrisini devreye sokar (<c>Supervisor/Program.cs:215-216</c>) ve (b) "aktif branch çözülemedi"
    /// (detached HEAD / bozuk git) durumunu <c>warn + in-place</c> yerine <b>run'ı hiç başlatmayan</b> bir
    /// hataya çevirir (<c>:207-208</c>). Bir seed değerini niyet diye göndermek tam da C1/I4'ün kök nedenidir.
    /// Bu yüzden komuta YALNIZ kullanıcının açık seçimi gider; aksi halde boş — ve Supervisor'ın zaten yazılı
    /// olan sözleşmesi devreye girer: <i>"Branch boş gelirse niyet aktif branch'in COMMITTED hâlidir"</i>
    /// (<c>Program.cs:214</c>), <c>UseWorktree</c> kapalıysa tek bir git çağrısı bile yapılmadan in-place
    /// (<c>:183</c>). Yani açık seçim yapmamış kullanıcı için davranış T2 ÖNCESİYLE birebir aynıdır.</para>
    ///
    /// <para><b>Sync BUNU KULLANMAZ</b> ve kullanmamalıdır: <c>SyncWorkspaceCommand.Branch</c> yalnız
    /// <c>git fetch origin &lt;ref&gt;</c>'in ref'ini ve <c>syncCompleted</c> echo'sunu besler, worktree
    /// matrisini DEĞİL. Orada görüntüleme değeri doğru olandır (aksi halde fetch boş ref'e giderdi).</para>
    /// </summary>
    internal string RunBranchIntent => _branchChosenByUser ? Branch : "";

    /// <summary>
    /// [T2 fix-1 · C1] <b>Worktree'nin ETKİN durumu</b> — <c>forced || kullanıcının toggle'ı</c>. Kullanıcıya
    /// gösterilen ve motora giden TEK doğruluk kaynağı budur; <see cref="UseWorktree"/> yalnız kullanıcının
    /// KENDİ tercihini taşır (kalıcı duruma yazılan da odur).
    ///
    /// <para><b>Ölçülen kusur:</b> zorlamayı yalnız <see cref="SelectBranch"/> uyguluyordu (<c>UseWorktree</c>'yi
    /// mutasyona uğratarak); seed yolu uygulamıyordu. Sonuç <b>forced + <c>UseWorktree=false</c></b>
    /// kombinasyonuydu: build worktree'yi ZORUNLU açıp başka bir branch'in committed HEAD'ini derlerken chip
    /// <c>"off"</c>, popover switch'i işaretsiz-ve-disabled ve source satırı "working directory — local changes
    /// included" diyordu — UI motorun yapacağının TERSİNİ gösteriyordu. Türetilmiş değerle o kombinasyon
    /// ÜRETİLEMEZ hâle gelir.</para>
    ///
    /// <para><b>Neden mutasyon DEĞİL türetim:</b> prototipin semantiği de budur (<c>BuildApp.jsx:1153</c>
    /// <c>wtActive = forced || wtOn</c>) — zorlama bir KATMANDIR, kullanıcının tercihini KALICI olarak
    /// ezmez. Mutasyon denendi ve ölçüldü: aktif-olmayan bir branch'ten aktife DÖNÜNCE kullanıcının
    /// <c>false</c> tercihi geri gelmiyordu (<c>ActionBarTests.Selecting_the_active_branch_…</c> kırmızı
    /// verdi) ve zorlama diske persist ediliyordu.</para>
    /// </summary>
    public bool EffectiveUseWorktree => UseWorktree || IsWorktreeForced;

    /// <summary>
    /// [T40 · K3 — prototipten SAPMA, plan kazanır (BuildApp.jsx:1336-1353)] Branch seçimi. Aktif-OLMAYAN bir branch
    /// seçilince: worktree ZORUNLU ON, proje durumları Pending'e sıfırlanır, faz <see cref="AppPhase.Boot"/>'a düşer
    /// ve konsola İKİ niyet satırı yazılır — <c>git switch --detach …</c> satırı <b>YAZILMAZ</b> (prototipteki o
    /// satır App'te yanıltıcı olurdu: gerçek switch Build anında worktree kurulumunda olur). Aktif branch seçilince:
    /// yalnız <see cref="Branch"/> set edilir (worktree zorlaması/reset YOK).
    /// </summary>
    public void SelectBranch(BranchRef branch)
    {
        ArgumentNullException.ThrowIfNull(branch);
        // [T2 fix-1 · C1] AÇIK seçim: bundan sonra envanter seed'i bu değeri EZMEZ ve StartRunCommand'a
        // gerçek bir NİYET olarak gider (bkz. RunBranchIntent).
        _branchChosenByUser = true;
        Branch = branch.Name;
        NotifyBehindChip();   // [v1.16.0] seçim aktif branch'ten ayrılınca (ya da ona dönünce) chip değişir
        if (branch.IsActive) return; // aktif branch: worktree zorlaması/reset/niyet-satırı YOK

        WorktreeName = null;  // seçili hedef worktree'yi auto'ya döndür (BuildApp.jsx:1340)
        UseWorktree = true;   // aktif-olmayan branch → kullanıcının toggle'ı da açılır (BuildApp.jsx:1342)
        // BuildApp.jsx:1345-1346 status='discovered' → Pending, will='unknown' → hollow, eng.willBuild = new Set()
        ResetRowsToHollow();
        Phase = AppPhase.Boot;       // BuildApp.jsx:1347
        string sha7 = Short7(branch.Sha);
        AppendRunLine($"branch target: {branch.Name} ({sha7}) — worktree will be used at Build");
        AppendRunLine($"Branch changed: {branch.Name} — Sync required"); // BuildApp.jsx:1350
    }

    /// <summary>Branch popover'daki mono SHA + niyet satırındaki <c>{sha7}</c> için 7-haneli kısaltma (uzunsa kırp,
    /// zaten kısaysa olduğu gibi) — brief 7-hane pinler.</summary>
    internal static string Short7(string sha) => sha.Length > 7 ? sha[..7] : sha;

    /// <summary>
    /// [Harici projeler] Bir REVİZYON KİMLİĞİNİ kısaltır. Kısaltma yalnız gerçek bir git sha'sına (40 hex)
    /// uygulanır; başka her değer olduğu gibi gösterilir.
    ///
    /// <para>Gerekçe: sha yuvası artık iki farklı sürüm kontrolünün kimliğini taşıyor. Bir TFVC changeset'i
    /// (<c>C48213</c>) kırpılırsa anlamsız bir sayıya döner — 7 hane bir git alışkanlığıdır, evrensel bir
    /// biçim değil.</para>
    /// </summary>
    internal static string ShortSha(string? revision) => Core.Git.RevisionText.Short(revision);

    // ---------------------------------------------------------------- [T40] worktree auto-ad + silme

    /// <summary>[T40] Worktree otomatik adı (BuildApp.jsx:1154-1155): slug = branch'te <c>/</c>→<c>-</c>; ek sayı =
    /// (aynı slug önekiyle başlayan mevcut worktree sayısı) + 1. Saf/statik — WPF'siz test edilir.</summary>
    public static string AutoWorktreeName(string branch, IEnumerable<Worktree> worktrees)
    {
        string slug = branch.Replace('/', '-');
        int existing = worktrees.Count(w => w.Name.StartsWith(slug, StringComparison.Ordinal));
        return string.Create(CultureInfo.InvariantCulture, $"{slug}-{existing + 1}");
    }

    /// <summary>[T40] Seçili worktree adı; auto (<c>null</c>) ise türetilen ada döner (worktree chip değeri + popover
    /// hedef satırı bunu okur).</summary>
    public string EffectiveWorktreeName => WorktreeName ?? AutoWorktreeName(Branch, Worktrees);

    /// <summary>[T40] Havuzdan bir worktree sil (BuildApp.jsx:1582): <see cref="DeleteWorktreeCommand"/> gönderilir ve
    /// konsola dim satır yazılır. Silinen worktree seçiliyse seçim auto'ya döner. Supervisor silince güncel envanteri
    /// (<see cref="WorktreeListEvent"/>) yayınlar — liste ORADAN uzlaşır (yerel <see cref="Worktrees"/> reset'i YOK).</summary>
    public async Task DeleteWorktreeAsync(string name)
    {
        if (string.Equals(WorktreeName, name, StringComparison.Ordinal)) WorktreeName = null;
        AppendRunLine($"worktree removed: {name}");
        await TrySendAsync(new DeleteWorktreeCommand(RootPath, name), "deleteWorktree");
    }

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

    /// <summary>[Settings] Save'in TEK giriş noktası: katman pattern'lerini uygular, gerekirse repo kökünü
    /// değiştirir ve TEK bir Sync gönderir.
    ///
    /// <para><b>Sıra ZORUNLUdur:</b> katmanlar Sync'ten ÖNCE uygulanır — <see cref="SyncWorkspaceCommand"/>
    /// <see cref="LayerPatterns"/>'i TAŞIR, ters sırada komut ESKİ pattern'lerle giderdi.</para>
    ///
    /// <para><b>Sync KOŞULSUZdur:</b> "repo mu katman mı değişti" ayrımı YAPILMAZ — Save'e basmak
    /// "senkronize et" demektir ve Sync salt-okurdur, tekrarı zararsızdır. ÜÇ kapı vardır:</para>
    ///
    /// <para>(a) <b>Koşu uçuşta</b> (<see cref="IsMidRunLocked"/>): katmanlar yine uygulanır ama kök DEĞİŞMEZ
    /// ve Sync GİTMEZ — koşan bir build'in kökünü altından çekmek doğru değildir
    /// (<see cref="ChangeRepositoryAsync"/> de mid-run'da no-op'tur). Bekleyen GERÇEK bir kök değişimi varsa
    /// konsola TEK satır düşer: diyaloğun yol etiketi seçimi "Change…" anında ONAYLAMIŞ olur (etiket taslaktan
    /// okur), dolayısıyla sessiz bir düşürme kullanıcıya yalan söylerdi. Değişim yoksa satır YAZILMAZ —
    /// katman-only bir Save'de gürültü olurdu.</para>
    ///
    /// <para>(b) <b>Kök yok</b>: gidecek bir kök yoksa Sync anlamsızdır. Bu kapı <see cref="ApplyRepositoryRoot"/>
    /// çağrısından SONRA gelmek ZORUNDADIR — ilk repo Settings'ten seçildiğinde <see cref="RootPath"/> tam da
    /// orada dolar; kapı yukarıda olsaydı (ya da <paramref name="repositoryRoot"/> yerine <c>RootPath</c>'in
    /// ESKİ değerine bakılsaydı) yeni kullanıcının manşet yolculuğu — kökü seç, Save — Sync'siz kalır ve
    /// açıklamasız Boot'ta takılırdı.</para>
    ///
    /// <para>(c) <b>Motor erişilemez</b> (<see cref="IsEngineUnavailable"/>): Sync GİTMEZ. Gerekçe orada
    /// yazılıdır — gönderim zaten hataya düşer ve şeritteki KALICI mesajla çelişen ikinci bir hata satırı
    /// üretirdi; Sync/Build/Rebuild/Retry/Continue düğmelerinin o durumda devre dışı kalmasıyla AYNI mantık.
    /// Save bir düğme DEĞİLDİR (CanExecute'la kapatılamaz), bu yüzden kapı metodun İÇİNDE durur. Katmanlar ve
    /// kök yine de uygulanır: ikisi de motora dokunmaz, kök kalıcı duruma yazılır (UiState.RepositoryRoot) ve
    /// motor geri geldiğinde ilk Sync onu taşır — motorun yokluğu bir kök seçimini YANLIŞ yapmaz.</para></summary>
    /// <param name="patterns">Taslağın katman tanımları.</param>
    /// <param name="repositoryRoot">Bekleyen repo kökü (değişmediyse mevcut kökün aynısı).</param>
    /// <param name="externals">[K5] Taslağın harici proje listesi — katmanlarla AYNI koşulsuz adımda uygulanır
    /// (motora dokunmaz, mid-run kilidinden ETKİLENMEZ — <see cref="ApplyLayerPatterns"/> ile AYNI gerekçe:
    /// ikisi de yalnız App içi durumdur, koşan bir build'i etkilemez).</param>
    /// <param name="pullExternalsBeforeBuild">[design v1.15.0] Bölümün "Pull before build" switch'i.</param>
    public async Task ApplySettingsAsync(IReadOnlyList<LayerPattern> patterns, string? repositoryRoot,
        IReadOnlyList<ExternalProject> externals, bool pullExternalsBeforeBuild = true)
    {
        ApplyLayerPatterns(patterns);
        // SIRA: bayrağın notu listeyi TANIMLI görmeli — "harici proje varsa yaz" kuralı yeni listeye bakar.
        ApplyExternalProjects(externals);
        ApplyPullExternals(pullExternalsBeforeBuild);
        if (IsMidRunLocked)
        {
            if (IsRepositoryChange(repositoryRoot)) AppendRunLine("Repository change deferred — run in flight");
            return;
        }
        ApplyRepositoryRoot(repositoryRoot);
        if (RootPath.Length == 0) return;
        if (IsEngineUnavailable) return;
        // [D3/T5 · design v1.13.2] clearBuffers:false — ApplyLayerPatterns/ApplyRepositoryRoot bu Sync'ten
        // HEMEN ÖNCE KENDİ notunu yazdı (bkz. SyncCoreAsync XML doc'u); ikinci bir clear onu da silerdi.
        await SyncCoreAsync(clearBuffers: false);
    }

    /// <summary>[D7 · K10] Kabuğun "Choose Folder" yolu: yeni bir repo kökü seçilince kökü değiştirir, proje
    /// durumlarını sıfırlar (yeni repo = yeni taban) ve HEMEN Sync başlatır — burada bir Save yoktur. Settings
    /// diyaloğu bu yolu KULLANMAZ; orada seçim Save'e ertelenir (<see cref="ApplySettingsAsync"/>). Klasör
    /// seçici çağıranın enjekte ettiği bir seam'dir — bu metot yalnız sonucu (yol) alır.</summary>
    public async Task ChangeRepositoryAsync(string path)
    {
        if (IsMidRunLocked) return;
        if (!ApplyRepositoryRoot(path)) return;
        // [D3/T5 · design v1.13.2] clearBuffers:false — ilk kurulumda not YOK (konsol zaten boş), sonraki bir
        // kök değişiminde ApplyRepositoryRoot bu Sync'ten HEMEN ÖNCE KENDİ notunu yazdı; ikinci bir clear onu
        // da silerdi (bkz. SyncCoreAsync XML doc'u).
        await SyncCoreAsync(clearBuffers: false);
    }

    /// <summary>[Settings · K10] Repo kökünü UYGULAR: kök değişir (<see cref="OnRootPathChanged"/> Empty→Boot
    /// geçişini sürer), satırlar hollow'a sıfırlanır, willBuild kümesi temizlenir ve run yüzeyi tazelenir.
    /// Sync GÖNDERMEZ — o kararı çağıran verir (Choose Folder hemen, Settings Save'de tek Sync içinde). İki
    /// yolun ortak adımı burada TEK yerdedir (kopya yasağı).
    /// <para>Boş yol ya da AYNI kökün yeniden seçilmesi NO-OP'tur ve <c>false</c> döner — aksi halde her satır
    /// boşuna hollow'a sıfırlanır ve gereksiz bir Sync gönderilirdi. Kararı <see cref="IsRepositoryChange"/>
    /// verir.</para></summary>
    private bool ApplyRepositoryRoot(string? path)
    {
        if (!IsRepositoryChange(path)) return false;
        // [design v1.8.0 §2.9] Kök SONRADAN değiştiğinde konsola dim bir not düşer: durum SIFIRLANMAZ,
        // kullanıcı Sync'ler. (İlk kurulumda — Empty'den çıkarken — not YAZILMAZ: orada zaten otomatik bir
        // Sync akışı başlar ve not gürültü olurdu.)
        if (RootPath.Length > 0) AppendRunLine(RepositoryRootChangedLine(path));
        RootPath = path;
        ResetRowsToHollow();
        return true;
    }

    /// <summary>[Settings · K10] Verilen yol GERÇEKTEN bir kök değişimi mi: boş yol DEĞİLDİR, AYNI kökün
    /// yeniden seçilmesi de DEĞİLDİR (Windows yolları case-insensitive). <see cref="ApplyRepositoryRoot"/>'un
    /// kapısı ile mid-run erteleme notunun koşulu (<see cref="ApplySettingsAsync"/>) AYNI soruyu sorar; soru
    /// TEK yerde durur (kopya YASAK) — aksi halde iki karşılaştırma zamanla ayrışır ve UI, motorun yaptığından
    /// başka bir şey anlatırdı.</summary>
    private bool IsRepositoryChange([NotNullWhen(true)] string? path) =>
        !string.IsNullOrEmpty(path) && !string.Equals(path, RootPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>[design v1.8.0 §2.9] Kök değişiminin konsol notu — BİREBİR metin, TEK yer.</summary>
    internal static string RepositoryRootChangedLine(string path) =>
        string.Format(CultureInfo.InvariantCulture, "Repository root → {0} — Sync required", path);

    /// <summary>[D7] Plan yüzeyini yeni bir taban için "hollow"a sıfırlar: satırlar (durum Pending, will
    /// bilinmiyor, süre/dep temizli), <see cref="RunViewModel._willBuildIds"/> kümesi ve ondan türeyen şerit
    /// yüzeyi (<c>wb</c>/<c>fin</c>/<c>allClean</c>).
    ///
    /// <para><b>Liste BOŞALTILMAZ, kararları boşaltılır.</b> Satırların varlığı topolojidendir ve bu reset'i
    /// tetikleyen olayların hiçbiri topolojiyi geçersizleştirmez; koleksiyon gerçekten boşalsa panel
    /// "<c>No projects found under this folder.</c>" derdi ve bu YANLIŞ olurdu.</para>
    ///
    /// <para>Üç çağıranı vardır ve üçü de "elimizdeki kararlar artık geçerli değil" demenin ayrı bir
    /// biçimidir: branch değişimi (<see cref="SelectBranch"/>), repo değişimi
    /// (<see cref="ApplyRepositoryRoot"/>) ve Clean'in başlaması (<c>RunViewModel.Workspace.OnCleanStarted</c> —
    /// çıktılar siliniyor). Üçlü blok TEK yerde durur (kopya YASAK); çağıranların kendine ait olan tek şey
    /// konsol notu ve faz seçimidir.</para></summary>
    private void ResetRowsToHollow()
    {
        foreach (var row in Projects)
        {
            row.State = ProjectRowState.Pending;
            row.WillBuild = null;
            row.DepIssues = null;
            row.DurationMs = 0;
        }
        _willBuildIds.Clear();  // küme ADD-ONLY'dir: temizlenmezse şeritteki wb sayacı bayat kalır
        RefreshRunSurface();    // sayaç/görünür-liste + willBuild yüzeyi
    }
}
