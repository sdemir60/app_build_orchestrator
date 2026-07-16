# Plan v7 — Aşama Aşama Uygulama Rehberi (Kopyala-Yapıştır Promptlar + Model & Effort Seçimi)

> **Nasıl kullanılır:** Her aşama için → ① Claude Code'da modeli VE effort'u seç (`/model claude-fable-5` veya `/model claude-opus-4-8`; effort, model menüsündeki reasoning-effort seçeneğinden — aşağıdaki tabloya göre) → ② aşamanın PROMPT kutusunu olduğu gibi yapıştır → ③ "Bitti kriteri"ni kontrol et → ④ sonraki aşamaya geç. Her aşamayı **temiz (yeni) oturumda** başlatman önerilir — promptlar kendi bağlamını dosyalardan kuruyor, önceki sohbete ihtiyaç yok.
>
> **Kaynak dosyalar (promptların referans verdiği):**
> - Plan v7: `.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md`
> - Tasarım paketi: `.claude/outputs/2026-07-15-19-00-design-v1/README.md` (+ `prototype/`)
> - Fizibilite raporu: `.claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md`

---

## Model + Effort dağılımı — tek bakış

| Aşama | İçerik | Model | Effort | Neden |
|---|---|---|---|---|
| A1 | Spike (It -1, GATE) | **Fable** | **high** | Job Object/P-Invoke edge case'leri; yanlış sonuç tüm planı etkiler |
| A2 | Gate kararı + It-0 TDD planı | **Fable** | **high** | Plan yazımı + gate yorumu — hata maliyeti yüksek |
| A3 | It-0 uygulama (iskelet) | **Fable** | **high** | Process topolojisi, cascade-kill, IPC — mimarinin temeli |
| A4 | It-1 uygulama (Sync/graph) | **Opus** | **medium** | İyi spec'lenmiş Core işi; acceptance net |
| A5 | It-2 uygulama (Rebuild) | **Opus** | **medium** | İyi spec'lenmiş; Stop/copy-aware kısmında review şart |
| A6 | It-3 uygulama (Incremental) | **Opus** | **medium** | İyi spec'lenmiş; test listesi hazır |
| A7 | It-4 BAŞI: T65 font A/B testi | **Fable** | **medium** | Karar kapısı (K9) — küçük harness, derin akıl yürütme gerektirmez |
| A8 | It-4a: zor-custom UI paketi | **Fable** | **high** | AvalonEdit, sticky overlay, TrackedTextBlock, graf render, WindowChrome — A13'ün riskli parçaları |
| A9 | It-4b: kalan UI görevleri | **Opus** | **medium** | Template/stil hacim işi; değerler design-v1'de hazır |
| A10 | It-5: perf + dağıtım + docs | **Opus** | **medium** | Rutin; perf sorunu çıkarsa Fable high'a dön |
| R | Her iterasyon SONU review | **Fable** | **high** | Kod review'da en güçlü model; ucuz aşamaların sigortası (`/code-review high` argümanı promptta zaten var) |

> **Kural 1 (model):** Opus'lu bir aşamada model tıkanırsa (aynı hatada 2-3 tur dönüyorsa) o task'ı Fable ile temiz oturumda yaptır, sonra Opus'a dön. Ters yönde de serbestsin — bütçe önceliğin varsa A4-A6'yı da Fable yerine Opus review'suz GEÇME, review'u atlama.
> **Kural 2 (effort):** Tıkanmada İLK çare model değiştirmek değil, aynı modelde effort'u bir kademe yükseltmek (medium → high). `low` hiçbir aşamada kullanılmaz — bu projede en ucuz iş bile davranış spec'ine birebir sadakat istiyor. Effort'u düşürmek yalnız mekanik tekrar işlerinde (örn. A9'da ikon/stil kopyalama alt-taskları) kabul edilebilir, onda da medium tabandır.

---

## A1 — Feasibility Spike (T23, GATE) · Model: **Fable** · Effort: **high**

**Ön koşul:** `D:\Projects\Delta\OSYS` erişilebilir; makinede VS/Build Tools kurulu (vswhere bulacak).

**PROMPT — yapıştır:**

```
.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md dosyasını oku (Plan v7 — güncel uygulama kaynağımız).

Görev: PART D'deki Iteration -1 Feasibility Spike'ı (T23) uygula. Kurallar:
- Bu THROWAWAY/investigation kodudur; ana solution'a SIZMAZ. Tüm spike kodunu .claude/temp/spike/ altında ayrı tut.
- S1–S5 adımlarını sırasıyla, gerçek OSYS reposu (D:\Projects\Delta\OSYS) üzerinde çalıştır.
- Ölçümler deterministik olsun (sleep-say yok; handle/wait sinyali).
- Sonucu .claude/outputs/ altına SPIKE-RESULTS dosyası olarak yaz (dosya adı formatı: YYYY-MM-DD-HH-mm-spike-results.md, gerçek zaman Bash date ile): S1–S5 her biri için PASS/FAIL/PARTIAL + ölçülen sayılar + S6 verdict.
- Part D'deki gate kuralları geçerli: S2/S4 FAIL → dur ve bana bildir; S3 PARTIAL → not düş, devam edilebilir.

Bittiğinde bana özetle: hangi gate'ler geçti, sayılar ne, It-0'a başlayabilir miyiz?
```

**Bitti kriteri:** `SPIKE-RESULTS` dosyası var; S1–S5 net PASS/FAIL/PARTIAL; S2/S3/S4 karara bağlanmış. **S2 veya S4 FAIL ise devam ETME** — bana (Fable) sonucu getir, planı revize ederiz.

---

## A2 — Gate kararı + It-0 detaylı TDD planı · Model: **Fable** · Effort: **high**

**PROMPT — yapıştır** (`<spike-results>` yerine A1'in ürettiği dosya adını koy):

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7)
2. .claude/outputs/<spike-results>.md (spike sonuçları)

Görev:
1. Spike sonuçlarını v7 Part D gate kurallarına göre değerlendir: It-0'a geçiş onayı var mı? S3 PARTIAL ise It-1'e HintPath fallback task'ı öner ve plana not düş.
2. Onay varsa: superpowers:writing-plans skill'ini kullanarak IT-0 İÇİN DETAYLI TDD UYGULAMA PLANI yaz. Kapsam = v7 Part C It-0 satırındaki tasklar (T22 resolve, T30, T7, T28 base, T6, T31, T4 base, T56 CompositeFont spike, T62 WindowChrome temel + maximize düzeltmesi, T64 font gömme + glif testi). Her task: adımlar, önce test, kabul kriteri. v7 Global Constraints ve A13 kuralları bağlayıcı.
3. Planı .claude/outputs/ altına yaz (YYYY-MM-DD-HH-mm-it0-tdd-plani.md, gerçek zaman ile).

D9 flag notunu (S5) plana kayıt olarak işle. Bittiğinde planın özetini ver.
```

**Bitti kriteri:** `it0-tdd-plani` dosyası var; her task için test-önce adımlar + kabul kriterleri yazılı.

---

## A3 — It-0 uygulama (iki process + IPC + Job cascade + minimal pencere) · Model: **Fable** · Effort: **high**

**PROMPT — yapıştır** (`<it0-plani>` yerine A2'nin dosya adını koy):

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7 — Global Constraints + A2/A3 + A13 bağlayıcı)
2. .claude/outputs/<it0-plani>.md (It-0 detay planı — birincil yürütme kaynağın)

Görev: superpowers:subagent-driven-development skill'i ile It-0 planını task-by-task uygula. Kurallar:
- Solution: BuildOrchestrator.slnx (kökte), proje yerleşimi v7 A2 tablosuna göre (src/App, src/Core, src/Supervisor, src/Contracts, tests/Tests).
- TDD: her task önce test (superpowers:test-driven-development).
- v7 A3 kabulü ZORUNLU: X→tray'de build devam; Exit/kill/crash → ≤2sn artık process yok; testler deterministik.
- A13.2 kuralları: WindowChrome + maximize Padding düzeltmesi; AllowsTransparency ASLA; fontlar statik OTF (vercel/geist-font), variable font YASAK; CompositeFont line-height spike sonucunu kaydet.
- stdout YALNIZ NDJSON; tüm log stderr/dosyaya.
- Commit'leri ben istemeden yapma; task biterken bana "commit'e hazır" de.

Her task bitiminde kısa durum ver. Tümü bitince: dotnet build + dotnet test çıktılarını göster, It-0 acceptance'ının her maddesini kanıtla.
```

**Bitti kriteri:** `dotnet build` + `dotnet test` yeşil; It-0 acceptance maddeleri kanıtlı (özellikle ≤2sn cascade-kill testi). Ardından **R (review) promptunu** çalıştır.

---

## A4 — It-1 uygulama (Sync/graph) · Model: **Opus** · Effort: **medium**

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7)
2. .claude/handoffs/ altındaki EN YENİ handoff (önceki aşamanın durumu)

Görev: v7 Part C It-1'i uygula: T24 (HintPath→producer graf + batch eval + mtime/hash cache), T32 (solution belirsizliği), T26 (BuildPlan Core'da), T53 Core kısmı (pre-run willBuild kümesi: dirty⇒true, güncel⇒false, imza-yok⇒null).

Kurallar:
- Önce superpowers:writing-plans ile bu iterasyonun kısa TDD dökümünü çıkar (.claude/outputs/YYYY-MM-DD-HH-mm-it1-tdd-plani.md), sonra superpowers:subagent-driven-development ile task-by-task uygula.
- TDD zorunlu; v7 A8'deki It-1 unit test kalemleri kapsanacak.
- Graf primer = HintPath-basename→producer, ProjectReference İKİNCİL (D11). file→project = MSBuild-evaluated Compile items, path-prefix DEĞİL.
- Gerçek OSYS (D:\Projects\Delta\OSYS) ile entegrasyon kontrolü: Sync cache-hit hızlı; 191 csproj'da producer match-rate spike'taki eşiği tutuyor mu raporla.
- Commit'leri ben istemeden yapma.

It-1 acceptance'ının her maddesini kanıtla; bitince .claude/summaries/ + .claude/handoffs/ güncelle ("aşamamızı kaydet" kuralı).
```

**Bitti kriteri:** It-1 acceptance kanıtlı (kartlar build-order'da, cycle rozeti verisi, willBuild testli). Ardından **R promptu (Fable)**.

---

## A5 — It-2 uygulama (Rebuild, paralel + Continue + konsol akışı) · Model: **Opus** · Effort: **medium**

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7)
2. .claude/handoffs/ altındaki EN YENİ handoff

Görev: v7 Part C It-2'yi uygula: T22(invoke), T28(stream), T5 (per-run disk log), T4 (copy-aware Stop), T8, T9, T55(Continue kısmı), T56 (AvalonEdit konsol canlı akış + batch flush).

Kurallar:
- Önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-it2-tdd-plani.md), sonra task-by-task uygulama (superpowers:subagent-driven-development).
- Scheduler = ready-set, ileri atlamalı (v7 K2); dispatch deterministik.
- Konsol A13.2'ye uyar: AvalonEdit, IPC background → Channel → ~50ms batch flush → BeginUpdate/tek Insert/EndUpdate; satır başına Dispatcher.Invoke YASAK.
- Stop → kalanlar queued; Continue kalanlardan sürer, elapsed korunur (T55/K karar kaydı).
- kill mid-parallel-build testi: torn DLL yok + leftover process yok (T9).
- Commit'leri ben istemeden yapma.

It-2 acceptance'ını kanıtla (OSYS rebuild paralel green dahil); bitince aşamamızı kaydet.
```

**Bitti kriteri:** OSYS'te gerçek paralel rebuild yeşil; Stop→Continue çalışıyor; karta tıkla→tam log. Ardından **R promptu (Fable)** — bu iterasyonda review'u atlama (Stop/copy-aware riskli bölge).

---

## A6 — It-3 uygulama (Incremental + worktree + fetch + depIssue + Retry) · Model: **Opus** · Effort: **medium**

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7)
2. .claude/handoffs/ altındaki EN YENİ handoff

Görev: v7 Part C It-3'ü uygula: T25, T27, T11, T13, T14, T29 (branch-driven worktree + K3 niyet satırı), T15, T53(UI), T69 (Sync-fetch ref-only + offline degrade — K1), T54 (depIssue motor kısmı), T55 (Retry failed), T70 (ETA + lastDurationMs).

Kurallar:
- Önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-it3-tdd-plani.md), sonra task-by-task uygulama.
- Sync başında git fetch origin <branch> — YALNIZ ref güncelleme; checkout/pull ASLA; ağ yoksa warn + yerel HEAD (K1).
- Branch seçimi = niyet; konsola 'branch target: … — worktree will be used at Build' satırı; git worktree add YALNIZ Build anında (K3).
- depIssue: resolved = succeeded|failed|skipped; hatalı bağımlılık bloklamaz; kök adlar zincirde taşınır; Contracts alanları (ProjectResult.depIssues[], runCompleted.depIssueCount) v7 A9'a birebir.
- ETA formülü v7 A6'ya birebir (EMA 0.75/0.25, +400ms, 5s yuvarlama, almost done).
- Commit'leri ben istemeden yapma.

It-3 acceptance'ını kanıtla (branch-bounce, L1→L3 dirty, worktree matrisi, will-build dot'lar, fetch degrade, depIssue zinciri, Retry kümesi); bitince aşamamızı kaydet.
```

**Bitti kriteri:** It-3 acceptance kanıtlı. Ardından **R promptu (Fable)**.

---

## A7 — It-4 BAŞI: T65 Font A/B karar kapısı · Model: **Fable** · Effort: **medium**

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (v7 — T65 + A13.1 madde 1)
2. .claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md (§3.1 tipografi + §5 yapısal farklar)

Görev (T65, K9 karar kapısı): Küçük bir WPF test penceresi yap — design-v1'deki gerçek metin örnekleri (konsol satırları, 13px liste satırı, 11px caps başlık; Geist + Geist Mono gömülü) 4 kombinasyonda yan yana: TextFormattingMode Display/Ideal × TextRenderingMode ClearType/Grayscale. Aynı metnin tarayıcı (prototip) görünümüyle karşılaştırma talimatı ekle.

Bana ekran görüntüsü alıp karşılaştıracağım net bir yönerge ver. SONUCU BEN KARAR VERECEĞİM: kabul → saf WPF kesinleşir (varsayılan ayar kombinasyonunu koda sabitle); ret → bana dön, WebView2 hibrit planını konuşuruz. Kararımı .claude/outputs/ altına kısa karar notu olarak yaz (YYYY-MM-DD-HH-mm-t65-font-karari.md).
```

**Bitti kriteri:** Sen ekranda karşılaştırdın, karar verdin, karar notu dosyası yazıldı. (Beklenen: kabul — analiz ~%95-98 diyor.)

---

## A8 — It-4a: Zor-custom UI paketi · Model: **Fable** · Effort: **high**

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (v7 — A7 + A13 bağlayıcı)
2. .claude/outputs/2026-07-15-19-00-design-v1/README.md (görsel otorite; gerekli yerlerde prototype/app/BuildApp.jsx ve prototype/_ds token'larına in)
3. .claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md (§3-§5 teknik çözümler)
4. .claude/handoffs/ altındaki EN YENİ handoff

Görev: It-4'ün ZOR-CUSTOM paketini uygula (yalnız bunlar): T56 (AvalonEdit konsol UI'sının kalanı: colorizer + hibrit aktif-satır typewriter + kaskat tempo+fade + chunk loader), T57 (TrackedTextBlock), T58 (sticky overlay + LayoutMetrics; virtualization KAPALI başlar), T59 (ScrollAnimator/BottomAnchor/Follow + latest pill), T62 (pencere kabuğu paketi: Snap Layouts, restore glyph, tray+balloon, single-instance AllowSetForegroundWindow, Alt+B), T63 (graf render: Shapes yolu + kamera + dash-flow tek clock + EdgeStyleResolver; etiketler Ideal).

Kurallar:
- Önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-it4a-tdd-plani.md), sonra task-by-task.
- A13.2 kuralları HARFİYEN (DoDragDrop yasak, dash birimi thickness çarpanı, ContainerVisual.Opacity animate edilemez, koleksiyon reset yasak…).
- Görsel değerler design-v1'den BİREBİR (süreler, easing KeySpline karşılıkları, renk token'ları) — uydurma değer yok.
- Reduced-motion: tüm süre/eğri tek ResourceDictionary'den; SystemParameters.ClientAreaAnimation canlı takip.
- Commit'leri ben istemeden yapma.

Her task sonunda uygulamayı çalıştırıp ilgili davranışı gözle doğrulayabileceğim kısa bir kontrol adımı ver; bitince aşamamızı kaydet.
```

**Bitti kriteri:** Konsol (seçim+renk+typewriter), sticky başlıklar, graf canlı animasyonları ve pencere kabuğu davranışları prototiple yan yana karşılaştırıldığında birebir his veriyor. Ardından **R promptu (Fable)**.

---

## A9 — It-4b: Kalan UI görevleri · Model: **Opus** · Effort: **medium**

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (v7 — A7 + A13; Part C It-4)
2. .claude/outputs/2026-07-15-19-00-design-v1/README.md (görsel otorite; kopya metinleri buradan birebir)
3. .claude/handoffs/ altındaki EN YENİ handoff

Görev: It-4'ün KALAN görevlerini uygula: T34–T43, T45–T48, T10, T12, T16, T50 (graf panelinin kalan davranışları), T49 (token ResourceDictionary), T54(UI: ▲ rozet + dep filtresi), T60 (DS kontrol kütüphanesi), T61 (tooltip altyapısı), T64 (ikon/ICO kalanı), T66 (Settings: LAYERS + REPOSITORY), T67 (OS eylemleri), T68 (klavye/focus + SR), T70 (ETA gösterimi).

Kurallar:
- Önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-it4b-tdd-plani.md), sonra task-by-task (superpowers:subagent-driven-development).
- Görsel/kopya değerleri design-v1'den BİREBİR; README'de olmayan davranışlar için fizibilite raporu Ek A listesi bağlayıcı (Continue/Retry menüleri, Copy log, Ctrl+F, render dilimleri…).
- A13.2: tooltip delay=0 + CustomPopupPlacementCallback; Settings sürükle-sırala Mouse.Capture (DoDragDrop YASAK); Clipboard retry; 120ms geçişler template-lokal brush.
- Kısayol şeması v7 K6'ya birebir (F5 ailesi; çift-Shift/Ctrl+P YOK).
- Commit'leri ben istemeden yapma.

It-4 acceptance'ının tamamını (v7 Part C) madde madde kanıtla; bitince aşamamızı kaydet.
```

**Bitti kriteri:** It-4 acceptance tam; tasarımla yan yana gözle karşılaştırma yapıldı. Ardından **R promptu (Fable)** — UI'da review'u mutlaka çalıştır.

---

## A10 — It-5: Perf + dağıtım + docs · Model: **Opus** · Effort: **medium**

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (v7 — Part C It-5)
2. .claude/handoffs/ altındaki EN YENİ handoff

Görev: It-5'i uygula: T20 (CPU-cap × copy/git/IPC + copy rate floor), T33 (yalnız spike kanıtladıysa), T44 (success flourish — YALNIZ stream done glow), T51+T63 (graf 500–1000 node perf: DrawingVisual katmanları + cull; sentetik büyük graf ile ölç), T49 (token son geçiş), T17 (trust-boundary doc), README, dotnet publish.

Kurallar:
- Perf modları K11'e birebir: sabit 6/4/2 + priority + inner Job CPU cap (∞/%70/%40); cap tavanı ölçümle kanıtla.
- 500–1000 kart + node akıcılık ölçümü (v7 A8 perf kalemleri); takılma varsa profiling sonucunu raporla — çözümü büyükse durup bana bildir (Fable'a taşırız).
- Commit'leri ben istemeden yapma.

It-5 acceptance'ını kanıtla (publish çalışır exe dahil); bitince aşamamızı kaydet.
```

**Bitti kriteri:** It-5 acceptance tam; `dotnet publish` çıktısı çalışıyor. Son **R promptu (Fable)** + istersen `/code-review ultra` ile kapanış denetimi.

---

## R — Her iterasyon SONU: Review promptu · Model: **Fable** · Effort: **high** (yeniden kullanılabilir)

Her A3/A4/A5/A6/A8/A9/A10 sonrası, **Fable (high)** ile:

```
.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md dosyasını oku; sonra bu iterasyonda değişen kodu review et: /code-review high

Review'a ek iki odak:
1. v7 UYUM DENETİMİ: Global Constraints + A13 yasaklarına aykırılık var mı (in-process MSBuild, OutDir okuma, stdout'a NDJSON-dışı, DoDragDop, AllowsTransparency, koleksiyon reset, frozen brush animasyonu…)?
2. TASARIM SADAKATİ (UI iterasyonlarında): design-v1 README değerlerinden sapma var mı (süre/easing/renk/kopya metni)?

Bulguları önem sırasıyla ver; düzeltmeleri onaylarsam uygula.
```

---

## Ç — Oturumlar arası devam (gerekirse)

Bir aşama yarıda kaldıysa, yeni oturumda aynı modelle sadece şunu yaz:

```
kaldığımız yerden devam et
```

(CLAUDE.md'deki handoff mekanizması en son `.claude/handoffs/` girişini okuyup devam eder — bu yüzden her aşama sonundaki "aşamamızı kaydet" adımı atlanmamalı.)

---

## Sık sorulanlar

- **Sıra atlayabilir miyim?** Hayır — A1 (spike) GATE'tir; A2 onsuz başlamaz. A4–A6 sıralıdır (walking-skeleton). A7 (font kapısı) It-4'ün ilk işi olmalı.
- **Hepsini Fable ile yapsam?** Olur, daha güvenli ama daha pahalı/yavaş. Kritik olan minimum şu üçünün Fable olması: **A1, A3, A8** + tüm **R** review'ları.
- **Hepsini Opus ile yapsam?** Önermem — A1/A3/A8'de hata maliyeti yüksek. Ama yaparsan R review'larını kesinlikle Fable ile çalıştır.
- **Commit ne zaman?** Promptlar commit'i sana bırakıyor (CLAUDE.md kuralı). Her aşama sonunda "commit et" demen yeterli.
