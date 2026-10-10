# Performans ve Resolve hızlandırma — uygulama sonrası rapor

**Kapsam:** iki onaylı plan tek oturumda uygulandı:

- PERF: `.claude/outputs/2026-10-03-04-28-performance-implementation-plan.md` — Faz 0 ve A-E; E4 karar gereği atlandı.
- RESOLVE: `.claude/outputs/2026-10-03-07-15-resolve-cycles-speedup-plan.md` — Faz 1-3. **Faz 4 başlatılmadı**; karar 11 (Cycles koşusunda
  öncelik) kullanıcı onayı bekliyor.

**Son durum:** `develop` = `934a1a6`, origin'e push'lu. Her faz aynı sırayla ilerledi: task başına uygulama, task incelemesi, düzeltme
turları, bütünsel inceleme, tam süit (yerel kapı), `--no-ff` merge, push, branch silme. `main`'e, sürüm numarasına ve CHANGELOG'a
dokunulmadı.

## 1. Fazlar ve kapanış süitleri

| Faz | Merge | Öz | Kapanış süiti (Category!=Acceptance) |
|---|---|---|---|
| PERF Faz 0 | f03b936 | Test izolasyonu guard'ı genişledi; performans tabanı ölçüldü | yeşil |
| RESOLVE Faz 1 | 3e7a5b2 | Yüzey özeti kör noktaları kapandı (parametre öznitelikleri, struct alan/layout) | yeşil |
| RESOLVE Faz 2 | 799a04d | WPF geçici assembly metadata-only derlenir (targets tek kaynak) | yeşil + WPF acceptance 3/3 |
| PERF Faz A | 28b6d89 | Tepside derleme gizli modda: ekran işi yok, pencere gelince tek seferde | 4.191 geçti |
| PERF Faz B | e9f4f8c | İş sürerken koşu komutları kapalı, tepsi balonu, Stop now, imleç tek saat, ucuz UI işleri | 4.266 geçti |
| PERF Faz C | eeca70c | Defterler yalnız kirliyse yazılır, koşu logları üç gün tutulur, bellek tanısı, WPF hedef tarihi, App tamponları | 4.405 geçti |
| PERF Faz D | 05f2880 | İşçi sayısı çekirdeğe ve boş belleğe göre kırpılır; IO paralelliği tek sabitte | 4.437 geçti |
| PERF Faz E | d8a2a43 | Resolve karar izi, paralel yüzey hash'i, koşullu restore | 4.475 geçti + Acceptance 6/6 |
| RESOLVE Faz 3 | 934a1a6 | Resolve tur 1 üye düzeyi artımlı | 4.565 geçti |

Her kapanış süitinde 6 başarısızlık aynıydı: `StickyLayerHeaderClickTests`. Bu testler masaüstü etkileşimsizken düşüyor; develop'ta da aynı
sonucun alındığı gösterildi. B ve C'de birer test daha düştü: aralıklı `IOException: İşleyici geçersiz` (ERROR_INVALID_HANDLE). Bu
düşüşler tek başına koşulunca geçiyor ve çalışmanın başından beri görülüyor.

Faz E'nin Acceptance testleri canlı OSYS ağacını Rebuild ettiği için ölçüm kampanyaları gibi snap korumasıyla koşuldu: restore yapıldı,
verify 0 fark verdi.

## 2. PERF kabul tablosu (plan §5)

| Ölçü | Taban | Hedef | Sonuç | Durum |
|---|---|---|---|---|
| Tepsiden kısayol → `runStarted` | 5,68 s (%100 ölçekte 4,41) | ≤ 2,5 s | 2,77 s (Faz A) | ❌ — kalan süre iki harici kökün seri `fetch` + `merge --ff-only` turu; karar 9 bu güncellemeye dokunulmamasını istiyor |
| Tepside no-op Rebuild, UI thread | 1.385 (1.254) Mcycles/s | ≤ 300 | 144 | ✅ |
| Tepside no-op Rebuild, App toplam | %57 (%49,1) | ≤ %25 | %15,7 | ✅ |
| Göstergede 100 ms üstü kesinti / 25 s | 7 (2) | ≤ 1 | 0 (en uzun 9 ms) | ✅ |
| Koşudan sonra tepside UI thread | 129 (81,6) | ≤ 40 | 95,1 (Faz A) | ❌ — örneklenen işin %80'i WPF dispatcher/timer iç işi; ayrı inceleme (WPR, yönetici yetkisi) bekliyor |
| Ön planda boşta (aktif / arkada) | 181 / 181 | ≤ 120 / ≤ 50 | — | ⏳ masaüstü (B4'ten sonra ölçülmedi) |
| Supervisor Private, 6 dönüş sonrası | 309 MB; +36-91/dönüş | ≤ 200 MB; +≤ 10 | — | ⏳ masaüstü (measure3 S) |
| App Private, iki rebuild sonrası tepside | 433 MB | ≤ 300 MB | — | ⏳ masaüstü (measure2 E) |
| Sync/Build başına defter yazımı (değişiklik yok) | 12 MB | 0 | 0 — "yalnız kirliyse yazılır" testle pinli | ✅ |
| Restore süresi payı (Build, paket değişmedi) | %7,1 | ~0 | harness: paket kanıtı kayıtlıyken restore çağrısı 0, 4 "restore skipped" satırı | ✅ harness / ⏳ gerçek kullanım (logmine, ≥ 10 Build) |
| Resolve grup başı hash | 5,93 s | ≤ 2 s | UI grubu 1,60 / 1,64 s; aynı gün aynı koşulda eski motorun önsözü 6,47 s | ✅ |
| Log klasörü | 1,5 GB, 1.640 klasör | ≤ 3 gün | üç gün saklama ve en son koşunun korunması testle pinli | ✅ kod / ⏳ yeni sürüm kullanılınca |
| 2 işlemci, tepside Rebuild | 51 s (4 işçi) | D1'in en iyisi (~42 s) | — | ⏳ masaüstü (measure3 T3) |

Tablo notları:

- Parantez içindeki tabanlar Faz A kabulünde %100 ekran ölçeğiyle yeniden alınan değerlerdir.
- **Grup başı hash:** E motorunun `decision.log` hash süresi olarak ölçüldü. Ölçüm, boş defterle tam Resolve sırasında ve dosya önbelleği
  soğutularak yapıldı. Planın 5,93 s tabanı soğuk okuma koşuluydu: sıcak önbellekte iki motor da ~0,4 s veriyor.
- **2 işlemci Rebuild:** Faz D'nin kuralı Balanced'ın 4 işçisini 2 işlemcide kırpmaz (sınır çekirdeğin iki katı). Bu yüzden tabana yakın
  bir sonuç beklenir. Faz D'nin asıl kazancı aşırı yüklü profillerde ve düşük bellekli makinelerdedir.

## 3. RESOLVE kabul tablosu (plan §5)

| Ölçü | Taban | Hedef | Sonuç | Durum |
|---|---|---|---|---|
| S1 tek üye değişimi, Balanced, sakin | 62-65 s (bugün Faz 2 öncesi motor: 64,8 / 66,9 s) | ≤ 50 s (Faz 2) | Faz C motoru 51,5 s; Faz E motoru 50,3 / 51,2 s (RESOLVE Faz 2 kapanışı: 47,4 s) | ✅ sınırda |
| S1, aynı | — | ≤ 10 s (Faz 3) | 5,7 s — tek üye derlendi, iş CPU'su 10,7 s. İki tekrarda 43,7 / 46,0 s (aşağıdaki bulgu) | ⚠️ 1/3 |
| S3 Clean → Optimize → Resolve | 115-122 s | ≤ 105 s (Faz 2) | 98,1 s (RESOLVE Faz 2 kapanışı) | ✅ |
| Rebuild (154 proje), Balanced | 84 s | ≤ 78 s | kararlı durumda Faz 3 motoru 27,2 s, Faz 2 öncesi motor 26,5 s; targets yüzünden toplu yeniden derleme yok | ✅ |
| S1 yük altında (`LOAD=4`) | 119 s | ≤ 85 s (Faz 4) | — | Faz 4 onay bekliyor |
| Ardışık Resolve, değişiklik yok | 1,1 s | değişmez | 1,0-1,3 s (dört motor) | ✅ |
| Çıktı eşdeğerliği | aynı | aynı | aynı (RESOLVE Faz 2 acceptance) | ✅ |

Rebuild notu: RESOLVE Faz 2 kapanışındaki geçersiz 78,2 s ölçümün nedeni targets dosyasının her koşuda yeni tarihle yazılmasıydı. Bu,
Faz C'deki "sabit eski tarih" düzeltmesiyle kapandı ve bu kampanyada doğrulandı.

**S1 bulgusu (yeni):** ölçülen üç Faz 3 koşusundan ikisinde UI.General derlenince ortak çıktı klasöründeki beslenen kopyasının
(`C:\OSYS\Client\Bin\OSYS.UI.General.dll`) yüzey özeti oynadı. Bunun üzerine 15 okuyan üye tur 2'de derlendi (16 üye, 2 tur). Karar
doğru çalıştı: yüzey oynamışsa derlemek gerekir. Ama değişiklik yalnız `AssemblyInfo.cs` sonuna eklenen bir yorum satırıydı:

- `AssemblyVersion` sabit (`1.0.0.0`), yani otomatik artan bir sürüm yok.
- Aynı içerikten iki derleme farklı yüzey özeti verdi. Kayıt koşusunun işaretli derlemesi, başlangıçtaki kopya ve ilk ölçüm koşusunun
  orijinal derlemesi aynı özeti verdi; ikinci koşunun işaretli derlemesi farklı.

Yani yüzey özeti kaynağın saf bir fonksiyonu değil. Olası nedenler: başvurulan derlemelerin çıktısına bağlı bir parça, ya da özete giren
deterministik olmayan bir derleyici artefaktı. Bu, üye düzeyi atlamanın kazancını doğrudan belirliyor. İnceleme ayrı bir iş: iki derlemenin
yüzey diff'i çıkarılmalı (`SurfaceBench`).

## 4. Ölçümle ya da incelemeyle değişen kararlar

- **Faz D işçi kırpması.** Planın varsayılan hipotezi (`cores<=2 ? 1 : cores-1`) gerçek OSYS Rebuild ölçümünde çürüdü: iki işlemcide tek
  işçi, iki işçinin neredeyse iki katı sürdü; dört işlemcide dört işçi üç işçiden hızlıydı.
  - Kural: işçi yalnız mantıksal işlemcinin iki katını aşarsa kırpılır.
  - Bellek sabiti gerçek derleme ölçümüne göre: işçi başı 512 MB + 2 GB makine payı. Ölçümde her ek işçi birkaç yüz MB commit ekledi.
    Varsayılan profil olağan boş bellekte kırpılmaz.
- **E3 paket tanığı (planın lafzından daha sıkı).** Paket, klasörü varsa değil, klasöründe `<id>.<version>.nupkg` varsa kurulu sayılır.
  Yarıda kesilen bir restore'un bıraktığı klasör kanıt sayılmaz. Çalışma alanındaki 43 paket klasörünün hepsinde bu dosya var, yani gerçek
  kullanımda atlama azalmaz. Kullanıcı veto edebilir; değişiklik tek satır.
- **RESOLVE 3.3 "gerekli" kuralları planın altı kuralından geniş (güvenli taraf).** Şunlar da "gerekli" sayılır:
  - `DepIssue` taşıyan kayıt,
  - boş okunan-yüzey listesi,
  - grup içi bağımlılıkların hepsini kapsamayan okuma kaydı,
  - boş bağımlılık kümesi,
  - terimsiz üye (Fast modu).
- **RESOLVE 3.4 iki-yeşil kuralı.** Taşınan üyesi olan bir tur 1, "iki ardışık yeşil tur" yakınsama kuralına girmez. Taşınan üyenin yapay
  "Succeeded"ı tur 1'i yeşil gösterip bayat üyeyle yakınsatıyordu. İnceleme yakaladı; kırmızı testle düzeltildi. `CycleRoundPolicy` değişmedi
  (karar 3).
- **Ölçüm zamanı.** Faz E'nin harness ölçümü, RESOLVE Faz 3 kapanışıyla tek kampanyada yapıldı. Dört motor aynı koşulda ölçüldü ve tek bir
  yedek alma/geri yükleme döngüsü yetti.

## 5. Ölçüm yöntemi ve kampanya bulguları

Arayüzsüz motor ölçümleri `cycle-resolve-perf-2026-10-02` harness'iyle yapıldı. Kullanılan motorlar:

| Motor | Commit |
|---|---|
| Faz 2 öncesi | 821e350 |
| Faz C sonrası | eeca70c |
| Faz E | d8a2a43 |
| Faz 3 | 934a1a6 |

Her dizi canlı OSYS'de `snap.py save` ile başladı ve her koşulda `restore` + `verify` ile bitti. Sonuçlar:

- Ana kampanya: 291 konum geri yüklendi, verify 0 fark.
- Faz 3 ek kampanyası: 35 konum geri yüklendi, verify 0 fark.
- Canlı defter iki kampanyanın sonunda da yedekle birebir aynı.

Kampanyadaki üç bulgu ölçüm düzeneğine ait, ürüne değil:

1. **Parmak izi ve durum klasörü.** Harness her koşuya ayrı bir durum klasörü veriyor. WPF targets dosyası o klasörde durduğu ve motor parmak
   izi targets yolunu kapsadığı için her koşunun parmak izi farklı çıktı ("engine changed"). Üretimde durum klasörü sabit
   (`%LOCALAPPDATA%\BuildOrchestrator`). Faz 3 zinciri bu yüzden tek, ortak bir durum klasörüyle yeniden ölçüldü. Ürün notu: parmak izi
   aracın kendi durum klasörünün mutlak yolunu içeriyor. Klasör taşınırsa her üye bir kez gerekli olur (güvenli taraf).
2. **Ayrı defter, ortak çıktı.** Zincirlerin defterleri ayrı, disk çıktıları ortak. Bir zincirin ilk koşusunda defter diskteki çıktıları
   tanımıyor ve tasarım gereği tarihe bakıyor ("araç dışında derlenmiş" kipi). Dört motor da bu davranışa uydu. Ölçüm koşuları buna göre
   seçildi.
3. **Önbellek soğutma hızı.** Defender, okunan PE dosyalarını tarıyor. Önbellek soğutma (`evict.py`) önce taranmayan dosyaları okuyacak
   şekilde değiştirildi; süre 672 s'den 184 s'ye indi.

## 6. Cevap bekleyen sorular

1. **RESOLVE Faz 4 / karar 11:** Cycles koşusunda öncelik. Plan bu faz için kullanıcı onayı istiyor; başlatılmadı.
2. **E3 `.nupkg` tanığı:** onay ya da veto (bkz. §4).
3. **S1 yüzey özeti bulgusu (§3):** inceleme yapılsın mı?
4. **ARCHITECTURE'daki daktilo cümlesi:** "satır yazıldıktan sonra 420 ms 'typing' sayılır" diyor. Kod satırı yazım biter bitmez bırakıyor;
   420 ms artık yalnız imleç tonunun süresi. Öneri: dokümanı koda göre düzeltmek.
5. **Motorun bellek tanı satırı:** yalnız stderr'e gidiyor, App ise stderr'i atıyor. Öneri: böyle kalsın.
6. **`BuildDurationPersister`:** üretimde çağıranı yok; kod haritası (§22) süre kaydının sahibi olarak onu gösteriyor. Öneri: haritayı
   `RunCoordinator`'a çevirmek, ölü sınıfı ve testlerini silmek.
7. **ARCHITECTURE §8.4 ve Resolve'da kalan süre:** doküman Resolve için hesaplanan bir kalan süre anlatıyor, ama bu tahmin hiçbir yerde
   gösterilmiyor. Öneri: dokümanı düzeltmek.
8. **Tur sonu "iki yeşil tur" kuralı:** kanıt kipinde tur ≥ 2 yalnız bayat üyeleri derliyor, ama kural kanıta bakmadan yakınsatıyor. Bu,
   uzun sabit zincirlerinde bir kuşak geride kalmaya yol açabilir. Kural bu işlerden önce de böyleydi. Öneri: kanıt varken bayat üye
   kaldıkça yakınsama denmesin. Bu karar 3'ü değiştirir.
9. **Eski ölçüm yedekleri:** `D:\bo-perf-snap` altında bu oturumun beş yedeği (~130 GB) duruyor: `d1workers`, `d1bmemory`, `eaccept`,
   `finalm`, `finalr3`. Hepsinin geri yüklemesi doğrulandı. Silmek için onay gerekiyor.

## 7. Park edilenler (takip)

- **Aralıklı `IOException: İşleyici geçersiz`:** kök nedeni incelenmedi. Process.Start'ta ve dizin taramasında görülüyor.
- **`StickyLayerHeaderClickTests`:** kullanıcı masa başındayken yeniden koşulmalı.
- **`PopoverTests` ve CI:** CI'da `Category=LocalOnly` almalı; gerekçesi dokümana yazılmalı.
- **Faz F incelemesi:** koşu sonrası tepside UI thread (WPR, yönetici yetkisi).
- **RESOLVE 3.4 park maddeleri:**
  - Taşınan üyenin yenilenen kaydı App satırına yansımıyor (UX).
  - Taşınan üyede `LastRunAt` yenileniyor. Bu planın açık kararı; dar bir pencerede araç dışı bir derlemeyi gizleme riski not edildi.
  - "Üretici → MovedFiles" iskeleti iki yerde.
- **Kopya ve adlandırma işleri:**
  - SHA-256 hex yardımcısı beş yerde ayrı yazılmış (ortak bir yardımcı ayrı refactor).
  - "output may be one generation behind" metni hem Core'da hem App `StreamText`'te tanımlı.
  - `EtaCalculator.BuildingProject.LastDurationMs` alan adı yanıltıcı.
- **`packages.config` iki kez okunuyor:** restore gerekirken iki kez özetleniyor (ihmal edilebilir).
- **Kozmetik süpürme:** `PROGRESS.md`'deki listeler.

## 8. Masaüstü gerektiren ölçümler (bekliyor)

Bu oturumda masaüstü etkileşimsizdi. Kullanıcı varken koşulacak ölçümler:

- **measure2:** A, C, D, E.
- **measure3:** J, S, T2, T3.
- **measure-behind**, ve Faz B için arka plandaki pencere ile taban karşılaştırması.
- **F incelemesi.**

`run-all.ps1` tam yeniden ölçüm bunları kapsar.

Kampanya çıktıları `.claude/temp/perf-2026-10-03/` altında: `d1-measure/`, `d1b-measure/`, `final-measure/` (`analysis.txt`, iki kampanya
logu), `suite/`. Ayrıntılı karar defteri: `PROGRESS.md`.
