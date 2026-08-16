# Aşama — filtre input'u ve odak halkası düzeltmeleri merge edildi

## İlgili özet/çıktı dosyaları

- `.claude/outputs/2026-08-14-18-26-scroll-follow-and-console-transition-fixes.md` — kaydırma takibi, stream
  alt satırı, konsol tilt-in (ikinci saha turu).
- `.claude/outputs/2026-08-15-03-05-will-build-consistency-plan.md` — will-build tutarlılığı planı (onaylı).
- `.claude/outputs/2026-08-15-04-01-will-build-consistency-results.md` — o planın uygulama sonuçları,
  plandan iki sapmanın gerekçesi ve göz kontrolü listesi.

## Nerede kaldık

Beş kusur (caret çift dolgu · şerit yüksekliği · şerit payı · popover genişliği · odak halkası) iki turda
düzeltildi, `main`'e merge + push edildi (`dc954db`); çalışma branch'leri silindi. Süit: **2082 geçti,
1 atlandı.**

Açık tek konu: `tests/BuildOrchestrator.Tests/App/ScrollAnimatorTests.cs` içinde bu oturumda benim
yazmadığım, commit'lenmemiş **2 kırmızı test** duruyor — `ScrollAnimator.CancelForUser` paneli
animasyonun bıraktığı yere değil başlangıcına geri sarıyor; fix henüz yok.

Buradan devam edilecek.
