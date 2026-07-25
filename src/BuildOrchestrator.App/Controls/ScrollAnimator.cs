using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ICSharpCode.AvalonEdit;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [T59] WPF'te native smooth scroll YOK — <c>ScrollViewer.VerticalOffset</c> salt-okunurdur (feasibility §3.3,
/// doğrulanmış: dotnet/wpf). Bu, attached DP <c>ScrollAnimator.VerticalOffset</c> + <c>DoubleAnimationUsingKeyFrames</c>
/// (süre Foundation <c>Duration.*</c>'tan, ease Foundation <c>KeySpline.*</c>'tan — çağıran çözer) ile telafi eder.
///
/// <para><b>Hedef türleri:</b> hem <see cref="ScrollViewer"/> (proje listesi — <c>ScrollUnit=Pixel</c> önkoşuluyla,
/// bkz. <c>StickyOverlayTests.List_disables_virtualization_and_scrolls_by_pixel</c>) HEM AvalonEdit
/// <see cref="TextEditor"/> (konsol) — ikisi de <c>ScrollToVerticalOffset(double)</c> sunar; TEK animasyon
/// inşa/kablaj kodu paylaşılır (kopya YASAK).</para>
///
/// <para><b>Wheel iptali:</b> WPF'te kullanıcı animasyon SÜRERKEN tekerleği çevirirse animasyon offset'i fethetmeye
/// devam eder (tarayıcının aksine — orada <c>behavior:'smooth'</c> kullanıcı girdisiyle OTOMATİK iptal olur).
/// <see cref="EnableUserCancellation"/> bunu <c>PreviewMouseWheel</c>'e bağlayıp <c>BeginAnimation(prop, null)</c>
/// ile iptal eder + <see cref="IsUserSuppressedProperty"/> bayrağını kaldırır (follow-mode'un T48 arbitration'ı bu
/// bayrağı tüketir — burada yalnız primitif sağlanır, "ne zaman devam" politikası paket dışı).</para>
///
/// <para><b>Motion sözleşmesi:</b> <paramref name="animationsEnabled"/>/<paramref name="effectiveDuration"/> ÇAĞIRAN
/// tarafından, animasyonun BAŞLADIĞI ANDA <c>App.Motion</c>'dan TAZE okunmalıdır (bkz. <see cref="AnimateTo"/> XML
/// yorumu) — bu sınıf statik <c>App.Motion</c>'a dokunmaz (test edilebilirlik, D8).</para>
/// </summary>
public static class ScrollAnimator
{
    public static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "VerticalOffset", typeof(double), typeof(ScrollAnimator),
            new PropertyMetadata(0.0, OnVerticalOffsetChanged));

    public static double GetVerticalOffset(DependencyObject d) => (double)d.GetValue(VerticalOffsetProperty);
    public static void SetVerticalOffset(DependencyObject d, double value) => d.SetValue(VerticalOffsetProperty, value);

    /// <summary>Son kullanıcı-tetikli (wheel) iptalden bu yana kimse yeni bir <see cref="AnimateTo"/> BAŞLATMADI mı —
    /// follow-mode/T48 arbitration'ın "kullanıcıyla dövüşme" bayrağı. <see cref="AnimateTo"/> her çağrıldığında
    /// false'a döner (yeni bir programatik hareket, kullanıcı müdahalesi ARTIK geçerli değil).</summary>
    public static readonly DependencyProperty IsUserSuppressedProperty =
        DependencyProperty.RegisterAttached("IsUserSuppressed", typeof(bool), typeof(ScrollAnimator), new PropertyMetadata(false));

    public static bool GetIsUserSuppressed(DependencyObject d) => (bool)d.GetValue(IsUserSuppressedProperty);
    private static void SetIsUserSuppressed(DependencyObject d, bool value) => d.SetValue(IsUserSuppressedProperty, value);

    /// <summary>Bir scroll host'a (ScrollViewer/TextEditor) BİR KEZ bağlanır: kullanıcı tekerleği çevirdiği ANDA
    /// uçuştaki animasyonu iptal eder + suppress bayrağını kaldırır (feasibility §3.3 — WPF'in tarayıcıdan eksik
    /// olduğu "kullanıcı girdisi otomatik iptal eder" davranışının elle karşılığı).</summary>
    public static void EnableUserCancellation(UIElement scrollHost) =>
        scrollHost.PreviewMouseWheel += (_, _) => CancelForUser(scrollHost);

    /// <summary>Uçuştaki animasyonu (varsa) <c>BeginAnimation(prop, null)</c> ile iptal eder + suppress bayrağını
    /// kaldırır. <see cref="EnableUserCancellation"/>'ın wheel kancası bunu çağırır; testler de doğrudan çağırabilir.
    /// <c>BeginAnimation</c> <see cref="UIElement"/> üzerinde tanımlıdır (ScrollViewer/TextEditor ikisi de UIElement).</summary>
    public static void CancelForUser(UIElement target)
    {
        target.BeginAnimation(VerticalOffsetProperty, null);
        SetIsUserSuppressed(target, true);
    }

    /// <summary>[E4 fix] Kullanıcı-suppress bayrağını AÇIKÇA (yeni bir <see cref="AnimateTo"/> başlatmadan) temizler —
    /// <see cref="CancelForUser"/>'ın (bayrağı KURAN) simetriği. Frontier follow'un "kullanıcı dibe/frontier'e geri
    /// döndü → takip sürsün" resume tetiği (StickyLayerList) bunu çağırır: <see cref="AnimateTo"/>'yu tetiklemeden
    /// suppress'i düşürür, gerçek re-engagement bir sonraki follow tick'inde FollowScrollController'ın throttle/
    /// dead-band'ine kalır (uçuştaki tekerlekle dövüşmez).</summary>
    public static void ClearUserSuppressed(UIElement target) => SetIsUserSuppressed(target, false);

    /// <summary>
    /// <paramref name="target"/>'ı (ScrollViewer veya AvalonEdit TextEditor) <paramref name="targetOffset"/>'e
    /// kaydırır. <paramref name="animationsEnabled"/> false ya da <paramref name="effectiveDuration"/> ≤0 iken
    /// (reduced-motion) ANINDA (animasyonsuz) atlar — motion sözleşmesi. Aksi halde Foundation'dan gelen süre +
    /// KeySpline ile bir <c>DoubleAnimationUsingKeyFrames</c>/<c>SplineDoubleKeyFrame</c> uygular ("yer değiştirme"
    /// = ease-in-out, design-v1 §1.3).
    ///
    /// <para><b>ÇAĞIRAN SÖZLEŞMESİ:</b> <paramref name="animationsEnabled"/>/<paramref name="effectiveDuration"/>
    /// çağrı ANINDA <c>App.Motion?.AnimationsEnabled</c> / <c>App.Motion.Effective(baseDuration)</c>'dan TAZE
    /// okunmalı (cache'lenmiş bir değer DEĞİL) — bkz. <see cref="Services.IMotionSettings"/> TÜKETİM SÖZLEŞMESİ.</para>
    ///
    /// <para>Döner: bir animasyon BAŞLATILDIYSA true, anında atlandıysa false (BottomAnchorBehavior'ın 560ms
    /// "jumping" penceresini yalnız GERÇEKTEN animasyon varken açması için).</para>
    /// </summary>
    public static bool AnimateTo(UIElement target, double currentOffset, double targetOffset,
        bool animationsEnabled, TimeSpan effectiveDuration, KeySpline keySpline)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(keySpline);

        SetIsUserSuppressed(target, false); // yeni programatik hareket — önceki kullanıcı iptali artık geçersiz
        SetVerticalOffset(target, currentOffset); // DP'nin taban değerini GERÇEK konuma tohumla — klips/zıplama yok

        if (!animationsEnabled || effectiveDuration <= TimeSpan.Zero)
        {
            PushOffset(target, targetOffset);
            SetVerticalOffset(target, targetOffset);
            return false;
        }

        target.BeginAnimation(VerticalOffsetProperty, BuildAnimation(targetOffset, effectiveDuration, keySpline));
        return true;
    }

    /// <summary>Saf fabrika — testler doğrudan çağırıp hedef/süre/eğriyi WPF clock'u hiç tetiklemeden doğrulayabilir.
    /// [T63] Gövde <see cref="MotionTokens.SplineTo"/>'ya taşındı (graf kamerası AYNI şekli kullanır — kopya YASAK).</summary>
    internal static DoubleAnimationUsingKeyFrames BuildAnimation(double to, TimeSpan duration, KeySpline keySpline)
        => MotionTokens.SplineTo(to, duration, keySpline);

    private static void OnVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        PushOffset(d, (double)e.NewValue);

    private static void PushOffset(DependencyObject d, double value)
    {
        switch (d)
        {
            case ScrollViewer sv: sv.ScrollToVerticalOffset(value); break;
            case TextEditor te: te.ScrollToVerticalOffset(value); break;
        }
    }
}
