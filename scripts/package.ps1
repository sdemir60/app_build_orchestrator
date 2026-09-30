<#
 [yayin hatti] Publish + surum notu kesimi + Velopack paketlemenin TEK sahibi. Lokal deneme ve release.yml AYNI
 script'i calistirir; publish komutu baska hicbir yerde yazilmaz (README/ARCHITECTURE buraya isaret eder,
 verify-publish.ps1 -PublishOnly ile cagirir).
   package.ps1                          -> notes + publish + vpk pack  (artifacts\velopack\)
   package.ps1 -PublishOnly -PublishDir X
   package.ps1 -NotesOnly -NotesVersion 1.8.0 -NotesOut notes.md
   package.ps1 -DownloadPrevious -RepoUrl ... -Token ...   (delta icin onceki paketi ceker; release yoksa atlar)
 -WhatIf hicbir seyi yazmaz/calistirmaz (download, publish, notes, pack atlanir); testler bunu kullanir.
 Surum, Product ve Company Directory.Build.props'tan okunur (release-common.ps1), elle yazilmaz.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$Configuration = 'Release',
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$ArtifactsDir,
    [string]$PublishDir,
    [switch]$PublishOnly,
    [switch]$NotesOnly,
    [string]$NotesVersion,
    [string]$NotesOut,
    [switch]$DownloadPrevious,
    [string]$RepoUrl,
    [string]$Token,
    [int]$ReleaseCount = -1
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')

$Version = Get-BuildProp 'Version'
$Product = Get-BuildProp 'Product'
$Company = Get-BuildProp 'Company'
if (-not $RepoUrl) { $RepoUrl = $DefaultRepoUrl }
if (-not $ArtifactsDir) { $ArtifactsDir = Join-Path $RepoRoot 'artifacts' }
if (-not $PublishDir) { $PublishDir = Join-Path $ArtifactsDir 'publish' }
$ReleasesDir = Join-Path $ArtifactsDir 'velopack'
$appProj = Join-Path $RepoRoot 'src\BuildOrchestrator.App\BuildOrchestrator.App.csproj'

function Get-ChangelogSection([string]$Wanted) {
    # "## [x.y.z] - yyyy-MM-dd" basligindan bir sonraki "## " basligina kadar (baslik dahil) - App'in parser'i
    # (ReleaseNotes.Parse) bu bicimi tek bolum olarak okur.
    $log = Read-Changelog
    $heading = $log.Headings | Where-Object { $_.Version -eq $Wanted } | Select-Object -First 1
    if (-not $heading) { throw "CHANGELOG.md has no section for version $Wanted." }
    $end = $heading.Line + 1
    while ($end -lt $log.Lines.Length -and -not $log.Lines[$end].StartsWith('## ')) { $end++ }
    $section = New-Object System.Collections.Generic.List[string]
    $section.AddRange([string[]]$log.Lines[$heading.Line..($end - 1)])
    while ($section.Count -gt 0 -and $section[$section.Count - 1].Trim() -eq '') { $section.RemoveAt($section.Count - 1) }
    return ($section -join "`r`n") + "`r`n"
}

function Write-ReleaseNotes([string]$Out, [string]$ForVersion) {
    New-Item -ItemType Directory -Force (Split-Path $Out -Parent) | Out-Null
    [System.IO.File]::WriteAllText($Out, (Get-ChangelogSection $ForVersion), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "notes -> $Out"
}

if ($NotesOnly) {
    if (-not $NotesVersion) { $NotesVersion = $Version }
    if (-not $NotesOut) { $NotesOut = Join-Path $ReleasesDir 'notes.md' }
    if ($PSCmdlet.ShouldProcess($NotesOut, 'write release notes')) { Write-ReleaseNotes $NotesOut $NotesVersion }
    exit 0
}

# Not bolumu paketlemeden ONCE kesilir: CHANGELOG'da bu surum yoksa dakikalar suren publish'e girilmez.
$notes = Join-Path $ReleasesDir 'notes.md'
if (-not $PublishOnly -and $PSCmdlet.ShouldProcess($notes, 'write release notes')) { Write-ReleaseNotes $notes $Version }

if ($DownloadPrevious) {
    if ($ReleaseCount -lt 0) { $ReleaseCount = Get-ReleaseCount $RepoUrl $Token }
    if ($ReleaseCount -eq 0) {
        Write-Host 'no previous release - delta skipped (first release)'
    }
    elseif ($PSCmdlet.ShouldProcess($RepoUrl, 'vpk download github')) {
        $dl = @('vpk', 'download', 'github', '--repoUrl', $RepoUrl, '--outputDir', $ReleasesDir)
        if ($Token) { $dl += @('--token', $Token) }
        & dotnet @dl
        if ($LASTEXITCODE -ne 0) { throw "vpk download github failed (exit $LASTEXITCODE)." }
    }
}

if ($PSCmdlet.ShouldProcess($PublishDir, 'dotnet publish')) {
    if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
    & dotnet publish $appProj -c $Configuration -r $RuntimeIdentifier --self-contained false -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }
}
if ($PublishOnly) { exit 0 }

# --shortcuts verilmez: Velopack varsayilani (Desktop + Start Menu) gecerlidir.
if ($PSCmdlet.ShouldProcess("$Product $Version", 'vpk pack')) {
    $pack = @(
        'vpk', 'pack',
        '--packId', 'BuildOrchestrator.App',
        '--packVersion', $Version,
        '--packDir', $PublishDir,
        '--mainExe', 'BuildOrchestrator.App.exe',
        '--packTitle', $Product,
        '--packAuthors', $Company,
        '--icon', (Join-Path $RepoRoot 'src\BuildOrchestrator.App\Assets\app-icon.ico'),
        '--framework', 'net10.0-x64-desktop',
        '--releaseNotes', $notes,
        '--noPortable',
        '--outputDir', $ReleasesDir
    )
    & dotnet @pack
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed (exit $LASTEXITCODE)." }
    Write-Host "package -> $ReleasesDir"
}
