# It-3 Tamamlandı — Incremental + Git + depIssue + Retry + ETA

**Tarih:** 2026-07-18 · **Branch:** `it3-incremental` (main `37e97d4`'ten; HEAD `bea483e`) · **Plan:** [.claude/outputs/2026-07-18-00-29-it3-tdd-plan.md](../outputs/2026-07-18-00-29-it3-tdd-plan.md) · **Otorite:** v7 Part C It-3

> **Branch main'e MERGE EDİLMEDİ ve origin'e PUSH EDİLMEDİ** — ikisi de kullanıcı onayında (It-2 deseni). 34 WIP/fix commit, main'in üstünde.

## Sonuç — It-3 tam, merge-ready

`superpowers:subagent-driven-development` ile **19 task** (12 It-3 task + It-2 devir girdileri) task-by-task uygulandı: her task için implementer → task review (spec+quality) → gerekli yerde fix loop → sonraki. Sonda **Fable whole-branch review → MERGE READY** (tek doc-blocker düzeltildi). Ledger: `.superpowers/sdd/progress.md` (her task + tüm minor roll-up + It-4 backlog).

- **Build 0 uyarı / 0 hata.** Non-acceptance suite **473 geçti / 1 skip** (pre-existing CompositeFont). Acceptance **3/3** (gerçek OSYS).
- **Incremental Build UÇTAN UCA ÇALIŞIYOR (canlı OSYS kanıtı):** Run1 Build 177 → 122 succ / 23 fail / 32 cycle-skip (19s); **Run2 (kaynak değişmeden) → 122 "up to date" skipped, 0 önceden-başarılı atlanmadı**, yalnız 23 repo-bozuk yeniden (5.7s). Minimal-rebuild: 1 dirty proje → hedef + 3 direct dependent, 100 alakasız skipped. Ordering assert: **145 ProjectStarted, 0 ihlal.**
- **K1 doğrulandı:** OSYS aktif branch (`feature/service-invoice-…`, HEAD `6b4ecba…`) koşu öncesi/sonrası DEĞİŞMEDİ (assert'li). Tüm git production yolu salt-okur (fetch ref-only; checkout/switch/pull/reset/commit/stash YOK).

## Uygulanan başlıklar

**Incremental çekirdek (T25/T27/T7 + user-onaylı refinement):** GitService (ProcessRunner tabanlı; HEAD/branch/dirty/ls-tree, edge input T11: no-commits/detached/shallow → treat-as-dirty; locale-independent no-commits) · BuildSignature (byte-stable, hash-per-term collision-proof; **per-project committed fingerprint** = ls-tree blob-hash'leri, global HEAD yerine — kullanıcı kararı, A6 "projeyi etkiliyor") · IncrementalPlanner (topo-memoized; Safe fresh-upstream cascade / Fast frozen-upstream suppression) · BuildStateStore (global JSON, single-writer atomik temp+rename, deterministik flake-serialized testler) · IncrementalRunBinder (abs→repo-relative path resolution; Program.cs wiring; Build mode pre-skip "skipped — up to date"; persist on success).

**Git subsistem:** Sync-fetch ref-only + offline degrade (T69/K1) · PathSanitizer (T13, max-suffix collision-free worktree ad) · WorktreeManager (T29/T14, 3-durum matris + K3 niyet satırı + pool/LRU; shared GitCommandExecutor) · obj-izolasyon worktree yolunda (T10 seam).

**Diğer motor:** Layer pattern engine (T15, hard barrier + reverse-dep warn) · depIssue propagation (T54, direct+inherited root'lar, dedup, resolved={succ,fail,skip}) · Retry failed + Continue re-queue stopped (T55) · ETA (T70, EMA 0.75/0.25 + 400ms + 5s round + almost-done; raw-saniye gösterim) · StaleObjDetector wiring (T72).

**App/VM (VM-seviyesi — pixel It-4):** EngineExited → RunViewModel reset (wedge fix) · buildPreview event + will-build/succeeded→clean VM state · depIssue ▲ VM · ETA text.

**Devir/cleanup:** MsBuildOutputEncoding pure UTF-8 (mojibake fix) · sync-I/O kilit dışı · IsDone reformülasyon · ctor dedup · ölü kod (STARTUPINFOW, _stopRequested, GetACP) · XML-doc · Program.cs tek-scan · K1 Sync doc fix.

**Cross-cutting bug (T19'da bulundu):** ProcessRunner child stdin'i kapatmıyordu → git.exe Supervisor NDJSON pipe'ını miras alıp ~30s asılıyordu (tüm incremental planlamayı bloklardı) → redirect+close. Reviewer: yalnız git/vswhere'e scope'lu, MSBuild JobProcessLauncher kullandığı için regresyon yok.

## It-4 devir backlog (final review + task review'lardan — progress.md'de detaylı)
- **[Önemli] Cycle-tangled transitive under-build (stale):** yalnız SCC üzerinden erişilen downstream, upstream değişse de skip edilebilir — pre-existing Task-7 sınırı, bounded, it3-records §3'te belgeli → It-4 SCC-aware propagation.
- **[Önemli] depIssue-persist stale-skip penceresi:** depIssue taşıyan succeeded dependent, upstream kaynak-değişmeden yeşile dönerse skip edilebilir → tek-satır kapatma: depIssues doluyken persist etme.
- **[Önemli] Build pre-skip + persist için deterministik (non-acceptance) test yok** (yalnız OSYS acceptance) → in-process test ekle.
- Worktree e2e BUILD wiring · sync-workspace IPC/UI · pixel/kart/graf render · ETA live-tick · LayerEngine warn'ları (layer UI'dan ÖNCE kapat) · StaleObj double-Evaluate perf · Upsert batching · repoRoot==toplevel · inPlace-resolved-worktree.

## Kanıt dosyaları
- **It-3 acceptance kayıtları (canlı sayılar):** [.claude/outputs/2026-07-18-12-37-it3-records.md](../outputs/2026-07-18-12-37-it3-records.md)
- **It-3 TDD planı (19 task):** [.claude/outputs/2026-07-18-00-29-it3-tdd-plan.md](../outputs/2026-07-18-00-29-it3-tdd-plan.md)
- **Task ledger + minor roll-up + It-4 backlog:** `.superpowers/sdd/progress.md`
