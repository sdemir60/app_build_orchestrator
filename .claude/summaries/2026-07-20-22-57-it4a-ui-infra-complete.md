# It-4a TAMAM — ZOR-CUSTOM UI altyapı paketi (8 task + final review)

**Branch `it4a-ui-infra` @ `c5a59d0`** (main `3d28f88`'ten, **20 commit**, 108 dosya, +10064/−119).
Build **0 uyarı / 0 hata** · Suite **761 geçti / 1 atlandı (pre-existing CompositeFont) / 0 hata**.
**main + origin'e DOKUNULMADI — merge/push kullanıcı onayında.**

Plan: [.claude/outputs/2026-07-20-11-02-it4a-tdd-plan.md](../outputs/2026-07-20-11-02-it4a-tdd-plan.md) (UNTRACKED).
Ledger (yerel, gitignore'lu): `.superpowers/sdd/progress.md` — task başına commit SHA'ları + tüm fix dalgaları.
Execution: `superpowers:subagent-driven-development` — task başına fresh implementer → review → fix → re-review.

## Teslim edilenler

| Task | İçerik | Not |
|---|---|---|
| **0** | wpftmp scanner dışlaması + EvaluationCache vanish-tolerance | Devralınan FAIL kapandı |
| **1** | Motion.xaml (80/120/180/280 + 3 KeySpline) + Tokens.xaml (45 brush) + ReducedMotion (canlı `ClientAreaAnimation`) + `--it4a-lab` harness + SampleGraphData (36-node) | Foundation |
| **2 (T57)** | TrackedTextBlock (GlyphRun 0.07em + uppercase, gerçek DPI) | |
| **3 (T56)** | AvalonEdit konsol: colorizer (plain-text-copy garantili) + hibrit typewriter (≤250ms kanıtlı sınırlı) + kaskat (26ms/3 satır) + chunk loader + copy-log + **reseed-dup sentinel** + render slice | 3a+3b; **C-1 Critical** (chunk-loader kalıcı veri kaybı deliği) bulundu+düzeltildi |
| **4 (T58)** | LayoutMetrics (saf) + StickyLayerList (birikimli sticky, virtualization KAPALI) | |
| **5 (T59)** | ScrollAnimator + BottomAnchor (48px) + FollowScroll (550ms/54px) + ⌄ latest pill | LayoutMetrics'i paylaşır |
| **6 (T63)** | Graf: EdgeStyleResolver/GraphLayout/GraphCamera (saf) + GraphView (Shapes yolu, kamera, dash-flow, seçim, ▲ rozet, stagger, **building pulse**) | **A13.2 HARFİYEN**: dash 1.6px'te bölünür **ve** tek clock — ikisi birden |
| **7 (T62)** | Snap Layouts + restore glyph + tray/ilk-X balloon + single-instance + Alt+B | **I-1 Critical** (%100 CPU spin) bulundu+düzeltildi |

## Final whole-branch review (Opus) — "With fixes" → düzeltildi (`c5a59d0`)

Çapraz-kesen temiz doğrulandı: A13.2 invaryantları, T65 kök font kararı (graf `Ideal` yalnız lokal), FontAbWindow
dokunulmamış, sonsuz-clock hijyeni, engine threading kontratı (`_gate` + marshal-free ProjectLogEvent + reseed
sentinel) T59'un yeniden kablajından sonra da sağlam, paylaşılan servis tekliği (tek LayoutMetrics / tek dash clock /
tek MotionSettings), scope kayması yok.

**Per-task review'ların yapısal olarak göremediği 3 Important — hepsi düzeltildi:**
1. **Motion token'larının iki otoritesi vardı** — `MotionSettings.Apply()` sözlüğü kendi hardcoded tablosundan
   yazdığı için `Motion.xaml` fiilen ölüydü (orada değer değiştirmek hiçbir şeyi değiştirmiyordu). Artık baseline
   sözlükten okunuyor; yeni test gerçek dosyayı 0.28→0.32 kaydırıp restore'un **dosyayı** izlediğini kanıtlıyor.
2. **Sticky overlay her scroll karesinde koleksiyon reset ediyordu** (A13.2 ihlali). T58'de tek tetikleyici wheel'di;
   **T59'un animasyonlu scroll'u** bunu kare başına ItemsControl teardown'a çevirmişti. Önbellekli prefix +
   `ReferenceEquals` guard'ı ile kapatıldı.
3. **Task 0'ın catch'i her IOException'ı yutuyordu** → editörün kilitlediği bir csproj **sessizce plandan düşüp**
   build eksik grafla koşabilirdi. Filtre kayboluş vakasına daraltıldı; gerçek IO hataları artık yukarı sızıyor.

## Kapanış (2026-07-21)
1. **Gözle görsel doğrulama YAPILDI (kullanıcı).** Konsol renk/seçim/kaskat, Snap Layouts, restore glyph, X→tray +
   ilk balloon, ikinci instance'ın pencereyi öne getirmesi, Alt+B — hepsi çalışıyor.
   **Bulunan bug:** tray→Exit'te ikon kayboluyor ama process yaşıyordu → kök neden `App.OnExit`'in UI thread'ini
   `EngineHost.DisposeAsync` üzerinde bloklaması + dispose yolunda `ConfigureAwait(false)` olmaması =
   **sync-over-async deadlock**; deadlock `_outerJob.Dispose()`'dan önce olduğu için supervisor da ölmüyordu
   (§3/D8 ihlali). It-0/It-2'den **latent**; T62 görünür kıldı. TDD ile düzeltildi (`a7ac3ca`).
   **Kullanıcı yeniden doğruladı:** Exit artık process'i sonlandırıyor ve uygulama sonrasında tekrar açılabiliyor
   (ikincisi `SingleInstanceGuard.Dispose()`'un mutex'i temiz bıraktığının kanıtı — M-6 senaryosu oluşmuyor).
2. **Merge + push YAPILDI:** `main` @ `d1c1912` (`--no-ff`), `it4a-ui-infra` silindi, origin'e push edildi.
   Merge edilen kod, doğrulanmış `a7ac3ca` ağacıyla **birebir aynı** (aradaki tek fark `.claude/` dokümanları).

## It-4b'ye devredilenler (final review triyajı — hiçbiri merge'ü bloke etmiyor)
Gerçek 2×2 layout (T35), tam token çevirisi (T49) + MainWindow kökündeki eski hardcoded hex, DS kontrol kütüphanesi
(T60) + çizilmiş caption/ikon geometrisi ve ikon stratejisi birleştirmesi, tooltip (T61), ikon/ICO hattı (T64),
Settings (T66), OS eylemleri (T67), klavye/focus (T68), kart/çip render, ETA live-tick; ayrıca MUST-DO-FIRST
Core/motor kalemleri (depIssue-persist stale-skip, pre-skip deterministik test, LayerEngine inert, worktree e2e,
Sync IPC/UI) ve şu Minor'lar: AppFonts tek tanım yeri değil, second-instance aktivasyon hatası sessiz, frontier
koşulu duplikasyonu, çift motion enjeksiyonu, user32 P/Invoke bölünmesi, konsol deferred doc-set flicker'ı.
