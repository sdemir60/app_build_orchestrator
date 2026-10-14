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
    // Komut, uyarı ve hata satırları ortak kurucudan (MainWindowHost.CommandLine / WarningLine / ErrorLine): her satır numarası ayrı,
    // tekil bir tanıdır.

    /// <summary>Motorun projenin loguna yazdığı bir bağımlılık notu (<c>RunCoordinator.DepIssueWarnLines</c>'ın ilk biçimi) — bu sınıfın
    /// motor notu girdisinin TEK tanımı (N5 testi ve ayırma pini).</summary>
    private const string EngineNote = "warning: R failed in this run — last successful output referenced (R)";

    [Fact]
    public void Errors_pass_the_first_three_distinct_warnings_pass_and_every_other_line_is_hidden()
    {
        var f = new CompactConsoleFilter();
        Assert.Null(f.LogLine("a", "  Copying file from X to Y"));          // Info
        Assert.Null(f.LogLine("a", ""));                                    // boş satır
        Assert.Null(f.LogLine("a", "msbuild A.csproj -t:Build"));           // komut satırı (Cmd)
        Assert.Equal(ErrorLine(2), f.LogLine("a", ErrorLine(2)));
        Assert.Equal(WarningLine(1), f.LogLine("a", WarningLine(1)));
        Assert.Equal(WarningLine(2), f.LogLine("a", WarningLine(2)));
        Assert.Equal(WarningLine(3), f.LogLine("a", WarningLine(3)));
        Assert.Null(f.LogLine("a", WarningLine(4)));                        // 4. tekil uyarı
        Assert.Null(f.LogLine("a", WarningLine(5, "MSB3277", "Found conflicts")));
        Assert.Equal(ErrorLine(2), f.LogLine("a", ErrorLine(2)));           // hata hep geçer — tekrarı da; kota hatayı etkilemez

        Assert.Equal("A: 2 more warnings — CS1591 ×1, MSB3277 ×1 — see the project log", f.ProjectDone("a", Named("A")));
        Assert.Null(f.ProjectDone("b", Named("B")));                        // gizlenen uyarı yok
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

        Assert.Equal("A: 1 more warning — CS0168 ×1 — see the project log", f.ProjectDone("a", Named("A"))); // 2 değil
    }

    /// <summary>Sayaçlar projeye aittir (kimlik harf duyarsız — Windows yolu) ve yeni işlemde (<see cref="CompactConsoleFilter.Reset"/>)
    /// sıfırlanır: önceki işlemde görülmüş uyarı yeni işlemin ilk üçü arasında yeniden görünür.</summary>
    [Fact]
    public void Counts_are_per_project_and_reset_per_operation()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 5; i++) f.LogLine(@"C:\p\a.csproj", WarningLine(i));
        Assert.Equal(WarningLine(1), f.LogLine(@"C:\p\b.csproj", WarningLine(1)));      // B'nin kendi kotası: A'nın beşi onu etkilemez
        Assert.Null(f.LogLine(@"C:\P\A.CSPROJ", WarningLine(6)));                       // aynı proje, başka yazım: A'nın kotası dolu

        f.Reset();
        Assert.Equal(WarningLine(1), f.LogLine(@"C:\p\a.csproj", WarningLine(1)));      // yeni işlem: görülmüş uyarı ilk üç arasında yeniden görünür
        Assert.Null(f.ProjectDone(@"C:\p\a.csproj", Named("A")));                       // önceki işlemin gizledikleri sayılmaz
        Assert.Null(f.ProjectDone(@"C:\p\b.csproj", Named("B")));
    }

    /// <summary>Kodu olmayan uyarı — MSBuild'in kodsuz biçimi (<c>…(1,5): warning : …</c>; kodu verilmemiş bir <c>&lt;Warning&gt;</c>
    /// görevi böyle yazar) — <c>other</c> sayılır; tek gizlenen uyarı tekil yazılır (<c>StreamText.Counted</c> — "1 more warnings"
    /// kopyala-yapıştır kokusudur).
    /// <para><b>[DEĞİŞEN KURAL — dalga 2 son inceleme N5]</b> Eski iddia: motorun projenin loguna yazdığı bağımlılık notları
    /// (<c>warning: R1 failed in this run — …</c>) kodsuz uyarıdır — "ilk 3"e girer, sonrası <c>other</c> sayılır. Değişme gerekçesi:
    /// spec §6.5 "motor notları süzülmez" planın süzgeç testinden üstündür (karar N5) — o notlar artık her zaman geçer ve sayılmaz
    /// (<see cref="The_engines_own_notes_always_pass_and_neither_count_nor_use_the_quota"/>); <c>other</c> kodsuz MSBuild uyarısının
    /// kovasıdır.</para></summary>
    [Fact]
    public void A_warning_without_a_code_counts_as_other_and_a_single_hidden_warning_is_singular()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 4; i++) f.LogLine("a", $@"C:\x\A.csproj({i},5): warning : R{i} is not signed");
        Assert.Equal("A: 1 more warning — other ×1 — see the project log", f.ProjectDone("a", Named("A")));
    }

    /// <summary>
    /// [dalga 2 son inceleme N5 · spec §6.5 "motor notları süzülmez"] Motorun kendi notları — projenin loguna derleme çıktısından ÖNCE
    /// yazdığı bağımlılık uyarıları (<c>RunCoordinator</c>'ın dört biçimi) — kompakt kipte her zaman geçer: sayılmaz ve projenin "ilk 3"
    /// kotasını harcamaz. Tanıma sınıflandırıcının uygulama/motor öneki tanımındandır (<c>warning:</c>); ikinci bir literal yok.
    /// <para>KIRMIZI: notlar kodsuz uyarı sayılıyordu — kotayı onlar dolduruyor, derleyicinin ilk üç uyarısı gizlenip özette
    /// sayılıyordu ("4 more warnings").</para>
    /// </summary>
    [Fact]
    public void The_engines_own_notes_always_pass_and_neither_count_nor_use_the_quota()
    {
        var f = new CompactConsoleFilter();
        string[] notes =
        [
            EngineNote,
            "warning: S is in a dependency cycle and was not rebuilt — last known output referenced",
            "warning: T has pending changes and was not rebuilt in this run — last known output referenced",
            "warning: failure in dependency chain (U, V) — referenced outputs may be stale",
        ];
        foreach (string note in notes) Assert.Equal(note, f.LogLine("a", note));
        for (int i = 1; i <= 3; i++) Assert.Equal(WarningLine(i), f.LogLine("a", WarningLine(i))); // kota notlardan bağımsız
        Assert.Null(f.LogLine("a", WarningLine(4)));                                               // derleyicinin dördüncüsü
        Assert.Equal(notes[0], f.LogLine("a", notes[0]));                                          // döngü turunda yinelenen not da

        Assert.Equal("A: 1 more warning — CS1591 ×1 — see the project log", f.ProjectDone("a", Named("A")));
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

        Assert.Equal("A: 8 more warnings — NU1903 ×3, CS0252 ×2, MSB3277 ×2 — see the project log", f.ProjectDone("a", Named("A")));
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

    /// <summary>[son inceleme O7] Her kimliğe aynı adı veren çözücü — süzgeç adı yalnız bir satır yazılırken sorar
    /// (<see cref="CompactConsoleFilter.ProjectDone"/>, <see cref="CompactConsoleFilter.DrainPending"/>).</summary>
    private static Func<string, string> Named(string name) => _ => name;

    /// <summary>[karar 6 · preflight C5] Özet satırı Warn rengindedir: sınıflandırıcı onu süzgecin TEK biçim tanımıyla tanır
    /// (<see cref="CompactConsoleFilter.IsSummary"/>) — ikinci bir literal yok. Benzer ama özet olmayan bir satır tanınmaz.</summary>
    [Fact]
    public void The_summary_line_is_classified_as_a_warning()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 5; i++) f.LogLine("a", WarningLine(i));
        string summary = f.ProjectDone("a", Named("OSYS.Business.Customer"))!;

        Assert.Equal("OSYS.Business.Customer: 2 more warnings — CS1591 ×2 — see the project log", summary);
        Assert.True(CompactConsoleFilter.IsSummary(summary));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify(summary));
        Assert.False(CompactConsoleFilter.IsSummary("Build succeeded — see the project log"));
        Assert.Equal(ConsoleLineType.Info, ConsoleLineClassifier.Classify("Build succeeded — see the project log"));
    }

    /// <summary>[dalga 2 son inceleme F3] Tek gizlenen uyarının özeti TEKİL yazılır (<c>1 more warning</c>) ve o da uyarı rengindedir: dört
    /// tekil uyarılı her proje bu satırı basar. Tanıma sayılı adın çekimini <c>StreamText.Counted</c>'ın kendi parçalarından okur (ayraç ve
    /// çoğul eki tek tanım) — çekim değişirse tanıma da onunla değişir.
    /// <para>KIRMIZI (geçici bozma: tanımada çoğul eki zorunlu kılındı): tekil özet bilgi rengine düşüyordu; testler yalnız çoğul özeti
    /// sınıflandırıyordu.</para></summary>
    [Fact]
    public void A_single_hidden_warnings_summary_is_singular_and_classified_as_a_warning()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 4; i++) f.LogLine("a", WarningLine(i));
        string summary = f.ProjectDone("a", Named("OSYS.Business.Customer"))!;

        Assert.Equal("OSYS.Business.Customer: 1 more warning — CS1591 ×1 — see the project log", summary);
        Assert.True(CompactConsoleFilter.IsSummary(summary));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify(summary));
        Assert.Equal(ConsoleLineType.Warn, ConsoleLineClassifier.Classify("A: 1 more warning — CS1591 ×1 — see the project log"));
    }

    /// <summary>[karar 7 · 10] Projenin başlığı (<c>building &lt;ad&gt;</c>) işlem başına BİR kez gelir — döngü turları onu
    /// yinelemez; özet üretilince projenin durumu düşer, aynı sonucun tekrarı (replay) özeti ikilemez; yeni işlem ikisini de sıfırlar.
    /// <para>[DEĞİŞEN KURULUM — fix 1 · M2b] Başlık adı artık bir çözücüyle verilir ve YALNIZ başlık üretilirken sorulur: ikinci turun
    /// satırı ad aramaz (eskiden ad her satırda çağıranın önceden çözdüğü bir string'di). İddia aynı; sorulan adlar da pinlenir.</para>
    /// <para>[DEĞİŞEN KURULUM — dalga 2 son inceleme O7] Özetin adı da aynı çözücüyle ve YALNIZ özet üretilirken sorulur: özeti olmayan
    /// sonuç (replay) ad aramaz — eskiden çağıran her proje sonucunda adı önceden çözüyordu.</para></summary>
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

        Assert.Equal("A: 1 more warning — CS1591 ×1 — see the project log", f.ProjectDone(@"C:\P\A.CSPROJ", NameOf));
        Assert.Null(f.ProjectDone(@"C:\p\a.csproj", NameOf));               // replay: özet ikilenmez, ad sorulmaz
        Assert.Equal([@"C:\p\a.csproj", @"C:\P\A.CSPROJ"], asked);

        f.Reset();
        Assert.Equal("building A", f.ProjectHeader(@"C:\p\a.csproj", NameOf)); // yeni işlem
    }

    /// <summary>[fix 1 · M2] Koşunun sonunda, sonucu hiç gelmemiş (motor kaybı) ve uyarı gizlemiş projelerin bekleyen özetleri — projelerin
    /// ilk uyarı sırasıyla; adlar yalnız özet için sorulur. Sonucu gelmiş ya da gizlediği uyarı olmayan proje bekleyen değildir;
    /// boşaltılan projenin gizlenen sayacı sıfırlanır: ikinci boşaltma — ve her projenin sonucunu almış normal bir bitiş — boştur.
    /// <para>[DEĞİŞEN İFADE — son inceleme · defter 8] Eskiden "boşaltılan proje düşer" deniyordu; artık yalnız gizlenen sayacı düşer,
    /// görülenlerin izi geç gelen tekrarlar için kalır (VM düzeyinde: <c>CompactConsoleTests.A_late_repeat_after_an_engine_loss_…</c>).
    /// Bu testin iddiası aynı.</para></summary>
    [Fact]
    public void The_pending_summaries_are_drained_once_in_first_warning_order()
    {
        var f = new CompactConsoleFilter();
        for (int i = 1; i <= 5; i++) f.LogLine("b", WarningLine(i));       // B'nin uyarıları önce
        for (int i = 1; i <= 4; i++) f.LogLine("a", WarningLine(i));
        for (int i = 1; i <= 5; i++) f.LogLine("c", WarningLine(i));
        f.LogLine("d", WarningLine(1));                                    // gizlenen yok
        Assert.NotNull(f.ProjectDone("c", Named("C")));                    // C sonucunu aldı

        Assert.Equal(["B: 2 more warnings — CS1591 ×2 — see the project log", "A: 1 more warning — CS1591 ×1 — see the project log"],
            f.DrainPending(id => id.ToUpperInvariant()));
        Assert.Empty(f.DrainPending(id => id));                            // boşaltılanların gizlenen sayacı sıfırlandı
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
        Assert.Null(f.ProjectDone("a", Named("A")));                 // uyarı sayılmadı: gizlenen yok
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
        string inline = WarningLine(4);                                                                    // satır içi: eksiz
        Assert.Null(f.LogLine("a", inline));
        Assert.Null(f.LogLine("a", "    " + inline.Trim() + @" [C:\x\A.csproj::TargetFramework=net8.0]")); // bitiş özeti

        Assert.Equal("A: 1 more warning — CS1591 ×1 — see the project log", f.ProjectDone("a", Named("A")));
    }

    /// <summary>
    /// [fix 1 · M4] Süzgeç IPC thread'inde, VM'in kilidi altında her proje satırında koşar (OSYS koşusunda ~390 k satır): gizlenen satır
    /// (bilgi, komut, boş) ve görülmüş bir uyarının tekrarı hiç ayırma yapmaz — ısınmadan sonra 0 bayt; geçen satırlar da kendileridir: hata
    /// ve motorun notu (N5: her turda aynen geçer, sayılmaz).
    /// <para>KIRMIZI (eski sınıflandırıcı): girintili satırın kırpılmış kopyası ve tanı aramasının birleştirdiği string'ler.</para>
    /// </summary>
    [Fact]
    public void A_hidden_line_and_a_repeated_warning_allocate_nothing()
    {
        var f = new CompactConsoleFilter();
        string[] lines =
        [
            WarningLine(1), WarningLine(2), WarningLine(3), WarningLine(4, "MSB3277"), WarningLine(5, "CS0168"),
            "warning NU1701: package restored", EngineNote,
            "  Copying file from X to Y", CommandLine, "msbuild A.csproj -t:Build", "",
            "    0 Hata", ErrorLine(2),
        ];
        // Isınma: ilk üç uyarı gösterildi, sonraki tekil uyarılar sayıldı — ölçülen turlarda hepsi tekrardır; hata satırı ve motorun notu
        // her turda aynen geçer, gerisi gizlenir.
        foreach (string line in lines) f.LogLine("a", line);

        long allocated = PerfMeasure.AllocatedBy(() =>
        {
            for (int round = 0; round < 200; round++)
                foreach (string line in lines) f.LogLine("a", line);
        });

        Assert.Equal(0, allocated);
    }

    /// <summary>
    /// [dalga 2 son inceleme · defter 7] Sayaç yolu da ayırma yapmaz: projenin kotası dolduktan sonra gelen YENİ bir tekil uyarı, kodu
    /// sayaçta zaten varsa yerinde sayılır — kod string'e yalnız sayaca yeni bir kod girerken çevrilir. Ölçüm tekil kümenin o anki
    /// kapasitesinin altında kalır (bir HashSet büyümesi ayırır — üretimde de uyarı sayısıyla orantılı, satır başına değil) ve satırlar
    /// ölçümün DIŞINDA kurulur.
    /// <para>KIRMIZI (geçici bozma: sayacın anahtarı <c>code.ToString()</c>): gizlenen her yeni uyarı bir string ayırıyordu; mevcut
    /// pinler yalnız görülmüş tekrarları besliyordu (tekrar sayaca hiç ulaşmaz).</para>
    /// </summary>
    [Fact]
    public void A_new_distinct_warning_with_a_counted_code_allocates_nothing()
    {
        var f = new CompactConsoleFilter();
        // Isınma 1000 tekil uyarı: ilk üçü gösterildi, gerisi CS1591 olarak sayıldı. Tekil küme bu sırada büyüdü — kapasitesi her
        // büyümede ikiye katlanır (asal boylar), son büyüme ölçülen 300 yeni uyarıya yer açmıştır: ölçüm içinde büyüme olmaz.
        string[] lines = [.. Enumerable.Range(1, 1300).Select(i => WarningLine(i))];
        foreach (string line in lines.AsSpan(0, 1000)) f.LogLine("a", line);

        long allocated = PerfMeasure.AllocatedBy(() =>
        {
            for (int i = 1000; i < lines.Length; i++) f.LogLine("a", lines[i]);
        });

        Assert.Equal(0, allocated);
        Assert.Equal("A: 1297 more warnings — CS1591 ×1297 — see the project log", f.ProjectDone("a", Named("A"))); // hepsi sayıldı
    }
}
