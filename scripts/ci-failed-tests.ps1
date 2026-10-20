# [yayin hatti] ci.yml'in test adimi dusunce kosar: TRX'teki dusen testleri GitHub'a TEK bir hata notu (annotation) olarak yazar -
# baslikta sayi, govdede test basina bir satir (ad + hata mesajinin ilk satiri). Not herkese acik API'den kimliksiz okunur
# (check-runs/{id}/annotations); job logu ve TRX artifact'i giris ister. Tek not, cunku GitHub adim basina en cok on hata notu tutar.
# Adimi dusurmez: job zaten kirmizidir. Pin: ReleaseScriptsTests (cikti), RepoHygieneTests (ci.yml'deki adim).
param([Parameter(Mandatory = $true)][string]$Trx)
$ErrorActionPreference = 'Stop'
# Hata mesajlari ASCII disi karakter tasiyabilir; runner adim ciktisini UTF-8 okur.
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)

# GitHub is akisi komutunun mesaj kisminda % , CR ve LF kacirilir.
function ConvertTo-CommandData([string]$text) { $text.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A') }

if (-not (Test-Path -LiteralPath $Trx)) {
    Write-Output '::error title=No test results::The test step wrote no TRX file - it stopped before any result (build or test host failure).'
    exit 0
}
[xml]$doc = Get-Content -LiteralPath $Trx -Raw -Encoding UTF8
$failed = @($doc.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' })
if ($failed.Count -eq 0) { exit 0 }

$listed = 50
$lines = @(foreach ($result in ($failed | Select-Object -First $listed)) {
    $message = ''
    if ($result.Output -and $result.Output.ErrorInfo -and $result.Output.ErrorInfo.Message) {
        $message = ([string]$result.Output.ErrorInfo.Message -split "`r?`n")[0].Trim()
    }
    if ($message.Length -gt 300) { $message = $message.Substring(0, 300) + '...' }
    "$($result.testName) :: $message"
})
if ($failed.Count -gt $listed) { $lines += "... $($failed.Count - $listed) more in the TRX artifact" }
Write-Output ('::error title={0} test(s) failed::{1}' -f $failed.Count, (ConvertTo-CommandData ($lines -join "`n")))
