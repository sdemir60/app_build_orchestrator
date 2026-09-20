# Aşama girişi — kullanıcı testlerinden çıkan düzeltmeler merge edildi

Branch: `main` (her şey merge + push edildi, çalışma branch'leri silindi). Filtrelenmiş tam süit yeşil.

## Özetler

- `.claude/summaries/2026-09-18-09-33-sync-build-flow-design-decided.md` — tasarım oturumu: kararlar, bağlayıcı
  spec, Faz 1 planı, çatı branch yapısı.
- `.claude/summaries/2026-09-18-23-34-sync-build-flow-phase1-done.md` — Faz 1 (kümülatif renk modeli) uygulaması.
- `.claude/summaries/2026-09-19-07-06-sync-build-flow-phase2-done.md` — Faz 2 (tek ağaç ve branch).
- `.claude/outputs/2026-09-19-14-49-sync-build-behaviour-test-guide.md` — sync/build davranış rehberi (tarihsel;
  §9'daki fark listesinin bir kısmı bu oturumda kapandı).
- `.claude/outputs/2026-09-19-18-32-sync-graph-polish-plan.md` — bu oturumun ilk TDD planı (8 task).

## Nerede kaldık

Kullanıcının testlerinden çıkan kusurlar `main`'de düzeltildi: sessiz Sync'in biten satırları tazelemesi,
graph kamerasının gerçekten fit olması (yeniden kurulumda, boş alan tıklamasında ve Build başlarken), elle
Sync / branch değişiminde ekranın baştan açılması, döngü grubunun zaman kipinde yeşil olabilmesi, filtre
açıkken Build'de grafın filtreyi askıya alması, konsolun native caret'inin gizlenmesi, git retlerinin amber
konsol satırı + akışta Warn satırı olması, kanıtlı hatalı upstream'in artık cascade başlatmaması (ve bayat
bağımlılık notunun satırı kilitlememesi), karar etiketlerinden yaşların kalkması, nefes katmanının satır
genişliğini kaplaması, aksiyon barından ○ "To build" chip'inin kaldırılması. ARCHITECTURE.md ve README.md
bunlara göre güncellendi.

Kullanıcı yeni bir oturumda uygulamayı test etmeye devam edecek. Buradan devam edilecek.
