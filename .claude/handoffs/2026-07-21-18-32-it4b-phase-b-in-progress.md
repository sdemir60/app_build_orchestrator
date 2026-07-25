# Devir — It-4b Faz B sürüyor (B1/B2 kapandı, B3 review'ü açık)

## İlgili dosyalar
- `.superpowers/sdd/progress.md` — **It-4b ledger, tek güvenilir hafıza.** Sonunda `>>> RESUME HERE <<<`.
  A1–A5, B1, B2 tam kayıtlı; B3 "review in flight" olarak işaretli; ileriye taşınan Minor'lar task bazında yazılı.
- [.claude/outputs/2026-07-21-05-46-it4b-tdd-plan.md](../outputs/2026-07-21-05-46-it4b-tdd-plan.md) — It-4b TDD planı, 24 task. Global Constraints bölümü bağlayıcı.
- `.claude/temp/it4b/task-{A1..E6}-brief.md` + `global-constraints.md` — task brief'leri (subagent'lara plan yerine bunlar verilir).
- `.claude/temp/it4b/task-{A1..A5,B1,B2,B3}-report.md` — implementer raporları (fix wave'ler aynı dosyaya ekli).
- [.claude/summaries/2026-07-21-13-59-it4b-phase-a-engine-complete.md](../summaries/2026-07-21-13-59-it4b-phase-a-engine-complete.md) — Faz A özeti.
- [.claude/handoffs/2026-07-21-13-59-it4b-phase-a-engine-complete.md](2026-07-21-13-59-it4b-phase-a-engine-complete.md) — bir önceki devir.
- [.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md](../outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md) — davranış otoritesi (Part C It-4 acceptance).
- [.claude/outputs/2026-07-15-19-00-design-v1/README.md](../outputs/2026-07-15-19-00-design-v1/README.md) — görsel otorite (+ prototype/app/BuildApp.jsx, _ds token'ları).
- [.claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md](../outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md) — A13.2 + Ek A'nın 25 davranışı.
- [.claude/summaries/2026-07-20-22-57-it4a-ui-infra-complete.md](../summaries/2026-07-20-22-57-it4a-ui-infra-complete.md) — It-4a UI altyapı paketi.
- [.claude/outputs/2026-07-18-12-37-it3-records.md](../outputs/2026-07-18-12-37-it3-records.md) — It-3 kabul kayıtları.

## Nerede kaldık
Branch **`it4b-ui` @ `29bf5c5`**, çalışma dizini temiz, `main`/`origin`'e dokunulmadı.
A5 re-review Approved; **B1** Approved; **B2** fix wave sonrası Approved; **B3** implementasyonu indi
(suite 884 geçti / 1 atlandı, build 0/0) fakat **review'ü çalışırken session bitti — B3 review'ü baştan
çalıştırılacak** (`review-package 66a68e5 29bf5c5`). Buradan devam edilecek.
