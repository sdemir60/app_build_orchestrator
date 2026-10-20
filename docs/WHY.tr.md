[English](WHY.md) | Türkçe

# Neden Build Orchestrator

Aracın her parçasının ne için olduğu ve iş gününde neyi değiştirdiği,
[FEATURES.tr.md](FEATURES.tr.md) ile aynı sırada; her maddenin arkasındaki mekanizma orada anlatılır. Her madde
amacı, yaygın pratiği ve Build Orchestrator'da bunun yerine ne olduğunu verir.

*Yaygın pratik* satırları, çok projeli bir .NET çözümünün böyle bir araç yokken genelde nasıl derlendiğini anlatır:
elle Visual Studio'da, kendi yazılmış bir script'le ya da her geliştiricinin kendi yöntemiyle. Genel bir
resimdir, belirli bir ekibin kaydı değildir.

## Yalnız değişeni derler

**Amaç:** yalnız gerçekten değişen kısmı beklemek.\
**Yaygın pratik:** çok projeli bir çözümde bir değişikliğin hangi projelere dokunduğu kolay görülmez; bu yüzden
çözümün tamamı ya da büyük bir kısmı yeniden derlenir, ya da bir proje atlanır ve eski bir DLL çalışma anında
ortaya çıkar.\
**Build Orchestrator'da:** Build yalnız değişen projeleri ve onları kullananları derler; hiçbir şey değişmediyse
koşu derlenecek bir şey olmadığını söyleyip hemen biter.\
`kaynak içeriği hash'i` · `oturumlar arası derleme defteri`

## Bağımlılık sırasıyla, paralel derler

**Amaç:** aynı işi doğru sırayla, boş duracak çekirdekleri de çalıştırarak daha kısa sürede bitirmek.\
**Yaygın pratik:** projeler birinin aklında ya da bir script'te tutulan sırayla tek tek derlenir; biri bitmeden
öteki başlamaz, çekirdeklerin çoğu bekler ve yanlış sıra ancak bir sonraki denemede ortaya çıkar.\
**Build Orchestrator'da:** sıra her seferinde depodan çıkar; birbirine bağlı olmayan projeler aynı anda derlenir,
kırılan bir proje ilgisiz olanları durdurmaz.\
`paralel derleme için hazır-küme zamanlayıcı` · `gerçek graf için HintPath haritası`

## Bağımlılık döngülerini çözer

**Amaç:** döngüsel referanslı çözümleri bir ritüele gerek kalmadan derlenebilir tutmak.\
**Yaygın pratik:** projeler birbirinin DLL'ine referans verdiğinde aynı projeler belli bir sırayla birkaç kez
derlenir ve hangisinin eski bir DLL'e karşı derlendiği hiçbir zaman tam belli olmaz; sıra zamanla ekipte sözlü bir
alışkanlığa dönüşür.\
**Build Orchestrator'da:** döngüdeki projeler tek Build içinde oturana kadar yeniden derlenir; sonuç oturup
oturmadığını ya da hangi projenin durdurduğunu açıkça söyler.\
`döngü tespiti için Tarjan SCC` · `oturmanın kanıtı API yüzey hash'i`

## Her projenin neden derleneceğini söyler

**Amaç:** "bunu neden derliyor?" sorusunu derleme başlamadan cevaplamak.\
**Yaygın pratik:** IDE bir projeyi neden yeniden derlediğini ya da neden atladığını söylemez; şüphe olunca her şey
temizlenip baştan derlenir ya da çıktıda sebep aranır.\
**Build Orchestrator'da:** her projenin adının yanında tek kelimelik bir gerekçe durur, kırmızı yalnız gerçekten
derlenmeyen kod için yanar.\
`modified ile affected ayrımı için içerik parmak izi` · `kanıta dayalı kırmızı, yanlış alarm yok`

## Bayat bağımlılığı gösterir

**Amaç:** kırık bir bağımlılığa karşı derlenmiş projeyi görünür kılmak ve onu boşuna yeniden derlememek.\
**Yaygın pratik:** bir kütüphane derlenmediğinde üstündeki projeler onun eski DLL'iyle derlenmeye devam eder,
uygulama çalışıyor görünür ve sorun geç fark edilir; ya da her denemede bütün zincir yeniden derlenir.\
**Build Orchestrator'da:** kırık bir bağımlılığın arkasında bekleyen projeler tek bakışta görünür, bağımlılık
düzelince sonraki Build'e kendiliğinden katılırlar.\
`hedefli yeniden derleme için kök notu` · `tek amber uyarı kanalı`

## API değişmediyse bağımlıyı atlar

**Amaç:** alt seviyedeki bir kütüphanede yapılan küçük bir iç değişikliğin üstündeki her şeyi derletmesini önlemek.\
**Yaygın pratik:** paylaşılan bir kütüphanedeki tek satırlık düzeltme ona bağlı her projeyi yeniden derletir;
katmanlı bir çözümde bu tek değişiklik uzun bir beklemeye dönüşür.\
**Build Orchestrator'da:** iç değişiklikte yalnız kütüphanenin kendisi derlenir, üstündeki projeler yeşil kalır;
public bir imza değişince onlar da derlenir.\
`derlenmiş çıktının API yüzey hash'i` · `paylaşılan kütüphanede zincirleme derlemeyi kesme`

## Visual Studio ile yan yana

**Amaç:** IDE'de yapılan derlemeleri çöpe atmak yerine saymak.\
**Yaygın pratik:** ayrı bir derleme aracı IDE'nin zaten derlediğini yeniden derler; geliştiriciler sonunda ikisinden
birini seçer.\
**Build Orchestrator'da:** Visual Studio'da derlenen projeler yeşil görünür ve atlanır; yalnız Visual Studio'nun
görmediği bir bağımlılık değiştiyse yeniden derlenir.\
`başkasının çıktısını tanıma` · `bayat kopyayı yakalayan üst zincir`

## Visual Studio'nun derlediği gibi derler

**Amaç:** Visual Studio'nun vereceği sonucun aynısını almak.\
**Yaygın pratik:** farklı bir derleme komutu ya da farklı argümanlar kullanan script'ler "VS'de derleniyor,
script'te derlenmiyor" türünden farklar çıkarır; eski .NET Framework projelerinde bu daha sık olur.\
**Build Orchestrator'da:** projeler Visual Studio'nun derlediği gibi derlenir, eksik paketler yolda kendiliğinden
gelir, makinede ek bir paket aracı gerekmez.\
`MSBuild'i bulmak için vswhere` · `proje başına ayrı MSBuild.exe süreci`

## Arkada süreç bırakmaz

**Amaç:** derleme durduğunda ya da araç kapandığında hiçbir derleyicinin çalışmaya devam etmemesi.\
**Yaygın pratik:** yarıda kesilen bir derlemeden sonra arkada kalan derleyici süreçleri dosyaları kilitli tutar;
bunlar Task Manager'da tek tek bulunur ya da makine yeniden başlatılır.\
**Build Orchestrator'da:** uygulama nasıl kapanırsa kapansın derleyiciler de birkaç saniye içinde onunla kapanır,
sonraki derleme kilitli bir dosyaya takılmaz.\
`yetim sürece karşı iç içe Job Object` · `zincirleme kapanış için kill-on-close`

## Güvenle durur, kaldığı yerden sürer

**Amaç:** derlemeyi istenen anda bozuk çıktı bırakmadan durdurmak ve oradan devam etmek.\
**Yaygın pratik:** uzun bir derlemeyi yarıda kesmek yarım yazılmış çıktılar ve hangi projelerin bittiğine dair
belirsizlik bırakır; güvenmek için sonrasında her şey yeniden derlenir.\
**Build Orchestrator'da:** Stop derlenmekte olan projelerin temiz bitmesine izin verir, yeniden Build'e basınca
yalnız bitmeyenler derlenir.\
`tutarlı çıktı için proje sınırında boşaltma` · `doğru devam için kayıt geçersizleme`

## Yarım kalan işi toparlar

**Amaç:** beklenmedik bir kesintiden sonra neyin derlendiğine dair şüpheyi ortadan kaldırmak.\
**Yaygın pratik:** makine kilitlenir ya da derleme yarıda öldürülürse yarım yazılmış çıktılar kalır; güvenli yol
her şeyi temizleyip yeniden derlemektir.\
**Build Orchestrator'da:** sonraki açılışta konsol hangi projelerin yeniden derleneceğini söyler ve yalnız onlar
derlenir.\
`yarım çıktıyı bulan uçuştaki-iş defteri` · `bozuk dosyaya karşı atomik yazım`

## Çıktı klasörlerine dokunmaz

**Amaç:** aracı kullanmaya başlarken hiçbir şeyin derlendiği ya da dağıtıldığı yeri değiştirmemek.\
**Yaygın pratik:** bazı derleme script'leri çıktıyı kendi klasörlerine yönlendirir ya da kopyalar; IDE ile script
farklı yerlere yazınca hangi DLL'in güncel olduğu belirsizleşir.\
**Build Orchestrator'da:** çıktı her zaman Visual Studio'nun koyduğu yerdedir; aracı kullanmaya başlamak ya da
bırakmak çıktı düzeninde hiçbir şeyi değiştirmez.\
`değişmeyen çıktı yolu, VS ile aynı düzen` · `çıktıyı yalnız okuma`

## Git'e kendiliğinden yazmaz

**Amaç:** geçmişine ve commit'lenmemiş işine asla dokunmayan bir derleme aracı.\
**Yaygın pratik:** güncel olmak için derlemeden önce pull ya da reset yapan script'ler zaman zaman yerel
değişiklikleri ezer ya da beklenmedik merge'ler bırakır; bu yüzden ekipler derleme script'ine git adımı koymaktan
çekinir.\
**Build Orchestrator'da:** depoya yalnız sen bir chip'e tıkladığında ve yalnız güvenli yoldan yazılır; Build ve
Sync depoyu sadece okur.\
`denetlenebilirlik için tek git yazıcısı` · `yalnız fast-forward, geçmiş korunur`

## Duruma veya ada göre süzer

**Amaç:** hatalı projelere, derlenenlere ya da adı bilinen bir projeye tek tıkla ulaşmak.\
**Yaygın pratik:** uzun bir derleme çıktısında hangi projelerin hata verdiğini bulmak metni kaydırmak ya da aramak
demektir; henüz neyin derlenmediğini anlamak elle karşılaştırma ister.\
**Build Orchestrator'da:** bir sayaca tek tık yalnız hatalı ya da yalnız güncel projeleri gösterir, birkaç harf
yazmak bir projeyi adıyla bulur.\
`tek tık filtre olarak sayaç chip'leri` · `projeyi hızlı bulmak için ad filtresi`

## Satırdan derle, aç, göster

**Amaç:** tek bir projeyi bütün koşuyu beklemeden derlemek ve koduna tek tıkla geçmek.\
**Yaygın pratik:** tek bir projeyi denemek için IDE'de doğru çözüm açılır, proje Solution Explorer'da bulunup
derlenir; proje birden çok çözümdeyse ilk iş hangisini açacağını hatırlamaktır.\
**Build Orchestrator'da:** projenin yanındaki düğme yalnız o projeyi derler; aynı satır çözümünü Visual Studio'da ya
da klasörünü Explorer'da açar.\
`hızlı deneme için tek proje koşusu` · `doğru .sln için çözüm eşlemesi`

## Bağımlılık grafı

**Amaç:** yüzlerce projenin birbirine nasıl bağlı olduğunu tek bakışta görmek.\
**Yaygın pratik:** bir projenin neye bağlı olduğunu ve neyi etkilediğini öğrenmek proje dosyalarını tek tek açmak
ya da ekipte en uzun süredir çalışana sormak demektir.\
**Build Orchestrator'da:** bir projeye tıklamak onun kullandıklarını ve onu kullananları öne çıkarır; derleme
sırasında hangi projelerin derlendiği grafta da görünür.\
`bağımlılık incelemek için odakla-sığdır` · `koşuyu izleten opaklık anlatısı`

## Konsol ve proje logları

**Amaç:** derleme hatasını yüzlerce uyarı satırının arasında aramadan bulmak.\
**Yaygın pratik:** büyük bir çözümün derleme çıktısı binlerce uyarı satırı taşır; hata kaydırarak, arayarak ya da
çıktıyı bir dosyaya yapıştırarak bulunur.\
**Build Orchestrator'da:** konsol hataları tam, uyarıları özet olarak gösterir; bir projeye tıklamak logunu baştan
açar ve ilk hata hemen önündedir.\
`uyarı gürültüsünü kesen Compact console` · `hatayı baştan okutan proje sayfası`

## Durum şeridi ve olay akışı

**Amaç:** derlemenin her an ne yaptığını ve ne kadar kaldığını bilmek.\
**Yaygın pratik:** konsoldan yapılan derleme bir metin akışı basar; nereye geldiği ya da ne kadar süreceği akan
yazıdan tahmin edilir.\
**Build Orchestrator'da:** üstteki satır her zaman fazı, ilerlemeyi ve kalan süreyi verir; oradaki chip'ler o an
derlenen projeleri, koşu bitince de hatalıları adıyla gösterir, tek tık uzaklıkta.\
`kalan süre tahmini` · `hatalıya tek tık götüren chip'ler`

## Her yerden çalışan kısayollar

**Amaç:** editörden ayrılmadan derleme başlatmak.\
**Yaygın pratik:** ayrı bir derleme aracı ya da script'le her derleme, editörden o pencereye geçmek, komutu
çalıştırmak ve geri dönmek demektir.\
**Build Orchestrator'da:** Visual Studio'da çalışırken tek kısayol arka planda derlemeyi başlatır; geçilecek bir
pencere yoktur, sonuç bildirim olarak gelir.\
`arka plandan derlemek için global hotkey` · `Visual Studio gibi F5/F6/F7`

## Tepsi, açılışta başlama, bildirimler

**Amaç:** derleme aracının gün boyu hazır olması ama yolu kesmemesi.\
**Yaygın pratik:** bir derleme script'i her seferinde terminalden başlatılır, bitip bitmediğini öğrenmek o
terminale geri dönmeyi gerektirir.\
**Build Orchestrator'da:** uygulama istenirse Windows ile başlar ve tepside bekler; ekran köşesindeki küçük işaret
derlemenin sürdüğünü gösterir, sonucu bir bildirim söyler.\
`arka planda çalışmak için tepsi ikonu` · `sonuç için Windows bildirimi`

## Kendiliğinden Sync

**Amaç:** "neyin derleneceği" bilgisini her an depoyla uyumlu tutmak.\
**Yaygın pratik:** branch değiştirdikten ya da pull yaptıktan sonra hangi projelerin etkilendiği belli olmaz; ya
her şey derlenir ya da akla gelenler.\
**Build Orchestrator'da:** Visual Studio'da ya da terminalde yapılan commit veya branch değişikliğinden sonra liste
birkaç saniye içinde kendiliğinden güncellenir; Sync düğmesine nadiren gerek kalır.\
`Sync'e nadiren bastıran HEAD izleme` · `ekranı bozmayan sessiz Sync`

## Harici projeler, tek graf

**Amaç:** başka depolarda duran paylaşılan kütüphaneleri ana çözümle birlikte, doğru sırayla derlemek.\
**Yaygın pratik:** ortak kütüphaneler ayrı bir depodaysa önce o depo güncellenip derlenir, sonra ana çözüm;
adımlardan biri unutulunca eski bir kütüphaneyle çalışılır.\
**Build Orchestrator'da:** başka depolardaki projeler aynı listenin en üstünde görünür, önce onlar güncellenip
derlenir; commit'lenmemiş iş varsa araç güncellemek yerine durur.\
`çok kökten tek grafa tarama` · `her Build öncesi fast-forward güncelleme`

## Bakım: Deep Clean, Optimize, döngüler

**Amaç:** "bende derlenmiyor" durumunu tek tıkla gidermek.\
**Yaygın pratik:** taze bir klondan, yarım kalmış bir paket indirmesinden ya da karışmış .NET sürümlerinden sonra
derleme anlaşılmaz hatalar verir; bin ve obj klasörleri elle silinir, paketler yeniden indirilir, bazen depo
baştan klonlanır.\
**Build Orchestrator'da:** bir düğme çalışma alanını sıfırlar, diğeri eksik paketleri getirir ve kırık referansları
adıyla söyler; ardından liste kendiliğinden yenilenir.\
`temiz çalışma alanı için bin/obj silme` · `"bende derlenmiyor"u düzelten NuGet restore`

## Perf profilleri ve canlı maliyet

**Amaç:** makine derlerken kullanılabilir kalsın.\
**Yaygın pratik:** büyük bir çözüm bütün çekirdeklerle derlenirken editör, tarayıcı ve toplantı uygulaması takılır;
uzun derlemeler bu yüzden bir molayı bekler.\
**Build Orchestrator'da:** derlemenin makineden ne kadar alacağı tek tık uzaktadır; varsayılan profilde pencere ve
diğer uygulamalar akıcı kalır, derlemenin kullandığı kaynak title bar'da görünür.\
`makine payını sınırlayan Job Object CPU sınırı` · `canlı şerit için job muhasebesi`

## Boşta hafif, yükte akıcı

**Amaç:** gün boyu açık kalıp makineye yük olmayan bir araç.\
**Yaygın pratik:** arka planda açık bırakılan geliştirici araçları zamanla yüzlerce megabayt bellek ve sürekli biraz
işlemci tutar; bu yüzden gerektiğinde açılıp sonra kapatılır.\
**Build Orchestrator'da:** uygulama tepside kalabilir: iş yokken neredeyse hiç işlemci kullanmaz, belleğin çoğunu
geri verir ve sonraki derlemeye birkaç saniyede hazırdır.\
`bellek için boşta motor uykusu` · `görünürlüğe bağlı animasyon, boşta sıfıra yakın CPU`

## Sakin ve erişilebilir tasarım

**Amaç:** derlemeyi izlemeyi bir konsol metni duvarı olmaktan çıkarıp keyifli kılmak.\
**Yaygın pratik:** derleme, sonuna kadar hiçbir şeyin öne çıkmadığı akan bir konsolda izlenir ve sonuç metnin
içinden okunur.\
**Build Orchestrator'da:** her projenin durumu bir renk, bir sembol ve bir kelimeyle gösterilir, koşu liste ve
grafta sakin biçimde akar, arayüz Windows'un animasyon ve erişilebilirlik ayarlarına uyar.\
`tutarlı görünüm için tasarım token'ları` · `erişilebilir hareket için işletim sistemi reduced-motion sinyali`
