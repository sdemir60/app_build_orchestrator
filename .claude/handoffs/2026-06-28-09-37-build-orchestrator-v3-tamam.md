# Build Orchestrator — Aşama Girişi (Handoff)

v3 plan oluşturuldu ve eski/karalama notları v3'e karşı değerlendirilerek şu kararlar işlendi: §7 animasyon paralel-farkında hibrit (build frontier + sticky şerit), global build-state (projectId/imza), worktree her-zaman toggle + karar matrisi + chip selector, Job Object CPU rate cap + ana-UI perf seçici, config'in build-signature'a katılması + post-build copy gerçeği + VS-parity. Buradan devam edilecek (yeni session'da v4 yazılacak, son dönem yorumlar değerlendirilecek).

## İlgili dosyalar (kümülatif)

- **Orijinal prompt (spec):** [outputs/2026-06-27-21-40-build-orchestrator-orijinal-prompt.md](../outputs/2026-06-27-21-40-build-orchestrator-orijinal-prompt.md) — 11 bölümlük orijinal spec.
- **Mevcut durum analizi:** [summaries/2026-06-27-21-52-build-orchestrator-mevcut-durum-analizi.md](../summaries/2026-06-27-21-52-build-orchestrator-mevcut-durum-analizi.md) — eski kodun spec'e karşı durumu, kritik bulgular.
- **v2 plan:** [outputs/2026-06-27-22-46-build-orchestrator-yeni-plan.md](../outputs/2026-06-27-22-46-build-orchestrator-yeni-plan.md) — shell-out + nested Job + walking-skeleton temel plan.
- **v3 plan (güncel / onaylı):** [outputs/2026-06-28-08-19-build-orchestrator-plan-v3.md](../outputs/2026-06-28-08-19-build-orchestrator-plan-v3.md) — bu session'ın çıktısı; uygulanacak güncel plan.
- **CLAUDE.md** — mimari hâlâ v2'ye referanslı (v4 sonrası güncellenecek).
