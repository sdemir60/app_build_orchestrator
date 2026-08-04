# design-v1.2.1 uyumu — ürün markası + About · kısa TDD dökümü

Kaynak: `.claude/outputs/2026-08-05-01-26-design-v1.2.1/` (§2.1 title bar · §2.10 About · §6 Assets · §9 1.1.0→1.2.1)
Branch: `feat/brand-mark-and-about-v121`

## Kapsam kararı

Sürüm geçmişindeki 1.1.0 → 1.2.1 arası işler taranıp bizdeki karşılıkları ölçüldü:

| Sürüm | Madde | Durum |
|---|---|---|
| 1.1.0 | About penceresi | Var ama tasarımdan sapıyor → **bu iş** |
| 1.1.0 | `⌄ latest` pill yumuşak iniş (jump guard) | **Zaten doğru** — `BottomAnchorDecision.BeginJump/EndJump` + 560ms `IsJumping` |
| 1.1.0 | Pill görünürlüğü "dipten ≥48px" | **Zaten doğru** — `ShouldShowPill` = `distance > 48` |
| 1.1.0 | log ↔ statü tutarlılığı | Prototip simülasyon bug'ı; bizde loglar gerçek MSBuild'den gelir, **karşılığı yok** |
| 1.2.0 | Ürün logosu (3 varyant) + title bar kilidi + About başlığı | **Bu iş** |
| 1.2.1 | Logo kurumsal palete | **Bu iş** (marka doğrudan bu palette çizilir) |

**Kapsam dışı:** §2.9'daki Settings sapmaları (surface, "Load sample layers", varsayılan boş katman) 1.0.0'dan gelir
ve ARCHITECTURE'da kayıtlı bilinçli kararlardır — kullanıcı 1.1+ dedi, dokunulmuyor.

**Sürüm numarası (kullanıcı kararı):** uygulamanın kendi assembly sürümü kalır (`1.0.0+it5`). Tasarımdaki
`1.2.1` tasarım PAKETİNİN sürümüdür. Tasarımdan alınan yalnız **biçim**: tek sürüm + telif, app/engine ayrımı
Environment sekmesinde.

## Görevler

### T1 — Marka token'ları + `AppMark` kontrolü
`app-mark.svg` → XAML: 5 pill `RectangleGeometry` + gradient chevron `PathGeometry`.
Renk eşlemesi (dördü mevcut token, ikisi yeni):
`#EDEDEE`→`Brush.TextPrimary` · `#A9A9B0`→`Brush.TextSecondary` · `#2A2A30`→`Brush.Neutral700` ·
`#EDA10F`→`Brush.Amber` · `#FFB52E`→`Brush.AmberBright` · **`#44444B` ve `#C9860C` yeni** (DS rampasında yok,
markaya ait — `Brush.StatusQueuedBorder` ile aynı statüde gerekçeli eklenir).
*Testler:* geometri tek dosyada · realize + oran korunur · iki yeni token çözülür · ham hex yok.

### T2 — Title bar logo kilidi (§2.1)
`AppMark` 19px + ad + 1×13 ayraç + Delta 10px %55 (tooltip "Delta") + bağlam.
*Testler:* sıra ve ölçüler · Delta opaklığı · mevcut `TitleBarContextTests`/`MainWindowRealizeTests` yeşil kalır
(logo artık 15px değil → **eski 15px testi yeni kuralı pinleyecek şekilde yeniden yazılır**, gerekçesi doc'una).

### T3 — About başlık bloğu (§2.10)
`AppMark` 30px · ad 17px/600 · tagline · mono tek sürüm satırı `{sürüm} · {telif}` · sağda `LICENSED TO` +
Delta 13px %80, 1×30 ayraç.
*Testler:* tek sürüm satırı (engine ADI GEÇMEZ) · LICENSED TO bloğu · logo 30px.

### T4 — About davranışı
F1 **toggle** · About Settings'in ÜSTÜNE açılabilir · Esc önce About'u kapatır · 180ms fade + 6px giriş.
*Testler:* toggle · katmanlama · Esc önceliği · **eski "F1 modal açıkken no-op" testi yeniden yazılır**.

### T5 — About ayrıntıları
Copy diagnostics: copy→✓ ikon + yeşil + metin başlığı `Build Orchestrator {sürüm}` · Third-party kolonları
(sürüm 70px, lisans 92px sağa) + "Bundled components and their licenses." · gövde `MinHeight=236` ·
tooltip "(F1)".
*Testler:* pano metni başlık satırı · kolon ölçüleri · min-height (sekme değişince zıplamaz KORUNUR).

### T6 — İkonlar (.exe + tray)
`generate-tray-icon.ps1` yeni markadan üretecek şekilde yeniden yazılır: `app-icon.svg` (tile+chevron+pill'ler)
→ 16/24/32/48/256; `app-mark-mono.svg` → tray 16.
*Risk:* 16px'te 5 pill (21/286 birim ≈ 1.2px) çamura döner. Önce rasterleştir, SONUCA BAK, gerekirse kullanıcıya
sadeleştirilmiş küçük-boy varyantı öner. Sessizce bozuk ikon TESLİM ETME.

### T7 — Dokümanlar + tam süit + merge

## Değişmezler
Kırmızı-önce · ham renk/motion yasağı · yeni XAML kökü ⇒ realize testi · kopya YASAK · bitişte tam süit yeşil.
