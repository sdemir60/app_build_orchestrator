<#
 [yayin hatti] package.ps1, release-guard.ps1, release.ps1 ve verify-publish.ps1'in ORTAK parcalari: Directory.Build.props
 degerleri, CHANGELOG surum basliklari, GitHub'daki release sayisi ve calisan uygulama ornegi sondasi tek yerde
 (dot-source edilir, tek basina calistirilmaz). Baslik bicimini uygulamanin kendi parser'i (ReleaseNotes.Parse) da okur;
 ReleaseScriptsTests ikisinin ayni bolumu verdigini pinler.
#>

$RepoRoot = Split-Path $PSScriptRoot -Parent
$DefaultRepoUrl = 'https://github.com/sdemir60/app_build_orchestrator'

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

function Get-RunningApp([string]$ProcessName = 'BuildOrchestrator.App') {
    # Calisan uygulama ornekleri (yoksa bos). Uygulama tek-orneklidir (ikinci ornek mevcut pencereyi one getirip kapanir) ve
    # calisan Supervisor kendi binary'lerini kilitler (CLAUDE.md "uygulama acikken build alma"): release.ps1 (Release build)
    # ve verify-publish.ps1 (olcum) ayni sondayi kullanir. Tek elemanli sonuc dizi olarak gelmez - cagiran @(...) ile sarar.
    return @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
}

function Get-ReleaseCount([string]$RepoUrl, [string]$Token) {
    # Repoda yayinlanmis release var mi (0 = ilk yayin). Windows PowerShell 5.1'de Invoke-RestMethod JSON dizisini
    # numaralandirmaz: @(Invoke-RestMethod ...) bos diziyi TEK eleman olarak sarar (Count 1 verir). Bu yuzden cevap
    # once degiskene atanir, sonra sayilir. Bos cevap PowerShell 7'de $null gelir; @($null).Count da 1 oldugundan
    # $null ayrica sifir sayilir.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $api = ($RepoUrl -replace '^https://github.com/', 'https://api.github.com/repos/') + '/releases?per_page=1'
    $headers = @{ 'User-Agent' = 'BuildOrchestrator-package' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $releases = Invoke-RestMethod -Uri $api -Headers $headers
    if ($null -eq $releases) { return 0 }
    return @($releases).Count
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
