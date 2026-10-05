namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [Clean] Bir <c>RunMode.Clean</c> koşusunun planı. Clean hiçbir şey DERLEMEZ: her projede yalnız
/// <c>msbuild -t:Clean</c> koşar ve bir projenin temizliği başka bir projenin çıktısına ihtiyaç duymaz. Clean'in
/// bu yüzden <b>bağımlılık anlamı yoktur</b> ve plan onu taşımaz — kural tek yerdedir, hem Build menüsünün tam
/// Clean'i hem satır menüsünün proje Clean'i (<see cref="ProjectRunScope"/> bu planın ÜSTÜNDE kurulur) buradan
/// geçer:
/// <list type="bullet">
///   <item><b>Kenarlar düşer</b> (<see cref="ProjectNode.Dependencies"/> boş): sıra gerekmez, her proje ilk anda
///   hazırdır ve paralellik tavanına kadar eşzamanlı temizlenir. Derlemeye özgü kurallar da kenar olmadan
///   devreye giremez — hatalı bir bağımlılık dependent'ına dep-issue yapıştırmaz, satır Clean'inde bayat
///   bağımlılık listesi boş çıkar.</item>
///   <item><b>Döngü işareti düşer</b> (<see cref="ProjectNode.InCycle"/> false, <see cref="BuildPlan.Cycles"/>
///   boş): döngü üyesi de temizlenir. Scheduler onu "in dependency cycle" diye önden atlamaz ve dairesel kenar
///   kalmadığı için kilitlenme (A6) yapısal olarak imkânsızdır; tur döngüsü de hiç devreye girmez.</item>
///   <item><b><see cref="ProjectNode.WillBuild"/> = true</b>: önizleme BU KOŞUNUN işini anlatır ve bu koşu her
///   projeyi temizler — kuyruk, ilerleme paydası ve açılış satırı aynı kümeyi okur.</item>
///   <item><b>Gerekçe DOKUNULMAZ</b> (<see cref="ProjectNode.WillBuildReason"/>): etiket bir disk olgusudur ve
///   proje temizlenene kadar çıktısı yerindedir — güncel bir satır temizlendiği ana kadar "up to date" yazar.</item>
///   <item><b>Sıra uyarıları düşer</b> (katman ve belirsiz üretici): ikisi de sıra/kenar uyarısıdır; sırası ve
///   kenarı olmayan bir koşunun konsoluna basılmaz. Belirsiz üretici uyarısını Sync zaten gösterir; katman uyarısını
///   App topolojiden okumaz — kullanıcı onu yalnız tüm planın sırasını izleyen koşuların başında görür
///   (<c>runStarted.Warnings</c>), Clean'de görmez.</item>
/// </list>
/// Grafta ne varsa kapsamdadır: harici projeler de sıradan düğümler olarak kalır (çalışma kopyaları Clean'de
/// güncellenmez — <c>ExternalUpdater.ShouldUpdate</c>).
///
/// Saf Core state: I/O, process, async, log YOK [D3].
/// </summary>
public static class CleanRunScope
{
    public static BuildPlan Of(BuildPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan with
        {
            Nodes = [.. plan.Nodes.Select(n => n with { Dependencies = [], InCycle = false, WillBuild = true })],
            Cycles = [],
            LayerWarnings = null,
            ProducerWarnings = null,
        };
    }
}
