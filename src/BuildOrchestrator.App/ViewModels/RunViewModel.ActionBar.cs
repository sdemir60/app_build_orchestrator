using System.Globalization;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

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

    /// <summary>[T40] Sayaç chip'i toggle'ı: aynı filtreye ikinci tık temizler; <c>null</c> (Σ) HER ZAMAN temizler
    /// (BuildApp.jsx:1550 <c>onClick={() =&gt; setFilter(null)}</c> vs :1554 <c>toggleFilter('building')</c>).</summary>
    public void ToggleFilter(string? filter)
    {
        if (filter is null) { ActiveFilter = null; return; }
        ActiveFilter = string.Equals(ActiveFilter, filter, StringComparison.Ordinal) ? null : filter;
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

    /// <summary>
    /// [T40 · K3 — prototipten SAPMA, plan kazanır (BuildApp.jsx:1336-1353)] Branch seçimi. Aktif-OLMAYAN bir branch
    /// seçilince: worktree ZORUNLU ON, proje durumları Pending'e sıfırlanır, faz <see cref="AppPhase.Boot"/>'a düşer
    /// ve konsola İKİ niyet satırı yazılır — <c>git switch --detach …</c> satırı <b>YAZILMAZ</b> (prototipteki o
    /// satır App'te yanıltıcı olurdu: gerçek switch Build anında worktree kurulumunda olur). Aktif branch seçilince:
    /// yalnız <see cref="Branch"/> set edilir (worktree zorlaması/reset YOK).
    /// </summary>
    public void SelectBranch(BranchRef branch)
    {
        Branch = branch.Name;
        if (branch.IsActive) return; // aktif branch: worktree zorlaması/reset/niyet-satırı YOK

        WorktreeName = null;  // seçili hedef worktree'yi auto'ya döndür (BuildApp.jsx:1340)
        UseWorktree = true;   // aktif-olmayan branch → worktree zorunlu (BuildApp.jsx:1342)
        ResetRowsToHollow();  // BuildApp.jsx:1345 status='discovered' → Pending, will='unknown' → hollow
        _willBuildIds.Clear();       // BuildApp.jsx:1346 eng.willBuild = new Set()
        Phase = AppPhase.Boot;       // BuildApp.jsx:1347
        string sha7 = Short7(branch.Sha);
        AppendRunLine($"branch target: {branch.Name} ({sha7}) — worktree will be used at Build");
        AppendRunLine($"Branch changed: {branch.Name} — Sync required"); // BuildApp.jsx:1350
        RefreshRunSurface();         // sayaç/görünür-liste + willBuild yüzeyi tazelensin
    }

    /// <summary>Branch popover'daki mono SHA + niyet satırındaki <c>{sha7}</c> için 7-haneli kısaltma (uzunsa kırp,
    /// zaten kısaysa olduğu gibi) — brief 7-hane pinler.</summary>
    internal static string Short7(string sha) => sha.Length > 7 ? sha[..7] : sha;

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
    /// BİRLİKTE (tek sabit eşleme, <see cref="CyclePerf"/> ile aynı otorite). <see cref="CyclePerf"/>'ten farkı: döngü
    /// YOK ve konsol notu YOK (bu bir seed'dir, kullanıcı aksiyonu değil). Geçersiz değer no-op (varsayılan korunur).</summary>
    public void SetPerfMode(string mode)
    {
        if (mode is not ("Full" or "Balanced" or "Light")) return;
        PerfMode = mode;
        Parallelism = ParallelismFor(mode);
    }

    // ---------------------------------------------------------------- [D7/T66] Settings — layers + repository

    /// <summary>[D7] Settings Save: yeni katman pattern'lerini uygular. <see cref="LayerPatterns"/> set edilir
    /// (sonraki Sync/Build komutlarıyla motora gider — A1/A5) ve konsola BİREBİR dim not yazılır
    /// (BuildApp.jsx:1423): katman kaldıysa <c>Layer definitions updated — {n} layers</c>, liste boşaltıldıysa
    /// <c>Layers removed — single project list</c>. Yeniden gruplama Core'dan <c>LayerName</c> olarak geri döner
    /// (App'te regex YOK — mimari kural).</summary>
    public void ApplyLayerPatterns(IReadOnlyList<LayerPattern> patterns)
    {
        LayerPatterns = patterns;
        AppendRunLine(patterns.Count > 0
            ? $"Layer definitions updated — {patterns.Count} layers"
            : "Layers removed — single project list");
    }

    /// <summary>[D7 · K10] Settings "Change…": yeni bir repo kökü seçilince kökü değiştirir, proje durumlarını
    /// sıfırlar (yeni repo = yeni taban) ve OTOMATİK Sync başlatır. Klasör seçici çağıranın (dialog) enjekte
    /// ettiği bir seam'dir — bu metot yalnız sonucu (yol) alır, böylece testler gerçek diyalog açmaz.
    /// <see cref="OnRootPathChanged"/> Empty→Boot geçişini zaten sürer.</summary>
    public async Task ChangeRepositoryAsync(string path)
    {
        if (IsMidRunLocked || string.IsNullOrEmpty(path)) return;
        RootPath = path;         // OnRootPathChanged Empty→Boot geçişini sürer
        ResetRowsToHollow();     // yeni repo: önceki repo'nun sonuçları artık geçersiz (SelectBranch ile aynı reset)
        _willBuildIds.Clear();
        RefreshRunSurface();
        await SyncAsync();       // otomatik Sync (aynı gönderim yolu; SelectedProjectId'yi temizler)
    }

    /// <summary>[D7] Satırları yeni bir taban için "hollow"a sıfırlar (durum Pending, will bilinmiyor, süre/dep
    /// temizli). Branch değişimi (<see cref="SelectBranch"/>) ve repo değişimi (<see cref="ChangeRepositoryAsync"/>)
    /// AYNI reset'i paylaşır — tek yer (kopya YASAK).</summary>
    private void ResetRowsToHollow()
    {
        foreach (var row in Projects)
        {
            row.State = ProjectRowState.Pending;
            row.WillBuild = null;
            row.DepIssues = null;
            row.DurationMs = 0;
        }
    }
}
