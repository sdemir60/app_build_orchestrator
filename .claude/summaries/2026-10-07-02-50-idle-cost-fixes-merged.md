# Özet — boşta imleç, görünür koşuda takılma, koşu sonrası bellek düzeltmeleri (2026-10-07 02:50)

Önceki oturumdan devam: F (tepside yetim animasyon saatleri) `786f2f9` ile develop'taydı; kullanıcının üç yeni konusu
(boşta ~900 MB bellek, derleme sonrası fan/CPU, Resolve sırasında graf animasyonlarında takılma) ve B4 (ön planda boşta
181 M/s) için "en doğru, en sağlıklı, veriye ve teste dayalı, uygulamayı bozmadan" talimatı. Plan:
`.claude/outputs/2026-10-07-00-05-idle-cost-plan.md`; sonuç: `.claude/outputs/2026-10-07-02-30-idle-cost-results.md`.

## Yapılanlar (hepsi develop'ta, push edildi; birleşik tam süit 4670 geçti / 8 atlandı / 0 düştü; CI yeşil)

- **G1 (`769fdaf`)** — imleçler girdi yokken durur: `CaretIdleGate` (saf çekirdek, `SPI_GETCARETTIMEOUT`, varsayılan 5 s),
  `CursorClock`'a üçüncü kapı (`SetInputIdle`), `MainWindow`'da `PreProcessInput` + aktivasyon → yoklama zamanlayıcısı.
  Testler `CaretIdleGateTests`, `CursorClockTests`. ARCHITECTURE §14.5.
- **G2 (`deaa79d`)** — plan değişti: havuz değil, kalıcı satırlar. İzin dilim ayrıştırması (yeni araçlar
  `slice_breakdown.py`, `slice_anchor.py`) uzun dilimlerin satır İNŞASI değil, pencere kayınca geri dönüştürülen
  container'ların yeniden bağlanması olduğunu gösterdi; UIA yürüyüşü de meşgul sürenin %23'üydü (Custom tür peer'lar her turda
  baştan sayılıyor). `FixedHeightVirtualizingPanel` aşamalı kurar (pencere senkron, gerisi ApplicationIdle dilimleri, 3/dilim),
  container bırakmaz; `StickyLayerList.SetGroups` `ListReconciler` ile yerinde uzlaştırır (reset yok), reveal tetiği Loaded
  kuyruğunda; 21 UserControl `UserControlRolePeer` ile gerçek rol bildirir (guard `AutomationRoleTests`). WPF generator'ının
  taşınan container'ı yanlış bağlama kusuru yüzünden taşınan satır bilerek yeniden kurulur. Testler
  `ListRowsStayRealizedTests`, `ListReconcilerTests`; `StickyOverlayTests`/`ProjectListFilterTests` yeni kurala göre.
  ARCHITECTURE §13.2/§15/§17.2/§22 ve bilinen sınırlar; README.
- **G3 (`d3b0476`)** — gcdump tip dağılımı (çevrimdışı `dotnet-gcdump report` + `gcdump_agg.py`): canlı yığının yarısından
  fazlası metin, en büyüğü konsol belgesinin ipi; kırpma çalışıyor ama AvalonEdit geri-alma yığını kaldırılan metni canlı
  tutuyordu. `ConsoleView.NewDocument` (tek fabrika, `UndoStack.SizeLimit = 0`). Test `ConsoleMemoryTests`. ARCHITECTURE konsol.
- **G4** — ölçüldü (metin biçimleme 1690 ms: AvalonEdit 1142, TextBlock 523), kaldıraç yok; değişiklik yapılmadı.
- Fanlar: uygulama dışı (Defender + ısı ataleti) — önceki raporda.

## Kalan

- Masaüstü ölçümü (kilit açılınca, ~20 dk, klavye/fareye dokunmadan): `dotnet build BuildOrchestrator.slnx -c Release`
  (02:40'ta yapıldı, bin güncel) → `.claude\temp\perf-2026-10-06\measure-all.ps1 -Dir .claude\temp\perf-2026-10-06\after1`.
  Hedefler: B4 ≤ 40 M/s; Resolve'da >100 ms boşluk 4 → 0–1; iz dilimleri/UIA payı; M1 WS 688 → ≤ ~400 MB.
- Ölçüm sonucuna göre: konsol anlatısının 2–3 kopyası (tek kopya tasarımı) ve kalan payların raporlanması.

## Notlar

- Gece alınan `heap1` dökümü geçersiz (kilitli masaüstü: Clean çalışmadı, Resolve her şeyi atladı).
- Zamanlama testleri (SafeExitProcessTests) tam süit yükünde düşebiliyor; tek başına yeşil.
- Çözümleyiciler `.claude/temp/perf-2026-10-06/tools/`; bellek notu `perf-measurement-toolkit` güncellendi.
