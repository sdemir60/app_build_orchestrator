# It-1 (Sync/Graph/Planning + It-0 Hardening) — Tamamlanma Özeti

Tarih: 2026-07-16 21:09 · Branch: `it1-sync-graph` (main base `4a87782`, 29 commit, **push edilmedi**) · HEAD `4e529a5`

## Ne yapıldı

v7 Part C **It-1** subagent-driven-development ile task-by-task uygulandı. TDD plan: [.claude/outputs/2026-07-16-17-25-it1-tdd-plan.md](../outputs/2026-07-16-17-25-it1-tdd-plan.md). 17 task (her biri implementer + reviewer subagent; bulgularda fix + re-review döngüsü).

### Faz 0 — It-0 final review devir sertleştirmeleri (5)
- **T1** `NdjsonWriter` base-type overload'ları (polimorfik discriminator garantisi; generic private → 21 çağrı yeri CS0308 ile zorunlu güncellendi).
- **T2** `ProcessRunner` kill-path: bounded post-kill wait + geniş catch; **review Critical**: stdout/stderr okumaları da bounded yapıldı (stalled-kill'de hang yok).
- **T3** `EngineHost` concurrency (4 fix pass): `volatile`/`Interlocked` generation; ReadLoop framing-hatası loop içine + engine kill + `EngineExited` (Supervisor exit-2 simetri); **monotonik generation-scoped exit-gate** (`TryClaimExit` — stale gen yeni gen'in raporunu çalamaz); atomik idempotent `KillCurrent`; graceful `ShutdownCommand`; startup-framing `_ready` surface.
- **T4** App copy-target: TFM-agnostik `*\**\` pattern + `RemoveDir` (stale temizliği, yapı-koruma).
- **T5** `CascadeKillTests` `handles.Count>=5` vakum-geçiş guard'ı.

### Faz 1-5 — Core Sync/graph/planning (12)
- **T6** Contracts DTO'ları (`ProjectNode`/`BuildPlan`/`BuildState`/`HintPathRef`/`SolutionRef`/enum'lar). `ProjectNode`'a `SequenceEqual` structural equality (gelecekteki incremental plan-diff için doğru).
- **T7** `WorkspaceScanner` (recursive + ignore + deterministik).
- **T8** `CsprojEvaluator` (raw-XML; AssemblyName/Compile/HintPath/ProjectReference; namespace-tolerant LocalName; glob+link; SDK/legacy). **review Important**: recursive `**` glob sessiz-sıfır-dosya bug'ı düzeltildi + `;`-split.
- **T9** `EvaluationCache` (mtime+**length**+hash fingerprint; atomik flush). **review Critical**: mtime-only fast-path stale-cache (Windows aynı-tick) → length eklenerek çözüldü + disk-persistence/touch-only testleri.
- **T10** `ProducerMap`+`GraphBuilder` (HintPath-primer/ProjectReference-ikincil; ambiguity dışlama; self-edge/dangling drop; D8).
- **T11** `HintPathClassifier` (T71 3-sınıf: Edge/ThirdParty/OsysPlatform/Unclassified; `RepoResolveRatio = edge/(edge+unc)`; divide-by-zero→1.0).
- **T12** `TopoSort` (Tarjan SCC + Kahn; dependency-first; deterministik; diamond/multi-SCC opus-trace ile doğrulandı).
- **T13** `SolutionMapper` (T32; sln parse regex gerçek VS/.vbproj/solution-folder'a karşı doğrulandı; 0/>1).
- **T14** `BuildPlanBuilder` (T26; scan→eval→graph→solution→topo→BuildPlan; uçtan-uca integration test).
- **T15** `WillBuildEvaluator`+`BuildPreview` (T53; dirty=true/güncel=false/imza-yok=null; inCycle=false; precedence testli).
- **T16** `StaleObjDetector` (T72; **dokunmadan warn**). **review Important**: whole-file scan false-positive → yalnız `targets` anahtarları (JsonDocument) taranarak düzeltildi.
- **T17** OSYS integration (acceptance kanıtı).

## Kabul kanıtı (It-1 acceptance — 8/8 ✅, opus final review doğruladı)
Kartlar build-order'da · cycle rozeti verisi (InCycle+Cycles) · willBuild kümesi testli · OSYS cache-hit hızlı · sınıflandırma metriği raporlanır · unclassified→warn · stale-obj no-touch warn · 5 sertleştirme.

**Gerçek OSYS (`D:\Projects\Delta\OSYS`):** 177 csproj · **Edge=1060** (spike'ın 1060'ı birebir) · ThirdParty=78 · OsysPlatform=716 · **Unclassified=0** · **RepoResolveRatio=1.0** (≥%95) · **AmbiguousDlls=0**. Toplam 1854 HintPath = spike yer-gerçeği. Reviewer spike ham verisiyle çapraz doğruladı (66 Program-Files + 716 platform = 782 = spike unmatched-other) → metrik **meşru, gamed değil** (Edge check external'den önce olduğu için yapısal olarak imkansız).

## Final durum
- **Clean build: 0 uyarı, 0 hata** (CS0420 x3 EngineHost `volatile` redundancy'si giderildi). Full suite: **85 geçti / 1 skip (önceden var olan CompositeFont) / 0 fail**.
- Opus final whole-branch review: **Ready with must-fixes** → tek must-fix (`EvaluationCache.Load` catch'e `UnauthorizedAccessException`) uygulandı; kalan roll-up bulguları It-2/It-3'e ertelendi.

## v7 yasaklarına uyum
In-process MSBuild yok (evaluation raw-XML — gerekçeli sapma, planda kayıtlı) · OutDir okunmaz · stdout yalnız NDJSON · determinizm/D8 (sleep-poll yok) korundu.

## Ertelenen (It-2/It-3 fix-wave) — ledger'da tam liste
`.superpowers/sdd/progress.md` "Minor findings roll-up": TopoSort diamond/multi-SCC testleri (öneri), StaleObjDetector malformed-JSON robustness testi + expectedTfm tam-moniker doc'u, çeşitli test-coverage genişletmeleri, symlink-cycle guard. Hiçbiri correctness-blocker değil.
