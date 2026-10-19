using System.IO;
using System.Text.RegularExpressions;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Core.Formatting;
using BuildOrchestrator.Tests.App;
using Xunit;

namespace BuildOrchestrator.Tests.Formatting;

/// <summary>
/// [temizlik madde 3] Değerin yokluğunun işareti (<c>—</c>) TEK sabittir: <see cref="MissingValue.Mark"/>. Eskiden üç ayrı tanımdı —
/// <c>DurationFormat.Duration(null)</c>'ın literali, <c>DiagnosticsReport.Unknown</c> ve <c>ResourceStripText.Unreadable</c> — ve biri
/// değişse diğerleri sessizce ayrışırdı. Adlar kalır, değer tek yerden gelir; kaynak guard'ı tam <c>"—"</c> literalinin ikinci kez
/// yazılmasını yakalar.
/// </summary>
public class MissingValueTests
{
    /// <summary>Literalin TAMAMI tek bir uzun tire olan literal: C# literali tırnaklarıyla (ve varsa <c>$</c>/<c>@</c> önekiyle) gelir,
    /// XAML özniteliğinin değeri tırnaksız. Cümle içindeki tire bu kuralın dışındadır.</summary>
    private static readonly Regex StandaloneMark = new("^(?:[$@]*\"—\"|—)$");

    private static readonly string Declaration = Path.Combine("BuildOrchestrator.Core", "Formatting", "MissingValue.cs");

    [Fact]
    public void The_mark_is_the_em_dash() => Assert.Equal("—", MissingValue.Mark);

    /// <summary>Üç yüzey de aynı işareti yazar: bilinmeyen süre, şeridin okunamayan değeri, About'un olmayan değeri.</summary>
    [Fact]
    public void Every_surface_writes_the_one_mark_for_a_missing_value()
    {
        Assert.Equal(MissingValue.Mark, DurationFormat.Duration(null));
        Assert.Equal(MissingValue.Mark, ResourceStripText.Unreadable);
        Assert.Equal(MissingValue.Mark, ResourceStripText.Cpu(null));
        Assert.Equal(MissingValue.Mark, DiagnosticsReport.Unknown);
    }

    /// <summary>KAYNAK GUARD'ı: tam <c>"—"</c> literali bütün <c>src</c>'de (.cs + .xaml) yalnız bildirildiği yerde yazılır; diğer her kullanım
    /// sabite bağlanır. Taramanın her üretim projesini gördüğü ayrıca assert edilir (boş bir tarama guard'ı sessizce yeşil bırakırdı).</summary>
    [Fact]
    public void The_mark_is_written_as_a_literal_only_where_it_is_declared()
    {
        var hits = SourceGuard.ScanSrcLiterals("*.cs", StandaloneMark).Concat(SourceGuard.ScanSrcLiterals("*.xaml", StandaloneMark)).ToList();

        Assert.StartsWith(Declaration + ":", Assert.Single(hits));
        SourceGuard.AssertScansEveryProductionProject("*.cs");
    }

    /// <summary>Guard'ın KENDİ kanıtı: dikilen tam literal (C# ve XAML) satır numarasıyla yakalanır; aynı tireyi taşıyan cümle ve yorum
    /// yakalanmaz.</summary>
    [Fact]
    public void The_guard_catches_a_planted_mark_but_not_a_dash_inside_a_sentence_or_a_comment()
    {
        const string code = """
            // — yalnız bir yorumda
            internal static class Planted
            {
                public const string Unknown = "—";
                public const string Sentence = "CPU used by the app — engine and compilers";
            }
            """;
        const string markup = """
            <!-- — yorumda -->
            <TextBlock Text="—" ToolTip="app — engine" />
            """;

        Assert.StartsWith("Planted.cs:4: ", Assert.Single(SourceGuard.ScanLiteralText("Planted.cs", code, ".cs", StandaloneMark)));
        Assert.StartsWith("Planted.xaml:2: ", Assert.Single(SourceGuard.ScanLiteralText("Planted.xaml", markup, ".xaml", StandaloneMark)));
    }
}
