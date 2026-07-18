using System;
using System.Collections.Generic;
using System.IO;
using BuildOrchestrator.Core.Processes;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [T11] Ephemeral temp git repo fixture: gerçek <c>git.exe</c> ile izole bir temp dizinde
/// <c>init</c> + commit üretir (D8 — mock/sahte repo yok, gerçek git komutları). OSYS/gerçek repo'ya
/// ASLA dokunulmaz; her instance kendi temp kökünde çalışır ve <see cref="Dispose"/> ile silinir
/// (klonlanan ek dizinler dahil, best-effort — açık dosya tutan bir process kalırsa sessizce yutulur,
/// test assertion'larını etkilemez).
/// </summary>
public sealed class GitTestRepo : IDisposable
{
    private readonly List<string> _extraDirsToClean = [];

    public string RootPath { get; }

    public GitTestRepo()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "gitsvc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootPath);
        RunGit(RootPath, "init", "-q", ".");
        RunGit(RootPath, "config", "user.email", "test@buildorchestrator.local");
        RunGit(RootPath, "config", "user.name", "Build Orchestrator Test");
    }

    /// <summary>Dosya yazar (yoksa oluşturur, varsa üzerine yazar) — commit/dirty senaryoları için.</summary>
    public void WriteFile(string relativePath, string content)
    {
        string full = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    /// <summary>Tüm değişiklikleri stage'ler ve commit'ler; oluşan commit SHA'sını döner.</summary>
    public string CommitAll(string message)
    {
        RunGit(RootPath, "add", "-A");
        RunGit(RootPath, "commit", "-q", "-m", message);
        return RunGit(RootPath, "rev-parse", "HEAD").Trim();
    }

    public void Checkout(string refName) => RunGit(RootPath, "checkout", "-q", refName);

    /// <summary>
    /// <c>.git/config</c>'e bozuk bir satır ekleyerek repoyu gerçek bir "corrupted repo" durumuna sokar:
    /// git bundan sonra <c>rev-parse</c>/<c>symbolic-ref</c> gibi komutlarda exit=128 + "fatal: bad config
    /// line ..." ile başarısız olur — stderr'de "not a git repository" GEÇMEZ. Bu, gerçek 128-class bir
    /// git hatasının "not a git repository" alt-string eşleşmesine (yanlışlıkla) dayanan eski sınıflandırma
    /// mantığının, no-commits ile karıştırılıp yutulduğunu ispatlamak için kullanılır (deneysel doğrulandı).
    /// </summary>
    public void CorruptGitConfig()
        => File.AppendAllText(Path.Combine(RootPath, ".git", "config"), "\n[garbage syntax error !!! ===\n");

    public void CreateBranch(string name) => RunGit(RootPath, "branch", name);

    /// <summary>Şu an checkout edilmiş branch adı (fixture doğrulaması için — GitService sonucu buna karşı kıyaslanır).</summary>
    public string CurrentBranchName() => RunGit(RootPath, "symbolic-ref", "--short", "-q", "HEAD").Trim();

    /// <summary>
    /// Bu repoyu <c>git clone --depth 1</c> ile ayrı bir temp dizine klonlar ve klonun kökünü döner
    /// (shallow-repo edge testi). <c>file://</c> URI (<c>new Uri(RootPath).AbsoluteUri</c>) KASITLI
    /// kullanılıyor: düz yerel yol ile klonlarken git "--depth is ignored in local clones; use file://
    /// instead." uyarısı verip depth'i SESSİZCE YOK SAYAR (deneysel doğrulandı: <c>rev-parse
    /// --is-shallow-repository</c> düz yol klonunda "false" döner) — <c>file://</c> transport'u zorlayarak
    /// gerçek bir shallow clone (<c>.git/shallow</c> dosyası + <c>--is-shallow-repository</c> == "true")
    /// üretilir.
    /// </summary>
    public string CloneShallow()
    {
        string cloneRoot = Path.Combine(Path.GetTempPath(), "gitsvc-shallow-" + Guid.NewGuid().ToString("N"));
        string sourceUri = new Uri(RootPath).AbsoluteUri;
        RunGit(Path.GetTempPath(), "clone", "-q", "--depth", "1", sourceUri, cloneRoot);
        _extraDirsToClean.Add(cloneRoot);
        return cloneRoot;
    }

    /// <summary>Bu repoyu TAM (non-shallow) klonlar — remote-tracking branch listesi testi için (origin/*).</summary>
    public string CloneFull()
    {
        string cloneRoot = Path.Combine(Path.GetTempPath(), "gitsvc-clone-" + Guid.NewGuid().ToString("N"));
        string sourceUri = new Uri(RootPath).AbsoluteUri;
        RunGit(Path.GetTempPath(), "clone", "-q", sourceUri, cloneRoot);
        _extraDirsToClean.Add(cloneRoot);
        return cloneRoot;
    }

    private string RunGit(string workingDirectory, params string[] args) => RunGitAt(workingDirectory, args);

    /// <summary>
    /// Herhangi bir çalışma dizininde keyfi bir git komutu çalıştırır — bu bir <see cref="GitTestRepo"/>
    /// fixture'ına bağlı olmayan yardımcı (ör. <see cref="CloneFull"/>'un döndürdüğü klon dizininde remote
    /// URL'sini bozmak — Task 5 offline-degrade testi). Instance metodu <see cref="RunGit"/> bunu sarmalar.
    /// </summary>
    public static string RunGitAt(string workingDirectory, params string[] args)
    {
        var result = new ProcessRunner().RunAsync(new ProcessSpec("git", args, workingDirectory)).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', args)} (cwd={workingDirectory}) başarısız (exit {result.ExitCode}): {result.StandardError}");
        return result.StandardOutput;
    }

    public void Dispose()
    {
        TryDelete(RootPath);
        foreach (string dir in _extraDirsToClean) TryDelete(dir);
    }

    private static void TryDelete(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (IOException) { /* best-effort cleanup — açık handle kalmışsa test sonucu etkilenmez */ }
        catch (UnauthorizedAccessException) { /* aynı, ör. read-only bir dosya */ }
    }
}
