# Resolve cycles — ölçüme dayalı performans analizi

> Kapsam: "Resolve cycles" düğmesinin süresi. Kod DEĞİŞTİRİLMEDİ; bu bir ölçüm + öneri raporudur.
> Ölçümler gerçek OSYS çalışma kopyasında (187 proje, 7 döngü grubu, 33 üye), 2026-10-02 sabah ve akşam
> oturumlarında, `develop` HEAD (f624f23) Release derlemesiyle alındı. Makine: Core Ultra 7 258V (4P+4LPE),
> 32 GB, prizde, güç planı Dengeli. Ham veri: `.claude/temp/cycle-resolve-perf-2026-10-02/`.

---

## 0. Sonuç (kısa)

Resolve'un süresini belirleyen şey **çizelgeleme değil, kaç üyenin derlendiği ve her WPF üyesinin ne kadar
pahalı derlendiği**. Dört somut sonuç:

1. **Tek bir üyede tek satır değişince 17 üyenin 17'si tam derleniyor: 63 sn.** Yalnız o üye derlense 6 sn
   (10×). Git geçmişinizde UI grubunu kirleten hareketlerin %40'ı tam bu durumda (tek üye değişmiş).
2. **Her WPF üyesi iki kez derleniyor** (WPF'in "geçici assembly" adımı). Bu adımı metadata-only yapmak çıktıyı
   değiştirmeden süreyi **%18–24** kısaltıyor (63 → 48 sn; 132 → 109 sn).
3. **Makine başka işle meşgulken Balanced profil derlemeyi aç bırakıyor:** aynı iş 62 sn yerine 119 sn. Full
   profilde 80 sn. Bugün 17:15'teki kendi Resolve koşunuz 266 sn sürdü; sakin makinede aynı tür iş ~132 sn.
4. **Çizelgeleme tükenmiş durumda:** UI grubunun "aynı anda derlenemeyen" en ağır üye kümesi 68 sn; gözlenen tur
   68–73 sn. Slot sayısını 6'ya, 8'e çıkarmak süreyi değiştirmiyor. Derleyici sunucusu mümkün ama yalnız %5–10
   kazandırıyor ve 2,5–3,6 GB RAM tutuyor.

| Senaryo (Balanced, sakin makine) | Bugün | Öneriler uygulanınca |
|---|---|---|
| Ardışık Resolve, değişiklik yok | 1,1 sn | — (sorun yok) |
| Tek üyede gövde değişikliği | 63 sn | **~6–8 sn** (Ö1; iki uç ölçüldü, uygulanmış hâli değil) |
| Branch değişimi sonrası (API değişmiş, 48 derleme) | 132 sn | **109 sn** (Ö2, ölçüldü) · 97 sn (Ö2 + Full + sunucu, ölçüldü) |
| Clean → Optimize → Resolve (83 derleme) | 122 sn | **105 sn** (Ö2 + Full birlikte, ölçüldü) |
| Tek üye değişimi, makine yük altında | 119 sn | 80 sn (Full, ölçüldü) · 61 sn (Full + Ö2, ölçüldü) · ~12 sn (Ö1, hesap) |

---

## 1. Ölçüm düzeneği

- **Harness** (`harness.py`): Supervisor'ı arayüzsüz, NDJSON ile sürer; her olayı zaman damgalar; kendi job
  nesnesiyle CPU/IO muhasebesi tutar; süreç başına CPU'yu (Defender dahil) ve CPU frekans durumunu örnekler.
- **Yalıtım:** her koşu gerçek defterin taze bir KOPYASIYLA (`--logs` ile ayrı kök) koşar; sizin
  `%LOCALAPPDATA%\BuildOrchestrator` klasörünüze yazılmadı. Derleme çıktıları gerçek yerlerine yazıldığı için
  deney dizileri bir disk yedeğiyle (`snap.py`: tüm bin/obj + `C:\OSYS\{Server,Client}\Bin`) başlayıp geri
  yüklemeyle bitti; son geri yüklemeler doğrulandı (0 fark, defter bayt bayt aynı, kaynak ağaçta değişen dosya yok).
- **Görev kırılımı:** `D:\Projects\Delta\Directory.Build.rsp` içine `-clp:PerformanceSummary` koyarak her
  invoke'un hedef/görev sürelerini proje loglarından okudum (dosya kaldırıldı — bkz. §9).
- **Motor-tarafı zamanlar:** olay akışı binlerce uyarı satırında anlık 1,4 sn gecikebildiği için repo kaynağının
  bir KOPYASINA (`exp-server/`) ortam değişkeniyle açılan zaman satırları ve deney bayrakları eklendi. Repo
  kaynağına dokunulmadı.
- **Tekrarlanabilirlik:** taban örnekleri 62,2–64,5 sn (6 örnek) ve 126,4–136,2 sn (3 örnek) aralığında; %5'in
  altındaki farkları anlamlı saymadım. Değişken koşuları (x2, v4, y2–y4, z1, z2) tek örnektir; raporlanan farklar
  taban yayılımının çok üstünde.
- Defender her deneyden önce sakinleşene kadar beklendi (`quiet.py`); yedek kopyanın taranması ilk ölçümü
  bozduğu için o ölçüm (V0) yalnız yapı analizinde kullanıldı, süre tablolarında yok.

## 2. Senaryolar

| # | Senaryo | Derlenen | Süre | Kaynak |
|---|---|---|---|---|
| S0 | Ardışık Resolve, değişiklik yok | 0 | **1,1 sn** (planlama 1,05) | `runs/e6-noop` |
| S1 | Tek üyede gövde değişikliği (UI.General'a yorum satırı) | 17 üye, tek tur, hepsi csc'li | **62,2 · 62,4 · 62,9 · 63,3 · 63,5 · 64,5 sn** (ort. 63,1) | `e7a`, `e7b`, `x1`, `x4`, `x6`, `y1` |
| S1′ | Aynı değişiklik, yalnız o üye (satırdan Build) | 1 | **6,3 sn** | `e7c-single-member` |
| S2 | Branch değişimi sonrası (gerçek durum; 33 üye + 5 upstream; UI grubu 17 + 7 bayat) | 48 derleme | **126,4 · 134,6 · 136,2 sn** (ort. 132,4) | `v0b`, `v0d`, `v0c` |
| S3 | Clean → Optimize → Resolve (33 üye + 50 upstream, tek tur) | 83 derleme | **122,1 sn** (+ Clean 4,1 + Optimize 4,2) | `z1-clean-balanced` |
| R1 | Sizin koşunuz, bugün 17:15 (branch değişimi sonrası) | 33 üye, 38 derleme | **266 sn** | `run-20261002-171511-896` |
| R2 | Geçmiş: 09-29 13:50 (tek tur, 17 UI üyesi) | 23 | 321 sn | `run-20260929-135019-989` |
| R3 | Geçmiş: 10-01 01:04 (UI grubu 2 TAM tur) | 33 üye, 52 derleme | 184 sn | `run-20261001-010430-305` |

R1 ve R2 aynı işin sakin makinedeki süresinin 2–2,5 katı: invoke başına süreler 3–10× (R1'de
`UI.SparePart.Finance` 72,5 sn; sakin makinede 19 sn. `UI.General`'da MarkupCompilePass1 16,8 sn; sakin
makinede 2,4 sn). Kopya retry'ı yok; neden CPU açlığı (B3).

## 3. Zaman nereye gidiyor (S2, üç temiz taban koşusu)

- Planlama 1,1 sn → Types zinciri (3 küçük grup, 13 invoke, hepsi seri) ~16 sn → **UI grubu 102–110 sn
  (%80)**: tur 1 68–73 sn (17 üye, 6 seviye), tur 2 33–37 sn (7 bayat üye, 4 seviye).
- Eşzamanlılık ortalaması 1,76–1,99; sürenin %43–45'inde TEK invoke koşuyor.
- MSBuild görev süreleri (48 build invoke, toplam 203–217 sn):

| Görev | Süre | Pay |
|---|---|---|
| Csc (iki geçiş toplamı) | 115–123 sn | %57 |
| — bunun WPF geçici assembly derlemesi (GenerateTemporaryTargetAssembly) | 48–52 sn | %24 |
| MarkupCompilePass1 | 40–45 sn | %20 |
| Exec (post-build `copy`) | 17,4–17,7 sn | %8 |
| MarkupCompilePass2 | 8–9 sn | %4 |
| ResolveAssemblyReference | 6,1–6,5 sn | %3 |
| Copy (copy-local) | 2,6–3,4 sn | %1–2 |
| Restore prologu (9 ayrı MSBuild) | 4,4–4,8 sn | — |

- Job CPU'su 405–434 sn (csc 296–317, MSBuild 79–86). **Aynı sürede Defender (MsMpEng) 61–70 sn CPU** harcıyor.
- **MSBuild süreci koşu başına 5,6–5,7 GB yazıyor** (çoğu copy-local; Clean sonrası 11,6 GB). OSYS'in bin
  klasörleri toplam 23,9 GB / 23.494 dosya.
- CPU frekansı koşu boyunca nominalin %155–177'si; ciddi termal düşüş yok.

## 4. Bulgular

### B1 — Tur 1 herkesi derliyor; döngüde MSBuild'in kendi artımlılığı hiç oturmuyor (S1: 63 sn → 6,3 sn)

- `RunCoordinator.BuildCycleGroupAsync`: `IReadOnlyList<string> toBuild = members;`
  ([RunCoordinator.cs:1582](../../src/BuildOrchestrator.Supervisor/RunCoordinator.cs#L1582)) — grup kirliyse tur 1
  bütün üyeleri invoke eder.
- Invoke edilen üyede MSBuild "güncel, atla" demiyor: döngüde her üye kendisinden SONRA derlenmiş bir kardeşi
  okur, o kardeşin DLL'i üyenin çıktısından yenidir → her invoke tam derlemedir. Ölçüm: S1'de değişmeyen 16
  üyenin 16'sında da csc koştu (`perfsum.py runs/e7a-hub-body --per-invoke`).
- Değişen tek üyenin yüzeyi değişmediği için tur 2 olmadı; yani 16 derlemenin hiçbiri sonucu değiştirmedi.
- Gerçek geçmiş (`gitstat.py`, OSYS reflog'undaki 83 HEAD hareketi): UI grubunu kirleten 73 hareketin
  **29'unda (%40) tam 1 üye**, 42'sinde (%58) en çok 3 üye değişmiş; 12'sinde 9+ üye. En sık değişen üye
  `UI.Service.WorkOrder` (66/83).
- Karşı örnek: S2'de (branch değişimi) 38 projenin 38'i de gerekliydi — 20'sinin kendi kaynağı değişmiş,
  kalan 18'i yüzeyi değişen `OSYS.Types.General`'ı okuyor (`needcompile.py`). Bu öneri o senaryoda 0 kazandırır.

### B2 — Her WPF üyesi iki kez derleniyor; ilki metadata-only yapılabilir (ölçüldü, çıktı aynı)

- WPF, XAML'de yerel tip kullanılınca projeyi önce geçici bir assembly'ye (`*_wpftmp.csproj`) TAM derler, sonra
  asıl derlemeyi yapar. S2'de bu adım 48–52 sn, S1'de 33 sn.
- Deney: geçici projeye `ProduceOnlyReferenceAssembly=true` vermek
  (`-p:CustomBeforeMicrosoftCommonTargets=<küçük bir targets dosyası>`).

| Ölçüm | Taban | Metadata-only | Fark |
|---|---|---|---|
| S1 (17 üye tek tur) | 63,1 sn | **48,2 sn** | −%24 |
| S2 (branch değişimi) | 132,4 sn | **108,6 sn** | −%18 |
| S3 (Clean sonrası) — Full ile BİRLİKTE, payı ayrıştırılmadı | 122,1 sn | 104,5 sn | (−%14, iki değişken) |
| Tek başına `UI.Service.WorkOrder` (7'şer örnek) | 9,0–9,8 sn | 6,6–7,3 sn | −%21…−24 |
| Tek başına `UI.SparePart.Finance` (3'er örnek) | 12,0–13,5 sn | 8,9–9,8 sn | −%25…−27 |
| Tek başına `UI.General` | 4,4 sn | 3,8 sn | −%14 |

- Geçici derleme görevi S1'de 33 → 9,6 sn, S2'de 51 → 15,9 sn; job CPU'su S1'de 231–252 → 168 sn.
- **Çıktı eşdeğerliği:** 17 WPF üyesinin 17'sinde derlenmiş BAML özeti, `.g.resources` boyutu, DLL boyutu ve API
  yüzey özeti taban derlemeyle AYNI (`baml-after-x5-refonly.json` ↔ `baml-after-x6-base.json`,
  `surface-after-*.txt`). S3 koşusunda kapsamdaki 83 projenin tamamı (22'si WPF, 20'si geçici assembly'li) bu
  ayarla 0 hatayla derlendi. Uyarı satırları azalıyor (geçici geçişin tekrar uyarıları gidiyor: 30,4 → 26,8 bin).
- Aynı çift derleme Build/Rebuild'deki WPF projelerinde de var (OSYS'te 52 WPF projesi); kazanç orada da
  beklenir (ölçmedim).
- Denetimde çıkan iki tuzak küçük deney projeleriyle (`mini/`) doğrulandı ve giderildi — bkz. Ö2.

### B3 — Balanced profil yük altında derlemeyi aç bırakıyor (62 → 119 sn; Full 80 sn)

- Deney: S1 senaryosu, yanında normal öncelikli 4 CPU-yakan süreç.

| Koşul | Profil | Süre |
|---|---|---|
| Sakin | Balanced | 62,4 sn |
| 4 çekirdek yük | Balanced (BelowNormal + %70 tavan) | **118,6 sn** |
| 4 çekirdek yük | Full (Normal, tavansız, 6 slot) | **80,0 sn** |
| 4 çekirdek yük | Full + B2 | 60,6 sn |

- Balanced koşusunda yük süreçleri alabilecekleri CPU'nun ~%98'ini aldı: derleme onlardan pay alamadı. Full
  koşusunda bu oran ~%78'e indi.
- Sakin makinede profil farkı küçük: S2'de Full 123,3 sn (−%7).
- R1 (bugünkü 266 sn) ve R2 (321 sn) bu örüntüyle uyumlu: tek iş parçacıklı görevler bile 3–7× yavaş.
  O anda neyin yük bindirdiğini loglardan bilemiyorum; ölçtüğüm şey mekanizma.

### B4 — Çizelgeleme tükenmiş: daha fazla slot/paralellik kazandırmıyor

- UI grubunda 136 üye çiftinin 64'ü (%47) aynı anda derlenemiyor (doğrudan kenar + ad öneki çakışması).
- Birbirini dışlayan en ağır küme: `UI.General`, `UI.SparePart.Common`, `UI.Service.Common`,
  `UI.Service.WorkOrder`, `UI.SparePart.Finance`, `UI.Service.Report` = **68,1 sn**. Komşuları eşzamanlı
  derlemeyen HER çizelgenin alt sınırı bu; gözlenen tur 1 68–73 sn (`graph.py`).
- Bariyersiz liste çizelgesi benzetimi 4, 6 ve 8 slotta aynı sonucu veriyor (73–74 sn).
- Kısaltmanın tek yolu bu kümedeki üyeleri hızlandırmak (B2) ya da hiç derlememek (B1).

### B5 — Derleyici sunucusu mümkün (doküman aksini söylüyor) ama kazancı küçük

- `Microsoft.CSharp.Core.targets:160` → `SharedCompilationId="$(SharedCompilationId)"`: pipe adı dışarıdan
  verilebiliyor (VS 18.9 toolset'i).
- Ölçüm (`-p:UseSharedCompilation=true -p:SharedCompilationId=<motor başına GUID>`): sunucunun ebeveyni
  `MSBuild.exe`, harness job'ının içinde doğuyor, motor kapanınca ölüyor (`runs/v2-server-balanced/meta.json`
  → `vbcs`).
- Kazanç: S2 132,4 → 119,8 sn (−%10); Csc görevi 115–123 → 95 sn. Full ile birlikte 116,7 sn. Tek üyede
  (sıcak sunucu) 9,2 → 8,7–9,4 sn.
- Bedel: sunucunun tepe belleği dört koşuda **2,5 · 2,5 · 2,8 · 3,6 GB**.
- ARCHITECTURE §9.2'deki "~2,9×" bu iş yükünde yok: csc zaten hızlı açılıyor, süre gerçek derleme işi.

### B6 — Antivirüs ve copy-local hacmi

- S2 koşusu sırasında Defender 61–70 sn CPU harcıyor (8 temiz ölçüm); S3'te 83 sn.
- Taze yazılmış bir DLL'in ilk açılışı 0,1–1,2 sn bekletiyor (`UI.Service.Print.dll`, 6,2 MB: 1,2 sn). Motorun
  derleme sonrası yüzey özeti bu ilk açılışı üstleniyor: koşu başına toplam 10,8–15 sn (üye slotu tutulurken).
  Eski dosyalarda özet ms düzeyinde (60 dosya 78 ms).
- 26 GB'lık yedeği Defender yaklaşık 17–20 dakikada taradı (~1 çekirdek sürekli).
- Yazılan verinin %86–90'ını MSBuild süreci yazıyor — büyük kısmı copy-local kopyaları (S2: 6,6 GB'ın 5,7'si;
  S3: 12,8 GB'ın 11,6'sı). csc'nin yazdığı yalnız 0,19–0,25 GB.
- Defender'ı kapatıp A/B ölçemedim (yönetici yetkisi ve Tamper Protection). Etkisi için elimdeki kanıt
  yukarıdaki CPU ve bekleme süreleri; net kazancı söyleyemem.

### B7 — Tur 2: bu senaryoda gerçek API değişiminden; iki ucuz fikir kazandırmıyor

- S2'de tur 2'ye giren 7 üyenin okuduğu kardeş yüzeyleri gerçekten değişmiş (13 üyenin DLL boyutu da farklı).
  Yüzey özeti kararlı: yeniden derlenen 33 üyenin 20'sinde özet aynı kaldı (gövde değişimi özeti oynatmıyor).
- "Internal üyeleri sayma" (OSYS assembly'lerinde `InternalsVisibleTo` yok): bu senaryoda yine aynı 13 üye
  "değişti" çıkıyor → 0 kazanç (`surf2.py`).
- "Değişen üyeler önce" sıralaması: bayat 7 üyenin 3'ü (Print, DMS, Finance) düşerdi ama tur 2'nin kritik
  kümesi (General, SparePart.Common, Service.Common, WorkOrder = 33 sn) kalır → en çok ~6 sn (hesap, ölçülmedi).

### B8 — Küçük motor maliyetleri

- **Restore prologu:** paketleri yerinde olan üyelere koşu başına 9 (S3'te 13) ayrı `-t:restore` süreci:
  4,4–8,5 sn; her biri 0,45–0,85 sn.
- **Yüzey kanıtı ya hep ya hiç ve sessiz:** tek bir üreticinin dosyası koşu başında okunamazsa grup tam tura
  düşüyor ve `decision.log` nedenini yazmıyor. R3'te UI grubu 17 üyeyi iki kez derledi (34 derleme, 184 sn);
  sonradan nedenini bulmak mümkün olmadı.
- **İlk yüzey okuması seri:** soğuk dosyalarda 34 dosya 7,1 sn (deneyimde geri yüklenen dosyalar yüzünden;
  gerçek hayatta eski dosyalarla 0,3–0,5 sn).
- **Log hacmi:** koşu başına 70–78 bin satır (`Types.UsedCars` tek başına 11.958 uyarı, `-clp:Summary` ile iki
  kez). Süreye etkisi küçük; olay akışını anlık 1,4 sn geciktiriyor.

## 5. Öneriler (öncelik sırasıyla)

### Ö1 — Üye düzeyi artımlı tur 1 (en büyük kazanç: günlük döngü)

Tur 1'de yalnız GEREKEN üyeleri derle; kalanlar bugünkü "bayat mı?" kuralıyla izlenmeye devam etsin.

- Gerekli üye = (i) kendi terimi değişmiş (kendi içeriği + grup dışı upstream imzaları), ya da (ii) son güvenilir
  derlemesinde okuduğu bir dosyanın yüzeyi artık farklı, ya da (iii) güvenilir kaydı / yüzey kanıtı yok (o zaman
  bugünkü davranış: herkes).
- Gereken veri zaten hesaplanıyor ama atılıyor: üye başına terim
  ([IncrementalPlanner.cs:198-205](../../src/BuildOrchestrator.Core/Incremental/IncrementalPlanner.cs#L198-L205),
  bileşik imzanın girdisi) ve üyenin okuduğu yüzeyler
  ([RunCoordinator.cs:1981](../../src/BuildOrchestrator.Supervisor/RunCoordinator.cs#L1981), `ReadStates`,
  yalnız bellekte). İkisi yakınsamada deftere yazılır; sonraki koşu atlanan üyeyi "Succeeded + defterdeki
  ReadStates" ile başlatır; tur sonu döngüsü (staleNow, `CycleRoundPolicy.Decide`) aynen kalır.
- Beklenen: S1 63 → ~6–8 sn. En sık değişen `UI.Service.WorkOrder` için ~10–18 sn (yüzeyi değişmezse);
  yüzeyi değişirse 4 okuyucusuyla birlikte ~35 sn (hesap). S2/S3'te kazanç yok.

**Bağımsız denetimin gösterdiği eksik: (i)–(iii) tek başına yetmez.** Bugünkü seçici turlar, atlanan üyenin
diskteki çıktısının AYNI koşuda araç tarafından üretilmiş olmasına da dayanıyor; kayıt koşular arasına taşınınca
bu kendiliğinden doğru olmaz. Zorunlu ekler (`agents/review.md`, K1–K10):

| Ek kural | Kapattığı durum |
|---|---|
| (iv) Üyenin kendi çıktısı eksik/bozuksa üye gereklidir | Çıktısı silinmiş üye derlenmez, okuyucuları patlar (K1); önek çakışmasının ezdiği ortak kopya (K10) |
| (v) Kayda çıktının kimliği (boyut + zaman) yazılır; kimlik tutmuyorsa ya da üye "dışarıda derlenmiş" kipindeyse üye gereklidir | Visual Studio'nun ya da satır menüsünün araya girip yeniden derlediği üye (K2, K3) |
| (vi) Durdurulan, çöken ya da yakınsamayan koşu üye kayıtlarını siler | Eski yakınsamanın kaydının yarım-yeni çıktılarla eşleşmesi (K4, K5) |
| (vii) Kayıt, kardeş olmayan referansları ve bir "motor parmak izi"ni de taşır (toolset sürümü + argüman sözleşmesi) | Üçüncü parti DLL değişimi, toolset güncellemesi, Ö2 gibi sözleşme değişiklikleri (K8, K9) |
| (viii) Kullanıcıya görünür "grubu tam derle" yolu | Kalan bilinmeyenler için kaçış |

- Bilinçli karar gerektiren yan etki: atlanan üyenin kendi `bin` klasöründeki kardeş kopyaları (copy-local)
  tazelenmez (K6). OSYS ortak `C:\OSYS\…\Bin`'den çalıştığı sürece etkisi yok.
- Kontrol edildi, OSYS'te yok: bir üyenin başka bir projenin `bin`'indeki KOPYAYI okuması (K7) — döngü
  kapsamındaki 83 projede proje `bin`'ine giden 28 referansın 28'i üreticinin kendi çıktısı (`k7.py`).
- Doğrulanmadı: satır menüsünden tek üye derlemesinin deftere ne yazdığı (K3'ün ikinci yarısı).
- Risk: orta–yüksek. Defter şeması büyür; App'te derlenmeyen üyenin satırı/sayacı ve ETA terimi yeniden
  düşünülür; ARCHITECTURE §7.3 ve §8.8 yeniden yazılır. **Ön koşul Ö1a.**

**Ö1a — Yüzey özetinin kör noktalarını kapat (doğruluk; Ö1'den bağımsız olarak da gerekli).** Küçük test
assembly'leriyle doğrulandı (`blind/`): şu dört değişiklikte `ApiSurfaceHash` AYNI kalıyor:

| Değişiklik | Tüketiciye etkisi |
|---|---|
| `decimal` varsayılan parametre 0,18m → 0,20m | Çağrı yerine eski değer gömülü kalır |
| `int[]` → `params int[]` | Çağrı biçimi değişir |
| `[CallerMemberName]` kaldırıldı | Çağrı yerinde üretilen argüman değişir |
| struct'ın private alanı `int` → `object` | Kesin atama / `unmanaged` kuralları değişir |

Kök neden dört örnekten geniş: parametre, dönüş değeri ve generic parametre özniteliklerinin TAMAMI özete
girmiyor ([ApiSurfaceHash.cs:223-230](../../src/BuildOrchestrator.Core/Incremental/ApiSurfaceHash.cs#L223-L230),
`:247-262`; `[Dynamic]`, tuple adları, diğer `Caller*` öznitelikleri, `DateTimeConstant` de aynı sınıfta) ve
private alanlar struct'larda da atılıyor
([ApiSurfaceHash.cs:153](../../src/BuildOrchestrator.Core/Incremental/ApiSurfaceHash.cs#L153)). Bugün etkisi:
böyle bir değişiklikten önce derlenmiş okuyucu tur 2'ye alınmaz ve grup "yakınsadı" sayılır.

### Ö2 — WPF geçici assembly'sini metadata-only derle (ölçülen −%18…−24)

Argüman sözleşmesine tek ek: araçla birlikte gelen küçük bir targets dosyası; `$(MSBuildProjectName)`
`_wpftmp` ile bitiyorsa üç şey yapar. İlk deney dosyası yalnız birincisini yapıyordu; denetimin işaret ettiği iki
tuzak deney projeleriyle doğrulanıp diğer ikisiyle kapatıldı (`mini/bo-wpftmp-v3.targets`, `mini/minitest*.py`):

| Öğe | Neden | Doğrulama |
|---|---|---|
| `ProduceOnlyReferenceAssembly=true` | Geçici derlemeyi gövdesiz yapar | OSYS: 17/17 üyede çıktı aynı; S3'te 83 proje 0 hata |
| `ProduceReferenceAssembly=false` | SDK-style projede `/refout` + `/refonly` birlikte **CS8308** veriyor | Mini SDK-style WPF projesi: eksikken hata, ekleyince derleniyor, BAML aynı |
| Geçici assembly'ye bir `InternalsVisibleTo` özniteliği (tek satırlık ek kaynak dosyası) | Öznitelik yokken metadata-only derleme internal üyeleri atıyor; XAML yerel bir tipin internal özelliğini kullanıyorsa **MC3072** veriyor | Mini eski-stil proje: eksikken hata, ekleyince derleniyor, BAML ve DLL boyutu aynı, öznitelik nihai DLL'e GİRMİYOR |

- Üç öğeli dosya gerçek üyelerde aynı kazancı veriyor: `UI.Service.WorkOrder` 9,0–9,2 → 6,9–7,0 sn,
  `UI.SparePart.Finance` 12,0 → 9,0 sn; BAML ve yüzey özeti aynı.
- Yarıda kesilme denendi: geçici assembly yazıldıktan hemen sonra kesilen derlemeden sonra normal derleme
  (ayarlı ya da ayarsız) csc'yi yeniden koşturuyor, nihai DLL tam (gövdeli) çıkıyor.
- OutDir'e, obj düzenine, nihai çıktıya dokunmaz; etkilenen tek şey WPF'in zaten silip attığı ara assembly.
- Kalan riskler: (a) §9.2 argüman sözleşmesi ve onu pinleyen `MsBuildArgumentsTests` yeniden yazılır;
  (b) `CustomBeforeMicrosoftCommonTargets` global verilince bir projenin kendi tanımını ezer — OSYS'te bu
  özelliği tanımlayan proje/props/targets yok (tarandı), ortam değişkeni de yok; başka çalışma alanlarında
  olabilir; (c) yalnız bu toolset'te (VS 18.9) doğrulandı — eski toolset'ler ve derleyici paketi sabitleyen
  projeler için sürüm kapısı ya da proje bazında geri düşüş gerekir (OSYS'te böyle proje yok, tarandı);
  (d) targets dosyası sürümlü kurulum klasöründe değil, kalıcı bir yolda durmalı; (e) gövde hataları artık
  geçici geçişte değil asıl derlemede çıkar (hatalı projede süre kısalmaz), uyarı sayısı azalır.

### Ö3 — Resolve koşusunda öncelik: yük altında 1,5× (karar sizin)

Balanced'ın amacı ("makine kullanılabilir kalsın") ile Resolve'un doğası (kullanıcı sonucunu bekliyor)
çatışıyor. Seçenekler: (a) Cycles koşusu Full'ün öncelik/tavanıyla koşsun; (b) koşu sırasında görünür bir
"hızlandır" yolu (perf chip'i canlı değişimi zaten destekliyor); (c) hiçbir şey değişmesin, davranış
bilinsin. Ölçüm: yük altında 118,6 → 80,0 sn.

### Ö4 — Küçük motor işleri

1. Restore prologunu paket hedefleri yerindeyken atla (Optimize'ın kullandığı kontrol): koşu başına 4–8 sn
   süreç, kritik yolda ~1–4 sn.
2. Yüzey kanıtı düşünce nedenini `decision.log`'a yaz; mümkünse üretici bazında düş (yalnız o üreticinin
   okuyucuları bayat sayılsın).
3. Derleme sonrası yüzey özetini slot dışına al ve ilk okumayı paralel yap (kritik yolda birkaç saniye; soğuk
   durumda 7 sn).

### Ö5 — Ortam (araç dışı)

- Defender için derleme ağaçlarına (`D:\Projects\Delta`, `C:\OSYS`) ya da süreçlere (MSBuild.exe, csc.exe)
  dışlama / Dev Drive. Kazancı ölçemedim; yönetici olarak tek bir A/B koşusu (aynı harness) kesin sayıyı verir.
- OSYS referanslarında Copy Local = False: koşu başına 5,7–11,6 GB yazım ve 24 GB bin kopyası gider. OSYS
  ekibinin kararı; ad öneki çakışmasını (`copy $(TargetName).*`) da ortadan kaldırır.

### Ö6 — OSYS'te döngüleri kırmak (kalıcı çözümün verisi)

Bir grubu DAG yapmak için kesilmesi gereken en az referans sayısı (`fas.py`, kesin çözüm): UI grubu **15**
(14 karşılıklı çift + 1), Types grupları 1 + 2 + 3, Business grupları 1 + 1 + 1 → toplam 24. UI grubunun kesim
listesi `fas.py` çıktısında; ilk bakılacak adaylar B4'teki 6 üyeli kümenin içindeki karşılıklı
çiftler (`SparePart.Common ↔ Service.WorkOrder`, `SparePart.Common ↔ SparePart.Finance`,
`Service.WorkOrder ↔ SparePart.Finance`, `Service.Common ↔ Service.WorkOrder`, `General ↔ SparePart.Common`).

## 6. Ölçüldü, kazandırmıyor (yapılmasın)

| Fikir | Sonuç |
|---|---|
| Daha fazla slot / bariyersiz çizelge | 73–74 sn → aynı (B4) |
| Derleyici sunucusu tek başına | −%10, 2,5–3,6 GB RAM (B5) |
| XAML'i ayrı AppDomain'siz derlemek (`AlwaysCompileMarkupFilesInSeparateDomain=false`) | daha YAVAŞ (9,4 → 10,5–13,8 sn) |
| Portable PDB | fark yok (9,2–9,8 sn) |
| Internal üyeleri yüzeyden çıkarmak | S2'de aynı 13 üye (B7) |
| Sakin makinede Full profil | −%3…−7 (asıl fark yük altında, B3) |

## 7. Doküman ile gerçek uyuşmuyor (kararınız gerekli)

ARCHITECTURE.md §9.2: derleyici sunucusunun pipe adının "hiçbir dış geçersiz kılması olmadığını (VS 18'de
doğrulandı)" ve paylaşılan derlemenin "~2,9×" kazandırdığını söylüyor. Ölçüm: pipe adı `SharedCompilationId`
ile verilebiliyor, sunucu job içinde doğup motorla ölüyor; kazanç bu iş yükünde %5–10. Kod bugün
`UseSharedCompilation=false` geçiyor ve bu doğru çalışıyor; yanlış olan dokümandaki gerekçe. Hangisinin
düzeltileceği sizin kararınız (öneri: bayrak kalsın, §9.2'nin gerekçesi "mümkün ama ölçülen kazanç maliyetine
değmiyor" olarak yeniden yazılsın).

## 8. Yeniden üretim

```bash
W=.claude/temp/cycle-resolve-perf-2026-10-02
dotnet build BuildOrchestrator.slnx -c Release          # uygulama kapalı olmalı
python $W/snap.py save <ad>                             # ÖNCE yedek (≈26 GB, 15 sn)
$W/run.sh <etiket> --mode cycles --perf Balanced        # yalıtılmış defterle tek koşu
python $W/analyze.py $W/runs/<etiket>                   # zaman çizelgesi
python $W/perfsum.py $W/runs/<etiket> --per-invoke      # görev kırılımı (Directory.Build.rsp gerekir)
python $W/snap.py restore <ad> && python $W/snap.py verify <ad>   # SONRA geri yükle, 0 fark bekle
```

| Dosya | İçerik |
|---|---|
| `runs/<etiket>/` | `meta.json`, `events.jsonl`, `samples.csv`, `analysis.md`, `perfsum.txt`, `diag.log`, koşu logları |
| `campaign*.log`, `evening.log` | Deney dizilerinin dökümü |
| `graph.py`, `fas.py`, `needcompile.py`, `gitstat.py`, `surf2.py`, `k7.py` | Çakışma grafı, kesim listesi, gereklilik, git profili, yüzey karşılaştırması, referans taraması |
| `wpfspike.py`, `bo-wpftmp.targets`, `mini/` | WPF geçici assembly deneyleri (gerçek üyeler + deney projeleri) |
| `exp-server/` | Deney bayraklı motor kopyası (`BO_EXP_*`) |
| `blind/` | Yüzey özeti kör nokta testleri |
| `agents/review.md` | Bağımsız denetçinin tam metni |
| `PROGRESS.md` | Oturum defteri |

## 9. Sınırlar ve dürüstlük notları

- Ö1'in kazancı ölçülmüş iki uçtan türetildi (63,1 sn ↔ 6,3 sn); uygulanmış hâli ölçülmedi. Ara durumlar
  (WorkOrder + okuyucuları) hesaptır. Ek kurallar (iv)–(viii) tasarım önerisidir, denenmedi.
- Ö2 bu makinedeki toolset'te, döngü kapsamındaki 83 OSYS projesinde ve iki deney projesinde doğrulandı; kapsam
  dışındaki ~30 OSYS WPF projesi derlenmedi.
- Yük deneyi sentetik (4 sabit CPU süreci); sizin 17:15'teki gerçek yükün ne olduğunu bilmiyorum.
- Defender'ın net etkisi ölçülemedi (B6).
- Denetim: ilk iki ajanlık inceleme oturum limitine takılıp sonuçsuz bitti. Akşam tek, dar kapsamlı bir denetçi
  raporu ham veriye karşı denetledi (süre sayıları tuttu; görev aralıkları ve birkaç ifade düzeltildi) ve Ö1/Ö2'ye
  saldırdı; bulguları bu sürüme işlendi.
- Sabah oturumu limit yüzünden yarıda kaldığı için: (a) sabahki son deneyin çıktıları (döngüler derlenmiş
  hâlde) diskte kaldı, o günkü ilk yedek geri yüklenmedi; (b) `D:\Projects\Delta\Directory.Build.rsp` gün boyu
  yerinde kaldı — bugünkü koşularınızın proje loglarında PerformanceSummary bölümleri bu yüzden var (derlemeyi
  etkilemez). Akşam oturumunda: dosya kaldırıldı, akşam deneylerinden önce alınan yedek geri yüklendi ve
  doğrulandı, kaynak dosya (`OSYS.UI.General\Properties\AssemblyInfo.cs`) özgün baytları ve özgün zaman
  damgasıyla yerinde, yedekler silindi.
