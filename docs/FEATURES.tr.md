[English](FEATURES.md) | Türkçe

# Özellikler

Build Orchestrator'ın ne yaptığı ve nasıl yaptığı. Bir bakışta altındaki maddeler büyük resimdir; Ayrıntılar
altındakiler en önemliden başlayarak gelir. Her madde kısa bir cümle, arkasındaki mekanizma ve tekniği adlandıran birkaç etiketten oluşur.
Bunların her birinin neden önemli olduğu [WHY.tr.md](WHY.tr.md)'de, adım adım kullanımı
[Kullanım kılavuzu](USER-GUIDE.md)'nda (İngilizce), tam teknik referans [ARCHITECTURE.md](../ARCHITECTURE.md)'dedir
(İngilizce).

## Bir bakışta

### Bütün çözüm tek ekranda

**Ne yapar:** Bağımlılık grafı, proje listesi, konsol ve olay akışı sakin, koyu tek bir pencereyi paylaşır; bütün
çözüm ve süren koşu gözünün önünde kalır: her projenin durumu, o an neyin derlendiği, neyin neden düştüğü. Derleme,
akıp giden bir metin duvarı olmaktan çıkar; satır satır, düğüm düğüm ilerleyişini izlediğin bir şeye dönüşür.\
**Nasıl:** Üstte title bar ve durum şeridi, solda graf ve liste, sağda konsol ve olay akışı, altta aksiyon çubuğu;
üç düzen (Quad, List, Focus) ve sürüklenebilir ayraçlar hatırlanır. Her panel aynı proje durumlarından boyanır, tek
tık bir projeyi hepsinde seçer, gizli graf çizim yapmaz.\
`graf, liste, konsol ve olaylar için tek pencere` · `her panelde ortak seçim` · `odak seçmek için düzen modları` · `hatırlanan ayraçlar`

### Bağımlılık grafını kendisi çıkarır

**Ne yapar:** Depo kökünü göstermen yeter; her projeyi ve projelerin birbirine nasıl bağlı olduğunu kendisi bulur.
Elle tutulacak proje listesi, script'te bakımı yapılacak derleme sırası yoktur: yeni bir proje ya da yeni bir
referans bir sonraki Sync'te plandadır.\
**Nasıl:** Depo kökü altındaki her .csproj ham XML olarak, her .sln listelediği projeler için okunur; harici
kartların gösterdiği projeler de bunlara katılır, keşif için MSBuild hiç değerlendirilmez. Bağımlılıklar, DLL'i
üreten projeyle eşleştirilen HintPath'lerden ve ProjectReference'lardan çıkar. Başka bir depoda derlenen bir DLL, o
depo harici kart olarak eklenince gerçek bağımlılığa dönüşür; iki projenin sahiplendiği bir DLL tahmin edilmez,
adıyla bildirilir.\
`saniyeler süren ham XML keşfi` · `gerçek graf için HintPath haritası` · `ikinci sinyal olarak ProjectReference` · `boyut ve tarih anahtarlı değerlendirme önbelleği`

### Yalnız değişeni derler

**Ne yapar:** Tek bir dosyayı değiştir: Build o projeyi ve onu kullanan projeleri derler, başka hiçbir şeyi; geri
kalan her şey güncel diye atlanır, hiçbir şey değişmediyse koşu derlenecek bir şey olmadığını söyleyip neredeyse
hemen biter.\
**Nasıl:** Her projenin diskteki girdileri (.csproj, öğeleri, en yakın Directory.Build dosyaları) hash'lenir ve son
başarılı derlemesiyle karşılaştırılır. Git'e bakılmaz, çıktının tarihi tek başına "güncel" demeye yetmez; silinmiş
ya da üzerine yazılmış çıktı, kaynak aynı olsa da yeniden derlenir.\
`kaynak içeriği hash'i` · `boyut ve tarih anahtarlı özet önbelleği` · `silinen DLL'i yakalayan çıktı kanıtı` · `oturumlar arası derleme defteri`

### Sırayla ve paralel derler

**Ne yapar:** Projeler gerçek bağımlılık sırasıyla derlenir, birbirini beklemeyenler aynı anda derlenir. Bir hata
koşuyu durdurmaz: ona bağlı projeler onun son sağlam çıktısına karşı derlenir ve işaretlenir, geri kalanlar sürer.\
**Nasıl:** Graf, eşitlikte sabit bir kuralla topolojik sıraya dizilir; aynı depo her zaman aynı planı verir.
Boşalan her worker bağımlılıkları bitmiş sıradaki projeyi alır ve her proje kendi MSBuild.exe'sinde koşar.\
`paralel derleme için hazır-küme zamanlayıcı` · `derleme sırası için Kahn topolojik sıralaması` · `tekrarlanabilir koşu için sabit sıra`

### Bağımlılık döngülerini çözer

**Ne yapar:** Döngüsel referanslar planı bozmaz. Birbirinin çıktısına referans veren projeler tek birim olarak,
okudukları API oturana kadar gereken en az turda birlikte derlenir; sonuç döngünün oturup oturmadığını ya da hangi
projenin durdurduğunu açıkça söyler.\
**Nasıl:** Döngüler güçlü bağlı bileşenler olarak bulunur ve tek bir imzayı paylaşır. Build onları bütün koşunun
içinde oturtur; bakım kutusunun üçüncü düğmesi Resolve cycles ise yalnız döngüleri ve ihtiyaç duydukları bayat
projeleri koşturur. İlk tur yalnız gereken üyeleri derler, bir üye ancak okuduğu kardeş API'si değiştiyse sabit bir
tavana kadar yeniden derlenir, kök bağımlılığı hâlâ kırık olan grup da turlarını boşuna yeniden ödemek yerine
atlanır.\
`döngü tespiti için Tarjan SCC` · `yazılırken DLL okumayan bariyerli turlar` · `oturmanın kanıtı API yüzey hash'i` · `yalnız döngü koşusu için Resolve cycles`

### Makineyi kullanılır tutar

**Ne yapar:** Derleme makinenin tamamını almaz: varsayılan olarak editöre, tarayıcıya ve açık olan her şeye
işlemci zamanı ve bellek bırakır. Perf chip'i Full, Balanced (varsayılan) ve Light arasında geçer; title bar'ın
ortasındaki kaynak şeridi aracın o anki maliyetini gösterir: CPU, bellek ve profiliyle paralellik.\
**Nasıl:** Profil paralelliği belirler; yalnız derleyicileri taşıyan job'a da bir öncelik sınıfı ve Full dışında
sert bir CPU tavanı koyar; pencere, git ve Sync tam hızda kalır. Koşu başında worker sayısı işlemcilere ve boş
belleğe göre ayarlanır, makinenin geri kalanına bir pay bırakılır. Kaynak şeridi işlemci zamanını job'ın kendi
accounting sayaçlarından, belleği private bytes olarak okur; makinedeki süreçleri listelemez.\
`makine payını sınırlayan JOB_OBJECT_CPU_RATE_CONTROL sert tavanı` · `sayfalamaya karşı çekirdeğe ve boş belleğe göre worker bütçesi` · `canlı CPU değeri için QueryInformationJobObject accounting'i` · `canlı RAM değeri için GetProcessMemoryInfo private bytes`

### Native bir Windows uygulaması

**Ne yapar:** Build Orchestrator, WebView2 hibriti, Avalonia ve WinUI 3 yerine seçilen WPF ile yazılmış bir Windows
masaüstü uygulamasıdır. İçinde tarayıcı çalışma zamanı yoktur; tepsi, bildirimler, global kısayollar ve başlattığı
her derleyici üzerindeki denetim Windows'un kendisinindir.\
**Nasıl:** Arayüz WPF ve .NET 10 üzerinde, derleme motoru ayrı bir süreçte koşar; MSBuild ne kadar çıktı üretirse
üretsin pencere akıcı kalır. Süreç denetimi doğrudan Win32'ye gider: derleyiciler için Job Object ve CreateProcessW,
global kısayollar için RegisterHotKey. Uygulama Velopack ile kullanıcı başına kurulur.\
`.NET 10 üzerinde WPF, tarayıcı çalışma zamanı yok` · `süreç denetimi için Win32 P/Invoke` · `ayrı süreçlerde motor ve arayüz`

## Ayrıntılar

### Her projenin neden derleneceğini söyler

**Ne yapar:** Bir şeyin neden yeniden derlendiğini tahmin etmek bitti: koşu başlamadan her satır, projesinin neden
derleneceğini (modified, modified · local, affected, never built, failed) ya da güncel olduğunu söyler; ipucu
ayrıntıyı verir.\
**Nasıl:** Sync, tam bir Build'in vereceği kararı önizler: projenin kendi dosyaları değiştiyse modified, yalnız bir
bağımlılığı değiştiyse affected, değişiklik git'te de commit'lenmemişse local. Kırmızı yalnız bugünkü kaynak için
kanıtlanmış derleyici hatası demektir.\
`modified ile affected ayrımı için içerik parmak izi` · `local işareti için git status` · `kanıta dayalı kırmızı, yanlış alarm yok`

### Bayat bağımlılığı gösterir

**Ne yapar:** Bir proje, sonradan kırılan bir bağımlılığının son sağlam çıktısına karşı derlendiyse satırındaki
amber üçgen bunu söyler; proje her Build'de boşuna yeniden derlenmez, kök düzelince derlenir.\
**Nasıl:** Kırık kök projelerin adları graf boyunca aşağı taşınır ve derleme defterine not edilir. Sonraki Build
notlu projeyi şartlı alır: kök düzeldiyse derler, kök hâlâ kırıksa atlar.\
`hedefli yeniden derleme için kök notu` · `boşa tur atmayan şartlı derleme` · `tek amber uyarı kanalı`

### API değişmediyse bağımlıyı atlar

**Ne yapar:** Bir bağımlılıkta yalnız metot gövdeleri değiştiyse üstündeki projeler yeniden derlenmez; public API
değiştiyse derlenir.\
**Nasıl:** Bağımlı bir proje derlenmeden hemen önce her doğrudan bağımlılığın derlenmiş çıktısındaki bildirim
yüzeyi hash'lenir ve projenin son derlendiği yüzeyle karşılaştırılır. Hepsi aynıysa proje atlanır ve hemen yeşile
döner.\
`derlenmiş çıktının API yüzey hash'i` · `paylaşılan kütüphanede zincirleme derlemeyi kesme` · `atlama nedeni olay akışında`

### Visual Studio ile yan yana

**Ne yapar:** Visual Studio'da ya da komut satırında zaten derlediğin proje boşuna yeniden derlenmez: built outside
this tool diye yeşil görünür; bu Build'de bir bağımlılığı değişirse affected olarak derlenir.\
**Nasıl:** Projenin çıktısı aracın son koşusundan yeniyse çıktı başkasınındır; hiçbir girdisi o çıktıdan yeni
değilse proje güncel sayılır.\
`başkasının çıktısını tanıma` · `çifte derlemeye karşı girdi-çıktı tarihi` · `bayat kopyayı yakalayan üst zincir`

### Visual Studio'nun derlediği gibi derler

**Ne yapar:** Her proje Visual Studio'nun kullandığı MSBuild.exe ile, kendi sürecinde derlenir; eski .NET Framework
projeleri IDE'deki gibi derlenir, MSBuild bulunamazsa sessiz takılma değil açık hata çıkar.\
**Nasıl:** MSBuild.exe vswhere ile bulunur, her proje referanslarını yeniden derlemeden bir alt süreç olarak koşar;
dotnet build ve süreç içi MSBuild yoktur. packages.config projeleri derlemeden önce MSBuild ile restore edilir.\
`MSBuild'i bulmak için vswhere` · `proje başına ayrı MSBuild.exe süreci` · `çifte derlemeye karşı BuildProjectReferences=false` · `nuget.exe'siz MSBuild restore`

### Build, Rebuild ve Clean

**Ne yapar:** Ana düğme Build'dir; menüsünde önbelleği yok sayıp her projeyi derleyen Rebuild ve Visual Studio'nun
Clean Solution'ı gibi her projede MSBuild'in kendi temizliğini çalıştıran Clean vardır; Debug | Release anahtarı
konfigürasyonu seçer.\
**Nasıl:** Her koşu o an checkout'lu çalışma ağacını seçili konfigürasyonla yeniden planlar. Konfigürasyon her
projenin imzasındadır, bu yüzden değiştirmek yalnız öteki konfigürasyonda derlenmiş olanı kirletir. Sync, Deep
Clean, Optimize, checkout ya da pull sürerken derleme komutları sıraya alınmaz, reddedilir.\
`her derleme komutu için split button` · `Clean Solution ile aynı -t:Clean` · `imzada konfigürasyon` · `çakışan işe karşı tek iş kapısı`

### Arkada süreç bırakmaz

**Ne yapar:** Uygulama kapansa da, çökse de, Task Manager'dan öldürülse de arkada hiçbir MSBuild.exe ya da onun
alt süreci kalmaz.\
**Nasıl:** Derleme motoru ve her MSBuild.exe, uygulamanın sahip olduğu iç içe Windows Job Object'lerinde yaşar;
uygulamanın son tutamacı kapanınca Windows bütün ağacı sonlandırır. Her alt süreç askıda başlar, job'a girer ve
ancak sonra çalışır; aradan hiçbir şey kaçmaz.\
`yetim sürece karşı iç içe Job Object` · `zincirleme kapanış için kill-on-close` · `boşluğu kapatan askıda başlatma`

### Güvenle durur, kaldığı yerden sürer

**Ne yapar:** Koşu sürerken Build düğmesi Stop olur: ilk basış yeni iş başlatmaz ve derlenmekte olanları bitirir,
ikinci basış (Stop now ya da Esc) onları hemen keser, sonraki Build yalnız kalanı derler.\
**Nasıl:** Yumuşak durdurma proje sınırında, post-build kopyaları dahil bekler ve biteni kaydeder. Sert durdurma
derleyicilerin bulunduğu job'ı sonlandırır ve yarıda kestiği projelerin kaydını geçersizler; sonraki Build onları
sıfırdan derler.\
`tutarlı çıktı için proje sınırında boşaltma` · `anında durdurma için job sonlandırma` · `doğru devam için kayıt geçersizleme`

### Yarım kalan işi toparlar

**Ne yapar:** Motor koşu ortasında ölürse (çökme, Task Manager, oturum kapatma) ya da koşu sırasında branch
değişirse yarım derlenen projeler bitmiş sayılmaz ve sonraki Build onları yeniden derler; ayarlar ve defterler asla
yarım yazılmış kalmaz.\
**Nasıl:** Derlenmekte olan projeler bir uçuştaki-iş defterine yazılır, sonraki açılışta orada kalan her şey
geçersizlenir. HEAD hareket ederse koşu kesilir; her durum dosyası geçici dosya ve yeniden adlandırmayla atomik
yazılır.\
`yarım çıktıyı bulan uçuştaki-iş defteri` · `branch değişince kesen HEAD izleme` · `bozuk dosyaya karşı atomik yazım`

### Çıktı klasörlerine dokunmaz

**Ne yapar:** Derleme çıktısı tam Visual Studio'nun koyacağı yere düşer: araç hiçbir çıktı yolunu değiştirmez,
OutDir'e kendisi bir şey yazmaz, kopyalamaz, silmez; her koşu o an checkout'lu tek çalışma ağacını derler.\
**Nasıl:** MSBuild'e OutDir, OutputPath ya da obj yolu geçilmez, çıktılar yalnız karar için okunur; projelerin
kendi post-build adımları çıktıyı Visual Studio'daki gibi dağıtır. Tek istisna senin istediğin Clean'dir: MSBuild
projenin kendi kaydettiği çıktıları siler.\
`değişmeyen çıktı yolu, VS ile aynı düzen` · `tek çalışma ağacı, kopya yok` · `çıktıyı yalnız okuma`

### Git'e kendiliğinden yazmaz

**Ne yapar:** Sync, Build ve arka plan işleri depoya asla yazmaz: pull, reset, switch, rebase, commit ve push
kendiliğinden çalışmaz; git'e yazan üç hareketin her biri senin kararındır: bir chip'e tıklaman ya da açık
bıraktığın bir ayar.\
**Nasıl:** Üç yazma yolu (N behind chip'inden fast-forward, branch chip'inden checkout ve açık bıraktıysan harici
çalışma kopyalarının güncellenmesi) tek dosyada durur, bir kaynak testi git yazma komutlarını başka her yerde
yasaklar. status, ref ve fetch gibi okumalar ayrı yoldan yapılır.\
`denetlenebilirlik için tek git yazıcısı` · `kazara yazmaya karşı kaynak guard'ı` · `yalnız fast-forward, geçmiş korunur` · `yalnız ref fetch, çalışma ağacına dokunmaz`

### Kendi motorunu izler

**Ne yapar:** Derleme motoru ölür, takılır ya da uyanamazsa şerit sorunu adlandırır ve Restart engine sunar; motor
dönünce Engine ready der ve açılış Sync'ini çalıştırır.\
**Nasıl:** Uygulama motor sürecini izler ve cevap beklenen anlarda (koşu başı, durdurma, Sync, bakım, checkout,
pull) motor susarsa amber uyarı gösterir. Hiçbir kilit kendiliğinden açılmaz; karar senindir.\
`motor ölümü için süreç izleme` · `yanlış alarma karşı cevap bekleme pencereleri` · `tek tıkla yeniden başlatma`

### Proje listesi tek bakışta

**Ne yapar:** Her satır projenin durumunu renk şeridi, nokta, glyph ve kelimeyle; adını, kararını, uyarısını ve
süresini yanında gösterir: yeşil güncel, düz gri derlenecek, kırmızı derleyici hatası, derlenen satır amber "nefes"
alır.\
**Nasıl:** Şerit, nokta, glyph ve graf düğümü tek bir durum değerinden boyanır. Koşu sırasında liste derlenen
sınırı sen kaydırana kadar görünürde tutar, birkaç saniye dokunulmazsa geri döner.\
`renk + glyph + metin, renk körü dostu` · `her yüzey için tek durum kaynağı` · `koşuyu izleyen takip modu` · `yapışkan katman başlıkları`

### Duruma veya ada göre süzer

**Ne yapar:** Aksiyon çubuğundaki sayaç chip'leri (tümü, derleniyor, güncel, hatalı, uyarılı) aynı zamanda
birleşebilen filtrelerdir; PROJECTS başlığındaki kutu (Ctrl+F) listeyi ada göre daraltır, graf da eşleşerek
soluklaşır.\
**Nasıl:** Chip'ler kendi aralarında VEYA, ad kutusuyla VE ile birleşir; ad araması büyük/küçük harf duyarsız alt
dizedir. Şeritteki hatalı proje chip'leri hatalı filtresini tek tıkla açar; filtre build ve Sync boyunca kalır.\
`tek tık filtre olarak sayaç chip'leri` · `hedefli liste için VEYA/VE birleşimi` · `projeyi hızlı bulmak için ad filtresi`

### Satırdan derle, aç, göster

**Ne yapar:** Satırın üstüne gelince Build this project, Build · Rebuild · Clean menüsü, Reveal in Explorer ve Open
in Visual Studio çıkar; sağ tık aynı menüyü açar, satırdan başlayan koşu değişmemiş olsa da yalnız o projeyi
derler.\
**Nasıl:** Koşu tam planlanır ve tek projeye indirilir; bağımlılıkları derlenmez, bayat olan satırda ve logda
bildirilir. Open in Visual Studio projenin çözümünü yeni bir Visual Studio'da açar, proje birden çok çözümdeyse
hangisi diye sorar; Reveal in Explorer .csproj'u seçili gösterir.\
`hızlı deneme için tek proje koşusu` · `gizli DLL riskine karşı bayat bağımlılık notu` · `devenv'i bulmak için vswhere` · `doğru .sln için çözüm eşlemesi`

### Grafı canlı gösterir

**Ne yapar:** Bütün projeler, her zaman panele sığan build sırası bantlarında küçük düğümler olarak çizilir. Bir
düğümü seçince graf onu doğrudan bağımlılıkları ve bağımlılarıyla birlikte panele sığdırır ve aralarındaki
bağlantıları çizer;
koşu grafta sakin bir ışık oyunu olarak akar, derlemenin nerede olduğunu tek satır okumadan görürsün.\
**Nasıl:** Bantlar listenin gruplarıyla aynıdır, kalıcı bir kenar ağı yoktur. Her düğüm satırıyla aynı durumdan
boyanır: hatalı projeler kırmızı kalır, döngüdeki projeler amber küp olarak görünür. Koşuda bekleyen düğümler söner,
derlenenler etraflarında amber noktalarla parlar, sonunda bu koşuda derlenen projeler tek tek yanar.\
`her zaman sığan düzen, kaydırma çubuğu yok` · `bağımlılık incelemek için odakla-sığdır` · `koşuyu izleten opaklık anlatısı` · `listeyle ortak vurgulama`

### Hatayı gösterir, gürültüyü değil

**Ne yapar:** Konsol koşunun hikâyesini renkli, seçilebilir düz metin olarak anlatır; Compact console her projeden
yalnız işe yarayanı alır: hatalar tam, ilk farklı uyarılar, kalanın koda göre sayısı. Bir projeye tıklamak konsolu o
projenin tam loguna çevirir; log baştan açılır, ilk hata önündedir.\
**Nasıl:** Proje logları ve konsol anlatısı her koşunun diskteki klasörüne yazılır. Konsol ekranda sınırlı sayıda
satır tutar ve yukarı kaydırınca diskten geri yükler; hiçbir şey kaybolmaz, koşu ne kadar uzun olursa olsun
konsolun belleği sınırlı kalır.\
`önce diske yazılan, kaybolmayan loglar` · `uyarı gürültüsünü kesen Compact console` · `hatayı baştan okutan proje sayfası` · `uzun koşuda sabit bellek bütçesi`

### Derlemenin neresinde olduğunu hep söyler

**Ne yapar:** Title bar'ın altındaki şerit en son ne istendiğini, o anki fazı, ilerlemeyi, kalan süreyi ve hata
durumunu her zaman gösterir; olay akışı koşunun olaylarını sırayla, her biri tek satır listeler.\
**Nasıl:** Kalıcı işlem etiketi (SYNC, BUILD, REBUILD, CLEAN, DEEP CLEAN, OPTIMIZE, RESOLVE, SWITCHING BRANCH) iş
sürerken amber, bitince nötrdür. Yanında faz satırı, o an derlenen projelerin (koşu bitince hatalıların) chip'leri
ve ince ilerleme çubuğu durur; acil uyarılar faz satırının yerine geçer.\
`"son ne istendi" için kalıcı etiket` · `kalan süre tahmini` · `hatalıya tek tık götüren chip'ler` · `koşunun zaman çizelgesi olarak olay akışı`

### Her yerden çalışan kısayollar

**Ne yapar:** Pencerede F5 derler, F6 yeniden derler, F7 temizler, Ctrl+F süzer, F1 About'u açar, Esc üstteki
katmanı kapatır, build'i durdurur ya da — yapacak iş kalmayınca — pencereyi X gibi kapatır, Ctrl+Q çıkar; her yerden
Shift+Space pencereyi gösterir ya da gizler, Ctrl+Shift+Space pencereyi açmadan Build başlatır.\
**Nasıl:** Pencere ve global kısayollar About → Shortcuts'u, menü rozetlerini ve ipuçlarını da çizen tek bir
katalogdan gelir. Globaller Windows'a kaydedilir, pencere Visual Studio'nun arkasındayken ya da tepsideyken de
çalışır; sonuç bildirim olarak gelir.\
`Visual Studio gibi F5/F6/F7` · `arka plandan derlemek için global hotkey` · `tutarlı metin için tek kısayol kataloğu` · `öngörülebilir kapanış için Esc zinciri`

### Tepside yaşar

**Ne yapar:** Pencereyi kapatmak onu tepsiye gizler, ekran köşesindeki küçük hareketli işaret derleme sürdüğünü
gösterir, sonucu Windows bildirimi söyler. Uygulama istenirse Windows ile, istersen tepside gizli başlar; ikinci
açılış çalışan pencereyi öne getirir.\
**Nasıl:** Close to tray, Start with Windows, Start minimized to tray ve Show notifications Settings → General'dadır.
Windows ile başlama kullanıcıya özel Run kaydını kullanır, yönetici hakkı gerekmez; tepsi menüsünde Stop ve Exit
vardır, ikona tıklamak pencereyi geri getirir.\
`arka planda çalışmak için tepsi ikonu` · `pencere açmadan ilerleme gösteren köşe işareti` · `sonuç için Windows bildirimi` · `yöneticisiz kullanıcı Run kaydı`

### Yarım iş bırakmadan çıkar

**Ne yapar:** Title bar'daki güç düğmesi, Ctrl+Q, tepsideki Exit ve Close to tray kapalıyken pencerenin kapatma
düğmesi uygulamadan çıkar: iş yoksa hemen kapanır, iş varsa bir kart neyin sürdüğünü, hangi süreçlerin biteceğini
ve neyin kaybolacağını söyler.\
**Nasıl:** Stop and exit koşuyu sert durdurmayla keser, Sync, Deep Clean ya da Optimize'ı yarıda keser, ama branch
değişimini ve pull'u bekler ki git yarım kalmasın. Kapanış ekranı adımları gösterir, uygulama motoru ve
derleyicileriyle birlikte kapanır.\
`doğru karar için tek "meşgul" tanımı` · `neyin biteceğini gösteren job süreç sayımı` · `git yazımını bekleme` · `sonraki Build'in toparlaması için uçuştaki-iş defteri`

### Kendiliğinden Sync

**Ne yapar:** Sync depoyu ve harici kökleri tarar, grafı kurar, hangi projelerin derleneceğini önizler ve remote'a
uzaklığı ölçer, hiçbir şey derlemeden; terminalde ya da Visual Studio'da yapılan commit, checkout, pull, merge ve
rebase onu kendiliğinden tazeler.\
**Nasıl:** Elle Sync yalnız ref'leri fetch eder, sonra tarar ve karar verir; ağ yoksa yerel HEAD ile sürer. Deponun
HEAD'i yoklama yerine dosya bildirimiyle izlenir; git durulunca kendiliğinden bir Sync çalışır, branch
değişmediyse bu, konsolu, seçimi ve kaydırma konumunu koruyan sessiz bir Sync'tir.\
`yalnız ref fetch, çalışma ağacına dokunmaz` · `Sync'e nadiren bastıran HEAD izleme` · `ekranı bozmayan sessiz Sync` · `yarım merge'de bekleme`

### Branch değiştirme ve pull

**Ne yapar:** Aksiyon çubuğundaki branch chip'i checkout'lu branch'i gösterir ve aranabilir listeden gerçekten
değiştirir; geride kalınca beliren N behind chip'i aktif branch'i yalnız fast-forward ile günceller.\
**Nasıl:** Remote branch'e yerel tracking branch açılır. Kirli ağaçta geçiş varsayılan olarak reddedilir; Stash and
switch branches açıksa değişiklikler (untracked dahil) stash'e alınır ve konsolda adlandırılır, araç onları asla
pop etmez. Pull, fetch artı merge --ff-only'dir: merge commit yok, rebase yok, kirli ya da ayrışmış ağaçta
reddeder.\
`araçtan git checkout` · `kirli ağacı koruyan stash push -u` · `geçmişi koruyan merge --ff-only` · `ucuz geride-kalma sayımı`

### Harici projeler, tek graf

**Ne yapar:** Depo dışındaki klasörler, çözümler ya da proje dosyaları Settings'te kart olarak eklenir; aynı grafa
girer, depodaki projeler onlara gerçek bağımlılık olarak bağlanır ve önce onlar derlenir; istersen her Build'den
önce her git çalışma kopyası fast-forward ile güncellenir.\
**Nasıl:** Kart yalnız bir yoldur: klasör altındaki her proje, .sln listelediği projeler, .csproj kendisi demektir;
git çalışma kopyası yoldan bulunur. Pull before build açıksa kopyalar kart sırasıyla fetch artı merge --ff-only ile
güncellenir, commit'lenmemiş iş koşuyu başlamadan durdurur.\
`çok kökten tek grafa tarama` · `doğru sıra için HintPath bağlantıları` · `her Build öncesi fast-forward güncelleme`

### Bozuk çalışma alanını onarır

**Ne yapar:** Çalışma alanı kodla hiç ilgisi olmayan sebeplerle derlenmiyorsa Sync'in yanındaki iki düğme bunu
düzeltmeye yardım eder. Deep Clean (silgi; ipucu Clean der) her projenin bin ve obj klasörlerini ve derleme kaydını
siler, sonraki Build başka bir framework'ten kalan artıklardan arınmış olarak sıfırdan başlar (SDK-style projeler
bundan sonra derlenmeden önce bir Optimize ister). Optimize eksik paketleri geri getirir ve düzeltemediği
referansları adıyla söyler. Her koşunun başında da bir projenin obj'sinde başka bir framework'ten kalan NuGet
artıkları varsa uyarı verilir.\
**Nasıl:** Deep Clean MSBuild çağırmaz: harici kökler dahil dosya sisteminden siler ve bitince Sync çalıştırır.
Optimize, NuGet HintPath hedefleri eksik olan eski tip packages.config projelerini ve her SDK-style projeyi restore
eder; bu, taze klondan ya da Deep Clean'den sonra çıkan NETSDK1004'ü çözer. Ardından yalnız eski tip projelerde
bayat obj'deki NuGet artıklarını (project.assets.json, *.nuget.g.props, *.nuget.g.targets) siler, ölü defter
kayıtlarını ayıklar, kilitli dosyayı yeniden denemek yerine adıyla söyler. Koşu başı uyarısı hiçbir şey silmez.\
`temiz çalışma alanı için bin/obj silme` · `"bende derlenmiyor"u düzelten NuGet restore` · `NuGet artıkları için bayat obj temizliği` · `koşu başı uyarısı, hiçbir şey silinmez`

### Tek ekranda kurulur

**Ne yapar:** İlk açılışta liste çalışma alanını kurmaya davet eder; Settings dört bölümdür (General, Workspace,
External projects, Layers), Save'e basılana kadar hiçbir şey uygulanmaz ve bütün kurulum bir JSON dosyasına dışa
aktarılıp başka makinede içe aktarılabilir.\
**Nasıl:** Workspace depo kökünü alır, ilk kurulumda düğme Save and sync yazar; Save taslağı uygular ve bir Sync
başlatır. General başlangıç, derleme, branch, konsol, motor ve bildirim anahtarlarını taşır.\
`güvenli değişiklik için taslak ve Save` · `ekipte aynı kurulum için JSON dışa/içe aktarma` · `ilk açılış daveti`

### Projeleri katmanlara ayırır

**Ne yapar:** Proje adlarına uyan regex desenleri listeyi ve grafı adlı katmanlara ayırır, eşleşmeyenler Other
altına düşer ve her katman bir sonrakinden önce derlenir.\
**Nasıl:** Settings → Layers ad ve desen kartlarını taşır; desenler yukarıdan aşağı denenir, ilk eşleşen kazanır,
varsayılanda katman yoktur. Katman sırası bir projeyi bağımlılıklarından birinin önüne koyacaksa koşu engellenmez,
bir uyarıyla başlar.\
`uzun listede yön bulmak için regex gruplama` · `öngörülebilir katman için ilk eşleşme kuralı` · `denetimli sıra için katman fazları` · `ters katman uyarısı`

### Boşta hafif, yükte akıcı

**Ne yapar:** Motor birkaç dakika boş kalırsa kapanır ve sonraki işte yeniden başlar; pencere tepsideyken ya da
arkadayken uygulama neredeyse hiç işlemci harcamaz, her işlemden sonra belleği geri verir ve yüzlerce projelik
koşuda bile pencere donmaz.\
**Nasıl:** Stop the engine when idle (varsayılan açık) motoru uyutur, uyanırken şerit Starting the engine der.
Görünmeyen animasyonlar durur, olaylar küçük zaman dilimlerinde işlenir, liste önce görünen satırları kurar.\
`bellek için boşta motor uykusu` · `görünürlüğe bağlı animasyon, boşta sıfıra yakın CPU` · `her işlem sonrası bellek kırpma` · `donmayan arayüz için dilimli olay işleme`

### Kendini kurar ve günceller

**Ne yapar:** GitHub Releases'taki kurulumcu yönetici hakkı istemeden mevcut kullanıcıya kurar; kurulu kopya yeni
sürümleri sessizce indirir ve hazır olunca Update pill'i gösterir, What's new sürüm notlarını, About sürümü, ortamı
ve kısayolları gösterir.\
**Nasıl:** Velopack kullanıcı başına kurar, tam ya da delta paket uygular; Restart to update hemen kurar, Later
çıkışta kurar. Her sürüm GitHub Actions ile test edilir, paketlenir, etiketlenir ve yayınlanır; sürüm notları
uygulamaya gömülü CHANGELOG'dan gelir.\
`yöneticisiz kurulum ve delta güncelleme için Velopack` · `test, paket ve yayın için GitHub Actions` · `işi kesmeyen çıkışta kurulum` · `notların tek kaynağı CHANGELOG`

### Sakin ve erişilebilir tasarım

**Ne yapar:** Arayüz tek koyu temayı ve tek vurgu rengi olarak amber'i kullanır; renk yalnız durum anlatır ve her
durum bir glyph ve kelimeyle de söylenir, Windows animasyonları kapalıysa geçişler anında olur, ekran okuyucu ve
klavye desteklenir.\
**Nasıl:** Renk, ölçü, yazı ve hareket tek bir token sisteminden gelir, Geist fontları uygulamayla gelir. Hareket
Windows'un animasyon ayarını canlı izler, her kontrol tek bir erişilebilir ad ve gerçek bir rol bildirir.\
`tutarlı görünüm için tasarım token'ları` · `erişilebilir hareket için işletim sistemi reduced-motion sinyali` · `ekran okuyucu için UI Automation adları` · `her yerde aynı görünüm için gömülü fontlar`

---

Aracın bilerek yapmadıkları ve bilinen sınırları
[ARCHITECTURE.md § Known limits](../ARCHITECTURE.md#20-known-limits)'te (İngilizce) listelenir.
