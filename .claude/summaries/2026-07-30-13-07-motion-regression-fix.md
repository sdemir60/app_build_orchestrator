# A12 tamamlandı — kart reveal stagger regresyonu kapatıldı (main'e merge + push)

**Tarih:** 2026-07-30 13:07 · **Branch:** `a12-motion-regression` (merge sonrası silindi) → **`main` @ `4fb98f4`**
**Süit:** 1433 passed / 2 skipped / 0 failed (1435) · build 0/0 · token guard'ları 69/69
**Ölçek:** 3 commit / 2 dosya (1 satır üretim kodu + 1 yeni test dosyası) + 1 çıktı belgesi

---

## 1. Kusur ve kök neden

**Kullanıcı bildirimi (2026-07-26):** *"Sol alt köşedeki kartlarda loading ile animasyonlar çalışırdı; bu
adımda hiç hareket etmiyor, animasyonlar yok, renklendirmeler vs hiç çalışmıyor."*

**Ölçülen kusur:** liste kartlarının kademeli beliriş animasyonu (`bo-reveal`) üretimde **HİÇ oynamıyordu**.
Canlı uygulamada ilk Sync'te 4 kart, 19 ms aralıkla alınan **721 karede 0 ara-opaklık karesi** verdi —
300 ms'lik bir rampa ~15 ara kare üretirdi. Kartlar tam opaklıkta "pat" diye beliriyordu.

**Kök neden — `Controls/StickyLayerList.xaml.cs::SetGroups`:** `_revealPending = true` bayrağı
`Flow.ItemsSource = entries` atamasından **SONRA** kuruluyordu. Üretimdeki sıra "kabuk realize edilir, gruplar
sonra akar" (`MainWindow.xaml.cs:361`) olduğu için liste **zaten realize**; o durumda `ItemsSource` ataması
container üretimini **senkron** tamamlıyor → `OnGeneratorStatusChanged` o satırın **içinde** ateşleniyor,
bayrağı `false` görüp dönüyor, ve **bir daha status değişimi gelmiyor** → `PlayRevealStagger` hiç çağrılmıyor
→ satır opaklığı hiç 0'a çekilmiyor.

**Fix:** bayrak atamadan **önceye** alındı (1 satır). Senkron da asenkron da doğru çalışır.

**Suite neden yeşildi:** `StickyRevealTests`'in yardımcısı `SetGroups`'u realize'den **ÖNCE** çağırıyor
(üretim ertelenir, hatalı sıra hiç tetiklenmez) ve **yedi testin hepsi** `PlayRevealStagger()`'ı **DOĞRUDAN**
çağırıyor. Yani "reveal çağrılırsa doğru oynar" kanıtlanıyor, "reveal çağrılır mı" hiç sorulmuyordu —
`c6e9a21` sınıfı bir runtime yolu.

**Doğrulama (aynı ölçüm, fix sonrası):** 737 karede **5 ara-opaklık karesi** ve satır 4'ün satır 1-3'ün
gerisinde kalması → **10 ms/satır stagger gözlendi.**

---

## 2. Playbook'un beş hipotezi — hepsi ölçümle ELENDİ

`MotionGate` tek kapısı · `StaticAnimationsEnabled` erken snapshot · G2 donmuş paylaşılan `ScaleTransform` ·
G2 `IconPaint` self-heal · L1 tembel alt-ağaç. **Hiçbiri kusur değil.** Canlı Build koşusunda ölçüldü:
statü şeridi renkleri (gri/yeşil/yeşil/amber), glyph'ler, ad dim/parlak, süre, will-dot **doğru**;
**spinner dönüyor** (kare farkı 13,5–19,0, tepe 391) ve **nefes salınıyor** (2,8–10,2). Graf düğümleri
renkleniyor, ikonlar doğru ölçek/renkte.

---

## 3. Çürütülen ara-hipotez (dürüstlük kaydı)

Teşhis ortasında yanlış bir kök nedene vardım: *"`SystemParameters.ClientAreaAnimation` önbelleğe alınıp hiç
tazelenmiyor, `StaticPropertyChanged` hiç ateşlenmiyor → uygulama motion durumunu mandallıyor."* İlk teşhis
testi bunu "kanıtladı".

**Kendi ölçümümle çürüttüm:** o test ayarı `fWinIni = SPIF_SENDCHANGE (2)` ile yazıyordu — bu form ayarı
kalıcılaştırmaz ve WPF'in invalidation yolunu tetiklemez. Windows Ayarlar'ın kullandığı
`SPIF_UPDATEINIFILE | SPIF_SENDCHANGE (3)` ile: `signal.Changed=1`, `StaticPropertyChanged=1`, değer iki
yönde de doğru → **`SystemParametersMotionSignal` DOĞRU çalışıyor.** O premise üzerine yazılmış 4 kırmızı test
**silindi**; üretim kodu onlara göre değiştirilmedi.

---

## 4. Kapsam

Desen **tek yerde**: erteleyen bayrak + generator-status tetikleyicisi yalnız `StickyLayerList`'te.
**Graf** reveal'i `SetGraph` içinden **senkron** tetikliyor (`Graph/GraphView.xaml.cs:364`), **konsol/event
stream** bu deseni hiç kullanmıyor → etkilenmiyorlar. Kök nedenle ilgisiz görsel kusurlar alınmadı.

---

## 5. Yeni testler (fix'ten ÖNCE 3/3 KIRMIZI)

`tests/BuildOrchestrator.Tests/App/StickyRevealTriggerTests.cs` — üretim sırasını (realize → sonra
`SetGroups`) kuran `RealizeEmptyThenFeed` yardımcısı üzerinde:
`Feeding_groups_into_a_realized_list_actually_fires_the_reveal` ·
`The_fired_reveal_collected_the_rows_and_took_the_hero` (tek assert'te hero + ≥1 satır) ·
`Rows_start_transparent_so_the_stagger_is_actually_visible`.
Mevcut 7 `StickyRevealTests` **değişmeden** yeşil kaldı (10/10).

---

## 6. Yöntem notu — harness ekran görüntüsü ALABİLİYOR

Playbook "harness ekran görüntüsü alamaz" diyordu; **alabiliyor.**
`PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` pencere **örtülü olsa bile** içeriği bitmap'e alıyor; UIA ile
ağaç okunup buton `Invoke`/`Toggle` ediliyor; piksel farkı/parlaklık serisiyle animasyon **ölçülebiliyor**.
Bu, A13'ün "gözle kontrol borcunu teste çevir" hedefi için doğrudan kullanılabilir bir kanaldır.

> **DPI tuzağı:** PowerShell 5.1 DPI-unaware → `GetWindowRect` sanallaştırılmış (1400×800), UIA fiziksel
> (1750×1000) verir. Bitmap boyutu **UIA'nın `BoundingRectangle`'ından** alınmalı, yoksa yakalama kırpılır.

---

## 7. Koşum kayıtları + hijyen

Build **0/0** · tam süit **1433/2/0** · token guard'ları (renk/motion/D8 + token) **69/69** · reveal testleri
**10/10**.

⚠️ **İlk tam koşumda 2 kırmızı vardı, gizlenmiyor:** `EngineHostTests.Start_receives_engineReady_and_ping_pong_works`
ve `RunViewModelTests.RebuildCommand_enables_Stop_and_disables_Rebuild_before_runStarted_arrives`. İkisi de
izole koşuda geçti (2/2), **ikinci tam koşum 0 failed** verdi; oturumda gerçek uygulama + Build'ler koştuğu
için makine yüklüydü. İkisi de bu değişiklikle ilgisiz katmanlarda → **A13 (B) flake triyajına iki aday.**
Bilinen `MsBuildInvokerTests.LingeringPostBuildGrandchild` flake'i bu koşumlarda kırmızı vermedi.

**Hijyen:** geride App/Supervisor process'i **0** (kaskat doğrulandı). Teşhis için OS "Animasyon efektleri"
ayarı geçici kapatıldı; **geri açıldı ve doğrulandı (=1)**.

**Doküman senkronu:** `CLAUDE.md`/`README.md`/`docs/TRUST-BOUNDARY.md`'de geçersiz kılınan olgusal ifade YOK
(`README.md:207`'deki "reveal staggering" yalnız L2 riski listesinde geçiyor, hâlâ doğru) → dokunulmadı.

---

## 8. A13/A14'e devredilen (kök nedenle ilgisiz, bu adımda alınmadı)

1. Başarılı Sync'ten sonra bile başlıkta **`no repository`**, action bar'daki **branch chip'i boş** — A7
   `OSYS · main` bekliyor.
2. Konsola **Türkçe metin sızıyor**: `warning: git fetch failed — ... (git fetch başarılı ama remote-tracking
   ref okunamadı…)` — D1'in 77 metinlik süpürmesinden artakalan.
3. Sync "no changes" döndüğünde `SetGroups` çağrılmıyor → o Sync'te reveal de oynamaz. Kasıtlı (tam reset'ten
   kaçınma) ama **karar kaydı yok**.
4. `CollectRows()` realize olmamış satırı atlıyor ve *"bir sonraki reveal onu yakalar"* diyor; oysa `SetGroups`
   yalnız topoloji değişiminde koşar → bir sonraki reveal gelmeyebilir.
5. `Services/SystemParametersMotionSignal.cs` **sıfır testli** (OS'a dokunan tek sınıf). Kodu doğru ölçüldü;
   test yazmak makine-global erişilebilirlik ayarını değiştirmeyi gerektiriyor → **kullanıcı kararı.**
6. **"Renklendirmeler yok"** yakınmasının ölçülebilir karşılığı bulunamadı; kullanıcı hâlâ görüyorsa A14'e
   `panel · ne yaptım · ne bekliyordum · ne gördüm · her seferinde mi` formatında yazılmalı.
