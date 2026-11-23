# Console

Konsol: `console-bg` zemin, keskin köşe (`radius-none`), Geist Mono satırlar; `ConsoleLine` satır bileşeniyle.

**Tüketici sağlar:** `lines` (İngilizce; örn. `Engine ready — v1.15.0`, `build requested`) = `{type, time, text}[]` ya da `children` olarak `ConsoleLine`'lar; yüksekliği sınırlayan `height` ya da kapsayıcı.

**Props:** `lines` · `height` · `autoScroll` (varsayılan açık) · `children` · `style`.
**ConsoleLine:** `type` `info | success | warn | error | cmd | dim` · `time` · `children`. `cmd` satırı `▸` önekli.

Kural: konsol gövdesi Geist Mono **300 (Light)**, 12px / `leading-mono` (1.55), tabular rakam.
