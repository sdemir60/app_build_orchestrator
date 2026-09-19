# Sync / Build akışı — nihai spec ve kaçak analizi

Tarih: 2026-09-18 · Durum: **bağlayıcı spec**, kodda değişiklik YOK · `main` = `ea3ab62` · çatı branch: `sync-build-flow`
Önceki dosyalar tarihseldir (v1 `…-20-36`, v2 `…-05-39`, karar `…-06-27`, taslak `…-07-37`); bu dosya hepsinin
yerine geçer. Kod kanıtları `2026-09-17-19-41-cumulative-status-analysis-verification.md`. Uygulama planı:
`2026-09-18-07-56-sync-build-flow-phase1-plan.md`.

## 0. İki terim

**Havuz:** derlenen DLL'lerin toplandığı ortak klasör. OSYS'de `C:\OSYS\Client\bin` ve `C:\OSYS\Server\bin`. Her
projenin csproj'u derleme sonrası DLL'ini oraya kopyalar, her projenin referansı (HintPath) oradan okur. VS de araç
da aynı havuza yazar, aynı havuzdan okur. Araç bu klasörün adını bilmez; referansların gösterdiği yerden öğrenir.

**Defter:** aracın kendi not dosyası, `build-state.json`. Araç bir projeyi kendisi derleyince yazar: hangi proje,
o anki kaynak içeriğinin hash'i, ne zaman, sonuç. Başka hiçbir şey yazmaz.

## 1. Kararlar

| # | Konu | Karar |
|---|---|---|
| 1 | Çalışma ağacı | **Ana proje.** Worktree yok. Araç kullanıcının çalışma ağacında derler; Sync de Build de aynı ağacı görür |
| 2 | Renk | Çıktının durumu: yeşil güncel · gri derlenecek · kırmızı kanıtlı bozuk · boş bilinmiyor. Kümülatif |
| 3 | Karar terimi | Kaynak dosyaların içerik hash'i (bugünkü). Defter yalnız aracın kendi derlediğini yazar |
| 4 | Dışarıdan derleme | **Kredi verilir** (§5). Çıktı bütün girdilerinden yeniyse proje güncel, kim derlemiş olursa olsun |
| 5 | Defterin sınırı | **Defter yalnız kendi ürettiği çıktı için konuşur** (§5). Çıktı defterden yeniyse zaman damgası karar verir |
| 6 | Çıktı yoksa | Kanıt dosyası yoksa proje güncel olamaz: `never built` |
| 7 | Branch takibi | Anlık: yalnız `.git\logs\HEAD` izlenir; checkout, commit, pull, reset görülünce Sync kendiliğinden (§6.1) |
| 8 | Araçtan branch | Branch chip'inden seçim gerçek `git checkout`; yalnız tıklamayla (§6.3) |
| 9 | Kirli ağaç | Ayar (Settings → General): *Stop* varsayılan · *Stash and switch*. Stash mesajı aracı ve branch'i adlandırır, konsola satır; geri uygulama kullanıcının (§6.3) |
| 10 | Koşu sırasında checkout/pull/reset | Nazik durdurma; değişim anında derlenmekte olanların sonucu deftere yazılmaz; koşu bitince Sync. Commit durdurmaz (§6.1) |
| 11 | Pencereye dönüş | Sessiz Sync; son Sync'ten 5 s geçmediyse tekrar etmez (§6.2) |
| 12 | Çökme | Koşu uçuştayken motor ölürse, sonraki açılışta uçuştaki projeler defterde geçersizlenir (§5.5) |
| 13 | Sync reveal | Yapı aynıysa yerinde tazeleme; değiştiyse bugünkü reveal |
| 14 | Kırmızı | Kanıt: hata anındaki imza tutulur, bugünkü imza eşitse kırmızı. Yalnız derleyici hatası (exit ≠ 0) kanıttır; timeout gri `never built` |
| 15 | Üçgen | Kümülatif: defterdeki bağımlılık notu durdukça durur |
| 16 | Küp | Döngü üyesinde her zaman amber; çerçeve durumu taşır |
| 17 | Etiketler | `up to date · 2h` · `modified` · `modified · local` · `affected` · `never built` · `failed · 2h` · boş. Yuva 134 px |
| 18 | Sayaçlar | Durumu sayar: Σ · building · ✓ güncel · ○ derlenecek · ✗ bozuk · ⚠. Atlandı chip'i kalkar |
| 19 | Animasyonlar | Değişmez |
| 20 | `C:\OSYS` | Koda gömülü yol yok; havuz HintPath'lerden türer. Enum üyesi `ExternalOsysPlatform` ürün-bağımsız ada döner |
| 21 | Konsol bölümleri | Konsol yalnız kullanıcının başlattığı işlemde ya da branch değişiminde temizlenir; kendiliğinden Sync sessizdir (§6.2) |
| 22 | Git işlemi yarıdayken | Kendiliğinden Sync bekler; araçtaki git düğmeleri kilitlenir; Build uyarıyla serbest; kilit dosyası silinmez (§6.4) |
| 23 | Pull | Aynen; kirli ağaçta uyarı verir ve yapmaz; stash ayarı pull'a uygulanmaz (§6.5) |
| 24 | Sıra | Faz 1 renk modeli → Faz 2 tek ağaç ve branch → Faz 3 dışarıdan derleme kredisi |

## 2. Model

Araç, kullanıcının çalışma ağacındaki kaynağı derler ve havuzu o kaynakla tutarlı tutar. Karar içerik hash'inden
verilir. VS de araç da aynı dosyaları okuduğu için ikisi asla farklı dünyaları derlemez; kullanıcının commit'lemediği
değişiklik de aracın gözünde sıradan içeriktir. Havuzdaki bir çıktının aracın mı yoksa başkasının mı ürünü olduğu
zaman damgasından anlaşılır ve iki durum farklı kurallarla değerlendirilir (§5).

## 3. Akış — eskiden ve şimdi

| Ne yaptığımda | Eskiden | Şimdi |
|---|---|---|
| Sync | Her satır renksiz gri | Yeşil / gri / kırmızı; döngü küpü amber |
| Build | Herkes griye iner, kapsam sarıya yanar, yalnız o koşu boyanır | Kimse inmez; dalga gri ve kırmızıları yakar; sonuç toplama eklenir |
| Tekrar Build | Yine herkes gri | Kapsam yalnız kırmızılar ve arada değişenler |
| Satırdan Build / Rebuild / Clean | Hedef dışı satırlar griye iner | Yalnız hedef; Clean → `never built` |
| Resolve cycles | Kapsam dışı griye iner | Kapsam dışı değişmez; küp amber kalır |
| Tekrar Sync | Sonuçlar silinir | Yeniden hesaplanır, silinmez |
| VS'de dosya değiştirip derledim | Görünmez | Pencereye dönünce sessiz Sync; çıktı taze ise **yeşil**, değilse `modified` |
| Pencereye döndüm | Hiçbir şey | Sessiz Sync; konsol ve liste yerinde; değişiklik varsa akışa tek satır |
| Branch'i VS'den değiştirdim | Araç fark etmez | Yeni bölüm: konsol temizlenir, birkaç saniyede Sync; içeriği aynı projeler yeşil kalır |
| Branch'i araçtan değiştirdim | Yalnız worktree'de derlerdi | Gerçek checkout ve yeni bölüm; ağaç kirliyse ayar karar verir |
| Commit ettim | Hiçbir şey | Sessiz Sync; içerik aynı, yalnız `local` kuyrukları düşer; akışa tek satır |
| Pull (`N behind`) | ff-only, ardından Sync | Aynen; kirli ağaçta uyarı verir ve yapmaz |
| Merge çakışması / yarıda rebase | Sync'e basılırsa yarım ağaç taranır | Kendiliğinden Sync bekler, branch chip'inde amber nokta; bitince Sync |

## 4. Durum modeli ve etiketler

| Durum | Renk | Glyph | Etiket | Kaynak |
|---|---|---|---|---|
| Bilinmiyor | başlangıç modu | kesikli daire | boş | Sync yok / imza hesaplanamadı |
| Güncel | yeşil | ✓ | `up to date · 2h` | defter kipi: imza eşit · zaman kipi: kanıt taze |
| Güncel, bekliyor | yeşil + ▲ | ✓ | `up to date · 2h` | defter notu: hatalı bağımlılığa karşı derlendi |
| Derlenecek | gri | kesikli daire | `modified` / `modified · local` / `affected` / `never built` | içerik değişti / kanıt eski / kayıt ya da çıktı yok |
| Bozuk | kırmızı | ✗ | `failed · 2h` | hata imzası = bugünkü imza |
| Kuyrukta / derleniyor | amber | saat / halka | değişmez | yalnız koşu içinde |

`local`: projenin girdilerinden en az biri `git status`'ta kirli. Zaman kipinde `modified` = kendi girdisi çıktıdan
yeni, `affected` = yalnız bağımlılığının çıktısı yeni. Kalkan etiketler: `failed · retry`, üç parçalı
`affected · up to date · 2h`. Tooltip'ler: Up to date — last built 2h ago · Up to date — built outside this tool 5m
ago · Its own files changed since the last build · Its own files are newer than its build output · Its own files are
unchanged — a dependency changed · No build output known to this tool · Failed at this source 2h ago — Build will
retry it (döngü üyesinde: Resolve cycles will retry it).

## 5. Kanıt ve iki kip

### 5.1 Kanıt dosyaları

- **Derleme kanıtı:** projenin kendi çıktısı, `<OutputPath(cfg)>\<AssemblyName>.<dll|exe>`. `OutputPath` csproj'un
  configuration koşullu grubundan okunur, yoksa `bin\<cfg>\`; uzantı `OutputType`'tan. SDK-style / multi-target
  projede yol güvenle türetilemiyorsa **kanıt yok** demektir: proje defter kipinde kalır, kredi ve çıktı-yok vetosu
  uygulanmaz.
- **Kopya kanıtı ("beslenen çıktılar"):** projeye HintPath ile bağlanan projelerin o HintPath'lerinin gösterdiği
  dosyalar. Hangi hedeflerin bu projenin derlemesiyle gerçekten güncellendiği **aracın kendi derlemesinden
  öğrenilir**: başarılı derlemeden sonra hedef dosya var, boyutu derleme kanıtıyla eşit ve zamanı ona eşitse (2 s
  içinde) "beslenen" sayılır ve deftere yazılır (`FedOutputs`). Araç henüz derlememişse liste boştur ve yalnız derleme
  kanıtı konuşur. Repo içine check-in edilmiş bir kütüphane kopyası hiç beslenmeyeceği için hiç denetlenmez; sonsuz
  "bayat" döngüsü oluşmaz.
- **Girdiler:** bugünkü girdi kümesi (csproj, Compile/Resource öğeleri, klasör taraması, Directory.Build.*) **artı
  taranan klasörlerin kendisi** (silme ve yeniden adlandırma klasörün zamanını ilerletir) **artı projenin kendi
  HintPath hedefleri** (bağımlılık çıktıları, üçüncü parti dahil; olmayan dosya yok sayılır).

### 5.2 Çıktı kimin

Derleme kanıtının zamanı, defterdeki son başarılı aracın derlemesinden (`LastRunAt`) yeniyse ya da kayıt yoksa
çıktı **başkasının**dır: zaman kipi. Değilse çıktı **aracın**dır: defter kipi. Aracın kendi çıktısı her zaman
`LastRunAt`'tan eskidir, çünkü defter MSBuild bittikten sonra yazılır ve kopya komutu kaynak dosyanın zamanını korur.

### 5.3 Defter kipi

Bugünkü kural: bugünkü imza = defterdeki `BuiltSignature` ⇒ güncel; hata imzası = bugünkü ⇒ bozuk; aksi bayat.
Zaman damgası **veto etmez**: içerik aynıysa dosyaya dokunup geri almak derletmez, branch'ler arasında derlemeden
gidip gelmek derletmez. İki ek koşul: derleme kanıtı yoksa `never built`; beslenen bir çıktı yoksa, boyutu derleme
kanıtından farklıysa ya da ondan eskiyse bayat (`affected`, tooltip "its output in the shared folder was replaced").

### 5.4 Zaman kipi

Güncel ⇔ derleme kanıtı var ∧ zamanı bütün girdilerden (dosya, klasör, HintPath hedefi) yeni ya da eşit ∧ her
beslenen çıktı var, boyutu eşit, zamanı derleme kanıtından yeni ya da eşit. Yeşil; yaş derleme kanıtının
zamanından; tooltip "built outside this tool". Deftere yazılmaz; her Sync yeniden kanıtlar. Bayatsa: kendi girdisi
yeniyse `modified`, yalnız HintPath hedefi yeniyse `affected`, kanıt yoksa `never built`. Kırmızı zaman kipinde
yoktur (kanıt yok). Bağımlı projeler her zaman imzayla değerlendirilir.

### 5.5 Çökme kurtarma

Supervisor dispatch anında `run-inflight.json`'a projeyi yazar, tamamlanınca siler. Açılışta dosya doluysa listedeki
her proje için defter `LastResult=Failed, LastRunAt=şimdi` ile güncellenir (imza korunur, hata imzası yazılmaz),
konsola "previous run was interrupted; N projects will rebuild" düşer. Böylece yarım yazılmış taze bir çıktı zaman
kipine giremez: derleme kanıtı `LastRunAt`'tan eski kalır, defter kipi `LastResult ≠ Succeeded` der, satır gri
`never built`.

### 5.6 Döngü üyeleri

Grup olarak: üyelerden herhangi birinin çıktısı başkasınınsa grup zaman kipindedir; grubun güncel olması için her
üyenin kanıtı taze olmalı. Aksi hâlde grup bayat.

## 6. Branch, izleyici ve konsol

### 6.1 İzleyici

`.git` bir dosyaysa (worktree / submodule) `git rev-parse --git-dir` ile gerçek dizin bulunur. Yalnız `logs\HEAD`
izlenir (Windows dosya bildirimi; yoklama yok, boştayken iş yok); repo, kaynak dosyalar, `bin`/`obj` izlenmez. Git'in
bir işlemde yaptığı ardışık yazımlar 1,5 s sessizlikle tek tetiğe iner; uzun bir rebase tek Sync üretir. VS'nin arka
plan `fetch` ve `status` işlemleri bu dosyaya yazmaz, tetiklemez. Reflog satırı işlemi söyler.

- **Çift Sync yok:** tetik geldiğinde HEAD commit'i ve branch adı son Sync'tekiyle aynıysa atlanır. Aracın kendi
  checkout'u ve pull'un zincirli Sync'i böylece tek Sync kalır.
- **Koşu sırasında:** checkout / pull / merge / reset / rebase → nazik durdurma (yeni proje başlamaz, derlenenler
  biter, değişim anında derlenmekte olanların sonucu **deftere yazılmaz**, olay akışına "interrupted by branch
  change"), koşu bitince Sync; commit → hiçbir şey. Güvenlik ağı: koşu bitince plan anındaki commit ile şimdiki
  farklıysa Sync.
- **İzleyici çalışamazsa** (ağ sürücüsü, izin): konsola tek satır; pencereye dönüş Sync'i güvenlik ağı olarak kalır.

### 6.2 Kendiliğinden Sync ve konsol bölümleri

İlke: konsol bir **bölüm** anlatır. Yeni bölümü iki şey açar: kullanıcının başlattığı bir işlem, ya da listenin
altındaki dünyanın değişmesi (branch). Aynı dünyada kendiliğinden olan tazelemeler bölüm açmaz; olay akışına tek
satır düşer. **Kendiliğinden Sync** (commit, pencereye dönüş): konsolu ve olay akışını temizlemez, listeyi kaydırmaz,
reveal oynatmaz, fetch yapmaz; `N behind` son bilinen uzak duruma göre yerelde hesaplanır.

| Tetik | Konsol ve olay akışı | Liste ve graf | Fetch |
|---|---|---|---|
| Sync butonu | temizlenir, tam transkript | yapı aynıysa yerinde; proje eklendi/çıktıysa reveal | evet |
| Branch değişti (araçtan ya da dışarıdan) | temizlenir; ilk satır yeni branch, ardından kısa Sync transkripti | aynı kural | hayır |
| Build / Rebuild / satırdan / Resolve | tıklamada temizlenir (bugünkü) | renkler kalır | hayır |
| Clean / Optimize | tıklamada temizlenir; zincirlenen Sync altına eklenir (bugünkü) | boşalır, dolar (bugünkü) | evet (bugünkü) |
| Pull | korunur; pull sonucu ve Sync altına eklenir (bugünkü) | yerinde | evet |
| Commit | dokunulmaz; akışa tek satır ("synced after commit") | yerinde | hayır |
| Pencereye dönüş (5 s eşiği) | dokunulmaz; bir şey değiştiyse akışa tek satır ("synced · N projects changed") | yerinde | hayır |
| Uygulama açılışı | ilk bölüm, tam transkript | reveal | evet |

Kurallar:

- **Temizlik önce, not sonra.** Araçtan branch değişiminde stash ve checkout satırları temizlikten SONRA yazılır;
  yeni bölümün ilk satırları onlardır.
- **Başarısız işlem bölüm açmaz.** Kirli ağaç yüzünden reddedilen checkout konsolu temizlemez; uyarı altına eklenir.
- **Koşu sırasında branch değişirse** koşu nazikçe durur, sonra yeni bölüm açılır ve ilk satırı koşunun özetidir:
  kaç proje bitti, kaçı yarıda kaldı, run log klasörünün yolu.
- **Diskteki koşu logları hiçbir durumda silinmez;** temizlenen yalnız ekran.
- **Liste ve grafın boşalıp dolması** (bugün her Sync'te, `ClearPlanSurface`) yalnız yapısal imza değiştiğinde olur;
  Clean ve Optimize'ın tıklamadaki boşaltması aynen kalır.

### 6.3 Araçtan branch değiştirme

Branch chip'i menüsünden seçim `git checkout <branch>` (uzak branch için izleyen yerel branch oluşur). Koşu
sırasında ve git işlemi yarıdayken kilitli. Kirli ağaçta ayara göre (Settings → General, *Stop* varsayılan):
*Stop* → konsola "N files have uncommitted changes — commit or stash them first", hiçbir şey yapılmaz, konsol
temizlenmez; *Stash and switch* → `git stash push -u -m "build-orchestrator: leaving <branch> for <target>"`, yeni
bölüm, konsola stash satırı, checkout. Geri uygulama kullanıcının; araç stash'i ne gösterir ne geri uygular.

### 6.4 Git işlemi yarıdayken

Git dizinindeki işaretler: `index.lock` (bir git komutu şu an çalışıyor) · `MERGE_HEAD` (merge çakışma çözümü
bekliyor) · `rebase-merge\` / `rebase-apply\` (rebase yarıda) · `CHERRY_PICK_HEAD` / `REVERT_HEAD` (cherry-pick /
revert yarıda).

- **Kendiliğinden Sync bekler.** Akışa bir kez tek satır; branch chip'inde amber nokta, tooltip "Merge in progress
  — finish or abort it in git" (metin işleme göre).
- **Devam:** işaret durduğu sürece 2 s'de bir yalnız bu dosyaların varlığı kontrol edilir; bu, yalnız bu durumda
  çalışan tek yoklamadır. İşaret kaybolunca Sync.
- **Takılı kilit:** `index.lock` 30 s sürerse konsola "git's index.lock has been there for 30 s — a git process may
  have crashed; delete it only if no git command is running". Araç kilidi **silmez**.
- **Araçtaki git düğmeleri** (checkout, pull) kilitli; tooltip nedeni söyler.
- **Build engellenmez;** planlamanın başında tek uyarı satırı: "a merge is in progress — files with conflict markers
  will not compile".
- **Sync butonu** her zaman çalışır ve ağacın yarım olduğunu tek satırla söyler.

### 6.5 Pull

`N behind` chip'i ve ff-only pull aynen. Kirli ağaçta uyarı verir ve **yapmaz** (bugünkü: "uncommitted changes …
commit or stash them first"). Stash ayarı pull'a **uygulanmaz**: pull'da branch'te kalınır, geri uygulama olmadığı
için stash'lenen değişiklikler kaybolmuş gibi görünürdü. Koşu sırasında kilitli (bugün de, `CanPullRepository`),
git işlemi yarıdayken kilitli. Worktree kalktığı için chip, geride olunan her an görünür (`CanShowBehind`'deki
worktree koşulu düşer).

### 6.6 Mutasyon yüzeyi

Tek dosyada kalır ve kaynak guard'ı onu pinler: ff-only pull, checkout, stash push. CLAUDE.md "iki istisna"yı
"üç istisna" yapar; havuz worktree'si kalktığı için "reset hiçbir akışta çalıştırılmaz" cümlesi harfiyen doğru olur.

## 7. Kaçak analizi

Her satır: senaryo → araç ne görür → hangi kural yakalar. "✓" = doğru karar, "○" = fazladan derleme (güvenli yön),
"✗" = bilinen sınır.

| # | Senaryo | Sonuç | Kural |
|---|---|---|---|
| 1 | Araçla derledim, dokunmadım | ✓ yeşil | defter kipi, imza eşit |
| 2 | Dosyayı değiştirdim, derlemedim | ✓ gri `modified` | defter kipi, imza farklı |
| 3 | Değiştirdim, VS'de derledim, dönünce Sync | ✓ yeşil "built outside" | zaman kipi: çıktı defterden ve girdilerden yeni |
| 4 | Değiştirmeden VS'de derledim | ✓ yeşil | zaman kipi, kanıt taze; içerik zaten aynı |
| 5 | Değiştirdim, VS'de derledim, **geri aldım**, derlemedim | ✓ gri `modified` | zaman kipi: geri alınan dosya çıktıdan yeni |
| 6 | Değiştirdim, VS'de derledim, geri aldım, sonra branch değiştirdim (proje aynı) | ✓ gri | 5 ile aynı; git aynı dosyaya dokunmaz, saat kalır |
| 7 | Aynısı, projede branch'ler farklı | ✓ gri | içerik değişti; hem imza hem zaman bayat der |
| 8 | Değiştirip VS'de derledim, geri almadan branch değiştirdim (git değişikliği taşıdı) | ✓ yeşil | içerik B, çıktı B'den, zaman kipi taze |
| 9 | Dosyayı değiştirip geri aldım, hiç derlemedim | ✓ yeşil, derleme yok | defter kipi: imza aynı, zaman veto etmez |
| 10 | Branch'e gidip derlemeden geri geldim | ✓ yeşil, derleme yok | defter kipi: içerik aynı |
| 11 | Branch'e gidip derledim, geri geldim | ✓ farklı projeler gri, aynı olanlar yeşil | içerik hash'i; havuz o branch'e dönmüştü, yeniden derlenir |
| 12 | Commit ettim | ✓ değişmez | içerik aynı |
| 13 | Pull yaptım | ✓ değişen projeler gri | dosyalar yeniden yazıldı, imza farklı; izleyici Sync koşar |
| 14 | Stash / stash pop | ✓ | içerik değişti; HEAD oynamadı → pencere Sync'i ya da Build'in yeniden planlaması yakalar |
| 15 | VS'de derleme patladı | ✓ gri `modified` | csc çıktı üretmez, eski çıktı girdiden eski |
| 16 | VS'de derleme geçti, havuza kopya patladı | ✓ gri `affected` | beslenen çıktı derleme kanıtından eski |
| 17 | VS'de Release derledim, araç Debug'da | ✓ gri | derleme kanıtı `bin\Debug` eski; havuzdaki Release DLL beslenen çıktı denetiminde boyut/zaman uyuşmaz |
| 18 | Araç Debug derledi, sonra VS Release derledi | ✓ gri `affected` | defter kipi; beslenen çıktı derleme kanıtından yeni ve boyutu farklı → bayat; Build havuzu Debug'a döndürür |
| 19 | Havuza elle DLL kopyaladım | ✗ boyut aynıysa taze görünebilir | hash önbelleğinin mtime varsayımıyla aynı sınıf; boyut farklıysa yakalanır |
| 20 | Dosyayı eski tarihiyle geri getirdim (yedekten) | ✗ | git ve VS bunu yapmaz; hash önbelleği de aynı varsayımda |
| 21 | Yeni `.cs` ekledim, csproj'a yazdım | ✓ gri | csproj ve klasör zamanı yeni |
| 22 | `.cs` sildim (csproj'dan da) | ✓ gri | csproj zamanı; zaman kipinde klasör zamanı da |
| 23 | Bağımlılık D değişti ve derlendi, P'ye dokunmadım | ✓ P gri `affected` | defter kipi: P'nin imzası D'nin imzasını içerir; zaman kipi: D'nin HintPath hedefi P'nin çıktısından yeni |
| 24 | D VS'de yeniden derlendi, içeriği aynı | ✓ P yeşil | imza aynı; P'nin çıktısı D'den eski olsa da defter kipi veto etmez, D aynı içerik |
| 25 | Üçüncü parti paket güncellendi | ✓ gri | csproj HintPath sürüm yolu değişir; zaman kipinde paket DLL'i girdi |
| 26 | Araç derlerken dosya değiştirdim (proje henüz derlenmedi) | ○ bir kez fazladan | defter plan anındaki hash'i yazar, Sync farkı görür |
| 27 | Araç derlerken dosya değiştirdim (proje o an derleniyor) | ○ | aynı; plan hash'i ≠ bugünkü |
| 28 | Araç derlerken VS'den branch değiştirdim | ✓ | nazik durdurma, uçuştakiler deftere yazılmaz, sonra Sync |
| 29 | Aynısı, checkout git tarafında dosya kilidine takıldı | ✓ | araç diskteki hâli hash'ler; kullanıcı checkout'u tekrarlar |
| 30 | Araç derlerken commit ettim | ✓ durmaz | commit dosyaları değiştirmez |
| 31 | Araç derlerken VS aynı projeyi derledi | ✗ kullanıcı kuralı | aynı obj; bugün de böyle |
| 32 | Motor derlerken çöktü / öldürüldü | ✓ gri `never built` | `run-inflight.json` kurtarması (§5.5) |
| 33 | MSBuild zaman aşımı | ✓ gri `never built` | `LastResult=Failed`, hata imzası yok, çıktı `LastRunAt`'tan eski |
| 34 | Derleyici hatası | ✓ kırmızı `failed · 2h` | hata imzası = bugünkü |
| 35 | Derleyici hatası sonra dosyaya dokundum | ✓ gri `modified` | imza değişti, kanıt düştü |
| 36 | Derleyici hatası, ortam değişti (paket restore), kaynak aynı | ✓ kırmızı kalır, Build yeniden dener | kırmızı engellemez |
| 37 | Bakım Clean | ✓ hepsi `never built` | çıktılar ve defter kayıtları silindi |
| 38 | Satırdan Clean | ✓ `never built` | çıktı silindi, kayıt silindi |
| 39 | Araç ilk kez kuruldu, VS her şeyi zaten derlemişti | ✓ çoğu yeşil "built outside" | kayıt yok → zaman kipi |
| 40 | Defter dosyası silindi / bozuk | ✓ 39 gibi | |
| 41 | Eski sürüm defteri (yeni alanlar yok) | ✓ | alanlar null çözülür, güvenli yön |
| 42 | Uygulama açıkken uzun süre bekledim, dışarıda çok şey oldu | ✓ | pencereye dönüş Sync'i |
| 43 | Döngü üyesi VS'de tek başına derlendi | ✓ grup bayat kalır | grup kuralı (§5.6): tüm üyelerin kanıtı taze değil |
| 44 | Aynı DLL adını iki proje üretiyor | ✓ | kenar düşer (bugünkü uyarı); beslenen çıktı öğrenilmez, yalnız derleme kanıtı |
| 45 | Harici kök projesi | ✓ | aynı kurallar; HEAD izleyicisi yalnız ana repo |
| 46 | İki uygulama örneği | ✓ | tek örnek zorunlu (bugün) |
| 47 | Sistem saati geri alındı | ✗ | kabul edilen sınır |
| 48 | Araçtan checkout yaptım | ✓ tek Sync | aracın Sync'i + izleyici tetiği; ikincisi HEAD aynı olduğu için atlanır (§6.1) |
| 49 | VS arka planda fetch / status yaptı | ✓ tetik yok | bu işlemler `logs\HEAD`'e yazmaz |
| 50 | Uzun bir rebase (çok commit) | ✓ tek Sync | 1,5 s sessizlik penceresi; işlem yarıda durursa §6.4 |
| 51 | Merge çakışması | ✓ Sync bekler | `MERGE_HEAD`; çözülünce ya da iptal edilince Sync |
| 52 | Rebase yarıda durdu | ✓ Sync bekler | `rebase-merge\` / `rebase-apply\` |
| 53 | Git çöktü, `index.lock` kaldı | ✓ uyarı, silinmez | 30 s eşiği (§6.4) |
| 54 | Koşu sırasında branch değişti | ✓ yeni bölüm, özet satırı | koşunun hikâyesi özetle taşınır; diskteki loglar durur (§6.2) |
| 55 | Repo ağ sürücüsünde, izleyici çalışmıyor | ✓ | tek satır uyarı; pencereye dönüş Sync'i yakalar |

Sınırlar 19, 20, 31, 47'dir; dördü de bugünkü modelin de sınırıdır ya da kullanıcı davranışıdır.

## 8. Ekrandan kalkanlar ve nedenleri

| Kalkan | Neden |
|---|---|
| Worktree chip'i ve popover'ı, açma/kapama anahtarı, hedef listesi, silme | Worktree yok |
| "Sync required" bekleme hâli, hollow satırlar | Sync kendiliğinden koşuyor |
| Sync sonrası başlangıç modu (halka, kesikli çerçeve) | Sync artık renk veriyor; başlangıç modu yalnız hiç Sync yokken |
| Her Sync'te liste ve grafın boşalıp baştan dolması | Sync kendiliğinden koşuyor; zıplamamalı. Yalnız proje eklenince ya da çıkınca |
| `N behind` chip'inin worktree modunda gizlenmesi | Worktree yok |
| `failed · retry` | "yeniden denenecek" sözünü sarı dalga zaten veriyor |
| `affected · up to date · 2h` | anlattığını kalıcı üçgen anlatıyor; yuva 134 px'e döner |
| Atlandı chip'i (—) | "atlanmak" bir durum değil; koşunun tablosu ribbon'da kalıyor |
| "Sync colours nothing" ve "renk yalnız son işlemin hikâyesi" cümleleri (README, ARCHITECTURE, tasarım §8) | ilke değişti |

Eklenenler: branch chip'i checkout düğmesi olur; branch chip'inde git işlemi göstergesi (amber nokta); Settings →
General'a kirli ağaç seçimi; konsola stash, durdurma ve koşu özeti satırları; olay akışına kendiliğinden Sync satırları.

## 9. Bilinmesi gerekenler

- Aynı projeyi aynı anda VS ve araçla derleme.
- Test için başka branch'e geçince havuz o branch'e döner; dönüşte Build'e basmadan VS'de derleme yapma.
- Kendiliğinden Sync fetch yapmaz; `N behind` son bilinen uzak duruma göredir, Sync butonu ve pull tazeler.
- Tasarım paketi §8 kararı tersine dönüyor; küçük bir tasarım sürümü (§2.3, §2.4, §5, §8, §9) gerekir.
- ARCHITECTURE §4 / §7.1 / §9.4'ün "çıktı okunmaz" cümlesi "aracın kendi çıktısı için defter, başkasının çıktısı
  için zaman damgası; çıktı hiçbir zaman güncel olduğunu tek başına kanıtlamaz" olarak yeniden yazılır (Faz 3).

## 10. Bilerek eklenmeyenler

Sync sonrası otomatik Build · "yeşil say" düğmesi · stash'in araçla geri uygulanması · dönüşte kendiliğinden stash pop
· stash ayarının pull'a uygulanması · aracın `index.lock` silmesi · worktree ya da branch başına havuz · "checkout
etmeden başka branch derle" (ihtiyaç kanıtlanırsa ayrı faz) · "dirty projeleri Build'den hariç tut" anahtarı.

## 11. Fazlar

**Faz 1 — Renk modeli.** Plan: `2026-09-18-07-56-sync-build-flow-phase1-plan.md`. In-place Sync ile bugün bile
çalışır; worktree kararından bağımsızdır.

**Faz 2 — Tek ağaç ve branch.** Worktree kodu ve UI'ı silinir; branch chip'i checkout düğmesi; kirli ağaç ayarı;
HEAD izleyici, çift Sync önleme ve koşu içi durdurma; sessiz Sync ve konsol bölümleri; git işlemi kapısı; pull
kapıları; açılışta ve pencereye dönüşte Sync; reveal kapısı; çökme kurtarma; guard ve doküman; CLAUDE.md. Kendi
dökümü Faz 1 bitince yazılır.

**Faz 3 — Dışarıdan derleme kredisi.** Değerlendiriciye `OutputPath`/`OutputType`; kanıt dosyaları; beslenen
çıktıların öğrenilmesi; iki kip; klasör ve HintPath girdileri; döngü grubu; enum adı; ARCHITECTURE ve README. Kendi
dökümü Faz 2 bitince yazılır.
