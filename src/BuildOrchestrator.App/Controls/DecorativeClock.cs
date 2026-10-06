using System.Windows;
using System.Windows.Media.Animation;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// Sahibinin ELİNDE tuttuğu dekoratif (sonsuz) animasyon saati — satır nefesi, dönen halka, şerit süpürmesi, grafın beads
/// yörüngeleri ve seçim kenarı akışı bunu kullanır. Kurmanın ve BIRAKMANIN tek yolu: bırakılan saat hedeflerinden sökülür
/// VE zaman ağacından çıkarılır.
///
/// <para><b>Neden <c>BeginAnimation</c> değil (ÖLÇÜLDÜ):</b> <c>BeginAnimation(dp, null)</c> saati yalnız özellikten SÖKER
/// (WPF <c>AnimationStorage.ClearAnimations</c> → <c>DetachAnimationClock</c> yalnız dinleyicileri çözer); saat zaman
/// ağacının kökünde <c>Active</c> kalır ve bir <c>AnimationClock</c> aktifken her karede tik ister (<c>NeedsTicksWhenActive</c>).
/// Ağaç yetimi zayıf referansla tutar, yani onu ancak bir GC bırakır — boşta duran süreçte o GC hiç gelmez. Görünür
/// pencerede biten bir koşunun ardından durdurulan nefes, halka, süpürme ve beads saatleri bu yüzden tepside de dönmeye devam
/// etti: koşu sürerken gizlenen pencere tepside boşta 13 Mdöngü/s iken, koşu görünürken bitip 3 s sonra gizlenen pencere
/// 118 Mdöngü/s ölçüldü (hedef ≤ 40) — gizli pencerede her tik kanalı yeniden gönderir ve UI thread'i ekranın tazeleme
/// hızında uyanır. Çözüm <c>CursorClock</c>'un zaten yaptığıdır: saat sahibinde durur, bırakılırken
/// <c>Controller.Remove</c> ile ağaçtan çıkarılır. Kanıt: <c>HiddenShellClockTests</c>, <c>HiddenDecorativeClockTests</c>.</para>
///
/// <para>Saat birden çok hedefe uygulanabilir (grafın yörüngeleri ve kenarları tek saati paylaşır: faz kilitli, düğüm başına
/// ayrı sonsuz animasyon yok); <see cref="Stop"/> hepsini söker. Görünürlük ve reduced-motion kapıları SAHİPTEDİR — bu sınıf
/// yalnız kurar ve bırakır.</para>
/// </summary>
internal sealed class DecorativeClock
{
    private readonly List<(IAnimatable Target, DependencyProperty Property)> _targets = [];
    private AnimationClock? _clock;

    /// <summary>Saat şu an kurulu mu (dönüyor).</summary>
    public bool IsRunning => _clock is not null;

    /// <summary>[test yüzeyi] Kurulu saat; yoksa <c>null</c>.</summary>
    internal AnimationClock? Clock => _clock;

    /// <summary>Saati kurar; hedefler <see cref="Attach"/> ile sonradan bağlanır (graf: saat önce, o anki ve sonraki yörüngeler
    /// sonra). Zaten kuruluysa <paramref name="timeline"/> yok sayılır — dönen bir animasyon baştan almaz (ritim sıfırlanmaz);
    /// yeniden kurmak isteyen önce <see cref="Stop"/> der.</summary>
    public void Start(AnimationTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        _clock ??= timeline.CreateClock(); // kök saat: kurulduğu anda zaman ağacına girer ve başlar
    }

    /// <summary>Saati kurar ve hedefe uygular — tek hedefli sahiplerin (nefes, halka, süpürme) tek satırı.</summary>
    public void Start(AnimationTimeline timeline, IAnimatable target, DependencyProperty property)
    {
        Start(timeline);
        Attach(target, property);
    }

    /// <summary>Kurulu saati bir hedefe daha uygular (yeni bir yörünge, yeni bir kenar). Saat yoksa ya da hedef zaten
    /// bağlıysa hiçbir şey yapmaz.</summary>
    public void Attach(IAnimatable target, DependencyProperty property)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(property);
        if (_clock is null || _targets.Contains((target, property))) return;
        target.ApplyAnimationClock(property, _clock);
        _targets.Add((target, property));
    }

    /// <summary>Saati her hedeften söker ve zaman ağacından ÇIKARIR; kurulu değilse no-op. Hedefin taban değerini sahibi
    /// yazar (sökülen özellik tabanına döner).</summary>
    public void Stop()
    {
        if (_clock is not { } clock) return;
        foreach (var (target, property) in _targets) target.ApplyAnimationClock(property, null);
        _targets.Clear();
        _clock = null;
        clock.Controller?.Remove();
    }
}
