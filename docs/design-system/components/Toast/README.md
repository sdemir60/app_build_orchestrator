# Toast

Bildirim (340px): statü glyph'i + başlık + açıklama + isteğe bağlı eylem.

**Tüketici sağlar:** `tone`, `title`, isteğe bağlı `description`, `action` (ör. `Open console` butonu), `onClose`; yığınlama/konum kapsayıcının işi.

Uygulamada toast ve uygulama içi popup yoktur (ARCHITECTURE §14.7); bileşen sistemde durur.

**Props:** `tone` `info | success | fail | warn | building` · `title` · `description` · `action` · `onClose` · `style`.
