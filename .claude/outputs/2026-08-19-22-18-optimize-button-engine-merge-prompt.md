# Optimize Butonu Motoru — Main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. İş bittiğinde `main` üzerinde açılan yeni bir session'ın ilk
> mesajına aşağıdaki "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır.

| | |
|---|---|
| **Branch** | `feat/optimize-button-engine` |
| **Plan** | `.claude/outputs/2026-08-19-22-18-optimize-button-engine-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-22-18-optimize-button-engine-opus-prompt.md` |
| **Merge hedefi** | `main` |
| **Merge commit mesajı** | `merge: optimize butonu motoru` |

---

## Yapıştırılacak prompt

`feat/optimize-button-engine` branch'inde MaintenanceBox'taki Optimize butonuna gerçek motor takıldı:
`optimizeWorkspace` IPC komutu bir workspace doktoru olarak eksik NuGet paketlerini restore eder,
restore'un çözemediği kırık referansları proje + dosya adıyla raporlar, build-kırıcı stale `obj` NuGet
artıklarını siler, ölü cache girdilerini ve öksüz `.tmp`'leri budar. Geliştirme şu iki dosyaya göre yapıldı:

- Plan: `.claude/outputs/2026-08-19-22-18-optimize-button-engine-plan.md`
- Uygulama promptu: `.claude/outputs/2026-08-19-22-18-optimize-button-engine-opus-prompt.md`

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra yukarıdaki plan ve uygulama promptu. Planın bağlayıcı kararları
   (K-1…K-14) ve edge-case dizini merge'ün ölçütüdür.
2. **Branch'i incele.** `git log --oneline main..feat/optimize-button-engine` ve
   `git diff --stat main...feat/optimize-button-engine` ile ne geldiğini çıkar. Planın task listesiyle
   karşılaştır: **eksik kalan task var mı**, plan dışına taşan değişiklik var mı (v1 sonrası adaylar bu işte
   YAPILMAYACAKTI)? Varsa merge etmeden önce bana bildir.
3. **Doğrula, iddia etme.** `git switch feat/optimize-button-engine` sonrası:
   ```powershell
   dotnet build BuildOrchestrator.slnx
   dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
   ```
   Tam süit (token / motion / kaynak guard'ları dahil) **yeşil görülmeden** merge yok. Uygulama açıksa
   kapat — çalışan Supervisor kendi binary'lerini kilitler. Kırmızı varsa merge etme, bana raporla.
4. **Doküman kontrolü.** Planın doküman güncelleme listesi uygulanmış mı; ARCHITECTURE.md / README.md artık
   yanlış bir şey söylüyor mu (Optimize butonu artık disabled değil; `StaleObjDetector` uyarısının kökü
   temizleniyor)? Eksik varsa merge'den ÖNCE aynı branch'te tamamla.
5. **Merge.** `main`'e geç, `--no-ff` ile merge et; commit mesajı: `merge: optimize butonu motoru`. Sonra
   push.
6. **Doğrula ve temizle.** Merge'ün `main`'e geçtiğini doğruladıktan **sonra** branch'i local ve remote'tan
   sil. Oturum `main` üzerinde bitsin.

**Bu işe özel dikkat:**

- NuGet paket yolu ayrımı tek kaynaktan mı geliyor — `HintPathClassifier.IsNuGetPackagesPath` (K-2), literal
  yeniden yazılmamış mı (kopya yasak).
- `packages.config` İÇERİĞİ parse edilmemiş, tespit HintPath-varlık tabanlı mı (K-1).
- Dokunulmayanlar korunmuş mu: global NuGet cache'leri, `NuGet.config`, git (Optimize hiç git komutu
  koşmaz), worktree havuzu + `_obj`, bin/OutDir, run logları, `ui-state.json`.
- Restore yüzeyi mevcut kanıtlanmış per-proje `-t:restore` sözleşmesini yeniden mi kullanıyor.
- Servis yapısı düz kalmış mı — "yeni sorun sınıfı = yeni private adım + sayaç".

Bu iş **Clean butonu motoruyla** aynı yüzeye dokunur (MaintenanceBox, IPC komut/event kuyrukları).
`feat/clean-button-engine` de merge bekliyorsa merge sırasını bana sor; ikinci merge'de çakışmaları bu
planın "Clean planıyla paralel yürütme koordinasyonu" bölümüne göre çöz.
