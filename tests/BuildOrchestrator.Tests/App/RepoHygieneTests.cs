using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BuildOrchestrator.Tests.App;

/// <summary>[yayın hattı · Task 1, 5] Repo kökündeki dağıtım dosyaları: MIT lisansı (SignPath önkoşulu; public repo'da
/// lisanssız = hak verilmemiş), SDK bandını lokal ile CI'da eşitleyen global.json, vpk'yı pinleyen tool manifest ve
/// GitHub Actions workflow'ları (ci.yml build+test, release.yml main'e push ile yayın, release-retry.yml bir kez yeniden deneme).</summary>
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

    /// <summary>Workflow metni: YAML yorumları atılmış, satır sonları LF'e indirilmiş. Yorumlar atılır ki iddialar yalnız
    /// GitHub'ın koşturduğu satırlara baksın — bir yorumdaki anahtar adı (ör. <c># ... -OnMain ...</c>) adım silindiğinde testi
    /// yeşil tutmasın; workflow'larda tırnak içinde <c>#</c> yoktur, satır başındaki ya da boşluktan sonra gelen <c>#</c> yorum
    /// başlatır. Satır sonu normalizasyonu şart — runner checkout'u CRLF verir ve <c>(?m)…$</c> .NET'te yalnız <c>\n</c> öncesini
    /// yakalar; ilk CI koşusunda <see cref="CI_only_reads_the_repository"/> bu yüzden runner'da düşüp lokalde geçiyordu.</summary>
    private static string Workflow(string name) =>
        Regex.Replace(
            File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, ".github", "workflows", name)).Replace("\r\n", "\n"),
            @"(?m)(^|[ \t]+)#.*$", "");

    [Fact]
    public void CI_builds_and_tests_on_a_pinned_windows_image_and_is_callable_by_the_release()
    {
        string ci = Workflow("ci.yml");
        Assert.Contains("runs-on: windows-2025", ci, StringComparison.Ordinal);   // windows-latest sürüklenmez
        Assert.Contains("workflow_call:", ci, StringComparison.Ordinal);          // release.yml build+test'i buradan alır (kopya yok)
        Assert.Contains("global-json-file: global.json", ci, StringComparison.Ordinal);
        Assert.Contains("Category!=Acceptance&Category!=LocalOnly", ci, StringComparison.Ordinal);
        Assert.DoesNotContain("vpk", ci, StringComparison.Ordinal);               // CI yayın yapmaz
    }

    /// <summary>Workflow'un <c>push:</c> tetikleyicisindeki branch listesi (sırası serbest).</summary>
    private static HashSet<string> PushBranches(string workflow, string name)
    {
        var push = Regex.Match(workflow, @"(?m)^[ \t]+push:[ \t]*\n[ \t]+branches:[ \t]*\[(?<list>[^\]\n]*)\]");
        Assert.True(push.Success, $"{name}: push tetikleyicisinin branches listesi bulunamadı");
        return push.Groups["list"].Value.Split(',').Select(b => b.Trim().Trim('\'', '"')).ToHashSet();
    }

    /// <summary>Günlük iş <c>develop</c>'ta, <c>main</c> yalnız sürümleri taşır (kullanıcı kararı 2026-09-30): CI develop'un her
    /// push'unu derleyip koşturur — yayının ön koşuludur, <c>release.ps1</c> develop HEAD'inin <c>ci.yml</c> koşusu yeşil değilse
    /// durur (<see cref="ReleaseScriptsTests"/>). <c>main</c>'e push yayını (<c>release.yml</c>) başlatır; main'in build ve testi
    /// onun <c>ci</c> job'ıdır (<c>workflow_call</c> ile buradan) — ci.yml main'de AYRICA koşmaz. Pull request'ler de koşar.
    /// Tetikleyicide yol filtresi (<c>paths</c> / <c>paths-ignore</c>) YOKTUR: release.ps1 develop'un HER ucunda bir koşu bekler —
    /// yalnız doküman değiştiren bir develop ucu koşu almasaydı sonuç "not green (no run)" olur, yayın kilitlenirdi.
    /// <para><b>Değişen kural (kullanıcı kararı 2026-10-13: tag'i GitHub, testler geçince koyar):</b> eski iddia "CI develop'un ve
    /// main'in her push'unu koşar" idi. Bir yayın push'u main'de iki ayrı test koşusu doğuruyordu (ci ve release'in ci'ı); ölçüm
    /// (2026-10-12): aynı commit (<c>cc49ff58</c>) main'in ci koşusunda kırmızı, release'inkinde yeşil çıktı — yayın çıktı, rozet
    /// kırmızı kaldı. main'in tek test koşusu artık yayının kendi koşusudur.</para></summary>
    [Fact]
    public void CI_runs_on_every_push_to_develop_and_leaves_main_to_the_release()
    {
        string ci = Workflow("ci.yml");
        var branches = PushBranches(ci, "ci.yml");
        Assert.Contains("develop", branches);
        Assert.DoesNotContain("main", branches);
        Assert.Contains("pull_request:", ci, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?m)^[ \t]+paths(-ignore)?:", ci); // her develop ucu koşu alır
    }

    /// <summary>Çağrılan workflow'un üst düzey <c>concurrency</c>'si ÇAĞIRANIN bağlamında değerlendirilir: release'in ci job'ı
    /// <c>github.ref</c> = <c>refs/heads/main</c> ile koşar. Grup yalnız ref'ten kurulsaydı (<c>ci-${{ github.ref }}</c>) main'de
    /// elle tetiklenen bir ci koşusu ("Run workflow" varsayılan branch'i, main'i önerir) aynı gruba düşer ve
    /// <c>cancel-in-progress</c> yayının testlerini iptal ederdi; iptal edilen koşu <c>cancelled</c> biter, release-retry yalnız
    /// <c>failure</c>'da yeniden dener — ne tag ne Release olur, kimse uyarılmaz. Grup workflow adını da taşır: reusable çağrıda
    /// <c>github.workflow</c> çağıranın adıdır (release), elle ya da push'la koşan ci'da "ci". develop'a art arda gelen push'larda
    /// eskisi yine iptal edilir (aynı grup).</summary>
    [Fact]
    public void A_manual_CI_run_never_cancels_the_tests_of_a_release()
    {
        string ci = Workflow("ci.yml");
        Assert.Matches(@"(?m)^concurrency:[ \t]*\n[ \t]+group: ci-\$\{\{ github\.workflow \}\}-\$\{\{ github\.ref \}\}[ \t]*\n[ \t]+cancel-in-progress: true[ \t]*$", ci);
    }

    /// <summary>README'nin rozeti <c>main</c>'in yayın koşusunu (<c>release.yml</c>) gösterir — yayınlanan kodun build ve test
    /// durumu. Parametresiz rozet GitHub'ın varsayılan branch'ini gösterir; varsayılan branch <c>develop</c>'a çevrilirse rozet
    /// sessizce develop'un durumunu göstermeye başlardı. Bu yüzden rozet <c>?branch=main</c> taşır. ci.yml rozeti YOKTUR: ci main'de
    /// koşmaz, main'li bir ci rozeti son eski koşuda donup kalırdı.
    /// <para><b>Değişen kural (kullanıcı kararı 2026-10-13):</b> eski rozet ci.yml'in main koşusuydu; main'in testi artık yayının
    /// kendi koşusudur (bkz. <see cref="CI_runs_on_every_push_to_develop_and_leaves_main_to_the_release"/>).</para></summary>
    [Fact]
    public void The_README_badge_shows_the_release_run_of_main()
    {
        string readme = File.ReadAllText(Path.Combine(RepoPaths.RepoRoot, "README.md"));
        var badges = Regex.Matches(readme, @"actions/workflows/release\.yml/badge\.svg(?<query>[^)\s]*)");
        Assert.NotEmpty(badges);
        Assert.All(badges, badge => Assert.Equal("?branch=main", badge.Groups["query"].Value));
        Assert.DoesNotMatch(@"actions/workflows/ci\.yml/badge\.svg", readme);
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

    /// <summary>Yayın <c>main</c>'e push'la başlar (main'i yalnız release.ps1 iter; her push bir sürüm denemesidir), tag'le DEĞİL:
    /// tag'i workflow'un kendisi, testler ve paket geçtikten sonra koyar (<c>release-tag.ps1</c>,
    /// <see cref="The_tag_comes_after_the_tests_and_the_package_and_before_the_upload"/>). Build ve test ci.yml'den gelir (kopya
    /// yok); paketleme package.ps1'den.
    /// <para><b>Değişen kural (kullanıcı kararı 2026-10-13):</b> eski tetik <c>tags: ['v*']</c> idi — tag release.ps1'den testten
    /// ÖNCE gelirdi ve kırmızı bir koşu origin'de Release'siz bir tag, yanmış bir sürüm numarası bırakırdı.</para></summary>
    [Fact]
    public void The_release_workflow_runs_on_pushes_to_main_reuses_CI_and_publishes_through_the_scripts()
    {
        string release = Workflow("release.yml");
        Assert.Equal(new[] { "main" }, PushBranches(release, "release.yml"));
        Assert.DoesNotMatch(@"(?m)^[ \t]+tags:", release);                       // tag tetiklemez
        Assert.Contains("contents: write", release, StringComparison.Ordinal);
        Assert.Contains("uses: ./.github/workflows/ci.yml", release, StringComparison.Ordinal);
        Assert.Contains("scripts/release-guard.ps1", release, StringComparison.Ordinal);
        Assert.Contains("scripts/release-tag.ps1", release, StringComparison.Ordinal);
        Assert.Contains("scripts/package.ps1", release, StringComparison.Ordinal);
        Assert.Contains("vpk upload github", release, StringComparison.Ordinal);
        Assert.Contains("gh release edit", release, StringComparison.Ordinal);     // gövde = CHANGELOG bölümü
        Assert.DoesNotContain("dotnet publish", release, StringComparison.Ordinal); // publish komutunun tek sahibi package.ps1
        // Not, Velopack'in çıktı klasörünün (artifacts/velopack) DIŞINDA: vpk o klasörün sahibidir ve dosya yayın açıldıktan SONRA okunur.
        Assert.Matches(@"--notes-file artifacts/notes\.md\b", release);
    }

    /// <summary>İki sürüm arka arkaya itilirse iki yayın aynı anda koşmaz (ikincisinin delta'sı birincinin henüz yüklenmemiş
    /// paketine dayanırdı) ve koşan yayın yenisi yüzünden iptal edilmez (yarım yüklenmiş bir release kalırdı).</summary>
    [Fact]
    public void Releases_run_one_at_a_time_and_a_running_one_is_never_cancelled()
    {
        string release = Workflow("release.yml");
        Assert.Contains("group: release", release, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", release, StringComparison.Ordinal);
    }

    /// <summary>Yazma izni yalnız yayımlayan job'dadır (tag ve Release): guard ve ci (main'in kodunu derleyip koşturur) yalnız
    /// okur. Workflow seviyesi <c>contents: read</c>; <c>contents: write</c> tek yerde, son job olan publish'in içinde.</summary>
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

    /// <summary>Guard job'ı CI kipinde koşar (<c>-OnMain</c>): Version == CHANGELOG, push edilen commit origin'in main ucu, tag başka
    /// bir commit'te değil — davranışı <see cref="ReleaseScriptsTests"/>'te gerçek git sandbox'ıyla pinlenir. Tag'i adımın çıktısı
    /// olarak job çıktısına verir (<c>outputs.tag</c>); publish job'ı yükleme ve not adımlarında onu kullanır. Anahtar guard
    /// script'ini çağıran <c>run:</c> satırında aranır (yorumlar <see cref="Workflow"/>'da atılır): yorumdaki bir anahtar adı, run
    /// satırından silinen anahtarı yeşil tutmasın.
    /// <para><b>Değişen kural (kullanıcı kararı 2026-10-13: tag'i GitHub, testler geçince koyar):</b> eski guard tag'le tetiklenen
    /// koşuda tag'in commit'inin origin/main'in atası olmasını isterdi (<c>-RequireOnMain</c> + <c>fetch-depth: 0</c>). Artık tag
    /// guard'dan sonra konur; origin'e <c>ls-remote</c> ile sorulduğu için tam tarihçe gerekmez.</para></summary>
    [Fact]
    public void The_release_guard_job_checks_the_head_of_main_and_hands_the_tag_on()
    {
        string release = Workflow("release.yml");
        int guard = release.IndexOf("\n  guard:", StringComparison.Ordinal);
        int ci = release.IndexOf("\n  ci:", StringComparison.Ordinal);
        Assert.True(guard > 0 && ci > guard, "release.yml: guard ya da ci job'ı bulunamadı");
        string guardJob = release[guard..ci];
        Assert.Matches(@"(?m)^\s*run: .*scripts/release-guard\.ps1 -OnMain[ \t]*$", guardJob);
        Assert.Matches(@"(?m)^\s*- id: guard[ \t]*$", guardJob);
        Assert.Matches(@"(?m)^\s*tag: \$\{\{ steps\.guard\.outputs\.tag \}\}[ \t]*$", guardJob);
    }

    /// <summary>Tag, yayının geri dönülmez adımıdır ve testlerle paketten SONRA gelir: publish job'ı ci'a bağlıdır, sırası
    /// package.ps1 → release-tag.ps1 → <c>vpk upload</c> → <c>gh release edit</c>. Kırmızı bir test ya da düşen paketleme tag
    /// bırakmaz (sürüm numarası yanmaz); tag'in yükleme ve notla eşleşmesi guard'ın verdiği aynı çıktıdan gelir — ref adından değil
    /// (tetik artık main'dir; <c>github.ref_name</c> "main" olurdu). Tag adımı script'in tek satırıdır: tag'in kimliği ve mesajı
    /// script'in işidir (<see cref="ReleaseScriptsTests"/> pinler), workflow'da ayrıca git ayarı yapılmaz.</summary>
    [Fact]
    public void The_tag_comes_after_the_tests_and_the_package_and_before_the_upload()
    {
        string release = Workflow("release.yml");
        int publish = release.IndexOf("\n  publish:", StringComparison.Ordinal);
        Assert.True(publish > 0, "release.yml: publish job'ı bulunamadı");
        string job = release[publish..];
        Assert.Matches(@"(?m)^\s+needs: \[guard, ci\][ \t]*$", job);
        int package = job.IndexOf("scripts/package.ps1", StringComparison.Ordinal);
        int tag = job.IndexOf("scripts/release-tag.ps1", StringComparison.Ordinal);
        int upload = job.IndexOf("vpk upload github", StringComparison.Ordinal);
        int notes = job.IndexOf("gh release edit", StringComparison.Ordinal);
        Assert.True(package > 0 && tag > package && upload > tag && notes > upload,
            $"publish sırası package → tag → upload → notes değil ({package}, {tag}, {upload}, {notes})");
        Assert.Matches(@"(?m)^\s*- run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release-tag\.ps1[ \t]*$", job);
        Assert.DoesNotContain("git config", job, StringComparison.Ordinal);
        Assert.Contains("--tag ${{ needs.guard.outputs.tag }}", job, StringComparison.Ordinal);
        Assert.Contains("gh release edit ${{ needs.guard.outputs.tag }}", job, StringComparison.Ordinal);
        Assert.DoesNotContain("github.ref_name", release, StringComparison.Ordinal);
    }

    /// <summary>Kırmızı biten yayın BİR kez kendiliğinden yeniden denenir — yalnız düşen job'lar ("Re-run failed jobs"): runner'da
    /// tesadüfen düşen bir test yayını durdurmasın. İkinci deneme de kırmızıysa durur; gerçek hata insan ister. Workflow yalnız
    /// koşuyu yeniden başlatma iznini alır (<c>actions: write</c>), repoya yazmaz. Yayın workflow'unu ADIYLA bulur
    /// (<c>workflows: [release]</c>): release.yml'in adı değişirse tekrar sessizce ölürdü — ad da burada pinlenir.</summary>
    [Fact]
    public void A_failed_release_is_retried_once_on_its_failed_jobs()
    {
        Assert.Matches(@"(?m)^name: release[ \t]*$", Workflow("release.yml"));
        string retry = Workflow("release-retry.yml");
        Assert.Matches(@"(?m)^[ \t]+workflow_run:[ \t]*\n[ \t]+workflows:[ \t]*\[release\][ \t]*\n[ \t]+types:[ \t]*\[completed\][ \t]*$", retry);
        Assert.Contains("github.event.workflow_run.conclusion == 'failure'", retry, StringComparison.Ordinal);
        Assert.Contains("github.event.workflow_run.run_attempt == 1", retry, StringComparison.Ordinal);
        Assert.Matches(@"(?m)^\s*(- )?run: gh run rerun \$\{\{ github\.event\.workflow_run\.id \}\} --failed\b", retry);
        Assert.Matches(@"(?m)^permissions:[ \t]*\n[ \t]+actions: write[ \t]*$", retry);
        Assert.DoesNotMatch(@"(?m)^[ \t]+contents:", retry);
    }
}
