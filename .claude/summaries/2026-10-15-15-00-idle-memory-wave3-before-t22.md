# Boşta bellek azaltma — Dalga 3, T22 öncesine kadar (2026-10-15 08:31 → 15:00)

## Durum

- Branch `fix/idle-memory-wave3` (develop `f50478dd`'den), HEAD `e1e237d2`, ağaç temiz; oturum `develop`'ta bırakıldı.
- **Task 18–21 ve dalga 3 düzeltme turu commit'li.** Sahibin talimatı: çalışmayı bitir, **D3 T22'de (doküman + tam süit + ölçüm) dur — onu
  sahip başlatacak.** Kalan tek iş: düzeltme turunun kapsamı dar yeniden incelemesi (limit yüzünden durduruldu, yeniden gönderilecek).

## Bu bölümde yapılanlar

1. **Task 18** (EngineHost uyku/uyanma): iki düzeltme turu — dispose öne geçer (çıkış ~150–170 ms), ölüm–uyku yarışında uyku kontrolünün
   gördüğü nesli sahiplenir, kapanış yazıcıyı devralır, uyku UI thread'inden ayrılır, `JobBirths` test yardımcısı.
2. **Task 19** (Stop the engine when idle ayarı, ENGINE grubu, `EngineIdleRule.IdleAfter` = 5 dk): onaylandı.
3. **Task 20** (uyku kuralı + tek `EngineState` + tik): onaylandı; düşen uyku "Engine stopped unexpectedly (idle shutdown failed)" + Restart.
4. **Task 21** (uyandırma kapısı): tek kapı yalnız uyku döngüsünü (Sleeping/Asleep/Waking) yönetir; ölü/erişilemez motor bugünkü gibi
   gönderir ve bugünkü hata satırını yazar (R2); çıkış istenince kapıda bekleyen gönderilmez (R3); tek "uyandırır mı" tablosu
   (`EngineWakeRule.cs`, 13 satır + yansıma testi); uyanma sessiz (konsol satırı yok, açılış Sync'i yok); askıda kalan uyanma
   "(wake failed)" ölümüne döner; motor yaşam döngüsü `RunViewModel.Engine.cs`'te.
5. **Dalga 3 genel incelemesi** (opus): kritik 0, önemli 0, küçük 6, düzen 4. Host ile VM her durumda tutarlı, her gönderim kapıdan
   geçiyor, uyku/uyanma döngüsü başına sızıntı yok, boşta motora komut gönderen zamanlayıcı yok.
6. **Düzeltme turu** (24 madde + 2. kısım, 13 commit): kendi nesil alanı, uyanma UI thread'inden ayrılır, ölü motorun ikinci bildirimi
   yalnız metni yeniler, kapı geçişi (`GatePass`, `using` ile, kayıt teslimde biter), tek çıkış sorusu, **Run'ın uyanması tıklamada başlar
   ve koreografiyle örtüşür**, kaynak guard'ları, test yardımcıları tekleşti.

## Sahibin kararını bekleyenler (önerilerimle)

- **Dalga 3:** A uyanınca eski proje logu — uygulama koşu klasörünü göndersin; B uyanırken şerit "Starting the engine…"; C kabul satırı
  "S4 motor uyudu" + tepside T1b; D Hakkında'da uykudaki motor PID'i "asleep"; iki yeni şerit metni ("idle shutdown failed" /
  "wake failed") kalsın; M-1 oturum ortasında uyanma başlatılamazsa Restart sunulsun; M-4 yarım koşuyu bildiren uyanmadan sonra bir Sync.
- **Dalga 2'den:** (1) uzun proje logu baştan açılsın; (2) proje sayfasında dibe inince takip geri gelsin; (3) açılışta `⌄ latest` kalsın;
  (4) PageUp bir sayfa kaysın; (5) iki küçük doküman hatası düzeltilecek; (6) ayar varsayılanlarını tek yere toplama + ~27 test kopyası
  dalga 3'ten sonra ayrı iş.

## Devam planı

1. `git switch fix/idle-memory-wave3`.
2. Düzeltme turunun yeniden incelemesini yeniden gönder (paket `review-a72becb1..e1e237d2.diff` hazır); bulgu varsa tek küçük tur.
3. T22'nin başında dur; sahibe doküman envanterini (`wave3-final-review.md` §6 + düzeltme raporu §5), ölçüm betiği değişikliklerini
   (`wave3-final-review.md` §7) ve bekleyen kararları ver.
4. Sahip cevap verince: A/B/D/M-1/M-4 ve dalga 2'nin 1, 2, 4. maddeleri ayrı commit'lerle; sonra T22 (sahip başlatır), merge, CI, son özet
   ve bellek göstergesi hatırlatması.

## Dosyalar

- Kurtarma haritası: `.superpowers/sdd/2026-10-13-14-38-idle-memory-reduction-plan/progress.md` (sondaki "SESSION PAUSED 2026-10-15 15:00").
- Kararlar `task-18-22-decisions.md` (1–36); incelemeler `wave3-final-review.md`; raporlar `w3-t18…t21-report.md`, `wave3-fix-wave-report.md`.
