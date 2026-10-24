[English](WHY.md) | Türkçe

# Neden Build Orchestrator

Aracın iş gününde neyi değiştirdiği, madde madde; [FEATURES.tr.md](FEATURES.tr.md) ile aynı sırada ve aynı
başlıklarla. Her maddenin arkasındaki mekanizma orada anlatılır.

**Kısaca, ekip için:**

- **Daha az bekleme.** Bir değişiklik yalnız dokunduğunu derletir, paralel ve fazlası olmadan.
- **Eski DLL sürprizi yok.** Neyin neden derleneceği Build'e basılmadan ekrandadır; kırık bir bağımlılığa karşı
  derlenmiş proje işaretlenir.
- **Bakımı yapılacak derleme script'i yok.** Sıra, döngüler dahil, deponun kendisinden çıkar.
- **Kullanılır kalan bir makine.** Derleme makineden yalnız kendisine verilen payı alır.
- **Arkandan hiçbir şeye dokunulmaz.** Git'e yalnız bir tıklamayla yazılır, çıktı tam Visual Studio'nun koyduğu
  yerde kalır.

Her madde amacı, yaygın pratiği ve Build Orchestrator'da bunun yerine ne olduğunu verir. *Yaygın pratik* satırları,
çok projeli bir .NET çözümünün böyle bir araç yokken genelde nasıl derlendiğini anlatır: elle Visual Studio'da,
kendi yazılmış bir script'le ya da her geliştiricinin kendi yöntemiyle. Genel bir resimdir, belirli bir ekibin
kaydı değildir.

## Bir bakışta

### Bütün çözüm tek ekranda

**Amaç:** bütün çözüm gözünün önünde olsun; derleme beklemek, izleyip yönettiğin bir işe dönüşsün.\
**Yaygın pratik:** büyük bir derleme akıp giden bir konsoldur; hangi projelerin derlendiği, hangisinin kırıldığı ve
neyin kaldığı sonradan metinden toparlanır, bekleme de başka bir şeyle geçirilen ölü zamandır.\
**Build Orchestrator'da:** her proje durumuyla ekrandadır, koşu liste ve grafta olduğu anda akar: derlenen parlar,
biten yeşile, kırılan her panelde aynı anda kırmızıya döner. Tek satır log okumadan derlemenin nerede olduğunu
bilirsin.\
`graf, liste, konsol ve olaylar için tek pencere` · `her panelde ortak seçim`

### Bağımlılık grafını kendisi çıkarır

**Amaç:** hangi projenin hangisine bağlı olduğunu hiçbir yere yazmamak, güncel tutmakla da uğraşmamak.\
**Yaygın pratik:** derleme sırası bir script'te, bir wiki sayfasında ya da en eskilerin aklında durur; yeni bir
proje ya da referans eklenince liste eskir, derleme ya kırılır ya da sessizce eski bir DLL'i alır, ta ki biri fark
edene kadar.\
**Build Orchestrator'da:** sıra her seferinde deponun kendisinden okunur: bir proje ya da referans ekle, bir sonraki
Sync onu yerine koymuş olur. Bakımı yapılacak bir şey yoktur; yerleştirilemeyen referans da sessizce atlanmaz,
adıyla söylenir.\
`gerçek graf için HintPath haritası` · `saniyeler süren ham XML keşfi`

### Yalnız değişeni derler

**Amaç:** tek dosyalık bir değişiklik, bir kahve molasına değil, tek projelik bir derlemeye mal olsun.\
**Yaygın pratik:** paylaşılan bir kütüphanede tek bir sınıfı düzeltirsin, Build'e basarsın ve bütün zincirin
derlenmesini izlersin, çünkü hangi projelerin buna gerçekten ihtiyacı olduğunu kimse söyleyemez; ya da yalnız önemli
sandığını derlersin ve eski bir DLL çalışma anında ortaya çıkar.\
**Build Orchestrator'da:** daha hiçbir şeye basmadan liste hangi projelerin neden derleneceğini söyler; Build tam
olarak onları alır, gerisini atlar, derlenecek hiçbir şey yoksa da bunu söyler.\
`kaynak içeriği hash'i` · `oturumlar arası derleme defteri`

### Sırayla ve paralel derler

**Amaç:** aynı derlemeyi, boş duracak çekirdekleri de çalıştırarak daha kısa sürede ve sırayı hiç şaşırmadan
bitirmek.\
**Yaygın pratik:** projeler sabit bir sırayla birbiri ardına derlenir; öncekisi bitmeden hiçbiri başlamaz,
çekirdeklerin çoğu bekler, ortadaki tek bir kırık proje ilgili ilgisiz arkasındaki her şeyi durdurur.\
**Build Orchestrator'da:** birbirinden bağımsız projeler yan yana derlenir, her biri bağımlılıkları yeşile döndüğü
anda başlar; kırık bir proje yalnız gerçekten ona bağlı olanları bekletir, derlemenin geri kalanı sürer.\
`paralel derleme için hazır-küme zamanlayıcı` · `tekrarlanabilir koşu için sabit sıra`

### Bağımlılık döngülerini çözer

**Amaç:** döngüsel referanslı bir çözümü tek seferde ve ritüelsiz derlenebilir tutmak.\
**Yaygın pratik:** projeler birbirinin DLL'ine referans verdiğinde aynı projeler, birinin bir zamanlar çıkardığı
bir sırayla birkaç kez derlenir; arada bir adım atlanır ve hangi projenin eski bir DLL'e karşı derlendiği hiçbir
zaman tam belli olmaz.\
**Build Orchestrator'da:** döngü aynı Build içinde oturur, tek ihtiyacın buysa Resolve cycles ile tek başına da
oturur; girdisi değişmeyen üye yeniden derlenmez, sonuç döngünün oturup oturmadığını ya da hangi projenin
durdurduğunu açıkça söyler.\
`döngü tespiti için Tarjan SCC` · `oturmanın kanıtı API yüzey hash'i` · `yalnız döngü koşusu için Resolve cycles`

### Makineyi kullanılır tutar

**Amaç:** derlerken çalışmaya devam etmek.\
**Yaygın pratik:** büyük bir çözümü bütün çekirdeklerle aynı anda derlemek makineyi kilitleyebilir: editör takılır,
tarayıcı donar, içinde olduğun toplantının sesi kesilir; uzun derlemeler bu yüzden öğle arasını ya da gün sonunu
bekler.\
**Build Orchestrator'da:** derleme makineden yalnız chip'e tek tıkla ona verdiğin payı alır, bulduğu çekirdeklere
ve boş belleğe de kendini uydurur; varsayılan profilde editör ve diğer uygulamaların kullanılır kalır,
derlemenin o an ne kullandığı title bar'da görünür.\
`makine payını sınırlayan JOB_OBJECT_CPU_RATE_CONTROL sert tavanı` · `sayfalamaya karşı çekirdeğe ve boş belleğe göre worker bütçesi` · `canlı CPU değeri için QueryInformationJobObject muhasebesi`

### Yerli bir Windows uygulaması

**Amaç:** gün boyu açık kalabilen ve başlattığı her süreci sıkı tutan bir masaüstü aracı.\
**Yaygın pratik:** derleme yardımcıları genelde komut satırı script'leridir; sırayla derleyebilir ama ne graf, ne
gerekçe, ne canlı durum gösterir. Web teknolojisiyle yazılmış bir masaüstü aracı bunların hepsini gösterebilir ama
yanında bir tarayıcı çalışma zamanı getirir ve başlattığı derleyicileri denetlemek için yine yerli koda ihtiyaç
duyar.\
**Build Orchestrator'da:** pencere, içinde tarayıcı olmayan yerli WPF'tir; motor kendi sürecinde, Windows'un kendi
süreç denetimi altında koşar: uygulama tepside bekler, global kısayola cevap verir, kapanınca başlattığı her
derleyici de onunla kapanır.\
`.NET 10 üzerinde WPF, tarayıcı çalışma zamanı yok` · `süreç denetimi için Win32 P/Invoke`

## Ayrıntılar

### Her projenin neden derleneceğini söyler

**Amaç:** bir projenin neden derleneceğini ya da neden derlenmeyeceğini derleme başlamadan bilmek.\
**Yaygın pratik:** IDE bir projeyi neden yeniden derlediğini ya da neden atladığını söylemez; şüphe olunca her şey
temizlenip baştan derlenir ya da derleme çıktısında sebep aranır.\
**Build Orchestrator'da:** her projenin adının yanında tek kelimelik bir gerekçe durur; kırmızı yalnız gerçekten
derlenmeyen kod için yanar, bu yüzden kırmızı satır her zaman dikkatini hak eder.\
`modified ile affected ayrımı için içerik parmak izi` · `kanıta dayalı kırmızı, yanlış alarm yok`

### Bayat bağımlılığı gösterir

**Amaç:** kırık bir bağımlılığa karşı derlenmiş projeleri hemen görmek, onları boşuna yeniden derlemeden.\
**Yaygın pratik:** bir kütüphane derlenmediğinde üstündeki projeler onun eski DLL'iyle derlenmeye devam eder;
uygulama çalışıyor görünür ve sorun çok sonra ortaya çıkar, ya da güvenmek için her denemede bütün zincir yeniden
derlenir.\
**Build Orchestrator'da:** kırık bir bağımlılığın arkasında bekleyen projeler tek bakışta görülen amber bir işaret
taşır, bağımlılık düzelince sonraki Build'e kendiliğinden katılırlar.\
`hedefli yeniden derleme için kök notu` · `tek amber uyarı kanalı`

### API değişmediyse bağımlıyı atlar

**Amaç:** paylaşılan bir kütüphanenin derinindeki küçük bir düzeltme, üstündeki her şeyi derletmesin.\
**Yaygın pratik:** paylaşılan bir kütüphanedeki tek satırlık düzeltme ona bağlı her projeyi yeniden derletir;
katmanlı bir çözümde bu tek değişiklik uzun bir beklemeye dönüşür.\
**Build Orchestrator'da:** iç değişiklikte yalnız kütüphanenin kendisi derlenir, üstündeki projeler yeşil kalır;
public bir imza değişince onlar da derlenir, derlenmesi gereken hiçbir şey atlanmaz.\
`derlenmiş çıktının API yüzey hash'i` · `paylaşılan kütüphanede zincirleme derlemeyi kesme`

### Visual Studio ile yan yana

**Amaç:** istediğinde Visual Studio'da derlemeye devam etmek, o emeği çöpe atmadan.\
**Yaygın pratik:** ayrı bir derleme aracı IDE'nin zaten derlediğini yeniden derler; geliştiriciler sonunda birini
seçip ötekinden kaçınır.\
**Build Orchestrator'da:** Visual Studio'da derlediğin projeler yeşil görünür ve atlanır; yalnız Visual Studio'nun
görmediği bir bağımlılık değiştiyse yeniden derlenir.\
`başkasının çıktısını tanıma` · `bayat kopyayı yakalayan üst zincir`

### Visual Studio'nun derlediği gibi derler

**Amaç:** her seferinde Visual Studio'nun vereceği sonucu almak.\
**Yaygın pratik:** farklı bir derleme komutu çağıran ya da farklı argüman geçen script'ler "VS'de derleniyor,
script'te derlenmiyor" türünden farklar çıkarır; eski .NET Framework projelerinde bu daha sık olur.\
**Build Orchestrator'da:** projeler tam Visual Studio'nun derlediği gibi derlenir, eksik paketler yolda restore
edilir, makinede ek bir paket aracı gerekmez.\
`MSBuild'i bulmak için vswhere` · `proje başına ayrı MSBuild.exe süreci`

### Arkada süreç bırakmaz

**Amaç:** derleme durduğunda ya da araç kapandığında arkada çalışmaya devam eden derleyici kalmasın.\
**Yaygın pratik:** yarıda kesilen bir derlemeden sonra arkada kalan derleyici süreçleri dosyaları kilitli tutar;
bunlar Task Manager'da tek tek avlanır ya da makine yeniden başlatılır.\
**Build Orchestrator'da:** uygulama nasıl kapanırsa kapansın, çökse bile, derleyicileri birkaç saniye içinde onunla
kapanır; sonraki derleme kilitli bir dosyaya takılmaz.\
`yetim sürece karşı iç içe Job Object` · `zincirleme kapanış için kill-on-close`

### Güvenle durur, kaldığı yerden sürer

**Amaç:** derlemeyi istenen anda, bozuk çıktı bırakmadan durdurmak ve sonra oradan devam etmek.\
**Yaygın pratik:** uzun bir derlemeyi yarıda kesmek yarım yazılmış çıktılar ve hangi projelerin bittiğine dair
belirsizlik bırakır; güvenmek için sonrasında her şey yeniden derlenir.\
**Build Orchestrator'da:** Stop derlenmekte olan projelerin temiz bitmesine izin verir, yeniden Build'e basınca
yalnız bitmeyenler derlenir.\
`tutarlı çıktı için proje sınırında boşaltma` · `doğru devam için kayıt geçersizleme`

### Yarım kalan işi toparlar

**Amaç:** bir çökmeden, zorla kapanan bir oturumdan ya da öldürülen bir süreçten sonra neyin derlendiğine dair
şüphe kalmasın.\
**Yaygın pratik:** makine kilitlenir ya da derleme yarıda öldürülürse yarım yazılmış çıktılar kalır; tek güvenli
yol her şeyi temizleyip yeniden derlemektir.\
**Build Orchestrator'da:** sonraki açılışta konsol hangi projelerin yeniden derleneceğini söyler ve yalnız onlar
derlenir.\
`yarım çıktıyı bulan uçuştaki-iş defteri` · `bozuk dosyaya karşı atomik yazım`

### Çıktı klasörlerine dokunmaz

**Amaç:** aracı kullanmaya başlarken hiçbir şeyin derlendiği ya da dağıtıldığı yeri değiştirmemek.\
**Yaygın pratik:** bazı derleme script'leri çıktıyı kendi klasörlerine yönlendirir ya da kopyalar; IDE ile script
farklı yerlere yazınca hangi DLL'in güncel olduğundan kimse emin olamaz.\
**Build Orchestrator'da:** çıktı her zaman Visual Studio'nun koyduğu yerdedir; aracı kullanmaya başlamak ya da
bırakmak çıktı düzeninde hiçbir şeyi değiştirmez, denemenin hiçbir bedeli yoktur.\
`değişmeyen çıktı yolu, VS ile aynı düzen` · `çıktıyı yalnız okuma`

### Git'e kendiliğinden yazmaz

**Amaç:** geçmişine ve commit'lenmemiş işine asla dokunmayan bir derleme aracı.\
**Yaygın pratik:** güncel olmak için derlemeden önce pull ya da reset yapan script'ler zaman zaman yerel
değişiklikleri ezer ya da beklenmedik merge'ler bırakır; bu yüzden ekipler derleme script'ine git adımı koymaktan
çekinir.\
**Build Orchestrator'da:** depoya yalnız sen bir chip'e tıkladığında ve yalnız güvenli yoldan yazılır; Build ve
Sync depoyu sadece okur.\
`denetlenebilirlik için tek git yazıcısı` · `yalnız fast-forward, geçmiş korunur`

### Duruma veya ada göre süzer

**Amaç:** hatalı projelere, derlenenlere ya da adı bilinen bir projeye tek tıkla ulaşmak.\
**Yaygın pratik:** uzun bir derleme çıktısında hangi projelerin hata verdiğini bulmak metni kaydırmak ya da aramak
demektir; henüz neyin derlenmediğini anlamak elle karşılaştırma ister.\
**Build Orchestrator'da:** bir sayaca tek tık yalnız hatalı ya da yalnız güncel projeleri gösterir, birkaç harf
yazmak bir projeyi adıyla bulur; graf da geri kalan her şeyi soldurur.\
`tek tık filtre olarak sayaç chip'leri` · `projeyi hızlı bulmak için ad filtresi`

### Satırdan derle, aç, göster

**Amaç:** kırmızı bir satırdan koduna ya da o tek projenin derlemesine tek tıkla geçmek.\
**Yaygın pratik:** projenin hangi çözümde olduğunu bulursun, IDE'de açarsın, projeyi Solution Explorer'da arayıp
derlersin; ya da klasörünü diskte ararsın.\
**Build Orchestrator'da:** satırdaki düğme bütün koşuyu beklemeden yalnız o projeyi derler; aynı satır çözümünü
Visual Studio'da ya da klasörünü Explorer'da açar.\
`hızlı deneme için tek proje koşusu` · `doğru .sln için çözüm eşlemesi`

### Grafı canlı gösterir

**Amaç:** "bu proje neye dokunuyor?" sorusunu tek tıkla cevaplamak ve derlemenin nerede olduğunu tek bakışta
görmek.\
**Yaygın pratik:** bir projenin neye bağlı olduğunu ve onda yapılacak bir değişikliğin neyi etkileyeceğini öğrenmek
proje dosyalarını tek tek açmak ya da ekipte en uzun süredir çalışana sormak demektir.\
**Build Orchestrator'da:** bir projeye tıkla: kullandıkları ve onu kullananlar bağlantılarıyla öne çıkar; derleme
sırasında graf hangi projelerin derlendiğini, hangilerinin düştüğünü ve hangilerinin bir döngüde olduğunu
gösterir.\
`bağımlılık incelemek için odakla-sığdır` · `koşuyu izleten opaklık anlatısı`

### Hatayı gösterir, gürültüyü değil

**Amaç:** derlemeyi kıran hataya, uyarıların arasında boğulmadan ulaşmak.\
**Yaygın pratik:** büyük bir çözümün derleme çıktısı sayfalar dolusu uyarı taşır; tek hata kaydırarak, arayarak ya
da çıktıyı bir editöre yapıştırarak bulunur.\
**Build Orchestrator'da:** konsol hataları tam, uyarıları kısa bir özet olarak gösterir; kırmızı projeye tıkla,
logu baştan açılır ve ilk hata hemen önündedir.\
`uyarı gürültüsünü kesen Compact console` · `hatayı baştan okutan proje sayfası`

### Derlemenin neresinde olduğunu hep söyler

**Amaç:** derlemenin her an ne yaptığını ve ne kadar kaldığını bilmek.\
**Yaygın pratik:** komut satırından yapılan derleme bir metin akışı basar; nereye geldiği ya da ne kadar süreceği
akan yazıdan tahmin edilir.\
**Build Orchestrator'da:** üstteki satır her zaman fazı, ilerlemeyi ve kalan süreyi verir; oradaki chip'ler o an
derlenen projeleri, koşu bitince de hatalıları adıyla gösterir, tek tık uzaklıkta.\
`kalan süre tahmini` · `hatalıya tek tık götüren chip'ler`

### Her yerden çalışan kısayollar

**Amaç:** editörden ayrılmadan derleme başlatmak.\
**Yaygın pratik:** ayrı bir derleme aracı ya da script'le her derleme, editörden o pencereye geçmek, komutu
çalıştırmak ve geri dönmek demektir.\
**Build Orchestrator'da:** Visual Studio'da çalışırken tek kısayol arka planda derlemeyi başlatır; geçilecek bir
pencere yoktur, sonuç bildirim olarak gelir.\
`arka plandan derlemek için global hotkey` · `Visual Studio gibi F5/F6/F7`

### Tepside yaşar

**Amaç:** derleme aracı gün boyu hazır olsun, ama ne bir pencere ne de dikkatini işgal etsin.\
**Yaygın pratik:** bir derleme script'i her seferinde terminalden başlatılır; bitip bitmediğini öğrenmek o
terminale dönüp bakmayı gerektirir.\
**Build Orchestrator'da:** uygulama istenirse Windows ile başlar ve tepside bekler; ekran köşesindeki küçük işaret
derlemenin sürdüğünü gösterir, nasıl bittiğini bir bildirim söyler.\
`arka planda çalışmak için tepsi ikonu` · `sonuç için Windows bildirimi`

### Kendiliğinden Sync

**Amaç:** "neyin derleneceği" bilgisini sormaya gerek kalmadan, her an depoyla uyumlu tutmak.\
**Yaygın pratik:** branch değiştirdikten ya da pull yaptıktan sonra hangi projelerin etkilendiği belli olmaz; ya
her şey derlenir ya da akla gelenler.\
**Build Orchestrator'da:** Visual Studio'da ya da terminalde yapılan commit, pull ya da branch değişikliğinden sonra
liste birkaç saniye içinde kendiliğinden güncellenir; Sync düğmesine nadiren gerek kalır.\
`Sync'e nadiren bastıran HEAD izleme` · `ekranı bozmayan sessiz Sync`

### Harici projeler, tek graf

**Amaç:** başka depolarda duran paylaşılan kütüphaneleri ana çözümle birlikte, doğru sırayla derlemek.\
**Yaygın pratik:** ortak kütüphaneler ayrı bir depodaysa önce o depo güncellenip derlenir, sonra ana çözüm; bir adım
unutulursa farkında olmadan eski bir kütüphaneyle çalışılır.\
**Build Orchestrator'da:** başka depolardaki projeler aynı listenin en üstünde görünür ve önce onlar derlenir, açık
bıraktıysan öncesinde güncellenir; commit'lenmemiş iş varsa araç güncellemek yerine durur.\
`çok kökten tek grafa tarama` · `her Build öncesi fast-forward güncelleme`

### Bozuk çalışma alanını onarır

**Amaç:** "bende derlenmiyor" diyen bir çalışma alanını, depoyu yeniden klonlamadan, tek tıkla yeniden derlenir
hale getirmek.\
**Yaygın pratik:** bir branch değişikliğinden, .NET sürümü değişiminden ya da yarım kalmış bir paket indirmesinden
sonra derleme, sebebin yakınından bile geçmeyen hatalar verir: obj'de başka bir framework'ün artıkları ya da diskte
olmayan paketler. bin ve obj elle silinir, paketler yeniden indirilir, bazen depo baştan klonlanır.\
**Build Orchestrator'da:** Deep Clean tek tıkla her projenin derleme çıktısını siler, Optimize eksik paketleri geri
getirir ve hâlâ kırık olanı adıyla söyler; liste kendiliğinden yenilenir, sonraki Build temiz başlar. Derlemeyi
bozacak artıklar daha koşu başında, sana bir öğleden sonraya mal olmadan işaretlenir.\
`temiz çalışma alanı için bin/obj silme` · `"bende derlenmiyor"u düzelten NuGet restore` · `koşu başı uyarısı, hiçbir şey silinmez`

### Boşta hafif, yükte akıcı

**Amaç:** gün boyu açık kalıp makineye yük olmayan bir araç.\
**Yaygın pratik:** arka planda açık bırakılan geliştirici araçları zamanla bellek ve sürekli biraz işlemci tutar;
bu yüzden gerektiğinde açılıp sonra kapatılır.\
**Build Orchestrator'da:** uygulama tepside kalabilir: iş yokken neredeyse hiç işlemci kullanmaz, belleğin çoğunu
geri verir ve sonraki derlemeye birkaç saniyede hazırdır.\
`bellek için boşta motor uykusu` · `görünürlüğe bağlı animasyon, boşta sıfıra yakın CPU`

### Sakin ve erişilebilir tasarım

**Amaç:** gözü ya da Windows ayarları ne olursa olsun, ekipteki herkesin okuyabildiği bir ekran.\
**Yaygın pratik:** birçok araç başarıyı ve hatayı yalnız kırmızı ve yeşille ayırır, bunları herkes ayırt edemez;
animasyonları da sistemin hareket ayarı ne derse desin oynar.\
**Build Orchestrator'da:** her durum bir renk, bir sembol ve bir kelimeyle söylenir, renk olmadan da okunur; hareket
Windows'un animasyon ayarına uyar, ekran okuyucu her kontrol için bir ad ve gerçek bir rol alır.\
`erişilebilir hareket için işletim sistemi reduced-motion sinyali` · `ekran okuyucu için UI Automation adları`
