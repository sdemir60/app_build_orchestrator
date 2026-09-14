# Odaklı performans geçişi — rapor

Tarih: 2026-09-14 · branch `perf/focused-pass` (main @ fa7cd39'dan) · kullanıcı kararı: "Odaklı analiz + uygula"

## 1. Yönetici özeti

Uygulamanın temeli performans açısından sağlıklı. Açılış 1,5 saniye; uygulama zorla kapatıldığında motor 84 ms'de
ölüyor ve geride süreç kalmıyor. Build sırasında satır başına iş O(1). 200 ms'lik periyodik tick zaten "değişmediyse
yazma" disiplinine uyuyor. ARCHITECTURE.md'deki bilinçli perf kararları (shared compilation kapalı, Reset yasağı,
anlık renk geçişleri, graceful stop vb.) yerinde duruyor ve bu turda hiçbirine dokunulmadı.

Gerçek sorun **boştaki uygulamanın** davranışındaydı. Uygulama günün çoğunu tray'de geçiriyor, ama tray'de bile
sürekli CPU yakıyordu. İki kaynak bulundu, ikisi de düzeltildi. Tray'de boştaki süreç maliyeti **~81 → ~5 milyon
CPU döngüsü/sn** indi (%94). Pencere açıkken boştaki maliyet ~205 → ~146 M döngü/sn oldu.

## 2. Ölçümler

Yöntem: `QueryProcessCycleTime` / `QueryThreadCycleTime` (CPU döngü sayacı). 20 sn'lik 3 pencere alındı, medyan
raporlandı. `TotalProcessorTime` 15,6 ms kuantumlu olduğundan bu işte güvenilmez çıktı. Görünür değerler koşudan
koşuya ±%20 oynuyor; maximize açılan pencerenin altındaki gerçek fare hover geçişleri tetikliyor. Tray değerleri
kararlı (pencereler arası sapma %1–3).

| Ölçüm | Değer |
|---|---|
| Debug build (sıcak, değişiklik yok) | 19 sn |
| Release build | 12 sn |
| Tam süit (`Category!=Acceptance`), taban | 2803 başarılı · 0 başarısız · 6 atlanan · 4 dk 37 sn |
| Tam süit, düzeltmelerden sonra | 2812 başarılı (taban + 9 yeni) · 0 başarısız · 6 atlanan · 4 dk 23 sn |
| Açılış (MainWindowHandle) | 1,5 sn; Supervisor pencereyle aynı anda |
| Kill cascade (App `Stop-Process`) | Supervisor 84 ms'de öldü, artık süreç yok |
| Supervisor boşta | ~%0 CPU, 36 MB |

Boştaki App, milyon döngü/sn (süreç / UI thread):

| Build | tray | görünür (süreç) |
|---|---|---|
| A — taban | 80,9 / 65,5 | 204,8 |
| C — yalnız imleç kapısı | 67,1 / 52,9 | 214,3 |
| E — yalnız batcher bekler | 71,6 / 63,0 | 150,9 |
| **D — ikisi birlikte (uygulanan)** | **4,6 / 4,5** | **145,6** |

Etki toplamsal değil. Tek başına her düzeltme tray'de yalnız %11–17 kazandırıyor; ikisi birlikte %94. Görünür
durumdaki kazancın neredeyse tamamı batcher'dan geliyor. Neden birlikte bu kadar büyük etki yaptıkları mekanizma
olarak kanıtlanmadı. D'de UI thread tray'de hâlâ saniyede ~450 kez uyanıyor, ama uyanma başına ~10 bin döngüyle çok
ucuz; büyük olasılıkla sistem girdi mesajları. Maliyeti gösteren ölçü uyanma sayısı değil, döngü sayısı.

## 3. Uygulanan bulgular

### 3.1 [önemli] İmleç saatleri pencere tray'deyken dönüyordu

- **Konum (değişen):**
  - `src/BuildOrchestrator.App/Console/ConsoleView.xaml.cs:151` (`IsVisibleChanged`) ve `:517` (`StartBlink` kapısı)
  - `src/BuildOrchestrator.App/Views/EventStreamView.xaml.cs:107` (`IsVisibleChanged`) ve `:410` (`StartCursorBlink` kapısı)
- **Sorun:** Konsol ve event-stream imleçlerinin iki sonsuz saati var: kırpma (`MotionTokens.CreateBlinkAnimation`,
  30 fps) ve renk turu (`CursorHop`, 20 fps). İkisi de görünürlüğe bağlı değildi. X pencereyi gizliyor ama görünümleri
  boşaltmıyor, `Unloaded` ateşlenmiyor. Doküman §14.5 "every infinite animation is gated on `IsVisible`" diyor; bu iki
  sahip kurala uymuyordu.
- **Neden kapı başlatıcıda:** Başlatıcılar olaylarla yeniden çağrılıyor. Konsolda zincir
  `VisualLinesChanged → RefreshPrompt → StartBlink` (mevcut kod, değişmez). Stream'de her olayda
  `UpdateActiveLine → StartCursorBlink` çağrılıyor (mevcut kod, değişmez). Yalnız `IsVisibleChanged`'de durduran
  deneme (B) tray'deki UI thread maliyetini düşürmedi, çünkü saat bir sonraki olayda geri kuruluyordu.
- **Test:** `tests/BuildOrchestrator.Tests/App/HiddenCursorClockTests.cs`, 6 test (her imleç için: gizlenince durur ·
  gizliyken olay gelse de başlamaz · geri gelince devam eder). Düzeltmeden önce 6/6 kırmızıydı.
- **Commit:** `bf110d0`

### 3.2 [önemli] Konsol pompası boşta saniyede 20 kez uyanıyordu

- **Konum (değişen):**
  - `src/BuildOrchestrator.App/Console/ConsoleBatcher.cs:80-86` (`Batching` fabrikası + `WaitForLineThenAsync`)
  - `src/BuildOrchestrator.App/App.xaml.cs:106` (üretim kurulumu)
- **Sorun:** Üretim tick'i koşulsuz `Task.Delay(50)` idi; pompa uygulama ömrü boyunca uyanıyordu. Tray'de boşta
  alınan dotnet-trace profilinde .NET zamanlayıcı thread'i (~160 ms) ve thread pool (~58 ms) 20 saniyede UI thread
  kadar CPU harcıyordu; `ConsoleBatcher.PumpAsync` stack'te açıkça görünüyordu.
- **Çözüm:** Üretim tick'i önce kanalda satır (ya da reseed sentinel'i / tamamlanma) bekliyor, sonra 50 ms'lik
  pencereyi açıyor. Tick sözleşmesi değişmedi (tick → boşalt → varsa tek flush). Pompa döngüsü ve reseed nesil
  koruması (D4) aynen duruyor; mevcut 10 test dokunulmadan geçiyor. Akış sürerken davranış aynı. Sessizlikten sonraki
  ilk satır en geç 50 ms'de basılıyor; bu eskiden de üst sınırdı.
- **Test:** `ConsoleBatcherTests`'e 3 test eklendi: boşta pencere açılmaz · açık pencerede gelen satırlar tek flush'ta
  birleşir · boştaki pompa `Complete` ile biter. Düzeltmeden önce 3/3 kırmızıydı. Testler dönen bir pompada askıda
  kalmak yerine düşecek şekilde yazıldı (`SpinGuard`). İlk tasarım eski davranışta süiti kilitlediği için değiştirildi.
- **Commit:** `38280d1`

### 3.3 Doküman

ARCHITECTURE.md'de değişenler:

- §12.1: batcher tanımı güncellendi (pencere satırla açılır).
- §13.5: "~50 ms flush" maddesine "saatle değil satırla açılır" eklendi.
- §14.5: "pencere tray'e inince de görünüm boşaltılmaz", "kapı başlatıcıdadır", "pompa boşta uyumaz" ve döngü
  ölçümü eklendi.

## 4. Doküman ile kod uyuşmuyor — karar senin

§14.5 "every infinite animation is gated on `IsVisible` as well as on its own state" diyor. Bu turdan sonra imleçler
kurala uyuyor, ama şu sahipler hâlâ uymuyor:

| Sahip | Konum (mevcut kod, değişmedi) | Ne zaman döner | Pencere tray'deyken |
|---|---|---|---|
| Satır nefesi | `Views/ProjectRow.xaml.cs:577` `ApplyBreathing` | proje derlenirken | döner |
| Şerit belirsiz süpürmesi | `Views/StickyRibbon.xaml.cs:367` `ApplyIndeterminate` | yalnız Syncing/Starting | döner |
| Graf beads / edge-flow | `Graph/GraphView.xaml.cs:1026`, `:1279` | building frontier / seçim | döner — graf kendi `Visibility`'sine bakar; headless gerekçesi `GraphView.xaml.cs:622-626` |

Hepsi yalnız bir işlem sürerken koşuyor; boştaki tray maliyetine katkıları yok. Build sırasında MSBuild'in yükü
yanında payları çok küçük. Bu yüzden "nokta atışı, gerçekten gerekli" ölçütüne göre uygulanmadı. İki seçenek var:
dokümandaki kuralı "boşta ve imleçler" kapsamına daraltmak ya da bu sahipleri de kurala uydurmak. Graf için ikincisi
headless test gerekçesi yüzünden daha invaziv.

## 5. Karar gerektiren (uygulanmadı)

- **Görünür boşta render döngüsü ~55–60 kare/sn.** İki imlecin kırpma ve renk turu fazları farklı olduğu için birleşik
  kare hızı 30'un üstüne çıkıyor. Tasarım gereği (yanıp sönen imleç); değiştirmek tasarım kararı.
- **Gizlemeden sonra render ~8 sn daha sürüp duruyor.** 2 sn çözünürlüklü zaman çizelgesiyle ölçüldü. Kalıcı değil;
  gizli anda tüm kök saatler `Filling` ve `CompositionTarget.Rendering` abonesi yok. Kaynağı belirlenmedi, düşük öncelik.

## 6. Kapalı kaldıraçlar — neden kapalı (yeniden önerilmesin)

| Kaldıraç | Neden kapalı | Kaynak |
|---|---|---|
| Shared compilation (~2,9× build) | `VBCSCompiler` job dışında → torn-DLL riski | §9.2, §4.5 |
| `nodeReuse` | ölçülmüş kazanç ~0 | §9.2 |
| `ObservableCollection` Reset ile toplu güncelleme | çalışan animasyonları yok eder | §14.5 |
| Her yere yumuşak renk geçişi | 177 proje tek tick'te 11 → 51 ms, 50 ms bütçesi kırılır | §14.5 |
| Supervisor stderr drain'ini kaldırmak | pipe dolar, planlama ve stop donar | §4.3 |
| Hard stop | yarım derlemeler çöpe gider | §4.5 |
| DLL/bin timestamp ile erken çıkış | değişmez ihlali | CLAUDE.md |
| eval-cache'ten uzunluk terimini atmak | mtime koruyan edit görünmez olur | §6.2 |
| Scheduler'da ince taneli kilit | birkaç yüz projede fayda yok | §8.2 |
| Graf culling | tasarım: graf her boyutta panele sığar | §20 |

## 7. Temiz çıkanlar (bulgu değil)

- 200 ms tick: `ConsoleHeader.SetLineCount` ve `EngineOverdueMessage` değer değişmediyse yazmıyor.
- `RunViewModel.OnProjectLog` satır başı O(1): kilit + append + kilitsiz `Post`.
- Tray build göstergesi: döngüsü sonsuz değil, `IsVisible` kapısı var, saati gerçekten sökülüyor.
- `BuildingSpinner` / `StatusGlyph` kapılı.
- VM boşta hiç `PropertyChanged` yaymıyor.
- Açılış yolu hafif; motor `Loaded`'da async başlıyor.
- Kozmetik, uygulanmadı: `NdjsonWriter` mesaj başına 2 write + flush yapıyor (`NdjsonFraming.cs:24-27`, mevcut kod,
  değişmez). Saniyede yüzlerce satırda birkaç ms'lik syscall; darboğaz değil.

## 8. Yapılacaklar (öncelik sırası)

1. §4 kararı: kuralı daraltmak mı, satır nefesi + şerit süpürmesini kurala uydurmak mı `[kisa]` — graf ayrıca `[orta]`.
2. Gizleme sonrası ~8 sn render kuyruğunun kaynağı `[kisa]` (düşük öncelik).
3. İsteğe bağlı: build sürerken, pencere tray'deyken App'in döngü maliyetini ölçmek (gerçek repo build'i gerektirir) `[orta]`.
