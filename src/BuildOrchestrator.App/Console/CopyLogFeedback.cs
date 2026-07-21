namespace BuildOrchestrator.App.Console;

/// <summary>
/// [T56/3b] Copy-log butonunun geri-bildirim durumu (Ek A #3): başarılı kopyalamada ikon <b>1400ms ✓</b> +
/// "Copied" tooltip gösterir, sonra normale (copy ikonu + "Copy log") döner. SAF, enjekte-saatli durum makinesi
/// — kontrol bir DispatcherTimer ile <see cref="ShouldRevert"/>'i yoklar; süre mantığı burada deterministik
/// test edilir (gerçek 1400ms beklenmez — D8).
/// </summary>
public sealed class CopyLogFeedback
{
    /// <summary>Ek A #3: kopyalama sonrası ✓ + "Copied" gösterim süresi.</summary>
    public const double RevertMs = 1400.0;

    private TimeSpan _copiedAt;

    /// <summary>true iken ikon ✓, tooltip "Copied"; false iken copy ikonu + "Copy log".</summary>
    public bool Copied { get; private set; }

    /// <summary>Başarılı kopyalama anı — ✓ durumuna geçer ve zamanlayıcıyı bu andan başlatır.</summary>
    public void MarkCopied(TimeSpan now)
    {
        Copied = true;
        _copiedAt = now;
    }

    /// <summary>✓ durumundayken <see cref="RevertMs"/> geçtiyse true (normale dönme zamanı).</summary>
    public bool ShouldRevert(TimeSpan now) => Copied && (now - _copiedAt).TotalMilliseconds >= RevertMs;

    /// <summary>Normale döner (copy ikonu).</summary>
    public void Revert() => Copied = false;
}
