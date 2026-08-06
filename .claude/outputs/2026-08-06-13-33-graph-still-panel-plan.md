# Graf paneli — "Durgun Graf" planı

> **Karar (4/4 review aynı yönde):** paneli kaldırma, **hareketi** kaldır.
> **Öncül:** v1 `main`'de (`c6535eb`). v2 "Yakın Odak" tasarımı
> (`2026-08-06-10-06-graph-close-focus-design.md`) — **yarısı hayatta**, koşu modu kamerası düşüyor.

---

## 0. Bu planın cevaplaması gereken üç istek

| İstek | Nerede karşılanır |
|---|---|
| Stabil, göz yormayan | Faz 1 — koşarken **hiçbir şey hareket etmez** |
| Proje isimleri okunur | Faz 2 (incelemede) + Faz 3 (koşu için karar) |
| Performansı etkilemez | Faz 0 ölçer, Faz 1'in "kenar yok"u en büyük kazancı verir |

---

## Faz 0 — ÖLÇÜM (BLOKLAYICI · koda dokunulmaz)

Eng review'ın blokladığı nokta. **"Kasma"nın kaynağı isimlendirilmeden Faz 1 başlamaz.**

### A/B: view mode — ama iddiası DAR

Panel bugün `list` ve `focus` modlarında kapalı, `graph` modunda açık. Aynı workspace'te aynı build'i iki
modda koşmak ucuz bir karşılaştırma verir.

> **Dikkat — bu A/B "bedava" değil ve toplam maliyeti ölçmüyor.** `Konum:`
> `ShellRoot.xaml.cs:191` paneli yalnız **`Collapsed`** yapıyor; `MainWindow.xaml.cs:552` ise `SetGraph`'ı
> view mode'a **bakmadan** çağırıyor. Yani `list` modunda da 1214 kenarın görseli **kuruluyor**, sadece
> gizleniyor. Dolayısıyla A/B **kurulum** maliyetini değil, **render + animasyon** farkını izole ediyor.
> Bu hâliyle hâlâ değerli — sorduğumuz asıl soru zaten o — ama "kasma graftan mı geliyor" diye geniş
> okunamaz.

### Ön koşul: A1 önce düzelmeli

Aynı yerden çıkan bir **bug** var ve ölçümü kirletiyor: panel `Collapsed` iken de `UpdateStatuses` her
200 ms'de `ApplyEdgeStyles`'ı 1214 kenar üzerinde koşturuyor. Kimsenin görmediği iş, her tick.

**A1 bu planın hiçbir kararını beklemez** — panel kalsa da kalkmasa da geçerli bir kazanç. Ayrı ve küçük
bir iş olarak **önce** gider: panel görünmezken tick başına kenar/statü işi atlanır + regresyon testi.
Sonra ölçüm temiz bir zeminde yapılır.

### Ölçümler

| # | Soru | Yöntem | Karar değeri |
|---|---|---|---|
| **M1** | Kasma **render/animasyondan** mı? | Aynı build, `graph` vs `list` mode; UI thread blok süresi ve atlanan kare | Fark yoksa **plan burada durur**, iş konsol tarafına kayar |
| **M2** | Her zaman mı, yalnız 150+ projede mi? | 100 ve 200 düğümlük sentetik workspace | Yalnız 150+ ise sorun "panel" değil **"150 kapısının ardındaki ağır yol"** |
| **M3** | Hareket mi suçlu, varlık mı? | reduced-motion AÇIK (kamera geçişi ve dash animasyonu devre dışı) | Reduced-motion'da geçiyorsa suçlu **hareket**; geçmiyorsa suçlu **varlık** (kenar sayısı) |
| **M4** | Maliyet ne zaman? | Kamera geçişi sırasında vs durgunken kare maliyeti | Geçiş sırasındaysa kamera, durgunken de varsa kenarlar |
| **M5** | Maliyet nerede? | visual-tree kurulumu vs re-raster ayrımı | Bugüne dek yalnız **açılış** ölçüldü; animasyon karesi hiç ölçülmedi |

### Çıkış kriteri

Faz 1'e geçmek için **M1 ve M3'ün** cevabı gerekiyor. M1 "graf değil" derse plan iptal. M3 "varlık" derse
Faz 1'in ağırlığı kamera değil **kenarlar** olur.

> **Kayda geçen düzeltme:** bu oturumda kullanıcıya "kasma boyamadan geliyor" denildi. Bu bir **hipotezdi**,
> ölçüm değil. Elimizdeki tek ölçüm açılış maliyetinin %64-72'sinin visual-tree kurulumu olduğunu söylüyor
> ve o animasyonu kapsamıyor. Faz 0'ın var olma sebebi budur.

### Faz 0'ın çıktısı bir TEST olmalı, tek seferlik koşu değil

`UiResponsivenessBudgetTests` sync adımlarını ölçüyor, **animasyon karesini ölçmüyor**. M4 ve M5 için yeni
enstrüman gerekiyor ve o enstrüman tekrarlanabilir olmalı — aksi halde Faz 1'den sonra "düzeldi mi"
sorusunu cevaplayamayız.

**Süre tahmini:** enstrüman yazmak dahil **yarım gün**. (Önceki "~1 saat" tahmini enstrümanın var olduğunu
varsayıyordu; yanlıştı.)

---

## Faz 1 — Durgun varsayılan (ölçüm doğrularsa)

Ucuz, geri dönüşlü, çoğu **varsayılan değiştirme**. Kod silinmez; davranış kapatılır.

| Değişiklik | Bugün | Sonra |
|---|---|---|
| Takip kamerası | Cepheyi izler, 460 ms'lik geçişler | **Kapalı** — koşarken kamera hiç hareket etmez |
| Kenar sisi | 1214 kenar %16'ya iner | **Kapalı** — sislenecek kenar kalmaz |
| Kenarlar | Kurulumda hepsi kurulur | **Varsayılanda yok** (yalnız incelemede) |
| Kamera | Statü, seçim, settled ile hareket eder | Yalnız **derleme yokken** ve yalnız seçimle |
| Koşarken düğüm | Nabız + renk + sis | Yalnız **renk + sabit halka** (gözle ayarlanacak) |
| `FOLLOW PAUSED` pili + 4 sn dönüşü | Manuel moddan takibe döndürür | **Kalkar** — dönülecek takip kalmıyor |
| Kuşbakışı ölçek kelepçesi | `MinScale` 0.68 | **İnilir** — graf gerçekten sığsın (aşağıda) |

**Neden silme değil kapatma:** CEO review'ın kapısı — 7.500 satırı silmek tek yönlü, varsayılanı çevirmek
iki yönlü. Durgun sürüm kendini kanıtladıktan **sonra** silme ayrı bir karar olur.

> **Ama iki kapsam karıştırılmasın (eng review A4).** CEO'nun geri-dönülebilirlik argümanı **paneli**
> silmeye dairdi (7.500 satır), takip kamerasına (~300 satır) değil. Geri dönüş mekanizması git'tir, ölü
> dal değil. Kodda duran ama varsayılanda kapalı bir yol, testleri hâlâ onu pinlerken, **kimsenin
> koşmadığı bir konfigürasyonu bakmak** demek. Karar: takip kamerası ve sis **silinir**; panel kalır.

**En büyük perf kazancı burada:** kenar görselleri 1214 → 0. Bu aynı zamanda sis sorusunu tümden ortadan
kaldırıyor.

### Kamera kuralının tamamı

Mod önceliği diye bir şey yok. Kamera için tek soru var: **bir şey derleniyor mu?**

| Durum | Kamera |
|---|---|
| Bir şey derleniyor | **Hiç hareket etmez** — ne tıklanırsa tıklansın |
| Derleme yok + seçim var | Seçili düğümü ve bağımlılıklarını çerçeveler |
| Derleme yok + seçim yok | Kuşbakışı |
| Sürükleme/zoom sürüyor | Kullanıcıda |
| Sürükleme bırakıldı | **Bırakıldığı yerde kalır** — hiçbir yere geri uçmaz; askı kalkar, sonraki seçim normal çalışır |
| Boş zemine çift tık | Kuşbakışına oturur (açık çıkış yolu) |
| Yeni topoloji (Sync) | Kuşbakışına oturur |

**Seçim ile kamera ayrılır.** Seçim her zaman çalışır ve **vurgu** yapar (diğerleri söner, o düğümün
kenarları ve isimleri belirir) — çünkü seçim aynı zamanda konsolu o projenin loguna geçiren şeydir
(`RunViewModel.Stream.cs:148` tek seçim kaynağı; `MainWindow.xaml.cs:254` konsolu çevirir) ve koşarken
o etkileşim elden alınamaz. **Kamera** ise yalnız derleme yokken çerçeveler.

Koşarken kamera kuşbakışında durduğu için **her düğüm ekrandadır** — dolayısıyla "kamerayı oynatmadan
vurgula" her zaman görünür bir sonuç verir; seçilen şey kadraj dışında kalmaz.

### Kuşbakışı gerçekten sığmalı

Ölçülen dünya genişliği ~1379 px (katman başına ~39 düğüm × 34 px aralık; OSYS'te ~44/katman, benzer).
600 px'lik bir panele sığması için ölçek **~0.44** olmalı. Bugünkü kelepçe **0.68** ⇒ grafın yaklaşık
**üçte biri kadraj dışında**. Kamera hiç hareket etmezse o üçte bir *hiç görünmez* — bu, durgun graf
kararıyla doğrudan çelişir.

`MinScale = 0.68` kelepçesi düğümler okunmaz olmasın diye vardı; koşarken zaten isim okumadığımız için o
gerekçe düştü. **Kelepçe indirilir**, 177 projede düğümler ~11 px'lik renkli karelere iner ve **her şey
aynı anda ekranda olur**. Yeni taban değeri **gözle** kararlaştırılır; bu belge sayı pinlemez.

**Gözle bakılacak tek risk:** 11 px'lik bir karede üç parlaklık kademesi (derleniyor / bitti / başlamadı)
ayırt edilebilir mi? Yetmezse çare kamerayı geri getirmek **değil**, vurguyu güçlendirmektir.

### Kabul edilen takas

Bu karar ekrana "177 küçük renkli kare" koyuyor. Etkileyici bir görsel değil, **sakin bir durum
haritası**. İlk istek "görsellik katmak"tı; bu takasla yerine **okunabilirlik ve dinginlik** geçiyor.
Dört review de o yöne işaret ettiği için doğru takas sayılıyor — ama takas olduğu bilinerek yapılıyor.

---

## Faz 2 — İnceleme modu (panelin asıl işi)

> **Faz 1 ile AYNI turda gitmeli.** Faz 1 kenarları kaldırıyor, Faz 2 seçimle geri getiriyor; ikisi tek
> kuraldır. Faz 1 tek başına yayınlanırsa grafta **hiç kenar olmayan** bir ara sürüm oluşur — bugünden
> daha az bilgi veren bir durum. Ayrı fazlar olarak yazılmalarının tek sebebi anlatım sırası.

DevEx review'ın çerçevesi: graf **bakılan** değil **gidilen** bir yer; "X neden bekliyor" sorusunu
hata sonrası cevaplar. O yüzden panel koşarken sessiz durur, **seçince** konuşur.

### Giriş noktası: liste (graf değil)

177 düğüm arasında doğru kareyi gözle bulmak zor. Asıl giriş yolu **liste**: listede bir projeye
tıklarsın, graf onu vurgular ve (derleme yoksa) çerçeveler. Graftaki tıklama ikinci yoldur.

**Kablo zaten var, yeniden kurulmayacak:** `MainWindow.xaml.cs:584` seçimi grafa iletiyor
(`Shell.GraphHost.SelectedNode = name`), `MainWindow.xaml.cs:214` ters yönü bağlıyor
(`GraphHost.SelectionChanged`). Plan bunu **asıl giriş yolu olarak adlandırır**, yeni kablo çekmez.

Seçince:
- Seçili düğüm **+ bağımlılıkları** kadraja girer (tamamı; kümeye girmeyenler kadraj dışında kalabilir)
- Yalnız o alt grafın kenarları görünür
- **O kümenin isimleri okunur** — ekranda az düğüm olduğu için yer var
- Gerisi söner

Kamera burada hareket eder ve bu **meşrudur**: kullanıcı istedi, dolayısıyla dikkat için yarışmıyor.
Design review'ın "aynı anda tek kahraman" ilkesi korunur — koşarken kahraman konsol, incelerken graf.

Bu, v2 tasarımının hayatta kalan yarısı; kararları (yalnız bağımlılıklar, tamamı kadraja, isim kırpması)
oradan aynen gelir.

---

## Faz 3 — İsimler (Faz 1-2 bittikten sonra karar)

Faz 2 isim sorununu **incelemede** çözüyor. **Koşarken** çözmüyor — ve çözmesi gerekip gerekmediği açık.

| Seçenek | Ne demek | Maliyet |
|---|---|---|
| **(a) Koşarken isim yok** | Graf renk ve şekil verir; adı listede/konsolda okursun, tooltip zaten var | Sıfır — bugünkü davranış |
| **(b) Yalnız derlenenin ismi** | Bugünkü odak muafiyeti; 5-10 paralel derlemede yan yana gelirse örtüşür | Sıfır — bugünkü davranış |
| **(c) Yerleşimi isme göre aç** | Düğüm aralığı etiket genişliğinden türer → geniş, **kaydırılabilir** tuval; her isim hep okunur | Yüksek — yerleşim algoritması değişir, "bir bakışta neredeyiz" kaybolur |

Faz 1-2 bittikten sonra **gözle** karar verilir. Bu plan (c)'yi ne vaat ediyor ne dışlıyor.

---

## Kamera durum diyagramı

Uygulamada bu diyagram `GraphView`'ın kamera bölümüne yorum olarak da girer (eng review A6).

```
                        ┌──────────────────────────────┐
        Sync / yeni     │                              │
        topoloji  ─────►│         KUŞBAKIŞI            │◄──── boş zemine çift tık
                        │  (fit; her düğüm ekranda)    │
                        └──────┬───────────────▲───────┘
                               │ seçim var     │ seçim kalktı
                    (yalnız derleme YOKKEN)    │
                               ▼               │
                        ┌──────────────────────┴───────┐
                        │        İNCELEME              │
                        │ seçili + bağımlılıkları       │
                        │ kadrajda, kenarlar ve isimler │
                        └──────────────────────────────┘

        ── derleme sürerken ─────────────────────────────────────────────
        Kamera hangi durumdaysa ORADA DONAR. Seçim yine çalışır ve
        VURGU yapar (söndürme + kenar + isim), kamera kıpırdamaz.
        ──────────────────────────────────────────────────────────────────

        ── sürükleme / wheel ────────────────────────────────────────────
        Kamera kullanıcıya geçer. Bırakınca BIRAKILDIĞI YERDE kalır;
        geri uçmaz. Askı kalkar: sonraki seçim (derleme yoksa) normal
        çalışır. Zamanlayıcı YOK, pil YOK.
        ──────────────────────────────────────────────────────────────────
```

## Testlerin geleceği — planın en büyük gizli maliyeti

~4.600 satır test, kapatılacak/silinecek davranışı pinliyor. CLAUDE.md "bilerek değişen kuralı pinleyen
test silinmez, YENİ kuralı pinleyecek şekilde yeniden yazılır + doc'una eski iddia ve gerekçe" diyor.
Bu, tek satırla geçiştirilemez. Uygulama planı **her test dosyası için** şu üçlüyü açıkça yazmalı:

| Sınıf | Ne olur |
|---|---|
| **Yaşar** | Kural değişmedi (yerleşim, culling, tooltip, erişilebilirlik, seçim kablosu) |
| **Yeniden yazılır** | Kural bilerek değişti (kamera hedefi, ölçek politikası, kenar yaşam döngüsü, etiket kararı) — doc'una eski iddia + gerekçe |
| **Gerekçesiyle silinir** | Pinlediği özellik tamamen kalktı (takip dönüşü, `FOLLOW PAUSED` pili, sis) — silme gerekçesi commit mesajına ve ilgili doc'a yazılır |

Bu sınıflandırma yapılmadan iş büyüklüğü bilinemez. **Uygulama planının ilk adımı bu envanterdir.**

## Başarı ölçütü

| Ölçüt | Bugün | Hedef |
|---|---|---|
| Koşarken kamera hareketi | 460 ms'lik geçişler, 200 ms'de yeniden hedefleme | **Yok** |
| Boştaki kenar görseli | 1214 | **0** |
| Panel görünmezken tick başına iş | 1214 kenar × her 200 ms | **0** (A1) |
| Kuşbakışında kadraj dışında kalan graf | ~1/3 | **0** — her düğüm ekranda |
| UI thread blok süresi (graf modunda) | Faz 0'da ölçülecek | Taban çizgisinin altında |
| Bellek: canlı `Path` nesnesi | 1214 | onlarca |
| Süit | 1904 passed / 0 failed | Aynı veya daha iyi; test envanteri (yukarıda) uygulanmış hâlde |

> Not: "silinen üretim satırı 0" hedefi **kaldırıldı** — A4 gereği takip kamerası ve sis silinir. Panel
> kalır; geri dönüş git'tedir.

---

## Kapsam dışı

Paneli kaldırmak · yerleşim algoritmasını değiştirmek (Faz 3-c hariç, o da ayrı karar) · düğüm boyutu ·
minimap · `FOLLOW PAUSED` pilini `Button`'a çevirmek (v1 backlog) · süit hijyeni (ayrı iş) ·
graf düğümlerinin klavye erişilebilirliği (DevEx review'ın notu — ayrı iş, ARCHITECTURE §15/§20'de kayıtlı).
