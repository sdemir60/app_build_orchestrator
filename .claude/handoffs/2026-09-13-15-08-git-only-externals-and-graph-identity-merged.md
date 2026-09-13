# Aşama — git-only harici projeler + graf kimliği merge edildi

## İlgili özet/çıktı dosyaları

- `.claude/outputs/2026-08-14-18-26-scroll-follow-and-console-transition-fixes.md` — kaydırma takibi, stream
  alt satırı, konsol tilt-in (ikinci saha turu).
- `.claude/outputs/2026-08-15-03-05-will-build-consistency-plan.md` — will-build tutarlılığı planı (onaylı).
- `.claude/outputs/2026-08-15-04-01-will-build-consistency-results.md` — o planın uygulama sonuçları,
  plandan iki sapmanın gerekçesi ve göz kontrolü listesi.
- `.claude/outputs/2026-09-12-11-39-drop-tfvc-and-key-graph-by-id.md` — TFVC'yi kaldırma + graf kimliğini
  Id'ye çevirme planı (ölçümüyle birlikte).

## Nerede kaldık

TFVC desteği UI'dan ve arka plandan tamamen kalktı (harici kart artık yalnız bir yol), graf düğümleri ada
göre değil proje Id'sine göre anahtarlanıyor (kullanıcının bildirdiği "node'lar arasında boşluk" kusurunun
kök nedeni), çakışan `AssemblyName` artık konsola bildiriliyor ve Sync de Clean gibi plan yüzeyini tıklama
anında boşaltıyor. `main`'e `--no-ff` merge + push edildi (`4e7ecaa`), çalışma branch'i local ve remote'tan
silindi. Tam süit yeşil: **2629 geçti, 1 atlandı, 0 kırık.**

Kullanıcı kendi harici kart listesini düzeltip (yanlış eklenen `Rent\OSYS.RentACar` kartı) gerçek uygulamada
göz kontrolü yapacak.

Buradan devam edilecek.
