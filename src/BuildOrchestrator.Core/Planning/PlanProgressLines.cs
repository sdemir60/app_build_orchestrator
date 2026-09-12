using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Core.Planning;

/// <summary>
/// Planlama adımlarının kullanıcıya görünen satırları — TEK kaynak.
///
/// <para><b>İki tüketici, tek metin:</b> aynı planlama pipeline'ı (tarama → csproj değerlendirme → graf →
/// topo → incremental) iki yerden koşar: <c>SyncWorkspaceService</c> (Sync komutu) ve Supervisor'ın
/// <c>BuildRunPlan</c>'ı (bir run'ın taze segmenti). Satırlar Core'da toplanır çünkü CLAUDE.md aynı metnin
/// iki yerde tanımlanmasını yasaklar — biri güncellenip diğeri unutulursa kullanıcı AYNI işin iki farklı
/// adını görürdü.</para>
///
/// <para><b>Neden run tarafında da yayınlanır:</b> Build'e basıldığında motor planlamayı yeniden koşar
/// (Sync'ten bu yana çalışma ağacı değişmiş olabilir; ayrıca worktree hazırlığı ve <c>MSBuild.exe</c>
/// çözümü YALNIZ burada vardır). O pencere eskiden TEK SATIR bile yazmıyordu: App konsolu temizliyor,
/// şerit önceki metinde donuyordu — tıklamanın kaydedildiğine dair hiçbir kanıt yoktu.</para>
///
/// <para>Metin İngilizce (uygulama İngilizce-only). Sayılar çağıranın elindeki gerçek plandan gelir;
/// burada hiçbir şey HESAPLANMAZ.</para>
/// </summary>
public static class PlanProgressLines
{
    /// <summary>Worktree hazırlığı (git) — planlamanın İLK adımı ve tek başına saniyeler sürebilir, bu yüzden
    /// satır işin ÖNCESİNDE yazılır. Yalnız run yolunda görülür (Sync worktree hazırlamaz).</summary>
    /// <param name="branch">Seçili branch; boşsa (aktif branch'in worktree'si) adsız biçim kullanılır.</param>
    public static string PreparingWorktree(string? branch)
        => string.IsNullOrWhiteSpace(branch) ? "Preparing worktree" : $"Preparing worktree for '{branch}'";

    /// <summary>Tarama bitti — sayı taramanın SONUCUDUR, bu yüzden satır işin ardından yazılır.</summary>
    public static string ScanningSolutions(int solutions) => $"Scanning solutions ({solutions})";

    public static string ReadingProjectItems(int projects) => $"Reading HintPath/Compile items ({projects} projects)";

    public static string DependencyGraph(int cycles) => $"Dependency graph — {cycles} cycles";

    public static string BuildOrderResolved(int nodes) => $"Build order resolved ({nodes})";

    /// <summary>
    /// [v1.16.0] Sync'in ikinci satırı: yerel HEAD ve uzak uçtan mesafesi. <paramref name="behind"/>
    /// <c>null</c> ise yalnız HEAD yazılır — mesafe bilinmiyor (fetch degrade oldu ya da seçili branch aktif
    /// branch değil) ve uydurma bir sayı yazmaktansa susmak doğrudur.
    ///
    /// <para>Uzak uçtaki commit'in KİMLİĞİ yazılmaz: kullanıcı onu pull etmedikçe o commit yereldeki hiçbir
    /// şeyi anlatmaz. Anlamlı olan tek şey MESAFEDİR — ve o mesafe alt bardaki <c>N behind</c> chip'iyle
    /// aynı sayıdır.</para>
    /// </summary>
    public static string HeadDistance(string headRevision, int? behind, string branch) => behind switch
    {
        null => $"HEAD {headRevision}",
        0 => $"HEAD {headRevision} · up to date with origin/{branch}",
        1 => $"HEAD {headRevision} · 1 commit behind origin/{branch}",
        _ => $"HEAD {headRevision} · {behind} commits behind origin/{branch}",
    };

    /// <summary>Incremental pass (git diff + proje başına imza) planlamanın EN UZUN adımıdır ve kendi
    /// sayısını üretmez — satır işin ÖNCESİNDE yazılır, yoksa akış tam da en uzun beklemede sessizleşirdi.
    /// Yalnız run yolunda: Sync kendi iki-pass'ini <c>changed/to build</c> özetiyle raporlar.</summary>
    public static string ComputingIncremental(int projects) => $"Computing incremental state ({projects} projects)";

    /// <summary>
    /// [D1/D3] İçerik özeti önbelleğinin doldurulması — koşu başına yalnız EKSİK dosyalar için. Satır işin
    /// ÖNCESİNDE yazılır: ilk indeksleme (ölçülen gerçek repoda 23 bin dosya) paralel okumayla bile onlarca
    /// saniye sürer ve kullanıcı beklemenin nedenini görmelidir. Küçük değişiklik kümeleri için hiç
    /// yazılmaz (bkz. <see cref="Incremental.SourceHashCache.NoisyPrefillThreshold"/>) — her koşuda
    /// "3 dosya okundu" demek gürültü olurdu.
    /// </summary>
    public static string IndexingSources(int files) => $"Indexing {files} source files — later runs reuse the index";

    // --- Ana repo: kullanıcının tetiklediği ff-only pull (v1.16.0) --------------------------------
    // Metinler İKİ şeyi birden söyler: ne YAPILDI ve ne YAPILMADI. Korkulan şey merge/rebase olduğu için
    // reddetme satırları da nedeni ve çözümü açıkça yazar — kullanıcı terminale gitmeden ne yapacağını bilir.

    /// <summary>Çalıştırılan git komutu (konsolda cmd tonu) — kullanıcı ne koştuğumuzu görür.</summary>
    public static string PullCommand(string branch) => $"git merge --ff-only origin/{branch}";

    /// <summary>Fast-forward başarılı: eski ve yeni HEAD kısa biçimde.</summary>
    public static string Pulled(string branch, string fromRevision, string toRevision)
        => $"Pulled origin/{branch} — fast-forward {fromRevision}..{toRevision}";

    /// <summary>Commit'lenmemiş değişiklik var — araç kullanıcının dosyalarının üstüne çalışmaz.</summary>
    public static string PullRefusedDirty()
        => "Pull refused — uncommitted changes in the working tree; commit or stash them first";

    /// <summary>Yerel branch ayrışmış: fast-forward mümkün değil, birleştirme kararı araca ait DEĞİLDİR.</summary>
    public static string PullRefusedDiverged(string branch)
        => $"Pull refused — local branch has diverged from origin/{branch}; reconcile it manually";

    /// <summary>HEAD bir branch'e bağlı değil — neyin ilerletileceği belirsiz.</summary>
    public static string PullRefusedDetached()
        => "Pull refused — HEAD is not on a branch; check out a branch first";

    /// <summary>Ağ/kimlik hatası ya da beklenmeyen git hatası; çalışma ağacına DOKUNULMADI.</summary>
    public static string PullFailed(string reason) => $"Pull failed — {reason}";

    /// <summary>Uzak uç zaten yakalanmıştı — ilerletilecek bir şey yok.</summary>
    public static string PullAlreadyCurrent(string branch) => $"Already up to date with origin/{branch}";

    // --- Harici projeler ------------------------------------------------------------------------
    // Aynı metinler iki yüzeyde görünür: Sync transkripti ve koşu planlaması. Bu yüzden onlar da burada, tek
    // kaynakta durur. Koşuyu İPTAL eden metinler buraya GİRMEZ — onlar progress satırı değil,
    // ExternalPreparationException'ın gövdesidir.

    /// <summary>Bir haricinin çalışma kopyası güncelleniyor — iş sürerken satır önce yazılır.</summary>
    public static string UpdatingExternal(string name) => $"Updating external '{name}'";

    /// <summary>
    /// Bir haricinin çalışma kopyası güncellendi ve HANGİ sürümde olduğu okundu. Satır güncellemenin
    /// ARDINDAN yazılır ve yalnız güncelleme gerçekten koştuğunda (kullanıcı bayrağı açık) görülür.
    ///
    /// <para>Revizyon kimliği kaynağına göre değişir: git'te kısa sha (<c>a1b2c3d</c>), TFVC'de changeset
    /// (<c>C48213</c>). Kullanıcının "hangi sürümü derliyorum" sorusunun cevabı budur; satırlarda revizyon
    /// GÖSTERİLMEZ (v1.16.0: satır kararı söyler, sürümü değil).</para>
    /// </summary>
    public static string UpdatedExternal(string name, string revision)
        => $"Updated external '{name}' → {revision}";

    /// <summary>Remote'a ulaşılamadı; yerel sürümle devam edilir (ana repo degraded fetch ile aynı felsefe).</summary>
    public static string ExternalUpdateDegraded(string name, string reason)
        => $"warning: external '{name}' could not be updated — building the local version ({reason})";

    /// <summary>Ayarlar'daki yol hiçbir projeye çözülemedi (yok, boş klasör, solution/proje olmayan dosya).
    /// <paramref name="problem"/> <see cref="Externals.ExternalWorkspaceResolver"/>'ın cümlesidir.
    /// <b>Sync bunu yazıp devam eder; Build durur</b> — yapılandırılmış bir haricinin sessizce düşmesi, bayat
    /// bir DLL'e link'lenmiş yeşil bir build demektir.</summary>
    public static string ExternalNotScanned(string name, string problem)
        => $"warning: external '{name}': {problem} — no projects from it will be built";

    /// <summary>[clean] Aynı çözümleme hatasının Clean'deki cümlesi. <see cref="ExternalNotScanned"/>'dan AYRI
    /// olmasının nedeni SONUCUN farklı olmasıdır: Clean hiçbir şey DERLEMEZ, temizler — ve o kökün çıktıları
    /// yerinde kalır. Clean bunu yazıp devam eder (ana kök yine temizlenir).</summary>
    public static string ExternalNotCleaned(string name, string problem)
        => $"warning: external '{name}': {problem} — nothing from it will be cleaned";

    /// <summary>[optimize] Aynı çözümleme hatasının Optimize'daki cümlesi. Sonuç yine farklıdır: o kökün
    /// projeleri onarılmaz — paketleri restore edilmez, kırık referansları raporlanmaz, artıkları kalır.
    /// Optimize bunu yazıp devam eder (ana kök yine onarılır).</summary>
    public static string ExternalNotOptimized(string name, string problem)
        => $"warning: external '{name}': {problem} — nothing from it will be repaired";

    /// <summary>Yolun üstünde SEÇİLEN türde bir çalışma kopyası işareti yok (git için <c>.git</c>, TFVC için
    /// <c>$tf</c>) — güncelleme ve kir kapısı çalışmaz, projeler olduğu gibi derlenir.</summary>
    public static string ExternalNoWorkingCopy(string name, VcsKind vcs)
        => $"warning: external '{name}': no {VcsKinds.Label(vcs)} working copy found above its path — building as-is";

    // Planner'dan SONRAKİ iki adım (MSBuild.exe çözümü, bayat-obj taraması) BİLEREK raporlanmaz: vswhere
    // sonucu Supervisor ömrü boyunca cache'lenir (ilk run dışında "resolving" demek yalan olurdu), bayat-obj
    // taraması ise proje başına küçük bir dosya okumasıdır (ölçülen üretim vakasında 6 satır 17ms). Uzun
    // bekleyişin tamamı ComputingIncremental satırının ALTINDA geçer — yani kullanıcının baktığı satır,
    // gerçekten koşan iştir.
}
