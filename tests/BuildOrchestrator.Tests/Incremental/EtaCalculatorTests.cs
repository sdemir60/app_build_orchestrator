using System;
using System.Collections.Generic;
using BuildOrchestrator.Core.Incremental;
using Xunit;

namespace BuildOrchestrator.Tests.Incremental;

// [T70][A6/Δ8] EtaCalculator: "kalan süre" tahmini — BAĞLAYICI formül (bkz. task-11-brief.md / v7 A6/Δ8):
//   raw = (queued tahminleri toplamı + building kalanları) / parallelism  +  (building varsa) 400ms
//   smoothed = previous == null ? raw : 0.75*previous + 0.25*raw
//   display: <4000ms -> "· almost done"; aksi halde en yakın 5s'e yuvarlanmış ham saniye "~Ns left" (design-v1
//   prototype BuildApp.jsx:761-763 birebir, ASLA "~Nm SSs left" dakika formatına çevrilmez)
//   fallback: hiçbir yerde bilinen LastDurationMs yoksa ComputeRawEstimateMs -> null (ETA numarası YOK,
//             yalnız X/N + elapsed gösterilir); KISMİ bilgi varsa bilinmeyenler ortalamayla temsil edilir.
public class EtaCalculatorTests
{
    // ---- Base formula (hand-computed) --------------------------------------------------------

    [Fact]
    public void raw_estimate_sums_queued_and_building_remaining_divides_by_parallelism_and_adds_building_overhead()
    {
        // queued: 10s + 20s bilinen süre; building: LastDurationMs=15s, elapsed=5s -> remaining=10s
        // (30000+10000)/2 = 20000; +400 (building var) = 20400
        var queued = new long?[] { 10_000, 20_000 };
        var building = new[] { new EtaCalculator.BuildingProject(ElapsedMs: 5_000, LastDurationMs: 15_000) };

        long? raw = EtaCalculator.ComputeRawEstimateMs(queued, building, parallelism: 2);

        Assert.Equal(20_400, raw);
    }

    [Fact]
    public void raw_estimate_has_no_400ms_overhead_when_nothing_is_building()
    {
        // queued: 10s only, hiç building yok -> (10000)/1 = 10000, +0 (building yok)
        var queued = new long?[] { 10_000 };
        var building = Array.Empty<EtaCalculator.BuildingProject>();

        long? raw = EtaCalculator.ComputeRawEstimateMs(queued, building, parallelism: 1);

        Assert.Equal(10_000, raw);
    }

    [Fact]
    public void building_remaining_never_goes_negative_when_elapsed_exceeds_estimate()
    {
        // building projesi tahmininden UZUN sürüyor (elapsed > LastDurationMs) -> remaining clamp 0
        var queued = Array.Empty<long?>();
        var building = new[] { new EtaCalculator.BuildingProject(ElapsedMs: 20_000, LastDurationMs: 5_000) };

        long? raw = EtaCalculator.ComputeRawEstimateMs(queued, building, parallelism: 1);

        // (0 + 0)/1 + 400 = 400
        Assert.Equal(400, raw);
    }

    // ---- EMA smoothing (two-step sequence) ---------------------------------------------------

    [Fact]
    public void first_tick_with_no_previous_eta_returns_the_raw_estimate_unsmoothed()
    {
        long smoothed = EtaCalculator.Smooth(previousEtaMs: null, rawEstimateMs: 2_000);
        Assert.Equal(2_000, smoothed);
    }

    [Fact]
    public void subsequent_ticks_apply_075_previous_plus_025_raw_two_step_sequence()
    {
        // tick 1 (ilk): previous=null -> raw aynen (2000)
        long tick1 = EtaCalculator.Smooth(previousEtaMs: null, rawEstimateMs: 2_000);
        Assert.Equal(2_000, tick1);

        // tick 2: previous=2000, raw=4000 -> 0.75*2000 + 0.25*4000 = 1500+1000 = 2500
        long tick2 = EtaCalculator.Smooth(previousEtaMs: tick1, rawEstimateMs: 4_000);
        Assert.Equal(2_500, tick2);

        // tick 3: previous=2500, raw=500 -> 0.75*2500 + 0.25*500 = 1875+125 = 2000
        long tick3 = EtaCalculator.Smooth(previousEtaMs: tick2, rawEstimateMs: 500);
        Assert.Equal(2_000, tick3);
    }

    // ---- Display: rounding to nearest 5s + almost-done threshold --------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(3_999)]
    public void smoothed_eta_under_4000ms_displays_almost_done_with_no_numeric(long smoothedEtaMs)
    {
        string display = EtaCalculator.FormatDisplay(smoothedEtaMs, completedCount: 3, totalCount: 5, elapsedMs: 9_000);
        Assert.Equal("· almost done", display);
    }

    [Fact]
    public void smoothed_eta_at_4000ms_boundary_is_not_almost_done()
    {
        // 4000ms >= threshold -> numeric path; round(4000/5000)=round(0.8)=1 -> 5s
        string display = EtaCalculator.FormatDisplay(4_000, completedCount: 1, totalCount: 5, elapsedMs: 1_000);
        Assert.Equal("~5s left", display);
    }

    [Fact]
    public void smoothed_eta_rounds_to_nearest_5_seconds_sub_minute()
    {
        // 35000ms -> round(35000/5000)*5 = 35s -> "~35s left" (design-v1 prototype example, birebir)
        string display = EtaCalculator.FormatDisplay(35_000, completedCount: 2, totalCount: 5, elapsedMs: 12_000);
        Assert.Equal("~35s left", display);
    }

    [Fact]
    public void smoothed_eta_over_a_minute_stays_in_raw_seconds_no_minutes_conversion()
    {
        // 125000ms -> round(125000/5000)*5 = 125s -> "~125s left" (design-v1 prototype BuildApp.jsx:761-763
        // never switches to a minutes format, it always shows raw seconds — NOT "~2m 05s left")
        string display = EtaCalculator.FormatDisplay(125_000, completedCount: 4, totalCount: 20, elapsedMs: 60_000);
        Assert.Equal("~125s left", display);
    }

    // ---- First-run fallback: no LastDurationMs anywhere -> no ETA number ----------------------

    [Fact]
    public void compute_raw_estimate_returns_null_when_no_duration_is_known_anywhere()
    {
        var queued = new long?[] { null, null };
        var building = new[] { new EtaCalculator.BuildingProject(ElapsedMs: 2_000, LastDurationMs: null) };

        long? raw = EtaCalculator.ComputeRawEstimateMs(queued, building, parallelism: 3);

        Assert.Null(raw);
    }

    [Fact]
    public void format_display_with_null_smoothed_eta_shows_progress_and_elapsed_with_no_eta_number()
    {
        string display = EtaCalculator.FormatDisplay(smoothedEtaMs: null, completedCount: 2, totalCount: 5, elapsedMs: 12_000);
        Assert.Equal("2/5 · 12s", display);
    }

    // ---- Partial history: some known, some unknown -> fallback average for unknowns -----------

    [Fact]
    public void unknown_queued_duration_falls_back_to_the_average_of_known_durations()
    {
        // queued: 10000 (bilinen), null (bilinmeyen) -> ortalama(10000)=10000 kullanılır
        // (10000 + 10000)/1 = 20000, building yok -> +0
        var queued = new long?[] { 10_000, null };
        var building = Array.Empty<EtaCalculator.BuildingProject>();

        long? raw = EtaCalculator.ComputeRawEstimateMs(queued, building, parallelism: 1);

        Assert.Equal(20_000, raw);
    }

    [Fact]
    public void unknown_building_duration_falls_back_to_the_average_of_known_durations_from_elsewhere()
    {
        // queued: 10000 bilinen; building: LastDurationMs=null, elapsed=1000 -> ortalama(10000)=10000 tahmini,
        // remaining = max(0, 10000-1000) = 9000
        // (10000 + 9000)/1 = 19000; +400 (building var) = 19400
        var queued = new long?[] { 10_000 };
        var building = new[] { new EtaCalculator.BuildingProject(ElapsedMs: 1_000, LastDurationMs: null) };

        long? raw = EtaCalculator.ComputeRawEstimateMs(queued, building, parallelism: 1);

        Assert.Equal(19_400, raw);
    }

    // ---- parallelism defensiveness ------------------------------------------------------------

    [Fact]
    public void non_positive_parallelism_is_clamped_to_one()
    {
        var queued = new long?[] { 10_000 };
        var building = Array.Empty<EtaCalculator.BuildingProject>();

        long? raw = EtaCalculator.ComputeRawEstimateMs(queued, building, parallelism: 0);

        Assert.Equal(10_000, raw); // /1, /0 değil
    }

    // ---- null-arg guards (codebase convention: ArgumentNullException.ThrowIfNull) --------------

    [Fact]
    public void compute_raw_estimate_throws_on_null_arguments()
    {
        var building = Array.Empty<EtaCalculator.BuildingProject>();
        Assert.Throws<ArgumentNullException>(() => EtaCalculator.ComputeRawEstimateMs(null!, building, 1));
        Assert.Throws<ArgumentNullException>(() => EtaCalculator.ComputeRawEstimateMs(Array.Empty<long?>(), null!, 1));
    }
}
