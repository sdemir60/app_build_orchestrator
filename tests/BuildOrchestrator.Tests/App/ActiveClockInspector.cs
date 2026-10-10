using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// Bir dispatcher'ın WPF zamanlama ağacındaki (<c>MediaContext.TimeManager</c>) KÖK saatleri SAHİPLERİYLE listeler —
/// "pencere gizlendi, hangi animasyon saati durmadı" sorusunun tek cevap yeri.
///
/// <para><b>Neden reflection:</b> WPF saat tik'lerini sahibine bağlayan hiçbir genel API yoktur. Bir <c>dotnet-trace</c>
/// atfı yalnız <c>TimeManager.Tick</c> → <c>ClockGroup.ComputeTreeState</c> gösterir, uygulama çerçevesi taşımaz
/// (ölçüldü: <c>.claude/outputs/2026-10-06-00-14-desktop-measurements-final.md</c> §2). Sahip, saatin
/// <c>CurrentTimeInvalidated</c> aboneliklerinden okunur: bir özelliğe uygulanan her <see cref="AnimationClock"/>'a WPF'in
/// <c>AnimationStorage</c>'ı abone olur ve o storage hedef nesneyi (<c>_dependencyObject</c>) ile özelliği
/// (<c>_dependencyProperty</c>) tutar. Fırça/dönüşüm gibi bir <see cref="Freezable"/> hedef ise onu taşıyan öğe
/// <c>InheritanceContext</c>'ten bulunur ve yol görsel ağaçta uygulamanın kendi görünümüne dek yukarı yürür.</para>
///
/// <para><b>Hangi saat döngüyü uyanık tutar:</b> WPF yalnız <see cref="ClockState.Active"/> kök saat için her karede tik
/// ister (<c>Clock.ComputeCurrentState</c>: aktif ve olay dinleyicisi olan saatte <c>_nextTickNeededTime = 0</c>);
/// <c>DesiredFrameRate</c> taşıyan saat bu isteği kendi kare ızgarasına yuvarlar. Dolan (<see cref="ClockState.Filling"/>)
/// saat tik istemez. Bu yüzden testlerin baktığı ölçü <see cref="ClockInfo.State"/>'tir; <see cref="NextTickNeeded"/>
/// ise ağacın bütününün cevabıdır (negatif = kimse tik istemiyor, render döngüsü uyur).</para>
/// </summary>
internal static class ActiveClockInspector
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly Assembly PresentationCore = typeof(Visual).Assembly;
    private static readonly Type MediaContextType = PresentationCore.GetType("System.Windows.Media.MediaContext", throwOnError: true)!;
    private static readonly Type TimeManagerType = PresentationCore.GetType("System.Windows.Media.Animation.TimeManager", throwOnError: true)!;
    private static readonly Type StorageType = PresentationCore.GetType("System.Windows.Media.Animation.AnimationStorage", throwOnError: true)!;

    private static readonly MethodInfo MediaContextFrom = Require(MediaContextType.GetMethod("From", BindingFlags.Static | BindingFlags.NonPublic, [typeof(Dispatcher)]), "MediaContext.From");
    private static readonly PropertyInfo TimeManagerOfContext = Require(MediaContextType.GetProperty("TimeManager", Hidden), "MediaContext.TimeManager");
    private static readonly PropertyInfo RootOfTimeManager = Require(TimeManagerType.GetProperty("TimeManagerClock", Hidden), "TimeManager.TimeManagerClock");
    private static readonly MethodInfo NextTickOfTimeManager = Require(TimeManagerType.GetMethod("GetNextTickNeeded", Hidden), "TimeManager.GetNextTickNeeded");
    private static readonly PropertyInfo RootChildren = Require(typeof(ClockGroup).GetProperty("InternalRootChildren", Hidden), "ClockGroup.InternalRootChildren");
    private static readonly FieldInfo NextTickOfClock = Require(typeof(Clock).GetField("_nextTickNeededTime", Hidden), "Clock._nextTickNeededTime");
    private static readonly FieldInfo HandlersOfClock = Require(typeof(Clock).GetField("_eventHandlersStore", Hidden), "Clock._eventHandlersStore");
    private static readonly object TimeInvalidatedKey = Require(typeof(Timeline).GetField("CurrentTimeInvalidatedKey", BindingFlags.Static | BindingFlags.NonPublic), "Timeline.CurrentTimeInvalidatedKey").GetValue(null)!;
    // Dinleyici deposunun tipi (EventHandlersStore) alanın kendi tipinden alınır — hangi assembly'de durduğu framework sürümüne göre değişir.
    private static readonly MethodInfo StoreGet = Require(HandlersOfClock.FieldType.GetMethod("Get", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(EventPrivateKey)]), "EventHandlersStore.Get");
    private static readonly FieldInfo StorageObject = Require(StorageType.GetField("_dependencyObject", Hidden), "AnimationStorage._dependencyObject");
    private static readonly FieldInfo StorageProperty = Require(StorageType.GetField("_dependencyProperty", Hidden), "AnimationStorage._dependencyProperty");
    private static readonly PropertyInfo InheritanceContext = Require(typeof(DependencyObject).GetProperty("InheritanceContext", Hidden), "DependencyObject.InheritanceContext");

    private static T Require<T>(T? member, string name) where T : MemberInfo =>
        member ?? throw new MissingMemberException($"WPF internal '{name}' not found — the inspector needs updating for this framework build.");

    /// <summary>Bir kök saat: zaman çizelgesi, durumu, tik isteği, kare hızı tavanı, ilerlemesi ve SAHİBİ (hedef öğe.özellik).</summary>
    internal sealed record ClockInfo(Clock Clock, string Timeline, ClockState State, TimeSpan? NextTick, int? FrameRate, double? Progress, string Owner)
    {
        public override string ToString() =>
            $"{Timeline} state={State} nextTick={(NextTick is { } t ? t.ToString() : "none")} fps={FrameRate?.ToString() ?? "full"} " +
            $"progress={(Progress is { } p ? p.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "-")} owner={Owner}";
    }

    /// <summary>Dispatcher'ın zamanlama ağacındaki kök saatler (WPF'in zayıf referanslı kök listesi; toplanmış olanlar atlanır).</summary>
    public static IReadOnlyList<ClockInfo> RootClocks(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        var root = (ClockGroup)RootOfTimeManager.GetValue(TimeManagerOf(dispatcher))!;
        var result = new List<ClockInfo>();
        foreach (var weak in (List<WeakReference>)RootChildren.GetValue(root)!)
            if (weak.Target is Clock clock) result.Add(Describe(clock));
        return result;
    }

    /// <summary>Yalnız ŞU AN dönen kök saatler — render döngüsünü uyanık tutanlar.</summary>
    public static IReadOnlyList<ClockInfo> ActiveRootClocks(Dispatcher dispatcher) =>
        [.. RootClocks(dispatcher).Where(c => c.State == ClockState.Active)];

    /// <summary>Zamanlama ağacının bir sonraki tik isteği: negatif = hiçbir saat tik istemiyor (<c>TimeManager.GetNextTickNeeded</c>).</summary>
    public static TimeSpan NextTickNeeded(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        return (TimeSpan)NextTickOfTimeManager.Invoke(TimeManagerOf(dispatcher), null)!;
    }

    /// <summary>Assertion mesajı için satır satır döküm.</summary>
    public static string Describe(IEnumerable<ClockInfo> clocks) =>
        string.Join(Environment.NewLine, clocks.Select(c => "  " + c));

    private static object TimeManagerOf(Dispatcher dispatcher) =>
        TimeManagerOfContext.GetValue(MediaContextFrom.Invoke(null, [dispatcher]))!;

    private static ClockInfo Describe(Clock clock) => new(
        clock, TimelineLabel(clock.Timeline), clock.CurrentState, (TimeSpan?)NextTickOfClock.GetValue(clock),
        Timeline.GetDesiredFrameRate(clock.Timeline), clock.CurrentProgress, OwnerOf(clock));

    private static string TimelineLabel(Timeline timeline)
    {
        string duration = timeline.Duration.HasTimeSpan
            ? timeline.Duration.TimeSpan.TotalMilliseconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "ms"
            : timeline.Duration.ToString();
        string repeat = timeline.RepeatBehavior == RepeatBehavior.Forever ? " forever" : "";
        return $"{timeline.GetType().Name}({duration}{repeat})";
    }

    /// <summary>Saatin sahipleri: bir grupta (Storyboard) çocuklarınki; yaprakta <c>CurrentTimeInvalidated</c> dinleyicilerinin
    /// hedefi — WPF'in kendi storage'ı ise "öğe yolu.özellik", başka bir dinleyici ise tipi ve metodu.</summary>
    private static string OwnerOf(Clock clock)
    {
        if (clock is ClockGroup group)
            return "group[" + string.Join("; ", group.Children.Select(OwnerOf)) + "]";
        if (HandlersOfClock.GetValue(clock) is not { } store) return "(no handlers)";
        if (StoreGet.Invoke(store, [TimeInvalidatedKey]) is not Delegate handler) return "(no CurrentTimeInvalidated handler)";

        var owners = new List<string>();
        foreach (var listener in handler.GetInvocationList())
        {
            if (listener.Target is { } storage && StorageType.IsInstanceOfType(storage))
            {
                var target = (StorageObject.GetValue(storage) as WeakReference)?.Target as DependencyObject;
                var property = StorageProperty.GetValue(storage) as DependencyProperty;
                owners.Add($"{PathOf(target)}.{property?.Name}");
            }
            else owners.Add($"{listener.Target?.GetType().Name}.{listener.Method.Name}");
        }
        return string.Join(" | ", owners);
    }

    /// <summary>Hedefin yolu: fırça/dönüşümse onu taşıyan öğe, sonra ağaçta yukarı — uygulamanın kendi görünümüne (ya da pencereye)
    /// ulaşınca durur; adsız bir sahip zinciri bile hangi görünümde olduğunu söyler.</summary>
    private static string PathOf(DependencyObject? target)
    {
        if (target is null) return "(collected)";
        var parts = new List<string>();
        for (var current = target; current is not null && parts.Count < 12; current = ParentOf(current))
        {
            parts.Add(Label(current));
            if (parts.Count > 1 && IsAppView(current)) break;
        }
        return string.Join(" < ", parts);
    }

    private static string Label(DependencyObject d) =>
        d is FrameworkElement { Name.Length: > 0 } named ? $"{d.GetType().Name}#{named.Name}" : d.GetType().Name;

    private static bool IsAppView(DependencyObject d) =>
        d is UserControl or Window && d.GetType().Assembly == typeof(BuildOrchestrator.App.MainWindow).Assembly;

    private static DependencyObject? ParentOf(DependencyObject d)
    {
        if (d is Visual or Visual3D && VisualTreeHelper.GetParent(d) is { } visualParent) return visualParent;
        if (LogicalTreeHelper.GetParent(d) is { } logicalParent) return logicalParent;
        return InheritanceContext.GetValue(d) as DependencyObject; // Freezable (fırça, dönüşüm): onu taşıyan öğe
    }
}
