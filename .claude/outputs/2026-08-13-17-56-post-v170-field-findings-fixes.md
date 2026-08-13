# v1.7.0 sonrası ilk saha turu — bulgu düzeltmeleri (TDD dökümü)

Tasarım v1.7.0 dört fazda uygulandıktan sonra gerçek OSYS reposunda yapılan ilk elle testte 11 bulgu çıktı.
Üç paralel kod incelemesiyle kök nedenler bulundu. Bu döküm görev sırasını ve her görevin kırmızı testini
sabitler.

İki madde kusur değil, cevap oldu:

- **SCC içi tek tek derleme** doğruluk gereğidir — bir döngü üyesi kardeşinin DLL'ini okurken o kardeş aynı
  dosyayı yazıyor olurdu (`RunCoordinator.cs:1143`). Bağımsız döngü grupları ve upstream prerequisite'ler
  zaten paralel koşar. Yavaşlığın asıl kaynağı serilik değil, grup başına en az iki tam tur ve invoke başına
  soğuk derleyici (`UseSharedCompilation=false`, `nodeReuse:false`).
- **Döngü atlamaları koşuyu kırmızıya çevirmez.** Atlamalar `Skipped` kovasına girer, `Failed`'a değil
  (`RunCounters.cs:39-42`); progress rengi yalnız `Failed > 0` ile kırmızıya döner
  (`RibbonText.ProgressStatus`). Progress paydası `willBuild` olduğundan çubuk %100'e de ulaşır.

Kullanıcı kararları: konsol imleci **amber**; motorda yalnız doğruluğu bozmayan en güvenli değişiklik;
Resolve'a tekrar basmak çözümü baştan başlatmalı. Stop butonu bulgusu düştü — uygulamada doğru çalışıyor.

---

## Bloklayıcı

### T1 — Konsol fontu hiç yüklenmiyor

`Fonts/GeistMonoConsole.CompositeFont` kök elemanı `.../winfx/2006/xaml/presentation` namespace'iyle
yazılmış; WPF CompositeFont'u yalnız `.../winfx/2006/xaml/composite-font` ile parse eder. Dosya kök elemanda
`FileFormatException` alıp tamamen reddediliyor, `Geist Mono Console` ailesi hiç çözülmüyor ve konsol
**Segoe UI Light**'a düşüyor — orantılı bir UI fontu. "İç içe geçmiş, ince, okunmayan yazı" bu.

Hata It-0'dan beri duruyor: T56 spike'ının "LineSpacing AvalonEdit'te tutmuyor" teşhisi aslında bu parse
hatasıydı. Light ağırlığa geçiş hatayı yaratmadı, görünür yaptı (fallback Regular'dan Light'a düştü).

**Kırmızı test:** aile çözülüyor ve monospace — `FormattedText` ile `iiiiiiiiii` ve `MMMMMMMMMM`
genişlikleri eşit olmalı. Bugün değil.

### T2 — Şerit building chip'i ilk üyede donuyor

`RunCounters` bir `readonly record struct`. Döngü grubunda sıra A'dan B'ye geçince sayaç demeti byte-byte
aynı kalır (building 1, toplam sabit), `[ObservableProperty]` setter'ı eşitlik görüp `PropertyChanged`
yaymaz, `StickyRibbon.RebuildChipsIfChanged` hiç çağrılmaz. Grup içi `CycleWaiting` takibi doğru çalışıyor;
kusur bildirim kanalında.

**Kırmızı test:** sayaçlar sabitken aktif üye değişince chip imzası değişmeli.

### T3 — Liste takibi Resolve koşusunda durur

`MainWindow.FollowFrontier` ham motor durumunu okuyor (`State == Started`). Döngü grubunun tüm üyeleri grup
bitene kadar `Started` kalır, frontier ilk üyeye çakılır ve dead-band nedeniyle bir daha kaymaz. Doğru
predikat kodda zaten var: `RunViewModel.IsCompiling` — "predikatı beş yüzey okur" doküman notunun kaçırılmış
altıncı tüketicisi.

**Kırmızı test:** döngü akışında aktif üye değişince frontier satır indeksi ilerlemeli.

## Önemli

### T4 — Resolve'a tekrar basmanın anlamı

Bugün yakınsamama hafızası (`NoProgress`) ve temiz bileşik imza, grubu sessizce dispatch dışı bırakabiliyor;
kullanıcı butona basıp hiçbir şey olmadığını görüyor. Karar: açık Resolve basışı bir komuttur — hafıza o
koşuda grubu engellemez, grup taze bir çözüme girer. Zaten temiz olan grup derlenmez ama bunu açıkça söyler.
Tur planı, serilik ve persist kuralları değişmez.

**Kırmızı testler:** hafızada `NoProgress` taşıyan grup yeni Cycles komutunda dispatch edilmeli; temiz grup
atlanırken kullanıcıya görünür anlatı düşmeli.

### T5 — Konsol imleci

İmleç AvalonEdit'in üstünde mutlak konumlu bir overlay; ilk çıktı gelince tamamen gizleniyor ve belge
akışında yer kaplamadığı için son satırın üstüne binebiliyor. Otorite (README §2.5 ve prototip) prompt
satırının koşulsuz, son satırın altında, kendi satırında durmasını ister; koşarken yalnız metni boşalır.
Boyut ve blink zaten doğru. Renk amber olacak — tasarımın "dim"inden kullanıcı kararıyla sapma.

### T6 — Resolve başında graf parlaması

Döngü kapsamı dışındaki her proje koşu başında `Skipped (OutOfCycleScope)` seed'leniyor. Grafta
Queued'dan Skipped'a geçiş "iş bitti" sayılıp hold-fade tetikliyor: 2400 ms tam opak, sonra sönüş. Onlarca
proje aynı anda yanıp sönüyor. Hold yalnız gerçekten derlenmiş projeye (önceki statüsü Building olana) ait
olmalı; pre-skip bir bitiş değildir.

**Kırmızı test:** koşu başı pre-skip hold-fade tetiklememeli.

### T7 — Eksik tooltip'ler

Şeritteki döngü kümesinin tooltip'i hiç yok; tasarım iki satır ister (`In a dependency cycle — won't be
built` + mono döngü yolu). Döngü yolu tasarımda kart noktası, uyarı üçgeni ve statü glyph'inde de geçiyor
ama kodda hiçbirinde yok. Yol verisi Sync topolojisinde var, tek kaynaktan taşınacak — kopya yasak.

## Kozmetik

### T8 — Sol şerit ince görünüyor

Değer doğru (2 px, seçilide 3 px, 1 px dikey boşluk). Subpixel render şüphesi: şeride pixel-snap.
Kalınlık değerine dokunulmaz.

### T9 — Filtre soluklaşması

Graf zaten 280 ms glide ile sönüyor; kullanıcı daha belirgin bir geçiş istiyor. Filtre kaynaklı opaklık
geçişi uzatılır — tasarımdan kullanıcı isteğiyle bilinçli sapma, dokümana gerekçesiyle yazılır.

---

## Kapanış

Değişen davranışlar ARCHITECTURE.md ve README.md'de yerinde güncellenir. Tam süit yeşil olur; Cycles
motoruna dokunulduğu için acceptance ayrıca koşulur. Kullanıcı bilgisayar başında olmadığından branch
`main`'e merge EDİLMEZ — açık kalan konularla birlikte dönüşte değerlendirilecek.
