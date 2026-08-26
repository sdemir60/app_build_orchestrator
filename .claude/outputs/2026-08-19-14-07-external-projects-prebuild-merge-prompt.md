# Harici Projeler Ön-Derleme — Main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. İş bittiğinde `main` üzerinde açılan yeni bir session'ın ilk
> mesajına aşağıdaki "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır.

| | |
|---|---|
| **Branch** | `feat/external-projects-prebuild` |
| **Plan** | `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-opus-prompt.md` |
| **Sonuç kaydı** | `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-results.md` |
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
- **Sonuç kaydı:** `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-results.md` — commit
  haritası, planla arasındaki **4 bilinçli sapma**, öteki branch'lerle çakışma yüzeyi ve bilinen açıklar.

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra yukarıdaki plan, uygulama promptu ve **sonuç kaydı**. Planın
   kabul/doğrulama senaryoları ile bağlayıcı kararları (D1–D13) merge'ün ölçütüdür; sonuç kaydı ise planla
   kodun AYRILDIĞI yerleri gerekçesiyle sayar.
2. **Branch'i incele.** `git log --oneline main..feat/external-projects-prebuild` ve
   `git diff --stat main...feat/external-projects-prebuild` ile ne geldiğini çıkar. Planın faz/task
   listesiyle karşılaştır: **eksik kalan faz var mı**, plan dışına taşan değişiklik var mı? **Önce sonuç
   kaydının "Plandan sapmalar" bölümüne bak** — oradaki dört sapma BİLİNENDİR, onları yeniden sorma; yalnız
   orada açıklanmayan bir fark bulursan merge etmeden önce bana bildir.
3. **Manuel duman testi.** Bu iş uçtan uca ELDE denenmedi (sonuç kaydı, "Bilinen açıklar" 1). Planın kabul
   senaryoları 3–8 gerçek bir git harici projesi ve mümkünse bir TFVC workspace'i ister. Bana bu testin
   yapılıp yapılmadığını SOR; yapılmadıysa merge etmeden önce birlikte koşalım — süit yeşil olması bu
   özelliğin kullanıcının kendi harici projeleriyle çalıştığını kanıtlamaz.
4. **Doğrula, iddia etme.** `git switch feat/external-projects-prebuild` sonrası:
   ```powershell
   dotnet build BuildOrchestrator.slnx
   dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
   ```
   Tam süit (token / motion / kaynak guard'ları dahil) **yeşil görülmeden** merge yok. Uygulama açıksa
   kapat — çalışan Supervisor kendi binary'lerini kilitler. Kırmızı varsa merge etme, bana raporla.
5. **Doküman kontrolü.** Planın doküman güncelleme listesi uygulanmış mı; ARCHITECTURE.md / README.md artık
   yanlış bir şey söylüyor mu? Eksik varsa merge'den ÖNCE aynı branch'te tamamla.
6. **Merge.** `main`'e geç, `--no-ff` ile merge et; commit mesajı: `merge: harici projeler on-derleme`.
   Sonra push.
7. **Doğrula ve temizle.** Merge'ün `main`'e geçtiğini doğruladıktan **sonra** branch'i sil. Branch yalnız
   LOCAL'dir (remote'a hiç push edilmedi), yani `git push origin --delete` GEREKMEZ — denersen hata alırsın.
   Oturum `main` üzerinde bitsin.

**Bu işe özel dikkat:** plan beş fazlıdır ve Core / Contracts / Supervisor / App'in dördüne birden dokunur.
Merge öncesi şu değişmezleri özellikle doğrula:

- Mutasyon yapan git yüzeyi yalnız `Core/Externals` içinde mi, kaynak guard'ı bunu çitliyor mu (D7).
- Ana repoda `checkout` / `switch` / `pull` / `reset` hâlâ hiç koşmuyor mu.
- OutDir'e dokunulmamış, "değişti mi" kararı yalnız kaynak sinyalinden mi.
- Planlama Core'da mı kalmış; Supervisor yalnız bağlayıp yürütüyor mu.
- `vswhere` çağrısı `VsWhereLocator`'a çıkarılmış ve `MsBuildResolver` da onu kullanıyor mu (kopya yasak).

Bu özellik ARCHITECTURE §20'deki "one repository at a time" ifadesini değiştirir — doküman changelog
biriktirmeden, ilgili bölümde **yerinde yeniden yazılmış** olmalı.

**Ortak yüzey / merge sırası:**

`main`'e merge bekleyen DÖRT branch var (`feat/external-projects-prebuild`, `feat/clean-button-engine`,
`feat/optimize-button-engine`, `feat/tray-build-animation`) ve dördü de `RunViewModel.cs` ile
ARCHITECTURE.md / README.md'ye dokunuyor. İlk merge temiz geçer; sonrakiler elle çakışma çözer.

Bu branch için sıra serbesttir, ama ikinci ya da sonraki merge olursa şu beş nokta gözden kaçmaya açıktır —
tam listesi ve gerekçeleri sonuç kaydının "Öteki branch'lerle çakışma yüzeyi" bölümündedir:

- **`GitCommandExecutor` → `Core/Processes/CommandLineTool.cs` taşındı** ve araç adı parametreleşti.
  `GitService.cs` / `WorktreeManager.cs`'te 23 çağrı yeri satır satır değişti. Çakışmayı eski adı geri
  getirerek çözme — `TfvcService` yeni yüzeyi kullanıyor, branch kırılır.
- `MsBuildInvokeRequest` kuyruk alanı (`ExternalTarget`) ve argüman seçiminin `MsBuildArguments.PlanFor`'a
  taşınması — Optimize branch'i de `MsBuildInvoker.cs`'e dokunuyor.
- `RunCoordinator`: `RunPlan.Externals`, planner catch filtresi, worker spawn öncesi harici faz ve kapanış
  sayaçlarındaki `+ external*` terimleri.
- `WorkspaceServices.Default` bu branch'te 6. bir ctor argümanı veriyor; Clean branch'i AYNI record'a 4. bir
  parametre eklemiş — ikisi de korunmalı.
- `ProjectRow.xaml.cs` hem burada (sha yuvası) hem tray branch'inde değişti.
