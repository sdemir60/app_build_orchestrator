<# [yayin hatti] Surum tutarliligi: tag <-> Directory.Build.props Version <-> CHANGELOG en ust surum. Iki kip:
   -Tag vX.Y.Z (release.ps1, release commit'inden once): tag == "v" + Version == CHANGELOG en ust. origin'e sorulmaz.
   -OnMain (release.yml: guard job'i ve publish'teki release-tag.ps1): tag Version'dan cikar ("v" + Version); Version ==
     CHANGELOG en ust; HEAD (push edilen commit) origin'in main UCU olmali - push edilmemis, main'e girmemis ya da main'in
     gerisinde kalmis commit yayin cikarmaz (sonuncusu yeni bir surum itildikten sonra eski bir kosunun yeniden denenmesidir;
     gecseydi eski surum yenisinin ustune "latest" olurdu). Tag origin'de yoksa ya da bu commit'teyse (publish'i yeniden
     denenen kosu) gecer; baska bir commit'teyse durur (surum yukseltilmemis). origin okunamazsa durur (dogrulanamayan yayin
     cikmaz). Gecerse tag'i GITHUB_OUTPUT'a yazar ("tag=vX.Y.Z") - publish job'i onu kullanir. origin'e ls-remote ile sorulur
     (Get-OriginRelease): runner'in sig checkout'u tarihce tasimaz.
   Cikis 0 = tutarli, 1 = uyumsuz (mesaj sebebi yazar). #>
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Local')][string]$Tag,
    [Parameter(Mandatory = $true, ParameterSetName = 'OnMain')][switch]$OnMain
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
Set-Location $RepoRoot

$version = Get-BuildProp 'Version'
$headings = (Read-Changelog).Headings
$topVersion = if ($headings.Count -gt 0) { $headings[0].Version } else { '' }
$problems = @()
if ($OnMain) { $Tag = "v$version" }
elseif ($Tag -ne "v$version") { $problems += "tag '$Tag' does not match Directory.Build.props Version '$version' (expected 'v$version')" }
if ($topVersion -ne $version) { $problems += "CHANGELOG.md top section is '$topVersion', Directory.Build.props Version is '$version'" }
if ($OnMain) {
    $head = git rev-parse HEAD
    try {
        $origin = Get-OriginRelease $Tag
        if ($origin.Main -ne $head) { $problems += "this commit ($head) is not the head of origin's main ($($origin.Main)); a release publishes main's head only - if main moved on, release the newer version" }
        if ($origin.TagCommit -and $origin.TagCommit -ne $head) { $problems += "$Tag already exists on origin at $($origin.TagCommit); a new release needs a new version" }
    }
    catch { $problems += "cannot read origin ($($_.Exception.Message)); the release cannot be verified" }
}
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "release guard: $_" }; exit 1 }
if ($OnMain) {
    # GitHub'in adim ciktisi; BOM'suz UTF-8 (PS 5.1'in Out-File -Encoding utf8'i BOM yazar ve ilk anahtar bozulurdu).
    if ($env:GITHUB_OUTPUT) { [System.IO.File]::AppendAllText($env:GITHUB_OUTPUT, "tag=$Tag`n") }
    Write-Host "release guard: $Tag == Version == CHANGELOG top ($version), the head of origin's main"
}
else { Write-Host "release guard: $Tag == Version == CHANGELOG top ($version)" }
exit 0
