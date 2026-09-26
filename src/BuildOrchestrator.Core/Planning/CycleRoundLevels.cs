namespace BuildOrchestrator.Core.Planning;

/// <summary>
/// [seviyeli turlar] Bir SCC turunun İÇİNDEKİ güvenli paralellik planı: turun üyeleri BARİYERLİ seviyelere
/// bölünür — bir seviye tamamen bitmeden sonraki başlamaz, seviye İÇİNDEKİ üyeler eşzamanlı derlenebilir.
///
/// <para>Kurallar (tur semantiği birebir korunur):</para>
/// <list type="number">
/// <item><b>İleri kenar sıralar.</b> Build-order'da ÖNCE gelen bir grup-içi üreticiye kenarı olan tüketici,
/// üreticiden SONRAKİ bir seviyededir — sıralı turda olduğu gibi bu turda üretilen TAZE çıktıyı okur.</item>
/// <item><b>Komşular asla eşzamanlı değil.</b> HERHANGİ yönde doğrudan kenarla bağlı iki üye aynı seviyeye
/// konmaz: biri diğerinin DLL'ini okurken öteki aynı dosyayı yazıyor olabilirdi (torn read). Bariyer +
/// komşu ayrımı birlikte bunu yapısal olarak imkânsız kılar.</item>
/// <item><b>Geri kenar sıralamaz.</b> Build-order'da SONRA gelen üreticinin tüketicisi, üreticinin BİR ÖNCEKİ
/// nesil çıktısını okur — sıralı turun semantiği de tam olarak buydu; seviyeler bunu değiştirmez.</item>
/// <item><b>Determinizm.</b> Üyeler build-order'da tek geçişte yerleştirilir; seviye içi sıra build-order'dır.
/// Aynı girdi her zaman aynı planı verir.</item>
/// </list>
///
/// <para>Tur kapsamı DIŞINDA kalan bir bağımlılık (bu turda derlenmeyen üye ya da grup dışı proje) kısıt
/// üretmez: dosyası bu tur boyunca yazılmaz, dolayısıyla ne sıralamaya ne komşu ayrımına girer.</para>
///
/// Saf Core state: I/O, process, async, log YOK [D3].
/// </summary>
public static class CycleRoundLevels
{
    /// <param name="toBuild">Bu turda derlenecek üyeler, BUILD-ORDER sıralı (çağıran sırayı garanti eder).</param>
    /// <param name="intraDepsOf">Üyenin grup-içi bağımlılıkları; kapsam dışı id'ler filtrelenir.</param>
    /// <returns>Bariyerli seviyeler; birleşimi <paramref name="toBuild"/>'in kendisidir, sıra korunur.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> Compute(
        IReadOnlyList<string> toBuild, Func<string, IReadOnlyList<string>> intraDepsOf)
    {
        ArgumentNullException.ThrowIfNull(toBuild);
        ArgumentNullException.ThrowIfNull(intraDepsOf);

        var orderOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < toBuild.Count; i++) orderOf[toBuild[i]] = i;

        // Komşuluk (yönsüz) yalnız TUR İÇİ çiftler arasında: m → deps(m) ∩ toBuild, iki uçtan da yazılır.
        var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in toBuild) neighbors[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in toBuild)
            foreach (string dep in intraDepsOf(id))
                if (orderOf.ContainsKey(dep) && !string.Equals(dep, id, StringComparison.OrdinalIgnoreCase))
                {
                    neighbors[id].Add(dep);
                    neighbors[dep].Add(id);
                }

        // Tek geçiş, build-order'da: sonra yerleşen üye öncekileri asla oynatmaz — ileri kısıtlar (bağımlılık
        // hep daha önce yerleşti) kalıcı olarak doğru kalır; komşu ayrımı da her çift için, çiftin GEÇ üyesi
        // yerleşirken uygulanır. Seviye numarası üye sayısını aşamaz ⇒ sonlanma apaçık.
        var levelOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in toBuild)
        {
            int level = 1;
            foreach (string dep in intraDepsOf(id))
                if (orderOf.TryGetValue(dep, out int depOrder) && depOrder < orderOf[id]
                    && levelOf.TryGetValue(dep, out int depLevel))
                    level = Math.Max(level, depLevel + 1); // ileri kenar: taze çıktıyı okuyacak — sonra derlenir
            // Komşu ayrımı: aynı DLL'i biri yazarken öteki okuyamaz. Yükselen seviye daha önce bakılan bir
            // komşunun seviyesine denk gelebilir — tarama sabitlenene dek tekrarlanır (seviye, yerleşmiş komşu
            // sayısını aşamayacağı için sonlanma yine apaçık).
            bool moved = true;
            while (moved)
            {
                moved = false;
                foreach (string neighbor in neighbors[id])
                    if (levelOf.TryGetValue(neighbor, out int taken) && taken == level)
                    {
                        level++;
                        moved = true;
                    }
            }
            levelOf[id] = level;
        }

        return [.. toBuild
            .GroupBy(id => levelOf[id])
            .OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<string>)[.. g])];
    }
}
