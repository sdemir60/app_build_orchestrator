namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// Bir <c>RunMode.Cycles</c> koşusunun KAPSAMI: dairesel bağımlılık (SCC) üyeleri <b>ve</b> onların transitif
/// upstream'i. Kapsam dışındaki her proje o koşuda pre-skip edilir.
///
/// <para><b>Neden yalnız üyeler YETMEZ.</b> Bir üye, döngü dışındaki KİRLİ bir bağımlılığının bir önceki
/// nesil DLL'ine karşı derlenirse derleme başarılı olur — ama üretilen çıktı bayattır. Koşu sonunda o üyenin
/// imzası persist edilir ve imza, upstream'in KAYNAK terimini zaten içerdiği için bir sonraki <c>Build</c> onu
/// "güncel" sayıp atlar: proje, kimse fark etmeden kalıcı olarak bayat bir binary'e link'li kalır. §4 gereği
/// DLL/bin timestamp'i OKUNMADIĞI için bunu yakalayacak ikinci bir mekanizma yoktur. Upstream'i kapsama almak
/// bu deliği kapatır — koşu kendi içinde tutarlıdır: derlediği her şeyi TAZE girdilere karşı derler.</para>
///
/// <para><b>Neden downstream kapsama GİRMEZ.</b> Bir SCC'nin dependent'leri o grubun çıktısına bağlıdır ve
/// grup derlendikten sonra yeniden derlenmeleri gerekebilir — ama bu, bu koşunun işi değildir: kullanıcı
/// Cycles'ı Build'den ÖNCE çalıştırır ve dependent'leri zaten Build derler. Downstream'i de almak, kapsamı
/// sessizce tüm repoya genişletirdi (bir çekirdek kütüphanenin dependent kümesi pratikte her şeydir) — yani
/// düğmenin var oluş sebebini, "ne kadar ödediğini bilerek ödemeyi", ortadan kaldırırdı.</para>
///
/// Saf Core state: I/O, process, async, log YOK [D3].
/// </summary>
public static class CycleRunScope
{
    /// <summary>
    /// <paramref name="plan"/>'daki SCC üyeleri + onların transitif bağımlılıkları (kendileri dahil), proje
    /// id'leri kümesi olarak. Plan'da hiç SCC yoksa küme BOŞ döner — o koşu hiçbir şey derlemez ve bu doğrudur
    /// (App düğmeyi zaten o durumda pasif tutar). Plan'da karşılığı olmayan bağımlılık id'leri sessizce
    /// atlanır: kümenin tek tüketicisi "bu düğüm kapsamda mı" sorusudur ve orada var olmayan bir id'nin
    /// karşılığı zaten yoktur.
    /// </summary>
    public static IReadOnlySet<string> Of(BuildPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var byId = new Dictionary<string, ProjectNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in plan.Nodes) byId[node.Id] = node;

        var scope = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Yığın tabanlı geçiş: dairesel kenarlar tam da burada beklenir, özyineleme taşardı.
        var pending = new Stack<string>();
        foreach (var cycle in plan.Cycles)
            foreach (string id in cycle)
                if (scope.Add(id)) pending.Push(id);

        while (pending.Count > 0)
            if (byId.TryGetValue(pending.Pop(), out var node))
                foreach (string dep in node.Dependencies)
                    if (scope.Add(dep)) pending.Push(dep);

        return scope;
    }
}
