# Sync / Build akışı ve tek çalışma ağacı — özgün analiz: bugün ne, ne olmalı

Tarih: 2026-09-17 · Durum: yalnız analiz, kodda değişiklik YOK · `main` = `ea3ab62`
Kaynak: senin isteğin (Sync sonrası renkli ve kümülatif liste, tek çalışma ağacı, otomatik hizalanma) + kodun ve
dokümanın bugünkü hâli. Kod kanıtları `2026-09-17-19-41-cumulative-status-analysis-verification.md`'de (satır
numaralı); burada tekrar edilmez. Claude web raporunun sonuçları ölçüt alınmadı; örtüşen yerler örtüştüğü için
örtüşüyor.

## 0. Tek bakışta

| | Bugün | Olmalı |
|---|---|---|
| Aracın işi | "Değişeni sırayla derle" | **`C:\OSYS\*\bin` havuzunu, checkout edilmiş branch'in commit'lenmiş kaynağıyla tutarlı tut.** Developer kendi değişikliğini VS'de derler; araç bağımlılıkları taze tutar |
| Renk | Son işlemin hikâyesi; Sync hiçbir şeyi boyamaz | **Çıktının durumu:** yeşil güncel · gri derlenecek · kırmızı bu kaynak derlenmiyor (kanıtlı) · boş bilinmiyor |
| İşlem başlangıcı | Herkes griye iner, yalnız bu koşu boyanır | Kimse renk değiştirmez; dalga yalnız kapsamı amber'a yakar; sonuç toplama eklenir |
| Toplam | Görünmez (her işlem sıfırlar) | 50 yeşil + 2 gri → Build → 51 yeşil 1 kırmızı → satırdan düzelt → 52 yeşil |
| Sync'in gördüğü | Ana (kirli) ağaç; Build başka ağacı derleyebilir | **Build tree** — gördüğün = derlenecek |
| Çalışma ağacı | Üç mod, havuz, LRU, worktree chip'i | **Tek ağaç `<repo>-Build`**, mod yok, chip yok |
| Branch | Niyet; otomatik Sync yok; açılışta checkout'a dönülür | **Checkout'u izle**; HEAD her değiştiğinde (checkout, commit, pull) ve açılışta Sync kendiliğinden |
| Kırmızı | "Son koşu hatalı" bayrağı | **Kanıt:** bu imza derleyiciden geçmedi; kaynak değişince gri |
| Üçgen (bağımlılık) | Koşuya özel, sonraki işlemde silinir | Kümülatif: defter notu varken durur |
| Döngü küpü | Yalnız derlenmediğinde amber | **Her zaman amber**; çerçeve durumu taşır |
| Etiket | 5 sözcük + `failed · retry` + üç parçalı | 5 sözcük: `up to date · 2h` · `modified` · `affected` · `never built` · `failed · 2h` |
| Animasyonlar | — | **Aynen.** Yalnız zemin nötr gri değil, satırın kendi rengi |
| VS'den derleme | Görünmez | Görünmez; **gerek kalmaz** — commit anında Sync yakalar |

## 1. Ölçüt: aracın işi ne

OSYS'de 177 projenin hepsi post-build copy ile `C:\OSYS\{Client,Server,…}\bin`'e yazar ve HintPath'ler oradan
okur (bugün ölçtüm: 1792 absolute HintPath, 217 copy satırı, 177/177 projede PostBuildEvent). Havuz **tek ve
paylaşımlıdır**: VS'nin de aracın da çıktısı aynı klasöre iner. Senin "bağımlılıklarda sorun oluyor, projeyi
derlemek için o projeye dallanıyorsun" dediğin şey tam olarak **havuzdaki DLL'in kaynağın gerisinde kalması**.

Bu yüzden aracın tek cümlelik tanımı şu olmalı: *havuzu, developer'ın üzerinde çalıştığı branch'in commit'lenmiş
kaynağına yetiştir; developer'ın kendi değişikliğini VS derler.* Her karar bu cümleye göre verildi. Ölçüt:
developer kaç adımda ve ne kadar güvenle "hepsi yeşil" görüyor; araç ne kadar az soru soruyor.

## 2. Bugün (ve neden böyle)

- **Renk = son işlemin hikâyesi; Sync boyamaz.** v1.11.0'da üç kanal (sonuç / plan / döngü) aynı piksellerde
  yarışıyordu; plan etikete, döngü tek üçgene indi. Karar doğruydu — bedeli, senin şikâyetin: Sync'ten sonra
  hangisi güncel hangisi bayat, yalnız 10,5 px mono etiketten okunuyor, renkten değil.
- **Her işlem listeyi nötrler.** "Renk = bu işlem" doğru olsun diye. Sonuç: toplam hiç görünmez; satırdan tek
  proje derlediğinde diğer 176'sı griye iner.
- **Üç ağaç modu.** Aktif branch + switch kapalı → in-place (local değişiklikler dahil); aktif + açık → worktree;
  başka branch → worktree zorunlu. Havuz `%LOCALAPPDATA%`'da, 20 GiB LRU, worktree chip/popover'ı.
- **Sync ana (kirli) ağacı tarar, Build başka bir ağacı derleyebilir.** Doküman bunu "bilinen seam" diye
  adlandırıyor. Gördüğün ile derlenecek olan ayrışabiliyor.
- **Branch seçimi niyet.** Otomatik Sync yok; farklı branch seçince "Sync required" yazar ve bekler; açılışta
  seçim checkout'a döner ama Sync yine yok.
- **Defter (`build-state.json`) global ve içerik tabanlı.** Anahtar ana kökteki csproj yolu — worktree'de
  derlense de aynı. İmza yol-göreli ve içerikten; branch değişince değişmemiş proje kendiliğinden güncel. **Bu,
  senin istediğin her şeyin zaten mümkün olmasının sebebi.**
- **Hata kaydı bir bayraktır** ("son koşu hatalı"); hata anındaki imza tutulmaz. Üçgen koşuya özeldir.

Kalması gerekenler — bunlar bugünün doğru kararları, tasarımı üstlerine kuruyorum: DLL/bin timestamp'i
okunmaz (paylaşımlı havuzda bir DLL'in hangi kaynaktan çıktığı bilinemez; timestamp tam da önemli durumlarda
yanıltır) · OutDir'e dokunulmaz · `obj` yalnız worktree'de izole · ana repo asla checkout/reset edilmez, tek
yazım ff-only · tek seferde tek koşu · koreografiler.

## 3. Üç ilke

1. **Renk = çıktının durumu.** Yeşil: bu kaynak derlendi, havuzda. Gri: derlenecek. Kırmızı: bu kaynak
   derleyiciden geçmedi (kanıtlı). Boş: bilinmiyor. Koşu, üstüne geçici amber bindirir; sonuç duruma yazılır
   ve **kalır**. Sync durumu yeniden hesaplar, silmez. Tek kanal ilkesi korunur (şerit, nokta, glyph, çerçeve,
   küp aynı değerden); değişen, kanalın söylediği şey.
2. **Tek ağaç, checkout'u izle.** Araç yalnız `<repo>-Build`'de, yalnız checkout edilmiş branch'in HEAD'ini
   (commit'lenmiş hâl) derler. In-place, mod, havuz, worktree chip'i yok. Sync de o ağacı tarar: gördüğün =
   derlenecek. Uncommitted değişiklik araca hiç girmez; onu VS derler.
3. **Kendiliğinden hizalan.** HEAD değişince (checkout, commit, pull) ve açılışta Sync kendiliğinden koşar.
   Sync ucuzdur (warm'da saniyenin altı) ve ana repoda salt-okurdur; kullanıcıdan "Sync'e bas" istemek için
   sebep yok.

## 4. Durum modeli

| Durum | Ne demek | Renk (şerit · nokta · çerçeve) | Glyph | Etiket |
|---|---|---|---|---|
| Bilinmiyor | Sync yok / imza hesaplanamadı | bugünkü başlangıç modu (halka, kesikli çerçeve) | kesikli daire | boş |
| Güncel | imza = son başarı | **yeşil** | ✓ | `up to date · 2h` |
| Güncel, bekliyor | hatalı bağımlılığa karşı derlendi, kendi imzası değişmedi | **yeşil + ▲** | ✓ | `up to date · 2h` (tooltip kökü söyler) |
| Derlenecek | kendi dosyası / bağımlılığı değişti / kayıt yok | **gri** (bugünkü düz nötr gri) | kesikli daire | `modified` / `affected` / `never built` |
| Bozuk | bu imza derleyiciden geçmedi | **kırmızı** | ✗ | `failed · 2h` |
| Kuyrukta / derleniyor | yalnız koşu içinde | amber | saat / dönen halka | değişmez |

**Kırmızının kuralı.** Kırmızı bir hatıra değil, kanıt: "bu kaynak derleyiciden geçmedi". Defter hata anındaki
imzayı tutar; Sync'te bugünkü imza ona eşitse — kendi dosyası da, hiçbir bağımlılığı da değişmemişse — kırmızı
kalır, çünkü tekrar derlemek aynı sonucu verir. Bir şey değiştiyse kanıt düşer, satır gri (`modified` /
`affected`). Senin "Sync'te hatalıyı bilemem" cümlesine cevap: bilinebilir — aynı kaynağın aynı derleyicideki
sonucu bellidir; bilinmeyen yalnız değişen kaynaktır, o da zaten gridir. Kanıt yalnız derleyici hatasıdır
(exit ≠ 0). Timeout kanıt değildir: o proje gri `never built` olur, tooltip "the last attempt did not finish"
der. Kırmızı hiçbir şeyi engellemez: Build kırmızıları her zaman yeniden dener (paket, ortam değişmiş
olabilir).

**Üçgen kümülatif olur.** Bugün ▲ yalnız koşunun event'inden gelir ve bir sonraki işlem onu siler; Sync'ten
sonra "bekleyen" projenin tek izi üç parçalı etikettir. Yeni: defterde bekleme notu varken ▲ durur; proje
notsuz derlenince düşer. Döngü üyeliği ve tek satırlık tooltip'ler aynen.

**Döngü küpü her zaman amber.** Bugün yalnız "bu işlemde derlenmedi" derken amber; Resolve derleyince sonuç
rengini alıyor. Yeni: küp = üçgenin graftaki vekili, yapısal, hep amber; çerçeve ve şerit durumu taşır (grup
bileşik imzayla ya bütün yeşil ya bütün gri ya bütün kırmızı).

**Etiketler.** Renk durumu söyler; etiket rengin söyleyemediğini: gri için *neden*, yeşil ve kırmızı için *ne
zaman*. `failed · retry` → `failed · 2h`: `retry` bir sözdü (döngü üyesinde zaten tutulamıyordu), dalga o sözü
görünür kılıyor; yaş ise kırmızının anlamlı olduğunu söyleyen şey — kanıt ne kadar taze. `affected · up to date
· 2h` → `up to date · 2h` + ▲: üçgen kalıcı olunca "affected" parçası tekrar. Yuva tasarımın 134 px'ine döner.
Boş yalnız bilinmiyorken.

**Sayaçlar.** Chip'ler durumu sayar: Σ · building · ✓ güncel · ○ derlenecek · ✗ bozuk · ⚠. `—` chip'i kalkar
(atlanmak bir durum değil). Chip'ler filtre olduğu için filtreler de duruma göre olur. Koşunun kendi tablosu
(`Completed — 3 failed · 24 succeeded · 9 skipped`) ribbon'da ve akışın kapanış satırında aynen kalır.

## 5. Akışlar — bugün → olmalı

| Akış | Bugün | Olmalı |
|---|---|---|
| Açılış | Repo yüklenir, halka; kullanıcı Sync'e basar | Sync kendiliğinden; ilk kare boş, sonra renkler |
| Sync | fetch → ana ağaç → halka | fetch → build tree HEAD'e getirilir → **o ağaç** taranır → yeşil / gri / kırmızı. Konsol: `build tree D:\…\OSYS-Build @ a3f81c2 · 3 behind origin/developer` |
| Build | Herkes griye iner, dalga, koşu, sonuç | Kimse inmez; dalga gri + kırmızıları amber'a yakar, yeşiller yeşil; sonuç toplama eklenir |
| Tekrar Build | Yine herkes gri | Kapsam yalnız kırmızılar + arada değişenler |
| Satırdan Build / Rebuild / Clean | Diğer satırlar griye iner | Yalnız hedef amber; diğerleri dokunulmaz. Clean → `never built` gri |
| Resolve cycles | Üyeler amber → sonuç; küp sonuç rengi | Aynı; küp amber kalır, çerçeve yeşil / kırmızı |
| Rebuild | Herkes amber → sonuç | Aynı (döngü dışı herkes zaten kapsam) |
| Commit | Hiçbir şey olmaz; sonraki Sync'e kadar | HEAD değişir → Sync kendiliğinden → commit'lenen proje gri → Build → yeşil. (Bir "fazladan" derleme: havuz commit'le birebir olur) |
| Checkout | "Sync required", bekler | Sync kendiliğinden; build tree yeni HEAD'e; içeriği aynı olan yeşil kalır, değişen gri, aynı imzada patlamış kırmızı |
| `N behind` → pull | ff-only, ardından Sync | Aynen (yalnız ana ağaç, yalnız checkout edilmiş branch) |
| Kırmızıyı düzeltme | — | Satırdan *Open in Visual Studio* → düzelt → VS'de derle (havuz düzelir) → **commit** → Sync (gri) → Build ya da satırdan Build → yeşil |
| VS'den derleme | Görünmez | Görünmez ve gerek yok: aracın yeşili "commit derlendi" der, VS'nin DLL'i developer'ın kendi işi; commit anında yakalanır |
| Clean / Optimize (bakım) | Plan yüzeyi boşalır, Sync zincirlenir | Aynen; zincirlenen Sync renkleri koyar (Clean sonrası hepsi gri) |

## 6. Animasyonlar — ne aynı, ne değişir

| Parça | Karar |
|---|---|
| Nötr an (440 ms) | **Aynen**, ama artık "griye inme" değil, dalgadan önceki bekleme anı. Süreye dokunulmaz |
| Rastgele dalga (110 ms/node, ≤ 1,1 s) | **Aynen**; kapsam gri/kırmızıdan amber'a geçer, aynı 200 ms renk geçişi |
| Veda (0,18), sıralı teslim (0,13), koşu opaklıkları | **Aynen** — hepsi opaklık, renkten bağımsız |
| Building nefes, bead orbit, failed shake | **Aynen** |
| Neon final | **Aynen**; "kalan griler birlikte gelir" → "kalan herkes kendi renginde gelir". Kod zaten opaklık çalıştırıyor; değişen yalnız cümle |
| Sync reveal | Yapı aynıysa **yerinde tazeleme** (satırlar kalır, renkler değişir, scroll durur); yapı değiştiyse bugünkü reveal. Gerekçe: Sync artık her commit'te kendiliğinden koşacak; her seferinde "sıfırdan listeleme" gürültü olur. Yapısal imza zaten var |
| Satırda opaklık | Yok (v1.13.2), aynen |

## 7. Branch ve build tree — kurallar

| Konu | Kural |
|---|---|
| Konum | `<repoRoot>-Build`, repo klasörünün yanında; ayar yok, türetilir; Ayarlar → Workspace salt-okur gösterir. **`D:\Projects\Delta\OSYS-Build` zaten var** (8 Eylül'de açılmış detached worktree) — devralınır, konsola bir kez "adopting existing worktree … it is reset on every Sync" yazılır |
| Hangi commit | Checkout edilmiş branch'in HEAD'i. Uncommitted değişiklik asla girmez |
| Ne zaman | Her Sync'te: `worktree list` → var ve detached → `reset --hard <sha>`; yok → `worktree add --detach`; klasör var ama bu reponun worktree'si değil ya da attached → Sync durur, konsol nedenini ve çözümü söyler |
| Ana ağaç | Dokunulmaz. `N behind` ff-only tek istisna, aynen |
| `obj` | `<repoRoot>-Build\_obj\<hash(projectId)>` — kimlik ana kök yolu; branch'ler arası sıcak |
| Eski havuz | İlk Sync'te `%LOCALAPPDATA%\…\worktrees\*` `worktree remove --force`; her biri konsola |
| HEAD izleyici | `.git\logs\HEAD` (reflog) dosya izleyicisi + 1-2 s sessizlik → Sync. Checkout, commit, pull, reset hepsi oraya yazar; build tree'nin kendi HEAD'i ayrı dosyada olduğu için aracın reset'i tetiklemez. Koşu sırasında kuyruğa alınır, koşu bitince koşar; Sync sürerken gelen ikinci istek tekle birleşir |
| Branch seçimi | **Yalnız izle.** Havuz tek olduğu için çalıştığın branch dışında bir branch'i havuza derlemek, havuzu senin kaynağından ayırır — çözmeye çalıştığımız sorunu üretir. Popover kalkar; chip checkout edilmiş branch'i ve `N behind`'ı gösterir. (Havuzda `version_v79.x` worktree'leri var — o ihtiyaç gerçekten varsa: **pin** açık bir istisna olarak kalır; chip "pinned · checked out: feature/x" yazar, ilk satır "● Checked-out branch" ile kalkar. Karar §11-1) |
| Harici kökler | Değişmez |
| Uyarı | Build tree elle düzenlenirse her Sync siler; köke `BUILD-TREE-DO-NOT-EDIT.txt` |
| Test dikişi | Supervisor `--build-tree <yol>` (bugünkü `--worktrees` gibi); acceptance testleri gerçek `OSYS-Build`'e dokunmaz |

## 8. UI — ne değişir

- **Liste:** aynı satır anatomisi. Renk kalıcı ve durum anlamlı; başlangıç modu yalnız "bilinmiyor"; yuva
  134 px; ▲ kümülatif; `—` glyph'i yalnız koşu içinde.
- **Graf:** çerçeve durum rengi; küp döngüde hep amber; koreografiler aynen.
- **Ribbon:** Sync satırı build tree ve commit'i söyler; koşu tablosu aynen.
- **Action bar:** worktree chip'i kalkar; branch chip'i salt-okur (ya da pinned işaretli); `N behind` aynen;
  sayaç chip'leri durum sayar; Build split, bakım kutusu, cfg, perf aynen.
- **Ayarlar → Workspace:** repo kökü + türetilen build tree yolu (salt-okur).
- **Konsol / akış:** Sync transkriptine worktree satırları (Build planlamasıyla aynı metinler, tek kaynak).

## 9. Bedeller — açıkça

1. **Commit alışkanlığı.** Kırmızıyı VS'de düzeltip aracın yeşil görmesi için **commit gerekir**; aksi hâlde
   satır kırmızı kalır — havuz doğru olsa bile. Bu, "araç uncommitted'ı derlemez"in doğrudan sonucu ve senin
   verdiğin karar. Alternatifi (satırdan in-place derleme) bilerek yok (§10).
2. **Aracın Build'i dirty projeni ezebilir.** Pull, P'nin commit'ini değiştirdiyse araç P'yi commit'ten derler;
   senin VS'de derlediğin dirty `P.dll` havuzdan gider. Sıra: **önce Build, sonra kendi projelerini VS'de derle.**
   Bugün in-place modda bu yoktu; tek ağaçta var ve kabul edilen bedel bu.
3. **Havuz tek.** Başka branch derlemek (pin) çalıştığın branch'in DLL'lerini ezer — bugün de öyle; bu yüzden
   varsayılan "izle".
4. **VS'den derleme araca görünmez.** Sebep teknik ve kalıcı: havuzdaki bir DLL'in hangi kaynaktan çıktığı
   bilinemez; timestamp heuristiği tam da önemli durumda (branch değişimi, dirty build) yanıltır. Karşılığı: commit
   anında Sync yakalar, araç bir kez daha derler — fazladan ama doğru.
5. **Sync biraz pahalanır:** `reset --hard` (aynı commit'te saniyenin altı; branch değişiminde birkaç saniye;
   ilk oluşturma büyük repoda onlarca saniye, bir kez).
6. **Tasarım paketi §8 kararı tersine dönüyor** ("v1.11.0: renk yalnız son işlemin hikâyesini anlatır — tekrar
   önerme"). Meşru: tek kanal ilkesi kalıyor, anlamı değişiyor. Ama paketsiz başlamamalı — testler "design vX §Y"
   ile pinli. Küçük bir v1.20.0: §2.3 renk kuralı, §2.4 etiket, §5 statü tablosu, §8, §9.

## 10. Bilerek eklenmeyenler

- **Sync sonrası otomatik Build.** Havuz paylaşımlı; VS derlemesinin ortasında araç DLL yazarsa sürpriz olur.
  Build senin elinde kalır.
- **"Mark as built"** (VS'de derledim, yeşil say). Yalan biriktirir: commit'lenmiş kaynak için "derlendi" yazar,
  havuzdaki DLL ise dirty derlemedir.
- **Satırdan "working tree'den derle".** In-place'in geri gelmesi; ölü yol + üç durum matrisi geri döner. VS bu
  iş için var.
- **DLL timestamp okuma.** §9-4.
- **Branch başına havuz.** Tek kazancı sıcak `obj`'di; değişen proje zaten baştan derleniyor.

## 11. Kararlar — senin

| # | Soru | Önerim |
|---|---|---|
| 1 | Branch seçimi tamamen kalksın mı (yalnız izle), yoksa pin istisna olarak kalsın mı? | **Kalksın.** Pin'i yalnız `version_v79.x`'i gerçekten havuza derliyorsan tut |
| 2 | Sync sonrası kanıtlı kırmızı var mı? | **Evet** |
| 3 | `failed · 2h` mi, `failed` mi? | `failed · 2h` |
| 4 | HEAD izleyici Faz 2'de mi? | **Evet** — "izle"yi otomatik yapan parça bu |
| 5 | Sync reveal'ı yapısal imzaya bağlansın mı? | **Evet** |
| 6 | Design v1.20.0 paketi mi, bu rapor spec mi? | **Paket** (küçük) |
| 7 | `OSYS-Build` devralınsın mı; içinde korunacak bir şey var mı? | Devralınsın |
| 8 | Sıra | **Faz 1 → Faz 2** |

## 12. Fazlar ve süreç

**Faz 1 — Renk modeli (App + küçük Core/Supervisor).** Defterde hata imzası; karar sırasında "kanıtlı kırmızı"
en önde; `VisualStatus` durum + koşu bindirmesi; nötrleme kalkar; ▲ kümülatif; küp hep amber; etiketler ve 134 px;
sayaçlar/filtreler; testler yeniden pinlenir; ARCHITECTURE §7.4-7.5, §13.2, §13.6, §14.3, §16 + README + design
v1.20.0. Bugünkü in-place Sync'le bile senin ilk isteğini verir: Sync'ten sonra renkli, toplamı gösteren liste.

**Faz 2 — Tek ağaç ve izle (Core + Supervisor + App).** Planlama hattı tek yerde (Sync ve Build aynı hattı
çağırır); `WorktreeManager` tek ağaç; komutlardan worktree alanları düşer, `syncCompleted` build tree'yi taşır;
worktree chip/popover kalkar; branch izle (+ pin karar 1'e göre); açılışta ve HEAD değişiminde otomatik Sync;
reveal kapısı; eski havuz temizliği; `--build-tree` test dikişi; kaynak guard'ları yeniden pinlenir;
ARCHITECTURE §8.6-8.7, §10.2-10.4, §12-13 action bar, §16, §20 + README "Branch / worktree" + CLAUDE.md'nin
`reset` cümlesi (bugün de havuz worktree'sinde `reset --hard` koşuyor; cümle "ana repoda" diye netleşmeli).

Süreç: §11 kararları → design v1.20.0 → spec (`.claude/outputs/…-design.md`) → `writing-plans` → kırmızı
test kuralıyla task-by-task. Kod yazılmadı.
