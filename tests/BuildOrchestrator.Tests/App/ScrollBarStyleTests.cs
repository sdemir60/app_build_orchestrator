using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [SCROLLBAR] Resources/Controls.xaml SCROLLBAR bölümü — design-v1 `.bo-scroll` sözleşmesini
/// (BuildApp.jsx:35-38) pinler: 10px ray, şeffaf track, ok butonu YOK, thumb = Brush.Neutral700 +
/// 3px içerlek hap. Implicit stil olduğu için ŞABLON SINIRLARINI geçmesi ayrıca kanıtlanır (ScrollViewer ve
/// AvalonEdit üzerinden) — bir Style'ın varlığını okumak, gerçek ScrollViewer'ların onu giydiğini kanıtlamaz.
/// Kontroller GERÇEKTEN kurulur (DsControlTemplateTests deseni).
///
/// <para>[SCROLLBAR-HOVER] Hap ARTIK statik değil: fare RAY'a girdiğinde 4px'ten 8px'e genişler ve rampada
/// bir basamak açılır; sürüklerken bir basamak daha açılır. Kavrama hedefi 10px'lik ray olduğundan tetik de
/// ray'ındır (thumb'ın kendi <c>IsMouseOver</c>'ı DEĞİL). <b>WPF'in <c>IsMouseOver</c>'ı headless'ta
/// sürülemez</b> (gerçek imleç ister) — bu yüzden hover/drag testleri trigger'ı OBJE MODELİNDEN bulur ve
/// setter'larını WPF'in yapacağı gibi uygular: ölçülen şey hap'ın GERÇEK sonucu (Margin + renk), sadece
/// trigger'ın varlığı değil.</para>
/// </summary>
[Collection("Console UI (serial)")] // WPF StaFact çekişme flake'i — bkz. ConsoleUiSerialCollection
public class ScrollBarStyleTests
{
    private static ScrollBar NewVerticalBar() => new()
    {
        Orientation = Orientation.Vertical,
        Height = 150,
        Minimum = 0,
        Maximum = 100,
        ViewportSize = 20,
        Value = 10,
    };

    private static Thumb ThumbOf(ScrollBar bar) => DsResources.Descendants(bar).OfType<Thumb>().Single();

    private static Border PillOf(Thumb thumb) => DsResources.Descendants(thumb).OfType<Border>().Single();

    /// <summary>Bir trigger'ın setter'larını WPF'in uyguladığı gibi uygular. <c>{DynamicResource …}</c> değerleri
    /// setter üzerinde ÇÖZÜLMEMİŞ durur (bir <see cref="DynamicResourceExtension"/>'dır) — hedefin kaynak
    /// kapsamından burada çözülür, tıpkı WPF'in yaptığı gibi.</summary>
    private static void Apply(FrameworkElement target, TriggerBase trigger)
    {
        var setters = trigger switch
        {
            Trigger t => t.Setters,
            DataTrigger d => d.Setters,
            _ => throw new InvalidOperationException($"desteklenmeyen trigger: {trigger.GetType().Name}"),
        };
        foreach (var setter in setters.Cast<Setter>())
        {
            object? value = setter.Value is DynamicResourceExtension dynamic
                ? target.FindResource(dynamic.ResourceKey)
                : setter.Value;
            target.SetValue(setter.Property, value);
        }
        target.UpdateLayout();
    }

    /// <summary>Ray hover'ı: thumb stilindeki, thumb'ı DOĞURAN <see cref="ScrollBar"/>'ın
    /// <c>IsMouseOver</c>'ına bağlı DataTrigger. Bağın ŞEKLİ de iddianın parçasıdır — thumb'ın kendi
    /// hover'ına düşürülürse test görür.</summary>
    private static DataTrigger RailHoverTrigger(Thumb thumb) =>
        thumb.Style.Triggers.OfType<DataTrigger>().Single(t =>
            t.Binding is Binding binding
            && binding.Path?.Path == "IsMouseOver"
            && binding.RelativeSource?.Mode == RelativeSourceMode.TemplatedParent
            && Equals(t.Value?.ToString(), "True"));

    private static Trigger DraggingTrigger(Thumb thumb) =>
        thumb.Style.Triggers.OfType<Trigger>().Single(t => t.Property == Thumb.IsDraggingProperty);

    [StaFact]
    public void Vertical_rail_is_10px_and_the_thumb_is_a_neutral700_pill_inset_by_3px()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);

        // BuildApp.jsx:36 — ::-webkit-scrollbar { width: 10px }.
        Assert.Equal(10.0, bar.ActualWidth);

        // BuildApp.jsx:38 — thumb: neutral-700 zemin, 3px şeffaf kenar → 4px'lik hap.
        var thumb = ThumbOf(bar);
        var pill = PillOf(thumb);
        Assert.Equal(new Thickness(3), pill.Margin);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral700"), DsResources.ColorOf(pill.Background));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [SCROLLBAR-HOVER] Hap HER İKİ genişlikte de tam kapsüldür: yarıçap boyanan genişliğin YARISI. Kenar
    /// artık değişken olduğundan (3 → 1) yarıçap da onunla akar — BuildApp.jsx:38'in "dış 5 − kenar" formülü.
    ///
    /// <para><b>Eski iddia:</b> <c>pill.CornerRadius == 2</c>, şablonda SABİT. Sabit 3px kenar döneminde
    /// doğruydu. <b>Neden değişti:</b> tek bir sayı iki genişliğe birden yetmez.</para>
    ///
    /// <para><b>Neden "sabit 4 verip Border kırpsın" YETMEZ (ÖLÇÜLDÜ):</b> WPF taşan yarıçapı yatayda kırpar,
    /// dikeyde kırpmaz — 4px genişliğinde 30px yüksekliğindeki bir hapta yarıçap 4, yarıçap 2'ye göre 18
    /// pikselde (maks. delta 68/255) farklı boyanır: uçlar yarım daire değil ELİPS olur. Aşağıdaki iki iddia
    /// o kestirmeyi de kapatır — sabit 4 dingin hâlde, sabit 2 hover'da düşer.</para>
    /// </summary>
    [StaFact]
    public void The_pill_is_a_capsule_at_both_widths()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);
        var thumb = ThumbOf(bar);
        var pill = PillOf(thumb);

        Assert.Equal(new CornerRadius(2), pill.CornerRadius);              // dingin: 4px hap
        Assert.Equal(pill.ActualWidth / 2, pill.CornerRadius.TopLeft);

        Apply(thumb, RailHoverTrigger(thumb));

        Assert.Equal(new CornerRadius(4), pill.CornerRadius);              // hover: 8px hap
        Assert.Equal(pill.ActualWidth / 2, pill.CornerRadius.TopLeft);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [SCROLLBAR-HOVER] Fare RAY'a girince hap 4px'ten 8px'e genişler (kenar 3 → 1) ve rampada bir basamak
    /// açılır. İkisi TEK trigger'dadır: genişleyip parlamayan ya da parlayıp genişlemeyen bir ara durum yoktur.
    /// </summary>
    [StaFact]
    public void Hovering_the_rail_widens_the_pill_to_8px_and_lightens_it_one_step()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);
        var thumb = ThumbOf(bar);
        var pill = PillOf(thumb);

        Apply(thumb, RailHoverTrigger(thumb));

        Assert.Equal(new Thickness(1), pill.Margin);            // 10 − 2×1 = 8px hap
        Assert.Equal(8.0, pill.ActualWidth);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral600"), DsResources.ColorOf(pill.Background));
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [SCROLLBAR-HOVER] Trigger'ın BAĞI gerçekten çözülüyor ve gerçekten RAY'ı buluyor mu? Bu, yukarıdaki
    /// testin göremediği tek şeydir: orada setter'lar ELLE uygulanır, yani bağ hiç değerlenmez.
    ///
    /// <para><b>Neden gerekli (ÖLÇÜLDÜ):</b> ilk kablaj <c>AncestorType={x:Type ScrollBar}</c> kullanıyordu ve
    /// bağ HİÇ çözülmüyordu — <c>FindAncestor</c> MANTIKSAL ağacı yürür, thumb'ın ebeveyni ise
    /// <see cref="Track"/>'tir ve zincir orada kopar. Setter'ları elle uygulayan testler yeşildi, uygulamada
    /// hover ölü olurdu. Bu yüzden trigger'ın KENDİ Binding nesnesi bir sonda özelliğine takılır ve KAYNAĞIN
    /// KİMLİĞİ sorulur — çözülemeyen bağ <c>null</c> bırakır, yanlış kaynak (ör. Track) yanlış nesne getirir.</para>
    /// </summary>
    [StaFact]
    public void The_rail_hover_binding_really_finds_the_scrollbar_from_inside_the_thumb_template()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);
        var thumb = ThumbOf(bar);
        var railBinding = (Binding)RailHoverTrigger(thumb).Binding;

        // Kaynak nesnenin KENDİSİ (Path yok): ray'ın ta kendisi olmalı — Track ya da başka bir ara öğe değil.
        BindingOperations.SetBinding(thumb, FrameworkElement.TagProperty,
            new Binding { RelativeSource = railBinding.RelativeSource });
        Assert.Same(bar, thumb.Tag);

        // Ve trigger'ın okuduğu yol o kaynakta gerçekten bir değer veriyor (null DEĞİL, kutulanmış false).
        BindingOperations.SetBinding(thumb, FrameworkElement.TagProperty, railBinding);
        Assert.Equal(bar.IsMouseOver, thumb.Tag);
        Assert.NotNull(thumb.Tag);
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [SCROLLBAR-HOVER] Sürükleme hover'dan AYRI bir basamaktır (Neutral500) ve hap geniş kalır.
    ///
    /// <para><b>Eski iddia:</b> drag da hover ile aynı <c>Brush.Neutral600</c>'a giderdi — yani hap'ı
    /// yakaladığın an hiçbir şey değişmezdi. Yeni kural rampanın üçüncü basamağını kullanır.</para>
    /// </summary>
    [StaFact]
    public void Dragging_the_pill_lightens_it_one_further_step_and_keeps_it_wide()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);
        var thumb = ThumbOf(bar);
        var pill = PillOf(thumb);

        Apply(thumb, DraggingTrigger(thumb));

        Assert.Equal(new Thickness(1), pill.Margin);
        Assert.Equal(DsResources.TokenColor(host, "Brush.Neutral500"), DsResources.ColorOf(pill.Background));
        Assert.NotEqual(DsResources.TokenColor(host, "Brush.Neutral600"),
                        DsResources.TokenColor(host, "Brush.Neutral500")); // iki basamak gerçekten farklı
        GC.KeepAlive(window);
    }

    /// <summary>
    /// [SCROLLBAR-HOVER] Genişleme PAT DİYE değil, renk geçişiyle AYNI kapıdan akar: reduced-motion'da anında
    /// yazılır, animasyonlar açıkken <c>Duration.Fast</c> boyunca sürülür. Kanıt <c>IsAnimated</c>'dır — değeri
    /// doğrudan yazan bir uygulama testi yeşile boyayamaz.
    /// </summary>
    [StaFact]
    public void The_widening_runs_through_the_motion_gate_not_as_a_jump()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);
        var thumb = ThumbOf(bar);
        var pill = PillOf(thumb);

        // Reduced-motion (headless varsayılanı: App.Motion null) — anında, animasyonsuz.
        DsTransition.SetAnimatedPadding(thumb, new Thickness(1));
        Assert.False(DependencyPropertyHelper.GetValueSource(thumb, Control.PaddingProperty).IsAnimated);
        thumb.UpdateLayout();
        Assert.Equal(new Thickness(1), pill.Margin);

        // Animasyonlar açıkken AYNI atama bir animasyon clock'u kurar. Clock BİR SONRAKİ tick'te bağlanır —
        // ScrollAnimatorTests'teki desen: pompala, bekleme değil (FillBehavior.HoldEnd → bitince de animated
        // kalır, yani 120ms'lik yarış yoktur).
        using (MotionScope.Enable(new MotionSettings(new FakeMotionSignal { AnimationsEnabled = true })))
        {
            DsTransition.SetAnimatedPadding(thumb, new Thickness(3));
            DispatcherPump.PumpUntil(
                () => DependencyPropertyHelper.GetValueSource(thumb, Control.PaddingProperty).IsAnimated,
                TimeSpan.FromSeconds(2));
            Assert.True(DependencyPropertyHelper.GetValueSource(thumb, Control.PaddingProperty).IsAnimated);
        }
        GC.KeepAlive(window);
    }

    [StaFact]
    public void Horizontal_rail_is_10px()
    {
        var host = DsResources.NewHost();
        var bar = new ScrollBar
        {
            Orientation = Orientation.Horizontal,
            Width = 200,
            Minimum = 0,
            Maximum = 100,
            ViewportSize = 20,
        };
        var window = DsResources.Realize(host, bar);

        Assert.Equal(10.0, bar.ActualHeight); // BuildApp.jsx:36 — height: 10px
        Assert.Single(DsResources.Descendants(bar).OfType<Thumb>());
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_rail_has_no_arrow_buttons_only_two_transparent_page_areas()
    {
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);

        // webkit scrollbar'ında buton çizilmez (BuildApp.jsx:36-38 yalnız track+thumb tanımlar): ok glyph'i
        // (Path) HİÇ yok; ray'ın boş alanı = 2 şeffaf sayfa-atlama RepeatButton'ı (davranış korunur).
        // (Şekil tipleri NİTELENDİRİLİR: `using System.Windows.Shapes` System.IO.Path'i belirsizleştirirdi.)
        Assert.Empty(DsResources.Descendants(bar).OfType<System.Windows.Shapes.Path>());
        var pageAreas = DsResources.Descendants(bar).OfType<RepeatButton>().ToList();
        Assert.Equal(2, pageAreas.Count);
        Assert.All(pageAreas, b => Assert.Equal(Colors.Transparent, DsResources.ColorOf(
            DsResources.Descendants(b).OfType<Border>().Single().Background)));
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_scrollviewer_gets_the_ds_bar_through_its_default_template()
    {
        // Implicit stilin ŞABLON SINIRINI geçtiğinin kanıtı: ScrollBar'ı biz değil, ScrollViewer'ın
        // default şablonu kurar (üretimdeki StickyLayerList/EventStream yolu budur).
        var host = DsResources.NewHost();
        var viewer = new ScrollViewer
        {
            Width = 200,
            Height = 120,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new Border { Height = 1000 },
        };
        var window = DsResources.Realize(host, viewer);

        var bar = DsResources.Descendants(viewer).OfType<ScrollBar>()
            .Single(b => b.Orientation == Orientation.Vertical);
        Assert.Equal(Visibility.Visible, bar.Visibility);
        Assert.Equal(10.0, bar.ActualWidth);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void A_disabled_bar_collapses_its_track()
    {
        // Kaydıracak şey yokken (IsEnabled=false) boş koyu hap kalıntısı görünmez — restraint.
        var host = DsResources.NewHost();
        var bar = NewVerticalBar();
        var window = DsResources.Realize(host, bar);

        var track = DsResources.Descendants(bar).OfType<Track>().Single();
        Assert.Equal(Visibility.Visible, track.Visibility);

        bar.IsEnabled = false;
        bar.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, track.Visibility);
        GC.KeepAlive(window);
    }

    [StaFact]
    public void The_console_editor_realizes_ds_bars_and_a_transparent_corner()
    {
        // Console'un GERÇEK yolu: AvalonEdit TextEditor → iç ScrollViewer → ScrollBar'lar. Implicit stilin
        // AvalonEdit şablonunun içine de ulaştığı ve iki bar'ın kesiştiği köşe karesinin (Corner) şeffaf
        // olduğu burada, üretimdekiyle aynı kontrol üzerinden kanıtlanır.
        var host = DsResources.NewHost();
        var editor = new ICSharpCode.AvalonEdit.TextEditor
        {
            Width = 260,
            Height = 100,
            WordWrap = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Text = string.Join(Environment.NewLine, Enumerable.Repeat(new string('x', 400), 60)),
        };
        var window = DsResources.Realize(host, editor);

        var bars = DsResources.Descendants(editor).OfType<ScrollBar>().ToList();
        Assert.Equal(10.0, bars.Single(b => b.Orientation == Orientation.Vertical).ActualWidth);
        Assert.Equal(10.0, bars.Single(b => b.Orientation == Orientation.Horizontal).ActualHeight);

        // Default ScrollViewer şablonundaki köşe karesi ControlBrushKey'i DynamicResource ile okur —
        // Controls.xaml'deki override onu şeffaflaştırır (açık-tema grisi koyu konsolda parlamaz).
        // Kimlik YAPISAL olarak kurulur: şablondaki x:Name="Corner" örnekte Name olarak GÖRÜNMEZ (ölçüldü),
        // ama kare iki bar'ın kesiştiği hücrededir (Grid satır 1 / sütun 1) ve şablondaki TEK Rectangle'dır.
        var corner = DsResources.Descendants(editor).OfType<System.Windows.Shapes.Rectangle>()
            .Single(r => Grid.GetRow(r) == 1 && Grid.GetColumn(r) == 1);
        Assert.Equal(Colors.Transparent, DsResources.ColorOf(corner.Fill));
        GC.KeepAlive(window);
    }

    [Fact]
    public void Console_view_declares_auto_visibility_for_both_bars()
    {
        // BuildApp.jsx:616 konsol kutusu overflow AUTO'dur — bar yalnız gerektiğinde görünür. AvalonEdit'in
        // default'u Visible olduğundan ConsoleView bunu AÇIKÇA Auto'ya çevirmek zorundadır (kaynak pinlenir;
        // ConsoleView pack URI'siz headless kurulamadığı için realize DEĞİL kaynak taraması kullanılır —
        // NoHardcodedColorTests ile aynı yaklaşım).
        string xaml = File.ReadAllText(Path.Combine(RepoPaths.AppSrcRoot, "Console", "ConsoleView.xaml"));
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml);
        Assert.Contains("HorizontalScrollBarVisibility=\"Auto\"", xaml);
    }
}
