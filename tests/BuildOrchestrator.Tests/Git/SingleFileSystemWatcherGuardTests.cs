using System.Collections.Generic;
using System.Text.RegularExpressions;
using BuildOrchestrator.Tests.App;

namespace BuildOrchestrator.Tests.Git;

/// <summary>
/// [T11 · rehber madde 53] <c>FileSystemWatcher</c> inşasının kaynak-tarayan guard'ı — klasik
/// <c>new FileSystemWatcher(...)</c> ve target-typed <c>FileSystemWatcher x = new(...)</c> biçimlerinin
/// İKİSİ de yakalanır: src altında bu inşa TEK bir dosyada yaşar.
///
/// <para><b>Neden var:</b> "dosya kaydetmek Sync'i tetiklemez" garantisi tek bir öncüle dayanır —
/// <c>HeadWatcher</c> yalnız <c>&lt;gitDir&gt;\logs\HEAD</c>'i izler; kaynak ağacına, <c>bin</c>/<c>obj</c>'a
/// ya da başka bir dosyaya bakan İKİNCİ bir <c>FileSystemWatcher</c> yoktur. İnceleme dikkatine güvenmek
/// yerine sınır burada çitlenir: ileride eklenecek bir "kaynak değişince otomatik tetikle" özelliği bu
/// guard'ı kırmadan yeni bir izleyici açamaz.</para>
///
/// <para><b>İzin listesi DAR ve GEREKÇELİ:</b> yalnız <c>HeadWatcher</c>'ın kendi dosyası. Adet PİNLENMEZ
/// ama dosya listesi pinlenir — yeni bir dosyaya <c>FileSystemWatcher</c> inşası eklemek (klasik ya da
/// target-typed) guard'ı kırmızıya çeker.</para>
///
/// <para><b>[DEĞİŞEN KURAL — review bulgusu]</b> Eski regex yalnız klasik <c>new FileSystemWatcher(</c>
/// biçimini yakalıyordu; target-typed <c>FileSystemWatcher x = new(...)</c> (aynı satırda tip adı +
/// değişken + <c>=</c> + <c>new(</c>) bu deseni atlayıp dört testi de sessizce yeşil bırakırdı. Regex artık
/// İKİ alternatifin birleşimi — her iki biçim de
/// <see cref="The_rule_recognises_a_watcher_that_sneaks_into_another_file"/> ile tek tek kanıtlanır.</para>
///
/// <para><b>YAKALAYAMADIĞI (bilinçli sınır):</b> tip adını çalışma zamanında birleştirmek
/// (<c>"FileSystem" + "Watcher"</c>) ya da yansımayla kurmak. Guard literal çağrı biçimine bakar; niyetin
/// denetimi review'ın işidir.</para>
/// </summary>
public sealed class SingleFileSystemWatcherGuardTests
{
    /// <summary><c>FileSystemWatcher</c> inşası — klasik <c>new FileSystemWatcher(</c> YA DA target-typed
    /// <c>FileSystemWatcher x = new(</c> (tip adı, değişken, <c>=</c>, <c>new(</c> aynı satırda).</summary>
    private static readonly Regex FileSystemWatcherConstruction = new(
        @"new\s+FileSystemWatcher\s*\(|FileSystemWatcher\??\s+\w+\s*=\s*new\s*\(", RegexOptions.Compiled);

    /// <summary>Mutasyonun MEŞRU olduğu tek dosya (src köküne göre) ve gerekçesi.</summary>
    private static readonly IReadOnlyCollection<string> Allowed =
    [
        // Sync'in tek tetikleyicisi: logs\HEAD üzerindeki gerçek Windows dosya bildirimi (T11, madde 53).
        @"BuildOrchestrator.Core\Git\HeadWatcher.cs",
    ];

    [Fact]
    public void No_file_system_watcher_is_constructed_outside_the_allowed_file()
    {
        var offenders = SourceGuard.ScanSrc("*.cs", FileSystemWatcherConstruction, Allowed, skipCommentLines: true);

        Assert.True(offenders.Count == 0,
            "FileSystemWatcher izinli dosyanın dışında inşa ediliyor:\n  " + string.Join("\n  ", offenders));
    }

    [Fact]
    public void The_head_watcher_is_the_only_place_that_constructs_one()
    {
        // 'new FileSystemWatcher(' tek bir dosyada yaşar — hangi kök verilirse verilsin ilke aynı.
        var watcherUsers = SourceGuard.ScanSrc("*.cs", FileSystemWatcherConstruction,
            allowedFiles: null, skipCommentLines: true);

        Assert.All(watcherUsers, offender =>
            Assert.StartsWith(@"BuildOrchestrator.Core\Git\HeadWatcher.cs", offender, StringComparison.Ordinal));
        Assert.NotEmpty(watcherUsers); // tarama gerçekten bir şey gördü
    }

    [Fact]
    public void The_guard_actually_scans_the_production_tree()
    {
        // Boş bir tarama guard'ı sessizce yeşil bırakırdı.
        var scanned = SourceGuard.ScannedSrcFiles("*.cs");

        Assert.Contains(@"BuildOrchestrator.Core\Git\HeadWatcher.cs", scanned);
        Assert.True(scanned.Count > 50, $"Beklenenden az dosya tarandı: {scanned.Count}");
    }

    [Fact]
    public void The_rule_recognises_a_watcher_that_sneaks_into_another_file()
    {
        // Guard'ın kendi kanıtı: sahte bir ihlal — klasik VE target-typed 'new' biçimi — gerçekten
        // raporlanıyor mu? İkisi de AYRI birer offender üretmeli, tek bir alternatif sessizce atlamamalı.
        var offenders = SourceGuard.ScanText("Core/Git/GitService.cs",
            """
            var watcher = new FileSystemWatcher(root, "*.cs");
            FileSystemWatcher watcher2 = new(root, "*.cs");
            """,
            FileSystemWatcherConstruction);

        Assert.Equal(2, offenders.Count);
        Assert.Contains(offenders, o => o.Contains("new FileSystemWatcher(", StringComparison.Ordinal));
        Assert.Contains(offenders, o => o.Contains("FileSystemWatcher watcher2 = new(", StringComparison.Ordinal));
    }
}
