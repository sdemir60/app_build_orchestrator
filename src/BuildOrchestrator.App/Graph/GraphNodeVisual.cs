using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BuildOrchestrator.App.Graph;

/// <summary>
/// [A13/T5] Bir graf düğümünün TIKLANABİLİR gövdesi — davranışça <see cref="Grid"/>'in ta kendisi, tek fark
/// bir automation peer'ı olmasıdır.
///
/// <para><b>Neden ayrı bir tip (ölçüldü):</b> düz bir panelin peer'ı YOKTUR
/// (<c>UIElementAutomationPeer.CreatePeerForElement(new Grid())</c> → <c>null</c>), yani UIA ağacında
/// kendi öğesi olarak HİÇ GÖRÜNMEZ ve ona verilen bir <c>AutomationProperties.Name</c> ekran okuyucuya ASLA
/// ulaşmaz. Grafın "ekran okuyucuya görünmez" olması bu yüzden tek başına ad eksikliği değildi; düğümün UIA'da
/// bir öğe HÂLİNE gelmesi gerekiyordu.</para>
///
/// <para>Görünüm/ölçü/motion tarafında hiçbir üye override EDİLMEZ.</para>
/// </summary>
internal sealed class GraphNodeBody : Grid
{
    /// <summary>[A13/T5 fix-1] Düğümün <b>TEK</b> etkinleştirme yolu: fare tıklaması da UIA <c>Invoke</c>'u da
    /// bu delegate'ten geçer (iki ayrı seçim mantığı YOK — kopya YASAK). <see cref="GraphView"/> düğümü kurarken
    /// bağlar.</summary>
    internal Action? Activate { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new GraphNodeBodyPeer(this);

    /// <summary>Gövdeye tıklamak düğümü seçer → UIA rolü <see cref="AutomationControlType.Button"/>'dır.
    /// Adı (proje adı + statü) düğüm başına <see cref="GraphView"/> verir.</summary>
    private sealed class GraphNodeBodyPeer(GraphNodeBody owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;

        protected override string GetClassNameCore() => nameof(GraphNodeBody);

        /// <summary>[A13/T5 fix-1] <b>Rol ile YETENEK örtüşür.</b> <see cref="AutomationControlType.Button"/>
        /// rolü UIA istemcisine "bu öğe invoke edilebilir" diye söz verir; pattern'i vermezsek söz boşa çıkar
        /// (ekran okuyucu düğümü duyurur ama etkinleştiremez).</summary>
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Invoke ? this : base.GetPattern(patternInterface);

        /// <summary>WPF'in kendi <c>ButtonAutomationPeer</c> deseni birebir: devre dışıysa
        /// <see cref="ElementNotEnabledException"/>, eylem ise dispatcher'a <c>Input</c> önceliğiyle post edilir
        /// (UIA çağrısı, seçim + kamera animasyonu bitene kadar bloklanmaz).</summary>
        void IInvokeProvider.Invoke()
        {
            if (!IsEnabled()) throw new ElementNotEnabledException();
            if (owner.Activate is { } activate)
                owner.Dispatcher.BeginInvoke(DispatcherPriority.Input, activate);
        }
    }
}

/// <summary>
/// [quiet] Bir düğümün MODELİ + yerleşimdeki yeri + görseli.
///
/// <para><b>Görsel HER ZAMAN vardır.</b> Eski tembel materyalizasyon (cull) kaldırıldı: graf artık her panel
/// boyutunda TAM SIĞDIĞI için varsayılan görünümde her düğüm görünür alandadır ve eleyecek bir şey yoktur.</para>
///
/// <para><see cref="Center"/> <c>init</c> DEĞİLDİR: panel yeniden boyutlandığında yerleşim yeniden hesaplanır
/// ve merkez yerinde güncellenir (görsel yeniden KURULMAZ).</para>
/// </summary>
internal sealed class GraphNodeSlot
{
    public required GraphNode Model { get; set; }
    /// <summary>Düğüm karesinin İÇERİK koordinatlarındaki merkezi.</summary>
    public required Point Center { get; set; }
    public required GraphNodeVisual Visual { get; init; }
}

/// <summary>
/// [quiet] Tek bir graf düğümünün WPF görselleri. Statü değiştiğinde bu parçalar YERİNDE güncellenir —
/// şablon yeniden kurulmaz; panel yeniden boyutlandığında da yalnız ölçü/konum yazılır.
///
/// <para><b>ÜÇ ayrı opaklık taşıyıcısı:</b> <see cref="Cell"/> açılış dalgasının hedefi, <see cref="Body"/>
/// koşu/seçim opaklığının hedefi, <see cref="PulseHost"/> ise building animasyonunun hedefidir. Aynı elemanda
/// olsalardı biri diğerinin değerini ezerdi.</para>
///
/// <para>v1.3.0 §2.3 ile SÖKÜLENLER: <c>Label</c> (node üstü ad etiketi), <c>Badge*</c> (graf içi dep-issue
/// rozeti) ve <c>SquareHost</c> (yalnız rozeti kareden ayırmak için vardı).</para>
/// </summary>
internal sealed class GraphNodeVisual
{
    public required GraphNode Model { get; set; }
    /// <summary>Canvas'a yerleştirilen dış hücre — açılış dalgasının (opacity + yukarıdan) hedefi.</summary>
    public required Grid Cell { get; init; }
    /// <summary>Tıklanabilir gövde — koşu/seçim opaklığının hedefi. [A13/T5] Ekran-okuyucu adını TAŞIYAN öğe
    /// de budur (tıklanan öğe = UIA'da adlanan öğe).</summary>
    public required GraphNodeBody Body { get; init; }
    /// <summary>Halka + kare + ikon kabı — building animasyonunun hedefi.</summary>
    public required Grid PulseHost { get; init; }
    /// <summary>Building animasyonu şu an dönüyor mu — her <c>UpdateStatuses</c> tick'inde animasyonun baştan
    /// başlatılmasını (takılmasını) önler.</summary>
    public bool IsPulsing { get; set; }
    /// <summary>Statü renkli KARE (radius-sm). discovered'ta kesikli çerçeve — WPF Border dashed
    /// desteklemediği için Rectangle.</summary>
    public required Rectangle Square { get; init; }
    /// <summary>Seçiliyken görünen focus-ring halkası (§2.3: 2px outline, offset 2).</summary>
    public required Rectangle SelectionRing { get; init; }
    /// <summary>Lucide <c>box</c> glyph'i — node'un %52'si (§2.3).</summary>
    public required Path Icon { get; init; }
}
