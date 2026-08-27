namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.11.0 §2.2 · §9-9] Sticky şeridin solundaki <b>kalıcı işlem pill'inin</b> metni — TEK kaynak.
///
/// <para>Pill "ne yapmıştım?" sorusunu tek bakışta bitirir: koşarken amber yanar, bitince nötrleşir ama
/// <b>bir sonraki işleme kadar KALIR</b>. Faz metni anlıktır (bir sonraki faz üzerine yazar), pill ise son
/// işlemin kimliğidir.</para>
///
/// <para>Etiketler caps ve mono'dur; hedefli işlemlerde (bir satırdan tetiklenen build/rebuild/clean) hedefin
/// kısa adı em-dash ile eklenir: <c>REBUILD — Sales.Core</c>.</para>
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

    /// <summary>Etiket + (varsa) hedefin kısa adı. Hedef boşsa yalnız etiket döner — sallantıda bir em-dash
    /// bırakmak design-v1'in "sakin, kesin" tonuna aykırıdır.</summary>
    public static string Compose(string label, string? target) =>
        string.IsNullOrEmpty(target) ? label : label + " — " + target;

    /// <summary>Bir <c>RunMode</c>'un pill etiketi. Motor modu ile pill sözcüğü arasındaki TEK eşleme yeri.</summary>
    public static string ForRunMode(Contracts.Ipc.RunMode mode) => mode switch
    {
        Contracts.Ipc.RunMode.Rebuild => Rebuild,
        Contracts.Ipc.RunMode.Cycles => Resolve,
        _ => Build,
    };
}
