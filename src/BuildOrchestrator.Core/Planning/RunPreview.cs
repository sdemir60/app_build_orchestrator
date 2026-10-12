namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

/// <summary>
/// Bir koşunun KENDİ önizlemesinde (<c>buildPreview</c>) bir projenin <c>WillBuild</c>'i: BU koşunun işi. App kuyruğu,
/// sabit ilerleme paydasını, "ortada iş var mı" sorusunu ve açılış satırını bundan okur. Sync'in önizlemesi başka bir
/// soruyu cevaplar (bir sonraki düz Build ne yapar) ve bu kurala girmez.
/// <list type="bullet">
/// <item>Koşunun önden atladığı (pre-skip) proje dispatch edilmez: <c>false</c>.</item>
/// <item>Defteri yok sayan koşu (<see cref="IncrementalModes"/> dışı — Rebuild derler, Clean temizler) önden atlamadığı
/// her projeyi işler: <c>true</c>, planlayıcı ne derse desin.</item>
/// <item>Defteri dinleyen koşu (Build, Cycles) planlayıcının cevabını taşır — tohumu ve koşullu kararları o cevaptan
/// kurulur. Cevabı olmayan (<c>null</c>: artımlı geçiş düştü) proje önden hiç atlanmaz ve derlenir: <c>true</c>.
/// Planlayıcının <c>false</c> dediği ama önden atlanmayan proje — kökünü bekleyen ya da kısmen güncel bir döngü
/// grubunun güncel üyesi — <c>false</c> kalır: derlenip derlenmeyeceği grubun kaderine bağlıdır
/// (<see cref="ConditionalRebuild.GroupAppliesTo"/>, tur 1'in kararı), önizleme yapmayacağı bir işi vaat etmez.</item>
/// </list>
/// <para><b>[DEĞİŞEN KURAL — 2026-10-12]</b> Önizleme pre-skip edilmeyen her düğümde planlayıcının cevabını AYNEN taşırdı.
/// Ölçülen kusur (gerçek koşu, 10:44 Rebuild, 194 proje, hepsi güncel): Rebuild hiçbir şeyi önden atlamaz ama güncel
/// projelere <c>false</c> gidiyordu — dalganın yaktığı düğümler koşu başlar başlamaz yeşile döndü, şerit koşu boyunca
/// "Checking…" dedi; artımlı geçişi düşen koşuda motor her şeyi derlerken önizleme <c>null</c> gönderiyordu. Kuralın ara
/// bir hâli ("önden atlanmayan her proje <c>true</c>") Build'de kökünü bekleyen karışık grubun güncel üyesini kesin iş
/// diye yazdı (inceleme, aynı gün) — defteri dinleyen koşu bu yüzden planlayıcının cevabında kalır.</para>
/// </summary>
public static class RunPreview
{
    public static bool WillBuild(ProjectNode node, RunMode mode, bool preSkipped)
    {
        ArgumentNullException.ThrowIfNull(node);
        return !preSkipped && (!IncrementalModes.Includes(mode) || node.WillBuild != false);
    }
}
