using System.Collections.Frozen;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Core.Planning;

/// <summary>
/// [RESOLVE Faz 3 — karar 2] Resolve'un tur 1'inde SCC üyelerinden hangilerinin derleneceği. SAF: I/O, process, saat,
/// log YOK [D3]; her kanıt çağıranın verdiği girdidir, karar yalnız onlardan çıkar.
///
/// <para><b>Güvenli taraf.</b> Yanlış "gerekmez" derlenmesi gereken üyeyi atlar ve eski bir çıktı yayar; yanlış
/// "gerekir" yalnız zaman kaybıdır. Bu yüzden her kural "gerekli" yönünde ve erken çıkışlıdır: eksik, boş ya da
/// şüpheli her kanıt "gerekli" demektir; üye ancak HİÇBİR kural tutmuyorsa taşınır (atlanır).</para>
///
/// <para><b>Kural sırası</b> (ilk eşleşen neden yazılır; karar 2'nin harfleri parantezde): güvenilir kayıt yok (iii) —
/// kayıt yok, başarısız, başarısız bir bağımlılığa link'li, döngü alanları eksik/boş, üyenin grup içi bağımlılık kümesi
/// boş ya da bağımlılıklarının hepsini kapsamayan okuma kaydı — → kayıt başka bir motordan (vi) → üyenin kendi terimi yok
/// ya da değişmiş (i) → grup dışı bir bağımlılığın yüzeyi kayıttan farklı ya da kayıtta yok (i-b) → çıktı kanıtı eksik
/// (iv) → çıktı kendi girdisinden eski (v) → beslenen kopyası bozuk (iv) → okuma
/// kaydının bütünlüğü (aynı (Producer, File) iki kez ya da eksik parçalı girdi: yine "güvenilir kayıt yok", iii) → kayıtlı
/// okuduğu bir kardeş yüzeyi artık farklı (ii). Grup çapındaki nedenler (kayıt, motor) üyeye özgü olanlardan önce gelir.
/// Çıktının KİPİ karara girmez: bu araç dışında derlenmiş ama girdilerinden yeni bir çıktı, üyenin terimi aynı ve okuduğu
/// yüzeyler diskle aynıysa aynı girdilerden üretilmiştir.</para>
/// </summary>
public static class CycleMemberNeed
{
    /// <summary>Kayıt yok, başarısız, başarısız bir bağımlılığa link'li (<c>DepIssue</c>) ya da döngü kanıtı
    /// eksik/bozuk — boş yüzey listesi, boş grup içi bağımlılık kümesi ve bağımlılıkları kapsamayan okuma kaydı dahil
    /// (karar 2 iii).</summary>
    public const string NoTrustedRecordReason = "no trusted record";

    /// <summary>Kayıt başka bir motorla yazılmış (karar 2 vi): toolset ya da build argüman sözleşmesi değişti.</summary>
    public const string EngineChangedReason = "engine changed";

    /// <summary>Bugünkü üye terimi yok (<c>DependentMode.Fast</c>: planlayıcı bileşik kurmaz, terim hesaplanmaz).
    /// "own inputs changed" demek yanlış olurdu; üye yine de gerekli — güvenli taraf.</summary>
    public const string NoMemberTermReason = "no member term";

    /// <summary>Üyenin kendi girdisi (dosyaları ya da configuration'ı) kayıttakinden farklı (karar 2 i). Grup dışı upstream bu
    /// terime girmez — onun değişimi <see cref="DependencySurfaceMovedPrefix"/> ile söylenir (i-b).</summary>
    public const string OwnInputsChangedReason = "own inputs changed";

    /// <summary>[D7-b] Grup DIŞI bir doğrudan bağımlılığın kayıttaki yüzeyi (<see cref="BuildState.DependencySurfaces"/>) grup
    /// başında okunan diskten farklı, diskte yok ya da kayıtta hiç yok (kural i-b); ardından dosyalar
    /// <see cref="CycleDecisionLines.MovedTerm"/> biçiminde gelir.</summary>
    public const string DependencySurfaceMovedPrefix = "dependency surface moved: ";

    /// <summary>Çıktı kanıtı yok, diskte eksik ya da beslenen kopyaları bozuk (karar 2 iv).</summary>
    public const string OutputEvidenceMissingReason = "output evidence missing";

    /// <summary>Zaman kipinde üyenin KENDİ girdisi çıktıdan yeni (karar 2 v): çıktı mevcut kaynaktan üretilmemiş olabilir —
    /// VS derlemesinden sonra düzenleme, branch değişimi.</summary>
    public const string OutputOlderThanInputsReason = "output older than its inputs";

    /// <summary>Kayıtlı okuduğu bir kardeş yüzeyi diskte farklı ya da yok (karar 2 ii); ardından kayan dosyalar
    /// <see cref="CycleDecisionLines.MovedTerm"/> biçiminde gelir (tur satırının <c>moved</c> alanıyla aynı terim).</summary>
    public const string ReadSurfaceMovedPrefix = "read surface moved: ";

    /// <summary>Diskten hiç yüzeyi okunmamış üretici için "diskte hiçbir şey yok" görünümü.</summary>
    private static readonly IReadOnlyDictionary<string, string> NoFiles = FrozenDictionary<string, string>.Empty;

    /// <summary>Bir üyenin kanıtları. <paramref name="Record"/>: defterdeki kayıt (<c>LedgerAtStart</c>);
    /// <paramref name="CurrentTerm"/>: bu koşunun üye terimi (<c>IncrementalPlan.MemberTermById</c>; yoksa null);
    /// <paramref name="Output"/>: çıktı kanıt kontrolü (<c>IncrementalPlan.ChecksById</c>; null ⇒ kanıt yok);
    /// <paramref name="InGroupDependencies"/>: üyenin DOĞRUDAN grup içi bağımlılıkları (proje referansları ∩ grubun
    /// üyeleri, tam csproj yolu). Kayıt bunların HER BİRİ için en az bir okuma girdisi taşımalıdır (üretici kimlikleri
    /// OrdinalIgnoreCase eşleşir). İki ya da daha çok üyeli bir SCC'de her üyenin en az bir doğrudan grup içi bağımlılığı
    /// vardır: BOŞ küme güvenilmez sayılır (üye gerekli) ve çağıranın hatası gereksiz derleme olarak görünür kalır.
    /// Çağıran kümeyi TAM hesaplamalıdır: eksik verilen bağımlılık denetlenmez.
    /// <paramref name="OutsideDependencies"/>: [D7-b] üyenin grup DIŞI doğrudan bağımlılıkları; kayıt
    /// (<see cref="BuildState.DependencySurfaces"/>) her biri için yüzey taşımalı ve disk (<c>surfaceState</c>) ile aynı
    /// olmalı (kural i-b). Boş küme ⇒ grup dışı bağımlılık yok, kural etkisiz.</summary>
    public sealed record MemberEvidence(BuildState? Record, string? CurrentTerm, OutputCheck? Output,
                                        IReadOnlyCollection<string> InGroupDependencies,
                                        IReadOnlyCollection<string> OutsideDependencies);

    /// <summary>Kararın sonucu. <paramref name="ToBuild"/>: build order'a göre sıralı gerekli üyeler.
    /// <paramref name="CarriedReadStates"/>: TAŞINAN (atlanan) üye → kayıttan kurulan üretici → dosya → yüzey özeti
    /// (tur sonu bayatlığının referansı). <paramref name="Reasons"/>: GEREKLİ üye → ilk eşleşen neden (decision.log
    /// için); taşınanların nedeni yoktur.</summary>
    public sealed record Decision(
        IReadOnlyList<string> ToBuild,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> CarriedReadStates,
        IReadOnlyDictionary<string, string> Reasons);

    /// <summary>Tur 1'de hangi üyelerin derleneceğine karar verir.</summary>
    /// <param name="members">Grup üyeleri, build order'da; <see cref="Decision.ToBuild"/> bu sırayı korur.</param>
    /// <param name="evidence">Üye kimliği → kanıtları.</param>
    /// <param name="surfaceState">Grup başında diskten okunan yüzeyler: üretici → dosya → yüzey özeti.</param>
    /// <param name="engineFingerprint">Bu koşunun motor parmak izi (<c>EngineFingerprint.Compute</c>).</param>
    public static Decision Decide(IReadOnlyList<string> members, Func<string, MemberEvidence> evidence,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> surfaceState, string engineFingerprint)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(surfaceState);
        ArgumentNullException.ThrowIfNull(engineFingerprint);

        var toBuild = new List<string>();
        var carried = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>>(
            StringComparer.OrdinalIgnoreCase);
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string member in members)
        {
            void Need(string reason)
            {
                toBuild.Add(member);
                reasons[member] = reason;
            }

            var (record, currentTerm, output, inGroupDependencies, outsideDependencies) = evidence(member);

            // (iii) Güvenilir kayıt: defterde var, son derleme başarılı, başarısız bir bağımlılığa link'li DEĞİL ve üç
            // döngü alanı dolu. Biri null/boşsa kanıt yok (eski defter, döngü dışı kayıt, yakınsamayan koşu): üye
            // gerekli. DepIssue: kayıt başarısız bir bağımlılığın çıktısına link'liydi ve kaynak değişmese de yeniden
            // derlemenin tek sinyali odur (ConditionalRebuild); kökler (DepIssueRoots) bilinse de bilinmese de üye
            // gerekli. Yüzey listesi BOŞ da olamaz: döngüdeki her üye en az bir kardeşin çıktısını okur, meşru kayıt asla
            // boş liste taşımaz; boş liste ancak bir yazım hatasından doğar ve üyeyi yüzey kontrolü olmadan sonsuza dek
            // taşırdı.
            if (record is not
                {
                    LastResult: BuildResult.Succeeded,
                    DepIssue: false,
                    CycleMemberTerm: { Length: > 0 } recordedTerm,
                    CycleReadSurfaces: { Count: > 0 } recordedSurfaces,
                    CycleEngineFingerprint: { Length: > 0 } recordedEngine,
                })
            { Need(NoTrustedRecordReason); continue; }

            // (iii) Kayıt, üyenin HER grup içi bağımlılığı için en az bir okuma girdisi taşımalı. Kayıtta olmayan bir
            // üreticinin yüzeyi oynasa da karar bunu GÖREMEZ (yazıcı bir üreticiyi düşürmüş olabilir): kısmi okuma kaydı
            // güvenilmez. Bağımlılıkları çağıran hesaplar (üyenin proje referansları ∩ grubun üyeleri). Küme BOŞ da
            // olamaz: iki ya da daha çok üyeli bir SCC'de her üyenin (güçlü bağlılık gereği) en az bir doğrudan grup içi
            // bağımlılığı vardır; boş küme ancak çağıranın hatasıdır ve kuralı sessizce etkisiz bırakıp bayat bir üyeyi
            // taşırdı — hata gereksiz derleme olarak görünür kalsın.
            if (inGroupDependencies.Count == 0 || !CoversEveryDependency(recordedSurfaces, inGroupDependencies))
            { Need(NoTrustedRecordReason); continue; }

            // (vi) Motor: kayıt başka bir toolset/argüman sözleşmesinin ürünü. Grubun HER üyesi bu kapıdan geçer.
            // (Boş parmak izi yukarıda zaten güvenilmez sayıldı; iki boş "eşit" okunmaz.)
            if (recordedEngine != engineFingerprint) { Need(EngineChangedReason); continue; }

            // (i) Kendi girdisi. Bugünkü terim yoksa (Fast: bileşik kurulmaz) karşılaştırma yapılamaz — "değişti"
            // demek yanlış olurdu, ama atlamak da yanlış: üye gerekli, nedeni ayrı.
            if (string.IsNullOrEmpty(currentTerm)) { Need(NoMemberTermReason); continue; }
            if (recordedTerm != currentTerm) { Need(OwnInputsChangedReason); continue; }

            // (i-b) [D7-b] Grup dışı upstream: terim onu taşımaz; kayıttaki bağımlılık yüzeyi diskle (grup başında okunan)
            // karşılaştırılır. Kayıtta olmayan ya da diskte olmayan bağımlılık "taşındı" sayılır (güvenli taraf).
            if (OutsideSurfacesMoved(record.DependencySurfaces, outsideDependencies, surfaceState) is { Count: > 0 } movedOutside)
            { Need(DependencySurfaceMovedPrefix + CycleDecisionLines.MovedTerm(movedOutside)); continue; }

            // (iv) Çıktı kanıtı yok / diskte eksik. Zaman kipinde kanıt dosyası yoksa "kendi girdisinden eski" yanlış
            // olurdu; bu yüzden kanıt eksikliği önce gelir.
            if (output is null || output.Mode == EvidenceMode.None || output.EvidenceMissing)
            { Need(OutputEvidenceMissingReason); continue; }

            // (v) Zaman kipinde kendi girdisi çıktıdan yeni: çıktının mevcut kaynaktan üretildiği bilinemez. Kip tek başına
            // neden DEĞİLDİR — Fresh/DependencyNewer çıktı kaynaktan sonra üretilmiştir ve kalan kurallarla sınanır.
            if (output.Mode == EvidenceMode.Time && output.Time == TimeVerdict.OwnNewer)
            { Need(OutputOlderThanInputsReason); continue; }

            // (iv) Beslenen kopyalar (paylaşılan klasördeki DLL) eksik, boyutu farklı ya da kanıttan eski: bağımlılar
            // başka bir çıktıya link'lenir.
            if (!output.FedIntact) { Need(OutputEvidenceMissingReason); continue; }

            // (ii) Kayıtlı okunan yüzeyler. Aynı (Producer, File) iki kez ya da eksik parçalı girdi: kayıt şüpheli.
            if (ReadStatesOf(recordedSurfaces) is not { } readStates) { Need(NoTrustedRecordReason); continue; }

            var moved = MovedSurfaceFiles(readStates, surfaceState);
            if (moved.Count > 0) { Need(ReadSurfaceMovedPrefix + CycleDecisionLines.MovedTerm(moved)); continue; }

            // Hiçbir kural tutmadı: taşınır. Okuma durumu kayıttan kurulur; tur sonu bayatlığı buna karşı sorulur.
            carried[member] = readStates;
        }

        return new Decision(toBuild, carried, reasons);
    }

    // [D7-b] Grup dışı bağımlılıklardan yüzeyi kayıttakinden farklılaşmış, diskte olmayan ya da kayıtta hiç olmayanların
    // dosyaları: harf-duyarsız sıralı ve tekil. Kayıtta girdi yoksa dosya adı bilinmez — diskte grup başında okunan ilk
    // dosya, o da yoksa bağımlılığın kimliği yazılır (yine "gerekli" yönünde).
    private static List<string> OutsideSurfacesMoved(IReadOnlyList<CycleReadSurface>? recorded,
        IReadOnlyCollection<string> outsideDependencies,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> surfaceState)
    {
        var moved = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dep in outsideDependencies)
        {
            var seen = recorded?.FirstOrDefault(s => string.Equals(s?.Producer, dep, StringComparison.OrdinalIgnoreCase));
            var now = surfaceState.TryGetValue(dep, out var files) ? files : NoFiles;
            if (seen?.File is null || seen.Hash is null || !now.TryGetValue(seen.File, out string? current) || current != seen.Hash)
                moved.Add(seen?.File ?? now.Keys.FirstOrDefault() ?? dep);
        }
        return [.. moved];
    }

    // Kayıt, üyenin HER grup içi bağımlılığı için en az bir okuma girdisi (Producer == bağımlılık) taşıyor mu.
    // Karşılaştırma OrdinalIgnoreCase (Windows yolu). Null eleman ya da null üretici hiçbir bağımlılığı kapsamaz
    // (bozuk girdi; ReadStatesOf de kaydı güvenilmez sayar) ve çökertmez.
    private static bool CoversEveryDependency(IReadOnlyList<CycleReadSurface> surfaces,
                                              IReadOnlyCollection<string> dependencies)
    {
        foreach (string dependency in dependencies)
            if (!surfaces.Any(surface => string.Equals(surface?.Producer, dependency, StringComparison.OrdinalIgnoreCase)))
                return false;
        return true;
    }

    // Kayıtlı yüzeyleri üretici → dosya → özet biçimine çevirir; aynı (Producer, File) iki kez ya da eksik parçalı
    // (null) girdi ⇒ kayıt şüpheli ⇒ null (çağıran üyeyi gerekli sayar). Bozuk bir defter dosyasındaki null girdi
    // sözlük anahtarı olamaz; karar çökmek yerine güvenli tarafa düşer. Anahtarlar OrdinalIgnoreCase: kanonik sıra
    // kuralıyla (BuildState.CycleReadSurfaces) ve çağıranın sözlükleriyle aynı.
    private static Dictionary<string, IReadOnlyDictionary<string, string>>? ReadStatesOf(
        IReadOnlyList<CycleReadSurface> surfaces)
    {
        var byProducer = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var surface in surfaces)
        {
            if (surface?.Producer is null || surface.File is null || surface.Hash is null) return null;
            if (!byProducer.TryGetValue(surface.Producer, out var files))
                byProducer[surface.Producer] = files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!files.TryAdd(surface.File, surface.Hash)) return null;
        }
        return byProducer.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, string>)pair.Value,
                                       StringComparer.OrdinalIgnoreCase);
    }

    // Kayıttaki yüzeylerden diskte farklılaşmış ya da kaybolmuş dosyalar: harf-duyarsız sıralı ve tekil (tur satırının
    // moved alanıyla aynı sıra). Diskte hiç yüzeyi olmayan üretici ⇒ kayıttaki her dosyası taşınmış sayılır.
    private static List<string> MovedSurfaceFiles(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> readStates,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> surfaceState)
    {
        var moved = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (producer, seen) in readStates)
        {
            var now = surfaceState.TryGetValue(producer, out var current) ? current : NoFiles;
            foreach (string file in CycleReadFiles.MovedFiles(seen, now)) moved.Add(file);
        }
        return [.. moved];
    }
}
