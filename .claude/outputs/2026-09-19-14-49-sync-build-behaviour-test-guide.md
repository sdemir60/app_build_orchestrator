# Sync / Build davranış rehberi — test için

Kaynak: `sync-build-flow` kodu (c40e5cf) — `main`'e merge edildi (`9167704`), içerik birebir aynı. Kod esastır;
dokümanla farklar §9'da.

## 0. Üç kavram

- **Defter** (`build-state.json`): aracın KENDİ derlediği projelerin kaydı (imza, sonuç, zaman).
- **İmza**: projenin kaynak dosyalarının içerik hash'i (+ bağımlılıklarının imzası + Debug/Release).
- **Kanıt dosyası**: projenin kendi çıktısı (`bin\<cfg>\X.dll/.exe`). SDK-style projede YOKTUR.

## 1. Renk = çıktının durumu (kümülatif)

| Renk / işaret | Anlamı | Ne zaman |
|---|---|---|
| Yeşil ✓ | güncel | imza defterle aynı, ya da dışarıda derlenmiş ve taze |
| Gri, dolu nokta, kesikli halka | derlenecek | değişti / hiç derlenmedi / kanıtsız hata |
| Kırmızı ✗ | kanıtlı hata | derleyici hata verdi VE kaynak o andan beri aynı |
| Gri, dört yaylı halka (başlangıç modu) | bilinmiyor | açılış, Sync gelmeden; ya da repo kökü değişti |
| Amber (şerit, nokta) | işaretli / kuyrukta / derleniyor | YALNIZ koşu sırasında |
| Amber üçgen ⚠ | döngü üyesi ya da bağımlılık sorunu | kalıcı; not durdukça durur |
| Graph'ta amber küp | döngü üyesi | her zaman (çerçeve durumu taşır) |

Bir koşu biten projeyi kendi rengine boyar, bu renk sonraki işleme kadar kalır. Sync rengi silmez, yeniden hesaplar.

## 2. Sync sonrası satır etiketleri

| Etiket | Anlamı | Renk | Tooltip |
|---|---|---|---|
| `up to date · 2h` | güncel, araç 2 saat önce derledi | yeşil | `Up to date — last built 2h ago` |
| `up to date · 5m` | VS vb. dışarıda derlemiş, çıktı taze | yeşil | `Up to date — built outside this tool 5m ago` |
| `modified` | kendi dosyaları değişti | gri | `Its own files changed since the last build` |
| `modified · local` | aynısı, dosya `git status`'ta kirli | gri | `… — includes uncommitted edits` |
| `modified` (zaman kipi) | kendi dosyası çıktıdan yeni | gri | `Its own files are newer than its build output` |
| `affected` | kendi dosyası aynı, bir bağımlılığı değişti | gri | `Its own files are unchanged — a dependency changed` |
| `affected` (kopya) | paylaşılan klasördeki kopyası çıktısıyla uyuşmuyor | gri | `Its copy in the shared folder does not match its build output` |
| `never built` | araç bu projenin çıktısını bilmiyor (hiç derlenmedi, çıktı silindi, kanıtsız hata, Clean) | gri | `No build output known to this tool` |
| `failed · 2h` | bu kaynakla 2 saat önce hata verdi | kırmızı | `Failed at this source 2h ago — Build will retry it` (döngü üyesi: `Resolve cycles will retry it`) |
| boş | karar yok (Sync yapılmadı) | başlangıç modu | — |

- Yaş yoksa kuyruk düşer: yalnız `up to date` / `failed`.
- Yaş kendiliğinden artmaz; satır yeniden çizilince tazelenir.
- Bağımlılığı hatalıyken derlenmiş proje: `up to date` + amber üçgen (`Dependency issue: X +2`).

**Üçgen tooltip önceliği** (güçlüden zayıfa): `Cycle did not converge…` → `Cycle did not fully settle…` → `In a dependency cycle` → `Dependency issue: X +N`. Satır derlenirken üçgen gizlenir.

**Sayaç chip'leri:** `Σ` tümü · derlenen · `✓` yeşiller · `○` griler · `✗` kırmızılar · `⚠` (yalnız varsa). Atlandı chip'i YOK. Kanıtsız hata `○`'da sayılır.

## 3. Sync türleri

| Tür | Kim başlatır | Konsol / akış | Fetch | Pill + "Syncing" |
|---|---|---|---|---|
| Manual | Sync butonu | temizlenir | var | var |
| Appended | açılış, motor restart, pull sonrası, Clean/Optimize sonrası, Settings Save | korunur, altına eklenir | var | var |
| BranchChange | branch değişti (chip'ten ya da dışarıdan) | temizlenir, ilk satır `Switched to …` | yok | var |
| Silent | commit, pencereye dönüş, aynı branch'te HEAD hareketi | dokunulmaz; yalnız warn/error; akışa tek satır | yok | yok |

Silent Sync'in akış satırı: `synced after commit` ya da `synced · N projects changed` (N>0 ise).

Liste ve graph yalnız YAPI değişince (proje eklendi/silindi, katman, kenar) baştan açılır (reveal). Aksi halde satırlar yerinde tazelenir.

## 4. Build'e basınca

1. Şartlar: Sync yapılmış (topoloji var), Sync/Clean/Optimize çalışmıyor, motor hayatta.
2. Tıklama anı: konsol ve akış temizlenir, seçim/filtre düşer, pill `BUILD`, buton Stop olur, şerit `▸ Starting — resolving what to build`.
3. Satırlar koşu alanlarını bırakır ama **rengini korur** (gri'ye inmez).
4. Açılış koreografisi: 440 ms nötr an → dalga (derlenecekler rastgele sırayla amber) → graph'ta kapsam dışı düğümler söner (0.18), kapsam 0.13'e iner. Liste opaklığı değişmez.
5. Komut motora koreografi bitince gider. Bu arada Stop'a basılırsa: `Cancelled — build not started`, hiçbir şey gönderilmez.
6. Planlama satırları konsola düşer (`Scanning solutions`, `Computing incremental state`, …).
7. `runStarted` + önizleme: kuyruk amber'i YALNIZ bu koşunun kesin derleyeceklerine. Koşullu olanlar (bağımlılığını bekleyen) amber almaz.
8. Derlenen satır: dönen halka + amber "nefes" (3.8 s), graph'ta boncuk yörüngesi, şeritte en çok 4 chip. Liste derlenen satırı takip eder (kullanıcı kaydırırsa 5 sn durur).
9. Biten satır anında: başarı → yeşil `up to date · just now`; kanıtlı hata → kırmızı `failed · just now` + sallanma.
10. Bitiş: şerit `Completed — …` ya da `Everything up to date — …`. Graph'ta neon final (hiç derleme yoksa yok). Hata yoksa akıştaki son satır bir kez parlar.

**Neyi derler:**

| Komut | Kapsam |
|---|---|
| Build | derlenecekler (gri/kırmızı). Döngü üyeleri derlenmez. Bağımlılık bekleyen proje, kökü düzeldiyse derlenir, yoksa `skipped — dependency still failing`. |
| Rebuild | döngü dışı herkes, `-t:Build`, önbellek yok sayılır |
| Resolve cycles | döngü üyeleri + onların upstream'i; turlarla (en çok 3). Kapsam dışı satırlar değişmez. |
| Satır → Build | yalnız o proje, güncel olsa bile derlenir. Bağımlılıkları derlenmez; bayat bağımlılık varsa hedefe üçgen düşer. |
| Satır → Rebuild | yalnız o proje, `-t:Rebuild` |
| Satır → Clean | `-t:Clean`, defter kaydı silinir → gri `never built` |
| Bakım kutusu Clean | tüm `bin`/`obj` + defter silinir, ardından Sync (MSBuild çalışmaz) |
| Bakım kutusu Optimize | NuGet restore + artık temizliği, ardından Sync |

## 5. Hata, durdurma, yarım kalma

| Durum | Deftere ne yazılır | Satır | Başka |
|---|---|---|---|
| Derleyici hatası (`exit N`) | hata imzası + zamanı (kanıtlı) | kırmızı `failed · just now`, sallanır | şeritte kırmızı chip (ilk 3 + `+N more`) |
| Hatalı projenin bağımlıları | başarı + "hatalı bağımlılığa karşı derlendi" notu | yeşil + amber üçgen | derlenmeye devam eder, akış `built — dependency issue` |
| Stop | derlenmekte olanlar biter ve normal yazılır; başlamayanlar değişmez | başlamayanlar kendi rengine döner | şerit `▸ Stopped — f/w · k not built` |
| Zaman aşımı (10 dk), öldürülme | kanıtsız hata | gri `never built` (kırmızı değil) | şeritte yine "failed" sayılır, satır yine sallanır |
| Koşu sırasında branch değişti | koşu nazikçe durur; o andan sonra biten her sonuç güvenilmez → kanıtsız hata | gri `never built` | `Run interrupted by a branch change — N built, M not built · logs: …` |
| Motor çöktü | uçuştakiler `run-inflight.json`'da kalır | şerit kırmızı `Engine stopped unexpectedly` + *Restart engine* | yeniden açılışta `previous run was interrupted; N projects will rebuild`, sonra Sync → o projeler gri |
| Pencere X | pencere tepsiye iner | koşu devam eder | — |
| Tepsi → Exit (koşu sürerken) | motor kapatılır | çökme yolu | sonraki açılışta aynı kurtarma satırı |

**Tekrar Build:** yeşil bitenler atlanır; kırmızılar ve kanıtsız hatalar derlenir; başlamayanlar derlenir; bağımlılığı hatalı olanlar kök düzeldiyse derlenir. "Devam et / hatalıları tekrar dene" diye ayrı komut yoktur, Build bu işi yapar.

## 6. Başka editörde (VS) derleme

**Nasıl fark edilir:** VS derlemesi git'e dokunmadığı için izleyici tetiklenmez. Araç şu durumlarda fark eder:
- son Sync'ten 5 sn sonra pencereye dönünce (Silent Sync),
- Sync'e basınca,
- Build'e basınca (motor yeniden planlar).

**Karar (tek cümle):** çıktı aracın kendi derlemesinden yeniyse "başkası derledi" sayılır ve zamanla karar verilir. Değilse defterle (içerikle) karar verilir.

| Senaryo | Sonuç |
|---|---|
| VS'de derledim, sonra dosyaya dokunmadım | yeşil `up to date · Xm`, tooltip `built outside this tool`. Deftere YAZILMAZ; her Sync yeniden kanıtlar. |
| Araç hiç derlememiş, VS önceden derlemiş (ilk kurulum) | yeşil (built outside), `never built` değil |
| VS'de derledim, sonra dosyayı değiştirdim | gri `modified` |
| VS'de derledim, değiştirip geri aldım | gri `modified` (geri alınan dosya çıktıdan yeni) |
| Araç derledi, dosyayı değiştirip geri aldım (VS derlemesi yok) | yeşil kalır (içerik aynı) |
| VS'de derlediğim projeye bağımlı projeler | gri `affected`, Build'de derlenir |
| Dışarıda derlenmiş ama bir upstream'i derlenecek | gri `affected` |
| Çıktı dosyası silinmiş | gri `never built` |
| Paylaşılan klasördeki kopya ezilmiş / eski | gri `affected` (kopya tooltip'i). Yalnız araç o kopyayı daha önce öğrendiyse. |
| SDK-style proje | kredi YOK; yalnız defterle karar |
| VS başka configuration'da derledi | etkisi yok; seçili cfg'nin `bin`'ine bakılır |
| VS derlemesi başarısız | çıktı tazelenmediği için kredi yok |

Zaman kipinde kırmızı yoktur. Döngü grubu: bütün üyeler taze ise hepsi yeşil, biri bayatsa grup bayat.

## 7. Branch değişimi

| Senaryo | Olan |
|---|---|
| Chip'ten branch seçtim, ağaç temiz | `git checkout`; pill `SWITCHING BRANCH` → BranchChange Sync, ilk satır `Switched to X (sha) — from Y` |
| Uzak branch seçtim | yerel varsa ona, yoksa `checkout --track origin/x` |
| Ağaç kirli, ayar kapalı (varsayılan) | hiçbir şey yapılmaz: `N files have uncommitted changes — commit or stash them first` |
| Ağaç kirli, *Stash and switch* açık | `stash push -u` → `Stashed uncommitted changes: "…" — restore them with git stash pop` → checkout. Araç stash'i geri uygulamaz. |
| Koşu sürerken chip | kilitli |
| VS/terminalde checkout | 1.5 sn sessizlikten sonra BranchChange Sync (konsol temizlenir) |
| Commit | Silent Sync, akışa `synced after commit` |
| Aynı branch'te pull/merge/reset | Silent Sync |
| Koşu sırasında commit | koşu durmaz; bitince Silent Sync |
| Koşu sırasında checkout/pull/merge/reset | nazik kesme (bkz. §5), bitince Sync |
| Git işlemi yarıda (merge/rebase/cherry-pick/revert/index.lock) | otomatik Sync bekler (`waiting for git — …`), chip'te amber nokta, checkout/pull kilitli, 2 sn yoklama. `index.lock` 30 sn durursa tek uyarı. Build ve Sync butonu çalışır. |
| `N behind` chip'i | yalnız aktif branch'te: ff-only pull (kirli ya da ayrışmışsa reddeder, nedenini yazar) → Appended Sync |
| İzleyici başlayamadı (reflog yok, ağ sürücüsü) | `HEAD watcher unavailable … — switching back to the window still syncs` |

Aynı içerikli projeler branch değişince yeşil kalır, çünkü karar içerikten verilir.

## 8. Paneller nasıl hareket eder

| An | Proje listesi | Graph |
|---|---|---|
| Açılış (Sync yok) | gri şerit + dört yaylı halka, etiket boş | kesikli çerçeve |
| Sync (yapı değişti) | kademeli belirme (reveal), seçim yoksa başa kayar | düğümler kademeli belirir |
| Sync (yapı aynı) | yerinde renk ve etiket tazelenir | yerinde tazelenir |
| Build tıklama | renk korunur; dalga kapsamı amber yakar | dalga + kapsam dışı söner (0.18), kapsam 0.13 |
| Koşu | kuyruk amber, derlenen nefes alır, takip modu | derlenen parlak + boncuk, kuyruk 0.13, biten 1.4 sn parlak kalır, sonra 0.2 |
| Hata | kırmızı + sallanma | kırmızı çerçeve |
| Bitiş | kalıcı renkler | neon final (Done ve Stopped'da) |
| Seçim | takip durur | kamera düğüme + komşulara; akan amber bağ çizgileri; diğerleri 0.1 |
| Filtre chip'i | liste süzülür, seçim düşer | eşleşmeyen düğümler 0.1 |

Graph'ta üçgen yoktur. Döngü üyesinin küpü her zaman amber'dir.

## 9. Doküman ↔ kod farkları (kod esas)

### A. Olası bug — davranış testte doğrulanmalı (kod okumasıyla bulundu, çalıştırılmadı)

1. **Silent Sync son koşunun satırlarını tazelemiyor.** Koşuda başarılı/hatalı/atlanmış biten satırların etiketi ve rengi Silent Sync'te güncellenmez (`RunViewModel.cs:1883`).
   - Doküman (§10.3, §14.3) "silent Sync refreshes the decisions" diyor.
   - Senaryo: Build → biten projede VS'de dosya değiştir → pencereye dön → satır hâlâ yeşil `up to date` kalır. Manual Sync'te düzelir.
2. **Rebuild'de kuyruk rengi ve sayaç.** Dalga döngü dışı herkesi amber yakar, ama önizleme gelince yalnız "kirli" olanlar kuyrukta kalır; diğerleri amber'i kaybeder. İlerleme paydası da yalnız kirlileri sayar.
   - Hiçbir şey kirli değilse şerit `Checking…` ve sonunda `Everything up to date — nothing to build` diyebilir, oysa her şey derlenir (`RunViewModel.cs:1913`, `Program.cs:130`).
3. **Planlama sırasında Stop.** Koreografi bittikten sonra, `runStarted` gelmeden Stop'a basılırsa faz kısa süre `Stopping → Running`'e döner ve Stop yeniden tıklanabilir olur.
4. **Motor ölünce derlenen satırlar** dönen halkayla kalır; ancak sonraki görünür Sync temizler. Doküman bunu anlatmıyor.
5. **packages.config restore hatası** `exit N` olarak kanıtlı sayılır → satır kırmızı. Doküman yalnız derleyici hatasının kanıt olduğunu söylüyor.
6. **Kanıtsız hata da satırı sallar**, ama satır gri kalır.

### B. Doküman düzeltmesi gerekenler (kod doğru görünüyor)

| Doküman | Kod |
|---|---|
| §8.1: Rebuild "all projects" | döngü üyeleri Rebuild'de de atlanır (doküman kendi içinde çelişiyor, §8.1 satır 930) |
| §8.6: "(Build only) incremental pass" | her modda koşar |
| §8.4: ETA `LastDurationMs`'ten | bu koşunun ortalamasından; ilk proje bitene kadar ETA yok |
| §8.3/§14.3: bitişte `N dependency-affected` | yalnız hata varken yazılır |
| §4.5: Stop'la öldürülen satır kırmızı | gri `never built` (§14.3 ile çelişiyor) |
| §14.3: "membership en zayıf" | tablo ve kod: bağımlılık sorunu en zayıf |
| §7.4: etiket listesi | yaşsız `up to date` / `failed` da var |
| §14.3: kuyruk tek renk kanalı, amber | kuyruk glyph'i gri (#9a9aa2), işaretli glyph soluk; şerit/nokta/graph amber |
| §14.5: komut koreografi BİTİNCE gider | devir (handover) BAŞLARKEN gider |
| §14.5: "36 proje dörtten uzun sürmez" | 36 proje ~1085 ms, 4 proje 330 ms |
| §14.3: "işlem başlamak başlangıç modunu düşürmez" | kararsız satır işaretlenince/kuyruğa girince dolu noktaya geçer |
| §13.6: "Sync'ten sonra düğümler belirir" | yalnız yapı değişince (§13.2 doğru söylüyor) |
| §13.2: yapı imzası "id, ad, katman, kenar" | katman adı ve sıra da dahil |
| §5 / §8.8: interrupt "HEAD hareket edince" | commit hariç |
| §16: harici projelerde commit yok | harici kopyanın kendi revizyonu yazılır (§7.5 doğru) |
| §10.2 tablosu: BranchChange yalnız chip + izleyici | pencereye dönüş ve koşu sonu da açabilir |
| §10.3: `index.lock`'ta Build/Sync uyarı satırı | `index.lock`'ta ikisi de yazılmaz |
| §10.3: koşuda commit "hiçbir şey yapmaz" | koşu bitince Silent Sync koşar |
| README 168-171: dışarıdan branch değişimi konsolu temizlemez | temizler (README 208 doğru) |
| README 212-213: amber nokta listesinde `index.lock` yok | `index.lock`'ta da nokta çıkar |
| §13.2: tıklanabilir akış satırları | döngü bitiş satırları da tıklanabilir |
| §14.3: `Ready — N to build` | `Ready — N to build · M up to date` |
