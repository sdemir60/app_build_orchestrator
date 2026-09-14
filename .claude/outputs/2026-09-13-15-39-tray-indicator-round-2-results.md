# Tepsi Göstergesi — 2. Tur Sonucu (görsel test bulguları)

> Bu dosya bir **kayıttır**. Planı (`2026-09-13-15-39-tray-indicator-round-2-plan.md`) tekrar anlatmaz: ne
> yapıldı, nerede sapıldı, hangi karar ölçülerek verildi, ne açık kaldı.

| | |
|---|---|
| **Plan** | `.claude/outputs/2026-09-13-15-39-tray-indicator-round-2-plan.md` |
| **Önceki kayıtlar** | `2026-08-20-12-40-tray-build-animation-results.md` · `2026-09-12-10-31-tray-build-animation-v2-results.md` |
| **Branch** | `fix/tray-indicator-round-2` (ana proje), `origin`'e push EDİLDİ |
| **Taban** | `main` @ `a59dee6` (plan tablosundaki `bb8adfc` yazıldığı anda eskimişti: main bu arada optimize merge'i ve iki docs commit'iyle ilerlemişti; branch o uçtan açıldı) |
| **Durum** | Tam süit yeşil; **merge EDİLMEDİ** — kullanıcı görsel turu tekrar edecek, dört soru açık |

---

## Bulgular → yapılanlar

| # | Kullanıcı gözlemi | Sonuç | Commit |
|---|---|---|---|
| B1 | Donmalar (ekran + animasyon) | **Kod kusuru değil.** Ölçüm: gösterge tek başına çekirdeğin +3.7 puanı (statik 8.0 % → döngü 11.7 %, +30 kare/sn); profil `FullPower` = altı paralel MSBuild, Normal öncelik, CPU tavanı yok. Ölçüm testi repoda kaldı (`TrayOverlayMeasurementTests`, yalnız `BO_MEASURE_OVERLAY=1` ile koşar). | `32b3d03`, `b698c00` |
| Ö1 | Sayaç okunmuyor | Sayaç ve tüm kablosu kaldırıldı; beyaz şerit markanın kendi pill'i (60 birim), çıkış mesafesi 88 (sade asset). `Brand.Pill.WhiteCounter`, `Brush.Brand.CounterText`, `ITrayBuildIndicatorView.UpdateCounter` gitti. | `b1ccd0a`, `3fc001f` |
| Ö2 | Çok küçük | Sahne tasarımcının bandına kırpıldı (375×134, iç tuval 30,−76) ve overlay tek bir ölçekle (`Scale = 2/3`) ≈250×89 DIP oldu — şerit ~14 px (eskiden 7). Logo artık görev çubuğunun hemen üstünde. | `5892506`, `5eaad9b` |
| Ö3 | Şeritler kesilmiş gibi kayboluyor | **Geometrik kusur bulunamadı** (pill/maske/keyframe'ler asset ile birebir; çıkışta maske şerit ucunun ≥44 birim önünde). İki katkı: 7 px şeritte 340 ms solma algılanmıyor; yük altında kare düşmesi. Ö2 + Balanced denemesinden sonra yeniden bakılacak. | — |
| Ö4 | Bildirime tıklayınca uygulama açılsın | `TrayBalloonTipClicked` → aynı `RestoreRequested` yolu (tepsi ikonu ve logo tıkıyla aynı `ShowFromTray`). Bu ikonun HER balloon'u için geçerli. | `e4e3d86` |
| Ö5 | Bildirim animasyondan sonra mı, yarıda kesilme var mı | Kod ve pinler doğruladı: tur bitişi → kaybolma → `Duration.Slow` nefesi → balloon; yarıda kesme yok. Bedel: koşu giriş evresinde biterse ≤3 s bekler. | — |
| K1 | Daha estetik bildirim | Sol tarafta uygulamanın büyük ikonu (`app-icon.ico` 48 px); başlık = şerit satırının başı (`Completed` / `▸ Stopped` / `Run failed` / `Engine missing`…), gövde = geri kalanı; ayırıcı `RibbonLine.HeadSeparator = " — "`, tek kaynak; ayırıcısız satırda (`Engine stopped unexpectedly (…)`) başlık ürün adı, gövde satırın tamamı. `RibbonLine.Healthy` emekli (tüketicisi kalmadı). İkon adresi tek kaynak `AppIdentity.AppIconUri`; `MainWindow` ikonu artık ctor'da koddan yükler. | `ebce54f`, `d83e95a` |

## Sapmalar ve ölçülerek verilen kararlar

1. **`Category!=Acceptance` filtresi `Measurement`'ı DIŞLAMIYOR.** VSTest'in `!=` operatörü diğer her
   kategori değerini kabul eder; ölçüm testi bu yüzden her tam süitte koşmuş, overlay'i 17 s ekrana çıkarmıştı.
   Kapı artık `Skip.IfNot(BO_MEASURE_OVERLAY == "1")`. Kanonik test komutu değiştirilmedi; seçenek: komutu
   `--filter "Category!=Acceptance&Category!=Measurement"` yapmak (`ContentDecisionMeasurementTests` aynı
   yanlış "hariç" iddiasını taşıyor, `Skip.IfNot` ile kurtuluyor).
2. **`Icon="{x:Static …}"` XAML'de çalışmadı** (ölçüldü: 17 `MainWindow` testi `XamlParseException`) —
   `ImageSourceConverter` markup-extension dizgisine uygulanmıyor. İkon `MainWindow` ctor'unda
   `BitmapFrame.Create(new Uri(AppIdentity.AppIconUri))` ile kuruluyor; realize pini ikonun çözüldüğünü ölçüyor.
3. **Stopped satırı `"▸ Stopped — …"`** → bildirim başlığı `▸ Stopped`. İşaret kırpılmadı: şerit ne yazıyorsa
   başlık o; kırpmak için `"▸ "` tek sabit olmalı, `Compose` dizgileri onu literal taşıyor (kopya riski).
   Kullanıcı kararı (aşağıda D).
4. **H.NotifyIcon 2.4.1 parametre adı `customIconHandle`** (plan `customIcon` demişti); `NotificationIcon.None
   + customIconHandle + largeIcon:true`. Modern toast'ta büyük ikon slotu kabuğun kararıdır; 48 px kaynak kare.
5. **Overlay testleri örnek üzerinden pinliyor** (review bulgusu): `OverlayWidth == StageWidth * Scale` sabiti
   kendine karşı sınıyordu ve oran testi `precision`sizdi; şimdi kurulan pencerenin `Width/Height`'ı ve
   realize edilen içerik, toleransla. Mutasyonla kanıt: ctor `Width = 200` → testler kırmızı.
6. **Skippable STA gövdesi tek yardımcıda** (`tests/App/StaThread.cs`): `[StaFact]` runner'ı `SkipException`'ı
   tanımadığı için gövde manuel STA thread'de koşar; `DragReorderTests` ve `AppShutdownTests`'teki özel
   kopyalar da silindi (kopya yasağı testlerde de geçerli).
7. **`RunFinishedBody`** `default(RibbonLine)` için `""` taban (eski `_terminalText = ""` savunması korundu).
8. Park edilenler (kozmetik, bilerek dokunulmadı): `AppIdentity.AppIconUri` doc'u "constructor" sözcüğü yerine
   dosya adını anıyor · Tokens.xaml:57 "Bu ikisi" yorumu iki amber tonunu, ARCHITECTURE üç ham `Color`'ı
   anlatıyor — ikisi de doğru · viewBox dört yerde anlatılıyor (const doc'una gönderme var) · başsız-satır
   fallback pini üretim literalinin elle kopyası (drift kilidi gerçek `Compose`'dan geçiyor) · kapanışta
   disposed ikon üzerinde teorik `ShowRunFinished` yarışı · eski test dosyalarındaki yedi Release uyarısı.

## Doğrulama

- **Tam süit (`-c Release`, `Category!=Acceptance`, 2026-09-13):** Başarısız 0 · Başarılı 2784 · Atlanan 2 —
  atlananlar `DragReorderTests.Reorder_uses_mouse_capture_…` (bilinen ortam skip'i) ve kapılanan ölçüm testi.
  Her task'ta kırmızı→yeşil kanıtı ve tam süit koşusu; her task'ta ayrı review + gerekirse fix turu; sonda
  tüm branch için review + tek fix dalgası + re-review.
- **Ölçüm (Release, boş makine, 8 s örnek):** statik kare 32.1 kare/sn · 8.0 % çekirdek; döngü 62.3 kare/sn ·
  11.7 %; fark +30.2 kare/sn · +3.7 puan. (`CompositionTarget.Rendering` aboneliği render döngüsünü kendisi
  tetiklediği için "statik" örnek de kare üretir; fark anlamlı olan sayıdır.)
- **Derleme:** 0 hata. Tüm derleme/test `-c Release` (kullanıcının Debug instance'ı açıktı, VS DLL'leri
  kilitliydi). Uygulama bizden hiç başlatılmadı.
- **Gözle doğrulama YOK** — kullanıcı görsel turu tekrar edecek.

## Açık sorular (kullanıcıya)

- **A — Donma:** pencere açıkken de derleme sırasında ekran donuyor mu? `Balanced` profilde deneme.
- **B — Başarısız koşuda ikon:** her sonuçta uygulama ikonu (bugünkü) mü, başarısızlıkta OS'in kırmızı hata
  ikonu mu? (`Healthy` geri getirilir, tek satır.)
- **C — Pencere açıkken balloon:** çıkış animasyonu sırasında pencere geri getirilirse balloon yine geliyor
  (K-4 "taahhüt korunur"). Yutulsun mu?
- **D — `▸ Stopped` başlığı:** işaret kalsın mı, kırpılsın mı?

## Görsel turda bakılacaklar

Orijinal on senaryo (`2026-08-20-12-40-tray-build-animation-merge-prompt.md`) sayaç maddesi hariç aynen; ek:
gösterge boyutu ve görev çubuğuna yakınlığı · şeritlerin çıkışı (kesik görünüyor mu, Balanced'da) · bildirimde
uygulama ikonu + başlık/gövde · bildirime tıklayınca pencere · pencere/görev çubuğu ikonunun yerinde olması
(artık koddan yükleniyor) · başarısız koşuda bildirim metni.
