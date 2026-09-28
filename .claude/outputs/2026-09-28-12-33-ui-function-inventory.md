# Arayüz İşlev Envanteri — App Build Orchestrator

- **Tarih:** 2026-09-28 · **Branch:** `main` (`35ddc41`)
- **Yöntem:** Her işlev XAML'dan başlanarak kodda izlendi: kontrol → code-behind → ViewModel komutu → IPC komutu →
  Supervisor handler → Core servisi. "Dolu" = bu zincir uçtan uca var. Çalışma zamanı kanıtı olarak Acceptance
  dışı test süiti Release'te koşuldu (sonuç en altta, §17).
- **Kısaltmalar:** `App/` = `src/BuildOrchestrator.App/` · `Sup/` = `src/BuildOrchestrator.Supervisor/` ·
  `Core/` = `src/BuildOrchestrator.Core/` · `Contracts/` = `src/BuildOrchestrator.Contracts/`.
  Aynı dosyada devam eden satırlar yalnız `:satır` ile yazıldı.

## Lejant

| İşaret | Anlamı |
|---|---|
| ✅ Dolu | Tıklama gerçek bir işe bağlı, zincir uçtan uca var |
| ⚠️ Yarım | Arka ucu var ama arayüzden açılamıyor (ya da tersi) |
| ❌ Boş | Arayüzde duruyor, altında iş yok (pasif ya da handler'sız) |
| ℹ️ Gösterge | Tıklanmaz, yalnız durum gösterir |

## Özet

| Durum | Adet | Hangileri |
|---|---|---|
| ✅ Dolu | 90 | aşağıdaki tablolar |
| ❌ Boş | 6 | Build ▾ menüsü → **Clean** · proje listesinin **`⌄ latest`** pill'i · Settings → General: **Start with Windows**, **Start minimized to tray**, **Close to tray**, **Show notifications** |
| ⚠️ Yarım | 2 | **Autostart** (altyapı hazır, açan UI yok) · **Alt+B** global kısayolu (çalışıyor, değiştiren UI yok) |
| ℹ️ Gösterge | 3 | işlem pill'i, faz satırı + ilerleme çubuğu, workspace etiketi |

Ayrıca: grafın klavyeyle gezilememesi bilinçli bir karardır (ARCHITECTURE §20), boş sayılmadı.

---

## 1. Başlık çubuğu (en üst, sağ)

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 1 | Küçült (—) | ✅ | Pencereyi simge durumuna küçültür | `App/MainWindow.xaml.cs:1279` |
| 2 | Büyüt / Geri al (□) | ✅ | Maximize ↔ restore | `:1280`, `:1210-1214` |
| 3 | Kapat (×) | ✅ | Uygulamayı **kapatmaz**; pencereyi tepsiye gizler, ilk seferde bir kez OS balonu çıkar. Gerçek çıkış: tepsi → Exit | `:1281`, `:1251-1262`, `:1217-1221` |
| 4 | Görünüm: Quad | ✅ | Graf + liste · konsol + stream; üç ayracı 50/50/50'ye sıfırlar | `:923`, `App/ShellRoot.xaml.cs` (`SetMode`) |
| 5 | Görünüm: List | ✅ | Graf gizlenir, sol kolon yalnız liste | `:924` |
| 6 | Görünüm: Focus | ✅ | Graf gizlenir, konsol sağ kolonun %76'sı | `:925` |
| 7 | ⚙ Settings | ✅ | Ayarlar diyaloğunu açar (başka diyalog açıksa hiçbir şey yapmaz) | `:931-936` |
| 8 | ✨ What's new | ✅ | Sürüm notlarını açar/kapatır; görülmemiş sürümde amber nokta, açınca söner | `:1002-1006`, `:491-498` |
| 9 | ⓘ About | ✅ | About diyaloğunu açar/kapatır | `:989-993` |

Görünüm modu ve ayraç konumları `ui-state.json`'a yazılır (`:1067-1073`).

## 2. Durum şeridi (ribbon — başlığın hemen altı)

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 10 | İşlem pill'i (`SYNC` · `BUILD` · `REBUILD` · `CLEAN` · `DEEP CLEAN` · `OPTIMIZE` · `RESOLVE` · `SWITCHING BRANCH`) | ℹ️ | Son istenen işlemi gösterir; koşarken amber + spinner | `App/Views/StickyRibbon.xaml.cs` |
| 11 | Faz satırı + 2px ilerleme çubuğu | ℹ️ | Motor ölümü / sessizlik / Sync hatası / koşu hatası mesajları da burada | aynı |
| 12 | Derlenen proje chip'leri (en çok 4, sonra düz `+N`) | ✅ | Tık → o projeyi seçer (konsol proje loguna geçer, graf odaklanır). `+N` tıklanmaz | `:438`, `:445-459` |
| 13 | Hatalı proje chip'leri (ilk 3) | ✅ | Tık → o projeyi seçer | `:487` |
| 14 | `+N more` | ✅ | Tık → listeyi yalnız ✗ (failed) filtresine indirir | `:504-512` |
| 15 | Restart engine | ✅ | Motor ölünce ya da uzun süre sessiz kalınca (yeniden başlatılabiliyorsa) görünür; Supervisor'ı yeniden başlatır, workspace varsa ardından Sync | `App/Views/StickyRibbon.xaml:66`, `App/Views/StickyRibbon.xaml.cs:232-235` → `App/ViewModels/RunViewModel.cs:1452-1478` |

## 3. Bağımlılık grafı (sol üst)

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 16 | Düğüme tık | ✅ | Seçer; ikinci tık seçimi kaldırır. Liste ve konsol aynı seçimi izler | `App/Graph/GraphView.xaml.cs:989-995` |
| 17 | Düğüm hover | ✅ | Gecikmesiz ad/durum etiketi; listedeki satır da hover olur (karşılıklı) | `:997-998`, `App/Graph/GraphHoverEcho.cs` |
| 18 | Boşluğa tık | ✅ | Seçim varsa kaldırır; yoksa kamerayı varsayılan (sığdır) görünüme döndürür | `:1716-1725` |
| 19 | Sürükle | ✅ | Pan (3px eşiğinden sonra) | `:238-248`, `:1691-1709` |
| 20 | Fare tekerleği | ✅ | İmleç merkezli zoom | `:252-256`, `:1729-1738` |

Graf klavyeyle gezilmez (sekme durağı, ok tuşu yok) — bilinçli karar, klavye yolu proje listesidir
(ARCHITECTURE §20).

## 4. Proje listesi (sol alt)

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 21 | `Filter…` kutusu | ✅ | Proje adında alt-dize araması (chip filtreleriyle VE'lenir); graf eşleşmeyenleri soldurur. Ctrl+F odaklar, Esc temizler ve odaktan çıkar, ✕ temizler | `App/ShellRoot.xaml:68-69`, `App/ShellRoot.xaml.cs:89-95`, `App/MainWindow.xaml.cs:1050-1056` |
| 22 | Başlıktaki aktif filtre chip'i (ör. `Failed + Up to date ✕`) | ✅ | Tık → tüm chip filtrelerini kaldırır (Σ ile aynı yol) | `App/MainWindow.xaml.cs:255` |
| 23 | Katman başlığına tık (yapışkan kopya dahil) | ✅ | O katmanın ilk satırına yumuşak kaydırır; seçim/filtre/konsola dokunmaz; yalnız fare | `App/Controls/StickyLayerList.xaml.cs:371-432` |
| 24 | Satıra tık / Enter / Space | ✅ | Seçer (tekrar → kaldırır); konsol o projenin loguna geçer (`GetProjectLogCommand`), graf düğüme odaklanır | `App/Views/ProjectRow.xaml.cs:786-790`, `:848-855`, `App/ViewModels/RunViewModel.cs:2776` |
| 25 | ▶ Build this project (hover) | ✅ | Yalnız o projeyi derler, bağımlılıklarını derlemez; güncel olsa bile derler (`-t:Build`) | `ProjectRow.xaml.cs:134` → `RunViewModel.cs:1156-1157` → `Core/Planning/ProjectRunScope.cs:79` |
| 26 | ■ Stop (hedef satırda, koşu sürerken) | ✅ | Aksiyon barındaki Stop'un aynısı (graceful) | `ProjectRow.xaml.cs:135` |
| 27 | ⋯ menü / satıra sağ tık → Build | ✅ | ▶ ile aynı | `ProjectRow.xaml.cs:553-565`, `:808-813` |
| 28 | ⋯ menü → Rebuild | ✅ | Yalnız o projede MSBuild `-t:Rebuild` | `RunViewModel.cs:1161-1162`, `Sup/RunCoordinator.cs:1023` |
| 29 | ⋯ menü → Clean | ✅ | Yalnız o projede `msbuild -t:Clean`; projenin build-state kaydı silinir, sonraki Build baştan derler | `RunViewModel.cs:1168-1169`, `Sup/RunCoordinator.cs:1022` |
| 30 | 📁 Reveal in Explorer (hover) | ✅ | `explorer.exe /select,"<csproj>"` + konsola not | `ProjectRow.xaml.cs:860-863` → `RunViewModel.cs:2695-2700` → `App/Services/OsActions.cs:110-118` |
| 31 | Open in Visual Studio (hover) | ✅ | vswhere ile devenv bulunur, projenin .sln'i açılır; birden çok .sln varsa seçim popover'ı; VS yoksa konsola "Visual Studio not found" | `ProjectRow.xaml.cs:868-915` → `RunViewModel.cs:2710-2738` → `OsActions.cs:120-137` |
| 32 | `⌄ latest` pill'i (liste) | ❌ | **Boş.** XAML'de hep `Collapsed`; görünür yapan ve `Click`'ine abone olan kod yok, yalnız erişilebilirlik adı atanıyor (konsol ve stream'deki eşleri çalışıyor) | `App/ShellRoot.xaml:187`, `App/ShellRoot.xaml.cs:50` |
| 33 | Boş durum → Open settings | ✅ | Settings'i açar (ilk kurulumda Workspace sayfasında) | `App/MainWindow.xaml.cs:224`, `:931` |
| 34 | Boş durum → Import settings… | ✅ | Settings'i açar ve dosya seçiciyi hemen tetikler | `:225`, `:940-945` |

Koşu sürerken diğer satırların ▶ ve menü maddeleri pasiftir, tooltip nedenini söyler
(`Build in progress — wait or stop it first`).

## 5. Konsol (sağ üst)

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 35 | ← Back (proje logu modunda) | ✅ | Seçimi kaldırır, koşu anlatısına döner | `App/Console/ConsoleHeader.xaml.cs:166` → `App/MainWindow.xaml.cs:359`, `:649` |
| 36 | Copy log | ✅ | **Yalnız proje logu modunda** (listeden bir proje seçiliyken) ve log boş değilken, başlığın sağında `N lines`'ın solunda görünür; o projenin tam logunu panoya kopyalar, ✓ "Copied" geri bildirimi. Normal "CONSOLE" (anlatı) görünümünde düğme yoktur — orada metin seçilip Ctrl+C ile kopyalanır | `ConsoleHeader.xaml.cs:68-77`, `:160-163`, `:239-255`, `App/Console/ConsoleHeader.xaml:110-125` |
| 37 | Metin seçimi / Ctrl+C | ✅ | Salt-okur AvalonEdit editörü; seçip kopyalama çalışır | `App/Console/ConsoleView.xaml:38-47` |
| 38 | `⌄ latest` pill'i (konsol) | ✅ | Yukarı kaydırılınca görünür; tık → yumuşakça en alta | `App/Console/ConsoleView.xaml.cs:813` |

## 6. Event stream (sağ alt)

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 39 | Proje satırına tık | ✅ | O projeyi seçer | `App/Views/EventStreamView.xaml.cs:658` |
| 40 | Alttaki canlı `X building…` satırına tık | ✅ | Derlenen projeyi seçer | `:251-254` |
| 41 | `⌄ latest` pill'i (stream) | ✅ | En alta atlar | `:93` |

## 7. Ayraçlar

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 42 | Kolon / graf-liste / konsol-stream ayracı | ✅ | Sürükle ya da odaklanıp ok tuşlarıyla boyutlandırır; konum diske yazılır (kolon %28–72, satır %18–82) | `App/ShellRoot.xaml.cs:39-41`, `App/Controls/DsSplitter.cs:110-117` |

## 8. Alt aksiyon barı — sol grup

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 43 | Sync | ✅ | `git fetch` + tam analiz (tarama, graf, katmanlar, incremental kararlar) + branch listesi; konsol/stream temizlenir, liste ve graf baştan belirir | `App/Views/ActionBar.xaml:85` → `RunViewModel.cs:1181-1269` → `Sup/SupervisorHost.cs:124-125` |
| 44 | Bakım → Clean (silgi) | ✅ | Keşfedilen her projenin (harici kökler dahil) `bin`/`obj` klasörlerini siler, build-state'i sıfırlar; onay yok; MSBuild çağırmaz; bitince otomatik Sync | `App/Views/MaintenanceBox.xaml.cs:101` → `RunViewModel.cs:1288-1313` → `SupervisorHost.cs:126-127` |
| 45 | Bakım → Optimize (gösterge) | ✅ | Eksik NuGet paketlerini restore eder, restore'un çözemediği kırık referansları raporlar, `obj` içindeki bozucu artıkları ve ölü defter kayıtlarını temizler; bitince otomatik Sync | `MaintenanceBox.xaml.cs:104` → `RunViewModel.cs:1335-1355` → `SupervisorHost.cs:128-129` |
| 46 | Bakım → Resolve cycles | ✅ | Döngüsel bağımlılık (SCC) üyelerini (+ kirli upstream'lerini) yakınsayana kadar turlarla derler; döngü yoksa pasif | `MaintenanceBox.xaml.cs:97` → `RunViewModel.cs:1148-1149`, `:1176` |
| 47 | Σ chip'i | ✅ | Tüm filtreleri temizler | `App/Views/ActionBar.xaml.cs:248` |
| 48 | Derleniyor chip'i | ✅ | Filtre: şu an derlenenler | `:251` |
| 49 | ✓ chip'i | ✅ | Filtre: güncel (yeşil) satırlar | `:259`, `:276` |
| 50 | ✗ chip'i | ✅ | Filtre: hatalı (kırmızı) satırlar | `:260`, `:276` |
| 51 | ⚠ chip'i (yalnız >0 iken görünür) | ✅ | Filtre: döngüde olan ya da bağımlılık sorunu bekleyen satırlar | `:268`, `:353` |

Chip'ler birlikte seçilebilir; seçilenlerin birleşimi (VEYA) listelenir. Filtreye basmak seçimi düşürür
(`App/ViewModels/RunViewModel.ActionBar.cs:26-34`).

## 9. Alt aksiyon barı — sağ grup

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 52 | Workspace etiketi | ℹ️ | Repo kök klasörünün adı; tooltip tam yol | `ActionBar.xaml.cs:509-516` |
| 53 | Branch chip'i + popover (aramalı) | ✅ | Seçilen branch'e gerçek `git checkout` (uzak branch'te izleyen yerel branch kurulur); kirli ağaçta Settings'teki "Stash and switch" ayarına göre durur ya da `git stash push -u` yapar; başarılıysa Sync. Git'te yarım işlem (merge/rebase…) varsa amber nokta + kilit | `App/Views/BranchPopover.xaml.cs:110-120` → `RunViewModel.ActionBar.cs:57-75` → `SupervisorHost.cs:136-137` |
| 54 | `N behind` chip'i | ✅ | Yalnız aktif branch'i uzak uca `ff-only` ilerletir (kirli/ayrışmış ağaçta reddeder), sonra Sync. Yalnız geride kalınca görünür | `ActionBar.xaml.cs:438` → `App/ViewModels/RunViewModel.Workspace.cs:468-481` → `SupervisorHost.cs:134-135` |
| 55 | Debug \| Release | ✅ | Configuration değişir (sonraki Sync/Build ile motora gider); tüm projeler "yeniden derlenecek" işaretlenir; koşu sırasında kilitli | `ActionBar.xaml.cs:540-545` → `RunViewModel.cs:1584-1606` |
| 56 | perf chip'i | ✅ | Full → Balanced → Light döngüsü (paralellik + CPU cap + öncelik). Koşu sürerken de canlı: CPU cap/öncelik hemen, paralellik sonraki koşuda | `ActionBar.xaml.cs:76` → `RunViewModel.cs:1642-1650` → `SupervisorHost.cs:132-133` |
| 57 | Build (birincil düğme) | ✅ | Yalnız bayat projeleri derler (döngü üyelerini derlemez). F5 | `ActionBar.xaml.cs:584-585` → `RunViewModel.cs:1133-1134` |
| 58 | Build ▾ → Build | ✅ | Aynısı | `App/Views/BuildMenu.xaml.cs:170` |
| 59 | Build ▾ → Rebuild | ✅ | Cache'i yok sayıp döngü dışı tüm projeleri derler (`-t:Build`). Ctrl+F5 / Shift+F5 | `BuildMenu.xaml.cs:171` → `RunViewModel.cs:1112-1113` |
| 60 | Build ▾ → Clean | ❌ | **Boş.** Madde pasif çizilir, tooltip `Clean — msbuild /t:Clean on every solution; caches are untouched — not available yet`; tıklama hiçbir komuta bağlı değil. Motorun tek-proje `-t:Clean`'i var (#29), tüm-solution kapsamı yazılmamış | `BuildMenu.xaml.cs:151-159`, `:168-173`, `App/AccessibilityNames.cs:74-75`, `Contracts/Ipc/IpcMessages.cs:96` |
| 61 | Stop (koşu sürerken Build'in yerinde) | ✅ | Graceful: yeni proje başlatılmaz, uçuştakiler biter. İşaretleme dalgası sırasında basılırsa istek yerel olarak iptal edilir (`Cancelled — build not started`). Koşarken F5 da Stop'tur | `App/Views/ActionBar.xaml:68` → `RunViewModel.cs:1389-1406` |

## 10. Settings diyaloğu

Sol ray (General · Workspace · External projects · Layers) sayfa değiştirir ✅ (`App/Views/SettingsDialog.xaml.cs:88-108`).

| # | İşlev | Yer | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|---|
| 62 | Start with Windows | General | ❌ | **Yalnız taslak:** kaydedilmez, dosyaya yazılmaz, hiçbir davranışı yok; her açılışta varsayılana döner | `App/ViewModels/GeneralSettings.cs:6-15`, `App/ViewModels/SettingsDraftViewModel.cs:84-89` |
| 63 | Start minimized to tray | General | ❌ | Aynı (yalnız "Start with Windows" açıkken etkin görünür) | aynı |
| 64 | Close to tray | General | ❌ | Aynı — anahtar ne olursa olsun × her zaman tepsiye gizler | aynı, `App/MainWindow.xaml.cs:1251-1262` |
| 65 | Pull before build | General | ✅ | Kaydedilir (`UpdateExternals`); her Build'den önce harici çalışma kopyalarını `fetch + merge --ff-only` ile günceller | `SettingsDraftViewModel.cs:100-104`, `:261`, `RunViewModel.ActionBar.cs:159-167` |
| 66 | Stash and switch branches | General | ✅ | Kaydedilir; branch değişiminde kirli ağaç `git stash push -u` ile saklanıp geçilir (kapalıysa değişim durur) | `SettingsDraftViewModel.cs:109-113`, `RunViewModel.ActionBar.cs:67`, `:175-182` |
| 67 | Show notifications | General | ❌ | Yalnız taslak — anahtar ne olursa olsun pencere gizliyken biten koşu bildirimi her zaman gösterilir | `GeneralSettings.cs:22-23`, `App/Shell/AppTrayIcon.cs:118-123` |
| 68 | Repository root + Browse… | Workspace | ✅ | Taslağa yazar; Save'de kök değişir, liste boşalır, Sync gider. Boşsa Save kapalı | `SettingsDialog.xaml.cs:187-192`, `RunViewModel.ActionBar.cs:221-240` |
| 69 | Add external project / sil / yol / sürükle-sırala | External projects | ✅ | Kaydedilir; yollar her Sync/Build'de taranıp aynı grafa girer, en önce derlenir; kart sırası güncelleme sırasıdır | `SettingsDialog.xaml.cs:174-179`, `SettingsDraftViewModel.cs:233-248` |
| 70 | "Pull before build" bağlantısı | External projects | ✅ | General sayfasına götürür | `SettingsDialog.xaml.cs:182` |
| 71 | Add layer / Delete / ad / regex / sürükle-sırala | Layers | ✅ | Kaydedilir; desenler Sync ile motora gider, liste katmanlara gruplanır. Geçersiz regex kırmızı + Save kapalı | `SettingsDialog.xaml.cs:165-170`, `SettingsDraftViewModel.cs:229-241` |
| 72 | Export (↓) | Footer | ✅ | Formu JSON'a yazar: kök, katmanlar, harici projeler, Pull before build, Stash. Bağlanmamış 4 anahtar, Debug/Release, perf, hotkey ve yerleşim dosyaya **girmez** | `SettingsDialog.xaml.cs:196-209`, `App/ViewModels/SettingsFile.cs:30-60` |
| 73 | Import (↑) | Footer | ✅ | JSON'u yalnız **forma** yükler (Save'e kadar uygulanmaz); dosyada olmayan anahtar mevcut değeri korur | `SettingsDialog.xaml.cs:211-222`, `SettingsDraftViewModel.cs:194-214` |
| 74 | Clear (çöp kutusu) | Footer | ✅ | İki aşamalı (2.4 sn içinde ikinci tık): formu boşaltır (kök, katmanlar, harici projeler; anahtarlar varsayılana). Save'e kadar uygulanmaz | `SettingsDialog.xaml.cs:226-241` |
| 75 | Cancel / × / Esc / arka plana tık | Footer/başlık | ✅ | Taslağı atar | `SettingsDialog.xaml.cs:281` |
| 76 | Save (ilk kurulumda "Save and sync") | Footer | ✅ | `ui-state.json`'a yazar + uygular + tek Sync. Koşu/işlem sürüyorsa kök değişimi ertelenir (konsola not). Kök/harici yol/katman adı boşsa ya da regex geçersizse kapalı, nedeni footer'da | `SettingsDialog.xaml.cs:272-278`, `SettingsDraftViewModel.cs:153-168`, `:254-265` |

## 11. About ve What's new

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 77 | About sekmeleri: About · Environment · Shortcuts | ✅ | Environment ilk açılışta MSBuild yolunu vswhere ile çözer; Shortcuts kısayol tablosunu gösterir (Alt+B kaydolmadıysa "unavailable") | `App/Views/AboutDialog.xaml.cs:110-136`, `:166-172` |
| 78 | About → What's new in X | ✅ | About'u kapatıp What's new'i açar | `:295-299` |
| 79 | About → Copy diagnostics | ✅ | Sürüm/motor/runtime/yollar raporunu panoya kopyalar | `:238-253` |
| 80 | About → Close | ✅ | Kapatır | `:291` |
| 81 | What's new → Earlier versions (N) | ✅ | Eski sürümleri açar — bugün listede tek sürüm olduğu için düğme görünmez | `App/Views/NotesDialog.xaml.cs:77-96`, `App/Services/ReleaseNotes.cs:88-144` |
| 82 | What's new → Close | ✅ | Kapatır; diyalog açıldığı anda sürüm "görüldü" yazılır ve sparkle'daki nokta söner | `NotesDialog.xaml.cs:65-73` |

## 12. Klavye kısayolları

| # | Kısayol | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 83 | F5 | ✅ | Build; koşu sürerken Stop | `App/Shell/KeyboardShortcuts.cs:79`, `:105-115` |
| 84 | Ctrl+F5 / Shift+F5 | ✅ | Rebuild | `:77-78` |
| 85 | Ctrl+F | ✅ | Proje filtresine odak | `:80` |
| 86 | F1 | ✅ | About aç/kapat | `:81` |
| 87 | Ctrl+F1 | ✅ | What's new aç/kapat | `:82` |
| 88 | Esc | ✅ | En üstteki katmanı kapatır: diyalog → popover/menü → seçim | `:83`, `App/MainWindow.xaml.cs:516-528` |
| 89 | Alt+B (global) | ⚠️ | Çalışıyor: pencereyi tepsiden getirir. Değiştirmek için UI yok — yalnız `ui-state.json` → `Hotkey`. Çakışırsa sessizce kapanır (About'ta "unavailable") | `App/MainWindow.xaml.cs:1127-1132`, `:1202-1208`, `App/Shell/UiStateStore.cs:20` |

## 13. Tepsi, arka plan ve otomatik davranışlar

| # | İşlev | Durum | Ne yapıyor | Kanıt |
|---|---|---|---|---|
| 90 | Tepsi ikonu sol tık / çift tık / balona tık | ✅ | Pencereyi geri getirir | `App/Shell/AppTrayIcon.cs:65-69` |
| 91 | Tepsi menüsü → Stop | ✅ | Koşan build'i graceful durdurur (koşu yoksa hiçbir şey yapmaz) | `AppTrayIcon.cs:50-51`, `App/MainWindow.xaml.cs:1122` |
| 92 | Tepsi menüsü → Exit | ✅ | Gerçek çıkış; job object'ler motoru ve tüm MSBuild process'lerini kapatır | `AppTrayIcon.cs:52-53`, `MainWindow.xaml.cs:1241-1245` |
| 93 | Ekranın sağ altındaki animasyonlu logo | ✅ | Pencere gizliyken build sürerken görünür; tık → pencereyi getirir | `App/Views/TrayBuildOverlayWindow.xaml.cs:62` |
| 94 | Koşu bitti bildirimi (OS balonu) | ✅ | Pencere gizliyken biten koşunun sonucu (şeridin son satırı). Settings'teki "Show notifications" bunu kontrol **etmez** | `AppTrayIcon.cs:118-123` |
| 95 | İlk × kapatmada bilgi balonu | ✅ | Bir kez gösterilir | `MainWindow.xaml.cs:1217-1221` |
| 96 | Tek instance | ✅ | İkinci açılış mevcut pencereyi öne getirir; getiremezse balon + ayrı çıkış kodu | `App/App.xaml.cs:72-99` |
| 97 | Otomatik Sync | ✅ | HEAD değişince (commit, dışarıdan checkout) ve pencereye dönünce (son Sync'ten 5 sn+ geçmişse) sessiz Sync | `App/ViewModels/RunViewModel.AutoSync.cs`, `App/Services/AutoSyncCoordinator.cs`, `MainWindow.xaml.cs:324-325` |
| 98 | Koşu sırasında branch değişirse kesme | ✅ | `StopKind.Interrupt`: kesmeden sonra biten projeler deftere başarı olarak yazılmaz | `Contracts/Ipc/IpcMessages.cs:60-63` |
| 99 | Git'te yarım işlem algısı | ✅ | Merge/rebase/cherry-pick… varken branch chip'inde amber nokta, checkout/pull kilitli, tooltip nedeni söyler | `App/Views/ActionBar.xaml.cs:410-416` |
| 100 | Motor sessizlik bekçisi | ✅ | Motor uzun süre olay göndermezse şeritte amber satır | `RunViewModel.cs` (`ArmEngineWatchdog`) |
| 101 | Autostart (Windows'la başlama) | ⚠️ | **Altyapı hazır, açan UI yok.** Her açılışta `ui-state.json` → `Autostart` değerine göre `HKCU\...\Run`'a `"<exe>" --autostart` yazılır/silinir; `--autostart` ile tepside gizli başlar. Ama bu değeri yazan hiçbir kod yok ("Start with Windows" anahtarı bağlı değil) — yalnız dosya elle düzenlenirse çalışır | `App/App.xaml.cs:115-119`, `:127`, `App/Services/AutostartService.cs:53-64`, `App/Shell/UiStateStore.cs:93` |

---

## 14. Boş / yarım olanların ayrıntısı

1. **Build ▾ → Clean (#60).** Tasarımdaki yerinde, bilinçli olarak pasif bırakılmış (`AccessibilityNames.NotAvailableSuffix`
   yorumu: "arka ucu henüz yazılmamış bir yüzey"). ARCHITECTURE §13.2 de "the one surface in the bar whose engine is not
   written" diyor. Motorda `RunMode.Clean` var ama bugün yalnız satır menüsünden `ScopeProjectId` ile gönderiliyor
   (`Contracts/Ipc/IpcMessages.cs:96`).
2. **Proje listesi `⌄ latest` pill'i (#32).** Kontrol yerleştirilmiş, adı verilmiş, testi de yalnız adını pinliyor
   (`tests/BuildOrchestrator.Tests/App/AccessibilityTests.cs:179-183`). Görünürlük ve tıklama kablosu hiç yazılmamış.
   Konsol ve stream'deki eşleri `BottomAnchorBehavior` ile çalışıyor; listenin karşılığı "frontier'e dön" olurdu.
3. **General'daki 4 anahtar (#62, #63, #64, #67).** ARCHITECTURE §20 "Known limits" bunu açıkça kabul ediyor:
   "Four General switches are not wired yet". What's new de "they take effect in a later version" diyor. Kodda bu
   anahtarları okuyan tek satır yok (yalnız `GeneralSettings.cs` kataloğunda tanımlı).
   - "Close to tray" ve "Show notifications" için davranış **hep açık**: × her zaman tepsiye gizler, gizliyken biten
     koşu her zaman bildirim verir. Anahtarı kapatmak bunu değiştirmez.
4. **Autostart (#101).** Registry yazıcısı, `--autostart` açılış yolu ve her açılıştaki uzlaştırma çalışıyor; eksik olan
   tek parça `UiState.Autostart`'ı yazan UI. "Start with Windows" anahtarı bağlandığında büyük ölçüde hazır bir
   altyapıya oturacak.
5. **Alt+B (#89).** Kısayol çalışıyor; yalnız ayar ekranı yok (ARCHITECTURE §12.3 ve §20 kabul ediyor).

## 15. Kodda olup arayüzde olmayanlar

| Öğe | Durum | Kanıt |
|---|---|---|
| Zorla durdurma (`StopKind.Hard`) | Kontratta ve motorda var, App göndermiyor — Stop her zaman graceful | `Contracts/Ipc/IpcMessages.cs:63`, `App/ViewModels/RunViewModel.cs:1367-1372` |
| `RunViewModel.ChangeRepositoryAsync` | Üretimde çağıranı yok, yalnız testler kullanıyor (eski "Choose Folder" yolu kaldırıldı) | `App/ViewModels/RunViewModel.ActionBar.cs:246`, `App/MainWindow.xaml.cs:1033-1038` |
| `DebugSpawnChildrenCommand` | Motor karşılıyor, App göndermiyor (test/debug amaçlı) | `Sup/SupervisorHost.cs:122-123` |
| `--font-ab` | Font A/B geliştirici penceresi, kullanıcı yüzeyi değil | `App/Shell/StartupArgs.cs:37-44`, `App/Spikes/FontAbWindow.xaml.cs` |

## 16. Metin ve yorum tutarsızlıkları (düzeltilmedi, yalnız rapor)

| # | Nerede | Söylenen | Koddaki gerçek |
|---|---|---|---|
| 1 | What's new (uygulama içi sürüm notu) — `App/Services/ReleaseNotes.cs:100` | "The Build menu offers Clean — msbuild /t:Clean on every solution." | Madde pasif, "not available yet" (#60). ARCHITECTURE §13.2 kodla uyumlu |
| 2 | What's new — `ReleaseNotes.cs:105`, `:141` | Satır etiketleri "failed · retry", "up to date with the age of its last successful build", "affected · up to date · 2h" | `DecisionLabel.cs`'te ne `retry` kuyruğu ne süre var (`:48`, `:67`); tek kuyruk `local`. ARCHITECTURE §13.2 de "No label and no tooltip carries a time" diyor |
| 3 | Kod yorumu — `App/ViewModels/RunViewModel.cs:1151-1155`, `Contracts/Ipc/IpcMessages.cs:125` | Satırdan ▶ Build: "güncelse `up to date` atlanır" / "Build modunda incremental kural" | Hedef her zaman derlenir: `Core/Planning/ProjectRunScope.cs:79` (`WillBuild = true`). ARCHITECTURE §8.1 kodla uyumlu |
| 4 | XAML yorumu — `App/Views/SettingsDialog.xaml:159` | "Yalnız Pull before build davranışa bağlıdır; diğer dört anahtar henüz yalnız taslaktır" | 6 anahtar var; 2'si bağlı (Pull before build + Stash and switch), 4'ü taslak |
| 5 | Kod yorumu — `App/Shell/UiStateStore.cs:44-47` | `SeenVersion`: "ⓘ düğmesinde nokta, About doğrudan o sekmede açılır" | Nokta artık ✨ What's new düğmesinde, notlar kendi diyaloğunda (`MainWindow.xaml.cs:474-498`) |

## 17. Motor (Supervisor) komut haritası

| IPC komutu | Gönderen arayüz | Handler |
|---|---|---|
| `StartRun` (Build) | Build, F5, ▶, ⋯ → Build | `Sup/SupervisorHost.cs:116` → `RunCoordinator.StartAsync` |
| `StartRun` (Rebuild) | Build ▾ → Rebuild, Ctrl/Shift+F5, ⋯ → Rebuild | aynı |
| `StartRun` (Cycles) | Resolve cycles | aynı |
| `StartRun` (Clean, tek proje) | ⋯ → Clean | aynı |
| `StopRun` (Graceful / Interrupt) | Stop, F5 (koşarken), tepsi → Stop / branch değişimi | `:118` |
| `SyncWorkspace` | Sync, Settings Save, pull/Clean/Optimize/checkout sonrası, otomatik Sync, açılış | `:124` |
| `CleanWorkspace` | Bakım → Clean | `:126` |
| `OptimizeWorkspace` | Bakım → Optimize | `:128` |
| `ListBranches` | Her Sync ile birlikte | `:130` |
| `SetPerfMode` | perf chip'i (koşarken) | `:132` |
| `PullRepository` | `N behind` chip'i | `:134` |
| `CheckoutBranch` | Branch popover'ı | `:136` |
| `GetProjectLog` | Proje seçimi | `:120` |

Motorda karşılığı olmayan komut yok: `DispatchAsync` her kontrat komutunu bir handler'a bağlıyor, bilinmeyen tip
`unknownCommand` hatası dönüyor (`Sup/SupervisorHost.cs:138-139`).

## 18. Test süiti

`dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --filter "Category!=Acceptance"`
(`35ddc41`, 2026-09-28):

| Başarılı | Başarısız | Atlanan | Toplam | Süre |
|---|---|---|---|---|
| 3610 | 0 | 6 | 3616 | 7 dk 30 sn |

Atlananlar ortam değişkeni kapılı ölçüm/sonda testleridir (`TrayOverlayMeasurementTests`,
`TrayIndicatorFrameProbeTests`, `TrayBalloonProbeTests`, `PerfProfileUiLatencyMeasurementTests` ×2) ve
`DragReorderTests.Reorder_uses_mouse_capture_and_never_calls_the_ole_drag_drop_api`.

**Sınır:** Süit "✅ Dolu" işlevlerin mantığını ve kablajını doğrular; gerçek bir repoya karşı uçtan uca derleme
(Acceptance, gerçek OSYS) bu koşuya dahil değildir ve uygulama elle açılıp tıklanmadı.
