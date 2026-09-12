using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Core.Workspace;
using Xunit;

namespace BuildOrchestrator.Tests.Workspace;

/// <summary>
/// [optimize] <see cref="OptimizeWorkspaceService"/>: workspace doktorunun uçtan uca akışı — needy tespiti →
/// per-proje restore → restore SONRASI kırık referans teşhisi → legacy stale obj artıkları → cache budama →
/// öksüz <c>.tmp</c> süpürme → tek özet.
///
/// <para>Gerçek dosya sistemi, GERÇEK MSBuild YOK: restore yüzeyi sahte bir <see cref="IMsBuildInvoker"/> ile
/// temsil edilir (sahte restore, paket dosyasını gerçekten yaratabilir — "restore düzeltti" iddiası böyle
/// gözlemlenir). Git HİÇ gerekmez: Optimize tek bir git komutu bile koşmaz. Sleep/poll YOK [D8] — heartbeat
/// dikişi senkron sinyallerle sürülür.</para>
/// </summary>
public class OptimizeWorkspaceServiceTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("bo-optimize-").FullName;

    public void Dispose() { try { Directory.Delete(_tmp, recursive: true); } catch (IOException) { } }

    private string Root => Path.Combine(_tmp, "repo");
    private string CacheRoot => Path.Combine(_tmp, "cache");

    // ---------------------------------------------------------------- fixture

    /// <summary>Legacy (packages.config çağı) bir csproj yazar; <paramref name="hintPaths"/> ham HintPath
    /// metinleridir (csproj dizinine göre göreli ya da mutlak).</summary>
    private string WriteLegacyProject(string name, IEnumerable<string>? hintPaths = null, bool packagesConfig = false,
        string tfmVersion = "v4.6")
    {
        string dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        string refs = string.Concat((hintPaths ?? []).Select(h =>
            $"<Reference Include=\"{Path.GetFileNameWithoutExtension(h)}\"><HintPath>{h}</HintPath></Reference>"));
        string csproj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(csproj,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
            + "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">"
            + $"<PropertyGroup><AssemblyName>{name}</AssemblyName>"
            + $"<TargetFrameworkVersion>{tfmVersion}</TargetFrameworkVersion></PropertyGroup>"
            + $"<ItemGroup>{refs}</ItemGroup>"
            + "<ItemGroup><Compile Include=\"Class1.cs\" /></ItemGroup></Project>");
        File.WriteAllText(Path.Combine(dir, "Class1.cs"), "public class Class1 { }");
        if (packagesConfig)
            File.WriteAllText(Path.Combine(dir, "packages.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages><package id=\"Some.Package\" version=\"1.0.0\" /></packages>");
        return csproj;
    }

    private string WriteSdkProject(string name, IEnumerable<string>? hintPaths = null)
    {
        string dir = Path.Combine(Root, name);
        Directory.CreateDirectory(dir);
        string refs = string.Concat((hintPaths ?? []).Select(h =>
            $"<Reference Include=\"{Path.GetFileNameWithoutExtension(h)}\"><HintPath>{h}</HintPath></Reference>"));
        string csproj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(csproj,
            "<Project Sdk=\"Microsoft.NET.Sdk\">"
            + $"<PropertyGroup><AssemblyName>{name}</AssemblyName><TargetFramework>net10.0</TargetFramework></PropertyGroup>"
            + $"<ItemGroup>{refs}</ItemGroup></Project>");
        return csproj;
    }

    /// <summary>Kök altına bir paket DLL'i yazar ve tam yolunu döner (HintPath hedefi olarak kullanılır).</summary>
    private string WritePackageDll(string relative)
    {
        string full = Path.GetFullPath(Path.Combine(Root, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "MZ-not-a-real-dll");
        return full;
    }

    /// <summary>Bir projenin <c>obj</c> klasörüne YABANCI TFM çözmüş bir <c>project.assets.json</c> + NuGet
    /// üretimli props/targets yazar — <see cref="StaleObjDetector"/> bunu "stale" der.</summary>
    private static void WriteStaleObj(string csproj, string name)
    {
        string objDir = Path.Combine(Path.GetDirectoryName(csproj)!, "obj");
        Directory.CreateDirectory(objDir);
        File.WriteAllText(Path.Combine(objDir, "project.assets.json"),
            "{\"targets\":{\".NETStandard,Version=v2.0\":{}}}");
        File.WriteAllText(Path.Combine(objDir, name + ".csproj.nuget.g.props"), "<Project/>");
        File.WriteAllText(Path.Combine(objDir, name + ".csproj.nuget.g.targets"), "<Project/>");
        File.WriteAllText(Path.Combine(objDir, "keep-me.txt"), "obj klasörünün kendisine dokunulmaz");
    }

    // ---------------------------------------------------------------- sahte restore yüzeyi

    /// <summary>
    /// Restore yalnız bu yüzeyden koşar; build yolu (<see cref="IMsBuildInvoker.InvokeAsync"/>) çağrılırsa test
    /// KIRILIR — Optimize hiçbir projeyi DERLEMEZ.
    /// </summary>
    private sealed class FakeRestoreInvoker(Func<MsBuildRestoreRequest, Action<string>, CancellationToken, Task<MsBuildInvokeResult>> handler)
        : IMsBuildInvoker
    {
        private readonly List<MsBuildRestoreRequest> _requests = [];
        public IReadOnlyList<MsBuildRestoreRequest> Requests { get { lock (_requests) return [.. _requests]; } }

        public Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct)
            => throw new NotSupportedException("Optimize must never build a project");

        public Task<MsBuildInvokeResult> RestoreAsync(MsBuildRestoreRequest req, Action<string> onLine, CancellationToken ct)
        {
            lock (_requests) _requests.Add(req);
            return handler(req, onLine, ct);
        }
    }

    private static MsBuildInvokeResult Exit(int code) => new(code, DurationMs: 1, TimedOut: false, Killed: false);

    private OptimizeWorkspaceService ServiceWith(
        Func<MsBuildRestoreRequest, Action<string>, CancellationToken, Task<MsBuildInvokeResult>>? restore = null,
        Func<CancellationToken, Task<IMsBuildInvoker>>? factory = null,
        FakeRestoreInvoker? invoker = null)
    {
        var fake = invoker ?? new FakeRestoreInvoker(restore ?? ((_, _, _) => Task.FromResult(Exit(0))));
        return new OptimizeWorkspaceService(
            new WorkspaceScanner(), new CsprojEvaluator(),
            new EvaluationCache(Path.Combine(CacheRoot, "evaluation-cache.json")),
            new BuildStateStore(CacheRoot),
            new SourceHashCache(Path.Combine(CacheRoot, SourceHashCache.FileName)),
            factory ?? (_ => Task.FromResult<IMsBuildInvoker>(fake)));
    }

    private async Task<List<IpcEvent>> RunAsync(OptimizeWorkspaceService svc, string? root = null,
        IReadOnlyList<ExternalProject>? externals = null)
    {
        var events = new List<IpcEvent>();
        await svc.RunAsync(new OptimizeWorkspaceCommand(root ?? Root, externals), events.Add, CancellationToken.None);
        return events;
    }

    private static OptimizeCompletedEvent Completed(List<IpcEvent> events) =>
        Assert.Single(events.OfType<OptimizeCompletedEvent>());

    private static IReadOnlyList<string> Lines(List<IpcEvent> events) =>
        events.OfType<OptimizeProgressEvent>().Select(e => e.Line).ToList();

    private static IReadOnlyList<string> WarnLines(List<IpcEvent> events) =>
        events.OfType<OptimizeProgressEvent>().Where(e => e.Level == "warn").Select(e => e.Line).ToList();

    // ---------------------------------------------------------------- 1) restore

    [Fact]
    public async Task A_project_with_packages_config_and_a_missing_packages_hintpath_is_restored()
    {
        string csproj = WriteLegacyProject("Needy",
            hintPaths: ["..\\packages\\Some.Package.1.0.0\\lib\\net46\\Some.Package.dll"], packagesConfig: true);
        File.WriteAllText(Path.Combine(Root, "Osys.sln"),
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Needy\", \"Needy\\Needy.csproj\", \"{1}\"\nEndProject\n");

        FakeRestoreInvoker? captured = null;
        // Sahte restore GERÇEKTEN paketi yerine koyar — "restore düzeltti" iddiası re-check ile gözlemlenir.
        captured = new FakeRestoreInvoker((_, _, _) =>
        {
            WritePackageDll("packages\\Some.Package.1.0.0\\lib\\net46\\Some.Package.dll");
            return Task.FromResult(Exit(0));
        });

        var events = await RunAsync(ServiceWith(invoker: captured));

        var req = Assert.Single(captured.Requests);
        Assert.Equal(Path.GetFullPath(csproj), Path.GetFullPath(req.ProjectId));
        Assert.Equal(Path.GetFullPath(Root), Path.GetFullPath(req.SolutionDir)); // sln bağlamı çözüldü

        var done = Completed(events);
        Assert.Equal(1, done.RestoredProjects);
        Assert.Equal(0, done.FailedRestores);
        Assert.Equal(0, done.UnresolvedReferences); // restore sonrası re-check temiz

        // Sıra: started → progress* → completed
        Assert.IsType<OptimizeStartedEvent>(events[0]);
        Assert.IsType<OptimizeCompletedEvent>(events[^1]);
    }

    [Fact]
    public async Task A_healthy_workspace_reports_nothing_to_fix_and_spawns_no_restore()
    {
        string dll = WritePackageDll("packages\\Ok.1.0.0\\lib\\net46\\Ok.dll");
        WriteLegacyProject("Healthy", hintPaths: ["..\\packages\\Ok.1.0.0\\lib\\net46\\Ok.dll"], packagesConfig: true);
        Assert.True(File.Exists(dll));

        var invoker = new FakeRestoreInvoker((_, _, _) => throw new InvalidOperationException("restore must not run"));
        var events = await RunAsync(ServiceWith(invoker: invoker));

        Assert.Empty(invoker.Requests);
        var done = Completed(events);
        Assert.Equal(1, done.ProjectCount);
        Assert.Equal(0, done.RestoredProjects);
        Assert.Equal(0, done.UnresolvedReferences);
        Assert.Equal(0, done.StaleObjCleaned);
        Assert.Contains(Lines(events), l => l.Contains("nothing to fix", StringComparison.Ordinal));
    }

    [Fact] // needy tanımı: packages.config YOKSA proje restore edilmez — eksikler yalnız teşhise gider
    public async Task A_project_without_packages_config_is_never_restored_even_with_missing_hintpaths()
    {
        WriteLegacyProject("NoConfig",
            hintPaths: ["..\\packages\\Gone.1.0.0\\lib\\net46\\Gone.dll"], packagesConfig: false);

        var invoker = new FakeRestoreInvoker((_, _, _) => throw new InvalidOperationException("restore must not run"));
        var events = await RunAsync(ServiceWith(invoker: invoker));

        Assert.Empty(invoker.Requests);
        var done = Completed(events);
        Assert.Equal(0, done.RestoredProjects);
        Assert.Equal(1, done.UnresolvedReferences);
    }

    [Fact]
    public async Task A_failed_restore_warns_and_continues_to_the_next_project()
    {
        WriteLegacyProject("A", hintPaths: ["..\\packages\\P.1.0.0\\lib\\A.dll"], packagesConfig: true);
        WriteLegacyProject("B", hintPaths: ["..\\packages\\P.1.0.0\\lib\\B.dll"], packagesConfig: true);

        var invoker = new FakeRestoreInvoker((_, onLine, _) =>
        {
            onLine("error: unable to reach the package source");
            return Task.FromResult(Exit(1));
        });
        var events = await RunAsync(ServiceWith(invoker: invoker));

        Assert.Equal(2, invoker.Requests.Count); // ilk proje düşse de ikincisi denenir
        var done = Completed(events);
        Assert.Equal(2, done.FailedRestores);
        Assert.Equal(0, done.RestoredProjects);
        Assert.Contains(WarnLines(events), l => l.Contains("restore failed", StringComparison.Ordinal));
        Assert.Equal(2, done.UnresolvedReferences); // restore düşünce paketler hâlâ yok
    }

    [Fact] // sürüm drift'i: restore koşar, exit 0 verir, ama HintPath'in gösterdiği sürüm gelmez
    public async Task A_hintpath_still_missing_after_restore_is_reported_as_unresolved()
    {
        WriteLegacyProject("Drift",
            hintPaths: ["..\\packages\\Json.12.0.3\\lib\\net46\\Json.dll"], packagesConfig: true);

        var events = await RunAsync(ServiceWith(restore: (_, _, _) => Task.FromResult(Exit(0))));

        var done = Completed(events);
        Assert.Equal(1, done.RestoredProjects);
        Assert.Equal(1, done.UnresolvedReferences);
        Assert.Contains(WarnLines(events), l =>
            l.Contains("unresolved reference", StringComparison.Ordinal) &&
            l.Contains("Json.dll", StringComparison.Ordinal) &&
            l.Contains("Drift", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Unresolved_detail_lines_are_capped_and_the_counter_keeps_the_true_total()
    {
        var hints = Enumerable.Range(0, 42).Select(i => $"..\\packages\\Pkg{i}\\lib\\Pkg{i}.dll").ToArray();
        WriteLegacyProject("Many", hintPaths: hints, packagesConfig: false);

        var events = await RunAsync(ServiceWith());

        var done = Completed(events);
        Assert.Equal(42, done.UnresolvedReferences);
        Assert.Equal(30, WarnLines(events).Count(l => l.StartsWith("unresolved reference:", StringComparison.Ordinal)));
        Assert.Contains(Lines(events), l => l.Contains("and 12 more unresolved references", StringComparison.Ordinal));
    }

    [Fact] // K-1: MSBuild property'li ham HintPath çözülemez — ne needy tetikler ne unresolved sayılır
    public async Task A_hintpath_with_an_msbuild_property_is_ignored_everywhere()
    {
        WriteLegacyProject("Propertied",
            hintPaths: ["$(SolutionDir)packages\\Mystery.1.0.0\\lib\\Mystery.dll"], packagesConfig: true);

        var invoker = new FakeRestoreInvoker((_, _, _) => throw new InvalidOperationException("restore must not run"));
        var events = await RunAsync(ServiceWith(invoker: invoker));

        Assert.Empty(invoker.Requests);
        var done = Completed(events);
        Assert.Equal(0, done.UnresolvedReferences);
    }

    // ---------------------------------------------------------------- 2) stale obj (K-7)

    [Fact]
    public async Task Stale_obj_leftovers_are_removed_only_from_legacy_projects()
    {
        string legacy = WriteLegacyProject("Legacy");
        WriteStaleObj(legacy, "Legacy");
        string sdk = WriteSdkProject("Modern");
        WriteStaleObj(sdk, "Modern");

        var events = await RunAsync(ServiceWith());

        string legacyObj = Path.Combine(Path.GetDirectoryName(legacy)!, "obj");
        Assert.False(File.Exists(Path.Combine(legacyObj, "project.assets.json")));
        Assert.False(File.Exists(Path.Combine(legacyObj, "Legacy.csproj.nuget.g.props")));
        Assert.False(File.Exists(Path.Combine(legacyObj, "Legacy.csproj.nuget.g.targets")));
        Assert.True(Directory.Exists(legacyObj));                                   // obj klasörünün KENDİSİ durur
        Assert.True(File.Exists(Path.Combine(legacyObj, "keep-me.txt")));           // diğer içerik durur

        // K-7 bloklayıcı kural: SDK-style projede restore'suz silmek build'i kırar — HİÇBİR dosya silinmez.
        string sdkObj = Path.Combine(Path.GetDirectoryName(sdk)!, "obj");
        Assert.True(File.Exists(Path.Combine(sdkObj, "project.assets.json")));
        Assert.True(File.Exists(Path.Combine(sdkObj, "Modern.csproj.nuget.g.props")));

        var done = Completed(events);
        Assert.Equal(1, done.StaleObjCleaned);
        Assert.True(done.BytesReclaimed > 0);
    }

    /// <summary>
    /// Çalışan bir VS/OSYS artığı elinde tutuyor olabilir. Kilit HATA DEĞİLDİR: sayılır, isimle raporlanır ve
    /// akış diğer artıklarla + sonraki adımlarla devam eder.
    /// <para><b>Kilit neden props üzerinde:</b> teşhisin okuduğu dosya <c>project.assets.json</c>'dır ve o
    /// paylaşımsız açıkken <see cref="StaleObjDetector"/> (never-throw, belirsizde "temiz") projeyi stale bile
    /// SAYMAZ — silme adımına hiç girilmez. Silinecek artıklardan birini kilitlemek, sınanmak istenen yolu
    /// (tespit edildi, silinemedi) gerçekten kurar.</para>
    /// </summary>
    [Fact]
    public async Task A_locked_leftover_is_reported_and_skipped_without_stopping_the_optimize()
    {
        string legacy = WriteLegacyProject("Locked");
        WriteStaleObj(legacy, "Locked");
        string objDir = Path.Combine(Path.GetDirectoryName(legacy)!, "obj");
        string lockedProps = Path.Combine(objDir, "Locked.csproj.nuget.g.props");

        using (new FileStream(lockedProps, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var events = await RunAsync(ServiceWith());

            var done = Completed(events);
            Assert.True(done.LockedFileCount >= 1);
            Assert.Contains(WarnLines(events), l => l.Contains("could not be removed", StringComparison.Ordinal));
            Assert.Contains(WarnLines(events), l =>
                l.Contains("close the running application", StringComparison.Ordinal)); // özet uyarısı da düşer
            Assert.False(File.Exists(Path.Combine(objDir, "project.assets.json")));      // kilitsiz artık yine de gitti
        }
        Assert.True(File.Exists(lockedProps)); // kilitli dosya duruyor
    }

    // ---------------------------------------------------------------- 3) cache hijyeni

    [Fact]
    public async Task Dead_cache_entries_under_the_root_are_pruned_and_foreign_roots_survive()
    {
        WriteLegacyProject("Alive");
        string dead = Path.Combine(Root, "Gone", "Gone.csproj");
        string foreign = Path.Combine(_tmp, "other", "X", "X.csproj");

        var store = new BuildStateStore(CacheRoot);
        store.Upsert(new BuildState(dead, "sig-dead"));
        store.Upsert(new BuildState(foreign, "sig-foreign"));

        var events = await RunAsync(ServiceWith());

        var map = new BuildStateStore(CacheRoot).Load();
        Assert.False(map.ContainsKey(dead));
        Assert.True(map.ContainsKey(foreign));
        Assert.Equal(1, Completed(events).PrunedStateEntries);
    }

    [Fact]
    public async Task Orphan_temp_files_older_than_the_threshold_are_swept()
    {
        WriteLegacyProject("Alive");
        Directory.CreateDirectory(CacheRoot);
        string stale = Path.Combine(CacheRoot, "build-state.json." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(stale, "yarım kalmış atomik yazım");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-3));
        string fresh = Path.Combine(CacheRoot, "build-state.json." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllText(fresh, "aktif bir yazımın tmp'si olabilir");

        var events = await RunAsync(ServiceWith());

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
        Assert.Equal(1, Completed(events).RemovedTempFiles);
    }

    // ---------------------------------------------------------------- 4) dayanıklılık

    [Fact] // K-6: MSBuild çözülemezse Optimize DÜŞMEZ — restore adımı warn ile atlanır, kalanlar koşar
    public async Task MSBuild_resolve_failure_skips_restore_but_the_other_steps_still_run()
    {
        string legacy = WriteLegacyProject("Needy",
            hintPaths: ["..\\packages\\P.1.0.0\\lib\\P.dll"], packagesConfig: true);
        WriteStaleObj(legacy, "Needy");

        var svc = ServiceWith(factory: _ => throw new MsBuildResolveException("no Visual Studio installation found"));
        var events = await RunAsync(svc);

        Assert.Empty(events.OfType<ErrorEvent>()); // hata DEĞİL
        Assert.Contains(WarnLines(events), l => l.Contains("package restore skipped", StringComparison.Ordinal));
        var done = Completed(events);
        Assert.Equal(0, done.RestoredProjects);
        Assert.Equal(1, done.StaleObjCleaned); // sonraki adımlar koştu
    }

    [Fact] // K-13: uzun ve SESSİZ bir restore watchdog'u yanlış alarma düşürmesin diye kalp atışı basılır
    public async Task The_restore_heartbeat_line_appears_while_a_child_is_slow()
    {
        WriteLegacyProject("Slow", hintPaths: ["..\\packages\\P.1.0.0\\lib\\P.dll"], packagesConfig: true);

        var restoreGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invoker = new FakeRestoreInvoker(async (_, _, _) => { await restoreGate.Task; return Exit(0); });
        var svc = ServiceWith(invoker: invoker);

        // Dikiş: ilk "bekleme" ANINDA döner (bir kalp atışı basılır), ikincisinde restore serbest bırakılır ve
        // bekleme bir daha ASLA tamamlanmaz — gerçek zaman beklenmez [D8].
        int beats = 0;
        svc.HeartbeatDelay = _ =>
        {
            if (++beats == 1) return Task.CompletedTask;
            restoreGate.TrySetResult();
            return new TaskCompletionSource().Task;
        };

        var events = await RunAsync(svc);

        Assert.Contains(Lines(events), l => l.StartsWith("still restoring Slow", StringComparison.Ordinal));
        Assert.Equal(1, Completed(events).RestoredProjects);
    }

    [Fact]
    public async Task A_missing_workspace_root_becomes_a_defined_error_event()
    {
        var events = await RunAsync(ServiceWith(), root: Path.Combine(_tmp, "no-such-root"));

        var err = Assert.Single(events.OfType<ErrorEvent>());
        Assert.Equal("optimizeFailed", err.Code);
        Assert.Contains("no-such-root", err.Message, StringComparison.Ordinal);
        Assert.Empty(events.OfType<OptimizeCompletedEvent>()); // yarıda kaldı, sahte bir özet uydurulmaz
    }

    [Fact]
    public async Task A_workspace_with_no_projects_completes_with_zero_counters()
    {
        Directory.CreateDirectory(Root);

        var events = await RunAsync(ServiceWith());

        var done = Completed(events);
        Assert.Equal(0, done.ProjectCount);
        Assert.Equal(0, done.RestoredProjects);
        Assert.Equal(0, done.UnresolvedReferences);
        Assert.Equal(0L, done.BytesReclaimed);
    }

    [Fact] // Optimize hiçbir kullanıcı metnini Türkçe yazmaz (NoTurkishUserTextTests'in servis-düzeyi ikizi)
    public async Task Every_progress_line_and_summary_is_english()
    {
        WriteLegacyProject("Needy", hintPaths: ["..\\packages\\P.1.0.0\\lib\\P.dll"], packagesConfig: true);
        var events = await RunAsync(ServiceWith(restore: (_, _, _) => Task.FromResult(Exit(1))));

        const string turkish = "çğıöşüÇĞİÖŞÜ";
        foreach (string line in Lines(events))
            Assert.False(line.Any(turkish.Contains), $"Türkçe karakter içeren kullanıcı metni: {line}");
    }

    // ---------------------------------------------------------------- harici kökler

    /// <summary>Kök DIŞINDA, kendi klasöründe yaşayan bir harici proje yazar ve kartını döner.</summary>
    private ExternalProject WriteExternalProject(string name, IEnumerable<string>? hintPaths = null,
        bool packagesConfig = false)
    {
        string dir = Path.Combine(_tmp, "externals", name);
        Directory.CreateDirectory(dir);
        string refs = string.Concat((hintPaths ?? []).Select(h =>
            $"<Reference Include=\"{Path.GetFileNameWithoutExtension(h)}\"><HintPath>{h}</HintPath></Reference>"));
        string csproj = Path.Combine(dir, name + ".csproj");
        File.WriteAllText(csproj,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
            + "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">"
            + $"<PropertyGroup><AssemblyName>{name}</AssemblyName>"
            + "<TargetFrameworkVersion>v4.6</TargetFrameworkVersion></PropertyGroup>"
            + $"<ItemGroup>{refs}</ItemGroup></Project>");
        if (packagesConfig)
            File.WriteAllText(Path.Combine(dir, "packages.config"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?><packages><package id=\"P\" version=\"1.0.0\" /></packages>");
        return new ExternalProject(csproj, VcsKind.Git);
    }

    /// <summary>
    /// [harici projeler] Harici projeler SIRADAN projelerdir: aynı grafa girer, aynı kararı alır — dolayısıyla
    /// Optimize onları da onarır. Ana kökün tarama önekinin DIŞINDA yaşadıkları için taramaya ancak kartla
    /// girerler; kart verilmezse Optimize onları hiç görmezdi.
    /// </summary>
    [Fact]
    public async Task External_projects_are_scanned_and_restored_like_any_other_project()
    {
        WriteLegacyProject("Main");
        var external = WriteExternalProject("Shared",
            hintPaths: ["..\\packages\\P.1.0.0\\lib\\P.dll"], packagesConfig: true);

        var invoker = new FakeRestoreInvoker((_, _, _) => Task.FromResult(Exit(0)));
        var events = await RunAsync(ServiceWith(invoker: invoker), externals: [external]);

        var done = Completed(events);
        Assert.Equal(2, done.ProjectCount);                       // ana kök + harici kök
        Assert.Equal(1, done.RestoredProjects);
        Assert.Equal(external.Path, Assert.Single(invoker.Requests).ProjectId);
    }

    /// <summary>Çözülemeyen bir kart Optimize'ı DÜŞÜRMEZ: isimli bir uyarı yazılır ve ana kök yine onarılır.</summary>
    [Fact]
    public async Task An_external_card_that_resolves_to_nothing_warns_and_the_main_root_is_still_repaired()
    {
        WriteLegacyProject("Main");
        var ghost = new ExternalProject(Path.Combine(_tmp, "externals", "Ghost", "Ghost.csproj"), VcsKind.Git);

        var events = await RunAsync(ServiceWith(), externals: [ghost]);

        Assert.Contains(WarnLines(events), l => l.Contains("Ghost") && l.Contains("nothing from it will be repaired"));
        Assert.Equal(1, Completed(events).ProjectCount); // ana kök onarıldı
    }

    // ---------------------------------------------------------------- üçüncü defter

    /// <summary>
    /// [optimize] <c>source-hash-cache.json</c> da hijyene girer. Bu defter csproj ile değil KAYNAK DOSYA ile
    /// anahtarlanır, yani silinen her <c>.cs</c> orada bir ölü girdi bırakır — dışarıda kalsaydı en hızlı
    /// biriken defter hiç budanmazdı. Ayrı sayılmasının sebebi de budur: sayısı ötekilerle aynı büyüklükte değildir.
    /// </summary>
    [Fact]
    public async Task Dead_source_hash_entries_under_the_root_are_pruned_and_foreign_roots_survive()
    {
        WriteLegacyProject("Alive");
        string deadSource = Path.Combine(Root, "Gone", "Gone.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(deadSource)!);
        File.WriteAllText(deadSource, "public class Gone { }");
        File.SetLastWriteTimeUtc(deadSource, DateTime.UtcNow.AddMinutes(-5));

        string foreignSource = Path.Combine(_tmp, "other-repo", "X.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(foreignSource)!);
        File.WriteAllText(foreignSource, "public class X { }");
        File.SetLastWriteTimeUtc(foreignSource, DateTime.UtcNow.AddMinutes(-5));

        string cachePath = Path.Combine(CacheRoot, SourceHashCache.FileName);
        var seed = new SourceHashCache(cachePath);
        Assert.NotNull(seed.HashOf(deadSource));
        Assert.NotNull(seed.HashOf(foreignSource));
        seed.Flush();

        File.Delete(deadSource);
        File.Delete(foreignSource); // kök DIŞI ölü girdi — budanmamalı

        var done = Completed(await RunAsync(ServiceWith()));

        Assert.Equal(1, done.PrunedSourceHashEntries);
        Assert.Contains("other-repo", File.ReadAllText(cachePath));
    }

    // ---------------------------------------------------------------- platform DLL teşhisi

    /// <summary>
    /// Üreticisi OLMAYAN bir <c>\bin\</c> hedefi eksikse bu repo-dışı bir OSYS platform DLL'idir: restore onu
    /// getiremez, kullanıcının üreten solution'ı derlemesi gerekir — satır bunu SÖYLER. <c>packages</c>
    /// hedefinden ayrı bir cümledir çünkü eylem farklıdır.
    /// </summary>
    [Fact]
    public async Task A_missing_platform_dll_without_a_producer_says_to_build_the_producing_solution()
    {
        WriteLegacyProject("Consumer", hintPaths: ["..\\..\\platform\\bin\\Osys.Platform.dll"]);

        var warns = WarnLines(await RunAsync(ServiceWith()));

        // BaseName graf tarafında küçük harfe normalize edilir (üretici eşlemesinin anahtarıdır); satır onu
        // olduğu gibi taşır, bu yüzden karşılaştırma harf kutusundan bağımsızdır.
        string line = Assert.Single(warns, l => l.Contains("osys.platform", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("unresolved reference: Consumer: osys.platform", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("build the producing solution first", line);
    }
}
