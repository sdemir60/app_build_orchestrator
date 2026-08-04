# İlk açılışta maximize taşması — teşhis + TDD planı

**Durum:** kök neden ÖLÇÜLDÜ, doğrulandı. Kod yazılmadı; bu doküman kodlama session'ına devredilecek plandır.

**Semptom (kullanıcı):** VS'ten ya da exe'den başlatınca ilk açılışta pencere tam ekrana "oturmuyor" — görev
çubuğunun altında çok küçük bir kısım kalıyor, sağdan/soldan hafif ekran dışına taşıyor. Küçültüp tekrar
maximize edince oturuyor.

---

## 1. Kök neden (tek cümle)

`MainWindow` XAML'de **doğuştan maximized** açılır (`MainWindow.xaml:7`), ama maximize taşma düzeltmesi
YALNIZ `OnStateChanged` override'ından uygulanır (`MainWindow.xaml.cs:798-807`) — WPF ise HWND'den ÖNCE
kurulmuş bir `WindowState` için **`StateChanged`'i hiç tetiklemez**. Sonuç: ilk açılışta
`RootShell.Padding` `0,0,0,0` kalır ve içerik her kenardan frame kalınlığı kadar ekran dışına taşar.
Kullanıcı küçültüp büyütünce ilk kez gerçek bir durum GEÇİŞİ olur, override koşar, padding uygulanır ve
pencere "oturur".

Yani hatalı olan `MaximizeFix.PaddingFor` DEĞİL (hesap doğru) — **kablajı**: düzeltme ilk açılışta hiç
çalışmıyor.

---

## 2. Kanıt (ölçüm, tahmin değil)

`MainWindow`'un pencere kabuğunu birebir taklit eden tek kullanımlık bir probe koşuldu
(`WindowStyle=SingleBorderWindow` + kod-tarafı `WindowChrome(Caption 40 / ResizeBorder 6 / GlassFrame 0)` +
`WindowState=Maximized` `Show()`'dan ÖNCE + `OnStateChanged` override + PerMonitorV2 manifest).
Probe repo DIŞINDA, scratchpad'de:
`…\scratchpad\probe\` (Program.cs / probe.csproj / app.manifest) — repoya hiçbir şey eklenmedi.

Ham çıktı:

```
ctor sonu:            WindowState=Maximized, StateChanged sayisi=0
[Loaded]              state=Maximized StateChanged sayisi=0 dpiScale=1,25 RootPadding=0,0,0,0
[ContentRendered]     state=Maximized StateChanged sayisi=0 dpiScale=1,25 RootPadding=0,0,0,0
[T+1.2s kararli]      state=Maximized StateChanged sayisi=0 dpiScale=1,25 RootPadding=0,0,0,0
      HWND rect (px): L=-9 T=-9 R=2889 B=1749  (W=2898 H=1758)
      work    L=0 T=0 R=2880 B=1740 | monitor L=0 T=0 R=2880 B=1800
--- simdi Restore ---
### OnStateChanged #1: state=Normal,    dpiScale=1,25 ppi=120 CXSIZEFRAME=4 CYSIZEFRAME=4 CXPADDEDBORDER=5
--- simdi Maximize ---
### OnStateChanged #2: state=Maximized, dpiScale=1,25 ppi=120 CXSIZEFRAME=4 CYSIZEFRAME=4 CXPADDEDBORDER=5
```

Okunuşu:

| Gözlem | Sayı | Anlamı |
|---|---|---|
| `StateChanged` sayısı ilk açılışta (Loaded / ContentRendered / +1.2s) | **0** | Override HİÇ koşmuyor → padding 0 kalıyor |
| `StateChanged` sayısı restore→maximize sonrası | **2** | Kullanıcının workaround'u tam olarak bu: ilk gerçek geçiş |
| Maximized HWND rect vs work area | her kenarda **9 px** taşma (`-9,-9` … `2889,1749` vs `0,0…2880,1740`) | Sağ/sol/üst ekran dışında, alt 9 px görev çubuğunun ALTINDA |
| Frame metrikleri @120 dpi | `CXSIZEFRAME=4` + `CXPADDEDBORDER=5` = **9 px** | Taşma miktarıyla BİREBİR aynı |
| Doğru padding | `(4+5)/1.25 = 7.2 DIP` = 9 px | `MaximizeFix.PaddingFor` bunu üretir — sadece çağrılmıyor |

Taşma miktarı ile frame metriğinin birebir eşleşmesi, `StateChanged=0` ölçümüyle birlikte hipotezi
kanıtlıyor. Semptomun "çok hafif" olması da buradan: 9 px.

> Not: bu `dotnet/wpf#3887`'nin ta kendisidir ve proje bunu zaten biliyor
> (ARCHITECTURE.md §12.2 "Maximize padding correction is mandatory"). Eksik olan mecburiyetin kendisi değil,
> **ilk açılışta uygulanması**.

---

## 3. Bulgular (bloklayıcı → önemli → kozmetik)

### B1 — BLOKLAYICI · Düzeltme ilk açılışta hiç uygulanmıyor

**Konum:** `src/BuildOrchestrator.App/MainWindow.xaml.cs:798-807` (`OnStateChanged` override — tek uygulama
noktası) ↔ `src/BuildOrchestrator.App/MainWindow.xaml:7` (`WindowState="Maximized"`).

`OnStateChanged` yalnız WM_SIZE üzerinden, yani pencere GÖSTERİLDİKTEN sonraki durum GEÇİŞLERİNDE koşar.
Doğuştan maximized pencerede geçiş yoktur → `RootShell.Padding` hiç yazılmaz.

**Bu tuzağın adı bu repoda zaten konmuş** — `Shell/CaptionGlyphs.cs:61-64` (mevcut kod, değişmez):

> "**Neden `DependencyPropertyDescriptor`, `StateChanged` DEĞİL:** `StateChanged` yalnız pencere
> gösterildikten sonra (WM_SIZE üzerinden) tetiklenir; DP izleyicisi hem OS kaynaklı hem de programatik
> (**ilk kurulum dahil**) her değişimi yakalar."

Yani caption glyph'i doğru desende kurulmuş, padding aynı pencerede yanlış desende kalmış.

### B2 — ÖNEMLİ · DPI değişimi padding'i bayatlatıyor (ve ilk değeri de bozabiliyor)

**Konum:** aynı yer — `MainWindow.xaml.cs:798-807` tek yeniden-hesap noktası; `OnDpiChanged` override
EDİLMİYOR.

İki sonucu var:

1. Maximize haliyle farklı ölçekli bir monitöre taşınırsa (Win+Shift+←/→) `WindowState` DEĞİŞMEZ →
   padding eski DPI'nin px değerinde donar.
2. Uygulama sistem ölçeğinden farklı ölçekli bir monitörde açılırsa, ctor anında (HWND yokken)
   okunacak DPI sistem DPI'sidir; doğru değer ancak DPI olayıyla gelir.

B1'in fix'i (2) sayesinde B2'yi de kapsamalı — aksi halde B1 çözülür ama çok monitörlü kurulumda hâlâ
yanlış padding görülür. **Aynı kök nedene bağlı, tek fix — ama ayrı test** (CLAUDE.md kuralı).

### K1 — KOZMETİK · Aynı DP üzerinde iki ayrı izleyici

Fix uygulanınca `Window.WindowStateProperty` üzerinde iki `DependencyPropertyDescriptor` aboneliği olur
(`CaptionGlyphs.BindMaxButton` + yeni padding binder'ı). İşlevsel sorun değil, birleştirme de gerekmez:
sorumluluklar ayrı (glyph vs. yerleşim) ve birleştirmek iki kabuk davranışını tek kabloya bağlardı.
**Öneri: dokunma.** Burada yalnız bilinçli karar olarak kayda geçiyor.

---

## 4. Çözüm tasarımı

### Seçilen: `MaximizeFix`'e kablaj (`Bind`) ekle, `OnStateChanged` override'ını KALDIR

`Shell/MaximizeFix.cs` bugün yalnız saf hesabı (`PaddingFor`) taşıyor. Yanına, `CaptionGlyphs.BindMaxButton`
ile **birebir aynı desende** tek kablaj noktası eklenir:

```
MaximizeFix.Bind(Window window, Border target)
    Update()                       // ← ilk kurulum: doğuştan maximized pencereyi YAKALAR (B1)
    DependencyPropertyDescriptor.FromProperty(Window.WindowStateProperty, typeof(Window))
        .AddValueChanged(window, Update)     // her durum değişimi (OS + programatik)
    + DPI değişimi de aynı Update'e bağlanır (B2)

Update() = target.Padding = PaddingFor(window.WindowState, GetSystemMetricsForDpi(...), ..., dpiScale)
```

- `MainWindow` ctor'da `CaptionGlyphs.BindMaxButton(...)` çağrısının hemen yanında çağrılır (kabuk kablajı
  tek blokta toplanır — CLAUDE.md "dağılmış mantık" kuralı).
- `MainWindow.xaml.cs:798-807` `OnStateChanged` override'ı **silinir**. Bırakılırsa aynı hesap iki yerden
  sürülür → "kopya YASAK / tek doğruluk kaynağı" değişmezi ihlal edilir.
- `MaximizeFix.PaddingFor` ve `MaximizeFixTests`'e **DOKUNULMAZ** — hesap zaten doğru, kusur orada değil.

**Kodlama session'ında doğrulanacak tek belirsizlik:** `Window.DpiChanged` event'i mi kullanılacak yoksa
`MainWindow`'da `OnDpiChanged` override edilip aynı `Update`'e mi yönlendirilecek. İkisi de tek uygulama
noktasını korur; olay `Bind` içinden abone olunabiliyorsa kablaj tek yerde kalır (tercih edilen). Bu bir
API doğrulaması, tasarım kararı değil.

### Reddedilen: `OnStateChanged` kalsın, ilk uygulama `OnSourceInitialized`'a eklensin

Çalışırdı, ama **süit içinde KIRMIZI gösterilemezdi**: `OnSourceInitialized` HWND ister, bu süit
`MainWindow`'u bilerek `Show()` etmez (gerçek tepsi ikonu/global kısayol/supervisor bir testin yan etkisi
olamaz — `StartupWindowStateTests` sınıf özeti, `MainWindowRealizeTests`). Kırmızı gösterilemeyen fix bu
projede yapılmaz. DP izleyicisi HWND'siz çalışır (`RestoreGlyphTests` bunu zaten kanıtlıyor) → seçilen
tasarım hem doğru desen hem test edilebilir.

---

## 5. TDD sırası (kırmızı önce)

Hedef dosya: **yeni** `tests/BuildOrchestrator.Tests/App/MaximizePaddingWiringTests.cs`
(`[Collection("Console UI (serial)")]`, `[StaFact]`, `MainWindowHost.New(temp).window` deseni —
`StartupWindowStateTests` ile aynı iskelet). `MaximizeFixTests.cs` saf hesabın testidir, karıştırılmaz.

| # | Test | Bugün | Neyi pinler |
|---|---|---|---|
| T1 | `Padding_is_applied_when_the_window_is_born_maximized` — ctor'dan çıkan pencerede `RootShell.Padding != 0` (dört kenar eşit) | **KIRMIZI** (`0,0,0,0`) | B1 — kusurun tam kendisi |
| T2 | `Restoring_clears_the_padding_and_maximizing_puts_it_back` — `WindowState=Normal` → `0`; `=Maximized` → tekrar `!=0` | Bugün de yeşil olabilir (override koşar) → **T1'den SONRA, override SİLİNDİKTEN sonra anlamlı**: yeni kablajın eski davranışı kaybetmediğini pinler | Regresyon: eski yolun kapsadığı davranış |
| T3 | DPI değişimi tek uygulama noktasına bağlı (B2) | KIRMIZI | B2 |

**T1'in beklenen değeri testte YENİDEN HESAPLANMAZ** (formül kopyası yasak): assertion "sıfır değil +
dört kenar eşit" üzerinden kurulur; sayısal doğruluk zaten `MaximizeFixTests`'in işidir. Böylece test
makinenin DPI'sinden bağımsız kalır (ölçüm makinesinde 7.2 DIP, 96 dpi'de 8 DIP çıkar).

**T3 için uyarı — kodlama session'ında ölçülecek:** headless süitte gerçek bir DPI değişimi ÜRETİLEMEZ
(HWND ve `WM_DPICHANGED` gerekir). İki seçenek var, sırayla denenir:
1. `Bind`'in DPI dalını enjekte edilebilir bir dpi kaynağı (`Func<DpiScale>` benzeri) üzerinden kurup
   testte tetiklemek — gerçek KIRMIZI/YEŞİL ölçülür (**tercih edilen**);
2. o mümkün değilse, `NoSnapLayoutsTests` desenindeki gibi kaynak-tarama guard'ı (tek uygulama noktasının
   DPI olayına bağlı olduğunu pinler) — daha zayıf, ancak (1) ölçülüp uygulanamazsa.
Seçenek (2)'ye düşülürse gerekçesi test doc'una yazılır. Test yeşile boyamak için (2)'ye kaçmak YASAK.

---

## 6. Bitiş koşulları

- `dotnet build BuildOrchestrator.slnx` temiz.
- `dotnet test … --filter "Category!=Acceptance"` **tam yeşil** (token/motion/D8 guard'ları dahil).
- Elle doğrulama (kusur runtime'da yaşıyor, süit `Show()` etmiyor): `dotnet run --project
  src/BuildOrchestrator.App/…` → **ilk açılışta** pencere görev çubuğunun üstünde bitmeli, sağ/sol kenarda
  taşma olmamalı; küçült→maximize sonrası görüntü ilk açılıştakiyle AYNI olmalı (bugün farklı).
- Uygulama açıkken build alma (çalışan Supervisor kendi binary'lerini kilitler).

## 7. Doküman etkisi

- `ARCHITECTURE.md` **§12.2 Window chrome** (satır 869-870): "Maximize padding correction is mandatory
  (`dotnet/wpf#3887`)" iddiası **doğru kalıyor**, dokunulmaz. B2 uygulanırsa düzeltmenin ne zaman
  uygulandığı (doğuştan-maximized + DPI değişimi dahil) bir yan cümleyle eklenir — anlatı üslubu, changelog
  değil.
- `ARCHITECTURE.md` §22 kod haritası satır 1671 zaten `MaximizeFix.cs`'i işaret ediyor → değişiklik yok.
- `README.md` / `CLAUDE.md`: etki yok.

## 8. Temizlik

Scratchpad'deki probe (`…\scratchpad\probe\`) tek kullanımlıktır, repoya girmez; kanıtı bu dokümanda
kayıtlı olduğu için silinebilir.
