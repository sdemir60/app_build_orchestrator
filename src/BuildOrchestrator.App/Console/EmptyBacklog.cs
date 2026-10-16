namespace BuildOrchestrator.App.Console;

/// <summary>
/// [idle-memory · dalga 3 · B2 fix 1 · O2] Arkasında hiçbir şey olmayan backlog: satırı yok, her aralık <c>""</c>. Konsol görünümünün iki
/// yer tutucusudur: anlatı bağlanmadan önceki görünüm (kabuk açılışta store'u bağlar — <c>ConsoleView.BindNarrative</c>) ve proje sayfası
/// (logun tamamı belgededir ve sayfa hiç izlenmediği için kırpılmaz — <c>ConsoleView.ReplaceProjectDocument</c>).
/// <para>[DEĞİŞEN KURAL] İki yer tutucu eskiden boş bir görünüm listesiydi (<c>ListBacklog</c>): kırpılan satırları saklayan bir liste
/// gibi davranıyor, arkasında bir şey olmayan belge için "kopya tutuyorum" diyordu.</para>
/// </summary>
public sealed class EmptyBacklog : IConsoleBacklog
{
    /// <summary>Tek örnek — durum taşımaz.</summary>
    public static EmptyBacklog Instance { get; } = new();

    private EmptyBacklog() { }

    public int Count => 0;

    public string Join(int from, int to) => "";
}
