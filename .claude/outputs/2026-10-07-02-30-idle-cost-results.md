# Sonuç — boşta imleç, görünür koşuda takılma, koşu sonrası bellek (G1–G4)

**Tarih:** 2026-10-07 02:30 · **Plan:** `2026-10-07-00-05-idle-cost-plan.md` · **Taban:** develop `786f2f9` · **Kanıt:**
`.claude/temp/perf-2026-10-06/` (verify3 izi, resolve1 kare sondası, mem1 gcdump) + bu oturumun iz analizleri.
Masaüstü kilitli olduğu için (LogonUI) **"sonra" ölçümleri henüz alınmadı**; her görevin kanıtı iz/döküm analizi +
kırmızı→yeşil testlerdir. Ölçüm için hazır betik: `.claude/temp/perf-2026-10-06/measure-all.ps1` (aşağıda).

## Durum

Üçü de `develop`'a `--no-ff` ile girdi ve push edildi (`769fdaf` G1, `deaa79d` G2, `d3b0476` G3); birleşik develop'ta tam
süit yeşil (4670 geçti, 8 atlandı, 0 düştü). Çalışma branch'leri silindi.

| Görev | Merge | Durum |
|---|---|---|
| G1 — imleçler girdi yokken durur | `769fdaf` (feat/caret-idle-stop) | **geri alındı (kullanıcı kararı, 03:00):** imleçler odağı izler — pencere önde ve görünürken yanıp söner, arkada/tepsideyken durur; "5 s girdi yok" kapısı kalktı (`feat/carets-follow-focus`, guard `CaretFocusRuleTests`). B4 hedefi (≤ 40 M/s) bilerek bırakıldı: ön planda boşta ~181 M/s kabul edildi |
| G2 — satırlar kalıcı, akış yerinde uzlaşır, UIA rolleri | `deaa79d` (perf/list-keeps-rows) | ölçüldü (aşağıda): UI thread meşguliyeti %31 → %21; "tüm satırlar çizilir" bedeli (+47 MB, +26 M/s) **Hidden bandı** ile geri alındı (`perf/list-render-band`) |
| G3 — konsol belgeleri geri-alma geçmişi tutmaz | `d3b0476` (perf/console-memory) | ölçüldü: koşu sonrası canlı yönetilen yığın 196 → 119 MB |
| G4 — konsol çizim yükü | — | ölçüldü, değişiklik yok (gerekçe aşağıda) |

## G2 — plan değişti: havuz değil, kalıcı satırlar (ÖLÇÜLDÜ)

Plan "container havuzunu boşta ısıt" diyordu; izin dilim dilim ayrıştırılması (`slice_breakdown.py`, `slice_anchor.py`) bunun
yanlış kaldıraç olduğunu gösterdi. Görünür Rebuild izinde (30 s) UI thread'inin 90 ms'nin üstündeki beş dilimi:

| Dilim | Panel Realize | Arrange | CleanUp | Satır inşası (ctor+InitializeComponent) | İş türü (ilk üç) |
|---|---|---|---|---|---|
| 109 ms | 33 | 5 | 4 | 12 | metin biçimleme 22 %, UIA 21 %, yerleşim 14 % |
| 139 ms | 39 | 12 | 11 | 9 | metin 31 %, UIA 20 %, yerleşim 16 % |
| 226 ms | 68 | 38 | 17 | 14 | metin 35 %, UIA 15 %, binding 12 % |
| 124 ms | 38 | 13 | 7 | — | (aynı dağılım) |
| 98 ms | 22 | 9 | 5 | — | (aynı dağılım) |

Yani dilimler, takip kaydırması pencereyi tek karede 30–60 satır taşıdığında geri dönüştürülen container'ların YENİ veriye
bağlanmasıdır (satır başına metin biçimleme + UIA peer tazelemesi + binding ≈ 2,5 ms); inşa dilimin %10'u. Havuz yalnız o
%10'u alırdı. Yapılan:

- **Satır kontrolü satırında kalır** (`FixedHeightVirtualizingPanel`): ilk yerleşim pencereyi kurar, kalan satırlar
  `ApplicationIdle` dilimleriyle gelir (3 girdi/dilim ≈ 8 ms, kareler arasına sığar); kurulan container bir daha bırakılmaz —
  kaydırma hiçbir satırı yeniden bağlamaz. Tek-seferlik bedel: 191 satır ≈ 1 s boşta iş, bellek satır başına bir kontrol.
- **Akış yerinde uzlaşır** (`ListReconciler`): topoloji/filtre tazelemesi `ItemsSource` takası (tam reset) yerine çıkanı siler,
  gireni ekler, yer değiştireni taşır; dokunulmayan satırın kontrolü, ölçümü ve kaydırma konumu kalır. Taşınan satır bilerek
  yeniden kurulur: WPF generator'ı ağaçta bırakılan container'ı, yeni yer kurulmamış bir bloğa komşuysa yanlış ofsete
  bağlıyor (`OnItemMoved`, `++uib.ItemCount` dalı) — testte satır kaybıyla görüldü; WPF'in kendi panelleri de söker.
- **UIA rolleri** (`UserControlRolePeer`): izde UI Automation yürüyüşü meşgul sürenin **%22,8'i** (2,0 s / 9,2 s), tamamı
  yerleşim sonrası `UpdateSubtree`. WPF kaynağı: denetim türü `Custom` olan peer'ın alt ağacı HER turda baştan sayılır
  (`UpdateSubtree`: `ControlType.Custom == GetControlType()`), `UserControl`'ün varsayılan peer'ı Custom. Uygulamanın 21
  UserControl'ü artık gerçek rol bildirir (satır ListItem, paneller Pane, menüler Menu, başlıklar Header, işaretler Image);
  kaynak guard'ı `AutomationRoleTests`.
- Reveal tetiği generator `StatusChanged`'den `Loaded` kuyruğuna taşındı (koleksiyon değişmese de reveal oynar).

Testler: `ListRowsStayRealizedTests` (kaydırmada kimlik, boşta dolum, dilim boyu, yeniden sıralama, silme, aynı yapı),
`ListReconcilerTests`, `AutomationRoleTests`; `StickyOverlayTests` (Recycling pini → Standard) ve `ProjectListFilterTests`
("tam olarak bir reset" → "reset yok, kalan satır aynı kontrol") yeni kurala göre yeniden yazıldı. ARCHITECTURE §13.2, §15,
§17.2, §22, bilinen sınırlar; README bilinen sınırlar.

Beklenen etki (ölçülecek): koşu içi >100 ms dilimlerin kaynağı kalkar; UIA payı küçülür; metin biçimlemenin satır payı
(148 + 101 ms) kalkar. Kalan: konsol metin biçimleme (aşağıda), olay işleme, GC.

## G3 — koşu sonrası bellek: belgenin arkasında tutulan anlatı (ÖLÇÜLDÜ)

`mem1/app-after-resolve.gcdump` (görünür Clean+Resolve 100 s, boşta, tam GC sonrası) `dotnet-gcdump report` ile tip bazında
toplandı (`gcdump_agg.py`; döküm örneklemelidir, oranlar geçerli): **`System.Char[]` %26 + `AvalonEdit RopeNode<char>` %18 +
`System.String` %10,5** — canlı yığının yarısından fazlası metin, en büyük parça konsol belgesinin ipi. Oysa belge takip
açıkken 200 satıra kırpılır (testte doğrulandı: 1000 satır → ≤ 201). Neden: AvalonEdit her Insert/Remove'u geri-alma yığınına
kaydeder ve kaldırılan metni ip dilimi olarak canlı tutar; `UndoStack.SizeLimit` varsayılanı sınırsız — kırpma hiçbir şeyi
serbest bırakmıyordu.

Yapılan: konsol belgelerinin tek fabrikası `NewDocument` (`UndoStack.SizeLimit = 0`; açılış, anlatı ve proje logu belgeleri).
Test: `ConsoleMemoryTests` (kırmızı: SizeLimit 2147483647 → yeşil). ARCHITECTURE konsol bölümüne madde eklendi.

Gece alınan `heap1/heap-after-resolve.txt` döküm GEÇERSİZ: kilitli masaüstünde Clean (F7, SendKeys) hiç çalışmadı, Resolve 16 ms'de
"187 atlandı" ile bitti; 22 MB'lik "canlı yığın" taze uygulamadır.

Sonraki kaldıraç (ölçüm sonrası karar): anlatının VM tamponu (`_runText`, StringBuilder) + görünümün satır listesi
(`_backlogLines`) + her `Back`'te `ToString()` anlık kopyası = aynı metnin 2–3 kopyası (Resolve'da disk logu 21 MB → bellekte
UTF-16 42 MB/kopya). Tek kopya (VM'de satır listesi, görünüm dilim ister) ~40–60 MB daha; konsol tohumlama yollarının
atomiklik gerekçeleri nedeniyle ancak ölçüm bu payı gösterirse.

## G4 — konsol çizim yükü (ÖLÇÜLDÜ, değişiklik yok)

İzde metin biçimleme 1690 ms (%18,3): AvalonEdit görsel satırları 1142 ms, `TextBlock` 523 ms (249'u liste paneli — G2 ile
kalkar). Konsol payı yeni gelen satır başına bir biçimlemedir (≈ 130 satır/s × 0,3 ms); kırpma görünen satırları yeniden
biçimletmiyor, batch penceresi de satır sayısını değiştirmiyor. Kaldıraç yalnız gösterilen satırı azaltmak (gecikme/okunurluk
kararı) — kazanç belirsiz, dokunulmadı. Finalizer thread'i konusu (DWrite tutamaçları) iz sınıflandırmasıyla kesin değil;
G2+G3 sonrası ölçümde yeniden bakılacak.

## Masaüstü ölçümü — 07.10 sabahı (`after1/`), sonuçlar

Makine sabah dün geceden çok daha yüklüydü: iki Visual Studio, Chrome, Edge webview'ları, üç VS Code, Defender; **Memory
Compression 1,7 GB** (bellek baskısı). Bu yüzden mutlak sayılar dün geceyle değil, **aynı sabah arka arkaya alınan A/B** ile
okunmalı (değişiklik öncesi `786f2f9` Release derlemesi `app_build_orchestrator-ai` worktree'sinden, `BO_MEASURE_APP` ile).

| Ölçüm | Değişiklik öncesi (786f2f9) | Birleşik (satırlar çizilir) | Hidden bandı ile | Not |
|---|---|---|---|---|
| Taze açılış, ön plan boşta: WS / Private | 405 / 315 | 452 / 363 | 415 / 328 | tam GC sonrası aynı; canlı yığın 16,9 → 26,1 → 25,8 MB (kurulu satırların yönetilen payı ~9 MB) |
| Taze açılış, ön plan boşta: M/s | 198–210 | 224–277 | 206–211 | imleçler odağı izlediği için ~200; dün gece 181 |
| Resolve'da >100 ms kare boşluğu (100 pencere) | **640** | **677** | — | ikisi de çöküyor: ortam (bellek baskısı + CPU); dün gece sakin makinede 4 |
| Görünür Rebuild izi, UI thread meşgul | %31 (dün gece) | **%21,3** | — | ≥150 ms dilim: yalnız koşu başı (180 ms); UIA 2107 → 1311 ms; metin biçimleme 1690 → 1132 ms; satır yeniden bağlama dilimleri yok |
| Clean + Resolve sonrası, tam GC: canlı yönetilen yığın | 195,6 MB (dün gece, 100 s koşu) | **118,6 MB** (190 s koşu) | — | G3: ip düğümleri listeden düştü; koşu başına büyüme 417 → 366 MB (1,9× uzun koşuda) |
| Clean + Resolve sonrası WS / Private (tam GC) | 682 / 578 (dün gece) | 748 / 646 | — | sabah ortamı +~115 MB taban (önceki derleme de 405'te); koşu 2× uzun |
| Tepside koşu sonrası boşta | 11,4 (dün gece) | 9,7 M/s | — | F düzeltmesi yerinde |

Okuma: (1) G2'nin UI thread'e etkisi ölçüldü ve olumlu (meşguliyet üçte bir düştü, yeniden bağlama dilimleri kalktı); kare
boşluğu hedefi (4 → 0–1) bu sabah SINANAMADI — değişiklik öncesi derleme de aynı makinede aynı çöküşü veriyor, yani ölçüm
ortamla boğulmuş; sakin makinede tekrar gerekir. (2) G2'nin "tüm satırlar çizilir" bedeli A/B ile ölçüldü (+47 MB, +26 M/s)
ve **Hidden bandı** ile geri alındı (`perf/list-render-band`): viewport'un bir ekran dışındaki kurulu satırlar çizilmez. (3) G3
canlı veriyi %40 düşürdü; kullanıcının gördüğü WS yine de GC'nin elde tuttuğu bölgeler + ortam yüzünden yüksek kalabiliyor.
(4) Finalizer thread'i: izde 19–21 s "RunFinalizers" CPU görünüyor ama altında yönetilen çerçeve yok — büyük olasılıkla
profilleyici artefaktı (VM içinde bekleme), cycle sayacıyla doğrulanmadı; açık soru.

## Masaüstü ölçümü (betik; sakin makinede tekrar için)

```
cd D:\Projects\Other\Apps\app_build_orchestrator
dotnet build BuildOrchestrator.slnx -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File .claude\temp\perf-2026-10-06\measure-all.ps1 -Dir .claude\temp\perf-2026-10-06\after1
```

Sırası: `b4-idle` (ön plan boşta taban çizgisi; imleç odağı izlediği için ~181 M/s beklenir, hedef yok) → `visible-run-trace`
(G2: `slice_breakdown.py`/`uia_breakdown.py` ile >90 ms dilimler, UIA payı) → `resolve-stutter -Priority Normal`
(frames-Normal.log: >100 ms boşluk 4 → hedef 0–1) → `mem-after-resolve` (G3: M1 WS 688 → hedef ≤ ~400; gcdump tip dağılımında
rope payı).
