# Sync / Build akışı — kararlaşan model

Tarih: 2026-09-18 · Durum: karar alındı, kodda değişiklik YOK · `main` = `ea3ab62`
Öncekiler tarihseldir: `2026-09-17-20-36-…-analysis.md` (v1), `2026-09-18-05-39-…-analysis-v2.md` (v2),
kod kanıtları `2026-09-17-19-41-cumulative-status-analysis-verification.md`.

## 1. Karar

Kullanıcı kararı (2026-09-18): **araç her zaman worktree'de, seçilen branch'in commit'lenmiş hâlini derler.**
Gerekçe kullanıcının kendi sözleriyle: çalışırken farklı bir branch'i derleyip test edebilmek isteniyor; branch
değiştirirken localdeki değişiklikler sorun çıkarmamalı; kafa karışıklığı olmamalı; localdeki değişiklikler için
kendi projeleri zaten elle derleniyor; önemli olan genel derlemenin başarılı olması.

Bu karar v2'nin A önerisini (ana ağaçta derleme) geçersiz kılar. Kalan her şey (renk modeli, kanıtlı kırmızı,
kümülatif üçgen, hep amber küp, etiket sözlüğü, sayaçlar, animasyonlar, otomatik Sync) aynen geçerlidir.

Kararla birlikte düşen: `modified · local` işareti. Araç zaten commit'lenmiş hâli derlediği için "localde
düzenleme var" bilgisi satırın kararıyla ilgisizdir; işaret eklenmez.

## 2. Model ve iş bölümü

**Araç:** seçilen branch'in commit'lenmiş kaynağını `<repo>-Build` ağacında derler ve ortak çıktı havuzunu
(`C:\OSYS\*\bin`) o kaynakla tutarlı tutar. Karar diskteki kaynak dosyaların içerik hash'inden verilir;
git yalnız hangi commit'in ağaca kurulacağını söyler, karara girmez.

**Developer:** commit'lemediği kendi değişikliklerini Visual Studio'da derler. Araç onları görmez ve görmemelidir.

Ayrım tek cümlede: **araç commit'lenmiş dünyayı derler, sen commit'lenmemiş dünyanı derlersin.**

## 3. Akış — eskiden ve şimdi

| Ne yaptığımda | Eskiden | Şimdi |
|---|---|---|
| **Sync** | Her satır renksiz griye döner; hangisi güncel yalnız sağdaki küçük yazıdan okunur | Yeşil güncel, gri derlenecek, kırmızı kanıtlı bozuk; döngü üyesinin küpü amber |
| **Build** | Önce herkes griye iner, sonra kapsam sarıya yanar, yalnız o koşu boyanır | Yeşiller yeşil kalır; sarı dalga yalnız gri ve kırmızıları yakar; sonuç toplamın üstüne eklenir |
| **Tekrar Build** | Yine herkes griye iner, önceki sonuçlar silinir | Sıfırlama yok; kapsam yalnız kırmızılar ve arada değişenler |
| **Satırdan Build** | Hedef dışındaki bütün satırlar griye iner | Yalnız hedef sarıya yanar; başka hiçbir satır renk değiştirmez |
| **Resolve cycles** | Üyeler boyanır, kapsam dışı griye iner | Üyeler boyanır; kapsam dışı hiçbir satır değişmez; küp amber kalır |
| **Tekrar Sync** | Her şey griye döner, sonuçlar gider | Durum yeniden hesaplanır, silinmez; toplam ekranda kalır |

Örnek: elli yeşil, iki gri. Build sonrası elli bir yeşil, bir kırmızı. Kırmızıyı düzeltip satırdan derleyince elli
iki yeşil. Hiçbir aşamada liste sıfırlanmaz.

## 4. Branch ve worktree

| Konu | Kural |
|---|---|
| Ağaç | Tek ağaç: `<repoRoot>-Build`, repo klasörünün yanında, türetilir, ayar yok. `D:\Projects\Delta\OSYS-Build` zaten var ve OSYS'nin detached worktree'si; devralınır |
| Varsayılan branch | Checkout edilmiş branch. Terminalde checkout yapılırsa araç kendiliğinden ona geçer |
| Başka branch | Seçilebilir. Seçim kalıcıdır, kapat-aç aynı branch'te kalır. Yalnız test amaçlıdır ve havuzu o branch'e çevirir (§6) |
| Commit çözümü | Yerel ref önce, yoksa `origin/<branch>`. Push edilmemiş yerel branch çalışır |
| Ne zaman kurulur | Her Sync'te: ağaç varsa ve detached ise `reset --hard <sha>`, yoksa `worktree add --detach`. Ana repoya dokunulmaz |
| Otomatik Sync | Açılışta; `.git\logs\HEAD` değişince (checkout, commit, pull); branch seçilince; pencereye dönüldüğünde (kısa bir bekleme penceresiyle, güvenlik ağı) |
| Kalkanlar | In-place mod, worktree açma/kapama anahtarı, worktree chip'i ve popover'ı, worktree adı ve hedef listesi, `%LOCALAPPDATA%` havuzu ve 20 GiB LRU, listele/sil komutları |
| Kalanlar | Branch chip'i (artık salt branch seçimi), `N behind` chip'i ve ff-only pull, harici kökler |
| `obj` | `<repoRoot>-Build\_obj\<hash(projectId)>` — izole, branch'ler arası sabit, VS'nin `obj`'siyle karışmaz |
| Uyarı | Build tree elle düzenlenirse her Sync siler; köke `BUILD-TREE-DO-NOT-EDIT.txt` |
| Test dikişi | Supervisor `--build-tree <yol>` alır; acceptance testleri gerçek ağaca dokunmaz |

## 5. Commit'lenmemiş değişiklik ve worktree

**Worktree commit'lenmemiş değişiklikleri göremez.** Bir worktree bir commit'in ayrı bir checkout'udur; senin
düzenlemelerin yalnız ana ağacın dosyalarında yaşar. Onları worktree'ye taşımanın tek yolu commit ya da
stash-ve-uygula olurdu; ikisi de tam olarak kaldırdığımız karmaşıklık. Bu yüzden araç onları **hiç** derlemez.

Pratikte anlamı:

- Bir projeyi değiştirdin, commit etmedin. Araç o projeyi **commit'lenmiş hâliyle** derler ve havuzdaki DLL'i
  onunla değiştirir. Senin VS'de ürettiğin DLL gider.
- Bu yüzden sıra önemlidir: **önce Build, sonra kendi projelerini VS'de derle.**
- Bir bağımlılığı değiştirip commit etmediysen, onu VS'de kendin derlemen gerekir; araç sana o DLL'i vermez.

Bu, kararın kabul edilen bedelidir. Yumuşatması §6-2'dedir.

## 6. Kararla birlikte gelen iki yeni gereklilik

Bunlar worktree kararının doğrudan sonucudur ve öneri olarak eklenmiştir.

**1. Havuzun hangi branch'i taşıdığı görünmeli.** Ortak çıktı havuzu tektir ve branch'e göre izole değildir
(ARCHITECTURE §20, bilinen sınır). `feature/x` üzerinde çalışırken `developer`'ı derlersen havuzdaki bütün
DLL'ler `developer`'ındır ve VS'deki derlemen onlara link olur. Bu, kullanıcının "kafa karışıklığı olmasın"
şartına doğrudan aykırı bir sessiz durumdur. Öneri: ribbon ya da action bar, havuzun hangi branch'i taşıdığını
söyler; checkout edilen branch ile havuzun branch'i farklıysa amber bir uyarı durur ve tek tıkla geri dönülür.

**2. Commit'lenmemiş projeler için tek satırlık uyarı.** Build başlarken, commit'lenmemiş değişikliği olan proje
varsa konsola tek satır: kaç proje, hangileri, ve "bu koşu onların çıktısını commit'lenmiş hâlle değiştirecek".
Bloklamaz, sormaz. Git sorgusu zaten salt-okur yüzeyde mevcut (`GetDirtyPathsAsync`).

## 7. Satır etiketleri

| Yazı | Ne demek |
|---|---|
| `up to date · 2h` | Güncel; son başarılı derleme iki saat önce |
| `modified` | Kendi dosyaları değişti |
| `affected` | Kendi dosyası aynı, bir bağımlılığı değişti |
| `never built` | Bu araç bu projeyi hiç derlemedi |
| `failed · 2h` | İki saat önce derlenirken patladı ve o gün bugün kaynağı değişmedi |
| boş | Henüz Sync yapılmadı |

Kalkanlar: `failed · retry` (sözü sarı dalga zaten veriyor) ve üç parçalı `affected · up to date · 2h` (anlattığını
artık kalıcı üçgen anlatıyor). `modified · local` eklenmez (§1). Etiket yuvası tasarımın 134 pikseline döner.

**Üçgen kalıcı olur:** defterdeki bağımlılık notu durdukça durur, proje notsuz derlenince düşer. **Küp** döngü
üyesinde her zaman amber; çerçeve ve şerit durumu taşır.

**Kırmızının kuralı:** kanıt, hatıra değil. Defter hata anındaki imzayı tutar; bugünkü imza ona eşitse kırmızı
kalır, çünkü aynı kaynak aynı derleyicide yine patlar. Kaynağa dokunulunca kanıt düşer, satır griye döner. Kanıt
yalnız derleyici hatasıdır; zaman aşımı kanıt değildir ve o satır gri `never built` olur. Kırmızı hiçbir şeyi
engellemez, Build her zaman yeniden dener.

**Sayaçlar** durumu sayar: toplam, derlenen, güncel, derlenecek, bozuk, uyarı. Atlandı chip'i kalkar; koşunun
kendi tablosu ribbon'da ve akışın kapanış satırında aynen kalır.

## 8. Animasyonlar

Nötr an, rastgele sarı dalga, veda, sıralı teslim, koşu opaklıkları, nefes, bead orbit, failed shake ve neon
final aynen kalır; hepsi opaklık ya da kapsam işidir, renkten bağımsızdır. Tek fark: altlarındaki zemin nötr gri
değil, satırın kendi rengidir. Finalin son adımı "kalan griler" yerine "kalan herkes kendi renginde" olur; bu bir
isim değişikliğidir, kod aynıdır. Sync sonrası liste, yapı değişmediyse yerinde tazelenir; yapı değiştiyse
bugünkü reveal oynar.

## 9. Bilinmesi gerekenler

- **Branch testi havuzu çevirir.** Farklı branch derlemek ucuz değildir: havuz o branch'e döner, geri dönüşte
  farklı olan projeler yeniden derlenir. İçeriği iki branch'te aynı olan projeler yeşil kalır, çünkü karar
  içerik hash'idir; yani maliyet sanıldığından küçüktür ama sıfır değildir.
- **Araç VS'nin derlediğini göremez.** Havuzdaki bir DLL'in hangi kaynaktan çıktığı bilinemez; bu yüzden çıktı
  hiç okunmaz. Commit ettiğin anda Sync o projeyi gri gösterir, araç bir kez daha derler. Fazladan ama doğru.
- **Yeşilin anlamı:** bu branch'in commit'lenmiş kaynağı derlendi. Senin dirty derlemen bu iddiaya girmez.
- **Renk kanalının anlamı değişiyor.** Tasarım paketi §8'in "renk yalnız son işlemin hikâyesini anlatır, tekrar
  önerme" kararı tersine dönüyor. Tek kanal ilkesi korunur, kanalın söylediği şey değişir. Küçük bir tasarım
  sürümü gerekir: §2.3 renk kuralı, §2.4 etiket, §5 statü tablosu, §8 kararlar, §9 sürüm notu.
- **CLAUDE.md düzeltmesi.** "reset hiçbir akışta çalıştırılmaz" cümlesi bugün de kodla uyumsuz: `reset --hard`
  havuz worktree'sinin içinde koşuyor ve ARCHITECTURE §10.1 bunu belgeliyor. Cümle "ana repoda" niteliğiyle
  yeniden yazılır.

## 10. Fazlar

**Faz 1 — Renk modeli.** App ve küçük bir defter dokunuşu. Hata imzası deftere yazılır; karar sırasında kanıtlı
kırmızı öne alınır; görsel durum "çıktının durumu" olur ve koşu onun üstüne biner; nötrleme kalkar; üçgen kalıcı
olur; küp hep amber; etiketler ve yuva; sayaçlar ve filtreler; testler yeni kuralı pinleyecek şekilde yeniden
yazılır; doküman ve tasarım sürümü. Bu faz tek başına "Sync sonrası renkli ve toplamı gösteren liste" isteğini
karşılar ve worktree işinden bağımsızdır.

**Faz 2 — Tek worktree ve otomatik hizalanma.** Planlama hattı Core'da tek yere iner ve Sync de Build de aynı
hattı çağırır; worktree yönetimi tek ağaca indirilir ve mevcut ağaç devralınır; in-place mod, havuz, LRU, chip
ve popover kalkar; komut sözleşmesinden worktree alanları düşer, Sync sonucu build tree'yi taşır; branch seçimi
sadeleşir; açılışta, HEAD değişiminde, branch seçiminde ve pencereye dönüşte otomatik Sync; havuz branch
göstergesi; commit uyarısı; kaynak guard'ları ve doküman.

Süreç: bu doküman esas alınarak tasarım sürümü, sonra spec, sonra plan, sonra kırmızı test kuralıyla task-by-task
uygulama.

## 11. Açık kalan küçük başlıklar

1. Havuz branch göstergesi nereye: ribbon'un faz satırına mı, action bar'da branch chip'inin yanına mı.
2. Commit uyarısı yalnız konsol satırı mı, yoksa Build butonunun tooltip'inde de mi görünsün.
3. Branch seçimi kalıcı mı (kapat-aç aynı branch) yoksa her açılışta checkout edilmiş branch'e mi dönsün.
   Öneri: kalıcı, ama checkout edilmiş branch'ten farklıysa havuz göstergesi amber uyarıyı taşısın.
