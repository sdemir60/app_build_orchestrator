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
/// <para><b>Kural sırası</b> (ilk eşleşen neden yazılır; karar 2'nin harfleri parantezde): güvenilir kayıt yok (iii) →
/// kayıt başka bir motordan (vi) → üyenin kendi terimi yok ya da değişmiş (i) → çıktı kanıtı eksik, bu araç dışında
/// derlenmiş ya da beslenen kopyası bozuk (iv, v) → kayıtlı okuduğu bir kardeş yüzeyi artık farklı (ii). Grup çapındaki
/// nedenler (kayıt, motor) üyeye özgü olanlardan önce gelir.</para>
/// </summary>
public static class CycleMemberNeed
{
    /// <summary>Kayıt yok, başarısız ya da döngü kanıtı eksik/bozuk (karar 2 iii).</summary>
    public const string NoTrustedRecordReason = "no trusted record";

    /// <summary>Kayıt başka bir motorla yazılmış (karar 2 vi): toolset ya da build argüman sözleşmesi değişti.</summary>
    public const string EngineChangedReason = "engine changed";

    /// <summary>Bugünkü üye terimi yok (<c>DependentMode.Fast</c>: planlayıcı bileşik kurmaz, terim hesaplanmaz).
    /// "own inputs changed" demek yanlış olurdu; üye yine de gerekli — güvenli taraf.</summary>
    public const string NoMemberTermReason = "no member term";

    /// <summary>Üyenin kendi girdisi (dosyaları, configuration'ı, grup dışı upstream'i) kayıttakinden farklı (karar 2 i).</summary>
    public const string OwnInputsChangedReason = "own inputs changed";

    /// <summary>Çıktı kanıtı yok, diskte eksik ya da beslenen kopyaları bozuk (karar 2 iv).</summary>
    public const string OutputEvidenceMissingReason = "output evidence missing";

    /// <summary>Çıktı zaman kipinde: bu araç dışında (Visual Studio, satır menüsü) derlenmiş (karar 2 v).</summary>
    public const string OutputBuiltOutsideReason = "output built outside this tool";

    /// <summary>Kayıtlı okuduğu bir kardeş yüzeyi diskte farklı ya da yok (karar 2 ii); ardından kayan dosyalar
    /// <see cref="CycleDecisionLines.MovedTerm"/> biçiminde gelir (tur satırının <c>moved</c> alanıyla aynı terim).</summary>
    public const string ReadSurfaceMovedPrefix = "read surface moved: ";

    /// <summary>Diskten hiç yüzeyi okunmamış üretici için "diskte hiçbir şey yok" görünümü.</summary>
    private static readonly IReadOnlyDictionary<string, string> NoFiles = FrozenDictionary<string, string>.Empty;

    /// <summary>Bir üyenin kanıtları. <paramref name="Record"/>: defterdeki kayıt (<c>LedgerAtStart</c>);
    /// <paramref name="CurrentTerm"/>: bu koşunun üye terimi (<c>IncrementalPlan.MemberTermById</c>; yoksa null);
    /// <paramref name="Output"/>: çıktı kanıt kontrolü (<c>IncrementalPlan.ChecksById</c>; null ⇒ kanıt yok).</summary>
    public sealed record MemberEvidence(BuildState? Record, string? CurrentTerm, OutputCheck? Output);

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

            var (record, currentTerm, output) = evidence(member);

            // (iii) Güvenilir kayıt: defterde var, son derleme başarılı ve üç döngü alanı dolu. Biri null/boşsa kanıt
            // yok (eski defter, döngü dışı kayıt, yakınsamayan koşu): üye gerekli. Boş yüzey LİSTESİ null değildir —
            // "hiçbir kardeş yüzeyi okumadı" demektir ve güvenilir kanıttır.
            if (record is not
                {
                    LastResult: BuildResult.Succeeded,
                    CycleMemberTerm: { Length: > 0 } recordedTerm,
                    CycleReadSurfaces: { } recordedSurfaces,
                    CycleEngineFingerprint: { Length: > 0 } recordedEngine,
                })
            { Need(NoTrustedRecordReason); continue; }

            // (vi) Motor: kayıt başka bir toolset/argüman sözleşmesinin ürünü. Grubun HER üyesi bu kapıdan geçer.
            // (Boş parmak izi yukarıda zaten güvenilmez sayıldı; iki boş "eşit" okunmaz.)
            if (recordedEngine != engineFingerprint) { Need(EngineChangedReason); continue; }

            // (i) Kendi girdisi. Bugünkü terim yoksa (Fast: bileşik kurulmaz) karşılaştırma yapılamaz — "değişti"
            // demek yanlış olurdu, ama atlamak da yanlış: üye gerekli, nedeni ayrı.
            if (string.IsNullOrEmpty(currentTerm)) { Need(NoMemberTermReason); continue; }
            if (recordedTerm != currentTerm) { Need(OwnInputsChangedReason); continue; }

            // (iv) Çıktı kanıtı yok / diskte eksik. Zaman kipinde kanıt dosyası yoksa "araç dışında derlendi" yanlış
            // olurdu; bu yüzden kanıt eksikliği kip kontrolünden ÖNCE gelir.
            if (output is null || output.Mode == EvidenceMode.None || output.EvidenceMissing)
            { Need(OutputEvidenceMissingReason); continue; }

            // (v) Çıktı bu araç dışında derlenmiş (zaman kipi): kaynağın içeriği aynı kalsa da çıktının kimin
            // olduğu bilinmez.
            if (output.Mode != EvidenceMode.Ledger) { Need(OutputBuiltOutsideReason); continue; }

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

    /// <summary>
    /// [okunan dosya kanıtı] Okuma anında kaydedilen dosyalardan ŞİMDİ farklı olanlar (diskte artık olmayanlar dahil;
    /// özet karşılaştırması Ordinal) — boşsa üye bu üretici yüzünden bayat değildir. Yalnız kayıttaki dosyalara
    /// bakılır: üyenin okumadığı bir kopyanın değişmesi onu bayat yapmaz. Tur 1 kararı (<see cref="Decide"/>) ile
    /// tur sonu bayatlığı AYNI soruyu sorar: kaydedilen yüzey şimdiki yüzeyle hâlâ aynı mı. Sonuç, neden satırında
    /// adıyla yazılır (<see cref="ReadSurfaceMovedPrefix"/>).
    /// </summary>
    /// <param name="seen">Okuma anında kaydedilen: dosya → yüzey özeti.</param>
    /// <param name="now">Şimdiki (diskten okunan): dosya → yüzey özeti.</param>
    public static IEnumerable<string> MovedFiles(IReadOnlyDictionary<string, string> seen,
                                                 IReadOnlyDictionary<string, string> now)
    {
        foreach (var (file, hash) in seen)
            if (!now.TryGetValue(file, out string? current) || !string.Equals(hash, current, StringComparison.Ordinal))
                yield return file;
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
            foreach (string file in MovedFiles(seen, now)) moved.Add(file);
        }
        return [.. moved];
    }
}
