namespace BuildOrchestrator.App.Console;

/// <summary>
/// [karakter kilitlenmesi] Event stream satır metninin SAF (WPF'siz) açılış matematiği — daktilonun yerini alır.
///
/// <para><b>Jest:</b> bir okuma başı satırın solundan sağına ilerler. Başın SOLU gerçek metindir (kilitlendi),
/// başın önündeki <see cref="WindowChars"/> karakterlik pencere her tick'te rastgele mono glyph'lerle titrer,
/// pencerenin sağındaki kuyruk ŞEFFAF basılır.</para>
///
/// <para><b>Kuyruk neden şeffaf, neden boş değil:</b> satırın genişliği ilk kareden itibaren SABİT kalsın diye.
/// Kuyruk basılmasaydı metin her tick'te uzar, satır (ve mono ızgara) akar/reflow olurdu — daktilonun en
/// rahatsız edici yanı buydu.</para>
///
/// <para><b>Süre satır uzunluğundan BAĞIMSIZDIR:</b> adım <c>uzunluk / <see cref="Steps"/></c>'tir, yani okuma
/// başı her satırı tam <see cref="Steps"/> tick'te kat eder ve jest her zaman
/// <see cref="Duration"/> sürer. Sabit adım kullanılsaydı uzun satır uzun, kısa satır kısa açılırdı; akış
/// düzensiz okunurdu.</para>
///
/// <para><b>Yalnız harfler titrer.</b> İçinde RAKAM geçen token'lar (<c>1.4s</c>, <c>a3f81c2</c>, <c>14/38</c>,
/// <c>09:41:02</c>) bütün olarak muaftır ve noktalama hiç titremez: rakamlar tabular hizalıdır ve rastgele bir
/// glyph onları ızgarada zıplatırdı, üstelik bir sha ya da süre "bozulmuş veri" gibi okunurdu.</para>
/// </summary>
internal static class CharacterLockIn
{
    /// <summary>Kare bütçesi — 24 ms ≈ 41 fps. Adlandırılmış sabit (çağrı yerinde literal süre YASAK).</summary>
    internal const double TickMs = 24.0;

    /// <summary>Okuma başının satırı kat ettiği tick sayısı. Süre buradan türer, satır uzunluğundan değil.</summary>
    internal const int Steps = 15;

    /// <summary>Başın önünde titreyen pencere (karakter).</summary>
    internal const int WindowChars = 5;

    /// <summary>Titreme glyph'leri — mono ızgarada tek hücre kaplayan, "veri henüz oturmadı" okunan işaretler.</summary>
    internal const string Glyphs = "#$%&*+=<>";

    /// <summary>Jestin toplam süresi (~360 ms).</summary>
    internal static TimeSpan Duration => TimeSpan.FromMilliseconds(TickMs * Steps);

    /// <summary>Verilen ana ait dilim: <c>[0,Locked)</c> gerçek metin, <c>[Locked,WindowEnd)</c> titreyen
    /// pencere, <c>[WindowEnd,length)</c> şeffaf kuyruk.</summary>
    internal static (int Locked, int WindowEnd) SliceAt(int length, TimeSpan elapsed)
    {
        if (length <= 0) return (0, 0);
        int ticks = (int)(elapsed.TotalMilliseconds / TickMs);
        // Kesirli adım: baş her satırı TAM Steps tick'te kat eder (uzunluktan bağımsız süre).
        int locked = (int)Math.Round(Math.Min(Steps, Math.Max(0, ticks)) * (double)length / Steps,
            MidpointRounding.AwayFromZero);
        locked = Math.Clamp(locked, 0, length);
        return (locked, Math.Min(length, locked + WindowChars));
    }

    /// <summary>Baş satırın sonuna vardı mı — jest biter, satır tam gerçek metnidir.</summary>
    internal static bool IsDone(int length, TimeSpan elapsed) => SliceAt(length, elapsed).Locked >= length;

    /// <summary>
    /// Hangi karakterler titreyebilir. Boşlukla ayrılmış token'lar tek tek değerlendirilir: token içinde bir
    /// RAKAM varsa o token'ın TAMAMI muaftır (süre/sha/oran/saat bozulmaz); aksi halde yalnız HARFLER titrer,
    /// noktalama olduğu gibi basılır.
    /// </summary>
    internal static bool[] ScrambleMask(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var mask = new bool[text.Length];
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i])) { i++; continue; }
            int start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            bool hasDigit = false;
            for (int k = start; k < i; k++)
                if (char.IsDigit(text[k])) { hasDigit = true; break; }
            if (hasDigit) continue; // token bütün olarak muaf
            for (int k = start; k < i; k++) mask[k] = char.IsLetter(text[k]);
        }
        return mask;
    }

    /// <summary>Titreyen pencerenin O ANKİ görüntüsü. Muaf karakterler gerçek hâlleriyle basılır; titreyenler
    /// <paramref name="pick"/>'in seçtiği glyph'le. Rastgelelik ÇAĞIRANDAN gelir — bu sınıf saf kalır ve test
    /// sabit bir seçiciyle sürülebilir (D8).</summary>
    internal static string Scramble(string text, bool[] mask, int from, int toExclusive, Func<int> pick)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(mask);
        ArgumentNullException.ThrowIfNull(pick);
        if (toExclusive <= from) return "";
        var buffer = new char[toExclusive - from];
        for (int k = from; k < toExclusive; k++)
            buffer[k - from] = mask[k] ? Glyphs[Math.Abs(pick()) % Glyphs.Length] : text[k];
        return new string(buffer);
    }
}
