namespace BuildOrchestrator.Core.MsBuild;

/// <summary>[files-in-use] Kopya komutunun tek bir dosyası: kaynak ve hedefteki tam yolu.</summary>
public sealed record CopyTarget(string Source, string Destination);

/// <summary>
/// [files-in-use] Kopya komutlarının hedefteki dosyaları ve hedefe ulaşıp ulaşmadıkları. Yalnız dosya BİLGİSİ okunur (var mı,
/// boyut, değiştirilme zamanı); hiçbir dosya açılmaz, yazılmaz.
/// <para><b>Hangi dosya hedeftir:</b> komutun KOPYALADIĞI dosyanın hedefteki adı — hedef bir klasörse
/// <c>&lt;hedef&gt;\&lt;kaynak dosya adı&gt;</c>, tek kaynaklı komutta hedef bir dosya yoluysa o yolun kendisi. Joker kaynak
/// (<c>Acme.Types.*</c>) kaynak klasöründe eşlediği dosyalara açılır — komutun kendisi de tam olarak onları kopyalar. Hedef
/// klasörde adı benzeyen ama komutun kopyalamadığı bir dosya (başka projenin çıktısı) hiçbir zaman hedef değildir.</para>
/// <para><b>Yalnız tam yollar [son inceleme I1].</b> Göreli bir yolun tabanı komutun koştuğu hedefe bağlıdır — MSBuild
/// <c>PostBuildEvent</c>'i <c>$(OutDir)</c>'de, özel bir <c>Exec</c>'i projenin klasöründe koşturur — ve yanlış taban, komutun hiç
/// dokunmadığı bir klasördeki dosyayı hedef yapardı. Bir kaynağı ya da hedefi göreli olan komut bu yüzden hiç hedef üretmez;
/// <c>%DEĞİŞKEN%</c> motorun ortamından açılır ve tam bir yola açılmalıdır.</para>
/// </summary>
public static class CopyTargets
{
    public static IReadOnlyList<CopyTarget> Resolve(IEnumerable<CopyCommand> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);
        var targets = new List<CopyTarget>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in commands)
        {
            if (FullPath(command.Destination) is not { } destination) continue;
            if (command.Sources.Any(source => FullPath(source) is null)) continue;
            bool destinationIsFolder = Directory.Exists(destination) || EndsWithSeparator(command.Destination);
            var sources = command.Sources.SelectMany(SourceFiles).ToList();
            if (destinationIsFolder)
            {
                foreach (string source in sources)
                    Add(new CopyTarget(source, Path.Combine(destination, Path.GetFileName(source))));
            }
            else if (command.Sources.Count == 1 && !HasWildcard(command.Sources[0]) && sources.Count == 1)
            {
                Add(new CopyTarget(sources[0], destination));
            }
        }
        return targets;

        void Add(CopyTarget target)
        {
            if (seen.Add(target.Destination)) targets.Add(target);
        }
    }

    /// <summary>
    /// Hedef kaynakla aynı boyutta ve TAM aynı değiştirilme zamanında mı — <c>cmd</c>'nin kopyası zamanı tam korur. Tolerans YOKTUR: arka
    /// arkaya iki derleme aynı boyutta ve bir saniyeden kısa arayla iki DLL üretebilir (ölçüldü), kilit yüzünden yerinde kalan eski kopya
    /// toleransla "ulaşmış" görünürdü. Zamanı yuvarlayan bir hedefte (FAT) ulaşmış bir kopya ulaşmamış okunur; orada yeniden denemenin
    /// sonucu bir uyarıyla kapanabilir (ARCHITECTURE §20). Hedef yoksa ulaşmamıştır. Kaynak yoksa yargılanacak bir şey yoktur (ulaşmış sayılır).
    /// </summary>
    public static bool Landed(CopyTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        FileInfo source, destination;
        try
        {
            source = new FileInfo(target.Source);
            if (!source.Exists) return true;
            destination = new FileInfo(target.Destination);
            if (!destination.Exists) return false;
            return destination.Length == source.Length && destination.LastWriteTimeUtc == source.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Okuma arasında kaybolan ya da okunamayan dosya: kanıt yok — ulaşmamış sayılır, kilit yoklaması karar verir.
            return false;
        }
    }

    /// <summary>
    /// Hedef var ve kaynaktan ESKİ mi — kilitli kopyanın kararı yalnız böyle bir hedefe verilir [son inceleme C1]. Kaynaktan YENİ bir hedef
    /// (başka bir projenin koyduğu yeni sürüm; <c>xcopy /d</c>'nin bilerek atladığı) hiçbir zaman kenara alınmaz: alınsaydı yeniden deneme yeni
    /// DLL'in yerine eskisini koyardı. Aynı zamandaki hedef ulaşmıştır, olmayan hedef taşınacak bir şey değildir; okunamayan dosya eski sayılmaz.
    /// </summary>
    public static bool Outdated(CopyTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        try
        {
            var source = new FileInfo(target.Source);
            var destination = new FileInfo(target.Destination);
            return source.Exists && destination.Exists && destination.LastWriteTimeUtc < source.LastWriteTimeUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> SourceFiles(string rawSource)
    {
        if (FullPath(rawSource) is not { } full) return [];
        string name = Path.GetFileName(full);
        if (!HasWildcard(name)) return File.Exists(full) ? [full] : [];
        if (Path.GetDirectoryName(full) is not { } folder || !Directory.Exists(folder)) return [];
        try
        {
            return Directory.EnumerateFiles(folder, name, SearchOption.TopDirectoryOnly).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Tam yol (sürücü harfli ya da UNC); göreli yol, çözülemeyen değişken ya da geçersiz yol <c>null</c>.</summary>
    private static string? FullPath(string raw)
    {
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
            if (expanded.Length == 0 || expanded.Contains('%') || !Path.IsPathFullyQualified(expanded)) return null;
            return Path.GetFullPath(expanded);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool HasWildcard(string path) => Path.GetFileName(path).IndexOfAny(['*', '?']) >= 0;

    private static bool EndsWithSeparator(string path) =>
        path.TrimEnd().EndsWith(Path.DirectorySeparatorChar) || path.TrimEnd().EndsWith(Path.AltDirectorySeparatorChar);
}

/// <summary>[files-in-use] <see cref="FileLock.Probe"/>'un cevabı.</summary>
public enum FileLockState
{
    /// <summary>Yazmak için açılabildi.</summary>
    Free,
    /// <summary>Başka bir süreç tutuyor: görüntü olarak yüklenmiş bir DLL ya da yazmayı paylaşmayan bir tutamaç (sharing/lock violation).</summary>
    Locked,
    /// <summary>Dosya (ya da klasörü) yok.</summary>
    Missing,
    /// <summary>Yazılamıyor ama sebep bir süreç değil: salt okunur dosya, erişim izni.</summary>
    Denied,
}

/// <summary>
/// [files-in-use] Bir dosyanın yazmak için açılıp açılamadığı. Dosya en geniş paylaşımla yazma isteğiyle açılır ve HEMEN kapanır;
/// hiçbir bayt yazılmaz, değiştirilme zamanı değişmez. Yalnız hedefe ULAŞMAMIŞ bir kopya hedefi için sorulur.
/// </summary>
public static class FileLock
{
    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    public static FileLockState Probe(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
            return FileLockState.Free;
        }
        catch (FileNotFoundException) { return FileLockState.Missing; }
        catch (DirectoryNotFoundException) { return FileLockState.Missing; }
        catch (UnauthorizedAccessException) { return FileLockState.Denied; }
        catch (IOException ex) when (ex.HResult is SharingViolation or LockViolation) { return FileLockState.Locked; }
        catch (IOException) { return FileLockState.Denied; }
    }
}
