<# [yayin hatti] Tek komutla yayin - /release skill'inin mekanik yarisi. Gunluk is develop'ta; main yalniz surumleri tasir
   ve ona yalniz bu script dokunur: main'deki her merge commit'i bir surum denemesidir (kullanici karari 2026-09-30). Tag'i bu
   script ATMAZ: main'e push GitHub'da release.yml'i baslatir, testler ve paket gecince tag'i orada release-tag.ps1 koyar
   (kullanici karari 2026-10-13) - tag yalniz testleri ve paketi gecmis surume konur; kirmizi bir test ya da dusen paket ne tag ne
   Release birakir, o deneme main'de tag'siz bir merge olarak kalir. Notu Claude yazmis olmali (CHANGELOG en ustte "## [X.Y.Z] - <bugun>"). Akis:
     guard'lar (hicbir yazmadan once) -> Version'i yazar -> build -> develop'ta "release: vX.Y.Z" commit'i ->
     main'e --no-ff merge ("merge: release vX.Y.Z") -> develop main'e ff (develop == main)
     -> tek atomik push: main + develop ya birlikte gider ya hicbiri.
   Guard'lar: CHANGELOG'un en ust bolumu bu surum ve bugun; branch develop; agacta CHANGELOG/props disinda degisiklik yok;
   budayan fetch sonrasi develop ve main origin'de var, develop == origin/develop, yerel main (varsa) == origin/main,
   origin/main develop'un atasi, main baska bir worktree'de acik degil; tag ne yerelde ne origin'de (surum yayinlanmamis);
   yayinlanacak bir sey var (develop main'in onunde ya da agacta yayinin degisikligi); bu checkout'tan calisan uygulama yok;
   develop HEAD'inin ci.yml kosusu yesil (completed + success). Sorulamayan ata, worktree listesi, origin ve CI kosusu (git ya
   da API hatasi) gecmis sayilmaz, durdurur.
   Testleri lokal KOSMAZ: develop HEAD'inin ci.yml kosusu yesil olmadan baslamaz (guard) ve release.yml main'in push'unu tag'den
   once yine test eder; kirmizi bir test orada durur - main origin'de kalir, tag ve Release olmaz; release-retry.yml kosuyu bir
   kez yeniden dener (SKILL adim 6).
   Build'de durursa hicbir sey commit/push edilmemistir. Release commit'inden sonra durursa (en olasi: push reddi - bu
   arada origin/develop ya da origin/main ilerledi) son satir yerel kurtarma komutlarini yazar. Push hata dondugunde once
   origin'in main'i sorulur: yayinin merge'uyse push uygulanmistir ve kurtarma basilmaz; origin okunamazsa kurtarma once
   origin'e bakmayi ister.
     release.ps1 -Version 1.8.0              (tam akis)
     release.ps1 -Version 1.8.0 -SkipCiCheck (develop'un CI kosusu sorulmaz - cevrimdisi/acil durum; uyari yazar)
     release.ps1 -Version 1.8.0 -DryRun      (butun guard'lar, sonra cikis; dosyaya ve branch'e dokunmaz) #>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$SkipCiCheck,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
Set-Location $RepoRoot
$tag = Get-VersionTag $Version
# Yazma evresinde (release commit'inden itibaren) dolar: o andan sonra duran adim yerelde commit/merge birakir ve Fail son
# satira onlari geri alan komutlari yazar. Sha'lar yayinin basladigi commit'lerdir; araya giren bir fetch onlari kaydirmaz.
$script:Undo = $null
function Fail([string]$why) {
    Write-Host "release: $why"
    if ($script:Undo) { Write-Host "release: undo the local release (commit, merge) with: $script:Undo" }
    exit 1
}
# Yazan git komutlari: sifirdan farkli cikis kodu script'i durdurur (yarim kalan bir merge sessizce gecmez).
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
# --prune: origin'de silinmis bir branch'in uzak izleme ref'i klonda kalsaydi asagidaki "origin'de var mi" guard'lari onu var sayar,
# yayin da silinmis branch'i origin'de yeniden yaratirdi.
Invoke-Git fetch origin --prune --quiet
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
try { $mainInDevelop = Test-GitAncestor $main $develop }
catch { Fail "cannot check origin/main against develop ($($_.Exception.Message))." }
if (-not $mainInDevelop) { Fail 'origin/main has commits develop does not have; merge main into develop first.' }
# Akis main'e gecer (git switch main); main baska bir worktree'de acikken bu, release commit'inden SONRA duserdi. Liste
# okunamazsa guard gecmez (dogrulanamayan yayin cikmaz - ata ve ls-remote guard'lari gibi).
$worktrees = @(git worktree list --porcelain)
if ($LASTEXITCODE -ne 0) { Fail "cannot check where main is checked out (git worktree list exit $LASTEXITCODE)." }
$worktree = $null
foreach ($line in $worktrees) {
    if ($line -like 'worktree *') { $worktree = $line.Substring(9) }
    elseif ($line -eq 'branch refs/heads/main') { Fail "main is checked out in $worktree; the release switches to main here - move that worktree off main first." }
}
# Tag'i GitHub testler ve paket gecince koyar: varsa bu surum yayina cikmistir (ya da yuklemesi yarim kalmistir - SKILL adim 6)
# ve numara bir daha kullanilmaz.
if (git tag --list $tag) { Fail "tag $tag already exists." }
# Uzak tag: fetch yalniz getirdigi tarihceye isaret eden tag'leri alir; origin'de erisilemeyen bir commit'e duran ayni ad
# yerelde gorunmez. ls-remote hatasi "tag yok" sayilmaz (dogrulanamayan yayin cikmaz).
try { $originRelease = Get-OriginRelease $tag }
catch { Fail "cannot read origin ($($_.Exception.Message)); cannot verify tag $tag on origin." }
if ($originRelease.TagCommit) { Fail "tag $tag already exists on origin." }
# Yayinlanacak bir sey: develop main'in onunde ya da agacta yayinin degisikligi (CHANGELOG/props). Ikisi de yoksa onceki deneme
# main'i zaten tasimistir (GitHub'da dustu, tag yok): commit, merge ve push bos gecer, GitHub degismeyen ref icin kosu acmaz -
# script "pushed" deyip hicbir sey baslatmazdi. Ayni kod icin yol kosuyu yeniden denemektir; bir duzeltme once develop'a girer.
if ($develop -eq $main -and -not (git status --porcelain)) { Fail "nothing to release: origin/main already has everything on develop. If the release workflow of $tag failed on GitHub, re-run its failed jobs there ($DefaultRepoUrl/actions); a fix goes to develop first." }
# Calisan uygulama (CLAUDE.md "uygulama acikken build alma"): Release build, BU checkout'tan calisan Supervisor'in kilitli
# binary'lerine carpar; tepsideki KURULU kopya baska klasordedir ve build'i kilitlemez - sonda yalniz repo kokunun altindaki
# kopyalari sayar (konumu okunamayan sayilir). Sonda Version yazilmadan ONCE kosar; sonradan dusen build, aciklanmasi gereken
# degismis bir Directory.Build.props birakirdi.
$running = @(Get-RunningApp -UnderPath $RepoRoot)
if ($running.Count -gt 0) { Fail "Build Orchestrator is running from this checkout or from a location that could not be read (pid $($running.Id -join ', ')); close it first (tray icon > Exit) - a copy running from this checkout keeps its binaries locked and the build would fail." }
# develop'un CI'i: yayin, CI'in yesil gordugu commit'ten cikar (release commit'i yalniz CHANGELOG + Version ekler; release.yml
# main'in push'unu tag'den once yine derleyip test eder). Kosu yoksa, bitmediyse ya da basarisizsa durur; sorulamazsa da
# (dogrulanamayan yayin cikmaz).
if ($SkipCiCheck) {
    Write-Host "release: -SkipCiCheck - develop's CI run was not checked; the release workflow still builds and tests main before it tags it."
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

# --- kapi: build (testler lokal kosmaz - CI'dadir: develop HEAD'inin ci.yml kosusu guard'da, main'inki release.yml'de)
& dotnet build BuildOrchestrator.slnx -c Release
if ($LASTEXITCODE -ne 0) { Fail 'build failed - nothing was committed or pushed.' }

# --- develop'ta release commit'i -> main'e merge -> develop = main -> atomik push (tag'i GitHub koyar: release.yml)
$script:Undo = "git switch develop; git reset --soft $develop; git restore --staged .; git branch -f main $main"
Invoke-Git add CHANGELOG.md Directory.Build.props
if (git status --porcelain) { Invoke-Git commit -q -m "release: $tag" }
if (-not $localMain) { Invoke-Git branch -q --track main origin/main }
Invoke-Git switch -q main
Invoke-Git merge -q --no-ff develop -m "merge: release $tag"
Invoke-Git switch -q develop
Invoke-Git merge -q --ff-only main
# --atomic: fetch ile push arasinda build dakikalar surebilir; origin/develop ya da origin/main bu arada ilerlerse o ref
# reddedilir ve digeri de gitmez (atomiksiz push reddedilmeyeni yine gonderirdi: main gider develop gitmez - yayin baslar,
# develop ile main ayrisir).
& git push --atomic origin main develop
$pushExit = $LASTEXITCODE
if ($pushExit -ne 0) {
    # "Hicbir sey gitmedi" origin'e bakilmadan soylenmez: sunucu atomik guncellemeyi uyguladiktan sonra baglanti koparsa git yine
    # hata doner. origin'in main'i yayinin merge'uyse push uygulanmistir (atomik: develop da gitti) ve yayin GitHub'da baslamistir;
    # kurtarma basilmaz (yerel develop/main'i geri alirdi, sonraki pull da agactaki degisiklige carpardi). origin okunamazsa
    # kurtarma basilir ama once origin'e bakilmasi istenir (ls-remote hatasi "gitmedi" sayilmaz).
    $released = git rev-parse refs/heads/main
    try { $origin = Get-OriginRelease $tag }
    catch { Fail "push failed (git exit $pushExit) and origin cannot be read to check it ($($_.Exception.Message)); run the undo below only after seeing origin's main is not $released - if it is, the release went out." }
    if ($origin.Main -eq $released) { $script:Undo = $null; Fail "push reported an error (git exit $pushExit) but origin's main is the release merge - the push applied; GitHub tests it and tags $tag ($DefaultRepoUrl/actions)." }
    Fail "push refused (git exit $pushExit); --atomic: nothing reached origin - did origin/develop or origin/main move meanwhile?"
}
$script:Undo = $null
Write-Host "release: $tag pushed (main and develop) - GitHub tests the merge, then tags it $tag and publishes the release: $DefaultRepoUrl/actions"
exit 0
