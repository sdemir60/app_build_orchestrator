namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [PERF Faz D / karar 10] <see cref="WorkerBudget.Clamp"/>'in cevabı. <c>Workers</c> FİİLİ işçi sayısıdır (en az 1);
/// <c>Reason</c> null ise istenen sayı olduğu gibi verilmiştir (kırpılmadı), doluysa kırpmanın gerekçesidir: Supervisor
/// onunla decision.log satırını yazar ve <c>runStarted</c>'la App'e taşır (App konsol ve event stream satırını yazar);
/// metin tek kaynaktan gelir (<see cref="PerfNoteText.WorkersReduced"/>).
/// </summary>
public readonly record struct WorkerBudgetDecision(int Workers, string? Reason);

/// <summary>
/// [PERF Faz D / karar 10] İşçi bütçesi: profilin istediği işçi (paralel MSBuild) sayısı, makinenin mantıksal işlemci
/// sayısına ve boş fiziksel belleğine göre koşu başında BİR KEZ kırpılır. Kural ve TÜM sabitleri burada, tek yerde;
/// Supervisor yalnız uygular (planlama Core'dadır). Saf fonksiyondur: makineyi okumaz — onu
/// <see cref="MachineResources"/> okur — böylece kural donanımdan bağımsız test edilir.
/// <para><b>Çekirdek kuralı:</b> işçi, mantıksal işlemcinin <see cref="WorkersPerCore"/> katını aşarsa kırpılır.
/// Gerçek OSYS Rebuild'i yakınlık maskesiyle 2 ve 4 mantıksal işlemcide 1-4 işçiyle ölçüldü: işlemciden az işçi süreyi
/// uzattı (iki işlemcide tek işçi iki işçinin neredeyse iki katı sürdü, dört işlemcide dört işçi üçten hızlıydı); küçük
/// makinede üçüncü ve dördüncü işçi hâlâ küçük ama tutarlı kazanç verdi, büyük makinede dört işçi en hızlıydı. Ölçülen en
/// yüksek oran (küçük makinede dört işçi = işlemcinin iki katı) tavan alındı; daha fazlası ölçülmedi.</para>
/// <para><b>Bellek kuralı (güvenlik ağı):</b> boş bellekten <see cref="ReserveBytes"/> makineye (ve motorla ilk işçinin
/// tabanına) bırakılır, kalan işçi başına <see cref="BytesPerWorker"/> ile bölünür. İşçi payı gerçek bir derlemenin
/// ölçümüne dayanır (Clean → Build, tam derleyici): motor ile ilk işçinin tabanından sonra her ek işçi birkaç yüz MB
/// commit ekledi; kural bunun üstüne pay koyar. Makine payı ölçülen o tabanı karşılar; geri kalanı (IDE, tarayıcı,
/// işletim sistemi için bırakılan) ölçüm değil yargıdır (ARCHITECTURE §11.1). Kural düşük bellekli makinede devreye
/// girer.</para>
/// </summary>
public static class WorkerBudget
{
    private const long Gb = 1024L * 1024 * 1024;

    /// <summary>Mantıksal işlemci başına izin verilen işçi — çekirdek kuralının TEK parametresi.</summary>
    public const int WorkersPerCore = 2;

    /// <summary>Bir işçi için bütçelenen bellek: gerçek derlemede ölçülen ek işçi maliyetinin (birkaç yüz MB) üstüne
    /// konan pay (ARCHITECTURE §11.1).</summary>
    public const long BytesPerWorker = Gb / 2;

    /// <summary>Makineye bırakılan pay: boş bellekten bu kadarı işçilere AYRILMAZ. Motorun ve ilk işçinin sabit tabanını
    /// ve makinenin geri kalanını (IDE, tarayıcı, işletim sistemi) karşılar.</summary>
    public const long ReserveBytes = 2 * Gb;

    /// <summary>
    /// <paramref name="requested"/> = profilin işçisi; <paramref name="cores"/> = mantıksal işlemci;
    /// <paramref name="freeBytes"/> = boş fiziksel bellek. Sonuç HİÇBİR girdide 1'in altına inmez ve istenen sayıyı
    /// aşmaz (istek de en az 1 sayılır). İki sınır aynı anda bağlıysa bellek adlandırılır — kullanıcının elinde
    /// çevirebileceği şey odur (ARCHITECTURE §11.1: ilk sınır genelde bellektir).
    /// </summary>
    public static WorkerBudgetDecision Clamp(int requested, int cores, long freeBytes)
    {
        requested = Math.Max(1, requested);
        int byCores = (int)Math.Min(int.MaxValue, (long)WorkersPerCore * Math.Max(1, cores));
        long spareBytes = freeBytes > ReserveBytes ? freeBytes - ReserveBytes : 0;   // negatif/taşan girdi güvenli
        int byMemory = (int)Math.Clamp(spareBytes / BytesPerWorker, 1, int.MaxValue);
        int workers = Math.Min(requested, Math.Min(byCores, byMemory));
        if (workers == requested) return new WorkerBudgetDecision(workers, null);

        string reason = byCores < byMemory
            ? PerfNoteText.LogicalProcessorsLimit(cores)
            : PerfNoteText.FreeMemoryLimit(Math.Max(0, freeBytes) / Gb);
        return new WorkerBudgetDecision(workers, reason);
    }
}
