# Graf paneli v2 — "Yakın Odak" tasarımı

> **Durum:** taslak, kullanıcı onayı bekliyor. Onaylanana kadar koda dokunulmaz.
> **Öncülü:** v1 (`merge: graf sinema modu`, `main` @ `c6535eb`) — kamera altyapısı, jestler, manuel mod
> ve takip dönüşü oradan gelir ve **korunur**. Bu belge onun üstüne **çıkarıcı** bir revizyondur.

---

## 1. Problem

177 proje / 1214 bağımlılıkta v1 üç şeyi birden kaybediyor.

**Kasıyor.** Panel her an ~1200 kenar görselini taşıyor. Kamera hareket ederken tüm dünya bir
`RenderTransform` altında olduğu için WPF her karede altındaki her şeyi yeniden rasterize ediyor.
Kuşbakışında grafın büyük kısmı ekranda olduğundan **culling neredeyse hiçbir şey elemiyor**. Ölçülen
profil de bunu söylüyor: maliyetin %64–72'si görsel ağaç, layout aritmetiği %0.03. Yani darboğaz hesap
değil, **boyama**.

**Sis yanlış kaldıraçtı.** 1214 kenarı %16 opaklığa indirmek onları ucuzlatmıyor — WPF için soluk path ile
parlak path aynı maliyet. Görüntüde de karışıklık duruyor, yalnız soluklaşıyor.

**Zoom ölü.** Takip ölçeği, derlenen düğümlerin **tamamının sınırlayıcı kutusunu** çerçeveliyor. Paralel
cephe genişleyince o kutu tuvalin tamamı oluyor, ölçek tabanına (`FollowMinScale` 0.85) yapışıyor ve orada
kalıyor. Kullanıcının gördüğü "yakınlaşma hissi yok" tam olarak bu.

**İsimler okunmuyor.** 177 düğümü aynı anda göstermeye çalıştığımız sürece hiçbir düğüm ismini taşıyacak
yere sahip olamaz.

## 2. Yeniden çerçeveleme

> Panel "baktığın bir bağımlılık grafı" olmaktan çıkar, **"bağımlılığa göre yerleşmiş canlı bir derleme
> görüntüsü"** olur. Yapı **talep üzerine** gelir; varsayılan görüntü ilerlemeyi anlatır.

Bu tek cümle aşağıdaki her kuralı üretiyor.

## 3. Kurallar

### R1 — Kenarın varlık kuralı (çekirdek)

Bir kenar görseli yalnız **aktif kümeye** bağlı olarak var olur. Aktif küme moda göre değişir:

| Mod | Aktif küme | Kenar kuralı |
|---|---|---|
| **Koşu** | `Status == Building` olan düğümler | Bir kenar, **uçlarından biri** aktif kümedeyse vardır |
| **İnceleme** (seçim var) | `{seçili} ∪ bağımlılıkları` | Bir kenar, **her iki ucu da** aktif kümedeyse vardır |
| **Boşta** | ∅ | Hiç kenar yok |

İki kural bilerek farklı: koşuda "derlenen neyle besleniyor"u görmek istiyoruz (tek uç yeter), incelemede
"bu projenin alt grafı"nı görmek istiyoruz (her iki uç) — aksi halde bir bağımlılığın kendi tüm kenarları
da içeri sızardı.

**Sonuç:** bir proje bitince (Succeeded ya da Failed) aktif kümeden düşer ve **kenarları kaybolur**;
düğüm rengiyle kalır. Koşu ilerledikçe ekranda biriken şey bir **renk haritası** olur — yeşil/kırmızı
düğümler ve aralarında hiçbir çizgi. Çizgi yalnız "şu an akan" yerde ve tıklanan yerde.

Canlı kenar sayısı: **1214 → tipik olarak 10–30.**

### R2 — Düğüm belirginliği

| Mod | Tam parlaklık | Hafif sönük | Sönük |
|---|---|---|---|
| **Koşu** | Building | Biten (Succeeded/Failed) — **rengini korur** | Henüz başlamamış |
| **İnceleme** | Seçili + bağımlılıkları | — | Diğer hepsi |
| **Boşta** | Hepsi — **tam parlaklık** | — | — |

Koşuda üç kademe olmasının sebebi kullanıcının sözü: *"derlenirken hafif soluk olabilir, derlenenler öne
çıksın diye"* — ama biten düğüm **görünür kalmalı**, çünkü sonucun kendisi o.

Koşu bitince sönüklük **kalkar**: sonuç haritası tam parlaklıkta durur, yeşil ve kırmızı eşit ağırlıkta.
Kararlaştırıldı — sonucun tamamı bir bakışta okunabilmeli.

İki yeni sönüklük kademesi gerekiyor; bugünkü tek `DimmedNodeOpacity` (0.25) yetmiyor. Değerler tasarım
token'ı olarak tek yerde tanımlanır ve **gözle ayarlanır** — bu belge sayı pinlemez.

### R3 — Kamera: kutu çerçeveleme yok

| Mod | Hedef | Ölçek |
|---|---|---|
| **Koşu** | Building düğümlerin **ağırlık merkezi** | **Sabit** `FocusScale` — pazarlık yok |
| **İnceleme** | `{seçili} ∪ bağımlılıkları`nın sınırlayıcı kutusu | Kutunun **tamamını** sığdıran fit |
| **Boşta** | Grafın merkezi | `FitScale` (bugünkü kuşbakışı) |

Kutu çerçeveleme **yalnız incelemede** kalıyor, çünkü kullanıcının istediği tam olarak o: *"tıklayınca tümü
gözükmeli, tümünü tek parça düşündüğünde ekrana ortalamalı; diğerleri kadraj dışında olabilir."* Yani
incelemede kadraj ne kadar uzaklaşmak gerekirse gereksin **aktif kümenin tamamını içerir**; kümeye girmeyen
projelerin kadraj dışında kalması normaldir. Alt taban yalnız manuel bandın tabanıdır (`ManualMinScale`).

Koşuda kutu **yok**. Ölçek sabittir; yalnız merkez kayar. Bu bilinçli bir karardır: v1'in ölü zoom'u tam
olarak "derlenenlerin hepsini sığdır" demekten doğuyordu — paralel cephe genişleyince kutu tuvalin tamamı
oluyor ve kamera kalıcı olarak uzakta takılıyordu. Sabit ölçek bu sınıfı tümden ortadan kaldırır ve zoom
titremesi de olmaz. Cephe ekrandan geniş olduğunda ortası görünür, taşan kısım kadraj dışında kalır.

`FrontierScale` / `FollowMinScale` / `FollowMaxScale` / `ShouldRescale` / `_previousScale` latch'i **kalkar**;
yerini tek bir `FocusScale` alır. Odak Zeno guard'ı (8 px) **kalır** — 200 ms'lik statü tick'i 460 ms'lik
geçişi sürekli yeniden başlatmasın diye.

`FocusScale` için başlangıç değeri **1.6** önerilir (26 px düğüm ekranda 42 px, 600 px viewport ~11 düğüm
hücresi gösterir) — ama bu **gözle ayarlanacak** bir sayıdır, ölçümle değil.

### R4 — İsimler

Bugünkü kural (katman başına örtüşme + odak muafiyeti) **korunur**, tek değişiklikle: **odak muafiyeti
aktif kümenin tamamına genişler.** Yani incelemede seçili düğüm de, bağımlılıkları da adını taşır —
incelediğin şey zaten onlar.

Örtüşme kuralının ölçek-değişmez olduğu (etiketler kameranın altında yaşadığı için) v1'de kanıtlandı ve
korunur: kalabalık bir katman yakınlaşmayla isim kazanmaz. Ama artık **önemli olan düğüm her zaman muaf**,
dolayısıyla kullanıcının şikâyeti ("isimler okunsun") karşılanır.

> **Açık:** komşuların da adlanması istenirse çözüm zoom değil **etiketleri şaşırtmaktır** (alt alta iki
> sıra), çünkü örtüşme ölçekten bağımsız. Bu bir sonraki tura bırakılır; muafiyet yetmezse yapılır.

### R5 — İsim kırpması

Bugün sağdan kırpıyoruz: `UI.UsedCars.R…`. Ama bu isimlerde **ayırt edici kısım sonda** —
`UI.UsedCars.Reports` ile `UI.UsedCars.Rules` bu kırpmayla ayırt edilemiyor.

Yeni kural: **son iki noktalı segment** gösterilir (`UsedCars.Reports`), sığmazsa ortadan üç nokta.
Tam ad tooltip'te kalır. Ölçekten ve boyuttan bağımsız, ucuz bir okunurluk kazancı.

> **Geçici.** Kullanıcı bu kuralı sonra kendisi düzenleyecek; şimdilik böyle ilerlenir. Dolayısıyla kırpma
> kuralı **tek bir yerde** durmalı ve değiştirilmesi tek satırlık bir iş olmalı.

### R6 — Modların önceliği ve geçişler

Üç mod aynı anda talep edilebilir; sıralama şudur:

1. **Manuel** her şeyi bastırır (kullanıcı kamerayı elinde tutuyor). 4 sn sonra kendiliğinden düşer;
   açık bir seçim onu **anında** düşürür. *(v1'den gelir, değişmez.)*
2. **İnceleme** koşuyu bastırır. Koşu sürerken bir projeye tıklarsan inceleme kazanır ve **koşu arkada
   devam eder**; seçimi kaldırınca kamera koşuya döner.
3. **Koşu** boştayı bastırır.
4. **Boşta** — hiçbiri yoksa.

Koşu bittiğinde kamera **hemen** kuşbakışına dönmez: son derlenen düğüm kısa bir süre ekranda kalır,
sonra geçiş başlar. Süre `SettleDelayMs` sabitiyle tanımlanır; **başlangıç değeri 2 sn** (kullanıcı
"birkaç saniye, sen karar ver" dedi). Gözle ayarlanacak.

### R7 — Düğüm boyutu: şimdilik DOKUNULMAZ

`NodeSize` (26 px) tüm yerleşimi (`NodeCellWidth`, aralık, kelepçe, etiket ölçümü) besliyor; değiştirmek
her şeyi etkiler. **Önce yakın kamerayla bakılır** — 1.6×'te düğüm ekranda 42 px görünür. Yetmezse ayrı ve
bilinçli bir adım olarak büyütülür.

## 4. Kaldırılanlar

- **Kenar sisi** (`FogFinishedOpacity`, `Resolve`'un `fogged` parametresi, `_cullEnabled`'a bağlı sis
  kablajı) — sislenecek kenar kalmıyor.
- **Kutu çerçeveleyen takip ölçeği** (`FrontierScale`, `FollowMinScale`, `FollowMaxScale`,
  `FrontierMarginX/Y`, `ShouldRescale`, `ScaleRetargetThreshold`, `_previousScale`).

Bunları pinleyen testler CLAUDE.md gereği **silinmez**: yeni kuralı pinleyecek şekilde yeniden yazılır ve
doc'larına eski iddia + değişme gerekçesi (bu belgedeki ölçüm/gerekçe) işlenir.

## 5. Korunanlar (v1'den gelen temel)

Kamera transform'u ve `ClampPan` · `Pan` / `ZoomAt` · manuel kamera modu · sürükleme + wheel jestleri ·
el imleci · 4 sn takip dönüşü · `FOLLOW PAUSED` pili · culling (yakın kamerada **çok daha etkili** olacak) ·
etiket metrikleri ve tooltip yedeği · `EnsureLabel`'ın tek etiket kurulum yolu olması ·
`ExitManualCamera`'nın tek manuel-çıkış temizliği olması.

## 6. Ana teknik risk: kenar görsellerinin devir hızı

Kenarlar artık her statü tick'inde doğup ölecek. Üç seçenek var ve karar **ölçümle** verilir:

| Strateji | Artı | Eksi |
|---|---|---|
| **Kur / yok et** | Canlı sayı sınırlı kalır, bellek düz | Tick başına ~10–30 `Path` inşası; inşa maliyeti ölçülen darboğazın kendisi |
| **Kur / gizle** (etiket deseni) | İnşa bir kez; `Collapsed` boyanmaz, measure/arrange'e girmez | Uzun koşuda her kenar bir kez doğar ⇒ sonunda 1214 gizli görsel (bellek; boyama değil) |
| **Havuz** | İnşa da bellek de sınırlı; geometri yeniden yönlendirilir | En karmaşığı |

**Başlangıç kararı:** en basit olanla (kur/yok et) başla, **ölç**; tick başına maliyet bütçeyi aşarsa
havuza geç. "Daha az çiziyoruz o yüzden hızlıdır" denmez — sayı gösterilir.

## 7. Başarı ölçütü (ölçülecek, varsayılmayacak)

| Ölçüt | Bugün | Hedef |
|---|---|---|
| Boştaki kenar görseli sayısı | 1214 | **0** |
| Koşu sırasında canlı kenar görseli | 1214 | < 40 |
| Kamera geçişi sırasında kare maliyeti | ölçülmedi | ölçülecek, taban çizgisi kurulacak |
| 500/1000 düğüm açılış süresi | 37 / 75 ms | artmamalı |

`GraphRealizationPerfTests` bu sayıları zaten basıyor; kamera geçişi için yeni bir ölçüm gerekiyor.
**Sayılar düşmezse tasarım yanlıştır ve geri dönülür.**

## 8. Kararlar (kullanıcıyla netleştirildi)

| Soru | Karar |
|---|---|
| Koşuda kamera hedefi | Derlenenlerin **ağırlık merkezi**, "tek parça" olarak. Sabit ölçek; yalnız merkez kayar. |
| Ağırlık merkezi boşluğa düşerse | Kabul. İki uzak düğüm paralel derleniyorsa aralarına bakılır — "tek parça" kararının doğal sonucu. **Gözle bakılacak**; rahatsız ederse "en son başlayanı ortala" alternatifi elde tutulur. |
| Koşu bitince dönüş | Hemen değil; `SettleDelayMs` (**başlangıç 2 sn**) sonra. |
| İncelemede kadraj | Aktif kümenin **tamamı** kadraja girer, ne kadar uzaklaşmak gerekirse gereksin. Kümeye girmeyenler kadraj dışında kalabilir. |
| Sonuç haritası | **Tam parlaklık**, yeşil ve kırmızı eşit ağırlıkta. |
| Tıklama kapsamı | Yalnız **bağımlılıklar** (ona bağımlı olanlar değil). |
| İsim kırpması | Son iki segment — **geçici**, kullanıcı sonra düzenleyecek. |

### Gözle ayarlanacak sayılar (bu belge pinlemez)

`FocusScale` (öneri 1.6) · `SettleDelayMs` (öneri 2 sn) · koşudaki iki sönüklük kademesi.
Bunlar ölçümle değil bakışla kararlaştırılır; uygulamada **tek kaynakta** durur ki değiştirmek tek satır olsun.

## 9. Kapsam dışı

Düğüm boyutunun büyütülmesi (R6) · etiket şaşırtma (R4 notu) · minimap · grafın yerleşim algoritmasının
değişmesi · `FOLLOW PAUSED` pilinin `Button`'a çevrilmesi (v1 backlog'unda) · süit hijyeni (ayrı iş).
