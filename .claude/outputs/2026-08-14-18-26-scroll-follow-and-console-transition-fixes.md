# Kaydırma takibi, stream alt satırı ve konsol geçişi — kök neden ve düzeltmeler

İkinci saha turunda bildirilen beş kusur. Hiçbirine tahminle dokunulmadı; her biri önce kırmızı bir testle
ya da bir ölçümle kanıtlandı.

## Bulgular ve kök nedenleri

### 1. Konsolda scroll'a müdahale edilemiyor, panel sürekli dibe zorluyor

**Kök neden — iki katmanlı.**

- AvalonEdit'in `TextView.ScrollOffsetChanged`'i bir **offset** olayıdır. Kullanıcı yukarıdayken eklenen
  içerik offset'i oynatmaz, yani hiç olay doğmaz; `ConsoleView`'ın elle izlediği `_lastExtentHeight` bu
  sürede bayatlar. Kullanıcı nihayet tekerleği çevirdiğinde olayın hesaplanan farkı `> 0` çıkıyor ve
  `BottomAnchorDecision` bunu "içerik büyüdü" sayıp takip kararını **atlıyordu** — yani kullanıcının
  kaydırması hiç değerlendirilmiyordu.
- `AppendBatch` ikinci bir auto-scroll yoluydu: salt `StickToBottom`'a bakıp her batch'te `ScrollToEnd()`
  çağırıyor, kullanıcının direksiyonu aldığını (`_steering`) hiç sormuyordu.

**Düzeltme.** Konsol offset olaylarını offset olarak bildirir (`extentHeightChange: 0`), elle extent izleme
kaldırıldı; dibe çekme yetkisi tek bir yerde toplandı: `BottomAnchorBehavior.ShouldFollow` (takip açık +
uçuşta atlama yok + direksiyon kullanıcıda değil). `AppendBatch` onu okur.

### 2. Event stream kendiliğinden takibi bırakıyor

**Kök neden.** Takip kararı salt geometriden veriliyordu: `extentHeightChange == 0` olan her olay "kullanıcı
kaydırdı" sayılıyordu. Oysa offset'i yerleşim, viewport değişimi ve programatik kaydırma da oynatır ve o an
offset henüz güncellenmemişken ölçülen uzaklık 48px eşiğini aşabiliyor.

**Düzeltme.** Takip yalnız **ham girdiyle** değişir. Yeni `UserScrollSignal` üç kanalı da bağlar: tekerlek,
kaydırma çubuğu (`ScrollBar.ScrollEvent` — programatik kaydırmada ateşlenmez) ve gezinme tuşları.
Kaydırma çubuğu ayrı bir kanaldır çünkü başlığı sürüklemek hiç tekerlek olayı üretmez.

Yan bulgu: takip artık kendiliğinden değişmediği için `⌄ latest` pill'i durum geçişlerinde tazelenemez
kalıyordu. Pill anlık uzaklığı izleyecek şekilde ayrıldı (zaten §2.5'in tarifi budur).

### 3. Beş saniyelik dibe dönüş hiç çalışmıyor

**Kök neden.** Bekleme sayacı her **scroll olayında** baştan kuruluyordu. Derleme sürerken o olayları üreten
kullanıcı değil akan içerikti — sayaç her satırla sıfırlanıyor ve beş saniye hiç dolmuyordu.

**Düzeltme.** Sayacı yalnız `NotifyUserScroll` (ham girdi) kurar. Sayaç scroll olaylarını değil, kullanıcının
elini ölçer.

### 4. Sync'ten sonra event stream'de imleç çıkmıyor (ikinci Sync'te çıkıyor)

**Kök neden.** Bekleme satırı yalnız **aktif proje değiştiğinde** (`ActiveLineGeneration` guard'ı)
kuruluyordu. Bir Sync hiçbir proje başlatmaz, yani kuşak hiç değişmez. İkinci Sync'te çıkmasının nedeni,
birinci Sync'in yazımı bitince guard'ın yan etkiyle sıfırlanmasıydı — düzelme tesadüftü.

**Düzeltme.** Satır koşulsuz durur, ilk kareden itibaren. Kuşak guard'ı yalnız **daktiloyu** kapatır, satırın
kendisini değil. `No events yet.` boş-durum metni kaldırıldı (kullanıcı kararı): panel boşken de bekleme
satırı konuşur, konsolla simetrik.

### 5. Event stream'de renkler kararsız

**Kök neden.** Tek yazı yüzeyinde **üç yazıcı** yarışıyordu ve yazım biter bitmez kuşak guard'ı sıfırlanıyor,
aynı amber `X building…` cümlesi her olaydan sonra harf harf baştan yazılıyordu. Hızlı bir koşuda alt satır
sürekli yarım amber metinle yarım renkli metin arasında gidip geliyordu.

**Düzeltme.** Alt satır iki bekleme hâline indirildi (derlenen proje / bomboş — ikisi de amber); yazım üçüncü
hâldir ve yalnız sürerken yüzeye sahiptir. Daktilo **yalnız yeni bilgiye** çalışır: kesilmiş bir cümleyi geri
koymak yeni bilgi değildir, o yüzden anında konur.

### 6. Konsol geçiş animasyonu prototiple uyuşmuyor

Otorite kullanıcının paylaştığı animasyon spec'i.

- **Prompt satırı tilt'e katılmamalı** (§1.3, §4). Önceki tur ikisini tek parça yapmıştı — spec'in tersi.
  Prompt ve `build in progress` satırı tilt kabının dışına alındı; imlecin konumu yine kaymıyor çünkü ölçü
  tilt kabının kendi dönüşümsüz koordinat uzayında alınıyor.
- **Menteşe ölçeği** 0,94 → **0,965** (§2.4'ün WPF eşlemesi).
- Yeni test canlı çıktının geçişi yeniden oynatmadığını da pinliyor (§2.1/§3).

**Ek bulgu (ölçüldü).** Spec §2.5'in sırası "içeriği değiştir → dibe pinle → animasyonu başlat"tı ama pin
çalışmıyordu: belge değiştikten hemen sonra editörün kaydırma geometrisi bayat olduğu için `ScrollToEnd`
eski geometriyle hesaplıyor ve panel **tepede** kalıyordu (3739px'lik bir belgede offset 3161 yerine 19).
Pin artık ölçümü zorluyor.

## Prototipten bilinçli sapmalar (dokümana işlendi)

| Sapma | Gerekçe |
|---|---|
| Beş saniye sonra dibe dönüş | Spec §5.1 "kullanıcının konumuna asla müdahale edilmez" der; kullanıcı bu dönüşü açıkça istedi. Dönüş olmadan panel takibi bırakıp geri gelmiyordu. |
| Stream'de satır-bazlı daktilo yerine tek yazı yüzeyi | Spec §6 en yeni satırı yerinde yazar. Kullanıcı alternatifi açıkça seçti (her satır kendi renginde alt satırda yazılsın, sonra yukarı bırakılsın). |
| İmleç her zaman amber | Spec bekleme satırını `text-faint` yapar; iki panelin aynı dili konuşması istendi. |
| Stream'de boş-durum metni yok | Spec §6/prototip `No events yet.` gösterir; konsol ilk açılışta zaten imleç gösteriyor. |

## Açık konu

**Spec §6 ile mevcut stream modeli çelişiyor.** Paylaşılan spec event stream'de prototipin satır-bazlı
daktilosunu (ve "daktilo bitene kadar prompt gizli" kuralını) korur; uygulamada kullanıcının daha sonraki
açık kararı olan tek yazı yüzeyi var. İkisi aynı anda doğru olamaz. Şu an kullanıcının kararı uygulanıyor ve
sapma dokümana yazıldı — hangisinin kalacağına birlikte karar verilecek.

## Doğrulama

- Tam süit yeşil: **2023 geçti, 1 atlandı** (atlanan önceden de vardı).
- Kırmızı kanıtları: takip kararı ve bekleme sayacı saf çekirdekte kırmızıya düşürüldü; "building satırı
  yeniden yazılmıyor" kuralı eski davranış geçici geri konarak kırmızı doğrulandı; Sync/imleç kusuru iki
  ayrı testle kırmızı gösterildi.
- Değişen kuralı pinleyen eski testler silinmedi, `[DEĞİŞEN KURAL]` notuyla yeni kurala göre yeniden yazıldı.
