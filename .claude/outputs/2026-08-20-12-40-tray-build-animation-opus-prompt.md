# Opus Prompt — Tray Build Animasyonu

> Aşağıdaki metni olduğu gibi Opus'a yapıştır (derlemenin çalıştığı makinede, repo kökünde).

---

Tepsi modunda derleme göstergesini yapacağız. Detaylı uygulama planı şurada:
`.claude/outputs/2026-08-20-12-40-tray-build-animation-plan.md` — önce bu planı OKU, sonra uygula.

## Önemli: plan başka makinede, koddan bağımsız bir oturumda hazırlandı

- Plandaki **satır numaraları YAKLAŞIKTIR** ve kod bu arada değişmiş olabilir. Dosya adları, kalıplar ve
  kararlar (K-1…K-13) bağlayıcıdır; her task'a başlamadan önce ilgili dosyaları güncel haliyle oku ve
  konumları tazele.
- Plan ile güncel kod arasında gerçek bir çelişki bulursan (anılan üye/kalıp yok, davranış değişmiş,
  guard'ın adı/kapsamı farklı) sessizce kendi kararını verme: durumu söyle ve bana sor. Küçük konum/isim
  kaymalarını sormadan güncel koda uyarla.
- **Tasarım deliverable'ı bağlayıcıdır:**
  `.claude/outputs/2026-08-05-05-06-logo-animation-v1.3.0/BuildOrchestratorIconCounter.xaml` (sayaçlı
  sürüm — esas alınacak asset) + aynı klasördeki `README.md` (zamanlama tablosu ve entegrasyon notları).
  İkisini de UYGULAMADAN ÖNCE oku. Zaman çizelgesi ve `ChevronShift`/`SweepShift` senkron kuralı verbatim
  taşınır; SVG/asset'teki hex gradyanlar taşınMAZ — dolgular AppMark'ın token'larıyla değiştirilir (K-8).

## İşin özü

Uygulama tepsideyken (ana pencere gizli) bir derleme koşuyorsa, ekranın sağ alt köşesinde **penceresiz,
arka plansız, odak çalmayan** bir logo animasyonu döner — **logoya tıklamak ana pencereyi geri getirir**
(tray ikonu sol-tıkıyla aynı `ShowFromTray` yolu), logonun etrafındaki şeffaf alan ise tıklamayı alttaki
pencereye geçirir (layered pencerenin per-pixel hit-test'i; `WS_EX_TRANSPARENT` bileREK yok); beyaz şeritteki sayaç
(`139/248`) ribbon'un kullandığı AYNI `fin/wb` değerleriyle canlı ilerler. Derleme bitince animasyon
**mevcut döngünün çıkış evresini tamamlar** (parçalar sağa süzülür, son şeridin yok olduğu 3.000 s
karesinde overlay kapanır) ve **tam o anda** OS balloon bildirimi sonucu söyler. Balloon metni yeniden
compose EDİLMEZ: VM'in o anki terminal ribbon satırı aynen taşınır
(`Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s` / `Stopped — …` / `Run failed — …` /
engine-died metni); healthy→Info, değilse→Error ikonu.

- Görünürlük kuralı: `!pencereGörünür && Phase ∈ {Starting, Running, Stopping}`. Pencere geri gelirse
  overlay animasyonsuz ANINDA gizlenir; run ortasında `X` ile tepsiye inilirse belirir. `Syncing` kapsam
  dışı. Pencere görünürken biten koşu balloon üretmez.
- Reduced motion (OS sinyali) kapalıyken döngü hiç başlamaz: statik işaret + sayaç; bitişte animasyonsuz
  gizlenme + balloon.
- Döngü `Forever` DEĞİL: tek iterasyon + `Completed`'da koşullu yeniden başlatma (K-10) — "çıkışı tamamla"
  davranışının mekanizması bu. Görünmez olunca clock durur; `DesiredFrameRate=30`.
- Marka geometrisi (5 pill + chevron) `Resources/BrandGeometry.xaml`'e TEK KAYNAK olarak çıkarılır;
  `AppMark` ve yeni `TrayBuildIndicator` ikisi de oradan tüketir (geometri tek-kaynak guard'ı yeni
  düzene göre yeniden yazılır — gevşetme değil kaynak taşıma). Sweep kaplaması chevron geometrisinin
  per-instance `Clone()`'u üzerine kurulur (frozen paylaşılan kaynak transform animasyonu alamaz).
- **Supervisor/IPC/Core'a dokunulmaz** — iş tamamen App tarafında. Tray menüsü, `X`→tepsi davranışı,
  ilk-kapanış balloon'u, ribbon mantığı değişmez.
- **Gelecek işi yapma:** tepsideyken kısayolla arka planda build başlatma bu işte YOK; gösterge
  faz-güdümlü olduğu için hazır olacak — yalnız doc'a tek cümle not düşülür.

## Proje kuralları (CLAUDE.md geçerli — özellikle şunlar)

- **Kırmızı test kuralı:** her task'ta önce davranışı pinleyen test KIRMIZI gösterilir, sonra
  implementasyon. Kırmızıyı gösteremiyorsan test yanlıştır.
- **Davranış değişince test yeniden yazılır:** `AppMarkTests` ve geometri tek-kaynak guard'ı sessizce
  gevşetilmez — yeni kuralı (geometri `BrandGeometry.xaml`'de, iki tüketici) pinleyecek şekilde yeniden
  yazılır, gerekçe test doc'una.
- **Yeni XAML kökü = realize testi:** `TrayBuildIndicator` ve `TrayBuildOverlayWindow` için zorunlu
  (mevcut STA/realize kalıbı; `Window.Measure/Arrange` HWND'siz içeriğe inmez — realize `window.Content`
  üzerinde).
- Değişmezler: kopya YASAK (balloon metni ribbon'dan, sayaç değerleri ribbon girdilerinden, geometri tek
  dosyadan, ürün adı `AppIdentity.Product`'tan); renk/süre hardcode kod tarafına yazılmaz (bespoke zaman
  çizelgesi saf-XAML storyboard'da yaşar — K-9'daki bilinçli istisna doc'lanır); toast/in-app popup YASAK
  (bildirim OS balloon'udur); kullanıcıya görünen tüm metinler İNGİLİZCE.
- Motion sözleşmesi (§14.5): yalnız transform+opacity; infinite animasyon görünmezken koşmaz;
  `AnimationsEnabled` animasyon başlangıcında TAZE okunur, `AnimationsEnabledChanged` canlı dinlenir.
- Doküman aynı işte güncellenir (plan T7: ARCHITECTURE §12.2 "AllowsTransparency is never used"
  cümlesinin ana pencereye daraltılması DAHİL, §12.3, §14.4, §14.5, §22 + README; anlatı üslubu,
  changelog dili yok, bayatlayacak ölçü/rakam gömme yok).

## Çalışma şekli

1. İş branch'i aç (`feature/tray-build-indicator` gibi).
2. Task sırası: **T1 ‖ T2 → T3 → T4 → T5 → T6 → T7** (plandaki bağımlılık şeması). Task başına commit.
3. Her task: önce kırmızı test(ler) → implementasyon → o task'ın süiti yeşil → commit.
4. Sonda tam süit:
   `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
   (uygulama açıkken build alma — çalışan Supervisor kendi binary'lerini kilitler). Ardından T6'daki elle
   doğrulama listesini benimle birlikte koş (uygulamayı sen başlat, senaryoları söyle, ben bakarım).
5. main'e merge + push; merge doğrulandıktan sonra branch'i local + remote'tan sil; oturumu main'de bitir.

## Sapma noktaları (bana sormadan değiştirme)

- Bitiş koreografisi: overlay YARIM döngüde kesilmez — çıkış evresi tamamlanır, kaybolma anında balloon
  (tek-iterasyon + `Completed` mekanizması; seek/hız değiştirme yolu SEÇİLMEZ).
- Balloon metninin ribbon satırından aynen taşınması (yeni özet formatlayıcı YAZILMAZ) ve balloon'un
  yalnız tepsideyken yakalanan terminalde gösterilmesi.
- Tıklama modeli: logo pikselleri tıkla-aç (`RestoreRequested` → `ShowFromTray`, tray ikonuyla AYNI
  handler), şeffaf pikseller per-pixel geçirgen; `WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE` var,
  `WS_EX_TRANSPARENT` YOK (eklenirse logo tıklanamaz olur); tam-dikdörtgen görünmez hit alanı
  (`#01000000` zemin hack'i) YAZILMAZ. `ShowActivated=false` — overlay odak çalmaz, Alt-Tab'da görünmez.
- Geometri tek-kaynak yaklaşımı (`BrandGeometry.xaml`). Uygulanamaz çıkarsa (ör. WPF kaynak/freeze
  engeli) alternatife kendin geçme — bana sor.
- SVG gradyanlarının port edilmemesi; dolguların AppMark token kümesi olması. Sayaç metni için ramp'te
  karşılık yoksa token ekleme kalıbı serbest, hex'i kontrole gömmek YASAK.
- Reduced-motion davranışı (statik işaret + sayaç; döngü yok) ve `Syncing`'in kapsam dışı olması.
- Supervisor/IPC/Core'a ve tray menüsüne dokunmama kararı; arka-plan build kısayolunun bu işte
  yapılmaması.
