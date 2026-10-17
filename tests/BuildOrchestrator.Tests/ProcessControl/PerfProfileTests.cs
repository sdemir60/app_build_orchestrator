using BuildOrchestrator.Core.ProcessControl;
using Xunit;

namespace BuildOrchestrator.Tests.ProcessControl;

/// <summary>
/// [T20-a] K11 perf tablosu: Full(6, cap yok, Normal) · Balanced(4, %70, BelowNormal) · Light(2, %40, Idle).
/// <see cref="PerfProfile"/> SAF'tır — Win32/WPF türü taşımaz, priority kendi enum'uyla
/// (<see cref="ProcessPriorityClassKind"/>) ifade edilir; bu sınıf da saftır (App/Win32 bağımlılığı YOK).
/// <para>Buradaki assert'ler yalnız ENUM değerini pinler; enum→Win32 sabit çevirisi
/// <c>JobCpuRateTests.Priority_write_maps_to_the_win32_class_...</c>'ta, App ile paralellik eşitliği ise
/// <c>App/PerfProfileParityTests</c>'te doğrulanır.</para>
/// </summary>
[Trait("Category", "ProcessControl")]
public class PerfProfileTests
{
    [Fact]
    public void Perf_table_matches_the_K11_decision_for_all_three_modes()
    {
        Assert.Equal(new PerfProfile(6, null, ProcessPriorityClassKind.Normal), PerfProfile.For(PerfMode.Full));
        Assert.Equal(new PerfProfile(4, 70, ProcessPriorityClassKind.BelowNormal), PerfProfile.For(PerfMode.Balanced));
        Assert.Equal(new PerfProfile(2, 40, ProcessPriorityClassKind.Idle), PerfProfile.For(PerfMode.Light));
    }

    /// <summary>
    /// [T20-b/P3] Copy fazı tabanının DEĞERİ pinlenir — türetme zinciri (<c>For(Balanced)</c>) DEĞİL. Gerekçe
    /// K11 kopya pini ile aynıdır: türetmeyi doğrulamak totolojidir, oysa burada korunan şey "sıkışan copy'yi
    /// açmak için job'ı ne kadar gevşetiyoruz" KARARIdır. Tablo değişirse bu assert bilerek kırılır ve karar
    /// yeniden gözden geçirilir.
    /// </summary>
    [Fact]
    public void Copy_phase_floor_is_70_percent_and_below_normal()
    {
        Assert.Equal(70, PerfProfile.CopyPhaseFloorPercent);
        Assert.Equal(ProcessPriorityClassKind.BelowNormal, PerfProfile.CopyPhaseFloorPriority);
    }

    [Theory]
    [InlineData("Full")]
    [InlineData("Balanced")]
    [InlineData("Light")]
    public void Try_parse_resolves_the_three_perf_mode_strings_the_app_uses(string text)
        => Assert.Equal(PerfProfile.For(Enum.Parse<PerfMode>(text)), PerfProfile.TryParse(text));

    [Theory]
    [InlineData("full")]   // eşleşme ordinal — App'in yazdığı string birebir gelir
    [InlineData("")]
    [InlineData("Turbo")]
    public void Try_parse_returns_null_for_an_unknown_perf_mode_text(string text)
        => Assert.Null(PerfProfile.TryParse(text));

    /// <summary>
    /// [kaynak şeridi] Metinden moda eşlemenin TEK tablosu <see cref="PerfProfile.TryParseMode"/>'dur (ordinal): şeridin metinleri modu
    /// enum olarak ister, App ise modu <c>"Balanced"</c> gibi metin olarak taşır — köprü ikinci bir "Full/Balanced/Light" tablosu yazmaz.
    /// </summary>
    [Theory]
    [InlineData("Full", PerfMode.Full)]
    [InlineData("Balanced", PerfMode.Balanced)]
    [InlineData("Light", PerfMode.Light)]
    public void Try_parse_mode_resolves_the_three_perf_mode_strings_to_their_modes(string text, PerfMode expected)
        => Assert.Equal(expected, PerfProfile.TryParseMode(text));

    [Theory]
    [InlineData("full")]   // eşleşme ordinal — App'in yazdığı string birebir gelir
    [InlineData("")]
    [InlineData("Turbo")]
    [InlineData(" Full")]
    public void Try_parse_mode_returns_null_for_an_unknown_perf_mode_text(string text)
        => Assert.Null(PerfProfile.TryParseMode(text));

    /// <summary>Şeridin gösterdiği mod sözcüğü (<c>mode.ToString()</c>) ile tel metni AYNI sözcüktür: hiçbir enum üyesi tablosuz kalmaz,
    /// tablo da enum'da olmayan bir ad taşımaz.</summary>
    [Fact]
    public void Every_perf_mode_name_is_a_perf_mode_text_that_parses_back_to_the_mode()
        => Assert.All(Enum.GetValues<PerfMode>(), mode => Assert.Equal(mode, PerfProfile.TryParseMode(mode.ToString())));

    // ---------------------------------------------------------------- chip notu: profilin tek biçimi

    /// <summary>
    /// Koşu içi chip notu (<see cref="PerfNoteText.Note(PerfProfile)"/>) her mod için profilin kendi notudur: işçi sayısı ve cap
    /// (<c>parallelism: 4 · cpu cap 70%</c>), cap'siz Full'de <c>cpu cap off</c>. Koşu moduna göre değişen bir biçim ve Resolve cycles'a
    /// özgü bir metin (priority terimi, koşu adı) yoktur. Metinler KOPYA METİN olduğundan bilerek literaldir — türetilseydi test kendini
    /// doğrulardı.
    /// <para><b>[DEĞİŞEN KURAL — 2026-10-17]</b> Eski iddia (<c>A_resolve_cycles_run_drops_the_cap_runs_at_normal_priority_and_keeps_the_profile_workers</c>,
    /// <c>A_resolve_cycles_run_follows_the_profile_when_the_switch_is_off</c>, <c>Build_rebuild_and_clean_always_follow_the_profile</c>,
    /// <c>Full_is_unchanged_by_the_resolve_transform</c>, <c>The_resolve_note_names_the_lifted_priority_and_other_runs_keep_the_profile_note</c>,
    /// <c>The_priority_value_names_every_class_and_rejects_an_unknown_one</c>): Core'daki bir koşu dönüşümü (<c>PerfProfile.ForRun</c>), Resolve
    /// cycles'ta ve Ayarlar'ın "Resolve cycles at full priority" anahtarı açıkken, profilin cap'ini ve priority'sini Full'ün satırından alırdı
    /// (işçi sayısı profilden); not (<c>PerfNoteText.ResolveNote</c>) bunu <c>parallelism: 4 · cpu cap off · priority normal (Resolve cycles)</c>
    /// diye anlatırdı ve priority'nin değer terimi (<c>PerfNoteText.PriorityValue</c>) her sınıf için açıkça yazılıydı. Dönüşüm, not, terim
    /// sözlüğü ve anahtar kalktı: profilin tablosu koşu modundan bağımsız aynen uygulanır, not tek biçimdir (gerekçe ve ölçüm:
    /// <see cref="BuildOrchestrator.Tests.App.ResolveFollowsProfileSettingsTests"/>). Terim sözlüğünü pinleyen test, sözlük kalktığı için
    /// kalktı; Core'da priority'yi anlatan başka bir metin kalmadı.</para>
    /// </summary>
    [Theory]
    [InlineData(PerfMode.Full, "parallelism: 6 · cpu cap off")]
    [InlineData(PerfMode.Balanced, "parallelism: 4 · cpu cap 70%")]
    [InlineData(PerfMode.Light, "parallelism: 2 · cpu cap 40%")]
    public void The_perf_note_is_the_profiles_own_note_for_every_mode(PerfMode mode, string expected)
    {
        string note = PerfNoteText.Note(PerfProfile.For(mode));

        Assert.Equal(expected, note);
        Assert.DoesNotContain("priority", note, StringComparison.Ordinal);
    }
}
