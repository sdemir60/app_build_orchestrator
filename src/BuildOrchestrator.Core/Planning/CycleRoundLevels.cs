namespace BuildOrchestrator.Core.Planning;

/// <summary>
/// [seviyeli turlar] Bir SCC turunun İÇİNDEKİ güvenli paralellik planı: turun üyeleri BARİYERLİ seviyelere
/// bölünür — bir seviye tamamen bitmeden sonraki başlamaz, seviye İÇİNDEKİ üyeler eşzamanlı derlenebilir.
///
/// <para>Kurallar:</para>
/// <list type="number">
/// <item><b>Komşular asla eşzamanlı değil.</b> HERHANGİ yönde doğrudan kenarla bağlı iki üye aynı seviyeye
/// konmaz: biri diğerinin DLL'ini okurken öteki aynı dosyayı yazıyor olabilirdi (torn read). Bariyer + komşu
/// ayrımı birlikte bunu yapısal olarak imkânsız kılar.</item>
/// <item><b>Paylaşılan kopyaya dokunanlar da.</b> Bir üyenin derlemesi başka bir projenin paylaşılan kopyasını
/// da yazabiliyorsa (<see cref="SharedCopyCollisions"/>) o projeyle ve onu okuyanlarla aynı seviyeye konmaz.</item>
/// <item><b>En çok okunan önce.</b> Üyeler çakışmadıkları en erken seviyeye, en çok OKUNANDAN başlayarak
/// yerleşir (eşitlikte daha çok komşusu olan, sonra build-order). Önceki seviyedeki bir kardeşi okuyan üye onun
/// bu turdaki taze çıktısını, sonraki seviyedekini ise önceki neslini okur; ikisi de doğrudur — hangisinin
/// yeniden derleme gerektirdiğine tur sonu kararı (<see cref="CycleRoundPolicy"/>) bakar. Çok okunanın önde
/// olması hem seviyeleri kısaltır hem eski nesli okuyan kenarları azaltır: API'si değişen merkez üye, okuyucularına
/// ikinci tur ödetmez.</item>
/// <item><b>Determinizm.</b> Aynı girdi her zaman aynı planı verir; seviye içi sıra build-order'dır.</item>
/// </list>
///
/// <para>Tur kapsamı DIŞINDA kalan bir bağımlılık (bu turda derlenmeyen üye ya da grup dışı proje) komşuluk ve
/// okunma sayısı üretmez: dosyası bu tur boyunca yazılmaz.</para>
///
/// Saf Core state: I/O, process, async, log YOK [D3].
/// </summary>
public static class CycleRoundLevels
{
    /// <summary>
    /// [paylaşılan kopya çakışması] Yaygın post-build <c>copy $(TargetName).*</c> kendi çıktısının yanında bin'deki
    /// "Ad.*" dosyalarının HEPSİNİ kopyalar — "Ad.Başka.dll" adlı bir referansın copy-local kopyası dahil. Bu
    /// yüzden adı başka bir projenin adının NOKTALI öneki olan proje, derlenirken o projenin paylaşılan kopyasını
    /// da yeniden yazabilir (sahada: UI.General → UI.General.Common, UI.NewSales → NewSales.Stock/Pricing). Kopya
    /// yazılırken aynı dosyayı okuyan derleyici kilide takılır ya da yarım dosya görür.
    ///
    /// <para>Kural yalnız adlara bakar, post-build metnine bakmaz: gereğinden fazla çakışma ilan edebilir (en çok
    /// biraz paralellik kaybı), eksik ilan etmez — ad deseni dışındaki bir kopyalama zaten görünmezdir.</para>
    /// </summary>
    /// <param name="nameOf">Projenin adı (AssemblyName — <c>$(TargetName)</c>'in varsayılanı).</param>
    /// <param name="dependenciesOf">Projenin TÜM bağımlılıkları (grup içi ve dışı).</param>
    /// <returns>Simetrik yüklem: iki üye aynı seviyede derlenemez mi.</returns>
    public static Func<string, string, bool> SharedCopyCollisions(
        Func<string, string> nameOf, Func<string, IReadOnlyList<string>> dependenciesOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        ArgumentNullException.ThrowIfNull(dependenciesOf);

        // writer, other'ın kendisinin ya da okuduğu bir projenin kopyasını yazabilir mi?
        bool MayWriteWhatOtherTouches(string writer, string other)
        {
            string prefix = nameOf(writer) + ".";
            if (nameOf(other).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string dependency in dependenciesOf(other))
                if (nameOf(dependency).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        return (a, b) => !string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
            && (MayWriteWhatOtherTouches(a, b) || MayWriteWhatOtherTouches(b, a));
    }

    /// <param name="toBuild">Bu turda derlenecek üyeler, BUILD-ORDER sıralı (çağıran sırayı garanti eder).</param>
    /// <param name="intraDepsOf">Üyenin grup-içi bağımlılıkları; kapsam dışı id'ler filtrelenir.</param>
    /// <param name="mayCollide">Kenar dışında aynı seviyede derlenemeyecek çiftler
    /// (<see cref="SharedCopyCollisions"/>); <c>null</c> ⇒ yalnız kenarlar.</param>
    /// <returns>Bariyerli seviyeler; her üye tam bir kez yer alır, seviye içi sıra build-order'dır.</returns>
    public static IReadOnlyList<IReadOnlyList<string>> Compute(
        IReadOnlyList<string> toBuild, Func<string, IReadOnlyList<string>> intraDepsOf,
        Func<string, string, bool>? mayCollide = null)
    {
        ArgumentNullException.ThrowIfNull(toBuild);
        ArgumentNullException.ThrowIfNull(intraDepsOf);

        var orderOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < toBuild.Count; i++) orderOf[toBuild[i]] = i;

        // Tur içi okuma kenarları: m → deps(m) ∩ toBuild. Komşuluk yönsüzdür; okunma sayısı yönlüdür.
        var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var readers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in toBuild)
        {
            neighbors[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            readers[id] = 0;
        }
        foreach (string id in toBuild)
            foreach (string dep in intraDepsOf(id).Distinct(StringComparer.OrdinalIgnoreCase))
                if (orderOf.ContainsKey(dep) && !string.Equals(dep, id, StringComparison.OrdinalIgnoreCase))
                {
                    readers[dep]++;
                    neighbors[id].Add(dep);
                    neighbors[dep].Add(id);
                }

        // Yerleşim sırası: en çok okunan önce (okuyucuları taze çıktıyı okur), eşitlikte en çok komşulu (en
        // kısıtlı), sonra build-order. Her üye, çakışan hiçbir üyenin bulunmadığı en erken seviyeye girer;
        // seviye numarası üye sayısını aşamaz ⇒ sonlanma apaçık.
        var levelOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in toBuild
                     .OrderByDescending(id => readers[id])
                     .ThenByDescending(id => neighbors[id].Count)
                     .ThenBy(id => orderOf[id]))
        {
            int level = 1;
            while (levelOf.Any(placed => placed.Value == level
                       && (neighbors[id].Contains(placed.Key) || mayCollide?.Invoke(id, placed.Key) == true)))
                level++;
            levelOf[id] = level;
        }

        return [.. toBuild
            .GroupBy(id => levelOf[id])
            .OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<string>)[.. g])];
    }
}
