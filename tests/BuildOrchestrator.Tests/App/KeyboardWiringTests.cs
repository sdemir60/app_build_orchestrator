using System.Windows.Input;
using BuildOrchestrator.App.Console;
using BuildOrchestrator.App.Services;
using BuildOrchestrator.App.Shell;
using BuildOrchestrator.App.ViewModels;
using BuildOrchestrator.Tests.Supervisor;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [E5/T46 · Fix Wave 1] Kısayol KABLAJININ (pure gate'in ALTINDAKİ katman) regresyon guard'ı. Saf
/// <see cref="KeyboardShortcuts.Resolve"/>/<see cref="KeyboardShortcuts.ResolveEsc"/> zaten 15 testle pinli
/// (<see cref="KeyboardShortcutTests"/>); AMA o enum kararlarını GERÇEK tuş bağlamalarına ve VM komutlarına
/// bağlayan katman (MainWindow.SetupKeyboardShortcuts / DispatchShortcut) test edilmiyordu — takas edilmiş bir
/// switch arm'ı ya da yanlış modifier HİÇBİR testi kırmadan uygulamayı bozardı (F5-koşarken Build gibi). Bu
/// sınıf o kablajı SAF seam'ler üzerinden pinler: <see cref="KeyboardShortcuts.CommandFor"/> (aksiyon→VM komutu)
/// ve <see cref="KeyboardShortcuts.WindowBindings"/> (tuş+modifier→niyet).
/// </summary>
[Collection("Console UI (serial)")] // NewVm EngineHost/VM kurar — diğer WPF StaFact'larla seri (kaynak çekişmesi deseni)
public class KeyboardWiringTests
{
    private static ConsoleBatcher NeverTickingBatcher() => new(_ => Task.Delay(Timeout.Infinite));

    private static RunViewModel NewVm() =>
        new(new EngineHost(TestPaths.SupervisorExe), NeverTickingBatcher(), () => "r1") { RootPath = @"D:\repo" };

    // ------------------------------------------------------------------ aksiyon → VM komutu (DispatchShortcut kablajı)
    [StaFact]
    public void Command_for_each_shortcut_action_maps_to_the_matching_vm_command()
    {
        var vm = NewVm();
        // Referans-eşitlik: takas edilmiş bir arm (ör. Stop→BuildCommand) burada YAKALANIR — VM komut property'leri
        // aynı örneği (lazy backing field) döndürdüğünden ReferenceEquals kesin ayrıştırır.
        Assert.Same(vm.BuildCommand, KeyboardShortcuts.CommandFor(ShortcutAction.Build, vm));
        Assert.Same(vm.RebuildCommand, KeyboardShortcuts.CommandFor(ShortcutAction.Rebuild, vm));
        Assert.Same(vm.StopCommand, KeyboardShortcuts.CommandFor(ShortcutAction.Stop, vm));
        // FocusFilter bir VM komutu DEĞİL (MainWindow ayrı ele alır); None de bağlı değil.
        Assert.Null(KeyboardShortcuts.CommandFor(ShortcutAction.FocusFilter, vm));
        Assert.Null(KeyboardShortcuts.CommandFor(ShortcutAction.None, vm));
    }

    // ------------------------------------------------------------------ tuş+modifier → niyet (SetupKeyboardShortcuts kablajı)

    /// <summary>
    /// [About] ESKİ İDDİA: "tabloda TAM 5 satır var". O sayı bir bütçe değil, günün kısayol kümesinin
    /// negatif-pin'iydi (yanlışlıkla eklenen bir bağlamayı yakalamak için). About ekranı F1'i ekledi, yani
    /// KURAL BİLEREK DEĞİŞTİ: satır sayısı 6'dır ve tablo artık <see cref="WindowIntent.ShowAbout"/>'u da
    /// taşır. Negatif-pin'in NİYETİ korunuyor — sayı tablodan türetilmiyor, açıkça yazılıyor ki fazladan ya da
    /// kayıp bir bağlama yine kırsın.
    /// </summary>
    [Fact]
    public void The_window_binding_table_maps_each_key_gesture_to_the_correct_intent()
    {
        // Single: tam olarak BİR satır eşleşmezse (yanlış/eksik modifier veya tuş) fırlatır → yanlış kablaj kırar.
        WindowIntent Intent(Key key, ModifierKeys mods) =>
            KeyboardShortcuts.WindowBindings.Single(b => b.Key == key && b.Modifiers == mods).Intent;

        Assert.Equal(WindowIntent.Rebuild, Intent(Key.F5, ModifierKeys.Control));     // Ctrl+F5  → Rebuild
        Assert.Equal(WindowIntent.Rebuild, Intent(Key.F5, ModifierKeys.Shift));       // Shift+F5 → Rebuild
        Assert.Equal(WindowIntent.F5StateBranch, Intent(Key.F5, ModifierKeys.None));  // çıplak F5 → duruma-dallı
        Assert.Equal(WindowIntent.FocusFilter, Intent(Key.F, ModifierKeys.Control));  // Ctrl+F   → filtre odağı
        Assert.Equal(WindowIntent.ShowAbout, Intent(Key.F1, ModifierKeys.None));      // F1       → About
        Assert.Equal(WindowIntent.Escape, Intent(Key.Escape, ModifierKeys.None));     // Esc      → katman zinciri

        // Negatif-pin: tabloda TAM 6 satır — fazladan/kayıp bir bağlama (ör. yanlışlıkla eklenen Ctrl+P) kırar.
        Assert.Equal(6, KeyboardShortcuts.WindowBindings.Count);
    }
}
