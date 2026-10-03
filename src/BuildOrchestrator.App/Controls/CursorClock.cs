using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [perf B4 · karar 4] <b>İmleçlerin TEK paylaşımlı saati.</b> Konsol prompt'unun ve event stream aktif satırının
/// imleçleri kırpma (<see cref="MotionTokens.CreateBlinkAnimation"/>) ve renk turu (<see cref="CursorHop"/>) için
/// artık kendi saatini kurmaz; pencerenin TEK saat çiftine bağlanır.
///
/// <para><b>Neden (ÖLÇÜLDÜ):</b> ön planda boşta işlemci maliyetinin en büyük payı imleç saatleriydi — iki imleç ×
/// iki sonsuz saat = dört saat — ve pencere başka bir pencerenin arkasındayken de dönüyorlardı. Artık (a) iki imleç
/// AYNI fazda kırpar ve renk turu ikisi için bir kez adım atar: saatler dörtten ikiye iner; (b) saatler YALNIZ
/// pencere aktifken koşar — imlecin yalnız etkin pencerede kırpması Windows geleneğidir.</para>
///
/// <para><b>Saat ne zaman koşar:</b> en az bir bağlı imleç GÖRÜNÜRKEN <b>ve</b> pencere aktifken. İki kapı
/// birbirinden bağımsızdır ve ikisi de saati durdurur. Görünürlük kapısı başlatıcıların İÇİNDE kalır
/// (<c>HiddenCursorClockTests</c>: başlatıcılar tepsideyken de koşar, kapı çağıranlarda olsaydı bir sonraki olay
/// saati geri kurardı) — bu sınıf yalnız bağlı imlecin kendi <c>IsVisible</c> değişimine ek olarak tepki verir.
/// Saat durunca her imleç SABİT kalır: opaklık 1, renk dinlenme rengi (<see cref="CursorHop.Stop"/> deseni);
/// kırpmanın sönük karesinde donmaz.</para>
///
/// <para><b>Pencere aktifliği nasıl gelir:</b> yalnız <see cref="SetWindowActive"/> ile (üretimde <c>MainWindow</c>'un
/// Activated/Deactivated olayları). Görünümler <c>Window.IsActive</c>'i KENDİLERİ okumaz: başsız test pencereleri
/// etkin olmayabilir ve imleç hiç kırpmazdı. Sinyal gelmemiş pencere "aktif" sayılır.</para>
///
/// <para><b>Saat pencere BAŞINA tektir</b> (<see cref="For"/>): kabuktaki iki görünüm aynı pencerede olduğundan aynı
/// saati bulur; her test kendi pencerelerini kurduğundan süreç geneli paylaşılan durum yoktur (WPF saatleri
/// <c>DispatcherObject</c>'tir — süreç geneli tek örnek, başsız süitte thread'ler arası erişime düşerdi).</para>
///
/// <para><b>Reduced motion:</b> başlatıcılar hareket kapalıyken <see cref="Attach"/> çağırmaz, <see cref="Detach"/>
/// çağırır — saat HİÇ kurulmaz, imleç sabit kalır (<c>MotionGate</c> sözleşmesi değişmez).</para>
/// </summary>
internal sealed class CursorClock
{
    // Pencere → saat. Zayıf anahtar: pencere ölünce saat de gider.
    private static readonly ConditionalWeakTable<DependencyObject, CursorClock> s_byWindow = new();

    // İmleç → bağlı olduğu saat. Detach, görünüm ağaçtan çıkmışken (Unloaded) de DOĞRU saati bulmalıdır: o anda
    // pencere artık çözülemez ve For() imleci başka bir saatte arardı.
    private static readonly ConditionalWeakTable<Shape, CursorClock> s_byCursor = new();

    private readonly List<Attachment> _attached = [];
    private bool _windowActive = true;
    private AnimationClock? _blinkClock;
    private AnimationClock? _colorClock;

    /// <summary>[test yüzeyi] Pencerenin son bildirilen aktifliği (sinyal gelmediyse <c>true</c>).</summary>
    internal bool WindowActive => _windowActive;

    /// <summary>[test yüzeyi] Saate bağlı imleç sayısı — pencere aktif olmasa da bağlı kalırlar.</summary>
    internal int AttachedCount => _attached.Count;

    /// <summary>[test yüzeyi] Bağlı imleçlerin opaklığını ŞU AN süren TEK kırpma saati; saat durmuşsa <c>null</c>.</summary>
    internal AnimationClock? ActiveBlinkClock => _blinkClock;

    /// <summary>Öğenin penceresine ait saat. Henüz bir pencerede olmayan öğe (ör. ctor'daki pencerenin kendisi) kendi
    /// anahtarıdır: pencere gösterilince <c>GetWindow</c> ona aynı pencereyi döndürür, yani anahtar değişmez.</summary>
    internal static CursorClock For(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return s_byWindow.GetValue(Window.GetWindow(element) ?? element, static _ => new CursorClock());
    }

    /// <summary>
    /// İmleci pencerenin saatine bağlar; saat koşmuyorsa (ilk imleç, ya da pencere az önce aktifleşti) başlatır.
    /// <b>İdempotenttir:</b> bu yol her stream olayında ve her konsol prompt tazelemesinde koşar — bağlı imleç için
    /// pencereyi yeniden çözmez, saati ve fırçayı yeniden kurmaz (ritim sıfırlanmaz, imleç ilk renkte takılmaz).
    /// </summary>
    /// <param name="restKey">İmlecin dinlenme rengi (token anahtarı). Saat imleci bıraktığında (pencere aktif değil,
    /// görünmez) imleç bu renge döner; event stream'in ton kanalı değiştiği için her seferinde TAZE okunur.</param>
    internal static void Attach(Shape cursor, FrameworkElement host, Func<string> restKey)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(restKey);
        if (s_byCursor.TryGetValue(cursor, out var owner)) { owner.Reconcile(); return; }
        var clock = For(host);
        s_byCursor.Add(cursor, clock);
        clock.Add(cursor, host, restKey);
    }

    /// <summary>
    /// İmleci saatten ayırır ve SABİT bırakır: opaklık 1, renk <paramref name="restKey"/>. Hiç bağlanmamış imleç için
    /// de aynı sonucu verir (reduced-motion yolu hiç <see cref="Attach"/> çağırmaz) — idempotenttir. Son bağlı imleç
    /// de ayrıldıysa saatler durur.
    /// </summary>
    internal static void Detach(Shape cursor, string restKey)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(restKey);
        if (s_byCursor.TryGetValue(cursor, out var owner))
        {
            s_byCursor.Remove(cursor);
            owner.Remove(cursor);
        }
        Rest(cursor, restKey);
        owner?.Reconcile();
    }

    /// <summary>
    /// Pencerenin aktifliğini bildirir (<c>MainWindow</c> Activated/Deactivated). Pasifleşince saatler durur ve
    /// bağlı her imleç sabit kalır; aktifleşince taze saatlerle, İKİ imleç aynı fazda yeniden başlar.
    /// </summary>
    internal void SetWindowActive(bool active)
    {
        if (_windowActive == active) return;
        _windowActive = active;
        Reconcile();
    }

    private void Add(Shape cursor, FrameworkElement host, Func<string> restKey)
    {
        var attachment = new Attachment(this, cursor, host, restKey);
        _attached.Add(attachment);
        // Görünürlük sırası: pencere gösterilirken görünümün IsVisibleChanged'i imlecinkinden ÖNCE ateşlenebilir
        // (imleç o anda henüz görünür değildir). İmlecin kendi değişimi dinlenmezse saat bir sonraki olaya kadar
        // kalkmazdı.
        cursor.IsVisibleChanged += attachment.OnVisibleChanged;
        Reconcile();
    }

    private void Remove(Shape cursor)
    {
        for (int i = 0; i < _attached.Count; i++)
        {
            if (!ReferenceEquals(_attached[i].Cursor, cursor)) continue;
            cursor.IsVisibleChanged -= _attached[i].OnVisibleChanged;
            _attached.RemoveAt(i);
            return;
        }
    }

    /// <summary>Saatlerin durumunu bağlı imleçlerle ve pencere aktifliğiyle eşitler — tek karar noktası.</summary>
    private void Reconcile()
    {
        FrameworkElement? visibleHost = null;
        for (int i = 0; i < _attached.Count && visibleHost is null; i++)
            if (_attached[i].Cursor.IsVisible) visibleHost = _attached[i].Host;

        if (_windowActive && visibleHost is not null)
        {
            if (_blinkClock is null) StartClocks(visibleHost);
            for (int i = 0; i < _attached.Count; i++)
                if (!_attached[i].Applied) Apply(_attached[i]);
            return;
        }

        for (int i = 0; i < _attached.Count; i++)
            if (_attached[i].Applied) Release(_attached[i]);
        StopClocks();
    }

    private void StartClocks(FrameworkElement host)
    {
        // Kırpma ve renk turu zaman çizelgeleri MotionTokens/CursorHop'ta kurulur (renk çizelgesinin tek kurucusu
        // MotionTokens'tır); burada yalnız SAAT yaratılır. Palet çözülemezse renk turu kurulmaz, kırpma yine döner.
        _blinkClock = MotionTokens.CreateBlinkAnimation().CreateClock();
        _colorClock = CursorHop.CreateClock(host.TryFindResource);
        // İkisi AYNI anda başlar: renk adımı sınırları kırpmanın dibine düşer (CursorHop.PhaseMs); ayrı anlarda
        // başlasalar faz kayardı.
        _blinkClock.Controller?.Begin();
        _colorClock?.Controller?.Begin();
    }

    private void StopClocks()
    {
        // Remove: kök saat zaman ağacından TAMAMEN çıkarılır. Yalnız durdurulan bir kök saat ağaçta kalır ve her
        // pencere aktivasyonunda bir çift daha birikirdi.
        _blinkClock?.Controller?.Remove();
        _colorClock?.Controller?.Remove();
        _blinkClock = null;
        _colorClock = null;
    }

    private void Apply(Attachment attachment)
    {
        attachment.Applied = true;
        attachment.Cursor.ApplyAnimationClock(UIElement.OpacityProperty, _blinkClock);
        if (_colorClock is not null) CursorHop.Start(attachment.Cursor, _colorClock);
    }

    private static void Release(Attachment attachment)
    {
        attachment.Applied = false;
        Rest(attachment.Cursor, attachment.RestKey());
    }

    /// <summary>Sabit imleç: saat sökülür, opaklık 1 (sönük karede donmaz), renk dinlenme rengine döner.</summary>
    private static void Rest(Shape cursor, string restKey)
    {
        cursor.ApplyAnimationClock(UIElement.OpacityProperty, null);
        cursor.Opacity = 1.0;
        CursorHop.Stop(cursor, restKey);
    }

    private sealed class Attachment(CursorClock owner, Shape cursor, FrameworkElement host, Func<string> restKey)
    {
        internal Shape Cursor { get; } = cursor;
        internal FrameworkElement Host { get; } = host;
        internal Func<string> RestKey { get; } = restKey;

        /// <summary>Saat ŞU AN bu imlece uygulanmış mı (idempotent Attach'ın ve Release'in bekçisi).</summary>
        internal bool Applied { get; set; }

        internal void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => owner.Reconcile();
    }
}
