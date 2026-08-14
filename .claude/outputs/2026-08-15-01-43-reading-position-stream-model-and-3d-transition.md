# Okuma konumu, stream modeli ve 3B geçiş — kök neden ve düzeltmeler

Üçüncü saha turu. Beş konu; dördü düzeltildi, biri (graf) kanıtlanana kadar açık bırakıldı.

## 1. "Scroll duruyor ama yazılar akmaya devam ediyor" — iki panelde de

**Kök neden.** Kaydırma konumu MUTLAK bir pikseldir. Her iki panel de içeriği **baştan** kırpar (konsol:
render dilimi 200 satır; stream: 150 satır). Kullanıcı yukarıda okurken tepeden satır silinince offset sabit
kalsa bile okunan metin yukarı kayar. Yani panel "kullanıcıya bırakılmış" ama içerik ayağının altından
çekiliyordu. Kullanıcının "ikisi de aynı davranıyor" demesi tam olarak bu ortak mekanizmayı işaret ediyor.

**Düzeltme — konsol:** kırpma yalnız takip açıkken yapılır. Kullanıcı elini çekince (bekleme dolar, takip
geri gelir) kırpma tek hamlede yetişir ve o an panel zaten dipte olduğu için görünmez.

**Düzeltme — event stream:** satırlar yine silinir ama silinen yükseklik kadar offset geri alınır — chunk
prepend telafisinin aynadaki hâli. (İki panel farklı çözüm kullanıyor çünkü konsolda kırpılan satır kalıcı
kaybolur, stream'de 150 satırlık dilimin en tepesi pratikte okunmaz.)

## 2. Event stream renkleri

**Bulgu — regresyon YOK.** `ok` satırı prototipin kendi eşlemesiyle **yeşil glyph + `text-secondary`**
metindir (BuildApp.jsx:635-638 → `StreamEventViewModel.BrushKeyFor`). Kullanıcının gördüğü "yeşil ikon,
beyazımsı yazı" tasarımın kendisidir.

**Gerçek kusur** başka yerdeydi: yazı yüzeyi modelinde satır tampona bırakıldığı anda 12px'lik **imleç
sütunu statü glyph'ine dönüşüyordu**. Metin hiç değişmese de göz bunu "renk değişti" diye okuyor, akış
kararsız görünüyordu.

**Düzeltme.** Prototipin §6 modeline dönüldü: daktilo **en yeni tampon satırına** aittir; satır kendi
yerinde, ilk karesinden itibaren kendi rengi ve kendi glyph'iyle yazılır. Alt satır artık bir
**göstergedir** — hiç yazmaz, hiçbir olayın rengini almaz, hep amberdir. Aynı anda tek satır yazar (yeni
satır öncekini anında tamamlar) — bu, "her satırın kendi zamanlayıcısı" döneminin çoklu-yazım kusurunun
düzeltmesidir ve korunur.

## 3. Proje logu / geri dönüş konumu

**Düzeltme (kullanıcı kararı, spec §5.1'den bilinçli sapma).** Proje logu **baştan** açılır ve takip
**kapalı** başlar — bir derleme logunda aranan ilk hatadır ve takip açık olsaydı gelen ilk canlı satır
okuyucuyu dibe fırlatırdı. `← Back` ile anlatı **sondan** okunur. Kullanıcı kendi eliyle dibe inerse takip
geri gelir (herhangi bir kullanıcı kaydırmasıyla aynı kural).

## 4. Panel geçiş animasyonu

**Kök neden.** Prototipin `perspective(900px) rotateX(7deg)`'i gerçek bir perspektif projeksiyonudur: üst
kenar geriye giderken **daralır**, alt kenar öne gelirken **genişler** — bir trapez. WPF'in 2D dönüşümleri
**afindir** ve trapez üretemezler; ölçek+kaydırma yaklaşımı jesti salt dikey bırakıyordu. Kullanıcının
"yatayda da bir hareket vardı, burada 2 boyutlu" demesi tam olarak bu eksik ipucudur.

**Düzeltme.** Spec §2.4'ün ikinci yolu: log bloğu bir `Viewport3D` içinde dokulu bir düzlem, `PerspectiveCamera`
900px uzakta, alt kenardan geçen eksen etrafında 7° → 0. Kameranın görüş açısı, dönüş sıfırken düzlemin
viewport'u tam doldurmasına göre türetilir — gerçek editöre devrin görünmez olmasını o sağlar.

Doku, geçişin başında alınan bir görüntüdür. Canlı bir `VisualBrush` kullanılamaz: fırça kaynağını olduğu
gibi çizer, yani gerçek bloğu gizlemek onu fırçada da gizlerdi.

## 5. Graf — AÇIK KONU (kanıtlanmadı, dokunulmadı)

Kullanıcı: "Sync'ten sonra gri olanlar Build'de de derleniyor."

**Bulunan.** İki tahmin AYNI kodu (`IncrementalRunBinder.Bind`) çağırır ama girdileri farklı olabilir:

| Girdi | Sync | Build koşusu |
|---|---|---|
| Kök / ağaç | `cmd.RootPath`, `inPlace: true` | `workspace.ScanRoot`, `workspace.InPlace` |
| Bağımlı modu | `DependentMode.Safe` | `cmd.DependentMode` — App her zaman `Safe` gönderir |
| Döngü kapısı | `buildCycles: false` | `cmd.Mode == RunMode.Cycles` |
| State store | `Load()` | `Load()` |

Yani **tek gerçek fark worktree'dir**: build ayrı bir worktree'de koşuyorsa imza, commit'lenmemiş yerel
değişiklikleri içermez ve kök yol farklıdır — Sync'in tahmini çalışma kopyasını, koşu ise worktree'yi anlatır.
Graf koşunun kendi önizlemesiyle (`OnBuildPreview` → `row.WillBuild`) tazelendiği için Build anında doğruyu
gösterir; tutarsızlık Sync'in tahminindedir.

**Ayırt edici soru:** Build sırasında worktree kullanılıyor mu (hedef branch aktif branch'ten farklı mı /
worktree anahtarı açık mı)? Evetse düzeltme "Sync, build'in koşacağı workspace'i tahmin etsin" olur — bu bir
ürün kararıdır ve motor semantiğine dokunur, o yüzden onaysız yapılmadı. Hayırsa fark zamansaldır (Sync ile
Build arasında dosya değişmiş) ve tekrarlanabilir bir repro gerekir.

## Doğrulama

- Süit: **2024 geçti, 1 atlandı, 1 zamanlama kırılganı**. Kırılan test koşudan koşuya değişiyor
  (`UiResponsivenessBudgetTests` / `PopoverTests`), ikisi de İZOLE koşuda geçiyor ve ikisi de bu çalışmanın
  dokunmadığı yolları ölçüyor: bütçe testinin aştığı adımlar `topoloji layout` (izole 91ms → yük altında
  185ms) ve `filtre KAPA` (55ms → 161ms), yani proje listesi yerleşimi. `PopoverTests` önceki turlarda da
  kırılgandı. Yeni tilt testleri gerçek bitmap render'ı yaptığı için panel ölçüsü 800×400'den 200×100'e
  indirildi.
- Değişen kuralı pinleyen eski testler silinmedi, `[DEĞİŞEN KURAL]` / `[DEĞİŞEN ÖN-KOŞUL]` notuyla yeniden
  yazıldı (`ConsoleTiltInTests`, `EventStreamTypingTests`, `ConsoleViewTests` proje-modu üçlüsü,
  `EventStreamTests`, `ReducedMotionCoverageTests`).
