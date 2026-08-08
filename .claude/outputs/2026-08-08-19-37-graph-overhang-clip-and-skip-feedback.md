# Quiet graph — taşan görsellerin kırpılması, overlay kelepçesi ve atlanan projenin geri bildirimi

Kullanıcının ikinci gözle doğrulama turunda bildirdiği beş madde. İkisi ölçülmüş kök nedene, üçü karara bağlandı.

## 1. "Tıklayınca köşelerde sarı noktalar, amber çerçeve yok"

**Ölçüm (kesin):** `FrameworkElement.GetLayoutClip` doğrudan sorgulandı.

| Öğe | İstenen ölçü | Layout clip | Sonuç |
|---|---|---|---|
| `SelectionRing` | 30 px | `(3, 3, 24, 24)` | düz kenarların tamamı kırpık |
| `Beads` yörüngesi | 29.6 px | `(2.8, 2.8, 24, 24)` | aynı |

WPF bir çocuğu **arrange slot'una** kırpar; slot ise düğüm kadar (24 px) olan hücrenin kendisiydi. Halkanın
köşe yayı (merkez 8,8 · yarıçap 6) 45°'de (3.76, 3.76) noktasına kadar içeri girdiği için **yalnız o dört yay**
kırpma dikdörtgeninin içinde kalıyordu — kullanıcının gördüğü "sarı noktalar" halkanın ta kendisiydi.

Prototip bu sorunu hiç yaşamaz: düğüm kabı `width:0; height:0` ve çocuklar mutlak konumla dışarı taşar
(BuildApp.jsx:437).

**Çözüm:** hücre, taşan her şeye yetecek kadar büyütüldü (`GraphView.CellOverhang`); halka gövdenin içinden
çıkarılıp hücrenin çocuğu yapıldı (gövde tıklama alanıdır, büyümemeli) ve hover ölçeğini gövdeyle **aynı
transform nesnesi** üzerinden paylaşıyor.

**Yan bulgu:** halkanın ölçüsü de yanlıştı. CSS `outline: 2px solid; outline-offset: 2` iç kenarı kareden 2px,
dış kenarı 4px dışarı koyar. WPF kalemi Rectangle'ın **içine** çizer, dolayısıyla pay `offset + tam kalem`
olmalıydı; `SelectionRingInset` 3 → 4.

**Kanıt:** yapısal iddiaların yanına **piksel** testi kondu (`RenderTargetBitmap` + bant taraması). Yapısal
iddia tek başına yetmezdi — eski kod da "halka 30px" diyordu, o 30px ekrana ulaşmıyordu.

## 2. "Hover tooltip'i saçma sapan yerlerde"

**Kök neden:** kelepçe kutunun TAMAMINA uygulanıyordu. 500px'lik bir panelde 30 karakterlik bir proje adı
~215px'lik kutu demektir; kenardaki her düğümde tooltip düğümden onlarca piksel uzağa kayıyordu. Tıklamadan
sonra düzgün görünmesinin sebebi de buydu: odak kamerası düğümü panelin ortasına getiriyor ve kelepçe hiç
devreye girmiyordu.

**Çözüm:** prototipin kuralı (BuildApp.jsx:470) — kelepçe **ankraja**, kutu ankraja **ortalı**. Kelepçe payı
ayrı bir sayı değil, grafın kendi iç payı (`QuietGraphLayout.ContentInset`), böylece odak kipinde etiket köşeye
yapışmıyor. Bedeli: panel kenarındaki çok uzun bir ad kırpılabilir — ortalı durmak buna tercih edildi.

## 3. "Beads animasyonu yok"

**Ölçüm:** yörünge kuruluyor, doğru geometride ve GERÇEKTEN dönüyor — saat `SeekAlignedToLastTick` ile
sürüldüğünde yarım turda `StrokeDashOffset` tam yarım çevreye gidiyor. Gerçek bir koşu karesi
`RenderTargetBitmap` ile boyandı ve noktalar görünür durumda.

Yine de bu turdan önce bunu **kanıtlayan bir test yoktu**: mevcut test saatin *kurulduğunu* pinliyordu,
döndüğünü değil — bağlanmamış ya da hiç başlamamış bir saat de o testi geçerdi. Eksik kapatıldı
(`The_shared_clock_actually_carries_the_dots_around_the_orbit`).

Kalan fark yalnız görünürlük: gerçek panelde düğüm 11–13px, noktalar 1px `--amber-text` ve hız 15px/s. Bunlar
§2.3'ün sayılarıdır; değiştirmek tasarım kararıdır, kullanıcı isterse ayrı ele alınır.

## 4. Kenar payı 28 → 36

Ferahlık isteği. Hesap alanı daraldığı için aynı panelde pitch bir tık küçülür (640×360'ta 6×40 grafta
19.5 → 18.5). Bilinçli takas.

## 5. Atlanan proje "işlem görmüş" görünsün

Eski kural hold-fade'i **building'den çıkışa** bağlıyordu; atlanan proje hiç building olmadığı için 0.13'ten
0.2'ye sessizce kayıyordu.

**Çözüm:** kural artık statünün kendisine bakar (`GraphNodeOpacity.IsSettled`) — sonuç statüsüne **giriş**
parlak beklemeyi doğurur. Ayrıca aynı amber yörünge atlanan düğümde tek atımlık oynar (girer, düğüm parlak
dururken döner, düğüm soluklaşırken söner).

**Yapılmayan:** kareyi bir an amber boyamak. Statüyü yanlış söylerdi ve renk geçişinin ölçülmüş bedeli zaten
`ApplyNodeStatus`'te yazılı. Amber olan, işin kendisini anlatan yörüngedir.

## Süit

1889 geçti / 2 atlandı / 0 başarısız (`Category!=Acceptance`).
