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
| **A11** | CLAUDE.md bayat bilgi denetimi ✅ | **Opus** | **low** | Dört olgusal ifade; karar kullanıcıda, uygulama mekanik |
| **A12** | Bilinen regresyon: kart animasyonu / renklendirme | **Opus** | **high** | Teşhis işi; yeşil suite'in kaçırdığı runtime kusuru (c6e9a21 sınıfı) |
| **A13** | Gözle-kontrol borcunun otomatikleştirilmesi + park listesi triyajı | **Opus** | **high** | 81 görsel kalemin pinlenebilenleri süite; kalanı kısa artık liste |
| **A14** | **Test-düzelt döngüsü** (tekrarlanır) | **Opus** | **high** | Kullanıcının bulguları; her fix'ten önce kırmızı test |
| **A15** | Kapanış belge pası (CLAUDE.md · README · docs/) | **Opus** | **low** | Mekanik denetim; kanıt zaten koddadır |
| R | Her iterasyon SONU review | **Opus** | **high** (UI iter. **xhigh**) | Plandaki en güçlü model; her iterasyonun sigortası (`/code-review high` argümanı promptta zaten var); A8/A9 gibi UI iterasyonlarının review'unda xhigh |

> **DURUM (2026-07-30):** A1-A10 **tamamlandı** (v7'nin planlı kod iterasyonları bitti); **A11 de tamamlandı** (aynı gün, commit `4bb6158`).
> **Kalan 4 adım:** A12 → A13 → A14 (tekrarlanır) → A15. **Kalan bölüm 2026-07-30'da revize edildi** (kullanıcı kararı):
> eski A12 (81 kalemlik kullanıcı gözle-kontrol pası) kaldırıldı; onun yerine regresyon fix'i (A12) ve
> görsel borcun otomatikleştirilmesi (A13) agent'a alındı, kullanıcıya yalnız kısa bir artık liste +
> tekrarlanan test-düzelt döngüsü (A14) kaldı. Detay için "KALAN ADIMLAR" bölümüne bak.

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

> **✅ TAMAMLANDI (2026-07-25) — KOD TARAFI TAM; GÖRSEL DOĞRULAMA KULLANICI KARARIYLA ERTELENDİ.** Kısa TDD dökümü ([2026-07-21-05-46-it4b-tdd-plan.md](2026-07-21-05-46-it4b-tdd-plan.md), **24 task**), sonra superpowers:subagent-driven-development ile **6 oturuma yayılarak** (oturum doldu / makine kapandı — iş kaybı yok, ledger durable) task-by-task. **Yöntem It-4b'de sertleşti:** taze implementer → `scripts/review-package BASE HEAD` → **3-lens paralel review** (spec/design-fidelity · WPF/threading+A13.2 · tests/yapı) + her Critical/Important'a **3-açılı adversarial** (reproduce/code-reading/severity, ≥2 onay = hayatta kalır) + dejenere-lens tespiti → tek fix wave → re-review. **Sonuç:** 24/24 task complete + Approved (**A1-A5 · B1-B4 · C1-C2 · D1-D7 · E1-E6**); clean build **0/0**; suite **1199 PASS + 1 SKIP** (CompositeFont); **acceptance (canlı OSYS) 3/3** (3m10s, K1 read-only testlerce assert edildi). **MUST-DO-FIRST a–f kapandı:** a→A1 (LayerEngine wiring + sıra-bağımsız propagation), b→A2 (depIssue-persist penceresi + pre-skip testi), c→A4 (worktree e2e BUILD + Continue mirası), d→A5 (Sync/branch/topoloji IPC), e→A3 (SCC-aware propagation), f→B1/B2 (AppFonts tekleştirme · tek ikon stratejisi · hardcoded hex→token). Part C It-4'ün TÜM task numaraları sahiplendi (T34-T43, T45-T48, T10, T12, T16, T49, T50, T53-UI, T54-UI, T56-T68, T70; T56-T59/T62/T63/T65 It-4a'dan) — **kapsam dışı T yok.** **main'e `--no-ff` merge (`59de4de`) + origin'e PUSH EDİLDİ;** `it4b-ui` silindi, `main..it4b-ui = 0` (kaçak commit yok), repoda tek dal `main`. Kayıt: [2026-07-25-04-12-it4-records.md](2026-07-25-04-12-it4-records.md) (v7 Part C acceptance matrisi 33 satır + biriken TÜM GÖZLE KONTROL tek listesi §2 + kararlar §3) + [summaries/2026-07-25-05-20-...](../summaries/2026-07-25-05-20-it4b-e6-closed-all-tasks-complete.md) + ledger `.superpowers/sdd/progress.md`.
>
> **⚠️ ÖNEMLİ — E6 sonrası yakalanan launch-fatal:** `c6e9a21` — `ShellRoot.xaml:19` `RowDefinition.Height` (GridLength) bir **Double** token'ı (`Size.ActionBarHeight`) DynamicResource ile alıyordu → üretimde XamlParseException → **uygulama HİÇ açılmıyordu.** 1198 test yeşilken kaçtı; **kullanıcı ilk gerçek launch'ta buldu.** Kök-neden fix + `ShellRootTests` (ShellRoot'u tam realize eder; transitif olarak StickyRibbon/GraphView/DsSplitter/PanelHeader/StickyLayerList/LatestPill/EventStreamView/ActionBar'ı da realize eder). **Ders: headless suite XAML runtime çözümlemesini görmez.**
>
> **AÇIK KALEMLER (bilinçli, kayıtlı):**
> 1. **GÖZLE KONTROL manuel pası YAPILMADI** — acceptance matrisinin 33 satırından **26'sı 👁 VISUAL**, ~81 madde ([it4-records.md §2](2026-07-25-04-12-it4-records.md)); D4 zorunlu. **KULLANICI KARARI (2026-07-25): tüm adımlar bitince topluca yapılacak** ("adımlar çok detaylı, en son bakacağım"). Bitti kriterinin *"tasarımla yan yana gözle karşılaştırma"* yarısı (E6 Step 3) bu pasa dahildir ve o da ertelenmiştir.
> 2. **R promptu (final whole-branch review) ÇALIŞTIRILMADI.** Gerekçe: (a) kaçan bug'ın tam sınıfı hedefli taramayla kapalı doğrulandı — Double-token→GridLength/Thickness **0**, tanımsız DynamicResource key **0** (228 tanımlı/106 kullanılan), MOTION SÖZLEŞMESİ ihlali **0**, Window `AllowsTransparency` **0**; (b) It-4b'nin per-task review'ları (3-lens + adversarial) önceki iterasyonlarda *final* review'un yaptığı işi zaten yapıyor — 4/4 tarihsel isabet tek-geçişli per-task review dönemine ait; (c) kalan risk statik değil **runtime/görsel**, çaresi GÖZLE pası. **Diff büyüklüğü:** 200 dosya, +23.927 satır (src +12.148 · tests +9.543). Karar geri alınabilir — semantik cross-task konuları (VM↔view kablajı, state-machine delikleri) taranmadı.
> 3. **Acceptance #5 (500–1000 kart+node akıcı) KARŞILANMADI** — ölçüldü: 191 satır **~660-730ms** (bütçe 400ms), 500 satır ~1200ms → **T51/It-5'e devir** (plan-öngörülü).
> 4. Kullanıcının bildirdiği "ufak tefek görsel sorunlar" ledger'da kayıtlı, çözülmedi (görsel pasla birlikte ele alınacak).
>
> **It-5'e DEVREDİLENLER (karar kayıtlı):** CurrentSha tam BuiltCommit wire (interim App-only guard yapıldı) · liste virtualization / kart-sadeleştirme · motion seam-helper fold (PopoverBase/MotionGate/PlayRevealStagger) · E4 full-arbiter (istenirse). **Varsayılanla ilerlenen kullanıcı kararları:** B3 C-1/C-2, B2 tray ikonu, A2 depIssue maliyeti, D6 SelectBranch aktif-dönüş reset, E5 kontrast RATIFY (TextDim 4.28 / TextFaint 2.57 — design-v1 birebir, dokümanlı sub-AA istisnası). **A10'a geç.**

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

> **✅ TAMAMLANDI (2026-07-26) — KOD TARAFI TAM; MAIN'E MERGE EDİLDİ. GÖRSEL DOĞRULAMA VE BİLİNEN BİR REGRESYON AÇIK.**
> Kısa TDD dökümü ([2026-07-25-13-40-it5-tdd-plan.md](2026-07-25-13-40-it5-tdd-plan.md), **14 task**), sonra superpowers:subagent-driven-development ile task-by-task; yöntem A9'daki gibi (taze implementer → `review-package` → 3-lens paralel review → tek fix wave → scoped re-review → ledger). **Sonuç:** 14/14 task complete; **`main @ f620e52`**, merge commit `6c173f2` (`--no-ff`), `main == origin/main`, `it5-perf-dist` silindi, tek dal. Build **0/0**, suite **1430 passed / 2 skipped / 0 failed**, canlı OSYS acceptance **3/3**. Final whole-branch review (36 commit / 103 dosya / +10925−931): **Karar 1 `MERGE WITH FIXES`** (0 Critical / 2 Important) → fix `1546783` → **Karar 2 `READY TO MERGE`** (0 Critical / 0 Important).
> Kayıt: [2026-07-26-10-17-it5-records.md](2026-07-26-10-17-it5-records.md) (kabul kaydı + park edilen ~60 kalemin tam tablosu) · [2026-07-26-10-17-visual-check-walkthrough.md](2026-07-26-10-17-visual-check-walkthrough.md) (**gözle kontrol listesi**) · [2026-07-26-07-38-t33-decision.md](2026-07-26-07-38-t33-decision.md) · [summaries/2026-07-26-11-33-...](../summaries/2026-07-26-11-33-it5-complete-merged-to-main.md) · ledger `.superpowers/sdd/progress.md`.
>
> **ÖLÇÜMLER:** liste ilk realize (191 satır) **787,3 → 487,5 ms** (bütçe 400 ms **TUTMADI**, satır başına nesne 55→39) · graf 1000 düğüm **934,8 → 136,0 ms** (ilk görünür alan) / **469,1 ms** (tüm graf gezildiğinde), 500 düğüm 394,1 → 91,5 / 206,2; düğüm başına nesne **17 → 9** · publish uçtan uca doğrulandı (`scripts/verify-publish.ps1`, 16 check + ön koşul, **§3 cascade ölçülmüş kanıt**).
>
> **ÖLÇÜME DAYALI İKİ "YAPMA" KARARI:** (a) **`DrawingVisual` göçü YAPILMADI** — G1'in kırılımı darboğazın çizim değil **nesne kurulumu** olduğunu gösterdi (saf layout aritmetiği toplamın %0,03'ü); (b) **L2 virtualization AÇILMADI** (kullanıcı kararı) — 487 ms kabul edildi, gerekçe sticky/LayoutMetrics/FollowScroll/ScrollArbiter riskinin son iterasyonda alınmaması.
>
> **⚠️ AÇIK KALEM 1 — KULLANICININ BİLDİRDİĞİ REGRESYON (2026-07-26, It-5 sonrası ilk gerçek launch):**
> *"Sol alt köşedeki kartlarda loading ile animasyonlar çalışırdı; bu adımda **hiç hareket etmiyor**, animasyonlar yok, **renklendirmeler vs hiç çalışmıyor** — bozulmuş."* It-4b sonunda çalışıyordu. **Kullanıcı kararı: tüm eksikler en sonda topluca analiz edilip düzeltilecek** (bkz. **A13**). Kod tarafı bu haliyle merge edildi.
>
> **⚠️ AÇIK KALEM 2 — GÖZLE KONTROL PASI HÂLÂ YAPILMADI.** It-4b'den ertelenen ~81 madde + It-5'in kendi görsel kalemleri, tek yürünebilir listede: [visual-check-walkthrough.md](2026-07-26-10-17-visual-check-walkthrough.md) (81/81 kalem panel sırasına göre, **D4 zorunlu**, prototiple yan yana design-v1 §2.1-§2.9). Bkz. **A12**.
>
> **DİĞER AÇIK/PARK KALEMLER (bilinçli, kayıtlı):** W2'de guard'ın 4 + primitifin 3 kopyası katlanmadı · `Show()` başlatma yolu realize kapsamı dışı · `debugSpawnChildren` üretimde dinleniyor · **a11y kümesi** (graf düğümlerinde `AutomationProperties.Name` yok + etiket LOD'un tek yedeği fare-hover tooltip + düğümler klavyeyle gezilemiyor — final review: *It-5'in getirdiği gerileme değil, LOD'un görünür kıldığı ürün-seviyesi boşluk, merge'i bloklamaz*) · ~~CLAUDE.md'nin bayat ifadeleri~~ (**A11'de kapatıldı**, 2026-07-30 · `4bb6158`) · tam liste `it5-records.md` park tablosunda.
>
> **SÜREÇ DERSLERİ (ledger'da):** (1) **Aynı worktree'de iki implementer paralel koşturulmaz** — W2-fix ile D1 paralel koştu, `git add -A` çapraz commit'e yol açtı, ağaç bir süre derlenmez kaldı; read-only reviewer'lar paralel sorunsuz. (2) **Park edilmiş bir kalem, sonradan yazılan dokümantasyon onu iddia haline getirdiğinde yeniden açılmalıdır** — `EffectivePriorityLocked` P3'te Minor diye park edilmişti; TRUST-BOUNDARY + README garantiyi kodun verdiğinden geniş anlatınca final review haklı olarak yeniden açtı. (3) **Guard'ın yeşil olması bir şeye baktığı anlamına gelmez** — T33 "tek kaynak" pini repo kökünü taramadığı için sıfır dosya tarıyordu.

> **REVİZE EDİLDİ (2026-07-25, It-4b kapanışından sonra).** Değişenler: (a) okuma listesi 2→6 dosya + DURUM bloğu; (b) **T44 görev listesinden ÇIKARILDI** — D3'te zaten teslim edildi (`EventStreamView.xaml.cs:291`, glow-once 1.1s, per-instance brush, testli), yeniden yazılmamalı; (c) It-4b'den devredilen 4 kayıtlı kalem eklendi; (d) **ertelenen GÖZLE KONTROL pası son faz olarak eklendi** (It-5 son iterasyon — bu pas burada yapılmazsa kaybolur); (e) commit kuralı CLAUDE.md'nin 2026-07-21 kararına göre düzeltildi ("commit etme" → branch + task-başı commit + merge/push); (f) yöntem (TDD dökümü + subagent-driven-development + per-task 3-lens review) ve realize-test kuralı bağlayıcı yazıldı.

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (v7 — Part C It-5 + K11 + A13 BAĞLAYICI)
2. .claude/outputs/2026-07-15-19-00-design-v1/README.md (görsel otorite — T49 drift kontrolü + perf sonrası görsel eşdeğerlik)
3. .claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md (§3-§5 + Ek A; graf hibrit render kararları)
4. .claude/outputs/2026-07-25-04-12-it4-records.md (It-4 acceptance matrisi §1 · ERTELENEN GÖZLE KONTROL tek listesi §2 · kararlar §3)
5. .claude/handoffs/ altındaki EN YENİ handoff (2026-07-25-13-08-... → It-4b doğrulandı, It-5 hazır)
6. .superpowers/sdd/progress.md en üstteki ">>> RESUME HERE <<<" (durable ledger; git-ignored, yalnız lokal — çelişkide ledger kazanır)

DURUM: It-0..It-4b main'de ve origin'de (HEAD 133e385, tek dal, kaçak commit yok). Build 0/0, suite 1200 PASS + 1 SKIP (CompositeFont), acceptance (canlı OSYS) 3/3. UI tam: 2x2 layout, kart/şerit/stream/konsol/graf/action-bar/Settings, OS eylemleri, motion, scroll arbitration, klavye/a11y. Bu iterasyona main'den başla.

Görev: v7 Part C It-5'i uygula: T20 (CPU-cap × copy/git/IPC etkileşimi + copy fazına rate floor), T33 (KOŞULLU — aşağıya bak), T51+T63-perf (graf 500-1000 node: DrawingVisual katmanları + cull + GlyphRun cache; sentetik büyük grafla ölç), T49 (token SON GEÇİŞ = drift denetimi, yeniden yazma), T17 (trust-boundary doc), README, dotnet publish.

⛔ T44 YAPILMIŞ — TEKRAR YAZMA: success flourish (yalnız stream done satırı glow-once) D3'te teslim edildi
   (EventStreamView.xaml.cs:291 GlowMs=1100, StatusSuccessSoft→şeffaf, per-instance brush, EventStreamTests'te testli).
   Yalnız DOĞRULA: liste/graf dalgası YOK, tek satır, bir kez.

⛔ T33 KOŞULLU — spike zaten karar verdi (D9/S5): v1 flag'leri (-p:UseSharedCompilation=false -nodeReuse:false)
   KORUNUR. Ölçüm: kapalıyken ≈2.9× yavaş (47-50s ↔ 16-21s); kazanç TAMAMEN shared compilation'dan (nodeReuse tek
   başına ≈0); AMA shared compilation açıkken emit job-DIŞI VBCSCompiler'da olur → torn-DLL riski (§3 cascade-kill
   garantisini kırar). T33'ü ancak torn-DLL'i kapatan bir mekanizma kanıtlarsan aç; kanıtlayamıyorsan KAPALI bırak
   ve gerekçeyi kayda geç. Varsayılan = KAPALI.

It-4b'den DEVREDEN KAYITLI KALEMLER (bu iterasyonda kapat — ledger + it4-records §3):
a) Liste virtualization / kart-sadeleştirme — ÖLÇÜLDÜ: 191 satır ~660-730ms MEDIAN (bütçe 400ms), 500 satır ~1200ms.
   Bu, It-4 acceptance maddesi #5'in ("500-1000 kart+node akıcı") AÇIK kalan tek kalemidir; T51'in liste-tarafı ikizi.
   ⚠️ RİSK: virtualization'ı AÇMAK StickyLayerList'in birikimli sticky overlay'i + LayoutMetrics kümülatif offset
   tablosu + FollowScrollController.ScrollTargetForRow + ScrollArbiter ile etkileşir (It-4a/It-4b bunları
   virtualization KAPALI varsayımıyla kurdu). Önce kart-sadeleştirmeyi ölç — bütçeyi tek başına tutturuyorsa
   virtualization'a hiç girme. Girersen sticky/follow/arbiter regresyon testleri ZORUNLU.
b) CurrentSha tam BuiltCommit wire (cross-boundary Contracts + never-built display kararı). It-4b'de yalnız
   App-tarafı interim guard var: ProjectRow.ApplySha cur boşken yalnız target gösterir.
c) Motion seam-helper fold: PopoverBase / MotionGate / PlayRevealStagger tek helper'a (tekrar temizliği).
d) E4 full-arbiter routing (İSTEĞE BAĞLI — kullanıcı "BIRAK" dedi, belgeli spec-surface; yalnız perf işi gerektirirse).

Kurallar:
- Önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-it5-tdd-plan.md, gerçek zaman Bash date ile; yukarıdaki
  devir kalemlerini ve GÖZLE fazını task olarak dahil et), sonra superpowers:subagent-driven-development ile task-by-task.
- PER-TASK METOD (ledger'da bağlayıcı): taze implementer → scripts/review-package BASE HEAD → 3-lens paralel review
  (spec/design-fidelity · WPF/threading+A13.2 · tests/yapı) + her Critical/Important'a 3-açılı adversarial
  (reproduce/code-reading/severity, ≥2 onay = hayatta kalır) → tek fix wave → re-review → ledger.
- Perf modları K11'e birebir: sabit Full(6)/Balanced(4)/Light(2) + process priority + inner Job CPU rate cap
  (∞/%70/%40); konsol notu cap'i de yazar (`parallelism: 4 · cpu cap 70%`). Cap tavanını ÖLÇÜMLE kanıtla.
- REALIZE TESTİ ZORUNLU (It-4b dersi, bağlayıcı): yeni XAML kökü/şablonu ekleyen her task DsResources.Realize ile
  bir realize testi de ekler. Gerekçe: c6e9a21 — ShellRoot'ta Double token GridLength'e veriliyordu, 1198 test
  yeşilken uygulama HİÇ açılmıyordu; headless suite XAML runtime çözümlemesini görmez. Kullanıcının görsel pası
  en sona ertelendiği için bu testler tek güvenlik ağıdır.
- MOTION SÖZLEŞMESİ + A13.2 aynen geçerli: code-driven animasyonlar MotionSettings.Effective/AnimationsEnabled'ı
  animasyon BAŞINDA taze okur; saf-XAML Storyboard süreleri {DynamicResource Duration.X} (StaticResource ASLA);
  koleksiyon reset YASAK; frozen/paylaşılan brush anime edilemez; ContainerVisual.Opacity animate edilemez;
  hardcoded hex/ms YASAK; Window'da AllowsTransparency ASLA.
- v7 yasakları değişmedi: in-process MSBuild yok, OutDir okunmaz, stdout yalnız NDJSON, D8 (sleep-poll yasak), K1
  (git salt-okur — checkout/pull/reset ASLA).
- Git (CLAUDE.md 2026-07-21 kararı): it5 çalışma branch'i aç, task başına commit at, iş bitince main'e merge +
  push, merge'ü DOĞRULADIKTAN sonra branch'i local+remote sil, oturumu main'de bitir.
- Takılma/perf sorunu çözümü büyükse durup bana bildir (effort'u xhigh'a çıkarırız ya da ayrı oturuma alırız).

SON FAZ — ERTELENEN GÖZLE KONTROL PASI (atlanamaz; It-5 SON iterasyon):
Kullanıcı It-4'ün tüm görsel doğrulama borcunu bilinçli olarak buraya erteledi. Kod task'ları bittikten SONRA:
1. it4-records.md §2'deki tek listeyi (18 task ~81 madde, B1→E5) uygulama açıkken tek tek yürütülebilir hale getir;
   maddeleri panelden panele sıralı, kullanıcının tek oturumda yürüyebileceği bir kontrol listesi olarak sun.
   D4 (konsol gerçek akışı) ZORUNLU — headless imkansız.
2. E6 Step 3: prototype/Build Orchestrator (standalone).html tarayıcıda ↔ uygulama yan yana, README §2.1-§2.9 tek tek.
   Sapmalar ya düzeltilir ya A13.1 "algısal eşdeğer" sınıfına GEREKÇESİYLE yazılır.
3. It-5'in kendi görsel kalemlerini (perf modu chip'i + cpu cap notu, graf 500+ akıcılık, publish edilmiş exe'nin
   ilk açılışı) bu listeye ekle.
Kullanıcı listeyi yürüyüp bulguları bildirecek; çıkan sapmalar için fix wave aç.

It-5 acceptance'ının her maddesini kanıtla (CPU cap tavanı tutar · copy fazı starve olmaz · graf 500-1000 akıcı +
cull · publish çalışır exe · flourish yalnız stream glow · README + trust-boundary) VE It-4 acceptance #5'in
kapandığını göster. Bitince aşamamızı kaydet.
```

**Bitti kriteri:** It-5 acceptance tam; `dotnet publish` çıktısı çalışıyor. Son **R promptu (Opus)** + istersen `/code-review ultra` ile kapanış denetimi. — **Not (2026-07-30):** gözle kontrol pası + tasarım yan-yana karşılaştırması It-5'te YAPILMADI, kullanıcı pasına ertelendi; borç tek listeye alındı ([visual-check-walkthrough.md](2026-07-26-10-17-visual-check-walkthrough.md)) ve **A13'te teste çevriliyor**, artığı **A14**'te yürünüyor.

---

# KALAN ADIMLAR (2026-07-30 revizyonu) — **3 adım** (A11 + A12 tamamlandı)

A1-A12 bitti; **v7'nin planlı kod iterasyonları tamamlandı.** Kalan adımlar kod planı değil **kapanış**
adımlarıdır. Her birinin **yapıştırmaya hazır promptu** aşağıda, A1-A10 ile aynı biçimde — her adımı
**temiz (yeni) oturumda** başlat, prompt kendi bağlamını dosyalardan kuruyor.

| Adım | Ne | Kim | Model | Effort |
|---|---|---|---|---|
| ~~**A11**~~ ✅ | `CLAUDE.md`'deki 4 bayat olgusal ifadenin düzeltilmesi — **TAMAMLANDI** (`4bb6158`) | agent | **Opus** | **low** |
| ~~**A12**~~ ✅ | Kart animasyonu regresyonu — **TAMAMLANDI** (`739cfa0`, merge `4fb98f4`; kullanıcı gözle doğruladı) | agent | **Opus** | **high** |
| **A13** | Gözle-kontrol borcunun **teste çevrilmesi** + park edilmiş ~60 minor'ın triyajı | agent | **Opus** | **high** |
| **A14** | **Test-düzelt döngüsü** — senin bulguların, dalga dalga (**tekrarlanır**) | sen + agent | **Opus** | **high** |
| **A15** | **Kapanış belge pası** — `CLAUDE.md` · `README.md` · `docs/TRUST-BOUNDARY.md` son duruma | agent | **Opus** | **low** |

**Neden bu sıra:** A12, A13'e bağlı DEĞİLDİ (kusur zaten bildirilmişti) ve animasyon/renk ölüyken UI'ı gezmek
her panelde sahte bulgu üretir — o yüzden regresyon fix'i öne alındı. A13, senin gezeceğin 81 kalemi
15-25'e indirir. A14 asıl test-düzelt döngündür.

## Otorite hiyerarşisi (kalan adımlarda hangi belge bağlayıcı)

Çelişkide **yukarıdaki kazanır**:

| # | Belge | Ne için bağlayıcı |
|---|---|---|
| 1 | [`.superpowers/sdd/progress.md`](../../.superpowers/sdd/progress.md) (ledger) + `it5-records` | **Mevcut durum**: ne yapıldı, ne park edildi, hangi karar hangi ölçümle alındı |
| 2 | [**Plan v7**](2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md) (+ `[SPIKE-AMEND 2026-07-16]`) | **PLAN OF RECORD.** Global Constraints (yasaklar) · A7 UI/UX · A8 test stratejisi · A13 fidelity & WPF kararları · PART B task ID'leri (T1-T72) |
| 3 | [design-v1 README](2026-07-15-19-00-design-v1/README.md) | **Görsel otorite** — v7 A7 zaten "OTORİTE: design-v1" diyor: değerler, süreler, renk token'ları, kopya metinleri BİREBİR |
| 4 | [fizibilite analizi](2026-07-15-23-34-design-wpf-feasibility-analysis.md) | A13.1 "algısal eşdeğer" sınıfı + A13.2 WPF teknik yasakları |
| 5 | Bu playbook | Yalnız **yürütme sırası ve promptlar** — teknik otorite değil |

**v2/v3/v4.x/v5/v6 planları TARİHSELDİR** — kalan adımlarda referans alınmaz. (`CLAUDE.md`'nin v2
referansı **A11'de v7'ye çevrildi** — 2026-07-30, `4bb6158`; artık CLAUDE.md de v7'yi gösteriyor.)

**Promptlar v7'yi TEKRAR ETMEZ, İŞARET EDER.** Yasak listesini prompta kopyalamak iki risk taşır: (a) v7
güncellenirse prompt yalan söyler, (b) kopyalanan kısa liste planın tamamı sanılır. Bu yüzden A12-A15
promptlarında tek satır var — *"v7 Global Constraints + A13 (+ A7/A8) BAĞLAYICI: oku ve uygula; çelişkide v7
kazanır."* Prompta yalnız **v7'de OLMAYAN** proje pratiği yazılır:

- kırmızı-test kuralı (fix'ten önce testin KIRMIZI verdiğini göster),
- realize testi + `Window.Measure/Arrange` HWND dersi (`c6e9a21` / It-5 T1 — plandan sonra ölçülerek öğrenildi),
- per-task review metodu (taze implementer → 3-lens review → fix wave → ledger),
- git akışı ve doküman senkronu.

## SEN NE YAPACAKSIN — sırayla

1. ✅ **A11 TAMAMLANDI** (2026-07-30, `4bb6158`) — `CLAUDE.md`'nin dört olgusal ifadesi koda/plana hizalandı.
2. ✅ **A12 TAMAMLANDI** (2026-07-30, `739cfa0` → merge `4fb98f4`) — kart reveal stagger'ı hiç oynamıyordu;
   kök neden ölçüldü, 1 satırla kapatıldı, 3 regresyon testi kırmızıdan yeşile döndü. **Kullanıcı gözle
   doğruladı** ("ilk aşamada tamam").
3. **Yeni oturum aç** → **Opus / high** → **A13** promptunu yapıştır. **← BURADAN DEVAM** Senden bir şey
   istemez; çıktısı `visual-check-residue.md` (senin gezeceğin **kısa** liste).
4. **Uygulamayı kullan** + o kısa listeyi gez. Gördüğün her kusuru şu formatta not al:
   `hangi panel · ne yaptım · ne bekliyordum · ne gördüm · her seferinde mi`.
5. **Yeni oturum aç** → **Opus / high** → **A14** promptunu yapıştır, bulgularını `<<< >>>` bloğuna yaz.
   Bulgu kalmayana kadar 4-5'i tekrarla (dalga başına 5-15 bulgu ideal).
6. Dalgalar seyreldiğinde **son bir oturum** → **Opus / low** → **A15** promptu: `CLAUDE.md`, `README.md`,
   `docs/TRUST-BOUNDARY.md` son duruma gelir. Bir kez, en sonda.

**Belgeler ne zaman güncelleniyor?** İkili düzen: **(a)** A12/A13/A14'ün her birinde "DOKÜMAN SENKRONU"
kuralı var — yapılan değişiklik bir belgedeki *olgusal* ifadeyi yalanlıyorsa o dalgada düzeltilir, drift
büyümez; **(b)** A15 üç belgeyi baştan sona bir kez denetler. `CLAUDE.md`'nin kendi bayat kalemleri ise en
başta, **A11**'de kapanıyor. `.claude/outputs/` altındaki kayıtlar tarihsel belgedir — geriye dönük
düzeltilmez.

**Test yazma işi sende değil.** Sen kusuru görüp tarif ediyorsun; testi agent yazıyor. Kural: **hiçbir fix,
kusuru yakalayan test kırmızı verdiği gösterilmeden yapılmaz.** (1430 test yeşilken animasyonların ölmesi
tam olarak bu kuralın neden gerektiğini gösteriyor.)

---

## A11 — CLAUDE.md bayat bilgi denetimi · Model: **Opus** · Effort: **low**

> **✅ TAMAMLANDI (2026-07-30, commit `4bb6158`).** Dört kalemin hepsi düzeltildi — `CLAUDE.md`: **satır 3 + 19 + 25 + 26**
> `dotnet build` → **`MSBuild.exe`** (`vswhere` ile resolve; ilke maddesinde "`dotnet build` DEĞİL" gerekçesiyle) ·
> **satır 21** tests TFM `net10.0 (xUnit)` → `net10.0-windows (xUnit, UseWPF)` + WPF realize/STA notu ·
> **satır 9** DURUM: "kod henüz yoktur / hedef yapı" → kod mevcut ve olgun, It-0→It-5 tamamlandı, suite yeşil,
> publish hattı çalışıyor (**rakam gömülmedi**, ledger'a link) · **satır 7 + 11** plan referansı v2 →
> **PLAN OF RECORD = v7** (+ `[SPIKE-AMEND]`; UI otoritesi A7 → design-v1, fidelity A13; v2-v6 "tarihsel").
> Kanıtlar koddan teyit edildi: `Core/MsBuild/MsBuildResolver.cs:21-22` · `Supervisor/Program.cs:362-364` ·
> `Core/MsBuild/MsBuildArguments.cs:10-12` · `tests/…/BuildOrchestrator.Tests.csproj:4-6` · build **0/0**.
>
> **Bu adımda ÇIKAN İKİ YENİ KALEM (sahipleri atandı, kapsam dışı bırakıldı):**
> 1. **Bilinen flaky test → A13 (B) triyajı.** `MsBuildInvokerTests.LingeringPostBuildGrandchild_does_not_stall_success_path`
>    tam suite koşusunda `TimeoutException` verdi (`tests/BuildOrchestrator.Tests/MsBuild/MsBuildInvokerTests.cs:155`
>    — dış `WaitAsync(20s)`, ayrıca `sw.Elapsed < 15s` assert'i), **izole koşuda geçiyor** (16 s, 2/2). Gerçek
>    `MSBuild.exe` + 60 sn yaşayan `ping.exe` grandchild kullanan **yük-hassas** integration testi; paralel suite
>    yükü altında deadline'a çarpıyor. "Suite yeşil" ifadesi bu tek flaky ile birlikte okunmalı.
> 2. **Beşinci bayat ifade → A15.** `CLAUDE.md` proje tablosunun Core hücresi `DiffAnalyzer` sınıfına atıf
>    yapıyor; kodda böyle bir tip **yok** (`Core/Incremental/IncrementalPlanner.cs` var). A11 kapsamı dört
>    ifadeyle sınırlı tutulduğu için dokunulmadı.

> **Durum (adım öncesi kayıt):** It-5'in D3 (README) task'ının review'ı, `CLAUDE.md`'nin **Proje Yapısı / Mimari** bölümünde
> koddan sapmış üç olgusal ifade buldu (dört bağımsız kanıtla kesinleştirildi). It-5'te **düzeltilmedi** —
> kullanıcı kararı: ayrı ele alınacak. **2026-07-30'da dördüncü kalem eklendi** (kullanıcı tespiti):
> `CLAUDE.md` mimari kaynağı olarak hâlâ **v2 plan**'ı gösteriyor, oysa uygulanan plan **v7**'dir.
>
> **Neden önemli:** `CLAUDE.md` her session'da otomatik olarak context'e yükleniyor. Bayat kaldığı sürece
> onu okuyan her agent'ı yanlış yönlendirir. It-5'te bu fiilen yaşandı: D3 implementer'ı README'yi yazarken
> CLAUDE.md ile kodun çeliştiğini görüp durmak ve sormak zorunda kaldı.

**Dört kalem — neyi neyle karşılaştırıyoruz:**

| # | `CLAUDE.md`'deki ifade | Koddaki gerçek | Kanıt |
|---|---|---|---|
| 1 | "her projeyi **shell-out** (`dotnet build` ayrı child process) ile derler" ve Supervisor satırındaki aynı ifade | Kod **hiçbir yerde `dotnet build` çalıştırmıyor**; `MSBuild.exe`'yi vswhere ile çözüp çalıştırıyor | `Core/MsBuild/MsBuildResolver.cs:21-22` (vswhere → `MSBuild\**\Bin\MSBuild.exe`) · `Supervisor/Program.cs:362` yorumu birebir "`[D10] dotnet build DEĞİL, MSBuild.exe`" + `:364` `new MsBuildInvoker(..., location.MsBuildExePath)` · `Core/MsBuild/MsBuildInvoker.cs:68` o exe'yi inner job'a launch ediyor · **kesin belirleyici:** `Core/MsBuild/MsBuildArguments.cs:10-12`'deki `-nodeReuse:false` ve `-clp:Summary` `dotnet build`'in switch'leri değildir |
| 2 | Proje tablosunda `tests/BuildOrchestrator.Tests` → **`net10.0`** (xUnit) | Gerçekte **`net10.0-windows`** + `UseWPF` (WPF testleri var: realize testleri, STA thread testleri) | `tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj` |
| 3 | "**DURUM:** Proje sıfırdan yeniden kuruluyor… **Kod henüz yoktur**; bu tablo hedef yapıdır." | Kod var ve olgun: It-0→It-5 tamamlandı, **1430 test** yeşil, publish hattı çalışıyor | `git log` · `.superpowers/sdd/progress.md` · `dotnet test` |
| 4 | Başlık "Proje Yapısı / Mimari **(hedef — v2 plan)**" ve gövdedeki "onaylı **v2 plan**'a dayanır: `2026-06-27-22-46-build-orchestrator-yeni-plan.md`" (satır 7 + 9) | Uygulanan plan **v7**'dir: `2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md`. v2, v3→v4→v4.1→v4.2→v4.3→v5→v6→v7 zincirinin ilk halkası — **tarihsel**. Bugünkü yürütme kaynağı v7 (+ `[SPIKE-AMEND 2026-07-16]`) ve onun **A7 (UI otoritesi = design-v1)** / **A13 (fidelity & WPF kararları)** / **Global Constraints** bölümleridir | Playbook'un tamamı v7'yi referans veriyor · `.superpowers/sdd/progress.md` v7 task ID'leriyle (T1-T72) yürüdü · v7 PART C It-0→It-5 = fiilen uygulanan yol haritası |

> **Not:** `-p:UseSharedCompilation=false -nodeReuse:false` flag'lerinden bahseden satır **doğrudur**
> (bkz. [t33-decision.md](2026-07-26-07-38-t33-decision.md)) — yalnız o flag'leri taşıyan komutun
> `dotnet build` değil `MSBuild.exe` olduğu düzeltilecek.

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md — PLAN OF RECORD, bu
   projede fiilen uygulanan plan. (Bu adım için başlık + "v7 KARAR KAYDI" + "Global Constraints" + PART C
   yeter; 75 KB'ın tamamını okuma.)
2. CLAUDE.md (proje kökü) — özellikle "Proje Yapısı / Mimari" bölümü (satır 7-9 civarı; başlık şu an
   yanlışlıkla "(hedef — v2 plan)" ekiyle duruyor ve gövdesi v2 planına link veriyor — DÜZELTİLECEK
   OLAN BU, aşağıdaki kalem 4)
3. .claude/outputs/2026-07-16-09-40-v7-execution-playbook.md — "A11" bölümü (karşılaştırma tablosu orada)

DİKKAT — v2 YANILGISI: CLAUDE.md her session'da context'e yüklendiği için, bu adıma başlarken senin
context'inde de "onaylı v2 plan" ifadesi duruyor olacak. O ifade BAYATTIR. Uygulanan plan **v7**'dir
(2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md + içindeki [SPIKE-AMEND 2026-07-16]);
v2 → v3 → v4 → v4.1 → v4.2 → v4.3 → v5 → v6 → v7 zincirinin ilk halkası olan v2 yalnız TARİHSELDİR.
Mimariyi v2'ye göre değerlendirme, v2'den alıntı yapma.

DURUM: It-0..It-5 main'de, main == origin/main, build 0/0, suite 1430 passed / 2 skipped / 0 failed.
v7'nin planlı kod iterasyonları bitti.

Görev: CLAUDE.md'deki DÖRT olgusal ifade koddan/plandan sapmış; düzelt.
1) "her projeyi shell-out (dotnet build ayrı child process) ile derler" (hem mimari ilkeler hem Supervisor
   satırı) → kod dotnet build DEĞİL, vswhere ile çözülen MSBuild.exe çalıştırıyor.
2) Proje tablosunda tests/BuildOrchestrator.Tests TFM'i "net10.0" → gerçekte net10.0-windows + UseWPF.
3) "DURUM: Proje sıfırdan yeniden kuruluyor... Kod henüz yoktur; bu tablo hedef yapıdır." → kod var ve
   olgun (It-0..It-5 tamam, 1430 test yeşil, publish hattı çalışıyor).
4) Başlıktaki "(hedef — v2 plan)" ve gövdedeki "onaylı v2 plan'a dayanır: 2026-06-27-22-46-build-
   orchestrator-yeni-plan.md" → uygulanan plan V7'dir:
   .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (+ içindeki
   [SPIKE-AMEND 2026-07-16] bölümü). v2 tarihsel ilk halkadır; referansı v7'ye çevir ve v2'yi istersen
   "tarihsel" diye tek parantezde bırak. UI/görsel otorite v7 A7 üzerinden design-v1'dir
   (.claude/outputs/2026-07-15-19-00-design-v1/README.md); WPF fidelity kararları v7 A13'tedir.

ÖNCE HER İDDİAYI KODDA/PLANDA DOĞRULA (playbook'taki kanıtları teyit et, körlemesine uygulama). Sonra düzelt.

RAKAM GÖMME: CLAUDE.md'ye "1430 test yeşil" gibi her dalgada değişecek bir sayı YAZMA — sonraki adımlar
(A13/A14) test ekleyecek, sayı ertesi gün bayatlar ve bu adımı tekrar yaptırır. Dayanıklı dil kullan:
"It-0→It-5 tamamlandı; suite yeşil, publish hattı çalışıyor." Güncel sayı zaten ledger'da
(.superpowers/sdd/progress.md) ve it5-records'ta.

KAPSAM YALNIZ BU DÖRT OLGUSAL İFADEDİR. Dil kuralları, çıktı/özet/handoff dizin kuralları, git kuralları,
build/test komutları ve talimatların geri kalanı DEĞİŞMEZ. Üslup ve biçim mevcut dosyayla aynı kalsın.

Ayrıca: düzeltirken "hedef yapı" dili yerine mevcut durumu anlatan bir dil kullan, ama tabloyu yeniden
tasarlama — yalnız yanlış hücreleri düzelt.

Bitince bana neyi neye çevirdiğini dosya:satır ile göster ve commit et.
```

**Bitti kriteri:** Dört ifade de kodla/planla uyumlu; kapsam dışına çıkılmamış (diff yalnız o dört yeri
gösteriyor) — özellikle **mimari kaynağı artık v7'yi işaret ediyor**.

**Senin işin:** yok — bu adım tamamen agent'ta. Prompt'u yapıştır, bitince "commit et" de.

---

## A12 — Bilinen regresyon: kart animasyonları / renklendirmeler · Model: **Opus** · Effort: **high**

> **✅ TAMAMLANDI (2026-07-30, fix `739cfa0` · doküman `879f376` · merge `4fb98f4`).** Kullanıcı gözle
> doğruladı. Kayıt: [motion-regression-fix](2026-07-30-13-04-motion-regression-fix.md).
>
> **Kök neden (ölçüldü):** kartların kademeli beliriş animasyonu (`bo-reveal`) üretimde **HİÇ oynamıyordu**.
> `Controls/StickyLayerList.xaml.cs::SetGroups` içinde `_revealPending = true` bayrağı
> `Flow.ItemsSource = entries` atamasından **SONRA** kuruluyordu. Üretimdeki sıra "kabuk realize edilir,
> gruplar sonra akar" (`MainWindow.xaml.cs:361`) olduğu için liste **zaten realize**; o durumda `ItemsSource`
> ataması container üretimini **senkron** bitiriyor → `OnGeneratorStatusChanged` o satırın **içinde**
> ateşleniyor, bayrağı `false` görüp dönüyor, **bir daha status değişimi gelmiyor** → `PlayRevealStagger`
> hiç çağrılmıyor → satır opaklığı hiç 0'a çekilmiyor. **Fix: bayrak atamadan önceye alındı (1 satır).**
>
> **Ölçüm:** ilk Sync'te 4 kart, 19 ms aralıkla — **fix öncesi 721 karede 0 ara-opaklık karesi**
> (300 ms'lik rampa ~15 ara kare üretirdi), **fix sonrası 737 karede 5 ara kare** + satır 4'ün satır 1-3'ün
> gerisinde kalması (**10 ms/satır stagger gözlendi**).
>
> **Suite neden yeşildi:** `StickyRevealTests`'in **yedi testi de** `PlayRevealStagger()`'ı **DOĞRUDAN**
> çağırıyor, yardımcısı da `SetGroups`'u realize'den **ÖNCE** yapıyor (o sırada üretim ertelenir, hatalı sıra
> hiç tetiklenmez) → tetikleyici hiç sınanmıyordu. Yeni: `App/StickyRevealTriggerTests.cs` (3 test,
> fix'ten önce **3/3 KIRMIZI**), üretim sırasını kuran `RealizeEmptyThenFeed` yardımcısıyla.
>
> **Playbook'un beş hipotezi de ELENDİ (ölçümle):** `MotionGate` tek kapısı · `StaticAnimationsEnabled`
> snapshot'ı · G2 donmuş paylaşılan `ScaleTransform` · G2 `IconPaint` self-heal · L1 tembel alt-ağaç.
> Canlı Build koşusunda şerit renkleri / glyph'ler / will-dot / süre **doğru**, **spinner dönüyor**
> (kare farkı 13,5-19,0) ve **nefes salınıyor** (2,8-10,2). *"Renklendirmeler yok"* yakınmasının ölçülebilir
> karşılığı **bulunamadı** — kullanıcı hâlâ görürse A14 dalgasına yazılacak.
>
> **Kapsam:** desen tek yerde. Graf reveal'i `SetGraph` içinden **senkron** tetikliyor
> (`Graph/GraphView.xaml.cs:364`); konsol/event stream bu deseni hiç kullanmıyor → etkilenmediler.
>
> ### >>> A12'nin ürettiği ve A13'ü DOĞRUDAN etkileyen bulgu: **harness ekran görüntüsü ALABİLİYOR**
> Bu playbook (ve `it5-records` §4 / walkthrough) "harness ekran görüntüsü alamaz → gözle kontrol kullanıcıya
> ait" diyordu. **Bu yanlış.** `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` pencere içeriğini **örtülü olsa
> bile** bitmap'e alır; UIA ile ağaç okunup buton `Invoke`/`Toggle` edilir; kare-arası piksel farkı ve
> parlaklık serisiyle **animasyon ölçülebilir** (bu adımda tam olarak böyle ölçüldü). **A13 bunu kullanmalı:**
> "göz ister" sanılan bazı kalemler (animasyon gerçekten oynuyor mu, stagger sırası, renk gerçekten uygulandı
> mı) aslında **ölçülebilir** ⇒ artık listeye atılmadan önce bu kanal denenmeli.
> **DPI tuzağı:** PowerShell 5.1 DPI-unaware'dır → `GetWindowRect` **sanallaştırılmış** (1400×800), UIA
> **fiziksel** (1750×1000) verir. Bitmap boyutu **UIA'nın `BoundingRectangle`'ından** alınmalı, yoksa yakalama
> kırpılır (bu adımda önce kırpıldı ve action bar "yok" sanıldı).
>
> **Dürüstlük kaydı — çürütülen ara-hipotez:** teşhis ortasında *"`SystemParameters.ClientAreaAnimation`
> önbelleğe alınıp hiç tazelenmiyor"* diye yanlış bir kök nedene varıldı; kendi ölçümüyle çürütüldü — o test
> ayarı `SPIF_SENDCHANGE (2)` ile yazıyordu (ayarı kalıcılaştırmaz, WPF invalidation'ını tetiklemez).
> Windows Ayarlar'ın kullandığı `SPIF_UPDATEINIFILE|SPIF_SENDCHANGE (3)` ile sinyal **doğru çalışıyor**
> (`signal.Changed=1`, `StaticPropertyChanged=1`, iki yönde de). O premise üzerine yazılmış 4 kırmızı test
> **silindi**; üretim kodu onlara göre değiştirilmedi.

> **KUSUR (kullanıcı bildirdi, 2026-07-26 — tarihsel kayıt):** *"Sol alt köşedeki kartlarda loading ile animasyonlar
> çalışırdı; bu adımda hiç hareket etmiyor, animasyonlar yok, renklendirmeler vs hiç çalışmıyor."*
> **It-4b sonunda çalışıyordu, It-5'ten sonra bozuk.** Suite 1430 yeşil olduğu hâlde bozulması, kusurun
> **headless suite'in görmediği bir runtime yolunda** olduğunu söylüyor — `c6e9a21` ile aynı sınıf.
>
> **Neden A13'ten (gözle kontrol) ÖNCE:** animasyon ve renklendirme ölüyken UI'ı gezmenin anlamı yok —
> her panelde sahte bulgu üretir. Bu adım gözle kontrole bağlı DEĞİL; kusur zaten bildirilmiş.
>
> **İlk bakılacak yerler (hipotez, doğrulanacak — It-5'te bu alanlara dokunuldu):**
> 1. **W2 motion fold** — `Controls/MotionGate.cs`. `App.Motion?.AnimationsEnabled ?? false` ifadesinin
>    **9 kopyası tek noktaya** indirildi. Tek kapı yanlış çözümlenirse **tüm** code-driven animasyonlar aynı
>    anda susar; "hiç hareket etmiyor" tam olarak tek-nokta arızası profilidir. Latch-first ↔ latch'siz kip
>    seçimi ve `App.Motion`'ın kart kurulurken null olup olmadığı ilk kontrol.
> 2. **W2 fix round 1** — `ConsoleView` (5 çağrı) + `PopIn` (1) `MotionGate.StaticAnimationsEnabled`'a
>    bağlandı. **Statik** okuma, canlı okumanın yerine geçtiyse ve snapshot erken alınıyorsa animasyon hiç
>    açılmaz.
> 3. **G2 ikon değişikliği** — `Viewbox` → **paylaşılan donmuş `ScaleTransform`**. **A13.2 zaten uyarıyor:**
>    *"frozen/paylaşılan brush anime edilemez"*. Aynı ilke transform için de geçerli; animasyon yolundaki
>    per-instance bir kaynak paylaşılan/frozen bir kaynakla değiştiyse animasyon sessizce no-op'a düşer —
>    bu **"renklendirmeler çalışmıyor"** yakınmasını da açıklar.
> 4. **G2 parked minor** — *"`IconPaint` self-heal turunun fast-path'le kalkması"* (ledger'da kayıtlı).
>    Renk uygulamasını doğrudan ilgilendiriyor.
> 5. **L1 tembel kart** — `ProjectRow.xaml.cs::EnsureActions` + yeni `ProjectRowActions.xaml`. Durum/spinner
>    veya renklendirme elemanları yanlışlıkla **tembel alt-ağaca** düştüyse ancak hover'da kurulur.
>
> **Bu hipotezler doğrulanmadan koda dokunma** — önce hangi katmanda öldüğünü ölç (gate mi false dönüyor,
> animasyon mu başlamıyor, başlıyor da görsel mi değişmiyor).

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/handoffs/ altındaki EN YENİ handoff + işaret ettiği özet
2. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (PLAN OF RECORD — bu adım
   için YALNIZ "Global Constraints" + "A7 UI/UX" + "A13 Fidelity & WPF kararları"; tamamı gerekmez.
   v2/v3/v4.x/v5/v6 planları TARİHSELDİR — referans alma, alıntı yapma)
3. .claude/outputs/2026-07-16-09-40-v7-execution-playbook.md — "A12" bölümü (hipotez listesi orada)
4. .claude/outputs/2026-07-26-10-17-it5-records.md (It-5 kabul kaydı + park edilen kalemlerin tam tablosu)
5. .claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md (A13.1 / A13.2 + Ek A)
6. .superpowers/sdd/progress.md — It-5 bölümü (en üstteki RESUME HERE; çelişkide ledger kazanır)

DURUM: It-0..It-5 main'de (tek trunk). Build 0/0, suite yeşil (güncel sayı ledger'da). v7'nin planlı kod
iterasyonları BİTTİ; A11 (CLAUDE.md denetimi) de bitti — CLAUDE.md artık koda hizalı ve PLAN OF RECORD
olarak v7'yi gösteriyor, ama otorite sırası değişmedi: çelişkide ledger > v7 > CLAUDE.md. Bu adım TEK BİR
REGRESYONU kapatır — başka iş alma, gözle kontrol pası bir SONRAKİ adımda (A13).

BİLİNEN FLAKY (bu adımın işi DEĞİL, kovalamayacaksın): MsBuildInvokerTests.
LingeringPostBuildGrandchild_does_not_stall_success_path (MsBuildInvokerTests.cs:155) tam suite koşusunda
yük altında 20 sn dış deadline'a çarpıp TimeoutException verebiliyor; izole koşuda geçiyor. Suite'i
koşarken TEK kırmızı bu ise regresyon sayma — izole koş, geçiyorsa geç; triyajı A13'te.

GÖREV: It-4b'de çalışan kart animasyonları/renklendirmeleri It-5'ten sonra ÇALIŞMIYOR — kullanıcının
bildirimi: "sol alt köşedeki kartlar, loading animasyonları, hiç hareket yok, renklendirmeler yok".
Suite yeşil olduğu hâlde bozuk => kusur headless suite'in görmediği bir runtime yolunda (c6e9a21 sınıfı).

ÖNCE TEŞHİS, SONRA FİX. Hangi katmanda öldüğünü ÖLÇ: motion gate false mu dönüyor · animasyon başlamıyor
mu · başlıyor da görsel mi değişmiyor. Bakılacak ilk yerler (hipotez — doğrulanmadan koda DOKUNMA):
  1. Controls/MotionGate.cs — W2'de 9 kopya tek kapıya indirildi; tek kapı yanlışsa TÜM animasyonlar
     aynı anda susar. Latch-first ↔ latch'siz kip ve kart kurulurken App.Motion null mı.
  2. ConsoleView (5) + PopIn (1) → MotionGate.StaticAnimationsEnabled; statik okuma canlı okumanın
     yerine geçip erken snapshot alıyorsa animasyon hiç açılmaz.
  3. G2: ikon Viewbox → paylaşılan DONMUŞ ScaleTransform. A13.2: frozen/paylaşılan kaynak anime
     edilemez — animasyon sessizce no-op'a düşer; "renklendirmeler yok" yakınmasını da açıklar.
  4. G2 parked minor: "IconPaint self-heal turunun fast-path'le kalkması" (ledger'da kayıtlı).
  5. L1: ProjectRow.xaml.cs::EnsureActions + ProjectRowActions.xaml — durum/spinner/renk elemanları
     yanlışlıkla tembel alt-ağaca düştüyse ancak hover'da kurulur.
Teşhisi KANITLA (hangi dosya:satır, neden ölüyor), sonra düzelt.

KAPSAM: teşhis kusurun graf düğümlerini / konsolu / event stream'i de etkilediğini gösterirse aynı kök
nedeni oralarda da kapat. Kök nedenle ilgisi olmayan başka görsel kusurları BU ADIMDA alma — onlar A13/A14.

Kurallar:
- V7 BAĞLAYICI: yasaklar ve teknik kararlar planın kendisindedir — "Global Constraints" + "A13 Fidelity &
  WPF kararları" + "A8 Test Stratejisi". OKU ve UYGULA; bu prompt onları tek tek TEKRAR ETMEZ. Çelişkide v7
  kazanır.
- superpowers:systematic-debugging ile teşhis; hipotezleri ölçerek ele.
- REGRESYON TESTİ ZORUNLU: kusuru yakalayan testi ÖNCE yaz ve fix'ten ÖNCE KIRMIZI verdiğini GÖSTER.
  Bu adımın tamamı zaten "yeşil suite bir şeyi kaçırdı" üzerine kurulu.
- REALIZE TESTİ (v7'de YOK, sonradan ölçülerek öğrenildi): yeni XAML kökü/template eklersen
  DsResources.Realize üzerinden realize testi de ekle (gerekçe c6e9a21). Window.Measure/Arrange HWND'siz
  İÇERİĞE İNMEZ — realize window.Content üzerinde yapılmalı (It-5/T1).
- Repo'daki token guard'larını (renk/motion/D8) çalıştır.
- DOKÜMAN SENKRONU: bitirmeden önce, yaptığın değişikliğin CLAUDE.md / README.md / docs/TRUST-BOUNDARY.md
  içindeki bir OLGUSAL ifadeyi geçersiz kılıp kılmadığını KONTROL ET. Kılıyorsa aynı dalgada düzelt, aynı
  commit serisine koy. Kılmıyorsa DOKUNMA (kozmetik doc düzenlemesi yapma). Sayı gömme — güncel test sayısı
  ledger'dadır.
- Git: kendi çalışma branch'ini aç, task başına commit at, iş bitince main'e merge + push, merge'ü
  DOĞRULADIKTAN sonra branch'i sil, oturumu main'de bırak.

ÇIKTI:
1. .claude/outputs/YYYY-MM-DD-HH-mm-motion-regression-fix.md — teşhis (hangi katmanda öldü, kanıt) +
   fix + kırmızıdan yeşile dönen testler.
2. Bana 5-8 maddelik KISA bir göz kontrolü listesi ver ("uygulamayı çalıştır, şuna bak, şunu görmelisin")
   — sadece bu fix'in doğrulaması için, uzun walkthrough DEĞİL.

Takılırsan veya çözümü büyük bir sorun görürsen durup bana bildir. Bitince aşamamızı kaydet.
```

**Bitti kriteri:** Teşhis kanıtlı (dosya:satır) · regresyon testi kırmızıdan yeşile döndü · suite yeşil ·
main'e merge + push.

**Senin işin:** agent bitirince uygulamayı bir kez aç ve verdiği 5-8 maddelik listeye bak — kartlar hareket
ediyor mu, renkler geliyor mu. **Hâlâ ölüyse aynı oturumda söyle** (teşhis bağlamı elinde). Yaşıyorsa A13'e
geç.

---

## A13 — Gözle-kontrol borcunun otomatikleştirilmesi + park listesi triyajı · Model: **Opus** · Effort: **high**

> **Amaç:** 81 kalemlik gözle-kontrol listesini **senin sırtından alıp süite taşımak.** Kalemlerin çoğu
> aslında ölçülebilir bir değere dayanıyor (yükseklik, padding, renk token'ı, kopya metni, binding, realize) —
> bunlar test olur. Yalnız gerçekten göz isteyenler (akıcılık hissi, renk algısı, OS davranışı) sana kalır.
> Hedef: **81 → 15-25 kalem.**
>
> **İkinci iş:** `it5-records` §2'deki **18 satır / ~60 park edilmiş minor** triyaj edilir; gerçek kusur
> olanlar burada kapanır. Kullanıcı kararı (2026-07-30): *"sıkıntılı konular varsa bakılsın, düzeltilsin."*
>
> **Girdi:** [2026-07-26-10-17-visual-check-walkthrough.md](2026-07-26-10-17-visual-check-walkthrough.md)
> (BÖLÜM 1 It-4b'nin 81 kalemi · BÖLÜM 2 prototiple yan yana design-v1 §2.1-§2.9 · BÖLÜM 3 It-5'in kendi
> görsel kalemleri) + [2026-07-26-10-17-it5-records.md](2026-07-26-10-17-it5-records.md) §2
> + [2026-07-30-13-04-motion-regression-fix.md](2026-07-30-13-04-motion-regression-fix.md) (A12'nin ölçüm
> kanalı + §8'deki 6 devredilen kalem).
>
> **⚠️ A12'DEN GELEN REVİZYON — bu adımın hedefi büyüdü:** "harness ekran görüntüsü alamaz" varsayımı
> **çürütüldü** (bkz. A12 bölümü). `PrintWindow(PW_RENDERFULLCONTENT)` + UIA + kare-arası piksel farkı ile
> **canlı uygulamada animasyon ölçülebiliyor.** Yani daha önce kaçınılmaz "GÖZ İSTER" sanılan kalemlerin bir
> kısmı (animasyon gerçekten oynuyor mu · stagger sırası · renk gerçekten uygulandı mı · bir öğe gerçekten
> görünür mü) **pinlenebilir**. A13, bir kalemi artık listeye atmadan önce bu kanalı denemek zorundadır.

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. .claude/outputs/2026-07-26-10-17-visual-check-walkthrough.md (81 kalem + BÖLÜM 2 prototip karşılaştırması
   + BÖLÜM 3 It-5 görsel kalemleri) — bu adımın BİRİNCİL girdisi
2. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (PLAN OF RECORD — bu adım
   için "Global Constraints" + "A7 UI/UX (OTORİTE: design-v1)" + "A8 Test Stratejisi" + "A13 Fidelity &
   WPF kararları"; bir kalemin kabul ölçütü tartışmalıysa v7 bağlayıcıdır. v2/v3/v4.x/v5/v6 planları
   TARİHSELDİR — referans alma)
3. .claude/outputs/2026-07-15-19-00-design-v1/README.md (görsel otorite; değerler ve kopya metinleri BİREBİR)
4. .claude/outputs/2026-07-26-10-17-it5-records.md — özellikle "2. Kapanmayan / bilinçli park edilen
   kalemler" tablosu (18 satır, ~60 minor)
5. .claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md (A13.1 "algısal eşdeğer" / A13.2)
6. .superpowers/sdd/progress.md (ledger; çelişkide ledger kazanır — NOT: en üstteki RESUME HERE It-5'te
   kalmış ve BAYAT; A11+A12 bittiği için "sıradaki iş" satırını olduğu gibi almayın, güncel durum en yeni
   handoff'tadır)
7. .claude/handoffs/ altındaki EN YENİ handoff
8. .claude/outputs/2026-07-30-13-04-motion-regression-fix.md — A12 teşhis kaydı. İKİ nedenle bu adımın
   girdisi: (a) §1.1'deki ÖLÇÜM KANALI (aşağıya bak), (b) §8'de A13/A14'e devredilen 6 kalem.

DURUM: It-0..It-5 main'de (main == origin/main), build 0/0, suite yeşil (güncel sayı ledger'da). Kod planı
bitti. A11 (CLAUDE.md denetimi) ve A12 (kart reveal stagger regresyonu) kapandı; A12'yi kullanıcı gözle
doğruladı. Bu adımda İKİ iş var.

>>> BU ADIMIN HEDEFİNİ BÜYÜTEN BULGU (A12'de ölçüldü — ESKİ VARSAYIM ÇÜRÜTÜLDÜ)
Walkthrough ve it5-records "harness ekran görüntüsü alamaz, gözle kontrol kullanıcıya ait" diyor. BU YANLIŞ.
Ölçülmüş kanal: PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT) pencere içeriğini PENCERE ÖRTÜLÜ OLSA BİLE
bitmap'e alır; UI Automation ile ağaç okunur ve buton Invoke/Toggle edilir; kare-arası piksel farkı +
parlaklık serisi ile ANİMASYON ÖLÇÜLEBİLİR. A12 kök nedeni tam olarak böyle bulundu (19 ms aralıkla 721
kare → 0 ara-opaklık karesi = animasyon hiç oynamıyor).
  - Uygulama gerçek OSYS ile açılıp Sync/Build sürülebilir (A12 bunu yaptı; kalıcı yan etki yok).
  - DPI TUZAĞI: PowerShell 5.1 DPI-unaware → GetWindowRect SANALLAŞTIRILMIŞ (1400x800), UIA FİZİKSEL
    (1750x1000) verir. Bitmap boyutunu UIA'nın BoundingRectangle'ından al, yoksa yakalama kırpılır.
  - UIA notu: action bar'ın filtre/segment butonları InvokePattern DEĞİL TogglePattern sunar; "Sync"
    InvokePattern sunar; menü öğeleri (ör. Rebuild) hiç pattern sunmaz → SetWindowPos(HWND_TOPMOST) ile
    pencereyi öne al, fiziksel tıkla (SetForegroundWindow arka plan process'ten çalışmaz).
SONUÇ: bir kalemi "GÖZ İSTER"e atmadan ÖNCE bu kanalı dene. Gerçekten göz isteyenler daralır.

(A) GÖZLE-KONTROL BORCUNUN OTOMATİKLEŞTİRİLMESİ
Walkthrough'un HER kalemini (BÖLÜM 1 + 2 + 3, tamamı) tek tek sınıflandır:
  - PİNLENEBİLİR → TEST YAZ. Şunlar pinlenebilir: XAML/kod değerinin design-v1 değeriyle karşılaştırılması
    (yükseklik, padding, kolon, renk/motion token'ı), kopya metni birebirliği, realize testi
    (DsResources.Realize), ölçü/geometri assert'i, binding/CanExecute canlılığı, durum→şablon eşlemesi,
    kaynak-deseni guard'ı. Test yoksa kalem KAPANMAZ.
  - GÖZ İSTER → ARTIK LİSTEYE yaz. Yalnız: akıcılık/hız hissi, animasyon estetiği, renk algısı, prototiple
    yan yana genel izlenim, OS davranışı (tepsi, global hotkey, DPI değişimi, ekran okuyucu).
Sınıflandırmayı GEREKÇESİZ yapma: her "GÖZ İSTER" kalemi için NEDEN pinlenemediğini tek satırla yaz.
"Zor" gerekçe değildir; "assert edilebilir bir değeri yok" gerekçedir. "Harness göremez" de ARTIK gerekçe
DEĞİLDİR (yukarıdaki kanal) — kullanacaksan neden yetmediğini yaz. Emin olamadığın kalemi GÖZ İSTER'e at ama
bunu belirt.
Not: walkthrough'un D4 (konsol gerçek akış) kalemi ZORUNLU işaretli — pinlenebilir kısmını teste çevir,
kalanını artık listede ZORUNLU olarak işaretle.

TETİKLEYİCİ DERSİ (A12, bağlayıcı): bir davranışı test ederken onu ÜRETİMDEKİ YOLDAN tetikle. A12'nin kusuru
tam olarak burada saklanmıştı — 7 test animasyonu doğrudan çağırıyordu, tetikleyici hiç sınanmıyordu ve
üretimde animasyon HİÇ oynamıyordu. Bu adımda yazdığın her "X doğru oynar" testi için sor: "X üretimde
GERÇEKTEN çağrılıyor mu, onu kim tetikliyor, o tetikleyici testli mi?" Ayrıca kurulum sırası üretimle aynı
olmalı (kabuk realize → sonra veri akar); tersi sıra senkron/asenkron farkını gizler.

(B) PARK EDİLMİŞ KALEMLERİN TRİYAJI (it5-records §2 — 18 satır, ~60 minor)
Her kalemi üç kovaya ayır:
  1. GERÇEK KUSUR → bu adımda KAPAT (önce kırmızı test, sonra fix).
  2. KABUL EDİLEN BORÇ → gerekçesiyle listede kalsın.
  3. ARTIK GEÇERSİZ (kod değişti / dayanağı yok) → sil, nedenini yaz.
Öncelik: davranış/veri doğruluğu > a11y (G2'deki AutomationProperties.Name eksikleri) > üretimde duran
debug hook'u (debugSpawnChildren — Contracts/Ipc/IpcMessages.cs:22 + Supervisor/SupervisorHost.cs:80) >
test/kayıt zayıflıkları > kozmetik.
EK KALEMLER — it5-records listesinde YOK, A11/A12'de ÖLÇÜLEREK bulundu; hepsini aynı üç kovaya sok:

 E1. FLAKY ÜÇLÜSÜ (yük-hassas; hepsi izole koşuda geçiyor, tam suite'te ara sıra kırmızı):
     · MsBuildInvokerTests.LingeringPostBuildGrandchild_does_not_stall_success_path
       (MsBuildInvokerTests.cs:155) — dış WaitAsync(20s) + sw.Elapsed<15s assert'i, gerçek MSBuild.exe +
       60 sn yaşayan ping.exe grandchild'ına bağlı (A11'de bulundu).
     · EngineHostTests.Start_receives_engineReady_and_ping_pong_works (A12'de bulundu)
     · RunViewModelTests.RebuildCommand_enables_Stop_and_disables_Rebuild_before_runStarted_arrives (A12'de)
     A12'de son ikisi bir tam koşumda kırmızı verdi, izole 2/2 geçti, ikinci tam koşum 0 failed. Seçenekler:
     deadline'ı/beklemeyi yük-bağımsız sinyale çevir (gerçek kusur) · kabul edilen borç olarak gerekçesiyle
     kaydet · seri collection'a al. ÜÇÜNÜ birlikte değerlendir (aynı kök: gerçek zamana bağlı bekleme).
     Sessizce "yeşil" sayma.

 E2. Başarılı Sync'ten SONRA bile başlıkta "no repository" yazıyor ve action bar'daki branch chip'i BOŞ
     ("branch"), oysa v7 A7 başlıkta "OSYS · main" ve chip'te branch değeri bekliyor. A12'de canlı gözlendi
     (4 proje başarıyla sync edildi, graf/kartlar doldu, başlık yine "no repository"). Muhtemelen aynı kökten:
     konsolda "warning: git fetch failed — continuing against the local HEAD (... remote-tracking ref
     okunamadı)". Repo adı/branch tespitinin fetch başarısına gereksiz bağlı olup olmadığını KONTROL ET.

 E3. Konsola TÜRKÇE kullanıcı metni sızıyor: "warning: git fetch failed — continuing against the local HEAD
     (git fetch başarılı ama remote-tracking ref okunamadı…)". Uygulama İngilizce-only; D1'in 77 metinlik
     süpürmesinden artakalan. Aynı turda TÜM ağacı yeniden tara (D1 taramasının kaçırdığı başka metin var mı).

 E4. Sync "no changes" döndüğünde SetGroups çağrılmıyor → o Sync'te reveal de oynamaz (kartlar yeniden
     belirmez). Kasıtlı görünüyor (tam reset'ten kaçınma, StickyLayerList "koleksiyon reset YOK" kuralı) ama
     KARAR KAYDI YOK. Ya gerekçeyi koda/belgeye yaz (kabul edilen borç) ya da tasarım kararını netleştir.

 E5. StickyLayerList.CollectRows() realize olmamış satırı atlıyor ve yorumu "bir sonraki reveal onu yakalar"
     diyor; oysa SetGroups yalnız topoloji değişiminde koştuğu için BİR SONRAKİ REVEAL GELMEYEBİLİR (E4 ile
     aynı kök). A12'nin testi "en az bir satır toplandı"yı pinliyor ama kısmi realize hâlâ sessiz.

 E6. src/BuildOrchestrator.App/Services/SystemParametersMotionSignal.cs — OS reduced-motion sinyaline dokunan
     TEK sınıf — SIFIR TESTLİ; tüm reduced-motion testleri FakeMotionSignal enjekte ediyor. A12'de kodu
     ÖLÇÜLDÜ ve DOĞRU çıktı (SPIF_UPDATEINIFILE|SPIF_SENDCHANGE ile canlı takip iki yönde tutuyor), yani
     gerçek kusur DEĞİL — ama koruması yok. Test yazmak makine-global bir erişilebilirlik ayarını
     değiştirmeyi gerektiriyor (A12'de bir kez ayar yanlışlıkla KAPALI kaldı, elle geri alındı) → riski
     kullanıcıya sor, kendi başına suite'e ekleme.

AÇILMAYACAKLAR (ölçüme dayalı kullanıcı/stop-gate kararları, yeniden tartışma): L2 liste virtualization ·
G1 DrawingVisual katman göçü · W2 guard kopyalarının katlanması · T33 shared compilation.

ÇIKTI:
1. .claude/outputs/YYYY-MM-DD-HH-mm-visual-check-residue.md — SADECE göz isteyen kalemler, uygulamada
   gezilecek sıraya göre (pencere → sol panel → graf → konsol → action bar → popover → ayarlar → tepsi).
   Her kalem TEK SATIR: "ne yap / ne görmelisin". Bu benim yürüyeceğim liste — mümkün olduğunca kısa olsun
   ama kalem GİZLEME. Başına: kaç kalem pinlendi / kaç kalem kaldı.
2. .claude/outputs/YYYY-MM-DD-HH-mm-parked-items-triage.md — üç kovalı tablo + kapatılanların commit'i.
3. Süite eklenen testler. Raporla: X yeni test, walkthrough'un Y/81 kalemini pinliyor.

Kurallar:
- V7 BAĞLAYICI: yasaklar, teknik kararlar ve kabul ölçütleri planın kendisindedir — "Global Constraints" +
  "A7 UI/UX" + "A8 Test Stratejisi" + "A13". OKU ve UYGULA; bu prompt onları TEKRAR ETMEZ. Çelişkide v7
  kazanır. Bir kalemin "doğru"su tartışmalıysa önce v7'ye, sonra design-v1'e bak.
- Önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-{baslik}.md), sonra
  superpowers:subagent-driven-development ile task-by-task.
- PER-TASK METOD (bağlayıcı): taze implementer → scripts/review-package BASE HEAD → 3-lens paralel review
  (spec/design-fidelity · WPF/threading+A13.2 · testler/yapı) → tek fix wave → scoped re-review → ledger.
  Aynı worktree'de İKİ İMPLEMENTER PARALEL KOŞTURMA (It-5 dersi); read-only reviewer'lar paralel serbest.
- Yazdığın her testin GERÇEKTEN AYIRT ETTİĞİNİ göster: değeri bilerek boz, test KIRMIZI olsun, geri al.
  Her zaman yeşil kalan test kalem kapatmaz.
- REALIZE TESTİ (v7'de YOK, sonradan öğrenildi): yeni XAML kökü/template için ZORUNLU (gerekçe c6e9a21).
  Window.Measure/Arrange HWND'siz İÇERİĞE İNMEZ — realize window.Content üzerinde yapılmalı.
- Repo'daki token guard'larını (renk/motion/D8) çalıştır.
- Süit süresi ölçülebilir şekilde artıyorsa raporla (It-5'te perf testlerinin maliyeti ölçülmemişti).
- DOKÜMAN SENKRONU: (B)'de kapattığın her kalem için belgeleri kontrol et. ÖZELLİKLE: debugSpawnChildren
  üretimden kaldırılırsa docs/TRUST-BOUNDARY.md §3 "Komut yönü (App → Supervisor) hangi girdileri kabul
  ediyor" bölümü GÜNCELLENMELİ. Aynı şekilde bir davranış/akış/komut değişirse CLAUDE.md ve README.md'nin
  ilgili bölümü. Değişmeyen belgeye DOKUNMA; sayı gömme.
- Git: kendi çalışma branch'in, task başına commit, iş bitince main'e merge + push, doğrulayıp branch'i sil,
  oturumu main'de bırak.

Bir kalem belirsizse TAHMİN YÜRÜTME — bana sor. Bitince aşamamızı kaydet.
```

**Bitti kriteri:** Walkthrough'un her kalemi ya teste çevrildi ya artık listeye gerekçesiyle yazıldı ·
park listesi üç kovaya ayrıldı, gerçek kusurlar kapandı · `visual-check-residue.md` + `parked-items-triage.md`
var · suite yeşil · main'e merge + push.

**Senin işin:** yok — bu adım tamamen agent'ta. Çıktısı senin A14'te yürüyeceğin **kısa** listedir.

---

## A14 — Test-düzelt döngüsü (senin pasın · **tekrarlanır**) · Model: **Opus** · Effort: **high**

> **Buradan sonrası senin.** Uygulamayı kullanırsın; A13'ün ürettiği `visual-check-residue.md` listesini
> gezersin ve serbest kullanımda ne görürsen not alırsın. Sonra aşağıdaki promptu **her dalga için yeniden**
> yapıştırırsın — bulgularını içine yazarak.
>
> **Sen test YAZMIYORSUN.** Senin işin kusuru görmek ve tarif etmek; testi agent yazar. Kural değişmez:
> **hiçbir fix, kusuru yakalayan test kırmızı verdiği gösterilmeden yapılmaz.** 1430 test yeşilken
> animasyonların ölmesi bu kuralın neden gerektiğinin kanıtı.
>
> **İyi bulgu nasıl yazılır** (agent'ın soru sormasına gerek kalmasın):
> `hangi panel · ne yaptım · ne bekliyordum · ne gördüm · her seferinde mi / bir kez mi`
> Örnek: *"Projects listesi · Build başlattım · kart sarıya dönüp spinner dönmeli · kart beyaz kaldı,
> spinner yok · her seferinde."*
>
> **Dalga boyutu:** tek seferde 5-15 bulgu ideal. 40 bulguyu tek promptta verme — dalgayı böl, her dalga
> sonunda suite yeşil kalsın.

**PROMPT — yapıştır** (bulgularını `<<< >>>` bloğuna yaz):

```
Şu dosyaları oku:
1. .claude/handoffs/ altındaki EN YENİ handoff + işaret ettiği özet
2. .claude/outputs/ altındaki EN YENİ *-visual-check-residue.md (gezdiğim liste)
3. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (PLAN OF RECORD — "Global
   Constraints" + "A7 UI/UX" + "A13"; bir bulgunun kusur mu tasarım kararı mı olduğu tartışmalıysa v7 karar
   verir. v2/v3…v6 planları TARİHSELDİR, referans alma)
4. .claude/outputs/2026-07-15-19-00-design-v1/README.md (görsel otorite; değerler ve kopya metinleri BİREBİR)
5. .claude/outputs/2026-07-15-23-34-design-wpf-feasibility-analysis.md (A13.1 "algısal eşdeğer" / A13.2)
6. .superpowers/sdd/progress.md (ledger; çelişkide ledger kazanır)

DURUM: v7 kod planı + kapanış adımları (A11-A13) bitti; main == origin/main, suite yeşil.
Bu bir DÜZELTME DALGASIDIR — uygulamayı kullanırken gördüğüm kusurlar aşağıda.

BULGULARIM:
---
<<< BURAYA BULGULARINI YAZ. Her satır:
    hangi panel · ne yaptım · ne bekliyordum · ne gördüm · her seferinde mi / bir kez mi >>>
---

GÖREV: her bulguyu sırayla ele al.
- ÖNCE ŞİDDET SIRALA (bloklayıcı → önemli → kozmetik) ve bana sırayı göster, sonra başla.
- Bir bulgu belirsizse TAHMİN YÜRÜTME — bana ayırt edici soru sor (hangi durumda, her seferinde mi,
  pencere boyutuna bağlı mı, reduced-motion açık mı).
- Her bulgu için: kusuru yakalayan testi ÖNCE yaz, fix'ten ÖNCE KIRMIZI verdiğini GÖSTER, sonra düzelt,
  YEŞİL olduğunu göster. Kırmızıyı gösteremiyorsan testin yanlış — testi düzelt, kuralı esnetme.
- Kusur değil de tasarımdan kabul edilebilir bir sapmaysa: A13.1 "algısal eşdeğer" sınıfına GEREKÇESİYLE
  yaz, düzeltme. Gerekçesiz "eşdeğer" deme.
- Birden çok bulgu tek kök nedene bağlıysa tek fix'le kapat, ama HER BİRİ için ayrı test yaz.
- Teşhis gerektiren (nedeni belirsiz) bulgularda superpowers:systematic-debugging kullan; hipotezi
  doğrulamadan koda dokunma.

Kurallar:
- V7 BAĞLAYICI: yasaklar, teknik kararlar ve kabul ölçütleri planın kendisindedir — "Global Constraints" +
  "A7 UI/UX" + "A13". OKU ve UYGULA; bu prompt onları TEKRAR ETMEZ. Çelişkide v7 kazanır. Bir bulgunun
  "kusur mu, kabul edilmiş karar mı" olduğu tartışmalıysa cevap v7'dedir (A11 kapsam sınırları · A12
  varsayımlar · A13.1 algısal eşdeğer).
- Bulgu sayısı 5'ten fazlaysa: önce kısa TDD dökümü (.claude/outputs/YYYY-MM-DD-HH-mm-{baslik}.md), sonra
  superpowers:subagent-driven-development ile task-by-task. Azsa doğrudan TDD döngüsüyle ilerle.
- REALIZE TESTİ (v7'de YOK, sonradan öğrenildi): yeni XAML kökü/template için ZORUNLU (c6e9a21).
  Window.Measure/Arrange HWND'siz İÇERİĞE İNMEZ — realize window.Content üzerinde.
- Repo'daki token guard'larını (renk/motion/D8) çalıştır.
- Dalga sonunda TAM SÜİT yeşil olacak (acceptance dahil değilse belirt). Sayıyı raporla.
- DOKÜMAN SENKRONU: bu dalgadaki fix'ler CLAUDE.md / README.md / docs/TRUST-BOUNDARY.md içindeki bir
  OLGUSAL ifadeyi geçersiz kılıyorsa aynı dalgada düzelt (tetikleyiciler: mimari/akış değişikliği · yeni ya
  da kaldırılan komut/kısayol/script · TFM veya bağımlılık değişikliği · trust-boundary'yi ilgilendiren şey
  — IPC komutu, dosya yolu, process/job davranışı, git dokunuşu · README "Using it" / "Keyboard shortcuts" /
  "Performance modes" / "Known limits (v1)" bölümlerini yalanlayan davranış). Kılmıyorsa DOKUNMA; sayı gömme.
- Git: kendi çalışma branch'in, task başına commit, iş bitince main'e merge + push, doğrulayıp branch'i sil,
  oturumu main'de bırak.

Dalga bitince: hangi bulgu düzeltildi / hangisi algısal eşdeğere yazıldı / hangisi açık kaldı — tek tablo.
Bitince aşamamızı kaydet.
```

**Bitti kriteri (dalga başına):** Her bulgu ya düzeltildi (kırmızı→yeşil testiyle) ya A13.1'e gerekçesiyle
yazıldı · suite yeşil · main'e merge + push.

**Senin işin:** uygulamayı kullan, kusurları yukarıdaki formatta yaz, dalgayı başlat. Bulgu kalmayana kadar
tekrarla. Bu adımın "bitti"si yok — proje kapanana kadar döngü budur.

---

## A15 — Kapanış belge pası (CLAUDE.md · README.md · docs/) · Model: **Opus** · Effort: **low**

> **Ne zaman:** A14 dalgaları seyreldiğinde — yeni bulgu kalmadığında ya da kalanlar yalnız kozmetikken.
> **Tek seferlik**, en sonda.
>
> **Neden ayrı adım:** A12-A14'ün her birinde "DOKÜMAN SENKRONU" kuralı var — o kural drift'in *büyümesini*
> engeller, ama üç belgeyi baştan sona kimse bir kez okumaz. Bu adım onu yapar. A11'in yaşattığı dersin
> (bayat `CLAUDE.md` her session'da her agent'ı yanlış yönlendirdi) tekrarlanmaması için.
>
> **Kapsamdaki belgeler:** `CLAUDE.md` (84 satır) · `README.md` (240 satır) · `docs/TRUST-BOUNDARY.md`
> (418 satır). `.claude/outputs/` altındaki kayıt dosyaları **tarihsel belgedir, geriye dönük düzeltilmez** —
> onlara dokunma.

**PROMPT — yapıştır:**

```
Şu dosyaları oku:
1. CLAUDE.md · README.md · docs/TRUST-BOUNDARY.md (denetlenecek üç belge)
2. .claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md (PLAN OF RECORD — Global
   Constraints + PART C; belgelerin anlatması gereken şey budur. v2/v3/v4.x/v5/v6 TARİHSELDİR — belgelerde
   hâlâ onlara referans varsa v7'ye çevir)
3. .superpowers/sdd/progress.md (ledger — güncel durum; çelişkide ledger kazanır)
4. .claude/outputs/ altındaki A12/A13/A14 çıktıları: *-motion-regression-fix.md · *-parked-items-triage.md ·
   *-visual-check-residue.md + dalga raporları (bu adımlarda ne değişti)
5. .claude/handoffs/ altındaki EN YENİ handoff

DURUM: v7 kod planı + kapanış adımları (A11-A14) bitti. Bu SON adım: üç kalıcı belgeyi koda göre
senkronlamak.

GÖREV: her belgeyi KODLA karşılaştır, olgusal sapmaları düzelt.
1. CLAUDE.md — proje/mimari tablosu (proje adı · TFM · sorumluluk), mimari ilkeler, build/test/run
   komutları, plan referansı (v7 olmalı), dizin/isimlendirme kuralları hâlâ geçerli mi.
   BİLİNEN KALEM (A11'de bulundu, kapsam dışı bırakıldı): tablonun Core hücresi "incremental planlama
   (DiffAnalyzer/IncrementalPlanner)" diyor; kodda DiffAnalyzer diye bir tip YOK (Core/Incremental/
   IncrementalPlanner.cs var). Hücreyi koddaki gerçek tiplere göre düzelt.
2. README.md — What it does · Architecture · Requirements · Build,test,run · Publish · Using it ·
   Keyboard shortcuts · State on disk · Performance modes · Known limits (v1) · Design and decision records.
   "Known limits (v1)" A13/A14'te kapanan kalemleri hâlâ limit diye sayıyor mu? "Design and decision
   records" listesine A12/A13/A14'ün ürettiği kayıt dosyalarını EKLE.
3. docs/TRUST-BOUNDARY.md — process/IPC/dosya sistemi/git sınırları. A13'ün park-triyajında kapatılan
   kalemler yansıdı mı (ör. debugSpawnChildren üretimden kalktıysa §3 "Komut yönü ... hangi girdileri kabul
   ediyor"), A14 dalgalarında dosya yolu / IPC komutu / job davranışı değiştiyse ilgili bölüm.

YÖNTEM (bağlayıcı):
- Her iddiayı KODDA DOĞRULA ve düzeltmeyi dosya:satır KANITIYLA göster. Doğru olan ifadeye DOKUNMA.
- TEKRAR / OTORİTE DENETİMİ: bu üç belge v7'nin kurallarını ÖZETLER — bu DOĞRUDUR, README'yi v7'yi hiç
  okumamış birine yeter hâlde tutmak gerekir; belgeleri "bkz. v7" pointer'ına İNDİRGEME. Denetleyeceğin iki
  şey: (a) özet KODLA birebir mi, (b) belge kendini rakip bir doğru-kaynak gibi mi sunuyor. (b) için:
  CLAUDE.md'de kural kaynağının v7 olduğu EN AZ BİR KEZ geçsin (plan referansı + "otorite: v7 Global
  Constraints / A13"); README'de "Design and decision records" bölümü v7'yi plan of record olarak
  göstersin. Çelişki varsa v7 + kod kazanır, belge düzeltilir.
- Belgeleri yeniden tasarlama, üslup ve biçim korunur. Kozmetik düzenleme, yeniden yazım, bölüm ekleme yok —
  yalnız olgusal düzeltme + yukarıda istenen kayıt listesi güncellemesi.
- RAKAM GÖMME: "X test yeşil" gibi bir dalgada bayatlayacak sayı yazma; dayanıklı dil + ledger'a işaret.
- .claude/outputs/ ve .claude/summaries/ altındaki dosyalar TARİHSEL kayıttır — geriye dönük düzeltme YOK.
- Emin olamadığın bir ifade varsa TAHMİN YÜRÜTME, bana sor.

ÇIKTI: neyi neye çevirdiğinin dosya:satır tablosu (üç belge için ayrı ayrı) + "denetlendi, sapma yok"
dediğin bölümlerin listesi. Sonra commit + push. Bitince aşamamızı kaydet.
```

**Bitti kriteri:** Üç belge de kodla uyumlu (her düzeltme dosya:satır kanıtlı) · sapma bulunmayan bölümler
de raporlanmış (sessiz atlama yok) · `.claude/outputs/` tarihsel kayıtlarına dokunulmamış · main'e push.

**Senin işin:** yok. A14 dalgaları bitince bu promptu yapıştır — projenin kalıcı belgeleri son duruma gelir.
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
- **Model seçimi?** Plan artık **tamamı Opus** (Fable kaldırıldı). **A1-A10 bitti (2026-07-26), A11 bitti (2026-07-30).** Kalan aşamalar: **A12** (Opus/**high** — animasyon/renk regresyonu), **A13** (Opus/**high** — görsel borcun teste çevrilmesi + park listesi triyajı), **A14** (Opus/**high** — tekrarlanan test-düzelt dalgaları), **A15** (Opus/low — kapanış belge pası).
- **Kaç adım kaldı?** **4:** A12 → A13 → A14 (tekrarlanır) → A15. **Dördünün de yapıştırmaya hazır promptu** "KALAN ADIMLAR" bölümünde. Sıra bağlayıcı: A12 animasyonu diriltmeden UI gezilmez; A13'ün çıktısı (`visual-check-residue.md`) A14'ün girdisidir; A15 en sonda, bir kez.
- **CLAUDE.md / README / docs ne zaman güncelleniyor?** Üç noktada: **A11** (CLAUDE.md'nin bilinen 4 bayat kalemi — ✅ 2026-07-30'da kapandı) · **A12/A13/A14 içindeki "DOKÜMAN SENKRONU" kuralı** (yapılan değişiklik bir olgusal ifadeyi yalanlıyorsa aynı dalgada düzeltilir; yalanlamıyorsa dokunulmaz) · **A15** (üç belgenin baştan sona son denetimi). Belgelere her dalgada bayatlayacak **sayı gömülmez** (test sayısı gibi) — güncel rakam ledger'dadır.
- **Eski A12 (81 kalemlik gözle kontrol pası) ne oldu?** 2026-07-30'da kaldırıldı (kullanıcı kararı: *"playbook ile işim kalmasın"*). Kalemlerin pinlenebilir kısmı **A13'te teste çevriliyor**; yalnız gerçekten göz isteyenler kısa bir artık listede kullanıcıya kalıyor. Eski liste (`visual-check-walkthrough.md`) A13'ün girdisi olarak duruyor, silinmedi.
- **A12/A13'te neden high?** A12 kodlama değil **teşhis**: 1430 test yeşilken bozulan bir runtime davranışı aranıyor — `c6e9a21` ile aynı sınıf, headless suite'in görmediği yol. A13 ise 81 görsel kalemi tek tek "pinlenebilir mi" diye ayırıp gerçekten ayırt eden test yazmayı gerektiriyor; yüzeysel yapılırsa hep-yeşil testler üretir ve borcu gizler.
- **Testleri ben mi yazacağım?** Hayır. Sen kusuru görüp tarif ediyorsun (`panel · ne yaptım · ne bekliyordum · ne gördüm · her seferinde mi`), testi agent yazıyor — ve **her fix'ten önce testin KIRMIZI verdiğini göstermek zorunda.**
- **Tıkanırsam?** Model değiştirme kolu yok; effort'u yükselt (medium → high → xhigh, Kural 2). Effort modelin tavanını aşmaz — yalnız o tavanı sonuna kadar kullandırır. Review'u hiçbir koşulda atlama — riskli bölgelerin (UI custom render, Stop/copy-aware) tek sigortası o.
- **Commit ne zaman?** Promptlar commit'i sana bırakıyor (CLAUDE.md kuralı). Her aşama sonunda "commit et" demen yeterli.
