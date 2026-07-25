namespace BuildOrchestrator.App.Console;

/// <summary>
/// [D4 review §1/§3] Pump flush'ının SAF yönlendirme kararı — birim-test edilebilir seam. <c>MainWindow</c> DI
/// olmadan kurulamadığından (RestoreGlyphTests.cs:106) bu karar Window'dan çıkarılıp burada test edilir; Window
/// yalnız kararı UYGULAR (ilgili <c>ConsoleView</c> metodunu çağırır).
///
/// <para>Üç sonuç:
/// <list type="bullet">
/// <item><see cref="Route.Drop"/> — batch, aradan geçen bir reseed'den ÖNCEki nesle ait (bayat) → at (§1
/// generation guard: taze dokümana sızmasın).</item>
/// <item><see cref="Route.Narrative"/> — anlatı modu (<c>ActiveProjectId</c> null): en yeni satır T34 hibrit
/// daktilo kurallarıyla.</item>
/// <item><see cref="Route.Raw"/> — proje-log modu: ham MSBuild instant (ham çıktı ASLA harf-harf — DD2).</item>
/// </list></para>
/// </summary>
public static class ConsoleBatchRouter
{
    public enum Route { Drop, Narrative, Raw }

    /// <summary>[§1 generation guard] <paramref name="batchGen"/>, pump'ın bu batch'i OKUDUĞU reseed nesli.
    /// Aradan bir reseed geçtiyse (<paramref name="currentReseedGen"/> ilerlemiş) batch bayattır → <see cref="Route.Drop"/>.
    /// Aksi halde <paramref name="activeProjectId"/>'ye göre anlatı (null) / ham (proje-log).</summary>
    public static Route Decide(long batchGen, long currentReseedGen, string? activeProjectId)
    {
        if (batchGen < currentReseedGen) return Route.Drop; // aradan reseed geçti → bayat batch
        return activeProjectId is null ? Route.Narrative : Route.Raw;
    }
}
