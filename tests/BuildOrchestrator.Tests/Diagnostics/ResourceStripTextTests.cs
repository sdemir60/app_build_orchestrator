using System.Globalization;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Core.ProcessControl;
using static BuildOrchestrator.Tests.ByteUnits;

namespace BuildOrchestrator.Tests.Diagnostics;

/// <summary>
/// [kaynak şeridi · design v1.25.0 §2.1 · karar K3-K6] Şeridin SAF hesabı ve metinleri (<see cref="ResourceStripText"/>): CPU
/// yüzdesi, RAM'in MB/GB metni, üç tooltip, paralellik paydası. Çıktı metinleri KOPYA METİN olduğundan bilerek literaldir — kuraldan
/// türetilseydi test kendini doğrulardı; yalnız "her mod" testleri tabloyu (<see cref="PerfProfile.For"/>) okur ki yeni bir mod
/// metinsiz unutulamasın. Hiçbir test süreç, saat ya da UI okumaz.
/// </summary>
public class ResourceStripTextTests
{
    // ---------------------------------------------------------------- CPU yüzdesi

    /// <summary>
    /// Yüzde = işlemci zamanı / (duvar zamanı × işlemci sayısı) × 100, en yakın tam sayıya, yarımlar sıfırdan UZAĞA, [0, 100] aralığında.
    /// Payda yoksa (duvar ya da işlemci sayısı ≤ 0) <c>null</c>. İlk satır sınırdır: 12,5 → 13 (bankacı yuvarlaması 12 verirdi). Son iki
    /// satır hesabın TİK üzerinde tam yapıldığını sabitler: bir tik altı yarım aşağı iner; 3,5 ms / 100 ms tam %3,5'tir ve 4 olmalıdır —
    /// aynı bölme çift duyarlıklı sayıyla 3,4999999999999996 verir ve 3'e yuvarlanırdı.
    /// </summary>
    public static TheoryData<TimeSpan, TimeSpan, int, int?> CpuPercentCases => new()
    {
        { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 8, 13 },
        { TimeSpan.Zero, TimeSpan.FromSeconds(3), 8, 0 },
        { TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(3), 8, 100 },      // %1250 → tavan
        { TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(3), 8, 0 },        // sayaç geri gitmez; negatif fark = 0
        { TimeSpan.FromSeconds(1), TimeSpan.Zero, 8, null },                // payda yok
        { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), 0, null },
        { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(-3), 8, null },     // negatif duvar zamanı da payda değil
        { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), -8, null },
        { TimeSpan.FromSeconds(24), TimeSpan.FromSeconds(3), 8, 100 },      // tam %100 (tavan değil, eşit)
        { TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(3), 4, 13 },      // örnekleyici örneği: 12,5 → 13
        { TimeSpan.FromTicks(9_999_999), TimeSpan.FromSeconds(1), 8, 12 },  // 12,4999998… → 12
        { TimeSpan.FromTicks(35_000), TimeSpan.FromMilliseconds(100), 1, 4 },
    };

    [Theory]
    [MemberData(nameof(CpuPercentCases))]
    public void CpuPercent_is_cpu_time_over_wall_time_times_cores_rounded_half_away_from_zero_and_clamped(
        TimeSpan cpuTime, TimeSpan wallTime, int processorCount, int? expected) =>
        Assert.Equal(expected, ResourceStripText.CpuPercent(cpuTime, wallTime, processorCount));

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0, "0%")]
    [InlineData(1, "1%")]
    [InlineData(69, "69%")]
    [InlineData(100, "100%")]
    public void Cpu_writes_the_percent_or_the_unreadable_mark(int? percent, string expected) =>
        Assert.Equal(expected, ResourceStripText.Cpu(percent));

    // ---------------------------------------------------------------- RAM metni

    /// <summary>
    /// 1 GB altı tam MB (sayı <see cref="MemoryLine.Megabytes"/>'ın kuralı: yarımlar sıfırdan uzağa); GB'ye geçiş YUVARLANMIŞ MB 1024
    /// olunca olur, yani 1023,5 MiB bile <c>"1024 MB"</c> değil <c>"1.0 GB"</c> yazılır. GB tek ondalıktır ve ondalığın yarımı da sıfırdan
    /// uzağa gider: 1280 MB tam 1,25 GB'dir → <c>"1.3 GB"</c> (bankacı yuvarlaması 1.2 verirdi). 4,85'in iki yanı: 4966 MB = 4,8496 GB,
    /// 4967 MB = 4,8506 GB; <c>5_207_642_522</c> bayt 4966,39 MB'dir → 4966. Eksi bayt okuma hatasıdır, <c>0 MB</c> değil <c>—</c>
    /// yazılır (<c>0 MB</c> uyuyan motorun gerçek ölçümüdür).
    /// </summary>
    public static TheoryData<long?, string> MemoryCases => new()
    {
        { null, "—" },
        { -1L, "—" },
        { -5 * Mb, "—" },
        { 0L, "0 MB" },
        { Mb / 2 - 1, "0 MB" },
        { Mb / 2, "1 MB" },
        { 5 * Mb / 2, "3 MB" },
        { 310 * Mb, "310 MB" },
        { 1023 * Mb, "1023 MB" },
        { 1023 * Mb + 512 * Kb - 1, "1023 MB" },
        { 1023 * Mb + 512 * Kb, "1.0 GB" },
        { 1024 * Mb, "1.0 GB" },
        { 1279 * Mb, "1.2 GB" },
        { 1280 * Mb, "1.3 GB" },
        { 2304 * Mb, "2.3 GB" },
        { 4966 * Mb, "4.8 GB" },
        { 4967 * Mb, "4.9 GB" },
        { 5_207_642_522, "4.8 GB" },
        { 4506 * Mb, "4.4 GB" },
        { 12595 * Mb, "12.3 GB" },
        { 100 * Gb, "100.0 GB" },
    };

    [Theory]
    [MemberData(nameof(MemoryCases))]
    public void Memory_writes_whole_megabytes_below_a_gigabyte_and_one_decimal_gigabytes_from_it(long? bytes, string expected) =>
        Assert.Equal(expected, ResourceStripText.Memory(bytes));

    /// <summary>MB kısmı <see cref="MemoryLine.Megabytes"/>'ın TEK kuralıdır: motorun <c>memory:</c> satırı ve About'un tanı satırlarıyla
    /// aynı yuvarlama; şerit kendi MB yuvarlamasını yazmaz.</summary>
    [Fact]
    public void The_megabyte_part_is_the_one_megabyte_rule_of_the_memory_line()
    {
        foreach (long bytes in new[] { 0L, Mb / 2 - 1, Mb / 2, 5 * Mb / 2, 310 * Mb, 1023 * Mb + 512 * Kb - 1 })
            Assert.Equal(MemoryLine.Megabytes(bytes) + " MB", ResourceStripText.Memory(bytes));
    }

    /// <summary>Metin kültürden bağımsızdır: virgüllü ondalık kültürlerde (Türkçe, Almanca) de nokta görünür, sayılar aynı kalır.</summary>
    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void The_texts_do_not_depend_on_the_current_culture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            Assert.Equal("4.8 GB", ResourceStripText.Memory(4966 * Mb));
            Assert.Equal("1.3 GB", ResourceStripText.Memory(1280 * Mb));
            Assert.Equal("69%", ResourceStripText.Cpu(69));
            Assert.Equal("/4", ResourceStripText.ParallelAllowed(PerfMode.Balanced));
            Assert.Equal("Memory used by Acme · app 310 MB · engine 85 MB · compilers 4.4 GB",
                ResourceStripText.MemoryTooltip("Acme", 310 * Mb, 85 * Mb, 4506 * Mb));
            Assert.Equal("3 of 4 projects building in parallel",
                ResourceStripText.ParallelTooltip(PerfMode.Balanced, 3, running: true, reducedReason: null));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---------------------------------------------------------------- tooltip'ler

    // Ürün adı çağıranın parametresidir (App'te AppIdentity.Product): bu testler tarafsız bir örnek ad ("Acme") verir; Core metne kendi
    // adını gömseydi beklenen metin tutmazdı. Gömmeme kuralının kaynak tarafı AppIdentityTests'tedir (adın literal olarak yazılamadığı yüzeyler).

    /// <summary>Cap modun tablo değeridir (Balanced %70, Light %40); cap'siz Full'de ek hiç yoktur ("cap off" yazılmaz). Ürün adı verilen addır.</summary>
    [Theory]
    [InlineData(PerfMode.Full, "CPU used by Acme — app, engine and compilers")]
    [InlineData(PerfMode.Balanced, "CPU used by Acme — app, engine and compilers · cap 70%")]
    [InlineData(PerfMode.Light, "CPU used by Acme — app, engine and compilers · cap 40%")]
    public void CpuTooltip_names_the_given_product_and_the_cap_of_the_mode_and_Full_carries_none(PerfMode mode, string expected) =>
        Assert.Equal(expected, ResourceStripText.CpuTooltip("Acme", mode));

    [Fact]
    public void MemoryTooltip_lists_app_engine_and_compilers_each_as_a_memory_text() =>
        Assert.Equal("Memory used by Acme · app 310 MB · engine 85 MB · compilers 4.4 GB",
            ResourceStripText.MemoryTooltip("Acme", 310 * Mb, 85 * Mb, 4506 * Mb));

    /// <summary>Uyuyan motorun job'ı boştur: motor ve derleyiciler 0 verilir ve <c>0 MB</c> yazılır — ölçüm vardır, değeri sıfırdır.</summary>
    [Fact]
    public void MemoryTooltip_writes_a_sleeping_engine_and_no_compilers_as_zero_megabytes() =>
        Assert.Equal("Memory used by Acme · app 300 MB · engine 0 MB · compilers 0 MB",
            ResourceStripText.MemoryTooltip("Acme", 300 * Mb, 0, 0));

    /// <summary>Okunamayan parça <c>—</c> olur, öteki parçalar yerinde kalır (bir parçanın hatası ötekini silmez).</summary>
    [Fact]
    public void MemoryTooltip_writes_an_unreadable_part_as_the_mark_and_keeps_the_others()
    {
        Assert.Equal("Memory used by Acme · app — · engine 85 MB · compilers —",
            ResourceStripText.MemoryTooltip("Acme", null, 85 * Mb, null));
        Assert.Equal("Memory used by Acme · app — · engine — · compilers —",
            ResourceStripText.MemoryTooltip("Acme", null, null, null));
    }

    /// <summary>Ürün adı olduğu gibi geçer — özel karakter, süslü parantez ve çok baytlı harf dahil; ikinci bir ad hiçbir yerde belirmez.</summary>
    [Theory]
    [InlineData("Acme")]
    [InlineData("Acme {0} & Sons — 2")]
    [InlineData("Café Ω")]
    public void Both_tooltips_carry_exactly_the_product_name_they_are_given(string product)
    {
        Assert.Equal($"CPU used by {product} — app, engine and compilers · cap 70%",
            ResourceStripText.CpuTooltip(product, PerfMode.Balanced));
        Assert.Equal($"Memory used by {product} · app 1 MB · engine 2 MB · compilers 3 MB",
            ResourceStripText.MemoryTooltip(product, 1 * Mb, 2 * Mb, 3 * Mb));
    }

    // ---------------------------------------------------------------- paralellik

    [Theory]
    [InlineData(PerfMode.Full, "/6")]
    [InlineData(PerfMode.Balanced, "/4")]
    [InlineData(PerfMode.Light, "/2")]
    public void ParallelAllowed_is_the_workers_of_the_mode_after_a_slash(PerfMode mode, string expected) =>
        Assert.Equal(expected, ResourceStripText.ParallelAllowed(mode));

    /// <summary>
    /// Üç hâl: BOŞTA (koşu yok) mod sözcüğü ve modun tablo paralelliği; KOŞUDA koşan sayısı — pay 0 iken de koşu metni, boşta metnine
    /// dönmez; KOŞUDA VE KISILMIŞ <c>pay of payda — reduced: gerekçe</c>. Boşta metni payı ve gerekçeyi dinlemez. Boş ya da yalnız
    /// boşluk olan gerekçe kısılma yok demektir.
    /// </summary>
    public static TheoryData<PerfMode, int, bool, string?, string> ParallelTooltipCases => new()
    {
        { PerfMode.Full, 0, false, null, "Full — up to 6 projects build in parallel" },
        { PerfMode.Balanced, 0, false, null, "Balanced — up to 4 projects build in parallel" },
        { PerfMode.Light, 0, false, null, "Light — up to 2 projects build in parallel" },
        { PerfMode.Balanced, 3, false, "3 GB free memory", "Balanced — up to 4 projects build in parallel" },
        { PerfMode.Balanced, 0, true, null, "0 of 4 projects building in parallel" },
        { PerfMode.Balanced, 3, true, null, "3 of 4 projects building in parallel" },
        { PerfMode.Full, 6, true, null, "6 of 6 projects building in parallel" },
        { PerfMode.Light, 1, true, null, "1 of 2 projects building in parallel" },
        { PerfMode.Balanced, 3, true, "", "3 of 4 projects building in parallel" },
        { PerfMode.Balanced, 3, true, "   ", "3 of 4 projects building in parallel" },
        { PerfMode.Balanced, 2, true, "3 GB free memory", "2 of 4 — reduced: 3 GB free memory" },
        { PerfMode.Balanced, 0, true, "3 GB free memory", "0 of 4 — reduced: 3 GB free memory" },
        { PerfMode.Full, 2, true, "2 logical processors", "2 of 6 — reduced: 2 logical processors" },
    };

    [Theory]
    [MemberData(nameof(ParallelTooltipCases))]
    public void ParallelTooltip_tells_the_idle_the_running_and_the_reduced_story(
        PerfMode mode, int building, bool running, string? reducedReason, string expected) =>
        Assert.Equal(expected, ResourceStripText.ParallelTooltip(mode, building, running, reducedReason));

    /// <summary>Gerekçe motorun metnidir (<see cref="PerfNoteText"/>) ve değişmeden geçer: çevrilmez, kısaltılmaz, büyük/küçük harfi ve
    /// noktalaması olduğu gibi kalır.</summary>
    [Fact]
    public void ParallelTooltip_passes_the_reason_the_engine_wrote_through_verbatim()
    {
        Assert.Equal("2 of 4 — reduced: 3 GB free memory",
            ResourceStripText.ParallelTooltip(PerfMode.Balanced, 2, running: true, PerfNoteText.FreeMemoryLimit(3)));
        Assert.Equal("1 of 4 — reduced: 1 logical processor",
            ResourceStripText.ParallelTooltip(PerfMode.Balanced, 1, running: true, PerfNoteText.LogicalProcessorsLimit(1)));
        Assert.Equal("2 of 4 — reduced: LOW  memory, 3GB!",
            ResourceStripText.ParallelTooltip(PerfMode.Balanced, 2, running: true, "LOW  memory, 3GB!"));
    }

    // ---------------------------------------------------------------- tablo ve sabitler

    /// <summary>Payda, cap ve mod sözcüğü TABLODAN ve enum'dan gelir — her mod için: yeni bir mod eklenirse bu test onu da sınar, şerit
    /// ikinci bir 6/4/2 ya da Full/Balanced/Light tablosu tutmaz.</summary>
    [Fact]
    public void Every_mode_takes_its_workers_cap_and_name_from_the_profile_table_and_the_enum()
    {
        foreach (PerfMode mode in Enum.GetValues<PerfMode>())
        {
            var profile = PerfProfile.For(mode);
            string workers = profile.Parallelism.ToString(CultureInfo.InvariantCulture);

            Assert.Equal("/" + workers, ResourceStripText.ParallelAllowed(mode));
            Assert.Equal($"{mode.ToString()} — up to {workers} projects build in parallel",
                ResourceStripText.ParallelTooltip(mode, 0, running: false, reducedReason: null));
            Assert.Equal($"1 of {workers} projects building in parallel",
                ResourceStripText.ParallelTooltip(mode, 1, running: true, reducedReason: null));

            string cpuTooltip = ResourceStripText.CpuTooltip("Acme", mode);
            if (profile.CpuCapPercent is { } cap)
                Assert.EndsWith($" · cap {cap.ToString(CultureInfo.InvariantCulture)}%", cpuTooltip, StringComparison.Ordinal);
            else
                Assert.DoesNotContain("cap", cpuTooltip, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_constants_are_the_texts_the_design_names()
    {
        Assert.Equal("—", ResourceStripText.Unreadable);
        Assert.Equal("100%", ResourceStripText.CpuGhost);
        Assert.Equal("12.3 GB", ResourceStripText.MemoryGhost);
        Assert.Equal("12/12", ResourceStripText.ParallelGhost);
        Assert.Equal("Balanced", ResourceStripText.ModeGhost);
    }

    /// <summary>
    /// Sabit çerçeve: hayalet, hücrenin gösterebileceği EN GENİŞ metin kadar yer ayırır (değer yazısı mono, genişlik karakter sayısıdır).
    /// CPU'nun en geniş metni tavandır; RAM'inki <c>1023 MB</c> ya da iki haneli GB; paralellikte pay hanesi + payda en geniş mod için,
    /// mod sözcüğü de en uzun mod adı kadardır — bir mod ya da tablo büyürse hayalet onu da kapsamak zorundadır.
    /// </summary>
    [Fact]
    public void The_ghosts_reserve_the_width_of_the_widest_text_each_cell_can_show()
    {
        Assert.Equal(ResourceStripText.CpuGhost, ResourceStripText.Cpu(100));
        Assert.True(ResourceStripText.Memory(1023 * Mb).Length <= ResourceStripText.MemoryGhost.Length);   // "1023 MB"
        Assert.True(ResourceStripText.Memory(102298 * Mb).Length <= ResourceStripText.MemoryGhost.Length); // "99.9 GB"

        int widestModeName = 0, widestNumbers = 0;
        foreach (PerfMode mode in Enum.GetValues<PerfMode>())
        {
            widestModeName = Math.Max(widestModeName, mode.ToString().Length);
            int workersDigits = PerfProfile.For(mode).Parallelism.ToString(CultureInfo.InvariantCulture).Length;
            widestNumbers = Math.Max(widestNumbers, workersDigits + ResourceStripText.ParallelAllowed(mode).Length);
        }
        Assert.Equal(widestModeName, ResourceStripText.ModeGhost.Length);
        Assert.True(widestNumbers <= ResourceStripText.ParallelGhost.Length);

        foreach (string ghost in new[] { ResourceStripText.CpuGhost, ResourceStripText.MemoryGhost, ResourceStripText.ParallelGhost })
            Assert.True(ResourceStripText.Unreadable.Length <= ghost.Length);
    }
}
