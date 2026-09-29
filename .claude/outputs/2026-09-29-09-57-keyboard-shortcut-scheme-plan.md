# Klavye kısayol şeması — TDD dökümü

Kaynak: 2026-09-28/29 oturumunda kullanıcıyla kesinleşen tablo. Kısayollar YALNIZ bunlardır:

| Kısayol | Eylem | Nerede |
|---|---|---|
| `Shift+Space` | Getir / gizle (gizli, küçük ya da arkadaysa öne; öndeyse tepsiye) | global |
| `Ctrl+Shift+Space` | Build, pencere açılmadan; yalnız başlatır | global |
| `Esc` | Katman zinciri: diyalog → popover/menü → seçim → **çalışan Build/Rebuild/Clean'i durdurur** | pencere |
| `F1` | About (toggle) | pencere |
| `F5` | Build — yalnız başlatır, koşarken hiçbir şey | pencere |
| `F6` | Rebuild | pencere |
| `F7` | Clean (Build menüsündeki `-t:Clean`) | pencere |
| `Ctrl+F` | Proje filtresi | pencere |

Kalkanlar: `Alt+B`, `Ctrl+F5`, `Shift+F5`, `Ctrl+F1`.

Esc geri bildirimi: ilk Esc → bugünkü stop satırı + Stopping şeridi; Stopping'de tekrar Esc → şerit satırı kısa vurgu
(konsol satırı YOK); Sync / Deep Clean / Optimize (ve checkout/pull) sürerken Esc → konsola tek satır
"<işlem> can't be stopped — it will finish on its own", aynı işlemde yalnız ilk basışta.

## Görevler (her biri kırmızı testle başlar)

1. **Global tuş okuyucu + tablo + kalıcı alanlar** — `HotkeyBinding.TryParse` `Space` tanır; iki globalin tek tablosu
   (id + varsayılan jest); `UiState`'te iki yeni alan, eski `Hotkey` (`"Alt+B"`) yok sayılır.
2. **Getir/gizle kararı** — saf karar (görünür + küçültülmemiş + aktif → gizle, aksi → getir); global Build → VM
   `BuildCommand` (CanExecute onurlanır).
3. **Pencere tuş tablosu** — F5→Build, F6→Rebuild, F7→CleanAll, Ctrl+F, F1, Esc; F5'in duruma-dallı hâli ve
   Ctrl/Shift+F5, Ctrl+F1 kalkar (negatif pin).
4. **Esc'in durdurma katmanı + geri bildirim** — saf zincirin son halkası; VM "durdurulamaz işlem" etiketi ve
   işlem başına tek konsol satırı; tekrar Esc'te şerit vurgusu (reduced-motion'da saat YOK).
5. **Katalog ve gösterim yerleri** — About grupları (BUILD / APPLICATION / GLOBAL), iki global satırın
   `unavailable` işareti, Build menüsü rozetleri (F5/F6/F7), ✨ ipucu jestsiz, ikinci-instance balonu.
6. **Dokümanlar** — README kısayol tablosu, ARCHITECTURE §12.3 / §13.7 / §13.9 / About bölümü.
7. **Tam süit + merge.**

Eski kuralı pinleyen her test SİLİNMEZ; yeni kurala göre yeniden yazılır, doc'una eski iddia + değişme gerekçesi
(kullanıcı kararı 2026-09-29) yazılır.
