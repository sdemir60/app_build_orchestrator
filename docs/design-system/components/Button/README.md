# Button

Eylem butonu; amber yalnız `primary` varyantında — bir görünümde tek birincil eylem.

**Ne zaman:** `Build`, `Sync`, `Stop`, `Cancel` gibi metinli eylemler. Yalnız ikonlu eylem için `IconButton`.

**Tüketici sağlar:** `children` (kısa, İngilizce emir kipi metin), isteğe bağlı `icon` (14px Lucide stroke SVG), `onClick` ve diğer button prop'ları.

**Props:** `variant` `primary | secondary | ghost | danger` (varsayılan `secondary`) · `size` `sm 24 | md 28 | lg 32` px · `icon` · `disabled` (%45 opaklık) · `style`.

- Primary: `amber` zemin, `text-on-accent` yazı; hover `amber-bright`, press `amber-dim`.
- Secondary: `surface-raised` + `border-strong`; hover bir yüzey adımı (`surface-overlay`).
- Aynı bölgede ikinci bir amber öğe koyma.
