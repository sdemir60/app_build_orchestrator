# Aşama girişi — boşta maliyet düzeltmeleri (masaüstü ölçümü bekliyor)

Özetler:
- `.claude/summaries/2026-10-07-02-50-idle-cost-fixes-merged.md` — bu oturum: G1 imleç, G2 kalıcı satırlar + uzlaştırma +
  UIA rolleri, G3 konsol geri-alma geçmişi; hepsi develop'ta, süit ve CI yeşil.
- `.claude/outputs/2026-10-07-02-30-idle-cost-results.md` — sonuç raporu (iz/döküm kanıtları, masaüstü ölçüm betiği).
- `.claude/outputs/2026-10-07-00-05-idle-cost-plan.md` — plan (G2 planı ölçümle değişti).
- `.claude/outputs/2026-10-06-22-38-memory-fans-resolve-stutter.md` — bellek/fan/takılma ilk ölçümleri.
- `.claude/outputs/2026-10-06-21-53-orphaned-animation-clocks.md` — F (tepside yetim saatler) raporu.

Nerede kaldık: G1 kullanıcı kararıyla geri alındı (imleçler odağı izler, girdi kapısı yok; `CaretFocusRuleTests`), develop'a
merge edildi. Masaüstü kilitli olduğu için G2–G3'ün "sonra" ölçümleri alınmadı; kilit açılınca (Release bin güncel)
`.claude\temp\perf-2026-10-06\measure-all.ps1 -Dir .claude\temp\perf-2026-10-06\after1` koşulacak ve sonuç raporu
rakamlarla tamamlanacak.
