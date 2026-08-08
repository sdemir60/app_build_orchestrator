# Devir — 2026-08-08 21:18 — **Quiet graph: görsel turlar kapandı**

## Nerede kaldık

`main` @ `8fd4b9d` · `origin/main` ile birebir · çalışma ağacı temiz · süit **1894 passed / 2 skipped /
0 failed**.

Graf paneli design v1.3.0 §2.3'e göre sıfırdan yazıldı, ardından **dört gözle-doğrulama turu** yapıldı:
taşan görsellerin WPF layout clip'i (halka + beads görünmüyordu), overlay'in bayat ölçüyle ortalaması,
ad etiketinin düğümle çakışması, hover ölçeği, kenar payı. Hepsi kapandı.

**Son karar:** atlanan projeler için hiçbir canlandırma yok — denendi, gözle bakıldı, geri alındı.
Gerekçe `GraphNodeOpacity.IsSettled` dokümanında ve `GraphSkippedProjectTests`'te yazılı.

Bu oturumda ayrıca `status.ps1` düzeltildi (tamamlanmış bir adım listeden çıkınca 🟥'ye dönüyordu) ve
global `CLAUDE.md`'nin ilerleme kuralları revize edildi.

## İlgili dosyalar (kümülatif)

- `.claude/outputs/2026-08-08-19-37-graph-overhang-clip-and-skip-feedback.md` — **bu turun ölçümleri**
  (layout clip kanıtı, tooltip kelepçe ölçümü, karar gerekçeleri).
- `.claude/outputs/2026-08-07-01-30-quiet-graph-visual-checklist.md` — gözle doğrulama listesi.
- `.claude/outputs/2026-08-06-22-34-quiet-graph-tdd-plan.md` — quiet graph TDD dökümü.
- `.claude/outputs/2026-08-06-22-34-quiet-graph-test-inventory.md` — test envanteri (hangi test yaşadı/öldü).
- `.claude/outputs/2026-08-05-01-26-design-v1.3.0/README.md` — **GÜNCEL görsel otorite** (§2.3 quiet graph).
- `.claude/outputs/2026-08-05-01-26-design-v1.3.0/prototype/app/BuildApp.jsx` — algoritmanın kaynağı.
- `.claude/outputs/2026-08-05-01-38-brand-mark-and-about-conformance-plan.md` — v1.2.1 uyum dökümü.
- `.claude/outputs/2026-08-04-21-17-about-dialog-design.md` — About spec'i.
- `.claude/outputs/2026-08-04-21-44-about-dialog-implementation-plan.md` — About TDD planı.
- `.claude/summaries/2026-08-03-10-44-a13-complete-merged-to-main.md` — önceki oturum özeti (A13 bilançosu).
- `.claude/outputs/2026-08-03-09-44-visual-check-residue.md` — 20 göz-ister kalem.
- `.claude/outputs/2026-08-03-09-44-parked-items-triage.md` — 106 karar satırı + A14 öncelik sırası.
- `.claude/outputs/2026-07-16-09-40-v7-execution-playbook.md` — kalan adımlar (A14 → A15).
- `.claude/summaries/2026-07-31-23-02-a13-b3-b4-complete-final-review-pending.md` — önceki oturum özeti (B3/B4).
- `.claude/summaries/2026-07-31-17-54-a13-b0-b2-complete-suite-policy-found.md` — önceki oturum özeti (B0-B2).
- `.claude/summaries/2026-07-31-08-38-a13-t2-t4-complete-b1-promoted.md` — önceki oturum özeti (T2/T3/T4).
- `.claude/summaries/2026-07-30-18-26-visual-check-automation-progress.md` — daha önceki oturum özeti.
- `.claude/outputs/2026-07-30-18-45-a13-inventory-appendix.md` — envanter eki.
- `.claude/outputs/2026-07-30-14-59-visual-check-automation-tdd-plan.md` — A13 TDD dökümü.
- `.claude/outputs/2026-07-26-10-17-visual-check-walkthrough.md` — gözle kontrol listesi.
- `.claude/outputs/2026-07-26-10-17-it5-records.md` — It-5 kabul kaydı.
- `.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md` — PLAN OF RECORD (v7).
- `.claude/outputs/2026-07-15-19-00-design-v1/README.md` — önceki görsel otorite (v1.0).
- `.claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md` — A13.1 / A13.2.
- `.claude/outputs/2026-07-02-01-38-delta-design-system-v1.md` — tasarım sistemi.
- Önceki handoff: `.claude/handoffs/2026-08-06-08-19-graph-cinema-mode-awaiting-visual-check.md`.

Buradan devam edilecek.
