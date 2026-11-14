using System;
using System.IO;
using System.Linq;
using BuildOrchestrator.Core.MsBuild;
using Xunit;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [files-in-use · Task 2] Bir kopya komutunun hedefteki dosyaları (<see cref="CopyTargets.Resolve"/>), o dosyanın hedefe
/// ulaşıp ulaşmadığı (<see cref="CopyTargets.Landed"/>: <c>cmd</c>'nin kopyası kaynağın değiştirilme zamanını korur; ulaşan
/// kopya kaynakla aynı boyutta ve tam aynı zamandadır) ve yazmak için açılıp açılamadığı (<see cref="FileLock.Probe"/>).
/// Görüntü olarak yüklenmiş bir DLL'in kilidini — üzerine yazılamaz ama yeniden adlandırılabilir — silme paylaşan, yazma
/// paylaşmayan bir tutamaç birebir taklit eder (<c>FileShare.Read | FileShare.Delete</c>).
/// </summary>
[Trait("Category", "MsBuild")]
public sealed class CopyTargetsTests
{
    private static string WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static (string Source, string Destination) Folders(TempDir temp)
    {
        string source = Path.Combine(temp.Path, "src");
        string destination = Path.Combine(temp.Path, "dst");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        return (source, destination);
    }

    [Fact]
    public void A_wildcard_source_names_each_file_it_matches_in_the_destination_folder()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);
        foreach (string ext in new[] { "dll", "pdb", "xml" }) WriteFile(Path.Combine(src, "Acme.Types." + ext), ext);
        WriteFile(Path.Combine(src, "Other.dll"), "other");

        var targets = CopyTargets.Resolve([new CopyCommand([Path.Combine(src, "Acme.Types.*")], dst + "\\")], temp.Path);

        Assert.Equal(["Acme.Types.dll", "Acme.Types.pdb", "Acme.Types.xml"], targets.Select(t => Path.GetFileName(t.Destination)).Order());
        Assert.All(targets, t => Assert.Equal(Path.Combine(dst, Path.GetFileName(t.Source)), t.Destination));
    }

    [Fact]
    public void A_destination_file_the_command_does_not_copy_is_not_a_target_even_when_it_shares_the_prefix()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);
        WriteFile(Path.Combine(src, "Acme.Types.dll"), "types");
        WriteFile(Path.Combine(dst, "Acme.Types.Common.dll"), "another project's output");

        var targets = CopyTargets.Resolve([new CopyCommand([Path.Combine(src, "Acme.Types.*")], dst)], temp.Path);

        var target = Assert.Single(targets);
        Assert.Equal(Path.Combine(dst, "Acme.Types.dll"), target.Destination);
    }

    [Fact]
    public void A_destination_file_path_with_one_source_file_is_itself_the_target()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);
        string source = WriteFile(Path.Combine(src, "x.dll"), "x");

        var targets = CopyTargets.Resolve([new CopyCommand([source], Path.Combine(dst, "renamed.dll"))], temp.Path);

        Assert.Equal([new CopyTarget(source, Path.Combine(dst, "renamed.dll"))], targets);
    }

    [Fact]
    public void A_missing_source_or_a_folder_source_names_no_target()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);

        var targets = CopyTargets.Resolve(
        [
            new CopyCommand([Path.Combine(src, "missing.dll")], dst),
            new CopyCommand([Path.Combine(src, "nothing.*")], dst),
            new CopyCommand([src], dst),
        ], temp.Path);

        Assert.Empty(targets);
    }

    [Fact]
    public void A_target_two_commands_share_is_named_once()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);
        string source = WriteFile(Path.Combine(src, "x.dll"), "x");

        var targets = CopyTargets.Resolve(
            [new CopyCommand([source], dst), new CopyCommand([Path.Combine(src, "x.*")], dst.ToUpperInvariant() + "\\")], temp.Path);

        Assert.Single(targets);
    }

    [Fact]
    public void A_relative_path_resolves_against_the_working_directory_and_a_variable_expands()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);
        string source = WriteFile(Path.Combine(src, "x.dll"), "x");
        string variable = "ACME_FIU_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, dst);
        try
        {
            var targets = CopyTargets.Resolve([new CopyCommand([@"src\x.dll"], $"%{variable}%\\")], temp.Path);

            Assert.Equal([new CopyTarget(source, Path.Combine(dst, "x.dll"))], targets);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void An_unknown_variable_names_no_target()
    {
        using var temp = new TempDir();
        var (src, _) = Folders(temp);
        string source = WriteFile(Path.Combine(src, "x.dll"), "x");

        Assert.Empty(CopyTargets.Resolve([new CopyCommand([source], "%ACME_FIU_NOT_SET_ANYWHERE%\\")], temp.Path));
    }

    [Fact]
    public void A_copy_that_kept_the_source_time_and_length_has_landed()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);
        string source = WriteFile(Path.Combine(src, "x.dll"), "built");
        string destination = Path.Combine(dst, "x.dll");
        File.Copy(source, destination);

        Assert.True(CopyTargets.Landed(new CopyTarget(source, destination)));
    }

    [Fact]
    public void A_target_of_another_length_has_not_landed()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);
        string source = WriteFile(Path.Combine(src, "x.dll"), "new build");
        string destination = WriteFile(Path.Combine(dst, "x.dll"), "old");
        File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));

        Assert.False(CopyTargets.Landed(new CopyTarget(source, destination)));
    }

    /// <summary>
    /// <para><b>[DEĞİŞEN KURAL — kabul testi bulgusu 2026-11-14]</b> Eski iddia (<c>…_more_than_two_seconds_off_…_and_one_within_has</c>):
    /// kaynaktan iki saniye içinde kalan hedef ulaşmış sayılırdı (öğrenilen beslenen çıktıların toleransı, ARCHITECTURE §7.6). Ölçüm: arka
    /// arkaya iki derleme aynı boyutta DLL üretti (3584 bayt) ve aralarında 0,35 sn vardı — kilit yüzünden yerinde kalan ESKİ kopya ulaşmış
    /// sayıldı ve karar hiç verilmedi (<see cref="Integration.FilesInUseAcceptanceTests"/> kırmızıydı). <c>cmd</c>'nin kopyası değiştirilme
    /// zamanını TAM korur; ulaşan kopya kaynakla aynı zamandadır. Tolerans gereksizdir: zamanı yuvarlayan bir hedefte (FAT) ulaşmış bir kopya
    /// "ulaşmamış" okunur ve yalnız bir kilit yoklamasına mal olur — kilit yoksa hiçbir şey taşınmaz.</para>
    /// </summary>
    [Fact]
    public void A_target_off_its_source_by_any_time_has_not_landed_and_only_the_same_time_has()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);
        string source = WriteFile(Path.Combine(src, "x.dll"), "same");
        string destination = WriteFile(Path.Combine(dst, "x.dll"), "same");
        var built = File.GetLastWriteTimeUtc(source);

        File.SetLastWriteTimeUtc(destination, built - TimeSpan.FromSeconds(3));
        Assert.False(CopyTargets.Landed(new CopyTarget(source, destination)));

        File.SetLastWriteTimeUtc(destination, built - TimeSpan.FromMilliseconds(350));
        Assert.False(CopyTargets.Landed(new CopyTarget(source, destination)));

        File.SetLastWriteTimeUtc(destination, built);
        Assert.True(CopyTargets.Landed(new CopyTarget(source, destination)));
    }

    [Fact]
    public void A_missing_target_has_not_landed_and_a_missing_source_has_nothing_to_judge()
    {
        using var temp = new TempDir();
        var (src, dst) = Folders(temp);
        string source = WriteFile(Path.Combine(src, "x.dll"), "x");

        Assert.False(CopyTargets.Landed(new CopyTarget(source, Path.Combine(dst, "x.dll"))));
        Assert.True(CopyTargets.Landed(new CopyTarget(Path.Combine(src, "gone.dll"), Path.Combine(dst, "gone.dll"))));
    }

    [Fact]
    public void A_free_file_probes_free_and_a_missing_one_missing()
    {
        using var temp = new TempDir();
        string path = WriteFile(Path.Combine(temp.Path, "x.dll"), "x");

        Assert.Equal(FileLockState.Free, FileLock.Probe(path));
        Assert.Equal(FileLockState.Missing, FileLock.Probe(Path.Combine(temp.Path, "absent.dll")));
        Assert.Equal(FileLockState.Missing, FileLock.Probe(Path.Combine(temp.Path, "no-folder", "absent.dll")));
    }

    [Fact]
    public void A_file_held_like_a_loaded_dll_probes_locked_and_the_probe_changes_nothing()
    {
        using var temp = new TempDir();
        string path = WriteFile(Path.Combine(temp.Path, "x.dll"), "loaded");
        var before = File.GetLastWriteTimeUtc(path);

        using (PostBuildCopy.HoldLikeALoadedDll(path))
            Assert.Equal(FileLockState.Locked, FileLock.Probe(path));

        Assert.Equal(FileLockState.Free, FileLock.Probe(path));
        Assert.Equal("loaded", File.ReadAllText(path));
        Assert.Equal(before, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void A_read_only_file_probes_denied_not_locked()
    {
        using var temp = new TempDir();
        string path = WriteFile(Path.Combine(temp.Path, "x.dll"), "x");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal(FileLockState.Denied, FileLock.Probe(path));
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }
}
