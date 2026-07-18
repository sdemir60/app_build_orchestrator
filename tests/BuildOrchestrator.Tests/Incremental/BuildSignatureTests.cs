using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Tests.Incremental;

// [T25][A6][D6] BuildSignature.Compute determinism + sinyal testleri (It-3 — incremental build çekirdeği).
// Signature = configuration + committedFingerprint (PER-PROJECT, A6 refinement — bkz. Task 7b) +
// (yalnız in-place) local-diff hash + transitive upstream imzaları.
public class BuildSignatureTests
{
    private static ProjectNode Node(string id, params string[] dependencies) =>
        new(id, id, id, [], dependencies, 0, null, null, InCycle: false, WillBuild: null);

    private static Func<string, string> ContentMap(params (string Path, string Content)[] entries)
    {
        var map = entries.ToDictionary(e => e.Path, e => e.Content, StringComparer.Ordinal);
        return path => map.TryGetValue(path, out var c) ? c : throw new KeyNotFoundException(path);
    }

    private static readonly Func<string, string?> NoUpstream = _ => null;
    private static readonly Func<string, string> NoRead = _ => throw new InvalidOperationException("okunmamalıydı");

    // ---- Determinism / byte-stable ----------------------------------------------------------

    [Fact]
    public void identical_inputs_produce_byte_identical_signature_across_two_calls()
    {
        var node = Node("A", "Dep1", "Dep2");
        var read = ContentMap(("f1.cs", "int x;"), ("f2.cs", "int y;"));
        Func<string, string?> upstream = id => id == "Dep1" ? "sigDep1" : "sigDep2";

        string sig1 = BuildSignature.Compute(node, "Debug", "abc123", ["f1.cs", "f2.cs"], read, upstream, inPlace: true);
        string sig2 = BuildSignature.Compute(node, "Debug", "abc123", ["f1.cs", "f2.cs"], read, upstream, inPlace: true);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void reordering_dirty_file_list_yields_same_signature()
    {
        var node = Node("A");
        var read = ContentMap(("f1.cs", "int x;"), ("f2.cs", "int y;"));

        string sigOrder1 = BuildSignature.Compute(node, "Debug", "abc123", ["f1.cs", "f2.cs"], read, NoUpstream, inPlace: true);
        string sigOrder2 = BuildSignature.Compute(node, "Debug", "abc123", ["f2.cs", "f1.cs"], read, NoUpstream, inPlace: true);

        Assert.Equal(sigOrder1, sigOrder2);
    }

    [Fact]
    public void reordering_upstream_dependencies_yields_same_signature()
    {
        var nodeAB = Node("A", "Dep1", "Dep2");
        var nodeBA = Node("A", "Dep2", "Dep1"); // aynı bağımlılıklar, ters sırada tanımlı
        Func<string, string?> upstream = id => id == "Dep1" ? "sigDep1" : "sigDep2";

        string sig1 = BuildSignature.Compute(nodeAB, "Debug", "abc123", [], NoRead, upstream, inPlace: false);
        string sig2 = BuildSignature.Compute(nodeBA, "Debug", "abc123", [], NoRead, upstream, inPlace: false);

        Assert.Equal(sig1, sig2);
    }

    // ---- Config değişimi ----------------------------------------------------------------------

    [Fact]
    public void changing_configuration_changes_signature()
    {
        var node = Node("A");

        string debug = BuildSignature.Compute(node, "Debug", "abc123", [], NoRead, NoUpstream, inPlace: false);
        string release = BuildSignature.Compute(node, "Release", "abc123", [], NoRead, NoUpstream, inPlace: false);

        Assert.NotEqual(debug, release);
    }

    // ---- Committed fingerprint değişimi [A6 refinement — Task 7b: PER-PROJECT, global HEAD DEĞİL] --------

    [Fact]
    public void changing_committed_fingerprint_changes_signature()
    {
        var node = Node("A");

        string sig1 = BuildSignature.Compute(node, "Debug", "fp1", [], NoRead, NoUpstream, inPlace: false);
        string sig2 = BuildSignature.Compute(node, "Debug", "fp2", [], NoRead, NoUpstream, inPlace: false);

        Assert.NotEqual(sig1, sig2);
    }

    [Fact]
    public void null_committed_fingerprint_is_tolerated_and_differs_from_a_real_fingerprint()
    {
        var node = Node("A");

        string sigNull = BuildSignature.Compute(node, "Debug", null, [], NoRead, NoUpstream, inPlace: false);
        string sigReal = BuildSignature.Compute(node, "Debug", "fp1", [], NoRead, NoUpstream, inPlace: false);

        Assert.NotEqual(sigNull, sigReal);
    }

    // ---- In-place local-diff --------------------------------------------------------------------

    [Fact]
    public void inplace_dirty_file_content_change_changes_signature()
    {
        var node = Node("A");
        var readV1 = ContentMap(("f1.cs", "int x;"));
        var readV2 = ContentMap(("f1.cs", "int x = 1;")); // aynı dosya, farklı içerik

        string sigV1 = BuildSignature.Compute(node, "Debug", "abc123", ["f1.cs"], readV1, NoUpstream, inPlace: true);
        string sigV2 = BuildSignature.Compute(node, "Debug", "abc123", ["f1.cs"], readV2, NoUpstream, inPlace: true);

        Assert.NotEqual(sigV1, sigV2);
    }

    [Fact]
    public void inplace_clean_project_no_dirty_files_is_stable()
    {
        var node = Node("A");

        string sig1 = BuildSignature.Compute(node, "Debug", "abc123", [], NoRead, NoUpstream, inPlace: true);
        string sig2 = BuildSignature.Compute(node, "Debug", "abc123", [], NoRead, NoUpstream, inPlace: true);

        Assert.Equal(sig1, sig2);
    }

    // ---- Transitive upstream ----------------------------------------------------------------------

    [Fact]
    public void upstream_signature_change_propagates_to_downstream_signature()
    {
        var node = Node("A", "Dep1");

        string sigWithOldUpstream = BuildSignature.Compute(node, "Debug", "abc123", [], NoRead, _ => "sigOld", inPlace: false);
        string sigWithNewUpstream = BuildSignature.Compute(node, "Debug", "abc123", [], NoRead, _ => "sigNew", inPlace: false);

        Assert.NotEqual(sigWithOldUpstream, sigWithNewUpstream);
    }

    [Fact]
    public void unknown_upstream_signature_null_is_tolerated_and_differs_from_a_real_signature()
    {
        var node = Node("A", "Dep1");

        string sigNullUpstream = BuildSignature.Compute(node, "Debug", "abc123", [], NoRead, _ => null, inPlace: false);
        string sigRealUpstream = BuildSignature.Compute(node, "Debug", "abc123", [], NoRead, _ => "sigDep1", inPlace: false);

        Assert.NotEqual(sigNullUpstream, sigRealUpstream);
    }

    // ---- Worktree/committed modda local-diff DAHİL EDİLMEZ ------------------------------------------

    [Fact]
    public void worktree_mode_ignores_dirty_files_entirely_only_commit_and_upstream_matter()
    {
        var node = Node("A", "Dep1");
        var read = ContentMap(("f1.cs", "int x;"), ("f2.cs", "different content entirely"));
        Func<string, string?> upstream = _ => "sigDep1";

        // Aynı commit + aynı upstream, ama dirty dosya listesi TAMAMEN farklı (biri boş, biri dolu,
        // içerikleri de farklı) — worktree (committed) modda (inPlace=false) sonuç DEĞİŞMEMELİ.
        string sigNoDirty = BuildSignature.Compute(node, "Debug", "abc123", [], read, upstream, inPlace: false);
        string sigWithDirty = BuildSignature.Compute(node, "Debug", "abc123", ["f1.cs", "f2.cs"], read, upstream, inPlace: false);

        Assert.Equal(sigNoDirty, sigWithDirty);
    }

    [Fact]
    public void worktree_mode_never_calls_read_file_content_even_when_dirty_files_given()
    {
        var node = Node("A");

        // NoRead throw eder eğer çağrılırsa — worktree modda hiç çağrılmamalı, dirty liste dolu olsa bile.
        string sig = BuildSignature.Compute(node, "Debug", "abc123", ["f1.cs"], NoRead, NoUpstream, inPlace: false);

        Assert.False(string.IsNullOrEmpty(sig)); // exception atılmadan tamamlandı
    }

    // ---- In-place dirty dosya varlığı/yokluğu (simetrik) --------------------------------------------

    [Fact]
    public void inplace_mode_empty_vs_nonempty_dirty_list_yields_different_signature()
    {
        // worktree_mode_ignores_dirty_files_entirely_only_commit_and_upstream_matter testinin simetriği:
        // inPlace=TRUE iken boş dirty listesi ile dolu dirty listesi FARKLI imza üretmeli (doğrudan
        // varlık/yokluk testi — worktree=false'ta ise aynı olmalıydı, bu zaten ayrı testle kanıtlı).
        var node = Node("A");
        var read = ContentMap(("f1.cs", "int x;"));

        string sigNoDirty = BuildSignature.Compute(node, "Debug", "abc123", [], NoRead, NoUpstream, inPlace: true);
        string sigWithDirty = BuildSignature.Compute(node, "Debug", "abc123", ["f1.cs"], read, NoUpstream, inPlace: true);

        Assert.NotEqual(sigNoDirty, sigWithDirty);
    }

    // ---- Hash-per-term collision-resistance (raw path/id ayraç yanına gömülmez) ---------------------

    [Fact]
    public void upstream_ids_containing_separator_and_equals_characters_do_not_cause_signature_collision()
    {
        // Boundary-shift saldırısı: RAW upstreamId (hash'lenmeden) doğrudan '='+ItemSeparator yanına
        // gömülseydi, iki FARKLI upstream term kümesi aynı pre-hash string'e indirgenebilirdi:
        //   SetA: Dep1->"S1", Dep2->"S2"           => "Dep1=S1" ItemSep "Dep2=S2" ItemSep
        //   SetB: tek id "Dep1=S1<ItemSep>Dep2"->"S2" => AYNI concatenation (id'nin içine gömülü
        //         "=S1<ItemSep>Dep2" parçası, SetA'daki iki-öğeli sınırı taklit eder)
        // HashText(upstreamId) düzeltmesiyle bu iki küme artık FARKLI imza üretmeli.
        char itemSep = (char)0x1E;

        var nodeA = Node("A", "Dep1", "Dep2");
        Func<string, string?> upstreamA = id => id switch { "Dep1" => "S1", "Dep2" => "S2", _ => null };

        string collidingId = "Dep1=S1" + itemSep + "Dep2";
        var nodeB = Node("A", collidingId);
        Func<string, string?> upstreamB = id => id == collidingId ? "S2" : null;

        string sigA = BuildSignature.Compute(nodeA, "Debug", "abc123", [], NoRead, upstreamA, inPlace: false);
        string sigB = BuildSignature.Compute(nodeB, "Debug", "abc123", [], NoRead, upstreamB, inPlace: false);

        Assert.NotEqual(sigA, sigB);
    }

    [Fact]
    public void dirty_file_path_containing_separator_and_equals_characters_yields_different_signature_than_benign_path()
    {
        // Minimum robustness kanıtı: dosya yolu içinde literal ItemSeparator/'=' karakterleri geçse
        // bile (ör. bozuk/egzotik bir path) fonksiyon çökmez ve benzer-ama-farklı benign bir yoldan
        // FARKLI bir imza üretir — path artık raw değil HashText(path) olarak gömülür.
        char itemSep = (char)0x1E;
        var node = Node("A");

        string weirdPath = "a" + itemSep + "b=c.cs";
        var readWeird = ContentMap((weirdPath, "int x;"));
        var readBenign = ContentMap(("abc.cs", "int x;"));

        string sigWeird = BuildSignature.Compute(node, "Debug", "abc123", [weirdPath], readWeird, NoUpstream, inPlace: true);
        string sigBenign = BuildSignature.Compute(node, "Debug", "abc123", ["abc.cs"], readBenign, NoUpstream, inPlace: true);

        Assert.NotEqual(sigWeird, sigBenign);
    }

    // ---- Non-build-affecting dosya filtresi ------------------------------------------------------

    [Fact]
    public void non_build_affecting_dirty_file_does_not_change_signature()
    {
        var node = Node("A");
        var read = ContentMap(("README.md", "v1"));
        var readChanged = ContentMap(("README.md", "v2 completely different"));

        string sig1 = BuildSignature.Compute(node, "Debug", "abc123", ["README.md"], read, NoUpstream, inPlace: true);
        string sig2 = BuildSignature.Compute(node, "Debug", "abc123", ["README.md"], readChanged, NoUpstream, inPlace: true);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void non_build_affecting_dirty_file_content_is_never_read()
    {
        var node = Node("A");

        // NoRead throw eder — .txt build-etkileyen olmadığı için filtrelenip hiç okunmamalı.
        string sig = BuildSignature.Compute(node, "Debug", "abc123", ["notes.txt"], NoRead, NoUpstream, inPlace: true);

        Assert.False(string.IsNullOrEmpty(sig));
    }

    [Theory]
    [InlineData("Foo.cs", true)]
    [InlineData("App.xaml", true)]
    [InlineData("Strings.resx", true)]
    [InlineData("Project.csproj", true)]
    [InlineData("Directory.Build.props", true)]
    [InlineData("Directory.Build.targets", true)]
    [InlineData("README.md", false)]
    [InlineData("notes.txt", false)]
    [InlineData("noextension", false)]
    public void IsBuildAffecting_filter_matches_global_constraints_extension_list(string path, bool expected)
        => Assert.Equal(expected, BuildSignature.IsBuildAffecting(path));

    // ---- ProjectNode.Id / diğer alanlar imzayı etkilememeli (yalnız Dependencies kullanılır) ----------

    [Fact]
    public void differing_node_id_alone_does_not_change_signature_when_dependencies_equal()
    {
        var nodeA = Node("A");
        var nodeB = Node("B"); // farklı Id, aynı (boş) Dependencies

        string sigA = BuildSignature.Compute(nodeA, "Debug", "abc123", [], NoRead, NoUpstream, inPlace: false);
        string sigB = BuildSignature.Compute(nodeB, "Debug", "abc123", [], NoRead, NoUpstream, inPlace: false);

        // Signature bir projenin KENDİ Id'sini taşımaz (o zaten BuildState.ProjectId anahtarıyla eşlenir);
        // yalnız config+commit+diff+upstream taşır. Bu, tasarım kararının açık kanıtıdır.
        Assert.Equal(sigA, sigB);
    }
}
