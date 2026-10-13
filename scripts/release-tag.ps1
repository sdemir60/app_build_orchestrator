<# [yayin hatti] Surumun tag'i - YALNIZ release.yml'in publish job'i kosar, testler (ci) ve paket (package.ps1) gectikten
   SONRA: tag yalniz yayinlanan surumde bulunur. Tag testten once gitseydi kirmizi bir kosu origin'de Release'siz bir tag ve
   yanmis bir surum numarasi birakirdi (release.ps1 ayni surumu bir daha cikarmaz).
   Once release-guard.ps1 -OnMain: Version == CHANGELOG, HEAD origin'in main ucu, tag baska bir commit'te degil. Publish job'i
   yeniden denendiginde guard job'i yeniden kosmaz (yalniz dusen job'lar kosar) - kontrol bu yuzden burada tekrarlanir.
   Tag origin'de zaten bu commit'teyse (tag atildi, sonraki adim dustu, kosu yeniden denendi) dokunulmaz ve cikis 0. Yoksa HEAD'e
   annotated tag konur ve origin'e itilir. Kimligi (user.name / user.email) workflow verir: runner'da global git kimligi yoktur.
   Cikis 0 = tag origin'de bu commit'te, 1 = atilmadi (mesaj sebebi yazar). #>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
Set-Location $RepoRoot

& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'release-guard.ps1') -OnMain
if ($LASTEXITCODE -ne 0) { exit 1 }
$version = Get-BuildProp 'Version'
$tag = "v$version"
$head = git rev-parse HEAD
try { $origin = Get-OriginRelease $tag }
catch { Write-Host "release tag: cannot read origin ($($_.Exception.Message))"; exit 1 }
if ($origin.TagCommit -eq $head) { Write-Host "release tag: $tag is already on $head (re-run) - left as it is"; exit 0 }
& git tag -a $tag -m "$(Get-BuildProp 'Product') $version - release notes in CHANGELOG.md" $head
if ($LASTEXITCODE -ne 0) { Write-Host "release tag: git tag $tag failed (exit $LASTEXITCODE)"; exit 1 }
& git push origin "refs/tags/$tag"
if ($LASTEXITCODE -ne 0) { Write-Host "release tag: pushing $tag failed (exit $LASTEXITCODE)"; exit 1 }
Write-Host "release tag: $tag pushed at $head"
exit 0
