using System.Globalization;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [PERF Faz D / karar 10] <see cref="WorkerBudget.Clamp"/>'in cevabı. <c>Workers</c> FİİLİ işçi sayısıdır (en az 1);
/// <c>Reason</c> null ise istenen sayı olduğu gibi verilmiştir (kırpılmadı), doluysa konsola yazılacak gerekçedir
/// (<see cref="PerfNoteText.WorkersReduced"/>).
/// </summary>
public readonly record struct WorkerBudgetDecision(int Workers, string? Reason);

/// <summary>
/// [PERF Faz D / karar 10] İşçi bütçesi: profilin istediği işçi (paralel MSBuild) sayısı, makinenin mantıksal işlemci
/// sayısına ve boş fiziksel belleğine göre koşu başında BİR KEZ kırpılır. Kural ve TÜM sabitleri burada, tek yerde;
/// Supervisor yalnız uygular (planlama Core'dadır). Saf fonksiyondur: makineyi okumaz — onu
/// <see cref="MachineResources"/> okur — böylece kural donanımdan bağımsız test edilir.
/// <para><b>Çekirdek kuralı:</b> işçi, mantıksal işlemcinin <see cref="WorkersPerCore"/> katını aşarsa kırpılır. İlk plan
/// hipotezi "işlemci ≤ 2 ise 1, değilse işlemci − 1"di (zayıf makinede ilk sorunun işlemci olduğu sanılmıştı). Ölçüm
/// (gerçek OSYS Rebuild, yakınlık maskesiyle 2 ve 4 mantıksal işlemcide 1-4 işçi) bunu çürüttü: iki işlemcide tek işçi
/// iki işçinin neredeyse iki katı sürdü, dört işlemcide dört işçi üçten hızlıydı. İş işlemci değil süreç/IO gecikmesi
/// ağırlıklı; çekirdek sayısının üstündeki işçi hâlâ kazandırıyor. Ölçülen aralıkta kazanan oran iki kattı, daha
/// fazlası ölçülmedi — tavan bu yüzden oradadır.</para>
/// <para><b>Bellek kuralı (güvenlik ağı):</b> boş bellekten <see cref="ReserveBytes"/> makineye bırakılır, kalan işçi
/// başına <see cref="BytesPerWorker"/> ile bölünür (ARCHITECTURE §11.1'deki gerçek derleme ölçümü). Ölçümdeki Rebuild
/// çoğunlukla güncellik denetimi olduğundan commit artışı bunun çok altında kaldı; kural düşük bellekli makinede devreye
/// girer.</para>
/// </summary>
public static class WorkerBudget
{
    private const long Gb = 1024L * 1024 * 1024;

    /// <summary>Mantıksal işlemci başına izin verilen işçi — çekirdek kuralının TEK parametresi.</summary>
    public const int WorkersPerCore = 2;

    /// <summary>Bir işçinin tahmini commit'i (gerçek derlemede ölçülen; ARCHITECTURE §11.1).</summary>
    public const long BytesPerWorker = 2 * Gb;

    /// <summary>Makineye bırakılan pay: boş bellekten bu kadarı işçilere AYRILMAZ (IDE, tarayıcı, işletim sistemi).</summary>
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
            ? string.Format(CultureInfo.InvariantCulture, cores == 1 ? "{0} logical processor" : "{0} logical processors", cores)
            : string.Format(CultureInfo.InvariantCulture, "{0} GB free memory", Math.Max(0, freeBytes) / Gb);
        return new WorkerBudgetDecision(workers, reason);
    }
}
