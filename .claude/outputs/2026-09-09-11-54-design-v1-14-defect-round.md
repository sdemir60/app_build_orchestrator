# Tasarım v1.14.0 aktarımı — kusur turu (kullanıcı testi sonrası)

> Branch: `feat/design-v1.14`. Kullanıcı gerçek uygulamada beş sorun bildirdi. Bu döküm, her birinin
> kodda bulunan kök nedenini ve düzeltme kararını kaydeder. Otorite: tasarım paketi README §9 sürüm
> notları (kullanıcı kuralı: "yeni sürüm notlarında ne yazıyorsa hedef konularda o doğrudur").

## Önceki turun dersi

Altı task da birim/realize testleriyle "yeşil" kapandı ama dört davranış gerçek uygulamada çalışmıyordu.
Ortak neden: testler VM'in ya da kontrolün **kendi** davranışını pinledi, **kabuk kablajını** (MainWindow →
ConsoleView / graf) ve **motor zamanlamasını** görmedi. Bu turda her düzeltme çalışan uygulamada gözle
doğrulanır (ekran görüntüsü), test yalnız pin içindir.

## K1 — Konsol ve event stream Build/Sync'te temizlenmiyor

**Kök neden:** `RunViewModel.ClearConsoleForNewOperation` yalnız VM tamponlarını (`_runText` vb.) siliyor.
Ekrandaki AvalonEdit belgesi **yalnız** `ConsoleView.ShowRunDocument` ile yeniden kurulur ve o yalnız
seçim→anlatı dönüşünde çağrılır (`MainWindow.ShowRunConsole`). İşlem başlangıcında kabuk konsola hiç
dokunmuyor (`OnVmPropertyChangedForGraph` yalnız grafı itiyor); yeni satırlar pompadan eski metnin
**altına** ekleniyor. Event stream'in `RemoveAt` yolu görünüşte doğru — uygulamada doğrulanacak.

**Düzeltme:** VM temizliği bir kabuk kancasıyla ekrana taşınır: `ClearConsoleForNewOperation` tamponu
sildikten sonra bayat pompa batch'lerini düşürür (`_console.PostReseedDrop()`, `SeedRunDocument`'ın yaptığı
gibi) ve `ConsoleReset` kancasını çağırır; `MainWindow` bu kancada — anlatı modundaysa — belgeyi
**animasyonsuz** sıfırlar (`ShowRunDocument`'ın tilt'siz çekirdeği). Proje-log modundayken belge dokunulmaz,
`← Back` zaten taze belgeyi kurar.

## K2 — Sync'te liste başa dönmüyor (ve reveal oynamıyor)

**Kök neden:** `RunViewModel.OnWorkspaceTopology` yapı imzası değişmedikçe `TopologyChanged` ateşlemiyor
(A13/B3·E4 guard'ı, "gereksiz churn"). Aynı repoda ikinci Sync → reveal yok → `PlayRevealStagger` (scroll'un
bağlandığı yer) hiç koşmuyor. Guard'ın kendi doc'u prototiple bilerek ayrıştığını kaydediyor; **tasarım
v1.13.2 bu ayrılığı kapatıyor** (Sync = revealKey artışı: graf reveal'i yeniden oynar, liste başa döner).

**Düzeltme:** bir Sync'ten gelen topoloji (`_syncInFlight`) imza aynı olsa da `TopologyChanged` ateşler →
liste reveal + scroll 0 (seçim zaten Sync'te düşer) + graf reveal/kamera. İmza guard'ı **Sync dışı**
yayınlar için kalır. Karakterizasyon testi `A_no_changes_sync_neither_resets_the_list_nor_replays_the_reveal`
yeni kurala göre yeniden yazılır (eski iddia + gerekçe doc'unda).

## K3 — Build koreografisi: "sönüş ve akış garip"

**Kök neden:** sabitler prototiple birebir (440/W/740+W/…/2520+W, 0.45/0.18, 440/1120). Fark **bitişte**:
`OperationChoreographer.Finish` doğal bitişte `Step=None` yazıp grafı 1.0 opaklığa **geri getiriyor**;
komut o zaman gönderiliyor, motor planlama yapıyor (saniyeler), `runStarted` gelince node'lar **tekrar**
0.13/0.2'ye sönüyor. Prototipte `fn()` (startRun) koreografinin son anında çalışır: settle(0.45/0.18) →
running(1/0.13/0.2) **tek geçiş**, geri gelme yok.

**Düzeltme:** doğal bitişte koreografi son adımının (Wait2) opaklıklarını **tutar** (Step sıfırlanmaz),
yalnız bekleyen komutu serbest bırakır. `runStarted` (`IsRunning`) geldiğinde kabuk önce koşu fazını ve
statüleri grafa iter, **sonra** `Cancel/ClearMarks` — böylece tek geçiş: settle → running. Komut gönderimi
düşerse (`!IsStarting`) yine `Cancel` ile 1.0'a dönülür.

## K4 — What's new penceresi tasarımla uyuşmuyor

**Bulunan:** `NotesDialog.BuildVersionBlock`'ta `INSTALLED` çipi `DockPanel`'in SON çocuğu → `LastChildFill`
ile satırın kalan genişliğine **yayılıyor** (sürüm satırında kocaman bir kutu). Prototipte çip numaranın
yanında içeriğe sıkı; boşluğu esnek ayraç doldurur. Ayrıca sürüm notu içeriği bayat ("release notes now
live in this window" — artık ayrı pencere). Diğer farklar **görsel karşılaştırmayla** bulunacak: prototip
(tarayıcı) ve WPF (render) ekran görüntüleri yan yana.

**Düzeltme:** çip içeriğe sıkı; başlık/gövde ölçüleri prototiple satır satır eşlenir; sürüm notu içeriği bu
turun değişikliklerini anlatacak şekilde güncellenir.

## K5 — Harici projeler UI'ı hiç aktarılmamış

**Neden:** kullanıcı kararı yanlış okundu — "arkasını sonra yazacağız" motoru kastediyordu, tasarım (Settings
bölümü) şimdi istenmişti.

**Düzeltme (v1.14.0 §9 + prototip `SettingsDialog` :1690-1890 birebir):** Settings'te Workspace →
**External projects** → Layers sırası; kart = grip + mono path input + 96px `Source` (`Git`/`TFVC`,
varsayılan Git) + sil; başlık satırı `PROJECT PATH` / `SOURCE`; boş durum kesikli kutu; `Add external
project`; boş path varken Save disabled; kalıcılık (UiState), export/import/clear (`externalProjects`,
import düz string dizisini de kabul eder, eksik `vcs` → git), import geri bildirimi
`Imported — N layers · M external · root set`, Save'de sayı değiştiyse konsol notu. **Motor bağlantısı yok**
(IPC/Core'a dokunulmaz; o iş `feat/external-projects-prebuild` ile gelecek) — liste/graf grubu da motor
verisi olmadan çizilemez, bu turda yok.

## Doğrulama

Her K için: kırmızı test (seam nerede izin veriyorsa) → fix → yeşil; sonra **uygulama çalıştırılıp ekran
görüntüsüyle** davranış gözlenir. Bitişte tam süit yeşil; ARCHITECTURE.md/README.md yanlış kalan cümleler
yerinde düzeltilir.

## Kararlar (bu turda verilen ruling'ler)

- **K2 — Sync her zaman reveal'i yeniden oynatır.** İmza guard'ının koruduğu "mid-run Sync" durumu ulaşılabilir
  değildi (Sync koşarken kilitli, motor topolojiyi yalnız Sync içinde yayınlıyor); tasarım v1.13.2 notu kazandı.
  Guard Sync dışı yayınlar için savunma olarak duruyor.
- **K3 — Koreografi doğal bitişte son adımını tutar.** Prototipte `startRun()` koreografinin son anında çalışır;
  gerçek motorun planlama penceresinde grafı 1.0'a geri getirmek "çift sönüş" üretiyordu. Kabuk `runStarted`'da
  önce koşu fazını iter, sonra koreografiyi düşürür.
- **K1 — Temizlik seçimden önce gelir.** Seçim düşünce kabuk anlatı belgesini yeniden kuruyor; temizlik sonra
  gelseydi eski metin tilt'le gelip hemen silinirdi. `BeginRunAsync` ve `SyncCoreAsync` aynı sırayı izler
  (`ClearSelectionAndFilter` bu yüzden komutlardan `BeginRunAsync`'e taşındı).
- **K5 — Motor bu turda yok.** Harici proje listesi App'te kalıcı tutulur, Save'de uygulanır, motora gitmez;
  kod ve doküman bunu açıkça söyler. Motor `feat/external-projects-prebuild` ile gelecek.
- **K5 — Bölüm ayracı 18/16.** Prototip `margin: '18px 0 16px'`; uygulamadaki 18/18 önceki turdan kalan bir
  ayrışmaydı — paylaşılan `Ds.Settings.SectionDivider` prototipe çekildi (Workspace→Layers ayracı da değişti).
- **K5 — Export alan sırası** `repositoryRoot → externalProjects → layers` (brief + prototip `doExport`).
- **Kırmızı test kuralı — yeni davranış.** Bir KUSUR düzeltilirken kırmızı zorunludur (test kusuru gerçekten
  yakalamalı: K1-K4 ve K5'in export sırası böyle gösterildi). Sıfırdan yazılan bir özelliğin testinde "kırmızı"
  özelliğin yokluğudur; kodu geri sarıp göstermek gösteri olurdu — K5'in ~30 yeni testi için tek yeşil koşu
  kabul edildi, self-review'da yakalanan üç test hatası raporda kayıtlı.
- **Doğrulama yöntemi.** K1-K4 çalışan uygulamada UIA ile sürülüp ekran görüntüsüyle doğrulandı; K5 ekran
  kilitli olduğu için test host'unda ekran-dışı render (RenderTargetBitmap) ile doğrulandı — canlı uygulamada
  açılır liste (popup) görülmedi, realize testleri kapsıyor.
