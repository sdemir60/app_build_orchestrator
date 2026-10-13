<# [yayin hatti] Surumun tag'i - YALNIZ release.yml'in publish job'i kosar, testler (ci) ve paket (package.ps1) gectikten
   SONRA, yuklemeden ONCE: tag yalniz testleri ve paketi gecmis surume konur. Tag testten once gitseydi kirmizi bir kosu origin'de
   Release'siz bir tag ve yanmis bir surum numarasi birakirdi (release.ps1 tag'i olan surumu bir daha cikarmaz).
   Once yayin kontrolu (Get-MainRelease - guard job'inin -OnMain kontrolunun aynisi, ayni process'te): yeniden denenen publish
   job'i guard job'ini kosmaz, kontrol bu yuzden burada tekrarlanir. Tag origin'de zaten bu commit'teyse (tag atildi, sonraki adim
   dustu, kosu yeniden denendi) dokunulmaz, cikis 0. Yoksa HEAD'e annotated tag konur ve origin'e itilir. Tag'i atan GitHub'in bot
   kimligidir: runner'da git kimligi tanimsizdir; kimlik ve mesaj tag'in ayrintisidir, burada durur.
   Cikis 0 = tag origin'de bu commit'te, 1 = atilmadi (mesaj sebebi yazar). #>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'release-common.ps1')
Set-Location $RepoRoot

$release = Get-MainRelease
if ($release.Problems.Count -gt 0) { $release.Problems | ForEach-Object { Write-Host "release tag: $_" }; exit 1 }
$tag = $release.Tag
if ($release.Tagged) { Write-Host "release tag: $tag is already on $($release.Head) (re-run) - left as it is"; exit 0 }
& git -c 'user.name=github-actions[bot]' -c 'user.email=41898282+github-actions[bot]@users.noreply.github.com' tag -a $tag -m "$(Get-BuildProp 'Product') $(Get-BuildProp 'Version') - release notes in CHANGELOG.md" $release.Head
if ($LASTEXITCODE -ne 0) { Write-Host "release tag: git tag $tag failed (exit $LASTEXITCODE)"; exit 1 }
& git push origin "refs/tags/$tag"
if ($LASTEXITCODE -ne 0) { Write-Host "release tag: pushing $tag failed (exit $LASTEXITCODE)"; exit 1 }
Write-Host "release tag: $tag pushed at $($release.Head)"
exit 0
