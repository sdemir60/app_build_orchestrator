# Planlama görünürlüğü + motor sessizlik watchdog'u — TDD dökümü

Kullanıcı kararı: (1) planlama penceresi görünür olsun — **Öneri B** (faz + canlı adım satırları),
(2) planlama sırasındaki Stop süresine DOKUNULMAZ (davranış zaten doğru, yalnız uzun sürüyor),
(3) motor susarsa kurtulma yolu — **Öneri B** (bağlamsal watchdog + mevcut "Restart engine" aksiyonunu
o durumda da göster).

## Kanıt (neden bu iki iş)

- `BeginRunAsync` ([RunViewModel.cs:449](../../src/BuildOrchestrator.App/ViewModels/RunViewModel.cs))
  konsolu TEMİZLER, `IsStarting`'i açar, komutu gönderir — `Phase`'e ve konsola DOKUNMAZ. Şerit önceki
  metinde (`Stopped …` / `Ready …`) kalır, konsol boş. Motor `runStarted`'ı ancak planlama bitince yazar
  (`RunCoordinator.cs:798`).
- Planlama, Sync'in yaptığı işin aynısını TEKRAR yapar (`Program.BuildRunPlan` ↔ `SyncWorkspaceService.RunAsync`)
  ve bu doğrudur (ağaç Sync'ten sonra değişmiş olabilir; ayrıca worktree hazırlığı + MSBuild çözümü yalnız
  burada var). Sorun tekrar değil, **tek satır bile yazmaması**.
- "Restart engine" aksiyonu ZATEN VAR (`StickyRibbon.xaml:45`, `RunViewModel.RestartEngineAsync`) ama
  görünürlüğü `EngineDiedMessage`'a bağlı (`StickyRibbon.xaml.cs:220`) — yani motor process'i GERÇEKTEN
  öldüğünde. Yaşayan ama donmuş motorda hiç görünmez.
- Ping/pong bu vakayı yakalayamaz: donan run task'ıydı, komut döngüsü canlıydı — pong dönerdi. Doğru sinyal
  **bekleyen geçiş + motor sessizliği**.

## Görevler

| # | İş | Kırmızı test |
|---|---|---|
| 1 | Core: paylaşılan planlama adım metinleri (`PlanProgressLines`), Sync onları kullanır | metin formatları + Sync'in aynı kaynaktan yazdığı |
| 2 | Contracts: `PlanProgressEvent` | polymorphic round-trip |
| 3 | Supervisor: planner'a progress sink; adımlar `runStarted`'dan ÖNCE akar | koordinatör sırayla planProgress yazar |
| 4 | App: `AppPhase.Planning` + konsol satırı + şerit satırı + `planProgress` handler | Build'e basınca faz/konsol; runStarted→Running; hata/motor-ölümü→resting |
| 5 | App: motor sessizlik watchdog'u + "Restart engine"i o durumda da göster + restart'ta run state sıfırla | sahte saatle eşik altı/üstü; restart kilidi açar |
| 6 | Doküman (ARCHITECTURE §4.5/§13.2/§22, README) | — |

## Değişmezler

- Metin TEK yerde: Sync ve run planlaması AYNI formatter'ları çağırır (kopya yasak).
- Watchdog HİÇBİR ŞEYİ otomatik açmaz — yalnız kapıyı gösterir. Graceful drain dakikalarca sürebilir.
- Saat enjekte (`_nowMs`), tick `MainWindow`'un mevcut `DispatcherTimer`'ından (`TickElapsed`) — D8: sleep/poll yok.
