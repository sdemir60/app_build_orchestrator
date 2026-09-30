namespace BuildOrchestrator.App.Services.Updates;

/// <summary>[motor] Feed'in bulduğu, kurulu sürümden yeni bir paket: sürüm, indirme boyutu (bayt) ve paketin notu —
/// yayın script'inin <c>CHANGELOG.md</c>'den kestiği o sürümün bölümü (What's new ile aynı biçim).</summary>
public sealed record UpdateCandidate(string Version, long DownloadBytes, string NotesMarkdown);

/// <summary>
/// [motor] Güncelleme motorunun seam'i. Velopack bu arayüzün arkasındadır: testler fake kullanır, uygulamanın geri
/// kalanı Velopack tiplerini görmez.
///
/// <para><see cref="PendingRestart"/> = indirilmiş, kurulum bekleyen paket (önceki oturumda indirilip process
/// öldürülmüş olabilir). <see cref="ApplyOnExit"/> Update.exe'yi başlatır ve process'in çıkmasını bekletir
/// (<c>WaitExitThenApplyUpdates</c>, silent); <c>restart</c> false ise sessiz kurulum, uygulama yeniden açılmaz.</para>
/// </summary>
public interface IAppUpdater
{
    /// <summary>Uygulama Velopack ile kurulmuş bir kopya olarak mı çalışıyor — motor yalnız o zaman çalışır.</summary>
    bool IsInstalled { get; }

    /// <summary>İndirilmiş ve kurulum bekleyen paket; yoksa <c>null</c>.</summary>
    UpdateCandidate? PendingRestart { get; }

    /// <summary>Feed'i sorar; kurulu sürümden yeni bir paket varsa onu döner, yoksa <c>null</c>.</summary>
    Task<UpdateCandidate?> CheckAsync(CancellationToken ct);

    /// <summary>Paketi indirir; bittiğinde <see cref="PendingRestart"/> onu taşır.</summary>
    Task DownloadAsync(UpdateCandidate candidate, CancellationToken ct);

    /// <summary>Update.exe'yi başlatır; kurulum bu process çıktıktan sonra yapılır. <paramref name="restart"/> false
    /// ise uygulama kurulumdan sonra yeniden açılmaz.</summary>
    void ApplyOnExit(UpdateCandidate candidate, bool restart);
}
