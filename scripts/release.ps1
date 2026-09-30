<# [yayin hatti] Tek komutla yayin - /release skill'inin mekanik yarisi. Gunluk is develop'ta; main yalniz surumleri tasir
   ve ona yalniz bu script dokunur: main'deki her merge commit'i bir surum + tag'tir (kullanici karari 2026-09-30). Notu
   Claude yazmis olmali (CHANGELOG en ustte "## [X.Y.Z] - <bugun>"). Akis:
     guard'lar (hicbir yazmadan once) -> Version'i yazar -> build + tam suit -> develop'ta "release: vX.Y.Z" commit'i ->
     main'e --no-ff merge ("merge: release vX.Y.Z") -> merge commit'ine annotated tag -> develop main'e ff (develop == main)
     -> tek atomik push: main + develop + tag ya birlikte gider ya hicbiri.
   Guard'lar: CHANGELOG'un en ust bolumu bu surum ve bugun; branch develop; agacta CHANGELOG/props disinda degisiklik yok;
   fetch sonrasi develop == origin/develop, yerel main (varsa) == origin/main, origin/main develop'un atasi, main baska bir
   worktree'de acik degil; tag ne yerelde ne origin'de; bu checkout'tan calisan uygulama yok; develop HEAD'inin ci.yml
   kosusu yesil (completed + success).
   Build/test'te durursa hicbir sey commit/push edilmemistir. Release commit'inden sonra durursa (en olasi: push reddi - bu
   arada origin/develop ya da origin/main ilerledi) origin'e hicbir sey gitmemistir; son satir yerel kurtarma komutlarini yazar.
     release.ps1 -Version 1.8.0              (tam akis)
     release.ps1 -Version 1.8.0 -SkipTests   (suit lokalde zaten yesil gorulduyse)
     release.ps1 -Version 1.8.0 -SkipCiCheck (develop'un CI kosusu sorulmaz - cevrimdisi/acil durum; uyari yazar)
     release.ps1 -Version 1.8.0 -DryRun      (butun guard'lar, sonra cikis; dosyaya, branch'e, tag'e dokunmaz) #>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$SkipTests,
    [switch]$SkipCiCheck,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
Set-Location $RepoRoot
$tag = "v$Version"
# Yazma evresinde (release commit'inden itibaren) dolar: o andan sonra duran adim yerelde commit/merge/tag birakir ve Fail son
# satira onlari geri alan komutlari yazar. Sha'lar yayinin basladigi commit'lerdir; araya giren bir fetch onlari kaydirmaz.
$script:Undo = $null
function Fail([string]$why) {
    Write-Host "release: $why"
    if ($script:Undo) { Write-Host "release: undo the local release (commit, merge, tag) with: $script:Undo" }
    exit 1
}
# Yazan git komutlari: sifirdan farkli cikis kodu script'i durdurur (push edilmemis bir tag sessizce gecmez).
function Invoke-Git { & git @args; if ($LASTEXITCODE -ne 0) { Fail "git $($args -join ' ') failed (exit $LASTEXITCODE)." } }

# --- guard'lar: hicbir yazmadan once. -DryRun hepsini kosar ve yazmadan cikar (tek yazdigi fetch'in uzak izleme ref'leri).
$today = Get-Date -Format 'yyyy-MM-dd'
$headings = (Read-Changelog).Headings
if ($headings.Count -eq 0) { Fail 'CHANGELOG.md has no version section.' }
$top = $headings[0]
if ($top.Version -ne $Version) { Fail "CHANGELOG.md top section is $($top.Version); write the $Version section first (CHANGELOG.md)." }
if ($top.Date -ne $today) { Fail "CHANGELOG.md $Version is dated $($top.Date); a release is dated today ($today)." }

if ((git branch --show-current) -ne 'develop') { Fail 'release runs from develop (daily work is on develop; main only carries releases).' }
if (git status --porcelain | Where-Object { $_ -notmatch '^.. (Directory\.Build\.props|CHANGELOG\.md)$' }) { Fail 'working tree has changes besides CHANGELOG.md / Directory.Build.props.' }
Invoke-Git fetch origin --quiet
$develop = git rev-parse HEAD
$originDevelop = git rev-parse --verify --quiet refs/remotes/origin/develop
if (-not $originDevelop) { Fail 'origin has no develop branch; push develop first.' }
if ($develop -ne $originDevelop) { Fail 'develop and origin/develop differ; push or pull develop first.' }
$main = git rev-parse --verify --quiet refs/remotes/origin/main
if (-not $main) { Fail 'origin has no main branch.' }
# Yerel main yoksa yazma evresi onu origin/main'i izleyen branch olarak acar; varsa yalniz yayinla ilerlemis olmali.
$localMain = git rev-parse --verify --quiet refs/heads/main
if ($localMain -and $localMain -ne $main) { Fail 'main and origin/main differ; main moves only by a release - reset it (git branch -f main origin/main).' }
# main develop'un tamamini alir, fazlasini degil: origin/main develop'un atasi degilse merge, build'in hic gormedigi bir agac
# uretirdi (catisirsa main'de yarim bir merge kalirdi).
& git merge-base --is-ancestor $main $develop
switch ($LASTEXITCODE) {
    0 { }
    1 { Fail 'origin/main has commits develop does not have; merge main into develop first.' }
    default { Fail "cannot check origin/main against develop (git exit $LASTEXITCODE)." }
}
# Akis main'e gecer (git switch main); main baska bir worktree'de acikken bu, release commit'inden SONRA duserdi.
$worktree = $null
foreach ($line in @(git worktree list --porcelain)) {
    if ($line -like 'worktree *') { $worktree = $line.Substring(9) }
    elseif ($line -eq 'branch refs/heads/main') { Fail "main is checked out in $worktree; the release switches to main here - move that worktree off main first." }
}
if (git tag --list $tag) { Fail "tag $tag already exists." }
# Uzak tag: fetch yalniz getirdigi tarihceye isaret eden tag'leri alir; origin'de erisilemeyen bir commit'e duran ayni ad
# yerelde gorunmez ve push'ta reddedilirdi. ls-remote hatasi "tag yok" sayilmaz (dogrulanamayan yayin cikmaz).
$remoteTag = & git ls-remote --tags origin "refs/tags/$tag"
if ($LASTEXITCODE -ne 0) { Fail "git ls-remote failed (exit $LASTEXITCODE); cannot verify tag $tag on origin." }
if ($remoteTag) { Fail "tag $tag already exists on origin." }
# Calisan uygulama (CLAUDE.md "uygulama acikken build alma"): Release build, BU checkout'tan calisan Supervisor'in kilitli
# binary'lerine carpar; tepsideki KURULU kopya baska klasordedir ve build'i kilitlemez - sonda yalniz repo kokunun altindaki
# kopyalari sayar (konumu okunamayan sayilir). Sonda Version yazilmadan ONCE kosar; sonradan dusen build, aciklanmasi gereken
# degismis bir Directory.Build.props birakirdi.
$running = @(Get-RunningApp -UnderPath $RepoRoot)
if ($running.Count -gt 0) { Fail "Build Orchestrator is running from this checkout or from a location that could not be read (pid $($running.Id -join ', ')); close it first (tray icon > Exit) - a copy running from this checkout keeps its binaries locked and the build would fail." }
# develop'un CI'i: yayin, CI'in yesil gordugu commit'ten cikar (release commit'i yalniz CHANGELOG + Version ekler; release.yml
# tag'i yine derleyip test eder). Kosu yoksa, bitmediyse ya da basarisizsa durur; sorulamazsa da (dogrulanamayan yayin cikmaz).
if ($SkipCiCheck) {
    Write-Host "release: -SkipCiCheck - develop's CI run was not checked; the release workflow still builds and tests the tag."
}
else {
    try { $ci = Get-CiConclusion -RepoUrl $DefaultRepoUrl -Sha $develop }
    catch { Fail "cannot read develop's CI run ($($_.Exception.Message)); check the network, or -SkipCiCheck." }
    $state = if (-not $ci) { 'no run' } elseif ($ci.Status -ne 'completed') { $ci.Status } else { $ci.Conclusion }
    if ($state -ne 'success') { Fail "develop's CI run is not green ($state); push develop and wait for ci, or -SkipCiCheck." }
}
if ($DryRun) { Write-Host "release: guards passed for $Version (dry run)"; exit 0 }

# --- Version tek yerde
$propsPath = Join-Path $RepoRoot 'Directory.Build.props'
$props = [System.IO.File]::ReadAllText($propsPath)
$updated = ([regex]'<Version>\d+\.\d+\.\d+</Version>').Replace($props, "<Version>$Version</Version>", 1)
if ($updated -ne $props) { [System.IO.File]::WriteAllText($propsPath, $updated, (New-Object System.Text.UTF8Encoding($false))) }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'release-guard.ps1') -Tag $tag
if ($LASTEXITCODE -ne 0) { exit 1 }

# --- kapi: build + tam suit (lokal kapi; CI de kosar)
& dotnet build BuildOrchestrator.slnx -c Release
if ($LASTEXITCODE -ne 0) { Fail 'build failed - nothing was committed or pushed.' }
if (-not $SkipTests) {
    & dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --no-build --filter 'Category!=Acceptance'
    if ($LASTEXITCODE -ne 0) { Fail 'tests failed - nothing was committed or pushed.' }
}

# --- develop'ta release commit'i -> main'e merge -> tag -> develop = main -> atomik push
$script:Undo = "git switch develop; git tag -d $tag; git reset --soft $develop; git restore --staged .; git branch -f main $main"
Invoke-Git add CHANGELOG.md Directory.Build.props
if (git status --porcelain) { Invoke-Git commit -q -m "release: $tag" }
if (-not $localMain) { Invoke-Git branch -q --track main origin/main }
Invoke-Git switch -q main
Invoke-Git merge -q --no-ff develop -m "merge: release $tag"
Invoke-Git tag -a $tag -m "$(Get-BuildProp 'Product') $Version - release notes in CHANGELOG.md"
Invoke-Git switch -q develop
Invoke-Git merge -q --ff-only main
# --atomic: fetch ile push arasinda build + tam suit dakikalar surer; origin/develop ya da origin/main bu arada ilerlerse o ref
# reddedilir ve digerleri de gitmez (atomiksiz push reddedilmeyenleri yine gonderirdi: main + tag gider develop gitmez - yayin
# cikar, develop ile main ayrisir).
& git push --atomic origin main develop $tag
if ($LASTEXITCODE -ne 0) { Fail "push refused (git exit $LASTEXITCODE); --atomic: nothing reached origin - did origin/develop or origin/main move meanwhile?" }
$script:Undo = $null
Write-Host "release: $tag pushed (main, develop and the tag) - $DefaultRepoUrl/actions"
exit 0
