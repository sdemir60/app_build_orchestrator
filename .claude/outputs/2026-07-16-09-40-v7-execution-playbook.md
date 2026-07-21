# Plan v7 — Aşama Aşama Uygulama Rehberi (Kopyala-Yapıştır Promptlar + Model & Effort Seçimi)

> **Nasıl kullanılır:** Her aşama için → ① Claude Code'da modeli VE effort'u seç (`/model claude-opus-4-8`; effort, model menüsündeki reasoning-effort seçeneğinden — aşağıdaki tabloya göre. **Not: bu plan artık yalnız Opus kullanır — Fable kaldırıldı; tamamlanmış aşamaların ✅ kayıtlarındaki "Fable" ifadeleri o gün ne kullanıldığının tarihsel kaydıdır.**) → ② aşamanın PROMPT kutusunu olduğu gibi yapıştır → ③ "Bitti kriteri"ni kontrol et → ④ sonraki aşamaya geç. Her aşamayı **temiz (yeni) oturumda** başlatman önerilir — promptlar kendi bağlamını dosyalardan kuruyor, önceki sohbete ihtiyaç yok.
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
| A8 | It-4a: zor-custom UI paketi | **Opus** | **xhigh** | AvalonEdit, sticky overlay, TrackedTextBlock, graf render, WindowChrome — A13'ün riskli parçaları; plandaki en zor iş, Fable telafisi → xhigh |
| A9 | It-4b: kalan UI görevleri | **Opus** | **medium** | Template/stil hacim işi; değerler design-v1'de hazır |
| A10 | It-5: perf + dağıtım + docs | **Opus** | **medium** | Rutin; perf sorunu çıkarsa effort'u high/xhigh'a çıkar |
| R | Her iterasyon SONU review | **Opus** | **high** (UI iter. **xhigh**) | Plandaki en güçlü model; her iterasyonun sigortası (`/code-review high` argümanı promptta zaten var); A8/A9 gibi UI iterasyonlarının review'unda xhigh |

> **Kural 1 (model):** Plan artık tek model kullanır: **Opus** (Fable kaldırıldı). Tıkanırsan model değiştirme kolu yok; çare effort'u yükseltmek (Kural 2). Hiçbir aşamada review'u atlama — özellikle riskli bölgelerde (Stop/copy-aware, UI custom render).
> **Kural 2 (effort):** Tıkanmada çare, aynı modelde effort'u bir kademe yükseltmek (medium → high → xhigh; Fable olmadığı için tek yükseltme kolu bu). **Fable'ın atandığı aşamalarda taban high değil xhigh'dır** (A8) — güçlü modelin kaybı effort ile telafi edilir; effort modelin tavanını AŞMAZ, yalnız o tavanı sonuna kadar kullandırır (xhigh Opus, Fable'a yaklaşır ama Fable OLMAZ). `low` hiçbir aşamada kullanılmaz — bu projede en ucuz iş bile davranış spec'ine birebir sadakat istiyor. Effort'u düşürmek yalnız mekanik tekrar işlerinde (örn. A9'da ikon/stil kopyalama alt-taskları) kabul edilebilir, onda da medium tabandır.

---

## A1 — Feasibility Spike (T23, GATE) · Model: **Fable** · Effort: **high**

> **✅ TAMAMLANDI (2026-07-16).** Sonuç: [2026-07-16-10-20-spike-results.md](2026-07-16-10-20-spike-results.md) — S1 PASS · S2 PASS · S3 PARTIAL · S4 PASS · S5 kayıt · **S6: GATE GEÇİLDİ.** A2'ye geç.

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

> **✅ TAMAMLANDI (2026-07-16).** Gate teyidi: S1/S2/S4 PASS · S3 PARTIAL (kabul) → **It-0 ONAYLI.** Çıktılar: (a) It-0 TDD planı [2026-07-16-10-35-it0-tdd-plan.md](2026-07-16-10-35-it0-tdd-plan.md) — 14 task, test-önce döngülü; (b) v7 plana `[SPIKE-AMEND 2026-07-16]` bölümü + **T71** (HintPath 3-sınıf sınıflandırıcı, metrik=matched/(matched+sınıflandırılamayan)) + **T72** (bayat obj tanı/warn) It-1'e eklendi; (c) yer-gerçeği 177 csproj·44 sln·1854 HintPath; (d) D9/S5 kaydı (flag'ler v1'de korunur, T33 koşullu). Bağlayıcı spike girdileri (SolutionDir·BuildProjectReferences=false·nuget'siz restore·obj-izolasyon) It-0 planına test olarak kilitlendi. **A3'e geç.**

**PROMPT — yapıştır** (spike sonuç dosyası işlendi — 2026-07-16 son hali):

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7)
2. .claude/outputs/2026-07-16-10-20-spike-results.md (spike sonuçları — S1 PASS · S2 PASS · S3 PARTIAL · S4 PASS · S5 kayıt; S6 verdict: gate geçildi)

Görev:
1. Spike sonuçlarını v7 Part D gate kurallarına göre değerlendir ve teyit et: It-0'a geçiş onayı var mı? S3 PARTIAL → spike'ın önerdiği 3-sınıf HintPath sınıflandırıcısını (edge / external-3rdparty / external-osys-platform + sınıflandırılamayana warn; metrik = matched / (matched + sınıflandırılamayan)) It-1'e fallback task olarak plana not düş.
2. Onay varsa: superpowers:writing-plans skill'ini kullanarak IT-0 İÇİN DETAYLI TDD UYGULAMA PLANI yaz. Kapsam = v7 Part C It-0 satırındaki tasklar (T22 resolve, T30, T7, T28 base, T6, T31, T4 base, T56 CompositeFont spike, T62 WindowChrome temel + maximize düzeltmesi, T64 font gömme + glif testi). Her task: adımlar, önce test, kabul kriteri. v7 Global Constraints ve A13 kuralları bağlayıcı.
3. Spike'ın mühendislik bulgularını (SPIKE-RESULTS S2/S3 bölümleri) plana BAĞLAYICI girdi olarak işle:
   - packages.config restore per-project çağrıda -p:SolutionDir=<projenin bağlı olduğu sln dizini>\ İSTER (T22 resolve/invoke sözleşmesine yaz; T32 sln eşlemesi girdisi).
   - Per-project shell-out'ta -p:BuildProjectReferences=false ZORUNLU.
   - Bayat obj zehirlenmesi gerçek (silinmiş kardeş csproj'un netstandard artıkları build'i kırıyor; OSYS.Types.NewSales.Print vakası) — obj-izolasyon tasarımına test senaryosu olarak ekle; in-place build'ler için It-1'e "obj tanı/warn" notu düş.
   - nuget.exe PATH'te YOK; restore yolu msbuild -t:restore -p:RestorePackagesConfig=true (orchestrator nuget.exe'ye bağımlı olmamalı).
   - Repo yer-gerçeği güncellendi: 177 csproj · 1854 HintPath · 44 sln (plandaki 191/1927/45 eskidi).
4. Planı .claude/outputs/ altına yaz (YYYY-MM-DD-HH-mm-it0-tdd-plan.md, gerçek zaman Bash date ile).

D9 flag notunu (S5) plana kayıt olarak işle: v1 flag'leri kapalıyken ≈2.9× yavaş (47-50s ↔ 16-21s); kazanç TAMAMEN shared compilation'dan (nodeReuse tek başına ≈0); ama shared compilation açıkken emit job-DIŞI VBCSCompiler'da gerçekleşir → torn-DLL riski → v1'de flag'ler KORUNUR, T33 fast-follow bu sayılarla koşullu. Bittiğinde planın özetini ver.
```

**Bitti kriteri:** `it0-tdd-plan` dosyası var; her task için test-önce adımlar + kabul kriterleri yazılı.

---

## A3 — It-0 uygulama (iki process + IPC + Job cascade + minimal pencere) · Model: **Fable** · Effort: **high**

> **✅ TAMAMLANDI (2026-07-16).** superpowers:subagent-driven-development ile 14 task sırayla, task başına implementer + spec/quality review + fix-loop döngüsüyle uygulandı. **Sonuç:** `dotnet build BuildOrchestrator.slnx` yeşil (0 uyarı, 0 hata); `dotnet test` **47 PASS + 1 SKIP** (skip = CompositeFont spike'ının FAIL dalı, protokol gereği meşru kayıt). It-0 acceptance her maddesi kanıtlı: **§3 cascade-kill** ≤2000ms (per-test 106–303ms; 0 orphan; breakaway `win32=5`), **stdout yalnız NDJSON** (D4, çöp komut sonrası dahil), **CompositeFont** LineSpacing=1.55 **TUTMUYOR** (ölçülen 15.96 DIP @13px vs hedef 20.15, sapma ~%20.8 — konsol DefaultLineHeight ile kalır), **Geist statik OTF** 400/500/600 ayrışması testli (binary-level doğrulandı, variable font yok). Final whole-branch review (Fable, high): **Ready to merge (with fixes)** — kritik yok, davranışsız temizlikler uygulandı. Kayıt: [2026-07-16-15-33-it0-records.md](2026-07-16-15-33-it0-records.md) (§3/D4/T56/T64 kanıtları + **It-1/It-2 giriş backlog'u** = ertelenen sertleştirmeler). **Commit BEKLİYOR** (kullanıcı kararı; hiçbir commit yapılmadı, 14 task working tree'de). **A4'e geç.**
>
> **Manuel (insana kalan) görsel kontroller:** dark chrome görünümü, maximize'da taşma olmaması, Restart butonu akışı, caption buton stilleri — `.superpowers/sdd/task-13-report.md` listeliyor.

**PROMPT — yapıştır** (yapıştırmaya hazır — gerçek dosya adları gömülü):

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7 — Global Constraints + A2/A3 + A13 bağlayıcı; ayrıca SPIKE-AMEND bölümü)
2. .claude/outputs/2026-07-16-10-35-it0-tdd-plan.md (It-0 detay TDD planı, 14 task — BİRİNCİL yürütme kaynağın)

Görev: superpowers:subagent-driven-development skill'i ile It-0 planını Task 1'den Task 14'e kadar sırayla uygula. Kurallar:
- Solution: BuildOrchestrator.slnx (kökte), proje yerleşimi It-0 planının "Dosya Yapısı" bölümüne göre (src/App, src/Core, src/Supervisor, src/Contracts, tests/Tests).
- TDD: her task önce failing test → FAIL doğrula → minimal implementasyon → PASS (superpowers:test-driven-development). Plandaki adımlar/kodlar birebir.
- v7 A3 kabulü ZORUNLU: X→tray'de build devam; Exit/kill/crash → ≤2sn artık process yok; testler deterministik (sleep-poll YASAK — D8).
- BAĞLAYICI spike girdileri (SPIKE-RESULTS S2, plana test olarak kilitli): per-project restore -p:SolutionDir=<sln dizini>\ İSTER; per-project shell-out -p:BuildProjectReferences=false ZORUNLU; nuget.exe'ye bağımlılık YOK (msbuild -t:restore -p:RestorePackagesConfig=true); obj-izolasyon (-p:BaseIntermediateOutputPath) — bayat-obj senaryosu test.
- v1 flag'leri SABİT: -p:UseSharedCompilation=false -nodeReuse:false (D9/S5 kaydı — torn-DLL riski).
- A13.2: WindowChrome + maximize Padding düzeltmesi (dotnet/wpf#3887); AllowsTransparency ASLA; fontlar statik OTF (vercel/geist-font), variable font YASAK; CompositeFont line-height 1.55 spike sonucunu kaydet (tutuyor/tutmuyor + ölçülen değer).
- stdout YALNIZ NDJSON; tüm log stderr/dosyaya (D4).
- Yeni dosya adları İngilizce olsun (proje kuralı); it0-records.md gibi.
- Commit'leri ben istemeden yapma; task biterken bana "commit'e hazır" de.

Her task bitiminde kısa durum ver. Tümü bitince: dotnet build + dotnet test çıktılarını göster, It-0 acceptance'ının her maddesini (§3 cascade ≤2s, NDJSON-only, CompositeFont kaydı, font 400/500/600 ayrışması) kanıtla.
```

**Bitti kriteri:** `dotnet build` + `dotnet test` yeşil; It-0 acceptance maddeleri kanıtlı (özellikle ≤2sn cascade-kill testi). Ardından **R (review) promptunu** çalıştır.

---

## A4 — It-1 uygulama (Sync/graph) · Model: **Opus** · Effort: **medium**

> **✅ TAMAMLANDI (2026-07-16).** superpowers:writing-plans → It-1 TDD planı ([2026-07-16-17-25-it1-tdd-plan.md](2026-07-16-17-25-it1-tdd-plan.md), **17 task**), sonra superpowers:subagent-driven-development ile task-başına implementer + spec/quality review + fix-loop. **Sonuç:** clean build **0 uyarı/0 hata**; `dotnet test` **85 PASS + 1 SKIP** (CompositeFont spike). **It-1 acceptance 8/8 kanıtlı** (kartlar build-order · InCycle+Cycles rozet verisi · willBuild testli · OSYS cache-hit · sınıflandırma metriği · unclassified→warn · stale-obj no-touch · 5 sertleştirme). **Gerçek OSYS (`D:\Projects\Delta\OSYS`):** 177 csproj · Edge=**1060** (spike'la birebir) · ThirdParty=78 · OsysPlatform=716 · Unclassified=0 · **RepoResolveRatio=1.0** · AmbiguousDlls=0 (metrik spike ham verisiyle çapraz doğrulandı, gamed değil). Devir sertleştirmeleri kapandı: **EngineHost** concurrency (4 fix-pass: monotonik generation-scoped exit-gate + atomik `KillCurrent` + graceful shutdown + startup-framing surface), NdjsonWriter base-overload, ProcessRunner bounded kill-path, App copy TFM-agnostik+RemoveDir, CascadeKill `>=5` guard + **T71** (3-sınıf HintPath) + **T72** (bayat-obj no-touch warn). Review'ın yakaladığı **gerçek buglar** düzeltildi: EvaluationCache stale-cache (mtime-only → +file-length fingerprint), CsprojEvaluator recursive `**` glob sessiz-sıfır-dosya, StaleObjDetector `libraries` false-positive (→ yalnız `targets` anahtarları), CS0420 x3. Opus final whole-branch review: **Ready with must-fixes** → tek must-fix (EvaluationCache `UnauthorizedAccessException`) uygulandı. **main'e fast-forward merge edildi** (It-0 zaten main'deydi) → tek trunk; It-1 + tüm yardımcı branch'ler (lokal+remote) temizlendi. **main henüz origin'e push EDİLMEDİ** (origin/main 30 commit geride — kullanıcı kararı). Kayıt: [2026-07-16-21-09-it1-sync-graph-complete.md](2026-07-16-21-09-it1-sync-graph-complete.md) (summaries/ + aynı adlı handoff). Ertelenen Minor'lar It-2/It-3 fix-wave'e bırakıldı. **A5'e geç.**

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7)
2. .claude/outputs/2026-07-16-15-33-it0-records.md (It-0 kabul kayıtları + FINAL REVIEW It-1/It-2 giriş backlog'u — önceki aşamanın çıktısı; It-0 kodu working tree'de HAZIR, henüz commit'siz)
3. .claude/handoffs/ altındaki EN YENİ handoff (varsa; It-0 handoff'u It-0 records'a işaret eder)

Görev: v7 Part C It-1'i uygula: T24 (HintPath→producer graf + batch eval + mtime/hash cache), T32 (solution belirsizliği), T26 (BuildPlan Core'da), T53 Core kısmı (pre-run willBuild kümesi: dirty⇒true, güncel⇒false, imza-yok⇒null). AYRICA It-0 final review'ın It-1'e devrettiği sertleştirmeleri bu iterasyonda kapat (it0-records "It-1 giriş listesi" bölümü): EngineHost kümesi (_generation Interlocked/volatile; ReadLoop swallow-all catch'i loop içine + framing hatasında engine'i öldür/EngineExited — Supervisor exit-2 ile simetri; startup çift-sinyal; StartAsync-timeout child temizliği; graceful ShutdownCommand+generation; App copy-target hardcoded TFM/stale), NdjsonWriter base-type kısıtı (polimorfizm footgun'ı), ProcessRunner kill-path sertleştirme, CascadeKillTests handles.Count>=5 assert. T71 (HintPath 3-sınıf sınıflandırıcı) + T72 (bayat obj tanı/warn) zaten It-1 kapsamında.

Kurallar:
- Önce superpowers:writing-plans ile bu iterasyonun kısa TDD dökümünü çıkar (.claude/outputs/YYYY-MM-DD-HH-mm-it1-tdd-plan.md; yukarıdaki devir sertleştirmelerini de task olarak dahil et), sonra superpowers:subagent-driven-development ile task-by-task uygula.
- TDD zorunlu; v7 A8'deki It-1 unit test kalemleri kapsanacak.
- Graf primer = HintPath-basename→producer, ProjectReference İKİNCİL (D11). file→project = MSBuild-evaluated Compile items, path-prefix DEĞİL.
- Gerçek OSYS (D:\Projects\Delta\OSYS) ile entegrasyon kontrolü: Sync cache-hit hızlı; 177 csproj'da (spike yer-gerçeği) HintPath sınıflandırması spike'ın 3-sınıf modeline uyuyor mu raporla (repo-içi kenar %100 çözülür, sınıflandırılamayan artığa warn).
- Determinizm/D8 korunur (sleep-poll yasak); It-0'ın v7 yasakları (in-process MSBuild yok, OutDir okunmaz, stdout yalnız NDJSON, AllowsTransparency yok) It-1'de de geçerli.
- Commit'leri ben istemeden yapma.

It-1 acceptance'ının her maddesini kanıtla; bitince .claude/summaries/ + .claude/handoffs/ güncelle ("aşamamızı kaydet" kuralı).
```

**Bitti kriteri:** It-1 acceptance kanıtlı (kartlar build-order'da, cycle rozeti verisi, willBuild testli). Ardından **R promptu (Fable)**.

---

## A5 — It-2 uygulama (Rebuild, paralel + Continue + konsol akışı) · Model: **Opus** · Effort: **medium**

> **✅ TAMAMLANDI (2026-07-17).** superpowers:writing-plans → It-2 TDD planı ([2026-07-17-12-39-it2-tdd-plan.md](2026-07-17-12-39-it2-tdd-plan.md), **15 task**), sonra superpowers:subagent-driven-development ile task-başına implementer + spec/quality review + fix/re-review döngüsü. **Sonuç:** clean build **0 uyarı/0 hata**; non-acceptance suite **214 PASS + 1 SKIP** (CompositeFont). **Bloklayıcı giriş kriteri kapatıldı** (Task 1: `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` handle-inheritance izolasyonu — paralel redirected launch'ta kardeş pipe uçlarının çapraz sızması kökten kesildi; izolasyon kapatılınca test 3/3 timeout ile ayırt ediyor). **It-2 acceptance kanıtlı** — gerçek OSYS 177-proje paralel rebuild: **122 succeeded / 23 failed / 32 skipped / 0 queued**, Outcome.Completed, max eşzamanlı 6 (tavan tuttu), 0 copy-contention retry → **orchestrator-kaynaklı 0 hata = YEŞİL** (23 başarısızlık repo-kaynaklı: stale-obj NewSales kökleri + CS0006 cascade + gerçek CS/MC compile; reviewer obj=null'ı bağımsız doğrulayarak standalone MSBuild'in de aynı hataları vereceğini teyit etti). **Stop→Continue** (graceful=proje sınırı copy-aware / hard=anında TerminateJobObject; runStopped/runCompleted her yolda tam bir kez; elapsed korunur) ve **karta tıkla→tam log** (aktif run dizininden chunk + canlı dikiş; ilk satır gerçek MSBuild komutu) çalışıyor. Kayıt: [2026-07-17-21-01-it2-records.md](2026-07-17-21-01-it2-records.md) (acceptance kanıtları + It-3 handoff + manuel checklist) + [summaries/2026-07-17-22-38-it2-build-engine-complete.md](../summaries/2026-07-17-22-38-it2-build-engine-complete.md).
>
> **Review (R promptu, Fable) atlanmadı — işe yaradı.** Final whole-branch review verdikt "With fixes". **Riskli bölge (Stop/copy-aware/scheduler-concurrency — Supervisor+Core) ROCK SOLID: Critical yok** (exactly-once event protokolü, snapshot-after-join, onLine latch retry decorator'ı kapsıyor, handle izolasyonu, worker lost-wakeup-safe). Yakalanan 3 Important App seam'indeydi (per-task review'ların göremediği cross-cutting): cross-run stitch kirlenmesi, PendingLoad hang (Skipped-satır tıklaması), planlamada Stop erişilemezliği — 3 fix wave ile kapatıldı (biri fix wave 1'in kendi getirdiği IsStarting-stuck regresyonu, o da re-review'da yakalandı). Review süreci iki gerçek motor bug'ı da erken yakaladı: Task 5 sonsuz pipe-hang (`PerProjectTimeout` başarı-yolu drain'ini kapsamıyordu; OSYS'nin ~178 copy-event'i tetikleyebilirdi), Task 12 canlı UI'da ölü Stop/Continue butonları (`[NotifyCanExecuteChangedFor]` eksik). **main'e fast-forward merge edildi** (It-0+It-1 zaten main'deydi) → tek trunk, 28 commit; branch temizlendi. **main henüz origin'e push EDİLMEDİ** (kullanıcı kararı). Ertelenen bulgular A6/It-3 girdisi (aşağıda) + `.superpowers/sdd/progress.md` It-3 backlog. **A6'ya geç.**
>
> **Manuel (insana kalan) WPF geçişi** (records §7 checklist): canlı pencerede paralel rebuild, Stop→Continue elapsed korunumu, karta tıkla→log (ilk satır gerçek MSBuild komutu), konsol MSBuild-verbose altında akıcılık, Task 12 CanExecute buton canlılığı.

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7)
2. .claude/outputs/2026-07-16-15-33-it0-records.md ("It-2 GİRİŞ KRİTERİ (bloklayıcı)" + "Kabul edilenler" bölümleri — devir girdileri)
3. .claude/handoffs/ altındaki EN YENİ handoff (2026-07-16-21-09-... → It-1 tamam, main'de)

DURUM: It-0 + It-1 main'de (fast-forward merge, tek trunk). Clean build 0 uyarı/0 hata, 85 test yeşil, working tree temiz. Bu iterasyona main'den başla. (main henüz push edilmedi — origin'e taşımak istersen kullanıcıya sor.)

Görev: v7 Part C It-2'yi uygula: T22(invoke: MSBuild.exe+nuget shell-out), T28(stream), T5 (per-run disk log), T4 (copy-aware Stop), T8, T9, T55(Continue kısmı), T56 (AvalonEdit konsol canlı akış + batch flush).

⛔ BLOKLAYICI GİRİŞ KRİTERİ (it0-records — paralel redirected build BAŞLAMADAN çözülmeli, It-2 planının İLK task'ı):
- JobProcessLauncher: bInheritHandles=true tüm inheritable handle'ları miras verir → paralel redirected launch'ta pipe uçları çapraz sızar (EOF/deadlock). Paralel redirected build başlamadan PROC_THREAD_ATTRIBUTE_HANDLE_LIST ile handle-inheritance izolasyonu ŞART. (Tek child'lı It-0 akışında sorun çıkmadı; It-2'nin paralelliği bunu tetikler.)

Kurallar:
- Önce superpowers:writing-plans ile kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-it2-tdd-plan.md; yukarıdaki bloklayıcı girişi İLK task olarak dahil et), sonra superpowers:subagent-driven-development ile task-by-task.
- Scheduler = ready-set, ileri atlamalı (v7 K2); dispatch deterministik.
- Konsol A13.2'ye uyar: AvalonEdit, IPC background → Channel → ~50ms batch flush → BeginUpdate/tek Insert/EndUpdate; satır başına Dispatcher.Invoke YASAK.
- Stop → kalanlar queued; Continue kalanlardan sürer, elapsed korunur (T55/K karar kaydı).
- kill mid-parallel-build testi: torn DLL yok + leftover process yok (T9).
- Determinizm/D8 (sleep-poll YASAK); v7 yasakları (in-process MSBuild yok, OutDir okunmaz, stdout yalnız NDJSON) It-2'de de geçerli. v1 flag'leri SABİT (-p:UseSharedCompilation=false -nodeReuse:false — torn-DLL, D9/S5); per-project restore -p:SolutionDir + -p:BuildProjectReferences=false + obj-izolasyon (SPIKE S2, It-1 T32/graf'ından besle).
- It-1'den ertelenen It-2-ilgili minor'ları fırsat oldukça kapat (ör. TopoSort diamond/multi-SCC testleri) — kritik değil.
- Commit'leri ben istemeden yapma (It-1 deseni: feature branch + task-başı WIP commit; main'e merge / push benim onayımla).

It-2 acceptance'ını kanıtla (OSYS rebuild paralel green dahil); bitince aşamamızı kaydet.
```

**Bitti kriteri:** OSYS'te gerçek paralel rebuild yeşil; Stop→Continue çalışıyor; karta tıkla→tam log. Ardından **R promptu (Fable)** — bu iterasyonda review'u atlama (Stop/copy-aware riskli bölge).

---

## A6 — It-3 uygulama (Incremental + worktree + fetch + depIssue + Retry) · Model: **Opus** · Effort: **medium**

> **✅ TAMAMLANDI (2026-07-18).** Kısa TDD dökümü ([2026-07-18-00-29-it3-tdd-plan.md](2026-07-18-00-29-it3-tdd-plan.md), **19 task** = 12 It-3 task + It-2 devir girdileri), sonra superpowers:subagent-driven-development ile task-başına implementer + spec/quality review + fix/re-review döngüsü. **Sonuç:** clean build **0 uyarı/0 hata**; non-acceptance suite **473 PASS + 1 SKIP** (CompositeFont); gerçek OSYS **incremental acceptance 3/3 GREEN**. **Incremental UÇTAN UCA çalışıyor:** Run1 Build 177 → 122 succ/23 fail/32 cycle-skip (19s); **Run2 (kaynak değişmeden) → 122 "up to date" skipped, 0 önceden-başarılı kaçak** (5.7s); minimal-rebuild: 1 dirty → hedef + 3 direct dependent, 100 alakasız skip; **ordering assert 145 ProjectStarted / 0 ihlal**. **K1 doğrulandı:** OSYS aktif branch (HEAD `6b4ecba…`) koşu öncesi/sonrası DEĞİŞMEDİ; tüm git production yolu salt-okur (checkout/switch/pull/reset YOK). **Kullanıcı kararı:** imza commit-terimi **per-project committed fingerprint**'e (ls-tree blob-hash) rafine edildi (global HEAD yerine — A6 "projeyi etkiliyor"); branch-bounce minimal/doğru. **Cross-cutting bug** T19'da bulundu+düzeltildi: ProcessRunner child stdin'i kapatmıyordu → git.exe Supervisor NDJSON pipe'ını miras alıp ~30s asılıyordu (MSBuild JobProcessLauncher kullandığı için regresyon yok). **Tüm It-2 devir girdileri kapatıldı** (mojibake→pure UTF-8, EngineExited→VM, depIssue ▲ etiketleme, obj-izolasyon worktree seam, ordering assert, sync-I/O kilit dışı, ucuz sertleştirmeler). **Fable whole-branch review → MERGE READY** (tek doc-blocker düzeltildi). **main'e `--no-ff` merge (commit `b82f739`) + origin'e PUSH EDİLDİ** (`37e97d4..b82f739`); `it3-incremental` branch'i de origin'de. Kayıt: [2026-07-18-12-37-it3-records.md](2026-07-18-12-37-it3-records.md) (canlı sayılar + It-4 backlog) + [summaries/2026-07-18-13-02-it3-incremental-complete.md](../summaries/2026-07-18-13-02-it3-incremental-complete.md). **It-4 MUST-DO-FIRST** (final review + progress.md): SCC-aware propagation (cycle-tangled stale-skip), depIssue-persist penceresi (depIssues doluyken persist etme), Build pre-skip için deterministik unit-test, LayerEngine warn'larını layer-UI'dan ÖNCE kapat, worktree e2e wiring. **A7'ye geç.**
>
> **Manuel (insana kalan) WPF/canlı kontroller** (records + It-2 §7 checklist devam): canlı pencerede incremental Build → temiz projeler "up to date" atlanır, Sync (fetch) satırları, branch-niyet satırı, will-build dot'ları (pixel It-4).
>
> **⚠️ Not — DURUM (aşağıdaki kutu tarihseldir):** It-2 sonu itibarıyla yazılmıştı; It-3 başlangıç girdilerini gösterir. Güncel durum yukarıdaki ✅ bloğudur.
> It-0 + It-1 + **It-2 main'de** (fast-forward merge, tek trunk; 28 commit). Clean build 0/0, non-acceptance suite 214 PASS + 1 SKIP, OSYS 177-proje acceptance GREEN. It-2 motoru (paralel MSBuild.exe shell-out, ReadySetScheduler, RunCoordinator, RunLogWriter, RunClock/RunSnapshot, RetryingMsBuildInvoker, RunViewModel/ConsoleBatcher) hazır ve review'lu — It-3 bunların ÜSTÜNE incremental/worktree/fetch/depIssue/Retry/ETA ekler; ağır motor tekrar yazılmaz.

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (Plan v7)
2. .claude/outputs/2026-07-17-21-01-it2-records.md (It-2 acceptance kayıtları + It-3 handoff backlog + manuel checklist — devir girdileri)
3. .claude/handoffs/ altındaki EN YENİ handoff (2026-07-17-22-38-... → It-2 tamam, main'de)
4. .superpowers/sdd/progress.md "It-3 backlog" + "Minor findings roll-up (It-2)" bölümleri (It-2 review'larının bıraktığı bağlayıcı devir kalemleri)

DURUM: It-0+It-1+It-2 main'de (tek trunk, clean build 0/0, 214 test yeşil, acceptance GREEN). Bu iterasyona main'den başla. (main origin'e push edilmedi — taşımak istersen kullanıcıya sor.)

Görev: v7 Part C It-3'ü uygula: T25, T27, T11, T13, T14, T29 (branch-driven worktree + K3 niyet satırı), T15, T53(UI), T69 (Sync-fetch ref-only + offline degrade — K1), T54 (depIssue motor kısmı), T55 (Retry failed), T70 (ETA + lastDurationMs).

⚠️ It-2'den DEVREDEN BAĞLAYICI GİRDİLER (bu iterasyonda kapatılacak — it2-records + progress.md It-3 backlog'undan):
- depIssue (T54) ACCEPTANCE'TA GÖRÜLDÜ: OSYS koşusunda kök proje (stale-obj) başarısız olunca dependent'ları CS0006 "metadata dosyası bulunamadı" ile başarısız oldu (resolved=succ|fail|skip olduğu için dispatch edildiler, upstream DLL üretilmemişti). T54 bu zinciri ▲ dependency-affected olarak ETİKETLEMELİ (ham failure değil); kök adlar zincirde taşınmalı. AYRICA: hard-stop mid-copy bir DLL'i yarım bırakabilir; reason=stopped ile Failed'a düşen proje Continue'da yeniden derlenmiyor → dependent'lar olası torn DLL'e referansla derleniyor. Continue'da reason=stopped Failed'ları Queued'a geri çevirmeyi değerlendir.
- MsBuildOutputEncoding mojibake (Task 5 defect, log-okunabilirliği): VS18/Roslyn redirected pipe'a UTF-8 yazıyor, kod ANSI CP1254 sanıyor (basladi→baÅŸladi). Proje loglarındaki Türkçe MSBuild çıktısı bozuk. UTF-8 decode et (ya da tespit et). Build'i kırmıyor ama It-3'te düzelt.
- EngineExited RunViewModel'e BAĞLI DEĞİL: engine başarılı startRun sonrası ama runStarted öncesi (ya da run ortasında) ölürse IsStarting/IsRunning sıfırlanmıyor, "Restart Engine" bile temizlemiyor → butonlar app-restart'a kadar kilitli. EngineHost.EngineExited → run-state sıfırlayan bir VM handler'ı bağla.
- TryGetProjectLogSnapshot senkron dosya I/O'sunu RunCoordinator._gate ALTINDA yapıyor (latency, correctness değil): büyük log okuması sırasında stopRun/startRun bloklanabilir. Okumayı kilit dışına taşı.
- Acceptance testi resolved-gate'i (dependent yalnız dep'i terminal olduktan SONRA dispatch) bağımsız pinlemiyor, RunCoordinatorTests'e güveniyor. It-3'te ProjectStarted-after-dependencies-terminal ordering assert'i ekle.
- Ucuz sertleştirmeler (progress.md minor roll-up): ReadySetScheduler IsDone "queued empty"→"nothing ready" (self-loop güvenliği); resume-ctor dedup (fresh ctor'un kopyası); SupervisorHost dead _stopRequested temizliği; NativeMethods dead STARTUPINFOW overload; RunLogWriter XML-doc CS1574/CS1570.

Kurallar:
- Önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-it3-tdd-plan.md; yukarıdaki devir girdilerini de task olarak dahil et), sonra superpowers:subagent-driven-development ile task-by-task.
- Sync başında git fetch origin <branch> — YALNIZ ref güncelleme; checkout/pull ASLA; ağ yoksa warn + yerel HEAD (K1).
- Branch seçimi = niyet; konsola 'branch target: … — worktree will be used at Build' satırı; git worktree add YALNIZ Build anında (K3).
- It-2 kararları KORUNUR: I2-K1 iki-katmanlı Stop, ready-set ileri-atlamalı deterministik scheduler, per-run disk log, stdout yalnız NDJSON, D8 (sleep-poll yasak), v1 flag'leri sabit. It-2'de in-place obj kullanılıyordu (I2-K2); It-3 worktree build'lerinde obj-izolasyonu (BaseIntermediateOutputPath, proje Id anahtarlı) DEVREYE GİRER — plumbing (MsBuildArguments/MsBuildInvokeRequest) It-2'de hazır, worktree yolunda null yerine izole path geç.
- depIssue: resolved = succeeded|failed|skipped; hatalı bağımlılık bloklamaz; kök adlar zincirde taşınır; Contracts alanları (ProjectResult.depIssues[], runCompleted.depIssueCount) v7 A9'a birebir.
- ETA formülü v7 A6'ya birebir (EMA 0.75/0.25, +400ms, 5s yuvarlama, almost done); BuildState.lastDurationMs zaten Contracts'ta hazır (It-1'de eklendi).
- Commit'leri ben istemeden yapma (It-2 deseni: feature branch + task-başı WIP commit; main'e merge / push benim onayımla).

It-3 acceptance'ını kanıtla (branch-bounce, L1→L3 dirty, config-switch all-dirty, worktree matrisi + niyet satırı, will-build dot'lar + succeeded→clean canlı geçiş, fetch'li Sync + offline degrade, depIssue zinciri testli, Retry failed kümesi, ETA formülü testli); bitince aşamamızı kaydet.
```

**Bitti kriteri:** It-3 acceptance kanıtlı (branch-bounce, L1→L3 dirty, worktree matrisi, will-build dot'lar, fetch degrade, depIssue zinciri, Retry kümesi) + It-2 devir girdileri (mojibake, EngineExited→VM, depIssue etiketleme, obj-izolasyon worktree yolunda) kapatılmış. Ardından **R promptu (Fable)**.

---

## A7 — It-4 BAŞI: T65 Font A/B karar kapısı · Model: **Fable** · Effort: **medium**

> **✅ TAMAMLANDI (2026-07-19).** T65 spike penceresi App'e eklendi: `BuildOrchestrator.App.exe --font-ab` → `Spikes/FontAbWindow` (DI/Supervisor kurulmadan açılır; design-v1 gerçek örnekleri — 11px caps PanelHead, 13px ProjectRow Medium/SemiBold, 12px mono konsol satırları, renkler token'lardan — 4 kombinasyon yan yana). Tarayıcı referansı **aynı OTF dosyalarıyla** `.claude/temp/2026-07-18-23-45-t65-font-ab-reference.html` (standalone prototip HTML'i kullanılmadı — Google CDN blob'ları kırık). Önkoşul olarak **PerMonitorV2 manifest'i** eklendi (`app.manifest`, A13.2 zorunlu kararı). **KULLANICI KARARI: KABUL — saf WPF kesinleşti; WebView2 hibrit kapısı (K9) KAPANDI** (yalnız v2 backlog'unda). Kullanıcı ne 4 kombinasyonu ne WPF↔tarayıcıyı ayırt edebildi; piksel doğrulaması farkların gerçek ama algı-altı olduğunu kanıtladı (çeyrekler arası ~%15,5-15,8 piksel farkı; renk saçağı ClearType %82 ↔ Grayscale %33-40) → A13.1 madde 1'in ~%95-98 öngörüsü tuttu. Varsayılan **`Display × Grayscale`** MainWindow köküne sabitlendi (gerekçe: Display <14px netlik şartı; Grayscale prototipin `antialiased` görünümüne en yakın, saçaksız, sistem-ClearType/panel-tipinden bağımsız deterministik). Karar notu: [2026-07-19-03-29-t65-font-decision.md](2026-07-19-03-29-t65-font-decision.md). Testler: **475 PASS + 1 SKIP (CompositeFont) + 1 FAIL (T65-DIŞI):** OsysRebuildAcceptance canlı koşusunda WPF markup derlemesinin geçici `*_wpftmp.csproj` dosyası, `EvaluationCache.GetOrEvaluate` FileInfo.Length okuyana kadar silinip `FileNotFoundException` fırlattı (canlı build ↔ scan yarış durumu; Core'a dokunulmadı, T65 kaynaklı değil) → **A8'e devir: scanner `*_wpftmp.csproj` dışlasın + EvaluationCache kaybolan dosyaya toleranslı olsun.** Commit `1d004f0` main'de + origin'de; yardımcı branch'ler temizlendi (lokal `it2-build-engine`/`it3-incremental` + `origin/it3-incremental` silindi — tek trunk `main`). **A8'e geç.**
>
> **⚠️ Not — DURUM (aşağıdaki kutu tarihseldir):** It-3 sonu itibarıyla yazılmıştı; A7 başlangıç girdilerini gösterir. Güncel durum yukarıdaki ✅ bloğudur.
> **DURUM (2026-07-18):** It-0 + It-1 + It-2 + **It-3 main'de VE origin'e PUSH EDİLDİ** (merge commit `b82f739`; `it3-incremental` branch'i de origin'de). Clean build 0/0, non-acceptance suite 473 PASS + 1 SKIP, gerçek OSYS **incremental acceptance GREEN** (Run2 no-change → 122 up-to-date skip / 0 kaçak; K1 salt-okur doğrulandı). Motor tam: incremental (per-project committed-fingerprint imza + build-state persist + Safe/Fast propagation), git subsistem (ref-only fetch + branch-driven worktree + pool), layer/depIssue/Retry/ETA, ve **VM-seviyesi** will-build/depIssue/ETA state (buildPreview). **It-4 = design-v1 birebir UI** (pixel/kart/graf/typewriter render — motor VM state hazır, App yalnız minimal). Temiz oturumda başla. **It-4 MUST-DO-FIRST backlog** (`.superpowers/sdd/progress.md` + it3-records §7): SCC-aware propagation (cycle-tangled stale-skip), depIssue-persist penceresi, Build pre-skip deterministik unit-test, **LayerEngine warn'larını layer-config UI'dan ÖNCE kapat**, worktree e2e BUILD wiring + Continue'nun UseWorktree'yi devralması + inPlace'i resolved-worktree'den türetme, sync-workspace IPC/UI, ETA live-tick. Bunlar A8/A9'un (UI/feature It-4) kapsamı; **A7 yalnız izole T65 font kapısı** — motor/backlog'a dokunmaz.

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (v7 — T65 + A13.1 madde 1)
2. .claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md (§3.1 tipografi + §5 yapısal farklar)
3. .claude/handoffs/ altındaki EN YENİ handoff (2026-07-18-13-02-... → It-3 tamam, main'de + origin'de)

DURUM: It-0..It-3 main'de ve origin'e push edildi (merge b82f739). Geist/Geist Mono statik OTF It-0'da gömüldü (400/500/600 ayrışması testli); CompositeFont line-height 1.55 It-0 spike'ında TUTMADI (ölçülen ~15.96 DIP @13px, konsol DefaultLineHeight ile kalır) — T65 bu bağlamda font rasterization kalitesini (Display/Ideal × ClearType/Grayscale) hedef monitörde karara bağlar.

Görev (T65, K9 karar kapısı): Küçük bir WPF test penceresi yap — design-v1'deki gerçek metin örnekleri (konsol satırları, 13px liste satırı, 11px caps başlık; Geist + Geist Mono gömülü) 4 kombinasyonda yan yana: TextFormattingMode Display/Ideal × TextRenderingMode ClearType/Grayscale. Aynı metnin tarayıcı (prototip) görünümüyle karşılaştırma talimatı ekle.

Bana ekran görüntüsü alıp karşılaştıracağım net bir yönerge ver. SONUCU BEN KARAR VERECEĞİM: kabul → saf WPF kesinleşir (varsayılan ayar kombinasyonunu koda sabitle); ret → bana dön, WebView2 hibrit planını konuşuruz. Kararımı .claude/outputs/ altına kısa karar notu olarak yaz (YYYY-MM-DD-HH-mm-t65-font-decision.md).
```

**Bitti kriteri:** Sen ekranda karşılaştırdın, karar verdin, karar notu dosyası yazıldı. (Beklenen: kabul — analiz ~%95-98 diyor.)

---

## A8 — It-4a: Zor-custom UI paketi · Model: **Opus** · Effort: **xhigh**

> **✅ TAMAMLANDI (2026-07-21).** Kısa TDD dökümü ([2026-07-20-11-02-it4a-tdd-plan.md](2026-07-20-11-02-it4a-tdd-plan.md), **8 task** = İLK task + Foundation + 6 zor-custom), sonra superpowers:subagent-driven-development ile task-başına implementer + spec/quality review + fix/re-review döngüsü. **Sonuç:** clean build **0 uyarı/0 hata**; suite **765 PASS + 1 SKIP** (CompositeFont). **A7'den devreden FAIL kapandı** (Task 0): scanner `*_wpftmp.csproj` dışlar + `EvaluationCache` kaybolan dosyaya toleranslı (canlı OSYS build'inde wpftmp satırı log'da görünüyor, tarama artık çökmüyor). **Foundation:** `Motion.xaml` (80/120/180/280 + 3 KeySpline) + `Tokens.xaml` (45 brush, colors.css birebir) App.xaml'e merge; `MotionSettings`/`ReducedMotion` (canlı `ClientAreaAnimation`); `--it4a-lab` dev harness + `SampleGraphData` (36-node, build-data.js birebir). **T57** TrackedTextBlock (GlyphRun 0.07em + uppercase, gerçek DPI) · **T56** AvalonEdit konsol (colorizer — doküman düz metin kalır, hibrit typewriter ≤250ms kanıtlı sınırlı, kaskat 26ms/3 satır, chunk loader, copy-log, reseed-dup sentinel, render slice) · **T58** LayoutMetrics (saf) + StickyLayerList (birikimli sticky, virtualization KAPALI) · **T59** ScrollAnimator + BottomAnchor(48px) + FollowScroll(550ms/54px) + `⌄ latest` pill · **T63** graf (EdgeStyleResolver/GraphLayout/GraphCamera saf + GraphView Shapes yolu, kamera 460ms, dash-flow, seçim %25/%16, ▲ rozet, 55ms stagger, building pulse) · **T62** pencere kabuğu (Snap Layouts HTMAXBUTTON, restore glyph K8, tray + ilk-X OS balloon K5, single-instance `AllowSetForegroundWindow` sinyalden ÖNCE, Alt+B). **A13.2 HARFİYEN:** dash'in "1.6px'te bölünmesi" ile "TEK paylaşımlı clock" kuralları çelişiyor sanılmıştı — ikisinin birlikte sağlanabildiği kanıtlandı ve uygulandı (tek `ClockGroup` kökü + kalınlık başına child; IEEE-754 tam: `2.5×1.6=4.0`, `4.375×1.6=7.0`, `13.75×1.6=22.0`, `−13.75/6.875=−2.0` → mutlak 4px/7px desen + 22px/0.9s yol iki kalınlıkta da birebir, dikişsiz, faz kilitli). **Review'ın yakaladığı 2 Critical:** (a) T56 follow-trim `_loadedFrom`'u bayatlatıyordu → chunk loader tepeye scroll'da **kalıcı veri kaybı deliği** (repro'lu); (b) T62 single-instance listener'ında back-off'suz `catch` → **%100 CPU sonsuz spin** (makine-global pipe adı vs oturum-yerel mutex). **Final whole-branch review (Opus)** per-task review'ların yapısal olarak göremediği 3 Important buldu, hepsi düzeltildi: (1) motion token'larının **iki otoritesi** vardı — `MotionSettings` sözlüğü kendi tablosundan ezdiği için `Motion.xaml` fiilen ölüydü; (2) **T59'un animasyonlu scroll'u, T58'in overlay'ini kare başına koleksiyon reset'ine sokmuştu** (A13.2 ihlali); (3) Task 0'ın catch'i **her** `IOException`'ı yutuyordu → kilitli bir csproj sessizce plandan düşebilirdi. **Kullanıcının gözle doğrulama pass'i bir bug daha yakaladı:** tray→Exit'te ikon kayboluyor ama process yaşıyordu — kök neden `App.OnExit`'in UI thread'ini `DisposeAsync` üzerinde bloklaması + `EngineHost`'ta `ConfigureAwait(false)` olmaması → **sync-over-async deadlock**; deadlock `_outerJob.Dispose()`'dan önce olduğu için **supervisor da ölmüyordu** (§3/D8 ihlali). It-0/It-2'den gelen **gizli (latent)** defect; T62 onu görünür kıldı. TDD ile düzeltildi (`ConfigureAwait(false)` + `AppShutdown.WaitForAsyncDisposal` → `Task.Run` + sınırlı 2sn; testler pump ETMEYEN STA thread kullanır, pump deadlock'u gizlerdi). **KULLANICI DOĞRULADI (2026-07-21):** tray→Exit artık process'i gerçekten sonlandırıyor VE sonrasında uygulama tekrar açılabiliyor — ikinci kısım ayrıca `SingleInstanceGuard.Dispose()`'un mutex'i temiz bıraktığının kanıtı (bırakmasaydı sonraki açılış bayat guard'ı görüp sessizce kapanırdı = triyajdaki M-6 senaryosu). Kullanıcı ayrıca Snap Layouts, restore glyph, X→tray + ilk balloon, ikinci instance'ın pencereyi öne getirmesi, Alt+B ve konsol renk/seçim/kaskat davranışlarını gözle doğruladı. **main'e `--no-ff` merge edildi (`d1c1912`); `it4a-ui-infra` branch'i silindi; origin'e push edildi.** Kayıt: [summaries/2026-07-20-22-57-it4a-ui-infra-complete.md](../summaries/2026-07-20-22-57-it4a-ui-infra-complete.md) + aynı adlı handoff. **It-4b'ye devir** (final review triyajı, hiçbiri merge'ü bloke etmedi): gerçek 2×2 layout (T35) — It-4a primitifleri şu an `--it4a-lab` harness'ında yaşıyor, konsol+pencere kabuğu gerçek pencerede; `AppFonts` henüz tek tanım yeri değil; ikon stratejisi çelişkisi (CaptionGlyphs karakter ↔ ConsoleHeader Segoe MDL2) → T60/T64'te tekleştir; MainWindow kökündeki eski hardcoded hex → T49; second-instance aktivasyon hatası sessiz; konsol deferred doc-set flicker'ı. **A9'a geç.**

> **DURUM (2026-07-19):** It-0..It-3 + **A7 (T65)** main'de VE origin'de (tek trunk `main`, HEAD `1d004f0`; yardımcı branch'ler temizlendi). **T65 kararı: KABUL — saf WPF; `Display × Grayscale` MainWindow köküne sabit** (karar notu: [2026-07-19-03-29-t65-font-decision.md](2026-07-19-03-29-t65-font-decision.md)); WebView2 kapısı kapandı. Motor tam ve VM-seviyesi state hazır (will-build/depIssue/ETA/buildPreview); App yalnız minimal fonksiyonel iskelet — **It-4a = design-v1 birebir UI'nin zor-custom parçaları.** Clean build 0/0; test **475 PASS + 1 SKIP** + **1 bilinen FAIL** (T65-dışı, aşağıda İLK task). Temiz oturumda başla.

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (v7 — A7 + A13 bağlayıcı)
2. .claude/outputs/2026-07-15-19-00-design-v1/README.md (görsel otorite; gerekli yerlerde prototype/app/BuildApp.jsx ve prototype/_ds token'larına in)
3. .claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md (§3-§5 teknik çözümler)
4. .claude/outputs/2026-07-18-12-37-it3-records.md §7 + .superpowers/sdd/progress.md "It-4 backlog" (MUST-DO-FIRST devir kalemleri)
5. .claude/handoffs/ altındaki EN YENİ handoff (2026-07-18-13-02-... → It-3 tamam; A7/T65 handoff üretmedi, durum bu playbook'un A7 ✅ bloğunda)

DURUM: It-0..It-3 + A7(T65) main'de ve origin'de (HEAD 1d004f0, tek trunk). T65 KABUL → saf WPF; MainWindow kökünde TextFormattingMode=Display + TextRenderingMode=Grayscale SABİT (dokunma). App şu an minimal fonksiyonel iskele; motor VM state (will-build/depIssue/ETA/buildPreview) hazır. Bu iterasyona main'den başla.

⛔ İLK TASK (T65'ten devreden, T65-dışı bilinen FAIL — bu iterasyonun ilk işi):
OsysRebuildAcceptance canlı koşusunda WPF markup derlemesinin ürettiği geçici *_wpftmp.csproj dosyası, EvaluationCache.GetOrEvaluate FileInfo.Length okuyana kadar silinip FileNotFoundException fırlatıyor (canlı build ↔ project scan yarış durumu; src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs:25 + BuildPlanBuilder.cs:26). Fix: (a) project scanner *_wpftmp.csproj (ve benzeri geçici WPF temp proje) dosyalarını DIŞLASIN — bunlar kalıcı proje değil; (b) EvaluationCache kaybolan/erişilemeyen dosyaya toleranslı olsun (FileNotFound → skip/yeniden-değerlendir, throw etme). Önce failing test, sonra fix.

Görev: yukarıdaki İLK task'tan sonra It-4'ün ZOR-CUSTOM paketini uygula (yalnız bunlar): T56 (AvalonEdit konsol UI'sının kalanı: colorizer + hibrit aktif-satır typewriter + kaskat tempo+fade + chunk loader), T57 (TrackedTextBlock), T58 (sticky overlay + LayoutMetrics; virtualization KAPALI başlar), T59 (ScrollAnimator/BottomAnchor/Follow + latest pill), T62 (pencere kabuğu paketi: Snap Layouts, restore glyph, tray+balloon, single-instance AllowSetForegroundWindow, Alt+B), T63 (graf render: Shapes yolu + kamera + dash-flow tek clock + EdgeStyleResolver; etiketler Ideal).

Kurallar:
- Önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-it4a-tdd-plan.md; İLK task'ı ve MUST-DO-FIRST backlog'undan bu pakete düşenleri dahil et), sonra superpowers:subagent-driven-development ile task-by-task.
- A13.2 kuralları HARFİYEN (DoDragDrop yasak, dash birimi thickness çarpanı, ContainerVisual.Opacity animate edilemez, koleksiyon reset yasak…). T65 kararı gereği kökteki Display+Grayscale font ayarına dokunma; graf etiketleri Ideal (A13.2 lokal override).
- Görsel değerler design-v1'den BİREBİR (süreler, easing KeySpline karşılıkları, renk token'ları) — uydurma değer yok.
- Reduced-motion: tüm süre/eğri tek ResourceDictionary'den; SystemParameters.ClientAreaAnimation canlı takip.
- FontAbWindow (--font-ab spike) repoda referans olarak KALIR; It-4a onu silmez/taşımaz.
- Commit'leri ben istemeden yapma.

Her task sonunda uygulamayı çalıştırıp ilgili davranışı gözle doğrulayabileceğim kısa bir kontrol adımı ver; bitince aşamamızı kaydet.
```

**Bitti kriteri:** Konsol (seçim+renk+typewriter), sticky başlıklar, graf canlı animasyonları ve pencere kabuğu davranışları prototiple yan yana karşılaştırıldığında birebir his veriyor. Ardından **R promptu (Opus)**.

---

## A9 — It-4b: Kalan UI görevleri · Model: **Opus** · Effort: **medium**

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (v7 — A7 + A13 BAĞLAYICI; Part C It-4)
2. .claude/outputs/2026-07-15-19-00-design-v1/README.md (görsel otorite; kopya metinleri BİREBİR; gerekli yerlerde prototype/app/BuildApp.jsx + prototype/_ds token'larına in)
3. .claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md (§3-§5 teknik çözümler + Ek A'nın 25 davranışı)
4. .claude/handoffs/ altındaki EN YENİ handoff (2026-07-20-22-57-... → It-4a tamam) + işaret ettiği özet
5. .superpowers/sdd/progress.md "It-4a" bölümü (task ledger + It-4b triyaj listesi — MUST-DO-FIRST kalemleri orada)

DURUM: It-0..It-4a main'de (tek trunk). Build 0/0, suite 765 PASS + 1 SKIP. T65 KABUL → saf WPF; MainWindow kökünde TextFormattingMode=Display + TextRenderingMode=Grayscale SABİT (dokunma). FontAbWindow (--font-ab) referans kalır.

It-4a'nın kurduğu ALTYAPI hazır ve testli — bunları YENİDEN YAZMA, TÜKET:
- Motion/token: Resources/Motion.xaml (Duration.Instant/Fast/Base/Slow = 80/120/180/280 + KeySpline.EaseOut/EaseStandard/EaseInOut) + Resources/Tokens.xaml (Brush.*) + Services/MotionSettings (canlı reduced-motion) + Controls/AppFonts (gömülü Geist Mono).
- Controls/: TrackedTextBlock (caps 0.07em), LayoutMetrics (saf, kümülatif 36/24 + ScrollTargetForRow), StickyLayerList (birikimli sticky, virtualization KAPALI), ScrollAnimator, BottomAnchorBehavior (48px), FollowScrollController (550ms/54px), LatestPill.
- Console/: colorizer + hibrit typewriter + kaskat + chunk loader + copy-log (GERÇEK pencerede bağlı).
- Graph/: EdgeStyleResolver/GraphLayout/GraphCamera (saf) + GraphView (Shapes yolu) — şu an yalnız lab'da.
- Shell/: WindowChrome + maximize fix + DWM + Snap Layouts + restore glyph + tray/balloon + single-instance + Alt+B (GERÇEK pencerede bağlı).
KRİTİK: Liste/graf primitifleri şu an yalnız dev harness'ta (`--it4a-lab`, Spikes/It4aLabWindow). Gerçek 2x2 layout (T35) BU AŞAMANIN işi — primitifleri gerçek pencereye wire et; layout gelince lab harness'ı kaldırılabilir (FontAbWindow KALIR).

MUST-DO-FIRST (devir — bunları sıraya koy):
a) LayerEngine INERT (Program.cs layerPatterns geçmiyor, ters-katman warn'ları yutuluyor) — T66'nın katman-config UI'ı bağlanmadan ÖNCE kapat, yoksa ters-katman config'i topo varsayan tek-geçiş algoritmaları (IncrementalPlanner memo, RetryPlanning) sessizce under-build eder.
b) depIssue-persist stale-skip penceresi (depIssues doluyken BuildState persist etme) + Build pre-skip için deterministik non-acceptance test.
c) Worktree uçtan uca BUILD wiring (RunCoordinator → WorktreeManager.PrepareWorktreeAsync → obj root; in-place için null; Continue orijinal run'ın worktree'sini miras alsın; inPlace RESOLVED köke göre türetilsin).
d) Sync-workspace IPC/UI akışı (SyncWorkspace komutu + Sync/Branch event'leri emit edilmiyor) — Sync butonu, granular adım logu, fetch satırı, curSha→targetSha bunlara bağlı.
e) SCC-aware propagation (cycle-tangled transitive under-build) — It-3'ten açık.
f) It-4a triyajı: AppFonts'u TEK tanım yeri yap (AppFonts.Ui + AppFonts.MonoConsole; TrackedTextBlock/ConsoleView kendi pack URI'sini kurmasın) · ikon stratejisini tekleştir (CaptionGlyphs karakter ↔ ConsoleHeader Segoe MDL2 çelişkisi; caption glyph'leri çizilmiş Geometry'ye → T60) · MainWindow kökündeki hardcoded hex'leri token'a çevir (T49) · ikinci instance aktivasyonu başarısızsa sessiz kalmasın · konsol reseed'inin ~50ms ertelenmiş doküman geçişi (kart tıklamasında kısa içerik flicker'ı) ölç/iyileştir.

Görev: It-4'ün KALAN task'ları: T35 (2x2 layout + görünüm modları quad/list/focus + splitter sınırları + persist), T49 (tam token ResourceDictionary), T50 (graf panelini gerçek veriye bağla + kalan davranışlar), T34 (typing degradation), T36 (reduced-motion tam kapsam), T37 (interaction state'leri + engine-died sticky şerit + Restart engine), T38 (global progress + ETA gösterimi, T70 ile), T39 (failure orchestration: sticky şerit hata kümesi + +N more → Failed filtresi), T40 (canonical click/deselect + Back + aranabilir branch chip + worktree 2-sinyal), T41 (MotionCoordinator, 1 hero), T42 (sync reveal stagger), T43 (config-değişti mini-uyarısı), T45 (anti-slop), T46/T47/T68 (klavye/focus/SR + kontrast), T48 (auto-scroll arbitration), T10 (empty/error state), T12 (mid-run kilit), T16 (autostart), T54-UI (▲ rozet + dep filtresi), T60 (DS kontrol kütüphanesi), T61 (tooltip altyapısı), T64 (ikon/ICO hattı), T66 (Settings: LAYERS + REPOSITORY), T67 (OS eylemleri: explorer /select, vswhere→devenv, OpenFolderDialog, Clipboard retry).

Kurallar:
- Önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-it4b-tdd-plan.md; MUST-DO-FIRST kalemlerini dahil et), sonra superpowers:subagent-driven-development ile task-by-task.
- MOTION SÖZLEŞMESİ (It-4a'dan, bağlayıcı): code-driven animasyonlar MotionSettings.Effective(base)/AnimationsEnabled'ı animasyon BAŞINDA TAZE okur; saf-XAML Storyboard süreleri {DynamicResource Duration.X} kullanır, {StaticResource} ASLA. Renk/süre/eğri anahtarla tüketilir — hardcoded hex/ms YASAK.
- A13.2 HARFİYEN: tooltip delay=0 + CustomPopupPlacementCallback (Top/Bottom ORTALAMAZ); Settings sürükle-sırala Mouse.Capture (DragDrop.DoDragDrop YASAK); Clipboard retry; 120ms geçişler template-lokal brush (frozen/paylaşılan brush anime edilemez); koleksiyon reset YASAK; dash birimi thickness çarpanı; ContainerVisual.Opacity animate edilemez; liste virtualization KAPALI kalır (500+ hedefi T51/It-5).
- Görsel/kopya değerleri design-v1'den BİREBİR; README'de olmayan davranışlar için fizibilite Ek A bağlayıcı (Continue/Retry menüleri, Copy log, Ctrl+F, ETA +400ms, engine 120ms tick kadansı, render dilimleri 200/150, tampon 240/260…).
- Kısayol şeması v7 K6'ya birebir (F5=Build/Continue, Ctrl+F5=Rebuild, Ctrl+F=filtre, Esc zinciri, Alt+B; çift-Shift/Ctrl+P YOK).
- Commit'leri ben istemeden yapma.

Her task sonunda uygulamayı çalıştırıp ilgili davranışı gözle doğrulayabileceğim kısa bir kontrol adımı ver. It-4 acceptance'ının tamamını (v7 Part C It-4) madde madde kanıtla; bitince aşamamızı kaydet.
```

**Bitti kriteri:** It-4 acceptance tam; tasarımla yan yana gözle karşılaştırma yapıldı. Ardından **R promptu (Opus)** — UI'da review'u mutlaka çalıştır.

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
- 500–1000 kart + node akıcılık ölçümü (v7 A8 perf kalemleri); takılma varsa profiling sonucunu raporla — çözümü büyükse durup bana bildir (effort'u xhigh'a çıkarır ya da ayrı oturumda ele alırız).
- Commit'leri ben istemeden yapma.

It-5 acceptance'ını kanıtla (publish çalışır exe dahil); bitince aşamamızı kaydet.
```

**Bitti kriteri:** It-5 acceptance tam; `dotnet publish` çıktısı çalışıyor. Son **R promptu (Opus)** + istersen `/code-review ultra` ile kapanış denetimi.

---

## R — Her iterasyon SONU: Review promptu · Model: **Opus** · Effort: **high** (yeniden kullanılabilir)

Her A8/A9/A10 sonrası (A3-A6 zaten tamamlandı), **Opus (high)** ile:

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
- **Model seçimi?** Plan artık **tamamı Opus** (Fable kaldırıldı). Kalan aşamalar: A8 (Opus/**xhigh** — plandaki en zor iş, Fable telafisi), A9 (Opus/medium), A10 (Opus/medium) ve her iterasyon sonu **R** review'ları (Opus/high; UI iterasyonlarında xhigh).
- **Tıkanırsam?** Model değiştirme kolu yok; effort'u yükselt (medium → high → xhigh, Kural 2). Effort modelin tavanını aşmaz — yalnız o tavanı sonuna kadar kullandırır. Review'u hiçbir koşulda atlama — riskli bölgelerin (UI custom render, Stop/copy-aware) tek sigortası o.
- **Commit ne zaman?** Promptlar commit'i sana bırakıyor (CLAUDE.md kuralı). Her aşama sonunda "commit et" demen yeterli.
