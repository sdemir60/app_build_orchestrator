# Aşama girişi — boşta maliyet düzeltmeleri (masaüstü ölçümü bekliyor)

Özetler:
- `.claude/summaries/2026-10-07-02-50-idle-cost-fixes-merged.md` — bu oturum: G1 imleç, G2 kalıcı satırlar + uzlaştırma +
  UIA rolleri, G3 konsol geri-alma geçmişi; hepsi develop'ta, süit ve CI yeşil.
- `.claude/outputs/2026-10-07-02-30-idle-cost-results.md` — sonuç raporu (iz/döküm kanıtları, masaüstü ölçüm betiği).
- `.claude/outputs/2026-10-07-00-05-idle-cost-plan.md` — plan (G2 planı ölçümle değişti).
- `.claude/outputs/2026-10-06-22-38-memory-fans-resolve-stutter.md` — bellek/fan/takılma ilk ölçümleri.
- `.claude/outputs/2026-10-06-21-53-orphaned-animation-clocks.md` — F (tepside yetim saatler) raporu.

Nerede kaldık: G1 kullanıcı kararıyla geri alındı (imleçler odağı izler; `CaretFocusRuleTests`). Sabah masaüstü ölçümleri
alındı (`after1/`, rapor §"07.10 sabahı"): G2'nin UI thread kazancı ölçüldü, "tüm satırlar çizilir" bedeli A/B ile bulunup
Hidden bandıyla (`perf/list-render-band`) geri alındı; G3 canlı yığını %40 düşürdü. Resolve kare boşluğu hedefi bu sabah
sınanamadı (değişiklik öncesi derleme de aynı çöküşü verdi: makine yüklüydü) — sakin makinede
`resolve-stutter.ps1 -Priority Normal` ile tekrar edilecek. Açık soru: finalizer thread'inin izdeki 19–21 s'lik payı
(artefakt mı, gerçek mi — cycle sayacıyla doğrulanmadı).
