# Derin performans analizi — bulgu kataloğu

Ana rapor: `.claude/outputs/2026-10-02-03-07-performance-deep-analysis.md`. Bu dosya, 16 boyutta çıkarılan 154 bulgunun tam metnidir
(konum, kanıt, etki, çözüm, test fikri) ve her bulgunun bağımsız doğrulayıcı notunu içerir. Ham JSON: `.claude/temp/perf-2026-10-01/`.

Doğrulama etiketleri: `confirmed` doğrulandı · `downgraded` önemi düşürüldü ya da sayısı düzeltildi · `upgraded` yükseltildi · `refuted` çürütüldü.
Kanıt türleri: `olculdu` · `koddan-kanitli` · `tahmin`. "Doğrulayıcı düzeltmesi" satırı varsa geçerli olan odur.

## Boyutlar

| Kod | Konu |
|---|---|
| D1 | Boşta CPU (ön plan, tepsi) |
| D2 | Bellek (App, Supervisor) |
| D3 | Kapanış ve durdurma |
| D4 | Build motoru |
| D5 | Resolve cycles |
| D6 | UI akışı |
| D7 | Animasyonlar ve arka plan çizimi |
| D8 | Açılış ve kapanış süresi |
| D9 | Disk ve durum dosyaları |
| D10 | IPC ve Supervisor CPU |
| D11 | Git, otomatik Sync, arka plan tetikleyiciler |
| N1 | Tepside derleme yolu |
| N2 | Sağ alttaki gösterge animasyonu |
| N3 | UI donma kaynakları |
| N4 | Zayıf donanım uygunluğu |
| N5 | Motor belleği |

## Bulgular

### D5-resolve-cycles

#### D5-1 — UI grubunda 2. tur 17/17 koştu: yüzey kanıtı (hash-mode) grup başında SESSİZCE kapanmış; tek okunamayan dosya tüm grubun kanıtını iptal ediyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Supervisor/RunCoordinator.cs:1527-1533 (sessiz düşüş `else hashMode = false;` satır 1532), 1501-1512 (SurfaceStateOf), 1673-1680 (tur ortası düşüş — bu LOGLAR), 1741 (toBuild); src/BuildOrchestrator.Core/Incremental/ApiSurfaceHash.cs:40-53 (OfFile, `catch { return null; }`); tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs:1367
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1526-1533 — hashMode başlangıç döngüsü: bir üreticinin SurfaceStateOf'u null dönerse `else hashMode = false;` — HİÇBİR decision.log satırı yazılmaz (tur ortasındaki 1673-1680 dalı ise 'output surface unreadable' yazar)
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1501-1512 — SurfaceStateOf: kanıt yolu + beslenen kopyaların her biri hash'lenir; TEK dosya null ⇒ üretici null ⇒ grup tamamen tam-tur kipine düşer ('kanıt YARIM olmaz' yorumu 1519-1522)
- `src/BuildOrchestrator.Core/Incremental/ApiSurfaceHash.cs` satır 41-53 — OfFile: her istisna `catch { return null; }` — kilitli dosya, PE/metadata hatası ve kod hatası ayırt edilmez, neden kaybolur
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1717-1741 — staleNow yalnız hashMode'da hesaplanır; null ⇒ `toBuild = members` (herkes 2. tura girer) ve Decide klasik iki-yeşil-tur kuralına düşer
- `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs` satır 1365-1382 — an_unreadable_surface_falls_back_to_full_rounds: tam-tur düşüşü pinli ama log satırı ve 'yalnız o üreticinin okuyucuları' davranışı pinli değil

**Kanıt:** Koşu run-20261001-010430-305: 17 UI üyesinin HEPSİ 2. turda yeniden derlendi (proje loglarında üye başına 2 build invoke; tur-1 seviye başlangıçları 01:04:54/05:09/05:17/05:24/05:45/06:07, tur-2 01:06:15/06:28/06:35/06:41/07:02/07:28 — aynı 6-seviyeli plan, yani toBuild=17). Eleme kanıtı: csc /reference satırlarından çıkarılan gerçek 77 kenarla CycleRoundLevels.Compute'u yeniden ürettim, gözlenen seviyelerle BİREBİR eşleşti; son seviyedeki Service.Report ve Accounting.Common.UI'nin TÜM kardeş bağımlılıkları önceki seviyelerde derlendiği için hash-mode açıkken bayat olmaları imkânsızdır (read[dep] = surfaceState[dep] taze, tur sonunda değişmez) — buna rağmen ikisi de 2. turda derlendi ⇒ hashMode bu grupta baştan false idi. Aynı koşuda küçük gruplar hash-mode ile çalıştı (Business.NewSales/Finance, NewSales.Sales/Pricing, Types.General.Common grubu TEK turda converged — eski kural round≥2 isterdi). decision.log'da 'output surface unreadable' satırı yok ⇒ tur ortası dalı değil, sessiz başlangıç dalı. 34 dosyanın (17 üye × bin\Debug + C:\OSYS\Client\Bin kopyası) hepsi bugün mevcut ve geçerli managed PE (script ile doğrulandı: evaluation-cache HintPath'lerinden türetilen FedCandidates + CLI header kontrolü); yani o an geçici kilit ya da OfStream içindeki yakalanan bir istisna — loglardan AYIRT EDİLEMEZ.

**Etki:** Ölçülen: 2. tur 01:06:15→01:07:34 = 79 s = toplam 184 s'nin %43'ü. Kaynak turlar arasında değişmediği için bu turun tamamı kanıtsız tekrar. Kullanıcının 'hâlâ 3-4 dk' şikâyetinin en büyük tek kalemi.

**Etki (doğrulayıcı düzeltmesi):** Ölçülen: 2. tur 01:06:15 → 01:07:34 = 79 s, 184 s'lik koşunun %43'ü. Sıklık: 73c1db2 sonrası 5 UI Resolve koşusunun 1'i. 'Kullanıcının şikâyetinin en büyük tek kalemi' yalnız bu koşu için doğru.

**Çözüm:** (1) Başlangıç düşüşünü LOGLA: 1532'de `Decide(run.Logs, "cycle {lider}: surface evidence unavailable at start — {üretici}: {dosya} ({neden}) — full rounds")`; nedeni taşımak için ApiSurfaceHash'e `TryOfFile(path, out string? reason)` (istisna mesajı / 'locked' / 'bad image') ekle, OfFile onu sarsın (kopya yok). (2) Kanıtı GRUP değil KENAR düzeyinde düşür: okunamayan dosya için hash yerine bir NESİL JETONU tut (`gen#0` başlangıç; üretici her derlendiğinde `gen#N` artar). Moved() jeton farkını 'değişti' okur ⇒ o üreticiyi tur içinde KENDİNDEN ÖNCE okuyan üyeler bayat sayılır (klasik 'bir tur sonra yeniden derle' kanıtı o kenara indirgenir), diğer tüm kenarlar hash kanıtıyla yargılanmaya devam eder; üretici yeniden derlenmezse okuyucuları yakınsar (sonsuz tur yok). CycleRoundPolicy saf kalır, staleNow sözleşmesi değişmez. Bu, 1519-1522'deki 'kısmi bilgiyle karar tahmin olurdu' gerekçesini TERSİNE çevirir — yeni kanıt: bayatlık üye başına 'okuduğu kenarlardan HERHANGİ biri değişti mi' sorusudur; bilinmeyen kenarı 'değişti' saymak tahmin değil muhafazakâr yöndür, öteki kenarların kanıtını atmak ise bilgi kaybıdır. (3) Geçici kilit için OfFile'ı 2-3 kez 100-200 ms arayla yeniden dene (kilit IOException'ı için). Kırmızı testler: (a) B'nin dosyası okunamazken yalnız B'yi B'den ÖNCE okuyan üye 2. tura girer, B'yi okumayan üye girmez; (b) decision.log'da 'surface evidence unavailable' satırı ve dosya adı; (c) tur ortası düşüş de aynı kenar-düzeyi davranışa iner.

**Çözüm (doğrulayıcı düzeltmesi):** Sıra: (1) ÖNCE yalnız log — 1532'ye Decide satırı (üretici + dosya + neden); nedeni taşımak için ApiSurfaceHash'e tek bir TryOfFile, OfFile onu sarar. Kök neden görülmeden (2) ve (3) yapılmamalı. (2) Kenar düzeyi nesil jetonu, RunCoordinator 1519-1522 yorumundaki ve ARCHITECTURE.md §8.8 satır 1517-1518'deki ('a file that cannot be read before or during the run — the group falls back to full rounds') yazılı kararı değiştirir; CLAUDE.md gereği kullanıcı kararı ister. (3) 100-200 ms'lik yeniden deneme, neden kilit değilse boşa bekleme olur.

**Değişmezler:** Shell-out, nested job, OutDir, git, NDJSON, Core'da planlama: hiçbirine dokunmaz. Yalnız §8.8'deki 'Without surface evidence … the group falls back to full rounds' cümlesi ve RunCoordinator 1519-1522 'kanıt YARIM olmaz' bilinçli kararı kenar-düzeyine yeniden yazılır (gerekçe yukarıda).

**Test fikri:** Sahte SurfaceDisk'te B null döndürsün: beklenen çağrı dizisi A#1,B#1,C#1 sonra yalnız A#2 (A, B'yi B'den önce okudu); C (B'yi okumayan) tekrar derlenmez; decision.log'da 'surface evidence unavailable at start' + B'nin dosya adı.

**Doğrulayıcı notu:** Doğrulanan: (1) 17 UI üyesinin 17'si 2. turda derlendi — proje loglarında 'Oluşturma başlatıldı' sayısı üye başına 2 (packages.config'li 4 üyede restore ile 3). (2) Başlangıç dalı (1532) hiçbir satır yazmıyor, tur ortası dalı (1678) yazıyor; o koşunun decision.log'unda 'unreadable' 0 kez geçiyor. (3) Aynı koşuda Business.NewSales/Finance ve NewSales.Sales/Pricing üye başına TEK derlemeyle converged oldu, yani yüzey kanıtı koşuda açıktı. Eleme mantığı kodla tutarlı: surfaceState[dep] yalnız dep derlenince yenilenir, komşular aynı seviyede olamaz, dolayısıyla son seviyedeki üye hashMode açıkken bayat çıkamaz. Düzeltilen: bu KALICI bir kusur değil. 73c1db2 (2026-09-27 16:29) sonrasındaki 5 UI Resolve koşusunun 3'ü tek turda bitti (09-27 20:30, 09-28 12:18, 09-29 13:50), 1'i seçici turla (09-29 13:31: Service.Report 1, DMS/WorkOrder 2 derleme), yalnız 10-01 tam 2 tur koştu. 'Bloklayıcı' değil 'önemli'. Kök neden (kilit mi, OfStream içinde yakalanan istisna mı) loglardan ayırt edilemiyor — bulgu ajanının da kabul ettiği gibi. Ek gözlem: UI loglarının CreationTime'ı (OpenProjectLog, hash döngüsünden SONRA) 01:04:54.219, önceki grup 01:04:48.292'de converged ⇒ preamble 5,93 s; kanıtın açık kaldığı 09-29 13:31 koşusunda aynı aralık 7,29 s. Döngü ilk null'da kırıldığı hâlde süre benzer ⇒ okunamayan dosya sıranın sonlarındaydı (zayıf kanıt).

#### D5-2 — ETA cycle terimi turları İKİ KEZ sayıyor: defterdeki LastDurationMs zaten turların toplamı, EtaCalculator onu bir daha BaselineRounds ile çarpıyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | refuted · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2398-2413 (tahmin kaynağı observedAverageMs), 470 ve 2345 (yorum: 'App'te BuildState.LastDurationMs YOK'); src/BuildOrchestrator.Core/Incremental/EtaCalculator.cs:116-119; ARCHITECTURE.md:1292
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1652 — `member.DurationMs += outcome.DurationMs; // süre TURLARIN TOPLAMI`
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2001-2020, 2159-2181 — ReportCycleMember → ReportProjectResult(totalDurationMs) → PersistBuildStateOnSuccess(durationMs) ⇒ BuildState.LastDurationMs = tur toplamı
- `src/BuildOrchestrator.Core/Incremental/EtaCalculator.cs` satır 116-119 — `raw += cycleQueuedSum * CycleRoundPolicy.BaselineRounds;` — toplam süre bir daha ×2, paralelliğe bölünmez
- `ARCHITECTURE.md` satır 1285-1307 (§8.4) — 'multiplied by the baseline round count' ile 'carrying the sum of its rounds as the duration' (§8.8) aynı anda doğru; çift sayım dokümanda görünmüyor

**Kanıt:** decision.log'daki 17 UI üyesinin persist edilen süreleri toplamı 325.059 ms (ör. SparePart.Finance 47.262 = 21,26+25,57 s iki tur). Bir sonraki Resolve'da ETA terimi = 325 s × 2 = 650 s; grubun gerçek duvar süresi 160 s (01:04:54→01:07:34). Küçük gruplar dahil ~4× fazla vaat.

**Etki:** Kullanıcı ribbon'da ~10-11 dk görürken koşu 3 dk sürer; 'çok uzun sürüyor' algısını büyütür, ETA güvenini bitirir.

**Etki (doğrulayıcı düzeltmesi):** Gerçek sapma TERS yönde. 10-01 koşusunda UI grubu başlarken (01:05:20) bitmiş 16 üyenin süre toplamı 35.203 ms, ortalama 2.200 ms; ETA cycle terimi = 17 × 2,2 s × 2 = ~75 s; gerçek kalan süre 01:05:20 → 01:07:34 = 134 s. ETA ~1,8× AZ gösterdi. İlk grup bitene kadar bilinen süre olmadığı için ETA hiç yok.

**Çözüm:** Defterdeki değeri TUR BAŞINA yaz: ReportCycleMember'a üyenin bu koşuda derlendiği tur sayısını ver, `durationMs / roundsCompiled` persist et (ETA'nın ×BaselineRounds bütçesi o zaman doğru okunur); alternatif olarak ETA'da çarpanı kaldırıp toplamı olduğu gibi kullan — ilki seçici turlarla (1 turda biten üye) daha dürüst. §8.4/§8.8 doc'u ve EtaCalculator/CycleRounds ETA testleri yeni kurala göre yeniden yazılır (CLAUDE.md 'davranış değişince test değişir'). Ek (ayrı karar): cycle terimini paralelliğe değil grubun beklenen seviye genişliğine (CycleRoundLevels'ın plandan hesaplayabileceği ortalama genişlik ~2,8) bölmek 4× → ~1,4× sapmaya indirir.

**Çözüm (doğrulayıcı düzeltmesi):** Önce doküman/kod uyuşmazlığı kullanıcıya sorulmalı: ARCHITECTURE.md §8.4 satır 1292 'The per-project estimate comes from BuildState.LastDurationMs' diyor, kod koşu içi ortalamayı kullanıyor. Defter beslenecekse o zaman 'tur başına süre' sorusu anlam kazanır; bugün değişiklik gerekmez.

**Değişmezler:** Değişmezlere dokunmaz; §8.4'teki 'uzun süren ETA daha iyi hata' kararı korunur ama 4× sapma o kararın kapsamı değildir.

**Test fikri:** İki turda 10+10 s derlenen üye için persist edilen LastDurationMs=10.000 (tur başına) ve ETA katkısı 20 s olduğunu pinle; eski 'toplam × 2 = 40 s' iddiasını yeni kuralla yeniden yaz.

**Doğrulayıcı notu:** İddia edilen mekanizma kodda yok. ETA defterdeki LastDurationMs'i OKUMUYOR: RunViewModel.UpdateEta hem sıradan hem cycle kovasını `observedAverageMs` ile, yani BU koşuda bitmiş satırların DurationMs ortalamasıyla dolduruyor (satır 2398-2404). LastDurationMs'in src altında ETA için hiçbir okuyucusu yok (yalnız yazılıyor ve eşitlik/hash'e giriyor). Dolayısıyla '325 s persist → 650 s ETA, 4,1× fazla' sayısı hiçbir kod yolunda oluşmaz. Persist edilen değerin turların toplamı olduğu doğru (1652 → ReportCycleMember → PersistBuildStateOnSuccess) ama zararsız.

#### D5-3 — Grup içi üye düzeyinde artımlılık yok: bir üyede tek gövde değişikliği 17 üye × tur öder (bileşik imza 'bütünüyle kirli')

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | uzun / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Core/Incremental/IncrementalPlanner.cs:171-210 (ComputeComponent); src/BuildOrchestrator.Supervisor/RunCoordinator.cs:787-796 (grup yalnız TÜM üyeler WillBuild==false ise atlanır), 1582 (`toBuild = members`), 2170-2180 (BuiltContent/FedOutputs persist); src/BuildOrchestrator.Contracts/Model/ProjectModels.cs:136 ve sonrası (BuildState)
- `src/BuildOrchestrator.Core/Incremental/IncrementalPlanner.cs` satır 171-210 — ComputeComponent: SCC için TEK bileşik imza; tüm üyeler aynı değeri okur ⇒ grup ya bütünüyle kirli ya temiz
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 785-796 — Cycles tohumu: grup yalnız TÜM üyeler WillBuild==false ise atlanır; aksi halde BuildCycleGroupAsync herkesi derler
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1580-1583 — `IReadOnlyList<string> toBuild = members;` — tur 1 HERKES
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2170-2181 — PersistBuildStateOnSuccess zaten üye başına BuiltContent (içerik parmak izi) ve FedOutputs yazıyor — üye düzeyi kanıtın yarısı defterde var
- `src/BuildOrchestrator.Contracts/Model/ProjectModels.cs` satır 136-160 — BuildState: BuiltSignature (bileşik), BuiltContent, FedOutputs var; 'okunan kardeş yüzeyleri' ve 'dış upstream terimi' alanı YOK

**Kanıt:** Koşuda 33 üyenin 33'ü de derlendi; 7 SCC'nin 7'si kirliydi (evaluation-cache'ten türetilen SCC DAG'ı: Types SCC0→SCC4→SCC5, Business SCC1→SCC2→SCC3 zinciri ve UI SCC6'nın yalnız Types SCC'lerine bağlı olması ile uyumlu). Tur-1 maliyeti UI grubunda 81 s (17 derleme, 6 seviye); tipik geliştirici döngüsünde (bir UI üyesinde gövde değişikliği) gereken iş 1 derlemedir (ör. DMS 2,8 s, WorkOrder 20,6 s). Yüzey kanıtı yalnız TUR-2'yi daraltıyor (1741), tur-1'i değil.

**Etki:** Tipik 'tek dosya değişti → Resolve' senaryosunda ~100-184 s yerine ~15-45 s mümkün (preamble ~6 s + 1 derleme 3-26 s + persist). API değiştiyse + doğrudan okuyucular (ör. WorkOrder değişince 4 okuyucu, +~40 s; General değişince 15 okuyucu — bugünkü maliyete yakın).

**Etki (doğrulayıcı düzeltmesi):** Ölçülen taban: UI grubunda tur 1 = 01:04:54 → 01:06:15 = 81 s (17 derleme). 'Tek gövde değişikliğinde 184 s → 30-45 s' TAHMİN; hangi senaryonun tipik olduğu (tek üye mi, ortak upstream mi) bilinmiyor — 10-01 koşusunda 7 SCC'nin 7'si kirliydi, yani o koşuda bu tasarım hiçbir şey kazandırmazdı.

**Çözüm:** Tasarım (bileşik imza ve converged-persist KORUNARAK): (a) Defter kaydına üye başına iki alan ekle: `ReadSurfaces` (kardeş → okunan dosya → ApiSurfaceHash; tur döngüsündeki member.ReadStates'in son converged hâli — zaten hesaplanıyor, yalnız persist edilmiyor) ve `ExternalUpstream` (üyenin SCC-dışı bağımlılıklarının imza terimlerinin hash'i — ComputeComponent'in Upstream(depId) memo'sundan, yeni hesap yok). (b) Grup dirty ise tur-1 toBuild'ini daralt: üye derlenir ⇔ BuiltContent ≠ güncel ContentById ∨ ExternalUpstream ≠ güncel ∨ kanıt dosyası yok/zaman kipinde 'başkası yazdı' (OutputEvidence.Inspect) ∨ ReadSurfaces kaydı yok ∨ ReadSurfaces'taki herhangi bir dosyanın hash'i ŞU ANKİ diskteki yüzeyden farklı (grup başında zaten hesaplanan surfaceState ile karşılaştır — ek I/O yok) ∨ son sonucu güvenilmez/Failed. Diğer üyeler 'settled' başlar: member.ReadStates = defterdeki kayıt, Result=Succeeded; mevcut staleNow mekanizması (1720-1733) bir kardeşin yüzeyi tur içinde hareket ederse onları OTOMATİK 2. tura çeker. (c) Persist değişmez: yalnız Converged'de, tüm üyelere aynı bileşik imza; settled üye de persist edilir (ReadSurfaces'ı aynen). Kalıcı-bayat-binary deliği neden açılmaz — kanıt zinciri: bir derlemenin girdisi (kendi kaynağı, SCC-dışı upstream'in imzası, SCC-içi kardeş YÜZEYLERİ) üçlüsüdür; üçü de son başarılı derlemedekiyle birebir aynıysa çıktı geçerlidir; kardeş yüzeyi tur içinde değişirse üye bayat sayılıp derlenir; upstream'i CycleRunScope zaten aynı koşuda taze derler. (d) Yalnız Safe modda (bileşik harita Fast'te kurulmuyor — yakınsamama hafızasıyla aynı sınır). Riskler (dürüst): packages.config içerik parmak izinde değilse restore atlanabilir (açık soru); ReadSurfaces büyür (üye başına ~10 kayıt × 64 hex, önemsiz); D5-1 kapatılmadan bu tasarımın kanıtı olmaz (aynı hash altyapısı).

**Çözüm (doğrulayıcı düzeltmesi):** Sıralama: D5-6 (log) + D5-1 (neden görünür) önce; ardından birkaç gerçek Resolve koşusunda 'kaç üyenin BuiltContent'i değişmişti' sayılmalı (defterde alan zaten var, ölçüm yeni kod gerektirmez). Sayı kazancı gösterirse tasarım kullanıcıya §7.3 değişikliği olarak sunulur; karar saf Core fonksiyonunda kalmalı (planlama Core'da değişmezi).

**Değişmezler:** §7.3 bileşik imza ve 'converged dışında persist yok' korunur; planlama Core'da kalır (WillBuild/toBuild kararı saf bir Core fonksiyonuna — ör. CycleRoundScope.InitialToBuild(ledger, surfaces, content) — çıkarılır, Supervisor yalnız yürütür). Önceki analiz raporunun 'üye bazlı imzaya dönmek deliği açar' uyarısı: burada imza değil KANIT üye bazlıdır, imza bileşik kalır.

**Test fikri:** 3 üyeli sahte SCC'de yalnız B'nin içeriği değişsin: tur-1 çağrı listesi [B#1] (A ve C yok); B'nin yüzeyi değişirse tur-2 [A#1] (yalnız B'yi okuyan); persist sonrası üçünün de aynı bileşik imzayı taşıdığı; A'nın ReadSurfaces'ı bozulmuşsa (dış araç kopyayı ezmiş) A'nın tur-1'e girdiği pinlenir.

**Doğrulayıcı notu:** Yapı doğru: SCC tek bileşik imza taşır, grup kirliyse tur 1 her üyeyi derler; yüzey kanıtı yalnız tur 2'yi daraltır (1741). Koşuda 33 üyenin 33'ü derlendi (decision.log). Ancak kazanç rakamları ölçüm değil model. Çözüm ARCHITECTURE.md §7.3'teki yazılı davranışı değiştirir ('a component is either wholly dirty or wholly up to date — members never disagree'): imza bileşik kalsa da yürütme üye bazına iner. Doğruluk riski 'orta' değil yüksek: kanıt zinciri üç girdiye (kendi içerik, SCC-dışı upstream, kardeş yüzeyi) dayanıyor ama packages.config'in içerik parmak izinde olup olmadığı açık soru, ve D5-1'deki sessiz kanıt kaybı çözülmeden bu tasarımın dayanağı yok.

#### D5-4 — Bariyerli seviyeler yerine 'çatışma-farkında ready-set' KAZANDIRMIYOR: gerçek sürelerle 81,0 s → 80,6 s; alt sınır zaten 74 s (yapma)

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Core/Planning/CycleRoundLevels.cs (Compute, dosyanın ikinci yarısı); src/BuildOrchestrator.Supervisor/RunCoordinator.cs:1692-1698 (seviye döngüsü, WhenAll satır 1696); ARCHITECTURE.md §8.8 satır 1463 civarı
- `src/BuildOrchestrator.Core/Planning/CycleRoundLevels.cs` satır 68-115 — Compute: en-çok-okunan-önce yerleşim, komşu/ad-öneki çakışması aynı seviyeye konmaz
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1692-1697 — Seviye döngüsü: `Task.WhenAll(level.Select(CompileOneAsync))` — bariyer; sonraki seviye tamamen bitmeden başlamaz
- `ARCHITECTURE.md` satır 1391-1632 (§8.8 'barriered levels') — Komşu asla eşzamanlı değil (torn read) — kural buradan

**Kanıt:** csc /reference satırlarından türetilen 77 gerçek kenar + tur-2 MSBuild süreleri (General 9,3 · Service.Print 13,3 · DMS 2,8 · NewSales 6,6 · SparePart.Common 7,0 · Customer 2,9 · Service.Common 3,9 · Finance 5,5 · UsedCars 17,3 · NewSales.Stock 6,1 · General.Common 9,2 · WorkOrder 20,6 · BeyazSistem 3,7 · Pricing 6,9 · SparePart.Finance 25,6 · Acc.Common.UI 4,6 · Service.Report 6,1; +0,5 s invoke ek yükü) ile simülasyon: bariyerli plan 81,0 s (gözlenen ~79-81 s ile uyumlu, seviyeler birebir); komşu-olmayan hemen başlar kuralıyla ready-set 4 slotta 80,6 s, 6 slotta 80,6 s, 3 slotta 78,7 s (aynı). Neden: {WorkOrder, SparePart.Finance, SparePart.Common, Service.Common, General, Acc.Common.UI} karşılıklı komşu bir KLİK — hiçbir ikisi eşzamanlı olamaz ⇒ Σ = 71,0 s + ek yük ≈ 74 s alt sınır; bariyer bunun %10 üstünde. Kural olmasaydı (torn-read güvencesi kaldırılsa) alt sınır max(151,3/4, 26,1) ≈ 40 s.

**Etki:** Ready-set için tasarım turu + eşzamanlılık testi harcanır, ≤ 7 s (≤ %8) döner. Asıl kaldıraçlar D5-1/D5-3 (tur sayısı ve üye sayısı), derleme paralelliği değil.

**Etki (doğrulayıcı düzeltmesi):** Ready-set'in beklenen kazancı ≤ 7 s (simülasyon). 2-4 çekirdekli makinede paralellik zaten 2 (Light) olacağından fark daha da küçülür.

**Çözüm:** Yapma. Bunun yerine seviye planını decision.log'a yaz (D5-6) ve klik alt sınırını raporla — kullanıcı 'neden 4 slotun 2'si boş' sorusunun cevabını görsün. Şartlı (ÖNERMİYORUM): komşu kuralını 'yalnız yazan→okuyan yönünde ve kopya penceresinde' gevşetmek ~74 s → ~40 s verir ama §8.8'in torn-read garantisini bozar; OutDir/HintPath'e dokunmadan bir 'okuma anlık görüntüsü' kurulamaz (OutDir değişmezi).

**Değişmezler:** Değişiklik önerilmiyor; §8.8 komşu kuralı korunur.

**Test fikri:** —

**Doğrulayıcı notu:** Negatif sonuç ('yapma') kodla ve aritmetikle tutarlı: tur-2 üye süreleri toplamı 151,4 s (yeniden topladım), klik olarak verilen altı üyenin toplamı 71,0 s, gözlenen tur 79 s. Seviye planı gerçekten bariyerli (1692-1698). Simülasyonu ve 77 kenarı yeniden üretmedim; 80,6 s ve 74 s sayıları ölçüm değil simülasyon çıktısıdır. Öneri bir değişiklik istemediği için risk yok.

#### D5-5 — Yüzey hash'leme kritik yolda ve önbelleksiz: grup başında ~6 s preamble (34 dosya, ~111 MB), her üretici derlemesinden sonra seviye içinde 2 dosya daha

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Supervisor/RunCoordinator.cs:1527-1533 (grup başı sıralı hash), 1667-1672 (derleme sonrası hash — InvokeSlots bırakılmadan, finally'deki Release'ten önce); src/BuildOrchestrator.Core/Incremental/ApiSurfaceHash.cs:40-75 (OfFile/OfStream)
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1527-1533 — Grup başında TÜM üreticiler tek thread'de sırayla hash'lenir (kanıt + beslenen kopya), ilk invoke bundan sonra
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1667-1672 — Her başarılı üretici derlemesinden sonra SurfaceStateOf(id) yeniden hash'ler — slot bırakılmadan, seviye bariyerinin içinde
- `src/BuildOrchestrator.Core/Incremental/ApiSurfaceHash.cs` satır 56-80, 96-176 — Tam metadata gezisi + her üye için string render + sıralama + SHA256; önbellek yok; kopya ile kanıt aynı içerikken iki kez

**Kanıt:** UI grubu dış bağımlılıkları 01:04:48,29'da terminal (SCC5 converged satırı), ilk MSBuild 'Oluşturma başlatıldı' 01:04:54 ⇒ 5,3-6,7 s preamble; arada hash dışında ağır iş yok. Business.NewSales grubu (dispatch 01:04:55,47 → ilk MSBuild 01:04:58) 2-2,5 s, NewSales.Sales grubu 2,8 s, Business.Acc.Common 1,7 s — küçük DLL'lerde bile 1,7-2,8 s ⇒ sabit + boyuta bağlı maliyet. Dosya boyutları: SparePart.Finance 11,8 MB, WorkOrder 8,3, UsedCars 6,7, Service.Print 6,2 (×2 kopya). Tur-2 seviye geçişlerinde ek yük ~0,4-0,7 s (L4→L5: WorkOrder 20,58 s, seviye 21 s sonra). MSBuild 'Geçen Süre' ile DurationMs farkı 0,15-0,6 s (spawn+pump) — invoke ek yükü küçük; asıl ek yük hash.

**Etki:** Tahmin: UI grubunda preamble ~6 s + tur başına ~3-4 s (seviye içi yeniden hash) × 2 = ~12-14 s (~%7-8 / 166 s); D5-1/D5-3 sonrası oransal payı büyür (tek derlemelik koşuda 6 s preamble = sürenin üçte biri).

**Etki (doğrulayıcı düzeltmesi):** Ölçülen: kritik yoldaki preamble toplamı 0,38 + 0,62 + 0,78 + 5,93 = 7,7 s (184 s'nin %4,2'si); Business zincirinde 8,2 s (kritik yol dışı). Yavaş disk/CPU'da oransal olarak büyür.

**Çözüm:** (1) (path, length, mtimeTicks) anahtarlı yüzey-hash önbelleği — SourceHashCache kalıbı (Core, saf), Supervisor ömrü boyunca bellekte + %LOCALAPPDATA%'da küçük json; koşudan koşuya aynı DLL bir daha hash'lenmez. (2) Kanıt ile beslenen kopya aynı length+mtime ise (copy /y zaman damgasını korur) bir kez hash'le, ikisine yaz. (3) Grup başı hash'lemeyi Parallel.ForEach ile üreticiler üzerinde paralelleştir (saf okuma, kilit yok). (4) Ölçüm: decision.log'a 'cycle L: surfaces — F files, B MB, T ms' satırı (D5-6) — bu bulgu tahmini kesinleştirir.

**Çözüm (doğrulayıcı düzeltmesi):** (3) grup başı hash'i üreticiler üzerinde paralelleştirmek ve derleme sonrası hash'i slot bırakıldıktan sonra yapmak değişmezlere dokunmaz — önce bunlar. (1)/(2) (length+mtime anahtarlı önbellek, kanıt ile kopyayı mtime eşitliğiyle tek saymak) CLAUDE.md'deki 'çıktının tarihi tek başına güncel demeye asla yetmez' ilkesiyle gerilimlidir (boyut+mtime yalnız KAYNAK özet önbelleğinin anahtarı olarak tanımlı); kullanıcı kararı ister. Ek: OfStream tüm yüzeyi tek StringBuilder + ToString + UTF8 byte[] olarak üç kopya tutuyor; IncrementalHash ile akıtmak Supervisor belleğini düşürür (bkz. missing).

**Değişmezler:** Dokunmaz (yalnız okuma; önbellek dosyası araç state'idir, OutDir değil).

**Test fikri:** Aynı (length,mtime) ile ikinci OfFile çağrısının dosyayı açmadığını (sayaçlı sahte FileSystem) ve mtime değişince yeniden hash'lediğini pinle.

**Doğrulayıcı notu:** Preamble'ı daha kesin ölçtüm: proje logu OpenProjectLog ile hash döngüsünden SONRA açılıyor, dolayısıyla 'önceki grup converged' → 'üye loglarının CreationTime'ı' aralığı preamble'dır. 10-01 koşusu: UI 01:04:48.292 → 01:04:54.219 = 5,93 s; Business.Accounting.Common 48.292 → 50.336 = 2,04 s; Business.NewSales 55.465 → 58.267 = 2,80 s; NewSales.Sales 07.245 → 10.635 = 3,39 s; Types grupları 0,38 / 0,62 / 0,78 s. Bulgu ajanının 5,3-6,7 s ve 1,7-2,8 s aralıklarıyla uyumlu. Sınır: aralık dispatch + ComputeDepIssues'u da içerir ve Business grupları UI'nin hash/derlemesiyle eşzamanlı koştu (%70 cap altında) — sürenin tamamı hash'e yazılamaz. 'Tur başına 3-4 s' ve 'toplam 12-14 s' TAHMİN olarak kalıyor.

#### D5-6 — decision.log tur kararının NEDENİNİ yazmıyor: hangi üyeler bayat, hangi kenar/dosya hareket etti, seviye planı, tur duvar süresi, hash süresi — kullanıcı 'neden 2. tur' sorusuna cevap alamıyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Supervisor/RunCoordinator.cs:1585-1586 (CycleRoundStartedEvent yalnız olay), 1717-1741 (staleNow loglanmıyor), 1821-1840 (RecordCycleOutcome: tek 'converged (N members)' satırı)
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1584-1586 — CycleRoundStartedEvent yalnız App'e gider (Round, RoundCap, MemberCount); decision.log'a tur satırı yok
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1717-1741 — staleNow hesaplanır ama loglanmaz (hangi üye, hangi dep, hangi dosya)
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1820-1836 — RecordCycleOutcome: yalnız 'converged (N members)' — tur sayısı ve süre kırılımı yok (CycleCompletedEvent taşıyor ama diske yazılmıyor)

**Kanıt:** run-20261001-010430-305/decision.log'da UI grubu için tek satır: '01:07:34.446 cycle OSYS.UI.DMS: converged (17 members)'. Tur sayısı, 2. turun 17/17 olduğu ve nedeni yalnız proje loglarındaki invoke sayılarından çıkarılabildi (bu analizde script ile).

**Etki:** D5-1'in kök nedeni bugün TEŞHİS EDİLEMİYOR; her performans sorusu log kazısı gerektiriyor.

**Etki (doğrulayıcı düzeltmesi):** Doğrudan süre kazancı yok; teşhis süresini kısaltır.

**Çözüm:** Tur başına iki satır: 'cycle L: round r/K — building N/M [levels: 2|3|3|4|3|2]' ve tur sonunda 'cycle L: round r done in T s — failed {…}; stale {A←B:C:\…\B.dll, …}; decision=…'; grup başında 'cycle L: surface evidence on (F files hashed in T ms)' ya da D5-1'deki 'unavailable' satırı; sonuç satırına 'rounds=r, total=T s'. Hepsi Decide(run.Logs, …) ile, olay akışına yeni alan gerekmez. Sıfır risk.

**Değişmezler:** Yok (stderr/dosya log; stdout NDJSON'a dokunmaz).

**Test fikri:** Seçici turlu sahte SCC'de decision.log'da 'round 2/3 — building 1/3' ve 'stale {A←B}' satırlarının varlığını pinle.

**Doğrulayıcı notu:** Kod ve log birebir doğruluyor: 10-01 decision.log'unda UI grubu için tek satır var ('01:07:34.446 cycle OSYS.UI.DMS: converged (17 members)'); tur sayısını ve 17/17'yi ben de ancak proje loglarındaki derleme sayılarından çıkarabildim. Performans kazancı yok; D5-1 ve D5-3'ün ön koşulu olduğu için değerli. decision.log App'e IPC ile gitmiyorsa ek satırların maliyeti ihmal edilebilir (grup başına birkaç satır).

#### D5-7 — Kalıcı çözüm raporu (Ö6) hâlâ yok: 7 SCC'nin 4'ü tek kenarla, biri 1, biri 3 kenarla dağılır; UI 17'lisi 14 karşılıklı çift taşıyor — somut kesim listesi çıkarılabilir

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Core/Graph/TopoSort.cs:14 ve sonrası (Tarjan SCC); .claude/outputs/2026-09-26-17-42-cycle-resolve-analysis.md
- `src/BuildOrchestrator.Core/Graph/TopoSort.cs` satır (SCC üretimi) — SCC'ler zaten hesaplanıyor; iç kenar listesi ve kesim önerisi üretilmiyor
- `.claude/outputs/2026-09-26-17-42-cycle-resolve-analysis.md` satır Ö6 — Önceki analizde önerildi, yapılmadı

**Kanıt:** evaluation-cache.json'dan (503 gerçek proje, 1271 kenar) Tarjan ile: SCC0 Types.Accounting.Common↔Types.General (2 kenar); SCC1 Business.Accounting.Common↔Business.SparePart.Finance; SCC2 Business.Finance↔Business.NewSales; SCC3 Business.NewSales.Pricing↔Business.NewSales.Sales (her biri TEK kenar kesilince çözülür); SCC4 Types.Customer→General.Common→UsedCars→Customer (+Rent; 1 kesim); SCC5 Types.{Service.Common, Service.WorkOrder, SparePart.Common, SparePart.Finance} 9 kenar, 3 karşılıklı çift (SC↔WO, SPC↔SPF, SPC↔WO) ⇒ en az 3 kesim; SCC6 UI 17 üye 77 kenar, 14 karşılıklı çift (ör. General↔General.Common, General↔DMS, Service.Common↔Service.WorkOrder, SparePart.Common↔{General, General.Common, Service.Common, Service.Print, Service.WorkOrder, SparePart.Finance}, UsedCars↔BeyazSistem, Acc.Common.UI↔{Finance, SparePart.Finance}) ⇒ en az 14 kesim. Business zinciri (SCC1→2→3) bu koşuda 01:04:48→01:05:20 seri 32 s harcadı — bugün kritik yolda değil ama UI düzelince kritik yol olur.

**Etki:** Dört Business/Types ikilisi dağılırsa 8 üye Resolve'dan çıkar, sıradan artımlı Build'e döner (o koşuda ~50 s'lik seri zincir yok olur); UI grubunda SparePart.Common'ın 6 karşılıklı kenarı kesilirse klik dağılır ve seviye sayısı düşer.

**Etki (doğrulayıcı düzeltmesi):** Araç tarafında ölçülebilir kazanç yok. '8 üye Resolve'dan çıkar' OSYS kaynak değişikliğine bağlı tahmin.

**Çözüm:** Core'a saf `CycleReport` (SCC başına iç kenarlar; karşılıklı çiftler; okunan tip sayısına göre 'hangi yön kesilmeli' sezgisi: Types/Business katmanı için 'iki yönden az tip okuyanı interface/Types projesine indir'); Supervisor koşu sonunda `cycles.md`'yi log klasörüne yazsın ve konsola 'N cut candidates — see cycles.md' düşsün. Kesim adayı olarak her kenarın csproj'daki HintPath satırı verilir.

**Değişmezler:** Rapor yalnız okur; git/OutDir'e dokunmaz.

**Test fikri:** Sahte 2-üyeli SCC için raporda tek kenar çifti ve HintPath satırı; 4-üyeli sahte SCC'de karşılıklı çift sayısının doğru olduğunu pinle.

**Doğrulayıcı notu:** Kodda CycleReport/cycles.md yok (grep boş) — 'yapılmamış' doğru. Ancak bu bir performans kusuru değil, yeni bir özellik; kazanç yalnız OSYS ekibi csproj kenarlarını keserse oluşur, aracın kontrolünde değil. SCC kenar/karşılıklı çift sayılarını (77 kenar, 14 çift) yeniden türetmedim. Doğrulanabilen tek sayı: Business zinciri 01:04:48 → 01:05:20 = 32 s.

#### D5-8 — Resolve, kullanıcının önünde beklediği bir koşu; yine de Balanced'ın %70 hard cap + BelowNormal'ı altında koşuyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Supervisor/RunCoordinator.cs:848-861 (ApplyPerfLocked satır 860); ARCHITECTURE.md §11.1 satır 2152-2178
- `ARCHITECTURE.md` satır 2152-2178 (§11.1) — Balanced = 4 paralel, BelowNormal, %70 hard cap — mod bağımsız
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 851-862 — ApplyPerfLocked(profile) koşu türünden bağımsız uygulanır

**Kanıt:** decision.log: 'mode=Cycles parallelism=4 cpuCap=70%'. Etkin paralellik ~1,9 (151 s üye toplamı / 79 s duvar) — 4 slotun 2-3'ü klik yüzünden boş; yani cap çoğu zaman bağlamaz, ama 20-26 s'lik WorkOrder/SparePart.Finance derlemeleri (csc çok iş parçacıklı + wpftmp ikinci csc) tek başına 8 thread'i doldurabilir ve %70 cap orada bağlar (üst sınır 1/0,7 = 1,43×).

**Etki:** Tahmin: kritik yoldaki uzun derlemelerde %10-30 uzama; toplamda ~10-20 s.

**Etki (doğrulayıcı düzeltmesi):** Ölçülmedi. Karar için aynı Resolve'u Full profilde bir kez koşmak yeter.

**Çözüm:** Cycles koşusunda ve pencere ön plandayken cap'i kaldır (parallelism 4 kalsın — §11.1'in bellek gerekçesi paralellikle ilgili, cap'le değil); tray/arka planda Balanced'a dön (D4'ün adaptif profil önerisiyle tek mekanizma: setPerfMode zaten canlı uygulanıyor, §11.1).

**Çözüm (doğrulayıcı düzeltmesi):** Yapma; ölçüm gelmeden profil mantığına dokunulmamalı.

**Değişmezler:** Dokunmaz; §11.1 'Balanced varsayılan' kararı korunur, yalnız ön-plan+Cycles için geçici üst profil.

**Test fikri:** —

**Doğrulayıcı notu:** Konum doğru, profil koşu türünden bağımsız uygulanıyor. Fakat etki tamamen tahmin ('%10-30', güven 0,5) ve bulgunun kendi verisi cap'in çoğu zaman bağlamadığını söylüyor (etkin paralellik 151 s / 79 s = 1,9). Öneri §11.1'in 'One chip cycles three fixed profiles. This is the single source of truth' kuralına koşu-türüne bağlı gizli bir dördüncü davranış ekler. Zayıf makinede cap'i kaldırmak kullanıcının 'UI donmasın' önceliğiyle çelişir; kullanıcı isterse chip'ten Full'e zaten canlı geçebiliyor.

#### D5-9 — Restore prologu tur-1'de kritik yolda: 4 UI üyesi (General, Customer, UsedCars, Acc.Common.UI) ~2,9 s; packages.config değişmemişken atlanabilir

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Supervisor/RunCoordinator.cs:2050 (NeedsRestore), 2321-2325 (HasPackagesConfig: yalnız dosya var mı); src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs:33-37 (RestorePackagesConfig)
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2043-2051 — NeedsRestore = !suppressRestore && HasPackagesConfig — restore-once yalnız tur ≥2 için; tur-1 her packages.config'li üye restore alır
- `src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs` satır 33-36 — -t:restore -p:RestorePackagesConfig=true ayrı MSBuild child'ı

**Kanıt:** Loglar: 7 restore invoke (Types.General 0,43 s, Customer 0,44, UsedCars 0,45, UI.General 0,54, UI.Customer 0,83, UI.UsedCars 0,85, Acc.Common.UI 0,68). UI'daki 4'ü kendi seviyesinde seri (restore→build) koşar ⇒ ~2,9 s kritik yol.

**Etki:** ~3 s (%2); D5-1/3 sonrası oransal olarak büyür.

**Etki (doğrulayıcı düzeltmesi):** Resolve'da UI kritik yolunda ~2,9 s (dört restore: 0,54 + 0,83 + 0,85 + 0,68). Build yolunda kirli ve packages.config'li proje başına ~0,4-0,85 s'lik ek MSBuild süreci (süreç başlatma + değerlendirme); toplamı ölçülmedi.

**Çözüm:** packages.config'in içerik hash'ini defterde tut (ProjectInputs'a zaten giriyorsa ContentById'den okunur); son BAŞARILI restore'daki hash ile aynıysa ve packages klasörü mevcutsa NeedsRestore=false. Restore hatası olasılığı için: derleme MSB/NU restore hatasıyla düşerse bir kez restore'lu tekrar (tek yerde, InvokeOnceAsync).

**Çözüm (doğrulayıcı düzeltmesi):** Öneri geçerli ama iki şart: packages.config'in içerik parmak izine girip girmediği önce kodda doğrulanmalı; 'packages klasörü mevcut' kontrolü paket başına değil klasör düzeyinde kalırsa eksik paketi kaçırır — başarısızlıkta bir kez restore'lu tekrar şart. Ayrıca restore çıktısı her seferinde NuGet zafiyet dizinine bakıyor (logda 'CACHE https://api.nuget.org/v3/vulnerabilities/index.json'); bkz. missing.

**Değişmezler:** Dokunmaz (§9.3 restore sözleşmesi korunur, yalnız koşul daralır).

**Test fikri:** packages.config hash'i eşitken komut satırında restore child'ının olmadığını, hash değişince olduğunu pinle.

**Doğrulayıcı notu:** Kod doğru: packages.config varsa her ilk invoke ayrı bir restore MSBuild child'ı alır; restore-once yalnız tur ≥ 2 içindir (suppressRestore). Örnek ölçüm: UI.General logu satır 3-30, restore 'Geçen Süre 00:00:00.54', hemen ardından build. Kapsam bulgudakinden GENİŞ: aynı satır sıradan Build/Rebuild yolunda da çalışır (InvokeOnceAsync ortak) — yani birincil senaryoyu da etkiler. OSYS'te (derinlik ≤ 4) 19 packages.config var.

#### D5-10 — Döngü üyelerinin MSBuild çıktısı aşırı gürültülü: MSB3277 sürüm çatışmaları ve uyarılar binlerce satır — RAR ek işi + satır pompası/observeLine/senkron flush maliyeti

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Supervisor/RunCoordinator.cs:1625-1629 (ObserveCompilerLine), 2064-2068 (her satır Emit + observeLine); src/BuildOrchestrator.Core/MsBuild/CompilerReferences.cs:16-21; src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs:25-27 ('-clp:Summary')
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1622-1627 — ObserveCompilerLine her satırda CompilerReferences.Parse (regex IsMatch) — ucuz ama satır sayısıyla çarpılır
- `src/BuildOrchestrator.Core/MsBuild/CompilerReferences.cs` satır 15-21 — CompilerPattern satır başına regex

**Kanıt:** Koşu klasörü 27 MB / 33 proje. UI.Service.Report 6957 satır (4996 MSB3277), UI.UsedCars 8824 satır (7288 MSB3277 + 96 'denendi' arama satırı: MSB3245 Microsoft.QualityTools.Testing.Fakes çözülemiyor), UI.SparePart.Finance 5683 satır (5112 CS uyarısı), Types.UsedCars 11969 satır (11894 CS uyarısı). MSB3277 = RAR'ın aynı assembly'nin farklı sürümlerini uzlaştırması; MSB3245 = her arama yolunda dosya aranması.

**Etki:** Tahmin: proje başına yüzlerce ms (RAR arama + 10k satır pompalama; RunLogWriter satır başı senkron flush D-log boyutunda ayrı bulgu). Asıl fayda OSYS tarafında: çatışan referans sürümleri ve olmayan Fakes referansı temizlenirse.

**Etki (doğrulayıcı düzeltmesi):** Ölçülen hacim: 79.538 satır / 27,4 MB / 184 s (432 satır/s). App CPU'suna etkisi bu koşuda ölçülmedi (Rebuild ölçümünden çıkarım).

**Çözüm:** D5-7 raporuna 'gürültü kaynakları' bölümü: proje başına MSB3277/MSB3245 sayısı ve çatışan assembly adı; araç tarafında değişiklik gerekmez.

**Çözüm (doğrulayıcı düzeltmesi):** 'Araç tarafında değişiklik gerekmez' yanlış. İki araç tarafı kaldıraç var: (a) '-clp:Summary' her uyarıyı koşu sonunda bir kez daha yazdırır ve ikinci tur hepsini tekrarlar — aynı uyarı 4 kez akıyor; (b) ara turların satırları App'e akmak zorunda değil (ara tur sonucu zaten yayılmıyor). İkisi de log/IPC boyutunun (D-IPC) bulgusuyla tek çözümde ele alınmalı; proje logunun içeriği değişeceği için kullanıcı kararı ister.

**Değişmezler:** —

**Test fikri:** —

**Doğrulayıcı notu:** Satır sayıları birebir doğru: Service.Report 6957 satır / 4996 MSB3277; UsedCars 8824 / 7288; SparePart.Finance 5683 / 5112 CS uyarısı; Types.UsedCars 11969 / 11894 CS uyarısı. Bulgu etkiyi küçümsüyor: koşunun tamamı 79.538 satır / 27,4 MB, 184 s'de = 432 satır/s — ölçülen tam Rebuild'in (23.047 satır / 7,7 MB, ~160 satır/s) satırda 3,4 katı, hızda 2,7 katı; üstelik yalnız 33 projeyle. Her satır Emit'ten geçiyor. Rebuild'de App'in tek çekirdeğin %60-100'ünü yaktığı ölçüldüğüne göre Resolve, zayıf makinede App tarafındaki en yoğun log yükü. Süreye etkisi ölçülmedi.

#### D5-11 — WPF üyelerinde her invoke iki csc koşusu (_wpftmp geçici assembly + CoreCompile) — araç tarafından kaçınılamaz, ama üye başına derleme maliyetini ikiye katladığı için D5-3'ün (daha az üye) değerini artırır

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) ARCHITECTURE.md §9.2 satır 1644 civarı (UseSharedCompilation=false); src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs:26
- `ARCHITECTURE.md` satır 1640-1660 (§9.2) — UseSharedCompilation=false bilinçli (2,9×); her csc ayrı process; spike: pipe adı override edilemiyor

**Kanıt:** 17 UI logunun 16'sında 'wpftmp' satırları ve 12 csc satırı (2 invoke × 2 csc × komut+CompilerServer satırı); MarkupCompilePass1 XAML yerel tip referansı gördüğünde geçici assembly derler — proje özelliği, araç argümanı değil.

**Etki:** Üye başına 2 csc × 2,9× sunucusuz ceza; Round-2 önlemek/üye azaltmak bunu doğrudan azaltır.

**Etki (doğrulayıcı düzeltmesi):** Yok (bilgi). wpftmp geçen log sayısı 15.

**Çözüm:** Araçta değişiklik yok. §9.2 spike sonucu (pipe adı) geçerli — yeniden açılmaz. OSYS tarafında XAML'in yerel tiplere x:Class dışı referansı azaltılırsa wpftmp geçişi düşer (rapor notu).

**Değişmezler:** §9.2 bilinçli karar korunur.

**Test fikri:** —

**Doğrulayıcı notu:** Bağlam notu, eylem yok. 33 logun 15'inde 'wpftmp' geçiyor (bulgu '17 UI logunun 16'sı' diyor; koşu genelinde saydığım 15). '2,9× sunucusuz ceza' §9.2'den alıntı, bu oturumda ölçülmedi. Önerilen değişiklik olmadığı için çatışma yok.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs:25-27 — '-clp:Summary' her uyarıyı ikinci kez yazdırıyor; Resolve'da iki turla aynı uyarı 4 kez akıyor (Types.UsedCars logunun 11.969 satırının 11.894'ü CS uyarısı) ve koşu 79.538 satır / 27,4 MB üretiyor — tam Rebuild'in 3,4 katı satır.
- src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2398-2404 ile ARCHITECTURE.md:1292 uyuşmuyor — doküman tahminin BuildState.LastDurationMs'ten geldiğini söylüyor, kod koşu içi ortalamayı kullanıyor; Resolve'da ilk grup bitene kadar ETA yok, sonra küçük Types üyelerinin ortalamasıyla (2,2 s) UI grubunu ~75 s gösteriyor, gerçek 134 s.
- src/BuildOrchestrator.Core/Incremental/ApiSurfaceHash.cs:56-70 — OfStream tüm yüzey metnini StringBuilder + ToString + Encoding.UTF8.GetBytes ile üç kopya hâlinde bellekte kuruyor (11,8 MB'lık DLL'ler için); IncrementalHash ile parça parça beslemek Supervisor'ın tepe belleğini düşürür (büyüklüğü ölçülmedi).
- src/BuildOrchestrator.Supervisor/RunCoordinator.cs:1667-1672 — derleme sonrası yüzey hash'i MSBuild slotu (InvokeSlots) hâlâ tutulurken yapılıyor (Release finally'de, 1688 civarı); eşzamanlı koşan başka grubun üyesi bu sürede slot bekliyor (Business.NewSales.Sales grubunda 7,85 s derlemeye karşı 13,1 s duvar süresi).
- src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs:33-37 — restore child'ı her çağrıda NuGet zafiyet denetimini çalıştırıyor (logda 'CACHE https://api.nuget.org/v3/vulnerabilities/index.json' + NU1903 uyarıları); önbellek bayatladığında ağ çağrısı kritik yola girer. Restore argümanlarında denetimi kapatmak (NuGetAudit=false) değerlendirilebilir — etkisi ölçülmedi.
- src/BuildOrchestrator.Supervisor/RunCoordinator.cs:1501-1512 — SurfaceStateOf grup başında kanıt yolunun yanında TÜM FedCandidates kopyalarını hash'liyor; derlemeden sonra kayıt yalnız gerçekten okunan dosyaya daraltılıyor (CycleReadFiles.Tracked). Okunmayan kopyaların ilk hash'i preamble'ı büyütüyor ve D5-1'deki 'tek okunamayan dosya tüm grubu düşürür' yüzeyini genişletiyor.

**Temiz bulunan alanlar:**
- Grup dispatch zamanlaması DOĞRU: UI SCC6 yalnız Types SCC0/4/5'e bağlı (evaluation-cache'ten türetilen SCC DAG'ı), SCC5 01:04:48,29'da converged olur olmaz dispatch edildi; Business gruplarını beklemedi. Business SCC1→SCC2→SCC3 ve Types SCC0→SCC4→SCC5 zincirleri gerçek bağımlılık — seri koşmaları scheduler hatası değil (ReadySetScheduler.IsReadyLocked 249-260, TryDispatch grup dalı).
- CycleRoundLevels yerleşimi iyi: gerçek 77 kenarla yeniden üretilen plan gözlenen 6 seviyeyle birebir; 81 s, komşu-kuralı altındaki teorik alt sınırın (74 s klik) %10 üstünde. Ad-öneki çakışma kuralı (UI.General→General.Common, NewSales→Stock/Pricing, UsedCars→BeyazSistem) doğru tetikleniyor.
- Restore-once çalışıyor: yalnız tur-1'de ve yalnız packages.config'li üyelerde (7 restore, hepsi ilk turda); tur-2'de hiç restore yok.
- Seçici tur mekanizması küçük gruplarda ÇALIŞIYOR: Business.NewSales/Finance, NewSales.Sales/Pricing, Business.Acc.Common/SparePart.Finance ve Types.General.Common grubu TEK turda converged (yüzey kanıtıyla), Types.Service.Common grubunda 4 üyeden yalnız 1'i 2. tura girdi. Sorun mekanizma değil, UI grubunda kanıtın sessizce kapanması (D5-1).
- ApiSurfaceHash tasarımı sağlam: IL/MVID/timestamp/derleyici-üretimi adlar dışarıda, internal içeride, sürüm yalnız strong-name'de; OSYS UI projelerinin 17'sinde SignAssembly yok ⇒ wildcard-sürüm tuzağı yok.
- Slot yönetimi: SCC alan worker slotunu geri veriyor, seviye üyeleri ortak semafordan alıyor (InvokeSlots); duvar/ilan tutarlılığı (ProjectStarted slot alındıktan sonra, CycleMemberHeld bırakılmadan önce) korunuyor.
- Yakınsamama hafızası yalnız raporluyor, komutu yutmuyor (§8.8 kararı kodda: 760-780); Converged/CapReached'te siliniyor.
- CycleReadFiles: Clean sonrası 'yok→var' geçişinin okunmayan dosyada olduğu test pinli (after_a_clean_… converges_in_one_round); bu koşuda okuyucuların hepsi C:\OSYS\Client\Bin kopyasından okudu (csc /reference satırları) ve FedOutputs defterde doğru öğrenilmiş.
- Invoke başı süreç ek yükü küçük: DurationMs − MSBuild 'Geçen Süre' = 0,15-0,6 s (spawn+pump); '0,5 s/invoke' şüphesi büyük ölçüde MSBuild'in kendi değerlendirme süresi (0,4-1 s) + yüzey hash'idir, süreç başlatma değil.
- Yarıda kesilen tur karara sokulmuyor, kesilen grup herkesi Failed raporluyor (I1 kapısı) — stop/persist doğruluğu tur döngüsünde korunuyor.

**Açık sorular:**
- 01:04:48-54 arasında UI DLL'lerini (bin\Debug ya da C:\OSYS\Client\Bin) açık tutabilecek bir şey var mıydı — Visual Studio'da OSYS solution'ı açık mı, OSYS istemci uygulaması çalışıyor mu, antivirüs? (D5-1'in kök nedeni: sessiz düşüş loglanana kadar ayırt edilemez; D5-6/D5-1 log satırı eklenince bir sonraki koşu cevabı verir.)
- Son converged Cycles koşusundan 01:04'e kadar ne değişti ki 7 SCC'nin 7'si kirliydi — ortak bir Types upstream'i mi (bileşik imza herkesi kirletir) yoksa branch/Clean mi? (D5-3'ün kazancını kalibre etmek için: tek üye mi, ortak upstream mi tipik senaryo?)
- packages.config içerik parmak izine (ProjectInputs) giriyor mu? (D5-3'te settled üyenin restore'u atlanabilir mi; D5-9'un koşulu.)
- Resolve ön plandayken cap'in kaldırılması kabul edilebilir mi (bellek gerekçesi paralellikte kalır)? — D4 ile ortak karar.
- Kesim listesi (D5-7) OSYS ekibine gidecek bir çıktı mı, yoksa yalnız decision.log/konsol notu mu? Rapor biçimi (cycles.md vs konsol) buna göre.
- D5-3 yalnız Safe modda çalışır (Fast'te bileşik harita kurulmuyor); Fast'te bugünkü tam-grup davranışının kalması kabul mü?
- ETA cycle terimi için (D5-2) tercih: defterde tur-başına süre mi, ETA'da çarpanı kaldırmak mı? Seviye genişliğine bölme de istenir mi?


### D6-ui-akisi

#### D6-1 — Koşu metni App belleğinde 3-4 kopya tutuluyor ve bir sonraki işleme kadar hiç düşürülmüyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2694-2716 — OnProjectLog: her satır _liveLines[projectId] listesine ProjectLogEvent olarak eklenir VE _runText StringBuilder'a kopyalanır
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 440-443 — _liveLines tanımı — biten projelerin olayları da kalır, yalnız ClearConsoleForNewOperation (2738-2750) temizler
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 286-307 — AppendBatch: render dilimini aşan satırlar document.GetText ile kopyalanıp _backlogLines'a AddRange edilir (üçüncü kopya)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2821-2830 — SeedRunDocument: _runText.ToString() _gate kilidi altında (tam kopya) → ResetRunDocument
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 633-642 — ResetRunDocument: SplitLines(fullRunText) tüm koşuyu satır listesine böler (dördüncü kopya), belgeye yalnız son 200 satır girer
- `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` satır 302 — ProjectLogEvent(RunId, ProjectId, LineNumber, Text): her satır kendi RunId ve ProjectId string örneğini taşır (intern yok)

**Kanıt:** Gerçek koşu run-20261001-010430-305: 33 proje logu toplam 27 MB / 79.734 satır. Kod bu metni (a) _runText'te (UTF-16 ≈ 54 MB), (b) _liveLines'ta 79.7k ProjectLogEvent nesnesi olarak (Text + her olayda ayrı RunId ~36 ve ProjectId ~90 karakterlik string ≈ +20 MB), (c) anlatı modunda konsol _backlogLines'ında (≈54 MB) tutar; Back'e basınca (d) ToString snapshot'ı (54 MB, LOH) ve (e) SplitLines listesi de eklenir. Retention: koşu bitince hiçbir kopya düşmez; yalnız yeni Build/Sync temizler.

**Etki:** Bir Resolve/Build sonrası App'te ~130-180 MB metin kalıcı olarak bekler (27 MB'lık koşu için; 109 projelik Build daha büyük olabilir), Back anında +100 MB geçici LOH tahsisi ve Gen2 GC. Kullanıcının 'boşta en düşük RAM' beklentisiyle çelişir. Rakamlar log boyutundan türetilmiş hesaptır; RSS ölçümü yapılmadı (tahmin).

**Etki (doğrulayıcı düzeltmesi):** Ölçülen Rebuild (23.047 satır / 7,7 MB IPC, zarf dahil): metin gövdesi kabaca 3,8 MB (7,7 MB - 23.047 x ~170 B zarf; türetilmiş) -> kopya başına ~7,6 MB UTF-16; _runText + _liveLines + konsol backlog = ~23 MB + olay başına ayrı RunId/ProjectId string'leri ~7 MB = ~30 MB (hesap, tahmin). Ölçülen App Private büyümesi 194 -> 381 MB (+187 MB); yani bu bulgu ölçülen büyümenin yaklaşık altıda birini açıklar, tamamını DEĞİL. 27 MB'lık Resolve koşusunda aynı hesap ~130-180 MB verir (ölçülmedi). Kalan büyümenin kaynağı bu boyutta kanıtlanmadı.

**Çözüm:** (1) OnProjectDone/OnProjectSkipped'da _liveLines[projectId]'yi bırak: proje bitince dosya tamdır, getProjectLog chunk'ı ThroughLineNumber'a kadar her şeyi getirir, dikişte yalnız SONRADAN gelen canlı satır gerekir ve biten projeden satır gelmez (OnProjectLogChunk 2994-2998 aynen çalışır). (2) _liveLines'ta olayın kendisini değil yalnız (LineNumber, Text) tut ya da RunId/ProjectId'yi EngineHost.ReadLoopAsync'te intern et (string.Intern ya da küçük bir cache). (3) Koşu metnini TEK yerde tut: VM'de List<string> satır tamponu; ConsoleView.ShowRunDocument/ResetRunDocument string yerine IReadOnlyList<string> alsın (Join yalnız son 200 satır için), AppendBatch kırpılan satırları backlog'a kopyalamasın (backlog zaten VM listesidir) — ToString/SplitLines gidiş-dönüşü kalkar. Mimari korunur: konsol yine 200 satır pencere + geçmişe kaydırma (§13.5), IPC değişmez.

**Çözüm (doğrulayıcı düzeltmesi):** Fix (1) olduğu gibi YARIŞ içerir: getProjectLog chunk'ı ThroughLineNumber=N ile üretildikten sonra N+1.. satırları ve projectDone olayı chunk'tan ÖNCE gelebilir; OnProjectDone _liveLines'ı bırakırsa OnProjectLogChunk (2996-2998) dikişte o satırları kaybeder. Bırakma yalnız '_pendingLoad o projeye ait değilse' yapılmalı ya da bekleyen yükleme bitene dek ertelenmeli. Fix (2) (yalnız LineNumber+Text tut) risksiz ve ilk adım olmalı. Fix (3) (tek kaynaklı List<string>, ConsoleView arayüz değişimi) reseed-generation guard'ına dokunan büyük refactor: risk 'orta' değil 'orta-yüksek', ayrı iş olarak ve kırmızı testle.

**Değişmezler:** Değişmezlere dokunmaz: stdout NDJSON aynı, planlama Core'da, OutDir/git yok. §13.5 'backlog bir pencere, limit değil' kuralı korunur (geçmiş yine erişilebilir, yalnız tek kopya). ConsoleBatcher reseed-generation sözleşmesi ve _gate atomikliği aynen kalır.

**Test fikri:** RunViewModelStateTests: 1000 satırlık sahte projectLog akışı + projectSucceeded → _liveLines[projectId] boş (internal seam). ConsoleView testi: ShowRunDocument(lines) sonrası backlog referansı VM listesiyle aynı (ReferenceEquals) ve AppendBatch trim sonrası liste boyutu artmaz.

**Doğrulayıcı notu:** Kod iddiayı doğruluyor: RunViewModel.cs 2694-2716 OnProjectLog her satırı hem _liveLines'a (ProjectLogEvent nesnesi, 440) hem _runText'e (438) yazıyor; ikisini yalnız ClearConsoleForNewOperation (2740-2747) temizliyor. ConsoleView.xaml.cs 300-306 kırpılan canlı satırları _backlogLines'a ekliyor (GetText 396 + SplitLines). SeedRunDocument 2820-2829 _gate altında ToString, ResetRunDocument 638 SplitLines. Etki sayısı ise abartılı genelleme: 130-180 MB yalnız 27 MB'lık Resolve koşusu (klasör boyutu du ile doğrulandı: 27M) için hesap; RSS ölçümü yok.

#### D6-2 — Koşu başındaki pre-skip patlaması (154/150 projectSkipped) UI thread'inde O(n²) iş üretiyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 967-972 — PreSkipped + upToDateSkips döngüleri: skip olayları sıkı döngüde art arda yazılır (Cycles'ta 154 kapsam dışı, Build'de 150 'up to date')
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 339-343 — Her olay ayrı Dispatcher.InvokeAsync (Normal) — birleştirme yok
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2323-2324 — FindRow = Projects.FirstOrDefault (doğrusal, OrdinalIgnoreCase tam yol karşılaştırması) — her olayda
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2165-2210 — OnProjectSkipped → EnsureRow(FindRow) + UpdateEta + RefreshRunSurface
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2360-2400 — UpdateEta: Projects üzerinde 4 ayrı LINQ geçişi + ToList tahsisleri
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1798-1820 — RefreshRunSurface: RunCounters.From O(n) + RecomputeWillBuildSurface O(n) + VisibleProjects bildirimi
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 250-257 — VisibleProjects bildirimi → RefreshVisibleRows (VisibleRowSignature: getter Where/ToList + 187 id string.Join ≈10 KB) + RefreshListInvite (getter tekrar) + RefreshGraphFilter
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 832-855 — Counters bildirimi → PushGraphStatuses: RowsById dict (187) + GraphBinder.Nodes (TopologicalDepths DFS + 187 record) her olayda
- `src/BuildOrchestrator.App/ViewModels/GraphBinder.cs` satır 29-47, 74-98 — Nodes her çağrıda TopologicalDepths'i (byId dict + depth dict + DFS) yeniden hesaplar; topoloji değişmeden sonuç aynıdır
- `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs` satır 217-231, 425-432 — Counters VE VisibleProjects dallarının ikisi de RebuildChipsIfChanged çağırır: olay başına 2×(2 O(n) Where.ToList + 2 string.Join)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` satır 359-361 — ResolveName = Projects.FirstOrDefault (doğrusal) — her stream satırında

**Kanıt:** Tek olayın maliyeti ≈ 15 O(n) geçiş + ~10 adet n boyutlu koleksiyon/string tahsisi (yukarıdaki konumlar toplandı). 154 olay × 187 satır ≈ 29k satır ziyareti × 15 ≈ 430k ziyaret + ~1.500 tahsis, hepsi UI thread'inde ardışık dispatcher turlarında. UiResponsivenessBudgetTests doc'u CI'da TEK proje olayını 71 ms ölçmüş (bütçe 50) — yüklü makinede olay başına onlarca ms gerçekçi.

**Etki:** Resolve/Build başında UI thread 154 ardışık turda meşgul: lokalde olay başına 1-5 ms varsayımıyla 0,15-0,8 s giriş gecikmesi ve animasyon takılması (tahmin, ölçülmedi). Repo büyüdükçe kare-kare (n²) büyür.

**Etki (doğrulayıcı düzeltmesi):** Süre ÖLÇÜLMEDİ (0,15-0,8 s tahmin). Cycles başındaki 154 skip için maliyet olay başına FindRow + UpdateEta (yaklaşık 5 O(n) geçiş), 15 değil. Asıl ağır senaryo birincil kullanım: tepsiden Build, 187 projenin çoğu güncel -> ~150-180 ardışık projectSkipped(UpToDate), her biri tam zincir ve hepsi pencere gizliyken de çalışır (hiçbir yol pencere görünürlüğüne bakmıyor). Ölçülen Rebuild koşusunda (152 proje derlendi) skip patlaması yoktur; orada aynı zincir 152 x (Started+Succeeded) olayına yayılır.

**Çözüm:** (1) RunViewModel'e Dictionary<string, ProjectRowViewModel> _rowById ekle; EnsureRow/Projects.Add ve topoloji yeniden kurulumu bu haritayı günceller; FindRow ve Stream.ResolveName O(1) olur (MainWindow.RowsById de aynı haritayı alır, yeniden kurmaz). (2) Olay birleştirme: MainWindow.EventReceived non-log olayları ConcurrentQueue'ya atsın ve yalnız kuyruk boşsa bir InvokeAsync planlasın; UI turunda kuyruk drenajlanıp RunViewModel.OnEvents(batch) çağrılsın; VM batch boyunca RefreshRunSurface/UpdateEta'yı 'kirli' bayrağıyla erteleyip batch sonunda BİR kez koşsun (protokol ve olay sırası değişmez). (3) GraphBinder.TopologicalDepths sonucunu topoloji başına önbellekle (WorkspaceTopologyEvent'te yenile). (4) VisibleRowSignature'ı yalnız filtre/sorgu aktifken hesapla; filtre yokken görünür küme = Projects sırası, yalnız satır eklenince değişir. (5) StickyRibbon: RebuildChipsIfChanged yalnız VisibleProjects dalında (kendi yorumu onun üst küme olduğunu söylüyor).

**Çözüm (doğrulayıcı düzeltmesi):** Öneriler geçerli; sıralama: (a) _rowById haritası (FindRow/ResolveName/RowsById) ve TopologicalDepths önbelleği - risksiz; (b) StickyRibbon'da Counters dalından RebuildChipsIfChanged'i kaldırmak - kendi yorumu VisibleProjects'in üst küme olduğunu söylüyor; (c) olay birleştirme (batch) en riskli parça: OnEvent sırasına ve stream/ETA testlerine dokunur, ölçümle gerekçelendirilmeden yapılmamalı. Önce D6-7'deki patlama testi yazılıp tek olay süresi lokalde ölçülmeli.

**Değişmezler:** Olay sırası ve tek yazıcı kuralları korunur (batch aynı thread'de, aynı sırada uygulanır). Planlama Core'da kalır; Supervisor'a dokunulmaz. 'Koleksiyon reset yok' (A13.2) ihlal edilmez.

**Test fikri:** UiResponsivenessBudgetTests'e 'Pre-skip burst': 177 ProjectSkippedEvent ard arda → toplam duvar süresi bütçe altında VE FindRow çağrı başına O(1) (bir sayaç seam'i ile Projects enumerasyonu ≤ sabit). GraphBinder testi: aynı topolojiyle iki Nodes çağrısı TopologicalDepths'i bir kez hesaplar.

**Doğrulayıcı notu:** Kısmen yanlış: verilen '154 kapsam dışı skip' örneği ağır yoldan GEÇMEZ. OnProjectSkipped (RunViewModel.cs 2165-2187) Cycles + OutOfCycleScope dalında yalnız EnsureRow + UpdateEta çalıştırıp return eder; RefreshRunSurface çağrılmaz, dolayısıyla Counters/VisibleProjects bildirimi, PushGraphStatuses, RebuildChipsIfChanged, VisibleRowSignature tetiklenmez. Tam zincir (olay başına ~15 O(n) geçiş) yalnız Build'in 'up to date' skip'lerinde (2189-2197) ve Started/Succeeded olaylarında geçerli. Geri kalan konumlar doğru: MainWindow 339-343 olay başına ayrı InvokeAsync, FindRow 2323-2324 doğrusal, UpdateEta 2353-2418 çoklu LINQ + ToList, RefreshRunSurface 1798-1803, MainWindow 250-257 ve 879-881, GraphBinder.Nodes her çağrıda TopologicalDepths (36), StickyRibbon 219-231 çift RebuildChipsIfChanged, Stream.ResolveName 359-361.

#### D6-3 — 200 ms tick her turda tam graf beslemesi kuruyor ve ETA'yı 4 geçişle yeniden hesaplıyor — değişiklik olmasa da

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 353-363 — _elapsedTimer.Tick: TickElapsed + SetLineCount + IsRunUnderway ise PushGraphStatuses + FollowFrontier — koşulsuz her 200 ms
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 832-840, 848-853 — PushGraphStatuses her tick RowsById (187 girişli dict) + GraphBinder.Nodes (DFS + 187 record) kurar; ApplyStatuses diff'i ucuz ama besleme O(n) tahsisli
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 879-890 — Counters/CurrentOperation değişiminde de PushGraphStatuses — tick ile çift itiş
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1850-1866 — TickElapsed: foreach Projects (DurationMs yazımı) + UpdateEta her tick
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2369-2400 — UpdateEta: Count + Where.ToList + Count + Where.Select.ToList + Enumerable.Repeat.ToList ×2 + Select.ToList her tick
- `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` satır ApplyDuration gövdesi (grep 'private void ApplyDuration') — DurationMs her tick değişince ApplyDuration: PART_Duration.SetResourceReference her tick (kaynak ağacı yürüyüşü) — metin saniye çözünürlüklü olduğu için 5 tick'in 4'ünde aynı değer

**Kanıt:** Tick gövdesi koşarken her 200 ms'de: 1 dict(187) + 1 List<GraphNode>(187) + 2 dict + 1 set (TopologicalDepths) + UpdateEta'da 6 liste tahsisi + building satır sayısı kadar SetResourceReference. ARCHITECTURE §14.5 kendi kuralını 'anything called from the 200 ms tick writes only when the value actually changed' diye yazıyor; graf beslemesi ve ETA bu kurala uymuyor (ApplyStatuses'ın diff'i yalnız SON adımı koruyor).

**Etki:** Koşu boyunca saniyede 5 kez ~10 tahsis + ~1.000 nesne inşası; CPU payı küçük (tahmin: tick başına <1 ms lokalde) ama sürekli Gen0 çöpü ve arka planda/tray'de de dönüyor (IsRunUnderway kapısı pencere görünürlüğüne bakmaz). Test 'The_mid_run_graph_status_tick_stays_negligible' yalnız UpdateStatuses'ı ölçer, besleme kurulumunu ve TickElapsed'i ölçmez.

**Etki (doğrulayıcı düzeltmesi):** Tick başına süre ölçülmedi ('<1 ms' tahmin). Ölçüm yalnız toplamı veriyor: koşu sırasında tepsideyken App 1,44 Gcycles/s (tek çekirdeğin ~%40'ı); bu tick'in payı ayrıştırılmadı. Kesin olan: saniyede 5 kez ~10 koleksiyon tahsisi + 187 GraphNode record'u, pencere gizliyken de.

**Çözüm:** (1) RunViewModel.RefreshRunSurface bir 'RowsChangedSinceLastPush' bayrağı kursun; tick PushGraphStatuses'ı yalnız bayrak açıksa çağırsın ve bayrağı kapatsın (Counters yolu zaten anında itiyor; tick yalnız kaçanı yakalar). (2) TopologicalDepths topoloji başına önbellek (D6-2/3 ile aynı). (3) UpdateEta'yı tick'te yalnız ElapsedMs'in saniye kısmı değiştiğinde ya da building satır varken koş; LINQ ToList'leri tek foreach'e indir. (4) ProjectRow.ApplyDuration: SetResourceReference'ı State dalına taşı (renk yalnız State'e bağlı), DurationMs dalında yalnız metin yaz. (5) Tick'i pencere gizliyken (tray) yalnız TickElapsed/watchdog'a indir — graf zaten gizliyken beklet mekanizmasına sahip, liste FollowFrontier gizliyken anlamsız.

**Çözüm (doğrulayıcı düzeltmesi):** Fix (1)'deki 'RefreshRunSurface kirli bayrağı' eksik: satırın görsel durumu RefreshRunSurface'e uğramadan da değişir (OnBuildPreview'in plan bayrakları, döngüde IsCompiling kimlik değişimi, CurrentOperation) ve tick bugün bunların yakalayıcısıdır; bayrak yalnız orada kurulursa graf bayat kalabilir. Güvenli sıra: (a) TopologicalDepths'i topoloji başına önbellekle, RowsById yerine VM haritası - davranış değişmez; (b) pencere gizliyken (IsVisible false) tick'te PushGraphStatuses + FollowFrontier atlanır, pencere görününce bir kez itilir (MainWindow 1244'teki IsVisibleChanged kablosu hazır); (c) ApplyDuration'da SetResourceReference'ı State dalına taşı; (d) UpdateEta LINQ'lerini tek foreach'e indir. TickElapsed/EvaluateEngineSilence gizliyken de çalışmalı (watchdog + tepsi göstergesi ElapsedMs okuyor olabilir).

**Değişmezler:** Graf/liste senkron kuralı (§14.5 'the wave pushes the graph at its own pace') korunur: dalga kendi itişini yapıyor (ApplyMarkingToGraph), tick yalnız düzeltme kanalı. Motion sözleşmesi değişmez.

**Test fikri:** MainWindow testi: satır durumu değişmeden 10 tick → GraphView.UpdateStatuses çağrı sayısı 0 (seam); değişince 1. ProjectRow testi: 5 ardışık DurationMs artışı → SetResourceReference sayısı 0 (mevcut ApplyAllCount deseninde bir sayaç).

**Doğrulayıcı notu:** MainWindow.xaml.cs 352-362: tick koşulsuz TickElapsed + SetLineCount, IsRunUnderway ise PushGraphStatuses + FollowFrontier; pencere görünürlüğü kapısı yok. PushGraphStatuses 832-840 her tick RowsById (851-856) + GraphBinder.Nodes (TopologicalDepths dahil) kuruyor; ApplyStatuses (GraphView 811-829) record-eşitliğiyle yalnız son adımı kısıyor. TickElapsed 1850-1866 her tick UpdateEta. ProjectRow.ApplyDuration 442-455 her DurationMs değişiminde (285) SetResourceReference çağırıyor. GraphView.IsPanelVisible kendi Visibility'sine bakar (654), pencere tepsideyken açık kalır.

#### D6-4 — Üç sonsuz animasyon pencere tray'deyken kapılı değil — doküman §14.5 'her sonsuz animasyon IsVisible'a kapılıdır' diyor, kod demiyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` satır 621-633 — ApplyBreathing: kapı yalnız IsCompiling && AnimationsEnabledProvider(); IsVisible/IsVisibleChanged yok (Forever, 30 fps, satır başına ayrı saat)
- `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs` satır 395-415 — ApplyIndeterminate: kapı yalnız AnimationsEnabledProvider(); Forever sweep 30 fps
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 1084-1098, 1151-1170 — ApplyBeads/EnsureBeadsClock: kapı Status==Building && AnimationsEnabled; panelin kendi Visibility'si (654) layout modu içindir, pencere gizliyken Visible kalır
- `ARCHITECTURE.md` satır 5002-5010 — 'every infinite animation is gated on IsVisible as well as on its own state ... The same is true of the whole window: closing it to the tray hides it and unloads nothing'

**Kanıt:** Doküman ile kod uyuşmuyor: doküman tüm sonsuz animasyonların IsVisible'a kapılı olduğunu söylüyor; ProjectRow nefes, StickyRibbon sweep ve GraphView beads yalnız kendi durumlarına bakıyor (BuildingSpinner.cs:85 ve EventStreamView imleci ise kapılı — doğru örnekler). Koşu sürerken pencere tepsideyse: en çok 4 nefes saati + 1 sweep + 1 paylaşımlı beads saati canlı kalır; WPF zamanlama ağacı bir saat aktifken render döngüsünü uyanık tutar (dokümanın kendi ölçümü: unutulmuş tek Forever = %133 çekirdek).

**Etki:** Kullanıcının 'tray'de küçükken CPU en düşük olsun' isteğiyle doğrudan çelişir: koşu boyunca gizli pencerede 30 fps property invalidation + layout. Büyüklük ölçülmedi (tahmin: bir çekirdeğin onda birleri mertebesi; 2026-09-14 raporundaki imleç örneği 81→5 M cycle/s farkı ölçek verir). Koşu bitince saatler durur (IsCompiling düşer, spindown 1198), yani 'bittiği anda rahatlama' bu yolda sağlanıyor.

**Etki (doğrulayıcı düzeltmesi):** Büyüklük ÖLÇÜLMEDİ; 'çekirdeğin onda birleri' tahmin. Dayanak: koşu sırasında tepsideyken App 1,44 Gcycles/s ölçüldü (görünürken 1,9-3,2 G) - bu sayı içinde saatlerin payı ayrıştırılmadı (aynı pencerede IPC deserialize, konsol batch'leri ve tepsi göstergesi de var). Kesin olan: tepside koşu boyunca en az 2-5 Forever saat canlı, doküman kuralı ihlal ediliyor. Kullanıcıya 'doküman ile kod uyuşmuyor' diye bildirilmeli (CLAUDE.md kuralı).

**Çözüm:** Doküman kuralını koda taşı, kural dokümanda zaten yazılı: (1) ProjectRow: MotionGate'e ek olarak IsVisibleChanged'e abone ol (Loaded'da += / Unloaded'da -=) ve ApplyBreathing'in kapısına '&& IsVisible' ekle (yöntemin İÇİNDE, çağıranlarda değil — §14.5'in gerekçesi). (2) StickyRibbon: aynı desen, ApplyIndeterminate + IsVisibleChanged → yeniden değerlendir. (3) GraphView: EnsureBeadsClock/EnsureEdgeFlowClock kapısına 'IsVisible || !IsLoaded' (headless süitte IsVisible hep false — 732-736'daki gerekçe; IsLoaded değilken kapıyı açık bırakmak süitle üretimi ayırmaz), IsVisibleChanged'de klokları bırak/yeniden kur (_pendingStatuses replay deseni zaten var). Doküman doğru, kod düzelir — kullanıcıya bildirilmesi gereken bir doküman/kod ayrışması.

**Çözüm (doğrulayıcı düzeltmesi):** ProjectRow ve StickyRibbon için öneri doğru (kapı metodun içinde + IsVisibleChanged). GraphView için 'IsVisible || !IsLoaded' kırılgan: GraphView 731-735 IsVisible'ın headless süitte kullanılamayacağını açıkça yazıyor. Daha sağlam yol: MainWindow, pencere görünürlüğünü (1244'teki IsVisibleChanged) GraphView'a açık bir özellik olarak itsin (RunPhase gibi, örn. HostVisible); kapı EnsureBeadsClock/EnsureEdgeFlowClock içinde bu özelliğe baksın, false olunca saatler bırakılsın, true olunca BeadsVisible olan yörüngeler için yeniden kurulsun. Aynı sinyal ProjectRow/StickyRibbon'a da verilebilir (tek kaynak). Her biri için ayrı kırmızı test.

**Değişmezler:** Motion sözleşmesi (§14.5) tam olarak bu kuralı istiyor; hiçbir değişmezle çatışmaz. Reduced-motion yolu aynen kalır.

**Test fikri:** ProjectRow STA testi: satır IsCompiling iken host Visibility=Hidden → PART_Breath.HasAnimatedProperties false; Visible'a dönünce true. StickyRibbon ve GraphView için aynı kalıp (_beadsClock null olur). Mevcut MotionGuard kaynak testine 'Forever kullanan her dosya IsVisible okur' pini eklenebilir.

**Doğrulayıcı notu:** Doküman-kod ayrışması gerçek. ARCHITECTURE.md 5002-5008 'every infinite animation is gated on IsVisible ... re-evaluated from IsVisibleChanged ... The same is true of the whole window' diyor. Kod: ProjectRow.ApplyBreathing 621-633 kapısı yalnız IsCompiling && AnimationsEnabledProvider(); StickyRibbon.ApplyIndeterminate 391-415 yalnız AnimationsEnabledProvider(); GraphView.ApplyBeads 1084-1098 yalnız Status==Building && AnimationsEnabledProvider(), EnsureEdgeFlowClock 1426-1438 kapısız. Üç dosyada IsVisible/IsVisibleChanged grep'i sıfır eşleşme (GraphView'da yalnız 731-735 'kullanılamaz' yorumu). MotionGate (Controls/MotionGate.cs) pencere görünürlüğü taşımıyor. Karşı örnek BuildingSpinner.cs 85/127 kapılı. Düzeltme: sweep yalnız belirsiz modda (Syncing) kurulur; Build koşusunda tepside canlı kalanlar nefes (realize edilmiş derlenen satır başına bir saat, paralellik 4) + tek paylaşımlı beads saati (+ seçim varsa edge-flow saati).

#### D6-5 — Anlatı modunda tüm paralel projelerin ham MSBuild satırları konsola akıyor; batch başına ölü kopya işi ve satır başına ~%50 IPC/deserialize ek yükü

| Alan | Değer |
|---|---|
| Önem | kozmetik (ölü kod + batch kopyası) / onemli yalnız IPC batch kısmı ölçülürse |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2130-2134, 2060-2067 — Emit: her proje logu satırı için ProjectLogEvent (RunId + ProjectId + LineNumber + Text) yazılır — seçili proje ayrımı yok
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2712-2714 — ActiveProjectId null iken her satır _console.Post ile anlatı konsoluna gider
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 440-458 — AppendNarrativeBatch: body/newest/prefix/lineCount hesaplanır (iki substring kopyası + tam char taraması) ama HİÇ kullanılmaz; ardından AppendBatch(text)
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 274-312, 390-400 — AppendBatch: Insert + TrimToRenderSlice (GetText kopyası + Remove) + backlog AddRange her 50 ms batch'te
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 172-197 — ReadLoopAsync: satır başına JsonSerializer.Deserialize (polimorfik discriminator, reflection) → 3 string (RunId, ProjectId, Text)

**Kanıt:** Gerçek koşuda 79.734 satır / 184 s ≈ 430 satır/s ortalama; UI grubunun bir seviyesinde 4 proje × ~440 satır/s ≈ 1.7k satır/s tepe. JSON zarfı satır başına ≈170 B (type + runId ~36 + kaçışlı tam yol ~100 + lineNumber) — ortalama satır gövdesi 27 MB/79.7k ≈ 340 B → boruda ≈%50 ek yük ve satır başına 2 gereksiz string. ConsoleView.AppendNarrativeBatch'te 5 satırlık ölü hesap (v1.7.0'da daktilo kalktı, kod kaldı). Doküman §13.5 bu akışı tasarım olarak yazıyor ('a parallel build streams hundreds of lines a second'), yani davranış bilinçli; maliyet değil.

**Etki:** IPC thread'inde saniyede yüzlerce-binlerce deserialize + kilit + kanal yazımı; UI thread'inde 20 Hz batch'te 2 fazla kopya + AvalonEdit Insert/Remove. Konsol 200 satırla sınırlı olduğu için belge büyümez (temiz), ama batch başına iş gereksiz yüksek. Ölçüm yok (tahmin: IPC thread'i tepe anlarda bir çekirdeğin belirgin bir payı).

**Etki (doğrulayıcı düzeltmesi):** Ölçülen: 23.047 satır / 7,7 MB / 142 s = ~160 satır/s, ~54 KB/s; proje başına medyan 67, max 2590 satır. Bu hacimde deserialize + kilit maliyetinin App'in 1,44-3,2 Gcycles/s'lik yükündeki payı ölçülmedi; 'IPC thread'i bir çekirdeğin belirgin payı' iddiası kanıtsız (tahmin). Ölü kod batch başına 2 substring + 1 tam tarama (20 Hz) - küçük ama sıfır değerli iş.

**Çözüm:** (1) AppendNarrativeBatch'teki ölü body/newest/prefix/lineCount bloğunu sil (yalnız ClearReadyText + EnsureColorizer + AppendBatch kalır) — sıfır risk. (2) AppendBatch'te GetText kopyasını yalnız backlog'a gerçekten yeni satır girecekse al (D6-1'in tek kaynaklı backlog'uyla anlatı modunda hiç gerekmez). (3) ŞARTLI — protokol değişikliği (Contracts): Supervisor tarafında projectLog satırlarını 50 ms/ N satır pencereyle tek olayda taşıyan ProjectLogBatchEvent (ProjectId, FirstLineNumber, Lines[]) — stdout yine NDJSON, RunId olay başına bir kez; App tarafı dikiş (LineNumber) aynen çalışır. Bu, deserialize ve string sayısını 10-50× düşürür; ama §5.5 sözleşmesini genişlettiği için kullanıcı onayı ister. (4) RunId/ProjectId intern (D6-1/2).

**Çözüm (doğrulayıcı düzeltmesi):** (1) ölü bloğu sil - risksiz, mevcut testler yeşil kalmalı. (2) GetText kopyası backlog için gerekli olduğu sürece kalır. (3) ProjectLogBatchEvent protokol değişikliği: değişmez ihlali değil (stdout yine NDJSON) ama §5.5 sözleşmesini genişletir; ÖNCE ölçüm (IPC thread cycle sayısı) olmadan önerilmemeli. Daha ucuz ve tepsi senaryosuna doğrudan dokunan seçenek eksik bırakılmış: pencere gizliyken konsol batch'lerini belgeye yazmamak (bkz. missing #1).

**Değişmezler:** (1)(2)(4) hiçbir değişmeze dokunmaz. (3) 'stdout yalnız NDJSON' korunur; §5.5 Log delivery sözleşmesi genişler (yeni olay tipi) — şartlı, doküman güncellemesi gerekir.

**Test fikri:** ConsoleView testi: AppendNarrativeBatch('a\nb\n') sonrası belge metni birebir — ölü kod silinince davranış değişmediğini pinler (zaten var olan testler yeşil kalmalı). Şartlı batch olayı için: NdjsonFraming round-trip + dikiş testi (ThroughLineNumber ile örtüşen batch'in yalnız fazlası eklenir).

**Doğrulayıcı notu:** Kod kısımları doğru: RunCoordinator.Emit 2130-2134 her satır için ProjectLogEvent; RunViewModel 2714-2715 anlatı modunda her satır _console.Post; ConsoleView.AppendNarrativeBatch 441-452 body/newest/prefix/lineCount hesaplanıp hiç kullanılmıyor (ölü kod, doğrulandı); AppendBatch 275-309 + TrimToRenderSlice 391-399 her batch'te GetText kopyası + canlı satırlar için SplitLines/AddRange; EngineHost.ReadLoopAsync 171-197 satır başına deserialize. Düşürme nedeni: sayılar ölçülen koşuyla uyuşmuyor ve CPU payı ölçülmedi. Bulgu 430 satır/s ortalama (Resolve koşusu) kullanıyor; ölçülen Rebuild ~160 satır/s. '%50 zarf yükü' ortalama satır 340 B varsayımına dayanıyor; ölçülen koşuda satır başına toplam 7,7 MB / 23.047 = ~334 B (zarf dahil) - yani zarf payı daha da yüksek olabilir ama mutlak hacim küçük (7,7 MB / 142 s = ~54 KB/s). Ham satırların konsola akması §13.5'te yazılı bilinçli tasarım.

#### D6-6 — 'Back' (anlatıya dönüş) büyük koşudan sonra tüm koşu metnini UI thread'inde iki kez kopyalıyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır ShowRunConsole gövdesi (grep 'private void ShowRunConsole') — SeedRunDocument(text => ShowRunDocument(text))
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2821-2830 — SeedRunDocument: _runText.ToString() _gate altında — 27 MB koşuda 54 MB'lık string (LOH) + IPC OnProjectLog kopya süresince bloklu
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 633-642, SplitLines gövdesi — ResetRunDocument: SplitLines tüm metni ~80k substring'e böler; belgeye yalnız son 200 satır girer

**Kanıt:** Kod tam metni iki kez materyalize ediyor (string + List<string>), belge için gereken yalnız son 200 satır + geçmiş için referans. 27 MB koşu = 54 MB string + ~80k×(ortalama 340 B + 24 B başlık) ≈ 60 MB liste, tek dispatcher turunda.

**Etki:** Büyük koşu sonrası Back'te gözle görülür takılma (tahmin: 100-300 ms) ve ~110 MB geçici tahsis → Gen2/LOH GC. Küçük koşularda görünmez.

**Etki (doğrulayıcı düzeltmesi):** Ölçülen Rebuild boyutunda (metin ~3,8 MB, türetilmiş): ToString ~7,6 MB + satır listesi ~8 MB geçici tahsis, tek dispatcher turunda; süre ölçülmedi. 27 MB'lık Resolve koşusunda ~54 MB + ~60 MB (hesap). Ek gerçek etki: ToString _gate altında olduğu için kopya süresince IPC okuma thread'i OnProjectLog'ta bekler.

**Çözüm:** D6-1'in tek kaynaklı satır listesiyle çözülür: VM koşu satırlarını List<string> tutar, SeedRunDocument kilit altında yalnız Count snapshot'ı ve liste referansı verir (yeni satırlar sonradan eklenir; ConsoleBatcher reseed sentinel'i aynı görevi görür), ConsoleView.ShowRunDocument(IReadOnlyList<string>, int count) son 200 satırı Join eder — kopya yok. Kilit altında ToString kalkar.

**Çözüm (doğrulayıcı düzeltmesi):** D6-1 fix (3)'e bağlı; tek başına ucuz ara adım: SeedRunDocument kilit altında yalnız son N satırı (render dilimi) kopyalasın, backlog tepeye kaydırılınca tembel doldurulsun. Büyük refactor ölçümsüz yapılmamalı.

**Değişmezler:** ConsoleBatcher generation-guard atomikliği (D4 review §1) korunur: snapshot sınırı Count ile ifade edilir, sentinel aynı kilitte yazılır.

**Test fikri:** ConsoleView testi: 100k satırlık liste ile ShowRunDocument → belge LineCount == 200 ve çağrı boyunca yeni string tahsisi satır sayısıyla ölçeklenmez (GC.GetAllocatedBytesForCurrentThread farkı ≤ sabit × 200 satır).

**Doğrulayıcı notu:** Kod doğru: MainWindow.ShowRunConsole 645-651 -> SeedRunDocument (RunViewModel 2820-2829, _gate altında _runText.ToString()) -> ConsoleView.ResetRunDocument 634-642 SplitLines tüm metni böler, belgeye son 200 satır girer. Düşürme: yalnız kullanıcı Back/seçim kaldırma yaptığında bir kez çalışır; tepside-derleme senaryosuna dokunmaz; 100-300 ms ve ~110 MB yalnız 27 MB'lık Resolve koşusu için tahmin.

#### D6-7 — UiResponsivenessBudgetTests koşu akışının gerçek şeklini ölçmüyor: skip patlaması, kümülatif süre ve tam tick gövdesi bütçe dışı

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) tests/BuildOrchestrator.Tests/App/UiResponsivenessBudgetTests.cs 107-135 (olay testi), 139-163 (tick testi), 24-30 (LocalOnly + 71 ms notu)
- `tests/BuildOrchestrator.Tests/App/UiResponsivenessBudgetTests.cs` satır 117-142 — No_single_project_event: yalnız Started+Succeeded çiftleri, tek olay <50 ms; toplam süre yalnız yazdırılır, assert yok; ProjectSkippedEvent/CycleMemberHeldEvent yok
- `tests/BuildOrchestrator.Tests/App/UiResponsivenessBudgetTests.cs` satır 146-166 — Tick testi yalnız graph.UpdateStatuses'ı ölçer; RowsById + GraphBinder.Nodes + TickElapsed + FollowFrontier ölçüm dışında
- `tests/BuildOrchestrator.Tests/App/UiResponsivenessBudgetTests.cs` satır 27-28 — Category=LocalOnly: CI'da hiç koşmaz; doc'ta CI'da tek olay 71 ms ölçüldüğü yazıyor

**Kanıt:** Gerçek koşunun ilk saniyesi 154 ardışık ProjectSkippedEvent'tir (decision.log); test bu olayı hiç üretmiyor ve olay başına 50 ms'lik tavan 154 olayda 7,7 s'ye izin verir. Tick testi beslemenin en ucuz parçasını ölçüyor.

**Etki:** D6-2 ve D6-3'teki regresyonlar sessizce büyüyebilir; 'UI thread hiçbir şart ve koşulda kilitlenmez' iddiası patlama senaryosu için kanıtsız.

**Çözüm:** Aynı dosyaya: (a) 'Pre-skip burst' — RunStarted + 177 ProjectSkippedEvent(UpToDate) + BuildPreview → toplam duvar süresi ≤ BudgetMs (120) VE en kötü tek olay ≤ EventBudgetMs; (b) tick testi MainWindow'un gerçek tick yolunu (internal TickForTest seam: TickElapsed + PushGraphStatuses + FollowFrontier) ölçsün; (c) Cycles akışı: CycleMemberHeld/Started dizisi. Fix'lerden ÖNCE kırmızı gösterilerek yazılır (CLAUDE.md kırmızı test kuralı).

**Çözüm (doğrulayıcı düzeltmesi):** Öneri doğru. Ek: patlama testi Build modunda UpToDate skip'leriyle yazılmalı (Cycles OutOfCycleScope dalı hafif yoldur, kırmızı vermez); tick testi için MainWindow'a internal tick seam'i gerekir. Tepsi senaryosu için ayrı test: pencere Hide() edilmişken aynı olay akışının toplam süresi.

**Değişmezler:** Test kuralları: eşik gevşetilmez, LocalOnly kalır.

**Test fikri:** Yukarıdaki (a)(b)(c) testlerin kendisi.

**Doğrulayıcı notu:** UiResponsivenessBudgetTests.cs: sınıf Trait Category=LocalOnly (30); No_single_project_event testi (107-135) yalnız ProjectStarted + ProjectSucceeded üretir, toplam süre yalnız yazdırılır, assert yalnız en kötü tek olay < EventBudgetMs; ProjectSkippedEvent/CycleMemberHeld yok. Tick testi (139-163) beslemeyi MsOf dışında kurar, yalnız graph.UpdateStatuses + UpdateLayout ölçer; TickElapsed/RowsById/GraphBinder.Nodes/FollowFrontier ölçüm dışı. Doc'taki 71 ms CI değeri 24-26. satırlarda. Satır numaraları birkaç satır kaymış.

#### D6-8 — BuildMenu her Counters değişiminde 3 menü satırını yıkıp yeniden kuruyor — yalnız Total okunmasına rağmen

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs` satır 67-71 — OnVmPropertyChanged: Counters → RefreshRows
- `src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs` satır RefreshRows gövdesi (grep 'private void RefreshRows') — PART_Rows.Children.Clear() + 3 × BuildRow — kapalı popup için her olayda

**Kanıt:** Yorum 'Menü içeriğini etkileyen TEK sinyal ... toplam proje sayısı' diyor ama kapı Counters'ın herhangi bir alanı; koşuda her proje olayı (154 skip + her Started/Succeeded) menüyü yeniden inşa eder.

**Etki:** Olay başına ~15-20 WPF nesnesi çöpe gider; görünür değil ama D6-2 patlamasına eklenir.

**Etki (doğrulayıcı düzeltmesi):** Nesne sayısı (15-20) ve süre ölçülmedi. Kesin olan: olay başına 3 satırın yıkılıp kurulması, pencere tepsideyken de.

**Çözüm:** _lastTotal alanı; RefreshRows yalnız Counters.Total değişince (ya da OnVmPropertyChanged'da if (_vm.Counters.Total == _lastTotal) return).

**Değişmezler:** Yok.

**Test fikri:** BuildMenu testi: Counters Total sabitken 10 bildirim → PART_Rows.Children referansları değişmez.

**Doğrulayıcı notu:** BuildMenu.xaml.cs 67-71: Counters değişiminde RefreshRows; 98-106: yalnız Counters.Total okunur, PART_Rows.Children.Clear() + her öğe için BuildRow (Grid + ikon + metinler). Counters koşuda her durum değişiminde yeni değer alır, yani kapalı menü her proje olayında yeniden kurulur.

#### D6-9 — OnBuildPreview O(n²): 187 önizleme öğesinin her biri için doğrusal FindRow

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2054-2058, 2326-2329 — foreach item → EnsureRow → FindRow (Projects.FirstOrDefault, tam yol OrdinalIgnoreCase)

**Kanıt:** 187 × ortalama 94 karşılaştırma ≈ 17.5k tam yol (~90 karakter) karşılaştırması, her Sync ve her koşu başında; Sync bütçe testi bu adımı 120 ms altında tutuyor (geçiyor), yani bugün görünür değil.

**Etki:** Düşük (tahmin: birkaç ms); D6-2'nin _rowById haritasıyla kendiliğinden O(n) olur.

**Etki (doğrulayıcı düzeltmesi):** 187 x ortalama ~94 karşılaştırma = ~17,5k string karşılaştırması, her önizlemede (pencereye dönüş Sync'i dahil). Süre tahmin (ms altı - birkaç ms).

**Çözüm:** D6-2 (1) ile aynı harita.

**Değişmezler:** Yok.

**Test fikri:** D6-2 testinin buildPreview adımı.

**Doğrulayıcı notu:** RunViewModel.cs 2054-2058 foreach item -> EnsureRow -> FindRow (2323-2324, Projects.FirstOrDefault + OrdinalIgnoreCase). Karesel yapı kesin; süre ölçülmedi, bugünkü 187 satırda görünür bir etki kanıtı yok (Sync bütçe testi yeşil).

#### D6-10 — Proje seçiminde 64 KB chunk'ların her biri ayrı dispatcher turu; dikiş her seferinde tam log kopyası

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/Logs/LogChunker.cs` satır 7 — MaxChunkChars = 64 KB → 4 MB log = ~64 ProjectLogChunkEvent, her biri Dispatcher.InvokeAsync (MainWindow 342)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2979-3010 — OnProjectLogChunk: Assembly.Append (UI thread) + son chunk'ta pending.Assembly.ToString() kopyası → new StringBuilder(kopya) → _projectText
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır OnSelectedProjectChangedAsync gövdesi — SeedProjectDocument → sb.ToString() (ikinci kopya) → SplitLogLines (üçüncü, satır listesi) → PlayCascade [.. allLines] (dördüncü, liste kopyası)

**Kanıt:** Types.UsedCars logu 8.824 satır / ~4 MB: seçimde ~64 dispatcher turu + 4 materyalizasyon (≈ 4×8 MB UTF-16). PlayCascade satır başına animasyon yapmıyor (temiz), yalnız kopya.

**Etki:** Seçimde kısa takılma olası (tahmin: 20-60 ms); RAM geçici +30 MB. Kullanıcı hissetmiyorsa öncelik düşük.

**Etki (doğrulayıcı düzeltmesi):** Süre (20-60 ms) tahmin. Ölçülen koşuda proje başına medyan 67, p90 202, max 2590 satır: tipik proje logu tek chunk'a sığar; ~64 chunk yalnız 4 MB'lık uç örnek için. Ek: dikişteki buffered.Where(...).OrderBy(...) _gate altında o projenin tüm canlı satırlarını tarar.

**Çözüm:** OnProjectLogChunk'ta new StringBuilder(pending.Assembly.ToString()) yerine pending.Assembly'yi doğrudan devral (sahipliği geçir) ve dikişi ona Append et; SeedProjectDocument satır listesi versin (D6-1/6 ile aynı IReadOnlyList<string> arayüzü), PlayCascade kopyayı [.. allLines] yerine çağıranın listesini sahiplensin (canlı büyüme için VM listesi zaten kaynak).

**Değişmezler:** §5.5 dikiş sözleşmesi aynen.

**Test fikri:** Mevcut dikiş testleri (ThroughLineNumber) yeşil kalır; ek: 3 chunk'lık yükleme sonrası _projectText StringBuilder'ı pending.Assembly ile ReferenceEquals.

**Doğrulayıcı notu:** LogChunker.cs 7 MaxChunkChars = 64*1024; MainWindow 342 her olay ayrı InvokeAsync; RunViewModel 2979-3013 son chunk'ta new StringBuilder(pending.Assembly.ToString()) kopyası (_gate altında); MainWindow 633-634 SeedProjectDocument (sb.ToString) -> SplitLogLines -> PlayCascade; ConsoleView 682 '_backlogLines = [.. allLines]' liste kopyası. Yalnız kullanıcı proje seçince çalışır.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.App/MainWindow.xaml.cs 580 + 598-606 (AppendConsoleBatch): pencere görünürlüğü kapısı yok - tepsideyken de her 50 ms batch'i AvalonEdit belgesine Insert + TrimToRenderSlice + backlog AddRange olarak işleniyor; gizliyken batch'ler düşürülüp pencere görününce mevcut SeedRunDocument/ShowRunDocument yolu ile bir kez tohumlanabilir (VM tamponu _runText zaten tam).
- src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs 213-216 + 244-265: ElapsedMs/EtaMs her 200 ms tick'te RefreshText çağırıyor; RefreshText koşulsuz _vm.RibbonLine üretir, PART_PhaseText.Text yazar, SetResourceReference ve RefreshOpPill çalıştırır - §14.5'in '200 ms tick yalnız değer değişince yazar' kuralının değişim kontrolü yok (ElapsedMs ms çözünürlüklü olduğu için her tick değişir), pencere gizliyken de döner.
- src/BuildOrchestrator.App/MainWindow.xaml.cs 339-343 ve 352-362 genel: non-log olayların tamamı ve tick, pencere Hide() durumundayken de tüm görünüm zincirini (liste, graf, şerit, BuildMenu) çalıştırıyor; kodda 'pencere gizli -> görünüm güncellemelerini beklet, gösterilince tek seferde uygula' mekanizması yalnız GraphView'un panel Visibility'si için var (GraphView.xaml.cs 654-687), pencere düzeyinde yok - kullanıcının birincil senaryosu (tepside Build) için asıl yapısal eksik bu.
- src/BuildOrchestrator.App/ViewModels/RunViewModel.cs 1850-1866 (TickElapsed): Started satırlar için tüm Projects her tick taranıyor ve DurationMs ms çözünürlükle yazılıyor -> realize edilmiş her derlenen satırda saniyede 5 PropertyChanged + ApplyDuration; metin saniye çözünürlüklüyse yazım saniyede bir kez yeterli (DurationFormat.Elapsed çözünürlüğü doğrulanmadı - kontrol edilmeli).
- src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs 429-442 (RebuildChipsIfChanged): her çağrıda iki Where.ToList + iki string.Join (tam yol Id'leri) kuruluyor; Counters ve VisibleProjects dallarının ikisinden de çağrıldığı için durum olayı başına iki kez - imza karşılaştırması değişmeyen durumda da tam tahsis yapıyor.

**Temiz bulunan alanlar:**
- ConsoleBatcher (Console/ConsoleBatcher.cs): kanal SingleReader, boşta uyuyan pencere (WaitToReadAsync → 50 ms), batch başına TEK flush/TEK Dispatcher.InvokeAsync, reseed generation guard — satır başına dispatcher yok.
- Konsol belgesi 200 satır render dilimiyle sınırlı (ConsoleRenderSlice.DefaultMaxLines, ConsoleView.TrimToRenderSlice tek Remove) — 10k+ satırda AvalonEdit yavaşlaması yok; colorizer offset tabanlı, belge düz metin.
- Event stream tamponu sınırlı: render 150 (RunViewModel.Stream.cs:347), tampon 260 (StreamComposer.BufferCap); daktilo Stopwatch tabanlı, imleç saati IsVisible kapılı (EventStreamView.xaml.cs:107-109, 419).
- FixedHeightVirtualizingPanel: kümülatif yükseklik tablosu + ikili arama, yalnız viewport + %50 cache realize (~32 satır), Recycling container geri dönüşümü; ListRealizationPerfTests 191 ve 500 satırda aynı bütçe (<120 ms) — repo büyüklüğüyle ölçeklenmiyor.
- StickyLayerList.RefreshVisibleRows imza guard'ı: filtre yokken proje olayları ItemsSource reset'i tetiklemiyor (MainWindow 709-716); UpdateOverlay ReferenceEquals guard'ıyla her ScrollChanged'de ItemsSource yazmıyor.
- ProjectRow: XAML'de sıfır Binding (kod-tarafı push), hover eylem bloğu ilk hover'a dek kurulmuyor (16 nesne ertelenmiş), satır başına ApplyAll bir kez (ApplyAllCount pini), nesne tavanı 43 (deterministik sayım testi).
- StepPlayer/StepHold: tek DispatcherTimer (Render), adımlar arası fark interval, bitince kendini durduruyor; onlarca ayrı saat yok. Reduced-motion'da bekleme yok.
- GraphView.ApplyStatuses record-eşitliği diff'i (811-829): değişmeyen düğüme dokunmuyor; beads ve edge-flow için düğüm/kenar başına değil PAYLAŞIMLI tek saat; panel gizliyken (list/focus layout) besleme bekletilip replay ediliyor (679-686, 732-748).
- BuildingSpinner IsVisibleChanged kapılı (Controls/BuildingSpinner.cs:85); ConsoleHeader.SetLineCount ve tick'teki 'N lines' yazımı değişmediyse yazmıyor (§14.5 kuralına uygun).
- ProjectLogEvent marshal-free yolu: satır başına Dispatcher yok, kilit kısa (Channel TryWrite), Counters record struct olduğu için eşit değerde PropertyChanged yayılmıyor.
- EngineHost.ReadLoopAsync: tek 'byte[] _buffer' + yeniden kullanılan MemoryStream satır tamponu; satır başına yalnız deserialize tahsisi. _elapsedTimer varsayılan Background önceliği — giriş ve render'a yol veriyor.
- ActionBar.RefreshChips O(1) (yalnız sayaç metinleri); ActiveFilters/filtre değişimi imza guard'ından geçiyor; RefreshGraphFilter filtre yokken null yazıyor (ucuz).

**Açık sorular:**
- Resolve/Build sırasında pencereyi genelde önde mi tutuyorsunuz, yoksa tepsiye mi indiriyorsunuz? (D6-4'ün önceliğini belirler: tepsideyken nefes/sweep/beads saatleri dönüyor.)
- Büyük bir koşudan sonra konsolda 'Back'e basınca ya da bir proje kartı seçince gözle görülür bir takılma hissediyor musunuz? (D6-6/D6-10 doğrulaması; tahmin 100-300 ms.)
- Anlatı (narrative) modunda konsolun 4 paralel projenin HAM MSBuild satırlarını akıtması istediğiniz davranış mı (ARCHITECTURE §13.5 bunu tasarım olarak yazıyor)? Yalnız planlama/karar satırları + seçili proje logu yeterliyse D6-5'in yükü kökten düşer (satır akışı yalnız seçili projeye) — ama bu bir davranış değişikliğidir.
- IPC sözleşmesinde (Contracts) projectLog satırlarını toplu taşıyan yeni bir olay tipine (D6-5 şartlı madde 3) izin var mı, yoksa §5.5 sözleşmesi dokunulmaz mı?
- Resolve sonrası App'in RAM'i (Görev Yöneticisi, boşta) kaç MB görünüyor? D6-1'in 130-180 MB hesabını gerçek RSS ile doğrulamak için tek sayı yeter.
- Koşu başında (ilk 1-2 s, 154 skip patlaması) tıklama/kaydırma gecikmesi fark ediyor musunuz? D6-2 için bütçe testini yazmadan önce lokal tek olay süresini ölçmemiz gerekir (test LocalOnly, ben koşturmadım).


### D7-animasyon-arka-plan

#### D7-1 — Pencere tray'e inince (Hide) satır nefesi, graf beads saati ve şerit süpürmesi durmuyor — §14.5 IsVisible kuralı üç sahipte hâlâ eksik

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` satır 621-633 — ApplyBreathing: `shouldBreathe = building && AnimationsEnabledProvider()` — IsVisible yok; PART_Breath 3.8 s Forever @30fps (BuildBreathingAnimation 176-185)
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 1084-1097, 1151-1169 — ApplyBeads: `live = Status==Building && AnimationsEnabledProvider()`; EnsureBeadsClock Forever @30fps — pencere kapısı yok
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 654, 666-687 — IsPanelVisible = `Visibility == Visible` — yalnız list/focus modunu yakalar; pencere Hide() ile gizlenince Visibility Visible kalır, statü itişi ve saat sürer (headless gerekçesi 731-737)
- `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs` satır 391-415 — ApplyIndeterminate: Forever sweep, kapı yalnız AnimationsEnabledProvider(); Syncing/Starting fazında (tray'de HeadWatcher auto-sync ve Ctrl+Shift+Space ile başlayan koşunun Starting evresi) gizliyken döner
- `src/BuildOrchestrator.App/Controls/MotionGate.cs` satır 31, 54-60 — StaticAnimationsEnabled = App.Motion.AnimationsEnabled — tek okuma noktası; tek kapı buraya takılabilir
- `src/BuildOrchestrator.App/Services/MotionSettings.cs` satır 46-53, 74-86 — AnimationsEnabled yalnız OS sinyali; AnimationsEnabledChanged tüm sahiplere zaten dağıtılıyor (MotionGate.Changed → ApplyBreathing/ReapplyMotion/Refresh)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1241-1245, 1329-1332, 1344-1348 — OnTrayIndicatorMotionChanged App.Motion'a bağlı; MinimizeToTray → Hide(); ShowFromTray → Show()

**Kanıt:** Önceki geçiş (2026-09-14 raporu §4) bu üç sahibi 'hâlâ uymuyor' diye listeledi ve karar bekledi; kod bugün de aynı: hiçbirinde IsVisible/IsVisibleChanged yok. §14.5 kuralı: 'every infinite animation is gated on IsVisible as well as on its own state'. Kullanıcı senaryosu tam bu: 3-4 dk Resolve cycles koşusu tray'de → Balanced'ta 4 satır nefes alıyor (4 opacity saati) + 4 yörünge tek saatte ama 4 Rectangle StrokeDashOffset her karede yeniden çiziliyor + PushGraphStatuses (bkz. D7-3). Ölçülmüş referans: yalnız iki imleç saatinin kapısı boşta tray'de 81→5 M cycle/s indirdi (§14.5) — aktif saatler gizliyken render döngüsünü ayakta tutar.

**Etki:** Tray'de build sürerken App tarafında 30 fps'lik UI-thread timing tick'i + dirty-region render sürer; kullanıcının 'tray'de küçükken her şey ölmeli' beklentisi karşılanmıyor. Sayı: tahmin (ölçülmeli); mertebe olarak 2026-09-14'teki imleç ölçümüyle aynı sınıf (on milyonlarca cycle/s).

**Etki (doğrulayıcı düzeltmesi):** Kapi eksikligi kesin (kod). Buyukluk olculmedi: tepside-derleme toplam 1,44 Gcycles/s olculdu, saatlerin payi bilinmiyor (tahmin). 81->5 M referansi bosta imlec+pump icindir, bu saatlere dogrudan aktarilamaz.

**Çözüm:** TEK KAPI, sahiplere dokunmadan: MotionSettings'e 'yüzey askıda' sinyali ekle — `SetSurfaceSuspended(bool)`; yeni özellik `Decorative => AnimationsEnabled && !_suspended`; askı değişince mevcut `AnimationsEnabledChanged` yayınlanır (sahipler zaten abone). MotionGate.StaticAnimationsEnabled ve MotionGate.Enabled varsayılanı `Decorative`'i okur → ProjectRow/GraphView/StickyRibbon/BuildingSpinner/imleçler pencere gizlenince kendiliğinden söker, geri gelince kurar. MainWindow: `IsVisibleChanged` (+ D7-2'deki StateChanged) → `App.Motion.SetSurfaceSuspended(!IsVisible || WindowState==Minimized)`. DİKKAT iki istisna: (1) `Duration.*` sıfırlama (MotionSettings.Apply) askıya BAĞLANMAZ — yalnız OS sinyaline; askı reduced-motion değildir. (2) TrayBuildIndicatorController.SetAnimationsEnabled (MainWindow 1241) saf OS sinyalini (`AnimationsEnabled`) okumaya devam eder, yoksa overlay tray'de statik kareye düşer. Yan etki (istenen): koreografi/dalga gizliyken StepPlayer yerine anında sonuçlanır → tray'den hotkey ile başlayan build komutu beklemeden gider. Test: HiddenCursorClockTests deseni ile üç yeni test — window.Hide() → PART_Breath.HasAnimatedProperties=false, GraphView.BeadsClock=null, PART_IndicatorTranslate.HasAnimatedProperties=false; Show() → geri. Alternatif (dağınık): her sahibe ayrı IsVisible kapısı — GraphView headless gerekçesi (731-737) yüzünden Visibility+Window.IsVisible ikilisi gerekir, önerilmez.

**Çözüm (doğrulayıcı düzeltmesi):** Onerilen tek kapi (MotionSettings 'Decorative' + MotionGate.StaticAnimationsEnabled) uygulanabilir ama EKSIK: (1) MainWindow.xaml.cs:67 ve :71 choreographer ve StepHold `App.Motion?.AnimationsEnabled`'i DOGRUDAN okur, MotionGate'i degil — bulgunun vaat ettigi 'gizliyken koreografi atlanir, komut hemen gider' yan etkisi bu iki satir da yeni sinyale cevrilmeden GERCEKLESMEZ. (2) GraphView.ReapplyMotion (338-343) yalniz beads'i yeniden degerlendirir; edge-flow saatini (1399, 1426-1438) ne soker ne kurar — askida da donmeye devam eder; ReapplyMotion'a edge-flow dali eklenmeli. (3) StickyRibbon'un AnimationsEnabledChanged'e abone olup sweep'i yeniden degerlendirdigi dogrulanmali (abonelik yoksa askida sweep surer). (4) Show aninda tum sahiplerin ayni anda yeniden kurulmasi tek seferlik bir patlama uretir — olculmeli. Kirmizi test once: Hide() sonrasi PART_Breath.HasAnimatedProperties / beads saati / PART_IndicatorTranslate.

**Değişmezler:** Değişmezlere dokunmaz (UI katmanı). §14.5 kuralını genişletir: 'gizli/minimize pencere → tüm dekoratif saatler askıda'. ARCHITECTURE §14.5 son paragraf ve §12.3 güncellenmeli.

**Test fikri:** HiddenCursorClockTests kalıbı: DsResources.Realize + Hide/Show; ayrıca MotionOwnerHygieneTests'e 'her Forever sahibi Decorative'i okur' guard'ı (grep tabanlı).

**Doğrulayıcı notu:** Kod dogrulandi: ProjectRow.ApplyBreathing (ProjectRow.xaml.cs:621-633) yalniz `building && AnimationsEnabledProvider()` okur; GraphView.ApplyBeads (GraphView.xaml.cs:1084-1088) ayni; IsPanelVisible (654) yalniz Visibility'ye bakar, pencere Hide() bunu degistirmez; StickyRibbon.ApplyIndeterminate (391-415) Forever sweep'i yalniz motion kapisiyla kurar. Ucunde de IsVisible/IsVisibleChanged yok (grep). ARCHITECTURE.md:5002-5008 kurali (her sonsuz animasyon IsVisible'a bagli, pencere dahil) bu uc sahipte uygulanmiyor. Olcum yonu tutarli: tepside Rebuild sirasinda App 1,44 Gcycles/s (E1 ornegi) — ama bu sayinin ne kadarinin bu saatlerden, ne kadarinin log/IPC isleminden geldigi AYRISTIRILMADI; bulgunun 'on milyonlarca cycle/s' mertebesi tahmindir.

#### D7-2 — Minimize edilmiş pencerede hiçbir kapı tetiklenmiyor: WPF'te IsVisible minimize'da true kalır; occluded pencere için WPF'in mekanizması yok

| Alan | Değer |
|---|---|
| Önem | kozmetik-onemli arasi (olcum gelene kadar 'onemli' degil) |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1244, 1310-1311 — IsVisibleChanged yalnız Hide/Show'u görür; WindowState==Minimized ayrı okunuyor (Hotkey kararı) — Minimized için hiçbir saat/besleme kapısı yok
- `src/BuildOrchestrator.App/Shell/Hotkey.cs` satır 138-139 — WindowToggle.Decide(isVisible, isMinimized, isActive) — kod ikisinin ayrı olduğunu zaten biliyor
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 577 — İmleç kapısı `if (!IsVisible)` — minimize'da true → imleç saatleri minimize'da döner
- `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs` satır 107-111, 419 — Aynı IsVisible kapısı — minimize'da etkisiz
- `src/BuildOrchestrator.App/Controls/BuildingSpinner.cs` satır 85, 127 — IsVisible && _motion.Enabled — minimize'da döner

**Kanıt:** WPF'te UIElement.IsVisible görsel ağaç görünürlüğüdür (Visibility + pencere Hide); minimize edilmiş pencerede true kalır ve IsVisibleChanged ateşlenmez. WPF timing engine minimize/occluded farkını bilmez: aktif saatler tick etmeye, dirty visual'lar render data üretmeye devam eder (bilinen WPF davranışı; DWM yalnız sunumu atlar). Yani 2026-09-14'te imleçler için kazanılan 81→5 M cycle/s kazancı, kullanıcı pencereyi tray yerine görev çubuğuna küçültürse kaybolur. Occluded (başka pencerenin arkasında): WPF'e occlusion bilgisi gelmez, kapı imkânsız — bilinen sınır.

**Etki:** Minimize edilmiş pencere = görünür pencereyle aynı animasyon/render maliyeti (imleçler 4 saat ~55-60 uyanış/s boşta — bkz. D7-4; koşarken nefes/beads/spinner). Occluded'da aynı, kaçınılmaz.

**Etki (doğrulayıcı düzeltmesi):** Kapi eksikligi kesin; maliyet bilinmiyor. Ust sinir tahmini icin elde tek olcum: on planda bosta 131-139 M vs tepside 50-54 Mcycles/s — minimize'da en fazla aradaki ~80 M/s (tek cekirdegin ~%2-3'u) geri gelir; minimize'da gercekte ne kadari kaldigi olculmeli.

**Çözüm:** D7-1'in askı sinyalini `!IsVisible || WindowState == WindowState.Minimized` olarak kur; MainWindow'da `StateChanged += ...` aboneliği (CaptionGlyphs.cs:75 zaten WindowStateProperty'yi DependencyPropertyDescriptor ile izliyor — aynı desen). Occluded için: yapılacak bir şey yok, §20'ye 'başka pencerenin arkasındaki pencere görünür sayılır' satırı eklenmeli; kullanıcıya öneri: arkaya atmak yerine tray'e indir (Shift+Space).

**Çözüm (doğrulayıcı düzeltmesi):** D7-1'in askisina `WindowState == Minimized` eklenmesi dogru; once minimize durumunda 10 s QueryProcessCycleTime olcumu alinmali (ayni measure.ps1). Occluded icin §20 notu yeterli.

**Değişmezler:** Değişmezlere dokunmaz. §12.3/§14.5 dokümana 'minimize = askı' eklenir.

**Test fikri:** Realize edilmiş pencerede WindowState=Minimized → Decorative=false ve imleç HasAnimatedProperties=false (HiddenCursorClockTests'e üçüncü senaryo).

**Doğrulayıcı notu:** Konumlar dogru (MainWindow.xaml.cs:1244 IsVisibleChanged, 1310-1311 WindowState ayri okunuyor; Hotkey.cs:138-139 WindowToggle.Decide; ConsoleView.xaml.cs:577; EventStreamView.xaml.cs:107-111, 419; BuildingSpinner.cs:85,127). WPF'te minimize IsVisible'i degistirmez — kapilarin minimize'da tetiklenmedigi koddan kesin. Ancak 'minimize = gorunur pencereyle ayni maliyet' iddiasi OLCULMEDI; olcum setinde minimize durumu yok. Birincil senaryo tepsi (CloseToTray acik), minimize ikincil.

#### D7-3 — 200 ms tick gizli/minimize pencerede tam UI beslemesini sürdürüyor: PushGraphStatuses (187 GraphNode + TopologicalDepths/tick), FollowFrontier (scroll animasyonu), SetLineCount, satır DurationMs yazımı

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 352-364 — _elapsedTimer.Tick: TickElapsed + SetLineCount + `if (_vm.IsRunUnderway) { PushGraphStatuses(); FollowFrontier(); }` — IsVisible kapısı yok
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 832-840 — PushGraphStatuses → GraphBinder.Nodes(_vm.Topology, RowsById()) her tick
- `src/BuildOrchestrator.App/ViewModels/GraphBinder.cs` satır 31-55 — Nodes: TopologicalDepths(topology) + topology.Count kadar `new GraphNode(...)` (187 kayıt) her çağrıda
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 781-797 — FollowFrontier → ProjectsList.FollowRow → ScrollAnimator (Duration.Slow saat) gizliyken de kurulur
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1850-1858 — TickElapsed: tüm Projects taranır, Started satırların DurationMs'i her tick yazılır → ProjectRow.ApplyDuration metin/layout invalidation (gizliyken de)
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 811-830 — ApplyStatuses record eşitliğiyle değişmeyeni atlar (iyi) — ama üretim tarafı (GraphBinder) yine her tick tam liste kurar

**Kanıt:** Tick'in tek kapısı IsRunUnderway (boşta itmez — doğru). Koşarken pencere tray'de/minimize iken saniyede 5 kez: 187 GraphNode allocation + dictionary derinlik hesabı + 187 record karşılaştırması + frontier scroll animasyonu + satır süre metinleri. Hepsi görünmeyen yüzeye. GraphView'ın 'panel gizliyken sakla' kapısı (654) yalnız Visibility'ye baktığından pencere gizliyken devreye girmez.

**Etki:** Tahmin: tick başına ~1-3 ms UI-thread (187 alloc + karşılaştırma + layout invalidation), yani gizliyken %0.5-1.5 tek çekirdek + GC baskısı; asıl mesele 'tray'de her şey ölmeli' beklentisi. Sayı ölçülmeli.

**Etki (doğrulayıcı düzeltmesi):** Is yuku kesin (saniyede 5x: 187 GraphNode + 3 Dictionary/HashSet + 187 record karsilastirmasi + FollowRow), ms/tick degeri ve '%0,5-1,5' TAHMIN — olculmedi. GC baskisi App Private'in kosu boyunca 194->381 MB buyumesiyle ayni yonde ama pay ayristirilmadi.

**Çözüm:** Tick gövdesine görünürlük kapısı (VM zamanı saymaya devam eder — ETA/elapsed doğruluğu bozulmaz): `_vm.TickElapsed(); if (!IsVisible || WindowState==Minimized) return;` sonra SetLineCount/PushGraphStatuses/FollowFrontier. Pencere geri gelince (IsVisibleChanged → true ve StateChanged) BİR kez PushGraphStatuses()+FollowFrontier() çağır — GraphView zaten 'pending' modelini (652, 683) uygulayacak biçimde tasarlı, ek mantık yok. İsteğe bağlı ikinci adım: GraphBinder.Nodes'un TopologicalDepths'ini topoloji değişmedikçe önbelleğe al (topoloji imzası zaten RunViewModel.Workspace.cs:212'de var) — tick başına 187 alloc'a iner.

**Çözüm (doğrulayıcı düzeltmesi):** Onerilen kapi dogru; ek: (1) RowsById sozlugu ve TopologicalDepths topoloji degisene kadar onbelleklenmeli (gorunurken de kazanc). (2) Pencere geri gelince tek seferlik PushGraphStatuses + FollowFrontier sart (ekran tutarliligi). (3) TickElapsed icindeki DurationMs/UpdateEta yazimlari VM'de kalir ama gizli pencerede binding/layout invalidation uretir — satir tarafinda ayri olculmeli. Kirmizi test: Hide() + 1 s pump -> GraphHost.NodeStatusApplyCount ve GraphBinder cagri sayisi artmaz.

**Değişmezler:** Değişmezlere dokunmaz; §14.5 'anything called from the 200 ms tick writes only when the value actually changed' ilkesinin görünürlük boyutuna genişletilmesi.

**Test fikri:** MainWindow realize testi: Hide() → 1 s pump → GraphHost.NodeStatusApplyCount değişmedi; Show() → bir kez itildi.

**Doğrulayıcı notu:** MainWindow.xaml.cs:352-363 tick govdesi dogru: TickElapsed + SetLineCount + IsRunUnderway ise PushGraphStatuses + FollowFrontier; gorunurluk kapisi yok. PushGraphStatuses (832-840) her tick GraphBinder.Nodes cagirir; GraphBinder.cs:31-55 her cagrida TopologicalDepths (iki Dictionary + HashSet, 84-88) + topology.Count kadar GraphNode kurar. Bulgunun kacirdigi ek tahsis: RowsById() (MainWindow.xaml.cs:851-856) her tick 187 girisli yeni Dictionary. GraphView.ApplyStatuses (811-830) record esitligiyle degismeyeni atlar — dogru. RunViewModel.cs:1850-1863 Started satirlarin DurationMs'i + UpdateEta her tick yazilir.

#### D7-4 — Görünür boşta iki imleç = dört sonsuz saat, iki farklı DesiredFrameRate (30 ve 20) ve faz kaymalı → render döngüsü 30 yerine ~55-60 uyanış/s

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Controls/MotionTokens.cs` satır 41-51 — CreateBlinkAnimation: 550 ms AutoReverse Forever, DesiredFrameRate=30
- `src/BuildOrchestrator.App/Controls/CursorHop.cs` satır 31-39, 104 — Renk turu StepMs 1100, PhaseMs −550 (BeginTime negatif), FrameRate=20 — ayrı kök timeline, farklı hız ve faz
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 573-584 — StartBlink: ActiveCursor.BeginAnimation(blink) + CursorHop.Start — iki bağımsız kök saat
- `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs` satır 416-424 — StartCursorBlink: aynı iki saat ikinci imleç için — toplam 4 kök saat

**Kanıt:** WPF timing engine her KÖK timeline'ın DesiredFrameRate'ini ayrı uygular; farklı hız/fazlı kökler render tick'lerinin BİRLEŞİMİNİ üretir. 2026-09-14 raporu §5 bunu ölçtü: 'görünür boşta render döngüsü ~55-60 kare/sn, iki imlecin kırpma ve renk turu fazları farklı' — tasarım kararı diye bırakıldı. Ama tasarım 'yanıp sönen imleç'tir, saat sayısı değil: aynı görsel tek kökle 30 uyanış/s'de üretilebilir.

**Etki:** Boşta görünür pencerede render/timing döngüsü ~2× gereksiz uyanış (55-60 vs 30). Pil/ısı için boşta en görünür kalem; laptop (Core Ultra 258V) senaryosunda önemli.

**Etki (doğrulayıcı düzeltmesi):** Ust sinir olcumden: on planda bosta 131-139 M vs tepside 50-54 Mcycles/s -> gorunur bosta TUM render+imlec maliyeti ~80 M/s (tek cekirdegin ~%2-3'u). Saatleri birlestirmek bunun en fazla bir kismini alir; kazanc olculmeden soylenemez.

**Çözüm:** Blink + renk turunu TEK kök altına al: `Storyboard`/`ParallelTimeline` içinde blink (opacity, Storyboard.Target=imleç) ve DiscreteColorCycle (Storyboard.Target=yerel fırça) çocuk olarak; DesiredFrameRate=30 köke; renk turunun negatif BeginTime'ı çocukta korunur (faz aynı kalır → renk yine kırpmanın dibinde atlar). CursorHop.FrameRate(20) sabiti düşer. İki imleç (konsol + stream) iki kök kalır ama aynı hızda ve aynı anda başladıklarında (Loaded) fazları çakışır → pratikte ~30-35 uyanış/s. İsteğe bağlı: iki imleci de tek uygulama-düzeyi paylaşımlı clock'la sür (GraphView'ın ApplyAnimationClock deseni, 1136/1168) → kesin 30. MotionTokens'ın 'renk çizelgesinin tek kurucusu' guard'ı korunur (DiscreteColorCycle çağrısı aynı kalır).

**Çözüm (doğrulayıcı düzeltmesi):** Tek kok Storyboard onerisi mumkun ama risk 'dusuk' degil 'orta': CursorHop.Start/Stop/IsRunning bagimsiz yasam dongusu (EventStreamView'da imlec dinlenme/ton kanali) ve mevcut testler ayri saat varsayar. Once CompositionTarget.Rendering sayaciyla mevcut durumu yeniden olc; kazanc <%1 cekirdek ise dokunma. CursorHop.FrameRate=20 ikinci bir kare hizi sabiti — ARCHITECTURE.md:4996 'one shared constant' ile uyusmuyor: dokuman/kod uyusmazligi olarak kullaniciya sorulmali.

**Değişmezler:** Değişmezlere dokunmaz; §14.5 'DecorativeFrameRate=30 tek sabit' ilkesine uyar (bugün CursorHop 20 ile ikinci bir sayı taşıyor — kopya yasağıyla da uyumsuz).

**Test fikri:** Realize edilmiş ConsoleView+EventStreamView boşta 3 s: CompositionTarget.Rendering sayısı ≤ 3×35.

**Doğrulayıcı notu:** Konumlar dogru: MotionTokens.cs:41-51 blink 30 fps Forever; CursorHop.cs:31-39 StepMs/PhaseMs/FrameRate=20, 104 DiscreteColorCycle; ConsoleView.xaml.cs:573-584; EventStreamView.xaml.cs:416-424. '~55-60 kare/s' sayisi 2026-09-14 raporundan (.claude/outputs/2026-09-14-19-56-focused-performance-pass-report.md:110) — o gun olculmus, 2026-10-01 setinde YENIDEN olculmedi ve orada 'tasarim karari' diye birakilmis. '2x gereksiz uyanis'in CPU karsiligi turetilmis degil. Tepside-derleme senaryosuna etkisi yok (imlecler gizliyken zaten kapili).

#### D7-5 — Tray overlay: katmanlı pencere (AllowsTransparency) + 3 s döngü @30 fps + hareket eden öğeler üstünde iki DropShadowEffect — build boyunca sürekli tam-bitmap kompozisyon; maliyeti hiç ölçülmemiş

| Alan | Değer |
|---|---|
| Önem | olcum-bekliyor (onemli degil; aday) |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Views/TrayBuildOverlayWindow.xaml` satır 5-13 — WindowStyle=None, AllowsTransparency=True, Topmost — layered window: her karede tam yüzey readback + UpdateLayeredWindow
- `src/BuildOrchestrator.App/Views/TrayBuildOverlayWindow.xaml.cs` satır 37-39 — Scale=0.5 → 187.5×67 DIP
- `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml` satır 48-173 — TrayLoop 3 s: 17 keyframe animasyonu (7 TranslateTransform.X, 7 Opacity, 1 Clip Object, 2 kaplama)
- `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml` satır 212-217, 252-254 — Canvas.Effect DropShadowEffect BlurRadius 4.8 (şeritler, içindeki transformlar animate) ve Path.Effect BlurRadius 5.6 (şevron; kendi transform+opacity animate) — efekt girdisi her karede yeniden hesaplanır
- `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml.cs` satır 48, 120-153 — DesiredFrameRate=30; her Completed'da yeni tur (koşu bitene kadar) — 3-4 dk Resolve'da 60-80 tur
- `tests/BuildOrchestrator.Tests/App/TrayIndicatorFrameProbeTests.cs` satır 30-60 — BO_PROBE_TRAY sondası var ama yalnız geometri ölçer, CPU/kare maliyeti ölçmez

**Kanıt:** Layered window'da WPF her sunulan kareyi GPU'dan CPU'ya okuyup DWM'e kopyalar; hareket eden transform'lar yüzeyi her tick kirletir. Bitmap boyutu: 100% DPI 188×67 ≈ 12.6 k px (50 KB/kare, 1.5 MB/s); 150% 281×101 ≈ 28 k px (114 KB, 3.4 MB/s); 200% 375×134 ≈ 50 k px (200 KB, 6 MB/s). Efektler GPU shader'dır ama her kare için iki ara yüzey + blur pass. Duruş evresinde (1.34-2.10 s) animasyon değerleri sabit → WPF property değişmeyince dirty olmaz, o 0.76 s'de readback beklenmez (doğrulanmadı).

**Etki:** Tahmin: tek çekirdeğin %1-3'ü + küçük GPU yükü, build boyunca sürekli (Resolve'da 3-4 dk). Tek başına büyük değil ama 'tray'de küçükken' bütçesinin görünür kalemi; ölçüm olmadan kesin söylenemez. RenderCapability.Tier 0 ise (RDP, yazılım) efektler CPU'ya düşer ve maliyet kat kat artar (bkz. D7-9).

**Etki (doğrulayıcı düzeltmesi):** Bilinmiyor. Ayristirmak icin gereken olcum: ayni kosu tepside gosterge ShowLoop vs ShowStatic (reduced-motion acik) 10 s pencere farki.

**Çözüm:** Önce ÖLÇ (yapıyı bozmaz): TrayIndicatorFrameProbeTests yanına ikinci sonda — gerçek TrayBuildOverlayWindow ShowLoop 20 s vs ShowStatic 20 s, Process.TotalProcessorTime deltası + CompositionTarget.Rendering sayısı; BO_PROBE_TRAY kapısıyla. Sonuca göre üç kademeli, sanatı bozmayan seçenekler: (a) iki DropShadowEffect'e `RenderingBias="Performance"` (tek satır, görsel fark gölge kalitesinde alt-piksel). (b) Şerit gölgesi katmanına `CacheMode=BitmapCache` DEĞİL (içi hareket ediyor) — ama şevron için Effect'i transform'un DIŞINA taşımak (Path'i saran Canvas'a Effect, Path'e transform) efekt girdisini sabitler; WPF efekt girdisini yalnız içerik değişince yeniden çizer, transform dışarıda kalınca blur önbelleklenir. (c) [ŞARTLI — verbatim sanat kuralı §14.5 kural 3] Gölgeyi Effect yerine 2 px offset'li ikinci koyu Path ile çizmek: blur kaybolur, tasarımcı onayı gerekir. (d) Ürün kararı: overlay'i tüm koşu boyunca değil ilk N tur döndürüp duruş karesinde bırakmak (ShowStatic yolu zaten var, controller'a 'loop bütçesi' eklemek küçük) — kullanıcıya sorulmalı.

**Çözüm (doğrulayıcı düzeltmesi):** 'Once olc' dogru. Secenek (b) YANLIS: Effect'i saran Canvas'a tasimak blur'u onbelleklemez — efektin girdisi (icindeki hareketli Path) her karede degistigi icin ara yuzey yine her kare yeniden uretilir. Secenek (a) RenderingBias=Performance yalniz yazilim render yolunda fark eder. Secenek (c) §14.5'teki teslim edilen sanat kuraliyla catisir (kullanici onayi). Secenek (d) urun karari. Olcum farki kucukse hicbirini uygulama.

**Değişmezler:** (a)(b)(d) yapıyı korur; (c) §14.5 kural 3 'delivered timeline / verbatim artwork' istisnasına dokunur → şartlı.

**Test fikri:** [SkippableFact] BO_PROBE_TRAY=1: ShowLoop vs ShowStatic 20 s CPU-time ve render sayısı çıktısı (iddia değil sonda).

**Doğrulayıcı notu:** Konumlar dogru: TrayBuildOverlayWindow.xaml:5-13 (AllowsTransparency=True, Topmost), .xaml.cs Scale=0.5, TrayBuildIndicator.xaml:213-217 ve 252-254 iki DropShadowEffect, .xaml.cs:48 DesiredFrameRate=30, 142-147 gorunmezken tur acmaz. Sayim hatasi: TrayLoop'ta 17 degil 15 keyframe animasyonu var (8 TranslateTransform.X, 6 Opacity, 1 Clip). Maliyet iddiasi (%1-3) tamamen tahmin; bulgu da bunu kabul ediyor (confidence 0.55). Olcumde yalniz toplam var: tepside Rebuild + gosterge acik App 1,44 Gcycles/s; gostergenin payi ayristirilmadi.

#### D7-6 — Konsol tilt geçişi her proje-logu açılışında/Back'te tam DPI çözünürlükte RenderTargetBitmap (yazılım rasterizer, UI thread'de senkron) + Viewport3D — 'UI akışı seri mi' sorusunda tıklama başına takılma adayı

| Alan | Değer |
|---|---|
| Önem | kozmetik (olculene kadar) |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 714-746 — PlayTiltIn: BuildTiltScene → 340 ms Viewport3D animasyonu; tetik 613 (Back) ve 689 (proje logu)
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 750-760 — BuildTiltScene: `new RenderTargetBitmap(ceil(w*dpiX), ceil(h*dpiY), 96*dpi, Pbgra32)`; `texture.Render(PART_TiltHost)` — AvalonEdit tam metin yüzeyi yazılımla rasterize
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml` satır 55 — PART_Tilt3D Viewport3D — geçiş süresince 3B kompozisyon

**Kanıt:** RenderTargetBitmap.Render WPF'te her zaman yazılım rasterizer'ıyla, çağıran thread'de senkron çalışır. Focus modunda konsol ~76% sütun: 1200×700 DIP panel 150% DPI'da 1800×1050×4 B ≈ 7.5 MB doku; AvalonEdit'in görünür satırlarının glyph çizimi tahminen 20-80 ms UI-thread blokajı (ölçülmedi). Doku Land()'da bırakılıyor (iyi), ama her tıklamada yeniden üretiliyor. Doküman (satır 725 yorumu) dokunun geçici olduğunu ve sonunda 'metin yeniden keskin' olduğunu söylüyor — yani tam netlik gerekmiyor.

**Etki:** Proje satırına her tıklamada bir kare takılma adayı (tahmin 20-80 ms, 50 ms UI bütçesi sınırında). Kullanıcının 'UI akışı seri mi' sorusunun somut bir kalemi.

**Etki (doğrulayıcı düzeltmesi):** Tiklama basina tek seferlik UI-thread blokaji; sure bilinmiyor. Doku boyutu hesabi (w*dpi x h*dpi x 4 B) koddan dogru.

**Çözüm:** (1) Önce ölç: BuildTiltScene etrafına Stopwatch (DiagnosticsReport'a ya da test sondasına). (2) Dokuyu DPI ölçeğinde değil sabit 96 dpi (ya da 0.5×) al: 340 ms boyunca hareket eden, sonunda gerçek editörle değiştirilen bir görüntü için tam çözünürlük gereksiz — RTB piksel sayısı 150%'de 2.25×, 200%'de 4× düşer; 3B düzlem dokuyu zaten ölçekler. (3) İsteğe bağlı: ilk dokuyu RTB yerine editörün VisualBrush'ıyla değil (doküman gerekçesi geçerli) — ama `PART_TiltHost`'un CacheMode=BitmapCache ile GPU'da tutulmuş karesini kullanmak; daha invaziv, önce (2).

**Çözüm (doğrulayıcı düzeltmesi):** Once BuildTiltScene'i Stopwatch ile olc (buyuk log + %150 DPI). 50 ms'yi asiyorsa dokuyu 96 dpi'da al; bu 340 ms boyunca metni bulanik gosterir — gorsel karar kullaniciya sorulmali. BitmapCache secenegi invaziv, onerilmez.

**Değişmezler:** Değişmezlere dokunmaz; tasarım (§13.5 tilt) korunur, yalnız doku çözünürlüğü.

**Test fikri:** UiResponsivenessBudget kalıbıyla: realize edilmiş konsolda 2000 satırlık log + proje logu açma → BuildTiltScene süresi ≤ bütçe (önce ölçüp sonra pin).

**Doğrulayıcı notu:** Konum dogru: ConsoleView.xaml.cs:714-746 PlayTiltIn, 755-768 BuildTiltScene tam DPI'da RenderTargetBitmap + texture.Render(PART_TiltHost) UI thread'inde senkron. '20-80 ms' suresi olculmedi (bulgu kendisi soyluyor). Yalniz kullanici tiklamasinda (proje logu / Back) calisir; tepside-derleme senaryosunda yok; reduced-motion'da hic oynamaz (729).

#### D7-7 — Doküman ile kod uyuşmuyor: §14.5 'only transform and opacity are animated, never layout' — ama şerit Width (ProjectRow) ve ilerleme çubuğu Width (StickyRibbon) animate ediliyor (layout pass her karede)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` satır 739-741, 748-766 — AnimateStripeWidth → AnimateDouble(PART_Stripe, FrameworkElement.WidthProperty, …) — seçim değişiminde 120 ms Width animasyonu (Grid kolonu Auto → satır Measure/Arrange her karede)
- `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs` satır 374-387 — AnimateIndicatorWidth: PART_ProgressIndicator.BeginAnimation(WidthProperty, …) Duration.Base — her statü değişiminde (koşarken saniyede birkaç kez) 180 ms layout animasyonu
- `ARCHITECTURE.md` satır 4827-4829 — 'only transform and opacity are animated, never layout' iddiası

**Kanıt:** WidthProperty AffectsMeasure'dur: animasyonun her karesi (30-60 fps) şeridin/satırın Measure+Arrange'ını tetikler. Şeritte Grid.Column 0 Auto olduğu için satırın tüm kolonları yeniden ölçülür; şeritte ise yalnız Border. Doküman bunu yasaklıyor; kod iki yerde yapıyor. Karar kullanıcının: doküman mı kod mu.

**Etki:** Küçük: satır seçiminde 120 ms × ~7 kare layout; şeritte koşarken her statü değişiminde 180 ms × ~11 kare. Boşta sıfır. Ama doküman iddiası yanlış ve gelecekte yeni layout animasyonlarına kapı açık.

**Etki (doğrulayıcı düzeltmesi):** Serit: secim degisiminde 80 ms layout animasyonu; cubuk: her ilerleme guncellemesinde Duration.Base (180 ms) Width animasyonu — pencere tepsideyken de kurulur (kapi yalniz motion; D7-1 askisi bunu da kapatir). Sure/CPU olculmedi.

**Çözüm:** Karar sorulur. Kod tarafını dokümana uydurma yolu ucuz: (a) şerit: Width yerine `ScaleTransform.ScaleX` (2→3 px, RenderTransformOrigin sol) — Grid kolonu 3 px sabit; (b) ilerleme çubuğu: Width'i anında yaz, görsel geçişi `ScaleTransform.ScaleX` (fraction) ile — track genişliği zaten biliniyor (359). Ya da dokümanı 'iki ölçülmüş istisna dışında' diye düzelt.

**Çözüm (doğrulayıcı düzeltmesi):** Kullaniciya sor: dokuman mi kod mu. Kod dokumana uydurulacaksa ScaleTransform onerisi gecerli; serit icin 2->3 px olcekleme alt-piksel bulanikligi uretebilir (SnapsToDevicePixels) — gorsel dogrulama gerekir.

**Değişmezler:** Değişmezlere dokunmaz; §14.5 metni ya da iki animasyon değişir.

**Test fikri:** NoHardcodedMotionTests yanına 'AffectsMeasure DP animate edilmez' guard'ı (BeginAnimation(WidthProperty/HeightProperty/Margin…) grep) — istisnalar açıkça listelenir.

**Doğrulayıcı notu:** Dokuman/kod uyusmazligi gercek: ARCHITECTURE.md:4828-4829 'only transform and opacity are animated, never layout'; kod ProjectRow.xaml.cs:739-741 PART_Stripe WidthProperty'yi, StickyRibbon.xaml.cs:374-387 PART_ProgressIndicator WidthProperty'yi animate ediyor. Serit Grid kolonu Auto (ProjectRow.xaml:29, 43) — olcum zinciri satira yayilir. Duzeltme: serit animasyonu Duration.Instant = 80 ms (bulgudaki 120 ms yanlis; 120 ms olan InnerTranslate'tir, o transform). CLAUDE.md geregi sessizce secilmez, kullaniciya sorulur.

#### D7-8 — DropShadowEffect'ler ana pencerede: LatestPill (BlurRadius 18) canlı akan konsolun üstünde durur, Popup (BlurRadius 28) — RenderingBias varsayılan Quality, önbellek yok

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Resources/Tokens.xaml` satır 229-231 — Effect.OverlayShadow ShadowDepth 10 BlurRadius 28; Effect.PopoverShadow ShadowDepth 6 BlurRadius 18 — RenderingBias belirtilmemiş (Quality)
- `src/BuildOrchestrator.App/Controls/LatestPill.xaml` satır 24 — Pill Effect=PopoverShadow — konsol yukarı kaydırılmışken canlı append sırasında görünür; içeriği statik
- `src/BuildOrchestrator.App/Resources/Controls.xaml` satır 1061-1067 — PART_Popup içeriği Effect=OverlayShadow — geçici, açıkken sabit

**Kanıt:** Efekt ara yüzeyi, efektli alt ağaç ya da onunla kesişen dirty bölge yeniden çizildiğinde yeniden hesaplanır. LatestPill koşuda konsolun üstünde durur; konsol 50 ms batch'lerle metin ekler (~20 dirty/s) ve pill'in bölgesiyle kesişebilir → blur pass tekrarı. Pill içeriği statiktir → BitmapCache ile tek sefer rasterize edilebilir. Popup geçicidir, açıkken dirty yok — maliyet yalnız açılışta.

**Etki:** Küçük (tahmin <%1 tek çekirdek, yalnız pill görünürken ve koşarken). Efekt sayısı azdır (4 kullanım), genel olarak temiz.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi; tepside-derleme senaryosunda sifir.

**Çözüm:** LatestPill köküne `CacheMode="BitmapCache"` (içerik değişince WPF önbelleği kendisi yeniler); iki efekte `RenderingBias="Performance"` (blur kalitesi gözle fark edilmez düzeyde düşer, ölçülü karar). Tokens.xaml tek yer olduğu için iki satır.

**Çözüm (doğrulayıcı düzeltmesi):** Olcum olmadan dokunma; yapilacaksa yalniz LatestPill'e BitmapCache. RenderingBias degisikligi donanim hizlandirmali yolda fark uretmez.

**Değişmezler:** Değişmezlere dokunmaz; tasarım gölge değerleri aynı.

**Test fikri:** Gerek yok (kozmetik); isteğe bağlı: pill görünürken 5 s CompositionTarget.Rendering sayısı önce/sonra.

**Doğrulayıcı notu:** Konumlar dogru: Tokens.xaml:229-231 iki DropShadowEffect (RenderingBias yok), LatestPill.xaml:24, Controls.xaml:1061-1067. App'te Effect kullanimi yalniz 4 yer (grep). Maliyet iddiasi tahmin ve kucuk; pill yalniz konsol yukari kaydirilmisken gorunur, tepside hic.

#### D7-9 — RenderCapability.Tier hiç okunmuyor/loglanmıyor: yazılım render (Tier 0 — RDP, bazı sanal GPU'lar) durumunda tüm opacity/efekt maliyeti CPU'ya düşer ve tanı raporunda görünmez

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.App/Services/DiagnosticsReport.cs (satir 1 degil, rapor govdesine yeni satir)
- `src/BuildOrchestrator.App/Services/DiagnosticsReport.cs` satır 1 — Tanı raporu var, render tier alanı yok (grep: App'te RenderCapability hiç geçmiyor)

**Kanıt:** grep 'RenderCapability' src/BuildOrchestrator.App → 0 sonuç. Tier 0'da WPF tüm sahneyi CPU'da rasterize eder; D7-5'teki DropShadowEffect'ler ve 187 düğümün opacity katmanları CPU maliyetine döner. Kullanıcı 'bilgisayarı yoruyor mu' diye sorduğunda tier bilinmeden cevap verilemez.

**Etki:** Tanı eksikliği; normal masaüstünde (Tier 2) etkisi yok.

**Çözüm:** DiagnosticsReport'a `RenderCapability.Tier >> 16` ve `RenderOptions.ProcessRenderMode` satırı; Tier 0 ise açılışta stderr'e tek satır uyarı. İsteğe bağlı: Tier 0'da App.Motion Decorative'i (D7-1) otomatik askıya al — dekoratif animasyon yazılım render'da anlamsız.

**Çözüm (doğrulayıcı düzeltmesi):** Yalniz tani satiri ekle (RenderCapability.Tier >> 16). 'Tier 0'da dekoratif animasyonu otomatik kapat' bir davranis degisikligidir — kullanici karari, tani ile birlikte paketlenmemeli.

**Değişmezler:** Değişmez yok.

**Test fikri:** DiagnosticsReport çıktısında 'render tier' satırı var (string guard).

**Doğrulayıcı notu:** grep: src/BuildOrchestrator.App altinda RenderCapability / ProcessRenderMode / RenderMode hic gecmiyor. Services/DiagnosticsReport.cs mevcut. Tani eksigi gercek; zayif makinelerde (eski iGPU/surucu, RDP) Tier 0/1 ihtimali bu kullanici kitlesi icin anlamli — arkadaslarin makinesinde yazilim render varsa D7-1/D7-5 maliyetleri katlanir ve bugun bunu gormenin yolu yok.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.App/MainWindow.xaml.cs:66-67 + 323-326, Services/OperationChoreographer.cs:89-109, Controls/MarkingChoreography.cs:51-65 ve TotalMs: pencere TEPSIDEYKEN baslatilan Build'de kosu komutu acilis koreografisi bitene kadar bekler — kapi yalniz `App.Motion.AnimationsEnabled`, pencere gorunurlugu yok; sure 2060 + W ms, 152 projede stagger=min(110, round(1100/151))=7 -> W=7*151+380=1437 -> ~3,5 s gorunmeyen animasyon icin gecikme (koddan turetildi, olculmedi). Birincil senaryonun en somut kalemi.
- src/BuildOrchestrator.App/MainWindow.xaml.cs:70-71 (StepHold) ayni dogrudan okuma: Clean/adim arasi bekletmeler de gizli pencerede surer; ayrica bu iki satir MotionGate.cs:28-31'in 'statik sinyalin TEK okuma ifadesi' kuralinin kopyasidir (kopya yasagi).
- src/BuildOrchestrator.App/Graph/GraphView.xaml.cs:1399 ve 1426-1438 (EnsureEdgeFlowClock): bir proje seciliyken Forever 30 fps saat kurulur; IsVisible kapisi yok ve ReapplyMotion (338-343) bu saati ne soker ne kurar. Secim bostayken de durdugu icin saat bosta ve tepside doner — tepside bosta olculen 50-54 Mcycles/s'nin (onceki 4,6 M) ADAYI; dogrulamak icin secimli/secimsiz tepside 10 s olcumu gerekir (tahmin). Bulgu ajani bunu 'temiz alan' saymis.
- src/BuildOrchestrator.App/MainWindow.xaml.cs:851-856 (RowsById): PushGraphStatuses her 200 ms tick'te 187 girisli yeni Dictionary kurar — GraphBinder tahsislerine ek, gorunurken de odenir.
- src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs:359 + 374-387: ilerleme her degistiginde gizli pencerede de 180 ms Width (layout) animasyonu kurulur; kapi yalniz motion sinyali.
- Kosu sonu finale gizli pencerede oynuyor: olcumde G1-tray-post-run-idle 81,7 Mcycles/s, sonraki pencere 54,4 M (measure-release-2026-10-01.log:155-159) — ilk 10 s'de ~27 M/s fazla; GraphView _endPlayer (GraphView.xaml.cs:185, 549) gorunurluk kapisi tasimiyor. Kucuk ama olculmus.

**Temiz bulunan alanlar:**
- Tray göstergesi (TrayBuildIndicator.xaml.cs:54, 99-118, 142-147): IsVisibleChanged → StopNow, tur sonsuz değil, Teardown gerçekten Remove() ile saati söküyor, görünmezken yeni tur açmıyor; overlay HideNow (TrayBuildOverlayWindow.xaml.cs:103-109) hem gizler hem söker; controller (TrayBuildIndicatorController.cs) saf ve pencere geri gelince ANINDA gizliyor. Reduced-motion'da döngü hiç kurulmuyor (ShowStatic).
- BuildingSpinner (BuildingSpinner.cs:85, 127): IsVisible && motion kapısı, Unloaded'da Stop, dönen saat yeniden başlatılmıyor; StatusGlyph artık kendi nabzını taşımıyor (IdleClockTests ile pinli).
- Konsol ve event-stream imleçleri (ConsoleView.xaml.cs:577, EventStreamView.xaml.cs:419, 100-111): kapı başlatan metodun içinde (çağıranlarda değil), Unloaded'da blink+rest saatleri sökülüyor; HiddenCursorClockTests ile pinli.
- GraphView paylaşımlı saatler: beads (1151-1169) ve edge-flow (1426-1447) düğüm/çizgi başına değil TEK AnimationClock (ApplyAnimationClock), 30 fps; unload'da ReleaseBeadsClock/ReleaseEdgeFlowClock (323-331); spin-down 700 ms sonra saat bırakılıyor (1190-1196); reduced-motion'da ReapplyMotion saati söküyor (341). ApplyStatuses (811-830) record eşitliğiyle değişmeyen düğüme dokunmuyor. Kamera/opaklık animasyonları FillBehavior ile asılı saat bırakmıyor (shake FillBehavior.Stop, 728).
- MotionGate (MotionGate.cs): subscribe-once (-= sonra +=) ve Unloaded'da unsubscribe — sızıntı yok; tek statik okuma noktası (31) sayesinde D7-1'in tek kapısı ucuz.
- Zamanlayıcı disiplini: StepPlayer tek DispatcherTimer (StepPlayer.cs:21, Stop'ta temiz), StepHold tek timer, GraphView _dimFirst ve BottomAnchor/FollowScroll/RevealStagger tek atımlık timer'ları kendi tick'inde Stop ediyor (GraphView 716-719); ConsoleBatcher boşta uyuyor (§14.5, ölçülmüş); 200 ms tick boşta yalnız değişince yazıyor (SetLineCount) ve grafı boşta itmiyor (IsRunUnderway kapısı).
- CompositionTarget.Rendering abonesi hiç yok; VisualBrush/OpacityMask/BlurEffect kullanımı yok; Effect yalnız 4 yerde (tray 2, pill 1, popup 1); DesiredFrameRate=30 tek sabitten (MotionTokens.DecorativeFrameRate) — CursorHop'un 20'si dışında (D7-4).
- Reduced-motion: OS sinyali canlı (SystemParametersMotionSignal, StaticPropertyChanged filtreli), Duration.* toplu sıfırlama/geri yükleme (MotionSettings.Attach/Apply), her sahip başlatma anında taze okuyor; ReducedMotionCoverageTests ile pinli. Koreografiler reduced-motion'da hiç oynamıyor.
- Renk geçişleri bilinçli olarak yalnız işaretleme dalgasında açık (GraphView.ApplyNodeStatus 1045-1059: 177 düğüm × 3 yüzey ölçümü 11→51 ms) — dalga dışı anında; bu sınır doğru ve korunmalı.
- Konsol hover bandı (§13.5 son paragraf) saat açmıyor, yalnız gerçek fare/scroll olayında yeniden hesaplıyor.

**Açık sorular:**
- Pencereyi arka plana nasıl alıyorsun: × ile tray'e mi (Hide), görev çubuğuna minimize mi, yoksa başka pencerenin arkasında bırakıyor musun? (D7-1 tray'i, D7-2 minimize'ı kapatır; arkada bırakma için WPF'te kapı yok — 'görünür' sayılır.)
- Ekran ölçeği (DPI) kaç — 100/150/200%? Tray overlay bitmap'i (D7-5) ve konsol tilt dokusu (D7-6) bununla 2-4× büyüyor.
- Build sırasında tray'deki logo animasyonunun tüm koşu boyunca (3-4 dk) dönmesi istenen davranış mı, yoksa ilk birkaç turdan sonra duruş karesinde kalması kabul edilebilir mi? (D7-5 seçenek d — ürün kararı.)
- D7-5 ve D7-6 için BO_PROBE_TRAY / ölçüm sondası koşturmama izin var mı (uygulama kapalıyken, tepsi ikonu ve overlay penceresi açar)?
- §14.5 'never layout' ile şerit/ilerleme çubuğu Width animasyonları (D7-7): doküman mı doğru, kod mu? Hangisine uydurayım?
- 2026-09-14'teki 'gizlemeden sonra ~8 sn render devam ediyor' gözlemi hâlâ var mı? Adaylar: D7-3'teki 200 ms tick beslemesi (koşu sürüyorsa), hold-fade HoldMs+FadeMs (2.1 s) ve neon finale (~3.6 s) HoldEnd'e gelene dek, tray overlay çıkış evresi + Duration.Slow nefesi; D7-1/D7-3 uygulanınca yeniden ölçülmeli.
- Build'i tray'deyken Ctrl+Shift+Space ile başlatıyor musun? Evetse D7-1'in yan etkisi (gizliyken koreografi atlanır, komut hemen gider) istenen davranış mı?


### D1-bosta-cpu

#### D1-1 — Graf seçim kenarlarının akan-kesik saati (edge-flow) seçim durdukça sonsuza dek döner — boşta ve tepsideyken de

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 1385-1400 — RebuildSelectionEdges: seçim varsa ve motion açıksa EnsureEdgeFlowClock() — pencere görünürlüğüne bakılmaz
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 1426-1439 — EnsureEdgeFlowClock: RepeatBehavior.Forever, 640 ms tur, DesiredFrameRate 30, CreateClock + ApplyAnimationClock
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 654, 731-746 — IsPanelVisible yalnız panelin kendi Visibility'sine bakar; IsVisible/IsVisibleChanged bilerek kullanılmaz (headless gerekçesi)
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 323-331 — Saat yalnız Unloaded'da bırakılır — Hide() unload etmez (§14.5)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1331, 1244-1245 — MinimizeToTray → Hide(); IsVisibleChanged yalnız tepsi göstergesi controller'ına gider, grafa gitmez

**Kanıt:** Kullanıcı bir projeye tıklayıp seçili bırakınca (log okumak için olağan akış) seçimin bağımlılık kenarları kurulur ve StrokeDashOffset'i süren Forever saat başlar. Saat yalnız seçim değişince/Unloaded'da bırakılır. Pencere tepsiye inince (Hide) GraphView'ın Visibility'si değişmez, IsPanelVisible true kalır, seçim de korunur → saat gizli pencerede dönmeye devam eder. ARCHITECTURE §14.5: 'WPF's timing engine keeps the whole render loop awake while any clock is active … an idle application was measured burning 133 % of a core'. Bu sahip aynı bölümde tanımlanan 'her sonsuz animasyon IsVisible'a kapılıdır' kuralının dışında (2026-09-14 raporu §4 tablosu da 'döner' diye işaretlemiş ama 'yalnız işlem sürerken' saymıştı — seçim işlem değildir, boşta da durur).

**Etki:** Seçili bir düğüm varken uygulama boşta bile tam render döngüsü (30 fps hedef, gerçekte dispatcher + render thread uyanık) çalışır; tepsiye indirildiğinde de sürer. Ölçülmedi; §14.5'teki tek-Forever ölçümü (133 % çekirdek, 60 fps blink) üst sınır, 30 fps ve birkaç Path için tahmin ~%3-10 tek çekirdek. 2026-09-14'teki '~5 M döngü/sn tepsi boşta' ölçümü seçimsiz alınmıştır — seçimli boşta ölçüm yok.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. '~%3-10 tek cekirdek' rakami dayanaksiz tahmin. Elde olan tek olculmus kiyas: dort imlec saati (2x30 fps + 2x20 fps) on planda ~80-85 Mcycles/s ediyor (131-139 eksi tepsi 50-54); tek 30 fps saatin maliyeti bunun altinda/ayni mertebede beklenir (tahmin). Kosul: kullanici bir dugumu secili birakmis olmali; finale sonrasi EffectiveSelection null oldugu icin kenar kurulmaz (1380-1383 doc).

**Çözüm:** (1) Tek 'host görünür' sinyali: MainWindow, Shell.GraphHost.AnimationsEnabledProvider'ını `() => MotionGate.StaticAnimationsEnabled && IsVisible` olarak kurar ve IsVisibleChanged'de `GraphHost.ReapplyMotion()` çağırır; ReapplyMotion'a `if (!AnimationsEnabledProvider()) ReleaseEdgeFlowClock(); else if (_selectionEdges.Count>0 && _edgeFlowClock is null) EnsureEdgeFlowClock();` eklenir. Headless testleri bozmaz: provider zaten enjekte edilen bir seam. (2) Ön planda boşta için tasarım kararı: kenar akışı yalnız pencere aktifken (Window.IsActive) dönsün — Deactivated'da bırak, Activated'da kur (Win32 caret geleneği ile aynı mantık). Hiçbir değişmezi etkilemez.

**Çözüm (doğrulayıcı düzeltmesi):** Fix (1) gecerli; ek dikkat: provider'a `&& IsVisible` eklemek GraphView'in TEK-ATIMLIK animasyonlarini da (finale 539, dimFirst 710, opaklik 1558) gizliyken anlik yapar — istenen de bu, ama geri gelis yolunda IsVisibleChanged(true) -> ReapplyMotion cagrisinin edge-flow'u yeniden kurmasi (`_selectionEdges.Count>0 && _edgeFlowClock is null`) pinlenmeli. Fix (2) (Window.IsActive kapisi) tasarim karari — kullaniciya sorulmadan yapilmaz.

**Değişmezler:** Planlama Core'da kalır; UI-only. §14.5 'IsVisible kapısı' kuralı graf sahipleri için de uygulanmış olur; GraphView'ın 'IsVisible kullanılmaz' gerekçesi (731-735) provider seam'i ile korunur (view kendi IsVisible'ını okumaz, sinyal dışarıdan gelir).

**Test fikri:** GraphView realize testi: düğüm seç → `_edgeFlowClock` dolu; AnimationsEnabledProvider false döndürüp ReapplyMotion() → saat null (mevcut HiddenCursorClockTests deseni). MainWindow testi: Hide() sonrası GraphHost'un provider'ı false döner.

**Doğrulayıcı notu:** Kod iddiayi dogruluyor: GraphView.xaml.cs:1399 EnsureEdgeFlowClock yalniz `_selectionEdges.Count > 0 && AnimationsEnabledProvider()` kosuluna bagli; saat Forever + DecorativeFrameRate (1428-1439); birakildigi yerler yalniz RebuildSelectionEdges basi (1389) ve Unloaded (323-331). IsPanelVisible = kendi Visibility'si (654), MainWindow.IsVisibleChanged yalniz tepsi gostergesine gidiyor (1244). ReapplyMotion (336-342) edge-flow'a hic dokunmuyor. ANCAK etki sayisi olculmedi ve 2026-10-01 olcumundeki tepsi-bosta 50-54 Mcycles/s bu bulguyla ACIKLANAMAZ: measure.ps1 yalniz klavye gonderiyor, olcumde secili dugum yoktu. Ayrica ARCHITECTURE §14.5 (satir 5002-5007) 'every infinite animation is gated on IsVisible' diyor, kod bu sahipte oyle degil -> dokuman/kod uyusmazligi, kullaniciya bildirilmeli.

#### D1-2 — Pencere görünürken boşta iki imleç dört saat döndürüyor: render döngüsü ~55-60 fps, ~146 M CPU döngüsü/sn — pencere arkadayken de

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 572-583 — StartBlink: IsVisible kapısı var, IsActive kapısı yok; CreateBlinkAnimation + CursorHop.Start
- `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs` satır 415-424 — StartCursorBlink: aynı iki saat (blink + CursorHop) ikinci imleç için
- `src/BuildOrchestrator.App/Controls/MotionTokens.cs` satır 40-50 — CreateBlinkAnimation: 550 ms AutoReverse Forever, DesiredFrameRate 30 — her çağrı yeni, faz-bağımsız bir saat
- `src/BuildOrchestrator.App/Controls/CursorHop.cs` satır 31-39, 104 — DiscreteColorCycle: StepMs 1100, FrameRate 20, Forever — imleç başına ikinci bir sonsuz saat
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 350 — Activated aboneliği var (auto-sync için), Deactivated aboneliği yok — imleçler pencere arkadayken de yanar

**Kanıt:** 2026-09-14 raporu §5: 'Görünür boşta render döngüsü ~55-60 kare/sn. İki imlecin kırpma ve renk turu fazları farklı olduğu için birleşik kare hızı 30'un üstüne çıkıyor' ve §1: 'Pencere açıkken boştaki maliyet ~205 → ~146 M döngü/sn' (tepsi: ~5 M). Yani görünür boşta maliyetin ~%96'sı imleçlerden; pencere başka bir pencerenin ARKASINDA olsa da IsVisible true olduğu için aynı maliyet sürer. Kod: dört ayrı saat (2× blink 30 fps + 2× renk turu 20 fps), her biri ayrı CreateClock; WPF birleşik tick'i en yoğun kesişime çeker.

**Etki:** Ölçülmüş: görünür boşta ~146 M döngü/sn ≈ tepsinin ~30 katı. Kullanıcının 'ön planda / arka planda' sorusunun cevabı: ön planda ve arkada aynı, tepsiye inince düşer. Laptop'ta (Core Ultra 7 258V) boşta sürekli uyanık render thread = pil/fan etkisi.

**Etki (doğrulayıcı düzeltmesi):** On planda bosta 131-139 Mcycles/s (tek cekirdegin %3,4-5,2'si), kosu sonrasi on planda 163 M/s; tepside 50-54 M/s. Imlec saatlerinin payi ~80-85 M/s. IsActive kapisi arkadaki pencereyi en fazla tepsi seviyesine (50-54 M) indirir, ~5 M'e DEGIL. Paylasimli saatle '%40-50 azalma' tahmindir, olculmedi.

**Çözüm:** İki bağımsız adım, ikisi de tasarımı korur: (a) IsActive kapısı — Win32/WPF geleneğinde caret pencere odağını kaybedince yanıp sönmez; MainWindow Deactivated/Activated'da ConsoleView.RefreshPrompt() ve EventStreamView'ın StartCursorBlink yolunu tetikleyen bir `HostActive` sinyali (MotionGate'e statik `StaticHostActive` gibi tek yer) ekle; StartBlink/StartCursorBlink kapısı `if (!IsVisible || !MotionGate.StaticHostActive) { Stop…; return; }`. Arkadayken maliyet tepsi seviyesine iner (~5 M). (b) Ön plandayken: dört saati TEK paylaşımlı saate indir — MotionTokens'ta `BlinkClock` (CreateBlinkAnimation().CreateClock()) ve CursorHop'ta tek renk-turu saati; iki imleç ApplyAnimationClock ile aynı saate bağlanır (GraphView beads deseni, 1151-1170). Faz kilitlenir → birleşik kare hızı 30'a düşer (tahmini ~%40-50 azalma). Reduced-motion ve IsVisible kapıları aynen kalır.

**Çözüm (doğrulayıcı düzeltmesi):** (a) IsActive kapisi tasarim karari: kullanici onayi gerekir (imlec pencere odakta degilken sabit durur). Sinyal MotionGate'e statik degisken olarak degil, mevcut provider seam'i uzerinden verilmeli (StaticAnimationsEnabled'a eklenirse MainWindow 1239-1240'taki tepsi gostergesi de kapanir). (b) Paylasimli saat: once olc — iki imlecin dort saati tek blink + tek renk saatine inince birlesik kare hizinin gercekten dustugu kanitlanmadan yapilmamali.

**Değişmezler:** Kopya yasağı: paylaşımlı saat MotionTokens/CursorHop'ta tek yerde. §14.5 'kapı saati başlatan metodun içindedir' kuralı korunur. Tasarım kararı (imleç yanıp söner) değişmez; yalnız aktif olmayan pencerede durur — bu, önceki raporun 'tasarım kararı' diye açık bıraktığı nokta; kullanıcıya sorulmalı (open_questions).

**Test fikri:** HiddenCursorClockTests desenine ek: host inactive sinyali ile StartBlink → ActiveCursor.HasAnimatedProperties false. Paylaşımlı saat için: iki view'da imleçlerin aynı Clock örneğine bağlandığını pinle (ReferenceEquals).

**Doğrulayıcı notu:** Konumlar dogru: ConsoleView.StartBlink 572-583 (kapi yalniz IsVisible, 577), EventStreamView.StartCursorBlink 415-424 (419), MotionTokens.CreateBlinkAnimation 42-52, CursorHop StepMs/FrameRate 20 (31-39), MainWindow.Activated 350, Deactivated/StateChanged aboneligi yok (grep). 2026-10-01 olcumu on planda bosta 131-139 Mcycles/s veriyor (eski 146 ile tutarli). ANCAK bulgunun iki sayisi bayat: (a) 'tepsi ~5 M' artik gecerli degil, olculen 50-54 M; (b) 'gorunur bosta maliyetin ~%96'si imleclerden' yanlis — imlece atfedilebilir fark ~80-85 M/s, yani ~%60. 'Pencere arkadayken ayni maliyet' iddiasi koddan dogru (IsVisible true kalir) ama AYRICA olculmedi.

#### D1-3 — Tepsideyken build sürerken gizli pencere tam hızda animasyon ve 200 ms yüzey tazeleme çalıştırıyor (beads, satır nefesi, şerit süpürmesi, graf statü itişi, bitiş finali)

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 1151-1170 — EnsureBeadsClock: building düğümler için Forever 4000 ms saat, panel Visibility'ye kapılı, pencereye değil
- `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` satır 621-633, 179-183 — ApplyBreathing: IsCompiling && motion → Forever 3.8 s nefes; IsVisible yok
- `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs` satır 391-415 — ApplyIndeterminate: Syncing'de Forever 1.4 s sweep; IsVisible yok
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 352-364 — 200 ms tick: IsRunUnderway ise PushGraphStatuses() (187 düğüm dictionary + GraphBinder) + FollowFrontier() — pencere gizliyken de
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 939-941 — Phase Done/Stopped → PlayEndFinale — görünürlük kapısı yok
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 535-546 — PlayEndFinale yalnız AnimationsEnabledProvider'a bakar

**Kanıt:** Hide() WPF'te IsVisible'ı alt ağaca yayar ama Visibility özelliğini değiştirmez; GraphView 654'te IsPanelVisible = Visibility==Visible, ProjectRow/StickyRibbon hiç görünürlük sormaz. Tick'te `if (_vm.IsRunUnderway) { PushGraphStatuses(); FollowFrontier(); }` görünürlükten bağımsız. 2026-09-14 raporu §4 bu üç sahibi 'tray'de döner' diye listeleyip 'MSBuild yükü yanında küçük' diyerek bırakmış; kullanıcının bu turdaki isteği ise tepside 'en düşük seviye'.

**Etki:** Build sırasında tepside: 30 fps beads (aktif düğüm sayısı kadar Rectangle, 4 paralelde 4-17), gerçekleşmiş building satırlarında nefes, 5×/s 187 düğümlük statü itişi + frontier hesabı, koşu bitince ~6 s finale — hepsi kimsenin görmediği pencerede. Ölçülmedi; tahmin MSBuild'in yanında tek çekirdeğin birkaç yüzdesi ama UI thread'i ve render thread'i sürekli uyanık tutar (D1-1 ile aynı mekanizma).

**Etki (doğrulayıcı düzeltmesi):** Olculen ust sinir: ayni Rebuild kosusunda tepsideyken (gosterge acik) App 1,44 Gcycles/s = tek cekirdegin ~%40'i (E1 ornegi); pencere gorunurken 1,9-3,2 G/s. Bu 1,44 G'nin ne kadarinin gizli pencere animasyonlarina, ne kadarinin log/IPC islemeye (~160 satir/s) ve 30 fps katmanli overlay gostergesine ait oldugu OLCULMEDI; 'tek cekirdegin birkac yuzdesi' tahmindir. Atif icin thread/stack bazli olcum (N7) sart.

**Çözüm:** D1-1'deki tek 'host görünür' sinyalini genişlet: (a) MainWindow IsVisibleChanged → GraphHost provider'ı false → ReapplyMotion() ReleaseBeadsClock (zaten 341'de var) + ReleaseEdgeFlowClock; geri gelince ApplyBeads yeniden kurar. (b) ProjectRow/StickyRibbon MotionGate üzerinden aynı sinyali okur (MotionGate.AnimationsEnabledProvider varsayılanına `&& StaticHostVisible` — TEK yer, altı sahip birden kapılanır); MotionGate.Changed sinyali IsVisibleChanged'de de yayınlanır ki ApplyBreathing/ApplyIndeterminate yeniden değerlendirsin. (c) Tick'te `if (_vm.IsRunUnderway && IsVisible)`; GraphView zaten _pendingStatuses ile kuyruklar, geri gelişte ShowFromTray → PushGraphStatuses() bir kez. (d) Finale: pencere gizliyken PlayEndFinale yerine ResumeFilter()+tam opaklık (reduced-motion dalıyla aynı, 539-543) — kullanıcı görmediği finali kaçırmaz.

**Çözüm (doğrulayıcı düzeltmesi):** (b) duzeltme: kapisi MotionGate.StaticAnimationsEnabled'a EKLENMEMELI — MainWindow 1239-1240 tepsi gostergesi controller'ini bu statik degerle besliyor, pencere gizlenince gosterge animasyonu da kapanirdi; yalniz instance varsayilani (MotionGate.cs:54) ya da sahip basina provider degismeli; StatusDot.cs:68 kendi ayri varsayilan lambda'sini tasiyor, o da ele alinmali. (c) eksik: tick disinda PushGraphStatuses'in bes cagirani daha var (MainWindow 221, 827, 888, 907, 936) ve TickElapsed gizliyken de her Started satirin DurationMs'ini + UpdateEta'yi 5 Hz yaziyor (RunViewModel 1851-1864) — 'gizliyken tek seferlik, geri geliste bir kez' kurali bunlari da kapsamali. (d) gizliyken finale atlanirsa geri geliste graf SON goruntuye (ResumeFilter + tam opaklik) oturmali; bu 'pencere gelince butunluk' sartinin pin testi olmali.

**Değişmezler:** Sadece UI. Statüler kaybolmaz (GraphView _pendingStatuses mekanizması mevcut, 683). Reduced-motion yolu aynen. Headless testler provider seam'i ile çalışmaya devam eder.

**Test fikri:** ProjectRow: IsCompiling + motion açık + host görünmez → PART_Breath.HasAnimatedProperties false. MainWindow: Hide() sonrası tick PushGraphStatuses çağırmaz (sayaç seam). GraphView: provider false iken PlayEndFinale → _endPlayer.IsPlaying false ve filtre geri döner.

**Doğrulayıcı notu:** Kod dogruluyor: EnsureBeadsClock 1151-1170 gorunurluk sormuyor; ProjectRow.ApplyBreathing 621-633 yalniz IsCompiling && motion (IsVisible yok, 99'da yalniz Unloaded); StickyRibbon.ApplyIndeterminate 391-415 ayni; tick 352-363 `if (_vm.IsRunUnderway) { PushGraphStatuses(); FollowFrontier(); }` gorunurlukten bagimsiz; Phase Done/Stopped -> PlayEndFinale 939-941, PlayEndFinale yalniz provider'a bakiyor (539). §14.5 metniyle celisiyor (dokuman 'her sonsuz animasyon IsVisible'a kapili' diyor) -> dokuman/kod uyusmazligi. Duzeltme: '5x/s 187 dugumluk statu itisi' pahali DEGIL — ApplyStatuses dugum basina `slot.Model == node` ile kisa devre yapiyor (GraphView 819); maliyet tick basina bir Dictionary(187) + GraphBinder.Nodes listesi ayirmaktan ibaret. Asil adaylar beads/nefes/finale saatleri.

#### D1-4 — 'Gizlemeden sonra ~8 sn render' için en güçlü hipotez: gizli pencerede oynayan bitiş finali + düğüm sönme/beads spindown zinciri (~6-8 s)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Controls/EndFinale.cs` satır 34-49 — HoldMs 900, MaxChainMs 1500, NeonMs 1150, BreathMs 700, GreyMs 980, LitGlideMs 300
- `src/BuildOrchestrator.App/Controls/MarkingChoreography.cs` satır 49 — LightMs 200 (finalin son adımı filtre dönüşü)
- `src/BuildOrchestrator.App/Graph/GraphNodeOpacity.cs` satır 49-54, 64 — HoldMs 1400 + FadeMs 700 (biten düğüm), GlideMs 280, FilterFadeMs 420
- `src/BuildOrchestrator.App/Graph/GraphBeads.cs` satır 63-70 — SpinAfterStopMs 700 + FadeOutMs 640
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 939-941 — Finale görünürlükten bağımsız tetiklenir
- `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs` satır 776-779, 382-384 — Typewriter Render-öncelikli timer + 420 ms cursorRest — gizlenince durdurulmaz (yalnız Unloaded)

**Kanıt:** 2026-09-14 raporu §5: '2 sn çözünürlüklü zaman çizelgesiyle ölçüldü; gizli anda tüm kök saatler Filling ve CompositionTarget.Rendering abonesi yok'. Bu, hide ANINDA aktif saat olmadığını söyler ama hide'dan sonra BAŞLAYAN ya da hide anında henüz başlamamış adımları (StepPlayer'ın DispatcherTimer'ı Filling saat değildir) dışlamaz. Aritmetik: finale 900 + ≤1500 + 1150 + 700 + 980 + 200 + 420 ≈ 5.9 s; üstüne beads spindown 0.7 + 0.64 ve biten düğümlerin 1.4 + 0.7 s tutma/sönmesi → ölçüm bir koşu bitiminden hemen sonra alındıysa 7-8 s'lik kuyruk tam oturur. Alternatif kuyruklar çok daha kısa: RevealStagger ≤ 380+reveal, _dimFirst 280 ms, typewriter satır başına 22×11 = 242 ms (kuyrukta birkaç satır varsa 1-2 s).

**Etki:** Tepsiye inen kullanıcı için ~8 s render; tek seferlik ama her koşu bitiminde. Kalıcı değil (rapor teyit ediyor).

**Etki (doğrulayıcı düzeltmesi):** Olculen: bosta gizlemede kuyruk yok (B0 = B1-B3). Kosu sonrasi tepside ilk olculen pencerede 81,7 M, sonrakinde 54,4 M; kaynagi bilinmiyor.

**Çözüm:** Önce ayırt edici ölçüm (open_questions #1): (i) 15 sn boşta + seçimsiz iken gizle, (ii) koşu bittiği saniyede gizle, (iii) seçimli boşta gizle — üçünün kuyruğu farklıysa kaynak belli olur. Ölçüm aracı: geçici tanı (BO_DIAG_RENDER=1 ile) Hide()'da CompositionTarget.Rendering'e abone olup son kare zamanını ve StepPlayer.IsPlaying/_endPlayer durumunu stderr'e yazan 10 sn'lik izleyici; ya da PerfView'da Microsoft-Windows-WPF `RenderMessage`/`Layout` olayları. Kaynak (ii) çıkarsa D1-3(d) çözer; (iii) çıkarsa D1-1.

**Çözüm (doğrulayıcı düzeltmesi):** Ayri fix yok. Olcum sorusu degisti: 'neden 8 s kuyruk' degil, 'tepside bosta neden kalici 50-54 M' (bkz. missing #1).

**Değişmezler:** Yok — yalnız tanı ve D1-1/D1-3'ün çözümleri.

**Test fikri:** Ölçüm sonrası: 'gizliyken finale oynamaz' pin testi (D1-3) ya da 'gizliyken edge-flow saati yok' (D1-1).

**Doğrulayıcı notu:** Sabitler dogru (EndFinale 34-49, MarkingChoreography.LightMs 49, GraphNodeOpacity 49/51/54/64, GraphBeads 67/70; EventStreamView 776 Render oncelikli timer, 382 cursorRest). Fakat hipotezin dayandigi belirti 2026-10-01 olcumunde YOK: bosta gizlemeden sonraki ilk 10 s (B0: 53,7 M) ile sonraki pencereler (B1-B3: 50,6-53,9 M) ayni — 8 s'lik kuyruk degil, KALICI ~50 M taban var. Kosu sonrasi tepside G1 81,7 M -> G2 54,4 M seklinde bir kuyruk goruluyor ama o pencere kosu bitisinden 53-63 s, gizlemeden 12-22 s sonra; ~6-8 s'lik finale/sonme zinciri bunu aciklayamaz. Kodla kanitli kisim (finale gizliyken oynar) zaten D1-3'te. Bagimsiz bulgu olarak dusuruldu: yalniz 'olcum gerekli' notu.

#### D1-5 — 200 ms _elapsedTimer boşta ve tepsideyken de tık atıyor; boşta yaptığı iş sıfıra yakın ama saniyede 5 uyanış + 1 string ayırma

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 30, 352-364, 395 — Ctor'da Start(), yalnız Closed'da Stop(); tick her zaman TickElapsed + SetLineCount
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1850-1866, 1904-1909 — TickElapsed: IsRunning değilse yalnız EvaluateEngineSilence; WaitingOnEngine false iken null=null
- `src/BuildOrchestrator.App/Console/ConsoleHeader.xaml.cs` satır 161-162 — SetLineCount: karşılaştırmadan ÖNCE string.Format ile yeni string ayırır (5/s)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2782-2788 — GetActiveLineCount: her tick lock(_gate)

**Kanıt:** Timer DispatcherPriority.Background (varsayılan), Interval 200 ms, koşulsuz. Boşta gövde: TickElapsed→EvaluateEngineSilence (iki alan okuma), GetActiveLineCount (lock), string.Format + Ordinal karşılaştırma, `if (_vm.IsRunUnderway)` false. Değer değişmediği için ölçüm/çizim geçersizlenmez (2026-09-14 §7 temiz). Yine de dispatcher 5×/s uyanır; tepsideyken de.

**Etki:** Çok küçük (tahmin <0.1 % tek çekirdek; 5 kısa string/s GC baskısı ihmal edilebilir). Kullanıcının 'en optimum' hedefi için: boşta sıfır zamanlayıcı mümkün.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. Bilinen: saniyede 5 dispatcher uyanisi + 5 string ayirma + 5 lock, tepside de. Tepsi tabaninin (50-54 M/s) ne kadarini olusturdugu bilinmiyor; timer'i gecici kapatip olcmek tek kesin yol.

**Çözüm:** Timer'ı ihtiyaç kapısına bağla: VM'de `NeedsTick => IsRunning || WaitingOnEngine` (ikisi de mevcut, 876 ve 1904); PropertyChanged'da MainWindow `_elapsedTimer.IsEnabled = _vm.NeedsTick`. Sessizlik watchdog'u yalnız bekleyiş pencerelerinde ölçtüğü için davranış birebir korunur. Ek: SetLineCount'a `int _lastCount` alanı — sayı aynıysa format etme.

**Çözüm (doğrulayıcı düzeltmesi):** `NeedsTick => IsRunning || WaitingOnEngine` yonu dogru (WaitingOnEngine = IsStarting || Stopping || Syncing || WorkspaceBusy, RunViewModel 1888-1889; private, disari acilmasi gerekir). Eksik: (1) tick 'N lines' sayacinin TEK tazeleyicisi — timer durdugu anda son bir SetLineCount cagrilmazsa kosunun son satirlari sayaca yansimaz; durma gecisinde bir kez tazele. (2) ProjectLog modunda copy-butonu gorunurlugu de bu tick'ten suruluyor (ConsoleHeader 150-166). (3) IsRunUnderway dalindaki graf itisi/frontier takibi NeedsTick ile ayni kosulda kalir.

**Değişmezler:** Yok. §14.5 'anything called from the 200 ms tick writes only when the value actually changed' zaten sağlanıyor; bu adım tick'in kendisini kaldırır.

**Test fikri:** MainWindow realize testi: koşu yokken _elapsedTimer.IsEnabled false; IsStarting true olunca true; Sync bitince false. Watchdog testi (EngineSilence) aynen geçmeli.

**Doğrulayıcı notu:** Konumlar dogru: MainWindow 30 (Interval 200 ms, varsayilan Background oncelik), 352-364 tick + kosulsuz Start, 395 yalniz Closed'da Stop; RunViewModel.TickElapsed 1849-1866, EvaluateEngineSilence 1900-1906, GetActiveLineCount lock 2782-2788; ConsoleHeader.SetLineCount string.Format'i karsilastirmadan once yapiyor. Etki '<%0,1' ise OLCULMEDI. Dikkat: tepside bosta olculen 50-54 M/s'nin kodda gorunen TEK kosulsuz periyodik App isi bu tick; dotnet-trace tepsi profilinde UI thread'in GetMessage disi payi ~%0,36 (11,07/11,11) ve DispatcherTimer.FireTick / MediaContext.RenderMessageHandlerCore / TimeManager.Tick kareleri goruluyor. Yani tick'in gercek maliyeti 'ihmal edilebilir' diye kapatilamaz; olculmeden kozmetik sayilmasi savunulamaz.

#### D1-6 — Pencere MINIMIZE (taskbar'a) edildiğinde hiçbir kapı kapanmıyor: IsVisible true kalır, imleçler ve tüm saatler döner

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1310-1311 — WindowToggle minimized'ı IsVisible'dan ayrı bir durum olarak bilir; ama hiçbir yerde StateChanged→saat durdurma yok
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 577 — Kapı yalnız IsVisible — minimized'da true
- `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs` satır 419 — Aynı

**Kanıt:** WPF'te Minimized pencere IsVisible=true'dur (yalnız Hide/Collapsed false yapar). Kodda StateChanged/WindowState==Minimized üzerine hiçbir saat/timer kararı yok (grep). Dolayısıyla D1-2'deki ~146 M döngü/sn minimize edilmiş pencerede de sürer. Not: WPF'in HwndTarget'ı minimize edilmiş pencerede present'i atlayabilir (doğrulanmadı); saatler ve UI thread invalidation'ları yine çalışır.

**Etki:** Tepsi yerine minimize eden kullanıcı için boşta maliyet ön planla aynı olabilir. CloseToTray=true olduğundan kullanıcı çoğunlukla tepsiye iner; etkisi kullanım alışkanlığına bağlı (open_questions #4).

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. Ust sinir on plandaki bosta deger (131-139 Mcycles/s); minimize'da present atlaniyorsa daha dusuk olabilir.

**Çözüm:** D1-1/D1-3'teki 'host görünür' sinyalini `IsVisible && WindowState != WindowState.Minimized` olarak tanımla ve StateChanged'de de yayınla. Tek sinyal, tek yer.

**Değişmezler:** Yok.

**Test fikri:** Realize testi: WindowState=Minimized → host-visible sinyali false → StartBlink saat kurmaz.

**Doğrulayıcı notu:** Kodda WindowState/StateChanged uzerine saat karari yok (MainWindow'da StateChanged yalniz MaximizeFix yorumunda, 139); imlec kapilari yalniz IsVisible (ConsoleView 577, EventStreamView 419). WPF'te minimize edilmis pencerede IsVisible true kalir. Ancak minimize durumunda gercek CPU olculmedi; 'on planla ayni' iddiasi tahmin. CloseToTray acik ve birincil senaryo tepsi oldugu icin onceligi dusuk.

#### D1-7 — AvalonEdit'in kendi caret blink timer'ı: fırça şeffaf ama TextArea odak alınca timer ve caret-layer invalidation büyük ihtimalle sürüyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 161-166 — CaretBrush = Transparent; yorum: 'Focusable'a dokunulmaz' → TextArea klavye odağı alabilir

**Kanıt:** AvalonEdit (6.3.1.120) Caret'i odak kazanınca Show() ile bir DispatcherTimer (sistem caret blink süresi, ~530 ms) başlatır ve her tick'te CaretLayer'ı InvalidateVisual eder; CaretBrush yalnız çizilen rengi değiştirir, timer'ı değil. Bu oturumda AvalonEdit kaynağı doğrulanmadı — kod okumasından çıkarım. Kullanıcı konsola tıklayıp bıraktıysa (Ctrl+C için olağan) boşta saniyede ~2 görünmez invalidation → render kareleri.

**Etki:** Küçük (2 kare/s, tek katman). Yalnız konsol odaklıyken.

**Etki (doğrulayıcı düzeltmesi):** Yalniz pencere aktif ve konsol odaktayken ~2 InvalidateVisual/s (sistem caret suresi); tepsi/arka plan senaryosuna etkisi yok.

**Çözüm:** Önce doğrula (aşağıdaki test). Doğruysa: `EditorControl.TextArea.Caret.Hide()`'ı TextArea.GotKeyboardFocus'ta çağır (Caret.Hide blink'i durdurur, konum/seçim/Ctrl+C mantığı sürer) — ya da WPF caret geleneğine uyan D1-2(a) sinyaliyle birlikte. Şeffaf fırça satırı olduğu gibi kalır (ConsoleCaretTests pinliyor).

**Çözüm (doğrulayıcı düzeltmesi):** Oncelik disi. Yapilacaksa once reflection'li realize testiyle timer'in gercekten calistigi kirmizi gosterilmeli; Caret.Hide() IME/Win32 caret'ini de kaldirir — Ctrl+C ve klavye secimi testleri (ConsoleCaretTests) yesil kalmali.

**Değişmezler:** Yok.

**Test fikri:** Realize + STA testi: TextArea.Focus() → reflection ile Caret'in blink timer alanı (`caretBlinkTimer`/CaretLayer) IsEnabled mi; Hide() sonrası false.

**Doğrulayıcı notu:** ConsoleView 161-166 dogru (CaretBrush Transparent, Focusable'a dokunulmuyor). AvalonEdit 6.3.1.120 DLL metadata'sinda caretBlinkTimer / StartBlinkAnimation / StopBlinkAnimation / GetCaretBlinkTime adlari var (NuGet onbelleginde dogrulandi) — timer'in varligi kanitli; tick'te InvalidateVisual yaptigi ise kaynak okunmadan bilgiye dayali. Kapsam dar: timer yalniz TextArea KLAVYE ODAGINDAYKEN doner; pencere gizlenince/deaktive olunca klavye odagi kalkar ve caret gizlenir. Yani tepside ve arka planda maliyeti SIFIR; yalniz on planda + konsola tiklanmisken ~2 gecersizleme/s. Olculmedi.

#### D1-8 — Tepsiden her dönüş (>5 s sonra) sessiz bir Sync başlatır: motor 187 projeyi tarar/değerlendirir/incremental karar verir — gizlemeden dönüşün gizli maliyeti

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | upgraded · kanıt: olculdu · zayıf makine önemi: yuksek [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs` satır 72, 180-184 — ActivationQuietMs 5000; WindowActivatedAsync → EvaluateAsync
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.AutoSync.cs` satır 48-52 — OnWindowActivated: RefreshGitOperation + autoSync.WindowActivatedAsync
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 350, 1346-1348 — Activated aboneliği; ShowFromTray Activate() çağırır
- `src/BuildOrchestrator.App/ViewModels/SyncMode.cs` satır 49 — Silent kip fetch YAPMAZ (yalnız Manual/Appended) — git'e yazma yok

**Kanıt:** ARCHITECTURE §12.3 son paragraf: 'when more than five seconds have passed since the last Sync, a silent Sync refreshes the decisions'. Bilinçli karar (spec 2026-09-18 karar 11). Sessiz Sync fetch etmez ama Supervisor tarafında tam Sync yolu (tarama, evaluation-cache yükleme — SupervisorHost.cs:46 her Sync'te 5.9 MB JSON parse, kaynak özetleri, incremental) koşar. Kullanıcı sık tepsi↔pencere geçişi yapıyorsa her dönüş bir CPU/IO patlaması.

**Etki:** Boşta değil, dönüş anında; süresi Sync süresi kadar (ölçülmedi; evaluation-cache 5.9 MB + 187 proje). Kullanıcının 'tepsiden dönünce cihaz rahat mı' algısını etkiler.

**Etki (doğrulayıcı düzeltmesi):** Her pencere aktivasyonunda (son Sync'ten >5 s sonra): 10 s'de Supervisor ~6,1 Gcycles + App bosta tabanin ~1,2 Gcycles ustu; Supervisor Private +91 MB (151 -> 242 MB) kalici. 8 GB / 2-4 cekirdekli makinede VS ile ayni anda calisirken hem CPU patlamasi hem kalici RAM artisi.

**Çözüm:** Bilinçli kararı korumak koşuluyla ucuzlatma: (a) Supervisor'da EvaluationCache'i Sync'ler arasında bellekte tut (D-diğer boyutun bulgusu, burada yalnız referans); (b) HeadWatcher zaten HEAD hareketini yakalıyor — dönüş Sync'ini 'son Sync'ten beri HEAD değişmedi VE çalışma ağacında son mtime taraması değişmedi' gibi ucuz bir ön koşula bağlamak spec kararı değiştirir → kullanıcıya sorulmalı (open_questions #6).

**Çözüm (doğrulayıcı düzeltmesi):** Sync'i UCUZLATMAK (cache'i bellekte tutmak, Sync sonrasi bellegi geri vermek) hicbir kararla catismaz ve once o yapilmali. 'HEAD/agac degismediyse atla' on kosulu ARCHITECTURE §10 (satir 1967-1969: 'runs a silent Sync even when HEAD has not moved, because files may have been edited elsewhere') ve §12.3 (2413) bilincli karariyla CATISIR — yalniz kullanici onayiyla.

**Değişmezler:** Git'e yazma yok (Silent fetch etmez, doğrulandı). Değişiklik önerisi 'şartlı': §12.3/§10.3 bilinçli kararına dokunur.

**Test fikri:** Mevcut AutoSyncCoordinator testleri; ön koşul eklenirse 'HEAD ve ağaç değişmediyse dönüş Sync'lemez' pin testi.

**Doğrulayıcı notu:** Konumlar dogru: AutoSyncCoordinator.ActivationQuietMs 72, WindowActivatedAsync 181-185, EvaluateAsync'te 5 s kapisi; RunViewModel.AutoSync.cs OnWindowActivated 48-52; MainWindow 350 Activated; SyncMode.Fetches 49 (Silent fetch etmez). Bulgu 'olculmedi, kozmetik' demis; oysa 2026-10-01 olcumu tam bu ani yakaliyor (C1): pencereye donusun ilk 10 s'sinde Supervisor 609,9 Mcycles/s (%18 cekirdek), App 251,4 M/s; Supervisor Private 150,7 -> 241,7 MB ve geri inmiyor. Ayrica kapsam bulguda dar yazilmis: tetik 'tepsiden donus' degil Window.Activated — VS'ten Alt+Tab ile her donus (son Sync'ten >5 s sonra) ayni Sync'i kosar. 'SupervisorHost.cs:46 her Sync'te 5,9 MB JSON parse' iddiasi bu dogrulamada kanitlanmadi (46'da EvaluationCache bir factory icinde kuruluyor; factory'nin Sync basina mi kok basina mi cagrildigi okunmadi).

#### D1-0 — Referans: boşta/tepside zamanlayıcı ve sonsuz-saat envanteri (bulgu değil, tablo)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 30 — _elapsedTimer · 200 ms · Background · koşulsuz · boşta ÇALIŞIR · tepside ÇALIŞIR (D1-5)
- `src/BuildOrchestrator.App/Services/IPollTimer.cs` satır 32 — Git operation poll · 2 s · Background · yalnız MERGE_HEAD/rebase/index.lock işareti varken (GitOperation.cs 78-86) · boşta KAPALI · tepside KAPALI
- `src/BuildOrchestrator.App/Services/Updates/UpdateService.cs` satır 21-22, 43 — Update timer · ilk 5 s, sonra 4 saat · thread-pool · yalnız kurulu kopyada · HTTP GitHub Releases · boşta seyrek
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 572-583 — Prompt blink (Forever 30fps) + CursorHop renk turu (Forever 20fps) · koşul: anlatı modu + dipte + IsVisible + motion · boşta GÖRÜNÜRKEN ÇALIŞIR · tepside kapalı
- `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs` satır 415-424 — Stream imleci blink + CursorHop · koşul: aktif satır görünür + IsVisible + motion · boşta GÖRÜNÜRKEN ÇALIŞIR · tepside kapalı
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 1426-1439 — Edge-flow (Forever 30fps) · koşul: seçim var + motion · boşta seçimliyse ÇALIŞIR · tepside ÇALIŞIR (D1-1)
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 1151-1187 — Beads (Forever 30fps) + spindown timer 700 ms · koşul: building düğüm · boşta kapalı · tepside build varken ÇALIŞIR (D1-3)
- `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` satır 621-633 — Nefes (Forever 30fps) · koşul: IsCompiling + motion · boşta kapalı · tepside build varken ÇALIŞIR (D1-3)
- `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs` satır 391-415 — Belirsiz sweep (Forever 30fps) · yalnız Syncing · boşta kapalı · tepside Sync varken ÇALIŞIR
- `src/BuildOrchestrator.App/Controls/BuildingSpinner.cs` satır 85, 124-131 — Dönüş (Forever 30fps) · IsVisible && motion · boşta kapalı · tepside kapalı
- `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml.cs` satır 48-54, 148-170 — Tray loop · 3 s tek iterasyon, Completed'da karar · yalnız pencere gizli + koşu aktif · boşta kapalı
- `src/BuildOrchestrator.App/Controls/StepPlayer.cs` satır 21 — Koreografi timer (Render) · yalnız koreografi oynarken · boşta kapalı
- `src/BuildOrchestrator.App/Services/StepHold.cs` satır 23 — Adım bekletme timer (Render) · yalnız işlem dizisinde · boşta kapalı
- `src/BuildOrchestrator.App/Console/ConsoleBatcher.cs` satır 82-86 — Pompa · satır gelene dek WaitToReadAsync'te uyur · boşta kapalı
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 176-200, 87-91 — stdout okuma + stderr drain · ReadAsync'te bloklu · boşta kapalı
- `src/BuildOrchestrator.App/Shell/SingleInstance.cs` satır 118-125 — Pipe dinleyici · WaitForConnectionAsync'te bloklu · boşta kapalı
- `src/BuildOrchestrator.Core/Git/HeadWatcher.cs` satır 75-84, 28 — FileSystemWatcher .git/logs/HEAD · olay güdümlü, 1.5 s debounce yalnız yazımda · boşta kapalı
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 93-101 — Komut döngüsü · stdin ReadAsync'te bloklu · timer yok · boşta kapalı

**Kanıt:** grep envanteri (DispatcherTimer|RepeatBehavior.Forever|CompositionTarget.Rendering|Task.Delay|FileSystemWatcher) + her sahibin başlatma koşulunun okunması. CompositionTarget.Rendering abonesi hiç yok. Tek-atımlık timer'lar (RevealStagger 83-88, GraphView _dimFirst 712-726, EventStream _cursorRest 388-399, BottomAnchor 185, FollowScroll 129, ConsoleHeader/About 60 ms revert) kendi tick'inde Stop() ediyor.

**Etki:** Boşta+görünür: 4 imleç saati + 200 ms tick (+ seçimliyse edge-flow). Boşta+tepsi: 200 ms tick (+ seçimliyse edge-flow). Build+tepsi: beads/nefes/statü itişi/finale.

**Etki (doğrulayıcı düzeltmesi):** Bosta+tepsi: kodda gorunen tek is 200 ms tick; olculen 50-54 Mcycles/s. Envanter eksiksiz gorunuyor ama olculen tabani aciklamiyor -> kaynak henuz bilinmiyor.

**Çözüm:** Bkz. D1-1, D1-2, D1-3, D1-5.

**Değişmezler:** —

**Test fikri:** —

**Doğrulayıcı notu:** Envanter grep ile yeniden cikarildi (RepeatBehavior.Forever: BuildingSpinner 141, MotionTokens 46/73, GraphView 1162/1433, ProjectRow 179, StickyRibbon 409; XAML'de Forever yok; CompositionTarget.Rendering yok; App'te DispatcherTimer disinda periyodik timer yok; UpdateService yalniz kurulu kopyada) — tablo kodla uyumlu. Duzeltme: tablonun 'tepside kapali / ~5 M' sonucu 2026-10-01 olcumuyle celisiyor: tepside bosta 50-54 Mcycles/s olculdu ve envanterdeki hicbir satir bunu aciklamiyor (olcumde secim yoktu, kosu yoktu).

**Doğrulayıcının eklediği noktalar:**
- ACIKLANAMAYAN TABAN — tepside bosta 50-54 Mcycles/s (measure-release-2026-10-01.log B0-B3; secim yok, kosu yok). D1'in hicbir bulgusu bunu aciklamiyor ve ARCHITECTURE §14.5 (satir 5015-5017) ayni durum icin '~5 million cycles a second' diyor -> dokuman ile olcum uyusmuyor, kullaniciya bildirilmeli. Ipucu: profile-dotnet-trace-2026-10-01.log tepsi profilinde MediaContext.AnimatedRenderMessageHandler, TimeManager.Tick ve ClockGroup.ComputeTreeState kareleri var (inclusive ~%0,01; on planda ~%0,03 — oran olculen 52/135 M ile ayni mertebede); yani tepside de bir saat/render mesaji calisiyor olabilir. Yonetilen UI thread'i ~%0,36 mesgul, dolayisiyla maliyetin cogu dotnet-trace'in gormedigi native thread'lerde (render thread vb.). Thread basina cycle olcumu + _elapsedTimer'i gecici kapatarak A/B olcumu gerekir.
- src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:1849-1864 — TickElapsed kosu sirasinda pencere GIZLIYKEN de 5 Hz calisir: tum Projects'i dolasir, her Started satirin DurationMs'ini yazar (PropertyChanged -> gerceklesmis satirlarda metin/olcum gecersizlenir) ve UpdateEta() cagirir. Tepside derleme senaryosunda kimsenin gormedigi UI isi; D1-3'un listesinde yok.
- src/BuildOrchestrator.App/MainWindow.xaml.cs:350 — Activated aboneligi HER pencere aktivasyonunda calisir (yalniz tepsiden donuste degil): VS <-> uygulama Alt+Tab'i 5 s'den seyrekse her seferinde sessiz Sync (olculen: Supervisor 610 Mcycles/s x 10 s, +91 MB Private). D1-8 bunu yalniz 'tepsiden donus' olarak cercevelemis.
- src/BuildOrchestrator.App/MainWindow.xaml.cs:832-856 — PushGraphStatuses her cagrida RowsById() ile yeni bir Dictionary (187 giris) + GraphBinder.Nodes ile yeni dugum listesi ayirir; kosu sirasinda 5 Hz + olay basina (221, 888, 907, 936). GraphView degismeyen dugumu atlar (819) ama ayirma yine yapilir; kirli-bayragi yok. App belleginin kosu boyunca 283 -> 482 MB buyumesiyle birlikte GC baskisi adayidir (payi olculmedi).
- ARCHITECTURE.md §14.5 satir 5002-5007 'every infinite animation is gated on IsVisible ... The same is true of the whole window' diyor; kodda dort sonsuz saat sahibi bu kapiyi tasimiyor: GraphView beads (1151-1170) ve edge-flow (1426-1439), ProjectRow nefes (621-633), StickyRibbon sweep (391-415). Dokuman/kod uyusmazligi — hangisinin dogru oldugunu kullanici soylemeli (CLAUDE.md kurali).
- Kosu sonrasi tepside kuyruk: G1 (gizlemeden 12-22 s sonra) 81,7 Mcycles/s -> G2 54,4 M/s. Finale zinciri (~6-8 s, kosu bitisinden 53 s once tamamlanmis olmali) bunu aciklamaz; kaynagi kodda belirlenemedi, olcum gerekir.

**Temiz bulunan alanlar:**
- Supervisor boşta gerçekten uyuyor: SupervisorHost.cs:93-101 komut döngüsü stdin ReadAsync'te bloklu; RunCoordinator'da idle timer/poll yok (yalnız koşu içi retry Task.Delay 982 ve PumpEventsAsync 587); MSBuild nodeReuse:false olduğu için build bitince arkada node kalmıyor (ARCHITECTURE §9.2). 2026-09-14 ölçümü: Supervisor boşta ~%0 CPU, 36 MB.
- App'in üç arka plan okuyucusu bloklu, dönmüyor: EngineHost.ReadLoopAsync 176-200 (NdjsonReader.ReadAsync), DrainEngineStderrAsync 87-91, SingleInstance.ListenLoopAsync 118-125 (WaitForConnectionAsync; meşgul pipe'ta geri çekilme, spin yok).
- ConsoleBatcher pompası satır gelene dek uyuyor (ConsoleBatcher.cs:82-86 WaitToReadAsync; App.xaml.cs:116) — önceki turun düzeltmesi yerinde.
- Konsol ve event-stream imleç saatleri başlatıcı metodun İÇİNDE IsVisible'a kapılı (ConsoleView 577, EventStreamView 419) ve IsVisibleChanged/Unloaded'da sökülüyor → tepside boşta bu saatler kapalı (ölçülmüş: ~5 M döngü/sn).
- BuildingSpinner (85, 124-131) ve dolayısıyla StatusGlyph: IsVisible && motion kapısı, zaten dönen saat yeniden başlatılmıyor.
- Tray build göstergesi: döngü sonsuz değil (Completed'da karar), IsVisible kapısı (48-54, 160-164), Teardown Remove() ile saati gerçekten söküyor; overlay penceresi lazy (MainWindow 1250-1256) ve boşta Hide() edilmiş.
- Git operation poll timer yalnız yarım git işlemi işareti varken kuruluyor, işaret kalkınca Stop (RunViewModel.GitOperation.cs 78-86; IPollTimer Background öncelik).
- HeadWatcher yoklama değil FileSystemWatcher; 1.5 s debounce yalnız yazım olunca (HeadWatcher.cs 28, 75-84). Boşta sıfır iş.
- UpdateService: yalnız kurulu kopyada, 4 saatte bir, tek uçuşta tur guard'ı (UpdateService.cs 21-22, 43, 55-59); hata sessiz ve yeniden deneme yok (döngüye girmez).
- Tek-atımlık DispatcherTimer'lar kendi tick'inde duruyor (RevealStagger 83-88, GraphView _dimFirst 712-726, EventStreamView _cursorRest 382-399, BottomAnchorBehavior 185, FollowScrollController 129) — §14.5 'dispatcher onu kökler' kuralı uygulanmış.
- CompositionTarget.Rendering abonesi yok (grep boş); koreografiler tek DispatcherTimer (StepPlayer) ile ve yalnız oynarken.
- 200 ms tick'in yazdığı yüzeyler değişmediyse yazmıyor: ConsoleHeader.SetLineCount 158-162, EngineOverdueMessage ObservableProperty eşitlik kısa devresi; boşta PropertyChanged yayılmıyor (2026-09-14 §7 ile tutarlı).
- Sessiz (aktivasyon) Sync git'e yazmıyor: SyncMode.Fetches yalnız Manual/Appended (SyncMode.cs:49) — değişmez korunuyor.
- Beads ve edge-flow tek paylaşımlı Clock ile (CreateClock + ApplyAnimationClock, GraphView 1151-1170, 1426-1439) — düğüm başına ayrı saat yok; DesiredFrameRate tek sabitten (MotionTokens.DecorativeFrameRate).

**Açık sorular:**
- ~8 sn render kuyruğu hangi senaryoda ölçüldü? Ayırt edici üç ölçüm gerekir: (a) 15 sn boşta ve hiçbir düğüm seçili değilken gizle, (b) bir koşu bittiği saniyede gizle, (c) bir düğüm seçiliyken boşta gizle. (a)'da kuyruk yoksa D1-4 hipotezi (finale/sönme zinciri) doğrulanır; (c)'de kuyruk hiç bitmiyorsa D1-1.
- Boştayken genelde bir proje seçili bırakılıyor mu (log okuyup pencereyi öyle mi bırakıyorsunuz)? Evet ise D1-1 boşta CPU'nun ana kaynağı olabilir ve öncelik bloklayıcıya çıkar.
- İmleçlerin pencere AKTİF DEĞİLKEN (başka pencere önde) yanıp sönmeyi bırakması kabul edilir mi? Windows'un kendi caret geleneği budur; 2026-09-14 raporu bunu 'tasarım kararı' diye açık bırakmıştı (D1-2a).
- Pencereyi tepsiye mi indiriyorsunuz (X / Shift+Space) yoksa taskbar'a minimize mi? Minimize'da bugün hiçbir kapı kapanmıyor (D1-6); kullanım alışkanlığı önceliği belirler.
- GraphView için 'host görünür' sinyalini dışarıdan (provider seam) vermeyi kabul ediyor musunuz? GraphView.xaml.cs 731-735 IsVisible'ı headless test gerekçesiyle bilerek kullanmıyor; öneri o gerekçeyi koruyor ama ARCHITECTURE §13.6/§14.5'e bir cümle eklemek gerekir.
- Tepsiden her dönüşte (>5 s) koşan sessiz Sync bilinçli karar (§12.3, spec karar 11). Dönüş anındaki CPU patlamasını azaltmak için 'HEAD ve ağaç değişmediyse atla' gibi bir ön koşul kararı değiştirir — istiyor musunuz, yoksa Sync'in kendisini ucuzlatmak (evaluation-cache'i bellekte tutmak, D-diğer) yeterli mi?
- AvalonEdit caret blink timer'ı (D1-7) için kaynak doğrulaması yapılmadı; konsola tıklayıp odakta bıraktığınız bir kullanım var mı? Yoksa bulgu düşürülebilir.


### D2-ram

#### D2-1 — App koşu bittikten sonra TÜM MSBuild satırlarını üç kopya hâlinde bellekte tutuyor (yalnız bir sonraki işlemde bırakılıyor)

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) Konumlar doğru. Ek: _liveLines'ın tek tüketicisi RunViewModel.cs:2996 (mevcut kod); proje-log chunk'ları koşu kanalından DEĞİL doğrudan writer'dan yazılır: SupervisorHost.cs:376-381 (mevcut kod).
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2130-2134 — Emit: diske yazılan HER MSBuild satırı ProjectLogEvent(RunId, ProjectId, LineNumber, Text) olarak App'e gider — filtre yok
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2693-2716 — OnProjectLog: her satır _liveLines[projectId].Add(e) (event nesnesi + RunId + ProjectId string'leri) VE _runText.Append(text) — koşulsuz, proje modunda da
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2420-2433 — OnRunCompleted hiçbir tamponu bırakmaz
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2741-2752 — ClearConsoleForNewOperation: tamponlar YALNIZ bir sonraki Build/Sync başlarken temizlenir
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 299-306 — AppendBatch: belgeden kırpılan her satır _backlogLines'a eklenir (sınırsız üçüncü kopya, satır başına ayrı string)
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 636-642 — ShowRunDocument/ResetRunDocument: SplitLines(fullRunText) — tüm anlatının satır listesi yeniden kurulur

**Kanıt:** Kod: OnProjectLog her satır için hem event nesnesini (_liveLines) hem metni (_runText) saklar; RunCompleted'da temizlik yok; ConsoleView belgeyi 200 satıra kırpar ama kırpılanları _backlogLines'ta biriktirir. Ölçüm (salt-okur, logs klasörü): run-20261001-010430-305 = 34 dosya, 26,15 MB, 79.349 satır; en büyük koşular 36,8 MB (run-20260915-145051-165). JSON deserialize her event için RunId (36 kr → ~96 B) ve ProjectId (~90-120 kr → ~200-260 B) için YENİ string üretir (interning yok).

**Etki:** TAHMİN (kayıt düzeninden hesap, ölçülmedi): satır başına _liveLines ≈ Text(UTF-16 ~690 B)+event 48 B+RunId 96 B+ProjectId ~220 B ≈ 1,05 KB; _runText ≈ 0,69 KB; backlog ≈ 0,72 KB → ~2,5 KB/satır × 79k satır ≈ 190 MB. Bu bellek koşu bittikten sonra, pencere tepsideyken de, kullanıcı yeni bir Build/Sync başlatana kadar App'te kalır — 'bittiği anda cihaz rahatlamalı' beklentisinin tam tersi. Büyük koşularda (36 MB log) ~270 MB.

**Etki (doğrulayıcı düzeltmesi):** Ölçülen Rebuild (23.047 satır / 7,7 MB, ~334 B/satır) için bulgunun kendi formülü (~2,5 KB/satır) ≈ 57 MB verir (TAHMİN), 190 MB değil. Aynı koşuda ÖLÇÜLEN App Private büyümesi 194,5 → 381,6 MB = +187 MB ve koşu sonrası/tepside 380,2 MB'da kaldı (measure log F0-G2). Yani bu tamponlar ölçülen büyümenin en fazla ~%30'unu açıklar; kalanı toplanmamış GC heap'i + başka tahsisler (ayrıştırılmadı). 190 MB rakamı yalnız 79k satırlık koşu için geçerli bir tahmindir.

**Çözüm:** (1) Sonuç olayında bırak: projectSucceeded/Failed/Skipped geldiğinde _liveLines[projectId] silinsin — güvenli, çünkü Supervisor tek FIFO kanal (RunCoordinator.cs:586, PumpEventsAsync) üzerinden o projenin tüm projectLog satırlarını sonuç olayından ÖNCE yazar; sonrasında alınacak her disk snapshot'ında ThroughLineNumber = toplam satır olur ve dikiş filtresi (RunViewModel.cs:2997, LineNumber > ThroughLineNumber) zaten hiçbir satır seçmez. RunCompleted'da _liveLines.Clear(). (2) _liveLines'ta event yerine yalnız (LineNumber, Text) tut; ProjectId/RunId string'leri saklama (satır başına ~320 B). (3) ConsoleView run-modu backlog'u kopya tutmak yerine ihtiyaç anında VM'den türetsin (ShowRunDocument zaten SplitLines(GetRunDocumentText()) ile bunu yapıyor): scroll-to-top ilk kez istendiğinde _vm.GetRunDocumentText()'ten kur — kırpılan satırları biriktirme. (4) _runText'in tamamını tutma kararı tasarım kararıdır (anlatı MSBuild satırlarını da gösterir); korunacaksa en azından (1)-(3) ile kopya sayısı 3'ten 1'e iner. Davranış değişmez: konsol görünümü, dikiş, 'N lines' sayacı aynı kalır.

**Çözüm (doğrulayıcı düzeltmesi):** (1)'deki 'projectSucceeded/Failed gelince _liveLines[id] sil' tek başına GÜVENSİZ: GetProjectLog yanıtı (SupervisorHost.cs:380-381) koşu event kanalını (PumpEventsAsync) atlayıp doğrudan writer'a yazılır; snapshot build sürerken alınıp (ThroughLineNumber=N) son chunk sonuç olayından SONRA tele düşerse, N'den sonraki canlı satırlar dikişte (RunViewModel.cs:2996-2998) _liveLines'tan okunur — silinmişse kalıcı kaybolur. Doğrusu: sonuç olayında sil, AMA _pendingLoad o projeyi bekliyorsa silmeyi dikiş tamamlanana (OnProjectLogChunk IsLast) ertele; RunCompleted'da da aynı koşulla temizle. (2) event yerine (LineNumber, Text) saklamak ve (3) backlog'u VM'den türetmek geçerli. Ek: tepsideyken AppendBatch hiç çağrılmazsa (bkz. missing[0]) üçüncü kopya o senaryoda hiç oluşmaz. Her adım için ayrı kırmızı test (geç gelen chunk + sonuç olayı sırası dahil).

**Değişmezler:** Planlama Core'da, stdout NDJSON, OutDir dokunulmaz — hiçbirine dokunmaz. Kopya yasağıyla UYUMLU (aynı metnin üç kopyası zaten yasağın ruhuna aykırı). Dikiş atomikliği (T28/Fix wave 1) korunmalı: silme _gate altında ve yalnız sonuç olayı UI thread'inde işlendikten sonra.

**Test fikri:** RunViewModelTests: 80k ProjectLogEvent besle → RunCompletedEvent → GC.Collect → _liveLines boş (InternalsVisibleTo zaten var) ve GC.GetTotalMemory(true) farkı koşu öncesine göre < 5 MB; ayrıca kart tıklama dikişi (LineNumber > ThroughLineNumber) testleri yeşil kalmalı.

**Doğrulayıcı notu:** Kod doğrulandı: RunCoordinator.cs:2130-2134 Emit her satırı filtresiz ProjectLogEvent yapar; RunViewModel.cs:2693-2716 OnProjectLog koşulsuz _liveLines[id].Add(e) + _runText.Append; OnRunCompleted (2420-2433) hiçbir tamponu bırakmaz; tek temizlik ClearConsoleForNewOperation (2741-2752). ConsoleView.xaml.cs:299-306 kırpılan canlı satırları _backlogLines'a ekler. _liveLines'ın TEK okuyucusu dikiştir (RunViewModel.cs:2996). İki düzeltme gerekli: (1) etki sayısı ölçülen koşuyla değil 79k satırlık Resolve-cycles koşusuyla hesaplanmış; (2) önerilen 'sonuç olayında _liveLines sil' adımında gözden kaçan bir yarış var (aşağıda).

#### D2-2 — Her Sync ve her koşu 12 MB JSON'u sıfırdan parse edip (değişmese bile) yeniden yazıyor; %93'ü ölü test girdisi olan evaluation-cache hiç budanmıyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 44-48 — Sync fabrikası: her syncWorkspace komutunda new EvaluationCache + new SourceHashCache + new BuildStateStore
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 168 — workspace.Sync(cmd.RootPath) — fabrika her komutta çağrılır, memoize yok
- `src/BuildOrchestrator.Supervisor/Program.cs` satır 92-97 — Koşu planlaması da kendi taze EvaluationCache + SourceHashCache örneğini kurar
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 142-151, 96-104 — Load: File.ReadAllText(5,9 MB) → 11,8 MB UTF-16 string (LOH) + Deserialize; Flush: tüm sözlük yeniden Serialize
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 195-208, 148-166 — Load: 6,25 MB → 12,5 MB string + 24.588 girişli ConcurrentDictionary; Flush: ToDictionary kopyası + 12,5 MB serialize
- `src/BuildOrchestrator.Core/Planning/BuildPlanBuilder.cs` satır 43 — cache.Flush() KOŞULSUZ — hiçbir giriş değişmese de dosya yeniden yazılır
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` satır 324 — hashes.Flush() KOŞULSUZ
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 54, 122-130 — Schema != CurrentSchema girişler asla isabet olmaz; PruneMissingUnderRoot yalnız verilen kök altını budar → Temp yolundaki girişler hiç silinmez
- `src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs` satır 72, 267 — Pencereye her dönüşte (son Sync'ten 5 s geçtiyse) ve HEAD değişiminde otomatik Sync → bu maliyet sık tekrarlanır

**Kanıt:** evaluation-cache.json (salt-okur incelendi): 2.761 giriş; 2.558'i C:\Users\Delta\AppData\Local\Temp\bo-vm-rebuild-*\X|Y.csproj (SAYICA %93, ama karakter olarak ~1,36M / 5,6M ≈ %24 — ortak bağlamdaki '%93' sayıya göredir, bayta göre değil). Tüm 2.558 temp girişi Schema=0 (toplam 2.565 schema-0, 196 schema-1) → CurrentSchema=1 olduğundan hiçbir zaman isabet olmaz, yalnız parse/serialize yükü. Girişlerin csproj mtime aralığı 2026-07-17 → 2026-09-19; SupervisorSandbox/SupervisorIsolationGuardTests 83942f7 (2026-09-19) ile geldi — sızıntı kaynağı RunViewModelTests.cs:1304 Rebuild_wires_through_the_real_engine_and_populates_rows testinin o tarihten önce gerçek motoru --logs'suz başlatması (Program.cs:24-30 varsayılan cacheRoot = %LOCALAPPDATA%\BuildOrchestrator). Bugün guard bunu yakalıyor; §17.2 hiçbir şeyi kaçırmıyor, yalnız TORTU temizlenmiyor. source-hash-cache: 24.588 giriş, 34 temp; build-state: 196 giriş, 0 temp.

**Etki:** TAHMİN: Sync başına ≈ 24 MB geçici UTF-16 okuma string'i (LOH) + ~21 MB canlı nesne (SourceHashCache ~12 MB: giriş başı ~500 B × 24.588; EvaluationCache ~8-9 MB: 20.677 CompileFiles yolu + 2.558 ölü giriş) + Flush'ta ~24 MB serialize string'i + 24.588 girişlik ToDictionary kopyası → Sync başına ~70 MB LOH ağırlıklı çöp ve 12 MB disk yazımı, değişiklik olmasa bile. Workstation concurrent GC'de LOH yalnız gen2'de toplanır → Supervisor working set Sync'ler arasında şişik kalır; pencereye her dönüş bunu tetikler.

**Etki (doğrulayıcı düzeltmesi):** Sync başına '~70 MB çöp' hesabı TAHMİN olarak kalır, ama ölçümle tutarlı: aktivasyon Sync'inde Supervisor Private 150,7 → 241,7 MB (+91 MB), koşu sonu Sync'inde 144,9 → 216,6 MB (+72 MB) ÖLÇÜLDÜ ve boşta geri inmedi. Aynı bellek koşu sırasında kendiliğinden 137,2 MB'a indi (measure log D2-rebuild-foreground-6) — yani kalıcı sızıntı değil, boşta toplanmayan çöp. Temp tortusu bayt olarak ~1,69 MB / 5,9 MB (≈%29), sayıca %93.

**Çözüm:** (a) Supervisor ömrü boyunca cacheRoot başına TEK EvaluationCache ve TEK SourceHashCache örneği: WorkspaceServices.Default fabrikası örnekleri kapatıp memoize etsin ve Program.cs BuildRunPlan aynı örnekleri kullansın (SourceHashCache zaten ConcurrentDictionary; EvaluationCache'in GetOrEvaluate/Flush'ına kilit gerekir — EvaluationCache.cs:84-90 planner thread ↔ Sync eşzamanlılığını zaten belgeliyor). (b) Kirli bayrak: yalnız giriş eklendi/değişti/silindi ise Flush yazsın (BuildPlanBuilder.cs:43 ve SyncWorkspaceService.cs:324 koşulsuz çağrılar aynı kalır, Flush içeride no-op olur) — §16'nın 'değişiklik yoksa dosya yeniden yazılmaz' ilkesini önbelleklere de taşır. (c) Optimize'a şema budaması: Schema < CurrentSchema olan giriş hiçbir kökte servis edilemez → kök kapsamına bakmadan silinebilir (bu §16'daki 'kök kapsamı' ilkesiyle çelişmez: canlı bir karar değişmez). Bu tek başına 2.565 girişi ve ~1,4 MB'ı düşürür. (d) Load yolunda File.ReadAllText yerine FileStream + JsonSerializer.Deserialize(Stream) — 12/12,5 MB UTF-16 ara string'i hiç oluşmaz (Utf8JsonReader doğrudan bayttan okur); Flush'ta SerializeAsync(Stream) — aynı kazanç yazarken.

**Çözüm (doğrulayıcı düzeltmesi):** Sıra: önce (b) kirli bayrak + (d) Stream tabanlı Deserialize/Serialize + (c) budama; (a) memoize EN SON ve ölçümle. Gerekçeler: (a) EvaluationCache düz Dictionary'dir (thread-safe değil) ve doc'u iki ayrı örneğin paralel çalıştığını varsayar (EvaluationCache.cs:84-90) — paylaşılan örnek kilit ister; ayrıca memoize ~20 MB'ı (tahmin) Supervisor'da KALICI canlı tutar, zayıf makinede geçici çöp + D2-3 daha ucuz olabilir. (b) SourceHashCache'te racy girişler Flush'ta dışarıda bırakılır (SourceHashCache.cs:150-153): kirli bayrak, yazılamayan racy giriş varken TEMİZLENMEMELİ, yoksa o özet hiç kalıcılaşmaz. (c) 'Schema<Current giriş hiçbir zaman servis edilmez' iddiası tam doğru değil: Stale() (EvaluationCache.cs:45, 50, 77) şemaya bakmadan eski girişi 'dosya kayboldu' yarışında döndürür; budama bu yedeği de kaldırır (Temp girişleri için önemsiz) — doc'a yazılmalı. Aktivasyon Sync'inin HEAD değişmeden de koşması spec karar 11'dir (disk içeriği HEAD'den okunmaz); sıklığı değiştirmek ayrı bir kullanıcı kararıdır.

**Değişmezler:** Karar mantığı değişmez (aynı veri, aynı isabet kuralı). 'Planlama Core'da' korunur. Şema budaması ARCHITECTURE §16 'both operations scope by root' cümlesine EK bir kural getirir — doküman aynı işte güncellenmeli (şartlı: kullanıcı kök dışını budamayı onaylamalı).

**Test fikri:** EvaluationCacheTests: (1) hiçbir GetOrEvaluate yeni giriş üretmediyse Flush dosyanın mtime'ını değiştirmez (kırmızı: bugün her Flush yazar); (2) Optimize, Schema=0 girişi kök dışında olsa da budar (kırmızı: PruneMissingUnderRoot kök dışını atlar); (3) SupervisorHost iki ardışık Sync'te aynı SourceHashCache örneğini kullanır (referans eşitliği, test seam).

**Doğrulayıcı notu:** Kod doğrulandı: SupervisorHost.cs:44-48 fabrikası her Sync'te new EvaluationCache/SourceHashCache/BuildStateStore kurar; Program.cs:92-97 koşu planı da kendi örneklerini kurar; EvaluationCache.cs:147 ve SourceHashCache.cs:201 File.ReadAllText + Deserialize; BuildPlanBuilder.cs:43 cache.Flush() ve SyncWorkspaceService.cs:324 hashes.Flush() koşulsuz; Flush'lar (EvaluationCache.cs:96-104, SourceHashCache.cs:148-166) kirli bayraksız tam serialize. Dosyaları kendim ölçtüm (verify_d2_cache.py): evaluation-cache 5.901.064 B, 2761 giriş, Schema 0 = 2565 / Schema 1 = 196, Temp yollu 2558 giriş ≈ 1,69 MB; source-hash-cache 6.251.491 B, 24.588 giriş, 34 temp. Pencereye dönüşte HEAD değişmese de Sync atılır: AutoSyncCoordinator.cs:291 (IsActivation ise sha eşitliği dönüşü atlanır), yalnız 5 s sessizlik kapısı (satır 72, 267).

#### D2-3 — Koşu sonunda hiçbir 'rahatlama' adımı yok: Supervisor ve App'in ısınmış heap'i GC keyfine bırakılıyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 584-650 — ExecuteRunAsync finally: cap geri alınır, ledger boşalır, pump beklenir, _scheduler/_wake null'lanır — GC/heap'e dair adım yok
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1056-1057, 2337 — _logs = null + Dispose — koşu-ömürlü nesneler doğru bırakılıyor (retained set küçük)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2420-2433 — OnRunCompleted: bellek bırakma adımı yok
- `src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` satır 22-40 — GC/runtime knob'u yok (runtimeconfig.template.json de yok)

**Kanıt:** grep: src/ altında GC.Collect / GCSettings / SetProcessWorkingSetSize / EmptyWorkingSet HİÇ yok. Koşu boyunca Supervisor planlama (12 MB JSON string'leri, 24.588 girişli sözlükler, 187 düğümlü plan/graf, binder fingerprint'leri) ve proje başına 148 KB build-state parse/serialize (BuildStateStore.cs:199-214, her Upsert Load çağırır) ile büyük miktarda LOH+gen0 çöp üretir; koşu bitince canlı nesne az kalır ama .NET GC boşta kalan bir process'te kendiliğinden hemen gen2 toplayıp bölgeleri decommit etmez (resmi davranış: GC tahsis tetiklidir; tahsis durunca toplama da durur). App'te D2-1'in tamponları serbest bırakılsa bile toplanmaları için bir gen2 gerekir.

**Etki:** Kullanıcının gördüğü: build bittikten sonra Supervisor ve App working set'i koşu sırasındaki tepe civarında kalır; tepside boşta dururken 'cihaz rahatlamaz'. TAHMİN: Supervisor'da onlarca MB (JSON string'leri + plan), App'te D2-1'in ~190 MB'ı + koşu sırasında birikmiş gen2.

**Etki (doğrulayıcı düzeltmesi):** ÖLÇÜLDÜ: App Private koşu sonunda 381,6 MB, 60 s sonra tepside 380,2 MB (koşu öncesi 194,5). Supervisor Private Sync sonrası 241,7 MB'da boşta sabit kaldı; aynı process koşu içinde tahsis sürerken 137,2 MB'a kendiliğinden indi, koşu sonu Sync'iyle tekrar 216,6 → 209,5 MB'da kaldı. Yani Supervisor'da boşta ~70-100 MB toplanabilir bellek duruyor (üst sınır ölçümden; zorlanmış GC'nin gerçekte ne kadarını geri vereceği ÖLÇÜLMEDİ). App'te geri kazanılabilir pay D2-1 bırakmalarına bağlı; bırakma yapılmadan GC.Collect yalnız çöpü alır.

**Çözüm:** Supervisor ExecuteRunAsync finally'sinde `await pump` sonrası (kilit dışında): GCSettings.LargeObjectHeapCompactionMode = CompactOnce; GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true); GC.WaitForPendingFinalizers(). Bedeli: heap o anda küçük olduğundan on ms mertebesi, process boşta — App'e görünmez. App'te RunCompleted işlendikten sonra Dispatcher.InvokeAsync(…, DispatcherPriority.ContextIdle) ile bir kez aynı çağrı (D2-1 bırakmaları YAPILDIKTAN sonra; yoksa toplanacak şey yok). SetProcessWorkingSetSize(-1,-1)/EmptyWorkingSet DÜRÜST DEĞERLENDİRME: yalnız sayfaları standby listesine iter — Task Manager 'Working set' düşer, commit düşmez, sonraki dokunuşta soft page fault ile geri gelir; gerçek rahatlama değildir. Kullanıcı 'rahatlama'yı Task Manager'dan okuyorsa GC.Collect'in ARDINDAN bir kez çağrılabilir (zararsız, ama ölçüp karar verilmeli). Sync sonrası (D2-2 çözülmezse) aynı adım Sync bitiminde de mantıklı.

**Çözüm (doğrulayıcı düzeltmesi):** Yer düzeltmesi: Supervisor'da asıl şişme Sync'ten geliyor (ölçüm), koşu finally'sinden değil — ve koşu sonu Sync'i koşudan saniyeler sonra gelir; finally'deki GC boşa gider. Tek bir 'boşta rahatlama' noktası: Sync tamamlanınca (SupervisorHost.SyncWorkspaceAsync sonrası) ve koşu aktif değilken, kısa bir sessizlikten sonra bir kez. App'te GC.Collect HANGİ thread'den çağrılırsa çağrılsın tüm managed thread'leri durdurur; ~380 MB heap'te blocking+compacting gen2 duraklaması ölçülmeden UI thread'e konmamalı — pencere tepsideyken/gizlenince ya da koşu bitiminden sonra boşta, arka plan thread'inden tetiklenmeli ve duraklama süresi ortam-kapılı testle ölçülmeli. EmptyWorkingSet değerlendirmesi (kozmetik) doğru.

**Değişmezler:** Hiçbir değişmeze dokunmaz. Kilit içinde ve worker'lar join olmadan çağrılmamalı (zaten finally'nin sonu). Testlerde D8 (sleep-poll yok) etkilenmez.

**Test fikri:** Supervisor: RunCoordinatorTests'e koşu sonrası GC.GetGCMemoryInfo().HeapSizeBytes'ın koşu-öncesi seviyeye (±%20) döndüğünü pinleyen ortam-kapılı ölçüm testi ([SkippableFact], BO_MEASURE_MEMORY=1).

**Doğrulayıcı notu:** grep doğrulandı: src/, scripts/, Directory.Build.props altında GC.Collect / GCSettings / SetProcessWorkingSetSize / EmptyWorkingSet yok. ExecuteRunAsync finally (RunCoordinator.cs:584-650) ve OnRunCompleted (RunViewModel.cs:2420-2433) bellek adımı içermez; _logs = null satır 1056 ve 2337 doğru. Bulgunun 'ölçülmedi' dediği etki artık ölçümle destekli.

#### D2-4 — GC yapılandırması varsayılanda: Supervisor için concurrent GC gereksiz, ConserveMemory kapalı

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Supervisor/BuildOrchestrator.Supervisor.csproj` satır 12-15 — Yalnız OutputType/TargetFramework — GC/tier ayarı yok
- `Directory.Build.props` satır 1-25 — Ortak props'ta runtime knob'u yok
- `scripts/package.ps1` satır publish satırı — Framework-dependent publish, R2R yok (ortak bağlam)

**Kanıt:** grep (csproj/props/json/ps1): ConcurrentGarbageCollection, ServerGarbageCollection, GCConserveMemory, RetainVM, TieredCompilation, TieredPGO, ReadyToRun, InvariantGlobalization → 0 sonuç (yalnız .claude/temp raporunda geçiyor). Varsayılan: workstation + concurrent (background) GC her iki process'te. Resmi .NET davranışı: background GC UI duyarlılığı için ayrı bir GC thread'i çalıştırır; System.GC.ConserveMemory (0-9, .NET 6+) GC'nin gen2/LOH'u daha agresif sıkıştırıp heap'i küçük tutmasını sağlar (daha sık toplama pahasına); System.GC.RetainVM varsayılan false (segmentler OS'a geri verilir).

**Etki:** Supervisor'ın UI'ı yok; concurrent GC'nin sağladığı düşük duraklama hiçbir şeye yaramaz ama bir GC thread'i ve arka plan toplama yükü taşır. Koşu/Sync sırasında LOH string patlaması (D2-2) ile boşta bekleme dönüşümlü olan bir process için ConserveMemory heap'in şişik kalmasını sınırlar. Etki büyüklüğü ÖLÇÜLMELİ — rakam veremem.

**Etki (doğrulayıcı düzeltmesi):** Rakam yok. D2-3 (boşta GC) yapıldıktan SONRA A/B ile ölçülmeden değer atfedilemez.

**Çözüm:** Supervisor csproj'una: <ConcurrentGarbageCollection>false</ConcurrentGarbageCollection> ve runtimeconfig.template.json ile "System.GC.ConserveMemory": 5 (orta değer; 7-9 daha sık GC demektir). App'te concurrent GC KALSIN (UI duraklaması). TieredCompilation/TieredPGO varsayılanda kalsın. ŞARTLI: <InvariantGlobalization>true — ICU yüklemesini kaldırır (her iki process'te birkaç MB eşlenmiş DLL + culture verisi); uygulama İngilizce-only ama WPF/CultureInfo davranışlarını (karşılaştırma, tarih biçimi) etkiler → önce ölç, sonra karar. R2R (D-startup boyutunda anıldı) RAM'e de yarar: JIT edilmiş kod özel (private) bellekken R2R kodu dosya-eşlemeli ve paylaşılabilir; App ve Supervisor aynı Core/Contracts imajını paylaşır.

**Çözüm (doğrulayıcı düzeltmesi):** Kod değişikliği yapmadan önce aynı ikili üzerinde DOTNET_gcConcurrent=0 / DOTNET_GCConserveMemory=5 ortam değişkenleriyle Sync + Build + boşta Private ölç; fark görülürse yalnız Supervisor csproj'una işle. InvariantGlobalization WPF kültür davranışını etkiler — App'e önerilmez.

**Değişmezler:** Velopack yalnız App'te — dokunulmaz. Yayın hattı (package.ps1 publish) değişmez; knob'lar csproj/runtimeconfig.template'te. Kopya yasağı: GC ayarı yalnız Supervisor csproj'unda, props'a konmaz (App farklı politika ister).

**Test fikri:** Kaynak guard'ı (PublishLayoutTests deseni): Supervisor csproj ConcurrentGarbageCollection=false içermiyorsa kırmızı; App csproj'unda BULUNMADIĞI da pinlenir. Ortam-kapılı A/B: DOTNET_gcConcurrent=0 ve DOTNET_GCConserveMemory=5 ile aynı ikili üzerinde Sync+Build sonrası working set.

**Doğrulayıcı notu:** Olgu doğru: Supervisor csproj (satır 12-15) ve Directory.Build.props'ta hiçbir GC/tier ayarı yok, runtimeconfig.template.json yok, publish framework-dependent (scripts/package.ps1:97 --self-contained false). Ancak etki tamamen ölçümsüz; ölçülen sorun 'boşta GC tetiklenmediği için bellek geri verilmiyor' ve ConserveMemory/ConcurrentGC=false bunu çözmez — ikisi de yalnız bir GC GERÇEKLEŞTİĞİNDE davranışı değiştirir, boşta GC tetiklemez. Background GC thread'inin maliyeti için kodda/ölçümde kanıt yok. InvariantGlobalization ve R2R önerileri de ölçümsüz.

#### D2-5 — Proje logu açılışında 4 MB'lık log 5-6 kopya olarak dolaşıyor (Supervisor'da 1, App'te 4-5)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2984, 2995, 3000 (3001 değil) + 2839 (SeedProjectDocument ToString) + src/BuildOrchestrator.App/MainWindow.xaml.cs:633-634 (SplitLogLines).
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 69-75, 146-155 — ReadAllText / ReadToEnd: 4 MB dosya → 8 MB UTF-16 string (LOH)
- `src/BuildOrchestrator.Core/Logs/LogChunker.cs` satır 7-25 — 64 KB'lık Substring'ler → chunk başına ~130 KB string (LOH eşiği 85 KB üstü)
- `src/BuildOrchestrator.Contracts/Ipc/NdjsonFraming.cs` satır 19 — SerializeToUtf8Bytes: chunk başına yeni byte[] (~64-70 KB) — havuz yok
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2984, 2995, 3001 — pending.Assembly.Append(chunk) → new StringBuilder(pending.Assembly.ToString()) → _projectText[id] = stitched: aynı 8 MB üç kez
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2800-2802 — GetProjectDocumentText: sb.ToString() — dördüncü kopya
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 682 — PlayCascade: _backlogLines = [.. allLines] — satır listesinin kopyası

**Kanıt:** Kod okuması: yukarıdaki zincir. Ölçüm: proje logları 0,2-4 MB (Types.UsedCars 8.824 satır, ortak bağlam). ARCHITECTURE §13.5 chunk loader tasarımı 'son 128-256 KB diskten yüklenir' der ama uygulama snapshot'ın TAMAMINI IPC'den geçirip App'te tam metni tutar (ConsoleView yalnız belgeyi 200 satırla sınırlar).

**Etki:** TAHMİN: 4 MB'lık logda tıklama başına ~40 MB geçici tahsis (çoğu LOH) ve ~16 MB kalıcı (StringBuilder + satır listesi) — tıklama gecikmesi ve gen2 baskısı. Sık kart tıklamada LOH parçalanması.

**Etki (doğrulayıcı düzeltmesi):** MB rakamları TAHMİN; yalnız kart tıklamasında oluşur, tepside-derleme senaryosunda hiç tetiklenmez. Ölçülen koşuda proje başına satır medyan 67, p90 202, max 2590 — 4 MB'lık log uç durumdur.

**Çözüm:** App: _projectText[e.ProjectId] = pending.Assembly (aynı StringBuilder'a canlı satırları ekle; ToString+new StringBuilder ikilisini kaldır); GetProjectDocumentText yerine satır listesini VM'den tek kez üretip ConsoleView'a kopyasız ver (PlayCascade'in `[.. allLines]` kopyası, VM listeyi bir daha kullanmıyorsa gereksiz). Supervisor: ProjectLogFile.Snapshot'ta ReadToEnd yerine dosya boyutu kadar önceden boyutlanmış StringBuilder ya da doğrudan chunk'lara bölerek okuma (StreamReader ile 64K'lık okuma → chunk başına serialize) — 8 MB'lık tek string hiç oluşmaz. NdjsonWriter: ArrayBufferWriter/pool ile serialize (Utf8JsonWriter(IBufferWriter)) — mesaj başına byte[] tahsisi kalkar.

**Çözüm (doğrulayıcı düzeltmesi):** '_projectText[id] = pending.Assembly' önerisi olduğu gibi uygulanırsa UI thread'inde yeni bir donma üretir: CountLines(StringBuilder) (RunViewModel.cs:2790-2796) sb[i] indeksleyicisiyle sayar; chunk'larla Append edilmiş çok parçalı bir StringBuilder'da indeksleyici parça zincirini yürür (maliyet ≈ uzunluk × parça sayısı). Bugün hızlı olmasının tek nedeni 2995'teki tek parçalı kopyadır. Kopya kaldırılacaksa satır sayımı GetChunks() ile ya da chunk gelirken ('\n' sayarak) yapılmalı ve bu bir testle pinlenmeli.

**Değişmezler:** stdout NDJSON ve chunk sözleşmesi (LogChunker 64K, ThroughLineNumber) değişmez; dikiş atomikliği korunur.

**Test fikri:** RunViewModelTests: 4 MB'lık sahte snapshot chunk'ları besle, OnProjectLogChunk sonrası GC.GetAllocatedBytesForCurrentThread farkı < 2× metin boyutu (bugün ~5×).

**Doğrulayıcı notu:** Zincir kodda doğrulandı: RunLogWriter.cs:73 ReadAllText ve 146-155 ReadToEnd; LogChunker.cs:7-25 64K Substring; NdjsonFraming.cs:19 SerializeToUtf8Bytes; RunViewModel.cs:2984 Assembly.Append, 2995 new StringBuilder(pending.Assembly.ToString()), 3000 _projectText[id] = stitched (bulgu 3001 demiş, bir satır kayık); 2800-2802 ve 2839 ToString; MainWindow.xaml.cs:633-634 SplitLogLines; ConsoleView.xaml.cs:682 [.. allLines]. Tek yanlış: 'ARCHITECTURE §13.5 son 128-256 KB diskten yüklenir der' iddiasını doğrulayamadım — ARCHITECTURE.md'de '128' / '256 KB' geçmiyor; bu cümle kanıt olarak kullanılmamalı.

#### D2-6 — build-state.json her Upsert'te baştan okunup parse ediliyor ve tamamen yeniden yazılıyor (koşu başına ≥187 kez)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/State/BuildStateStore.cs` satır 199-214 — Write: her çağrı Load() (AtomicFile.ReadAllTextSharingDelete + Deserialize + GroupBy/ToDictionary) → mutate → Serialize → AtomicFile.WriteAllText
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2253-2262 — InvalidateBuildStateOnFailure: Load() + Upsert (Upsert kendi içinde tekrar Load) — hata başına 2 parse
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1933-1941 — UpdateCycleNonConvergenceMemory: Load + üye başına Upsert (her biri tekrar Load)
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 771, 879 — Planlamada iki ayrı tam Load
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 47, 51, 56 — Sync/Clean/Optimize her komutta yeni BuildStateStore örneği — aynı dosya, aynı process, örnekler arası paylaşım yok

**Kanıt:** build-state.json 148.858 B / 196 giriş (ölçüldü). 187 projelik koşuda her sonuç → Upsert → Load(148 KB parse) + Serialize(148 KB) + atomik yazım. TAHMİN: koşu başına ~55 MB gen0 çöp + 187 dosya yazımı/rename.

**Etki:** RAM için küçük (gen0, kısa ömürlü); asıl bedel CPU/IO (başka boyutun konusu). Yine de koşu sırasında GC sıklığını artırır.

**Etki (doğrulayıcı düzeltmesi):** 'Koşu başına ≥187' yanlış: Upsert yalnız GERÇEKTEN derlenen/başarısız olan proje için çağrılır. Ölçülen Rebuild'de succeeded=152 + failed=2 → ~154-156 yazım (hata dalında 2 parse); her şey güncelken ~0. Çöp gen0 değil ağırlıkla LOH: 148 KB dosya UTF-16'da ≈ 297 KB string (85 KB eşiğinin üstü) — Upsert başına okuma string'i + serialize string'i = 2 LOH tahsisi ≈ 0,6 MB → koşu başına ≈ 90 MB LOH devri (koddan türetilmiş hesap, ölçülmedi). Asıl bedel ~154 tam dosya yazımı + rename (IO), zayıf diskte derlemeyle yarışır.

**Çözüm:** BuildStateStore içinde _writeGate altında bellek-içi map + dosya (mtime, length) damgası: damga değişmediyse Load atlanır; yazdıktan sonra damga güncellenir. Aynı process'teki diğer örnekler (Sync'in BuildStateStore'u) dosyayı değiştirince damga farkı yakalar — never-throw sözleşmesi ve atomik yazım aynen kalır. Alternatif: SupervisorHost fabrikası tek BuildStateStore örneğini paylaşsın (koordinatörle aynı), Load damgası yine gerekir (harici process yazıcısı yok ama güvenli).

**Çözüm (doğrulayıcı düzeltmesi):** (mtime, length) damgası güvenilmez: aynı process'teki ikinci örnek (Sync'in BuildStateStore'u, SupervisorHost.cs:47) aynı saat tikinde aynı uzunlukta yazarsa damga değişmez ve güncelleme kaybolur. Daha güvenlisi: Supervisor'da cacheRoot başına TEK BuildStateStore örneği (fabrika + koordinatör aynı nesne), yetkili bellek-içi map _writeGate altında; Load() o map'in kopyasını döner. Dosya biçimi ve atomik yazım aynı kalır. Ayrı bir kazanım: yazımı Stream'e SerializeAsync ile yapmak LOH string'ini kaldırır.

**Değişmezler:** 'Yalnız bu JSON dosyasına I/O' (§4) ve atomik temp+rename korunur. Dosya biçimi değişmez.

**Test fikri:** BuildStateStoreTests: ardışık iki Upsert arasında dosya değişmediyse ikinci Upsert'in okuma yapmadığı (AtomicFile okuma sayacı seam'i) — kırmızı: bugün her Upsert okur.

**Doğrulayıcı notu:** BuildStateStore.cs:199-214 Write her çağrıda Load() (satır 206) + tam Serialize + atomik yazım yapar; bellek-içi önbellek yok. RunCoordinator.cs:2181 başarı Upsert'i, 2253-2255 hata dalında Load + Upsert (çift parse), 1933-1947 cycle hafızasında Load + üye başına Upsert, 771 ve 879 planlama Load'ları doğru. Dosya 148.870 B (ölçtüm). Sayı düzeltmesi gerekli.

#### D2-7 — IPC satır başına 3 yeni string + reflection tabanlı JSON: RunId/ProjectId her satırda yeniden tahsis ediliyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` satır 7-15, 302 — IpcJson.Options reflection tabanlı (JsonSerializerContext yok); ProjectLogEvent(RunId, ProjectId, LineNumber, Text)
- `src/BuildOrchestrator.Contracts/Ipc/NdjsonFraming.cs` satır 19, 52 — Her mesaj için SerializeToUtf8Bytes byte[] (Supervisor) ve Deserialize ile taze string'ler (App)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2695-2700 — Event nesnesi olduğu gibi _liveLines'a konur — RunId/ProjectId kopyaları yaşamaya devam eder

**Kanıt:** grep: JsonSerializerContext/JsonSerializable src altında 0 sonuç. 79.349 satırlık koşuda ≈ 240k kısa string (RunId 36 kr + ProjectId ~100 kr) + 79k byte[] tahsisi; sözlük anahtarı zaten aynı ProjectId'yi taşıyor.

**Etki:** D2-1'in satır başı ~320 B'lik kısmı (≈25 MB / 79k satır) ve gen0 baskısı. Source generator'ın bellek kazancı küçüktür (metadata önbelleği bir kez kurulur); asıl kazanç interning'de.

**Etki (doğrulayıcı düzeltmesi):** Ölçülen koşuda 23.047 satır × ~320 B ≈ 7 MB kalıcı (bulgunun kendi satır başı tahminiyle), 25 MB değil. Source generator'ın RAM kazancı için kanıt yok.

**Çözüm:** App'te alımda küçük bir ProjectId/RunId 'intern' sözlüğü (RunViewModel OnProjectLog'da _liveLines anahtarı zaten var: event yerine (LineNumber, Text) sakla — D2-1 (2) ile aynı düzeltme). Contracts'ta [JsonSerializable] source generator: startup'ta reflection metadata'sı ve emit edilen IL'i kaldırır; D2-1/D2-2 sonrası ölçülüp karar verilmeli — tek başına düşük öncelik.

**Çözüm (doğrulayıcı düzeltmesi):** D2-1 ile birleştir; ayrı iş açma. Source generator ayrı bir startup/CPU konusu olarak ölçülmeden yapılmaz.

**Değişmezler:** IPC sözleşmesi ve NDJSON değişmez. Contracts'a source generator eklenirse polymorphic attribute'lar aynen kalır.

**Test fikri:** D2-1 testinin parçası: _liveLines içeriği ProjectLogEvent referansı taşımaz (tip pinlemesi).

**Doğrulayıcı notu:** Olgu doğru (IpcMessages.cs:7-15 reflection tabanlı Options, satır 302 ProjectLogEvent; NdjsonFraming.cs:19 ve 52; src'de JsonSerializerContext yok), ancak bağımsız bir bulgu değil: kalıcı kısmı D2-1'in (2) numaralı düzeltmesinin aynısı, geçici kısmı gen0'da ölür. Sayı 79k satırlık koşudan alınmış.

#### D2-8 — Settings/About/Notes/UpdateRestart overlay'leri açılışta eager kuruluyor (Collapsed ama ağaç bellekte)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.App/MainWindow.xaml:270-294
- `src/BuildOrchestrator.App/MainWindow.xaml` satır 271-294 — Dört overlay pencere XAML'inde doğrudan örneklenir; Visibility Collapsed
- `src/BuildOrchestrator.App/Views/SettingsDialog.xaml` satır 1-404 — 404 satırlık ağaç (en büyüğü)
- `src/BuildOrchestrator.App/Views/AboutDialog.xaml` satır 112-283 — ItemsControl'lü kısayol/diagnostik listeleri

**Kanıt:** XAML: 404+286+89+48 satır; hepsi MainWindow InitializeComponent'ta kurulur. Collapsed öğeler ölçülmez/çizilmez ama DependencyObject ağacı, binding'ler ve template'ler tahsis edilir.

**Etki:** TAHMİN: birkaç MB (yüzlerce-binlerce DependencyObject); açılış süresine de küçük katkı. Boşta RAM'e etkisi küçük; sıralamada en sonda.

**Etki (doğrulayıcı düzeltmesi):** Ölçülmedi; boşta App Private 194,5 MB içinde payı bilinmiyor. Tepside-derleme senaryosuna etkisi yok.

**Çözüm:** Overlay'leri ContentControl/ContentPresenter yuvasına ilk açılışta kurmak (lazy) mümkün, ancak ModalShellTests ve realize testleri x:Name'lere dayanır; kazanç küçük, dokunma riski orta → yalnız ölçüm (GC.GetTotalMemory farkı) kazancı gösterirse yapılmalı.

**Değişmezler:** Modal shell guard'ı (§17.2) ve realize testleri korunmalı.

**Test fikri:** Ölçüm: MainWindowHost realize sonrası GC.GetTotalMemory(true) — overlay'ler dahil/hariç fark.

**Doğrulayıcı notu:** MainWindow.xaml:270-294 dört overlay doğrudan örneklenir (SettingsDialog 404, AboutDialog 286, NotesDialog 89, UpdateRestartScreen 48 satır — sayılar doğru). Kozmetik sıralaması ve 'ölçmeden dokunma' sonucu doğru. Not: Collapsed öğeler ölçülmediği için kontrol şablonları açılmaz; maliyet yalnız XAML'de yazılı mantıksal ağaçtır — 'birkaç MB' üst tahmindir.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.App/MainWindow.xaml.cs:579 ve 598-606 + Console/ConsoleBatchRouter.cs:24-28: konsol pompası pencere görünürlüğüne bakmaz — tepsideyken de her batch Dispatcher.InvokeAsync ile AvalonEdit'e Insert + TrimToRenderSlice (ConsoleView.xaml.cs:390-399 GetText/Remove) + _backlogLines.AddRange yapar; VM tamponu (_runText) zaten her satırı tuttuğu için gizliyken AppendBatch atlanıp pencere gelince ShowRunDocument ile tek seferde kurulabilir (tepside UI işi + üçüncü kopya birlikte kalkar).
- src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2790-2796: CountLines(StringBuilder) indeksleyiciyle sayar; çok parçalı StringBuilder'da maliyet uzunluk × parça sayısıdır — bugün yalnız 2995'teki tek parçalı kopya sayesinde hızlı, D2-5 düzeltmesi bunu UI thread donmasına çevirir.
- src/BuildOrchestrator.Supervisor/SupervisorHost.cs:376-381: proje-log chunk'ları koşu event kanalını atlayıp doğrudan writer'a yazılır; canlı satır ↔ sonuç olayı ↔ chunk sırası garanti değildir — _liveLines'ı erken bırakan her düzeltme bu yarışı testle pinlemeli.
- src/BuildOrchestrator.Core/State/BuildStateStore.cs:206-208: 148 KB'lık defter UTF-16'da ~297 KB olduğundan her Upsert'in okuma ve serialize string'i LOH'a düşer (Upsert başına 2 LOH tahsisi); bulgu bunu 'gen0 çöp' diye sınıflamış.
- src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs:45: Stale() şema kontrolü yapmadan eski şemalı girişi döndürür ('dosya kayboldu' yarışı) — 'Schema 0 girişler asla servis edilmez' iddiasının istisnası; şema budaması bu yedeği de kaldırır.
- src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs:291: pencereye dönüş HEAD değişmese de tam Sync atar (yalnız 5 s kapısı, satır 267); ölçümde tek dönüş Supervisor'da 610 Mcycles/s + Private +91 MB — tepsiden sık dönen kullanıcıda D2-2 maliyetinin çarpanı budur (spec karar 11; sıklık kullanıcı kararı).
- src/BuildOrchestrator.Core/State/BuildDurationPersister.cs:36-50: src altında çağıranı yok (grep yalnız tanımı buluyor); Load + Upsert (içinde ikinci Load) çift parse deseni taşıyan ölü kod — dağınıklık, kaldırılabilir ya da testlerde kullanılıyorsa belgelenmeli.

**Temiz bulunan alanlar:**
- Konsol belgesi sınırlı: ConsoleView.AppendBatch her batch'te belgeyi son 200 satıra kırpar (ConsoleView.xaml.cs:37, 290-306, 390-399); AvalonEdit TextDocument büyümez.
- Event stream sınırlı: StreamEvents 150 satırla front-trim edilir (RunViewModel.Stream.cs:346-347); yeni işlemde RemoveAt ile boşaltılır (Reset yok, container churn yok).
- ConsoleBatcher boşta uyanmaz: tick önce satır bekler (ConsoleBatcher.cs:101-108); batch StringBuilder yalnız satır varsa kurulur — tepside boşta tahsis yok.
- NdjsonReader sabit 64 KB tampon + tek yeniden kullanılan MemoryStream (NdjsonFraming.cs:35-36), 1 MiB satır tavanı (MaxLineBytes) — okuma tarafında sınırsız büyüme yok.
- Supervisor event kanalı unbounded ama tek hızlı tüketici (PumpEventsAsync, RunCoordinator.cs:652-663): stdout koptuğunda 'broken' ile drenaj sürer, kanal birikmez; App tarafı ProjectLogEvent'i marshal etmeden tüketir (UI thread'ine bağlı değil).
- Supervisor proje loglarını bellekte TUTMAZ: ProjectLogFile satırı doğrudan diske yazar (RunLogWriter.cs:136-144), snapshot yalnız istek anında okunur; RunLogWriter Dispose'da dosyalar kapanır.
- Koşu-ömürlü Supervisor nesneleri bırakılıyor: _logs = null (RunCoordinator.cs:1056, 2337), _scheduler/_wake = null (finally), RunContext yerel — koşular arası retained set küçük (GC.Collect gelmediği için working set'in kalması D2-3, sızıntı değil).
- GraphView graf yeniden kurulurken katmanları Clear eder ve animasyon saatlerini bırakır (GraphView.xaml.cs:754-762, ReleaseBeadsClock/ReleaseEdgeFlowClock); düğüm başına Grid + 4-5 primitif, düğümlerde BitmapCache/Effect yok (DropShadowEffect yalnız popover/dialog/tray göstergesinde: Tokens.xaml:229-231, TrayBuildIndicator.xaml:216,253).
- Gömülü fontlar (7 OTF, ~1,18 MB, Fonts/) process başına bir kez yüklenir; küçük ve kaçınılmaz.
- Velopack UpdateManager Lazy (VelopackUpdater.cs:18,30), kontrol 5 s sonra ve 4 saatte bir (UpdateService.cs:21-22,43); kurulu olmayan kopyada hiç kurulmaz — boşta bellek bırakmaz.
- build-state.json'da test tortusu yok (196 giriş, 0 temp); source-hash-cache'te yalnız 34 temp giriş (24.588 içinde önemsiz).
- Test izolasyonu bugün kapalı: SupervisorSandbox + SupervisorIsolationGuardTests (83942f7, 2026-09-19) gerçek motoru --logs'suz başlatan testi yakalar; evaluation-cache'teki temp girişlerin son tarihi 2026-09-19 — sonrasında yeni tortu yok.
- SourceHashCache.Flush racy girdileri dışarıda bırakır ve atomik yazar; IsCached yalnız stat yapar — Prefill 16 kanallı paralel, bellekte ekstra dosya içeriği tutmaz (hash hesaplanınca byte[] hemen çöp).

**Açık sorular:**
- Koşu anlatısı (run belgesi) koşu bittikten sonra da TÜM MSBuild satırlarını bellekte tutmalı mı (bugünkü tasarım), yoksa 'son N bin satır bellekte, gerisi diskteki logdan' kabul edilebilir mi? D2-1'in ne kadar derine ineceğini belirler.
- 'Rahatlama'yı Task Manager'daki Working set sütunundan mı okuyorsunuz? Öyleyse GC.Collect'e ek EmptyWorkingSet (kozmetik ama görünür) eklenir; değilse yalnız GC adımı yeter.
- Optimize'ın kök DIŞINDAKİ (Temp yollu, eski şemalı) evaluation-cache girişlerini de budaması kabul mü? ARCHITECTURE §16 bugün 'kök kapsamlı' der; şema budaması ona ek bir kural olur.
- Pencereye her dönüşte (5 s sessizlikten sonra) otomatik Sync bilinçli mi? D2-2 çözülmeden her dönüş ~70 MB çöp + 12 MB disk yazımı demek; çözülürse maliyet düşer ama sıklık sorusu ayrı.
- Sync ve koşu sırasında Supervisor/App working set tepe değerleri ve koşu sonrası boşta değerler hiç ölçüldü mü? Analiz sırasında process çalışmıyordu; rakamlar hesapla tahmindir — kabulden önce bir ölçüm koşusu (Task Manager + GC.GetGCMemoryInfo) gerekir.


### D10-ipc-supervisor-cpu

#### D10-1 — Her MSBuild satırı ayrı projectLog mesajı: koşu başına ~80K mesaj, ~42 MB tel hacmi; zarfın ~%30'u tekrar eden runId+projectId

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) RunCoordinator.cs:2130-2134 (Emit), :2064-2068 (InvokeOnceAsync onLine lambda), :652-663 (PumpEventsAsync); IpcMessages.cs:302; EngineHost.cs:172-198
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2129-2134 — Emit: her satır için log.AppendLine + yeni ProjectLogEvent(runId, projectId, lineNumber, text) kanala yazılır
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2109-2113 — InvokeOnceAsync onLine lambda'sı: satır başına Emit + observeLine
- `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` satır 302 — ProjectLogEvent(string RunId, string ProjectId, int LineNumber, string Text) — her mesaj 36 karakterlik runId + tam csproj yolu (~90 karakter) taşır
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 652-663 — PumpEventsAsync: kanaldan tek tek okuyup her event için writer.WriteAsync
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 172-198 — App okuma döngüsü: mesaj başına Deserialize + EventReceived

**Kanıt:** Gerçek koşu run-20261001-010430-305: 33 proje logu toplam 79.734 satır / 27,4 MB (ortalama 349 byte/satır, en uzun satır 44.724 karakter), decision.log 196 satır → koşu boyunca ~79,5K projectLog mesajı, 184 s'de ortalama 433 mesaj/s. Build koşusu 00:56: 22.624 satır / 7,8 MB / 79 s (286 mesaj/s). Zarf: {"type":"projectLog","runId":"<36>","projectId":"<~90 karakter yol>","lineNumber":N,"text":...} ≈ 150 byte/mesaj → ~12 MB; JSON kaçış +2,4 MB (D10-7). Tel toplamı ≈ 27,4+12+2,4 ≈ 42 MB (~230 KB/s).

**Etki:** Mesaj başına Supervisor'da serialize + 2 syscall + semaphore, App'te read/IndexOf/Deserialize/lock/list-add ≈ toplam 20-40 µs (tahmin) → koşu başına iki süreçte toplam ~2-3 s CPU (tahmin; 184 s'lik koşunun ~%1-2'si, tek çekirdek). Resolve süresinin sebebi DEĞİL (tur-2 üye derleme toplamı 151 s csc'dedir) ama bellek (D10-4) ve UI belge churn'ü (D10-5) bu hacimle doğru orantılı büyür; batch'lemek hepsini birden küçültür.

**Etki (doğrulayıcı düzeltmesi):** Olculen kosuda ~160 mesaj/s; IPC mesaj yolu iki surecte toplam <%1 cekirdek (tahmin, ust sinir Supervisor'in olculen %1-5'i). App'in tepside 1,44 Gcycles/s'lik yukunu ACIKLAMAZ. Asil maliyet hacmin App'te biriktirilmesi (D10-4) ve belgeye basilmasidir (D10-5).

**Çözüm:** Contracts'a additive bir 'projectLogBatch' event'i ekle: ProjectLogBatchEvent(RunId, ProjectId, FirstLineNumber, IReadOnlyList<string> Lines). Zamanlayıcı YOK: PumpEventsAsync kanaldan TryRead ile o an bekleyen ardışık aynı-proje projectLog'larını tek batch'e katlar (yük yokken 1 satır = 1 mesaj, latency değişmez; yük altında doğal batch), araya başka event girince batch kapanır → FIFO ve 'runStarted→projectStarted*→sonuç*→runCompleted' sırası aynen korunur. App tarafı: OnEvent'te batch'i satır satır mevcut OnProjectLog'a açar (LineNumber = FirstLineNumber+i), T28 dikişi (LineNumber<=ThroughLineNumber) hiç değişmez. Eski 'projectLog' tipi yerinde kalır (tek satırlık batch için de kullanılabilir). Sözleşme riski: App ve Supervisor aynı Velopack paketinde birlikte dağıtılır (SupervisorLayout, supervisor\ klasörü) → sürüm ayrışması yok; IpcMessagesTests.Event_roundtrip_all_types yeni tipi de kapsayacak şekilde güncellenir.

**Çözüm (doğrulayıcı düzeltmesi):** projectLogBatch sozlesme degisikligi YAPILMAMALI (orta efor, olculebilir kazanc yok). Hacim kaynakta azaltilir (D10-6); App tarafinda event nesnesi saklanmaz (D10-4).

**Değişmezler:** stdout yalnız NDJSON korunur; event sırası (ARCHITECTURE 1413) korunur; planlama Core'da kalır; disk logu değişmez.

**Test fikri:** RunCoordinatorTests: sahte invoker 5.000 satır üretsin, kanalda birden çok mesaj bekletilirken pump'ın ürettiği event sayısı satır sayısından küçük ve satır numaraları/sırası birebir olsun; App RunViewModelStateTests: batch geldiğinde _liveLines ve dikiş sonucu tek-satır yoluyla aynı metni üretsin.

**Doğrulayıcı notu:** Hacim sayilari dogru (yeniden saydim). Ama CPU etkisi tahmin ve olcumle sinirli: olculen Rebuild'de 23.047 mesaj / 142 s = ~160 mesaj/s; mesaj basina 40 us kabul edilse bile iki surecte toplam ~0,9 s CPU = tek cekirdegin ~%0,65'i. Supervisor'in TUM kosu CPU'su zaten %1-5 olculdu. Onerilen 'dogal batch' fiilen calismaz: pump bir mesaji ~10 us'de yazar, satirlar ~6 ms arayla gelir, kanal neredeyse hep bos → TryRead ile birlestirilecek bekleyen mesaj yok; 4 paralel proje araya girdikce batch zaten kapanir. Zarf tekrari (%30) bu yuzden de gitmez. Konum hatasi: onLine lambda'si 2109-2113 degil 2064-2068.

#### D10-2 — NdjsonWriter mesaj başına 2 ayrı pipe write + flush: koşu başına ~160K syscall

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Contracts/Ipc/NdjsonFraming.cs` satır 17-30 — WriteCoreAsync: SerializeToUtf8Bytes → gate → stream.WriteAsync(payload) → stream.WriteAsync(NewLine) → FlushAsync
- `src/BuildOrchestrator.Supervisor/Program.cs` satır 20-22, 46 — stdout = Console.OpenStandardOutput() (tamponsuz ConsoleStream) — her WriteAsync doğrudan WriteFile
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 652-663 — Pump event başına WriteAsync çağırır; kanalda bekleyen mesaj olsa da birleştirmez

**Kanıt:** NdjsonFraming.cs:25-27 payload ve '\n' için iki ayrı WriteAsync + FlushAsync; stream Program.cs:20'deki ConsoleStream olduğundan her WriteAsync bir WriteFile syscall'ıdır (tampon yok, Flush no-op). 79,5K mesaj × 2 = ~159K syscall/koşu; App'in stdin ucu (EngineHost.cs:128) da aynı yazıcıyla yazar ama komut sayısı azdır.

**Etki:** Syscall başına ~3-8 µs (tahmin) → koşu başına ~0,5-1,3 s Supervisor CPU; ayrıca her mesaj için ayrı byte[] payload tahsisi (SerializeToUtf8Bytes) ve SemaphoreSlim al/bırak.

**Etki (doğrulayıcı düzeltmesi):** Olculen Rebuild'de ~46K ek syscall/kosu (koddan); sure tahmini <0,4 s / 142 s. Supervisor kosu CPU'su %1-5 olculdu — bunun bir parcasi.

**Çözüm:** (a) Tek write: Utf8JsonWriter ile ArrayBufferWriter<byte>/PooledByteBufferWriter'a serialize et, sonuna '\n' ekle, TEK WriteAsync (NewLine dizisi ve ikinci write gider). (b) Pump'ta yazı birleştirme: PumpEventsAsync bir event'i yazdıktan sonra reader.TryRead ile hemen sıradakileri alıp aynı tamponda (üst sınır ör. 64 KB) biriktirir, kanal o an boşalınca tek write yapar — sıra ve 'kanal boşken anında gönder' latency'si korunur; tampon sınırı MaxLineBytes kontrolünü satır başına yapmaya devam eder. NdjsonFramingTests yalnız roundtrip/oversize pinler, write sayısını pinlemez → test değişmez, sadece yeni bir 'çok mesaj tek write' testi eklenir.

**Çözüm (doğrulayıcı düzeltmesi):** Yalniz (a): payload + '\n' tek tamponda tek WriteAsync (kisa, risksiz). (b) birlestirme eklenmesin.

**Değişmezler:** Satır bütünlüğü (tek yazıcı + kilit) korunur; MaxLineBytes 1 MiB kontrolü mesaj başına kalır; okuyucu tarafı değişmez.

**Test fikri:** NdjsonFramingTests: sayan bir Stream sarmalayıcısıyla 3 mesaj yazıldığında Write çağrı sayısı ≤ 3 (tek write/mesaj) ve okuyucu üçünü de sırayla çözsün; pump birleştirme için RunCoordinatorTests'te bloklanan bir stream ile 100 event kuyruklanınca tek çağrıda ≥2 satır yazıldığı doğrulansın.

**Doğrulayıcı notu:** Kod dogru: NdjsonFraming.cs:25-27 iki WriteAsync + FlushAsync; stream Program.cs:20 ConsoleStream. Ama etki tahmin: olculen kosuda 23.047 mesaj x 2 = ~46K syscall / 142 s; 8 us'den ~0,37 s = %0,26 cekirdek. (b) pump birlestirme D10-1 ile ayni nedenle etkisiz (kanal neredeyse hep bos).

#### D10-3 — Proje logu satır başına senkron flush (AutoFlush=true): 27 MB, ~350 byte'lık 80K WriteFile

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: dusuk [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 124 — ProjectLogFile ctor: new StreamWriter(FileStream(...)) { AutoFlush = true }
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 136-144 — AppendLine: her satır WriteLine → AutoFlush → FileStream.Flush → WriteFile
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 146-155 — Snapshot() zaten kilit altında açıkça _writer.Flush() yapar — T28 dikişi AutoFlush'a bağlı değil
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2132 — Emit → log.AppendLine — MSBuild pump thread'inde senkron

**Kanıt:** RunLogWriter.cs:124 AutoFlush=true; :141 WriteLine. Gerçek koşuda 79.734 satır → 79.734 flush; en büyük log 2BD97A137DE5F0EC.log 11.969 satır / 4,2 MB (Types.UsedCars). Snapshot (:150) kendi Flush'ını çağırdığı için canlı okuma doğruluğu AutoFlush'sız da korunur; Dispose (:163) StreamWriter'ı flush eder.

**Etki:** Satır başına dosya syscall'ı (+NTFS) ~5-15 µs (tahmin) → koşu başına ~0,4-1,2 s CPU MSBuild pump thread'lerinde; bu thread'ler onLineLock altında olduğu için (MsBuildInvoker.cs:139-141) stdout/stderr pump'ları birbirini de bekler. AutoFlush'ın tek kazancı Supervisor çökerse projenin son ≤4 KB satırının diskte olması; o proje zaten in-flight ledger ile 'kesildi' sayılır.

**Etki (doğrulayıcı düzeltmesi):** Satir basina 1 WriteFile (olculen kosuda 23.047, cycles'ta 79.734 — koddan); CPU tahmini <0,4 s/kosu. Zayif diskte (HDD) etkisi olculmedi.

**Çözüm:** AutoFlush=false (FileStream/StreamWriter varsayılan tamponu, 4-16 KB) + Snapshot'taki mevcut Flush ve Dispose flush'ı yeterlidir; istenirse proje sonunda (InvokeOnceAsync dönüşünde) açık Flush eklenir. decision.log'un AutoFlush'ı (RunLogWriter.cs:24) KALIR — 196 satır, ucuz ve çökme kaydıdır.

**Çözüm (doğrulayıcı düzeltmesi):** Varsayilan: dokunma. Yapilacaksa AutoFlush kapatilip proje sonunda + Snapshot'ta flush yeterli degil; kesilme senaryosu icin kullanici karari gerekir (log kuyrugu kaybi kabul mu).

**Değişmezler:** Disk logu 'tek gerçek kaynak' [D4] korunur; getProjectLog anlık görüntüsü Snapshot'ın açık Flush'ı sayesinde aynı kalır; OutDir'e dokunulmaz.

**Test fikri:** ProjectLogStreamTests mevcut 'Running_project_log_snapshots_atomically...' zaten Snapshot'ın flush'ını doğrular; ek: AutoFlush kapalıyken AppendLine sonrası dosya boyutu artmayabilir ama Snapshot metni tüm satırları içerir.

**Doğrulayıcı notu:** Kod dogru: RunLogWriter.cs:124 AutoFlush=true, :141 WriteLine, :150 Snapshot kendi Flush'ini yapiyor; getProjectLog canli yolu yalniz Snapshot'tan gecer (RunCoordinator.cs:221). Ama etki tahmin ve kucuk: olculen kosuda 23.047 flush; 15 us'den ~0,35 s / 142 s. Karsiliginda App oldurulunce (Supervisor 81 ms'de job ile gider) ucustaki projelerin son tampon kadar satiri diske yazilmaz — ARCHITECTURE.md:1417 'the disk log is the real record' [D4] ile gerilim; kesilen kosunun logu tam da teshis icin gereken seydir.

#### D10-4 — App her log satırının 3-4 kopyasını koşu boyunca tutuyor (_liveLines, _runText, ConsoleView _backlogLines, _projectText): cycles koşusunda ~165 MB (tahmin)

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 440, 2696-2700 — _liveLines: proje başına List<ProjectLogEvent> — her event nesnesi (runId/projectId referansı + Text) koşu sonuna kadar tutulur, biten projeler için de
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 438, 2703 — _runText StringBuilder: TÜM projelerin tüm satırları, sınırsız
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 104, 300-306 — _backlogLines: belgeden kırpılan her canlı satır SplitLines ile YENİ string olarak eklenir — koşu boyunca büyür
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2996-2998 — _liveLines'ın tek tüketicisi: chunk gelince LineNumber > ThroughLineNumber olanları dikmek
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2798, 2820-2829 — GetRunDocumentText/SeedRunDocument: _runText.ToString() ile tüm metnin kopyası, ardından ConsoleView.ShowRunDocument (:637-642) SplitLines ile bir kopya daha

**Kanıt:** 27,4 MB UTF-8 log ≈ 27M karakter → UTF-16 string olarak ~55 MB. Kopyalar: _liveLines (55 MB string + ~80K × ~56 B event nesnesi ≈ 4-5 MB), _runText (55 MB), _backlogLines (kırpılan satırların yeni kopyaları, 55 MB) → ~165-170 MB (tahmin); Build koşusunda ~45-50 MB. ClearConsoleForNewOperation (:2744-2746) yalnız yeni işlemde temizler; koşu bittikten sonra bellekte kalır. Önceki rapor da '_liveLines biten projelerin olaylarını tutuyor' demişti — hâlâ öyle (:2696-2700, terminal olayda silme yok).

**Etki:** Koşu sonrası App RSS'i kalıcı olarak onlarca-yüz MB yüksek kalır (kullanıcının 'bittiği anda cihaz rahatlamalı' beklentisiyle çelişir); SeedRunDocument'ta 55 MB ToString + 80K SplitLines UI thread'inde (applyNow) → proje kartından anlatıya her dönüşte 50-200 ms takılma (tahmin).

**Etki (doğrulayıcı düzeltmesi):** Olculen Rebuild (7,7 MB log, 23.047 satir): 3 metin kopyasi ~46 MB + olay basina runId/projectId kopyalari ~8 MB = ~54 MB (hesap) — olculen App Private artisinin (194 → 381 MB = +187 MB) ~%29'u; kalan ~130 MB'i bu bulgu ACIKLAMAZ. Cycles kosusu (27,4 MB log): ~165 MB + ~28 MB (hesap, olculmedi). Bellek yeni isleme kadar geri verilmez (olcumle uyumlu: kosu bitince ve tepside inmedi). 50-200 ms takilma rakami tahmin.

**Çözüm:** (1) _liveLines: projectSucceeded/Failed/Skipped geldiğinde _liveLines.Remove(projectId) (biten projenin logu diskten tam gelir, dikiş ihtiyacı kalmaz); chunk geldiğinde LineNumber<=ThroughLineNumber olanları listeden at. (2) _runText ve _backlogLines aynı içeriğin iki kopyası: VM'de List<string> (satır listesi) tut, ConsoleView backlog'unu bu listeden BESLE (kopyasız, kopya yasağıyla da uyumlu); SeedRunDocument yalnız son RenderSliceLines satırını Join edip belgeye koyar, ToString+SplitLines gider. (3) Koşu bittiğinde (runCompleted/runStopped) tampon boyutu için üst sınır (ör. son 50K satır) — kullanıcı geçmişi hâlâ diskteki proje logundan (getProjectLog) görür.

**Çözüm (doğrulayıcı düzeltmesi):** (1) _liveLines'ta ProjectLogEvent yerine (int LineNumber, string Text) sakla — runId/projectId kopyalari gider. (2) Terminal olayda (projectSucceeded/Failed/Skipped) _liveLines.Remove(projectId): terminal olay UI thread'ine marshal'li, log satirlari reader thread'inde islenir; silme _gate altinda ve yalniz GERCEK terminalde yapilmali (cycle uyesi turlar arasi acik kalir — CycleMemberHeld'de silinmez). (3) _runText + backlog tek satir listesine indirilir. Ust sinir (50K satir) kullaniciya gorunen davranis degisikligi — karar gerekir.

**Değişmezler:** T28 dikişi (satır no eşleşmesi) korunur; konsol 200 satırlık render dilimi (design §2.5) korunur; disk logu değişmez.

**Test fikri:** RunViewModelStateTests: 3 projeye 1.000'er satır, ikisi bitince _liveLines yalnız uçuştaki projeyi içersin (internal sayaç); SeedRunDocument'ın applyNow'a verdiği metnin uzunluğu render dilimiyle sınırlı olsun; bellek için 100K satırlık sahte koşu sonrası GC.GetTotalMemory farkı eşiği (ölçüm testi, BO_ ortam kapılı).

**Doğrulayıcı notu:** Kod dogru: _liveLines yalniz ClearConsoleForNewOperation'da temizleniyor (RunViewModel.cs:2744; terminal olayda silme yok, tek tuketici :2996-2998); _runText sinirsiz (:2703); ConsoleView backlog'u kirpilan satirlari yeni string olarak ekliyor (:300-305, :937-941); SeedRunDocument tum metni ToString ediyor (:2825) ve ResetRunDocument SplitLines yapiyor (:638). Bulgu ajani bir kalemi eksik saymis: her ProjectLogEvent KENDI deserialize edilmis RunId ve ProjectId string kopyasini tutar (System.Text.Json intern etmez) → olay basina ~56 B degil ~300-350 B.

#### D10-5 — Anlatı modunda TÜM MSBuild satırları AvalonEdit belgesine giriyor, 50 ms sonra baştan kırpılıyor: belge churn'ü + kırpılan metnin iki kez kopyalanması

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) RunViewModel.cs:2713-2714; MainWindow.xaml.cs:580, 598-606; ConsoleView.xaml.cs:276-319, 390-399, 440-454, 937-941
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2714-2717 — OnProjectLog: ActiveProjectId null iken her satır _console.Post(e.Text)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 580, 598-606 — Batch başına Dispatcher.InvokeAsync → AppendConsoleBatch → AppendNarrativeBatch → AppendBatch
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 274-312 — AppendBatch: document.Insert(tüm batch) → TrimToRenderSlice → GetText(0,len) + Remove + LastLines(removed) (SplitLines tüm kırpılan satırları ayrı string yapar) + _backlogLines.AddRange
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 390-399, 937-941 — TrimToRenderSlice ve LastLines: kırpılan metin önce string olarak kopyalanır, sonra satırlara bölünür
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 440-455 — AppendNarrativeBatch: body/newest/prefix substring'leri hesaplanır ama yalnız AppendBatch(text) çağrılır (ölü hesap)

**Kanıt:** Cycles koşusunda ortalama 433 satır/s, 4 paralel MSBuild patlamasında binlerce satır/s; 50 ms pencerede 20-200+ satır tek batch olarak belgeye Insert edilip aynı çağrıda 200 satır sınırına kırpılıyor (ConsoleRenderSlice.DefaultMaxLines=200). Kırpılan her satır: document.GetText kopyası + SplitLines kopyası + backlog'a ekleme. AppendNarrativeBatch:449-453 newest/prefix/lineCount hesaplar, hiçbirini kullanmaz.

**Etki:** UI thread'inde saniyede ~20 belge Insert+Remove (AvalonEdit satır ağacı güncellemesi, TextChanged/anchor işlemleri) ve batch başına 2× metin kopyası; pencere arkadayken de sürer (render değil ama belge işlemi). Ölçülmedi — tahmin: koşu başına 1-3 s UI thread CPU; büyük batch'lerde tek kare 10-30 ms.

**Etki (doğrulayıcı düzeltmesi):** Tepsideki derlemede UI thread'inde saniyede en cok 20 belge guncellemesi (koddan). Olculen tepsi-kosu yuku 1,44 Gcycles/s'nin ne kadarinin bundan geldigi OLCULMEDI; '1-3 s UI CPU' ve '10-30 ms kare' rakamlari tahmin. ETW/dotnet-trace ile UI thread ayristirmasi gerekir.

**Çözüm:** (1) Kırpmayı Insert'ten ÖNCE yap: batch'in satır sayısı ≥ RenderSliceLines ise belgeye yalnız SON 200 satırı ekle, öncekileri doğrudan backlog'a ver (belge round-trip'i yok); belge ≤200 satır kaldığı için TrimToRenderSlice çoğu batch'te 0 döner. (2) TrimToRenderSlice'ta kırpılan satırları GetText+SplitLines yerine DocumentLine üzerinden doğrudan al (tek kopya). (3) AppendNarrativeBatch'teki kullanılmayan substring hesabını kaldır. (4) Pencere gizli/tepsideyken (IsVisible false) batch'i belgeye basmayıp yalnız backlog'a biriktir, görünür olunca son 200'ü tek seferde kur — §14.5 IsVisible kapısıyla aynı desen.

**Çözüm (doğrulayıcı düzeltmesi):** Oncelik (4): pencere gorunmezken batch belgeye basilmaz (MainWindow'da IsVisible kapisi, batch dusurulur); gorunur olunca mevcut SeedRunDocument/SeedProjectDocument + reseed nesli ile belge tek seferde kurulur — yeni bir backlog yolu acilmaz. Bu, D10-4(2) ile birlikte yapilmali: aksi halde gosterimde 27 MB'lik _runText.ToString + SplitLines tek karede odenir. (3) olu hesap silinir. (1)-(2) ikincil.

**Değişmezler:** Render dilimi 200 satır ve 'geçmiş erişilebilir' (backlog) kuralı korunur; ConsoleBatcher sözleşmesi (tek flush/batch) değişmez; reseed nesil guard'ı dokunulmaz.

**Test fikri:** ConsoleView realize testi (STA): 1.000 satırlık tek batch AppendBatch sonrası document.LineCount ≤ 201, backlog 800 satır, ve TextDocument'e yapılan Insert çağrısının metin uzunluğu ≤ son 200 satır (TextChanged ile ölç).

**Doğrulayıcı notu:** Kod dogru: OnProjectLog anlati modunda her ham satiri Post ediyor (RunViewModel.cs:2713-2714); AppendBatch once Insert sonra TrimToRenderSlice (ConsoleView.xaml.cs:281-306, :390-399); AppendNarrativeBatch:445-451 body/newest/prefix/lineCount hesaplayip kullanmiyor (olu kod, :453 yalniz AppendBatch). MainWindow.AppendConsoleBatch (:598-606) IsVisible'a BAKMIYOR → tepsideyken de 50 ms'de bir Dispatcher turu + AvalonEdit Insert/Remove + ScrollToEnd calisiyor. CPU payi olculmedi.

#### D10-6 — MSBuild çıktı hacminin %70'i tekrar eden uyarı: 36.291 satır Microsoft.Common.CurrentVersion.targets (MSB3277 tarzı) + ~12K satır NU1903 (Newtonsoft.Json 9.0.1) — verbosity bayrağı yok

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) MsBuildArguments.cs:22-28; CompilerReferences.cs:18-34; RunCoordinator.cs:1625-1629
- `src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs` satır 22-28 — Build argümanları: -clp:Summary -nologo; -v/-verbosity yok (MSBuild varsayılanı normal), -warnAsMessage/-nowarn yok
- `src/BuildOrchestrator.Core/MsBuild/CompilerReferences.cs` satır 18-23, 27-34 — Döngü kanıtı csc komut satırını (csc.exe ... /reference:) loglardan okur — verbosity düşürülürse bu satır kaybolabilir
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1623-1627 — ObserveCompilerLine: her satırda CompilerPattern.IsMatch

**Kanıt:** run-20261001-010430-305 tüm loglar: 36.291 satır 'C:\Program Files\Microsoft Visual Studio\18\Enterp...' ile başlıyor (CurrentVersion.targets uyarıları) = toplamın %45'i; Types.UsedCars logu (2BD97A137DE5F0EC.log) 11.969 satırın 11.902'si (5.954+5.948) 'warning NU1903: Newtonsoft.Json 9.0.1 ... güvenlik açığı' çifti; Build koşusunda (00:56) 22.624 satırın 7.484'ü CurrentVersion.targets. MsBuildArguments.cs:24-28'de verbosity yok → 'normal'.

**Etki:** D10-1..5'in tüm maliyetleri (IPC, disk flush, App bellek, belge churn) bu satırlarla orantılı: uyarılar giderse cycles koşusunun mesaj/byte hacmi ~%70 düşer. Logs klasörünün 1,5 GB'ının da ana kaynağı budur (D4 ile ortak).

**Etki (doğrulayıcı düzeltmesi):** Cycles kosusu: satirlarin %70,6'si iki uyari (MSB3277 36.304 + CS1591 20.022). Rebuild: MSB3277 %32. D10-4/5 maliyetleri ve logs klasoru buyumesi bu hacimle orantili.

**Çözüm:** İki katman: (a) Kaynakta (OSYS reposu, kullanıcı kararı): MSB3277 = çakışan assembly sürümleri (binding redirect / referans hizalama), NU1903 = Newtonsoft.Json 9.0.1 yükseltme ya da packages.config projesinde NuGetAudit kapatma (-p:NuGetAudit=false restore argümanına eklenebilir — yalnız restore prologu, derlemeyi etkilemez). (b) Araçta ŞARTLI: '-warnAsMessage:MSB3277' MSBuild uyarısını mesaja çevirir; normal verbosity'de mesajın hâlâ yazılıp yazılmadığı DOĞRULANMALI (ölçüm gerekir); '-v:minimal' ve '-clp:ErrorsOnly' csc komut satırını kaybettirdiği için (CompilerReferences.Parse döngü kanıtı) KULLANILMAZ. Verbosity'i düşürmeden önce csc satırının hangi importance ile loglandığı bir spike ile ölçülmeli.

**Çözüm (doğrulayıcı düzeltmesi):** NuGetAudit=false onerisi gecersiz (30 satir). Hedef iki kod: MSB3277 ve CS1591. (a) OSYS reposunda (kullanici karari): CS1591 icin NoWarn/GenerateDocumentationFile, MSB3277 icin referans hizalama. (b) Aracta: -warnAsMessage:MSB3277;CS1591 — normal verbosity'de dusuk onemli mesaja donup yazilmadigi tek projelik spike ile OLCULMELI; -v:minimal kullanilmaz. Arguman listesi §9.2'de sabitlenmis ('v1 flag'leri SABIT') → degisiklik kullanici onayi + dokuman guncellemesi ister.

**Değişmezler:** Shell-out MSBuild.exe, argüman sözleşmesi §9.2 (UseSharedCompilation=false, nodeReuse:false) korunur; döngü kanıtı (csc /reference satırı) kaybolmamalı — bu yüzden verbosity düşürme şartlı.

**Test fikri:** MsBuildArgumentsTests: restore argümanına NuGetAudit=false eklenirse pinle; spike: tek OSYS UI projesinde -warnAsMessage:MSB3277 ile log satır sayısı ve csc satırının varlığı karşılaştırılır (Acceptance kategorisi).

**Doğrulayıcı notu:** Ana iddia olcumle dogru ama ikinci kaynak YANLIS adlandirilmis. Yeniden sayim: MSB3277 36.304 satir (%45,5; ~10,96 MB = byte'in %40'i) — dogru. Types.UsedCars logundaki 11.902 satir NU1903 DEGIL, CS1591 (eksik XML yorumu); tum kosuda NU1903 yalniz 30 satir, CS1591 20.022 satir. Toplam warning satiri 72.240 / 79.734 (%90,6). Olculen Rebuild'de 11.888 / 23.047 (%52), MSB3277 7.426. Verbosity gecilmedigi dogru (MsBuildArguments.cs:24-28) ve bu ARCHITECTURE.md:1667'de gerekceli bilincli karar (csc komut satiri dongu kaniti).

#### D10-7 — IpcJson kaçış ayarı varsayılan: Türkçe MSBuild metni \uXXXX ile 6 byte'a şişiyor (+~1 MB/koşu), ters bölü zaten 2× (+1,4 MB)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` satır 9-15 — IpcJson.Options: Encoder verilmemiş → JavaScriptEncoder.Default (non-ASCII, +, <, >, & kaçar)
- `src/BuildOrchestrator.Contracts/Ipc/NdjsonFraming.cs` satır 19 — SerializeToUtf8Bytes(message, IpcJson.Options)

**Kanıt:** Cycles koşusu loglarında 494.876 byte non-ASCII UTF-8 (≈250K karakter: 'Oluşturma başarılı oldu', 'Geçen Süre', 'düğümünde', NU1903 metni) → varsayılan encoder her birini \uXXXX (6 byte) yazar ≈ 1,5 MB (ham 0,5 MB); ters bölü sayısı 1.416.106 → '\\' ile +1,4 MB. Toplam kaçış ek yükü ≈ 2,4 MB / 42 MB tel (~%6), artı escape/unescape CPU.

**Etki:** Küçük ama bedava: yerel, güvenilir anonim pipe üzerinde HTML bağlamı yok; relaxed kaçış güvenlidir.

**Etki (doğrulayıcı düzeltmesi):** Cycles kosusunda non-ASCII kacisi ~+1,0 MB tel (hesap); ters bolu ikilemesi (+1,4 MB) relaxed encoder ile de KALIR. Olculen Rebuild'de oransal ~0,3 MB.

**Çözüm:** IpcJson.Options'a Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping ekle (ters bölü kaçışı JSON'un zorunluluğu, kalır). Okuyucu tarafı değişmez (her iki biçim de geçerli JSON).

**Değişmezler:** NDJSON framing (payload'da ham \n olamaz — kaçış kuralı korunur) değişmez.

**Test fikri:** IpcMessagesTests: 'Geçen Süre' içeren ProjectLogEvent serialize edilince payload'da G benzeri kaçış bulunmasın ve roundtrip metni aynı olsun.

**Doğrulayıcı notu:** IpcJson.Options'ta Encoder yok (IpcMessages.cs:9-15) → varsayilan encoder non-ASCII'yi \uXXXX yazar. Sayilar yeniden sayildi: 494.876 non-ASCII byte, 1.416.106 ters bolu — dogru. Varsayilan encoder ayrica ' ve " karakterlerini de 6 byte yazar (CS1591 satirlarinda tirnak cok) — ajan saymamis, ek yuk bir miktar daha buyuk. CPU etkisi ihmal edilebilir.

#### D10-8 — System.Text.Json reflection tabanlı (JsonSerializerContext yok): iki süreçte de ilk mesajda reflection/JIT ısınması, mesaj başına küçük ek yük

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` satır 9-15, 17-30, 243-276 — IpcJson.Options + [JsonPolymymorphic]/[JsonDerivedType] hiyerarşileri, source-gen context yok
- `src/BuildOrchestrator.Contracts/Ipc/NdjsonFraming.cs` satır 19, 52 — Serialize/Deserialize reflection metadata ile

**Kanıt:** Kodda JsonSerializerContext/[JsonSerializable] yok (grep). Contracts 13 komut + 33 event tipi; ilk kullanımda her tip için reflection metadata + IL emit.

**Etki:** Açılışta App ve Supervisor'da onlarca ms (tahmin 20-60 ms, ölçülmedi); mesaj başına fark küçük (metadata cache'lidir). Öncelik düşük — D10-1/2 çok daha büyük kazanç.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. Acilis icin ReadyToRun (D8) daha genis kapsar; bu bulgu ondan sonra yeniden olculmeli.

**Çözüm:** Contracts'a [JsonSourceGenerationOptions(PropertyNamingPolicy=CamelCase, DefaultIgnoreCondition=WhenWritingNull, UseStringEnumConverter=true)] [JsonSerializable(typeof(IpcCommand))] [JsonSerializable(typeof(IpcEvent))] partial class IpcJsonContext ekle; IpcJson.Options.TypeInfoResolver = IpcJsonContext.Default. Polimorfizm attribute'ları source-gen tarafından desteklenir; enum camelCase için JsonStringEnumConverter yerine UseStringEnumConverter + naming policy. Mevcut roundtrip testleri regresyon kapısıdır.

**Değişmezler:** Tel formatı bayt-bayt aynı kalmalı (IpcMessagesTests ile pinli).

**Test fikri:** IpcMessagesTests: mevcut roundtrip testleri + 'Options.TypeInfoResolver source-gen context' pin testi; startup ölçümü BO_ ortam kapılı.

**Doğrulayıcı notu:** src altinda JsonSerializerContext/JsonSerializable yok (grep 0 sonuc). Etki tamamen tahmin (20-60 ms); olculen acilis 1,33-1,38 s icinde payi bilinmiyor.

#### D10-9 — MSBuild stdout pump'ı 1 KB StreamReader tamponu + 4 KB varsayılan anonim pipe: 27 MB için ~27K sync-over-async okuma ve pool thread hop'u

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 205-222 — PumpLinesAsync: StreamReader(bufferSize: 1024) + ReadLineAsync döngüsü
- `src/BuildOrchestrator.Core/ProcessControl/JobProcessLauncher.cs` satır 45-47 — AnonymousPipeServerStream(direction, inheritability) — bufferSize verilmemiş (sistem varsayılanı ~4 KB), overlapped değil
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 157-166 — Yorum: anonim pipe overlapped olamaz → ReadLineAsync fiilen thread-pool thread'inde bloklu ReadFile

**Kanıt:** bufferSize: 1024 (MsBuildInvoker.cs:211); pipe ctor'da tampon boyutu yok (JobProcessLauncher.cs:45-47). Her ReadAsync bir pool iş öğesi + bloklu ReadFile; 27,4 MB / 1 KB ≈ 27K okuma (cycles koşusu), 44.724 karakterlik satır 44 parçada birleştirilir.

**Etki:** Küçük: okuma başına birkaç µs + pool hop; ayrıca 4 KB pipe tamponu MSBuild'in yazmasını okuyucu geç kaldığında bloklar (onLineLock + AutoFlush D10-3 ile birleşince pump'lar birbirini bekler).

**Etki (doğrulayıcı düzeltmesi):** Olculen Rebuild'de en cok ~7,5K-23K okuma (hesap); CPU etkisi olculmedi, kucuk.

**Çözüm:** StreamReader bufferSize 32-64 KB; AnonymousPipeServerStream'in 4 parametreli ctor'u ile bufferSize: 65536 (stdout/stderr için). D10-3 ile birlikte pump thread'inin kilit altındaki süresi kısalır.

**Değişmezler:** Nested job / HANDLE_LIST / CREATE_SUSPENDED protokolü (§4.3) değişmez.

**Test fikri:** MsBuildInvokerTests: sahte child 100 KB tek satır yazdığında onLine tek çağrı ve metin bütün; pipe tamponu büyütülünce mevcut KillMidBuild/CascadeKill testleri yeşil kalır.

**Doğrulayıcı notu:** Kod dogru: MsBuildInvoker.cs:210-211 bufferSize 1024; JobProcessLauncher.cs:45-47 pipe tampon boyutu verilmemis. '27K okuma' bir hesap (27,4 MB / 1 KB); gercek okuma sayisi MSBuild'in yazma parcalarina bagli, olculmedi. Okuyucu hizli oldugu icin 4 KB tamponun MSBuild'i blokladigina dair kanit yok.

#### D10-10 — SanitizeLine her satırda iki kez çağrılıyor (AppendLine içinde ve Emit'te)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) RunLogWriter.cs:141; RunCoordinator.cs:2130-2134
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 141 — AppendLine: _writer.WriteLine(SanitizeLine(text))
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2132-2133 — Emit: log.AppendLine(line) sonra ProjectLogEvent(..., RunLogWriter.SanitizeLine(line)) — aynı dönüşüm ikinci kez

**Kanıt:** İki çağrı da aynı satır üzerinde IndexOfAny taraması yapar; CR/LF içeren satırda iki ayrı Replace kopyası üretilir.

**Etki:** CPU olarak ihmal edilebilir (80K × ~0,1 µs); 'kopya yasak' açısından dağınıklık: aynı dönüşüm iki katmanda.

**Çözüm:** AppendLine sanitize edilmiş metni döndürsün (ör. (int LineNumber, string Text)) ya da Emit önce bir kez sanitize edip AppendLine'a temiz metni versin; tek dönüşüm noktası kalır.

**Değişmezler:** Disk satırı ile canlı event metninin birebir aynı olması (T28) korunur — tek kaynaktan geldiği için daha da garanti.

**Test fikri:** RunLogWriterTests: CR içeren satırda AppendLine'ın döndürdüğü metin == diske yazılan satır == event metni.

**Doğrulayıcı notu:** RunLogWriter.cs:141 ve RunCoordinator.cs:2133 ayni satira iki kez SanitizeLine uyguluyor. CPU ihmal edilebilir; organizasyon bulgusu (ayni donusum iki katmanda). SanitizeLine'in public olmasi bilincli (RunLogWriter.cs:93-95: kopya sanitizer olmasin diye) — duzeltme bu gerekceyi korur.

#### D10-11 — Event kanalı ve konsol kanalı sınırsız: App/pipe geri kaldığında Supervisor belleği koşu boyutu kadar büyüyebilir (bilinçli, ama ölçülmüyor)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 586 — Channel.CreateUnbounded<IpcEvent>
- `src/BuildOrchestrator.App/Console/ConsoleBatcher.cs` satır 47 — Channel.CreateUnbounded<Op>
- `ARCHITECTURE.md` satır 1413-1418 — Bilinçli karar: 'writer never blocks', kanal sonuna kadar boşaltılır

**Kanıt:** Pipe geri basıncı yalnız pump'ı bloklar (PumpEventsAsync), üreticileri değil; App UI thread'i D10-4/5 yüzünden takılırsa Dispatcher kuyruğu ve ConsoleBatcher kanalı büyür. Üst sınır doğal olarak koşunun toplam logu (~42 MB tel eşdeğeri) — sonsuz değil.

**Etki:** Normalde App hızlı okur (ölçülen koşuda takılma kanıtı yok); en kötü durumda tek koşu için onlarca MB geçici bellek. Kararı tersine çevirmeyi ÖNERMİYORUM (sıra garantisi ve 'pump thread bloklanmaz' kuralı doğru).

**Çözüm:** Yalnız gözlem: pump'ta kanal derinliğini (reader.Count) decision.log'a koşu sonunda bir satır olarak yazmak (ör. 'ipc backlog peak=N'); D10-1/2 uygulandığında zaten yük düşer. Şartlı alternatif (önerilmez): yalnız projectLog için BoundedChannel/DropOldest — T28 dikişi ve konsol bütünlüğünü bozar.

**Çözüm (doğrulayıcı düzeltmesi):** Derinlik olculecekse reader.Count degil, yazimda/okumada Interlocked sayac + tepe degeri. Davranis degisikligi yok.

**Değişmezler:** ARCHITECTURE 1413 event sırası kararı korunur.

**Test fikri:** RunCoordinatorTests: yazmayan (bloklu) stream ile 10K satır üretilince koordinatör tamamlanır ve pump kanalı sonuna kadar tüketir (mevcut 'broken' dalı) — zaten var mı kontrol edilir.

**Doğrulayıcı notu:** Iki kanal da sinirsiz (RunCoordinator.cs:586, ConsoleBatcher.cs:47-48) ve bu ARCHITECTURE.md:1413-1418'de bilincli karar; ajan karari degistirmeyi onermiyor. Onerilen olcum yanlis: kanal SingleReader=true ile kurulu; bu kanal turunde Reader.Count desteklenmez (CanCount=false, NotSupportedException) — BCL davranisi, bu repoda calistirilip dogrulanmadi.

#### D10-12 — Pump thread'leri: paralellik 4'te 8'e kadar bloklu pool thread + post-build torun süreçleri pump'ı terk edilmiş bırakabilir

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 143-144, 167, 205-222 — child başına 2 pump task; WaitPumpsBoundedAsync 5 s sonra pes eder ama pump'ı iptal etmez
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 151-166 — Yorum: torun (post-build Exec) pipe ucunu tutarsa pump EOF alamaz, bloklu pool thread torun ömrü boyunca kalır
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1006 — Worker'lar Task.Run ile pool'da

**Kanıt:** Koşu sırasında thread envanteri: 4 worker (çoğunlukla await'te) + 4×2 pump (sync-over-async ReadFile'da bloklu) + event pump + stdin okuyucu (bloklu) ≈ 10-11 aktif thread; Process.WaitForExitAsync RegisterWait kullanır, thread tutmaz. Terk edilmiş pump riski yalnız OSYS post-build olayları uzun ömürlü torun bırakıyorsa gerçekleşir — ölçülmedi.

**Etki:** Normal koşuda sorun yok. Torun senaryosunda pool'un 1-2 thread/s enjeksiyonu yüzünden yeni MSBuild pump'ları gecikebilir (invoke başına ~0,5 s süreç ek yükü gözleminin bir parçası olabilir — doğrulanmadı).

**Çözüm:** Ölçüm: koşu sonunda ThreadPool.ThreadCount ve terk edilmiş pump sayısını decision.log'a yaz; torun varsa OSYS post-build olaylarında süreçlerin MSBuild'den önce bitmesini sağlamak (repo kararı). Kod tarafında mevcut mandal (detached) doğru; ek değişiklik önermiyorum.

**Değişmezler:** Nested job ve 'managed parent-watcher yok' kuralı korunur.

**Test fikri:** Mevcut CascadeKillTests/KillMidBuildTests'e ek: torun pipe ucunu tutarken InvokeAsync DrainWait içinde döner ve ThreadPool.ThreadCount koşu bitince eski değere iner (ölçüm testi).

**Doğrulayıcı notu:** Envanter kodla uyumlu (MsBuildInvoker.cs:143-144, 167, 205-222; RunCoordinator.cs:1006). Terk edilmis pump riski kod yorumunda zaten belgeli (:157-166). Torun etkisi ve 'invoke basina ~0,5 s' baglantisi olculmemis; kod degisikligi onerilmiyor.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2698-2700 — _liveLines tum ProjectLogEvent nesnesini sakliyor; her olay kendi deserialize edilmis RunId + ProjectId (~92 karakter yol) string kopyasini tasir: olay basina ~300-350 B, olculen kosuda ~8 MB, cycles'ta ~28 MB (hesap).
- src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs:27 — -clp:Summary uyarilari kosu sonunda ikinci kez yazdiriyor: girintili (ozet) warning satiri cycles kosusunda 17.984 / 79.734 (%22,6), Rebuild'de 2.238 / 23.047 (%9,7) — log sayimi; src'de ozeti ayristiran kod yok (grep). -clp:NoSummary etkisi spike ile dogrulanmali, §9.2 arguman degisikligi kullanici karari.
- src/BuildOrchestrator.App/MainWindow.xaml.cs:598-606 — AppendConsoleBatch'te pencere gorunurlugu kapisi yok: tepsideki derlemede konsol belgesi 50 ms'de bir guncelleniyor (D10-5'in tepsi boyutu ayri bir bulgu olarak yazilmamis).
- src/BuildOrchestrator.Core/Logs/RunLogWriter.cs:146-154 — Snapshot tum dosyayi (en buyuk log 4,2 MB) yazici kilidi (_gate) altinda okuyor; o sure boyunca ayni projenin MSBuild pump thread'i AppendLine'da bekler. Yalniz kullanici calisan projenin kartina tiklayinca; sure olculmedi.
- src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2825 + Console/ConsoleView.xaml.cs:638 — anlatiya her donuste _runText.ToString (cycles'ta ~27M karakter = ~55 MB LOH tahsisi) + SplitLines UI thread'inde; D10-4 icinde anilmis ama ayri bir UI-donma bulgusu olarak olculmemis.

**Temiz bulunan alanlar:**
- Supervisor koşu dışında BOŞTA gerçekten sıfır CPU: SupervisorHost.cs:93-103 tek döngü, reader.ReadAsync (NdjsonFraming.cs:61) stdin ConsoleStream üzerinde bloklu okuma; Supervisor/Core'da koşu dışı hiçbir timer/poll yok (grep: yalnız retry Task.Delay RunCoordinator.cs:982, Optimize heartbeat, App tarafındaki HeadWatcher). Tek maliyet bir bloklu pool thread'i.
- App EngineHost stderr drain'i (EngineHost.cs:91-103) 4 KB'lık okuma ile ucuz; Supervisor stderr hacmi küçük (RunCoordinator'da yalnız uyarılar + planlama başında bayat-obj satırları) — ölçülebilir bir maliyet yok.
- Motor çıkış izleme (EngineHost.cs:136-145, JobChildProcess.cs:25-38) Process.WaitForExitAsync ile RegisterWait tabanlı — polling/timer yok.
- Event sırası tasarımı (RunCoordinator.cs:586-663, ARCHITECTURE 1413-1418): tek FIFO kanal + tek pump, üretici pump thread'leri hiç bloklanmaz, stdout koparsa koşu devam eder — doğru ve korunmalı.
- ProjectLogEvent App'te marshal edilmeden (MainWindow.xaml.cs:339-343) arka plan thread'inde işleniyor; OnProjectLog (RunViewModel.cs:2694-2718) yalnız kilitli tampon + kilitsiz Channel Post yapıyor, ObservableProperty'e dokunmuyor — satır başına Dispatcher yok.
- ConsoleBatcher (ConsoleBatcher.cs:80-87) boşta uyanmıyor (WaitToReadAsync sonra 50 ms pencere) — önceki geçişin düzeltmesi yerinde; batch başına tek Dispatcher.InvokeAsync (MainWindow.xaml.cs:580).
- CompilerReferences.Parse (CompilerReferences.cs:18-34) [GeneratedRegex] ile derlenmiş; satır başına ilk IsMatch ucuz ('csc.' kelime sınırı), yalnız döngü turlarında çağrılıyor.
- NdjsonReader (NdjsonFraming.cs:33-69) 64 KB tampon, satır başına tek MemoryStream yeniden kullanımı, IndexOf ile ayraç arama — okuyucu tarafı verimli; Deserialize doğrudan span'dan.
- MSBuild invoke başına Task.Run/lock kullanımı (MsBuildInvoker.cs:139-141 onLineLock, detached mandalı) doğru; kill sonrası bounded bekleme hang üretmiyor.
- IPC komut yönü (App→Supervisor) trafik olarak önemsiz: koşu başına onlarca komut; NdjsonWriter'ın 2-write maliyeti (D10-2) burada hissedilmez.

**Açık sorular:**
- OSYS post-build olayları (copy/Exec) MSBuild.exe'den daha uzun yaşayan süreç bırakıyor mu? (MsBuildInvoker.cs:151-166 senaryosu: terk edilmiş pump'lar pool thread tutar; invoke başına ~0,5 s ek yükün bir parçası olabilir — ölçülmeli)
- Anlatı (run) modunda konsolun TÜM projelerin ham MSBuild satırlarını akıtması istenen davranış mı, yoksa yalnız koşu düzeyi satırlar (plan/sonuç) yeterli mi? Cevap D10-5'in kapsamını belirler (ham satırlar yalnız proje seçilince gösterilirse belge churn'ü tamamen kalkar).
- MSB3277 (çakışan assembly sürümleri) ve NU1903 (Newtonsoft.Json 9.0.1) uyarıları OSYS reposunda giderilebilir mi (binding redirect / paket yükseltme / NuGetAudit)? Kaynakta çözüm cycles koşusunun log hacmini ~%70 düşürür ve VS'de de görünür.
- projectLogBatch için App ile Supervisor'ın her zaman aynı paketle dağıtıldığı (Velopack, supervisor\ klasörü) doğrulansın — sürüm ayrışması destekleniyor mu? Destekleniyorsa eski 'projectLog' tipi de üretilmeye devam etmeli.
- Verbosity/-warnAsMessage denemesi için: csc komut satırının (döngü kanıtı) 'normal' dışındaki verbosity'de loglanıp loglanmadığını tek projelik bir spike ile ölçmeye onay var mı?


### D4-build-motoru

#### D4-1 — MSBuild ozet blogu (-clp:Summary) her uyari/hata satirini IKINCI kez basiyor — Resolve kosusunda satirlarin %29'u tekrar

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs` satır 22-28 — Build argumanlari: "-clp:Summary" sabit — konsol logger'in ozet blogu (tum warning/error listesi + sayac + Gecen Sure) acik
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2130-2134 — Emit: her satir disk logu + ProjectLogEvent — ozetteki tekrar satirlar da ayni yoldan gecer
- `tests/BuildOrchestrator.Tests/MsBuild/MsBuildArgumentsTests.cs` satır 26-45 — arguman listesi pinli (degisiklik testi de guncelletir)
- `ARCHITECTURE.md` satır 1640-1676 — §9.2 arguman sozlesmesi — -clp:Summary burada yazili

**Kanıt:** run-20261001-010430-305 (Resolve): 33 proje logu toplam 79.734 satir, 72.240'i 'warning' satiri; bunlarin 23.254'u son 'Olusturma basarili/BASARISIZ' isaretinden SONRA (ozet blogu) — yani inline'da basilmis uyarinin aynisi. Ornek: run-20261001-005657-179/D1E11A4203B0C86A.log (UI.NewSales.Report) 2360 satir; ozet 1253. satirda basliyor, MSB3277 satirlari 1003 once + 1003 sonra (birebir tekrar, 672 KB). Types.UsedCars logu (2BD97A137DE5F0EC.log, 4.2 MB, 11.969 satir): 5947 + 5947.

**Etki:** Resolve kosusu basina ~23 bin fazladan satir: her biri RunLogWriter'da senkron flush (AutoFlush) + bir NDJSON ProjectLogEvent + App tarafinda isleme. Logs klasoru 1.5 GB'in kabaca dortte biri bu tekrarlar (tahmin: uyari satirlari toplam baytin %90'i, ozet payi ~%30). Derleme suresine dogrudan etkisi kucuk (csc paralel kosuyor) ama Supervisor/App CPU'su ve disk yazimi olculebilir sekilde artiyor (Supervisor tarafi tahmin: satir basina flush+serialize ~20-50 us → Resolve basina ~1 s; App tarafi D2/D3 kapsaminda).

**Etki (doğrulayıcı düzeltmesi):** Olculen: Resolve kosusunda satirlarin %29'u (23.254/79.734) tekrar; her tekrar satir disk yazimi + 1 IPC olayi + App'te _liveLines/_runText'e ekleme. Supervisor tarafinda kazanc kucuk (olcum: Supervisor kosu boyunca tek cekirdegin %1-5'i); asil kazanc App tarafinda (olcum: kosuda App %40-100 tek cekirdek, 160 satir/s) ve disk hacminde. '1,5 GB'in dortte biri' ve '~1 s Supervisor CPU' sayilari TAHMIN.

**Çözüm:** "-clp:Summary" yerine "-clp:NoSummary" (tek satir degisiklik; MsBuildArgumentsTests ve §9.2 metni ayni iste guncellenir). Kodda ozet blogunu okuyan hicbir tuketici yok (grep: yalniz CompilerReferences csc /reference: satirini ve CopyContention MSB302x'i okur; ikisi de inline satirdan gelir; 'N Warning(s)'/'Gecen Sure' hicbir yerde parse edilmiyor). Kullanici ozetin sayaci/suresini istiyorsa: decision.log zaten sure basiyor; uyari/hata SAYISI istenirse Emit'te 'warning '/'error ' onekli satirlari sayip ProjectSucceeded/Failed olayina eklemek daha ucuz. Alternatif (daha kirilgan): ozet isaretinden sonra tekrar satirlari arac tarafinda filtrelemek — yerellestirilmis metne bagli, onerilmez.

**Çözüm (doğrulayıcı düzeltmesi):** '-clp:NoSummary' (MsBuildArgumentsTests + ARCHITECTURE §9.2 ayni iste). Kullanici karari gerekir: NoSummary ile log sonundaki 'N Uyari / N Hata / Gecen Sure' ve hatalarin sonda toplu listesi de kalkar (tahmin: MSBuild ShowSummary ayni bayraga bagli) — basarisiz projede hata, binlerce uyari satirinin arasinda inline kalir. Bunu telafi icin Emit'te ': error ' satirlarini sayip/isaretleyip sonuc olayina eklemek ayri is. Restore argumani (MsBuildArguments.cs:35-36) ayni ozeti basiyor (ornek logda NU1903 satirlari 14-15 ve 22-23'te tekrar) — oraya da ayni bayrak.

**Değişmezler:** Hicbir degismezle catismaz: shell-out ayni, OutDir yok, arguman kaynagi tek yerde kaliyor. §9.2 'No verbosity switch is passed' korunur (verbosity degismiyor, yalniz ozet kapaniyor).

**Test fikri:** MsBuildArgumentsTests: Build() listesinde '-clp:NoSummary' var, '-clp:Summary' yok; ayrica gercek MSBuild ile kucuk bir projede uyari ureten derlemede uyari satirinin loga TEK kez dustugu (Acceptance).

**Doğrulayıcı notu:** Konum dogru: MsBuildArguments.cs:27 '-clp:Summary' sabit; RunCoordinator.cs:2130-2134 Emit her satiri disk + ProjectLogEvent yapiyor; ARCHITECTURE.md:1645 ayni listeyi yaziyor. Kanit loglardan yeniden dogrulandi: run-20261001-010430-305 = 79.734 satir / 27.422.456 bayt; run-20261001-005657-179/D1E11A4203B0C86A.log 2360 satir, 'Olusturma basarili oldu.' 1253. satirda, MSB3277 toplam 2006, ilk 1252 satirda 1003 (birebir yarisi ozet). Kodda ozet blogunu okuyan tuketici yok (grep: 'Warning(s)', 'Time Elapsed', 'Gecen Sure' src altinda hic gecmiyor). Not: MSBuild normal verbosity'de ozeti VARSAYILAN olarak basar; '-clp:Summary'yi yalniz silmek yetmez, onerildigi gibi '-clp:NoSummary' gerekir.

#### D4-2 — Uyari seli (MSB3277 11 MB, CS1591 11.6k satir) satir basina 3 maliyetli yoldan geciyor: senkron flush, cift SanitizeLine, satir basina IPC olay+flush

| Alan | Değer |
|---|---|
| Önem | kozmetik (Supervisor tarafi); batch'leme onerisi App boyutunda 'onemli' olarak degerlendirilmeli |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 124 — ProjectLogFile: StreamWriter AutoFlush=true → satir basina WriteFile
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 136-144 — AppendLine: SanitizeLine(text) — 1. tarama
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2130-2134 — Emit: ayni satir icin ikinci SanitizeLine + satir basina ProjectLogEvent
- `src/BuildOrchestrator.Contracts/Ipc/NdjsonFraming.cs` satır 27 — mesaj basina FlushAsync (onceki raporun 'mesaj basina 2 pipe write + flush' adayi hala duruyor)
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 148-151 — Snapshot zaten Flush() cagiriyor — AutoFlush kapatilsa canli okuma bozulmaz

**Kanıt:** run-20261001-010430-305: 33 log = 27.4 MB, 'warning MSB3277' satirlari 11.0 MB (%40). Types.UsedCars: 11.632 x CS1591 (XML doc eksik), 258 x CS0108. Build 00:56: UI.Report logu 1984 x CS0436. Uyarilarin kaynagi PROJE (mscorlib surum cakismasi = binding redirect / karisik TFM referansi; CS1591 = /doc acik ama yorum yok) — arac uretmiyor, sadece tasiyor.

**Etki:** Resolve basina ~80 bin satir x (flush syscall + 2 tarama + NDJSON serialize + pipe flush). Supervisor tarafinda tahmin ~1-3 s CPU/kosu; disk 27 MB/kosu (1623 kosu → 1.5 GB'in ana kaynagi); App tarafinda her satir render kuyruguna giriyor (D2/D3). Derleme duvar suresine etkisi dolayli (pump thread'leri ve App CPU'su derleyicilerle yarisiyor).

**Etki (doğrulayıcı düzeltmesi):** Olculen hacim: 79.734 satir / 27,4 MB (Resolve), 23.047 satir / 7,7 MB (Rebuild). Supervisor CPU ust siniri olcumden: %1-5 tek cekirdek x 142 s. 'Supervisor ~1-3 s CPU/kosu' TAHMIN, per-line maliyet olculmedi. Disk: satir basina 1 WriteFile (AutoFlush) — zayif diskte/AV'li makinede goreli olarak daha pahali (tahmin).

**Çözüm:** Uc katman, hepsi yapiyi korur: (1) PROJE TARAFI (arac disi, kullaniciya soylenmeli): MSB3277 icin AutoGenerateBindingRedirects / referans surumlerini esitleme; CS1591 icin <NoWarn>1591 ya da /doc kapatma — en buyuk kazanc burada. (2) ARAC, satir basina maliyet: AutoFlush=false + Snapshot/Dispose'da Flush (zaten var), FileStream buffer 64 KB; SanitizeLine'i tek yerde (AppendLine sanitize edip temiz metni dondursun, Emit onu kullansin); ProjectLogEvent'leri proje basina ~50 ms pencerede batch'lemek (Contracts'a 'projectLogBatch' ya da mevcut olayda satir listesi → orta is, App'in LogChunker'i satir numarasi tasidigi icin numara araligi korunur). (3) SARTLI: '-warnAsMessage:MSB3277' (MSBuild logging katmani, proje semantigi degismez; ama kullanici MSB3277'yi hic gormez — bilincli kabul ister).

**Çözüm (doğrulayıcı düzeltmesi):** Oncelik sirasi: (a) D4-1 (NoSummary) hacmi %29 dusurur — en ucuz; (b) NdjsonWriter'da payload+'\n' tek buffer/tek write (NdjsonFraming.cs:25-26) ve PumpEventsAsync'te (RunCoordinator.cs:652-661) kanalda birikeni tek yazimda gondermek — Contracts semasi degismeden syscall sayisini yariya/altina indirir; (c) AutoFlush=false guvenli: Snapshot (:150) ve Dispose flush ediyor — ama cokme aninda son buffer kaybolur, disk logu 'tek gercek kaynak' [D4] oldugu icin periyodik flush (or. 250 ms) eklenmeli; (d) projectLogBatch olayi Contracts degisikligidir, App'in satir-numarasi dikisi (§5.5) korunarak yapilir. '-warnAsMessage:MSB3277' kullanici karari ister (uyari tamamen gorunmez olur).

**Değişmezler:** stdout yalnizca NDJSON korunur (batch de NDJSON). Kopya yasagi: SanitizeLine tek yerde kalir (bugun iki cagri var). Proje dosyalarina dokunulmaz (oneri 1 kullanicinin kendi repo isi).

**Test fikri:** RunLogWriter: AutoFlush kapaliyken Snapshot() son yazilan satiri iceriyor (mevcut SnapshotProjectLog testi) + Dispose sonrasi dosya tam. Emit: SanitizeLine'in tek cagri oldugunu pinleyen kaynak guard'i (grep testi).

**Doğrulayıcı notu:** Konumlar dogru: RunLogWriter.cs:124 AutoFlush=true, :141 SanitizeLine, :150 Snapshot Flush; RunCoordinator.cs:2132-2133 ikinci SanitizeLine + satir basina ProjectLogEvent; NdjsonFraming.cs:25-27 mesaj basina 2 WriteAsync + FlushAsync. Ancak Supervisor tarafindaki etki olcumle sinirli: 142 s'lik kosuda Supervisor tek cekirdegin %1-5'i (23.047 satir) — satir basina maliyet Supervisor'da darbogaz DEGIL. Cift SanitizeLine temiz satirda yalniz IndexOfAny taramasi (27 MB icin ms mertebesi) — performans bulgusu degil, kopya/temizlik notu. stdout raw Console stream oldugu icin FlushAsync fiilen no-op; gercek maliyet mesaj basina 2 pipe WriteFile. Bulgunun degeri (2)'deki batch'leme ile App tarafinda; o da D2/D3 kapsaminda.

#### D4-3 — Scheduler yalniz build-order'a bakiyor; uzun UI projeleri kosunun sonuna kaliyor → son 25 s'de 4 slotun 1-2'si dolu

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/Scheduling/ReadySetScheduler.cs` satır 161-205 — TryDispatch: _nodesInOrder uzerinden ILK hazir dugum — sure/kritik yol bilgisi yok
- `src/BuildOrchestrator.Core/Scheduling/ReadySetScheduler.cs` satır 7-10 — doc: 'build-order'da EN ONDE olan' — bilincli kural [K2]
- `ARCHITECTURE.md` satır 1168-1176 — §8.2 'first in build order — never random or hash-ordered' (bilincli karar; degisirse yeniden yazilir)
- `ARCHITECTURE.md` satır 1285-1295 — §8.4 ETA zaten BuildState.LastDurationMs'i proje basina tahmin olarak kullaniyor — ayni veri sira icin de var

**Kanıt:** run-20261001-005657-179 decision.log slot analizi (baslangic = bitis − sure): 113 invoke, duvar 79.3 s, toplam slot-suresi 290.3 s, ort. eszamanlilik 3.66/4 (%92). Zamanin %84'unde 4 slot dolu; 1-3 dolu olan 12.6 s'nin neredeyse tamami KUYRUK: UI.NewSales.Report (17.0 s) 56.9 s'de basladi (bagimliligi Orchestration.NewSales.Report 27.2 s'de bitmisti — ~30 s hazir bekledi cunku build-order'da onunde ~25 kisa UI projesi vardi), UI.UsedCars.Report (11.2 s) 68.1 s'de basladi ve 79.3 s'de tek basina bitti. Teorik alt sinir sum/4 = 72.6 s.

**Etki:** Bu kosuda ~6-7 s (%8) kayip (tahmin: duvar 79.3 → ~73 s). Az projeli ama uzun kuyruklu kosularda oran buyur; Rebuild (150 x 0.65 s) gibi homojen kosularda etki sifira yakin (%98 doluluk olculdu).

**Etki (doğrulayıcı düzeltmesi):** Bu kosuda ust sinir ~7 s (79,3 → ~72 s, %9): UsedCars.Report bagimliligi biter bitmez baslasaydi 60,8+11,2 = 72,0 s. Homojen kosuda (Rebuild, %98 doluluk) kazanc ~0. Zayif makinede Light (2 slot) ile kuyruk etkisi farkli olur — olculmedi.

**Çözüm:** Core'da, ReadySetScheduler.TryDispatch'in secim anahtarini degistir: hazir kume icinde once 'en yuksek oncelik', esitlikte build-order (determinizm korunur: ayni graf + ayni defter + ayni tamamlanma sirasi ⇒ ayni dispatch). Oncelik = kritik yol uzunlugu: node'un kendi LastDurationMs'i (defterden, ETA'nin kullandigi ayni alan; yoksa medyan) + ona bagimli en uzun zincir — planlama asamasinda (Core/Planning) bir kez hesaplanip BuildPlan.Nodes'a 'Priority' olarak yazilir; scheduler yalniz siralar. Daha basit ara adim: LPT (yalniz kendi suresi). §8.2 cumlesi 'kritik yolu en uzun hazir proje once, build order esitlik bozucu' diye yerinde yeniden yazilir; determinizm iddiasi korunur.

**Çözüm (doğrulayıcı düzeltmesi):** Oncelik = kritik yol (kendi LastDurationMs + en uzun bagimli zincir), esitlikte build-order; Core/Planning'de bir kez hesaplanir. SCC grubu icin oncelik grup uyelerinin toplami/maks'i olarak tanimlanmali (bulgu bunu atlamis). Determinizm cumlesi 'ayni graf + ayni defter + ayni tamamlanma sirasi' diye yeniden yazilir; §17.3 determinizm testleri yeni kurali pinler. Once ayni decision.log uzerinde yeniden oynatma (simulasyon) ile kazanc dogrulanmali.

**Değişmezler:** Planlama Core'da kalir (oncelik hesabi Core/Planning). Scheduler saf state (I/O yok) — sureleri plan uzerinden alir, defteri kendisi okumaz. §8.2'nin 'never random/hash' vaadi korunur; 'first in build order' bilincli karari tersine cevrilir → gerekce: yukaridaki olcum (kuyrukta 12.6 s dusuk doluluk).

**Test fikri:** ReadySetScheduler testi: A(uzun, hazir) build-order'da B,C,D(kisa)'dan sonra → ilk dispatch A; esit oncelikte build-order korunur; ayni girdiyle iki kosu ayni dizi (determinizm).

**Doğrulayıcı notu:** Kod iddiasi dogru: ReadySetScheduler.cs:161-205 _nodesInOrder'da ILK hazir dugumu veriyor, sure bilgisi yok; ARCHITECTURE.md:1168-1173 'first in build order' diyor. Fakat kanit yanlis okunmus: UI.NewSales.Report '~30 s hazir bekledi' degil. csproj referanslarina gore son bagimliligi OSYS.UI.NewSales.Print 00:57:44.807'de bitti (decision.log), proje 00:57:55.73'te basladi (bitis 58:12.730 − 17000 ms) → hazir bekleme ~10,9 s. UI.UsedCars.Report'un son bagimliligi OSYS.UI.Report 00:57:59.589'da bitti, proje 58:06.89'da basladi → ~7,3 s. Yani kuyrugun bir kismi bagimlilik zinciri (UI.Report 12,3 s → UsedCars.Report 11,2 s), yalniz siralama degil. Cozum §8.2'deki yazili karari ve 'ayni graf + ayni tamamlanma sirasi ⇒ ayni dispatch' determinizm iddiasini (defter girdisi eklendigi icin) degistirir.

#### D4-4 — Perf profili statik: kullanici on planda BEKLERKEN de %70 hard cap + BelowNormal + 4 slot; adaptif (on plan/arka plan) politika mevcut komut/kanca ile kurulabilir

| Alan | Değer |
|---|---|
| Önem | acik soru / olcum gerektirir |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: yuksek [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Core/ProcessControl/PerfProfile.cs:30-36 (For: Full 6/none/Normal, Balanced 4/70/BelowNormal, Light 2/40/Idle)
- `src/BuildOrchestrator.Core/ProcessControl/PerfProfile.cs` satır 133-141 — Full(6, cap yok, Normal) / Balanced(4, %70, BelowNormal) / Light(2, %40, Idle) — tek kaynak
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 354-366 — ApplyPerfMode: canli degisen YALNIZ cap + priority
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 973, 1005-1007 — SemaphoreSlim(parallelism, parallelism) + worker dizisi kosu basinda sabit → paralellik kosu ortasinda degismez
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 350, 1244 — Activated → OnWindowActivated; IsVisibleChanged → SetMainWindowVisible — on/arka plan kancalari zaten var
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1793 — SetPerfModeCommand gonderimi — kanal hazir
- `ARCHITECTURE.md` satır 2152-2178 — §11.1: Full'un RAM'i tuketebildigi olculmus (bilincli: Balanced varsayilan)

**Kanıt:** Build 00:56: 4 slot %92 dolu, her slot cok is parcacikli bir csc.exe + MSBuild.exe kosturuyor; 8 mantiksal cekirdekte %70 hard cap = ~5.6 cekirdek esdegeri → makine bosken bile ~%30 CPU bilerek kullanilmiyor. Resolve tur-2: uye derleme toplami 151 s, duvar 79 s. Cap'in duvar suresine katkisi LOGDAN OLCULEMEZ (cap job seviyesinde; loglar sure basmaz) — §17.5 opt-in olcum testi (PerfProfileUiLatencyMeasurementTests) ayni seti Full/Balanced ile kosturarak verebilir.

**Etki:** Kullanici pencerenin basinda beklerken derleme gereksiz yavas (tahmin: CPU-bagli fazlarda cap kaldirilinca %10-25 daha kisa duvar; olculmeli). Tersine, tray'de/arka plandayken Balanced hala 4 derleyici + %70 → 'makine rahatlasin' beklentisiyle celisiyor.

**Etki (doğrulayıcı düzeltmesi):** Olculen yalniz slot dolulugu (%92 Build, %98 Rebuild). Cap'in etkisi OLCULMEDI. Zayif makine icin asil risk ters yonde: paralellik cekirdek/RAM'e gore olceklenmiyor (bkz. missing).

**Çözüm:** Yapi bozulmadan: (1) PerfProfile tek kaynak kalir; App'e chip'te 4. durum 'Auto' eklenir: pencere aktif → kullanicinin sectigi profil; gizli/tray/minimize/deaktif > 10 s (histerezis) → bir kademe asagi (Balanced→Light); OTOMATIK Full asla (§11.1 RAM gerekcesi). Politika saf fonksiyon olarak Core'da (AdaptivePerf.Resolve(chosen, windowState) → PerfMode), App yalniz mevcut SetPerfModeCommand'i gonderir. (2) Paralelligi canli yapmak icin: worker'lar en yuksek profil sayisinda (6) yaratilir, tek kapi zaten RunContext.InvokeSlots; semafor SemaphoreSlim(parallelism, MaxParallelism) olur; artis = Release(n), azalis = arka planda n slot Wait eden 'sink' (in-flight child'lar bitene kadar eski sayi surer — dogal drenaj). 'dalga gorunurlugu' degismezi (ilan edilen derleme ≤ slot) korunur cunku ilan yine slotu tutanin. (3) Once olc: §17.5 testiyle Full vs Balanced duvar suresi; cap'in bagli olmadigi cikarsa yalniz arka-plan-kademe-dusurme kalir. Arti: on planda hiz, arka planda serinlik, kullanici chip'i elle degistirmeden. Eksi: mod gecisi in-flight csc'leri etkilemez (en uzun 17-26 s), chip metni 'auto · light' gibi acik olmali; flapping icin histerezis sart.

**Çözüm (doğrulayıcı düzeltmesi):** Once olc (§17.5 opt-in testi: Full vs Balanced duvar suresi). 'Auto = gizliyken dusur' onerisi kullanicinin oncelikleriyle celistigi icin ONERILMEZ; yapilacaksa ters yon (kullanici baska uygulamada aktifken degil, yalniz RAM baskisinda dusur) ve kullanici karari. Canli paralellik degisimi (semafor) ayri, orta riskli is: seviye uyeleri de InvokeSlots'u kullaniyor (RunCoordinator.cs:1636-1688).

**Değişmezler:** Planlama/politika Core'da; Velopack'e dokunmaz; nested job korunur (cap yazimi ayni JobObject API'si); §11.1'in 'parallelism does not change mid-run' cumlesi bilincli degistirilir → yerinde yeniden yazilir.

**Test fikri:** Core: AdaptivePerf.Resolve tablo testi (secili×pencere durumu×histerezis). Supervisor: kosu ortasinda setPerfMode ile slot sayisi 4→2 dustugunde yeni dispatch 2'yi asmiyor, in-flight'lar tamamlaniyor; 2→4'te bekleyen worker uyaniyor.

**Doğrulayıcı notu:** Konum hatasi: PerfProfile.cs 63 satir; tablo 32-34'te (133-141 yok). Diger konumlar dogru (RunCoordinator.cs:354-364 ApplyPerfMode, :973 SemaphoreSlim, :1005-1007 worker dizisi; MainWindow.xaml.cs:350 ve :1244; RunViewModel.cs:1793). Etki olculmemis: cap'in duvar suresine katkisi icin hicbir sayi yok ('%10-25' tahmin). Onerinin 'tepside/arka planda bir kademe dusur' kismi kullanicinin BIRINCIL senaryosuyla celisir (tepsiden baslatilan derleme HIZLI olmali). §11.1'in olculmus gerekcesi (RAM; Balanced varsayilan, 'parallelism does not change mid-run') ile catisir.

#### D4-5 — packages.config'li projede restore prologu HER Build kosusunda kosuyor (kaynak/paket degismemisken de) — proje basina ~0.8-1.6 s

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2044-2051 — NeedsRestore: !suppressRestore && target != Clean && HasPackagesConfig(projectId) — icerik/degisim kontrolu YOK; suppressRestore yalniz SCC turlarinda
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2321-2325 — HasPackagesConfig: yalniz dosya var mi
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 64-74 — restore child → build child; DurationMs ikisini kapsar
- `src/BuildOrchestrator.Core/Workspace/OptimizeWorkspaceService.cs` satır 431-442 — HintPath hedefi diskte var mi detektoru (IsNuGetPackagesHint/TargetExists) — yeniden kullanilabilir
- `ARCHITECTURE.md` satır 1678-1690 — §9.3 restore prologu tanimi

**Kanıt:** OSYS'te 21 packages.config projesi (177 csproj). Build 00:56: 13 proje 2 invoke (restore+build); restore'un MSBuild 'Gecen Sure'si 0.58-1.36 s + ~0.2 s surec ek yuku (olculen invoke basi sabit yuk, D4-7). Resolve 01:04: 7 proje tur-1'de restore. Restore ciktisi 'yapilacak bir sey yok' (paketler yerinde) ama NuGet audit de kosuyor (NU1903 uyarilari: Newtonsoft.Json 9.0.1 — ag erisimli zafiyet DB'si).

**Etki:** Build kosusu basina ~13-15 s slot-suresi (290 s'nin ~%5'i, ~3.5 s duvar); Resolve tur-1'de 7 x ~1 s. Kucuk ama her kosuda odenen sabit bedel.

**Etki (doğrulayıcı düzeltmesi):** Olculen: 13 restore invoke/kosu, restore MSBuild suresi 0,58-1,36 s (bulgu ajani), ornek 0,72 s. '+~0,2 s spawn' D4-7'den turetme. '~3,5 s duvar' tahmin. Tepsiden kucuk derlemede (birkac proje) kapsamdaki her packages.config projesi icin +~1 s; zayif makinede surec acilisi daha pahali (tahmin).

**Çözüm:** Restore'u kosullu yap (arac disi bir sey degismez): BuildState'e 'RestoredPackagesConfigHash' (packages.config icerik ozeti; source-hash-cache altyapisi zaten var) yaz; prolog yalniz (a) ozet farkli ya da kayit yok, (b) OptimizeWorkspaceService'in HintPath detektoru en az bir NuGet hedefini diskte bulamiyor, (c) onceki kosu bu proje icin restore'da patladi ise kosar. Detektor Core/Workspace'ten Core'da ortak bir yardimciya cikarilir (kopya yasak). Restore'un kendi cikisi degismez; decision.log'a 'restore skipped — packages unchanged' satiri. SARTLI ek: '-p:NuGetAudit=false' restore'u kisaltabilir ama NU19xx zafiyet uyarilarini gizler — kullanici karari; once restore suresindeki audit payi olculmeli.

**Çözüm (doğrulayıcı düzeltmesi):** Onerilen kosullu restore gecerli; iki duzeltme: (1) yalniz HintPath detektoru yetmez — HintPath'siz paketler (build .targets/analyzer/content) icin packages.config icerik ozeti sart, bulgu bunu dogru kurmus; ozet build-state'e degil BASARILI restore'dan sonra yazilmali. (2) §9.3 'packages.config is never parsed' cumlesi korunur (ozet almak parse degil) ama §9.3 prolog tanimi ayni iste yeniden yazilir. NuGetAudit kapatma ayri kullanici karari; audit payi olculmedi.

**Değişmezler:** OutDir/obj'ye dokunulmaz (yalniz build-state.json'a hash yazilir). Git'e yazilmaz. Arguman kaynagi tek (MsBuildArguments.PlanFor NeedsRestore=false alir). Optimize'in restore-only yolu etkilenmez.

**Test fikri:** RunCoordinator: ayni packages.config + paketler yerinde → ikinci Build'de restore komut satiri loga yazilmiyor; packages.config degisince ya da HintPath hedefi silinince yeniden yaziliyor; restore'u patlayan proje bir sonraki kosuda yine restore aliyor.

**Doğrulayıcı notu:** Kod dogru: RunCoordinator.cs:2050 NeedsRestore = !suppressRestore && target != Clean && HasPackagesConfig; :2321-2325 yalniz File.Exists. MsBuildInvoker.cs:64-74 restore child → build child. Kanit dogrulandi: run-20261001-005657-179'da 113 proje logunun 13'unde '-t:restore' satiri var; ornek logda restore 'Gecen Sure 00:00:00.72', NU1903 uyarilari mevcut. OSYS'te 21 packages.config (find ile sayildi). OptimizeWorkspaceService.cs:431-445 HintPath detektoru mevcut (private static — ortak yardimciya cikarmak gerekir).

#### D4-6 — vswhere ilk eslesme olarak 32-bit MSBuild.exe'yi seciyor (Bin\MSBuild.exe = X86); amd64 surumu mevcut ve VS'nin kendi kullandigi

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Core/MsBuild/MsBuildResolver.cs:14
- `src/BuildOrchestrator.Core/MsBuild/MsBuildResolver.cs` satır 140-141 — -find MSBuild\**\Bin\MSBuild.exe — desen hem Bin\ hem Bin\amd64\ ile eslesir
- `src/BuildOrchestrator.Core/MsBuild/VsWhereLocator.cs` satır 56-57 — ciktinin ILK satiri alinir → Bin\MSBuild.exe (x86)
- `ARCHITECTURE.md` satır 1634-1638 — §9.1 cozumleme deseni

**Kanıt:** GetAssemblyName: Bin\MSBuild.exe → X86; Bin\amd64\MSBuild.exe → Amd64; Roslyn\csc.exe → MSIL (AnyCPU, 64-bit kosar). Her iki MSBuild icin ngen native image var (MSBuild 15.1, Microsoft.Build x86+x64). Proje loglarindaki komut satiri Bin\MSBuild.exe. VS 2022+ (64-bit IDE) derlemeyi amd64 MSBuild ile yapar.

**Etki:** Performans farki OLCULMEDI (tahmin: degerlendirme/RAR icin kucuk; 32-bit surecte 4 GB adres alani ve WPF markup derleyicisi (PresentationBuildTasks, in-proc) 32-bit kosuyor — buyuk XAML projelerinde bellek baskisi). 'VS-parity' hedefi acisindan amd64 daha tutarli.

**Etki (doğrulayıcı düzeltmesi):** Sure/bellek farki OLCULMEDI. csc.exe zaten ayri 64-bit surec; fark yalniz MSBuild.exe'nin kendi evaluation/RAR/in-proc task'lari icin.

**Çözüm:** Once olc: ayni Build kosusunu iki MSBuild ile (ornek 5 UI projesi) karsilastir. Fark varsa deseni 'MSBuild\**\Bin\amd64\MSBuild.exe' yap, bulunamazsa Bin\MSBuild.exe'ye dus (tek FindAsync cagrisi, ikinci desen fallback). §9.1 ve resolver testleri guncellenir.

**Çözüm (doğrulayıcı düzeltmesi):** Olcmeden dokunma. Olcum amd64 lehine cikarsa: bulunan yolun yanindaki 'amd64\MSBuild.exe' var mi diye bakmak ya da ikinci desen; test fikrindeki 'vswhere iki satir verir' senaryosu gercek ciktiya uymuyor.

**Değişmezler:** Shell-out ve nested job aynen; yalniz hangi exe. Kopya yasak: desen listesi tek yerde.

**Test fikri:** MsBuildResolver: vswhere sahte ciktisi iki satir verdiginde amd64 secilir; yalniz x86 varsa ona duser.

**Doğrulayıcı notu:** Sonuc dogru, mekanizma yanlis. Konum: desen MsBuildResolver.cs:14'te (dosya 37 satir; 140-141 yok); ilk satir secimi VsWhereLocator.cs:56-57 dogru. PE basligi okundu: Bin\MSBuild.exe machine=0x14c (x86), Bin\amd64\MSBuild.exe=0x8664. Ancak vswhere'i ayni desenle calistirdim: TEK satir donuyor (Bin\MSBuild.exe) — desen amd64'u hic eslestirmiyor; 'iki eslesmeden ilki' iddiasi yanlis. Performans farki olculmemis. Zayif (8 GB) makinede 32-bit MSBuild daha AZ bellek kullanir; amd64'e gecis RAM'i artirabilir (tahmin) — yon belirsiz.

#### D4-7 — MSBuild.exe invoke basina ~200 ms sabit yuk (surec + CLR/MSBuild acilisi + drenaj) + ~0.47 s degerlendirme/up-to-date tabani — buyuk olcude arac disi, kucuk kirintilar arac tarafinda

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/ProcessControl/JobProcessLauncher.cs` satır 40 — CREATE_NO_WINDOW → her child icin gizli conhost (DETACHED_PROCESS conhost'u hic acmaz; pipe'lar STARTF_USESTDHANDLES ile zaten veriliyor)
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 211 — StreamReader bufferSize: 1024 — 4 MB'lik loglarda okuma cagri sayisini artirir (kucuk)
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 150-168 — cikis → WaitPumpsBoundedAsync (en fazla 5 s; olculen ortalamada bekleme yok)

**Kanıt:** Olcum: decision.log 'DurationMs' − proje logundaki MSBuild 'Gecen Sure' (MSBuild kendi saatini 'Olusturma basladi' ile baslatir; fark = surec spawn + MSBuild.exe/CLR acilisi + cikis + pipe drenaji). Rebuild 01:00 (154 invoke, hepsi up-to-date): ortalama 198 ms (min 149, p50 193, p90 232, max 291), toplam 34.6 s / 118.7 s slot (%29). Build 00:56 (113): ortalama 234 ms, toplam 29.3 s / 290 s (%10). MSBuild'in kendi up-to-date kosusu (evaluation + hedef atlama) p50 ≈ 667 − 193 = ~0.47 s. Orkestrator'un invoke'lar ARASINDAKI boslugu ayrica ~20 ms ((4×30.4 − 118.7)/154) — dispatch/persist/wake yolu darbogaz degil.

**Etki:** Sabit yukun buyuk kismi MSBuild.exe'nin kendisi (ngen'li, .NET Framework 4.x); arac tarafinda kazanilabilecek tahmin 10-30 ms/invoke (conhost) — 150 invoke'ta 1.5-4.5 s slot, ~1 s duvar. Gercek kazanc yalniz gereksiz invoke'u hic yapmamaktan gelir (incremental pre-skip zaten yapiyor: Build 01:10'da 187 projede 6 invoke, 5.5 s).

**Etki (doğrulayıcı düzeltmesi):** Invoke basi ~200 ms sabit yuk logdan turetilmis (bulgu ajani; yeniden hesaplamadim). Conhost kaldirmanin kazanci (10-30 ms/invoke) TAHMIN. Zayif makinede surec acilisi + AV taramasi nedeniyle sabit yuk daha buyuk olur (tahmin).

**Çözüm:** (1) DETACHED_PROCESS deneyi: JobProcessLauncher'da CREATE_NO_WINDOW yerine DETACHED_PROCESS (redirected yolda); MSBuild konsol logger'i yonlendirilmis handle'la zaten calisiyor — olc, fark yoksa dokunma. (2) Reader bufferSize 1024 → 16-64 KB (bellek onemsiz, cagri sayisi duser). (3) Rebuild (alt bar) semantigi bilincli: 150 gereksiz invoke = 30 s; kullanici bunu 'cache'i yok say' olarak istiyorsa dogru, aksi halde acik soru.

**Çözüm (doğrulayıcı düzeltmesi):** DETACHED_PROCESS riski 'dusuk' degil 'orta': konsolsuz MSBuild'in baslattigi, CreateNoWindow vermeyen bir konsol alt sureci (post-build Exec zinciri, cmd/lc goruldu) GORUNUR konsol penceresi acabilir; ayrica MSBuild'in csc'yi CREATE_NO_WINDOW ile acmasi conhost'larin yarisini zaten korur (en fazla 4 conhost kazanilir). Yalniz olcum + OSYS post-build olaylariyla denendikten sonra. bufferSize 1024 → 16 KB guvenli ve kucuk. Asil kazanc: gereksiz invoke'u yapmamak (alt bar Rebuild semantigi kullanici sorusu).

**Değişmezler:** Nested job ayni (Assign SUSPENDED iken). stdout NDJSON degismez.

**Test fikri:** Mevcut KillMidBuildTests/pipe EOF testleri DETACHED_PROCESS ile yesil; conhost sayisi (Get-Process conhost) kosu sirasinda artmiyor.

**Doğrulayıcı notu:** Konumlar dogru: JobProcessLauncher.cs:40 CREATE_SUSPENDED|CREATE_NO_WINDOW|CREATE_UNICODE_ENVIRONMENT; MsBuildInvoker.cs:210-211 bufferSize 1024; :150-168 cikis + bounded drain. Olcumle tutarli: kosuda 5-9 conhost goruldu (4 MSBuild + csc'ler). No-op Rebuild 38-45 s olcumu, 154 invoke x (~0,65+0,2 s)/4 ≈ 33 s hesabiyla uyumlu. Bulgu zaten 'kozmetik' ve 'buyuk olcude arac disi' diyor — durust.

#### D4-8 — Kosu ici defter yazimlari proje sonucu basina dosyanin tamamini okuyup yeniden yaziyor (build-state.json 148 KB) + uc kez in-flight ledger dosyasi

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Core/State/BuildStateStore.cs:199-214 (Write), :40-62 (Load)
- `src/BuildOrchestrator.Core/State/BuildStateStore.cs` satır 198-208 — Write: her Upsert/Remove/Invalidate = Load() (JSON parse 196 giris) + tam Serialize + temp dosya + rename
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2253 — InvalidateBuildStateOnFailure: Write'in kendi Load'undan ONCE ayri bir Load() daha
- `src/BuildOrchestrator.Core/State/InFlightLedger.cs` satır 44-60, 143-148 — Add/Remove: proje basina 2 atomik dosya yazimi

**Kanıt:** Build 00:56: 113 sonuc → ~113 x (148 KB oku+parse+serialize+yaz) + 226 ledger yazimi; hepsi worker thread'inde, _writeGate ile serilesiyor (ayni anda biten 2-4 proje birbirini bekler).

**Etki:** Olculmedi; tahmin proje basina 2-5 ms → kosu basina <1 s, orkestrator-arasi ~20 ms boslugun bir parcasi. Bosluk zaten kucuk oldugu icin derleme suresine etkisi ihmal edilebilir; disk yazimi ve GC baskisi olarak kozmetik. (Ayni dosyanin acilista tekrar okunmasi state boyutunun konusu.)

**Etki (doğrulayıcı düzeltmesi):** Cagri sayisi kesin: proje sonucu basina 1 tam oku-parse-yaz (148 KB) + 2 ledger dosyasi = 3 temp dosya + 3 rename. Sure OLCULMEDI ('2-5 ms' tahmin). Zayif/AV'li makinede her temp dosya + rename taranir; 150 projelik kosuda ~450 dosya olusturma (koddan). Olculen orkestrator-arasi bosluk ~20 ms oldugundan derleme suresine etkisi kucuk.

**Çözüm:** BuildStateStore kosu boyunca bellek-ici haritayi tutup (kilit altinda) yalniz seri hale getirip yazsin (Load bir kez, sonra map guncelle → Serialize → atomic write); InvalidateBuildStateOnFailure'daki ekstra Load kaldirilir (Write'in mutate lambda'si existing'i zaten goruyor). Ledger: Add+Remove'u tek yazimda birlestirmek olmaz (cokme kurtarmasi icin her ikisi gerekli) — burada dokunma.

**Çözüm (doğrulayıcı düzeltmesi):** Bellek-ici harita + yalniz Serialize+atomik yazim; :2253'teki ekstra Load yerine Write'in mutate lambda'si (InvalidateWithoutEvidence'in :183-194 deseni) kullanilir. RunCoordinator.cs:1933 (SCC yakinsamama hafizasi) da ayni Load+Upsert ciftini yapiyor — ayni yola alinmali. Ledger'a dokunulmaz (§8.7).

**Değişmezler:** Atomik rename korunur; cokme kurtarmasi (§8.7) davranisi degismez.

**Test fikri:** BuildStateStore: 200 girisli defterde 100 Upsert sonrasi dosya icerigi eski davranisla birebir; eszamanli Upsert kaybi yok (mevcut _writeGate testi).

**Doğrulayıcı notu:** Kod dogru: BuildStateStore.cs:199-214 Write = kilit → Load() (tam JSON parse + GroupBy/ToDictionary) → kopya map → Serialize → AtomicFile (temp + rename). RunCoordinator.cs:2253 kanitli hata yolunda ek Load(). InFlightLedger.cs:44-60 Add/Remove → WriteLocked (:143-148) her biri atomik yazim. build-state.json 148.870 bayt (dogrulandi). Baslikta 'uc kez' yaziyor, govde ve kod 2 yazim (Add + Remove) — baslik yanlis. Tek yazici Supervisor (Program.cs:31) oldugu icin bellek-ici harita guvenli.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.Core/ProcessControl/PerfProfile.cs:32-34 + src/BuildOrchestrator.Supervisor/RunCoordinator.cs:845 — paralellik sabit 6/4/2; Environment.ProcessorCount ya da fiziksel RAM'e gore hic kisilmiyor (src'de ProcessorCount/bellek sorgusu yok). 2-4 cekirdek / 8 GB makinede Balanced yine 4 MSBuild + 4'e kadar csc baslatir; olcumde child toplam WS 1,85 GB'a cikti ve §11.1 'ilk sinir bellek' diyor — zayif makine icin en somut motor riski.
- src/BuildOrchestrator.Supervisor/RunCoordinator.cs:2133 + src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2694-2717 — Supervisor HER projenin HER satirini kosulsuz yayinliyor; App hepsini iki kez tutuyor (_liveLines olay listesi + _runText), pencere tepside ve hicbir proje secili degilken bile. getProjectLog/projectLogChunk (§5.5) diskten okuma yolu zaten var: canli satir yalniz konsol gorunurken/secili projede yayinlanabilir. Olcumdeki App Private 194 → 381 MB buyumesinin ve tepsideki %40 CPU'nun aday kaynagi (pay olculmedi).
- src/BuildOrchestrator.Contracts/Ipc/NdjsonFraming.cs:25-26 + src/BuildOrchestrator.Supervisor/RunCoordinator.cs:652-661 — her olay icin payload ve '\n' AYRI iki pipe yazimi, kanal birikmis olsa bile birlestirme yok: 23.047 satirlik kosuda ~46 bin WriteFile; tek buffer + biriken olaylari tek yazimda gondermek sema degistirmeden yariya/altina indirir.
- src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs:33-37 — restore argumanlarinda NoSummary/verbosity yok: restore ciktisi banner + uyari + ozet tekrariyla (ornek logda NU1903 satirlari 14-15 ve 22-23) her packages.config projesinin loguna ~28 satir ekliyor; D4-1 duzeltmesi yalniz Build listesine uygulanirsa restore tekrarlari kalir.
- src/BuildOrchestrator.Core/Logs/RunLogWriter.cs (logs klasoru) — kosu klasorleri icin retention yok (olcum: 1626 klasor / 1,5 GB); motor her kosuda yeni klasor aciyor, hicbir yol eskileri silmiyor. Zayif makinede kucuk SSD icin somut birikim; D4 bulgulari yalniz hacmi azaltmayi oneriyor, silmeyi degil.

**Temiz bulunan alanlar:**
- Copy contention (§9.5) zaman yemiyor: 1623 kosu klasorunde MSB302x satiri yalnizca 3 proje logunda gecmis, decision.log'larda 'Copy contention detected' retry satiri 0 — backoff/copy-floor mekanizmasi pratikte hic devreye girmiyor, maliyeti sifir (RetryingMsBuildInvoker.cs:266,318).
- Slot dolulugu ve dispatch yolu: Build 00:56'da ort. eszamanlilik 3.66/4 (%92), Rebuild 01:00'da 3.91/4 (%98); orkestratorun iki invoke arasindaki boslugu ~20 ms (wake/TryDispatch/ComputeDepIssues/log acma/persist toplam). ReadySetScheduler'in tek kilitli O(n) taramasi 187 dugumde olculebilir maliyet uretmiyor (ReadySetScheduler.cs:161-205; RunCoordinator.cs:1080-1131).
- Arguman sozlesmesi (§9.2) dogru kurulmus: -p:BuildProjectReferences=false (bagimlilik yeniden girilmiyor), -m verilmiyor (tek node, ek node spawn yok), -nodeReuse:false (olculmus ~0 maliyet), verbosity varsayilan (csc /reference: satiri okunabiliyor — CompilerReferences.cs:88-92). MSBuild'in kendi incremental'i calisiyor: Rebuild kosusunda 150/150 projede CoreCompile atlandi (p50 0.65 s).
- ApiSurfaceHash yalniz SCC tur yolunda calisiyor (RunCoordinator.cs:1509, BuildCycleGroupAsync icinde); Build/Rebuild/Clean/tek proje kosularinda hic cagrilmiyor — cycle disi kosulara yuk bindirmiyor. Restore-once turlar arasinda dogrulandi (suppressRestore, RunCoordinator.cs:2046-2050; 01:04 loglarinda tur-2 uyelerinde -t:restore yok).
- Ortam degiskenleri: JobProcessLauncher CreateProcessW'ye lpEnvironment=0 veriyor (JobProcessLauncher.cs:64-66) → child Supervisor'in ortamini miras alir, arac hicbir sey set etmiyor. MSBuild.exe .NET Framework (MSBuild.exe.config supportedRuntime v4.0 sku 4.6; PE32) oldugundan DOTNET_TieredPGO/DOTNET_CLI_TELEMETRY_OPTOUT anlamsiz; MSBUILDDISABLENODEREUSE -nodeReuse:false ile gereksiz. Ayarlanmasi gereken bir degisken YOK.
- Roslyn soguk baslangici JIT'siz: ngen display ile dogrulandi — csc 5.9.0.0, Microsoft.CodeAnalysis 5.9.0.0, Microsoft.CodeAnalysis.CSharp 5.9.0.0 icin 64-bit native image mevcut; csc.exe MSIL (AnyCPU) → 64-bit kosar. UseSharedCompilation=false'un bedeli yalniz surec acilisi + is-ici JIT'siz yukleme; arac tarafinda yapilacak bir sey yok (paylasimli derleme §9.2'de gerekceli reddedildi).
- WPF projelerinde csc'nin iki kez kosmasi (_wpftmp: GenerateTemporaryTargetAssembly → MarkupCompilePass2) projelerin kendi yapisi: Build 00:56'da 112 csc'li projenin 13'unde, Resolve'daki 17 uyeli UI grubunun tamaminda. MSBuild property ile kapatilamaz (XAML'de yerel tipler kullanildigi surece Pass2 zorunlu); tek kacis paylasimli derleme (reddedildi) — arac disi.
- Timeout: invoke basina tek 10 dk (restore+build toplam, MsBuildInvoker.cs:44,105-111), kill+bounded drain (5 s) — asili kalma yok, olculen kosularda timeout yok.
- Toolset cozumu kosu basina degil bir kez (Program.cs:221-229 _toolset cache) — vswhere kosu basina spawn edilmiyor. Cikti encoding'i sabit UTF-8, tespit yok (MsBuildOutputEncoding.cs:31-32) — pump hafif.
- Kosu ortasinda perf degisimi: cap + priority canli, JobObject API'siyle (RunCoordinator.cs:354-366, JobObject.cs:111-130); planlama penceresinde gelen niyet kaybolmuyor (_pendingPerf).

**Açık sorular:**
- MSBuild ozet blogu (N Warning(s) / N Error(s) / Gecen Sure satirlari) sizin icin degerli mi? Degilse -clp:NoSummary ile Resolve logunun ~%29'u (tekrar uyarilar) kalkar; degerliyse sayaclari arac kendisi uretebilir.
- MSB3277 (mscorlib surum cakismasi) ve CS1591 (XML doc) uyarilari OSYS projelerinde duzeltilebilir mi (binding redirect / NoWarn)? Yoksa aracin bunlari log katmaninda susturmasini (-warnAsMessage:MSB3277) kabul eder misiniz — bu durumda o uyarilari hic gormezsiniz.
- Perf chip'ine 'Auto' (on planda secili profil, arka planda/tray'de bir kademe dusuk) eklenmesini ister misiniz? Otomatik Full'u asla acmayacagiz (§11.1 RAM olcumu) — on planda daha hizli istiyorsaniz Full'u elle secmek gerekir; bunun icin once Full vs Balanced duvar suresi olcumu yapalim mi (§17.5 opt-in testi)?
- Alt bardaki Rebuild'in 'cache'i yok say ama MSBuild up-to-date ise derleme' anlami (150 gereksiz invoke = 30 s) beklediginiz davranis mi?
- Restore prologunu kosullu yapmayi (packages.config ozeti + HintPath hedefleri diskte) onayliyor musunuz? Ayrica NuGet audit (NU1903 zafiyet uyarilari) restore'u uzatiyor olabilir; -p:NuGetAudit=false ile kapatilmasi istenir mi, yoksa uyarilar kalsin mi?
- 32-bit MSBuild → amd64 MSBuild karsilastirma olcumu icin bir Build kosusu ayirabilir miyiz (tek kosu, salt gozlem)?


### D9-disk-io-state

#### D9-1 — Koşu loglarında retention yok: 1,39 GB / 497 gerçek koşu, aylık ~1 GB büyüme, hiçbir şey silinmiyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 20-25 — Her koşu yeni run-<ts> klasörü + decision.log açar; silme yolu yok
- `src/BuildOrchestrator.Supervisor/Program.cs` satır 24-26 — logsRoot yalnız CreateDirectory ile açılır; açılışta temizlik adımı yok
- `src/BuildOrchestrator.Core/Workspace/OptimizeWorkspaceService.cs` satır 131-141 — Optimize üç defteri budar, log klasörüne hiç bakmaz
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 183-224 — _lastRunDirectory: koşu bittikten sonra kart tıklaması bu dizinden okur — retention bunu korumalı

**Kanıt:** Kod aramasında (src altında) run klasörü silen tek satır yok (Directory.Delete yalnız CleanWorkspaceService'te, obj/bin için). Diskte: %LOCALAPPDATA%\BuildOrchestrator\logs = 1624 klasör, 24.987 dosya, 1.386 MB. Aya göre: Tem 455 koşu/64 MB, Ağu 595/332 MB, Eyl 569/944 MB, Eki (5 gün) 5/44 MB. En büyük koşu 36 MB; 2026-10-01 01:04 Cycles koşusu 34 dosya / 27,4 MB. 1128 klasör test artığı (decision.log'da 'run r1 … OSYS.X/OSYS.Y', toplam <1 MB — kaynak D9-2), 497 gerçek koşu 1.389 MB. RunLogPaths.DefaultLogsRoot'u yalnız About → Paths satırı okur; App log klasörünü yalnız RunStartedEvent.LogDirectory ile bir kesme özet satırında yazar (RunViewModel.AutoSync.cs:107-114).

**Etki:** Ölçülen: 1,39 GB disk, Eylül tempo'suyla ~1 GB/ay sınırsız büyüme; 25 bin dosyalık klasör Defender/indexer yükü ve Explorer'da açılmaz hale gelir. CPU/RAM'e etkisi yok, diske ve 'cihaz rahatlasın' isteğine doğrudan etki.

**Etki (doğrulayıcı düzeltmesi):** Olculen: 1.392,7 MB / 1626 klasor / 25.297 dosya; Eylul 944,8 MB. CPU/RAM etkisi yok, yalniz disk. 'Defender/indexer yuku' olculmedi (tahmin).

**Çözüm:** Core'a tek bir `RunLogRetention` (Core/Logs) ekle: politika = en yeni N koşu (öneri 30) VE en çok D gün (öneri 14) VE toplam üst sınır (öneri 500 MB); klasör adı run-<ts> zaten sıralanabilir, mtime'a gerek yok. Korunanlar: aktif koşu dizini ve koordinatörün _lastRunDirectory'si (host koşu başlamadan önce bilir); silme best-effort (kilitli/açık klasör atlanır, IOException yutulur). Çalışma yeri: Supervisor Program.Main'de RecoverInterruptedRun'dan SONRA, host.RunAsync'ten ÖNCE bir Task.Run ile arka planda (stdout NDJSON değişmezine dokunmaz; stderr'e tek satır 'log retention: removed N runs, X MB'). Aynı sınıfı Optimize da çağırır ve sayacını tally'ye ekler (kullanıcıya görünen tek satır). Test artığı klasörler (decision.log 340 B) aynı politikayla kendiliğinden gider. Politika sabitleri tek yerde (kopya yasağı); README'ye 'logs klasörü N koşu/D gün tutulur' satırı.

**Çözüm (doğrulayıcı düzeltmesi):** Politika ve yer ayni (Core/Logs, tek sabit kumesi), ama: (1) ARCHITECTURE §10.3 'never deleted' cumlesi ve §16 tablosu ayni iste guncellenmeli — once kullaniciya sorulmali (dokuman-kod kurali). (2) Ilk temizlik ~25 bin dosya siler; zayif makinede Supervisor acilisindaki ilk Sync ile ayni anda kosmamali — acilis Sync'i bittikten sonra, dusuk oncelikli/parcali ya da yalniz Optimize'da. (3) Kucuk klasorler 'test artigi' diye ayri ele alinmamali; yalniz yas/sayi/boyut politikasi uygulanmali.

**Değişmezler:** stdout yalnız NDJSON (satır stderr'e); OutDir'e dokunulmaz (yalnız kendi log klasörü); planlama Core'da (politika Core'da, host yalnız çağırır).

**Test fikri:** Sahte logsRoot'ta 40 run-<ts> klasörü oluştur (biri 'aktif', biri 'son'), retention'ı N=30 ile koş: en eski 8 gider, aktif+son kalır, kilitli klasör atlanır ve dönen sayı doğru; ikinci koşu hiçbir şey silmez.

**Doğrulayıcı notu:** src altinda kosu klasoru silen kod yok (tek Directory.Delete CleanWorkspaceService.cs:227, bin/obj icin). RunLogWriter.cs:20-25, Program.cs:24-26, OptimizeWorkspaceService.cs:131-134 (log klasorune bakmiyor), RunCoordinator.cs:184/218/727 (_lastRunDirectory) dogrulandi. Diskte yeniden olctum: 1626 klasor / 25.297 dosya / 1.392,7 MB; Tem 455/64,9 MB, Agu 595/332,5 MB, Eyl 569/944,8 MB, Eki 7/50,5 MB. Duzeltme: '1128 test artigi' kesin degil — <2 KB klasor sayisi 1108 ve bunlarin icinde GERCEK kucuk kosular da var (or. run-20260920-085851-600 gercek bir tek-proje Clean kosusu). Ayrica ARCHITECTURE.md §10.3 (satir 1999-2000) acikca 'The run logs on disk are never deleted; clearing is for the screen only' diyor: retention bu cumleyle celisir, kullanici karari + dokuman guncellemesi gerekir.

#### D9-2 — evaluation-cache.json'un %93'ü hiç isabet edemeyecek ölü giriş (2558 test artığı + şema-0 kayıtlar); Optimize kök dışını ve şema-eskisini budamıyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: dusuk [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs:176-185 (PruneMissingUnderRoot); EvaluationCache.cs:54 ve 122-130 dogru
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 54 — e.Schema == CurrentSchema kapısı: şema-0 kayıt asla isabet SAYILMAZ, ama diskte tutulur
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 122-130 — PruneMissingUnderRoot yalnız kök altını budar; C:\Users\…\Temp altındaki girişler kalıcı
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 328-337 — Aynı kök-kapsamlı budama; 34 Temp girişi kalıcı
- `tests/BuildOrchestrator.Tests/App/RunViewModelTests.cs` satır 1304-1320 — 'bo-vm-rebuild-' fixture'ı — bugün SupervisorSandbox ile izole (--logs), kirlilik geçmişten
- `tests/BuildOrchestrator.Tests/Supervisor/SupervisorIsolationGuardTests.cs` satır 1-80 — Kaynak guard'ı (83942f7, 2026-09-19 05:28) — o tarihten sonra yeni Temp girişi yok

**Kanıt:** Dosya 5.901.064 B / 2761 giriş. 2558 giriş C:\Users\Delta\AppData\Local\Temp\bo-vm-rebuild-*\X|Y.csproj (1,69 MB), hepsi Schema=0, csproj mtime aralığı 2026-07-17 → 2026-09-19 02:02 (guard commit'i 05:28'den ÖNCE) → kaynak tarihsel, süit bugün temiz. Gerçek 203 giriş: 177 OSYS + 16 CustomerProject + 7 Rent + 3 IntegrationHub = 4,35 MB; 7 Rent girişi de Schema=0 (2025-10 tarihli, artık taranmayan kök). Schema dağılımı {0: 2565, 1: 196}. source-hash-cache: 24.588 giriş, 34 Temp + 62 diskte olmayan. build-state: 196 giriş, 0 Temp, 0 eksik (temiz). Diskte öksüz .tmp yok, run-inflight.json boştayken yok.

**Etki:** Her Sync ve her koşu bu 5,9 MB'ı parse edip yeniden yazıyor (D9-3): faydalı yük 4,35 MB, 1,69 MB'ı hiçbir zaman okunmayacak çöp. Kullanıcı Optimize'a bassa da bu girişler asla gitmez.

**Etki (doğrulayıcı düzeltmesi):** 1,65 MB / 5,9 MB olu yuk (Load/Flush basina birkac ms parse+yazim; .NET suresi olculmedi). source-hash-cache'te 34 Temp girisi / 24.611.

**Çözüm:** (a) Sıfır riskli kural: EvaluationCache.Flush/Prune'da Schema != CurrentSchema olan HER giriş ölüdür (satır 54 zaten onları asla servis etmiyor) → Optimize'ın prune'una 'stale schema' budaması ekle (kök bağımsız). (b) evaluation-cache ve source-hash-cache SALT optimizasyon olduğu için (dosya doc'ları böyle diyor) 'dosyası diskte olmayan giriş' budaması kök bağımsız yapılabilir: `PruneMissing()` (köksüz) — ama build-state.json için DEĞİL, o bir karar defteri ve ağ/çıkarılabilir sürücüdeki başka bir workspace geçici yoksa kayıtları uçar. Riski daha da azaltmak için: kök dışı ölü girişleri yalnız sürücü/kök erişilebilirken (Directory.Exists(Path.GetPathRoot)) buda. (c) Optimize satırına 'pruned N stale-schema, M outside-root' sayacı. Tek seferlik kullanıcı temizliği için ayrıca bir şey gerekmez — Optimize bir kez basılır.

**Çözüm (doğrulayıcı düzeltmesi):** Yalniz (a): Optimize'da Schema != CurrentSchema girislerini kok bagimsiz buda — 2558 Temp girisinin hepsi Schema=0 oldugu icin tek basina yeterli ve 'kok disi korunur' kararina dokunmaz. (b) kullanici kararina birakilmali.

**Değişmezler:** Kopya yasağı: 'ölü giriş' kararı tek yerde (RootScope yanına 'DeadEntryRule' ya da her cache'in kendi Prune'unda tek predicate); OutDir'e dokunulmaz.

**Test fikri:** Cache'e Schema=0 bir giriş + kök dışı var-olmayan yolla bir giriş yaz; Optimize koş; ikisi de gitmeli, kök içi canlı giriş kalmalı; build-state'te kök dışı giriş KALMALI (ayrı pin).

**Doğrulayıcı notu:** Kod iddialari dogru: EvaluationCache.cs:54 sema kapisi, :122-130 PruneMissingUnderRoot yalniz kok alti. Olcum dogrulandi: 5.914.117 B / 2761 giris; 2558 Temp girisi (1,65 MB = baytin %28'i, girislerin %93'u); Schema {0:2565, 1:196}; kalan 203 girisin hepsi diskte var. SourceHashCache konumu YANLIS: dosya 209 satir, budama 176-185'te (328-337 yok). Performans etkisi kucuk: tum dosyanin Python parse'i 20 ms, cop pay bunun ~%28'i; bu bir hijyen bulgusu, 'onemli' degil. Fix (b) (kok disi 'dosyasi yok' budamasi) EvaluationCache.cs:117 ve ARCHITECTURE §16 (~5098) 'kok disindaki girdiler korunur' bilincli karariyla celisir.

#### D9-3 — Her Sync ve her koşu iki büyük cache'i (5,9 + 6,25 MB) sıfırdan parse edip değişiklik olmasa da bütünüyle yeniden yazıyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 40-49 — WorkspaceServices.Default: root => new SyncWorkspaceService(… new EvaluationCache(path) … new SourceHashCache(path)) — her Sync komutunda yeni örnek = yeni Load
- `src/BuildOrchestrator.Supervisor/Program.cs` satır 92-97, 201 — Her koşuda yine new SourceHashCache + new EvaluationCache; hashes.Flush() koşulsuz
- `src/BuildOrchestrator.Core/Planning/BuildPlanBuilder.cs` satır 42-43 — cache.Flush() koşulsuz — hiçbir csproj değişmese de 5,9 MB yazım
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` satır 324 — hashes.Flush() koşulsuz — 6,25 MB yazım + 24.588 girişlik 'persistable' kopyası
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 96-112, 142-151 — Dirty bayrağı yok; Load File.ReadAllText → string → Deserialize
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 148-166, 195-208 — Aynı desen; Flush her seferinde ToDictionary kopyası + Serialize → string → WriteAllText
- `src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs` satır 70-72, 180-184, 216-245 — Pencereye her dönüşte (son Sync'ten 5 sn geçtiyse) sessiz Sync — HEAD aynı olsa da

**Kanıt:** Bir Sync = EvaluationCache.Load (5,9 MB oku → ~12 MB UTF-16 string → parse) + SourceHashCache.Load (6,25 MB → ~12,5 MB string) + BuildStateStore.Load (149 KB) + EvaluationCache.Flush (Serialize → ~4-12 MB string → WriteAllText tmp → Move) + SourceHashCache.Flush (aynı). Her koşu (Program.BuildRunPlan) aynı dört işlemi bir daha yapar. ARCHITECTURE §16 'nothing to change → not rewritten' cümlesi yalnız Clean/Optimize için doğru (BuildStateStore.Write mutate=false → dokunmaz); Sync/koşu yolunda karşılığı yok. Python referans ölçümü (aynı dosyalar, .NET değil): eval parse 18 ms / serialize 18 ms; hash parse 29 ms / serialize 23 ms; okuma 5-7 ms. Pencere aktivasyonu tetiği: AutoSyncCoordinator doc'u 'Pencereye dönüş HEAD aynı olsa da sessiz yeniler'.

**Etki:** Sync başına ~12 MB okuma + ~12 MB yazma + tahminen 35-50 MB geçici LOH tahsisi (UTF-16 string'ler + serialize string'leri) → Gen2 GC; koşu başına aynısı bir daha. Pencereye her dönüş bunu tetikler. Süre olarak tahmin: sıcak diskte 100-250 ms/Sync (parse+serialize+yazım), Defender on-access ile daha fazla; 'bosta cihaz rahatlasın' ve 'ön plana geçişte takılma' başlıklarına doğrudan girer.

**Etki (doğrulayıcı düzeltmesi):** Kesin: Sync ve kosu basina 5,9+6,26 MB okuma ve 5,9+6,26 MB yazim (degisiklik olmasa da). Olculen referans (Python, .NET degil): parse 20 ms + 22,6 ms. '100-250 ms/Sync' ve '35-50 MB LOH' TAHMIN. OLCUM'deki aktivasyon Sync'i Supervisor'da 610 Mcycles/s x 10 s tuketiyor; bunun ne kadarinin cache IO oldugu olculmedi (tarama + 24,6 bin stat + git de ayni pencerede). Supervisor Private 151->242 MB artisinin bu tahsislerle iliskisi dogrulanmadi.

**Çözüm:** (1) Dirty bayrağı: EvaluationCache/SourceHashCache'te _dirty; GetOrEvaluate/HashOf yeni giriş yazınca true; Flush() dirty değilse hiç dokunmaz (§16 cümlesi Sync'e de uzar). (2) Tek örnek: WorkspaceServices.Default ve Program.BuildRunPlan aynı cache örneklerini paylaşsın (Program.Main'de bir kez kur, factory'lere kapat); Load'ı yalnız dosyanın mtime+size'ı belleğe alındığından farklıysa tekrarla (Optimize aynı örneği buduğu için çakışma yok; App tek Supervisor koşturur, eşzamanlı Sync+run zaten host tarafından serileşir). EvaluationCache.Flush'taki 'iki örnek paralel flush' savunması (satır 84-90) korunur. (3) Utf8 akış API'si: File.OpenRead + JsonSerializer.Deserialize<T>(Stream) ve Utf8JsonWriter/FileStream ile Serialize(Stream) — 12 MB'lık ara string'ler kalkar. (4) SourceHashCache.Flush'taki ToDictionary kopyası yerine Utf8JsonWriter ile doğrudan filtreleyerek yaz.

**Çözüm (doğrulayıcı düzeltmesi):** Once yalniz (1) dirty bayragi: en dusuk risk, yazimin tamamini kaldirir ve §16 cumlesini dogru yapar (SourceHashCache'te bayrak yalniz KALICI yazilabilir giris degisince kalkmali; racy girisler zaten diske inmiyor). (2) Tek ornek paylasimi su haliyle guvenli DEGIL: EvaluationCache._entries duz Dictionary ve EvaluationCache.cs:84-90 doc'u kosunun planner thread'i ile Sync'in ESZAMANLI olabildigini soyluyor (startRun komut dongusunu bloklamaz) — paylasim kilit gerektirir; ayri bir adim olarak ve olcumden sonra. (3)/(4) stream API D9-8 ile ayni is.

**Değişmezler:** Planlama Core'da (cache'ler Core'da kalır, yalnız yaşam süresi Supervisor kompozisyon kökünde uzar); kopya yasağı (yol adları zaten tek yerde).

**Test fikri:** Değişmemiş workspace'te iki ardışık Sync: ikinci Sync'ten sonra evaluation-cache.json ve source-hash-cache.json mtime'ları DEĞİŞMEMELİ (bugün kırmızı). Tek csproj'a dokun → yalnız evaluation-cache yazılır.

**Doğrulayıcı notu:** Kod dogrulandi: SupervisorHost.cs:44-48 her Sync'te yeni EvaluationCache+SourceHashCache (ctor'da Load); Program.cs:94,97 her kosuda yine; BuildPlanBuilder.cs:43 cache.Flush() kosulsuz; SyncWorkspaceService.cs:324 ve Program.cs:201 hashes.Flush() kosulsuz; EvaluationCache.cs:96-112 ve SourceHashCache.cs:148-166'da dirty bayragi yok, ikisi de string uzerinden Serialize/WriteAllText. AutoSyncCoordinator.cs:72 (5 sn esigi) ve :236-237 (HEAD ayni olsa da yeniler) dogru. ARCHITECTURE §16 satir 5105 'nothing to change -> not rewritten' cumlesi Sync/kosu yolunda kodla UYUSMUYOR (dokuman-kod celiskisi, kullaniciya bildirilmeli). Sure/LOH rakamlari ise olculmedi.

#### D9-4 — evaluation-cache.json faydalı yükünün %91'i CompileFiles/ResourceFiles mutlak yolları — proje klasörü içindekiler zaten klasör taramasıyla toplanıyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.Core/Discovery/CsprojEvaluator.cs` satır 27-45 — EvaluatedProject.CompileFiles / ResourceFiles mutlak yol listeleri cache'e olduğu gibi giriyor
- `src/BuildOrchestrator.Core/Incremental/ProjectInputs.cs` satır 74-85 — Add(CompileFiles) + Add(ResourceFiles) sonra SweepFolder(projectDir) — SortedSet aynı yolları tekilleştiriyor; klasör içi liste gereksiz
- `src/BuildOrchestrator.Core/Discovery/CsprojEvaluator.cs` satır 36-42 — ResourceFiles doc'u zaten 'klasör içi zaten görülür; bu liste dışarı link'lenen dosyayı yakalar' diyor

**Kanıt:** 203 gerçek giriş 4.351.361 B; alan bazında: CompileFiles 2.756.135 B, ResourceFiles 1.198.526 B, HintPaths 205.281 B, geri kalanı <100 KB. Giriş büyüklüğü medyan 8,7 KB, en büyük 351 KB (tek proje). CompileFiles'ın tek tüketicisi ProjectInputs.cs:77 (grep). Her yol 'D:\Projects\Delta\OSYS\…' önekini taşıyor.

**Etki:** Cache'in %91'i, klasör taramasının zaten bulduğu dosyaların tekrarı; D9-3'teki her parse/serialize bu yükü taşıyor. Tahmin: yalnız proje klasörü DIŞINDAKİ link'ler saklansa faydalı yük 4,35 MB → <0,5 MB (Türkçe: ~10 kat küçülme).

**Etki (doğrulayıcı düzeltmesi):** evaluation-cache faydali yuku ~4,26 MB -> ~0,3 MB (3,97 MB liste alani duser). Zaman kazanci olculmedi; D9-3 sonrasi yalniz okuma/parse tarafinda kalir.

**Çözüm:** Şartlı (eşdeğerlik doğrulanmalı): EvaluatedProject'e proje klasörü DIŞINDA kalan Compile/Resource yollarını taşıyan tek bir liste ekle (ya da mevcut listeleri evaluate anında proje-göreli + klasör-dışı filtreyle doldur); ProjectInputs zaten klasörü süpürdüğü için karar değişmez. Diskte olmayan ama csproj'da listelenen dosya bugün de HashOf=null ile terim dışı kalıyor (SourceHashCache.cs:218-238), yani klasör içi listeyi düşürmek imzayı değiştirmez. CurrentSchema'yı 2 yap → 203 csproj bir kez yeniden değerlendirilir (saniyeler). ARCHITECTURE §6.2 'each entry records the schema' cümlesi zaten bunu anlatıyor.

**Çözüm (doğrulayıcı düzeltmesi):** Filtre 'SweepFolder'in ULASAMAYACAGI yollar' olmali: proje klasoru disi VEYA yol icinde IsSkippedFolder segmenti olanlar (Add() atlanan klasoru elemiyor, evaluator yalniz bin/obj atliyor — ProjectInputs.cs:65-70, CsprojEvaluator.cs:144-145). Esdegerlik testi gercek OSYS projelerinde yol kumesinin birebir ayni kaldigini pinlemeli. D9-3'ten SONRA ve ayri karar olarak.

**Değişmezler:** İçerik kararı değişmez (aynı girdi kümesi); planlama Core'da; §7.1 imza formülü değişmez — değişirse §7 'Upgrading rebuilds everything once' notu gereği kullanıcıya söylenmeli.

**Test fikri:** Gerçek bir OSYS projesinde eski ve yeni EvaluatedProject ile ProjectInputs.CollectWithFolders çıktısı (paths kümesi) BİREBİR aynı olmalı; klasör dışına link'lenmiş .cs içeren sentetik csproj'da link yeni listede yaşamalı.

**Doğrulayıcı notu:** Kod dogru: CompileFiles/ResourceFiles'in tek tuketicisi ProjectInputs.cs:77-78, ardindan :82 SweepFolder ayni klasoru supuruyor (SortedSet tekillestirir). Olctum: 203 gercek giriste 30.829 Compile+Resource yolunun TAMAMI proje klasoru icinde, 0'i disarida, 0'i atlanan klasor (bin/obj/.git/.vs/node_modules) altinda; alan baytlari CompileFiles 2.762.199 + ResourceFiles 1.205.648. Yani bugun listeler karara HICBIR sey eklemiyor. Ancak D9-3'un dirty bayragi yapilinca kalan kazanc yalniz Load parse'idir (tum dosya Python'da 20 ms); sema atlatma + karar girdisi degisikligi riski buna gore yuksek. Fix'teki 'yalniz klasor DISI' filtresi eksik.

#### D9-5 — build-state.json her proje sonucunda tamamen okunup yeniden yazılıyor (Load+Serialize+rename × proje sayısı) ve Load() koşu içinde 5 ayrı yerden tekrarlanıyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.Core/State/BuildStateStore.cs` satır 196-212 — Write(): her Upsert/Remove/Invalidate = Load() (149 KB oku+parse) + Serialize + AtomicFile.WriteAllText
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2159-2183 — PersistBuildStateOnSuccess → Upsert, proje başına; worker'ın slot'u bırakmadan önce (1300-1360 aralığında Complete'ten önce)
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 771, 879, 1933, 2253 — Aynı koşuda ayrı Load() çağrıları (Cycles hafızası, önizleme, grup hafızası, hata invalidasyonu)
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1929-1948 — Grup hafızası: üye başına ayrı Upsert (17 üyeli UI grubunda 17 tam yazım)
- `src/BuildOrchestrator.Core/State/BuildDurationPersister.cs` satır 20-26, 35-45 — Load+Upsert yapan yardımcı; wiring notuna göre hiç çağrılmıyor (grep: src'de çağıran yok) — ölü kod

**Kanıt:** Dosya 148.959 B / 196 giriş (giriş 530-1054 B; alanlar: ProjectId 23 KB, BuiltSignature 12,7 KB, BuiltContent 12,7 KB, FedOutputs 6,8 KB). 187 projelik bir Build'de proje başına 149 KB oku + 149 KB yaz ⇒ ~56 MB IO ve 374 JSON (de)serialize; Cycles'ta her tur her üye için yine. §8.7 uçuş defteri (run-inflight.json) zaten çökme kurtarmasını tek başına sağlıyor: Add dispatch'te, Remove sonuçta; build-state'in ANINDA yazılmasının kurtarma için gerekçesi yok — çökmede yazılmamış başarı yalnız 'yeniden derlenir' (güvenli yön).

**Etki:** Bugünkü ölçekte tahmin: yazım başına 2-5 ms, koşu başına 0,5-2 s toplam, worker slot'unun içinde (Complete'ten önce) ama derleme sürelerinin yanında küçük. Ölçek n²: 1000 projede ~750 KB × 2000 işlem ≈ 1,5 GB/koşu. Eşzamanlı 4 worker aynı SemaphoreSlim'de sıralanıyor.

**Etki (doğrulayıcı düzeltmesi):** 152 derlemelik kosuda ~152 x (149 KB oku + 149 KB yaz) ~= 45 MB IO, 142 s icinde; yazim basina sure olculmedi (tahmin: ms mertebesi). '1000 projede 1,5 GB' ekstrapolasyon, bu workspace icin gecersiz (187 proje).

**Çözüm:** RunContext'e koşu başına TEK bellek kopyası (Load bir kez, 771/879/1933/2253 hepsi ondan okusun). BuildStateStore'a 'toplu mutasyon + tek yazım' API'si: Upsert bellekte, `Flush()` (a) sonuç raporlarında birleştirilerek (debounce ≤ 1 s ya da her N sonuçta bir), (b) koşunun her çıkışında (ExecuteRunAsync finally, ledger.Clear'ın hemen ÖNCESİNDE — uçuş defteri boşalmadan önce durum diske inmiş olsun), (c) Stop/Interrupt'ta. InvalidateWithoutEvidence açılış kurtarmasında zaten Write ile anında (aynı kalabilir). Sync yolunun Load'ı değişmez (salt-okur). BuildDurationPersister'ı sil ya da bağla (ölü kod, kopya yasağıyla çelişen ikinci bir partial-merge yolu).

**Çözüm (doğrulayıcı düzeltmesi):** Birlestirme (debounce) su haliyle ONERILMEZ: (a) RunCoordinator.cs:1350-1353 'sonuc raporlandi VE defter yazildi -> ucustan dus' sirasina dayaniyor; bellekte bekleyen yazimla ledger.Remove onceye gecer. (b) Hata gecersizlemesi (InvalidateBuildStateOnFailure) gecikirse ve motor olurse (App kapaninca Supervisor 81 ms'de gidiyor — olculdu) eski 'Succeeded+eslesen imza' kaydi kalir ve bozuk proje pre-skip edilir. (c) Dort ayri BuildStateStore ornegi var (Program.cs:31, SupervisorHost.cs:47,51,56), her birinin kendi _writeGate'i — bellek kopyasi baska ornegin yazimini gormez. Guvenli kisim: BuildDurationPersister olu kodunu sil; istenirse once yazim suresini olc.

**Değişmezler:** §8.7 kurtarma: uçuş defteri anında yazılmaya devam eder; çökmede yalnız yazılmamış BAŞARILAR kaybolur → o projeler bir sonraki Build'de derlenir (güvenli yön). Doc §16/§8.7'ye 'build-state coalesced, flushed at run end' cümlesi eklenir.

**Test fikri:** Sahte invoker ile 50 projelik koşu: BuildStateStore'a enjekte edilen yazım sayacı bugün ≥50, hedefte ≤ ceil(süre/1 s)+1; koşu sonunda dosya içeriği bugünküyle BİREBİR aynı map. Koşu ortasında process kill simülasyonu: uçuş defteri dolu, build-state'te yarım kayıt yok.

**Doğrulayıcı notu:** Mekanizma dogru: BuildStateStore.cs:199-214 Write = Load + Serialize + AtomicFile.WriteAllText; PersistBuildStateOnSuccess (RunCoordinator.cs:2159-2183) proje basina ve Scheduler.Complete'ten ONCE (try icinde :1319, Complete finally'de :1338). BuildDurationPersister src'de cagrilmiyor (yalniz doc cref :2225 + testleri) — olu kod dogru. Ama baslik abartili: 771 (yalniz Cycles, kosu basina 1), 879 (kosu basina 1), 1933 (grup bitisinde 1), 2253 (yalniz kanitli hata) proje basina degil. Dosya 148.870 B; OLCUM kosusunda 152 derleme => 152 yazim, 142 s'ye yayilmis; sure hic olculmedi. No-op kosuda Upsert yok. Onerilen birlestirme §8.7 siralamasini bozar.

#### D9-6 — Proje logunda satır başına AutoFlush: her MSBuild satırı ayrı bir WriteFile çağrısı (bir WPF projesinde 8.800-12.000 satır)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 124 — ProjectLogFile: new StreamWriter(new FileStream(...)) { AutoFlush = true }
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 146-155 — Snapshot() zaten _writer.Flush() yapıyor → buffered yazımda dikiş bozulmaz
- `src/BuildOrchestrator.Core/Logs/RunLogWriter.cs` satır 24 — decision.log AutoFlush=true — 196 satır/koşu, kalsın
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2129-2133 — Emit: AppendLine pump thread'inde SENKRON — yazım gecikmesi child stdout pipe'ını geri-baskılar
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 130-141 — SafeOnLine kilit altında; stdout/stderr pump'ları onLine'da serileşir

**Kanıt:** run-20261001-010430: 34 dosya 27,4 MB; en büyük proje logu 4,2 MB / 11.969 satır (11.896'sı warning), Types.UsedCars 8.824 satırın 7.288'i MSB3277. Koşu başına toplam ~70 bin satır ⇒ ~70 bin WriteFile syscall (fsync değil, yalnız OS'a teslim). Her AppendLine, MSBuild'in stdout okuyucusunda senkron koşuyor.

**Etki:** Tahmin (ölçülmedi): syscall başına 5-20 µs → büyük projede 0,05-0,25 s pump thread zamanı, koşu başına toplam ~0,5-1,5 s, 4 pump thread'e dağılmış; kritik yolda değil ama pipe 4 KB dolduğunda MSBuild bloklanır (Defender on-access ile her WriteFile daha pahalı). Kazanç küçük ama bedava.

**Etki (doğrulayıcı düzeltmesi):** Kosu basina satir sayisi kadar WriteFile: olculen 23.047 (gercek Rebuild) – 112.011 (en buyuk kosu). Sure tahmini (syscall basina 5-20 us => 0,1-2 s/kosu, 4 pump thread'e yayilmis) OLCULMEDI.

**Çözüm:** AutoFlush=false + 64 KB buffer (new StreamWriter(fs, UTF8, 65536)). Flush noktaları zaten var: Snapshot() (satır 150) ve Dispose (satır 163, StreamWriter.Dispose flush eder). Ek olarak invoke bittiğinde (BuildProjectAsync'in using'i zaten Dispose ediyor) — başka bir şeye gerek yok. Veri kaybı senaryosu: Supervisor'ın SERT ölümü (App'in outer job'ı, Görev Yöneticisi) son ≤64 KB satırı kaybeder; o anda koşu zaten ölmüş ve §8.7 projeyi 'kanıtsız hata' sayıyor — log tam olsa da kullanılmazdı. MSBuild'in kendi çökmesi etkilemez (satırlar Supervisor belleğinde). decision.log'a dokunma (küçük, tanı kaydı).

**Çözüm (doğrulayıcı düzeltmesi):** Oneri gecerli (AutoFlush=false + buyuk buffer; Snapshot ve Dispose zaten flush ediyor). Ek not: dosya FileShare.Read ile acik — kosu sirasinda log dosyasini disaridan acan kullanici son <=64 KB'yi goremez; kabul ediliyorsa uygula. ProjectLogFile'in testlerinde 'AppendLine sonrasi dosyayi dogrudan oku' kalibi varsa Snapshot'a cevrilmeli.

**Değişmezler:** Disk log 'tek gerçek kaynak' (§8.5) korunur — Snapshot/Dispose flush'ı sayesinde okuyucu asla eksik satır görmez; T28 satır-numarası dikişi değişmez.

**Test fikri:** ProjectLogFile'a 10 bin satır yaz, Snapshot al: satır sayısı ve metin bugünküyle aynı; Dispose sonrası dosya tam. Perf pin: enjekte edilebilir FileStream sayacı ile 10 bin AppendLine'da Write çağrısı sayısı ≤ 10.000/ (64K/ortalama satır) mertebesine düşmeli (bugün 10.000).

**Doğrulayıcı notu:** RunLogWriter.cs:124 AutoFlush=true (FileStream varsayilan buffer) — satir basina bir flush; :150 Snapshot flush ediyor, :163 Dispose flush ediyor; RunCoordinator.cs:2130-2134 Emit AppendLine'i senkron cagiriyor; MsBuildInvoker.cs:139-141 SafeOnLine kilidi dogru. Satir sayisi duzeltmesi: OLCUM'de gercek Rebuild 23.047 satir (70 bin degil); diskteki en buyuk kosu (run-20260915-145051-165) 112.011 satir. Syscall suresi olculmedi.

#### D9-7 — Log hacminin çoğu MSBuild MSB3277 uyarı tekrarları — argüman sözleşmesi (§9.2) sabit, yalnız kullanıcı kararıyla değişebilir

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / orta |

**Konum:**
- `src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs` satır 22-28 — '-clp:Summary -nologo' — verbosity varsayılan (normal); uyarı filtresi yok
- `ARCHITECTURE.md` satır 1640-1678 — §9.2 argüman sözleşmesi pinli (test guard'ı var)

**Kanıt:** Cycles koşusunun 11 büyük logunda satırların %75-99'u 'warning'/MSB3277 (ör. 7.288/8.824, 11.896/11.969). Bu uyarılar OSYS'in gerçek assembly-sürüm çakışmalarıdır (araç üretmiyor).

**Etki:** 27 MB/koşu diskin ve D9-6'daki satır sayısının ana kaynağı. UI'a giden projectLog olayı da satır başına (D5/D3 boyutu).

**Etki (doğrulayıcı düzeltmesi):** MSB3277 satirlari olculen en buyuk kosuda %34 (37.898/112.011); arac tarafinda yapilacak is yok.

**Çözüm:** Şartlı — §9.2 bilinçli sözleşmeyle çelişir, ÖNERMİYORUM: '-nowarn:MSB3277' gerçek bir uyarıyı gizler, '-v:minimal' derleyici hata bağlamını inceltir. Doğru yer OSYS'in kendi binding-redirect/paket hizalaması (proje dışı). Aracın tarafında yapılacak tek şey D9-1 retention'ı. Bu bulguyu 'bilgi' olarak taşıyorum; kullanıcı loglarda uyarı istemiyorsa ayrı bir karar.

**Değişmezler:** §9.2 argüman sözleşmesi değişmez; OutDir'e dokunulmaz.

**Test fikri:** Yok (değişiklik önerilmiyor).

**Doğrulayıcı notu:** MsBuildArguments.cs:22-28 dogru ('-clp:Summary -nologo', verbosity/nowarn yok). Olctum: en buyuk kosuda 112.011 satirin 37.898'i MSB3277 (%34) — 'cogu' proje bazinda dogru olabilir ama kosu toplaminda degil. Bulgu zaten degisiklik onermiyor; §9.2 sozlesmesiyle uyumlu.

#### D9-8 — System.Text.Json reflection + string tabanlı IO: üç defter File.ReadAllText→Deserialize<string> ve Serialize→string→WriteAllText ile çalışıyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) BuildStateStore.cs:45-47 ve 208 (205 degil)
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 103, 147 — Serialize(_entries) → string; Deserialize(File.ReadAllText)
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 159, 201 — Aynı desen
- `src/BuildOrchestrator.Core/State/BuildStateStore.cs` satır 45-47, 205 — ReadAllTextSharingDelete → Deserialize; Serialize → AtomicFile.WriteAllText(string)
- `src/BuildOrchestrator.Core/State/AtomicFile.cs` satır 25-41 — Yalnız string kabul ediyor; Stream/Utf8JsonWriter kabul eden bir overload yok

**Kanıt:** grep: src altında JsonSerializerContext/JsonSerializable yok; Directory.Build.props'ta IsAotCompatible/ReadyToRun yok; Supervisor.csproj sade. Her Load 5,9-6,25 MB UTF-8 dosyayı önce UTF-16 string'e (2×) sonra nesneye çeviriyor; her Flush nesneyi string'e, string'i UTF-8'e.

**Etki:** Tahmin: Supervisor'da ilk (de)serialize çağrısında reflection metadata kurulumu on ms'ler (her motor açılışı, yani App açılışında bir kez); Sync başına ~24-36 MB geçici string tahsisi (LOH) — D9-3'ün GC bedelinin yarısı. Source generator hem bunu hem trim/AOT uyumunu getirir; runtime içeriği değişmez.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. Kesin olan: her Load 5,9/6,26 MB dosyayi once string'e sonra nesneye ceviriyor; D9-3'un dirty bayragindan sonra yalniz okuma tarafi kalir.

**Çözüm:** Core'a tek `LedgerJsonContext : JsonSerializerContext` ([JsonSerializable(typeof(Dictionary<string,EvaluationCache.Entry>))] vb. — Entry'ler private record; internal'a çek ya da context'i aynı dosyada nested tut). AtomicFile'a `Write(string path, Action<Stream> body, retryDelay)` overload'u; Load'larda `JsonSerializer.Deserialize(stream, ctx.X)`. Contracts'taki IPC JSON ayrı boyut (D5), buraya karıştırma.

**Değişmezler:** Kopya yasağı: tek context, üç defter aynı yerden; §16 atomik yazım protokolü (temp+rename) aynen.

**Test fikri:** Mevcut disk dosyalarını yeni yolla yükle-yaz-yükle: içerik eşitliği (round-trip); bozuk/boş dosya davranışı (boş map) korunur.

**Doğrulayıcı notu:** Konumlar dogru: EvaluationCache.cs:103,147; SourceHashCache.cs:159,201; BuildStateStore.cs:45-47,208; AtomicFile.cs:25-41 yalniz string aliyor. src'de JsonSerializerContext/JsonSerializable yok (grep bos). Etki rakamlari (reflection kurulumu, LOH) olculmedi.

#### D9-9 — Windows Defender on-access taraması: logs/cache klasörü ve OSYS bin/obj için dışlama önerisi dokümante değil

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `ARCHITECTURE.md` satır 769-771 — Soğuk ilk geçişte dosya başına 8,9 ms'yi 'on-access scanning' açıklıyor — ama README'de öneri yok
- `README.md` satır 1-40 — Gereksinimler/kurulum bölümünde Defender/antivirüs satırı yok (grep: Defender/exclusion eşleşmesi yok)

**Kanıt:** Doc kendisi on-access taramanın dosya açılış maliyetini belirlemesini ölçmüş (8,89 ms → 1,86 ms paralelde). Araç koşu başına 25 bin log dosyası klasörüne + 12 MB JSON'a yazıyor; MSBuild obj/bin'e binlerce dosya. Kullanıcı ortamı kararı — kodla çözülmez.

**Etki:** Tahmin: Defender dışlaması cold-pass ve her Flush/AppendLine'ın syscall maliyetini belirgin düşürür; ölçmeden rakam vermiyorum.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. Dokumanin kendi olcumu yalniz soguk ilk indeksleme icin (dosya basina 8,9 ms sirali).

**Çözüm:** README 'Performance notes' alt bölümü: isteğe bağlı dışlamalar — %LOCALAPPDATA%\BuildOrchestrator (logs + üç defter), repo köklerindeki bin/obj, MSBuild.exe/csc.exe process'leri; 'kurumsal politika izin veriyorsa' notu; Set-MpPreference örneği. Kod değişikliği yok.

**Değişmezler:** —

**Test fikri:** Yok (doküman).

**Doğrulayıcı notu:** ARCHITECTURE.md:769-771 (§7.1 'Why disk and not commits') on-access taramayi soguk gecisin baskin maliyeti olarak yaziyor (8,9 ms -> 1,9 ms/dosya); README ve ARCHITECTURE'da Defender/exclusion onerisi yok (grep bos). Kod degisikligi degil; kazanc olculmedi.

#### D9-10 — Durum klasöründe hiçbir kodun okumadığı eski sürüm artıkları (config.json, settings.json, dependency-graph.json) ve §16'da yer almıyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `ARCHITECTURE.md` satır 5063-5075 — §16 tablosu 5 dosya + worktrees\ sayıyor; bu üç dosya yok
- `src/BuildOrchestrator.Core/Paths/LegacyWorktreePool.cs` satır 1-30 — worktrees\ için 'oturum başına bir kez konsola not' var; diğer artıklar için yok

**Kanıt:** Diskte: config.json (337 B, 2026-06-01), settings.json (372 B, 2026-06-18), dependency-graph.json (3,8 KB, 2026-06-18). grep src: 'dependency-graph.json', '"config.json"', '"settings.json"' eşleşmesi yok (App'in SettingsFile'ı 'build-orchestrator-settings.json' dışa aktarım dosyasıdır).

**Etki:** Sıfır performans etkisi (4,5 KB); yalnız doc-kod tutarlılığı ve 'neyi silebilirim' sorusu.

**Çözüm:** Optimize'ın süpürme adımına 'bilinen eski dosya adları' listesi (tek sabit, LegacyWorktreePool'un yanında) ekle ve §16'ya 'older versions may have left …; Optimize removes them' cümlesi — ya da yalnız doc'a yaz, dokunma. Kullanıcı kararı.

**Değişmezler:** —

**Test fikri:** Optimize sandbox'ta eski adlı dosya oluştur → silinir; bilinmeyen dosyaya dokunulmaz.

**Doğrulayıcı notu:** Diskte dogrulandi: config.json 337 B, settings.json 372 B, dependency-graph.json 3797 B; src'de bu adlari okuyan kod yok (grep bos); §16 tablosu (ARCHITECTURE ~5063-5075) bunlari saymiyor. Performans etkisi sifir.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs:131-142 — Prefill'in stat gecisi ve hash gecisi SABIT 16 yollu paralel (WithDegreeOfParallelism(16) / MaxDegreeOfParallelism=16), cekirdek sayisina gore olceklenmiyor; her Sync ve her kosuda ~24,6 bin yol icin kosar. §7.1 olcumu 8 thread'li makinede alinmis; 2-4 cekirdekli makinedeki etkisi olculmedi (tahmin).
- src/BuildOrchestrator.Core/Logs/RunLogWriter.cs:146-154 — Snapshot() ProjectLogFile._gate ALTINDA tum dosyayi ReadToEnd ile okuyor; canli projenin logu aciliyken (olculen en buyuk tek log 4,2 MB) ayni kilidi bekleyen AppendLine pump thread'i ve dolayisiyla MSBuild stdout'u o sure bloklanir. Bulgu ajaninin 'kilit DISINDA okunuyor' notu yalniz koordinator kilidi icin dogru.
- src/BuildOrchestrator.Supervisor/RunCoordinator.cs:2132-2133 + RunLogWriter.cs:141 — SanitizeLine her satir icin IKI kez kosuyor (AppendLine icinde ve ProjectLogEvent icin); satir basina iki IndexOfAny taramasi (kosu basina 23-112 bin satir).
- src/BuildOrchestrator.Core/State/BuildStateStore.cs:199-214 + Program.cs:31 + SupervisorHost.cs:47,51,56 — dort ayri BuildStateStore ornegi ayni dosyaya yaziyor, _writeGate ornek basina; Load->mutate->rename dizisi ornekler ARASINDA serilesmiyor (kosu sirasinda Clean/Sync komutu gelirse son yazan kazanir). Komut kapilari bunu fiilen engelliyor olabilir — dogrulanmadi.
- src/BuildOrchestrator.Core/Discovery/CsprojEvaluator.cs:216-217 — SDK-style projede Directory.EnumerateFiles(dir, '*.cs', Recurse) bin/obj'un ICINE girip sonra eliyor (ProjectInputs.SweepFolder'in doc'u tam bunu 'boşuna gezmek' diye yasakliyor); yalniz cache miss'te ve SDK-style projede kosar.
- src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs:57,61 — degisen csproj'da dosya iki kez okunup iki kez SHA-256'laniyor (57'de karsilastirma, 61'de kayit); yalniz miss yolunda, kucuk.
- ARCHITECTURE.md:5105 (§16) 'nothing to change -> file is not rewritten' cumlesi Sync/kosu yolundaki kosulsuz Flush'larla (BuildPlanBuilder.cs:43, SyncWorkspaceService.cs:324, Program.cs:201) uyusmuyor — dokuman-kod celiskisi olarak kullaniciya ayrica bildirilmeli.

**Temiz bulunan alanlar:**
- Atomik yazım protokolü (Core/State/AtomicFile.cs:25-73): örnek başına tekil <hedef>.<guid>.tmp + rename, sınırlı retry (20×5 ms), Delete-share'li okuma; üç defter aynı yolu paylaşıyor, kopya yok. Diskte öksüz .tmp bulunmadı (0).
- Uçuş defteri (Core/State/InFlightLedger.cs): dosya en fazla paralellik kadar (4 id, ~300 B), Add/Remove zaten listedeyse dosyaya dokunmuyor (satır 123/133), boşken siliniyor; koşu başına ~2×proje küçük yazım — kabul, değişiklik gerekmez. run-inflight.json boştayken diskte yok (doğrulandı).
- build-state.json'ın kendisi temiz: 196 giriş, 0 Temp/test artığı, 0 diskte-olmayan csproj; giriş boyutu 530-1054 B, alanlar makul (en büyük ProjectId yolu). Clean/Optimize 'değişiklik yoksa dosyaya dokunma' sözü kodda gerçek (BuildStateStore.Write mutate=false → return, satır 203).
- Test izolasyonu bugün sağlam: SupervisorSandbox (--logs <temp>) + SupervisorIsolationGuardTests kaynak guard'ı (83942f7, 2026-09-19 05:28). Gerçek cache'teki en yeni Temp girişi 2026-09-19 02:02 — guard'dan sonra yeni kirlilik YOK; ContentDecisionDiagnosticsTests gerçek köke yalnız salt-okur bakıyor.
- SourceHashCache tasarımı: stat-only hızlı yol (boyut+mtime eşitse dosya açılmaz), 16 kanallı paralel Prefill, git tarzı 2 sn racy penceresi; 24.588 girişin yalnız 62'si ölü, 34'ü Temp — birikme düşük.
- EvaluationCache isabet mantığı: mtime+size hızlı yol, farklıysa SHA-256 doğrulaması (touch'ta gereksiz evaluate yok), şema kapısı; gerçek 203 girişin hepsi diskte var, 196'sı güncel şemada.
- Koşu log klasörü ancak planlama BAŞARILI olunca açılıyor (RunCoordinator.cs:726 logFactory planner'dan sonra) — planFailed/msbuildNotFound artık boş klasör bırakmıyor. decision.log küçük (196 satır / 15 KB).
- Proje logu okuma yolu (RunLogWriter.SnapshotProjectLog / ReadProjectLogFromDisk + LogChunker 64 KB): büyük log kilit DIŞINDA okunuyor (RunCoordinator.cs:213-224), yalnız kaynak seçimi kilit altında; 4 MB'lık log tıklamada ~ms mertebesi, sorun değil.
- Optimize'ın kök kapsamı (Paths/RootScope) prefix tuzağına karşı doğru (sondaki ayraç, OrdinalIgnoreCase) ve orphan .tmp süpürme eşiği (TempFileSweeper) aktif yazımı koruyor.

**Açık sorular:**
- Log retention politikası: kaç koşu / kaç gün / kaç MB tutulsun (öneri 30 koşu, 14 gün, 500 MB)? Son BAŞARISIZ koşu süreden bağımsız korunsun mu? Temizlik motor açılışında arka planda mı, yoksa yalnız Optimize'a basınca mı koşsun?
- Buffered proje logu: Supervisor SERT öldürüldüğünde (App exit/Görev Yöneticisi) son ≤64 KB satırın kaybolması kabul edilebilir mi? (Koşu zaten ölmüş ve §8.7 projeyi kanıtsız hata sayıyor.)
- build-state yazımını birleştirme: Supervisor çökerse son ~1 sn'lik başarıların defter'e yazılmamış olup bir sonraki Build'de yeniden derlenmesi kabul edilebilir mi? (Uçuş defteri kurtarması değişmiyor.)
- Kök dışı ölü giriş budaması: bu makinede başka bir workspace (ağ/çıkarılabilir sürücü) aynı cache'leri paylaşıyor mu? Paylaşıyorsa evaluation/source-hash için yalnız 'sürücü erişilebilirken buda' kuralı yeterli mi, yoksa yalnız Temp altını mı budayalım?
- evaluation-cache'te klasör içi CompileFiles/ResourceFiles'ı düşürmek (D9-4) şema atlatır ve 203 csproj bir kez yeniden değerlendirilir (saniyeler) — istenir mi, yoksa yalnız D9-2/D9-3 ile yetinilsin mi?
- MSB3277 uyarı seli OSYS tarafında (binding redirect / paket hizalaması) çözülecek mi? Araç tarafında uyarıları filtrelemek §9.2 sözleşmesini değiştirir — istenmiyor varsayıyorum.
- Defender dışlaması kurumsal politikaya uygun mu (README'ye isteğe bağlı öneri olarak yazılsın mı)?
- Eski artık dosyalar (config.json, settings.json, dependency-graph.json): Optimize silsin mi, yoksa yalnız dokümante mi edilsin?


### D3-kapanis

#### D3-1 — Stop yalnız graceful drain: en yavaş uçuştaki projeyi bekler ve drain boyunca CPU cap KALKAR — 'durdurdum, hemen rahatlasın' isteğinin tersi

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | kisa / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) RunViewModel.cs:1552-1561 (StopAsync), 1581 (CanStop); RunCoordinator.cs:282-300 (TryRequestStop), 318-328 (DrainCapLocked); SupervisorHost.cs:361-366; ARCHITECTURE.md:213-247 (§4.5). §11.3 satır aralığı doğrulanmadı.
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1552-1581 — StopCommand tek tıklık; 1560: SendStopAsync(_currentRunId, StopKind.Graceful) — App HİÇ Hard göndermez; 1581 CanStop: Phase==Stopping iken ikinci tık kapalı
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 282-300, 318-327 — TryRequestStop: Graceful'da DrainCapLocked → TryWriteCapLocked(null) = HARD CAP kaldırılır; Hard'da innerJob.Terminate() (var ama App'ten gelmiyor)
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 361-366 — StopRunAsync: Kind==Hard → innerJob.Terminate() — sözleşme ve motor hazır
- `ARCHITECTURE.md` satır §4.5 (211-247), §11.3 (2197-2205) — Bilinçli karar: 'Hard stop … the App never sends it'; 'Once a drain begins the CPU cap is removed'

**Kanıt:** Ölçülen koşu (run-20261001-010430-305 decision.log): UI grubunun 2. turunda üye başına 21-26 s, toplam UsedCars 37360ms, SparePart.Finance 47262ms (iki tur). Balanced'da 4 slot → Stop'a basıldığında en kötü ~26 s daha 4 MSBuild.exe koşar ve DrainCapLocked cap'i (%70) kaldırdığı için o 26 s'de makine STOP ÖNCESİNDEN DAHA YÜKLÜ olur. Kullanıcı 'durdurdum, rahatlasın' bekliyor; bugünkü davranış 'durdurdum, daha çok yüklendi, sonra rahatladı'.

**Etki:** Kullanıcı algısı: Stop'un etkisi ~20-30 s gecikir ve o pencerede CPU tavanı yok. Hard stop mevcut sözleşmede hazır (tek satır); bedeli §4.5'te yazılı: en çok parallelism (4) proje failed('stopped') olur, bir sonraki Build onları yeniden derler; ortak bin'e post-build copy ortasında yakalanırsa yarım DLL (ledger sayesinde bir sonraki açılışta geçersizlenir, ama VS o arada yarım DLL'e link'leyebilir).

**Etki (doğrulayıcı düzeltmesi):** Stop sonrası bekleme = uçuştaki en yavaş projenin kalan süresi (koddan kesin); süre 21-26 s rakamı tek bir koşunun UI grubuna ait, genel değil. Drain boyunca %70 hard cap kalkar (koddan kesin); zayıf 2-4 çekirdekli makinede 4 MSBuild + csc cap'siz koşar — yük artışının büyüklüğü ÖLÇÜLMEDİ.

**Çözüm:** KULLANICI KARARI — üç seçenek, mevcut yapıyı bozmadan:
(a) 'İkinci Stop = hard': CanStop'u Stopping fazında da açık bırakıp ikinci tıkta/Esc'te StopKind.Hard gönder (RunViewModel.cs:1560 kind parametresi zaten var; Supervisor tarafı hazır: RunCoordinator.cs:294). Buton metni 'Stop now'. +Anında rahatlama (TerminateJobObject ~20-30 ms, CascadeKillTests spike). −≤4 proje yeniden derlenir; ortak bin'de yarım kopya riski (§4.5). Sadece ikinci tıkta olduğu için varsayılan davranış değişmez.
(b) Drain sırasında cap'i KORU (DrainCapLocked'ı kaldır): makine yüklenmez ama drain uzar (~%70 cap → ~1.4× süre, tahmin). §11.3 bilinçli kararının tersi; yeni kanıt: kullanıcının önceliği 'makine rahatlasın'. Alternatif: drain'de cap'i kaldırmak yerine Balanced tabanında tut (copy-floor mekanizması zaten var, RunCoordinator.cs:395-404).
(c) Tray → Exit ve × (Close to tray kapalı) yolunda 'drain yerine hard stop' (RunViewModel.Exit.cs:70 StopCommand yerine Hard gönder). Aynı bedel; sadece çıkışta.
Öneri: (a) — varsayılan graceful kalır, ikinci basış kullanıcının bilinçli 'şimdi' kararıdır; ARCHITECTURE §4.5'e 'second press' paragrafı ve EscStopTests.A_second_escape_while_stopping_sends_nothing testinin yeni kuralı pinleyecek şekilde yeniden yazılması gerekir.

**Çözüm (doğrulayıcı düzeltmesi):** Seçenek (a) (ikinci Stop = Hard) ve (b) (drain'de cap'i koru) ikisi de ARCHITECTURE §4.5'in açık gerekçeli kararını tersine çevirir → kullanıcıya 'doküman bilinçli olarak tersini söylüyor' diye sorulmadan yapılmaz. (b)'nin 'cap'i Balanced tabanında tut' varyantı §4.5 satır 246-247 ile doğrudan çelişir (torn-DLL garantisi kaynak ayarına bağlanmaz). En az çatışan yol (a): varsayılan değişmez, yalnız ikinci basış Hard; bedeli §4.5'te yazılı (≤parallelism proje failed('stopped') + ortak bin'de yarım kopya riski). EscStopTests.cs:70 testi yeni kuralı pinleyecek şekilde yeniden yazılmalı (silinmez).

**Değişmezler:** Değişmezlerle çatışmaz: Hard stop zaten sözleşmede (StopKind.Hard) ve motorda; inner job/shell-out korunur. (b) §11.3 bilinçli kararını tersine çevirir — 'şartlı'. Kopya yasağı: Hard gönderim mevcut SendStopAsync'ten geçer, ikinci kapı yazılmaz.

**Test fikri:** RunViewModelTests: Stopping fazında ikinci Stop/Esc → StopRunCommand(Kind=Hard) gider (bugün 'sends nothing' pinli; kural değişince yeniden yazılır). RunCoordinatorTests: Hard sonrası runStopped(WasHard=true) + in-flight projectFailed('stopped') (mevcut HardStop dalı).

**Doğrulayıcı notu:** Kod iddiayı doğruluyor: RunViewModel.cs:1560 yalnız StopKind.Graceful gönderir (src'de App tarafında StopKind.Hard gönderen hiçbir yer yok), CanStop (1581) Stopping fazında kapalı; RunCoordinator.cs:295 graceful'da DrainCapLocked → 328 TryWriteCapLocked(null) cap'i kaldırır; Hard yolu 294 ve SupervisorHost.cs:364'te hazır. ANCAK ikisi de ARCHITECTURE §4.5'te gerekçeli bilinçli karar (satır 219-220 'the App never sends it'; 246-247 'The "no torn DLL" guarantee is not negotiated against a resource setting'). Bu yüzden bulgu bir kusur değil, kullanıcı kararı gerektiren bir takas. '21-26 s' rakamı ajan tarafından decision.log'dan okunmuş; bu doğrulamada yeniden ölçülmedi (verilen ölçümle tutarlı: 17 üye tur başına ~79 s, 6 seviye). 'Drain'de makine stop öncesinden DAHA yüklü' ifadesi ölçülmedi: Balanced'da cap %70 → kalkınca tavan %100; fiilî artış en çok 4 in-flight MSBuild'in kullanabildiği kadar — tahmin.

#### D3-2 — Koşu bitiminde inner job'ın BOŞ olduğu doğrulanmıyor: post-build torun (Exec/cmd) ya da takılı csc kalırsa 'Completed' yazar ama process ve bloklu pump thread'leri Supervisor ölene kadar yaşar

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) MsBuildInvoker.cs:150-168 (DrainWait=5 s satır 50); RunCoordinator.cs:600-648 (ExecuteRunAsync finally), 1018-1058 (run sonu); NativeMethods.cs:148-154
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 150-168 — MSBuild.exe çıktı → WaitPumpsBoundedAsync(DrainWait=5s) → döner; torun pipe ucunu tutuyorsa pump TERK edilir (iptal edilmez)
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 157-166 — Kodun kendi yorumu: anonim pipe overlapped değil → ReadLineAsync thread-pool thread'inde BLOKLU ReadFile; 'yüzlerce bloklu thread-pool thread'i tutabilir', pool 1-2 thread/sn enjekte eder
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1017-1058 — Run sonu: yalnız scheduler sayaçları (unfinished/succeeded/failed) ve 'run finished' satırı; job'daki canlı process sayısı sorgulanmıyor
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 584-650 — ExecuteRunAsync finally: ReleasePerf + ledger Clear + ack; inner job'a bakılmıyor
- `src/BuildOrchestrator.Core/ProcessControl/NativeMethods.cs` satır 148-153 — QueryInformationJobObject var; JobObjectBasicAccountingInformation (ActiveProcesses) sınıfı tanımlı DEĞİL

**Kanıt:** MsBuildInvoker torunun pipe ucunu tutmasını bilinçli tolere ediyor (kontrat: InvokeAsync asılmaz) ama koşu sonunda kimse 'inner job'da kaç process kaldı' diye sormuyor. OSYS'te her projede post-build copy Exec'i var (ARCHITECTURE §20 'copy $(TargetName).*'); Exec → cmd.exe → xcopy/copy zinciri normalde saniyeler içinde biter, ama biten bir koşudan sonra job'da kalan HER process (takılı bir cmd, dosya kilidinde bekleyen copy, PerProjectTimeout'a hiç uğramayan bir torun) kullanıcıya görünmez; 'Completed' şeridi ve tray balloon'u gelir, CPU/RAM Supervisor çıkana kadar (uygulama tepside günlerce yaşar) kalır. Kullanıcının 'build bitti ama makine rahatlamadı' şikâyetinin en olası mekanik adayı budur — doğrulanmamış (bkz. open_questions).

**Etki:** Sessiz kalıntı: N torun × (RSS + bloklu thread-pool thread'i) koşu bitiminden Exit'e kadar. Tanı imkânı sıfır (decision.log'da iz yok). Tahmin: normal koşuda 0; patolojik koşuda 1-4 process, her biri cmd.exe/xcopy ölçeğinde (birkaç MB) ama takılı copy diskte kilit tutar.

**Etki (doğrulayıcı düzeltmesi):** Ölçülen koşuda kalıntı = 0 process, 0 fazladan thread. Bulgu yalnız bir gözlemlenebilirlik boşluğu: patolojik bir post-build adımı olursa iz kalmaz. Perf kazancı beklenmez.

**Çözüm:** 1) JobObject'e ActiveProcessCount ekle: NativeMethods'a JOBOBJECT_BASIC_ACCOUNTING_INFORMATION (info class 1) + tek QueryInformationJobObject çağrısı (mikrosaniye). 2) ExecuteRunAsync finally'de (ReleasePerf'ten sonra, RunCoordinator.cs:607 civarı) sayıyı oku; >0 ise decision.log'a 'run finished but N processes remain in the inner job' + Warn (konsol). 3) İkinci adım (ayrı karar): koşu bittiğinde inner job'da hiçbir şeyin yaşaması BEKLENMEDİĞİ için (worker'lar join oldu; Optimize/restore invoker aynı job'ı kullanır ama IsRunActive kapısıyla eşzamanlı olamaz) kalanları innerJob.Terminate() ile süpür — 'Completed' anında makine gerçekten rahatlar. Terminate edilmiş job yeni process kabul eder (mevcut Hard stop yolu bunu zaten kanıtlıyor). 4) Aynı sayı runCompleted'a taşınmak istenirse Contracts'a alan eklenir (opsiyonel; önce log yeter).

**Çözüm (doğrulayıcı düzeltmesi):** Yalnız adım 1-2 (ActiveProcesses sayısını run sonunda decision.log'a yaz) düşük riskli tanı olarak değerlendirilebilir. Adım 3 (run sonunda innerJob.Terminate()) önerilmez: ölçülmüş bir sorunu çözmüyor ve inner job'ı paylaşan Optimize/restore invoker'ı (Program.cs:61) ile sıra garantisi yalnız IsRunActive kapısına dayanıyor; ayrıca terminate, post-build'in bilerek arkada bıraktığı bir süreci sessizce öldürür (davranış değişikliği).

**Değişmezler:** Shell-out/nested job/OutDir'e dokunmama korunur; planlama Core'da kalır (sayaç Core/ProcessControl'da, karar Supervisor'da). Terminate adımı yalnız run sınırında ve yalnız inner job'da — §4.5'in 'App hard göndermez' kararına dokunmaz (bu Supervisor'ın kendi run-sonu temizliği).

**Test fikri:** RunCoordinatorTests: sahte invoker inner job'a (JobProcessLauncher ile) 300 s uyuyan bir cmd.exe doğurup 'başarı' dönsün → run sonunda decision.log'da 'N processes remain' satırı ve (adım 3 açıksa) ActiveProcessCount==0. JobObjectTests: ActiveProcessCount doğum/ölümde 0→1→0.

**Doğrulayıcı notu:** Mekanizma kodda doğru: run sonunda inner job'ın boşluğu sorgulanmıyor (RunCoordinator.cs:1018-1058 ve 600-648'de job sorgusu yok; NativeMethods'ta accounting sınıfı tanımlı değil), MsBuildInvoker.cs:167 pump'ları 5 s sonra terk ediyor. AMA 'kalıntı process' iddiası ölçümle desteklenmiyor, tersine çürütülüyor: measure-release-2026-10-01.log'da 152 projelik gerçek Rebuild'den sonra F0/F1/F2/G1/G2 örneklerinin HEPSİNDE 'supervisor-children: none'; Supervisor thread sayısı koşu sonu 25 → 20 s sonra 12 → tepside 9 (boşta başlangıç değeri 9-12) — yani bloklu pump thread'i de kalmamış. MSBuild Exec görevi cmd.exe'nin çıkışını bekler; torunun yaşaması yalnız 'start'/detached süreç başlatan bir post-build adımıyla mümkün ve OSYS'te böyle bir adım olduğu gösterilmedi. 'Build bitti ama makine rahatlamadı' şikâyetinin 'en olası adayı' nitelemesi kanıtsız; ölçüm bunun yerine App'in koşu sonrası 897 Mcycles/s + büyüyen RAM'ini gösteriyor.

#### D3-3 — Kapanış senaryolarının yarısı testle pinli değil: Supervisor tek başına çökerse MSBuild çocuklarının öldüğü, Windows oturum kapanışı (SessionEnding) ve OnExit'teki 'motor öldü → Update.exe' sırası sınanmıyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) EngineHostTests.cs:33-51; StartupPathTests.cs:128-137 (OnExit sırası ZATEN pinli); WindowCloseRuleTests.cs:22; MainWindow.xaml.cs:401, 1392; App.xaml.cs:182-195
- `tests/BuildOrchestrator.Tests/App/EngineHostTests.cs` satır 33-50 — Supervisor_kill_raises_EngineExited_and_restart_recovers: Supervisor'ı Kill() eder ama altında HİÇ child yok — 'inner job kapanışı MSBuild'i öldürür' iddiası (Program.cs:43 using var innerJob) bu testte doğrulanmıyor
- `tests/BuildOrchestrator.Tests/Supervisor/CascadeKillTests.cs` satır 11-55 — Yalnız OUTER job kapanışı (App ölümü) sınanır; inner-alone yolu yok
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 401, 1392 — SessionEnding → _exiting=true; testlerde 'SessionEnding' geçmiyor (grep: yalnız bin/ DLL'lerde)
- `src/BuildOrchestrator.App/App.xaml.cs` satır 182-194 — OnExit sırası: EngineHost dispose (≤2 s) → tray → single-instance → UpdateService.ApplyOnExit; hiçbir test App.OnExit'i koşturmuyor
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 1017-1058 — Run sonu inner job boşluğu (D3-2) — test yok

**Kanıt:** Test envanteri: JobObjectTests (dispose→kill ≤2s, Terminate), JobCpuRateTests (priority yazımı KILL bayrağını korur; cap sonrası kaskat ≤2s), CascadeKillTests (outer dispose, 0 orphan), KillMidBuildTests (gerçek MSBuild ortasında outer dispose, yarım DLL yok), SafeExitProcessTests (build sürerken güvenli çıkış, 0 orphan), AppShutdownTests (2 s bütçe, dispatcher-bağımsız), RunCoordinatorTests 2067/2254/2281 (cap run sonunda geri alınır, sızmaz), 443/535 (graceful drain). Eksik: (e) Supervisor-alone crash + canlı child, (f) SessionEnding kuralı, (g) OnExit sırası, run-sonu job boşluğu.

**Etki:** Regresyon görünmez: Program.cs'te innerJob'ın 'using'ten çıkması ya da ikinci bir job handle'ının sızması (ör. ileride eklenecek bir izleyici) MSBuild yetimlerini geri getirir ve hiçbir test kırmaz. SessionEnding'de tray'e sapma/K5 balloon regresyonu da görünmez.

**Etki (doğrulayıcı düzeltmesi):** Performansa etkisi yok. Gerçek boşluk iki test: (1) Supervisor-alone kill + canlı inner-job child → ≤2 s'de ölür, (2) SessionEnding → _exiting bağlantısı.

**Çözüm:** (1) CascadeKillTests kalıbıyla yeni test: sandbox Supervisor + debugSpawnChildren(2) → Process.GetProcessById(supervisorPid).Kill() (AĞAÇ DEĞİL) → outer job IOCP'sinden 2×cmd+2×powershell EXIT bildirimleri ≤ OrphanBudget (2 s); mevcut ProcessTree.AssertNoOrphansAsync yeniden kullanılır. (2) MainWindow realize testi: OnSessionEnding internal seam ile çağrılır → sonraki OnClosing'de WindowCloseRule.Decide(_exiting=true) Close döner, tray'e sapmaz (CloseToTrayTests kalıbı, sayaçlı ShutdownApplication). (3) OnExit sırası için AppShutdown gibi saf bir 'ExitSequence' çıkarımı: EngineHost dispose → tray → mutex → ApplyOnExit sırasını sahte IAppUpdater ile pinle (ApplyOnExit'in dispose'dan SONRA çağrıldığı). (4) D3-2'nin testi.

**Çözüm (doğrulayıcı düzeltmesi):** Öneri (3) (ExitSequence çıkarımı) düşürülür — sıra zaten kaynak guard'ıyla pinli. (1) ve (2) kalır; (4) D3-2'ye bağlı, D3-2 düşürüldüğü için opsiyonel.

**Değişmezler:** Yalnız test; üretim yüzeyine dokunmaz. Ölçüm testleri kuralı: (1) gerçek process doğurur → mevcut ProcessControl kategorisinde, ortam kapısı gerekmez (CascadeKillTests zaten normal süitte).

**Test fikri:** Yukarıdaki (1)-(4).

**Doğrulayıcı notu:** Üç iddiadan biri yanlış, biri kısmen yanlış. (g) 'hiçbir test OnExit sırasını pinlemiyor' YANLIŞ: tests/.../App/StartupPathTests.cs:128-137 kaynak guard'ı ApplyOnExit'in EngineHost dispose'undan sonra ve base.OnExit'ten önce çağrıldığını pinliyor. (f) SessionEnding: karar kuralı WindowCloseRuleTests.cs:22'de pinli (exiting:true → Close); eksik olan yalnız 'SessionEnding → _exiting=true' tek satırlık bağlantısı (MainWindow.xaml.cs:401, 1392) — testlerde 'SessionEnding' geçmediği doğrulandı. (e) Supervisor tek başına öldürülünce inner job child'larının öldüğü gerçekten sınanmıyor: EngineHostTests.cs:33-40 child'sız Kill ediyor, CascadeKillTests yalnız outer; SupervisorIpcTests.cs:126-150 Hard stop'u (Terminate) sınıyor, handle kapanışını değil. Bu bir test boşluğu, perf bulgusu değil.

#### D3-4 — Planlama penceresinde Stop işlemez: planner senkron ve iptal token'sız — Stop ancak tarama/değerlendirme/harici fetch/incremental hash bitince uygulanır

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) RunCoordinator.cs:698 (planner çağrısı), 296, 862; Program.cs:72-134 (88-90 harici güncelleme, 128-129 revizyon okuma); Core/Externals/ExternalUpdater.cs:83-85
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 698 — runPlan = planner(cmd, line => …) — CancellationToken yok, senkron
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 296, 862 — _scheduler null iken stop yalnız _stopKind'a yazılır; plan kurulunca scheduler.RequestStop() — planlama süresi boyunca bekler
- `src/BuildOrchestrator.Supervisor/Program.cs` satır 72-134 — BuildRunPlan(cmd, progress): harici ff güncelleme (git fetch ×2 kök, 88-90) → scan → evaluate → graph → incremental (git rev-parse + hash prefill); hepsi GetAwaiter().GetResult() ile senkron

**Kanıt:** Ölçülen koşuda planlama kısaydı ('preparing dependencies ~0', upstream up-to-date) ama UpdateExternals=true ile her Build 2 harici kökte git fetch koşar; ağ yavaşsa Stop tıkı fetch bitene kadar hiçbir şey durdurmaz. Faz App'te Stopping'e geçer (§4.5 'moves the phase before the command is sent'), motor cevabı planlama sonuna kalır.

**Etki:** Kullanıcı Starting fazında Stop'a basınca gecikme = kalan planlama süresi (tahmin: OSYS'te sıcak cache ile 1-3 s; soğuk cache/ağ ile 10+ s). CPU: hash prefill o sürede devam eder.

**Etki (doğrulayıcı düzeltmesi):** Starting fazında Stop gecikmesi = kalan planlama süresi; bu süre ÖLÇÜLMEDİ (bulgudaki 1-3 s / 10+ s tahmin). Zayıf makinede soğuk cache ile planlama daha uzun olacağı için gecikme büyür — büyüklüğü bilinmiyor.

**Çözüm:** planner imzasına CancellationToken ekle (Func<StartRunCommand, Action<string>, CancellationToken, RunPlan>); RunCoordinator TryRequestStop'ta run'a özel bir CTS iptal etsin; Program.BuildRunPlan içinde pahalı adımların arasına (harici fetch sonrası, scan sonrası, incremental öncesi) ct.ThrowIfCancellationRequested(); OperationCanceledException → mevcut planFailed yolu yerine runStopped ack (ExecuteRunAsync finally'deki 'ACK BORCU' zaten bunu kapatıyor, 611-623). ExternalUpdater/ProcessRunner zaten ct alır.

**Çözüm (doğrulayıcı düzeltmesi):** Bulgunun çözümünde bir hata var: OperationCanceledException RunCoordinator.cs:699-700'deki catch filtresine (IOException/UnauthorizedAccessException/ArgumentException/ExternalPreparationException) GİRMEZ; ExecuteRunAsync'in genel catch'ine (596-600) düşer ve kullanıcıya ErrorEvent('runFailed') yazılır, ardından ack borcu runStopped'ı ekler. Yani iptal 'hata' olarak görünür. Düzeltme: planner çağrısının etrafında OperationCanceledException ayrı yakalanıp sessizce return edilmeli (runStopped'ı 611-622'deki ack borcu zaten yazar). ct geçirilecek ilk nokta ExternalUpdater.UpdateAsync (imza hazır).

**Değişmezler:** Planlama Core'da kalır (ct yalnız geçirilir); git'e yazma yok (fetch/ff-only zaten kullanıcı kararı, iptali yazmayı yarıda kesmez: ProcessRunner timeout kill yolu mevcut).

**Test fikri:** RunCoordinatorTests: planner sahte bir gate'te beklerken TryRequestStop(Graceful) → planner ct iptal → runStopped gelir, runStarted hiç gelmez.

**Doğrulayıcı notu:** RunCoordinator.cs:698 planner(cmd, line => ...) senkron ve CancellationToken almıyor; 296'da _scheduler null iken stop yalnız _stopKind'a yazılıyor, 862'de plan kurulduktan sonra uygulanıyor. Program.cs:72-134 BuildRunPlan harici güncellemeyi (88-90) ve revizyon okumayı (128-129) GetAwaiter().GetResult() ile koşturuyor; ExternalUpdater.UpdateAsync (Externals/ExternalUpdater.cs:83-85) ct parametresi alıyor ama çağrı ct geçirmiyor. Süre rakamları (1-3 s / 10+ s) ölçülmedi; verilen ölçümde tek veri: planlamada Supervisor 1,07 Gcycles. Kozmetik sınıflaması doğru.

#### D3-5 — Her MSBuild.exe invoke'u CREATE_NO_WINDOW ile gizli bir konsol açar → invoke başına bir conhost.exe doğar/ölür (187+ kez/koşu); DETACHED_PROCESS ile bu ek yük kalkabilir

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Core/ProcessControl/JobProcessLauncher.cs:40; MsBuildInvoker.cs:127-128
- `src/BuildOrchestrator.Core/ProcessControl/JobProcessLauncher.cs` satır 220 — flags = CREATE_SUSPENDED | CREATE_NO_WINDOW | CREATE_UNICODE_ENVIRONMENT — stdio zaten pipe'a yönlendirilmiş (RedirectStdio=true)
- `src/BuildOrchestrator.Core/MsBuild/MsBuildInvoker.cs` satır 127-128 — Launch(innerJob, commandLine, RedirectStdio:true) — MSBuild'in konsola ihtiyacı yok

**Kanıt:** CREATE_NO_WINDOW 'penceresiz konsol' demektir, 'konsolsuz' değil: Windows her böyle process için bir conhost.exe başlatır (bilinen davranış). Ölçülen koşuda invoke'lar arası ~0.5 s ek yük görülmüştü (parent bağlamı); conhost doğum+ölümü bunun küçük bir parçası (tahmin 10-30 ms + geçici ~5 MB). csc.exe'nin kendi conhost'u MSBuild ToolTask'ının kararıdır, bizim elimizde değil.

**Etki:** Tahmin: 187 invoke × 10-30 ms = 2-6 s/koşu; cycle turlarında üye×tur kadar. Kesin değil — spike ile ölçülmeli.

**Etki (doğrulayıcı düzeltmesi):** Ölçülen: koşu sırasında 5-9 conhost'tan en çok 4'ü MSBuild.exe'ye ait + boştayken Supervisor'a ait 1 kalıcı conhost. Süre kazancı ÖLÇÜLMEDİ; RAM kazancı conhost başına birkaç MB mertebesinde (ölçülmedi).

**Çözüm:** Spike: JobProcessLauncher'da RedirectStdio yolunda CREATE_NO_WINDOW yerine DETACHED_PROCESS dene; 3 kanıt topla: (1) MSBuild.exe konsolsuz sorunsuz koşuyor mu (Console.BufferWidth sorgusu IOException'ı MSBuild içinde yakalanıyor — doğrulanmalı), (2) Process Explorer/IOCP ile conhost doğumu gerçekten kalkıyor mu, (3) invoke süresi farkı. Sonuç olumluysa flag'i LaunchOptions'a (NoConsole) taşı; Supervisor'ı başlatan EngineHost yolu (App) da aynı bayrağı kullanır (Supervisor da konsol istemez). Olumsuzsa ARCHITECTURE §4.3'e 'ölçüldü, kazanç yok' notu.

**Çözüm (doğrulayıcı düzeltmesi):** Yalnız spike olarak kalmalı; üretim değişikliği ölçüm olmadan önerilmez. Spike'ta ayrıca Supervisor'ın stdout=NDJSON değişmezinin konsolsuz süreçte bozulmadığı doğrulanmalı.

**Değişmezler:** Shell-out ve job atama sırası (§4.3) aynen kalır; yalnız CreateProcess bayrağı değişir.

**Test fikri:** HandleInheritanceTests kalıbında: launch sonrası GetConsoleWindow/AttachConsole ile child'ın konsolsuz olduğu; SafeExitProcessTests/JobMembers IOCP'sinde conhost.exe adlı doğum görülmediği.

**Doğrulayıcı notu:** Konum yanlış: JobProcessLauncher.cs 105 satır, bayrak satır 40'ta (220 değil). Mekanizma ölçümle uyumlu: koşu örneklerinde conhost sayısı ≈ MSBuild + csc + cmd (ör. D1: conhost×7 = MSBuild×4 + csc×2 + 1; E1: conhost×7 = MSBuild×4 + cmd×3) ve kapanışta 'supervisor children before kill: conhost.exe' — Supervisor'ın kendisi de ömür boyu bir conhost taşıyor. Ama DETACHED_PROCESS yalnız MSBuild.exe'nin (ve Supervisor'ın) conhost'unu kaldırır; csc/cmd conhost'ları MSBuild görevlerinin kendi CreateNoWindow kararı, kalır. '10-30 ms/invoke, 2-6 s/koşu' tamamen tahmin ve seri toplam: 4 paralel slotta duvar saati etkisi en çok ~1/4'ü. MSBuild'in konsolsuz koşup koşmadığı doğrulanmadı (risk).

#### D3-6 — EngineHost kapanışı ShutdownCommand'ı yazar yazmaz ağacı öldürür: Supervisor'ın kendi düzenli çıkışı hiç koşmaz, 'graceful' yazım fiilen süs; Process.Kill(entireProcessTree) tüm sistem process'lerini tarar

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) EngineHost.cs:203-213 (ShutdownGracefullyAsync), 234 (KillTree), 246-250 (KillAndAwaitExit), 252-266 (KillCurrent), 268-272 (DisposeAsync); SupervisorHost.cs:101, 113-114
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 203-213 — ShutdownGracefullyAsync: WriteAsync(ShutdownCommand).WaitAsync(500ms) → hemen KillCurrent() — çıkış beklenmez
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 235, 248-252 — KillTree = Kill(entireProcessTree:true) → .NET tüm process listesini snapshot'lar; sonra WaitForExit(1 s)
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 101, 113-114 — stdin EOF ya da ShutdownCommand → RunAsync 0 döner → Program.Main'in using'leri (coordinator, innerJob) çalışır — ama App bunu beklemediği için pratikte hiç görülmez

**Kanıt:** Yazım mikrosaniye sürer, ardından koşulsuz Kill. Supervisor'ın ShutdownCommand'ı okuyup RunAsync'ten dönmesi birkaç ms; yarışı kill kazanır. Zararı yok (bekleyen lazy yazım yok: BuildStateStore/ledger/cache'ler koşu içinde yazılıyor; KILL_ON_JOB_CLOSE süpürür) ama (a) her çıkış 'çöktü' gibi sert biter, (b) Kill(entireProcessTree) 300+ process'lik makinede onlarca ms tarama yapar (tahmin), (c) sessiz-motor çıkışında drain'deki MSBuild'ler yarım kesilir (§20 kabul).

**Etki:** Perf etkisi ihmal edilebilir (çıkış yolunda onlarca ms). Asıl değer: sıra netliği — motorun kendi çıkışına ≤300 ms tanınırsa Supervisor stdout'unu kapatır, innerJob'ı kendisi Dispose eder ve App tarafı 'kill' yerine 'exit' görür; Update.exe için dosya kilitleri daha erken bırakılır.

**Etki (doğrulayıcı düzeltmesi):** Çıkış yolunda ölçülebilir bir perf kaybı gösterilmedi. Değeri yalnız sıra temizliği.

**Çözüm:** ShutdownGracefullyAsync: yazımdan sonra child.WaitForExitAsync'i kısa bir bütçeyle (ör. 300 ms; toplam AppShutdown.DisposalTimeout 2 s içinde: 500+300+1000 < 2000) bekle; çıkmadıysa mevcut KillCurrent. KillCurrent'ta Kill(entireProcessTree) yerine _outerJob.Terminate() da düşünülebilir (O(1), tarama yok) — ama outer job Terminate'i App'in outer job'ında yaşayan git.exe/vswhere.exe'yi de anında keser (zaten kaskatta ölürler) ve test dikişi killStrategy'nin sözleşmesi değişir; bu yüzden yalnız bekleme eklemek yeterli.

**Çözüm (doğrulayıcı düzeltmesi):** Bekleme eklenirse KillExitWait=1 s sabiti EngineHostTests.Kill_exit_wait_stays_one_second ile literal pinli ve §4.4 matrisi 'Kill tree + ≤1 s' diyor — ikisi de aynı işte güncellenmeli; toplam 500+300+1000 ms AppShutdown.DisposalTimeout (2 s) içinde kalıyor ama pay 200 ms'ye iner. Öncelik düşük; yapılmasa da olur.

**Değişmezler:** Nested job/KILL_ON_JOB_CLOSE değişmez; §4.4 matrisi ('App → Supervisor only: Kill tree + ≤1 s') 'önce kendi çıkışı, sonra kill' diye güncellenir.

**Test fikri:** EngineHostTests: sağlıklı bir Supervisor'a DisposeAsync → process ExitCode==0 (kill'de -1/terminated olurdu) ve KillStrategy sayacı 0.

**Doğrulayıcı notu:** EngineHost.cs:203-213: ShutdownCommand yazımı (≤500 ms) ardından koşulsuz KillCurrent(); 234 KillTree = Kill(entireProcessTree:true); 246-250 KillAndAwaitExit ≤1 s bekler. Supervisor tarafı (SupervisorHost.cs:113-114) ShutdownCommand'da _running=false ile düzenli çıkıyor ama App beklemiyor. Ölçüm zararsızlığı destekliyor: App öldürülünce Supervisor 81 ms'de gitti, 3 s sonra kalıntı yok. Perf etkisi ihmal edilebilir; bulgu bunu kendisi de söylüyor. 'Kill ağaç taraması onlarca ms' ölçülmedi.

#### D3-7 — Sessiz motor çıkışı drain'i sert keser: Exit beklerken motor 90 s hiç olay göndermezse App kapanır ve outer job kaskadı uçuştaki MSBuild.exe'leri yazım ortasında öldürür — drain'de heartbeat yok

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) RunViewModel.Exit.cs:74-78, 88; RunViewModel.cs:753, EvaluateEngineSilence/WaitingOnEngine (~1883-1900); OptimizeWorkspaceService.cs:60-65, 228-235; ARCHITECTURE.md:5527-5528
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.Exit.cs` satır 75-78, 88 — EvaluateExit: ExitPending && (WorkspaceIdle || EngineOverdueMessage != null) → ExitReady
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 753, 1899-1902 — EngineSilenceThresholdMs = 90_000; bekçi son motor sinyalinden ölçer
- `src/BuildOrchestrator.Core/Workspace/OptimizeWorkspaceService.cs` satır 60-65, 228-235 — Restore için 30 s heartbeat deseni ZATEN var — drain için yok
- `ARCHITECTURE.md` satır §20 (5520-5524) — Bilinen sınır: 'a build step that prints nothing for that long while a stop drains, is then cut off … An engine heartbeat … would close it'

**Kanıt:** Drain'de MSBuild satırları ProjectLogEvent olarak akar ve saati sıfırlar; uzun bir csc/link adımı ya da post-build copy 90 s sessiz kalırsa (OSYS UI üyeleri 21-47 s — eşik altında ama büyük bir projede ya da disk kilidi bekleyen copy'de mümkün) Exit → OnExit → Kill tree → torn DLL. Ledger bir sonraki açılışta projeyi geçersizler ama ortak bin'deki yarım kopya o ana kadar VS'e görünür.

**Etki:** Düşük olasılık, yüksek maliyet (yarım DLL). §20 zaten kabul ediyor; kapatması ucuz.

**Etki (doğrulayıcı düzeltmesi):** Zayıf makinede tek bir derleme adımının 90 s hiç satır yazmaması daha olası (yavaş disk/CPU), ama sıklığı ölçülmedi. Sonuç: Exit beklerken uçuştaki MSBuild'ler yazım ortasında kesilir.

**Çözüm:** RunCoordinator: _stopKind Graceful olduğunda drain boyunca 30 s'de bir (OptimizeWorkspaceService.HeartbeatInterval ile AYNI sabit — kopya yasağı, sabiti Core'da paylaş) PlanProgressEvent/RunProgress tipi bir 'still finishing N projects' olayı yaz; App'te her olay saati sıfırlar (mevcut kural), ek kod yok. Bilinçli olarak yalnız drain penceresinde (normal koşuda projectLog akışı zaten var).

**Çözüm (doğrulayıcı düzeltmesi):** Heartbeat yalnız drain'de değil, proje derlenirken de sessizlik olabileceği için 'en az bir proje uçuştayken 30 s'de bir' olarak kurulmalı (watchdog Stopping'de devrede; Starting'de de devrede). HeartbeatInterval sabiti private (OptimizeWorkspaceService.cs:65) — kopya yasağı gereği Core'da ortak yere taşınmalı.

**Değişmezler:** stdout NDJSON; mevcut olay tipi yeniden kullanılır; §4.6 'Any event resets the clock' korunur.

**Test fikri:** RunCoordinatorTests: sahte invoker drain'de 'sessiz' beklerken sahte saatle 30 s ilerlet → heartbeat olayı gelir; SafeExitTests: heartbeat gelirken EngineOverdueMessage null kalır ve exit drain'i bekler.

**Doğrulayıcı notu:** RunViewModel.Exit.cs:74-78 EvaluateExit: ExitPending && (WorkspaceIdle || EngineOverdueMessage != null) → ExitReady; RunViewModel.cs:753 eşik 90 000 ms; WaitingOnEngine Stopping fazını kapsıyor ve her event saati sıfırlıyor (OnEvent başı). OptimizeWorkspaceService.cs:65 HeartbeatInterval=30 s deseni mevcut, drain'de yok. ARCHITECTURE §20 (satır 5527-5528) bunu bilinen sınır olarak yazıyor ve çözümün heartbeat olduğunu söylüyor — bulgu dokümanla uyumlu. Olasılık ölçülmedi: verilen koşuda ~160 log satırı/s akıyor, 90 s sessizlik gözlenmedi. Perf değil doğruluk (yarım DLL) bulgusu.

**Doğrulayıcının eklediği noktalar:**
- ÖLÇÜM bulgu ajanının 'en olası aday' dediği kalıntı-process hipotezini çürütüyor ama bulgu bunu kullanmamış: measure-release-2026-10-01.log F0-G2 örneklerinde 'supervisor-children: none' ve Supervisor thread 25→12→9. Koşu sonrası 'makine rahatlamadı' etkisinin ölçülen kaynağı App (ilk 10 s 897 Mcycles/s, WS 283→482 MB geri verilmiyor) ve Supervisor'ın koşu sonu Sync'i (456 Mcycles/s, Private 151→210 MB) — kapanış boyutunda bu hiç bulgu olarak yazılmamış.
- Supervisor'ın kendisi ömür boyu bir conhost.exe taşıyor (ölçüm logu: 'supervisor children before kill: conhost.exe'; kaynak JobProcessLauncher.cs:40 CREATE_NO_WINDOW, EngineHost aynı launcher'ı kullanıyor) — tepside boştayken de yaşayan ek bir process; D3-5 bunu yalnız yan cümlede geçiyor, ölçülmüş kanıtı kullanmıyor.
- MsBuildInvoker.cs:50, 167: başarı yolunda DrainWait=5 s — bir torun pipe ucunu tutarsa o projenin worker slotu MSBuild çıktıktan sonra 5 s daha dolu kalır (4 slotluk Balanced'da doğrudan duvar saati kaybı). Bulgu bunu yalnız thread sızıntısı açısından ele almış; slot bekleme maliyeti ve bunun koşuda kaç kez tetiklendiği (decision.log'da iz yok) incelenmemiş. Ölçülen koşuda sıklığı bilinmiyor.
- RunViewModel.Exit.cs:70: tam çıkış isteği yalnız koşu için Stop gönderir; Optimize/Clean/Sync uçuştaysa iptal komutu yok (ARCHITECTURE.md:275 bunu bilinen sınır olarak yazıyor) ve Optimize'ın 30 s heartbeat'i sessizlik bekçisini susturduğu için Exit tüm restore dizisi bitene kadar bekler — kapanış boyutunda bulgu olarak geçmiyor.
- RunCoordinator.cs:699-701: planner catch filtresi dar; Win32Exception/InvalidOperationException gibi (ör. git.exe bulunamadı) hatalar 'planFailed' yerine genel catch'ten 'runFailed' olarak çıkar — D3-4'ün iptal önerisi de aynı deliğe düşer.

**Temiz bulunan alanlar:**
- Kaskat kill omurgası sağlam: outer job App'te (EngineHost.cs:59), inner job Supervisor'da (Program.cs:43), her child CREATE_SUSPENDED → job.Assign → Resume (JobProcessLauncher.cs:244-264, kaçış penceresi yok); JobObject.Dispose son handle'ı kapatır (JobObject.cs:173-179); priority yazımı KILL_ON_JOB_CLOSE bayrağını Query→OR→Set ile korur (JobObject.cs:82-103, JobCpuRateTests.Priority_write_…). Breakaway job içinden reddediliyor (CascadeKillTests.Breakaway_from_inside_job_is_denied_err5).
- App çökmesi / Task Manager kill (senaryo d): OS outer handle'ı kapatır → Supervisor + tüm MSBuild ağacı ölür; ölçülü ≤2 s, spike'ta 18-34 ms (CascadeKillTests, KillMidBuildTests yarım DLL yok). Mutex/pipe/hotkey/tray OS tarafından bırakılır (tray ghost ikonu hover'a kadar kalabilir — Windows davranışı).
- Koşu bitince inner job'daki CPU cap KALDIRILIYOR ve priority Normal'e pinleniyor: ReleasePerf (RunCoordinator.cs:559-580) ExecuteRunAsync'in tek huni finally'sinden (607) her yolda (normal, graceful, hard, planFailed, exception) koşar; _pendingPerf/_capDrained/_copyFloorDepth aynı kilitte sıfırlanır (640-646). Testler: RunCoordinatorTests 2067 (Light cap run sonunda kalkar), 2254, 2281 (sızma yok). Bir sonraki koşu temiz job'da başlar.
- MSBuild kalıntısı yok: -nodeReuse:false + UseSharedCompilation=false + -m YOK (MsBuildArguments.cs:26) → tek node, VBCSCompiler yok ('using command line tool by design' proje loglarında). csc.exe/_wpftmp csc'si MSBuild.exe'nin child'ı olarak inner job'da; MSBuild çıkınca çıkıyor. Timeout yolu: PerProjectTimeout 10 dk → Kill(entireProcessTree) + 5 s bounded bekleme (MsBuildInvoker.cs:170-183) — asılma yok.
- Supervisor tek başına çökerse (senaryo e): innerJob handle'ı process ile kapanır → MSBuild çocukları ölür (mekanizma doğru; yalnız testi eksik — D3-3). App EngineExited'i tek atım alır (EngineHost.cs:136-145, TryClaimExit) ve şerit 'Restart engine' verir.
- Supervisor stdin EOF'ta düzenli çıkar (SupervisorHost.cs:101 → return 0 → Program.Main using'leri). git.exe/vswhere.exe Supervisor'dan ProcessRunner ile (CreateNoWindow, ProcessRunner.cs:26-42) doğar, outer job üyeliğini miras alır, kaskatta ölür; timeout'ta Kill tree (ProcessRunner.cs:55).
- Güvenli tam çıkış (senaryo c) uçtan uca pinli: RequestExit → WorkspaceIdle/ExitReady tek atım (RunViewModel.Exit.cs) → ExitNow → Shutdown kuyrukta (MainWindow.xaml.cs:410, 1368) → OnClosed kabuk kaynaklarını bırakır: hotkey UnregisterHotKey (1425 → Hotkey.cs:179-184), tray NIM_DELETE + büyük ikon GDI (1426 → AppTrayIcon.cs:169-173), HEAD FileSystemWatcher (1427 → HeadWatcher.cs:174-180), git poll DispatcherTimer (1428), tray overlay penceresi Close (1431; storyboard Remove ile sökülür TrayBuildIndicator.xaml.cs:117), _elapsedTimer Stop + console CTS Cancel (395). SafeExitProcessTests gerçek MSBuild uçuştayken 0 orphan ve disposal ≤2 s'yi pinler.
- App.OnExit sırası doğru ve dispatcher'dan bağımsız: EngineHost.DisposeAsync Task.Run'da ≤2 s (AppShutdown.cs:35; AppShutdownTests deadlock/timeout pinli) → ikinci-instance tray → SingleInstance mutex ReleaseMutex + pipe CTS (SingleInstance.cs:166-177) → UpdateService.ApplyOnExit en son (App.xaml.cs:192): Update.exe App'in çıkışını bekler, Supervisor kill+1 s wait (KillExitWait) ile supervisor\*.dll kilitleri bırakılmış olur; App bir job üyesi olmadığı için Update.exe kaskattan etkilenmez (senaryo g).
- Windows oturum kapanışı (senaryo f): SessionEnding → _exiting=true (MainWindow.xaml.cs:1392) → tray'e sapma/balloon yok, drain beklenmez; kapanış OnExit'ten geçer (≤2 s < Windows'un 5 s'si). Uçuştaki projeler run-inflight.json defterinde (InFlightLedger: dispatch'te Add, sonuçta Remove, koşu sonunda Clear — RunCoordinator.cs:1154/1353/610) → bir sonraki açılışta RecoverInterruptedRun geçersizler (Program.cs:34-35). Doğrulandı: temiz kapanış sonrası run-inflight.json diskte YOK. Defter maliyeti ~2 küçük atomik yazım/proje — kabul edilebilir.
- Tray göstergesi ve balloon: gösterge yalnız pencere gizli + faz aktifken (TrayBuildIndicatorController.cs:124-145), koşu bitince çıkış evresi → HideNow → nefes → balloon; overlay penceresi gizlenince storyboard tamamen sökülür (IsVisibleChanged → StopNow, TrayBuildIndicator.xaml.cs:54, 99-117) — tepside boşta çizim yok.
- Graceful drain'in kendisi doğru: yeni dispatch yok, SCC turunun kalan üyeleri ve yeni tur başlamaz (RunCoordinator.cs:1598-1603, 1640-1643, 1745), runStopped tam bir kez ve ancak tüm in-flight sonuçlar raporlandıktan sonra (1024-1043); Stop'a rağmen biten proje persist edilir (RunCoordinatorTests 535).

**Açık sorular:**
- 'Build bitti ama makine rahatlamadı' gözlemi TAM olarak neyle yapıldı: Task Manager'da hangi process CPU/RAM tutuyordu (MSBuild.exe? conhost.exe? cmd.exe/xcopy? BuildOrchestrator.Supervisor? App'in kendisi?) ve pencere açık mıydı, tepside miydi? Cevap D3-2 (kalıntı torun) ile D1/D4 (App tarafı) arasında karar verdirir.
- OSYS projelerinin post-build Exec adımlarında uzun yaşayan / kilit bekleyen bir process (ör. bir servis, robocopy /MON, bir watcher) başlatan proje var mı? Varsa koşu sonrası inner job'da kalır (D3-2).
- Stop'a İKİNCİ basış = hard stop (TerminateJobObject) kabul edilir mi? Bedel: en çok 4 proje bir sonraki Build'de yeniden derlenir + ortak bin'de yarım kopya riski (§4.5). Yoksa Stop'un varsayılanı graceful kalsın, ayrı bir 'Stop now' maddesi mi istenir (tray menüsü/Esc Esc)?
- Drain sırasında CPU cap'in KALKMASI (kısa ama tam yük) mi, cap'li ama daha uzun drain mi tercih edilir? (§11.3 bilinçli kararının tersine çevrilmesi — D3-1 seçenek b)
- Tray → Exit / × ile tam çıkışta da drain beklensin mi, yoksa 'Exit = hemen öldür' mü? (D3-1 seçenek c; SafeExitTests bunun tersini pinliyor, kural değişirse yeniden yazılır)
- D3-5 conhost spike'ı (DETACHED_PROCESS) denenmesini ister misiniz? Kazanç tahmini küçük (2-6 s/koşu), ölçmeden söylenemez.


### D11-git-autosync-update

#### D11-1 — Pencere aktivasyonu her alt-tab dönüşünde TAM Sync koşturuyor (5 s sessizlik eşiği), ön-kontrol yok

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs` satır 72, 180-184, 267, 289-292 — ActivationQuietMs=5000; WindowActivatedAsync → EvaluateAsync; satır 267 yalnız 5 s eşiği; satır 289'daki 'HEAD sha aynıysa hiçbir şey yapma' kapısı `!trigger.IsActivation` ile aktivasyonu BİLEREK dışarıda bırakır → satır 291-292 SyncSilentlyAsync(Refresh)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 350, 1344-1348 — Activated += _vm.OnWindowActivated(); ShowFromTray → Activate() (tepsiden, overlay'den, balloon'dan dönüş de aynı yol)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1359, 1402-1404, 1417 — SyncCoreAsync: Silent kipte Fetch=false ama komut motora TAM SyncWorkspaceCommand olarak gider; ardından koşulsuz ListBranchesCommand
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` satır 101, 111, 113, 132-143, 158, 162, 257, 262, 289-324 — Silent Sync'in tam pipeline'ı: rev-parse HEAD, symbolic-ref, rev-parse refs/remotes/origin/<b>, rev-list --count, scan(187 csproj + 2 harici kök), BuildPlanBuilder (evaluate+graf+topo), build-state Load, status --porcelain -z, will-build pass (Safe+Fast, 24k dosya stat), hashes.Flush()

**Kanıt:** Kod: aktivasyon tetiği HEAD kıyasından muaf (satır 289 `!trigger.IsActivation`), gerekçe §12.3/§10.3 'files may have been edited elsewhere'. Ölçüm (OSYS, Git Bash, sıcak): status --porcelain -z 126-135 ms (27.144 izlenen dosya, ağaç temiz), symbolic-ref 68 ms, rev-parse 74 ms, rev-list --count 128 ms, for-each-ref 76 ms → silent Sync başına 5 git process (~470 ms seri) + listBranches 2 process (~145 ms). Ortak bağlamdaki ölçüm: scan+graph+eval 764 ms, içerik kararı 241-303 ms sıcak. Üstüne D11-3'teki 12 MB JSON okuma + 12 MB yazma. App tarafı: topoloji uzlaştırma + 187 satır önizleme yeniden yazımı + DecisionKeys() iki kez (RunViewModel.Workspace.cs 777-800).

**Etki:** Her ≥5 s sonraki pencereye dönüşte (VS'den alt-tab, tepsiden geri gelme, balloon tıklaması) Supervisor ~1,5-2,5 s (tahmin, ölçümlerin toplamı) CPU+IO yakar, ~24 MB disk IO yapar, 7 git process açar; hiçbir şey değişmemişse sonuç bir öncekiyle aynıdır. Ayrıca Sync komut döngüsünü BLOKLADIĞI için (SupervisorHost.cs 147) dönüşün hemen ardından basılan Build, `_queuedRun` (RunViewModel.cs 1124: `WorkspaceBusy` iken bekler) ile o Sync bitene dek gecikir — 'UI akışı seri mi' sorusunun en somut cevabı bu: dönüş+Build tıklaması 1,5-2,5 s gecikmeli başlar.

**Etki (doğrulayıcı düzeltmesi):** Olcum (measure-release-2026-10-01.log satir 35-36, aktivasyon sonrasi ilk 10 s): Supervisor 609,9 Mcycles/s = tek cekirdegin %18,21'i → 10 s'de ~1,8 CPU-saniye (6,1 Gcycles); Supervisor Private 151 → 241,7 MB (+~91 MB, geri inmiyor); App 251,4 Mcycles/s (%7,32; bosta %3,4-5,2). Git kismi yeniden olculdu: 5 process seri ~245 ms (bulgudaki ~470 ms degil) + listBranches ~85 ms (145 degil). Disk: 12,17 MB okuma (5.914.117 + 6.257.488 B) + ayni boyutta yazma. Duvar saati suresi olculmedi ('1,5-2,5 s' tahmin olarak kalir; CPU-saniye olcumuyle ayni mertebede). Tepside-derleme senaryosunda TETIKLENMEZ: pencere gizliyken Activated dusmez; etki yalniz pencere one getirildiginde (ve hemen ardindan basilan Build'in bu Sync'i beklemesinde) gorulur. 2-4 cekirdekli makinede ayni is daha uzun surer (oran olculmedi — tahmin).

**Çözüm:** Yapıyı koruyan ucuz ön-kontrol (Core'da, planlama kararı Core'da kalır): aktivasyon tetiğinde Sync'i göndermeden önce App'te değil Supervisor'da bir 'workspace parmak izi' karşılaştır: (a) HEAD sha + branch (zaten HeadReader ile okunuyor, process yok), (b) `.git/index` mtime+size (git yazımı olmadı mı), (c) `git status --porcelain -z` çıktısının hash'i (tek process, 130 ms) — üçü de son tamamlanan Sync'inkiyle aynıysa ve (d) will-build pass'in zaten yaptığı stat geçişi 'hiçbir kaynak boyut/mtime değişmedi' diyorsa `syncCompleted`'ı önceki sonuçla YENİDEN yayınla, scan/evaluate/graf/Flush adımlarını atla. Bu, §12.3'ün 'decisions current the moment the user looks' sözünü bozmaz: değişiklik yoksa karar aynıdır. Alternatif/ek: (1) aktivasyon Sync'inde ListBranchesCommand'ı gönderme (bkz. D11-4); (2) ŞARTLI — spec karar 11'in 5 s eşiğini 30-60 s'ye çekmek (AutoSyncCoordinatorTests 'The_activation_quiet_threshold_is_five_seconds' pinliyor; yeniden yazılır, gerekçe ölçüm). En düşük riskli ilk adım: Silent Sync'te SourceHashCache/EvaluationCache Flush'ını yalnız değişiklik varsa yapmak (D11-3).

**Çözüm (doğrulayıcı düzeltmesi):** Onerilen 'parmak izi' eksik ve §10.3 kararini (satir 1967-1969) + §7.6 cikti kanitini bozabilir: (1) `git status` cikti hash'i, ZATEN kirli bir dosyanin ikinci kez duzenlenmesini gormez (satir ayni kalir) — bunu yalniz kaynak stat gecisi yakalar; stat gecisinin girdi kumesi ise scan+evaluate'ten gelir (SyncWorkspaceService.cs:297-309), yani 'scan/evaluate'i atla' ile '(d) stat gecisi' ayni anda saglanamaz — onceki planin girdi kumesi bellekte tutulmali (bugun servis her komutta yeniden kuruluyor, SupervisorHost.cs:44-48). (2) VS'de disarida derleme (BuiltOutside/OutputReplaced, binder.ChecksFor — :321) HEAD/index/status'u degistirmez; parmak izi cikti dosyalarinin stat'ini da icermezse 'donunce satir dogru' sozu bozulur. (3) 5 s esigini 30-60 s'ye cekmek ayni Sync'i yalniz seyreltir, maliyeti dusurmez ve spec karar 11 + pinli testi (AutoSyncCoordinatorTests.cs:119) degistirir — kullanici karari gerekir. Sirali, davranisi degistirmeyen yol: (a) D11-3 (dirty bayragi + stream IO) → 12 MB yazma ve LOH tahsisleri gider; (b) Sync basina yeniden parse'i kaldirmak icin kok basina uzun omurlu, kilitli cache ornegi; (c) sessiz kipte ListBranches'i gonderme (D11-4); (d) parmak izi kisa devresi ancak bunlardan sonra, Sync adimlari tek tek olculup (scan / evaluate / Prefill stat / Bind x2 / JSON IO) kalan maliyet hala yuksekse ve cikti kaniti + kaynak stat'i kapsayacak sekilde. Ek: Window.Activated yerine uygulama duzeyi aktivasyon (bkz. missing #3).

**Değişmezler:** Planlama Core'da kalır (ön-kontrol SyncWorkspaceService içinde). Git'e yazılmaz (status/rev-parse salt-okur). stdout NDJSON değişmez. Kopya yok: parmak izi tek yerde (SyncWorkspaceService) hesaplanır; App yalnız tetikler.

**Test fikri:** AutoSyncCoordinatorTests'e sahte port ile: aktivasyon tetiği HEAD/index/status parmak izi aynıyken SyncSilentlyAsync'in motora TAM pipeline göndermediğini (ya da SyncWorkspaceService'in scan/evaluate'i atlayıp önceki syncCompleted'ı yayınladığını) pinle; parmak izi farklıyken tam pipeline koşsun.

**Doğrulayıcı notu:** Konumlar dogru: AutoSyncCoordinator.cs:72 (ActivationQuietMs=5000), :180-184, :267 (yalniz 5 s esigi), :289 (`!trigger.IsActivation` ile aktivasyon HEAD kapisindan muaf), :291-292 SyncSilentlyAsync. MainWindow.xaml.cs:350 Activated → OnWindowActivated; :1344-1349 ShowFromTray → Activate. RunViewModel.cs:1402-1404 tam SyncWorkspaceCommand (Fetch=false), :1417 kosulsuz ListBranches. SyncWorkspaceService.cs:101/111/257/262/162 = 5 git process, :132-143 scan+plan, :158 state Load, :321-324 iki Bind + hashes.Flush. Build'in Sync arkasinda beklemesi de dogru: RunViewModel.cs:930 (WorkspaceBusy → QueueRun), SupervisorHost.cs:147-149 (Sync komut dongusunu bloklar). ARCHITECTURE §10.3 satir 1967-1969 bunu bilincli karar olarak anlatiyor ('files may have been edited elsewhere'). Etki sayilari duzeltildi: git sureleri bulgudakinden ~1,7-1,9x dusuk olculdu; pipeline toplami icin tahmin yerine gercek olcum var (C1-show-activation-burst).

#### D11-2 — Koşu sırasında pencereye tek bir dönüş, koşu bitince tam bir sessiz Sync tetikliyor — 'bittiği anda cihaz rahatlasın' ile çelişiyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs` satır 246-259 — Koşu uçuştayken HeadMove'lu tetik 246-254'te ele alınır; aktivasyon (Move=null) 255'e düşer: `IsWorkspaceBusy` (IsRunInFlight dahil, RunViewModel.AutoSync.cs:66) → Remember(trigger) — aktivasyon tetiği koşu boyunca SAKLANIR
- `src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs` satır 197-213 — OnWorkspaceIdle: koşu bitince `_pending ??= RunEnded` — bekleyen aktivasyon varsa RunEnded güvenlik ağı (sha kapılı) DEĞİL aktivasyon (sha kapısız) değerlendirilir → 267'deki 5 s eşiği koşu süresi yüzünden zaten geçmiştir → 291 SyncSilentlyAsync
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1700-1710 — PropagateRunLock → OnWorkspaceBusyChanged → _autoSync.OnWorkspaceIdle() (RunViewModel.Workspace.cs 578-583)

**Kanıt:** Kod izi: koşu 1-3 dk sürerken kullanıcı VS'e geçip geri geldiğinde (ya da tepsi overlay'ine/balloon'a tıkladığında → ShowFromTray → Activate) Activated bir kez düşer; EvaluateAsync 255'te tetiği saklar; runCompleted → OnWorkspaceIdle → post → EvaluateAsync(aktivasyon) → HEAD aynı olsa da satır 289 atlanır → tam Silent Sync. RunEnded güvenlik ağı tek başına (satır 201) sha kapılıdır ve HEAD değişmemişse HİÇBİR ŞEY yapmaz — yani sorun yalnız 'koşu içinde aktivasyon' kombinasyonundadır. decision.log'lar planlama satırlarını içermediği için diskte kanıt yok; davranış koddan kesin.

**Etki:** Build/Cycles bittiği anda (kullanıcı sonucu görmek için pencereye döndüyse — en olası senaryo) Supervisor 1,5-2,5 s daha tam analiz koşar, 7 git process açar, 24 MB JSON IO yapar; oysa koşunun kendi önizlemesi ve sonuçları her satırın yeni durumunu ZATEN yazdı (§10.2 'her preview her satırın kararını yeniden yazar'). Kullanıcının 'bitti ama diskte/CPU'da hâlâ bir şeyler oluyor' hissinin D11 tarafındaki kaynağı budur.

**Etki (doğrulayıcı düzeltmesi):** Kosu bitiminde Supervisor 456 Mcycles/s x 10 s = ~4,6 Gcycles (olcum; kosu sonu Sync + finale birlikte, ayristirilmadi) + App 897 Mcycles/s; Supervisor bellegi 138 → 217 MB (olcum). Yalniz kosu SIRASINDA pencere en az bir kez etkinlestiyse olusur. Birincil senaryoda (tepside kisayolla Build, pencere hic gelmedi) Activated dusmez → kosu sonu yalniz sha kapili RunEnded tetigi calisir ve HEAD ayniysa Sync YOKTUR (:201 + :289) — yani tepside-derlemede bu maliyet odenmiyor.

**Çözüm:** AutoSyncCoordinator.OnWorkspaceIdle'da koşu bitişinde (`_runWasInFlight && !runInFlight`) bekleyen tetik bir AKTİVASYON ise onu RunEnded güvenlik ağına indirge: `if (_pending is { IsActivation: true }) _pending = new Trigger(HeadMove.Other, RunEnded: true, Baseline: _pending.Value.Baseline);` — böylece koşu bitişi yalnız HEAD gerçekten oynadıysa Sync'ler (mevcut satır 289 kapısı), HEAD aynıysa hiçbir şey yapmaz. Gerekçe: koşu, aktivasyonun 'başka yerde dosya düzenlendi' sorusunu zaten cevapladı (planlama pipeline'ı aynı stat geçişini yaptı, sonuçlar defterde). Spec §6.1 'pencereye dönüş → saklanır' cümlesi 'koşu bitince güvenlik ağı olarak değerlendirilir' diye yerinde yeniden yazılır; ARCHITECTURE §10.3 'A return to the window is the second trigger' paragrafına bir cümle. HeadMove'lu (checkout/pull) tetikler aynen kalır.

**Çözüm (doğrulayıcı düzeltmesi):** Aktivasyonu RunEnded'e indirgemek ARCHITECTURE §10.3 (satir 1967-1969: 'runs a silent Sync even when HEAD has not moved, because files may have been edited elsewhere') ve §13 satir 4617-4619 ('the silent one a return to the window starts included — decides every row afresh ... so an output that went stale in the background after the run does not stay green') ile catisir: kullanici kosu bitince pencerede kalirsa yeni bir aktivasyon gelmez ve kosu sirasinda duzenlenen dosyanin satiri yesil kalir. Dokuman-kod uyumu burada tam; degisiklik bir kullanici kararidir. Catismasiz yol: tetigi koru, Sync'i ucuzlat (D11-3 dirty bayragi + stream IO + uzun omurlu cache; sessiz kipte ListBranches yok). Kullanici davranis degisikligini kabul ederse: indirgeme yerine kosu sonu aktivasyon Sync'ini finale/animasyon bittikten sonraya ertelemek (ayni Sync, CPU cakismasi yok) daha az riskli; her iki halde de yeni kural icin test (kosu icinde aktivasyon → kosu bitisi) yazilmali.

**Değişmezler:** Git'e yazılmaz; planlama Core'da; Sync yolu değişmez, yalnız tetik ağırlığı. Tek tanım: Trigger.RunEnded zaten var, yeni kavram eklenmez.

**Test fikri:** AutoSyncCoordinatorTests: koşu uçuştayken WindowActivatedAsync → koşu biter (IsRunInFlight false, OnWorkspaceIdle) → HEAD son Sync ile aynı → port.SyncSilentlyAsync ÇAĞRILMAZ; HEAD farklı → çağrılır. Mevcut 'activation is remembered during a run' testi varsa yeni kuralı pinleyecek şekilde yeniden yazılır.

**Doğrulayıcı notu:** Kod izi dogru: AutoSyncCoordinator.cs:246 yalniz `trigger.Move is {}` olan tetikleri ele alir; aktivasyon (Move=null) :255'e duser, IsWorkspaceBusy (RunViewModel.AutoSync.cs:66 = WorkspaceBusy || IsRunInFlight) → Remember. :198-202 kosu bitisinde `_pending ??=` oldugu icin bekleyen aktivasyon RunEnded guvenlik aginin yerini alir; :267 esigi kosu suresi yuzunden gecmistir; :289 aktivasyonu muaf tutar → :291 tam sessiz Sync. RunViewModel.cs:1709 PropagateRunLock → OnWorkspaceBusyChanged; RunViewModel.Workspace.cs:578-580 → OnWorkspaceIdle. Mevcut testler bu kombinasyonu pinlemiyor (AutoSyncCoordinatorTests: :365 ve :377 yalniz aktivasyonsuz kosu bitisini pinliyor). Olcumle tutarli: 'kosu bitince ilk 10 s Supervisor 456 Mcycles/s' — o olcumde pencere kosu sirasinda one getirilmisti. ANCAK onerilen cozumun gerekcesi yanlis: 'kosu aktivasyonun sorusunu zaten cevapladi' yalniz PLANLAMA anina kadar dogrudur; 142 s'lik kosu SIRASINDA duzenlenen dosya (gelistirici VS'de calismaya devam eder) planlamadan sonra degisir ve kosu onu gormez.

#### D11-3 — Her Sync (sessiz dahil) 12 MB JSON defteri yeniden PARSE edip 12 MB'ı koşulsuz YENİDEN YAZIYOR

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 44-48, 160-168 — WorkspaceServices.Default: `root => new SyncWorkspaceService(..., new EvaluationCache(path), ..., new SourceHashCache(path))` — fabrika her SyncWorkspaceCommand'da (168: `workspace.Sync(cmd.RootPath)`) yeni örnek kurar
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 17, 97-105, 142-147 — `_entries = Load(cachePath)` alan başlatıcısı → kurucuda eager 5,9 MB Deserialize; Flush() koşulsuz Serialize + yaz (dirty bayrağı yok)
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 51-54, 146-162, 195-201 — kurucuda eager 6,25 MB Deserialize; Flush() koşulsuz 6,25 MB Serialize + atomik yaz
- `src/BuildOrchestrator.Core/Planning/BuildPlanBuilder.cs` satır 42-43 — `cache.Flush()` her Build() çağrısında — Sync (SyncWorkspaceService.cs 142) ve koşu planlaması (Program.cs 105) dahil
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` satır 324 — `hashes.Flush()` her Sync'te koşulsuz

**Kanıt:** Disk: evaluation-cache.json 5.901.064 B, source-hash-cache.json 6.251.491 B (`ls -la`). Kod: iki cache'te de 'değişti mi' bayrağı yok (grep `_dirty|Dirty` boş). Ortak bağlam: evaluation-cache'in %93'ü test süitinin bo-vm-* girdileri (gerçekte ~200 girdi yeterli). Sync başına: 12,1 MB okuma+reflection deserialize + 12,1 MB serialize+yazma+rename. System.Text.Json reflection ile ~100-200 MB/s → parse+serialize ≈ 120-250 ms CPU (tahmin) + disk yazımı.

**Etki:** D11-1/D11-2'deki her aktivasyon/koşu-sonu Sync'i ve her Build planlaması bu maliyeti öder; hiçbir csproj/kaynak değişmediğinde yazılan dosya bayt bayt öncekiyle aynıdır. SSD'ye günde onlarca kez 12 MB gereksiz yazım.

**Etki (doğrulayıcı düzeltmesi):** Her Sync VE her Build planlamasi (tepside kisayolla Build dahil — Program.cs:94-97, :119, :201) 12,17 MB JSON okur+parse eder ve 12,17 MB'i yeniden yazar. Bellek tarafi bulguda yok: File.ReadAllText iki dosyayi UTF-16 string'e cevirir (~11,8 + ~12,5 MB), Flush'ta JsonSerializer.Serialize ayni boyutta iki string daha uretir → Sync basina ~48 MB LOH string tahsisi (dosya boyutlarindan turetildi) + SourceHashCache.Flush'ta 24.588 girisli sozluk kopyasi (SourceHashCache.cs:151-153). Bu, olculen 'aktivasyon Sync'inden sonra Supervisor Private 151 → 242 MB, geri inmiyor' ile ayni yonde; payi ayri olculmedi (tahmin). CPU suresi olculmedi.

**Çözüm:** (1) İki cache'e `_dirty` bayrağı: GetOrEvaluate/HashOf yeni ya da güncellenen girdi yazınca true; Flush() `if (!_dirty) return;` — tek satırlık, davranış değişmez. (2) Supervisor ömrü boyunca tek EvaluationCache/SourceHashCache örneği: WorkspaceServices.Default'ta iki cache'i fabrika DIŞINDA bir kez kur ve lambdalara kapat (Sync, Optimize ve koşu planlaması — Program.cs 96-102 de kendi örneğini kuruyor — aynı örneği paylaşır; kopya yasağına da uyar: 'iki yüzey aynı özetleri iki kez hesaplamaz' ilkesi zaten §16'da). Dikkat: Optimize'ın PruneMissingUnderRoot'u aynı örnekte çalışır, sorun yok. (3) Test kirliliği (bo-vm-*) başka boyutun konusu; ama temizlenirse parse maliyeti ~15× düşer.

**Çözüm (doğrulayıcı düzeltmesi):** (1) dirty bayragi dogru ve dusuk riskli; ayrinti: EvaluationCache'te bayrak :58 (mtime/size yenileme), :61 (yeni giris) ve Prune'da; SourceHashCache'te :80 ve Seed/Prune'da kurulmali. Racy girisler (mtime son 2 s) diske yazilmadigi icin sonraki ornekte yeniden hash'lenir ve o Sync yine yazar — kabul edilebilir. (2) 'tek ornek paylas' OLDUGU GIBI GUVENLI DEGIL: EvaluationCache duz Dictionary kullanir (EvaluationCache.cs:17) ve kendi yorumu (:85-88) kosu planner thread'i ile es zamanli dispatch edilen Sync'in paralel calistigini soyluyor (StartRunCommand hemen doner, SupervisorHost.cs:116). Paylasilan ornek icin once kilit/ConcurrentDictionary gerekir; ayrica uzun omurlu ornek diskteki dosyayi baska surecin (testler ayni cacheRoot'a yaziyorsa) degistirmesini gormez — dosya mtime'i degistiyse yeniden yukle kurali eklenmeli. (3) Ek, davranissiz kazanc: ReadAllText/WriteAllText+Serialize(string) yerine stream tabanli JsonSerializer.Deserialize(FileStream) / Serialize(Stream) → UTF-16 ara string'ler (Sync basina ~48 MB LOH) hic olusmaz. Test: degisiklik yokken ikinci Flush dosyanin LastWriteTime'ini degistirmez (kirmizi gosterilebilir).

**Değişmezler:** Defter biçimi ve atomik yazım (AtomicFile deseni) değişmez; Core saf kalır; OutDir'e dokunulmaz.

**Test fikri:** EvaluationCacheTests/SourceHashCacheTests: değişiklik olmadan iki kez Flush → dosyanın LastWriteTime'ı ikinci çağrıda değişmez; bir girdi eklenince değişir.

**Doğrulayıcı notu:** Konumlar dogru: SupervisorHost.cs:44-48 fabrika her Sync komutunda yeni EvaluationCache + SourceHashCache kurar (:168 `workspace.Sync(cmd.RootPath)`); EvaluationCache.cs:17 alan baslaticisinda Load (:142-151 File.ReadAllText + Deserialize), :96-105 Flush kosulsuz; SourceHashCache.cs:51-55 kurucuda Load, :148-166 Flush kosulsuz; BuildPlanBuilder.cs:43 `cache.Flush()` her Build()'de; SyncWorkspaceService.cs:324 ve Program.cs:201 `hashes.Flush()`; Program.cs:94,97 kosu planlamasi da kendi orneklerini kurar. `_dirty` benzeri bayrak yok (dosyalar bastan sona okundu). Dosya boyutlari bugun: evaluation-cache.json 5.914.117 B, source-hash-cache.json 6.257.488 B. Parse/serialize suresi (120-250 ms) OLCULMEDI — tahmin. Cozumun (2) maddesi oldugu gibi uygulanamaz (asagida).

#### D11-4 — Her Sync'ten sonra (sessiz dahil) koşulsuz ListBranches: 2 git process daha

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1417 — `await TrySendAsync(new ListBranchesCommand(RootPath), "listBranches")` — kipten bağımsız, her SyncCoreAsync'te
- `src/BuildOrchestrator.Core/Git/GitService.cs` satır 153, 159 — for-each-ref + içinde GetCurrentBranchAsync (symbolic-ref) = 2 process

**Kanıt:** Ölçüm: for-each-ref 76 ms (157 ref), symbolic-ref 68 ms → ~145 ms/Sync. Yorumdaki gerekçe (1405-1416) 'Sync salt-okur, tekrarı zararsız' — doğru ama sessiz Sync'te branch envanteri neredeyse hiç değişmez; HEAD izleyicisi zaten branch değişimini ayrı tetikler.

**Etki:** Aktivasyon ve koşu-sonu Sync'lerinin her birine ~145 ms ve 2 process eklenir; tek başına küçük, D11-1 ile çarpılır.

**Etki (doğrulayıcı düzeltmesi):** Sessiz Sync basina ~85 ms ve 2 process (olcum, sicak, makine bosta). Tepside-derleme yolunda yok (Build ListBranches gondermez).

**Çözüm:** ListBranches'ı yalnız görünür kiplerde (mode.IsVisible(): Manual/Appended/BranchChange/ConfigurationChange) ya da `syncCompleted.HeadSha/ActiveBranch` son bilinenle farklıysa gönder. Tek liste: SyncModeRules'a `RefreshesBranchList` eklenir (kopya değil, mevcut kural tablosu genişler). Alternatif: `.git/packed-refs` + `.git/refs` klasörü mtime parmak izi aynıysa atla (HeadReader gibi process'siz).

**Çözüm (doğrulayıcı düzeltmesi):** 'Yalniz gorunur kiplerde' kurali iki seyi bayatlatir: (a) sessiz Commit Sync'inden sonra aktif branch'in BranchRef.Sha'si; (b) VS'nin arka plan fetch'iyle gelen yeni remote branch'ler — bunlar logs/HEAD'e yazmaz (§10.3 satir 1966-1967), bugun yalniz pencereye donus Sync'inin ListBranches'i tazeliyor. Dogru bicim: envanteri branch popover'i ACILIRKEN iste (liste yalniz orada tuketiliyor) + gorunur kiplerde bugunku gibi; ya da bulgunun ikinci secenegi (`packed-refs` + `refs/` mtime parmak izi ayniysa atla — process'siz). Kural tek yerde (SyncModeRules) kalmali.

**Değişmezler:** Salt-okur; tek huni korunur (yine SyncCoreAsync'ten gider).

**Test fikri:** RunViewModelTests: SyncSilentlyAsync sonrası gönderilen komutlarda ListBranchesCommand YOK; SyncAsync (Manual) sonrası VAR.

**Doğrulayıcı notu:** RunViewModel.cs:1417 `TrySendAsync(new ListBranchesCommand(RootPath), "listBranches")` kipten bagimsiz, her SyncCoreAsync'te (ListBranchesCommand'in tek gonderim noktasi — grep). GitService.cs:153 for-each-ref + :159 GetCurrentBranchAsync (symbolic-ref) = 2 process. Sayi duzeltildi: yeniden olcumde for-each-ref 43-44 ms, symbolic-ref 40-42 ms → ~85 ms (bulgudaki 145 ms degil). Supervisor'da ayri komut olarak, Sync'ten SONRA islenir (SupervisorHost.cs:129) — komut dongusunu ~85 ms daha mesgul tutar.

#### D11-5 — Her Build'de harici kök güncellemesi taramadan ÖNCE ve seri: DoganTrend için ağ fetch (Azure DevOps) + 5 git process koşunun başına eklenir

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: orta |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.Supervisor/Program.cs` satır 88-90, 100 — ShouldUpdate → ExternalUpdater.UpdateAsync(...).GetAwaiter().GetResult() senkron; ana kök taraması (100) ancak ondan SONRA başlar
- `src/BuildOrchestrator.Core/Externals/ExternalUpdater.cs` satır 91-95, 105, 112-115 — kartlar foreach ile seri; OSYSLogo kartı için VcsDetector.FindRoot null → yalnız uyarı satırı (git yok)
- `src/BuildOrchestrator.Core/Git/RepositoryWriter.cs` satır 94, 101, 111, 112, 118 — FastForwardUpdater: status --porcelain -z → symbolic-ref → fetch origin <b> --no-tags (AĞ) → rev-parse HEAD → (fetch içinde rev-parse remote ref) → AlreadyCurrent

**Kanıt:** ui-state.json: ExternalProjects = [IntegrationHub\OSYSLogo, CustomerProject\DoganTrend], UpdateExternals=true. OSYSLogo yolunda git kökü yok (`git rev-parse --show-toplevel` boş) → her Build'de 'no working copy' uyarısı, VCS komutu yok. DoganTrend kökü D:\Projects\Delta\CustomerProject, origin https://…dev.azure.com/… (2.208 izlenen dosya, status 115 ms). Fetch süresi ölçülmedi (ağ + kimlik yöneticisi; salt-okur kural gereği fetch koşturulmadı) — TLS+auth+ref negotiation tipik 0,5-3 s (tahmin). Kararlı durumda sonuç hep AlreadyCurrent. decision.log koşu başlangıcından itibaren yazdığı için planlama/‘Updating external’ zaman damgaları diskte yok — bu satırlar yalnız IPC PlanProgressEvent olarak App'e gider.

**Etki:** Her Build'in planlamasına ~1-3 s (tahmin) seri gecikme; ana kök taraması (764 ms) bu bekleyişle üst üste bindirilmiyor. Cycles ve Clean modları zaten atlıyor (ShouldUpdate). Bir kart (OSYSLogo) hiç git değil — kullanıcı için işe yaramayan uyarı satırı her koşuda.

**Etki (doğrulayıcı düzeltmesi):** UpdateExternals acikken her Build'in (tepside kisayolla baslatilan dahil) planlamasi, git kokulu kart basina ~200 ms yerel git + bir ag fetch'i (suresi olculmedi) kadar gec baslar. Bu bekleyis CPU degil ag/IO bekleyisidir; zayif makinede CPU yukune etkisi kucuk, 'Build'e bastim, ne zaman basliyor' gecikmesine etkisi dogrudan. Ust sinir koddan: fetch 30 s tavanli (GitService.cs:72, :200) — bkz. missing #1.

**Çözüm:** Yapıyı koruyan: (1) Harici güncellemeyi ana kök taramasıyla PARALEL koştur — ana kökün taranması harici kökün ff'sine bağlı değildir (§10.4 'ff yeni proje dosyası getirebilir' yalnız harici kökün kendi taraması için geçerli); ExternalWorkspaceResolver.Resolve harici kökleri ana taramadan sonra zaten ayrı tarıyor, o adım güncellemeyi await eder. (2) Kartları birbirine paralel güncelle (bağımsız repolar; sıra yalnız konsol satırları için — satırlar bitişte sıralı yazılabilir). (3) ŞARTLI — 'son N dakikada zaten fetch edildiyse ağa çıkma' kısa devresi: §10.4 'every Build updates their working copies' kuralını değiştirir; kullanıcıya sorulmalı. (4) Kullanıcı tarafı: OSYSLogo kartı git değilse UpdateExternals'ın ona etkisi yok; uyarı satırı gürültü — kart 'no working copy' ise satırı yalnız ilk koşuda yazmak düşünülebilir (kozmetik).

**Çözüm (doğrulayıcı düzeltmesi):** (1) 'Ana tarama ile paralel' kazanci sinirli: harici kokun taramasi ve BuildPlanBuilder guncellemeyi beklemek ZORUNDA (Program.cs:74-77 yorumu, §10.4 satir 2025); ust uste binebilecek tek is ana kok dosya taramasi (scanner.Scan(cmd.RootPath)) ve iki cache'in yuklenmesi (12 MB JSON parse) — kazanc = min(fetch suresi, tarama+yukleme suresi), ikisi de ayri olculmeden sayi verilemez. (2) Kartlari paralel guncellemek bugunku veriyle kazanc getirmez: 2 karttan yalniz biri git (OSYSLogo'da calisma kopyasi yok). (3) 'Son N dakikada fetch edildiyse atla' §10.4 'every Build updates their working copies' kuraliyla catisir — bulgu da SARTLI demis; kullanici karari. Once olc: harici fetch'in gercek suresi PlanProgress satirlarinin ('Updating external…' → 'Updated …') zaman farkindan alinabilir; karar o sayiyla verilmeli.

**Değişmezler:** Git yazımı yalnız RepositoryWriter.cs'te kalır; ff-only + kir kapısı aynen; planlama Core'da (paralellik Program.cs'in BuildRunPlan'ında Task.WhenAll ile).

**Test fikri:** Supervisor planner testi: sahte IProcessRunner ile harici fetch 500 ms geciktirildiğinde ana tarama satırı ('Scanning N solutions') fetch bitmeden yayınlanır (paralellik); ExternalPreparationException yine koşuyu durdurur.

**Doğrulayıcı notu:** Program.cs:88-90 `ExternalUpdater.UpdateAsync(...).GetAwaiter().GetResult()` taramadan once ve senkron; ana tarama :105-106. ExternalUpdater.cs:91-95 kartlar seri; :112-116 calisma kopyasi yoksa yalniz uyari satiri. RepositoryWriter.cs:94 status, :101 symbolic-ref, :111 fetch (icinde GitService.cs:204 rev-parse remote), :112 rev-parse HEAD, :118 AlreadyCurrent = kart basina 5 process — dogru. ShouldUpdate (ExternalUpdater.cs:72-73) Cycles/Clean'i atlar — dogru. Fetch suresi OLCULMEDI (bulgu da kabul ediyor): '1-3 s' tahmin. Yerel 4 process yeniden olcumle ~200 ms (status 115 ms bulgu olcumu + 3 x ~41 ms).

#### D11-6 — git status 27k dosyalık OSYS'te ~130 ms — core.untrackedCache / fsmonitor kapalı (araç dışı, kullanıcı repo ayarı)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/Git/GitService.cs` satır 127 — `status --porcelain -z` (-uall değil, doğru); her Sync'te + her harici ff'de + pull/checkout kir kapısında

**Kanıt:** OSYS: `git config core.untrackedCache` → boş (exit 1), `core.fsmonitor` → boş; git 2.51.0.windows.2; 27.144 izlenen dosya, 354 bin/obj dizini (.gitignore ile budanıyor), ağaç temiz; status 126-135 ms (3 ölçüm); `git update-index --test-untracked-cache` → OK (dosya sistemi destekliyor). DoganTrend: 115 ms.

**Etki:** Sync başına ~130 ms; D11-1 sıklığıyla çarpılır. Tek başına küçük.

**Etki (doğrulayıcı düzeltmesi):** Sync basina ~75-80 ms (olcum). Process baslatma tabani ~40 ms oldugundan (rev-parse 40 ms) ayarla kazanilabilecek ust sinir ~35-40 ms/Sync. Tepside-derleme yolunda ana repoda status calismaz (Build planlamasi yalniz rev-parse + symbolic-ref cagirir, Program.cs:183-185); yalniz harici kartin kir kapisinda calisir.

**Çözüm:** Araç dışı öneri (kullanıcı, OSYS reposunda, isteğe bağlı): `git config core.untrackedCache true` (status'ta untracked taramasını mtime ile kısa devreler, genelde 2-3× hızlanır — tahmin) ve/veya `git config core.fsmonitor true` (yerleşik daemon; status ~20-40 ms'ye iner — tahmin; ancak `git fsmonitor--daemon` arka planda kalıcı bir process açar — 'boşta cihaz rahat' isteğiyle tartılmalı, untrackedCache daemon'suzdur). Araç bunları YAZMAZ (git'e kendiliğinden yazmama değişmezi); en fazla README'ye 'büyük repoda öneri' notu.

**Çözüm (doğrulayıcı düzeltmesi):** Yapmaya degmez duzeyde; en fazla README notu. fsmonitor onerilmemeli: kalici `git fsmonitor--daemon` sureci 'bosta cihaz rahat' hedefiyle ters.

**Değişmezler:** Araç git config'e dokunmaz; öneri kullanıcı elinde.

**Test fikri:** Yok (araç dışı ayar); README notu doğrulaması yeterli.

**Doğrulayıcı notu:** GitService.cs:127 `status --porcelain -z` (-uall yok) dogru. Yeniden olcum: OSYS'te 74-81 ms (3 olcum, --no-optional-locks, makine bosta) — bulgudaki 126-135 ms'den dusuk; 27.245 izlenen dosya; core.untrackedCache ve core.fsmonitor ayarsiz (exit 1); git 2.51.0.windows.2. Onerilen '2-3x hizlanir' / '20-40 ms' rakamlari olculmedi (tahmin). Arac disi, kullanici ayari — arac git config'e yazmaz (degismezle uyumlu).

#### D11-7 — AutostartService.Apply her açılışta Run anahtarını koşulsuz yazıyor (önce okuyup karşılaştırmıyor)

| Alan | Değer |
|---|---|
| Önem | yapmaya-degmez |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Services/AutostartService.cs` satır 96 — `public void Apply(bool autostartEnabled) => TryWrite(() => WriteRunValue(autostartEnabled), out _);` — mevcut değerle karşılaştırma yok
- `src/BuildOrchestrator.App/App.xaml.cs` satır 136-139 — her açılışta `Apply(ShellSwitches.StartWithWindows(uiState))`

**Kanıt:** Kod: Apply doğrudan yazar; §12.3 'every start reconciles the value' bunu bilinçli anlatıyor (exe taşındıysa yeniden hedefle). Maliyet: tek HKCU registry yazımı, milisaniye altı — ölçülmedi.

**Etki:** Pratikte ihmal edilebilir (bir registry değeri); 'gereksiz yazım' sınıfında, Autostart=false iken değer silme çağrısı da her açılışta gider.

**Etki (doğrulayıcı düzeltmesi):** Acilis basina tek registry yazimi; performans bulgusu degil.

**Çözüm:** WriteRunValue'da önce mevcut değeri oku; aynıysa (ya da kapalı ve değer yoksa) yazma. Davranış aynı kalır (exe taşındıysa değer farklıdır → yazılır). §12.3 cümlesi doğru kalır.

**Değişmezler:** —

**Test fikri:** AutostartServiceTests: değer zaten doğruyken Apply → sahte registry'nin Write sayacı 0.

**Doğrulayıcı notu:** Kod dogru: AutostartService.cs:96 Apply → WriteRunValue (:134-138) karsilastirmasiz; App.xaml.cs:139 her acilista. Ancak etki olculmedi ve yapisal olarak tek HKCU deger yazimi (acik) ya da DeleteValue(throwOnMissingValue:false) (kapali; deger yoksa yazim da yok — AutostartService.cs:55-59). Acilis basina bir kez; CPU/RAM/akicilik hedeflerinin hicbirine olculebilir katkisi yok. §12.3 satir 2388 'every start reconciles the value' bunu bilincli anlatiyor.

**Doğrulayıcının eklediği noktalar:**
- GitService.cs:72 + :200 ve ProcessRunner.cs:26-38: fetch 30 s tavanla ve GIT_TERMINAL_PROMPT / GCM_INTERACTIVE ortam degiskeni OLMADAN calisir (src'de hic gecmiyor — grep). Ag/VPN yokken ya da Azure DevOps kimligi dusmusken (a) UpdateExternals acik her Build'in baslangici git kart basina 30 s'ye kadar bekleyebilir (Program.cs:88-90), (b) acilis Sync'i fetch'lidir (RunViewModel.cs:2667 SyncMode.Appended; SyncMode.cs:49) ve komut dongusunu bloklar (SupervisorHost.cs:147-149) — Windows acilisinda tepside baslayan uygulamada kisayolla basilan ilk Build bu fetch'in arkasinda kuyruklanir (RunViewModel.cs:930). Sure ust siniri koddan; gercek bekleyis olculmedi.
- EvaluationCache.cs:103,:147 ve SourceHashCache.cs:159,:201: JSON, File.ReadAllText / JsonSerializer.Serialize(string) + File.WriteAllText ile tam UTF-16 string uzerinden gecer — Sync ve Build planlamasi basina ~48 MB LOH string (5,9 + 6,26 MB dosyalarin okuma ve yazmada UTF-16 karsiligi). Olculen 'Sync'ten sonra Supervisor Private +91 MB, geri inmiyor' ile ayni yonde; stream tabanli Deserialize/Serialize ile ara string'ler kalkar (D11-3'un bellek yarisi, bulguda yok).
- MainWindow.xaml.cs:350: tetik `Window.Activated` — yalniz baska uygulamadan donuste degil, uygulamanin KENDI modal pencereleri kapaninca da duser (SettingsDialog.xaml.cs:154, AboutDialog.xaml.cs:139, NotesDialog.xaml.cs:70, dosya/klasor diyaloglari MainWindow.xaml.cs:1012/1023/1089). Son Sync'ten 5 s gectiyse her diyalog kapanisi tam sessiz Sync kosturur (AutoSyncCoordinator.cs:267, :291); 'baska yerde dosya duzenlendi' gerekcesi bu durumda gecerli degil. Uygulama duzeyi aktivasyon (Application.Activated) ayni sozu daha az tetikle tutar.
- Program.cs:94, :97 + SupervisorHost.cs:46-48: Sync ve kosu planlamasi ayni iki defteri AYRI orneklerle acar ve ikisi de tumunu geri yazar (BuildPlanBuilder.cs:43, Program.cs:201, SyncWorkspaceService.cs:324) — es zamanli kosarlarsa (EvaluationCache.cs:85-88 yorumu bu durumu kabul ediyor) son yazan kazanir, digerinin yeni girisleri kaybolur ve sonraki kosuda yeniden hash/evaluate edilir. Dogruluk sorunu degil (cache optimizasyon), ama tekrar is; dirty bayragi tek basina bunu cozmez.
- ExternalUpdater.cs:112-116: calisma kopyasi olmayan kart (bugun OSYSLogo) icin her Build'de VcsDetector.FindRoot ust dizinleri yurur ve 'no working copy' uyari satiri IPC'den gider; ayni kok icin Program.cs:129-130 ExternalRevisionReader.cs:38 FindRoot'u ikinci kez cagirir. Maliyet kucuk (birkac File/Directory.Exists; olculmedi), ama ayni soru ayni planlamada iki kez soruluyor.

**Temiz bulunan alanlar:**
- HEAD izleyicisi (Core/Git/HeadWatcher.cs 28-181): FileSystemWatcher yalnız `<gitdir>\logs` klasöründe, filtre `HEAD`, alt dizin yok; yoklama yok; SettleDebouncer 1,5 s (satır 31) ardışık yazımları tek tetiğe indiriyor; reflog okuması yalnız eklenen baytları okuyor (offset tutuyor, 128-148). Boşta sıfır iş — temiz.
- Git işlemi yoklaması (RunViewModel.GitOperation.cs 255-263): 2 s'lik DispatcherTimer YALNIZ bir işaret (MERGE_HEAD/rebase/index.lock…) dururken kurulur (260-261: None → Stop, aksi → Start); normal durumda hiç çalışmaz. GitOperationProbe ve HeadReader process'siz dosya okuması (~6 File.Exists) — temiz.
- Koşu bitişi güvenlik ağı (AutoSyncCoordinator.cs 197-201 + 289): tek başına HEAD sha kapılıdır — HEAD oynamadıysa koşu sonunda Sync YOKTUR. Sorun yalnız koşu içinde aktivasyon birikmesiyle çıkıyor (D11-2).
- Sessiz Sync fetch yapmaz (SyncMode.cs 49 `Fetches` yalnız Manual/Appended; SyncWorkspaceService.cs 255-259 fetch=false dalı `refs/remotes/origin/<b>`'yi yerelden okur) — otomatik tetikler ağa çıkmaz. 'N behind' tek yerel process (rev-list --count, 128 ms) — temiz.
- git status `-uall` DEĞİL (GitService.cs 127, ARCHITECTURE §10.2 ile uyumlu); untracked klasör tek satır — doğru seçim.
- UpdateService (Services/Updates/UpdateService.cs 20-21, 46-50, 61-68): ilk kontrol 5 s, sonra 4 saatte bir; tek-uçuş (Interlocked), hata sessiz ve YENİDEN DENEME FIRTINASI YOK (yalnız bir sonraki periyodik tik); kurulu olmayan kopya hiç kontrol etmez (Start: `if (!updater.IsInstalled) return`). Günde ~6 HTTP isteği — temiz. Tek not: CheckAsync/DownloadAsync CancellationToken.None ile — çıkışta process ölümü zaten keser.
- Mutasyon yüzeyi tek dosyada (RepositoryWriter.cs); ff-only + kir kapısı + `merge-base --is-ancestor` sırası; ana repoda kendiliğinden pull yok — kaynak guard'larıyla pinli. Cycles ve Clean modları harici güncellemeyi atlıyor (ExternalUpdater.ShouldUpdate 72-73).
- Sync'te build-state tek kez Load (SyncWorkspaceService.cs 158, ComputeWillBuild'e parametre olarak veriliyor) — Sync yolunda tekrar okuma yok; 148 KB, ucuz.
- Branch listesi tek for-each-ref çağrısından (GitService.cs 151-153) — branch başına rev-parse yok.

**Açık sorular:**
- Pencereye dönüş Sync'i (spec karar 11, 5 s) sizin için ne kadar kritik? 'Başka yerde dosya düzenledim, dönünce satırlar hemen griye dönsün' beklentisi varsa D11-1'deki parmak izi ön-kontrolü tercih edilir (davranış korunur); yoksa eşik 30-60 s'ye çekilebilir (şartlı, spec değişir).
- Koşu bitince (Build/Cycles tamamlanınca) hiçbir Sync koşmamasını mı istiyorsunuz, yoksa HEAD oynadıysa (koşu sırasında commit/pull) koşu sonu Sync'i kalsın mı? D11-2 önerisi ikincisi (yalnız HEAD kapısı); ilkini isterseniz RunEnded güvenlik ağı da kaldırılır ama izleyicinin kaçırdığı HEAD hareketi bir sonraki tetiğe kalır.
- D:\Projects\Delta\IntegrationHub\OSYSLogo kartının üstünde .git yok — bu kart bilerek git'siz mi (kopya klasör)? Öyleyse her Build'deki 'no working copy' uyarısı gürültü; kartı çıkarmak ya da satırı ilk koşuya indirmek ister misiniz?
- DoganTrend (Azure DevOps) ff-only fetch'inin her Build'de koşması şart mı, yoksa 'son X dakikada fetch edildiyse atla' (şartlı, §10.4 'every Build updates' kuralı değişir) kabul edilebilir mi? Kabul edilirse her Build'in başından tahminen 1-3 s düşer.
- OSYS reposunda `core.untrackedCache=true` (daemon'suz) ya da `core.fsmonitor=true` (arka planda git fsmonitor--daemon process'i) ayarlarından birini kendiniz açmak ister misiniz? Araç bunlara dokunmaz; ikincisi boşta bir process demektir.


### D8-acilis-kapanis

#### D8-1 — Her Sync (açılış Sync'i dahil) ve her Build'de iki defter (5,9 MB + 6,25 MB JSON) koşulsuz parse edilip koşulsuz yeniden yazılıyor — defterler Supervisor'da yaşatılmıyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) SupervisorHost.cs:43-57; Program.cs:94, 97, 201; EvaluationCache.cs:17, 96-104, 142-151; BuildPlanBuilder.cs:43; SyncWorkspaceService.cs:324; SourceHashCache.cs:148-166, 195-208
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 36-48 — WorkspaceServices.Default: her Sync çağrısında `new EvaluationCache(evaluationCachePath)` + `new SourceHashCache(sourceHashPath)` + `new BuildStateStore` (factory, memoize yok)
- `src/BuildOrchestrator.Supervisor/Program.cs` satır 94-97 — BuildRunPlan: her Build'de yine `new SourceHashCache(...)` ve `new EvaluationCache(cachePath)`
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 17, 142-148, 96-104 — ctor'da Load → File.ReadAllText + JsonSerializer.Deserialize (reflection); Flush() dirty kontrolü olmadan tüm sözlüğü serialize edip temp+rename yazar
- `src/BuildOrchestrator.Core/Planning/BuildPlanBuilder.cs` satır 43 — `cache.Flush()` her Build çağrısında koşulsuz
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` satır 324 — `hashes.Flush()` her Sync'te koşulsuz
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 148-160, 195-208 — Flush: 24.5k girişi ToDictionary + Serialize + WriteAllText; Load: 6,25 MB Deserialize

**Kanıt:** Kod: iki defter her komutta ctor'da diskten okunur ve iş sonunda değişsin değişmesin diske yazılır (EvaluationCache.cs:96-104 ve SourceHashCache.cs:148-160'ta 'değişti mi' bayrağı yok). Disk kanıtı (salt-okur): %LOCALAPPDATA%\BuildOrchestrator\evaluation-cache.json 5.901.064 B ve source-hash-cache.json 6.251.491 B, ikisinin de mtime'ı 01:58 (son Sync/Build) — yani hiç değişmese de her seferinde yeniden yazılıyor. Düzeltme (ortak bağlama): Supervisor SPAWN'ında bu defterler parse EDİLMİYOR — Program.cs:31-35 yalnız BuildStateStore ctor'u (Load yok, BuildStateStore.cs:24) + InFlightLedger.Recover (run-inflight.json 458 B) çalıştırır; ilk parse açılış Sync'inde (OnEngineReady → SyncMode.Appended, RunViewModel.cs:2667) olur ve her Sync/Build'de tekrarlanır. evaluation-cache'in %93'ü (2558 giriş) test temp'i (bo-vm-*) olduğundan parse maliyeti ~14× şişkin.

**Etki:** Her Sync ve Build'de ~12 MB JSON okuma + ~12 MB yazma ve reflection tabanlı deserialize. Ölçülmedi; tahmin: bu CPU'da parse 150-400 ms + serialize/yazma 60-150 ms per Sync, artı Supervisor'da her komutta yeni büyük sözlüklerin GC churn'ü (24.5k + 2.7k giriş). Açılışta 'Syncing…' penceresini ve her Build'in planlama süresini doğrudan uzatır.

**Etki (doğrulayıcı düzeltmesi):** Her Sync ve her Build'de ~12 MB JSON okuma + ~12 MB yazma (kodda ve diskte kesin). Sure olculmedi; ust sinir: tepsiden Build'de koreografi disindaki tum hazirlik (harici kok ag turu dahil) ~2,1 s. Olculen yan etki: Sync basina Supervisor Private'i +36..+81 MB oynuyor.

**Çözüm:** (1) Dirty bayrağı: EvaluationCache/SourceHashCache'te `_entries` gerçekten değiştiyse Flush yazsın, aksi halde no-op (Sync'in 'yalnız Load' anlatısıyla da hizalanır). (2) Defterleri Supervisor ömrü boyunca cacheRoot başına TEK örnek olarak yaşat: WorkspaceServices.Default içinde lazy memoize (Lazy<EvaluationCache>, Lazy<SourceHashCache>) ve Program.BuildRunPlan aynı örnekleri kullansın; Sync/Run/Optimize App tarafında zaten seri (WorkspaceBusy kapısı) ama Supervisor'da bir SemaphoreSlim ile korunsun; Optimize'ın prune'u aynı örnek üzerinde çalışsın. Planlama Core'da kalır, yalnız örnek ömrü değişir. (3) System.Text.Json source generator (JsonSerializerContext) — Entry/EvaluatedProject/BuildState için reflection ısınmasını ve parse süresini düşürür. (4) Kirlilik: Optimize'a kök-dışı ölü girdi budaması (dosyası artık olmayan HER girdi) ekle ya da Load'da kök sürücüsü/klasörü yok olan girdileri at (2,5k File.Exists ≈ 10-30 ms, bir kerelik).

**Çözüm (doğrulayıcı düzeltmesi):** (1) kirli bayragi + (N5-2) akisla oku/yaz once; (2) 'surec boyu tek ornek' RAM onceligiyle catistigi icin onerilmez (bosta kalici ~24 MB). Ayrica EvaluationCache duz Dictionary kullaniyor ve kendi yorumu (84-90) iki ornegin paralel flush edebildigini soyluyor - paylasilan tek ornek kilit olmadan guvenli degil. (3) source generator ayri, olcume bagli bir is. SyncWorkspaceService.cs:152-153 'Sync hicbir sey PERSIST ETMEZ' diyor ama ayni Sync iki onbellek dosyasini yaziyor: yorum build-state'i kastediyor olabilir; dokuman/kod uyumu kullaniciya sorulmali.

**Değişmezler:** OutDir'e dokunulmaz (etkilenmez); Planlama Core'da (etkilenmez); kopya yasağı — defter yolları tek yerde (SupervisorHost.cs:40-41) kalır; 'Sync hiçbir şey persist etmez' iddiası (SyncWorkspaceService.cs:150-151) bugün zaten cache flush'ıyla çelişiyor, dirty bayrağı onu doğru hale getirir. Optimize'ın 'yalnız kök altını budar' sözleşmesi genişletilirse ARCHITECTURE §16 güncellenmeli.

**Test fikri:** Core testi: aynı fixture'da iki ardışık Sync → ikinci Sync'te evaluation-cache.json ve source-hash-cache.json mtime'ı DEĞİŞMEMELİ (bugün kırmızı). Ölçüm: [SkippableFact] + Skip.IfNot(BO_MEASURE_ROOT var) ile gerçek defterlerde Load/Flush Stopwatch'ı (mevcut ContentDecisionDiagnosticsTests kalıbı).

**Doğrulayıcı notu:** Kod aynen: SupervisorHost.cs:43-57 her Sync/Optimize'da yeni EvaluationCache + SourceHashCache + BuildStateStore; Program.cs:94, 97 her Build'de yine yeni ornek; EvaluationCache.Flush (96-104) ve SourceHashCache.Flush (148-166) 'degisti mi' bakmadan yazar; cagrilar BuildPlanBuilder.cs:43, SyncWorkspaceService.cs:324, Program.cs:201. Diskte evaluation-cache.json 5.914.117 B, source-hash-cache.json 6.257.488 B, ikisinin mtime'i son kosunun ani (02:44). Sure rakamlari (150-400 ms + 60-150 ms) TAHMIN, olculmedi. 'Tek ornek yasat' onerisi N5-2 ile celisiyor: bugun Supervisor canli heap'i 2,0 MB (olculdu); defterleri surec boyu tutmak bunu ~24 MB (turetilmis) yukseltir.

#### D8-2 — Açılış Sync'i ağ fetch'ine kilitli: `git fetch origin <branch>` bitmeden tarama/graf/karar transkripti başlamıyor; yavaş ağda 30 s'ye kadar bekleme ve Exit de bu Sync'i bekliyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) SyncWorkspaceService.cs:101, 111-114, 128-134, 142, 241-253; GitService.cs:72, 200; SyncMode.cs:49; RunViewModel.cs:2667; SupervisorHost.cs:147-158
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` satır 111-116 — `await MeasureRemoteAsync(cmd.Fetch, …)` — tarama (136) ve plan (142) bundan SONRA
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` satır 241-253 — fetch=true dalı: `git.FetchRefOnlyAsync` beklenir, Degraded ise uyarı
- `src/BuildOrchestrator.Core/Git/GitService.cs` satır 72, 200 — CommandTimeout 30 s; `git fetch origin <branch> --no-tags`
- `src/BuildOrchestrator.App/ViewModels/SyncMode.cs` satır 49 — Fetches: Manual | Appended
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2656-2667 — OnEngineReady → SyncCoreAsync(SyncMode.Appended) — açılış Sync'i fetch'li
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.Exit.cs` satır 13-15, 58-72 — Exit uçuştaki Sync'i bekler ('Sync'in fetch'i git ref'lerini yazar' — bilinçli)

**Kanıt:** SyncWorkspaceService.RunAsync'te sıra kesin seri: HEAD (101) → branch (111) → MeasureRemoteAsync/fetch (113) → HeadDistance satırı → scan (136) → plan (142) → build-state + status (158-162) → topoloji/önizleme. Fetch tamamen yerel işten bağımsız bir bilgi (behind sayısı ve chip) üretir ama yerel işin önünde durur. Exit.cs:13-15 açıkça 'Sync'in fetch'i beklenir' der; EngineSilence eşiği 90 s (RunViewModel.cs:753) daha kısa bir kurtarma sağlamaz.

**Etki:** Açılışta ilk kararların görünmesi ağ RTT + fetch süresi kadar gecikir. Ölçüm yok; tahmin: GitHub/LAN'da 0,3-1,5 s, VPN/captive portal'da 5-30 s (timeout). Aynı gecikme Exit'e de yansır: pencere kapatılır kapatılmaz süreç ölmez, fetch bitene kadar yaşar.

**Etki (doğrulayıcı düzeltmesi):** Yalniz acilis Sync'i ve elle Sync: ilk kararlar ag turu kadar (bu agda ~1,2 s, olculdu) gecikir; ag yokken 30 s'ye kadar. Asil risk baska: Sync komut dongusunu bloklar (SupervisorHost.cs:147-150), yani Windows ile tepside acilistan hemen sonra kisayolla Build, fetch bitene kadar baslamaz.

**Çözüm:** Fetch'i Task olarak ÖNCE başlat, tarama+plan+status'u hemen koştur, yerel emit'leri fetch çözülene kadar bir listeye tampona al (transkript sırası §10.2 — fetch satırı → HeadDistance → adım satırları — korunur), fetch bitince tamponu sırayla boşalt. Degraded semantiği aynen kalır. Şartlı-2: Sync'in fetch'i için daha kısa bir zaman aşımı (ör. 10 s) — 30 s CommandTimeout diğer komutlar için kalsın. Şartlı-3 (bilinçli karar Exit.cs:13-15 ile çelişir): Exit'te uçuştaki Sync'in fetch'ini ct ile iptal et — ref-only fetch iptali git tarafında atomiktir, ama bu kararın sahibi kullanıcı.

**Çözüm (doğrulayıcı düzeltmesi):** Siralamayi degistirmeden yapilabilecek tek dusuk riskli adim: Sync'in fetch'ine ayri, daha kisa bir zaman asimi. Paralellestirme ve Exit'te iptal, ARCHITECTURE §10.2 (transkript sirasi) ve Exit'in fetch'i beklemesi bilincli kararlar oldugu icin kullaniciya sorulmadan yapilmaz.

**Değişmezler:** Git'e kendiliğinden yazma yok (fetch zaten tek izin — değişmiyor); stdout NDJSON (emit sırası korunur); Planlama Core'da (değişiklik Core'da). ARCHITECTURE §10.2 transkript sırası ve §12.3 Exit bekleyişi bilinçli — şartlı maddeler için kullanıcı kararı gerekir.

**Test fikri:** SyncWorkspaceService testi: fetch'i 2 s geciktiren sahte IProcessRunner ile scan/plan emit'lerinin fetch bitmeden ÜRETİLDİĞİ (tampona alındığı) ve son transkript sırasının değişmediği pinlenir; bugün kırmızı (scan fetch'ten sonra başlar).

**Doğrulayıcı notu:** Sira kodda dogru: HEAD (101) -> branch (111) -> MeasureRemoteAsync/fetch (113-114) -> tarama (132) -> plan (142). Ama (a) etki yalniz fetch eden Sync'lerde: SyncMode.Fetches = Manual | Appended (SyncMode.cs:49); pencereye donus, commit ve kosu sonu Sync'leri Silent'tir ve aga cikmaz - birincil senaryo (tepsiden Build) bu bulgudan etkilenmez; (b) onerilen yeniden siralama bilincli bir karara dokunuyor: SyncWorkspaceService.cs:128-131 '[design v1.24.0] Tarama yeniden SIRALANMAZ (fetch'ten sonra, adim satirlari yerinde)' ve SupervisorHost.cs:152-158 'Event'ler TAMPONLANMAZ' (tamponlama eskiden ilerlemeyi gorunmez kiliyordu). Yerel isi fetch'le paralel kosturup emit'leri tamponlamak bu iki gerekceyle de catisir. Sure: bu agda uzak uca tek tur ~1,2 s olculdu (git ls-remote); 30 s zaman asimi GitService.cs:72'de dogru.

#### D8-3 — Publish çıktısı ReadyToRun değil — kurulu kopyada App + Core + Contracts + AvalonEdit + H.NotifyIcon + Mvvm + DI + Velopack her açılışta baştan JIT ediliyor; Supervisor kopyası da build çıktısından geldiği için R2R bayrağı ona kendiliğinden geçmez

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.App/BuildOrchestrator.App.csproj (R2R ozelligi yok; _ResolveSupervisorFiles 71-82, GetTargetPath); scripts/package.ps1 (publish adimi)
- `src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` satır 22-33 — PropertyGroup: PublishReadyToRun/TieredCompilation/TieredPGO yok (grep: repo'da 0 sonuç, runtimeconfig.template.json yok)
- `scripts/package.ps1` satır 112-116 — `dotnet publish … -r win-x64 --self-contained false` — RID var, FDD; R2R'ın tek önkoşulu sağlanmış ama bayrak yok
- `src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` satır 62-86, 115-125 — _ResolveSupervisorFiles Supervisor'ın BUILD çıktısını (GetTargetPath → bin\<Config>\<TFM>\) kopyalar; publish listesine de aynı küme girer
- `src/BuildOrchestrator.App/Program.cs` satır 19-26 — Main: Velopack.dll (279 KB) daha WPF kurulmadan yüklenir ve JIT edilir

**Kanıt:** Release bin listesi (salt-okur): BuildOrchestrator.App.dll 2.101.248 B (fontlar dahil), ICSharpCode.AvalonEdit 622.592, H.NotifyIcon 398.848 + Wpf 194.048, Core 337.408, Velopack 279.040, Mvvm 146.760, Contracts 119.808, MS.Extensions.DI 95.568 + 65.832 — hiçbiri R2R değil; paylaşılan framework (WPF) zaten R2R. Bu makinede kurulu kopya YOK (%LOCALAPPDATA%\BuildOrchestrator.App yok) → kullanıcı bin'den (büyük olasılıkla Debug/dotnet run) çalıştırıyor; 2026-09-14'teki 1,5 s ölçümünün konfigürasyonu raporda yazmıyor.

**Etki:** Kurulu kopyanın açılışında yönetilen kodun tier-0 JIT'i; tahmin: WPF uygulamalarında bu ölçekte 200-500 ms (toplam 1,5 s'nin kayda değer dilimi). Debug bin'den koşan kopyada R2R hiç devreye girmez — orada tek çözüm Release ile koşmak.

**Etki (doğrulayıcı düzeltmesi):** Olculen acilis (MainWindowHandle, 100 ms adimli yoklama, Release bin): 832 ms (sicak), 1.334 ms, 1.704 ms (derlemeden sonraki ilk acilis). R2R kazanci olculmedi; ust siniri sicak acilisin kendisi (832 ms) ve bunun icinde WPF cercevesi zaten R2R.

**Çözüm:** App.csproj PropertyGroup'una `<PublishReadyToRun>true</PublishReadyToRun>` (publish zaten RID'li; crossgen2 paketi publish'te iner, CI runner'ı internetli). Supervisor için: ya `_ResolveSupervisorFiles` yerine publish akışında Supervisor'ın `Publish` target'ını (PublishDir=…;RuntimeIdentifier=win-x64;SelfContained=false;PublishReadyToRun=true) çağırıp o klasörü supervisor\ olarak paketle, ya da motoru IL bırak (etkilediği tek şey engineReady gecikmesi — önce ölç). Paket boyutu tahminen +2-4 MB; delta paketler bunu emer. TieredCompilation/TieredPGO varsayılanda kalsın.

**Çözüm (doğrulayıcı düzeltmesi):** Once olc: ayni publish ciktisini DOTNET_ReadyToRun=0/1 ile ve zayif makinede (ya da 2 islemci yakinligiyla) karsilastir. Kazanc gosterilmeden bayrak eklenmemeli. Supervisor'i publish hedefinden almak ARCHITECTURE'daki paketleme anlatisini degistirir - ayri karar.

**Değişmezler:** Velopack yalnız App'te (değişmez); shell-out (değişmez); tek-dosya publish reddi (_RejectSingleFilePublish) korunur — R2R klasör publish ile uyumlu. Supervisor'ı publish çıktısından almak ARCHITECTURE §18'deki 'GetTargetPath'ten çözülür' anlatısını değiştirir → doküman aynı işte güncellenmeli.

**Test fikri:** Kaynak guard'ı PublishLayoutTests deseniyle (App.csproj `PublishReadyToRun` içermiyorsa kırmızı). Ölçüm: BO_MEASURE_STARTUP=1 kapılı test — aynı publish çıktısını `DOTNET_ReadyToRun=1/0` ile 10'ar kez başlat, MainWindowHandle ve ContentRendered'a kadar medyanı yaz.

**Doğrulayıcı notu:** Olgu dogru: repo'da PublishReadyToRun / TieredCompilation / TieredPGO yok (csproj, props, ps1 taramasi bos); package.ps1 publish'i RID'li ve framework-dependent. Ama etki iddiasi olcumle desteklenmiyor: 'toplam 1,5 s'nin kayda deger dilimi (200-500 ms)' TAHMIN. Yeni olcumler Release bin'de (R2R'siz ayni ikili) pencere tutamacina kadar 832 ms, 1.334 ms ve 1.704 ms verdi - ayni ikilinin uc acilisi arasinda 870 ms fark var; bu fark JIT degil, soguk/sicak disk ve sistem durumu. R2R goruntuleri buyuttugu icin soguk diskte ters yone de calisabilir. 'Kullanici buyuk olasilikla Debug'dan calistiriyor' cumlesi spekulasyon; olcumler Release'de alindi.

#### D8-4 — Motor spawn'ı ilk kare çizildikten SONRA (Loaded) başlıyor — Supervisor'ın runtime açılışı ve JIT'i App'in kendi kurulumuyla örtüştürülmüyor; 'Engine ready' ve açılış Sync'i seri toplam kadar geç geliyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) MainWindow.xaml.cs:388-394, 949-956, 1341; EngineHost.cs:105-153; App.xaml.cs:141-149
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 388-394 — `Loaded += async … await StartEngineAsync()` — spawn Loaded'a bağlı
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 949-956 — StartEngineAsync → _engine.StartAsync → OnEngineReady
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 126-158 — StartAsync: CreateProcessW (senkron, UI thread) + engineReady bekleyişi (5 s)
- `src/BuildOrchestrator.App/App.xaml.cs` satır 141-149 — MainWindow ctor'u burada biter (tüm engine abonelikleri ctor'da: MainWindow.xaml.cs:331-346), sonra Show/StartInTray
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1341 — StartInTray yalnız EnsureHandle — Loaded'ın bu yolda hiç ateşlenip ateşlenmediği belirsiz (bkz. open question)

**Kanıt:** Zaman çizgisi koddan: Main → OnStartup (DI, registry, MainWindow ctor: XAML + 4 overlay + graf/liste kurulumları) → Show → layout/render → Loaded → ANCAK ŞİMDİ CreateProcessW → Supervisor runtime init + JIT (Program.cs:19-70) → engineReady → Sync. 2026-09-14 raporu 'Supervisor pencereyle aynı anda' diyor ama bu, HWND anı; engineReady'nin ne zaman geldiği ölçülmemiş. Supervisor'ın açılışı hafif (D8 clean_areas) ama kendi .NET runtime init'i + JIT'i (tahmin 150-300 ms) App'in ilk karesinden SONRA seri koşuyor.

**Etki:** Kullanıcının beklediği asıl şey (kararlar/graf) 'Engine ready' + Sync'e bağlı; motorun başlangıcı App'in kurulumuyla örtüşseydi ilk Sync tahminen 150-300 ms erken başlardı. 8 thread'li makinede ilk kare çizimine CPU yarışı etkisi muhtemelen küçük ama ölçülmeli.

**Etki (doğrulayıcı düzeltmesi):** Olculen: Supervisor sureci pencere tutamacindan ~45 ms sonra gorunuyor; engineReady ani olculmedi. Kazanc ust siniri Show -> Loaded arasi (olculmedi).

**Çözüm:** App.xaml.cs:141'de pencere kurulduktan (abonelikler hazır) sonra, Show'dan ÖNCE `_ = window.StartEngineAsync()` (metot internal yapılır; Loaded'daki çağrı kaldırılır; ShowReady guard'ı `GetActiveLineCount()==0` zaten 'Engine ready' satırı erken gelirse placeholder'ı atlar). Aynı değişiklik StartInTray yolunu da kapsar (Loaded'a bağımlılık kalkar). Önce ölç: ContentRendered'a kadar süre değişiyor mu (4P+4E çekirdek).

**Çözüm (doğrulayıcı düzeltmesi):** Zamanlama degisikligi yerine once D8-12 zaman cizgisi. Ayri ve daha onemli is: StartInTray yolunun (yalniz EnsureHandle) motoru gercekten baslattigini pinleyen bir test - bkz. missing.

**Değişmezler:** Nested Job korunur (EngineHost aynen); stdout NDJSON aynen; planlama Core'da. ARCHITECTURE §12.1 'The update engine starts only once the window has been shown' cümlesi motor için değil güncelleme için — motor spawn anı için doküman 'Loaded' demiyor, ama §4.3/§12.1 anlatısı yerinde güncellenmeli.

**Test fikri:** BO_TRACE_STARTUP kapılı zaman çizgisi (D8-12) ile 'Show→engineReady' ve 'Main→ContentRendered' iki değişkenin önce/sonra değerleri; realize testi: engine hazır olmadan Loaded gelirse ShowReady'nin görünmediği pinlenir.

**Doğrulayıcı notu:** Konum dogru (MainWindow.xaml.cs:388-394 Loaded -> StartEngineAsync 949-956), ama 'ilk kare cizildikten SONRA' ifadesi yanlis: WPF Loaded'i yerlesimden sonra, ilk cizimden ONCE yukseltir; kaybedilen sure en cok ctor sonu ile Loaded arasi (Show + ilk yerlesim) kadardir. Olcum de kucuk bir aralik gosteriyor: 2026-10-01'de pencere tutamaci 1.334 ms'de, Supervisor sureci 1.379 ms'de goruldu (100 ms adimli yoklama). '150-300 ms erken Sync' TAHMIN. Motoru Show'dan once baslatmak UI thread'inde senkron CreateProcess'i (EngineHost.cs:119, JobProcessLauncher.Launch) ilk karenin ONUNE alir - zayif makinede ilk kareyi geciktirebilir; yani oneri kullanicinin 'UI donmasin' oncelligine ters yone de calisabilir.

#### D8-5 — Tepsi ikonu ve balloon için System.Drawing (GDI+) ilk kare çizilmeden ÖNCE, UI thread'inde, OnSourceInitialized'da kuruluyor; balloon ikonu hiç balloon gösterilmese de eager

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) Shell/AppTrayIcon.cs:47, 56-74, 154, 172; MainWindow.xaml.cs:1180-1199
- `src/BuildOrchestrator.App/Shell/AppTrayIcon.cs` satır 56-74 — ctor: `_largeIcon = LoadBalloonIcon()` (58) → System.Drawing.Icon (108, GDI+ init) + `_icon.ForceCreate(false)` (74) → Shell_NotifyIcon NIM_ADD senkron explorer RPC
- `src/BuildOrchestrator.App/Shell/AppTrayIcon.cs` satır 154 — _largeIcon'un tek tüketicisi: bildirim gösterilirken customIconHandle
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1180-1199 — OnSourceInitialized: DWM ×3 + `_tray = new AppTrayIcon(...)` + SetUpTrayBuildIndicator — HWND doğduğu an, ilk boyamadan önce

**Kanıt:** AppTrayIcon.cs:58 balloon ikonunu ctor'da çözer ve 154 dışında hiçbir yer okumaz; System.Drawing.Icon GDI+'ı bu process'te ilk kez başlatır (gdiplus.dll + GdiplusStartup). ForceCreate NIM_ADD'i explorer'a senkron gönderir; explorer meşgulken (oturum açılışı, --autostart) bu çağrının 4 s'ye kadar bekleyebildiği Win32 belgelidir. Hepsi ShowWindow yolunda ilk kareden önce UI thread'inde.

**Etki:** Normal açılışta tahmin 10-40 ms (GDI+ init + NIM_ADD); oturum açılışında autostart ile çok daha uzun sürebilir. Ölçülmedi.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. Yalniz (1) Lazy balloon ikonu risksiz; (2) tepsi kurulumunu ertelemek global kisayol kaydiyla ayni metotta oldugu icin siralama riski tasir.

**Çözüm:** (1) `_largeIcon` → Lazy<System.Drawing.Icon>, ilk ShowNotification'da çözülsün (Dispose'da IsValueCreated ise bırak). (2) ShowWindow yolunda tepsi kurulumunu ContentRendered/ContextIdle'a ertele; StartInTray yolunda (1341) tepsi tek yüzey olduğundan senkron kalsın. TrayBalloonIconTests'in 32px pini etkilenmez.

**Çözüm (doğrulayıcı düzeltmesi):** Yalniz madde (1). Madde (2) olcum gosterene kadar yapilmasin: tepsi ikonu ve gosterge kurulumu (SetUpTrayBuildIndicator) birincil senaryonun yuzeyi.

**Değişmezler:** Tepsi davranışı (K5, K-12 gösterge) değişmez; yalnız yaratılma anı kayar. StartInTray → OnSourceInitialized → tepsi bağı korunur.

**Test fikri:** BO_PROBE_TRAY kapılı mevcut probe kalıbıyla: pencere Show → ContentRendered arası Stopwatch, tepsi erken/geç kurulumu A/B.

**Doğrulayıcı notu:** Kod dogru: AppTrayIcon ctor (56-74) _largeIcon = LoadBalloonIcon() ile ikonu hemen cozer; _largeIcon'un tek tuketicisi satir 154 (bildirim) ve 172 (Dispose); ForceCreate(false) satir 73. Kurulum OnSourceInitialized'da (MainWindow.xaml.cs:1180-1199). Sure (10-40 ms) TAHMIN; explorer mesgulken uzun bekleme iddiasi bu makinede gozlenmedi.

#### D8-6 — ui-state.json açılışta 5 kez okunup deserialize ediliyor; aynı karar (HasUnseenNotes) art arda iki kez diskten hesaplanıyor; her kapatma/yerleşim/tercih değişimi de Load+Save

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) App.xaml.cs:62; MainWindow.xaml.cs:108, 152, 478, 498, 505, 516, 1141, 1160, 1202, 1232, 1399; Shell/UiStateStore.cs:183-194
- `src/BuildOrchestrator.App/App.xaml.cs` satır 62 — 1. okuma — yalnız rota kararı (StartMinimizedToTray) ve Autostart için; store örneği MainWindow'a geçirilmiyor (DI `AddSingleton<MainWindow>` → ctor uiState=null → 108'de ikinci store)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 152 — 2. okuma — yerleşim + seed
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 311 → 505 → 498 ve 311 → 498 — RefreshUnseenNotesMark: HasUnseenNotes (498, Load) + SetupNotesButtonTooltip (478 → 498, Load) — 3. ve 4. okuma, aynı karar iki kez
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1202 — OnSourceInitialized hotkey jestleri — 5. okuma
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1141, 1160, 1399 — her yerleşim değişimi ve tercih değişimi Load→muta→Save; her × kapatması Load
- `src/BuildOrchestrator.App/Shell/UiStateStore.cs` satır 183-194 — Load: File.Exists + ReadAllText + JsonSerializer.Deserialize<UiState> (reflection) her çağrıda

**Kanıt:** Sayım grep ile: `_uiState.Load()` çağrıları 152, 498, 516, 1141, 1160, 1202, 1232 (closure), 1399; ctor sırasında 152 + 498×2 (311→505→478) ve 1202, App'te 62 → açılışta 5. Dosya 1.124 B.

**Etki:** Küçük: sıcak okuma <1 ms; ilk okuma STJ reflection ısınması (tahmin 10-30 ms) zaten bir kez ödenir. Asıl sorun organizasyon: aynı gerçek 5 yerden diskten sorulur, yazan yollar birbirinin yazdığını okumaya güvenir.

**Etki (doğrulayıcı düzeltmesi):** Performans etkisi olculmedi ve beklenmez; deger 'tek dogruluk kaynagi' kuralinda.

**Çözüm:** App.xaml.cs:62'deki store örneğini DI'a IUiStateStore olarak kaydet ve MainWindow ctor parametresine geçir (bugün DI null geçiyor → ikinci store); ctor'daki `saved` bir alan (`_state`) olsun, HasUnseenNotes ve OnSourceInitialized o alanı okusun; RefreshUnseenNotesMark kararı bir kez hesaplayıp SetupNotesButtonTooltip(bool unseen)'e geçirsin; persist yolları (1141/1160/516) alanı güncelleyip Save etsin. 'Close to tray her kapatmada TAZE okunur' (1397-1399) kuralı bilinçli — Settings Save aynı alanı güncelliyorsa taze okuma alan üzerinden de sağlanır.

**Çözüm (doğrulayıcı düzeltmesi):** Satir 1399 ve 1232'deki 'her seferinde TAZE oku' bilincli (yorumda yazili); alan onbellegine gecilirse Settings Save ayni alani guncellemeli, yoksa kural bozulur.

**Değişmezler:** Kopya yasağı: tek store örneği (bugün iki: App.xaml.cs:62 ve MainWindow:108). Kalıcı durumun 'her koşulda bir durum döndürür' sözleşmesi korunur.

**Test fikri:** Sayaçlı fake IUiStateStore ile MainWindowHost realize testi: ctor + SourceInitialized boyunca Load çağrısı ≤1 (bugün 4).

**Doğrulayıcı notu:** Sayim dogru: App.xaml.cs:62 (1) + MainWindow ctor 152 (2) + RefreshUnseenNotesMark icinde HasUnseenNotes iki kez (498; biri 505'ten, biri SetupNotesButtonTooltip 478'den) (3, 4) + OnSourceInitialized 1202 (5). Iki ayri store ornegi de dogru (App.xaml.cs:62 ve MainWindow.xaml.cs:108). UiStateStore.Load her cagrida ReadAllText + Deserialize (183-194). Dosya 1.124 B; maliyet onemsiz - organizasyon bulgusu.

#### D8-7 — HKCU\…\Run autostart değeri her açılışta koşulsuz yazılıyor/siliniyor — seam'deki Exists hiç okunmuyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) Services/AutostartService.cs:96, 134-138; App.xaml.cs:139
- `src/BuildOrchestrator.App/App.xaml.cs` satır 139 — `AutostartService.Apply(ShellSwitches.StartWithWindows(uiState))` her açılışta, pencereden önce
- `src/BuildOrchestrator.App/Services/AutostartService.cs` satır 79, 118-122 — Apply → WriteRunValue: on ise Set (CreateSubKey + SetValue), değilse Remove (OpenSubKey writable + DeleteValue) — okuma yok
- `src/BuildOrchestrator.App/Services/AutostartService.cs` satır 45-64 — RegistryAutostartRegistry.Set/Remove/Exists — Exists Apply'da kullanılmıyor

**Kanıt:** Kullanıcının ui-state.json'ında Autostart=false → her açılış HKCU\Software\Microsoft\Windows\CurrentVersion\Run'ı yazılabilir açıp var olmayan değeri silmeye çalışıyor. Yorum 'idempotent — her açılışta güvenli' der (76-78); güvenli ama gereksiz yazma-modu erişimi.

**Etki:** <1 ms; işlevsel değil, düzen sorunu (registry'ye yazma amaçlı erişim + hive kirlenmesi yalnız fark varken olmalı).

**Çözüm:** Apply önce okusun (Exists + değer karşılaştırması için seam'e `string? Get(name)` ekle) ve yalnız tercih ile kayıt ayrışıyorsa (yok/var ya da exe yolu değişmiş) yazsın/silsin. AutostartServiceTests'in in-memory fake'i bunu doğrudan taşır.

**Değişmezler:** Windows başlangıç kaydının tek sahibi AutostartService kalır (§12.3); 'exe taşındıysa yeni yola hizala' davranışı korunur (değer karşılaştırması bunu yakalar).

**Test fikri:** Fake registry sayaçlı: kayıt tercihle eşitken Apply hiçbir Set/Remove çağırmaz (bugün kırmızı).

**Doğrulayıcı notu:** AutostartService.Apply (96) -> WriteRunValue (134-138): acikken Set, kapaliyken Remove; okuma yok. Cagri App.xaml.cs:139, her acilista. Maliyet onemsiz; duzen bulgusu.

#### D8-8 — Single-instance oturum id'si için Process.GetCurrentProcess().SessionId — tüm sistemin süreç tablosunu çeken NtQuerySystemInformation yolu, DI'dan önce UI thread'inde

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) Shell/SingleInstance.cs:60-64, 75-79; App.xaml.cs:77
- `src/BuildOrchestrator.App/Shell/SingleInstance.cs` satır 75-79 — CurrentSessionId: `Process.GetCurrentProcess().SessionId` — .NET'te SessionId, HaveProcessInfo için tüm süreç tablosunu okur ve pid'e göre süzer
- `src/BuildOrchestrator.App/Shell/SingleInstance.cs` satır 60-64 — Acquire her çağrıda BuildPipeName(key, CurrentSessionId())
- `src/BuildOrchestrator.App/App.xaml.cs` satır 77 — Acquire OnStartup'ta, DI ve pencere kurulmadan önce senkron

**Kanıt:** Kod aynen: `using var current = Process.GetCurrentProcess(); return current.SessionId;`. .NET'in Windows ProcessManager'ı SessionId'yi tek süreç için de sistem-geneli SystemProcessInformation tamponuyla doldurur (süzme sonradan). Shell/Win32.cs'te kernel32 P/Invoke yok.

**Etki:** Birkaç ms + birkaç yüz KB geçici tampon (tahmin), bir kerelik. Kozmetik.

**Çözüm:** Shell/Win32.cs'e `ProcessIdToSessionId(uint pid, out uint sessionId)` (kernel32) ekle; CurrentSessionId `Environment.ProcessId` ile onu çağırsın, başarısızlıkta 0'a düşsün. Pipe adı sözleşmesi (BuildPipeName, 73) ve SingleInstanceTests değişmez.

**Değişmezler:** Oturum-yerel pipe adı (I-1 fix) korunur.

**Test fikri:** Mevcut SingleInstanceTests pipe adı testi aynen geçer; ek: sessionId>0 ve Process.SessionId ile eşit.

**Doğrulayıcı notu:** SingleInstance.cs:75-79 aynen Process.GetCurrentProcess().SessionId; Acquire (60-64) her cagrida kullanir; App.xaml.cs:77'de DI'dan once. Maliyet (birkac ms) TAHMIN, olculmedi; bir kerelik.

#### D8-9 — Dört overlay diyalog (Settings 404 satır XAML, About 286, Notes 89, UpdateRestart 48) açılışta Collapsed hâlde eager kuruluyor — tepsi overlay'inin lazy deseni bunlara uygulanmamış

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) MainWindow.xaml:271-293; MainWindow.xaml.cs:1250-1256 (mevcut lazy desen)
- `src/BuildOrchestrator.App/MainWindow.xaml` satır 271-293 — SettingsOverlay / AboutOverlay / NotesOverlay / UpdateRestartOverlay — hepsi MainWindow InitializeComponent'inde örneklenir
- `src/BuildOrchestrator.App/Views/SettingsDialog.xaml.cs` satır 57-63 — ctor: InitializeComponent + timer
- `src/BuildOrchestrator.App/Views/AboutDialog.xaml.cs` satır 64-71 — ctor: InitializeComponent + WhatsNewInLabel (ReleaseNotes parse'ı Lazy — ReleaseNotes.cs:111, tetiklemez)
- `src/BuildOrchestrator.App/Views/NotesDialog.xaml.cs` satır 41-45 — ctor: InitializeComponent + AddBlockColumns
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1250-1256 — mevcut lazy desen: EnsureTrayOverlay (ilk gösterimde yarat)

**Kanıt:** Collapsed öğede şablon uygulanmaz (Measure yok) ama BAML yükleme + nesne ağacı (Settings 404 satır) ctor'da kurulur; wc -l ile boyutlar. Tepsi overlay'i için aynı sorun lazy çözülmüş (1250-1256), diyaloglar için uygulanmamış.

**Etki:** Tahmin 10-40 ms toplam (ölçülmedi); bellekte kullanılmayan dört görsel ağaç.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi; acilistaki canli managed heap'in tamami 15,5 MB.

**Çözüm:** EnsureSettingsOverlay/EnsureAboutOverlay/EnsureNotesOverlay (EnsureTrayOverlay deseni) ilk Open'da kurup kök Grid'e Grid.RowSpan=2 ile eklesin; AnyDialogOpen ve Esc zinciri null-güvenli okusun. ÖNCE ölç: InitializeComponent süresi dört öğe yorumdayken/varken. Realize testleri diyalogları açarak XAML kapsamını korumalı (lazy yapmak launch-fatal XAML hatalarını testsiz bırakmasın).

**Çözüm (doğrulayıcı düzeltmesi):** Olcum (InitializeComponent suresi) kazanc gostermeden yapilmasin.

**Değişmezler:** Realize testi kuralı (CLAUDE.md): yeni XAML kökü/şablon yok ama görünürlük anı değişiyor → mevcut realize testleri diyalog açılışını kapsamalı. Kopya yasağı: tek lazy yardımcı.

**Test fikri:** Stopwatch ile MainWindowHost realize: ctor süresi; lazy sonrası Settings/About/Notes Open() realize testleri yeşil.

**Doğrulayıcı notu:** Olgu dogru (MainWindow.xaml:271-293 dort overlay InitializeComponent'te kurulur) ama kazanc (10-40 ms) TAHMIN ve risk 'orta': AnyDialogOpen/Esc zinciri ve realize testleri degisir. Olculen acilis 832-1.704 ms araliginda oynarken 10-40 ms'lik bir kalem ayirt edilemez. Bellek etkisi: acilista App canli heap toplam 15,5 MB (olculdu) - dort Collapsed agacin payi bunun kucuk bir kismi.

#### D8-10 — Çıkışta 'graceful' ShutdownCommand nominal: komut yazılır yazılmaz Kill(entireProcessTree) geliyor — Supervisor kendi düzenli çıkış yolunu (return 0 → coordinator/innerJob dispose) hiç koşamıyor; hız zaten iyi, temizlik değil

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) EngineHost.cs:203-213, 229-234, 246-250, 252-266, 268-272; SupervisorHost.cs:101, 113-114
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 203-212 — ShutdownGracefullyAsync: WriteAsync(ShutdownCommand).WaitAsync(500 ms) → hemen KillCurrent()
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 236-262 — KillTree = Kill(entireProcessTree:true) (tüm süreçleri numaralandırır) + WaitForExit(1 s); Process.GetProcessById de süreç listesini tarar
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 101-104, 113 — ShutdownCommand → _running=false → return 0; stdin EOF → return 0 — bu yol pratikte öldürme yarışını kaybeder
- `src/BuildOrchestrator.App/Shell/AppShutdown.cs` satır 21, 28-42 — OnExit bekleyişi ≤2 s, Task.Run + Wait (dispatcher deadlock yok)

**Kanıt:** Sıra kodda kesin: yazma tamamlanınca (pipe'a düştüğü an) KillCurrent. Supervisor komutu okuyup `return 0`'a gelmeden TerminateProcess iner; `using coordinator`/`using innerJob` (Program.cs:38-60) dispose'ları normal Exit'te hiç koşmaz. Zarar yok: Exit WorkspaceIdle bekler (Exit.cs:58-66), MSBuild child'ı yok, outer job KILL_ON_JOB_CLOSE süpürür (EngineHost.cs:271). 2026-09-14 ölçümü: App Stop-Process → Supervisor 84 ms'de öldü, artık süreç yok.

**Etki:** Süre açısından sorun yok (≈100 ms mertebesi, 2 s bütçenin çok altında). Kozmetik: 'graceful' adı yanıltıcı; Kill(entireProcessTree) her Exit'te tüm süreç tablosunu tarar (tahmin 10-50 ms) — yalın exit'te gereksiz.

**Etki (doğrulayıcı düzeltmesi):** Kapanis kaskadi 77-139 ms (olculdu, Stop-Process yolu); Exit menusu yolu ayrica olculmedi.

**Çözüm:** ShutdownCommand'dan sonra Supervisor'ın çıkışını kısa bir süre (ör. ≤300 ms) bekle (WaitForExitAsync), çıkmadıysa Kill; toplam bütçe 2 s içinde kalır. Kazanç hız değil düzen (RunLogWriter/decision.log dispose, sürec tarama yok). Alternatif: hiç değiştirme, yalnız EngineHost.cs:209 yorumundaki 'graceful'ı 'best-effort bildirim' diye düzelt.

**Çözüm (doğrulayıcı düzeltmesi):** Bekleme eklenirse KillExitWait (1 s, EngineHostTests ile literal pinli) ve §4.4 matrisi ayni iste guncellenmeli. Oncelik dusuk; yapilmasa da olur.

**Değişmezler:** §4.4 termination matrisi (App→Supervisor: Kill + ≤1 s bekleme) — bekleme eklenirse tablo yerinde güncellenir; KillExitWait=1 s ve DisposalTimeout=2 s literal pinleri (EngineHostTests) korunur.

**Test fikri:** SafeExitProcessTests zaten Stopwatch SinceExit tutuyor (183) — ExitNow→Supervisor exit süresini rapora yaz ve ≤2 s pinle; graceful bekleme eklenirse Supervisor'ın exit code 0 ile çıktığı gözlenir.

**Doğrulayıcı notu:** EngineHost.cs:203-213: ShutdownCommand yazimi (<=500 ms) ardindan kosulsuz KillCurrent; KillTree = Kill(entireProcessTree: true) (234), KillAndAwaitExit <=1 s (246-250). Supervisor ShutdownCommand'da _running=false ile duzenli cikar (SupervisorHost.cs:113-114) ama App beklemez. Yeni olcum zararsizligi dogruluyor: App oldurulunce Supervisor 77, 113, 139, 113 ms'de gitti, 3 s sonra artik surec yok. D3-6 ile ayni bulgu.

#### D8-11 — Açılış Sync'i 5 ayrı git.exe spawn'ı seri koşturuyor (+ aynı anda listBranches komutu) — HEAD/branch/status/behind tek-iki çağrıda alınabilir

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) SyncWorkspaceService.cs:101, 111, 162, 244, 257, 262; GitService.cs:200-206; RunViewModel.cs:1417
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` satır 101, 111, 162, 244, 262 — rev-parse HEAD → branch → (fetch) → count behind → status --porcelain: her biri ayrı process
- `src/BuildOrchestrator.Core/Processes/ProcessRunner.cs` satır 36-51 — her çağrı Process.Start + iki ReadToEndAsync + WaitForExitAsync
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1417 — her Sync'te ayrıca ListBranchesCommand → SupervisorHost.cs:330 → ek git spawn(lar)ı

**Kanıt:** Sıra ve sayı kodda kesin; spawn başına maliyet ölçülmedi (Windows'ta git.exe soğuk 40-80 ms, sıcak 15-30 ms tahmin); `git status --porcelain` 177 projelik ağaçta muhtemelen en pahalısı (untracked tarama) ve gerekli (LocalEdits).

**Etki:** Tahmin 100-250 ms/Sync spawn ek yükü; açılışta fetch'in arkasında seri.

**Etki (doğrulayıcı düzeltmesi):** Sync basina 5-6 seri git.exe + listBranches (kodda kesin); sure olculmedi. Ayni desen Build planlamasinda da var (bkz. missing).

**Çözüm:** `git rev-parse HEAD --abbrev-ref HEAD` (iki satır, tek spawn) ve `git status --porcelain=v1 --branch` (branch + kir + upstream varsa ahead/behind tek spawn → CountBehind'ı da karşılar) → 5 spawn'dan 2-3'e. Parser'lar GitService'te kalır; K1 (salt-okur) aynen.

**Çözüm (doğrulayıcı düzeltmesi):** 'status --branch ile CountBehind'i da karsila' onerisi her durumda dogru degil: status'un ahead/behind'i branch'in UPSTREAM'ine goredir, kod ise fetch'in getirdigi origin/<branch> ucuna (targetSha) gore sayar; upstream tanimsizsa ya da farkliysa sayi degisir. Guvenli birlestirme yalniz 'rev-parse HEAD + branch' cifti icin.

**Değişmezler:** Git salt-okur (fetch dışında) — değişmiyor; GitResult 'exception yok' sözleşmesi korunur.

**Test fikri:** GitService testleri: sahte IProcessRunner ile bir Sync'te kaç git komutu koştuğu sayılır (bugün 5+), hedef ≤3.

**Doğrulayıcı notu:** Sira kodda dogru; sayi bulgudakinden bir fazla: fetch'li Sync'te rev-parse HEAD (101), branch (111), fetch (244) + ardindan uzak ref okumasi (GitService.FetchRefOnlyAsync icinde GetRemoteTrackingShaAsync), behind sayimi (262), status --porcelain (162) = 6 git sureci; fetch'siz Sync'te 5. Ek olarak her Sync'te ListBranchesCommand (RunViewModel.cs:1417). Surec basina maliyet TAHMIN, olculmedi.

#### D8-12 — Açılış/kapanış için kapılı bir zaman çizgisi ölçümü yok — eldeki tek sayı (1,5 s, MainWindowHandle) HWND anını ölçüyor, ilk kareyi/engineReady'yi/ilk Sync'i değil; Exit menüsü yolu hiç ölçülmemiş

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `.claude/outputs/2026-09-14-19-56-focused-performance-pass-report.md` satır 29 — 'Açılış (MainWindowHandle) 1,5 s; Supervisor pencereyle aynı anda' — konfigürasyon yazılmamış
- `scripts/verify-publish.ps1` satır 272-283, 294, 350-355 — WMI ile Supervisor spawn olayı, WaitForInputIdle, kill kaskadı — zamanlayıcı yok; Exit menüsü yolu değil kill
- `tests/BuildOrchestrator.Tests/App/SafeExitProcessTests.cs` satır 183, 193 — ExitOutcome.SinceExit Stopwatch var, süre raporlanmıyor

**Kanıt:** Repo'da açılış aşamalarını damgalayan bir kapı (BO_*) yok (mevcut kapılar: BO_MEASURE_PERF/OVERLAY/ROOT/COLD_ROOT, BO_PROBE_TRAY/FILE_DIALOG, BO_CACHE_ROOT). App'in stderr'i kimseye gitmiyor; Supervisor stderr'i App'te atılıyor (EngineHost.DrainEngineStderrAsync).

**Etki:** D8-3/D8-4/D8-5/D8-9'un değip değmeyeceği ancak bu ölçümle söylenebilir; bugün hepsi tahmin.

**Etki (doğrulayıcı düzeltmesi):** Eldeki olcumler: pencere tutamaci 832-1.704 ms (Release bin, 100 ms adim); Supervisor sureci +~45 ms; kapanis kaskadi 77-139 ms. engineReady ve ilk Sync suresi yok.

**Çözüm:** BO_TRACE_STARTUP=1 kapısı: App, Process.StartTime'a göre şu damgaları `%LOCALAPPDATA%\BuildOrchestrator\logs\startup-trace.log`'a yazsın — Main girişi, OnStartup sonu, MainWindow ctor sonu, OnSourceInitialized, ContentRendered, engineReady (OnEngineReady), ilk syncCompleted; Exit için ExitNow, OnExit giriş/çıkış, Supervisor WaitForExit dönüşü. Dış gözlem: verify-publish.ps1'e Start-Process → Supervisor spawn WMI olayı → UIA 'Engine ready' satırı arası süreler ve __InstanceDeletionEvent ile Exit sonrası iki sürecin yok olma süresi (Exit tetikleyicisi: CloseToTray=false ile WM_CLOSE ya da tepsi menüsü UIA). 10 tekrar medyan; Release publish çıktısında.

**Çözüm (doğrulayıcı düzeltmesi):** Ayni kapili zaman cizgisine KOSU baslangici da eklenmeli (tus -> koreografi sonu -> startRun gonderimi -> her planlama adimi -> runStarted): olculen 5,6-7,3 s'nin dagilimi bugun yalniz koddan turetilebiliyor. decision.log planlama bittikten sonra aciliyor (RunCoordinator.cs:724-727), bu yuzden planlama adimlarinin zamani hicbir dosyada yok.

**Değişmezler:** Ölçüm testleri ortam değişkeni kapılı ve §17.5 listesine eklenir (CLAUDE.md kuralı); stdout NDJSON'a dokunulmaz (App tarafı dosyaya, Supervisor tarafı stderr'e).

**Test fikri:** [SkippableFact] + Skip.IfNot(BO_MEASURE_STARTUP == "1"): publish çıktısını N kez başlatıp damgaları toplayan test; sonuç dosyaya (BO_MEASURE_OUT kalıbı).

**Doğrulayıcı notu:** Kaynakta acilis asamalarini damgalayan bir kapi hala yok (BO_TRACE_STARTUP / BO_MEASURE_STARTUP grep'i bos). Yeni olcumler de yalniz pencere tutamacini olcuyor (832 / 1.334 / 1.704 ms) - engineReady, ilk kare ve ilk syncCompleted anlari yok. Bulgudaki '1,5 s' eski sayi; guncel degerler bunlar.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.App/MainWindow.xaml.cs:388-394 ve 1341 - motor yalniz Loaded'da baslatiliyor; StartInTray ise yalniz EnsureHandle cagiriyor. Bu yolda Loaded'in yukselip yukselmedigini pinleyen bir test yok (StartupPathTests yalniz tepsi ikonunu ve rota kararini pinliyor). Yukselmiyorsa Windows ile tepside acilan kopyada motor hic baslamaz ve kisayolla Build calismaz; bulgu bunu yalniz acik soru olarak birakmis. Canli dogrulama gerekir.
- src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:998-1003 + Controls/MarkingChoreography.cs:62-84 + Services/OperationChoreographer.cs:89 - startRun komutu acilis koreografisi bitene kadar gonderilmiyor; sure sabitlerden 2.440 ms (tek satir) ile ~3.560 ms (11+ satir) arasi ve kapi yalniz reduced-motion/bos kapsam: pencere tepsideyken de bekleniyor. Olculen 5,68 s'lik tepsi Build gecikmesinin en buyuk kalemi; acilis-kapanis boyutunda hic gecmiyor (N1'de var).
- src/BuildOrchestrator.Supervisor/Program.cs:88-90 + Core/Git/RepositoryWriter.cs:90-141 - her Build/Rebuild'in ilk adimi, harici kok basina SERI ve taramadan once: status -> branch -> fetch (ag, 30 s zaman asimi) -> uzak ref -> rev-parse; iptal belirteci gecirilmiyor. Ag turu bu makinede ~1,2 s olculdu; D8-2 yalniz Sync'in fetch'ini ele almis, asil sik yolu (Build) kacirmis.
- src/BuildOrchestrator.Supervisor/SupervisorHost.cs:147-150 + RunViewModel.cs:2667 - acilis Sync'i (fetch'li) komut dongusunu blokluyor; acilistan hemen sonra gelen startRun, fetch dahil Sync bitene kadar bekler. Ag yokken bu 30 s'ye kadar cikabilir (GitService.cs:72).
- src/BuildOrchestrator.Supervisor/RunCoordinator.cs:724-727 - run klasoru ve decision.log planlama BITTIKTEN sonra aciliyor; planlama adimlarinin (harici guncelleme, defter yukleme, tarama, incremental) sureleri hicbir yere yazilmiyor. 'Kisayoldan kosuya 5,6-7,3 s' olcumunun dagilimi bu yuzden diskten okunamiyor.

**Temiz bulunan alanlar:**
- İkinci-instance ve --font-ab rotaları DI/motor kurulmadan döner (App.xaml.cs:68-109); normal açılışta VelopackApp.Run() no-op (Program.cs:19-22).
- Supervisor spawn'ı hafif: Program.cs:19-70 yalnız run-inflight.json (458 B) okur, build-state'i ancak kurtarma varsa yazar, engineReady'yi hemen yazar (SupervisorHost.cs:84-93); MSBuild/vswhere çözümü lazy ve memoize (Program.cs ResolveMsBuildAsync) — ilk Build'e kadar hiç koşmaz. Ortak bağlamdaki '2 cache JSON parse 12 MB spawn'da' iddiası kodda doğrulanmadı: parse ilk Sync'te (bkz. D8-1).
- Kapanış kaskadı düzenli: OnClosed hotkey/tepsi/HeadWatcher/poll timer/overlay'i bırakır (MainWindow.xaml.cs:1422-1433); OnExit ≤2 s bütçeli, Task.Run+Wait ile dispatcher deadlock'suz (AppShutdown.cs:28-42); outer job KILL_ON_JOB_CLOSE her koşulda süpürür (EngineHost.cs:271); ölçülmüş kaskad 84 ms, artık süreç yok (2026-09-14). Koşu bitince MSBuild.exe kalmaz (nodeReuse:false, UseSharedCompilation=false — §9.2 bilinçli).
- Güncelleme motoru yalnız kurulu kopyada (UpdateService.cs:42 IsInstalled kapısı), ilk kontrol Show'dan 5 s sonra, 4 saatte bir System.Threading.Timer; bu makinede kurulu kopya olmadığından tamamen kapalı — açılışa HttpClient maliyeti yok.
- HEAD izleyicisi yalnız .git\logs\HEAD üzerinde FileSystemWatcher, yoklama yok (HeadWatcher.cs:66-90); AutoSync Attach ucuz.
- Child process başlatma: CREATE_NO_WINDOW + SUSPENDED → job assign → resume, handle mirası 3 pipe ile sınırlı (JobProcessLauncher.cs:40-84) — konsol penceresi parlaması ve kaçak süreç yok.
- Gömülü fontlar: 7 OTF (1,18 MB) hepsi kullanımda (Console ağırlığı Light — Tokens.xaml:148), pack URI tek yerde (AppFonts.cs); ilk metin ölçümünde bir kerelik parse — kaldırılacak/ertelenecek bir şey yok, yalnız ölçülebilir.
- BrandGeometry.xaml 4 KB, ReleaseNotes (CHANGELOG) parse'ı Lazy (ReleaseNotes.cs:111) — About ctor'u tetiklemiyor; SupervisorLayout klasör adı assembly metadata'dan bir kez okunur.
- ConsoleBatcher boşta uyumuyor (App.xaml.cs:116, pencere yalnız satır gelince açılır) ve imleç saatleri IsVisible kapılı (2026-09-14) — açılışta arka plan pompası yok.
- İlk graf/StickyLayerList realize'ı açılışın parçası değil, ilk topolojiyle (Sync sonrası) gelir; 187 düğüm için daha önce 50-130 ms ölçülmüş.

**Açık sorular:**
- 1,5 s açılış ölçümü hangi kopyada alındı — bin\Debug'dan (dotnet run / F5) mı, Release publish'ten mi? Bu makinede kurulu kopya yok (%LOCALAPPDATA%\BuildOrchestrator.App yok): günlük kullanımda uygulamayı nereden başlatıyorsunuz? (R2R yalnız kurulu/publish kopyayı hızlandırır; Debug bin'den koşan kopya için tek kazanç Release.)
- Start minimized to tray + Start with Windows açıkken (bugün ikisi de kapalı) pencere hiç gösterilmeden konsolda 'Engine ready' geliyor mu, yani tepsideyken Ctrl+Shift+Space ile Build çalışıyor mu? Motor spawn'ı Loaded'a bağlı (MainWindow.xaml.cs:388-394) ve StartInTray yalnız EnsureHandle yapıyor (1341); ARCHITECTURE §12.1 ve StartInTray doc'u 'iki yolda da Sync motor hazır olunca koşar' diyor — doküman ile kod uyuşmayabilir, canlı doğrulama gerek (D8-4 bunu kendiliğinden birleştirir).
- Açılış Sync'inin fetch'i ağınızda tipik kaç saniye sürüyor (VPN/GitHub)? Exit'in uçuştaki Sync'in fetch'ini beklemesi bilinçli karar (RunViewModel.Exit.cs:13-15): fetch'i iptal edilebilir yapmak ister misiniz, yoksa yalnız yerel işi fetch'le paralel koşturmak (transkript sırası korunarak) yeterli mi?
- evaluation-cache.json'daki 2558 bo-vm-* (test temp) girişi nereden geliyor — test süitini BO_CACHE_ROOT vermeden mi koşuyorsunuz (ContentDecisionDiagnosticsTests gerçek cache kökünü kullanır) yoksa bir test gerçek Supervisor'ı --logs olmadan mı başlatıyor? Kaynağı bulunmadan budama kalıcı olmaz (D8-1 madde 4).
- 'Bittiği anda arka planda her şey ölmeli' Supervisor'ı da kapsıyor mu? Bugün koşu bitince Supervisor boşta kalır (~36 MB, ~%0 CPU) ve ancak Exit'te ölür; 'boşta N dk sonra motoru kapat, ilk komutta yeniden doğur' isteniyorsa bu ayrı bir tasarım kararı (EngineHost.RestartAsync yolu zaten var).
- %LOCALAPPDATA%\BuildOrchestrator\run-inflight.json şu an dolu (458 B, 01:59): bir koşu hâlâ uçuşta mı, yoksa uygulama koşu ortasında mı kapatıldı? Dolu kaldıysa bir sonraki açılış o projeleri 'kesildi, yeniden derlenecek' sayacak.


### N1-tepside-derleme-yolu

#### N1-1 — Tepside kısayolla Build: startRun komutu açılış koreografisi bitene kadar gitmiyor — gizli pencerede de oynuyor, nominal 1,68-2,78 s ölü bekleme

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.App/MainWindow.xaml.cs:66-71 (kapı lambda'ları), 322-326 (OperationChoreography kablosu), 1305-1317 (OnGlobalHotkey); src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:1003-1008; src/BuildOrchestrator.App/Services/OperationChoreographer.cs:90-95
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1305-1317 — OnGlobalHotkey: Build pencereyi getirmez, vm.BuildCommand'ı çalıştırır (görünürlük kontrolü yok)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 997-1011 — StartRunAsync: await playChoreography(scope) bitmeden StartRunCommand gönderilmez
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 66-71, 322-328 — Koreografi/StepHold kapısı yalnız App.Motion?.AnimationsEnabled okur (OS reduced-motion); IsVisible girdisi yok
- `src/BuildOrchestrator.App/Services/OperationChoreographer.cs` satır 67-110 — Atlama koşulu yalnız 'n == 0 || !_animationsEnabled()' (satır 90); aksi hâlde StepPlayer oynar, komut Settle adımından sonra serbest kalır
- `src/BuildOrchestrator.App/Controls/MarkingChoreography.cs` satır 51-80 — NeutralMs 440, WaveTailMs 380, MaxStaggerMs 110, MaxWaveMs 1100; son adım Settle = 1300 + WaveSpan
- `src/BuildOrchestrator.App/Controls/StepPlayer.cs` satır 21, 71-92 — Tek DispatcherTimer; her adım için nominal fark interval olarak kurulur (birikimli kayma düzeltilmez)

**Kanıt:** Kod: komut son adımda (Settle, StepAtMs = 1300 + WaveSpanMs(n)) serbest kalır; TotalMs (2060+W) oynatıcıda kullanılmaz. WaveSpan = Stagger*(n-1)+380, Stagger = min(110, round(1100/(n-1))). Türetilen nominal gecikme: n=1 → 1680 ms; n=10 → 2670 ms; n=11-12 → 2780 ms (tavan); n=154 (ölçülen Rebuild'in kapsamı: 187 - 33 döngü üyesi) → stagger 7 ms, W=1451 → 2751 ms. n=0 (her şey güncel) → 0 ms. Gizli pencere için hiçbir dal yok: OnGlobalHotkey → BuildCommand → StartRunAsync aynı yoldan geçer. ARCHITECTURE §14.5 (satır 4915-4922) 'The run command goes out when the choreography ends' kararının gerekçesi 'aynı tıklama bazen animasyonlu bazen anında açılmasın'dır — gizli pencerede izleyen yok, gerekçe uygulanmaz. Ölçüm (ön planda, measure-release log satır 44 + decision.log): F6 01:52:09.771 → 'run started' 01:52:17.463 = 7,69 s; bunun en az 2,75 s'si (nominal) koreografi, kalanı planlama. Ek risk (tahmin): StepPlayer 154 dalga adımını 7 ms aralıkla tek tek zamanlıyor; ARCHITECTURE §14.5'in kendi notuna göre DispatcherTimer çözünürlüğü ~15,6 ms → dalga 1,07 s yerine ~2,4 s, komut ~4,1 s'de gidebilir (ölçülmeli).

**Etki:** Birincil senaryoda (VS'te çalışırken Ctrl+Shift+Space) her Build, MSBuild'e tek satır gitmeden önce 1,7-2,8 s (nominal; gerçek değer ölçülmeli, 4 s'ye çıkabilir) bekliyor; bu sürede gizli pencerede dalga UI işi de yapılıyor (N1-2). Zayıf makinede timer kayması büyür.

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü (measure3.log:29-49): tepsiden Ctrl+Shift+Space -> runStarted 5,68 s; ön planda F5 5,58 s; F6 Rebuild 6,7-7,3 s. Koddan türetilen nominal koreografi payı 2,75-2,78 s (5,68 s'nin yaklaşık yarısı); gerçek payı ölçülmedi. Rebuild'in Build'den 1,0-1,6 s geç başlaması 154 adımlı dalganın DispatcherTimer çözünürlüğüne takılması hipoteziyle (StepPlayer.cs:83-88, 7 ms aralık) tutarlı ama kanıtlanmadı. Bulgudaki '7,69 s' eski ölçümdür.

**Çözüm:** Kapıyı 'dekoratif hareket açık mı' sorusuna çevir: koreografi ve StepHold lambda'ları (MainWindow 66-71) App.Motion.AnimationsEnabled yerine N1-6'daki tek sinyali (reduced-motion VEYA yüzey gizli → false) okusun. Mevcut reduced-motion dalı (OperationChoreographer 90-95) aynen yeniden kullanılır: kapsam Marked=true, tek PushGraph, PlayAsync Task.CompletedTask döner → komut aynı dispatcher turunda gider. Yeni kod yolu yazılmaz.

**Çözüm (doğrulayıcı düzeltmesi):** Öneri geçerli; iki düzeltme: (1) _choreographer/_stepHold alan başlatıcılarıdır (MainWindow.xaml.cs:66-71) — lambda 'this.IsVisible' yakalayamaz (CS0236), kurulum ctor'a taşınmalı. (2) Koşu ortasında gizlenirse oynayan koreografi _choreographer.Cancel(_vm.Projects) ile kesilmeli (Finish kapıyı serbest bırakır, OperationChoreographer.cs:163-175). Minimize kapsam dışı kalır (IsVisible true). ARCHITECTURE §14.5 'A choreography either always plays or never does' kararına 'yüzey gizli' sınıfı eklenir — doküman aynı işte güncellenir, kullanıcı onayı gerekir.

**Değişmezler:** Shell-out, planlama Core'da, IPC sözleşmesi: dokunulmaz. §14.5 'ya her zaman oynar ya hiç' kararı korunur — reduced-motion ile aynı sınıfa 'yüzey gizli' eklenir; ARCHITECTURE §14.5 ve §12.3 (hotkey paragrafı) aynı işte güncellenmeli.

**Test fikri:** Kırmızı test (ChoreographyTests yanında): yüzey gizli sinyali verilmiş MainWindow'da BuildCommand.Execute sonrası aynı turda DebugOnCommandSent StartRunCommand görür ve _choreographer.IsPlaying false. Bugün kırmızı: komut gelmez, IsPlaying true. İkinci test: görünür + animasyon açıkken komut hâlâ koreografi sonunda gider (kural değişmedi).

**Doğrulayıcı notu:** Kod doğrulandı: OnGlobalHotkey (MainWindow.xaml.cs:1305-1317) görünürlüğe bakmadan BuildCommand'ı çalıştırır; StartRunAsync 'await playChoreography(scope)' bitmeden StartRunCommand göndermez (RunViewModel.cs:1003-1008); koreografi kapısı yalnız App.Motion?.AnimationsEnabled okur (MainWindow.xaml.cs:66-71), atlama dalı 'n == 0 || !_animationsEnabled()' (OperationChoreographer.cs:90-95). Nominal süre türetmesi doğru (Settle = 1300 + WaveSpan; n>=11 için 2,75-2,78 s). Ölçüm toplam gecikmeyi verdi ama koreografi payını AYIRMADI.

#### N1-2 — Açılış dalgası her işaretlenen satır için tam graf beslemesi kuruyor (O(n²)) — pencere gizliyken de

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Services/OperationChoreographer.cs` satır 99-109, 161-166, 182-186 — Her dalga adımı row.Marked=true + PushGraph(); PushGraph her seferinde kapsam üzerinden yeni HashSet kurar; Enter() her adımda tüm satırlara Fade yazar
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 823-840, 851-856 — ApplyMarkingToGraph = SetMarking + PushGraphStatuses; PushGraphStatuses her çağrıda RowsById sözlüğü + GraphBinder.Nodes kurar
- `src/BuildOrchestrator.App/ViewModels/GraphBinder.cs` satır 31-55, 80 — Nodes her çağrıda TopologicalDepths'i yeniden hesaplar ve 187 GraphNode üretir
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 485-494, 1468-1472 — SetMarking adım değişmese de ApplyAllOpacities (tüm düğümler) çağırır
- `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` satır 257-261 — Marked değişimi gerçeklenmiş satırda 200 ms renk akışı başlatır

**Kanıt:** n=154 kapsamda dalga 154 + 5 adım oynar; her adım: HashSet(≤154) + ApplyAllOpacities(187) + RowsById(187) + GraphBinder.Nodes(187 + TopologicalDepths). Yani ~159 kez tam besleme ≈ 159×(154+3×187) ≈ 114.000 düğüm-işlemi + 159 topolojik derinlik hesabı, nominal 1,07 s'lik pencerede (türetilmiş sayım; süre ölçülmedi). ARCHITECTURE §14.5 (satır 4906-4913) bu itişi bilerek istiyor (liste ile graf senkron yansın) — ama gerekçe görünür pencere içindir. Görünürlük kapısı yok: GraphView.IsPanelVisible (satır 654) yalnız panelin kendi Visibility'sine bakar, Window.Hide() onu değiştirmez.

**Etki:** Tepside başlatılan her Build'in ilk ~1-2,5 saniyesinde UI thread'i kimsenin görmediği dalga için çalışıyor; aynı anda Supervisor planlamaya başlamış olması gerekirken komut henüz gitmemiş (N1-1). Görünürken de aynı O(n²) şekil dalganın akıcılığını yiyor (zayıf makinede kare düşüşü — ölçülmeli).

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü: graf beslemesi 25 s'de 185 ms (bunun 175 ms'i TopologicalDepths). Dalga sırasındaki O(n^2) payı ölçülmedi; gizliyken N1-1 ile zaten hiç oynamaz.

**Çözüm:** Gizliyken: N1-1 ile dalga hiç oynamaz, tek PushGraph kalır. Görünürken (ayrı, küçük iş): PushGraph kümeyi artımlı tutsun (HashSet'e Add; her adımda yeniden kurma) ve dalga adımında PushGraphStatuses yerine yalnız değişen düğümün statüsü itilsin; GraphBinder.Nodes TopologicalDepths'i topoloji başına bir kez önbelleklesin (D6-3 / D7-3 ile aynı kök).

**Çözüm (doğrulayıcı düzeltmesi):** Tek değerli parça: GraphBinder.Nodes'ta TopologicalDepths'i topoloji başına bir kez önbellekle (GraphBinder.cs:39) — ölçülen 185 ms'nin 175 ms'ini kaldırır, görünür pencerede de geçerli. Artımlı HashSet / tek düğüm itişi için ölçülmüş gerekçe yok.

**Değişmezler:** §14.5 'dalga grafı kendi temposunda iter' kararı korunur (itme kalır, yalnız maliyeti düşer). Kopya yasağı: önbellek tek yerde (GraphBinder ya da MainWindow'un topoloji sinyali).

**Test fikri:** Sayaçlı test: 154 satırlık kapsamda dalga boyunca TopologicalDepths en çok 1 kez hesaplanır (bugün kırmızı: ~159). Gizli yüzeyde PushToGraph tam 1 kez çağrılır.

**Doğrulayıcı notu:** Yapı doğru: her dalga adımı PushGraph -> ApplyMarkingToGraph -> SetMarking + PushGraphStatuses (OperationChoreographer.cs:99-109, 182-186; MainWindow.xaml.cs:823-840), GraphBinder.Nodes her çağrıda TopologicalDepths hesaplar (GraphBinder.cs:39), SetMarking adım değişmese de ApplyAllOpacities çağırır (GraphView.xaml.cs:485-494). Ama ölçülen maliyet küçük: tepsi rebuild trace'inde (25 s) PushGraphStatuses kapsayıcı 185 ms, GraphBinder.Nodes 179 ms, TopologicalDepths 175 ms — UI thread aktif süresinin %1,6'sı. Dalganın kendi maliyeti hiçbir trace'te yok (trace pencereleri koşu içinde). 'Önemli' değil.

#### N1-3 — Gizli pencerede konsol hattı tam çalışıyor: her projectLog satırı pompaya, AvalonEdit belgesine ve üçüncü bir kopyaya (_backlogLines) gidiyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | upgraded · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.App/MainWindow.xaml.cs:576-606 (pompa + AppendConsoleBatch), 285-288 (ConsoleCleared -> ClearRunDocument); src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs:274-319, 341-362, 390-399, 610-652
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2694-2716 — OnProjectLog (IPC okuma thread'i): _liveLines + _runText + anlatı modunda koşulsuz _console.Post
- `src/BuildOrchestrator.App/Console/ConsoleBatcher.cs` satır 129-164 — Pump: satır varsa 50 ms pencere → StringBuilder → ToString → flush
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 576-606 — RunConsolePumpAsync → Dispatcher.InvokeAsync → AppendConsoleBatch; görünürlük kontrolü yok
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 274-319, 390-399, 440-455 — AppendBatch: Insert + TrimToRenderSlice (GetText ile kırpılan metnin kopyası) + _backlogLines.AddRange + ScrollToEnd
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 610-655 — Mevcut yeniden kurma yolu: ShowRunDocument (tilt'li) / ClearRunDocument; ortak gövde ResetRunDocument private
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2820-2843 — SeedRunDocument / SeedProjectDocument: _gate altında snapshot + PostReseedDrop (mevcut, yarışsız senkron tohumlama)

**Kanıt:** Ölçülen koşuda 23.047 satır / 7,7 MB (~160 satır/s) bu yoldan geçti. Anlatı modunda (ActiveProjectId null — StartRunAsync satır 960 her koşuda null'a çeker) TÜM projelerin ham satırları Post edilir (satır 2713-2714). UI thread'inde batch başına: belge Insert, 200 satır üstü için Remove + GetText (kırpılan metin string'i) + LastLines ile _backlogLines'a ekleme, ScrollToEnd. Pencere gizliyken bu belgeyi kimse görmüyor; VM'de aynı metin _runText'te zaten duruyor ve görünür olunca SeedRunDocument ile tek seferde kurulabiliyor (Back akışı bunu bugün yapıyor: MainWindow 645-651). Yani gizliyken belgeye yazmak saf israf; ayrıca _backlogLines koşu metninin görünümde yaşayan üçüncü kopyası (D6-1/D10-4: _liveLines + _runText + _backlogLines).

**Etki:** Tepside build boyunca UI thread'inde saniyede ~20 batch'e kadar AvalonEdit belge churn'ü + string kopyaları; gizli pencerede layout/TextFormatting'in ne kadarının koştuğu ölçülmeli (N1-0 WPF notu). Bellek: koşu metninin bir kopyası (≈ satır metni × 2 byte UTF-16; 7,7 MB tel hacmi için üst sınır ~15 MB + satır başı nesne yükü) gizliyken hiç oluşmaz.

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü (app-tray-rebuild.speedscope.json, UI thread 7164): AvalonEdit ölçümü 2122 ms / 25 s; Finalizer thread'in 23,3 s meşguliyeti aynı metin biçimlemesinin DWrite sarmalayıcılarından. Bulgudaki bellek tahmini (~15 MB) ölçülmedi.

**Çözüm:** VM'e tek bayrak (N1-6 sinyalinden beslenir): gizliyken OnProjectLog ve AppendRunLine Post ETMEZ (satır 2713-2714 ve 2775; _gate altında okunur). _runText/_liveLines birikimi aynen sürer (durum VM'de). Görünür olunca tek seferde: bayrak aç + SeedRunDocument(text => ConsoleView'ın tilt'siz kurulumu); seçili proje modundaysa SeedProjectDocument. Eksik olan tek şey: ResetRunDocument'ın (ve PlayCascade'in) animasyonsuz açık yüzeyi — ShowRunDocument'a 'animate' parametresi (kopya gövde açılmaz). Yarış güvenliği mevcut mekanizmadan gelir: snapshot + PostReseedDrop aynı _gate altında, bayat batch'ler nesil damgasıyla düşer (ConsoleBatchRouter).

**Çözüm (doğrulayıcı düzeltmesi):** Daha az müdahaleli yol: VM'e görünürlük bayrağı taşımak yerine kapıyı kabukta kur — AppendConsoleBatch (MainWindow.xaml.cs:597-605) pencere gizliyken batch'i düşürür ve 'konsol bayat' işaretler; ConsoleCleared (285-288) gizliyken ClearRunDocument çağırmaz (o çağrı PinAfterModeSwitch ile koşu başında 2-4 kez zorla UpdateLayout yapıyor, ConsoleView.xaml.cs:341-362). Dönüşte mevcut yol: anlatı -> _vm.SeedRunDocument(...) (RunViewModel.cs:2818-2827), proje modu -> SeedProjectDocument (2833-2843); nesil damgası uçuştaki batch'leri düşürür (ConsoleBatchRouter). Eksik tek yüzey: ResetRunDocument'ın (ConsoleView.xaml.cs:635-652, private) ve PlayCascade'in animasyonsuz çağrısı. Yeniden kurulum pencere görünür olduktan SONRA yapılmalı (pin layout okur).

**Değişmezler:** A13.2 (satır başına Dispatcher yok, marshal'sız log yolu) korunur; §5.5 dikiş kuralı değişmez (_liveLines birikmeye devam eder). IPC sözleşmesine dokunulmaz.

**Test fikri:** Kırmızı test (HiddenCursorClockTests deseni, gerçek Hide()): gizli pencerede 1000 OnProjectLog + pump tick → EditorControl.Document.TextLength değişmez ve AppendBatch çağrılmaz (bugün kırmızı). Show sonrası aynı dispatcher turunda belgenin son 200 satırı GetRunDocumentText()'in son 200 satırına eşit; arada gelen satır ne çift ne eksik.

**Doğrulayıcı notu:** Kod doğrulandı: OnProjectLog anlatı modunda her satırı Post eder (RunViewModel.cs:2694-2716), pompa batch'i görünürlüğe bakmadan belgeye basar (MainWindow.xaml.cs:576-606), AppendBatch Insert + TrimToRenderSlice + ScrollToEnd yapar (ConsoleView.xaml.cs:274-319, 390-399). Ölçüm bulguyu en büyük tekil kaleme çıkardı: tepsi rebuild trace'inde AvalonEdit TextView.MeasureOverride kapsayıcı 2122 ms (BuildVisualLine 1719 ms) — UI thread aktif süresinin (11412 ms) %18,6'sı. AppendBatch'in kendisi örneklenmedi; maliyet belgeye yazmanın tetiklediği ertelenmiş ölçüm/biçimlemede.

#### N1-4 — 200 ms tick ve olay başına yüzey beslemesi gizliyken sürüyor; graf beslemesi kapıdan ÖNCE kuruluyor (panel Collapsed iken de boşa)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.App/MainWindow.xaml.cs:352-364; src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:1850-1866, 1896-1902; src/BuildOrchestrator.App/ViewModels/RunViewModel.Exit.cs:75-88
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 30, 352-364 — _elapsedTimer 200 ms: TickElapsed + SetLineCount + (koşarken) PushGraphStatuses + FollowFrontier; görünürlük kapısı yok, timer hiç durmaz
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1850-1866, 2353-2418 — TickElapsed: ElapsedMs, Started satırların DurationMs'i, UpdateEta (Projects üzerinde 4-5 LINQ geçişi + liste ayırmaları), EvaluateEngineSilence
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 832-840, 879-946 — PushGraphStatuses: tick + Counters + CurrentOperation + IsRunning/IsStarting + IsRunUnderway + RowDecisionsChanged her birinde RowsById + GraphBinder.Nodes
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 654, 679-687 — Kapı GraphView içinde ve yalnız panel Visibility'sine bakar; besleme listesi çağıranda kurulduktan sonra _pendingStatuses'a konur
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1798-1803 — RefreshRunSurface her proje olayında VisibleProjects bildirimi yayar
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 250-258, 709-716, 752 — VisibleProjects bildirimi: VisibleRowSignature (187 yol id'sinin string.Join'i) + RefreshListInvite (VisibleProjects ikinci kez) + RefreshGraphFilter

**Kanıt:** Tick'in gizli pencerede koştuğu D7-3'te doğrulandı (verify-D7: confirmed); burada ek olan: (a) hangi parça durum, hangi parça görsel — ElapsedMs/DurationMs/ETA türev değerlerdir (kaynak: _elapsedStartMs, _projectStartedAtMs, satır State'leri VM'de durur), görünür olunca tek TickElapsed çağrısıyla yeniden üretilir; koşu bitişinde ElapsedMs zaten motorun kesin süresinden yazılır (OnRunCompleted satır 2423), yani balon metni tick'e bağlı değil. Tek 'bekçi' iş EvaluateEngineSilence'tır ve çıktısı (EngineOverdueMessage) yalnız şeritte okunur. (b) GraphView'in erteleme kapısı ('yalnız EN SON besleme tutulur', satır 641-652) beslemenin ÜRETİM maliyetini kurtarmıyor: list/focus yerleşiminde de her tick 187 GraphNode + TopologicalDepths kurulup saklanıyor. (c) Proje olayı başına (ölçülen koşuda ~187 sonuç + ~154 başlangıç) 3× O(n) + ~15 KB'lık imza string'i.

**Etki:** Tepside koşu boyunca saniyede 5 kez ETA + graf beslemesi + satır süre yazımları (bağlı satırlarda TextBlock güncellemesi). Tek başına ölçülen %40 çekirdeği açıklamaz (5 uyanış/s) — sürekli yük animasyon saatleri ve konsol hattından gelir (D7-1, N1-3); pay ölçülmeli.

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü: tick gövdesi 83 ms / 25 s (UI aktif süresinin %0,7'si). Dolaylı etki (Started satırların DurationMs yazımı -> TextBlock ölçümü; FollowFrontier -> liste kaydırma/realize) ayrıştırılmadı: FixedHeightVirtualizingPanel.MeasureOverride 682 ms, Realize 539 ms, ArrangeOverride 234 ms, CleanUp 143 ms ölçüldü ama tetikleyicisi atfedilmedi.

**Çözüm:** Gizliyken _elapsedTimer durur (IsEnabled=false); görünür olunca Start + aynı turda bir tam tick. Watchdog için gizliyken ayrı timer AÇILMAZ: EvaluateEngineSilence görünür olunca ilk tick'te koşar (mesajın tek tüketicisi şerit). PushGraphStatuses başına 'yüzey canlı değil ya da panel gizli → yalnız kirli bayrağı' kapısı: GraphView 'AcceptsStatuses' sorusunu açar (IsPanelVisible && yüzey canlı), MainWindow besleme listesini ancak o true iken kurar; görünür olunca mevcut üçlü tek sefer çağrılır: PushGraphRunPhase + PushGraphStatuses + PushGraphSelection (RebuildGraph'ın 811-812'de yaptığı sıra). FollowFrontier gizliyken çağrılmaz; dönüşte bir kez.

**Çözüm (doğrulayıcı düzeltmesi):** Timer DURMAZ; gövdesi kapılanır: gizliyken yalnız bekçi koşar (VM'e ayrı giriş: ör. TickElapsed'in görsel yarısını atlayan parametre ya da ayrı EvaluateEngineSilence çağrısı), ElapsedMs/DurationMs/UpdateEta/SetLineCount/PushGraphStatuses/FollowFrontier atlanır. Görünür olunca aynı turda tam tick gövdesi bir kez. PushGraphStatuses için GraphView'e 'AcceptsStatuses' açmak yerine MainWindow'da tek erken dönüş yeterli (çağıran tek sınıf).

**Değişmezler:** Kapı kararı tek yerde (GraphView) kalır — 'her çağıran aynı kontrolü kopyalamasın' gerekçesi (satır 641-649) korunur; yalnız soru çağırana açılır. D8 (timer türü VM'e sızmaz) korunur: timer MainWindow'da.

**Test fikri:** Kırmızı: gizli MainWindow + IsRunning → _elapsedTimer.IsEnabled false; Show() sonrası aynı turda ElapsedMs>0 ve graf düğüm statüleri satırlarla eşit. list modunda (panel Collapsed) tick GraphBinder.Nodes çağırmaz.

**Doğrulayıcı notu:** Tick'in gizliyken koştuğu doğru (MainWindow.xaml.cs:352-364) ama iki düzeltme var. (1) Doğrudan maliyeti küçük ölçüldü: tick lambda'sı 25 s'de kapsayıcı 83 ms, UpdateEta 27 ms, FollowFrontier 5 ms, TickElapsed 13 ms. (2) Çözüm HATALI: 'EvaluateEngineSilence çıktısı yalnız şeritte okunur, timer tamamen durabilir' iddiası yanlış — EngineOverdueMessage bekleyen çıkışı da serbest bırakır (RunViewModel.Exit.cs:75-78 EvaluateExit, :87 OnEngineOverdueMessageChanged) ve tek yazıcısı tick'tir (RunViewModel.cs:1896-1902). Timer gizliyken durursa tepsi menüsünden Exit + susmuş motor = uygulama kapanmaz.

#### N1-5 — Koşu bitince 'neon' bitiş finali gizli pencerede de oynuyor: 154 derlenen projede 5,68 s StepPlayer + 154 keyframe animasyonu

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 938-942 — Phase Done/Stopped → GraphHost.PlayEndFinale(BuiltInThisRun(), ...) — görünürlük kontrolü yok
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 535-566, 594-634 — PlayEndFinale: atlama yalnız 'derlenen yok || !AnimationsEnabledProvider()'; SetEndStep her adımda ApplyAllOpacities + ApplySelection + ApplyCamera; PlayNeon düğüm başına DoubleAnimationUsingKeyFrames
- `src/BuildOrchestrator.App/Controls/EndFinale.cs` satır 33-74 — HoldMs 900, NeonMs 1150, BreathMs 700, GreyMs 980, +420; zincir = Stagger*(n-1), Stagger = min(150, round(1500/(n-1)))

**Kanıt:** Türetme (ölçülen koşu: 152 succeeded + 2 failed = 154 derlenen): stagger = round(1500/153) = 10 ms, zincir 1530 ms, TotalMs = 900+1530+1150+700+980+420 = 5680 ms, filtre dönüşü 5880 ms. Bu süre boyunca 154 düğümde 9 keyframe'lik opaklık animasyonu ve 5 kez tüm-düğüm opaklık geçişi çalışır. Kod yolu pencere gizliyken aynıdır (D1-4'ün 'gizlemeden sonra ~8 s render' hipotezinin kod tarafı sayıları). Aynı anda tepsi göstergesinin çıkış evresi ve balon nefesi de oynar (TrayBuildIndicatorController 159-186). Görünümde kalan kalıcı durum yok: final bittiğinde EndStep.None = tüm düğümler tam opak; animasyonsuz dal (satır 539-543) aynı son hâle doğrudan gider.

**Etki:** Tepside biten her koşudan sonra ~5,7 s (154 projede) UI thread'i + render hattı kimsenin görmediği final için çalışır — kullanıcının 'bittiği an cihaz rahatlasın' beklentisinin tersi. Ön planda koşu sonrası ilk 10 s App 897 Mcycles/s ölçüldü (final + koşu sonu Sync birlikte); tepsideki pay ölçülmedi.

**Etki (doğrulayıcı düzeltmesi):** Süre koddan: 5,68 s boyunca 154 düğümde keyframe animasyonu + 5 tüm-düğüm opaklık geçişi, pencere gizliyken. CPU payı ölçülmedi. Not: koşudan 15-25 s SONRA tepside UI thread hâlâ 129 Mcycles/s (koşu öncesi 37) — bu final değildir, nedeni bulunmadı.

**Çözüm:** N1-6 sinyali GraphView.AnimationsEnabledProvider'a (MotionGate) yansıdığında mevcut animasyonsuz dal kendiliğinden devreye girer: final atlanır, ResumeFilter çağrılır. Koşu ortasında gizlenirse/gösterilirse: gizlenme anında oynayan final CancelEndFinale ile kesilir (mevcut metot, satır 575-579); final gizliyken bitmiş bir koşu için dönüşte sonradan OYNATILMAZ (graf son hâlinde gelir).

**Değişmezler:** §14.5 motion kuralları: reduced-motion dalıyla aynı sonuç. Liste bitişte sabit kalır kararı etkilenmez.

**Test fikri:** Kırmızı: gizli yüzeyde Phase=Done → GraphView.EndStep None kalır ve _endPlayer.IsPlaying false (bugün Hold'a girer). Final oynarken Hide → EndStep None, filtre askısı kalkmış.

**Doğrulayıcı notu:** Kod doğrulandı: Phase Done/Stopped -> PlayEndFinale (MainWindow.xaml.cs:938-942), atlama yalnız 'built yok || !AnimationsEnabledProvider()' (GraphView.xaml.cs:535-543), süre türetmesi doğru (EndFinale.cs:33-74; 154 derlenende 5680 ms — ölçülen koşu 141 succeeded + 13 failed = 154). Tepsideki maliyeti ÖLÇÜLMEDİ: koşu sonrası ölçüm pencereleri bitişten 15-25 s sonra başlıyor, final (5,7 s) pencerenin dışında.

#### N1-6 — 'Gizli mod' için tek sinyal yok: görünürlük bugün 4 ayrı yerde, 3 farklı tanımla okunuyor; önerilen tek kapı + tek seferlik yeniden senkron

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1240-1245, 1251-1252 — Pencere görünürlüğünün tek merkezi okuması tepsi controller'ına gider; motion sinyali overlay için ayrı okunur
- `src/BuildOrchestrator.App/Controls/MotionGate.cs` satır 31, 54 — StaticAnimationsEnabled = App.Motion?.AnimationsEnabled — sahiplerin tek okuma ifadesi (yalnız OS reduced-motion)
- `src/BuildOrchestrator.App/Services/MotionSettings.cs` satır 46-53, 75-87 — AnimationsEnabled sinyali Duration.* token'larını da 0'lar (Apply) — 'gizli' bu yola bağlanırsa her gizle/göster kaynak sözlüğünü yeniden yazar
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 182, 572-577 — Mevcut kapı 1: imleç saati IsVisible'a bakar
- `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs` satır 107-110, 419 — Mevcut kapı 2: aktif satır imleci IsVisible'a bakar
- `src/BuildOrchestrator.App/Controls/BuildingSpinner.cs` satır 85, 127 — Mevcut kapı 3: spinner IsVisible && motion
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 654, 731-752 — Mevcut kapı 4: panel Visibility'si (pencere gizlenince değişmez); bekleyen topoloji/statü görünür olunca uygulanır
- `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` satır 621-633 — Kapısız: satır nefesi yalnız motion'a bakar
- `src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs` satır 391-414 — Kapısız: belirsiz ilerleme süpürmesi (RepeatBehavior.Forever)
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 1084-1090, 1162, 1433 — Kapısız: beads ve kenar-akış saatleri
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1344-1349 — ShowFromTray: Show + Activate — yeniden senkronun bağlanacağı tek dönüş yolu

**Kanıt:** D7-1 (doğrulandı) MotionSettings'e 'yüzey askıda' sinyali önerdi; bu boyutta kod üstünden iki tuzak ve eksik tüketiciler doğrulandı. Tuzak 1: koreografi ve StepHold MotionGate'i değil App.Motion?.AnimationsEnabled'ı doğrudan okur (MainWindow 66-71) — sinyal yalnız MotionGate'e eklenirse koreografi gizliyken oynamaya devam eder. Tuzak 2: MotionSettings.Apply Duration.* token'larını sıfırlar ve tepsi overlay'i aynı sinyali (1240, 1252) + ExitBreath için Duration.Slow token'ını (1236) okur — 'gizli' reduced-motion'a katlanırsa overlay tepsiye inildiği anda statik kareye düşer ve nefes 0 olur. Sonuç: iki ayrı soru gerekir — 'OS hareket istiyor mu' (overlay + token'lar) ve 'ana yüzey dekoratif hareket oynatsın mı' (diğer her şey). Bütünlük envanteri: VM'de yaşayan durum — satırlar (State, Marked, InRunQueue, DurationMs, DepIssues), Counters, Phase, ElapsedMs/ETA kaynakları, StreamEvents + aktif satır, _runText/_projectText/_liveLines, seçim, RibbonLine. Yalnız görünümde yaşayan — AvalonEdit belgesi + _backlogLines (VM'den yeniden kurulabilir), GraphView slot modelleri/opaklık hedefleri (GraphBinder'dan yeniden kurulabilir), EndStep/_focusOff/_filterSuspended ve MarkStep (animasyonsuz dalda zaten son hâle gider), kaydırma/frontier konumu, şerit chip imzaları. VM'e taşınması gereken durum bulunmadı; eksik olan iki görünüm yüzeyi: konsolun tilt'siz kurulumu ve GraphView'in 'yüzey canlı' girdisi.

**Etki:** Tek sinyal olmadığı için her yeni sonsuz saat ya da besleme yolu gizli pencereyi unutabiliyor (bugün 3 saat sahibi + tick + konsol + koreografi + final kapısız). Ölçülen sonuç: tepside build %40 çekirdek (1,44 Gcycles/s), tepside boşta 50-54 Mcycles/s.

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü: tepside rebuild App 1853 Mcycles/s (tek çekirdeğin %57'si), UI thread 1385; tepside boşta 37; koşu sonrası tepside boşta UI thread 129. Bulgudaki '%40 / 1,44 Gcycles/s' ve '50-54 Mcycles/s' eski ölçümdür. Saatlerin kendi payı küçük: TimeManager.Tick 390 ms / 25 s, 'animasyon saatleri' %2,3.

**Çözüm:** Adımlar (her biri ayrı kırmızı testle): (1) Tek kaynak: IMotionSettings'e 'SurfaceLive' girdisi ve 'Decorative => AnimationsEnabled && SurfaceLive' okuması; MainWindow IsVisibleChanged (1244'teki mevcut abonelik — ikinci abonelik açılmaz) hem controller.SetMainWindowVisible'ı hem bunu besler; minimize D7-2'de. Duration token'ları ve tepsi controller'ı ham AnimationsEnabled'da kalır. (2) MotionGate.StaticAnimationsEnabled/Enabled Decorative'i okur; değişimde mevcut AnimationsEnabledChanged yayınlanır → ProjectRow nefesi, şerit süpürmesi, beads/kenar akışı, spinner mevcut abonelikleriyle söker/kurar. (3) MainWindow 66-71'deki iki lambda aynı ifadeye bağlanır → N1-1 ve N1-5 kapanır. (4) Tick: N1-4. (5) Konsol: N1-3. (6) Graf: GraphView.IsPanelVisible yüzey-canlı girdisini de sorar; OnPropertyChanged 742-751'deki 'topoloji önce, statü sonra' boşaltması bir metoda çıkarılıp iki tetikleyiciden çağrılır (kopya yasak). (7) Event stream: StreamEvents VM'de birikmeye devam eder (≤150/260, sınırlı); daktilo/parıltı Decorative false iken anlık. (8) Dönüş: yüzey canlı olduğu AYNI dispatcher turunda sırayla — timer tick'i, graf üçlüsü, konsol tohumlama, FollowFrontier, şerit RefreshAll; hepsi mevcut metotlar. Sıra: sinyal IsVisibleChanged içinde (Show() senkron çağırır, ilk kare Render önceliğinde sonra gelir) → ilk kare tutarlı.

**Çözüm (doğrulayıcı düzeltmesi):** Sıra ölçüme göre değişmeli: önce konsol (N1-3, %18,6), sonra layout üreten beslemeler (şerit chip'leri, liste, akış, graf), saat kapıları EN SON — tek başına saat kapısı ölçülen yükün küçük kısmını alır. Sinyal MotionGate'e eklenirse MainWindow.xaml.cs:1240 ve 1252'deki iki okuma ham AnimationsEnabled'a çevrilmeli (yoksa overlay tepsiye inince statik kareye düşer). Motion/token kaynak guard'ları yeni okuma ifadesine göre yeniden yazılır.

**Değişmezler:** Kopya yasak: görünürlük tek abonelikten, motion tek ifadeden okunur. §14.5: 'her sonsuz animasyon IsVisible'a kapılı' kuralı tek kapıya iner; overlay istisnası (§12.3) açıkça yazılmalı. Planlama Core'da, IPC, git, OutDir: dokunulmaz.

**Test fikri:** HiddenCursorClockTests kalıbında gerçek Hide()/Show(): (a) Hide → PART_Breath.HasAnimatedProperties false, beads saati null, şerit süpürmesi yok; (b) Hide iken overlay controller'ı hâlâ ShowLoop alır (istisna pinlenir); (c) Hide → 50 olay + koşu bitişi → Show: satır statüleri, graf düğüm statüleri, konsol kuyruğu, şerit metni VM ile birebir. Kaynak guard'ı: App ağacında 'App.Motion?.AnimationsEnabled' doğrudan okuması yalnız MotionGate ve tepsi kablosunda.

**Doğrulayıcı notu:** Envanter doğrulandı: IsVisible kapısı olan saatler ConsoleView.xaml.cs:577, EventStreamView.xaml.cs:107-110/419, BuildingSpinner.cs:127; kapısız olanlar ProjectRow.xaml.cs:621-633 (nefes), StickyRibbon.xaml.cs:391-414 (süpürme), GraphView.xaml.cs:1084-1090 + 1150-1166 (beads), :1433 (kenar akışı). İki tuzak da doğru: koreografi MotionGate'i değil App.Motion'ı doğrudan okur (MainWindow.xaml.cs:66-71); MotionSettings.Apply Duration.* token'larını sıfırlar (MotionSettings.cs:75-80) ve overlay aynı sinyali okur (MainWindow.xaml.cs:1240, 1252, 1236). DOKÜMAN-KOD UYUŞMAZLIĞI: ARCHITECTURE §14.5 'every infinite animation is gated on IsVisible as well as on its own state' diyor; kod üç sahipte kapısız — kullanıcıya sorulmalı.

#### N1-7 — IPC: pencere gizliyken projectLog satırlarını hiç göndermemek — mümkün, ama iki aşamalı ve ölçüme bağlı; anlatı konsolunda davranış değişikliği doğurur

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2130-2134 — Emit: diske AppendLine + ProjectLogEvent(…, SanitizeLine(line)) + Events.TryWrite — abone/koşul yok
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 655-658 — Olay pompası: her olay için writer.WriteAsync
- `src/BuildOrchestrator.Contracts/Ipc/NdjsonFraming.cs` satır 17-30 — Mesaj başına SerializeToUtf8Bytes + 2 WriteAsync + FlushAsync
- `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` satır 65, 287, 302 — GetProjectLogCommand / ProjectLogChunkEvent(ThroughLineNumber) / ProjectLogEvent(RunId, ProjectId, LineNumber, Text)
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 172-196 — ReadLoopAsync: satır başına Deserialize + EventReceived
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 339-343 — ProjectLogEvent marshal'sız doğrudan VM'e
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1914-1920, 2694-2716, 2979-2996 — OnEvent → OnProjectLog (3 birikim) ve chunk dikişi (_liveLines + ThroughLineNumber)

**Kanıt:** Satır başı maliyet (koddan): Supervisor — ikinci SanitizeLine, olay nesnesi, kanal yazımı, JSON serialize (byte[] ayırma), semafor, 2 pipe yazımı + flush. App — okuma tamponundan satır kopyası, polimorfik Deserialize (olay + 3 string: RunId 36 karakter, ProjectId tam yol, Text), OnEvent, _gate kilidi, _liveLines.Add (olay nesnesi koşu boyunca tutulur), _runText.Append, Post. Hacim (ölçüldü): 23.047 satır / 7,7 MB / 142 s ≈ 162 satır/s, satır başına ~334 byte tel yükü → ~46.000 pipe yazımı + 23.000 flush. Supervisor'ın koşu içi TOPLAM CPU'su ölçümde tek çekirdeğin %1-5'i — Supervisor tarafındaki kazancın üst sınırı budur, yani asıl kazanç App'in okuma thread'i işi ve App belleğindedir (payı ölçülmedi). Dikiş güvenliği: §5.5 mekanizması yeterli — akış yeniden açıldıktan SONRA gönderilen getProjectLog'un chunk'ı o ana kadarki tüm satırları diskten getirir, canlı satırlar LineNumber > ThroughLineNumber ile eklenir; komutlar tek sıralı kanaldan gittiği için boşluk oluşmaz. Anlatı konsolu: plan/istek/hata satırları projectLog DEĞİL (PlanProgressEvent, ErrorEvent, AppendRunLine) — gizliyken de gelir ve _runText'te kalır; kaybolan yalnız gizli dönemin ham MSBuild satırlarının anlatıdaki kopyasıdır (diskte proje loglarında durur).

**Etki:** Tam bastırmada tepside build için App'e koşu başına ~23.000 mesaj ve koşu metninin üç kopyası (D6-1) hiç gelmez. Bedeli: pencereye dönüldüğünde anlatı konsolu gizli dönemin ham satırlarını göstermez ve 'N lines' sayacı küçülür — kullanıcının göreceği bir davranış değişikliği.

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü: projectLog taşımasının App okuma thread'indeki payı 25 s'de onlarca ms. Bellek tarafı: iki rebuild sonrası App managed canlı heap 74 MB (tek 13,7 MB char[]) — Aşama B bunu tepside azaltırdı ama ölçülmüş bir bellek sorunu değil.

**Çözüm:** Aşama A (sözleşme değişmez): N1-3 + N1-4 — App satırı almaya devam eder ama UI'ya taşımaz; ölç. Aşama B (yalnız ölçüm okuma thread'i/bellek payını anlamlı gösterirse): Contracts'a tek komut — 'SetLogStreamCommand(bool Live)' (varsayılan true; App ve Supervisor aynı pakette dağıtıldığı için sürüm kayması yok). Supervisor Emit'te diske yazım koşulsuz kalır, yalnız TryWrite kapanır. App gizlenince false, görünür olunca true gönderir; dönüşte seçili proje varsa mevcut LoadProjectLogAsync yeniden çağrılır. Karar Core'a girmez (bu bir taşıma tercihi, planlama değil).

**Çözüm (doğrulayıcı düzeltmesi):** Aşama B yapılmaz. Aşama A zaten N1-3'tür.

**Değişmezler:** stdout yalnız NDJSON: korunur. Diskteki proje logu eksiksiz kalır (§8.5). Yeni komut Contracts'ta; kopya yok. Anlatıdaki davranış değişikliği kullanıcı kararı ister (ARCHITECTURE §13.5/§5.5 güncellenir).

**Test fikri:** IPC integration: Live=false iken koşu → App'e 0 projectLog, disk logu tam; Live=true + getProjectLog → chunk + canlı kuyruk, satır numaraları 1..N kesintisiz ve tekrarsız.

**Doğrulayıcı notu:** Konumlar doğru (RunCoordinator.cs:2130-2134 Emit, 652-660 pompa; NdjsonFraming.cs:17-30; EngineHost.cs:172-196). Ama ölçüm Aşama B'nin gerekçesini kaldırdı: tepsi rebuild trace'inde App'in IPC okuma tarafı thread başına NdjsonReader.ReadAsync 42-50 ms, EngineHost.ReadLoopAsync 29 ms, 'JSON / IPC' 12-14 ms (25 s içinde). Supervisor'ın toplam CPU'su tek çekirdeğin %9-14'ü, managed canlı heap'i 2,0 MB. Kazanç ihmal edilebilir; bedeli sözleşme değişikliği + anlatı konsolunda davranış değişikliği.

#### N1-8 — Tepsiden başlatılan Build'de geri bildirim boşlukları: bekleyen istekte gösterge yok, reddedilen basış sessiz, bildirim kapalıyken sonuç ayırt edilemiyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | kisa / orta |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1315-1316 — CanExecute false ise hiçbir şey yapılmaz, hiçbir sinyal üretilmez
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1254-1255 — CanRequestRun: koşu/başlatma sürerken, motor erişilemezken, çıkış beklerken ya da topoloji yokken kapalı
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 927-936, 1092-1102 — WorkspaceBusy iken QueueRun: IsStarting=true ve konsola satır — Phase DEĞİŞMEZ
- `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` satır 84-85, 124-131, 181-185 — Gösterge yalnız Phase Starting/Running/Stopping iken; balon notificationsOn() false ise hiç gösterilmez

**Kanıt:** (a) Bekleyen istek: sessiz Sync (HEAD hareketi — örn. VS'te pull/commit sonrası) ya da Clean/Optimize sürerken kısayola basılırsa koşu kuyruğa alınır; QueueRun fazı Syncing'de bırakır, controller IsActive(Syncing)=false döndürür → gösterge iş bitene kadar çıkmaz; tek iz gizli konsoldaki satırdır. (b) Reddedilen basış: koşu zaten sürüyorsa ARCHITECTURE §12.3 (satır 2371-2373) 'it does nothing' diyor — gösterge zaten ekranda olduğu için tutarlı; ama motor erişilemez, topoloji yok (ilk Sync düşmüş) ya da çıkış bekliyor durumlarında da aynı sessizlik var ve ekranda hiçbir şey yok. (c) Show notifications kapalıyken koşu sonucu yalnız göstergenin kaybolmasıdır: başarı, hata, plan hatası (planFailed) ve motor ölümü aynı görünür. Tutarlı bulunanlar: ardışık koşular (her StartRunAsync konsol+akış tamponlarını temizler, satır 946-965), balon bütçesi koşu başına sıfırlanır (controller 97-101), motor ölümü fazı Stopped'a çeker ve balon şeridin öncelikli satırını taşır.

**Etki:** Geliştirici VS'teyken kısayola basıp hiçbir tepki görmeyebilir (Sync sürerken birkaç saniye; kalıcı kapalı durumlarda süresiz) — tekrar basar ya da pencereyi açmak zorunda kalır; birincil senaryonun güven sorunu. CPU/RAM etkisi yok.

**Etki (doğrulayıcı düzeltmesi):** CPU/RAM etkisi yok. Bekleyen istek penceresi ölçülmedi.

**Çözüm:** Karar gerektirir (davranış): (a) controller'ın 'aktif' sorusu faz yerine VM'in mevcut bayrağına genişletilir: bekleyen istek varken (IsStarting && bekleyen koşu) gösterge çıkar — veri VM'de var (IsMidRunLocked), yeni durum gerekmez. (b) Kapalı kapıda basış: pencere gizliyse ve notificationsOn ise şeridin o anki satırıyla tek balon (mevcut ShowRunFinished yolu; yeni metin yazılmaz). (c) Bildirim kapalıyken sonucun görünmesi için seçenek: göstergenin çıkış evresinden önce statik sonuç karesi — tasarım kararı, burada yalnız boşluk raporlanır.

**Çözüm (doğrulayıcı düzeltmesi):** (a) ARCHITECTURE §12.3'ün gerekçeli kararıyla çatışır ('Syncing is deliberately out of scope'; satır 2323-2324) — kullanıcı kararı olmadan uygulanmaz. (b) ve (c) ürün kararı.

**Değişmezler:** §12.3 'gösterge derleme koşularınındır, Sync kapsam dışı' kararı (a)'da genişler — bilinçli kararın değişimi: yeni kanıt, bekleyen isteğin de bir derleme isteği olması ve gizliyken tek görünür yüzeyin gösterge olması. Balon metni yeniden yazılmaz (K-5).

**Test fikri:** TrayBuildIndicatorControllerTests/TrayIndicatorBinderTests: pencere gizli + SyncBusy iken BuildCommand → ShowLoop çağrılır (bugün kırmızı). Gizli + IsEngineUnavailable iken kısayol → notifier bir kez çağrılır (bugün kırmızı).

**Doğrulayıcı notu:** Kod doğrulandı: QueueRun fazı değiştirmez, yalnız IsStarting=true + konsol satırı (RunViewModel.cs:1092-1102); controller yalnız Starting/Running/Stopping'i aktif sayar (TrayBuildIndicatorController.cs:84-85) — bekleyen istekte gösterge çıkmaz. CanExecute false ise sessiz (MainWindow.xaml.cs:1315-1316). Bildirim kapalıyken sonuç yalnız göstergenin kaybolması (controller 181-185). Performans bulgusu değil, davranış boşluğu.

#### N1-9 — Ölçüm boşluğu: birincil senaryo (tepsiden kısayolla başlatılan tam koşu) hiç ölçülmedi

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta [ZATEN YAPILMIŞ] |
| İş / risk | kisa / dusuk |

**Konum:**
- `.claude/temp/perf-2026-10-01/measure-release-2026-10-01.log` satır 44-52 — Koşu ön planda F6 ile başlatıldı; tepsi örneği tek 12 s'lik pencere (E1), koşu ortasında gizlenerek alındı
- `tests/BuildOrchestrator.Tests/App/UiResponsivenessBudgetTests.cs` satır 1-40 — Bütçe testi gizli pencere durumunu ölçmüyor (D6-7)

**Kanıt:** Eldeki tepsi-build sayısı (1.438 Mcycles/s, %40) 01:52:24-01:52:36 arası tek örnek; koşu 01:52:17'de başlamıştı, yani örnek koşunun ilk 7-19. saniyesi (pre-skip patlaması ve ilk projeler). Kısayolla tepside başlatma, basıştan ilk MSBuild.exe'ye kadar geçen süre, tepside koşu sonu (final + balon) ve tepside koşu boyunca bellek eğrisi ölçülmedi. Tek türetilebilen gecikme: F6 → 'run started' 7,69 s (ön plan).

**Etki:** N1-1..N1-7 kazançları sayıya bağlanamıyor; 'tepside %40' değerinin koşunun geneli için temsil gücü bilinmiyor.

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü: tepsiden Build tuş -> runStarted 5,68 s; o 10 s pencerede App 1054 Mcycles/s (%34), UI thread 758, render thread 170; koşu 5,0 s. Tepsi rebuild: App 1853 Mcycles/s, UI 1385, UI gecikmesi p95 13,1 / p99 26,9 / max 55-86 ms.

**Çözüm:** measure.ps1'e tepsi senaryosu: (1) Shift+Space ile gizle, 20 s bekle; (2) Ctrl+Shift+Space gönder, zamanı yaz; (3) decision.log 'run started' ve ilk proje logunun oluşma zamanı ile farkı al; (4) koşu boyunca 10 s pencerelerle App/Supervisor cycle + WS/Private; (5) koşu bittikten sonra 3×10 s; (6) Shift+Space ile göster, ilk 10 s. Aynı protokol no-op Build (her şey güncel) için de.

**Değişmezler:** Salt ölçüm; koda dokunmaz. Yeni test yazılırsa BO_ ortam değişkeni kapısı (CLAUDE.md ölçüm kuralı).

**Doğrulayıcı notu:** Yazıldığı anda doğruydu; 2026-10-02 ölçümü boşluğun çoğunu kapattı (measure3.log J senaryosu: gizli pencerede Ctrl+Shift+Space; T1 tepsi rebuild'leri; F koşu sonrası tepsi boşta). Kalan boşluklar: tuş -> runStarted'ın koreografi/planlama ayrımı; gizli BAŞLAYAN tam koşunun trace'i (trace'li tepsi rebuild'leri görünürken F6 ile başlatılıp 5 s sonra gizlendi — açılış koreografisi görünür oynadı); bitiş finali penceresi (koşu sonu 0-10 s); tepside bellek eğrisi.

#### N1-10 — TrayIndicatorBinder her VM PropertyChanged'inde şerit satırını yeniden derliyor (pencere görünürken ve boşta da)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Services/TrayIndicatorBinder.cs` satır 42-64 — Route: property adına bakmadan PushLine → vm.RibbonLine
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 626-633 — RibbonLine = RibbonText.Compose(20 girdi) — her okumada yeniden üretilir
- `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` satır 122, 170-186 — Satır yalnız CompleteExitAsync'te, balon gösterilirken okunur

**Kanıt:** Koşarken tick başına en az ElapsedMs + EtaMs + EtaText bildirimi (saniyede ~15 Compose), proje olayı başına Counters/WillBuild/Finished/VisibleProjects/aktif satır alanları (~8-10 Compose). Satırın tek tüketim anı balondur (koşu başına en çok 1 kez, yalnız pencere gizliyken). Sınıfın kendi doc'u (satır 15-19) itme modelini 'bildirim anında en güncel satır' için seçtiğini söylüyor; aynı garanti satırı o anda okuyan bir Func ile de sağlanır.

**Etki:** Küçük: saf string biçimleme, saniyede onlarca kez; ölçülmedi. Zayıf makinede GC baskısına katkı (kısa ömürlü string'ler).

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü: örnekleme çözünürlüğünün altında.

**Çözüm:** Controller'a satır yerine okuyucu ver: SetTerminalLine(RibbonLine) → ctor'da Func<RibbonLine>; CompleteExitAsync notifier'ı çağırırken okur. Binder yalnız Phase'i yönlendirir. Metin yine tek kaynaktan (vm.RibbonLine).

**Değişmezler:** K-5: balon metni şeridin satırıdır, ikinci kez derlenmez — korunur.

**Test fikri:** TrayIndicatorBinderTests: 100 ElapsedMs değişiminde okuyucu 0 kez çağrılır; koşu bitişinde balon en güncel Counters'lı satırı taşır (mevcut 'yarım cümle' testi yeşil kalır).

**Doğrulayıcı notu:** Kod doğru: Route her PropertyChanged'de PushLine -> vm.RibbonLine -> RibbonText.Compose (TrayIndicatorBinder.cs:51-64; RunViewModel.cs:626-633). Ama tepsi rebuild trace'inde RibbonText.Compose, get_RibbonLine ve TrayIndicatorBinder çerçeveleri HİÇ örneklenmedi (25 s). Performans bulgusu olarak değeri yok; yalnız N2-3'ün doğruluk düzeltmesinin parçası olarak anlamlı.

#### N1-0 — Referans: tepside bir Build'in başından sonuna App'te koşan işler ve WPF'in gizli pencere davranışı (bulgu değil, envanter)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1287-1317 — 1. WM_HOTKEY → OnGlobalHotkey → BuildCommand (UI thread)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 940-1016 — 2. StartRunAsync: akış/konsol temizliği, NeutralizeRows, faz Starting, koreografi beklemesi, komut
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1976-2035 — 4. OnRunStarted: kilit, faz Running, sayaç/ETA sıfırlama
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.Stream.cs` satır 114-349 — 5. AppendStreamFor/PushStream: olay akışı satırları (≤150 görünür / ≤260 tampon)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2420-2432 — 8. OnRunCompleted: faz Done/Stopped, meşguliyet bildirimi
- `src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs` satır 191-212, 240-287 — 9. Koşu sonu güvenlik ağı tetiği: HEAD aynıysa Sync YOK
- `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` satır 124-186 — 10. Gösterge çıkışı → nefes → balon

**Kanıt:** İş | tür | bugün gizliyken çalışıyor mu | thread. (1) Hotkey → BuildCommand | durum | evet | UI. (2) Akış+konsol tamponu temizliği, NeutralizeRows, RefreshRunSurface, pill, faz Starting, 'build requested' satırı | durum (+ConsoleCleared ile belge sıfırlama: görsel) | evet | UI. (3) Açılış koreografisi: GraphHost.BeginOperation, StepPlayer, satır Marked, graf itişi | Marked durum, gerisi görsel | evet, 1,7-2,8 s nominal (N1-1, N1-2) | UI. (3b) Tepsi göstergesi: faz Starting → ShowLoop | görsel, istenen | evet | UI + render. (4) planProgress satırları → AppendRunLine → Post → belge | _runText durum, belge görsel | evet | UI. (5) runStarted / buildPreview: kilit, faz, InRunQueue, önizleme kümeleri, ETA, ClearMarks, graf itişleri, akış satırı | durum + görsel itiş | evet | UI. (6) projectStarted / sonuçlar / skipped: satır State, sayaçlar, ETA, VisibleProjects imzası, graf itişi, akış satırı (daktilo/parıltı), şerit chip'leri, satır nefesi/şerit/sarsıntı animasyonları | State+sayaç durum, gerisi görsel | evet | UI. (7) projectLog satırı: _liveLines + _runText + Post | birikim durum, pompa→belge görsel | evet (N1-3) | IPC okuma thread'i → pool → UI. (7b) 200 ms tick: ElapsedMs, DurationMs, ETA, satır sayacı, graf beslemesi, frontier takibi, watchdog | türev durum + görsel | evet (N1-4) | UI. (8) runCompleted: faz, sayaç, akış kapanış satırı | durum | evet | UI. (8b) Bitiş finali | görsel | evet, 154 projede 5,68 s (N1-5) | UI + render. (9) Koşu sonu kendiliğinden Sync | — | HAYIR: tepside Activated gelmediği için bekleyen dönüş tetiği yok; güvenlik ağı HEAD değişmediyse hiçbir şey yapmaz | UI. (10) Gösterge çıkış evresi + nefes + balon | görsel, istenen | evet | UI + render. WPF davranışı (bilinen): Window.Hide() Visibility=Hidden yapar, HWND ve görsel ağaç yaşar, hiçbir şey Unloaded olmaz; binding ve DependencyProperty güncellemeleri görünürlükten bağımsız çalışır; Hidden (Collapsed değil) öğeler layout'a katılır, yani ölçüm/yerleşim geçersiz kılmaları gizli pencerede de işlenir; alt öğelerin IsVisible'ı false olur ve IsVisibleChanged ateşlenir (mevcut üç kapı buna dayanıyor ve testle pinli: HiddenCursorClockTests); aktif animation clock'ları pencere görünürlüğüne bakmadan tick eder ve render zamanlamasını uyanık tutar (ARCHITECTURE §14.5'in kendi ölçümü: tek unutulmuş Forever saat). Ölçülmeli (emin değilim): gizli pencerede render thread'inin rasterizasyonu tamamen atlayıp atlamadığı ve gizliyken AvalonEdit'in metin biçimlemesinin (DWrite) ne kadarının koştuğu — eldeki dotnet-trace ön plan profili. Ölçülmüş dolaylı kanıt: aynı koşuda görünür 1,9-3,2 Gcycles/s, gizli 1,44 Gcycles/s → gizlemek işin yaklaşık yarısını bile kaldırmıyor.

**Etki:** Tek başına etki yok; N1-1..N1-8'in dayanağı.

**Etki (doğrulayıcı düzeltmesi):** Bulgudaki 'görünür 1,9-3,2 / gizli 1,44 Gcycles/s' eski ölçüm; yenisi görünür 3427, gizli 1853 Mcycles/s.

**Çözüm:** Yok (envanter).

**Değişmezler:** —

**Doğrulayıcı notu:** Envanter kodla uyumlu. 'Ölçülmeli' denen iki soru artık cevaplı: (1) gizli pencerede layout TAM koşuyor — ContextLayoutManager.UpdateLayout kapsayıcı 8030 ms / 11412 ms UI aktif süresi (Measure 3986, fireAutomationEvents 1944, Arrange 1879); (2) AvalonEdit metin biçimlemesi gizliyken koşuyor (TextView.MeasureOverride 2122 ms). Render thread tepside 248 Mcycles/s (görünürken 1246) — rasterizasyon büyük ölçüde düşüyor, UI thread'i düşmüyor (1385'e karşı 1038).

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs:67-70, 98-106 — RefreshRows her Counters değişiminde (proje olayı başına) üç menü satırını silip yeniden kuruyor; içerik yalnız toplam proje sayısına bağlı. Ölçüldü (tepsi rebuild): RefreshRows 185 ms, BuildRow 118 ms kapsayıcı; UI thread'in pompaya dönmediği 233 ms'lik dilimin ilk işi BuildMenu.BuildRow.
- src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs:184, 219-231 — RebuildChipsIfChanged her VisibleProjects/Counters sinyalinde gizliyken de koşuyor: ölçülen 301 ms (BuildBuildingChips 245 ms, DsChipFactory.Small 95 ms). Gizliyken atlanıp dönüşte RefreshAll (235-241) çağrılabilir.
- src/BuildOrchestrator.App/MainWindow.xaml.cs:285-288 + src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs:341-362 — her koşu başında ConsoleCleared -> ClearRunDocument -> PinAfterModeSwitch gizli pencerede de 2-4 kez zorla UpdateLayout çağırıyor (ContextLayoutManager'ın tamamını sürer).
- src/BuildOrchestrator.App/Controls/FixedHeightVirtualizingPanel.cs (MeasureOverride/Realize/CleanUp) — gizli pencerede liste satırları realize edilip temizleniyor: MeasureOverride 682 ms, Realize 539 ms, ArrangeOverride 234 ms, CleanUp 143 ms (25 s). Tetikleyici (FollowFrontier kaydırması mı, satır güncellemesi mi) atfedilmedi.
- WPF UI Automation eş ağacı eşitlemesi (uygulama kodu değil): ContextLayoutManager.fireAutomationEvents -> AutomationPeer.UpdateSubtree kapsayıcı 1944 ms (UI aktif süresinin %17'si), InvalidateAutomationAncestors 821 ms. Trace'te RaisePropertyChangedInternal/RaiseStructureChangedEvent var, yani ölçüm makinesinde bir UIA istemcisi dinliyordu (ölçüm betiği UIA kullanmıyor; hangisi olduğu bilinmiyor). İstemci olmayan makinede bu pay yoktur — ölçümün genellenebilirliği için kayıt.
- Koşu sonrası tepside boşta UI thread 129 Mcycles/s, koşu öncesi 37 (measure2.log F senaryosu, bitişten 15-25 s sonra) — bitiş finali bu pencerenin dışında; hangi işin açık kaldığı hiçbir bulguda konumlanmadı.
- Ölçüm protokolü: trace'li 'tepsi rebuild' koşuları pencere GÖRÜNÜRKEN F6 ile başlatılıp ~5 s sonra gizlendi (measure2.log:107-108) — gizli pencerede açılış koreografisinin maliyeti hiçbir trace'te yok; yalnız J senaryosunun 10 s'lik cycle penceresi var.

**Temiz bulunan alanlar:**
- Koşu sonu kendiliğinden Sync tepside tetiklenmiyor: AutoSyncCoordinator.OnWorkspaceIdle (191-212) yalnız RunEnded güvenlik ağı tetiği kurar; EvaluateAsync HEAD son Sync'tekiyle aynıysa döner (satır 283). Ölçümdeki koşu sonu Sync'i ön plandaki Activated tetiğinden geliyordu (D11-2).
- ConsoleBatcher boşta uyuyor (WaitToReadAsync, satır 82-87); satır yokken timer uyanışı yok.
- ProjectLogEvent UI thread'ine marshal edilmiyor (MainWindow 339-343); satır başına Dispatcher çağrısı yok — batch başına tek InvokeAsync (576-581).
- Olay akışı sınırlı: StreamEvents ≤150 görünür / ≤260 tampon (RunViewModel.Stream.cs 346-348); gizliyken birikse de büyümez.
- Ardışık tepsi koşularında App tamponları birikmiyor: her StartRunAsync ClearStreamForNewOperation + ClearConsoleForNewOperation çağırır (RunViewModel.cs 946, 965, 2740-2754); bellek bir koşuyla sınırlı (koşu sonrası geri verilmemesi D2-1/D2-3'te).
- Build kapısı tek yerde ve kısayol aynı komutu kullanıyor (Hotkey.cs 117-121, RunViewModel.cs 1254-1258); WorkspaceBusy iken basış kaybolmuyor, QueueRun ile bekliyor.
- Balon bütçesi ve metni tutarlı: koşu başına bir balon (TrayBuildIndicatorController 97-101, 181-185), metin şeridin kendi satırı; motor ölümü ve planFailed fazı aktif kümeden çıkardığı için balon üretir.
- Koreografi/finale için animasyonsuz dallar zaten var ve son hâli doğru kuruyor (OperationChoreographer 90-95; GraphView 539-543) — gizli mod yeni kod yolu gerektirmiyor.
- Yarışsız konsol yeniden tohumlama mekanizması mevcut (SeedRunDocument + PostReseedDrop + nesil damgası; ConsoleBatchRouter) — dönüşte yeniden kullanılabilir.
- Pencere gizliyken overlay ayrı top-level pencere ve faz-sürümlü (MainWindow 1229-1248); kısayolla tepside başlayan koşu göstergeyi ek iş olmadan alıyor.

**Açık sorular:**
- Koreografinin gerçek süresi: StepPlayer 7 ms aralıklı 154 adımı DispatcherTimer ile oynatıyor; nominal 2,75 s mi, timer çözünürlüğü yüzünden ~4 s mi? (Stopwatch ile ölçülmeli; pencere gizliyken ayrıca.)
- F6 → 'run started' 7,69 s'nin koreografi dışındaki kısmı (planlama + harici kök güncellemesi) tepside de aynı mı? N1-9 protokolü ayırır.
- Gizli pencerede WPF render thread'i rasterizasyonu atlıyor mu, AvalonEdit metin biçimlemesi koşuyor mu? Tepside dotnet-trace gerekli.
- Aşama B (projectLog'u gizliyken hiç göndermemek) anlatı konsolunda gizli dönemin ham satırlarını düşürür — kabul edilebilir mi, yoksa dönüşte anlatı için de diskten kuyruk mu istenir? Kullanıcı kararı.
- Minimize edilmiş (tepside değil) pencere 'gizli mod'a dahil mi? IsVisible minimize'da true kalır (D7-2); sinyalin WindowState'i de okuması gerekir.
- Bekleyen istekte (Sync sürerken basılan Build) göstergenin çıkması §12.3'teki 'Syncing kapsam dışı' kararını genişletir — isteniyor mu?
- Doküman ile ölçüm uyuşmuyor: ARCHITECTURE §14.5 tepside boşta ~5 Mcycles/s diyor, 2026-10-01 ölçümü 50-54 Mcycles/s; hangi kapının geri açıldığı bu boyutta bulunmadı (D1/D7 kapsamı).


### N2-gosterge-animasyonu

#### N2-1 — Gosterge ana UI thread'in dispatcher'inda kosuyor: clock tick Render(7) onceliginde, motor olaylari ve konsol batch'i Normal(9) ile ayni kuyruga giriyor — tepside build sirasinda kare atlamasi buradan gelir

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1256-1262 — EnsureTrayOverlay: overlay ana pencerenin thread'inde new TrayBuildOverlayWindow() ile yaratilir (ayri Dispatcher yok)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 339-343 — ProjectLog disindaki her IPC olayi Dispatcher.InvokeAsync (varsayilan oncelik Normal) ile UI thread'ine tasinir
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 580 — Konsol batch'i de Dispatcher.InvokeAsync (Normal) ile gelir
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 352-364 — 200 ms tick: pencere gizliyken de PushGraphStatuses + FollowFrontier + SetLineCount (D7-3)
- `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml.cs` satır 48, 120-125 — Storyboard 30 fps; her tur _loop.Begin(this, true) ile ana dispatcher'da baslar

**Kanıt:** Kod: overlay icin ayri thread/Dispatcher.Run yok (src altinda Dispatcher.Run / SetApartmentState araması bos); controller fiilleri IsVisibleChanged ve VM PropertyChanged icinden, yani ana UI thread'inden cagriliyor (MainWindow.xaml.cs:1244, TrayIndicatorBinder.cs:42-59). WPF'te animasyon clock'lari ve katmanli pencerenin kare uretimi sahip Dispatcher'da Render onceliginde tick eder; Normal oncelikli InvokeAsync isleri (motor olaylari, konsol batch'i) kuyrukta her zaman onun onune gecer ve tek bir uzun is suresince kare uretilemez. Olcum (ortak baglam): tepside Rebuild sirasinda App 1,44 Gcycles/s = tek cekirdegin ~%40'i; gostergenin kendi payi ayristirilmamis (verify-D7: D7-5 'olcum-bekliyor'). Kosu bitisinde ilk 10 s App 897 Mcycles/s olculdu; cikis evresi (turun son <=0,9 s'si, TrayBuildIndicator.xaml:27-29) tam bu pencereye duser. Kare araligi build ALTINDA hic olculmedi — mevcut tek olcum bos makinede (N2-2).

**Etki:** Bu makinede UI thread'in %40'i dolu; zayif makinede ayni is tek cekirdegin tamamina yaklasir ve 30 fps'lik gosterge basamakli/takilarak akar (round-2 sonuc dosyasi O3: 'yuk altinda kare dusmesi hareketi basamakli gosteriyor'). Buyukluk (kac kare dusuyor) olculmedi.

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü (tepsi rebuild, gösterge açık): UI thread %45,6 meşgul; pompaya dönmediği en uzun dilimler 327, 261, 233, 178, 168 ms; 25 s'de >50 ms 27 kez, >100 ms 7 kez. 30 fps'te (kare 33 ms) bu, saniyede yaklaşık bir takılma ve 7 kez 3+ kare kaybı demek. WM_NULL sondası p95 13,1 / p99 26,9 / max 55-86 ms. Kare aralığının kendisi (CompositionTarget.Rendering farkları) hâlâ ölçülmedi. 2 mantıksal işlemcide tepsi rebuild'inde App 912 Mcycles/s.

**Çözüm:** Sirayla: (a) ONCE gizli modda ana UI isini kapat — D7-1 (gizli pencerede satir nefesi/graf beads/serit supurmesi) ve D7-3 (200 ms tick'in gizliyken graf+liste beslemesi) duzeltmeleri; tek kapi IsVisible. Arti: mimariyi bozmaz, hem CPU'yu hem gostergeyi kurtarir; pencere geri gelince durum VM'den tek seferde yeniden okunur (butunluk). Eksi: gostergeyi motor olaylarinin (proje basina ~5-8 PropertyChanged) isinden kurtarmaz. Is: orta. (b) Yetmezse gostergeyi KENDI STA thread'inde kostur (Dispatcher.Run, IsBackground=true). Arti: ana thread ne yaparsa yapsin clock'lar akar. Eksi/tuzaklar: uretimde ayri kaynak kapsami yukleyicisi YOK — DsResources.NewScope test yardimcisidir (tests/BuildOrchestrator.Tests/App/DsResources.cs:75-80); ctor'daki resourceScope parametresi (TrayBuildOverlayWindow.xaml.cs:49-55) kapisi hazir ama Tokens.xaml + BrandGeometry.xaml o thread'de ayrica yuklenmeli, cunku Brush.Brand.Chevron (BrandGeometry.xaml:46-50) DynamicResource durakli oldugu icin dondurulamaz ve Application kaynagindaki ornek baska thread'den kullanilamaz; controller'in dort fiili overlay dispatcher'ina BeginInvoke, BeginExit geri cagrisi ve RestoreRequested ana dispatcher'a BeginInvoke olmali; OnClosed'da (MainWindow.xaml.cs:1431) Close yerine o dispatcher'in InvokeShutdown'u gerekir; MotionGate/Duration.Slow okumasi ana thread'de kalmali. WPF'te render thread surec basina TEKTIR — ayri dispatcher yalniz tick'i kurtarir. Is: uzun, risk orta-yuksek. (c) cizim maliyetini dusurmek: yuzey 12,6k-28k px (N2-2) — olcum kucuk cikarsa uygulanmaz. (d) N2-4.

**Çözüm (doğrulayıcı düzeltmesi):** (a) doğru ilk adım ve ölçüme göre yeterli olması beklenir: gizli mod layout/render geçişlerinin kaynağını keser (konsol belgesi %18,6, liste, şerit chip'leri, akış, graf). (b) ayrı dispatcher thread'i ŞİMDİ gerekli değil: (1) en uzun dilimlerin ilk işi render/layout geçişi ve BuildMenu.BuildRow — gizli modda kalkıyor; kalan olay başına iş en kötü 10,0 ms ölçüldü (bütçe testi), kare bütçesinin altında. (2) UI thread aktif süresinin %23,4'ü GC bekleme — GC duraklaması ayrı bir managed dispatcher thread'ini de durdurur, (b) bunu çözmez. (3) WPF render thread'i süreç başına tek. (4) Risk yüksek: üretimde ayrı kaynak kapsamı yükleyicisi yok, dört fiil + geri çağrı çapraz-thread olur. Karar: gizli moddan sonra kare aralığı ölçülür; p95 hâlâ 66 ms'yi aşıyorsa (b) yeniden açılır.

**Değişmezler:** Planlama Core'da, controller WPF'siz kalir (TrayBuildIndicatorControllerTests.The_controller_carries_no_wpf_type). §14.5: gorunmeyen saat donmez kurali korunur. Sanat cizelgesi (KeyTime/KeySpline) degismez.

**Test fikri:** (a) icin: pencere gizliyken 200 ms tick'in PushGraphStatuses cagirmadigini pinleyen test (D7-3 ile ortak). (b) icin: overlay'in Dispatcher'i != Application.Current.Dispatcher ve BeginExit geri cagrisinin ana thread'de geldigini pinleyen STA testi.

**Doğrulayıcı notu:** Kod doğrulandı: overlay ana thread'de yaratılır (MainWindow.xaml.cs:1256-1262), src altında Dispatcher.Run / SetApartmentState yok; motor olayları ve konsol batch'i Dispatcher.InvokeAsync (Normal) ile gelir (339-343, 579). Ölçüm artık iddiayı destekliyor ama NEDENİ düzeltiyor: gösterge kare atlıyorsa bunun ana kaynağı motor olaylarının işi değil, gizli ana pencerenin kendi layout/render geçişleri — UI thread aktif süresinin %48,5'i geçersizleşme-kaynaklı, %30,9'u animasyon-kaynaklı render geçişi, uygulama metotları ~%12. Overlay ile ana pencere aynı MediaContext'i paylaştığı için göstergenin karesi, ana pencerenin bekleyen layout'u bitmeden üretilemez.

#### N2-2 — Gostergenin eldeki TEK olcumu bugunku gostergeyi olcmuyor: farkli olcek (2/3 → 0,5), sayim araci kare hizini kendisi yukseltiyor, CPU test host'unun toplami, yuk altinda ve yazilim render'da olcum yok

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) tests/BuildOrchestrator.Tests/App/TrayOverlayMeasurementTests.cs:80-108 (Sample)
- `tests/BuildOrchestrator.Tests/App/TrayOverlayMeasurementTests.cs` satır 73-98 — Sample: CompositionTarget.Rendering sayaci + Process.TotalProcessorTime (tum test sureci), 8 s pencere
- `.claude/outputs/2026-09-13-15-39-tray-indicator-round-2-results.md` satır 20, 62-64 — Olculen sayilar: statik 32,1 kare/sn %8,0; dongu 62,3 kare/sn %11,7; fark +30,2 kare/sn, +3,7 puan (Release, bos makine, FullPower profilli gelistirici makinesi)
- `src/BuildOrchestrator.App/Views/TrayBuildOverlayWindow.xaml.cs` satır 37-39 — Scale = 0.5 → 187,5 x 67 DIP (olcum aninda 2/3 → 250 x 89 DIP; commit 49fb6f5 ve a3d46c3 sonradan kucultu)

**Kanıt:** Aktarilan olculmus sayilar: 2026-09-13, Release, bos makine, 8 s ornek — statik kare 32,1 kare/sn ve tek cekirdegin %8,0'i; dongu 62,3 kare/sn ve %11,7; fark +30,2 kare/sn ve +3,7 puan. 2026-09-12 v2 ve 2026-08-20 sonuc dosyalarinda kare hizi/CPU olcumu YOK (yalniz geometri pinleri). Gecersizlik nedenleri (koddan): (1) olcum Scale=2/3 iken alindi (git 5892506), bugun 0,5 — yuzey alani 0,5^2/0,667^2 = %56'sina indi. (2) Rendering aboneligi render dongusunu kendisi tetikler: saat kurulmayan 'statik' ornek 32,1 kare/sn, DesiredFrameRate=30 olan dongu 62,3 kare/sn sayiyor — sayilan sey gostergenin karesi degil (sonuc dosyasi 63-64 bunu kabul ediyor). (3) TotalProcessorTime xUnit host'unun tum thread'lerini kapsar. (4) Yuk altinda (tepside build), %125/%150 olcekte ve yazilim render'da (Tier 0) hic ornek yok. Bugunku yuzey koddan: 187,5x67 DIP = %100'de ~188x67 px (12,6k px, ~50 KB/kare), %125'te ~234x84 (19,7k px, ~79 KB), %150'de ~281x101 (28,4k px, ~114 KB); 30 fps'te 1,5 / 2,4 / 3,4 MB/s.

**Etki:** '+3,7 puan' ne ust ne alt sinir olarak kullanilabilir; N2-1 (b)/(c)/(d) kararlari ve D7-5 bu sayiya dayanamaz. Olcum olmadan golge/pencere optimizasyonu yapmak kor ucus olur.

**Etki (doğrulayıcı düzeltmesi):** Güncel ölçüm (2026-10-02, boş makine, Release): statik 32,2 kare/sn ve tek çekirdeğin %3,7'si; döngü 59,5 kare/sn ve %10,2; fark 27,3 kare/sn ve 6,5 puan. Bulgudaki 32,1/%8,0 — 62,3/%11,7 — +3,7 puan eski (Scale 2/3) ölçümdür. 6,5 puan, Rendering aboneliğinin şişirdiği bir ÜST sınırdır.

**Çözüm:** Ayni test dosyasini genislet (yeni dosya acma, kopya yasak): (1) CPU ornegi Rendering aboneligi OLMADAN alinsin; kare sayimi ayri gecis. (2) Surec toplami yerine UI thread + render thread icin QueryThreadCycleTime (ortak baglamdaki olcum yontemiyle ayni birim: Mcycles/s). (3) Ucuncu ornek: HwndSource.CompositionTarget.RenderMode = SoftwareOnly ile ayni dongu (Tier 0 maliyetini bu makinede verir). (4) Dorduncu ornek: gercek kosuda ShowLoop vs ShowStatic (reduced-motion acik) 10 s pencere farki — verify-D7'nin istedigi ayristirma.

**Değişmezler:** Olcum testi kurali: [SkippableFact] + ilk satirda Skip.IfNot(BO_MEASURE_OVERLAY == 1), STA govdesi StaThread.RunAsync (CLAUDE.md, ARCHITECTURE §17.5). Esik/pin eklenmez; okuma testidir.

**Test fikri:** Okuma testi; pin degil.

**Doğrulayıcı notu:** Dört gerekçeden biri kapandı, üçü duruyor. Kapanan: ölçek — test 2026-10-02'de bugünkü Scale=0,5 ile yeniden koşuldu (tests-overlay.txt). Duranlar (TrayOverlayMeasurementTests.cs:80-108): (2) CompositionTarget.Rendering aboneliği render döngüsünü kendisi uyanık tutuyor — 30 fps'lik döngü 59,5 kare/sn, saat kurulmayan statik örnek 32,2 kare/sn sayıyor; (3) CPU = Process.TotalProcessorTime, yani xUnit host'unun tamamı; (4) yük altında ve yazılım render'da örnek yok.

#### N2-3 — Cikis evresi surerken yeni kosu baslarsa: gosterge kosu ortasinda kapanir, balon yanlis satiri ('Starting...') tasir ve ikinci kosunun sonuc balonu hic cikmaz

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` satır 94-103 — SetPhase: enteringRun → _notified=false; _exitPending'e bakmaz
- `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` satır 124-131 — Apply: gizli+aktif ve _shown ise return — ucustaki cikis iptal edilmez
- `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` satır 170-186 — CompleteExitAsync: kosulsuz HideNow, _notified=true, o anki _terminal ile balon
- `src/BuildOrchestrator.App/Services/TrayIndicatorBinder.cs` satır 51-59 — Route: her sinyalde once PushLine — yeni kosunun satiri eski kosunun 'terminal' satirini ezer
- `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml.cs` satır 86-90, 132-140 — RequestFinish bayragi tur sonuna kadar yasar; tur bitince Teardown + onFinished

**Kanıt:** Iz (tepside): kosu1 biter → Phase=Done → Apply → StartExit (_exitPending=true, gostergede _finishing=true). Tur bitmeden (en cok 3 s, Storyboard Duration 0:0:3) kisayolla yeni Build: Route once PushLine ('▸ Starting — resolving what to build'), sonra SetPhase(Starting) → enteringRun → _notified=false → Apply → '_shown ise return'. Tur bitince OnIterationCompleted → onFinished → CompleteExitAsync: _shown=false + view.HideNow() (kosu2 aktifken gosterge gizlenir; ancak Starting→Running gecisinde Apply yeniden Show eder), nefesten sonra _notified=true ve notifier.ShowRunFinished(_terminal) — _terminal artik kosu2'nin satiri. Kosu2 bittiginde enteringRun bir daha olusmadigi icin _notified true kalir → 'if (_notified) return' → kosu2'nin balonu cikmaz. TrayBuildIndicatorControllerTests'te bu senaryoyu pinleyen test yok (test adlari: satir 112-415; 'Starting_reverting_to_idle...' farkli yol).

**Etki:** Birincil senaryoda (tepside kisayolla build) Stop → hemen Build ya da hizli biten kosu → hemen yeniden Build durumunda: gosterge yanip soner, yanlis metinli bildirim gelir, asil sonuc bildirimi kaybolur. Pencere: kosu bitisinden sonraki <=3 s + Duration.Slow (280 ms).

**Etki (doğrulayıcı düzeltmesi):** Pencere: koşu bitişinden sonraki en çok 3 s (tur süresi) + nefes. Tetikleyen: tepside biten koşudan hemen sonra kısayolla yeni Build. Performans etkisi yok; birincil senaryoda yanlış metinli balon + kaybolan sonuç balonu + koşu boyunca görünmeyen gösterge.

**Çözüm:** Controller'da (tek yer): Apply'in 'goster' dalinda _exitPending ise cikisi IPTAL et — view'a 'bitisi geri al' fiili (TrayBuildIndicator'da _finishing=false, _onFinished=null; dongu kesilmeden surer) ve _exitPending=false; onceki kosunun balonu ya o anda (dondurulmus satirla) verilir ya da bilerek dusurulur (urun karari). Bitis satiri, faz aktif kumeye geri girdigi anda dondurulmali: binder'in Route sirasi (PushLine once) yuzunden controller yeni kosunun satirini almadan once 'yeni kosu' bilgisini gormeli — or. SetTerminalLine yalniz faz aktif degilken ya da aktif→terminal gecisinde kabul edilir. CompleteExitAsync, cagrildigi anda faz yeniden aktifse HideNow yapmamali.

**Değişmezler:** K-4 (pencere donunce aninda gizle, taahhut korunur), K-5 (metin controller'da uretilmez), K-10 (dongu yarida kesilmez), tek balon butcesi. Kirmizi test kurali: once kirmizi.

**Test fikri:** Controller testi: gizli+Running → Done → (FinishExit cagirmadan) Starting → Running → FinishExit; beklenen: HideNow log'da yok, ShowRunFinished 'Starting' satiriyla cagrilmamis; sonra Done → FinishExit → kosu2 balonu tam bir kez.

**Doğrulayıcı notu:** İz kodda adım adım doğrulandı ve bulgudan daha kötü bir dal var. Çıkış beklerken (_exitPending) yeni koşu: Route önce PushLine ile _terminal'i ezer (TrayIndicatorBinder.cs:51-59), SetPhase enteringRun ile _notified=false yapar (controller 94-103), Apply '_shown ise return' der (126-131). Tur bitince CompleteExitAsync koşulsuz HideNow + _notified=true + yeni koşunun satırıyla balon (170-186). Ek dal: koşu2 o anda zaten Running ise bir sonraki faz değişimine (Stopping/Done) kadar Apply hiç çağrılmaz — gösterge koşu2 boyunca HİÇ geri gelmez; Done'da 'if (!_shown) return' (133) çıkışı da balonu da atlar. Koşu2 hâlâ Starting'deyse Running geçişinde geri gelir.

#### N2-4 — Yazilim render (Tier 0: RDP, VM, surucusuz/eski GPU) ve pil durumunda dongu aynen oynuyor — statik kareye dusen kapi yalniz ClientAreaAnimation

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1240-1241, 1251-1252 — Controller'a giden tek hareket sinyali MotionGate.StaticAnimationsEnabled
- `src/BuildOrchestrator.App/Services/SystemParametersMotionSignal.cs` satır 18, 36-37 — Sinyal = SystemParameters.ClientAreaAnimation; baska girdi yok
- `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml` satır 213-217, 252-254 — Iki DropShadowEffect (BlurRadius 4.8 ve 5.6), ikisi de hareket eden ogelerin ustunde; RenderingBias varsayilan (Quality)
- `src/BuildOrchestrator.App/Views/TrayBuildOverlayWindow.xaml` satır 5-9 — AllowsTransparency=True (katmanli pencere), Topmost

**Kanıt:** src altinda RenderCapability, RenderMode, IsRemoteSession, PowerLineStatus, SystemEvents aramasi SIFIR sonuc (D7-9 genel halini kaydetti). Dongu yapisi (koddan sayildi): 15 keyframe animasyonu — 8 TranslateTransform.X, 6 Opacity, 1 Clip (ObjectAnimationUsingKeyFrames) — Duration 3 s, 30 fps (MotionTokens.DecorativeFrameRate, MotionTokens.cs:32); 3 s'nin 0,76 s'si (1,34-2,10) durus, degerler sabit. Efektler: serit katmaninda 5 hareketli Path'i saran Canvas'ta 1, sevron Path'inde 1; BitmapCache/CacheMode yok; gradient yalniz sevron dolgusu (BrandGeometry.xaml:46-50); maske girisin ilk 1,15 s'sinde animasyonlu transform altinda Clip. Tier 0'da efektler, clip ve katmanli yuzey UI/render thread'inde CPU ile cizilir; maliyeti olculmedi.

**Etki:** Zayif/GPU'suz makinede gostergenin tum cizim maliyeti CPU'ya, yani derlemenin kullandigi cekirdeklere duser. Buyukluk bilinmiyor — N2-2'nin SoftwareOnly ornegi verir. Yuzey kucuk oldugu icin (12,6k-28k px) mutlak maliyet dusuk olabilir; olculmeden duzeltme yapilmamali.

**Etki (doğrulayıcı düzeltmesi):** Bilinmiyor. Eldeki tek sayı donanım hızlandırmalı makinede döngü-statik farkı 6,5 puan (üst sınır). Ölçülmeden düzeltme yapılmamalı — bulgunun kendi koşulu geçerli.

**Çözüm:** Olcum (N2-2 madde 3) anlamli cikarsa: controller'in ZATEN var olan statik yolu kullanilir — MainWindow.xaml.cs:1240/1252'de SetAnimationsEnabled'a giden deger 'MotionGate.StaticAnimationsEnabled && (RenderCapability.Tier >> 16) > 0' olur; RenderCapability.TierChanged ayni handler'a baglanir. Karar tek yerde (kucuk saf yardimci, test edilebilir). Pil icin ayri kural onermiyorum: olcum yok. RenderingBias=Performance yalniz yazilim yolunda fark eder (verify-D7); sanata dokunmaz, ama yine olcumle.

**Değişmezler:** §14.5 kural 2: reduced motion OS sinyalidir, uygulama toggle'i yok — bu ek kapi kullanici ayari degil donanim durumudur; ARCHITECTURE §14.5'e islenmeli. Statik kare ayri kompozisyon degildir (Stop = durus karesi).

**Test fikri:** Saf karar fonksiyonu (animationsEnabled, tier) → bool icin tablo testi; controller testi 'Reduced_motion_shows_the_static_frame' zaten statik yolu pinliyor.

**Doğrulayıcı notu:** Yokluk doğrulandı: src altında RenderCapability, RenderMode, IsRemoteSession, PowerLineStatus, SystemEvents araması boş; tek hareket sinyali SystemParameters.ClientAreaAnimation (SystemParametersMotionSignal.cs:18). İki DropShadowEffect doğru (TrayBuildIndicator.xaml:216, 253). Etkinin büyüklüğü hâlâ ölçülmedi — yazılım render örneği yok.

#### N2-5 — Gosterge ekrandayken calisma alani degisirse yeniden konumlanmiyor — konum yalniz Reveal aninda hesaplaniyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Views/TrayBuildOverlayWindow.xaml.cs` satır 111-119 — Reveal: SystemParameters.WorkArea bir kez okunur, Left/Top yazilir, Show
- `src/BuildOrchestrator.App/Views/TrayBuildOverlayWindow.xaml.cs` satır 75-77 — Place: saf, parametreli — yeniden kullanilabilir
- `src/BuildOrchestrator.App/Services/SystemParametersMotionSignal.cs` satır 14, 36-40 — Mevcut kod, degismez: StaticPropertyChanged aboneligi var ama yalniz ClientAreaAnimation'i filtreler

**Kanıt:** Overlay'de SystemParameters.StaticPropertyChanged / DisplaySettingsChanged / WM_SETTINGCHANGE / DpiChanged aboneligi yok (grep: tek StaticPropertyChanged abonesi SystemParametersMotionSignal; DpiChanged yalniz MaximizeFix.cs:63). Gosterge kosu boyunca ekranda kalir (olculen Rebuild 142 s); bu surede cozunurluk degisimi, dock/undock, gorev cubugunun tasinmasi, RDP baglanma/kopma olursa pencere eski koordinatta kalir. Gorev cubugu ust/sol/sag: Place calisma alanindan turedigi icin Reveal aninda dogru (XML doc 72-74). Birincil ekran: bilincli sinir (ARCHITECTURE §12.3).

**Etki:** Kosu ortasinda ekran duzeni degisirse gosterge ekran disinda ya da gorev cubugunun altinda kalabilir; bir sonraki kosuda duzelir. Performans etkisi yok.

**Çözüm:** Overlay gorunurken (IsVisibleChanged ile ac/kapa) SystemParameters.StaticPropertyChanged'te PropertyName == WorkArea icin ayni Place hesabini yeniden uygula (Reveal'in konum kismi tek ozel metoda cikar; Show cagirmadan). Gizliyken abonelik birakilir.

**Değişmezler:** Konum hesabi tek yerde (Place); DPI hesabi elle yapilmaz (DIP). Birincil ekran karari degismez.

**Test fikri:** TrayBuildOverlayWindowTests: yeniden konumlama metodunu farkli Rect ile cagirip Left/Top'u Place sonucuyla karsilastiran STA testi.

**Doğrulayıcı notu:** Doğru: konum yalnız Reveal'de hesaplanır (TrayBuildOverlayWindow.xaml.cs:111-119); StaticPropertyChanged'in tek abonesi motion sinyali ve yalnız ClientAreaAnimation'ı süzer (SystemParametersMotionSignal.cs:14, 36-42); DpiChanged yalnız MaximizeFix.cs:63.

#### N2-6 — Topmost gosterge tam ekran uygulama / sunum / ekran paylasimi sirasinda da cikiyor — kullanici durumu sorgulanmiyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Views/TrayBuildOverlayWindow.xaml` satır 9 — Topmost=True
- `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` satır 124-131 — Gorunurluk kurali yalniz 'pencere gizli VE faz aktif'
- `src/BuildOrchestrator.App/Shell/Win32.cs` satır 38-60 — Overlay ex-style kumesi; SHQueryUserNotificationState tanimi yok

**Kanıt:** src altinda SHQueryUserNotificationState aramasi sifir sonuc. Balonlar OS tarafindan sunum/odak modunda bastirilir, gosterge ise kendi Topmost penceresidir ve bastirilmaz. Tam ekran D3D uygulamasinin ustunde animasyonlu katmanli pencerenin o uygulamaya maliyeti olculmedi.

**Etki:** Sunum yaparken ya da tam ekran video/oyun acikken sag altta animasyon gorunur. Siklik: dusuk; gelistirici senaryosunda (VS acik) etkisiz.

**Çözüm:** Reveal'den once SHQueryUserNotificationState sorulur (P/Invoke Win32.cs'e, karar saf yardimciya): QUNS_BUSY / QUNS_RUNNING_D3D_FULL_SCREEN / QUNS_PRESENTATION_MODE ise gosterge acilmaz (controller'a 'ekran musait' ucuncu girdi olarak; balon akisi aynen surer). Urun karari — kullaniciya sorulmali.

**Değişmezler:** Win32.cs ince kalir (karar yok); gorunurluk karari controller'da tek yerde.

**Test fikri:** Controller testi: screenAvailable=false iken gizli+Running → ShowLoop cagrilmaz; Done → balon yine bir kez.

**Doğrulayıcı notu:** Doğru: Topmost=True (TrayBuildOverlayWindow.xaml:9), görünürlük kuralı yalnız 'pencere gizli VE faz aktif' (controller 124-131), SHQueryUserNotificationState araması boş. Ürün kararı; performans bulgusu değil.

#### N2-7 — Oturum kilitliyken / ekran kapaliyken dongu donmeye devam ediyor — gorunmeyen saat kuralinin (§14.5) oturum duzeyi karsiligi yok

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml.cs` satır 51-54, 142-147 — Tek kapi IsVisible; pencere Show edilmis oldugu surece kilit ekraninda da true kalir
- `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` satır 66-72 — Girdiler: _mainVisible, _phase, _animationsEnabled — oturum/ekran durumu yok

**Kanıt:** src altinda SystemEvents / SessionSwitch / PowerModeChanged aramasi sifir sonuc. 'Build baslat, ekrani kilitle' senaryosunda gosterge kosu boyunca 30 fps tick eder; maliyeti olculmedi.

**Etki:** Kilitli oturumda kimsenin gormedigi animasyon icin UI thread tick'i + katmanli yuzey kopyasi. Buyukluk bilinmiyor; N2-2 olcumundeki dongu-statik farki kadar (ust sinir).

**Çözüm:** N2-2 farki anlamliysa: SystemEvents.SessionSwitch (SessionLock/SessionUnlock) controller'in mevcut SetAnimationsEnabled girdisine N2-4'teki ayni saf karar fonksiyonundan beslenir (kilitliyken statik kare, acilinca dongu). Ayri mekanizma yazilmaz.

**Değişmezler:** §14.5 gorunmeyen saat kurali; controller WPF'siz.

**Test fikri:** N2-4'teki karar fonksiyonu tablosuna 'sessionLocked' satiri.

**Doğrulayıcı notu:** Kodun oturum/ekran durumunu sorgulamadığı doğru (SessionSwitch/PowerModeChanged araması boş). Ama kilitli oturumda WPF'in kare üretmeye devam edip etmediği ve maliyeti için hiçbir kanıt yok — bulgunun kendisi de 'tahmin'. Ölçüm olmadan iş listesine girmemeli.

#### N2-8 — TrayIndicatorBinder her RunViewModel.PropertyChanged'de serit satirini yeniden derliyor — pencere gorunurken, bostayken ve gosterge kapaliyken de; satir yalniz balon aninda gerekli

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Services/TrayIndicatorBinder.cs` satır 42-64 — OnChanged → Route → PushLine: property adi secilmez, her sinyalde vm.RibbonLine okunur
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 626-633 — RibbonLine onbelleksiz getter: her okuma RibbonText.Compose (string.Format + DurationFormat + EtaSuffix)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1854 — ElapsedMs kosarken 200 ms'de bir yazilir (en az 5 sinyal/s)
- `src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs` satır 122, 185 — _terminal yalniz CompleteExitAsync sonunda okunur

**Kanıt:** Koddan: binder aboneligi uygulama omru boyunca acik ve kapisiz; RunViewModel'in her ObservableProperty yazimi (ElapsedMs, Counters, FinishedOfWillBuild, EtaMs, EtaText, StreamEventCount, ActiveLine*, Phase...) bir Compose tetikler. Ekrandaki serit ayni Compose'u ayrica yapar (binder XML doc 29-31). Sinyal sikligi olculmedi; alt sinir kosarken 5/s (ElapsedMs). Compose basina maliyet olculmedi — tek string.Format mertebesi; dotnet-trace'te ayri bir kalem olarak gorunmuyor.

**Etki:** CPU etkisi kucuk (tahmin: saniyede onlarca kisa string bicimleme + cop). Asil deger: N2-3'un kok nedenlerinden biri bu 'her sinyalde ez' modeli. Tek basina performans kazanci icin yapilmaz.

**Etki (doğrulayıcı düzeltmesi):** Ölçüldü: örnekleme çözünürlüğünün altında.

**Çözüm:** N2-3 ile birlikte: controller satiri ITILEN deger yerine CEKILEN deger olarak alir — Func<RibbonLine> (binder vm.RibbonLine'i verir) ve controller onu (i) cikis baslarken degil, (ii) balon aninda, ama faz yeniden aktif olmadiysa okur; faz yeniden aktif olduysa N2-3'teki dondurulmus satir kullanilir. Binder'da yalniz Phase dali kalir. Binder'in 'yarim cumle' gerekcesi (Phase'den sonra Counters gelir) cekme modelinde kendiliginden cozulur: okuma en az Duration.Slow sonra yapilir.

**Değişmezler:** K-5: metin controller'da uretilmez, seridin satiridir (tek kaynak RunViewModel.RibbonLine). Davranis degisince TrayIndicatorBinderTests yeni kurali pinleyecek sekilde yeniden yazilir (sessizce silinmez).

**Test fikri:** Binder testi: Phase disi 100 PropertyChanged → RibbonLine getter 0 kez okunur (sayac sahte VM yerine Func sarmalayicisinda).

**Doğrulayıcı notu:** N1-10 ile aynı bulgu. Kod doğru ama tepsi rebuild trace'inde RibbonText.Compose / get_RibbonLine / TrayIndicatorBinder çerçeveleri hiç örneklenmedi (25 s). Performans değeri yok; yalnız N2-3'ün kök nedenlerinden biri ('her sinyalde ez') olarak anlamlı.

#### N2-9 — Overlay penceresi ilk gosterimde, yani kisayolla Build'in baslatildigi anda UI thread'inde kuruluyor (XAML + HWND + katmanli yuzey) — maliyeti olculmemis

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1256-1262, 1271-1275 — LazyOverlayView: ShowLoop/ShowStatic ilk cagrida new TrayBuildOverlayWindow()
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 979 — Phase = Starting — tepsideyken gostergeyi acan ilk sinyal, Build komutunun icinde

**Kanıt:** Yasam dongusu (koddan): surec basina TEK ornek (_trayOverlay alani, MainWindow.xaml.cs:50); ilk gercek gosterimde yaratilir, sonraki kosularda ayni HWND Hide/Show edilir, her build'de yeni HWND YOK; yalniz MainWindow.OnClosed'da kapanir (1431). Kapaliyken bellekte kalan: bir HWND + gorsel agac (1 Viewbox, 5 Canvas, 6 Path, 2 efekt) + Storyboard kaynagi; saat yok (Teardown → _loop.Remove). Ilk kurulumun suresi olculmedi.

**Etki:** Yalniz oturumun ILK tepsi build'inde, Build komutuyla ayni UI turunda tek seferlik gecikme. Buyukluk bilinmiyor.

**Çözüm:** Once olc (Stopwatch: EnsureTrayOverlay + ilk Reveal). 30 ms'yi asiyorsa: pencere ilk kez tepsiye indiginde (Hide sonrasi ApplicationIdle oncelikli tek BeginInvoke) onceden kurulur — acilis suresine ve hic tepsiye inmeyen kullanicinin bellegine dokunmaz. Asmiyorsa dokunma.

**Değişmezler:** D2-8/D8-9 tembel kurulum yonu korunur (acilista eager kurulum yok).

**Doğrulayıcı notu:** Yaşam döngüsü doğru: tek örnek, ilk ShowLoop/ShowStatic'te yaratılır (MainWindow.xaml.cs:1256-1262, 1271-1275), sonraki koşularda aynı HWND. İlk kurulum süresi hâlâ ölçülmedi: J senaryosu (tepsiden Build 5,68 s) ilk gösterim değildi — overlay önceki tepsi koşularında zaten kurulmuştu.

**Doğrulayıcının eklediği noktalar:**
- Overlay ile gizli ana pencere aynı dispatcher'ı ve aynı MediaContext'i paylaşıyor (MainWindow.xaml.cs:1256-1262): göstergenin 30 fps'lik saati her karede ana pencerenin bekleyen layout'unu da işletiyor — ölçülen 'animasyon-kaynaklı render geçişi' 3522 ms (UI aktif süresinin %30,9'u); en uzun dilim (327 ms) bu yoldan başladı. Bulgu bunu 'Normal öncelikli işler öne geçer' diye açıkladı; ölçülen asıl mekanizma bu bağlaşım.
- GC duraklaması gösterge için ayrı bir sınır: tepsi rebuild'inde UI thread aktif süresinin %23,4'ü (2670 ms) GC bekleme, Finalizer thread'i 25 s'nin 23,3 s'inde meşgul (DWrite sarmalayıcıları). Kaynağı metin biçimlemesinin ayırmaları; gösterge hangi thread'de koşarsa koşsun bu duraklamaları yer.
- src/BuildOrchestrator.App/Services/TrayBuildIndicatorController.cs:124-143 — Apply yalnız faz ya da görünürlük DEĞİŞİMİNDE çağrılıyor; '_shown' ile gerçek durum ayrışırsa (N2-3'teki dal) bir sonraki değişime kadar kendini düzeltecek bir yol yok.

**Temiz bulunan alanlar:**
- Yasam dongusu: tek overlay ornegi, tembel kurulum, her build'de yeni HWND yok; ana pencere gorunurken gosterge HIC yok (controller Apply 126-142); pencere geri gelince aninda HideNow + saat sokulur (TrayBuildOverlayWindow.xaml.cs:103-109).
- Gorunmeyen saat: IsVisibleChanged → StopNow, Teardown gercekten _loop.Remove(this), gorunmezken yeni tur acilmaz (TrayBuildIndicator.xaml.cs:54, 114-118, 142-147). Kosu disinda gosterge icin calisan zamanlayici yok; controller'in tek beklemesi cikis sonrasi Task.Delay(Duration.Slow) (MainWindow.xaml.cs:1236).
- Reduced motion: dongu hic kurulmaz (ShowStatic = StopNow, durus karesi taban degerler); cikis evresi atlanir, nefes token'dan 0'a duser; OS ayari kosu sirasinda degisirse yerinde takas (controller 111-117, 153-157, 164-165).
- Show notifications kapali: yalniz ShowRunFinished atlanir, gosterge fiilleri ve nefes aynen (controller 181-185); deger balon aninda taze okunur.
- Faz kapsami: yalniz Starting/Running/Stopping; Syncing, Idle, Done, Stopped gostergeyi acmaz (controller 84-85; testler 154, 168). Clean/Sync gostergeyi acmaz — bilincli sinir.
- Animasyonlu ozellikler yalniz TranslateTransform.X ve Opacity (+1 ayrik Clip takasi); layout animasyonu yok; DesiredFrameRate tek sabitten (MotionTokens.cs:32 = 30); BlurEffect, VisualBrush, OpacityMask yok.
- Tur basina yeniden Begin: 3 s'de bir 15 clock'luk agac kurulur (5 clock/s) — ihmal edilebilir; sonsuz dongu yerine tek gecis karari (§14.5) dogru ve test pinli.
- Yuzey boyutu kucuk: 187,5x67 DIP (12,6k px %100 — 28,4k px %150); pencereyi daraltmak ya da clip yerine opacity mask'e gecmek icin olculmus bir gerekce yok.
- Odak/Alt-Tab: ShowActivated=False + WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE tek sabitten (Win32.cs:60); tiklama gecirgenligi katmanli pencerenin alfa testinden, ek hit alani yok.
- DPI: olculer DIP, PerMonitorV2 manifestte (app.manifest:7); gorev cubugu ust/sol/sag icin Place calisma alanindan turer (TrayBuildOverlayWindow.xaml.cs:75-77).
- Kapanis: overlay MainWindow.OnClosed'da kapatilir (1431), AnimationsEnabledChanged aboneligi birakilir (1430).

**Açık sorular:**
- Dokuman ile sonuc kaydi: 2026-09-13 sonuc dosyasi overlay'i Scale=2/3 (~250x89 DIP) diye anlatir, kod Scale=0.5 (187,5x67). ARCHITECTURE §12.3 olcu yazmadigi icin dokuman-kod celiskisi yok; .claude/outputs tarihseldir, duzeltilmez — yalniz olcumun gecersizligi icin kayit (N2-2).
- Kosu sonu kendiliginden Sync (AutoSyncCoordinator 'end of run' tetigi) cikis evresi + nefes penceresinde (<=3,3 s) BuildPreview getirirse AllClean true olur ve Done satiri 'Completed — ...' yerine 'Everything up to date — N projects checked ...' okunabilir (RibbonText.cs:221-226; binder her sinyalde ezer). Bu pencerede gercekten oluyor mu dogrulanmadi — tepside biten bir kosunun balon metni ile serit metni kayda alinmali.
- 30 fps tavani: kullanici 'cok akiskan' istiyor; §14.5 dekoratif tavan 30 fps. Round-2 plani bunu 'hareket basamakli gorunurse AYRI karar' diye birakmis. 60 fps'e cikmak tick maliyetini kabaca iki katina cikarir — N2-2 olcumunden sonra kullanici karari.
- Otomatik gizlenen gorev cubugu: WorkArea tam ekran olur, gosterge ekran altindan 12 DIP yukarida durur; gorev cubugu acildiginda ikisi de topmost — hangisi ustte kalir, canli denenmeli.
- Coklu sanal masaustu: WS_EX_TOOLWINDOW'lu, sahipsiz Topmost pencerenin diger masaustlerinde gorunup gorunmedigi canli denenmeli.
- Birincil ekran olcegi oturum icinde degistirilirse (logoff'suz): SystemParameters.WorkArea sistem DPI'siyla, pencere boyutu monitor DPI'siyla olceklenir; Place'in cikardigi genislik ile gercek piksel genisligi ayrisabilir (sag kenardan tasma). Canli denenmeli.
- Ana pencere MINIMIZE iken (tepside degil) gosterge cikmaz, cunku IsVisible true kalir (D7-2 ile ayni kok). Istenen davranis bu mu?
- RDP oturumunda ClientAreaAnimation genellikle kapali gelir mi (o zaman statik yol kendiliginden devreye girer) — hedef makinelerde okunmali.


### N3-ui-donma-kaynaklari

#### N3-1 — IPC olaylari ve konsol batch'leri UI thread'ine Normal oncelikle, birlestirilmeden ve geri basincsiz giriyor: olay yagmuru Render ve Input'u bekletir

| Alan | Değer |
|---|---|
| Önem | kozmetik (mekanizma dogru, olculen girdi gecikmesi kucuk) |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: orta |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) MainWindow.xaml.cs:342, 349, 580, 598-606; Console/ConsoleBatcher.cs:129-164; ViewModels/RunViewModel.cs:1914-1974
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 339-343 — ProjectLog disindaki HER IPC olayi kendi Dispatcher.InvokeAsync(() => _vm.OnEvent(ev)) turu; oncelik verilmiyor = DispatcherPriority.Normal
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 576-581 — Konsol pompasi: batch basina Dispatcher.InvokeAsync(() => AppendConsoleBatch(...)) — yine Normal; donen DispatcherOperation beklenmiyor
- `src/BuildOrchestrator.App/Console/ConsoleBatcher.cs` satır 129-164 — PumpAsync: flush(batch.ToString(), batchGen) senkron Action; UI'nin batch'i uyguladigini beklemeden 50 ms sonra bir sonrakini kuyruga atar
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 349 — AutoSync geri cagrilari da ayni yoldan (InvokeAsync, Normal)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1914-1974 — OnEvent: olay basina handler + AppendStreamFor + StartQueuedRunWhenWorkEnds; toplu/zaman dilimli isleme yok
- `tests/BuildOrchestrator.Tests/App/UiResponsivenessBudgetTests.cs` satır 37-41, 106-136 — Butce: tek olay 50 ms, tek blok 120 ms; olaylar testte senkron cagriliyor — dispatcher kuyrugu/oncelik etkisi olculmuyor

**Kanıt:** Kod: Dispatcher.InvokeAsync(Action) asiri yuklemesi DispatcherPriority.Normal (9) kullanir. WPF oncelik sirasi Send(10) > Normal(9) > DataBind(8) > Render(7) > Loaded(6) > Input(5) > Background(4); kuyrukta Normal is varken Render ve Input isleri calismaz. Proje olaylari icin ConsoleBatcher benzeri bir toplayici yok (grep: src/BuildOrchestrator.App altinda DispatcherPriority yalniz timer/popover/reveal'de geciyor, olay yolunda yok). Olculen kosuda (142 s, 152 proje derlendi) turetilen yuk: 23.047 log satiri 50 ms pencereyle en cok 20 flush/s = ust sinir ~2.840 Normal oncelikli AppendConsoleBatch turu (AvalonEdit Insert + TrimToRenderSlice + ScrollToEnd, ConsoleView.xaml.cs 274-319) + ~340 proje olayi (152 x started/succeeded + 35 skip + cerceve). Kosu basindaki pre-skip patlamasi (D6-2: 154 ardisik projectSkipped) art arda Normal turlar olarak kuyruga girer: hepsi bosalmadan tek kare cizilmez, tek tus/fare olayi islenmez. Olay basina sure icin eldeki tek sayi: test butcesi 50 ms, CI kosusunda tek proje olayi 71 ms olculmus (UiResponsivenessBudgetTests.cs 24-25). Konsol tarafinda geri basinc yok: UI thread T ms mesgul kalirsa T/50 adet batch kuyrukta birikir ve her biri AYRI belge guncellemesi olarak, yine Render'dan once uygulanir (duraklamayi uzatir). Olcumle tutarli: pencere gorunurken App 1,9-3,2 Gcycles/s (tek cekirdegin %60-100'u) — UI thread'i kosu boyunca neredeyse surekli dolu.

**Etki:** Donma hissinin yapisal nedeni: is miktari degil SIRASI. Zayif makinede olay basina sure uzadikca (CI'da 71 ms) ardisik Normal turlar kare cizimini ve girdiyi saniyelerce erteleyebilir; kullanici 'tikladim, tepki yok' / 'animasyon takildi' gorur. Kuyrukta bekleyen sure olculmedi (asagida nasil olculecegi yazili).

**Etki (doğrulayıcı düzeltmesi):** Olculen girdi gecikmesi: gorunur p95 7,5 ms / max 69,5 ms; tepsi p95 13,1 ms / max 55-86 ms. UI thread'in pompaya donmedigi en uzun dilimler (tepsi, 25 s): 327, 261, 233, 178, 168 ms — bunlarin ilk isi render/layout gecisi ya da BuildMenu.BuildRow, olay handler'i degil.

**Çözüm:** Yapi korunur (stdout NDJSON, planlama Core'da, ProjectLog marshal-free yolu aynen). (1) Tek olay kuyrugu: MainWindow.EventReceived non-log olaylari ConcurrentQueue<IpcEvent>'e yazar ve yalniz 'bosaltma planli degil' ise TEK bir DispatcherOperation planlar (D6-2 fix 2 ile ayni kuyruk). (2) Zaman butceli bosaltma: ilk dilim Normal oncelikte (seyrek tek olayda bugunkuyle ayni gecikme), dilim Stopwatch ile ~8 ms'yi asarsa kalan kuyruk DispatcherPriority.Loaded (6) ile yeniden planlanir — Render (7) araya girer, kare cizilir; Input hala olaylardan SONRA calisir, yani 'komut calistiginda tum olaylar uygulanmis' degismezi bozulmaz. Siralama garantisi: tek FIFO kuyruk + tek bosaltici (ayni anda iki bosaltma plani yok). (3) Dilim sonunda bir kez: RefreshRunSurface/UpdateEta/PushGraphStatuses dilim basina TEK kez (D6-2). (4) Konsol geri basinci: flush Func<string,long,Task> olur, pompa DispatcherOperation.Task'i bekler — UI mesgulken satirlar kanalda birikir ve tek buyuk batch olarak iner (belge 200 satir dilimiyle sinirli oldugu icin buyuk batch pahali degil); flush onceligi Loaded. (5) Ikinci adim (ayri karar): bosaltmayi Input (5) onceligine indirip her komut girisinde 'kuyrugu senkron bosalt' cagrisi — girdiyi de one alir ama davranis degisikligi, once (1)-(4) olculsun.

**Çözüm (doğrulayıcı düzeltmesi):** Onerinin (2) adimi (8 ms dilim + Loaded onceligi) olcumle desteklenmiyor, ertelensin. Ise yarayacak kisim: (3) turev guncellemelerin (RefreshRunSurface -> Counters/VisibleProjects -> serit chip'leri, BuildMenu satirlari) olay basina degil bosaltma basina tek kez yapilmasi ve (4) konsol geri basinci. Asil kaldirac 'missing' 1-2: pencere gizliyken gorunum guncellemesi yapilmamasi.

**Değişmezler:** ProjectLogEvent yolu marshal-free kalir (A13.2). Olay sirasi korunur (tek FIFO). Reseed generation guard'i (ConsoleBatcher) aynen calisir: applyNow senkron, flush kuyruga sonra girer. ConsoleBatcherTests'in flush imzasi degisir — test YENI kurali pinleyecek sekilde yeniden yazilir (CLAUDE.md).

**Test fikri:** Kirmizi test: 177 ProjectSkippedEvent art arda InvokeAsync ile kuyruga atilir; ayni anda Render oncelikli bir sayaç operasyonu planlanir; iddia: Render operasyonu kuyrugun TAMAMI bosalmadan once en az bir kez calisir (bugun kirmizi: Normal turlarin hepsi once kosar). Ikinci test: UI 300 ms bloke edilir, 6 batch birikir; iddia: AppendConsoleBatch cagri sayisi 1 (bugun 6).

**Doğrulayıcı notu:** Mekanizma koddan dogru: src/BuildOrchestrator.App/MainWindow.xaml.cs:342 (olay basina Dispatcher.InvokeAsync, oncelik yok = Normal), :349 (AutoSync ayni yol), :580 (batch basina InvokeAsync, donen operation beklenmiyor), Console/ConsoleBatcher.cs:129-164 (flush senkron Action, geri basinc yok). Etki iddiasi ('saniyelerce erteleme') olcumle celisiyor: WM_NULL sondasi gorunurken p95 7,5 / p99 16,3 / max 69,5 ms; tepside p95 13,1 / p99 26,9 / max 55-86 ms. Trace'te (tepsi) uygulama handler'lari kucuk: RunViewModel.OnEvent kapsayici 890 ms = UI aktif suresinin %7,8'i; aktif surenin %79,4'u zaten Render onceligindeki gecislerde (RenderMessageHandler %48,5 + AnimatedRenderMessageHandler %30,9). Yani sorun 'Normal isler Render'i bekletiyor' degil, Render/layout gecisinin kendisi pahali; oncelik degistirmek isi azaltmaz.

#### N3-2 — Proje logu acma / Back tek dispatcher turunda: tam metin kopyasi + yeni belge + 4'e kadar zorla UpdateLayout + yazilim rasterizer'li RenderTargetBitmap; 340 ms sonra 3 UpdateLayout daha — hicbiri butce testinde yok

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 341-363 — PinAfterModeSwitch: UpdateLayout + (dip icin) MaxPinPasses=3 kez ScrollToEnd+UpdateLayout
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 377-385 — SettleAtBottomIfFollowing: tilt bitince (Land) ayni pin dongusu bir kez daha
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 634-652, 672-690 — ResetRunDocument / PlayCascade: SplitLines + yeni TextDocument + PinAfterModeSwitch + PlayTiltIn ayni turda
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 714-746, 758-768 — PlayTiltIn -> BuildTiltScene: tam DPI RenderTargetBitmap.Render (UI thread, senkron)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2820-2843 — SeedRunDocument/SeedProjectDocument: _gate kilidi altinda tum tamponun ToString kopyasi (IPC okuma thread'i de bu kilidi bekler)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 612-651 — OnSelectedProjectChangedAsync / ShowRunConsole: zincirin cagrildigi yer
- `src/BuildOrchestrator.App/Controls/StickyLayerList.xaml.cs` satır 510-511 — Kodun kendi notu: UIElement.UpdateLayout elemana kapsanmaz, ContextLayoutManager'in TAMAMINI surer (mevcut kod, degismez)

**Kanıt:** Tek tiklamanin (kart sec ya da Back) senkron zinciri koddan sayildi: (a) kilit altinda tam metin kopyasi — olculen kosuda kosu metni 7,7 MB (UTF-16 ~15 MB, LOH), D6-6/D6-10 bunu kopya sayisi acisindan yaziyor; (b) SplitLines + new TextDocument; (c) PinAfterModeSwitch: 1 + en cok 3 UpdateLayout; (d) BuildTiltScene: RenderTargetBitmap (D7-6); (e) 340 ms sonra Land -> SettleAtBottomIfFollowing: en cok 3 UpdateLayout daha. Toplam en cok 7 zorla layout turu. UpdateLayout pencere genelindeki TUM kirli olcum/yerlesimi bosaltir; kosu surerken satir/serit/graf surekli kirli oldugundan her cagri konsol disindaki isi de one ceker. Sure olculmemis: UiResponsivenessBudgetTests secim/Back yolunu hic calistirmiyor (dosyada OnSelectedProject/ShowRunDocument/PlayCascade yok).

**Etki:** Kullanicinin en sik yaptigi etkilesim (bir projenin loguna bak, geri don) kosu sirasinda tek parca bloktur; blok suresi kosu metninin boyutuyla (a) ve pencerenin kirli layout miktariyla (c,e) buyur. Zayif makinede tiklama basina takilma adayi; sayisi olculmeli.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. Bilinen tek ilgili sayi: managed heap'te tek 13,7 MB char[] (iki rebuild sonrasi) — kosu metni tamponunun buyuklugu; Back'te bunun ToString kopyasi alinir. Tepsi senaryosunu etkilemez (etkilesim gerektirir).

**Çözüm:** (1) Once olc (asagida). (2) Pin dongusunu daralt: EditorControl.UpdateLayout yerine yalniz editorun kendi olcumunu zorlayan yol (TextView.EnsureVisualLines + ScrollViewer'in InvalidateScrollInfo'su) ya da pin'i tek UpdateLayout + DispatcherPriority.Loaded'da tek duzeltme turuna indir; MaxPinPasses dongusu yalniz extent gercekten degistiginde donuyor ama her tur tum pencereyi olcturuyor. (3) Kilit altindaki ToString'i kaldir: D6-1/D6-6'nin satir listesi (IReadOnlyList<string> + Count snapshot) — belge icin yalniz son 200 satir Join edilir. (4) RTB'yi 96 dpi'da al (D7-6). (5) Land'deki ikinci pin yalniz extent degistiyse calissin (ExtentHeight karsilastirmasi UpdateLayout'tan ONCE yapilamiyor; ScrollChanged olayindan ogrenilebilir).

**Değişmezler:** Solution B (baslik ve govde ayni karede), reseed generation guard'i, 'anlati sondan / proje logu bastan okunur' kurali, tilt hareketi (§14.5) korunur. Pin dogrulugu icin mevcut testler (dip TAM olmali) yesil kalmali.

**Test fikri:** UiResponsivenessBudgetTests'e iki adim: 'proje logu ac' ve 'Back' — her biri BudgetMs (120) altinda; fix'ten once kirmizi gosterilir (kosu metni olculen boyutta).

**Doğrulayıcı notu:** Koddan dogrulandi: Console/ConsoleView.xaml.cs:341-363 (PinAfterModeSwitch: dipte 1 + en cok MaxPinPasses=3 UpdateLayout; tepede 2), :377-381 (SettleAtBottomIfFollowing yalniz anlati modunda ve takip acikken), :634-652 (ResetRunDocument: SplitLines + yeni TextDocument + pin), :672-690 (PlayCascade), :764-768 (tam DPI RenderTargetBitmap.Render); ViewModels/RunViewModel.cs:2820-2843 (_gate altinda ToString). Duzeltme: '7 zorla layout' yalniz Back (anlati) yolunun ust siniri; proje logu acilisi 2 UpdateLayout. Sure bu olcumde YOK (senaryo kosulmadi) — etki buyuklugu tahmin.

#### N3-3 — Ctrl+F filtre kutusu her tus vurusunda listeyi tam reset ediyor (gecikme/debounce yok): tus basina bir viewport dolusu satir yeniden kuruluyor

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: olculdu · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/ShellRoot.xaml` satır 69 — Text='{Binding ProjectQuery, UpdateSourceTrigger=PropertyChanged}' — Delay yok
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 814, 880 — _projectQuery ([NotifyPropertyChangedFor] VisibleProjects) ve VisibleProjects = Projects.Where(...).ToList()
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 250-258, 709-716, 737-748, 752 — VisibleProjects -> RefreshVisibleRows (imza degistiyse) -> ApplyProjectGroups -> SetGroups; ayrica imza her bildirimde string.Join ile yeniden kuruluyor
- `src/BuildOrchestrator.App/Controls/StickyLayerList.xaml.cs` satır 138-144, 153-186 — SetGroups: ItemsSource atamasi = tam reset (bilincli, belgeli)
- `tests/BuildOrchestrator.Tests/App/ListRealizationPerfTests.cs` satır 33-42 — Turetme: ~32 satir x ~3 ms/ProjectRow = ~95 ms (referans makine), butce 120 ms
- `ARCHITECTURE.md` satır 5485-5488 — §20: liste degisiminde container'lar atilir, viewport yeniden kurulur

**Kanıt:** Baglama UpdateSourceTrigger=PropertyChanged ve Delay'siz: her karakter ProjectQuery'yi yazar -> VisibleProjects bildirimi -> gorunur kume degistiyse ApplyProjectGroups -> ItemsSource reset -> container'lar atilir (ARCHITECTURE §20) -> viewport + %50 cache kadar satir yeniden kurulur. Maliyetin kaynagi testin kendi turetmesi: referans makinede ProjectRow basina ~3 ms, ~32 satir ~95 ms; 'filtre AC/KAPA' adimi 120 ms butceyle pinli (UiResponsivenessBudgetTests.cs 87-88) — yani tus basina referans makinede 120 ms'ye kadar blok kabul edilmis durumda. 5 harflik bir arama = 5 ardisik reset. Durum filtresi acikken kosu sirasinda gorunur kume her degistiginde de ayni reset calisir.

**Etki:** Yazarken takilma: referans makinede tus basina ~95 ms, zayif CPU'da orantili olarak daha fazla (olculmeli). Hizli yazan kullanici harflerin gec dustugunu gorur; kosu sirasinda N3-1'deki olay turlariyla ayni kuyrukta yarisir.

**Etki (doğrulayıcı düzeltmesi):** Referans makinede tus basina en kotu ~43-58 ms (filtre kapat 43,1 ms; 191 satir realize 58,4 ms). Dusuk cekirdekte olculmedi.

**Çözüm:** (1) En ucuz: baglamaya Delay=150 (WPF Binding.Delay; kaynak guncellemesi son tustan 150 ms sonra) — tek satir XAML, reset sayisi yazma hizindan bagimsiz hale gelir. Esc/temizle yolu (ShellRoot.xaml.cs 89, RunViewModel.ActionBar.cs 336) dogrudan VM'e yazdigi icin gecikmez. (2) VisibleRowSignature string.Join yerine sayim + sirali Id hash'i (187 uzun yolun birlestirilmesi her proje olayinda yapiliyor: MainWindow.xaml.cs 714, 752). (3) Daha buyuk is (ayri karar): daralan sorguda yerinde Remove ile uzlastirma — StickyLayerList doc'u (138-144) bunu bilerek reddediyor; (1) yeterliyse dokunulmaz.

**Çözüm (doğrulayıcı düzeltmesi):** Binding Delay tek satir ve yeterli; (2) imza icin string.Join yerine hash ayri ve kucuk. Daralan sorguda yerinde uzlastirma onerilmez (StickyLayerList'in belgeli reset karari).

**Değişmezler:** Reset semantigi ve reveal:false kurali degismez; secim satir VM'inde kalir; scroll konumu korunur (ProjectListFilterTests). Filtre sonucu ayni, yalniz gec uygulanir.

**Test fikri:** Kirmizi test: 5 karakter 20 ms arayla yazilinca SetGroups cagri sayisi 1 olmali (bugun gorunur kume her degistiginde 1 = 5'e kadar).

**Doğrulayıcı notu:** Kod dogru: ShellRoot.xaml:69 baglamada Delay yok (UpdateSourceTrigger=PropertyChanged); RunViewModel.cs:880 VisibleProjects her seferinde ToList; MainWindow.xaml.cs:709-716 imza degisince ApplyProjectGroups(reveal:false) -> SetGroups tam reset. Etki sayisi abartili: bulgu tus basina ~95-120 ms diyor; olculen 'filtre KAPA' 43,1 ms, 'filtre AC' 8,1 ms, 191 satir realize medyan 58,4 ms (referans makine).

#### N3-4 — Copy log / Copy diagnostics: pano kilitliyken UI thread'i Thread.Sleep ile bekliyor; dis dongu WPF'in kendi ic retry'inin ustune biniyor — belgelenen '~100 ms' en kotu durum gercekte saniyeler

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: tahmin · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Console/ClipboardRetry.cs` satır 17-18, 41-45 — DefaultAttempts=10, DefaultDelayMs=10; SetText UI thread'inde Thread.Sleep ile retry; yorum: 'en kotu ~100ms bloklar'
- `src/BuildOrchestrator.App/Console/ConsoleHeader.xaml.cs` satır 248-251 — CopyLog: LogTextProvider (tam proje logu ToString) + ClipboardWriter ayni tiklama turunda
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2799-2802 — GetProjectDocumentText: _gate altinda tam log kopyasi
- `src/BuildOrchestrator.App/Views/AboutDialog.xaml.cs` satır 75 — Copy diagnostics ayni yazici

**Kanıt:** Uygulamadaki tek Thread.Sleep burada ve UI thread'inde (grep: src/BuildOrchestrator.App altinda baska Thread.Sleep/.Result/.Wait yok; AppShutdown.cs 35'teki Wait yalniz OnExit'te). Sarmalayici yalniz CLIPBRD_E_CANT_OPEN'i 10 kez dener. WPF'in Clipboard.SetText -> SetDataObject(copy:true) yolu ise bu HRESULT'u firlatmadan ONCE kendi icinde OleSetClipboard'u 10 kez 100 ms arayla dener (ve basarida OleFlushClipboard icin ayni donguyu kurar) — yani tek bir dis deneme zaten ~0,9 s bloklayabilir; 10 dis deneme = ~9 s + 90 ms. Bu ic retry sayilari WPF kaynagindan bilinen degerlerdir, bu calismada yeniden dogrulanmadi (acik soru). Basarili yolda da panoya 4 MB'lik log (D6-10: 8.824 satir) UTF-16 ~8 MB olarak iki kez kopyalanir (ToString + OLE flush).

**Etki:** Pano baska bir surec tarafindan tutulurken (pano yoneticileri, uzak masaustu, Office) tek tiklama pencereyi saniyelerce dondurabilir; yorumdaki 100 ms sozu yaniltici. Sik degil ama gorulunce 'uygulama kilitlendi' diye okunur.

**Etki (doğrulayıcı düzeltmesi):** Kanitli olan: dis dongu en cok 90 ms uyur. Ic retry varsa tek tiklama ~9 s bloklayabilir — olculmedi.

**Çözüm:** Dis donguyu UI thread'inde uyutma: (a) ilk denemeyi senkron yap; kilit hatasinda kalan denemeleri DispatcherTimer (10-50 ms) ile zamanla — UI akmaya devam eder, basari/kalici basarisizlik ayni geri bildirim yolundan (ShowCopiedVisual) verilir; (b) dis deneme sayisini WPF'in ic retry'i dogrulandiktan sonra 2-3'e indir (toplam tavan ~1 s); (c) buyuk logda ToString'i D6-1'in satir listesinden uret. SyncRetry (Core) ortak dongusu senkron kalir; burada yalniz 'wait' stratejisi degisir — kopya kod acilmaz.

**Değişmezler:** Clipboard yalniz UI (STA) thread'inden yazilir; kilit disi istisnalar yutulmaz; 'Copied' geri bildirimi 1400 ms kurali ayni. ClipboardRetryTests deterministik wait seam'i korunur.

**Test fikri:** Kirmizi test: set eylemi her cagrida 900 ms bloklayip CLIPBRD_E_CANT_OPEN firlatan sahte ile CopyLog cagrilir; iddia: cagri 1 s icinde doner ve kalan denemeler dispatcher'a ertelenir.

**Doğrulayıcı notu:** Dis dongu koddan dogru: Console/ClipboardRetry.cs:17-18 (10 deneme x 10 ms), :42-45 (UI thread'inde Thread.Sleep). 'Gercekte saniyeler' iddiasi WPF'in Clipboard.SetText icindeki kendi retry'ina (10 x 100 ms) dayaniyor; bu repo'da ne kaynakla ne olcumle dogrulandi (bulgunun kendi acik sorusu). Pano kilidi nadir ve donanimdan bagimsiz; tepsi/derleme senaryosuyla ilgisi yok.

#### N3-5 — Yanit verebilirlik testleri handler suresini olcuyor, kullanicinin hissettigi gecikmeyi degil; alti yol tamamen kapsam disi ve sinif CI'da kosmuyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | orta / dusuk |

**Konum:**
- `tests/BuildOrchestrator.Tests/App/UiResponsivenessBudgetTests.cs` satır 29-31, 66-102, 106-136, 140-166 — LocalOnly; uc test: Sync adimlari (120 ms), proje olayi (50 ms, yalniz started/succeeded), graf statu itisi (50 ms)
- `tests/BuildOrchestrator.Tests/App/UiResponsivenessBudgetTests.cs` satır 121-135 — 354 olayin TOPLAM suresi yazdiriliyor ama iddia edilmiyor
- `tests/BuildOrchestrator.Tests/App/GraphRealizationPerfTests.cs` satır 39-42, 112-135 — Butce yalniz 36 dugum icin (400 ms, ertelenebilir); 500/1000 kayit; resize yalniz 'yeniden hesaplandi mi' diye soruluyor
- `tests/BuildOrchestrator.Tests/App/PerfProfileUiLatencyMeasurementTests.cs` satır 28-60 — Dispatcher gecikmesini olcen tek sonda — ama bos bir dispatcher uzerinde, uygulamanin kendi is yukuyle degil

**Kanıt:** Olculen: Sync adimlari tek tek <120 ms, started/succeeded olayi <50 ms, graf itisi <50 ms, envanter yayini <50 ms (InventoryPublishTests), liste realizasyonu <120 ms (ListRealizationPerfTests), 177 dugumun ayni tick'te bitmesi <120 ms (GraphRunLifecycleTests 179-204). Olculmeyen (dosyalarda cagrisi yok): (1) dispatcher kuyrugunda bekleme / girdi gecikmesi (N3-1); (2) proje logu acma ve Back (N3-2); (3) ConsoleView.AppendBatch'in akis altindaki maliyeti; (4) MainWindow tick'inin tam govdesi (D6-7); (5) tepsiden donus: Show + ilk tam cizim + aktivasyon Sync'inin art arda uc turu (topoloji, onizleme, syncCompleted — her biri 120 ms butceli, yani kabul edilen toplam 360 ms'ye kadar); (6) Settings/About/What's new'in ILK acilisi (Collapsed agac ilk kez olculur — D8-9), maximize/restore ve splitter suruklemesinde 187 dugumlu graf Relayout (N3-6), Ctrl+F yazimi (N3-3). Ayrica butun sayilar 8 thread / 32 GB makineden; sinif LocalOnly oldugu icin CI'da hic kosmuyor ve dusuk donanim vekili (2 cekirdek) yok.

**Etki:** Zayif makine hedefi icin elde olculmus tek sayi yok; yeni bir donma kaynagi bu yollardan girerse testler yesil kalir.

**Etki (doğrulayıcı düzeltmesi):** Butce testleri: tek olay 10,0 ms (177x2 olay toplam 545 ms), topoloji 36,8+56,7 ms, filtre kapat 43,1 ms. Canli kosu: UI gecikmesi gorunur p99 16,3 / max 69,5 ms; tepsi p99 26,9 / max 55-86 ms; en uzun mesgul dilim 327 ms (tepsi), 99 ms (on plan). Gizli pencere senaryosu icin hicbir butce testi yok.

**Çözüm:** Tek bir env kapili olcum sinifi (BO_MEASURE_UI_LATENCY=1, [SkippableFact] + StaThread.RunAsync — CLAUDE.md kurali): gercek pencere + Dispatcher.Hooks + Input oncelikli kalp atisi; senaryolar: pre-skip patlamasi, tam kosu olay dizisi + satir akisi, proje logu ac/Back, tepsiden donus (Hide -> olaylar -> Show), ilk Settings acilisi, 20 adimli pencere genisligi degisimi, 5 karakter filtre. Her senaryo icin p50/p95/max girdi gecikmesi ve en uzun tek tur yazdirilir; ayni kosu Process.ProcessorAffinity ile 2 cekirdege kisilarak tekrarlanir. Sayilar cikinca kalici butceler UiResponsivenessBudgetTests'e eklenir (kirmizi test kurali: once kirmizi).

**Çözüm (doğrulayıcı düzeltmesi):** Olcum sinifi onerisi gecerli; ilk kalici pin 'pencere gizliyken kosu olaylari + konsol batch'leri layout gecisi baslatmaz' olmali (ContextLayoutManager.UpdateLayout tepside UI aktif suresinin %70,4'u).

**Değişmezler:** Mevcut butceler gevsetilmez; olcum testi varsayilan suitte kosmaz (env kapisi).

**Test fikri:** Yukaridaki senaryo listesi; ilk kalici pin: pre-skip patlamasinda en uzun girdi gecikmesi < BudgetMs.

**Doğrulayıcı notu:** Kod dogru: tests/BuildOrchestrator.Tests/App/UiResponsivenessBudgetTests.cs:30 (LocalOnly), :37 BudgetMs=120, :41 EventBudgetMs=50; testler handler'i senkron cagiriyor. Olcum bulguyu dogruluyor ve keskinlestiriyor: butce testi 'en kotu tek olay 10,0 ms' derken ayni kosunun trace'inde UI thread'i 327/261/233 ms pompaya donmuyor (25 s'de >50 ms 27 kez, >100 ms 7 kez) — fark, handler'dan SONRA gelen layout/render gecisinde; testler onu olcmuyor. 'Zayif makine icin tek sayi yok' kismi artik eskidi: 2 ve 4 mantiksal islemci benzetimi olculdu (script olarak, test olarak degil).

#### N3-6 — Graf, panel olcusu her degistiginde tum dugumleri yeniden yerlestirip boyutlandiriyor (187 dugum x 12 layout ozelligi); pencere/splitter suruklemesinde her adimda tekrarlanir, butcesi yok

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 257 — Ground.SizeChanged += Relayout + ApplyCamera — birlestirme/erteleme yok
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 849-862 — Relayout: QuietGraphLayout.Compute (model listesi her seferinde yeniden kuruluyor) + dugum basina PlaceNode
- `src/BuildOrchestrator.App/Graph/GraphView.xaml.cs` satır 878-913 — ApplySizes: dugum basina 5 elemanin Width+Height'i; geometri degistiyse beads saati birakilip yeniden kuruluyor
- `ARCHITECTURE.md` satır 5489-5494 — §20: 500 dugum ~130 ms, 1000 dugum ~300 ms realize (referans makine) — yeniden yerlesim icin sayi yok

**Kanıt:** Her SizeChanged'de dugum basina 2 Canvas konumu + 10 Width/Height yazimi (hepsi AffectsMeasure) = 187 dugumde 2.244 layout ozelligi yazimi + kenarlarin yeniden cizimi; surukleme sirasinda WPF her boyut adiminda SizeChanged yayar. GraphRealizationPerfTests resize'i yalniz LayoutComputeCount > 0 ile dogruluyor, sure olcmuyor. Tetikleyiciler: pencere kenarindan boyutlandirma, splitter suruklemesi, maximize/restore, yerlesim modu degisimi, DPI degisimi (MaximizeFix.cs 63).

**Etki:** Tek seferlik gecislerde (maximize) bir tur; surukleme boyunca surekli. Sure olculmedi — realize sayilarindan (500 dugum 130 ms) 187 dugum icin tur basina onlarca ms altinda olmasi beklenir (tahmin).

**Çözüm:** Once olc. Gerekirse: SizeChanged'de dogrudan Relayout yerine 'kirli' bayragi + DispatcherPriority.Render'da TEK Relayout (ayni karedeki birden cok boyut olayi birlesir); dugum olcusu (NodeSize) degismediyse ApplySizes'in dugum dongusu atlanir (yalniz konum yazilir); Compute icin model listesi SetGraph'ta bir kez kurulur.

**Değişmezler:** §13.6 'graf her panel boyutunda tam sigar' korunur; yerlesim sonucu ayni, yalniz kare basina en cok bir kez hesaplanir.

**Test fikri:** Ayni dispatcher turunda 5 boyut degisimi -> LayoutComputeCount 1 artmali (bugun 5).

**Doğrulayıcı notu:** Koddan dogrulandi: Graph/GraphView.xaml.cs:257 (SizeChanged -> Relayout + ApplyCamera, birlestirme yok), :849-862 (Compute + dugum basina PlaceNode), :878-913 (ApplySizes). Sure olculmedi; tepside SizeChanged gelmez.

#### N3-7 — UI thread'inde senkron dosya IO'su: her pencere aktivasyonunda git yoklamasi iki kez + HEAD/packed-refs okumasi; her kapatmada ui-state.json iki kez okunuyor; yazim atomik degil

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 350 — Activated -> _vm.OnWindowActivated()
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.AutoSync.cs` satır 48-52 — OnWindowActivated: RefreshGitOperation (1. yoklama) + WindowActivatedAsync
- `src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs` satır 255, 269 — EvaluateAsync: WaitForGitOperation (2. yoklama) ve _readHead(_gitDir) — ikisi de UI thread'inde senkron
- `src/BuildOrchestrator.Core/Git/GitOperationProbe.cs` satır 27-32 — 6 File.Exists/Directory.Exists (mevcut kod, degismez)
- `src/BuildOrchestrator.Core/Git/HeadReader.cs` satır 20-41, 69-72 — HEAD ReadAllText + commondir + loose ref ya da packed-refs ReadAllLines
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.GitOperation.cs` satır 31, 78-86 — Isaret dururken 2 s'de bir ayni yoklama (DispatcherPollTimer, Background)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 1399, 1329-1333, 1141-1143, 1160-1166, 1232 — _uiState.Load: OnClosing + FirstCloseBalloonGate.ClaimShow (her x'te 2 okuma), yerlesim/tercih degisiminde Load+Save, kosu sonu bildirim kapisi
- `src/BuildOrchestrator.App/Shell/UiStateStore.cs` satır 183-208 — Load: File.Exists + ReadAllText + Deserialize; Save: File.WriteAllText (gecici dosya + rename yok)

**Kanıt:** Aktivasyon basina UI thread'inde: GitDirectory.Resolve + 6 varlik sorgusu x 2 (OnWindowActivated ve hemen ardindan WaitForGitOperation) + HEAD okumasi; aktif ref loose degilse packed-refs'in tamami okunur — OSYS'te packed-refs 15.856 B / 201 satir (olculdu: ls + wc). Ilk yoklama 5 s sessizlik esiginden bagimsiz, HER Activated'da calisir. ui-state.json 1.124 B (olculdu); D8-6 acilistaki 5 okumayi yaziyor, buradaki ek: her x iki okuma, her perf/config/yerlesim degisimi okuma+yazma. Sureler olculmedi: sicak onbellekte dosya basina alt-milisaniye beklenir; antivirus on-access taramasi ya da ag/yavas disk durumunda her cagri UI thread'ini bekletir (tahmin). Save atomik degil: yazim sirasinda surec olurse dosya kesik kalir, Load JsonException'i yutup varsayilan dondurur ve sonraki Save tum tercihleri (repo koku dahil) varsayilanla ezer — performans degil veri kaybi riski, ayni duzeltmeyle kapanir.

**Etki:** Normalde gorunmez; yavas diskte ya da antiviruslu makinede alt-tab donusunde ve x'te kisa takilma adayi. Asil kazanc: UI thread'inden IO'yu tamamen cikarmak ve tercih dosyasini saglamlastirmak.

**Çözüm:** (1) UiState tek ornek ve bellekte: D8-6'nin onerisi (DI'da tek IUiStateStore, alan olarak tutulan durum); okuma yollari diske gitmez. Save: degisiklik bellege yazilir, diske yazim thread pool'da tek yazicili kuyruktan gecici dosya + File.Move(overwrite) ile; cikista bekleyen yazim senkron bosaltilir. 'Close to tray her kapatmada taze okunur' kurali bellek kopyasi Settings Save'de guncellendigi icin korunur. (2) Git yoklamasi: OnWindowActivated ve EvaluateAsync tek yoklama sonucunu paylassin; yoklama + HeadReader.Read Task.Run'da, sonuc UI'ya InvokeAsync ile (AutoSync zaten async zincir). (3) D8-7/D11-7: autostart kaydi oku-karsilastir-yaz.

**Değişmezler:** Git'e yazilmaz (salt okuma yoklamasi); karar sirasi (spec §6.1-6.4) ayni; UiState semasi ve dosya yolu degismez.

**Test fikri:** Sahte IUiStateStore sayaciyla: bir x kapatmasi 0 disk okumasi yapmali (bugun 2). InspectGitOperation seam'i sayacla: bir aktivasyonda 1 yoklama (bugun 2).

**Doğrulayıcı notu:** Koddan dogrulandi: MainWindow.xaml.cs:350; ViewModels/RunViewModel.AutoSync.cs:48-52 (RefreshGitOperation + WindowActivatedAsync); Services/AutoSyncCoordinator.cs:255 (WaitForGitOperation = 2. yoklama), :269 (_readHead); Shell/UiStateStore.cs:183-208 (Load her cagrida diskten, Save File.WriteAllText — atomik degil); MainWindow.xaml.cs:1399 ve 1232 (kapatmada/bildirimde Load). Sureler olculmedi. Atomik olmayan Save bir dayaniklilik bulgusu, performans degil.

#### N3-8 — Uc yerde senkron Dispatcher.Invoke (Send onceligi): UI'yi bloklamaz ama kuyruktaki Normal motor olaylarinin ONUNE gecer ve arka plan thread'ini UI bosalana dek bekletir

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 330-335 — _engine.EngineExited += code => Dispatcher.Invoke(() => _vm.OnEngineExited(code))
- `src/BuildOrchestrator.App/App.xaml.cs` satır 131-132 — UpdateService teklifi: Dispatcher.Invoke(() => AvailableUpdate = offer)
- `src/BuildOrchestrator.App/App.xaml.cs` satır 143 — Ikinci instance sinyali: Dispatcher.Invoke(window.ShowFromTray)
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 136-145, 188 — EngineExited thread pool'dan atesleniyor (cikis izleyicisi / framing hatasi)

**Kanıt:** Dispatcher.Invoke(Action) baska thread'den cagrildiginda isi Send (10) onceligiyle kuyruga koyar ve cagirani bekletir. Motor olaylari ise Normal (9) ile kuyrukta (N3-1). Sonuc: motor coktugunde, okuma dongusunun az once kuyruga attigi son olaylar (or. projectFailed) henuz islenmemisken OnEngineExited ONCE calisabilir; olaylar 'motor kaybi' durumunun ustune sonradan uygulanir. Bunun gorunur bir kusur uretip uretmedigi dogrulanmadi (IsStaleRunEnd, RunViewModel.cs 2451-2459, _currentRunId null iken runCompleted'i kabul ediyor — tahmin). Kilitlenme riski: UI thread'inin bu thread'leri senkron bekledigi tek yer OnExit (AppShutdown.cs 35) ve orada EngineHost generation'i artirip izleyiciyi susturuyor (EngineHost.cs 205) — kilitlenme yolu bulunmadi.

**Etki:** Donma degil siralama kirilganligi; N3-1 kuyruguna gecildiginde bu uc cagri kuyrugun disinda kalirsa siralama farki buyur.

**Çözüm:** Ucunu de InvokeAsync'e cevir; EngineExited'i N3-1'in olay kuyruguna sentinel olarak yaz (motorun son olaylarindan SONRA islenir). Guncelleme teklifi ve ikinci-instance sinyali icin cagiranin sonucu beklemesine gerek yok.

**Değişmezler:** EngineExited tek sinyal (TryClaimExit) kurali ve generation korumasi aynen; ShowFromTray UI thread'inde calisir.

**Test fikri:** Iddia: OnEngineExited, ayni motorun daha once kuyruga girmis olaylarindan sonra calisir (bugun once calisir = kirmizi).

**Doğrulayıcı notu:** Koddan dogrulandi: MainWindow.xaml.cs:330-335, App.xaml.cs:132, App.xaml.cs:143 senkron Dispatcher.Invoke. Donma degil siralama kirilganligi; gorunur bir kusur uretmedigi gosterilmedi.

#### N3-9 — App tarafinda GC duraklamalari hic olculmemis; buyuk nesne yigini (LOH) tahsisleri UI thread'inde yapiliyor ve kosu boyunca bellek 187 MB buyuyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | upgraded · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) Tahsis kaynaklari: MainWindow.xaml.cs:598-606 -> Console/ConsoleView.xaml.cs:274-319 (batch basina AvalonEdit satir yeniden bicimleme); Views/StickyRibbon.xaml.cs:450-470; Views/BuildMenu.xaml.cs:98-105; ViewModels/GraphBinder.cs:37,80; csproj'larda GC ayari yok (dogru).
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2798-2802, 2825, 2839, 2995 — Tam tampon ToString kopyalari (kosu metni, proje logu, dikis) — 85 KB ustu = LOH
- `src/BuildOrchestrator.App/Console/ConsoleBatcher.cs` satır 154-159 — Batch basina StringBuilder + ToString (20/s'ye kadar)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 752, 851-856 — Proje olayi basina 187 yolun string.Join'i (imza) ve Dictionary(187) (RowsById) — D6-2/D6-3 ile ayni tahsisler
- `src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` satır - — GC ayari yok (grep: ConcurrentGarbageCollection/TieredPGO/ConserveMemory 0 sonuc) -> varsayilan workstation + concurrent

**Kanıt:** Olculen: kosu boyunca App Private 194 -> 381 MB, kosu bitince geri verilmiyor; dotnet-trace'te GC.RunFinalizers %5,5 ve CriticalHandle.Finalize %3 (cok sayida sonlandirilan handle). Kod: kosu metni olculen kosuda 7,7 MB -> Back'te tek ~15 MB'lik LOH string; proje logu aciliminda 4 MB'lik logun 4-5 kopyasi (D2-5/D6-10). Varsayilan concurrent GC'de gen2 arka planda kosar ama gen0/gen1 ve arka plan GC'nin baslangic/bitis evreleri UI thread'ini durdurur; LOH tahsisi gen2 butcesini tuketir. Duraklama suresi/sayisi olculmedi. Sonlandirici yukunun kaynagi icin en guclu aday WPF metin bicimlendirmenin DirectWrite sarmalayicilari (her 50 ms'de yeniden bicimlenen konsol satirlari) — tahmin, N7'nin konusu.

**Etki:** Zayif makinede (az RAM, yavas cekirdek) GC duraklamalari kare atlamasi olarak gorunebilir; buyuklugu bilinmiyor.

**Etki (doğrulayıcı düzeltmesi):** UI thread GC bekleme: tepsi 2.670 ms / 25 s, on plan 1.788 ms / 25 s (ornekleme; gercek duraklama olculmedi). Finalizer thread'inde DWrite sarmalayici birakma: 5.032 ms / 25 s (IDWriteFont 4.350 + DWRITE_SCRIPT_ANALYSIS 682). App Private 158-195 MB -> 433 MB (zorla GC 319 MB); managed canli 15,5 -> 74 MB (tek 13,7 MB char[]).

**Çözüm:** Once olc. Ayar degil tahsis azaltma oncelikli: D6-1/D6-6/D6-10 (tek kaynakli satir listesi, LOH kopyalari kalkar), D6-2/D6-3 (olay basina koleksiyon tahsisleri), N3-1 (dilim basina tek turev). App'te concurrent GC ACIK kalmali (D2-4 ile ayni karar). Kosu bitiminde ve pencere gizliyken tek seferlik GC.Collect(2, GCCollectionMode.Optimized, blocking:false) + LOH sikistirma istegi D2-3'un onerisi — UI gorunurken yapilmaz.

**Çözüm (doğrulayıcı düzeltmesi):** Once gercek duraklamayi olc (dotnet-trace gc-verbose ya da GC.GetTotalPauseDuration farki). Tahsis azaltma sirasi olcume gore: (1) pencere gizliyken konsol belgesine batch uygulama (metin bicimleme ve DWrite sarmalayicilari kaynaginda kesilir); (2) UI Automation agac guncellemesini tetikleyen layout hacmi; (3) kapali BuildMenu ve serit chip'lerinin her olayda yeniden kurulmasi; (4) TopologicalDepths onbellegi. LOH kopyalari (D6-1/D6-6) ayri ve ikincil. GC ayari degistirme onerilmez (App'te concurrent GC kalir).

**Değişmezler:** Hicbir mimari degismez; ayar eklenirse yalniz App csproj'unda.

**Test fikri:** Olcum sondasi: GC.GetTotalPauseDuration() farki tam bir olay dizisi oynatiminda yazdirilir (env kapili).

**Doğrulayıcı notu:** Bulgu 'kozmetik, tahmin, guven 0,4' idi; olcum GC/tahsis baskisini buyuk gosteriyor: UI thread'inde GC bekleme tepside 2.670 ms (%23,4), on planda 1.788 ms (%19,2); Private iki rebuild sonrasi 433 MB, zorla GC ile 319 MB (114 MB cop), managed canli heap 74 MB. AMA bulgunun gosterdigi neden (LOH metin kopyalari) olcumle desteklenmiyor: GC beklemenin 2.654 ms'i Thread.PollGC cercevesi ve cagiranlari UI Automation (InvalidateAutomationAncestors 775+33 ms, CreateAutomationPeer 642 ms) ile DependencyObject deger tablosu (InsertEntry/RemoveEntry ~420 ms); sonlandirici yuku WPF metin bicimlemesinin DWrite sarmalayicilari (5.032 ms / 25 s). Kayit: PollGC orneklemesi dotnet-trace'in kendi thread durdurmasini da icerir; gercek GC duraklama sayisi/suresi (GCStats) OLCULMEDI. 'Finalizer 23,3 s mesgul' CPU degil: cycle sayaci tepside UI+render disindaki TUM thread'lere ~220 Mcycles/s (1853-1385-248) veriyor.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.App/MainWindow.xaml.cs:580, 598-606 -> Console/ConsoleView.xaml.cs:274-319: konsol batch'leri pencere gizliyken (tepsi) de AvalonEdit belgesine uygulaniyor; olcum (tepsi rebuild, 25 s): TextView.MeasureOverride 2.122 ms = UI aktif suresinin %19,1'i, metin bicimlemenin 1.466 / 2.286 ms'i.
- Pencere gizliyken layout durmuyor (hicbir gorunurluk kapisi yok; MainWindow.xaml.cs'te IsVisible yalniz :1244 gosterge ve :1311 toggle icin okunuyor): ContextLayoutManager.UpdateLayout tepside 8.030 ms = UI aktif suresinin %70,4'u (on planda 5.302 ms = %56,8); Window.ArrangeOverride kokunden baslayan yerlesim 1.296 ms.
- UI Automation maliyeti OLCUM ozetindeki %8,3'ten buyuk: yiginda Automation gecen toplam sure tepside 2.907 ms (%25,5), on planda 2.144 ms (%23,0) — ContextLayoutManager.fireAutomationEvents 1.944 ms + InvalidateAutomationAncestors 821 ms; olcum betikleri UIA kullanmiyor (probe-helper.ps1 yalniz SendKeys/SendMessageTimeout), yani makinede baska bir UIA istemcisi dinliyor; kodda kapisi yok.
- src/BuildOrchestrator.App/Views/BuildMenu.xaml.cs:70, 98-105: menu KAPALIYKEN her Counters degisiminde uc satir silinip yeniden kuruluyor; olcum: RefreshRows kapsayici 185 ms / 25 s ve 233 ms'lik en uzun dilimlerden birinin ilk isi BuildMenu.BuildRow (emsal: InventoryPublishTests.A_closed_branch_popover_builds_no_rows_when_the_inventory_changes).
- src/BuildOrchestrator.App/Views/StickyRibbon.xaml.cs:432-470 (BuildBuildingChips): derlenen kume her degistiginde Children.Clear + yeni BuildingSpinner/TextBlock/ToggleButton; olcum: StickyRibbon kapsayici 347 ms, BuildBuildingChips 245 ms, DsChipFactory.Small 95 ms (tepsi, 25 s).
- src/BuildOrchestrator.App/MainWindow.xaml.cs:839 + ViewModels/GraphBinder.cs:37, 80: her statu itisinde (200 ms tick + olaylar) TopologicalDepths bastan hesaplaniyor, oysa topoloji kosu boyunca sabit; olcum: 175 ms kapsayici (tepsi), 54 ms'i GC beklemesi.
- src/BuildOrchestrator.App/MainWindow.xaml.cs:352-364: 200 ms'lik _elapsedTimer tick'i (TickElapsed + SetLineCount + PushGraphStatuses + FollowFrontier) gorunurluge bakmiyor; tepside FixedHeightVirtualizingPanel kapsayici 916 ms (on planda 361 ms) — gizli pencerede satir realize/CleanUp'in neden daha cok oldugu belirlenmedi.
- src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs:768-779: karakter kilitlenmesi saati (24 ms, DispatcherPriority.Render) yalniz hareket sinyaline bakiyor, IsVisible'a degil (imlec yanip sonmesi :419'da kapili); olculen dogrudan maliyeti kucuk (EventStream cerceveleri 66 ms / 25 s).

**Temiz bulunan alanlar:**
- src/BuildOrchestrator.App altinda .Result / GetAwaiter().GetResult() yok (grep: yalniz yorumlarda); .Wait tek yerde (Shell/AppShutdown.cs 35) ve yalniz App.OnExit'te, pencere kapandiktan sonra, Task.Run ile context'siz — 2 s tavanli, kilitlenme yolu kapali.
- Open in Visual Studio: vswhere sorgusu async ve oturum basina bir kez (Services/OsActions.cs 120-176); OsActionsBlockingTests cagiran thread'in <50 ms bloklandigini pinliyor.
- EngineHost.StartAsync/RestartAsync: oldurme + 1 s WaitForExit ConfigureAwait(false) ile UI baglaminin disinda (EngineHost.cs 146-158, 203-213); test pinli. Motor zaten olmusse KillCurrent senkron ama GetProcessById hemen firlatir (hizli yol).
- About diyalogu: MSBuild cozumu yalniz Environment sekmesi ilk secildiginde ve async (Views/AboutDialog.xaml.cs 170-176); Open icinde disk/surec IO'su yok.
- Settings Save: diyalog once kapanir, commit arkasindan surer (Views/SettingsDialog.xaml.cs 295-301); Export/Import dosya IO'su kullanici secimiyle ve KB boyutunda.
- ProjectLogEvent yolu marshal-free ve okuma thread'indeki _gate bolumleri kisa (RunViewModel.cs 2694-2716); UI thread'inin 200 ms tick'te aldigi kilit (GetActiveLineCount) yalniz sayac okuyor.
- _elapsedTimer ve DispatcherPollTimer Background oncelikte (MainWindow.xaml.cs 30; Services/IPollTimer.cs 32) — render ve girdiye yol verir.
- Koleksiyonlar: topoloji yerinde uzlastiriliyor (Remove/Move/Insert, Clear yok — RunViewModel.Workspace.cs 961-991); Branches SnapshotCollection yayin basina en cok bir Reset (InventoryPublishTests 50 ms); StreamEvents 150 ile sinirli.
- Global kisayolla Build (MainWindow.xaml.cs 1305-1317): yalniz komut CanExecute/Execute — disk IO, pencere getirme ya da ui-state okumasi yok.
- RefreshVisibleRows imza guard'i filtre yokken proje olaylarinda liste reset'ini kesiyor (MainWindow.xaml.cs 709-716).
- Maximize/restore kablaji DP izleyicisi (Shell/MaximizeFix.cs) — senkron IO ya da bekleme yok; tek maliyet N3-6'daki graf Relayout.

**Açık sorular:**
- N3-4: .NET 10 WPF'te Clipboard.SetText'in ic retry'i (10 x 100 ms) bu calismada kaynak/olcumle dogrulanmadi; sonda testi sonucu belirleyecek.
- N3-1: 'kullanici komutu calistiginda bekleyen tum motor olaylari uygulanmis olmali' bir degismez mi? Evetse bosaltma Loaded'in altina inmemeli (onerinin 1-4. adimlari buna gore yazildi); hayirsa Input onceligi + komut girisinde senkron bosaltma daha akici.
- Dokuman/yorum ile kod uyusmuyor: StickyLayerList.xaml.cs 473-476 ve 506-509 ile ListRealizationPerfTests sinif doc'u (satir 12) 'virtualization KAPALI' diyor; StickyLayerList.xaml 87-88 IsVirtualizing=True/Recycling ve UiResponsivenessBudgetTests 99-101 sanallastirmayi pinliyor. Yorumlar bayat gorunuyor — hangisinin dogru oldugunu kullanici teyit etmeli.
- Butun butce sayilari referans makineden (8 thread, 32 GB). Hedef makinelerin gercek ozellikleri (cekirdek sayisi, RAM, disk turu, antivirus) nedir — 2 cekirdek/affinity vekili yeterli mi?
- measure2/ klasoru bu analiz sirasinda bostu; UI gecikme sondasi sonuclari gelince N3-1/N3-2/N3-5'in 'olculmeli' alanlari gercek sayilarla doldurulmali.
- What's new (ReleaseNotes) ayristirmasinin ne zaman yapildigi (statik ilk erisim mi, acilis mi) bu turda dogrulanmadi.


### N4-zayif-donanim

#### N4-1 — Perf profili donanimdan bagimsiz SABIT: 4 thread / 8 GB makinede Balanced = 4 MSBuild + 4 csc; isci sayisi ne cekirdege ne bos RAM'e gore kisiliyor

| Alan | Değer |
|---|---|
| Önem | bloklayıcı |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.Core/ProcessControl/PerfProfile.cs` satır 30-36 — For(): Full(6,null,Normal) / Balanced(4,70,BelowNormal) / Light(2,40,Idle) — sabit literal, makine girdisi yok
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 572-576, 823-826, 1008 — Parallelism = ProfileFor(DefaultPerfMode).Parallelism; DefaultPerfMode sabit "Balanced"; StartRunCommand'a bu sayi gider (yorum: Environment.ProcessorCount varsayilani bilerek kaldirilmis)
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 845 — int parallelism = Math.Max(1, cmd.Parallelism) — motor gelen sayiyi oldugu gibi kullanir, ust sinir yok
- `ARCHITECTURE.md` satır 2152-2178 — §11.1: 'three fixed profiles'; 'Memory, not cores, is usually the first limit'; derleyici ~1 GB; Full'de bos fiziksel bellek 'a fraction of a gigabyte'

**Kanıt:** src/ altinda Environment.ProcessorCount yalniz bir yorumda geciyor (RunViewModel.cs:573); GlobalMemoryStatusEx/GC.GetGCMemoryInfo/TotalAvailableMemoryBytes uretim kodunda HIC yok (grep: yalniz tests/.../PerfProfileUiLatencyMeasurementTests.cs:101-148). MSBuild argumanlari -m tasimaz, UseSharedCompilation=false (MsBuildArguments.cs:25-27) → her isci 1 MSBuild + kendi cok-thread'li csc'si. Olcum (8 thread/32 GB, Balanced, 152 proje rebuild): 4 MSBuild + 1-4 csc + 5-9 conhost, child toplam WS tepe 1,85 GB; ayni anda App WS 482 MB + Supervisor 217 MB → arac kaynakli tepe ~2,55 GB WS. 8 GB makinede bu, VS + tarayici acikken fiziksel bellegin ~%32'si. §11.1 kendi olcumuyle bellek baskisinin masaustunu dondurdugunu soyluyor ama koruma yalniz 'kullanici profili dusursun' (README.md:684-686).

**Etki:** Zayif makinede varsayilan (Balanced) ile ilk Rebuild: 4 thread'e 4 MSBuild + 4 csc (her csc cok thread'li) → asiri abonelik + bellek baskisi → paging → tum masaustu (VS dahil) donar. Kullanicinin birincil senaryosu (VS acikken tepsiden build) tam bu durum. Kullanici hafizasindaki log analizi notu paralel derlemenin ~1,1-1,5x olceklendigini kaydediyor (run-log-perf-analysis) → 4→2 isciye inmek sureyi az uzatir, tepe bellegi kabaca yarilar (tahmin; zayif makinede olculmeli).

**Etki (doğrulayıcı düzeltmesi):** Rebuild (152 invoke): 8 islemci on plan 28,4-29,7 s / tepsi 25,4-26,3 s; 4 islemci 32,7 / 28,6 s; 2 islemci 66,4 / 51,0 s — hepsi 4 isciyle. Bellek (gercek derleyici): Light +4,4 GB commit, Balanced +8,4 GB (bos RAM 16,8 -> 8,8 GB), Full +12,2 GB (bos 5,2 GB) = isci basina ~2 GB commit; 8 GB'lik makinede Balanced'in commit'i fiziksel bellegi asar. Bulgudaki '1,85 GB child WS tepe' no-op kosusuna aittir, gercek derlemeye degil.

**Çözüm:** Tek dogruluk kaynagi PerfProfile kalir; Core'a SAF bir kisma fonksiyonu eklenir: PerfProfile.Effective(profile, logicalProcessors, availablePhysicalBytes) → Parallelism = max(1, min(profile.Parallelism, logicalProcessors/2 yuvarla-yukari, floor(availablePhysical / isciBasinaButce))). isciBasinaButce tek sabit (oneri 1,0-1,5 GB; §11.1'in 'compiler ~1 GB'ina dayanir, olcumle sabitlenmeli). Cagri yeri Supervisor'da RunCoordinator.cs:845 (kosu basinda bos RAM'i bilen taraf; GlobalMemoryStatusEx P/Invoke'u Core/ProcessControl/NativeMethods'a, saf fonksiyon girdiyi parametre alir → test edilebilir). Fiili sayi zaten RunStartedEvent.Parallelism ile App'e donuyor ve App onu _runParallelism'e donduruyor (RunViewModel.cs:2031) → ETA/UI ek kod olmadan tutarli. Kisma olduysa PerfNoteText (Core, tek sahip) uzerinden konsol notu: 'parallelism: 2 (limited: 4 logical processors, 3.1 GB free)'. Ilk acilista (ui-state'te PerfMode null iken; UiStateStore.cs:52) varsayilan profil donanima gore: <=4 mantiksal islemci ya da <=8 GB toplam RAM → Light, aksi Balanced; kullanicinin acik secimi asla ezilmez.

**Çözüm (doğrulayıcı düzeltmesi):** (1) Isci basina butce olcumden ~2 GB commit alinmali (bulgudaki 1,0-1,5 GB dusuk). (2) 'ceil(lp/2)' CPU kismasi OLCULMEDI: 2 islemcide 2 ya da 1 isciyle kosu yok; once o olculmeli (affinity benzetimi hazir), sonra esik secilmeli. (3) Kisma Supervisor'da RunCoordinator.cs:845'te yapilirsa RunStartedEvent.Parallelism (:869) gercek sayiyi App'e tasir ve ETA dogru kalir (RunViewModel.cs:2031, :2413; RunViewModel.Stream.cs:157); ama RunViewModel.cs:1792'deki PerfNoteText.Note(profile) ve :576/:1790/ActionBar.cs:123'teki Parallelism hala tablo degerini gosterir — gosterim de fiili sayiya baglanmali. (4) RunCoordinator.cs:846-847 yorumu ('paralellik buradan gelmez') ve §11.1 ayni iste guncellenir.

**Değişmezler:** Planlama Core'da (saf fonksiyon Core'da), kopya yasak (tablo tek, kisma tek fonksiyon), §11.1 'Parallelism does not change mid-run' korunur (kisma yalniz kosu basinda). §11.1 'three fixed profiles' cumlesi degisir → dokuman ayni iste guncellenir; RunViewModelStateTests.cs:37 (Assert.Equal(4, vm.Parallelism) — 'ProcessorCount DEGIL') bilincli kurali pinliyor: kural degisirse test yeni kurali pinleyecek sekilde yeniden yazilir (CLAUDE.md 'Davranis degisince testi de degisir'). Bu, 'v7 plan K11: perf mode SABIT 6/4/2' kararini tersine cevirir — yeni kanit: hedef kullanici donanimi 2-4 cekirdek / 8 GB ve §11.1'in kendi 'memory is the first limit' olcumu.

**Test fikri:** PerfProfile.Effective icin saf birim testi: (Balanced, 4 lp, 3 GB bos) → 2; (Balanced, 8 lp, 16 GB) → 4; (Full, 2 lp, 1 GB) → 1; (Light, 16 lp, 32 GB) → 2 (profil ust sinirdir, asla artmaz).

**Doğrulayıcı notu:** Koddan dogrulandi: Core/ProcessControl/PerfProfile.cs:30-36 sabit 6/4/2; App/ViewModels/RunViewModel.cs:576 ve :1789-1790, RunViewModel.ActionBar.cs:123 sayiyi tablodan alir; :1008 StartRunCommand'a koyar; Supervisor/RunCoordinator.cs:845 'Math.Max(1, cmd.Parallelism)' ust sinir uygulamaz; src/ altinda Environment.ProcessorCount yalniz RunViewModel.cs:573 yorumunda, bos bellek okuyan kod yok. Olcum dogruluyor: 2 mantiksal islemcide Balanced yine 4 isci calistirdi.

#### N4-2 — CPU hard cap makine TOPLAMININ yuzdesi: 2 cekirdekte Light %40 = 0,8 cekirdek + Idle oncelik; Balanced %70 = 1,4 cekirdege 4 isci

| Alan | Değer |
|---|---|
| Önem | kozmetik (N4-1'in alt maddesi) |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/ProcessControl/JobObject.cs` satır 106-123 — SetCpuRate: ENABLE|HARD_CAP, CpuRate = percent*100; yorum: 'job'daki tum process'lerin TOPLAMI icin makinenin toplam CPU'sunun yuzdesi'
- `src/BuildOrchestrator.Core/ProcessControl/PerfProfile.cs` satır 32-34 — cap yuzdeleri sabit 70 / 40
- `ARCHITECTURE.md` satır 2180-2184 — §11.2: cap 'applies to the sum of the inner job's processes as a percentage of the whole machine'

**Kanıt:** Koddan turetilen tablo (cap% x mantiksal islemci = job'un toplam butcesi; isci basina = butce / paralellik): 8 lp: Balanced 5,6 lp / 4 isci = 1,4 lp/isci; Light 3,2 / 2 = 1,6. 4 lp: Balanced 2,8 / 4 = 0,7 lp/isci; Light 1,6 / 2 = 0,8; Full 4 lp'ye 6 isci (cap yok, Normal oncelik → UI ve VS ile esit oncelikte 6 MSBuild + 6 csc). 2 lp: Balanced 1,4 / 4 = 0,35 lp/isci; Light 0,8 / 2 = 0,4 lp/isci + Idle oncelik. Yani zayif makinede isci basina dusen CPU, olcum makinesindekinin 1/2 - 1/4'u; isciler birbirini bekler, her biri bellekte acik kalir (N4-1 ile ayni kok).

**Etki:** Zayif makinede Balanced'da 4 isci 0,35-0,7 cekirdek payiyla kosar: proje basina duvar suresi uzar, es zamanli acik csc sayisi (bellek) dusmez. Light'ta Idle oncelik + %40: VS ya da antivirus CPU kullanirken build fiilen durabilir (Idle sinifi yalniz bos zaman alir); §11.3 copy floor'un varlik nedeni zaten bu sikisma.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. Benzetim cap'i kuculttmedigi icin gercek 2 islemcili makinedeki sure 66,4 s'den (on plan) uzun olacaktir; ne kadar, bilinmiyor.

**Çözüm:** Yuzdeler degismez (bilincli tablo). N4-1'deki kisma ayni anda bu sorunu da cozer: isci sayisi min(profil, ceil(lp/2)) olunca 4 lp'de Balanced 2 isci x 1,4 lp, 2 lp'de 1 isci x 1,4 lp olur. Ek olarak UI'da gosterim: perf chip tooltip'i/konsol notu fiili degeri soylesin ('parallelism: 2 · cpu cap 70% (2.8 of 4 processors)') — metin PerfNoteText'te, tek sahip. 2 lp makinede Light'in Idle onceligi icin karar kullaniciya sorulmali (Idle kalir mi, BelowNormal mi) — olcum olmadan degistirme.

**Değişmezler:** Nested job, cap yalniz inner job'a yazilir (§11.2) — degismez. Tablo tek kaynak. Copy floor / drain kurali (§11.3) dokunulmaz.

**Doğrulayıcı notu:** Aritmetik koddan dogru: Core/ProcessControl/JobObject.cs:106-123 cap makine toplaminin yuzdesi (ARCHITECTURE §11.2 ayni seyi soyler). Ama ayri bir bulgu degil: kok N4-1 ile ayni, kendi cozumu yalniz gosterim metni. 'Light'ta build fiilen durabilir' olculmedi. Onemli kayit: OLCUM'deki 2/4 islemci benzetimi surec yakinligiyla yapildi; cap 8 islemcinin %70'i (5,6 islemci) olarak kaldigi icin benzetimde cap HIC baglamadi — gercek 2 islemcili makinede cap 1,4 islemciye iner ve 66,4 s bir alt sinirdir.

#### N4-3 — Dusuk bellekte kosu oncesi hicbir kontrol yok; csc/MSBuild OOM cikisi 'exit N' olarak DERLEYICI KANITI sayilip projeyi 'bu kaynakta patladi' diye deftere yaziyor

| Alan | Değer |
|---|---|
| Önem | kozmetik (bir OOM yeniden uretilene kadar) |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | orta / orta |

**Konum:**
- `src/BuildOrchestrator.Core/State/FailureClassification.cs` satır 20-26 — IsCompilerFailure: reason 'exit ' ile basliyorsa kanit — cikis kodunun/nedeninin ne olduguna bakilmaz
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2294-2317 — FailureEvidenceSignature + ReasonFor: TimedOut/Killed/Hard stop disindaki her sifir-disi cikis 'exit {kod}'
- `ARCHITECTURE.md` satır 2186-2187 — §11.2: 'No memory, disk, network, process-count or thread limit is written'

**Kanıt:** Uretim kodunda bos fiziksel bellek okuyan hicbir yer yok (N4-1 grep'i). OutOfMemory / CS8750 / 'out of memory' / paging-file icin hicbir siniflandirma yok (grep: yalniz UpdateService.cs:72 kendi istisnasi icin). Bellek tukendiginde csc 'error CS0007/CS8750 ... out of memory' ya da 0xC0000017/0xC000012D ile cikar, MSBuild sifir-disi doner → ReasonFor 'exit N' → kapi kosullari (Build/Rebuild, trusted, imza var) saglaniyorsa evidence=true: satir kirmizi 'failed on this source' olur ve defterde failed imzasi yazilir (§7 'LastFailed imza esitligine bakar').

**Etki:** 8 GB makinede bellek baskisi altinda gecici bir OOM, kaynak bozukmus gibi raporlanir; kullanici yanlis yere bakar. Ayrica kosu, bos bellek 1 GB altindayken bile 4 isciyle baslar (uyari yok). OOM'un fiilen ne siklikla gorulecegi olculmedi (tahmin) — ama siniflandirma boslugu koddan kesin.

**Etki (doğrulayıcı düzeltmesi):** Olculmedi. Kosu basi bellek uyarisi (a) N4-1'in okumasiyla bedavaya gelir; (b) siniflandirma icin once gercek bir OOM ciktisi yakalanmali.

**Çözüm:** (a) Kosu basinda N4-1'in bos-RAM okumasi zaten yapilacak: bos fiziksel < 1 isci butcesi ise kosu 1 isciyle baslar ve konsola + decision.log'a tek satir uyari ('low memory: 0.8 GB free — running 1 worker'). Kosuyu REDDETME (kullanici karari). (b) Core'da mevcut satir-gozlem yoluna (observeLine) bellek-tukenme isareti: 'CS8750', 'CS0007.*memory', 'MSB6006' + cikis kodu 0xC0000017 / 0xC000012D / 0x8007000E → reason 'out of memory' (ExitPrefix ile BASLAMAZ → IsCompilerFailure false → kanit degil, defter 'failed, kanitsiz'). Siniflandirma FailureClassification'da kalir (tek yer). (c) Istege bagli: boyle bir hatada proje kosu sonunda tek isciyle 1 kez yeniden denenir — bu yeni davranis, kullanici onayi gerekir.

**Değişmezler:** Kanit kapisi tek yerde (FailureEvidenceSignature) kalir; reason metni tek kaynaktan (FailureClassification). stdout NDJSON degismez. Job'a bellek limiti YAZILMAZ (§11.2 bilincli; JOB_OBJECT_LIMIT_JOB_MEMORY csc'yi oldurur — onerilmiyor).

**Test fikri:** FailureClassification birim testi: 'out of memory' reason'i IsCompilerFailure=false; RunCoordinator testi: sahte invoke exit 0xC0000017 → ProjectFailedEvent.Evidence=false.

**Doğrulayıcı notu:** Siniflandirma boslugu koddan dogru: Core/State/FailureClassification.cs:20-26 'exit ' onekiyle baslayan her neden kanit; Supervisor/RunCoordinator.cs:2305-2317 ReasonFor timeout/stop disindaki her sifir-disi cikisi 'exit N' yapar; kosu oncesi bellek kontrolu yok. Ancak OOM'un bu arac altinda gercekten olusup olusmadigi, hangi cikis kodu/satirla geldigi hic gozlenmedi; onerilen isaret listesi (CS8750, 0xC0000017...) dogrulanmamis. Olcumde commit siniri asilan Full profilinde bile hata raporlanmadi.

#### N4-4 — Icerik karari IO'su sabit 16-yollu paralel ve cap'siz/Normal oncelikli Supervisor'da kosuyor: 2-4 cekirdekte olculen hizlanma gelmez, planlama penceresi tum cekirdekleri doldurur

| Alan | Değer |
|---|---|
| Önem | kozmetik (tek sabite cekme) — performans kismi olcum bekler |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/Incremental/IncrementalRunBinder.cs` satır 74, 103-106, 162 — Parallel.ForEach MaxDegreeOfParallelism = 16 (uc ayri yerde literal 16)
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 131-142 — AsParallel().WithDegreeOfParallelism(16) + Parallel.ForEach MaxDegreeOfParallelism = 16
- `ARCHITECTURE.md` satır 760-772 — §7: sicak 303 ms (seri 544 ms); soguk diskte dosya basina 8,9 ms seri / 1,9 ms 16-yollu; ilk indeks ~40 s; 'dominant cost is per-file open overhead (on-access scanning)'
- `ARCHITECTURE.md` satır 2184-2186 — §11.2: cap yalniz inner job'a; Supervisor'un kendi isi kisilmaz

**Kanıt:** Literal 16 bes yerde tekrarli (kopya-yasak ihlali: ayni deger 5 yerde). SetMinThreads cagrisi yok (grep) → thread pool taban is parcacigi = mantiksal islemci sayisi; 2-4 lp makinede bloklayan IO icin 16 is parcacigi ancak hill-climbing enjeksiyonuyla (yaklasik saniyede 1-2) zamanla olusur. Olculen 16-yollu kazanc (8,9 → 1,9 ms/dosya; 22.982 dosya → ~40 s) 8 lp makineye aittir. Seri alt sinir koddan/dokumandan: 22.982 x 8,9 ms = ~205 s. 4 lp'de gercek deger bu ikisinin arasindadir (tahmin; olculmeli). Olcum: bu makinede planlama penceresinde Supervisor 1,07 Gcycles/s; aktivasyon Sync'inde 610 Mcycles/s — bu is inner job disinda, Normal oncelikte.

**Etki:** Zayif makinede her Sync/Build planlamasi (aktivasyon Sync'i dahil, D11-1) kisa sureli de olsa tum cekirdekleri Normal oncelikte doldurur — VS'de yazarken hissedilir. Ilk indeks (ya da branch degisimi sonrasi cok dosya) 40 s degil, dakikalar mertebesine cikabilir (tahmin) ve bu sirada antivirus on-access taramasi her dosya acilisinda devrede.

**Etki (doğrulayıcı düzeltmesi):** Ilgili tek olcum 8 islemcide: tus -> runStarted 5,58-5,68 s (Build), 6,7-7,3 s (Rebuild); 2/4 islemcide olculmedi.

**Çözüm:** 16 literal'i Core'da tek sabite cekilir (or. IoParallelism.Degree) ve deger = clamp(Environment.ProcessorCount * 2, 4, 16) — 8+ lp'de davranis AYNEN 16 kalir (mevcut olcumler gecerli), 4 lp'de 8, 2 lp'de 4. Supervisor'un planlama is parcaciklarinin onceligi dusurulmez (planlama kullanicinin bekledigi is); yalniz genislik makineye uyar. Aktivasyon Sync'inin gereksizligi ayri bulgu (D11-1).

**Çözüm (doğrulayıcı düzeltmesi):** Simdi: bes literal Core'da tek sabite (deger 16 kalir). Deger degisimi ancak BO_MEASURE_ROOT/BO_MEASURE_COLD_ROOT olcumu 2 ve 4 islemci yakinliginda kosturulduktan sonra.

**Değişmezler:** Planlama Core'da; sonuc thread sirasindan bagimsiz (girdi listeleri sirali — §7) → genislik degisimi karari degistirmez. Kopya yasak: 5 literal → 1 sabit.

**Doğrulayıcı notu:** Koddan dogru: literal 16 bes yerde — Core/Incremental/IncrementalRunBinder.cs:74, 105, 162 ve SourceHashCache.cs:132, 141 (kopya-yasak ihlali: daginiklik bulgusu olarak gecerli). Performans iddiasi ('2-4 cekirdekte hizlanma gelmez, dakikalar') tahmin; dusuk cekirdekte planlama suresi olculmedi. Onerilen clamp(lp*2,4,16) da olculmedi: is bloklayan dosya IO'su oldugu icin genisligi kismak dusuk cekirdekte planlamayi yavaslatabilir.

#### N4-5 — Yazilim render'i (RenderCapability.Tier 0) hic okunmuyor: tepsi gostergesi = katmanli pencere + hareketli 2 DropShadowEffect @30 fps, konsol tilt = tam DPI RenderTargetBitmap + Viewport3D — hepsi CPU'ya duser, azaltilmis-hareket yolu yalniz OS ayarina bagli

| Alan | Değer |
|---|---|
| Önem | kozmetik (Tier 0 olcumu yapilana kadar) |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Services/SystemParametersMotionSignal.cs` satır 10-18 — Tek sinyal: SystemParameters.ClientAreaAnimation
- `src/BuildOrchestrator.App/App.xaml.cs` satır 55 — new MotionSettings(new SystemParametersMotionSignal()) — sinyalin tek kurulum yeri
- `src/BuildOrchestrator.App/Views/TrayBuildOverlayWindow.xaml` satır 6 — AllowsTransparency=True (katmanli pencere)
- `src/BuildOrchestrator.App/Controls/TrayBuildIndicator.xaml` satır 48-173, 216, 253 — 3 s dongu, 13 keyframe animasyonu (X kaydirma + Opacity + Clip); hareket eden ogelerde 2 DropShadowEffect (BlurRadius 4.8 / 5.6)
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 764-768 — RenderTargetBitmap tam DPI olcekli, UI thread'inde senkron Render
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml` satır 55 — Viewport3D PART_Tilt3D
- `src/BuildOrchestrator.App/Resources/Tokens.xaml` satır 229, 231 — Effect.OverlayShadow BlurRadius 28, Effect.PopoverShadow BlurRadius 18 (LatestPill.xaml:24, Controls.xaml:1067/1450/1465/1557/1582)

**Kanıt:** grep: src/ altinda RenderCapability, RenderOptions.ProcessRenderMode, IsRemoteSession, RenderMode HIC gecmiyor (D7-9'u dogrular). Efekt/3D envanteri yukaridaki konumlar; BlurEffect ve BitmapCache yok. Olcum (bu makine, donanim GPU): tepside gosterge acikken rebuild'de App 1,44 Gcycles/s = tek cekirdegin %40'i (pencere gorunurken %60-100). Tier 0'da ayni sahnenin maliyeti olculmedi → 'tahmin': WPF'te Tier 0'da tum kompozisyon + DropShadowEffect (piksel shader yazilim yolu) + Viewport3D yazilim rasterizer'da kosar; en pahali sira (beklenen): Viewport3D tilt > hareketli ogede DropShadowEffect (her karede yeniden blur) > BlurRadius 28 overlay golgesi (yalniz acilis/kapanis) > opacity animasyonlari.

**Etki:** Kullanicinin birincil senaryosu (tepsiden build, gosterge ekranda) zayif/entegre/eski GPU'lu ya da RDP/VM oturumundaki makinede build'le AYNI cekirdekler icin yarisir: gosterge animasyonu build suresince surekli 30 fps yazilim kompozisyonu demektir. 2 cekirdekli makinede tek cekirdegin %40'i (olculen, donanim hizlandirmali deger) makinenin %20'sidir; Tier 0'da daha yuksek (olculmeli).

**Etki (doğrulayıcı düzeltmesi):** Olculen (donanim hizlandirmali): gosterge dongusu 59,5 kare/s, tek cekirdegin %10,2'si; statik kare 32,2 kare/s, %3,7; tepsi rebuild'inde App 1.853 Mcycles/s (bulgudaki 1,44 G eski olcum). Tier 0 maliyeti bilinmiyor.

**Çözüm:** MotionSettings'e ikinci sinyal, AYNI kod yoluyla: IMotionSignal'in bilesik implementasyonu (or. CompositeMotionSignal: OS sinyali && render tier sinyali). Yeni RenderTierMotionSignal: AnimationsEnabled => (RenderCapability.Tier >> 16) > 0; RenderCapability.TierChanged → Changed. App.xaml.cs:55'te tek satir degisir; tum tuketiciler (MotionGate, TrayBuildIndicatorController.SetAnimationsEnabled, ConsoleView tilt, GraphView, ProjectRow, StickyRibbon) mevcut reduced-motion dalini kullanir — yeni dal yazilmaz. Acilista tier bir kez stderr/log'a yazilir (tani). Tier 0'da ayrica iki tray DropShadowEffect'i kaldirmak icin statik gosterge zaten reduced-motion modunda (controller 'mod takasi'); golgeler statik kalirsa maliyet tek kare. Ayri ve daha genis oneri (kullanici karari): pil tasarrufu modunda da ayni sinyal (N4-9).

**Değişmezler:** §14.5 motion kurallari: reduced-motion tek yol, sure otoritesi Motion.xaml — korunur; yeni sure/egri eklenmez. 'Canli pencere != ekran disi cizim' kurali: Tier 0 davranisi RenderTargetBitmap ile dogrulanamaz — gercek Tier 0 oturumunda (RDP ya da HKCU\Software\Microsoft\Avalon.Graphics\DisableHWAcceleration=1 ile test makinesinde) gozle/olcumle dogrulanir.

**Test fikri:** Sahte IMotionSignal cifti ile CompositeMotionSignal: biri false → AnimationsEnabled false; herhangi biri Changed → tek Changed; MotionSettings Duration.* kaynaklari 0'a iner (mevcut MotionSettings testleri deseni).

**Doğrulayıcı notu:** Kod dogru: src/ altinda RenderCapability/ProcessRenderMode/IsRemoteSession yok; Services/SystemParametersMotionSignal.cs:17 tek sinyal. Etki tamamen tahmin: Tier 0'da hicbir sey olculmedi ve hedef makinelerin (zayif CPU/RAM) yazilim render'ina dustugune dair veri yok. Olcum, tepside maliyetin render tarafinda olmadigini gosteriyor: tepsi rebuild'inde App 1.853 Mcycles/s'in 1.385'i UI thread'i, render thread'i yalniz 248. Ayrica bulgunun '@30 fps' iddiasi olcumle uyusmuyor: TrayBuildIndicator.xaml.cs:48 DesiredFrameRate=30 yaziyor ama gosterge testi dongude 59,5 kare/s olctu.

#### N4-6 — Bellek tabani (App Private 194 MB + Supervisor 151 MB bosta; kosu sonrasi 381 + 217 MB geri verilmiyor) icin hicbir runtime ayari yok — iki surec de varsayilan GC/JIT ile kosuyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Supervisor/BuildOrchestrator.Supervisor.csproj` satır 12-15 — PropertyGroup: yalniz OutputType + TargetFramework; GC/Tiered ayari yok
- `src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` satır 23-39 — PropertyGroup: GC/Tiered/R2R ayari yok
- `Directory.Build.props` satır 1-26 — Ortak ayarlar: GC/Tiered ayari yok

**Kanıt:** Derlenmis runtimeconfig'ler (src/BuildOrchestrator.App/bin/Release/net10.0-windows/BuildOrchestrator.App.runtimeconfig.json ve supervisor/BuildOrchestrator.Supervisor.runtimeconfig.json) configProperties'te System.GC.* ve System.Runtime.TieredPGO tasimiyor → varsayilanlar: Workstation + Concurrent GC, TieredPGO acik, ConserveMemory 0. src/ altinda GCSettings / GC.Collect / SetProcessWorkingSetSize / EmptyWorkingSet cagrisi yok (grep). Olcum: Supervisor Private 151 MB bosta (CPU ~0) → aktivasyon Sync'inden sonra 242 MB, geri inmiyor; App kosu boyunca Private 194 → 381 MB, tepside geri vermiyor. Supervisor thread sayisi 9-23 (olcum).

**Etki:** 8 GB makinede arac bosta ~345 MB, bir kosudan sonra ~600 MB private tutar (VS + tarayici yaninda). D2-3/D2-4 ile ayni kok; burada yapilandirma yeri ve resmi davranis netlestirilir.

**Etki (doğrulayıcı düzeltmesi):** App Private: acilis Sync'i sonrasi 158-195 MB (managed canli 15,5 MB); iki rebuild sonrasi 433 MB, zorla GC ile 319 MB, managed canli 74 MB (tek 13,7 MB char[]). Supervisor: managed canli 2,0 MB; Private 150 MB (zorla GC 112-148); 4 rebuild sonrasi 336 MB; aktivasyon Sync serisinde 226-324 MB arasi salinim, monoton sizinti yok.

**Çözüm:** Her biri csproj <PropertyGroup> ile (SDK runtimeconfig.json'a yazar), olcum sirasiyla: (1) Supervisor: <ConcurrentGarbageCollection>false</ConcurrentGarbageCollection> — arka plan GC is parcacigi ve onun ayrilmis yapilari kalkar; UI'si olmayan surecte gen2 duraklamasi zararsiz. (2) Supervisor + App: <TieredPGO>false</TieredPGO> — tier0 enstrumantasyon kodu/sayaclari uretilmez; bellek kazanci KUCUK ve olculmeli. (3) Her iki surec: System.GC.ConserveMemory (csproj: <ItemGroup><RuntimeHostConfigurationOption Include="System.GC.ConserveMemory" Value="5" /></ItemGroup>) — yalniz gen2/LOH parcalanmasi yuksekken sikistirmayi tetikler; 12 MB JSON parse'inin LOH'u icin Supervisor'da anlamli, kazanc olculmeli. (4) App'te ConcurrentGarbageCollection KAPATILMAZ (UI thread'inde bloklayan gen2 = takilma). (5) ServerGarbageCollector zaten false (varsayilan) — dokunma. (6) TieredCompilation KAPATILMAZ (kapatmak acilis JIT suresini artirir). (7) Kosu sonu ve tepsiye inis 'rahatlama' adimi (D2-3): GCSettings.LargeObjectHeapCompactionMode=CompactOnce + GC.Collect(2, Aggressive) — kok neden olan kopyalar (D2-1/D6-1) cozulmeden tek basina az kazandirir. Hangi ayarin kac MB kazandirdigi BILINMIYOR — her biri ayri olculmeli; olcumsuz toplu acma.

**Çözüm (doğrulayıcı düzeltmesi):** Ayarlar tek tek ortam degiskeniyle A/B olculmeden csproj'a yazilmasin (DOTNET_gcConcurrent=0 yalniz Supervisor; DOTNET_TieredPGO=0; DOTNET_GCConserveMemory). App tarafinda olculen 114 MB'lik geri alinabilir cop (433 -> 319) icin 'kosu bitti ve pencere gizli' aninda tek seferlik toplama adayi; asil kaynak tahsis hacmi (N3-9).

**Değişmezler:** Velopack yalniz App'te — etkilenmez. Supervisor klasoru build ciktisindan kopyalandigi icin (App.csproj:72-84) Supervisor csproj'una yazilan ayar runtimeconfig.json ile birlikte otomatik tasinir. Ayar tek yerde: ortak olanlar Directory.Build.props'a DEGIL (Tests/Core'u da etkiler), ilgili iki csproj'a.

**Doğrulayıcı notu:** Kod dogru: App/Supervisor csproj ve Directory.Build.props'ta GC/Tiered/ConserveMemory ayari yok (grep 0 sonuc). Bellek sayilari yeni olcumle guncellendi. Onerilen ayarlarin hicbirinin kazanci olculmedi; Supervisor'da managed canli heap yalniz 2,0 MB iken Private 150 MB oldugu icin LOH sikistirma (ConserveMemory) ile kazanc beklentisi dayaniksiz.

#### N4-7 — ReadyToRun yok ve Supervisor dosyalari publish'ten degil BUILD ciktisindan kopyalaniyor: App'e PublishReadyToRun eklemek Supervisor/Core/Contracts kopyalarini R2R yapmaz

| Alan | Değer |
|---|---|
| Önem | kozmetik (acilis olcumu dusuk cekirdekte yapilana kadar) |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | orta / orta |

**Konum:**
- `scripts/package.ps1` satır 97 — dotnet publish $appProj -c Release -r win-x64 --self-contained false — R2R parametresi yok
- `src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` satır 71-85, 121-129 — _ResolveSupervisorFiles: GetTargetPath → Supervisor'un bin dizini globlanir; AddSupervisorToPublishList bu IL dosyalarini ResolvedFileToPublish'e ComputeResolvedFilesToPublishList'ten SONRA ekler
- `scripts/package.ps1` satır 113 — '--framework', 'net10.0-x64-desktop' — framework-dependent; RID zaten win-x64 (R2R'in on kosulu saglaniyor)

**Kanıt:** Release cikti boyutlari (IL): BuildOrchestrator.App.dll 2.101.248 B, Core.dll 337.408, Contracts.dll 119.808, AvalonEdit 622.592, H.NotifyIcon 398.848 + Wpf 194.048, Velopack 279.040, Mvvm 146.760, DI 95.568 + 65.832; supervisor\ altinda Core/Contracts/DI'nin IKINCI IL kopyasi + Supervisor.dll 122.368. Toplam ~4,4 MB IL App surecinde, ~0,74 MB Supervisor surecinde her acilista JIT edilir (framework kendi R2R goruntuleriyle gelir). Olcum (8 lp, hizli makine): pencere 1,33 s, Supervisor 1,38 s. Cikti klasoru 70 dosya / 12 MB; 6 .pdb (App.pdb 364 KB dahil) dagitima giriyor.

**Etki:** Zayif CPU'da JIT suresi oransal buyur (acilis 1,33 s'nin ne kadarinin JIT oldugu olculmedi — D8-12). JIT'lenen kod private bellektir; R2R kodu image-backed'dir (paylasilabilir/atilabilir sayfa) → bosta Private'i da dusurur (miktar olculmeli).

**Çözüm:** (1) App.csproj ve Supervisor.csproj'a <PublishReadyToRun>true</PublishReadyToRun> (yalniz publish'te etkili; Debug/test build'i degismez). (2) Supervisor icin publish akisinda kaynak degismeli: AddSupervisorToPublishList'te (yalniz publish yolunda) Supervisor'un Publish hedefi ayni RID ile ara bir dizine cagrilir (<MSBuild Projects=... Targets="Publish" Properties="RuntimeIdentifier=$(RuntimeIdentifier);SelfContained=false;PublishDir=$(IntermediateOutputPath)supervisor-publish\" />) ve _SupervisorFiles o dizinden toplanir; build yolu (CopySupervisorOutput) aynen kalir. Dosya KUMESI tanimi tek target'ta kalmali (kopya yasak) — _ResolveSupervisorFiles'a 'kaynak dizin' parametresi. (3) Composite/self-contained'e GECILMEZ (framework-dependent karar korunur). (4) Boyut: R2R IL+native tasir, dll'ler tipik 2-3x buyur (tahmin) → Velopack paket/delta boyutu artar; olc. (5) pdb'ler publish'ten cikarilabilir (ayri, kucuk: toplam ~0,66 MB) — tanilama ihtiyaci kullaniciya sorulmali. Dogrulanmasi gereken: SDK'nin R2R derleme listesi sonradan eklenen ResolvedFileToPublish ogelerini kapsamaz (bu yuzden (2) gerekli) — verify-publish.ps1 ile publish ciktisindaki dll'lerde R2R basligi kontrol edilerek kanitlanmali.

**Değişmezler:** PublishSingleFile yasagi (App.csproj:134-137) ve supervisor\ klasor duzeni (SupervisorLayout) korunur; publish komutunun tek sahibi package.ps1 kalir (parametre csproj'da, script degismez). PublishLayoutTests guard'lari gecmeli. Velopack yalniz App.

**Doğrulayıcı notu:** Kod dogru: scripts/package.ps1:97 R2R'siz publish; App.csproj:71-85 ve :121-129 Supervisor dosyalarini build ciktisindan toplar. Etki olculmedi: acilisin ne kadarinin JIT oldugu bilinmiyor ve 'SDK sonradan eklenen dosyalari R2R derlemez' iddiasi bulgunun kendi beyaniyla dogrulanmamis. Kullanicinin birincil senaryosuna (tepside derleme) etkisi yok; yalniz acilis.

#### N4-8 — Antivirus on-access taramasi: arac kosu basina yuzlerce kisa omurlu surec + binlerce dosya acilisi uretiyor; dislama onerisi ne README'de ne ARCHITECTURE'da var

| Alan | Değer |
|---|---|
| Önem | kozmetik (dokuman) |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `ARCHITECTURE.md` satır 768-771 — §7 kendi olcumu: soguk okumada baskin maliyet 'per-file open overhead (on-access scanning)' — 8,9 ms/dosya seri
- `README.md` satır 73-84 — Requirements: antivirus/dislama, RAM/CPU alt siniri yok
- `src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs` satır 22-28 — UseSharedCompilation=false, nodeReuse:false → proje basina taze MSBuild.exe + csc.exe (+conhost)

**Kanıt:** grep -i 'defender|antivirus|exclusion' README.md ARCHITECTURE.md → yalniz §7'deki tek cumle (dislama onerisi yok; D9-9'u dogrular). Olcum: 152 projelik rebuild'de ayni anda 4 MSBuild + 1-4 csc + 5-9 conhost (+cmd, lc); no-op Rebuild bile 152 MSBuild invoke'u (38-45 s). Her surec baslangici ve her obj/bin yazimi on-access taramaya girer; arac ayrica %LOCALAPPDATA%\BuildOrchestrator altina satir basina flush'li log (D9-6/D10-3) ve 12 MB JSON yazar (D8-1). Antivirusun bu makinedeki/kurumsal makinelerdeki payi OLCULMEDI.

**Etki:** Kurumsal AV'li zayif makinede surec baslatma ve dosya acma maliyeti carpanla buyur; arac bunu degistiremez ama kullaniciya soyleyebilir. Kazanc buyuklugu tahmin; olcum yolu asagida.

**Çözüm:** Dokuman (README 'Troubleshooting'/'Requirements' + ARCHITECTURE §20 Known limits): onerilen dislamalar — repo koku (ya da en azindan **\bin, **\obj), %LOCALAPPDATA%\BuildOrchestrator (logs + cache), surec dislamalari MSBuild.exe / csc.exe / VBCSCompiler.exe; 'kurumsal politika izin veriyorsa' kaydiyla. Arac kendisi dislama YAZMAZ (yonetici hakki + guven siniri §21). Kod tarafinda surec sayisini azaltan iki mevcut bulgu bu maliyeti dogrudan dusurur: D3-5 (invoke basina conhost) ve D4-5 (her Build'de restore prologu).

**Değişmezler:** Shell-out MSBuild ve UseSharedCompilation=false (§9.2) degismez. Arac sistem ayarina yazmaz.

**Doğrulayıcı notu:** Dokuman boslugu dogru: README.md:73-84 Requirements'ta ve ARCHITECTURE.md'de antivirus dislama onerisi yok (grep 'defender|antivirus|exclusion' eslesme yok). Kod kusuru degil; antivirus payi hic olculmedi.

#### N4-9 — Pil/termal: bosta on planda 131-139 Mcycles/s, tepside 50-54 Mcycles/s surekli uyaniklik; guc durumuna (pil, pil tasarrufu) bakan hicbir kod yok

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/Services/SystemParametersMotionSignal.cs` satır 17 — Hareket karari yalniz ClientAreaAnimation; guc durumu girdisi yok
- `src/BuildOrchestrator.App/Controls/MotionTokens.cs` satır 32 — DecorativeFrameRate = 30 (imlec/dekoratif saatler)

**Kanıt:** Olcum: on planda bosta App 131-139 Mcycles/s (tek cekirdegin %3,4-5,2'si), 31-35 thread; tepside bosta 50-54 Mcycles/s (2026-09-14 duzeltmesi sonrasi ~4,6 M olculmustu → ~10x; neden bilinmiyor). grep: PowerLineStatus / PowerModeChanged / SystemEvents / IsRemoteSession src/'de yok. Kaynak envanteri D1-0/D1-2/D7-4'te (iki imlec = dort saat, 55-60 uyanis/s) — burada yeniden kesfedilmedi.

**Etki:** Dizustunde surekli 30-60 Hz uyanis CPU paketinin derin C-state'e inmesini engeller; 2 cekirdekli dusuk frekansli islemcide ayni mutlak is (131 Mcycles/s) cekirdegin daha buyuk yuzdesidir (or. 1,6 GHz efektifte ~%8 — turetme: 131e6 / 1,6e9; gercek makinede olculmeli). Tepsideki 50 M'nin kaynagi bulunmadan pil etkisi kapanmaz.

**Etki (doğrulayıcı düzeltmesi):** On planda bosta App 181 Mcycles/s (UI 68 + render 71 + surucu thread'leri 41). Tepside bosta 37 Mcycles/s (%90'i UI thread'i; trace'te UI %1,9 mesgul, isin %90,5'i 'diger dispatcher isi', DispatcherTimer yonetimi gorunuyor). Kosu sonrasi tepside bosta UI thread'i 129 Mcycles/s.

**Çözüm:** Oncelik sirasi: (1) tepsideki 50 M regresyonunun kok nedeni (ayri bulgu/ajan konusu; ETW ya da dotnet-trace ile tepside 30 s ornekleme) — bu, zayif makine icin en yuksek kaldiracli bosta kalem. (2) D1-2/D7-4 (imlec saatleri tek saate/tek frame rate'e). (3) Istege bagli ucuncu hareket sinyali: pil tasarrufu acikken (GetSystemPowerStatus.SystemStatusFlag == 1) azaltilmis hareket — N4-5'teki CompositeMotionSignal'e bir uye daha; kullanici karari gerekir (gorsel davranis degisir).

**Çözüm (doğrulayıcı düzeltmesi):** Ilk is: kosu sonrasi tepsideki 129 Mcycles/s'in kaynagi (kosu biterken durdurulmayan saat/animasyon). Pil tasarrufu sinyali kullanici karari; olcumden once eklenmesin.

**Değişmezler:** §14.5: sonsuz animasyonlar IsVisible'a kapili olmali (D6-4/D7-1 eksikleri ayri). Yeni sinyal ayni reduced-motion yolundan gecer.

**Doğrulayıcı notu:** Kod dogru: guc durumunu okuyan kod yok. Sayilar yeni olcumle degisti ve yeni bir olgu cikti: kosu SONRASI tepside bosta UI thread'i 129 Mcycles/s — kosu oncesi tepsi toplaminin (37) ~3,5 kati; yani bir kosudan sonra tepside bir sey donmeye devam ediyor (kaynak belirlenmedi).

#### N4-10 — 'Her sey guncel' maliyeti moda bagli: Build 0 invoke, Rebuild 152 invoke (olcum 38-45 s, 12.700 satir) — zayif makinede oransal ve kisayolun hangi modu baslattigi belirleyici

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.Core/MsBuild/MsBuildArguments.cs` satır 22-28 — Her invoke taze MSBuild.exe (nodeReuse:false) — invoke basina sabit yuk kacinilmaz
- `ARCHITECTURE.md` satır 2152-2166 — Profil tablosu: Balanced 4 isci, %70 cap, BelowNormal

**Kanıt:** Olcum: no-op Rebuild 152 invoke, 38-45 s, 12.700 satir / 3,1 MB IPC. Turetme: 152 invoke / 4 isci / ~41 s → invoke basina ~1,1 s isci-zamani (D4-7: ~200 ms surec + ~0,47 s degerlendirme tabani ile uyumlu). 2 isciye kisilmis 4 lp makinede ayni is, cekirdek hizi esit varsayilsa bile ~82 s; cekirdek basina hiz yarisiysa ~160 s (turetilmis tahmin, olcum degil).

**Etki:** Kullanici tepsiden kisayolla Rebuild baslatirsa zayif makinede dakikalar surer; Build modu ayni durumda MSBuild hic cagirmaz. Bu bir kusur degil, mod semantigi; ama zayif makinede varsayilan/kisayol seciminin maliyeti buyuk.

**Etki (doğrulayıcı düzeltmesi):** No-op Rebuild (152 invoke): 8 islemci 25,4-29,7 s; 4 islemci 28,6-32,7 s; 2 islemci 51,0-66,4 s. Tepsiden Ctrl+Shift+Space Build baslatir (tus -> runStarted 5,68 s).

**Çözüm:** Kod degisikligi onerilmiyor (Rebuild'in anlami 'hepsini yeniden derle'). Dokuman: README kullanim bolumunde 'gunluk is icin Build; Rebuild yalniz cikti bozuksa' + global kisayolun hangi modu baslattiginin acik yazilmasi. Maliyeti dusuren mevcut bulgular: D4-5 (restore prologu), D4-1 (Summary cift satir), D3-5 (conhost).

**Değişmezler:** MSBuild arguman sozlesmesi (§9.2) sabit.

**Doğrulayıcı notu:** Mod semantigi dogru, kod degisikligi onerilmiyor. Sayilar eskidi: no-op Rebuild bu olcumde 25,4-29,7 s (bulgudaki 38-45 s degil); dusuk cekirdek artik turetme degil olcum.

#### N4-11 — Gomulu fontlar: 7 OTF, 1,18 MB — bellek tabaninda ikincil kalem; hepsinin kullanilip kullanilmadigi dogrulanmali

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- `src/BuildOrchestrator.App/BuildOrchestrator.App.csproj` satır 144-146 — <Resource Include="Fonts\*.otf" /> + CompositeFont — joker ile tum dosyalar gomulur
- `src/BuildOrchestrator.App/Fonts/GeistMonoConsole.CompositeFont` satır 12-22 — Bildirilen yuzler: Light + Normal; FamilyMap ./#Geist Mono

**Kanıt:** Dosya boyutlari (ls): Geist-Regular 157.508, Geist-Medium 162.304, Geist-SemiBold 164.780, GeistMono-Light 174.364, GeistMono-Regular 171.952, GeistMono-Medium 174.136, GeistMono-SemiBold 177.224 → toplam 1.182.268 B (App.dll'in 2,1 MB'inin ~%56'si). WPF pack-URI fontlarini kullanilan yuz bazinda acar; 7 yuzun hangilerinin fiilen cizildigi bu turda dogrulanmadi. Private 194 MB'a katkisi en fazla ~1-2 MB mertebesinde (tahmin) — oncelik dusuk.

**Etki:** Kucuk; bellek hedefi icin N4-6, D2-1, D2-8 cok daha buyuk kalemler.

**Çözüm:** Yalniz envanter: XAML/Tokens'ta FontWeight kullanimi taranip hic istenmeyen agirlik (or. GeistMono-Medium/SemiBold) varsa csproj'dan cikarilir. Kullanilmayan yoksa dokunma.

**Değişmezler:** OFL lisans dosyasi (GEIST-LICENSE.txt) dagitimda kalir; CompositeFontTests guard'i.

**Doğrulayıcı notu:** Dosyalar dogru: App/Fonts altinda 7 OTF toplam 1.182.268 B; App.csproj:144-146 joker ile gomuyor. Bellek katkisi olculmedi; bulgu zaten 'yalniz envanter' diyor.

#### N4-12 — Zayif makine icin onerilen varsayilanlar (tablo) — bulgu degil, N4-1..N4-11'in ozeti

| Alan | Değer |
|---|---|
| Önem | kozmetik (ozet) |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta [DEĞİŞMEZ/KARAR ÇATIŞMASI] |
| İş / risk | uzun / orta |

**Konum:**
- `src/BuildOrchestrator.Core/ProcessControl/PerfProfile.cs` satır 30-36 — Tablonun tek sahibi — tum satirlar buradan turetilir

**Kanıt:** Ayar | Bugun (kod) | Zayif makine onerisi (<=4 lp ya da <=8 GB) | Dayanak
--- | --- | --- | ---
Ilk acilis profili | Balanced sabit (RunViewModel.cs:823) | Light (kullanici secimi yoksa) | N4-1
Isci sayisi | 6/4/2 sabit | min(profil, ceil(lp/2), floor(bosRAM / isci butcesi)), en az 1 | N4-1, N4-2
CPU cap | %70 / %40 makine toplami | degismez; fiili cekirdek karsiligi UI/konsolda gosterilir | N4-2
Kosu oncesi bellek | kontrol yok | bos RAM < 1 isci butcesi → 1 isci + konsol uyarisi | N4-3
OOM siniflandirmasi | 'exit N' = derleyici kaniti | 'out of memory' = kanitsiz hata | N4-3
Icerik karari IO genisligi | 16 sabit (5 yerde) | clamp(lp*2, 4, 16), tek sabit | N4-4
Hareket | yalniz OS ClientAreaAnimation | + RenderCapability.Tier 0 → azaltilmis hareket (ayni yol) | N4-5
Supervisor GC | concurrent (varsayilan) | ConcurrentGarbageCollection=false, ConserveMemory olculerek | N4-6
App GC | concurrent | degismez; TieredPGO=false olculerek | N4-6
JIT | her acilista IL JIT | PublishReadyToRun (App + Supervisor publish yolu) | N4-7
Antivirus | dokumanda yok | README/§20'de dislama onerisi | N4-8
Gunluk mod | kullaniciya bagli | Build (0 invoke); Rebuild yalniz gerektiginde | N4-10

**Etki:** Tek bakista uygulanacak varsayilanlar; her satirin sayisal kazanci ilgili bulgunun how_to_measure adimiyla olculmeden iddia edilmez.

**Çözüm:** Uygulama sirasi onerisi: N4-1 (+N4-2 gosterimi) → N4-3(a) uyari → N4-5 → N4-4 → N4-6 (olcumle) → N4-7 → N4-8 dokuman.

**Değişmezler:** Tum satirlar CLAUDE.md degismezleriyle uyumlu; N4-1 bilincli 'sabit tablo' kararini tersine cevirir (gerekce ilgili bulguda).

**Doğrulayıcı notu:** Bulgu degil ozet tablo. Iki satiri duzeltilmeli: isci butcesi olcumde ~2 GB commit/isci; 'IO genisligi clamp(lp*2,4,16)' ve 'lp/2' satirlari olculmemis oneridir. 'Tum satirlar degismezlerle uyumlu' cumlesi yanlis: ilk iki satir ARCHITECTURE §11.1'in belgeli 'uc sabit profil' kararini degistirir.

**Doğrulayıcının eklediği noktalar:**
- ARCHITECTURE.md §11.2 ('The App is in no job at all') + RunCoordinator perf uygulamasi: App Normal oncelikte ve job disinda, MSBuild Balanced'da BelowNormal; dusuk cekirdekte UI isi derlemeden CPU aliyor — olcum: 2 islemcide on plan rebuild 66,4 s, tepsi 51,0 s (App 1.836'ya karsi 912 Mcycles/s), 4 islemcide 32,7'ye karsi 28,6 s.
- Core/ProcessControl/JobObject.cs:106-123: CPU cap makine toplaminin yuzdesi oldugu icin surec yakinligi benzetimi cap'i ve RAM'i kucultmedi; 2/4 islemci sayilari gercek zayif makine icin alt sinirdir, gercek 2 islemci / 8 GB kosusu yok.
- Supervisor/RunCoordinator.cs:845: kosu, commit sinirina bakmadan baslar; olculen isci basina ~2 GB commit (Light +4,4 / Balanced +8,4 / Full +12,2 GB) 8 GB'lik makinede Balanced'i fiziksel bellegin ustune cikarir.
- Tus -> runStarted gecikmesi 8 islemcide 5,58-7,3 s (harici kok basina 'git ls-remote origin' ~1,2 s dahil); bu planlama penceresi cap'siz ve 16-yollu (IncrementalRunBinder.cs:74, 105, 162) — 2/4 islemcide hic olculmedi.
- Kosu sonrasi tepside bosta UI thread'i 129 Mcycles/s (kosu oncesi tepsi toplami 37): N4-9 bunu icermiyor; kaynak kodda belirlenmedi.

**Temiz bulunan alanlar:**
- MSBuild cagrisi -m / maxcpucount tasimiyor (MsBuildArguments.cs:22-28): isci basina tek MSBuild dugumu; arac kendi paralelligi disinda gizli bir MSBuild paralelligi acmiyor.
- ServerGarbageCollector hicbir yerde acilmamis (csproj/props grep) — iki surec de Workstation GC; zayif makine icin dogru varsayilan.
- Supervisor bosta ~0 CPU (olcum: 0,0-1,1 Mcycles/s) — motor bosta uyanmiyor.
- Kapanis: App oldurulunce Supervisor 81 ms'de gidiyor, 3 s sonra surec kalmiyor (olcum) — zayif makinede artik surec/bellek birakma riski yok.
- Build modunda her sey guncelken 0 MSBuild invoke (ledger pre-skip) — zayif makinede en ucuz yol zaten mevcut.
- CPU cap ve oncelik yalniz inner job'a yaziliyor; App hicbir job'da kisilmiyor (§11.2) — UI thread'i build tarafindan dogrudan kisilmiyor.
- BlurEffect ve BitmapCache kullanimi yok (grep); efekt envanteri 4 DropShadowEffect tanimi + 1 Viewport3D + 1 RenderTargetBitmap ile sinirli.
- Tepsi gostergesi dongusu DecorativeFrameRate=30 ile sinirli ve sonsuz RepeatBehavior degil (TrayBuildIndicator.xaml.cs:11, 48) — her gecis sonunda 'devam mi' karari var.
- PublishSingleFile reddi ve framework-dependent publish (package.ps1:97) R2R ile uyumlu: RID zaten win-x64 veriliyor.

**Açık sorular:**
- Hedef makinelerin gercek donanimi nedir (mantiksal islemci sayisi, RAM, SSD/HDD, GPU, RDP/VDI kullanimi)? Kisma formulundeki esikler (lp/2, isci basina butce) buna gore sabitlenmeli.
- Isci basina bellek butcesi: OSYS'in en buyuk projelerinde csc tepe Private'i kac GB? (§11.1 '~1 GB' diyor; olcum makinesinde 4 isci toplam WS tepe 1,85 GB.) BO_MEASURE_PERF olcumu zayif makinede kosulabilir mi?
- 2 lp makinede Light'in Idle onceligi kalsin mi? VS aktifken build'in ac kalmasi istenen davranis mi?
- RenderCapability.Tier 0 → azaltilmis hareket otomatik mi olsun, yoksa ayarlarda acik bir secenek mi? (Uygulama-ici hareket toggle'i bilerek yok: SystemParametersMotionSignal.cs:6.)
- PDB'ler dagitimda kalmali mi (tanilama) yoksa publish'ten cikarilabilir mi?
- Kurumsal antivirus politikasi kullanici duzeyinde dislama eklemeye izin veriyor mu? Vermiyorsa N4-8 yalniz bilgilendirme olur.
- OOM sonrasi tek isciyle otomatik yeniden deneme istenir mi (yeni davranis), yoksa yalniz dogru siniflandirma + uyari yeterli mi?


### N5-motor-bellegi

#### N5-1 — Supervisor'in bosta tuttugu 150-242 MB canli veri degil, Sync/planlama copu: hicbir defter ornegi surec boyu tutulmuyor, her Sync +72..+91 MB Private birakiyor ve bosta GC tetiklenmedigi icin geri verilmiyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) SupervisorHost.cs:43-57 (fabrikalar), 160-176 (SyncWorkspaceAsync); Program.cs:94, 97, 201 (hashes.Flush); BuildPlanBuilder.cs:43; SyncWorkspaceService.cs:324
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 43-57 — WorkspaceServices.Default: Sync/Optimize fabrikalari HER cagrida yeni EvaluationCache + SourceHashCache + BuildStateStore kurar (mevcut kod)
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 160-176 — SyncWorkspaceAsync: workspace.Sync(root) ile kurulan servis yerel; donuste hicbir alan onu tutmaz
- `src/BuildOrchestrator.Supervisor/Program.cs` satır 92-97, 131-133 — BuildRunPlan: sourceHashes ve cache YEREL degisken (kosu basina yeni ornek); yalniz stateStore (satir 31) surec boyu ve o da bellekte map tutmaz
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 148-166, 195-208 — Load: File.ReadAllText + Deserialize; Flush: ToDictionary kopyasi + Serialize -> string -> WriteAllText
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 96-104, 142-151 — Ayni desen: ReadAllText/Serialize-to-string
- `src/BuildOrchestrator.Core/Workspace/SyncWorkspaceService.cs` satır 324 — hashes.Flush() kosulsuz
- `src/BuildOrchestrator.Core/Planning/BuildPlanBuilder.cs` satır 43 — cache.Flush() kosulsuz

**Kanıt:** OLCUM (measure-release-2026-10-01.log): Supervisor Private A1 150,8 MB (acilis Sync'inden sonra bosta) -> C1 pencereye donus Sync'i 241,7 MB (+90,9) -> kosu icinde 164,0 -> 138,3 -> en dusuk 137,2 MB (D2-6) -> kosu sonu 144,9 -> kosu-sonu Sync'i F0 216,6 MB (+71,7) -> F1/G2 bosta 209,5 MB, 40 s boyunca degismiyor (CPU 0,0). KOD: Sync donunce cache ornekleri erisilemez (fabrika her cagrida new; alan yok). TURETILMIS (n5_cache_live_size.py, gercek dosyalar): source-hash 24.588 giris, ort. anahtar 120 kr -> nesne grafi ~12,6 MB; ReadAllText string'i 12,5 MB (LOH). evaluation 2.761 giris -> grafi ~11,2 MB; string 11,8 MB (LOH). Bir Sync'te 2 Load + 2 Flush = 4 buyuk string = 48,6 MB LOH + 23,8 MB nesne grafi = >=72,4 MB cop; olculen +71,7 / +90,9 MB ile ayni mertebe. Yani bostaki 150-242 MB'in kaynagi, olu ama toplanmamis Sync copudur; GC tahsis tetikli oldugu icin bosta duran surecte calismaz.

**Etki:** Zayif makinede motor bostayken 150-240 MB commit tutuyor; pencereye her donus (5 s esigi) ve her kosu sonu Sync'i bu degeri yeniden 210-240 MB'a cikariyor. Kosu icinde olculen 137 MB tabani, arka plan GC'lerinin bile ~100 MB'i geri verebildigini gosteriyor.

**Etki (doğrulayıcı düzeltmesi):** Olculen (2026-10-02): Supervisor Private acilis Sync'i sonrasi 148-174 MB; rebuild icinde 155-283 MB; kosu sonrasi bosta 302 MB (D ornegi, CPU 1,1 Mcycles/s); 4 rebuild sonrasi 336 MB; aktivasyon Sync serisi 243 -> 324 -> 226 -> 282 -> 261 -> 297 -> 309 MB (monoton degil). Canli heap 2,0 MB. Zorla (siradan) GC sonrasi taban 112-148 MB. Yani 'kazanc >=70-105 MB' yalniz TEPE degerlerden (240-336 MB) ~150 MB'a inis icin gecerli; 150 MB'in altina inis siradan GC ile olculmedi (6-38 MB verdi).

**Çözüm:** Secenek B (birincil): is bitiminde tek bir 'rahatlama' adimi. Supervisor'da TEK yardimci (kopya yasak), iki cagri noktasi: (1) SupervisorHost komut dongusunde Sync/Clean/Optimize/Pull/Checkout donusunden sonra, (2) RunCoordinator.ExecuteRunAsync finally'sinde 'await pump' sonrasi (satir 625-647 kilit DISINDA). Icerik: GCSettings.LargeObjectHeapCompactionMode = CompactOnce; GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true). Aggressive modu (.NET 7+) tam olarak 'surec bosa gecti, commit'i geri ver' icindir. Kapi: coordinator.IsRunActive false iken ve komut dongusunu geciktirmemek icin ~2 s sessizlikten sonra (App Sync'in hemen ardindan listBranches, kosu bitiminde Sync gonderiyor - D11-2/D11-4); bekleme tek bir Timer/CTS ile, yeni komut gelince iptal. Beklenen kazanc: ALT SINIR olculdu (241,7 -> 137 MB araligi kosu ici GC'lerle zaten goruldu, yani >=70-105 MB); UST SINIR taze motorun ilk Sync oncesi Private degeridir ve OLCULMEDI (bkz. N5-9). SetProcessWorkingSetSize/EmptyWorkingSet eklenmemeli: Private (commit) dusmez, yalniz WS sutunu duser ve sayfalar pagefile'a yazilir - zayif diskte zararli.

**Çözüm (doğrulayıcı düzeltmesi):** Oneri (Aggressive + compacting) yon olarak dogru ama kazanci OLCULMEDI: gcdump'in GC'si Aggressive degildi, dolayisiyla olcum oneriyi ne dogrular ne curutur. Once tek satirlik tani: rahatlama adiminda GC.GetGCMemoryInfo().TotalCommittedBytes / HeapSizeBytes ile surecin Private'i yan yana stderr'e yazilsin; TotalCommitted ~ Private ise kalan GC'nin bos bolgeleridir ve Aggressive hedefi tutar, degilse (yerel bellek) GC ayari ile cozulmez. LOH CompactOnce bu surecte anlamsiz (canli 2 MB, sikistirilacak bir sey yok). Rahatlama adimi kosu aktifken ve MSBuild child'i varken KOSMAMALI (IsRunActive kapisi dogru).

**Değişmezler:** Shell-out, nested job, stdout NDJSON, planlama Core'da ayni kalir; GC cagrisi yalniz Supervisor sureci icinde. Sync'in salt-okurlugu ve komut sirasi degismez (GC komut dongusunu bloklamadan, sessizlik sonrasi kosar).

**Test fikri:** Supervisor entegrasyon testi (SupervisorSandbox, --logs izole): syncWorkspace -> syncCompleted -> sessizlik suresi enjekte edilebilir saatle ilerletilir -> yardimcinin TAM bir kez cagrildigi (enjekte edilen Action sayaci) ve kosu aktifken HIC cagrilmadigi pinlenir. Kirmizi: bugun sayac 0.

**Doğrulayıcı notu:** Iki yarisi ayri degerlendirildi. (a) 'Canli veri degil' DOGRU ve artik OLCULDU: gcdump'ta Supervisor canli managed heap 1.989.713 B (acilis) ve 2.023.646 B (iki rebuild sonrasi); Private ayni anlarda 150 ve 154 MB. Kod da ayni seyi soyluyor: SupervisorHost.cs:43-57 fabrikalari her cagrida yeni EvaluationCache/SourceHashCache/BuildStateStore kurar, Program.cs:94,97 yerel degisken; hicbir alan defter tutmuyor. (b) 'Bosta GC tetiklenmedigi icin geri verilmiyor' nedenselligi OLCUMLE ZAYIFLADI: gcdump'in tetikledigi zorla tam GC Private'i yalniz 150 -> 112 MB (-38) ve 154 -> 148 MB (-6) indirdi. Yani siradan bir bloklayan gen2, 2 MB canli heap'e ragmen 110-148 MB commit birakiyor; kalan, toplanmamis nesne degil, GC'nin elinde tuttugu bos (commit'li) bolgeler ve/veya runtime'in yerel bellegi. Konumlarin hepsi dogru (SupervisorHost.cs:43-57, 160-176; Program.cs:92-97, 131-133; SourceHashCache.cs:148-166, 195-208; EvaluationCache.cs:96-104, 142-151; SyncWorkspaceService.cs:324; BuildPlanBuilder.cs:43).

#### N5-2 — Secenek A'nin bellek bedeli: 'surec boyu tek cache ornegi' bostaki canli tabani ~24 MB YUKSELTIR; RAM hedefi icin dogru bicim 'ornek tutma, string'siz oku/yaz + kirli bayragi'

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) SourceHashCache.cs:148-166, 195-208; EvaluationCache.cs:96-104, 142-151; SupervisorHost.cs:43-57; Program.cs:94-97, 201; BuildPlanBuilder.cs:43; SyncWorkspaceService.cs:324
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 195-208 — Load: ReadAllText (12,5 MB UTF-16, LOH) + Dictionary -> ConcurrentDictionary ikinci tablo kopyasi
- `src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs` satır 148-161 — Flush: 24.588 girisli ToDictionary kopyasi + Serialize string'i (12,5 MB, LOH); degisiklik yoksa da yazar
- `src/BuildOrchestrator.Core/Discovery/EvaluationCache.cs` satır 96-104, 142-151 — Ayni desen (11,8 MB string x2)
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 44-48 — Tek ornek yapilacaksa memoize edilecek fabrika (mevcut kod)

**Kanıt:** Turetilmis (gercek dosyalar, n5_cache_live_size.py): source-hash canli grafi ~12,6 MB (anahtar string'leri 6,5 MB + hash string'leri 3,7 MB + Entry 1,0 MB + dugum/bucket 1,4 MB); evaluation canli grafi ~11,2 MB (49.716 string, 9,7 MB). Toplam ~23,8 MB. evaluation'in yalniz gecerli kismi (Schema=1 ve diskte var: 196 giris) 3,93 MB JSON / 7,6 MB string; 2.565 giris sema-0 olu (D2-2/D9-2). D2-2 ve D8-1 'tek ornek' oneriyor; bu, Sync basina ~72 MB copu ve parse CPU'sunu kaldirir ama 23,8 MB'i KALICI canli veri yapar - bugun canli taban icinde bu veri yok (N5-1).

**Etki:** A (tek ornek): bosta +~24 MB kalici (olu girisler budanirsa ~20 MB), Sync basina cop ~72 MB -> ~0, parse/yazma CPU'su kalkar. A' (ornek tutmadan): kalici +0 MB; Sync basina LOH string'i 48,6 MB -> 0, nesne grafi 23,8 MB cop kalir (B toplar); yazma degisiklik yoksa hic olmaz.

**Etki (doğrulayıcı düzeltmesi):** Sync/Build basina ~12 MB JSON okuma + ~12 MB yazma kodda kesin; sure olculmedi. Ust sinir: tepsiden Build'de koreografi disi tum hazirlik (ag dahil) ~2,1 s olculdu, yani iki defterin okuma+yazmasi bu makinede bunun icinde. Bellek dalgalanmasi olculdu: aktivasyon Sync'leri arasinda Supervisor Private +81 / -98 / +56 / -21 / +36 / +12 MB.

**Çözüm:** RAM oncelikli makineler icin A': (1) Load'da FileStream + JsonSerializer.Deserialize(Stream) (UTF-16 ara string yok), (2) Flush'ta kirli bayragi (ekleme/guncelleme/silme olmadiysa donus) + FileStream'e Serialize, (3) SourceHashCache.Load'da Deserialize sonucunu dogrudan ConcurrentDictionary'ye kopyalamak yerine tek tablo. 'Tek ornek' (A) yalnizca D (talep uzerine motor) ile birlikte anlamli: motor bosta kapaniyorsa tutulan 24 MB de onunla gider ve ardisik Sync/Build'ler parse odemez. D yoksa A + B birlikte bosta ~24 MB fazladan tutar.

**Çözüm (doğrulayıcı düzeltmesi):** Kirli bayragi iki defterde ayri anlam tasir: EvaluationCache'te _entries yazimlari yalniz satir 58 ve 61'de; SourceHashCache'te satir 80 (ve Seed). SourceHashCache.Flush racy girisleri disarida biraktigi icin 'yazilmayan racy giris' sonraki Flush'ta yazilabilsin diye bayrak o durumda kirli kalmali (bulgunun kendi notu dogru). Ayrica PruneMissingUnderRoot zaten 'degismediyse yazma' kuralini uyguluyor (EvaluationCache.cs:128, SourceHashCache.cs:183) - ayni kural Flush'a tasinmali, ikinci bir yol acilmamali.

**Değişmezler:** Defter bicimi ve atomik temp+rename ayni; SourceHashCache'in racy-window kurali (satir 150-153) Flush'ta korunur - kirli bayragi 'racy oldugu icin yazilmayan giris'i de kirli saymali ki sonraki Flush yazsin. Kopya yasak: akis tabanli oku/yaz iki cache ve BuildStateStore icin tek yardimcida.

**Test fikri:** SourceHashCacheTests: degisiklik olmadan Flush -> dosya LastWriteTimeUtc ayni kalir (bugun kirmizi: yeniden yazilir). EvaluationCacheTests icin ayni.

**Doğrulayıcı notu:** Kod dogru: SourceHashCache.Load (195-208) File.ReadAllText + Deserialize<Dictionary> + ConcurrentDictionary'ye ikinci kopya; Flush (148-166) kirli bayragi olmadan ToDictionary + Serialize(string) + WriteAllText; EvaluationCache (96-104, 142-151) ayni desen. Diskte dosyalar 5.914.117 B ve 6.257.488 B ve ikisinin de mtime'i son kosunun ani (02:44) -> her Sync/Build'de yeniden yazildigi diskten de gorunuyor. 'Tek ornek +~24 MB kalici' sayisi TURETILMIS (bu oturumda yeniden hesaplanmadi) ama olcumle celismiyor: bugun canli heap 2,0 MB, yani defterler gercekten canli tabanda yok. D8-1'in 'tek ornek' onerisiyle catisma gercek ve N5-2'nin secimi (ornek tutma, akisla oku/yaz + kirli bayragi) dusuk RAM onceligine daha uygun.

#### N5-3 — Secenek D (talep uzerine motor) uygulanabilir ama bugunku kodda dort yerde butunlugu bozar: engineReady her seferinde Sync baslatir, kapali motora gonderim hata satiri yazar, biten kosunun log dizini motor belleginde, setPerfMode motoru bosuna uyandirir

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: orta |
| İş / risk | uzun / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) RunViewModel.cs:1788-1794 (setPerfMode yalniz IsMidRunLocked iken), 1826-1831, 2656-2668; SupervisorHost.cs:147-150; RunCoordinator.cs:184, 211-225, 727; EngineHost.cs:203-213, 252-266
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 203-213, 136-145 — ShutdownGracefullyAsync once _generation'i artirir -> exit watcher susar -> EngineExited ATESLENMEZ: bilincli kapatma 'engine died' ribbon'ini zaten gostermez (mevcut kod, private)
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 59, 120 — _outerJob alan olarak yasar; her StartAsync ayni outer job'a atar - nested job degismezi korunur
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 163-164 — SendAsync: _writer null ise InvalidOperationException('Engine is not running.')
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1826-1831, 2953 — Iki gonderim kapisi: TrySendAsync (10 cagri yeri) ve LoadProjectLogAsync; kapali motorda '[error] failed to send' yazar
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2656-2668, 1632-1633 — OnEngineReady: 'Engine ready' satiri + kosulsuz Appended Sync; RestartEngineAsync de ayni yoldan gecer
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 147-150, 160-176 — Sync komut dongusunu BLOKLAR: uyandirmada Sync giderse arkasindaki startRun onu bekler
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 184, 211-225, 727 — _lastRunDirectory yalniz motor belleginde; motor kapaninca getProjectLog -> logNotFound
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.AutoSync.cs` satır 107, 114 — App kosunun log dizinini zaten biliyor (_runLogDirectory <- RunStartedEvent.LogDirectory)
- `src/BuildOrchestrator.Supervisor/Program.cs` satır 24-35, 43-64, 219-230 — Motor acilisi: klasor olustur + InFlightLedger.Recover + job + host; cache YUKLEMEZ; MSBuild cozumu lazy ve static memo (_toolset) - her motor omrunde bir vswhere
- `src/BuildOrchestrator.App/Services/AutoSyncCoordinator.cs` satır 91, 157-163 — HeadWatcher App surecinde kurulur: motor kapaliyken HEAD degisimi gorulur (tetik bir Sync ister -> motoru uyandirir)
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1888-1903 — Sessizlik bekcisi yalniz WaitingOnEngine pencerelerinde; bosta kapali motor alarm uretmez
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 1793 — setPerfMode kosu disinda da gonderilir -> kapali motoru bosuna uyandirir
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 254-266 — KillCurrent: Kill(entireProcessTree) + 1 s'ye kadar senkron bekleme - bosta kapatma UI thread'inde kosmamali

**Kanıt:** Kod okumasi. Elverisli olanlar: (a) generation mekanizmasi bilincli kapatmada EngineExited uretmiyor; (b) outer job EngineHost omrunde, yeni Supervisor ayni job'a girer; (c) motor acilisinda cache yuklenmez, cokme kurtarmasi kucuk bir dosya okumasidir; (d) HeadWatcher App'te; (e) bekci yalniz cevap beklenen pencerelerde. Bozulanlar: (1) OnEngineReady her hazir olusta Sync baslatir; Sync komut dongusunu blokladigi icin tepsiden kisayolla Build = once tam Sync (C1'de Supervisor 610 Mcycles/s x 10 s), sonra planlama. (2) Kapali motora gonderim '[error] failed to send ...: Engine is not running.' satiri uretir. (3) Kosu bittikten ve motor kapandiktan sonra karta tiklamak logNotFound doner - bugun calisan 'biten kosunun logunu diskten ac' davranisi kaybolur. (4) About'taki EnginePid/EngineVersion (RunViewModel.cs:2633-2639) bayat kalir. (5) _toolset memo'su her uyanista yeniden cozulur (vswhere child'i).

**Etki:** Bellek: motor bostayken 150-240 MB -> 0 MB (surec yok). Bedel: ilk komutta motor soguk acilisi (Popen -> engineReady suresi OLCULMEDI; framework-dependent, R2R'siz - D8-3) + ilk startRun'da vswhere. Tepsi senaryosu: gelistirici VS'de calisirken motor kapali kalir; kisayolla Build'de once acilis odenir.

**Etki (doğrulayıcı düzeltmesi):** Bellek: motor bostayken olculen 148-336 MB -> 0. Bedel olculmedi (Popen -> engineReady, vswhere). Eldeki tek ilgili sayi: Supervisor sureci pencere tutamacindan ~45 ms sonra gorunuyor (1.334 ms / 1.379 ms, 100 ms adimli yoklama) - engineReady ani degil.

**Çözüm:** Yalniz surec omru degisir. (1) EngineHost'a public SuspendAsync (ShutdownGracefullyAsync'in aynisi ama once ShutdownCommand'a cevap olarak surecin KENDI cikisini <=500 ms bekler, sonra KillCurrent; thread-pool'da) ve EnsureStartedAsync (child yoksa StartAsync; es zamanli cagrilar tek Task'ta birlesir). (2) RunViewModel'de iki gonderim kapisi EnsureStartedAsync'ten gecer; uyandirma yolu OnEngineReady'yi CAGIRMAZ (yalniz EngineVersion/EnginePid guncellenir; 'Engine ready' satiri ve Sync yok). Cokme kurtarma sayisi zaten EventReceived'dan OnEngineRecovered'a gider (satir 1963) - degismez. (3) Bosta kapatma zamanlayicisi App'te (VM saat enjekte): son motor olayi/komutundan N dakika sonra, yalniz !IsRunInFlight && !IsWorkspaceBusy && bekleyen kosu istegi yokken. (4) Proje logu: GetProjectLogCommand'a opsiyonel RunDirectory (App'in _runLogDirectory'si) - motor _lastRunDirectory null ise onu kullanir (RunLogWriter.ReadProjectLogFromDisk zaten statik, RunLogWriter.cs:69). (5) setPerfMode yalniz kosu ucustayken gonderilsin (kosu disinda yeni kosu komutu PerfMode'u zaten tasir). (6) About: motor kapaliyken 'engine: idle (starts on demand)'. Oncelik: B'den SONRA; B'nin olculen tabani hedefin (or. <=40 MB) altindaysa D'ye gerek kalmaz.

**Çözüm (doğrulayıcı düzeltmesi):** Madde (5) dusurulur (zaten yapilmis). D, B olculmeden acilmamali ve acilirsa ARCHITECTURE §4.6 motorun omrunu 'App yasadikca yasar' diye anlattigi icin kullaniciya sorulmali (dokuman degisir). Uyandirma yolunda Sync atlanirsa ekranin kararlari bayat kalabilir: motor kapaliyken HeadWatcher bir Sync isterse motor yine uyanir - 'tepsideyken VS'den her commit motoru uyandirir' sonucu acik soru olarak kalir.

**Değişmezler:** Shell-out MSBuild, nested job (outer App'te, inner Supervisor'da), planlama Core'da, stdout NDJSON ayni. §4.6 degisir: 'The App watches the Supervisor process' bolumune bilincli bosta kapatma + talep uzerine baslatma eklenmeli; §12.1 'Engine ready' satirinin yalniz ilk acilis ve Restart'ta yazildigi belirtilmeli. Guncelleme motoru icin arti: motor kapaliyken supervisor/*.dll kilidi yok (EngineHost.cs:227-232 gerekcesi).

**Test fikri:** Ayri testler: (a) EngineHostTests: SuspendAsync sonrasi EngineExited ateslenmez ve EnginePid null; (b) Suspend sonrasi SendAsync yolu motoru baslatir ve komut ulasir (ping/pong); (c) RunViewModel: uyandirma hic SyncWorkspaceCommand gondermez (DebugOnCommandSent ile; bugun OnEngineReady gonderir - kirmizi); (d) kosu bitti + motor yeniden basladi -> getProjectLog log'u doner (bugun logNotFound - kirmizi); (e) bosta zamanlayici kosu/Sync ucustayken kapatmaz.

**Doğrulayıcı notu:** Dort 'bozulan' iddiadan biri YANLIS: setPerfMode kosu disinda GONDERILMIYOR - RunViewModel.cs:1791 'if (!IsMidRunLocked) return;' gonderimden once doner; yani oneri (5) zaten yapilmis. Digerleri kodda dogru: OnEngineReady (2656-2668) her hazir olusta kosulsuz Appended Sync baslatir; Sync komut dongusunu bloklar (SupervisorHost.cs:147-150, 160-176); TrySendAsync (1826-1831) kapali motorda '[error] failed to send' yazar; _lastRunDirectory yalniz motor belleginde (RunCoordinator.cs:184, 218, 727) ve yeniden baslayan motor logNotFound doner. Secenek D'nin bedeli kullanicinin BIRINCIL senaryosuna dogrudan vurur: tepsiden kisayolla Build zaten 5,68 s gecikiyor (olculdu); uzerine motorun soguk acilisi + ilk kosuda vswhere eklenir (ikisi de OLCULMEDI). Bu yuzden D bir iyilestirme degil, kullanici karari gerektiren bir takas.

#### N5-4 — Secenek C tek basina bostaki bellegi dusurmez; Concurrent=false ise workstation GC'de gen0 butcesini buyutebilir - olcmeden acilmamali

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: tahmin · zayıf makine önemi: orta |
| İş / risk | kisa / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) src/BuildOrchestrator.Supervisor/BuildOrchestrator.Supervisor.csproj (GC ozelligi yok); bin/Release/net10.0-windows/BuildOrchestrator.Supervisor.runtimeconfig.json
- `src/BuildOrchestrator.Supervisor/BuildOrchestrator.Supervisor.csproj` satır 12-15 — PropertyGroup: yalniz OutputType + TargetFramework; GC ayari yok
- `src/BuildOrchestrator.Supervisor/bin/Release/net10.0-windows/BuildOrchestrator.Supervisor.runtimeconfig.json` satır 1-13 — Uretilen runtimeconfig: System.GC.* anahtari yok -> workstation + concurrent varsayilan

**Kanıt:** Koddan: csproj ve uretilen runtimeconfig.json'da GC anahtari yok. Runtime davranisi (koddan degil, .NET GC bilgisi - TAHMIN): System.GC.ConserveMemory yalniz tam bloklayan GC'lerde LOH/gen2 sikistirma esigini degistirir, bosta surecte GC TETIKLEMEZ; yani N5-1'deki 'bosta toplanmayan cop'u tek basina cozmez. System.GC.Concurrent=false workstation'da arka plan GC thread'ini kaldirir ama gen0 ust butcesi concurrent modda 6 MB ile sinirliyken non-concurrent'ta segment/region boyutuna bagli daha buyuk bir degere cikabilir (gc.cpp init_static_data) - sonuc daha seyrek GC ve daha buyuk tepe olabilir. D2-4 bu ikisini dogrudan oneriyordu.

**Etki:** C'nin beklenen kalici kazanci belirsiz (0 ile birkac on MB arasi); yanlis yonde etki riski var. B ile birlikte ConserveMemory, B'nin sikistirmasini pekistirir.

**Etki (doğrulayıcı düzeltmesi):** ConserveMemory'den bosta taban icin beklenen kazanc: olcume gore ~0 (canli heap 2,0 MB). Concurrent=false'un etkisi bilinmiyor (olculmeli).

**Çözüm:** Sira: once B'yi uygula ve olc. C'yi ancak olcumle ac. Eklenme bicimi (Supervisor.csproj): <ItemGroup><RuntimeHostConfigurationOption Include="System.GC.ConserveMemory" Value="7" /></ItemGroup> (runtimeconfig.json configProperties'e yazilir); Concurrent icin <PropertyGroup><ConcurrentGarbageCollection>false</ConcurrentGarbageCollection></PropertyGroup>. Iki ayar AYRI AYRI denenmeli (dort kombinasyon: varsayilan / Conserve / NonConcurrent / ikisi) ve Sync x3 + Rebuild sonrasi Private ile GC sayisi karsilastirilmali. App'e dokunulmaz (UI duraklamasi).

**Çözüm (doğrulayıcı düzeltmesi):** C siralamadan cikarilabilir ya da en sona alinir; dort kombinasyonluk deney yalniz B olculdukten ve taban hedefi tutmadiysa anlamli.

**Değişmezler:** Yalniz Supervisor'in runtimeconfig'i; Velopack/App etkilenmez. PublishLayoutTests supervisor klasorunun icerigini pinliyorsa runtimeconfig degisimi beklenen degisikliktir.

**Test fikri:** Kaynak guard'i: Supervisor runtimeconfig.json'da secilen anahtarin bulundugu (publish cikti testi). Davranis testi yok - karar olcumle verilir.

**Doğrulayıcı notu:** Olgu dogru: Supervisor.csproj'da ve uretilen runtimeconfig.json'da System.GC.* anahtari yok (dosya bu oturumda okundu: yalniz MetadataUpdater / BinaryFormatter / CSWINRT ozellikleri). Bulgunun asil iddiasi ('C tek basina bostaki bellegi dusurmez') olcumle GUCLENDI: canli heap 2,0 MB iken sikistirma esigini degistiren ConserveMemory'nin sikistiracagi bir sey yok; sorun parcalanma degil, commit'li ama bos bellek. gen0 butcesi / Concurrent=false uzerine yazilanlar runtime ic davranisi hakkinda TAHMIN, dogrulanmadi.

#### N5-5 — App'in kosudaki +187 MB'inin yalniz ~56 MB'i log tamponlari; 'kosu bitince tamponu birak' tek basina buyumenin ucte birini geri verir - kalan ~130 MB'in kaynagi olculmeli

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) RunViewModel.cs:438-440, 2694-2716, 2740-2754; ConsoleView.xaml.cs:104, 300-306, 638-642, 682-686
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2694-2716 — OnProjectLog: her satir _liveLines'a (olay nesnesi) ve _runText'e; _projectText'e yalniz aktif proje icin
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 438-440 — Uc tampon alani
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 300-306 — Kirpilan canli satirlar _backlogLines'a YENI string olarak eklenir (ucuncu kopya)

**Kanıt:** OLCUM: App Private 194,5 MB (A1) -> 381,6 MB (F0), +187 MB; kosu sonrasi 381,4 ve tepside 380,2 MB (G2) - geri verilmiyor. Ayni kosunun diskteki loglari (n5_run_text_size.py, run-20261001-015217-122): 154 proje logu, 22.858 satir, 7.606.757 karakter. TURETILMIS: _runText 15,2 MB (UTF-16); _liveLines metin 15,9 MB + 22.858 x ~400 B (olay nesnesi + her olayda ayri RunId 36 kr ve ProjectId ~117 kr string'i) = ~9,1 MB -> ~25 MB; _backlogLines ~15,9 MB. Toplam ~56 MB = olculen buyumenin ~%30'u. Metin akisi 7,6 MB / 142 s = 54 KB/s; tamponlar ~0,39 MB/s buyur, olculen buyume 1,3 MB/s.

**Etki:** D2-1/D6-1/D10-4'un onerdigi kopya azaltma bu kosuda en cok ~40 MB (liveLines + backlog) kazandirir; _runText (15 MB) butunluk icin kalir. Kalan ~130 MB toplanmamis cop / WPF-AvalonEdit churn'u / finalizer kuyrugu (dotnet-trace: GC.RunFinalizers %5,5) olabilir - hangisi oldugu bilinmiyor.

**Etki (doğrulayıcı düzeltmesi):** Olculen: +275 MB (158 -> 433) iki no-op rebuild sonrasi; zorla GC 114 MB geri veriyor; canli managed artis 58,6 MB. 'Geri verilmiyor' ifadesi mutlak degil: 4 rebuild sonrasi App Private 317 MB, sonraki orneklerde 327-332 MB'ta sabit - kendi GC'leri tepeyi 433'ten indirmis. Ayni trace'te Finalizer thread'i 25 s'nin 23,3 s'inde mesgul (DWrite sarmalayicilari): yerel bellegin bir kismi sonlandirma kuyrugunun arkasinda bekliyor olabilir (olculmedi).

**Çözüm:** Iki adim, ikisi de olcumle: (1) tamponlari N5-6'daki yasam dongusune gore birak; (2) App'te de is bitiminde rahatlama: runCompleted islendikten sonra ve pencere tepsiye inerken (kimse bakmiyor) thread-pool'dan GC.Collect(2, Aggressive, blocking, compacting) + LOH CompactOnce. UI thread'inden cagrilmaz; GC tum thread'leri durdurdugu icin on plandayken YAPILMAZ (pencere gorunurken yalniz kosu bitiminden sonra ContextIdle'da ve yalniz kullanici girdisi yokken). Kalan ~130 MB icin once gcdump al, sonra karar ver.

**Çözüm (doğrulayıcı düzeltmesi):** App'te rahatlama GC'si yalniz kosu bittikten sonra VE gosterge animasyonu oynamiyorken kosmali: gosterge UI thread'inde ve bloklayan GC tum thread'leri durdurur (74 MB canli heap'te duraklama suresi olculmedi). Once tampon birakma (N5-6), sonra olcum; GC cagrisi son care.

**Değişmezler:** §14.5 motion: GC duraklamasi animasyon oynarken tetiklenmez (kosu sonu finali bittikten sonra ya da pencere gizliyken). Konsol butunlugu N5-6'da.

**Test fikri:** RunViewModel testi: N satirlik sahte kosu + runCompleted sonrasi _liveLines toplam eleman sayisi 0 (internal sayac); bugun N - kirmizi.

**Doğrulayıcı notu:** Yeni olcum bulgunun acik sorusunu kismen cevapliyor. App Private acilista 158 MB (canli heap 15,5 MB) -> iki rebuild sonrasi 433 MB; zorla GC sonrasi 319 MB (-114 MB); canli heap 74,1 MB (+58,6 MB). Bulgunun 'tamponlar ~56 MB' tahmini canli heap artisiyla (58,6 MB) ayni mertebede; gcdump'ta tek 13.746.814 B'lik char[] ve ProjectLogEvent[] dizileri kosu bittikten SONRA hala canli (tamponlarin birakilmadiginin olculmus kaniti). Kalan: 114 MB siradan bir GC'nin geri verdigi cop/commit, ~100 MB (319 - 158 - 58) ise zorla GC'den sonra da duran, managed canli olmayan bellek (yerel WPF/DWrite kaynaklari + GC'nin bos commit'i - ayrimi olculmedi). Konumlar dogru (RunViewModel.cs:438-440, 2694-2716; ConsoleView.xaml.cs:300-306).

#### N5-6 — App log tamponlarinin tam yasam dongusu: uc tampon da yalniz BIR SONRAKI islemde bosaliyor; _liveLines'in tek okuyucusu ucustaki projenin dikisi, _projectText secim kalkinca da duruyor

| Alan | Değer |
|---|---|
| Önem | önemli |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) RunViewModel.cs:2740-2754 (tek bosaltma), 2694-2716 (dolum), 2993-3001 (dikis), 2798-2802, 2820-2843; ConsoleView.xaml.cs:104, 300-306, 634-642, 672-686
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2740-2754 — ClearConsoleForNewOperation: uc tamponun TEK bosaltildigi yer
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 965, 1375, 1441, 1488 — Cagiranlar: yeni kosu, bolum acan Sync, Clean, Optimize
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.ActionBar.cs` satır 331 — Besinci cagiran
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2993-3001 — _liveLines'in TEK okuyucusu: getProjectLog dikisi, yalniz LineNumber > ThroughLineNumber olanlar
- `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` satır 2798-2802, 2820-2843 — _runText okuyuculari: Back (SeedRunDocument); _projectText okuyuculari: SeedProjectDocument ve LogTextProvider
- `src/BuildOrchestrator.App/MainWindow.xaml.cs` satır 612-651 — Kart secimi her seferinde LoadProjectLogAsync ile diskten yeniden yukler; secim kalkinca ShowRunConsole
- `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs` satır 104, 634-642, 672-686 — _backlogLines: mod gecisinde bastan kurulur (SplitLines / kopya), canli kirpmayla buyur

**Kanıt:** Kod: DOLAR - _liveLines ve _runText her projectLog olayinda (2698-2703); _projectText yalniz aktif projede (2705-2706) ya da dikiste (3000); _backlogLines anlati modunda takip acikken kirpilan her satirda (300-306). BOSALIR - yalniz ClearConsoleForNewOperation (5 cagiran, hepsi YENI islem); runCompleted/runStopped/Hide'da bosaltma yok. OKUR - _liveLines: yalniz dikis ve yalniz snapshot'tan SONRA gelen satirlar (2997); biten projenin diskteki logu tamdir, dikis ondan hic satir secmez. _runText: Back ve anlati belgesinin yeniden kurulmasi (MainWindow.cs:649). _projectText: secili projenin belgesi ve kopyalama; secim kalkinca (ShowRun) girdi silinmez ama yeniden secimde 3000. satir onu diskten gelenle EZER.

**Etki:** Kosu bitince _liveLines'in tamami (olculen kosuda ~25 MB) ve secimi kalkmis her _projectText girdisi (en buyuk proje logu 1,65 MB -> 3,3 MB UTF-16) hic okunmayacak veri olarak bir sonraki isleme kadar durur; tepsideyken de.

**Etki (doğrulayıcı düzeltmesi):** Kosu sonunda tutulan canli managed veri olculdu: 74,1 MB (acilista 15,5 MB). Bunun ne kadarinin _liveLines oldugu tip bazinda ayrilmadi.

**Çözüm:** Butunlugu bozmayan birakmalar: (1) proje terminal olayinda (succeeded/failed/skipped) _liveLines.Remove(projectId), runCompleted'da _liveLines.Clear() - dikis filtresi zaten bos kume secer (D2-1 ile ayni gerekce). (2) ShowRun'da (secim kalkti) o projenin _projectText ve _projectLineCount girdisini sil - yeniden secim diskten yukler. (3) _backlogLines anlati modunda VM'in _runText'inden turetilsin (D6-1 madde 3). (4) Pencere tepsiye inince ConsoleView beslemesi durdurulup belgesi/backlog'u birakilabilir; gosterimde mevcut SeedRunDocument yolu belgeyi _runText'ten yeniden kurar (yeni mekanizma gerekmez). _runText KALIR: anlati satirlari + harmanlanmis MSBuild satirlari diskte bu sirayla yok (diskte yalniz proje basina log + decision.log var), Back ve yukari kaydirma ona muhtac. 'Diskten oku' yalniz proje loglari icin gecerli ve o yol motorun _lastRunDirectory'sine bagli (N5-3 madde 4).

**Çözüm (doğrulayıcı düzeltmesi):** (1) 'proje terminal olayinda _liveLines.Remove' dogrudan guvenli DEGIL: getProjectLog cevabi (SupervisorHost dogrudan writer'a yazar) ile proje olaylari (koordinatorun kanal pompasi) ayri yollardan ayni writer'a gider; snapshot ucustayken alinmis (ThroughLineNumber = N) bir cevap, sonraki satirlar ve terminal olayindan SONRA islenirse N'den buyuk satirlar _liveLines'tan silinmis olur ve dikis onlari kaybeder. Guvenli bicim: o proje icin bekleyen bir log yuklemesi varken silme; ya da yalniz runCompleted'da ve bekleyen yukleme yokken temizle. Bu yaris ayri bir testle pinlenmeli (snapshot -> ek satirlar -> terminal -> gec cevap). (4) 'tepside ConsoleView beslemesini durdur' kullanicinin 'tepside UI isi olmasin' oncelligiyle ortusur; pencere gelince belge _runText'ten yeniden kurulur (SeedRunDocument) - tutarlilik korunur.

**Değişmezler:** §13.5 konsol: 200 satir pencere + gecmise kaydirma, Back'te tam anlati, 'N lines' sayaci (_runLineCount/_projectLineCount int'leri kalir). T28 dikisi: ucustaki proje icin _liveLines aynen calisir.

**Test fikri:** Ayri testler: (a) projectSucceeded sonrasi o projenin _liveLines girdisi yok ve ardindan getProjectLog dikisi tam metni verir (kayip/tekrar yok); (b) secim kalkinca _projectText girdisi yok, yeniden secim diskten gelen metni gosterir; (c) Back sonrasi anlati belgesi kosunun tum satirlarini icerir.

**Doğrulayıcı notu:** Kod iddialari tek tek dogru: uc tampon yalniz ClearConsoleForNewOperation'da bosaliyor (RunViewModel.cs:2740-2754); _liveLines'in tek okuyucusu dikis (2996-2998, LineNumber > ThroughLineNumber); _runText okuyuculari 2798, 2825; _projectText 2801, 2839, 3000. gcdump kosu sonrasi ProjectLogEvent[] dizilerini canli gosteriyor - olculmus destek. Ancak cozumun (1) maddesinde bir yaris riski var (asagida).

#### N5-7 — Kosu sirasinda Supervisor'da build-state defteri LOH'a duser: 148.870 baytlik dosya her sonucta 2 x ~298 KB string (olculen kosuda >=92 MB LOH tahsisi)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: koddan-kanitli · zayıf makine önemi: dusuk |
| İş / risk | orta / orta |

**Konum:**
- (doğrulayıcı düzeltmesi) BuildStateStore.cs:40-61 (Load), 199-209 (Write)
- `src/BuildOrchestrator.Core/State/BuildStateStore.cs` satır 40-48, 199-209 — Load: ReadAllTextSharingDelete -> string; Write: Load + Serialize -> string -> WriteAllText
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 2159-2183, 2253-2254 — Basarida Upsert; kanitli hatada ek bir Load + Upsert
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 771, 879, 1933 — Kosu icindeki diger Load cagirilari

**Kanıt:** Dosya 148.870 B / 196 giris (olculdu) -> UTF-16 string ~297,7 KB; LOH esigi 85.000 B. Olculen Rebuild'de 152 basari + 2 hata = 154 yazim x (Load string'i + Serialize string'i) = >=91,7 MB LOH tahsisi. LOH tahsisi gen2 butcesini tuketir. OLCUM: Supervisor Private kosu boyunca 137,2-144,9 MB bandinda kaldi (D2-4..D2-20) ve kosu basindaki 241,7 MB'tan indi; CPU %1-5. (Inisin bu gen2'lerden kaynaklandigi cikarimdir.)

**Etki:** Bellek tepe degerine etkisi olculen kosuda yok (bant sabit); CPU etkisi kucuk. Asil deger: defter buyudukce (giris basi ~760 B) maliyet proje sayisiyla karesel artar.

**Etki (doğrulayıcı düzeltmesi):** Yazim basina 2 x ~311 KB LOH string'i (kodda kesin); toplam LOH tahsisi ve GC sayisina etkisi olculmedi.

**Çözüm:** D2-6/D9-5'teki 'kosu basina tek bellek kopyasi + toplu yazim' cozumu; ek olarak N5-2'deki akis tabanli oku/yaz yardimcisi BuildStateStore'da da kullanilirsa LOH string'leri tamamen kalkar. Ayri bir is acmaya gerek yok.

**Değişmezler:** Atomik temp+rename, never-throw, _writeGate sirasi ayni; ucus defteri (run-inflight.json) cokme kurtarmasini saglamaya devam eder.

**Test fikri:** BuildStateStoreTests: N Upsert sonrasi dosya okuma sayisi (enjekte sayac) 1 - bugun N.

**Doğrulayıcı notu:** BuildStateStore.Write (199-209) her degisiklikte Load() (ReadAllTextSharingDelete -> string -> Deserialize -> GroupBy/ToDictionary) + yeni Dictionary kopyasi + Serialize(string) yapiyor; dosya bugun 155.702 B -> UTF-16 string ~311 KB, LOH esiginin (85.000 B) ustunde. Kosu basina kac Upsert oldugu bu oturumda sayilmadi (bulgunun 154'u bir onceki olcumden). Etki kucuk: Supervisor kosuda tek cekirdegin %8-14'unu kullaniyor (olculdu: 212-466 Mcycles/s) ve bunun ne kadarinin defter yazimi oldugu ayrilmadi. Kozmetik siniflama dogru.

#### N5-8 — Kosu sirasinda Supervisor bellegi buyumuyor: sinirsiz event kanali bu yukte birikmiyor (D10-11 icin olcum)

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: dusuk |
| İş / risk | kisa / dusuk |

**Konum:**
- (doğrulayıcı düzeltmesi) RunCoordinator.cs:586-587
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 586-587 — Channel.CreateUnbounded<IpcEvent> + pump

**Kanıt:** OLCUM: 142 s'lik gercek Rebuild'de (23.047 satir, ~160 satir/s) Supervisor Private 137,2-144,9 MB, WS 150-155 MB bandinda; App ayni surede tek cekirdegin %60-100'unu kullanirken bile artis yok. Kanal birikseydi Private kosu boyunca artardi.

**Etki:** Bu yukte risk yok. Risk yalniz App okumayi tamamen biraktiginda (UI thread kilitlenmesi) ve satir hizi cok yukseldiginde; ust sinir kosunun toplam logu.

**Etki (doğrulayıcı düzeltmesi):** Kosu sonrasi canli heap 2,0 MB (olculdu) -> kalici birikim yok. Kosu ici Private 155-283 MB araliginda oynuyor; kaynak kanal degil, GC commit'i.

**Çözüm:** Degisiklik gerekmez. Istege bagli gozlem: kosu sonunda pump'in gordugu en yuksek kanal derinligini decision.log'a tek satir yaz (D10-11 onerisi) - boylece zayif makinede de dogrulanir.

**Değişmezler:** Kanalin sinirsiz kalmasi T28 dikisi ve konsol butunlugu icin bilincli; BoundedChannel/DropOldest onerilmez.

**Test fikri:** RunCoordinator testi: yavas okuyucu ile kosuda tepe derinlik satirinin yazildigi.

**Doğrulayıcı notu:** Sonuc ('kanal birikmiyor, degisiklik gerekmez') yeni olcumle destekleniyor: iki rebuild'den sonra Supervisor canli heap 2,0 MB - biriken IpcEvent olsaydi burada gorunurdu. Ancak dayanak cumlesi ('Private kosu boyunca 137-145 MB bandinda') yeni olcumlerde TEKRARLANMADI: kosu icinde 155, 246, 262, 283 MB; 4 rebuild sonrasi 336 MB. Private kosuda dalgalaniyor, sabit bir bant yok.

#### N5-9 — Motor bellegi kararlarinin dayanacagi uc sayi yok: taze motorun bosta Private degeri, zorlanmis GC sonrasi taban, Popen -> engineReady suresi

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | confirmed · kanıt: olculdu · zayıf makine önemi: orta |
| İş / risk | kisa / dusuk |

**Konum:**
- `.claude/temp/perf-2026-10-01/measure.ps1` satır 83 — Acilis olcumu MainWindowHandle yoklamasi (100 ms adim); Supervisor icin yalniz 'surec gorundu' ani (1.379 ms), engineReady ani yok
- `src/BuildOrchestrator.App/Services/EngineHost.cs` satır 105-153 — StartAsync: baslangic ve _ready arasinda sure olculmuyor/loglanmiyor
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 84-92 — engineReady yazilirken bellek/GC tanisi yok

**Kanıt:** Eldeki olcumde Supervisor'in ilk ornegi acilis Sync'inden SONRA alindi (A1 150,8 MB); Sync oncesi deger yok. B'nin ust sinir kazanci ve D'nin uyandirma maliyeti bu yuzden sayiyla soylenemiyor. Bu oturumda izole --logs sandbox'iyla Release Supervisor'i baslatan bir sonda denendi; izin sistemi reddetti, olcum alinamadi.

**Etki:** B/C/D arasindaki secim (ozellikle 'B yeterli mi, D gerekli mi') olcum olmadan yapilirsa ya gereksiz uzun is (D) acilir ya da hedef tutmaz.

**Etki (doğrulayıcı düzeltmesi):** Eksik kalan iki sayi B'nin ust sinirini ve D'nin bedelini belirliyor; ikisi de olculmeden D acilmamali.

**Çözüm:** Kapili olcum testi (CLAUDE.md kurali: [SkippableFact] + Skip.IfNot(BO_MEASURE_ENGINE_MEMORY == "1")): SupervisorSandbox ile gercek Supervisor'i baslat; yaz: (1) Popen -> engineReady ms (5 tekrar), (2) hazir + 1,5 s Private/WS, (3) OSYS koku varsa fetch=false 3 Sync'in her birinden sonra Private/WS ve Sync basina CPU ms, (4) 20 s ve 40 s bosta Private. Rahatlama adimi (N5-1) eklendikten sonra ayni test onceki/sonraki farki verir. Ek olarak Supervisor rahatlama adiminda stderr'e GC.GetGCMemoryInfo ozeti (stdout NDJSON kalir).

**Çözüm (doğrulayıcı düzeltmesi):** Olcum testine ucuncu bir sutun eklenmeli: Sync sonrasi (a) hicbir sey yapmadan, (b) GC.Collect(2, Forced, blocking, compacting), (c) GC.Collect(2, Aggressive, blocking, compacting) ile Private ve GC.GetGCMemoryInfo().TotalCommittedBytes - 'hangisi commit'i geri veriyor' sorusunun tek kesin cevabi bu.

**Değişmezler:** Olcum testi normal suitte kosmaz (ortam degiskeni kapisi); gercek %LOCALAPPDATA%'a dokunmaz (SupervisorIsolationGuardTests kurali).

**Test fikri:** Testin kendisi.

**Doğrulayıcı notu:** Uc eksik sayidan biri artik var: zorla GC sonrasi Supervisor tabani 112 MB (acilis) ve 148 MB (iki rebuild sonrasi) olculdu - ama siradan GC ile, Aggressive ile degil. Digerleri hala yok: taze motorun ilk Sync ONCESI Private'i (tum ornekler acilis Sync'inden sonra: 148-174 MB) ve Popen -> engineReady suresi. Kaynakta BO_MEASURE_ENGINE* / BO_TRACE_STARTUP kapisi yok (grep bos).

#### N5-10 — Onerilen sira (etki MB x risk x is): B -> A' -> App tampon birakma + App rahatlama -> (olcume gore) C -> (B tabani hedefi tutmazsa) D

| Alan | Değer |
|---|---|
| Önem | kozmetik |
| Doğrulama | downgraded · kanıt: koddan-kanitli · zayıf makine önemi: yuksek |
| İş / risk | orta / dusuk |

**Konum:**
- `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` satır 584-649 — B'nin kosu sonu cagri noktasi (finally, await pump sonrasi)
- `src/BuildOrchestrator.Supervisor/SupervisorHost.cs` satır 107-140 — B'nin komut sonu cagri noktasi (DispatchAsync)

**Kanıt:** N5-1..N5-9'un ozeti. Sayilar: Supervisor bosta 150,8-241,7 MB (olculdu); kosu ici taban 137,2 MB (olculdu); Sync basina cop >=72 MB (turetildi, olcumle uyumlu); tek ornek canli bedeli ~23,8 MB (turetildi); App tamponlari ~56 MB / +187 MB (turetildi/olculdu).

**Etki:** 1) B - Supervisor rahatlama: etki >=70-105 MB (alt sinir olculdu), ust sinir olculmeli; risk dusuk; is kisa. 2) A' - string'siz oku/yaz + kirli bayragi: Sync basina tepe -48,6 MB LOH, disk yazimi 12 MB -> 0; kalici +0; risk dusuk; is orta. 3) App tampon birakma (N5-6) + App rahatlama (N5-5): ~40 MB kesin + olculmesi gereken ~130 MB'in bir kismi; risk orta; is orta. 4) C - ConserveMemory / Concurrent: etki belirsiz, yanlis yon riski; yalniz olcumle; is kisa. 5) D - talep uzerine motor: bosta 150-240 MB -> 0 (B sonrasi kalan taban kadar ek kazanc); risk orta; is uzun; tepsiden Build'e soguk acilis ekler. A (tek ornek) yalniz D ile birlikte.

**Etki (doğrulayıcı düzeltmesi):** Olculmus siralama girdileri: Supervisor bosta 148-336 MB, canli 2,0 MB, siradan zorla GC sonrasi 112-148 MB; App 158 -> 433 MB, zorla GC sonrasi 319 MB, canli 74 MB.

**Çözüm:** Once N5-9 olcumunu ekle, B'yi uygula, tabani oku. Taban <=~40 MB ise D'yi acma. EmptyWorkingSet hicbir adimda onerilmez.

**Çözüm (doğrulayıcı düzeltmesi):** Sira: (0) N5-9 olcumu (Forced / Aggressive / hicbiri karsilastirmasi) -> (1) A' kirli bayragi + akisla oku/yaz (hem disk yazimini hem Sync basina copu keser; risk dusuk; GC sonucundan bagimsiz kazanc) -> (2) olcum Aggressive'in commit'i geri verdigini gosterirse B -> (3) App tampon birakma -> D yalniz kullanici karariyla.

**Değişmezler:** Tum adimlar surec/bellek omrune dokunur; shell-out, nested job, OutDir, git, stdout NDJSON, planlama Core'da, Velopack yalniz App degismez.

**Test fikri:** Adim basina kendi testleri (ilgili bulgularda).

**Doğrulayıcı notu:** Siralama makul ama dayandigi iki sayi duzeltilmeli: (1) 'B: etki >=70-105 MB (alt sinir olculdu)' yalniz tepe degerlerden ~150 MB'a inis icin gecerli; olcum, siradan zorla GC'nin 150 MB'tan yalniz 6-38 MB aldigini gosterdi - B'nin 150 MB altina inip inmeyecegi Aggressive denenmeden bilinemez. (2) 'App tampon birakma ~40 MB kesin' -> olculen canli artis 58,6 MB, tip bazinda dagilimi ayrilmadi. C icin 'belirsiz' yerine 'canli heap 2 MB oldugu icin bosta tabana etkisi beklenmez' denmeli. D, birincil senaryoya (tepsiden kisayolla Build, bugun 5,68 s) gecikme ekledigi icin son sirada kalmali.

**Doğrulayıcının eklediği noktalar:**
- src/BuildOrchestrator.Core/Incremental/SourceHashCache.cs:131-142 - Prefill'in stat gecisi ve okuma dongusu sabit 16 paralellikle kosuyor (WithDegreeOfParallelism(16) / MaxDegreeOfParallelism = 16), cekirdek sayisina bakmiyor; her Sync ve her Build'de 24,5 bin dosya icin calisir, 2-4 cekirdekli makinede is parcacigi sayisi ve Supervisor'in kosu oncesi CPU tepesi buna bagli (olculmedi).
- src/BuildOrchestrator.Core/ProcessControl/JobProcessLauncher.cs:40 - Supervisor omru boyunca bir conhost.exe tasiyor (olculdu: bosta 'children: conhost.exe x1 | total WS 7 MB'); motor bellegi boyutunda bulgu olarak gecmiyor.
- src/BuildOrchestrator.Supervisor/Program.cs:201 ve src/BuildOrchestrator.Core/Planning/BuildPlanBuilder.cs:43 - Build yolunda da iki kosulsuz Flush var (toplam ~12 MB yazim) ve bu yazim runStarted'dan ONCE, planlama thread'inde seri kosuyor; bulgu bunu bellek acisindan ele almis, tepsiden Build gecikmesine (olculen 5,68 s) katkisini ayirmamis.
- Olcumle celisen varsayim: bulgu 'bosta GC calismadigi icin' diyor, oysa zorla GC sonrasi da 112-148 MB kaliyor (measure2.log 02:23:10 ve 02:26:26) - kalan bellegin GC commit'i mi yerel bellek mi oldugunu ayiran bir tani (GC.GetGCMemoryInfo().TotalCommittedBytes ile Private karsilastirmasi) hicbir bulguda onerilen ilk adim degil.
- src/BuildOrchestrator.App/ViewModels/RunViewModel.cs:2715-2716 - pencere tepsideyken de her log satiri _console.Post ile konsol pompasina gidiyor (gorunurluk kapisi yok); App'in kosudaki bellek/CPU buyumesinin bir kaynagi ve N5-6 madde (4) bunu yalniz 'birakilabilir' diye geciyor.

**Temiz bulunan alanlar:**
- Supervisor acilisi cache YUKLEMEZ: Program.cs:24-64'te yalniz klasor, InFlightLedger.Recover, job ve host kurulur; MSBuild cozumu lazy (Program.cs:219-230).
- Hicbir cache ornegi surec boyu tutulmuyor: SupervisorHost.cs:44-57 fabrikalari cagri basina yeni ornek; Program.cs:94-97 yerel degisken. Sizinti (sureklilik gosteren tutulan referans) yok.
- RunCoordinator kosu bitince kosu durumunu birakir: _scheduler/_wake null (RunCoordinator.cs:632-633), _logs null + Dispose (1056-1057); geriye yalniz _lastRunDirectory (string) kalir.
- Supervisor kosu sirasinda buyumuyor: Private 137-145 MB bandi, 142 s boyunca (olculdu) - event kanali ve log yazicilari birikmiyor.
- Supervisor bosta CPU ~0 (0,0-1,1 Mcycles/s olculdu): bostayken timer/poll yok; komut dongusu stdin okumasinda bloklu (SupervisorHost.cs:93-96).
- Bilincli motor kapatma 'engine died' sinyali uretmez: EngineHost.ShutdownGracefullyAsync generation'i once artirir (EngineHost.cs:205), exit watcher generation kontrolunden doner (139).
- Outer job EngineHost omrunde tek ornek (EngineHost.cs:59) ve her baslatma ona atanir (120): motor yeniden baslatilsa da nested job degismezi korunur.
- HeadWatcher App surecinde (AutoSyncCoordinator.cs:91, 157-163): motorun omrunden bagimsiz.
- Sessizlik bekcisi yalniz cevap beklenen pencerelerde kurulu (RunViewModel.cs:1888-1903): bosta/kapali motor yanlis alarm uretmez.
- App oldurulunce Supervisor 81 ms'de gidiyor, 3 s sonra artik surec yok (olculdu) - kapanista bellek/surec artigi yok.
- Proje logu icin 'diskten oku' yolu zaten var: RunCoordinator.TryGetProjectLogSnapshot kosu bittikten sonra RunLogWriter.ReadProjectLogFromDisk kullanir (RunCoordinator.cs:211-225).

**Açık sorular:**
- Taze Supervisor'in ilk Sync ONCESI bosta Private degeri kac MB? (B'nin ulasabilecegi taban; bu oturumda sonda calistirma izni reddedildi, olculemedi.)
- Popen -> engineReady suresi bu makinede ve zayif bir makinede kac ms? (D'nin tepsiden Build'e ekleyecegi gecikme.)
- Hedef nedir: bostaki motor icin kabul edilebilir Private (or. <=40 MB) - B'nin olculen tabani bunu tutarsa D acilmayacak.
- App'in kosuda buyuyen ~130 MB'i (tamponlarin aciklamadigi kisim) canli nesne mi, toplanmamis cop mu? gcdump gerekli.
- Kosu anlatisi (_runText) kosu bittikten sonra tam mi kalmali, yoksa 'son N bin satir + gerisi proje loglarindan' kabul mu? (D2-ram acik sorusu; N5-6 tam kalacagini varsayiyor.)
- D secilirse bosta kapatma suresi kac dakika olmali ve tepsideyken VS'den gelen her commit'in (HeadWatcher -> sessiz Sync) motoru uyandirmasi kabul mu?
- Dokuman: ARCHITECTURE §4.6 motorun bilincli kapatilmasini tanimlamiyor; D uygulanirsa bolum yeniden yazilmali.

