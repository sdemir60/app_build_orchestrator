namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [perf B4 · karar 5] İmleç saatinin ÜÇÜNCÜ kapısı: <b>girdi yokken imleçler durur.</b> Windows'un kendi imleç kuralı —
/// imleç son klavye/fare girdisinden belli bir süre sonra kırpmayı bırakır (<c>SPI_GETCARETTIMEOUT</c>, varsayılan 5 s) ve ilk
/// girdide yeniden başlar; aktifleşme de girdi sayılır.
///
/// <para><b>Neden (ÖLÇÜLDÜ):</b> ön planda etkin pencerede boşta işlemci maliyetinin neredeyse tamamı iki imlecin kırpma +
/// renk turu saatinin WPF render döngüsünü ayakta tutmasından geliyordu: imleçler dönerken 181 Mdöngü/s (UI 67 + render 69 +
/// GPU sürücüsü 40), kırpma 15 fps'e indirilince 180 (kare hızı kaldıraç değil), imleçler durunca (pencere arkada) 17. Bir
/// karenin çizilmesi pencere kompozisyonu ve GPU sürücüsüyle birlikte ödeniyor; tek kaldıraç imlecin HİÇ çizilmemesidir.</para>
///
/// <para><b>Saf çekirdek:</b> zamanı dışarıdan alır (<c>nowMs</c>), zamanlayıcıyı sahibi (<c>MainWindow</c>) sürer —
/// <see cref="Tick"/> boşta kalınıp kalınmadığını söyler, <see cref="NoteInput"/> uyandırır. Durum değişimi
/// <see cref="IdleChanged"/> ile <c>CursorClock.SetInputIdle</c>'a gider; aynı durumda tekrar çağrı olay üretmez. Zaman aşımı
/// kurulurken bir kez okunur; <see cref="PollInterval"/> sahibin yoklama aralığıdır — boşta kalma en geç bu kadar gecikmeyle
/// fark edilir.</para>
/// </summary>
internal sealed class CaretIdleGate
{
    /// <summary>İşletim sistemi değeri okunamazsa Windows'un varsayılanı (5 s).</summary>
    internal const int DefaultTimeoutMs = 5000;

    /// <summary>Sahibin yoklama aralığı: girdi varken saniyede bir bakılır; boşta kalma en geç bir saniye gecikmeyle görülür.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly long _timeoutMs;
    private long _lastInputMs;

    public CaretIdleGate(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        _timeoutMs = (long)timeout.TotalMilliseconds;
    }

    /// <summary>Son girdinin üstünden geçmesi gereken süre.</summary>
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(_timeoutMs);

    /// <summary>Girdi yok sayılıyor mu (imleçler durmuş).</summary>
    public bool IsIdle { get; private set; }

    /// <summary><see cref="IsIdle"/> değişti — <c>true</c>: imleçler durur, <c>false</c>: yeniden başlar.</summary>
    public event Action<bool>? IdleChanged;

    /// <summary>Girdi geldi (klavye, fare, aktifleşme): zaman damgası yenilenir; boştaysa uyanır.</summary>
    public void NoteInput(long nowMs)
    {
        _lastInputMs = nowMs;
        if (!IsIdle) return;
        IsIdle = false;
        IdleChanged?.Invoke(false);
    }

    /// <summary>Yoklama: son girdinin üstünden zaman aşımı geçtiyse boşta. <c>true</c> = artık boşta (sahip yoklamayı bırakabilir).</summary>
    public bool Tick(long nowMs)
    {
        if (IsIdle) return true;
        if (nowMs - _lastInputMs < _timeoutMs) return false;
        IsIdle = true;
        IdleChanged?.Invoke(true);
        return true;
    }
}
