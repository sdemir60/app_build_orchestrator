# Aşama — will-build tutarlılığı merge edildi

## İlgili özet/çıktı dosyaları

- `.claude/outputs/2026-08-14-18-26-scroll-follow-and-console-transition-fixes.md` — kaydırma takibi, stream
  alt satırı, konsol tilt-in (ikinci saha turu).
- `.claude/outputs/2026-08-15-03-05-will-build-consistency-plan.md` — will-build tutarlılığı planı (onaylı).
- `.claude/outputs/2026-08-15-04-01-will-build-consistency-results.md` — o planın uygulama sonuçları,
  plandan iki sapmanın gerekçesi ve göz kontrolü listesi.

## Nerede kaldık

Her iki çalışma da `main`'e merge edildi ve push edildi (`b1b8315`). Çalışma branch'leri silindi, açık
branch yok. Tam süit yeşil: **2047 geçti, 1 atlandı, 0 kırık.**

## Açık bulgu (kod yazılmadı) — tepside dönen saatler

X / Alt+F4 uygulamayı KAPATMAZ, tepsiye indirir (`MainWindow.OnClosing`, `e.Cancel = true` — K5 kararı);
gerçekten kapatan tek şey tepsi menüsündeki *Exit*. `Hide()` ise `Unloaded` tetiklemediği için görünürlük
guard'ı OLMAYAN sonsuz saatler dönmeye devam eder: **konsol prompt imleci**, **event stream imleci** (ikisi
de 30fps, `StopBlink`/`StopCursorBlink` yalnız `Unloaded`'da) ve bir düğüm seçiliyse **graf seçim kenar
akışı**. Guard'ı olanlar (StatusGlyph nabzı, BuildingSpinner) tepside duruyor.

Öksüz süreç sorunu YOK — ölçüldü, uygulamadan kalan MSBuild/VBCSCompiler bulunmadı.

Olası düzeltme tek noktadan: pencere gizlenince motion'ı topluca kapatmak (tepside animasyonun izleyicisi
yok). Bu, `2026-08-13-19-14-post-v170-fix-results-and-open-items.md`'deki açık konu #2 ve #4 ile aynı ailedir.

Buradan devam edilecek.
