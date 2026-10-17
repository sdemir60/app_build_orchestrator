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

    /// <summary>
    /// [T20-b/P3] Post-build copy sıkıştığında (MSB302x) cap'in geçici olarak yükseltileceği TABAN. Değer
    /// tesadüfen değil, KASITLI olarak Balanced'ın cap'idir: "kullanıcı makineyi kullanmaya devam edebilir"
    /// sözünü veren en düşük profil odur, yani sıkışan copy'yi açmak için gereken en küçük gevşetme zaten
    /// bilinen ve kabul edilmiş bir noktadır. Ayrı bir sabit yazmak, tablo değiştiği gün sessizce ayrışırdı.
    /// </summary>
    public static int CopyPhaseFloorPercent => For(PerfMode.Balanced).CpuCapPercent!.Value;

    /// <summary>
    /// [T20-b/P3] Aynı pencerenin priority TABANI — cap tabanıyla AYNI profilden (Balanced) türetilir. İki yarı
    /// birlikte gitmek zorundadır: tavanı %70'e çıkarıp priority'yi Idle'da bırakmak, sıkışan copy'yi yüklü bir
    /// makinede yine zamanlayıcı kuyruğunun sonunda tutardı (Idle bir child, tavanı serbest olsa bile sıra
    /// alamaz) — yani floor'un yarısı etkisiz kalırdı.
    /// </summary>
    public static ProcessPriorityClassKind CopyPhaseFloorPriority => For(PerfMode.Balanced).Priority;

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
