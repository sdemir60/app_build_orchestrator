using System.Text.RegularExpressions;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [perf Faz B · son toparlama · madde 1] <c>ProjectFailedEvent.Reason</c>'ın "kullanıcı durdurdu" değeri TEK yerde
/// tanımlıdır: <see cref="FailureReasons.Stopped"/> (Contracts — App ↔ Supervisor sözleşmesinin projesi). Supervisor
/// (<c>RunCoordinator</c>) onu YAZAR, App (<c>RunViewModel.NoteTerminated</c>) OKUR; aynı literal iki projede tanımlı
/// kalırsa biri değişip diğeri unutulduğunda "Stop now" sonrası sonlandırılan proje sayısı sessizce sıfır kalırdı
/// (kopya YASAK, CLAUDE.md).
///
/// <para><b>Kural:</b> <c>"stopped"</c> string literali src'de yalnız sabitin kendi dosyasında geçer. Yorum satırları
/// (<c>failed("stopped")</c> diye ANLATAN doküman) ihlal değildir.</para>
///
/// <para><b>İzin listesi DAR ve GEREKÇELİ:</b> sabitin dosyası ve <c>OptimizeWorkspaceService</c> — orada <c>"stopped"</c> bir
/// proje başarısızlık NEDENİ (wire değeri) değil, restore uyarısının insan okur parantezidir
/// (<c>restore failed for X (stopped)</c>); Optimize'ın kendi sözlüğüdür ("timed out" · "stopped" · "exit N" — wire'daki
/// "timeout" değil).</para>
///
/// <para><b>Testler wire değerini LİTERAL yazmaya devam eder</b> (<c>Assert.Equal("stopped", e.Reason)</c>): sabit tek
/// doğruluk kaynağı olsa da değer protokoldedir; testlerdeki literal, sabit yanlışlıkla değiştirilirse
/// <see cref="The_constant_keeps_the_wire_value"/> ile birlikte uyumu çitler.</para>
/// </summary>
public class FailureReasonsGuardTests
{
    private static readonly Regex StoppedLiteral = new("\"stopped\"", RegexOptions.Compiled);

    /// <summary>"stopped" literalinin MEŞRU olduğu yollar (src köküne göre) ve gerekçeleri.</summary>
    private static readonly IReadOnlyCollection<string> Allowed =
    [
        // Sabitin kendisi — tek tanım.
        @"BuildOrchestrator.Contracts\Ipc\FailureReasons.cs",
        // Wire değeri DEĞİL: Optimize'ın restore uyarısındaki insan okur parantez ("restore failed for X (stopped)").
        @"BuildOrchestrator.Core\Workspace\OptimizeWorkspaceService.cs",
    ];

    [Fact]
    public void The_stopped_reason_literal_lives_only_in_the_contract()
    {
        var offenders = SourceGuard.ScanSrc("*.cs", StoppedLiteral, Allowed, skipCommentLines: true);

        Assert.True(offenders.Count == 0,
            "\"stopped\" literali Contracts'ın FailureReasons.Stopped sabiti dışında tanımlı:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Sabitin değeri protokoldedir (NDJSON): değişirse eski bir Supervisor ile yeni bir App ayrışır.</summary>
    [Fact]
    public void The_constant_keeps_the_wire_value()
    {
        Assert.Equal("stopped", FailureReasons.Stopped);
    }

    /// <summary>Guard'ın kendi kanıtı: Supervisor'a geri sızan bir literal gerçekten raporlanır.</summary>
    [Fact]
    public void The_rule_recognises_a_literal_that_sneaks_back_in()
    {
        var offenders = SourceGuard.ScanText(@"BuildOrchestrator.Supervisor\RunCoordinator.cs",
            "            failReason = \"stopped\";", StoppedLiteral, skipCommentLines: true);

        Assert.Single(offenders);
    }

    /// <summary>Guard'ın kendi kanıtı: değeri ANLATAN bir doküman satırı ihlal sayılmaz.</summary>
    [Fact]
    public void The_rule_ignores_a_comment_that_describes_the_value()
    {
        var offenders = SourceGuard.ScanText(@"BuildOrchestrator.App\ViewModels\RunViewModel.cs",
            "    /// projeleri <c>failed(\"stopped\")</c> yapıp stored state'lerini geçersizleştirir", StoppedLiteral,
            skipCommentLines: true);

        Assert.Empty(offenders);
    }
}
