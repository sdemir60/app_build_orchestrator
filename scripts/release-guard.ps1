<# [yayin hatti] Tag <-> Directory.Build.props Version <-> CHANGELOG en ust surum esitligi. release.ps1 push'tan
   once, release.yml ilk adim olarak calistirir. Cikis 0 = tutarli, 1 = uyumsuz (mesaj sebebi yazar). #>
param([Parameter(Mandatory = $true)][string]$Tag)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')

$version = Get-BuildProp 'Version'
$headings = (Read-Changelog).Headings
$topVersion = if ($headings.Count -gt 0) { $headings[0].Version } else { '' }
$problems = @()
if ($Tag -ne "v$version") { $problems += "tag '$Tag' does not match Directory.Build.props Version '$version' (expected 'v$version')" }
if ($topVersion -ne $version) { $problems += "CHANGELOG.md top section is '$topVersion', Directory.Build.props Version is '$version'" }
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "release guard: $_" }; exit 1 }
Write-Host "release guard: $Tag == Version == CHANGELOG top ($version)"
exit 0
