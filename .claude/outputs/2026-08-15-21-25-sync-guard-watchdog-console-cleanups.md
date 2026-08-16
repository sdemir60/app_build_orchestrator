# Sync Guard + Watchdog + Konsol Temizlikleri — Uygulama Planı

> **Agentic worker için:** ZORUNLU ALT-SKILL: Bu planı task-by-task uygulamak için
> `superpowers:subagent-driven-development` (önerilen) ya da `superpowers:executing-plans` kullan.
> Adımlar checkbox (`- [ ]`) sözdizimiyle izlenir.

**Hedef:** Önceki oturumda not alınan 6 bulgu + yeni gözlemlenen "run konsolunda geçmiş kayboluyor"
kusuru için, `fix/console-back-return-gesture` branch'i üzerinde doğrulanmış tespitlerin düzeltilmesi.

**Mimari:** Tüm değişiklikler App katmanında (RunViewModel + ConsoleView) ve dokümandadır; Core/Supervisor'a
dokunulmaz. Her fix kırmızı-test-önce (proje kuralı: hiçbir fix, kusuru yakalayan test KIRMIZI verdiği
gösterilmeden yapılmaz).

**Tech stack:** .NET 10 WPF, CommunityToolkit.Mvvm, AvalonEdit, xUnit (STA/WPF testleri mevcut süit
desenleriyle).

**Spec:** Bu dosyanın kendisi (analiz + karar aşağıda her task'ın başındaki "Durum/Karar" bloğunda).

## Global kısıtlar (proje CLAUDE.md'den — ihlal edilemez)

- Kırmızı test kuralı: fix'ten önce testin KIRMIZI verdiği gösterilir; kırmızı gösterilemiyorsa test yanlıştır.
- Kopya YASAK: aynı kural/metin/primitif iki yerde tanımlanmaz.
- Kod, UI metinleri ve loglar İngilizce; kod yorumları Türkçe.
- Test komutu: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.
  Bitişte TAM süit yeşil (token/motion/D8 guard'ları dahil). Uygulama açıkken build alınmaz.
- Davranış bilerek değişiyorsa onu pinleyen eski test sessizce silinmez/gevşetilmez — YENİ kuralı pinleyecek
  şekilde yeniden yazılır, doc'una gerekçe işlenir.
- Doküman kodla uyuşmaz hâle gelirse ilgili bölüm AYNI işte güncellenir (ARCHITECTURE.md/README.md).
- Git: her task ayrı commit; iş bitince `main`'e merge + push, branch silinir, oturum `main`'de biter.

## Bulgular — öncelik sırası

Bloklayıcı: yok.

| # | Task | Önem | Konu |
|---|------|------|------|
| 1 | Task 1 | Önemli | Sync'in çift tetiklenmesine kapı yok (`CanSync` `_syncInFlight`'a bakmıyor) |
| 2 | Task 2 | Önemli | Sessizlik watchdog'u Sync penceresini kapsamıyor (`WaitingOnEngine`) |
| 3 | Task 3 | Önemli | Sync uçarken Build serbest → transkript karışması |
| 4 | Task 6 | Önemli | Run konsolunda geçmiş 200 satıra kırpılıyor, geri getirilemiyor (yeni bulgu) |
| 5 | Task 4 | Hijyen | Satır araması iki yerde case-sensitive (`OnProjectDone`/`EnsureRow`) + dağınık tekrar |
| 6 | Task 5 | Hijyen | `ConsoleView._trimTail` ölü alan + bayat yorum |
| 7 | Task 7 | Kozmetik | ARCHITECTURE.md §13.5 iç çelişki (tek satır doküman düzeltmesi) |

Task sırası bağımlılığa göre: 1 → 2 → 3 (aynı guard yüzeyi), sonra 4, 5, 6, 7.

---

### Task 1: Sync çift-tetiklenme kapısı

**Durum:** [RunViewModel.cs:681](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L681)
`CanSync() => !IsRunning && !IsStarting && !IsEngineUnavailable` — `_syncInFlight`'a bakmıyor.
Rebuild/Cycles bakıyor ([RunViewModel.cs:627](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L627)).
Sync'e ikinci basış ikinci bir tam analiz kuyruğa alır; her basış üç komut gönderir
(sync + listBranches + listWorktrees, satır 660-680). Ayrıca `_syncInFlight` yalnız `syncStarted`
event'iyle kurulur — tıklama ile `syncStarted` arasındaki pencerede de kapı yoktur.

**Karar:** İki katmanlı kapı. (a) `_syncRequested` alanı: `SyncAsync` girişinde senkron kurulur,
`syncStarted` gelince ya da gönderim senkron düşünce bırakılır — `BeginRunAsync`'in `IsStarting`
deseninin birebir simetriği. (b) `CanSync` hem `_syncRequested` hem `_syncInFlight`'ı okur. İki bayrağın
birleşimi TEK predicate'te toplanır (`SyncBusy`) ve `CanRebuildOrRetry` de onu okur (kopya yasak).
Bayrak sızıntısı kapıları: engine ölümü → `ReleaseSyncPhase` (zaten var), Sync hatası →
`TryConsumeSyncFailure`, gönderim hatası → `SyncAsync` içinde geri açma.

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (SyncAsync ~660, CanSync ~681, CanRebuildOrRetry ~627)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs` (OnSyncStarted ~167, ReleaseSyncPhase ~196, TryConsumeSyncFailure ~234)
- Test: `tests/BuildOrchestrator.Tests/App/RunViewModelTests.cs`

**Interfaces:**
- Produces: `private bool SyncBusy => _syncRequested || _syncInFlight;` — Task 2 ve Task 3 bunu kullanır.
- Produces: `private bool _syncRequested;` alanı (RunViewModel.Workspace.cs'te, `_syncInFlight`'ın yanında).

- [ ] **Step 1: Kırmızı testleri yaz** (RunViewModelTests.cs'e; mevcut `NeverTickingBatcher` + `EngineHost(TestPaths.SupervisorExe)` deseni — engine hiç başlatılmaz, `OnEvent` doğrudan sürülür):

```csharp
[Fact]
public async Task Sync_cannot_be_triggered_again_while_one_is_in_flight()
{
    // [Sync guard] syncStarted geldi, syncCompleted gelmedi: ikinci Sync kuyruğa ikinci bir tam
    // analiz eklerdi (scan+graph+topo+iki incremental pass) — kapı kapalı olmalı.
    await using var engine = new EngineHost(TestPaths.SupervisorExe);
    var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
    vm.OnEvent(new SyncStartedEvent(@"C:\repo", "main"));
    Assert.False(vm.SyncCommand.CanExecute(null));

    vm.OnEvent(new SyncCompletedEvent("main", "abc123", FetchDegraded: false, ProjectCount: 1, CycleCount: 0));
    Assert.True(vm.SyncCommand.CanExecute(null)); // Sync bitti — kapı geri açık
}

[Fact]
public async Task A_second_Sync_is_gated_the_moment_the_first_is_requested()
{
    // [Sync guard] Tıklama ile syncStarted arasındaki pencere: bayrak GÖNDERİMDEN ÖNCE senkron
    // kurulmalı (BeginRunAsync'in IsStarting simetriği). DebugOnCommandSent gönderimden hemen önce
    // senkron tetiklenir — o anda kapı kapalı olmalı.
    await using var engine = new EngineHost(TestPaths.SupervisorExe);
    var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
    bool? canExecuteDuringSend = null;
    vm.DebugOnCommandSent = cmd =>
    {
        if (cmd is SyncWorkspaceCommand) canExecuteDuringSend = vm.SyncCommand.CanExecute(null);
    };

    await vm.SyncCommand.ExecuteAsync(null); // engine başlatılmadı → gönderim senkron düşer

    Assert.False(canExecuteDuringSend);            // istek uçuştayken kapı kapalıydı
    Assert.True(vm.SyncCommand.CanExecute(null));  // gönderim düştü → bayrak geri açıldı, kalıcı kilit yok
}

[Fact]
public async Task Engine_death_mid_sync_reopens_the_Sync_gate()
{
    // [Sync guard] _syncInFlight sızarsa Sync düğmesi kalıcı pasif kalırdı — ReleaseSyncPhase
    // (OnEngineExited yolu) yeni bayrağı da bırakmalı.
    await using var engine = new EngineHost(TestPaths.SupervisorExe);
    var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
    vm.OnEvent(new SyncStartedEvent(@"C:\repo", "main"));
    Assert.False(vm.SyncCommand.CanExecute(null));

    vm.OnEngineExited(1);
    Assert.True(vm.SyncCommand.CanExecute(null));
}
```

- [ ] **Step 2: Kırmızıyı göster** — `dotnet test ... --filter "FullyQualifiedName~RunViewModelTests.Sync_cannot|FullyQualifiedName~RunViewModelTests.A_second_Sync|FullyQualifiedName~RunViewModelTests.Engine_death_mid_sync"` → 3 test FAIL (CanExecute true dönüyor).

- [ ] **Step 3: Uygula.**

RunViewModel.Workspace.cs — `_syncInFlight` tanımının (satır ~97) hemen altına:

```csharp
/// <summary>[Sync guard] Sync İSTENDİ ama motor henüz <c>syncStarted</c> ile cevap vermedi —
/// <see cref="RunViewModel.SyncAsync"/> gönderimden ÖNCE senkron kurar (BeginRunAsync'in IsStarting
/// simetriği). Tıklama→syncStarted penceresinde ikinci bir Sync'i keser; <c>syncStarted</c> gelince
/// nöbeti <see cref="_syncInFlight"/> devralır. Gönderim senkron düşerse hemen geri açılır
/// (hiçbir engine event'i gelmeyecek — kalıcı kilit bırakılamaz).</summary>
private bool _syncRequested;

/// <summary>[Sync guard] Sync yüzeyi meşgul mü — istek uçuşta (<see cref="_syncRequested"/>) YA DA
/// <c>syncStarted</c> görüldü (<see cref="_syncInFlight"/>). Sync/Rebuild/Build/Cycles kapıları
/// TEK predicate'i okur (kopya YASAK).</summary>
private bool SyncBusy => _syncRequested || _syncInFlight;
```

`OnSyncStarted` başına: `_syncRequested = false;` (nöbet `_syncInFlight`'a geçti) ve metodun sonundaki
Notify bloğuna `SyncCommand.NotifyCanExecuteChanged();` eklenir. `ReleaseSyncPhase` ve
`TryConsumeSyncFailure`'ın `_syncInFlight = false` yazan dallarına da `_syncRequested = false;` +
`SyncCommand.NotifyCanExecuteChanged();` eklenir.

RunViewModel.cs — `SyncAsync` gövdesi:

```csharp
[RelayCommand(CanExecute = nameof(CanSync))]
private async Task SyncAsync()
{
    SelectedProjectId = null; // [design doSync] seçim temizlenir, filtre KORUNUR
    // [Sync guard] Bayrak GÖNDERİMDEN ÖNCE: tıklama→syncStarted penceresinde ikinci basışı keser.
    _syncRequested = true;
    SyncCommand.NotifyCanExecuteChanged();
    ArmEngineWatchdog(); // Sync istendi — motor bundan sonra konuşmalı (Task 2 bunu okur)
    if (!await TrySendAsync(
        new SyncWorkspaceCommand(RootPath, Branch, LayerPatterns, Configuration), "sync"))
    {
        // Gönderim senkron düştü: hiçbir syncStarted gelmeyecek — bayrak asılı bırakılamaz.
        _syncRequested = false;
        SyncCommand.NotifyCanExecuteChanged();
        return; // engine zaten ölü/erişilmez — envanter komutlarını da göndermenin anlamı yok
    }
    // ... mevcut listBranches / listWorktrees gönderimleri ve yorumları AYNEN kalır ...
}
private bool CanSync() => !IsRunning && !IsStarting && !IsEngineUnavailable && !SyncBusy;
```

`CanRebuildOrRetry` `_syncInFlight` yerine `SyncBusy` okur (yorumu güncelle: kapı artık istek
penceresini de kapsar):

```csharp
private bool CanRebuildOrRetry() => CanStartRun() && !SyncBusy;
```

- [ ] **Step 4: Testler yeşil** — Step 2'deki filtre PASS; sonra tam süit: `dotnet test ... --filter "Category!=Acceptance"` yeşil.

- [ ] **Step 5: Commit** — `fix(sync): cift tetiklenme kapisi — istek penceresi dahil tek SyncBusy predicate'i`

---

### Task 2: Watchdog Sync penceresini kapsar

**Durum:** [RunViewModel.cs:938](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L938)
`WaitingOnEngine => IsStarting || Phase == AppPhase.Stopping`. Sync de "motordan bir geçiş bekleniyor"
penceresidir: motor Sync ortasında donarsa şerit sonsuza dek `▸ Sync — git fetch origin…` der ve
"Restart engine" kapısı hiç görünmez. Git çağrıları 30 sn timeout'lu
([GitService.cs:72](src/BuildOrchestrator.Core/Git/GitService.cs#L72), mevcut kod — değişmez) ama donmuş
motor hiç event üretmez; 90 sn eşiği (EngineSilenceThresholdMs) bunu yakalamalı. Task 1'in bayrağı
sızarsa (donmuş-ama-yaşayan motor) Sync düğmesi kalıcı pasif kalır — çıkış kapısı Restart engine'dir ve
`RestartEngineAsync → ReleaseAfterEngineLoss → ReleaseSyncPhase` zinciri bayrağı zaten bırakır; eksik
olan tek şey kapının GÖRÜNMESİdir.

**Karar:** `WaitingOnEngine`'e Sync penceresi eklenir: `Phase == AppPhase.Syncing` (syncStarted sonrası)
ve `_syncRequested` (tıklama→syncStarted arası). Saat kurma noktaları: `SyncAsync` (Task 1'de eklendi)
ve `OnPhaseChanged`'ın Syncing dalı (Stopping ile aynı yerde — faz set eden her yol oradan geçer).

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (WaitingOnEngine ~938, XML doc'u dahil)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs` (OnPhaseChanged ~72)
- Test: `tests/BuildOrchestrator.Tests/App/RunViewModelTests.cs`

**Interfaces:**
- Consumes: Task 1'in `_syncRequested` alanı ve `SyncAsync` içindeki `ArmEngineWatchdog()` çağrısı.

- [ ] **Step 1: Kırmızı test** (deterministik saat enjeksiyonu — D8, sleep/poll yok):

```csharp
[Fact]
public async Task Engine_silence_during_sync_raises_the_overdue_gate()
{
    // [watchdog] Sync tam olarak "motordan bir geçiş bekleniyor" penceresidir: motor Sync ortasında
    // donarsa amber uyarı + "Restart engine" kapısı görünmeli (eskiden yalnız IsStarting/Stopping'te).
    long now = 0;
    await using var engine = new EngineHost(TestPaths.SupervisorExe);
    var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => now);
    vm.OnEvent(new SyncStartedEvent(@"C:\repo", "main")); // saat sıfırlanır, faz Syncing

    now += RunViewModel.EngineSilenceThresholdMs;
    vm.TickElapsed();

    Assert.Equal(RunViewModel.EngineSilentMessage, vm.EngineOverdueMessage);
}

[Fact]
public async Task Sync_progress_events_keep_resetting_the_silence_clock()
{
    // [watchdog] Konuşan bir motor "gecikmiş" sayılmaz: her SyncProgressEvent saati sıfırlar —
    // yavaş ama canlı bir Sync (ör. büyük fetch) uyarı üretmemeli.
    long now = 0;
    await using var engine = new EngineHost(TestPaths.SupervisorExe);
    var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1", () => now);
    vm.OnEvent(new SyncStartedEvent(@"C:\repo", "main"));

    now += RunViewModel.EngineSilenceThresholdMs - 1;
    vm.OnEvent(new SyncProgressEvent("git fetch origin main", "dim")); // motor konuştu
    now += RunViewModel.EngineSilenceThresholdMs - 1;
    vm.TickElapsed();

    Assert.Null(vm.EngineOverdueMessage);
}
```

- [ ] **Step 2: Kırmızıyı göster** — ilk test FAIL (EngineOverdueMessage null kalıyor: Syncing kümede yok). İkinci test bugün de yeşil olabilir (saat sıfırlama zaten `OnEvent`'te) — o bir koruma pini; kırmızı zorunluluğu İLK test için geçerli.

- [ ] **Step 3: Uygula.**

```csharp
/// <summary>Bir GEÇİŞ bekleniyor mu: run istendi ama <c>runStarted</c> gelmedi (<see cref="IsStarting"/>),
/// stop istendi ama <c>runStopped</c> gelmedi (faz <see cref="AppPhase.Stopping"/>), ya da Sync istendi/
/// koşuyor ama <c>syncCompleted</c> gelmedi (<see cref="_syncRequested"/> / faz <see cref="AppPhase.Syncing"/>).
/// Watchdog YALNIZ bu pencerelerde kuruludur — koşan bir run'da tek bir projenin sessizce dakikalarca
/// derlenmesi meşrudur ve orada uyarmak kullanıcıyı sağlıklı bir build'i öldürmeye davet ederdi. Sync'in
/// git çağrıları 30 sn'de zaman aşımına uğrar (GitService.CommandTimeout) — 90 sn'lik eşiğe takılan motor
/// "yavaş" değil "donmuş"tur.</summary>
private bool WaitingOnEngine =>
    IsStarting || Phase is AppPhase.Stopping or AppPhase.Syncing || _syncRequested;
```

`OnPhaseChanged` (Workspace.cs): `if (value is AppPhase.Stopping or AppPhase.Syncing) ArmEngineWatchdog();`
(yorumunu iki pencereyi de anlatacak şekilde güncelle).

- [ ] **Step 4: Testler yeşil** + tam süit yeşil.
- [ ] **Step 5: ARCHITECTURE.md güncelle** — watchdog'u anlatan bölüm (silence watchdog / §11 civarı; `EngineSilenceThresholdMs` aranarak bulunur) "yalnız Starting/Stopping" diyorsa Sync penceresi eklenerek YERİNDE yeniden yazılır (anlatı üslubu, changelog yok). Demiyorsa dokunulmaz.
- [ ] **Step 6: Commit** — `fix(watchdog): sessizlik bekcisi Sync penceresini de kapsar`

---

### Task 3: Sync uçarken Build de kilitlenir

**Durum:** Supervisor, Sync boyunca komut döngüsünü BLOKLAR
([SupervisorHost.cs:117-119](src/BuildOrchestrator.Supervisor/SupervisorHost.cs#L117-L119), mevcut kod —
değişmez). Sync uçarken Build'e basılırsa `BeginRunAsync` konsol tamponlarını hemen temizleyip
"build requested" yazar ([RunViewModel.cs:563-597](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L563-L597)),
ama motor hâlâ Sync'in içindedir: kalan `SyncProgressEvent` satırları yeni run dokümanına akar → karışmış
anlatı. Rebuild/Cycles bu yüzden bloklanmıştı; Build bilerek serbest bırakılmıştı
([RunViewModel.cs:625-626](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L625-L626) — prototip
paritesi, BuildApp.jsx:1194). Bu bilinçli kararın bedeli tam da düzeltilmek istenen davranış.

**Karar:** Build de aynı kapıya girer: `BuildCommand`'ın CanExecute'u `CanRebuildOrRetry` olur (Task 1'in
`SyncBusy`'sini içerir). Prototipten bilinçli ayrılıştır — gerekçesi kodda yorumla, ARCHITECTURE.md'nin
bilinçli kararlar bölümünde de kayda geçer. Eski asimetriyi pinleyen bir test varsa YENİ kuralı pinleyecek
şekilde yeniden yazılır (sessiz silme yok; doc'una eski iddia + değişme gerekçesi yazılır).

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (BuildAsync attribute ~629, satır 622-627 yorumu)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs` (OnSyncStarted/ReleaseSyncPhase/TryConsumeSyncFailure Notify blokları — BuildCommand eklenir)
- Modify: `ARCHITECTURE.md` (bilinçli kararlar/known-tradeoffs bölümündeki "Build mid-Sync serbest" kaydı — varsa yerinde yeniden yazılır)
- Test: `tests/BuildOrchestrator.Tests/App/RunViewModelTests.cs`

- [ ] **Step 1: Eski asimetriyi pinleyen testi bul** — `Grep "syncInFlight|SyncInFlight" tests/` ve `Grep "BuildCommand.CanExecute" tests/`. Varsa yeni kurala göre yeniden yazılacak listeye al.
- [ ] **Step 2: Kırmızı test:**

```csharp
[Fact]
public async Task Build_is_gated_while_a_sync_is_in_flight()
{
    // [DEĞİŞEN KURAL] Prototip (BuildApp.jsx:1194) Build'i mid-Sync serbest bırakır; üretim bilerek
    // AYRILIR: Supervisor Sync boyunca komut döngüsünü blokladığından mid-Sync Build, tamponları
    // temizleyip kalan SyncProgressEvent satırlarını yeni run dokümanına karıştırıyordu (ölçüldü).
    await using var engine = new EngineHost(TestPaths.SupervisorExe);
    var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
    var node = new ProjectNode(@"C:\p\a.csproj", "A", @"C:\p\a.csproj",
        SolutionNames: [], Dependencies: [], BuildOrder: 0,
        LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);
    vm.OnEvent(new WorkspaceTopologyEvent([node], [], [], [])); // HasTopology kapısı açık
    Assert.True(vm.BuildCommand.CanExecute(null));

    vm.OnEvent(new SyncStartedEvent(@"C:\repo", "main"));
    Assert.False(vm.BuildCommand.CanExecute(null)); // Sync uçuşta — Build de bekler

    vm.OnEvent(new SyncCompletedEvent("main", "abc123", FetchDegraded: false, ProjectCount: 1, CycleCount: 0));
    Assert.True(vm.BuildCommand.CanExecute(null));
}
```

- [ ] **Step 3: Kırmızıyı göster** — FAIL (Build guard'sız).
- [ ] **Step 4: Uygula** — `BuildAsync`'in attribute'u: `[RelayCommand(CanExecute = nameof(CanRebuildOrRetry))]`.
  622-627 arasındaki "Build BİLEREK dahil DEĞİL" yorumu yeni kararı anlatacak şekilde yeniden yazılır
  (prototipten ayrılış + gerekçe: Supervisor'ın bloklayan Sync'i + transkript karışması). OnSyncStarted /
  ReleaseSyncPhase / TryConsumeSyncFailure'ın Notify bloklarına `BuildCommand.NotifyCanExecuteChanged();`
  eklenir (RelayCommand RequerySuggested'a abone olmaz — mevcut Rebuild/Cycles satırlarının simetriği).
- [ ] **Step 5: Testler + tam süit yeşil.** Step 1'de bulunan eski pin testleri yeni kurala taşındı mı kontrol et.
- [ ] **Step 6: ARCHITECTURE.md** — "Build mid-Sync serbest" bilinçli-karar kaydı varsa yerinde yeniden yazılır: kural artık "Sync uçarken hiçbir run başlatılamaz".
- [ ] **Step 7: Commit** — `fix(sync): Build de Sync penceresinde kilitlenir — transkript karismasi kapandi`

---

### Task 4: Satır araması tek yerde, case-insensitive

**Durum:** [RunViewModel.cs:1117](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L1117)
(`OnProjectDone`: `p.Id == projectId`) ve [RunViewModel.cs:1140](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L1140)
(`EnsureRow`: `p.Id == id`) case-SENSITIVE. Aynı dosyada diğer tüm aramalar/sözlükler `OrdinalIgnoreCase`
(gerekçe yorumlarda: proje Id'leri Windows dosya yollarıdır). Casing ayrışırsa bedel sessiz: `OnProjectDone`
satırı bulamaz (savunmacı return) → satır sonsuza dek "building"; `EnsureRow` kopya satır yaratır.
Ayrıca aynı `FirstOrDefault(... OrdinalIgnoreCase)` deseni 4+ yerde inline kopyalanmış
(OnCycleCompleted:1109, OnProjectStarted:1074, ShortNameFor:1588) — kural tek yerde durmalı.

**Karar:** Tek helper: `FindRow(string id)`. İki hatalı yer + inline kopyalar ona bağlanır.

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs`
- Test: `tests/BuildOrchestrator.Tests/App/RunViewModelTests.cs`

**Interfaces:**
- Produces: `private ProjectRowViewModel? FindRow(string id)` — OrdinalIgnoreCase, tek arama kuralı.

- [ ] **Step 1: Kırmızı testler:**

```csharp
[Fact]
public async Task Project_completion_matches_the_row_case_insensitively()
{
    // Proje Id'leri Windows dosya yollarıdır: motor farklı casing yayınlarsa satır "building"de
    // asılı kalıyordu (OnProjectDone'ın savunmacı return'ü satırı hiç bulamaz).
    await using var engine = new EngineHost(TestPaths.SupervisorExe);
    var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
    vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

    vm.OnEvent(new ProjectSucceededEvent("r1", @"C:\P\A.CSPROJ", 1200));

    var row = Assert.Single(vm.Projects);
    Assert.Equal(ProjectRowState.Succeeded, row.State);
    Assert.Equal(1200, row.DurationMs);
}

[Fact]
public async Task A_differently_cased_id_does_not_create_a_duplicate_row()
{
    // EnsureRow aynı projeyi casing farkı yüzünden ikinci kez yaratmamalı (kopya satır).
    await using var engine = new EngineHost(TestPaths.SupervisorExe);
    var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
    vm.OnEvent(new ProjectStartedEvent("r1", @"C:\p\a.csproj", "A"));

    vm.OnEvent(new ProjectSkippedEvent("r1", @"C:\P\A.CSPROJ", "up to date"));

    Assert.Single(vm.Projects);
}
```

- [ ] **Step 2: Kırmızıyı göster** — ikisi de FAIL (ilkinde State=Started kalır; ikincisinde Count=2).
- [ ] **Step 3: Uygula:**

```csharp
/// <summary>Satır aramasının TEK kuralı: proje Id'leri Windows dosya yollarıdır →
/// <see cref="StringComparison.OrdinalIgnoreCase"/>. Inline FirstOrDefault kopyaları buraya bağlanır —
/// iki yer (OnProjectDone/EnsureRow) case-sensitive kalmıştı ve casing ayrışırsa satır sonsuza dek
/// "building"de kalıyor / kopya satır doğuyordu.</summary>
private ProjectRowViewModel? FindRow(string id) =>
    Projects.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
```

Bağlanan yerler: `OnProjectDone` (1117), `EnsureRow` (1140), `OnCycleCompleted` (1109),
`OnProjectStarted`'ın sibling araması (1074 — `FindRow(sibling)` + `is { State: Started }` deseni korunur),
`ShortNameFor` (1588). Davranış değişikliği yalnız ilk ikisinde; diğerleri saf DRY (davranış aynı).

- [ ] **Step 4: Testler + tam süit yeşil.**
- [ ] **Step 5: Commit** — `fix(rows): satir aramasi tek helper'da ve case-insensitive (Windows yol Id'leri)`

---

### Task 5: `ConsoleView._trimTail` ölü alanı ve bayat yorum

**Durum:** 4cc9784 tail-trim koşulunu `_bottomAnchor.ShouldFollow`'a çevirdi
([ConsoleView.xaml.cs:235](src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs#L235)); `_trimTail`'i artık
kimse OKUMUYOR. Kalanlar: tanım [satır 79](src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs#L79), iki
atama ([471](src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs#L471) run modu,
[508](src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs#L508) proje modu) ve
[satır 241](src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs#L241)'deki artık yanlış atıf
("run modunun (_trimTail) chunk bookkeeping'i yoktur"). Davranış kaybı yok (analiz: proje modu
`ForceStuck(false)` ile açılır → takip kapalı → kırpma yok; ayrım ShouldFollow'da zaten yaşıyor).
CS0414 uyarısı da kalkar.

**Not:** Task 6 bu dosyanın aynı bölgesine dokunur — Task 5 ÖNCE yapılır, Task 6 temiz zeminde çalışır.

**Karar:** Alan + iki atama silinir; 241'deki yorum `_projectMode` üzerinden yeniden yazılır. Bu bir
davranış fix'i değil kural silme — kırmızı test yerine mevcut davranışı pinleyen karakterizasyon testi
eklenir (bugün ShouldFollow'un proje modundaki trim davranışını pinleyen test yok), silme işlemi o test
yeşilken yapılır.

**Files:**
- Modify: `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs`
- Test: `tests/BuildOrchestrator.Tests/App/ConsoleViewTests.cs` (mevcut STA/realize desenleri kullanılır)

- [ ] **Step 1: Karakterizasyon testi ekle** (ConsoleViewTests'teki mevcut host/fixture desenine uy — dosyadaki diğer testlerin kurulumunu birebir izle):

```csharp
[WpfFact] // dosyadaki mevcut STA test attribute'u hangisiyse o kullanılır
public void Project_mode_does_not_trim_appended_lines_while_follow_is_off()
{
    // [tail-trim tek kural] Proje modu ForceStuck(false) ile açılır → ShouldFollow kapalı → canlı
    // append KIRPMAZ (chunk loader eski satırları yönetir). Bu pin, _trimTail alanı silinirken
    // davranışın ShouldFollow'da yaşadığını kanıtlar.
    var view = new ConsoleView();
    Realize(view); // dosyadaki mevcut realize helper'ı
    view.PlayCascade(Enumerable.Range(0, 10).Select(i => $"line {i}").ToList());

    view.AppendBatch(string.Concat(Enumerable.Range(0, ConsoleView.RenderSliceLines + 50)
        .Select(i => $"live {i}\n")));

    Assert.True(view.Editor.Document.LineCount > ConsoleView.RenderSliceLines); // kırpılmadı
}
```

- [ ] **Step 2: Testi koş** — YEŞİL olmalı (mevcut davranışı pinliyor; kırmızıysa analiz yanlış demektir → dur, kullanıcıya bildir).
- [ ] **Step 3: Sil** — satır 79 tanımı, 471 ve 508 atamaları. Satır 241 yorumu şöyle yeniden yazılır:
  `// ... Yalnız proje modunda: run modunun chunk bookkeeping'i yoktur (_projectAllLines boş, _loadedFrom=0).`
  (Task 6 bu cümleyi zaten yeniden ele alacak — Task 5 yalnız `_trimTail` atfını düşürür.)
- [ ] **Step 4: Derle + tam süit yeşil** (CS0414 uyarısının gittiğini build çıktısında doğrula).
- [ ] **Step 5: Commit** — `refactor(console): olu _trimTail alani silindi — tail-trim tek kurali ShouldFollow`

---

### Task 6: Run konsolu geçmişi geri getirilebilir olur (yeni bulgu)

**Durum (kullanıcı gözlemi):** "Konsola bir sürü şey basılıyor ama en sonda bakıyorum azıcık içerik var;
paralel [projeler] hiçbiri birbirini ezmeden yazabilmeli."

**Analiz (kod doğrulaması):** Veri KAYBOLMUYOR ve kimse kimseyi ezmiyor:
- Her projenin TAM logu ayrı tutulur (`_projectText`/`_liveLines`, RunViewModel.cs:265-266) ve kartına
  tıklanınca chunk loader'lı tam sayfa açılır — paralel yazımlar `_gate` kilidi altında satır satır ayrışır.
- Run anlatısı (`_runText`) da TAM transkripti tutar (RunViewModel.cs:1406 — proje modundayken bile birikir).

Görünen kayıp SUNUMDA: canlı append'te belge son 200 satıra kırpılır
([ConsoleView.xaml.cs:235-237](src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs#L235-L237),
`RenderSliceLines = 200`) ve `ShowRunDocument` da yalnız son 200 satırı tohumlar
([ConsoleView.xaml.cs:474](src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs#L474)). Proje modunda
kırpılan satırları chunk loader geri getirir; **run modunda chunk loader yoktur**
([ConsoleView.xaml.cs:680](src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs#L680) `if (!_projectMode ...) return;`)
— yukarı kaydırınca 200 satırdan öncesi ERİŞİLEMEZ. Paralel build'de yüzlerce satır/sn aktığı için 200
satırlık pencere saniyeler içinde dolar → "bir sürü şey basıldı, azıcık kaldı" algısı birebir bu.
Ek boşluk: proje modunda da canlı gelen satırlar backlog'a (`_projectAllLines`) eklenmediğinden, takip
açıkken kırpılan CANLI satırlar oradan da geri getirilemez (satır 242'deki üst-sınır clamp'i bunu itiraf
eder: "index'i yok").

**Karar:** Chunk loader iki modda da çalışır; backlog canlı büyür.
1. `_projectAllLines` → `List<string> _backlogLines` (mod-bağımsız backlog).
2. `ShowRunDocument(fullRunText)`: tam metni satırlara böler → backlog; belgeye son 200 satır;
   `_loadedFrom = max(0, count-200)`. (MainWindow zaten `GetRunDocumentText()` ile TAM metni veriyor —
   VM tarafında değişiklik YOK.)
3. `EvaluateChunkScroll`: `_projectMode` şartı kalkar (iki modda da tepeye kaydırınca prepend).
4. `AppendBatch`: `TrimToRenderSlice` kırptığı METNİ döndürür; kırpılan satırlardan backlog'un ucunu aşanlar
   (`_loadedFrom + K > _backlogLines.Count` taşması) backlog'a EKLENİR, `_loadedFrom += K` (clamp kalkar —
   artık delik oluşamaz). Bu, proje modundaki mevcut canlı-satır deliğini de kapatır.
5. Takip davranışı DEĞİŞMEZ: run modunda kullanıcı dibe dönünce/bekleme dolunca kırpma yetişir (mevcut
   kural); fark, kırpılanın artık backlog üzerinden geri kaydırılabilir olması.

**Files:**
- Modify: `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` (alanlar 83-84, AppendBatch 220-256,
  TrimToRenderSlice 287-294, ShowRunDocument 466-478, PlayCascade 498-517, EvaluateChunkScroll 678-687,
  PrependPreviousChunk 715-740, ilgili XML doc'lar)
- Modify: `ARCHITECTURE.md` §13.5 (satır 1498-1499: "the full log is on disk and is paged in on demand" —
  run anlatısı için kaynak VM tamponudur; madde iki modu da anlatacak şekilde yerinde yeniden yazılır)
- Test: `tests/BuildOrchestrator.Tests/App/ConsoleViewTests.cs`

**Interfaces:**
- Consumes: `ShowRunDocument(string fullRunText)` imzası DEĞİŞMEZ (MainWindow/testler kırılmaz).
- Produces: `TrimToRenderSlice(TextDocument)` → dönüş tipi `(int Lines, string RemovedText)` (yalnız
  ConsoleView içinde kullanılır, private).

- [ ] **Step 1: Kırmızı testler:**

```csharp
[WpfFact]
public void Run_mode_scroll_to_top_prepends_older_history()
{
    // [run backlog] Run anlatısı da chunk loader'lıdır: 200 satırlık render dilimi bir SINIR değil
    // PENCEREdir — tepeye kaydırınca önceki dilim geri gelir (proje moduyla aynı jest).
    var view = new ConsoleView();
    Realize(view);
    string full = string.Concat(Enumerable.Range(0, 500).Select(i => $"narrative {i}\n"));
    view.ShowRunDocument(full);
    Assert.Equal(ConsoleView.RenderSliceLines, view.Editor.Document.LineCount - 1); // son 200 + boş prompt satırı

    view.EvaluateChunkScroll(100.0); // kullanıcı tepeden uzaklaştı — arm
    view.EvaluateChunkScroll(0.0);   // tepeye vurdu — önceki chunk

    Assert.Contains("narrative 100", view.Editor.Document.Text); // 300..500 → 100..500 yüklendi
    Assert.NotNull(view.LastPrepend);
}

[WpfFact]
public void Lines_trimmed_while_following_stay_reachable_through_the_backlog()
{
    // [run backlog] Canlı kırpılan satır KAYBOLMAZ: kırpılan metin backlog'a taşınır, tepeye
    // kaydırınca deliksiz geri gelir. (Eski davranış: run modunda 200'den öncesi erişilemezdi.)
    var view = new ConsoleView();
    Realize(view);
    view.ShowRunDocument(""); // boş başla — takip açık (ForceStuck(true))
    view.AppendBatch(string.Concat(Enumerable.Range(0, 300).Select(i => $"live {i}\n")));
    Assert.True(view.Editor.Document.LineCount <= ConsoleView.RenderSliceLines + 1); // kırpıldı

    view.EvaluateChunkScroll(100.0);
    view.EvaluateChunkScroll(0.0);

    Assert.Contains("live 0", view.Editor.Document.Text); // kırpılan baş, backlog'dan geri geldi
}
```

(Assert ayrıntıları — prompt satırı / LineCount off-by-one — mevcut ConsoleViewTests'in AppendBatch
testlerindeki sayıma göre ayarlanır; iddianın özü: prepend sonrası eski satırlar belgededir.)

- [ ] **Step 2: Kırmızıyı göster** — ikisi de FAIL (EvaluateChunkScroll run modunda erken döner; backlog boş).
- [ ] **Step 3: Uygula** — Karar bloğundaki 1-4 maddeleri. `TrimToRenderSlice` önce kırpılacak aralığın
  metnini `document.GetText(0, removeLength)` ile alır, sonra siler, `(excess, removedText)` döndürür.
  `AppendBatch` içinde:

```csharp
if (_bottomAnchor.ShouldFollow)
{
    var (trimmed, removedText) = TrimToRenderSlice(document);
    if (trimmed > 0)
    {
        // [run backlog] Kırpılan CANLI satırlar backlog'un ucunu aşıyorsa oraya taşınır — belge
        // penceresinden çıkan hiçbir satır erişilmez kalmaz (eski clamp "index'i yok" diyip delik
        // bırakıyordu; delik artık tanım gereği oluşamaz).
        int overflow = _loadedFrom + trimmed - _backlogLines.Count;
        if (overflow > 0)
            _backlogLines.AddRange(SplitTrailingLines(removedText, overflow));
        _loadedFrom += trimmed;
    }
}
```

  `SplitTrailingLines(string text, int lastN)`: metnin SON `lastN` satırını döndüren küçük private helper
  (`'\n'` sonekli sözleşme — Join'in tersi). `PlayCascade` `_backlogLines = new List<string>(allLines)`
  yapar (parametre imzası `IReadOnlyList<string>` kalır). `EvaluateChunkScroll`'daki `!_projectMode` şartı
  kalkar; XML doc'lar iki modu anlatacak şekilde güncellenir.
- [ ] **Step 4: Testler + tam süit yeşil** (özellikle mevcut ConsoleViewTests / ChunkStitchTests /
  ConsoleRenderSliceTests ve Task 5'in karakterizasyon pini).
- [ ] **Step 5: ARCHITECTURE.md §13.5** — render-slice maddesi yerinde yeniden yazılır: "The live document
  is capped at a render slice of 200 lines; scrolling to the top pages older history back in — from the
  view-model's full run buffer in narrative mode, from the on-disk log in project mode (§5.5)." (dil
  anlatı üslubunda, rakam 200 zaten kodda sabit — `RenderSliceLines`).
- [ ] **Step 6: Commit** — `feat(console): run anlatisi da geriye kaydirilabilir — backlog canli buyur, kirpilan satir kaybolmaz`

---

### Task 7: ARCHITECTURE.md §13.5 iç çelişki (tek satır)

**Durum:** [ARCHITECTURE.md:1495](ARCHITECTURE.md#L1495) "The document stays **plain text**
(`HH:MM:SS ▸ message`)" derken [ARCHITECTURE.md:1500](ARCHITECTURE.md#L1500) "There is no wall-clock column
and no `▸` marker" der. Kod ikincisini uygular (`AppendRunLine`, RunViewModel.cs:1429-1442 — damga da ▸ de
yok). v1.7.0 §2.5 damgayı kaldırırken yeni madde eklenmiş, eski maddedeki format örneği güncellenmemiş.

**Karar:** 1495'teki parantez içi format örneği kaldırılır; asıl iddia (düz metin, kopyalanabilirlik,
offset tabanlı renklendirme) aynen kalır. Kod değişikliği yok, test yok (kaynak guard'ları doküman satırını
pinlemiyor).

**Files:**
- Modify: `ARCHITECTURE.md:1495`

- [ ] **Step 1: Düzelt** — satır 1495:

```markdown
- The document stays **plain text** — a line is just a string — so what the user copies is meaningful.
  Colour comes from an offset-based `DocumentColorizingTransformer`.
```

  (Not: Task 6'nın Step 5'i aynı bölümün 1498-1499 maddesine dokunur — çakışmayı önlemek için bu task
  Task 6'dan SONRA yapılır ya da aynı commit'te birleştirilmez, sırayla uygulanır.)
- [ ] **Step 2: Bölümü baştan sona bir kez oku** — başka bayat format iddiası kalmadığını doğrula
  (özellikle §2.5'e atıf yapan yerler).
- [ ] **Step 3: Commit** — `docs: §13.5 bayat format ornegi kaldirildi (duvar saati/ok isareti v1.7.0'da kalkti)`

---

## Kapanış (plan yürütücüsü için)

- [ ] Tam süit: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"` → yeşil.
- [ ] Acceptance ayrı koşulur (kullanıcı onayıyla; uygulama KAPALI olmalı): `--filter "Category=Acceptance"`.
- [ ] `superpowers:finishing-a-development-branch` — branch `main`'e merge + push; merge doğrulandıktan sonra branch local+remote silinir; oturum `main`'de biter.

## Bilinçli kapsam dışı

- Run anlatısında proje-başına gruplama/renk ayrımı ("ezmeden yazma"nın alternatif okuması): anlatı
  BİLEREK kronolojik tek akıştır; proje-başına ayrık okuma zaten kart sayfasının işi. Kullanıcı gruplamayı
  ayrıca isterse ayrı bir tasarım işi olarak ele alınır.
- Supervisor'ın Sync-bloklayan komut döngüsü değişmez (SupervisorHost.cs:117 gerekçesi geçerli): App
  tarafındaki kapılar (Task 1-3) pencereyi zaten kapatıyor.
