using BuildOrchestrator.App.Shell;

namespace BuildOrchestrator.App;

/// <summary>
/// [pencere monitörü] Pencere en son hangi monitörde kapandıysa uygulama yeniden açılınca orada, tam ekran açılır (kural ve yerleştirme:
/// <see cref="WindowMonitor"/>). Kayıt kalıcı durumdadır (<see cref="UiState.LastMonitor"/>) ve yalnız GERÇEK kapanışta yazılır — Exit, Restart
/// to update, Close to tray kapalıyken ×, oturum kapanışı; hepsi <c>OnClosing</c>'in Close dalından geçer. Tepsiye inmek bir şey yazmaz:
/// pencere kapanmaz, yerinde gizlenir ve geri çağrılınca aynı monitörde çıkar.
/// <para>Okuma kapanış kararı verilirken yapılır: <c>OnClosed</c>'da HWND artık yoktur (ölçüldü: tanıtıcı 0).</para>
/// </summary>
public partial class MainWindow
{
    /// <summary>[test dikişi] Pencerenin şu an bulunduğu monitörün kimliği; <c>null</c> ⇒ kayıt değişmez. Üretim değeri
    /// <see cref="SetupMonitor"/>'da kurulur ve yalnız bu oturumda gösterilmiş pencereden okur (<c>IsLoaded</c> ilk gösterimden sonra doğrudur,
    /// tepsiye inmek onu düşürmez): tepside başlayıp hiç açılmadan kapanan bir oturum, Windows'un o gün gizli pencereye seçtiği ekranı
    /// kullanıcının en son gördüğü monitörün yerine yazmaz. Headless süitte pencerenin HWND'si yoktur; testler okumayı buradan verir.</summary>
    internal Func<string?> CurrentMonitorKey { get; set; } = null!;

    /// <summary>Ctor'dan bir kez: hatırlanan monitör bağlıysa pencere orada tam ekran açılmak üzere normal doğar
    /// (<see cref="WindowMonitor.OpenOn"/>); değilse XAML'deki gibi doğuştan maximized kalır ve yeri Windows seçer.</summary>
    private void SetupMonitor(UiState saved)
    {
        CurrentMonitorKey = () => IsLoaded ? WindowMonitor.KeyOf(this) : null;
        if (WindowMonitor.Remembered(saved.LastMonitor) is { } monitor) WindowMonitor.OpenOn(this, monitor);
    }

    /// <summary>Gerçek kapanışta pencerenin monitörü kaydedilir; okunamazsa ya da değişmediyse dosyaya dokunulmaz.</summary>
    private void RememberMonitor()
    {
        if (CurrentMonitorKey() is not { } key) return;
        var state = _uiState.Load();
        if (state.LastMonitor == key) return;
        state.LastMonitor = key;
        _uiState.Save(state);
    }
}
