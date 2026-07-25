using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [D4/T34 — DD2] Konsol anlatı satırı "typing degradation" motorunun SAF karar mantığı (v7 plan §230:
/// "drop-to-latest; throughput-suspend; hatalar anında; ham MSBuild asla harf-harf"). Tempo/daktilo
/// <see cref="TypewriterScheduler"/>'dadır; burada YALNIZ "en yeni satır daktilolanmalı mı?" kararı test edilir —
/// WPF'siz, deterministik (nowMs enjekte edilir; sleep/poll YOK — D8).
/// </summary>
public class TypingDegradationTests
{
    [Fact]
    public void Live_line_drops_to_latest_when_lines_arrive_faster_than_the_typewriter()
    {
        // İlk sakin satır daktilolanır; ardından burst penceresi (< 340ms) içinde gelen satır "en yeniye düşer"
        // (drop-to-latest) — daktilolanmaz, anında basılır. Pencere geçince daktilo yeniden mümkün olur.
        var gate = new ConsoleTypingGate();

        Assert.True(gate.ShouldType(isRawProjectLog: false, ConsoleLineType.Info, batchLineCount: 1, nowMs: 0));
        Assert.False(gate.ShouldType(isRawProjectLog: false, ConsoleLineType.Info, batchLineCount: 1, nowMs: 100));
        Assert.False(gate.ShouldType(isRawProjectLog: false, ConsoleLineType.Info, batchLineCount: 1, nowMs: 300));
        // 300 + 340 = 640'tan sonra tekrar sakin: daktilo yeniden açılır.
        Assert.True(gate.ShouldType(isRawProjectLog: false, ConsoleLineType.Info, batchLineCount: 1, nowMs: 1000));
    }

    [Fact]
    public void Typing_is_suspended_entirely_above_the_throughput_threshold()
    {
        // Tek bir flush eşik satır sayısını AŞARSA (fırtına), daktilo TAMAMEN askıya alınır — satır sakin bir
        // anlatı satırı (Info) olsa bile anında basılır.
        var gate = new ConsoleTypingGate();

        Assert.False(gate.ShouldType(isRawProjectLog: false, ConsoleLineType.Info,
            batchLineCount: ConsoleTypingGate.ThroughputSuspendLines + 1, nowMs: 0));
        // Eşiğin altında + sakin: daktilolanır (askı yalnız eşik ÜSTÜnde).
        var calm = new ConsoleTypingGate();
        Assert.True(calm.ShouldType(isRawProjectLog: false, ConsoleLineType.Info,
            batchLineCount: ConsoleTypingGate.ThroughputSuspendLines, nowMs: 0));
    }

    [Fact]
    public void Errors_are_always_printed_instantly()
    {
        // Hata satırı sakin ve tek başına gelse bile ASLA daktilolanmaz (anında görünür — DD2).
        var gate = new ConsoleTypingGate();

        Assert.False(gate.ShouldType(isRawProjectLog: false, ConsoleLineType.Error, batchLineCount: 1, nowMs: 0));
    }

    [Fact]
    public void Raw_msbuild_output_is_never_typed_character_by_character()
    {
        // Ham MSBuild çıktısı (anlatı değil — zaman damgası yok) ASLA harf-harf yazılmaz; sakin/tek olsa bile instant.
        var gate = new ConsoleTypingGate();

        Assert.False(gate.ShouldType(isRawProjectLog: true, ConsoleLineType.Info, batchLineCount: 1, nowMs: 0));
    }

    [Fact]
    public void A_calm_single_narrative_line_is_typed()
    {
        // Sağlık kontrolü: sakin, tek, hatasız, zaman-damgalı anlatı satırı (cmd/info) daktilolanır.
        var gate = new ConsoleTypingGate();

        Assert.True(gate.ShouldType(isRawProjectLog: false, ConsoleLineType.Cmd, batchLineCount: 1, nowMs: 0));
    }
}
