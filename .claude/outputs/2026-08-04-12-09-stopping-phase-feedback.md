# Stop geri bildirimi — `Stopping` fazı

## Karar

Graceful stop semantiği **korunur** (hard kill masada değil). Değişen tek şey: Stop'a basıldığında
uygulamanın bunu **anında göstermesi** ve o durumdan çıkışın **her yoldan** garantilenmesi.

Kullanıcının "stop çalışmıyor" demesinin sebebi davranış değil, sessizlik: tıklamadan sonra faz `Running`
kalıyor, buton hâlâ "Stop" diyor, spinner dönüyor, ETA sayıyor — tıklamanın kaydedilip kaydedilmediği
anlaşılmıyor.

## Kapsam dışı (bilerek)

- **Hard kill / `StopKind.Hard`:** kontratta ve motorda çalışır durumda kalır, App'ten gönderilmez.
- **Sync iptali:** `Syncing` fazında Stop yok ve `syncWorkspace` Supervisor'ın komut döngüsünü blokluyor
  (`SupervisorHost.cs:117-119`) — ayrı bir iş.
- **Run-bitiren hata sonrası `Running` fazında asılı kalma:** mevcut bir kusur (`OnError` `Phase`'e hiç
  dokunmuyor), bu işte yalnız `Stopping` dalı ele alınır; `Running` dalı kullanıcı kararına bırakılır.

## Tasarım

### 1. Yeni faz: `AppPhase.Stopping`

`Running` ile `Stopped` arasında. Faz makinesi: `empty → boot → syncing → idle → running → stopping →
stopped | done`.

### 2. Giriş — `RunViewModel.StopAsync`

Tıklama anında (gönderim beklenmeden) `Phase = Stopping`, run dokümanına tek satır not. Gönderim
SENKRON başarısız olursa faz geri alınır — `BeginRunAsync`'in "gönderim başarısız → IsStarting geri açılır"
deseniyle aynı (`RunViewModel.cs:470-471`).

`IsRunning`/`IsStarting`'e **dokunulmaz**: motor hâlâ koşuyor, `IsMidRunLocked` sürüyor, branch/worktree/
configuration kilidi ve split-button gizliliği korunuyor.

### 3. Çıkış — dört yol, hepsi kapalı

| Yol | Hedef faz |
|---|---|
| `runCompleted(Stopped/Completed)` | `Stopped` / `Done` (mevcut davranış, değişmez) |
| `runStopped` + `runStarted` hiç gelmedi (planlama sırasında stop) | dinlenme fazı |
| Run-bitiren `ErrorEvent` | dinlenme fazı |
| `OnEngineExited` | `Stopped` (mevcut `Running` dalına `Stopping` eklenir) |

**Dinlenme fazı** = `Topology.Count > 0 ? Idle : Boot`. Bu ifade bugün `RunViewModel.Workspace.cs:142` ve
`:182`'de iki kez yazılı — tek bir `RestingPhase` özelliğine çıkarılır ve üç yer de onu okur (kopya yasağı).

### 4. Buton

`Stopping` fazında Stop butonu görünür kalır, etiketi **"Stopping…"** olur ve `CanStop` false döndüğü için
pasifleşir (`Command="{Binding StopCommand}"` → `IsEnabled` kendiliğinden). `CanStop() => (IsRunning ||
IsStarting) && Phase != Stopping`; `_phase` alanına `[NotifyCanExecuteChangedFor(nameof(StopCommand))]`
eklenir.

Stop butonunun içeriği bugün `BuildButtons()`'ta bir kez yazılıyor; artık duruma bağlı olduğu için
`RefreshBuildArea()`'ya taşınır (tek yazıcı). UIA adı `BuildButtons`'ta ve **sabit** kalır.

### 5. Şerit

`RibbonText.Compose`'a `Stopping` dalı:

- `Building > 0` → `▸ Stopping — {fin}/{wb} · finishing {n} in flight`
- `Building == 0` → `▸ Stopping — wrapping up`

Brush `Brush.TextSecondary` (faz hâlâ etkin). **ETA eki yok**: yeni dispatch olmadığı için ETA yanıltıcı.
Bu dal olmadan `Compose` default'a düşüp "Not ready — no repository selected" diyordu.

### 6. Dokunulmayanlar

- **Satır durumları:** kuyruktaki projeler `queued` kalır (gerçekten öyleler; Continue onlardan devam eder).
  Uçuştakiler `building` kalır — gerçekten derleniyorlar.
- **Animasyon/motion:** spinner dönmeye devam eder, yeni animasyon eklenmez. Motion guard yüzeyi değişmez.
- **F5:** `Resolve` koşarken Stop'a çözer, `StopCommand` pasif olduğu için no-op.
- **Erişilebilirlik:** `StickyRibbon` zaten faz değişimini duyuruyor; yeni faz otomatik duyurulur.

## TDD sırası

| # | Test | Dosya | Beklenen RED |
|---|---|---|---|
| 1 | `Stopping` şerit satırı (in-flight sayısıyla, ETA'sız) | `RibbonTextTests` | default dal → "Not ready…" |
| 2 | `Stopping` + `Building==0` → "wrapping up" | `RibbonTextTests` | aynı |
| 3 | Stop → `Phase == Stopping`, `StopCommand.CanExecute == false` | `RunViewModelTests` | `Phase` `Running` kalıyor |
| 4 | `Stopping`'te `IsMidRunLocked` hâlâ true | `RunViewModelTests` | (3 ile aynı kökten) |
| 5 | Gönderim başarısız → faz geri alınır | `RunViewModelTests` | faz `Stopping`'te asılı |
| 6 | `runCompleted(Stopped)` → `Stopped` + `CanContinue` | `RunViewModelStateTests` | — (regresyon pini) |
| 7 | Planlama sırasında stop → dinlenme fazı | `RunViewModelStateTests` | `Stopping`'te asılı |
| 8 | Run-bitiren hata `Stopping`'te → dinlenme fazı | `RunViewModelStateTests` | `Stopping`'te asılı |
| 9 | `OnEngineExited` `Stopping`'te → `Stopped` | `RunViewModelStateTests` | yalnız `Running` ele alınıyor |
| 10 | ActionBar: `Stopping`'te buton görünür + pasif + "Stopping…" | `ActionBarTests` | etiket "Stop", buton etkin |

## Doküman etkisi

- `ARCHITECTURE.md §4.5` — graceful stop'un UI karşılığı (Stopping fazı, buton, şerit) eklenir.
- `ARCHITECTURE.md §13` — faz makinesi listesi `stopping` ile güncellenir.
- `README.md` — Stop'un ne yaptığı bir cümleyle netleşir.
