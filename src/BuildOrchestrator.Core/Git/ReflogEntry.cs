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

    /// <summary>Bir penceredeki hareketlerin en güçlüsü — <see cref="Stronger"/>'ın eskiden yeniye katlanmışı. İzleyicinin iki
    /// kipi (reflog dosyası, reftable) aynı kuralı buradan alır. Liste boş olamaz.</summary>
    public static HeadMove Strongest(IEnumerable<HeadMove> oldestToNewest) => oldestToNewest.Aggregate(Stronger);
}

/// <summary>[reftable] Git'ten okunan bir HEAD reflog kaydı (<see cref="GitService.GetHeadReflogAsync"/>).</summary>
/// <param name="Key">Kaydın kimliği: yeni commit + kaydın zamanı (saniye) + mesaj. İzleyici "son gördüğüm kayıttan sonra ne
/// eklendi" sorusunu bununla cevaplar.</param>
/// <param name="Message">Reflog mesajı (<see cref="ReflogEntry.ClassifyMessage"/>'ın girdisi).</param>
public sealed record ReflogRecord(string Key, string Message);

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

        return ClassifyMessage(lastLine[(tab + 1)..]);
    }

    /// <summary>[reftable] Reflog satırının yalnız mesajı (TAB'den sonrası) — sınıflandırmanın TEK kuralı. reftable repoda
    /// reflog dosya değildir; izleyici kayıtları git'ten alır (<see cref="GitService.GetHeadReflogAsync"/>) ve mesajı
    /// doğrudan buraya verir.</summary>
    public static HeadMove ClassifyMessage(string message)
    {
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
