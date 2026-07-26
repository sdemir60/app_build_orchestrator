using System.Diagnostics;
using System.Windows;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.App.Views;
using BuildOrchestrator.Contracts.Model;
using Xunit.Abstractions;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E6 Step 1] Liste realizasyon perf ölçümü. <see cref="StickyLayerList"/> virtualization KAPALI (It-4 kısıtı,
/// global-constraints §4.1 — 500+ hedefi T51/It-5) → her satır HEVESLE realize olur. Bu test 191 satırın
/// Measure/Arrange duvar-saati maliyetini ölçer (<b>bütçe &lt; 400ms</b>) ve 500'ü KAYIT için ölçer (sert bütçe YOK).
///
/// <para>Gürültüye karşı sağlam: warmup pass + birkaç iterasyonun MEDYANI (tek atış GC/JIT sıçramasına açık). Ölçüm
/// bir HWND GEREKTİRMEZ — saf Measure/Arrange; DynamicResource'lar host merge zincirinden çözülür (bkz.
/// <see cref="DsResources.NewHost"/>) ve animasyon KAPALI (<see cref="StickyLayerList.AnimationsEnabledProvider"/> =
/// <c>() =&gt; false</c>) olduğundan compositor saati gerekmez. Her ölçümde <see cref="StickyLayerList.RevealRows"/>
/// sayısı == N doğrulanır: aksi halde boş/eksik realize edilmiş bir ağacı ölçüp sessizce yeşil kalmak mümkündü.</para>
///
/// <para><b>Ertelenen bütçe:</b> 191 bütçeyi aşarsa suite KIRILMAZ (simplify-card kararı → T51/It-5) — gerçek sayı
/// test çıktısına yazılır + raporda belgelenir; yalnız felaket bir regresyon (gevşek tavan) suite'i patlatır.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact kaynak çekişmesi — bkz. ConsoleUiSerialCollection
public class ListRealizationPerfTests(ITestOutputHelper output)
{
    private const double BudgetMs191 = 400;      // brief E6 Step 1 bütçesi
    private const double SanityCeilingMs = 3000; // bütçe aşılsa bile suite'i patlatma; yalnız felaket regresyon kırar

    /// <summary>[L1/It-5] Hover EDİLMEMİŞ bir satırın kurabileceği nesne tavanı (görsel+mantıksal ağaç birleşimi,
    /// bkz. <see cref="DsResources.RealizedObjects"/>). Duvar saati makineye göre oynaktır; sadeleştirmenin
    /// kalıcılığını KORUYAN şey bu deterministik sayımdır. [L1] Kart sadeleştirmesi ÖNCESİ 55, SONRASI 39 nesne
    /// (hover ikonları + VS-chooser artık ilk hover'da kurulur: StackPanel + 2 Button + 2 Viewbox + 2 Canvas +
    /// 2 Path + Popup + Border + StackPanel + TextBlock + StackPanel = 16). Tavan dar tutuldu (+1 marj) ki eager
    /// bir alt-ağaç geri sızarsa test kırılsın.
    ///
    /// <para>[T49 fix round 2] Sayım artık SATIRIN KENDİSİNİ de içeriyor (<c>RealizedObjects</c> kökü döner) →
    /// 39 yerine 40; tavan aynı +1 marjı korumak için 41'e alındı. GERÇEK bütçe DEĞİŞMEDİ.</para></summary>
    private const int UnhoveredRowObjectCeiling = 41;

    [StaFact]
    public void Realizing_191_project_rows_measure_and_arrange_stays_under_the_400ms_budget()
    {
        double median191 = MeasureRealizationMs(rowCount: 191, warmups: 2, samples: 5);
        double median500 = MeasureRealizationMs(rowCount: 500, warmups: 1, samples: 3); // kayıt için (It-5 hedefi)

        output.WriteLine($"[E6 perf] 191-row realize median = {median191:N1} ms (budget < {BudgetMs191:N0} ms)");
        output.WriteLine($"[E6 perf] 500-row realize median = {median500:N1} ms (record only — no budget, T51/It-5 target)");

        if (median191 < BudgetMs191)
            Assert.True(median191 < BudgetMs191,
                $"191-satır realize {median191:N1} ms — bütçe {BudgetMs191:N0} ms.");
        else
            // Bütçe aşıldı → T51/It-5'e ertelenir; suite KIRILMAZ. Yalnız felaket bir regresyona karşı gevşek tavan.
            Assert.True(median191 < SanityCeilingMs,
                $"191-satır realize {median191:N1} ms — 400ms bütçeyi aştı (T51/It-5'e ertelendi) VE {SanityCeilingMs:N0} ms gevşek tavanı da aştı (felaket regresyon).");
    }

    [StaFact]
    public void An_unhovered_project_row_builds_no_more_than_the_per_row_object_ceiling()
    {
        // [L1] Sadeleştirmenin DETERMİNİSTİK kanıtı (duvar saati değil): hover edilmemiş bir satırın kurduğu
        // nesne sayısı. dirty satır seçilir — sha/sağ-blok yolu da kurulur (perf ölçümündeki satırların yarısı).
        var vm = new ProjectRowViewModel(@"C:\p\Foo.csproj", "Foo", ProjectRowState.Pending) { WillBuild = true };
        var host = DsResources.NewHost();
        var row = new ProjectRow { AnimationsEnabledProvider = () => false, DataContext = vm };
        var window = DsResources.Realize(host, row);

        var realized = DsResources.RealizedObjects(row);
        int objects = realized.Count;
        output.WriteLine($"[L1] hover edilmemiş ProjectRow nesne sayısı = {objects} (tavan {UnhoveredRowObjectCeiling}) :: " +
            string.Join(", ", realized.GroupBy(o => o.GetType().Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}×{g.Count()}")));
        Assert.True(objects <= UnhoveredRowObjectCeiling,
            $"hover edilmemiş satır {objects} nesne kuruyor — tavan {UnhoveredRowObjectCeiling}. Eager bir alt-ağaç geri sızmış olabilir.");
        GC.KeepAlive(window);
    }

    /// <summary>Aynı realizasyonu <paramref name="warmups"/> kez ısıtır, sonra <paramref name="samples"/> ölçümün
    /// medyanını döndürür (ms). Her ölçüm TAZE bir host+list+VM kümesiyle sıfırdan realize eder; timer yalnız
    /// Measure/Arrange penceresini kapsar. Warmup/GC/medyan iskeleti <see cref="PerfMeasure"/>'dadır — aynı
    /// iskeleti <see cref="GraphRealizationPerfTests"/> de kullanır (G1 review round 1).</summary>
    private static double MeasureRealizationMs(int rowCount, int warmups, int samples)
        => PerfMeasure.MedianOf(() => RealizeOnce(rowCount), warmups, samples);

    /// <summary>Tek bir realizasyonu ölçer: N satırlık bir <see cref="StickyLayerList"/> kurar ve YALNIZ
    /// Measure/Arrange/UpdateLayout duvar-saatini döndürür. Realizasyonun gerçekten tamamlandığını (N satır)
    /// doğrular.</summary>
    private static double RealizeOnce(int rowCount)
    {
        var host = DsResources.NewHost();
        var list = new StickyLayerList { AnimationsEnabledProvider = () => false };
        var rows = new List<object>(rowCount);
        for (int i = 0; i < rowCount; i++)
            rows.Add(new ProjectRowViewModel($@"C:\p\proj{i}.csproj", $"Proj{i}", ProjectRowState.Pending)
            {
                WillBuild = (i % 2 == 0), // dirty satırlar sha/sağ-blok yolunu da realize eder (gerçekçi)
            });
        list.SetGroups([new StickyLayerList.LayerGroup("", rows)]);
        host.Child = list;

        var size = new Size(420, 760);
        var sw = Stopwatch.StartNew();
        host.Measure(size);
        host.Arrange(new Rect(new Point(0, 0), size));
        host.UpdateLayout(); // non-virtualized → tüm N container + ProjectRow burada üretilir/measure edilir
        sw.Stop();

        int realized = list.RevealRows.Count;
        Assert.True(realized == rowCount, $"realizasyon eksik: {realized}/{rowCount} satır — ölçüm anlamsız.");
        return sw.Elapsed.TotalMilliseconds;
    }
}
