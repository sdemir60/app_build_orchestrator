using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [Faz 3 — spec 2026-09-18 §5] Çıktı kanıtı testlerinin ORTAK saati ve damgası: girdiler <see cref="InputsAt"/>'ta,
/// derleme kanıtları <see cref="EvidenceAt"/>'ta (girdilerden yeni ⇒ zaman kipinde taze). Zamanlar açıkça yazılır
/// (D8 — sleep yok). Binder, Sync ve koşu testleri aynı değerleri ve aynı damgalama sırasını buradan okur.
/// </summary>
internal static class EvidenceTimes
{
    public static readonly DateTime InputsAt = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime EvidenceAt = InputsAt.AddMinutes(10);
    public static readonly DateTime ToolRunAt = InputsAt.AddMinutes(11);
    public static readonly DateTime EditedAt = InputsAt.AddMinutes(20);

    /// <summary>
    /// <paramref name="root"/> altındaki her dosyayı <see cref="InputsAt"/>'a, <paramref name="outputs"/>'u
    /// <see cref="EvidenceAt"/>'a damgalar. Dosya oluşturmak klasör zamanını ilerlettiği için klasörler (kök dahil)
    /// EN SONDA <see cref="InputsAt"/>'a çekilir.
    /// </summary>
    public static void Stamp(string root, IEnumerable<string> outputs)
    {
        var evidence = outputs.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(file, evidence.Contains(Path.GetFullPath(file)) ? EvidenceAt : InputsAt);
        foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Append(root))
            Directory.SetLastWriteTimeUtc(dir, InputsAt);
    }
}
