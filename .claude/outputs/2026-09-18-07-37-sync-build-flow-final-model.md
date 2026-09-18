# Sync / Build akışı — nihai model

Tarih: 2026-09-18 · Durum: kararlar kapandı, kodda değişiklik YOK · `main` = `ea3ab62`
Bu dosya önceki analizlerin (v1 `…-20-36`, v2 `…-05-39`, karar `…-06-27`) yerine geçer; onlar tarihseldir. Kod
kanıtları `2026-09-17-19-41-cumulative-status-analysis-verification.md`'de.

## 1. Kararlar

| # | Konu | Karar |
|---|---|---|
| 1 | Çalışma ağacı | **Ana proje.** Worktree yok; araç her zaman kullanıcının çalışma ağacında derler. Sync de Build de aynı ağacı görür |
| 2 | Renk | Çıktının durumu: yeşil güncel · gri derlenecek · kırmızı kanıtlı bozuk · boş bilinmiyor. Kümülatif; Sync yeniden hesaplar, silmez |
| 3 | Karar terimi | Kaynak dosyaların içerik hash'i (bugünkü mekanizma). Defter yalnız aracın kendi derlediğini yazar |
| 4 | Dışarıdan derleme | **Kredi verilir.** Çıktı dosyası bütün girdilerinden ve bağımlılıklarının çıktılarından yeniyse proje güncel sayılır, kim derlemiş olursa olsun (§5) |
| 5 | Defterin sınırı | **Defter yalnız kendi ürettiği çıktı için konuşur.** Çıktı, defterdeki son derlemeden sonra değişmişse defter değil zaman damgası karar verir (§5) |
| 6 | Branch takibi | **Anlık.** Araç `.git\logs\HEAD` dosyasını izler; checkout, commit, pull, reset görülünce Sync kendiliğinden koşar |
| 7 | Araçtan branch değiştirme | **Var.** Branch chip'inden seçilen branch `git checkout` ile değiştirilir; yalnız tıklamayla |
| 8 | Kirli ağaçta değiştirme | **Ayar:** *Stop* (varsayılan) ya da *Stash and switch*. Stash and switch: anlaşılır mesajla stash, konsola satır, checkout. Geri uygulama yok, chip'te işaret yok |
| 9 | Koşu sırasında branch değişimi | Nazik durdurma; değişim anında derlenmekte olan projelerin sonucu deftere yazılmaz; koşu bitince Sync. Commit koşuyu durdurmaz |
| 10 | Pencereye dönüş | Sync koşar (kısa bekleme penceresiyle) |
| 11 | Sync reveal | Yapı aynıysa yerinde tazeleme; yapı değiştiyse bugünkü reveal |
| 12 | Kırmızı | Kanıt: hata anındaki imza tutulur, bugünkü imza eşitse kırmızı. Yalnız derleyici hatası kanıttır; timeout gri `never built` |
| 13 | Üçgen | Kümülatif: defterdeki bağımlılık notu durdukça durur |
| 14 | Küp | Döngü üyesinde her zaman amber; çerçeve durumu taşır |
| 15 | Etiketler | `up to date · 2h` · `modified` · `modified · local` · `affected` · `never built` · `failed · 2h` · boş. Yuva 134 px |
| 16 | Sayaçlar | Durumu sayar: toplam · derlenen · güncel · derlenecek · bozuk · uyarı. Atlandı chip'i kalkar |
| 17 | Animasyonlar | Değişmez |
| 18 | `C:\OSYS` bağımlılığı | **Yok.** Havuz yolu HintPath'lerden türer; koda gömülü yol yoktur (§7) |
| 19 | Sıra | Faz 1 renk modeli → Faz 2 tek ağaç ve izleyici → Faz 3 dışarıdan derleme kredisi |

## 2. Model

Araç, kullanıcının çalışma ağacındaki kaynağı derler ve ortak çıktı havuzunu o kaynakla tutarlı tutar. Havuz,
projelerin HintPath'lerinin gösterdiği yerdir; araç onun nerede olduğunu bilmez, bilmesi gerekmez. Karar, kaynak
dosyaların içerik hash'inden verilir. VS de araç da aynı dosyaları okuduğu için ikisi asla farklı dünyaları
derlemez: kullanıcının commit'lemediği değişiklik de aracın gözünde sıradan içeriktir.

## 3. Akış — eskiden ve şimdi

| Ne yaptığımda | Eskiden | Şimdi |
|---|---|---|
| Sync | Her satır renksiz gri | Yeşil / gri / kırmızı; döngü küpü amber |
| Build | Herkes griye iner, kapsam sarıya yanar | Kimse inmez; dalga gri ve kırmızıları yakar; sonuç toplama eklenir |
| Tekrar Build | Yine herkes gri | Kapsam yalnız kırmızılar ve arada değişenler |
| Satırdan Build / Rebuild / Clean | Hedef dışı satırlar griye iner | Yalnız hedef; Clean → `never built` |
| Resolve cycles | Kapsam dışı griye iner | Kapsam dışı değişmez; küp amber kalır |
| Tekrar Sync | Sonuçlar silinir | Yeniden hesaplanır, silinmez |
| VS'de dosya değiştirip derledim | Görünmez | Pencereye dönünce Sync; çıktı taze ise **yeşil** (§5), değilse `modified` |
| Branch'i VS'den değiştirdim | Araç fark etmez | Birkaç saniyede Sync; aynı içerikli projeler yeşil kalır |
| Branch'i araçtan değiştirdim | Yalnız worktree'de derlerdi | Gerçek checkout; ağaç kirliyse ayar karar verir (§6) |
| Commit ettim | Hiçbir şey | Sync koşar; içerik aynı olduğu için hiçbir şey değişmez |

## 4. Durum modeli

| Durum | Renk | Glyph | Etiket | Kaynak |
|---|---|---|---|---|
| Bilinmiyor | başlangıç modu | kesikli daire | boş | Sync yok / imza yok |
| Güncel | yeşil | ✓ | `up to date · 2h` | defter imzası eşit, ya da çıktı taze (§5) |
| Güncel, bekliyor | yeşil + ▲ | ✓ | `up to date · 2h` | defter notu: hatalı bağımlılığa karşı derlendi |
| Derlenecek | gri | kesikli daire | `modified` / `modified · local` / `affected` / `never built` | içerik değişti / kayıt yok |
| Bozuk | kırmızı | ✗ | `failed · 2h` | hata anındaki imza = bugünkü |
| Kuyrukta / derleniyor | amber | saat / halka | değişmez | yalnız koşu içinde |

`local` kuyruğu: projenin girdi dosyalarından en az biri `git status`'ta kirli. Kırmızı + `local` = kullanıcının
yarım işi; kırmızı, `local` yok = commit'lenmiş kaynak bozuk. Bilgi salt-okur `GetDirtyPathsAsync`'ten gelir.

Kalkan etiketler: `failed · retry` (sözü dalga veriyor) ve üç parçalı `affected · up to date · 2h` (üçgen kalıcı).

## 5. Dışarıdan derleme kredisi ve defterin sınırı

Tek ağaç olduğu için şu çıkarım geçerlidir: **bir projenin çıktısı bütün girdilerinden yeniyse, o çıktı bugünkü
kaynağa aittir** — derleyici o dosyaları okudu ve o andan beri hiçbiri değişmedi. MSBuild'in kendi artımlı kuralı.

**Kanıt dosyası.** Projeye başka projeler HintPath ile bağlanıyorsa kanıt, o HintPath'lerin gösterdiği dosyalardır
(havuz; birden çok yol varsa hepsi). Kimse bağlanmıyorsa (uygulama projeleri) kanıt, projenin kendi çıktı dosyasıdır:
csproj'daki configuration'a bağlı `OutputPath` + `AssemblyName` + `OutputType`'a göre uzantı. Değerlendirici bugün
`OutputPath`/`OutputType` okumuyor; Faz 3'te eklenir, ham XML, configuration koşullu grup, yoksa `bin\<cfg>\`.
Bağımlılık tazeliği: projenin kendi HintPath'lerinin gösterdiği dosyalar kanıt dosyasından eski olmalı.

**İki kip, "çıktı kimin" sorusuyla seçilir:**

- **Defter kipi** — kanıt dosyası, defterdeki son aracın derlemesinden yeni DEĞİLSE (yani çıktı aracın kendi
  ürünü): karar bugünkü gibi içerik hash'inden. Zaman damgası veto etmez; içerik aynıysa gündür, dosyaya dokunup
  geri almak derletmez.
- **Zaman kipi** — kanıt dosyası defterdeki son derlemeden yeniyse ya da kayıt yoksa (biri aracın ardından derledi,
  ya da araç hiç derlemedi): güncel ⇔ kanıt dosyası bütün girdilerden ve bağımlılık çıktılarından yeni. Yeşil,
  yaş kanıt dosyasının zamanından, tooltip "built outside this tool". Deftere yazılmaz; her Sync yeniden kanıtlar.

Kapanan delikler: (a) VS'de derlenen proje yeşil olur; (b) ilk kurulumda VS'nin zaten derlediği projeler `never
built` değil yeşil görünür; (c) düzenle, VS'de derle, geri al — defter "güncel" derdi, havuzda düzenlenmiş DLL
kalırdı; zaman kipi bunu yakalar, çünkü geri alınan dosya çıktıdan yenidir.

Sınırlar: derlemenin ortasında dosya değiştirme yarışı (MSBuild'de de var); havuza elle kopyalanmış bir DLL taze
görünebilir (hash önbelleğinin mtime varsayımıyla aynı sınıf); Debug/Release ayrı `bin` klasörleri olduğu için
kip, seçili configuration'a göre bakar.

Bağımlılar her zaman imzayla değerlendirilir: P değiştiyse onlar gri olur ve Build'de havuzdaki taze `P.dll`'e
karşı derlenir. Döngü üyeleri grup olarak ele alınır: hepsinin kanıtı taze değilse grup bayat.

## 6. Branch

- **İzleyici.** `.git\logs\HEAD` üzerinde dosya izleyicisi; bir iki saniye sessizlikten sonra Sync. Reflog satırı
  işlemi söyler: checkout, pull, merge, reset koşu sırasında **nazik durdurma** tetikler (yeni proje başlamaz,
  derlenenler biter, değişim anında derlenmekte olanların sonucu deftere yazılmaz, koşu bitince Sync); commit
  hiçbir şeyi durdurmaz. Güvenlik ağı: koşu bitince plan anındaki commit ile şimdiki farklıysa Sync.
- **Checkout düğmesi.** Branch chip'i menüsünden seçim `git checkout <branch>` çalıştırır (uzak branch için
  izleyen yerel branch oluşur). Koşu sırasında kilitli. Kirli ağaçta ayara bakılır: *Stop* → konsola "N dosyada
  commit edilmemiş değişiklik var, önce commit ya da stash" ve hiçbir şey yapılmaz; *Stash and switch* →
  `git stash push -u -m "build-orchestrator: leaving <branch>"`, konsola tek satır, checkout. Geri uygulama
  kullanıcınındır. Ayar Settings → General'da, "Pull before build" ile aynı ilkede: kullanıcının açık ve kalıcı
  kararı.
- **Pencereye dönüş** Sync tetikler; ard arda dönüşlerde kısa bekleme penceresi.
- Mutasyon yüzeyi tek dosyada kalır ve kaynak guard'ı onu pinler: ff-only pull, checkout, stash push. CLAUDE.md
  "iki istisna"yı "üç istisna" olarak yeniden yazar; havuz worktree'si kalktığı için "reset hiçbir akışta
  çalıştırılmaz" cümlesi harfiyen doğru olur.

## 7. Çıktı dosyası ve `C:\OSYS`

Araçta `C:\OSYS`'ye gömülü bir yol yoktur; kaynakta "OSYS" yalnız bir sınıflandırma enum üyesinin adında
(`HintPathClass.ExternalOsysPlatform`: üreticisi bulunamayan, `bin` altındaki HintPath'ler) ve bir geliştirme
spike penceresinde geçer. Havuz, projelerin HintPath'lerinin gösterdiği yerdir; başka bir repoda başka bir yer
olur. Kredi kuralı da bu yüzden geneldir: kanıt dosyası HintPath hedefidir, kopya komutu okunmaz. Enum üyesi
Faz 3'te ürün-bağımsız bir adla (`ExternalPlatformBin`) yeniden adlandırılır; davranış değişmez.

## 8. Animasyonlar

Nötr an, rastgele dalga, veda, sıralı teslim, koşu opaklıkları, nefes, bead orbit, failed shake, neon final:
değişmez. Zemin nötr gri değil satırın kendi rengidir; finalin son adımı "kalan herkes kendi renginde". Kod aynı,
cümleler değişir.

## 9. Bilinmesi gerekenler

- VS'de derlediğin proje, çıktısı tazeyse yeşil görünür; kopya başarısız olduysa görünmez (havuz kontrolü).
- Aynı projeyi aynı anda VS ve araçla derleme; aynı `obj`'ye yazarlar. Bugünkü kural.
- Derleme sürerken checkout git tarafında dosya kilidi yüzünden yarım kalabilir; Windows'un huyu, aracın değil.
  Araç diskte ne varsa onu hash'ler, yanlış karar vermez.
- Test için başka branch'e geçince havuz o branch'e döner; dönüşte Build'e basmadan VS'de derleme yapma.
- Tasarım paketi §8 "renk yalnız son işlemin hikâyesini anlatır" kararı tersine dönüyor; küçük bir tasarım
  sürümü gerekir: §2.3, §2.4, §5, §8, §9.

## 10. Bilerek eklenmeyenler

Sync sonrası otomatik Build · "yeşil say" düğmesi · stash'in araç tarafından geri uygulanması · dönüşte
kendiliğinden stash pop · worktree ya da branch başına havuz · "başka branch'i checkout etmeden derle" (ihtiyaç
kanıtlanırsa Faz 4, model değişmez).

## 11. Kalkanlar

In-place / worktree modları ve üç durum matrisi · worktree anahtarı, chip'i, popover'ı, adı ve hedef listesi ·
`%LOCALAPPDATA%` havuzu, LRU ve listele/sil komutları · `StartRunCommand.UseWorktree/WorktreeName` ·
`RunBranchIntent` ve zorlama mantığı · "Sync required" bekleme durumu · `ProjectIdentityRebase`'in worktree dalı
(in-place'te zaten no-op) · `failed · retry` ve üç parçalı etiket · atlandı chip'i.

Eski havuz worktree'leri için ilk açılışta konsola tek satır ipucu: `git worktree remove` ile temizlenebilir;
araç kendisi silmez. `D:\Projects\Delta\OSYS-Build` kullanıcıya kalır.

## 12. Fazlar

**Faz 1 — Renk modeli.** Defterde hata imzası; karar sırasında kanıtlı kırmızı önde; görsel durum = çıktı durumu +
koşu bindirmesi; nötrleme kalkar; üçgen kümülatif; küp hep amber; etiketler ve 134 px; `local` kuyruğu; sayaçlar ve
filtreler; testler yeni kuralı pinler; doküman ve tasarım sürümü. In-place Sync ile bugün bile çalışır.

**Faz 2 — Tek ağaç ve izleyici.** Worktree kodu ve UI'ı silinir; branch chip'i checkout düğmesi olur; kirli ağaç
ayarı; HEAD izleyici ve koşu içi durdurma kuralı; açılışta ve pencereye dönüşte Sync; reveal kapısı; guard ve
doküman; CLAUDE.md cümleleri.

**Faz 3 — Dışarıdan derleme kredisi.** Değerlendiriciye `OutputPath`/`OutputType`; kanıt dosyası çözümü; iki kip;
döngü grubu kuralı; enum yeniden adlandırma; testler ve doküman (§7.1'in "çıktı okunmaz" cümlesi "aracın kendi
çıktısı için defter, başkasının çıktısı için zaman damgası" olarak yeniden yazılır).

Süreç: tasarım sürümü → spec → plan → kırmızı test kuralıyla task-by-task. Kod yazılmadı.
