# Design system sürümleri

Design system (bu klasör) prototipin ve uygulamanın görsel kaynağıdır; *Build Orchestrator - Design System* artifact'i gezilebilir kopyasıdır. Klasördeki her değişiklik burada bir sürümdür, en yenisi en üstte: ne değişti, neden, uygulamaya ne düşüyor. Numaralama: bir bileşen, token, varlık veya marka kitabı kuralı eklenir ya da değişirse **minor**; bir şey düzeltilir ve sistem aynı şeyi söylemeye devam ederse **patch**; **major** proje sahibinin kararıdır ve bir milatı işaretler. Prototipin sürümlerinden ([docs/design/VERSIONS.md](../design/VERSIONS.md)) ayrı numaralanır. Bir sürüm uygulamaya geçince kaydın altına `Uygulandı: app X.Y.Z` satırı eklenir.

## 2.1.0 — 2026-11-24 · Uygulamada olmayan bileşenler kaldırıldı; ProjectRow uygulamadaki gibi

Uygulandı: app 1.15.0 (sistem uygulamaya eşitlendi; uygulamaya düşen bir değişiklik yok).

**Neden**: `MainWindow` kiti 1.x'ten kalma, bugünkü uygulamayla tutmayan bir pencere taklidiydi; `Toast` ve `WillBuildDot` uygulamada yok. Güncel pencereyi tasarım prototipi gösterir. `ProjectRow`'un şerit tablosu ve will-build noktası da 1.x'ten kalmıştı.

**Ne değişti**
- Kaldırıldı: `MainWindow` kiti (`PROJECTS` ve `GraphView` ile), `Toast`, `WillBuildDot`: bundle, `index.d.ts`, README'ler ve önizlemeler.
- `ProjectRow`: şerit ve nokta uygulamanın tablosuyla boyanır (işaretli, kuyrukta ve derleniyor amber, güncel yeşil, başarısız kırmızı, gerisi gri; döngü üyeliği listede renk taşımaz); başlangıç modunda (`discovered`) nokta yerine dört yaylı kesikli halka; `willBuild` prop'u kalktı.

## 2.0.1 — 2026-11-24 · Spinner, building glyph'i ve varsayılan adlar uygulamadaki gibi

Uygulandı: app 1.15.0 (sistem uygulamaya eşitlendi; uygulamaya düşen bir değişiklik yok).

**Neden**: `bundle.js` 1.x'ten geldiği için `Spinner` ve `StatusGlyph status="building"` 270°'lik bir yayı 900 ms'de döndürüyordu (building'de ayrıca opaklık nabzıyla); uygulama başlangıç modundaki kesikli halkayı döndürür. Varsayılan adlar Türkçeydi; uygulamanın UI metinleri İngilizcedir.

**Ne değişti**
- `Spinner` ve `StatusGlyph status="building"`: kesikli halka (r 6.7, desen 2.3 2.5, opaklık 0.9), 1.5 kontur, 1.4s lineer; building'de nabız yok. `Spinner`'ın varsayılan rengi `amber-text`, varsayılan adı `Loading`.
- Varsayılan adlar uygulamadaki İngilizce adlar: `STATUS_META` (`Not synced`, `Queued`, `Building`, `Up to date`, `Failed`, `Skipped`, `In a dependency cycle`), pencere düğmeleri (`Minimize`, `Maximize`, `Close`), `Dialog` ve `Toast`'un kapat düğmesi (`Close`). `MainWindow` kitinin ve kullanılmayan `WillBuildDot`'un metinleri 1.x'teki gibi kalır.
- Marka kitabı: tek yazılı chevron `⌄ latest` hapınınki; `▸` öneki konsolun komut satırlarında ve ribbon'un anlatı satırlarında.

## 2.0.0 — 2026-11-23 · Milat: uygulamanın kodundan yeniden kuruldu

Uygulandı: app 1.15.0 (sistem uygulamadan alındı; uygulamaya düşen bir değişiklik yok).

**Neden**: Design system başka bir Claude hesabında, 1.x tasarım paketlerinin içinde üretildi (`prototype/_ds/`, paketler `.claude/outputs/<tarih>-design-v1.x/` altında arşivde) ve uygulama ondan sonra kendi kaynaklarında ilerledi. 2.0.0 bu farkı kapatır: değerler, kurallar ve kopya uygulamanın bugünkü kodundan (`Resources/Tokens.xaml`, `Motion.xaml`, `Icons.xaml`, ARCHITECTURE §14) yeniden yazıldı. Bundan sonra tasarım bu sistemden başlar; 1.x numaraları devam ettirilmez.

**Ne var**
- `tokens.json` / `tokens.css`: renk, tipografi, boşluk, radius, gölge, süre ve easing; uygulamanın kullanıp eski sistemde olmayan değerler de: `status-queued-border`, ürün logosuna özel `brand-strip-dim` ve `brand-chevron-deep`, şerit ve başlık yükseklikleri (`ribbon-height` 32, `progress-height` 2, `action-bar-height` 42, `panel-header-height` 28, `layer-header-height` 24), pencere en küçük ölçüsü (1240 × 620), `focus-ring-offset`, `resource-strip-icon`, `leading-reading` 1.62.
- Marka kitabı (`README.md`): UI kopyası İngilizce; renk çıktının durumunu anlatır; turuncu döngü rengi ve ayrı will-build noktası uygulamada kullanılmaz, token'ları uyumluluk için durur; ikonografi, motion ve yasaklar uygulamadaki gibi.
- 28 bileşen (README + önizleme), örnek kopya İngilizce ve adlar temsili; `bundle.js` 1.x paketlerindeki bundle'ın kodudur, yalnız örnek verideki adlar değişti.
- Logolar ve uygulama ikonları; ürünün animasyonlu logosu `TrayBuildIndicator` (pencere gizliyken derleme sürerken ekranın sağ altında dönen 3 sn'lik döngü, reduced-motion'da sabit kare) uygulamanın `Controls/TrayBuildIndicator.xaml` kodundan.

**Bilinen farklar** (bilerek bırakıldı): uygulama gölgeleri tek katmanla çizer, sistem iki katmanlı CSS gölgesini tutar; nötr rampanın bir kısmı, `status-skipped-soft`, `tracking-tight`/`wide`, `leading-tight` ve `Toast`, `WillBuildDot` bileşenleri uygulamada kullanılmıyor. Geist fontları dosya olarak eklenmedi (uygulama kendi gömülü fontlarını taşır).
