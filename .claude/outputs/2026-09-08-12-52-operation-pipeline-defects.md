# İşlem akışı: bulunan kusurlar ve düzeltmeler

Şikâyet: *"sync sonunda ilk açılış modu, build dediğimizde animasyon, tekrar build, build sonrası tekrar
build — oralarda da bir akış/animasyon belirledik."*

Bulunan **beş kusur tek kök nedene** bağlıydı: açılış koreografisi doğru yazılmıştı ama **ön koşulları
kurulmamıştı**. Tasarımın borusu `_beginOp → _neutralize → _mark → koşu`; uygulamada `_neutralize` adımı
hiç yoktu ve dalga, önceki koşunun renkleri üzerine yanıyordu.

Her biri için önce kırmızı test yazıldı. Süit: 2161 geçti / 0 kaldı.

---

## 1. `_neutralize` yoktu — önceki koşunun renkleri ekranda kalıyordu ★

**Konum:** [RunViewModel.cs:628](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L628) (eski hâli)

`BeginRunAsync` yalnız `Fresh = false` yapıyordu. Yeni bir Build başladığında önceki koşunun **yeşili,
kırmızısı, grisi, süresi ve dependency uyarısı** satırlarda ve graf düğümlerinde duruyordu; işaretleme
dalgası o renklerin üzerine yanıyordu. Tasarımın cümlesi: *"önceki koşunun tüm izleri silinir — herkes düz
nötr gri."*

**Düzeltme:** `NeutralizeRows(bool fresh)` — statü, süre, dependency uyarısı, döngü hükümleri ve atlama
gerekçesi sıfırlanır. **Plan (`WillBuild`) ve yapısal bilgi (döngü üyeliği, SHA çifti, katman) korunur** —
kapsam plandan okunur ve "neyin bayat olduğu" renk olmadan da SHA'dan okunmalıdır.

Sync'in zaten yaptığı aynı sıfırlama **tek yere toplandı**; iki yol yalnız inilen zeminde ayrışıyor
(`fresh: true` = başlangıç modu · `fresh: false` = düz nötr gri).

**Test:** `A_new_operation_wipes_every_trace_of_the_previous_run`,
`Neutralizing_keeps_the_plan_so_the_marking_wave_still_has_a_scope`,
`A_sync_neutralizes_too_but_lands_in_fresh_mode_instead`

---

## 2. Nötr an hiç görünmüyordu — kapsam tıklama anında amber oluyordu ★

**Konum:** [RunViewModel.cs:198](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L198) +
[RunViewModel.cs:904](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L904)

`queued` statüsü `IsRunning || IsStarting`'den türüyordu. `IsStarting` **tıklama anında** açıldığı için
plandaki her proje o anda kuyruk amberine düşüyordu — koreografinin ilk iki adımı (440 ms nötr an + dalga)
ekranda **hiç yoktu**. Kullanıcının "animasyon yok" dediği şey buydu.

**Düzeltme:** `queued` artık yalnız **gerçekten koşan** bir run'dan türer. Bilgi kaybı yok: dalga tam olarak
kuyruğun aydınlatacağı kümeyi aydınlatır, yalnız anında değil kademeli. Koreografi atlandığında (reduced
motion ya da boş kapsam) kapsam tek adımda işaretlenir, amber yine anında görünür.

Ek olarak: **koşu hiç başlamazsa** (gönderim düştü ya da motor cevap vermedi) işaretler temizlenir —
olmayan bir işlemin amberi ekranda asılı kalamaz.

**Test:** `While_a_run_is_only_requested_the_scope_is_still_plain_grey`

---

## 3. Rebuild, koreografinin ortasında listeyi boşaltıyordu

**Konum:** [RunViewModel.cs:1155](src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L1155) (eski hâli)

`OnRunStarted` içinde `if (e.Mode == RunMode.Rebuild) Projects.Clear();` — dalganın işaretlediği satır
nesnelerini ortasında yok ediyor, liste remount oluyordu (design v1.10 §3.8: *"liste yerinden oynamaz"*).

**Düzeltme:** yerinde nötrleme. Aynı tabana dönülür, satır kimlikleri korunur.

**Test:** `A_rebuild_does_not_empty_the_list_when_the_run_starts`

---

## 4. Dalga grafa yalnız 200 ms'lik koşu tikiyle ulaşıyordu

**Konum:** [MainWindow.xaml.cs:231](src/BuildOrchestrator.App/MainWindow.xaml.cs#L231) (eski hâli)

Kablo yalnız `SetMarking` (opaklık adımı) itiyordu; düğüm **renkleri** statü kanalından gelir ve o kanal
koreografi sırasında yalnız `_elapsedTimer`'ın 200 ms'lik tikiyle tazeleniyordu. 36 projede dalga temposu
~31 ms/node olduğu için **liste akıcı boyanırken graf 6-7 düğümlük bloklar hâlinde sıçrıyordu.** Tasarım
ikisinin senkron olmasını ister.

**Düzeltme:** kablo adlandırılmış bir seam oldu (`ApplyMarkingToGraph`) ve işaretleme grafı **kendi
temposunda** boyuyor.

**Test:** `The_marking_wave_paints_the_graph_at_its_own_tempo_not_the_run_ticks` (MainWindow realize),
`The_wave_tells_the_graph_about_every_single_mark_not_just_every_step` (koreograf yarısı)

---

## 5. Nötrleme önceki koşunun döngü hükümlerini bırakıyordu

Uyarı üçgeninin metnini seçen öncelik sırasında `CycleUnconverged` ve `CycleUnsettled`, `DepIssues`'ın
**üstündedir**. Yalnız `DepIssues` temizlenince yeni işlemin ilk karesinde üçgen hâlâ geçen koşunun
*"did not converge"* gerekçesini anlatıyordu. `SkipReason` ve `CycleWaiting` de koşu sonucudur.

**Düzeltme:** dördü de nötrlemeye girdi. Döngü **üyeliği** (`InCycle`) topolojinin özelliğidir, kalır.

**Test:** `Neutralizing_also_clears_the_previous_runs_cycle_verdicts`

---

## Bir sıra hatası daha

İşlem pill'i (`CurrentOperation`) nötrlemeden **önce** yazılıyordu. Etiketin değişmesi, kabuğun grafa
"statüleri yeniden oku" dediği sinyaldir (başlangıç modunun düşüşü sayaçları hareket ettirmez, o yüzden
sayaca bakan kapı onu kaçırır). Sinyal erken çıktığı için graf **eski renklerle** tazeleniyordu. Yazım
nötrlemeden sonraya alındı. — `The_operation_label_is_written_only_after_the_rows_are_neutral`

---

## Karara bağlanması gerekenler

**1. Reduced motion'da bitiş koreografisi.** Tasarım §2.3 bitiş koreografisi için `prefers-reduced-motion`
altında *"flicker kapalı, opaklık kademeleri kalır"* diyor. Uygulamada koreografi reduced motion'da
**hiç oynamıyor** — çünkü uygulamanın genel kuralı (§1.3, guard testleriyle pinli) "tüm süreler 0".
"Opaklık kademeleri" ~3.7 saniyeye yayılan zamanlanmış bir dizidir, yani sıfır süreyle de hareketttir.
Uygulamanın kuralını bozmamak için **değiştirmedim**; istersen bitiş koreografisi için özel bir istisna
açarız.

**2. Koreografi hâlâ planlama penceresiyle örtüşüyor** (önceki rapor §3.1). Planlama koreografiden kısa
sürerse dalga tam bitmeden koşu devralır — kapsam amber kalır, görüntü tutarlıdır ama tam diziyi her zaman
göremezsin. Tersine çevirmek (koşuyu koreografinin sonuna almak) tek satır.
