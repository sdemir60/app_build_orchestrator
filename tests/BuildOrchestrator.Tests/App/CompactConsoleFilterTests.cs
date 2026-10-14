using BuildOrchestrator.App.Console;
using static BuildOrchestrator.Tests.App.MainWindowHost;

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
    // Uyarı satırları ortak kurucudan (MainWindowHost.WarningLine): her satır numarası ayrı, tekil bir uyarıdır.
    private const string E ="  C:\\r\\A.cs(2,1): error CS0103: The name 'x' does not exist";

    [Fact]
    public void Errors_pass_the_first_three_distinct_warnings_pass_and_every_other_line_is_hidden()
    {
        var f = new CompactConsoleFilter();
        Assert.Null(f.LogLine("a", "  Copying file from X to Y"));          // Info
        Assert.Null(f.LogLine("a", ""));                                    // boş satır
        Assert.Null(f.LogLine("a", "msbuild A.csproj -t:Build"));           // komut satırı (Cmd)
        Assert.Equal(E, f.LogLine("a", E));
        Assert.Equal(WarningLine(1), f.LogLine("a", WarningLine(1)));
        Assert.Equal(WarningLine(2), f.LogLine("a", WarningLine(2)));
        Assert.Equal(WarningLine(3), f.LogLine("a", WarningLine(3)));
        Assert.Null(f.LogLine("a", WarningLine(4)));                               // 4. tekil uyarı
        Assert.Null(f.LogLine("a", WarningLine(5, "MSB3277", "Found conflicts")));
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
        for (int i = 1; i <= 5; i++) f.LogLine(@"C:\p\a.csproj", WarningLine(i));
        Assert.Equal(WarningLine(1), f.LogLine(@"C:\p\b.csproj", WarningLine(1)));      // B'nin kendi kotası: A'nın beşi onu etkilemez
        Assert.Null(f.LogLine(@"C:\P\A.CSPROJ", WarningLine(6)));                 // aynı proje, başka yazım: A'nın kotası dolu

        f.Reset();
        Assert.Equal(WarningLine(1), f.LogLine(@"C:\p\a.csproj", WarningLine(1)));      // yeni işlem: görülmüş uyarı ilk üç arasında yeniden görünür
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
        for (int i = 0; i < 3; i++) f.LogLine("a", WarningLine(++line));                     // gösterilen ilk üç
        f.LogLine("a", WarningLine(++line, "CS0168"));
        for (int i = 0; i < 2; i++) f.LogLine("a", WarningLine(++line, "MSB3277"));
        for (int i = 0; i < 2; i++) f.LogLine("a", WarningLine(++line, "CS0252"));
        for (int i = 0; i < 3; i++) f.LogLine("a", WarningLine(++line, "NU1903"));

        Assert.Equal("A: 8 more warnings — NU1903 ×3, CS0252 ×2, MSB3277 ×2 — see the project log", f.ProjectDone("a", "A"));
    }

    /// <summary>[karar 3 · preflight C13] Uyarı kodu sınıflandırıcının TEK ayrıştırmasından gelir ve onun tanıdığı iki biçimi de tanır:
    /// kökenli (<c>…: warning CS1591:</c>) ve kökensiz (<c>warning NU1701:</c> satır başında). Kodu olmayan uyarıda kod yoktur.
    /// <para>[DEĞİŞEN KURULUM — fix 1 · M4] Eski API <c>ConsoleLineClassifier.WarningCode(string)</c> kodu ayrı bir regex'le bulup string
    /// olarak döndürüyordu (gizlenen her tekil uyarıda Match + Group + string; kodsuzda <c>null</c>). Kod artık sınıflandırmanın KENDİ
    /// ayrıştırmasının verdiği satır dilimidir (<c>Classify(span, out code)</c>; kodsuzda boş) — iddia aynı.</para></summary>
    [Fact]
    public void The_warning_code_is_read_from_the_located_and_the_origin_less_form()
    {
        Assert.Equal("CS1591", CodeOf(WarningLine(1)));
        Assert.Equal("CS1591", CodeOf("csc : warning CS1591: missing doc"));
        Assert.Equal("NU1903", CodeOf(@"D:\r\X.csproj : warning NU1903: 'Newtonsoft.Json' 9.0.1"));
        Assert.Equal("NU1701", CodeOf("warning NU1701: package restored"));
        Assert.Equal("NU1701", CodeOf("   warning NU1701 : package restored"));
        Assert.Equal("", CodeOf("warning: R failed in this run"));
        Assert.Equal("", CodeOf("Build succeeded."));
        Assert.Equal("", CodeOf(null));
    }

    private static string CodeOf(string? line)
    {
        ConsoleLineClassifier.Classify(line.AsSpan(), out var code);
        return code.ToString();
    }

    /// <summary>[karar 6 · preflight C5] Özet satırı Warn rengindedir: sınıflandırıcı onu süzgecin TEK biçim tanımıyla tanır
    /// (<see cref="CompactConsoleFilter.IsSummary"/>) — ikinci bir literal yok. Benzer ama özet olmayan bir satır tanınmaz.</summary>
    [Fact]
    public void The_summary_line_is_classified_as_a_warning()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 5; i++) f.LogLine("a", WarningLine(i));
        string summary = f.ProjectDone("a", "OSYS.Business.Customer")!;

        Assert.Equal("OSYS.Business.Customer: 2 more warnings — CS1591 ×2 — see the project log", summary);
        Assert.True(CompactConsoleFilter.IsSummary(summary));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify(summary));
        Assert.False(CompactConsoleFilter.IsSummary("Build succeeded — see the project log"));
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify("Build succeeded — see the project log"));
    }

    /// <summary>[karar 7 · 10] Projenin başlığı (<c>building &lt;ad&gt;</c>) işlem başına BİR kez gelir — döngü turları onu
    /// yinelemez; özet üretilince projenin durumu düşer, aynı sonucun tekrarı (replay) özeti ikilemez; yeni işlem ikisini de sıfırlar.
    /// <para>[DEĞİŞEN KURULUM — fix 1 · M2b] Başlık adı artık bir çözücüyle verilir ve YALNIZ başlık üretilirken sorulur: ikinci turun
    /// satırı ad aramaz (eskiden ad her satırda çağıranın önceden çözdüğü bir string'di). İddia aynı; sorulan adlar da pinlenir.</para></summary>
    [Fact]
    public void The_header_comes_once_per_operation_and_a_summary_is_produced_once()
    {
        var f = new CompactConsoleFilter();
        var asked = new List<string>();
        string NameOf(string id) { asked.Add(id); return "A"; }

        Assert.Equal("building A", f.ProjectHeader(@"C:\p\a.csproj", NameOf));
        Assert.Null(f.ProjectHeader(@"C:\P\A.csproj", NameOf));             // ikinci tur, başka yazım: başlık yok, ad sorulmaz
        Assert.Equal([@"C:\p\a.csproj"], asked);
        for (int i = 1; i <= 4; i++) f.LogLine(@"C:\p\a.csproj", WarningLine(i));

        Assert.Equal("A: 1 more warning — CS1591 ×1 — see the project log", f.ProjectDone(@"C:\P\A.CSPROJ", "A"));
        Assert.Null(f.ProjectDone(@"C:\p\a.csproj", "A"));                  // replay: özet ikilenmez

        f.Reset();
        Assert.Equal("building A", f.ProjectHeader(@"C:\p\a.csproj", NameOf)); // yeni işlem
    }

    /// <summary>[fix 1 · M2] Koşunun sonunda, sonucu hiç gelmemiş (motor kaybı) ve uyarı gizlemiş projelerin bekleyen özetleri — projelerin
    /// ilk uyarı sırasıyla; adlar yalnız özet için sorulur. Sonucu gelmiş ya da gizlediği uyarı olmayan proje bekleyen değildir;
    /// boşaltılan proje düşer: ikinci boşaltma — ve her projenin sonucunu almış normal bir bitiş — boştur.</summary>
    [Fact]
    public void The_pending_summaries_are_drained_once_in_first_warning_order()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 5; i++) f.LogLine("b", WarningLine(i));      // B'nin uyarıları önce
        for (int i = 1; i <= 4; i++) f.LogLine("a", WarningLine(i));
        for (int i = 1; i <= 5; i++) f.LogLine("c", WarningLine(i));
        f.LogLine("d", WarningLine(1));                                    // gizlenen yok
        Assert.NotNull(f.ProjectDone("c", "C"));                           // C sonucunu aldı

        Assert.Equal(["B: 2 more warnings — CS1591 ×2 — see the project log", "A: 1 more warning — CS1591 ×1 — see the project log"],
            f.DrainPending(id => id.ToUpperInvariant()));
        Assert.Empty(f.DrainPending(id => id));                            // boşaltılanlar düştü
    }

    /// <summary>
    /// [fix 1 · I1] "Hatalar tam" (spec §6.5, karar 5) MSBuild'in kanonik tanı biçiminin HER hâlinde geçerlidir — projenin uyarı kotası
    /// dolmuş olsa da; hata satırı uyarı sayılmaz (özetin "other ×n"ine de girmez). Satırlar sınıflandırıcının TEK listesindendir
    /// (<see cref="ConsoleColorizerTests.CanonicalMsBuildErrors"/>).
    /// <para>KIRMIZI (eski sınıflandırıcı): <c>MSBUILD : error …</c> komut, NU1605'in warning-as-error'u uyarı (kota dolu: gizlenip
    /// "other" sayılıyordu), alt kategorili XAML hatası bilgi sayılıyordu — dördü de kompakt anlatıdan kayboluyordu; projenin TEK hata
    /// satırı olabilirler.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(ConsoleColorizerTests.CanonicalMsBuildErrors), MemberType = typeof(ConsoleColorizerTests))]
    public void An_error_of_every_canonical_msbuild_form_comes_back_unchanged_after_the_warning_quota(string error)
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 3; i++) f.LogLine("a", WarningLine(i)); // uyarı kotası doldu

        Assert.Equal(error, f.LogLine("a", error));
        Assert.Null(f.ProjectDone("a", "A"));                         // uyarı sayılmadı: gizlenen yok
    }

    /// <summary>[fix 1 · M3] Çok hedefli (multi-targeted) bir projede MSBuild'in bitiş özeti eke hedef çerçeveyi de yazar:
    /// <c>[C:\x\A.csproj::TargetFramework=net8.0]</c>. Ek <c>[&lt;yol&gt;proj(::&lt;özellikler&gt;)?]</c> biçiminde atılır: satır içi kopya
    /// (eksiz) ile özetteki kopya TEK uyarıdır. Bu makinenin koşu loglarında böyle ek yoktu (OSYS çok hedefli değil; 528 k satır düz
    /// <c>[…proj]</c> ekiyle bitiyor) — biçim MSBuild konsol logger'ınındır.
    /// <para>KIRMIZI: ek yalnız <c>proj]</c> ile bitiyorsa atılıyordu — hedef çerçeveli tekrar YENİ bir uyarı sayılıyordu ("2 more
    /// warnings").</para></summary>
    [Fact]
    public void A_multi_targeted_projects_end_of_build_repeat_counts_once()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 3; i++) f.LogLine("a", WarningLine(i));
        string inline = WarningLine(4);                                                             // satır içi: eksiz
        Assert.Null(f.LogLine("a", inline));
        Assert.Null(f.LogLine("a", "    " + inline.Trim() + @" [C:\x\A.csproj::TargetFramework=net8.0]")); // bitiş özeti

        Assert.Equal("A: 1 more warning — CS1591 ×1 — see the project log", f.ProjectDone("a", "A"));
    }

    /// <summary>
    /// [fix 1 · M4] Süzgeç IPC thread'inde, VM'in kilidi altında her proje satırında koşar (OSYS koşusunda ~390 k satır): gizlenen satır
    /// (bilgi, komut, boş) ve görülmüş bir uyarının tekrarı hiç ayırma yapmaz — ısınmadan sonra 0 bayt; geçen hata satırı da kendisidir.
    /// <para>KIRMIZI (eski sınıflandırıcı): girintili satırın kırpılmış kopyası ve tanı aramasının birleştirdiği string'ler.</para>
    /// </summary>
    [Fact]
    public void A_hidden_line_and_a_repeated_warning_allocate_nothing()
    {
        var f = new CompactConsoleFilter();
        string[] lines =
        [
            WarningLine(1), WarningLine(2), WarningLine(3), WarningLine(4, "MSB3277"), WarningLine(5, "CS0168"),
            "warning NU1701: package restored", "warning: R failed in this run — last successful output referenced (R)",
            "  Copying file from X to Y", "\"C:\\Program Files\\MSBuild.exe\" C:\\p\\A.csproj -t:Build", "msbuild A.csproj -t:Build", "",
            "    0 Hata", E,
        ];
        foreach (string line in lines) f.LogLine("a", line); // ısınma: ilk üçü gösterildi, gerisi sayıldı — sonrası hep tekrar

        long allocated = PerfMeasure.AllocatedBy(() =>
        {
            for (int round = 0; round < 200; round++)
                foreach (string line in lines) f.LogLine("a", line);
        });

        Assert.Equal(0, allocated);
    }
}
