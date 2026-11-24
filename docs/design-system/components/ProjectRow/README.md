# ProjectRow

Proje listesi satırı (36px; `compact` 30px): sol statü şeridi, kutu glyph'i, ad, solution, statü ve süre.

**Tüketici sağlar:** `name`, `solution`, `status`, `willBuild`, `duration` (mono metin), seçim durumu ve isteğe bağlı `onOpenFile` / `onOpenVS` (hover'da görünen satır eylemleri).

`willBuild` bundle'da durur; uygulamada ayrı will-build kanalı yoktur — 8px nokta şerit ve glyph ile aynı görsel durum rengini taşır.

**Props:** `name` · `solution` · `status` · `willBuild` · `duration` · `selected` (`surface-raised` zemin) · `compact` · `onOpenFile` · `onOpenVS` · `onClick` · `style`.
