# Aşama girişi — boşta bellek azaltma, Dalga 3: T22'de ölçümün başında durduk

**Özetler**
- `.claude/summaries/2026-10-13-18-05-idle-memory-wave1-progress.md`: Dalga 1, Task 1–5 ve ön tarama kararları.
- `.claude/summaries/2026-10-14-05-51-idle-memory-wave1-fix-wave-progress.md`: Dalga 1 düzeltme turları.
- `.claude/summaries/2026-10-15-08-31-idle-memory-wave2-done-wave3-progress.md`: Dalga 2 bitti ve merge'lendi (276 MB); Dalga 3 başladı.
- `.claude/summaries/2026-10-15-15-00-idle-memory-wave3-before-t22.md`: Dalga 3 Task 18–21 ve düzeltme turu; bekleyen kararlar.
- `.claude/summaries/2026-10-16-08-18-idle-memory-wave3-decisions-and-cleanup.md`: sahip kararları (motor, konsol), hijyen, son toparlamanın yarısı.
- `.claude/summaries/2026-10-16-09-52-idle-memory-wave3-ready-for-t22.md`: son toparlama bitti (22/22), T22'ye hazır.

**Nerede kaldık:** T22'nin dokümanları ve tam süiti bitti, ölçümün başında durduk.
- **Dokümanlar:** README `90873815`, ARCHITECTURE `a22dfd52`. İnceleme düzeltmeleri `3fd3c1d4` ve `883ae8dc`'de.
- **Tam süit:** 5521 geçti, 11 atlandı. Düşen tek test (`HeadWatcherTests`, yükten kaynaklı) tek başına 3 kez geçti.
- **Ölçüm:** betikler hazır, sahip başlatacak. Başlatma komutu `task-22-plan.md` §3'te.
- **Branch:** `fix/idle-memory-wave3` açık kalmalı, çünkü ölçüm çalışma ağacını derler.
- **Ölçümden sonra:** sonuç raporu, `develop`'a merge, push, CI, branch silme, son özet ve bellek göstergesi hatırlatması.

Buradan devam edilecek.
