using BuildOrchestrator.Contracts.Ipc;
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

    // ---------------------------------------------------------------- [RESOLVE Faz 4 / karar 11] koşu dönüşümü

    /// <summary>Resolve cycles + anahtar açık: cap YOK, priority Normal, işçi sayısı profilden (kullanıcı onayı). Beklenen
    /// değerler LİTERAL anlamdır (null / Normal) — dönüşümün kendisinden türetilmez.</summary>
    [Theory]
    [InlineData(PerfMode.Balanced)]
    [InlineData(PerfMode.Light)]
    public void A_resolve_cycles_run_drops_the_cap_runs_at_normal_priority_and_keeps_the_profile_workers(PerfMode mode)
    {
        var profile = PerfProfile.For(mode);

        var run = PerfProfile.ForRun(RunMode.Cycles, profile, resolveAtFullPriority: true);

        Assert.Null(run.CpuCapPercent);
        Assert.Equal(ProcessPriorityClassKind.Normal, run.Priority);
        Assert.Equal(profile.Parallelism, run.Parallelism);
    }

    /// <summary>Anahtar kapalıyken Resolve bugünkü gibi profilin önceliği/tavanıyla koşar.</summary>
    [Theory]
    [InlineData(PerfMode.Full)]
    [InlineData(PerfMode.Balanced)]
    [InlineData(PerfMode.Light)]
    public void A_resolve_cycles_run_follows_the_profile_when_the_switch_is_off(PerfMode mode)
        => Assert.Equal(PerfProfile.For(mode),
            PerfProfile.ForRun(RunMode.Cycles, PerfProfile.For(mode), resolveAtFullPriority: false));

    /// <summary>Build/Rebuild/Clean HİÇ etkilenmez — anahtar açık olsa bile.</summary>
    [Theory]
    [InlineData(RunMode.Build, PerfMode.Balanced)]
    [InlineData(RunMode.Build, PerfMode.Light)]
    [InlineData(RunMode.Rebuild, PerfMode.Balanced)]
    [InlineData(RunMode.Rebuild, PerfMode.Light)]
    [InlineData(RunMode.Clean, PerfMode.Balanced)]
    [InlineData(RunMode.Clean, PerfMode.Light)]
    public void Build_rebuild_and_clean_always_follow_the_profile(RunMode runMode, PerfMode mode)
        => Assert.Equal(PerfProfile.For(mode),
            PerfProfile.ForRun(runMode, PerfProfile.For(mode), resolveAtFullPriority: true));

    /// <summary>Full zaten cap'siz + Normal: dönüşüm onu değiştirmez.</summary>
    [Fact]
    public void Full_is_unchanged_by_the_resolve_transform()
        => Assert.Equal(PerfProfile.For(PerfMode.Full),
            PerfProfile.ForRun(RunMode.Cycles, PerfProfile.For(PerfMode.Full), resolveAtFullPriority: true));

    /// <summary>Konsol notu chip notunun ailesindedir: priority ve koşu adı eklenmiş tek satır (KOPYA METİN bilerek
    /// literal). Dönüşüm profili değiştirmediyse not profilin kendi notudur ve run-başı satırı (ResolveNote) yoktur.</summary>
    [Fact]
    public void The_resolve_note_names_the_lifted_priority_and_other_runs_keep_the_profile_note()
    {
        var balanced = PerfProfile.For(PerfMode.Balanced);

        Assert.Equal("parallelism: 4 · cpu cap off · priority normal (Resolve cycles)",
            PerfNoteText.Note(RunMode.Cycles, balanced, resolveAtFullPriority: true));
        Assert.Equal("parallelism: 4 · cpu cap off · priority normal (Resolve cycles)",
            PerfNoteText.ResolveNote(RunMode.Cycles, balanced, resolveAtFullPriority: true));
        Assert.Equal(PerfNoteText.Note(balanced), PerfNoteText.Note(RunMode.Build, balanced, resolveAtFullPriority: true));
        Assert.Equal(PerfNoteText.Note(balanced), PerfNoteText.Note(RunMode.Cycles, balanced, resolveAtFullPriority: false));
        Assert.Null(PerfNoteText.ResolveNote(RunMode.Cycles, PerfProfile.For(PerfMode.Full), resolveAtFullPriority: true));
        Assert.Null(PerfNoteText.ResolveNote(RunMode.Build, balanced, resolveAtFullPriority: true));
    }

    /// <summary>[RESOLVE Faz 4 · fix 1B — M6] Priority'nin değer terimi her sınıf için AÇIKÇA yazılıdır (KOPYA METİN bilerek
    /// literal); tanımsız bir değer — ileride eklenen bir enum üyesi — sessizce <c>"idle"</c> diye etiketlenmez, fırlatır.</summary>
    [Fact]
    public void The_priority_value_names_every_class_and_rejects_an_unknown_one()
    {
        Assert.Equal("normal", PerfNoteText.PriorityValue(ProcessPriorityClassKind.Normal));
        Assert.Equal("below normal", PerfNoteText.PriorityValue(ProcessPriorityClassKind.BelowNormal));
        Assert.Equal("idle", PerfNoteText.PriorityValue(ProcessPriorityClassKind.Idle));
        Assert.Throws<ArgumentOutOfRangeException>(() => PerfNoteText.PriorityValue((ProcessPriorityClassKind)99));
    }
}
