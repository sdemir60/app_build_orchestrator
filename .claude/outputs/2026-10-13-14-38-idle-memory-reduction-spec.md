# Boşta bellek azaltma — tasarım (spec)

**Tarih:** 2026-10-13 · **Kod tabanı:** `develop` `490880ee` (v1.9.0 + graf bantları) · **Dayanak:** ölçüm raporu
`.claude/outputs/2026-10-13-13-38-idle-memory-analysis.md` (ham veri `.claude/temp/mem-2026-10-13/`) · **Uygulayıcı:**
bu spec'ten üretilecek plan ayrı bir oturumda (Opus) adım adım uygulanır; bu belge NE ve NEDEN'i, plan NASIL'ı taşır.

## 1. Amaç ve ölçüt

Derleme bitip uygulama boşta kaldığında tüketilen bellek, çalışan davranışı bozmadan, ölçülebilir biçimde düşürülür.

| Ölçüt | Bugün (ölçüldü) | Hedef |
|---|---|---|
| 195 projelik Rebuild (83 k satır log) bittikten 60 s sonra, pencere görünür, App + Supervisor private bayt | **~845 MB** (798 + 47) | Dalga 1 ≤ 500 · Dalga 2 ≤ 435 · Dalga 3 ≤ 390 |
| Aynı durumda 3 dk sonra | değişmiyor | değişmiyor ama yukarıdaki seviyede |
| Koşu sonrası görünür pencerede tek seferlik UI duraklaması | yok (toplama yok) | ≤ 300 ms (Dalga 1), ≤ 100 ms (Dalga 2 sonrası) |
| Konsol geçmişi | tamamı erişilebilir | tamamı erişilebilir (diskten) |
| Proje logu sayfası | tam | tam |

Ölçüm protokolü §9'dadır; her dalga bu protokolle kapanır ve sonucu `.claude/outputs/`'a yazılır.

**Kapsam dışı (bilerek):** süreç ayrımı (pencere kapalıyken UI sürecinin kapanıp yalnız tepsi ajanının kalması —
"sıfıra yakın"ın tek yolu, ayrı tasarım turu), native tabanın (~270 MB) araştırılması, MSBuild süzgecinin motora
taşınması (IPC küçültme; §6.5'te "sonraki adım" olarak tarif edilir, karar gerektirir).

## 2. Kararlar (kullanıcı onaylı)

| # | Karar |
|---|---|
| K1 | Koşu/işlem bitince boşta tek agresif toplama, pencere görünürken de (final animasyonundan sonra, boştayken). Tepsi yolu korunur. |
| K2 | Motor (Supervisor) 5 dk işsiz kalınca kapanır, ilk komutta kendiliğinden uyanır; ayar, varsayılan açık. |
| K3 | Koşu anlatısının tamamı RAM'de tutulmaz: tek kopya (Dalga 1) ve diske yayılmış geçmiş (Dalga 2). "Kaydırılan hiçbir satır erişilmez olmaz" kuralı korunur. |
| K4 | Kompakt konsol: hatalar tam, uyarılar proje başına özetlenir, diğer MSBuild satırları yalnız proje loguna; ayar, varsayılan açık; özetlendiği konsolda görünür (proje başına özet satırı + başlıkta "compact"). |
| K5 | Tüm çözümler geneldir: makineye/takıma özel varsayım yok, davranış değişiklikleri ayarlı ve güvenli varsayılanlı. |
| K6 | Yapılmayacaklar: `GCHeapHardLimit`, Server GC, `EmptyWorkingSet`/`SetProcessWorkingSetSize`, konsol geçmişini sessizce kısaltmak. |

## 3. Çalışma kuralları (uygulayıcı için, CLAUDE.md'den)

- Ana projede (`D:\Projects\Other\Apps\app_build_orchestrator`) `develop`'tan açılan çalışma branch'i; görev başına commit;
  bitince `develop`'a merge + push; CHANGELOG ve `Version`'a DOKUNULMAZ.
- **Kırmızı test kuralı:** her davranış değişikliği önce kırmızı testle gösterilir. Kural bilerek değişiyorsa onu pinleyen eski
  test silinmez, yeni kuralı pinleyecek biçimde yeniden yazılır ve doc'una eski iddia + gerekçe yazılır.
- **Kopya YASAK:** aynı sabit/metin/primitif iki yerde tanımlanmaz (GC çağrısı, dosya adı, ayar etiketi tek yerde).
- Değişmezler: planlama Core'da; stdout yalnız NDJSON; git'e kendiliğinden yazılmaz; OutDir'e dokunulmaz; Velopack yalnız App'te.
- Her dalga: tam süit (`dotnet test tests/... --filter "Category!=Acceptance"`) yeşil; ARCHITECTURE/README aynı işte güncellenir
  (anlatı üslubu, changelog biriktirmez); ölçüm protokolü (§9) koşulur ve sonuç raporu yazılır.
- Ölçüm/sonda testleri `[SkippableFact]` + ortam değişkeni kapısı (ARCHITECTURE §17.5 listesine eklenir).

## 4. Mevcut durum (kod haritası — neyin değiştiği)

| Yer | Bugün | Spec'te |
|---|---|---|
| `App/ViewModels/RunViewModel.cs:439` `_runText` (StringBuilder) | tüm projelerin tüm satırları + anlatı satırları, işlem boyunca canlı | `NarrativeStore` (tek sahip, Dalga 2'de diske yayılır) |
| `App/Console/ConsoleView.xaml.cs:107` `_backlogLines` | aynı metnin satır listesi (ikinci kopya); `ReplaceRunDocument(fullRunText)` tam metni böler (satır 653); kırpılan satırlar listeye eklenir (300–308) | anlatı modunda kopya yok: backlog = store; proje modunda bugünkü liste |
| `RunViewModel.cs:441` `_liveLines` | her projenin her satırı koşu boyunca; koşu sonunda bırakılır | yalnız bekleyen log yüklemesinin projesi için, yükleme süresince |
| `App/MainWindow.HiddenSurface.cs:103,159–170` `CollectMemory`/`CollectAfterRunWhenDue` | agresif toplama yalnız tepside biten koşudan sonra | her işlem sonunda, görünür pencerede de (§6.1) |
| `Core/Diagnostics/MemoryLine.cs`, `Supervisor/RunCoordinator.cs:306,695`, `SupervisorHost.cs:181` | motor işlem sonunda yalnız bellek satırı yazar | önce toplar, sonra yazar |
| `App/Services/EngineHost.cs` | Start/Restart/Dispose | + Sleep/Wake (§6.4) |
| `App/ViewModels/GeneralSettings.cs`, `Shell/UiStateStore.cs`, `SettingsDraftViewModel` | 7 bool ayar | + `CompactConsole`, `StopEngineWhenIdle` |
| `App/Console/ConsoleLineClassifier.cs` | satır rengi için sınıflandırma | kompakt süzgeç de aynı sınıflandırmayı kullanır (tek tanım) |
| `App/Services/DiagnosticsReport.cs` | sürüm/ortam/yollar | + bellek satırları |

## 5. Mimari ilkeler

1. **Bellek bütçesi:** hiçbir yapı log hacmiyle orantılı veri tutmaz; tutuyorsa (store) üst sınırı sabittir ve gerisi disktedir.
2. **Tek sahip:** koşu anlatısının bellekteki tek sahibi `NarrativeStore`'dur; konsol görünümü ondan dilim ister, kopyalamaz.
3. **Doğal sakin anda trim:** işlem bitince, boştayken, bir kez `GCCollectionMode.Aggressive` + compacting; tek tanım `Core/Diagnostics/MemoryTrim`.
4. **Boşta süreç yaşamaz:** motor işsizken kapanır; her motor isteyen yol önce uyandırır (tek kapı).
5. **Genel ürün:** davranış değişiklikleri ayar + güvenli varsayılan; UI metinleri İngilizce; makineye özel eşik yok.

## 6. Tasarım

### 6.1 Bellek trimi (K1) — Dalga 1

**Tek tanım.** `Core/Diagnostics/MemoryTrim.CollectAndRelease()` = `GC.Collect(2, GCCollectionMode.Aggressive, blocking: true,
compacting: true)`. `MainWindow.CollectMemory` ve Supervisor bunu çağırır; ikinci bir `GC.Collect` yazılmaz (kaynak guard'ı:
`GC.Collect(` yalnız bu dosyada geçer).

**App — kural (saf, test edilebilir `MemoryTrimRule`):** her *işlem serisi* (koşu, Sync, Clean, Optimize) için en fazla bir
trim. Trim şu koşulların hepsi tutunca istenir: işlem bitti; uçuşta işlem yok (`IsRunInFlight` false, Sync/Clean/Optimize/
checkout/pull busy değil); bekleyen log yüklemesi yok; **pencere görünürse** bitiş finali oynamıyor
(`GraphView.IsEndFinalePlaying` false — final bitince GraphView bir `EndFinaleFinished` olayı yayınlar, kural onu dinler);
**pencere gizliyse** bugünkü koşul (koşu tepside bittiyse gösterge çıkışı tamamlandı). Trim `DispatcherPriority.ApplicationIdle`
ile ve finalden sonra kısa bir pay (1,5 s; sabit tek yerde) ertelenir; ateşlenince koşullar yeniden sorulur (arada yeni
işlem başladıysa yapılmaz, o işlemin sonunda yapılır). Pencere gizlenirse (`SetSurfaceHidden(true)`) ve trim edilmemiş bir
bitmiş işlem varsa trim hemen istenir (gizlide duraklama görünmez).

**Sinyaller:** `RunViewModel.EndedRunSerial` (var) + yeni `EndedOperationSerial` (Sync/Clean/Optimize bitişleri de artırır;
tek yazıcı, her bitiş yolu). `MainWindow` ctor'daki `EndedRunSerial` aboneliği bu yeni sinyale taşınır.

**Supervisor:** `RunCoordinator.ReportMemory()` → `TrimAndReportMemory()`: önce `MemoryTrim.CollectAndRelease()`, sonra
`MemoryLine.Report`; çağrı yerleri aynı (koşu sonu, Sync sonu) + Clean/Optimize tamamlanmaları. Bellek satırı artık trim
SONRASI değeri taşır (ARCHITECTURE §11.4 böyle yazılır).

**Testler:** `RunBufferReleaseTests.A_visible_window_does_not_collect_after_a_run` →
`A_visible_window_collects_once_after_the_end_finale` (eski iddia + ölçüm gerekçesi doc'ta); yeni: "uçuşta işlem varken
toplanmaz", "işlem başına bir kez", "Sync bitişi de toplar", "final oynarken beklenir, bitince ister", "gizlenince bekleyen
trim hemen gider"; Supervisor: trim'in bellek satırından önce çağrıldığı (sahte toplayıcı sayaçla). Duraklama süresi birim
testle değil §9 ölçümüyle doğrulanır.

### 6.2 Tek kopya: `NarrativeStore` (K3) — Dalga 1

**Sınıf:** `App/Console/NarrativeStore.cs` (WPF'siz, saf). Satırları `List<string>` olarak tutar ('\n' içermez), `Count`,
`CharCount`, `Append(line)` (IPC thread'i), `Clear()`, `Join(from, to)` ('\n' sonekli metin, belge tohumlama/prepend için),
`Slice(from, to)` (kopya). İç kilit; `Append` O(1).

**Görünüm:** `ConsoleView` backlog'u bir arayüze bağlar: `IConsoleBacklog { int Count; string Join(int from, int to); }`.
Anlatı modunda store'un kendisi; proje modunda bugünkü satır listesi (`ListBacklog`). `_loadedFrom` store indeksidir.
Canlı append'te kırpılan satırlar backlog'a **eklenmez** (store zaten taşır); `_loadedFrom += kırpılan`. Tohumlama
(`SeedRunDocument`) tam metin yerine `(store, count)` snapshot'ı verir: görünüm `Join(count − 200, count)` ile kurulur; reseed-drop
sentinel'i ve nesil guard'ı aynen kalır (snapshot `count` + `PostReseedDrop` tek kilit altında).

**VM:** `_runText` ve `_runLineCount` kalkar; `GetActiveLineCount` anlatıda `store.Count`. `GetRunDocumentText()` yalnız test
yüzeyi olarak `Join(0, Count)` ile kalır (üretim çağıranı yok). `ClearConsoleForNewOperation` `store.Clear()` çağırır.

**`_liveLines` daraltma:** satırlar yalnız `_pendingLoad` varken ve onun projesi için biriktirilir (dikişin tek ihtiyacı:
istekle snapshot arası satırlar); `CompletePendingLoad` tamponu boşaltır. Açık sayfanın canlı satırları bugünkü gibi doğrudan
`_projectText`'e gider. `LiveLineCount` sözleşmesi (yükleme yokken 0) korunur; `ReleaseLiveLinesWhenIdle` sadeleşir.

**Testler:** `ConsoleMemoryTests` genişler: 1000 satır canlı anlatı sonrası görünümün kendi satır listesi yok (yeni iç yüzey
`BacklogOwnsCopy == false`), store satır sayısı = basılan satır; tohumlama/prepend/kırpma akışları `ConsoleViewTests` ve
`ConsoleRenderSliceTests` ile store üzerinden; `RunViewModelStateTests`: `_liveLines` yükleme yokken boş kalır, dikiş
sırasında yalnız o proje birikir.

### 6.3 Diske yayılmış anlatı (K3) — Dalga 2

`NarrativeStore` bir spool kazanır: `BeginSpool(path)` koşu başlayınca (`RunStartedEvent.LogDirectory` → `<LogDirectory>\narrative.log`;
ad tek yerde: `Core/Logs/RunLogPaths`) çağrılır; o ana dek bellekte biriken satırlar önce dosyaya yazılır, sonra her `Append`
tamponlu `StreamWriter`'a eklenir (UTF-8, satır başı '\n'); satır başlangıç ofsetleri `List<long>`'da tutulur (8 B/satır).
Bellekte yalnız son `MemoryWindowLines` (2.000, sabit tek yerde) satır kalır; daha eskisi dosyadan okunur (`Join/Slice`
önce `Flush`, sonra ofsetten okuma; 200 satır ≈ 70 KB, UI thread'inde kabul edilebilir). Spool yoksa (Sync, Clean, koşu klasörü
henüz bilinmiyor) ya da yazım başarısızsa (IO hatası bir kez konsola `warning:` satırı) store tamamen bellekte kalır — spool
bir optimizasyondur, kaybı doğruluğu bozmaz. Yeni işlem `Clear()` ile dosyayı kapatır (dosya koşu klasöründe kalır ve koşu logu
saklama kuralıyla birlikte silinir, §8.5). Konsol "Copy log" davranışı değişmez (anlatıda düğme yok).

**Doküman:** §8.5 ve §16'ya `narrative.log` (App yazar, koşu klasöründe, saklama aynı); §13.5 backlog anlatımı yeniden yazılır.

**Testler:** spool dosyasına yazım + pencere dışı okuma (geçici klasör), spool yokken bellek, yazım hatasında geri dönüş,
`Clear` sonrası dosya kapalı; görünüm testleri için 2.000 satırın üstünde tepeye kaydırmanın doğru dilimi getirdiği.

### 6.4 Motor uykusu (K2) — Dalga 3

**EngineHost:** `SleepAsync()` — işsizken `ShutdownCommand` gönderir, çıkışı ≤ 2 s bekler, çıkmazsa öldürür; bu çıkış
`EngineExited` ÜRETMEZ (kasıtlı; generation damgasıyla ayrılır); `IsAsleep` true, `EnginePid` null. `WakeAsync()` — uykudaysa
`StartAsync()` (yeni generation, `engineReady` beklenir, `StartupTimeout` aynı), değilse no-op. `DisposeAsync` uykudaki motor için
göndereceği bir şey olmadığını bilir.

**VM — tek kapı:** `EnsureEngineAwakeAsync()`; motoru isteyen HER kullanıcı yolu başında çağrılır: `BeginRunAsync`, `SyncCoreAsync`
(tüm kipler, otomatik dahil), Clean, Optimize, checkout, pull, `ListBranches`, `LoadProjectLogAsync`. Uyanma sırasında şerit
mevcut motor-başlıyor metnini gösterir; uyanınca `OnEngineResumed(version, pid)` yalnız kimlik alanlarını günceller — konsola
satır yazmaz, açılış Sync'ini TEKRARLAMAZ (açılıştaki `OnEngineReady` yolundan ayrı). Uyanma başarısız olursa bugünkü
`EngineUnavailable` yolu.

**Uyku kuralı (saf `EngineIdleRule`):** ayar açık, uçuşta işlem yok, bekleyen log yüklemesi yok ve son motor etkinliğinden
(komut gönderimi ya da olay alımı) bu yana ≥ 5 dk (sabit tek yerde) → uyut. 200 ms'lik tikin gizlide de koşan bölümünden
(bekçiyle birlikte) sorulur; pencere gizliyken de uyur.

**Etkileşimler:** sessizlik bekçisi yalnız istek pencerelerinde sayar, uyanma istekten önce bittiği için etkilenmez; "Restart
engine" eylemi uyku için gösterilmez; `WhyRunCannotStart` uykuyu "unavailable" saymaz; commit sonrası otomatik Sync ve
pencereye dönüş Sync'i önce uyandırır; tepsi Build kısayolu önce uyandırır; MSBuild çocukları işsizken zaten yoktur,
`run-inflight.json` boştur.

**Ayar:** General → yeni grup ENGINE: "Stop the engine when idle" — "After five minutes without work the engine process exits.
It starts again in a moment when you build or sync." Varsayılan açık; `ui-state.json` `StopEngineWhenIdle`; `SettingsFile`
round-trip; mevcut ayar satırı desenine (katalog + draft + store) birebir uyar.

**Doküman:** ARCHITECTURE yeni §4.7 "Engine sleep", §4.4 sonlandırma matrisine satır, §13.3 ayar, §16 anahtar; README ayar listesi.

**Testler:** `EngineHostTests` (uyku `EngineExited` üretmez; uyanma `engineReady` döner; pid değişir; dispose uykuda sorunsuz);
VM: uyku kuralı tablosu; her yol uyandırır (sahte motor, sayaç); uyanma konsola yazmaz ve Sync tetiklemez; uyanma hatası
unavailable yolu; ayar kapalıyken uyumaz; `EngineSilenceWatchdogTests` uyanma süresinin pencereye girmediğini pinler; ayar
testleri (varsayılan + dosya round-trip, `ResolveAtFullPrioritySettingsTests` deseni).

### 6.5 Kompakt konsol (K4) — Dalga 2

**Ayar:** General → yeni grup CONSOLE: "Compact console" — "Errors in full, the first warnings of each project and a count of
the rest; every other MSBuild line stays in the project log. Off: every line." Varsayılan açık; `ui-state.json` `CompactConsole`.
Değişiklik bir sonraki işlemde geçerli olur (mevcut tampon yeniden yazılmaz; ayar açıklamasında söylenmez, dokümanda söylenir).

**Süzgeç (saf `CompactConsoleFilter`, `App/Console`):** yalnız `projectLog` satırlarına uygulanır (uygulamanın kendi anlatı
satırları ve motor notları süzülmez). Sınıflandırma `ConsoleLineClassifier` (tek tanım). Proje başına durum: gösterilen uyarı
sayısı, koda göre gizlenen uyarı sayısı. Kurallar:

| Girdi | Kompakt kipte anlatıya |
|---|---|
| `ProjectStartedEvent` | `building <name>` (bir satır) |
| Error satırı | aynen |
| Warn satırı, projede ilk 3 | aynen |
| Warn satırı, 4. ve sonrası | gizlenir; kodu (`MSB3277`, `CS1591`… — `: warning <KOD>:` deseninden; yoksa `other`) sayılır |
| Diğer satırlar (Info/Dim/Cmd) | gizlenir |
| proje bitişi (`ProjectDone`, her sonuç) ve gizlenen uyarı > 0 | `<name>: <n> more warnings — MSB3277 ×200, CS1591 ×90 — see the project log` (ilk 3 kod, çoktan aza; Warn rengi) |

Kapalı kipte bugünkü davranış (her satır). Proje logu sayfası ve disk her zaman tam. Başlık: kompakt kip açıkken satır
sayacının yanında `compact` etiketi (aynı tipografi, soluk); kapalıyken yok. "N lines" sayacı anlatıdaki gerçek satır sayısıdır
(kompakt satırlar). Uyarı özet satırı tıklanabilir değildir (konsolda tıklanabilir öğe yok kuralı).

**Sonraki adım (bu spec dışı, karar ister):** süzgeci motora taşımak (`StartRunCommand.ConsoleDetail`, açık sayfa için
"watched project") IPC ve motor CPU'sunu da küçültür; §5.5 dikişi değişir.

**Testler:** süzgeç tablosu birebir (saf), proje başına sayaçların işlem başında sıfırlandığı, ayar kapalıyken tüm satırların
geçtiği, başlık etiketi, ayar varsayılanı + round-trip, kompakt kipte `store.Count`'un özet satırlarını saydığı.

### 6.6 Gözlemlenebilirlik ve bütçe — Dalga 1

- `DiagnosticsReport` Environment grubuna üç satır: App private / GC committed / heap (`MemoryLine.Format` tek biçim, Core).
- **Bütçe testleri (yapısal, deterministik):** sentetik 100 k satırlık koşu (`ProjectLogEvent` akışı, headless VM + görünüm):
  (a) görünüm kendi kopyasını tutmaz; (b) `_liveLines` yükleme yokken 0; (c) Dalga 2 sonrası `store.CharCount` ≤ pencere
  (2.000 satır × ortalama satır) ve disk dosyası tam satır sayısında; (d) yeni işlem her şeyi sıfırlar.
- **Ölçüm testi (`[SkippableFact]`, `BO_MEASURE_MEMORY=1`):** aynı sentetik koşu sonrası trim → yönetilen yığın büyümesi ≤ sabit
  bütçe (Dalga 1: ≤ 1,2 × metin; Dalga 2: ≤ 8 MB). §17.5 listesine girer; CI'da koşmaz.

## 7. Dalgalar ve beklenen sonuç

| Dalga | İçerik | Beklenen boşta toplam (App + Sup private) | Kapanış |
|---|---|---|---|
| 1 | §6.1 trim (App + Supervisor) · §6.2 tek kopya · §6.6 tanı + bütçe testleri | 845 → **≤ 500** (ölçülen tepsi yolu 444 + backlog 59'un kalkışı) | §9 ölçümü, rapor |
| 2 | §6.3 diske yayılmış anlatı · §6.5 kompakt konsol | **≤ 435** (koşu sonrası native ~330 + UI ~35 + Sup ~45 + pencere) | §9 ölçümü, rapor |
| 3 | §6.4 motor uykusu | **≤ 390** | §9 ölçümü, rapor |

Dalgalar bağımsız merge edilir (her biri kendi branch'i, kendi tam süiti, kendi ölçümü). Sıra değiştirilmez: 2, 1'in store'una;
3, 1'in trim kuralına dayanır.

## 8. Riskler ve karşılıkları

| Risk | Karşılık |
|---|---|
| Görünür pencerede trim duraklaması hissedilir | finalden sonra + ApplicationIdle + 1,5 s pay; §9'da ölçülür; Dalga 2 canlı yığını küçültünce duraklama düşer |
| Spool yazımı diskte başarısız | bellekte devam, tek uyarı satırı; doğruluk etkilenmez |
| Ofset indeksi büyür | 8 B/satır (83 k satır = 0,7 MB); kabul |
| Kompakt kip bilgi gizler | ayar; proje başına özet satırı sayıları söyler; proje logu tam; başlıkta `compact` |
| Uyanma gecikmesi | 1–2 s, şerit metni; ayar ile kapatılabilir |
| Uykudaki motor "öldü" sanılır | `EngineExited` kasıtlı çıkışta üretilmez (test pinler) |
| Zamanlama testleri paralel yükte düşer | tek başına tekrar; ortam kaynaklıysa `LocalOnly` (CLAUDE.md) |

## 9. Ölçüm protokolü (her dalganın kapanışı)

```powershell
dotnet build src\BuildOrchestrator.App\BuildOrchestrator.App.csproj -c Release        # uygulama kapalı
.\.claude\temp\mem-2026-10-13\mem-after-build.ps1 -Dir .\.claude\temp\mem-2026-10-13\wave<N>-visible   # görünür yol, ~10 dk
.\.claude\temp\mem-2026-10-13\mem-tray-run3.ps1   -Dir .\.claude\temp\mem-2026-10-13\wave<N>-tray      # tepsi yolu, ~7 dk
```

Karşılaştırılan satırlar: `S1-postrun-idle-60s` (App WS/Private, `live heap`, Supervisor), `S2`, `S3`; tepside `T1`, `T2`.
Taban: rapor §2 ve §6. Scriptler F6'yı ana pencereye `WM_ACTIVATE`+`WM_SETFOCUS`+`WM_KEYDOWN/UP` ile POST eder (odak gerekmez);
ölçüm sırasında uygulamaya dokunulmaz; makinede başka derleme olmamalı. Sonuç `.claude/outputs/<tarih>-idle-memory-wave<N>-result.md`.

## 10. Kabul ölçütleri

1. Her dalga sonunda tam süit yeşil; yeni/yeniden yazılmış testler listedeki davranışları pinler; kaynak guard'ı `GC.Collect(`
   için tek dosya.
2. §9 ölçümünde dalganın hedef toplamı tutturulur; tutmazsa dalga merge edilmez, fark raporlanır.
3. Konsol: koşu sonrası tepeye kaydırınca tüm geçmiş gelir (Dalga 2'de diskten); proje sayfası tam; "N lines" doğru.
4. Motor uykusu: 5 dk sonra Supervisor süreci yok; Build/Sync/branch değişimi/commit sonrası Sync/proje logu açma uyandırır;
   şeritte hata yok.
5. Dokümanlar aynı işte güncel: ARCHITECTURE §4.7 (yeni), §4.4, §8.5, §11.4, §13.3, §13.5, §16, §17.5, §22; README ayar listesi.
