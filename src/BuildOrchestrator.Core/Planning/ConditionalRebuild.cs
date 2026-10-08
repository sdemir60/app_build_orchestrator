namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;

/// <summary>Koşullu bir projenin sırası geldiğinde verilen karar.</summary>
public enum ConditionalRebuildVerdict
{
    /// <summary>Normal şekilde derlenir.</summary>
    Build,
    /// <summary>Kayıtlı köklerin HEPSİ hâlâ hatalı — atlanır (<see cref="SkipReasons.DependencyStillFailing"/>).</summary>
    DependencyStillFailing,
}

/// <summary>
/// Koşullu yeniden derlemenin saf kararları. Planlama <see cref="WillBuildEvaluator"/>'da
/// <see cref="WillBuildReason.WaitingForDependency"/> der; bu sınıf (1) bir koşunun o projeyi GERÇEKTEN koşullu
/// değerlendirip değerlendirmediğini ve (2) sırası geldiğinde derlenip derlenmeyeceğini söyler. Koordinatör
/// yalnız uygular: I/O, process, scheduler mutasyonu YOK.
///
/// <para><b>Neden koşullu.</b> Başarısız bir bağımlılığa rağmen başarıyla derlenen proje, o bağımlılığın SON
/// BAŞARILI çıktısına link'lidir. Bağımlılık hâlâ patlıyorsa projeyi yeniden derlemek hiçbir şey kazandırmaz:
/// aynı bayat çıktıya yeniden link'lenir. Ama bağımlılık bir gün KAYNAK DEĞİŞMEDEN düzelirse projenin imzası da
/// değişmez — çıktı aracın kendisinin olduğundan defter kipinde okunur ve orada çıktının tarihi eşleşen
/// imzayı bozmaz (ARCHITECTURE §7.6): onu yeniden derlemeye götürecek tek sinyal defterdeki nottur.
/// Tetik bu yüzden "her Build'de" değil "kök düzeldiğinde"dir.</para>
///
/// <para><b>Kök sonucu nereden okunur.</b> Kararın anı projenin hazır olduğu andır: tüm bağımlılıkları (ve
/// dolayısıyla onların üstündeki kökler) bu koşuda terminaldir. Bu koşuda DERLENEN kökün sonucu koşudan okunur;
/// derlenmeyen (up to date / kapsam dışı / döngüde atlanan ya da koşuda hiç görünmeyen) kökün önce BU KOŞUNUN
/// ÖNİZLEMESİNE bakılır — çıktısı güncel bulunduysa kök temizdir — ve ancak orada bir şey kanıtlanamıyorsa koşu
/// başındaki defter okunur. Belirsizlikte yön DERLEMEdir: kök bilinmiyorsa, projede artık yoksa ya da defterde
/// kaydı yoksa proje derlenir.</para>
/// </summary>
public static class ConditionalRebuild
{
    /// <summary>
    /// Bu koşu <paramref name="node"/>'u TEK BAŞINA koşullu mu değerlendirir. Yalnız Build ve Cycles koşuları;
    /// satırdan tetiklenen tek proje koşusunun hedefi koşulsuz derlenir (kullanıcının açık komutu), Rebuild her şeyi
    /// derler. Bir SCC grubunun üyesi tek başına koşullu değildir: grup tek iş kalemidir ve bir üyeyi atlayıp
    /// diğerlerini derlemek grubu yarım bırakırdı — üye yalnız grubuyla birlikte koşulludur (<see cref="ConditionalIds"/>).
    /// </summary>
    public static bool AppliesTo(ProjectNode node, RunMode mode, bool scopedRun, bool cycleGroupMember) =>
        ModeEvaluatesConditionally(mode)
        && !scopedRun
        && !cycleGroupMember
        && Waits(node);

    /// <summary>Proje YALNIZ kökünü bekliyor: kirli, ama tek gerekçesi kayıtlı köklerinin hâlâ hatalı olabilmesi.
    /// Tekil (<see cref="AppliesTo"/>), grup (<see cref="GroupAppliesTo"/>) ve küme (<see cref="ConditionalIds"/>)
    /// kuralının ORTAK tanımı (kopya YASAK).</summary>
    private static bool Waits(ProjectNode node) =>
        node.WillBuild == true && node.WillBuildReason == WillBuildReason.WaitingForDependency;

    /// <summary>
    /// [grup koşullu atlama] Bir SCC, dispatch anında GRUP OLARAK koşullu değerlendirilebilir mi.
    /// <see cref="AppliesTo"/> üyeyi tek başına koşullu saymaz — bir üyeyi atlayıp diğerlerini derlemek grubu
    /// yarım bırakırdı; grubun TAMAMI atlandığında ise yarım kalma yoktur ve tekil kural (kök düzelince derle,
    /// hâlâ kırıksa atla) atomik olarak gruba uygulanabilir. Uygunluk: koşu koşullu değerlendiren bir mod olmalı
    /// (<see cref="AppliesTo"/> ile AYNI kural — Rebuild her şeyi derler); her üye ya güncel (<c>WillBuild==false</c>)
    /// ya da YALNIZ kökünü bekliyor (<c>true</c> + <see cref="WillBuildReason.WaitingForDependency"/>) olmalı ve
    /// en az bir bekleyen üye bulunmalıdır (hepsi güncel olsaydı grup zaten pre-skip edilirdi). Başka HERHANGİ
    /// bir gerekçeyle kirli tek üye grubu derletir — güvenli yön. Kararın kendisi (kökler hâlâ kırık mı) üye
    /// başına <see cref="Decide"/>'a sorulur; TEK düzelen kök bile grubu normal derletir.
    /// <para>[ara inceleme I1 — Build cycle derler] Mod kuralı eskiden burada YOKTU: grupları yalnız Cycles koşusu
    /// derlediği için gerek yoktu. Rebuild de grup derlemeye başlayınca kökünü bekleyen grup Rebuild'de atlanıyor,
    /// aynı köke bekleyen tekil proje derleniyordu.</para>
    /// </summary>
    public static bool GroupAppliesTo(IReadOnlyList<ProjectNode> members, RunMode mode)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (!ModeEvaluatesConditionally(mode)) return false;
        bool anyWaiting = false;
        foreach (var member in members)
        {
            if (member.WillBuild == false) continue;
            if (Waits(member))
            {
                anyWaiting = true;
                continue;
            }
            return false; // kendi sebebiyle kirli (imza/asla derlenmedi/hata…) ya da karar yok (null) → derle
        }
        return anyWaiting;
    }

    /// <summary>Koşullu değerlendirme yapan modlar — tekil (<see cref="AppliesTo"/>) ve grup
    /// (<see cref="GroupAppliesTo"/>) kapısının ORTAK kuralı (kopya YASAK): Build ve Cycles. Rebuild her şeyi derler,
    /// Clean hiçbir şey derlemez.</summary>
    private static bool ModeEvaluatesConditionally(RunMode mode) => mode is RunMode.Build or RunMode.Cycles;

    /// <summary>
    /// [ara inceleme I2] Bir koşunun — ya da Sync'in simüle ettiği bir sonraki düz Build'in — sırası geldiğinde KOŞULLU
    /// değerlendireceği projeler. Koordinatör (koşunun kendi önizlemesi ve kuyruğu) ile Sync (önizleme ve "N to build")
    /// AYNI kümeyi buradan alır (kopya YASAK): grup üyeliği <paramref name="groups"/>'tan
    /// (<see cref="CycleCompilation.GroupsFor"/>) okunur. <paramref name="preSkipped"/>: koşunun baştan atladığı
    /// projeler — atlanan proje koşullu da değildir.
    /// <para><b>Grup üyesi</b> tek başına koşullu değildir; grubu dispatch anında BÜTÜN OLARAK koşullu
    /// değerlendirilecekse (<see cref="GroupAppliesTo"/>) bekleyen üyeleri kümededir. <b>[DEĞİŞEN KURAL — final review
    /// M-2]</b> Eskiden grup üyesi kümeye hiç girmezdi: Build'in açılış dalgası, kuyruğu ve "N to build" sayısı yalnız
    /// kökünü bekleyen grubu KESİN sayıyor, koşu ise grubu <c>dependency still failing</c> ile atlayabiliyordu —
    /// sıradan bekleyen projenin hiç vermediği bir söz. Karar grup düzeyinde kalır; söz grubun kaderine bağlanır.</para>
    /// </summary>
    public static IReadOnlySet<string> ConditionalIds(IReadOnlyList<ProjectNode> nodes, RunMode mode, bool scopedRun,
        CycleGroups? groups, IReadOnlySet<string>? preSkipped = null)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var byId = new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes) byId[node.Id] = node;
        // Grup kararı grup başına bir kez (anahtar: build-order lideri — MembersOf her üyeye aynı listeyi verir).
        var groupWaits = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        return nodes
            .Where(n => (preSkipped is null || !preSkipped.Contains(n.Id)) && IsConditional(n))
            .Select(n => n.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool IsConditional(ProjectNode node)
        {
            var members = groups?.MembersOf(node.Id) ?? [];
            if (members.Count == 0) return AppliesTo(node, mode, scopedRun, cycleGroupMember: false);
            if (scopedRun || !Waits(node)) return false;
            if (!groupWaits.TryGetValue(members[0], out bool waits))
            {
                var memberNodes = new List<ProjectNode>(members.Count);
                foreach (string id in members)
                    if (byId.TryGetValue(id, out var member)) memberNodes.Add(member);
                // Planda olmayan üye (savunmacı): grup kesin derlenir sayılır — güvenli yön, koordinatörle aynı.
                waits = memberNodes.Count == members.Count && GroupAppliesTo(memberNodes, mode);
                groupWaits[members[0]] = waits;
            }
            return waits;
        }
    }

    /// <summary>
    /// Koşullu projenin sırası geldiğinde kararı: köklerden EN AZ BİRİ başarılıysa (bu koşuda başarıyla
    /// derlendi, ya da bu koşuda derlenmedi ama defterdeki son sonucu başarı) derlenir; hepsi hâlâ hatalıysa
    /// (bu koşuda patladı, ya da derlenmedi ve defterdeki son sonucu hata) atlanır.
    /// </summary>
    /// <param name="rootIds">Defterdeki kök proje kimlikleri (<see cref="BuildState.DepIssueRoots"/>). Boş/null ⇒
    /// kök bilinmiyor ⇒ derlenir.</param>
    /// <param name="completedThisRun">Bu koşunun terminal sonuçları (pre-skip tohumları dahil).</param>
    /// <param name="inWorkspace">Kimlik bu koşunun planında var mı. Yoksa ⇒ derlenir.</param>
    /// <param name="ledgerAtRunStart">Koşu başında okunan defter. Kök kaydı yoksa ⇒ derlenir.</param>
    /// <param name="reasonOf">Kökün BU KOŞUNUN önizlemesindeki gerekçesi (<c>ProjectNode.WillBuildReason</c>);
    /// bilinmiyorsa <c>null</c>. Bkz. <see cref="ClassifyRoot"/> — defterin son sonucundan DAHA TAZE kanıttır.</param>
    public static ConditionalRebuildVerdict Decide(IReadOnlyList<string>? rootIds,
        IReadOnlyDictionary<string, BuildResult> completedThisRun, Func<string, bool> inWorkspace,
        IReadOnlyDictionary<string, BuildState>? ledgerAtRunStart, Func<string, WillBuildReason?> reasonOf)
    {
        ArgumentNullException.ThrowIfNull(completedThisRun);
        ArgumentNullException.ThrowIfNull(inWorkspace);
        ArgumentNullException.ThrowIfNull(reasonOf);
        if (rootIds is not { Count: > 0 }) return ConditionalRebuildVerdict.Build;

        foreach (string root in rootIds)
            if (ClassifyRoot(root, completedThisRun, inWorkspace, ledgerAtRunStart, reasonOf) == RootEvidence.Cleared)
                return ConditionalRebuildVerdict.Build;
        return ConditionalRebuildVerdict.DependencyStillFailing;
    }

    /// <summary>Bir kökün tekil kanıtı — <see cref="Decide"/> ve <see cref="DescribeStillFailingRoots"/>'un
    /// PAYLAŞTIĞI TEK sınıflandırma (kopya YASAK): kararı verdiren mantık ile o kararı METNE döken mantık aynı
    /// kaynaktan okur, aksi halde ikisi sessizce ayrışabilirdi.</summary>
    private enum RootEvidence
    {
        /// <summary>Kök artık temiz (başarılı ya da projeden düştü) — proje derlenmeli.</summary>
        Cleared,
        /// <summary>Kök BU KOŞUDA patladı — kanıt taze.</summary>
        FailedThisRun,
        /// <summary>Kök bu koşuda hiç denenmedi (skip/yok); "hâlâ hatalı" iddiası yalnız koşu BAŞINDAKİ
        /// defterin son bilinen sonucundan geliyor.</summary>
        FailedInLedgerOnly,
    }

    /// <summary>
    /// Bir kökün kanıt sırası: çalışma alanından düştü → bu koşuda derlendi → BU KOŞUNUN ÖNİZLEMESİ →
    /// koşu başındaki defter.
    ///
    /// <para><b>Önizleme defterden daha tazedir (kullanıcı kararı 2026-09-20).</b> Kök bu koşuda derlenmediyse
    /// ve önizleme onun çıktısını GÜNCEL buluyorsa (<see cref="WillBuildEvaluator.OutputIsCurrent"/> — yani
    /// <c>up to date</c> ya da <c>built outside this tool</c>) kök temizdir, defterin "son sonuç: hata" kaydına
    /// BAKILMAZ. O kayıt aracın en son ne gördüğünü anlatır; önizleme ise DİSKİN ŞU ANKİ hâlini. İki kip de bu
    /// deliği taşıyordu: kök dışarıda (VS'te) düzeltilip derlendiğinde zaman kipinde <c>BuiltOutside</c> okunup
    /// pre-skip edilir, defterde ise hâlâ <c>Failed</c> durur — bağımlı, araç kökü kendisi derleyene kadar her
    /// koşuda <c>dependency still failing</c> ile atlanırdı (kilitli durum). Defter kipinde de aynı hâl
    /// (kaydı <c>LastResult=Failed</c> ama imzası eşleşen, hata imzası artık tutmayan bir kök <c>up to date</c>
    /// okunur) daha seyrek olarak vardı. TEK kural ikisini de kapatır.</para>
    ///
    /// <para>Güncel OLMAYAN bir gerekçe (ör. kapsam dışı bir döngü üyesinin <c>SignatureChanged</c>'i) hiçbir
    /// şey KANITLAMAZ — orada karar yine defterin son bilinen sonucuna düşer.</para>
    /// </summary>
    private static RootEvidence ClassifyRoot(string root, IReadOnlyDictionary<string, BuildResult> completedThisRun,
        Func<string, bool> inWorkspace, IReadOnlyDictionary<string, BuildState>? ledgerAtRunStart,
        Func<string, WillBuildReason?> reasonOf)
    {
        if (!inWorkspace(root)) return RootEvidence.Cleared;

        // Bu koşuda DERLENDİ: sonucu koşudan. Skipped "derlenmedi" demektir — aşağıdan devam edilir.
        if (completedThisRun.TryGetValue(root, out var result) && result != BuildResult.Skipped)
            return result == BuildResult.Succeeded ? RootEvidence.Cleared : RootEvidence.FailedThisRun;

        if (WillBuildEvaluator.OutputIsCurrent(reasonOf(root))) return RootEvidence.Cleared;

        if (ledgerAtRunStart is null || !ledgerAtRunStart.TryGetValue(root, out var recorded)
            || recorded.LastResult == BuildResult.Succeeded)
            return RootEvidence.Cleared;
        return RootEvidence.FailedInLedgerOnly;
    }

    /// <summary>
    /// [Task 4 — carried item 3] <see cref="Decide"/> <c>DependencyStillFailing</c> derdiğinde, ATLAMA satırının
    /// ("dependency still failing (…)") kök listesini DOĞRU söyler: bir kök BU KOŞUDA gerçekten patladıysa çıplak
    /// adı yazılır (bugünkü davranış — "R failed in this run" iddiasıyla TUTARLI); kök bu koşuda hiç denenmediyse
    /// (atlanmış ya da henüz sonuçlanmamış ve önizlemesi güncel değil) ve "hâlâ hatalı" iddiası
    /// yalnız koşu başındaki DEFTERDEN geliyorsa <c>" (last known failure)"</c> eki eklenir — aksi hâlde satır,
    /// hiç gözlemlenmemiş bir "şimdi de patladı" iddiası taşırdı. Ad sıralı, tekil (<see cref="RootNames"/> ile
    /// AYNI biçim); girdi <see cref="Decide"/>'ın Build dönmediği (yalnız <c>Cleared</c> OLMAYAN kökler) hâli
    /// varsayılır — bir <c>Cleared</c> kök burada görülürse (çağıran hatası) sessizce atlanır.
    /// </summary>
    public static IReadOnlyList<string> DescribeStillFailingRoots(
        IReadOnlyList<string>? rootIds, IReadOnlyDictionary<string, BuildResult> completedThisRun,
        Func<string, bool> inWorkspace, IReadOnlyDictionary<string, BuildState>? ledgerAtRunStart,
        Func<string, WillBuildReason?> reasonOf, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(completedThisRun);
        ArgumentNullException.ThrowIfNull(inWorkspace);
        ArgumentNullException.ThrowIfNull(reasonOf);
        ArgumentNullException.ThrowIfNull(nameOf);
        if (rootIds is not { Count: > 0 }) return [];

        var entries = new List<(string Name, bool LedgerOnly)>();
        foreach (string root in rootIds)
        {
            var evidence = ClassifyRoot(root, completedThisRun, inWorkspace, ledgerAtRunStart, reasonOf);
            if (evidence == RootEvidence.Cleared) continue; // savunmacı: Decide zaten Build dönerdi
            entries.Add((nameOf(root), evidence == RootEvidence.FailedInLedgerOnly));
        }
        return [.. entries
            .DistinctBy(e => e.Name, StringComparer.Ordinal)
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => e.LedgerOnly ? $"{e.Name} (last known failure)" : e.Name)];
    }

    /// <summary>
    /// Kayıtlı köklerin GÖRÜNEN adları (etiket/tooltip ve decision.log satırı için) — yalnız proje
    /// <see cref="WillBuildReason.WaitingForDependency"/> iken dolu. Ad sıralı, tekil; planda olmayan kök için
    /// dosya adı kullanılır.
    /// </summary>
    public static IReadOnlyList<string>? RootNames(WillBuildReason? reason, BuildState? state, Func<string, string?> nameOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        if (reason != WillBuildReason.WaitingForDependency || state?.DepIssueRoots is not { Count: > 0 } roots) return null;
        return [.. roots
            .Select(id => nameOf(id) ?? Path.GetFileNameWithoutExtension(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)];
    }
}
