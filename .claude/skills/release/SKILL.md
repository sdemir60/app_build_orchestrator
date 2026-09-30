---
name: release
description: Build Orchestrator için yeni sürüm çıkar — CHANGELOG bölümünü yaz, numarayı seç, scripts/release.ps1 ile commit+tag+push; CI paketleyip GitHub Releases'a yayınlar. Kullanıcı "/release", "sürüm çıkar", "yeni versiyon" dediğinde.
---

# /release

Kurallar — kaynak, numara, notun yazımı, `Version` — CLAUDE.md **"Sürüm çıkarma"** bölümündedir; burada
tekrarlanmaz. Bu skill yalnız sırayı ve komutları verir.

1. **Ana proje checkout'unda** (`D:\Projects\Other\Apps\app_build_orchestrator`) `main` üzerinde koş — worktree'de
   (`main-ai`) DEĞİL: `main` orada açılamaz ve script `main` dışında durur. Ağaç temiz olmalı; `git fetch` →
   `origin/main` ile eşit değilse dur ve söyle.
2. Numarayı ve CHANGELOG bölümünü CLAUDE.md "Sürüm çıkarma" adım 1-3'e göre hazırla (major ise kullanıcıya SOR);
   `Version`'ı script yazar.
3. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Version X.Y.Z`
   — guard'lar, `Version`, build + tam süit, `release: vX.Y.Z` commit'i, annotated tag, atomik push (`main` ve tag
   ya birlikte gider ya hiçbiri). Düşerse sebebi kullanıcıya ilet; hiçbir şey push edilmemiştir.
4. **Push reddedilirse** (fetch'ten sonra `origin/main` ilerlemiş): yerelde `release:` commit'i ve tag kalır, origin'e
   hiçbiri gitmemiştir. Kurtarma — `git fetch`/`pull`'dan ÖNCE (yerel `origin/main` henüz yayının başladığı
   commit'tir): `git tag -d vX.Y.Z` → `git reset --soft origin/main`. CHANGELOG/props değişikliği çalışma ağacında
   kalır; sonra `git pull --ff-only` ve adım 3 yeniden.
5. Actions linkini ver: https://github.com/sdemir60/app_build_orchestrator/actions (`release` workflow'u). Bitince
   https://github.com/sdemir60/app_build_orchestrator/releases/latest sayfasında Setup.exe ve nupkg'ları gör.
