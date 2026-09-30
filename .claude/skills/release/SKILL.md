---
name: release
description: Build Orchestrator için yeni sürüm çıkar — CHANGELOG bölümünü yaz, numarayı seç, scripts/release.ps1 ile commit+tag+push; CI paketleyip GitHub Releases'a yayınlar. Kullanıcı "/release", "sürüm çıkar", "yeni versiyon" dediğinde.
---

# /release

1. `main`'de ve temiz ol; `git fetch` → `origin/main` ile eşit değilse dur ve söyle.
2. Kaynak: `git log --first-parent v<son tag>..main` merge mesajları + ilgili `.claude/outputs/*results*.md`.
3. Numara: yalnız düzeltme → patch · yeni özellik → minor · büyük dönüm noktası → major (kullanıcıya SOR).
4. `CHANGELOG.md`'nin en üstüne `## [X.Y.Z] - <bugün>` bölümü: kategoriler Added · Changed · Fixed · Performance ·
   Removed sırasıyla, boşu yazma; maddeler İngilizce, düz metin (markdown işareti yok), kısa, kullanıcının gördüğü
   özellik; iç terim, dosya/sınıf adı yok; küçük işler tek satırda. Her maddeyi o anki koda göre doğrula.
5. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Version X.Y.Z`
   — guard'lar, `Version`, build + tam süit, `release: vX.Y.Z` commit'i, annotated tag, push. Düşerse sebebi
   kullanıcıya ilet; hiçbir şey push edilmemiştir.
6. Actions linkini ver: https://github.com/sdemir60/app_build_orchestrator/actions — `release` workflow'u ~15 dk.
   Bitince https://github.com/sdemir60/app_build_orchestrator/releases/latest sayfasında Setup.exe ve nupkg'ları gör.
7. Yayınlanmış bir sürümün notu yalnız yanlışsa düzeltilir.
