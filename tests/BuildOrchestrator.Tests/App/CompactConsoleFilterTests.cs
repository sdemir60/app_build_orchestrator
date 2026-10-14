using BuildOrchestrator.App.Console;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [idle-memory · dalga 2 · spec §6.5 — K4] Kompakt konsolun SAF süzgeci (<see cref="CompactConsoleFilter"/>): bir işlemin proje
/// çıktısından anlatıya ne gideceğinin tablosu. Hata her zaman geçer; her projenin ilk <see cref="CompactConsoleFilter.ShownWarningsPerProject"/>
/// TEKİL uyarısı geçer, sonrakiler gizlenip kodlarına göre sayılır; diğer her satır (Info, komut, boş) gizlenir; proje bitince gizlenen
/// uyarı varsa tek özet satırı. Sınıflandırma <see cref="ConsoleLineClassifier"/>'ındır (tek tanım), uyarı kodu da onun yanında çıkarılır.
/// <para>Tekillik (karar 5 · preflight C6): MSBuild her tanıyı bitiş özetinde bir kez daha yazar — girintili ve çoğu zaman sonunda
/// <c>[…\Proje.csproj]</c> ekiyle. Ham satır sayımı "N more warnings"ı gerçek sayının ~2 katına şişirirdi (gerçek loglarda toplamda
/// 3,4×); bu yüzden görülmüş bir uyarının tekrarı ne yeniden gösterilir ne sayılır.</para>
/// </summary>
public class CompactConsoleFilterTests
{
    /// <summary>MSBuild'in satır içi tanısı — her satır numarası ayrı (tekil) bir uyarıdır.</summary>
    private static string Warn(int line, string code = "CS1591", string text = "Missing XML comment") =>
        $"  C:\\r\\A.cs({line},1): warning {code}: {text}";

    private const string E = "  C:\\r\\A.cs(2,1): error CS0103: The name 'x' does not exist";

    [Fact]
    public void Errors_pass_the_first_three_distinct_warnings_pass_and_every_other_line_is_hidden()
    {
        var f = new CompactConsoleFilter();
        Assert.Null(f.LogLine("a", "  Copying file from X to Y"));          // Info
        Assert.Null(f.LogLine("a", ""));                                    // boş satır
        Assert.Null(f.LogLine("a", "msbuild A.csproj -t:Build"));           // komut satırı (Cmd)
        Assert.Equal(E, f.LogLine("a", E));
        Assert.Equal(Warn(1), f.LogLine("a", Warn(1)));
        Assert.Equal(Warn(2), f.LogLine("a", Warn(2)));
        Assert.Equal(Warn(3), f.LogLine("a", Warn(3)));
        Assert.Null(f.LogLine("a", Warn(4)));                               // 4. tekil uyarı
        Assert.Null(f.LogLine("a", Warn(5, "MSB3277", "Found conflicts")));
        Assert.Equal(E, f.LogLine("a", E));                                 // hata hep geçer — tekrarı da; kota hatayı etkilemez

        Assert.Equal("A: 2 more warnings — CS1591 ×1, MSB3277 ×1 — see the project log", f.ProjectDone("a", "A"));
        Assert.Null(f.ProjectDone("b", "B"));                               // gizlenen uyarı yok
    }

    /// <summary>
    /// [karar 5] Bitiş özetinin tekrarları: satır içi dört tekil uyarı (biri dosyası projenin kendisi olduğu için eksiz), sonra MSBuild'in
    /// bitiş bloğu aynı dördünü girintili ve proje ekiyle yeniden yazar (gerçek log biçimi —
    /// <c>logs\run-20261014-192737-215\7A41B90FDD4FCAA5.log</c>). İlk üçü bir kez görünür, dördüncüsü bir kez sayılır.
    /// </summary>
    [Fact]
    public void A_repeated_warning_is_neither_shown_again_nor_counted()
    {
        const string Suffix = @" [D:\r\Orchestration.Customer\Orchestration.Customer.csproj]";
        string[] inline =
        [
            @"D:\r\Business.Customer\Business.Customer.csproj : warning NU1903: 'Newtonsoft.Json' 9.0.1 has a known vulnerability" + Suffix,
            @"D:\r\Orchestration.Customer\Orchestration.Customer.csproj : warning NU1903: 'Newtonsoft.Json' 9.0.1 has a known vulnerability",
            @"D:\r\Orchestration.Customer\Customer\Person.designer.cs(91,17): warning CS0472: The result is always 'true'" + Suffix,
            @"D:\r\Orchestration.Customer\Customer\Customer.designer.cs(265,38): warning CS0168: 'e' is declared but never used" + Suffix,
        ];
        var f = new CompactConsoleFilter();
        Assert.Equal(inline[0], f.LogLine("a", inline[0]));
        Assert.Equal(inline[1], f.LogLine("a", inline[1]));
        Assert.Equal(inline[2], f.LogLine("a", inline[2]));
        Assert.Null(f.LogLine("a", inline[3]));

        // Bitiş özeti: aynı tanılar girintili; eksiz satır ekli de gelebilir — ikisi de tekrardır.
        Assert.Null(f.LogLine("a", "  " + inline[0]));
        Assert.Null(f.LogLine("a", "  " + inline[1] + Suffix));
        Assert.Null(f.LogLine("a", "  " + inline[2]));
        Assert.Null(f.LogLine("a", "  " + inline[3]));

        Assert.Equal("A: 1 more warning — CS0168 ×1 — see the project log", f.ProjectDone("a", "A")); // 2 değil
    }

    /// <summary>Sayaçlar projeye aittir (kimlik harf duyarsız — Windows yolu) ve yeni işlemde (<see cref="CompactConsoleFilter.Reset"/>)
    /// sıfırlanır: önceki işlemde görülmüş uyarı yeni işlemin ilk üçü arasında yeniden görünür.</summary>
    [Fact]
    public void Counts_are_per_project_and_reset_per_operation()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 5; i++) f.LogLine(@"C:\p\a.csproj", Warn(i));
        Assert.Equal(Warn(1), f.LogLine(@"C:\p\b.csproj", Warn(1)));      // B'nin kendi kotası: A'nın beşi onu etkilemez
        Assert.Null(f.LogLine(@"C:\P\A.CSPROJ", Warn(6)));                 // aynı proje, başka yazım: A'nın kotası dolu

        f.Reset();
        Assert.Equal(Warn(1), f.LogLine(@"C:\p\a.csproj", Warn(1)));      // yeni işlem: görülmüş uyarı ilk üç arasında yeniden görünür
        Assert.Null(f.ProjectDone(@"C:\p\a.csproj", "A"));                 // önceki işlemin gizledikleri sayılmaz
        Assert.Null(f.ProjectDone(@"C:\p\b.csproj", "B"));
    }

    /// <summary>Kodu olmayan uyarı (Supervisor'ın bağımlılık satırı) <c>other</c> sayılır; tek gizlenen uyarı tekil yazılır
    /// (<c>StreamText.Counted</c> — "1 more warnings" kopyala-yapıştır kokusudur).</summary>
    [Fact]
    public void A_warning_without_a_code_counts_as_other_and_a_single_hidden_warning_is_singular()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 4; i++) f.LogLine("a", $"warning: R{i} failed in this run — last successful output referenced (R{i})");
        Assert.Equal("A: 1 more warning — other ×1 — see the project log", f.ProjectDone("a", "A"));
    }

    /// <summary>Özet en çok <see cref="CompactConsoleFilter.SummaryCodes"/> kod sayar: çoktan aza, eşitlikte alfabetik; toplam sayı
    /// listede olmayan kodları da içerir.</summary>
    [Fact]
    public void The_summary_names_at_most_three_codes_by_count_then_alphabetically()
    {
        var f = new CompactConsoleFilter();
        int line = 0;
        for (int i = 0; i < 3; i++) f.LogLine("a", Warn(++line));                     // gösterilen ilk üç
        f.LogLine("a", Warn(++line, "CS0168"));
        for (int i = 0; i < 2; i++) f.LogLine("a", Warn(++line, "MSB3277"));
        for (int i = 0; i < 2; i++) f.LogLine("a", Warn(++line, "CS0252"));
        for (int i = 0; i < 3; i++) f.LogLine("a", Warn(++line, "NU1903"));

        Assert.Equal("A: 8 more warnings — NU1903 ×3, CS0252 ×2, MSB3277 ×2 — see the project log", f.ProjectDone("a", "A"));
    }

    /// <summary>[karar 3 · preflight C13] Uyarı kodu sınıflandırıcının yanında TEK yerde çıkarılır ve onun tanıdığı iki biçimi de
    /// tanır: kökenli (<c>…: warning CS1591:</c>) ve kökensiz (<c>warning NU1701:</c> satır başında). Kodu olmayan uyarıda kod yoktur.</summary>
    [Fact]
    public void The_warning_code_is_read_from_the_located_and_the_origin_less_form()
    {
        Assert.Equal("CS1591", ConsoleLineClassifier.WarningCode(Warn(1)));
        Assert.Equal("CS1591", ConsoleLineClassifier.WarningCode("csc : warning CS1591: missing doc"));
        Assert.Equal("NU1903", ConsoleLineClassifier.WarningCode(@"D:\r\X.csproj : warning NU1903: 'Newtonsoft.Json' 9.0.1"));
        Assert.Equal("NU1701", ConsoleLineClassifier.WarningCode("warning NU1701: package restored"));
        Assert.Equal("NU1701", ConsoleLineClassifier.WarningCode("   warning NU1701 : package restored"));
        Assert.Null(ConsoleLineClassifier.WarningCode("warning: R failed in this run"));
        Assert.Null(ConsoleLineClassifier.WarningCode("Build succeeded."));
        Assert.Null(ConsoleLineClassifier.WarningCode(null));
    }

    /// <summary>[karar 6 · preflight C5] Özet satırı Warn rengindedir: sınıflandırıcı onu süzgecin TEK biçim tanımıyla tanır
    /// (<see cref="CompactConsoleFilter.IsSummary"/>) — ikinci bir literal yok. Benzer ama özet olmayan bir satır tanınmaz.</summary>
    [Fact]
    public void The_summary_line_is_classified_as_a_warning()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 5; i++) f.LogLine("a", Warn(i));
        string summary = f.ProjectDone("a", "OSYS.Business.Customer")!;

        Assert.Equal("OSYS.Business.Customer: 2 more warnings — CS1591 ×2 — see the project log", summary);
        Assert.True(CompactConsoleFilter.IsSummary(summary));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify(summary));
        Assert.False(CompactConsoleFilter.IsSummary("Build succeeded — see the project log"));
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify("Build succeeded — see the project log"));
    }

    /// <summary>[karar 7 · 10] Projenin başlığı (<c>building &lt;ad&gt;</c>) işlem başına BİR kez gelir — döngü turları onu
    /// yinelemez; özet üretilince projenin durumu düşer, aynı sonucun tekrarı (replay) özeti ikilemez; yeni işlem ikisini de sıfırlar.</summary>
    [Fact]
    public void The_header_comes_once_per_operation_and_a_summary_is_produced_once()
    {
        var f = new CompactConsoleFilter();
        Assert.Equal("building A", f.ProjectHeader(@"C:\p\a.csproj", "A"));
        Assert.Null(f.ProjectHeader(@"C:\P\A.csproj", "A"));                // ikinci tur, başka yazım: başlık yok
        for (int i = 1; i <= 4; i++) f.LogLine(@"C:\p\a.csproj", Warn(i));

        Assert.Equal("A: 1 more warning — CS1591 ×1 — see the project log", f.ProjectDone(@"C:\P\A.CSPROJ", "A"));
        Assert.Null(f.ProjectDone(@"C:\p\a.csproj", "A"));                  // replay: özet ikilenmez

        f.Reset();
        Assert.Equal("building A", f.ProjectHeader(@"C:\p\a.csproj", "A")); // yeni işlem
    }
}
