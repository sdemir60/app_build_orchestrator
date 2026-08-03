using BuildOrchestrator.App.Services;

namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [A13/T4 fix-1 · C4] <c>BuildOrchestrator.App.App.Motion</c> statik seam'inin geçici set/restore'u — TEK yer.
/// Öncesinde <c>MotionOwnerHygieneTests.AssertSubscribesOnce</c> ve (T4'te eklenen) <c>PopoverTests</c>'in pop-in
/// testi aynı iki satırı (set + finally'de restore) AYRI AYRI yazıyordu — statik geri-yükleme mantığının iki
/// yerde yaşaması, tam da A12'nin ders verdiği tipte bir sızıntı riskidir (biri unutulur, diğeri güncellenmez).
///
/// <para><c>using</c>-scope, eski <c>try/finally</c> ile DAVRANIŞ olarak AYNIDIR (<see cref="IDisposable.Dispose"/>,
/// istisna durumunda dahi finally gibi çalışır) — konsolidasyon davranışı SESSİZCE değiştirmez.</para>
/// </summary>
internal static class MotionScope
{
    public static IDisposable Enable(IMotionSettings motion)
    {
        IMotionSettings original = BuildOrchestrator.App.App.Motion;
        BuildOrchestrator.App.App.Motion = motion;
        return new Restorer(original);
    }

    private sealed class Restorer(IMotionSettings original) : IDisposable
    {
        public void Dispose() => BuildOrchestrator.App.App.Motion = original;
    }
}
