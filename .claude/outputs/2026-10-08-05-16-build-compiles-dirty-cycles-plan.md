# Build kirli cycle gruplarını da derler — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Düz Build (ve Rebuild), kirli dependency-cycle gruplarını Resolve cycles'ın tur mekanizmasıyla derler; temiz grup atlanır; bir cycle üyesine bağımlı projeler taze çıktıya karşı derlenir. Kullanıcı pull'dan sonra yalnız Build'e basar.

**Architecture:** Motorda tek kapı: SCC üyelik haritası (`CycleGroups`) artık yalnız `Cycles` modunda değil `Build`/`Rebuild`'de de scheduler'a ve run context'ine verilir. Grup kapısı (bileşik imzası temiz grup `up to date` tohumlanır), tur döngüsü (`CycleRoundPolicy`, yüzey kanıtı, `CycleMemberNeed`) ve defter kuralları değişmez. Core'da "bu koşu SCC derler mi" kararı tek yerde (`CycleCompilation.CompilesCycles`) tanımlanır; Sync önizlemesi, Supervisor planı, koordinatör kapısı ve App bu karardan okur. App'te mod kontrolleri "şu an bir grup turda mı" sorusuna döner. Resolve cycles düğmesi dar kapsamlı (yalnız cycle'lar + upstream) isteğe bağlı koşu olarak kalır.

**Tech Stack:** .NET 10 (`net10.0` / `net10.0-windows`), WPF, xUnit. Supervisor process + NDJSON IPC, `MSBuild.exe` shell-out. Derleme/test: `dotnet build BuildOrchestrator.slnx` · `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.

**Spec:** Bu dosyanın **A bölümü** (tasarım kararları). Ayrı spec dosyası yok. Kök neden kanıtı: `%LOCALAPPDATA%\BuildOrchestrator\logs\run-20261007-131748-933\decision.log` ve aynı klasördeki `6FFC08BC5474963D.log` (Business.Service.WorkOrder, `CS1061 IsDst`).

---

## A. Tasarım kararları (spec)

### A.1 Sorun

2026-10-07 12:32'deki pull, cycle üyesi `OSYS.Types.Service.WorkOrder` ve `OSYS.Types.UsedCars`'a `IsDst` ekledi. 13:17'deki Build cycle üyelerini `skipped — in dependency cycle` ile atladı; bağımlıları (`OSYS.Business.Service.WorkOrder`, `OSYS.Business.UsedCars`) paylaşılan `C:\OSYS\Server\Bin` klasöründeki eski Types DLL'ine karşı derlenip `CS1061` aldı. Kırmızı satırın logunda kök neden yok: bayat-bağımlılık uyarısı yalnız tek-proje kapsamında üretiliyor (`RunCoordinator.cs` `StaleDependenciesById`, yalnız `ProjectRunScope`). Tek sinyal koşu sonundaki "N cycle projects have pending changes — run Cycles" satırıydı; o satır da pratikte hiç tetiklenmiyordu (Sync ve Build önizlemesi üyeye hep `WillBuild=false` yazdığından). Visual Studio aynı projeleri solution sırasıyla derleyip geçti.

Bugünkü ayrı-buton tasarımının gerekçesi (ARCHITECTURE §8.1 "Why it is separate rather than folded into Build"): grup maliyeti üye × tur, Build'e gömülünce "iki dakikalık build on beş dakika" ölçülmüştü. O ölçüm tur-öncesi kanıt mekanizmasından (`CycleMemberNeed`, `ApiSurfaceHash` kısa devresi) öncedir. 2026-10-07'nin iki Cycles koşusunda (09:29 ve 11:39) her grup 1. turda yakınsadı (`moved=none`); tur mekanizmasının kendi payı grup başı ve tur sonu yüzey hash'leridir (saniyeler), süreyi derlemenin kendisi belirliyor (17 üyeli UI grubu ~100 sn). Maliyet artık "bir kez derle + hash'le" düzeyindedir; görünürlüğü koşu öncesi sayı (dalga, tooltip) ve koşu sırasındaki tur fazı sağlar.

### A.2 Yeni kural (tek cümle)

**Build, kirli olan her şeyi bağımlılık sırasında derler; cycle grubu plandaki bir düğümdür.**

| Mod | Eski | Yeni |
|---|---|---|
| Build | cycle üyeleri hiç derlenmez (`in dependency cycle`) | kirli grup turlarla derlenir; bileşik imzası temiz grup `up to date` atlanır; bağımlılar grubun bitmesini bekler |
| Rebuild | cycle üyeleri hiç derlenmez | her grup derlenir; 1. turda her üye (önbellek yok sayılır), sonraki turlar kanıta göre |
| Cycles (Resolve cycles) | üyeler + transitif upstream, turlarla | **değişmez**; "yalnız cycle'lar" kapsamı, isteğe bağlı |
| Clean | tümü, döngüsüz plan | değişmez |
| Satırdan Build/Rebuild (tek proje) | üye düz düğüm olarak tek başına, kardeşleri bayat referans | değişmez |

### A.3 Kararlar

- **D1 Kapı.** `groups` haritası `cmd.ScopeProjectId is null && CycleCompilation.CompilesCycles(cmd.Mode) && plan'da SCC var` ise kurulur. `CompilesCycles(mode) => mode is Build or Rebuild or Cycles`. Clean'in planı zaten döngüsüzdür (`CleanRunScope`).
- **D2 Grup kapısı Build'de de.** Her üyesi `WillBuild == false` olan grup `SkipReasons.UpToDate` ile tohumlanır (Cycles'taki aynı kod). Rebuild'de tohum yok: her grup dispatch edilir.
- **D3 Rebuild tur 1.** Rebuild'de `CycleMemberNeed` kararı atlanır, her üye 1. turda derlenir; yüzey kanıtı (`hashMode`) yine okunur ki sonraki turlar kanıtla karar versin. decision.log'a `cycle {grup}: rebuild — every member compiles in round one` yazılır.
- **D4 Tur politikası değişmez.** `CycleRoundPolicy.RoundCap` (3), yüzey kanıtı, iki ardışık yeşil tur kuralı aynen.
- **D5 Perf değişmez.** Build'in turları Build profilinin cap/priority'si altında koşar. "Resolve cycles at full priority" yalnız `Cycles` koşusuna uygulanır (`PerfProfile.ForRun` değişmez).
- **D6 Yakınsamama hafızası** grup derleyen her koşuda okunur ve yalnız raporlanır (`CycleDecisionLines.Retrying`); bloklamaz.
- **D7 Sync önizlemesi** `buildCycles: CycleCompilation.CompilesCycles(RunMode.Build)` ile bağlanır: kirli cycle üyesi `WillBuild=true` gelir, satır yanar, "N to build" sayar; temiz grup bütünüyle `false`.
- **D8 Supervisor planı** `Bind`'e `CycleCompilation.CompilesCycles(cmd.Mode)` geçer.
- **D9 ConditionalRebuild değişmez.** Grup üyesi tek başına koşullu değildir; `GroupAppliesTo` grup düzeyinde karar verir (Build'de de).
- **D10 App.** Şeritte tur satırı ("▸ Resolving cycles · round R/K · n/m · t") moda değil uçuştaki tura bağlıdır; "preparing dependencies" yalnız Cycles koşusunun turlar öncesi penceresidir. `IsResolvingCycles` (Resolve düğmesi spinner'ı, Restart kilidi, `PreviewWritesPlanFlag`) Cycles koşusuna bağlı kalır. ETA'nın döngü kovası grup derleyen her modda. `ScopeFor(Rebuild)` tüm satırlar. `CyclesHint` ve `CleanedCyclesHint` kalkar. `DecisionLabel.For`'un `inCycle` parametresi kalkar. `CycleCompletedEvent` ekrandaki grubun tur sayaçlarını sıfırlar. Konsol boş-durum metninde döngü üyesi plan bayrağına göre konuşur.
- **D11 IPC değişmez.** `RunStartedEvent.Mode` kalır; yeni alan yok.
- **D12 Resolve düğmesi** etkinlik/tooltip aynı; README ve ARCHITECTURE'daki "önce Resolve, sonra Build" cümleleri "isteğe bağlı dar kapsam" olarak yeniden yazılır.
- **D13 `NextPreview`** (App'in canlı geçişleri) bir sonraki düz Build'in cevabını verir: güvenilmez başarı ve dep-issue'lu üye `WillBuild=true`, Clean sonrası üye `true`.
- **D14 `SkipReasons.InDependencyCycle` kalır**: üreticisi yalnız grup haritasız scheduler (kill-switch yolu, üretimde erişilmez). Metni/sabit adı değişmez.
- **D15 Ölçüm.** Gerçek OSYS'te Clean → Build (tek koşu) süresi ile 2026-10-07'nin Clean → Cycles → Build zinciri karşılaştırılır; sonuç `.claude/outputs/` kaydına ve `CycleRoundsTests` sınıf doc'una yazılır (ARCHITECTURE'a rakam gömülmez).

### A.4 Değişmeyenler

`ReadySetScheduler`, `CycleRoundPolicy`, `CycleMemberNeed`, `ApiSurfaceHash`, `CycleReadFiles`, defter persist kuralları, `ProjectRunScope`, `ExternalUpdater.ShouldUpdate`, `PerfProfile.ForRun`, `CycleRunScope` (Cycles kapsamı), `SkipReasons` sabitleri, Contracts.

### A.5 Kullanıcının göreceği akış (Build'e basınca)

1. Dalga: derlenecek satırlar yanar; kirli cycle üyeleri de aralarında, temiz üyeler sönük.
2. Cycle'ın upstream'i (sıradan projeler) önce derlenir.
3. Grubun sırası gelince şerit "▸ Resolving cycles · round 1/3 · n/m · t" yazar; üyeler seviye seviye derlenir; grup başı ve tur sonu yüzey kontrolü. Kardeşinin API'si değiştiği için bayat kalan üye yalnız 2. turda derlenir.
4. Grup bitince şerit "▸ Building n/m"ye döner; Business/Orchestration/UI sırayla, taze cycle DLL'lerine karşı derlenir.
5. Tek "Completed" satırı. Yakınsamayan grup olursa üyeleri amber (mevcut yol), bağımlıları dependency issue üçgeniyle.

---

## Global Constraints

- **Branch:** `feat/build-compiles-dirty-cycles`, `develop`'tan (`37c98faf`), ana checkout `D:\Projects\Other\Apps\app_build_orchestrator` (worktree YOK). Her task sonunda commit; mesajlar Türkçe, `feat:`/`test:`/`docs:`/`refactor:` önekli. **Attribution satırı yazılmaz** (`Co-Authored-By`, `Generated with` yok — CLAUDE.md kuralı).
- **Kırmızı test kuralı:** önce test, kırmızı gösterilir, sonra kod. Eski kuralı pinleyen test silinmez; yeni kuralı pinleyecek biçimde yeniden yazılır ve XML doc'una **eski iddia + değişme gerekçesi (ölçüm: 2026-10-07 13:17 koşusu, ARCHITECTURE §8.1)** yazılır.
- **Kopya YASAK:** "SCC derleyen mod" kararı yalnız `CycleCompilation.CompilesCycles`. App'te ikinci bir mod tablosu yazılmaz.
- **Dil:** kod, UI metinleri ve loglar İngilizce; kod yorumları ve `.claude/` kayıtları Türkçe. README/ARCHITECTURE İngilizce, anlatı üslubu; değişen bölüm yerinde yeniden yazılır, "eskiden/şimdi" anlatısı yazılmaz; bayatlayacak rakam gömülmez.
- **Dokunulmaz:** `CHANGELOG.md`, `Directory.Build.props` → `Version`, Contracts (IPC), OutDir/bin/obj kuralları.
- **Açık uygulama:** Build Orchestrator (tray dahil) ve `BuildOrchestrator.Supervisor.exe` açıkken `src/.../bin/Debug` kilitlidir. Test/derleme öncesi kapat; kapanmıyorsa `-c Release` ile derle ve test et, `--no-build` KULLANMA (bayat DLL koşturur), yeni testlerin koştuğunu `--list-tests` ile doğrula.
- **Süit:** her task sonunda ilgili test sınıfı; bitişte tam süit `--filter "Category!=Acceptance"` yeşil. Yeni test pencere açmaz, CPU yakmaz; `Measurement`/`LocalOnly` etiketi gerekmez.
- **İlerleme panosu:** uygulayan oturum başında adım listesini `& 'C:\Users\Delta\.claude\scripts\status.ps1' -Event steps -Sid <session_id> -Steps ...` ile gönderir; task başına bir adım, boyut etiketi dürüst (`[kisa]`/`[orta]`).

## Review Focus

1. **Satırdan tetiklenen Build/Rebuild'de hedef bir cycle üyesi:** düz düğüm olarak tek başına derlenir, kardeşleri `warning: X is in a dependency cycle and was not rebuilt — last known output referenced` ile bayat referans. `groups` kapısındaki `cmd.ScopeProjectId is null` koşulu bunu korur; Task 2 Adım 9'daki test pinler.
2. **Rebuild'de güvenilir kaydı olan cycle üyesi:** yine 1. turda derlenir — Task 3 testi.
3. **Build'de yakınsamayan grup:** üyeler kanıtsız hata, bağımlıları dep-issue uyarısıyla derlenir — Task 2 Adım 8 testi (NoProgress + bağımlı Z).
4. **Build'in turu sürerken Stop:** her üye tam bir kez `Complete` edilir, koşu Stopped biter — Task 2 Adım 10 (mevcut stop testi iki modla koşar).
5. **Fast dependent mode'da Build:** üye terimleri boş → tur 1 herkes, kanıtsız iki yeşil tur — Task 2 Adım 3 testi kanıtsız planla tam bunu Build'de pinler.

## Dosya haritası

**Create**
- `src/BuildOrchestrator.Core/Planning/CycleCompilation.cs` — "bu koşu SCC derler mi" (tek kaynak).
- `tests/BuildOrchestrator.Tests/Planning/CycleCompilationTests.cs`.

**Modify — motor/Core**
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` (~310-323): `buildCycles`.
- `src/BuildOrchestrator.Supervisor/Program.cs` (~72-73 yorum, ~204 `Bind`).
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (~713-720 yorum, ~766-779 kapı, ~800-852 tohum, ~1731-1750 tur-1 kararı).
- `src/BuildOrchestrator.Core/Planning/NextPreview.cs` (`AfterSuccess`, `AfterClean`).
- `src/BuildOrchestrator.Core/Planning/CycleDecisionLines.cs` (yeni satır).
- XML doc düzeltmeleri: `Core/Planning/WillBuildEvaluator.cs`, `Core/Incremental/IncrementalPlanner.cs`, `Core/Incremental/IncrementalRunBinder.cs`, `Core/Scheduling/ReadySetScheduler.cs`, `Core/Planning/CycleRunScope.cs`, `Core/Planning/BuildPreview.cs`.

**Modify — App**
- `src/BuildOrchestrator.App/ViewModels/RibbonText.cs` (~164-186).
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` (~76-84 doc, ~288-298 `CycleCompletedEvent`, ~332-352 hint blokları).
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (~1098-1118 `ScopeFor`, ~2455-2480 ETA).
- `src/BuildOrchestrator.App/ViewModels/StreamText.cs` (~198-212 iki hint metodu).
- `src/BuildOrchestrator.App/ViewModels/DecisionLabel.cs` (~82-85 param doc, ~99-103) + çağıranlar `ViewModels/RunViewModel.Workspace.cs:787`, `Views/ProjectRow.xaml.cs:545`.
- `src/BuildOrchestrator.App/Console/ConsoleEmptyState.cs` (~88, ~121-124, ~209).
- `src/BuildOrchestrator.App/ViewModels/CycleText.cs` (ölü sabitler `Membership`, `ClusterHeadline` silinir).

**Modify — doküman:** `README.md`, `ARCHITECTURE.md` (liste Task 6'da).

**Tests (rewrite/ekle):** `Supervisor/CycleRoundsTests.cs`, `Supervisor/RunCoordinatorTests.cs` (yalnız `Start` doc), `Supervisor/CycleDecisionLogTests.cs`, `Workspace/SyncWorkspaceServiceTests.cs`, `Planning/NextPreviewTests.cs`, `App/RibbonTextTests.cs`, `App/EventStreamTests.cs`, `App/ChoreographyTests.cs`, `App/DecisionLabelTests.cs`, `App/ConsoleModesTests.cs`, `App/RunViewModelStateTests.cs` + Ek B'dekiler.

---

### Task 0: Branch ve pano

**Files:** yok (git).

- [ ] **Step 1: Temiz develop'tan branch aç**

```bash
cd /d/Projects/Other/Apps/app_build_orchestrator
git status --short            # boş olmalı
git switch develop && git pull --ff-only
git switch -c feat/build-compiles-dirty-cycles
```

- [ ] **Step 2: Pano adımlarını gönder** (session_id, mesajlardaki `[pano] session_id:` satırından)

```powershell
& 'C:\Users\Delta\.claude\scripts\status.ps1' -Event steps -Sid <session_id> -Steps 'T1 Core karar + Sync + NextPreview [orta]|in_progress','T2 Koordinator grup dispatch [uzun]|pending','T3 Rebuild tur-1 kurali [kisa]|pending','T4 App serit/akis/ETA/etiket [orta]|pending','T5 Dokuman yeniden yazimi [orta]|pending','T6 Tam suit + olcum + merge [orta]|pending'
```

- [ ] **Step 3: Uygulamanın kapalı olduğunu doğrula**

```bash
tasklist | grep -i -E "BuildOrchestrator|MSBuild.exe" || echo "temiz"
```
Açıksa kullanıcıdan kapatmasını iste; kapanmıyorsa tüm `dotnet build/test` komutlarına `-c Release` ekle.

---

### Task 1: Core — `CycleCompilation`, Sync önizlemesi, Supervisor planı, `NextPreview`

**Files:**
- Create: `src/BuildOrchestrator.Core/Planning/CycleCompilation.cs`
- Create: `tests/BuildOrchestrator.Tests/Planning/CycleCompilationTests.cs`
- Modify: `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` (~310-323)
- Modify: `src/BuildOrchestrator.Supervisor/Program.cs` (~72-73, ~204)
- Modify: `src/BuildOrchestrator.Core/Planning/NextPreview.cs`
- Modify (yalnız XML doc): `Core/Planning/WillBuildEvaluator.cs` (satır ~11-16, ~49-51, ~98-103), `Core/Incremental/IncrementalPlanner.cs` (~99, ~209-211), `Core/Incremental/IncrementalRunBinder.cs` (~117), `Core/Planning/BuildPreview.cs` (~70)
- Test: `tests/BuildOrchestrator.Tests/Workspace/SyncWorkspaceServiceTests.cs`, `tests/BuildOrchestrator.Tests/Planning/NextPreviewTests.cs`

**Interfaces:**
- Produces: `public static class BuildOrchestrator.Core.Planning.CycleCompilation { public static bool CompilesCycles(RunMode mode); }` — Task 2, 3, 4 bunu okur.
- `NextPreview.AfterSuccess`/`AfterClean` imzaları değişmez; dönüş değerleri değişir.

- [ ] **Step 1: Kırmızı test — `CycleCompilationTests`**

`tests/BuildOrchestrator.Tests/Planning/CycleCompilationTests.cs`:

```csharp
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Core.Planning;

namespace BuildOrchestrator.Tests.Planning;

/// <summary>
/// [Build cycle derler] "Bu koşu SCC (dependency cycle) üyelerini derler mi" kararının TEK kaynağı. Build ve
/// Rebuild de kirli grupları Cycles'ın tur mekanizmasıyla derler; Clean hiçbir şey derlemez. Sync'in önizlemesi,
/// Supervisor'ın planı, koordinatörün grup kapısı ve App'in tur muhasebesi hep buradan okur (kopya YASAK).
/// </summary>
public class CycleCompilationTests
{
    [Theory]
    [InlineData(RunMode.Build, true)]
    [InlineData(RunMode.Rebuild, true)]
    [InlineData(RunMode.Cycles, true)]
    [InlineData(RunMode.Clean, false)]
    public void Every_compiling_mode_compiles_dirty_cycle_groups_and_clean_does_not(RunMode mode, bool expected)
        => Assert.Equal(expected, CycleCompilation.CompilesCycles(mode));
}
```

- [ ] **Step 2: Kırmızıyı gör**

Run: `dotnet build tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj`
Expected: derleme hatası — `CycleCompilation` tanımsız (CS0103).

- [ ] **Step 3: `CycleCompilation.cs`**

```csharp
namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;

/// <summary>
/// Bir koşunun SCC (dependency cycle) üyelerini DERLEYİP derlemediğinin TEK kaynağı. Build ve Rebuild de kirli
/// grupları Cycles'ın tur mekanizmasıyla derler (ARCHITECTURE §8.1); Clean hiçbir şey derlemez, planı zaten
/// döngüsüzdür (<c>CleanRunScope</c>). Satırdan tetiklenen tek-proje kapsamı bu soruyu sormaz: hedef düz düğüm
/// olarak tek başına derlenir (<see cref="ProjectRunScope"/>), karar koordinatörde <c>ScopeProjectId</c> ile
/// ayrıca kapılıdır.
/// <para>Kopya YASAK: Sync'in önizlemesi (<c>SyncWorkspaceService</c>), Supervisor'ın planı
/// (<c>Program.ComputeIncremental</c>), koordinatörün grup kapısı (<c>RunCoordinator.PlanAndRunAsync</c>) ve
/// App'in tur muhasebesi (<c>RunViewModel.UpdateEta</c>) hep buradan okur — iki yer sessizce ayrışamaz.</para>
/// </summary>
public static class CycleCompilation
{
    public static bool CompilesCycles(RunMode mode) => mode is RunMode.Build or RunMode.Rebuild or RunMode.Cycles;
}
```

- [ ] **Step 4: Yeşili gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~CycleCompilationTests"`
Expected: 4 passed.

- [ ] **Step 5: Kırmızı test — Sync önizlemesi kirli cycle üyesini "derlenecek" sayar**

`tests/BuildOrchestrator.Tests/Workspace/SyncWorkspaceServiceTests.cs` — fixture bölümüne (`WriteWorkspace`'in altına) ekle:

```csharp
    /// <summary>A ↔ B: iki SDK-style proje birbirine <c>ProjectReference</c> verir — tek SCC. <see cref="WriteWorkspace"/>
    /// ile aynı üslup; tek farkı A'nın B'ye de referans vermesi.</summary>
    private static void WriteCycleWorkspace(GitTestRepo repo)
    {
        repo.WriteFile(Path.Combine("src", "A", "A.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>A</AssemblyName>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup>"
            + "<ItemGroup><ProjectReference Include=\"..\\B\\B.csproj\" /></ItemGroup></Project>");
        repo.WriteFile(Path.Combine("src", "A", "A.cs"), "public class A { }");
        repo.WriteFile(Path.Combine("src", "B", "B.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>B</AssemblyName>"
            + "<TargetFramework>net10.0</TargetFramework></PropertyGroup>"
            + "<ItemGroup><ProjectReference Include=\"..\\A\\A.csproj\" /></ItemGroup></Project>");
        repo.WriteFile(Path.Combine("src", "B", "B.cs"), "public class B { }");
        repo.WriteFile(SlnName,
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"src\\A\\A.csproj\", \"{1}\"\nEndProject\n"
            + "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"B\", \"src\\B\\B.csproj\", \"{2}\"\nEndProject\n");
    }
```

Test (sınıfın sonuna):

```csharp
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — Build cycle derler]</b> Sync'in önizlemesi bir sonraki DÜZ Build'i anlatır. Eski iddia: düz
    /// Build bir SCC'yi asla derlemediği için kapı sabit KAPALIYDI (<c>buildCycles: false</c>) ve cycle üyesi her
    /// zaman <c>WillBuild=false</c> gelirdi — idle will-dot hiç yanmaz, "N to build" üyeyi saymazdı.
    /// Değişme gerekçesi (ölçüm, 2026-10-07 13:17 koşusu): pull ile değişen iki Types cycle üyesi Build'de atlandı,
    /// bağımlıları paylaşılan klasördeki eski DLL'e karşı derlenip CS1061 verdi; satırda ve logda sebep görünmedi.
    /// Build artık kirli grupları derler (ARCHITECTURE §8.1); kapı <see cref="CycleCompilation"/>'dan okunur.
    /// </summary>
    [Fact]
    public async Task A_dirty_cycle_member_reads_will_build_in_the_sync_preview()
    {
        using var repo = new GitTestRepo();
        WriteCycleWorkspace(repo);
        repo.CommitAll("cycle");
        string cacheRoot = NewCacheRoot();
        try
        {
            var events = await SyncWithoutFetchAsync(repo, cacheRoot);

            var topology = Assert.Single(events.OfType<WorkspaceTopologyEvent>());
            Assert.Single(topology.Cycles);                                   // A ↔ B gerçekten bir SCC
            Assert.All(topology.Nodes, n => Assert.True(n.InCycle));
            Assert.All(topology.Nodes, n => Assert.True(n.WillBuild));        // hiç derlenmemiş → derlenecek
            var preview = Assert.Single(events.OfType<BuildPreviewEvent>());
            Assert.All(preview.Items, i => Assert.True(i.WillBuild));
            var done = Assert.IsType<SyncCompletedEvent>(events[^1]);
            Assert.Equal(1, done.CycleCount);
            Assert.Equal(2, done.ToBuildCount);
        }
        finally { Directory.Delete(cacheRoot, recursive: true); }
    }
```

- [ ] **Step 6: Kırmızıyı gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~A_dirty_cycle_member_reads_will_build_in_the_sync_preview"`
Expected: FAIL — `Assert.All ... WillBuild` (False).

- [ ] **Step 7: Sync kapısını aç**

`SyncWorkspaceService.cs` ~310-323: yorum bloğunu ve iki `Bind` çağrısını değiştir (dosyada `using BuildOrchestrator.Contracts.Ipc;` yoksa ekle):

```csharp
            // Idle'daki will-dot'ların kaynağı BURASIDIR ve onlar bir sonraki DÜZ Build'i tarif eder. Build kirli SCC
            // gruplarını da derlediği için kapı Build'in kendi kararını okur (CycleCompilation — tek kaynak): kirli
            // cycle üyesi "derlenecek" gelir, bileşik imzası temiz grup bütünüyle "güncel". Cycles koşusu kendi
            // önizlemesini kendi başlangıcında yayınlar — Sync burada onun adına söz VERMEZ.
            // [Faz 3/Task 6 — karar "Sync'in Fast geçişi"] Çıktı kanıtı YALNIZ Safe geçişine girer — Build'in planı
            // da aynı kontrollerle bağlanır, dolayısıyla Sync'in WillBuild'i bir sonraki düz Build'in kararıdır.
            // Fast geçişi yalnız "N changed" sayacını besler (satırın modified ↔ affected cevabı ondan DEĞİL, aşağıda
            // içerik özetinden gelir) ve kanıtsız bağlanır: kanıtla bağlansaydı havuzdaki kopyası bozulmuş
            // (OutputReplaced) ya da kanıtı silinmiş (OutputMissing) bir proje dosyasına dokunulmadığı hâlde
            // "changed" sayılırdı.
            var checks = binder.ChecksFor(state);
            bool buildCycles = CycleCompilation.CompilesCycles(RunMode.Build);
            var (safePlan, _) = binder.Bind(state, buildCycles, DependentMode.Safe, checks);
            var (fastPlan, _) = binder.Bind(state, buildCycles, DependentMode.Fast);
```

- [ ] **Step 8: Yeşili gör; Sync sınıfının tamamını koştur**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~SyncWorkspaceServiceTests"`
Expected: yeni test PASS; sınıfın geri kalanı yeşil (döngüsüz fixture'lar etkilenmez). `PrimeBuildStateAsUpToDateAsync` içindeki `buildCycles: false` (satır ~323) test yardımcısıdır, dokunma.

- [ ] **Step 9: Supervisor planı aynı kararı okur**

`Program.cs` ~204:

```csharp
            var (bound, signatures, memberTerms) = binder.Bind(state, CycleCompilation.CompilesCycles(cmd.Mode), cmd.DependentMode, checks);
```

~72-73 yorumunu değiştir: `// kapısı yok; yalnız Cycles modu Bind'a bileşik imza bayrağını geçirir.` → `// kapısı yok; Bind'ın "SCC derler mi" bayrağı CycleCompilation'dan okunur (Clean dışında her mod).`

Bu satırın birim testi yoktur (koordinatör testleri planı doğrudan enjekte eder); Task 6'daki gerçek OSYS ölçümü canlı kanıtıdır. `grep -rn "ComputeIncremental" tests/` boş dönmeli; dönerse o test de yeni kurala göre güncellenir.

- [ ] **Step 10: Kırmızı testler — `NextPreviewTests`**

`tests/BuildOrchestrator.Tests/Planning/NextPreviewTests.cs` üç yerde değişir (metodları yeniden yaz, doc'una eski iddia + gerekçeyi ekle):

1. `a_converged_cycle_member_with_a_dep_issue_waits_without_being_conditional` (satır ~34): beklenti `(false, WillBuildReason.WaitingForDependency, false)` → `(true, WillBuildReason.WaitingForDependency, false)`. Doc: eski iddia "bir sonraki Sync üyeyi buildCycles:false ile değerlendirir, WillBuild=false zorlanır"; yeni: Sync üyeyi Build'in kararıyla değerlendirir, kirli üye true, koşullu değil (grup `ConditionalRebuild.GroupAppliesTo` ile grup düzeyinde karar alır).
2. `an_untrusted_cycle_member_reads_what_the_invalidated_ledger_will_say` (satır ~46): `AfterSuccess(inCycle: true, trusted: false, ...)` için `WillBuild` beklentisi `false` → `true`; satır ~55'teki çapraz kontrol `WillBuildEvaluator.EvaluateWithReason(inCycle: true, "sig", invalidated, buildCycles: false)` → `buildCycles: CycleCompilation.CompilesCycles(RunMode.Build)` ve beklenti `true`.
3. `a_clean_result_reads_what_the_next_sync_says_for_its_ledger_row(bool inCycle)` (satır ~86): `willBuild` beklentisi `!inCycle` → her iki `InlineData` için `true`.

- [ ] **Step 11: Kırmızıyı gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~NextPreviewTests"`
Expected: 3 (ya da 4, theory iki satır) FAIL.

- [ ] **Step 12: `NextPreview` yeni kural**

`NextPreview.cs` (`using BuildOrchestrator.Contracts.Ipc;` ekle):

```csharp
    public static (bool WillBuild, WillBuildReason Reason, bool Conditional) AfterSuccess(
        bool inCycle, bool trusted, IReadOnlyList<string>? depIssues)
    {
        // Güvenilmez başarı: defter kanıtsız hata yazdı (NeverBuilt); bir sonraki düz Build kirli grubu derlediği
        // için üye de "derlenecek" okur — düz projeyle aynı.
        if (!trusted) return (true, WillBuildReason.NeverBuilt, false);
        if (depIssues is not { Count: > 0 }) return (false, WillBuildReason.UpToDate, false);
        // Dep-issue'lu üye: Sync onu Build'in kararıyla (buildCycles açık) WaitingForDependency + WillBuild=true okur.
        // Koşullu DEĞİLDİR: ConditionalRebuild.AppliesTo grup üyesini dışlar, grubun kaderine GroupAppliesTo
        // dispatch anında grup düzeyinde karar verir.
        return (true, WillBuildReason.WaitingForDependency, !inCycle);
    }
```

```csharp
    public static (bool WillBuild, WillBuildReason Reason, bool Conditional) AfterClean(bool inCycle) =>
        (!WillBuildEvaluator.OutOfScope(inCycle, CycleCompilation.CompilesCycles(RunMode.Build)),
         WillBuildReason.NeverBuilt, false);
```

`AfterSuccess` ve `AfterClean` XML doc'larındaki "döngü üyesi bir sonraki Sync'in kapsamı dışında olduğu için WillBuild=false" cümlelerini yeni kurala göre yeniden yaz (Sync kirli üyeyi Build'in kararıyla değerlendirir; üye tek başına koşullu değildir, grup `GroupAppliesTo` ile karar alır).

- [ ] **Step 12b: `NextPreview`'u App üzerinden pinleyen testler (aynı task'ta, yoksa App klasörü kırmızı kalır)**

Bu testler satırın canlı bayrağını `NextPreview` üzerinden okur; Adım 12'den sonra kırmızıya düşer ve yeni kuralı pinleyecek biçimde yeniden yazılır (doc'larına eski iddia + gerekçe):
- `App/RunViewModelTests.cs` ~1898 `A_converged_cycle_member_success_with_a_dep_issue_waits_without_being_conditional`: `row.WillBuild` beklentisi `false` → `true`; `Conditional` `false` kalır; `WillBuildReason` `WaitingForDependency` kalır.
- `App/RunViewModelTests.cs` ~1928 `A_converged_cycle_member_wait_label_survives_a_sync_without_flipping`: fixture'daki Sync önizlemesi (`BuildPreviewEvent`) üyeye `WillBuild: true` yazar (Task 1 Sync kuralı); etiket iddiası ("up to date" sözcüğü, flip yok) aynen kalır.
- `App/RunViewModelTests.cs` ~1969 `An_untrusted_cycle_member_success_reads_never_built_like_the_next_sync`: `row.WillBuild` `false` → `true`; gerekçe `NeverBuilt` kalır.
- `App/ChoreographyTests.cs` ~774 `After_a_full_clean_the_build_wave_lights_the_cleaned_projects_but_not_the_cycle_members` → ad `After_a_full_clean_the_build_wave_lights_the_cleaned_projects_cycle_members_included`; iddia: temizlenen döngü üyeleri de `ScopeFor(RunMode.Build)` içindedir (`AfterClean` → `true`).

- [ ] **Step 13: Yeşili gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~NextPreviewTests|FullyQualifiedName~CycleCompilationTests|FullyQualifiedName~SyncWorkspaceServiceTests|FullyQualifiedName~RunViewModelTests|FullyQualifiedName~ChoreographyTests"`
Expected: hepsi PASS. (`RunViewModelTests.Rebuild_wires_through_the_real_engine_and_populates_rows` bu task'ta hâlâ yeşildir — motor Task 2'ye kadar değişmez.)

- [ ] **Step 14: Kod içi doc'lar**

Eski kuralı söyleyen XML yorumlarını yeniden yaz (davranış değişmez, yalnız metin):
- `WillBuildEvaluator.cs` sınıf özeti "SCC üyeleri ve buildCycles" paragrafı: "Bir SCC'yi derleyen tek koşu RunMode.Cycles'tır; Build/Rebuild üyeleri 'in dependency cycle' ile atlar" → "SCC'yi derleyen koşular CycleCompilation.CompilesCycles'ın true dediği modlardır (Build, Rebuild, Cycles); Clean ve kapsamlı koşu derlemez. Bayrak kullanıcı tercihi DEĞİL, koşunun kapsamının yansımasıdır: Sync'in önizlemesi ve Build'in planı CompilesCycles(Build) geçer...". `Evaluate` param doc'undaki "(yalnız RunMode.Cycles)" ve `OutOfScope` doc'undaki "(bir SCC'yi yalnız RunMode.Cycles derler)" ifadeleri aynı şekilde.
- `IncrementalPlanner.cs:99` ve `IncrementalRunBinder.cs:117` param doc'ları: "yalnız RunMode.Cycles'ta true" → "CycleCompilation.CompilesCycles(mode); Sync Build'in değerini geçer".
- `IncrementalPlanner.cs` ~209-211 "[Task 11] kill switch'inin (buildCycles) işidir — kapalıyken hiç derlenmezler" → "üyelerin derlenip derlenmediği buildCycles bayrağının işidir (CycleCompilation); kapalıyken (Clean, testler) hiç derlenmezler".
- `BuildPreview.cs` ~70 yorumu "Kapsam dışı döngü üyesi yine derlenmez" doğru kalır (bayrak kapalıysa); dokunma.

- [ ] **Step 15: Commit**

```bash
git add src/BuildOrchestrator.Core/Planning/CycleCompilation.cs tests/BuildOrchestrator.Tests/Planning/CycleCompilationTests.cs \
  src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs src/BuildOrchestrator.Supervisor/Program.cs \
  src/BuildOrchestrator.Core/Planning/NextPreview.cs src/BuildOrchestrator.Core/Planning/WillBuildEvaluator.cs \
  src/BuildOrchestrator.Core/Incremental/IncrementalPlanner.cs src/BuildOrchestrator.Core/Incremental/IncrementalRunBinder.cs \
  tests/BuildOrchestrator.Tests/Workspace/SyncWorkspaceServiceTests.cs tests/BuildOrchestrator.Tests/Planning/NextPreviewTests.cs \
  tests/BuildOrchestrator.Tests/App/RunViewModelTests.cs tests/BuildOrchestrator.Tests/App/ChoreographyTests.cs
git commit -m "feat(core): SCC derleyen mod karari tek yerde (CycleCompilation); Sync onizlemesi ve NextPreview kirli cycle uyesini derlenecek sayar"
```

---

### Task 2: Koordinatör — Build/Rebuild kirli grupları turlarla derler

**Files:**
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (~713-720, ~766-779, ~800-852)
- Modify (doc): `src/BuildOrchestrator.Core/Scheduling/ReadySetScheduler.cs` (~55-70 ctor doc, ~17-18 sınıf doc), `src/BuildOrchestrator.Core/Planning/CycleRunScope.cs` (sınıf doc "Neden downstream kapsama GİRMEZ" paragrafı)
- Test: `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs`, `tests/BuildOrchestrator.Tests/Supervisor/RunCoordinatorTests.cs` (yalnız `Start` doc'u), Ek B'deki Supervisor testleri

**Interfaces:**
- Consumes: `CycleCompilation.CompilesCycles(RunMode)` (Task 1).
- Produces: Build/Rebuild koşusunda `CycleRoundStartedEvent`/`CycleCompletedEvent` yayılır; `ProjectSkippedEvent` cycle üyesi için yalnız `SkipReasons.UpToDate` (grup temizse) taşır. Task 4 (App) buna güvenir.

Harness notları (hepsi `RunCoordinatorTests` içinde, `using static` ile CycleRoundsTests'te görünür): `Node(name, deps, inCycle, willBuild)`, `CyclePlanOf(string[] cycle, params ProjectNode[] nodes)`, `Start(mode, parallelism, runId)`, `Ok()`, `Exit(code)`, `Id(name)`, `LogTextsFor(h, name)`, `Limit`, `new Harness(plan, invoker, stateStore:, apiSurface:)`, `h.Events`, `h.Sut.RunCompletion`. CycleRoundsTests içinde: `RoundRecorder`, `TwoMemberCycle()`, `SeedGreen(store, name)`, `SurfaceDisk`, `HashModePlan(plan, names)`, `NewCacheRoot()`.

**Tuzak — `PlanOf` döngü listesini boş kurar.** `RunCoordinatorTests.PlanOf` planı `Cycles: []` ile kurar; `CycleGroups.From` yalnız `BuildPlan.Cycles`'ı okur. `PlanOf(...) + Node(inCycle: true)` fixture'ında `groups` yeni kuralda da null kalır ve scheduler üyeyi `in dependency cycle` ile pre-skip eder — test mekanik olarak yeşil kalır ama ESKİ kuralı pinler. Döngü sınayan her fixture `CyclePlanOf`/`CyclesPlanOf` kullanır; yeni test yazarken ve Adım 11'de yeniden yazarken buna bak.

- [ ] **Step 1: Kırmızı test — Build kirli grubu derler, bağımlı grubun ARDINDAN gelir**

`CycleRoundsTests.cs` bölüm "0) kapsam"daki `a_build_run_pre_skips_every_member_with_the_original_reason_and_runs_no_rounds` testini SİLMEDEN yeniden yaz (ad ve gövde değişir, doc eski iddiayı taşır):

```csharp
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — Build cycle derler.]</b> Eski iddia: "Build bir SCC'yi ASLA derlemez" — üyeler tek bir tur
    /// bile koşmadan <c>"in dependency cycle"</c> gerekçesiyle pre-skip edilirdi; turlar kendi moduna (Cycles)
    /// taşınmıştı, çünkü Build'e gömülü turlar ölçülmüş ve iki dakikalık bir Build on beş dakikaya çıkmıştı.
    /// <para><b>Değişme gerekçesi (ölçüm):</b> o ölçüm tur-öncesi kanıt mekanizmasından (<see cref="CycleMemberNeed"/>,
    /// yüzey kısa devresi) öncedir. 2026-10-07'de iki Cycles koşusunda her grup 1. turda yakınsadı; tur mekanizmasının
    /// kendi payı saniyelik hash'lerdir. Aynı gün 13:17'deki Build, pull ile değişen iki Types cycle üyesini atladı ve
    /// bağımlıları eski DLL'e karşı derlenip CS1061 verdi — satırda ve logda sebep yoktu (ARCHITECTURE §8.1).
    /// Build artık kirli grubu plandaki bir düğüm gibi turlarla derler; bağımlıları grubun bitmesini bekler. Build
    /// için yazılmış AYRI bir kod yolu yine YOKTUR: koordinatör scheduler'a aynı <c>CycleGroups</c> haritasını
    /// geçer (<see cref="CycleCompilation"/>), gerisi Cycles'ınkiyle birebir aynı tur döngüsüdür.</para>
    /// </summary>
    [Fact]
    public async Task a_build_run_compiles_a_dirty_cycle_in_rounds_and_its_dependent_after_the_group()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        // A ↔ B kirli grup; Z gruba bağımlı ve kirli → grup bittikten SONRA, taze çıktıya karşı derlenir.
        var plan = CyclePlanOf(["A", "B"],
            Node("A", deps: ["B"], inCycle: true, willBuild: true),
            Node("B", deps: ["A"], inCycle: true, willBuild: true),
            Node("Z", deps: ["A"], willBuild: true));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Build, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2", "Z#1"], rec.Calls); // kanıtsız grup: iki yeşil tur, sonra Z
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());         // "in dependency cycle" YOK
        Assert.Equal([1, 2], h.Events.OfType<CycleRoundStartedEvent>().Select(e => e.Round));
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal(CycleOutcome.Converged, completed.Outcome);
        Assert.Equal(3, h.Events.OfType<ProjectSucceededEvent>().Count());
        var done = Assert.Single(h.Events.OfType<RunCompletedEvent>());
        Assert.Equal(3, done.Succeeded);
        Assert.Equal(0, done.Skipped);
        // Önizleme koşunun GERÇEKTEN yapacağını gösterir: üyeler de dalgada yanar.
        var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>());
        Assert.All(preview.Items, i => Assert.True(i.WillBuild));
    }
```

- [ ] **Step 2: Kırmızıyı gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~a_build_run_compiles_a_dirty_cycle_in_rounds"`
Expected: FAIL — `rec.Calls` `["Z#1"]` ya da A/B için `ProjectSkippedEvent` (`in dependency cycle`).

- [ ] **Step 3: Kırmızı test — temiz grup Build'de `up to date` atlanır**

```csharp
    /// <summary>Build de grup düzeyinde INCREMENTAL'dır: bileşik imzası temiz grup (<c>WillBuild == false</c> gelen
    /// üyeler) tek tur bile koşmadan sıradan "güncel" skip'iyle atlanır; bağımlısı yine derlenir. Karar GRUP
    /// düzeyindedir (<c>All</c>) — Cycles koşusuyla aynı kapı, aynı kod.</summary>
    [Fact]
    public async Task a_build_run_skips_a_cycle_whose_composite_signature_is_clean_as_up_to_date()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((_, _) => Ok());
        var plan = CyclePlanOf(["A", "B"],
            Node("A", deps: ["B"], inCycle: true, willBuild: false),
            Node("B", deps: ["A"], inCycle: true, willBuild: false),
            Node("Z", deps: ["A"], willBuild: true));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Build, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["Z#1"], rec.Calls);                                 // grup HİÇ derlenmedi, Z derlendi
        var skipped = h.Events.OfType<ProjectSkippedEvent>().ToList();
        Assert.Equal([Id("A"), Id("B")], skipped.Select(e => e.ProjectId));
        Assert.All(skipped, e => Assert.Equal(SkipReasons.UpToDate, e.Reason));
        Assert.All(skipped, e => Assert.False(e.CycleUnconverged));
        Assert.Empty(h.Events.OfType<CycleRoundStartedEvent>());
    }
```

Run: `dotnet test ... --filter "FullyQualifiedName~a_build_run_skips_a_cycle_whose_composite_signature_is_clean"`
Expected: FAIL — gerekçe `in dependency cycle` (`UpToDate` değil).

- [ ] **Step 4: Kırmızı test — Build'de yüzey kanıtı tek turda yakınsar**

`a_green_group_whose_surfaces_did_not_change_converges_in_one_round` testinin (bölüm "API kısa devresi") birebir kopyasını Build moduyla ekle:

```csharp
    /// <summary>Yüzey kanıtı mod tanımaz: Build'in derlediği grup da kimsenin okuduğu yüzey değişmediyse TEK turda
    /// yakınsar, persist edilir ve güvenilir raporlanır (Cycles koşusundaki kardeş testle aynı sahne).</summary>
    [Fact]
    public async Task a_build_run_with_surface_evidence_converges_a_green_group_in_one_round()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            SeedGreen(store, "A");
            SeedGreen(store, "B");
            var disk = new SurfaceDisk();
            disk.Set("A", "a1");
            disk.Set("B", "b1");
            var plan = HashModePlan(TwoMemberCycle(), "A", "B");
            var rec = new RoundRecorder();
            var invoker = rec.Invoker((name, _) => { disk.Set(name, name == "A" ? "a1" : "b1"); return Ok(); });
            using var h = new Harness(plan, invoker, stateStore: store, apiSurface: disk.Read);

            await h.Sut.StartAsync(Start(RunMode.Build, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Equal(["A#1", "B#1"], rec.Calls);
            var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
            Assert.Equal(CycleOutcome.Converged, completed.Outcome);
            Assert.Equal(1, completed.Rounds);
            foreach (string name in new[] { "A", "B" })
            {
                Assert.Equal("sig", store.Load()[Id(name)].BuiltSignature);
                Assert.Equal(BuildResult.Succeeded, store.Load()[Id(name)].LastResult);
            }
            Assert.All(h.Events.OfType<ProjectSucceededEvent>(), e => Assert.True(e.Trusted));
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }
```

Run: `dotnet test ... --filter "FullyQualifiedName~a_build_run_with_surface_evidence_converges"`
Expected: FAIL (üyeler derlenmez).

- [ ] **Step 5: Koordinatör — grup haritası kapısı**

`RunCoordinator.cs` ~766-779: yorumu ve atamayı değiştir:

```csharp
            // [cycle rounds] SCC üyelik haritası TEK yerde kurulur ve HEM scheduler'a (grup dispatch'i) HEM run
            // context'ine (tur döngüsü) AYNI örnek verilir — ikisi ayrı From çağrısıyla kurulsaydı üye sırası
            // sessizce ayrışabilirdi. Plan'ın SON hâlinden (Clean ve kapsam daraltmasından sonra) kurulur.
            // [Build cycle derler] Harita, SCC derleyen her modda (CycleCompilation — tek kaynak) ve yalnız TAM
            // koşuda kurulur: satırdan tetiklenen tek-proje kapsamı hedefi düz düğüm olarak tek başına derler
            // (ProjectRunScope), orada grup yoktur. Plan'da hiç SCC yoksa null geçilir; scheduler o zaman InCycle
            // düğümü "in dependency cycle" ile pre-skip eder — üretimde bu dala düşen düğüm yoktur (planda SCC yoksa
            // InCycle düğüm de yoktur), dal kill-switch testlerinin yoludur. Modlar için yazılmış ayrı bir kod yolu
            // yoktur: Cycles ile Build arasındaki tek fark aşağıdaki KAPSAM tohumudur.
            groups = cmd.ScopeProjectId is null && CycleCompilation.CompilesCycles(cmd.Mode)
                && CycleGroups.From(runPlan.Plan) is { Count: > 0 } withCycles
                ? withCycles
                : null;
```

- [ ] **Step 6: Koordinatör — tohum bloğu**

~800-852 arasını (`bool cyclesRun = ...` satırından `foreach (var n in runPlan.Plan.Nodes) { ... }` döngüsünün kapanışına kadar) şu yapıya getir. Yakınsamama-hafızası bloğunun GÖVDESİ (cycleState yükleme, `SignatureRepresentative`, `IsCycleNonConvergent`, `Decide(... Retrying ...)`) ve "SCC'ler de incremental olur" yorumu AYNEN taşınır; yalnız kapılar değişir:

```csharp
            bool cyclesRun = cmd.Mode == RunMode.Cycles;
            var cycleScope = cyclesRun ? CycleRunScope.Of(runPlan.Plan) : null;
            if (cmd.Mode == RunMode.Build || cyclesRun)
            {
                // Grup düzeyinde "güncel" bulunan SCC üyeleri — aşağıdaki tek pre-skip döngüsünün cycle üyelerine
                // açtığı KAPI. Harita varsa (Build ve Cycles) dolar; Rebuild bu bloğa hiç girmez (tohum yok, her
                // grup dispatch edilir, tur 1 her üyeyi derler — RunCycleGroupAsync).
                var cycleUpToDate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (groups is not null)
                {
                    // [Task 7 · DEĞİŞEN KURAL] Yakınsamama hafızası BLOKLAMAZ, yalnız RAPORLAR — ve grup derleyen her
                    // koşuda okunur (Build de artık gruba tur 1'den girer). Gerekçe: ... (mevcut yorumun geri kalanı,
                    // "tek yol Resolve basışı" cümlesi "Build ya da Resolve basışı" olarak düzeltilir) ...
                    if (stateStore is not null && runPlan.Incremental is { } inc)
                    {
                        // mevcut gövde AYNEN
                    }
                    // mevcut "SCC'ler de incremental olur" yorumu + willBuildById / cycle.All döngüsü AYNEN
                }
                foreach (var n in runPlan.Plan.Nodes)
                {
                    // KAPSAM kapısı yalnız Cycles'ındır: kapsam dışı kalan BURADA tohumlanır ve kendi gerekçesiyle
                    // raporlanır. Build'in kapsamı tüm plandır.
                    if (cyclesRun && !cycleScope!.Contains(n.Id))
                    {
                        schedulerSeed[n.Id] = BuildResult.Skipped;
                        upToDateSkips.Add((n.Id, SkipReasons.OutOfCycleScope, CycleUnconverged: false));
                        continue;
                    }
                    // Harita yokken InCycle düğüm tohumlanmaz: ReadySetScheduler onu kendi "in dependency cycle"
                    // gerekçesiyle pre-skip eder (planda SCC varken harita hep vardır — savunmacı dal).
                    if (n.InCycle && groups is null) continue;
                    // Cycle üyesi buraya YALNIZ grup kapısından geçtiyse gelir: tekil WillBuild bir SCC üyesini
                    // TEK BAŞINA temsil etmez (bileşik imza gruba aittir). Kapsamdaki upstream sıradan projedir
                    // ve bu kapıya hiç uğramaz — kendi WillBuild'i onu temsil eder.
                    if (n.InCycle && !cycleUpToDate.Contains(n.Id)) continue;
                    if (n.WillBuild != false) continue;
                    schedulerSeed[n.Id] = BuildResult.Skipped;
                    upToDateSkips.Add((n.Id, SkipReasons.UpToDate, CycleUnconverged: false));
                }
            }
```

~713-720'deki tohum yorumunu da düzelt: "Build'de 'up to date'; Cycles'ta kapsam içi 'up to date' (grup düzeyinde güncel SCC dahil) ve kapsam dışı" → "Build'de 'up to date' (grup düzeyinde güncel SCC dahil); Cycles'ta ayrıca kapsam dışı (OutOfCycleScope). Rebuild/Clean'de BOŞ kalır."

- [ ] **Step 7: Üç testi yeşil gör**

Run: `dotnet test ... --filter "FullyQualifiedName~a_build_run_compiles_a_dirty_cycle_in_rounds|FullyQualifiedName~a_build_run_skips_a_cycle_whose_composite|FullyQualifiedName~a_build_run_with_surface_evidence_converges"`
Expected: 3 PASS.

- [ ] **Step 8: Kırmızı→yeşil — yakınsamayan grup, bağımlı dep-issue ile derlenir (Review Focus 3)**

```csharp
    /// <summary>Build'de yakınsamayan grup bağımlısını BLOKLAMAZ: Z, patlayan üyenin son başarılı çıktısına karşı
    /// derlenir ve bunu logunun başında söyler (§8.3) — sessiz bayat referans YOK. Cycles koşusundaki NoProgress
    /// kuralı aynen: aynı küme iki turdur patlıyor, üçüncü tur yok.</summary>
    [Fact]
    public async Task a_build_run_with_a_no_progress_group_still_builds_the_dependent_with_a_dependency_issue()
    {
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok());
        var plan = CyclePlanOf(["A", "B"],
            Node("A", deps: ["B"], inCycle: true, willBuild: true),
            Node("B", deps: ["A"], inCycle: true, willBuild: true),
            Node("Z", deps: ["B"], willBuild: true));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(RunMode.Build, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1", "A#2", "B#2", "Z#1"], rec.Calls);
        var failed = Assert.Single(h.Events.OfType<ProjectFailedEvent>());
        Assert.Equal(Id("B"), failed.ProjectId);
        var z = Assert.Single(h.Events.OfType<ProjectSucceededEvent>(), e => e.ProjectId == Id("Z"));
        Assert.NotEmpty(z.DepIssues ?? []);
        Assert.Contains("warning: B failed in this run — last successful output referenced (B)", LogTextsFor(h, "Z"));
    }
```

Run: filtre `a_build_run_with_a_no_progress_group`. Expected: PASS (Adım 5-6 sonrası). Eğer `DepIssues` adı/tipi farklıysa `Contracts/Ipc/IpcMessages.cs`'teki `ProjectSucceededEvent` tanımına bak; iddia "Z dep-issue taşır" aynı kalır.

- [ ] **Step 9: Tek-proje kapsamı korunur (Review Focus 1)**

`tests/BuildOrchestrator.Tests/Supervisor/SingleProjectRunTests.cs` içinde cycle üyesini satırdan derleyen test(ler)i (grep: `inCycle: true` ve `ScopeProjectId`) koştur; `warning: ... is in a dependency cycle and was not rebuilt — last known output referenced` pinleri yeşil kalmalı (kapı `cmd.ScopeProjectId is null`). Kırılırsa kapı yanlış yazılmıştır; testi değil kapıyı düzelt.

Run: `dotnet test ... --filter "FullyQualifiedName~SingleProjectRunTests"`
Expected: PASS.

- [ ] **Step 10: Stop iki modda (Review Focus 4)**

`CycleRoundsTests.stopped_group_invalidates_every_member` testini gövdesine dokunmadan Theory yap: `[Fact]` → `[Theory] [InlineData(RunMode.Cycles)] [InlineData(RunMode.Build)]`, imzaya `RunMode mode`, içindeki `Start(RunMode.Cycles, ...)` → `Start(mode, ...)`. Doc'una bir cümle: "Stop semantiği moda bağlı değildir — Build'in grubu da aynı yoldan kesilir."

Run: `dotnet test ... --filter "FullyQualifiedName~stopped_group_invalidates_every_member"`
Expected: 2 PASS.

- [ ] **Step 11: Supervisor klasörünün tamamı; Rebuild varsayılanından düşenleri yeniden yaz**

`RunCoordinatorTests.Start(...)` varsayılan modu `Rebuild`'dir ve Rebuild artık grup derler: `inCycle: true` düğüm içeren, Build/Rebuild ile koşan ve pre-skip bekleyen her test yeni kurala göre yeniden yazılır (üyeler grup olarak derlenir, `ProjectSkippedEvent` yok ya da `UpToDate`). Önce `Start`'ın doc'unu düzelt:

```csharp
    /// <summary>Koordinatörün gördüğü komut. Varsayılan <see cref="RunMode.Rebuild"/> önbelleği yok sayar ve — SCC
    /// derleyen her mod gibi (<see cref="Core.Planning.CycleCompilation"/>) — plandaki döngü gruplarını turlarla
    /// derler; cycle'sız planlarda bugünkü davranış birebir aynıdır. Kapsam (upstream'li dar koşu) sınayan testler
    /// <see cref="RunMode.Cycles"/>'ı AÇIKÇA geçer.</summary>
```

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~BuildOrchestrator.Tests.Supervisor"`
Düşen her test için: gövdesi `inCycle: true` + Build/Rebuild + `SkipReasons.InDependencyCycle` beklentisi taşıyorsa beklenen kırmızıdır → testi yeni kuralı pinleyecek biçimde yeniden yaz (doc'una eski iddia + bu planın gerekçesi). Başka bir sebeple düşen test KOD hatasıdır; kodu düzelt. Klasör tamamen yeşil olmadan ilerleme. Bilinen liste:

- `RunCoordinatorTests.cs` ~1125 `cycle_members_are_pre_skipped_again_by_every_fresh_run`: `PlanOf` yerine `CyclePlanOf` (tuzak); yeni iddia: X↔Y her taze koşuda (varsayılan Rebuild) yeniden derlenir — iki koşuda da `rec`/invoker çağrısı var, `ProjectSkippedEvent` yok. Ad: `cycle_members_are_compiled_again_by_every_fresh_rebuild`.
- `RunCoordinatorTests.cs` ~1495 `a_skipped_dependency_produces_no_dep_issue_for_its_dependent`: "atlanan bağımlılık" artık döngüyle kurulmaz; `Start(RunMode.Build)` + `Node("X", willBuild: false)` (up-to-date tohumu) ile kur, iddia aynı kalır (Z tek başarı, dep-issue yok).
- `ConditionalRebuildRunTests.cs` ~272 `A_still_failing_root_that_never_ran_this_run_is_labelled_from_its_last_known_result`: Up↔Loop SCC'si Build'de artık dispatch edilir; "kök bu koşuda hiç koşmadı" sahnesini korumak için grubu temiz kur (`Node("Up", ..., inCycle: true, willBuild: false)`, `Node("Loop", ..., inCycle: true, willBuild: false)` ve plan `CyclePlanOf`) — grup `UpToDate` ile tohumlanır, Down'un etiketi `DependencyStillFailing "(Up (last known failure))"` aynen pinlenir.
- `App/RunViewModelTests.cs` ~1306 `Rebuild_wires_through_the_real_engine_and_populates_rows` (gerçek Supervisor + gerçek MSBuild, diskte X↔Y legacy csproj): Rebuild artık grubu turlarla derler. İddiayı "kablolama"ya indir: koşu `Completed` biter, iki satır da Pending'den çıkar (Succeeded ya da Failed — fixture'ın MSBuild'de derlenip derlenmediğine göre; `RunCoordinatorTests` ~1169'daki Cycles'lı gerçek-process testi aynı fixture'ı zaten koşuyor, ondan beklentiyi al), `done.Skipped == 0`. `Supervisor/SupervisorIpcTests.cs` ~27-39 `TestPaths.WideRunTimeout` yorumundaki "hiçbir MSBuild child'ı doğmaz" cümlesini düzelt ve süre yetmiyorsa Cycles'lı kardeş testin bütçesine hizala.
- Yalnız doc/yorum: `CycleRoundsTests` ~821 (ad "Build" ama iki koşu Cycles — adı `..._by_the_next_run_...` yap), ~963-973 yorumu ("Rebuild artık bir SCC'ye hiç dokunmaz" → "Rebuild grubun her üyesini tur 1'de derler"), `RunCoordinatorTests` ~1192-1194, ~1294-1295 (`RunTwiceWithPackagesInPlace`), ~2451-2452 (`ResolvePlan`), `SingleProjectRunTests` ~142, `FullCleanRunTests` ~24-25 (`GraphWithExternalAndCycle`).
- `CycleRoundsTests` ~1016-1026'daki silinmiş-test notu (`convergence_clears_the_memory_even_for_a_member_whose_success_carries_a_dep_issue`): gerekçesi "üye depIssue taşıyamaz" idi; Build'de grubun upstream'i aynı koşuda patlayabileceği için senaryo yeniden üretilebilir. Notu güncelle; testi yalnız senaryoyu harness'ta kurabiliyorsan geri getir (zorunlu değil).

- [ ] **Step 12: Kod içi doc'lar**

- `ReadySetScheduler.cs` ~55-70: "Build tohumu SCC üyelerini BİLEREK hiç taşımaz, bu yüzden bu savunmacı bir edge case DEĞİL, Build'in NORMAL yoludur" → "Üretimde SCC derleyen her mod haritayı geçer; haritasız dal yalnız SCC'siz planın (orada InCycle düğüm yoktur) ve kill-switch testlerinin yoludur." ~17-18 sınıf doc'u aynı yönde.
- `CycleRunScope.cs` "Neden downstream kapsama GİRMEZ" paragrafı: "kullanıcı Cycles'ı Build'den ÖNCE çalıştırır ve dependent'leri zaten Build derler" → "Resolve cycles dar kapsamlı, isteğe bağlı bir koşudur: yalnız cycle'ları ve onların bayat upstream'ini derler. Build ise kirli grupları da, bağımlılarını da derler; downstream'i buraya almak düğmenin tek anlamını (ne kadar ödediğini bilerek yalnız cycle'ı ödemek) ortadan kaldırırdı."

- [ ] **Step 13: Commit**

```bash
git add src/BuildOrchestrator.Supervisor/RunCoordinator.cs src/BuildOrchestrator.Core/Scheduling/ReadySetScheduler.cs \
  src/BuildOrchestrator.Core/Planning/CycleRunScope.cs tests/BuildOrchestrator.Tests/Supervisor/
git commit -m "feat(supervisor): Build ve Rebuild kirli cycle gruplarini turlarla derler; temiz grup up to date atlanir"
```

---

### Task 3: Rebuild tur 1 her üyeyi derler

**Files:**
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (~1731-1750, `RunCycleGroupAsync` içindeki tur-1 kararı)
- Modify: `src/BuildOrchestrator.Core/Planning/CycleDecisionLines.cs` (yeni satır)
- Test: `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs` (bölüm 14 fixture'ları: `MemberSkipPlan`, `TwoMembers`, `IntactOutput`), `tests/BuildOrchestrator.Tests/Supervisor/CycleDecisionLogTests.cs`

**Interfaces:**
- Produces: `CycleDecisionLines.RebuildCompilesEveryMember(string group) => "cycle {group}: rebuild — every member compiles in round one"`.

- [ ] **Step 1: Kırmızı test — Rebuild güvenilir kaydı olan üyeyi de derler**

Bölüm 14'teki "taşınan üye" sahnesini bul (`MemberSkipPlan`/`TwoMembers` kullanan, Cycles koşusunda bir üyenin `carried` çıktığı ilk test; adı `carried` ya da `round 1 ... only` içerir). Aynı kurulumun (aynı defter kayıtları, aynı sahte disk, aynı plan) kopyasını Rebuild ile ekle; tek fark mod ve beklenti:

```csharp
    /// <summary>Rebuild önbelleği yok sayar: güvenilir kaydı olan, terimi değişmemiş üye de tur 1'de derlenir
    /// (<see cref="CycleMemberNeed"/> sorulmaz). Yüzey kanıtı yine okunur: yüzeyler oturmuşsa grup tek turda yakınsar.
    /// Aynı sahne Cycles/Build'de üyeyi taşır (yukarıdaki kardeş test); burada herkes invoke edilir.</summary>
    [Fact]
    public async Task a_rebuild_compiles_every_member_in_round_one_even_with_trusted_records()
    {
        // <kardeş testin Arrange bloğu AYNEN: store/SeedTrusted, disk, plan = TwoMembers(...), rec, invoker, Harness>

        await h.Sut.StartAsync(Start(RunMode.Rebuild, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A#1", "B#1"], rec.Calls);                          // kimse taşınmadı
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal(CycleOutcome.Converged, completed.Outcome);
        Assert.Equal(1, completed.Rounds);
    }
```

Arrange bloğunu kardeş testten kelimesi kelimesine kopyala (defter kaydını trusted yapan yardımcı o testte hangisiyse onu kullan). Kardeş test Cycles'ta `["B#1"]` gibi kısmi bir liste bekliyorsa Rebuild'de tam liste beklenir.

Run: `dotnet test ... --filter "FullyQualifiedName~a_rebuild_compiles_every_member_in_round_one"`
Expected: FAIL — Rebuild de üyeyi taşır (kısmi liste).

- [ ] **Step 2: Kırmızı test — decision.log satırı**

`CycleDecisionLogTests`'e, mevcut bir tur-izi testinin kalıbıyla (Harness + `FindLine`/`IndexOf`), Rebuild koşusunda `CycleDecisionLines.RebuildCompilesEveryMember(<grup adı>)` satırının grup başlığından SONRA ve ilk tur satırından ÖNCE geldiğini pinleyen bir test ekle. Grup adı mevcut testlerde `GroupStarted` için nasıl kuruluyorsa aynı (`CycleGroupName`/lider adı). Dosyadaki `RunCyclesAsync` yardımcısı (~44) modu `RunMode.Cycles` sabit kodlar: `RunMode mode = RunMode.Cycles` parametresi ekle, mevcut çağrılar değişmez; yeni test `RunMode.Rebuild` geçer.

- [ ] **Step 3: Kırmızıyı gör**

Run: `dotnet test ... --filter "FullyQualifiedName~CycleDecisionLogTests"` → yeni test FAIL (satır yok).

- [ ] **Step 4: Satır + koordinatör kuralı**

`CycleDecisionLines.cs` (`Retrying`'in yanına):

```csharp
    /// <summary>[Build cycle derler] Rebuild önbelleği yok sayar: grubun her üyesi tur 1'de derlenir; yüzey kanıtı
    /// yalnız sonraki turların kararı için okunur.</summary>
    public static string RebuildCompilesEveryMember(string group) =>
        string.Format(CultureInfo.InvariantCulture, "cycle {0}: rebuild — every member compiles in round one", group);
```

`RunCoordinator.cs` ~1737 (`if (hashMode && run.Incremental is { MemberTermById: { } memberTerms } incremental)`):

```csharp
            // [Build cycle derler] Rebuild "önbelleği yok say"dır: üye ihtiyacı sorulmaz, herkes tur 1'de derlenir.
            // hashMode DOKUNULMAZ — tur sonu bayatlık kararı yine kanıtla verilir (Fast'teki "terim yok" dalıyla aynı
            // sonuç, ama sebebi açıkça yazılır).
            if (run.Mode == RunMode.Rebuild)
                Decide(run.Logs, CycleDecisionLines.RebuildCompilesEveryMember(group));
            else if (hashMode && run.Incremental is { MemberTermById: { } memberTerms } incremental)
            {
                // mevcut gövde AYNEN
            }
```

(`group` bu kapsamda `GroupStarted` için zaten kurulu olan grup adı değişkenidir; adı farklıysa onu kullan.)

- [ ] **Step 5: Yeşili gör**

Run: `dotnet test ... --filter "FullyQualifiedName~CycleRoundsTests|FullyQualifiedName~CycleDecisionLogTests"`
Expected: hepsi PASS.

- [ ] **Step 6: Commit**

```bash
git add src/BuildOrchestrator.Supervisor/RunCoordinator.cs src/BuildOrchestrator.Core/Planning/CycleDecisionLines.cs tests/BuildOrchestrator.Tests/Supervisor/
git commit -m "feat(supervisor): Rebuild cycle grubunun her uyesini tur 1'de derler, decision.log bunu yazar"
```

---

### Task 4: App — şerit, akış, ETA, kapsam, etiketler

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RibbonText.cs` (~164-186)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` (~76-84, `case CycleCompletedEvent` ~288-298, `case RunCompletedEvent` ~332-352)
- Modify: `src/BuildOrchestrator.App/ViewModels/StreamText.cs` (`CyclesHint`, `CleanedCyclesHint` ~198-212)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (`ScopeFor` ~1098-1118, `UpdateEta` ~2455-2480)
- Modify: `src/BuildOrchestrator.App/ViewModels/DecisionLabel.cs` (~82-85, ~99-103) + `ViewModels/RunViewModel.Workspace.cs:787`, `Views/ProjectRow.xaml.cs:545`
- Modify: `src/BuildOrchestrator.App/Console/ConsoleEmptyState.cs` (~88, ~121-124, ~209)
- Modify: `src/BuildOrchestrator.App/ViewModels/CycleText.cs` (`Membership`, `ClusterHeadline` silinir)
- Test: `App/RibbonTextTests.cs`, `App/EventStreamTests.cs`, `App/RunViewModelStateTests.cs`, `App/ChoreographyTests.cs`, `App/DecisionLabelTests.cs`, `App/ConsoleModesTests.cs`

**Interfaces:**
- Consumes: `CycleCompilation.CompilesCycles` (Task 1); Build'de `CycleRoundStartedEvent`/`CycleCompletedEvent` (Task 2).
- `DecisionLabel.For(bool? willBuild, WillBuildReason? reason, bool? ownFilesChanged, bool localEdits)` — `inCycle` parametresi kalkar.
- `RibbonText.Compose(... resolvingCycles, cycleRound, cycleRoundCap ...)` imzası aynı; kural değişir.

- [ ] **Step 1: Kırmızı test — şerit, Build'in turunu yazar**

`RibbonTextTests.cs`, `Resolving_cycles_line_shows_the_round_and_the_willbuild_progress`'in altına:

```csharp
    // [Build cycle derler] Tur satırı moda değil, uçuştaki tura bağlıdır: düz Build'in grubu turdayken de yazılır.
    [Fact]
    public void A_build_run_shows_the_round_line_while_one_of_its_cycle_groups_is_in_rounds()
    {
        var line = RibbonText.Compose(AppPhase.Running, true, allClean: false, Counters(building: 1, queued: 6),
            willBuild: 14, finishedOfWillBuild: 7, totalProjects: 14, elapsedMs: 24_000, etaMs: null, checkDurMs: null, warnings: 0,
            resolvingCycles: false, cycleRound: 1, cycleRoundCap: 3);
        Assert.Equal("▸ Resolving cycles · round 1/3 · 7/14 · 24s", line.Text);
        Assert.Equal("building", line.Glyph);
    }

    // "preparing dependencies" yalnız Resolve koşusunun turlar öncesi penceresidir; düz Build'de o pencere "Building"dir.
    [Fact]
    public void A_build_run_without_a_round_in_flight_reads_building()
    {
        var line = RibbonText.Compose(AppPhase.Running, true, allClean: false, Counters(building: 1, queued: 6),
            willBuild: 14, finishedOfWillBuild: 7, totalProjects: 14, elapsedMs: 24_000, etaMs: null, checkDurMs: null, warnings: 0,
            resolvingCycles: false, cycleRound: 0, cycleRoundCap: 0);
        Assert.StartsWith("▸ Building 7/14 · 24s", line.Text);
    }
```

Run: filtre `A_build_run_shows_the_round_line`. Expected: FAIL ("▸ Building ..." döner).

- [ ] **Step 2: `RibbonText` kuralı**

~174-186'daki `if (resolvingCycles) return ... cycleRound > 0 ? round : preparing` bloğunu şununla değiştir (yorum dahil):

```csharp
                // [Build cycle derler] Tur satırı moda değil UÇUŞTAKİ TURA bağlıdır: bir SCC grubu hangi düğmeyle başlamış
                // olursa olsun turlarını koşarken şerit turu yazar (cycleRound > 0; sayaçları CycleRoundStartedEvent
                // kurar, CycleCompletedEvent sıfırlar). "preparing dependencies" yalnız Resolve koşusunundur: o koşu
                // turlardan önce döngünün upstream'ini derler; düz Build'de aynı pencere sıradan "Building" satırıdır.
                // Sözcük motorunkidir ("round") — konsol ve event stream aynı kelimeyi kullanır.
                if (cycleRound > 0)
                    return new RibbonLine(
                        string.Format(CultureInfo.InvariantCulture,
                            "▸ Resolving cycles · round {0}/{1} · {2}/{3} · {4}",
                            cycleRound, cycleRoundCap, finishedOfWillBuild, willBuild,
                            DurationFormat.Elapsed(elapsedMs)),
                        "Brush.TextSecondary", "building");
                if (resolvingCycles)
                    return new RibbonLine(
                        string.Format(CultureInfo.InvariantCulture,
                            "▸ Resolving cycles · preparing dependencies · {0}/{1} · {2}",
                            finishedOfWillBuild, willBuild, DurationFormat.Elapsed(elapsedMs)),
                        "Brush.TextSecondary", "building");
```

Run: `dotnet test ... --filter "FullyQualifiedName~RibbonTextTests"` → hepsi PASS (mevcut iki Resolve pini de).

- [ ] **Step 3: Kırmızı test — grup bitince tur sayaçları sıfırlanır**

`RunViewModelStateTests.cs`'e (harness: `new EngineHost(TestPaths.SupervisorExe)`, `new RunViewModel(engine, NeverTickingBatcher(), () => "r1")`, `vm.OnEvent(...)`):

```csharp
    /// <summary>[Build cycle derler] Düz Build'de grup bitince sıradan projeler devam eder: şeridin tur satırı
    /// "Building"e dönmeli ve üye-detay kapısı kapanmalı. Sayaçlar yalnız EKRANDA YAZAN grubun (lider) bitişinde
    /// sıfırlanır — eşzamanlı ikinci bir grubun turu yerinde kalır.</summary>
    [Fact]
    public async Task A_cycle_completion_resets_the_round_counters_of_the_group_on_screen()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string leader = @"C:\p\A.csproj";
        vm.OnEvent(new WorkspaceTopologyEvent([Node(leader, "A", 0, inCycle: true), Node(@"C:\p\B.csproj", "B", 1, inCycle: true)],
            [[leader, @"C:\p\B.csproj"]], [], []));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 2, 1, "Debug"));
        vm.OnEvent(new CycleRoundStartedEvent("r1", leader, Round: 1, RoundCap: 3, MemberCount: 2));
        Assert.Equal(1, vm.CycleRound);

        // Başka bir grubun bitişi ekrandaki sayaçlara dokunmaz.
        vm.OnEvent(new CycleCompletedEvent("r1", @"C:\p\Other.csproj", CycleOutcome.Converged, MemberCount: 1, Rounds: 1, FailedCount: 0, DurationMs: 5, CompiledCount: 1));
        Assert.Equal(1, vm.CycleRound);

        vm.OnEvent(new CycleCompletedEvent("r1", leader, CycleOutcome.Converged, MemberCount: 2, Rounds: 1, FailedCount: 0, DurationMs: 9, CompiledCount: 2));
        Assert.Equal(0, vm.CycleRound);
        Assert.Equal(0, vm.CycleRoundCap);
    }
```

`WorkspaceTopologyEvent`'in ikinci argümanı döngü listesi (`IReadOnlyList<IReadOnlyList<string>>`) — ChoreographyTests'teki çağrı kalıbına (`new WorkspaceTopologyEvent([...], [], [], [])`) bak; `CycleCompletedEvent`'in parametre adlarını `Contracts/Ipc/IpcMessages.cs:540`'tan doğrula ve adlandırılmış argümanları ona göre yaz.

Run: filtre `A_cycle_completion_resets_the_round_counters`. Expected: FAIL (`CycleRound` 1 kalır).

- [ ] **Step 4: `CycleCompletedEvent` sıfırlar**

`RunViewModel.Stream.cs` `case CycleCompletedEvent e:` dalında `PushStream(...)`'den sonra:

```csharp
                // [Build cycle derler] Grup bitti: düz Build'de sıradan projeler devam eder — tur sayaçları sıfırlanır ki
                // şerit "Building"e dönsün (RibbonText: cycleRound > 0 kapısı) ve ProjectStartedEvent'in üye-detay kapısı
                // kapansın. Yalnız ekranda yazan grup (lider eşleşiyorsa): eşzamanlı başka bir grubun turu yerinde kalır.
                if (string.Equals(_cycleRoundLeaderId, e.ProjectId, StringComparison.OrdinalIgnoreCase))
                {
                    (_cycleRound, _cycleRoundCap, _cycleRoundMemberCount, _cycleMemberIndex) = (0, 0, 0, 0);
                    _cycleRoundLeaderId = null;
                }
                OnPropertyChanged(nameof(CycleRound));
                OnPropertyChanged(nameof(CycleRoundCap));
```

(`CycleRound`/`CycleRoundCap` bildirimi `CycleRoundStartedEvent` dalında nasıl yapılıyorsa — `RibbonLine`'ı yenileyen çağrı — aynı çağrıyı burada da yap; dalı oku ve kopyala.)

Run: filtre `A_cycle_completion_resets_the_round_counters` → PASS; `RunViewModelStateTests` tamamı → PASS.

- [ ] **Step 5: ETA döngü kovası Build'de de (Theory)**

`RunViewModelStateTests.The_eta_keeps_the_cycle_round_multiplier_while_the_group_is_running` (satır ~1712) → `[Theory] [InlineData(RunMode.Cycles)] [InlineData(RunMode.Build)]`, `RunStartedEvent(..., mode, ...)`; gövde aynı. Doc'una: "Kova moda değil, turların koşabildiği koşuya aittir (CycleCompilation)."

Run → Build satırı FAIL. Sonra `RunViewModel.cs` ~2463:

```csharp
        // [Build cycle derler] Döngü kovası turların koşabildiği her koşuya aittir (CycleCompilation — tek kaynak);
        // satırdan tetiklenen tek-proje koşusunda grup yoktur (hedef düz düğüm). Clean'de motor döngü anlamını düşürür
        // (Core/Planning/CleanRunScope): üye bir kez, sıradan bir proje gibi paralel işlenir.
        bool roundsRun = _currentRunMode is { } runMode && CycleCompilation.CompilesCycles(runMode) && RunTargetId is null;
```

(Üstündeki "[Clean] Döngü kovası yalnız TURLARIN koştuğu Cycles koşusuna aittir ... (Build/Rebuild'de üyeler zaten pre-skip edilir...)" yorumunu bu yeni yorumla değiştir.) Run → 2 PASS.

- [ ] **Step 6: Kapsam — Rebuild tüm satırlar; Resolve'da patlayan üye Build dalgasında yanar**

`ChoreographyTests.The_scope_of_an_operation_comes_from_its_run_mode`: `Assert.Equal(["A", "B"], vm.ScopeFor(RunMode.Rebuild)...)` → `["A", "B", "Cyc"]`, yorum "döngü dışı HERKES" → "HERKES — Rebuild grupları da derler". `After_a_resolve_the_build_wave_does_not_light_a_cycle_member_that_failed_there` → ad `After_a_resolve_the_build_wave_lights_the_cycle_member_that_failed_there`, gövde `Assert.Contains(vm.ScopeFor(RunMode.Build), r => r.InCycle);`, doc: eski iddia ("düz Build bir döngü üyesini ASLA derlemez, dalga onu boşuna yakıyordu — gerçek koşuda OSYS.Business.SparePart.Finance") + yeni kural (Build kirli grubu derler; Sync üyeye gerçek WillBuild verir, Task 1).

Run → 2 FAIL. Sonra `RunViewModel.cs` `ScopeFor`:

```csharp
    public IReadOnlyList<ProjectRowViewModel> ScopeFor(RunMode mode) => mode switch
    {
        // Rebuild ve Clean HERKESİ kapsar — döngü üyeleri ve harici projeler dahil (Rebuild grupları da derler).
        RunMode.Rebuild or RunMode.Clean => [.. Projects],
        RunMode.Cycles => [.. Projects.Where(r => r.InCycle)],
        _ => [.. Projects.Where(r => r.WillBuild == true && !r.Conditional)],
    };
```

Metodun XML doc listesindeki Rebuild maddesini ("döngü dışı her şey") düzelt. Run → PASS. `ResolvedWorkspace()` fixture'ı (~695, doc ~691-694 "Sync üyelere hep false") Sync önizlemesini elle kuruyorsa Task 1'in kuralına göre üyeye `true` yaz ve yorumunu güncelle.

- [ ] **Step 6b: Configuration geçişi — cycle üyesi de "bir sonraki Build"e girer**

`RunViewModelStateTests.cs` ~1993 `Switching_configuration_puts_no_cycle_member_or_undecided_row_in_the_next_build`: fixture C üyesine `WillBuild: false` + `SignatureChanged` yazıp `WillBuildCount == 0` bekliyor ("Build derlemez" gerekçesiyle). Yeni kural: Sync kirli üyeye `true` verir → fixture'da C `true`, beklenti `WillBuildCount == 1` (yalnız kararsız/undecided satır dışarıda kalır). Adı `Switching_configuration_puts_no_undecided_row_in_the_next_build_but_a_dirty_cycle_member_counts` yap; doc'una eski iddia + gerekçe. Run → kırmızı (eski fixture) → fixture ve beklenti düzeltilince yeşil.

- [ ] **Step 7: Hint satırları kalkar**

`EventStreamTests.cs`: dört `CyclesHint` testini (`A_completed_build_pushes_a_cycles_hint_after_completed_when_dirty_cycle_members_remain`, `A_cycles_run_completion_does_not_push_the_hint_even_with_dirty_members_remaining`, `A_completed_build_pushes_no_hint_when_no_cycle_member_is_dirty`, `A_stopped_run_pushes_no_hint_even_with_dirty_cycle_members_remaining`) TEK teste indir — ilkinin Arrange'ını kullan, iddia tersine:

```csharp
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — Build cycle derler]</b> Eski iddia: Build bitince hâlâ kirli cycle üyesi varsa akışa
    /// "N cycle projects have pending changes — run Cycles" satırı düşerdi. Satır üretimde hiç tetiklenmiyordu
    /// (Sync ve Build önizlemesi üyeye hep WillBuild=false yazıyordu) ve yeni kuralda yanıltıcı olurdu: Build kirli
    /// grubu zaten derler; koşu sonunda hâlâ kirli üye, derlenmesi başarısız ya da yakınsamamış üyedir ve onu
    /// satırın kendi etiketi/üçgeni söyler (ARCHITECTURE §13.2). Satır ve StreamText.CyclesHint kalktı.
    /// </summary>
    [Fact]
    public void A_completed_build_pushes_no_cycles_hint_even_when_dirty_cycle_members_remain()
    {
        // <eski ilk testin Arrange bloğu AYNEN>
        Assert.DoesNotContain(vm.StreamEvents, l => l.Text.Contains("cycle projects have pending changes", StringComparison.Ordinal));
    }
```

Üç `CleanedCyclesHint` testini (`A_full_clean_that_cleaned_cycle_members_points_to_resolve_cycles_after_completed`, `A_stopped_full_clean_counts_only_the_cycle_members_it_actually_cleaned`, `Only_successfully_cleaned_cycle_members_count_and_none_means_no_hint`) TEK teste indir — ilkinin Arrange'ı, iddia `Assert.DoesNotContain(vm.StreamEvents, l => l.Text.Contains("cycle projects cleaned", StringComparison.Ordinal));`. Doc: "[kullanıcı kararı 2026-09-28] Clean döngü üyelerini de temizler — bu kalır; 'run Resolve cycles before Build' satırı kalktı, çünkü temizlenen üyeyi bir sonraki Build derler (grup kayıtsız → tur 1)."

Run → 2 FAIL (satır hâlâ üretiliyor). Sonra:
- `RunViewModel.Stream.cs` ~332-352: `if (_currentRunMode != RunMode.Cycles) { int n = ...; if (n > 0) PushStream(... CyclesHint ...) }` bloğunu ve `[Clean · kullanıcı kararı 2026-09-28]` yorumlu `CleanedCyclesHint` bloğunu (sayaç `cleanedMembers` dahil, başka tüketicisi yoksa) sil.
- `StreamText.cs`: `CyclesHint` ve `CleanedCyclesHint` metodlarını doc'larıyla sil. `AccessibilityNames.ResolveCyclesButton`'ın StreamText'teki tek kullanımı buydu; `using` artığı kalmasın.

Run: `dotnet test ... --filter "FullyQualifiedName~EventStreamTests"` → PASS.

- [ ] **Step 8: `DecisionLabel` — yeniden deneyen her satırda Build**

`DecisionLabelTests.cs`: `For(...)` yardımcısından `inCycle` parametresini kaldır; `A_cycle_member_failure_names_resolve_cycles_in_the_tooltip` → yeniden yaz:

```csharp
    /// <summary><b>[DEĞİŞEN KURAL — Build cycle derler]</b> Eski iddia: döngü üyesinin <c>failed</c> satırını yeniden
    /// deneyecek şey Resolve cycles'tı, uzun gerekçe onu adlandırırdı ("Resolve cycles will retry it"). Build kirli
    /// grubu derlediği için (ölçüm: 2026-10-07 13:17 koşusu, ARCHITECTURE §8.1) yeniden deneyen her satırda
    /// Build'dir; döngü üyeliği etiketin girdisi olmaktan çıktı (parametre kalktı).</summary>
    [Fact]
    public void A_failed_row_names_build_as_the_retrier_whatever_its_cycle_membership()
    {
        Assert.Equal("Failed at this source — Build will retry it", For(true, WillBuildReason.LastFailed).Title);
        Assert.Equal("Failed at this source — Build will retry it", For(false, WillBuildReason.LastFailed).Title);
    }
```

Derleme kırmızısını gör (`For` imzası). Sonra `DecisionLabel.cs`: `inCycle` parametresini ve doc'unu (~82-85) kaldır; `retryClause` dalını `"Failed at this source — Build will retry it"` sabitine indir. Çağıranlar: `RunViewModel.Workspace.cs:787` ve `Views/ProjectRow.xaml.cs:545`'ten `_vm.InCycle`/`row.InCycle` argümanını kaldır; `ContentDecisionDiagnosticsTests.cs:147,167`, `RunViewModelTests.cs:1865,1938`, `RunViewModelStateTests.cs:2256` çağrıları zaten dört argümanlı — derle, kırılan varsa düzelt.

Run: `dotnet test ... --filter "FullyQualifiedName~DecisionLabelTests"` → PASS.

- [ ] **Step 9: Konsol boş-durum metni**

`ConsoleModesTests.cs` ~228-233: beklenti
`["In a dependency cycle — Build never compiles one; use Resolve cycles.", "Never built by this tool"]` → döngü üyesi artık plan bayrağına göre konuşur: `Row(ProjectRowState.Pending, willBuild: true, willBuildReason: WillBuildReason.NeverBuilt, inCycle: true)` için `["Will build — this tool has never built it."]` (düz satırla birebir aynı; yorumunu "Döngü üyeliği plandan ÖNCE gelmez: Sync üyeye gerçek bir WillBuild verir (Build kirli grubu derler), satır o cevabı söyler" yap).

Run → FAIL. Sonra `ConsoleEmptyState.cs`:
- `Pending(...)` içindeki "Döngü üyeliği plandan ÖNCE gelir..." yorumunu ve `if (row.InCycle) return InCycleText;` satırını sil.
- `InCycleText` sabitini `"In a dependency cycle — this run did not compile it."` yap; doc'unu "Yalnız motorun `in dependency cycle` ile atladığı satır için (grup haritasız koşu — üretimde erişilmez, bkz. ReadySetScheduler); plandan konuşan satır normal dallara düşer." olarak yaz. Skipped dalındaki eşleme (`SkipReasons.InDependencyCycle => InCycleText`) kalır.

Run: `dotnet test ... --filter "FullyQualifiedName~ConsoleModesTests"` → PASS.

- [ ] **Step 10: Ölü sabitler**

`grep -rn "CycleText.Membership\|CycleText.ClusterHeadline" src tests` boş dönmeli; `CycleText.cs`'ten `Membership` ve `ClusterHeadline` sabitlerini sil (sınıf doc'unda "membership line" geçiyorsa düzelt).

- [ ] **Step 11: Kod içi doc'lar**

- `RunViewModel.Stream.cs` ~76-84 `IsResolvingCycles` doc'u: "şerit koşu satırını buna göre yazar" → "bakım kutusu Resolve düğmesini amber zemin + spinner'a çevirir ve Restart kilidine girer; şeridin tur satırı buna DEĞİL uçuştaki tura bağlıdır (RibbonText)".
- `RunViewModel.cs` `InRunQueueFor` doc'u değişmez (Build: WillBuild && !Conditional, üyeler artık doğal olarak girer) — doğrula.

- [ ] **Step 12: App test klasörü**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~BuildOrchestrator.Tests.App"`
Düşen test varsa: "Build cycle derlemez"i pinleyen bir iddia ise yeni kurala göre yeniden yaz (Ek B), değilse kod hatası — düzelt.

- [ ] **Step 13: Commit**

```bash
git add src/BuildOrchestrator.App tests/BuildOrchestrator.Tests/App
git commit -m "feat(app): tur satiri uçuştaki tura bagli, hint satirlari kalkti, Rebuild dalgasi ve ETA kovasi cycle uyelerini kapsar, etiketler Build'i gosterir"
```

---

### Task 5: Doküman — README ve ARCHITECTURE yerinde yeniden yazılır

**Files:** `README.md`, `ARCHITECTURE.md`. Satır numaraları `37c98faf`'a göre; metni grep'le bul. Anlatı üslubu: bölüm yeni gerçeği anlatır, "eskiden/şimdi" yazmaz. Her iddia koda karşı doğrulanır (Task 1-4 bitmiş olmalı).

- [ ] **Step 1: README**

| Satır | Ne yapılır |
|---|---|
| 340-342 Build maddesi | sonuna: "— dirty dependency-cycle groups included, compiled in rounds (see *Resolve cycles* below)". |
| 349-350 | "— *Resolve cycles*, for a cycle member, and when a Clean cleaned any, the event stream says so as it ends" kuyruğunu kaldır; cümle "until the next *Build* compiles it." ile biter. |
| 409-417 | Paragrafı yeniden yaz: "Projects that reference each other's output form a dependency cycle. *Build* compiles such a group as one unit when it is dirty: the members compile in rounds until their public API surfaces settle, and whatever depends on the group waits for it and compiles against its fresh output; a group whose composite signature is clean is skipped as `up to date`. **Resolve cycles** — the third icon (unlink) of the maintenance box next to *Sync* — is the narrow form of the same work: it compiles only the cycle groups and whatever stale upstream they need, nothing downstream, so you can pay for the cycles alone — say before switching to Visual Studio. It is optional: a plain *Build* does the same rounds for a dirty group. It is enabled only when the workspace actually has a cycle, and its tooltip says what it will do ... (tooltip ve şerit cümleleri aynen kalır)". "It is meant to be pressed **before** a build, not instead of one" cümlesi kaldırılır. |
| 437 | "Why cycles are a button and not something *Build* does for you: a cycle is built as one unit —" → "How a cycle group is built, whichever run builds it: as one unit —"; 437-446 mekanik aynen. |
| 446-447 | "Even so the worst case is members × rounds ... Behind a button you decide when to pay it." → "The worst case is still members × rounds; in practice a group whose surfaces did not move settles in a single round, and the ribbon shows the round phase while it runs, so the bill is visible as it is paid." |
| 453-454 | "— those are Build's job, and Build is what you press next" kuyruğunu kaldır. |
| 456 | "The run reads like any other beyond that" → "A run that compiles a cycle group reads like any other beyond that — whether it is a *Build* or *Resolve cycles* —". |
| 467-469 | amber küp cümlesi: "so a finished run still answers "why was this one not built?"" → "so a finished run still shows which rows belong to a cycle". |
| 472-473 | "reads `failed` with *Resolve cycles will retry it*" → "reads `failed` with *Build will retry it*". |
| 476-480 | özne genelleştir: "Pressing *Build* or *Resolve cycles* again is always a real attempt." |
| 480-483 | "refusing to retry would mean the button silently doing nothing" → "refusing to retry would mean a *Build* or a *Resolve cycles* silently doing nothing". |
| 486-487 | "And when an ordinary *Build* finishes with cycle members still dirty, the event stream adds a closing line pointing at *Resolve cycles* as the next step." cümlesini kaldır. |
| 695-703 perf | değişmez; 703'e ekle: "— including the cycle rounds a *Build* runs for a dirty group". |

- [ ] **Step 2: ARCHITECTURE**

| Satır | Ne yapılır |
|---|---|
| 390 | "Building dependency cycles is not a field but a **mode** — `Cycles` (§8.1)." → "Which runs compile dependency cycles is not a field: every compiling run does (§8.1, `CycleCompilation`); `Cycles` is the narrow-scope one." |
| 392-393 | "and `Build` never compiles a cycle" → "and a `Build` compiles a dirty cycle group like any other dirty node". |
| 706-709 (§6.5) | "What happens to it at run time depends on the run's mode: a `Cycles` run dispatches ... every other mode leaves its members to be pre-skipped ..." → "Every compiling run dispatches the component as a single work item and compiles it in rounds (§8.2, §8.8); a run started from a row carries no component map and compiles its one target alone (§8.1). The scheduler's own pre-skip of members (`in dependency cycle`) is the path for a plan without a component map, which in production is a plan without a cycle." |
| 854-855 (§7.3) | "when a `Cycles` run starts the group" → "when a run starts the group". |
| 885-886, 888-890, 903-906 (§7.4) | kısa devre cümlelerini yeniden yaz: "The run's scope is the one short circuit: a run that does not compile cycles (`Clean`, and the preview a test asks with the flag off) reads every member `false`. Sync's preview and every compiling run read the member by the group's composite signature, so a dirty group reads `true` as a whole." 888-890 parantezi ("a cycle member stays `false`") kaldır; 903-906'daki örnek "such as a cycle member outside a `Cycles` run" → "such as a member of a group the run pre-skipped as up to date". |
| 950-951 (§7.5) | "behind the first round of a *Resolve cycles* run" → "behind the first round of the run that next compiles its group (a `Rebuild` compiles every member regardless)". |
| 1049-1051 (§7.6) | "in the *Cycles* run that builds it" → "in the run that builds its group". |
| 1103 (§8.1 tablo) | Build satırının sonuna "; dirty cycle groups included, compiled in rounds (§8.8)". Rebuild satırına "; cycle groups compile every member in round one, later rounds follow the evidence". |
| 1108-1110 | "`Cycles` is not a degree of difference ... meant to be run before a build, not instead of one." → "`Cycles` is the narrow form of the same work: `Build` and `Rebuild` compile a dirty cycle group in rounds as part of the plan; `Cycles` compiles only the cycle groups and their stale upstream. It is the third icon of the maintenance box in the action bar (§13.2) and is optional — useful when the cycles alone are the work to pay for." |
| 1143-1145 | "it is not pre-skipped as `in dependency cycle`, no rounds run" → "no rounds run". |
| 1150-1154 | "except a cycle member, which a plain `Build` never compiles" kaldır; "For the same reason the end of a Clean that cleaned cycle members is spelled out ... `N cycle projects cleaned — run Resolve cycles before Build` (§13.2)." cümlesini kaldır. |
| 1179-1180 | "for the next `Cycles` run" → "for the next run that compiles the group". |
| 1206-1212 | ", which it never compiles" gerekçesini kaldır; kural (Cycles önizlemesi plan bayrağını yazmaz) kalır, gerekçe: "the `Build` pressed next would light the members by this run's answer, not by the signature the next plain `Build` actually reads". |
| 1231-1239 | İki paragrafı yeniden yaz: "**Why the scope stops there.** Downstream is deliberately excluded: a plain `Build` compiles the group and everything that depends on it; `Cycles` exists to pay for the cycles alone, and including the dependents would quietly widen it to the whole repository. **Why `Build` compiles the group too.** A dependent compiled against a cycle member's previous output links to a stale binary and fails or, worse, succeeds silently (measured on the real workspace: a pull that changed two Types cycle members turned their Business dependents red with no cause on the row). A group's cost is members × rounds, but round one compiles only the members that need it (§8.8) and a group whose surfaces did not move settles in a single round, so the bill is one compile per dirty member plus a surface hash. The bill stays visible: the wave and the counts before the click, the ribbon's round phase while it runs." |
| 1242-1243 | "It is also the only mode that reads the non-convergence memory (§8.8)." → "Every run that compiles a group reads the non-convergence memory and reports it (§8.8)." |
| 1265-1268 (§8.2) | "Without the component map — which is how the scheduler is built in every mode but `Cycles` — members are marked `Skipped` ... Nothing else distinguishes the two modes: there is no code path written for cycles being out of scope, the mode only chooses between passing the map and passing nothing." → "Without the component map — a plan without a cycle, or a run started from a row — members are marked `Skipped` at construction with the reason `in dependency cycle`. Nothing else distinguishes the modes: `Cycles` differs from `Build` only by the scope seed (§8.1), and `Rebuild` by compiling every member in round one." |
| 1325, 1344-1346 (§8.3) | örnekleri değiştir: "(say a dormant cycle member reading signature changed)" → "(say a project the run pre-skipped as up to date)"; 1344-1346 "A root that is itself a dormant cycle member ... is pre-skipped in a `Build` run without ever being attempted" → "A root that is a member of a group the run skipped as up to date is never attempted". |
| 1364-1367 (§8.4) | "plus — in a `Cycles` run — the cycle members' estimates" → "plus — in any run that compiles a cycle group — the members' estimates ..."; "That term belongs to the run where rounds actually run" kalır. |
| 1376-1381 | ribbon paragrafı: "A `Cycles` run does not use that line. Its ribbon reads ..." → "While a group is in rounds — in a `Cycles` run or a `Build` — the ribbon reads `▸ Resolving cycles · round {r}/{cap} · {n}/{m} · {elapsed}` instead and carries no estimate suffix; `preparing dependencies` stands in for the round only in a `Cycles` run, before its first group starts, while a `Build` shows its ordinary line there." |
| 1567-1570 (§8.8) | "These run in one mode only — `Cycles` (§8.1), the third icon ..." → "These run in every compiling mode (§8.1); a `Rebuild` compiles every member in round one. While a group is in rounds the ribbon reads ... (1376-1381 ile aynı cümle)". |
| 1713-1715 | "the next `Cycles` run picks up" → "the next run that compiles the group picks up". |
| 1737-1739 | "(with the *Resolve cycles will retry it* clause, §13.2)" → "(with the *Build will retry it* clause, §13.2)". |
| 1767-1772 | "A later `Cycles` run that computes the same signature writes `cycle {leader}: retrying — ...`" → "A later run that compiles the group at the same signature writes ..."; "the only way into a `Cycles` run is the user pressing **Resolve cycles**" → "the only way into the rounds is the user pressing **Build** or **Resolve cycles**". |
| 1782-1783 | "in the same `Cycles` run" → "in the same run". |
| 2969 (§13.2 tablo) | "(for a cycle member, `Resolve cycles will retry it`)" kaldır. |
| 2978-2980 | "and whether the project sits in a dependency cycle (which only picks the retry clause ...)" maddesini kaldır (üç olgu kalır). |
| 3000-3004 | paragrafı yeniden yaz: "A failed row's tooltip names who will retry it: *Build*, for every row, because a plain Build compiles a dirty cycle group too. The word is a fact, not a promise ..." (kalan "word is a fact" cümleleri aynen). |
| 3019-3021 | "A cycle member is not compiled by a plain Build, but if its files changed it still reads `modified` — that is true, and the warning triangle is what says *Resolve cycles* is the thing that will compile it." → "A cycle member whose files changed reads `modified` like any other row, and the warning triangle says only that it sits in a cycle — its group compiles in rounds, in the next *Build* or *Resolve cycles*." Rebuild örneği ve 33/184 ölçümü kalır. |
| 3044-3048 | "Build never compiles it and Cycles compiles it with its whole group" → "every run compiles it with its whole group"; parantez "(`WaitingForDependency`, `WillBuild=false`, `Conditional=false` ...)" → "(`WaitingForDependency`, `Conditional=false` — the member is never individually conditional; `ConditionalRebuild.GroupAppliesTo` decides for the group at dispatch)". |
| 3239-3243 | Clean kapanış satırı paragrafını kaldır (satır kalktı); yerine tek cümle: "A Clean that cleaned cycle members needs no closing hint: the next *Build* compiles them, their records being gone." |
| 3389-3392 | "*Resolve cycles* is the cycle run" → "*Resolve cycles* is the narrow-scope cycle run (cycles and their stale upstream only)". |
| 3408-3411 | "these are things you do *before* a build" → "these are maintenance runs beside a build — a sync, the cycles alone, a clean —". |
| 3784-3786 (§13.3) | "Building dependency cycles is **not** a setting: it is a run of its own ... answered per run." → "Building dependency cycles is **not** a setting: a plain *Build* compiles a dirty group, and *Resolve cycles* compiles the cycles alone (§8.1, §13.2). A preference would have been the wrong shape — the question is not "should this tool ever build cycles" but "do I want to pay for the cycles alone right now", and that is answered per run." |
| 4243-4245 (§13.5) | "Cycle membership is checked before that verdict, since Sync gives every cycle member `false` and reading that as "up to date" would be a lie." → "A cycle member reads the same verdict as any row: Sync evaluates it by its group's composite signature (§7.4)." |
| 4387-4389, 5039-5043 | "and neither does Resolve cycles compiling the member" → "and neither does a run compiling the member". |
| 5074-5079 (§14.3) | "A `Build` will not compile a cycle; *Resolve cycles* will (§8.1)." → "Its group compiles in rounds, in a `Build` or in *Resolve cycles* (§8.1, §8.8)." |
| 6000-6006 (§20) | "lives inside a `Cycles` run" → "lives inside any run that compiles a group, Sync's preview included: the preview is computed per node from signatures alone, while round one ... (gerisi aynen)". |
| 6260 (§22) | "cycle group dispatch and pre-skip" → "cycle group dispatch (and the pre-skip of members in a plan without a component map)". |
| 6267 | "Resolve round trail" → "Cycle round trail". |
| 6358 | "the Resolve group-start surface hash" → "the group-start surface hash". |
| 6431 | "Cycle wording: membership line, cycle path" → "Cycle wording: cycle path". |
| §22 Core/Planning tablosu | yeni satır: "| Which run modes compile dependency-cycle groups (`Build`, `Rebuild`, `Cycles`; not `Clean`) — the single source read by Sync's preview, the Supervisor's plan, the coordinator's group gate and the App's round bookkeeping | `Core/Planning/CycleCompilation.cs` |". |

- [ ] **Step 3: Guard testleri**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~Guard|FullyQualifiedName~Doc|FullyQualifiedName~Source"`
Expected: PASS (README/ARCHITECTURE'ı okuyan kaynak guard'ları varsa yeni cümleleri görür).

- [ ] **Step 4: Commit**

```bash
git add README.md ARCHITECTURE.md
git commit -m "docs: Build kirli cycle gruplarini derler — koşu modlari, kapsam gerekcesi, serit ve etiket cumleleri yerinde yeniden yazildi"
```

---

### Task 6: Tam süit, gerçek OSYS ölçümü, merge

**Files:** `.claude/outputs/<YYYY-MM-DD-HH-mm>-build-compiles-dirty-cycles-measurement.md` (yeni), `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs` (sınıf doc'una ölçüm).

- [ ] **Step 1: Tam süit**

```bash
dotnet build BuildOrchestrator.slnx
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance" > .claude/temp/full-suite.txt 2>&1; tail -5 .claude/temp/full-suite.txt
```
Expected: `Failed: 0`. Zamanlama testi tek başına düşerse sakin makinede tek başına tekrar koştur; eşik gevşetilmez.

- [ ] **Step 1b: Acceptance testleri (gerçek OSYS, `Category=Acceptance`, VS ve Client kapalı)**

İki test eski kuralı varsayar; önce yeniden yaz, sonra bir kez koştur (`dotnet test ... --filter "Category=Acceptance"`, dakikalar sürer):
- `Integration/OsysRebuildAcceptanceTests.cs` ~105 `Osys_full_rebuild_parallel_is_green_with_zero_orchestrator_caused_failures`: ~142-145 yorumu "Rebuild SCC'ye hiç dokunmaz" → Rebuild grupları her üyeyi tur 1'de derleyerek koşar. Eşzamanlılık sayacı (`maxConcurrent`) `CycleMemberHeldEvent`'i de "bitti" saymalı (tur üyesi `ProjectSucceededEvent` yerine `CycleMemberHeldEvent` ile slotu bırakır); sıralama-ihlali kontrolü grup İÇİ kenarları saymaz (dairesel kenar ihlal değildir, `CycleGroups.From(plan)` üyeliğiyle ele); `OverallBudget` turları kaldırmıyorsa ölçüme göre büyüt (gerekçeyi doc'a yaz, eşik gevşetme değil kapsam genişlemesi).
- `Integration/OsysIncrementalAcceptanceTests.cs` ~66: ~194 `Bind(... buildCycles: false ...)` → `CycleCompilation.CompilesCycles(RunMode.Build)`; ~198 ve ~328 yorumları ("cycle üyeleri her zaman false") düzelt; Run 2'de yeniden derlenen üyeler (yakınsamayan grubun güvenilmez başarıları) `run1LegitimateRebuild` kümesine girer — `CycleCompletedEvent.Outcome != Converged` olan grupların üyeleri "meşru" sayılır.
- `ContentDecisionDiagnosticsTests` ~132-133 yorumu ("Build SCC'yi ASLA derlemez") düzelt.

- [ ] **Step 2: Gerçek OSYS ölçümü (kullanıcıyla)**

Önkoşul: Visual Studio'nun OSYS solution'ları ve `OSYS.UI.Container.exe` kapalı (post-build copy kilitleri, bkz. 2026-10-07 11:39-11:48 MSB3073). Uygulamayı yeni derlenen koddan başlat (`dotnet run --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj`), RepositoryRoot `D:\Projects\Delta\OSYS`.
1. **Clean** (Build menüsü) → **Build** (tek tık). `decision.log`'dan: toplam `duration=`, `cycle ... converged` satırları ve tur sayıları, `succeeded/failed/skipped`.
2. Karşılaştırma tabanı: 2026-10-07 `run-20261007-095730-736` (Clean) → `run-20261007-095819-438` (Cycles, 197 sn, 83 proje) → `run-20261007-100205-622` (Build, 186 sn, 103 proje).
3. İkinci koşu: hiçbir şey değiştirmeden tekrar **Build** → tüm cycle grupları `skipped — up to date`, süre saniyeler.
4. Mümkünse üçüncü: bir Types cycle üyesinde bir public üyeyi değiştirip (sonra geri alınacak yerel değişiklik) **Build** → grup derlenir, bağımlıları yeşil, UI grubunun kaç üyesinin derlendiği.

Sonuçları `.claude/outputs/<ts>-build-compiles-dirty-cycles-measurement.md`'ye tablo olarak yaz (ts = `Get-Date -Format 'yyyy-MM-dd-HH-mm'`). `CycleRoundsTests` sınıf doc'una iki cümlelik ölçüm özeti ekle (test doc'u rakam taşıyabilir; ARCHITECTURE taşımaz).

- [ ] **Step 3: Commit + merge + push**

```bash
git add .claude/outputs tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs
git commit -m "docs(outputs): Build icinde cycle derlemenin gercek OSYS olcumu"
git switch develop && git pull --ff-only
git merge --no-ff feat/build-compiles-dirty-cycles -m "merge: build kirli cycle gruplarini da derler (feat/build-compiles-dirty-cycles)"
git push origin develop
git log --oneline -1 origin/develop   # merge commit'i gösterir
git branch -d feat/build-compiles-dirty-cycles
```
(Branch remote'a push edildiyse `git push origin --delete feat/build-compiles-dirty-cycles`.) Oturum `develop` üzerinde biter. Panoda son adımı `completed` yap.

---

## Ek A — Değişmeyen davranışlar (uygulayıcı bunları "düzeltmez")

- `Cycles` koşusunun kapsamı (üyeler + transitif upstream), `OutOfCycleScope` pre-skip'i ve "N outside cycle scope — skipped" satırı.
- `PerfProfile.ForRun`: tam öncelik yalnız `Cycles`.
- `ExternalUpdater.ShouldUpdate`: harici kök güncellemesi `Cycles` ve `Clean` dışında.
- `ProjectRunScope` ve tek-proje bayat-bağımlılık uyarıları.
- `SkipReasons.InDependencyCycle` sabiti ve `ReadySetScheduler`'ın haritasız pre-skip dalı.
- `AccessibilityNames` Resolve tooltip metinleri, `RowWarning` metinleri, `CycleText.Path`.
- Contracts: hiçbir event/komut alanı eklenmez, kaldırılmaz.

## Ek B — Eski kuralı pinleyen testler (tam envanter, `37c98faf`)

Her satır ilgili task'ta ele alınır; hiçbiri silinmez, yeniden yazılır (doc: eski iddia + gerekçe). "doc" = yalnız ad/yorum/fixture açıklaması eski kuralı anlatıyor, iddia geçerli.

| Dosya | Test / yer | Ne olur | Task |
|---|---|---|---|
| Supervisor/CycleRoundsTests.cs | ~90 `a_build_run_pre_skips_every_member_with_the_original_reason_and_runs_no_rounds` | yeniden yaz | T2 Adım 1 |
| Supervisor/CycleRoundsTests.cs | ~126 `a_build_run_ignores_non_convergence_memory_left_behind_by_a_cycles_run` | yeniden yaz: Build hafızayı RAPORLAR (`CycleDecisionLines.Retrying` satırı r2'nin decision.log'unda, `CycleDecisionLogTests.IndexOf` ile) ve grubu yine dener (invoker r2'de çağrılır); `ProjectSkippedEvent` yok | T2 Adım 11 |
| Supervisor/CycleRoundsTests.cs | ~515 `stopped_group_invalidates_every_member` | Theory (Cycles, Build) | T2 Adım 10 |
| Supervisor/CycleRoundsTests.cs | ~821, ~963-973, ~1016-1026 | doc | T2 Adım 11 |
| Supervisor/RunCoordinatorTests.cs | ~1125 `cycle_members_are_pre_skipped_again_by_every_fresh_run` | yeniden yaz (`CyclePlanOf`; üyeler her taze Rebuild'de derlenir) | T2 Adım 11 |
| Supervisor/RunCoordinatorTests.cs | ~1495 `a_skipped_dependency_produces_no_dep_issue_for_its_dependent` | yeniden yaz (atlanan bağımlılık up-to-date tohumuyla kurulur) | T2 Adım 11 |
| Supervisor/RunCoordinatorTests.cs | ~81-83 `Start` doc, ~1192-1194, ~1294-1295, ~2451-2452 | doc | T2 Adım 11 |
| Supervisor/ConditionalRebuildRunTests.cs | ~272 `A_still_failing_root_that_never_ran_this_run_is_labelled_from_its_last_known_result` | yeniden yaz (SCC temiz kurulur, iddia aynı) | T2 Adım 11 |
| Supervisor/CycleDecisionLogTests.cs | `RunCyclesAsync` (~44) | mod parametresi | T3 Adım 2 |
| Supervisor/SingleProjectRunTests.cs ~142, FullCleanRunTests.cs ~24-25, SupervisorIpcTests.cs ~27-39 (`WideRunTimeout`) | doc | T2 Adım 11 |
| Workspace/SyncWorkspaceServiceTests.cs | yeni `A_dirty_cycle_member_reads_will_build_in_the_sync_preview` | ekle | T1 Adım 5 |
| Planning/NextPreviewTests.cs | ~34, ~46, ~86 | yeniden yaz | T1 Adım 10 |
| Planning/ConditionalRebuildTests.cs ~199-204, ProjectRunScopeTests.cs ~132/~152/~170 | doc ("tam Build'de üye derlenmez" premisi) | T2 Adım 12 |
| Scheduling/ReadySetSchedulerTests.cs | ~109, ~297, ~312 (haritasız dal) | geçerli kalır; ~308-310 yorumu "bu Build'in NORMAL yoludur" → "haritasız plan / kill-switch yolu" | T2 Adım 12 |
| App/RunViewModelTests.cs | ~1898, ~1928, ~1969 (`row.WillBuild` false) | yeniden yaz | T1 Adım 12b |
| App/RunViewModelTests.cs | ~1306 `Rebuild_wires_through_the_real_engine_and_populates_rows` | yeniden yaz (gerçek motor; kablolama iddiası) | T2 Adım 11 |
| App/ChoreographyTests.cs | ~609 `The_scope_of_an_operation_comes_from_its_run_mode` (Rebuild satırı) | yeniden yaz | T4 Adım 6 |
| App/ChoreographyTests.cs | ~684 `After_a_resolve_the_build_wave_does_not_light_a_cycle_member_that_failed_there` | beklenti tersine | T4 Adım 6 |
| App/ChoreographyTests.cs | ~774 `After_a_full_clean_the_build_wave_lights_the_cleaned_projects_but_not_the_cycle_members` | beklenti tersine | T1 Adım 12b |
| App/ChoreographyTests.cs | `ResolvedWorkspace()` ~695 fixture doc | doc/fixture | T4 Adım 6 |
| App/RunViewModelStateTests.cs | ~1712 `The_eta_keeps_the_cycle_round_multiplier_while_the_group_is_running` | Theory (Cycles, Build) | T4 Adım 5 |
| App/RunViewModelStateTests.cs | ~1993 `Switching_configuration_puts_no_cycle_member_or_undecided_row_in_the_next_build` | yeniden yaz | T4 Adım 6b |
| App/RunViewModelStateTests.cs | yeni `A_cycle_completion_resets_the_round_counters_of_the_group_on_screen` | ekle | T4 Adım 3 |
| App/RibbonTextTests.cs | yeni 2 test | ekle | T4 Adım 1 |
| App/EventStreamTests.cs | ~714, ~744, ~767, ~785 (CyclesHint) → tek test; ~838, ~859, ~875 (CleanedCyclesHint) → tek test | yeniden yaz | T4 Adım 7 |
| App/DecisionLabelTests.cs | ~138 `A_cycle_member_failure_names_resolve_cycles_in_the_tooltip`, `For` yardımcısı ~39 | yeniden yaz | T4 Adım 8 |
| App/ConsoleModesTests.cs | ~228-233 | yeniden yaz | T4 Adım 9 |
| Integration/OsysRebuildAcceptanceTests.cs ~105, OsysIncrementalAcceptanceTests.cs ~66 (`Category=Acceptance`) | yeniden yaz | T6 Adım 1b |
| Incremental/ContentDecisionDiagnosticsTests.cs ~132-133 | doc | T6 Adım 1b |

**Geçerli kalanlar (bayrak/sabit semantiği değişmedi — düşerse kod hatasıdır):** `Planning/WillBuildTests.cs` (tümü; `buildCycles:false` ⇒ `false` kuralı testlerde yaşamaya devam eder, yalnız "switch" adlı testlerin doc'u isteğe bağlı güncellenir), `Incremental/IncrementalPlannerTests.cs` (~243 "off" yarısı dahil; ~1096'nın "Build/Sync kapsam dışı" yorumu → "bayrak kapalı"), `Incremental/IncrementalRunBinderTests.cs`, `Planning/ConditionalRebuildTests.cs`, `Planning/CycleRunScopeTests.cs`, `Planning/CleanRunScopeTests.cs`, `ProcessControl/PerfProfileTests.cs` (~88 `Build_rebuild_and_clean_always_follow_the_profile` — D5), `Ipc/IpcMessagesTests.cs` (~353 `in dependency cycle` tel metni, ~65 fixture — sabit kalır, D14), `App/MaintenanceBoxTests.cs` ~353 (spinner yalnız Cycles koşusunda — D10), `App/EventStreamTests.cs` ~587 ve `App/RunViewModelTests.cs` ~135 (`InDependencyCycle` yalnız fixture), `RunCoordinatorTests` tam-öncelik pinleri (~2464-3184), `CycleDecisionLogTests` (tümü Cycles), `CycleRoundsTests` kalan Cycles testleri.

**Kaynak guard'ı yok:** README/ARCHITECTURE'daki döngü cümlelerini okuyan test bulunmuyor (`RepoHygieneTests` yalnız CI rozetine bakar); Task 5'in doğrulaması koda karşı elle okumadır.

**Kopya notu (kapsam dışı, dokunma):** diskteki X↔Y legacy csproj fixture'ı `RunCoordinatorTests` ~1173-1185 ve `RunViewModelTests` ~1312-1322'de birebir kopyadır; bu plan onu birleştirmez.
