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
/// <b>[A3] SCC (dependency cycle) = TEK kompozit imza:</b> bir cycle'ın üyeleri hiç derlenmez ama imzaları
/// SCC DIŞINDAKİ dependent'ların imzasına GİRER. Bu yüzden her SCC için, TÜM üyelerin kendi terimleri +
/// SCC-DIŞI upstream'lerinin imzaları üzerinden component başına TEK hash üretilir (SCC-içi kenarlar sabit
/// bir işarete düşürülerek döngü kırılır; üyeler sıralı ⇒ deterministik) ve hem üyeler hem downstream'ler
/// AYNI bu değeri okur. Aksi hâlde SCC bir "imza kara deliği" olurdu: cycle İÇİNDEKİ gerçek bir kaynak
/// değişimi, ziyaret sırasına bağlı olarak dışarıdaki bir dependent'a HİÇ yansımayabilir ve o dependent bir
/// sonraki Build'de sessizce "up to date" sayılıp atlanırdı (cycle-tangled transitive under-build).
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
    /// <param name="buildCycles">[Task 11] Kill switch (bkz. <c>StartRunCommand.BuildDependencyCycles</c>):
    /// SCC üyeleri motor tarafından turlarla DERLENİYOR mu. <c>false</c> ⇒ üyeler <c>WillBuild=false</c>'a kısa
    /// devre yapar (<see cref="WillBuildEvaluator"/>), <c>true</c> ⇒ sıradan imza/state mantığına tabidirler —
    /// SCC'nin bileşik imzası (bkz. <c>ComputeComponent</c>) tüm üyeler için ORTAK olduğundan grup ya bütün
    /// olarak "derlenecek" ya bütün olarak "güncel" görünür. <b>Varsayılanı YOKTUR:</b> bu değerin sessizce
    /// sabitlenmesi tam da bu görevin düzelttiği kusurdu (iki çağrı yeri <c>false</c>'ı gömüyordu ve önizleme
    /// motorla ayrışıyordu) — her çağıran kararı AÇIKÇA yazmak zorundadır.</param>
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
        bool buildCycles,
        DependentMode mode = DependentMode.Safe)
        => ComputeWillBuildWithSignatures(
            plan, headCommit, dirtyFilesForNode, readFileContent, committedFingerprintForNode, state, inPlace,
            buildCycles, mode).Plan;

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
        bool buildCycles,
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
            return (BuildPreview.ComputeWillBuild(plan, _ => null, StateLookup, buildCycles), hollowSignatures);
        }

        var byId = plan.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        // Fast frozen-upstream imzalarını da barındırdığı için "freshMemo" değil "computedMemo" — ikisi için de
        // tek bir isim doğru. Değerler her zaman gerçek (non-null) imzadır; tip yalnız dönüş sözleşmesi
        // (SignatureById, hollow dalında null taşır) ile aynı kalsın diye string?'tir.
        var computedMemo = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // [A3] üye id → o üyenin SCC'sinin (sıralı) üye listesi. plan.Cycles, TopoSort/Tarjan'ın ürettiği
        // MAKSİMAL ve AYRIK SCC'lerdir (>1 üye) — bu yüzden component grafı (condensation) bir DAG'dır ve
        // ComputeComponent'in özyinelemesi sonlanır. Plan DIŞI id'ler elenir, sıra burada sabitlenir
        // (determinizm: kompozit, Cycles'ın hangi sırada geldiğinden bağımsız olmalı). Fast'te bu harita
        // KURULMAZ — bkz. ComputeComponent'in gerekçesi.
        var componentOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (mode != DependentMode.Fast)
        {
            foreach (var cycle in plan.Cycles)
            {
                var members = cycle
                    .Where(byId.ContainsKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                foreach (string id in members) componentOf[id] = members;
            }
        }
        var componentOnStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Fast: upstream'in TAZE imzası yerine STORED/frozen imzasını besler — upstream'de bu run'da oluşan
        // bir değişiklik downstream'e YANSITILMAZ (suppressed/cascade yok). Bkz. tip özeti "Fast" bölümü.
        string? FrozenUpstream(string depId) => state.TryGetValue(depId, out var st) ? st.BuiltSignature : null;

        // Safe: upstream'in imzası ÖZYİNELEMELİ olarak (talep üzerine) hesaplanır — plan.Nodes'un topolojik
        // SIRALI olduğu varsayımı YOKTUR. Bu bilinçlidir: LayerEngine'ın sert faz bariyeri bir projeyi kendi
        // bağımlılığından ÖNCE koyabilir (warn-only tasarım, bkz. LayerEngine tip özeti) — düz ileri geçişte
        // o durumda upstream memo'da bulunamaz, imza "bilinmeyen upstream"e düşer ve upstream'deki DEĞİŞİKLİK
        // downstream'e YANSIMAZ: dependent sessizce "up to date" sayılıp ATLANIRDI (under-build).
        // Plan DIŞI bir bağımlılık (byId'de yok) → null: "bilinmeyen upstream", Compute'un tolere ettiği hâl.
        string? Upstream(string depId) => byId.TryGetValue(depId, out var dep) ? Compute(dep) : null;

        string Compute(ProjectNode node)
        {
            if (computedMemo.TryGetValue(node.Id, out var done) && done is not null) return done;
            // [A3] SCC üyesi: imza tek tek DEĞİL, component başına TEK kompozit olarak hesaplanır.
            if (componentOf.TryGetValue(node.Id, out var members)) return ComputeComponent(members);
            // Bir SCC'ye ait OLMAYAN kendine-bağımlılık (self-loop: TopoSort tek üyeli SCC'yi Cycles'a KOYMAZ)
            // → upstream terimi "bilinmeyen" ile AYNI deterministik işarete düşer; sonsuz özyinelemeyi
            // engelleyen tek şey budur. Kısmi değer sızmasın diye bu dönüş MEMOİZE EDİLMEZ.
            if (!onStack.Add(node.Id)) return BuildSignature.NullMarker;

            var upstreamSignature = mode == DependentMode.Fast ? FrozenUpstream : (Func<string, string?>)Upstream;
            string signature = BuildSignature.Compute(
                node, plan.Configuration, committedFingerprintForNode(node), dirtyFilesForNode(node),
                readFileContent, upstreamSignature, inPlace);

            onStack.Remove(node.Id);
            computedMemo[node.Id] = signature;
            return signature;
        }

        // [A3] Bir SCC'nin (dependency cycle) TEK kompozit imzası: TÜM üyelerin KENDİ terimleri + SCC-DIŞI
        // upstream'lerinin imzaları üzerinden tek hash; üyeler de downstream'ler de AYNI bu değeri okur.
        // SCC-İÇİ kenarlar sabit NullMarker'a düşürülerek döngü kırılır — üye sırasından bağımsız, deterministik.
        // ÖNCESİ: SCC bir "imza kara deliği"ydi — on-stack guard'a çarpan üyenin upstream terimi sabit
        // NullMarker'a düşüyordu, dolayısıyla SCC İÇİNDEKİ gerçek bir kaynak değişimi, SCC DIŞINDAKİ bir
        // downstream'e (o üyenin imzasını okuyor olmasına rağmen) ZİYARET SIRASINA bağlı olarak hiç
        // yansımayabiliyordu: dependent bir sonraki Build'de sessizce "up to date" sayılıp atlanırdı
        // (cycle-tangled transitive under-build). Bu düzeltmenin KENDİSİ yalnız downstream'in GÖRDÜĞÜ değeri
        // onarır; üyelerin derlenip derlenmediği [Task 11] kill switch'inin (buildCycles) işidir — kapalıyken
        // hiç derlenmezler, açıkken kompozit onların KENDİ WillBuild'ini de belirler (grup bütün olarak ya
        // "derlenecek" ya "güncel" görünür, çünkü değer üyeler arasında ORTAKTIR).
        // Fast'te kompozit KULLANILMAZ: Fast zaten hiçbir upstream'i takip etmez (frozen/stored imza okur),
        // yani kompozitin çözdüğü cascade sorunu orada tanım gereği yoktur — semantiği değiştirmemek için
        // Fast'in yolu A1'deki gibi bırakılır.
        string ComputeComponent(IReadOnlyList<string> members)
        {
            // Memo kontrolü BURADA TEKRARLANMAZ: kompozit, TÜM üyeler için aynı anda yazılır (aşağıda), bu
            // yüzden Compute'un başındaki computedMemo kontrolü hangi üyeden girilirse girilsin yakalar.
            string representative = members[0]; // üyeler sıralı → temsilci deterministik
            // Savunmacı guard: MAKSİMAL bir SCC'de, SCC-DIŞI bir upstream aynı component'e GERİ dönemez
            // (dönseydi o düğüm de SCC'nin üyesi olurdu) — yani sağlıklı bir plan'da buraya girilmez. Elle
            // kurulmuş/bozuk bir Cycles listesinde bu garanti yoktur; node seviyesindeki on-stack guard ile
            // AYNI gerekçe: sonsuz özyineleme (StackOverflow) yerine deterministik işaret.
            if (!componentOnStack.Add(representative)) return BuildSignature.NullMarker;

            var membersSet = new HashSet<string>(members, StringComparer.OrdinalIgnoreCase);
            var sb = new StringBuilder();
            foreach (string id in members)
            {
                var member = byId[id]; // members yalnız byId'de BULUNAN id'lerle kuruldu
                sb.Append(BuildSignature.Compute(
                    member, plan.Configuration, committedFingerprintForNode(member), dirtyFilesForNode(member),
                    readFileContent,
                    depId => membersSet.Contains(depId) ? BuildSignature.NullMarker : Upstream(depId),
                    inPlace));
                sb.Append(BuildSignature.ItemSeparator);
            }
            string composite = BuildSignature.HashText(sb.ToString());

            componentOnStack.Remove(representative);
            foreach (string id in members) computedMemo[id] = composite;
            return composite;
        }

        foreach (var node in plan.Nodes) Compute(node);

        return (BuildPreview.ComputeWillBuild(plan, node => computedMemo[node.Id], StateLookup, buildCycles), computedMemo);
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
