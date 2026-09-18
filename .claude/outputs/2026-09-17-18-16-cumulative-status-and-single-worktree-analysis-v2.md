# Sync/Build durum modeli ve tek çalışma ağacı — analiz ve öneri (v2)

> Kaynak: Claude web (claude.ai) oturumu, koda erişimsiz yazıldı; kullanıcı bu oturuma taşıdı. Kodla doğrulaması
> `2026-09-17-19-41-cumulative-status-analysis-verification.md` dosyasındadır. Bu dosya tarihseldir, düzeltilmez.

Tarih: 2026-09-17 · Durum: yalnız analiz, kodda değişiklik YOK · `main` = `ea3ab62`
Önceki sürüm: `2026-09-17-18-03-…-analysis.md` (tarihsel; bu dosya onun yerine geçer). v2'de yeni olan:
kırmızının anlamı (§4.2) ve satır ibareleri (§4.3).

## 1. Kısa cevap

- **İstediğin şey bir tasarım ilkesi değişikliğidir, motor değişikliği değil.** Bugün renk "son işlemde ne
  oldu"yu anlatır (ARCHITECTURE §14.3) ve Sync bilerek hiçbir şeyi boyamaz. Sen rengin "bu projenin çıktısı
  şu an ne durumda"yı anlatmasını istiyorsun — kümülatif, Sync'ten Sync'e taşınan bir durum. Motorun buna
  gereken verisi **zaten var**: her Sync'in önizlemesi proje başına `UpToDate / NeverBuilt / LastFailed /
  SignatureChanged / WaitingForDependency` gerekçesini ve son başarılı derleme zamanını taşıyor; App bunu
  yalnız sağdaki metin etikete yazıyor, renge yazmıyor.
- **Kırmızı bir tarih notu olmamalı, kanıt olmalı.** Haklısın: "en son kırmızıydı" tek başına anlamsız —
  Sync o an neyin derli olup olmadığını hesaplar, geçmiş bir denemenin sonucu değildir. Öneri: Sync sonrası
  kırmızı **yalnız aynı kaynak imzasında patlamış** proje için (defter hata anındaki imzayı da tutar; ne
  kendi dosyası ne bir bağımlılığı değişmişse "bu kaynak derlenmiyor" iddiası hâlâ geçerlidir). Bir şey
  değiştiyse proje sıradan gridir (`modified`/`affected`) — ne olacağı bilinmiyor, öyle görünür.
- **Satır ibareleri beş sözcükte kalır, iki kuyruk düşer** (§4.3): `up to date · 2h` · `modified` ·
  `affected` · `never built` · `failed`. `failed · retry` ve üç parçalı `affected · up to date · 2h`
  kalkar; verdikleri söz tooltip'e iner. Renk ve dalga o sözü zaten görünür kılar.
- **Kümülatif renk modeli App'te + küçük bir defter dokunuşuyla yapılır (Faz 1).** Koreografiler (nötr an
  → rastgele dalga → veda → sıralı teslim → neon final) birebir kalır; yalnız altlarındaki zemin nötr gri
  yerine projenin kendi rengi olur.
- **Tek çalışma ağacı (`<repo>-Build`) + Sync'in o ağaçta koşması motor değişikliğidir (Faz 2).** Build'in
  planlama hattı (worktree hazırla → tara → kimlikleri ana köke taşı → imza) zaten var; Sync bugün bunun
  yerine **aktif çalışma ağacını** tarıyor. İkisini tek hatta indirmek senin akışını verir ve dokümanın
  "bilinen seam"ini (§10.2) kapatır.
- **Dışarıdan (VS'den) derlemeyi araç asla göremez** — "DLL/bin timestamp okunmaz" değişmezi bunun tam
  karşısındadır. Yeni modelde ihtiyaç kendiliğinden düşer (§4.4).
- **Tek gerçek bedel:** araç commit'lenmemiş değişikliği bir daha hiç derlemez (§5).

## 2. Bugün nasıl çalışıyor (kod gerçeği)

### 2.1 Sync

`Core/Workspace/SyncWorkspaceService.cs`: `git fetch origin <branch>` (ref-only) → **ana kökü** tara →
graf → will-build geçişi → `workspaceTopology` + `buildPreview` + `syncCompleted`. Worktree'ye dokunmaz;
seçili branch aktif branch'ten farklı olsa bile önizleme aktif ağacı anlatır ("bilinen seam", `:26-32`).

Önizlemenin her satırı (`Contracts/Ipc/IpcMessages.cs:467`): `WillBuild` (üç durumlu), `Reason`,
`OwnFilesChanged`, `LastBuiltAt`, `Conditional`, `DependencyRoots`. Karar
`Core/Planning/WillBuildEvaluator.cs:72-91`'de tek yerde: kayıt yok → son koşu hatalı → imza değişti →
bağımlılık notu → güncel. Defter (`build-state.json`, §7.5) başarıda imzayı yazar; hatada yalnız
`LastResult=Failed` yazar, hata anındaki imzayı **tutmaz** (`RunCoordinator.cs:1881-1891`); daha önce hiç
başarıyla derlenmemiş proje patlarsa kayıt hiç açılmaz (`:1886`).

### 2.2 Renk modeli

- Tek kanal: şerit, nokta, glyph, node çerçevesi ve küp aynı `VisualStatus`'tan boyanır
  (`App/Controls/VisualStatus.cs:53-67`): `Fresh` (başlangıç modu), `Discovered` (nötr gri), `Marked`,
  `Queued`, `Building`, `Succeeded`, `Failed`, `Skipped`, `Cycle`, `CycleSkipped`.
- **Sync ve açılış hiçbir şeyi boyamaz** (§14.3 "start mode"): dört yaylı halka + kesikli glyph + kesikli
  node (`RunViewModel.Workspace.cs:601`). Neyin bayat olduğu yalnız karar etiketinden okunur.
- İşlem başlayınca (`RunViewModel.cs:958` `NeutralizeRows`) **herkes nötr griye iner**; plan ve döngü
  üyeliği korunur. Koşu bitince sonuçlar kalır, **bir sonraki Sync ya da işlem hepsini siler.** "50 yeşil +
  2 kırmızı" toplamı bu yüzden hiç görünmez: satırdan tek proje derlediğinde diğer 51'i griye iner.
- Döngü üyesi: işlem onu derlemiyorsa çerçeve gri, **küp amber**; listede yalnız uyarı üçgeni. Resolve
  derleyince küp sonuç rengini alır; Sync'te başlangıç moduna düşer, sonraki işlemin nötr anında yeniden
  amber olur.

Doküman ile kod bu başlıklarda uyumlu; çelişki bulmadım (§7, §8.1, §8.6-8.7, §10.2-10.4, §13.2, §14.3,
§14.5 · README "Sync colours nothing", "Branch / worktree").

### 2.3 Koşu ve koreografi (§14.5)

Tıklama → konsol/akış temizlenir → satırlar nötr gri → 440 ms nötr an → kapsam rastgele sırayla amber
(110 ms/node, zincir ≤ 1,1 s) → kapsam dışı node'lar 0,18'e söner, kapsam sırayla 0,13'e teslim olur →
komut motora gider → `runStarted`/`buildPreview` → sonuçlar → neon final (yalnız grafta: derlenenler
rastgele tutuşur, sonra "kalan griler" birlikte gelir). Kapsam: Build = `WillBuild==true && !Conditional`,
Rebuild = döngü dışı herkes, Resolve = döngü üyeleri, satır = tek hedef (`RunViewModel.cs:988`).

### 2.4 Satır ibareleri (bugün)

`App/ViewModels/DecisionLabel.cs:79-138` — beş sözcük + bir üçlü:

| İbare | Ne zaman | Tooltip |
|---|---|---|
| `modified` | kendi dosyası değişti | Its own files changed since the last build |
| `affected` | yalnız bağımlılığı değişti (ya da ayrım bilinmiyor) | Its own files are unchanged — a dependency changed |
| `never built` | kayıt yok / Clean sonrası | No build output on disk |
| `failed · retry` | son koşu hatalı; `retry` yalnız düz Build gerçekten deneyecekse (döngü üyesinde yok) | The last build of this project failed (— Resolve cycles will retry it) |
| `up to date · 2h` | güncel; kuyruk = son başarının yaşı | Up to date — last built 2h ago |
| `affected · up to date · 2h` | hatalı bağımlılığa karşı derlenmiş, imzası değişmemiş, koşu onu bekletiyor | Dependency issue: X — rebuilds once that dependency is healthy again |

Bu üçlü için yuva 134 px'ten 204 px'e genişletilmişti (§13.2). Koşu içinde etiket canlı değişir: başarı →
`up to date · just now`, hata → `failed · retry`. Rengi olmayan tek satırda bilgi budur.

### 2.5 Branch ve worktree

- Üç durum matrisi (`Core/Git/WorktreeManager.cs:140`, `Supervisor/Program.cs:244-341`): aktif branch +
  switch kapalı → **in-place** (local değişiklikler dahil); aktif + switch açık → committed HEAD worktree'de;
  başka branch → worktree **zorunlu**.
- Branch seçimi niyettir; git'e Build anında dokunulur. Farklı branch seçilince satırlar hollow, faz Boot,
  konsola `Branch changed: X — Sync required` — **otomatik Sync yok** (`RunViewModel.ActionBar.cs:124-142`).
- Açılışta otomatik Sync yok (`MainWindow.xaml.cs:156-160`). Kayıtlı branch yalnız görüntü değeri: ilk
  envanterde açık seçim yoksa **checkout edilmiş branch'e döner** (`RunViewModel.Workspace.cs:731-743`).
- Havuz: `%LOCALAPPDATA%\BuildOrchestrator\worktrees\<slug>-N`, branch başına kopya, 20 GiB LRU, daima
  detached, yeniden kullanımda `reset --hard`. Worktree chip'i/popover'ı (switch, hedef listesi, silme)
  bu havuzu yönetir.
- Worktree'nin commit'i: aktif branch → yerel HEAD; başka branch → `refs/heads/X`, yoksa `origin/X`
  (`Program.cs:360-374`).

### 2.6 Bu öneriyi mümkün kılan iki olgu

1. **Defter globaldir, anahtar mantıksal kimliktir** (§7.5, `Core/Planning/ProjectIdentityRebase.cs`):
   worktree'de derlenen proje ana kökteki csproj yoluyla yazılır; imza yol-göreli ve içerik tabanlıdır,
   ana kökte de havuz kopyasında da aynıdır (§7.1). Branch değişince değişmemiş projeler kendiliğinden
   güncel kalır — "branch değiştirdim, uygun olanlar yine yeşil" beklentin motorun bugünkü matematiğidir.
2. **OSYS'nin çıktısı ortak havuza düşer:** 1927 HintPath ve 178 post-build copy absolute
   (`copy /y "$(TargetDir)$(TargetName).*" "c:\OSYS\...\bin\"`; eng review D12). Worktree'de derlenen
   DLL, VS'nin ana ağaçtan derlediğiyle **aynı klasöre** iner (§9.4, §20). Developer'ın VS'deki projesi,
   aracın worktree'de derlediği bağımlılıkları görür.

## 3. Senaryonun bugünkü modelle çeliştiği yerler

| # | İstediğin | Bugün |
|---|---|---|
| 1 | Sync'ten sonra yeşil/gri/kırmızı | Sync boyamaz; bilgi yalnız etikette |
| 2 | Toplamı görmek, sıfırlamamak | Her işlem nötrler; renk yalnız o işlemin kapsamı |
| 3 | Sync'in gördüğü = derlenecek kaynak | Sync aktif (kirli) ağacı, Build seçili branch'in worktree'sini görebilir |
| 4 | Tek ağaç, mod seçimi yok | Üç mod, worktree chip'i, havuz, LRU, hedef listesi |
| 5 | Branch değişince / açılışta otomatik Sync, son branch hatırlansın | Otomatik Sync yok; açılışta checkout'a dönülür |
| 6 | Dış derleme Sync'te yeşile dönsün | İmkânsız (timestamp okunmaz) — ama ihtiyaç düşüyor, §4.4 |

## 4. Öneri

### 4.1 İki ilke

1. **Renk = projenin çıktısının durumu.** Güncel (yeşil), derlenecek (gri), bu kaynak derlenmiyor
   (kırmızı), bilinmiyor (halka). Koşu içinde kuyruk/derleniyor (amber) bunun üstüne geçici biner; sonuç
   duruma yazılır ve **kalır**. Sync durumu yeniden hesaplar, silmez.
2. **Tek çalışma ağacı.** Araç her zaman `<repo>-Build`'de (`D:\Projects\Delta\OSYS` →
   `D:\Projects\Delta\OSYS-Build`) seçili branch'in **committed** hâlini derler. In-place mod, switch,
   havuz, LRU, hedef listesi kalkar. Sync de bu ağacı tarar.

### 4.2 Durum modeli

| Durum | Kaynak | Renk (şerit · nokta · çerçeve) | Glyph | İbare |
|---|---|---|---|---|
| **Unknown** | `WillBuild == null` | bugünkü başlangıç modu (halka, kesikli çerçeve) | kesikli daire | boş |
| **Current** | `UpToDate`; koşuda başarı | **yeşil** | ✓ | `up to date · 2h` |
| **Current + ▲** | `WaitingForDependency` (hatalı bağımlılığa karşı derlenmiş, imza değişmemiş) | **yeşil** + amber üçgen | ✓ | `up to date · 2h` |
| **Stale** | `NeverBuilt`, `SignatureChanged`, `DepIssue` | **gri** (bugünkü `Discovered` görünümü) | kesikli daire | `never built` / `modified` / `affected` |
| **Failed** | `LastFailed` = **hata anındaki imza == bugünkü imza**; koşuda hata | **kırmızı** | ✗ | `failed` |
| Queued / Building | yalnız koşu içinde (`InRunQueue`, `IsCompiling`) | amber | saat / dönen halka | değişmez |

**Kırmızının kuralı (senin itirazının cevabı).** Kırmızı, "en son kırmızıydı" demez; "**bu** kaynak
derlenmedi" der. Defter hata anındaki imzayı da tutar (`FailedSignature`; bugünkü `NonConvergentSignature`
ile aynı ilke, §8.8): Sync'te bugünkü imza ona eşitse — kendi dosyası da, hiçbir bağımlılığı da
değişmemişse — iddia hâlâ kanıtlıdır, satır kırmızıdır ve Build'e basmadan "bunlar hâlâ bozuk" görünür.
Herhangi bir şey değiştiyse kanıt düşer, satır gridir (`modified`/`affected`). Kural imza karşılaştırmasıdır,
timestamp değil — §7.1 ilkesi korunur. İki ayrıntı:

- `FailedSignature` yalnız projenin **kendi MSBuild çağrısı** patladığında yazılır (exit ≠ 0, timeout,
  invoke hatası). Politika gereği geçersizleştirilen kayıt (yakınsamayan döngü grubunun yeşil üyesi,
  §8.8) yazmaz — o proje gridir, üçgen "did not converge" der. Stop in-flight projeyi bitirtir ve sonucu
  normal yazılır; App hard stop göndermez, "kesildi" diye ayrı bir kayıt oluşmaz (§4.5).
- Hiç başarıyla derlenmemiş proje patlayınca da kayıt açılır (`BuiltSignature: null, FailedSignature: S`);
  `WillBuildEvaluator` sırası `LastFailed(imza eşit)` → `NeverBuilt` → … olur. Böylece ilk denemesi patlayan
  proje de Sync sonrası kırmızıdır — bugün `never built` görünüyor.

Diğer kurallar:

- **Döngü üyesi:** küp **her zaman amber**, çerçeve/şerit tabloya göre (bileşik imza ortak; grup ya bütün
  yeşil ya bütün gri, §7.3). Listede üçgen aynen.
- **Skipped bir renk değildir.** `up to date` ile atlanan yeşildir; `dependency still failing`,
  `not needed by a dependency cycle`, `in dependency cycle` ile atlanan olduğu renkte kalır. "—" glyph'i
  yalnız koşu içinde, geçici.
- **Nötrleme kalkar, sıfırlama daralır:** işlem başlarken yalnız geçici alanlar temizlenir (süre,
  `CycleWaiting`, `SkipReason`). Renk, üçgen, etiket durur. `Fresh` yalnız Unknown.
- **Canlı geçiş zaten var:** `OnProjectDone` (`RunViewModel.cs:1842-1908`) başarıda `UpToDate` /
  `WaitingForDependency`, hatada `LastFailed` yazıyor; `VisualStatus` bu alanlardan türer.

### 4.3 Satır ibareleri — ne olmalı

İlke: **renk durumu söyler, ibare rengin söyleyemediğini söyler** — gri için *neden*, yeşil için *ne
zaman*, kırmızı için metin kanalı (renk körü kuralı, §14.3: durum = renk + glyph + metin). Söz veren
kuyruklar (`retry`, `up to date` üçlüsü) kalkar: bir sonraki Build'in neye dokunacağını dalga ve amber
zaten gösteriyor; söz, isteyen için tooltip'te.

| Renk | İbare | Ne zaman | Tooltip |
|---|---|---|---|
| yeşil | `up to date · 2h` | imza = son başarı | Up to date — last built 2h ago |
| yeşil + ▲ | `up to date · 2h` | hatalı bağımlılığa karşı derlenmiş, imza değişmemiş | ▲: Built against a failing dependency: X — rebuilds once it is healthy again |
| gri | `modified` | kendi dosyası değişti | Its own files changed since the last build |
| gri | `affected` | yalnız bağımlılığı değişti (ayrım bilinmiyorsa da bu) | Its own files are unchanged — a dependency changed |
| gri | `never built` | kayıt yok / Clean | No build output known to this tool |
| kırmızı | `failed` | aynı imzada patladı | Failed at this source — Build will retry it · döngü üyesinde: Failed at this source — Resolve cycles will retry it |
| — | (boş) | bilinmiyor | — |

Kaldırılanlar ve gerekçeleri:

- `failed · retry` → `failed`. `retry` bir sözdü ve döngü üyesinde tutulmadığı için zaten koşullu
  yazılıyordu; kırmızı + dalga sözü görünür kılar, tooltip adını söyler. Bir istisna kalmaz.
- `affected · up to date · 2h` → `up to date · 2h` + ▲. Üçlünün "affected" parçası üçgenin söylediğini
  tekrar ediyordu ve yuvayı 204 px'e genişletmişti; yuva tasarımın 134 px'ine döner.
- Koşu içinde ibare değişmez (disk olgusu); sonuç gelince `up to date · just now` / `failed` (bugünkü kural).
- `never built` tooltip'i "on disk" demiyor artık — araç diske bakmaz, defterine bakar; metin bunu söyler.

Eklenmeyenler: `queued`/`building` ibaresi (glyph zaten söyler), `interrupted` (Stop in-flight projeyi
bitirtir, kesik kayıt oluşmaz), `forced` (tasarımın zaten reddettiği kapsam sözcüğü).

### 4.4 Akışlar — bugün → öneri

**Açılış.** Bugün: repo yüklenir, ekran halka/kesikli, kullanıcı Sync'e basar. Öneri: kayıtlı branch'le
**otomatik Sync**; ilk kare Unknown, Sync bitince renkler.

**Sync.** Bugün: fetch → ana kök taranır → halka. Öneri: fetch → hedef commit (§4.5) → `<repo>-Build` o
commit'e getirilir → **o ağaç** taranır → kimlikler ana köke taşınır → imza → yeşil/gri/kırmızı, döngüde
küp amber. Konsol: `build tree: developer @ a3f81c2 · D:\…\OSYS-Build · 3 commits behind
origin/developer`. Gördüğün = derlenecek kaynak.

**Build.** Aynı koreografi; zemin nötr gri değil, satırın rengi. Dalga gri/kırmızı kapsamı amber'a yakar
(yeşiller **yeşil kalır**); veda ve sıralı teslim aynen; koşu boyunca kapsam dışı node'lar 0,13/0,18'de
kendi renginde soluk; sonuçlar yeşil/kırmızı; final: derlenenler tutuşur, sonra **kalan herkes kendi
renginde** parlar. Bitişte toplam: 50 yeşil + 2 gri → 1 yeşil 1 kırmızı → 51 yeşil 1 kırmızı.

**Tekrar Build.** Kapsam artık yalnız kırmızı (ve arada değişenler). Sıfırlama yok.

**Satırdan Build / Rebuild / Clean.** Yalnız hedef amber'a yanar (bugünkü kural), diğer satırlar renk
değiştirmez. Bayat bağımlılığa karşı derlendiyse yeşil + ▲ (§8.1/§8.3 aynen). Satırdan Clean → `never
built` gri.

**Resolve cycles.** Üyeler amber → sonuç; küp amber kalır, çerçeve yeşil/kırmızı. Kapsam dışı hiçbir şey
renk değiştirmez.

**Branch değişimi.** Bugün: hollow + Boot + "Sync required". Öneri: satırlar Unknown'a düşer (liste
boşaltılmaz), **Sync otomatik**; `OSYS-Build` yeni commit'e `reset --hard`; içerik aynı olanlar yeşil
döner, değişenler gri, aynı imzada patlamışlar kırmızı. Geri dönüşte defter yalnız son başarılı imzayı
tuttuğu için o branch'te farklı olanlar yine derlenir — ortak çıktı havuzunda doğru davranış (§20).

**Dışarıdan derleme (VS).** Araç göremez; değişmez korunur. Yeni modelde VS'den derlenen şey developer'ın
**kendi, commit'lenmemiş** işidir ve aracın ağacında yoktur: aracın yeşili "bu branch'in committed kaynağı
derlendi" der, VS'nin DLL'i havuza düşer, ikisi çelişmez. Commit edince Sync o projeyi gri gösterir ve
araç bir kez daha (committed hâliyle) derler — fazladan ama doğru bir derleme.

**Clean / Optimize (bakım kutusu).** Bugünkü akış aynen (plan yüzeyi tıklamada boşalır, Sync zincirlenir);
zincirlenen Sync renkleri koyar (Clean sonrası hepsi gri).

### 4.5 Tek çalışma ağacı — kurallar

| Konu | Kural |
|---|---|
| Konum | `<repoRoot>-Build`, repo klasörünün yanında; ayar yok, türetilir. Ayarlar → Workspace'te salt-okur. |
| Hangi commit | Aktif branch seçiliyse **yerel HEAD**; başka branch'te `refs/heads/X`, yoksa `origin/X` (bugünkü kural). Commit'lenmemiş değişiklik hiçbir zaman girmez. |
| Ne zaman güncellenir | Her Sync'te ve her koşunun planlamasında: `git worktree list` → varsa detached kapısıyla `reset --hard <sha>`; yoksa `worktree add --detach`. Ana repoya dokunan komutlar `worktree add/remove/prune` kalır (K1). |
| `obj` | `<repoRoot>-Build\_obj\<hash(projectId)>` — kimlik ana kök yolu, branch'ler arasında sabit ve sıcak. `source-hash-cache` tek yol kümesi taşır. |
| Ana ağaç | Hiçbir zaman derlenmez ve değiştirilmez. `N behind` chip'i ve ff-only pull aynen (`FastForwardUpdater.cs` tek mutasyon dosyası). |
| Harici kökler | Değişmez: kendi kopyaları, `obj` izolasyonu yok, güncelleme switch'i aynen. |
| Kalkanlar | In-place mod, worktree switch/chip/popover, `UseWorktree`/`WorktreeName` (komut + `ui-state.json`), `listWorktrees`/`deleteWorktree`, LRU/cap, `StaleObjRunStartWarner` (yalnız in-place içindi). |
| Kenar durumlar | (a) `<repo>-Build` var ama bu reponun worktree'si değil → Sync durur, konsol neden + çözüm. (b) kayıtlı ama klasör silinmiş → `worktree prune` + yeniden `add`. (c) attached HEAD → bugünkü kapı, reset yok, hata. (d) eski havuz → ilk Sync'te tek seferlik `worktree remove --force`, her biri konsola yazılır. |
| Görünürlük riski | Klasör repo'nun yanında; yanlışlıkla VS'de açılıp düzenlenebilir, her `reset --hard` düzenlemeyi siler (bugün de belgeli risk). Köke `BUILD-TREE-DO-NOT-EDIT.txt` + her Sync'te tek satır uyarı. |

Neden havuz değil tek ağaç: tek seferde tek koşu var (§8.8); defter ve çıktı havuzu branch'ten bağımsız;
branch başına kopyanın tek kazancı sıcak `obj`'ydi — değişen proje zaten baştan derlenir, kazanç küçük.

### 4.6 Branch seçimi — "hatırla" ile "izle"nin uzlaşması

Bugün açık seçim oturum içinde tutulur, açılışta checkout'a dönülür (C1 dersi: terminalde `git checkout
feature/y` yapan kullanıcı aracın `main` derlemeye devam ettiğini fark etmemişti). Sade öneri:

- Popover'ın ilk satırı **`● Checked-out branch`** (izle): chip aktif branch'i gösterir; terminalde branch
  değişirse sonraki Sync'te araç da geçer. **Varsayılan.**
- Listeden seçim **sabitler** (pin): kalıcı, açılışta o gelir, checkout değişse de o derlenir; chip'te
  "pinned" işareti, tooltip'te checkout edilmiş branch. Sabitleme yalnız ilk satırla kalkar.
- Her iki modda branch değişimi otomatik Sync'i tetikler.

İsteğe bağlı (Faz 3): `.git/HEAD` izleyicisiyle checkout değişimini anında yakalayıp Sync başlatmak.

### 4.7 UI — ne değişir, ne aynı kalır

| Yüzey | Aynı | Değişen |
|---|---|---|
| Liste | 36 px satır, şerit + nokta + glyph + etiket + üçgen + süre; hover ikonları; sticky katmanlar; follow-mode | Renk kalıcı ve durum anlamlı; başlangıç modu yalnız Unknown; etiket yuvası 134 px |
| Graf | Yerleşim, bead orbit, seçim/filtre opaklığı, tüm koreografiler | Çerçeve durum rengi; küp döngüde daima amber; final "kalan herkes kendi renginde" |
| Ribbon | Pill, faz satırı, chip'ler, progress | Sync satırı build tree + commit'i söyler |
| Action bar | Sync, bakım kutusu, `N behind`, cfg, perf, Build split | **Worktree chip'i kalkar**; branch chip'e izle/sabitle; sayaçlar §4.8 |
| Ayarlar | General, External, Layers | Workspace: repo kökü + türetilen build tree yolu (salt-okur) |
| Konsol / akış | Temizleme kuralları, typewriter, tonlar | Sync transkriptine worktree satırları (Build planlamasındakilerin aynısı — `PlanProgressLines`, tek kaynak) |
| Sync sonrası reveal | — | Kararını istediğim nokta (§7-3): topoloji yapısal olarak aynıysa satırları yerinde güncelleyip yalnız rengi geçirmek (200 ms), yapı değiştiyse bugünkü reveal |

### 4.8 Sayaçlar

Bugün chip'ler koşu odaklıdır (`RunCounters.cs:35-58`). Kümülatif modelde: Σ toplam · building (canlı) ·
✓ Current · ○ Stale ("derlenecek") · ✗ Failed · ⚠ döngü ∪ bağımlılık (aynen). "—" (skipped) chip'i
kalkar; koşunun kendi tablosu (`Completed — 3 failed · 24 succeeded · 9 skipped`) ribbon'da ve akışın
kapanış satırında aynen. Sync satırındaki "7 changed, 14 to build" aynen.

## 5. Bedeller ve riskler

1. **Commit'lenmemiş değişiklik araçta yok.** Kırmızı projeyi VS'de düzeltip satırdan derletmek için önce
   commit gerekir; aksi hâlde araç eski kaynağı derler, yine kırmızı. Alternatif akış: VS'de derle
   (havuz düzelir), commit et, Sync, Build. In-place yolu motorda gizli bırakılabilir; UI'a koymamayı
   öneririm.
2. **Ortak çıktı havuzu branch'ler arasında paylaşılır** (bugün de). `next` derleyip `developer`'a dönünce
   farklı projeler yeniden derlenir. "Hepsi yeşil"i her branch'te ayrı görmek beklenmesin.
3. **Yeşil = aracın son başarılı derlemesi bu kaynakla.** VS'den derleme bu iddiaya girmez.
4. **Kırmızı yalnız kanıtlıysa görünür** — bir dosya değişince gri olur; "dün patlamıştı" bilgisi kaybolur.
   Bu bilinçli: iddia edilmeyen şey gösterilmez. Proje logu "last failed at <sha>" satırıyla tarihi tutar.
5. **Sync biraz pahalanır:** `worktree list` + `reset --hard` (aynı commit'te saniyenin altı, branch
   değişiminde birkaç saniye; ilk oluşturma büyük repoda onlarca saniye, tek seferlik).
6. **Renk kanalının anlamı ve etiket sözlüğü değişiyor;** ARCHITECTURE §13.2/§13.6/§14.3, README "Sync
   colours nothing"/"Reading the list", `VisualStatus`/`DecisionLabel`/koreografi testleri yeniden yazılır
   (CLAUDE.md "davranış değişince testi de değişir").
7. **İzle/sabitle** iki durum getirir; §4.6'daki hâlden fazlası eklenmemeli.

## 6. Uygulama planı

**Faz 1 — Kümülatif renk + kanıtlı kırmızı + ibareler (App + küçük Core/Supervisor dokunuşu):**
- Core: `BuildState.FailedSignature`; `WillBuildEvaluator` sırası `LastFailed(imza eşit)` → `NeverBuilt`
  → `SignatureChanged` → not → `UpToDate`; hiç kaydı olmayan projede hata kaydı açılır
  (`InvalidateBuildStateOnFailure` yalnız gerçek MSBuild hatasında imzayı yazar).
- App: `VisualStatus` → `Unknown / Current / Stale / Failed` + koşu durumları; `VisualStatuses.For` sırası:
  canlı motor durumu → `Marked` → gerekçeden durum; küp için ayrı `inCycle`. `NeutralizeRows` yalnız
  geçici alanlar; `Fresh` yalnız `WillBuild==null`. `DecisionLabel` beş sözcük, kuyruklar tooltip'e; yuva
  134 px. `EndFinale` "kalan griler" → herkes. `RunCounters` + chip'ler. Testler: `VisualStatusTests`,
  `DecisionLabelTests`, `WillBuildEvaluatorTests`, koreografi, `RunCountersTests`. Doküman: §7.4-7.5,
  §13.2, §13.6, §14.3; README "Reading the list".

**Faz 2 — Tek ağaç + Sync worktree'de (Core + Supervisor + App):**
`Program.BuildRunPlan` hattı Core'a (`Core/Planning/RunPlanner`; "planlama Core'da" değişmezi);
`SyncWorkspaceService` aynı hattı çağırır. `WorktreeManager` → tek ağaç (`EnsureBuildTreeAsync(sha)`), K1
kapıları aynen, havuz/LRU/silme kalkar. Komutlar: `UseWorktree`/`WorktreeName` kalkar, `Branch` zorunlu;
`syncCompleted`'a `BuildTreePath`, `BuiltSha`, `CheckedOutBranch`. App: worktree chip/popover,
`EffectiveUseWorktree`, `IsWorktreeForced`, `RunBranchIntent` kalkar; izle/sabitle + kalıcılık; otomatik
Sync (açılış, branch değişimi). Kaynak guard'ları yeniden pinlenir. Doküman: §8.6-8.7, §10.2-10.4, §12-13
action bar, §16, §20; README "Branch / worktree".

**Faz 3 — İsteğe bağlı:** yapısal olarak aynı topolojide reveal yerine yerinde renk geçişi; `.git/HEAD`
izleyicisi; sabitlenmiş branch için `N behind` (`fetch origin X:X`, ref yazımı — `FastForwardUpdater.cs`).

Faz 1 tek başına değerlidir: bugünkü in-place Sync'le bile renkli, kümülatif, kanıtlı listeyi verir.

## 7. Kararını istediğim noktalar

1. **Varsayılan branch modu:** "checkout'u izle" (önerim) mi, "son seçimi hatırla" mı?
2. **Açılışta otomatik Sync:** evet (önerim) — repo kayıtlıysa sormadan.
3. **Sync'te reveal:** her Sync'te yeniden listeleme (bugün) mi, yapı aynıysa yerinde renk geçişi mi?
4. **In-place mod:** tamamen kalksın (önerim) mi, UI'sız gelişmiş ayar olarak kalsın mı?
5. **Sıra:** Faz 1 önce (önerim) mi, ikisi tek çalışma mı?
