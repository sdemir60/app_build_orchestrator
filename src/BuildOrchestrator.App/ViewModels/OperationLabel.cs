namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.11.0 §2.2 · §9-9] Sticky şeridin solundaki <b>kalıcı işlem pill'inin</b> metni — TEK kaynak.
///
/// <para>Pill "ne yapmıştım?" sorusunu tek bakışta bitirir: koşarken amber yanar, bitince nötrleşir ama
/// <b>bir sonraki işleme kadar KALIR</b>. Faz metni anlıktır (bir sonraki faz üzerine yazar), pill ise son
/// işlemin kimliğidir.</para>
///
/// <para>Etiketler caps ve mono'dur; pill <b>yalnız etiketi</b> taşır — hedef adı hiçbir işlemde eklenmez.</para>
///
/// <para><b>[DEĞİŞEN KURAL — v1.13.2]</b> Eski kural (v1.11.0): hedefli işlemlerde (bir satırdan tetiklenen
/// build/rebuild/clean) hedefin kısa adı em-dash ile ekleniyordu — <c>REBUILD — Sales.Core</c> — ve bunu üreten
/// <c>Compose(label, target)</c> metodu burada duruyordu. Tasarım v1.13.2 bunu kaldırdı: "tek proje derlemesi
/// şeritte tam koşudan ayırt edilmiyor, süreç de birebir aynı" (prototip otoritesi: <c>build-data.js</c>
/// <c>opLabel()</c> artık hedef adını yazmıyor). Hedefin kendisi konsolda ve grafta zaten bellidir; pill yalnız
/// "ne yapmıştım?" sorusuna cevap verir, "neyi" sorusuna değil. <c>Compose</c> kaldırıldı — üretim kodunda
/// zaten tek çağıranı YOKTU (satırdan tek-proje koşusunun arka ucu <see cref="Views.ProjectRowActions"/>'ta
/// henüz yazılmadığından hiçbir üretim yolu ona hedef geçirmiyordu).</para>
/// </summary>
public static class OperationLabel
{
    public const string Sync = "SYNC";
    public const string Build = "BUILD";
    public const string Rebuild = "REBUILD";
    /// <summary>Bakım kutusunun üçüncü ikonu (<c>RunMode.Cycles</c>). Prototiple aynı sözcük: <c>RESOLVE</c>.</summary>
    public const string Resolve = "RESOLVE";
    /// <summary>Build menüsündeki <i>Clean Solution</i> (yalnız <c>/t:Clean</c>).</summary>
    public const string Clean = "CLEAN";
    /// <summary>Bakım kutusundaki DERİN Clean (bin/obj + artifacts + NuGet cache) — <see cref="Clean"/> ile
    /// karıştırılmasın diye ayrı sözcük (prototip: <c>DEEP CLEAN</c>).</summary>
    public const string DeepClean = "DEEP CLEAN";
    public const string Optimize = "OPTIMIZE";

    /// <summary>Bir <c>RunMode</c>'un pill etiketi. Motor modu ile pill sözcüğü arasındaki TEK eşleme yeri —
    /// hedefli (satırdan tetiklenen) bir koşu da dahil, pill'in TEK üreticisi budur (v1.13.2).</summary>
    public static string ForRunMode(Contracts.Ipc.RunMode mode) => mode switch
    {
        Contracts.Ipc.RunMode.Rebuild => Rebuild,
        Contracts.Ipc.RunMode.Cycles => Resolve,
        _ => Build,
    };
}
