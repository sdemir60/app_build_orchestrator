# Clean Butonu Motoru (v2) — main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. `main` üzerinde açılan yeni bir session'ın ilk mesajına aşağıdaki
> "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır. 2026-08-19 tarihli merge promptunun yerini alır
> (o, artık bayat olan `feat/clean-button-engine` branch'ini adreslerdi).

| | |
|---|---|
| **Branch** | `feat/clean-button-engine-v2` (yalnız LOCAL) |
| **Plan** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-plan.md` |
| **Sonuç kaydı (v2)** | `.claude/outputs/2026-09-10-19-39-clean-button-engine-v2-results.md` |
| **Sonuç kaydı (2. tur)** | `.claude/outputs/2026-09-12-06-43-clean-post-run-refresh-and-busy-buttons.md` |
| **Sonuç kaydı (3. tur)** | `.claude/outputs/2026-09-12-08-54-clean-externals-and-step-choreography.md` |
| **Merge hedefi** | `main` |
| **Merge commit mesajı** | `merge: clean butonu motoru` |

> Branch ÜÇ tur taşır: (1) motorun kendisi; (2) Clean sonrası tazeleme + koşan bakım butonu (bitişte konsol
> korunarak Sync, düğmede amber zemin + spinner) — kararlar K-11…K-15; (3) harici projelerin de temizlenmesi,
> liste/grafın tıklama anında boşalması ve adım koreografisi (440 ms adım + 200 ms boşluk, sonra Sync) —
> kararlar K-16…K-20. Üçüncü tur ikinci turun iki kararını bilinçli olarak değiştirdi (boşaltmanın zamanı ve
> derecesi); gerekçeler 3. tur kaydında.

---

## Yapıştırılacak prompt

`feat/clean-button-engine-v2` branch'inde MaintenanceBox'taki Clean butonuna gerçek motor takıldı:
`cleanWorkspace` IPC komutu, aktif workspace'in keşfedilmiş projelerinin `bin\` ve `obj\` klasörlerini ve o
workspace'e ait `build-state.json` kayıtlarını siler (`/t:Clean` yok). Branch, güncel `main` üzerine task
sırasıyla cherry-pick edilerek kuruldu; sonuç kaydı `.claude/outputs/2026-09-10-19-39-clean-button-engine-v2-results.md`
commit haritasını, plandan sapmaları (kümülatif 9 madde), açık kararı (pill Clean sırasında amber yanmaz) ve
Optimize branch'iyle çakışma yüzeyini anlatır. Plan: `.claude/outputs/2026-08-19-17-33-clean-button-engine-plan.md`.

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra plan (K-1…K-10) ve sonuç kaydı. Kayıttaki 9 sapma BİLİNENDİR —
   onları yeniden sorma; kayıtta olmayan bir fark bulursan merge etmeden önce bana bildir.
2. **Branch'i incele.** `git log --oneline main..feat/clean-button-engine-v2` ve
   `git diff --stat main...feat/clean-button-engine-v2`. `main` bu arada ilerlediyse önce
   `git merge main` ile branch'i güncelle, çakışma varsa çöz ve süiti yeniden koş.
3. **Doğrula, iddia etme.** `git switch feat/clean-button-engine-v2` sonrası:
   ```powershell
   dotnet build BuildOrchestrator.slnx
   dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
   ```
   Tam süit yeşil görülmeden merge yok. Uygulama açıksa kapat — çalışan Supervisor kendi binary'lerini
   kilitler. Kırmızı varsa merge etme, bana raporla.
4. **Merge.** `main`'e geç, `--no-ff` ile merge et; commit mesajı: `merge: clean butonu motoru`. Sonra push.
5. **Doğrula ve temizle.** Merge'ün `main`'e geçtiğini doğruladıktan SONRA iki local branch'i sil:
   `feat/clean-button-engine-v2` (`git branch -d`) ve içeriği tamamen v2'ye taşınmış eski
   `feat/clean-button-engine` (cherry-pick'lendiği için `-d` reddeder; `git branch -D`). İkisi de yalnız
   LOCAL'dir — `git push origin --delete` GEREKMEZ. Oturum `main` üzerinde bitsin.

**Bu işe özel dikkat:** `/t:Clean` yok (K-1); build-state temizliği RootPath önekiyle workspace-scoped, `evaluation-cache.json`'a
dokunulmaz (K-2); run uçuştayken `error(cleanRejected)` (K-4); exception IPC sınırını geçmez, kilitli dosya hata
değil (K-6); silinmeyenler korunur (`packages\`, ortak OutDir, worktree havuzu + `_obj`, run logları,
`evaluation-cache.json`, `ui-state.json`).

**Ortak yüzey / merge sırası:** `feat/optimize-button-engine` de merge bekliyorsa (o da `main`'in çok
gerisindedir ve aynı yöntemle taşınmalıdır) sırayı bana sor; çakışma noktaları sonuç kaydının
"çakışma yüzeyi" bölümündedir.
