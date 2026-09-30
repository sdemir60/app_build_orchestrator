# Design v1.23.0 + v1.24.0 uygulama planı (TDD dökümü)

**Spec (bağlayıcı otorite):** `.claude/outputs/2026-09-19-08-05-design-v1.24.0/README.md` → §2.1, §2.3, §2.4,
§2.12, §8 ve §9 `## v1.24.0` / `## v1.23.0` (+ "Uygulama sayıları — güncelleme"). Prototip: aynı klasörde
`prototype/app/BuildApp.jsx` (BA), `prototype/app/build-data.js` (BD), token'lar `prototype/_ds/.../tokens/*.css`.

**Kapsam:** önceki paket v1.22.0 idi (yalnız metin). Bu pakette yeni olan iki konu var; başka hiçbir şeye dokunulmaz:
1. **v1.24.0 — Keşif sürerken panel boş kalmaz** (graf + proje listesi keşif bloğu, bulunan proje sayacı).
2. **v1.23.0 — Güncelleme hapı** (title bar hapı, kart, restart ekranı).

**Kullanıcı kararı (2026-09-29):** "güncelleme buton ve içeriğini sonra çalışacağız, güncelleme süreçlerinin
tasarımını aktarmamız yeterli, şimdilik güncelleme butonu hep görünür olsun." → Güncelleme motoru YOK; UI tasarımı
aktarılır, hap her zaman görünür, kart içeriği örnek veridir.

Keşif raporları (salt-okur inceleme, file:line referanslı): scratchpad `reports/{prototype,discovery,titlebar,tests}.md`.

## Global kurallar

- `CLAUDE.md` (kökte): kırmızı test kuralı, davranış değişince eski testi yeni kuralı pinleyecek şekilde yeniden
  yaz + doc'una eski iddia ve gerekçe, eşik gevşetmek yasak, yeni XAML kökü = realize testi, kopya yasak / tek
  kaynak, token dışı hex/ms yok, UI metni İngilizce, kod yorumu Türkçe.
- Dosya düzenlemede `Get-Content|Set-Content` ve `sed -i` YOK (UTF-8/CRLF bozar) — Edit/Write.
- Commit mesajı dosyaya yazılıp `git commit -F`; Türkçe ASCII, mevcut üslup (`feat(ui): ...`). Claude attribution
  satırı YAZILMAZ (kullanıcı global kuralı).
- Branch: `feat/design-v1.23-v1.24`. main'e merge YOK (kullanıcı bilgisayar başında değil; inceleyip kendisi karar verecek).

## Kararlar (kullanıcı yokken verildi — raporda listelenecek)

### v1.24.0
- **K1 — Ne zaman görünür:** ekrandaki liste boşken (VM satırı yok YA DA plan yüzeyi yeniden başlıyor: Manual /
  BranchChange / ConfigurationChange) ve Sync isteği yolda iken, topoloji gelene (ya da Sync topolojisiz bitene)
  kadar. Kapsananlar: açılış Sync'i, Clean/Optimize sonrası Sync, kök değişimi, Sync düğmesi, branch değişimi,
  Debug/Release. **Sessiz Sync hiç göstermez** (sessiz Sync ekranda iz bırakmaz kuralı korunur). Satırlar dururken
  gelen Appended Sync (pull, yalnız harici/katman Save) göstermez — tasarımın kendi istisnası.
- **K2 — Clean/Optimize'ın kendi penceresi** (Sync başlamadan önce) değişmez; keşif blokları yalnız Sync keşfi içindir.
- **K3 — Okuma sırası:** motor ana repoyu tek klasör yürüyüşüyle önce, harici kökleri sonra okur; tasarımın
  "önce hariciler" sırası için motor taraması yeniden sıralanmaz (keşif milisaniyeler sürer, ara değerler gözle
  görülmez). Sayaç her kaynak okununca kümülatif gönderilir: ana tarama bitince, sonra her harici kök çözülünce.
- **K4 — Sayım:** `external` = o ana kadar çözülen tekil harici proje kimlikleri; `repository` = ana taramadaki tekil
  csproj'lardan harici olmayanlar. Toplam App'te türetilir. Payda yok, kaynak adı yok, motion yok.
- **K5 — Kırılım** (`· N repository · N external`) Sync isteği anında `ExternalProjects.Count > 0` ise gösterilir
  (tasarım: "Settings'te en az bir tanım varken").
- **K6 — Filtre:** filtre Sync'te korunmaya devam eder (tasarımın "filtre çipi Sync'te sıfırlanır" varsayımı
  uygulamada doğru değil, davranışa dokunulmaz); keşif sırasında arama kutusu, `build-order` etiketi ve filtre
  çipi gizlenir, keşif bitince filtre aynen geri gelir. `NoFilterMatch` keşif bloğunun önüne geçemez.
- **K7 — Graf:** keşif sırasında kesikli `Graph appears after Sync` kutusu ve Viewport gizli, başlık sayacı gizli.
  `scroll = zoom · drag = pan` ipucu uygulamada yok; iş yok.

### v1.23.0
- **U1 — Hap her zaman görünür.** Kaynak: `UpdateOffer` (App servis katmanı) — şimdilik tek örnek kayıt
  (`Sample`, açıkça "placeholder, update engine not written yet" diye işaretli): gelen sürüm = kurulu sürümün
  (`AppIdentity.Version`) bir sonraki minor'ı, boyut `18.4 MB`, prototipteki üç örnek madde (1 Performance, 2 Fixed).
  CHANGELOG ve `Version`'a dokunulmaz. Hap, teklif başlangıçta hazır olduğu için ilk karede görünür (giriş animasyonu
  yalnız teklif sonradan gelirse oynar — gelecekteki motor için dikiş).
- **U2 — Kart = mevcut popover altyapısı** (`PopoverBase` + WPF `Popup`, `PopoverToggle`, Esc). Popup ayrı HWND
  olduğu için dialogların altında kalamaz → **bir dialog (Settings, About, What's new, Import) açılırken kart kapanır**
  (tasarım "dialog kartın üstüne açılır" diyor; en yakın karşılık).
- **U3 — Restart kilidi ve sebep sırası:** (1) Clean / Optimize / Resolve / checkout / pull sürüyorsa
  `Available once the running task finishes.` (2) herhangi bir Sync (sessiz dahil) sürüyorsa
  `Available once Sync finishes.` (3) koşu sürüyor / işaretleniyor / bekleyen Build isteği varsa
  `Available once the build finishes — Esc stops it.` — **tasarım "F5 stops it" diyor ama uygulamada F5 durdurmaz,
  Esc durdurur**; tuş adı `ShortcutCatalog`'dan okunur.
- **U4 — Restart to update:** motor olmadığı için restart ekranını oynatır (Closing 800 ms → Installing 1100 ms →
  Starting 800 ms, %20/%78/%100 doğrusal), sonra 280 ms'de söner ve uygulamaya aynen döner (Sync/sıfırlama yok,
  hap kalır). Ekran açıkken klavye ve global kısayollar yok sayılır; ekran title bar dahil tüm pencereyi örter.
- **U5 — Hover süresi** `Duration.Fast` (120 ms, DS standardı; README §2.12'deki "80ms" kendi §9 sayılarıyla ve
  prototip koduyla çelişiyor).
- **U6 — İkon** Lucide circle-arrow-up prototipteki gibi (r=9, stroke 1.7).
- **U7 — Kategori blokları** What's new'deki çizim ortak, ölçü parametreli bir yardımcıya çıkarılır; kart ve dialog
  aynı kodu kullanır (kopya yasak).

---

## Task A — v1.24.0 keşif durumu

Sıra: kırmızı test → kod → yeşil; her alt adım ayrı commit.

A1. **Contracts:** `SyncDiscoveryEvent(int RepositoryProjects, int ExternalProjects)` discriminator `syncDiscovery`.
    Test: round-trip + discriminator (`IpcMessagesTests`, `PlanProgressEvent_roundtrips...` emsali).
A2. **Core:** `ExternalWorkspaceResolver.Resolve`'e opsiyonel ilerleme callback'i (eski imza korunur; Clean /
    Optimize / Supervisor Program değişmez); `SyncWorkspaceService` ana tarama bitince ve her harici kök çözülünce
    kümülatif `SyncDiscoveryEvent` emit eder (yalnız Sync). Testler: emit sırası (`syncStarted` sonrası, topolojiden
    önce), kümülatif/yalnız artan toplam, tekilleştirme (üst üste binen harici kartlar iki kez sayılmaz), harici
    tanım yoksa `ExternalProjects == 0`, sorunlu kart sayıyı artırmaz.
A3. **App VM:** `IsDiscovering`, `DiscoveredRepositoryProjects`, `DiscoveredExternalProjects`,
    `DiscoveryShowsBreakdown` (+ toplam). K1 kuralına göre kurulur/düşer; `syncDiscovery` yalnız keşif sürerken
    sayılır. Testler: her tetik kipi için göster/gösterme matrisi (açılış, Clean/Optimize handover, kök değişimi,
    Manual, BranchChange, ConfigurationChange → göster; Silent, satırlı Appended → gösterme), topolojide düşer,
    topolojisiz Sync bitişinde (hata, motor kaybı) düşer, sayaç kümülatif.
A4. **Metinler** tek kaynakta (`InteractionText`): `Discovering projects`, `Graph appears once projects are
    discovered`, `{n} found`, ` · {r} repository · {e} external`.
A5. **Liste:** `ShellRoot` overlay'i: Lucide *list* 28px (stroke 1.4, `TextFaint`, opaklık 0.7, alt +2px) · 8px ·
    `Discovering projects` (13px/500 `TextSecondary`) · 8px · mono 11px tabular nowrap sayaç (toplam `TextDim`, gerisi
    `TextFaint`); zemin `SurfaceBase`, yatay padding 24. `role=status` + polite live region (sayaç değişince
    `LiveRegionChanged`). Arama kutusu, `build-order`, filtre çipi keşif boyunca gizli (tek kapı: `SetHasWorkspace`
    ile birleşen tek metot). `ListInvite.Resolve` keşfi bilir; eski "Boot/Syncing'de 0 satır → boş liste" testi
    yeni kuralı pinleyecek şekilde yeniden yazılır (doc'a eski iddia + gerekçe).
A6. **Graf:** `GraphView` keşif bloğu: Lucide *network* 28px (stroke 1.4, `TextFaint`, opaklık 0.7) · 10px ·
    `Graph appears once projects are discovered` (12px `TextFaint`); zemin `SurfaceBase`; kesikli kutu + Viewport +
    başlık sayacı gizli. Keşif bitince reveal her zamanki gibi (blok reveal'den ÖNCE kalkar).
A7. **İkonlar:** `Icon.Network`, `Icon.ListLines` (+ `.StrokeThickness` 1.4) `Icons.xaml` + `IconGeometryTests`.
A8. **Realize testleri:** iki blok + başlık gizleme; `MainWindow` kablajı (VM → Shell → GraphView).
A9. **Dokümanlar:** ARCHITECTURE §5 (event listesi), §10.2 (ekran boşaltma anlatısı), §13.2 (liste/graf boş
    durumları, Clean paragrafı), §14.4 (ikonlar), §22 (kod haritası). README gerekiyorsa.

## Task B — v1.23.0 güncelleme hapı (yalnız UI)

B1. **Veri:** `Services/UpdateOffer.cs` — `UpdateOffer(string Version, string Size, IReadOnlyList<ReleaseNote>
    Highlights)` + `Sample` (placeholder) + `NextMinor`. Testler: next minor, highlights KindOrder'a göre çizilir.
B2. **İkonlar:** `Icon.UpdateReady` (circle-arrow-up, 1.7), `Icon.ArrowRight` (1.8); rotate-cw = mevcut `Icon.Rebuild`.
B3. **Hap:** title bar sağ kümesinin başına `ToggleButton` + 1×14 ayraç (mevcut ayraçla tek stil). 22px, padding
    `6,0,8,0`, gap 6, `Radius.Sm`, `SurfaceRaised` + `BorderStrong`; hover/açık `SurfaceOverlay` + `Neutral500`
    (`Duration.Fast`), ikon 13px + `Update` (12px/500 `TextPrimary`) + mono 11px tabular sürüm (`TextDim`, hover/açık
    `TextSecondary`). UIA adı `Update to <v>` (AccessibilityNames), expanded durumu. Mevcut ikonlar yerinden oynamaz
    (test: sağ kümedeki mevcut butonların sağ kenara göre konumu değişmez).
B4. **Kart:** 344px `Ds.Popover` (Padding 0), hapın 9px altında sol kenarı hizalı, drop-in (−4px'ten, ölçek .985,
    origin sol üst, 140 ms — `PopIn` parametrelenir, kopya yok). Üç blok + 1px `Border` ayraçlar:
    (1) `13,16,15`: caps `UPDATE READY` (`TextFaint`) + sağda mono boyut; 11px altında kurulu (mono 13 `TextDim`) →
    arrow-right 12 (`TextFaint`) → gelen (mono 16/500 `TextPrimary`), gap 9.
    (2) `13,16,14`: kategori blokları (ortak yardımcı; blok arası 12, başlık altı 6, girinti 13, madde arası 5,
    12px `TextSecondary` satır 18).
    (3) `12,16,14`: açıklama 12px `TextDim` (varsayılan `Restart takes a few seconds and reopens the workspace. If you
    wait, it installs on the next start.` / kilitliyken U3 sebebi) · 12px altında sağa yaslı `Later` (Secondary.Sm) +
    `Restart to update` (Primary.Sm, `Icon.Rebuild`), aralarında 8.
    Kapatma: Later, hapa ikinci tık, dışarı tık, Esc; bir dialog açılınca (U2). Later hapı gizlemez.
B5. **Kilit:** VM'de tek hesaplanan `UpdateRestartBlockedReason` (U3), `OnWorkspaceBusyChanged` + Resolve
    değişiminde bildirilir. Testler: her durum → metin/disabled; iş bitince kendiliğinden açılır.
B6. **Restart ekranı:** ayrı UserControl, varsayılan Collapsed, pencerenin en üst katmanı (title bar dahil).
    `SurfaceBase`, 180 ms fade-in; 232px ortalı kolon: `AppMark` 30 · 16 · `Updating <AppIdentity.Product>`
    (13/600 `TextPrimary`) · 6 · mono 11 geçiş (kurulu `TextDim` → ok 11 `TextFaint` → gelen `TextSecondary`, gap 7) ·
    20 · 2px `Ds.ProgressBar` (amber) · 9 · adım etiketi 11px `TextFaint` + `…`. Adımlar adlandırılmış sabitlerle,
    tek zamanlayıcı (`StepPlayer` ya da enjekte timer; D8). Bitişte (+120 ms) 280 ms fade-out, sonra Collapsed.
    Klavye (pencere + global hotkey) yok sayılır. `role=status` polite. Reduced motion: fade'ler anında.
B7. **Realize + guard'lar:** hap, kart, restart ekranı realize; Accessibility, NoHardcodedColor/Motion, AntiSlop,
    AppIdentity (ürün adı `AppIdentity.Product`'tan), ShortcutCatalog, SuccessFlourish (Fixed rengi
    `ReleaseNotes.SwatchBrushKey`), MotionOwnerHygiene, ReducedMotionCoverage.
B8. **Dokümanlar:** ARCHITECTURE §12.2 (title bar), §13.3 (popover/kart, restart ekranı), §14.4, §14.5, §22;
    README (title bar öğeleri, varsa). Motorun olmadığı ve hapın şimdilik hep görünür olduğu açıkça yazılır.

## Kapanış

- Tam süit (`Category!=Acceptance`) yeşil, çıktı dosyaya.
- Gözle doğrulama: ekran dışı render (keşif blokları, hap, kart, restart ekranı) prototip görünümüyle karşılaştırılır.
- Kısa sonuç raporu `.claude/outputs/` (aynı ad kökü, `-results`).
