using BuildOrchestrator.Core.Paths;

namespace BuildOrchestrator.Core.Git;

/// <summary>
/// [worktree] <c>git worktree list --porcelain -z</c> çıktısının saf okuyucusu — hangi çalışma kopyası hangi branch'i tutuyor. Git'i
/// çağıran <see cref="BranchSwitcher"/>'dır (karar sorgusu yazımla aynı dosyada durur, <c>merge-base</c>'in emsali); ayrıştırma burada,
/// git'siz sınanır.
///
/// <para><b>Biçim:</b> <c>-z</c> ile her alan NUL ile biter ve hiçbir yol tırnaklanmaz — boşluklu ya da Latin dışı harfli yol olduğu gibi
/// gelir. Bir kopyanın bloğu <c>worktree &lt;yol&gt;</c> ile başlar ve boş bir alanla biter; branch'i <c>branch refs/heads/&lt;ad&gt;</c>
/// alanındadır. Detached ve bare kopyada branch alanı yoktur; <c>locked</c>/<c>prunable</c> alanları nedenleriyle araya girebilir. Git yolları
/// Windows'ta da <c>/</c> ile yazar.</para>
/// </summary>
public static class WorktreeList
{
    private const string WorktreeField = "worktree ";
    private const string BranchField = "branch refs/heads/";

    /// <summary><paramref name="branch"/>'i HEAD'inde tutan, <paramref name="self"/> dışındaki kopyanın kökü (Windows ayraçlarıyla); yoksa
    /// <c>null</c>. Kendi kopyası yol bileşeni üzerinden tanınır (<see cref="RootScope.NormalizeRoot"/> — büyük/küçük harf ve sondaki ayraç
    /// farkı aynı kopyadır).</summary>
    public static string? HolderOf(string porcelain, string self, string branch)
    {
        string? selfRoot = RootScope.NormalizeRoot(self);
        string? copy = null;
        foreach (string field in porcelain.Split('\0'))
        {
            if (field.Length == 0) { copy = null; continue; } // blok sonu
            if (field.StartsWith(WorktreeField, StringComparison.Ordinal)) { copy = field[WorktreeField.Length..]; continue; }
            if (copy is null || !field.StartsWith(BranchField, StringComparison.Ordinal)) continue;
            if (!string.Equals(field[BranchField.Length..], branch, StringComparison.Ordinal)) continue;
            if (RootScope.NormalizeRoot(copy) is { } root && string.Equals(root, selfRoot, StringComparison.OrdinalIgnoreCase)) continue;
            return Display(copy);
        }
        return null;
    }

    /// <summary>Konsolun göstereceği biçim: Windows ayraçları, sondaki ayraç yok; çözülemeyen yol olduğu gibi.</summary>
    private static string Display(string path) =>
        RootScope.NormalizeRoot(path) is { } root ? Path.TrimEndingDirectorySeparator(root) : path;
}
