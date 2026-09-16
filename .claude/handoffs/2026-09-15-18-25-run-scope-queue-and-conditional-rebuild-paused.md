# Aşama girişi — koşu kapsamı, kuyruk rengi ve koşullu yeniden derleme (duraklatıldı)

**İlgili dosyalar**
- `.claude/outputs/2026-09-15-16-06-run-scope-and-queued-colour-investigation.md` — inceleme: sarıya dönen node'ların kök nedenleri (A-D), kanıt satırları.
- `.claude/outputs/2026-09-15-17-04-run-scope-queue-and-conditional-rebuild-plan.md` — onaylı plan (hedef davranış, etiket tablosu, Task 1-5).
- `.superpowers/sdd/2026-09-15-17-04-run-scope-queue-and-conditional-rebuild-plan/progress.md` — ilerleme kaydı (ruling'ler, ertelenen küçük bulgular); görev brief'leri ve raporlar aynı klasörde.
- `.claude/outputs/2026-09-14-23-43-design-v1-17-v1-18-application-plan.md` — önceki iş (v1.17/v1.18 tasarım uygulaması, `main`'e merge edildi).

**Nerede kaldık**
Branch `fix/run-scope-queue-and-conditional-rebuild` (merge edilmedi). Task 1 (kuyruk rengi koşunun planından) tamam ve review'dan temiz geçti (`8eb3b68`, `cb2c2db`). Task 2 (Resolve: kuyruk yalnız döngü üyeleri, kapsam dışı projeler gri ve sayaçsız + tek proje koşusunda sayaçların şişmesi) başlatılmıştı, kod değişikliği yapılmadan durduruldu — buradan, Task 2'den yeniden başlanacak. Sonra Task 3 (koşullu yeniden derleme), Task 4 (dalga + `affected · up to date · just now` etiketi + genişleyen yuva), Task 5 (notlar, doküman, tam süit), final review ve `main`'e merge.

Buradan devam edilecek.
