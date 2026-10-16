# Boşta bellek analizi — "derleme bitti, uygulama boşta, RAM ~1 GB"

**Tarih:** 2026-10-13 13:25–13:45 · **Kod:** `develop` `490880ee` (v1.9.0 + graf bantları) · **Ortam:** kullanıcının OSYS
workspace'i (195 proje), Release bin'den açılan uygulama, pencere görünür, masaüstü kilitsiz · **Araçlar:**
`.claude/temp/mem-2026-10-13/` (`mem-after-build.ps1`, `mem-tray-run.ps1`; `mem-regions.ps1` VirtualQueryEx bölge
dökümü; ClrMD `HeapTool` tip bazlı yığın dökümü; `dotnet-gcdump` zorlanmış tam GC; `dotnet-counters` GC committed) ·
Ham çıktılar `run1/` ve `run2/` altında. Önceki ölçüm: `2026-10-06-22-38-memory-fans-resolve-stutter.md` §4.

## 1. Tek cümle

Boştaki ~1 GB = **App ~800 MB + Supervisor ~50–120 MB**; App'in 800 MB'si **koşu metninin bellekteki iki canlı kopyası
(~115 MB) + toplanmamış çöp (~170 MB) + GC'nin elinde tuttuğu boş bellek (LOH boşlukları + boş bölgeler, ~250 MB) +
~270 MB WPF/CLR native taban**'dan oluşuyor ve pencere görünürken koşu bittikten sonra **hiçbir şey toplanmadığı**
için düşmüyor. Şüphe doğru: loglar bellekte tutuluyor — ama tek sebep bu değil. **Kanıt:** aynı Rebuild tepside bitince
(aracın mevcut agresif toplama yolu, koşu 4) App **444 MB**'de kalıyor; 798 ile 444 arasındaki fark yalnız "toplama
yapıldı mı" sorusundan geliyor.

## 2. Ölçüm — koşu 1: görünür pencerede biten Rebuild

Koşu `run-20261013-132620-670`: 195 proje (193 başarılı, 2 başarısız — `OSYS.Orchestration.Service.Report` ve
`OSYS.Orchestration.SparePart.Finance`, kullanıcının 12–13 Ekim koşularında da aynı iki proje düşüyor), 220,8 s,
**196 log dosyası, 83.039 satır, 27,5 M karakter = 52,5 MB UTF-16**, ortalama satır 332 karakter.

| An | App WS / Private (MB) | GC yığını, kullanımdaki bölgeler (MB) | HeapTool: nesneler | Supervisor WS / Private |
|---|---|---|---|---|
| S0 taze açılış, 45 s boşta | 401 / 313 | 40 | 32,6 MB · 441 k nesne | 120 / 83 |
| R koşu sürerken (+30 s) | 607 / 508 | – | – | – |
| **S1 koşu bitti, +60 s boşta** | **898 / 798** | **404** | 400 MB · 1,37 M nesne: String 152 · Free 122 · Char[] 64 · `ProjectLogEvent` 4 (82.791 adet) | 95 / 47 |
| S2 zorlanmış tam GC (gcdump), +5 s | 847 / 746 | 235 | 230 MB · 576 k nesne: **Free 82 (tamamı LOH) · String 59 (107 k) · Char[] 55 (5 k)** · UI ~34 | – |
| S3 +3 dk boşta | 847 / 746 | – | – | – |

Okuma: koşu bittikten 60 s sonra kullanıcının gördüğü rakam **898 + 95 ≈ 1 GB** birebir çıktı. Zorlanmış sıradan bir
tam GC 170 MB çöp topladı ama private yalnız 52 MB düştü (GC boşalan bölgeleri elinde tutar); sonraki 3 dakikada tek bayt
değişmedi.

## 3. 798 MB'nin dağılımı (S1)

| Parça | ≈ MB | Kanıt |
|---|---|---|
| **Native taban** (CLR+JIT, WPF/D3D, DWrite/font, AvalonEdit) | **~270** (taze) → **~330–360** (koşu sonrası) | koşu 2 V0: private 306 − GC committed 38 = 268; pencere tepsideyken de aynı (H0: 304). Koşu 4 T1: 444 − 85 = 359 — bir koşu native'i ~90 MB büyütüyor (nedeni ölçülmedi, §5-E) |
| GC yığını kullanımda | 404 | HeapTool segment toplamı |
| ├ canlı kopya 1: `RunViewModel._runText` (StringBuilder, TÜM projelerin TÜM satırları) | 55 | S2 Char[] 55 MB / 5.003 parça (27,5 M karakter) |
| ├ canlı kopya 2: `ConsoleView._backlogLines` (aynı metnin satır listesi) | 59 | S2 String 59 MB / 107 k string (83 k satır) |
| ├ LOH boşlukları (sıkıştırılmayan büyük nesne yığını) | 82 | S2 "Free" 82 MB, tamamı LOH |
| ├ diğer canlı (satırlar, graf, animasyon saatleri) | ~34 | S2 kalan |
| └ **çöp** (3. kopya `_liveLines` 82.791 olay + metin biçimleme artıkları + batch stringleri) | ~170 | S1 404 → S2 235 |
| GC'nin elinde boş (committed ama kullanılmayan) bölgeler | ~50–100 (S1) → ~170–210 (S2) | private − kullanımdaki − native |

Supervisor: taze 83 MB private'ın 70 MB'si GC yığını — açılış Sync'inin kaynak özet defteri (25 k `SourceHashCache.Entry`,
128 k string) + 16 MB LOH boşluğu; koşu sonunda 47 MB'ye iniyor (defter koşu dışında tutulmuyor). Asıl sorun değil,
ama 8 GB'lik makinede 50–120 MB da sayılır.

## 4. Neden düşmüyor — mekanizma

1. **Boşta GC olmaz.** Uygulama boştayken neredeyse hiç tahsis yapmaz → GC tetiklenmez → çöp (170 MB) ve boş bölgeler
   öylece durur (S2→S3: 3 dk, sıfır değişim). .NET'in bölge GC'si boş bölgeleri yalnız SONRAKİ toplamalarda kademeli
   geri verir; toplama yoksa geri verme de yok.
2. **Agresif toplama yalnız tepsi yolunda.** `MainWindow.HiddenSurface.cs:103` `GC.Collect(2, Aggressive, blocking,
   compacting)` var ama `CollectAfterRunWhenDue` (satır 159–170) yalnız koşu TEPSİDE bittiyse çağırır; pencere
   görünürken hiç toplanmaz (`RunBufferReleaseTests.A_visible_window_does_not_collect_after_a_run`, satır 244 bunu
   pinler). Kullanıcı derlemeyi izlerken bu yol hiç çalışmaz.
3. **Metin kopyaları tasarım gereği canlı.** `RunViewModel.cs:439` `_runText` her `projectLog` satırını ekler
   (`OnProjectLog`, satır 2864–2888: "proje modunda bile birikir"); `ConsoleView.xaml.cs:107` `_backlogLines` 200
   satırlık render diliminden kırpılan her satırı ayrıca saklar (satır 300–308; tohumlamada satır 653 tüm metni
   yeniden böler). Bir sonraki işlem başlayana dek ikisi de durur (ARCHITECTURE §13.5 "backlog" + "view-model's full
   run buffer"). Üçüncü kopya `_liveLines` (satır 441) koşu bitince bırakılır ama toplanmadığı için bellekte kalır.
4. **LOH boşlukları.** Konsol batch'leri 0,2–0,7 MB'lik tek string'ler (S1'de 0,72 MB'lik 3 string görüldü),
   tohumlamada `_runText.ToString()` 55 MB'lik tek string: hepsi LOH'a düşer, ölünce yerleri sıkıştırılmaz → 82 MB delik.
5. **Native taban ~270 MB** pencereye bağlı değil (gizli: 304, görünür: 306) — WPF/D3D cihazı, DWrite, JIT kodu, CLR.
   10-06'daki taze ölçüm (private 148–200) ile fark +110 MB; bir koşu da native'i ~90 MB büyütüyor (koşu 4). Ayrı bir
   soru (§5-E).
6. **Pencere geri gelince kopya geri geliyor.** Koşu 4'te tepside biten koşudan sonra App 444 MB'deyken pencere
   gösterilince 571 MB'ye çıktı (GC committed 85 → 207): konsol belgesi `_runText.ToString()` (47 MB'lik tek string,
   LOH) ile yeniden kuruluyor ve `_backlogLines` listesi baştan üretiliyor — ikinci kopyanın bedeli tek başına ölçüldü.

## 5. Çözümler — en sağlıklıdan başlayarak

### 5.0 Bakış açıları (aynı soruya yedi yerden bakış)

| Bakış açısı | Soru | Bulgu | Kurumsal yöntem |
|---|---|---|---|
| **Ürün / kullanıcı** | Canlı konsolda 83 bin satırı kim okuyor? | Kimse canlı okumuyor; hata/uyarı/özet okunuyor, ayrıntı için proje logu zaten diskte ve tıklayınca açılıyor | Visual Studio, Rider ve CI konsolları gibi: canlı akış kısa (minimal/normal), ayrıntı dosyada (**D**) |
| **Mimari** | Veri nerede yaşamalı? | Aracın kendi ilkesi "the disk is the log" (§8.5) — ama anlatı RAM'de 2–3 kopya | Sınırlı bellek tamponu + diske yayılmış geçmiş; hiçbir özellik log hacmiyle orantılı veri tutmaz — "bellek bütçesi" tasarımı (**B**, **C**) |
| **.NET çalışma zamanı** | Bellek ne zaman geri verilir? | Boşta GC olmaz → hiç; sıradan GC de LOH'u sıkıştırmaz, boş bölgeleri geri vermez | Doğal sakin anda bilinçli "trim" noktası: `GCCollectionMode.Aggressive` tam da bunun için eklendi (**A**); LOH churn'ünü azalt (batch string'lerini parçala); ayar denemesi (**F**) |
| **İşletim sistemi / zayıf makine** | Takım arkadaşı ne görüyor, neyi ödüyor? | Görev Yöneticisi private working set gösterir, App + Supervisor'ı gruplar; derleme SIRASINDA asıl yük MSBuild işçileri (işçi başına 300–700 MB) | Zayıf makinede Balanced/Lite profil (işçi sayısı boş belleğe göre, §11.1); boşta hedef = taze açılış seviyesi; pencereyi gizlemek bellek kazandırmıyor (H0) ama CPU'yu 20× düşürüyor |
| **Native taban** | 270 MB nereden geliyor? | WPF/D3D cihazı, DWrite, JIT, CLR; pencereye bağlı değil; 10-06'ya göre +110 MB | Ayrı ölçüm işi (**E**): ReadyToRun, ara yüzey/efekt envanteri, ETW ile atıf |
| **Gözlemlenebilirlik** | Nasıl izlenir, nasıl gerilemez? | Motorun `memory:` satırı var (§11.4), App'in yok; bellek için test yok | App'e aynı satır + Diagnostics raporuna bellek bölümü; **bütçe testi**: sentetik 100 k satırlık koşu → trim sonrası canlı yönetilen bellek ≤ taze + X MB; tip bazlı guard (log hacmiyle büyüyen tampon kalmadı) |
| **Risk / sıra** | Neyi önce? | A küçük ve geri alınabilir, B orta, C büyük, D ürün kararı | Her adım kırmızı testle (repo kuralı): **A → B → D kararı → C → E** |

| # | Öneri | Beklenen kazanç (bu koşuda) | Risk / bedel |
|---|---|---|---|
| **A** | **Koşu bitince boşta tek agresif toplama — pencere görünürken de.** Mevcut `CollectMemory` kullanılır; `ApplicationIdle`'a ertelenir, final animasyonu bittikten ve koşu uçuşta değilken bir kez. Supervisor'da da koşu/Sync sonunda aynı çağrı (UI yok, duraklama görünmez). | **Ölçüldü (koşu 4, aynı kod, tepsi yolu): App 798 → 444**, GC committed 404 → 85 MB, LOH boşluğu 0. Görünür pencerede backlog kopyası da canlı kaldığı için ~500 beklenir. Sup 83 → ~45. | ~400 MB yığında 100–300 ms tek duraklama (ölçülmeli); test `A_visible_window_does_not_collect_after_a_run` yeni kurala göre yeniden yazılır. En küçük değişiklik, en hızlı kazanç. |
| **B** | **Metnin tek kopyası.** Konsol backlog'u `_runText`'in kopyasını tutmasın: VM satır listesini tek yerde tutsun (ya `List<string>` satırlar ya da parça tabanlı tampon), `ConsoleView` dilimi oradan istesin. `_liveLines` yalnız dikişin gerektirdiğini (açık proje sayfasının snapshot sonrası satırları) tutsun, tüm projelerin metnini değil. | canlı −59 MB (+ koşu sırasında −60 MB tepe); A ile birlikte **~370** | Orta; davranış değişmez (aynı backlog, aynı dikiş). Realize/VM testleri. |
| **C** | **Anlatıyı diske yaz, konsolu diskten sayfala** ("the disk is the log" ilkesinin anlatıya uygulanması). Supervisor ya da App koşu anlatısını `run-<ts>/narrative.log`'a akıtır; bellekte yalnız 200 satırlık pencere + satır indeksi kalır; yukarı kaydırmada chunk loader (proje logu için zaten var, §5.5) diskten okur. | canlı −114 MB; A ile birlikte **≈ taze seviye (~300–330)** | Büyük: §13.5/§8.5 tasarım kararı, "Back" ve mod geçişi akışları diske dayanır. Kalıcı ve ölçeklenen çözüm. |
| **D** | **Hacmi kaynağında azalt:** MSBuild'e `-clp:Verbosity=minimal` (canlı akış) + `-flp:LogFile=<proje logu>;Verbosity=normal` (diskteki tam log). 83 k satır yerine birkaç bin satır akar. | Metin kopyaları, IPC, metin biçimleme CPU'su ve LOH baskısı ~10× düşer | Ürün kararı: canlı konsol yalnız hata/uyarı/özet gösterir, proje logu sayfası tam kalır; §9.2 argüman sözleşmesi ve §5.5 değişir. |
| **E** | **Native tabanı araştır** (~270 MB; taze uygulamanın %88'i). Adaylar: JIT kod/loader yığınları (ReadyToRun publish ile küçülür, açılış da hızlanır), WPF ara yüzeyleri (Opacity<1 / Effect taşıyan büyük paneller), DWrite/font önbellekleri, AvalonEdit. 10-06'ya göre +110 MB artış da burada. | Bilinmiyor; 50–100 MB mümkün | Ayrı ölçüm işi (ETW VirtualAlloc atfı, ReadyToRun A/B). |
| **F** | **GC ayarı denemesi (yardımcı, tek başına çözüm değil):** `System.GC.ConserveMemory=5–7` (parçalanma yüksekken gen2/LOH'u kendiliğinden sıkıştırır). | LOH boşluğunun bir kısmı | Tahsis yoğun koşuda GC maliyeti artar; A varsa gereksiz. |

**Yapılmaması gerekenler:** `GCHeapHardLimit` (OOM riski, hız kaybı), Server GC (daha ÇOK bellek), `EmptyWorkingSet`/
`SetProcessWorkingSetSize` hilesi (Görev Yöneticisi'ndeki sayı düşer, private bellek düşmez, ilk dokunuşta sayfalar geri
gelir), konsol geçmişini sessizce kısaltmak (§13.5'in "backlog ulaşılabilir" kuralını bozar — C bunu diske taşıyarak korur).

**Önerilen sıra:** A (hemen, küçük) → B (orta) → D'yi kullanıcıyla karar ver → C (kalıcı) → E (ayrı araştırma).
Görünür pencerede 83 k satırlık koşudan sonra beklenen App private: bugün 798 → A ile ~500 → A+B ~445 → A+C ~390
(≈ koşu sonrası native taban + UI); D tüm metin parçalarını ~10× küçültür; onun altı yalnız E ile iner.

## 6. Koşu 2: pencere gizli/görünür ve tepside biten koşu (agresif GC yolunun bugünkü kazancı)

| An | App WS / Private (MB) | GC committed (sayaç) | Not |
|---|---|---|---|
| V0 taze, görünür, 45 s | 392 / 306 | 37,8 | native ≈ 268 |
| H0 tepsiye alındı (UIA Close → Close to tray), +25 s | 392 / 304 | 38,1 | gizlemek native'i düşürmüyor; UI thread 234 → 13 Mcycles/s |
| V1 yeniden gösterildi, +15 s | 414 / 327 | 38,1 | dönüş Sync'i + yeniden kurulum |
| **T1 Rebuild tepside bitti, +60 s** (koşu 4, `run-20261013-134849-009`) | **541 / 444** | **84,8** (parçalanma ~0, LOH 2 MB) | agresif toplama yolu çalıştı: yığın 86 MB = taze 33 + `_runText` 47 (Char[]); backlog yok (gizliyken belgeye yazılmaz); Sup 92 / 46 |
| T2 pencere yeniden gösterildi, +20 s | 670 / 571 | 207 | konsol `_runText.ToString()` + satır bölme ile yeniden kuruldu: +127 MB private |

Koşu 2 ve 3'te Rebuild başlatılamadı: pencere aktif değilken (ön planda VS Code) WPF POST edilen F6'yı yok sayıyor, tepsiden
dönüşte ise dönüş Sync'i koşu kapısını kapatıyor (`WhyRunCannotStart` → `SyncInProgress`). Koşu 4 `WM_ACTIVATE` +
`WM_SETFOCUS` POST edip ardından F6 göndererek başladı (`mem-tray-run3.ps1`).

## 6b. Kazanç merdiveni — boşta toplam nereden nereye iner

Boşta = tam bir Rebuild bittikten sonra, hiçbir şeye dokunulmadan. Rakamlar private bayt (Görev Yöneticisi'nin
gösterdiği "Bellek" private working set'tir ve paylaşımlı DLL sayfalarıyla ~100 MB daha yüksek görünür).

| Aşama | App | Supervisor | **Toplam** | Dayanak |
|---|---|---|---|---|
| Bugün, pencere görünür | 798 | 47 | **~845** (Görev Yön. ~990) | koşu 1 S1 |
| Bugün, koşu tepside bitti | 444 | 46 | ~490 | koşu 4 T1 (mevcut agresif toplama yolu) |
| **A** agresif toplama görünür pencerede de | ~500 | ~45 | **~545** | 444 + backlog kopyası 59 |
| **A+B** metnin tek kopyası | ~445 | ~45 | **~490** | −59 |
| **A+B+C** anlatı diskte (ya da A+D canlı akış minimal) | ~390 | ~45 | **~435** | −55; kalan ≈ koşu sonrası native 330 + UI 35 + küçük tamponlar |
| **+ motor boşta kapanır** (N dk işsiz Supervisor'ı kapat, komutta yeniden başlat — §4.6 yeniden başlatma zaten var) | ~390 | 0 | **~390** | Supervisor 1–2 s'de ayağa kalkar, defterler diskte |
| **+ pencere kapalıyken UI süreci de kapanır**, yalnız tepsi ajanı kalır (ikon, kısayol, balon, güncelleme kontrolü) | ~25–60 | 0 | **~25–60** | WPF'siz tepsi ajanı ~20–30 MB, WPF'li ~50–60; pencere açılınca UI süreci ~1 s'de doğar ve yukarıdaki merdivende yaşar |
| **E** native taban (ReadyToRun, yüzey/efekt envanteri) | −50…−100 | – | pencere AÇIKKEN de alt sınırı indirir | ayrı ölçüm |

Okuma: **yalnız uygulama içi yöntemlerle (A–D) boşta toplam ~845'ten ~435 MB'ye iner; pencere açıkken bu stack'in alt
sınırı ~400 MB'dir** (taze açılış bile 306 + 47). "Yok denecek kadar" (birkaç on MB) **yalnız süreç ayrımıyla** olur: boşta
ağır süreçler (UI, motor) yaşamaz, tepside yalnız küçük bir ajan durur. Kullanıcı ayarları zaten bu kullanıma uygun
(`StartMinimizedToTray` ve `CloseToTray` açık): tepside bekleyen bir aracın WPF penceresini canlı tutmasının gerekçesi yok.

**"Daha önce denedik, faydası olmadı" — neden:** 2026-10-06'daki deneme (görünür koşu sonrası arka plan GC,
`BackgroundMemoryCollector`) ölçülebilir kazanç vermeden geri alındı; o gün tam GC yalnız 5 MB verdi. Sebep GC değil,
**veri canlıydı**: AvalonEdit'in sınırsız geri-alma yığını anlatının tamamını tutuyordu (o oturumda bulunup kapatıldı,
§13.5), VM tamponu ve backlog kopyası da öyle — GC canlı veriyi bırakamaz. Bugünkü fark: (1) bugünkü kodda **agresif
sıkıştıran** toplamanın kazancı ölçüldü (798 → 444), tahmin değil; (2) plan canlı kopyaları da kaldırıyor (B/C/D), yani
toplamanın bırakabileceği şey büyüyor; (3) **bütçe testi** (sentetik 100 k satırlık koşu → trim sonrası canlı yönetilen bellek
≤ taze + X MB) kazancın gerilemesini engeller — önceki denemenin eksik olan parçası buydu.

## 7. Yeniden üretim

```powershell
dotnet build src\BuildOrchestrator.App\BuildOrchestrator.App.csproj -c Release   # uygulama kapalıyken
.\.claude\temp\mem-2026-10-13\mem-after-build.ps1 -Dir .\.claude\temp\mem-2026-10-13\runN   # ~10 dk
.\.claude\temp\mem-2026-10-13\mem-tray-run3.ps1   -Dir .\.claude\temp\mem-2026-10-13\runM   # ~7 dk (tepsi yolu)
.\.claude\temp\mem-2026-10-13\mem-tray-run.ps1    -Dir .\.claude\temp\mem-2026-10-13\runK   # gizli/görünür karşılaştırması
```

- Rebuild, ana pencereye POST edilen `WM_ACTIVATE` + `WM_SETFOCUS` + `WM_KEYDOWN/WM_KEYUP F6` ile başlatılır — gerçek
  odak/SendKeys gerekmez; pencere aktif değilken sahte odak olmadan F6 yok sayılır. Tepsiden gösterdikten sonra dönüş
  Sync'i bitene dek koşu kapısı kapalıdır. Tepsiye alma UI Automation `WindowPattern.Close` (Close to tray açık), geri
  gösterme ikinci örnek başlatma (`SingleInstance` → `ShowFromTray`).
- `HeapTool` (ClrMD, `scratchpad/heaptool`) yığındaki TÜM nesneleri sayar — ölüler dahil. "Canlı" için önce bir GC
  gerekir: `dotnet-gcdump collect` zorlanmış tam GC yapar (S2 bu yüzden var). Sıradan tam GC LOH'u sıkıştırmaz ve
  bölgeleri geri vermez; `GCCollectionMode.Aggressive` ikisini de yapar.
- GC committed'ın kesin değeri `dotnet-counters collect --counters System.Runtime` →
  `dotnet.gc.last_collection.memory.committed_size` (son toplamanın değeri; boşta bayatlar).
