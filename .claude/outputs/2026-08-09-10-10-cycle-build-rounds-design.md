# Tasarım — Dairesel bağımlılıkların turlarla derlenmesi

## 1. Sorun

Bugün bir SCC (strongly-connected component) üyesi **hiç derlenmiyor**:

- `WillBuildEvaluator.Evaluate` ilk satırında `if (inCycle) return false;` — will-build noktası her zaman gri.
- `ReadySetScheduler` kurulum anında üyeleri `Skipped` işaretliyor, gerekçe `"in dependency cycle"`.
- Satırda ve graf düğümünde turuncu `Cycle` rozeti duruyor.

Gerçek OSYS reposunda bu, 177 projenin ~32'si demek. Bu davranış "güvenli" değil: o projeler hiç
tazelenmediği için **bağımlıları, tarihi belirsiz DLL'lere karşı derleniyor** ve bu durum kendiliğinden
hiç düzelmiyor.

## 2. Neden çözülebilir

Bu grafın **birincil kenar sinyali `HintPath`**, `ProjectReference` değil (`GraphBuilder.BuildEdges`,
ARCHITECTURE §6.4). Yani A→B kenarı çoğunlukla "A, B'nin ürettiği DLL'e dosya yolundan referans veriyor"
demek.

MSBuild bir `HintPath` döngüsünü **reddetmez** — sadece DLL'in diskte var olmasını ister. Dolayısıyla
A↔B döngüsü iterasyonla çözülebilir:

1. A derlenir → diskteki **eski** B.dll'e karşı
2. B derlenir → artık **yeni** A.dll'e karşı
3. A tekrar derlenir → **yeni** B.dll'e karşı

Bu bir sabit-nokta iterasyonudur ve elle yapıldığında çalıştığı gözlenmiştir.

## 3. Karar özeti

| Konu | Karar |
|---|---|
| Tetikleme | **`Build`'in içinde, otomatik.** Ayrı buton/mode yok, developer'ın hatırlaması gereken ritüel yok |
| Konum | SCC, build-order'da zaten durduğu yerde derlenir — ekstra faz yok |
| Tur sayısı | Taban 2, tavan 3 |
| SCC içi paralellik | **Yok** — sıralı zorunlu |
| Yakınsamazsa | `Failed` + logda gerekçe |
| Tekrar deneme | O imza için bir daha denenmez (kaynak değişene kadar) |
| Geri dönüş | Settings'te kill switch, varsayılan **AÇIK** |
| Rozet | Turuncu `Cycle` rozeti **kalır** — anlamı "derlenmeyecek" değil, "döngüde" olur |

## 4. Davranış

### 4.1 Sync sonrası

- Cycle üyeleri de herkes gibi gerçek will-build noktasını alır: amber (kirli) / gri (güncel) / hollow (taban yok).
- SCC **tek bileşik imza** taşıdığı için (§7.3) bir üye kirliyse **tüm SCC** kirlidir. Üyeler ayrık davranmaz;
  hep birlikte amber ya da hep birlikte gri olurlar.
- Turuncu `Cycle` rozeti (satırda statü üçgeni, grafta turuncu düğüm) yerinde kalır. Amacı döngüyü
  normalleştirmek değil, yapısal sorunu görünür tutmak.

### 4.2 Build sırasında

**Dispatch mekanizması:** SCC, scheduler'a **tek bir iş kalemi** olarak girer (üye üye değil). Tüm *dış*
bağımlılıkları terminal olduğunda dispatch edilir; onu alan worker turların tamamını yürütür ve SCC'nin
tüm üyelerinin sonucuyla `Complete` çağırır. Bu kasıtlı bir tercihtir: alternatif — turları
`RunCoordinator`'da scheduler'ı atlayarak yürütmek — "bağımlılıklar terminal mi" mantığını ikinci bir yerde
kopyalardı (tek doğruluk kaynağı ihlali).

Konum ek bir sıralama gerektirmez: `TopoSort` SCC üyelerini condensation DAG'ında zaten ardışık
yerleştiriyor, yani SCC build-order'da durduğu yerde derlenir.

SCC içinde üyeler **sırayla** derlenir; paralellik yoktur, çünkü A `B.dll`'i okurken B aynı dosyayı yazıyor
olur. SCC bir worker slot'unu turlar boyunca meşgul eder; diğer worker'lar SCC dışı işlere devam eder.

### 4.3 Settings

`UiState`'e `BuildDependencyCycles` (bool, varsayılan `true`) eklenir — `UseWorktree` / `Autostart` ile
aynı kalıcılık yolu (`JsonUiStateStore`).

Kapalıyken **bugünkü davranış birebir geri gelir**: pre-skip çalışır, will-build gri kalır, rozet
"derlenmeyecek" anlamına döner. Kapalı hâl için yeni kod yazılmaz — mevcut kod yolu yerinde bırakılır.

Anahtarın gerekçesi tek cümledir: *"döngü derlemesi bu repoda çok yavaş veya sorunlu, bugünkü davranışa dön."*
Yakınsamama için değil — onu motor kendi halleder (§6).

## 5. Tur mantığı ve durma kuralı

Turlar arasında **kaynak değişmez**. Bu, tur sayısının üst sınırını verir: tur 1 her üyenin DLL'inin public
API'sini nihaileştirir; tur 2 herkesi nihai API'lere karşı yeniden derler. Tur 3, tur 1'de patlayan üyelerin
zincirine tolerans içindir.

`F_r` = tur `r`'de derlemesi başarısız olan üyeler kümesi.

| Koşul | Karar |
|---|---|
| `F_r` ve `F_{r-1}` ikisi de boş | `Converged` — dur, hepsi başarılı |
| `F_r == F_{r-1}` (aynı **küme**) | `NoProgress` — dur, kalanlar başarısız |
| `r == 3` (tavan) | `CapReached` — dur |
| Aksi | `Continue` |

`CapReached` sonucunda `F_3`'teki üyeler `Failed`, kalanlar `Succeeded` olur — **ama ekranda sessiz kalmaz**:
iki ardışık yeşil tur görülmediği için kalan üyelerin çıktıları bir kuşak geride olabilir, bu yüzden
`Succeeded` olanlar **mevcut dependency-issue üçgenini** taşır (§8.3'teki sabit 14 px slot; yeni ikon
eklenmez).

Bu iki durum aynı cümleyi söylediği için ikon paylaşılır — *"derlendi ama bağımlılık tarafında bir pürüz var,
çıktısına tam güvenme"*. Ayrım tooltip metnindedir:

| Durum | Tooltip |
|---|---|
| Bağımlılık patladı (bugünkü) | `Failed dependency: X — last successful output referenced` |
| Döngü tam oturmadı (yeni) | `Cycle did not fully settle — output may be one generation stale` |

Gerekçe — neden "hata vermedi" yetmiyor: A, B'nin **kaynağına** değil, diskteki derlenmiş **B.dll**'ine karşı
derlenir. B'de `Hesapla(int)` → `Hesapla(long)` olduysa, tur 1'de A eski B.dll'e karşı sorunsuz derlenir ve
A.dll'e `Hesapla(int)` çağrısı gömülür; B derlendikten sonra o metot artık yoktur ve hata **çalışma anında**
`MissingMethodException` olarak çıkar. Turun amacı hatayı bulmak değil, bu sessizliği bozmaktır.

Tavanın 3 olması kural dizisinden doğar: tur 1 ve 2 yeşilse `Converged` zaten 2'de olur; 3. tur yalnız
tur 1'de patlayan olduğunda gerekir. Tur 1 ve 2 aynı kümede patlarsa `NoProgress` 2'de durur.

**Sayıya değil kümeye bakılır:** `{A,C} → {B,D}` sayı olarak aynıdır ama ilerleme değil, salınımdır.

**Tek yeşil tur yeterli değildir:** tur 1'de A eski B.dll'e karşı derlenir; yeşil geçse bile çıktısı bir kuşak
geride olabilir. Bu yüzden durma koşulu *iki ardışık* yeşil turdur.

**Tavanın düşük olması bilgi kaybettirmez:** turlar diskteki duruma göre idempotenttir. Bir sonraki `Build`
kaldığı yerden devam eder.

## 6. Yakınsamama hafızası

Bir SCC yakınsamazsa, **o imza için bir daha denenmez.** Kaynak değişip bileşik imza değişene kadar üyeler
`Skipped` kalır; log `did not converge at signature X` der.

Bu, projenin mevcut incremental ilkesinin aynısıdır (kaynak imzası + kaydedilmiş state) — yeni bir kavram
değil. Sonuç: yapısal olarak bootstrap edilemeyen bir döngü, her build'de tur harcayıp garantili kırmızı
vermez.

## 7. Event ve sayaç kuralı

> **Ara tur sonuçları yayınlanmaz.** Bir SCC üyesi, SCC'nin turları bitene kadar `Succeeded`/`Failed`
> damgası almaz. Yalnız son turun sonucu event olarak çıkar.

Gerekçe: SCC zaten tek bir derleme birimidir (§7.3, tek bileşik imza). Yarı bitmiş bir birimi "bitti" saymak
yanlış olur. Bu kural üç sorunu birden çözer:

- **Progress geri gitmez.** Aksi hâlde tur 1'de `Succeeded` olan üye tur 2'de `Building`'e dönüp sayacı
  düşürürdü.
- **`RunCounters` doğru kalır.** (Zaten satır durumundan türetiliyor, biriktirmiyor — çift sayma riski
  yapısal olarak yok. Bu kural onu ayrıca geri-gitmeye karşı korur.)
- **Konsolda proje başına tek sonuç satırı** yazılır.

Ekranda:

- SCC üyeleri turlar boyunca `Queued`; o an derlenen üye `Building`.
- Satırdaki süre **turların toplamıdır** — gerçek maliyeti gösterir, son turunkini değil.
- Yeni run-seviyesi event: `cycleRoundStarted(sccName, round, cap, memberCount)` → konsolda tur başlığı,
  action bar'da `cycle round 2/2` göstergesi. Proje-seviyesi event sözleşmesi değişmez.

**ETA düzeltmesi:** ETA (§8.4) kuyruktaki projelerin tahminini paralelliğe böler. SCC işi sıralıdır ve tur
sayısıyla çarpılır. SCC üyelerinin katkısı `tahmin × 2` (taban tur sayısı) olarak alınır ve paralelliğe
**bölünmez**. Tur 3'e girilirse ETA bir kez daha kayar; bu kabul edilen sapmadır, tavan zaten 3'tür.

## 8. Bileşenler

| Birim | Yer | Sorumluluk |
|---|---|---|
| `CycleRoundPolicy` | `Core/Planning` | **Saf** karar fonksiyonu: `(tur no, F_r, F_{r-1}) → Continue \| Converged \| NoProgress \| CapReached`. I/O yok, process yok |
| `WillBuildEvaluator` | `Core/Planning` | `inCycle` kısa devresi anahtara bağlanır |
| `ReadySetScheduler` | `Core/Scheduling` | Pre-skip yalnız anahtar kapalıyken; açıkken SCC bir birim olarak dispatch edilir |
| SCC tur döngüsü | `Supervisor/RunCoordinator` | Turları yürütür, `CycleRoundPolicy`'yi sorar, kararı uygular, ara sonuçları yaymaz |
| Yakınsamama kaydı | `Core/State/BuildStateStore` | `did not converge` + imza |
| `CycleRoundStartedEvent` | `Contracts` | Run-seviyesi tur bildirimi |
| Üçgen tooltip'i | `App/Views/ProjectRow` | Mevcut dep slotu yeniden kullanılır; yalnız metin dallanır |
| ETA katkısı | `Core` (ETA hesabı) | SCC işi sıralı + tur çarpanı |
| Kill switch | `App/Views/SettingsDialog` + `Shell/UiState` | `BuildDependencyCycles`, varsayılan `true` |

Tasarım kararı: **tur mantığı Core'da saf bir birim olarak durur; çağıran onu yalnız *ne zaman*
çalıştıracağına karar verir.** Planlama Core'da kalır, iş mantığı App/Supervisor'a sızmaz.

## 9. Test stratejisi

Kırmızı test kuralı geçerli: her davranış, onu yakalayan test KIRMIZI gösterilmeden yazılmaz.

- `CycleRoundPolicy` — dört karar dalının her biri için ayrı test (yakınsama, salınım/aynı küme, tavan,
  devam). Saf fonksiyon, WPF'siz, process'siz.
- `WillBuildEvaluator` — anahtar açık/kapalı × inCycle matrisi. **Mevcut testi silinmez**: bugünkü iddiayı
  (`inCycle → false`) pinleyen test, YENİ kuralı (anahtar kapalıyken `inCycle → false`, açıkken normal karar)
  pinleyecek şekilde yeniden yazılır ve doc'una eski iddia + değişme gerekçesi yazılır.
- `ReadySetScheduler` — anahtar açıkken SCC'nin pre-skip edilmediği, dış bağımlılıkları terminal olmadan
  dispatch edilmediği.
- Tur döngüsü — ara turda `projectSucceeded` yayılmadığı; yalnız son turun sonucunun çıktığı.
- Yakınsamama hafızası — aynı imzayla ikinci koşuda tur harcanmadığı.
- `RunCounters` / progress — tur boyunca sayacın geri gitmediği.
- ETA — SCC katkısının paralelliğe bölünmediği.
- **Realize testi:** Settings'e eklenen toggle yeni bir XAML şablonu getiriyorsa (kod tabanında hazır bir
  toggle switch kontrolü YOK — ARCHITECTURE §14.7 bunların özel kontrol olduğunu söylüyor) realize testi
  eklenir; `window.Content` üzerinde yapılır.

Bitişte tam süit yeşil (token/motion/D8 guard'ları dahil). Bütçe/eşik gevşetmek yasaktır.

## 10. Doküman güncellemeleri

Anlatı üslubu korunur; changelog yazılmaz, ilgili bölüm yerinde yeniden yazılır.

- **ARCHITECTURE §6.5** — "Cycle members remain in the plan, flagged `InCycle`, and are pre-skipped by the
  scheduler" artık koşullu; turlarla derleme anlatılır.
- **ARCHITECTURE §7.4** — will-build tri-state tablosundan `inCycle → false` kısa devresi çıkar.
- **ARCHITECTURE §8.2** — scheduler'ın SCC'yi birim olarak ele alması.
- **ARCHITECTURE §8.4** — ETA'nın SCC katkısı.
- **ARCHITECTURE §14.3** — `Cycle` satırının metni: "derlenmeyecek" değil "döngüde".
- **README** — Settings'teki yeni anahtar.

## 11. Kabul edilen sınırlar

1. **Soğuk başlangıç yakınsamayabilir.** SCC hiç derlenmemişse diskte DLL yoktur; tur 1'de herkes patlar,
   tur 2'de de. Elle de aynı duvar. Sonuç `Failed` + net gerekçe olur; bugünkü sessiz `Skipped`'tan dürüsttür.
2. **SCC içinde paralellik yok.** Dosya yarışı nedeniyle mimari olarak imkânsız; optimize edilemez.
   Maliyet: ~32 üye × 2 tur, sıralı ≈ 2 dk. Yalnız SCC kirliyken ödenir.
3. **Bu bir yama, çare değil.** Asıl çözüm `HintPath` geri-kenarını kaynakta kırmaktır. Rozet bu yüzden
   kaldırılmıyor.

## 12. Kapsam dışı

- Döngüyü *teşhis* eden yüzey (hangi referans geri-kenarı yaratıyor) — ayrı bir iş.
- SCC'ler arası paralellik.
- `Rebuild` / `RetryFailed` modlarının tur davranışının özelleştirilmesi — `Build` ile aynı kuralı izlerler.
