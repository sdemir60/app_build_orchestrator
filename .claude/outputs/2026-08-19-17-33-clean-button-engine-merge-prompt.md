# Clean Butonu Motoru — Main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. İş bittiğinde `main` üzerinde açılan yeni bir session'ın ilk
> mesajına aşağıdaki "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır.

| | |
|---|---|
| **Branch** | `feat/clean-button-engine` |
| **Plan** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-opus-prompt.md` |
| **Merge hedefi** | `main` |
| **Merge commit mesajı** | `merge: clean butonu motoru` |

---

## Yapıştırılacak prompt

`feat/clean-button-engine` branch'inde MaintenanceBox'taki Clean butonuna gerçek motor takıldı:
`cleanWorkspace` IPC komutu, aktif workspace'in keşfedilmiş projelerinin `bin\` ve `obj\` klasörlerini ve o
workspace'e ait `build-state.json` kayıtlarını siler. Geliştirme şu iki dosyaya göre yapıldı:

- Plan: `.claude/outputs/2026-08-19-17-33-clean-button-engine-plan.md`
- Uygulama promptu: `.claude/outputs/2026-08-19-17-33-clean-button-engine-opus-prompt.md`

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra yukarıdaki plan ve uygulama promptu. Planın bağlayıcı kararları
   (K-1…K-10) ve edge-case dizini merge'ün ölçütüdür.
2. **Branch'i incele.** `git log --oneline main..feat/clean-button-engine` ve
   `git diff --stat main...feat/clean-button-engine` ile ne geldiğini çıkar. Planın task listesiyle
   karşılaştır: **eksik kalan task var mı**, plan dışına taşan değişiklik var mı? Varsa merge etmeden önce
   bana bildir.
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
6. **Doğrula ve temizle.** Merge'ün `main`'e geçtiğini doğruladıktan **sonra** branch'i local ve remote'tan
   sil. Oturum `main` üzerinde bitsin.

**Bu işe özel dikkat:**

- `/t:Clean` KULLANILMAMIŞ olmalı (K-1) — yalnız dosya sistemi silme.
- build-state temizliği RootPath önekiyle workspace-scoped mü (K-2); `evaluation-cache.json`'a
  dokunulmamış mı.
- Run uçuştayken `cleanWorkspace` reddediliyor mu — `error(cleanRejected)` (K-4).
- Exception IPC sınırını geçmiyor mu; kilitli dosya hata değil, sayaç + warn satırı mı (K-6).
- Silinmeyenler korunmuş mu: `packages\`, ortak OutDir, worktree havuzu (`_obj` dahil), run logları,
  `.tmp`'ler, `evaluation-cache.json`, `ui-state.json`.

Bu iş **Optimize butonu motoruyla** aynı yüzeye dokunur (MaintenanceBox, IPC komut/event kuyrukları).
`feat/optimize-button-engine` de merge bekliyorsa merge sırasını bana sor; ikinci merge'de çakışmaları
Optimize planının "Clean planıyla paralel yürütme koordinasyonu" bölümüne göre çöz.
