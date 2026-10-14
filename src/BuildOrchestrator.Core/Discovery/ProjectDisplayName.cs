namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// [idle-memory · dalga 2 · R3 — kopya YASAK] Bir projenin görünen adının proje yolundan türeyen hâli — TEK tanım. Keşif projenin adını
/// <c>AssemblyName</c>'den alır (<see cref="CsprojEvaluator"/>); öğe yoksa ad proje dosyasının uzantısız adıdır — MSBuild'in kendi
/// varsayılanı da budur (<c>$(AssemblyName)</c> = <c>$(MSBuildProjectName)</c>). Adı bilinmeyen bir kimlik (planda, koşunun
/// düğümlerinde ya da uygulamanın satır listesinde yok) de bu adla anılır: plan düğümü, döngü/bağımlılık kökleri, motorun uyarı
/// satırları, uygulamanın satırları ve konsolu. Önceki aramalar (satır, topoloji, düğüm, ad haritası) çağıranındır; paylaşılan
/// yalnız bu geri dönüştür.
/// <para>Kapsam dışı: bir harici kökün etiketi (<c>ExternalWorkspaceResolver.DisplayName</c> — kök bir klasör ya da çözüm de
/// olabilir) ve bir projenin log dosyasının adı (<c>ProjectLogNaming</c>) — ikisi de başka bir şeyin adıdır.</para>
/// </summary>
public static class ProjectDisplayName
{
    /// <summary><paramref name="projectPath"/>'in uzantısız dosya adı (<c>C:\p\OSYS.Sales.Core.csproj</c> → <c>OSYS.Sales.Core</c>).</summary>
    public static string FromPath(string projectPath) => System.IO.Path.GetFileNameWithoutExtension(projectPath);
}
