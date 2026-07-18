namespace BuildOrchestrator.Core.Incremental;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;

/// <summary>
/// [T25][A6] GLOBAL graf propagation + skip-gate: bir <see cref="BuildPlan"/>'ın her düğümü için
/// <see cref="BuildSignature.Compute"/> (Task 6) ile <see cref="BuildPreview.ComputeWillBuild"/>/<see
/// cref="WillBuildEvaluator"/> (mevcut, değişmez) arasındaki seam'i doldurur: <c>currentSignatureFunc</c>'ı
/// (topological memoization) ve <c>stateLookupFunc</c>'ı (state dictionary) üretip <see
/// cref="BuildPreview.ComputeWillBuild"/>'e enjekte eder.
///
/// <para>
/// <b>Safe (varsayılan) — "dirty + transitive":</b> her düğümün imzası, DOĞRUDAN upstream'lerinin ZATEN
/// hesaplanmış (bu run içindeki, taze) imzasını besler — <see cref="plan"/>.Nodes build-order'da olduğu için
/// (bkz. <see cref="BuildPlanBuilder"/>) bu topological memoization'dır, ayrıca bir "reverse dependents" graf
/// kurulmaz. Bir kök projenin imzası değişince bu, kendi imzasını değiştirir; imzası değişen HER düğüm,
/// kendisine bağımlı (downstream) düğümlerin upstream teriminide değiştirir — GLOBAL propagation böyle DOĞAL
/// olarak ortaya çıkar (bkz. <see cref="BuildSignature"/> tip özeti "Transitive upstream propagation").
/// </para>
///
/// <para>
/// <b>Fast — "sadece dirty" (cascade yok):</b> her düğümün imzası, upstream'lerinin TAZE (bu run'da yeniden
/// hesaplanmış) imzası yerine STORED/frozen imzasını (<c>state[upstreamId].BuiltSignature</c>) besler — yani
/// upstream'in bu run'da DEĞİŞMİŞ olsa bile bu değişiklik downstream'e YANSITILMAZ (suppressed). Bu tasarım
/// YENİ bir <see cref="BuildState"/> alanı GEREKTİRMEZ: eğer bir proje X en son başarıyla derlendiğinde
/// upstream'i Y'nin STORED imzası neyse (tutarlı bir geçmiş varsayımıyla — Y, X'ten önce/tutarlı derlenmiş),
/// X'in o zamanki tam (Safe formülüyle hesaplanmış) imzası da AYNI stored-Y-değerini gömerek hesaplanmıştı.
/// Bu yüzden "frozen upstream" ile şimdi yeniden hesaplanan X'in imzası, upstream GERÇEKTEN değişmediği sürece
/// X'in STORED <see cref="BuildState.BuiltSignature"/>'ı ile BİREBİR eşleşir — ekstra bir "own-only baseline"
/// alanı saklamaya gerek kalmaz, karşılaştırma doğrudan mevcut <see cref="BuildState.BuiltSignature"/>'a karşı
/// yapılır (bkz. Task 7 report — bu tasarım kararının tam gerekçesi).
/// </para>
///
/// <para>
/// <b>Config-switch her iki modda da TÜM projeleri dirty yapar:</b> configuration, upstream'den DEĞİL doğrudan
/// düğümün KENDİ imza teriminden gelir (bkz. <see cref="BuildSignature.Compute"/> "cfg=" terimi) — bu yüzden
/// Fast'in upstream-suppression'ı config değişimini MASKELEMEZ; hem Safe hem Fast'te config değişince HER
/// düğümün (upstream'i değişmese dahi) kendi imza terimi farklılaşır.
/// </para>
///
/// <para>
/// <b>Hollow / pre-Sync:</b> <paramref name="headCommit"/> <c>null</c> ise (henüz Sync yapılmamış / anlamlı bir
/// HEAD yok) TÜM düğümler için imza <c>null</c> döner — <see cref="WillBuildEvaluator.Evaluate"/> bunu hollow
/// (<c>WillBuild=null</c>) olarak yorumlar. Bu, <see cref="BuildSignature.Compute"/>'ın null headCommit'i
/// TOLERE ETMESİNDEN (deterministik NullMarker ile) farklı bir üst-seviye karardır — burada headCommit=null
/// açıkça "henüz anlamlı bir imza hesaplanamaz" sinyali olarak ele alınır.
/// </para>
/// </summary>
public static class IncrementalPlanner
{
    /// <param name="plan">Build-order'da (topological) bir <see cref="BuildPlan"/>.</param>
    /// <param name="headCommit">HEAD commit SHA'sı; <c>null</c> ise hollow (tüm plan için WillBuild=null).</param>
    /// <param name="dirtyFilesForNode">Düğüm → bu projeye ait working-tree dirty dosya yollarının listesi
    /// (zaten bu projeye filtrelenmiş — <see cref="BuildSignature.Compute"/>'ın beklediği gibi). Dirty yoksa boş liste.</param>
    /// <param name="readFileContent">path → o dosyanın güncel içeriği (yalnız <paramref name="inPlace"/>=true iken, filtrelenmiş dirty dosyalar için çağrılır).</param>
    /// <param name="state">projectId → <see cref="BuildState"/> (bkz. <see cref="BuildOrchestrator.Core.State.BuildStateStore.Load"/>). Kayıt yoksa never-built.</param>
    /// <param name="inPlace">true → in-place mod (local-diff dahil); false → worktree/committed (local-diff atlanır).</param>
    /// <param name="mode">Safe (varsayılan, dirty+transitive) veya Fast (yalnız dirty, cascade yok).</param>
    /// <returns><paramref name="plan"/> ile aynı düğümler, her birinin <see cref="ProjectNode.WillBuild"/> alanı doldurulmuş.</returns>
    public static BuildPlan ComputeWillBuild(
        BuildPlan plan,
        string? headCommit,
        Func<ProjectNode, IReadOnlyList<string>> dirtyFilesForNode,
        Func<string, string> readFileContent,
        IReadOnlyDictionary<string, BuildState> state,
        bool inPlace,
        DependentMode mode = DependentMode.Safe)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(dirtyFilesForNode);
        ArgumentNullException.ThrowIfNull(readFileContent);
        ArgumentNullException.ThrowIfNull(state);

        BuildState? StateLookup(string id) => state.TryGetValue(id, out var st) ? st : null;

        if (headCommit is null)
        {
            // Hollow / pre-Sync: anlamlı bir imza yok — WillBuildEvaluator bunu null (hollow) olarak yorumlar.
            return BuildPreview.ComputeWillBuild(plan, _ => null, StateLookup);
        }

        // Safe: bu run içinde TAZE hesaplanmış upstream imzalarını besler (topological memoization) — bir
        // upstream'in DEĞİŞMİŞ imzası downstream'e böyle doğal biçimde yayılır (GLOBAL propagation).
        var freshMemo = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? FreshUpstream(string depId) => freshMemo.TryGetValue(depId, out var sig) ? sig : null;

        // Fast: upstream'in TAZE imzası yerine STORED/frozen imzasını besler — upstream'de bu run'da oluşan
        // bir değişiklik downstream'e YANSITILMAZ (suppressed/cascade yok). Bkz. tip özeti "Fast" bölümü.
        string? FrozenUpstream(string depId) => state.TryGetValue(depId, out var st) ? st.BuiltSignature : null;

        Func<string, string?> upstreamSignature = mode == DependentMode.Fast ? FrozenUpstream : FreshUpstream;

        foreach (var node in plan.Nodes)
        {
            var dirtyFiles = dirtyFilesForNode(node);
            string signature = BuildSignature.Compute(
                node, plan.Configuration, headCommit, dirtyFiles, readFileContent, upstreamSignature, inPlace);
            // Safe modda sonraki düğümlerin FreshUpstream'i bu değeri okuyabilsin diye memoize edilir; Fast
            // modda bu memo hiç okunmaz (FrozenUpstream state'ten okur) ama yine de dolduruluyor — zararsız.
            freshMemo[node.Id] = signature;
        }

        return BuildPreview.ComputeWillBuild(plan, node => freshMemo[node.Id], StateLookup);
    }
}
