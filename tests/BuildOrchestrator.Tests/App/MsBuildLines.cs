namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [son temizlik · madde 7 · O2] MSBuild biçimli test satırlarının TEK evi (kopya YASAK): ölçümlerin gerçekçi uyarısı (<see cref="Warning"/>),
/// kompakt konsolun süzgeç, VM ve renklendirici testlerinin satır içi uyarı, hata ve komut satırları (<see cref="WarningLine"/>,
/// <see cref="ErrorLine"/>, <see cref="CommandLine"/>) ve anlatı bütçesinin girdisi (<see cref="BudgetWarning"/>). Eskiden üç üretici vardı:
/// bu sınıf (yalnız <see cref="Warning"/>), <c>MainWindowHost</c>'un satır kurucuları ve <c>NarrativeBudgetTests</c>'in kendi <c>Line</c>'ı.
/// </summary>
internal static class MsBuildLines
{
    /// <summary>[B2 fix 3 · madde 13] <paramref name="i"/>'inci gerçekçi uyarı satırı — <c>yol(satır,sütun): warning CS…: …</c> biçiminde,
    /// ~190 karakter (gerçek derleme satırları 100–300 karakterdir) ve konsolun renklendiricisinden "uyarı" olarak geçer (sınıflandırma, kod ve
    /// renk aralıkları). Ölçümlerin satır üreticisi: kısa <c>page{i}</c> satırları editörün satır kurma ve renklendirme maliyetini ölçmez. Yol,
    /// satır numarası ve proje 40 projeye, 300 dosyaya yayılır (satırlar birbirinin kopyası olmasın: görsel satır önbelleği tek bir metni
    /// yeniden kullanamasın).</summary>
    public static string Warning(int i) =>
        $"  D:\\repo\\src\\Orchestration.Customer{i % 40}\\PersonService{i % 300}.designer.cs({12 + i % 900},17): warning CS0618: "
        + "'LegacyGateway.ExecuteAsync(CancellationToken)' is obsolete: 'Use the pipeline API.' [Customer.csproj]";

    /// <summary>[idle-memory · dalga 2 · fix 1 · M5] MSBuild'in satır içi uyarı satırı (girintili, <c>&lt;dosya&gt;(&lt;satır&gt;,1): warning
    /// &lt;KOD&gt;: &lt;metin&gt;</c>) — her satır numarası ayrı, TEKİL bir uyarıdır. Kompakt konsolun süzgeç ve VM testlerinin satır kurucusu.</summary>
    public static string WarningLine(int line, string code = "CS1591", string text = "Missing XML comment") =>
        $"  C:\\r\\A.cs({line},1): warning {code}: {text}";

    /// <summary>[dalga 2 son inceleme · defter 9] MSBuild'in satır içi hata satırı (girintili, <c>&lt;dosya&gt;(&lt;satır&gt;,1): error
    /// CS0103: …</c>) — <see cref="WarningLine"/>'ın eşi: her satır numarası ayrı bir hatadır. Kompakt konsolun süzgeç, VM ve sınıflandırıcı
    /// testlerinin satır kurucusu.</summary>
    public static string ErrorLine(int line) => $"  C:\\r\\A.cs({line},1): error CS0103: The name 'x' does not exist";

    /// <summary>[dalga 2 son inceleme O5] Bir proje logunun ilk satırı: motorun yazdığı MSBuild komut satırı (tırnakla başlar — bilgi
    /// satırı, kompakt kipte gizlenir). Süzgeç ve VM testlerinin komut satırı girdisi.</summary>
    public const string CommandLine = "\"C:\\Program Files\\MSBuild.exe\" C:\\p\\A.csproj -t:Build -v:minimal";

    /// <summary>[idle-memory · spec §6.6] Anlatı bütçesinin girdisi (<c>NarrativeBudgetTests</c>): <paramref name="i"/>'inci ~100 karakterlik
    /// satır içi uyarı, 200 projeye yayılmış. METNİ DEĞİŞMEZ — bütçenin ölçtüğü sayılar (100 k satırın metni 22,93 MB ve yığının büyümesi) bu
    /// metinden gelir; gerçekçi uzun satıra (<see cref="Warning"/>) çevrilseydi sayılar kayardı. Bu yüzden ayrı bir addır.</summary>
    public static string BudgetWarning(int i) =>
        $"  D:\\repo\\Project{i % 200}\\File{i}.cs(12,5): warning CS1591: Missing XML comment for publicly visible type or member 'X{i}'";
}
