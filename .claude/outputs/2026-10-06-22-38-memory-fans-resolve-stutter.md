# Masaüstü takip ölçümleri — F doğrulaması, boşta bellek, derleme sonrası CPU, Resolve'da animasyon takılması, B4 tablosu

**Tarih:** 2026-10-06 22:04–23:00 · **Kod:** `fix/orphaned-animation-clocks` (develop `0bd4853` + yetim saat düzeltmesi + `BO_PROBE_FRAMES`
sondası) · **Ortam:** kullanıcı uzakta, klavye/fareye dokunulmadı; keepawake (ES_DISPLAY_REQUIRED) açık; OSYS temiz (Clean →
Resolve → Build ile her oturum sonunda çıktılar geri kuruldu) · **Araçlar:** `.claude/temp/perf-2026-10-06/` (`f-verify.ps1`,
`sys-watch.ps1` (WMI süreç başına CPU + bellek, `dotnet-counters` GC sayaçları), `resolve-stutter.ps1` (UIA ile Resolve, kare
aralığı sondası), `visible-run-trace.ps1` (dotnet-trace örnekleme + GC tahsis olayları, gcdump), `mem-after-resolve.ps1`,
`mem-regions.ps1` (VirtualQueryEx bölge dökümü), `b4-idle.ps1`); scratchpad'de `AllocTool` (TraceEvent ile tahsis özeti).
Önceki rapor: `2026-10-06-21-53-orphaned-animation-clocks.md` (kök neden ve düzeltme).

## 1. Özet

| Konu | Sonuç |
|---|---|
| Konu 1 (F): tepside koşu sonrası UI thread | **Düzeltme doğrulandı.** Pencere açıkken biten koşudan sonra gizlenince 11,4 M/s (önce 118); koşu sürerken gizlenince 11,5–11,9 (önce 13). Hedef ≤ 40 ✓ |
| Konu 2 (B4): ön planda etkin pencere boşta | 181–188 M/s, değişmedi (hedef ≤ 120). Tek sahip imleç saat çifti; kare hızı değil, imlecin HİÇ durmaması belirleyici (§3). **Karar sizin.** |
| Boşta ~900 MB | Uzun görünür bir koşudan sonra App 690–730 MB + Supervisor ~130 MB = gözlenen rakam (§4). Taze açılış 265–290 MB. Denenen arka plan GC değişikliği ölçülebilir kazanç vermedi, geri alındı. |
| Derleme sonrası fan | Uygulamanın kendisi koşu bitince 5 s içinde %6'ya düşüyor; sonraki 30–60 s'de işlemciyi Defender (MsMpEng %5–30, ara sıra %50+) ve makinedeki diğer işler kullanıyor (§5). Uygulamada düzeltilecek bir şey yok. |
| Resolve'da animasyon takılması | Ölçüldü: 50–118 ms'lik kare boşlukları, UI thread p99 37–46 ms. **Öncelik kaldıracı değil** (AboveNormal A/B: fark yok). Sebep uygulamanın kendi UI yükü: koşu başı/sonu satır inşası (100–230 ms'lik tek işler), metin biçimleme ve UI Automation olayları, tahsis baskısı (§6). Düzeltme ayrı, planlı bir iş. |

## 2. Konu 1 doğrulaması (f-verify, 22:04–22:09)

| Okuma | UI M/s | render M/s | App WS/Private MB | Not |
|---|---|---|---|---|
| B4 ön plan boşta, taze | 67,1 | 69,4 | 273 / 180 | önceki 67 — değişmedi |
| F: Rebuild görünürken bitti → +3 s gizle → tepsi 20 s | **11,4** | 0,0 | 462 / 359 | önce 118 |
| D: göster → ön plan koşu sonrası boşta | 93,9 | 58,5 | 460 / 357 | önce 103; koşu öncesi 67 (dolan sonlu saatler + büyüyen ağaç) |
| E: Rebuild +5 s gizle → tepside bitti → tepsi 20 s | **11,5** | 0,0 | 316 / 211 | önce 13 |

Aynı makinede ilk Rebuild 78,9 s, ikincisi 24,4 s, sonraki oturumlarda 28–30 s: ilk koşu soğuk (Defender/önbellek); UI yükü
değil (`F6 görünür` 28 s = `tepsi` 24 s).

## 3. Konu 2 — B4 tablosu (ön plan, pencere etkin, boşta)

Boşta etkin pencerede dönen tek süs: imleç saat çifti (kırpma 30 fps + renk turu 20 fps; `CursorHop.FrameRate`), iki imleç tek
saatte. Ölçümler (UI + render + GPU sürücüsü thread'leri):

| Durum | Toplam M/s | UI | render | Not |
|---|---|---|---|---|
| Bugünkü: imleçler dönüyor | 181 | 67 | 69 | her oturumda 176–188 |
| Kırpma 15 fps (renk turu 20'de kaldı; geçici derleme) | 180 | 67 | 70 | kare hızı tek başına kaldıraç değil |
| İmleçler durmuş (pencere arkada) | 16,9 | 16,6 | 0 | önceki ölçüm; etkin pencerede de aynı taban beklenir |

Yani fark tamamen "imleç animasyonu çalışıyor mu" sorusundan geliyor; bir kare çizildiği sürece pencere kompozisyonu + GPU
sürücüsü maliyeti ödeniyor. Seçenekler (kod değişmedi, siz seçeceksiniz):

1. **Girdi yokken imleci durdurmak** — Windows'un kendi imleç kuralı (`SPI_GETCARETTIMEOUT`, varsayılan 5 s): N s klavye/fare
   girdisi yoksa iki imleç sabit (opak, dinlenme rengi), ilk girdide ya da aktivasyonda yeniden başlar. Beklenen boşta: ~17 M/s.
   `CursorClock`'a üçüncü kapı (aktiflik ve görünürlük gibi); `HiddenCursorClockTests` deseninde testler.
2. **Kare hızını düşürmek** (hem kırpma hem renk turu, ör. 15/10) — ölçüm kırpmada tek başına etkisiz çıktı; ikisini birden
   düşürmek ölçülmedi; görsel olarak kırpma kabalaşır.
3. **Hedefi değiştirmek** (≤ 120 → ≈ 180): imleç çalışırken bu makinede altına inilemiyor.

## 4. Boşta bellek (~900 MB sorusu)

Ölçülen (App çalışma kümesi WS / özel bayt; Supervisor ayrı):

| An | App WS / Private | GC yığını (committed) | Supervisor WS | Not |
|---|---|---|---|---|
| Taze açılış, boşta (3 oturum) | 261–290 / 168–200 | 29 MB | 95–98 | bölge dökümü: image 356 (DLL sayfaları, paylaşımlı), mapped 229 (font/kaynak), private 148 |
| Görünür Rebuild 28–30 s sonrası | 392–398 / 271–295 | 90 MB; canlı yığın 50 MB (357k nesne) | 137 | zorla tam GC → yalnız −5 MB: kalan canlı veri |
| Görünür Rebuild 79 s sonrası | 485 / 359 | 161 MB (gen2 117 + LOH 26) | 127 | |
| Görünür Clean + Resolve (100 s) sonrası, boşta | **688 / 586** (bölgeler: image 356, mapped 241, private 559) | 388 MB: gen2 255 + LOH 113 | 125 | **kullanıcının gördüğü ~900 MB bu durum** (813 + derleme kalıntıları) |
| Tam GC sonrası (gcdump) | 682 / 578 | 379 MB: gen2 158 + LOH 112 — 97 MB çöp gitti, committed yalnız −9 (GC bölgeleri elde tutuyor) | 125 | kalan 270 MB CANLI yönetilen veri |
| Tepsiye inince | 683 / 578 | 379 MB | 125 | görünür koşunun ardından gizlenince bugün toplama yok (toplama yalnız tepside biten koşuda) |
| Tepside biten koşudan sonra | 316–332 / 211 | 36 MB | 127 | agresif toplama koşuyor |

Okuma: taze uygulamanın 148 MB'lik özel belleğinin yalnız 29 MB'si yönetilen yığın; gerisi CLR + WPF kompozisyon/D3D + DWrite/font +
AvalonEdit. Görünür bir koşu sırasında tahsis 28–83 MB/s (konsol metni, akış satırları, kare başına metin biçimleme) ve GC yığını
büyür. 100 s'lik görünür Resolve'dan sonra 559 MB özel belleğin ~270 MB'si CANLI yönetilen veri (LOH 112 MB: büyük metin/dizi
nesneleri — koşu metninin kopyaları; gen2 158 MB: satır/düğüm görselleri, olay listeleri, dolu kalan sonlu saatler), ~100 MB bir
GC'nin alacağı çöp (ama GC committed'ı elde tutuyor: 388 → 379), ~170 MB native (taze 120'ye göre +50: metin/kompozisyon
önbellekleri). Denenen "görünür koşu sonrası arka plan toplaması" bu tabloda da anlamlı olmazdı (çöp 100 MB, ama committed
düşmüyor; agresif/sıkıştıran kip UI'yi durdurur) — geri alındı. ~900 MB'nin bileşimi: App (uzun görünür koşu sonrası 690–730) +
Supervisor (125) (+ derleme sırasında MSBuild/csc). Kalıcı azaltma yolu, koşu bittikten sonra ekranda tutulan verinin kapsamını
daraltmaktır: koşu metninin kopyaları (VM tamponu + AvalonEdit belgesi + arka tampon), proje başına tutulan olay listeleri (disk
logu zaten tam), dolu kalan sonlu saatler — hangisinin ne kadar olduğu gcdump'tan tip bazında çıkarılabilir; bu bir tasarım
kararıdır (ne kadar geçmiş ekranda kalsın), kod değişmedi.

## 5. Derleme sonrası CPU / fan

`sys-watch` örneklerinden (tek çekirdek yüzdesi), Rebuild bitişi 22:07:08 (görünür) ve 22:08:44 (tepside):

| Koşu sonrası | App | Defender (MsMpEng) | System | Diğer |
|---|---|---|---|---|
| +0–5 s | 61 → 14 | 8–96 | 23–26 | |
| +5–30 s | 6–7 | 4–21 | 5–13 | VS Code %8–17, Logi agent %3–6 |
| +30–60 s | 6–13 | 11–53 | 10–18 | SQL Server %8–35, SDXHelper %59 (bir örnek) |

Uygulama koşu bitince hemen sakinleşiyor (final animasyonu ~5 s). Derleme sırasında ise görünür pencerede App tek çekirdeğin
%82–121'ini kullanıyor (UI + render + finalizer thread'i — §6) ve Defender %60–147: fanı ısıtan derleme boyunca bu ikisi + MSBuild;
sonrasında Defender'ın yeni çıktıları taraması ve ısı ataleti. Öneri (uygulama dışı): OSYS çıktı klasörleri için Defender dışlaması
— önceki A/B'de koşu −%9,5 (`cycle-resolve-perf-findings`).

## 6. Resolve sırasında animasyon takılması

**Ölçüm (resolve-stutter, pencere görünür, Clean → Resolve cycles tam öncelikte):**

| Bacak | Kare/s (sonda açıkken) | >33 ms | >50 ms | >100 ms | En uzun boşluk | UI gecikmesi p95 / p99 / max |
|---|---|---|---|---|---|---|
| App Normal öncelik | 79,3 | 196 | 41 | 4 | 113 ms | 11,7 / 36,9 / 93 ms (2150 örnek) |
| App AboveNormal | 77,5 | 188 | 44 | 7 | 118 ms | 13,4 / 46,3 / 108 ms |

Öncelik hiçbir şeyi değiştirmedi. O sırada işlemci (tek çekirdek %): App 76–121, Defender 90–147, System 13–78, MSBuild 14–38 —
derleyiciler değil, uygulamanın kendisi ve Defender. (Sonda `CompositionTarget.Rendering`'e abone olduğu için uygulama her karede
çizer; mutlak kare sayıları şişkin, A/B karşılaştırması geçerli.)

**Nerede harcanıyor (dotnet-trace, görünür Rebuild'in 30 s'i, UI thread %31 meşgul):** metin biçimleme %18, UI Automation olayları
%15 (bir UIA istemcisi bağlı: her yerleşimde `AutomationPeer.UpdateSubtree`), render %13, kanal kilidi bekleme %9, DP/binding %8,
animasyon saatleri %8, GC duraklaması %8, yerleşim %8. **Finalizer thread'i 30 s'nin 21,5 s'i meşgul** (DWrite font/metin
tutamaçlarını bırakıyor — metin biçimleme çöpü). Tahsis 839 MB/30 s (28 MB/s, 131 GC, toplam duraklama 569 ms): String 81 MB
(konsol pompası/NDJSON), `EffectiveValueEntry[]` 65 MB (`Shape.GetPen` ← `Rectangle.OnRender`: kesik çizgili yörünge/kare
kalemleri her çizimde), UIA peer nesneleri ~130 MB, AvalonEdit görsel satırları (GlyphRun, Int16[], UInt16[]) ~45 MB, animasyon
tik'leri (`ModifiedValue`, `TimeIntervalCollection`) ~30 MB.

**Takılmanın kendisi (UI thread'in pompaya dönmeden en uzun dilimleri):** 226 ms (diğer dispatcher işi), 139 ms (animasyon render
geçişi), **124 ms ve 109 ms `ProjectRow.InitializeComponent`** (satır inşası: koşu başı/sonunda liste yeniden kuruluyor ve
frontier takibi ilk kez görünen satırları inşa ediyor — satır başına XAML yükleme + şablon), **122 ms `RunViewModel.OnIsRunningChanged`**
(koşu başı/sonu yüzey yenilemesi), 98 ms. Bunlar kullanıcının gördüğü donmalar; 35–50 ms'lik sık boşluklar ise kare başına
maliyetten (AvalonEdit görünür satır yeniden üretimi, 24 ms'lik daktilo tik'i, UIA, GC).

**Seçenekler (her biri kendi kırmızı testi + ölçümüyle, ayrı iş):**
1. Satır container'larını koşu dışında önceden ısıtmak (havuz) ve koşu başı/sonundaki liste yeniden kurulumunu kaldırmak/ertelemek
   → 100–230 ms'lik dilimler.
2. AvalonEdit'e batch başına görünür satırları yeniden üretmek yerine daha seyrek/birleşik yazmak; daktilo satırının 41 Hz'lik
   `Run` yeniden kurulumunu seyreltmek → metin biçimleme ve finalizer yükü.
3. UIA peer güncellemelerinin maliyetini düşürmek (dekoratif öğelerde peer üretmemek) — bir UIA istemcisi bağlıyken ödeniyor.
4. Beads/kare kalemlerinin kare başına yeniden kurulmasını önlemek (`Shape.GetPen` tahsisi) — küçük.

## 7. Bu turda değişen kod

- `App/Services/FrameGapProbe.cs` (yeni) + `App.xaml.cs` kablajı: `BO_PROBE_FRAMES=<dosya>` verilince kare aralıkları yazılır;
  ARCHITECTURE §17.5'te bir cümle. Üretimde ortam değişkeni yoksa kurulmaz.
- Denenen ve GERİ ALINAN: görünür koşu sonrası arka plan GC (`BackgroundMemoryCollector`) — tam GC'nin bile 5 MB verdiği yerde
  anlamsız; test kuralı eski hâlinde.
- Rapor dosyası `09-53` → `21-53` (Bash `date` saat dilimi hatası; gerçek saat).
- Tam süit (`Category!=Acceptance`, son kod durumu, 7,7 dk): 4654 test — 4645 geçti, 8 atlandı, 1 düştü
  (`HeadWatcherTests.A_checkout_settles_into_one_branch_switch`: git izleyicisinin yerleşme penceresi paralel yük altında fazladan
  iki Commit olayı gördü; dokunulmayan kod). Sınıf tek başına yeniden koşuldu: 7/7 yeşil.
