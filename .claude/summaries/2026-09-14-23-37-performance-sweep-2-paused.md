# Performans taraması tur 2 — duraklatıldı (yarım)

Tarih: 2026-09-14 · main @ 8c635ee · **kod değişmedi**

## Durum
- Tur 1 bitti ve main'de: imleç saatleri IsVisible kapısı + ConsoleBatcher boşta uyumaz (tray'de boştaki süreç
  ~81 → ~5 M döngü/sn). Rapor: `.claude/outputs/2026-09-14-19-56-focused-performance-pass-report.md`.
- Tur 2'de kullanıcı kararı: "performans için ne gerekiyorsa yap, tara ve bulduklarını direkt düzelt". Bu karar,
  tur 1 raporunun §4 sorusunu da kapatıyor: §14.5 kuralına uymayan satır nefesi, şerit süpürmesi ve graf saatleri
  de kapsamda. Sonra kullanıcı "performans işine daha sonra bakacağız" diyerek durdurdu.
- Tarama workflow'u (`wf_4d1c850a-43f`, 11 boyut + 3 mercekli doğrulama + eleştirmen) **oturum limitine takıldı**.
  Yalnız 2 bulgu ajanı (açılış, IPC) bitti; 9 bulgu ajanı, tüm doğrulamalar ve eleştirmen düştü.

## Bu turda alınan ölçümler
- **Acceptance (gerçek OSYS, paralellik 6):** 3/3 geçti. Tam rebuild 1 dk 36 sn · dispatch determinizmi 13 sn ·
  incremental (hepsi atlanır + tek kirli projede minimal rebuild) 1 dk 54 sn.
- **İçerik kararı ölçümü (OSYS, 177 proje, 22 986 girdi dosyası):**
  - Üretim yolu: tarama + graf + değerlendirme 764 ms · özet önbelleği boş 568 ms · sıcak 241 ms (her Sync'in bedeli).
  - Ham okuma harness'i (üretim yolu değil): tam okuma + SHA256 ilk geçiş 194 sn (kapı ≤6 sn: FAIL) · ikinci geçiş
    950 ms · yalnız stat 132 ms.
  - Soğuk ilk geçiş karşılaştırması (OSYS-AI): sıralı 8,44 ms/dosya · paralel(16) 1,62 ms/dosya · tam ağaç paralel
    tahmini 37 sn.

## DOĞRULANMAMIŞ aday bulgular (2 ajanın çıktısı)
Ham kayıt: `.claude/temp/perf-analysis/round2-partial-findings.json` (git'e girmez, yalnız bu makinede).

| Önem (ajanın) | Konum | Aday |
|---|---|---|
| önemli | `App.csproj` 22-30 | Publish ReadyToRun değil → açılışta tüm assembly'ler JIT (Supervisor klasörü build çıktısından kopyalandığı için ayrıca ele alınmalı) |
| önemli | `MainWindow.xaml.cs` 284-288 · `RunViewModel.cs` 1375-1380, 1628-1638 | Proje olayı başına tam yüzey tazeleme; 177 projectSkipped patlamasında UI thread'de O(n²) |
| önemli | `NdjsonFraming.cs` 17-30 · `RunCoordinator.cs` 654-665 | Olay başına 2 pipe yazımı + flush; hazır bekleyen olaylar birleştirilmiyor |
| organizasyon | `MainWindow.xaml.cs` 141, 435-443, 1024 · `App.xaml.cs` 117 | ui-state.json açılışta 5 kez okunuyor |
| organizasyon | `AutostartService.cs` 59-63 | Run anahtarı her açılışta koşulsuz yeniden yazılıyor |
| organizasyon | `ConsoleView.xaml.cs` 409-424 | AppendNarrativeBatch'te ölü daktilo kolu batch metnini iki kez kopyalıyor |
| kozmetik | `RunLogWriter.cs` 124, 136-144 | Proje logu satır başına senkron flush |
| kozmetik | `RunViewModel.cs` 1996-1998 | `_liveLines` bitmiş projelerin olaylarını bir sonraki işleme kadar tutuyor |
| kozmetik | `SingleInstance.cs` 75-79 | Oturum id'si için tüm süreçlerin anlık görüntüsü alınıyor |
| kozmetik | `MainWindow.xaml` 214-229 | Settings/About/Notes diyalogları açılışta eager kuruluyor |

## Kalan iş
1. Taramayı yeniden koş. Script:
   `C:\Users\Delta\.claude\projects\D--Projects-Other-Apps-app-build-orchestrator\38bdd263-90d9-4a6e-8a64-e1f08751c413\workflows\scripts\perf-sweep-round-2-wf_4d1c850a-43f.js`.
   `resumeFromRunId` yalnız aynı oturumda çalışır; yeni oturumda script yolu ile baştan koşulur. Limite takılmamak
   için boyutlar 3-4'lük gruplar hâlinde koşulmalı.
2. Adayları ve yeni bulguları 3 mercekle doğrula (doğruluk / etki / kırmızı çizgi).
3. Branch `perf/sweep-2`: her bulgu için kırmızı test → düzeltme → commit. §14.5 kalan sahipleri (ProjectRow nefesi,
   StickyRibbon süpürmesi, GraphView beads/edge-flow) de dahil.
4. Tam süit + Acceptance + döngü sayacıyla önce/sonra ölçüm → rapor → merge.
