---
name: release
description: Build Orchestrator için yeni sürüm çıkar — CHANGELOG bölümünü yaz, numarayı seç, scripts/release.ps1 ile develop'tan main'e merge + tag + atomik push; CI paketleyip GitHub Releases'a yayınlar. Kullanıcı "/release", "sürüm çıkar", "yeni versiyon" dediğinde.
---

# /release

Kurallar — kaynak, numara, notun yazımı, `Version` — CLAUDE.md **"Sürüm çıkarma"** bölümündedir; burada
tekrarlanmaz. Bu skill yalnız sırayı ve komutları verir.

**Model:** günlük iş `develop`'ta, `main` yalnız sürümleri taşır. `main`'e yalnız `scripts/release.ps1` dokunur;
`main`'deki her merge commit'i bir sürüm + tag'tir (CLAUDE.md "Git").

1. **Ana proje checkout'unda** (`D:\Projects\Other\Apps\app_build_orchestrator`) `develop` üzerinde koş — worktree'de
   (`develop-ai`) DEĞİL; script `develop` dışında durur. **Ön koşullar** — script hepsini denetler; tutmayan olursa
   hiçbir şeye dokunmadan durur ve sebebini yazar:
   - `develop` push edilmiş ve güncel (`git fetch` sonrası `develop == origin/develop`); ağaçta `CHANGELOG.md` /
     `Directory.Build.props` dışında değişiklik yok.
   - **`develop` HEAD'inin `ci.yml` koşusu yeşil** (`completed` + `success`): push'tan sonra Actions'ta bitmesini
     bekle. Koşu yoksa, sürüyorsa ya da kırmızıysa script durur.
   - Yerel `main` (varsa) `origin/main`'e eşit, `origin/main` `develop`'un atası, `main` başka bir worktree'de açık
     değil; `vX.Y.Z` tag'i ne yerelde ne origin'de var.
   - Bu checkout'tan çalışan Build Orchestrator (`bin\`'den açılan kopya) kapalı: açıksa script `Version`'a dokunmadan
     durur. Tepsideki kurulu kopya (`%LOCALAPPDATA%\BuildOrchestrator.App\current`) engel değildir.
2. Numarayı ve CHANGELOG bölümünü CLAUDE.md "Sürüm çıkarma" adım 1-3'e göre hazırla (major ise kullanıcıya SOR);
   `Version`'ı script yazar. Ön koşullardan emin değilsen önce `-DryRun`: bütün guard'lar koşar; dosyaya, branch'e ve
   tag'e dokunulmaz.
3. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Version X.Y.Z`
   — guard'lar → `Version` → build + tam süit → `develop`'ta `release: vX.Y.Z` commit'i → `main`'e `--no-ff` merge
   (`merge: release vX.Y.Z`) → merge commit'ine annotated tag → `develop` `main`'e ff (develop = main) →
   `git push --atomic origin main develop vX.Y.Z` (üçü birlikte gider ya da hiçbiri). `-SkipCiCheck` yalnız
   çevrimdışı/acil durumda: CI kontrolünü atlar ve uyarır (`release.yml` tag'i yine derleyip test eder).
4. **Script durduysa** sebebi kullanıcıya ilet. Nerede durduğuna göre:
   - **Release commit'inden ÖNCE** (guard, build, test): commit/tag yok, hiçbir şey push edilmedi. En fazla
     `Directory.Build.props`'ta yeni `Version` yazılıdır; CHANGELOG ile birlikte ağaçta kalır, yeniden koşu aynı
     değeri yazar. Sebebi gider, adım 3'ü yeniden koş.
   - **Release commit'inden SONRA** (en olası: push reddedildi — fetch'ten sonra `origin/develop` ya da `origin/main`
     ilerledi; `--atomic` olduğu için origin'e hiçbir şey gitmedi): yerelde release commit'i, merge ve tag kalır.
     Script'in son `release: undo ... with:` satırındaki komutları olduğu gibi çalıştır —
     `git switch develop; git tag -d vX.Y.Z; git reset --soft <sha>; git restore --staged .; git branch -f main <sha>`
     (sha'lar yayının başladığı commit'lerdir; araya bir `fetch` girse de doğru kalır). `develop` ve `main` eski
     yerine döner, tag silinir; CHANGELOG bölümü ve `Version` çalışma ağacında değişiklik olarak kalır. Sonra
     `git pull --ff-only` (develop), CI'ın yeşilini bekle, adım 3.
5. Actions linkini ver: https://github.com/sdemir60/app_build_orchestrator/actions (`release` workflow'u). Bitince
   https://github.com/sdemir60/app_build_orchestrator/releases/latest sayfasında Setup.exe ve nupkg'ları gör.
6. **Tag push edildi ama `release.yml` düştü** (guard reddetti, `ci` kırmızı ya da `publish` düştü — `vpk pack` bir
   argümanı reddetti, GitHub API hatası): tag origin'de release'siz kalır ve `release.ps1` aynı sürümü bir daha çıkarmaz
   (`tag vX.Y.Z already exists on origin`); ertesi güne kalan bir yeniden deneme CHANGELOG tarihi yüzünden de düşer.
   Actions'taki `release` koşusunun log'una bak, sonra:
   - **Geçici hata** (ağ, GitHub API, runner) → Actions'ta *Re-run failed jobs*. Hiçbir şey yüklenmemişse güvenlidir;
     `vpk upload --merge` yarım kalmış release'i tolere eder.
   - **Kodun değişmesi gerekiyor** → iş branch'inde düzelt, `develop`'a merge + push et, CI yeşil olunca bir üst
     **patch sürümle** ileri düzelt (`/release`, X.Y.(Z+1)). GitHub Release **hiç oluşmadıysa** alternatif:
     `git push origin :refs/tags/vX.Y.Z` + `git tag -d vX.Y.Z`, CHANGELOG'daki sürüm tarihini bugüne yenile,
     `/release`'i aynı sürümle yeniden çalıştır (`main`'de tag'siz kalan önceki merge zararsızdır; yeni merge tag'i
     taşır).
