using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [T64] Uygulamanın TEK ikon stratejisi: her ikon <c>Resources/Icons.xaml</c>'de ÇİZİLMİŞ bir
/// <see cref="Geometry"/>'dir. Öncesinde dört uyumsuz kaynak vardı — Unicode karakterler, <b>Segoe MDL2
/// Assets</b> ikon fontu, düz semboller ve kod içine gömülü <c>Geometry.Parse</c> path'leri. Bu sınıf dört şeyi
/// pinler: (a) sözlükteki her değer gerçekten parse edilebilir, boş olmayan bir geometridir; (b) It-4b'nin
/// ihtiyaç duyduğu anahtarların TAMAMI tanımlıdır (eksik anahtar B3-E6'da runtime'da null Data olarak
/// patlardı, derlemede değil); (c) kaynak ağacında hiçbir yerde MDL2 ikon fontuna bağımlılık kalmamıştır;
/// (d) hiçbir kontrol ikon geometrisini koda gömülü bir string'den parse ETMEZ — dördüncü kaynağın
/// (GraphView'ün inline sabitleri) geri sızmasını kapatır (T64 review, fix wave 1).
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class IconGeometryTests
{
    /// <summary>design-v1 <c>BuildApp.jsx:44-72</c> I.* tablosu + <c>GraphView</c>'ün lucide "package"'ı +
    /// <c>_ds_bundle.js:1164-1172</c> WinBtn caption glyph'leri (restore K8'den türetildi).</summary>
    public static readonly string[] RequiredKeys =
    [
        "Icon.Play", "Icon.Stop", "Icon.Sync", "Icon.Rot", "Icon.Redo", "Icon.Folder", "Icon.FolderOpen",
        "Icon.Vs", "Icon.Branch", "Icon.Tree", "Icon.Search", "Icon.Check", "Icon.Copy", "Icon.Down",
        "Icon.Trash", "Icon.Plus", "Icon.Back", "Icon.Grip", "Icon.Gear", "Icon.Sigma", "Icon.ChevUp",
        "Icon.AlertTri", "Icon.DepWarn", "Icon.LayQuad", "Icon.LayList", "Icon.LayFocus", "Icon.Package",
        "Icon.CaptionMinimize", "Icon.CaptionMaximize", "Icon.CaptionRestore", "Icon.CaptionClose",
        // [T60] DS kontrol kütüphanesinin ihtiyaç duyduğu çizimler (StatusGlyph gövdesi + Spinner + chevron)
        "Icon.StatusRing", "Icon.StatusCheck", "Icon.StatusCross", "Icon.StatusDash", "Icon.StatusClock",
        "Icon.StatusCycle", "Icon.Spinner", "Icon.Chevron",
        // [A13/T2 · 2.3] Chip'in `onRemove` göstergesi (_ds_bundle.js:206-220) — ham `✕` karakteri yerine çizim.
        "Icon.ChipRemove",
    ];

    /// <summary>[T60] Tasarımda <c>fill="currentColor" stroke="none"</c> ile verilen (DOLU) ikonlar —
    /// BuildApp.jsx:47 play · :48 stop · :67 grip · :58 depWarn. Geri kalan her ikon KONTURLUDUR.</summary>
    public static readonly string[] FilledKeys = ["Icon.Play", "Icon.Stop", "Icon.Grip", "Icon.DepWarn"];

    [StaFact]
    public void Every_declared_icon_parses_to_a_non_empty_geometry()
    {
        var icons = IconResources.Load();

        Assert.NotEmpty(icons.Keys);
        // [T60] Sözlük artık geometrinin YANINDA boya semantiğini de taşır (Icon.X.StrokeThickness double'ı,
        // Icon.StatusRing.DashArray). "Her değer bir Geometry'dir" iddiası bu yüzden ANAHTAR ADINA göre
        // ayrışır — sessizce atlamak yerine: nokta-son-ek taşımayan HER anahtar bir geometri OLMALIDIR.
        foreach (var key in icons.Keys.Cast<object>().Select(k => k.ToString()!))
        {
            if (key.EndsWith(".StrokeThickness", StringComparison.Ordinal)) { Assert.IsType<double>(icons[key]); continue; }
            if (key.EndsWith(".DashArray", StringComparison.Ordinal)) { Assert.NotEmpty(Assert.IsType<DoubleCollection>(icons[key])); continue; }

            var g = Assert.IsAssignableFrom<Geometry>(icons[key]);
            Assert.False(g.Bounds.IsEmpty, $"{key} boş geometri");
        }
    }

    [StaFact]
    public void Every_declared_icon_also_declares_how_it_is_painted()
    {
        // [T60 · B2 review carried-forward #1] Geometri TEK BAŞINA bir ikonu çizmeye yetmez: tasarım kaynağı
        // her ikon için ayrıca bir strokeWidth ve "dolgu mu kontur mu" kipi taşır. B2 sonrasında bu bilgi
        // sözlükte YOKTU ve iki tüketici (ConsoleHeader, GraphView) sayıları elle yeniden yazmıştı. Bu test
        // sınıflandırmanın TAM olduğunu pinler: her geometrinin kardeş bir .StrokeThickness'ı var ve dolu/
        // konturlu ayrımı 0 / >0 ile doğru kodlanmış.
        var icons = IconResources.Load();

        var missing = RequiredKeys.Where(k => !icons.Contains(k + ".StrokeThickness")).ToList();
        Assert.Empty(missing);

        foreach (string key in RequiredKeys)
        {
            double thickness = Assert.IsType<double>(icons[key + ".StrokeThickness"]);
            bool expectedFilled = FilledKeys.Contains(key);
            Assert.True(expectedFilled == (thickness == 0),
                $"{key}: dolu={expectedFilled} ama StrokeThickness={thickness.ToString(CultureInfo.InvariantCulture)}");
        }
    }

    [StaFact]
    public void Every_icon_key_the_ui_tasks_need_is_declared()
    {
        var icons = IconResources.Load();

        var missing = RequiredKeys.Where(k => !icons.Contains(k)).ToList();
        Assert.Empty(missing);
    }

    [StaFact]
    public void The_restore_caption_glyph_is_two_squares_while_maximize_is_one()
    {
        // [K8] Restore glyph'i tasarımda LİTERAL olarak verilmemiştir; diğer üç caption glyph'iyle aynı
        // 10x10 stroke ızgarasında TÜRETİLİR. Bu test türetmenin iki koşulunu pinler: (a) "iki iç içe kare"
        // anlamı — restore İKİ figür, maximize TEK figür; (b) ızgara — her ikisi de 10x10 kutuya sığar.
        var icons = IconResources.Load();

        var maximize = Assert.IsType<PathGeometry>(icons["Icon.CaptionMaximize"]);
        var restore = Assert.IsType<PathGeometry>(icons["Icon.CaptionRestore"]);

        Assert.Single(maximize.Figures);
        Assert.Equal(2, restore.Figures.Count);

        foreach (var g in new Geometry[] { maximize, restore, (Geometry)icons["Icon.CaptionMinimize"], (Geometry)icons["Icon.CaptionClose"] })
        {
            Assert.True(g.Bounds.Left >= 0 && g.Bounds.Top >= 0, $"10x10 ızgara dışına taştı: {g.Bounds}");
            Assert.True(g.Bounds.Right <= 10 && g.Bounds.Bottom <= 10, $"10x10 ızgara dışına taştı: {g.Bounds}");
        }
    }

    [StaFact]
    public void App_icon_ships_every_frame_windows_scales_from()
    {
        // Windows ikonu ölçek başına AYRI kare olarak ister (tepsi 16, başlık/taskbar 24-32, Alt+Tab 48,
        // mağaza/büyük görünüm 256). Tek kareli bir ICO bulanık ölçeklenir — bu yüzden ≥5 kare pinlenir.
        string path = Path.Combine(RepoPaths.AppSrcRoot, "Assets", "app-icon.ico");
        Assert.True(File.Exists(path), $"çok boyutlu uygulama ikonu yok: {path}");

        var decoder = new IconBitmapDecoder(
            new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        Assert.True(decoder.Frames.Count >= 5, $"ICO yalnız {decoder.Frames.Count} kare taşıyor");
        int[] expected = [16, 24, 32, 48, 256];
        var sizes = decoder.Frames.Select(f => f.PixelWidth).ToList();
        Assert.All(expected, s => Assert.Contains(s, sizes));
    }

    /// <summary>[A13/T3c · c8] <see cref="BuildOrchestrator.App.Shell.AppTrayIcon"/>: tepsi BİLEREK ayrı, tek-kareli
    /// 16px raster kullanır (app-icon.ico'nun otomatik küçültülmesi amber "D"yi bozar — bkz. AppTrayIcon.cs
    /// sınıf özeti). <c>app-icon.ico</c> için kare testi vardı (yukarıda), tepsi ikonu için HİÇ yoktu.
    ///
    /// <para><b>[fix-1 · C4] OTORİTE BU KALEMDE SESSİZDİR</b> (brief kural 7): design-v1 README §6 (Assets)
    /// yalnız <c>delta-logo-dark.svg</c> ve <c>delta-app-icon.svg</c>'yi tanımlar; bir <i>tray</i> ikonundan ya
    /// da 16px şartından SÖZ ETMEZ (§1.4 ikonografi de tepsiye değinmez). Beklenen 16, üretimin KENDİ kararından
    /// gelir (<c>AppTrayIcon</c> + dosya adı <c>tray-icon-16.ico</c>). Bu test bu yüzden bir design-fidelity
    /// kalemi DEĞİL, bir <b>asset-bütünlüğü</b> testidir: shell'in beklediği asset gerçekten repoda ve gerçekten
    /// tek-kareli 16px mi.</para>
    ///
    /// <para><b>Ayırt edicilik (ikili asset):</b> "üretimi boz → kırmızı gör" döngüsü bir <c>.ico</c> için
    /// uygulanamaz (dosyayı bozmak testi assert'ten ÖNCE decoder hatasına düşürür ve kalemi kanıtlamaz);
    /// bunun yerine BEKLENEN DEĞER mutasyona uğratıldı — <c>16 → 32</c> koşuldu ve
    /// <c>Assert.Equal() Failure: Expected 32 / Actual 16</c> ile kırmızı verdi (bkz. task-T3-fix1-report.md),
    /// yani assert gerçekten dosyanın piksel boyutunu okuyor.</para></summary>
    /// <summary>
    /// [design-v1.2.1 §6] İkonlar ÜRÜN markasından üretilir — Delta'nın kendi ikonundan değil.
    ///
    /// <para>İki işaret ayırt edicidir: yeni markada <b>açık gri/beyaz pill şeritler</b> vardır
    /// (<c>#EDEDEE…#A9A9B0</c>), eski Delta ikonunda ise YALNIZ amber "D" + near-black tile vardı — tek bir
    /// açık piksel bile yoktu. Bu yüzden 256px karesinde hem açık şerit pikselleri hem amber chevron
    /// pikselleri aranır.</para>
    ///
    /// <para><b>Ayırt edicilik ölçüldü:</b> eski <c>app-icon.ico</c> git'ten geri alınıp koşuldu ve bu test
    /// kırmızı verdi (açık piksel oranı ~0). Yani assert gerçekten dosyanın içeriğini okuyor.</para></summary>
    [StaFact]
    public void The_app_icon_is_rendered_from_the_product_mark_not_the_company_icon()
    {
        string path = Path.Combine(RepoPaths.AppSrcRoot, "Assets", "app-icon.ico");
        var decoder = new IconBitmapDecoder(
            new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var frame = decoder.Frames.Single(f => f.PixelWidth == 256);
        var bgra = new BitmapImageAdapter(frame);

        Assert.True(bgra.LightPixelRatio > 0.01,
            $"açık şerit pikseli yok ({bgra.LightPixelRatio:P2}) — ikon hâlâ eski (şeritsiz) işaretten olabilir");
        Assert.True(bgra.AmberPixelRatio > 0.01,
            $"amber chevron pikseli yok ({bgra.AmberPixelRatio:P2})");
    }

    /// <summary>İkonların üreteci repoda ve csproj'un işaret ettiği adla duruyor — asset'ler elle
    /// düzenlenmez, buradan yeniden üretilir.</summary>
    [Fact]
    public void The_icon_generator_the_csproj_points_at_exists()
    {
        string generator = Path.Combine(RepoPaths.AppSrcRoot, "Assets", "generate-app-icons.ps1");
        Assert.True(File.Exists(generator), $"ikon üreteci yok: {generator}");

        string csproj = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "BuildOrchestrator.App.csproj"));
        Assert.Contains("generate-app-icons.ps1", csproj, StringComparison.Ordinal);
    }

    /// <summary>Bir ICO karesinin piksellerini okuyup marka işaretlerini sayar (yalnız bu testin ihtiyacı).</summary>
    private sealed class BitmapImageAdapter
    {
        public double LightPixelRatio { get; }
        public double AmberPixelRatio { get; }

        public BitmapImageAdapter(BitmapSource frame)
        {
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0.0);
            int w = converted.PixelWidth, h = converted.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            converted.CopyPixels(px, stride, 0);

            int light = 0, amber = 0, opaque = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                int b = px[i], g = px[i + 1], r = px[i + 2], a = px[i + 3];
                if (a < 128) continue;
                opaque++;
                if (r > 150 && g > 150 && b > 150) light++;             // beyaz/gümüş şeritler
                if (r > 150 && g is > 80 and < 200 && b < 90) amber++;  // amber chevron + amber şerit
            }
            LightPixelRatio = opaque == 0 ? 0 : (double)light / opaque;
            AmberPixelRatio = opaque == 0 ? 0 : (double)amber / opaque;
        }
    }

    [StaFact]
    public void Tray_icon_is_a_16px_square()
    {
        string path = Path.Combine(RepoPaths.AppSrcRoot, "Assets", "tray-icon-16.ico");
        Assert.True(File.Exists(path), $"tepsi ikonu yok: {path}");

        var decoder = new IconBitmapDecoder(
            new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        var frame = Assert.Single(decoder.Frames);
        Assert.Equal(16, frame.PixelWidth);
        Assert.Equal(16, frame.PixelHeight);
    }

    [StaFact]
    public void No_control_depends_on_the_Segoe_MDL2_icon_font()
    {
        // Brief'teki tarama `*.xaml*` idi; hedef "MDL2 kaynak ağacında HİÇBİR YERDE geçmesin" olduğu için
        // `*.cs` de taranır (glyph sabitleri code-behind'da yaşıyordu). bin/obj hariç — bkz. RepoPaths.
        foreach (string file in RepoPaths.AppSourceFiles("*.xaml").Concat(RepoPaths.AppSourceFiles("*.cs")))
            Assert.DoesNotContain("Segoe MDL2", File.ReadAllText(file), StringComparison.Ordinal);
    }

    [StaFact]
    public void No_control_parses_an_icon_geometry_from_a_string_embedded_in_code()
    {
        // [T64 review · fix wave 1] "Dört kaynağı bire indir" iddiasının GERÇEKTEN tuttuğunu pinler: T64
        // sonrasında GraphView hâlâ Icons.xaml ile BİREBİR aynı iki path'i inline sabit olarak taşıyordu ve
        // hiçbir test ayrışmayı görmüyordu. Geometri kodda parse edilmiyorsa ikinci bir doğruluk kaynağı da
        // OLUŞAMAZ (koda gömülü path'in tek giriş kapısı Geometry.Parse'tır — XAML tarafı sözlüğün kendisidir).
        foreach (string file in RepoPaths.AppSourceFiles("*.cs"))
            Assert.DoesNotContain("Geometry.Parse", File.ReadAllText(file), StringComparison.Ordinal);
    }
}
