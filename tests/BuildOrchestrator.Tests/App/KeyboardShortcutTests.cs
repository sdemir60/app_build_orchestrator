using System.Windows.Input;
using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E5/T46 · kullanıcı kararı 2026-09-29] Pencere kısayollarının SAF tablosu (<see cref="KeyboardShortcuts"/>):
/// F5'in yalnız Build'e bağlı olması, tablo dışında kalan jestlerin NEGATİF-PIN'leri (kalkan eski kısayollar,
/// çift-Shift YOK, Ctrl+P YOK) ve Esc zincirinin KATMAN sırası burada pinlenir. WPF gerekmez (enum'lar
/// WindowsBase) → hızlı [Fact].
/// </summary>
public class KeyboardShortcutTests
{
    // ------------------------------------------------------------------ F5 yalnız Build

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> ESKİ İDDİA (v7 K6, <c>BuildApp.jsx:1305</c>): çıplak F5
    /// duruma göre dallanırdı — boştayken Build, koşu sürerken STOP; Ctrl+F5 ve Shift+F5 Rebuild'di (koşarken bile,
    /// CanExecute reddederdi). Bu dallanmayı saf <c>KeyboardShortcuts.Resolve</c> verirdi ve altı test onu pinlerdi
    /// (idle/koşarken/stopped F5, Ctrl+F5, Shift+F5, koşarken Ctrl+F5); Ctrl+F ve çıplak F de aynı fonksiyondan
    /// sınanırdı.
    ///
    /// <para><b>Neden değişti:</b> VS'te F5 koşan bir şeyi ASLA durdurmaz ve Shift+F5 Stop Debugging'dir — VS
    /// alışkanlığıyla "durdur" diye basılan Shift+F5 burada Rebuild başlatıyordu (ölçüldü: kullanıcının VS'i Default
    /// şemada). Build tam biterken "durdur" için basılan F5 de yeni bir build başlatıyordu. Artık F5 YALNIZ Build'e
    /// bağlıdır — koşarken Build'in kapısı kapalıdır, yani hiçbir şey olmaz; Rebuild F6'da, Clean F7'de, durdurma
    /// Esc'tedir. <c>Resolve</c> kalktı: karar tablonun kendisidir (Ctrl+F'in niyeti
    /// <see cref="KeyboardWiringTests"/>'in tablo testinde, kalkan jestler aşağıdaki negatif-pin'de).</para>
    /// </summary>
    [Fact]
    public void F5_is_bound_to_build_only()
        => Assert.Equal(WindowIntent.Build, KeyboardShortcuts.WindowBindings.Single(b => b.Key == Key.F5).Intent);

    // ------------------------------------------------------------------ NEGATİF-PIN'ler

    /// <summary>Tablo DIŞINDAKİ jestler bağlı değildir: kalkan eski kısayollar (Ctrl+F5 / Shift+F5 — eski Rebuild;
    /// Ctrl+F1 — eski What's new), çıplak F, Ctrl+P ve tek başına Shift ("çift-Shift" bir komut paleti açmaz).</summary>
    [Theory]
    [InlineData(Key.F5, ModifierKeys.Control)]
    [InlineData(Key.F5, ModifierKeys.Shift)]
    [InlineData(Key.F1, ModifierKeys.Control)]
    [InlineData(Key.F, ModifierKeys.None)]
    [InlineData(Key.P, ModifierKeys.Control)]
    [InlineData(Key.LeftShift, ModifierKeys.Shift)]
    [InlineData(Key.RightShift, ModifierKeys.Shift)]
    public void Gestures_outside_the_table_are_not_bound(Key key, ModifierKeys modifiers)
        => Assert.DoesNotContain(KeyboardShortcuts.WindowBindings, b => b.Key == key && b.Modifiers == modifiers);

    // ------------------------------------------------------------------ Esc zinciri (katman sırası)
    [Fact]
    public void Esc_closes_the_dialog_first_even_when_lower_layers_are_open()
        => Assert.Equal(EscAction.CloseDialog,
            KeyboardShortcuts.ResolveEsc(dialogOpen: true, popoverOrMenuOpen: true, hasSelection: true, EscRunState.Stoppable));

    [Fact]
    public void Esc_closes_popovers_before_clearing_selection()
        => Assert.Equal(EscAction.ClosePopovers,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: true, hasSelection: true, EscRunState.Stoppable));

    [Fact]
    public void Esc_clears_selection_when_nothing_else_is_open()
        => Assert.Equal(EscAction.ClearSelection,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: true, EscRunState.Idle));

    /// <summary>[kullanıcı kararı 2026-09-29] Koşu katmanı zincirin EN ALTINDADIR: koşu sürerken bir proje seçiliyse
    /// ilk Esc seçimi bırakır (koşunun anlatısına dönülür), durdurmaz.</summary>
    [Fact]
    public void Esc_clears_the_selection_before_it_touches_a_running_build()
        => Assert.Equal(EscAction.ClearSelection,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: true, EscRunState.Stoppable));

    /// <summary>
    /// <b>[DEĞİŞEN KURAL — kullanıcı kararı 2026-09-29]</b> ESKİ İDDİA: açık katman yoksa Esc hiçbir şey yapmazdı —
    /// koşu sürerken de. Artık zincirin son halkası çalışan Build/Rebuild/Clean'dir: Esc onu DURDURUR (graceful;
    /// biten projeler kaydedilir, sonraki Build kaldığı yerden devam eder, çift basma gerekmez).
    /// </summary>
    [Fact]
    public void Esc_stops_a_running_build_when_no_layer_is_open()
        => Assert.Equal(EscAction.StopRun,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: false, EscRunState.Stoppable));

    /// <summary>[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-03] ESKİ İDDİA (kullanıcı kararı 2026-09-29): durdurma zaten
    /// sürüyorsa ikinci bir stop GİTMEZ — Esc yalnız "duyuldu" der (şerit vurgusu). GEREKÇE: drain en yavaş projenin kalan
    /// süresi kadar sürebilir; ikinci Esc beklemek istemediğini söyler ve hard stop'tur ("Stop now"). "Duyuldu" vurgusunun
    /// yerini konsol satırı ve düğmenin "Terminating…" hâli aldı.</summary>
    [Fact]
    public void Esc_while_stopping_resolves_to_StopNow()
        => Assert.Equal(EscAction.StopNow,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: false, EscRunState.Stopping));

    /// <summary>[kullanıcı kararı 2026-09-29] Durdurulamayan bir iş (Sync, Deep Clean, Optimize, checkout, pull)
    /// sürerken Esc sessiz kalmaz — neden durmadığını söyler.</summary>
    [Fact]
    public void Esc_explains_an_operation_that_cannot_be_stopped()
        => Assert.Equal(EscAction.ExplainUnstoppable,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: false, EscRunState.Unstoppable));

    [Fact]
    public void Esc_does_nothing_when_no_layer_is_open_and_nothing_runs()
        => Assert.Equal(EscAction.None,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: false, EscRunState.Idle));
}
