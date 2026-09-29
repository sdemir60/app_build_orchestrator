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
            KeyboardShortcuts.ResolveEsc(dialogOpen: true, popoverOrMenuOpen: true, hasSelection: true));

    [Fact]
    public void Esc_closes_popovers_before_clearing_selection()
        => Assert.Equal(EscAction.ClosePopovers,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: true, hasSelection: true));

    [Fact]
    public void Esc_clears_selection_when_nothing_else_is_open()
        => Assert.Equal(EscAction.ClearSelection,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: true));

    [Fact]
    public void Esc_does_nothing_when_no_layer_is_open()
        => Assert.Equal(EscAction.None,
            KeyboardShortcuts.ResolveEsc(dialogOpen: false, popoverOrMenuOpen: false, hasSelection: false));
}
