# Optimize Butonu Motoru (v2) — main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. `main` üzerinde açılan yeni bir session'ın ilk mesajına aşağıdaki
> "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır. 2026-08-19 tarihli merge promptunun yerini alır
> (o, artık bayat olan `feat/optimize-button-engine` branch'ini adreslerdi).

| | |
|---|---|
| **Branch** | `feat/optimize-button-engine-v2` (local + `origin`'e push edildi) |
| **Plan** | `.claude/outputs/2026-08-19-22-18-optimize-button-engine-plan.md` |
| **Sonuç kaydı (v2)** | `.claude/outputs/2026-09-12-18-54-optimize-button-engine-v2-results.md` |
| **Merge hedefi** | `main` |
| **Merge commit mesajı** | `merge: optimize butonu motoru` |

---

## Yapıştırılacak prompt

`feat/optimize-button-engine-v2` branch'inde MaintenanceBox'taki Optimize butonuna gerçek motor takıldı:
`optimizeWorkspace` IPC komutu bir workspace doktoru olarak eksik NuGet paketlerini restore eder, restore'un
çözemediği kırık referansları proje + dosya adıyla raporlar, old-style projelerdeki build-kırıcı stale `obj`
NuGet artıklarını siler, üç kalıcı defterdeki ölü girdileri budar ve öksüz `.tmp` artıklarını süpürür. Branch,
güncel `main` üzerine task sırasıyla taşınarak kuruldu. Sonuç kaydı
`.claude/outputs/2026-09-12-18-54-optimize-button-engine-v2-results.md` commit haritasını, plandan sapmaları
(kümülatif 15 madde) ve cherry-pick'in iki sessiz tuzağını anlatır. Plan:
`.claude/outputs/2026-08-19-22-18-optimize-button-engine-plan.md`.

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra plan (K-1…K-14) ve sonuç kaydı. Kayıttaki 15 sapma BİLİNENDİR —
   onları yeniden sorma; kayıtta olmayan bir fark bulursan merge etmeden önce bildir.
2. **Branch'i incele.** `git log --oneline main..feat/optimize-button-engine-v2` ve
   `git diff --stat main...feat/optimize-button-engine-v2`. `main` bu arada ilerlediyse önce
   `git merge main` ile branch'i güncelle, çakışma varsa çöz ve süiti yeniden koş.
3. **Doğrula, iddia etme.** `git switch feat/optimize-button-engine-v2` sonrası:
   ```powershell
   dotnet build BuildOrchestrator.slnx
   dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
   ```
   Tam süit yeşil görülmeden merge yok. Uygulama açıksa kapat — çalışan Supervisor kendi binary'lerini
   kilitler. Kırmızı varsa merge etme, bana raporla.
4. **Merge.** `main`'e geç, `--no-ff` ile merge et; commit mesajı: `merge: optimize butonu motoru`. Sonra push.
5. **Doğrula ve temizle.** Merge'ün `main`'e geçtiğini doğruladıktan SONRA branch'leri sil:
   `feat/optimize-button-engine-v2` (local `-d` + `git push origin --delete`) ve içeriği tamamen v2'ye taşınmış
   eski `feat/optimize-button-engine` (yalnız LOCAL; taşıma cherry-pick değil elle uyarlama olduğu için `-d`
   reddeder, `git branch -D`). Oturum `main` üzerinde bitsin.

**Bu işe özel dikkat:**

- **Hedef guard'ı yerinde mi:** `The_invoker_never_picks_the_build_target_itself`. Bu guard, taşımada
  gerçekten olan bir sessiz kırılmayı kapatıyor — `MsBuildInvoker` build argümanlarını yalnız
  `MsBuildArguments.PlanFor` üzerinden almalı; doğrudan `MsBuildArguments.Build(` çağrısı satır menüsünün
  Rebuild/Clean hedefini düşürür ve HİÇBİR davranış testi kırmızı vermez.
- `RunCoordinator`'da TEK bir `IsRunActive` olmalı (cherry-pick ikinci bir tane yazmaya çalışıyordu).
- NuGet paket yolu ayrımı tek kaynaktan mı geliyor — `HintPathClassifier.IsNuGetPackagesPath` /
  `IsUnderBin` (K-2), literal yeniden yazılmamış mı.
- `packages.config` İÇERİĞİ parse edilmemiş, tespit HintPath-varlık tabanlı mı (K-1).
- SDK-style projede stale-obj SİLİNMEMELİ (K-7, bloklayıcı kural) — `Stale_obj_leftovers_are_removed_only_from_legacy_projects`.
- Dokunulmayanlar korunmuş mu: global NuGet cache'leri, `NuGet.config`, git (Optimize hiç git komutu koşmaz),
  worktree havuzu + `_obj`, bin/OutDir, run logları, `ui-state.json`.
- Bayt biçimleyici TEK kaynakta mı (`Core/Formatting/ByteFormat`) — `CleanWorkspaceService.FormatBytes`
  silindi, üç çağıran ona bağlandı ve Clean'in görünen metni DEĞİŞMEDİ.
- Test tarafında kopya kalmamış mı: `SupervisorHostHarness` ve `NdjsonWire` tek yerde, `CleanDispatchTests`
  kendi `ParseWire`'ını taşımıyor.

**Ortak yüzey / merge sırası:** `feat/tray-build-animation-v2` de merge bekliyor ve `NoSleepPollTests.AllowedSleeps`
ile `RunViewModel`'e dokunuyor. İkinci merge olan taraf o iki küçük çakışmayı çözer; sırayı bana sor.
