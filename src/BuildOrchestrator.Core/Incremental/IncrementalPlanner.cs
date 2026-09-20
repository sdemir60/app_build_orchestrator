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
/// <b>[D1] İmza her zaman hesaplanabilir.</b> Karar diskteki içerikten geldiği için "anlamlı bir taban yok"
/// diye bir hâl KALMADI: commit'i olmayan bir repo, git'i bozuk bir makine ya da sürüm kontrolü hiç olmayan
/// bir klasör de tam bir karar üretir (hiç derlenmemiş projeler "derlenecek", diğerleri imzalarıyla
/// karşılaştırılır). Eskiden buraya <c>headCommit</c> verilirdi ve <c>null</c> ise TÜM düğümler hollow
/// (<c>WillBuild=null</c>) dönerdi — o kapı, imzanın git'ten beslendiği dönemin artığıydı. Hollow durum artık
/// yalnız App tarafında ve yalnız "henüz hiç önizleme gelmedi" anlamında vardır.
/// </para>
/// </summary>
public static class IncrementalPlanner
{
    /// <param name="plan">Bir <see cref="BuildPlan"/>. Nodes'un topolojik sıralı olması GEREKMEZ (bkz. tip özeti "Safe").</param>
    /// <param name="contentFingerprintForNode">[D1] Düğüm → bu projenin girdi dosyalarının DİSKTEKİ içeriğini
    /// temsil eden hash (bkz. <see cref="ComputeContentFingerprint"/>). <c>null</c> tolere edilir (hiçbir girdi
    /// okunamadı) — <see cref="BuildSignature.Compute"/> onu sabit bir null-işaretiyle imzaya katar.</param>
    /// <param name="state">projectId → <see cref="BuildState"/> (bkz. <see cref="BuildOrchestrator.Core.State.BuildStateStore.Load"/>). Kayıt yoksa never-built.</param>
    /// <param name="buildCycles">Bu koşu SCC üyelerini derliyor mu — yalnız <c>RunMode.Cycles</c>'ta <c>true</c>.
    /// <c>false</c> ⇒ üyeler <c>WillBuild=false</c>'a kısa devre yapar (<see cref="WillBuildEvaluator"/>),
    /// <c>true</c> ⇒ sıradan imza/state mantığına tabidirler — SCC'nin bileşik imzası (bkz.
    /// <c>ComputeComponent</c>) tüm üyeler için ORTAK olduğundan grup ya bütün olarak "derlenecek" ya bütün
    /// olarak "güncel" görünür. <b>Varsayılanı YOKTUR:</b> her çağıran koşunun kapsamını AÇIKÇA yazar.</param>
    /// <param name="mode">Safe (varsayılan, dirty+transitive) veya Fast (yalnız dirty, cascade yok).</param>
    /// <param name="outputs">[Faz 3 — spec 2026-09-18 §5] projectId → çıktı kanıtı kontrolü (<see
    /// cref="IncrementalRunBinder.ChecksFor"/>). Yalnız karara girer — <see cref="WillBuildEvaluator"/>'a aktarılır,
    /// Safe'te kirli upstream'in arkasındaki zaman kipi düğümünü de derletir (<c>BehindDirtyUpstream</c>); imza
    /// hesabı onu OKUMAZ. <c>null</c> ya da eksik proje ⇒ bugünkü karar.</param>
    /// <returns><paramref name="plan"/> ile aynı düğümler, her birinin <see cref="ProjectNode.WillBuild"/> alanı doldurulmuş.</returns>
    public static BuildPlan ComputeWillBuild(
        BuildPlan plan,
        Func<ProjectNode, string?> contentFingerprintForNode,
        IReadOnlyDictionary<string, BuildState> state,
        bool buildCycles,
        DependentMode mode = DependentMode.Safe,
        IReadOnlyDictionary<string, OutputCheck>? outputs = null)
        => ComputeWillBuildWithSignatures(plan, contentFingerprintForNode, state, buildCycles, mode, outputs).Plan;

    /// <summary>
    /// [Task 19 wiring] <see cref="ComputeWillBuild"/> ile AYNI hesap, ek olarak her düğüm için hesaplanan
    /// (topological memoize edilmiş) imzayı da döner. Supervisor'ın kompozisyon kökü, bir proje
    /// <c>projectSucceeded</c> olduğunda <see cref="BuildState.BuiltSignature"/>'ı bu haritadan persist eder —
    /// böylece BİR SONRAKİ <c>Build</c> koşusu incremental olur (temiz projeler skip).
    /// </summary>
    public static (BuildPlan Plan, IReadOnlyDictionary<string, string> SignatureById) ComputeWillBuildWithSignatures(
        BuildPlan plan,
        Func<ProjectNode, string?> contentFingerprintForNode,
        IReadOnlyDictionary<string, BuildState> state,
        bool buildCycles,
        DependentMode mode = DependentMode.Safe,
        IReadOnlyDictionary<string, OutputCheck>? outputs = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(contentFingerprintForNode);
        ArgumentNullException.ThrowIfNull(state);

        BuildState? StateLookup(string id) => state.TryGetValue(id, out var st) ? st : null;

        var byId = plan.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        // Fast frozen-upstream imzalarını da barındırdığı için "freshMemo" değil "computedMemo" — ikisi için de
        // tek bir isim doğru.
        var computedMemo = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
            if (computedMemo.TryGetValue(node.Id, out var done)) return done;
            // [A3] SCC üyesi: imza tek tek DEĞİL, component başına TEK kompozit olarak hesaplanır.
            if (componentOf.TryGetValue(node.Id, out var members)) return ComputeComponent(members);
            // Bir SCC'ye ait OLMAYAN kendine-bağımlılık (self-loop: TopoSort tek üyeli SCC'yi Cycles'a KOYMAZ)
            // → upstream terimi "bilinmeyen" ile AYNI deterministik işarete düşer; sonsuz özyinelemeyi
            // engelleyen tek şey budur. Kısmi değer sızmasın diye bu dönüş MEMOİZE EDİLMEZ.
            if (!onStack.Add(node.Id)) return BuildSignature.NullMarker;

            var upstreamSignature = mode == DependentMode.Fast ? FrozenUpstream : (Func<string, string?>)Upstream;
            string signature = BuildSignature.Compute(
                node, plan.Configuration, contentFingerprintForNode(node), upstreamSignature);

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
                    member, plan.Configuration, contentFingerprintForNode(member),
                    depId => membersSet.Contains(depId) ? BuildSignature.NullMarker : Upstream(depId)));
                sb.Append(BuildSignature.ItemSeparator);
            }
            string composite = BuildSignature.HashText(sb.ToString());

            componentOnStack.Remove(representative);
            foreach (string id in members) computedMemo[id] = composite;
            return composite;
        }

        foreach (var node in plan.Nodes) Compute(node);

        // [Faz 3/Task 5] Kanıt YALNIZ karara aktarılır — yukarıdaki imza hesabı onu hiç görmez (§5).
        BuildPlan Decide(IReadOnlyDictionary<string, OutputCheck>? checks) => BuildPreview.ComputeWillBuild(
            plan, node => computedMemo[node.Id], StateLookup, buildCycles,
            checks is null ? null : id => checks.GetValueOrDefault(id));

        var decided = Decide(outputs);
        // [Faz 3 final review — ruling R10] Safe'te bağımlılar imzayla değerlendirilir (§5.4): kirli upstream'in
        // arkasındaki zaman kipi düğümü de derlenir. Fast "yalnız dirty"dir, cascade yapmaz.
        if (mode == DependentMode.Safe && outputs is not null
            && BehindDirtyUpstream(decided, outputs) is { Count: > 0 } cascaded)
            decided = Decide(cascaded);
        return (decided, computedMemo);
    }

    /// <summary>
    /// [Faz 3 final review — ruling R10, spec 2026-09-18 §5.4 "Bağımlı projeler her zaman imzayla değerlendirilir"]
    /// Zaman kontrolü yalnız dosya zamanlarını okur: bağımlılığı <c>D</c> bu Build'de yeniden derlenecekken
    /// <c>D</c>'nin ortak kopyası henüz eskidir ve zaman kipindeki bağımlısı taze (<c>BuiltOutside</c>) okunup
    /// pre-skip edilirdi — ağaç ancak N Sync+Build turunda tutarlı olurdu. Defter kipindeki bağımlıda bu sorun
    /// yoktur: upstream'in imzası onun imzasına girer.
    ///
    /// <para>Kural: <see cref="ProducesNewOutput"/> olan bir upstream'in (doğrudan ya da transitive) arkasındaki
    /// zaman kipi düğümünün kontrolü <see cref="TimeVerdict.DependencyNewer"/>'a çekilir — değerlendirici onu
    /// <c>OutputStale</c> ile derlenecek okur, kendi dosyası değişmediği için etiket <c>affected</c>'tır
    /// (<see cref="OutputEvidence.OwnFilesChanged(OutputCheck?, bool?)"/>). Yalnız <see cref="TimeVerdict.Fresh"/>
    /// ve <see cref="TimeVerdict.FedBroken"/> çekilir: zaman kipinin sırasında "bağımlılık yeni" beslenen kopyanın
    /// önündedir; kanıtı olmayan (<see cref="TimeVerdict.Missing"/>) ya da kendi girdisi yeni
    /// (<see cref="TimeVerdict.OwnNewer"/>) düğüm kendi hükmünü korur. Kapsam dışı döngü üyesi yine
    /// derlenmez — onu değerlendirici söyler, burada tekrarlanmaz.</para>
    ///
    /// <para><b>Gezinti tohumdan SONRA süzülmez.</b> Cascade yalnız <see cref="ProducesNewOutput"/> düğümlerden
    /// BAŞLAR; oraya varan gezinti ters kenarları ayrım yapmadan izler, çünkü çekilen her düğüm de derlenecektir
    /// ve kendi aşağı akışını kirletir.</para>
    ///
    /// <para>Sıradan bağımsızdır (plan topolojik sıralı olmak zorunda değil [A1]) ve döngüye dayanıklıdır: kirli
    /// düğümlerden ters kenarlar boyunca TEK bir genişlik-öncelikli gezinti (ziyaret kümesiyle). Bu geçişte kirli
    /// olan düğümün bütün aşağı akışı zaten o gezintidedir, bu yüzden ikinci tur gerekmez.</para>
    /// </summary>
    /// <returns>Kontrolü çekilen düğüm varsa güncellenmiş kontrol haritası, yoksa boş.</returns>
    private static IReadOnlyDictionary<string, OutputCheck> BehindDirtyUpstream(
        BuildPlan decided, IReadOnlyDictionary<string, OutputCheck> outputs)
    {
        var dependents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in decided.Nodes)
            foreach (string dep in node.Dependencies)
            {
                if (!dependents.TryGetValue(dep, out var list)) dependents[dep] = list = [];
                list.Add(node.Id);
            }

        var behind = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(decided.Nodes.Where(ProducesNewOutput).Select(n => n.Id));
        while (queue.TryDequeue(out string? id))
            foreach (string dependent in dependents.GetValueOrDefault(id) ?? [])
                if (behind.Add(dependent)) queue.Enqueue(dependent);

        var pulled = behind
            .Where(id => outputs.GetValueOrDefault(id) is
                { Mode: EvidenceMode.Time, Time: TimeVerdict.Fresh or TimeVerdict.FedBroken })
            .ToList();
        if (pulled.Count == 0) return new Dictionary<string, OutputCheck>();

        var result = new Dictionary<string, OutputCheck>(outputs, StringComparer.OrdinalIgnoreCase);
        foreach (string id in pulled) result[id] = outputs[id] with { Time = TimeVerdict.DependencyNewer };
        return result;
    }

    /// <summary>
    /// [kullanıcı kararı 2026-09-20] Bu düğümün derlenmesi ortak kopyayı GERÇEKTEN tazeler mi — cascade'in
    /// (<see cref="BehindDirtyUpstream"/>) tohum ölçütü. Derlenecek olmak yetmez, YENİ bir çıktı gelmesi gerekir:
    /// <c>SignatureChanged</c>, <c>NeverBuilt</c>, <c>DepIssue</c>, <c>OutputStale</c>, <c>OutputMissing</c> ve
    /// <c>OutputReplaced</c> böyledir.
    ///
    /// <para>İki gerekçe DIŞARIDADIR. <see cref="WillBuildReason.LastFailed"/> KANITLI bir derleyici hatasıdır:
    /// hata anındaki imza bugünküyle AYNI, yani kaynaklar değişmedi — <b>en olası</b> sonuç aynı hatanın
    /// tekrarlanması ve ortak kopyanın olduğu gibi kalmasıdır. Arkasındaki zaman kipi düğümü tam da o kopyaya
    /// karşı dışarıda derlenmiştir; onu gri <c>affected</c>'a çekmek yanlış bir bayatlık iddiasıdır (defter
    /// kipindeki bağımlı aynı durumda yeşil + uyarı üçgeni okunur, iki kip ayrışamaz).
    /// <b>Kabul edilen bedel:</b> imza yalnız kaynakları özetler, bu yüzden nedeni kaynakta OLMAYAN bir hata
    /// (eksik DLL, restore, kilitli dosya) aradan düzelmiş olabilir ve kök bu koşuda BAŞARIYLA derlenebilir;
    /// o zaman arkasındaki zaman kipi düğümü bir tur pre-skip kalır ve ancak bir sonraki Sync'te — kendi
    /// HintPath hedefinin yeni tarihinden — bayat okunup derlenir. Bir tur gecikme, her koşuda yanlış bir
    /// <c>affected</c>'a yeğlenir.</para>
    ///
    /// <para><see cref="WillBuildReason.WaitingForDependency"/> ise KOŞULLUDUR: koşu onu yalnız bir kök
    /// düzelirse derler (<see cref="ConditionalRebuild"/>), yani yeni çıktı bir olgu değil bir ihtimaldir. Kök
    /// gerçekten düzelir ve koşullu proje derlenirse, onun arkasındaki zaman kipi düğümünü bir sonraki Sync
    /// kendi HintPath hedefinin tarihinden zaten bayat okur. <b>Ama döngü üyesi koşullu DEĞİLDİR</b>
    /// (<see cref="ConditionalRebuild.AppliesTo"/>: grup tek iş kalemidir, bir üyeyi atlamak grubu yarım
    /// bırakırdı) — bu yüzden <c>WaitingForDependency</c> okuyan bir SCC üyesi derleneceği koşuda (Cycles)
    /// KOŞULSUZ derlenir ve tohumdur. <b>Bilinen dar boşluk:</b> satırdan tetiklenen tek proje koşusunda
    /// (<c>scopedRun</c>) hedef de koşulsuz derlenir; planlayıcı koşunun kapsamını görmediği için orada
    /// <c>WaitingForDependency</c> bir hedef tohum sayılmaz. Bedeli dardır: o koşunun planı yalnız hedefi ve
    /// bağımlılıklarını taşır, aşağı akışı zaten içermez.</para>
    ///
    /// <para>Karışık hâl kendiliğinden doğrudur: hem kanıtlı hatanın hem içeriği değişmiş bir upstream'in
    /// arkasındaki düğüm, ikincisinin tohumundan gezintiye girer ve gri <c>affected</c> olur.</para>
    /// </summary>
    private static bool ProducesNewOutput(ProjectNode node) =>
        node.WillBuild == true
        && node.WillBuildReason != WillBuildReason.LastFailed
        && (node.WillBuildReason != WillBuildReason.WaitingForDependency || node.InCycle);

    /// <summary>
    /// [D1][D5] Bir projenin içerik fingerprint'i: girdi dosyalarının (bkz. <see cref="ProjectInputs"/>)
    /// DİSKTEKİ içeriğinden hesaplanan deterministik (sıralı, case-insensitive) tek hash. Ana repo, harici
    /// kökler, sürüm kontrolsüz klasörler — hepsi bu TEK yoldan geçer.
    ///
    /// <para><b>Terim çifti: yol + içerik.</b> Yol terimi <paramref name="pathTermOf"/> ile üretilir (çalışma
    /// alanı köküne göreli, <c>/</c>-normalize — bkz. <see cref="IncrementalRunBinder.PathTerm"/>), içerik ise
    /// aynı dosyanın diskteki hâlinden okunur.</para>
    ///
    /// <para>§4 kaynak-sinyali kuralı korunur: parmak izi için yalnız kaynak dosya İÇERİĞİ okunur — DLL/bin/obj
    /// ya da bir derleme çıktısının timestamp'ı ASLA (çıktı zamanı yalnız karara girer, bkz. <see
    /// cref="OutputEvidence"/>). Okuma bedeli <see cref="SourceHashCache"/> ile koşu başına bir
    /// stat geçişine iner.</para>
    ///
    /// <para>Okunamayan dosyalar (canlı build ↔ tarama yarışı, silinmiş dosya) sessizce elenir; hiçbiri
    /// okunamazsa <c>null</c> döner ve proje "hiç derlenmemiş" gibi ele alınır — güvenli taraf (over-build).</para>
    /// </summary>
    /// <param name="inputs">Projenin girdi dosyaları.</param>
    /// <param name="pathTermOf">Dosya yolu → imzaya girecek yol terimi.</param>
    /// <param name="hashOf">Dosya yolu → içerik özeti; okunamıyorsa <c>null</c>.</param>
    public static string? ComputeContentFingerprint(
        IReadOnlyList<ProjectInput> inputs, Func<string, string> pathTermOf, Func<string, string?> hashOf)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(pathTermOf);
        ArgumentNullException.ThrowIfNull(hashOf);

        var terms = inputs
            .Select(i => (Term: pathTermOf(i.Path), Hash: hashOf(i.Path)))
            .Where(x => x.Hash is not null)
            .GroupBy(x => x.Term, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Term: g.Key, g.First().Hash))
            .OrderBy(x => x.Term, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (terms.Count == 0) return null;

        var byTerm = terms.ToDictionary(x => x.Term, x => x.Hash!, StringComparer.OrdinalIgnoreCase);
        return HashFingerprint([.. terms.Select(x => x.Term)], term => byTerm[term]);
    }

    /// <summary>Fingerprint gövdesi — ayraçlar, boundary-shift koruması ve hash primitifi tek yerde
    /// (kopya YASAK, CLAUDE.md).</summary>
    private static string HashFingerprint(IReadOnlyList<string> orderedPaths, Func<string, string> termOf)
    {
        var sb = new StringBuilder();
        foreach (var path in orderedPaths)
        {
            // RAW yol terimi ASLA doğrudan ayraç yanına gömülmez — BuildSignature'daki boundary-shift
            // korumasıyla aynı kalıp (bkz. BuildSignatureTests: separator/`=` içeren id/yol testleri).
            // HashText ve ItemSeparator, BuildSignature'daki AYNI primitive'lerin (internal) reuse'u.
            sb.Append(BuildSignature.HashText(path)).Append('=').Append(termOf(path)).Append(BuildSignature.ItemSeparator);
        }

        return BuildSignature.HashText(sb.ToString());
    }
}
