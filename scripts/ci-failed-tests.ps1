# [yayin hatti] ci.yml'in test adimi dusunce kosar: TRX'teki dusen testleri GitHub'a TEK bir hata notu (annotation) olarak yazar -
# baslikta sayi, govdede test basina bir satir (ad + hata mesajinin ilk satiri). Not herkese acik API'den kimliksiz okunur
# (check-runs/{id}/annotations); job logu ve TRX artifact'i giris ister. Tek not, cunku GitHub adim basina en cok on hata notu tutar.
# Adimi dusurmez: job zaten kirmizidir. Pin: ReleaseScriptsTests (cikti), RepoHygieneTests (ci.yml'deki adim).
param([Parameter(Mandatory = $true)][string]$Trx)
$ErrorActionPreference = 'Stop'

# Not duz ASCII yazilir: runner'da cikti iki kabuk katmanindan gecer (adimin pwsh'i -> powershell.exe) ve ASCII disi karakter
# bozuluyordu (olculdu 2026-10-20: "calisma" -> "?al??ma"). Aksanli harf temel harfine iner (FormD + birlesen isaretler atilir),
# ayrismayanlar ve tipografik isaretler asagidaki tabloyla, kalan her sey '?'. Tam metin TRX artifact'inda kalir.
$asciiFor = @{
    0x0131 = 'i'; 0x2013 = '-'; 0x2014 = '-'; 0x2192 = '->'; 0x2190 = '<-'; 0x2026 = '...'; 0x00B7 = '.'; 0x00D7 = 'x'
    0x2264 = '<='; 0x2265 = '>='; 0x2260 = '!='; 0x201C = '"'; 0x201D = '"'; 0x2018 = "'"; 0x2019 = "'"; 0x00A0 = ' '
}
function ConvertTo-Ascii([string]$text) {
    $builder = New-Object System.Text.StringBuilder
    foreach ($ch in $text.Normalize([System.Text.NormalizationForm]::FormD).ToCharArray()) {
        $code = [int]$ch
        if ($code -lt 128) { [void]$builder.Append($ch); continue }
        if ([System.Globalization.CharUnicodeInfo]::GetUnicodeCategory($ch) -eq [System.Globalization.UnicodeCategory]::NonSpacingMark) { continue }
        if ($asciiFor.ContainsKey($code)) { [void]$builder.Append($asciiFor[$code]) } else { [void]$builder.Append('?') }
    }
    $builder.ToString()
}

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
    ConvertTo-Ascii "$($result.testName) :: $message"
})
if ($failed.Count -gt $listed) { $lines += "... $($failed.Count - $listed) more in the TRX artifact" }
Write-Output ('::error title={0} test(s) failed::{1}' -f $failed.Count, (ConvertTo-CommandData ($lines -join "`n")))
