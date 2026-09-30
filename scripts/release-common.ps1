<#
 [yayin hatti] package.ps1, release-guard.ps1, release.ps1 ve verify-publish.ps1'in ORTAK parcalari: Directory.Build.props
 degerleri, CHANGELOG surum basliklari, GitHub API'si (release sayisi, bir commit'in CI kosusu), uc durumlu git ata sorusu,
 bir surumun onceki paketlerinin temizligi ve calisan uygulama ornegi sondasi tek yerde (dot-source edilir, tek basina
 calistirilmaz). Baslik bicimini uygulamanin kendi parser'i (ReleaseNotes.Parse) da okur; ReleaseScriptsTests ikisinin ayni
 bolumu verdigini pinler.
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

function Get-RunningApp([string]$ProcessName = 'BuildOrchestrator.App', [string]$UnderPath) {
    # Calisan uygulama ornekleri (yoksa bos). Uygulama tek-orneklidir (ikinci ornek mevcut pencereyi one getirip kapanir) ve
    # calisan Supervisor kendi binary'lerini kilitler (CLAUDE.md "uygulama acikken build alma"). Iki cagiran, iki soru:
    #  - release.ps1 -UnderPath <repo koku> verir: Release build yalniz BU checkout'tan calisan kopyanin dosyalarina carpar; tepsideki
    #    KURULU kopya (%LocalAppData%\BuildOrchestrator.App\current) baska klasordedir ve yayini durdurmamali.
    #  - verify-publish.ps1 kok VERMEZ: tek-ornek kapisi ve canli pencere okuma yuzunden HER ornek (kurulu kopya dahil) olcumu bozar.
    # -UnderPath (mutlak yol; goreli yol surecin calisma dizinine gore cozulur, PowerShell'in konumuna gore degil) verilince yalniz
    # exe'si o klasorun ALTINDA olan process'ler sayilir; sinir klasordur, metin oneki degil ("C:\a" altinda "C:\a-ai" yok - repo
    # ile -ai calisma klasoru kardestir). Konumu okunamayan process (erisim reddi: yukseltilmis ya da
    # baska kullanicinin sureci) SAYILIR: yanlis "yok" build'i kilitli dosyada dusurur, yanlis "var" yalniz bir "kapat" mesaji ister.
    # Tek elemanli sonuc dizi olarak gelmez - cagiran @(...) ile sarar.
    $running = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if (-not $UnderPath) { return $running }
    $root = [System.IO.Path]::GetFullPath($UnderPath).TrimEnd('\', '/') + '\'
    return @($running | Where-Object {
            $counted = $true
            try {
                $exe = $_.Path
                if ($exe) { $counted = [System.IO.Path]::GetFullPath($exe).StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase) }
            }
            catch { }
            $counted
        })
}

function Test-GitAncestor([string]$Ancestor, [string]$Descendant) {
    # $Ancestor, $Descendant'in atasi mi (git merge-base --is-ancestor, repo $RepoRoot). Uc durum: cikis 0 = ata ($true), 1 = degil
    # ($false), digeri = sorulamadi (ref yok, sig checkout) -> throw "git exit N". Sorulamayan ata ne "ata" ne "degil" sayilir
    # (dogrulanamayan yayin cikmaz); cagiran (release.ps1'in ata guard'i, release-guard.ps1 -RequireOnMain) kendi mesajini yazar.
    & git -C $RepoRoot merge-base --is-ancestor $Ancestor $Descendant
    $code = $LASTEXITCODE
    if ($code -eq 0) { return $true }
    if ($code -eq 1) { return $false }
    throw "git exit $code"
}

function Invoke-GitHubApi([string]$RepoUrl, [string]$Path, [string]$Token) {
    # GitHub REST API'sine GET: RepoUrl web adresidir (https://github.com/<sahip>/<repo>), Path repo altindaki uc
    # ('releases?per_page=1' gibi). Acik repo kimliksiz okunur (saatte 60 istek); Token verilirse Bearer gider. GitHub
    # User-Agent'siz istegi reddeder. Cevap Invoke-RestMethod'un verdigi gibi gecer (degiskene atanip dondurulmez: donus
    # diziyi numaralandirirdi) - JSON dizisini sayan cagiran Windows PowerShell 5.1 davranisini bilir (Get-ReleaseCount).
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent' = 'BuildOrchestrator-scripts' }
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    Invoke-RestMethod -Uri (($RepoUrl -replace '^https://github.com/', 'https://api.github.com/repos/') + '/' + $Path) -Headers $headers
}

function Get-ReleaseCount([string]$RepoUrl, [string]$Token) {
    # Repoda yayinlanmis release var mi (0 = ilk yayin). Windows PowerShell 5.1'de Invoke-RestMethod JSON dizisini
    # numaralandirmaz: @(Invoke-RestMethod ...) bos diziyi TEK eleman olarak sarar (Count 1 verir). Bu yuzden cevap
    # once degiskene atanir, sonra sayilir. Bos cevap PowerShell 7'de $null gelir; @($null).Count da 1 oldugundan
    # $null ayrica sifir sayilir.
    $releases = Invoke-GitHubApi $RepoUrl 'releases?per_page=1' $Token
    if ($null -eq $releases) { return 0 }
    return @($releases).Count
}

function Get-CiConclusion([string]$RepoUrl, [string]$Sha, [string]$Workflow = 'ci.yml') {
    # Bir commit'in en yeni workflow kosusu (release.ps1: develop HEAD'inin ci.yml'i yesil mi): kosu yoksa $null, varsa
    # Status (queued / in_progress / completed ...) ve Conclusion (success / failure / cancelled ...; kosu bitmeden bos).
    # Ayni commit'in birden cok kosusu olabilir: ayni sha'nin birden cok ref'e itilmesi (yayin main ile develop'u ayni sha'da
    # iter), PR, elle tetikleme ("Re-run" yeni kosu acmaz, ayni kosunun yeni denemesidir). API en yenisini basta verir,
    # per_page=1 onu alir. workflow_runs cevabin bir OZELLIGIDIR (ust duzey dizi degil): bos dizi @() icinde bos kalir.
    $response = Invoke-GitHubApi $RepoUrl "actions/workflows/$Workflow/runs?head_sha=$Sha&per_page=1"
    $run = @($response.workflow_runs) | Select-Object -First 1
    if ($null -eq $run) { return $null }
    return [pscustomobject]@{ Status = [string]$run.status; Conclusion = [string]$run.conclusion }
}

function Remove-PackagedVersion([string]$ReleasesDir, [string]$Version) {
    # Ayni surumu yeniden paketlemek mesru (lokal deneme/prova), ama vpk pack klasorde ayni ya da daha yeni bir surumun paketi
    # varken "There is a release in channel win which is equal or greater ..." diye duser. vpk bunu klasordeki nupkg'lardan
    # okur (OLCULDU: yalniz indeks dosyalarini silmek yetmez; yalniz nupkg'lari silmek yeter). Bu yuzden O surumun full/delta
    # paketleri silinir; DIGER surumlerin paketleri kalir - sonraki surumun delta'si onlardan uretilir. Indeks dosyalari
    # (RELEASES, releases.*.json, assets.*.json) vpk'nin nupkg'lardan her pack'te yeniden urettigi ozetlerdir; silinen paketleri
    # gosteren eski satir kalmasin diye paketlerle birlikte gider. Silinecek paket yoksa hicbir dosyaya dokunulmaz (indeksler
    # onceki surumlerin kaydi). Kurulum dosyasi (*-Setup.exe) her pack'te uzerine yazilir; burada yeri yok.
    if (-not (Test-Path -LiteralPath $ReleasesDir)) { return }
    $files = @(Get-ChildItem -LiteralPath $ReleasesDir -File)
    $own = '^.+-' + [regex]::Escape($Version) + '-(full|delta)\.nupkg$'
    $packages = @($files | Where-Object { $_.Name -match $own })
    if ($packages.Count -eq 0) { return }
    $indexes = @($files | Where-Object { $_.Name -match '^(RELEASES|releases\..+\.json|assets\..+\.json)$' })
    foreach ($file in ($packages + $indexes)) { Remove-Item -LiteralPath $file.FullName -Force }
    $names = ($packages | ForEach-Object { $_.Name }) -join ', '
    Write-Host "removed the earlier packages of $Version ($names) and the index files; the other versions stay for the delta"
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
