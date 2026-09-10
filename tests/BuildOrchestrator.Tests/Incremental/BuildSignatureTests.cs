using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Tests.Incremental;

// [T25][A6][D6][D1] BuildSignature.Compute determinism + sinyal testleri (incremental build çekirdeği).
// Signature = configuration + content fingerprint (projenin girdi dosyalarının DİSKTEKİ içeriği) +
// transitive upstream imzaları.
//
// DEĞİŞEN KURAL: imza eskiden dört terimliydi — cfg + committed fingerprint (git ls-tree blob'ları) +
// (yalnız in-place modda) working-tree'deki kirli dosyaların içeriğinden kurulan bir "local-diff" terimi +
// upstream. Bu dosyadaki dirty/inPlace testleri o dönemin pinleriydi. Kural D1 ile değişti: karar tek bir
// kaynaktan, diskten veriliyor; git imzaya HİÇ girmiyor ve mod ayrımı (in-place / worktree) ortadan kalktı.
// Gerekçe ölçümle birlikte plana yazılıdır (2026-09-10-01-25-content-based-incremental-plan.md):
// commit'lenmiş xaml/resx değişiklikleri ve git'e girmemiş dosyalar eski formülde GÖRÜNMÜYORDU (under-build).
// Dosya içeriğinin imzaya nasıl girdiğini artık ComputeContentFingerprintTests, girdi kümesinin ne olduğunu
// ProjectInputsTests, mod eşitliğini IncrementalRunBinderTests pinler.
public class BuildSignatureTests
{
    private static ProjectNode Node(string id, params string[] dependencies) =>
        new(id, id, id, [], dependencies, 0, null, null, InCycle: false, WillBuild: null);

    private static readonly Func<string, string?> NoUpstream = _ => null;

    // ---- Determinism / byte-stable ----------------------------------------------------------

    [Fact]
    public void identical_inputs_produce_byte_identical_signature_across_two_calls()
    {
        var node = Node("A", "Dep1", "Dep2");
        Func<string, string?> upstream = id => id == "Dep1" ? "sigDep1" : "sigDep2";

        string sig1 = BuildSignature.Compute(node, "Debug", "abc123", upstream);
        string sig2 = BuildSignature.Compute(node, "Debug", "abc123", upstream);

        Assert.Equal(sig1, sig2);
    }

    [Fact]
    public void reordering_upstream_dependencies_yields_same_signature()
    {
        var nodeAB = Node("A", "Dep1", "Dep2");
        var nodeBA = Node("A", "Dep2", "Dep1"); // aynı bağımlılıklar, ters sırada tanımlı
        Func<string, string?> upstream = id => id == "Dep1" ? "sigDep1" : "sigDep2";

        Assert.Equal(
            BuildSignature.Compute(nodeAB, "Debug", "abc123", upstream),
            BuildSignature.Compute(nodeBA, "Debug", "abc123", upstream));
    }

    // ---- Config değişimi ----------------------------------------------------------------------

    [Fact]
    public void changing_configuration_changes_signature()
    {
        var node = Node("A");

        Assert.NotEqual(
            BuildSignature.Compute(node, "Debug", "abc123", NoUpstream),
            BuildSignature.Compute(node, "Release", "abc123", NoUpstream));
    }

    // ---- İçerik fingerprint'i [D1] ----------------------------------------------------------

    [Fact]
    public void changing_content_fingerprint_changes_signature()
    {
        var node = Node("A");

        Assert.NotEqual(
            BuildSignature.Compute(node, "Debug", "fp1", NoUpstream),
            BuildSignature.Compute(node, "Debug", "fp2", NoUpstream));
    }

    [Fact]
    public void null_content_fingerprint_is_tolerated_and_differs_from_a_real_fingerprint()
    {
        // null = projenin hiçbir girdisi okunamadı (silinmiş klasör, kilitli dosyalar). Fırlatmaz; imzaya
        // sabit bir işaretle girer, yani gerçek bir fingerprint'ten AYIRT EDİLİR (proje yeniden derlenir).
        var node = Node("A");

        Assert.NotEqual(
            BuildSignature.Compute(node, "Debug", null, NoUpstream),
            BuildSignature.Compute(node, "Debug", "fp1", NoUpstream));
    }

    // ---- Transitive upstream ----------------------------------------------------------------------

    [Fact]
    public void upstream_signature_change_propagates_to_downstream_signature()
    {
        var node = Node("A", "Dep1");

        Assert.NotEqual(
            BuildSignature.Compute(node, "Debug", "abc123", _ => "sigOld"),
            BuildSignature.Compute(node, "Debug", "abc123", _ => "sigNew"));
    }

    [Fact]
    public void unknown_upstream_signature_null_is_tolerated_and_differs_from_a_real_signature()
    {
        var node = Node("A", "Dep1");

        Assert.NotEqual(
            BuildSignature.Compute(node, "Debug", "abc123", _ => null),
            BuildSignature.Compute(node, "Debug", "abc123", _ => "sigDep1"));
    }

    // ---- Hash-per-term collision-resistance (raw id ayraç yanına gömülmez) ---------------------

    [Fact]
    public void upstream_ids_containing_separator_and_equals_characters_do_not_cause_signature_collision()
    {
        // Boundary-shift saldırısı: RAW upstreamId (hash'lenmeden) doğrudan '='+ItemSeparator yanına
        // gömülseydi, iki FARKLI upstream term kümesi aynı pre-hash string'e indirgenebilirdi:
        //   SetA: Dep1->"S1", Dep2->"S2"           => "Dep1=S1" ItemSep "Dep2=S2" ItemSep
        //   SetB: tek id "Dep1=S1<ItemSep>Dep2"->"S2" => AYNI concatenation (id'nin içine gömülü
        //         "=S1<ItemSep>Dep2" parçası, SetA'daki iki-öğeli sınırı taklit eder)
        // HashText(upstreamId) düzeltmesiyle bu iki küme FARKLI imza üretir.
        char itemSep = (char)0x1E;

        var nodeA = Node("A", "Dep1", "Dep2");
        Func<string, string?> upstreamA = id => id switch { "Dep1" => "S1", "Dep2" => "S2", _ => null };

        string collidingId = "Dep1=S1" + itemSep + "Dep2";
        var nodeB = Node("A", collidingId);
        Func<string, string?> upstreamB = id => id == collidingId ? "S2" : null;

        Assert.NotEqual(
            BuildSignature.Compute(nodeA, "Debug", "abc123", upstreamA),
            BuildSignature.Compute(nodeB, "Debug", "abc123", upstreamB));
    }

    // ---- Build-etkileyen uzantı filtresi ------------------------------------------------------

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

        // Signature bir projenin KENDİ Id'sini taşımaz (o zaten BuildState.ProjectId anahtarıyla eşlenir);
        // yalnız cfg + içerik + upstream taşır. Bu, tasarım kararının açık kanıtıdır.
        Assert.Equal(
            BuildSignature.Compute(nodeA, "Debug", "abc123", NoUpstream),
            BuildSignature.Compute(nodeB, "Debug", "abc123", NoUpstream));
    }
}
