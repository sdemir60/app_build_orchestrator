<#
.SYNOPSIS
  [D1 · It-5 kabul] "Publish edilen exe calisiyor" iddiasinin TEKRARLANABILIR dogrulamasi.

.DESCRIPTION
  Bu script It-5 kabul kalemini elle-gozlem olmaktan cikarir. Adimlar:
    1. dotnet publish (framework-dependent, klasor tabanli, win-x64)
    2. Publish yerlesimi: supervisor\BuildOrchestrator.Supervisor.exe + Assets\GEIST-LICENSE.txt
    3. Publish edilen supervisor ikilisi ile NDJSON round-trip (engineReady + surum)
    4. [A13/T6 t6] Publish edilen supervisor ikilisi GERCEK bir Sync + Build kosturur ve en az bir
       runCompleted uretir. HEDEF KUCUK VE KENDI KENDINE YETER: script'in KENDI olusturdugu gecici
       is alaninda TEK bir minimal .NET Framework v4.6 class library (tests/.../MsBuild/LegacyFixture.cs
       CreateClassLib ile ayni sekil) + o dizinde `git init` + tek commit. Kullanicinin gercek reposu,
       gercek log/cache/state klasoru ve worktree havuzu HIC hedef alinmaz (--logs/--worktrees temp'e
       yonlendirilir). Maliyet: bir MSBuild.exe cagrisi (~5-15 sn, cogu vswhere + MSBuild acilisi).
    5. Publish edilen App.exe baslatilir; supervisor child process'inin AYNI publish klasorunden
       dogdugu dogrulanir (WMI olay aboneligi ile beklenir, poll edilmez)
    6. Calisan pencereden UI Automation ile: konsol boot satiri "Engine ready - v<surum>" VE
       seritte "Engine missing/could not start" OLMADIGI dogrulanir
    7. App sonlandirilir ve §3 CASCADE dogrulanir: supervisor child'ina DOKUNULMADAN kendiliginden
       olmesi beklenir (outer Job = KILL_ON_JOB_CLOSE). Ardindan yalniz KENDI pid'lerimiz icin bir
       guvenlik agi ve publish klasorunun silinmesi.

  On kosul: makinede acik bir Build Orchestrator ORNEGI OLMAMALI (App single-instance'tir) — varsa
  script olcum yapmadan RESULT: SKIPPED ile durur.

  Cikis kodu 0 = PASS, 1 = FAIL, 2 = on kosul saglanmadi. Normal `dotnet test` kosumunu etkilemez
  (suite'e dahil degildir).

.EXAMPLE
  pwsh -File scripts\verify-publish.ps1
  powershell -ExecutionPolicy Bypass -File scripts\verify-publish.ps1 -KeepOutput
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [string] $RuntimeIdentifier = 'win-x64',
    [string] $OutputDir = (Join-Path $env:TEMP ("bo-verify-publish-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))),
    [int]    $TimeoutSeconds = 60,
    # [t6] Adim 4'un (Sync + Build) ust siniri: tek kucuk projede olcum ~5-15 sn, pay birakilmistir.
    # Sure dolarsa dongu biter ve Check FAIL verir (sessiz bekleme yok).
    [int]    $BuildTimeoutSeconds = 180,
    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appProj = Join-Path $repoRoot 'src\BuildOrchestrator.App\BuildOrchestrator.App.csproj'
$failures = New-Object System.Collections.Generic.List[string]
$appProcess = $null
$child = $null      # App'in dogurdugu supervisor (WMI olayindan) — kapanis dogrulamasi bunu kullanir
$runSup = $null     # [t6] adim 4'un supervisor'i (Sync + Build) — finally onu da birakir
$runDirs = @()      # [t6] adim 4'un gecici klasorleri (is alani + logs) — finally siler

function Step([string] $text) { Write-Host "==> $text" }
function Check([string] $name, [bool] $ok, [string] $detail = '') {
    if ($ok) { Write-Host "    [PASS] $name $detail" }
    else { Write-Host "    [FAIL] $name $detail"; $script:failures.Add($name) }
}

# [t6] Tek satirlik NDJSON komutu (BOM'suz UTF-8 + '\n'). Anahtar sirasi KORUNUR ([ordered]): polimorfik
# ayristirmada "type" ayirt edicisi ONCE gelmelidir (Contracts/IpcMessages.cs JsonPolymorphic).
function SendCommand([System.IO.Stream] $stream, $command) {
    $json = ($command | ConvertTo-Json -Compress -Depth 5)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json + "`n")
    $stream.Write($bytes, 0, $bytes.Length)
    $stream.Flush()
}

# --------------------------------------------------------------- 0. on kosul (try'dan ONCE)
# [round 2] App SINGLE-INSTANCE'tir (App.xaml.cs: ikinci ornek mevcut pencereyi one getirip HEMEN kapanir).
# Makinede acik bir ornek varken bu script yanlis-KIRMIZI verirdi (baslattigimiz process aninda olurdu).
# Bu bir dogrulama hatasi DEGIL, kullanim hatasidir: net mesajla ve AYRISAN cikis koduyla (2) dur.
Step 'precondition: no Build Orchestrator instance is running'
$running = @(Get-Process -Name 'BuildOrchestrator.App' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Host "    [STOP] A Build Orchestrator is already running (pid: $($running.Id -join ', '))."
    Write-Host '           The App is SINGLE-INSTANCE, so this script cannot take a meaningful measurement:'
    Write-Host '           the second instance it starts would bring the existing window forward and exit at once.'
    Write-Host '           Close the running instance first (tray icon > Exit), then run this again.'
    Write-Host ''
    Write-Host 'RESULT: SKIPPED (precondition not met)'
    exit 2
}
Write-Host '    [PASS] no instance running'

# --------------------------------------------------------------- stdin encoding (BOM tuzagi)
# [t6] OLCULDU: Windows PowerShell 5.1'de Console.InputEncoding UTF-8 "BOM'lu" varyantidir; .NET, child'in
# stdin StreamWriter'ini o encoding ile kurar ve PREAMBLE'i (EF BB BF) pipe'a YAZAR. Sonuc: supervisor'a giden
# ILK satir "<BOM>{...}" olur ve error(badCommand) ile reddedilir — komut KAYBOLUR. Bu tuzak adim 3'un shutdown
# komutunu da sessizce yutuyordu (process yalnizca stdin EOF ile kapaniyordu, ki o da calisir — bu yuzden
# gorunmuyordu). ProcessStartInfo.StandardInputEncoding .NET Framework'te YOK, bu yuzden konsol encoding'i
# gecici olarak BOM'suz UTF-8'e alinir ve finally'de geri konur.
$savedInputEncoding = $null
try {
    $savedInputEncoding = [Console]::InputEncoding
    [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
}
catch { Write-Host '    (note: the console input encoding could not be changed - the stdin BOM trap may be open)' }

try {
    # --------------------------------------------------------------- 1. publish
    Step "publish -> $OutputDir"
    $publishLog = & dotnet publish $appProj -c $Configuration -r $RuntimeIdentifier --self-contained false -o $OutputDir -v m 2>&1
    Check 'dotnet publish exit code 0' ($LASTEXITCODE -eq 0) "(exit $LASTEXITCODE)"
    if ($LASTEXITCODE -ne 0) { $publishLog | Select-Object -Last 15 | ForEach-Object { Write-Host "        $_" } }

    # --------------------------------------------------------------- 2. yerlesim
    Step 'publish layout'
    $appExe = Join-Path $OutputDir 'BuildOrchestrator.App.exe'
    $supExe = Join-Path $OutputDir 'supervisor\BuildOrchestrator.Supervisor.exe'
    $licence = Join-Path $OutputDir 'Assets\GEIST-LICENSE.txt'
    Check 'BuildOrchestrator.App.exe' (Test-Path $appExe)
    Check 'supervisor\BuildOrchestrator.Supervisor.exe' (Test-Path $supExe)
    Check 'Assets\GEIST-LICENSE.txt (OFL)' (Test-Path $licence)
    if (Test-Path (Join-Path $OutputDir 'supervisor')) {
        $count = (Get-ChildItem (Join-Path $OutputDir 'supervisor') -File).Count
        Write-Host "    supervisor\ contents: $count files"
    }
    if (-not (Test-Path $appExe) -or -not (Test-Path $supExe)) { throw 'the publish layout is incomplete - cannot continue' }

    # --------------------------------------------------------------- 3. NDJSON round-trip
    Step 'NDJSON round-trip with the published supervisor'
    $psi = New-Object System.Diagnostics.ProcessStartInfo $supExe
    $psi.RedirectStandardInput = $true; $psi.RedirectStandardOutput = $true; $psi.UseShellExecute = $false
    $sup = [System.Diagnostics.Process]::Start($psi)
    $bytes = [System.Text.Encoding]::UTF8.GetBytes("{`"type`":`"shutdown`"}`n")   # BOM'suz ham NDJSON
    $sup.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
    $sup.StandardInput.BaseStream.Flush()
    $sup.StandardInput.BaseStream.Close()         # stdin EOF: shutdown gelmese bile duzgun cikis (App kapanisi ile ayni yol)
    $firstLine = $sup.StandardOutput.ReadLine()   # blocking read — poll YOK
    $sup.StandardOutput.ReadToEnd() | Out-Null
    if (-not $sup.WaitForExit(15000)) { $sup.Kill(); $sup.WaitForExit(5000) | Out-Null }
    Check 'engineReady stdout NDJSON' ($firstLine -like '{"type":"engineReady"*') "-> $firstLine"
    Check 'supervisor exit code 0' ($sup.ExitCode -eq 0) "(exit $($sup.ExitCode))"
    # JSON'dan COZULMUS deger (stdout'ta '+' + olarak kacislidir).
    $engineVersion = $null
    if ($firstLine -like '{*') { $engineVersion = ($firstLine | ConvertFrom-Json).engineVersion }
    Check 'engineVersion is the Directory.Build.props value' ($engineVersion -and $engineVersion -notmatch '^\d+\.\d+\.\d+$') "-> $engineVersion"

    # --------------------------------------------------------------- 4. [t6] Sync + Build (publish edilen ikili)
    # Bu adima kadar publish edilen supervisor yalnizca "acilip engineReady yaziyor" seviyesinde dogrulaniyordu
    # (adim 3). Kabul kalemi ise publish edilen ikilinin GERCEKTEN is yaptigidir: bir workspace'i Sync edip en az
    # bir runCompleted uretmesi. Hedef BILINCLI olarak minimum tutulur (asagida) — kullanicinin makinesini yormaz.
    Step 'Sync + Build with the published supervisor (one small project)'
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Host '    [SKIP] git was not found - Sync cannot pass the git repository gate (SyncWorkspaceService), step not measured.'
    }
    else {
        $ws = Join-Path $env:TEMP ("bo-verify-ws-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        $runLogs = Join-Path $env:TEMP ("bo-verify-logs-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
        $runDirs = @($ws, $runLogs)
        New-Item -ItemType Directory -Force $ws | Out-Null
        New-Item -ItemType Directory -Force $runLogs | Out-Null

        # NE KOSUYORUZ: TEK bir minimal .NET Framework v4.6 class library — tek kaynak dosyasi, packages.config YOK,
        # post-build YOK, proje referansi YOK (tests/BuildOrchestrator.Tests/MsBuild/LegacyFixture.cs CreateClassLib
        # ile ayni sekil; suite'in gercek MSBuild testleri de bunu derliyor). MSBuild.exe icin en ucuz GERCEK is.
        $asm = 'VerifyPublishLib'
        Set-Content -Path (Join-Path $ws 'Class1.cs') -Encoding utf8 -Value @(
            "namespace $asm",
            '{',
            '    public class Class1',
            '    {',
            '        public int Answer() { return 42; }',
            '    }',
            '}')
        Set-Content -Path (Join-Path $ws "$asm.csproj") -Encoding utf8 -Value @(
            '<?xml version="1.0" encoding="utf-8"?>',
            '<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">',
            '  <Import Project="$(MSBuildToolsPath)\Microsoft.Common.props" Condition="Exists(''$(MSBuildToolsPath)\Microsoft.Common.props'')" />',
            '  <PropertyGroup>',
            '    <Configuration Condition=" ''$(Configuration)'' == '''' ">Debug</Configuration>',
            '    <Platform Condition=" ''$(Platform)'' == '''' ">AnyCPU</Platform>',
            "    <ProjectGuid>{$([Guid]::NewGuid().ToString('D').ToUpperInvariant())}</ProjectGuid>",
            '    <OutputType>Library</OutputType>',
            "    <RootNamespace>$asm</RootNamespace>",
            "    <AssemblyName>$asm</AssemblyName>",
            '    <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>',
            '  </PropertyGroup>',
            '  <PropertyGroup Condition=" ''$(Configuration)|$(Platform)'' == ''Debug|AnyCPU'' ">',
            '    <DebugSymbols>true</DebugSymbols>',
            '    <DebugType>full</DebugType>',
            '    <Optimize>false</Optimize>',
            '    <OutputPath>bin\Debug\</OutputPath>',
            '    <DefineConstants>DEBUG;TRACE</DefineConstants>',
            '    <ErrorReport>prompt</ErrorReport>',
            '    <WarningLevel>4</WarningLevel>',
            '  </PropertyGroup>',
            '  <ItemGroup>',
            '    <Reference Include="System" />',
            '    <Reference Include="System.Core" />',
            '  </ItemGroup>',
            '  <ItemGroup>',
            '    <Compile Include="Class1.cs" />',
            '  </ItemGroup>',
            '  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />',
            '</Project>')

        # Sync bir git repo'su ISTER (SyncWorkspaceService: HEAD okunamazsa planFailed). Kimlik yalniz BU commit
        # icin verilir (-c) — kullanicinin global git yapilandirmasina DOKUNULMAZ.
        & git -C $ws init --quiet 2>$null
        & git -C $ws add -A 2>$null
        & git -C $ws -c user.name='verify-publish' -c user.email='verify-publish@local' commit --quiet -m 'fixture' 2>$null
        $branch = (& git -C $ws rev-parse --abbrev-ref HEAD 2>$null)
        if (-not $branch) { $branch = 'main' }

        # Supervisor'in log/cache/build-state'i ve worktree havuzu temp'e yonlendirilir: kullanicinin gercek
        # %LOCALAPPDATA%\BuildOrchestrator icerigi bu olcumden ETKILENMEZ (suite'in TestPaths.Psi deseni).
        $rpsi = New-Object System.Diagnostics.ProcessStartInfo $supExe
        $rpsi.Arguments = "--logs `"$runLogs`" --worktrees `"$(Join-Path $runLogs 'worktrees')`""
        $rpsi.RedirectStandardInput = $true; $rpsi.RedirectStandardOutput = $true; $rpsi.UseShellExecute = $false
        $runSup = [System.Diagnostics.Process]::Start($rpsi)
        $runStdin = $runSup.StandardInput.BaseStream

        SendCommand $runStdin ([ordered]@{ type = 'syncWorkspace'; rootPath = $ws; branch = $branch; configuration = 'Debug' })

        # Olay dongusu: her satir BLOKE bir okumadir (poll YOK). startRun, syncCompleted GELINCE gonderilir —
        # yani sira gercekten "Sync sonra Build"tir. Sure siniri $BuildTimeoutSeconds; dolarsa dongu biter ve
        # asagidaki Check'ler FAIL verir (sessiz bekleme yok).
        $deadline = (Get-Date).AddSeconds($BuildTimeoutSeconds)
        $syncCompleted = $null; $runCompleted = $null; $engineError = $null
        while ($null -eq $runCompleted -and $null -eq $engineError -and (Get-Date) -lt $deadline) {
            $remaining = [int][Math]::Max(1, ($deadline - (Get-Date)).TotalMilliseconds)
            $readTask = $runSup.StandardOutput.ReadLineAsync()
            if (-not $readTask.Wait($remaining)) { break }   # sure doldu
            $line = $readTask.Result
            if ($null -eq $line) { break }                   # stdout kapandi (supervisor oldu)
            $evt = $null
            try { $evt = $line | ConvertFrom-Json } catch { continue }
            switch ($evt.type) {
                'syncCompleted' {
                    $syncCompleted = $evt
                    SendCommand $runStdin ([ordered]@{
                            type = 'startRun'; runId = 'verify-publish'; mode = 'rebuild'; rootPath = $ws
                            configuration = 'Debug'; parallelism = 1; branch = $branch
                        })
                }
                'runCompleted' { $runCompleted = $evt }
                'error' { $engineError = $evt }
            }
        }

        Check 'syncCompleted arrived (published supervisor)' ($null -ne $syncCompleted) `
            $(if ($syncCompleted) { "-> $($syncCompleted.projectCount) projects, branch $($syncCompleted.branch)" })
        if ($engineError -and $engineError.code -eq 'msbuildNotFound') {
            # VS/MSBuild kurulu degil: publish'in degil MAKINENIN eksigi — suite de bu durumda testi atlar
            # (MsBuildInvokerTests/KillMidBuildTests deseni). FAIL yazmak yaniltici olurdu.
            # NOT: bu satir CIFT tirnakli — BOM'suz dosyada Windows PowerShell 5.1'in ANSI cozumu yuzunden
            # cift tirnakli stringlerde ASCII-DISI karakter (em dash) parse'i bozar; burada duz '-' kullanilir.
            Write-Host "    [SKIP] MSBuild.exe was not found ($($engineError.message)) - the build cannot run on this machine."
        }
        else {
            if ($engineError) { Check "no engine error" $false "-> $($engineError.code): $($engineError.message)" }
            Check 'runCompleted arrived (the published binary really built)' ($null -ne $runCompleted) `
                $(if ($runCompleted) { "-> outcome $($runCompleted.outcome), succeeded $($runCompleted.succeeded), failed $($runCompleted.failed)" })
            if ($runCompleted) {
                Check 'run outcome = completed' ($runCompleted.outcome -eq 'completed')
                Check 'one project succeeded, none failed' ($runCompleted.succeeded -eq 1 -and $runCompleted.failed -eq 0)
                Check 'the built DLL is on disk' (Test-Path (Join-Path $ws "bin\Debug\$asm.dll"))
            }
        }

        SendCommand $runStdin ([ordered]@{ type = 'shutdown' })
        $runStdin.Close()                                   # stdin EOF (adim 3 ile ayni kapanis yolu)
        $runSup.StandardOutput.ReadToEnd() | Out-Null
        if (-not $runSup.WaitForExit(15000)) { $runSup.Kill(); $runSup.WaitForExit(5000) | Out-Null }
        Check 'Sync+Build supervisor exit code 0' ($runSup.ExitCode -eq 0) "(exit $($runSup.ExitCode))"
        $runSup = $null
    }

    # --------------------------------------------------------------- 5. exe'yi calistir
    Step 'starting the published App.exe'
    $query = "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_Process' AND TargetInstance.Name = 'BuildOrchestrator.Supervisor.exe'"
    Register-CimIndicationEvent -Query $query -SourceIdentifier 'BoSupervisorSpawn' | Out-Null
    $appProcess = Start-Process $appExe -PassThru
    $spawn = Wait-Event -SourceIdentifier 'BoSupervisorSpawn' -Timeout $TimeoutSeconds   # olay tabanli bekleme
    $child = $null
    if ($spawn) {
        $child = $spawn.SourceEventArgs.NewEvent.TargetInstance
        Remove-Event -EventIdentifier $spawn.EventIdentifier
    }
    Unregister-Event -SourceIdentifier 'BoSupervisorSpawn'
    Check 'the supervisor child process was spawned' ($null -ne $child) $(if ($child) { "(pid $($child.ProcessId), parent $($child.ParentProcessId))" })
    if ($child) {
        Check 'child parent = App process' ($child.ParentProcessId -eq $appProcess.Id)
        Check 'the child runs from the publish folder' ($child.CommandLine -like "*$OutputDir*") "-> $($child.CommandLine)"
    }

    # --------------------------------------------------------------- 6. calisan UI'dan dogrulama
    Step 'verifying the running window via UI Automation'
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $appProcess.WaitForInputIdle($TimeoutSeconds * 1000) | Out-Null
    $textCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $pidCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $appProcess.Id)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $texts = @()
    # [round 2 · D8 notu] Asagidaki dongu POLL eder ve `Start-Sleep` kullanir. Bu, D8 ("sleep-poll YASAK")
    # ihlali DEGILDIR: D8 urun ve test kodunu baglar; burasi bir dogrulama harness'idir ve "UI agacinda bir
    # metin belirdi" olayinin ucuz bir olay-tabanli karsiligi yoktur (UIA'nin property-changed aboneligi bu
    # is icin ayri bir runspace/callback altyapisi gerektirirdi). Bekleme SINIRLIDIR: $deadline =
    # $TimeoutSeconds; sure dolarsa dongu biter ve asagidaki Check FAIL verir (sessiz bekleme yok).
    while ((Get-Date) -lt $deadline) {
        $window = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $pidCondition)
        if ($window) {
            # Serit/panel etiketleri ControlType.Text; konsol govdesi (AvalonEdit) ValuePattern tasir —
            # boot satiri orada oldugu icin ikisi de toplanir.
            $texts = @($window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $textCondition) |
                ForEach-Object { $_.Current.Name } | Where-Object { $_ })
            $valued = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::IsValuePatternAvailableProperty, $true)))
            foreach ($v in $valued) {
                $pattern = $null
                if ($v.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref] $pattern)) {
                    $value = $pattern.Current.Value
                    if ($value) { $texts += ($value -split "`r?`n") }
                }
            }
            if ($texts -match 'Engine ready') { break }
        }
        Start-Sleep -Milliseconds 250
    }
    $bootLine = $texts | Where-Object { $_ -like '*Engine ready*' } | Select-Object -First 1
    if (-not $bootLine) { Write-Host "        UI texts read: $($texts -join ' | ')" }
    Check 'console boot line "Engine ready - v<version>"' ($null -ne $bootLine) "-> $bootLine"
    if ($bootLine -and $engineVersion) { Check 'the version on the boot line = the engineReady version' ($bootLine -like "*$engineVersion*") }
    Check 'NO engine error mode on the ribbon' (-not ($texts -match 'Engine missing|Engine could not start'))
    Check 'the window is visible (the UIA tree was read)' ($texts.Count -gt 0) "($($texts.Count) text elements)"
}
catch {
    Write-Host "    [FAIL] unexpected error: $($_.Exception.Message)"
    $failures.Add('unhandled: ' + $_.Exception.Message)
}
finally {
    # --------------------------------------------------------------- 7. kapanis: CASCADE + temizlik
    # [round 2] Eski hal TOTOLOJIKti: "geride process kalmadi" kendi force-kill supurmemizden SONRA
    # kosuyordu, yani hicbir kosulda fail edemezdi. Simdi olculen sey §3 GARANTISI: App olunce outer Job
    # (KILL_ON_JOB_CLOSE) supervisor'i KENDILIGINDEN oldurur. Bu yuzden child pid'ine DOKUNULMAZ; yalnizca
    # App sonlandirilir ve child'in kendi kendine olmesi beklenir. (Pencereyi kapatmak App'i sonlandirmaz —
    # tepsiye iner — bu yuzden "normal kapanis" burada App process'ini sonlandirmaktir; cascade'in en sert
    # bicimde sinanmasi da budur: graceful shutdown yok, isi Job Object yapmak zorunda.)
    Step 'shutdown: cascade verification + cleanup'
    if ($appProcess) {
        if (-not $appProcess.HasExited) { Stop-Process -Id $appProcess.Id -Force -ErrorAction SilentlyContinue }
        $appProcess.WaitForExit(15000) | Out-Null                      # olay tabanli bekleme (poll yok)
        Check 'the App process exited' $appProcess.HasExited "(pid $($appProcess.Id))"
    }
    if ($child) {
        $childProc = Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
        $cascaded = if ($childProc) { $childProc.WaitForExit(15000) } else { $true }   # KILL cagrisi YOK
        Check 'the supervisor died on its own via CASCADE (the child pid was never killed)' $cascaded "(pid $($child.ProcessId))"
    }

    # Guvenlik agi — YALNIZ bizim dogurdugumuz pid'ler (isme gore makine supurmesi YOK: baskasinin
    # ornegini oldurmeyiz). Buraya bir sey dusuyorsa yukaridaki cascade Check'i ZATEN fail etmistir.
    foreach ($targetPid in @($appProcess.Id, $child.ProcessId | Where-Object { $_ })) {
        $stray = Get-Process -Id $targetPid -ErrorAction SilentlyContinue
        if ($stray) { $stray.Kill(); $stray.WaitForExit(10000) | Out-Null; Write-Host "    (safety net: pid $targetPid was killed)" }
    }
    # [t6] Adim 4'un supervisor'i normalde orada kapanir; buraya bir istisnayla dusulduyse birakilir
    # (KENDI dogurdugumuz pid — isme gore supurme YOK).
    if ($runSup -and -not $runSup.HasExited) { $runSup.Kill(); $runSup.WaitForExit(5000) | Out-Null }

    if (-not $KeepOutput) {
        Remove-Item -Recurse -Force $OutputDir -ErrorAction SilentlyContinue
        foreach ($dir in $runDirs) { Remove-Item -Recurse -Force $dir -ErrorAction SilentlyContinue }
    }
    Unregister-Event -SourceIdentifier 'BoSupervisorSpawn' -ErrorAction SilentlyContinue
    if ($savedInputEncoding) { try { [Console]::InputEncoding = $savedInputEncoding } catch { } }
}

Write-Host ''
if ($failures.Count -eq 0) { Write-Host 'RESULT: PASS'; exit 0 }
Write-Host ("RESULT: FAIL (" + ($failures -join '; ') + ')')
exit 1
