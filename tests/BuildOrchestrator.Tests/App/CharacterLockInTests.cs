using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [karakter kilitlenmesi] Event stream satır metninin açılış matematiği — SAF, WPF'siz.
///
/// <para><b>[DEĞİŞEN KURAL]</b> Satır metni artık daktiloyla (<c>TypewriterScheduler</c>) harf harf
/// EKLENMİYOR; bir okuma başı satırı soldan sağa KİLİTLİYOR. Değişme gerekçesi (kullanıcı): daktiloda metin
/// her karede uzuyor, satır ve mono ızgara akıyordu; kilitlenmede kuyruk ilk kareden itibaren ŞEFFAF basılır,
/// yani satırın genişliği hiç değişmez.</para>
/// </summary>
public class CharacterLockInTests
{
    /// <summary>Süre satır UZUNLUĞUNDAN BAĞIMSIZDIR: kısa da olsa uzun da olsa baş tam 15 tick'te sona varır.
    /// Sabit adım kullanılsaydı uzun satır uzun, kısa satır kısa açılır ve akış düzensiz okunurdu.</summary>
    [Theory]
    [InlineData(8)]
    [InlineData(40)]
    [InlineData(200)]
    public void The_read_head_crosses_any_line_in_the_same_number_of_ticks(int length)
    {
        var oneTickShort = TimeSpan.FromMilliseconds(CharacterLockIn.TickMs * (CharacterLockIn.Steps - 1));

        Assert.False(CharacterLockIn.IsDone(length, oneTickShort));
        Assert.True(CharacterLockIn.IsDone(length, CharacterLockIn.Duration));
        Assert.Equal(360, CharacterLockIn.Duration.TotalMilliseconds);
    }

    /// <summary>Üç dilim bitişiktir ve toplamı satırın TAMAMIDIR — kuyruk basılmadığında satır akardı.</summary>
    [Fact]
    public void The_three_slices_always_cover_the_whole_line()
    {
        const int length = 40;
        for (int tick = 0; tick <= CharacterLockIn.Steps; tick++)
        {
            var (locked, windowEnd) = CharacterLockIn.SliceAt(
                length, TimeSpan.FromMilliseconds(CharacterLockIn.TickMs * tick));

            Assert.InRange(locked, 0, length);
            Assert.InRange(windowEnd, locked, length);
            Assert.True(windowEnd - locked <= CharacterLockIn.WindowChars, "titreyen pencere 5 karakteri aşamaz");
        }
    }

    /// <summary>Pencere baştan sonra kadar 5 karakterdir; yalnız satırın sonunda daralır (taşma yok).</summary>
    [Fact]
    public void The_jitter_window_is_five_characters_until_the_line_runs_out()
    {
        var (_, windowEnd) = CharacterLockIn.SliceAt(40, TimeSpan.FromMilliseconds(CharacterLockIn.TickMs));
        var (lockedAtStart, windowAtStart) = CharacterLockIn.SliceAt(40, TimeSpan.Zero);

        Assert.Equal(CharacterLockIn.WindowChars, windowAtStart - lockedAtStart);
        Assert.True(windowEnd > 0);

        // Sonda: baş sona vardı → titreyecek bir şey kalmadı.
        var (locked, end) = CharacterLockIn.SliceAt(40, CharacterLockIn.Duration);
        Assert.Equal(40, locked);
        Assert.Equal(40, end);
    }

    /// <summary>
    /// <b>Rakam içeren token'lar BÜTÜN olarak muaftır; noktalama hiç titremez.</b> Rakamlar tabular hizalıdır —
    /// rastgele bir glyph onları ızgarada zıplatır, üstelik bir sha ya da süre "bozulmuş veri" gibi okunurdu.
    /// </summary>
    [Fact]
    public void Only_letters_jitter_and_any_token_holding_a_digit_is_exempt()
    {
        const string text = "OSYS.Sales built (1.4s) a3f81c2 14/38 09:41:02";
        var mask = CharacterLockIn.ScrambleMask(text);

        Assert.All(Indices(text, "1.4s"), i => Assert.False(mask[i]));
        Assert.All(Indices(text, "a3f81c2"), i => Assert.False(mask[i]));
        Assert.All(Indices(text, "14/38"), i => Assert.False(mask[i]));
        Assert.All(Indices(text, "09:41:02"), i => Assert.False(mask[i]));

        // Rakamsız token: harfler titrer, noktalama titremez.
        int dot = text.IndexOf('.');
        Assert.False(mask[dot]);
        Assert.True(mask[text.IndexOf("built", StringComparison.Ordinal)]);
        Assert.True(mask[0]); // "OSYS"in ilk harfi
    }

    /// <summary>Titreme yalnız muaf OLMAYAN karakterleri değiştirir; gerisi gerçek karakteriyle basılır.</summary>
    [Fact]
    public void Scramble_replaces_only_the_jittering_characters()
    {
        const string text = "built 1.4s";
        var mask = CharacterLockIn.ScrambleMask(text);

        string window = CharacterLockIn.Scramble(text, mask, 0, text.Length, () => 0);

        Assert.Equal(text.Length, window.Length);
        Assert.Equal(CharacterLockIn.Glyphs[0], window[0]);       // 'b' titredi
        Assert.Equal(' ', window[5]);                              // boşluk aynen
        Assert.Equal("1.4s", window[6..]);                         // rakamlı token aynen
    }

    private static IEnumerable<int> Indices(string text, string token)
    {
        int start = text.IndexOf(token, StringComparison.Ordinal);
        Assert.True(start >= 0, $"token bulunamadı: {token}");
        return Enumerable.Range(start, token.Length);
    }
}
