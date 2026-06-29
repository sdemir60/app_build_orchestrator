# Claude Design — Build Orchestrator Prompt (v4.3 tasarımına göre)

Kullanım: Claude Design'da **Design system = Delta** (dark, tek accent amber `#eda10f`, mono console, yüklenen Delta logosu — dark zemin için `delta-logo-dark.svg` açık varyant) + **Template = Prototype** + **Model = Claude Opus 4.8** seç; aşağıdaki iki bloğu **birlikte** describe kutusuna yapıştır.

Kaynak tasarım kararları: [2026-06-29-11-17-build-orchestrator-plan-v4.3-design-review.md](2026-06-29-11-17-build-orchestrator-plan-v4.3-design-review.md) → `# DESIGN REVIEW` bölümü.

---

## Ana prompt

```
Bağlam: Windows MASAÜSTÜ developer aracı için tek-pencere bir UI tasarla. App UI (data-dense, utility) — pazarlama/landing DEĞİL. Delta design system'i kullan (dark, near-black; tek accent = marka amber #eda10f; gerçek monospace console; title bar'da yüklediğim Delta logosu). Ürün: "Build Orchestrator" — büyük çok-projeli bir .NET solution'unu bağımlılık sırasına göre, paralel ve yalnızca değişen projeleri derleyip CANLI gösteren araç. North-star: sakin-hassas dark (Linear/Geist ruhu) + heyecanlı build-frontier; restraint = kalite. Heyecan gürültüden değil, frontier'ın listede aşağı akışından gelir.

LAYOUT (tek pencere, hepsi tek kompozisyon):
- TITLE BAR (custom, near-black): solda Delta logosu (açık varyant) + "OSYS · main" (repo · branch); sağda min/max/close.
- STICKY ŞERİT + GLOBAL PROGRESS (title bar altı, ince): "▸ Building 8/120 · 1m04s · ~40s kaldı" + o anki paralel set çipleri (tıklanabilir). İnce determinate progress.
- GÖVDE iki pane (GridSplitter, ~%46/%54):
  • SOL = build-order'lı PROJE KART LİSTESİ (yüzlerce, virtualized). Kart = DENSE LİSTE SATIRI (marketing-card DEĞİL): sol kenarda statü-rengi accent şerit; proje adı (primary) + solution adı (dim, küçük); sağda durum glyph'i + süre; küçük sağlık göstergesi (cycle'sız=yeşil nokta, cycle=kırmızı rozet); "şu an <commitA> ile derli → hedef <commitB>" mini metni; satır sonunda "Dosyada Aç" / "Visual Studio'da Aç" ikonları.
  • SAĞ-ÜST = ANA CONSOLE (monospace): seçim yokken run anlatısı / granular adımlar; bir kart seçiliyken o projenin tam build log'u + sol-üstte [← Back].
  • SAĞ-ALT (yatay bölme) = KALICI ÖZET STREAM: kronolojik tek-satır olaylar, her zaman görünür; en alt satır = AKTİF (yazılan) satır.
- ACTION BAR (en alt): solda ⟳Sync + 5 sayaç (Σ Toplam · ● Derlenen · ✓ Başarılı · ✗ Başarısız · ↷ Atlanan); sağda branch chip (main ▾), worktree toggle chip ("committed temiz" / "local dahil" + caret), perf chip (Full/Balanced/Light), Build butonu (çalışırken Stop'a morph).

Statüler (renk + glyph + metin, colorblind-safe): Discovered / Queued / Building(amber active) / Succeeded(yeşil) / Failed(kırmızı) / Skipped(muted gri) / CycleDetected(warn).

6 EKRAN ÜRET (ayrı prototype frame; HERO merkez):
1) HERO — paralel build: birden çok kart "building" (amber, canlı); frontier üstten aşağı ilerliyor; üst şeritte global progress/ETA; sağ-altta özet stream akıyor; en alt satırda yazan "▌ Server.Api building…" imleç.
2) DETAY: bir kart seçili (accent şeridi kalınlaşmış + içerik bir tık içe kaymış, KUTU YOK); ana console o projenin tam log'u; sol-üstte [← Back]; altta özet stream hâlâ duruyor.
3) FAILURE: stream'de kırmızı "✗ Server.Api failed" satırı; nereye kaydırsan görünen "2 hata: Server.Api — [logu aç]" affordance'ı; action bar'da "✗ 2 failed" çipi vurgulu.
4) ALL-SKIPPED (delight): güvenli yeşil tonla "Her şey güncel — 120 proje 0.4sn'de kontrol edildi, derlenecek yok." (gri/başarısızlık hissi DEĞİL).
5) İLK AÇILIŞ (boş): sol panede ortalanmış sıcak davet "Başlamak için bir repo seç" + tek [Klasör Seç] butonu; sağ console "▌ Waiting for a workspace".
6) IDLE/READY: liste dolu, sakin; console "Ready" + blink imleç; chip'ler seçili.

ETKİLEŞİMLER (prototype destekliyorsa bağla):
- Özet stream'de HERHANGİ bir satıra tıkla → o projeyi seç + ana console'da detay + [← Back]. (Tek jest; modifier yok.)
- Kart tıkla → seçim efekti + detay. Console'da metin seçimi serbest (tıklayınca seçim kalkmaz; çıkış sadece Back).
- Sticky şerit çipine tıkla → ilgili karta git. Sayaç tıkla → kartları filtrele. Worktree chip: toggle durumu net + caret ile popup.

MOTION / ANİMASYON (prototype destekliyorsa uygula; yoksa o anı gösterecek şekilde çiz). Kural: aynı anda EN FAZLA 1 hero motion; yalnız RenderTransform+Opacity; OS reduced-motion açıksa hepsi anlık'a düşer.
- Frontier: building kartlarda hafif pulse + shimmer (yalnız görünür kartlar); oturmuş statüler STATİK (sonsuz glow yok).
- Auto-scroll: aktif grubun ağırlık merkezini yumuşak takip (zıplama/yo-yo yok).
- Typing live-line: en-yeni özet satırı SAKİNDE harf-harf yazılır; imleç hep blink eder; FIRTINADA (çok proje aynı anda biter) typing susar, satırlar anında eklenir; hata satırı typing'i ATLAR (anlık). Ham log asla harf-harf yazılmaz.
- Seçim efekti: accent şerit kalınlaşır + yazı bir tık içe kayar (anlık+hızlı, kutu yok).
- Sync reveal: kartlar build-order'da yukarıdan aşağı staggered fade-in (≤400ms toplam).
- Başarı: Done satırında TEK sakin settle/glow + frontier sakin-yeşile oturur. Hata: kart kısa shake (ikincil ipucu).
- Popup/menüler: RenderTransform+Opacity ile açılır (layout animasyonu yok).

KESİN YASAK (AI-slop): generic SaaS card grid, 3-kolon feature grid, renkli daire-içi ikon, mor/indigo gradient, her şeyde şişik radius, dekoratif blob/dalga, emoji'yi tasarım öğesi yapmak, marketing hero, ortalanmış her şey, dekoratif gölge. Kartlar dense satır; console keskin; glyph'ler gerçek ikon (emoji değil).

ÇIKTI: 6 frame (HERO en gösterişli) + en sonda token özeti (renk HEX, font adları + boyut/weight, spacing px, radius, icon set adı).
```

## Canlı demo eki (ana prompt'un sonuna eklenir)

```
CANLI DEMO (otomatik oynayan İNTERAKTİF prototype — statik frame değil):
- Ekran açılır açılmaz simüle bir build run OTOMATİK başlasın: önce Sync reveal (kartlar build-order'da staggered iner ≤400ms), sonra projeler sırayla Discovered → Building (amber pulse+shimmer, aynı anda 3-6 tanesi) → Succeeded/Failed/Skipped olur. Frontier listeyi üstten aşağı kat etsin; auto-scroll aktif grubu yumuşak takip etsin.
- Özet stream SÜREKLİ yazsın: her olayda yeni tek-satır eklensin; en-yeni satır SAKİN anlarda harf-harf yazılsın (typing imleci + sürekli blink); arada 1-2 "burst" anında (çok proje aynı anda biter) typing SUSSUN ve satırlar anında eklensin — bu kuralı canlı göster. Hata satırı typing'i ATLASIN (anında kırmızı).
- Global progress/ETA canlı saysın (X/N · geçen süre · kaba kalan).
- Sim ~20-40 sn sürsün; sonunda Done + success flourish (tek glow, frontier sakin-yeşil) ile otursun. İstersen başa sarıp loop'lasın — durması da sorun değil.
- Detay log da CANLI olsun: bir karta tıklayınca o projenin ana console'daki tam log'u SATIR SATIR aksın (monospace, hızlı append — harf-harf değil); hâlâ derleniyorsa "still going" göstergesiyle canlı stream.
- TÜM ETKİLEŞİM çalışsın (tıklanabilir prototype): karta tıkla → seçim efekti (accent kalınlaşır + yazı içe kayar, kutu yok) + detay + [← Back]; özet stream satırına tıkla → aynı detay; Back → özete dön; sticky çip / sayaç / branch-worktree-perf chip'leri → kendi davranışları. Detaydayken arka plan sim devam edebilir (auto-follow durur).
- REPLAY: Build butonu sim'i baştan başlatsın; Sync reveal'i tetiklesin — tüm süreci tekrar tekrar izleyebileyim.
- Görünür animasyonlar: frontier pulse/shimmer, typing imleç, seçim efekti, failure shake, success glow, sync reveal stagger, popup transform. OS reduced-motion açıksa hepsi anlık'a düşsün.
- Demo verisi: gerçekçi .NET adları (OSYS.Base, OSYS.Server.Api, OSYS.Client.Core, OSYS.Common.Utils…), ~30-40 kart, 2 fail + birkaç skip içersin.
```
