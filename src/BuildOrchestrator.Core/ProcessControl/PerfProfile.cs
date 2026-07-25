namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>[T20-a/K11] Kullanıcının perf chip'iyle seçtiği üç profil.</summary>
public enum PerfMode
{
    Full,
    Balanced,
    Light,
}

/// <summary>
/// [T20-a] Job priority class'ın SAF karşılığı. Bilerek <c>System.Diagnostics.ProcessPriorityClass</c>'a
/// bağlanılmaz: Core net10.0'dır (Windows'a bağlı değil) ve <see cref="PerfProfile"/> tablosu Win32 türü
/// taşımamalıdır — Win32 sabitine çeviri tek yerde, <see cref="JobObject.SetPriorityClass"/> içinde yapılır.
/// </summary>
public enum ProcessPriorityClassKind
{
    Normal,
    BelowNormal,
    Idle,
}

/// <summary>
/// [T20-a/K11] Sabit paralellik + CPU cap + process priority üçlüsünün TEK doğruluk kaynağı:
/// Full(6, cap yok, Normal) · Balanced(4, %70, BelowNormal) · Light(2, %40, Idle).
/// <c>CpuCapPercent == null</c> ⇒ cap yok (inner job'da <see cref="JobObject.ClearCpuRate"/>).
/// </summary>
public readonly record struct PerfProfile(int Parallelism, int? CpuCapPercent, ProcessPriorityClassKind Priority)
{
    public static PerfProfile For(PerfMode mode) => mode switch
    {
        PerfMode.Full => new PerfProfile(6, null, ProcessPriorityClassKind.Normal),
        PerfMode.Balanced => new PerfProfile(4, 70, ProcessPriorityClassKind.BelowNormal),
        PerfMode.Light => new PerfProfile(2, 40, ProcessPriorityClassKind.Idle),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    /// <summary>App/IPC tarafındaki perf-mode string'i ile köprü. Eşleşme ORDINAL'dir (App her zaman
    /// "Full"/"Balanced"/"Light" yazar); tanınmayan metin ⇒ <c>null</c>, çağıran kendi varsayılanına düşer.</summary>
    public static PerfProfile? TryParse(string perfModeText) => perfModeText switch
    {
        "Full" => For(PerfMode.Full),
        "Balanced" => For(PerfMode.Balanced),
        "Light" => For(PerfMode.Light),
        _ => null,
    };
}
