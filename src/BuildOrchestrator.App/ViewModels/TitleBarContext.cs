namespace BuildOrchestrator.App.ViewModels;

/// <summary>
/// [design v1.11.0 §2.7-5a] Alt bardaki <b>workspace etiketinin</b> tek kaynağı — SAF, WPF'siz test edilir
/// (<see cref="InteractionText"/>/<see cref="RibbonText"/> deseni: karar burada, uygulama <c>ActionBar</c>'da).
///
/// <para><b>[DEĞİŞEN KURAL]</b> Tip başlangıçta title bar'ın mono BAĞLAM metnini kuruyordu
/// (<c>OSYS · main</c> + worktree eki + repo yokken <c>no repository</c>). design-v1.11.0 §2.1 o metni
/// kaldırdı — branch ve worktree bilgisi zaten alt bardaki chip'lerdeydi — ve geriye kalan tek yeni bilgiyi,
/// workspace adını, alt bara taşıdı. Geriye yalnız o adı üreten hesap kaldı; adı korundu çünkü tüketicisi
/// değişse de ürettiği şey aynıdır.</para>
/// </summary>
public static class TitleBarContext
{
    /// <summary>Repo kökünün klasör adı — alt bardaki mono workspace etiketi (prototipteki sabit <c>'OSYS'</c>
    /// literalinin gerçek karşılığı). Disk'e DOKUNMAZ (saf dize işlemi): <c>DirectoryInfo</c> geçersiz
    /// karakterlerde fırlatırdı ve bu karar bir görüntüleme kararıdır. Sondaki ayraç(lar) yok sayılır
    /// (<c>D:\Projects\OSYS\</c> → <c>OSYS</c>); ayraç hiç yoksa (sürücü kökü / göreli ad) dizenin kendisi döner.</summary>
    public static string RepositoryName(string rootPath)
    {
        if (string.IsNullOrEmpty(rootPath)) return "";
        string trimmed = rootPath.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        if (trimmed.Length == 0) return "";
        int slash = trimmed.LastIndexOfAny([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar]);
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }
}
