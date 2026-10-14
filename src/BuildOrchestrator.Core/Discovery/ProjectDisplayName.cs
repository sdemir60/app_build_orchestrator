namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// [idle-memory · dalga 2 · R3 — kopya YASAK] Bir projenin görünen adının proje yolundan türeyen hâli — TEK tanım: adı bilinmeyen bir
/// kimlik (planda, koşunun düğümlerinde ya da uygulamanın satır listesinde yok) bu adla anılır — plan düğümü, döngü/bağımlılık kökleri,
/// motorun uyarı satırları, uygulamanın satırları ve konsolu. Önceki aramalar (satır, topoloji, düğüm, ad haritası) çağıranındır;
/// paylaşılan yalnız bu geri dönüştür.
/// <para>[DEĞİŞEN KAPSAM — dalga 2 son inceleme · defter 10] Keşfin <c>AssemblyName</c> varsayılanı bu tanımı KULLANMAZ: o derlemenin
/// kimliğidir ve MSBuild'in kuralını izler (<c>$(MSBuildProjectName)</c> — <see cref="CsprojEvaluator"/>'ın kendi adlı kuralı). Eski
/// metin varsayılanın bu tanımdan geldiğini söylüyordu; görünen adın kuralı değişirse derlemenin kimliği onunla kaymamalıdır.</para>
/// <para>Kapsam dışı: bir harici kökün etiketi (<c>ExternalWorkspaceResolver.DisplayName</c> — kök bir klasör ya da çözüm de
/// olabilir) ve bir projenin log dosyasının adı (<c>ProjectLogNaming</c>) — ikisi de başka bir şeyin adıdır.</para>
/// </summary>
public static class ProjectDisplayName
{
    /// <summary><paramref name="projectPath"/>'in uzantısız dosya adı (<c>C:\p\OSYS.Sales.Core.csproj</c> → <c>OSYS.Sales.Core</c>).</summary>
    public static string FromPath(string projectPath) => System.IO.Path.GetFileNameWithoutExtension(projectPath);
}
