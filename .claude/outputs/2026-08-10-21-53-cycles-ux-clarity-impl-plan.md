# Cycles UX Clarity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cycles koşusunun ekrandaki anlatısını düzeltmek — bekleyen üye derleniyormuş gibi görünmesin, skip gerekçeleri görünür olsun, grup kararı stream'e düşsün, graf düğümü standart kalıp üyeliği kalıcı mini rozetle anlatsın.

**Architecture:** Motor (Supervisor/Core planlama) davranışı DEĞİŞMEZ — tek istisna skip Reason metinlerinin normalize edilip Contracts'ta tek kaynağa alınması ve yeni bir `CycleCompletedEvent`'in yayınlanması. Geri kalan her şey App sunum katmanı (ProjectRow, stream, graf).

**Tech Stack:** .NET 10, WPF, xUnit (headless + STA realize testleri), NDJSON IPC (System.Text.Json polymorphism, camelCase enum metinleri).

**Spec:** `.claude/outputs/2026-08-10-20-10-cycles-ux-redesign-plan.md` (analiz + sahne sahne hedef UX).

## Global Constraints

- **Kırmızı test kuralı:** hiçbir fix, kusuru yakalayan test KIRMIZI verdiği gösterilmeden yapılmaz.
- **Davranış değişince testi de değişir:** eski kuralı pinleyen test silinmez/gevşetilmez; YENİ kuralı pinleyecek şekilde yeniden yazılır ve doc yorumuna eski iddia + değişme gerekçesi yazılır (`[DEĞİŞEN KURAL]` deseni).
- **Kopya YASAK:** aynı değer/metin iki yerde tanımlanmaz. Skip reason literalleri Contracts'taki tek kaynaktan okunur.
- **Dil:** kod, UI metinleri ve loglar İngilizce; kod yorumları Türkçe.
- **stdout yalnız NDJSON** (Supervisor); log/tanı stderr'e.
- **Yeni görsel öğe → realize testi** (headless süit XAML runtime çözümlemesini görmez); realize `window.Content` üzerinde.
- **Süit:** `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"` — bitişte tam yeşil. Uygulama açıkken build alınmaz.
- Test sınıfları/desenleri mevcut dosyalarda: `tests/BuildOrchestrator.Tests/App/ProjectRowTests.cs`, `App/EventStreamTests.cs`, `App/RunViewModelStateTests.cs`, `Supervisor/CycleRoundsTests.cs`, `Ipc/IpcMessagesTests.cs`, graf testleri `App/Graph*` — yeni test aynı dosyanın desenini izler.

---

### Task 1: Bekleyen döngü üyesi derleniyormuş gibi görünmesin (nefes + süre → IsCompiling)

**Files:**
- Modify: `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` (ApplyBreathing ~475, ApplyDuration ~311, OnVmPropertyChanged Status case ~207)
- Test: `tests/BuildOrchestrator.Tests/App/ProjectRowTests.cs`

**Interfaces:**
- Consumes: `ProjectRowViewModel.IsCompiling` (mevcut: `State == Started && !CycleWaiting`, RunViewModel.cs:137). `CycleWaiting` set edilince `Status` ve `IsCompiling` property-changed yayılır.
- Produces: değişen davranış — nefes katmanı ve canlı süre yalnız `IsCompiling` satırda.

**Bağlam:** `IsCompiling` üç yüzeye bağlandı (Status glyph'i, RunCounters, ribbon chip'leri) ama ProjectRow'un iki yüzeyi ham `State == Started` okumaya devam etti. Sonuç: sırasını bekleyen SCC üyesi saat (Queued) glyph'i gösterirken amber nefesle yanıp sönüyor ve süre sütunu canlı sayıyor (üstelik her turda sıfırlanarak).

- [ ] **Step 1: Kırmızı testleri yaz** (ProjectRowTests desenine uy — STA realize, satır VM'i + ProjectRow kur):

```csharp
[StaFact]
public void A_waiting_cycle_member_does_not_breathe()
{
    // KIRMIZI (fix öncesi): nefes katmanı ham State==Started okuyordu → bekleyen üye de yanıp sönüyordu.
    var vm = new ProjectRowViewModel("a.csproj", "A", ProjectRowState.Started) { CycleWaiting = true };
    var row = RealizeRow(vm); // dosyadaki mevcut realize yardımcısını kullan
    Assert.Equal(Visibility.Collapsed, row.BreathLayer.Visibility);
}

[StaFact]
public void A_waiting_cycle_member_shows_no_live_elapsed()
{
    var vm = new ProjectRowViewModel("a.csproj", "A", ProjectRowState.Started) { CycleWaiting = true, DurationMs = 5000 };
    var row = RealizeRow(vm);
    Assert.Equal("—", row.DurationText.Text);
}

[StaFact]
public void The_compiling_member_still_breathes_and_counts()
{
    var vm = new ProjectRowViewModel("a.csproj", "A", ProjectRowState.Started) { DurationMs = 5000 };
    var row = RealizeRow(vm);
    Assert.Equal(Visibility.Visible, row.BreathLayer.Visibility);
    Assert.NotEqual("—", row.DurationText.Text); // canlı elapsed
}

[StaFact]
public void Breathing_stops_the_moment_the_turn_passes_to_a_sibling()
{
    var vm = new ProjectRowViewModel("a.csproj", "A", ProjectRowState.Started);
    var row = RealizeRow(vm);
    vm.CycleWaiting = true; // kardeş başladı — sıra artık onda
    Assert.Equal(Visibility.Collapsed, row.BreathLayer.Visibility);
    Assert.Equal("—", row.DurationText.Text);
}
```

Not: `RealizeRow` benzeri bir yardımcı dosyada yoksa mevcut testlerin kurulum kalıbını aynen kullan; yeni kalıp icat etme. Test adlandırma dosyadaki mevcut stille uyumlu olsun.

- [ ] **Step 2: Koş, KIRMIZI gördüğünü doğrula** (`--filter` ile yalnız yeni testler). Beklenen: 1, 2 ve 4 kırmızı; 3 yeşil.

- [ ] **Step 3: Fix.** `ApplyBreathing`: `bool building = _vm?.IsCompiling ?? false;`. `ApplyDuration`:

```csharp
private void ApplyDuration()
{
    var state = _vm?.State ?? ProjectRowState.Pending;
    long ms = _vm?.DurationMs ?? 0;
    // Canlı elapsed yalnız GERÇEKTEN derlenen satırda; grubunun sırasını bekleyen üye (Started ama
    // IsCompiling değil) "—" gösterir — sayacı her turda sıfırlanıp yeniden koşan bir bekleme süresi
    // bilgi değil gürültüydü. Terminal satır kesin süresini (turların toplamı) gösterir.
    PART_Duration.Text = _vm?.IsCompiling ?? false
        ? DurationFormat.Elapsed(ms)
        : state == ProjectRowState.Started
            ? DurationFormat.Duration(null)
            : DurationFormat.Duration(ms == 0 ? null : ms);
    ...mevcut renk satırı aynen...
}
```

`OnVmPropertyChanged`'in `Status` case'ine `ApplyBreathing(); ApplyDuration();` ekle (CycleWaiting değişimi `Status`'u tetikler; `State` case'i zaten çağırıyor — çift çağrı zararsız, iki metod da idempotent).

- [ ] **Step 4: Yeni testler + ProjectRowTests + RunViewModelStateTests yeşil.** Mevcut bir test "Started → elapsed" iddiasını CycleWaiting'siz pinliyorsa yeşil kalmalı; CycleWaiting'li pinliyorsa `[DEĞİŞEN KURAL]` ile yeniden yaz.

- [ ] **Step 5: Commit** — `fix(app): bekleyen dongu uyesi nefes almiyor, sure saymiyor`

---

### Task 2: Skip gerekçeleri — Contracts'ta tek kaynak, stream'de görünür, kapsam-dışı fırtınası tek satır

**Files:**
- Create: `src/BuildOrchestrator.Contracts/Ipc/SkipReasons.cs`
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (~797-839 reason literalleri), `src/BuildOrchestrator.Core/Scheduling/ReadySetScheduler.cs` (~97), `src/BuildOrchestrator.App/ViewModels/StreamText.cs` (Skipped), `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` (ProjectSkippedEvent case + toplayıcı)
- Test: `tests/BuildOrchestrator.Tests/App/EventStreamTests.cs`, `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs`

**Interfaces:**
- Produces: `public static class SkipReasons { public const string UpToDate = "up to date"; public const string OutOfCycleScope = "not needed by a dependency cycle"; public const string InDependencyCycle = "in dependency cycle"; public const string CycleNonConvergent = "cycle did not converge at this signature"; }` — Contracts, çünkü hem Supervisor/Core yazar hem App okur (Core→Contracts referansı zaten var).
- Produces: `StreamText.Skipped(string name, string reason)` → `"{name} skipped — {reason}"`; `StreamText.OutsideCycleScope(int count)` → `"{count} outside cycle scope — skipped"`.

**Bağlam:** Bugün `StreamText.Skipped` sabit `"skipped — up to date"` basıyor; `ProjectSkippedEvent.Reason` stream'e hiç ulaşmıyor. Cycles koşusunda ~150+ kapsam-dışı proje yanlış gerekçeyle satır satır akıyor. Ayrıca Supervisor'daki reason'ların ikisi `"skipped — "` önekini İÇİNDE taşıyor (`"skipped — up to date"`, `"skipped — not needed by a dependency cycle"`), ikisi taşımıyor — decision.log satırı (`"{name}: skipped — {reason}"`, RunCoordinator.cs:952) çift önek basıyor ("skipped — skipped — up to date"). Normalizasyon: reason'lar HER YERDE yalın ifade; "skipped — " önekini basan katman basar.

- [ ] **Step 1: Kırmızı testler.**
  - EventStreamTests: `ProjectSkippedEvent(reason: SkipReasons.InDependencyCycle)` sür → satır metni `"X skipped — in dependency cycle"` bekle (bugün `"X skipped — up to date"` → KIRMIZI).
  - EventStreamTests: `RunMode.Cycles` koşusu aç (RunStarted+BuildPreview), 3 `OutOfCycleScope` + 1 `UpToDate` skip sür, sonra bir `ProjectStartedEvent` sür → beklenen: out-of-scope için proje-başına satır YOK; ProjectStarted işlenmeden ÖNCE tek `"3 outside cycle scope — skipped"` Info satırı; up-to-date için `"Y skipped — up to date"` satırı VAR. (KIRMIZI: bugün 4 ayrı "up to date" satırı.)
  - EventStreamTests: Build koşusunda toplayıcı ÇALIŞMAZ — `OutOfCycleScope` reason'lı bir skip Build'de gelemez ama `UpToDate` skipler satır satır akmaya devam eder (regresyon pini).
  - CycleRoundsTests: mevcut reason pinleri (`"skipped — up to date"` / `"skipped — not needed by a dependency cycle"`) yalın hâle `[DEĞİŞEN KURAL]` notuyla yeniden yazılır: eski iddia "reason 'skipped — ' önekiyle taşınırdı"; gerekçe "decision.log çift önek basıyordu ve stream katmanı kendi önekini ekliyor — reason artık yalın ifade, tek kaynak SkipReasons".

- [ ] **Step 2: Kırmızıyı gör.**

- [ ] **Step 3: Uygula.**
  - SkipReasons.cs (Türkçe doc yorumu: neden Contracts'ta — yazan Supervisor/Core, okuyan App; kopya YASAK).
  - RunCoordinator: üç literal → sabitler. decision.log formülü DEĞİŞMEZ.
  - ReadySetScheduler: `"in dependency cycle"` → `SkipReasons.InDependencyCycle`.
  - StreamText.Skipped(name, reason); RunViewModel.Stream ProjectSkippedEvent case'i `e.Reason` geçirir.
  - Toplayıcı (RunViewModel.Stream partial'da): `private int _outOfScopeSkips;` + aktif koşu modu alanı (OnRunStarted'ta yakala — `_pendingRunStartMode` BuildPreview'da temizlendiği için AYRI bir alan gerek, ör. `private RunMode? _streamRunMode;` RunStartedEvent'te set, EndRun'da sıfırlanmaz — RunCompleted'a kadar yaşar). ProjectSkippedEvent case: mod Cycles && `e.Reason == SkipReasons.OutOfCycleScope` → `_outOfScopeSkips++; break;` (satır yok). `PushStream`'in BAŞINDA flush: `if (_outOfScopeSkips > 0) { int n = _outOfScopeSkips; _outOfScopeSkips = 0; PushStream(StreamKind.Info, null, StreamText.OutsideCycleScope(n)); }` — sayaç ÖNCE sıfırlanır (recursion guard'ı). RunCompleted her koşuda geldiği için sayaç asla asılı kalmaz.

- [ ] **Step 4: Süit yeşil** (EventStream + CycleRounds + IpcMessages + Supervisor smoke'ları).

- [ ] **Step 5: Commit** — `fix(stream): skip gerekcesi gorunur, kapsam-disi firtinasi tek satir`

---

### Task 3: CycleCompletedEvent — grup kararı ekrana düşer

**Files:**
- Modify: `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` (enum + record + `[JsonDerivedType(typeof(CycleCompletedEvent), "cycleCompleted")]`)
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (`BuildCycleGroupAsync` ~1281 tur sayacı, `RecordCycleOutcome` ~1373 emit)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs`, `src/BuildOrchestrator.App/ViewModels/StreamText.cs`
- Test: `tests/BuildOrchestrator.Tests/Ipc/IpcMessagesTests.cs`, `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs`, `tests/BuildOrchestrator.Tests/App/EventStreamTests.cs`

**Interfaces:**
- Produces (Contracts):

```csharp
/// <summary>[cycles] Bir SCC koşusunun nihai kararı — CycleRoundDecision'ın (Core) wire karşılığı; Continue
/// (yarıda kesilme) bir karar DEĞİLDİR ve bu event hiç yayılmaz. camelCase METİN olarak yazılır.</summary>
public enum CycleOutcome { Converged, NoProgress, CapReached }

/// <summary>[cycles] Bir SCC'nin koşusu bitti. ProjectId = build-order'daki İLK üye (CycleRoundStartedEvent'in
/// lideriyle AYNI — satır tıklanabilir kalır). DurationMs üye sürelerinin toplamıdır; Rounds koşulan tur sayısı;
/// FailedCount SON turun başarısız üye sayısı.</summary>
public sealed record CycleCompletedEvent(string RunId, string ProjectId, CycleOutcome Outcome,
    int MemberCount, int Rounds, int FailedCount, long DurationMs) : IpcEvent;
```

- Produces (App): `StreamText.CycleCompleted(CycleOutcome outcome, int members, int rounds, int failed, long durationMs)`:
  - Converged → `cycle converged — {m} members · {r} rounds · {dur}` (StreamKind.Ok)
  - NoProgress → `cycle failed — same {f} members failing twice · {r} rounds` (StreamKind.Fail)
  - CapReached → `cycle round cap reached — output may be one generation behind · {r} rounds` (StreamKind.Info)

**Bağlam:** Karar bugün yalnız `decision.log`'a yazılıyor (`RecordCycleOutcome` → `Decide(...)`); kullanıcı koşunun NEDEN öyle bittiğini ekranda göremiyor. decision.log satırı aynen kalır; event ONA EK.

- [ ] **Step 1:** Contracts tipleri + IpcMessagesTests roundtrip testi (kırmızı: tip yokken derlenmez → önce tipleri ekle, sonra SUPERVISOR emit testini kırmızı göster): `CycleCompletedEvent_roundtrips_with_camelCase_outcome` — JSON'da `"type":"cycleCompleted"` ve `"outcome":"noProgress"` metin.
- [ ] **Step 2:** CycleRoundsTests kırmızı: yakınsayan 2 üyeli grup koşusu → event akışında `CycleCompletedEvent(Outcome=Converged, MemberCount=2, Rounds=2, FailedCount=0)` bekle (bugün event yok → KIRMIZI). İkinci test: NoProgress senaryosu → `Outcome=NoProgress, FailedCount=<sabit kırık üye sayısı>`. Mevcut fixture/host'u kullan.
- [ ] **Step 3:** Supervisor: `BuildCycleGroupAsync`'te `int roundsRun = 0;` (döngü gövdesinde artır) ve son turun `failed.Count`'unu sakla; `RecordCycleOutcome`'a geçir; orada `Decide(...)` satırının yanında `run.Events.TryWrite(new CycleCompletedEvent(run.RunId, leader, MapOutcome(decision), members.Count, roundsRun, lastFailedCount, state toplam süresi))`. Süre: `RecordCycleOutcome`'a toplam ms parametresi geçir (üye `DurationMs` toplamı — çağıran zaten `state`'i tutuyor). `MapOutcome` tek switch (Converged/NoProgress/CapReached; Continue buraya zaten gelmez — `RecordCycleOutcome` ilk satırda dönüyor).
- [ ] **Step 4:** App: EventStreamTests kırmızı (üç metin varyantı + kind eşlemesi), sonra `RunViewModel.Stream`'e case ekle (`e.ProjectId` satıra bağlanır — tıklanabilir).
- [ ] **Step 5:** Süit yeşil. Commit — `feat(cycles): grup karari CycleCompletedEvent ile ekrana dusuyor`

---

### Task 4: İlerleme anlatısı — açılış kırılımı, tur satırı, aktif satırda üye/tur

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/StreamText.cs` (CyclesStarted, CycleRound), `src/BuildOrchestrator.App/ViewModels/StreamComposer.cs` (detail parametresi), `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` (kırılım hesabı + grup ilerleme takibi)
- Test: `tests/BuildOrchestrator.Tests/App/EventStreamTests.cs` (+ StreamComposer'ı pinleyen mevcut test dosyası)

**Interfaces:**
- `StreamText.CyclesStarted(int members, int prerequisites)` → `Cycles started — {m} cycle members · {p} prerequisites · up to {cap} rounds` (cap kaynağı yine `CycleRoundPolicy.RoundCap`).
- `StreamText.CycleRound(int round, int cap, int memberCount)` → `cycle round {r}/{cap} — {n} members`.
- `StreamComposer.StartBuilding(string id, string name, long nowMs, string? detail = null)`; `ActiveText` = detail null → `"{name} building…"`, değilse `"{name} building… · {detail}"`. `SetActive` generation kararına detail'i de katar (aynı id + aynı ad + aynı detail → artmaz; detail değişimi daktiloyu yeniden BAŞLATMAZ olması isteniyorsa: detail değişince generation ARTMAZ, yalnız metin tazelenir — karar: generation yalnız id/ad değişiminde artar, detail salt metin günceller; daktilonun her üye geçişinde zaten yeni id ile baştan koşması yeterli).

**Bağlam:** Kullanıcının "birçok proje derleniyor, ne yaptığı belli değil" şikâyeti. Kırılım verisi App'te hazır: `_willBuildIds` (BuildPreview) ve `_cycleGroups` (Workspace partial — `IsMember`). members = will-build ∩ üye; prerequisites = kalan.

- [ ] **Step 1: Kırmızı testler.**
  - Cycles açılış satırı: üyeli+upstream'li preview sür → `"Cycles started — 2 cycle members · 1 prerequisites · up to 3 rounds"`. Eski `CyclesStarted` metnini pinleyen test `[DEĞİŞEN KURAL]` ile yeniden yazılır (eski iddia: tek toplam proje sayısı; gerekçe: toplamın çoğu upstream olabiliyor ve kullanıcı "neden bu kadar proje derleniyor"u göremiyordu).
  - Tur satırı: `CycleRoundStartedEvent(round:2, cap:3, memberCount:15)` → `"cycle round 2/3 — 15 members"`. Eski lider-adlı metni pinleyen test `[DEĞİŞEN KURAL]` ile yeniden yazılır (eski iddia: lider adı + "+N more"; gerekçe: tek ad grubu temsil etmiyordu, maliyeti üye sayısı anlatır; ProjectId event'te duruyor, satır hâlâ lidere tıklatır).
  - Aktif satır: Cycles koşusunda round başlat + 2. üyenin ProjectStarted'ı → `ActiveLineText == "B building… · member 2/15 · round 1/3"`; üye olmayan (upstream) proje → `"U building…"` (detaysız); yeni round → sayaç 1'e döner.
- [ ] **Step 2: Kırmızıyı gör.**
- [ ] **Step 3: Uygula.** RunViewModel.Stream: `CycleRoundStartedEvent` case'inde `(_cycleRound, _cycleRoundCap, _cycleRoundMembers, _cycleMemberIndex) = (e.Round, e.RoundCap, e.MemberCount, 0)`; `ProjectStartedEvent` case'inde üyelik (`_cycleGroups?.IsMember(e.ProjectId) == true`) ve aktif round varsa `_cycleMemberIndex++` + detail compose; `RunStartedEvent`/`RunCompletedEvent`'te sıfırla. Kırılım: BuildPreview'daki `RunMode.Cycles` dalında `int members = _willBuildIds.Count(id => _cycleGroups?.IsMember(id) == true);`.
- [ ] **Step 4: Süit yeşil.** Commit — `feat(stream): cycles kosusu uye/tur ilerlemesini okutuyor`

---

### Task 5: Graf — düğüm her zaman standart, döngü üyeliği kalıcı mini rozet

**Files:**
- Modify: `src/BuildOrchestrator.App/Graph/GraphModels.cs` (GraphNode + InCycle), `src/BuildOrchestrator.App/ViewModels/GraphBinder.cs` (satırdan taşı), `src/BuildOrchestrator.App/Graph/GraphNodeVisual.cs` (CycleBadge üyesi), `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` (ApplyNodeStatus Cycle case + rozet kurulum/güncelleme, boyut sabiti)
- Test: mevcut graf test dosyaları (GraphView/GraphBinder testleri hangi dosyadaysa oraya) + realize testi

**Interfaces:**
- `GraphNode(string Name, int Layer, GraphStatus Status, bool InCycle = false)` — GraphBinder `row.InCycle`'ı geçirir.
- `GraphNodeVisual.CycleBadge` (`Path?` — TALEP ÜZERİNE, yalnız `InCycle` düğümde kurulur; Beads deseni).

**Bağlam:** Bugün `GraphStatus.Cycle` düğümü komple turuncuya boyuyor (çerçeve+zemin+ikon) ve koşudan sonra üyelik graftan kayboluyor. Yeni kural: düğüm HER statüde standart ailesinde çizilir; `GraphStatus.Cycle` görsel olarak discovered ailesine düşer (kesikli `Brush.BorderStrong` çerçeve + `Brush.SurfaceRaised` zemin + `Brush.TextFaint` ikon); üyelik, düğümün sağ-üst köşesinde kalıcı minik turuncu döngü işaretiyle (`Icon.StatusCycle` geometrisi, `Brush.StatusCycleText`) anlatılır. Rozet `Body` içinde durur — koşuda düğüm sönerken onunla birlikte söner (sessiz graf ilkesi). Ekran-okuyucu adı değişmez (`LabelFor(Cycle)` = "Cycle" kalır — liste ile tutarlı).

- [ ] **Step 1: Kırmızı testler.**
  - `A_cycle_member_node_keeps_the_standard_square_and_wears_a_corner_badge`: InCycle + Status=Cycle düğüm → Square stroke resource'u `Brush.BorderStrong` (turuncu DEĞİL), dash'li; CycleBadge Visible. Eski turuncu-aile pinini `[DEĞİŞEN KURAL]` ile yeniden yaz (eski iddia: cycle düğümü turuncu aile — DS STATUS_META; gerekçe: koşu sonrası üyelik graftan kayboluyordu ve turuncu blok, düğümün koşu statüsüyle yarışıyordu; üyelik artık kalıcı köşe rozetinde).
  - `The_badge_survives_every_status`: InCycle düğümü Succeeded/Failed/Building statülerine sür → rozet hep Visible; InCycle=false düğümde hiç kurulmaz (null).
  - Realize testi: rozetli düğüm içeren GraphView `window.Content` realize → geometri `Icons.xaml`'den çözülür (IconGeometryTests deseni).
- [ ] **Step 2: Kırmızıyı gör** (yeni parametre derleme hatası veren testler önce eklenip API ile birlikte kırmızı koşulur — davranış kırmızısı ApplyNodeStatus/rozet üzerindedir).
- [ ] **Step 3: Uygula.** Boyut/konum sabitleri inline yazılmaz — GraphView'daki mevcut ölçü sabitlerinin yanına (`BadgeSizePx` vb., Türkçe gerekçe yorumu). Rozet kurulum: `ApplyNodeStatus` içinde `visual.Model.InCycle` ise ensure + Visible; değilse yok. `UpdateStatuses` yolunda Model değişince InCycle da tazelenir.
- [ ] **Step 4: Süit yeşil** (token/ikon guard'ları dahil). Commit — `feat(graph): dugum standart kaldi, dongu uyeligi kalici kose rozeti`

---

### Task 6: Build sonu Cycles ipucu + buton tooltip sayıları

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/StreamText.cs` (CyclesHint), `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` (RunCompleted dalı), `src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs` (sayılar), `src/BuildOrchestrator.App/Views/ActionBar.xaml.cs` (tooltip)
- Test: `tests/BuildOrchestrator.Tests/App/EventStreamTests.cs`, `tests/BuildOrchestrator.Tests/App/ActionBarTests.cs`

**Interfaces:**
- `StreamText.CyclesHint(int count)` → `{count} cycle projects have pending changes — run Cycles`.
- RunViewModel.Workspace: `_cycleGroups` mevcut; grup sayısı `CycleGroups.Count`; üye toplamı için topology'nin Cycles listesi zaten Workspace'e geliyor — oradan say (CycleGroups'a üye API'si EKLEME, veri elde var).

**Bağlam:** Kullanıcının gerçek akışı: "Build'de hata aldım, baktım döngüdeki projeye bağlı — demek yeniden Cycles demem lazım." Bu çıkarımı ekran yapmalı.

- [ ] **Step 1: Kırmızı testler.**
  - EventStream: Build koşusu, döngü üyesi satır `InCycle=true, WillBuild=true` iken `RunCompletedEvent(Completed)` → Completed satırından SONRA `"2 cycle projects have pending changes — run Cycles"` Info satırı. Cycles koşusu sonunda ve n==0'da satır YOK; `Outcome=Stopped`'da YOK.
  - ActionBar: topolojide 2 döngü/17 üye → `CyclesButton.ToolTip` `"Build dependency cycles — 2 cycles · 17 projects"`; döngü yokken sabit `"Build dependency cycles"`; UIA adı (AutomationProperties.Name) HER İKİ durumda sabit `AccessibilityNames.CyclesButton`.
- [ ] **Step 2: Kırmızıyı gör.**
- [ ] **Step 3: Uygula.** Hint: `RunCompletedEvent` case'inde Completed satırı basıldıktan sonra, `_streamRunMode != RunMode.Cycles && e.Outcome != RunOutcome.Stopped` iken `int n = Projects.Count(p => p.InCycle && p.WillBuild == true); if (n > 0) PushStream(Info, null, CyclesHint(n));`. Tooltip: ActionBar'ın topoloji yenileme yolunda sayılarla compose (metnin tek kaynağı `AccessibilityNames.CyclesButton` + ek; kopya yok).
- [ ] **Step 4: Süit yeşil.** Commit — `feat(app): build sonu cycles ipucu + buton tooltip sayilari`

---

### Task 7: Dokümanlar + tam süit

**Files:**
- Modify: `ARCHITECTURE.md` (§8 stream satırları/skip reason sözlüğü/CycleCompletedEvent; §8.8 ve §14.3 "IsCompiling'i okuyan yüzeyler" — üç yüzey ifadesi beş oldu: glyph, sayaç, chip, nefes, süre; §13-§14 graf rozeti + Cycle düğümünün artık boyanmadığı; §22 kod haritası: SkipReasons, CycleBadge), `README.md` (cycles akış paragrafı: buton → açılış satırı → tur/üye ilerlemesi → karar satırı → Build ipucu)
- Test: yok (doküman); bitişte TAM filtreli süit koşulur.

- [ ] **Step 1:** Değişen her davranışın dokümandaki eski ifadesini bul, YERİNDE yeniden yaz (changelog üslubu YASAK; "eskiden böyleydi" yazılmaz). Rakam gömme (test sayısı vb.) yok.
- [ ] **Step 2:** `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"` → tam yeşil.
- [ ] **Step 3:** Commit — `docs: cycles akisinin ekran anlatisi dokumanlara islendi`
