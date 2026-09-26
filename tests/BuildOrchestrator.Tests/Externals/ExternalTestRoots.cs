namespace BuildOrchestrator.Tests.Externals;

/// <summary>
/// Bu klasördeki testlerin PAYLAŞTIĞI sabit — kopya YASAK, tek kaynak (<see
/// cref="ExternalWorkspaceResolverTests"/> ve <see cref="ExternalRevisionReaderTests"/> AYNI değeri iki kopya
/// olarak taşıyordu).
/// </summary>
internal static class ExternalTestRoots
{
    /// <summary>Ana kök/kart ilişkisi testin KONUSU olmadığında (tarama davranışı, revizyon okuma) kullanılan
    /// ana kök: hiçbir kart fixture'ıyla (<see cref="TempDir"/>/<see cref="Git.GitTestRepo"/> — ikisi de
    /// <see cref="System.IO.Path.GetTempPath"/> altında GUID'li) ÇAKIŞMAYAN sabit bir yol. Ana kök/kart
    /// KAPSAMA testleri (§10.4 — <see cref="ExternalWorkspaceResolverTests"/>'in "ana kökün içi reddedilir"
    /// bölümü) bunu KULLANMAZ, kendi ana köklerini AÇIKÇA verir.</summary>
    public const string UnrelatedMainRoot = @"D:\bo-tests-unrelated-main-root";
}
