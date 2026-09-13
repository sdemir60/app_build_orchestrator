namespace BuildOrchestrator.Tests.App;

/// <summary>
/// [Kopya YASAK — final review round 2, F1 follow-up] <c>[StaFact]</c>'in runner'ı (Xunit.StaFact)
/// <c>Xunit.SkipException</c>'ı TANIMAZ: bir <c>Skip.If</c>/<c>Skip.IfNot</c> bu attribute altında atılırsa
/// Skipped yerine sessizce Failed üretir (doğrulandı: xunit.execution 2.9.3 derlemesinde "SkipException" hiç
/// geçmiyor). Bu yüzden WPF/STA gövdesi gerektiren AMA dinamik Skip de kullanması gereken bir test metodu ne
/// salt <c>[StaFact]</c> ne salt <c>[SkippableFact]</c> olabilir: test metodu <c>[SkippableFact]</c> (Skip
/// çağrısı dahil) kalır, gövde ise burada YENİ bir STA thread'de koşturulur.
///
/// <para>Bu, üç ayrı test sınıfının (<c>DragReorderTests</c>, <c>AppShutdownTests</c>,
/// <c>TrayOverlayMeasurementTests</c>) bağımsız yazdığı AYNI kalıbın TEK ortak yeridir — sonuç/istisna
/// <see cref="TaskCompletionSource{TResult}"/> ile (tip DEĞİŞMEDEN) çağıran thread'e taşınır; thread
/// <c>IsBackground</c>'dur (regresyonda asılı kalsa bile test host'unu kilitlemez). <c>AppShutdownTests</c>
/// bunun üstüne KENDİ farkını (kurulu ama pompalanmayan bir <c>DispatcherSynchronizationContext</c>) gövdenin
/// İÇİNDE, bu yardımcıyı çağırmadan ÖNCE kurar — o fark burada YOKTUR, yalnız STA thread + TCS mekaniği
/// ortaktır.</para>
/// </summary>
internal static class StaThread
{
    /// <summary>Gövdeyi yeni, ayrı bir STA thread'de koşturur ve sonucu döner.</summary>
    public static Task<T> RunAsync<T>(Func<T> body, string? name = null)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(body()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = name ?? "sta-thread" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    /// <summary>Dönüş değeri olmayan gövdeler için — <see cref="RunAsync{T}"/> üzerinden akar (kopya yok).</summary>
    public static Task RunAsync(Action body, string? name = null) =>
        RunAsync<object?>(() => { body(); return null; }, name);
}
