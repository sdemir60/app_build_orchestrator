# ProjectRow

Proje listesi satırı (36px; `compact` 30px): sol statü şeridi, kutu glyph'i, ad, solution, statü ve süre.

**Tüketici sağlar:** `name`, `solution`, `status`, `duration` (mono metin), seçim durumu ve isteğe bağlı `onOpenFile` / `onOpenVS` (hover'da görünen satır eylemleri).

8px nokta şeritle aynı rengi taşır (uygulamanın tablosu: işaretli, kuyrukta ve derleniyor amber, güncel yeşil, başarısız kırmızı, gerisi gri; döngü üyeliği listede renk taşımaz). Başlangıç modunda (`discovered`) nokta yerine dört yaylı kesikli halka çizilir.

**Props:** `name` · `solution` · `status` · `duration` · `selected` (`surface-raised` zemin) · `compact` · `onOpenFile` · `onOpenVS` · `onClick` · `style`.
