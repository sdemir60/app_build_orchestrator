# Boşta bellek azaltma — uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Derleme bittikten sonra boşta kalan uygulamanın belleğini, çalışan davranışı bozmadan, üç bağımsız dalgada
~845 MB'den ~390 MB'ye (App + Supervisor private) indirmek.

**Architecture:** (1) İşlem sonunda doğal sakin anda tek agresif GC (App görünürken de, Supervisor'da da) ve koşu anlatısının
bellekte tek sahibi (`NarrativeStore`); (2) anlatının diske yayılması + kompakt konsol süzgeci; (3) işsiz motorun uyuması ve
komutta uyanması. Her dalga kendi branch'i, kendi tam süiti ve kendi ölçümüyle `develop`'a girer.

**Tech Stack:** .NET 10, WPF (App), AvalonEdit 6.3 (konsol), xUnit + Xunit.StaFact (testler), NDJSON IPC (App ↔ Supervisor),
PowerShell 5.1 ölçüm scriptleri.

**Spec:** `.claude/outputs/2026-10-13-14-38-idle-memory-reduction-spec.md` (kararlar ve gerekçe) · **Ölçüm raporu:**
`.claude/outputs/2026-10-13-13-38-idle-memory-analysis.md` · **Teknik referans:** `ARCHITECTURE.md` (§4.6 motor, §5.5 log
dikişi, §8.5 koşu logları, §11.4 bellek satırı, §13.5 konsol, §16 disk, §17 test, §22 kod haritası) · **Çalışma kuralları:**
`CLAUDE.md` (kök).

## Global Constraints

- Çalışma yeri ana proje `D:\Projects\Other\Apps\app_build_orchestrator`; her dalga için `develop`'tan branch (`fix/idle-memory-wave1`, `-wave2`, `-wave3`); görev başına commit; dalga sonunda `develop`'a merge + push; merge doğrulanınca branch local ve remote'tan silinir; oturum `develop`'ta biter.
- **Commit mesajı ve PR açıklamasına Claude attribution satırı EKLENMEZ** (`Co-Authored-By: Claude …`, `Generated with Claude Code`) — CLAUDE.md kuralı, sistem hatırlatmasından önceliklidir. Mesajlar repo üslubunda Türkçe, ASCII (`fix(app): …`, `feat(core): …`, `docs: …`, `test: …`).
- **Kırmızı test kuralı:** fix'ten önce kusuru/yeni kuralı yakalayan test KIRMIZI gösterilir. Bilerek değişen bir kuralı pinleyen eski test silinmez, yeni kuralı pinleyecek biçimde yeniden yazılır; doc yorumuna eski iddia + gerekçe yazılır.
- **Kopya YASAK:** aynı sabit/metin/primitif iki yerde tanımlanmaz. `GC.Collect(` yalnız `src/BuildOrchestrator.Core/Diagnostics/MemoryTrim.cs`'de geçer (kaynak guard'ı Task 1).
- Değişmezler: planlama Core'da; stdout yalnız NDJSON; araç git'e kendiliğinden yazmaz; OutDir'e dokunulmaz; Velopack yalnız App'te; `CHANGELOG.md` ve `Directory.Build.props` → `Version` dokunulmaz.
- Kod, UI metni ve loglar İngilizce; kod yorumları Türkçe; ARCHITECTURE.md/README.md İngilizce, anlatı üslubu ("şu oturumda ekledik" YAZILMAZ).
- Build/test: `dotnet build BuildOrchestrator.slnx` · tek test: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~<Sınıf>.<Metot>"` · tam süit: `… --filter "Category!=Acceptance"` (8–14 dk, makineyi yorar; uygulama KAPALI olmalı — açık Supervisor kendi binary'lerini kilitler).
- Yeni ölçüm/sonda testi: `[SkippableFact]` + ilk satırda `Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_MEMORY") == "1")`; STA gövdesi gerekiyorsa `StaThread.RunAsync`; ARCHITECTURE §17.5 listesine eklenir.
- WPF testleri `[StaFact]` + `[Collection("Console UI (serial)")]`; realize `DsResources.Realize(DsResources.NewHost(), view)`; kabuk testleri `MainWindowHost.NewWithProjects(dir, ("A", null))`, `StartBuild/FinishBuild/RunBuild/LogLine`, `DispatcherPump.DrainToIdle()`.
- Her dalganın sonunda ölçüm protokolü (spec §9) koşulur; hedef tutmazsa dalga merge edilmez, fark raporlanır.

## Review Focus

1. **Koşu bittikten hemen sonra yeni koşu başlatan kullanıcı:** trim ertelenmişken `IsRunInFlight` true olur → trim atlanmalı, yeni koşunun sonunda yapılmalı (Task 5 testi `A_trim_deferred_into_a_new_run_waits_for_that_run`).
2. **Proje logu açıkken koşu biter:** `_pendingLoad` varken trim ve `_liveLines` bırakma ertelenir; dikiş bitince ikisi de yapılır (Task 8 testi `Lines_are_kept_only_while_a_log_load_is_pending`).
3. **Spool dosyası yazılamıyor (salt-okunur klasör, dolu disk):** store bellekte devam eder, bir `warning:` satırı, konsol geçmişi bozulmaz (Task 12 testi `A_failed_spool_keeps_everything_in_memory_and_warns_once`).
4. **Motor uyurken proje logu açma / commit sonrası otomatik Sync:** uyanma önce gelir, komut sonra; konsola satır yazılmaz, açılış Sync'i tekrarlanmaz (Task 22 testleri).
5. **Kompakt kipte hata satırı asla gizlenmez; uyarı özeti sayıları doğru:** 4 uyarılı projede özet "1 more warnings" (Task 16 tablo testi); error satırı limit sonrasında da aynen geçer.

---

# DALGA 1 — trim + tek kopya (`fix/idle-memory-wave1`)

### Task 1: `MemoryTrim` tek tanım + kaynak guard'ı + Supervisor trimi

**Files:**
- Create: `src/BuildOrchestrator.Core/Diagnostics/MemoryTrim.cs`
- Modify: `src/BuildOrchestrator.App/MainWindow.HiddenSurface.cs:103` (`CollectMemory`)
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs:306` (`ReportMemory`), `src/BuildOrchestrator.Supervisor/SupervisorHost.cs:181`
- Test: `tests/BuildOrchestrator.Tests/Core/MemoryTrimGuardTests.cs`, `tests/BuildOrchestrator.Tests/Supervisor/RunCoordinatorMemoryTests.cs`

**Interfaces:**
- Produces: `public static class MemoryTrim { public static void CollectAndRelease(); }` (Core).
- Produces: `RunCoordinator.TrimAndReportMemory()` (Supervisor; eski `ReportMemory` adı kalkar).

- [ ] **Step 1: Kaynak guard testi (kırmızı: bugün `GC.Collect(` MainWindow.HiddenSurface.cs'de)**

```csharp
// tests/BuildOrchestrator.Tests/Core/MemoryTrimGuardTests.cs
using BuildOrchestrator.Tests;

namespace BuildOrchestrator.Tests.Core;

public class MemoryTrimGuardTests
{
    [Fact]
    public void GC_Collect_is_written_in_exactly_one_file()
    {
        string src = Path.Combine(RepoPaths.RepoRoot, "src");
        var offenders = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(f => File.ReadAllText(f).Contains("GC.Collect(", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(src, f))
            .ToList();
        Assert.Equal([Path.Combine("BuildOrchestrator.Core", "Diagnostics", "MemoryTrim.cs")], offenders);
    }
}
```

- [ ] **Step 2: Kırmızıyı gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~MemoryTrimGuardTests"`
Expected: FAIL — offenders listesi `BuildOrchestrator.App\MainWindow.HiddenSurface.cs` içeriyor.

- [ ] **Step 3: `MemoryTrim` yaz, App ve Supervisor onu kullansın**

```csharp
// src/BuildOrchestrator.Core/Diagnostics/MemoryTrim.cs
namespace BuildOrchestrator.Core.Diagnostics;

/// <summary>Uygulamanın "işim bitti, boştayım" anındaki TEK bellek trimi: nesil 2 + LOH sıkıştırılır ve boşalan bölgeler
/// işletim sistemine geri verilir. <see cref="GCCollectionMode.Aggressive"/> yalnız <c>blocking: true</c> ile geçerlidir.
/// Çağıran, UI'yi durdurmayacağı bir anı seçer (App: final animasyonundan sonra boşta; Supervisor: işlem sonunda).
/// Bu çağrı başka hiçbir dosyada yazılmaz (kaynak guard'ı: MemoryTrimGuardTests).</summary>
public static class MemoryTrim
{
    public static void CollectAndRelease() =>
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
}
```

`MainWindow.HiddenSurface.cs:103`:
```csharp
private static void CollectMemory() => BuildOrchestrator.Core.Diagnostics.MemoryTrim.CollectAndRelease();
```

`RunCoordinator.cs:304–306` (XML doc'u da güncelle: "önce toplar, sonra yazar"):
```csharp
public void TrimAndReportMemory()
{
    try { MemoryTrim.CollectAndRelease(); } catch (Exception) { /* trim bir optimizasyondur; raporu ve işi bozmaz */ }
    MemoryLine.Report(console);
}
```
`RunCoordinator.cs:695` ve `SupervisorHost.cs:181` çağrılarını `TrimAndReportMemory()` yap. SupervisorHost'ta Clean ve Optimize
tamamlanma olaylarının yazıldığı `finally` bloklarına da aynı çağrıyı ekle (dört işlem bitişi aynı kuralı paylaşır; `grep -n
"CleanCompletedEvent\|OptimizeCompletedEvent" src/BuildOrchestrator.Supervisor/SupervisorHost.cs` ile yerleri bul).

- [ ] **Step 4: Supervisor sırasını pinleyen test (sahte toplayıcı değil, çıktı sırası): bellek satırı trim'den SONRA yazılır**

`RunCoordinator` ctor'una `Action? memoryTrim = null` enjekte edilebilir yap (üretim varsayılanı `MemoryTrim.CollectAndRelease`);
test iki sayaçla sırayı pinler:

```csharp
// tests/BuildOrchestrator.Tests/Supervisor/RunCoordinatorMemoryTests.cs
[Fact]
public void The_memory_line_is_written_after_the_trim()
{
    var order = new List<string>();
    var coordinator = SupervisorTestHost.NewCoordinator(console: line => { if (line.StartsWith(MemoryLine.Prefix)) order.Add("line"); },
        memoryTrim: () => order.Add("trim"));
    coordinator.TrimAndReportMemory();
    Assert.Equal(["trim", "line"], order);
}
```
(`SupervisorTestHost.NewCoordinator` yoksa `RunCoordinatorTests`'teki kuruluş yardımcısını kullan — mevcut testlerin `RunCoordinator`'ı
nasıl kurduğuna bak: `grep -rn "new RunCoordinator(" tests`.)

- [ ] **Step 5: Yeşil + tam derleme**

Run: `dotnet build BuildOrchestrator.slnx` ve guard + RunCoordinatorMemoryTests. Expected: PASS.

- [ ] **Step 6: Commit** — `git commit -m "feat(core): bellek trimi tek tanim (MemoryTrim) - App ve Supervisor ayni cagriyi kullanir; motor islem sonunda once toplar sonra bellek satirini yazar"`

### Task 2: `EndedOperationSerial` — her işlem bitişinin tek sinyali

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2587–2609` (`EndedRunSerial`, `MarkRunEnded`)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs:819–836` (`OnSyncCompleted`), Clean/Optimize tamamlanma handler'ları (`OnCleanCompletedAsync`, `OnOptimizeCompletedAsync` — `grep -n "OnCleanCompletedAsync\|OnOptimizeCompletedAsync" src/BuildOrchestrator.App/ViewModels/RunViewModel*.cs`)
- Test: `tests/BuildOrchestrator.Tests/App/OperationEndSignalTests.cs`

**Interfaces:**
- Produces: `internal int EndedOperationSerial { get; }` (bindable, `SetProperty`), `private void MarkOperationEnded()`.

- [ ] **Step 1: Kırmızı test**

```csharp
[Collection("Console UI (serial)")]
public class OperationEndSignalTests
{
    [StaFact]
    public void Every_operation_end_bumps_the_serial_once()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        int before = vm.EndedOperationSerial;

        MainWindowHost.RunBuild(vm, "A");
        Assert.Equal(before + 1, vm.EndedOperationSerial);

        _ = MainWindowHost.StartSync(vm, SyncMode.Manual);
        vm.OnEvent(MainWindowHost.SyncCompleted());          // yardımcı yoksa: new SyncCompletedEvent(...) mevcut testlerdeki gibi
        Assert.Equal(before + 2, vm.EndedOperationSerial);
        GC.KeepAlive(window);
    }
}
```
(Sync tamamlama olayının kurucu parametreleri için `RunViewModelStateTests`'teki mevcut `SyncCompletedEvent` kullanımını kopyala;
Clean/Optimize için `CleanCompletedEvent`/`OptimizeCompletedEvent` ile aynı testi genişlet: +3, +4.)

- [ ] **Step 2: Kırmızıyı gör** — `--filter "FullyQualifiedName~OperationEndSignalTests"` → FAIL: `EndedOperationSerial` yok.

- [ ] **Step 3: Uygula**

`RunViewModel.cs` (`EndedRunSerial`'in altına):
```csharp
/// <summary>Her işlem bitişinde (koşu, Sync, Clean, Optimize) bir artar — işlem sonu trimi (MainWindow.MemoryTrim) bu sinyali
/// dinler. Tek yazıcı <see cref="MarkOperationEnded"/>; koşu için <see cref="MarkRunEnded"/> üzerinden gelir.</summary>
internal int EndedOperationSerial { get => _endedOperationSerial; private set => SetProperty(ref _endedOperationSerial, value); }
private int _endedOperationSerial;
private void MarkOperationEnded() => EndedOperationSerial++;
```
`MarkRunEnded` sonuna `MarkOperationEnded();` (yalnız koşu gerçekten bittiyse: `if (EndedRunSerial == RunSerial && RunSerial != 0)`
koşulunu mevcut "koşu yokken sinyal değişmez" kuralıyla hizala — `EndedRunSerial = RunSerial;` ataması değer DEĞİŞTİRDİYSE çağır).
`OnSyncCompleted` sonuna, `OnCleanCompletedAsync`/`OnOptimizeCompletedAsync`'in yüzey bırakma çağrılarından sonra `MarkOperationEnded();`.

- [ ] **Step 4: Yeşil** — aynı filtre → PASS; `RunBufferReleaseTests` hâlâ yeşil.
- [ ] **Step 5: Commit** — `git commit -m "feat(app): her islem bitisinin tek sinyali EndedOperationSerial (kosu, Sync, Clean, Optimize)"`

### Task 3: Bitiş finalinin bittiğini söyleyen olay

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs:571–600` (`PlayEndFinale`), `:485` (`IsEndFinalePlaying`)
- Test: `tests/BuildOrchestrator.Tests/App/GraphEndFinaleSignalTests.cs` (mevcut final testlerinin bulunduğu dosyaya eklemek de olur: `grep -rln "PlayEndFinale" tests`)

**Interfaces:**
- Produces: `internal event Action? EndFinaleFinished;` (GraphView) — doğal bitişte VE "final yok" dalında ateşlenir; `CancelEndFinale`/`StopEndFinale` ateşlemez.

- [ ] **Step 1: Kırmızı test** (mevcut final testlerinin `AdvanceToForTest` desenini kullan):

```csharp
[StaFact]
public void EndFinaleFinished_fires_once_when_the_finale_completes()
{
    var view = new GraphView { AnimationsEnabledProvider = () => true };
    var window = DsResources.Realize(DsResources.NewHost(), view);
    view.SetGraph(GraphTestData.TwoNodes()); // mevcut yardımcı; yoksa final testlerindeki graf kuruluşunu kopyala
    int fired = 0;
    view.EndFinaleFinished += () => fired++;

    view.PlayEndFinale(["A"], runCount: 1);
    Assert.True(view.IsEndFinalePlaying);
    view.AdvanceEndFinaleForTest(EndFinale.FilterReturnAtMs(1) + 1);   // mevcut test ilerletme yüzeyi (bkz. GraphView:538)
    Assert.False(view.IsEndFinalePlaying);
    Assert.Equal(1, fired);

    view.PlayEndFinale([], runCount: 2);          // final yok dalı da sinyal verir
    Assert.Equal(2, fired);
    GC.KeepAlive(window);
}
```

- [ ] **Step 2: Kırmızıyı gör** → FAIL: `EndFinaleFinished` yok.
- [ ] **Step 3: Uygula** — `PlayEndFinale`'de "final yok" dalında `ResumeFilter(); EndFinaleFinished?.Invoke(); return;`; `_endPlayer.Play(steps, onDone: () => EndFinaleFinished?.Invoke());`.
- [ ] **Step 4: Yeşil**; mevcut final testleri de yeşil (`--filter "FullyQualifiedName~Finale"`).
- [ ] **Step 5: Commit** — `git commit -m "feat(app): graf bitis finali bitince EndFinaleFinished olayi"`

### Task 4: `MemoryTrimRule` — saf karar

**Files:**
- Create: `src/BuildOrchestrator.App/Services/MemoryTrimRule.cs`
- Test: `tests/BuildOrchestrator.Tests/App/MemoryTrimRuleTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed class MemoryTrimRule
{
    public bool ShouldTrim(int endedOperation, bool operationInFlight, bool pendingLogLoad,
                           bool surfaceHidden, bool finalePlaying, bool awaitingTrayIndicator);
    public void MarkTrimmed(int endedOperation);
}
```

- [ ] **Step 1: Kırmızı tablo testi**

```csharp
public class MemoryTrimRuleTests
{
    [Theory]
    [InlineData(0, false, false, false, false, false, false)] // hiç işlem bitmedi
    [InlineData(1, true,  false, false, false, false, false)] // uçuşta işlem
    [InlineData(1, false, true,  false, false, false, false)] // bekleyen log yüklemesi
    [InlineData(1, false, false, false, true,  false, false)] // görünür, final oynuyor
    [InlineData(1, false, false, false, false, false, true)]  // görünür, final bitti
    [InlineData(1, false, false, true,  false, true,  false)] // gizli, gösterge çıkışı bekleniyor
    [InlineData(1, false, false, true,  false, false, true)]  // gizli, gösterge çıktı
    [InlineData(1, false, false, true,  true,  false, true)]  // gizli: final önemsiz
    public void The_rule_table(int ended, bool inFlight, bool pending, bool hidden, bool finale, bool awaiting, bool expected)
    {
        var rule = new MemoryTrimRule();
        Assert.Equal(expected, rule.ShouldTrim(ended, inFlight, pending, hidden, finale, awaiting));
    }

    [Fact]
    public void Each_operation_is_trimmed_at_most_once()
    {
        var rule = new MemoryTrimRule();
        Assert.True(rule.ShouldTrim(3, false, false, false, false, false));
        rule.MarkTrimmed(3);
        Assert.False(rule.ShouldTrim(3, false, false, false, false, false));
        Assert.True(rule.ShouldTrim(4, false, false, false, false, false));
    }
}
```

- [ ] **Step 2: Kırmızıyı gör** → FAIL (tip yok).
- [ ] **Step 3: Uygula**

```csharp
namespace BuildOrchestrator.App.Services;

/// <summary>İşlem sonu bellek triminin SAF kararı (MainWindow uygular). İşlem başına en fazla bir trim; uçuşta işlem ya da
/// bekleyen log yüklemesi varken hiç; pencere görünürse bitiş finali bitmiş olmalı; gizliyse tepside biten koşunun gösterge
/// çıkışı tamamlanmış olmalı (bugünkü tepsi kuralı). Eski kural "görünür pencerede hiç toplanmaz"dı; ölçüm
/// (2026-10-13-13-38-idle-memory-analysis.md §2/§6): görünür yolda 798 MB, tepsi yolunda 444 MB — fark yalnız toplamadan.</summary>
public sealed class MemoryTrimRule
{
    private int _trimmed;

    public bool ShouldTrim(int endedOperation, bool operationInFlight, bool pendingLogLoad,
                           bool surfaceHidden, bool finalePlaying, bool awaitingTrayIndicator)
    {
        if (endedOperation == 0 || endedOperation == _trimmed) return false;
        if (operationInFlight || pendingLogLoad) return false;
        return surfaceHidden ? !awaitingTrayIndicator : !finalePlaying;
    }

    public void MarkTrimmed(int endedOperation) => _trimmed = endedOperation;
}
```
- [ ] **Step 4: Yeşil.** - [ ] **Step 5: Commit** — `git commit -m "feat(app): islem sonu bellek triminin saf karari (MemoryTrimRule)"`

### Task 5: Kabuk kablajı — görünür pencerede de trim; eski testin yeniden yazılması

**Files:**
- Modify: `src/BuildOrchestrator.App/MainWindow.HiddenSurface.cs:110–172` (`_collectedRun`, `_indicatorExitedRun`, `CollectAfterRunWhenDue`, `CollectWhileHidden`, `SetSurfaceHidden`)
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml.cs:413–416` (abonelik `EndedRunSerial` → `EndedOperationSerial`), ctor'a `Shell.GraphHost.EndFinaleFinished += RequestTrimWhenDue;`
- Modify: `tests/BuildOrchestrator.Tests/App/RunBufferReleaseTests.cs:244–259` (`A_visible_window_does_not_collect_after_a_run` yeniden yazılır)
- Test: `tests/BuildOrchestrator.Tests/App/RunBufferReleaseTests.cs` (yeni testler aynı sınıfa)

**Interfaces:**
- Consumes: `RunViewModel.EndedOperationSerial`, `RunViewModel.IsRunInFlight`, `RunViewModel.SyncBusy/CleanBusy/OptimizeBusy/CheckoutBusy/PullBusy` (var), yeni `internal bool HasPendingLogLoad => _pendingLoad is not null;` (RunViewModel'e ekle), `GraphView.IsEndFinalePlaying`, `GraphView.EndFinaleFinished`, `MemoryTrimRule`.
- Produces: `internal TimeSpan TrimGraceAfterFinale { get; set; } = TimeSpan.FromMilliseconds(1500);` (test seam: sıfır ⇒ zamanlayıcı yok), `internal void RequestTrimWhenDue()`.

- [ ] **Step 1: Eski testi yeni kurala göre yeniden yaz (kırmızı)**

```csharp
/// <summary>[DEĞİŞEN KURAL] Eski iddia: "görünür pencerede koşu sonrası hiç toplanmaz" (toplama UI'yi durdurur diye). Ölçüm
/// (2026-10-13): görünür yolda koşu sonrası App 798 MB, tepsi yolunda 444 MB — fark yalnız toplamadan; görünürde de bir kez,
/// finalden sonra boştayken toplanır.</summary>
[StaFact]
public void A_visible_window_collects_once_after_the_end_finale()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
    window.TrimGraceAfterFinale = TimeSpan.Zero;
    int collections = 0;
    window.MemoryCollector = () => collections++;

    MainWindowHost.RunBuild(vm, "A");                // headless: final oynamaz → EndFinaleFinished hemen gelir
    DispatcherPump.DrainToIdle();
    Assert.Equal(1, collections);                    // KIRMIZI: bugün görünürde 0

    DispatcherPump.DrainToIdle();
    Assert.Equal(1, collections);                    // aynı işlem ikinci kez toplanmaz
    GC.KeepAlive(window);
}

[StaFact]
public void A_trim_deferred_into_a_new_run_waits_for_that_run()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
    window.TrimGraceAfterFinale = TimeSpan.Zero;
    int collections = 0;
    window.MemoryCollector = () => collections++;

    MainWindowHost.RunBuild(vm, "A");
    MainWindowHost.StartBuild(vm, "A");              // ApplicationIdle işi koşmadan yeni koşu başladı
    DispatcherPump.DrainToIdle();
    Assert.Equal(0, collections);

    MainWindowHost.FinishBuild(vm, "A");
    DispatcherPump.DrainToIdle();
    Assert.Equal(1, collections);
    GC.KeepAlive(window);
}

[StaFact]
public void A_sync_end_also_trims()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
    window.TrimGraceAfterFinale = TimeSpan.Zero;
    int collections = 0;
    window.MemoryCollector = () => collections++;

    _ = MainWindowHost.StartSync(vm, SyncMode.Manual);
    vm.OnEvent(MainWindowHost.SyncCompleted());
    DispatcherPump.DrainToIdle();
    Assert.Equal(1, collections);
    GC.KeepAlive(window);
}

[StaFact]
public void Hiding_the_window_trims_a_finished_operation_at_once()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
    window.TrimGraceAfterFinale = TimeSpan.FromMinutes(10);   // görünürde pay dolmayacak
    int collections = 0;
    window.MemoryCollector = () => collections++;

    MainWindowHost.RunBuild(vm, "A");
    DispatcherPump.DrainToIdle();
    Assert.Equal(0, collections);                    // pay sürüyor

    window.SetSurfaceHidden(true);                   // gizlendi: pay beklenmez
    DispatcherPump.DrainToIdle();
    Assert.Equal(1, collections);
    GC.KeepAlive(window);
}
```
Mevcut gizli-yol testleri (`A_hidden_window_collects_memory_once…`, `Each_run_that_ends_while_hidden…`, `A_run_that_starts_during_the_indicator_breath…`)
aynen kalır ve yeşil kalmalıdır.

- [ ] **Step 2: Kırmızıyı gör** — `--filter "FullyQualifiedName~RunBufferReleaseTests"`.

- [ ] **Step 3: Uygula** (`MainWindow.HiddenSurface.cs`): `_collectedRun` → `MemoryTrimRule _trimRule = new()`; `_indicatorExitedRun` kalır.

```csharp
internal TimeSpan TrimGraceAfterFinale { get; set; } = TimeSpan.FromMilliseconds(1500);
private DispatcherTimer? _trimGrace;

internal void OnTrayIndicatorExitFinished(int run) { _indicatorExitedRun = run; RequestTrimWhenDue(); }

/// <summary>Dört sinyal buraya gelir: işlem bitti (EndedOperationSerial), gösterge çıktı, final bitti, pencere gizlendi.</summary>
internal void RequestTrimWhenDue()
{
    int ended = _vm.EndedOperationSerial;
    bool awaitingIndicator = _vm.EndedRunSerial != 0 && _vm.EndedRunSerial == _vm.RunSerial && _indicatorExitedRun != _vm.EndedRunSerial;
    if (!_trimRule.ShouldTrim(ended, OperationInFlight(), _vm.HasPendingLogLoad, IsSurfaceHidden,
                              Shell.GraphHost.IsEndFinalePlaying, awaitingIndicator)) return;
    _trimRule.MarkTrimmed(ended);
    _trimGrace?.Stop();
    if (IsSurfaceHidden || TrimGraceAfterFinale <= TimeSpan.Zero) { Dispatcher.InvokeAsync(TrimIfStillIdle, DispatcherPriority.ApplicationIdle); return; }
    _trimGrace = new DispatcherTimer(DispatcherPriority.Background) { Interval = TrimGraceAfterFinale };
    _trimGrace.Tick += (_, _) => { _trimGrace.Stop(); Dispatcher.InvokeAsync(TrimIfStillIdle, DispatcherPriority.ApplicationIdle); };
    _trimGrace.Start();
}

private bool OperationInFlight() => _vm.IsRunInFlight || _vm.SyncBusy || _vm.CleanBusy || _vm.OptimizeBusy || _vm.CheckoutBusy || _vm.PullBusy;

private void TrimIfStillIdle() { if (!OperationInFlight() && !_vm.HasPendingLogLoad) MemoryCollector(); }
```
`SetSurfaceHidden(true)` dalında koreografi iptalinden sonra `_trimGrace?.Stop(); RequestTrimWhenDue();`. `MainWindow.xaml.cs:415`
aboneliği `EndedOperationSerial` olsun; ctor'da `Shell.GraphHost.EndFinaleFinished += RequestTrimWhenDue;`. `awaitingIndicator` mantığı
bugünkü `CollectAfterRunWhenDue`'nun "koşu kimliği" kuralını birebir korur (tepsi testleri bunu pinler); gizli bir Sync bitişi için
`EndedRunSerial == RunSerial` koşulu tutmaz → beklenmez.

- [ ] **Step 4: Yeşil** — `RunBufferReleaseTests` tamamı + `TrayBuildIndicator*` testleri.
- [ ] **Step 5: Commit** — `git commit -m "fix(app): islem sonu bellek trimi gorunur pencerede de - finalden sonra bostayken bir kez; tepsi yolu korunur"`

### Task 6: `NarrativeStore` — anlatının tek sahibi (saf)

**Files:**
- Create: `src/BuildOrchestrator.App/Console/IConsoleBacklog.cs`, `src/BuildOrchestrator.App/Console/NarrativeStore.cs`, `src/BuildOrchestrator.App/Console/ListBacklog.cs`
- Test: `tests/BuildOrchestrator.Tests/App/NarrativeStoreTests.cs`

**Interfaces:**
- Produces:
```csharp
public interface IConsoleBacklog
{
    int Count { get; }
    string Join(int from, int to);                 // '\n' sonekli; from>=to ⇒ ""
    void AcceptTrimmed(IReadOnlyList<string> lines); // belgeden kırpılan CANLI satırlar (store: no-op; liste: ekler)
}
public sealed class NarrativeStore : IConsoleBacklog
{
    public int Count { get; }
    public long CharCount { get; }
    public void Append(string line);               // '\n' içermez; IPC thread'i
    public void Clear();
    public IReadOnlyList<string> Slice(int from, int to);
}
public sealed class ListBacklog(IEnumerable<string> lines) : IConsoleBacklog { … }
```

- [ ] **Step 1: Kırmızı testler**

```csharp
public class NarrativeStoreTests
{
    [Fact]
    public void Append_count_and_join_follow_the_console_line_contract()
    {
        var store = new NarrativeStore();
        store.Append("a"); store.Append("b"); store.Append("c");
        Assert.Equal(3, store.Count);
        Assert.Equal(3, store.CharCount);
        Assert.Equal("b\nc\n", store.Join(1, 3));
        Assert.Equal("", store.Join(3, 3));
        Assert.Equal(["a", "b"], store.Slice(0, 2));
    }

    [Fact]
    public void Clear_empties_everything()
    {
        var store = new NarrativeStore();
        store.Append("a");
        store.Clear();
        Assert.Equal(0, store.Count);
        Assert.Equal(0, store.CharCount);
    }

    [Fact]
    public void Trimmed_lines_are_not_stored_twice()
    {
        var store = new NarrativeStore();
        store.Append("a");
        store.AcceptTrimmed(["a"]);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void A_list_backlog_grows_with_trimmed_live_lines()
    {
        var list = new ListBacklog(["disk1", "disk2"]);
        list.AcceptTrimmed(["live1"]);
        Assert.Equal(3, list.Count);
        Assert.Equal("disk2\nlive1\n", list.Join(1, 3));
    }

    [Fact]
    public async Task Appends_from_another_thread_never_tear_a_join()
    {
        var store = new NarrativeStore();
        var writer = Task.Run(() => { for (int i = 0; i < 20000; i++) store.Append("line " + i); });
        for (int i = 0; i < 200; i++) { int n = store.Count; _ = store.Join(Math.Max(0, n - 50), n); }
        await writer;
        Assert.Equal(20000, store.Count);
    }
}
```

- [ ] **Step 2: Kırmızıyı gör** → FAIL (tipler yok).
- [ ] **Step 3: Uygula**

```csharp
// NarrativeStore.cs
namespace BuildOrchestrator.App.Console;

/// <summary>Koşu anlatısının bellekteki TEK sahibi. VM her satırı buraya ekler (IPC thread'i), konsol görünümü dilim ister
/// (UI thread'i); görünüm kendi kopyasını tutmaz (eski `_backlogLines` + `_runText` çiftinin ölçülen bedeli: 83 k satırlık koşuda
/// 55 + 59 MB). Satırlar '\n' içermez; Join '\n' sonekli metin üretir (append/dikiş sözleşmesi).</summary>
public sealed class NarrativeStore : IConsoleBacklog
{
    private readonly object _gate = new();
    private readonly List<string> _lines = [];
    private long _chars;

    public int Count { get { lock (_gate) return _lines.Count; } }
    public long CharCount { get { lock (_gate) return _chars; } }

    public void Append(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        lock (_gate) { _lines.Add(line); _chars += line.Length; }
    }

    public void Clear() { lock (_gate) { _lines.Clear(); _chars = 0; } }

    public string Join(int from, int to)
    {
        if (from >= to) return "";
        var sb = new System.Text.StringBuilder();
        lock (_gate)
        {
            to = Math.Min(to, _lines.Count);
            for (int i = Math.Max(0, from); i < to; i++) sb.Append(_lines[i]).Append('\n');
        }
        return sb.ToString();
    }

    public IReadOnlyList<string> Slice(int from, int to)
    {
        lock (_gate) { to = Math.Min(to, _lines.Count); from = Math.Max(0, from); return from >= to ? [] : _lines.GetRange(from, to - from); }
    }

    public void AcceptTrimmed(IReadOnlyList<string> lines) { /* store zaten taşır — kopya yok */ }
}

// ListBacklog.cs
public sealed class ListBacklog(IEnumerable<string> lines) : IConsoleBacklog
{
    private readonly List<string> _lines = [.. lines];
    public int Count => _lines.Count;
    public string Join(int from, int to)
    {
        if (from >= to) return "";
        var sb = new System.Text.StringBuilder();
        for (int i = Math.Max(0, from); i < Math.Min(to, _lines.Count); i++) sb.Append(_lines[i]).Append('\n');
        return sb.ToString();
    }
    public void AcceptTrimmed(IReadOnlyList<string> lines) => _lines.AddRange(lines);
}
```
- [ ] **Step 4: Yeşil.** - [ ] **Step 5: Commit** — `git commit -m "feat(app): NarrativeStore - kosu anlatisinin bellekteki tek sahibi; IConsoleBacklog"`

### Task 7: VM ve konsol görünümü store'u kullanır (ikinci kopya kalkar)

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:439` (`_runText`), `:443–445` (`_runLineCount`), `:2873–2874`, `:2915–2917` (`ClearConsoleForNewOperation`), `:2941–2945` (`AppendRunLine`), `:2951–2958` (`GetActiveLineCount`), `:2976` (`GetRunDocumentText`), `:3005–3014` (`SeedRunDocument`)
- Modify: `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs:107–111` (`_backlogLines`, `_loadedFrom`), `:273–310` (`AppendBatch` kırpma dalı), `:636–670` (`ClearRunDocument`, `ReplaceRunDocument`), `:697–716` (`ReplaceProjectDocument`), `:917–947` (`PrependPreviousChunk`), `:975–990` (`LastLines`, `Join`)
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml.cs` ve `MainWindow.HiddenSurface.cs:ResyncAfterShow` — `SeedRunDocument` çağrıları yeni imzaya
- Test: `tests/BuildOrchestrator.Tests/App/ConsoleMemoryTests.cs` (genişler), `ConsoleViewTests.cs`, `RunViewModelStateTests.cs` (derleme uyumu)

**Interfaces:**
- Produces: `public sealed record NarrativeSnapshot(IConsoleBacklog Backlog, int Count);` (App/Console), `RunViewModel.SeedRunDocument(Action<NarrativeSnapshot> applyNow)`, `ConsoleView.ReplaceRunDocument(NarrativeSnapshot snapshot)`, `ConsoleView.ShowRunDocument(NarrativeSnapshot snapshot)`, `ConsoleView.ClearRunDocument()` (boş snapshot ile), `internal bool BacklogOwnsCopy => _backlog is ListBacklog;` (test yüzeyi), `RunViewModel.Narrative` (`internal NarrativeStore`).
- Consumes: Task 6.

- [ ] **Step 1: Kırmızı testler (`ConsoleMemoryTests`'e ekle)**

```csharp
[StaFact]
public void The_narrative_view_keeps_no_copy_of_the_lines()
{
    var store = new NarrativeStore();
    var view = new ConsoleView { AnimationsEnabledProvider = () => false };
    var window = DsResources.Realize(DsResources.NewHost(), view);
    view.ReplaceRunDocument(new NarrativeSnapshot(store, 0));

    for (int batch = 0; batch < 20; batch++)
    {
        for (int i = 0; i < 50; i++) store.Append($"line {batch * 50 + i}");
        view.AppendNarrativeBatch(store.Join(batch * 50, batch * 50 + 50));
    }
    Assert.False(view.BacklogOwnsCopy);                       // KIRMIZI: bugün _backlogLines büyür
    Assert.Equal(1000, store.Count);
    Assert.True(view.Document.LineCount <= ConsoleView.RenderSliceLines + 1);
    view.PrependPreviousChunk();                               // geçmiş store'dan gelir
    Assert.True(view.Document.LineCount > ConsoleView.RenderSliceLines);
    GC.KeepAlive(window);
}

[StaFact]
public void Seeding_the_narrative_renders_the_last_slice_from_the_store()
{
    var store = new NarrativeStore();
    for (int i = 0; i < 500; i++) store.Append($"line {i}");
    var view = new ConsoleView { AnimationsEnabledProvider = () => false };
    var window = DsResources.Realize(DsResources.NewHost(), view);
    view.ReplaceRunDocument(new NarrativeSnapshot(store, store.Count));
    Assert.Equal(ConsoleView.RenderSliceLines + 1, view.Document.LineCount);
    Assert.StartsWith("line 300", view.Document.GetText(view.Document.GetLineByNumber(1)));
    GC.KeepAlive(window);
}
```

- [ ] **Step 2: Kırmızıyı gör** (derleme hatası da kırmızıdır: `NarrativeSnapshot` yok).

- [ ] **Step 3: Uygula**

ConsoleView: `private IConsoleBacklog _backlog = new ListBacklog([]);` (`_backlogLines` kalkar). `AppendBatch` kırpma dalı:
```csharp
if (trimmed > 0)
{
    int alreadyInBacklog = _backlog.Count - _loadedFrom;
    if (trimmed > alreadyInBacklog) _backlog.AcceptTrimmed(LastLines(removed, trimmed - alreadyInBacklog));
    _loadedFrom += trimmed;
}
```
`ReplaceRunDocument(NarrativeSnapshot snapshot)`: `_backlog = snapshot.Backlog; _loadedFrom = Math.Max(0, snapshot.Count - RenderSliceLines);
EditorControl.Document = NewDocument(_backlog.Join(_loadedFrom, snapshot.Count)); … if (snapshot.Count > 0) ClearReadyText();`
`ClearRunDocument() => ReplaceRunDocument(new NarrativeSnapshot(new ListBacklog([]), 0));` — DİKKAT: anlatı temizliği sonrası canlı
satırlar store'a akacağı için `MainWindow`, `ConsoleCleared` olayında `ClearRunDocument()` yerine `ReplaceRunDocument(_vm.NarrativeSnapshot())`
çağırır (store boş, Count 0). `ReplaceProjectDocument(allLines)`: `_backlog = new ListBacklog(allLines);`. `PrependPreviousChunk`:
`string chunk = _backlog.Join(from, _loadedFrom);`. `EvaluateChunkScroll` değişmez (`_loadedFrom > 0`). ConsoleView'daki `Join(list, from, to)`
ve `SplitLines` artık yalnız `LastLines` için kalır.

RunViewModel: `_runText` → `internal NarrativeStore Narrative { get; } = new();`; `_runLineCount` kalkar (sayaç = `Narrative.Count`);
`OnProjectLog`: `Narrative.Append(e.Text);`; `AppendRunLine`: `Narrative.Append(text);`; `ClearConsoleForNewOperation`: `Narrative.Clear();`;
`GetActiveLineCount`: anlatıda `Narrative.Count`; `GetRunDocumentText()`: `Narrative.Join(0, Narrative.Count)` (test yüzeyi, `internal`);
```csharp
public NarrativeSnapshot NarrativeSnapshot() => new(Narrative, Narrative.Count);
public void SeedRunDocument(Action<NarrativeSnapshot> applyNow)
{
    NarrativeSnapshot snapshot;
    lock (_gate) { snapshot = NarrativeSnapshot(); _console.PostReseedDrop(); }
    applyNow(snapshot);
}
```
Kilit ve reseed-drop gerekçesi (mevcut XML doc) aynen korunur: snapshot `Count` sentinel'den önce; sonrası batch'ler nesil guard'ıyla akar.
MainWindow'daki iki çağrı (`ShowRunDocument` / `ResyncAfterShow`) `text => …` yerine `snapshot => Shell.ConsoleViewControl.ReplaceRunDocument(snapshot)`.

- [ ] **Step 4: Yeşil** — `ConsoleMemoryTests`, `ConsoleViewTests`, `ConsoleRenderSliceTests`, `OperationConsoleClearTests`, `RunViewModelStateTests`, `RunBufferReleaseTests`.
- [ ] **Step 5: Commit** — `git commit -m "fix(app): kosu anlatisi tek kopya - konsol backlog'u VM store'undan dilim ister, _runText/_backlogLines cifti kalkar"`

### Task 8: `_liveLines` yalnız bekleyen yükleme için

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:441` (`_liveLines`), `:2864–2888` (`OnProjectLog`), `:3219–3236` (`ReleaseLiveLinesWhenIdle`, `CompletePendingLoad`), `:3164–3200` (`OnProjectLogChunk` okuma)
- Test: `tests/BuildOrchestrator.Tests/App/RunBufferReleaseTests.cs`

- [ ] **Step 1: Kırmızı test**

```csharp
[StaFact]
public void Lines_are_kept_only_while_a_log_load_is_pending_and_only_for_its_project()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null), ("B", null));
    MainWindowHost.StartBuild(vm, "A", "B");
    MainWindowHost.LogLine(vm, "A", 1, "a1");
    MainWindowHost.LogLine(vm, "B", 1, "b1");
    Assert.Equal(0, vm.LiveLineCount);               // KIRMIZI: bugün 2 (koşu boyunca her satır)

    RequestProjectLog(vm, "A");                      // yükleme bekliyor: yalnız A birikir
    MainWindowHost.LogLine(vm, "A", 2, "a2");
    MainWindowHost.LogLine(vm, "B", 2, "b2");
    Assert.Equal(1, vm.LiveLineCount);

    ReplyWithProjectLog(vm, "A", "a1\n", through: 1); // dikiş: a2 eklenir, tampon boşalır
    Assert.Equal("a1\na2\n", vm.GetProjectDocumentText(MainWindowHost.IdOf("A")));
    Assert.Equal(0, vm.LiveLineCount);
    GC.KeepAlive(window);
}
```
Mevcut `A_finished_run_releases_its_live_line_buffer` testinin ön koşulu ("koşu sürerken tampon dolu") yeni kuralla çelişir: testi
"yükleme beklerken koşu biterse tampon dikişe dek yaşar, dikiş bitince boşalır" olarak yeniden yaz (doc'a eski iddia + gerekçe).

- [ ] **Step 2: Kırmızıyı gör.**
- [ ] **Step 3: Uygula** — `OnProjectLog` içinde:
```csharp
if (_pendingLoad is { } pending && string.Equals(pending.ProjectId, e.ProjectId, StringComparison.OrdinalIgnoreCase))
{
    if (!_liveLines.TryGetValue(e.ProjectId, out var list)) _liveLines[e.ProjectId] = list = [];
    list.Add(e);
}
```
(`_pendingLoad` yalnız UI thread'inde yazılır; burada okuma benign race — mevcut `ActiveProjectId` notuyla aynı gerekçe, yorumda belirt.)
`CompletePendingLoad`: `lock (_gate) _liveLines.Clear();` (bırakma artık yüklemenin sonundadır); `ReleaseLiveLinesWhenIdle` gövdesi aynı kalır
(koşu bitişinde bekleyen yükleme yoksa zaten boştur). `OnProjectLogChunk` dikişi değişmez.
- [ ] **Step 4: Yeşil** — `RunBufferReleaseTests`, `RunViewModelStateTests` (dikiş testleri), `RunViewModelTests`.
- [ ] **Step 5: Commit** — `git commit -m "fix(app): canli satir tamponu yalniz bekleyen log yuklemesinin projesi icin, yukleme suresince"`

### Task 9: Tanı raporuna bellek satırları

**Files:**
- Modify: `src/BuildOrchestrator.App/Services/DiagnosticsReport.cs:14–25` (`DiagnosticsInput`), `:70–90` (`Compose` Runtime grubu); `DiagnosticsInput`'u dolduran yer (`grep -rn "new DiagnosticsInput(" src`)
- Test: `tests/BuildOrchestrator.Tests/App/DiagnosticsReportTests.cs` (mevcut dosyaya ekle; yoksa yeni)

- [ ] **Step 1: Kırmızı test** — `Compose` çıktısında `Runtime` grubunda `"Memory"` etiketli satır ve değer `MemoryLine.Format(1, 2, 3)` biçiminde ("memory: private=…" öneki olmadan, yalnız `private=0 committed=0 heap=0` gövdesi — `MemoryLine`'a `FormatBody(...)` ekle, `Format` onu kullanır; kopya yok).
- [ ] **Step 2: Kırmızı.** - [ ] **Step 3: Uygula** — `DiagnosticsInput`'a `string Memory`; `Compose`: `new("Memory", input.Memory)`; çağıran yer `MemoryLine.CurrentBody()` verir.
- [ ] **Step 4: Yeşil.** - [ ] **Step 5: Commit** — `git commit -m "feat(app): tani raporunda App bellek satiri (private/committed/heap)"`

### Task 10: Bütçe testleri (yapısal + ortam kapılı ölçüm)

**Files:**
- Create: `tests/BuildOrchestrator.Tests/App/NarrativeBudgetTests.cs`
- Modify: `ARCHITECTURE.md` §17.5 (ölçüm testleri listesi: `BO_MEASURE_MEMORY`)

- [ ] **Step 1: Testler (ilk ikisi Task 7–8 ile zaten yeşil olmalı — bunlar regresyon kilididir; üçüncüsü kapılı)**

```csharp
[Collection("Console UI (serial)")]
public class NarrativeBudgetTests
{
    private const int Lines = 100_000;
    private static string Line(int i) => $"  D:\\repo\\Project{i % 200}\\File{i}.cs(12,5): warning CS1591: Missing XML comment for publicly visible type or member 'X{i}'";

    [StaFact]
    public void A_synthetic_run_leaves_one_copy_of_the_text_and_no_live_line_buffer()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        MainWindowHost.StartBuild(vm, "A");
        for (int i = 1; i <= Lines; i++) MainWindowHost.LogLine(vm, "A", i, Line(i));
        MainWindowHost.FinishBuild(vm, "A");
        DispatcherPump.DrainToIdle();

        Assert.Equal(Lines, vm.Narrative.Count);
        Assert.Equal(0, vm.LiveLineCount);
        Assert.False(window.Shell.ConsoleViewControl.BacklogOwnsCopy);
        GC.KeepAlive(window);
    }

    [SkippableFact]
    public void Managed_memory_after_a_synthetic_run_and_a_trim_stays_within_budget()
    {
        Skip.IfNot(Environment.GetEnvironmentVariable("BO_MEASURE_MEMORY") == "1");
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        window.TrimGraceAfterFinale = TimeSpan.Zero;
        long before = GC.GetTotalMemory(forceFullCollection: true);
        long text = 0;
        MainWindowHost.StartBuild(vm, "A");
        for (int i = 1; i <= Lines; i++) { string l = Line(i); text += l.Length * 2L; MainWindowHost.LogLine(vm, "A", i, l); }
        MainWindowHost.FinishBuild(vm, "A");
        DispatcherPump.DrainToIdle();                   // trim burada koşar (üretim toplayıcısı)
        long after = GC.GetTotalMemory(forceFullCollection: true);
        Assert.True(after - before <= text * 12 / 10, $"growth {(after - before) / 1048576.0:0.0} MB > 1.2 x text {text / 1048576.0:0.0} MB");
        GC.KeepAlive(window);
    }
}
```
(Dalga 2'de ikinci testin bütçesi `8 MB` sabitine iner — Task 13.)

- [ ] **Step 2: Koştur** — yapısal test PASS; ölçüm testi `BO_MEASURE_MEMORY=1` ile PASS (`$env:BO_MEASURE_MEMORY='1'; dotnet test … --filter "FullyQualifiedName~NarrativeBudgetTests"`).
- [ ] **Step 3: §17.5'e satır** — "`BO_MEASURE_MEMORY=1` — `NarrativeBudgetTests`: sentetik 100 k satırlık koşu sonrası yönetilen bellek bütçesi".
- [ ] **Step 4: Commit** — `git commit -m "test(app): anlati bellek butcesi - yapisal kilit + ortam kapili olcum (BO_MEASURE_MEMORY)"`

### Task 11: Dalga 1 kapanışı — dokümanlar, tam süit, ölçüm, merge

- [ ] **Step 1: ARCHITECTURE.md** — §11.4 (motor trim eder, sonra yazar; App'in trim kuralı: her işlem sonunda, görünürde finalden sonra boşta, gizlide gösterge çıkınca; tek tanım `MemoryTrim`), §13.5 (konsol tamponu: `NarrativeStore` tek sahip, görünüm dilim ister, backlog kopyası yok; `_liveLines` yalnız bekleyen yükleme), §12.3 veya HiddenSurface'in anlatıldığı yer (tepsideki toplama artık genel kuralın bir dalı), §17.5 (Task 10), §22 kod haritası (yeni dosyalar: `Core/Diagnostics/MemoryTrim.cs`, `App/Services/MemoryTrimRule.cs`, `App/Console/NarrativeStore.cs`, `IConsoleBacklog.cs`, `ListBacklog.cs`). README: davranış değişmediği için yalnız "memory" bahsi varsa güncelle (`grep -n -i memory README.md`).
- [ ] **Step 2: Tam süit** — `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"` (uygulama kapalı; 8–14 dk). Düşen zamanlama testi varsa tek başına tekrar; ortam kaynaklıysa CLAUDE.md'deki `LocalOnly` kuralı.
- [ ] **Step 3: Ölçüm** — spec §9: `dotnet build src\BuildOrchestrator.App\BuildOrchestrator.App.csproj -c Release`, sonra `mem-after-build.ps1 -Dir .claude\temp\mem-2026-10-13\wave1-visible` (uygulamaya dokunma, ~10 dk). Beklenen `S1-postrun-idle-60s`: App private ≤ 500 MB (taban 798), Supervisor ≤ 50. Sonucu `.claude/outputs/<yyyy-MM-dd-HH-mm>-idle-memory-wave1-result.md`'ye yaz (taban/sonuç tablosu + heap tip dökümünün ilk 10 satırı).
- [ ] **Step 4: Merge** — `git switch develop && git merge --no-ff fix/idle-memory-wave1 -m "merge: islem sonu bellek trimi gorunur pencerede de (App+Supervisor), kosu anlatisi tek kopya, canli satir tamponu daraltildi, butce testleri; ARCHITECTURE (fix/idle-memory-wave1)" && git push`; doğrula (`git log --oneline -3 origin/develop`), branch'i sil.

---

# DALGA 2 — anlatı diskte + kompakt konsol (`fix/idle-memory-wave2`)

### Task 12: `NarrativeStore` spool: dosya + ofset indeksi + bellek penceresi

**Files:**
- Modify: `src/BuildOrchestrator.App/Console/NarrativeStore.cs`
- Modify: `src/BuildOrchestrator.Core/Logs/RunLogPaths.cs` (`public const string NarrativeFileName = "narrative.log";`)
- Test: `tests/BuildOrchestrator.Tests/App/NarrativeStoreTests.cs`

**Interfaces:**
- Produces: `public const int MemoryWindowLines = 2000;`, `public bool BeginSpool(string path)` (false ⇒ açılamadı, bellekte devam), `public bool IsSpooling`, `public void Flush()`, `public event Action<string>? Warning;` (tek sefer, İngilizce metin: `"warning: console history could not be written to <path> — kept in memory (<hata>)"`).

- [ ] **Step 1: Kırmızı testler**

```csharp
[Fact]
public void Lines_beyond_the_memory_window_are_read_back_from_the_spool()
{
    using var dir = new TempDir();
    var store = new NarrativeStore();
    Assert.True(store.BeginSpool(Path.Combine(dir.Path, "narrative.log")));
    for (int i = 0; i < NarrativeStore.MemoryWindowLines + 500; i++) store.Append("line " + i);
    Assert.Equal(NarrativeStore.MemoryWindowLines + 500, store.Count);
    Assert.True(store.CharCount <= NarrativeStore.MemoryWindowLines * "line 99999".Length); // bellekte yalnız pencere
    Assert.Equal("line 0\nline 1\n", store.Join(0, 2));                                 // diskten
    Assert.Equal(["line 2499"], store.Slice(2499, 2500));                                // bellekten
    store.Clear();
    Assert.False(store.IsSpooling);
}

[Fact]
public void Lines_appended_before_the_spool_opens_are_written_first()
{
    using var dir = new TempDir();
    var store = new NarrativeStore();
    store.Append("early");
    store.BeginSpool(Path.Combine(dir.Path, "narrative.log"));
    store.Append("late");
    store.Flush();
    Assert.Equal(["early", "late"], File.ReadAllLines(Path.Combine(dir.Path, "narrative.log")));
}

[Fact]
public void A_failed_spool_keeps_everything_in_memory_and_warns_once()
{
    var store = new NarrativeStore();
    var warnings = new List<string>();
    store.Warning += warnings.Add;
    Assert.False(store.BeginSpool(Path.Combine("Z:\\no-such-drive", "narrative.log")));
    for (int i = 0; i < NarrativeStore.MemoryWindowLines + 10; i++) store.Append("line " + i);
    Assert.Equal("line 0\n", store.Join(0, 1));
    Assert.Single(warnings);
    Assert.StartsWith("warning: console history could not be written", warnings[0]);
}
```

- [ ] **Step 2: Kırmızı.**
- [ ] **Step 3: Uygula** — alanlar: `FileStream? _file; StreamWriter? _writer; List<long> _offsets; int _evicted` (bellekten atılan satır sayısı; `_lines[i - _evicted]`). `Append`: spool açıksa yaz (`_writer.Write(line); _writer.Write('\n')`, ofset = yazmadan önceki `_file.Position` — tamponlu yazımda `StreamWriter` kullanılıyorsa ofseti kendin say: `_nextOffset += Encoding.UTF8.GetByteCount(line) + 1`), sonra `_lines.Count > MemoryWindowLines` ise baştan at (`RemoveRange(0, n)`, `_evicted += n`, `_chars` düş). `Join/Slice`: istenen aralık `_evicted`'ın altındaysa `Flush()` + dosyadan `_offsets[from]..(_offsets[to] ya da dosya sonu)` okunur (`FileShare.ReadWrite` ile ikinci bir `FileStream` aç, UTF-8 çöz, '\n' ile böl). Hata: yazım istisnasında spool kapanır, `Warning` bir kez, bellekte devam (atılmış satır yoksa kayıp yok; atılmış satır varsa o ana dek yazılanlar dosyada — okuma hâlâ oradan). `BeginSpool` dosyayı `FileMode.Create` ile açar; önce bellekteki satırları yazar. `Clear`: writer/stream kapatılır, `_offsets` ve `_evicted` sıfırlanır (dosya diskte kalır). `AcceptTrimmed` no-op kalır.
- [ ] **Step 4: Yeşil** (+ Task 6 testleri). - [ ] **Step 5: Commit** — `git commit -m "feat(app): NarrativeStore diske yayilir - ofset indeksi, 2000 satirlik bellek penceresi, yazim hatasinda bellekte devam"`

### Task 13: Koşu başında spool, işlem başında kapanış; bütçe sıkılaştırma; dokümanlar

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (`OnRunStarted` — `grep -n "private void OnRunStarted"`; `ClearConsoleForNewOperation`)
- Modify: `tests/BuildOrchestrator.Tests/App/NarrativeBudgetTests.cs` (ölçüm bütçesi `8 MB`), yeni test `RunViewModelStateTests`'e
- Modify: `ARCHITECTURE.md` §8.5, §13.5, §16; `README.md` (log klasörü içeriği anlatılıyorsa)

- [ ] **Step 1: Kırmızı test** — `RunStartedEvent(..., LogDirectory: dir.Path)` sonrası `vm.Narrative.IsSpooling` true ve dosya `Path.Combine(dir.Path, RunLogPaths.NarrativeFileName)` var; `LogDirectory: null` ile false; yeni işlem (`StartSync`) sonrası false. Spool uyarısı `AppendRunLine` ile konsola düşer (`vm.Narrative.Warning += AppendRunLine` kablajı ctor'da): `Z:\` ile test.
- [ ] **Step 2: Kırmızı.** - [ ] **Step 3: Uygula** — `OnRunStarted` içinde `if (e.LogDirectory is { } logDir) Narrative.BeginSpool(Path.Combine(logDir, RunLogPaths.NarrativeFileName));` (ClearConsoleForNewOperation'dan SONRA çalıştığından emin ol: temizlik `BeginRunAsync`'te, runStarted sonra gelir). `NarrativeBudgetTests` ölçüm bütçesi: `after - before <= 8L * 1024 * 1024`.
- [ ] **Step 4: Dokümanlar** — §8.5: "`narrative.log` — the run's console narrative as the App showed it, written by the App into the run folder once `runStarted` names it; pruned with the folder"; §16 tablo satırı; §13.5: bellek penceresi 2.000 satır, gerisi diskten, spool yoksa bellekte.
- [ ] **Step 5: Yeşil + commit** — `git commit -m "feat(app): kosu anlatisi run klasorunde narrative.log'a yayilir, konsol gecmisi diskten sayfalanir; ARCHITECTURE"`

### Task 14: `CompactConsole` ayarı — katalog → dosya → VM → ui-state

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/GeneralSettings.cs:7–16` (enum), `:49–85` (katalog; yeni grup `CONSOLE`), `src/BuildOrchestrator.App/Shell/UiStateStore.cs` (`public bool? CompactConsole { get; set; }`), `src/BuildOrchestrator.App/ViewModels/SettingsFile.cs` (`[JsonPropertyName("compactConsole")] public bool? CompactConsole`, `From(...)` parametresi), `src/BuildOrchestrator.App/ViewModels/SettingsDraftViewModel.cs` (özellik, ctor parametresi `bool compactConsole = true`, `ToFile`, `LoadFrom`, `ApplyAsync` → state + `run.ApplySettingsAsync(..., compactConsole)`, `BuildGeneralGroups` PropertyChanged köprüsü), `src/BuildOrchestrator.App/Views/SettingsDialog.xaml.cs:140–143` (`run.CompactConsole`), `src/BuildOrchestrator.App/ViewModels/RunViewModel.ActionBar.cs:200–215, 262–292` (`ApplyCompactConsole`, `ApplySettingsAsync` imzası), `src/BuildOrchestrator.App/MainWindow.xaml.cs:208` (`_vm.CompactConsole = saved.CompactConsole ?? true;`), `:1285–1305` (`OnWorkflowPreferenceChanged` — `CompactConsole` dalı + `s.CompactConsole = _vm.CompactConsole;`)
- Test: `tests/BuildOrchestrator.Tests/App/CompactConsoleSettingsTests.cs` (`ResolveAtFullPrioritySettingsTests` deseni birebir)

**Interfaces:**
- Produces: `GeneralSetting.CompactConsole`; `RunViewModel.CompactConsole` (`[ObservableProperty] bool _compactConsole = true;`); `RunViewModel.ApplySettingsAsync(..., bool resolveAtFullPriority = true, bool compactConsole = true)`.
- Katalog satırı: `new(GeneralSetting.CompactConsole, "Compact console", "Errors in full, the first warnings of each project and a count of the rest; every other MSBuild line stays in the project log. Off: every line.", Default: true)` — grup `"CONSOLE"`, NOTIFICATIONS'tan önce.

- [ ] **Step 1: Kırmızı testler** — `The_switch_is_on_by_default` (`Row().Default` true ve `new RunViewModel(...).CompactConsole` true), `The_setting_round_trips_through_the_file` (`"compactConsole": false`), `Applying_settings_updates_the_view_model` (`ApplySettingsAsync(... compactConsole: false)` → `vm.CompactConsole` false ve konsola `"Compact console off — every MSBuild line is shown from the next run"` satırı), `Saved_ui_state_seeds_the_view_model` (MainWindowHost ile `UiState.CompactConsole = false` kaydedilmiş dizinden açılış → false).
- [ ] **Step 2: Kırmızı.** - [ ] **Step 3: Uygula** (yukarıdaki dosyaların her birinde `ResolveAtFullPriority`'nin yanına aynı desenle). `ApplyCompactConsole(bool on)`: değer değiştiyse `CompactConsole = on;` ve not: `$"{label} on — errors, the first warnings of each project and a warning count from the next run"` / `$"{label} off — every MSBuild line is shown from the next run"`.
- [ ] **Step 4: Yeşil** (+ `ResolveAtFullPrioritySettingsTests`, `SettingsDialog*` testleri). - [ ] **Step 5: Commit** — `git commit -m "feat(app): Compact console ayari (General > CONSOLE) - katalog, dosya, ui-state, VM"`

### Task 15: `CompactConsoleFilter` — saf süzgeç

**Files:**
- Create: `src/BuildOrchestrator.App/Console/CompactConsoleFilter.cs`
- Test: `tests/BuildOrchestrator.Tests/App/CompactConsoleFilterTests.cs`

**Interfaces:**
- Produces:
```csharp
public sealed class CompactConsoleFilter
{
    public const int ShownWarningsPerProject = 3;
    public const int SummaryCodes = 3;
    public string ProjectStarted(string projectId, string name);          // "building <name>"
    public string? LogLine(string projectId, string text);               // null ⇒ gizle
    public string? ProjectDone(string projectId, string name);           // null ⇒ gizlenen uyarı yok
    public void Reset();                                                 // yeni işlem
}
```

- [ ] **Step 1: Kırmızı tablo testi**

```csharp
public class CompactConsoleFilterTests
{
    private const string W = "  C:\\r\\A.cs(1,1): warning CS1591: Missing XML comment";
    private const string E = "  C:\\r\\A.cs(2,1): error CS0103: The name 'x' does not exist";

    [Fact]
    public void Errors_pass_warnings_are_capped_other_lines_are_hidden()
    {
        var f = new CompactConsoleFilter();
        Assert.Equal("building A", f.ProjectStarted("a", "A"));
        Assert.Null(f.LogLine("a", "  Copying file from X to Y"));
        Assert.Null(f.LogLine("a", ""));
        Assert.Equal(E, f.LogLine("a", E));
        Assert.Equal(W, f.LogLine("a", W));
        Assert.Equal(W, f.LogLine("a", W));
        Assert.Equal(W, f.LogLine("a", W));
        Assert.Null(f.LogLine("a", W));                                   // 4.
        Assert.Null(f.LogLine("a", "  C:\\r\\B.cs(3,1): warning MSB3277: Found conflicts"));
        Assert.Equal(E, f.LogLine("a", E));                               // limit hatayı etkilemez
        Assert.Equal("A: 2 more warnings — CS1591 ×1, MSB3277 ×1 — see the project log", f.ProjectDone("a", "A"));
        Assert.Null(f.ProjectDone("b", "B"));                             // gizlenen yok
    }

    [Fact]
    public void Counts_are_per_project_and_reset_per_operation()
    {
        var f = new CompactConsoleFilter();
        for (int i = 0; i < 5; i++) f.LogLine("a", W);
        Assert.Equal("A: 2 more warnings — CS1591 ×2 — see the project log", f.ProjectDone("a", "A"));
        f.Reset();
        Assert.Equal(W, f.LogLine("a", W));
    }

    [Fact]
    public void A_warning_without_a_code_counts_as_other()
    {
        var f = new CompactConsoleFilter();
        for (int i = 0; i < 4; i++) f.LogLine("a", "warning: dependent X failed in this run");
        Assert.Equal("A: 1 more warnings — other ×1 — see the project log", f.ProjectDone("a", "A"));
    }
}
```
(Metin biçimi birebir: `"{name}: {n} more warnings — {code} ×{count}, … — see the project log"`; kodlar çoktan aza, eşitlikte alfabetik; en çok `SummaryCodes`.)

- [ ] **Step 2: Kırmızı.** - [ ] **Step 3: Uygula** — sınıflandırma `ConsoleLineClassifier.Classify(text)`; kod çıkarımı `Regex(@": warning ([A-Za-z]+\d+):")` (başarısızsa `"other"`); proje başına `Dictionary<string, (int shown, Dictionary<string,int> hidden)>`.
- [ ] **Step 4: Yeşil.** - [ ] **Step 5: Commit** — `git commit -m "feat(app): CompactConsoleFilter - hatalar tam, proje basina ilk 3 uyari, gerisi ozet satiri"`

### Task 16: Süzgecin VM'e bağlanması + başlıkta `compact`

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2237–2247` (`OnProjectStarted`), `:2356–2380` (`OnProjectDone`), `:2864–2888` (`OnProjectLog`), `ClearConsoleForNewOperation` (`_compact.Reset()`), `OnProjectSkipped` (atlanan projede özet yok)
- Modify: `src/BuildOrchestrator.App/Console/ConsoleHeader.xaml` (`LinesText`'in sağına `<TextBlock x:Name="CompactText" Text="compact" Margin="6,0,0,0" … aynı stil, Visibility="Collapsed"/>`), `ConsoleHeader.xaml.cs` (`public void SetCompact(bool on)`), `MainWindow.xaml.cs` (VM `CompactConsole` değişiminde ve açılışta `Shell.ConsoleHeaderControl.SetCompact(_vm.CompactConsole)`)
- Test: `tests/BuildOrchestrator.Tests/App/CompactConsoleTests.cs`

- [ ] **Step 1: Kırmızı testler**

```csharp
[Collection("Console UI (serial)")]
public class CompactConsoleTests
{
    [StaFact]
    public void Compact_mode_keeps_the_narrative_small_and_the_project_page_full()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        Assert.True(vm.CompactConsole);
        MainWindowHost.StartBuild(vm, "A");
        vm.OnEvent(new ProjectStartedEvent("r1", MainWindowHost.IdOf("A"), "A"));
        for (int i = 1; i <= 10; i++) MainWindowHost.LogLine(vm, "A", i, $"  C:\\r\\A.cs({i},1): warning CS1591: Missing XML comment");
        MainWindowHost.LogLine(vm, "A", 11, "  C:\\r\\A.cs(11,1): error CS0103: The name 'x' does not exist");
        MainWindowHost.FinishBuild(vm, "A");

        string narrative = vm.GetRunDocumentText();
        Assert.Contains("building A\n", narrative);
        Assert.Equal(3, narrative.Split('\n').Count(l => l.Contains(": warning ")));
        Assert.Contains(": error CS0103", narrative);
        Assert.Contains("A: 7 more warnings — CS1591 ×7 — see the project log", narrative);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Full_mode_shows_every_line()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        vm.CompactConsole = false;
        MainWindowHost.StartBuild(vm, "A");
        for (int i = 1; i <= 10; i++) MainWindowHost.LogLine(vm, "A", i, $"  C:\\r\\A.cs({i},1): warning CS1591: Missing XML comment");
        Assert.Equal(10, vm.GetRunDocumentText().Split('\n').Count(l => l.Contains(": warning ")));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_header_shows_the_compact_tag_only_when_the_mode_is_on()
    {
        using var dir = new TempDir();
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
        Assert.Equal(Visibility.Visible, window.Shell.ConsoleHeaderControl.CompactText.Visibility);
        vm.CompactConsole = false;
        DispatcherPump.DrainToIdle();
        Assert.Equal(Visibility.Collapsed, window.Shell.ConsoleHeaderControl.CompactText.Visibility);
        GC.KeepAlive(window);
    }
}
```
(Proje sayfasının tam kaldığı: `RequestProjectLog/ReplyWithProjectLog` ile `GetProjectDocumentText` 11 satır — `RunBufferReleaseTests` yardımcılarını kopyala.)

- [ ] **Step 2: Kırmızı.** - [ ] **Step 3: Uygula** — `OnProjectLog` (`_gate` altında): `string? shown = CompactConsole ? _compact.LogLine(e.ProjectId, e.Text) : e.Text;` → `_projectText` her zaman tam metinle (aktif proje), `Narrative.Append(shown)` + `_console.Post(shown)` yalnız `shown is not null`. `CompactConsole` IPC thread'inden okunur: `volatile bool` yedeği ya da `Volatile.Read` (ObservableProperty alanı doğrudan okunmaz — yorumla belirt). `OnProjectStarted`: `if (CompactConsole) AppendRunLine(_compact.ProjectStarted(e.ProjectId, e.Name));`. `OnProjectDone`: `if (CompactConsole && _compact.ProjectDone(projectId, row.Name) is { } summary) AppendRunLine(summary);`. `ClearConsoleForNewOperation`: `_compact.Reset();`. Başlık: `SetCompact`.
- [ ] **Step 4: Yeşil** (+ `ConsoleHeader*` realize testleri: yeni TextBlock için `ConsoleHeaderDesignTests`'e bir satır — "compact etiketi `LinesText` ile aynı font/renk").
- [ ] **Step 5: Commit** — `git commit -m "feat(app): kompakt konsol - projectLog suzgeci VM'de, proje basina uyari ozeti, baslikta compact etiketi"`

### Task 17: Dalga 2 kapanışı

- [ ] ARCHITECTURE §13.5 (kompakt kip kuralları tablosu, başlık etiketi, "ayar sonraki işlemde"), §13.3 (ayar satırı), §16 (`CompactConsole` anahtarı), §22 (yeni dosyalar); README ayar listesi + kısa "Compact console" açıklaması.
- [ ] Tam süit yeşil; ölçüm (spec §9, `wave2-visible`): App private ≤ 390 (kompakt açık), Supervisor ≤ 50 → toplam ≤ 435; ek olarak `Full` kipte bir koşu (ayarı kapatıp) ≤ 435 + ~50. Sonuç raporu `…-idle-memory-wave2-result.md`.
- [ ] Merge: `merge: kosu anlatisi diske yayilir (narrative.log) ve konsol diskten sayfalar; kompakt konsol ayari; ARCHITECTURE, README (fix/idle-memory-wave2)`; push; branch sil.

---

# DALGA 3 — motor uykusu (`fix/idle-memory-wave3`)

### Task 18: `EngineHost.SleepAsync / WakeAsync`

**Files:**
- Modify: `src/BuildOrchestrator.App/Services/EngineHost.cs:105–170` (`StartAsync`, `RestartAsync`), `:205–215` (`ShutdownGracefullyAsync`)
- Test: `tests/BuildOrchestrator.Tests/App/EngineHostTests.cs`

**Interfaces:**
- Produces: `public bool IsAsleep { get; }`, `public Task SleepAsync()`, `public Task<EngineReadyEvent> WakeAsync(CancellationToken ct = default)`.

- [ ] **Step 1: Kırmızı test**

```csharp
[Fact]
public async Task Sleep_stops_the_engine_without_EngineExited_and_wake_starts_a_fresh_one()
{
    using var sandbox = new SupervisorSandbox();
    await using var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
    var ready1 = await host.StartAsync();
    int exited = 0;
    host.EngineExited += _ => exited++;

    await host.SleepAsync();
    Assert.True(host.IsAsleep);
    Assert.Null(host.EnginePid);
    Assert.Throws<ArgumentException>(() => Process.GetProcessById(ready1.Pid));   // süreç gitti
    await Task.Delay(300);
    Assert.Equal(0, exited);                                                       // KIRMIZI: bugün kasıtlı çıkış da ölüm sayılır

    var ready2 = await host.WakeAsync();
    Assert.False(host.IsAsleep);
    Assert.NotEqual(ready1.Pid, ready2.Pid);
    var pong = new TaskCompletionSource<PongEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
    host.EventReceived += e => { if (e is PongEvent p) pong.TrySetResult(p); };
    await host.SendAsync(new PingCommand(7));
    Assert.Equal(7, (await pong.Task.WaitAsync(TimeSpan.FromSeconds(5))).Seq);
}

[Fact]
public async Task Wake_on_an_awake_engine_is_a_no_op_and_dispose_after_sleep_is_clean()
{
    using var sandbox = new SupervisorSandbox();
    var host = sandbox.IsolatedEngineHost(WideStartupTimeout);
    var ready = await host.StartAsync();
    Assert.Equal(ready.Pid, (await host.WakeAsync()).Pid);
    await host.SleepAsync();
    await host.DisposeAsync();                                                     // fırlatmaz
}
```

- [ ] **Step 2: Kırmızı.** - [ ] **Step 3: Uygula**

```csharp
public bool IsAsleep { get; private set; }

/// <summary>İşsiz motoru kasıtlı durdurur: ShutdownCommand + çıkış beklenir (≤ KillExitWait, sonra kill). Kasıtlı çıkış
/// EngineExited ÜRETMEZ — generation önce artırılır (ShutdownGracefullyAsync bunu zaten yapar), eski watcher susar.</summary>
public async Task SleepAsync()
{
    if (IsAsleep || _child is null) { IsAsleep = _child is null; return; }
    await ShutdownGracefullyAsync().ConfigureAwait(false);
    IsAsleep = true;
}

public async Task<EngineReadyEvent> WakeAsync(CancellationToken ct = default)
{
    if (!IsAsleep && _child is not null && _ready is { Task.IsCompletedSuccessfully: true } ready) return await ready.Task;
    var r = await StartAsync(ct).ConfigureAwait(false);
    IsAsleep = false;
    return r;
}
```
`StartAsync` başarılı olunca `IsAsleep = false`. `KillAndAwaitExit` 1 s bekler; `ShutdownGracefullyAsync` ShutdownCommand'ı 500 ms'de yazar — toplam ≤ 2 s (spec).

- [ ] **Step 4: Yeşil** (+ mevcut `EngineHostTests`). - [ ] **Step 5: Commit** — `git commit -m "feat(app): EngineHost.SleepAsync/WakeAsync - kasitli durdurma EngineExited uretmez"`

### Task 19: `StopEngineWhenIdle` ayarı (Task 14 deseni)

**Files:** Task 14'teki dosyaların aynıları; grup `"ENGINE"` (CONSOLE'dan sonra, NOTIFICATIONS'tan önce); satır:
`new(GeneralSetting.StopEngineWhenIdle, "Stop the engine when idle", "After five minutes without work the engine process exits. It starts again in a moment when you build or sync.", Default: true)`;
`UiState.StopEngineWhenIdle`, `SettingsFile` `"stopEngineWhenIdle"`, `RunViewModel.StopEngineWhenIdle` (varsayılan true), `ApplySettingsAsync(..., bool stopEngineWhenIdle = true)`, not metinleri: `$"{label} on — the engine exits after five idle minutes"` / `$"{label} off — the engine stays running"`.
**Test:** `tests/BuildOrchestrator.Tests/App/StopEngineWhenIdleSettingsTests.cs` (Task 14'teki dört testin aynısı).

- [ ] Kırmızı → uygula → yeşil → `git commit -m "feat(app): Stop the engine when idle ayari (General > ENGINE)"`

### Task 20: `EngineIdleRule` + etkinlik takibi + tik

**Files:**
- Create: `src/BuildOrchestrator.App/Services/EngineIdleRule.cs`
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:1871–1880` (`TrySendAsync` — başarılı gönderimde `_lastEngineActivityMs = _nowMs()`), `:1975–2020` (`OnEvent` başında aynı), `:1903–1919` (`TickElapsed` — `EvaluateEngineSilence()` yanına `EvaluateEngineIdle()`), `:2826` (`OnEngineReady` — etkinlik damgası)
- Test: `tests/BuildOrchestrator.Tests/App/EngineIdleRuleTests.cs`, `tests/BuildOrchestrator.Tests/App/EngineSleepTests.cs`

**Interfaces:**
- Produces: `public static class EngineIdleRule { public static readonly TimeSpan IdleAfter = TimeSpan.FromMinutes(5); public static bool ShouldSleep(long nowMs, long lastActivityMs, bool settingOn, bool operationInFlight, bool pendingLogLoad, bool isAsleep, bool engineAvailable); }`
- VM: `internal Func<bool> EngineIsAsleep { get; set; }` (varsayılan `() => _engine.IsAsleep`), `internal Func<Task> SleepEngine { get; set; }` (varsayılan `_engine.SleepAsync`), `internal Func<Task<EngineReadyEvent>> WakeEngine { get; set; }` (varsayılan `() => _engine.WakeAsync()`), `internal int EngineSleeps { get; }` (sayaç, test).

- [ ] **Step 1: Kırmızı testler**

```csharp
public class EngineIdleRuleTests
{
    private static readonly long Five = (long)EngineIdleRule.IdleAfter.TotalMilliseconds;

    [Theory]
    [InlineData(Five,     true,  false, false, false, true,  true)]
    [InlineData(Five - 1, true,  false, false, false, true,  false)]
    [InlineData(Five,     false, false, false, false, true,  false)]  // ayar kapalı
    [InlineData(Five,     true,  true,  false, false, true,  false)]  // uçuşta işlem
    [InlineData(Five,     true,  false, true,  false, true,  false)]  // bekleyen log yüklemesi
    [InlineData(Five,     true,  false, false, true,  true,  false)]  // zaten uyuyor
    [InlineData(Five,     true,  false, false, false, false, false)]  // motor yok/ölü (uyutacak şey yok)
    public void The_rule_table(long elapsed, bool on, bool inFlight, bool pending, bool asleep, bool available, bool expected) =>
        Assert.Equal(expected, EngineIdleRule.ShouldSleep(1000 + elapsed, 1000, on, inFlight, pending, asleep, available));
}

[Collection("Console UI (serial)")]
public class EngineSleepTests
{
    [StaFact]
    public void Five_idle_minutes_put_the_engine_to_sleep_once()
    {
        using var dir = new TempDir();
        long now = 0;
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, nowMs: () => now, ("A", null));   // nowMs enjeksiyonu yoksa MainWindowHost'a ekle
        bool asleep = false; int sleeps = 0;
        vm.EngineIsAsleep = () => asleep;
        vm.SleepEngine = () => { asleep = true; sleeps++; return Task.CompletedTask; };

        MainWindowHost.RunBuild(vm, "A");                 // son etkinlik: now=0
        now = (long)EngineIdleRule.IdleAfter.TotalMilliseconds - 1; vm.TickElapsed();
        Assert.Equal(0, sleeps);
        now += 1; vm.TickElapsed();
        Assert.Equal(1, sleeps);                          // KIRMIZI
        vm.TickElapsed();
        Assert.Equal(1, sleeps);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_setting_off_keeps_the_engine_running()
    {
        using var dir = new TempDir();
        long now = 0;
        var (window, vm, _) = MainWindowHost.NewWithProjects(dir, nowMs: () => now, ("A", null));
        int sleeps = 0; vm.SleepEngine = () => { sleeps++; return Task.CompletedTask; };
        vm.StopEngineWhenIdle = false;
        MainWindowHost.RunBuild(vm, "A");
        now = (long)EngineIdleRule.IdleAfter.TotalMilliseconds * 2; vm.TickElapsed();
        Assert.Equal(0, sleeps);
        GC.KeepAlive(window);
    }
}
```

- [ ] **Step 2: Kırmızı.** - [ ] **Step 3: Uygula** — `EvaluateEngineIdle()`: kural true ise `_ = SleepEngine()` (hata yutulur, `AppendRunLine` yok; `EngineSleeps++`). `engineAvailable = !IsEngineUnavailable && EngineDiedMessage is null`. Gizli pencerede de koşar (`TickElapsed(surfaceVisible)` bayrağından bağımsız — `EvaluateEngineSilence` gibi).
- [ ] **Step 4: Yeşil.** - [ ] **Step 5: Commit** — `git commit -m "feat(app): motor 5 dk issiz kalinca uyur (EngineIdleRule, tik)"`

### Task 21: Uyandırma kapısı — her motor yolu önce uyandırır

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:1871–1880` (`TrySendAsync`), `:3136–3160` (`LoadProjectLogAsync`), `:2826–2850` (`OnEngineReady` → yeni `OnEngineResumed`), `RunViewModel.Stream.cs`/`RibbonText` (uyanma metni: mevcut motor-başlıyor ifadesi; `grep -rn "Starting engine\|engine starting\|Boot" src/BuildOrchestrator.App/ViewModels/RibbonText.cs`)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:1185–1197` (`WhyRunCannotStart` — uyku "unavailable" değildir; değişiklik gerekmez, test pinler)
- Test: `tests/BuildOrchestrator.Tests/App/EngineSleepTests.cs` (devam), `EngineSilenceWatchdogTests.cs` (bir test)

**Interfaces:**
- Produces: `private async Task<bool> EnsureEngineAwakeAsync()` (true ⇒ hazır), `public void OnEngineResumed(string engineVersion, int pid)`, `[ObservableProperty] bool _engineWaking` (şerit metni için).

- [ ] **Step 1: Kırmızı testler**

```csharp
[StaFact]
public async Task Every_engine_path_wakes_the_engine_first_and_writes_nothing_to_the_console()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
    bool asleep = true; int wakes = 0;
    vm.EngineIsAsleep = () => asleep;
    vm.WakeEngine = () => { asleep = false; wakes++; return Task.FromResult(new EngineReadyEvent("1.0", 4242)); };
    var sent = new List<IpcCommand>(); vm.DebugOnCommandSent = sent.Add;
    int linesBefore = vm.Narrative.Count;

    await vm.BuildCommand.ExecuteAsync(null);
    Assert.Equal(1, wakes);
    Assert.IsType<StartRunCommand>(sent.Last());
    Assert.Equal(linesBefore, vm.Narrative.Count);        // uyanma sessiz
    Assert.Equal(4242, vm.EnginePid);

    MainWindowHost.FinishBuild(vm, "A");
    asleep = true;
    await MainWindowHost.StartSync(vm, SyncMode.Silent);   // commit sonrası otomatik Sync yolu
    Assert.Equal(2, wakes);
    asleep = true;
    _ = vm.LoadProjectLogAsync(MainWindowHost.IdOf("A"));
    Assert.Equal(3, wakes);
    GC.KeepAlive(window);
}

[StaFact]
public void A_sleeping_engine_does_not_close_the_run_gate()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
    vm.EngineIsAsleep = () => true;
    Assert.Null(vm.WhyRunCannotStart());
    GC.KeepAlive(window);
}

[StaFact]
public async Task A_failed_wake_takes_the_unavailable_path()
{
    using var dir = new TempDir();
    var (window, vm, _) = MainWindowHost.NewWithProjects(dir, ("A", null));
    vm.EngineIsAsleep = () => true;
    vm.WakeEngine = () => throw new EngineUnavailableException(@"C:\x\supervisor.exe", EngineUnavailableReason.CannotStart);
    await vm.BuildCommand.ExecuteAsync(null);
    Assert.True(vm.IsEngineUnavailable);
    GC.KeepAlive(window);
}
```
`EngineSilenceWatchdogTests`: uyanma `TrySendAsync`'in İÇİNDE komut gönderiminden önce bittiği için sessizlik penceresi gönderimde başlar —
"uyanma süresi sessizlik uyarısı üretmez" testi: `WakeEngine` 10 s'lik sahte gecikmeyle (nowMs ilerletilerek) → `EngineOverdueMessage` null.

- [ ] **Step 2: Kırmızı.** - [ ] **Step 3: Uygula** — `TrySendAsync` başında `if (!await EnsureEngineAwakeAsync()) return false;`; `LoadProjectLogAsync` başında aynı. `EnsureEngineAwakeAsync`: `if (!EngineIsAsleep()) return true; EngineWaking = true; try { var r = await WakeEngine(); OnEngineResumed(r.EngineVersion, r.Pid); return true; } catch (EngineUnavailableException ex) { OnEngineUnavailable(ex.ExePath, ex.Reason); ReleaseAfterEngineLoss(); return false; } catch (Exception ex) { AppendRunLine($"[error] engine could not be started: {ex.Message}"); ReleaseAfterEngineLoss(); return false; } finally { EngineWaking = false; }`. `OnEngineResumed` yalnız `EngineVersion`/`EnginePid` (ve etkinlik damgası) yazar — konsol satırı ve açılış Sync'i yok (`OnEngineReady`'nin yaptıklarını okuyup ayır). Şerit: `EngineWaking` true iken mevcut motor-başlıyor metni (`RibbonText.Compose` girdisi).
- [ ] **Step 4: Yeşil** (+ `TrayHotkeyBalloonTests`, `EngineSilenceWatchdogTests`, `RunViewModelStateTests`). - [ ] **Step 5: Commit** — `git commit -m "feat(app): uyuyan motoru her komut yolu once uyandirir (tek kapi), uyanma sessizdir"`

### Task 22: Dalga 3 kapanışı

- [ ] ARCHITECTURE: yeni **§4.7 Engine sleep** (ne zaman uyur, neyin uyandırdığı, EngineExited üretmediği, 5 dk sabiti, ayar), §4.4 sonlandırma matrisine "deliberate sleep" satırı, §4.6'ya bir cümle (uyku ≠ ölüm), §11.4 (motor uyurken bellek satırı yok), §13.3 ayar satırı, §16 anahtar, §22 (`EngineIdleRule.cs`); README ayar listesi.
- [ ] Tam süit yeşil; ölçüm: `mem-after-build.ps1` ile görünür koşu, ardından **6 dk** boşta bekleyip `Get-Process BuildOrchestrator*` ile Supervisor'ın yokluğunu ve App'in private'ını kaydet (script S3'ten sonra uygulamayı öldürür — ölçüm için scriptteki son `Start-Sleep -Seconds 120`'yi 400'e çıkar ya da elle bekle). Beklenen toplam ≤ 390. Sonuç raporu `…-idle-memory-wave3-result.md`.
- [ ] Merge: `merge: issiz motor 5 dk sonra uyur, ilk komutta uyanir; Stop the engine when idle ayari; ARCHITECTURE 4.7 (fix/idle-memory-wave3)`; push; branch sil; oturum `develop`'ta biter.

---

## Self-review (plan ↔ spec)

- Spec §6.1 → Task 1–5 · §6.2 → Task 6–8 · §6.3 → Task 12–13 · §6.4 → Task 18–21 · §6.5 → Task 14–16 · §6.6 → Task 9–10 · §7 dalgalar/ölçüm → Task 11, 17, 22 · §8 riskler → Review Focus 1–5 ve ilgili testler · §10 kabul → her dalganın kapanış görevi.
- Adlar tutarlı: `MemoryTrim.CollectAndRelease`, `MemoryTrimRule.ShouldTrim/MarkTrimmed`, `EndedOperationSerial`, `EndFinaleFinished`, `TrimGraceAfterFinale`, `RequestTrimWhenDue`, `NarrativeStore` (`Append/Clear/Join/Slice/CharCount/BeginSpool/Flush/IsSpooling/Warning/MemoryWindowLines`), `IConsoleBacklog` (`Count/Join/AcceptTrimmed`), `ListBacklog`, `NarrativeSnapshot`, `BacklogOwnsCopy`, `CompactConsoleFilter` (`ProjectStarted/LogLine/ProjectDone/Reset`), `RunViewModel.CompactConsole/StopEngineWhenIdle/EngineIsAsleep/SleepEngine/WakeEngine/EnsureEngineAwakeAsync/OnEngineResumed/EngineWaking`, `EngineHost.IsAsleep/SleepAsync/WakeAsync`, `EngineIdleRule.ShouldSleep/IdleAfter`, `RunLogPaths.NarrativeFileName`.
- Yardımcı yoksa (`MainWindowHost.SyncCompleted()`, `nowMs:` parametresi, `AdvanceEndFinaleForTest`, `SupervisorTestHost.NewCoordinator`): aynı dosyadaki mevcut deseni genişleterek ekle; planda adı geçen her yardımcı ya vardır ya da görevin içinde tanımlanır.
