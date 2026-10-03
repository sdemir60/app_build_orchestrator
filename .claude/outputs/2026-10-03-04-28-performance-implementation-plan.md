# Performans iyileştirme — uygulama planı

> **Uygulayıcı için:** Bu plan `superpowers:subagent-driven-development` (önerilen) ya da
> `superpowers:executing-plans` ile **task task** uygulanır. Adımlar `- [ ]` kutularıyla izlenir.
> Ajan sayısı: aynı anda **en çok 3** (kullanıcının Max 5x limiti; bkz. "Çalışma düzeni").

**Tarih:** 2026-10-03 · **Taban:** `develop` @ `5d764a1` (kod `5f8e00d` ile aynı; aradaki commit'ler yalnız
`.claude/outputs/`) · **Kaynak analiz:** `.claude/outputs/2026-10-02-03-07-performance-deep-analysis.md`
(ölçümler, §3 gizli mod tasarımı, §13 yol haritası) ve bulgu kataloğu `…-findings-catalog.md` (D/N kimlikleri
oraya gider).

**Kardeş plan:** `.claude/outputs/2026-10-03-07-15-resolve-cycles-speedup-plan.md` (Resolve cycles hızlandırma:
yüzey özeti kör noktaları, WPF geçici assembly metadata-only, üye düzeyi artımlı tur 1). İki plan **tek
oturumda, şu sırayla** yürütülür: bu planın Faz 0 → kardeş planın Faz 1 ve Faz 2 → bu planın Faz A–E → kardeş
planın Faz 3 → (Faz 4 kullanıcı onayı bekler). Kardeş plan E1–E3'ü tekrarlamaz; bu planın **E4'ü atlanır**
(kardeş rapor B1 cevapladı: UI grubunu kirleten hareketlerin %40'ı tek üye) ve karar 14 kardeş planın Faz 3'üyle
çözülmüştür. Kardeş planın `RunCoordinator` satır numaraları bu planın E1/E2'sinden sonra kayar — metot adıyla
bulunur (`BuildCycleGroupAsync`, `CompileOneAsync`, `PersistBuildStateOnSuccess`, `RecordCycleOutcome`).

**Amaç:** Zayıf makinelerde (4 çekirdek / 16 GB; 2 çekirdek de hedefte) uygulama tepsideyken **yalnız derleme**
çalışsın; kısayoldan derlemeye geçiş 5,7 s'den ~2 s'ye insin; boşta ve koşu sonrasında makine rahatlasın;
motor her Sync/Build'de 12 MB yazmasın; Resolve'un süresi nereye gittiği loglansın ve ucuz kazançlar alınsın.
**Mevcut yapı bozulmaz:** shell-out MSBuild, nested job, OutDir'e dokunmama, planlama Core'da, git'e yazmama,
kopya yasağı — hepsi aynen kalır.

**Mimari (tek cümleyle):** Durumun tek kaynağı `RunViewModel`'dir; pencere gizliyken model birikmeye devam
eder, **görünümlere dokunulmaz**, pencere görününce görünümler modelden **tek seferde** yeniden kurulur. Motor
tarafında defterler yalnız kirliyse ve akışla yazılır; işçi sayısı Core'daki saf bir bütçeyle kırpılır.

**Teknoloji:** .NET 10, WPF (MVVM, CommunityToolkit), xUnit (+`[StaFact]`, `[SkippableFact]`), PowerShell 5.1
ölçüm script'leri (`.claude/temp/perf-2026-10-01/measure2/`, git'e girmez), `dotnet-trace`, `dotnet-gcdump`.

---

## 0. Küresel kısıtlar (CLAUDE.md'den; her task'ın örtük gereksinimi)

- **Kırmızı test kuralı:** hiçbir fix, kusuru yakalayan test KIRMIZI verdiği gösterilmeden yapılmaz.
- **Davranış değişince testi de değişir:** eski kuralı pinleyen test silinmez/gevşetilmez; yeni kuralı pinleyecek
  şekilde yeniden yazılır, doc'una eski iddia + değişme gerekçesi (ölçüm) yazılır. Eşik gevşetmek YASAK.
- **Realize testi:** yeni XAML kökü/şablonu ekleyen her değişiklik realize testi ekler.
- **Ölçüm testi = ortam değişkeni kapısı:** pencere açan / CPU yakan YENİ test `[SkippableFact]` +
  `Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_HIDDEN") == "1")` (bu plan tek değişken açar).
- **Kopya YASAK:** aynı değer/metin/primitif iki yerde tanımlanmaz (kod ve test).
- **Doküman aynı işte güncellenir;** anlatı üslubu ("şu oturumda şunu yaptık" YOK); rakam gömme yok.
- **Kod, UI metinleri, loglar İngilizce; yorumlar ve `.claude/` kayıtları Türkçe.**
- **Değişmezler:** in-process MSBuild yok; nested Job Object; OutDir/obj'e dokunulmaz; git'e araç kendiliğinden
  yazmaz; stdout yalnız NDJSON; planlama Core'da; Velopack yalnız App'te.
- **Attribution satırı HİÇBİR YERE eklenmez** (commit, PR, dosya).
- **Build/test:** `dotnet build BuildOrchestrator.slnx` · `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Uygulama açıkken build alınmaz (Supervisor binary'leri kilitler). Uygulama açıkken test gerekiyorsa `-c Release`.
- **Git:** iş branch'i `develop`'tan açılır; task başına commit; bitince `develop`'a merge + push; merge
  doğrulanınca branch silinir. `main`'e yalnız `/release` dokunur. Oturum `develop` üzerinde bitirilir.
- **Pano:** her task başında/sonunda `status.ps1 -Event steps` (global CLAUDE.md "İlerleme İzlenebilirliği").

## 1. Karar seti (sabit — bu plan bunları tartışmaz, uygular)

| # | Konu | Karar |
|---|---|---|
| 1 | Koşu logları | **3 gün** saklanır; **son koşu her zaman** kalır. |
| 2 | MSB3277 | Dokunulmuyor (ne araçta ne OSYS'te). |
| 3 | `-clp:Summary` özet bloğu | Dokunulmuyor. |
| 4 | İmleç | **Tek paylaşımlı saat**; pencere **aktif değilken** imleç sabit (kırpmaz). |
| 5 | Stop | **İkinci basış ya da ikinci Esc = hard stop** ("Stop now"). |
| 6 | Pencereye dönüşte Sync | Kalıyor; **ucuzlatılıyor** (motor tarafı: kirli bayrağı + akışla IO). |
| 7 | Kullanıcı istek kuyruğu | **Kaldırılıyor.** Herhangi bir Sync (sessiz dahil) / Clean / Optimize / checkout / pull sürerken koşu düğmeleri **pasif** (tıklanamaz), Sync düğmesi standart meşgul hâli (amber + spinner). Tepsideyken yok sayılan kısayol için **balon**. **Motorun kendi zincirleri (Clean→Sync, Optimize→Sync, pull→Sync, checkout→Sync) dokunulmaz.** Sessiz Sync'te liste/graf yeniden kurulmaz (yerinde uzlaştırma zaten var). |
| 8 | Proje logunda satır başına flush | Dokunulmuyor. |
| 9 | Harici kök güncellemesi | Her Build'de, dokunulmuyor. |
| 10 | İşçi sayısı | Çekirdek ve boş RAM'e göre **otomatik kırpma**; eşikler **ölçümle** (Faz D). |
| 11 | ETA dokümanı | Doküman koda göre düzeltilir (koşu içi ortalama). |
| 12 | Genişlik (`Width`) animasyonları | Dokunulmuyor; dokümana istisna yazılır. |
| 13 | Talep üzerine motor | Yalnız C1+C3 yetmezse (ölçüm) — bu planda yok. |
| 14 | Resolve'da yalnız değişen üye | Bu planda yok — **kardeş planın Faz 3'ü** uygular (bkz. başlık); E4 ölçümü atlanır. |
| 15 | Tepside derleme | Animasyon ve ekran işi yok; pencere gelince ekran tek seferde kurulur. |

**Dokunulmayacaklar (yanlışlıkla "iyileştirilmesin"):** `MotionGate`/reduced-motion sözleşmesi, tepsi
göstergesinin kendi zaman çizelgesi (`TrayBuildIndicator`), `UseSharedCompilation=false`, 32-bit MSBuild seçimi,
`DETACHED_PROCESS`, Server GC, `GCSettings`/`runtimeconfig` GC ayarları (ölçümde kazanç yok, concurrent kapalı
zararlı), per-project `build-state.json` yazımı (düşük öncelik), ready-set yerine bariyer (yapma), `-m`.

## 2. Çalışma düzeni

- **Branch'ler (hepsi `develop`'tan):** `perf/a-tray-hidden-mode` → `perf/b-interaction-decisions` →
  `perf/c-engine-io-memory` → `perf/d-worker-budget` → `perf/e-build-resolve`. Her faz bitince tam süit yeşil,
  `develop`'a merge + push, CI yeşil, sonra sıradaki faz. Faz F (kalan doküman) E ile birlikte gider.
- **Ölçüm, ajanla değil script'le yapılır** (API harcamaz, limit dolsa da koşar). Ölçüm çıktıları
  `.claude/temp/perf-2026-10-03/` altına (yeni klasör; taban ölçüm `…/perf-2026-10-01/` ile karşılaştırılır).
  Script'ler: `measure2/measure2.ps1` (boşta / tepside / ön planda rebuild, bellek, trace), `measure2/measure3.ps1`
  (süreler, J = tepsiden kısayol, 4/2 işlemci), `measure2/measure4.ps1` (kapılı testler), `measure2/run-all.ps1`.
  Koşul: uygulama kapalı, makine boş, `dotnet build BuildOrchestrator.slnx -c Release` önce. Script'ler salt
  ASCII yazılır (PS 5.1 BOM'suz dosyayı ANSI okur). Script'lerin `$script:OutDir` değişkeni yeni klasöre
  çevrilir (probe-helper.ps1'in başında).
- **Ajan bütçesi:** grup başına en çok 3 ajan; her ajana sert araç bütçesi (≤35 çağrı) ve "ilk bölüm biter bitmez
  sonucu dosyaya yaz"; geniş kod okuması ana oturumda; ajan yalnız bağımsız göz gereken dar denetime.
- **Süre:** uzun duvar saati sorun değil; ama sessiz kalınmaz, her task sonunda 2-3 satır ara durum.
- **Her task:** test (kırmızı) → en küçük implementasyon → yeşil → doküman → commit. Task sonunda süit filtreli
  koşulur (`Category!=Acceptance`); faz sonunda tam süit.
- **Olmayan şeyi uydurma:** bir satır numarası kaymışsa dosyadaki gerçek yeri bul; plandaki satırlar
  `develop @ 5f8e00d` içindir.

## 3. Review Focus (planın hiçbir testi doğrudan sınamıyor; ilgili task'a test eklendi)

1. **Koşu ortasında gizle → göster → gizle (A1/A2/A8):** her geçişte konsol tek seferde kurulmalı, satır
   çiftlenmemeli, nesil damgası uçuştaki batch'leri düşürmeli. Test A8'de.
2. **Tepsideyken Stop/Esc yolu (A6/B3):** tick gövdesi kapalıyken motor sessizlik bekçisi çalışmalı; tepsiden Exit
   + susmuş motor = uygulama yine kapanmalı. Test A6'da.
3. **Proje logu açıkken gizlenip koşu bitince dönüş (A2/C4):** proje belgesi tilt'siz yeniden kurulmalı;
   `_liveLines` bırakılmış olsa da dikiş kaybolmamalı (bekleyen yükleme sayacı). Test C4'te.
4. **Kuyruk kalkınca kısayol ve F5 (B1/B2):** Sync sürerken F5 hiçbir şey yapmaz (konsola satır düşmez, düğme
   Stop olmaz); tepsideyken kısayol balon gösterir, pencere görünürken göstermez. Test B1/B2'de.
5. **İşçi kırpması + ETA (D2):** `runStarted.Parallelism` fiili sayıyı taşımalı; şerit "N workers" ve ETA bu sayıyı
   okumalı; komuttaki `Parallelism` ile farkı konsola yazılmalı. Test D2'de.

## 4. Dosya haritası

| Dosya | Durum | Sorumluluk |
|---|---|---|
| `src/BuildOrchestrator.App/MainWindow.xaml.cs` | değişir | `IsSurfaceHidden` tek tanım; gizli kapılar (koreografi, final, konsol batch, graf itişi, tick gövdesi); dönüşte `ResyncAfterShow()`; kısayol balonu |
| `src/BuildOrchestrator.App/MainWindow.HiddenSurface.cs` | **yeni** (partial) | Gizli mod kapıları ve dönüş senkronu tek dosyada (MainWindow.xaml.cs zaten büyük) |
| `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` | değişir | `ReplaceRunDocument` / `ReplaceProjectDocument` (tilt'siz çekirdekler public) |
| `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs`, `BuildMenu.xaml.cs`, `EventStreamView.xaml.cs`, `ProjectRow.xaml.cs` | değişir | Gizliyken bildirim biriktir, görünürken tek geçiş (`RefreshAll` / `RefreshRows` / `RebuildRows` / `ApplyAll`) |
| `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` | değişir | Beads ve seçim kenarı akışı `IsVisible` kapısı |
| `src/BuildOrchestrator.App/ViewModels/GraphBinder.cs` | değişir | `TopologicalDepths` memo (topoloji referansı başına bir kez) |
| `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (+ `.Workspace.cs`, `.AutoSync.cs`, `.Esc.cs`, `.Exit.cs`) | değişir | Kuyruk kaldırma; `TickElapsed(bool surfaceVisible)`; ikinci Stop = Hard; `WhyRunCannotStart()`; `_liveLines` bırakma |
| `src/BuildOrchestrator.App/Views/ActionBar.xaml.cs` | değişir | Stop düğmesi üçüncü hâl: "Stop now" |
| `src/BuildOrchestrator.App/Shell/KeyboardShortcuts.cs` | değişir | `EscAction.AcknowledgeStopping` → `EscAction.StopNow` |
| `src/BuildOrchestrator.App/Shell/AppTrayIcon.cs` | değişir | `ShowBuildIgnored(string reason)` (`ITrayRunNotifier` + uygulaması) |
| `src/BuildOrchestrator.App/Controls/CursorClock.cs` | **yeni** | İki imlecin paylaştığı tek kırpma + tek renk turu saati; pencere aktif değilken sabit |
| `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs`, `Core/Incremental/SourceHashCache.cs` | değişir | Kirli bayrağı; `Stream` ile oku/yaz |
| `src/BuildOrchestrator.Core/Logs/RunLogRetention.cs` | **yeni** | Saf saklama kararı (3 gün + son koşu) ve budama |
| `src/BuildOrchestrator.Core/ProcessControl/WorkerBudget.cs` | **yeni** | `Clamp(requested, logicalCores, freeBytes)` saf kural |
| `src/BuildOrchestrator.Core/ProcessControl/MachineResources.cs` | **yeni** | Çekirdek sayısı ve boş fiziksel bellek okuma (Windows `GlobalMemoryStatusEx`) — tek okuma yeri |
| `src/BuildOrchestrator.Core/Io/IoParallelism.cs` | **yeni** | IO paralelliği sabiti (bugün beş yerde 16) |
| `src/BuildOrchestrator.Core/MsBuild/RestoreEvidence.cs` | **yeni** | `packages.config` özeti + paket klasörü kontrolü (saf) |
| `src/BuildOrchestrator.Contracts/Model/ProjectModels.cs` | değişir | `BuildState.PackagesConfigHash` (sona, default null) |
| `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` | değişir | İşçi kırpması + konsol satırı; koşullu restore; Resolve karar logu; hash paralel ve slot dışı |
| `src/BuildOrchestrator.Supervisor/Program.cs` | değişir | Açılışta arka planda log budama; Sync sonu bellek tanı satırı (stderr) |
| `tests/BuildOrchestrator.Tests/App/HiddenSurfaceTests.cs` | **yeni** | Gizli mod kalıcı testleri (A1–A8) |
| `tests/BuildOrchestrator.Tests/App/HiddenSurfaceMeasurementTests.cs` | **yeni** | `BO_MEASURE_HIDDEN` kapılı ölçüm |
| `tests/BuildOrchestrator.Tests/App/RunRequestWaitsForWorkTests.cs` | **yeniden yazılır** → `RunRequestDuringWorkTests.cs` | Yeni kural: iş sürerken basış kapalı, iş bitince açık |
| `tests/BuildOrchestrator.Tests/App/StopNowTests.cs`, `CursorClockTests.cs`, `TrayHotkeyBalloonTests.cs` | **yeni** | B3, B4, B2 |
| `tests/BuildOrchestrator.Tests/Core/…` | **yeni** | `EvaluationCacheDirtyFlagTests`, `SourceHashCacheDirtyFlagTests`, `RunLogRetentionTests`, `WorkerBudgetTests`, `RestoreEvidenceTests`, `CycleDecisionLogTests` |
| `ARCHITECTURE.md` §4.5, §8.4, §8.5, §9.3, §10.2, §11.1, §12.3, §13.2, §14.5, §16 · `README.md` | değişir | Her task kendi bölümünü yerinde yeniden yazar |

---

## Faz 0 — Hazırlık ve taban ölçümü

### Task 0.1: Branch ve taban ölçümü

**Files:** yok (ölçüm çıktıları `.claude/temp/perf-2026-10-03/baseline/`)

- [ ] `git switch develop && git pull --ff-only && git switch -c perf/0-baseline-and-guard` (Faz 0 kendi branch'inde biter ve develop'a merge edilir; `perf/a-tray-hidden-mode` Faz A başında develop'tan açılır)
- [ ] `probe-helper.ps1` başındaki `$script:OutDir`'i `D:\Projects\Other\Apps\app_build_orchestrator\.claude\temp\perf-2026-10-03\baseline` yap (script git'te değil; düzenleme serbest).
- [ ] `dotnet build BuildOrchestrator.slnx -c Release` (uygulama kapalı).
- [ ] `powershell -NoProfile -ExecutionPolicy Bypass -File .claude\temp\perf-2026-10-01\measure2\measure2.ps1` ve ardından `measure3.ps1` (yalnız J ve T1; T2/T3 Faz D'de). Beklenen taban (2026-10-02 ölçümüyle ±%15): tepside no-op rebuild UI thread ~1.385 Mcycles/s; tuş → `runStarted` ~5,7 s.
- [ ] Sonuç dosyalarını `baseline/` altında bırak; `PROGRESS.md`'ye (bu klasörde yeni) "taban alındı: <değerler>" yaz.

### Task 0.2: Test izolasyonu sondası (D9 bulgusu — gerçek durum klasörüne test artığı)

**Files:** `tests/BuildOrchestrator.Tests/Guards/StateFolderIsolationGuardTests.cs` (**yeni**)

- [ ] **Önce ölç:** `%LOCALAPPDATA%\BuildOrchestrator\logs` altındaki `run-*` klasör sayısını ve `evaluation-cache.json` içindeki `bo-vm-` geçen anahtar sayısını not et; tam süiti koştur; aynı sayıları yeniden al. Fark 0 ise bugünkü süit temizdir (kirlilik eski sürümlerden) — bu durumda yalnız aşağıdaki guard yazılır. Fark > 0 ise sınıf bazında ikiye böl (`--filter "FullyQualifiedName~App."` / `~Core.` / `~Supervisor.`) ve kaçağı bul; kaçak `SupervisorSandbox.IsolatedEngineHost` ile izole edilir.
- [ ] **Guard (kırmızı önce):** süit koşarken gerçek durum klasörüne yazılmadığını pinleyen test: süit başında `RunLogPaths.DefaultLogsRoot` altındaki `run-*` sayısını bir `AssemblyFixture`'ta okuyup süit sonunda karşılaştırmak xUnit'te kararsızdır; bunun yerine **kaynak guard'ı**: testlerde `new EngineHost(TestPaths.SupervisorExe)` ile kurulan bir host'ta `.StartAsync()` çağrısı yalnız `IsolatedEngineHost` dönüşünde olabilir — mevcut §17.2 guard'ını oku (`tests/…/Guards/`), kapsamadığı deseni (ör. `EngineHost` ctor'una `--logs` olmadan argüman geçen yardımcılar) ekle. Guard'ı önce bilerek kırılan bir fixture ile kırmızı gör, sonra geri al.
- [ ] Commit: `test(guards): durum klasörü izolasyon guard'ı genişletildi`.
- [ ] **Faz 0 kapanışı:** süit yeşil → `develop`'a `--no-ff` merge + push → branch sil. Sıradaki: kardeş planın Faz 1 ve Faz 2'si; onlar develop'a girince bu planın Faz A'sı (`git switch -c perf/a-tray-hidden-mode`).

---

## Faz A — Tepside derleme (gizli mod)

Ortak tanım: **yüzey gizli** sinyali TEK yerde yaşar — `Controls/HiddenSurface.cs` içindeki **kalıtsal attached
DP** `HiddenSurface.IsHidden` (`FrameworkPropertyMetadataOptions.Inherits`, varsayılan `false`). `MainWindow`
onu pencerenin kendisine yazar (`SetSurfaceHidden(bool)`), tüm torunlar miras alır; görünümler `HiddenSurface.GetIsHidden(this)`
okur ve değişimini `OnPropertyChanged(DependencyPropertyChangedEventArgs)` override'ında yakalar (GraphView'ün
`Visibility` bekletmesiyle aynı idiom). **Neden `IsVisible` değil:** headless süitte (`MainWindowHost` pencereyi hiç
`Show()` etmez — tepsi ikonu ve kısayol kaydı kurulamaz) bağlı olmayan ağaçta `IsVisible` her zaman `false`'tur;
`IsVisible`'a bağlanan bir kapı bütün mevcut kabuk testlerini "gizli" sanırdı. DP varsayılanı `false` olduğu için
mevcut testler değişmez; gizli-mod testleri `window.SetSurfaceHidden(true)` ya da tek görünümde
`HiddenSurface.SetIsHidden(view, true)` der — `OnGlobalHotkey`'in `internal` test yüzeyiyle aynı gerekçe
(`WM_HOTKEY` gösterilmeyen pencerede üretilemez).

Üretim kablajı: `IsVisibleChanged += (_, _) => SetSurfaceHidden(!IsVisible)` (ctor'da tek abonelik);
`StartInTray()` pencere hiç gösterilmediği için `SetSurfaceHidden(true)` çağırır. Simge durumunda küçültülmüş
pencere gizli SAYILMAZ (ayrı ölçüm, bu planda yok). **Sonsuz saatler (A7, imleçler) `IsVisible` kapısında kalır** —
§14.5 kuralı odur ve bu testler görünümü gerçek bir host penceresinde realize eder (`HiddenCursorClockTests`
deseni, `DsResources.Realize`).

Dönüşte tek seferlik kurulum `MainWindow.ResyncAfterShow()`'dadır ve `SetSurfaceHidden(false)` üzerine
`Dispatcher.InvokeAsync(ResyncAfterShow, DispatcherPriority.Loaded)` ile **ilk layout turundan sonra** koşar
(konsol pini layout okur). Test yardımcıları: `MainWindowHost.NewWithProjects` (realize + topoloji),
`MainWindowHost.AcceptSends`, `DispatcherPump.PumpFor/PumpUntil`, `CommandPress.Press`.

### Task A1: Gizli yüzey sinyali; koreografi ve final gizliyken atlanır

**Files:**
- Create: `src/BuildOrchestrator.App/Controls/HiddenSurface.cs`, `src/BuildOrchestrator.App/MainWindow.HiddenSurface.cs`
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml.cs:64-71` (alan başlatıcıları), `:316-326` (koreografi kapısı), `:938-942` (final), `:1341 (StartInTray)`
- Test: `tests/BuildOrchestrator.Tests/App/HiddenSurfaceTests.cs` (yeni), `tests/…/App/HiddenSurfacePropertyTests.cs` (DP'nin kalıtımı)

**Interfaces (sonraki task'lar bunu kullanır):**
```csharp
// Controls/HiddenSurface.cs — TEK tanım
internal static class HiddenSurface
{
    public static readonly DependencyProperty IsHiddenProperty = DependencyProperty.RegisterAttached(
        "IsHidden", typeof(bool), typeof(HiddenSurface),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetIsHidden(DependencyObject d) => (bool)d.GetValue(IsHiddenProperty);
    public static void SetIsHidden(DependencyObject d, bool value) => d.SetValue(IsHiddenProperty, value);
}
// MainWindow.HiddenSurface.cs
public partial class MainWindow
{
    internal bool IsSurfaceHidden => HiddenSurface.GetIsHidden(this);
    /// Üretimde IsVisibleChanged ve StartInTray çağırır; testler doğrudan çağırır (OnGlobalHotkey deseni).
    internal void SetSurfaceHidden(bool hidden);   // DP'yi yazar; gizlenince koreografi/final keser; görününce ResyncAfterShow kuyruklar
    /// Gizliyken biriken "ekran bayat" bayrakları; ResyncAfterShow sıfırlar.
    private bool _consoleStaleWhileHidden, _graphStaleWhileHidden, _listStaleWhileHidden, _tickStaleWhileHidden;
    internal void ResyncAfterShow();   // A2–A6 doldurur; A1'de yalnız koreografi/final için no-op
}
```

- [ ] **Kırmızı test 0 — DP kalıtımı:** `HiddenSurfacePropertyTests`: bir `Window` içinde iç içe üç `FrameworkElement`; pencereye `SetIsHidden(true)` → en içteki `GetIsHidden == true`; `false` → `false`; varsayılan `false`.
- [ ] **Kırmızı test 1 — koreografi gizliyken oynamaz, komut hemen gider:**
```csharp
[StaFact]
public void A_run_started_while_the_surface_is_hidden_sends_the_command_without_playing_the_choreography()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null), ("C", null));
    window.AnimationsForTest = true; // koreografi oynayabilsin: _choreographer'ın motion girdisi test seam'i (OperationChoreographer ctor'una verilen Func — MainWindow'a internal setter)
    IpcCommand? sent = null; vm.DebugSendOverride = cmd => { sent = cmd; return Task.CompletedTask; };
    window.SetSurfaceHidden(true);
    var sw = Stopwatch.StartNew();
    _ = vm.BuildCommand.ExecuteAsync(null);
    DispatcherPump.PumpUntil(() => sent is not null, TimeSpan.FromSeconds(2));
    Assert.IsType<StartRunCommand>(sent);
    Assert.True(sw.ElapsedMilliseconds < MarkingChoreography.NeutralMs, "komut koreografiyi beklemeden gitmeli");
    Assert.All(vm.ScopeFor(RunMode.Build), r => Assert.True(r.Marked)); // azaltılmış-hareket dalı: kapsam tek adımda işaretlenir
}
```
- [ ] **Kırmızı test 2 — final gizliyken oynamaz, filtre hemen döner:**
```csharp
[StaFact]
public void A_run_that_ends_while_the_surface_is_hidden_plays_no_end_finale()
{
    // NewWithProjects + AnimationsForTest; SetSurfaceHidden(true); runStarted → projectSucceeded ×3 → runCompleted (vm.OnEvent)
    Assert.False(window.Shell.GraphHost.IsEndFinalePlaying); // GraphView'e internal test yüzeyi: _endPlayer.IsPlaying
    Assert.False(window.Shell.GraphHost.IsFilterSuspended);
}
```
- [ ] **Kırmızı test 3 — koşu ortasında gizlenince oynayan koreografi kesilir ve komut gider** (`_choreographer.Cancel` + `CancelEndFinale`):
```csharp
[StaFact]
public void Hiding_the_surface_mid_choreography_releases_the_command_at_once()
{ // görünürken Build bas (koreografi oynuyor, sent == null), SetSurfaceHidden(true) → PumpUntil(sent != null, 500 ms)
}
```
- [ ] Testleri koş: `dotnet test … --filter "FullyQualifiedName~HiddenSurface"` → 3 KIRMIZI (bugün koreografi gizliyken de oynar; komut 2,4-3,5 s bekler).
- [ ] **Implementasyon:**
  - `_choreographer` ve `_stepHold` alan başlatıcılarını ctor'a taşı (lambda `this`'i yakalayamıyor):
    ```csharp
    _choreographer = new(() => AnimationsEnabledForChoreography() && !IsSurfaceHidden);
    _stepHold = new(() => AnimationsEnabledForChoreography() && !IsSurfaceHidden);
    // AnimationsEnabledForChoreography: üretimde MotionGate.StaticAnimationsEnabled; testte AnimationsForTest ile true'ya çekilebilir
    ```
    (`MotionGate.StaticAnimationsEnabled` zaten `App.Motion?.AnimationsEnabled ?? false`'ın tek ifadesi — ikinci kopya yazma.)
  - Final (MainWindow.xaml.cs:940-941): `if (IsSurfaceHidden) Shell.GraphHost.CancelEndFinale(); else Shell.GraphHost.PlayEndFinale(...)`.
  - `SetSurfaceHidden(true)`: `_choreographer.Cancel(_vm.Projects); Shell.GraphHost.CancelEndFinale();` — koreografi kesilince bekleyen komut `OperationChoreographer.Finish` ile serbest kalır (mevcut davranış). `SetSurfaceHidden(false)`: `Dispatcher.InvokeAsync(ResyncAfterShow, DispatcherPriority.Loaded)`. Aynı değere ikinci yazım no-op.
  - Kablaj: ctor'da `IsVisibleChanged += (_, _) => SetSurfaceHidden(!IsVisible);` (mevcut gösterge aboneliği `:1244` ile birleştirilmez — o `controller.SetMainWindowVisible` ister, bu DP yazar; ikisi aynı olayda ayrı satır). `StartInTray()`: `EnsureHandle()` sonrası `SetSurfaceHidden(true)`.
  - **Dikkat:** `Cancel` işaretleri korur (`ClearMarks` değil) — koşu başlayınca `BuildPreviewApplied` siler, mevcut sıra bozulmaz.
  - Tepsi göstergesinin iki okuması (`MainWindow.xaml.cs:1240, 1252`) HAM `MotionGate.StaticAnimationsEnabled`'da kalır — gösterge gizli modda DURMAZ.
- [ ] Testler yeşil; tam App süiti (`--filter "FullyQualifiedName~BuildOrchestrator.Tests.App"`) yeşil — özellikle `ChoreographyTests`, `ReducedMotionCoverageTests`, `SuccessFlourishTests`.
- [ ] **Doküman:** ARCHITECTURE §14.5 "The run command goes out when the choreography ends" paragrafına yerinde ekle: koreografiler yalnız görünür pencerede oynar; gizli pencerede (tepside başlatılan ya da koşu sırasında tepsiye inen) kapsam tek adımda işaretlenir ve komut hemen gider — azaltılmış-hareket ile aynı dal; "ya her zaman oynar ya hiç" kuralı *görünür* pencere içindir (ölçüm: tepsiden kısayolla derlemede 5,7 s'nin 3,5 s'i görülmeyen animasyondu). §12.3 "A build that runs while the window is away" paragrafına tek cümle: gizli pencerede hiçbir koreografi oynamaz.
- [ ] Commit: `feat(app): koreografi ve final gizli pencerede atlanır; komut hemen gider`.

### Task A2: Konsol batch kapısı ve dönüşte tilt'siz yeniden kurulum

**Files:**
- Modify: `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs:610-652, 672-690` · `src/BuildOrchestrator.App/MainWindow.xaml.cs:285-288, 598-606` · `MainWindow.HiddenSurface.cs`
- Test: `HiddenSurfaceTests.cs`

**Interfaces:**
```csharp
// ConsoleView
public void ReplaceRunDocument(string fullRunText);                 // = mevcut private ResetRunDocument (tilt YOK); ShowRunDocument bunu çağırıp PlayTiltIn ekler
public void ReplaceProjectDocument(IReadOnlyList<string> allLines); // PlayCascade'in tilt'siz çekirdeği; PlayCascade bunu çağırıp PlayTiltIn ekler
```

- [ ] **Kırmızı test — gizliyken batch belgeye basılmaz, dönüşte belge tam metinle tek seferde kurulur:**
```csharp
[StaFact]
public void Console_batches_are_not_applied_while_hidden_and_the_document_is_rebuilt_once_on_show()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
    var console = window.Shell.ConsoleViewControl;
    window.SetSurfaceHidden(true);
    int docChanges = 0; console.EditorControl.Document.Changed += (_, _) => docChanges++;
    for (int i = 0; i < 200; i++) vm.OnEvent(new ProjectLogEvent("r1", MainWindowHost.IdOf("A"), i + 1, $"line {i}"));
    // pompa hiç tick etmez (NeverTickingBatcher) — batch'i üretimdeki hedefe test verir (internal test yüzeyi):
    window.AppendConsoleBatch(string.Join("", Enumerable.Range(0, 200).Select(i => $"line {i}\n")), window.ConsoleReseedGen);
    Assert.Equal(0, docChanges);                     // KIRMIZI: bugün batch gizli belgeye basılır
    window.SetSurfaceHidden(false);
    DispatcherPump.PumpUntil(() => console.RunDocumentReplacedCount == 1, TimeSpan.FromSeconds(2)); // internal sayaç: ReplaceRunDocument çağrıları
    Assert.EndsWith("line 199", console.EditorControl.Document.Text.TrimEnd());
    Assert.Equal(1, console.RunDocumentReplacedCount);
}
```
- [ ] Kırmızı gör. **Implementasyon:**
  - `AppendConsoleBatch` (MainWindow.xaml.cs:598) `internal` olur (test yüzeyi; `ConsoleReseedGen => _console.CurrentReseedGen` ile birlikte); ilk satır `if (IsSurfaceHidden) { _consoleStaleWhileHidden = true; return; }`.
  - `ConsoleCleared` aboneliği (:285-288): gizliyken `ClearRunDocument` çağırma, `_consoleStaleWhileHidden = true`.
  - `ResyncAfterShow`: `if (_consoleStaleWhileHidden) { _consoleStaleWhileHidden = false; if (_vm.ActiveProjectId is null) _vm.SeedRunDocument(t => Shell.ConsoleViewControl.ReplaceRunDocument(t)); else _vm.SeedProjectDocument(_vm.ActiveProjectId, t => Shell.ConsoleViewControl.ReplaceProjectDocument(SplitLogLines(t))); if (_vm.GetActiveLineCount() == 0) Shell.ConsoleViewControl.ShowReady(); }` — `SeedRunDocument` reseed-drop sentinel'i yazar: uçuştaki bayat batch'ler düşer, **yeni tampon yolu açılmaz**.
  - `ConsoleView`: `ResetRunDocument` → `public void ReplaceRunDocument` (ad değişir, tek gövde); `PlayCascade`'den tilt'siz `ReplaceProjectDocument` çıkarılır; `PlayCascade = ReplaceProjectDocument + PlayTiltIn`.
- [ ] Yeşil; `ConsoleModesTests`, `ConsoleForwardWiringTests`, `HiddenCursorClockTests` yeşil.
- [ ] **Doküman:** ARCHITECTURE §13.5 (Console host) "mode switch" anlatısına tilt'siz kurulumun gizli pencere dönüşünde kullanıldığını yerinde ekle; §12.3'e: tepsideyken konsol belgesine yazılmaz, dönüşte modelin tam metninden bir kez kurulur.
- [ ] Commit: `feat(app): konsol batch'leri gizli pencerede belgeye basılmaz; dönüşte tek seferde kurulur`.

### Task A3: Olay akışı satırları ve daktilo gizliyken kurulmaz

**Files:** `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs:156-210` · Test: `HiddenSurfaceTests.cs`

- [ ] **Kırmızı test:** `HiddenSurface.SetIsHidden(view, true)` iken 50 `StreamEvents` eklenince `view.Rows.Count` artmaz ve `view.TypingRow` null kalır; `SetIsHidden(view, false)` sonrası `Rows.Count == 50` (son 150 kırpma kuralı içinde), hiçbir satır `IsTyping` değil (daktilo geriye dönük oynamaz). (`EventStreamTests`'in mevcut realize/VM fixture'ı ile.)
- [ ] **Implementasyon:** `OnStreamEventsChanged` başında `if (HiddenSurface.GetIsHidden(this)) { _staleWhileHidden = true; return; }`; `OnVmPropertyChanged`'da `StreamEventCount`/`ActiveLineGeneration` gizliyken yalnız bayrak; `OnPropertyChanged(e)` override'ında `e.Property == HiddenSurface.IsHiddenProperty && !(bool)e.NewValue && _staleWhileHidden` → `RebuildRows(); RefreshCounter(); UpdateActiveLine();`. `RebuildRows` satırları "yazılmış" hâliyle kurar (mevcut `CreateRow` + `FinishTyping`) — kontrol et, gerekiyorsa `RebuildRows` içinde `row.FinishTyping()`.
- [ ] Yeşil; `EventStreamTests*`, `EventStreamWiringTests`, `EventStreamTypingTests` yeşil.
- [ ] Commit: `feat(app): olay akışı gizli pencerede satır kurmaz; dönüşte tek geçişte kurulur`.

### Task A4: Graf itişleri gizliyken atlanır, dönüşte üç itiş

**Files:** `src/BuildOrchestrator.App/MainWindow.xaml.cs:832-840 (PushGraphStatuses), 847-848, 861-866` · `MainWindow.HiddenSurface.cs` · Test: `HiddenSurfaceTests.cs`

- [ ] **Kırmızı test:** gizli pencerede 177 proje × `projectStarted/Succeeded` olaylarından sonra `GraphHost.UpdateStatusesCallCount` (internal sayaç) artmamış; `Show()` sonrası tam olarak 1 `UpdateStatuses` + `RunPhase` doğru + seçim doğru.
- [ ] **Implementasyon:** `PushGraphStatuses`/`PushGraphRunPhase`/`PushGraphSelection` başına `if (IsSurfaceHidden) { _graphStaleWhileHidden = true; return; }`; `ResyncAfterShow`: `PushGraphRunPhase(); PushGraphStatuses(); PushGraphSelection();`. `RebuildGraph` (topoloji) gizliyken de koşabilir — GraphView kendi `Visibility` bekletmesine sahip; **dokunma**.
- [ ] Yeşil. Commit: `feat(app): graf itişleri gizli pencerede atlanır; dönüşte tek senkron`.

### Task A5: Şerit, Build menüsü, satırlar ve liste gizliyken dokunulmaz

**Files:** `Views/StickyRibbon.xaml.cs:184-241` · `Views/BuildMenu.xaml.cs:67-71, 98-106` · `Views/ProjectRow.xaml.cs:232-337` · `MainWindow.xaml.cs:700-760 (ApplyProjectGroups/RefreshVisibleRows)` · Test: `HiddenSurfaceTests.cs`

- [ ] **Kırmızı test (ortak; `MainWindowHost.NewWithProjects` + `window.SetSurfaceHidden(true)`):** gizliyken koşu olaylarından sonra `StickyRibbon.RebuildCount`, `BuildMenu.RefreshRowsCount`, bir `ProjectRow`'un `ApplyAllCount` (zaten var) ve `ApplyDurationCount` artmaz; `SetSurfaceHidden(false)` + pump sonrası şerit metni/progress/chip'ler modelle eşit (`RefreshAll` bir kez), satırlar doğru statüde (`ApplyAll` satır başına bir kez), liste grupları `ApplyProjectGroups(reveal: false)` ile kurulmuş (reveal oynamadı: `StickyLayerList`'in mevcut reveal test yüzeyi — `StickyRevealTriggerTests`'in kullandığı; yoksa ekle).
- [ ] **Implementasyon (desen her görünümde aynı — kopya değil, idiom):**
  ```csharp
  private bool _staleWhileHidden;
  // bildirim girişinde:
  if (HiddenSurface.GetIsHidden(this)) { _staleWhileHidden = true; return; }
  // DP değişimi (kalıtsal attached DP torunlara OnPropertyChanged ile gelir):
  protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
  {
      base.OnPropertyChanged(e);
      if (e.Property == HiddenSurface.IsHiddenProperty && !(bool)e.NewValue && _staleWhileHidden) { _staleWhileHidden = false; RefreshAll(); }
  }
  ```
  StickyRibbon: `OnVmPropertyChanged` + `OnProjectsChanged` → `RefreshAll()` (mevcut; `AnnouncePhaseIfChanged` de burada çağrılır). BuildMenu: `OnVmPropertyChanged` → `RefreshRows()`; ayrıca B5'teki "yalnız toplam değişince" kuralı burada da uygulanır (`_lastTotal`). ProjectRow: `OnVmPropertyChanged` → `ApplyAll()`; `Marked` dalgası gizliyken zaten oynamaz (A1). Liste: `MainWindow.RefreshVisibleRows`/`RefreshProjectGroups` gizliyken `_listStaleWhileHidden = true; return;` → `ResyncAfterShow`: `ApplyProjectGroups(reveal: false); RefreshVisibleRows();`. **Sessiz Sync topoloji getirirse** (`TopologyChanged` gizliyken) de aynı yol: dönüşte `ApplyProjectGroups(reveal:false)` + `RebuildGraph()`.
- [ ] Yeşil; `StickyRibbonTests`, `BuildMenuTests`, `ProjectRow*Tests`, `ProjectListFilterTests`, `StickyRevealTriggerTests` yeşil.
- [ ] Commit: `feat(app): şerit, menü, satır ve liste gizli pencerede bildirimi biriktirir; dönüşte tek geçiş`.

### Task A6: 200 ms tick gövdesi gizliyken yalnız bekçi

**Files:** `ViewModels/RunViewModel.cs:1850-1866` · `MainWindow.xaml.cs:352-364` · `RunViewModel.Exit.cs:75-88` (okunur, değişmez) · Test: `HiddenSurfaceTests.cs`

- [ ] **Kırmızı test 1:** gizli pencerede 3 s boyunca tick atarken `row.DurationMs` ve `ElapsedMs`/`EtaMs` değişmez (`PropertyChanged` sayacı 0).
- [ ] **Kırmızı test 2 (bekçi korunur — Review Focus 2):** gizli pencerede `ArmEngineWatchdog` sonrası `_nowMs` ileri alınınca `EngineOverdueMessage` dolar; tepsiden `RequestExit` + susmuş motor senaryosunda `ExitReady` yine gelir (`SafeExitTests` deseni, pencere gizli).
- [ ] **Implementasyon:** `public void TickElapsed(bool surfaceVisible = true)`: `surfaceVisible == false` ise yalnız `EvaluateEngineSilence()`; MainWindow tick: `_vm.TickElapsed(!IsSurfaceHidden); if (IsSurfaceHidden) { _tickStaleWhileHidden = true; return; }` ve satır sayacı/graf/frontier yalnız görünürken. `ResyncAfterShow`: `_vm.TickElapsed(true)` bir kez + `Shell.ConsoleHeaderControl.SetLineCount(_vm.GetActiveLineCount())`. **Timer durdurulmaz.**
- [ ] Yeşil; `EngineSilenceWatchdogTests`, `SafeExitTests` yeşil. Commit: `feat(app): tick gövdesi gizli pencerede yalnız motor bekçisini koşturur`.

### Task A7: Sonsuz saatler `IsVisible` kapısına alınır (doküman kuralıyla hizalama)

**Files:** `Views/ProjectRow.xaml.cs:621-633 (ApplyBreathing)` · `Views/StickyRibbon.xaml.cs:391-414 (şerit süpürmesi)` · `Graph/GraphView.xaml.cs:1084-1090 (ApplyBeads), 1150-1166, 1426-1439 (seçim kenarı akışı)` · Test: `HiddenSurfaceTests.cs` (HiddenCursorClockTests deseni)

- [ ] **Kırmızı test:** building bir satırın `BreathLayer.HasAnimatedProperties`, şeridin süpürme öğesi ve grafın beads/edge-flow saatleri (`GraphView` internal test yüzeyleri: `BeadsClockRunning`, `EdgeFlowClockRunning`) `window.Hide()` sonrası false; gizliyken yeni `projectStarted` olayı saat kurmaz; `Show()` sonrası geri gelir (`ReapplyMotion` / `ApplyBreathing` yolları).
- [ ] **Implementasyon:** her başlatıcının İÇİNDE `if (!IsVisible) { Stop…(); return; }` (kapı çağıranlarda değil — §14.5 gerekçesi); `IsVisibleChanged` ile yeniden değerlendirme. `MotionGate`'e sinyal EKLENMEZ (gösterge aynı kapıyı okusaydı dururdu).
- [ ] Yeşil; `ReducedMotionCoverageTests`, `MotionOwnerHygieneTests` yeşil.
- [ ] **Doküman:** §14.5 "An infinite animation must stop being visible before it stops running" paragrafı artık doğru söylüyor — DOKUNMA; yalnız §14.5'in başındaki kural listesine istisna satırı (karar 12): şerit ve ilerleme çubuğundaki `Width` animasyonları bilinçli layout animasyonlarıdır (kısa süreli, tek öğe; ölçülen bedeli yok), kural "sonsuz ve dekoratif" animasyonlar içindir.
- [ ] Commit: `feat(app): nefes, süpürme, beads ve seçim akışı görünürlüğe kapılı; Width istisnası dokümana`.

### Task A8: Kalıcı "layout geçişi yok" testi, ölçüm testi, yeniden ölçüm

**Files:** `HiddenSurfaceTests.cs` · `HiddenSurfaceMeasurementTests.cs` (yeni) · Test ölçümü `.claude/temp/perf-2026-10-03/after-a/`

- [ ] **Kalıcı test (kırmızı A1–A7'siz, yeşil hepsiyle) — headless pin: gizliyken hiçbir görünüm ölçümünü geçersizlemez:**
```csharp
[StaFact]
public void Run_events_and_console_batches_leave_the_realized_shell_measure_valid_while_the_surface_is_hidden()
{
    using var dir = new TempDir();
    var (window, vm, list) = MainWindowHost.NewWithProjects(dir, Enumerable.Range(0, 177).Select(i => ($"P{i}", (string?)null)).ToArray());
    var content = MainWindowHost.Realize(window);
    window.SetSurfaceHidden(true);
    Assert.True(content.IsMeasureValid && content.IsArrangeValid); // ön-koşul: realize edilmiş ağaç geçerli
    // runStarted, buildPreview, her proje için projectStarted + projectSucceeded, 300 projectLog satırı (vm.OnEvent),
    // 15 tick (vm.TickElapsed(false) — A6), bir konsol batch'i (window.AppendConsoleBatch — A2)
    Assert.True(content.IsMeasureValid && content.IsArrangeValid);            // KIRMIZI kapılarsız: şerit/satır metni ölçümü geçersizler
    Assert.True(window.Shell.Ribbon.IsMeasureValid); // ShellRoot'a `public StickyRibbon Ribbon => PART_Ribbon;` test yüzeyi (GraphHost/ConsoleViewControl deseni)
    Assert.Equal(0, window.Shell.ConsoleViewControl.RunDocumentReplacedCount);
}
```
  Headless ağaçta dispatcher layout turu koşmadığı için pin "ölçüm geçersizlenmedi"dir (`IsMeasureValid`); gerçek
  "layout geçişi yok" kanıtı aşağıdaki kapılı ölçüm testi ve script'tir. Review Focus 1 için ikinci test:
  `SetSurfaceHidden(true/false/true/false)` dizisinde konsol satır sayısı modelle eşit, çift satır yok
  (`Document.Text` satır sayısı == `GetActiveLineCount()`), `RunDocumentReplacedCount == 2`.
- [ ] **Ölçüm testi (kapılı):** `[SkippableFact] Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_HIDDEN") == "1")`; `StaThread.RunAsync` içinde `DsResources.Realize` ile gerçek host penceresinde `ShellRoot` + VM (tepsi ikonu kurulmaz), `window.Hide()` + `HiddenSurface.SetIsHidden(window, true)`, 177 projelik sentetik olay akışı 10 s; `QueryThreadCycleTime` ile UI thread döngüsü ve `LayoutUpdated` sayısı ölçülür ve test çıktısına yazılır (eşik YOK — sayı raporlanır; `measure4.ps1` deseni). ARCHITECTURE §17.5 listesine eklenir.
- [ ] **Yeniden ölçüm:** `measure2.ps1` (B, E, I blokları yeter) + `measure3.ps1` (J). Kabul: tuş → `runStarted` **≤ 2,5 s**; tepside no-op rebuild UI thread **≤ 300 Mcycles/s** (taban 1.385); 100 ms üstü kesinti 25 s'de **≤ 1** (taban 7); koşudan sonra tepside UI thread **≤ 40** (taban 129 — kaynak bulunamamıştı; kalırsa `measure2` I-bloğu trace'i `speedscope_analyze.py` ile atfedilir ve ayrı task açılır). Sonuçlar `PROGRESS.md`'ye yazılır.
- [ ] Kabul tutmuyorsa: `*.attrib.txt` üzerinden en büyük kalan kalem bulunur ve A1–A7'den ilgili task'a dönülür (yeni kapı eklenir); kabul tutmadan Faz B'ye geçilmez.
- [ ] **Doküman:** §12.3 tepsi paragrafına ölçülen sonuç dayanıklı dille ("tepside derleme sırasında arayüz thread'i boşta seviyesine yakın kalır") ; README "You do not have to keep the window open" bölümüne bir cümle: tepsideyken ekran işi yapılmaz, pencere gelince durum tek seferde kurulur.
- [ ] Faz kapanışı: tam süit (`Category!=Acceptance`) yeşil → `develop`'a merge + push → CI yeşil → branch sil → `PROGRESS.md`.

---

## Faz B — Etkileşim kararları (`perf/b-interaction-decisions`)

### Task B1: Kullanıcı istek kuyruğu kaldırılır (karar 7)

**Files:**
- Modify: `ViewModels/RunViewModel.cs:652, 675 (NotifyPropertyChangedFor IsRunUnderway), 871-876, 927-936, 1050-1132, 1233-1312, 1539-1550, 1973, 2589` · `RunViewModel.AutoSync.cs:139-145` · `RunViewModel.Workspace.cs:402, 454, 515, 637, 667, 887-912` · `RunViewModel.Exit.cs:90 (doc)` · `MainWindow.xaml.cs:362, 844-848, 932-937` · `Views/StickyRibbon.xaml.cs:203, 287-290`
- Test: `tests/…/App/RunRequestWaitsForWorkTests.cs` → **`RunRequestDuringWorkTests.cs`** (harness korunur: `DebugSendOverride`, `CommandPress`, sahte HEAD/izleyici, enjekte saat)
- Doc: ARCHITECTURE §13.2 (3209-3233), §12.3 (2374-2376), §14.5 (4942-4948 "queued is derived from a run that is live" — kuyruk cümleleri), README 564-565

**Yeni kural (pinlenecek):** `CanRequestRun() => !IsRunning && !IsStarting && !IsEngineUnavailable && !ExitPending && HasTopology && !WorkspaceBusy`. İş sürerken basış **yapılamaz**: komut `CanExecute=false`, konsola satır düşmez, düğme Stop olmaz, kısayol/F5 yok sayılır. İş bitince `NotifySyncGatedCommands` zaten düğmeleri yeniden sorgular. `IsRunUnderway` kaldırılır; dört tüketici `IsMidRunLocked`'a döner (eski hâl).

- [ ] **Testleri yeniden yaz (kırmızı önce):** dosya başı doc'una ESKİ iddia ("basış istektir, iş bitince başlar", kullanıcı bildirimi 2026-09-29) + değişme gerekçesi (kullanıcı kararı 2026-10-02: kuyruk yanlış strateji; Sync sürerken ekran Sync modunda, pasifler tıklanamaz; ölçülen kusur — pencereye dönüşün sessiz Sync'i Build'i yutuyordu — artık B2 balonu ve A-fazı kısa Sync ile karşılanır). Yeni testler:
  - `Every_run_command_is_not_executable_while_a_sync_clean_optimize_checkout_or_pull_is_in_flight(RunMode)` — `CanExecute == false`, `DebugSendOverride` hiç çağrılmaz, konsol metni değişmez, `IsStarting == false`.
  - `Run_commands_become_executable_again_when_the_work_ends` — syncCompleted/cleanCompleted(+zincir Sync)/pull/checkout sonrası `CanExecuteChanged` ateşlenir ve `CanExecute == true`.
  - `A_silent_sync_locks_the_run_buttons_and_shows_the_sync_button_busy` — `SyncBusy == true`, `BuildCommand.CanExecute == false`, faz `Syncing`'e GEÇMEZ (sessiz).
  - `F5_during_a_sync_does_nothing` — `KeyboardWiringTests` deseniyle.
  - `A_sync_request_requeries_the_controls_it_closes_at_once` (mevcut, kalır).
  - Kuyruk testleri (Stop takes back, engine loss drops request, late end leaves waiting build…) SİLİNİR — pinledikleri davranış artık yok; silme gerekçesi dosya doc'unda.
- [ ] Kırmızı gör (mevcut kod basışı kuyruğa alır → `IsStarting == true`).
- [ ] **Implementasyon:** `BeginRunAsync` → doğrudan `StartRunAsync` (WorkspaceBusy dalı gider); `QueuedRun`, `_queuedRun`, `SetQueuedRun`, `TakeBackQueuedRun`, `QueueRun`, `RunQueuedLine`, `StartQueuedRunWhenWorkEnds` ve çağrıları silinir; `CancelPendingRun` yalnız koreografi penceresi için kalır (faz nüansı `Phase == Starting` koşulu kalabilir); `IsRunUnderway` → `IsMidRunLocked` (MainWindow 362/848/932-937 case'i silinir; StickyRibbon 203 case'i `IsRunning/IsStarting` zaten var — kontrol et; 290: `(_vm?.IsMidRunLocked ?? false) || Phase == Syncing`); `Workspace.cs:902` koşulu `if (IsStarting) return false;`'a sadeleşir; yorumlardaki "[kullanıcı bildirimi 2026-09-29] istek" anlatıları silinir/yeniden yazılır (anlatı güncel kalır).
- [ ] Yeşil; tam App süiti yeşil (özellikle `RunViewModelTests`, `RunViewModelStateTests`, `EscStopTests`, `SafeExitTests`, `PullBeforeBuildTests`, `BranchCheckoutTests`, `CleanCommandTests`, `OptimizeCommandTests`).
- [ ] **Doküman:** §13.2 "Nothing starts while a Sync is in flight" paragrafı yerinde yeniden yazılır: iş sürerken koşu düğmeleri kapalıdır (Build/Rebuild/Cycles/Clean, satır komutları, F5, global kısayol); Sync düğmesi meşgul hâlini gösterir (sessiz Sync dahil); ekran o işin ekranıdır; iş bitince kapı açılır; tepsideyken yok sayılan kısayol balonla söylenir (B2). "Refusing the press instead would lose it…" gerekçesi silinir (yerine: kısa Sync ve balon). §14.5 4942-4948 "queued is derived from a run that is live" paragrafında "requested" ifadesi koreografi penceresine daraltılır. §12.3 2374-2376: "pressed while a Sync or a maintenance job runs it waits…" → "does nothing while a Sync or a maintenance job runs; from the tray a balloon says why (below)". README 564-565 aynı cümle.
- [ ] Commit: `feat(app): istek kuyruğu kaldırıldı — iş sürerken koşu komutları kapalı, Sync düğmesi meşgul`.

### Task B2: Tepsideyken yok sayılan kısayol için balon

**Files:** `Shell/AppTrayIcon.cs` (+`ShowBuildIgnored`) · `MainWindow.xaml.cs:1305-1317 (OnGlobalHotkey)` · `ViewModels/RunViewModel.cs` (+`WhyRunCannotStart()`) · Test: `tests/…/App/TrayHotkeyBalloonTests.cs` (yeni; `TrayMenuTests`/`TrayBuildIndicatorController` testlerinin headless deseni — gerçek `TaskbarIcon` kurulmaz, `ITrayRunNotifier`/sahte notifier)

**Interfaces:**
```csharp
// RunViewModel — SAF, test edilebilir
internal string? WhyRunCannotStart() =>
    IsEngineUnavailable ? "the engine is not available"
    : ExitPending       ? "the application is closing"
    : IsMidRunLocked    ? "a run is already in flight"
    : SyncBusy          ? "a Sync is in progress"
    : CleanBusy         ? "a Clean is in progress"
    : OptimizeBusy      ? "an Optimize is in progress"
    : CheckoutBusy      ? "a branch switch is in progress"
    : PullBusy          ? "a pull is in progress"
    : !HasTopology      ? "no project list yet — Sync first"
    : null;
// ITrayRunNotifier (+ AppTrayIcon uygular)
void ShowBuildIgnored(string reason); // Info ikonu; başlık AppIdentity.Product; gövde: "Build not started — " + reason + ". Try again when it finishes." (metin AppTrayIcon'da TEK yerde: BuildIgnoredBody(reason) internal static)
```
- [ ] **Kırmızı test:** `MainWindowHost.NewWithProjects` + `window.SetSurfaceHidden(true)` + `MainWindowHost.StartSync(vm, SyncMode.Silent)`; `window.OnGlobalHotkey(GlobalHotkeyAction.Build)` → sahte `ITrayRunNotifier` (pencereye `internal` test seam'iyle verilir — `OnSourceInitialized` koşmadığı için `_tray` null'dur; `TrayNotifierForTest` setter) `ShowBuildIgnored("a Sync is in progress")` 1 kez; pencere GÖRÜNÜRKEN (`SetSurfaceHidden(false)`) → 0 (ekran zaten söylüyor); `ShowNotifications` kapalıyken (`saved: new UiState { ShowNotifications = false }`) → 0 (her balon tek anahtara bağlı, §12.3); kapı açıkken → komut çalışır (`DebugSendOverride` görür), balon 0.
- [ ] **Implementasyon:** `OnGlobalHotkey`: `if (command is not null && command.CanExecute(null)) command.Execute(null); else if (action == GlobalHotkeyAction.Build && IsSurfaceHidden && _vm.WhyRunCannotStart() is { } reason && ShellSwitches.ShowNotifications(_uiState.Load())) TrayNotifier?.ShowBuildIgnored(reason);` — `TrayNotifier` = `_tray` ya da test seam'i.
- [ ] Yeşil. **Doküman:** §12.3 "Every balloon answers to one switch" → dört balon (yeni: yok sayılan kısayol); hotkey paragrafı (B1'de yazılan cümle). README 564-565.
- [ ] Commit: `feat(app): tepsideyken yok sayılan Build kısayolu balonla açıklanır`.

### Task B3: İkinci Stop / ikinci Esc = hard stop (karar 5)

**Files:** `ViewModels/RunViewModel.cs:1552-1581 (StopAsync, SendStopAsync, StopRequestedLine, CanStop)` · `RunViewModel.Esc.cs:16-38` · `Shell/KeyboardShortcuts.cs:15, 102` · `MainWindow.xaml.cs:566` · `Views/ActionBar.xaml.cs:580-602` · Test: `tests/…/App/StopNowTests.cs` (yeni) + `EscStopTests.cs` (yeniden yazılır: "Stopping'de Esc 'duyuldu' der" iddiası değişir)

**Yeni kural:** `Stopping` fazında ikinci Stop basışı ya da Esc → `StopRunCommand(runId, StopKind.Hard)`; konsola `"stop now requested — in-flight compiles will be terminated"`; düğme `Stopping…` yerine **etkin** ve `Stop now` okur; hard gönderildikten sonra düğme pasif (`Stopping…` metni geri gelmez; `Terminating…`). Motor tarafı hazır (`SupervisorHost.cs:364`, `RunCoordinator.cs:292-295`, `RunStoppedEvent.WasHard`).

- [ ] **Kırmızı test:**
```csharp
[Fact] public async Task A_second_stop_while_stopping_sends_a_hard_stop_once()
{ // DebugSendOverride ile iki gönderim yakalanır: Graceful, sonra Hard; üçüncü basış hiçbir şey göndermez
}
[Fact] public void Esc_while_stopping_resolves_to_StopNow() => Assert.Equal(EscAction.StopNow, KeyboardShortcuts.ResolveEsc(false,false,false,EscRunState.Stopping));
[StaFact] public void The_stop_button_reads_Stop_now_and_is_enabled_while_stopping_until_the_hard_stop_is_sent()
```
- [ ] **Implementasyon:** `private bool _hardStopRequested;` (koşu başında sıfırlanır — `StartRunAsync`; `internal bool HardStopRequested` test/ActionBar yüzeyi); `StopAsync`: `if (Phase == AppPhase.Stopping) { if (_hardStopRequested || _currentRunId is null) return; _hardStopRequested = true; AppendRunLine(StopNowRequestedLine); StopCommand.NotifyCanExecuteChanged(); await TrySendAsync(new StopRunCommand(_currentRunId, StopKind.Hard), "stop now"); return; }`; `CanStop() => (IsRunning || IsStarting) && !_hardStopRequested` (Stopping'de ikinci basışa AÇIK). Esc zinciri: `EscRunState` hesabı önce fazı sorar — `Phase == AppPhase.Stopping ? EscRunState.Stopping : StopCommand.CanExecute(null) ? Stoppable : …`; `KeyboardShortcuts.ResolveEsc`: `Stopping => EscAction.StopNow` (`AcknowledgeStopping` yeniden adlandırılır, doc'u yeni anlamı anlatır); `MainWindow`: `case EscAction.StopNow: _vm.StopCommand.Execute(null); break;` (hard zaten gittiyse `StopAsync` erken döner). `StopRequestAcknowledged` olayı ve şerit pulse'ı (StickyRibbon 173-182) **kaldırılır** — "duyuldu" vurgusunun yerini konsol satırı ve düğmenin "Terminating…" hâli alır; eski iddia + gerekçe `EscStopTests` doc'una. ActionBar: `Phase == Stopping ? (_vm.HardStopRequested ? "Terminating…" : "Stop now") : "Stop"`. `OnRunStopped` `WasHard` için konsol satırı zaten var mı kontrol et; yoksa `"stopped — N in-flight compiles terminated"` (sayı `Counters.Building`).
- [ ] Yeşil; `EscStopTests` yeniden yazılmış, `ActionBarTests`, `StickyRibbonTests` yeşil.
- [ ] **Doküman:** §4.5 "Hard stop … the App never sends it" → yerinde yeniden yaz: ilk Stop graceful; `Stopping` sürerken ikinci Stop/Esc hard gönderir (inner job terminate; uçuştakiler `failed("stopped")`, bir sonraki Build baştan derler); düğme hâlleri Stop → Stop now → Terminating…. README 369-373 Stop maddesi aynı dille. §13.9 (Keyboard) Esc zinciri son halkası.
- [ ] Commit: `feat(app): Stopping sırasında ikinci Stop/Esc hard stop gönderir ("Stop now")`.

### Task B4: İmleçler tek saat; pencere aktif değilken sabit (karar 4)

**Files:** Create `Controls/CursorClock.cs` · Modify `Console/ConsoleView.xaml.cs:568-593`, `Views/EventStreamView.xaml.cs:105-111, ~410-430 (StartCursorBlink/StopCursorBlink)`, `Controls/CursorHop.cs`, `MainWindow.xaml.cs` (Activated/Deactivated kablajı) · Test: `tests/…/App/CursorClockTests.cs` (yeni; `HiddenCursorClockTests` deseni)

**Tasarım:** `CursorClock` (App içinde tek örnek; `App.CursorClock`, testte enjekte) bir `AnimationClock` (kırpma, `MotionTokens.CreateBlinkAnimation().CreateClock()`) ve bir renk turu saati (`CursorHop.CreateAnimation(...).CreateClock()`) tutar; `Attach(Shape cursor, FrameworkElement host)` / `Detach(Shape cursor)`; saat yalnız ≥1 bağlı ve görünür imleç varken **ve** pencere aktifken koşar (`Controller.Begin/Stop`); `SetWindowActive(bool)` MainWindow'dan (`Activated`/`Deactivated`). İmleç bağlıyken `cursor.ApplyAnimationClock(OpacityProperty, clock)`; durunca `ApplyAnimationClock(OpacityProperty, null)` + `Opacity = 1` + dinlenme rengi (`CursorHop.Stop` deseni). Tüm kırpma/renk zaman çizelgeleri yine **MotionTokens**'ta kurulur (guard: renk keyframe tek kurucu).

- [ ] **Kırmızı testler:** (1) konsol ve akış imleçleri görünürken `cursor.Opacity`'yi süren saat AYNI `AnimationClock` örneği (`CursorClock.ActiveBlinkClock` test yüzeyi; iki görünümün `HasAnimatedProperties` true, `CursorClock.AttachedCount == 2`, tek saat); (2) `SetWindowActive(false)` → iki imleç sabit (`HasAnimatedProperties == false`, `Opacity == 1`), `SetWindowActive(true)` → geri; (3) `HiddenCursorClockTests` aynen yeşil kalır (gizli kapı başlatıcının içinde).
- [ ] **Implementasyon** yukarıdaki tasarım; `ConsoleView.StartBlink`/`StopBlink` ve `EventStreamView.StartCursorBlink`/`StopCursorBlink` artık `CursorClock.Attach/Detach` çağırır (idempotent). Pencere aktifliği: `MainWindow` ctor'da `Activated += (_, _) => App.CursorClock.SetWindowActive(true); Deactivated += (_, _) => App.CursorClock.SetWindowActive(false);` — headless'ta `App` yoksa görünümler kendi `Window.GetWindow(this)?.IsActive` değerini okumaz; sinyal yalnız `CursorClock` üzerinden gelir (varsayılan aktif).
- [ ] Yeşil; `CursorHopTests`, `ConsoleModesTests`, `EventStreamIdlePromptTests`, `ReducedMotionCoverageTests` yeşil.
- [ ] **Ölçüm:** `measure2.ps1` A bloğu (ön planda boşta): taban 181 Mcycles/s; hedef pencere aktifken ≤ 120, pencere arkadayken (başka pencere aktif) ≤ 50. Sayılar `PROGRESS.md`'ye.
- [ ] **Doküman:** §14.5 "Decorative infinite animations run at DesiredFrameRate=30…" paragrafına: iki imleç tek saati paylaşır; imleç yalnız pencere aktifken kırpar (Windows geleneği) — ölçüm gerekçesi (ön planda boşta tek çekirdeğin %4,4'ü dört saatten geliyordu). §13.5 konsol prompt'u cümlesi.
- [ ] Commit: `feat(app): iki imleç tek saatte; pencere aktif değilken imleç sabit`.

### Task B5: Ucuz UI işleri — Build menüsü, `TopologicalDepths`, şerit chip'leri, filtre gecikmesi

**Files:** `Views/BuildMenu.xaml.cs:67-71` · `ViewModels/GraphBinder.cs:31-54, 80-109` · `Views/StickyRibbon.xaml.cs:432-470 (RebuildChipsIfChanged)` · `Views/ShellRoot.xaml:69 (filtre kutusu Binding)` · Test: `BuildMenuTests.cs` (+1), `tests/…/App/GraphBinderTests.cs` (+1), `StickyRibbonTests.cs` (+1), `ProjectListFilterTests.cs` (+1)

- [ ] **Kırmızı testler:** (1) `Counters` toplam aynı kalırken 50 bildirim → `BuildMenu.RefreshRowsCount` 0 artar; toplam değişince 1. (2) Aynı topoloji referansıyla iki `GraphBinder.Nodes` çağrısı `TopologicalDepths`'i bir kez hesaplar (`GraphBinder.DepthComputations` internal sayaç); yeni referans → yeniden. (3) Derlenen küme aynı kalırken `RebuildChipsIfChanged` chip'leri yıkıp kurmaz (zaten imza var — doğrula; artımlı ekleme/çıkarma: `ChipsRebuiltCount` yalnız küme değişince). (4) Filtre kutusuna 5 tuş 100 ms içinde → `VisibleProjects` 1 kez yayınlanır (`Binding.Delay=150`).
- [ ] **Implementasyon:** BuildMenu `_lastTotal`; GraphBinder: `ConditionalWeakTable<IReadOnlyList<ProjectNode>, IReadOnlyDictionary<string,int>>` memo (`TopologicalDepths` dışarıdan çağrılanlar aynı memo'dan geçer); StickyRibbon: küme imzası değişince yalnız farkı ekle/çıkar (mevcut `_lastBuildingSig/_lastFailedSig` zaten tam yeniden kurmayı engelliyor mu — ölçümde 301 ms/25 s görüldü; artımlı yol gerekli); `ShellRoot.xaml:69` `UpdateSourceTrigger=PropertyChanged, Delay=150`.
- [ ] Yeşil. Doküman: §13.2 Build menüsü ve §13.7 filtre cümlesine tek satır (gecikmeli bağlama). Commit: `perf(app): Build menüsü yalnız toplam değişince; topolojik derinlik memo; chip artımlı; filtre gecikmesi`.

- [ ] **Faz B kapanışı:** tam süit yeşil → merge + push → CI → branch sil → `measure2.ps1` A/B/C blokları yeniden (ön plan rebuild UI thread 1.038 → hedef ≤ 800) → `PROGRESS.md`.

---

## Faz C — Motor IO ve bellek (`perf/c-engine-io-memory`)

### Task C1: Defterlerde kirli bayrağı + akışla okuma/yazma (karar 6'nın motor yarısı; §14 uyumsuzluk 4)

**Files:** `Core/Discovery/EvaluationCache.cs:42-62, 96-112, 122-130, 142-151` · `Core/Incremental/SourceHashCache.cs:66-87, 95-102, 148-166, 176-185, 195-208` · Test: `tests/…/Core/EvaluationCacheDirtyFlagTests.cs`, `SourceHashCacheDirtyFlagTests.cs` (yeni; mevcut cache testlerinin fixture'ı yeniden kullanılır)

- [ ] **Kırmızı testler:** (1) yükle → hiçbir giriş değişmeden `Flush()` → dosyanın `LastWriteTimeUtc` ve uzunluğu değişmez (bugün koşulsuz yazar). (2) bir giriş değişince `Flush()` yazar ve sonraki `Flush()` yazmaz. (3) `SourceHashCache`: racy pencerede (mtime < `RacyWindow`) kalan giriş yazılmadıysa bayrak **kirli kalır** ve pencere geçince sonraki `Flush()` yazar (saat enjekte: `UtcNow` seam'i zaten var). (4) kaynak guard'ı: `EvaluationCache.cs` ve `SourceHashCache.cs` içinde `ReadAllText`/`WriteAllText` geçmez (12 MB'lık UTF-16 ara string'in geri gelmemesi; `tests/…/Guards/` deseni).
- [ ] **Implementasyon:** `private bool _dirty;` — `GetOrEvaluate`/`HashOf`/`Seed`/`Prune` yazımlarında `_dirty = true` (SourceHashCache: `ConcurrentDictionary` → `Volatile`/`Interlocked` ile bayrak); `Flush()`: `if (!_dirty) return;` … yazım sonrası `EvaluationCache: _dirty = false`, `SourceHashCache: _dirty = excludedRacy > 0`. Okuma: `using var fs = File.OpenRead(path); JsonSerializer.Deserialize<Dictionary<string,Entry>>(fs, Json)`; yazma: `using (var fs = File.Create(tmp)) JsonSerializer.Serialize(fs, _entries, Json);` sonra `File.Move(tmp, path, overwrite: true)` (atomik yol aynen).
- [ ] Yeşil; Core + Supervisor süitleri yeşil (`BuildPlanBuilder`, `SyncWorkspaceService`, `OptimizeWorkspaceService` çağıranları DEĞİŞMEZ — kapı defterin içinde).
- [ ] **Ölçüm:** `measure3.ps1` S0–S6 aktivasyon serisi (pencereye 6 dönüş): taban Supervisor Private 243→309 MB bandı ve +36-91 MB/dönüş; hedef dönüş başına **+≤10 MB** ve Private ≤ 200 MB. App tarafı ~1,2 çekirdek-saniye/dönüş ölçülür (değişmez, not edilir).
- [ ] **Doküman:** §16 "When an operation finds nothing to change in a ledger…" cümlesi Sync ve koşuyu da kapsayacak biçimde yerinde genişletilir; §6.2 ve §7.1 defter cümlelerine "kirliyse yazılır" tek ifade; §10.2 Sync maliyeti anlatısı (varsa) sadeleşir.
- [ ] Commit: `perf(core): defterler yalnız kirliyse ve akışla yazılır`.

### Task C2: Koşu logu saklama — 3 gün + son koşu (karar 1); eski şemalı girişler kökten bağımsız budanır

**Files:** Create `Core/Logs/RunLogRetention.cs` · Modify `Supervisor/Program.cs:24-27` (açılışta arka plan) · `Core/Discovery/EvaluationCache.cs` (Optimize budaması: `Schema != CurrentSchema` girişler kökten bağımsız) · Test: `tests/…/Core/RunLogRetentionTests.cs` (yeni), `OptimizeWorkspaceServiceTests` (+1)

**Interfaces:**
```csharp
public static class RunLogRetention
{
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(3);   // TEK tanım; README/ARCHITECTURE sayıyı buradan anlatır
    /// Silinecek klasörler: adı RunDirName kalıbında, damgası now-KeepFor'dan eski, ve EN YENİ klasör hariç.
    public static IReadOnlyList<string> Select(IReadOnlyList<string> runDirectories, DateTimeOffset now);
    /// Seçilenleri siler; IO hatası yutulur ve sayı döner (stderr'e tek satır). Parça parça: en çok 200 klasör/çağrı.
    public static int Prune(string logsRoot, DateTimeOffset now, Action<string> log);
}
```
- [ ] **Kırmızı testler:** `Select`: 3 günden eski 5 klasör + 2 taze → 5 seçilir; TÜM klasörler eskiyse en yenisi kalır; kalıba uymayan ad hiç seçilmez; `Prune` gerçek temp klasörde siler ve sayar; 200 sınırı. Optimize: `bo-vm-…` köküne ait eski şemalı girişler kök dışı olsa da budanır (`PruneStaleSchema()`), güncel şemalı kök dışı giriş korunur.
- [ ] **Implementasyon:** `Program.Main`: `_ = Task.Run(() => RunLogRetention.Prune(logsRoot, DateTimeOffset.UtcNow, Console.Error.WriteLine));` motor hazır olmadan değil, host kurulduktan hemen sonra (açılış Sync'iyle yarışmaz: ayrı klasörler). Aktif koşunun klasörü hiçbir koşulda silinmez (`RunLogWriter.RunDirectory` damgası `now`'dan yeni). `OptimizeWorkspaceService` budamaya `PruneStaleSchema` ekler ve sayıyı raporlar.
- [ ] Yeşil. **Doküman:** §8.5 "Every run writes to …" → saklama cümlesi (üç gün, son koşu hep kalır, motor açılışında arka planda budanır); §10.2 "The run logs on disk are never deleted; clearing is for the screen only" → yerinde yeniden yaz; §16 tablo satırı; README logs bölümü (300. satır civarı "where the run's logs are"). `Optimize` anlatısı §16: eski şemalı girişler kökten bağımsız budanır.
- [ ] Commit: `feat(engine): koşu logları üç gün saklanır, son koşu kalır; Optimize eski şemalı girişleri budar`.

### Task C3: Motor belleği — önce tanı, sonra (gerekirse) rahatlama adımı

**Files:** `Supervisor/SupervisorHost.cs` (Sync bitişi) · `Supervisor/RunCoordinator.cs` (koşu bitişi) · Test: yok (stderr tanı satırı; guard: stdout'a yazılmadığı mevcut NDJSON guard'ıyla)

- [ ] **Tanı satırı (stderr):** Sync ve koşu bitişinde `memory: private=<MB> committed=<MB> heap=<MB>` (`Process.GetCurrentProcess().PrivateMemorySize64`, `GC.GetGCMemoryInfo().TotalCommittedBytes`, `GC.GetTotalMemory(false)`). App tarafı bu satırı zaten stderr drain'iyle loglar (`EngineStderrDrainTests` deseni; satır formatı Core'da tek yerde: `Core/Diagnostics/MemoryLine.cs`).
- [ ] **Ölçüm (C1 sonrası):** `measure3.ps1` S-serisi + `measure2.ps1` D bloğu. Karar kuralı: koşu/Sync sonrası `private − committed` ≥ 100 MB ise bellek yerel (GC dışı) → kaynağı trace ile ara (ayrı task); `committed` yüksek ve `heap` ≤ 10 MB ise `GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true)` motor boşa düşünce (komut döngüsü boşken, Sync/koşu bitiminden 2 s sonra tek seferlik) denenir; 3 koşuda tutarlı ≥ 50 MB geri veriyor ve bitişe ≤ 50 ms ekliyorsa kalır, aksi hâlde geri alınır. Sonuç `PROGRESS.md` + rapor.
- [ ] Kalırsa test: `[Fact]` boş motorda rahatlama adımının stdout'a hiçbir şey yazmadığı ve `engineReady`'yi geciktirmediği (zaman eşiksiz, sıra pinli).
- [ ] Doküman: §4.6/§11 altına kısa paragraf (yalnız kalan davranış). Commit: `perf(engine): bellek tanı satırı; boşta tek seferlik toplama` (ya da yalnız tanı).

### Task C4: App belleği — koşu tamponları ve gizliyken tek seferlik toplama

**Files:** `ViewModels/RunViewModel.cs:2694-2754, 2420-2435 (OnRunCompleted), LoadProjectLogAsync` · `MainWindow.HiddenSurface.cs` · Test: `tests/…/App/RunBufferReleaseTests.cs` (yeni)

- [ ] **Kırmızı testler:** (1) `runCompleted` sonrası `LiveLineCount` (internal test yüzeyi) 0; (2) **Review Focus 3:** `LoadProjectLogAsync` uçuştayken `runCompleted` gelir, yanıt sonra gelir → dikilmiş belge kuyruk satırlarını İÇERİR, bırakma yanıt döndükten sonra olur (`_pendingLogLoads` sayacı); (3) seçim başka projeye geçince eski projenin `_projectText`/`_projectLineCount` girişi düşer, geri seçilince diskten yeniden yüklenir (`GetProjectDocumentText(old) == ""`); (4) `ClearConsoleForNewOperation` davranışı aynen.
- [ ] **Implementasyon:** `OnRunCompleted` sonunda `ReleaseLiveLinesWhenIdle()` → `_pendingLogLoads == 0 ? _liveLines.Clear() : _releaseLiveLinesWhenLoadsEnd = true`; `LoadProjectLogAsync` try/finally sayacı; `OnSelectedProjectIdChanged`'de eski id'nin tamponu bırakılır (aktif id değilse). Gizli + koşu bitti + gösterge çıkış evresi bitti (`TrayBuildIndicatorController` bildirimi) → `GC.Collect(2, GCCollectionMode.Aggressive, blocking: false, compacting: true)` **bir kez** (`MainWindow.HiddenSurface.cs`, `_collectedAfterRun` bayrağı koşu başında sıfırlanır). Konsolun kendi `_backlogLines` kopyası `ReplaceRunDocument` ile zaten yenilenir.
- [ ] Yeşil; `ConsoleModesTests`, `ConsoleForwardWiringTests` yeşil. **Ölçüm:** `measure2.ps1` C/D/E (iki rebuild sonrası tepside): taban 433 MB; hedef **≤ 300 MB** (zorla GC tabanı 319).
- [ ] Doküman: §13.5 tampon anlatısına koşu sonu bırakma; §12.3'e gizli-koşu-bitti toplaması tek cümle. Commit: `perf(app): koşu tamponları koşu bitince bırakılır; tepside koşu sonu tek seferlik toplama`.

- [ ] **Faz C kapanışı:** tam süit → merge + push → CI → `measure3.ps1` S-serisi ve `measure2.ps1` tam → `PROGRESS.md`.

---

## Faz D — Zayıf donanım: işçi bütçesi (`perf/d-worker-budget`)

### Task D1: Ölçüm — 2 ve 4 işlemcide 1/2/3 işçi

**Files:** `.claude/temp/perf-2026-10-03/measure-workers.ps1` (yeni script; `measure3.ps1` T2/T3 ve `probe-helper.ps1 Start-App [affinityMask]` yeniden kullanılır)

- [ ] İşçi sayısını script'ten vermek için **geçici** bir kapı GEREKMEZ: profiller 2 (Light) / 4 (Balanced) / 6 (Full) işçi verir; 1 ve 3 için `Supervisor`'ı arayüzsüz, `StartRunCommand(Parallelism: n)` ile sür (`cycle-resolve-perf-harness` hafızasındaki harness: NDJSON stdin'den komut, `--logs` izole klasör). Her kombinasyon (yakınlık 2/4 CPU × işçi 1/2/3/4) × 2 tekrar gerçek Rebuild (OSYS, uygulama kapalı; Defender bekleme). Ölçü: duvar saati, tepe commit (`Get-Counter` yerine `GlobalMemoryStatusEx` P/Invoke, PS 5.1 ASCII), toplam CPU.
- [ ] Sonuç tablosu `PROGRESS.md`'ye; **eşik kararı** buradan çıkar (aşağıdaki varsayılan kural ölçümle düzeltilir).

### Task D2: `WorkerBudget` ve motorda uygulama (karar 10)

**Files:** Create `Core/ProcessControl/WorkerBudget.cs`, `Core/ProcessControl/MachineResources.cs`, `Core/Io/IoParallelism.cs` · Modify `Supervisor/RunCoordinator.cs:845-870` · `Core/Incremental/SourceHashCache.cs:132, 141` + diğer dört `16` (grep `WithDegreeOfParallelism(16)|MaxDegreeOfParallelism = 16`) · Test: `tests/…/Core/WorkerBudgetTests.cs` (yeni), `RunCoordinatorTests` (+1), `PerfProfileParityTests` (kontrol)

**Interfaces:**
```csharp
public readonly record struct WorkerBudgetDecision(int Workers, string? Reason); // Reason null = kırpılmadı
public static class WorkerBudget
{
    public const long BytesPerWorker = 2L * 1024 * 1024 * 1024;   // ölçülen ~2 GB commit/işçi (ARCHITECTURE §11.1)
    public const long ReserveBytes   = 2L * 1024 * 1024 * 1024;   // makineye bırakılan pay (D1 ölçümüyle düzeltilir)
    /// requested = profilin işçisi; cores = mantıksal işlemci; freeBytes = boş fiziksel bellek. Sonuç ≥ 1.
    public static WorkerBudgetDecision Clamp(int requested, int cores, long freeBytes)
    {
        int byCores = Math.Max(1, cores <= 2 ? 1 : cores - 1);           // VARSAYILAN HİPOTEZ — D1 düzeltir
        int byMemory = (int)Math.Max(1, (freeBytes - ReserveBytes) / BytesPerWorker);
        int workers = Math.Min(requested, Math.Min(byCores, byMemory));
        string? reason = workers == requested ? null
            : byCores < byMemory ? $"{cores} logical processors" : $"{freeBytes / (1024*1024*1024)} GB free memory";
        return new(workers, reason);
    }
}
public static class MachineResources { public static int LogicalCores { get; } public static long FreePhysicalBytes(); } // GlobalMemoryStatusEx, Core'da tek P/Invoke yeri (NativeMethods.cs)
public static class IoParallelism { public const int Degree = 16; }
```
- [ ] **Kırmızı testler:** `Clamp(4, 2, 16 GB) == 1`, `Clamp(4, 4, 16 GB) == 3`, `Clamp(4, 8, 32 GB) == 4` (kırpma yok, Reason null), `Clamp(4, 8, 5 GB) == 1` (bellek), `Clamp(6, 8, 7 GB) == 2`; sonuç hiç 0 olmaz. RunCoordinator: `cmd.Parallelism=4`, enjekte `cores=2` → `RunStartedEvent.Parallelism == 1` ve konsola `workers reduced to 1 (2 logical processors)`. `IoParallelism.Degree` guard'ı: `16` literal'i beş dosyada kalmaz (kaynak guard testi, `Category!=Acceptance`).
- [ ] **Implementasyon:** `RunCoordinator`'a `Func<(int Cores, long FreeBytes)>` enjekte (üretimde `MachineResources`, testte sahte); satır 845: `var budget = WorkerBudget.Clamp(Math.Max(1, cmd.Parallelism), cores, free); int parallelism = budget.Workers; if (budget.Reason is not null) { Decide(logs, …); console(PerfNoteText.WorkersReduced(parallelism, budget.Reason)); }` — metin Core'da tek yerde (perf notu sahibi `PerfNoteText`'in yanına). `runStarted.Parallelism` fiili sayıyı taşıdığı için ETA (`_runParallelism`) ve akış satırı ("N workers") kendiliğinden doğru.
- [ ] Yeşil. **Doküman:** §11.1 "three fixed profiles" → profil **istenen** işçi sayısıdır; motor koşu başında makineye göre kırpar (çekirdek ve boş bellek kuralı, tek sabit seti Core'da), runStarted fiili sayıyı taşır, konsol kırpmayı yazar; D1 ölçüm özeti dayanıklı dille. README perf profili paragrafı.
- [ ] Commit: `feat(engine): işçi sayısı çekirdek ve boş belleğe göre kırpılır; IO paralelliği tek sabitte`.

- [ ] **Faz D kapanışı:** tam süit → merge → CI → `measure3.ps1` T2/T3 (2 ve 4 işlemci) yeniden: taban 2 CPU tepside 51 s; hedef D1'in en iyi değeri.

---

## Faz E — Build ve Resolve (`perf/e-build-resolve`)

### Task E1: Resolve karar logu (D5-6, D5-1)

**Files:** `Supervisor/RunCoordinator.cs:1500-1533, 1557-1570, 1692-1740 (tur kararı)` · Test: `tests/…/Supervisor/CycleDecisionLogTests.cs` (yeni; mevcut `RunCoordinatorTests` cycle fixture'ı ile, `decision.log` okunur)

- [ ] **Kırmızı testler:** (1) bir üreticinin kanıt dosyası okunamazken Cycles koşusu → `decision.log` satırı `cycle <grup>: surface evidence unavailable — producer <ad>, file <yol>: <neden> — full rounds`; (2) her tur sonunda `cycle <grup> round <n>: <karar>; stale=<k> [adlar]; moved=<dosya> …; levels=<l>; round <ms> ms; hash <ms> ms` satırı; (3) tur 1 öncesi `cycle <grup>: <m> members, <p> producers, evidence <on|off>, hash <ms> ms`.
- [ ] **Implementasyon:** `SurfaceStateOf` okunamayan dosya ve nedeni `out` ile döner (`_apiSurface` null nedeni: dosya kilitli/bozuk/yol türetilemedi); `hashMode=false` dalı loglar; tur döngüsünde `Stopwatch` ile tur ve hash süreleri; metinler Core'da `CycleDecisionLines` (kopya yasak: konsol satırıyla aynı kaynak).
- [ ] Yeşil. Doküman: §8.8 Resolve anlatısına "decision.log ne yazar" tek paragraf. Commit: `feat(engine): Resolve turlarının kararı ve kanıt kaybı decision.log'a yazılır`.

### Task E2: Yüzey hash'i paralel ve derleme sonrası hash slot dışında (D5-5)

**Files:** `Supervisor/RunCoordinator.cs:1527-1533, 1636-1689` · Test: `RunCoordinatorTests` (+2)

- [ ] **Kırmızı testler:** (1) 8 üreticili sahte `_apiSurface` (her çağrı 50 ms uyur) ile grup başı hash süresi < 8×50 ms (paralel, `IoParallelism.Degree`); (2) derleme sonrası hash sırasında `run.InvokeSlots.CurrentCount` slotun BIRAKILMIŞ olduğunu gösterir (sahte invoker + 200 ms uyuyan `_apiSurface`; `CycleMemberHeldEvent` yine slot bırakılmadan önce yazılır).
- [ ] **Implementasyon:** başlangıç döngüsü `Parallel.ForEach(producers, new ParallelOptions{MaxDegreeOfParallelism = IoParallelism.Degree}, …)` + `ConcurrentDictionary` ya da kilit; `hashMode` kapanışı ilk hatada (`Interlocked`). `CompileOneAsync`: `outcome` try dışına; satır 1667-1681 bloğu `finally`'den (slot bırakıldıktan) SONRA koşar; `surfaceState` yazımı yine kilit altında; seviye sırası korunur (`CompileOneAsync` hash yazılmadan dönmez).
- [ ] Yeşil; cycle rounds süiti yeşil. **Ölçüm:** gerçek Resolve koşusu (OSYS, uygulama kapalı, harness) — taban grup başı hash 5,93 s, toplam 7,7 s; hedef ≤ 2 s / ≤ 3 s. Commit: `perf(engine): Resolve yüzey hash'i paralel; derleme sonrası hash slot dışında`.

### Task E3: Koşullu restore (D4-5, D5-9) — Build ve Cycles'ta; Rebuild her zaman restore eder

**Files:** Create `Core/MsBuild/RestoreEvidence.cs` · Modify `Contracts/Model/ProjectModels.cs:136-170 (BuildState + PackagesConfigHash = null)` · `Supervisor/RunCoordinator.cs:2036-2051 (NeedsRestore), 2159 (PersistBuildStateOnSuccess)` · Test: `tests/…/Core/RestoreEvidenceTests.cs` (yeni), `RunCoordinatorTests` (+2)

**Interfaces:**
```csharp
public static class RestoreEvidence
{
    /// packages.config içerik özeti (SHA-256 hex); dosya yoksa null.
    public static string? HashOf(string packagesConfigPath);
    /// Kayıtlı özet bugünküyle aynı VE packages.config'teki her <package id= version=> için
    /// <solutionDir>\packages\<id>.<version>\ klasörü varsa true. XML bozuksa false (restore koşar — güvenli taraf).
    public static bool IsSatisfied(string packagesConfigPath, string solutionDir, string? recordedHash);
}
```
- [ ] **Kırmızı testler:** `IsSatisfied` için dört durum (özet aynı+klasörler var → true; özet farklı → false; klasör eksik → false; XML bozuk → false). RunCoordinator: Build modunda özet kayıtlı ve paketler yerinde → komut satırında `-t:Restore`/restore prologu YOK ve `decision.log`: `restore skipped — packages.config unchanged, N packages present`; Rebuild modunda → restore KOŞAR; başarılı derlemede `BuildState.PackagesConfigHash` yazılır.
- [ ] **Implementasyon:** `NeedsRestore: !suppressRestore && target != Clean && HasPackagesConfig(id) && !(run.Mode != RunMode.Rebuild && RestoreEvidence.IsSatisfied(pc, solutionDir, state?.PackagesConfigHash))`; `PersistBuildStateOnSuccess` özeti yazar (yalnız restore koştuysa ya da zaten tatmin ediliyorsa).
- [ ] Yeşil. **Doküman:** §9.3 Restore: koşullu kural, Rebuild istisnası (toparlanma yolu), Optimize'ın eksik paket onarımıyla ilişkisi; §7.5 BuildState alanı; §16 tablo satırı. README "Rebuild — cache ignored" cümlesine restore notu.
- [ ] Commit: `perf(engine): paketleri değişmemiş projede restore atlanır (Rebuild hariç)`.

### Task E4: ATLANIR — kardeş planın raporu cevapladı (B1); aşağıdaki adımlar uygulanmaz

**Files:** `.claude/temp/perf-2026-10-03/cycle-members.py` (yeni; `logmine.py` yeniden kullanılır) · Çıktı: `.claude/outputs/<tarih>-resolve-member-change-stats.md`

- [ ] Son 30 gerçek Cycles koşusunun `decision.log` + `build-state.json` özetlerinden (BuiltContent) grup başına "içeriği değişen üye sayısı / üye sayısı / tur sayısı / süre" tablosu; E1 logları eklendikçe veri zenginleşir.
- [ ] Rapor yazılır; **tasarım yapılmaz**, kullanıcıya "üye düzeyi artımlılık kazandırır mı" sorusu bu rapor üzerinden sorulur (ayrı onay).

### Task F1–F2: Kalan doküman düzeltmeleri (E branch'inde)

- [ ] **F1 (karar 11):** ARCHITECTURE §8.4 "The per-project estimate comes from `BuildState.LastDurationMs`; with no history…" → koda göre: tahmin bu koşuda bitmiş projelerin (succeeded ∪ failed) süre ortalamasıdır; ilk proje bitene kadar şerit tahminsiz ilerleme ve geçen süre gösterir; `_runParallelism` runStarted'ın fiili işçi sayısıdır (D2). `BuildState.LastDurationMs` alanının yazılıp okunmadığı kontrol edilir; okunmuyorsa §7.5'te "kaydedilir, bugün yalnız tanı" denir.
- [ ] **F2 (karar 12):** A7'de yazıldı; kontrol.
- [ ] README: kısayollar tablosu (Stop now, balon), logs saklama, perf profili kırpması; §23 doküman haritası; §22 kod haritası yeni dosyalar (`MainWindow.HiddenSurface.cs`, `CursorClock.cs`, `RunLogRetention.cs`, `WorkerBudget.cs`, `MachineResources.cs`, `IoParallelism.cs`, `RestoreEvidence.cs`).
- [ ] Commit: `docs: ETA, kod haritası ve README performans kararlarıyla hizalandı`.

- [ ] **Faz E kapanışı:** tam süit (+ `Category=Acceptance` testleri bir kez) → merge + push → CI → `run-all.ps1` TAM yeniden ölçüm → sonuç raporu `.claude/outputs/<tarih>-performance-after-implementation.md` (önce/sonra tablosu, §5 kabul tablosuyla aynı satırlar). Sıradaki: kardeş planın Faz 3'ü (`perf/resolve-member-skip`); o da bitince oturum `develop`'ta biter.

---

## 5. Kabul tablosu (ölçümle kapanır; eşik gevşetilmez, tutmuyorsa task'a dönülür)

| Ölçü | Taban (2026-10-02) | Hedef | Script |
|---|---|---|---|
| Tepsiden kısayol → `runStarted` | 5,68 s | ≤ 2,5 s | measure3 J |
| Tepside no-op Rebuild, UI thread | 1.385 Mcycles/s | ≤ 300 | measure2 E |
| Tepside no-op Rebuild, App toplam | tek çekirdeğin %57'si | ≤ %25 | measure2 E |
| Göstergede 100 ms üstü kesinti / 25 s | 7 | ≤ 1 | measure2 E (busy slices) |
| Koşudan sonra tepside UI thread | 129 Mcycles/s | ≤ 40 | measure2 D |
| Ön planda boşta (pencere aktif / arkada) | 181 / 181 | ≤ 120 / ≤ 50 | measure2 A |
| Supervisor Private, 6 dönüş sonrası | 309 MB; +36-91/dönüş | ≤ 200 MB; +≤10 | measure3 S |
| App Private, iki rebuild sonra tepside | 433 MB | ≤ 300 MB | measure2 E |
| Sync/Build başına defter yazımı (değişiklik yok) | 12 MB | 0 | C1 testi + dosya mtime |
| Restore süresi payı (Build, paket değişmedi) | %7,1 | ~0 | logmine — E3 sonrası yapılan ≥10 Build koşusu (C2 saklaması eski logları siler; taban `perf-2026-10-01/logmine-stats.md`'de) |
| Resolve grup başı hash | 5,93 s | ≤ 2 s | harness |
| Log klasörü | 1,5 GB, 1.640 klasör | ≤ 3 gün | C2 |
| 2 işlemci, tepside Rebuild | 51 s (4 işçi) | D1'in en iyisi | measure3 T3 |

## 6. Bu planda bilerek OLMAYANLAR

MSB3277 (araç/OSYS), `-clp:NoSummary`, proje logu tamponlu yazım, harici kök güncelleme seyreltmesi/zaman aşımı,
talep üzerine motor, Resolve üye düzeyi artımlılık ve WPF geçici assembly (kardeş plan), Cycles koşusunda öncelik
(kardeş planın Faz 4'ü — kullanıcı onayı bekler), ready-set tur yerleşimi, `RenderCapability.Tier`,
`SHQueryUserNotificationState`, ReadyToRun, 32-bit MSBuild, `DETACHED_PROCESS`, per-project `build-state.json`
yazımı, simge durumundaki pencere kapısı (ölçülmedi), pencerenin kendi diyaloglarından dönüşün aktivasyon
sayılmaması (kural §12.3 aynen kalır).

---

## Ek — İki planı tek oturumda yürüten başlangıç prompt'u

```
Build Orchestrator'da (D:\Projects\Other\Apps\app_build_orchestrator, ana proje, branch develop) iki ONAYLI
uygulama planını tek oturumda, aşağıdaki sırayla uygula. Kararlar sabittir: yeniden tartışma açma, alternatif önerme.

OKU (başlamadan, baştan sona):
- CLAUDE.md (global + proje).
- PERF planı: `.claude/outputs/2026-10-03-04-28-performance-implementation-plan.md`; dayandığı analiz
  `.claude/outputs/2026-10-02-03-07-performance-deep-analysis.md` (§3 gizli mod, §13 yol haritası, §16 ölçüm).
- RESOLVE planı: `.claude/outputs/2026-10-03-07-15-resolve-cycles-speedup-plan.md`; dayandığı rapor
  `.claude/outputs/2026-10-02-22-15-cycle-resolve-performance-analysis.md` ve denetçi notları
  `.claude/temp/cycle-resolve-perf-2026-10-02/agents/review.md`. ARCHITECTURE.md §7.3, §8.8, §9.2'yi planın
  gösterdiği yerlerden oku.
- Hafıza notları: perf-measurement-toolkit, cycle-resolve-perf-harness, wide-workflow-exhausts-session-limit,
  running-app-locks-debug-bin-use-release, full-suite-output-to-file.

SIRA (her adım kendi branch'inde biter; bir sonrakine ancak develop'a merge + push + CI yeşil olunca geçilir):
 0. PERF planı dosyası henüz commit'li değil: `outputs/perf-implementation-plan` branch'inde commit'le, develop'a
    --no-ff merge + push, branch'i sil.
 1. PERF Faz 0 (`perf/0-baseline-and-guard`): taban ölçümü + izolasyon guard'ı.
 2. RESOLVE Faz 1 (`perf/surface-hash-completeness`): yüzey özeti kör noktaları.
 3. RESOLVE Faz 2 (`perf/wpf-temporary-assembly`): WPF geçici assembly metadata-only + kapanış ölçümü.
 4. PERF Faz A (`perf/a-tray-hidden-mode`): tepside derleme / gizli mod. Kabul ölçümü tutmadan Faz B'ye geçme.
 5. PERF Faz B (`perf/b-interaction-decisions`): kuyruk kaldırma, balon, Stop now, imleç, ucuz UI işleri.
 6. PERF Faz C (`perf/c-engine-io-memory`): defterler, log saklama, bellek.
 7. PERF Faz D (`perf/d-worker-budget`): işçi bütçesi (önce ölçüm).
 8. PERF Faz E (`perf/e-build-resolve`): E1, E2, E3 ve F1–F2. E4 ATLANIR.
 9. RESOLVE Faz 3 (`perf/resolve-member-skip`): üye düzeyi artımlı tur 1. PERF Faz E develop'a girmeden başlama.
10. DUR. RESOLVE Faz 4'e BAŞLAMA; karar 11 için benden onay iste. Kapanış raporunu yaz (aşağıda).

İKİ PLAN ARASI KURALLAR:
- Satır numaraları develop @ 5d764a1 içindir ve önceki adımlardan sonra kayar; yeri metot/alan adıyla bul, uydurma.
- RESOLVE Faz 2 `MsBuildArguments.Build`, `MsBuildInvokeRequest` ve `MsBuildToolset`'i genişletir; PERF E3
  (koşullu restore) aynı isteğin `NeedsRestore` alanını o hâliyle değiştirir.
- `BuildState`'e önce PERF E3 (`PackagesConfigHash`), sonra RESOLVE 3.2 (üç döngü alanı) eklenir; hepsi sona ve
  default'lu.
- RESOLVE Faz 3'ün decision.log satırları PERF E1'in açtığı tek metin sahibine (`CycleDecisionLines`) eklenir;
  PERF E2'nin `CompileOneAsync` düzeni (derleme sonrası hash slot bırakıldıktan sonra) korunur.
- İki planın "Bu planda bilerek olmayanlar" listeleri birlikte geçerlidir.

ÇALIŞMA DÜZENİ:
- superpowers:subagent-driven-development ile task task: kırmızı test → en küçük implementasyon → yeşil →
  doküman (aynı işte) → commit. Aynı anda en çok 3 ajan; her ajana sert araç bütçesi (≤35 çağrı), okunacak
  dosya + satır aralığı ve "ilk bölüm biter bitmez sonucu dosyaya yaz". Geniş kod okumasını ana oturumda yap.
- Faz sonu: tam süit (`Category!=Acceptance`; planın istediği yerde Acceptance bir kez) yeşil → develop'a --no-ff
  merge + push → CI yeşil → branch sil. Tam süit çıktısını dosyaya al. Eşik gevşetme, test silme yok: davranış
  değişiyorsa test yeni kuralı pinleyecek şekilde yeniden yazılır, eski iddia + gerekçe doc'una girer.
- Attribution satırı hiçbir yere yazılmaz. Sürüm numarasına ve CHANGELOG'a dokunma. main'e dokunma.
- `.claude/outputs/` tarihseldir: plan dosyalarındaki kutuları işaretleme. İlerleme kaydı
  `.claude/temp/perf-2026-10-03/PROGRESS.md` (faz, merge commit'i, ölçüm sonuçları, sıradaki task) ve pano
  (`status.ps1 -Event steps`, her task başında/sonunda; session_id context'te). Oturum kesilirse oradan devam edilir.
- Uygulama açıkken build alma; açıksa bana söyle (gerekirse `-c Release`).

ÖLÇÜM (ajanla değil script'le; uygulama KAPALI, makine boş, önce `dotnet build BuildOrchestrator.slnx -c Release`):
- PERF: `.claude/temp/perf-2026-10-01/measure2/` script'leri; çıktılar `.claude/temp/perf-2026-10-03/` altına.
  Kabul tablosu PERF planı §5. Restore payı ölçümü E3 sonrası yapılan ≥10 Build koşusundan alınır.
- RESOLVE: harness `.claude/temp/cycle-resolve-perf-2026-10-02/`, planın §5'i. Kullanıcının canlı OSYS kopyasında
  koşar: önce `snap.py save`, sonunda `snap.py restore` + `verify` (0 fark bekle); `Directory.Build.rsp` geçici
  konur ve KALDIRILIR. Kullanıcının reposunda Clean koşturma (harness'in kendi senaryoları dışında).
- Bir kabul hedefi tutmuyorsa eşiği değiştirme: trace/log ile en büyük kalan kalemi bul, ilgili task'a dön;
  çözemiyorsan sayılarla bana sor.

BANA SOR (koda dokunmadan dur): bir kararı etkileyen yeni ölçüm; doküman ile kodun uyuşmaması; planın bir
varsayımının kodda tutmaması; RESOLVE Faz 4. Küçük teknik sapmaları (metot adı, test yardımcısı) kendin seç ve
commit mesajında söyle.

RAPORLAMA: her task sonunda 2-3 satır ara durum; her faz sonunda "ne ölçüldü, hedef tuttu mu, ne açık kaldı" +
önce/sonra tablosu. Adım 10'da `.claude/outputs/<tarih>-performance-after-implementation.md` (iki planın kabul
tablolarıyla aynı satırlar, taban → sonuç) ve oturumu develop üzerinde bitir.

Adım 0 ile başla.
```
