# Boşta bellek azaltma — Dalga 2 bitti, Dalga 3 sürüyor (2026-10-14 22:30 → 2026-10-15 08:31)

## Durum

- **Dalga 2 merge'lendi:** develop `f50478dd` (push'lu, CI `38678386604` yeşil). Branch `fix/idle-memory-wave2` silindi.
  Sonuç raporu `.claude/outputs/2026-10-15-06-50-idle-memory-wave2-result.md` (commit `fd43b4bb`).
- **Ölçüm (spec §9, `1452261d`):** koşu bitti +60 s, App + Supervisor private **349 → 276 MB** (hedef ≤ 435; Full kip 302 MB); tepsi
  T1 318 → 255 MB; App canlı yığın 95,8 → 35,0 MB; koşu sonu en uzun donma 50,6 → 7,8 ms. Supervisor S1'de 63 MB (46 MB `byte[]`
  tutumu, koşudan koşuya değişiyor — Dalga 3'ün uykusu 5 dk'da bırakır).
- **Ortam bulguları (dalgaya ait değil):** pencereye dönüşte ~70 ms tek donma — Dalga 1 ikilisi de aynı anda aynı sayıyı verdi;
  `StickyLayerHeaderClickTests` ×6 gece develop'ta da düştü, sabah 19/19 geçti; `UpdatePillTests` ×2 bilinen DPI farkı.
- **Son tam süit (`244bec9c`):** 5 187 geçti / 2 düştü (UpdatePill DPI) / 10 atlandı.
- **Dalga 3:** branch `fix/idle-memory-wave3` (develop `f50478dd`'den), HEAD `d4052fda`, ağaç temiz. Commit'ler:
  `01c7253e` Task 18 EngineHost Sleep/Wake · `f1b54229` Task 19 Stop the engine when idle ayarı · `e0ed7512` + `d4052fda` Task 18
  düzeltme turu 1 (dispose öne geçer, ölüm–uyku yarışı, yazıcı devri, uyku bağlamdan ayrılır, JobBirths test yardımcısı).

## Bu oturumda yapılanlar

1. **Dalga 2, Task 15–16 (kompakt konsol):** inceleme bir önemli bulgu verdi — sınıflandırıcı önceliği gerçek hata satırlarını
   (`MSBUILD : error …`, NU1605 "Warning As Error", alt kategori biçimi) kompakt kipte gizleyebiliyordu; kanonik MSBuild tanı biçimi
   önce okunacak şekilde düzeltildi (1 514 gerçek logda 528 854 tanı satırında sınıf değişimi 0). Küçükler (motor kaybında özet,
   satır başına ayırma sıfır, `::TargetFramework` eki, test kopyaları, proje adı yardımcısı `ProjectDisplayName` Core'da) kapandı.
2. **Dalga 2 genel incelemesi (opus):** kritik 0, önemli 0, küçük 4, düzen 7; tek düzeltme dalgası (20 madde, `CompactNarrative`
   çıkarımı, etiket kuralı, motor notları süzülmez, varsayılan tek kaynak) + 6 küçük tur + kapanış temizliği. Turlarda dalga öncesinden
   kalan konsol kusurları bulunup gerçek pencere testiyle kapatıldı: gerçek Ctrl+Home eski satırları yüklemiyordu; Ctrl+Home / ilk
   PageUp / eşiği aşan tekerlek dipten ayırmıyordu (`⌄ latest` çıkmıyordu, 5 sn dönüşte kalıyordu); ← Back / tepsi dönüşünde belge
   takası boşuna dilim okuyordu; bir ara gerileme (kaydırmadan 5 sn içinde açılan proje sayfası takip ediyordu) aynı dalgada kapandı.
   Konsolun kaydırma geometrisi tek kaynak: `ConsoleView.ScrollGeometry` (TextView), yükleyici `SettledOffset`, dip `BottomOffset`.
3. **Dokümanlar:** ARCHITECTURE §8.5, §12.1, §13.2, §13.3, §13.5, §16, §17, §20, §22 ve README dalga 2'ye göre yazıldı (`186fe8f3` +
   sonraki tek cümlelik düzeltmeler).
4. **Dalga 3 ön taraması** (`preflight-wave3-engine-sleep.md`, C1–C25) ve kararlar (`task-18-22-decisions.md`, 1–30): açılışta motorun
   yanlışlıkla uyutulmaması (kurala "motor hazır" girdisi), tek yaşam döngüsü kapısı, tek "uyandırır mı" tablosu, uyanma hatasında tek
   sinyal, dispose'un öne geçmesi (karar 28), Task 19/20/21 devirleri (karar 29–30).
5. **Task 18** (EngineHost): inceleme önemli bulgu I1 (dispose uçuştaki uyanma/uykunun arkasında bekliyordu → çıkış sınırı aşılırdı);
   düzeltme turu 1 bitti, kapanış artık ~150–170 ms; **kapsamı dar yeniden incelemesi durduruldu** (yeniden gönderilecek).
6. **Task 19** (ayar zinciri, ENGINE grubu, `EngineIdleRule.IdleAfter` = 5 dk tek sabit, fikstür varsayılanı kapalı): onaylandı
   (0/0/6 küçük; notlar Task 20/21/22'ye dağıtıldı).
7. **Task 20** (kural + VM motor durumu + tik): uygulayıcı gönderilmişti, **kod değişikliği yapmadan durduruldu** (yeniden gönderilecek).

## Sahibin kararını bekleyenler (sohbette soruldu)

- **Dalga 3:** (A) uyanan motor eski koşunun proje logunu veremiyor — `GetProjectLogCommand`'a koşu klasörü alanı (önerilen) mı,
  Supervisor'ın en yeni klasörü tohumlaması mı; (B) uyanırken şeritte "Starting the engine…" (önerilen) mi, yok mu; (C) kabul satırı
  yeni "S4 motor uyudu" + tepside T1b (önerilen); (D) küçük metinler — itiraz gelmediği için önerilenlerle ilerlendi (Close to tray,
  ayar kapalı notu); About'taki motor PID'i uykuda "asleep" (önerilen, karar 21) hâlâ bekliyor.
- **Dalga 2'den:** (1) 200 satırdan uzun proje logu son 200 satırın tepesinde açılıyor, doküman "baştan" diyor; (2) proje sayfasında
  dibe inince takip geri gelmiyor, doküman "gelir" diyor; (3) uzun proje sayfası açılınca `⌄ latest` görünüyor (yeni davranış, onay);
  (4) PageUp dilimin tepesine sıçrıyor; (5) iki küçük doküman hatası (§8.3 ilk satır, §16 "hotkey"); (6) takip işleri: General
  ayarlarının varsayılanı 5–6 yerde (Compact console ve Stop engine tek kaynakta), testlerde ~27 `NeverTickingBatcher` kopyası.

## Devam planı

1. `git switch fix/idle-memory-wave3` (oturum develop'ta bırakıldı).
2. Task 18 düzeltme turu 1'in kapsamı dar yeniden incelemesini yeniden gönder (paket `review-f1b54229..d4052fda.diff` hazır);
   bulguları + kabul edilen tek satırlık test düzeltmesini (`Dispose_waits_for_the_supervisor_process_to_exit` → `_ = supervisor.SafeHandle`)
   tek turda kapat.
3. Task 20'yi yeniden gönder (talimat `progress.md`'de; aynı içerik).
4. Task 21 (uyandırma kapısı) — A ve B cevaplanınca; cevapsız kısımlar olmadan da kapı + yarış testleri başlayabilir.
5. Dalga 3 genel incelemesi → düzeltme → docs pass (Task 22 doküman listesi ledger'da) → tam süit → ölçüm (C'ye göre S4/T1b; betik
   değişiklikleri preflight Q13) → rapor → merge.
6. Son: `.claude/summaries/<zaman>-idle-memory-reduction-summary.md`, bellek göstergesi hatırlatması (sağ alt performans hapı;
   aday ölçüler), sahibe açık soruların ve takip önerilerinin listesi.

## Dosyalar

- Kurtarma haritası: `.superpowers/sdd/2026-10-13-14-38-idle-memory-reduction-plan/progress.md` (sondaki "SESSION PAUSED" girişi).
- Kararlar: `task-12-13-decisions.md`, `task-14-16-decisions.md`, `task-18-22-decisions.md`; ön taramalar `preflight-wave2-*.md`,
  `preflight-wave3-engine-sleep.md`; dalga 2 incelemesi `wave2-final-review.md`, düzeltme raporu `wave2-fix-wave-report.md`.
- Ölçüm ham verisi: `.claude/temp/mem-2026-10-13/wave2-*`, `wave1-wreturn`, `wave2-wreturn`; sürücü `run-wave2-measure.ps1`,
  ayar geçişi `ui-state-compact.py`, pencereye dönüş sondası `mem-window-return.ps1`.
