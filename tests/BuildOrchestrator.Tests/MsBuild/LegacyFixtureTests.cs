using System.IO;

namespace BuildOrchestrator.Tests.MsBuild;

/// <summary>
/// [ilk CI koşusu] <see cref="LegacyFixture"/>'ın hedef framework seçimi: sabit v4.6, targeting pack'i olmayan makinede
/// (CI runner imajı Windows 2025 + VS 2026: 4.6.2 ve üstü var, 4.6 yok) fixture'ı MSB3644 ile düşürüyor ve gerçek
/// MSBuild'e dayanan altı testi kırmızı bırakıyordu. Seçim saf bir işlevdir; makinedeki klasör listesi girdidir.
/// </summary>
public class LegacyFixtureTests
{
    [Fact]
    public void Prefers_v46_when_its_targeting_pack_is_installed()
    {
        // Geliştirici makinesi: OSYS'in legacy projeleri gibi v4.6 — davranış eskisiyle aynı.
        Assert.Equal("v4.6", LegacyFixture.ChooseTargetFramework(["v4.0", "v4.5.2", "v4.6", "v4.6.2", "v4.8"]));
        Assert.Equal("v4.6", LegacyFixture.ChooseTargetFramework(["V4.6"]));
    }

    [Fact]
    public void Falls_back_to_the_newest_installed_4x_pack_when_v46_is_missing()
    {
        // CI runner: 4.6.2, 4.7, 4.7.1, 4.7.2, 4.8, 4.8.1 (+ sürüm olmayan v4.X klasörü) → en yeni.
        Assert.Equal("v4.8.1", LegacyFixture.ChooseTargetFramework(["v4.6.2", "v4.7", "v4.7.1", "v4.7.2", "v4.8", "v4.8.1", "v4.X"]));
        Assert.Equal("v4.7.2", LegacyFixture.ChooseTargetFramework(["v3.5", "v4.7.2", "v4.6.2"]));
    }

    [Fact]
    public void Without_any_4x_pack_it_keeps_v46_so_msbuild_fails_loudly()
    {
        Assert.Equal("v4.6", LegacyFixture.ChooseTargetFramework([]));
        Assert.Equal("v4.6", LegacyFixture.ChooseTargetFramework(["v3.5", "v4.X"]));
    }

    [Fact]
    public void Only_folders_with_a_framework_list_count_as_installed_packs()
    {
        // İkinci CI koşusu: runner'da bir v4.6 klasörü var ama pack değil (FrameworkList.xml yok) → seçim ona düşüp
        // MSB3644 veriyordu. Ölçüt MSBuild'inkidir: RedistList\FrameworkList.xml.
        string root = Path.Combine(Path.GetTempPath(), "ref-asm-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "v4.6"));                       // çıplak klasör
            Directory.CreateDirectory(Path.Combine(root, "v4.X"));                       // XML doc'lar, pack değil
            Directory.CreateDirectory(Path.Combine(root, "v4.8", "RedistList"));
            File.WriteAllText(Path.Combine(root, "v4.8", LegacyFixture.FrameworkListRelativePath), "<FileList />");
            Directory.CreateDirectory(Path.Combine(root, "v4.7.2", "RedistList"));
            File.WriteAllText(Path.Combine(root, "v4.7.2", LegacyFixture.FrameworkListRelativePath), "<FileList />");

            var packs = LegacyFixture.InstalledTargetingPacksUnder(root).OrderBy(p => p, StringComparer.Ordinal).ToList();
            Assert.Equal(["v4.7.2", "v4.8"], packs);
            Assert.Equal("v4.8", LegacyFixture.ChooseTargetFramework(packs));
            Assert.Empty(LegacyFixture.InstalledTargetingPacksUnder(Path.Combine(root, "missing")));
            Assert.Contains("v4.6 (no FrameworkList)", DescribeUnder(root), StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }

        static string DescribeUnder(string root) => string.Join(", ",
            Directory.EnumerateDirectories(root).Select(d => Path.GetFileName(d)!
                + (File.Exists(Path.Combine(d, LegacyFixture.FrameworkListRelativePath)) ? "" : " (no FrameworkList)")));
    }

    [Fact]
    public void The_generated_project_carries_the_chosen_framework()
    {
        string dir = Path.Combine(Path.GetTempPath(), "legacy-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            string csproj = LegacyFixture.CreateClassLib(dir, "Pick");
            Assert.Contains($"<TargetFrameworkVersion>{LegacyFixture.TargetFrameworkVersion}</TargetFrameworkVersion>",
                File.ReadAllText(csproj), StringComparison.Ordinal);
            Assert.StartsWith("v4.", LegacyFixture.TargetFrameworkVersion, StringComparison.Ordinal);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
