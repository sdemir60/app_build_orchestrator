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

    /// <summary>Incremental pass (git diff + proje başına imza) planlamanın EN UZUN adımıdır ve kendi
    /// sayısını üretmez — satır işin ÖNCESİNDE yazılır, yoksa akış tam da en uzun beklemede sessizleşirdi.
    /// Yalnız run yolunda: Sync kendi iki-pass'ini <c>changed/to build</c> özetiyle raporlar.</summary>
    public static string ComputingIncremental(int projects) => $"Computing incremental state ({projects} projects)";

    // --- Harici projeler ------------------------------------------------------------------------
    // Aynı metinler iki yüzeyde görünür: Sync transkripti ve koşu planlaması. Bu yüzden onlar da burada, tek
    // kaynakta durur. Koşuyu İPTAL eden metinler buraya GİRMEZ — onlar progress satırı değil,
    // ExternalPreparationException'ın gövdesidir.

    /// <summary>Bir haricinin çalışma kopyası güncelleniyor — iş sürerken satır önce yazılır.</summary>
    public static string UpdatingExternal(string name) => $"Updating external '{name}'";

    /// <summary>Remote'a ulaşılamadı; yerel sürümle devam edilir (ana repo degraded fetch ile aynı felsefe).</summary>
    public static string ExternalUpdateDegraded(string name, string reason)
        => $"warning: external '{name}' could not be updated — building the local version ({reason})";

    /// <summary>Ayarlar'daki yol hiçbir projeye çözülemedi (yok, boş klasör, solution/proje olmayan dosya).
    /// <paramref name="problem"/> <see cref="Externals.ExternalWorkspaceResolver"/>'ın cümlesidir.
    /// <b>Sync bunu yazıp devam eder; Build durur</b> — yapılandırılmış bir haricinin sessizce düşmesi, bayat
    /// bir DLL'e link'lenmiş yeşil bir build demektir.</summary>
    public static string ExternalNotScanned(string name, string problem)
        => $"warning: external '{name}': {problem} — no projects from it will be built";

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
