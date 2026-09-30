<# [yayin hatti] Tag <-> Directory.Build.props Version <-> CHANGELOG en ust surum esitligi. release.ps1 push'tan
   once, release.yml ilk adim olarak calistirir. Cikis 0 = tutarli, 1 = uyumsuz (mesaj sebebi yazar).
   -RequireOnMain (yalniz release.yml): HEAD - CI'da tag'in commit'i - origin/main'in atasi olmali; elle itilen ve
   main'e hic girmemis bir commit'e duran tag yayin cikarmaz - develop'a girmis ama main'e girmemis olan da (main yalniz
   surumleri tasir; release.ps1 tag'i main'deki merge commit'ine koyar). Checkout tam tarihce ister: fetch-depth 0.
   release.ps1 bu anahtari vermez: orada guard release commit'inden once kosar. #>
param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [switch]$RequireOnMain
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')

$version = Get-BuildProp 'Version'
$headings = (Read-Changelog).Headings
$topVersion = if ($headings.Count -gt 0) { $headings[0].Version } else { '' }
$problems = @()
if ($Tag -ne "v$version") { $problems += "tag '$Tag' does not match Directory.Build.props Version '$version' (expected 'v$version')" }
if ($topVersion -ne $version) { $problems += "CHANGELOG.md top section is '$topVersion', Directory.Build.props Version is '$version'" }
if ($RequireOnMain) {
    # Ata sorulamazsa (origin/main yok, sig checkout) da durur: dogrulanamayan yayin cikmaz (uc durum Test-GitAncestor'da).
    try { if (-not (Test-GitAncestor HEAD origin/main)) { $problems += "the tagged commit is not on origin/main (release tags sit only on main)" } }
    catch { $problems += "cannot check the tagged commit against origin/main ($($_.Exception.Message); is the history fetched?)" }
}
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "release guard: $_" }; exit 1 }
$onMain = if ($RequireOnMain) { ', on origin/main' } else { '' }
Write-Host "release guard: $Tag == Version == CHANGELOG top ($version)$onMain"
exit 0
