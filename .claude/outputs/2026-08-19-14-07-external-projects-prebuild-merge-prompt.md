# Harici Projeler Ön-Derleme — Main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. İş bittiğinde `main` üzerinde açılan yeni bir session'ın ilk
> mesajına aşağıdaki "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır.

| | |
|---|---|
| **Branch** | `feat/external-projects-prebuild` |
| **Plan** | `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-opus-prompt.md` |
| **Merge hedefi** | `main` |
| **Merge commit mesajı** | `merge: harici projeler on-derleme` |

---

## Yapıştırılacak prompt

`feat/external-projects-prebuild` branch'inde harici projelerin (repo dışında yaşayan, müşteriye özel
projeler) build öncesi VCS'ten güncellenip sırayla derlenmesi özelliği geliştirildi: Ayarlar'da EXTERNAL
PROJECTS listesi, git ff-only / TFVC get-latest güncellemesi, dirty kapısı, incremental karar ve node
panelinde ayrı "External" katmanı. Geliştirme şu iki dosyaya göre yapıldı:

- Plan: `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-plan.md`
- Uygulama promptu: `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-opus-prompt.md`

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra yukarıdaki plan ve uygulama promptu. Planın kabul/doğrulama
   senaryoları ile bağlayıcı kararları merge'ün ölçütüdür — kararı onlara göre ver.
2. **Branch'i incele.** `git log --oneline main..feat/external-projects-prebuild` ve
   `git diff --stat main...feat/external-projects-prebuild` ile ne geldiğini çıkar. Planın faz/task
   listesiyle karşılaştır: **eksik kalan faz var mı**, plan dışına taşan değişiklik var mı? Varsa merge
   etmeden önce bana bildir.
3. **Doğrula, iddia etme.** `git switch feat/external-projects-prebuild` sonrası:
   ```powershell
   dotnet build BuildOrchestrator.slnx
   dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
   ```
   Tam süit (token / motion / kaynak guard'ları dahil) **yeşil görülmeden** merge yok. Uygulama açıksa
   kapat — çalışan Supervisor kendi binary'lerini kilitler. Kırmızı varsa merge etme, bana raporla.
4. **Doküman kontrolü.** Planın doküman güncelleme listesi uygulanmış mı; ARCHITECTURE.md / README.md artık
   yanlış bir şey söylüyor mu? Eksik varsa merge'den ÖNCE aynı branch'te tamamla.
5. **Merge.** `main`'e geç, `--no-ff` ile merge et; commit mesajı: `merge: harici projeler on-derleme`.
   Sonra push.
6. **Doğrula ve temizle.** Merge'ün `main`'e geçtiğini doğruladıktan **sonra** branch'i local ve remote'tan
   sil. Oturum `main` üzerinde bitsin.

**Bu işe özel dikkat:** plan beş fazlıdır ve Core / Contracts / Supervisor / App'in dördüne birden dokunur.
Merge öncesi şu değişmezleri özellikle doğrula:

- Mutasyon yapan git yüzeyi yalnız `Core/Externals` içinde mi, kaynak guard'ı bunu çitliyor mu (D7).
- Ana repoda `checkout` / `switch` / `pull` / `reset` hâlâ hiç koşmuyor mu.
- OutDir'e dokunulmamış, "değişti mi" kararı yalnız kaynak sinyalinden mi.
- Planlama Core'da mı kalmış; Supervisor yalnız bağlayıp yürütüyor mu.
- `vswhere` çağrısı `VsWhereLocator`'a çıkarılmış ve `MsBuildResolver` da onu kullanıyor mu (kopya yasak).

Bu özellik ARCHITECTURE §20'deki "one repository at a time" ifadesini değiştirir — doküman changelog
biriktirmeden, ilgili bölümde **yerinde yeniden yazılmış** olmalı.
