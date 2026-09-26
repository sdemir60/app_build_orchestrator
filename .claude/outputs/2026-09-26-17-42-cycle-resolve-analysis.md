# Resolve Cycles — Derin Performans Analizi ve Öneriler

> Amaç: "Resolve cycles" (action bar'daki bakım kutusu, sol alt) neden uzun sürüyor, sağlıklı/hızlı/pratik
> nasıl olmalı. Bu bir **analiz** dokümanıdır, geliştirme yapılmamıştır. Kod referansları `main`
> (df18450) üzerindendir; ölçümler gerçek OSYS çalışma kopyasından (2026-09-26) alınmıştır.

---

## 1. Bugünkü davranış (kodla doğrulanmış)

Akış, ARCHITECTURE §6.5, §7.3, §8.1, §8.8 ve şu dosyalarda:

| Adım | Ne oluyor | Yer |
|---|---|---|
| Tespit | Tarjan SCC + Kahn (condensation) — hızlı, sorun değil | `src/BuildOrchestrator.Core/Graph/TopoSort.cs` |
| Kapsam | SCC üyeleri **+ transitif upstream**; kalan her şey pre-skip | `src/BuildOrchestrator.Core/Planning/CycleRunScope.cs` |
| Dispatch | SCC = **tek iş kalemi**, tek worker'a verilir; ayrı SCC'ler paralel koşabilir | `src/BuildOrchestrator.Core/Scheduling/ReadySetScheduler.cs:166` |
| Round döngüsü | Her round'da TÜM üyeler build-order'da **sırayla** invoke edilir (grup içi paralellik yok) | `src/BuildOrchestrator.Supervisor/RunCoordinator.cs:1373` (`BuildCycleGroupAsync`) |
| Durma kuralı | İki ardışık yeşil tur = `Converged` · aynı fail set iki kez = `NoProgress` · tavan 3 = `CapReached` | `src/BuildOrchestrator.Core/Planning/CycleRoundPolicy.cs:39` |
| Invoke | Üye başına round başına yeni `MSBuild.exe`; packages.config'li üyeye **her round** ayrıca restore child'ı | `src/BuildOrchestrator.Supervisor/RunCoordinator.cs:1733` (`NeedsRestore`), `Core/MsBuild/MsBuildArguments.cs:50` |
| Sonuç | Yalnız `Converged` persist eder; diğer her sonda (no-progress/cap/stop/hata) **yeşiller dahil herkes invalidate** | `RunCoordinator.cs:1391-1411`, ARCHITECTURE §8.8 |
| Hafıza | No-progress imzası kaydedilir ama **yalnız rapor eder**, sonraki basışta tam deneme yine yapılır | `RunCoordinator.cs:1559` (`UpdateCycleNonConvergenceMemory`) |

Maliyeti belirleyen sabitler:

- Argüman sözleşmesi (`Core/MsBuild/MsBuildArguments.cs:22`): `UseSharedCompilation=false` +
  `nodeReuse:false`. ARCHITECTURE §9.2'de ölçülmüş: bu bayraklar kapalıyken build ~**2.9×** — nedeni
  torn-DLL penceresini job dışına taşımamak (bilinçli karar).
- Perf profili (§11.1): varsayılan Balanced = **4 worker, BelowNormal, %70 hard CPU cap**; Light = %40/Idle.
  Round zinciri de bu cap altında koşar.
- `PerProjectTimeout` = invoke başına 10 dk (`Core/MsBuild/MsBuildInvoker.cs:44`) — restore+build toplamı.
- ETA cycle terimi paralelliğe bölünmez ve baseline round (2) ile çarpılır (`Core/Incremental/EtaCalculator.cs`).

**Önemli:** İki ardışık yeşil tur kuralı doğruluk açısından sağlamdır — tur 1'de üye, kardeşinin ESKİ
nesil DLL'ine bağlanmış olabilir; tur 2 herkesi nihai API'lere karşı yeniden derler. Sorun kuralın kendisi
değil, **koşulsuz ve seri** uygulanmasıdır (aşağıda).

## 2. Gerçek topoloji: OSYS ölçümü

Aracın kenar türetmesi taklit edilerek (ProjectReference + HintPath→producer) çıkarıldı:

- **177 proje, 1238 kenar, 7 SCC, toplam 33 cycle üyesi.**
- Cycle kapsamı (üye + transitif upstream) = **79 proje** → 46 proje "preparing dependencies" fazının adayı.
- packages.config taşıyan üye: **7/33** (her round'da birer restore child'ı daha).

| SCC | Üye | İç kenar | Kritik yol* | Not |
|---|---|---|---|---|
| UI katmanı (`OSYS.UI.*`, `OSYS.Accounting.Common.UI`…) | **17** | 77 | **6** | Baskın maliyet |
| `OSYS.Types.Customer/General.Common/Rent/UsedCars` | 4 | 6 | 3 | |
| `OSYS.Types.Service.*/SparePart.*` | 4 | 9 | 3 | |
| `OSYS.Types.Accounting.Common ↔ OSYS.Types.General` | 2 | 2 | 2 | Tek kenar kesilse çözülür |
| `OSYS.Business.Accounting.Common ↔ OSYS.Business.SparePart.Finance` | 2 | 2 | 2 | " |
| `OSYS.Business.Finance ↔ OSYS.Business.NewSales` | 2 | 2 | 2 | " |
| `OSYS.Business.NewSales.Pricing ↔ OSYS.Business.NewSales.Sales` | 2 | 2 | 2 | " |

\* Kritik yol: geri kenarlar atılıp ileri kenarlardan seviyeleme yapılırsa round'un kaç "adımda" bitebileceği
(bkz. Ö3). 17 üyeli grupta bugün 17 ardışık adım koşuluyor, yapısal alt sınır ~6.

## 3. Maliyet modeli — neden bu kadar uzun?

Bir Resolve basışının süresi kabaca:

```
T ≈ T_planlama + T_upstream(kirli olanlar, 4-way paralel)
  + max_gruplar( Σ_round Σ_üye [ MSBuild spawn+eval + (restore child) + derleme×2.9 ] )   ← grup içi SERİ
```

Converged olan tipik senaryoda round=2 → OSYS'te **66 üye-invoke**; en büyük grup tek worker'da
**34 ardışık invoke**. Ortalama invoke 15–25 sn alsa (spawn/eval 2–5 sn + derleyici-sunucusuz derleme;
packages.config'li üyede +restore) yalnız o grup **~8–14 dk** eder — %70 cap ve BelowNormal altında, öteki
gruplar ve upstream cabası. "Baya uzun sürüyor" gözlemi tasarımdan türetilebilir durumda; ek olarak:

- **Başarısız grupta da en az 2 tam round ödenir** (NoProgress ancak "aynı küme iki kez" görülünce, yani
  round 2 sonunda ilan edilebilir).
- **Converge etmeyen grupta yeşiller dahil her şey invalidate edildiği** için bir sonraki basış aynı faturayı
  baştan öder; non-convergence hafızası da yalnız log satırı yazar.
- Kullanıcı gözünde "cycle çözme"nin faturasına, buton hiç söylemeden **upstream'in kirli kısmı** da eklenir
  (bu doğruluk için şart — §CycleRunScope gerekçesi — ama görünmez olduğu için "cycle işi yavaş" algısını
  büyütür).

Kök nedenler, etki sırasıyla:

| # | Kök neden | Nerede |
|---|---|---|
| R1 | Grup içi tam **seri** yürütme (17 üye = 17 ardışık adım; yapısal alt sınır 6) | `RunCoordinator.cs:1436` |
| R2 | **Koşulsuz 2. round** — API yüzeyi hiç değişmemişse bile herkes ikinci kez derlenir | `CycleRoundPolicy.cs:34` |
| R3 | Derleyici sunucusuz derleme (**2.9×**, belgeli) — round'lar aynı projeleri dakikalar içinde yeniden derlediği için cycle'da ceza katlanır | `MsBuildArguments.cs:26` |
| R4 | packages.config'li üyeye **her round** restore child'ı (kaynak round'lar arasında değişemezken) | `RunCoordinator.cs:1733` |
| R5 | Umutsuz grupta erken kesme yok; tekrar basışta sıfırdan tam deneme | `CycleRoundPolicy.cs`, §8.8 hafıza |
| R6 | Fatura görünmez: buton "N cycles · M projects" der, upstream'i ve round çarpanını söylemez | App tooltip / preview |

## 4. Yöntem kataloğu — bu iş dünyada nasıl yapılıyor?

**Kurumsal norm: cycle bir hata durumudur.** Bazel/Buck/Nx doğrudan reddeder; MSBuild'in kendi graf modu
(`/graphBuild`) cycle'da fail olur; Visual Studio çözümü ProjectReference cycle'ını yüklemez. Yani "cycle'ı
hızlı derleyen hazır kurumsal motor" diye bir şey yok — herkesin kurumsal cevabı "kır". Cycle'la **yaşamak**
zorunda olan legacy dünyada fiili yöntem tam da bu aracın yaptığıdır: file-reference + ortak çıktı klasörü +
**sabit noktaya kadar yeniden derleme** (fixpoint). Yani mevcut tasarım doğru ailede; fark, endüstrinin
fixpoint'i nasıl ucuzlattığında:

1. **Referans-assembly / API-yüzeyi kısa devresi.** MSBuild+Roslyn'in kendi mekanizması
   (`ProduceReferenceAssembly`/`CopyRefAssembly`): public yüzey değişmediyse dependent'lar yeniden
   derlenmez. Fixpoint teorisinde karşılığı: kaynak turlar arasında değişmediği için, **başarıyla derlenen
   bir üyenin API yüzeyi turdan tura değişmez** — tur 2'nin tek işlevi "eski nesil API'ye bağlanmış olabilir"
   şüphesini kapatmaktır ve bu şüphe **metadata karşılaştırmasıyla kanıtlanarak** kapatılabilir (Ö2).
2. **SCC condensation + grup-içi kısmi sıralama.** SCC tek birim olarak sıralanır (araç yapıyor), ama grubun
   İÇİ tam graf değildir: geri kenarlar çıkarılınca kalan DAG'da seviyeler paralel koşabilir (Ö3). Bu,
   feedback-arc-set (FAS) literatürünün mühendislik kullanımıdır.
3. **Hata sınıflandırmalı erken kesme.** Fixpoint iterasyonunda tur tekrarının düzeltebileceği tek hata
   sınıfı "bayat referans" sınıfıdır (missing metadata/type: CS0006, CS0012, CS0234, CS0246, CS1705,
   CS0117, MSB3245…). Kaynak sabitken **sözdizimi/tip hatası gibi kalıcı hatalar turla düzelmez** — CI
   sistemlerindeki "fail fast" burada "bu hata sınıfı turla düzelmez → kes" olarak uygulanır (Ö4).
4. **Kalıcı çözüm: kesim listesi üretmek.** NDepend/ArchUnit tarzı araçların yaptığı, min-FAS yaklaşımıyla
   "şu kenarları kesersen graf DAG olur" raporudur. OSYS'te dört SCC **ikili** — her biri TEK kenar kesilerek
   çözülür; bu, derleme hızından bağımsız olarak en yüksek kaldıraçtır (Ö6).

**Bilinçli olarak yapılmaması gerekenler** (değişmezler ve kanıt standardı):

- In-process `BuildManager` / tek solution'da `msbuild /m` — shell-out değişmezini ve OutDir/obj
  sözleşmesini bozar; VS zaten circular ProjectReference'ı reddeder.
- `BuildProjectReferences=true` — MSBuild kardeş projelerin `obj`'una yeniden girer (§9.2 gerekçesi).
- Üye bazlı imza/persist'e dönmek — kompozit imzanın kapattığı "kalıcı bayat binary" deliğini geri açar (§7.3).
- Converge etmemiş sonucu persist etmek / testleri-eşiği gevşetmek — kanıt standardı düşürülemez.
- Kullanıcının Resolve komutunu hafızaya bakıp yutmak — denendi ve bilinçli geri alındı (§8.8 "memory
  reports; does not block"). Öneriler bunu korur: daha az İŞ yapılır, komut asla yutulmaz.

## 5. Öneriler (etki × risk sıralı)

### Ö1 — Round ≥ 2'de restore'u atla (hızlı kazanç, ~sıfır risk)

Round'lar arasında ne kaynak ne packages.config değişebilir; round 1'in restore'u yeterli.
`BuildCycleGroupAsync` üye durumunda "restore yapıldı" biti tutar, `InvokeOnceAsync`'e taşınır
(`NeedsRestore`'un tek hesap yeri `RunCoordinator.cs:1733` kalır). OSYS: round başına 7 gereksiz MSBuild
child'ı (no-op NuGet yürüyüşü banner+sertifika+timing ile saniyeler sürer). **Kazanç: round başına
~0.5–1.5 dk; risk: yok.** Test: round 2 invoke'unun komut satırında restore child'ının olmadığını pinleyen
kırmızı test.

### Ö2 — API-yüzeyi kısa devresi: 2. round'u kanıtla daralt (en büyük tek kazanç)

Kural şu gözleme dayanır: kaynak sabitken, **yeşil biten üyenin public/internal yüzeyi turdan tura
değişmez** — yüzey bildirimlerden türetilir. Dolayısıyla round 1 bittiğinde bütün yeşil üyelerin API'leri
nihaidir. Bir üyenin round-1 sonucunun "eski API'ye bağlanmış olma" şüphesi yalnız **geri kenarlarında**
vardır (build-order'da kendinden SONRA gelen grup-içi bağımlılık D: üye, D'nin ESKİ nesil DLL'ini okudu).

- Round 1 öncesi her üyenin diskteki DLL'inin, round 1 sonrası da yeni DLL'inin **metadata yüzey hash'i**
  alınır (System.Reflection.Metadata ile public + internal yüzey; IVT muhafazakârlığı için internal dahil).
- Üye M şu durumda round 2'ye girer: round 1'de fail etti, **veya** herhangi bir geri-kenar bağımlılığının
  eski↔yeni yüzey hash'i farklı, **veya** geri-kenar bağımlılığı fail etti (nihai API'si henüz bilinmiyor).
- Round 2 kümesi **boşsa grup tek round'da kanıtlı Converged olur** — "iki ardışık yeşil tur" kuralı
  gevşetilmez, *kanıtı değiştirilir*: "bağlandığı metadata == nihai metadata" birebir aynı iddiadır.
  `CycleRoundPolicy` saf kalır; yeni girdi "kanıtla atlanmış üyeler yeşil sayılır" olur.

Tipik durumda (gövde değişikliği, API sabit) süre **yarıya iner**; API gerçekten değiştiğinde bugünkü
davranış aynen korunur. Caveat'ler: (a) wildcard `AssemblyVersion` (`1.0.*`) + strong-name kombinasyonu
yüzeyi her derlemede değiştirir — tespit edilirse o grupta kısa devre kapatılır (güvenli yön), (b) hash
alınamayan DLL = "değişti" sayılır. `OutDir`'e dokunulmaz — hash **okumadır**, `ProduceReferenceAssembly`
gibi çıktı değiştiren bir yola gerek yok. Test: yüzeyi sabit iki-üyeli sahte SCC'de round 2'nin hiç
koşmadığını, gövdesi değil imzası değişende koştuğunu pinleyen çift.

### Ö3 — Round içi seviyeli paralellik (yapısal hızlanma)

Bugün "üyeler sırayla, çünkü A, B.dll'i okurken B yazıyor olabilir" (`RunCoordinator.cs:1354`). Bu gerekçe
yalnız **doğrudan kenar komşuları** için geçerlidir; 17 üyeli SCC'nin iç grafı tam graf değil (77/272 kenar).
Kural:

- Üyeler build-order'da sabitlenir; ileri kenarlardan (order'da geriden ileriye) **bariyerli seviyeler**
  kurulur (Kahn). Seviye k tamamen bitmeden k+1 başlamaz → determinizm ve log sırası korunur.
- Ek kısıt: **herhangi yönde doğrudan kenarla komşu iki üye aynı seviyeye konmaz** (geri kenarın "eski nesil
  DLL'ini okuma" semantiği korunur ama aynı anda okuma/yazma — torn read — imkânsızlaşır; gerekirse üye bir
  sonraki seviyeye itilir).
- Seviye içi paralellik worker havuzunun o anki payıyla sınırlanır; ortak klasöre copy contention zaten
  çözülü (§9.5 retry/backoff + Balanced tabanı).

Round süresi üye sayısından kritik yola iner: 17 üyeli grupta 17 → ~6-8 adım (**~2–2.5×**), Ö2 ile
birleşince tipik Resolve **~3–5×** hızlanır. Bu, semantiği en çok inceltilmesi gereken öneri — tek başına bir
tasarım turu + realize/round testleri ister. (Ara adım istenirse: yalnız İKİLİ SCC'lerde bile Ö2+Ö4 tam
sonuç verir, Ö3 en çok 17'liye lazım.)

### Ö4 — Erken kesme: "olmayacaksa devam etme" (kullanıcının açık isteği)

İki katman:

1. **Round 1 sonunda kalıcı-hata kesmesi.** Fail eden üyelerin hataları sınıflandırılır: *bayat-referans
   sınıfı* (CS0006/CS0012/CS0234/CS0246/CS1705/CS0117/MSB3245 **veya** hata metni bir kardeş üyenin
   assembly/namespace'ini anıyor) turla düzelebilir; diğer her şey (sözdizimi, kendi tip hatası…) kaynak
   sabitken **turla düzelemez**. Round 1'in fail seti boş değilse ve TAMAMI kalıcı sınıftaysa → round 2'ye
   girmeden `NoProgress` ilan edilir (bugün bu karar ancak round 2 sonunda çıkabiliyor). Emin olunamayan
   her hata bayat-referans sayılır (muhafazakâr yön: fazladan bir round, asla yanlış kesme).
   `CycleRoundPolicy.Decide`'a `permanentNow` girdisi eklenir; fonksiyon saf kalır.
2. **Tekrar basışta ucuz sondaj (opsiyonel).** Non-convergence hafızası imza eşleşmesini zaten görüyor
   (`retrying — did not converge at this signature`). Eşleşmede round 1, geçen sefer fail eden üyelerle
   **başlar** (build-order içinde deterministik reorder); kalıcı-hata kesmesiyle birleşince cevap ilk birkaç
   invoke'ta belli olur. Komut yutulmaz — gerçek bir deneme yapılır, sadece kanıt öne alınır.

Kazanç: kırmızı senaryoda maliyet ~yarıya, tekrar basışta ~üye sayısından birkaç invoke'a. Test: sahte
"kalıcı hata" üyeli SCC'de tek round koşulduğunu ve `NoProgress` yazıldığını pinleyen kırmızı test; sınıflandırıcının
kardeş-anmayan CS0246'yı (bayat referans olabilir) KESMEDİĞİNİ pinleyen ters test.

### Ö5 — Faturayı görünür yap (ucuz, algıyı düzeltir)

- Buton tooltip'i: `Build dependency cycles — N cycles · M projects` → sonuna `(+U upstream to build)`
  (preview'daki willBuild'den; U=0 ise eklenmez).
- `cycleCompleted` zaten round/süre taşıyor; decision.log grup satırına toplam süre kırılımı (round × üye)
  eklenebilir. ETA cycle terimi Ö2/Ö3 ile birlikte güncellenmek zorunda (`EtaCalculator` baseline çarpanı ve
  bölünmeme varsayımı değişir) — CLAUDE.md kuralı gereği eski ETA davranışını pinleyen testler yeni kurala
  yeniden yazılır.

### Ö6 — Cycle raporu: kalıcı çözümün verisi (kurumsal yol)

Cycle'ın en hızlı derlemesi, olmayan cycle'dır. Araç bugün döngü yolunu proje loguna yazıyor; bir adım
ötesi, SCC başına **iç kenar listesi + kesim önerisi** üreten bir rapor (decision.log'a ya da ayrı bir
`cycles.md`/konsol raporuna):

- OSYS'teki dört İKİLİ SCC'nin her biri **tek kenar** kesilerek çözülür (örn.
  `OSYS.Business.Finance ↔ OSYS.Business.NewSales` — yönlerden biri interface/Types katmanına inmeli).
- 4'lü Types SCC'leri 3-4 kenarla, 17'li UI SCC'si daha derin ama rapor kesim adaylarını sayısal
  gösterebilir (hangi kenar en çok döngüde).
- Bu rapor OSYS ekibinin borç eritme listesidir; her eritilen kenar Resolve maliyetini kalıcı düşürür.

### Ö7 — Spike: job-içi izole derleyici sunucusu (büyük potansiyel, şartlı)

R3'teki 2.9×'in kaynağı `UseSharedCompilation=false`; gerekçesi "server, job DIŞINDA yaşayan bir
`VBCSCompiler`'da emit yapabilir" (§9.2). Araştırılacak mekanizma: **run'a/supervisor'a özel pipe adıyla**
`UseSharedCompilation=true` — server ilk MSBuild child'ı olarak doğar → inner job'a kalıtımla girer →
`KILL_ON_JOB_CLOSE` emit penceresini kapalı tutar; dışarıdaki (VS'nin) server'ına bağlanma pipe izolasyonuyla
imkânsızlaşmalı. Pipe adı kontrol edilemiyorsa **yapılmaz** (değişmez önce gelir). Kanıtlanırsa kazanç tüm
koşulara yayılır; en çok da aynı projeleri dakikalar içinde 2-3 kez derleyen cycle round'larına. §9.2'nin
şartı aynen: "emit penceresini kapatan mekanizma" — hızlı sayı değil.

### Önerilen sıra ve kaba etki

| Sıra | Öneri | Beklenen etki (OSYS) | Çaba | Risk |
|---|---|---|---|---|
| 1 | Ö1 restore-once + Ö5 fatura | dakikalar + doğru beklenti | ~1 gün | ~0 |
| 2 | Ö4 erken kesme | kırmızı senaryoda ~2×, tekrar basışta ~10× | 1-2 gün | düşük (muhafazakâr sınıflandırma) |
| 3 | Ö2 API kısa devresi | tipik yeşil senaryoda ~2× | 2-4 gün | orta (caveat'ler net) |
| 4 | Ö3 seviyeli paralellik | büyük SCC'de ek ~2-2.5× | 3-5 gün | orta-yüksek (eşzamanlılık kısıtı) |
| 5 | Ö6 cycle raporu | kalıcı; derlemeden bağımsız | 2-3 gün | ~0 |
| 6 | Ö7 spike | kanıtlanırsa ~2-3× her şeyde | spike 1-2 gün | şartlı |

Birleşik hedef: bugün ~10-15 dk süren tipik bir Resolve → **~2-4 dk** (Ö1+Ö2+Ö4), Ö3 ile **~1-2 dk**;
başarısız cycle'da cevap ("olmuyor, şu üye şu hatayla") **ilk round sonunda**.

## 6. Korunacaklar (bilinçli dokunulmayanlar)

- **Kapsamın upstream'i içermesi** — kalıcı bayat binary deliğini kapatan tek mekanizma (`CycleRunScope`
  gerekçesi). Daraltılmaz; yalnız görünür yapılır (Ö5).
- **"Converge etmeyen persist etmez"** ve yeşillerin invalidate'i — kanıt standardıdır, kalır. (Ö2 bu kanıtı
  ucuzlatır, gevşetmez.)
- **İki-yeşil-tur ölçütü** — kaldırılmaz; Ö2 yalnız aynı iddianın daha ucuz kanıtını ekler.
- **Komut yutulmaz** — hafıza bloklamaya dönüştürülmez (§8.8'de denenip geri alınmıştı); Ö4/2 yalnız işi
  kısaltır.
- Shell-out / nested job / OutDir değişmezleri — hiçbir öneri bunlara dokunmaz (Ö7 ancak değişmezi
  koruyabilirse yapılır).

## 7. Doğrulama / ölçüm planı (geliştirmeden önce)

1. Gerçek dağılımı görmek için mevcut loglardan invoke süreleri: `%LOCALAPPDATA%\BuildOrchestrator\logs\`
   altındaki son Cycles koşusunun `decision.log` + `cycleCompleted` süreleri (round × üye kırılımı).
2. No-op restore'un gerçek maliyeti: packages.config'li bir üyenin round-2 restore child süresi.
3. 17'li SCC'de tek üye derleme süresi (p50/p95) → Ö2/Ö3 kazanç projeksiyonunun kalibrasyonu.
4. Ö7 için spike: pipe izolasyonu mümkün mü; server'ın inner job'a düştüğünün kanıtı (Job Object sorgusu).

## 8. Açık sorular (geliştirme oturumu için)

- Ö2'de IVT/strong-name taraması grup başına mı, üye başına mı raporlansın?
- Ö3'te seviye içi worker payı: grup, havuzdan kaç worker alabilir (öteki gruplar + upstream'le adalet)?
- Ö4 sınıflandırıcısında hata metni eşleşmesi hangi alandan okunacak (MSBuild satırı zaten proje loguna
  akıyor; `InvokeOutcome`'a hata kodu seti eklemek gerekir)?
- Ö5 tooltip'i preview'ın hangi anındaki U'yu göstermeli (Sync sonrası mı, basış anındaki plan mı)?

---

*Ölçüm scripti (Tarjan + kenar türetme, salt-okur) scratchpad'de çalıştırıldı; kalıcı bir yere alınmak
istenirse geliştirme oturumunda `tools/` altına taşınabilir.*
