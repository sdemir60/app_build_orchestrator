---
name: release
description: Build Orchestrator için yeni sürüm çıkar — CHANGELOG bölümünü yaz, numarayı seç, scripts/release.ps1 ile develop'tan main'e merge + atomik push; GitHub test eder, paketler, tag'i koyar ve GitHub Releases'a yayınlar. Kullanıcı "/release", "sürüm çıkar", "yeni versiyon" dediğinde.
---

# /release

Kurallar — kaynak, numara, notun yazımı, `Version` — CLAUDE.md **"Sürüm çıkarma"** bölümündedir; burada
tekrarlanmaz. Bu skill yalnız sırayı ve komutları verir.

**Model:** günlük iş `develop`'ta, `main` yalnız sürümleri taşır. `main`'e yalnız `scripts/release.ps1` dokunur;
`main`'deki her merge commit'i bir sürüm denemesidir. Tag'i script atmaz: `main`'e push GitHub'da `release` workflow'unu
başlatır, testler ve paket geçince tag'i orada `release-tag.ps1` koyar (CLAUDE.md "Git"). Tag'siz bir merge yalnız
GitHub'da düşmüş denemedir (adım 6).

1. **Ana proje checkout'unda** (`D:\Projects\Other\Apps\app_build_orchestrator`) `develop` üzerinde koş — worktree'de
   (`develop-ai`) DEĞİL; script `develop` dışında durur. **Ön koşullar** — script hepsini denetler; tutmayan olursa
   hiçbir şeye dokunmadan durur ve sebebini yazar:
   - `develop` push edilmiş ve güncel (`git fetch` sonrası `develop == origin/develop`); ağaçta `CHANGELOG.md` /
     `Directory.Build.props` dışında değişiklik yok.
   - **`develop` HEAD'inin `ci.yml` koşusu yeşil** (`completed` + `success`): push'tan sonra Actions'ta bitmesini
     bekle. Koşu yoksa, sürüyorsa ya da kırmızıysa script durur.
   - Yerel `main` (varsa) `origin/main`'e eşit, `origin/main` `develop`'un atası, `main` başka bir worktree'de açık
     değil; `vX.Y.Z` tag'i ne yerelde ne origin'de var (sürüm yayınlanmamış).
   - Yayınlanacak bir şey var: `develop` `main`'in önünde ya da ağaçta yayının CHANGELOG/`Version` değişikliği. İkisi de
     yoksa önceki deneme `main`'i zaten taşımıştır ve GitHub'da düşmüştür — script "nothing to release" der (adım 6).
   - Bu checkout'tan çalışan Build Orchestrator (`bin\`'den açılan kopya) kapalı: açıksa script `Version`'a dokunmadan
     durur. Tepsideki kurulu kopya (`%LOCALAPPDATA%\BuildOrchestrator.App\current`) engel değildir.
2. Numarayı ve CHANGELOG bölümünü CLAUDE.md "Sürüm çıkarma" adım 1-3'e göre hazırla (major ise kullanıcıya SOR);
   `Version`'ı script yazar. Ön koşullardan emin değilsen önce `-DryRun`: bütün guard'lar koşar; dosyaya ve branch'e
   dokunulmaz.
3. `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/release.ps1 -Version X.Y.Z`
   — guard'lar → `Version` → build → `develop`'ta `release: vX.Y.Z` commit'i → `main`'e `--no-ff` merge
   (`merge: release vX.Y.Z`) → `develop` `main`'e ff (develop = main) → `git push --atomic origin main develop`
   (ikisi birlikte gider ya da hiçbiri). Script **tag atmaz ve testleri lokal koşmaz**: develop HEAD'inin CI koşusu
   yukarıdaki ön koşuldur; `main`'e push GitHub'da `release` workflow'unu başlatır — kontrol → testler → paket → tag →
   Release. Kırmızı biterse `release-retry` düşen job'ları bir kez kendiliğinden yeniden koşar (yine kırmızıysa adım 6).
   `-SkipCiCheck` yalnız çevrimdışı/acil durumda: CI kontrolünü atlar ve uyarır (`release` workflow'u `main`'i yine
   derleyip test eder).
4. **Script durduysa** sebebi kullanıcıya ilet. Nerede durduğuna göre:
   - **Release commit'inden ÖNCE** (guard, build): commit yok, hiçbir şey push edilmedi. En fazla
     `Directory.Build.props`'ta yeni `Version` yazılıdır; CHANGELOG ile birlikte ağaçta kalır, yeniden koşu aynı
     değeri yazar. Sebebi gider, adım 3'ü yeniden koş.
   - **Release commit'inden SONRA** (en olası: push reddedildi — fetch'ten sonra `origin/develop` ya da `origin/main`
     ilerledi; `--atomic` olduğu için origin'e hiçbir şey gitmedi): yerelde release commit'i ve merge kalır.
     Script'in bastığı kurtarma satırını (son `release: undo … with:` satırı) olduğu gibi çalıştır; komutları elle
     yazma. Satırdaki sha'lar yayının başladığı commit'lerdir, araya bir `fetch` girse de doğru kalır. `develop` ve
     `main` eski yerine döner; CHANGELOG bölümü ve `Version` çalışma ağacında değişiklik olarak kalır.
     Sonra `git pull --ff-only` (develop), CI'ın yeşilini bekle, adım 3.
   - **Push hata verdi ama script origin'e baktı:** `… but origin's main is the release merge …` derse sunucu push'u
     uygulamıştır ve yayın GitHub'da başlamıştır — kurtarma satırı basılmaz, undo YAPMA; `git fetch` ile origin'deki
     `main` ve `develop`'un yereldekilerle aynı olduğunu gör, adım 5. `… origin cannot be read …` derse kurtarma satırını
     ancak origin'e ulaşıp `main`'in mesajdaki sha OLMADIĞINI gördükten sonra çalıştır
     (`git ls-remote origin refs/heads/main`); o sha'ysa yayın başlamıştır.
5. Actions linkini ver: https://github.com/sdemir60/app_build_orchestrator/actions (`release` workflow'u; `main`'e push'la
   başlar). Bitince `vX.Y.Z` tag'i origin'dedir (`git fetch` getirir) ve
   https://github.com/sdemir60/app_build_orchestrator/releases/latest sayfasında Setup.exe ve nupkg'lar görünür.
6. **`release` workflow'u kırmızı bitti** (guard reddetti, testler kırmızı ya da paketleme/yükleme düştü) ve
   `release-retry`'ın tek yeniden denemesi de kırmızı: yayın yok, kullanıcılara bir şey gitmedi. Tag yalnız yükleme
   adımlarında düşüldüyse vardır (tag paketten sonra, yüklemeden önce atılır); aksi halde yoktur ve sürüm numarası
   yanmamıştır. `main`'de tag'siz bir merge kalır — zararsızdır. Actions'taki `release` koşusunun log'una bak, sonra:
   - **Geçici hata** (ağ, GitHub API, runner, tesadüfen düşen test) → Actions'ta koşuyu aç, *Re-run jobs* → *Re-run
     failed jobs*. Tag zaten bu commit'teyse `release-tag.ps1` ona dokunmaz; `vpk upload --merge` yarım kalmış
     release'i tolere eder.
   - **Kodun değişmesi gerekiyor** → iş branch'inde düzelt, `develop`'a merge + push et, CI yeşil olunca `/release`'i
     **aynı sürümle** yeniden çalıştır (tag yok, numara yanmadı); CHANGELOG'daki tarih bugün değilse bugüne yenile.
     Yeni merge tag'i taşır; önceki tag'siz merge `main`'de kalır. (Tag yükleme adımında düşüp origin'de kaldıysa
     numara yanmıştır: bir üst patch sürümle ileri düzelt.)
   - `develop`'ta değişiklik olmadan aynı sürümle `/release` → script "nothing to release" der ve durur: aynı kod için
     yol *Re-run failed jobs*'tır.
   - `main` bu arada yeni bir sürümle ilerlediyse eski koşunun yeniden denemesi guard'da durur (eski sürüm yenisinin
     üstüne yayınlanmaz) — yeni sürümle devam et.
