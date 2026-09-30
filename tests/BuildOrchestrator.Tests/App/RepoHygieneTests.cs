using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>[yayın hattı · Task 1, 5] Repo kökündeki dağıtım dosyaları: MIT lisansı (SignPath önkoşulu; public repo'da
/// lisanssız = hak verilmemiş), SDK bandını lokal ile CI'da eşitleyen global.json, vpk'yı pinleyen tool manifest ve
/// GitHub Actions workflow'ları (ci.yml build+test, release.yml tag ile yayın).</summary>
public class RepoHygieneTests
{
    [Fact]
    public void The_repository_carries_an_MIT_licence()
    {
        string text = File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, "LICENSE"));
        Assert.StartsWith("MIT License", text, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2026 Delta Yazılım", text, StringComparison.Ordinal);
        Assert.Contains("THE SOFTWARE IS PROVIDED \"AS IS\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void global_json_pins_the_sdk_band_and_rolls_forward_within_it()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, "global.json")));
        var sdk = doc.RootElement.GetProperty("sdk");
        Assert.StartsWith("10.0.", sdk.GetProperty("version").GetString(), StringComparison.Ordinal);
        Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
    }

    [Fact]
    public void The_tool_manifest_pins_vpk()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, ".config", "dotnet-tools.json")));
        var vpk = doc.RootElement.GetProperty("tools").GetProperty("vpk");
        Assert.Matches(@"^\d+\.\d+\.\d+$", vpk.GetProperty("version").GetString());
        Assert.Equal("vpk", vpk.GetProperty("commands")[0].GetString());
    }

    /// <summary>Workflow metni YAML yorumları atılmış olarak: iddialar yalnız GitHub'ın koşturduğu satırlara bakar — bir
    /// yorumdaki anahtar adı (ör. <c># ... -RequireOnMain ...</c>) adım silindiğinde testi yeşil tutmasın. Workflow'larda
    /// tırnak içinde <c>#</c> yoktur; satır başındaki ya da boşluktan sonra gelen <c>#</c> yorum başlatır.</summary>
    /// <summary>Workflow metni: YAML yorumları atılmış, satır sonları LF'e indirilmiş. Satır sonu normalizasyonu şart —
    /// runner checkout'u CRLF verir ve <c>(?m)…$</c> .NET'te yalnız <c>\n</c> öncesini yakalar; ilk CI koşusunda
    /// <see cref="CI_only_reads_the_repository"/> bu yüzden runner'da düşüp lokalde geçiyordu.</summary>
    private static string Workflow(string name) =>
        Regex.Replace(
            File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, ".github", "workflows", name)).Replace("\r\n", "\n"),
            @"(?m)(^|[ \t]+)#.*$", "");

    [Fact]
    public void CI_builds_and_tests_main_on_a_pinned_windows_image_and_is_callable_by_the_release()
    {
        string ci = Workflow("ci.yml");
        Assert.Contains("runs-on: windows-2025", ci, StringComparison.Ordinal);   // windows-latest sürüklenmez
        Assert.Contains("workflow_call:", ci, StringComparison.Ordinal);          // release.yml build+test'i buradan alır (kopya yok)
        Assert.Contains("global-json-file: global.json", ci, StringComparison.Ordinal);
        Assert.Contains("Category!=Acceptance&Category!=LocalOnly", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("vpk", ci, StringComparison.Ordinal);               // CI yayın yapmaz
    }

    /// <summary>Günlük iş <c>develop</c>'ta, <c>main</c> yalnız sürümleri taşır (kullanıcı kararı 2026-09-30): CI iki branch'in
    /// de her push'unu derleyip koşturur. <c>develop</c>'unki yayının ön koşuludur — <c>release.ps1</c>, develop HEAD'inin
    /// <c>ci.yml</c> koşusu yeşil değilse durur (<see cref="ReleaseScriptsTests"/>); <c>main</c>'inki yayınlanan kodun durumudur
    /// (README rozeti). Listenin sırası serbesttir; pull request'ler de koşar.</summary>
    [Fact]
    public void CI_runs_on_every_push_to_develop_and_main()
    {
        string ci = Workflow("ci.yml");
        var push = Regex.Match(ci, @"(?m)^[ \t]+push:[ \t]*\n[ \t]+branches:[ \t]*\[(?<list>[^\]\n]*)\]");
        Assert.True(push.Success, "ci.yml: push tetikleyicisinin branches listesi bulunamadı");
        var branches = push.Groups["list"].Value.Split(',').Select(b => b.Trim().Trim('\'', '"')).ToHashSet();
        Assert.Contains("develop", branches);
        Assert.Contains("main", branches);
        Assert.Contains("pull_request:", ci, StringComparison.Ordinal);
    }

    /// <summary>CI repoya yazmaz. Kendi <c>push</c>/<c>pull_request</c> koşularında token izni repo ayarının varsayılanından
    /// gelir (yazma olabilir); <c>release.yml</c>'den çağrıldığında çağıranın <c>contents: read</c>'iyle kesişir. Workflow
    /// seviyesinde açık <c>contents: read</c> ikisini de sabitler. Satır başındaki (girintisiz) <c>permissions:</c> workflow
    /// seviyesidir — bir job'ın bloğu girintilidir ve bu deseni karşılamaz.</summary>
    [Fact]
    public void CI_only_reads_the_repository()
    {
        string ci = Workflow("ci.yml");
        Assert.Matches(@"(?m)^permissions:[ \t]*\r?\n[ \t]+contents: read[ \t]*$", ci);
        Assert.DoesNotMatch(@"(?m):[ \t]*write[ \t]*$", ci); // hiçbir kapsam yazma izni istemez
    }

    [Fact]
    public void The_release_workflow_runs_on_version_tags_reuses_CI_and_publishes_through_the_package_script()
    {
        string release = Workflow("release.yml");
        Assert.Contains("tags: ['v*']", release, StringComparison.Ordinal);
        Assert.Contains("contents: write", release, StringComparison.Ordinal);
        Assert.Contains("uses: ./.github/workflows/ci.yml", release, StringComparison.Ordinal);
        Assert.Contains("scripts/release-guard.ps1", release, StringComparison.Ordinal);
        Assert.Contains("scripts/package.ps1", release, StringComparison.Ordinal);
        Assert.Contains("vpk upload github", release, StringComparison.Ordinal);
        Assert.Contains("gh release edit", release, StringComparison.Ordinal);     // gövde = CHANGELOG bölümü
        Assert.DoesNotContain("dotnet publish", release, StringComparison.Ordinal); // publish komutunun tek sahibi package.ps1
        // Not, Velopack'in çıktı klasörünün (artifacts/velopack) DIŞINDA: vpk o klasörün sahibidir ve dosya yayın açıldıktan SONRA okunur.
        Assert.Matches(@"--notes-file artifacts/notes\.md\b", release);
    }

    /// <summary>İki tag arka arkaya itilirse iki yayın aynı anda koşmaz (ikincisinin delta'sı birincinin henüz yüklenmemiş
    /// paketine dayanırdı) ve koşan yayın yenisi yüzünden iptal edilmez (yarım yüklenmiş bir release kalırdı).</summary>
    [Fact]
    public void Releases_run_one_at_a_time_and_a_running_one_is_never_cancelled()
    {
        string release = Workflow("release.yml");
        Assert.Contains("group: release", release, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", release, StringComparison.Ordinal);
    }

    /// <summary>Yazma izni yalnız yayımlayan job'dadır: guard ve ci (tag'in kodunu derleyip koşturur) yalnız okur.
    /// Workflow seviyesi <c>contents: read</c>; <c>contents: write</c> tek yerde, son job olan publish'in içinde.</summary>
    [Fact]
    public void Only_the_publish_job_may_write_to_the_repository()
    {
        string release = Workflow("release.yml");
        int jobs = release.IndexOf("\njobs:", StringComparison.Ordinal);
        int publish = release.IndexOf("\n  publish:", StringComparison.Ordinal);
        Assert.True(jobs > 0 && publish > jobs, "release.yml: 'jobs:' ya da publish job'ı bulunamadı");
        Assert.Contains("contents: read", release[..jobs], StringComparison.Ordinal);
        int write = release.IndexOf("contents: write", StringComparison.Ordinal);
        Assert.True(write > publish, "contents: write publish job'ının dışında");
        Assert.Equal(write, release.LastIndexOf("contents: write", StringComparison.Ordinal)); // tek yer
    }

    /// <summary>Guard job'ı tag'in commit'inin origin/main'de olduğunu da ister — elle itilen, main'e hiç girmemiş bir
    /// commit'e duran tag yayın çıkarmaz: tam tarihçe (origin/main'e ata sorulabilsin) + <c>-RequireOnMain</c>. Kuralın
    /// davranışı <see cref="ReleaseScriptsTests"/>'te gerçek git sandbox'ıyla pinlenir. Anahtar guard script'ini çağıran
    /// <c>run:</c> satırında aranır (yorumlar <see cref="Workflow"/>'da atılır): eski hali job metninde düz
    /// <c>Contains("-RequireOnMain")</c> idi ve <c>fetch-depth</c> satırının yorumu anahtar adını taşıdığı için anahtar
    /// run satırından silindiğinde de yeşil kalıyordu.</summary>
    [Fact]
    public void The_release_guard_job_checks_that_the_tag_is_on_main()
    {
        string release = Workflow("release.yml");
        int guard = release.IndexOf("\n  guard:", StringComparison.Ordinal);
        int ci = release.IndexOf("\n  ci:", StringComparison.Ordinal);
        Assert.True(guard > 0 && ci > guard, "release.yml: guard ya da ci job'ı bulunamadı");
        string guardJob = release[guard..ci];
        Assert.Contains("fetch-depth: 0", guardJob, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s*- run: .*scripts/release-guard\.ps1 .*-RequireOnMain\b", guardJob);
    }
}
