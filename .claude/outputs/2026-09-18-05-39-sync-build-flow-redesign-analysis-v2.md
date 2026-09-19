# Sync / Build akışı ve çalışma ağacı — özgün analiz v2: bugün ne, ne olmalı

Tarih: 2026-09-18 · Durum: yalnız analiz, kodda değişiklik YOK · `main` = `ea3ab62`
Bu dosya `2026-09-17-20-36-sync-build-flow-redesign-analysis.md`'nin yerine geçer (o dosya tarihsel kalır).

**v2'de değişen.** Karar teriminin ne olduğu netleşti: **kaynak dosyaların diskteki içeriğinin hash'i** — commit
değil, çıktı klasörü değil. v1'deki "commit gerekir" cümlesi karardan değil, *hangi ağacın tarandığından*
geliyordu; bu ayrım netleşince çalışma ağacı sorusu bir doğruluk sorusu olmaktan çıkıp saf bir **politika
sorusuna** dönüştü: havuz neyi yansıtsın — senin çalışma ağacını mı, branch'in HEAD'ini mi? §7 bu iki seçeneği
yan yana koyuyor ve önerim v1'den farklı. Renk modeli, kırmızının kuralı, üçgen, küp, etiketler, sayaçlar,
animasyonlar ve otomatik Sync bu seçimden bağımsızdır; onlar aynen duruyor. Kod kanıtları
`2026-09-17-19-41-cumulative-status-analysis-verification.md`'de.

## 0. Tek bakışta

| | Bugün | Olmalı |
|---|---|---|
| Karar terimi | Kaynak dosyaların içerik hash'i (csproj + Compile/Resource öğeleri + proje klasöründeki `.cs .xaml .resx .csproj .props .targets` + Directory.Build.*), configuration ve bağımlılık imzalarıyla birlikte tek SHA-256. Çıktıya ve commit'e bakılmaz | **Aynen.** Bu değişmez |
| Aracın işi | "Değişeni sırayla derle" | **`C:\OSYS\*\bin` havuzunu, taradığı ağaçla tutarlı tut.** Hangi ağaç: §7 (A: senin çalışma ağacın · B: branch HEAD'i) |
| Renk | Son işlemin hikâyesi; Sync boyamaz | **Çıktının durumu:** yeşil güncel · gri derlenecek · kırmızı kanıtlı bozuk · boş bilinmiyor |
| İşlem başlangıcı | Herkes griye iner | Kimse renk değiştirmez; dalga yalnız kapsamı yakar; sonuç toplama eklenir |
| Toplam | Görünmez | 50 yeşil + 2 gri → Build → 51 yeşil 1 kırmızı → satırdan düzelt → 52 yeşil |
| Sync'in gördüğü | Ana (kirli) ağaç; Build başka ağacı derleyebilir | Sync ve Build **aynı ağacı** görür — gördüğün = derlenecek |
| Çalışma ağacı | Üç mod, havuz, LRU, worktree chip'i | **Tek ağaç, mod yok, chip yok.** A: ana ağaç, worktree hiç yok · B: `<repo>-Build` |
| Branch | Niyet; otomatik Sync yok | **Checkout izlenir**; checkout, commit, pull'da ve açılışta Sync kendiliğinden |
| Kırmızı | "Son koşu hatalı" bayrağı | **Kanıt:** bu içerik derleyiciden geçmedi; içerik değişince gri |
| Üçgen | Koşuya özel | Kümülatif |
| Döngü küpü | Yalnız derlenmediğinde amber | **Her zaman amber**; çerçeve durumu taşır |
| Etiket | 5 sözcük + `retry` + üç parçalı | `up to date · 2h` · `modified` · `affected` · `never built` · `failed · 2h` |
| Animasyonlar | — | **Aynen**; zemin nötr gri değil, satırın rengi |
| VS'den derleme | Görünmez | Görünmez; bir sonraki Sync içerikten yakalar (A: düzenleyince, B: commit'leyince) |

## 1. Ölçüt: aracın işi

OSYS'de 177 projenin hepsi post-build copy ile `C:\OSYS\{Client,Server,…}\bin`'e yazar ve HintPath'ler oradan
okur. Havuz tek ve paylaşımlıdır: VS'nin de aracın da çıktısı aynı yere iner. "Bağımlılıklarda sorun oluyor,
projeyi derlemek için o projeye dallanıyorsun" = havuzdaki DLL'in kaynağın gerisinde kalması.

Aracın işi: *havuzu, taradığı ağacın kaynağına yetiştir.* Karar bu ağaçtaki dosyaların içeriğinden verilir; git
karara girmez. Bu yüzden "hangi ağaç" sorusu aracın doğruluğunu değil, **havuzun neyi yansıtacağını** seçer (§7).

## 2. Bugün

- **Karar hash'tir.** Girdi kümesi toplanır (`obj`/`bin`'e hiç girilmez), her dosyanın içeriği SHA-256'lanır,
  yol terimi köke göreli tutulur, configuration ve doğrudan bağımlılıkların imzaları eklenir, tek SHA-256 çıkar.
  Defterdeki son başarılı imzayla karşılaştırılır. Dosya boyutu + mtime yalnız "özeti yeniden hesaplayayım mı"
  önbellek anahtarıdır. Commit yalnız tanı bilgisidir. (Eski commit tabanlı formül üç açık yüzünden kaldırıldı:
  commit'lenmiş XAML değişikliği görünmüyordu, git'e eklenmemiş dosya hiçbir terime girmiyordu, aynı soruyu iki
  kod yolu cevaplıyordu.)
- **Sync ana (kirli) ağacı tarar** — kaydedip commit etmediğin dosya da hash'e girer, satır `modified` olur, in-place
  Build onu derler. Başka branch seçiliyse Build havuzdaki worktree'yi derler; gördüğün ile derlenecek ayrışabilir.
- **Renk = son işlemin hikâyesi; Sync boyamaz** (v1.11.0). Her işlem listeyi nötrler; toplam görünmez.
- **Üç ağaç modu**, havuz, LRU, worktree chip'i. Branch seçimi niyet; otomatik Sync yok.
- **Defter global ve içerik tabanlı**, anahtar ana kök yolu — worktree'de derlense de aynı. Aynı içerik iki ağaçta
  aynı imzayı üretir. Senin istediğin her şeyin zaten mümkün olmasının sebebi bu.
- Hata kaydı bayraktır; hata anındaki imza tutulmaz. Üçgen koşuya özeldir.

Kalması gerekenler: DLL/bin timestamp'i okunmaz (paylaşımlı havuzda bir DLL'in kaynağı bilinemez) · OutDir'e
dokunulmaz · ana repo checkout/reset edilmez, tek yazım ff-only · tek koşu · koreografiler · **içerik hash'i**.

## 3. Üç ilke

1. **Renk = çıktının durumu.** Koşu geçici amber bindirir; sonuç duruma yazılır ve kalır. Sync yeniden hesaplar,
   silmez. Tek kanal ilkesi korunur; kanalın söylediği değişir.
2. **Tek ağaç.** Sync de Build de aynı ağacı tarar ve derler; mod yok, seçim yok. Hangi ağaç: §7.
3. **Kendiliğinden hizalan.** HEAD değişince (checkout, commit, pull) ve açılışta Sync kendiliğinden. Ana repoda
   salt-okur, warm'da saniyenin altı.

## 4. Durum modeli

| Durum | Ne demek | Renk | Glyph | Etiket |
|---|---|---|---|---|
| Bilinmiyor | Sync yok / imza hesaplanamadı | başlangıç modu (halka, kesikli çerçeve) | kesikli daire | boş |
| Güncel | imza = son başarı | **yeşil** | ✓ | `up to date · 2h` |
| Güncel, bekliyor | hatalı bağımlılığa karşı derlendi, kendi imzası değişmedi | **yeşil + ▲** | ✓ | `up to date · 2h` |
| Derlenecek | kendi dosyası / bağımlılığı değişti / kayıt yok | **gri** | kesikli daire | `modified` / `affected` / `never built` |
| Bozuk | bu imza derleyiciden geçmedi | **kırmızı** | ✗ | `failed · 2h` |
| Kuyrukta / derleniyor | yalnız koşu içinde | amber | saat / dönen halka | değişmez |

**Kırmızının kuralı.** Kanıt: "bu içerik derleyiciden geçmedi". Defter hata anındaki imzayı tutar; Sync'te imza
aynıysa kırmızı kalır (tekrar derlemek aynı sonucu verir); bir şey değiştiyse gri. Kanıt yalnız exit ≠ 0'dır;
timeout kanıt değildir → gri `never built`, tooltip söyler. Kırmızı Build'i engellemez.

**Üçgen kümülatif:** defterde bekleme notu varken durur, proje notsuz derlenince düşer. **Küp** döngü üyesinde her
zaman amber; çerçeve durumu taşır. **Etiketler:** renk durumu, etiket rengin söyleyemediğini söyler — gri için
neden, yeşil ve kırmızı için ne zaman. `retry` kalkar (dalga gösteriyor), üç parçalı etiket kalkar (▲ kalıcı), yuva
134 px. **Sayaçlar** durumu sayar: Σ · building · ✓ · ○ · ✗ · ⚠; `—` chip'i kalkar; koşu tablosu ribbon'da aynen.

## 5. Akışlar — bugün → olmalı

| Akış | Bugün | Olmalı |
|---|---|---|
| Açılış | Halka; kullanıcı Sync'e basar | Sync kendiliğinden |
| Sync | fetch → ana ağaç → halka | fetch → ağaç taranır → yeşil / gri / kırmızı |
| Build | Herkes griye iner | Kimse inmez; dalga gri + kırmızıları yakar; sonuç toplama eklenir |
| Tekrar Build | Yine herkes gri | Kapsam yalnız kırmızılar + arada değişenler |
| Satırdan Build / Rebuild / Clean | Diğerleri griye iner | Yalnız hedef; Clean → `never built` |
| Resolve / Rebuild | — | Aynı; küp amber kalır |
| Checkout, pull | "Sync required", bekler | Sync kendiliğinden; içeriği aynı olan yeşil kalır |
| Commit | Hiçbir şey | HEAD değişir → Sync. **A:** içerik aynı, hiçbir şey değişmez. **B:** commit'lenen proje gri → Build → yeşil |
| VS'de derlediğin proje | Görünmez | **A:** bir sonraki Sync'te `modified` (içerik değişti) → Build yeniden derler. **B:** commit'e kadar yeşil kalır, commit'te `modified` → Build yeniden derler. İkisinde de bir "fazladan" derleme; farkı zamanı |
| Kırmızıyı düzeltme | — | Satırdan *Open in VS* → düzelt → **A:** Sync → gri → satırdan Build → yeşil. **B:** VS'de derle, commit, Sync → gri → Build → yeşil |
| Clean / Optimize | Aynen | Aynen |

## 6. Animasyonlar

Nötr an, dalga, veda, teslim, koşu opaklıkları, nefes, bead orbit, shake, neon final: **aynen** — hepsi opaklık
ya da kapsam işi, renkten bağımsız. "Kalan griler" cümlesi "kalan herkes kendi renginde" olur (kod değil, isim).
Sync reveal: yapı aynıysa yerinde tazeleme, yapı değiştiyse bugünkü reveal — Sync kendiliğinden koşacağı için.

## 7. Çalışma ağacı — A mı B mi

Karar içerikten geldiği için iki seçenek de **doğru**; farkları havuzun neyi yansıttığı.

| | **A — Ana ağaç (worktree yok)** | **B — `<repo>-Build` (HEAD)** |
|---|---|---|
| Havuz neyi yansıtır | Senin çalışma ağacını: branch + uncommitted düzenlemelerin | Branch'in HEAD'ini; düzenlemelerin görünmez |
| Dirty bağımlılık (D'yi değiştirdin, P ona muhtaç) | Araç D'yi `modified` görür ve derler → havuz senin D'n → VS'de P derlenir. **Senin sorunun tam bu** | Araç committed D'yi derler → havuzda senin D'n yok → D'yi VS'de kendin derlersin |
| Ping-pong | Yok: araç ve VS aynı içeriği derler | Var: pull D'yi değiştirdiyse araç committed D'yi havuza yazar, senin dirty `D.dll`'in gider; VS'de yeniden derlersin |
| Yarım iş (WIP) | Derlenmeyen WIP'in kırmızı olur ve bağımlılarına ▲ düşer — dürüst ama gürültülü | WIP görünmez; liste sakin |
| Yeşilin anlamı | "Gördüğün ağaç derlendi" | "Branch derlendi" — CI benzeri, kararlı |
| Git yazımı | Hiç (ff-only pull dışında). CLAUDE.md'nin "reset hiçbir akışta çalıştırılmaz" cümlesi harfiyen doğru olur | Her Sync'te build tree'de `reset --hard` (kapılı, kendi klasörümüzde) |
| `obj` | VS ile paylaşımlı (bugünkü in-place). Bayat-obj zehri için Optimize + koşu başı uyarı | İzole `_obj\<hash>`; zehir oluşmaz |
| VS ile aynı ağaç | Araç, VS'nin açık olduğu ağaçta derler — bugünkü varsayılan mod, aylardır böyle | Araç başka klasörde derler; ana ağacın `obj`/`bin`'ine dokunmaz |
| Liste bayatlığı | Düzenleme, stash, discard HEAD'i oynatmaz → liste bir sonraki Sync'e kadar eski olabilir (Build zaten yeniden planlar, doğruluk etkilenmez) | Yalnız HEAD önemli; izleyici her şeyi yakalar |
| Kod | **Silme:** worktree manager, havuz, LRU, chip/popover, switch, `UseWorktree`/`WorktreeName`, list/delete komutları, zorlama mantığı, niyet ayrımı, üç durum matrisi. Bugünkü in-place yolu zaten var | Yeni kod: devralma/reset/oluşturma, planlayıcı birleştirme, `--build-tree` test dikişi, havuz göçü |
| Mevcut `OSYS-Build` | Dokunulmaz; istersen `git worktree remove` | Devralınır |
| Branch seçimi | Yok (checkout'u izle; başka branch = checkout et) | Yok; pin istenirse görünür istisna |
| HEAD izleyici | Evet (`.git\logs\HEAD`, 1-2 s sessizlik) | Evet |

**Önerim: A.** Gerekçe: (1) senin tarif ettiğin acı dirty bağımlılıktır ve onu yalnız A çözer; (2) havuz senin
makinendeki kendi havuzun — WIP'in oraya girmesi kimseyi etkilemez; (3) sıfır git yazımı, sıfır yeni kavram, kod
azalır; (4) bugünkü varsayılan yol zaten A'dır, yalnız diğer iki mod silinir. A'nın gürültüsünü (WIP kırmızısı)
ucuz bir işaret dindirir: `git status --porcelain` zaten salt-okur yüzeyde (`GetDirtyPathsAsync`); dirty dosyası
olan projenin etiketi `modified · local` olur ve tooltip "includes uncommitted edits" der. Böylece kırmızı + `local`
= benim WIP'im; kırmızı, `local` yok = committed kaynak bozuk. Tek satırlık bir ek, karar değişmez.

**B ne zaman:** aracın anlamı "branch derlenir mi" olsun istiyorsan (CI gibi), ya da aracın VS'nin açık olduğu
ağaçta derlemesini istemiyorsan. v1'de B'yi önermiştim çünkü "her zaman worktree'de" isteğini veri saydım; hash
bulgusu bunun bir doğruluk gereği değil tercih olduğunu gösterdi.

## 8. UI

Liste, graf, ribbon, action bar, konsol: §4-6'daki gibi. Worktree chip'i ve popover'ı kalkar; branch chip'i
checkout edilmiş branch'i ve `N behind`'ı gösterir, salt-okur. Ayarlar → Workspace: A'da yalnız repo kökü; B'de
ek olarak türetilen build tree yolu.

## 9. Bedeller

**Ortak:** renk kanalının anlamı ve etiket sözlüğü değişiyor — tasarım paketi §8 "renk yalnız son işlemin
hikâyesini anlatır — tekrar önerme" kararı tersine dönüyor; küçük bir v1.20.0 (§2.3, §2.4, §5, §8, §9) gerekir.
VS'de derlediğin her proje bir kez daha derlenir (araç DLL'e bakmaz, bakamaz).

**A:** WIP kırmızıları (`local` işaretiyle dindirilir) · `obj` paylaşımlı (bugünkü gibi) · liste düzenlemeden
sonra Sync'e kadar bayat olabilir.

**B:** dirty bağımlılığı kendin derlersin · ping-pong · commit alışkanlığı · her Sync'te reset · yeni kod ve göç.

## 10. Bilerek eklenmeyenler

Sync sonrası otomatik Build (havuz paylaşımlı, VS ortasında sürpriz) · "VS'de derledim, yeşil say" düğmesi (yalan
biriktirir) · havuz DLL izleyicisi / VS post-build kancası (kaynağı bilinmeyen DLL'e güvenmek; repo'ya dosya
sokmak) · DLL timestamp · branch başına havuz · A'da "dirty projeleri Build'den hariç tut" anahtarı (bağımlılar
eski DLL'e link'ler, ▲ karmaşası).

## 11. Kararlar — senin

| # | Soru | Önerim |
|---|---|---|
| 0 | **A (ana ağaç) mı, B (`OSYS-Build`) mi?** | **A** |
| 1 | Sync sonrası kanıtlı kırmızı? | Evet |
| 2 | `failed · 2h` mi `failed` mi? | `failed · 2h` |
| 3 | A'da `modified · local` işareti? | Evet |
| 4 | HEAD izleyici Faz 2'de mi? | Evet |
| 5 | Sync reveal yapısal imzaya bağlansın mı? | Evet |
| 6 | Design v1.20.0 paketi mi, rapor spec mi? | Paket |
| 7 | Sıra | Faz 1 → Faz 2 |

## 12. Fazlar

**Faz 1 — Renk modeli** (App + küçük Core/Supervisor; A/B'den bağımsız): defterde hata imzası; karar sırasında
kanıtlı kırmızı önde; `VisualStatus` = durum + koşu bindirmesi; nötrleme kalkar; ▲ kümülatif; küp hep amber;
etiketler + 134 px; sayaç/filtreler; testler yeniden pinlenir; doküman + design v1.20.0. Tek başına ilk isteğini
verir: Sync'ten sonra renkli, toplamı gösteren liste.

**Faz 2A — Tek ağaç = ana ağaç:** worktree kodu ve UI'ı silinir; branch popover'ı kalkar, chip salt-okur; açılışta ve
HEAD değişiminde Sync; reveal kapısı; `modified · local`; eski havuz için konsola tek satır ipucu (`git worktree
remove`); kaynak guard'ları daralır (mutasyon yüzeyi gerçekten tek dosya); ARCHITECTURE §8.6-8.7, §9.4, §10.2-10.4,
§13 action bar, §16, §20 + README + CLAUDE.md cümlesi harfiyen doğru hâle gelir.

**Faz 2B — Tek ağaç = `<repo>-Build`:** v1'deki gibi (devralma, reset kapıları, planlayıcı birleştirme,
`--build-tree`, göç, CLAUDE.md'ye "ana repoda" niteliği).

Süreç: §11 kararları → design v1.20.0 → spec → `writing-plans` → kırmızı test kuralıyla task-by-task. Kod yazılmadı.
