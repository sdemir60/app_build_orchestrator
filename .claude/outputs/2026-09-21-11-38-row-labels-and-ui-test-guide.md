# Satır etiketleri ve ekran testleri rehberi

Bu dosyadaki her kural koddan okundu (`main`, 2026-09-21). Parantez içindeki yollar `src/` altındadır. Koddan
doğrudan okunamayıp kodun akışından çıkarılan maddeler **[çıkarım]** diye işaretli; testte önce onlara bakılmalı.

Kısaltmalar: **A** bir proje, **B** A'yı kullanan (A'ya bağımlı) proje, **C** de B'yi kullanan proje.
Zincir: A → B → C.

---

## 0. Önce bilinmesi gereken üç şey

### 0.1 Araç bir projeye iki yoldan bakar

Hangi yolun kullanılacağına her Sync'te **proje başına** karar verilir (`Core/Incremental/OutputEvidence.cs:79-92`).

| Yol | Ne zaman | Neye bakar |
|---|---|---|
| **Defter yolu** | Araç bu projeyi daha önce derlemiş ve DLL, aracın son derlemesinden **yeni değil**. | Kaynak dosyaların **içeriğine** (özet/imza). Tarihe bakmaz. |
| **Zaman yolu** | Araç projeyi hiç derlememiş **ya da** DLL, aracın son derlemesinden **yeni** (başkası, ör. VS derlemiş). | Dosya **tarihlerine**: kaynak dosya DLL'den yeni mi, kullandığı DLL'ler bu DLL'den yeni mi? |
| **Kanıtsız** | Araç projenin DLL yolunu hesaplayamıyor: **SDK-style** proje, `OutputType` yok, yolda `$(` var. | Yalnız defter. Bu projelerde **VS'de derlemek hiçbir zaman fark edilmez.** |

Pratik anlamı: **VS'de derlediğiniz proje bir sonraki Sync'te zaman yoluna geçer.** Araç orada tarihlere bakar.

### 0.2 Rengi etiketin gerekçesi belirler (`App/Controls/StandingStatus.cs:30-44`)

| Renk | Gerekçeler |
|---|---|
| 🟢 yeşil ✓ | `up to date` (UpToDate, WaitingForDependency, BuiltOutside) |
| ⚪ gri, kesikli halka | `modified`, `affected`, `never built` |
| 🔴 kırmızı ✗ | `failed` |
| boş, etiket yok | Henüz Sync yapılmadı ("Not synced") |

### 0.3 "Kendi dosyası" ne demek (`Core/Incremental/ProjectInputs.cs:53-83`, `BuildSignature.cs:53-54`)

- **Sayılanlar:**
  - `.csproj`
  - `.cs`, `.xaml`, `.resx`, `.props`, `.targets`
  - Yukarı klasörlerdeki ilk `Directory.Build.props` / `Directory.Build.targets` / `Directory.Packages.props`
- **Sayılmayanlar:** `.dll`, `.config`, `.json`, `packages.config`, `.md`. Bunları değiştirmek **etiketi değiştirmez**.
- `bin/` ve `obj/` klasörlerine bakılmaz.

---

## 1. Etiketler

### 🟢 `up to date`

**Anlamı:** Projenin çıktısı güncel. Build'de derlenmez, `skipped — up to date` diye atlanır.

Tooltip üç şekilde gelir (`App/ViewModels/DecisionLabel.cs:108-116`):
- `Up to date`: araç derledi, o günden beri hiçbir şey değişmedi.
- `Up to date — built outside this tool`: çıktıyı başkası (VS) üretti ve çıktı güncel.
- Yanında ⚠ `Dependency issue: X` varsa: proje, **hatalı olan X'e karşı** başarıyla derlenmiş. Aşağıdaki "bekleyen" senaryolarına bakın.

**Bu etikete geliş senaryoları:**
1. Araçtan Build → proje başarıyla derlendi.
2. Kaynak dosyayı değiştirip **aynı içeriğe** geri aldın, sonra Sync. Defter yolu içeriğe baktığı için yeşil döner (`WillBuildEvaluator.cs:149-162`).
3. Projeyi **VS'de derledin**, sonra Sync. Proje zaman yoluna geçer. Hiçbir kaynak dosyası DLL'den yeni değilse ve kullandığı DLL'ler de yeni değilse → yeşil, tooltip "built outside this tool" (`OutputEvidence.cs:101-121`).
4. Önce kaynakta değişiklik yaptın, **Sync yapmadan VS'de derledin**, sonra Sync → yeşil (built outside). Değişiklik VS derlemesine girdi.
5. Araçta **kırmızı** olan projeyi VS'de düzeltip başarıyla derledin, sonra Sync → yeşil (built outside). Zaman yolu eski hatayı dikkate almaz (`WillBuildEvaluator.cs:118-121`).
6. **Bekleyen (⚠ ile yeşil):** A hata verdi ama B yine de derlendi. Araç hatalı bağımlılıkta durmaz, B'yi A'nın **son sağlam DLL'ine** karşı derler (`Core/Scheduling/ReadySetScheduler.cs:12-13`). Görülen:
   - akışta `B built — dependency issue`
   - konsolda amber `warning: A failed in this run — last successful output referenced (A)`
   - sonra B yeşil `up to date` + ⚠ `Dependency issue: A`
7. **Bekleyen, tek satır Build ile:** B'yi satırın ▶ butonuyla tek başına derledin, ama A o sırada derlenmeyi bekliyordu (gri). B, A adını taşıyan bir notla kaydedilir (`Core/Planning/ProjectRunScope.cs:64-70`). **[çıkarım]** Sonraki Sync'te B yeşil + ⚠ `Dependency issue: A` görünür.

**Buradan çıkış senaryoları:**
- Kendi dosyasını değiştirip Sync → `modified`.
- Kullandığı projeyi değiştirip Sync → `affected`.
- DLL'i silinirse ya da Clean yapılırsa → `never built`.
- **Bekleyen B için:**
  - A hâlâ hatalıyken Build → B derlenmez. Akışta `B skipped — dependency still failing` yazar, B yeşil + ⚠ kalır (`Planning/ConditionalRebuild.cs:62-75`).
  - A düzelirse (aynı Build'de başarılı olursa ya da VS'de derlenip yeşil görünürse) → Build B'yi yeniden derler, ⚠ kaybolur.
- Bekleyen B, `✓ Up to date` sayacına **sayılır**. Sync satırındaki "to build" sayısına **sayılmaz** (`SyncWorkspaceService.cs:336-339`).

---

### ⚪ `modified` / `modified · local`

**Anlamı:** Projenin **kendi** dosyası son derlemeden sonra değişti. Build'de derlenecek.
**· local**, değişen dosyalardan en az biri commit edilmemiş (git'te kirli) demektir (`Core/Workspace/LocalEdits.cs:28-58`).

Tooltip'ler (`DecisionLabel.cs:128-143`):
- Defter yolu: `Its own files changed since the last build`. Kirli dosya varsa: `… — includes uncommitted edits`.
- Zaman yolu: `Its own files are newer than its build output`.

**Bu etikete geliş senaryoları:**
1. A'da bir `.cs` dosyasını düzenledin, kaydettin, **commit etmedin**, Sync → `modified · local`.
2. Aynı değişikliği commit ettin, Sync → `modified`. Artık "local" yazmaz.
3. A'ya yeni bir `.cs` dosyası ekledin (untracked), Sync → `modified · local`. Yeni klasör de sayılır.
4. `.csproj`'u düzenledin (ör. referans ekledin), Sync → `modified`.
5. Branch değiştirdin ve o branch'te A'nın dosyaları farklı → `modified`.
6. "N behind" chip'inden pull yaptın ve gelen commit A'nın dosyalarını değiştirdi → `modified`.
7. A zaman yolundayken (VS'de derlenmişken) bir dosyasını kaydettin, Sync → `modified`, tooltip "…newer than its build output". Burada içerik değil **tarih** belirler: dosyayı aynı içerikle yeniden kaydetmek bile modified yapar.
8. Projenin klasöründeki `Directory.Build.props`'u değiştirdin → o klasörün altındaki bütün projeler `modified` olur.

**Buradan çıkış senaryoları:**
- Build başarılı → `up to date`.
- Build'de derleme hatası → `failed`.
- VS'de başarıyla derleyip Sync → `up to date` (built outside).
- Değişikliği geri alıp Sync:
  - Defter yolunda → `up to date`.
  - Zaman yolunda → **[çıkarım]** geri alma dosyanın tarihini yeniler, bu yüzden `modified` kalır.
- `local` yalnız Sync sırasında hesaplanır. Build sırasında ekranda Sync'teki değer kalır (`App/ViewModels/RunViewModel.cs:1884-1888`).
- Harici (external) projelerde `local` **hiçbir zaman** yazmaz (`LocalEdits.cs:11-14`).

---

### ⚪ `affected`

**Anlamı:** Projenin **kendi** dosyası aynı, ama derlendiği şey değişti. Build'de derlenecek. Dört farklı nedeni var.

**a) Kullandığı proje değişti (defter yolu).** Tooltip: `Its own files are unchanged — a dependency changed`.
- B'nin imzası A'nın imzasını da içerir (`BuildSignature.cs:86-117`). Bu yüzden A'nın içeriği değişince B de, C de (bütün zincir) `affected` olur.
1. A'da dosya değiştir, Sync → A `modified`, B ve C `affected`.
2. A'da sadece bir `.resx` ya da `.xaml` değiştir → yine A `modified`, B ve C `affected`.
3. A'da `.config` ya da `.md` değiştir → **hiçbiri** değişmez.
4. A değişti; A'yı araçta derledin ama B'yi derlemeden Build durdu → B `affected` kalır.
5. A'yı **VS'de** derledin (A yeşil, built outside), B'yi derlemedin, Sync → B `affected`. B'nin imzası A'nın yeni içeriğini gördü.
6. A'yı VS'de **içeriği değiştirmeden** yeniden derledin (Rebuild):
   - B defter yolundaysa → B `up to date` kalır. İçerik aynı olduğu için imza da aynı.
   - B zaman yolundaysa → aşağıdaki (b) maddesine bakın.

**b) Kullandığı DLL, kendi DLL'inden yeni (zaman yolu).** Aynı tooltip.
- B zaman yolundadır (VS'de derlenmiş). B'nin HintPath ile gösterdiği DLL'lerden biri, B'nin kendi DLL'inden yenidir (`OutputEvidence.cs:117`).
7. A ve B'yi VS'de derledin (ikisi de yeşil, built outside). Sonra **yalnız A'yı** VS'de yeniden derledin, A'nın DLL'i B'nin HintPath klasörüne kopyalandı, Sync → B `affected`.
8. Üçüncü taraf bir DLL'i (HintPath hedefi) yenisiyle değiştirdin:
   - Zaman yolundaki projede → `affected`.
   - Defter yolundaki projede → **fark edilmez**, çünkü DLL girdi sayılmaz (`BuildSignature.cs:20-21`).

**c) Zincirde bayat bir proje var (zaman yolu dalgası, `Core/Incremental/IncrementalPlanner.cs:254-280`).**
- Derlenecek bir A'nın aşağısındaki, zaman yolunda olup kendi başına yeşil görünen her proje `affected` olur.
9. A, B ve C VS'de derlendi (hepsi yeşil). A'da dosya değiştir, Sync → A `modified`, B ve C `affected`.
10. İstisna: A **kırmızı** (`failed`) ise dalga başlamaz. B ve C yeşil kalır (`IncrementalPlanner.cs:313-316`).

**d) Paylaşılan klasördeki kopya tutmuyor.** Tooltip: `Its copy in the shared folder does not match its build output`.
- Araç A'yı başarıyla derlediğinde, A'nın DLL'inin B'nin HintPath klasöründeki kopyasını öğrenir (`OutputEvidence.cs:59-73, 159-169, 208-210`).
- O kopya silinirse, boyutu farklıysa ya da A'nın DLL'inden eskiyse A `affected` olur.
11. Araçla A'yı derle. Sonra paylaşılan klasördeki `A.dll` kopyasını sil ya da eski bir sürümle değiştir, Sync → A `affected` (bu durumda A'nın kendisi etkilenir).
12. VS derlemeleri bu öğrenmeyi yapmaz. Hiç araçla derlenmemiş projede bu etiket çıkmaz.

**Buradan çıkış senaryoları:**
- Build → önce A, sonra B derlenir. Başarılıysa B `up to date`.
- A Build'de hata verirse B **yine derlenir** (A'nın son sağlam DLL'ine karşı) → B yeşil + ⚠ `Dependency issue: A`. B gri **kalmaz**.
- VS'de B'yi de derleyip Sync → B `up to date` (built outside).

---

### ⚪ `never built`

**Anlamı:** Araç bu projenin geçerli bir çıktısını bilmiyor. Build'de derlenecek.
Tooltip: `No build output known to this tool` (`DecisionLabel.cs:95-97, 148`).

**Bu etikete geliş senaryoları:**
1. Projeyi araç hiç derlemedi, DLL de yok.
2. Bakım kutusundaki **Clean** (silgi) tüm projelerin `bin/` ve `obj/` klasörlerini ve kayıtlarını siler, sonra kendiliğinden Sync yapar → projeler `never built` olur (`Core/Workspace/CleanWorkspaceService.cs:66-138`).
3. Satırın ⋯ menüsünden **Clean** → o proje `never built` olur (`RunViewModel.cs:2061-2067`).
4. Projenin DLL'ini `bin/`'den elle sildin, Sync → `never built`.
5. Build sırasında proje **zaman aşımına** uğradı, zorla durduruldu ya da MSBuild başlatılamadı. Bu, derleme hatası sayılmaz, kanıtsız geçersizleşmedir → **kırmızı değil gri** `never built` (`State/BuildStateStore.cs:180-195`).
6. Resolve cycles döngüyü oturtamadı (ilerleme yok ya da tur sınırı doldu) → döngü üyelerinin hepsi `never built` (`Supervisor/RunCoordinator.cs:1704-1713`).
7. Uygulama ya da motor derleme sırasında çöktü; açılışta yarım kalan projeler → `never built` (`State/InFlightLedger.cs`).

**İstisna [çıkarım]:**
- Projenin `OutputPath`'i `bin/` dışında paylaşılan bir klasörse, Clean o DLL'i silmez. Kayıt silindiği için proje zaman yoluna geçer ve `up to date` (built outside) görünebilir.
- Kilitli olduğu için silinemeyen DLL için de aynısı geçerli.

**Buradan çıkış senaryoları:**
- Build başarılı → `up to date`. Hata → `failed`.
- VS'de derleyip Sync → `up to date` (built outside). Bu yalnız SDK-style olmayan projelerde olur.

---

### 🔴 `failed`

**Anlamı:** Son derlemede **derleyici hatası** çıktı (MSBuild hata koduyla bitti). Build'de yeniden denenir.

Tooltip (`DecisionLabel.cs:99-103`):
- `Failed at this source — Build will retry it`
- Döngü üyesinde: `Failed at this source — Resolve cycles will retry it`

**Bu etikete geliş senaryoları:**
1. Koda derlenmeyen bir satır yaz, Build → proje `failed`. Satır bir kez sallanır, süre kırmızı yazılır.
2. Kırmızı projeye dokunmadan tekrar Sync → kırmızı kalır. Kural: aynı kaynak, aynı hata.

**Buradan çıkış senaryoları:**
- Hatayı düzeltip Sync, Build yapmadan → gri olur (`WillBuildEvaluator.cs:150-157`):
  - daha önce başarıyla derlenmişse `modified`
  - hiç başarıyla derlenmemişse `never built`
- Hatayı düzeltip Build → `up to date`.
- Hatalı değişikliği **tamamen geri alıp** (son başarılı içeriğe dönüp) Sync → `up to date` (`RunCoordinator.cs:1888-1891`).
- VS'de düzeltip başarıyla derle, Sync → `up to date` (built outside). A'yı bekleyen projelerin ⚠'si bir sonraki Build'de çözülür, çünkü A artık sağlam sayılır (`ConditionalRebuild.cs:118`).
- VS'de derleme **başarısız** olursa VS yeni DLL yazmaz. **[çıkarım]** Bu yüzden `failed` kalır.
- SDK-style projede VS derlemesi fark edilmez. Kırmızı ancak araçta başarılı bir Build ya da kaynak değişikliğiyle kalkar.

---

## 2. ⚠ Uyarı üçgeni (etiketin yanında, ayrı işaret)

Derleme sırasında gizlenir. Tooltip, aşağıdaki sırayla ilk tutan koşulu gösterir (`App/ViewModels/RowWarning.cs:51-64`):

1. `Cycle did not converge — its projects are still out of date`
2. `Cycle did not fully settle — output may be one generation stale`
3. `In a dependency cycle`
4. `Dependency issue: A` ya da `Dependency issue: A +2`. Ortak önek atılır, ör. `OSYS.`.

---

## 3. Döngüdeki (cycle) projeler

- Grafta çekirdek her zaman **amber**. Listede satır, gerçek durumunun rengini taşır.
- Build ve Rebuild döngü üyelerini **hiç derlemez**. Akışta `X skipped — in dependency cycle` yazar (`ReadySetScheduler.cs:95-98`).
- Etiket yine hesaplanır: döngü üyesi `modified` görünebilir ama Build onu derlemez. Bunu ⚠ söyler.
- Döngü tek parça davranır: bir üye değişince bütün üyeler bayat olur (`IncrementalPlanner.cs:120-137`).
- Zaman yolunda: üyelerden biri bile güncel değilse, güncel görünen üyeler de `affected` olur (`OutputEvidence.cs:130-152`).
- **Resolve cycles:** döngüleri ve onların kullandığı projeleri turlarla (en çok 3 tur) derler. Kapsam dışındakiler tek satırla geçer: `N outside cycle scope — skipped`.
  - Oturdu → yeşil.
  - Oturmadı → `never built` + ⚠ "did not converge" ya da "did not fully settle".
  - Döngünün tüm üyeleri güncelse grup hiç derlenmez.
- Döngü üyesini satır ▶ ile tek başına derlemek mümkündür. Döngü kardeşleri ⚠ notu olarak yazılır.

---

## 4. Build türleri ne yapar

| İşlem | Ne derler | Not |
|---|---|---|
| **Build** (F5) | Gri + kırmızı projeler. Bekleyen (yeşil + ⚠) projeler, bağımlılığı düzeldiyse. | Yeşiller `skipped — up to date`. Döngüler atlanır. |
| **Rebuild** (Ctrl+F5) | Döngü dışındaki **tüm** projeler. | MSBuild'e `-t:Build` gönderilir, kayıtlar yenilenir (`RunCoordinator.cs:1008-1016`). |
| Build menüsündeki **Clean** | — | Her zaman devre dışı: "not available yet". |
| Satır **▶ / ⋯ Build** | Yalnız o proje, değişmemiş olsa bile. | Bağımlılıklar derlenmez. Bayat olanlar ⚠ notu olur. |
| Satır **⋯ Rebuild** | Yalnız o proje, `-t:Rebuild`. | — |
| Satır **⋯ Clean** | O projede `msbuild /t:Clean`. | Sonra proje `never built`. |
| Bakım **Clean** (silgi) | Tüm `bin/` ve `obj/` + kayıtlar. | Onay sormaz. Liste boşalır, sonra kendiliğinden Sync. |
| **Stop** | Yeni proje başlatmaz. Derlenmekte olanlar biter. | Buton `Stopping…` olur. Akışta `Stopped — N remaining projects queued`. Başlamamış projelerin durumu değişmez. |

---

## 5. Ekrandan test listesi

Her testte: işlemi yap → **etiket, renk, ⚠, akış satırı, konsol satırı** beklenenle aynı mı? Tutmayan testin
numarasını ve ekran görüntüsünü alın.

### A. Temel akış
1. Açılışta Sync kendiliğinden çalışır → bütün satırlarda etiket vardır, boş etiket kalmaz.
2. Hiçbir şeye dokunmadan Build → hiçbir proje derlenmez. Hepsi `skipped — up to date`, yeşil kalır.
3. A'da bir `.cs` değiştir, commit etme, Sync → A `modified · local`, B ve C `affected`.
4. Değişikliği commit et, Sync → A `modified` ("local" yok), B ve C `affected`.
5. Build → sıra A, sonra B, sonra C. Üçü de yeşil. Diğer projeler atlanır.
6. A'da değişiklik yap, Sync, sonra değişikliği geri al (içerik aynı), Sync → A, B ve C yeşile döner.
7. A'da yalnız bir `.md` ya da `.config` değiştir, Sync → hiçbir etiket değişmez.
8. A'ya yeni, untracked bir `.cs` dosyası ekle, Sync → A `modified · local`.
9. `Directory.Build.props` değiştir, Sync → altındaki bütün projeler `modified`.

### B. Hata
10. A'ya derlenmeyen kod yaz, Build → A kırmızı `failed` ve satır bir kez sallanır.
    - B ve C **derlenir** (akışta `B built — dependency issue`).
    - Konsolda amber `warning: A failed in this run — last successful output referenced (A)`.
    - B ve C yeşil + ⚠ `Dependency issue: A`.
11. 10'dan sonra hiçbir şeye dokunmadan Sync → A kırmızı, B ve C yeşil + ⚠. "to build" sayısı B ve C'yi saymaz.
12. 10'dan sonra düzeltmeden Build → A yine denenir ve yine kırmızı olur. B ve C `skipped — dependency still failing`, yeşil + ⚠ kalır.
13. A'daki hatayı düzelt, Sync (Build yok) → A gri `modified`.
14. Sonra Build → A yeşil, ardından B ve C **yeniden derlenir**, ⚠ kaybolur.
15. A'daki hatalı değişikliği tamamen geri al, Sync → A yeşil.
16. Proje sayfası (satıra tıkla), kırmızı satırda → `Will build — it failed at this source.`

### C. VS ile derleme (SDK-style olmayan projelerde)
17. A'yı araçta derle. A'da değişiklik yap, **Sync yapmadan** VS'de A'yı derle, sonra araçta Sync.
    - A yeşil, tooltip `Up to date — built outside this tool`.
    - B `affected` (defter yolu).
18. 17'den sonra Build → A **derlenmez**, B ve C derlenir.
19. A'yı VS'de **değişiklik yapmadan** yeniden derle, Sync → A yeşil (built outside). B, araçla derlendiyse yeşil kalır.
20. A, B ve C'nin hepsini VS'de derle, Sync → üçü de yeşil (built outside).
21. 20'den sonra A'da dosya değiştir (VS'de derleme), Sync → A `modified` (tooltip "…newer than its build output"), B ve C `affected`.
22. 20'den sonra **yalnız A'yı** VS'de yeniden derle (DLL B'nin HintPath'ine kopyalanır), Sync → A yeşil, B `affected`.
23. Kırmızı A'yı VS'de düzeltip başarıyla derle, Sync → A yeşil (built outside).
    - B ve C yeşil kalır.
    - Sonraki Build B'yi yeniden derler, ⚠ kaybolur.
24. A'yı VS'de **hatalı** derle (derleme başarısız), Sync → A'nın etiketi değişmez.
25. VS'de derlenmiş A'da dosyayı **aynı içerikle** yeniden kaydet, Sync → A `modified`. Zaman yolu tarihe bakar.
26. SDK-style bir projeyi VS'de derle, Sync → etiketi **değişmez**. Araç bu projede VS'i göremez.
27. Hiç araçla derlenmemiş (yalnız VS'de derlenmiş) bir çözüm aç, Sync → derli projeler yeşil (built outside), derlenmemişler `never built`.

### D. Clean / never built
28. Bakım kutusundaki Clean (silgi) → onay sormaz.
    - Buton amber olur ve spinner döner.
    - Liste ve graf boşalır.
    - Bitince Sync kendiliğinden çalışır, projeler `never built` olur.
29. 28'den sonra Build → hepsi derlenir, yeşil olur.
30. 28'den sonra Build yerine **VS'de** tüm çözümü derle, Sync → hepsi yeşil (built outside). Sonra Build → hiçbir şey derlenmez.
31. Satırın ⋯ menüsünden Clean → yalnız o proje `never built`.
32. Bir projenin DLL'ini `bin/`'den elle sil, Sync → `never built`.
33. Paylaşılan bir OutputPath'e çıktı veren proje varsa Clean'den sonra etiketine bak → **[çıkarım]** yeşil görünebilir.
34. Build sırasında Stop → derlenmekte olanlar biter ve yeşil olur, başlamamış projeler olduğu gibi kalır.
35. Build sırasında uygulamayı Görev Yöneticisi'nden kapat, tekrar aç → yarım kalan proje `never built`.

### E. Tek satır işlemleri
36. Yeşil bir satırda ▶ → proje derlenir. "Değişmedi" demez; ▶ bir emirdir.
37. A gri (değişmiş) iken yalnız B'yi ▶ ile derle → B derlenir. Sonra Sync → **[çıkarım]** B yeşil + ⚠ `Dependency issue: A`.
38. 37'den sonra Build → önce A, sonra B yeniden derlenir.
39. Satırın ⋯ menüsü ya da sağ tık → Build, Rebuild ve Clean seçenekleri var. Build sürerken üçü de soluk.
40. Derlenen satırda ▶ butonu kırmızı Stop'a dönüşür, fareyi üstüne getirmeden de görünür.

### F. Paylaşılan klasör kopyası
41. Araçla A'yı derle. Paylaşılan klasördeki `A.dll` kopyasını sil, Sync → A `affected`, tooltip "Its copy in the shared folder…".
42. Aynı kopyayı eski bir DLL ile değiştir, Sync → A `affected`.
43. Build → A yeniden derlenir, kopya tazelenir, A yeşil olur.

### G. Döngü
44. Döngüdeki bir projeyi değiştir, Sync → döngünün **bütün** üyeleri bayat görünür, ⚠ `In a dependency cycle`.
45. Build → döngü üyeleri `skipped — in dependency cycle`.
46. Resolve cycles → üyeler turlarla derlenir. Ribbon'da `Resolving cycles · round 1/3 · …`. Bitince yeşil.
47. Döngü yokken Resolve cycles butonu devre dışı, tooltip `…no dependency cycles detected`.
48. Döngü üyelerinin hepsi VS'de derli ise Sync → hepsi yeşil. Biri bayat ise diğerleri de `affected`.

### H. Sync türleri
49. Sync butonu → konsol ve akış temizlenir. Liste ve graf boşalıp yeniden gelir, graf ekrana sığar.
50. Başka bir pencereye geç ve 5 saniyeden sonra geri dön → **sessiz** Sync. Ekran boşalmaz, yalnız değişen satırlar güncellenir.
51. 5 saniyeden kısa sürede geri dön → Sync çalışmaz.
52. Git'te commit at → sessiz Sync, akışta `synced after commit`.
53. Dosya kaydetmek Sync'i **tetiklemez**. Değişikliği görmek için pencereye dönün ya da Sync'e basın.
54. Build sürerken dışarıdan branch değiştir → akışta `interrupted by branch change`.
55. Build sürerken commit at → Build kesilmez.

### I. Git
56. Branch chip'inden temiz ağaçta başka branch'e geç → konsolda `Switched to X (sha) — from Y`. Liste ve graf boşalıp yeniden gelir.
57. Kirli ağaçta branch değiştir ("Stash and switch" kapalı) → konsolda amber `warning: N files have uncommitted changes — commit or stash them first`, akışta amber `branch switch refused — N uncommitted files`. Branch değişmez.
58. Ayarlarda "Stash and switch branches" açıkken kirli ağaçta branch değiştir → `Stashed uncommitted changes: "…" — restore them with git stash pop`, sonra branch değişir.
59. "N behind" chip'i → pull, `Pulled origin/<b> — fast-forward …`. Gelen değişikliğe göre projeler `modified` olur.
60. Kirli ağaçta pull → amber `warning: pull refused — uncommitted changes…`, akışta `pull refused — uncommitted changes`.
61. Branch ayrışmışken pull → amber `warning: pull refused — local branch has diverged…`.
62. Merge sürerken → branch chip'inde amber nokta, tooltip `Merge in progress — finish or abort it in git`. Merge notu konsolda **normal renkte** (amber değil).

### J. Filtre ve graf
63. ✓ chip → yalnız yeşiller. ✗ chip → yalnız kırmızılar. İkisi birlikte açıksa → yeşil **veya** kırmızı olanlar.
64. ⚠ chip yalnız döngü ya da bekleyen proje varken görünür.
65. Σ chip bütün filtreleri kapatır. PROJECTS başlığındaki chip (ör. `Up to date + Warnings`) tıklanınca da kapatır.
66. Filtre kutusu (Ctrl+F) proje adında arar ve chip'lerle **birlikte** daraltır. Esc temizler.
67. Filtre açıkken Build → graf bütün projeleri gösterir, liste filtreli kalır. Bitiş animasyonundan sonra graf da filtreye döner.
68. Reduced motion açıkken ya da hiçbir şey derlenmediğinde → filtre Build bitince hemen geri gelir.
69. Grafta zoom veya kaydırma yap, sonra Build → graf ekrana sığar.
70. Grafta bir düğüm seç → düğüm ve komşularına yakınlaşır. Boş alana tıkla → yeniden ekrana sığar.
71. Build sırasında graf kamerası kendiliğinden oynamaz.
72. Derlenen satırın arkasındaki yanıp sönme satırın tam genişliğinde. Reduced motion açıkken yanıp sönme yok.
73. Proje sayfası (satıra tıkla), yeşil satırda → `Up to date — nothing to compile.` ya da `Up to date — built outside this tool.`

---

## 6. Kodda yorum ile davranışın uyuşmadığı iki yer

Bu dosya hazırlanırken görüldü. İkisi de kod **yorumu**; davranış yukarıda anlatılan gibi.

- `Contracts/Ipc/IpcMessages.cs:79`: yorum, hatalı bağımlılığı olan projenin kaydının hiç yazılmadığını söylüyor. Kod kaydı notla yazıyor (`RunCoordinator.cs:1294-1310`).
- `App/Views/ProjectRowMenu.xaml.cs:61`: yorum, satır menüsündeki Clean'in hep devre dışı olduğunu söylüyor. Kod onu etkin yapıyor (`:108`).
