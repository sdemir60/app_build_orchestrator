namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [B2 fix 3 · madde 13] Gerçekçi bir MSBuild tanı satırı — <c>yol(satır,sütun): warning CS…: …</c> biçiminde, ~190 karakter (gerçek derleme
/// satırları 100–300 karakterdir) ve konsolun renklendiricisinden "uyarı" olarak geçer (sınıflandırma, kod ve renk aralıkları). Ölçümlerin TEK
/// satır üreticisi: kısa <c>page{i}</c> satırları editörün satır kurma ve renklendirme maliyetini ölçmez.
/// </summary>
internal static class MsBuildLines
{
    /// <summary><paramref name="i"/>'inci uyarı satırı — yol, satır numarası ve proje 40 projeye, 300 dosyaya yayılır (satırlar birbirinin
    /// kopyası olmasın: görsel satır önbelleği tek bir metni yeniden kullanamasın).</summary>
    public static string Warning(int i) =>
        $"  D:\\repo\\src\\Orchestration.Customer{i % 40}\\PersonService{i % 300}.designer.cs({12 + i % 900},17): warning CS0618: "
        + "'LegacyGateway.ExecuteAsync(CancellationToken)' is obsolete: 'Use the pipeline API.' [Customer.csproj]";
}
