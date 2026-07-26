using System.IO;
using System.Text.RegularExpressions;
using Xunit;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.MsBuild;

public class MsBuildArgumentsTests
{
    [Fact]
    public void Build_contains_v1_flags_and_BuildProjectReferences_false() // [SPIKE S2 şart-3 + D9]
    {
        var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug");
        Assert.Contains("-p:UseSharedCompilation=false", args);
        Assert.Contains("-nodeReuse:false", args);
        Assert.Contains("-p:BuildProjectReferences=false", args);
        Assert.Contains("-p:Configuration=Debug", args);
        Assert.Equal(@"c:\r\p.csproj", args[0]);
    }

    /// <summary>[T33 KARAR PİNİ] Shared compilation KAPALI kalır — karar ve gerekçe:
    /// <c>.claude/outputs/2026-07-26-07-38-t33-decision.md</c>. Yukarıdaki test bayrakların VARLIĞINI pinler;
    /// bu test ters yönü kapatır: hiçbir yol (build ya da restore) shared compilation'ı ya da node reuse'u
    /// GERİ AÇAMAZ. Açılırsa emit, inner Job'a üye OLMAYAN kalıcı <c>VBCSCompiler</c>'a taşınır ve §3'ün
    /// "torn DLL yok" garantisi kill anında kırılır (bkz. KillMidBuildTests: bayraklar → VBCSCompiler yok →
    /// her writer Job üyesi).</summary>
    [Fact]
    public void Shared_compilation_and_node_reuse_can_not_be_re_enabled_on_the_build_path() // [T33]
    {
        string[] reEnabling = ["UseSharedCompilation=true", "nodeReuse:true", "-m:", "MSBUILDDISABLENODEREUSE=0"];
        var build = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", @"c:\wt\obj\c__r_p");

        foreach (string flag in reEnabling)
            Assert.DoesNotContain(build, a => a.Contains(flag, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(1, build.Count(a => a == "-p:UseSharedCompilation=false")); // tek kez, çelişen ikinci değer YOK
        Assert.Equal(1, build.Count(a => a == "-nodeReuse:false"));
    }

    /// <summary>[T33 · fix round 1 · Important 5] Restore yolu neden bu bayrakları TAŞIMIYOR: restore DERLEMEZ,
    /// yalnız <c>-t:restore</c> hedefini koşar — compiler server (<c>VBCSCompiler</c>) hiç devreye girmez, emit
    /// olmaz, dolayısıyla torn-DLL yüzeyi yoktur. Bu testin önceki hâli restore'da "shared compilation açılmamış"
    /// diye assert ediyordu; restore o bayrağı ZATEN hiç geçirmediği için o assert KIRILAMAZDI (sahte kapsama).
    /// Gerçek ve kırılabilir pin budur: restore'un hedef kümesi Build'i İÇERMEZ. İçerecek şekilde değişirse
    /// (ör. <c>-t:restore;Build</c>) burada RED verir ve o yolun da flag'lenmesi gerektiği anlaşılır.</summary>
    [Fact]
    public void The_restore_path_compiles_nothing_which_is_why_it_carries_no_compiler_flags() // [T33]
    {
        var restore = MsBuildArguments.RestorePackagesConfig(@"c:\r\p.csproj", @"c:\r\slnDir");

        var targets = restore.Where(a => a.StartsWith("-t:", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(["-t:restore"], targets);                       // TEK hedef: restore
        Assert.DoesNotContain(restore, a => a.Contains("Build", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>[T33 · fix round 1 · Important 6] Bayrakların TEK KAYNAK iddiasının pini. Child process'ler
    /// parent ortamını miras alır (<c>JobProcessLauncher</c> env'i devreder), yani bu ayarlar komut satırı DIŞINDA
    /// da (env değişkeni, <c>Directory.Build.props</c>) doğabilir. Komut satırındaki <c>-p:</c> global property'si
    /// MSBuild önceliğinde environment property'sini EZER — asıl risk budur ve kapalıdır; buradaki guard ise
    /// "bu değerleri üretimde başka bir yerin yazmadığını" kilitler: <c>UseSharedCompilation</c> / <c>nodeReuse</c>
    /// / <c>MSBUILDDISABLENODEREUSE</c> literalleri üretim kodunda (<c>src/**.cs</c>) ve MSBuild yapılandırma
    /// dosyalarında (<b>TÜM repo</b>: <c>*.csproj</c> · <c>*.props</c> · <c>*.targets</c>) yalnız
    /// <see cref="MsBuildArguments"/>'ta geçebilir. Kalan yüzey (kullanıcı makinesinden MİRAS gelen env) karar
    /// kaydında açık sınırlama olarak yazılıdır.
    ///
    /// <para>[fix round 2 · Important 2] Yapılandırma kolu artık <c>src/</c>'den DEĞİL repo kökünden taranır:
    /// kökteki <c>Directory.Build.props</c> <c>src/</c> ağacının DIŞINDADIR, yani eski hâlde props/targets kolu
    /// <b>sıfır dosya</b> tarıyordu — flag oraya eklense pin hiç fark etmezdi. Ayrıca her kol için taramanın
    /// dosya GÖRDÜĞÜ ayrıca assert edilir: sıfır-dosya taraması artık sessizce yeşil kalmaz, RED verir.</para></summary>
    [Fact]
    public void The_compiler_server_switches_have_exactly_one_source_in_the_repo() // [T33]
    {
        var rule = new Regex("UseSharedCompilation|nodeReuse|MSBUILDDISABLENODEREUSE", RegexOptions.IgnoreCase);
        string srcOwner = Path.Combine("BuildOrchestrator.Core", "MsBuild", "MsBuildArguments.cs");
        string repoOwner = Path.Combine("src", srcOwner);

        var offenders = new List<string>();

        // (a) Üretim KODU — src ağacı (testler hariç: onlar bu literalleri anlatmak için kullanır).
        Assert.Contains(srcOwner, SourceGuard.ScannedSrcFiles("*.cs")); // tarama dosya görüyor mu
        offenders.AddRange(SourceGuard.ScanSrc("*.cs", rule, [srcOwner], skipCommentLines: true));

        // (b) MSBuild YAPILANDIRMASI — TÜM repo (kökteki Directory.Build.props dahil).
        foreach (string pattern in new[] { "*.csproj", "*.props", "*.targets" })
        {
            var scanned = SourceGuard.ScannedRepoFiles(pattern);
            if (pattern != "*.targets")                                 // *.targets bugün repoda YOK — kol boş olabilir
                Assert.NotEmpty(scanned);                               // …ama csproj/props kolları GERÇEKTEN dosya görmeli
            offenders.AddRange(SourceGuard.ScanRepo(pattern, rule, [repoOwner], skipCommentLines: true));
        }
        Assert.Contains("Directory.Build.props", SourceGuard.ScannedRepoFiles("*.props")); // kök props GERÇEKTEN taranıyor

        Assert.Empty(offenders);
        Assert.Matches(rule, File.ReadAllText(Path.Combine(RepoPaths.SrcRoot, srcOwner))); // muaf dosya GERÇEKTEN eşleşiyor
    }

    [Fact]
    public void Build_with_obj_isolation_has_trailing_backslash() // [SPIKE S2 şart-2 — bayat obj zehri]
    {
        var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", @"c:\wt\obj\c__r_p");
        Assert.Contains(@"-p:BaseIntermediateOutputPath=c:\wt\obj\c__r_p\", args);
    }

    [Fact]
    public void Build_with_null_obj_isolation_emits_no_BaseIntermediateOutputPath_arg() // [I2-K2] in-place = VS-parity, projenin kendi obj'i
    {
        var args = MsBuildArguments.Build(@"c:\r\p.csproj", "Debug", baseIntermediateOutputPath: null);
        Assert.DoesNotContain(args, a => a.StartsWith("-p:BaseIntermediateOutputPath=", StringComparison.Ordinal));
    }

    [Fact]
    public void Restore_requires_solutionDir_with_trailing_backslash_and_no_nuget_exe() // [SPIKE S2 şart-1 + S1]
    {
        var args = MsBuildArguments.RestorePackagesConfig(@"c:\r\p.csproj", @"c:\r\slnDir");
        Assert.Contains("-t:restore", args);
        Assert.Contains("-p:RestorePackagesConfig=true", args);
        Assert.Contains(@"-p:SolutionDir=c:\r\slnDir\", args);
        Assert.DoesNotContain(args, a => a.Contains("nuget", StringComparison.OrdinalIgnoreCase)); // nuget.exe bağımlılığı YOK
    }

    [Theory]
    [InlineData(@"c:\x", @"c:\x\")] [InlineData(@"c:\x\", @"c:\x\")] [InlineData("c:/x/", "c:/x/")]
    public void EnsureTrailingBackslash(string input, string expected)
        => Assert.Equal(expected, MsBuildArguments.EnsureTrailingBackslash(input));
}
