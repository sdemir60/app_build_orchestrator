# Clean Butonu Motoru — Main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. İş bittiğinde `main` üzerinde açılan yeni bir session'ın ilk
> mesajına aşağıdaki "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır.

| | |
|---|---|
| **Branch** | `feat/clean-button-engine` |
| **Plan** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-opus-prompt.md` |
| **Sonuç kaydı** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-results.md` |
| **Merge hedefi** | `main` |
| **Merge commit mesajı** | `merge: clean butonu motoru` |

---

## Yapıştırılacak prompt

`feat/clean-button-engine` branch'inde MaintenanceBox'taki Clean butonuna gerçek motor takıldı:
`cleanWorkspace` IPC komutu, aktif workspace'in keşfedilmiş projelerinin `bin\` ve `obj\` klasörlerini ve o
workspace'e ait `build-state.json` kayıtlarını siler. Geliştirme şu iki dosyaya göre yapıldı:

- Plan: `.claude/outputs/2026-08-19-17-33-clean-button-engine-plan.md`
- Uygulama promptu: `.claude/outputs/2026-08-19-17-33-clean-button-engine-opus-prompt.md`
- **Sonuç kaydı:** `.claude/outputs/2026-08-19-17-33-clean-button-engine-results.md` — commit haritası,
  kararların hangi dosyaya düştüğü, planla arasındaki **4 sapma** ve öteki branch'lerle çakışma yüzeyi.

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra yukarıdaki plan, uygulama promptu ve **sonuç kaydı**. Planın
   bağlayıcı kararları (K-1…K-10) ve edge-case dizini merge'ün ölçütüdür; sonuç kaydı bunların branch'te
   nereye düştüğünü söyler.
2. **Branch'i incele.** `git log --oneline main..feat/clean-button-engine` ve
   `git diff --stat main...feat/clean-button-engine` ile ne geldiğini çıkar. Planın task listesiyle
   karşılaştır: **eksik kalan task var mı**, plan dışına taşan değişiklik var mı? Sonuç kaydındaki 4 sapma
   BİLİNENDİR — onları yeniden sorma; kayıtta olmayan bir fark bulursan merge etmeden önce bana bildir.
3. **Doğrula, iddia etme.** `git switch feat/clean-button-engine` sonrası:
   ```powershell
   dotnet build BuildOrchestrator.slnx
   dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
   ```
   Tam süit (token / motion / kaynak guard'ları dahil) **yeşil görülmeden** merge yok. Uygulama açıksa
   kapat — çalışan Supervisor kendi binary'lerini kilitler. Kırmızı varsa merge etme, bana raporla.
4. **Doküman kontrolü.** Planın doküman güncelleme listesi uygulanmış mı; ARCHITECTURE.md / README.md artık
   yanlış bir şey söylüyor mu (Clean butonu artık disabled değil)? Eksik varsa merge'den ÖNCE aynı
   branch'te tamamla.
5. **Merge.** `main`'e geç, `--no-ff` ile merge et; commit mesajı: `merge: clean butonu motoru`. Sonra push.
6. **Doğrula ve temizle.** Merge'ün `main`'e geçtiğini doğruladıktan **sonra** branch'i sil. Branch yalnız
   LOCAL'dir (remote'a hiç push edilmedi), yani `git push origin --delete` GEREKMEZ — denersen hata alırsın.
   Oturum `main` üzerinde bitsin.

**Bu işe özel dikkat:**

- `/t:Clean` KULLANILMAMIŞ olmalı (K-1) — yalnız dosya sistemi silme.
- build-state temizliği RootPath önekiyle workspace-scoped mü (K-2); `evaluation-cache.json`'a
  dokunulmamış mı.
- Run uçuştayken `cleanWorkspace` reddediliyor mu — `error(cleanRejected)` (K-4).
- Exception IPC sınırını geçmiyor mu; kilitli dosya hata değil, sayaç + warn satırı mı (K-6).
- Silinmeyenler korunmuş mu: `packages\`, ortak OutDir, worktree havuzu (`_obj` dahil), run logları,
  `.tmp`'ler, `evaluation-cache.json`, `ui-state.json`.

**Ortak yüzey / merge sırası:**

Bu iş **Optimize butonu motoruyla** aynı yüzeye dokunur (MaintenanceBox, IPC komut/event kuyrukları).
`feat/optimize-button-engine` de merge bekliyorsa merge sırasını bana sor; ikinci merge'de çakışmaları
Optimize planının "Clean planıyla paralel yürütme koordinasyonu" bölümüne göre çöz. Çakışması beklenen
noktaların tam listesi sonuç kaydının "Öteki branch'lerle çakışma yüzeyi" bölümündedir. İki tanesi kolayca
gözden kaçar:

- `NoSleepPollTests.AllowedSleeps` sözlüğüne Clean bir satır ekledi (`CleanWorkspaceService.cs` = 1) —
  gerekçesi `BuildStateStore` satırıyla aynı. Optimize da bir retry backoff'u getirirse AYNI sözlüğe
  dokunacak; çakışmayı satır SİLEREK değil, iki satırı da koruyarak çöz.
- `WorkspaceServices` record'u Clean ile 4. bir parametre (`Func<string, CleanWorkspaceService> Clean`)
  kazandı; onu elle kuran her test bu parametreyi verir.
