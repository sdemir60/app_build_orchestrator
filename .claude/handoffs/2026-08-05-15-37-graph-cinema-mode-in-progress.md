# Devir — 2026-08-05 15:37 — **Graf sinema modu, `graph-live-camera` branch'inde yarım**

## Nerede kaldık

Branch **`graph-live-camera`** @ `af6f261` (main'den ayrık, merge EDİLMEDİ). Oturum bu branch'te bitti.
8 task'lık plan **subagent-driven** yürütülüyor; **Task 1-4 kapalı, Task 5 fix döngüsünde**.

**Canlı durum ledger'da:** `.superpowers/sdd/2026-08-05-12-27-graph-live-camera-implementation-plan/progress.md`
— hangi task kapandı, hangi bulgu ertelendi, hangi karar neden verildi hepsi orada. **Devam ederken önce onu oku.**

**Kesildiği an:** Task 5'in fix round'u (etiket kuralı yeniden tasarımı) bir subagent'a gönderilmişti; agent
**durduruldu (kill)**, commit ATMADI. Çalışma ağacında 5 dosya **kirli** kaldı — o agent'ın yarım işi:
`GraphLayout.cs`, `GraphNodeVisual.cs`, `GraphView.xaml.cs`, `GraphCinemaTests.cs`, `GraphLayoutTests.cs`.

Devam ederken **yarım işi `git checkout -- <dosyalar>` ile geri al ve Task 5'i temiz baştan yürüt** — Task 1'de
aynı durum yaşandı, aynı çözüm uygulandı ve çalıştı. Kırmızı-önce disiplini yarım bir başlangıçta bozulur.

**Agent'ın kesilmeden önceki son bulgusu (değerli, kaybolmasın):** mevcut testlerden biri, incelediği düğüm
**seçili** olduğu için yeni kuralda muafiyet kapsamına giriyor ve kırmızıya dönüyor. Bu gerçek bir davranış
değişimidir; CLAUDE.md gereği o test silinmez/gevşetilmez, YENİ kuralı pinleyecek şekilde yeniden yazılır ve
doc'una eski iddia + değişme gerekçesi yazılır.

**Task 5'in kararı (kullanıcı "en doğru yöntemi sen seç" dedi):** etiket kuralı ölçek-değişmez örtüşmeye
(bugünkü `LabelsFit`) döner + **odak muafiyeti** eklenir (Building veya seçili düğüm katman kararından bağımsız
etiket alır). Gerekçe: etiketler kameranın altında olduğu için örtüşme zoom'dan bağımsızdır, o yüzden zoom
eşiği geometrik olarak savunulamaz. `LabelVisibleAtScale`/`LabelShowRatio`/`LabelHideRatio`/histerezis kalkacak.

**Task 8 tuzağı:** Task 8'in doküman brief'i hâlâ ESKİ oran kuralını anlatmayı söylüyor — dispatch'te açıkça
ezilmeli, yoksa ARCHITECTURE.md'ye yanlış kural girer.

## İlgili dosyalar (kümülatif)

- `.claude/outputs/2026-08-05-12-02-graph-live-camera-design.md` — **bu işin spec'i** (onaylı).
- `.claude/outputs/2026-08-05-12-27-graph-live-camera-implementation-plan.md` — **8 task'lık TDD planı**.
- `.superpowers/sdd/2026-08-05-12-27-graph-live-camera-implementation-plan/progress.md` — **canlı ledger**
  (task brief'leri, implementer raporları ve review paketleri de aynı klasörde).
- `.claude/outputs/2026-08-05-01-26-design-v1.2.1/README.md` — **GÜNCEL görsel otorite** (§9 sürüm geçmişi).
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
- Önceki handoff: `.claude/handoffs/2026-08-05-04-40-about-screen-and-product-brand.md`.

Buradan devam edilecek.
