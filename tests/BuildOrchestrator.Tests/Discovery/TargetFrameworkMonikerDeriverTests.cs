using BuildOrchestrator.Core.Discovery;

namespace BuildOrchestrator.Tests.Discovery;

// [T72/Task 14] StaleObjDetector.Inspect'in beklediği TAM moniker'ı csproj'un ham
// TargetFrameworkVersion (legacy) / TargetFramework (SDK-style) değerinden türetir.
public class TargetFrameworkMonikerDeriverTests
{
    [Theory]
    [InlineData("v4.6", ".NETFramework,Version=v4.6")]
    [InlineData("v4.8", ".NETFramework,Version=v4.8")]
    public void legacy_target_framework_version_becomes_dot_net_framework_moniker(string tfv, string expected) =>
        Assert.Equal(expected, TargetFrameworkMonikerDeriver.FromRaw(tfv, null));

    // [Review fix/Task 14] SDK-style netstandardX.Y'nin project.assets.json "targets" anahtarı da diğer SDK-style
    // TFM'ler gibi KISA biçimdir (asla ".NETStandard,Version=vX.Y" uzun biçimine restore edilmez) — bu yüzden
    // burada da ham kısa TFM olduğu gibi geçmeli, yoksa temiz bir SDK-style netstandard projesi StaleObjDetector
    // tarafından yanlışlıkla "stale" işaretlenir (bkz. StaleObjRunStartWarnerTests round-trip testi).
    [Fact]
    public void sdk_style_netstandard_passes_through_unchanged() =>
        Assert.Equal("netstandard2.0", TargetFrameworkMonikerDeriver.FromRaw(null, "netstandard2.0"));

    // net5.0+ SDK projelerinde project.assets.json "targets" anahtarı UZUN moniker DEĞİL, KISA TFM'nin
    // kendisidir (doğrulandı: bu repodaki src/BuildOrchestrator.Supervisor/obj/project.assets.json →
    // "targets": { "net10.0-windows": ... }). StaleObjDetector.Inspect substring-Contains karşılaştırdığı
    // için ham kısa TFM ile de doğru eşleşir — uzun ".NETCoreApp,Version=vX.Y" biçimine ÇEVRİLMEZ.
    [Fact]
    public void modern_sdk_style_tfm_passes_through_unchanged() =>
        Assert.Equal("net10.0", TargetFrameworkMonikerDeriver.FromRaw(null, "net10.0"));

    [Fact]
    public void legacy_tfv_wins_when_both_are_somehow_present() =>
        Assert.Equal(".NETFramework,Version=v4.6", TargetFrameworkMonikerDeriver.FromRaw("v4.6", "net10.0"));

    [Fact]
    public void neither_present_returns_null() =>
        Assert.Null(TargetFrameworkMonikerDeriver.FromRaw(null, null));

    [Fact]
    public void blank_values_are_treated_as_absent() =>
        Assert.Null(TargetFrameworkMonikerDeriver.FromRaw("  ", ""));
}
