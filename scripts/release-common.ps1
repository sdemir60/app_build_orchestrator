<#
 [yayin hatti] package.ps1, release-guard.ps1 ve release.ps1'in ORTAK okuyuculari: Directory.Build.props degerleri ve
 CHANGELOG surum basliklari tek yerde okunur (dot-source edilir, tek basina calistirilmaz). Baslik bicimini uygulamanin
 kendi parser'i (ReleaseNotes.Parse) da okur; ReleaseScriptsTests ikisinin ayni bolumu verdigini pinler.
#>

$RepoRoot = Split-Path $PSScriptRoot -Parent

# "## [x.y.z] - yyyy-MM-dd" - Keep a Changelog surum basligi.
$ChangelogHeadingPattern = '^## \[(?<version>\d+\.\d+\.\d+)\] - (?<date>\d{4}-\d{2}-\d{2})$'

function Get-BuildProp([string]$Name) {
    # XmlDocument.Load dosyayi kendi kodlamasiyla okur (Get-Content -Raw PS 5.1'de BOM'suz dosyayi ANSI sayar).
    $doc = New-Object System.Xml.XmlDocument
    $doc.Load((Join-Path $RepoRoot 'Directory.Build.props'))
    $node = $doc.SelectSingleNode("/Project/PropertyGroup/$Name")
    if (-not $node) { throw "Directory.Build.props has no <$Name>." }
    return $node.InnerText
}

function Read-Changelog {
    # Satirlar + surum basliklari (Version, Date, Line = 0 tabanli satir indeksi); en yeni surum basta.
    $lines = [System.IO.File]::ReadAllLines((Join-Path $RepoRoot 'CHANGELOG.md'), [System.Text.Encoding]::UTF8)
    $headings = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match $ChangelogHeadingPattern) {
            $headings.Add([pscustomobject]@{ Version = $Matches.version; Date = $Matches.date; Line = $i })
        }
    }
    return [pscustomobject]@{ Lines = $lines; Headings = $headings.ToArray() }
}
