# Pencereyi öne getiren tık işlem düğmelerini de çalıştırır — TDD dökümü

**Spec (kullanıcı kararı 2026-10-26, sohbet):** Pencere arkadayken bir işlem düğmesine yapılan ilk tık, Build'deki gibi
doğrudan çalışmalı; dönüşün sessiz Sync'i o düğmeyi tıkın altından kapatıp tıkı yutmamalı. **Branch chip'i hariç**
("tıklayınca direkt branch değişmiyor, seçmeyebilir"). Kısayolla ya da boş bir yere tıklayarak dönüş bugünkü gibi Sync'ler.

**Kök neden:** Dönüşte Sync'in atlanacağı kontrol listesi dar: `ActionBar.StartsOwnRefreshAt` yalnız açık Build'i ve
seçili olmayan Debug/Release'i tanıyor, yalnız alt çubuğa bakıyor (`MainWindow.TakeActivatingClick` →
`Mouse.GetPosition(Shell.ActionBarControl)`) ve yalnız sol basışı not ediyor (`MouseActivateWndProc`). Sync istendiği
ANDA koşu/bakım/pull kapılarını kapatır (`NotifySyncGatedCommands`), tık kapalı düğmeye iner ve kaybolur.

## Bulgular (önem sırası)

Önemli — ilk tık yutuluyor (A: liste dar, B: yalnız alt çubuk + yalnız sol basış):

| # | Kontrol | Bugün | Olması gereken | Neden güvenli (kendi tazelemesi) |
|---|---|---|---|---|
| 1 | Sync düğmesi | yutulur, yerine sessiz Sync | Sync düğmesinin kendi (Manual) Sync'i | kendisi tam Sync |
| 2 | Clean (bakım kutusu) | yutulur | Clean başlar | bitince Sync zincirler (`HandOverToSyncAsync`) |
| 3 | Optimize | yutulur | Optimize başlar | bitince Sync zincirler |
| 4 | Resolve cycles | yutulur | döngü koşusu başlar | her koşu diski baştan planlar (`Program.BuildRunPlan`, dört mod) |
| 5 | N behind (pull) | yutulur | pull başlar | başarılı pull Sync zincirler |
| 6 | Build'in oku (chevron) | menü açılmaz | menü açılır | maddelerin hepsi koşu |
| 7 | Satırda play | yutulur | tek proje koşusu başlar | koşu diski baştan planlar |
| 8 | Satırda ⋯ | menü açılır ama maddeler pasif | menü açık maddelerle açılır | maddeler tek proje koşusu |
| 9 | Satıra sağ tık | 8 ile aynı | 8 ile aynı | 8 ile aynı |
| 10 | Exit | hemen kapanmak yerine onay kartı (sessiz Sync iş sayılır) | iş yoksa hemen kapanır | uygulama kapanıyor, Sync anlamsız |
| 11 | × | Sync başlar | Sync başlamaz | pencere kapanıyor/tepsiye iniyor |

Kozmetik/küçük (C: kapı yalnız açılışta okunuyor):

| # | Kontrol | Bugün | Olması gereken |
|---|---|---|---|
| 12 | Açık ⋯ menüsü | kapı değişince maddeler tazelenmez (Sync bitse de pasif kalır; açıkken Sync başlarsa açık görünen madde basışı sessizce yutar) | menü açıkken maddeler koşu kapısını izler |

Bilinçli kalan: menüyü açıp seçmeden kapatmak ya da reddedilen pull o dönüşte ekranı tazelemez; sonraki dönüş tazeler
(bugün Build'e basıp düğmeden kaydırmakla aynı — ARCHITECTURE §10.3).

## Global Constraints

- **Muaf liste (dönüşün Sync'i istenmez, tık çalışır) — yalnız AÇIK kontrol sayılır:** Build'in birincil yarısı (mevcut),
  Debug|Release'in seçili OLMAYAN öğesi (mevcut), Sync düğmesi, bakım kutusunun Clean / Optimize / Resolve cycles düğmeleri,
  `N behind` chip'i, Build split-button'ın chevron'u (`SplitButton.MenuToggle`), satırda play (`BuildButton`), satırda ⋯
  (`MoreButton`), satıra SAĞ basış (satırın neresi olursa olsun), title bar'da `ExitButton` ve `CloseButton` (×).
- **Muaf OLMAYAN (dönüş bugünkü gibi Sync'ler):** branch chip'i (kullanıcı kararı — pin testi zorunlu), sayaç chip'leri,
  perf chip'i, seçili Debug/Release öğesi, satır gövdesine sol tık (seçim), satırın klasör/VS ikonları, Settings / What's new
  / About / layout düğmeleri, min/max, konsolda Back / Copy log, klavye/görev çubuğu/tepsi dönüşü, title bar zemini.
- Sağ basış YALNIZ satır için muaftır; başka her yerde sağ basış dönüşü bugünkü gibi Sync'ler.
- Karar bir görünüm sorusudur: VM'e yalnız `clickRefreshes` bool'u gider (`RunViewModel.OnWindowActivated`,
  `AutoSyncCoordinator.WindowActivatedAsync` DEĞİŞMEZ). Core'a dokunulmaz.
- Kontrolün hangi düğmeleri olduğunu SAHİBİ bilir: alt çubuk `ActionBar`'da, bakım kutusunun üç düğmesi `MaintenanceBox`'ta,
  satırınkiler `ProjectRow`'da; pencere (MainWindow) yalnız tek isabet testini yapar, sahibini bulur ve sorar (title bar'ın iki
  düğmesi pencerenindir).
- Kırmızı test kuralı: her bulgu (1-12) için AYRI test; fix'ten önce kırmızısı çalıştırılıp rapor dosyasına çıktısıyla yazılır.
- Davranış değişince eski kuralı pinleyen test sessizce silinmez/gevşetilmez: yeni kurala göre yeniden yazılır, doc'una
  `[DEĞİŞEN KURAL — kullanıcı kararı 2026-10-26]` + eski iddia + değişme gerekçesi yazılır.
- Kopya YASAK: test yardımcıları tek yerde (mevcutları kullan: `MainWindowHost`, `CommandPress`, `DispatcherPump`,
  `VmTopology`, `ActionBarTests.CenterIn/ConfigurationItem`, `DsResources.RealizedObjects`); yeni bir yardımcı tek yere yazılır.
- Dil: kod, UI metni, log İngilizce; kod yorumları ve doc yorumları Türkçe (dosyalardaki mevcut üslup). Yorumlar kısa ve
  gerekçeli (`[kullanıcı kararı 2026-10-26]` etiketi).
- Dosya düzenleme yalnız Edit/Write ile (`sed -i` CRLF'i bozar, PowerShell `Set-Content` UTF-8'i bozar, heredoc ters bölüyü bozar).
- Commit: task başına commit; mesaj Türkçe, `fix(app): ...` biçimi; mesaj dosyaya yazılıp `git commit -F <dosya>` ile atılır.
  Claude attribution satırı (Co-Authored-By, Generated with) YAZILMAZ. Push yok.
- Lokal test yalnız ilgili sınıflar, kısıtlı: önce `dotnet build tests\BuildOrchestrator.Tests\BuildOrchestrator.Tests.csproj`,
  sonra bir `.cmd` dosyasından
  `start "bo" /b /wait /belownormal /affinity F0 dotnet test tests\BuildOrchestrator.Tests\BuildOrchestrator.Tests.csproj --no-build --filter "<filtre>" -- xUnit.MaxParallelThreads=3 > <çıktı dosyası> 2>&1`.
  Tam süit lokalde koşulmaz (kapı CI'dır). Kısıt zamanlama testini düşürürse önce tek başına, kısıtsız tekrarla.

### Task 1: Pencereyi öne getiren tık kendi tazelemesini yapan her kontrolde dönüşün Sync'ini atlar (bulgu 1-11)

**Sıra (kırmızı derleme hatası değil, iddia hatası olmalı):** (a) davranışı DEĞİŞTİRMEYEN hazırlık — konum kökü `RootShell`,
yeni imzalar (`OnActivated(Point?, MouseButton)`, `StartsOwnRefresh(Visual)`, `ButtonOf` yalnız sol basışı tanır), mevcut
testler yeni koordinatla yeşil; (b) yeni testler yazılır, koşulur, kırmızı çıktısı rapora; (c) uygulama, yeşil. Exit testi için
mevcut çıkış testlerinin dikiş desenine bak (`MainWindowHost.NeutralizeExitSeams`, `ShutdownApplication` sayan sahteler —
`CloseToTrayTests`, `HiddenSurfaceTests`).

**Üretim:**
- `src/BuildOrchestrator.App/Shell/Win32.cs`: `WM_RBUTTONDOWN = 0x0204` (Türkçe doc, `WM_LBUTTONDOWN`'ın yanında).
- YENİ `src/BuildOrchestrator.App/Shell/ActivatingClick.cs` (`internal static class ActivatingClick`):
  - `MouseButton? ButtonOf(nint lParam)` — `WM_MOUSEACTIVATE` lParam'ı: düşük söz `HTCLIENT` ve yüksek söz `WM_LBUTTONDOWN` →
    `Left`, `WM_RBUTTONDOWN` → `Right`, başka her şey → `null`. Saf fonksiyon (birim testi var).
  - `Visual? HitAt(UIElement root, Point point)` — `root.InputHitTest(point)`; `Visual` ise o; `ContentElement` ise (ör.
    TextBlock içindeki `Run`) mantıksal ebeveynlerden ilk `Visual`; yoksa `null`. (`InputHitTest` kapalı öğeyi atlar, isabet açık
    ataya düşer — mevcut kural korunur.)
- `src/BuildOrchestrator.App/MainWindow.xaml.cs`: tık kaydı ve kararın pencere tarafı yeni partial'a taşınır:
  YENİ `src/BuildOrchestrator.App/MainWindow.ActivatingClick.cs` — `MouseActivateWndProc` (bool `_activatedByClick` yerine
  `MouseButton? _activatingButton = ActivatingClick.ButtonOf(lParam)`), `TakeActivatingClick()` (konum artık
  `Mouse.GetPosition(RootShell)` — title bar + gövde aynı kökte), `internal void OnActivated(Point? clickAt, MouseButton button = MouseButton.Left)`
  ve `internal bool StartsOwnRefreshAt(Point atRoot, MouseButton button)`: tek isabet testi (`ActivatingClick.HitAt(RootShell, …)`);
  isabetin görsel atalarında bir `ProjectRow` varsa karar satırındır (`row.StartsOwnRefresh(hit, button)`); yoksa yalnız sol
  basışta `Shell.ActionBarControl.StartsOwnRefresh(hit)` ya da `ExitButton`/`CloseButton.IsAncestorOf(hit)`. Kablaj satırları
  (`source.AddHook(MouseActivateWndProc)`, `Activated += …`) yerinde kalır.
- `src/BuildOrchestrator.App/Views/ActionBar.xaml.cs`: `StartsOwnRefreshAt(Point)` → `internal bool StartsOwnRefresh(Visual hit)`:
  birincil yarı, `PART_Split.MenuToggle`, `PART_Sync`, `PART_BehindChip`, `PART_Maintenance.StartsOwnRefresh(hit)`, seçili olmayan
  segment öğesi. Branch chip'i, sayaç chip'leri, perf chip'i SAYILMAZ (doc'ta gerekçesiyle).
- `src/BuildOrchestrator.App/Views/MaintenanceBox.xaml.cs`: `internal bool StartsOwnRefresh(Visual hit)` — üç düğmeden birinin
  atası (ayraçlar ve kutu çerçevesi sayılmaz).
- `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs`: `internal bool StartsOwnRefresh(Visual hit, MouseButton button)` —
  `Right` → `true` (satırın sağ tıkı her yerde menüyü açar, `OnRowRightClicked`); `Left` → eylem bloğu kuruluysa
  `BuildButton` ya da `MoreButton` isabetin atası.
- Doc yorumları (Türkçe): `OnActivated`, `StartsOwnRefresh`'ler — liste, sahiplik ve "branch chip'i neden yok" gerekçesi.

**Testler (her biri önce kırmızı):**
- `tests/BuildOrchestrator.Tests/App/WindowReturnClickTests.cs`: tık konumu artık köke göre — tek yardımcı
  (ör. `CenterInRoot(MainWindow, FrameworkElement)` = `element.TranslatePoint(orta, window.RootShell)`); mevcut testlerin
  iddiaları değişmez. Yeni testler (her bulguya bir test; `AfterAMinuteElsewhere` fikstürü):
  1. Sync düğmesi: dönüş Sync istemez; basış Manual Sync'i gönderir (görünür Sync: `vm.CurrentOperation == OperationLabel.Sync`).
  2. Clean: dönüş Sync istemez; basış `CleanWorkspaceCommand` gönderir.
  3. Optimize: dönüş Sync istemez; basış `OptimizeWorkspaceCommand` gönderir.
  4. Resolve cycles: döngülü topoloji (ör. `OptimizeCommandTests` deseni: `InCycle` düğümler + `Cycles`); dönüş Sync istemez;
     basış koşuyu başlatır (`vm.IsStarting`).
  5. N behind: `vm.Behind = 2` (chip görünür, açık); dönüş Sync istemez; basış
     `BuildOrchestrator.Contracts.Ipc.PullRepositoryCommand` gönderir.
  6. Build chevron: dönüş Sync istemez; chevron açık kalır ve menüyü açar (`bar.Split.IsMenuOpen`).
  7. Satır play: satır realize + hover (`row.SimulateHover(true)`); dönüş Sync istemez; basış tek proje koşusunu başlatır.
  8. Satır ⋯: dönüş Sync istemez; ⋯ işaretlenince menü maddeleri açık.
  9. Satıra sağ basış (`MouseButton.Right`, satırın ortası): dönüş Sync istemez.
  10. Exit: dönüş Sync istemez; basış kart açmadan çıkışı ister (çıkış dikişi sayan sahte; `DsChrome.GetIsActive(ExitButton)` false).
  11. ×: dönüş Sync istemez.
  Dar kural pinleri (mevcut `Any_other_return_still_syncs_and_closes_build_at_once` teorisine yeni durumlar): branch chip'ine
  tık (kullanıcı kararı), satır gövdesine sol tık, Build'e SAĞ basış → dönüş Sync'ler ve Build o anda kapanır.
- `tests/BuildOrchestrator.Tests/App/ActionBarTests.cs`: `Only_an_open_build_and_the_configuration_not_selected_start_their_own_refresh`
  YENİ kurala yeniden yazılır (`[DEĞİŞEN KURAL]` doc: eski iddia "Sync düğmesi sayılmaz"; gerekçe: kullanıcı kararı 2026-10-26,
  ilk tık yutuluyordu): açık set true; branch chip, Σ chip, perf chip, seçili öğe false; Sync sürerken (hepsi kapalı) hepsi false.
  İsabet `ActivatingClick.HitAt(bar, CenterIn(bar, x))` ile alınır.
- `ActivatingClick.ButtonOf` birim testi (yeni test sınıfı ya da uygun mevcut sınıf): HTCLIENT+LBUTTONDOWN → Left,
  HTCLIENT+RBUTTONDOWN → Right, HTCAPTION(2)+LBUTTONDOWN → null, HTCLIENT+MBUTTONDOWN(0x0207) → null.

**Doğrulama:** filtre `FullyQualifiedName~WindowReturnClickTests|FullyQualifiedName~ActionBarTests|FullyQualifiedName~ActivatingClick|FullyQualifiedName~AutoSyncCoordinatorTests|FullyQualifiedName~MaintenanceBoxTests|FullyQualifiedName~ProjectRowInputTests|FullyQualifiedName~HiddenSurfaceTests`.

### Task 2: Açık satır menüsü koşu kapısını izler (bulgu 12)

**Üretim:** `src/BuildOrchestrator.App/Views/ProjectRow.xaml.cs` — menü açıkken (`MoreButton.IsChecked`) koşu kapısı değişince
(`BuildProjectCommand.CanExecuteChanged`) maddeler yeniden `SetRunActionsEnabled(CanRunProject())` alır. Abonelik ZAYIF olur
(`CanExecuteChangedEventManager.AddHandler`): satır konteynerleri geri dönüştürülür, VM satırı canlı tutmamalı
(`ProjectRow.xaml.cs` ~146-150'deki StopStage zayıf aboneliği emsaldir). Açılıştaki okuma kalır.

**Testler (`tests/BuildOrchestrator.Tests/App/ProjectRowInputTests.cs`, gerçek satır zinciri — `NewRunVm`, `Realize`, sağ tık ya da
`MoreButton.IsChecked = true`):**
1. Kapı açıkken açılan menü, açıkken kapı kapanınca (ör. `runVm.IsStarting = true` ya da `SyncStartedEvent`) maddeleri pasifleştirir.
2. Kapı kapalıyken açılan menü, açıkken kapı açılınca maddeleri açar (kullanıcının gördüğü: Sync bitti, maddeler pasif kaldı).

**Doğrulama:** filtre `FullyQualifiedName~ProjectRowInputTests|FullyQualifiedName~PopoverToggleTests|FullyQualifiedName~RowMenuPlacementTests`;
LocalOnly `PopoverTests` de lokalde koşulur (CI onu koşmaz).

## Kontrolcü adımları (alt ajana verilmez)

- Dokümanlar: ARCHITECTURE §10.3 (dönüş paragrafı: muaf liste, sağ basış, satır ve title bar, branch chip'inin neden yok
  olduğu), §13.2 segment cümlesi ("the one case") kontrolü, satır menüsü kapısı cümlesi, §22 kod haritası satırı; USER-GUIDE'da
  dönüş anlatılıyorsa. `DocumentationTests` lokalde.
- Son bütün-branch incelemesi, rebase, push, CI yeşili, develop'a merge, develop CI yeşili, branch silme.
</content>
</invoke>
