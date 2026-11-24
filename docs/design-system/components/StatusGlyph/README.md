# StatusGlyph

Statü glyph'i: ince halkalı daire içinde çizim — renk + glyph, metinle birlikte kullanılır.

**Tüketici sağlar:** `status`; yanında daima İngilizce statü metni (`Not synced`, `To build`, `Marked to build`, `Up to date`, `Queued`, `Building`, `Failed`) ya da `StatusBadge`. `STATUS_META` (aynı namespace) her statünün rengini ve uygulamadaki adını verir (`discovered` → `Not synced`, `succeeded` → `Up to date`, `cycle` → `In a dependency cycle`).

**Props:** `status` `discovered | queued | building | succeeded | failed | skipped | cycle` · `size` (px, varsayılan 16) · `title` · `style`. Building: dönen kesikli halka (1.4s, lineer); reduced-motion'da durur. `—` (skipped) yalnız event stream'in atlama satırındadır; `cycle` uygulamada statü değildir (uyarı üçgeni ayrı slottadır).
