# Resource strip (design v1.25.0) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Title bar'ın tam ortasında, aracın makineye o anki maliyetini gösteren üç hücreli, yalnız okuyan şerit: CPU · RAM · paralellik + mod; About → Environment'taki üç bellek satırı görünürden kalkar (kopyalanan tanı metninde kalır).

**Architecture:** Veri App'te, App'in sahibi olduğu outer job'dan okunur (motora IPC yok): CPU = job accounting + App'in kendi işlemci zamanı; RAM = App + job'daki her sürecin private bytes'ı. Saf hesap/metin Core'da (`Core/Diagnostics/ResourceStripText`), Win32 okumaları Core'da (`NativeMethods` + `JobObject` + `MemoryLine`), örnekleyici App/Services'te (`ResourceSampler`, `IPollTimer` ile UI thread'de), görüntü VM özelliklerinde (`RunViewModel.ResourceStrip.cs`), şerit `Views/ResourceStrip.xaml`'de ve MainWindow title bar'ında absolute ortalı.

**Tech Stack:** .NET 10, WPF + WindowChrome, CommunityToolkit.Mvvm, xUnit (+ `[StaFact]`, `DsResources.Realize`, `MainWindowHost`, `PerfMeasure.AllocatedBy`).

**Spec:** `.claude/outputs/2026-10-17-10-44-resource-strip-and-resolve-priority-opus-prompt.md` (İş 2, binding) · `.claude/outputs/2026-10-15-16-54-design-v1.25.0/README.md` §2.1, §3.6, §4, §8 (kaynak şeridi maddesi), v1.25.0 notu · gerekçe `.claude/outputs/2026-10-17-10-06-resolve-priority-and-resource-strip-decisions.md` K3-K8 + §8. `prototype/app/BuildApp.jsx` (`ResourceStrip`, `ResCell`, `I.cpu`, `I.memory`, `I.parallel`) yalnız görsel referanstır.

## Global Constraints

- Branch: `feature/resource-strip` (`develop`'tan, İş 1 merge edildikten sonra). Ana proje checkout'u.
- Commit mesajı Türkçe/ASCII; **Claude attribution satırı YOK**; çok satırlı mesaj dosya + `git commit -F`.
- Kırmızı önce: her yeni davranışın testi önce KIRMIZI gösterilir (yeni tip için "derlenmiyor" kırmızısı yetmez — önce iskelet `NotImplementedException` ile derlenir, test iddiada düşer).
- Kopya YASAK: perf tablosu (`PerfProfile.For`), cap değerleri, mod adları, MB biçimi (`MemoryLine.Megabytes`), GB kuralı, tooltip metinleri, `—` tek yerde. Mod adı ↔ metin eşlemesi `PerfProfile.TryParse`'ın tablosundan; ikinci bir "Full/Balanced/Light" tablosu yazılmaz.
- Görünüm (README §2.1): hücre arası 14px; hücre = 12px Lucide ikon (`Brush.TextFaint`, stroke 1.8) + 5px + değer Geist Mono 11px (`FontSize.2xs`) tabular `Brush.TextDim`; paralellik hücresinde pay `TextDim`, `/N` `TextFaint` mono, mod sözcüğü Geist 11px `TextFaint` 5px sonra. Etiket yok. Hayalet genişlikler: `100%` · `12.3 GB` · `12/12` + `Balanced`. Değer ikonun yanında sola hizalı. Okunamayan değer `—`.
- İstenmeyenler: şeritte metin etiketi, CPU'ya minik bar, cap'in metinde görünmesi, değer değişiminde animasyon, `2/4` için uyarı rengi, mod sözcüğünün tıklanması, hover zemini, amber, açık tema.
- Örnekleme: ilk örnek etkinleşmeden 0,5 sn sonra, sonra 3 sn'de bir; pencere gizliyken (`HiddenSurface`) ya da küçültülmüşken timer DURUR; yeniden görünür olunca HEMEN bir örnek ve 3 sn ritmi. Motora IPC eklenmez. `Process.GetProcesses()` ve makine çapında süreç taraması YASAK. Isınma sonrası örnek başına sıfır bayt tahsis.
- CPU % = (ΔApp + ΔJob işlemci zamanı) / (Δduvar × `Environment.ProcessorCount`) × 100, en yakın tam sayı (yarımlar sıfırdan uzağa), [0,100] aralığına kırpılır.
- RAM = App + motor + derleyiciler private bytes (commit charge). Motor = `EngineHost.EnginePid`'in süreci; derleyiciler = job toplamı − motor. Motor uyurken motor ve derleyiciler 0.
- Bitiş: CHANGELOG ve `Version` dokunulmaz. Değişmeyenler: perf çipi, `WorkerBudget.Clamp`, koşu başında sabit paralellik, copy floor, motorun `memory:` satırının biçimi.
- Build/test `-c Release`; tam süit yalnız controller'da, merge öncesi.

## Rulings carried from the controller

- R1 (kullanıcıya soruldu; cevap gelene dek geçerli — **A**): private bytes'ın TEK okuma yolu `MemoryLine`'a eklenen `GetProcessMemoryInfo` tabanlı metottur; şerit App/motor/derleyiciler için onu kullanır; `MemoryLine.Read()` da `PrivateBytes`'ı aynı metottan alır (ölçüldü: `Process.PrivateMemorySize64` her çağrıda makinedeki tüm süreçlerin anlık görüntüsünü alır, ~3,4 ms + ~3,5 KB; `GetProcessMemoryInfo` 0,2 µs + 0 bayt ve aynı sayı). `memory:` satırının biçimi değişmez.
- R2: Koşu sürerken (`runStarted` → koşu sonu) ve tam koşuda hücre KOŞUYU anlatır; tooltip'i pay 0 iken de koşu metnidir (`0 of 4 projects building in parallel`). Boşta metni yalnız koşu yokken.
- R3: Tek proje koşusu `RunTargetId is not null` ile tanınır (`WorkersReducedNote`'un kapısı); payda, mod ve CPU tooltip cap'i çipten (canlı).
- R4: CPU tooltip'inin cap'i hücrenin moduyla aynıdır (koşuda koşu başındaki mod, boşta çip) — "koşu ortası değişim şeritte gösterilmez".
- R5: Pencere geri gelince alınan anlık örnekte CPU, son örnekten beri geçen sürenin (gizli kalınan süre dahil) ortalamasıdır; 3 sn sonra güncel pencereye döner. Dokümana bir cümle.
- R6: Şerit `IsHitTestVisibleInChrome=True` olduğu için caption davranışını kendisi iletir: sol basış `DragMove`, çift tık maximize/restore (mevcut `OnMaximizeRestore`), sağ tık sistem menüsü — title bar sürüklemesi şeridin üstünde de bozulmaz.
- R7: Gerçek uygulamada "gizliyken örnekleme durur" kanıtı için `BO_PROBE_STRIP` (dosya yolu) sondası: verilmişse her örnek bir satır yazar (`FrameGapProbe` deseni); verilmemişse hiçbir şey yapmaz.

## Review Focus

- Motor uyurken (job boş) → RAM yalnız App, tooltip `engine 0 MB · compilers 0 MB`, CPU yalnız App (sampler + VM testi).
- Job sorgusu Win32 hatası verir (ör. handle kapanmış) → CPU ve RAM hücreleri `—`, tooltip parçaları `—`; bir sonraki başarılı örnek düzelir (sampler testi, sahte probe).
- PID listesi ile `OpenProcess` arasında süreç çıkar → o süreç 0 sayılır, hücre `—` OLMAZ (probe testi: çıkmış PID).
- Pencere tepsideyken uzun bir build biter, sonra pencere açılır → anında tek örnek, ritim 3 sn; gizliyken hiç örnek yok (sampler testi, sahte timer + sayaç).
- 1240 genişlikte Update hapı görünürken şerit sol kilit ve sağ kümeyle çakışmaz, ortası pencere ortasında (realize testi).

---

### Task 1: Saf hesap ve metinler (Core)

**Files:**
- Create: `src/BuildOrchestrator.Core/Diagnostics/ResourceStripText.cs`
- Test: `tests/BuildOrchestrator.Tests/Diagnostics/ResourceStripTextTests.cs`
- Modify (gerekirse): `src/BuildOrchestrator.Core/ProcessControl/PerfProfile.cs` — metinden moda tek eşleme (`TryParseMode(string) → PerfMode?`; `TryParse` onu kullanır, ikinci tablo yok)

**Interfaces:**
- Produces (hepsi `public static`, saf, süreç okumaz):
  - `const string Unreadable = "—"`, `CpuGhost = "100%"`, `MemoryGhost = "12.3 GB"`, `ParallelGhost = "12/12"`, `ModeGhost = "Balanced"`
  - `int? CpuPercent(TimeSpan cpuTime, TimeSpan wallTime, int processorCount)` — wall ≤ 0 ya da processorCount ≤ 0 ⇒ `null`
  - `string Cpu(int? percent)` → `"69%"` · `"—"`
  - `string Memory(long? bytes)` → `"310 MB"` (MB kısmı `MemoryLine.Megabytes`) · `"4.8 GB"` · `"—"`; GB'ye geçiş yuvarlanmış MB ≥ 1024 iken
  - `string CpuTooltip(PerfMode mode)` → `"CPU used by Build Orchestrator — app, engine and compilers · cap 70%"` (cap `PerfProfile.For(mode).CpuCapPercent`; Full'de ` · cap …` eki yok)
  - `string MemoryTooltip(long? app, long? engine, long? compilers)` → `"Memory used by Build Orchestrator · app 310 MB · engine 85 MB · compilers 4.4 GB"` (her parça `Memory`)
  - `string ParallelAllowed(PerfMode mode)` → `"/4"`
  - `string ParallelTooltip(PerfMode mode, int building, bool running, string? reducedReason)` → boşta `"Balanced — up to 4 projects build in parallel"`; koşuda `"3 of 4 projects building in parallel"`; koşuda ve `reducedReason` doluyken `"2 of 4 — reduced: 3 GB free memory"`

- [ ] **Step 1:** İskeleti `throw new NotImplementedException()` gövdeleriyle yaz, derle.
- [ ] **Step 2: KIRMIZI testler** (`ResourceStripTextTests`, Theory'ler):
  - CPU: `(1 s, 1 s, 8) → 13` (12,5 → 13, yarım sıfırdan uzağa) · `(0, 3 s, 8) → 0` · `(30 s, 3 s, 8) → 100` (tavan) · `(-1 s, 3 s, 8) → 0` · `(1 s, 0, 8) → null` · `(1 s, 3 s, 0) → null`; `Cpu(null) == "—"`, `Cpu(69) == "69%"`.
  - Bellek: `310 MiB → "310 MB"` · `1023 MiB → "1023 MB"` · `1023 MiB + 512 KiB → "1.0 GB"` · `1024 MiB → "1.0 GB"` · `5_207_642_522 B → "4.9 GB"` · `0 → "0 MB"` · `null → "—"`; GB metni kültürden bağımsız (test `CultureInfo.CurrentCulture = tr-TR` altında da nokta görür).
  - Tooltip'ler: üç mod için CPU metni (Full'de cap eki yok); RAM metni (`null` parça `—`); paralellik boşta/koşuda/kısılmış, payda `PerfProfile.For(mode).Parallelism`'den, mod sözcüğü `mode.ToString()`, reason AYNEN geçer (`PerfNoteText.FreeMemoryLimit(3)` ile kurulan reason metni değişmeden görünür).
- [ ] **Step 3:** Run `dotnet test … -c Release --filter "FullyQualifiedName~ResourceStripTextTests"` → FAIL (NotImplemented). Çıktıyı rapora koy.
- [ ] **Step 4:** Gövdeleri yaz.
- [ ] **Step 5:** Aynı filtre → PASS.
- [ ] **Step 6:** Commit `feat(core): kaynak seridinin saf hesap ve metinleri (CPU yuzdesi, MB/GB, tooltip'ler, paralellik)`.

### Task 2: Win32 okumaları (Core) + bellek okuma kuralı + kaynak guard'ları

**Files:**
- Modify: `src/BuildOrchestrator.Core/ProcessControl/NativeMethods.cs` — `JobObjectBasicAccountingInformation = 1` + `JOBOBJECT_BASIC_ACCOUNTING_INFORMATION` + `QueryInformationJobObject` aşırı yüklemesi; `JobObjectBasicProcessIdList = 3` + bayt tamponlu aşırı yükleme; `OpenProcess`, `PROCESS_QUERY_LIMITED_INFORMATION = 0x1000`, `GetCurrentProcess`, `PROCESS_MEMORY_COUNTERS_EX`, `K32GetProcessMemoryInfo` (kernel32)
- Modify: `src/BuildOrchestrator.Core/ProcessControl/JobObject.cs` — `QueryAccounting()`, `QueryProcessIds(int[] destination)`
- Modify: `src/BuildOrchestrator.Core/Diagnostics/MemoryLine.cs` — R1: `long? PrivateBytesOf(int processId)`, `long? CurrentPrivateBytes()`; `Read()`'in `PrivateBytes`'ı `CurrentPrivateBytes()`'tan (okunamazsa `Read` bugünkü gibi fırlatır)
- Modify: `tests/BuildOrchestrator.Tests/Diagnostics/MemoryReadGuardTests.cs` — `GetProcessMemoryInfo` çağrısı da yalnız `MemoryLine.cs`'te (bildirim dosyası `NativeMethods.cs` muaf); `PrivateMemorySize64` artık hiçbir yerde yok
- Create: `tests/BuildOrchestrator.Tests/ProcessControl/JobResourceQueryTests.cs`, `tests/BuildOrchestrator.Tests/Diagnostics/NoProcessScanGuardTests.cs`

**Interfaces:**
- Produces:
  - `public readonly record struct JobAccounting(TimeSpan TotalCpuTime, int ActiveProcesses)`; `public JobAccounting JobObject.QueryAccounting()` — `TotalUserTime + TotalKernelTime` (ölmüş süreçler dahil); Win32 hatasında `Win32Exception` (`QueryCpuRate` deseni)
  - `public int JobObject.QueryProcessIds(int[] destination)` — yazılan PID sayısı (≤ `destination.Length`); iç bayt tamponu ilk çağrıda bir kez ayrılır ve kilit altında yeniden kullanılır; ısınma sonrası sıfır tahsis; Win32 hatasında `Win32Exception` (`ERROR_MORE_DATA` hata değildir: sığan kadar döner)
  - `public static long? MemoryLine.PrivateBytesOf(int processId)` — `OpenProcess(QUERY_LIMITED)` + `GetProcessMemoryInfo.PrivateUsage` + `CloseHandle`; açılamayan/okunamayan süreç ⇒ `null` (fırlatmaz); sıfır tahsis
  - `public static long? MemoryLine.CurrentPrivateBytes()` — sözde tanıtıcıyla aynı okuma

- [ ] **Step 1:** İskelet (NotImplemented) + derle.
- [ ] **Step 2: KIRMIZI testler** (`JobResourceQueryTests`):
  - `A_job_reports_its_member_and_keeps_its_cpu_time_after_it_exits`: gerçek job + mevcut test yardımcısıyla (JobCpuRateTests deseni) CPU yakan kısa bir çocuk süreç; `QueryProcessIds` çocuğun PID'ini içerir; çocuk çıktıktan sonra `QueryAccounting().TotalCpuTime` > 0 ve ≥ önceki okuma.
  - `An_empty_job_reports_no_processes_and_zero_cpu`.
  - `The_private_bytes_reader_reads_what_the_old_counter_read`: `MemoryLine.CurrentPrivateBytes()` ile eski kaynak `Process.GetCurrentProcess().PrivateMemorySize64` (test kodunda serbest; guard yalnız src'yi tarar) aynı ölçü — art arda üç okumada (yeni, eski, yeni) eskisi iki yeninin arasında ya da farkı ≤ 1 MiB.
  - `A_process_that_has_exited_reads_as_null`: çıkmış bir PID için `PrivateBytesOf` `null`.
  - `Job_queries_and_private_bytes_allocate_nothing_after_warm_up`: ısınma sonrası `PerfMeasure.AllocatedBy(() => { job.QueryAccounting(); job.QueryProcessIds(buf); MemoryLine.PrivateBytesOf(pid); MemoryLine.CurrentPrivateBytes(); }) == 0`.
  - Guard: `NoProcessScanGuardTests.No_production_code_enumerates_the_machines_processes` — `Process.GetProcesses(` ve `Process.GetProcessesByName(` App, Core, Supervisor, Contracts kaynaklarında yok (yorum satırları hariç, `SourceGuard.ScanSrc`) + guard'ın kendi kanıtı (sahte metinde yakalar, yorumu geçer — `MemoryReadGuardTests` deseni).
  - `MemoryReadGuardTests` yeni kurala göre: bellek okumasının dosyası yine tek (`MemoryLine.cs`), `GetProcessMemoryInfo` çağrısı da yalnız orada; doc'una eski iddia (PrivateMemorySize64) + ölçüm gerekçesi.
- [ ] **Step 3:** `--filter "FullyQualifiedName~JobResourceQueryTests|FullyQualifiedName~NoProcessScanGuardTests|FullyQualifiedName~MemoryReadGuardTests|FullyQualifiedName~MemoryLine"` → yeni testler FAIL.
- [ ] **Step 4:** Uygula.
- [ ] **Step 5:** Aynı filtre + `FullyQualifiedName~JobCpuRate` → PASS; motorun `memory:` satırı testleri (`MemoryLine` adlı sınıflar) yeşil.
- [ ] **Step 6:** Commit `feat(core): outer job accounting ve PID listesi, private bytes tek okuma yolu (GetProcessMemoryInfo); makine taramasi guard'i`.

### Task 3: Örnekleyici (App/Services) + VM özellikleri

**Files:**
- Create: `src/BuildOrchestrator.App/Services/ResourceSampler.cs` (`IResourceProbe`, `JobResourceProbe`, `ResourceReading`, `ResourceSample`, `ResourceSampler`, R7 sondası)
- Create: `src/BuildOrchestrator.App/ViewModels/RunViewModel.ResourceStrip.cs`
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` — `BeginRunAsync` komutu kurarken tam koşunun perf modunu yakalar; `OnRunStarted` koşu hücresini kurar; `OnIsRunningChanged(false)` bırakır; `OnPerfModeChanged`/`OnCountersChanged` hücreyi tazeler
- Test: `tests/BuildOrchestrator.Tests/App/ResourceSamplerTests.cs`, `tests/BuildOrchestrator.Tests/App/ResourceStripViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1 `ResourceStripText.*`; Task 2 `JobObject.QueryAccounting/QueryProcessIds`, `MemoryLine.PrivateBytesOf/CurrentPrivateBytes`.
- Produces:
  - `internal readonly record struct ResourceSample(int? CpuPercent, long? AppBytes, long? EngineBytes, long? CompilersBytes)`
  - `internal interface IResourceProbe { ResourceReading Read(); }` — `ResourceReading(TimeSpan AppCpu, long? AppBytes, TimeSpan? JobCpu, long? EngineBytes, long? CompilersBytes)`; job sorgusu fırlatırsa job alanları `null`
  - `internal sealed class JobResourceProbe(JobObject outerJob, Func<int?> enginePid) : IResourceProbe` — App CPU tek `Process.GetCurrentProcess()` nesnesinin `TotalProcessorTime`'ı (ölçüldü: 0 bayt); PID tamponu yeniden kullanılır
  - `internal sealed class ResourceSampler(IResourceProbe probe, IPollTimer timer, Func<TimeSpan> clock, int processorCount, Action<ResourceSample> publish)`: `static readonly TimeSpan FirstDelay = 0.5 s, Period = 3 s`; `void SetActive(bool active)`; `internal int SampleCount`; `internal ResourceSample SampleNow()`
  - `RunViewModel`: `StripCpuText`, `StripCpuTooltip`, `StripMemoryText`, `StripMemoryTooltip`, `StripBuildingText`, `StripAllowedText`, `StripModeText`, `StripParallelTooltip` (ObservableProperty, string); `void ApplyResourceSample(ResourceSample sample)`

- [ ] **Step 1:** İskelet + derle.
- [ ] **Step 2: KIRMIZI testler**
  - Sampler (sahte probe + `FakePollTimer` + elle saat): ilk `SetActive(true)` ⇒ timer aralığı 0,5 sn, tikte örnek yayınlanır, sonra aralık 3 sn; `SetActive(false)` ⇒ timer durur, tik gelse de örnek yok (`SampleCount` sabit); yeniden `SetActive(true)` ⇒ ANINDA bir örnek (`SampleCount` +1) ve aralık 3 sn; probe job alanlarını `null` verince yayınlanan örnekte `CpuPercent`, `EngineBytes`, `CompilersBytes` `null`; yüzde Task 1'in formülüyle (ör. App 0,5 sn + job 1 sn işlemci / 3 sn × 4 çekirdek ⇒ 13).
  - Gerçek probe: `JobResourceProbe` boş bir job ve `enginePid = () => null` ile → motor 0, derleyiciler 0, App > 0; `Sampler_allocates_nothing_per_sample_after_warm_up`: gerçek probe + `PerfMeasure.AllocatedBy(() => sampler.SampleNow()) == 0` (yayın no-op).
  - VM (`MainWindowHost`/mevcut VM fixture'ı, motor olayları VM'e oynatılarak): boşta `"0"`, `"/4"`, `"Balanced"`, tooltip boşta metni; çip Light ⇒ `"/2"`, `"Light"` ANINDA; Balanced'ta başlayan tam koşuda `runStarted` sonrası çip Full'e çevrilir ⇒ hücre `"/4"`, `"Balanced"` kalır, koşu bitince `"/6"`, `"Full"`; pay `Counters.Building` ile aynı (iki proje derlenirken `"2"`); `runStarted.Parallelism = 2`, `WorkersReducedReason = "3 GB free memory"` ⇒ tooltip `"<n> of 4 — reduced: 3 GB free memory"`; tek proje koşusunda (`RunTargetId` dolu) payda/mod çipten, koşu ortası çip değişimi hücreyi ANINDA değiştirir; motor uykudayken uygulanan örnek (App 300 MiB, motor/derleyiciler 0) ⇒ `StripMemoryText == "300 MB"`, tooltip `… · engine 0 MB · compilers 0 MB`; CPU tooltip'i Balanced'ta `cap 70%`, koşu Balanced'ta başlayıp çip Light'a dönse de `cap 70%`.
- [ ] **Step 3:** `--filter "FullyQualifiedName~ResourceSampler|FullyQualifiedName~ResourceStripViewModel"` → FAIL.
- [ ] **Step 4:** Uygula. Sampler'ın tik ve ilk-tik delegeleri bir kez yaratılır; örnek başına LINQ/kapanış yok. R7 sondası `BO_PROBE_STRIP` dosya yolu verilmişse her örnekte tek satır yazar (`HH:mm:ss.fff cpu=… ram=…`), yoksa hiçbir şey.
- [ ] **Step 5:** Aynı filtre + `FullyQualifiedName~RunViewModel|FullyQualifiedName~NoSleepPoll` → PASS.
- [ ] **Step 6:** Commit `feat(app): kaynak seridi ornekleyicisi (outer job, 0,5 sn ilk ornek, 3 sn ritim, gizliyken durur) ve VM ozellikleri`.

### Task 4: Şerit görünümü, title bar yerleşimi, ikonlar, token'lar, kablaj

**Files:**
- Create: `src/BuildOrchestrator.App/Views/ResourceStrip.xaml` + `.xaml.cs`
- Modify: `src/BuildOrchestrator.App/Resources/Icons.xaml` — `Icon.Cpu`, `Icon.MemoryStick`, `Icon.Parallel` (prototip path'leri, viewBox 24) + üçü için `*.StrokeThickness` = 1.8 (README §2.1; prototipin `parallel` 1.9'u README'ye göre 1.8)
- Modify: `src/BuildOrchestrator.App/Resources/Tokens.xaml` — şeridin ölçüleri (12px ikon, 14px hücre arası, 5px değer/mod aralığı) adlı token olarak
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml` — `TitleBarRow` içindeki `DockPanel` bir `Grid`'e sarılır; şerit aynı hücrede `HorizontalAlignment="Center"`, üstte (Z-order), `WindowChrome.IsHitTestVisibleInChrome="True"`
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml.cs` / `MainWindow.HiddenSurface.cs` — sampler'ı `EngineHost.OuterJob` + `EnginePid` ile kurar, `SetActive(görünür && !Minimized)`'ı `Loaded`, `SetSurfaceHidden` ve `StateChanged`'den çağırır; R6 caption iletimi
- Modify: `src/BuildOrchestrator.App/Services/EngineHost.cs` — `OuterJob` doc'u ("üretim job'a buradan dokunmaz" → şerit yalnız OKUR)
- Test: `tests/BuildOrchestrator.Tests/App/ResourceStripRealizeTests.cs`, `tests/BuildOrchestrator.Tests/App/ResourceStripGuardTests.cs`, ikon testleri mevcut ikon guard'ına göre

**Interfaces:**
- Consumes: Task 3 VM özellikleri ve `ResourceSampler`.
- Produces: `MainWindow.ResourceStripView` (x:Name) — realize testleri ve kablaj için.

- [ ] **Step 1:** İkon ve token anahtarlarını ekle, boş bir `ResourceStrip` kontrolünü title bar'a yerleştir, derle.
- [ ] **Step 2: KIRMIZI realize testleri** (`window.Content` üzerinde; `MainWindowHost.NewRealizedWithOffer` ya da genişlik verilebilen eşdeğeri, 1240 × 620):
  - şeridin üç hücresi var; her hücrede ikon `Path`'i `Icon.Cpu`/`Icon.MemoryStick`/`Icon.Parallel` ve değer metni VM'den;
  - şeridin yatay ortası pencere (title bar) ortasında (±0,5 px);
  - Update hapı görünürken şeridin sınırları `TitleBarLogoLock` ve sağ kümeyle kesişmez (ölç, varsayma);
  - sabit çerçeve: VM değerleri `1%`/`310 MB`/`0`·`/2`·`Light` iken ve `100%`/`12.3 GB`/`12`·`/12`·`Balanced` iken her hücrenin `ActualWidth`'i aynı, değer `HorizontalAlignment.Left`;
  - `WindowChrome.GetIsHitTestVisibleInChrome(ResourceStripView) == true`, `TitleBarLogoLock` hâlâ `false`;
  - üç hücrenin tooltip'i `AppTooltip.Side == Bottom`, metinleri VM tooltip'leri;
  - renkler `Brush.TextFaint`/`Brush.TextDim`, font `AppFonts.Mono` ve `FontSize.2xs` token'larından (çözülmüş değerler token'larla eşit).
  - Guard (`ResourceStripGuardTests`): `ResourceStrip.xaml`'de `#` renk literal'i ve sayısal ölçü (Width/Height/Margin/FontSize/Padding) literal'i yok; `ResourceStrip.xaml(.cs)`'te `Storyboard`/`BeginAnimation`/`DoubleAnimation`/`Transition` yok (mevcut motion guard desenine bağlan); kendi kanıtı.
- [ ] **Step 3:** Filtre `FullyQualifiedName~ResourceStrip` → FAIL.
- [ ] **Step 4:** Görünümü ve kablajı yaz (hayalet metinler `Visibility="Hidden"` ile alanı tutar; değerler `x:Static`'le Core'un ghost sabitlerinden; tooltip'ler `controls:AppTooltip.Side="Bottom"`; hover zemini yok).
- [ ] **Step 5:** `FullyQualifiedName~ResourceStrip|FullyQualifiedName~UpdatePill|FullyQualifiedName~MainWindowRealize|FullyQualifiedName~Icon|FullyQualifiedName~Token|FullyQualifiedName~AntiSlop|FullyQualifiedName~Motion` → PASS (UpdatePillTests'in bilinen DPI kararsızlığı ayrıca not edilir).
- [ ] **Step 6:** Commit `feat(app): title bar'in ortasinda kaynak seridi - CPU, RAM, paralellik + mod; ikonlar, token'lar, sabit cerceve, tooltip'ler asagi, surukleme seridin ustunde de calisir`.

### Task 5: About → Environment'taki üç bellek satırı yalnız kopyada

**Files:**
- Modify: `src/BuildOrchestrator.App/Services/DiagnosticsReport.cs` — `DiagnosticsSnapshot`'a yalnız-kopya grubu (`CopyOnly`); `App private` · `GC committed` · `Managed heap` oraya taşınır; `ToText` sırası bugünküyle aynı (Engine, Runtime, CopyOnly, Paths); etiketler tek yerde
- Modify: `src/BuildOrchestrator.App/Views/AboutDialog.xaml(.cs)` — yalnız `Runtime` çizilir; `MemoryLine.TryRead()` okuması kopya için sürer
- Test: `tests/BuildOrchestrator.Tests/App/DiagnosticsReportTests.cs`, `AboutDialogTests.cs` — yeni kurala göre yeniden yazılır (doc'a eski iddia + "şerit geldi" gerekçesi)

- [ ] **Step 1: KIRMIZI testler:** Environment sekmesinin çizdiği satırlarda `App private` yok; `ToText` çıktısında üç satır bugünkü sırada var.
- [ ] **Step 2:** Run `--filter "FullyQualifiedName~DiagnosticsReport|FullyQualifiedName~AboutDialog"` → FAIL.
- [ ] **Step 3:** Uygula.
- [ ] **Step 4:** Aynı filtre → PASS.
- [ ] **Step 5:** Commit `feat(app): About -> Environment'taki uc bellek satiri kalkti, kopyalanan tani metninde kaliyor`.

### Task 6: Dokümanlar

**Files:**
- Modify: `ARCHITECTURE.md` — §12.2/§13.2 title bar anlatımı + ASCII yerleşim satırı ("the brand alone" düzelir), §11.1 (şerit koşu başındaki kırpmayı gösterir; `workersReducedReason` tooltip'e de gider), §11.2 (App outer job'ı yalnız OKUR), yeni alt bölüm "Resource strip" (§11 içinde: veri kaynağı, formül, 0,5 sn / 3 sn, gizliyken durma, R5, private bytes ve Task Manager farkı, git'in derleyicilere düşmesi, uyuyan motor 0, `BO_PROBE_STRIP`), §11.4 (MemoryLine'ın private bytes'ı tek okuma yolundan aldığı), §13.3 About anlatımı, §14.4 (üç yeni ikon), §16 (yeni kalıcı durum yok), §17 (yeni pin ve guard'lar; ölçüm sondası listesi §17.5 ise oraya), §22 (kod haritası satırları; "three memory rows" satırı)
- Modify: `README.md` — arayüz tanıtımındaki title bar cümlesi + "Performance modes"a şeridin ne gösterdiği (kısa)

- [ ] **Step 1:** Bölümleri anlatı üslubunda yerinde yaz; her cümleyi koda karşı doğrula.
- [ ] **Step 2:** `grep -n -i "brand alone\|three memory rows\|App private" ARCHITECTURE.md README.md` → bayat ifade kalmadı.
- [ ] **Step 3:** Commit `docs: kaynak seridi - ARCHITECTURE 11 (yeni alt bolum), 11.1, 11.2, 11.4, 12.2, 13.2, 13.3, 14.4, 16, 17, 22; README title bar ve Performance modes`.

---

## Bitiş (controller)

1. Tam süit (Release, dosyaya). 2. Gerçek uygulamada gözle: Release build, `BO_PROBE_STRIP` ile — boşta `0/4 Balanced`, Build sırasında pay canlı, tepsiye alınca sondada satır kesilir, Update hapı varken şerit oynamaz (ekran görüntüsü). 3. `develop`'a merge + push, branch silme, CI.
