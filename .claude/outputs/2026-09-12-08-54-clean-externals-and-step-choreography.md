# Clean: Harici Projeler ve Adım Koreografisi — Uygulama Sonucu

> Bu dosya bir **kayıttır**, prompt değil. `feat/clean-button-engine-v2` üzerindeki üçüncü turdur.
> Önceki turlar: motorun kendisi (`2026-09-10-19-39-...-v2-results.md`) ve Clean sonrası tazeleme
> (`2026-09-12-06-43-clean-post-run-refresh-and-busy-buttons.md`).

| | |
|---|---|
| **Branch** | `feat/clean-button-engine-v2` (yalnız LOCAL) |
| **İsteyen** | kullanıcı, üç tarif: (a) harici projeler temizlenmiyor, (b) liste ve düğümler tıklama anında gitsin, (c) geçişler üst üste biniyor, spinner "git gel" yapıyor |
| **Durum** | Tamamlandı, merge EDİLMEDİ |

---

## Ne değişti

| Task | Commit | İçerik |
|---|---|---|
| E | `ace91a9` | Harici projeler de temizleniyor; silme izni çözülen proje kümesine bağlandı |
| F | `2a0bbd4` | Liste, graf, döngü haritası ve will-build sayacı tıklama anında boşalıyor |
| G | `a2ff268` | Adım en az 440 ms oynuyor, 200 ms boşluk, sonra Sync; `Services/StepHold` |
| H | (doküman commit'i) | ARCHITECTURE §5.2/§13.2/§16/§22 + README |

## Kararlar

**K-16. Harici projeler sıradan projelerdir, Clean de onları temizler.** Komut artık kartları taşıyor ve servis
ana taramayı `ExternalWorkspaceResolver` ile birleştiriyor — Sync'in ve koşu planlayıcısının kullandığı AYNI
çözümleyici. Defter her kök için ayrı süpürülüyor, çünkü anahtar tam csproj yoludur ve ana kökün öneki harici
kökü kapsamaz. Çözülemeyen kart uyarı alır, akış durmaz; metni Sync'inkinden ayrıdır çünkü sonuç farklıdır
(derlenmez değil, temizlenmez).

**K-17. Silme izni "kayıtlı kök altında" DEĞİL, "çözülen projenin klasörünün hemen altında".** Eski kural iki
yönde de yanlış cevap veriyordu: kök altındaki ama hiçbir projeye ait olmayan bir `bin`'i siliyor, bir harici
`.sln`'in kendi klasörü dışında listelediği projenin `bin`'ini ise silmiyordu. Aynı açık defterde de vardı —
kayıt kalırsa bir sonraki Build projeyi "güncel" sayıp atlar ve silinmiş çıktıların üstüne yeşil bir koşu yazar;
bu tam olarak tasarımın engellemek için kurulduğu kusurdur. O projelerin kayıtları artık kendi klasörleriyle de
süpürülüyor.

**K-18. Liste ve graf TIKLAMA ANINDA boşalır (kullanıcı kararı, K-12'yi tersine çevirir).** Konsol ve stream
zaten tıklamada temizleniyordu; plan yüzeyinin başka bir anda düşmesi tek işlemi iki sarsıntı gibi gösteriyordu.
Boşaltma satırları hollow'a almakla kalmıyor, koleksiyonu ve topolojiyi de boşaltıyor (kullanıcı iki kez
"boşalsın / gitsin" dedi). Faz `Boot`'a alınıyor: davet kararı boş listeyi `Idle` fazında "klasörde proje yok"
diye okur ve bu yanlış olurdu — branch değişimi aynı gerekçeyle aynı şeyi yapıyor. `TopologyChanged` açıkça
ateşleniyor, yoksa liste boşalırken düğümler ekranda kalırdı.

**Bedeli kabul edildi:** gönderim düşerse ya da komut reddedilirse liste boş kalır ve geri getirmek kullanıcının
Sync'ine kalır. K-12'nin gerekçesi buydu; kullanıcı anındalığı seçti. Reddetme pratikte erişilemez (App kapısı
tıklamada kapanıyor), gönderim düşmesi ise zaten kendi başına bir motor hatası durumudur.

**K-19. Adım her zaman aynı süre oynar.** 440 ms (tasarımın nötr vuruşu) boyunca spinner döner ve kapı
bırakılmaz, sonra adım biter, 200 ms (kısa vuruş) boşluk, en son Sync. Süreler `MarkingChoreography`'den gelir,
yeni sayı uydurulmadı. Gerekçe projenin kendi ölçümüdür: açılış koreografisi motorun penceresiyle örtüştürülünce
aynı tıklama bazen animasyonlu bazen anında olmuş ve karar "bir koreografi ya her zaman oynar ya hiç" diye kayda
geçmişti. Yavaş bir Clean'e ek bekleme yapılmaz.

**K-20. Zamanı kabuk sayar.** `RunViewModel.OperationHold` bir delegedir (mevcut `OperationChoreography` ile aynı
bölüşüm); üretim karşılığı `Services/StepHold`, `DispatcherTimer` ile bekler. İki gerekçe: bu bir UI temposudur
ve UI thread'inde sayılmalıdır (StepPlayer'ın gerekçesi), ayrıca üretimde `Task.Delay` yasaktır ve D8 guard'ı
kaynak ağacını tarar. Azaltılmış harekette hiç beklenmez ve sinyal canlı okunur. Üst üste gelen bekletme
öncekini asılı bırakmaz — asılı bir `await` düğmeyi kalıcı kilitlerdi.

## Test notları

- Harici testlerin ilk kırmızısı derleme hatasıydı; kusuru yakaladıklarını ayrıca mutasyonla (kartları yok
  sayan bir `Resolve` çağrısı) gösterdim: ikisi kırmızı verdi.
- `A_folder_that_is_neither_the_root_nor_a_registered_external_is_left_alone` testi o mutasyonda yeşil kaldı —
  çünkü aslında kapıyı değil TARAMA KAPSAMINI ölçüyordu. Adı ve doc'u gerçeğe uyduruldu
  (`A_root_that_was_not_registered_as_an_external_card_is_never_scanned`).
- İki test davranış değiştiği için yeniden yazıldı (K-18): boşaltma artık tıklamada, gönderim düşse de geçerli.
  Eski iddia ve değişme gerekçesi testlerin doc'unda.
- `StepHold`'un yalnız ZAMANSIZ kuralları test edildi (azaltılmış hareket, pozitif olmayan süre, canlı sinyal,
  asılı await yokluğu). Gerçek tick beklenmez — D8 ihlali olurdu; o, şeridin elapsed timer'ı ile aynı
  kategoride kabuk kablajıdır.

## Açık kalan

Şeritteki pill Clean sırasında `DEEP CLEAN` yazar ama amber yanmaz (canlı ölçütü koşu/Sync fazına bağlı, K-7
Clean için yeni faz açmayı yasaklar). Bakım kutusundaki düğme artık canlı durumu gösterdiği için bu ayrı bir
iş olarak duruyor.
