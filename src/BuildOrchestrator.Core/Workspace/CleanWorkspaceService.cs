using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.Scheduling;
using BuildOrchestrator.Core.State;

namespace BuildOrchestrator.Core.Workspace;

/// <summary>
/// [clean] Clean akışının TAMAMI, Core'da (D3 — iş mantığı App/Supervisor'a sızmaz): tarama → build-state
/// reset → her proje klasörünün <c>bin</c>/<c>obj</c>'ini silme → tek bitiş özeti.
/// <see cref="SyncWorkspaceService"/>'in kardeşidir; ondan farkı git'e HİÇ dokunmaması ve csproj
/// DEĞERLENDİRMEMESİDİR (bin/obj csproj'un yanındadır, evaluation-cache gerekmez).
///
/// <para><b>MSBuild <c>/t:Clean</c> ÇAĞRILMAZ</b> — yalnız dosya sistemi silme; gerekçe
/// <see cref="CleanWorkspaceCommand"/>'in doc'unda. Silinen küme YALNIZ keşfedilen her csproj klasörünün
/// <c>bin</c> ve <c>obj</c>'idir: <c>packages\</c>, ortak OutDir ve worktree havuzu (kök DIŞINDADIR ve kendi
/// LRU yaşam döngüsü vardır) DOKUNULMADAN kalır.</para>
///
/// <para><b>Sıra: ÖNCE state, SONRA klasörler.</b> Ters sıra bir güvenlik açığıdır — klasörler silinip state
/// kalsaydı, imzası hâlâ "güncel" görünen ama <c>bin</c>'i olmayan bir proje pre-skip edilir ve bayat çıktı
/// üretirdi. State önce giderse en kötü durum <c>NeverBuilt</c>, yani fazladan derleme: güvenli taraf.</para>
///
/// <para><b>Kilitli dosya HATA DEĞİLDİR:</b> dosya başına atlanır ve sayılır; akış durmaz, exception
/// üretilmez. IPC sınırına yalnız tanımlı bir <c>cleanFailed</c> hata event'i çıkabilir.</para>
///
/// <para><b>Proje listesinin kaynağı</b> taze bir <see cref="WorkspaceScanner.Scan"/>'dir, son Sync sonucu
/// DEĞİL: Supervisor Sync sonucunu saklamaz ve Clean, hiç Sync yapılmamış bir workspace'te de çalışmalıdır.
/// Tarama zaten <c>bin</c>/<c>obj</c>'i atlar ve sıralı döner (determinizm).</para>
/// </summary>
public sealed class CleanWorkspaceService(WorkspaceScanner scanner, BuildStateStore stateStore)
{
    /// <summary>Silinemeyen (kilitli) bir dosya için deneme bütçesi — kısa bir sharing-violation penceresini
    /// absorbe eder, çalışan bir uygulamanın tuttuğu dosyayı beklemez.</summary>
    private const int DeleteAttempts = 3;

    /// <summary>Üretim backoff'u — <see cref="DefaultDeleteRetryDelay"/>'in TEK kaynağı.</summary>
    private static readonly TimeSpan DeleteRetryBackoff = TimeSpan.FromMilliseconds(20);

    /// <summary>[D8] Başarısız bir silme denemesinden SONRAKİ gecikmenin TAMAMI — enjekte edilebilir dikiş
    /// (<c>BuildStateStore.RenameRetryDelay</c> deseni). Üretimde null → <see cref="DefaultDeleteRetryDelay"/>;
    /// testte anında dönen bir dikiş, yani gerçek bekleme yok.</summary>
    internal Action<int>? DeleteRetryDelay { get; set; }

    /// <summary>Gerçekten koşacak gecikme: dikiş kuruluysa o, değilse ÜRETİM varsayılanı. Ayrı üye olmasının
    /// sebebi testtir — varsayılanı no-op'a çeviren bir mutasyon aksi halde süiti yeşil bırakırdı.</summary>
    internal Action<int> EffectiveDeleteRetryDelay => DeleteRetryDelay ?? DefaultDeleteRetryDelay;

    internal static void DefaultDeleteRetryDelay(int attempt) => Thread.Sleep(DeleteRetryBackoff);

    /// <summary>Silme sırasında biriken sayaçlar — özet event'in ve konsol satırlarının TEK kaynağı.</summary>
    private sealed class Tally
    {
        public int FoldersRemoved;
        public long BytesRemoved;
        public int LockedFiles;
    }

    /// <summary>
    /// Clean'i uçtan uca koşar ve her adımı <paramref name="emit"/> ile yayınlar. SENKRONDUR: silme I/O-bound
    /// bir iştir ve Supervisor bu komut boyunca komut döngüsünü bilinçli olarak bloklar (Sync emsali) — proje
    /// başına düşen progress satırı App'in sessizlik watchdog'unu besler.
    /// </summary>
    public void Run(CleanWorkspaceCommand cmd, Action<IpcEvent> emit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ArgumentNullException.ThrowIfNull(emit);

        emit(new CleanStartedEvent(cmd.RootPath));

        // Girdi kapısı: bozuk kök IPC sınırında EXCEPTION'a değil TANIMLI bir hata event'ine dönüşür
        // (SyncWorkspaceService'in kapı deseni; kod ayrıktır çünkü Clean bir planlama pipeline'ı değildir).
        if (!Directory.Exists(cmd.RootPath))
        {
            emit(new ErrorEvent("cleanFailed", $"Workspace root not found: '{cmd.RootPath}'."));
            return;
        }

        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cmd.RootPath));

        // [harici projeler] Ana tarama + kayıtlı harici kökler, TEK çalışma alanı — Sync'in ve koşu
        // planlayıcısının kullandığı AYNI çözümleyici (kopya YASAK). Harici proje sıradan bir projedir:
        // aynı grafa girer, aynı kararı alır, aynı Clean'i görür.
        var workspace = ExternalWorkspaceResolver.Resolve(scanner.Scan(root), cmd.ExternalProjects, scanner);
        foreach (var problem in workspace.Problems)
            emit(Warn(PlanProgressLines.ExternalNotCleaned(problem.Name, problem.Problem)));

        // Kayıtlı kökler: ana kök + her harici kartın arama kökü. İki işleri var — defterin ÖNEK süpürmesi
        // (silinmiş/yeniden adlandırılmış projelerin artık kayıtları da bu sayede gider) ve uyarı satırındaki
        // göreli yol. Silme İZNİ bunlardan DEĞİL, çözülen projelerden gelir (bkz. IsSafeOutputFolder).
        var roots = new List<string> { root };
        foreach (var external in workspace.Roots)
        {
            string searchRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(external.SearchRoot));
            if (!roots.Contains(searchRoot, StringComparer.OrdinalIgnoreCase)) roots.Add(searchRoot);
        }

        var projectDirs = workspace.Scan.CsprojPaths
            .Select(p => Path.GetDirectoryName(Path.GetFullPath(p))!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList(); // birleşik tarama zaten sıralı → dedupe sonrası da deterministik

        // --- 1) ÖNCE state (bkz. sınıf doc'u): en kötü durum fazladan derleme olmalı, bayat çıktı DEĞİL.
        // Defter anahtarı TAM csproj yoludur, yani ana kökün öneki harici kökü KAPSAMAZ: her kök ayrı süpürülür.
        // Kayıtlı köklerin ALTINA düşmeyen projeler de kendi klasörleriyle süpürülür — bir harici kart <c>.sln</c>
        // ise solution kendi klasörünün DIŞINDAKİ bir projeyi gösterebilir; kaydı kalsaydı bir sonraki Build onu
        // "güncel" sayıp atlar ve silinmiş çıktıların üstüne yeşil bir koşu yazardı.
        int cleared = roots.Sum(stateStore.RemoveUnderRoot)
                      + projectDirs.Where(d => OwningRoot(roots, d) is null).Sum(stateStore.RemoveUnderRoot);
        emit(Info($"build state reset — {cleared} entries cleared, the next build compiles from scratch"));

        // --- 2) SONRA klasörler.
        var tally = new Tally();
        foreach (string dir in projectDirs)
        {
            ct.ThrowIfCancellationRequested();

            (int foldersBefore, long bytesBefore, int lockedBefore) = (tally.FoldersRemoved, tally.BytesRemoved, tally.LockedFiles);
            foreach (string folder in new[] { "bin", "obj" }) RemoveOutputFolder(Path.Combine(dir, folder), dir, tally);

            long bytes = tally.BytesRemoved - bytesBefore;
            int folders = tally.FoldersRemoved - foldersBefore;
            int locked = tally.LockedFiles - lockedBefore;
            string name = Path.GetFileName(dir);

            if (folders > 0) emit(Dim($"{name} — bin + obj removed ({FormatBytes(bytes)})"));
            if (locked > 0) emit(Warn($"warning: {locked} files in use under {Relative(roots, dir)} — skipped"));
        }

        // --- 3) Tek bitiş özeti.
        emit(Info($"Clean complete — {projectDirs.Count} projects · {tally.FoldersRemoved} folders · {FormatBytes(tally.BytesRemoved)} removed"));
        if (tally.LockedFiles > 0)
            emit(Warn($"warning: {tally.LockedFiles} files could not be removed (in use) — close the running application and run Clean again"));

        emit(new CleanCompletedEvent(projectDirs.Count, tally.FoldersRemoved, tally.BytesRemoved,
            tally.LockedFiles, cleared));
    }

    /// <summary>Tek bir <c>bin</c>/<c>obj</c> klasörünü siler. Klasör yoksa sessizce geçilir (hata değildir).
    /// Klasör silme sayacına yalnız GERÇEKTEN yok olan klasör yazılır — kilitli bir dosya kalmışsa klasör de
    /// kalır ve sayılmaz.</summary>
    private void RemoveOutputFolder(string folder, string projectDir, Tally tally)
    {
        if (!Directory.Exists(folder)) return;
        if (!IsSafeOutputFolder(folder, projectDir)) return; // defense in depth: başka hiçbir yol ASLA silinmez

        DeleteTree(folder, tally);
        if (!Directory.Exists(folder)) tally.FoldersRemoved++;
    }

    /// <summary>
    /// [güvenlik] Silinecek yol, ÇÖZÜLEN bir projenin klasörünün HEMEN altında olmalı ve adı <c>bin</c>/<c>obj</c>
    /// olmalı. İzin böylece tek bir yerden gelir: bu çalışma alanının derlediği proje kümesi. Harici projeler de
    /// o kümededir (sıradan projelerdir), kartı verilmemiş bir dizin ise hiç taranmadığı için kümeye giremez.
    ///
    /// <para><b>Neden "kayıtlı kökün altında" DEĞİL:</b> o kural iki yönde de yanlış cevap veriyordu — kökün
    /// altındaki ama hiçbir projeye ait OLMAYAN bir <c>bin</c>'i siliyor, bir harici <c>.sln</c>'in kendi
    /// klasörü dışında listelediği projenin <c>bin</c>'ini ise silmiyordu. Proje klasörüne bağlamak ikisini de
    /// çözer ve kapıyı ölçülebilir tutar.</para>
    /// </summary>
    private static bool IsSafeOutputFolder(string folder, string projectDir)
    {
        string full = Path.GetFullPath(folder);
        string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(full));
        string? parent = Path.GetDirectoryName(full);
        return parent is not null
               && string.Equals(Path.TrimEndingDirectorySeparator(parent), Path.TrimEndingDirectorySeparator(projectDir),
                                StringComparison.OrdinalIgnoreCase)
               && (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Verilen yolu İÇEREN izinli kök, yoksa <c>null</c>. Aynı soruyu güvenlik kapısı ve göreli yol
    /// biçimleyici sorar — cevap TEK yerde durur.</summary>
    private static string? OwningRoot(IReadOnlyList<string> roots, string fullPath)
    {
        foreach (string root in roots)
            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return root;
        return null;
    }

    /// <summary>Bir ağacı dosya-dosya siler. Reparse point (junction/symlink) İZLENMEZ: yalnız bağlantının
    /// kendisi kaldırılır, hedefin içeriğine DOKUNULMAZ — aksi halde bin'e konmuş bir bağlantı silmeyi
    /// workspace'in tamamen dışına taşırdı.</summary>
    private void DeleteTree(string dir, Tally tally)
    {
        var info = new DirectoryInfo(dir);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            TryDeleteDirectoryEntry(dir);
            return;
        }

        foreach (string file in SafeEnumerate(() => Directory.EnumerateFiles(dir))) DeleteFile(file, tally);
        foreach (string sub in SafeEnumerate(() => Directory.EnumerateDirectories(dir))) DeleteTree(sub, tally);

        TryDeleteDirectoryEntry(dir); // bottom-up, best-effort: kilitli dosya kalmışsa klasör de kalır
    }

    private void DeleteFile(string path, Tally tally)
    {
        long size;
        try { size = new FileInfo(path).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { size = 0; }

        bool deleted = SyncRetry.Run(
            () =>
            {
                // Salt-okur bayrağı (kaynak kontrolünden gelen çıktılar) File.Delete'i engeller; kaldır.
                var file = new FileInfo(path);
                if (file.IsReadOnly) file.IsReadOnly = false;
                File.Delete(path);
            },
            DeleteAttempts,
            ex => ex is IOException or UnauthorizedAccessException,
            EffectiveDeleteRetryDelay,
            rethrowWhenExhausted: false);

        if (deleted) tally.BytesRemoved += size;
        else tally.LockedFiles++; // kullanımda olan dosya: hata DEĞİL, sayılır ve atlanır
    }

    private static void TryDeleteDirectoryEntry(string dir)
    {
        try { Directory.Delete(dir, recursive: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* içinde kilitli dosya kaldı */ }
    }

    /// <summary>Numaralandırma sırasında klasör başkası tarafından kaldırılırsa akış durmaz.</summary>
    private static IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> enumerate)
    {
        try { return enumerate().ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Uyarı satırının yol metni: dizini İÇEREN köke göre görelidir (harici bir projeyi ana köke göre
    /// yazmak <c>..\..\</c> zinciri üretirdi).</summary>
    private static string Relative(IReadOnlyList<string> roots, string dir)
    {
        string owner = OwningRoot(roots, dir) ?? roots[0];
        string relative = Path.GetRelativePath(owner, dir);
        return relative == "." ? Path.GetFileName(owner) : relative;
    }

    /// <summary>Kullanıcıya gösterilecek boyut metni. Projede insan-okur bayt biçimleyicisi YOKTU; TEK
    /// tanımı burasıdır — konsol satırları da App'in stream özeti de (<c>StreamText.CleanCompleted</c>) BUNU
    /// çağırır (kopya YASAK). Clean dışından üçüncü bir tüketici doğarsa ortak bir yere taşınır.</summary>
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return unit == 0
            ? $"{bytes} {units[unit]}"
            : $"{value.ToString(value < 10 ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture)} {units[unit]}";
    }

    // Satır fabrikaları — SyncWorkspaceService'in deseni; Level metni App'te satır rengine dönüşür.
    private static CleanProgressEvent Info(string line) => new(line, "info");
    private static CleanProgressEvent Dim(string line) => new(line, "dim");
    private static CleanProgressEvent Warn(string line) => new(line, "warn");
}
