# A13 — Oturum özeti · 2026-07-30 → 2026-07-31 08:38

**Branch:** `a13-visual-debt-automation` @ `6adfbe9` · **`main`'e merge EDİLMEDİ** · BASE `8e6ebbe`
**Süit:** oturum başı `1509 / 2 / 0` → şimdi `1588 passed / 2 skipped / 2 failed` · guard'lar `69/69` · build `0/0`
> İki kırmızı = bilinen yük-hassas flake'in **kendisi** (B1'in konusu), regresyon değil — izole koşumda yeşil.

---

## 1. Bu oturumda tamamlananlar

| Task | Sonuç | Test |
|---|---|---|
| **T2** üretim boşlukları | complete — 3 fix round, review temiz | +47 |
| **T3** kopya metinleri + ölçü/geometri | complete — 3 dalga (3a/3b/3c) + 2 fix round | +37 |
| **T4** motion sabitleri + negatif guard'lar | complete — 1 fix round, VERDICT APPROVE | +17 |

Her task: taze implementer → review package → **3-lens paralel review** → tek fix dalgası → scoped
re-review → ledger. Her yeni test için **ayırt edicilik** kanıtlandı (değer bozuldu → KIRMIZI → geri alındı).

### T2 — kesilen dalganın kurtarılması
Oturum, T2'nin **commit edilmemiş ve doğrulanmamış 18 dosyası** ile açıldı. Sıfırdan doğrulandı
(build 0/0 · süit 1525/2/0 · guard 69/69), 11 açık bulguya karşı okundu → **10/11 kodda karşılanmış**
çıktı, `af9a5ca` olarak commit'lendi. Ardından 2 fix round daha:

- **C1 (Critical):** branch seed `Branch`'i kalıcı çiviliyordu — harici `git checkout` sonrası uygulama
  kendini düzeltemiyor, `forced + UseWorktree=false` kombinasyonunda **UI motorun tersini gösteriyordu**.
  Çözüm: açık seçim ↔ bayat seed ayrımı (`_branchChosenByUser`), `ReconcileBranchWithInventory`,
  türetilmiş `EffectiveUseWorktree`, ve `StartRunCommand`'a `Branch` değil `RunBranchIntent` gitmesi.
- **m9 ölçüldü ve iddia ÇÜRÜTÜLDÜ:** WPF `ScrollViewer`, `ItemsSource` reset'inde `VerticalOffset`'i
  **korur** (başa dönmez). Raporun üç yerindeki yanlış gerekçe düzeltildi; reset semantiği kararı korundu.
- **fix round 3:** `ActionBar` worktree envanterini dinlemiyordu (chip `main-1` derken title bar `main-2`) ·
  `FollowScrollController.Rebind` seçim generation'ını artırmıyordu.

### T3 — üç dalga + kullanıcı kararıyla üç üretim düzeltmesi
33 yeni test (kopya metinleri a1-a12 · ölçü/geometri b1-b12 · token/bar/graf c1-c11).
Lens1, **33 assert'in tamamını otoriteye karşı bizzat açıp doğruladı** — uydurulmuş değer, vakum ya da
totoloji yok. `b11`'in totolojik assert'i (`Assert.Equal(DsSplitter.GrabBand, col.Width)`) düzeltildi.

**Üç üretim sapması bulundu, üç lens bağımsız doğruladı, kullanıcı kararıyla üçü de kapatıldı:**
1. Title bar zemini `Brush.Surface` (#141417) → otorite `--surface-base` (#0e0e10)
2. Title bar'ın **1px `border-subtle` alt hairline'ı üretimde hiç yoktu**
3. Konsol "ready" satırında `HH:mm:ss` damgası yoktu (otorite: `HH:mm:ss ▮ ready`, imleç metinden **önce**)

Fix dalgalarında ayrıca: `ShowReady` kablosunun **iki çağrı yeri de headless sürülemiyordu** → saat
`ConsoleView.WallClock` ctor seam'ine alındı; `MaxWidth` testleri sayı yerine **davranış** (ellipsis +
taşmama) pinler hâle getirildi; üç kopya fixture ve ata-yürüyüşünün üç kopyası tek yere toplandı.

### T4 — değerler doğru, pinleme gücü zayıftı
12 kalem (motion m1-m6 · negatif guard n1-n6), +17 test. **Üretim-otorite sapması yok** — m1-m6'nın altısı
da `BuildApp.jsx` literalleriyle birebir; eksik olan yalnız test kapsamıydı.

3-lens review'un ortak teşhisi: birkaç test **ölçtüğünü sandığı şeyi ölçmüyordu**:
- `ProjectRowTests`'teki shake süre assert'i **kendi bekleme timeout'unu** ölçüyordu; koşulsuz `PumpUntil`
  bir **gizli `Thread.Sleep`**ti; ön-koşul assert'i olmadığı için shake hiç oynamasa da yeşil kalıyordu.
- "BİR KEZ" iddiası `RepeatBehavior`'ı hiç okumuyordu (yanlış özellik: `FillBehavior.Stop`).
- m2/m4/m6 sabitin **değerini** hiç pinlemiyordu (imleç sönmesi 420 yerine 1100 olsa test yeşil kalırdı).

Hepsi kapatıldı; üç motion sabiti `internal` yapılıp **otorite literaliyle** saf assert'e bağlandı
(re-reviewer totoloji kontrolünü ayrıca yaptı, geçti).

---

## 2. Kapanan gerçek üretim kusurları (toplam 9)

T2'de 6: title bar bağlam metni · branch envanteri (`ListBranchesCommand` hiç gönderilmiyordu) ·
proje listesi filtresi (`VisibleProjects` **sıfır tüketiciliydi**) · filtre chip'i · "No projects match this
filter." · worktree envanteri (`ListWorktreesCommand`, çakışan ad üretiyordu).
T3'te 3: title bar zemini · title bar alt hairline · konsol ready damgası.

---

## 3. Ölçümler

- **Süit maliyeti:** sessiz makinede **2 dk 26 sn** (oturum başı yük altında 5 dk 59 sn'ydi).
- **E1 flake teşhisi kesinleşti:** aynı ağaçta iki ardışık tam süit koşumu **farklı isimlerde ve farklı
  sayıda** kırmızı verdi (3 → 2); ilgili testler **izole koşumda 3/3 yeşil (825 ms)**. Ortak kök
  `EngineHost.cs:92`'nin sabit `WaitAsync(5s)`'i.
- **Sıralama kararı (kontrolcü):** B1 (E1 flaky üçlüsü) T5/T6'nın **önüne alındı** — flake artık her task'ın
  doğrulamasını güvenilmez kılıyor. Task listesi değişmedi, yalnız sıra.

---

## 4. Kayda geçen borçlar (final triyaja gidecek)

- **Daktilo imlecinin konumu** — otoriteden sapıyor, "A13.2 kararı" diye **yanlış etiketlenmişti**;
  artık dürüst etiketli ve karakterizasyon testiyle pinli, ama **kapatılmadı**.
- **`CursorHoldMs = 420` üç kopya** — yalnız biri pinli (`ConsoleView.xaml.cs:34`), diğer ikisi
  (`EventStreamView.xaml.cs:29`, `:328`) pinsiz.
- **Title bar `MaxWidth` 320/200 otoritesiz** — sayılar üretimde kaldı, testler artık davranışı pinliyor.
- `EventStreamView` saati hâlâ `WallClock` seam'ini kullanmıyor · `GraphCullTests:472-502`'de
  `FindResource+Assert.Equal` kalıbı · ata-yürüyüşünde `includeLogical` farkı bugün gözlemlenebilir değil.

---

## 5. Kesinti anı

**Ağaçta commit edilmemiş ve DOĞRULANMAMIŞ 5 dosya var** (B1 kapsamı: `EngineHost.cs` ·
`EngineHostTests` · `RunViewModelTests` · `MsBuildInvokerTests` · `SupervisorIpcTests`; 77 ekleme/19 silme).
**Kökeni belirsiz:** B1 implementer'ı dispatch edilmedi (kullanıcı tool çağrısını reddetti); büyük
olasılıkla T4 implementer'ı süit sonucunu beklerken gördüğü flake'lere kendiliğinden müdahale etti.
Build/süit koşulmadı, ayırt edicilik gösterilmedi, rapor yazılmadı → **yarım sayılmalı**, T2'nin
kesintisinde uygulanan doğrulama yöntemiyle sıfırdan değerlendirilmeli.

Ayrıca ağaçta **bu çalışmaya ait olmayan** bir untracked dosya duruyor:
`.claude/outputs/2026-07-30-19-37-scrollbar-restyle-plan.md` (başka bir oturumun ayrı planı, `main` üstünde
ayrı branch öngörüyor) — A13 kapsamında değil, dokunulmadı.
