# UI asla kilitlenmemeli — kök neden raporu ve TDD planı

**Kural:** hiçbir şart ve koşulda UI thread'i bloke olmayacak; paneller birbirini bekletmeyecek.
**Ölçüm ortamı:** bu makine, xUnit STA harness (gerçek HWND/render/animasyon YOK → üretimde bu sayılar
**daha kötü**). Ölçek gerçek OSYS reposundan: **177 proje**, **475 branch** (`refs/heads` + `refs/remotes`).

---

## Ölçülen gerçek

| Yol | UI thread bloke |
|---|---|
| **Branch envanteri yayını — 475 branch** | **ilk 21 s · sonraki her Sync 36 s** |
| Branch envanteri — 200 branch | 4.2 s · 6.2 s |
| Branch envanteri — 55 branch | 0.7 s · 1.2 s |
| İlk Sync — proje listesi (177 satır realize) | 664–1.100 ms |
| Filtre kapatma (0→177 satır) | 600–695 ms |
| Filtre açıkken proje başına | 16–50 ms |
| 2. Sync, aynı topoloji | 0.7 ms (imza guard'ı çalışıyor) |
| Graf inşası (500 düğüm) | 35 ms |
| Graf statü tick'i (200 ms'de bir) | 0.1 ms |

Branch yolu **kuadratik**: 55 → 1.2 s, 200 → 6.2 s, 475 → 36 s.

---

## Bulgular — bloklayıcı

### B1. Envanter yayını O(n²) — her Sync'te ~36 s donma

`SyncAsync` her Sync'te branch **ve** worktree envanterini ister
([RunViewModel.cs:528-534](../../src/BuildOrchestrator.App/ViewModels/RunViewModel.cs#L528-L534)). Gelen
`BranchListEvent` dört ayrı kusurun çarpımına giriyor:

| # | Konum | Kusur |
|---|---|---|
| a | [RunViewModel.Workspace.cs:354-358](../../src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs#L354-L358) | `Replace<T>` sondan N kez `RemoveAt` + N kez `Add` → **2N bildirim**; envanter hiç değişmemişken bile. |
| b | [BranchPopover.xaml.cs:62](../../src/BuildOrchestrator.App/Views/BranchPopover.xaml.cs#L62), [:89-90](../../src/BuildOrchestrator.App/Views/BranchPopover.xaml.cs#L89-L90) · [WorktreePopover.xaml.cs:66](../../src/BuildOrchestrator.App/Views/WorktreePopover.xaml.cs#L66) · [ActionBar.xaml.cs:144](../../src/BuildOrchestrator.App/Views/ActionBar.xaml.cs#L144) | Her abone **her bildirimde** tüm satırlarını yeniden kuruyor (`Children.Clear()` + N × `BuildRow`). |
| c | [PopoverBase.cs:113-117](../../src/BuildOrchestrator.App/Views/PopoverBase.cs#L113-L117) | Abonelik `DataContext`'te kurulur, `IsOpen`'da değil → **popover KAPALIYKEN de** satır inşa ediyor. |
| d | [BranchPopover.xaml.cs:89-90](../../src/BuildOrchestrator.App/Views/BranchPopover.xaml.cs#L89-L90) | Satırlar kod-tarafı `Children`'a ekleniyor — virtualization yok; 475 satır her seferinde tam kuruluyor. |

Çarpım: 950 bildirim × ortalama ~237 satır ≈ **225.000 satır inşası / Sync**.

### B2. Proje listesi virtualization kapalı

[StickyLayerList.xaml:40](../../src/BuildOrchestrator.App/Controls/StickyLayerList.xaml#L40)
`IsVirtualizing="False"` · [StickyLayerList.xaml.cs:167](../../src/BuildOrchestrator.App/Controls/StickyLayerList.xaml.cs#L167)
`Flow.ItemsSource = entries` (ItemsControl için tam teardown + regen).
[MainWindow.xaml.cs:437-448](../../src/BuildOrchestrator.App/MainWindow.xaml.cs#L437-L448) `ApplyProjectGroups`
filtre değişiminde de aynı reset yolundan geçiyor.

ARCHITECTURE.md §20 (satır 1327-1331) bunu **bilinçli ertelenmiş** sınır olarak yazıyor; gerekçe
`ScrollUnit=Pixel`'in gerçekleşmemiş satır yüksekliğini ortalamadan tahmin etmesi ve sticky header /
follow-mode / selection-scroll'un okuduğu kümülatif tablonun kayması. Kullanıcı kararıyla (2026-08-03) bu
erteleme kapanıyor: **sabit yükseklikli özel virtualizing panel** yazılacak — 36 px satır / 24 px başlık
sabit olduğu için tahmin değil **kesin** aritmetik kurulabilir, yani §20'nin drift gerekçesi ortadan kalkar.

### B3. Open-in-VS UI thread'inde sync-over-async

[OsActions.cs:155](../../src/BuildOrchestrator.App/Services/OsActions.cs#L155)
`Task.Run(...).GetAwaiter().GetResult()`, spec timeout [:152](../../src/BuildOrchestrator.App/Services/OsActions.cs#L152) = **30 s**.
Satır hover ikonundan (UI click) çağrılıyor → `vswhere` dönene kadar pencere ölü.

## Bulgular — önemli

### Ö1. IPC event'leri `DispatcherPriority.Normal` ile marshal ediliyor

[MainWindow.xaml.cs:216-220](../../src/BuildOrchestrator.App/MainWindow.xaml.cs#L216-L220).
WPF öncelikleri: **Normal(9) > DataBind(8) > Render(7) > Loaded(6) > Input(5)**. Kuyrukta event varken WPF
ne çizer ne de tıklama/klavye işler. Tek başına sebep değil; **her donmayı "tam kilitlenme"ye çeviren çarpan** bu.

### Ö2. Pano yazma UI thread'inde uyuyor

[ClipboardRetry.cs:41-45](../../src/BuildOrchestrator.App/Console/ClipboardRetry.cs#L41-L45) —
`Thread.Sleep` × 10 deneme = en kötü ~100 ms bloke (kendi doc'unda da yazılı).

### Ö3. Çıkışta bloklu bekleme

[AppShutdown.cs:35](../../src/BuildOrchestrator.App/Shell/AppShutdown.cs#L35) `.Wait(timeout)` — sınırlı ve
kasıtlı (process çıkışı), ama "asla bloke etme" kuralı altında gözden geçirilecek.

## Suçsuz (kanıtla — yanlış yere fix yapılmasın)

- **Graf:** 500 düğüm inşası 35 ms, statü tick'i 0.1 ms. Kullanıcının "graph çizilirken dondu" dediği an,
  aynı senkron bloğun içindeki **listenin** realize oluşu.
- **Topoloji imza guard'ı** ([RunViewModel.Workspace.cs:268-273](../../src/BuildOrchestrator.App/ViewModels/RunViewModel.Workspace.cs#L268-L273)):
  aynı topolojiyle 2. Sync 0.7 ms — çalışıyor.
- **Event stream:** 150 satırla sınırlı (front-trim), donma kaynağı değil.

## Doküman ↔ kod çelişkisi (karar kullanıcının, sessizce seçilmedi)

ARCHITECTURE.md:881 "Event stream. **A virtualized list** of chronological one-line events" diyor. Kod
([EventStreamView.xaml:26](../../src/BuildOrchestrator.App/Views/EventStreamView.xaml#L26)) satırları
kod-tarafı bir `StackPanel`e (`PART_Rows`) ekliyor — virtualization **yok**. 150 satır tavanı olduğu için
performans sorunu değil; yalnız doküman yanlış.

---

## TDD planı — task-by-task

Her task: **önce kırmızı test**, sonra fix, sonra yeşil. Ölçüm testleri bütçe sabitleriyle kalıcı guard olur.

### T1 — Envanter yayını (B1)

- **Kırmızı:** `InventoryPublishTests` — 475 branch'lik `BranchListEvent`, UI thread bloğu **< 50 ms** olmalı.
  Bugün ~36.000 ms.
- **Kırmızı:** aynı envanter ikinci kez yayınlandığında **hiç** `CollectionChanged` çıkmamalı.
- **Kırmızı:** popover KAPALIYKEN envanter değişimi **hiç satır inşa etmemeli**.
- **Fix:**
  1. `Replace<T>` → kimlik bazlı **diff reconcile** (değişmemiş envanterde sıfır bildirim).
  2. Aboneler bildirim başına değil, **frame başına bir kez** tazelensin (coalesced refresh).
  3. Popover içeriği yalnız **açıkken** kurulsun (`IsOpen` kapısı).
  4. Popover satır listesi virtualized `ItemsControl`'e geçsin (kod-tarafı `Children.Add` yerine).

### T2 — Proje listesi (B2)

- **Kırmızı:** 177 satırlık ilk Sync'te en uzun tek UI bloğu **< 50 ms** olmalı. Bugün ~600 ms.
- **Kırmızı:** filtre değişimi (0↔177) **< 50 ms**. Bugün ~600 ms.
- **Fix:** sabit yükseklikli (36 px satır / 24 px başlık) özel virtualizing panel + `ItemsSource` reset yerine
  yerinde uzlaştırma.
- **Regresyon (zorunlu):** sticky header konumları, follow-mode, selection-scroll, reveal stagger kapsamı —
  dördü de kümülatif tabloyu okuyor; her biri için ayrı test.

### T3 — Open-in-VS (B3)

- **Kırmızı:** `vswhere` 2 s sürerken UI thread'i bloke olmamalı.
- **Fix:** `IOsActions` yolu asenkron; `devenv` yolu bir kez çözülüp önbelleklensin.

### T4 — Dispatcher önceliği (Ö1)

- **Kırmızı:** event akışı altında Input/Render önceliğindeki bir iş **aç kalmamalı**.
- **Fix:** IPC marshal'ı input/render'ı açlığa düşürmeyen bir önceliğe/pompaya alınsın; **sıra korunacak**.

### T5 — Pano (Ö2) ve çıkış (Ö3)

- Pano retry'ı UI thread'inde uyumasın; çıkış bekleyişi gözden geçirilsin.

### T6 — Kalıcı guard

- Tanı testi (`SyncUiBlockDiagnosticTests`) bütçeli kalıcı teste dönüşür: **Sync yolunda hiçbir tek UI bloğu
  50 ms'i aşamaz** (177 proje + 475 branch ölçeğinde).

### T7 — Doküman

- ARCHITECTURE.md §20 "project list is not virtualized" maddesi yeniden yazılır; §13 liste/envanter davranışı
  güncellenir. Event-stream çelişkisi kullanıcı kararına göre düzeltilir.
