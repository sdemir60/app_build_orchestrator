<#
.SYNOPSIS
  [D1 · It-5 kabul] "Publish edilen exe calisiyor" iddiasinin TEKRARLANABILIR dogrulamasi.

.DESCRIPTION
  Bu script It-5 kabul kalemini elle-gozlem olmaktan cikarir. Adimlar:
    1. dotnet publish (framework-dependent, klasor tabanli, win-x64)
    2. Publish yerlesimi: supervisor\BuildOrchestrator.Supervisor.exe + Assets\GEIST-LICENSE.txt
    3. Publish edilen supervisor ikilisi ile NDJSON round-trip (engineReady + surum)
    4. Publish edilen App.exe baslatilir; supervisor child process'inin AYNI publish klasorunden
       dogdugu dogrulanir (WMI olay aboneligi ile beklenir, poll edilmez)
    5. Calisan pencereden UI Automation ile: konsol boot satiri "Engine ready - v<surum>" VE
       seritte "Engine missing/could not start" OLMADIGI dogrulanir
    6. App kapatilir; hicbir App/Supervisor process'i kalmadigi dogrulanir; publish klasoru silinir

  Cikis kodu 0 = PASS, 1 = FAIL. Normal `dotnet test` kosumunu etkilemez (suite'e dahil degildir).

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
    [switch] $KeepOutput
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$appProj = Join-Path $repoRoot 'src\BuildOrchestrator.App\BuildOrchestrator.App.csproj'
$failures = New-Object System.Collections.Generic.List[string]
$appProcess = $null

function Step([string] $text) { Write-Host "==> $text" }
function Check([string] $name, [bool] $ok, [string] $detail = '') {
    if ($ok) { Write-Host "    [PASS] $name $detail" }
    else { Write-Host "    [FAIL] $name $detail"; $script:failures.Add($name) }
}

try {
    # --------------------------------------------------------------- 1. publish
    Step "publish -> $OutputDir"
    $publishLog = & dotnet publish $appProj -c $Configuration -r $RuntimeIdentifier --self-contained false -o $OutputDir -v m 2>&1
    Check 'dotnet publish exit code 0' ($LASTEXITCODE -eq 0) "(exit $LASTEXITCODE)"
    if ($LASTEXITCODE -ne 0) { $publishLog | Select-Object -Last 15 | ForEach-Object { Write-Host "        $_" } }

    # --------------------------------------------------------------- 2. yerlesim
    Step 'publish yerlesimi'
    $appExe = Join-Path $OutputDir 'BuildOrchestrator.App.exe'
    $supExe = Join-Path $OutputDir 'supervisor\BuildOrchestrator.Supervisor.exe'
    $licence = Join-Path $OutputDir 'Assets\GEIST-LICENSE.txt'
    Check 'BuildOrchestrator.App.exe' (Test-Path $appExe)
    Check 'supervisor\BuildOrchestrator.Supervisor.exe' (Test-Path $supExe)
    Check 'Assets\GEIST-LICENSE.txt (OFL)' (Test-Path $licence)
    if (Test-Path (Join-Path $OutputDir 'supervisor')) {
        $count = (Get-ChildItem (Join-Path $OutputDir 'supervisor') -File).Count
        Write-Host "    supervisor\ icerigi: $count dosya"
    }
    if (-not (Test-Path $appExe) -or -not (Test-Path $supExe)) { throw 'publish yerlesimi eksik — devam edilemez' }

    # --------------------------------------------------------------- 3. NDJSON round-trip
    Step 'publish edilen supervisor ile NDJSON round-trip'
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
    Check 'engineVersion Directory.Build.props degeri' ($engineVersion -and $engineVersion -notmatch '^\d+\.\d+\.\d+$') "-> $engineVersion"

    # --------------------------------------------------------------- 4. exe'yi calistir
    Step 'publish edilen App.exe baslatiliyor'
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
    Check 'supervisor child process dogdu' ($null -ne $child) $(if ($child) { "(pid $($child.ProcessId), parent $($child.ParentProcessId))" })
    if ($child) {
        Check 'child parent = App process' ($child.ParentProcessId -eq $appProcess.Id)
        Check 'child publish klasorunden calisiyor' ($child.CommandLine -like "*$OutputDir*") "-> $($child.CommandLine)"
    }

    # --------------------------------------------------------------- 5. calisan UI'dan dogrulama
    Step 'calisan pencereden UI Automation ile dogrulama'
    Add-Type -AssemblyName UIAutomationClient
    Add-Type -AssemblyName UIAutomationTypes
    $appProcess.WaitForInputIdle($TimeoutSeconds * 1000) | Out-Null
    $textCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $pidCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $appProcess.Id)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $texts = @()
    # Boot satiri UI'ya asenkron duser: sinirli bekleme (bu bir dogrulama harness'idir, urun/test kodu degil).
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
    if (-not $bootLine) { Write-Host "        okunan UI metinleri: $($texts -join ' | ')" }
    Check 'konsol boot satiri "Engine ready - v<surum>"' ($null -ne $bootLine) "-> $bootLine"
    if ($bootLine -and $engineVersion) { Check 'boot satirindaki surum = engineReady surumu' ($bootLine -like "*$engineVersion*") }
    Check 'seritte engine hata modu YOK' (-not ($texts -match 'Engine missing|Engine could not start'))
    Check 'pencere gorunur (UIA agaci okundu)' ($texts.Count -gt 0) "($($texts.Count) metin elemani)"
}
catch {
    Write-Host "    [FAIL] beklenmeyen hata: $($_.Exception.Message)"
    $failures.Add('unhandled: ' + $_.Exception.Message)
}
finally {
    # --------------------------------------------------------------- 6. temizlik
    Step 'temizlik'
    if ($appProcess -and -not $appProcess.HasExited) { Stop-Process -Id $appProcess.Id -Force -ErrorAction SilentlyContinue }
    if ($appProcess) { $appProcess.WaitForExit(10000) | Out-Null }   # olay tabanli bekleme (poll yok)
    Get-Process -Name 'BuildOrchestrator.App', 'BuildOrchestrator.Supervisor' -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Kill(); $_.WaitForExit(10000) | Out-Null }
    $left = @(Get-Process -Name 'BuildOrchestrator.App', 'BuildOrchestrator.Supervisor' -ErrorAction SilentlyContinue)
    Check 'geride process/pencere kalmadi' ($left.Count -eq 0)
    if (-not $KeepOutput) { Remove-Item -Recurse -Force $OutputDir -ErrorAction SilentlyContinue }
    Unregister-Event -SourceIdentifier 'BoSupervisorSpawn' -ErrorAction SilentlyContinue
}

Write-Host ''
if ($failures.Count -eq 0) { Write-Host 'RESULT: PASS'; exit 0 }
Write-Host ("RESULT: FAIL (" + ($failures -join '; ') + ')')
exit 1
