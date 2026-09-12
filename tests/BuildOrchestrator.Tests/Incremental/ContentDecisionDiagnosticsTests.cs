using System.Globalization;
using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Discovery;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.State;
using Xunit;
using System.Text.Json;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [tanı] "Başarıyla derledim, sonra Sync dedim, satırlar yine <c>modified</c>" kusurunun KÖK NEDENİNİ bulan
/// araç. Hiçbir şey assert etmez: kullanıcının GERÇEK durumunu (kendi <c>build-state.json</c>'ı ve kendi
/// çalışma alanı) okur, bugünkü imzayı yeniden hesaplar ve eşleşmeyen projeler için <b>son başarılı
/// derlemeden SONRA değişmiş girdi dosyalarını</b> adıyla listeler.
///
/// <para>Salt-okur: hiçbir dosyaya yazmaz, hiçbir state'i güncellemez. Varsayılan süitten hariçtir.</para>
/// </summary>
[Trait("Category", "Measurement")]
public sealed class ContentDecisionDiagnosticsTests(ITestOutputHelper output)
{
    private static string Root =>
        Environment.GetEnvironmentVariable("BO_MEASURE_ROOT") is { Length: > 0 } r ? r : @"D:\Projects\Delta\OSYS";

    private static string CacheRoot =>
        Environment.GetEnvironmentVariable("BO_CACHE_ROOT")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BuildOrchestrator");

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    /// <summary>Uygulamanın diskteki ayarları — tanı, kullanıcının GÖRDÜĞÜ planı yeniden kurmalıdır.</summary>
    private sealed record UiState(
        string Configuration, IReadOnlyList<ExternalProject> Externals, IReadOnlyList<LayerPattern> LayerPatterns)
    {
        public static UiState Read(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            string configuration = root.TryGetProperty("Configuration", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()! : "Debug";

            var externals = new List<ExternalProject>();
            if (root.TryGetProperty("ExternalProjects", out var e) && e.ValueKind == JsonValueKind.Array)
                foreach (var item in e.EnumerateArray())
                    externals.Add(new ExternalProject(item.GetProperty("Path").GetString()!));

            var layers = new List<LayerPattern>();
            if (root.TryGetProperty("LayerPatterns", out var l) && l.ValueKind == JsonValueKind.Array)
                foreach (var item in l.EnumerateArray())
                    layers.Add(new LayerPattern(item.GetProperty("Order").GetInt32(),
                        item.GetProperty("Regex").GetString()!, item.GetProperty("Name").GetString()!));

            return new UiState(configuration, externals, layers);
        }
    }

    [SkippableFact]
    public void Why_does_a_freshly_built_project_still_look_modified()
    {
        string root = Root;
        string cacheRoot = CacheRoot;
        Skip.IfNot(Directory.Exists(root), $"Workspace not found ({root}).");
        Skip.IfNot(File.Exists(Path.Combine(cacheRoot, "build-state.json")), $"build-state.json not found under {cacheRoot}.");

        var scanner = new WorkspaceScanner();
        var evaluator = new CsprojEvaluator();
        // ÖNEMLİ: kullanıcının kendi önbelleklerine YAZMAMAK için izole bir kopya kullanılır.
        string scratch = Directory.CreateTempSubdirectory("bo-diag-").FullName;
        var cache = new EvaluationCache(Path.Combine(scratch, "evaluation-cache.json"));

        // Uygulamanın GERÇEK yapılandırmasıyla aynı plan: harici kartlar ve katman tanımları dahil.
        // (Harici kökler üretici haritasını genişletir, yani bağımlılık KENARLARINI değiştirir — onlarsız
        // kurulan bir plan başka imzalar üretir ve tanı yanlış yere bakar.)
        var ui = UiState.Read(Path.Combine(cacheRoot, "ui-state.json"));
        var workspace = ExternalWorkspaceResolver.Resolve(scanner.Scan(root), ui.Externals, scanner);
        var scan = workspace.Scan;
        var plan = new BuildPlanBuilder(scanner, evaluator, cache)
            .Build(scan, ui.Configuration, ui.LayerPatterns, workspace.ExternalProjectIds);
        output.WriteLine(Inv($"- yapılandırma: {ui.Configuration} · harici kart: {ui.Externals.Count} · katman: {ui.LayerPatterns.Count}"));
        var evaluatedById = scan.CsprojPaths
            .Select(p => (Id: Path.GetFullPath(p), Project: cache.GetOrEvaluate(p, evaluator.Evaluate)))
            .Where(x => x.Project is not null)
            .ToDictionary(x => x.Id, x => x.Project!, StringComparer.OrdinalIgnoreCase);

        var state = new BuildStateStore(cacheRoot).Load();
        var hashes = new SourceHashCache(Path.Combine(scratch, SourceHashCache.FileName));

        // --- Sync'in yaptığı bağlama (eşleyicisiz) ve Build'in yaptığı bağlama (in-place eşleyiciyle):
        // ikisi AYNI imzayı üretmeli. Üretmiyorsa kusur buradadır.
        var syncBinder = new IncrementalRunBinder(plan, evaluatedById, root, hashes);
        syncBinder.Prefill();
        var (syncPlan, syncSignatures) = syncBinder.Bind(state, buildCycles: false, DependentMode.Safe);

        var buildBinder = new IncrementalRunBinder(plan, evaluatedById, root, hashes, logical => logical);
        var (_, buildSignatures) = buildBinder.Bind(state, buildCycles: false, DependentMode.Safe);

        int pathDisagreements = syncSignatures.Count(kv =>
            !buildSignatures.TryGetValue(kv.Key, out var other) || !string.Equals(kv.Value, other, StringComparison.Ordinal));

        output.WriteLine($"# Tanı — {root}");
        output.WriteLine(Inv($"- plan: {plan.Nodes.Count} proje · build-state kaydı: {state.Count}"));
        output.WriteLine(Inv($"- Sync yolu ile Build yolu arasında imza farkı: {pathDisagreements} (0 OLMALI)"));

        // --- Kayıtlı imza ile bugünkü imza karşılaştırması.
        var mismatched = new List<ProjectNode>();
        int matched = 0, noRecord = 0, notSucceeded = 0;
        foreach (var node in plan.Nodes)
        {
            if (!state.TryGetValue(node.Id, out var record) || record.BuiltSignature is null) { noRecord++; continue; }
            if (record.LastResult != BuildResult.Succeeded) { notSucceeded++; continue; }
            if (string.Equals(record.BuiltSignature, syncSignatures.GetValueOrDefault(node.Id), StringComparison.Ordinal)) matched++;
            else mismatched.Add(node);
        }

        output.WriteLine(Inv($"- kayıtlı imzasıyla EŞLEŞEN: {matched} · EŞLEŞMEYEN: {mismatched.Count} · kaydı yok: {noRecord} · son sonucu başarı değil: {notSucceeded}"));
        output.WriteLine("");

        // --- KENDİ terimleri mi değişti, yoksa yalnız upstream mi? Fast geçişi tam olarak bunu ayırır:
        // upstream'in KAYITLI imzasını okur, yani farkı yalnız projenin KENDİ terimlerinden alır.
        var (_, fastSignatures) = syncBinder.Bind(state, buildCycles: false, DependentMode.Fast);
        var ownDirty = mismatched
            .Where(n => !string.Equals(state[n.Id].BuiltSignature, fastSignatures.GetValueOrDefault(n.Id), StringComparison.Ordinal))
            .ToList();
        output.WriteLine(Inv($"- eşleşmeyenlerden KENDİ terimi değişmiş olan: {ownDirty.Count} · yalnız upstream'den etkilenen: {mismatched.Count - ownDirty.Count}"));

        // Döngü üyeliği AYIRT EDİCİDİR: bir SCC'nin imzası BİLEŞİKTİR (tüm üyeler + dışarıdaki upstream'ler),
        // yani tek bir üyenin durumu hepsini birden oynatır ve Build bir SCC'yi ASLA derlemez.
        int cycleMembers = plan.Cycles.Sum(c => c.Count);
        var mismatchedInCycle = mismatched.Count(n => n.InCycle);
        output.WriteLine(Inv($"- plan'da döngü: {plan.Cycles.Count} grup / {cycleMembers} üye · eşleşmeyenlerin {mismatchedInCycle} tanesi döngü üyesi"));
        foreach (var group in plan.Cycles.Take(6))
            output.WriteLine(Inv($"    · SCC({group.Count}): {string.Join(", ", group.Select(Path.GetFileNameWithoutExtension).Take(6))}"));
        output.WriteLine("");

        // --- Kullanıcının EKRANDA gördüğü: kaç satır derlenecek, kaç satır hangi etiketi taşıyor.
        // Üretimdeki kaynak: defterdeki içerik özetiyle bugünkünün karşılaştırması (Fast geçişi DEĞİL).
        var content = syncBinder.ContentById;
        var labels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in syncPlan.Nodes)
        {
            var decision = BuildOrchestrator.App.ViewModels.DecisionLabel.For(
                node.WillBuild, node.WillBuildReason,
                BuildStateStore.OwnFilesChanged(state, node.Id, content.GetValueOrDefault(node.Id)),
                BuildStateStore.LastBuiltAtOf(state, node.Id), DateTimeOffset.Now);
            string key = decision.IsEmpty ? "(BOŞ)" : decision.Word;
            labels[key] = labels.GetValueOrDefault(key) + 1;
        }
        output.WriteLine(Inv($"- derlenecek (WillBuild=true): {syncPlan.Nodes.Count(n => n.WillBuild == true)} · atlanacak: {syncPlan.Nodes.Count(n => n.WillBuild == false)}"));
        foreach (var (label, count) in labels.OrderByDescending(kv => kv.Value))
            output.WriteLine(Inv($"    · {label}: {count} satır"));
        output.WriteLine("## Derlenecek satırlar (kullanıcının gördüğü)");
        foreach (var node in syncPlan.Nodes.Where(n => n.WillBuild == true))
        {
            var r = state.GetValueOrDefault(node.Id);
            output.WriteLine(Inv($"- {node.Name} · reason={node.WillBuildReason} · kendi dosyası değişti={BuildStateStore.OwnFilesChanged(state, node.Id, content.GetValueOrDefault(node.Id))} · kayıt={(r is null ? "yok" : r.LastResult.ToString())} · depIssue={r?.DepIssue}"));
        }
        output.WriteLine("");

        output.WriteLine("## Etiketi BOŞ kalan satırlar (ilk 10)");
        foreach (var node in syncPlan.Nodes.Where(n =>
            BuildOrchestrator.App.ViewModels.DecisionLabel.For(n.WillBuild, n.WillBuildReason,
                BuildStateStore.OwnFilesChanged(state, n.Id, content.GetValueOrDefault(n.Id)),
                BuildStateStore.LastBuiltAtOf(state, n.Id), DateTimeOffset.Now).IsEmpty).Take(10))
        {
            var r = state.GetValueOrDefault(node.Id);
            output.WriteLine(Inv($"- {node.Name} · inCycle={node.InCycle} · will={node.WillBuild} · reason={node.WillBuildReason} · kayıt={(r is null ? "yok" : r.LastResult.ToString())}"));
        }
        output.WriteLine("");

        // --- Kaskadın KÖKLERİ: kendi terimi değişmiş ya da kaydı/başarısı olmayan projeler.
        var dirtyRoots = plan.Nodes.Where(n =>
            !state.TryGetValue(n.Id, out var r) || r.BuiltSignature is null || r.LastResult != BuildResult.Succeeded
            || !string.Equals(r.BuiltSignature, fastSignatures.GetValueOrDefault(n.Id), StringComparison.Ordinal)).ToList();
        output.WriteLine(Inv($"- kaskadın kökleri (kaydı yok / başarısız / kendi terimi değişmiş): {dirtyRoots.Count}"));
        foreach (var node in dirtyRoots.Take(25))
        {
            var r = state.GetValueOrDefault(node.Id);
            string why = r?.BuiltSignature is null ? "kayıt yok"
                : r.LastResult != BuildResult.Succeeded ? Inv($"son sonuç {r.LastResult}")
                : "kendi terimi değişti";
            output.WriteLine(Inv($"    · {node.Name} — {why}"));
        }
        output.WriteLine("");

        // --- Eşleşmeyenler için: son başarılı derlemeden SONRA dokunulmuş girdi dosyaları.
        output.WriteLine("## Eşleşmeyen projelerde, son derlemeden SONRA değişmiş girdi dosyaları");
        var suspects = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in mismatched.Take(12))
        {
            var record = state[node.Id];
            // NOT: "LastRunAt'ten sonra" filtresi kusurluydu — derlemenin ÜRETTİĞİ dosyaların mtime'ı
            // kaydın yazıldığı andan bir tık ÖNCEDİR. Bu yüzden en yeni girdiler doğrudan listelenir.
            var newest = syncBinder.InputsOf(node.Id)
                .Select(i => new FileInfo(i.PhysicalPath))
                .Where(f => f.Exists)
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToList();

            output.WriteLine(Inv($"- {node.Name}: {newest.Count} girdi · son derleme {record.LastRunAt:HH:mm:ss}"));
            foreach (var f in newest.Take(5))
                output.WriteLine(Inv($"    · {f.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} {Path.GetRelativePath(root, f.FullName)}"));
        }

        // --- Ad kalıbı: hangi dosya ADLARI sık sık "sonradan değişmiş" çıkıyor?
        foreach (var node in mismatched)
        {
            var record = state[node.Id];
            foreach (var input in syncBinder.InputsOf(node.Id))
            {
                var f = new FileInfo(input.PhysicalPath);
                // Derlemenin ürettiği dosyalar: mtime'ı kaydın yazıldığı ana ÇOK yakın (öncesi de olabilir).
                if (!f.Exists || record.LastRunAt is not { } at
                    || (at.UtcDateTime - f.LastWriteTimeUtc).TotalMinutes > 30) continue;
                string key = f.Name.Contains('.') ? "*" + f.Name[f.Name.IndexOf('.')..] : f.Name;
                suspects[key] = suspects.GetValueOrDefault(key) + 1;
            }
        }

        output.WriteLine("");
        output.WriteLine("## Sonradan değişen dosyaların ad kalıpları (en sık 15)");
        foreach (var (name, count) in suspects.OrderByDescending(kv => kv.Value).Take(15))
            output.WriteLine(Inv($"- {name}: {count}"));

        Assert.True(plan.Nodes.Count > 0);
        _ = syncPlan;
    }
}
