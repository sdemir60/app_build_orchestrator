# Hard stop + Continue'nun kaldırılması

## Karar ve gerekçe (önceki kararın tersine dönüşü)

Bu oturumun ortasında hard kill masadan kaldırılmış, graceful stop korunmuştu. Gerekçe: *yarıda kalan işi
Continue ile sürdürebilmek ve ortak çıktı dizininde torn DLL bırakmamak.* Kullanıcı Continue'yu kullanmayı
denedi, çalışmadı ve zaten istemediğine karar verdi: **Stop'tan sonra Build'e basmak yeterli.**

Continue kalkınca graceful'ün gerekçesi de kalkıyor:

- Torn DLL koruması zaten Continue'dan bağımsız: başarısız biten HER yol stored `BuildState`'i geçersizleştirir
  (`RunCoordinator.cs` ~1055) → öldürülen proje bir sonraki Build'de yeniden derlenir.
- Öldürülen bir projenin dependent'ları henüz BAŞLAMAMIŞTIR (sıra gereği) — torn bir DLL'e link eden kimse yok.

Build kapsamı (kullanıcı kararı): **normal incremental Build.** Stop'tan önce başarıyla bitmiş projeler
"up to date" diye atlanır; öldürülenler + kuyrukta kalanlar derlenir.

## Bulgular (bloklayıcı → önemli)

### B1 · `runInProgress` UI'yı kalıcı kilitliyor (KANITLI)

Motor, run slotunu `_runActive = false` ile tüm event'ler yazıldıktan SONRA bırakır
(`RunCoordinator.ExecuteRunAsync` finally). Yani `runCompleted` App'e ulaştıktan sonra kısa bir pencere boyunca
slot hâlâ doludur. O pencerede gönderilen bir `startRun` → `error(runInProgress)`.

App tarafında `runInProgress` `RunEndingErrorCodes`'ta DEĞİL → `OnError` erken döner → `BeginRunAsync`'in set
ettiği `IsStarting` **hiç geri alınmaz**. Sonuç: UI kilit penceresinde donar, Stop görünür ama arkada başlamış
bir run yoktur.

`runInProgress`'in run state'ine dokunmaması DOĞRUdur (koşan run'ı yıkmamalı) ama `IsStarting`
REDDEDİLEN isteğin kendi bayrağıdır — o mutlaka temizlenmelidir. Gerçekten koşan bir run varsa kilidi zaten
`IsRunning` sürdürür.

### B2 · `Stopping` fazı asılı kalabiliyor (yapısal düzeltme)

Kullanıcı "Stop dedim, Stopping'te kaldı" bildirdi. Bu durum koddan TÜRETİLEMEDİ — izlenen her yolda
`runStopped` dönüyor ve faz çözülüyor. Tahmin yerine yapıyı sağlamlaştırıyoruz:

`OnRunStopped` bugün `_sawRunStarted` ise ERKEN DÖNER ("runCompleted az sonra gelecek"). Bu, fazın çözülmesini
bir olay sıralaması varsayımına bağlar. Oysa koordinatör `runStopped`'ı zaten TÜM in-flight sonuçları
raporladıktan sonra yazar (`RunSegmentAsync` finally, `_finishing` kapısı) — yani `runStopped` görüldüğünde
koşan bir şey KALMAMIŞTIR.

Yeni kural, dalsız: **`runStopped` → faz `Stopped`, run state serbest.** `runCompleted` arkadan gelirse zaten
aynı fazı yazar (flicker yok). `_sawRunStarted` alanı bu okumadan sonra hiçbir yerde OKUNMUYOR → tamamen
kaldırılır.

### B3 · Stop = hard kill

`StopAsync` → `StopKind.Hard` → `TerminateJobObject(inner)`. Uçuştaki tüm `MSBuild.exe` ve alt process'leri
anında ölür, `projectFailed("stopped")` raporlanır.

### B4 · Continue UI'dan kaldırılır

`ContinueCommand`/`CanContinue`, split-button'ın stopped dalı, BuildMenu'nün `continue` maddesi, F5'in
`stopped → Continue` dalı ve `ShortcutAction.Continue`. Motor tarafı (`RunMode.Continue`, `HasResumableRun`)
kontratta KALIR — `StopKind.Hard`'ın tersi durum: yüzey kapanır, yetenek durur.

### B5 · Metinler

- Stopping satırı: "finishing N in flight" artık yalan (bitmiyorlar, ölüyorlar) → "terminating N in flight".
- Stopped satırı: "rest queued" resumability ima ediyor → "{N} not built".
- Konsol notu: "no new projects will start; N in flight will finish" → öldürme dilinde yeniden yazılır.
- BuildMenu'de Build'in stopped varyantı ("Start over — only changed projects") artık TEK davranış.

## Test sırası (her biri önce KIRMIZI)

| # | Test | Yer |
|---|---|---|
| 1 | `runInProgress` → `IsStarting` geri açılır, koşan run'a DOKUNULMAZ | `RunViewModelTests` |
| 2 | `runStopped` → faz `Stopped`, run state serbest (runStarted görülmüş olsa da) | `RunViewModelStateTests` |
| 3 | Stop → `StopRunCommand(Hard)` gider ve motor `wasHard:true` ack'ler | `RunViewModelTests` |
| 4 | Stopped fazında split-button Build'dir, Continue YOK | `ActionBarTests` |
| 5 | BuildMenu'de `continue` maddesi hiç üretilmez, F5 rozeti Build'de kalır | `ActionBarTests` |
| 6 | F5 stopped'ta Build'e çözülür | `KeyboardShortcutTests` |
| 7 | Stopping/Stopped şerit metinleri | `RibbonTextTests` |

## Değişen pinler (CLAUDE.md: eski iddia + gerekçe yazılır)

- `A_stop_during_planning_leaves_stopping_for_the_resting_phase` → artık `Stopped` bekler. Eski iddia "run hiç
  başlamadı, dinlenmeye dönülür"; yeni kural tek dallı ("runStopped → Stopped") ve fazı asılı bırakma
  ihtimalini yapısal olarak kapatıyor.
- `Stopping_line_reports_the_in_flight_count_and_drops_the_eta` → "finishing" yerine "terminating".
- `Stopped_line_shows_progress_and_rest_queued_in_dim_text` → "rest queued" yerine "{N} not built".
- `Stop_sends_StopRunCommand_graceful_and_engine_acks_it` → `Hard`/`wasHard:true`.
- BuildMenu Continue testleri (`Stopped_state_moves_the_F5_badge_from_build_to_continue` vb.) → Continue'nun
  ÜRETİLMEDİĞİNİ pinler.
