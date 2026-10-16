<# [yayin hatti] Surum tutarliligi: tag <-> Directory.Build.props Version <-> CHANGELOG en ust surum. Iki kip:
   -Tag vX.Y.Z (release.ps1, release commit'inden once): tag == "v" + Version == CHANGELOG en ust (Get-VersionProblems).
     origin'e sorulmaz.
   -OnMain (release.yml'in guard job'i): yayin kontrolu (Get-MainRelease, release-common.ps1 - release-tag.ps1 ayni kontrolu
     tag'den once yeniden yapar): tag Version'dan, Version == CHANGELOG, HEAD origin'in main ucu, tag yok ya da bu commit'te.
     Gecerse tag'i GITHUB_OUTPUT'a yazar ("tag=vX.Y.Z") - publish job'i onu kullanir.
   Cikis 0 = tutarli, 1 = uyumsuz (mesaj sebebi yazar). #>
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Local')][string]$Tag,
    [Parameter(Mandatory = $true, ParameterSetName = 'OnMain')][switch]$OnMain
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
Set-Location $RepoRoot

if ($OnMain) { $release = Get-MainRelease; $Tag = $release.Tag; $problems = $release.Problems }
else { $problems = @(Get-VersionProblems $Tag) }
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "release guard: $_" }; exit 1 }
$version = Get-BuildProp 'Version'
if ($OnMain) {
    # GitHub'in adim ciktisi; BOM'suz UTF-8 (PS 5.1'in Out-File -Encoding utf8'i BOM yazar ve ilk anahtar bozulurdu).
    if ($env:GITHUB_OUTPUT) { [System.IO.File]::AppendAllText($env:GITHUB_OUTPUT, "tag=$Tag`n") }
    Write-Host "release guard: $Tag == Version == CHANGELOG top ($version), the head of origin's main"
}
else { Write-Host "release guard: $Tag == Version == CHANGELOG top ($version)" }
exit 0
