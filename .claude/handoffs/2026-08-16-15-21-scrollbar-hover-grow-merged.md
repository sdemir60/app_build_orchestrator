# Aşama — scrollbar hover genişlemesi merge edildi

## İlgili özet/çıktı dosyaları

- `.claude/outputs/2026-08-14-18-26-scroll-follow-and-console-transition-fixes.md` — kaydırma takibi, stream
  alt satırı, konsol tilt-in (ikinci saha turu).
- `.claude/outputs/2026-08-15-03-05-will-build-consistency-plan.md` — will-build tutarlılığı planı (onaylı).
- `.claude/outputs/2026-08-15-04-01-will-build-consistency-results.md` — o planın uygulama sonuçları,
  plandan iki sapmanın gerekçesi ve göz kontrolü listesi.

## Nerede kaldık

Scrollbar hap'ı fare ray'a girince 4px'ten 8px'e animasyonlu genişliyor ve nötr rampada bir basamak
açılıyor (sürüklerken bir basamak daha, yeni `Brush.Neutral500`); yarıçap animasyonlu `Padding`'e bağlı.
`main`'e merge + push edildi (`7f34781`), çalışma branch'i silindi. Tam süit yeşil: **2087 geçti, 1 atlandı,
0 kırık.**

Açık iki iş var (benim değil): `fix/scroll-cancel-releases-not-rewinds` merge edilmemiş duruyor ve
`.claude/worktrees/fix-hover-flash` worktree'si `MotionTokens.TransitionColor`'ı değiştiriyor — bu merge
`ResolveFast` refactor'ü getirdiği için orada çakışma çıkacak.

Buradan devam edilecek.
