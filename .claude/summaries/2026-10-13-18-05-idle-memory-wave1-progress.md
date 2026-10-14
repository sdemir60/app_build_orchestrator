# Boşta bellek azaltma — Dalga 1 ara durum

**Tarih:** 2026-10-13 15:16–18:25 · **Branch:** `fix/idle-memory-wave1` (develop `490880ee`'den) · **Ara sebebi:** kullanıcı
bilgisayarı 18:25'te kapattı; iş yarıda, devam edilecek.

## İstek

Spec (`.claude/outputs/2026-10-13-14-38-idle-memory-reduction-spec.md`), plan (`…-plan.md`) ve ölçüm raporu
(`.claude/outputs/2026-10-13-13-38-idle-memory-analysis.md`) okunup plan `superpowers:subagent-driven-development` ile dalga dalga
uygulanacak. Her dalgada sırasıyla görevler, tam süit, ölçüm (spec §9), sonuç raporu ve develop'a merge + push yapılır. Hedef tutmazsa dalga merge
edilmez. Spec ile çelişen her durumda kullanıcıya sorulur. Commit'lere attribution satırı eklenmez.

## Yapılanlar

- **Taban:** değişiklik öncesi tam süit 4915 geçti / 0 düştü / 9 atlandı (10 dk 32 sn); CI develop `490880ee` yeşil.
- **Ön tarama:** iki salt-okuma ajanı Dalga 1 planını gerçek koda karşı taradı. Plan olduğu gibi uygulansaydı:
  - test projesi derlenmezdi (`Tests.Core` namespace'i);
  - planın kendi "gizleyince trim" testi kırmızı kalırdı;
  - trim bitiş finalinin ortasına düşerdi;
  - 10 mevcut test kırılırdı (`_liveLines`);
  - ~200 Supervisor testi gerçek agresif GC koşardı;
  - görünüm store'a hiç bağlanmazdı (Task 10 yapısal testi).

  Hepsine spec'e dayanarak karar verildi. Kararlar SDD ledger'ında `Ruling:` satırları olarak duruyor.
- **Commit'ler (hepsi incelendi ya da incelemede):**

| Commit | Görev | İnceleme |
|---|---|---|
| `4aeee207` | docs: ölçüm raporu, spec, plan | — |
| `3f1d4524` | Task 1 `MemoryTrim` tek tanım + guard + Supervisor trimi (koşu/Sync/Clean/Optimize sonu; koşu sürerken GC atlanır; testler no-op trim) | temiz |
| `882cdbde` | Task 2 `EndedOperationSerial` (her başlamış işlemin her bitiş yolu) | temiz |
| `bcc91548` | Task 3 `GraphView.EndFinaleFinished` | temiz |
| `8ca65641` | Task 4 `MemoryTrimRule` (+ 1,5 s pay sabiti tek yerde) | temiz |
| `5226fc76` | Task 5 kabuk kablajı: görünür pencerede de finalden sonra boşta bir kez trim; tepsi yolu korunur | **düzeltme gerekli** |

- **Task 5 inceleme sonucu "düzeltme gerekli":** Çekirdek davranış doğrulandı: final oynarken GC yok, pencereler arası zamanlayıcı
  sızıntısı yok, sıra değişmezleri pinli. Düzeltme turu 1'de üç madde var (`task-5-fix-round-1-findings.md`):
  - **F1:** Motor ölür ya da yeniden başlatılırsa, gönderilmiş bir proje logu yüklemesi "bekliyor" olarak takılı kalıyor ve sonraki
    her trim'i blokluyor.
  - **F2:** Motora gönderim yönlendirmesi kopyalanmış (Kopya YASAK).
  - **F3:** Sayılmayan bir iş (ör. başarısız pull) yüzünden atlanan trim yeniden istenmiyor.

  Tur 18:25 kapanışına yetişmeyeceği için başlatılmadı.
- Küçük inceleme bulguları ertelendi ve ledger'a `minor (deferred)` olarak yazıldı; dalga sonundaki genel incelemede ele alınacak.

## Açık soru (kullanıcıda)

- **Task 9:** Hakkında → Environment → RUNTIME grubuna bellek bilgisi. Spec §6.6 üç ayrı satır istiyor (App private · GC committed · heap),
  plan ise tek "Memory" satırı (`private=… committed=… heap=…`). Öneri: spec. Cevap gelene kadar Task 9 bekler.

## Kalan iş

- **Dalga 1:**
  - Task 5 düzeltme turu 1 (F1–F3) ve kapsamı dar yeniden inceleme.
  - Task 6–7: NarrativeStore + VM/konsol bağlama (karar dosyası hazır).
  - Task 8: `_liveLines` daraltma (karar dosyası hazır).
  - Task 9–10: tanı satırları (cevaba bağlı) ve bütçe testleri.
  - Task 11: dokümanlar, tam süit, ölçüm (UI duraklama probu eklenecek), sonuç raporu, merge.
- **Dalga 2 ve Dalga 3:** dokunulmadı. Her biri başlarken kendi ön taramasını yapacak.

## Dosyalar

- SDD çalışma alanı (git-ignored, diskte kalıcı): `.superpowers/sdd/2026-10-13-14-38-idle-memory-reduction-plan/`
  - `progress.md`: ledger, kurtarma haritası. Ön tarama tablosu, kararlar, ertelenen küçük bulgular ve görev durumları burada.
  - `preflight-wave1-trim.md`, `preflight-wave1-narrative.md`: Dalga 1'in gerçek kod karşılaştırması (satır numaralı).
  - `task-5-decisions.md`, `task-6-7-decisions.md`, `task-8-decisions.md`, `task-9-10-decisions.md`: görev başına kontrolcü
    kararları. Task 9'da iki seçenek de yazılı; kullanıcının cevabına göre biri uygulanacak.
  - `task-5-fix-round-1-findings.md`: Task 5 düzeltme turu 1'in bulguları (F1–F3 yapılacak, F4–F5 ertelendi).
- Ölçüm betiği `mem-after-build.ps1`'e koşu bitişinden itibaren 15 s'lik UI gecikme probu eklendi (`E-postrun-trim-15s`, spec §1
  "duraklama ≤ 300 ms" için). S1 yine +60 s'de.
  - `task-N-brief.md`: plan görev metinleri. `task-1-report.md`, `task-2-4-report.md`, `task-5-report.md`: uygulayıcı raporları.
- Ölçüm scriptleri: `.claude/temp/mem-2026-10-13/mem-after-build.ps1` (görünür) ve `mem-tray-run3.ps1` (tepsi). Bağımlılıkları
  doğrulandı (HeapTool, probe-helper, mem-regions, keepawake).
