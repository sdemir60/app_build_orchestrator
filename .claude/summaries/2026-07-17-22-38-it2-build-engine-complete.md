# It-2 (Rebuild motoru + per-run log + copy-aware Stop/Continue + konsol) — Tamamlanma Özeti

Tarih: 2026-07-17 22:38 · Branch: `it2-build-engine` (main base `f5cc736`, **28 commit**, HEAD `c749292`, **push edilmedi**)

## Ne yapıldı

v7 Part C **It-2** subagent-driven-development ile task-by-task uygulandı. TDD plan: [.claude/outputs/2026-07-17-12-39-it2-tdd-plan.md](../outputs/2026-07-17-12-39-it2-tdd-plan.md). 15 task (her biri implementer + reviewer subagent; bulgu → fix → re-review döngüsü). Kullanıcı onaylı kararlar: **I2-K1** iki-katmanlı Stop (Graceful=proje sınırı / Hard=anında TerminateJobObject; torn-DLL = "kill'den sağ çıkan yazıcı yok"), **I2-K2** obj-izolasyon plan §4 harfiyen (in-place = projenin kendi obj'i; izolasyon It-3/worktree), **I2-K3** kabul = tam 177 OSYS, yeşil = orchestrator-kaynaklı 0 hata.

### Bloklayıcı giriş kriteri (Task 1)
`JobProcessLauncher` `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` ile handle-inheritance izolasyonu — paralel redirected launch'ta kardeş pipe uçlarının çapraz sızması (EOF/deadlock) kökten kesildi. Planın paralel testi hatayı ayırt etmiyordu; `pause` child'ıyla yeniden kuruldu, izolasyon kapatılınca 3/3 timeout ile kanıtlandı.

### Motor + UI (Task 2–13)
- **Contracts** (T2): run/build IPC yüzeyi (startRun/continue, run+project event'leri).
- **SolutionDirResolver** (T3): restore'un `-p:SolutionDir`'i; `Map` ad-bazlı dedup regresyonu düzeltildi.
- **RunLogWriter** (T4): per-run disk log; `AppendLine` dispose sonrası fırlatır (sessiz satır düşürme yerine), "bir append = bir fiziksel satır" invariant'ı `LogChunker`'ın `'\n'` modeliyle örtüşür.
- **MsBuildInvoker** (T5): gerçek `MSBuild.exe` shell-out inner Job'da; **Critical**: başarı-yolu pipe drain'i sınırsızdı ve `PerProjectTimeout` kapsamıyordu (torun copy-event handle sızması → sonsuz hang) → `WaitPumpsBoundedAsync` + terk-edilen-pump onLine latch'i (Task 4'ün throw'uyla çarpışmayı önler).
- **ReadySetScheduler** (T6): K2 ileri-atlamalı, deterministik; resolved = succ|fail|skip (failed dep bloklamaz).
- **RunClock/RunSnapshot** (T7): Continue çekirdeği; enjekte monotonik saat; snapshot yalnız IsDone'da.
- **CopyContention/Retry** (T8): MSB302x'e enjekte backoff'lu retry.
- **RunCoordinator** (T9): plan → N worker → shell-out → disk log + FIFO event pump → graceful/hard Stop + Continue; exactly-once runStopped/runCompleted; stop-during-planning ack-borcu; worker lost-wakeup-safe.
- **T28** (T10): getProjectLog aktif run dizininden + canlı dikiş (ThroughLineNumber).
- **ConsoleBatcher/ConsoleView** (T11): AvalonEdit ~50ms batch flush (A13.2).
- **RunViewModel** (T12): App run UI; **Critical**: Stop/Continue butonları canlı UI'da ölüydü (`[NotifyCanExecuteChangedFor]` eksik) — düzeltildi.
- **Kill mid-build** (T13): gerçek MSBuild ağacı, ≤2s/0-orphan + succeeded DLL'ler geçerli PE.
- **T14**: ertelenen It-1 minor'ları (TopoSort diamond/SCC, StaleObjDetector robustness).

### Kabul koşusu (Task 15)
Gerçek OSYS 177 proje, Parallelism=6: **122 succeeded / 23 failed / 32 skipped / 0 queued**, Outcome.Completed, max in-flight 6 (tavan tuttu), 0 copy-contention retry. **Orchestrator-kaynaklı = 0 → YEŞİL.** 23 hatanın tamamı repo-kaynaklı (stale-obj NewSales* kökleri + CS0006 cascade + gerçek CS/MC compile). Records: [.claude/outputs/2026-07-17-21-01-it2-records.md](../outputs/2026-07-17-21-01-it2-records.md).

## Final whole-branch review (Fable) + fix waves
Verdikt "With fixes". **Riskli bölge (Stop/copy-aware/scheduler-concurrency — Supervisor+Core) ROCK SOLID: Critical yok.** 3 Important App seam'inde (cross-run stitch kirlenmesi, PendingLoad hang, planlamada Stop erişilemezliği). Fix wave 1 üçünü kapattı → re-review IT'in getirdiği bir Critical regresyon buldu (IsStarting stuck) → fix wave 2 → re-review Approved (2 non-blocking It-3 item) → fix wave 3 (`runFailed` allowlist) reachable olanı kapattı.

## Final durum
- **Clean build 0 uyarı/0 hata.** Non-acceptance suite **214 geçti / 1 skip (önceden var CompositeFont) / 0 fail**. Acceptance GREEN.
- **Yeni It-3 bulguları:** MsBuildOutputEncoding mojibake (UTF-8'i CP1254 sanıyor, log-okunabilirliği); EngineExited VM'e bağlı değil (engine ölünce IsStarting stuck); depIssue sistemi (T54) CS0006 cascade'i ▲ etiketlemeli. Tam liste: `.superpowers/sdd/progress.md` It-3 backlog + minor roll-up.

## v7 yasaklarına uyum
In-process MSBuild yok (shell-out) · OutDir okunmaz · stdout yalnız NDJSON · determinizm/D8 (sleep-poll yok) · v1 flag'leri sabit · planlama Core'da.

## Bekleyen
- **Merge/push kullanıcı onayında** (proje kuralı). main de origin'e push edilmedi.
- **Manuel WPF geçişi** (records §7): canlı rebuild, Stop→Continue elapsed korunumu, karta tıkla→tam log (ilk satır gerçek MSBuild komutu), konsol akıcılığı, Task 12 CanExecute canlılığı.
