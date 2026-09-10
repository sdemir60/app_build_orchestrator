namespace BuildOrchestrator.Core.Git;

/// <summary>
/// Kullanıcıya gösterilen bir REVİZYON KİMLİĞİNİN kısa biçimi — konsol satırları, proje logu başlığı ve
/// App'in okuduğu her yer buradan geçer (kopya YASAK, CLAUDE.md).
///
/// <para><b>Kısaltma yalnız git sha'sına uygulanır</b> (40 hex → 7 hane). Başka her değer olduğu gibi kalır:
/// bir TFVC changeset'i (<c>C48213</c>) kırpılırsa anlamsız bir sayıya döner — 7 hane bir git alışkanlığıdır,
/// evrensel bir biçim değil.</para>
/// </summary>
public static class RevisionText
{
    /// <summary>Git sha'sının ilk 7 hanesi; git sha'sı olmayan değer aynen döner. Boş/null → boş string.</summary>
    public static string Short(string? revision)
    {
        if (string.IsNullOrEmpty(revision)) return string.Empty;

        return revision.Length == 40 && revision.All(Uri.IsHexDigit) ? revision[..7] : revision;
    }
}
