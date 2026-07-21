using System;
using System.IO;
using BuildOrchestrator.Core.Discovery;
using Xunit;

namespace BuildOrchestrator.Tests.Discovery;

public class WorkspaceScannerTests
{
    [Fact]
    public void Scan_finds_csproj_and_sln_and_skips_ignored_dirs()
    {
        // Geçici bir dizin ağacı kur: normal proje + ignore edilen klasörler (obj, .git).
        string root = Path.Combine(Path.GetTempPath(), "wsscan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "A"));
            Directory.CreateDirectory(Path.Combine(root, "obj"));      // ignore
            Directory.CreateDirectory(Path.Combine(root, ".git"));     // ignore
            File.WriteAllText(Path.Combine(root, "A", "A.csproj"), "<Project/>");
            File.WriteAllText(Path.Combine(root, "Root.sln"), "");
            File.WriteAllText(Path.Combine(root, "obj", "Ghost.csproj"), "<Project/>"); // elenmiş
            var result = new WorkspaceScanner().Scan(root);
            Assert.Single(result.CsprojPaths);
            Assert.EndsWith("A.csproj", result.CsprojPaths[0]);
            Assert.Single(result.SlnPaths);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Scan_excludes_transient_wpftmp_csproj_files()
    {
        // Canlı build sırasında WPF MarkupCompilePass proje klasöründe (obj DEĞİL) geçici
        // "<Ad>_<8hex>_wpftmp.csproj" üretip saniyeler içinde siler; scanner bunu kalıcı
        // proje sanıp almamalı — aksi halde EvaluationCache o dosyayı okumaya çalışırken
        // dosya çoktan silinmiş olabilir (FileNotFoundException, canlı build ↔ scan yarışı).
        string root = Path.Combine(Path.GetTempPath(), "wsscan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Foo.csproj"), "<Project/>");
            File.WriteAllText(Path.Combine(root, "Foo_a1b2c3d4_wpftmp.csproj"), "<Project/>");
            File.WriteAllText(Path.Combine(root, "Foo.csproj_wpftmp.csproj"), "<Project/>"); // varyant
            var result = new WorkspaceScanner().Scan(root);
            Assert.Single(result.CsprojPaths);
            Assert.EndsWith("Foo.csproj", result.CsprojPaths[0]);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
