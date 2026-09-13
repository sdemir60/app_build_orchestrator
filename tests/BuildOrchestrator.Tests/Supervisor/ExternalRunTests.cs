using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// Harici projelerin koşu içindeki yeri: <b>sıradan düğümlerdir</b>. Aynı scheduler, aynı paralellik, aynı
/// MSBuild argüman sözleşmesi, aynı dependent kuralı — tek işaretleri <see cref="ProjectNode.IsExternal"/>
/// rozetidir ve o rozet yalnız iki şeye karar verir: obj izolasyonu ve defterdeki commit/branch yuvası.
///
/// <para>Hariciler build-order'ın BAŞINDA gelir — ama bunu sağlayan şey ayrılmış <c>External</c> katmanıdır
/// (index −1, §6.6), koordinatörde ayrı bir faz değil. Ana projeler zaten onların çıktısına bağlıdır ve o
/// bağımlılık grafta gerçek bir kenardır; katman TERCİHTİR, kenar GARANTİDİR.</para>
///
/// <para><b>[DEĞİŞEN KURAL]</b> Bir tur boyunca hariciler koşunun başında, worker'lar doğmadan önce, tek tek
/// derlenen ayrı bir fazdı; biri patlarsa koşu tümden iptal olurdu ve o faz kendi sayaçlarını, önizleme
/// öğelerini, argüman listesini ve build-state persist'ini taşıyordu. O tasarım harici projeyi grafın DIŞINDA,
/// tek bir solution hedefi olarak ele alıyordu. Artık grafın içindeler: aynı scheduler, aynı sayaçlar ve
/// başarısızlık için mevcut dependent kuralı. Bu dosya eski fazın pinlerini DEĞİL, yeni kuralı pinler.</para>
/// </summary>
public class ExternalRunTests
{
    private static readonly string ExternalRoot = Path.Combine(Path.GetTempPath(), "bo-ext-run");

    private static string ExternalId(string name) => Path.Combine(ExternalRoot, name, name + ".csproj");

    /// <summary>Harici bir düğüm — <see cref="RunCoordinatorTests.Node"/> ile aynı şekil, yalnız rozeti dolu.</summary>
    private static ProjectNode ExternalNode(string name, string[]? deps = null) =>
        new(ExternalId(name), name, ExternalId(name), SolutionNames: [], Dependencies: [.. deps ?? []],
            BuildOrder: 0, LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null,
            WillBuildReason: null, IsExternal: true);

    private static string NameOf(string projectId) => Path.GetFileNameWithoutExtension(projectId);

    // ---------------------------------------------------------------- sıradan bir düğüm gibi

    [Fact]
    public async Task An_external_project_is_dispatched_like_any_other_node()
    {
        var plan = PlanOf(ExternalNode("Mail"), Node("A"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 2), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A", "Mail"], h.Events.OfType<ProjectStartedEvent>().Select(e => NameOf(e.ProjectId)).Order());
        // Toplam ana + harici ayrımı YAPMAZ: hepsi plan düğümüdür.
        Assert.Equal(2, h.Events.OfType<RunStartedEvent>().Single().TotalProjects);
        Assert.Equal(2, h.Events.OfType<BuildPreviewEvent>().Single().Items.Count);
    }

    [Fact]
    public async Task An_external_project_gets_the_same_argument_contract_as_a_repository_project()
    {
        // Eskiden harici hedefler AYRI bir liste kullanıyordu (koşulsuz restore, BuildProjectReferences
        // serbest). Artık bağımlılıklarını bu araç ayrı düğümler olarak derlediği için sözleşme AYNI.
        var plan = PlanOf(ExternalNode("Mail"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var request = Assert.Single(invoker.Requests);
        Assert.Equal(ExternalId("Mail"), request.ProjectId);
        Assert.False(request.NeedsRestore); // packages.config yok → ana repo yolundaki kararın AYNISI
    }

    [Fact]
    public async Task An_external_project_waits_for_a_repository_dependency_instead_of_leading_the_run()
    {
        // Katman TERCİH, kenar GARANTİDİR: ters yönde gerçek bir bağımlılık varsa harici −1'de olsa bile
        // bekler (scheduler bağımlılığa uyar, katman sırasına değil).
        var plan = PlanOf(ExternalNode("Mail", [Id("A")]), Node("A"));  // harici, ana projenin ÇIKTISINA bağlı
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 4), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A", "Mail"], h.Events.OfType<ProjectStartedEvent>().Select(e => NameOf(e.ProjectId)));
    }

    [Fact]
    public async Task A_failed_external_flows_through_the_normal_dependency_issue_rule()
    {
        // Eski tasarımda bir harici patlayınca KOŞU TÜMDEN iptal olurdu (worker'lar hiç doğmazdı). Artık
        // harici sıradan bir düğüm: ona bağımlı proje derlenmeye devam eder ama T54'ün dependency-uyarısını
        // taşır — ana repo projeleri için ne yapılıyorsa aynısı, ayrı bir kapı yok.
        var plan = PlanOf(ExternalNode("Mail"), DependsOnExternal("A", "Mail"));
        var invoker = new FakeInvoker((req, _, _) =>
            Task.FromResult(req.ProjectId == ExternalId("Mail") ? Exit(1) : Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var succeeded = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
        Assert.Equal(Id("A"), succeeded.ProjectId);
        Assert.Equal(["Mail"], succeeded.DepIssues);
        var completed = Assert.IsType<RunCompletedEvent>(h.Events[^1]);
        Assert.Equal(1, completed.Failed);
        Assert.Equal(1, completed.DepIssueCount);
    }

    // ---------------------------------------------------------------- rozetin karar verdiği iki nokta

    [Fact]
    public async Task A_worktree_run_does_not_redirect_an_external_projects_obj()
    {
        // obj izolasyonu worktree havuzuna aittir; harici çalışma kopyası orada yaşamaz ve yerinde derlenir.
        var plan = PlanOf(ExternalNode("Mail"), Node("A"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker, worktreeObjRootResolver: _ => @"D:\pool\wt-1");

        await h.Sut.StartAsync(
            Start() with { UseWorktree = true }, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Null(invoker.Requests.Single(r => r.ProjectId == ExternalId("Mail")).BaseIntermediateOutputPath);
        Assert.NotNull(invoker.Requests.Single(r => r.ProjectId == Id("A")).BaseIntermediateOutputPath);
    }

    [Fact]
    public async Task A_successful_external_records_its_own_revision_not_the_repositorys()
    {
        // Satırın sha yuvası "en son hangi sürümden derlendi" der. Harici proje BAŞKA bir çalışma
        // kopyasından gelir: kendi revizyonu yazılır, ana reponun HEAD'i ve branch'i YAZILMAZ.
        string cacheRoot = Directory.CreateTempSubdirectory("bo-ext-state-").FullName;
        var store = new BuildStateStore(cacheRoot);
        var plan = PlanOf(ExternalNode("Mail"), Node("A")) with
        {
            Incremental = new IncrementalPlan(
                new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ExternalId("Mail")] = "SIG-Mail",
                    [Id("A")] = "SIG-A",
                },
                HeadCommit: "1111111111111111111111111111111111111111", Branch: "main",
                CommitByProjectId: new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ExternalId("Mail")] = "abcdef1234567890abcdef1234567890abcdef12",
                }),
        };
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker, stateStore: store);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var loaded = store.Load();
        var external = loaded[ExternalId("Mail")];
        Assert.Equal("SIG-Mail", external.BuiltSignature);   // imza YAZILIR — incremental karar ona dayanır
        Assert.Equal("abcdef1234567890abcdef1234567890abcdef12", external.BuiltCommit); // KENDİ revizyonu
        Assert.Null(external.LastBranch);                    // branch her koşulda ana repoya ait
        Assert.Equal("1111111111111111111111111111111111111111", loaded[Id("A")].BuiltCommit); // ana repo etkilenmez
    }

    // ---------------------------------------------------------------- hazırlık kapısı

    [Fact]
    public async Task A_preparation_failure_ends_the_run_before_it_starts()
    {
        // Kir, ayrışma, eksik tf.exe ya da çözülemeyen bir yol planlama sırasında yakalanır: koşu HİÇ
        // başlamaz ve kullanıcı hatayı olduğu gibi görür (Build butonu geri açılır).
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(PlanOf(Node("A")), invoker,
            planner: (_, _) => throw ExternalPreparationException.Dirty("Mail", @"D:\ext\mail"));

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var error = Assert.Single(h.Events.OfType<ErrorEvent>());
        Assert.Equal("planFailed", error.Code);
        Assert.Contains("'Mail'", error.Message);
        Assert.Empty(h.Events.OfType<RunStartedEvent>());
        Assert.Empty(invoker.Requests);
    }

    [Fact]
    public async Task A_run_without_externals_behaves_exactly_as_before()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(PlanOf(Node("A"), Node("B")), invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(2, h.Events.OfType<RunStartedEvent>().Single().TotalProjects);
        Assert.All(invoker.Requests, r => Assert.Null(r.BaseIntermediateOutputPath));
    }

    /// <summary>HARİCİ bir projeye bağımlı ana repo düğümü — <see cref="RunCoordinatorTests.Node"/> bağımlılık
    /// adlarını <c>Id</c>'den geçirdiği için harici kimlikler oradan verilemez.</summary>
    private static ProjectNode DependsOnExternal(string name, string externalName) =>
        new(Id(name), name, Id(name), SolutionNames: [], Dependencies: [ExternalId(externalName)],
            BuildOrder: 0, LayerIndex: null, LayerName: null, InCycle: false, WillBuild: null);
}
