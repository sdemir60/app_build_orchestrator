namespace BuildOrchestrator.Core.Incremental;

using System.Text;
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
/// <b>Safe (varsayılan) — "dirty + transitive":</b> her düğümün imzası, DOĞRUDAN upstream'lerinin bu run
/// içindeki TAZE imzasını besler; o imza gerektiğinde ÖZYİNELEMELİ (DFS + memo, on-stack cycle guard) olarak
/// yerinde hesaplanır — ayrıca bir "reverse dependents" grafı kurulmaz. Bu, <c>plan</c>.Nodes'un topolojik
/// SIRALI olmasına BAĞLI DEĞİLDİR [A1]: <see cref="BuildOrchestrator.Core.Planning.LayerEngine"/>'ın sert faz
/// bariyeri bir projeyi kendi bağımlılığından ÖNCE koyabilir (warn-only, kasıtlı) — düz bir ileri geçişte o
/// düğümün upstream terimi "bilinmeyen"e düşer ve upstream'deki değişiklik downstream'e YANSIMAZDI (dependent
/// sessizce "up to date" sayılıp atlanırdı = under-build). Bir kök projenin imzası değişince bu, kendi imzasını
/// değiştirir; imzası değişen HER düğüm, kendisine bağımlı (downstream) düğümlerin upstream terimini de
/// değiştirir — GLOBAL propagation böyle DOĞAL olarak ortaya çıkar (bkz. <see cref="BuildSignature"/> tip özeti
/// "Transitive upstream propagation").
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
/// (<c>WillBuild=null</c>) olarak yorumlar; bu durumda <paramref name="committedFingerprintForNode"/> hiç
/// çağrılmaz (kısa devre). Bu, <see cref="BuildSignature.Compute"/>'ın null committedFingerprint'i TOLERE
/// ETMESİNDEN (deterministik NullMarker ile) farklı bir üst-seviye karardır — burada headCommit=null açıkça
/// "henüz anlamlı bir imza hesaplanamaz" sinyali olarak ele alınır. <paramref name="headCommit"/>, [A6
/// refinement — Task 7b] sonrası SADECE bu hollow kapısı için kullanılır; proje imzasının "committed" terimi
/// artık bu parametreden DEĞİL, <paramref name="committedFingerprintForNode"/>'dan (per-project) gelir — bkz.
/// <see cref="ComputeCommittedFingerprint"/> tip özeti.
/// </para>
/// </summary>
public static class IncrementalPlanner
{
    /// <param name="plan">Bir <see cref="BuildPlan"/>. Nodes'un topolojik sıralı olması GEREKMEZ (bkz. tip özeti "Safe").</param>
    /// <param name="headCommit">HEAD commit SHA'sı; <c>null</c> ise hollow (tüm plan için WillBuild=null). [A6 refinement] Proje imzasına DOĞRUDAN girmez — yalnız hollow kapısı içindir, bkz. <paramref name="committedFingerprintForNode"/>.</param>
    /// <param name="dirtyFilesForNode">Düğüm → bu projeye ait working-tree dirty dosya yollarının listesi
    /// (zaten bu projeye filtrelenmiş — <see cref="BuildSignature.Compute"/>'ın beklediği gibi). Dirty yoksa boş liste.</param>
    /// <param name="readFileContent">path → o dosyanın güncel içeriği (yalnız <paramref name="inPlace"/>=true iken, filtrelenmiş dirty dosyalar için çağrılır).</param>
    /// <param name="committedFingerprintForNode">[A6 refinement — Task 7b] Düğüm → bu projenin PER-PROJECT committed fingerprint'i (bkz. <see cref="ComputeCommittedFingerprint"/> — GitService.GetTrackedBlobHashesAsync haritası ∩ projenin build-etkileyen dosyaları üzerinden çağıran tarafından önceden hesaplanır). Repo-GLOBAL headCommit'in YERİNİ alır: bir commit, yalnız BU projenin committed dosyalarını gerçekten değiştirdiyse bu terim değişir. <c>null</c> tolere edilir (proje hiç commit'lenmemiş / no-commits repo).</param>
    /// <param name="state">projectId → <see cref="BuildState"/> (bkz. <see cref="BuildOrchestrator.Core.State.BuildStateStore.Load"/>). Kayıt yoksa never-built.</param>
    /// <param name="inPlace">true → in-place mod (local-diff dahil); false → worktree/committed (local-diff atlanır).</param>
    /// <param name="mode">Safe (varsayılan, dirty+transitive) veya Fast (yalnız dirty, cascade yok).</param>
    /// <returns><paramref name="plan"/> ile aynı düğümler, her birinin <see cref="ProjectNode.WillBuild"/> alanı doldurulmuş.</returns>
    public static BuildPlan ComputeWillBuild(
        BuildPlan plan,
        string? headCommit,
        Func<ProjectNode, IReadOnlyList<string>> dirtyFilesForNode,
        Func<string, string> readFileContent,
        Func<ProjectNode, string?> committedFingerprintForNode,
        IReadOnlyDictionary<string, BuildState> state,
        bool inPlace,
        DependentMode mode = DependentMode.Safe)
        => ComputeWillBuildWithSignatures(
            plan, headCommit, dirtyFilesForNode, readFileContent, committedFingerprintForNode, state, inPlace, mode).Plan;

    /// <summary>
    /// [Task 19 wiring] <see cref="ComputeWillBuild"/> ile AYNI hesap, ek olarak her düğüm için hesaplanan
    /// (topological memoize edilmiş) imzayı da döner. Supervisor'ın kompozisyon kökü, bir proje
    /// <c>projectSucceeded</c> olduğunda <see cref="BuildState.BuiltSignature"/>'ı bu haritadan persist eder —
    /// böylece BİR SONRAKİ <c>Build</c> koşusu incremental olur (temiz projeler skip). Hollow (headCommit=null)
    /// durumda TÜM imzalar <c>null</c>'dır (persist edilmez).
    /// </summary>
    public static (BuildPlan Plan, IReadOnlyDictionary<string, string?> SignatureById) ComputeWillBuildWithSignatures(
        BuildPlan plan,
        string? headCommit,
        Func<ProjectNode, IReadOnlyList<string>> dirtyFilesForNode,
        Func<string, string> readFileContent,
        Func<ProjectNode, string?> committedFingerprintForNode,
        IReadOnlyDictionary<string, BuildState> state,
        bool inPlace,
        DependentMode mode = DependentMode.Safe)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(dirtyFilesForNode);
        ArgumentNullException.ThrowIfNull(readFileContent);
        ArgumentNullException.ThrowIfNull(committedFingerprintForNode);
        ArgumentNullException.ThrowIfNull(state);

        BuildState? StateLookup(string id) => state.TryGetValue(id, out var st) ? st : null;

        if (headCommit is null)
        {
            // Hollow / pre-Sync: anlamlı bir imza yok — WillBuildEvaluator bunu null (hollow) olarak yorumlar.
            // committedFingerprintForNode BURADA hiç çağrılmaz (kısa devre) — bkz. tip özeti "Hollow" notu.
            var hollowSignatures = plan.Nodes.ToDictionary(
                n => n.Id, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
            return (BuildPreview.ComputeWillBuild(plan, _ => null, StateLookup), hollowSignatures);
        }

        var byId = plan.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        // Fast frozen-upstream imzalarını da barındırdığı için "freshMemo" değil "computedMemo" — ikisi için de
        // tek bir isim doğru. Değerler her zaman gerçek (non-null) imzadır; tip yalnız dönüş sözleşmesi
        // (SignatureById, hollow dalında null taşır) ile aynı kalsın diye string?'tir.
        var computedMemo = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Fast: upstream'in TAZE imzası yerine STORED/frozen imzasını besler — upstream'de bu run'da oluşan
        // bir değişiklik downstream'e YANSITILMAZ (suppressed/cascade yok). Bkz. tip özeti "Fast" bölümü.
        string? FrozenUpstream(string depId) => state.TryGetValue(depId, out var st) ? st.BuiltSignature : null;

        // Safe: upstream'in imzası ÖZYİNELEMELİ olarak (talep üzerine) hesaplanır — plan.Nodes'un topolojik
        // SIRALI olduğu varsayımı YOKTUR. Bu bilinçlidir: LayerEngine'ın sert faz bariyeri bir projeyi kendi
        // bağımlılığından ÖNCE koyabilir (warn-only tasarım, bkz. LayerEngine tip özeti) — düz ileri geçişte
        // o durumda upstream memo'da bulunamaz, imza "bilinmeyen upstream"e düşer ve upstream'deki DEĞİŞİKLİK
        // downstream'e YANSIMAZ: dependent sessizce "up to date" sayılıp ATLANIRDI (under-build).
        string Compute(ProjectNode node)
        {
            if (computedMemo.TryGetValue(node.Id, out var done) && done is not null) return done;
            // Cycle: üye (transitif olarak) kendi kendine bağımlı — upstream terimi "bilinmeyen" ile AYNI
            // deterministik işarete düşer. Cycle üyeleri zaten hiç derlenmez (WillBuildEvaluator, InCycle).
            if (!onStack.Add(node.Id)) return BuildSignature.NullMarker;

            // Plan DIŞI bir bağımlılık (byId'de yok) → null: "bilinmeyen upstream", Compute'un tolere ettiği hâl.
            string? Upstream(string depId) => byId.TryGetValue(depId, out var dep) ? Compute(dep) : null;

            var upstreamSignature = mode == DependentMode.Fast ? FrozenUpstream : (Func<string, string?>)Upstream;
            string signature = BuildSignature.Compute(
                node, plan.Configuration, committedFingerprintForNode(node), dirtyFilesForNode(node),
                readFileContent, upstreamSignature, inPlace);

            onStack.Remove(node.Id);
            computedMemo[node.Id] = signature;
            return signature;
        }

        foreach (var node in plan.Nodes) Compute(node);

        return (BuildPreview.ComputeWillBuild(plan, node => computedMemo[node.Id], StateLookup), computedMemo);
    }

    /// <summary>
    /// [A6 refinement — Task 7b] Bir projenin PER-PROJECT committed fingerprint'i: <see
    /// cref="BuildOrchestrator.Core.Git.GitService.GetTrackedBlobHashesAsync"/>'in döndürdüğü (repo-relative
    /// path → blob SHA, HEAD'de) harita ile <paramref name="projectRepoRelativeFiles"/>'ın KESİŞİMİ üzerinden
    /// deterministik (sıralı, case-insensitive) bir hash. Yalnız <see cref="BuildSignature.IsBuildAffecting"/>
    /// dosyalar ve yalnız haritada BULUNAN (yani commit'lenmiş) dosyalar sayılır — projenin henüz commit'lenmemiş
    /// YENİ bir dosyası (haritada yok) bu terimi ETKİLEMEZ; onun varlığı zaten working-tree dirty listesi
    /// (in-place modda local-diff terimi) üzerinden ayrıca yakalanır.
    /// <para>
    /// Kesişim BOŞSA (proje hiç commit'lenmemiş VEYA repo'da commit yok → <paramref name="trackedBlobHashes"/>
    /// boş) <c>null</c> döner — <see cref="BuildSignature.Compute"/> bunu sabit bir null-işaretiyle tolere eder.
    /// </para>
    /// <para>
    /// Bu, eskiden TÜM projelere GLOBAL olarak enjekte edilen repo-HEAD commit SHA'sının YERİNİ alır: repo'da
    /// ilişkisiz bir projeyi etkileyen bir commit artık bu projenin fingerprint'ini DEĞİŞTİRMEZ (bkz.
    /// <c>IncrementalPlannerTests</c>: commit-granularity testleri).
    /// </para>
    /// </summary>
    public static string? ComputeCommittedFingerprint(
        IReadOnlyDictionary<string, string> trackedBlobHashes,
        IReadOnlyList<string> projectRepoRelativeFiles)
    {
        ArgumentNullException.ThrowIfNull(trackedBlobHashes);
        ArgumentNullException.ThrowIfNull(projectRepoRelativeFiles);

        var matches = projectRepoRelativeFiles
            .Where(BuildSignature.IsBuildAffecting)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(trackedBlobHashes.ContainsKey)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0) return null; // proje hiç commit'lenmemiş / no-commits repo

        var sb = new StringBuilder();
        foreach (var path in matches)
        {
            // RAW path ASLA doğrudan ayraç yanına gömülmez — BuildSignature'daki boundary-shift korumasıyla
            // aynı kalıp (bkz. BuildSignatureTests: separator/`=` içeren id/yol testleri). HashText ve
            // ItemSeparator, BuildSignature'daki AYNI primitive'lerin (internal) reuse'u — review fix (Task 7b):
            // eskiden burada verbatim-kopya edilmişti, artık tek kaynak.
            sb.Append(BuildSignature.HashText(path)).Append('=').Append(trackedBlobHashes[path]).Append(BuildSignature.ItemSeparator);
        }

        return BuildSignature.HashText(sb.ToString());
    }
}
