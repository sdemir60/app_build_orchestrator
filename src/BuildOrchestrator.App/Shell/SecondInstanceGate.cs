namespace BuildOrchestrator.App.Shell;

/// <summary>[E2/FIX1] İkinci instance mevcut pencereyi öne getirme DENEMESİNİN sonucundan (<c>activated</c>) türeyen
/// KARAR: bir tray balloon gösterilecek mi ve hangi çıkış koduyla kapanılacak. Saftır (WPF/tray/process İÇERMEZ)
/// ki <see cref="App.OnStartup"/> dışında test edilebilsin.</summary>
internal readonly record struct SecondInstanceOutcome(bool ShowBalloon, int ExitCode);

/// <summary>[E2/FIX1] İkinci-instance kararının TEK yeri. Öne getirebildiyse SESSİZ ve temiz kapan (balloon yok,
/// kod 0); getiremediyse SESSİZ KALMA — balloon iste ve AYRIŞAN çıkış koduyla kapan (<see
/// cref="App.SecondInstanceActivationFailedExitCode"/>) ki normal ikinci-instance (0) ile makine tarafında
/// ayırt edilebilsin.
/// <para>[P3 · Task 4] Balloon isteği <see cref="ShellSwitches.ShowNotifications"/>'a tabidir: <paramref
/// name="store"/> YALNIZ öne getirme BAŞARISIZ olduğunda okunur (C#'ın ternary'sinin tembel dalı — öne
/// getirildiyse store'a hiç dokunulmaz, bkz. <c>SecondInstanceGateTests.ThrowingStore</c>). Ayrışan çıkış kodu,
/// balloon bastırılsa bile DEĞİŞMEZ — makine ayrımı kullanıcı bildirim tercihinden bağımsızdır.</para></summary>
internal static class SecondInstanceGate
{
    public static SecondInstanceOutcome Decide(bool activated, IUiStateStore store) =>
        activated
            ? new SecondInstanceOutcome(ShowBalloon: false, ExitCode: 0)
            : new SecondInstanceOutcome(
                ShowBalloon: ShellSwitches.ShowNotifications(store.Load()),
                ExitCode: App.SecondInstanceActivationFailedExitCode);
}
