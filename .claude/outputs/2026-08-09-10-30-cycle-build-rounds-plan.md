# Dairesel bağımlılıkların turlarla derlenmesi — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** SCC (dairesel bağımlılık) üyeleri artık atlanmıyor; `Build` içinde, durdukları yerde, sıralı turlarla
derleniyorlar.

**Architecture:** SCC scheduler'a **tek iş kalemi** olarak girer; onu alan worker turları yürütür ve ara tur
sonuçlarını yaymadan yalnız son sonucu raporlar. Tur kararı Core'da saf bir fonksiyondur
(`CycleRoundPolicy`); grup üyeliği yine Core'da tek bir haritadan (`CycleGroups`) okunur.

**Tech Stack:** .NET 10, C#, WPF (App), xUnit.

**Spec:** [.claude/outputs/2026-08-09-10-10-cycle-build-rounds-design.md](2026-08-09-10-10-cycle-build-rounds-design.md)

## Global Constraints

- **Shell-out değişmez:** her proje kendi `MSBuild.exe` child process'i. Tur döngüsü bunu değiştirmez, yalnız
  aynı invoke'u tekrar eder.
- **OutDir'e dokunulmaz.** Turlar arasında hiçbir çıktı silinmez/taşınmaz; DLL/bin timestamp okunmaz.
- **Planlama Core'da.** `CycleGroups` ve `CycleRoundPolicy` saf; I/O, process, async, log YOK.
- **Kopya YASAK.** Tur döngüsü ile tekil proje derlemesi **aynı** invoke yolunu kullanır (Task 5'teki extract).
- **stdout yalnız NDJSON**; tüm log/tanı stderr'e.
- **Kırmızı test kuralı:** hiçbir implementasyon, onu gerektiren test KIRMIZI görülmeden yazılmaz.
- **Davranış değişince test yeniden yazılır**, silinmez/gevşetilmez; doc'una eski iddia + gerekçe yazılır.
- **Kod/UI/log İngilizce**, kod yorumları Türkçe.
- Uygulama açıkken build alınmaz (çalışan Supervisor kendi binary'lerini kilitler).

**Süit komutu (her adımda):**
```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

## Dosya haritası

| Dosya | Durum | Sorumluluk |
|---|---|---|
| `src/BuildOrchestrator.Core/Scheduling/CycleGroups.cs` | **Create** | SCC üyelik haritası — üye→grup, grup lideri |
| `src/BuildOrchestrator.Core/Planning/CycleRoundPolicy.cs` | **Create** | Tur durma kararı (saf) |
| `src/BuildOrchestrator.Core/Planning/WillBuildEvaluator.cs` | Modify | `inCycle` kısa devresi anahtara bağlanır |
| `src/BuildOrchestrator.Core/Planning/BuildPreview.cs` | Modify | Anahtarı `WillBuildEvaluator`'a taşır |
| `src/BuildOrchestrator.Core/Scheduling/ReadySetScheduler.cs` | Modify | Grup dispatch; pre-skip koşullu |
| `src/BuildOrchestrator.Core/Incremental/EtaCalculator.cs` | Modify | SCC katkısı paralelliğe bölünmez |
| `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` | Modify | `CycleRoundStartedEvent` |
| `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` | Modify | Invoke extract + tur döngüsü |
| `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` | Modify | Üçgen tooltip dallanması |
| `src/BuildOrchestrator.App/Shell/UiState.cs` | Modify | `BuildDependencyCycles` |
| `src/BuildOrchestrator.App/Views/SettingsDialog.xaml(.cs)` | Modify | Kill switch (mevcut `Ds.Chip`) |

---

### Task 1: `CycleGroups` — SCC üyelik haritası

SCC üyeliğini **tek yerden** okunur kılar. `BuildPlan.Cycles` üyeleri ordinal sıralı verir; scheduler ve
coordinator'ın ihtiyacı **build-order** sırasıdır. Bu dönüşüm iki yerde tekrarlanmaz.

**Files:**
- Create: `src/BuildOrchestrator.Core/Scheduling/CycleGroups.cs`
- Test: `tests/BuildOrchestrator.Tests/Scheduling/CycleGroupsTests.cs`

**Interfaces:**
- Consumes: `BuildPlan`, `ProjectNode` (`Contracts.Model`)
- Produces:
  ```csharp
  public sealed class CycleGroups
  {
      public static CycleGroups From(BuildPlan plan);
      public int Count { get; }                                  // grup sayısı
      public bool IsMember(string projectId);
      public IReadOnlyList<string> MembersOf(string projectId);   // build-order sıralı; üye değilse boş
  }
  ```

- [ ] **Step 1: Failing test'i yaz**

`tests/BuildOrchestrator.Tests/Scheduling/CycleGroupsTests.cs`:

```csharp
namespace BuildOrchestrator.Tests.Scheduling;

using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Scheduling;
using Xunit;

public class CycleGroupsTests
{
    private static ProjectNode Node(string id, int order, bool inCycle, params string[] deps) =>
        new(id, id, id, [], deps, order, null, null, inCycle, null);

    // plan.Cycles ordinal sıralı gelir ("a","b"); build-order ise b(0) → a(1).
    // MembersOf BUILD-ORDER vermeli — dispatch sırası buna dayanır.
    [Fact]
    public void members_are_in_build_order_not_ordinal_order()
    {
        var plan = new BuildPlan(
            [Node("b", 0, true, "a"), Node("a", 1, true, "b")],
            [new[] { "a", "b" }],
            "Debug");

        var groups = CycleGroups.From(plan);

        Assert.Equal(1, groups.Count);
        Assert.Equal(["b", "a"], groups.MembersOf("a"));
        Assert.Equal(["b", "a"], groups.MembersOf("b"));
    }

    [Fact]
    public void non_member_reports_empty_and_is_not_member()
    {
        var plan = new BuildPlan(
            [Node("b", 0, true, "a"), Node("a", 1, true, "b"), Node("c", 2, false)],
            [new[] { "a", "b" }],
            "Debug");

        var groups = CycleGroups.From(plan);

        Assert.False(groups.IsMember("c"));
        Assert.Empty(groups.MembersOf("c"));
        Assert.True(groups.IsMember("a"));
    }

    [Fact]
    public void plan_without_cycles_yields_no_groups()
    {
        var plan = new BuildPlan([Node("a", 0, false)], [], "Debug");

        Assert.Equal(0, CycleGroups.From(plan).Count);
    }
}
```

- [ ] **Step 2: Kırmızıyı gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~CycleGroupsTests"
```
Beklenen: derleme hatası — `CycleGroups` tipi yok.

- [ ] **Step 3: Implementasyon**

`src/BuildOrchestrator.Core/Scheduling/CycleGroups.cs`:

```csharp
namespace BuildOrchestrator.Core.Scheduling;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// SCC üyelik haritası — <see cref="BuildPlan.Cycles"/>'ın BUILD-ORDER'a çevrilmiş hâli.
///
/// Neden ayrı bir tip: plan.Cycles üyeleri ORDİNAL sıralı verir (TopoSort determinizmi), ama hem scheduler'ın
/// grup dispatch'i hem coordinator'ın tur döngüsü BUILD-ORDER ister. Bu dönüşüm iki yerde tekrarlanırsa
/// sıralar sessizce ayrışabilir (kopya YASAK) — tek kaynak burasıdır.
///
/// Saf Core state: I/O, process, async, log YOK [D3].
/// </summary>
public sealed class CycleGroups
{
    private readonly Dictionary<string, IReadOnlyList<string>> _byMember;

    private CycleGroups(Dictionary<string, IReadOnlyList<string>> byMember, int count)
    {
        _byMember = byMember;
        Count = count;
    }

    /// <summary>Plandaki SCC sayısı (tek üyeli bileşenler zaten Cycles'a girmez).</summary>
    public int Count { get; }

    public static CycleGroups From(BuildPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var orderOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in plan.Nodes) orderOf[node.Id] = node.BuildOrder;

        var byMember = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var scc in plan.Cycles)
        {
            // Plan'da bulunmayan üye (savunmacı) sona düşer — sıra yine deterministiktir.
            var ordered = scc.OrderBy(id => orderOf.TryGetValue(id, out int o) ? o : int.MaxValue)
                             .ToList();
            if (ordered.Count == 0) continue;
            count++;
            foreach (string id in ordered) byMember[id] = ordered;
        }

        return new CycleGroups(byMember, count);
    }

    public bool IsMember(string projectId) => _byMember.ContainsKey(projectId);

    /// <summary>Bu projenin SCC üyeleri, build-order sıralı. Üye değilse boş liste.</summary>
    public IReadOnlyList<string> MembersOf(string projectId) =>
        _byMember.TryGetValue(projectId, out var members) ? members : [];
}
```

- [ ] **Step 4: Yeşili gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~CycleGroupsTests"
```
Beklenen: 3 test PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BuildOrchestrator.Core/Scheduling/CycleGroups.cs tests/BuildOrchestrator.Tests/Scheduling/CycleGroupsTests.cs
git commit -m "feat(core): SCC uyelik haritasi — build-order sirali grup uyeleri"
```

---

### Task 2: `CycleRoundPolicy` — tur durma kararı

**Files:**
- Create: `src/BuildOrchestrator.Core/Planning/CycleRoundPolicy.cs`
- Test: `tests/BuildOrchestrator.Tests/Planning/CycleRoundPolicyTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public enum CycleRoundDecision { Continue, Converged, NoProgress, CapReached }

  public static class CycleRoundPolicy
  {
      public const int RoundCap = 3;
      public const int BaselineRounds = 2;
      public static CycleRoundDecision Decide(int round, IReadOnlySet<string> failedNow,
                                              IReadOnlySet<string>? failedPrevious);
  }
  ```
  `Decide` bir tur **bittikten sonra** çağrılır; `round` 1-tabanlıdır.

- [ ] **Step 1: Failing test'i yaz**

`tests/BuildOrchestrator.Tests/Planning/CycleRoundPolicyTests.cs`:

```csharp
namespace BuildOrchestrator.Tests.Planning;

using BuildOrchestrator.Core.Planning;
using Xunit;

public class CycleRoundPolicyTests
{
    private static HashSet<string> Set(params string[] ids) => new(ids, StringComparer.OrdinalIgnoreCase);

    // Tur 1 yeşil geçse bile DURULMAZ: A, tur 1'de B'nin ESKİ dll'ine karşı derlenmiş olabilir.
    // Yakınsama ölçütü İKİ ARDIŞIK yeşil turdur (spec §5).
    [Fact]
    public void first_green_round_alone_does_not_converge()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(1, Set(), null));
    }

    [Fact]
    public void two_consecutive_green_rounds_converge()
    {
        Assert.Equal(CycleRoundDecision.Converged, CycleRoundPolicy.Decide(2, Set(), Set()));
    }

    // Aynı KÜME iki turdur patlıyorsa ilerleme yok. (Sayı değil küme — {A,C}→{B,D} salınımdır.)
    [Fact]
    public void identical_failure_set_two_rounds_is_no_progress()
    {
        Assert.Equal(CycleRoundDecision.NoProgress, CycleRoundPolicy.Decide(2, Set("a"), Set("a")));
    }

    [Fact]
    public void same_count_different_members_is_not_no_progress()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(2, Set("a", "c"), Set("b", "d")));
    }

    [Fact]
    public void shrinking_failure_set_continues()
    {
        Assert.Equal(CycleRoundDecision.Continue, CycleRoundPolicy.Decide(2, Set("a"), Set("a", "b")));
    }

    // Tavan: tur 1 patladı, tur 2 düzeldi ama tur 3'e kadar iki ardışık yeşil görülemedi.
    [Fact]
    public void cap_stops_at_round_three()
    {
        Assert.Equal(CycleRoundDecision.CapReached, CycleRoundPolicy.Decide(3, Set(), Set("a")));
    }

    // Converged, cap'ten ÖNCE değerlendirilir: 3. turda iki ardışık yeşil varsa yakınsamıştır.
    [Fact]
    public void converged_wins_over_cap_at_round_three()
    {
        Assert.Equal(CycleRoundDecision.Converged, CycleRoundPolicy.Decide(3, Set(), Set()));
    }
}
```

- [ ] **Step 2: Kırmızıyı gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~CycleRoundPolicyTests"
```
Beklenen: derleme hatası — `CycleRoundPolicy` yok.

- [ ] **Step 3: Implementasyon**

`src/BuildOrchestrator.Core/Planning/CycleRoundPolicy.cs`:

```csharp
namespace BuildOrchestrator.Core.Planning;

/// <summary>Bir SCC turu bittikten sonraki karar.</summary>
public enum CycleRoundDecision
{
    /// <summary>Bir tur daha.</summary>
    Continue,
    /// <summary>İki ardışık yeşil tur — tüm üyeler nihai API'lere karşı derlendi.</summary>
    Converged,
    /// <summary>Aynı küme iki turdur patlıyor — tur eklemek çözmez.</summary>
    NoProgress,
    /// <summary>Tavana dayanıldı; çıktılar bir kuşak geride olabilir.</summary>
    CapReached,
}

/// <summary>
/// SCC tur döngüsünün durma kuralı. SAF: I/O, saat, log YOK [D3].
///
/// Neden tek yeşil tur yetmez: turlar arasında KAYNAK DEĞİŞMEZ, ama tur 1'de A diskteki ESKİ B.dll'e karşı
/// derlenir. Yeşil geçse bile A.dll eski imzaya bağlanmış olabilir (çalışma anında MissingMethodException).
/// Tur 1 her üyenin public API'sini nihaileştirir; tur 2 herkesi nihai API'lere karşı yeniden derler.
/// Bu yüzden yakınsama ölçütü İKİ ARDIŞIK yeşil turdur.
///
/// Neden tavan 3 yeterli: tur 1-2 yeşilse Converged zaten 2'de olur; tur 1-2 aynı kümede patlarsa NoProgress
/// 2'de durur. 3. tur yalnız "tur 1 patladı, sonra düzeldi" dalı için vardır. Turlar diskteki duruma göre
/// idempotent olduğu için düşük tavan bilgi kaybettirmez — sonraki Build kaldığı yerden devam eder.
/// </summary>
public static class CycleRoundPolicy
{
    /// <summary>Bir SCC için tek bir run'da yürütülecek azami tur sayısı.</summary>
    public const int RoundCap = 3;

    /// <summary>Yakınsama için gereken asgari tur sayısı (iki ardışık yeşil).</summary>
    public const int BaselineRounds = 2;

    /// <param name="round">Biten turun 1-tabanlı numarası.</param>
    /// <param name="failedNow">Bu turda derlemesi başarısız olan üyeler.</param>
    /// <param name="failedPrevious">Bir önceki turunki; ilk turda <c>null</c>.</param>
    public static CycleRoundDecision Decide(int round, IReadOnlySet<string> failedNow,
                                            IReadOnlySet<string>? failedPrevious)
    {
        ArgumentNullException.ThrowIfNull(failedNow);

        // Sıra ÖNEMLİ: Converged, NoProgress'ten ve tavandan ÖNCE değerlendirilir.
        if (round >= BaselineRounds && failedPrevious is not null
            && failedNow.Count == 0 && failedPrevious.Count == 0)
            return CycleRoundDecision.Converged;

        if (round >= BaselineRounds && failedPrevious is not null && failedNow.SetEquals(failedPrevious))
            return CycleRoundDecision.NoProgress;

        return round >= RoundCap ? CycleRoundDecision.CapReached : CycleRoundDecision.Continue;
    }
}
```

- [ ] **Step 4: Yeşili gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~CycleRoundPolicyTests"
```
Beklenen: 7 test PASS.

- [ ] **Step 5: Commit**

```bash
git add src/BuildOrchestrator.Core/Planning/CycleRoundPolicy.cs tests/BuildOrchestrator.Tests/Planning/CycleRoundPolicyTests.cs
git commit -m "feat(core): SCC tur durma kurali — iki ardisik yesil, ayni kume, tavan"
```

---

### Task 3: `WillBuildEvaluator` — `inCycle` kısa devresi anahtara bağlanır

**Bu görev mevcut bir kuralı bilerek değiştirir.** `WillBuildTests` içindeki `inCycle → false` iddiasını
pinleyen test SİLİNMEZ; yeni kuralı (anahtar kapalıyken `false`, açıkken normal karar) pinleyecek şekilde
**yeniden yazılır** ve doc'una eski iddia + gerekçe eklenir.

**Files:**
- Modify: `src/BuildOrchestrator.Core/Planning/WillBuildEvaluator.cs`
- Modify: `src/BuildOrchestrator.Core/Planning/BuildPreview.cs:16`
- Modify: `tests/BuildOrchestrator.Tests/Planning/WillBuildTests.cs`

**Interfaces:**
- Produces: `WillBuildEvaluator.Evaluate(bool inCycle, string? currentSignature, BuildState? state, bool buildCycles)`
  — **varsayılan değer YOK**; her çağıran açıkça geçmek zorunda, böylece hiçbir çağrı yeri sessizce atlanamaz.

- [ ] **Step 1: Failing test'i yaz**

`tests/BuildOrchestrator.Tests/Planning/WillBuildTests.cs` içine ekle (mevcut `inCycle` testini bu iki testle
DEĞİŞTİR — eskisini sil, iddiasını aşağıdaki doc yorumunda koru):

```csharp
    // [DEĞİŞEN KURAL] ESKİ İDDİA: "inCycle olan proje ASLA derlenmez → Evaluate her zaman false".
    // Bu kural kaldırıldı: graf kenarlarının primeri HintPath'tir (ProjectReference değil) ve MSBuild bir
    // HintPath döngüsünü reddetmez — döngü sıralı turlarla derlenebilir. Artık cycle üyeleri de normal
    // dirty/clean kararını alır; ESKİ davranış yalnız kill switch KAPALIYKEN geçerlidir.
    [Fact]
    public void cycle_member_is_not_built_when_switch_is_off()
    {
        Assert.False(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: "sig", state: null, buildCycles: false));
    }

    [Fact]
    public void cycle_member_follows_normal_decision_when_switch_is_on()
    {
        // Hiç derlenmemiş (BuiltSignature yok) ⇒ dirty ⇒ true
        Assert.True(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: "sig", state: null, buildCycles: true));

        // İmza eşleşiyor + son sonuç Succeeded ⇒ güncel ⇒ false
        var clean = new BuildState("p", "sig", LastResult: BuildResult.Succeeded);
        Assert.False(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: "sig", state: clean, buildCycles: true));
    }

    // Anahtar AÇIK olsa bile imza yoksa hollow kalır — cycle bunu ezmez.
    [Fact]
    public void cycle_member_stays_hollow_without_signature()
    {
        Assert.Null(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: null, state: null, buildCycles: true));
    }
```

- [ ] **Step 2: Kırmızıyı gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~WillBuildTests"
```
Beklenen: derleme hatası — `Evaluate` 4. parametreyi tanımıyor.

- [ ] **Step 3: Implementasyon**

`WillBuildEvaluator.cs` — imzayı ve ilk satırı değiştir:

```csharp
    /// <param name="buildCycles">Kill switch: cycle üyeleri turlarla derleniyor mu. Kapalıyken cycle üyesi
    /// her zaman "derlenmeyecek" sayılır (eski davranış). VARSAYILAN DEĞER YOK — her çağıran açıkça geçer,
    /// böylece yeni bir çağrı yeri sessizce eski davranışa düşemez.</param>
    public static bool? Evaluate(bool inCycle, string? currentSignature, BuildState? state, bool buildCycles)
    {
        if (inCycle && !buildCycles) return false;                     // anahtar kapalı: cycle projesi derlenmez
        if (currentSignature is null) return null;                     // hollow: imza hesaplanamadı / Sync öncesi
        if (state?.BuiltSignature is null) return true;                // hiç başarıyla derlenmemiş
        if (state.LastResult != BuildResult.Succeeded) return true;    // son koşu başarısız/skip
        return !string.Equals(currentSignature, state.BuiltSignature, StringComparison.Ordinal);
    }
```

`BuildPreview.cs:16` — `buildCycles` parametresini `BuildPreview`'in kendi imzasına ekleyip aktar:

```csharp
            WillBuild = WillBuildEvaluator.Evaluate(n.InCycle, currentSignature(n), stateLookup(n.Id), buildCycles)
```

`BuildPreview`'in public metoduna `bool buildCycles` parametresi eklenir (varsayılansız). Derleyicinin
işaret ettiği tüm çağrı yerlerini güncelle; test/fixture çağrılarında `buildCycles: false` geçerek mevcut
beklentileri koru.

- [ ] **Step 4: Yeşili gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```
Beklenen: tüm süit PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): will-build cycle kisa devresi anahtara baglandi"
```

---

### Task 4: `ReadySetScheduler` — SCC'yi tek iş kalemi olarak dispatch et

`cycleGroups` **null ise bugünkü davranış birebir korunur** (pre-skip). Bu, kill switch'in kapalı hâlini
sıfır ek kodla verir ve mevcut tüm test/çağrı yerlerini değiştirmeden bırakır.

**Files:**
- Modify: `src/BuildOrchestrator.Core/Scheduling/ReadySetScheduler.cs`
- Test: `tests/BuildOrchestrator.Tests/Scheduling/ReadySetSchedulerTests.cs`

**Interfaces:**
- Consumes: `CycleGroups` (Task 1)
- Produces: `ReadySetScheduler(BuildPlan plan, RunSnapshot snapshot, CycleGroups? cycleGroups = null)` ve
  `ReadySetScheduler(BuildPlan plan, CycleGroups? cycleGroups = null)`. `TryDispatch` imzası **değişmez** —
  grup için grubun ilk dispatch edilebilir üyesini verir ve TÜM üyeleri in-flight işaretler.

- [ ] **Step 1: Failing test'i yaz**

`ReadySetSchedulerTests.cs` içine ekle:

```csharp
    private static ProjectNode CycleNode(string id, int order, params string[] deps) =>
        new(id, id, id, [], deps, order, null, null, true, null);

    // Gruplar verilince pre-skip YAPILMAZ — üyeler gerçekten dispatch edilir.
    [Fact]
    public void group_members_are_not_pre_skipped_when_groups_supplied()
    {
        var plan = new BuildPlan(
            [CycleNode("b", 0, "a"), CycleNode("a", 1, "b")], [new[] { "a", "b" }], "Debug");

        var scheduler = new ReadySetScheduler(plan, CycleGroups.From(plan));

        Assert.Empty(scheduler.PreSkipped);
        Assert.True(scheduler.TryDispatch(out string id));
        Assert.Equal("b", id);   // build-order lideri
    }

    // Grup TEK kalem: bir üye dispatch edilince diğerleri de in-flight olur, ikinci worker onları kapamaz.
    [Fact]
    public void dispatching_group_marks_all_members_in_flight()
    {
        var plan = new BuildPlan(
            [CycleNode("b", 0, "a"), CycleNode("a", 1, "b")], [new[] { "a", "b" }], "Debug");

        var scheduler = new ReadySetScheduler(plan, CycleGroups.From(plan));

        Assert.True(scheduler.TryDispatch(out _));
        Assert.Equal(2, scheduler.InFlight);
        Assert.False(scheduler.TryDispatch(out _));   // ikinci worker'a verilecek iş yok
    }

    // Grup, DIŞ bağımlılığı terminal olmadan dispatch EDİLMEZ; grup-içi kenarlar hazırlığı bloklamaz.
    [Fact]
    public void group_waits_for_external_dependency_only()
    {
        var plan = new BuildPlan(
            [
                new ProjectNode("x", "x", "x", [], [], 0, null, null, false, null),
                CycleNode("b", 1, "a", "x"),
                CycleNode("a", 2, "b"),
            ],
            [new[] { "a", "b" }], "Debug");

        var scheduler = new ReadySetScheduler(plan, CycleGroups.From(plan));

        Assert.True(scheduler.TryDispatch(out string first));
        Assert.Equal("x", first);
        Assert.False(scheduler.TryDispatch(out _));      // grup henüz hazır değil
        scheduler.Complete("x", BuildResult.Succeeded);
        Assert.True(scheduler.TryDispatch(out string second));
        Assert.Equal("b", second);
    }

    // Gruplar VERİLMEZSE (kill switch kapalı) bugünkü davranış birebir korunur.
    [Fact]
    public void without_groups_cycle_members_are_still_pre_skipped()
    {
        var plan = new BuildPlan(
            [CycleNode("b", 0, "a"), CycleNode("a", 1, "b")], [new[] { "a", "b" }], "Debug");

        var scheduler = new ReadySetScheduler(plan);

        Assert.Equal(2, scheduler.PreSkipped.Count);
        Assert.All(scheduler.PreSkipped, p => Assert.Equal("in dependency cycle", p.Reason));
    }
```

- [ ] **Step 2: Kırmızıyı gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~ReadySetSchedulerTests"
```
Beklenen: derleme hatası — ctor `CycleGroups` almıyor.

- [ ] **Step 3: Implementasyon**

`ReadySetScheduler.cs`:

1. Alan ekle: `private readonly CycleGroups? _groups;`
2. Ctor'ları güncelle:

```csharp
    public ReadySetScheduler(BuildPlan plan, CycleGroups? cycleGroups = null)
        : this(plan, EmptySnapshot, cycleGroups)
    {
    }

    public ReadySetScheduler(BuildPlan plan, RunSnapshot snapshot, CycleGroups? cycleGroups = null)
    {
        // ... mevcut gövde ...
        _groups = cycleGroups;

        foreach (var node in _nodesInOrder)
        {
            _byId[node.Id] = node;
            // [cycle rounds] Gruplar VERİLDİYSE pre-skip YOK — SCC tek iş kalemi olarak dispatch edilir ve
            // turlarla derlenir. Gruplar null ise (kill switch kapalı) eski davranış birebir korunur:
            // üyeler burada Skipped sayılır, yoksa dairesel bağımlılık nedeniyle asla ready olamaz ve run
            // kilitlenirdi [A6].
            if (_groups is null && node.InCycle && !_completed.ContainsKey(node.Id))
            {
                _completed[node.Id] = BuildResult.Skipped;
                _preSkipped.Add((node.Id, "in dependency cycle"));
            }
        }
    }
```

3. `IsReadyLocked` — grup-içi kenarlar hazırlığı bloklamaz:

```csharp
    // _gate zaten tutulu iken çağrılmalı.
    private bool IsReadyLocked(ProjectNode node)
    {
        var members = _groups?.MembersOf(node.Id) ?? [];
        if (members.Count == 0) return node.Dependencies.All(IsResolvedLocked);

        // Grup TEK iş kalemidir: hazırlık, TÜM üyelerin DIŞ bağımlılıklarına bakar. Grup-içi kenarlar
        // (tanımı gereği dairesel) hariç tutulur — aksi halde grup asla ready olamazdı.
        foreach (string memberId in members)
            if (_byId.TryGetValue(memberId, out var member))
                foreach (string dep in member.Dependencies)
                    if (!members.Contains(dep, StringComparer.OrdinalIgnoreCase) && !IsResolvedLocked(dep))
                        return false;
        return true;
    }
```

4. `TryDispatch` — grup üyelerinin tamamını in-flight işaretle:

```csharp
                foreach (var node in _nodesInOrder)
                {
                    if (_completed.ContainsKey(node.Id) || _inFlight.Contains(node.Id)) continue;
                    if (!IsReadyLocked(node)) continue;

                    var members = _groups?.MembersOf(node.Id) ?? [];
                    if (members.Count == 0)
                    {
                        _inFlight.Add(node.Id);
                        projectId = node.Id;
                        return true;
                    }

                    // Grup: yalnız build-order'da İLK dispatch edilebilir üye verilir; TÜM üyeler in-flight
                    // olur, böylece ikinci bir worker aynı gruba giremez. "Lider tamamlandı ama üye kaldı"
                    // gibi bozuk bir durumda da kilitlenmez — kalan ilk üye devralır.
                    string? head = members.FirstOrDefault(
                        m => !_completed.ContainsKey(m) && !_inFlight.Contains(m));
                    if (head is null) continue;
                    foreach (string m in members)
                        if (!_completed.ContainsKey(m)) _inFlight.Add(m);
                    projectId = head;
                    return true;
                }
```

- [ ] **Step 4: Yeşili gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```
Beklenen: tüm süit PASS (mevcut scheduler testleri değişmeden geçmeli — gruplar null).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): SCC tek is kalemi olarak dispatch edilir"
```

---

### Task 5: `RunCoordinator` — invoke yolunu ayıkla (davranış değişmez)

Saf refactor. Tur döngüsü ile tekil derleme **aynı** invoke yolunu kullanmalı (kopya YASAK); bu görev o ortak
gövdeyi çıkarır ve `BuildProjectAsync`'i onun üstüne kurar. Yeni davranış YOK, yeni test YOK — güvence mevcut
süittir.

**Files:**
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs:970-1071`

**Interfaces:**
- Produces:
  ```csharp
  private sealed record InvokeOutcome(BuildResult Result, long DurationMs, string? FailReason);

  private async Task<InvokeOutcome> InvokeOnceAsync(
      RunContext run, string projectId, DepIssueResult depIssues, ProjectLogFile log, CancellationToken ct);

  private DepIssueResult ComputeDepIssues(RunContext run, string projectId,
                                          IReadOnlyList<string>? excludedDeps = null);
  ```
  `InvokeOnceAsync` YALNIZ invoke eder ve verilen log'a yazar. Event yaymaz, `Complete` çağırmaz, BuildState
  persist ETMEZ, **log'u AÇMAZ ve KAPATMAZ** — bunların hepsi çağıranın işidir.
  `OperationCanceledException` yukarı fırlar.

  **Log ömrü neden çağıranda:** `RunLogWriter.OpenProjectLog` dosyayı `FileMode.Create` ile açar
  (`RunLogWriter.cs:124`) — yani **truncate eder**, ve `Emit`'in yaydığı satır numarası taze dosyanın
  1'den başlayan sayacıdır. Log'u invoke'un içinde açmak tekil projede doğrudur ama tur döngüsünde
  yıkıcıdır: aynı proje N kez invoke edilince 1..N-1 turlarının logu diskten silinir ve satır numaraları
  her turda 1'e döner — kullanıcının canlı izlediği log ile diskteki log birbirini tutmaz. Bu yüzden
  `using` çağıranda durur: tekil projede tek invoke'u, grup turlarında tüm turları kapsar.

  `ComputeDepIssues`, `BuildProjectAsync`'in başındaki mevcut `DepIssueTracker.Compute` çağrısını sarar ve
  `run.DepIssuesById[projectId]`'yi yazar. `excludedDeps` **grup-içi kenarlar** içindir: SCC üyesi dispatch
  edildiğinde kardeş üyeleri henüz `Completed`'ta değildir, dolayısıyla dep-issue hesabına girmemelidir —
  aksi halde her üye kardeşlerini "çözülmemiş" sayardı. Tekil projede `null` geçilir (bugünkü davranış).

- [ ] **Step 1: Mevcut süitin yeşil olduğunu doğrula (taban)**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```
Beklenen: PASS. Bu, refactor'ın tabanıdır.

- [ ] **Step 2: `InvokeOnceAsync`'i çıkar**

`BuildProjectAsync`'in `try` bloğundaki `ProjectStartedEvent`'ten SONRAKİ, sonuç dallanmasından ÖNCEKİ gövdeyi
(request kurulumu + log açma + komut satırları + depIssue warn satırları + `InvokeAsync`) aşağıdaki metoda taşı:

```csharp
    /// <summary>
    /// [cycle rounds] Tek bir MSBuild invoke'u — log açma, komut satırları, depIssue warn satırları dahil.
    /// Event YAYMAZ, Complete ÇAĞIRMAZ, BuildState PERSIST ETMEZ: bunlar çağıranın kararıdır.
    ///
    /// Neden ayrı: SCC tur döngüsü aynı projeyi birden çok kez invoke eder ama sonucu YALNIZ son turda
    /// raporlar. İki yol aynı invoke gövdesini paylaşmazsa komut satırı/log/retry davranışı sessizce
    /// ayrışırdı (kopya YASAK, CLAUDE.md).
    /// </summary>
    private async Task<InvokeOutcome> InvokeOnceAsync(
        RunContext run, string projectId, DepIssueResult depIssues, ProjectLogFile log, CancellationToken ct)
    {
        var request = new MsBuildInvokeRequest(
            ProjectId: projectId,
            Configuration: run.Configuration,
            SolutionDir: SolutionDirResolver.Resolve(projectId, run.SolutionRefs.GetValueOrDefault(projectId, [])),
            NeedsRestore: HasPackagesConfig(projectId),
            BaseIntermediateOutputPath: run.WorktreeObjRoot is not null
                ? WorktreeObjPathResolver.Resolve(run.WorktreeObjRoot, projectId)
                : null);

        // [Kısıt 1] Proje logunu bu metot AÇMAZ ve KAPATMAZ — ömrü çağıranındır (bkz. Interfaces notu:
        // FileMode.Create truncate ettiği için tur döngüsünde log'u burada açmak önceki turları silerdi).
        foreach (string commandLine in CommandLines(request, run.MsBuildExePath))
            Emit(run, projectId, log, commandLine);
        foreach (string warnLine in DepIssueWarnLines(depIssues))
            Emit(run, projectId, log, warnLine);
        var invoke = await run.Invoker.InvokeAsync(request, line => Emit(run, projectId, log, line), ct);

        return invoke.ExitCode == 0 && !invoke.TimedOut && !invoke.Killed
            ? new InvokeOutcome(BuildResult.Succeeded, invoke.DurationMs, null)
            : new InvokeOutcome(BuildResult.Failed, invoke.DurationMs, ReasonFor(invoke));
    }
```

`InvokeOutcome` record'unu `RunCoordinator` içinde, `BuildProjectAsync`'in hemen üstüne koy.

- [ ] **Step 3: `ComputeDepIssues`'ı çıkar**

`BuildProjectAsync`'in başındaki `DepIssueTracker.Compute` bloğunu şu metoda taşı ve çağrısını
`var depIssues = ComputeDepIssues(run, projectId);` ile değiştir:

```csharp
    /// <summary>
    /// [T54] Dispatch anında TÜM bağımlılıklar terminaldir, bu yüzden depIssues invoke'tan ÖNCE güvenle
    /// hesaplanır ve üç tüketiciye birden verilir (log warn satırları, event, bu projenin dependent'larının
    /// miras alacağı birikim).
    ///
    /// [cycle rounds] <paramref name="excludedDeps"/> grup-içi kenarlar içindir: bir SCC dispatch edildiğinde
    /// kardeş üyeler henüz Completed'ta DEĞİLDİR, dolayısıyla dep-issue hesabına girmemelidirler — aksi halde
    /// her üye kardeşlerini "çözülmemiş" sayıp yanlış uyarı üretirdi. Tekil projede null geçilir.
    /// </summary>
    private DepIssueResult ComputeDepIssues(RunContext run, string projectId,
                                            IReadOnlyList<string>? excludedDeps = null)
    {
        run.NodeById.TryGetValue(projectId, out var node);
        var dependencies = node?.Dependencies ?? [];
        if (excludedDeps is { Count: > 0 })
            dependencies = [.. dependencies.Where(
                d => !excludedDeps.Contains(d, StringComparer.OrdinalIgnoreCase))];

        var depIssues = DepIssueTracker.Compute(
            dependencies,
            run.Scheduler.Completed,
            run.DepIssuesById,
            id => run.NodeById.TryGetValue(id, out var n) ? n.Name : id);
        run.DepIssuesById[projectId] = depIssues.All;
        return depIssues;
    }
```

- [ ] **Step 4: `BuildProjectAsync`'i yeni metotların üstüne kur**

`try` bloğunun gövdesi:

```csharp
            run.Events.TryWrite(new ProjectStartedEvent(run.RunId, projectId, name));

            InvokeOutcome outcome;
            // [Kısıt 1] Proje logu YALNIZCA bu projenin invoke'u bittikten sonra dispose edilir (dispose
            // sonrası AppendLine fırlatır — satır sessizce düşmez). Ömür BURADA, invoke'un içinde DEĞİL.
            using (var log = run.Logs.OpenProjectLog(projectId))
                outcome = await InvokeOnceAsync(run, projectId, depIssues, log, ct);

            if (outcome.Result == BuildResult.Succeeded)
            {
                result = BuildResult.Succeeded;
                run.StoppedFailedIds.TryRemove(projectId, out _);
                if (depIssuesForEvent is null)
                    PersistBuildStateOnSuccess(run, projectId, outcome.DurationMs);
                run.Events.TryWrite(new ProjectSucceededEvent(run.RunId, projectId, outcome.DurationMs, depIssuesForEvent));
                Decide(run.Logs, string.Format(CultureInfo.InvariantCulture,
                    "{0}: succeeded ({1}ms)", name, outcome.DurationMs));
            }
            else
            {
                string reason = outcome.FailReason!;
                MarkStoppedFailed(run, projectId, reason);
                run.Events.TryWrite(new ProjectFailedEvent(run.RunId, projectId, outcome.DurationMs, reason, depIssuesForEvent));
                Decide(run.Logs, string.Format(CultureInfo.InvariantCulture,
                    "{0}: failed — {1} ({2}ms)", name, reason, outcome.DurationMs));
            }
```

`catch`/`finally` blokları AYNEN kalır.

- [ ] **Step 5: Süitin hâlâ yeşil olduğunu doğrula**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```
Beklenen: PASS — davranış değişmedi.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(supervisor): tek invoke yolu ayiklandi — tur dongusu icin ortak govde"
```

---

### Task 6: `RunCoordinator` — SCC tur döngüsü

**Files:**
- Modify: `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` — `CycleRoundStartedEvent` (yeni record) +
  `ProjectSucceededEvent`'e `bool CycleUnsettled = false` alanı. **Bu iki sözleşme değişikliği BURADA yapılır**
  (Task 6 onları yayan taraftır); Task 8 yalnız konsol/App tüketicisini ekler.
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (`WorkerAsync`, yeni `BuildCycleGroupAsync`)
- Test: `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs` (Create)
- Test: `tests/BuildOrchestrator.Tests/Contracts/` — iki record'un NDJSON round-trip'i

**Interfaces:**
- Consumes: `CycleGroups` (Task 1), `CycleRoundPolicy` (Task 2), `InvokeOnceAsync` + `ComputeDepIssues` (Task 5)
- Produces:
  ```csharp
  // RunContext'e yeni alan:
  CycleGroups? Groups

  private async Task BuildCycleGroupAsync(RunContext run, IReadOnlyList<string> members, CancellationToken ct);

  // Tekil projeyle AYNI raporlama gövdesi: event + Decide + persist + Complete.
  private void ReportCycleMember(RunContext run, string projectId, BuildResult result, long totalDurationMs,
                                 string? failReason, CycleRoundDecision decision, DepIssueResult depIssues);
  ```
  **`CycleUnsettled` sinyali:** `decision == CycleRoundDecision.CapReached` **ve** üye `Succeeded` ise,
  `ProjectSucceededEvent`'e yeni bir `bool CycleUnsettled` alanı `true` olarak yazılır (Contracts'ta Task 8 ile
  birlikte eklenir). Dep-issue listesine sahte bir isim **enjekte edilmez** — o liste "hangi bağımlılık patladı"
  sorusunun cevabıdır ve ikinci bir anlam yüklenirse `▲ N` sayacı ile filtre chip'i yanlış sayar. Task 9 bu
  bayrağı satır VM'inde `CycleUnsettled` olarak taşır.

**Davranış sözleşmesi:**
- Üyeler her turda **build-order sırasıyla, sıralı** invoke edilir.
- Ara turlarda `ProjectSucceededEvent`/`ProjectFailedEvent` **yayılmaz**; yalnız son turun sonucu yayılır.
- Her üye için `ProjectStartedEvent` **turun başında** yayılır (o an gerçekten derleniyor).
- Raporlanan süre **turların toplamıdır**.
- `Complete`, **dispatch edilmiş** her üye için tam bir kez, `finally` içinde. Scheduler'ın
  `Completed`'ında zaten bulunan bir üye için `Complete` ÇAĞRILMAZ (o üye in-flight değildir ve
  `Complete` fırlatırdı).
- **Yakınsamayan grup hiçbir şey persist etmez.** `PersistBuildStateOnSuccess` YALNIZ
  `decision == CycleRoundDecision.Converged` ise çağrılır. `NoProgress` / `CapReached` / stop / iptal /
  beklenmeyen hata hâlinde **her üye** için `InvalidateBuildStateOnFailure` çağrılır — başarılı görünenler
  dahil.

  Gerekçe: turlar bir bütündür. Tur 1'de yeşil olmuş bir üye, koşu durdurulduğunda taze imzasını
  kaydederse bir sonraki `Build` onu "güncel" sayıp atlar ve grup **yarım kalmış hâlde temiz görünür** —
  §4 gereği DLL/bin timestamp okunmadığı için bunu yakalayacak başka bir mekanizma yoktur. Bu, kod
  tabanında zaten yürürlükte olan kuralın aynısıdır ([A2] `RunCoordinator.cs`: depIssue taşıyan bir
  success de persist edilmez — "arkasında duramadığın başarıyı kaydetme").

  Sonuç: Stop'tan sonra kullanıcı `Build`'e bastığında grup baştan, tüm üyeleriyle derlenir. Bu aynı
  zamanda "yarıda kalmış grupla resume" sorununu tamamen ortadan kaldırır — yarım grup hiçbir zaman
  sonraki koşuya taşınmaz. (`RunMode.Continue` sözleşmede kalır ama App onu göndermez: bkz.
  `RunViewModel.cs` [B4] — "Continue KOMUTU YOK".)

- [ ] **Step 1: Failing test'i yaz**

`tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs` — mevcut `RunCoordinatorTests` fixture/fake
invoker'ını **yeniden kullan** (yeni fixture yazma; ortak host tek yerde, CLAUDE.md). Testler:

```csharp
// 1) iki yeşil tur → her üye TAM BİR KEZ succeeded event'i alır, invoke sayısı üye×2
[Fact] public async Task green_group_emits_one_result_per_member_after_two_rounds()

// 2) ara tur sonucu yayılmaz → tur 1'de patlayıp tur 2-3'te düzelen üye için
//    HİÇ ProjectFailedEvent çıkmaz, yalnız succeeded çıkar
[Fact] public async Task intermediate_round_failure_is_not_reported()

// 3) aynı küme iki turdur patlıyor → NoProgress: 2 turdan sonra invoke DURUR, üye failed raporlanır
[Fact] public async Task no_progress_stops_after_two_rounds_and_reports_failed()

// 4) raporlanan süre turların TOPLAMI
[Fact] public async Task reported_duration_is_the_sum_of_rounds()

// 5) üyeler her turda build-order sırasıyla ve SIRALI invoke edilir (eşzamanlı invoke YOK)
[Fact] public async Task members_are_invoked_sequentially_in_build_order()

// 6) YAKINSAMAYAN GRUP PERSIST ETMEZ: NoProgress ile biten grupta, tur 1'de yesil olmus uye bile
//    BuildState'e taze imza YAZMAZ — invalidate edilir. (Yarim grup bir sonraki Build'de "guncel"
//    gorunmemeli.)
[Fact] public async Task non_converged_group_persists_nothing_even_for_green_members()

// 7) STOP ORTASINDA: koşu iptal edilirse tum uyeler invalidate edilir; sonraki Build grubu bastan derler.
[Fact] public async Task stopped_group_invalidates_every_member()
```

Her testte fake invoker çağrı sırasını/sayısını kaydeder; `Assert` bunlara bakar.

- [ ] **Step 2: Kırmızıyı gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~CycleRoundsTests"
```
Beklenen: FAIL — üyeler tek kez invoke ediliyor (veya hiç), ara sonuçlar yayılıyor.

- [ ] **Step 3: Implementasyon**

`WorkerAsync` içinde dispatch sonrası dallan:

```csharp
            var members = run.Groups?.MembersOf(projectId) ?? [];
            try
            {
                if (members.Count == 0) await BuildProjectAsync(run, projectId, ct);
                else await BuildCycleGroupAsync(run, members, ct);
            }
            finally { run.Wake.WakeAll(); }
```

Yeni metot:

```csharp
    /// <summary>
    /// [cycle rounds] Bir SCC'nin tüm yaşam döngüsü. Üyeler her turda build-order sırasıyla ve SIRALI
    /// invoke edilir — paralellik YOK: A, B.dll'i okurken B aynı dosyayı yazıyor olurdu.
    ///
    /// ARA TUR SONUÇLARI YAYILMAZ. SCC tek bir derleme birimidir (§7.3, tek bileşik imza); yarı bitmiş bir
    /// birimi "bitti" saymak progress'i geri götürür ve ETA'yı yanıltır. Yalnız son turun sonucu raporlanır,
    /// süre ise turların TOPLAMIDIR (gerçek maliyet).
    /// </summary>
    private async Task BuildCycleGroupAsync(RunContext run, IReadOnlyList<string> members, CancellationToken ct)
    {
        var results = new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase);
        var durations = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var depIssuesOf = new Dictionary<string, DepIssueResult>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in members)
        {
            results[id] = BuildResult.Failed;
            durations[id] = 0;
            depIssuesOf[id] = ComputeDepIssues(run, id, excludedDeps: members);   // grup-içi kenarlar hariç
        }

        // Her üyenin logu grubun TÜM turları boyunca AÇIK kalır. OpenProjectLog truncate ettiği için
        // (FileMode.Create) tur başına açmak önceki turların logunu silerdi ve satır numaraları her turda
        // 1'e dönerdi. Bir SCC'nin üye sayısı kadar dosya tanıtıcısı açık kalır — 32 üye için kabul edilir.
        var logs = new Dictionary<string, ProjectLogFile>(StringComparer.OrdinalIgnoreCase);

        var decision = CycleRoundDecision.Continue;
        try
        {
            foreach (string id in members) logs[id] = run.Logs.OpenProjectLog(id);

            HashSet<string>? previousFailed = null;
            for (int round = 1; decision == CycleRoundDecision.Continue; round++)
            {
                run.Events.TryWrite(new CycleRoundStartedEvent(
                    run.RunId, members[0], round, CycleRoundPolicy.RoundCap, members.Count));

                var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string id in members)                       // SIRALI — eşzamanlı invoke YOK
                {
                    string name = run.NodeById.TryGetValue(id, out var n) ? n.Name : id;
                    run.Events.TryWrite(new ProjectStartedEvent(run.RunId, id, name));
                    var outcome = await InvokeOnceAsync(run, id, depIssuesOf[id], logs[id], ct);
                    durations[id] += outcome.DurationMs;             // süre TURLARIN TOPLAMI
                    results[id] = outcome.Result;
                    if (outcome.Result != BuildResult.Succeeded)
                    {
                        failed.Add(id);
                        reasons[id] = outcome.FailReason!;
                    }
                }

                decision = CycleRoundPolicy.Decide(round, failed, previousFailed);
                previousFailed = failed;
            }
        }
        catch (OperationCanceledException)
        {
            foreach (string id in members)
                if (results[id] != BuildResult.Succeeded) reasons[id] = "stopped";
        }
        catch (Exception ex)
        {
            foreach (string id in members)
                if (results[id] != BuildResult.Succeeded) reasons[id] = "invoke error: " + ex.Message;
        }
        finally
        {
            // Loglar sonuç raporlanmadan ÖNCE kapatılır (dispose sonrası AppendLine fırlatır — geç gelen
            // satır sessizce düşmez). Bir dispose fırlarsa diğerleri yine de kapanır ve raporlama çalışır.
            foreach (var log in logs.Values)
                try { log.Dispose(); } catch { /* log kapanışı raporlamayı engellemez */ }

            // Sonuçlar TEK yerde raporlanır — ara turlarda hiçbir şey yayılmadı.
            foreach (string id in members)
                ReportCycleMember(run, id, results[id], durations[id],
                    reasons.GetValueOrDefault(id), decision, depIssuesOf[id]);
        }
    }
```

`ReportCycleMember` tekil projeyle **aynı** raporlama gövdesini kullanır (event + `Decide` + persist +
`Complete`; `Complete` her yoldan tam bir kez, `finally` içinde). `CapReached` + `Succeeded` olan üyeler
`ProjectSucceededEvent.CycleUnsettled = true` taşır — dep-issue listesine sahte isim enjekte EDİLMEZ.

- [ ] **Step 4: Yeşili gör**

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
```

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(supervisor): SCC tur dongusu — ara tur sonuclari yayilmaz"
```

---

### Task 7: Yakınsamama hafızası

Yakınsamayan bir SCC, kaynak (bileşik imza) değişene kadar bir daha tur harcamaz.

**Files:**
- Modify: `src/BuildOrchestrator.Core/State/BuildStateStore.cs`
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (`BuildCycleGroupAsync` sonu + plan aşaması)
- Test: `tests/BuildOrchestrator.Tests/State/CycleConvergenceMemoryTests.cs` (Create)

**Interfaces:**
- `BuildState.LastResult = BuildResult.Failed` + `BuiltSignature = <bileşik imza>` yazılır. Sonraki koşuda
  bu imza eşleşiyor **ve** son sonuç `Failed` ise grup pre-skip edilir, gerekçe
  `"cycle did not converge at this signature"`.

- [ ] **Step 1: Failing test** — aynı imzayla ikinci koşuda hiç invoke edilmediği; imza değişince edildiği.
- [ ] **Step 2: Kırmızıyı gör.**
- [ ] **Step 3: Implementasyon.**
- [ ] **Step 4: Süit yeşil.**
- [ ] **Step 5: Commit** — `feat(core): yakinsamayan SCC ayni imzada tekrar denenmez`

---

### Task 8: Tur göstergesi — konsol satırı ve App tarafı

Sözleşme (`CycleRoundStartedEvent`, `ProjectSucceededEvent.CycleUnsettled`) Task 6'da eklendi; bu görev onları
**tüketir**.

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` (event → konsol satırı + satır VM'i)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (`ProjectRowViewModel.CycleUnsettled`)
- Test: `tests/BuildOrchestrator.Tests/App/` — konsol satırının birebir metni; bayrağın satıra taşındığı

**Interfaces:**
- Produces: `ProjectRowViewModel.CycleUnsettled` (bool, `NotifyPropertyChangedFor` ile satır görselini tazeler)
- Konsol satırı — İngilizce, **tek yerde** kurulur (kopya YASAK):
  `cycle round {Round}/{Cap} — {leaderName} (+{MemberCount-1} more)`

- [ ] **Step 1: Failing test** — konsolda satırın birebir metni; `CycleUnsettled` bayrağının satır VM'ine taşındığı.
- [ ] **Step 2: Kırmızıyı gör.**
- [ ] **Step 3: Implementasyon.**
- [ ] **Step 4: Süit yeşil.**
- [ ] **Step 5: Commit** — `feat(app): tur gostergesi ve oturmamis dongu bayragi`

---

### Task 9: Üçgen tooltip dallanması

`CapReached` ile biten SCC'nin başarılı üyeleri **mevcut dep-issue üçgenini** taşır. Yeni ikon, yeni slot,
yeni sütun YOK — yalnız tooltip metni dallanır.

**Files:**
- Modify: `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs:304-315` (`ApplyDep`)
- Test: `tests/BuildOrchestrator.Tests/App/ProjectRowTests.cs`

**Interfaces:**
- Consumes: Task 6'nın `CycleUnsettledMarker` işareti (satır VM'inde `bool CycleUnsettled`)
- Produces: tooltip metinleri
  - mevcut: `Failed dependency: {names} — last successful output referenced`
  - yeni: `Cycle did not fully settle — output may be one generation stale`

- [ ] **Step 1: Failing test** — `CycleUnsettled` satırda üçgenin görünür olduğu ve tooltip'in BİREBİR yeni metin olduğu.
- [ ] **Step 2: Kırmızıyı gör.**
- [ ] **Step 3: Implementasyon** — `ApplyDep` içinde `has` koşuluna `|| CycleUnsettled`, tooltip metni dallanır.
- [ ] **Step 4: Süit yeşil** (D8/token/anti-slop guard'ları dahil).
- [ ] **Step 5: Commit** — `feat(app): oturmamis dongu icin ucgen tooltip dali`

---

### Task 10: ETA — SCC katkısı paralelliğe bölünmez

**Files:**
- Modify: `src/BuildOrchestrator.Core/Incremental/EtaCalculator.cs`
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:1005` civarı (besleme)
- Test: `tests/BuildOrchestrator.Tests/Incremental/EtaCalculatorTests.cs`

**Interfaces:**
```csharp
public static long? ComputeRawEstimateMs(
    IReadOnlyList<long?> queuedDurationEstimatesMs,
    IReadOnlyList<BuildingProject> building,
    int parallelism,
    IReadOnlyList<long?> cycleQueuedDurationEstimatesMs);   // YENİ, varsayılansız
```
Formül: `raw = (sumQueued + sumBuildingRemaining) / par + sumCycleQueued × CycleRoundPolicy.BaselineRounds + overhead`.
Bilinmeyen süre ortalaması **her iki listeyi birlikte** kapsar.

- [ ] **Step 1: Failing test** — cycle katkısının paralelliğe BÖLÜNMEDİĞİ ve ×2 alındığı.
- [ ] **Step 2: Kırmızıyı gör.**
- [ ] **Step 3: Implementasyon.**
- [ ] **Step 4: Süit yeşil.**
- [ ] **Step 5: Commit** — `fix(core): ETA'da SCC isi sirali ve tur carpanli sayilir`

---

### Task 11: Kill switch — Settings + wiring

**Files:**
- Modify: `src/BuildOrchestrator.App/Shell/UiStateStore.cs` (`UiState`)
- Modify: `src/BuildOrchestrator.App/Views/SettingsDialog.xaml` + `.xaml.cs`
- Modify: `src/BuildOrchestrator.App/ViewModels/SettingsDraftViewModel.cs`
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (scheduler'a `CycleGroups` verilip verilmeyeceği)
- Test: mevcut `tests/BuildOrchestrator.Tests/App/SettingsDialog*Tests.cs` **genişletilir** (yeni fixture YOK)

**Interfaces:**
- `UiState.BuildDependencyCycles` (bool, varsayılan `true`)
- Kapalıyken: `CycleGroups` **null** geçilir → scheduler bugünkü pre-skip'i yapar; `WillBuildEvaluator`'a
  `buildCycles: false` gider. Kapalı hâl için yazılan yeni davranış kodu YOKTUR.
- UI: mevcut `Ds.Chip` `ToggleButton` stili yeniden kullanılır — yeni kontrol/şablon YOK, bu yüzden ayrı bir
  realize testi gerekmez; mevcut SettingsDialog realize testi genişletilir.

- [ ] **Step 1: Failing test** — anahtar kapalıyken cycle üyelerinin pre-skip edildiği (uçtan uca), açıkken derlendiği.
- [ ] **Step 2: Kırmızıyı gör.**
- [ ] **Step 3: Implementasyon.**
- [ ] **Step 4: Süit yeşil.**
- [ ] **Step 5: Commit** — `feat(app): build dependency cycles anahtari (varsayilan acik)`

---

### Task 12: Dokümanlar

Anlatı üslubu korunur; changelog YAZILMAZ, ilgili bölüm yerinde yeniden yazılır. Bayatlayacak rakam gömülmez.

**Files:**
- Modify: `ARCHITECTURE.md` §6.5, §7.4, §8.2, §8.4, §14.3
- Modify: `README.md` (Settings'teki yeni anahtar)

- [ ] **Step 1:** §6.5 — "pre-skipped by the scheduler" ifadesi koşullu hâle getirilir; turlarla derleme anlatılır.
- [ ] **Step 2:** §7.4 — will-build tablosundan `inCycle → false` kısa devresi çıkarılır.
- [ ] **Step 3:** §8.2 — scheduler'ın SCC'yi tek iş kalemi olarak ele alması; §8.4 — ETA'nın SCC katkısı.
- [ ] **Step 4:** §14.3 — `Cycle` satırının metni: "derlenmeyecek" değil "döngüde". README'ye anahtar.
- [ ] **Step 5: Commit** — `docs: dongu turlariyla derleme`

---

## Kapanış

- [ ] Tam süit yeşil: `dotnet test ... --filter "Category!=Acceptance"`
- [ ] Acceptance süiti ayrı koşulur: `--filter "Category=Acceptance"` (gerçek OSYS, ~2 dk + SCC turları)
- [ ] Gerçek OSYS'te ölçüm: kaç SCC, kaç üye, kaç turda yakınsıyor, Faz B ne kadar sürüyor
- [ ] `main`'e merge + push; merge doğrulandıktan sonra branch local ve remote'tan silinir
