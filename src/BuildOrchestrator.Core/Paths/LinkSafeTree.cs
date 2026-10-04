namespace BuildOrchestrator.Core.Paths;

/// <summary>
/// [PERF Faz C · tek kaynak] Bir dizin ağacını BAĞLANTIYI (junction/symlink) İZLEMEDEN, aşağıdan yukarı siler. Reparse
/// point asla dizin olarak açılmaz: bağlantının KENDİSİ kaldırılır, hedefin içeriğine dokunulmaz; yalnız gerçek dizinde
/// özyinelenir (önce dosyalar, sonra alt dizinler, en son dizinin kendisi). Gezinme TEK yerdedir; iki çağıran aynı
/// gezinmeyi KENDİ hata politikasıyla sürer: Clean'in <c>bin</c>/<c>obj</c> silmesi best-effort (salt-okur temizliği,
/// retry, kilitli dosya sayacı), koşu logu saklaması sıkı (ilk IO hatası fırlar).
///
/// <para><b>Neden <c>Directory.Delete(path, recursive: true)</c> değil:</b> o da bağlantıyı izlemez ama bu makinede
/// (Windows, .NET 10) içinde junction olan bir klasörde bağlantıyı ve dosyaları kaldırdıktan sonra
/// <see cref="UnauthorizedAccessException"/> fırlatıp boş klasörü geride bırakıyor; üstelik dosya başına politika
/// (salt-okur, retry, sayaç) o çağrıya sokulamaz. Üretim kodunda özyinelemeli <c>Directory.Delete</c> bu yüzden
/// kullanılmaz — bir ağacı silmesi gereken her yer buradan geçer.</para>
/// </summary>
internal static class LinkSafeTree
{
    /// <summary>
    /// <paramref name="dir"/> ağacını siler. GİRİŞTE kendi kökünün bağlantı olup olmadığına TAZE bakar (çağıranın daha
    /// önce yaptığı bir kontrol bayat olabilir: seçimle silme arasında klasör bir bağlantıyla değiştirilebilir): kök bir
    /// bağlantıysa içine inmez, yalnız bağlantıyı <paramref name="removeDirectory"/> ile kaldırır. Gerçek dizinde her
    /// dosya için <paramref name="deleteFile"/>, her alt dizin için özyineleme ve sonda dizinin kendisi için
    /// <paramref name="removeDirectory"/> çağrılır; bir hatayla ne yapılacağı (fırlat / yut / say) ÇAĞIRANIN politikasıdır.
    /// </summary>
    /// <param name="deleteFile">Dosyayı (dosya bağlantısıysa yalnız bağlantının kendisini) siler.</param>
    /// <param name="removeDirectory">Boşalmış gerçek dizini YA DA dizin bağlantısının kendisini kaldırır (özyinelemesiz).</param>
    /// <param name="tolerateListingErrors">
    /// <c>true</c>: numaralandırma sırasında klasör başkası tarafından kaldırılırsa ya da erişilemezse
    /// (<see cref="IOException"/>/<see cref="UnauthorizedAccessException"/>) o klasör boş sayılır ve akış durmaz;
    /// <c>false</c> (varsayılan): hata fırlar. YALNIZ numaralandırmayı kapsar — <paramref name="deleteFile"/> ve
    /// <paramref name="removeDirectory"/>'in hataları her iki kipte de çağıranındır.
    /// </param>
    internal static void Delete(string dir, Action<string> deleteFile, Action<string> removeDirectory,
        bool tolerateListingErrors = false)
    {
        ArgumentNullException.ThrowIfNull(dir);
        ArgumentNullException.ThrowIfNull(deleteFile);
        ArgumentNullException.ThrowIfNull(removeDirectory);

        if (IsLink(dir))
        {
            removeDirectory(dir); // bağlantının KENDİSİ; hedefin içeriğine dokunulmaz
            return;
        }

        foreach (string file in List(dir, directories: false, tolerateListingErrors)) deleteFile(file);
        foreach (string sub in List(dir, directories: true, tolerateListingErrors))
            Delete(sub, deleteFile, removeDirectory, tolerateListingErrors); // alt dizin de girişte kendine bakar

        removeDirectory(dir); // aşağıdan yukarı: kalan (ör. kilitli) bir dosya varsa sonucu çağıranın politikası belirler
    }

    /// <summary>Yolun kendisi bir reparse point mi (junction, dizin sembolik bağı)? Yeni bir <see cref="DirectoryInfo"/>
    /// önbelleksizdir: öznitelik BURADA, çağrı anında okunur. Hedefe inmez — bağlantının kendi özniteliği okunur. Olmayan
    /// yol için okuma -1 döner: bağlantı DEĞİLDİR; yokluğu sonraki numaralandırma kendi kipine göre ele alır.</summary>
    private static bool IsLink(string dir)
    {
        FileAttributes attributes = new DirectoryInfo(dir).Attributes;
        return attributes != (FileAttributes)(-1) && (attributes & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>Dizinin dosyaları ya da alt dizinleri (dizin bağlantıları dahil — dizin olarak listelenirler). Dizi
    /// ÖNCEDEN toplanır: silme numaralandırmanın ortasında olmaz.</summary>
    private static string[] List(string dir, bool directories, bool tolerateListingErrors)
    {
        try { return directories ? Directory.GetDirectories(dir) : Directory.GetFiles(dir); }
        catch (Exception ex) when (tolerateListingErrors && (ex is IOException or UnauthorizedAccessException)) { return []; }
    }
}
