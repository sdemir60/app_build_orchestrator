# A12 — Kart animasyonu regresyonu: teşhis + fix

**Branch:** `a12-motion-regression` (BASE `5b13e56` = `main`) · **Fix commit:** `739cfa0`
**Kusur (kullanıcı, 2026-07-26):** *"Sol alt köşedeki kartlarda loading ile animasyonlar çalışırdı; bu adımda
hiç hareket etmiyor, animasyonlar yok, renklendirmeler vs hiç çalışmıyor."*

> **Belgenin kuralı (it5-records deseni):** her satır bir kanıta bağlıdır — `dosya:satır`, ölçüm sayısı, test
> adı ya da komut çıktısı. Kanıtı olmayan iddia yazılmadı; **çürüttüğüm kendi ara-hipotezlerim de yazıldı.**

---

## 0. Sonuç, tek paragrafta

Kusur **gerçek ve tek bir kök nedene** iniyor: **liste kartlarının kademeli beliriş animasyonu (`bo-reveal`)
üretimde HİÇ oynamıyordu.** `StickyLayerList.SetGroups`'ta `_revealPending` bayrağı `Flow.ItemsSource`
atamasından **SONRA** kuruluyordu; liste zaten realize edilmişken o atama container üretimini **senkron**
tamamlıyor, `OnGeneratorStatusChanged` bayrağı `false` görüp dönüyor ve bir daha status değişimi gelmiyor →
`PlayRevealStagger` hiç çağrılmıyor → kartlar tam opaklıkta "pat" diye beliriyor. Bayrak atamadan önceye
alındı (1 satır). **Kullanıcının "renklendirmeler yok" yakınmasının kaynağı ise ayrı:** ölçümde kartların
statü renkleri, şeritleri, glyph'leri, nefesi ve spinner'ı **çalışıyor** — aşağıdaki §2'de kanıtı var; o
yakınma büyük olasılıkla "beliriş hareketi yok" algısının yanına eklenmiş bir izlenimdir. Bu adımda
**yalnız ölçülebilen kusur** düzeltildi.

---

## 1. Hangi katmanda öldü — ÖLÇÜM

Prompt'un istediği ayrım: *motion gate false mu · animasyon başlamıyor mu · başlıyor da görsel mi değişmiyor.*
Cevap: **animasyon hiç BAŞLAMIYOR** (gate doğru, görsel katman sağlam).

### 1.1 Ölçüm yöntemi (harness ekran görüntüsü ALABİLİYOR — kayda geçsin)

Playbook "harness ekran görüntüsü alamaz" diyordu; **alabiliyor.** Kurulan araç:
`PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` ile pencere içeriği, **pencere örtülü olsa bile**, doğrudan
bitmap'e alınıyor; UIA ile de ağaç okunup buton `Invoke`/`Toggle` edilebiliyor. Bu sayede canlı uygulama
üzerinde piksel ölçümü yapıldı.

> **DPI tuzağı (kayda değer):** PowerShell 5.1 host'u DPI-unaware'dır → `GetWindowRect` **sanallaştırılmış**
> (1400×800), UIA ise **fiziksel** (1750×1000) verir. İlk yakalama bu yüzden kırpıldı ve action bar "yok"
> sanıldı. Bitmap boyutu **UIA'nın `BoundingRectangle`'ından** alınmalı.

### 1.2 KÖK NEDEN ÖLÇÜMÜ — reveal hiç oynamıyor

Uygulama yeniden başlatıldı (reveal yalnız **topoloji değişiminde** oynar → ilk Sync), ardından 4 kartın ad
bandı **19 ms aralıkla** örneklendi:

| Koşum | Örnek sayısı | **Ara-opaklık karesi** |
|---|---|---|
| **Fix ÖNCESİ** | 721 (14,0 s) | **0** |
| **Fix SONRASI** | 737 (14,0 s) | **5** |

300 ms'lik bir opaklık rampası 19 ms örneklemede **~15 ara kare** üretir. Fix öncesi satırlar boş (14,7) →
yerleşik (30,6 / 32,3 / 33,4 / 30,0) arasında **tek bir 19 ms adımda** atladı: rampa hiç yok.

Fix sonrası ham seri — **stagger de görünüyor** (satır 4, satır 1-3'ün gerisinde):

```
    ms    row1    row2    row3    row4
  2913    26,2    26,1    25,0    21,8     <- satir 4 geride = 10ms/satir stagger
  2934    28,4    28,2    28,4    25,1
  2951    28,8    28,5    29,1    25,8
  2967    29,4    29,7    30,1    26,9
  2985    29,8    30,5    31,2    27,9
```

### 1.3 Kök neden — `dosya:satır`

**`src/BuildOrchestrator.App/Controls/StickyLayerList.xaml.cs`**, `SetGroups`:

```csharp
// FIX ÖNCESİ (hatalı sıra)
Flow.ItemsSource = entries;          // <-- container üretimi BURADA senkron tamamlanabilir
UpdateOverlay(Scroll.VerticalOffset);
_revealPending = true;               // <-- bayrak ÇOK GEÇ kuruluyor
```

Tetikleyici zinciri (`:211-217`):

```csharp
private void OnGeneratorStatusChanged(object? sender, EventArgs e)
{
    if (Flow.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated) return;
    if (!_revealPending) return;     // <-- ATAMA SATIRININ İÇİNDE burada `false` görülüyor → DÖNÜYOR
    _revealPending = false;
    Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(PlayRevealStagger));
}
```

**Üretimdeki sıra kritik:** kabuk **önce** realize edilir, gruplar **sonra** akar
(`MainWindow.xaml.cs:361` `Shell.ProjectsList.SetGroups(groups)`). Liste zaten realize olduğu için
`ItemsSource` ataması üretimi senkron bitirir → handler o satırın **içinde** ateşlenir → bayrak henüz `false`
→ döner. Sonra bayrak `true` yapılır ama **bir daha status değişimi gelmez** → `PlayRevealStagger` hiç
çağrılmaz → `PART_Root.Opacity` hiç 0'a çekilmez → kartlar tam opaklıkta belirir.

**Fix (1 satır, `:117-126`):** bayrak `ItemsSource` atamasından **ÖNCE** kurulur. Üretim senkron bitse de
handler bayrağı `true` görür; asenkron bitse de davranış aynıdır (strictly daha dayanıklı).

### 1.4 Suite neden yeşildi — kanıtlı

`tests/.../App/StickyRevealTests.cs`:
- Yardımcı `Realize` (`:27-40`) **`SetGroups`'u realize'den ÖNCE** çağırıyor (`:34` sonra `:36`) — o sırada
  container üretimi **ertelenir**, yani hatalı sıra hiç tetiklenmez.
- **Yedi testin hepsi** `list.PlayRevealStagger()`'ı **DOĞRUDAN** çağırıyor (`:55, :70, :89, :90, :91, :110,
  :128, :144`).

Yani suite *"reveal çağrılırsa doğru oynar"* iddiasını kanıtlıyor, *"reveal gerçekten çağrılır mı"* sorusunu
**hiç sormuyordu** — prompt'un tarif ettiği `c6e9a21` sınıfı.

---

## 2. Playbook'un beş hipotezi — hepsi ÖLÇÜMLE ELENDİ

Prompt "doğrulanmadan koda DOKUNMA" dedi; beşi de ölçüldü ve **hiçbiri kusur değil**.

| # | Hipotez | Karar | Kanıt |
|---|---|---|---|
| 1 | `MotionGate` tek kapısı yanlış → tüm animasyonlar susar | **ELENDİ** | Nefes ve spinner **canlı ölçüldü** (aşağıda). Kapı `true` dönüyor; `App.Motion` kart kurulurken null değil (`App.xaml.cs:50-52`, pencereden önce set edilir) |
| 2 | `StaticAnimationsEnabled` statik okuma erken snapshot alıyor | **ELENDİ** | `MotionGate.cs:31` bir **property**'dir (`App.Motion?.AnimationsEnabled ?? false`) — her erişimde taze okunur, snapshot yok |
| 3 | G2 ikon `Viewbox` → paylaşılan **donmuş** `ScaleTransform` anime edilemiyor | **ELENDİ** | Graf düğüm ikonları doğru **ölçek ve renkte** çiziliyor (ekran görüntüsü); paylaşılan transform **statik** ölçek içindir, animasyon yolunda değil |
| 4 | G2 parked minor: `IconPaint` self-heal turunun fast-path'le kalkması | **ELENDİ** | Kart ve graf renklendirmeleri eksiksiz uygulanıyor (§2.1) |
| 5 | L1 tembel alt-ağaç: durum/spinner/renk elemanları hover'a düştü | **ELENDİ** | `ProjectRow.xaml` diff'i (`1b63720..HEAD`) yalnız **hover butonları + VS-chooser popup'ını** taşıdı; şerit/glyph/dot/sha/süre kartta kaldı |

### 2.1 Kartların renklendirmesi ve animasyonu ÇALIŞIYOR — ölçüm

Canlı Build koşusu sırasında (gerçek OSYS, 4 proje):

- **Statü şeridi doğru renkte** (6× büyütmeyle okundu): gri (skipped) · **yeşil** · **yeşil** · **amber**
  (building) — `ProjectRow.SetStripeFill` çalışıyor.
- **Statü glyph'leri:** discovered kesikli halka · ✓ yeşil · ⊖ skipped · amber spinner.
- **Ad rengi** dim/parlak, **süre** kolonu (0,5s / 0,6s / 2s), **will-build dot**'ları amber → temizde dim.
- **Spinner DÖNÜYOR:** ardışık karelerde spinner bölgesi ortalama farkı **13,5–19,0** (tepe 391).
- **Nefes NEFES ALIYOR:** building satırının amber zemini salınıyor, ortalama fark **2,8–10,2**.
- Graf düğümleri yeşile/ambere dönüyor, kenarlar/rozetler doğru; action bar sayaçları (`Σ4 ↻1 ✓2 ✗0 ⊖1`)
  ve event stream satırları renkli.

**Yani "renklendirmeler yok" ifadesinin ölçülebilir bir karşılığı bulunamadı.** Bu adımda ona dokunulmadı.

---

## 3. ÇÜRÜTÜLEN ARA-HİPOTEZ (dürüstlük kaydı — "iddiayı kurtarma")

Teşhis sırasında **yanlış bir kök nedene** varıp sonra kendi ölçümümle çürüttüm; kayda geçiyor çünkü
gelecekte aynı tuzağa düşülebilir.

**Yanlış iddia:** *"`SystemParameters.ClientAreaAnimation` ilk okumada önbelleğe alınıyor, hiç
tazelenmiyor ve `StaticPropertyChanged` bu özellik için hiç ateşlenmiyor → uygulama motion durumunu
mandallıyor."* İlk teşhis testi bunu "kanıtladı": raw SPI `False` iken WPF `True`, olay sayısı 0.

**Çürütme:** o test ayarı **`fWinIni = SPIF_SENDCHANGE` (2)** ile yazıyordu — bu form ayarı **kalıcılaştırmaz**
ve WPF'in invalidation yolunu tetiklemez. Windows Ayarlar uygulamasının kullandığı form
**`SPIF_UPDATEINIFILE | SPIF_SENDCHANGE` (3)**'tür. Onunla ölçüldüğünde:

```
--- SPIF_UPDATEINIFILE|SPIF_SENDCHANGE (fWinIni=3) --- start raw=True wpf=True
  flip to False: raw=False wpf=False signal=False signal.Changed=1 StaticPropertyChanged=1
  back to True:  raw=True  wpf=True  signal=True  signal.Changed=2 StaticPropertyChanged=2
```

→ **`SystemParametersMotionSignal` DOĞRU çalışıyor**; canlı takip iki yönde de tutuyor. Kusur orada değil.
O yanlış premise üzerine yazılmış 4 kırmızı test **silindi** (üretim kodu onlara göre değiştirilmedi).

**Kalan gerçek boşluk (A13 triyajına):** `src/BuildOrchestrator.App/Services/SystemParametersMotionSignal.cs`
— OS'a dokunan **tek** sınıf — **sıfır testlidir**; tüm reduced-motion testleri `FakeMotionSignal` enjekte
eder. Kodu doğru ölçüldü, ama koruması yok. Test yazmak makine-global bir erişilebilirlik ayarını
değiştirmeyi gerektiriyor (bu oturumda bir kez ayar yanlışlıkla **kapalı kaldı** ve elle geri alındı) →
bu riski kullanıcı kararı olmadan suite'e sokmadım.

---

## 4. Kapsam kararı — graf / konsol / event stream

Prompt: *"teşhis kusurun graf düğümlerini / konsolu / event stream'i de etkilediğini gösterirse aynı kök
nedeni oralarda da kapat."*

**Etkilemiyor — desen tek yerde.** `grep` ile tüm App ağacı tarandı: erteleyen bayrak + generator-status
tetikleyicisi **yalnız `StickyLayerList`'te** var.
- **Graf:** `GraphView.SetGraph` reveal'i **senkron** tetikliyor (`Graph/GraphView.xaml.cs:364`
  `PlayRevealStagger();`) → hatalı sıra yok.
- **Konsol / event stream:** bu deseni hiç kullanmıyor.

Başka görsel kusur alınmadı (A13/A14'e ait).

---

## 5. Kırmızıdan yeşile dönen testler

**Yeni:** `tests/BuildOrchestrator.Tests/App/StickyRevealTriggerTests.cs` — **3 test, fix'ten ÖNCE 3/3 KIRMIZI**
(kanıt: `Başarısız: 3, Başarılı: 0`), fix'ten sonra 3/3 yeşil.

| Test | Neyi pinler |
|---|---|
| `Feeding_groups_into_a_realized_list_actually_fires_the_reveal` | Tetikleyici **hiç** ateşliyor mu (`RevealGeneration` arttı mı) |
| `The_fired_reveal_collected_the_rows_and_took_the_hero` | `HasPendingRevealRelease` **tek assert'te ikisini** kanıtlar: release ancak hero alındıysa **ve** ≥1 satır toplandıysa zamanlanır |
| `Rows_start_transparent_so_the_stagger_is_actually_visible` | Gözle görülen sonuç: satırlar bir süre **şeffaf** olmalı; hepsi 1.0 ise reveal uygulanmadı |

Yardımcı `RealizeEmptyThenFeed` **üretim sırasını** kurar (realize → sonra `SetGroups`) — mevcut
`StickyRevealTests.Realize`'ın tersi; ayrım dosyanın XML yorumunda gerekçeli.

**Realize testi:** yeni XAML kökü / template **eklenmedi** (fix 1 satırlık sıra değişikliği + test), bu yüzden
`DsResources.Realize` üzerinden ek realize testi gerekmedi. Yeni testler yine de gerçek pencerede realize
ediyor (`DsResources.Realize`, It-5/T1 dersi: realize `window.Content` üzerinde).

**Mevcut 7 `StickyRevealTests` DEĞİŞMEDEN yeşil kaldı** (10/10 toplam) — fold davranış-nötr.

---

## 6. Koşum kayıtları (bu makinede, bu belge yazılırken)

| Ne | Komut | Sonuç |
|---|---|---|
| Build | `dotnet build BuildOrchestrator.slnx` | **0 Uyarı / 0 Hata** |
| Tam süit | `dotnet test .../BuildOrchestrator.Tests.csproj` | **1433 passed · 2 skipped · 0 failed** (1435; BASE 1432 + 3 yeni) |
| Token guard'ları (renk/motion/D8 + token) | `--filter NoHardcodedColor\|NoHardcodedMotion\|NoSleepPoll\|TokenBrushes\|TokenRealizeCoverage` | **69 / 69 passed** |
| Reveal testleri | `--filter StickyReveal` | **10 / 10 passed** |

> **⚠️ İlk tam koşumda 2 kırmızı vardı — gizlenmiyor:** `EngineHostTests.Start_receives_engineReady_and_ping_pong_works`
> ve `RunViewModelTests.RebuildCommand_enables_Stop_and_disables_Rebuild_before_runStarted_arrives`. İkisi de
> **izole koşuda geçti** (2/2) ve **ikinci tam koşum 0 failed** verdi. Oturum boyunca gerçek uygulama + Build'ler
> koştuğu için makine yüklüydü; ikisi de yük-hassas flake. İkisi de bu değişiklikle **ilgisiz** katmanlarda
> (IPC/process ve VM komut durumu). Bilinen `MsBuildInvokerTests.LingeringPostBuildGrandchild` flake'i bu
> koşumlarda hiç kırmızı vermedi. → **A13 (B) flake triyajına iki aday daha.**

**Ortam hijyeni:** oturum sonunda geride `BuildOrchestrator.App` / `BuildOrchestrator.Supervisor` process'i
**0** (kaskat doğrulandı: App kill → supervisor kendiliğinden öldü). OS "Animasyon efektleri" ayarı **1**
(teşhis sırasında geçici kapatıldı, geri açıldı ve **doğrulandı**).

---

## 7. Doküman senkronu

`CLAUDE.md` · `README.md` · `docs/TRUST-BOUNDARY.md` tarandı: bu değişikliğin **geçersiz kıldığı olgusal
ifade YOK**. `README.md:207` "reveal staggering"dan yalnız *L2 virtualization'ın aynı anda riske atacağı
şeyler* listesinde söz ediyor — o ifade hâlâ doğru. **Kozmetik doküman düzenlemesi yapılmadı** (kural).

---

## 8. Bu adımın kapsamadığı

- **"Renklendirmeler yok"** yakınmasının ölçülebilir bir karşılığı bulunamadı (§2.1) — kartlarda renk akışı
  çalışıyor. Kullanıcı hâlâ bir renk kusuru görüyorsa A14 dalgasına `panel · ne yaptım · ne bekliyordum ·
  ne gördüm · her seferinde mi` formatında yazılmalı.
- Aşağıdaki **kök nedenle ilgisiz** gözlemler bilinçli olarak alınmadı → **A13/A14**:
  1. Başarılı Sync'ten sonra bile başlıkta **`no repository`** yazıyor ve action bar'daki **branch chip'i
     boş** (`branch`), oysa A7 `OSYS · main` bekliyor.
  2. Konsola **Türkçe kullanıcı metni sızıyor**: `warning: git fetch failed — continuing against the local
     HEAD (git fetch başarılı ama remote-tracking ref okunamadı…)`. D1'in 77 metinlik süpürmesinden artakalan.
  3. Sync "no changes" döndüğünde `SetGroups` çağrılmıyor (kasıtlı, tam reset'ten kaçınmak için) → o Sync'te
     reveal de oynamaz. Tasarım kararı mı, kusur mu — **karar kaydı yok**.
  4. `CollectRows()` realize olmamış satırı atlar ve yorumu *"bir sonraki reveal onu yakalar"* der; oysa
     `SetGroups` yalnız topoloji değişiminde koştuğu için **bir sonraki reveal gelmeyebilir**. Bugün yeni
     testle pinli, ama savunma dalı hâlâ teorik olarak sessiz.
