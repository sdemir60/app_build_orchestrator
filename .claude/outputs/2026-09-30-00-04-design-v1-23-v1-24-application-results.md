# Design v1.23.0 + v1.24.0 uygulama sonucu

Plan: `.claude/outputs/2026-09-29-20-55-design-v1-23-v1-24-application-plan.md` · Tasarım paketi:
`.claude/outputs/2026-09-19-08-05-design-v1.24.0/` (main'e commit edildi: `102d1ca`).
Branch: `feat/design-v1.23-v1.24` — main'e merge EDİLMEDİ (kullanıcı bilgisayar başında değildi; inceleme ve merge
kararı kullanıcıda).

## Yapılanlar

**v1.24.0 — Keşif sürerken panel boş kalmaz**
- Motor: Sync her kaynağı okuyunca kümülatif `syncDiscovery` olayı gönderir (önce ana repo, sonra her harici kök);
  Clean/Optimize göndermez; tekrar sayım yok.
- Liste: list ikonu + `Discovering projects` + `31 found · 29 repository · 2 external` (harici tanım yoksa yalnız
  `N found`); arama kutusu, `build-order`, filtre çipi keşif boyunca gizli; polite canlı bölge.
- Graf: network ikonu + `Graph appears once projects are discovered`; başlık sayacı ve kesikli kutu gizli.
- Görünür olduğu Sync'ler: açılış, Clean/Optimize sonrası, kök değişimi, Sync düğmesi, branch değişimi,
  Debug/Release. Sessiz Sync ve satırlar dururken gelen Sync göstermez.

**v1.23.0 — Güncelleme hapı (yalnız tasarım; motor yok)**
- Title bar sağ kümesinin başında hep görünen `Update 1.8.0` hapı + ayraç; mevcut ikonlar kaymaz.
- Kart: `UPDATE READY · 18.4 MB`, `1.7.0 → 1.8.0`, öne çıkanlar (What's new blok dili, ortak yardımcı),
  `Later` / `Restart to update`; iş sürerken Restart kilitli ve sebebi yazar.
- Restart ekranı: logo, `Updating Build Orchestrator`, sürüm geçişi, amber çubuk, üç adım; bitince uygulamaya aynen
  döner. Ekran açıkken klavye ve global kısayollar yok sayılır.
- Kart içeriği örnek veridir (`UpdateOffer.Sample`); gerçek teklif ileride `RunViewModel.AvailableUpdate`'e yazılır.

## Kararlar (kullanıcı yokken)

- Motor okuma sırası değişmedi (önce repo, sonra hariciler); sayaç ara değerleri tasarımdaki sırayla değil.
- Filtre Sync'te korunmaya devam eder; keşif boyunca yalnız gizlenir.
- Kart WPF Popup olduğundan bir dialog açılınca kapanır (tasarım: dialog kartın üstüne açılır).
- Kilit metni `F5 stops it` yerine `Esc stops it` (uygulamada F5 durdurmaz).
- Hover 120 ms (DS standardı; README'de çelişen 80 ms).
- Restart to update gerçek kurulum yapmaz; ekranı oynatıp geri döner, hap kalır.
- Kart her makinede hapın sol kenarına hizalı: bu makinede `MenuDropAlignment=True` olduğu için özel yerleşim
  (`PopoverPlacement`) yazıldı.

## İnceleme turu

5 boyutlu inceleme + bulgu başına 1–3 şüpheci; 16 doğrulanmış bulgu düzeltildi (her biri için önce kırmızı test):
yeni metinler Segoe UI yerine Geist, restart ekranı çıkış sönümü görünmüyordu, kart yerleşimi, canlı bölge
duyurusu ve workspace meşguliyet listesi tek yerde, test yardımcısı kopyaları, doküman cümleleri.

## Doğrulama

- Tam süit (`Category!=Acceptance`): son koşu 3955 geçti / 6 başarısız / 7 atlandı. 6 başarısızın hepsi
  `StickyLayerHeaderClickTests` (gerçek fare yakalama); aynı 6 test değişmemiş `main`'de de aynı şekilde düşüyor —
  oturum kilitliyken ortam kaynaklı. Oturum açıkken yapılan önceki iki tam koşu 0 başarısızdı (3956 ve 3961 geçti).
- Görsel: ekran dışı render ile keşif blokları, hap, kart, restart ekranı ve What's new tasarımla karşılaştırıldı.
- Canlı pencerede gözle doğrulama yapılamadı (oturum kilitli).

## Açık kalanlar (kapsam dışı, dokunulmadı)

- Diğer popover'lar (branch, Build menüsü, VS seçici) da `MenuDropAlignment`'a bağlı; bu makinede sağa hizalı açılır.
- Pencere geneli yazı tipi: title bar ürün adı ve bazı eski boş durum metinleri hâlâ Segoe UI.
- What's new dialogu açılışta en alta kaymış açılıyor (odak `Earlier versions` düğmesine gidiyor; önceden var).
- Clean/Optimize'ın kendi penceresinde graf başlığı `0 projects · 0 dependencies` okuyor (önceden var).
