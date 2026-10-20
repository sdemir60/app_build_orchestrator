# Nihai özellik envanteri — FEATURES.md / WHY.md için

Kaynaklar: `.claude/temp/inventory-arch-merged.md` (ana kaynak), `inventory-readme.md`, `inventory-history.md`,
`inventory-code.md` (§6 cevapları uygulandı). Kısaltmalar: **ARCH** = ARCHITECTURE.md, **arch N** = arch-merged girdi
no, **K** = küçük özellik, **S** = bilinen sınır, **X** = kaldırılan/bayat.

## 1. Sayım

- **Kaynak girdiler:** arch-merged 137 (+2 iç ayrıntı, 61 sınır, 20 açık belirsiz) · README 88 başlık (+13 sınır maddesi, 15 belirsiz) · geçmiş 65 (+11 belirsiz) · kod 20 girdi + 5 katalog bölümü (+11 cevap)
- **Ana madde:** 35
- **WHY adayı:** 22 (madde 1-8, 10-14, 19, 20, 21, 23, 26, 28, 29, 32, 33)
- **Bilinen sınır:** 66 (tekrarsız)
- **Açık belirsizlik:** 17 (kullanıcı kararı gereken 13 nokta ayrıca §6'da)

## 2. Ana maddeler

## 1. Yalnız değişeni derleme
- EN başlık önerisi: Builds only what changed
- Ne yapar: Tek Build ile yalnız değişen projeler ve onlardan etkilenenler derlenir; güncel projeler `skipped — up to date` diye atlanır.
- Nasıl: Her projenin diskteki kaynak içeriğinden bir imza çıkarılır ve son başarılı derlemenin imzasıyla karşılaştırılır; git'e danışılmaz, çıktının tarihi tek başına "güncel" demeye yetmez. Silinmiş ya da ezilmiş çıktı, imza aynı olsa da projeyi yeniden derletir.
- Etiketler: SHA-256 içerik imzası ile değişeni bulma; boyut+mtime anahtarlı özet önbelleği ile hızlı karar; çıktı kanıtı ile silinen DLL'i yakalama; build-state defteri ile kalıcı karar
- Yüzler:
  - Girdi kümesi: `.csproj`, beyan edilen item'lar, proje klasöründeki etkileyen dosyalar (beyan edilmemiş, commit'lenmemiş, gitignore'lu dahil), en yakın `Directory.Build.*` / `Directory.Packages.props`; yalnız `.cs .xaml .resx .csproj .props .targets`
  - Commit'siz depo, çevrimdışı ya da sürüm kontrolü dışı klasör aynı cevabı alır; klonu taşımak hiçbir şeyi kirletmez; `.md` değişimi derletmez
  - Çıktı kanıtı: `output missing`, `fed copy no longer matches` (paylaşılan kopya ezildiyse); çıktı yolu csproj'dan türetilir, SDK-style varsayılan yol da tanınır
  - Soğuk diskte ilk indeks ~40 sn (konsolda duyurulur); eski sürümden geçişte ilk Build her şeyi bir kez derler
  - Yayılım her zaman Safe: kirli projenin tüm geçişli bağımlıları da derlenir (Fast modunun UI'ı yok)
- Görünür yer: action bar Build, F5; satır karar etiketi
- Kaynaklar: ARCH §1.1, §1.2, §7.1, §7.2, §7.5, §7.6; README What it does, Using it 344-346; CHANGELOG 0.1.0, 1.4.0; merge dde3b670; arch 1, 12, 13, 20
- Önem gerekçesi: ürünün varlık sebebi
- WHY adayı: evet
  - Amaç: Her derlemede yalnız gerçekten değişen kısmı derleyip bekleme süresini kısaltmak.
  - Yaygın pratik: Çok projeli bir çözümde hangi projenin etkilendiği kolay görülmediği için geliştirici çoğu zaman çözümün tamamını ya da büyük bir kısmını yeniden derler; bazen de bir projeyi atlayıp eski bir DLL ile çalıştığını sonradan fark eder.
  - Build Orchestrator'da: Build'e basınca yalnız değişen ve ondan etkilenen projeler derlenir, gerisi atlanır; hiçbir şey değişmediyse koşu kısa sürede "nothing to build" der.

## 2. Bağımlılık sırası ve paralellik
- EN başlık önerisi: Dependency order, in parallel
- Ne yapar: Projeler gerçek bağımlılık sırasıyla, birbirini beklemeyenler aynı anda derlenir; bir projenin hatası ilgisiz projeleri durdurmaz; aynı depo her seferinde aynı sırayı üretir.
- Nasıl: Depo kökü altındaki tüm `.csproj`/`.sln` dosyaları ham XML olarak okunur, bağımlılıklar `ProjectReference` olmasa da HintPath'lerden çıkarılır; boşalan worker'a sıradaki hazır proje verilir.
- Etiketler: HintPath üretici haritası ile gerçek bağımlılık grafı; ready-set scheduler ile paralel derleme; ham XML okuma ile saniyeler içinde keşif; deterministik sıra ile tekrarlanabilir koşu
- Yüzler:
  - Sıfır yapılandırma keşif (`.git`, `bin`, `obj`, `node_modules`, `.vs` atlanır); değerlendirme önbelleği; proje → çözüm eşlemesi
  - Aynı AssemblyName'i üreten iki proje Sync'te adlandırılır, o DLL'in kenarı düşer
  - HintPath sınıflandırması (Edge / ExternalThirdParty / ExternalPlatformBin / Unclassified) ve sağlık metriği
  - Hata yalnız bağımlılarını bekletir; paralellik perf profilinden gelir (Full 6 / Balanced 4 / Light 2)
- Görünür yer: GRAPH, PROJECTS `build-order` etiketi, kaynak şeridi paralellik hücresi
- Kaynaklar: ARCH §1.2, §5.2, §6.1-§6.4, §8.2; README What it does, Using it 249-251; CHANGELOG 0.1.0; arch 11, 27, 28
- Önem gerekçesi: hız ve doğru sıranın kaynağı
- WHY adayı: evet
  - Amaç: Doğru sırayı elle takip etmeden, makinenin izin verdiği kadar paralel derlemek.
  - Yaygın pratik: Çözüm dosyaları gerçek bağımlılığı tam yansıtmadığında geliştirici projeleri bildiği sırayla tek tek derler, sırayı aklında ya da bir script'te tutar; sıra yanlışsa sorun genelde bir sonraki denemede ortaya çıkar.
  - Build Orchestrator'da: Sıra her seferinde depodan yeniden çıkar; birbirini beklemeyen projeler aynı anda derlenir, bir proje kırılsa da ilgisiz olanlar bitmeye devam eder.

## 3. Bağımlılık döngülerini çözme
- EN başlık önerisi: Dependency cycles, resolved
- Ne yapar: Birbirinin çıktısına referans veren projeler (döngü) plandan atılmaz; tek birim olarak sıraya girer ve okudukları API oturana kadar en az sayıda turda derlenir; tur ve sonuç ekranda görünür.
- Nasıl: Döngüler SCC olarak bulunur, grup tek imzayla kirli/güncel sayılır; ilk tur yalnız gereken üyeleri derler, bir üyenin okuduğu kardeş yüzeyi değişirse sonraki tur onu yeniden derler; tavan üç tur.
- Etiketler: Tarjan SCC ile döngü tespiti; bariyerli dalgalar ile yazılmakta olan DLL'i okumama; ApiSurfaceHash ile yakınsama kanıtı; carried üye ile gereksiz derlemeyi atlama
- Yüzler:
  - Normal Build kirli döngü gruplarını kendisi derler; Resolve cycles düğmesi opsiyoneldir (bkz. 29)
  - Ribbon `▸ Resolving cycles · round r/cap · n/m · elapsed`; konsol `cycle round R/K — N members`
  - Sonuç converged / no progress / round cap; yakınsamayan grupta oturanlar korunur, bozan üye kırmızı `failed`, kalanlar `skipped — cycle did not converge at this signature`
  - Aynı imzada yeniden basış gerçek denemedir (`retrying — did not converge at this signature`)
  - Sırasını bekleyen üye Queued (saat glyph'i, süre `—`); graf amber küp; proje logunda döngü yolu `A → B → C → A`; `decision.log` üye başına neden
  - VS'de derlenmiş üye çıktısı güncelse taşınır; Rebuild her üyeyi derler
- Görünür yer: ribbon, event stream, satır uyarı üçgeni, graf, konsol
- Kaynaklar: ARCH §6.5, §7.3-§7.6, §8.4, §8.8, §14.3; README Dependency cycles 422-509; CHANGELOG 1.2.0, 1.7.0, 1.8.0; arch 9
- Önem gerekçesi: döngülü depoda derlenebilirlik şartı
- WHY adayı: evet
  - Amaç: Döngüsel referanslı eski çözümleri elle sıralı tekrar derleme gerektirmeden derlenebilir tutmak.
  - Yaygın pratik: Projeler birbirinin DLL'ine referans verdiğinde geliştirici aynı projeleri birkaç kez, belli bir sırayla derler ve hangisinin eski DLL'e karşı derlendiğinden emin olamaz; bu sıra zamanla ekipte sözlü bir alışkanlığa dönüşür.
  - Build Orchestrator'da: Döngüdeki projeler tek Build'de, oturana kadar kendiliğinden yeniden derlenir; sonuç "oturdu" ya da "oturmadı, sorun şu projede" diye açıkça görünür.

## 4. Karar etiketi ve önizleme
- EN başlık önerisi: Why each project builds
- Ne yapar: Her satır, koşu başlamadan neden derleneceğini sabit kelimelerle söyler (`modified`, `modified · local`, `affected`, `never built`, `failed`, `up to date`); tooltip ayrıntıyı verir, etiket koşu sırasında canlı güncellenir.
- Nasıl: Sync, tam Build'in o an vereceği kararı önizleme olarak gönderir; projenin kendi dosyaları değiştiyse `modified`, yalnız bir bağımlılığı değiştiyse `affected`; `local` eki dosyanın git status'ta da kirli olduğunu söyler. Kırmızı yalnız bugünkü kaynak için kanıtlanmış derleyici hatasıdır.
- Etiketler: üç durumlu karar ile derlenecek/güncel/bilinmiyor ayrımı; içerik parmak izi ile modified/affected ayrımı; `git status --porcelain -z` ile `local` işareti; kanıta dayalı kırmızı ile yanlış alarmı önleme
- Yüzler:
  - Tooltip'ler: `No build output known to this tool`, `Failed at this source — Build will retry it`, `Up to date — built outside this tool`, `Its own files changed since the last build` (`— includes uncommitted edits`), `Its own files are unchanged — a dependency changed`, `Its copy in the shared folder does not match its build output`
  - Etiket saat ya da commit taşımaz
  - Kesilen, öldürülen, zaman aşan, Clean'i başarısız ya da güvenilmeyen döngü üyesi kırmızı değil gri `never built` okur
  - Başarı anında `up to date`; Sync'in *N changed* sayacı ile `modified` sayısı ayrışabilir; harici kök projeleri `local` almaz
- Görünür yer: proje satırının sağ bloğu + tooltip
- Kaynaklar: ARCH §5.3, §7.4, §7.5, §10.2, §13.2; README Using it 276-283; kod `ViewModels/DecisionLabel.cs:94-146`; arch 15, 16
- Önem gerekçesi: kararın açıklanabilirliği
- WHY adayı: evet
  - Amaç: "Bunu neden derliyor?" sorusunu derleme başlamadan cevaplamak.
  - Yaygın pratik: IDE bir projeyi neden yeniden derlediğini ya da neden atladığını söylemez; geliştirici şüphelenince temizleyip baştan derler ya da çıktı satırlarında neden arar.
  - Build Orchestrator'da: Her projenin yanında tek kelimelik bir gerekçe durur; kırmızı yalnız gerçekten derlenmeyen kod için yanar.

## 5. Bağımlılık sorunu görünür
- EN başlık önerisi: Dependency issues, made visible
- Ne yapar: Bağımlılığı başarısız olan proje o bağımlılığın son başarılı çıktısına karşı derlendiğinde satırdaki amber üçgen bunu söyler; böyle bir proje her Build'de boşuna yeniden derlenmez, kök düzelince derlenir.
- Nasıl: Başarısız kök projelerin adları bağımlılar boyunca taşınır ve deftere not edilir; sonraki Build notlu projeyi şartlı alır, kök düzeldiyse derler, hâlâ bozuksa `skipped — dependency still failing` der.
- Etiketler: kök notu ile hedefli yeniden derleme; şartlı yeniden derleme ile boşa turu önleme; tek amber uyarı kanalı; `⚠` chip + filtre ile toplu görme
- Yüzler:
  - Tooltip öncelik sırası: `Cycle did not converge — its projects are still out of date` › `Cycle did not fully settle — output may be one generation stale` › `In a dependency cycle` › `Dependency issue: Sales.Core +2`
  - Üçgen asla kırmızı değildir (kırmızı = projenin kendisi kırıldı)
  - Satırdan derlenen projede derlenmeyen kirli bağımlılık: `X has pending changes and was not rebuilt in this run`
  - Event stream `built — dependency issue (2.4s)`; ribbon `(N dependency-affected)`, `(N stuck in a cycle)`; proje logunun başında uyarı satırı; konsol başlığında rozet
  - VS'de düzelen kök de tanınır
- Görünür yer: satır uyarı yuvası, `⚠ N` chip, ribbon özeti, proje logu
- Kaynaklar: ARCH §5.3, §7.4-§7.6, §8.3, §13.2, §14.3; README Using it 283-285, 396-399; CHANGELOG 1.5.0; arch 17, 60
- Önem gerekçesi: gizli bayat bağımlılığı ortaya çıkarır
- WHY adayı: evet
  - Amaç: Bozuk bir bağımlılığa karşı derlenmiş projeyi görünür kılmak ve boşuna tekrar derlememek.
  - Yaygın pratik: Bir kütüphane derlenmediğinde ona bağlı projeler eski DLL ile derlenmeye devam eder ve uygulama çalışır göründüğü için sorun geç fark edilir; ya da her denemede bütün zincir yeniden derlenir.
  - Build Orchestrator'da: Hangi projenin bozuk bir bağımlılığın arkasında beklediği tek bakışta görünür; bağımlılık düzelince o projeler kendiliğinden sıraya girer.

## 6. API yüzeyi kapısı
- EN başlık önerisi: Skip when the API holds
- Ne yapar: Bir bağımlılıkta yalnız metot gövdesi değiştiyse bağımlı projeler derlenmez (`skipped — up to date (no dependency surface changed)`); public API değiştiyse derlenir.
- Nasıl: Sırası gelen adayda her doğrudan bağımlılığın derlenmiş çıktısının bildirim yüzeyi (gövde değil) hash'lenir ve projenin en son neye karşı derlendiğiyle karşılaştırılır; hepsi aynıysa atlanır, satır hemen yeşile döner.
- Etiketler: ApiSurfaceHash ile bildirim karşılaştırması; aday seçimi ile yalnız bağımlılığı değişen projeye bakma; kaskad kesme ile zaman kazancı
- Yüzler:
  - Yalnız Build ve döngü koşusunda; Rebuild ve satır koşusunda değil
  - Aday sırası gelene dek derlenecek sayılır (dalga, kuyruk, sayaç); önizlemede `conditional`
  - WPF XAML yükleyici yardımcısı hash dışı; skip nedeni event stream ve `decision.log`'da
- Görünür yer: otomatik; event stream skip satırı
- Kaynaklar: ARCH §7.3, §7.5, §8.3; README Using it 348-354; CHANGELOG 1.8.0; arch 18
- Önem gerekçesi: gereksiz kaskadı keser
- WHY adayı: evet
  - Amaç: Alt kütüphanedeki küçük bir iç değişikliğin üstündeki her projeyi derletmesini önlemek.
  - Yaygın pratik: Paylaşılan bir kütüphanede tek satırlık düzeltme yapıldığında ona bağlı bütün projeler yeniden derlenir; çok katmanlı çözümlerde bu tek değişiklik uzun bir bekleyişe dönüşür.
  - Build Orchestrator'da: İç değişiklikte yalnız değişen kütüphane derlenir, üstündekiler yeşil kalır; public bir imza değişince bağımlılar da derlenir.

## 7. Visual Studio ile yan yana
- EN başlık önerisi: Works alongside Visual Studio
- Ne yapar: Visual Studio'da ya da komut satırında derlenmiş proje gereksiz yere yeniden derlenmez, yeşil `built outside this tool` okur; ama bu Build'de derlenecek bir bağımlılığın arkasındaysa aynı Build'de `affected` olarak derlenir.
- Nasıl: Projenin çıktısı aracın son koşusundan yeniyse çıktı başkasınındır; o zaman hiçbir girdi çıktıdan yeni değilse güncel sayılır. Tarih tek başına yetmez, girdilerle karşılaştırılır.
- Etiketler: zaman kipi ile yabancı çıktıyı tanıma; girdi-çıktı tarih karşılaştırması ile çifte derlemeyi önleme; upstream kaskadı ile bayat kopyayı yakalama
- Yüzler:
  - Zaman kipinde kırmızı yok; döngü üyesine de uygulanır (grup bölünmez)
  - Dosyaya dokunup geri almak ya da derlemeden branch'ler arası gidip gelmek hiçbir şeyi derletmez
  - Boş proje sayfası `Up to date — built outside this tool.`
- Görünür yer: satır etiketi ve tooltip
- Kaynaklar: ARCH §7.6; README Using it 272-276; CHANGELOG 1.6.0, 1.8.0; arch 19
- Önem gerekçesi: VS ile birlikte kullanım şartı
- WHY adayı: evet
  - Amaç: Geliştiricinin IDE'de yaptığı derlemeyi boşa saymamak.
  - Yaygın pratik: IDE dışında bir derleme aracı kullanan geliştirici, IDE'de zaten derlediği projeleri araç yeniden derlediği için ikisi arasında seçim yapar ya da birini bırakır.
  - Build Orchestrator'da: Visual Studio'da derlenen projeler yeşil görünür ve atlanır; yalnız VS'nin görmediği bir bağımlılık değişikliği varsa yeniden derlenir.

## 8. MSBuild.exe ile derleme
- EN başlık önerisi: Built the way VS builds
- Ne yapar: Her proje Visual Studio'nun kullandığı MSBuild.exe ile ayrı bir süreçte derlenir; legacy .NET Framework projeleri sorunsuz derlenir, MSBuild bulunamazsa sessiz asılma yerine açık hata verilir.
- Nasıl: MSBuild.exe vswhere ile bulunur, her proje kendi argümanlarıyla çocuk süreç olarak çağrılır (`-t:Build -p:Configuration=… -p:BuildProjectReferences=false -nodeReuse:false -p:UseSharedCompilation=false`); `dotnet build` ve in-process MSBuild yok.
- Etiketler: vswhere ile MSBuild'i bulma; proje başına süreç ile yalıtım; `BuildProjectReferences=false` ile çift derlemeyi önleme; `msbuild -t:restore` ile nuget.exe'siz restore
- Yüzler:
  - `packages.config` projeleri derlemeden önce restore edilir; dosya değişmemiş ve paketler yerindeyse atlanır, Rebuild her zaman restore eder
  - WPF geçici assembly'si yalnız metadata derlenir: WPF derlemesi kısalır, çıktı byte-aynı
  - Paylaşılan çıktı klasöründe MSB302x kopya çakışmasında geri çekilip yeniden dener
  - Türkçe Windows'ta MSBuild çıktısı bozulmaz; süreler her yerde `4.2s` biçiminde
  - Proje logunun ilk satırı gerçek MSBuild komut satırı; çıkış kodu süreç tam bittikten sonra okunur
- Görünür yer: proje logunun ilk satırı; About → Environment MSBuild yolu
- Kaynaklar: ARCH §2, §3.1, §4.1, §9.1-§9.6; README Requirements, Rebuild; CHANGELOG 1.8.0, 1.10.0; arch 21, 22, 24, 25, 26
- Önem gerekçesi: VS ile aynı sonuç
- WHY adayı: evet
  - Amaç: Derleme sonucunun Visual Studio'nunkiyle aynı olmasını sağlamak.
  - Yaygın pratik: Script ile derleyen ekipler farklı bir derleme komutu ya da farklı argümanlar kullandığında "VS'de derleniyor, script'te derlenmiyor" türü farklar çıkar; eski .NET Framework projelerinde bu daha sık görülür.
  - Build Orchestrator'da: Projeler Visual Studio'nun derlediği gibi derlenir; eksik paketler kendiliğinden gelir, makinede ek bir paket aracı gerekmez.

## 9. Build, Rebuild, Clean komutları
- EN başlık önerisi: Build, Rebuild and Clean
- Ne yapar: Ana düğme Build'dir; açılır menü *Build — Only stale projects*, *Rebuild — All N projects — cache ignored*, *Clean — Remove build outputs — next build is full* sunar; Debug | Release segmenti konfigürasyonu değiştirir.
- Nasıl: Her koşu kökte checkout'lu çalışma ağacını seçili konfigürasyonla yeniden planlar; Rebuild önbelleği yok sayıp her projeyi derler; Clean her projede `msbuild -t:Clean` çalıştırır (VS Clean Solution gibi), önbelleklere dokunmaz.
- Etiketler: split button ile tek yerden komut; `-t:Clean` ile VS Clean Solution eşdeğeri; konfigürasyonun imzada olması ile Debug/Release farkını doğru kirletme; tek iş kapısı ile çakışan işi reddetme
- Yüzler:
  - F5 / F6 / F7 badge'leri; koşu sırasında düğme Stop olur
  - Ayrı *Continue* / *Retry failed* yok — Build ikisini kapsar
  - Debug | Release değişimi Sync başlatır, konsolun ilk satırı `Configuration → Release`; başka konfigürasyonda derlenmiş proje kirlenir, derlemeden geri dönmek kirletmez; iş sürerken kilitli
  - Clean: onay yok, Stop durdurur, `Clean started — N projects, parallelism P`, temizlenen satır `never built`; harici ve döngü üyeleri dahil
  - Tek iş kapısı: Sync / Clean / Optimize / checkout / pull sürerken Build, Rebuild, Clean, Resolve cycles, satır eylemleri, F5 ve global Build kısayolu başlamaz; basış bekletilmez, reddedilir (dim düğme + tooltip, tepsideyken balon); ilk Sync'ten önce Build yok
  - Bu Clean, bakım kutusundaki Clean'den farklıdır (bkz. 29)
- Görünür yer: action bar sağ uç split-button; Debug | Release segmenti; F5/F6/F7
- Kaynaklar: ARCH §5.2, §5.4, §7.2, §8.1, §9.2, §9.4, §13.2; README Build commands 348-362; kod `Views/BuildMenu.xaml.cs:122-126`; CHANGELOG 1.1.0, 1.7.0, 1.8.0; arch 2, 3, 4, 14, 35
- Önem gerekçesi: temel komut seti
- WHY adayı: hayır

## 10. Yetim süreç bırakmaz
- EN başlık önerisi: No orphaned build processes
- Ne yapar: Uygulama kapatılınca, çökünce ya da Task Manager'dan öldürülünce hiçbir MSBuild.exe ya da onun çocuğu arkada kalmaz; MSBuild ne kadar çıktı üretse de arayüz takılmaz.
- Nasıl: Arayüz, motor (Supervisor) ve MSBuild iç içe Windows Job Object'lerindedir; son tutamaç kapanınca işletim sistemi tüm ağacı sonlandırır. Her çocuk askıda başlatılır, job'a atanır, sonra çalışır.
- Etiketler: nested Job Object ile yetim süreç engelleme; `KILL_ON_JOB_CLOSE` ile kaskad sonlandırma; `CREATE_SUSPENDED` ile yönetimsiz anı kapatma; ayrı motor süreci ile akıcı arayüz
- Yüzler:
  - Ölçülen sınır iki saniyenin altında
  - Paralel başlatmalar birbirinin pipe'ını sızdırmaz; her MSBuild/restore çocuğu bir zaman aşımına bağlı
  - Kullanıcı adına açılan Explorer ve Visual Studio job dışındadır, uygulama kapanınca yaşar
- Görünür yer: arka plan (garanti)
- Kaynaklar: ARCH §1.2, §3.2, §4.1-§4.4, §17.3; CHANGELOG 0.1.0; arch 36, 37, 38
- Önem gerekçesi: makinede çöp süreç kalmaz
- WHY adayı: evet
  - Amaç: Derleme durdurulduğunda ya da araç kapandığında makinede derleyici kalmamasını garanti etmek.
  - Yaygın pratik: Yarıda kesilen bir derlemeden sonra arkada kalan derleyici süreçleri dosyaları kilitler; geliştirici bunları Task Manager'dan bulup kapatır ya da makineyi yeniden başlatır.
  - Build Orchestrator'da: Uygulama nasıl kapanırsa kapansın derleyiciler de birkaç saniye içinde kapanır; sonraki derleme kilitli bir dosyaya takılmaz.

## 11. Güvenli durdurma ve devam
- EN başlık önerisi: Stop safely, then continue
- Ne yapar: Koşu sürerken Build düğmesi Stop olur; ilk basış yeni iş başlatmaz ve derlenenleri bitirir, ikinci basış (*Stop now* / Esc) hepsini hemen keser; durdurma yarım yazılmış DLL bırakmaz; sonra Build yalnız kalanı derler.
- Nasıl: Graceful stop proje sınırında (post-build kopyalar dahil) bekler ve bitenleri kaydeder; hard stop tüm iç job'ı sonlandırır ve kesilen projelerin kaydını geçersizler ki sonraki Build onları sıfırdan derlesin.
- Etiketler: proje sınırında drain ile tutarlı çıktı; `TerminateJobObject` ile anlık kesme; kayıt geçersizleme ile doğru devam; tıklamada anlık geri bildirim
- Yüzler:
  - Düğme *Stop* → *Stop now* → *Terminating…*; satır Stop ikonu ve tepsi menüsü Stop aynı etiketleri taşır
  - Ribbon `▸ Stopping — wrapping up` ve kaç projenin bitmeyi beklediği; hard stop'ta konsol kaç derlemenin kesildiğini yazar
  - Komut motora gitmeden basılan Stop koşuyu tamamen iptal eder (`Cancelled — build not started`); planlama sırasında ribbon `▸ Starting — resolving what to build`, Stop o an da geçerlidir
  - Drain başlayınca CPU sınırı kalkar; elapsed sayaç yeni Build'de sıfırdan başlar
- Görünür yer: action bar Stop; Esc; satır Stop ikonu; tepsi menüsü Stop
- Kaynaklar: ARCH §1.2, §4.5, §8.8, §13.2, §13.7; README What it does 24-26, Using it 376-391; CHANGELOG 0.1.0, 1.1.0, 1.8.0; arch 39
- Önem gerekçesi: temel kontrol
- WHY adayı: evet
  - Amaç: Derlemeyi istenen anda bozuk çıktı bırakmadan durdurmak ve kaldığı yerden sürdürmek.
  - Yaygın pratik: Uzun bir derlemeyi yarıda kesmek çoğu zaman yarım kalmış çıktılar ve "hangi proje bitti" belirsizliği bırakır; geliştirici güvenmek için sonrasında her şeyi yeniden derler.
  - Build Orchestrator'da: Stop'a basınca derlenmekte olan projeler temiz biter; yeniden Build'e basınca yalnız bitmemiş olanlar derlenir.

## 12. Yarım kalan iş toparlanır
- EN başlık önerisi: Interrupted work recovers itself
- Ne yapar: Motor koşu ortasında ölürse (çökme, Task Manager, oturum kapanması) ya da koşu sırasında branch değişirse yarım kalan projeler bitmiş sayılmaz, sonraki Build onları yeniden derler; ayar ve defter dosyaları yarım yazılmış kalmaz.
- Nasıl: O an derlenen projeler bir in-flight defterine yazılır, sonraki başlangıçta kalanlar geçersizlenir; HEAD hareket ederse koşu kesilir ve sonraki sonuçlara güvenilmez; dosyalar geçici dosya + yeniden adlandırma ile atomik yazılır.
- Etiketler: `run-inflight.json` ile yarım çıktı tespiti; HEAD izleme ile branch değişiminde kesme; atomik yazım ile bozuk dosya önleme; bozuk dosyaya tolerans ile açılabilen uygulama
- Yüzler:
  - Konsol `previous run was interrupted; N projects will rebuild`
  - Branch kesmesi: `Run interrupted by a branch change — N built, M not built · logs: <folder>`, event stream `interrupted by branch change`; commit koşuyu kesmez
  - Bozuk build-state/önbellek boşa düşer, `ui-state.json` varsayılana döner, tipi değişen tek alan dosyayı götürmez; değişmeyen dosya yeniden yazılmaz
- Görünür yer: konsol açılış satırı; gri `never built` satırlar
- Kaynaklar: ARCH §4.7, §5.3, §7.5, §8.7, §8.8, §10.3, §16, §21.3; README State on disk 714-717, Using it 306-309; arch 33, 34, 110
- Önem gerekçesi: kesintiye dayanıklılık
- WHY adayı: evet
  - Amaç: Beklenmedik bir kesintiden sonra "neyin derlendiğine güvenebilirim" sorusunu ortadan kaldırmak.
  - Yaygın pratik: Derleme sırasında bilgisayar kilitlenir ya da derleme kapatılırsa yarım yazılmış çıktılar kalır; geliştirici emin olmak için temizleyip hepsini yeniden derler.
  - Build Orchestrator'da: Kesintiden sonraki açılışta hangi projelerin yeniden derleneceği söylenir ve yalnız onlar derlenir.

## 13. Çıktı klasörüne dokunmaz
- EN başlık önerisi: Output folders left untouched
- Ne yapar: Derleme çıktısı tam olarak Visual Studio'nun yazacağı klasöre düşer; araç hiçbir çıktı yolunu değiştirmez, OutDir'e kendisi yazmaz, kopyalamaz, silmez; her koşu kökte o an checkout'lu tek çalışma ağacını derler.
- Nasıl: MSBuild'e OutDir / OutputPath / obj yolu geçilmez, çıktı yalnız karar için okunur; projelerin kendi post-build kopyaları çıktıyı VS'deki gibi dağıtır. Tek istisna kullanıcının bastığı Clean'dir (MSBuild projenin kaydettiği çıktıyı siler).
- Etiketler: çıktı yolunu değiştirmeme ile VS ile aynı düzen; tek çalışma ağacı ile ayrı kopya/worktree yok; yalnız okuma ile yan etkisizlik
- Yüzler:
  - Branch'ler arası çıktı yalıtımı yok (S2); başka branch derlemek = checkout
  - Proje kimliği tam csproj yolu; derlendiği yer ile aynı
- Görünür yer: arka plan (garanti)
- Kaynaklar: ARCH §1.2, §9.4, §20; README What it does 26-29; CLAUDE.md değişmezleri; arch 23
- Önem gerekçesi: VS ile aynı çıktılar
- WHY adayı: evet
  - Amaç: Aracı eklemenin projenin çıktı düzenini ve dağıtım adımlarını değiştirmemesi.
  - Yaygın pratik: Bazı derleme script'leri çıktıyı ayrı bir klasöre yönlendirir ya da kopyalar; IDE ile script farklı yerlere yazınca hangi DLL'in güncel olduğu karışır.
  - Build Orchestrator'da: Çıktılar her zaman Visual Studio'nun koyacağı yerdedir; aracı kullanmaya başlamak ya da bırakmak çıktı düzeninde hiçbir şeyi değiştirmez.

## 14. Git'e kendiliğinden yazmaz
- EN başlık önerisi: Never writes to git uninvited
- Ne yapar: Sync, Build ya da arka plan işi depoya asla yazmaz; pull, reset, switch, rebase, commit, push, worktree hiçbir akışta çalışmaz; git'e yazan üç hareketin her biri kullanıcının açık kararıdır.
- Nasıl: Üç yazma yolu (N behind chip'inden ff-only pull, branch chip'inden checkout, açık bırakılan harici kök ff-only güncellemesi) tek dosyada toplanır, bir kaynak testi git yazma fiillerini başka yerde yasaklar; okuma (status, refs, fetch, reflog) ayrı yoldan yapılır.
- Etiketler: tek git yazıcı ile denetlenebilirlik; kaynak guard'ı ile kazara yazmayı önleme; ff-only ile tarih yazmama; ref-only fetch ile çalışma ağacına dokunmama
- Yüzler:
  - Fetch her zaman `--no-tags` ve yalnız o branch
  - Stash yalnız ayar açıkken; araç stash'i asla pop etmez
  - Git işlemi ortasında checkout/pull kilitli; `index.lock` asla silinmez
- Görünür yer: N behind chip, branch chip, Settings → General → Pull before build / Stash and switch branches
- Kaynaklar: ARCH §1.2, §10.1, §17.2, §21.3; CLAUDE.md değişmezleri; README Using it 288-294; arch 44
- Önem gerekçesi: en kritik güven garantisi
- WHY adayı: evet
  - Amaç: Derleme aracının geliştiricinin git geçmişine ve commit'lenmemiş işine asla dokunmaması.
  - Yaygın pratik: Derlemeden önce "güncel olsun" diye otomatik pull ya da reset yapan script'ler zaman zaman yerel değişiklikleri ezer ya da beklenmedik merge'ler bırakır; bu yüzden ekipler derleme script'ine git adımı koymaktan çekinir.
  - Build Orchestrator'da: Depoya yalnız kullanıcı bir chip'e tıkladığında ve yalnız güvenli yoldan yazılır; derleme ve Sync depoyu sadece okur.

## 15. Motor sağlığı ve kurtarma
- EN başlık önerisi: Engine health and recovery
- Ne yapar: Motor ölür, takılır ya da uyanamazsa ribbon bunu adlandırır ve tek tıkla *Restart engine* sunar; motor hazır olunca `Engine ready — v<version>` yazılır ve başlangıç Sync'i çalışır.
- Nasıl: Arayüz motor sürecini izler; cevap beklenen pencerelerde (koşu başı, stop, Sync, bakım, checkout, pull) motor susarsa amber uyarı çıkar; kilitler kendiliğinden açılmaz, karar kullanıcınındır.
- Etiketler: süreç izleme ile ölüm tespiti; cevap-borcu penceresi ile sahte alarmı önleme; tek tık yeniden başlatma; nesil sayacı ile eski motorun olaylarını düşürme
- Yüzler:
  - `Engine stopped unexpectedly (exit N)` / `(protocol error)` / `(wake failed)` / `(idle shutdown failed)`; motor dosyası yoksa *Engine missing … reinstall required*
  - Her git okuması 30 sn ile sınırlı; uzun restore sırasında heartbeat
  - Restart motoru ve altındaki her MSBuild'i kapatıp yenisini başlatır
  - Eski sürümlerden kalan worktree klasörü varsa oturum başına bir kez ipucu (`git worktree prune`)
- Görünür yer: ribbon uyarı satırı + Restart engine
- Kaynaklar: ARCH §4.6, §4.7, §12.1, §13.2, §16; README Using it 591-597, State on disk 725-727; kod `Views/StickyRibbon.xaml:61-66`; arch 40, 41, 43
- Önem gerekçesi: takılmayı kullanıcıya bırakmaz
- WHY adayı: hayır

## 16. Dört panelli ana pencere
- EN başlık önerisi: One window, four panels
- Ne yapar: Bağımlılık grafı, proje listesi, konsol ve event stream tek pencerededir; üç düzen (Quad, List, Focus) ve sürüklenebilir ayraçlar vardır, seçilen düzen kalıcıdır.
- Nasıl: Title bar · sticky ribbon · sol sütun GRAPH / PROJECTS, sağ sütun CONSOLE / EVENT STREAM · alt action bar; düzen ve ayraç konumları `ui-state.json`'da, gizli graf çizim yapmaz.
- Etiketler: tek bakışta durum; düzen modları ile odak seçimi; kalıcı ayraçlar ile kişisel düzen; özel koyu pencere çerçevesi
- Yüzler:
  - Quad "Default layout — graph + list · console + stream (splits reset)", List "List layout — graph hidden · console + stream equal", Focus "Console focus — graph hidden · compact stream"
  - Title bar: solda ürün logosu, ortada kaynak şeridi, sağda Update pill → düzen → ⚙ Settings → ✦ What's new → i About → ⏻ Exit; branch chip, N behind ve perf chip alt action bar'da
  - Action bar'da workspace adı (tooltip tam kök yolu)
  - Panel başlık sayaçları `N projects · N dependencies`, `N lines`, `N events`
  - Pencere maximize açılır (restore 1400×800, en küçük 1240×620), Windows 11 yuvarlak köşe, farklı ölçekli monitöre geçince düzelir
- Görünür yer: ana pencere; title bar düzen düğmeleri
- Kaynaklar: ARCH §12.2, §12.4, §13.2, §13.6; README What it does 23-24; kod `MainWindow.xaml:151-187`; CHANGELOG 0.2.0, 1.1.0; arch 49, 50, 51, 57, 58, 131
- Önem gerekçesi: ekranın iskeleti
- WHY adayı: hayır

## 17. Proje listesi ve renk dili
- EN başlık önerisi: Project list at a glance
- Ne yapar: Her satır projenin durumunu renk şeridi, nokta, glyph ve metinle; adını, karar etiketini, uyarısını ve süresini tek satırda gösterir; yeşil güncel, düz gri derlenecek, kırmızı derleyici hatası; derlenen satır amber "nefes" alır.
- Nasıl: Satır şeridi, nokta, glyph ve graf düğümü tek bir durum değerinden boyanır; koşu sırasında liste derlenen sınır satırını görünürde tutar, kullanıcı kaydırınca bırakır, 5 sn dokunulmazsa döner.
- Etiketler: renk + glyph + metin ile renk körü dostu durum; tek durum kaynağı ile tutarlı yüzeyler; follow-mode ile koşuyu izleme; yapışkan katman başlıkları ile yön bulma
- Yüzler:
  - İlk Sync'ten önce start mode (dört yaylı halka, kesikli glyph); Queued (saat), Building (dönen halka), Succeeded, Failed (✗), Skipped; atlanan proje ayrı bir renk değildir
  - Hata veren satır bir kez titrer
  - Katman başlıkları kaydırırken yığılır; başlığa tık `Jump to <layer>` ile o grubun başına kaydırır (yalnız fare)
  - Yapı aynıysa otomatik Sync satırları yerinde günceller; yapı değiştiyse kademeli açılış
  - Tek seçim jesti: satır, graf düğümü ya da stream satırı her panelde aynı projeyi seçer; aynı öğe, Back ya da Esc temizler
  - Tab / ok / Enter ile klavyeyle gezilir; sağ tık seçim değil menü açar
- Görünür yer: PROJECTS paneli
- Kaynaklar: ARCH §13.2, §13.4, §13.6, §13.7, §14.3, §15; README Using it 272-276, 393-396, 418-420; CHANGELOG 0.2.0, 1.1.0, 1.5.0, 1.6.0; arch 59, 62, 63, 64, 77, 121
- Önem gerekçesi: ana gezinme yüzeyi
- WHY adayı: hayır

## 18. Listeyi süzme
- EN başlık önerisi: Filter by status or name
- Ne yapar: Action bar sayaç chip'leri aynı zamanda filtredir (Σ, building, ✓ up to date, ✗ failed, ⚠); chip'ler birleşir; PROJECTS başlığındaki kutu (Ctrl+F) listeyi proje adına göre daraltır; graf da eşleşerek soluklaşır.
- Nasıl: Chip'ler kendi aralarında VEYA, arama kutusuyla VE ile birleşir; ad araması büyük/küçük harf duyarsız alt dizedir ve kısa bir gecikmeyle uygulanır; chip rozeti filtrelenince kalan satır sayısıdır.
- Etiketler: sayaç chip'i ile tek tıkla durum filtresi; VEYA/VE birleşimi ile hedefli liste; ad filtresi ile hızlı bulma
- Yüzler:
  - Σ "All projects — clear filters"; ⚠ yalnız uyarı varken görünür
  - Aktif filtre başlıkta kaldırılabilir chip; ✕ ve Esc kutuyu temizler, dışarı tık odağı bırakır
  - Eşleşme yoksa `No projects match this filter.`
  - Ribbon'daki hatalı proje chip'leri ✗ filtresini açar
  - Filtre build ve Sync boyunca kalır; koşu sırasında graf tam gösterilir, bitince filtreye döner
- Görünür yer: action bar sayaç chip'leri; PROJECTS başlığı; Ctrl+F
- Kaynaklar: ARCH §13.2, §13.7; README Using it 399-404; kod `ViewModels/ProjectFilter.cs:27-61`; CHANGELOG 1.3.0, 1.4.0, 1.6.0; arch 66
- Önem gerekçesi: büyük listede odak
- WHY adayı: hayır

## 19. Satır eylemleri
- EN başlık önerisi: Row actions: build, open, reveal
- Ne yapar: Satırın üstüne gelince ▶ Build this project, ⋯ (Build · Rebuild · Clean), Reveal in Explorer ve Open in Visual Studio ikonları çıkar, sağ tık aynı menüyü açar; satırdan başlatılan koşu yalnız o projeyi, değişmemiş olsa da derler.
- Nasıl: Planlama tam yapılır ama koşu tek projeye kesilir; bağımlılıklar derlenmez, bayat bağımlılık satırda ve logda bağımlılık sorunu olarak raporlanır. Open in Visual Studio projenin çözümünü yeni bir VS'de açar, proje birden çok `.sln`'deyse hangisi diye sorar; Reveal in Explorer csproj'u seçili gösterir.
- Etiketler: tek proje kapsamı ile hızlı deneme; bayat bağımlılık notu ile gizli DLL riskini kapatma; vswhere ile devenv'i bulma; çözüm eşlemesi ile doğru `.sln`'i açma
- Yüzler:
  - Satır Rebuild = MSBuild `-t:Rebuild`; satır Clean = proje `-t:Clean`
  - Koşu sırasında hedef satırın ▶'si kırmızı Stop olur, diğerleri `Build in progress — wait or stop it first`
  - Satırdan başlatmak seçimi düşürür, filtreyi korur; "bağımlılıklarıyla derle" bilinçli olarak yok
  - VS yoksa `Visual Studio not found — install it to open <name>`; açılan VS/Explorer uygulama kapanınca yaşar; çalışan VS'ye bağlanılmaz
  - Seçici başlığı OPEN IN VISUAL STUDIO
- Görünür yer: satır hover ikonları; ⋯ menüsü; sağ tık
- Kaynaklar: ARCH §5.2, §6.3, §6.5, §8.1, §13.2, §13.3; README Using it 406-416, Requirements 84; kod `Views/ProjectRowActions.xaml`, `ViewModels/RunViewModel.cs:2978-3027`; CHANGELOG 0.2.0, 1.4.0, 1.5.0, 1.8.1; arch 10, 61
- Önem gerekçesi: tek projeye hızlı erişim
- WHY adayı: evet
  - Amaç: Tek bir projeyi bütün koşuyu beklemeden derlemek ve koda tek tıkla geçmek.
  - Yaygın pratik: Tek bir projeyi denemek için geliştirici IDE'de doğru çözümü açar, projeyi Solution Explorer'da bulup derler; proje birden çok çözümde yer alıyorsa önce hangisini açacağını hatırlaması gerekir.
  - Build Orchestrator'da: Listede projenin yanındaki düğmeyle yalnız o proje derlenir; aynı satırdan çözümü Visual Studio'da ya da klasörü Explorer'da açmak bir tık.

## 20. Bağımlılık grafı
- EN başlık önerisi: The dependency graph
- Ne yapar: Tüm projeleri adsız küçük düğümler olarak build sırasına göre bantlarda gösterir ve her zaman panele sığar; düğüm seçilince onu ve doğrudan bağımlılık/bağımlılarını sığdırıp bağımlılık çizgilerini çizer; koşu grafta sakin bir opaklık anlatısıyla izlenir.
- Nasıl: Bantlar listenin gruplarıyla aynıdır (External en üstte, katman yoksa tek bant); düğüm aralığı sığana dek daralır, kalıcı kenar ağı yoktur; koşuda bekleyenler söner, derlenenler parlar ve etraflarında amber noktalar döner.
- Etiketler: her zaman sığan düzen ile scrollbar'sız topoloji; focus-and-fit ile bağımlılık inceleme; opaklık anlatısı ile koşuyu izleme; liste–graf ortak hover ile eşleştirme
- Yüzler:
  - Düğüm hover'ı büyür, gecikmesiz tooltip'te tam ad, liste satırı da vurgulanır (ve tersi)
  - Seçimde amber odak halkası + ad etiketi, akan amber kesikli çizgiler (yukarıda bağımlılıklar, aşağıda bağımlılar)
  - Tekerlek imleçte zoom, boş zeminde sürükleyerek pan, boş zemine tık seçimi bırakır ya da varsayılan görünüme döner; Build başında kamera bir kez sığdırır, sonra sabit
  - Biten proje sonuç rengiyle kısa parlar, atlanan proje sessiz kalır
  - Bitiş finali: bu koşuda derlenen düğümler floresan gibi tek tek yanar, hiçbir şey derlenmediyse oynamaz
  - Start-mode düğümleri kesikli; döngü üyesi amber küp; yüzlerce projede düğümler en küçük boya iner (S13)
- Görünür yer: GRAPH paneli
- Kaynaklar: ARCH §6.6, §13.6, §14.5, §20; README Dependency graph 542-563; CHANGELOG 0.2.0, 1.2.0, 1.4.0, 1.6.0, 1.10.0; arch 72, 73, 74, 75, 76, 79
- Önem gerekçesi: bağımlılığı görmenin yolu
- WHY adayı: evet
  - Amaç: Yüzlerce projenin birbirine nasıl bağlı olduğunu tek bakışta görebilmek.
  - Yaygın pratik: Bir projenin nelere bağlı olduğunu ve neyi etkilediğini öğrenmek için geliştirici proje dosyalarını tek tek açar ya da bunu ekipte uzun süredir çalışan birine sorar.
  - Build Orchestrator'da: Bir projeye tıklayınca bağlı olduğu ve onu kullanan projeler grafta öne çıkar; derleme sırasında hangi projenin derlendiği grafta da görülür.

## 21. Konsol ve proje logu
- EN başlık önerisi: Console and project logs
- Ne yapar: Konsol koşunun anlatısını renkli, seçilebilir düz metin olarak gösterir; Compact console her projeden yalnız işe yarayanı alır (hatalar tam, ilk üç farklı uyarı, kalanların koda göre sayısı); bir projeye tıklamak konsolu o projenin tam loguna çevirir.
- Nasıl: Proje logları ve konsol anlatısı her koşunun klasörüne diske yazılır; konsol ekranda sınırlı satır tutar, yukarı kaydırınca diskten geri yükler; proje sayfası diskten anlık görüntü alıp canlı satırları ona ekler.
- Etiketler: disk-önce log ile kayıpsız geçmiş; Compact console ile uyarı gürültüsünü azaltma; proje sayfası ile hatayı baştan okuma; sabit bellek bütçesi ile uzun koşu
- Yüzler:
  - Renk yalnız biçimi bilinen satırlara: `error CS…` kırmızı, `warning MSB…` amber
  - Başlık: `CONSOLE` + `N lines` + `compact`; proje sayfasında Back, proje adı, durum glyph'i, bağımlılık rozetleri, Copy log
  - Proje logu baştan açılır ve takip etmez (ilk hata önünde), `⌄ latest` bir kez sona götürür; logu olmayan proje de sayfa açar ve nedenini söyler (`Up to date — nothing to compile in this run.`, `Never built by this tool`…)
  - Özet örneği `App.Core: 214 more warnings — CS1591 ×180, CS0618 ×30, MSB3277 ×4 — see the project log`; Compact console değişimi sonraki koşudan geçerli
  - Yatay kaydırma (tekerlek, touchpad, ←/→), PageUp/Down/↑/↓/Home/End; kullanıcı kaydırınca gelen satırlar çekmez, ~5 sn sonra döner; altta yanıp sönen caret ve `ready`
  - Her işlem yeni bölüm açar (manuel Sync, branch ve konfigürasyon değişimi temizler; otomatik Sync ve pull ekler)
  - Loglar `%LOCALAPPDATA%\BuildOrchestrator\logs\run-<timestamp>\` altında, 3 gün tutulur, en yenisi hep kalır; `decision.log` derleme kararlarını tutar; motor uyurken de son koşunun logları açılır
- Görünür yer: CONSOLE paneli; Settings → General → Compact console
- Kaynaklar: ARCH §5.1-§5.5, §8.5, §13.4, §13.5, §16; README Console and logs 511-540, State on disk; kod `Console/ConsoleHeader.xaml`; CHANGELOG 0.2.0, 1.3.0, 1.10.0, 1.10.1; arch 67, 68, 69, 70, 71
- Önem gerekçesi: hatayı bulmanın yeri
- WHY adayı: evet
  - Amaç: Derleme hatasını yüzlerce uyarı satırı arasında aramadan bulmak.
  - Yaygın pratik: Büyük bir çözümün derleme çıktısı binlerce uyarı satırı içerir; geliştirici hatayı bulmak için çıktıyı kaydırır, arar ya da bir dosyaya kopyalayıp orada inceler.
  - Build Orchestrator'da: Konsolda hatalar tam, uyarılar özet olarak görünür; bir projeye tıklayınca logu baştan açılır ve ilk hata hemen önündedir.

## 22. Durum şeridi ve olay akışı
- EN başlık önerisi: Status ribbon and event stream
- Ne yapar: Pencerenin üstündeki şerit en son ne istendiğini, o anki fazı, ilerlemeyi, kalan süreyi ve hata/uyarı durumunu her zaman gösterir; event stream koşunun olaylarını kronolojik tek satırlarla akıtır.
- Nasıl: Kalıcı işlem etiketi (`SYNC`, `BUILD`, `REBUILD`, `CLEAN`, `DEEP CLEAN`, `OPTIMIZE`, `RESOLVE`, `SWITCHING BRANCH`) iş sürerken amber, bitince nötr kalır; yanında faz satırı, derlenen projelerin chip'leri ve ince ilerleme çubuğu; öncelikli uyarılar faz satırının yerine geçer.
- Etiketler: kalıcı işlem etiketi ile "son ne istendi"; kalan süre tahmini; hata chip'leri ile tek tıkla hatalı projeler; event stream ile koşunun zaman çizelgesi
- Yüzler:
  - `▸ Building n/m · elapsed · ~Ns left` / `· almost done` (bilerek kötümser, koşu içi ortalamadan)
  - `Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s`; `Ready — nothing to build`; `Everything up to date — N projects checked in T, nothing to build`
  - İlk üç hatalı proje chip'i + `+N more`
  - Uyarı sırası: motor ölümü → motor sessiz → çıkış → başlatma → Sync hatası → koşu hatası; kapanabilir banner ya da toast yok
  - Koşu başlamadan konsolda adım adım planlama satırları
  - Event stream satır türleri ok / fail / skip / info / warn / sync / done; proje satırına tık seçer; sıfır hatalı koşunun done satırı bir kez parlar; otomatik Sync tek satır (`synced after commit`, `synced · N projects changed`)
- Görünür yer: ribbon (title bar altı); EVENT STREAM paneli
- Kaynaklar: ARCH §5.3, §8.4, §8.6, §13.2, §13.5; README Ribbon, Event stream; kod `ViewModels/RibbonText.cs`, `ViewModels/OperationLabel.cs`; CHANGELOG 0.2.0, 1.3.0, 1.4.0; arch 31, 32, 53, 65
- Önem gerekçesi: koşunun anlatıcısı
- WHY adayı: hayır

## 23. Kısayollar
- EN başlık önerisi: Keyboard shortcuts
- Ne yapar: Pencerede F5 Build, F6 Rebuild, F7 Clean, Ctrl+F filtre, F1 About, Esc üst katmanı kapat ya da build'i durdur, Ctrl+Q Exit; her yerden Shift+Space pencereyi gösterir/gizler, Ctrl+Shift+Space pencereyi açmadan Build başlatır.
- Nasıl: Pencere ve global kısayollar tek katalogdan gelir; About → Shortcuts, Build menüsü badge'leri ve tooltip'ler aynı katalogdan çizilir; global kısayollar Windows'a kaydedilir ve pencere arka plandayken de çalışır.
- Etiketler: F5/F6/F7 ile VS alışkanlığı; RegisterHotKey ile arka plandan Build; tek kısayol kataloğu ile tutarlı metin; Esc zinciri ile öngörülebilir kapanış
- Yüzler:
  - F5 durdurmaz (VS alışkanlığı); devre dışı komut kısayolla da çalışmaz
  - Esc sırası: exit kartı → dialog → popover/menü → seçim → build (graceful, ikinci basış hard); Sync / bakım Clean'i / Optimize / checkout / pull durdurulamaz, Esc konsola tek satır yazar
  - Shift+Space yalnız pencere gerçekten öndeyken gizler
  - Ctrl+Shift+Space başlayamazsa ve pencere tepsideyse balon nedenini söyler (`Build not started — a Sync is in progress.`)
  - Çakışan global kısayol sessizce devre dışı kalır, About'ta *unavailable*
  - Global kısayollar yalnız `ui-state.json`'dan (`ShowHideHotkey`, `BuildHotkey`) değiştirilir, UI yok
  - About → Shortcuts grupları Global · Build · Application
- Görünür yer: klavye; About → Shortcuts
- Kaynaklar: ARCH §12.3, §13.7, §13.9; README Keyboard shortcuts 599-632; kod `Shell/ShortcutCatalog.cs:77-102`, `Shell/Hotkey.cs:105-109`; CHANGELOG 0.2.0, 1.1.0, 1.8.0; arch 80, 81, 82
- Önem gerekçesi: IDE'den çıkmadan build
- WHY adayı: evet
  - Amaç: Editörden ayrılmadan derleme başlatabilmek.
  - Yaygın pratik: Ayrı bir derleme aracı ya da script kullanan geliştirici her derlemede editörden o pencereye geçer, komutu çalıştırır ve geri döner.
  - Build Orchestrator'da: Visual Studio'da çalışırken tek kısayolla arka planda derleme başlar; pencereye geçmek gerekmez, bitince sonuç bildirilir.

## 24. Tepsi, başlangıç ve bildirimler
- EN başlık önerisi: Tray, startup and notifications
- Ne yapar: Uygulama tepside yaşar: X pencereyi tepsiye gizler, tepsideyken build sürüyorsa ekran köşesinde animasyonlu ürün işareti görünür, koşu bitince Windows bildirimi gelir; istenirse Windows açılışında (gerekirse tepside gizli) başlar; ikinci açılış mevcut pencereyi öne getirir.
- Nasıl: Close to tray, Start with Windows, Start minimized to tray ve Show notifications Settings → General'dadır; Windows başlangıcı HKCU Run kaydıyla yapılır (yönetici gerekmez); tepsi menüsünde Stop ve Exit vardır, ikona tıklamak pencereyi geri getirir.
- Etiketler: tepsi ikonu ile arka planda çalışma; köşe göstergesi ile pencere açmadan ilerleme; Windows bildirimi ile sonuç haberi; HKCU Run ile yöneticisiz otomatik başlatma
- Yüzler:
  - İlk tepsiye kapanışta bir kez `Still running in the tray. Right-click the tray icon and choose Exit to quit.`; Close to tray kapalıyken X = Exit
  - Start with Windows anahtarı Windows'un gerçekte ne yapacağını gösterir (Task Manager'da kapatılmışsa söyler; açıp kaydetmek düzeltir); taşınan exe her başlangıçta yeniden kaydedilir; kaldırma kaydı siler
  - Start minimized to tray yalnız Windows açılışında geçerli; elle açılış hep pencereyi gösterir
  - Show notifications dört balonu yönetir: koşu sonucu, ilk kapanış, başlayamayan Build kısayolu, ikinci açılış (`Already running — could not bring the existing window forward. …`)
  - Köşe işareti odak almaz, Alt-Tab'da görünmez, etrafındaki saydam alan tıkı alttaki uygulamaya geçirir, bildirim sayılmaz
  - Pencere açıkken biten koşu bildirim göstermez; tepsideyken pencere çizim yapmaz, dönüşte tek geçişte güncellenir
- Görünür yer: sistem tepsisi; ekranın sağ alt köşesi; Settings → General → Startup, Notifications
- Kaynaklar: ARCH §12.1, §12.3; README Settings 212-227, Tray 565-579; kod `Shell/AppTrayIcon.cs`, `Services/TrayBuildIndicatorController.cs`; CHANGELOG 0.2.0, 1.5.0, 1.7.0, 1.8.0, 1.8.1; arch 98, 99, 100, 101, 102, 103, 104, 105
- Önem gerekçesi: arka planda çalışma modeli
- WHY adayı: hayır

## 25. Kontrollü tam çıkış
- EN başlık önerisi: Exit without loose ends
- Ne yapar: Title bar ⏻, Ctrl+Q, tepsi → Exit ve (Close to tray kapalıyken) X uygulamayı tamamen kapatır; iş yoksa hemen, iş varsa düğmenin altındaki kart neyin sürdüğünü, hangi süreçlerin biteceğini ve neyin kaybolacağını söyler.
- Nasıl: *Stop and exit* koşuyu hard stop ile keser, Sync / bakım Clean'i / Optimize'ı keser, branch değişimi ve pull'u bekler (*Wait and exit*) ki git yarıda kalmasın; kapanış ekranı adımları gösterir ve uygulama motor ve altındakilerle birlikte kapanır.
- Etiketler: tek "meşgul" tanımı ile doğru karar; job süreç sayımı ile "ne sonlanacak" şeffaflığı; git yazımını bekleme ile yarım index önleme; in-flight defteri ile kesilen işin sonraki Build'de toparlanması
- Yüzler:
  - Kart: `BUILD IN PROGRESS` + ilerleme; ENDS ON EXIT süreç listesi (`×N`); `4 projects in flight stay unbuilt. Finished projects keep their results; the next Build picks up the rest.`
  - *Cancel*, Esc ya da dış tık işi sürdürür; iş biterse `Nothing is running. Exit quits right away.`
  - Kapanış ekranı `Closing Build Orchestrator` → `Stopping build…` → `Ending N processes…`
  - Yanıt vermeyen motor çıkışı bekletmez; Windows oturumu kapanırken kartsız, anında çıkar
- Görünür yer: title bar ⏻; Ctrl+Q; tepsi → Exit
- Kaynaklar: ARCH §4.4, §4.5, §12.3, §12.4, §13.3; README Exit 578-589; kod `Views/ExitCard.xaml`, `ViewModels/ExitText.cs`; merge 704b72b0 (sürümsüz); arch 106
- Önem gerekçesi: güvenli kapanış
- WHY adayı: hayır

## 26. Sync ve otomatik Sync
- EN başlık önerisi: Sync, mostly automatic
- Ne yapar: Sync depoyu ve harici kökleri tarar, grafı kurar, her projenin derlenip derlenmeyeceğini önizler ve remote'a uzaklığı ölçer; hiçbir şey derlemez. Terminalde ya da VS'de yapılan commit, checkout, pull, merge, rebase ve pencereye dönüş kararları kendiliğinden ve sessizce tazeler.
- Nasıl: Manuel Sync önce yalnız ref'leri fetch eder, sonra tarar ve karar verir; ağ yoksa yerel HEAD ile sürer. Depo HEAD'i dosya bildirimiyle izlenir (polling yok), git bittikten ~1,5 sn sonra tek Sync tetiklenir; HEAD değişmemişse Sync atlanır.
- Etiketler: ref-only fetch ile çalışma ağacına dokunmama; HEAD izleme ile elle Sync ihtiyacını kaldırma; sessiz Sync ile ekranı bozmadan tazeleme; git işlem işaretleri ile yarım merge'de bekleme
- Yüzler:
  - Açılışta, ayar kaydında, bakım işinden ve pull'dan sonra otomatik Sync
  - Manuel Sync, branch ve konfigürasyon değişimi konsolu ve listeyi baştan kurar; keşif sırasında `Discovering projects — 31 found · 29 repository · 2 external`, graf `Graph appears once projects are discovered`; Sync başarısızsa önceki liste geri gelir
  - Sessiz Sync konsolu, seçimi, scroll'u korur, event stream'e tek satır yazar, ağa çıkmaz
  - Dışarıdan branch değişimi yeni konsol bölümü açar (koşu sürüyorsa koşu kesilir, bkz. 12)
  - Merge / rebase / cherry-pick / revert ortasında branch chip'inde amber nokta (`Merge in progress — finish or abort it in git`), checkout/pull kilitli, otomatik Sync bekler, Build uyarıyla çalışır; 30 sn takılı kalan `index.lock` bir kez raporlanır
  - Reftable formatlı depolar desteklenir; pencereyi Build ya da Debug | Release üstünden öne getiren tık yutulmaz
  - Sayaçlar: doğrudan değişen, derlenecek, güncel; Sync düğmesi sürerken amber spinner
- Görünür yer: action bar Sync; ribbon; event stream; konsol Sync satırları
- Kaynaklar: ARCH §5.3, §10.1-§10.3, §12.1, §13.2; README Sync 242-268, Branch 306-311; kod `ViewModels/SyncMode.cs`, `Services/AutoSyncCoordinator.cs`; CHANGELOG 0.1.0, 1.6.0, 1.8.0, 1.9.0; merge d5d50e59; arch 55, 85, 86, 87, 90
- Önem gerekçesi: ekranın veri kaynağı
- WHY adayı: evet
  - Amaç: Ekrandaki "neyin derleneceği" bilgisinin deponun o anki durumuna her zaman uyması.
  - Yaygın pratik: Branch değiştirdikten ya da pull yaptıktan sonra hangi projelerin etkilendiği belli olmaz; geliştirici ya her şeyi derler ya da aklında kalan projeleri.
  - Build Orchestrator'da: Visual Studio'da ya da terminalde commit veya branch değişikliği yapınca liste birkaç saniye içinde kendiliğinden güncellenir; Sync düğmesine nadiren gerek kalır.

## 27. Branch değiştirme ve pull
- EN başlık önerisi: Switch branches, fast-forward
- Ne yapar: Alt bardaki branch chip'i checkout'lu branch'i gösterir ve aranabilir listeden gerçek `git checkout` yapar; geride kalınca beliren `N behind` chip'i aktif branch'i yalnız fast-forward ile günceller.
- Nasıl: Remote branch seçilince yerel tracking branch açılır; kirli ağaçta geçiş varsayılan olarak reddedilir, *Stash and switch branches* açıksa değişiklikler (untracked dahil) `git stash push -u` ile saklanır. Pull `fetch` + `merge --ff-only`'dir: merge commit ya da rebase yoktur, kirli/ayrışmış ağaçta ve detached HEAD'de reddeder.
- Etiketler: git checkout ile araçtan branch değişimi; `stash push -u` ile kirli ağacı koruma; `merge --ff-only` ile tarih yazmayan pull; `rev-list --count` ile ucuz uzaklık
- Yüzler:
  - Popover "SWITCH BRANCH", "Filter branches", "Switching checks the branch out in your working tree."
  - Başarıda konsol `Switched to <branch> (<sha>) — from <previous>`, pill `SWITCHING BRANCH`, ardından Sync
  - Ret: `warning: N files have uncommitted changes — commit or stash them first`, stream `branch switch refused — N uncommitted files`
  - Stash bölümün ilk satırında adlandırılır ve `git stash pop` ile geri alınacağı söylenir; araç pop etmez; stash ayarı pull'a uygulanmaz
  - Sync satırı `HEAD a3f81c2 · 3 commits behind origin/main` / `· up to date with origin/main`
  - N behind tooltip `Fetch found N new commit(s) on origin/<branch>. Click to fast-forward; nothing is merged or rewritten.`; ret `pull refused — uncommitted changes` / `— branch diverged` / `— not on a branch`
  - Çevrimdışı ve detached HEAD'de chip çizilmez; koşu ve git işlemi sırasında ikisi de kilitli; aynı adlı tag varken de doğru çalışır
- Görünür yer: action bar branch chip + popover, `N behind` chip; Settings → General → Branches
- Kaynaklar: ARCH §10.1, §10.3, §10.5, §13.2, §13.3; README Using it 287-304; kod `Views/BranchPopover.xaml`, `ViewModels/InteractionText.cs:16-17`; CHANGELOG 1.4.0, 1.6.0, 1.9.0; arch 88, 89, 91
- Önem gerekçesi: günlük git akışı araçta
- WHY adayı: hayır

## 28. Harici kökler
- EN başlık önerisi: External projects, one graph
- Ne yapar: Depo dışındaki klasör, çözüm ya da proje dosyaları Settings'te kart olarak eklenir; aynı grafa taranır, depodaki projeler onlara gerçek bağımlılıkla bağlanır ve depodan önce derlenir; istenirse her Build'den önce kendi git kopyaları fast-forward ile güncellenir.
- Nasıl: Kart yalnız bir yoldur (klasör: altındaki her proje; `.sln`: listelediği projeler; `.csproj`: kendisi), git kopyası yoldan bulunur, hiçbir şey saklanmaz; *Pull before build* açıksa kart sırasıyla her kopyada `fetch` + `merge --ff-only` yapılır, commit'lenmemiş iş varsa koşu başlamadan durur.
- Etiketler: çok kök tarama ile tek graf; HintPath kenarı ile doğru sıra; ff-only güncelleme ile güvenli tazeleme; yoldan türetme ile taşınan projeyi tanıma
- Yüzler:
  - Liste ve grafta en üstte External grubu; katman desenleri uygulanmaz
  - Kartlar sürüklenerek sıralanır, sıra güncelleme sırasıdır; sayfa altında *Pull before build* düğmesi General'a götürür
  - Projeye çözülmeyen ya da ana kökü gösteren kart: Sync uyarır, Build başlamayı reddeder
  - Ayrışmış branch de durdurur; ulaşılamayan remote yalnız uyarır, yerel sürüm derlenir; `.git`'i olmayan yol olduğu gibi derlenir
  - Konsol `Updated external 'DoganTrend' → a1b2c3d`; anahtar kapalıyken hiçbir git komutu koşmaz; Sync harici köklerde git çalıştırmaz
  - Resolve cycles ve Clean güncellemeyi atlar; satırdan koşu yalnız hedefin kopyasını günceller
  - Kardeş depo eklenince platform DLL referansları gerçek kenara döner
- Görünür yer: Settings → External projects; Settings → General → Build → Pull before build; liste/graf External grubu
- Kaynaklar: ARCH §5.2, §6.4, §6.6, §10.4, §13.3; README Using it 229-233, 316-342; kod `Views/SettingsDialog.xaml:244-317`; CHANGELOG 1.4.0; arch 92, 93
- Önem gerekçesi: çok depolu kurulum
- WHY adayı: evet
  - Amaç: Ayrı depolardaki paylaşılan kütüphaneleri ana çözümle birlikte, doğru sırada derlemek.
  - Yaygın pratik: Ortak kütüphaneler ayrı bir depodaysa geliştirici önce o depoyu güncelleyip derler, sonra ana çözümü derler; adımlardan biri unutulunca eski kütüphaneyle çalışılır.
  - Build Orchestrator'da: Ayrı depodaki projeler aynı listede en üstte görünür ve önce onlar güncellenip derlenir; commit'lenmemiş iş varsa güncelleme yapılmadan durulur.

## 29. Bakım kutusu
- EN başlık önerisi: Maintenance: clean, repair, cycles
- Ne yapar: Sync'in yanında üç ikon: Clean (eraser) her projenin `bin`/`obj` klasörlerini ve derleme kaydını silip sonraki Build'i sıfırdan yaptırır; Optimize (gauge) eksik NuGet paketlerini restore eder, düzeltilemeyen referansları raporlar, bayat obj artıklarını ve ölü önbellek kayıtlarını temizler; Resolve cycles (unlink) yalnız döngüdeki projeleri ve ihtiyaç duydukları bayat upstream'i derler.
- Nasıl: Bu Clean MSBuild çağırmaz, dosya sisteminden siler ve bitince Sync zincirler; Optimize hiçbir derleme kararını değiştirmez ve yalnız bulunan projelerin klasörlerine yazar; Resolve cycles downstream'i bilerek dışarıda bırakır.
- Etiketler: bin/obj silme ile çalışma alanını sıfırlama; NuGet restore ile "bende derlenmiyor" onarımı; defter budama ile önbellek bakımı; dar kapsamlı döngü koşusu ile hedefli derleme
- Yüzler:
  - İkonlar etiketsiz, anlam tooltip'te (disabled'ken de okunur); çalışan iş amber zemin + spinner
  - Clean tooltip `Clean — remove every project's bin/ and obj/ and reset the build state; the next build compiles everything from scratch`; pill `DEEP CLEAN`; Build menüsündeki Clean'den farklı (o `-t:Clean`)
  - Clean: onay ve iptal yok; kullanımdaki dosya atlanır ve raporlanır; packages, paylaşılan OutDir ve loglar dokunulmaz; SDK-style projelerin paket asset'leri gider → önce Optimize
  - Optimize: konsol adımları packages · references · obj leftovers · caches, "nothing to fix"; her SDK-style projeyi restore eder; MSBuild yoksa yalnız restore adımı düşer
  - Resolve cycles tooltip `Resolve cycles — build the N cycle projects in repeated rounds: stale references first, then rebuild until they converge` (+ `· N upstream to build first`); döngü yoksa pasif; `N outside cycle scope — skipped`; `Cycles started — N cycle members · P prerequisites · up to K rounds`; perf profilinin aynısıyla koşar
  - Clean ve Optimize liste/grafı boşaltır, bitince Sync
- Görünür yer: action bar, Sync'in yanı (eraser · gauge · unlink)
- Kaynaklar: ARCH §5.2-§5.4, §8.1, §8.6, §9.3, §9.4, §13.2, §16; README Maintenance box 429-453; kod `AccessibilityNames.cs:48-127`, `Views/MaintenanceBox.xaml`; CHANGELOG 1.2.0, 1.5.0, 1.7.0, 1.8.0; arch 5, 6, 7, 8
- Önem gerekçesi: bozuk ortamın tek tık çözümü
- WHY adayı: evet
  - Amaç: "Bende derlenmiyor" durumlarını tek tıkla gidermek.
  - Yaygın pratik: Taze bir klondan ya da yarım kalmış paket indirmesinden sonra derleme anlaşılmaz hatalar verdiğinde geliştirici `bin`/`obj` klasörlerini elle siler, paketleri yeniden indirir, kimi zaman depoyu baştan klonlar.
  - Build Orchestrator'da: Bir düğme çalışma alanını sıfırlar, diğeri eksik paketleri getirip kırık referansları adıyla söyler; ardından liste kendiliğinden yenilenir.

## 30. Settings ve ilk kurulum
- EN başlık önerisi: Settings and first run
- Ne yapar: İlk açılışta liste kullanıcıyı çalışma alanını kurmaya davet eder; Settings dört bölümdür (General · Workspace · External projects · Layers) ve hiçbir şey Save'e kadar uygulanmaz; ayarlar bir JSON dosyasına dışa aktarılıp başka makinede içe aktarılabilir.
- Nasıl: Workspace'te repository root girilir (*Browse…*), ilk kurulumda düğme *Save and sync* olur; Save taslağı uygular ve bir Sync başlatır; boş root kaydetmek çalışma alanını kapatıp ilk açılış ekranına döner.
- Etiketler: taslak/Save modeli ile güvenli ayar; JSON export/import ile ekip içinde aynı kurulum; ilk açılış daveti ile yönlendirme; tek ayar kataloğu ile tutarlı varsayılanlar
- Yüzler:
  - Davet `Configure the workspace` (Repository root / Layers listesi, *Open settings*, *Import settings…*); ribbon `Not configured — repository root not set`, konsol `Waiting for a workspace`
  - General sekiz anahtar: Startup (Start with Windows, Start minimized to tray, Close to tray) · Build (Pull before build) · Branches (Stash and switch branches) · Console (Compact console) · Engine (Stop the engine when idle) · Notifications (Show notifications); varsayılan açık olanlar Pull before build, Compact console, Stop the engine when idle, Close to tray, Show notifications
  - Değişen her anahtar Save'de konsola tek not yazar
  - Footer: Export (`build-orchestrator-settings.json`) · Import (yalnız formu doldurur; bozuk dosyada `Invalid settings file`) · Clear (iki tık, anahtarlar varsayılana döner)
  - Save kapalıysa tek satır sebep: `Every external project needs a path` / `Every layer needs a name` / `Check the highlighted pattern`
  - Koşu sürerken root değişimi ertelenir (`Repository change deferred — run in flight`)
  - Perf profili ve global kısayollar Settings'te yok
- Görünür yer: title bar ⚙; ilk açılış daveti
- Kaynaklar: ARCH §13.2, §13.3; README Settings 200-241; kod `Views/SettingsDialog.xaml`, `ViewModels/GeneralSettings.cs:70-116`, `ViewModels/SettingsFile.cs`; CHANGELOG 0.2.0, 1.4.0, 1.5.0, 1.8.0; arch 54, 94, 95, 96, 97
- Önem gerekçesi: araç bunsuz çalışmaz
- WHY adayı: hayır

## 31. Katmanlar
- EN başlık önerisi: Layers: group by name
- Ne yapar: Kullanıcı proje adına uyan regex desenleriyle katmanlar tanımlar; liste ve graf bu katmanlara ayrılır, eşleşmeyenler Other altında toplanır; katman sırası bir projeyi bağımlılığının önüne koyarsa koşu başında uyarılır.
- Nasıl: Settings → Layers'ta ad + desen kartları; yukarıdan aşağı ilk eşleşen kazanır; varsayılan boş (tek düz liste, graf tek bant).
- Etiketler: regex gruplama ile büyük listede yön bulma; ilk eşleşme kuralı ile öngörülebilir atama; ters katman uyarısı; sürükle-sırala kartlar
- Yüzler:
  - Placeholder örneği `Core` / `^MyApp\.(Core|Common)\.`
  - Geçersiz regex ve boş ad Save'i kapatır; desen başına 100 ms zaman aşımı (aşan desen eşleşmez, uyarı)
  - External grubu desenlerden muaf ve en üstte; Save'de konsola katman sayısı notu
  - Ters katman uyarısı yalnız uyarır, hiçbir şeyi bloklamaz
  - Derleme sırasına etkisi tartışmalı — bkz. §7 belirsizlik 1
- Görünür yer: Settings → Layers; liste katman başlıkları; graf bantları
- Kaynaklar: ARCH §6.6, §13.3; README Using it 235-236; kod `Views/SettingsDialog.xaml:324-397`; CHANGELOG 0.2.0; arch 29
- Önem gerekçesi: yüzlerce projede düzen
- WHY adayı: hayır

## 32. Perf profilleri ve kaynak şeridi
- EN başlık önerisi: Performance profiles, live cost
- Ne yapar: Alt bardaki perf chip üç sabit profil arasında döner: Full (6 paralel, CPU sınırı yok), Balanced (4 paralel, %70, varsayılan), Light (2 paralel, %40); title bar ortasındaki kaynak şeridi aracın makineye o anki maliyetini (CPU, RAM, paralellik + mod) gösterir.
- Nasıl: Profil yalnız derleyicilerin bulunduğu iç job'a CPU sınırı ve öncelik olarak uygulanır; arayüz, git ve Sync tam hızda kalır. Koşu başında worker sayısı işlemci sayısına ve boş belleğe göre kırpılabilir; şerit dış job'un muhasebesinden ucuzca okunur.
- Etiketler: Job Object CPU rate control ile makine payını sınırlama; process önceliği ile arayüzü öne alma; worker bütçesi ile sayfalamayı önleme; job muhasebesi ile IPC'siz ölçüm
- Yüzler:
  - Öncelik Full Normal / Balanced BelowNormal / Light Idle; koşu sırasında değiştirmek sınırı ve önceliği anında değiştirir, paralellik sonraki koşuda
  - Konsol notu `parallelism: <n> · cpu cap <p>%` / `cpu cap off`; kırpılınca `workers reduced to <n> (<reason>)` (konsol, event stream)
  - Kopya çakışmasında ve stop drain'inde sınır gevşer (Light'ın %40'ı mutlak değil); Full için uyarı yok; profil kalıcı
  - Şerit hücreleri CPU `69%`, RAM `310 MB` / `4.8 GB` (uygulama + motor + derleyiciler), paralellik `3/4/4 Balanced` (şu an / bu koşu / mod sınırı), boşta `0/—/4 Balanced`, kırpılınca `2/2/4`
  - Tooltip'ler `app 310 MB · engine 85 MB · compilers 4.4 GB`, `· cap 70%`, `2 of 2 — reduced: <reason>`
  - Şerit ~3 sn'de bir yenilenir, tepside/minimize'da durur, tıklanamaz; mod chip'ten değişir
- Görünür yer: action bar perf chip; title bar ortası kaynak şeridi; konsol notu
- Kaynaklar: ARCH §4.1, §4.2, §5.2, §5.3, §11.1-§11.3, §11.5; README Performance modes 729-783; kod `Core/ProcessControl/PerfProfile.cs:4-36`, `Core/Diagnostics/ResourceStripText.cs`, `Views/ActionBar.xaml:60`; CHANGELOG 1.0.0, 1.8.0; merge a3ecfa0a (sürümsüz şerit); arch 52, 132, 133
- Önem gerekçesi: derlerken makineyi kullanabilmek
- WHY adayı: evet
  - Amaç: Derleme sürerken makinenin başka işler için kullanılabilir kalması.
  - Yaygın pratik: Büyük bir çözüm tüm çekirdeklerle derlenirken editör, tarayıcı ve toplantı uygulaması takılır; geliştirici uzun derlemeleri ara verdiği zamanlara bırakır.
  - Build Orchestrator'da: Derlemenin makineden ne kadar alacağı tek tıkla seçilir; varsayılan ayarda derleme sürerken arayüz ve diğer uygulamalar akıcı kalır, kullanılan kaynak başlıkta görünür.

## 33. Hafif ve akıcı çalışma
- EN başlık önerisi: Light when idle, smooth when busy
- Ne yapar: Motor beş dakika iş görmezse kapanır ve sonraki işte kendiliğinden başlar; pencere tepsideyken ya da arkadayken işlemci boşa dönmez; her işlem bitince bellek geri verilir; yüzlerce projelik koşuda bile arayüz donmaz.
- Nasıl: *Stop the engine when idle* (varsayılan açık) motoru durdurur, uyanırken ribbon `▸ Starting the engine…` der; görünmeyen animasyonlar durur; olaylar küçük zaman dilimleriyle işlenir, liste görünür satırları önce kurar.
- Etiketler: boşta motor uykusu ile bellek tasarrufu; görünürlüğe bağlı animasyonlar ile boşta sıfıra yakın CPU; işlem sonu bellek trim'i; zaman dilimli olay işleme ile donmayan arayüz
- Yüzler:
  - Uyandıran işler Build, Sync, Clean, Optimize, checkout, pull, proje logu; Stop ve perf chip uyandırmaz; About → Environment motor PID `asleep`
  - Uyanan motor ekranı değiştirmez; tepsiden kısayolla başlayan build açılış animasyonunu beklemez, dönüşte ekran tek geçişte yetişir
  - Ölçümler: tepside boşta 81 → 5 milyon cycle/s; boşta bellek 845 → ~236-349 MB; 4K'da projeye tıklama 1,1 GB → ~300 MB
  - Bellek trim'i kısa bir duraklamadır, görünür pencerede finale bittikten sonra çalışır (S19); caret'ler yalnız pencere aktifken yanıp söner
  - Gerçek ölçekte (177 proje) hiçbir adım arayüzü bütçesinin ötesinde bloklamaz
- Görünür yer: Settings → General → Engine; ribbon `▸ Starting the engine…`; arka plan
- Kaynaklar: ARCH §4.7, §11.4, §12.1, §12.3, §13.5, §14.5, §17.3; README Using it 214-219; CHANGELOG 1.0.0, 1.5.0, 1.8.0, 1.10.0, 1.10.1; arch 42, 134, 135, 136, 137
- Önem gerekçesi: tepside günlerce kalabilmek
- WHY adayı: evet
  - Amaç: Gün boyu açık duran bir aracın makineye yük olmaması.
  - Yaygın pratik: Arka planda açık bırakılan geliştirici araçları zamanla yüzlerce MB bellek ve sürekli biraz işlemci tutar; geliştirici bu yüzden aracı gerektiğinde açıp sonra kapatır.
  - Build Orchestrator'da: Uygulama tepside açık kalabilir; iş yokken neredeyse hiç işlemci kullanmaz, belleğin çoğunu geri verir ve ilk derlemede birkaç saniyede hazır olur.

## 34. Kurulum ve güncelleme
- EN başlık önerisi: Install and stay current
- Ne yapar: GitHub Releases'taki kurulumcu yönetici hakkı istemeden yalnız mevcut kullanıcıya kurar; kurulu kopya yeni sürümü sessizce indirir, hazır olunca title bar'da Update pill'i çıkar; What's new sürüm notlarını, About sürüm/ortam/kısayolları gösterir.
- Nasıl: Velopack ile kullanıcı başına kurulum; açılıştan ~5 sn sonra ve her 4 saatte bir GitHub Releases kontrol edilir, paketin boyutu ve SHA'sı doğrulanır; *Restart to update* hemen kurar, *Later* denirse çıkışta sessizce kurulur. Sürüm notları exe'ye gömülü CHANGELOG'dan okunur.
- Etiketler: Velopack ile yöneticisiz kurulum ve delta güncelleme; çıkışta kurulum ile çalışmayı kesmeme; CHANGELOG tek kaynak ile uygulama içi notlar; Copy diagnostics ile tek tık destek bilgisi
- Yüzler:
  - Kurulum `%LOCALAPPDATA%\BuildOrchestrator.App\`, Start menüsü ve masaüstü kısayolu, .NET 10 Desktop Runtime yoksa kurar; kurulumcu imzasız (SmartScreen → More info → Run anyway)
  - Güncelleme ve kaldırma ayarları/logları bırakır, kaldırma Start with Windows kaydını siler; eski kopyadan geçerken önce tepsi → Exit
  - Bin ya da publish klasöründen çalışan kopya güncelleme kontrolü yapmaz
  - Update kartı: UPDATE READY, kurulu → gelen sürüm, indirme boyutu, en çok beş öne çıkan + `+N more in What's new after restart`; iş sürerken *Restart to update* pasif ve neyi beklediğini söyler; hatalar sessiz, sonraki tur dener
  - Restart ekranı `Updating Build Orchestrator`, çubuk dolunca yeni sürüm açılır
  - What's new (✦): sürüm sürüm Added / Changed / Fixed / Performance / Removed, `INSTALLED`, en yeni üç açık, *Earlier versions (N)*; yeni sürümde ✦'de amber nokta, açınca silinir
  - About (ⓘ / F1): About · Environment (motor PID, .NET, OS, MSBuild yolu, root, state, logs) · Shortcuts; Copy diagnostics ürün, sürüm, ortam ve bellek satırlarını panoya kopyalar
  - Sürüm numarası her yüzeyde aynı (`Engine ready — v<version>`); ağa yalnız git fetch ve güncelleme kontrolü çıkar, telemetri yok
- Görünür yer: Setup.exe; title bar Update pill, ✦, ⓘ; F1
- Kaynaklar: ARCH §3.3, §12.5, §13.3, §16, §17.6, §18, §21.5; README Install, About, What's new, Update; kod `ViewModels/UpdateText.cs`, `Views/AboutDialog.xaml`, `Views/NotesDialog.xaml`; CHANGELOG 1.1.0, 1.4.0, 1.7.0, 1.8.0, 1.9.0; arch 47, 107, 108, 111, 112, 113, 114
- Önem gerekçesi: zahmetsiz dağıtım
- WHY adayı: hayır

## 35. Tasarım ve erişilebilirlik
- EN başlık önerisi: Calm design, accessible
- Ne yapar: Arayüz tek koyu temada, tek vurgu rengi amber ile çalışır; renk yalnız durum anlatır ve her durum renk + glyph + metinle söylenir; Windows'ta animasyonlar kapalıysa her geçiş anında olur; ekran okuyucu ve klavye ile kullanılabilir.
- Nasıl: Renk, ölçü, tipografi ve hareket tek token sisteminden gelir; Geist / Geist Mono fontları gömülüdür; hareket sistemi Windows'un animasyon ayarını canlı izler; her kontrol tek bir erişilebilir ad ve gerçek rol bildirir.
- Etiketler: token sistemi ile tutarlı görünüm; OS reduced-motion sinyali ile erişilebilir hareket; UI Automation ad tablosu ile ekran okuyucu desteği; gömülü fontlar ile çevrimdışı aynı görünüm
- Yüzler:
  - Açılış koreografisi: her işlem tıklandığı an başlar (pill yanar, düğme Stop olur), kapsamdaki projeler tek tek amber yanar, sonra koşu başlar
  - Dim dahil her metin 4.5:1 kontrast; Lucide ikonlar, emoji yok; ürün markası ve çok boyutlu uygulama ikonu
  - Ortak modal kabuğu (scrim/Esc kapatır, Tab içeride döner, modallar yığılır, alttaki taslak yaşar); popover'lar dış tık, Esc ya da ikinci basışla kapanır
  - Tooltip'ler gecikmesiz ve disabled düğmede de görünür (satır ikonları ve katman başlığı bilerek gecikmeli)
  - Tasarım sistemi kontrolleri (switch, segment, chip, ince scrollbar, odak halkası)
  - Ribbon faz metni, keşif sayacı, motor başlatma ve kapanış adımı ekran okuyucuya duyurulur
  - Tüm metin İngilizce, sakin ve kesin; toast ve uygulama içi popup yok; tema seçimi yok
- Görünür yer: tüm arayüz; Windows Ayarlar → Erişilebilirlik → Animasyon efektleri
- Kaynaklar: ARCH §13.3, §13.8, §14.1-§14.7, §15; CHANGELOG 1.3.0, 1.4.0, 1.5.0; arch 78, 83, 84, 119, 120, 122, 123, 124, 125, 126, 127, 128, 129
- Önem gerekçesi: sakin, okunur arayüz
- WHY adayı: hayır

## 3. Küçük özellikler

- Koşu başı uyarıları — bayat obj (başka TFM'den kalan NuGet artığı) ve ters katman sırası koşu başında proje başına uyarı; hiçbir şey silinmez ya da bloklanmaz, çoksa event stream'de tek satıra katlanır — ARCH §5.3, §6.6, §9.4; CHANGELOG 1.8.0
- Boş panel durumları — liste/graf boşken sebep ne olursa olsun `No projects` / `No graph` (liste/ağ ikonu); filtre boşsa `No projects match this filter.` — ARCH §13.2; merge 79db1481 (sürümsüz)
- Diskte durum — her şey `%LOCALAPPDATA%\BuildOrchestrator\` altında (logs, build-state, iki önbellek, ui-state, run-inflight, WPF targets), kurulumdan ayrı; branch saklanmaz — ARCH §7.5, §16; README State on disk
- Gereksinimler — Windows, .NET 10 Desktop Runtime, VS 2022 ya da Build Tools (MSBuild bileşeni), PATH'te git; Open in Visual Studio için tam VS IDE — README Requirements
- Güven sınırı — `.csproj`'daki her target/`<Exec>` kullanıcının yetkisiyle çalışır, kısıtlanmaz, yalnız kapsanır (job) ve kısılır (CPU); tehdit modeli dışı olanlar açıkça yazılı — ARCH §21.1, §21.5
- Sertleştirilmiş girdiler — yollar ve branch adları asla kabuktan geçmez, regex 100 ms zaman aşımı, IPC satırı 1 MiB sınırı — ARCH §21.2, §21.3
- Bilinçli kapsam dışı (v1) — çoklu depo tarihi, headless/CLI, açık tema, command palette, onboarding, MSIX, kod imzası, packages.config geçişi, branch başına çıktı yalıtımı, graf düzenleme, çalışan VS'ye bağlanma — ARCH §1.3
- Ürün kimliği — pencere başlığı, tepsi, balonlar, About ve Task Manager aynı ürün adını gösterir; motor süreci kendi adını korur — ARCH §3.3
- WPF kaynaklı davranışlar — klasör seçici/Explorer/VS sistem görünümünde açılır, tooltip pencere dışına taşabilir, konsol geçişi düz kayma — ARCH §19
- Güncelleme ortam değişkenleri (geliştirici) — `BO_UPDATE_SOURCE` yerel klasör/URL, `BO_UPDATE_PRERELEASE=1` pre-release — ARCH §12.5, §17.6; README Update
- Paketleme (geliştirici) — `scripts/package.ps1`: installer + full + delta + feed; motor pakete gömülü, single-file/portable yok — ARCH §18
- Sürüm çıkarma (geliştirici) — `/release` + `release.ps1`: develop → main merge; GitHub test eder, paketler, tag koyar, Release yayınlar; kırmızı test tag bırakmaz — ARCH §18
- Build, test, CI (geliştirici) — üç komut; `ci.yml` her develop push'unda; kabul testleri gerçek MSBuild ile uçtan uca; kaynak guard'ları kullanıcıya dolaylı garanti — ARCH §17, §18
- Clean sonrası döngü hatırlatması — bakım Clean'i döngü üyelerini temizlediyse akış Resolve cycles'ı hatırlatır (bugün geçerliliği doğrulanmalı, §7 belirsizlik 5) — merge cf7e102c (1.7.0)

## 4. Bilinen sınırlar

1. Tek depo geçmişi: harici kökler grafa girer ama branch, HEAD izleme ve `N behind` yalnız ana kökü anlatır; branch değişimi harici kopyayı oynatmaz (§20, §1.3; README)
2. Branch'ler arası çıktı yalıtımı yok: farklı branch build'leri aynı OutDir/obj'ye yazar; başka branch = checkout (§20; README)
3. Branch değişiminin kesme çizgisi: ~1,5 sn + IPC içinde biten eski ağaç projesi güvenilir sayılır, sonraki Sync düzeltir (§20)
4. Rebase uçuştaki koşuyu keser (§20)
5. HEAD izleme reflog ister: ilk commit'i olmayan depo izlenmez, pencereye dönüş yeniden dener (§20, §10.3; README)
6. Otomatik Sync fetch yapmaz; `N behind` son fetch'e göredir (§20; README)
7. Detached HEAD ve başarısız fetch'te remote uzaklığı ölçülmez, chip çizilmez (§10.2, §10.5)
8. Shared compilation ve node reuse kapalı: ~%10 hız yerine GB'larca bellek ve job dışı compiler server riski reddedildi (§20, §9.2; README)
9. Projenin kendi `CustomBeforeMicrosoftCommonTargets`'ı aracınkiyle yer değiştirir; tespit yok (§20, §9.2)
10. Build SDK-style projeyi restore etmez: `project.assets.json` eksikse (taze klon, bakım Clean'i) `NETSDK1004`; Optimize çözer (§20)
11. Şartlı restore `nuget.config repositoryPath`'i okumaz (§9.3)
12. Satır kurma maliyeti bir kez ödenir: ilk ekran senkron, kalanı boşta (§20; README)
13. Büyük graf açılışı: culling yok, 500 düğüm ~130 ms, 1000 ~300 ms; yüzlerce projede graf şekil olarak okunur (§20; README)
14. Run klasörü olmayan bölümün (Sync, bakım Clean'i, Optimize) anlatısı bellekte kalır (§20, §13.5; README)
15. Proje sayfasının tavanı yok: tüm log bellekte, derlenirken büyür (§20)
16. Uygulama çökerse `narrative.log`'un yazılmamış kuyruğu kaybolur (§8.5)
17. İlk geniş satır görünümü bir scrollbar yüksekliği yukarıda kalabilir (§20)
18. Pencerede 3B yok; konsol geçişi düz kayma (§20, §19)
19. Her işlem sonundaki tam GC kısa bir duraklamadır; o an tıklayan hisseder (§20; README)
20. Uyuyan motordan sonra ilk iş motor başlayana kadar bekler (README Known limits)
21. Önizleme döngülerde az söz verebilir: gri çizilen üye de derlenebilir (§20)
22. Rebuild / Clean / Cycles / satır koşusu ulaşmadığı satırın önizleme bayrağını Sync'in bıraktığı gibi bırakır (§20)
23. Çıktı kanıtı dosya zamanı ve uzunluğuna güvenir: aynı uzunlukta elle kopyalanan DLL geçer, eski tarihle geri yüklenen dosya yeni sayılmaz, geri alınan saat yanıltır (§20)
24. Depoya commit'li kütüphane kopyası tazelenmediği için denetlenmez (§7.6)
25. Visual Studio ile araç aynı projeyi aynı anda derlememeli; hakem yok (§20)
26. Araç dışında derlenmiş çıktı koşunun konfigürasyonu için derlenmiş sayılır; konfigürasyonlar arası paylaşılan çıktı yolu yanıltır (§20, §7.2)
27. API kapısı: durdurulan ya da satırdan başlayan koşudan sonra o koşunun derlediği upstream'in bağımlıları bir kez daha derlenir (§20; README)
28. Atlanan proje kendi klasöründeki bağımlılık kopyalarını son derlemedeki hâlde tutar (§20, §8.8; README)
29. Kapıyla atlanan proje kalıtılan bağımlılık notunu sonraki Sync'e kadar göstermez (§20)
30. Kanıt yolu olmayan upstream'in (layout'u değiştirilmiş SDK-style) bağımlıları hep derlenir; dışarıdan okuyan döngü üyesi carried sayılmaz (§20)
31. Değerlendiricinin okuyamadığı ayarla taşınan SDK-style çıktı yolu varsayılanda kalır; oradaki eski dosya donmuş yüzey olur, eski `bin` silinmeli (§20)
32. API kapısı bağımlılığın kendi çıktısını okur, paylaşılan kopyayı değil (§20)
33. Kendi çıktısından fazlasını kopyalayan post-build adımı yalnız adla görülür; MSB302x retry yalnız MSBuild Copy task'ını kapsar (§20, §9.5)
34. İç içe projede dış proje daha sık derlenir (güvenli yön) (§7.1)
35. İçerik formülüne geçiş her şeyi bir kez derletir; soğuk diskte ilk indeks ~40 sn (§7.1; README)
36. IPC'de alan düzeyi şema doğrulaması yok; eksik alan `planFailed`/`runFailed` olur, framing hatası kurtarılamaz (§20, §5.4; README)
37. Symlink/junction taramada izlenmez ve tespit edilmez; `.csproj` depo dışına referans verebilir (§20, §6.1; README)
38. Graf düğümleri klavyeyle gezilemez; ekran okuyucu okur ve çağırır (§20, §15; README)
39. Çıkışta git yazımını bekleme motor susarsa biter; uzun git yazımı kesilebilir (§20)
40. Çıkış fetch'i yarıda kesebilir; `.git`'te `.lock` kalırsa o ref'in fetch'i dosya silinene dek düşer (§20)
41. Uyutulurken ölen motor uyumuş sayılır (§20)
42. Ölüm bildirmeden başarısız Restart engine Starting'de kalır; istekler `Engine is not running.` ile düşer (§20)
43. Okunamayan proje logu "log yok" görünür; uyarı yalnız motorun tanı çıktısında (§20, §5.3)
44. Global kısayolların ayar arayüzü yok; çakışan kısayol sessizce devre dışı kalır (§20, §12.3; README)
45. `explorer` / `devenv` argümanları elle tırnaklı (§21.4)
46. Branch adı git'e `--` olmadan gider (§21.4)
47. Supervisor argümanları doğrulanmaz (§21.4)
48. Kaskad garantisinin istisnaları: kullanıcı adına açılan Explorer/VS ve COM/WMI/task scheduler'a devreden build adımı job dışında (§21.4, §4.2)
49. Proje log klasörü kontrolü leksikal (§21.4)
50. Harici kök projeleri `modified · local` almaz (§10.2)
51. Kaynak şeridi koşu ortası profil değişimini göstermez; süreç tamponu 256 (§11.5)
52. Full profil için uyarı yok (§11.1)
53. Tema seçimi yok, tek koyu tema (§14, §1.3)
54. Reduced motion uygulama içi anahtar değil, yalnız Windows ayarı (§14.5)
55. Koyu görev çubuğunda ikonun iki koyu şeridi kaybolur (§14.4)
56. Toast / uygulama içi popup yok (§14.7)
57. Hotfix ayrı çıkmaz; `main` hep `develop`'un tamamını alır (§18)
58. Self-contained publish doğrulanmamış; single-file reddedilir; portable yok (§18)
59. OS yüzeyleri temalanamaz; tooltip pencere dışına taşabilir/ters dönebilir; küçük metin tarayıcıyla bit-aynı değil; UI meşgulken animasyon garanti değil (§19)
60. Kod imzası yok; kurulumcu SmartScreen uyarısı verir, dağıtım bütünlüğü GitHub hesabına bağlı (§21.5, §1.3; README)
61. Güncelleme yolu test süitinde koşamaz, elle prova edilir (§17.6)
62. Güncelleme kontrolü yalnız kurulu kopyada (§21.5, §12.5; README)
63. Satırdan "bağımlılıklarıyla derle" yok; satır koşusu bağımlılıkları derlemez (§5.2, §6.5)
64. Sync, bakım Clean'i, Optimize, checkout ve pull durdurulamaz (§13.9; README Esc)
65. Bakım Clean'i ve Optimize'ın onayı ve iptali yok (§5.4, §13.2)
66. Snap Layouts açılır menüsü yok (§12.2)

## 5. Kaldırılan / bayat (dokümana girmez)

- Resolve cycles "full priority" (öncelik) ayarı — 1.8.0'da geldi, 7542cdd9 ile kaldırıldı; Resolve artık perf profilinin aynısıyla koşar. CHANGELOG 1.8.0 maddesi hâlâ ayardan söz ediyor (yayınlanmış not, olduğu gibi kalır)
- *Continue* / *Retry failed* komutları — 0.1.0 → 1.1.0'da kalktı; Build kalanı alır
- Ayrı worktree'de başka branch derleme — 0.2.0 → 1.6.0'da kalktı; yalnız eski klasör için ipucu kaldı (madde 15)
- Alt+B global kısayolu; Ctrl+F5, Shift+F5, Ctrl+F1 (What's new) — 1.8.0'da kalktı
- F5'in Stop olması — 1.8.0'da kalktı
- Konsolun 3B panel geçişi — 1.10.1'de 2B'ye döndü
- "To build" filtre chip'i — 2026-09-20'de kalktı
- Varsayılan OSYS katmanları — bugün varsayılan boş
- Graf viewport culling + lazy label (1.0.0) — ARCH bugün "culling yok" diyor; bayat
- Görünmez bead yörüngeleri (1.8.0 stutter düzeltmesi) — özellik (derlenen düğüm çevresinde noktalar) kalıyor
- Proje sayfasındaki sha satırı, `build in progress` işareti
- Yakınsamayan döngüyü aynı imzada ön-atlama — artık her basış gerçek deneme
- Grafın kesikli "Graph appears after Sync" kutusu; graf köşesindeki ipucu satırı
- Harici köklerin ayrı seri fazı (hata tüm koşuyu iptal ediyordu)
- Commit tabanlı (git) değişim formülü — 1.4.0'da içerik formülüne geçti
- "İş sürerken basılan Build bekler ve iş bitince başlar" (6f9cbedb) — bugün reddedilir
- Eski tek `Hotkey` alanı ve eski `vcs` anahtarı — okunup yok sayılır
- Satır sweep/shine animasyonu — denenip reddedildi
- Fast yayılım modu — Core'da var, UI yok (dokümana girişi §6 karar 5)
- `--font-ab` geliştirici kabuğu, Select kontrolü (canlı tüketicisi yok)
- İç ayrıntı: `BO_PROBE_*` ölçüm sondaları ve Measurement testleri; "çıktı kanıtı ucuz" optimizasyonu

## 6. Kullanıcı kararı gereken noktalar

1. İki Clean'in adı: Build menüsü/satır Clean'i (`-t:Clean`, F7) ile bakım kutusu Clean'i (bin/obj + derleme kaydı, pill `DEEP CLEAN`) dokümanda UI'daki gibi ikisi de "Clean" mi kalsın, yoksa bakım kutusununki "Deep Clean" diye mi anılsın?
2. Alt barın adı dokümanda "action bar" mı "bottom bar" mı olsun, ve README'nin branch chip'i ile `N behind`'ı title bar'da gösteren cümleleri (Using it 288-295) bu işte düzeltilsin mi?
3. Perf chip konumu "alt bardaki perf chip + title bar kaynak şeridindeki mod sözcüğü" diye mi anlatılsın?
4. Katmanlar derleme sırasını değiştiriyor mu: ARCHITECTURE §6.6 "katman faz bariyeridir, plan katman sırasına göre yeniden dizilir" diyor, kod kataloğu "yalnız gruplar, sırayı değiştirmez" diyor — hangisi doğru? (doküman ile kod uyuşmuyor)
5. Fast yayılım modunun UI'ı yok: FEATURES'ta hiç anılmasın mı, yoksa "yayılım her zaman Safe" diye tek cümle mi geçsin?
6. Global kısayolların yalnız `ui-state.json`'dan değiştirilebildiği FEATURES'ta anlatılsın mı?
7. Geliştirici yüzeyleri (paketleme, `/release`, CI, `BO_UPDATE_*`) FEATURES'a girsin mi, yoksa README/ARCHITECTURE'da mı kalsın?
8. WHY'da ölçüm rakamları (boşta bellek 845 → ~236-349 MB, 4K tıklama 1,1 GB → ~300 MB, 81 → 5 milyon cycle/s, 177 projede ~303 ms sıcak karar) kullanılsın mı, yoksa yalnız nitel mi kalsın?
9. Referans iş yükü (177 csproj / 44 sln, legacy .NET Framework) ve iç adlar (OSYS, Delta Yazılım) dokümanlarda geçsin mi?
10. *Pull before build* UI açıklaması "a fast-forward-only git pull" diyor, ARCHITECTURE "asla `pull`; `fetch` + `merge --ff-only`" diyor — dokümanda hangi ifade kullanılsın, UI metni de düzeltilsin mi?
11. Bilinçli kapsam dışı (v1) listesi FEATURES'a "ne yapmaz" bölümü olarak girsin mi?
12. "Eski sürümden geçişte ilk Build her şeyi bir kez derler" notu hâlâ yazılmaya değer mi?
13. Tasarım ve erişilebilirlik tek madde (35) mi kalsın, yoksa erişilebilirlik ayrı madde mi olsun?

## 7. Açık belirsizlikler

1. Katmanlar ile derleme sırası — ARCH §6.6 "faz bariyeri, `(layerIndex, orijinal sıra)` ile yeniden sıralama" ↔ kod kataloğu §6 ve `Views/SettingsDialog.xaml:325-327` "yalnız gruplama"; plan sıralaması kodda doğrulanmalı (§6 karar 4)
2. Proje başına zaman aşımının süresi ve ayarı hiçbir kaynakta yok (ARCH §4.3, §9.3; arch belirsiz 3)
3. HintPath sağlık metriğinin kullanıcıya göründüğü yer (ARCH §6.4; arch belirsiz 5)
4. Harici kökte commit'lenmemiş iş yüzünden koşu iptalinin tam metni (ARCH §10.4; README 338 "proje ve klasör adıyla")
5. Bakım Clean'i sonrası "Resolve cycles" hatırlatması bugün hâlâ var mı — yalnız merge cf7e102c'de; ARCH ve kod kataloğunda yok
6. Satırın ▶ "Build this project" düğmesi ile ⋯ menüsündeki Build aynı komut mu — kod kataloğu ikisini de "yalnız o proje" diye tanımlıyor, satır satır doğrulanmadı (arch belirsiz 8)
7. Event stream 150 satır sınırı tek yerde geçiyor (ARCH §13.5; arch belirsiz 9)
8. Konsolda Ctrl+tık link nereye açar (ARCH §13.5; arch belirsiz 12)
9. Panel başlığı `N events` sayacı yalnız bir yerde anılıyor, kod kataloğunda yok (ARCH §13.2; arch belirsiz 13)
10. Kurulum ayrıntıları: UAC istememesi, masaüstü kısayolunun oluşması, Desktop Runtime'ı indirip kurması doğrulanmadı (ARCH §12.5, §18; README Install; arch belirsiz 15)
11. Ribbon `elapsed` sayacının tanımı yalnız ETA biçiminde geçiyor (ARCH §8.4; arch belirsiz 14)
12. Kaynak şeridinin boşta metni `0/—/4 Balanced` kodda satır satır doğrulanmadı (kod kataloğu son not)
13. Harici kök kartında dosya/klasör seçici: kod kataloğu yalnız yol alanı + Remove listeliyor (seçici yok görünüyor), doğrulanmalı (arch belirsiz 10)
14. `cycle #df6f2b` token'ı ve "dört ton" başlığı altında beş renk ailesi — iç tutarsızlık, dokümana girmez (ARCH §14.1, §14.3)
15. "Until v1.11" sürüm referansı — CHANGELOG'da 1.11 yok (ARCH §14.3)
16. README Using it 288-295 branch chip ve `N behind`'ı title bar'da anlatıyor, kod alt action bar diyor — README düzeltmesi (§6 karar 2)
17. Geçmişte 1.0.0 "graf viewport culling" ↔ ARCH §20 "culling yok" — ARCH güncel kabul edildi, kodda doğrulanabilir

Kod kataloğunca kapanan eski belirsizlikler (kısa): Reveal in Explorer csproj'u seçili açar · Open in VS seçicisi yalnız proje birden çok `.sln`'deyse · tepsi menüsü yalnız Stop + Exit · ⚙ = Settings · Debug | Release, branch chip, `N behind`, perf chip alt action bar'da · ribbon title bar'ın altında · Restart engine yalnız ribbon'da · bakım kutusu sırası eraser · gauge · unlink · iş sürerken basılan Build reddedilir, bekletilmez · About bellek satırları yalnız Copy diagnostics'te.

## 8. Eşleme

### inventory-arch-merged.md (137)
- 1 Build (yalnız değişeni derle) → 1
- 2 Build menüsü ve mod seti → 9
- 3 Rebuild → 9
- 4 Clean (MSBuild -t:Clean) → 9
- 5 Deep Clean (workspace reset) → 29
- 6 Optimize → 29
- 7 Bakım kutusu → 29
- 8 Resolve cycles → 29
- 9 Döngü çözme → 3
- 10 Satırdan tek proje koşusu → 19
- 11 Paralel derleme → 2
- 12 İçerik tabanlı değişim kararı → 1
- 13 Safe / Fast yayılım modu → 1 (Fast → X)
- 14 Debug | Release anahtarı → 9
- 15 Karar etiketi ve will-build önizlemesi → 4
- 16 Kanıta dayalı "failed" → 4
- 17 Waiting for dependency → 5
- 18 Surface gate → 6
- 19 Built outside this tool → 7
- 20 Çıktı kanıtı → 1
- 21 Restore → 8
- 22 MSBuild.exe ile derleme → 8
- 23 Çalışma ağacında derleme (OutDir) → 13
- 24 WPF geçici assembly hızlandırması → 8
- 25 Kopya çakışması toleransı → 8
- 26 Türkçe Windows'ta bozulmayan çıktı → 8
- 27 Çalışma alanı keşfi → 2
- 28 HintPath bağımlılık grafı → 2
- 29 Katmanlar → 31
- 30 Koşu başı uyarıları → K
- 31 Planlama ilerlemesi → 22
- 32 ETA → 22
- 33 Branch değişince koşu kesilir → 12
- 34 Çökme kurtarma → 12
- 35 Tek iş kapısı → 9
- 36 Nested Job Object → 10
- 37 Arayüz ve motor ayrı process → 10
- 38 Güvenli child başlatma → 10
- 39 Stop / Stop now → 11
- 40 Restart engine → 15
- 41 Sessizlik bekçisi → 15
- 42 Boşta motor uykusu → 33
- 43 Motor hazır ve başlangıç Sync'i → 15
- 44 Git'e kendiliğinden yazmaz → 14
- 45 Güven sınırı ve tehdit modeli → K
- 46 Sertleştirilmiş girdi yüzeyleri → K
- 47 Ağ erişimi → 34
- 48 Bilinçli kapsam dışı → K
- 49 Dört panelli ana pencere → 16
- 50 Görünüm modları → 16
- 51 Title bar → 16
- 52 Resource strip → 32
- 53 Sticky ribbon → 22
- 54 İlk açılış ekranı → 30
- 55 Sync sırasında keşif sayacı → 26
- 56 Boş panel kendini söyler → K
- 57 Panel başlık sayaçları → 16
- 58 Workspace etiketi → 16
- 59 Proje satırı → 17
- 60 Uyarı üçgeni → 5
- 61 Reveal in Explorer / Open in Visual Studio → 19
- 62 Yapışkan katman başlıkları → 17
- 63 Liste/graf açılış reveal'i → 17
- 64 Otomatik takip ve kaydırma → 17 (konsol kısmı 21)
- 65 Event stream → 22
- 66 Listeyi süzme → 18
- 67 Konsol → 21
- 68 Narrative geçmişi → 21
- 69 Compact console → 21
- 70 Proje sayfası → 21
- 71 Koşu logları diskte → 21
- 72 Bağımlılık grafı paneli → 20
- 73 Koşu grafta → 20
- 74 Graf seçimi → 20
- 75 Graf gezinme → 20
- 76 Liste–graf ortak hover → 20
- 77 Tek seçim jesti → 17
- 78 Açılış koreografisi → 35
- 79 Bitiş finali → 20
- 80 Pencere içi kısayollar → 23
- 81 Global kısayollar → 23
- 82 Esc zinciri → 23
- 83 Popover davranışı → 35
- 84 Gecikmesiz tooltip'ler → 35
- 85 Sync → 26
- 86 Otomatik (sessiz) Sync → 26
- 87 HEAD watcher → 26
- 88 Branch chip'inden checkout → 27
- 89 Stash and switch branches → 27
- 90 Git mid-operation kapısı → 26
- 91 N behind ve ff-only pull → 27
- 92 Harici kökler → 28
- 93 Pull before build → 28
- 94 Settings dialogu → 30
- 95 General ayarları → 30
- 96 Repository root → 30
- 97 Ayarları dışa/içe aktarma → 30
- 98 Close to tray → 24
- 99 Start with Windows → 24
- 100 Başlangıç rotaları → 24
- 101 Show notifications → 24
- 102 Tepsi ikonu ve menüsü → 24
- 103 Koşu sonucu OS balonu → 24
- 104 Tepsi build göstergesi → 24
- 105 Tek instance → 24
- 106 Tam çıkış ve onay kartı → 25
- 107 About dialogu → 34
- 108 Copy diagnostics → 34
- 109 State klasörü ve defterler → K
- 110 Dayanıklı disk yazımı → 12
- 111 Kurulum → 34
- 112 Otomatik güncelleme → 34
- 113 What's new ve sürüm notları → 34
- 114 Tek sürüm numarası → 34
- 115 Ürün kimliği → K
- 116 Paketleme → K
- 117 Sürüm çıkarma → K
- 118 Build, test ve CI → K
- 119 Koyu tema ve token sistemi → 35
- 120 Gömülü Geist fontları → 35
- 121 Durum renkleri → 17
- 122 Lucide ikonlar → 35
- 123 Ürün markası ve uygulama ikonu → 35
- 124 Hareket sistemi ve reduced motion → 35
- 125 Ortak modal kabuğu → 35
- 126 Design-system kontrolleri → 35
- 127 Ekran okuyucu desteği → 35
- 128 Klavye erişimi → 35 (liste kısmı 17)
- 129 Üslup ve yasak kalıplar → 35
- 130 WPF platform kısıtları → K
- 131 Özel koyu pencere çerçevesi → 16
- 132 Perf profilleri → 32
- 133 Worker sayısını makineye uydurma → 32
- 134 UI donmaz → 33
- 135 Gizli pencere ekran işi yapmaz → 33
- 136 Boştayken CPU yakmaz → 33
- 137 Bellek trim'i → 33
- İç ayrıntı: ölçüm sondaları → X · çıktı kanıtı ucuz → X
- Bilinen sınırlar (61) → §4 · Belirsizler (20) → §7 / kapandı

### inventory-readme.md (88 + sınırlar)
- A Yalnız değişenleri derleme → 1
- A Bağımlılık sırasında paralel derleme → 2
- A Canlı ilerleme: liste, graf, konsol → 16
- A Durdur ve kaldığı yerden devam → 11
- A Tek ağaç, gerçek checkout → 13
- B Çalışma gereksinimleri → K
- B Tek tıkla kurulum → 34
- B Kopyalanmış klasörden kurulu kopyaya geçiş → 34
- B Kaldırma → 34
- C İlk çalıştırma daveti ve Settings yapısı → 30
- C General ayar grupları → 30
- C Stop the engine when idle → 33
- C Start with Windows / Start minimized to tray → 24
- C Close to tray → 24
- C Show notifications → 24
- C External projects kartları → 28
- C Layers → 31
- C Export / Import / Clear → 30
- C Workspace'i kapatma → 30
- D Sync → 26
- D İşlem sırasında kilit → 9
- D Aynı assembly adı uyarısı → 2
- D HEAD watcher ile otomatik sessiz Sync → 26
- D Manuel Sync ekranı baştan kurar → 26
- D Debug / Release anahtarı → 9
- D Satır renkleri → 17
- D Decision label → 4
- D Bozuk bağımlılıkta bekleme → 5
- D Sync konsol satırında remote mesafesi → 27
- D `N behind` chip ile pull → 27
- E Branch chip ile checkout → 27
- E Stash and switch branches → 27
- E Dışarıdan branch değişimini yansıtma → 26 (kesme kısmı 12)
- E Git işlem ortasında amber nokta → 26
- F External projeleri aynı graf ve listede derleme → 28
- F Pull before build → 28
- F Yükseltme sonrası tek seferlik tam derleme → 1
- G Build (yalnız stale) → 9 (API kapısı kısmı 6)
- G Rebuild → 9
- G Clean (Build menüsü) → 9
- G Koşu açılış ve kapanış animasyonu → 35 (kapanış kısmı 20)
- G Komut gitmeden iptal ve "resolving" fazı → 11
- G Stop / Stop now / Terminating → 11
- H Tutarlı renk dili → 17
- H Amber uyarı üçgeni → 5
- H Sayaç çipleri ve filtre birleşimi → 18
- H Satır eylemleri → 19
- H Open in Visual Studio → 19
- H Layer başlığına tıklayınca kaydırma → 17
- I Cycle gruplarını turlarla derleme → 3
- I Resolve cycles düğmesi → 29
- I Cycle koşusunun görünümü ve verdict'i → 3
- J Deep Clean (eraser) → 29
- J Optimize (gauge) → 29
- K Konsol kaydırma ve sahiplik → 21
- K Event stream → 22
- K Compact console → 21
- K Proje logu → 21
- K Her projenin sayfası var → 21
- K Konsol bölümleri ve bellekte tutulan metin → 21
- L Graf görünümü → 20
- L Graf koşu sırasında → 20
- L Graf pan / zoom → 20
- M Global kısayollar → 23
- M Tray'de koşuyu izleme ve köşe işareti → 24
- M Sonuç bildirimi → 24
- M Tray menüsü → 24
- N Exit → 25
- N Engine yanıt vermiyor → Restart engine → 15
- O Pencere kısayolları → 23
- O Esc katman kapatma sırası → 23
- P About ekranı → 34
- P Copy diagnostics → 34
- P What's new dialog'u → 34
- P What's new amber nokta → 34
- P Otomatik güncelleme → 34
- P Update kartı → 34
- P Restart to update ve kapanış ekranı → 34
- P Later / çıkışta sessiz kurulum → 34
- P Güncelleme ortam değişkenleri → K
- Q Diskte durum ve log saklama → K (log saklama 21)
- Q Kesilen koşu tespiti → 12
- Q Eski worktrees klasörü uyarısı → 15
- R Performance mode chip → 32
- R Koşu başında worker'ı makineye uydurma → 32
- R Kaynak şeridi → 32
- R Ribbon → 22
- R Title bar komut grubu → 16
- S Bilinen sınırlar (13 madde) → §4
- Belirsizler (15) → §7 / kapandı

### inventory-history.md (65)
- Proje keşfi ve Sync → 26
- Bağımlılık sıralı paralel derleme → 2
- Yalnız değişeni derleme → 1
- Dışarıda derlenen projeye kredi → 7
- API yüzey kapısı → 6
- SDK-style çıktı yolu → 1
- WPF geçici assembly metadata-only → 8
- Rebuild → 9
- Tek proje Build/Rebuild/Clean → 19
- Durdurma ve iz bırakmama → 11 (süreç kısmı 10)
- Kaldığı yerden devam → 11
- Başarısız bağımlılık yönetimi → 5
- Koşu başı uyarıları → K
- Sync'siz Build kapısı / planlama görünürlüğü → 9 (planlama 22)
- Resolve cycles (Cycles butonu) → 29 (öncelik ayarı → X)
- Build kirli döngü gruplarını da derler → 3
- Döngü güveni → 3
- Clean sonrası döngü hatırlatması → K
- Clean (bakım kutusu ve Build menüsü) → 9 ve 29
- Optimize → 29
- Harici projeler → 28
- Branch chip ile branch değiştirme → 27
- Behind chip ve ff-only pull → 27
- Otomatik Sync → 26
- Reftable repo desteği → 26
- Debug|Release geçişi → 9
- Ayrı worktree'de başka branch derleme → X
- Ana pencere ve üç yerleşim → 16
- Bağımlılık grafı → 20 (culling → X)
- Katmanlı proje listesi → 17
- Listenin koşuyu takip etmesi → 17
- Katman tanımları → 31 (varsayılan OSYS katmanları → X)
- Satır rengi modeli → 17
- Durum chip filtreleri ve arama → 18
- Liste–graf hover yansıması → 20
- Proje sayfası → 21
- Canlı konsol → 21
- Kompakt konsol → 21
- Event stream → 22
- İlerleme şeridi → 22
- Kaynak şeridi → 32
- Exit butonu ve çıkış onayı → 25
- Boş panel durumları → K
- İşlem dalgaları ve animasyonlar → 35 (finale 20)
- Tasarım sistemi ve cila → 35
- Erişilebilirlik ve klavye odağı → 35
- Performans modları → 32
- Worker bütçesi → 32
- İş sürerken komut kapısı → 9
- Klavye kısayolları → 23
- Tepsi, tek instance ve tepsi modu → 24
- Close to tray ve bildirim ayarları → 24
- Windows ile başlat → 24
- Visual Studio'da aç / Explorer'da göster → 19
- Settings → 30
- About → 34
- What's new → 34
- Otomatik güncelleme → 34
- Boşta motoru durdurma → 33
- Boşta bellek azaltma → 33
- Büyük çözümde akıcılık → 33
- Hafif pencere ve log saklama → 33 (log saklama 21)
- Ayrı motor süreci ve IPC → 10 (ölüm → 15)
- Süreç çıkış kodu doğruluğu → 8
- Git'e kendiliğinden yazmama → 14
- OutDir'e dokunmama → 13
- Yayın hattı ve CI → K
- Zaman çizelgesi / boşluklar → kaynak notu; eklenip kaldırılanlar → X; belirsizler (11) → §7 / kapandı

### inventory-code.md (20 girdi + katalog)
- §1 Settings kataloğu → 30 (Startup/Notifications anahtarları 24, Pull before build 28, Stash 27, Compact console 21, Stop engine 33)
- §2 Alt aksiyon barı → Sync 26 · bakım kutusu 29 · sayaç chip'leri 18 · workspace adı 16 · branch / N behind 27 · Debug | Release 9 · perf chip 32 · Build split-button 9
- §2 Proje satırı → 19 (karar etiketleri 4) · liste başlığı → 18, 17 · title bar → 16, 34, 25, 32 · sticky şerit → 22, 15 · tepsi menüsü → 24 · konsol araç çubuğu → 21, 22 · graf kontrolleri → 20, 16
- §3 Kısayollar → 23
- §4 Bildirimler → 24
- §5 Dialoglar ve ekranlar → Settings 30 · About / What's new / güncelleme ekranı 34 · Exit kartı / kapanış ekranı 25 · ilk açılış daveti 30 · boş panel durumları K
- §6 Belirsizlik cevapları → ilgili maddelere uygulandı (Perf 32, Clean ayrımı 9/29, Optimize 29, Resolve 29, Open in VS 19, düzen 16, Restart engine 15, harici kökler 28, Safe/Fast 1 + X, hotkey 23, Layers 31)
- Perf profili chip'i → 32
- Arka planda Build kısayolu → 23
- Pencereyi getir/gizle kısayolu → 23
- Tepside derleme göstergesi ve sonuç balonu → 24
- Satırdan tek proje derleme → 19
- Visual Studio'da aç / Explorer'da göster → 19
- Bakım kutusu: Clean / Optimize → 29
- Döngü çözme (Resolve cycles) → 29
- Branch değiştirme ve ff-only pull → 27
- Harici projeler → 28
- Katmanlar → 31
- Ayar içe/dışa aktarma → 30
- Kompakt konsol ve proje logu → 21
- Durum filtre chip'leri → 18
- Kaynak şeridi → 32
- Exit onay kartı ve kontrollü kapanış → 25
- Uygulama içi güncelleme → 34
- Motor boşta durdurma ve yeniden başlatma → 33 (yeniden başlatma 15)
- Otomatik (sessiz) Sync → 26
- Satır karar etiketleri → 4
- Belirsiz / bulunamadı → §5, §7
