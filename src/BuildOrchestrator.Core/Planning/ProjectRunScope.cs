namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;

/// <summary>
/// [tek proje · design v1.11.0 §3.8] Satırdan tetiklenen bir koşunun KAPSAMI: <b>yalnız hedef proje</b>.
/// Bağımlılıklar derlenmez, kapsam dışına dokunulmaz — plan tek düğüme indirgenir ve diğer projeler koşuya
/// hiç girmez (skip satırı yok, sayaç şişmez).
///
/// <para><b>Hedef HER ZAMAN derlenir</b> — güncel olsa bile. Satırdaki play bir soru değil bir EMİRDİR
/// (design §3.8 "koşul yok"; prototip <c>beginProject</c> hedefi koşulsuz <c>willBuild</c>'e sokar).
/// <b>[DEĞİŞEN KURAL — ölçüldü]</b> Önce düğümün kendi <see cref="ProjectNode.WillBuild"/>'i korunuyordu, yani
/// güncel bir hedef tam koşudaki gibi <c>skipped — up to date</c> oluyordu; sahada bunun anlamı "ilk basış
/// derliyor, ikinci basış hiçbir şey yapmadan satırı gri bırakıyor" oldu. Tek projelik bir kapsamda
/// incremental karar korunacak bir bilgi değil, yutulan bir komuttur. Kapsam DIŞI her şey (bağımlılıklar
/// dahil) yine dokunulmadan kalır.</para>
///
/// <para><b>Bayat bağımlılık = dep-issue.</b> Kirli (ya da bilinmeyen) bir bağımlılık bu koşuda derlenmez;
/// hedef onun SON BİLİNEN çıktısına karşı derlenir. Bu, <see cref="CycleRunScope"/>'un anlattığı deliğin ta
/// kendisidir: hedef yeşil döner, taze imzası (upstream terimi bağımlılığın YENİ kaynağını zaten içerir)
/// persist edilir ve bir sonraki Build onu "güncel" sayıp bir daha derlemez — proje kalıcı olarak bayat bir
/// DLL'e link'li kalır, §4 gereği DLL/bin timestamp'i okunmadığı için bunu yakalayacak ikinci mekanizma
/// yoktur. Kapsamı genişletmek yerine (tasarım "build with dependencies" istemedi) bağımlılık dep-issue
/// olarak hedefe yapışır: log başında uyarı, satırda üçgen, defterde NOT (§8.3) — ve o not bir sonraki
/// Build'de hedefi derleme listesinde tutar. Bilinmeyen (<c>null</c>) de bayat sayılır: güvenli yön bir kez
/// daha derlemektir.</para>
///
/// <para><b>Döngü üyesi hedef</b> tek başına, döngü dışıymış gibi derlenir (<see cref="ProjectNode.InCycle"/>
/// düşer — scheduler onu pre-skip etmesin) ve planın "kapsam dışı" anlamındaki <c>WillBuild=false</c>'unu
/// taşımaz (o değer "güncel" demek değildir; Build modunda hedefi <c>up to date</c> diye atlatırdı).
/// Döngüdeki bağımlılıkları HER koşulda bayattır: üye kardeşlerinin son bilinen çıktısına karşı derlenmiştir,
/// tur koşmadı, yakınsama kanıtı yok — dep-issue notu sayesinde bir sonraki <c>Cycles</c> koşusu grubu
/// "hepsi güncel" sayamaz ve yeniden derler.</para>
///
/// <para>Döngü DIŞI bir hedefin döngü üyesi bağımlılığı ise tam Build'dekiyle aynı muameleyi görür: orada da
/// üye derlenmez ve dependent'ı dep-issue almaz (Skipped bağımlılık issue üretmez) — tek proje koşusu o
/// kuralı değiştirmez.</para>
///
/// Saf Core state: I/O, process, async, log YOK [D3].
/// </summary>
/// <param name="Plan">Yalnız hedefi taşıyan plan. Kapsam dışı projeleri anlatan katman uyarıları düşer.</param>
/// <param name="Target"><see cref="Plan"/>'daki tek düğüm — <c>WillBuild</c> her zaman <c>true</c>.</param>
/// <param name="StaleDependencies">Hedefin bu koşuda DERLENMEYECEK bayat doğrudan bağımlılıkları —
/// koordinatör bunları dep-issue olarak hedefe verir.</param>
public sealed record ProjectRunScope(BuildPlan Plan, ProjectNode Target, IReadOnlyList<StaleDependency> StaleDependencies)
{
    /// <summary>
    /// <paramref name="targetId"/> için kapsam; hedef planda yoksa <c>null</c> — çağıran koşuyu hiç başlatmaz.
    /// Kimlikler Windows yollarıdır → harf-duyarsız eşleşir.
    /// </summary>
    public static ProjectRunScope? Of(BuildPlan plan, string targetId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(targetId);

        var target = plan.Nodes.FirstOrDefault(n => string.Equals(n.Id, targetId, StringComparison.OrdinalIgnoreCase));
        if (target is null) return null;

        var byId = new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in plan.Nodes) byId[candidate.Id] = candidate;

        var stale = new List<StaleDependency>();
        foreach (string depId in target.Dependencies)
        {
            if (!byId.TryGetValue(depId, out var dep)) continue;
            bool cycleMate = target.InCycle && dep.InCycle;
            if (cycleMate || dep.WillBuild != false) stale.Add(new StaleDependency(dep.Id, dep.Name, dep.InCycle));
        }

        var node = target with
        {
            BuildOrder = 0,
            InCycle = false,
            // Hedef koşulsuz derlenir; gerekçe YALNIZ gerçekten kirliyken taşınır — güncel bir projede
            // hiçbir <see cref="WillBuildReason"/> doğru değildir (UpToDate + WillBuild=true çelişkidir) ve
            // yüzey o durumda jenerik metne düşer.
            WillBuild = true,
            WillBuildReason = target.WillBuild == true ? target.WillBuildReason : null,
        };
        return new ProjectRunScope(
            new BuildPlan([node], Cycles: [], plan.Configuration, LayerWarnings: null),
            node,
            stale);
    }

    /// <summary>Kullanıcıya giden gerekçe (planlama-hatası kanalı): hedef planda yok — topoloji bayat.</summary>
    public static string NotInPlanMessage(string targetId) =>
        $"'{Path.GetFileNameWithoutExtension(targetId)}' is not in the workspace plan — it may have been moved or removed. Sync, then build it again.";
}
