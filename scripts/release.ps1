<# [yayin hatti] Tek komutla yayin - /release skill'inin mekanik yarisi. Notu Claude yazmis olmali (CHANGELOG en ustte
   "## [X.Y.Z] - <bugun>"); script: guard'lar -> Version'i yazar -> build + tam suit -> "release: vX.Y.Z" commit'i ->
   annotated tag -> push main + tag. Herhangi bir adimda durursa hicbir sey push edilmemistir.
     release.ps1 -Version 1.8.0            (tam akis)
     release.ps1 -Version 1.8.0 -SkipTests (suit lokalde zaten yesil gorulduyse)
     release.ps1 -Version 1.8.0 -DryRun    (yalniz guard'lar; git'e/dosyaya dokunmaz) #>
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$SkipTests,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
Set-Location $RepoRoot
function Fail([string]$why) { Write-Host "release: $why"; exit 1 }
# Yazan git komutlari: sifirdan farkli cikis kodu script'i durdurur (push edilmemis bir tag sessizce gecmez).
function Invoke-Git { & git @args; if ($LASTEXITCODE -ne 0) { Fail "git $($args -join ' ') failed (exit $LASTEXITCODE)." } }

# --- guard'lar (git'e dokunmadan)
$today = Get-Date -Format 'yyyy-MM-dd'
$headings = (Read-Changelog).Headings
if ($headings.Count -eq 0) { Fail 'CHANGELOG.md has no version section.' }
$top = $headings[0]
if ($top.Version -ne $Version) { Fail "CHANGELOG.md top section is $($top.Version); write the $Version section first (CHANGELOG.md)." }
if ($top.Date -ne $today) { Fail "CHANGELOG.md $Version is dated $($top.Date); a release is dated today ($today)." }
if ($DryRun) { Write-Host "release: guards passed for $Version (dry run)"; exit 0 }

if ((git branch --show-current) -ne 'main') { Fail 'not on main.' }
if (git status --porcelain | Where-Object { $_ -notmatch '^.. (Directory\.Build\.props|CHANGELOG\.md)$' }) { Fail 'working tree has changes besides CHANGELOG.md / Directory.Build.props.' }
Invoke-Git fetch origin --quiet
if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { Fail 'main and origin/main differ; sync first.' }
if (git tag --list "v$Version") { Fail "tag v$Version already exists." }

# --- Version tek yerde
$propsPath = Join-Path $RepoRoot 'Directory.Build.props'
$props = [System.IO.File]::ReadAllText($propsPath)
$updated = ([regex]'<Version>\d+\.\d+\.\d+</Version>').Replace($props, "<Version>$Version</Version>", 1)
if ($updated -ne $props) { [System.IO.File]::WriteAllText($propsPath, $updated, (New-Object System.Text.UTF8Encoding($false))) }
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'release-guard.ps1') -Tag "v$Version"
if ($LASTEXITCODE -ne 0) { exit 1 }

# --- kapi: build + tam suit (lokal kapi; CI de kosar)
& dotnet build BuildOrchestrator.slnx -c Release
if ($LASTEXITCODE -ne 0) { Fail 'build failed - nothing was committed or pushed.' }
if (-not $SkipTests) {
    & dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --no-build --filter 'Category!=Acceptance'
    if ($LASTEXITCODE -ne 0) { Fail 'tests failed - nothing was committed or pushed.' }
}

# --- commit + tag + push
Invoke-Git add CHANGELOG.md Directory.Build.props
if (git status --porcelain) { Invoke-Git commit -q -m "release: v$Version" }
Invoke-Git tag -a "v$Version" -m "$(Get-BuildProp 'Product') $Version - release notes in CHANGELOG.md"
Invoke-Git push origin main "v$Version"
Write-Host "release: v$Version pushed - $DefaultRepoUrl/actions"
exit 0
