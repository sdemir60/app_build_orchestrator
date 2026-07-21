# It-4a Checkpoint — ZOR-CUSTOM UI paketi (Tasks 0–5 BİTTİ, 6–7 kaldı)

> **Duraklama noktası (2026-07-20 18:37):** İterasyon ortasında, TEMİZ sınırda durduruldu — Task 6 (T63 graf)
> daha BAŞLAMADI (dispatch reddedildi), yarım kalan iş YOK. Working tree temiz.

## Branch / commit durumu (KRİTİK — devir riski)
- Branch: **`it4a-ui-infra`** (BASE `3d28f88` = main), HEAD **`e0d7cf1`**, main'in **14 commit** önünde.
- ⚠️ **origin'e PUSH EDİLMEDİ** (yerel-only branch). Main+origin'e merge/push → kullanıcı onayında (it2/it3 deseni).
- ⚠️ **Plan dosyası `.claude/outputs/2026-07-20-11-02-it4a-tdd-plan.md` UNTRACKED** (commit'li değil, yerel-only).
- ⚠️ **SDD ledger `.superpowers/sdd/progress.md` gitignore'lu** (`.superpowers/sdd/.gitignore` = `*`) → yerel-only,
  git'e HİÇ girmiyor. Task raporları/brief'leri/diff'leri de aynı (yerel-only). → **Farklı makinede (ev) bu üç
  öğe YOKTUR; branch push edilse bile untracked/ignored olanlar gelmez.** Bu özet + handoff tracked olduğundan
  (commit+push edilirse) devir bilgisini taşır.

## Plan / otorite
- Plan (untracked): `.claude/outputs/2026-07-20-11-02-it4a-tdd-plan.md` — 8 task (Task 0 wpftmp fix + Foundation +
  T57/T56/T58/T59/T63/T62). Görsel otorite design-v1 `.claude/outputs/2026-07-15-19-00-design-v1/` (README §1–§3 +
  BuildApp.jsx + `_ds` token'lar); teknik A13.2 + feasibility `2026-07-15-23-34-...` §3–§5; davranış v7 plan
  `2026-07-16-08-39-...`. Execution: `superpowers:subagent-driven-development` (task başına implementer → review → fix).

## BİTEN tasklar (hepsi review'dan Approved geçti)
- **Task 0** (`3d28f88..6545442`): wpftmp scanner dışlaması (`_wpftmp.csproj`) + EvaluationCache vanish-tolerance
  (tüm gövde try/catch {FileNotFound,DirNotFound,IOException}, XmlException propagate; `GetOrEvaluate`→nullable,
  ripple BuildPlanBuilder/Supervisor/test null-filter). Discovery 33/33.
- **Task 1** Foundation (`..4df7263`): Motion.xaml (Duration 80/120/180/280 + 3 KeySpline) + Tokens.xaml (45 brush,
  colors.css birebir) App.xaml'e merge; ReducedMotion (IMotionSignal enjekte edilebilir + SystemParametersMotionSignal
  canlı ClientAreaAnimation; MotionSettings.Effective()+Attach()). --it4a-lab harness + SampleGraphData (36-node OSYS,
  build-data.js birebir). AppResourcesMergeTests guard (kanıtlı).
- **Task 2** T57 (`..d112888`): TrackedGlyphs (saf; advance=glyphAdvance·fs + fs·0.07; ToUpperInvariant) +
  TrackedTextBlock (7 DP; Foreground=SetResourceReference Brush.TextFaint; pixelsPerDip=GetDpi gerçek).
- **Task 3** T56 (`..2acf659`, 3a+3b): ConsoleColorizer (view-only, plain-text-copy garantili) + hibrit typewriter
  (Stopwatch, ≤250ms, 7×13 Rectangle cursor, blink@30fps) + narrative/project-log modları + cascade (26ms/3 satır,
  140ms opacity-fade) + chunk loader (PrependPreviousChunk + CompensatedOffset) + copy-log (ClipboardRetry) +
  **reseed-dup fix (PostReseed sentinel, race-safe)** + render slice (son 200). **C-1 Critical (re-review'da bulundu):
  follow-trim `_loadedFrom`'u bayatlatıyordu → chunk-loader deliği/kalıcı veri kaybı → düzeltildi** (`_loadedFrom += K`).
- **Task 4** T58 (`..57606e3`): LayoutMetrics (PURE, WPF-siz; kümülatif 36/24; StickyHeadersAt i×24 ACCUMULATION;
  ScrollTargetForRow) + StickyLayerList (overlay ItemsControl, virtualization OFF/ScrollUnit=Pixel, opak; heights
  `{x:Static}` bağlı + drift guard testi).
- **Task 5** T59 (`..e0d7cf1`): ScrollAnimator (attached DP + DoubleAnimation, wheel-cancel+suppress, reduced→instant)
  + BottomAnchorBehavior (48px/jumping) + FollowScrollController (550ms/54px, SHARED LayoutMetrics) + ⌄ latest pill
  (console+stream; liste=follow-mode). ConsoleView.StickToBottom → BottomAnchor pass-through (3b invaryantları korundu).
  **I-1 fix:** bottom-anchor recompute EvaluateChunkScroll'dan ÖNCE (prepend yank'ı önlenir).

Son tam test durumu: **suite ~640 geçti / 1 pre-existing skip, build 0/0.** (Bilinen pre-existing MSBuild/IOCP
process-control flake'leri izolasyonda yeşil — acceptance'ta izle.)

## KALAN işler (buradan devam)
1. **Task 6 — T63 graf hibrit render (Shapes yolu):** brief HAZIR (`.superpowers/sdd/task-6-brief.md`, yerel).
   EdgeStyleResolver (saf) + GraphLayout + GraphCamera (saf, 0.68–1.08 clamp, y=H×0.3, <8px no-retarget) + GraphView
   (26px 4px-radius KARE düğüm, dash 4,7 @0.9s TEK paylaşımlı clock UIElement Path, seçim %25/%16 sönme, ▲ dep-badge,
   55ms katman stagger, etiketler **TextFormattingMode=Ideal LOKAL** override). Opus'a dispatch edilecekti.
2. **Task 7 — T62 pencere kabuğu:** Snap Layouts (WM_NCHITTEST HTMAXBUTTON), restore glyph (K8), tray+ilk-X balloon
   (K5, H.NotifyIcon), single-instance (AllowSetForegroundWindow), Alt+B (RegisterHotKey). Taban var: MaximizeFix+Dwm.
3. **Final whole-branch review** (en capable model) + Minor triyajı.
4. **aşamamızı kaydet** + **merge/push kullanıcı onayında.**

## Taşınan kararlar / sözleşmeler (Task 6/7 için bağlayıcı)
- **MOTION SÖZLEŞMESİ:** code-driven animasyonlar `MotionSettings.Effective(base)`/`AnimationsEnabled`'ı animasyon
  başında TAZE okur (canlı reduced-motion); XAML Storyboard süreleri `{DynamicResource Duration.X}` (StaticResource
  DEĞİL). Kaynak anahtarları: `Brush.*`, `Duration.Instant/Fast/Base/Slow`, `KeySpline.EaseOut/EaseStandard/EaseInOut`.
- **T65 dokunma:** MainWindow kökü `TextFormattingMode=Display` + `TextRenderingMode=Grayscale` SABİT; graf etiketleri
  Ideal = LOKAL override. FontAbWindow (--font-ab) referans kalır.
- **Bu pakette DEĞİL (It-4b):** T35 layout, T50 graf veri-wiring, tam T49, T60/61/64/66/67/68, kart/çip render,
  ETA live-tick, MUST-DO-FIRST Core/motor kalemleri (depIssue-persist, pre-skip test, LayerEngine inert,
  worktree e2e, Sync IPC/UI). Lab harness (It4aLabWindow) referans; gerçek 2×2 layout It-4b'de.
- **Final triyaj Minor'ları** (ledger'da tam liste): Task0 XmlException test yok; Task1 AnimationsEnabled XML-doc;
  Task2 .notdef fallback test yok; Task3b M-1 (~50ms deferred doc-set flicker) + M-5 (buffer 200 vs 240).

## Nasıl devam edilir (aynı makine)
`.superpowers/sdd/progress.md` (ledger) en yeni tamamlanmamış task'tan (Task 6) devam; `it4a-tdd-plan.md` Task 6/7
brief kaynağı; `subagent-driven-development` akışıyla Task 6 → review → fix → Task 7 → final review.
