namespace BuildOrchestrator.Core.Planning;

/// <summary>
/// [okunan dosya kanıtı] Bir döngü üyesi tur sonunda bir kardeşin HANGİ dosyalarıyla yargılanır.
///
/// <para>Kardeşin çıktısı birden çok dosyada durur: kendi derleme kanıtı (bin) ve post-build'in beslediği
/// paylaşılan kopyalar (<see cref="Incremental.ProjectOutputs"/>). Tur sonu kararının sorusu "üyenin OKUDUĞU
/// yüzey değişti mi?"dir; okunmamış bir dosyanın değişmesi derlemenin sonucunu değiştiremez. Clean kendi bin'ini
/// silip paylaşılan kopyaya dokunmadığında bu ayrım bir turun tamamıdır: "yok → var" geçişi hiç okunmamış
/// dosyada olur.</para>
///
/// <para>Kesin bilgi = derleyicinin bu derlemede referans verdiği ve kardeşin BİLİNEN dosyalarından biri olan
/// yol (<see cref="MsBuild.CompilerReferences"/>): yalnız o izlenir. Şüphenin her biçiminde kardeşin TÜM bilinen
/// dosyaları izlenir — eski kural, güvenli yön (en çok bir tur fazladan ödenir, yanlış yakınsama asla):</para>
/// <list type="bullet">
/// <item>derleme başarısız — "bir tur daha düzeltir mi?" sorusu en geniş kanıtla cevaplanır;</item>
/// <item>derleyici satırı yok — derleyici koşmadı ya da satır okunamadı;</item>
/// <item>derleyici kardeşin adını taşıyan hiçbir dosya okumadı — referans bulunamamıştır, dosya bu turda
/// gelebilir;</item>
/// <item>okunan dosya kardeşin adını taşıyor ama bilinen dosyalarından değil — turun onu yazıp yazmadığı
/// bilinmez.</item>
/// </list>
/// Saf: I/O yok [D3].
/// </summary>
public static class CycleReadFiles
{
    /// <param name="producerFiles">Kardeşin bilinen dosyaları: kanıt yolu + beslenen kopya adayları.</param>
    /// <param name="compiledReferences">Üyenin bu derlemesinde derleyiciye verilen referanslar; bilinmiyorsa
    /// <c>null</c>.</param>
    /// <param name="succeeded">Derleme başarılı mı.</param>
    /// <returns>İzlenecek dosyalar — <paramref name="producerFiles"/>'daki yazım ve sırayla.</returns>
    public static IReadOnlyList<string> Tracked(
        IReadOnlyList<string> producerFiles, IReadOnlyCollection<string>? compiledReferences, bool succeeded)
    {
        ArgumentNullException.ThrowIfNull(producerFiles);
        if (!succeeded || compiledReferences is not { Count: > 0 }) return producerFiles;

        var producerNames = new HashSet<string>(producerFiles.Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase);
        var read = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string reference in compiledReferences)
        {
            if (!producerNames.Contains(Path.GetFileName(reference))) continue;
            string? known = producerFiles.FirstOrDefault(file => SamePath(file, reference));
            if (known is null) return producerFiles;          // bilinmeyen bir kopya okundu
            read.Add(known);
        }
        return read.Count == 0 ? producerFiles : [.. producerFiles.Where(read.Contains)];
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Tam yollar klasör yazımı farkından arındırılır (<c>..</c>, ayraç); göreli yol olduğu gibi
    /// kalır — çalışma klasörüne göre çözmek, kararı süreç durumuna bağlardı.</summary>
    private static string Normalize(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return path;
        try { return Path.GetFullPath(path); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
}
