using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.App.Controls;

/// <summary>
/// [design v1.20.0 §2.3 · §5] <b>Çıktının durumu</b> — bir koşudan bağımsız, projenin diskteki çıktısının şu
/// anki hâli. Görsel durumun (<see cref="VisualStatus"/>) taban katmanıdır; koşu (kuyruk, derleme, sonuç) ve
/// işaretleme dalgası bunun ÜSTÜNE biner, atlanmak ise hiçbir şey bindirmez (<see cref="VisualStatuses.For"/>).
/// <para>Kaynak TEK'tir: önizlemenin kararı (<c>WillBuild</c> + <c>WillBuildReason</c>). Karar yoksa durum
/// bilinmez — uygulama açıldı ama henüz Sync yapılmadı.</para>
/// </summary>
public enum StandingStatus
{
    /// <summary>Karar yok (Sync yapılmadı ya da karar düşürüldü) — başlangıç modu.</summary>
    Unknown,
    /// <summary>Güncel — yeşil. Bağımlılığını bekleyen proje de buradadır: kendi çıktısı sağlamdır, "bekliyor"
    /// bilgisi uyarı üçgenindedir.</summary>
    Current,
    /// <summary>Derlenecek — gri (değişti, hiç derlenmedi, bağımlılığı bozuktu).</summary>
    Stale,
    /// <summary>Bozuk — kırmızı; yalnız KANITLI hata (derleyici hatası, imza bugünküyle aynı).</summary>
    Failed,
}

/// <summary>[design v1.20.0 §2.3] Önizleme kararı → çıktı durumu eşlemesinin TEK yeri (kopya YASAK).</summary>
public static class StandingStatuses
{
    /// <summary>Karar → durum. <paramref name="willBuild"/> ya da <paramref name="reason"/> yoksa karar yoktur
    /// ve durum <see cref="StandingStatus.Unknown"/>'dır; aksi halde renk yalnız gerekçeden okunur.</summary>
    public static StandingStatus From(bool? willBuild, WillBuildReason? reason)
    {
        if (willBuild is null || reason is not { } r) return StandingStatus.Unknown;
        return r switch
        {
            WillBuildReason.LastFailed => StandingStatus.Failed,
            // WaitingForDependency yeşildir: kendi çıktısı sağlam, "bekliyor" üçgende söylenir.
            WillBuildReason.UpToDate or WillBuildReason.WaitingForDependency => StandingStatus.Current,
            // [Faz 3 — spec 2026-09-18 §5.4] BuiltOutside de yeşildir: çıktı güncel, yalnız bu araçla değil.
            WillBuildReason.BuiltOutside => StandingStatus.Current,
            // NeverBuilt · SignatureChanged · DepIssue · OutputStale · OutputMissing · OutputReplaced —
            // üçü de bugünkü kanıta göre "derlenecek"tir; varsayılan dal, açıkça belirtilir (kopya YASAK).
            _ => StandingStatus.Stale,
        };
    }
}
