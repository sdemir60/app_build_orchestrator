namespace BuildOrchestrator.Core.Git;

/// <summary>[Faz 2/T1] HEAD reflog'unun son satırının sınıflandırılmış anlamı — bkz. <see cref="ReflogEntry"/>.</summary>
public enum HeadMove
{
    Commit,
    BranchSwitch,
    Other,
}

/// <summary>[Faz 2/T7] <see cref="HeadMove"/>'lar arasındaki güç sırası — TEK kaynak. Aynı pencerede birden çok
/// hareket birikirse (izleyicinin sessizlik penceresi, koordinatörün bekleyen tetiği) en güçlüsü kalır.</summary>
public static class HeadMoveRules
{
    /// <summary>Commit en zayıftır (altındaki dünya değişmez; koşuyu durdurmaz); branch değişimi ve diğer
    /// hareketler (pull, reset, rebase, merge) eşit ve daha güçlüdür — ikisi de dünyayı değiştirebilir.</summary>
    public static int Weight(this HeadMove move) => move == HeadMove.Commit ? 1 : 2;

    /// <summary>İkisinden güçlüsü; eşitse <paramref name="later"/> (en son görülen) kalır.</summary>
    public static HeadMove Stronger(HeadMove earlier, HeadMove later) =>
        earlier.Weight() > later.Weight() ? earlier : later;
}

/// <summary>
/// [Faz 2/T1] HEAD reflog'unun (<c>.git/logs/HEAD</c>) SON satırını sınıflandırır — saf metin ayrıştırması,
/// process YOK. Satır git'in ham reflog biçimidir (<c>&lt;eski sha&gt; &lt;yeni sha&gt; &lt;yazar&gt;
/// &lt;zaman&gt;\t&lt;mesaj&gt;</c>); yalnız TAB'den sonraki mesaj kısmı okunur. Mesajlar git'in kendi
/// İngilizce reflog ön ekleridir — lokalize DEĞİLDİR, git locale'i ne olursa olsun aynı kalır.
/// </summary>
public static class ReflogEntry
{
    private const string CommitPrefix = "commit";
    private const string CheckoutMovingFromPrefix = "checkout: moving from ";
    private const string ToSeparator = " to ";

    public static HeadMove Classify(string lastLine)
    {
        if (string.IsNullOrEmpty(lastLine)) return HeadMove.Other;

        int tab = lastLine.IndexOf('\t');
        if (tab < 0) return HeadMove.Other; // bozuk satır — TAB yok

        string message = lastLine[(tab + 1)..];

        if (message.StartsWith(CommitPrefix, StringComparison.Ordinal)) return HeadMove.Commit;

        if (message.StartsWith(CheckoutMovingFromPrefix, StringComparison.Ordinal))
        {
            string rest = message[CheckoutMovingFromPrefix.Length..];
            int toIndex = rest.IndexOf(ToSeparator, StringComparison.Ordinal);
            if (toIndex >= 0)
            {
                string from = rest[..toIndex];
                string to = rest[(toIndex + ToSeparator.Length)..].Trim();
                if (!string.Equals(from, to, StringComparison.Ordinal)) return HeadMove.BranchSwitch;
            }
        }

        return HeadMove.Other;
    }
}
