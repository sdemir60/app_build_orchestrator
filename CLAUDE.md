# App Build Orchestrator — Claude Talimatları

Çok projeli bir .NET çözümünü, bağımlılık sırasına göre ve yalnız değişenleri derleyen WPF masaüstü aracı.

## Dokümanlar

| Doküman | Ne için |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | **Teknik referans.** Mimari, process topolojisi, IPC, incremental karar, build motoru, git yüzeyi, UI, design system, güven sınırı, bilinçli kararlar, bilinen sınırlar. |
| [README.md](README.md) | Giriş: ne yapar, gereksinimler, kurulum, build/test/run, paketleme ve yayın, kullanım, güncelleme, kısayollar. |
| [CHANGELOG.md](CHANGELOG.md) | Sürüm notları — What's new ekranının TEK kaynağı (exe'ye gömülür). Yalnız sürüm çıkarılırken yazılır. |

**Bir kusur veya davranış sorusu geldiğinde önce bunları oku.** ARCHITECTURE.md §22 kod haritasıdır (hangi
davranış hangi dosyada), §13-§14 UI ve design system'i — renk, ölçü, tipografi, motion ve bileşen davranışları
oradadır.

## Proje yapısı

Solution: `BuildOrchestrator.slnx` (kökte).

| Proje | Target | Sorumluluk |
|---|---|---|
| `src/BuildOrchestrator.App` | net10.0-windows (WPF) | UI, MVVM, DI, tray, single-instance, IPC client, güncelleme motoru (Velopack). **Outer Job Object** sahibi. |
| `src/BuildOrchestrator.Core` | net10.0 | Saf çekirdek: discovery, graph, incremental karar, scheduler, git, MSBuild sözleşmesi, job primitifleri, state. |
| `src/BuildOrchestrator.Supervisor` | net10.0-windows | Motor process: build kuyruğu, **inner Job Object**, per-project `MSBuild.exe`, IPC server. Planlamaz, yürütür. |
| `src/BuildOrchestrator.Contracts` | net10.0 | App ↔ Supervisor sözleşmesi: command/event, DTO, JSON, NDJSON framing. |
| `tests/BuildOrchestrator.Tests` | net10.0-windows (xUnit, `UseWPF`) | Core + process-control + IPC + integration + WPF realize/STA testleri + kaynak guard'ları. |

### Değişmezler (ihlal edilemez)

- **Shell-out:** in-process MSBuild (BuildManager) yok; her proje `vswhere` ile resolve edilen `MSBuild.exe`
  child process'i — `dotnet build` DEĞİL.
- **Nested Job Object:** App outer job sahibi, Supervisor içinde, `MSBuild.exe` inner job'da. Managed
  parent-watcher / PID heuristiği yok.
- **OutDir'e dokunulmaz.** Hiçbir çıktı yolu değiştirilmez (ne `OutDir` ne `obj`); her koşu çalışma ağacında
  derlenir. Araç OutDir'e kendisi hiçbir şey yazmaz, kopyalamaz, silmez. Tek istisna kullanıcının bastığı
  Clean'dir (satır menüsündeki ve Build menüsündeki `-t:Clean`): MSBuild projenin kendi kaydettiği çıktıları
  oradan da siler — Visual Studio'nun Clean'i gibi; bu kural ihlali değildir. Araç kendi derlediği projede yalnız DİSKTEKİ kaynak İÇERİĞİNE bakar; başkasının (ör. VS'nin)
  derlediği çıktıda tarihlere bakılır; çıktının tarihi tek başına "güncel" demeye asla yetmez. Sürüm kontrolü
  karara girmez (git yalnız fetch, branch, checkout ve harici güncelleme içindir); kaynak dosyanın boyut+mtime
  bilgisi içerik kararında yalnız özet önbelleğinin anahtarıdır. Harici köklerden gelen projeler sıradan
  projelerdir (aynı argümanlar, aynı graf, aynı karar).
- **Git'e araç KENDİLİĞİNDEN yazmaz:** `pull`/`reset`/`switch` hiçbir akışta çalıştırılmaz. Üç istisna da
  kullanıcının açık kararıdır: (a) kayıtlı **harici kökler** — build anında, yalnız kullanıcı güncellemeyi açık
  bıraktıysa, kendi köklerinde ff-only güncelleme (`fetch` + `merge --ff-only`); (b) **ana repo pull** — yalnız
  `N behind` chip'i, yalnız aktif branch, yalnız ff-only, kirli ağaçta ret; (c) **branch chip'inden checkout**
  — kirli ağaçta ayar karar verir (Stop varsayılan; Stash and switch'te `git stash push -u`). Mutasyon yüzeyi
  TEK dosyadır: `Core/Git/RepositoryWriter.cs` (kaynak guard'ı).
- **stdout yalnız NDJSON;** tüm log/tanı stderr'e.
- **Planlama Core'da.** İş mantığını App/Supervisor'a sızdırma; Core UI ve process bağımsız test edilebilir kalır.
- **Velopack yalnız App'te.** Paket referansı, giriş noktası (`Program.Main`) ve güncelleme motoru
  (`Services/Updates/`) App'tedir; Core, Supervisor ve Contracts'a girmez.
- **Güncelleme motoru yalnız kurulu kopyada çalışır;** bin'den ya da publish klasöründen çalışan kopya hiç kontrol
  etmez. Hap yalnız indirilmiş, kuruluma hazır bir teklif varken görünür — örnek/yer tutucu teklif yoktur.
- **Kopya YASAK / tek doğruluk kaynağı:** aynı değer, metin veya primitif iki yerde tanımlanmaz — ne kodda
  (perf tablosu, konsol not metni, supervisor klasör adı) ne testlerde (ortak fixture/host tek yerde).

> Ayrıntı ARCHITECTURE.md'dedir, burada tekrarlanmaz.

## Dil ve üslup

- Yanıtları **Türkçe** ver, teknik terimleri İngilizce bırak. Sade yaz; sadece koddaki gerçeğe dayan, emin
  olmadığını yazma.
- **Kod, UI metinleri ve loglar İngilizce**; kod yorumları ve `.claude/` kayıtları Türkçe. README.md ve
  ARCHITECTURE.md İngilizce.

## Build / test

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test  tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
dotnet run   --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj
```

Süit **filtrelidir**: `Category=Acceptance` üç test gerçek OSYS reposunu derler (~2 dk), ayrı koşulur
(`--filter "Category=Acceptance"`). Uygulama açıkken build alma — çalışan Supervisor kendi binary'lerini kilitler.

- **CI (`ci.yml`) aynı süiti windows-2025 runner'ında koşar;** runner'da koşamayan/kararsız test
  **`Category=LocalOnly`** alır ve yalnız CI filtresinde dışlanır — eşik gevşetilmez, test silinmez; lokal tam
  süit kapı olmaya devam eder.
- **Ölçüm/sonda testi = ortam değişkeni kapısı.** `Category=Measurement` etiketi TEK BAŞINA yetmez:
  `Category!=Acceptance` filtresi diğer her kategoriyi kabul eder. Pencere açan, balloon gösteren ya da CPU
  yakan her YENİ test `[SkippableFact]` + ilk satırda `Skip.IfNot(<BO_... değişkeni> == "1")` taşır (içerik
  kararı ölçümleri eski kalıptadır: kök yoksa atlar, varsayılan kök varsa normal süitte koşar);
  STA gövdesi gerekiyorsa ortak `StaThread.RunAsync` kullanılır (`[StaFact]` Skip'i tanımaz). Liste
  ARCHITECTURE.md §17.5'te.
- **Canlı pencere ≠ ekran dışı çizim.** `RenderTargetBitmap` kompozisyon hattını atlar; katmanlı pencerede
  görülen bir kusur ekran dışı karede görünmeyebilir. Görsel kusurda sayı yetmiyorsa kullanıcının ekran
  görüntüsü kanıttır — yapısal pin yaz, "ekran dışı temiz" diye kusuru reddetme.

## Çalışma kuralları

Kullanıcı kusuru görüp tarif eder, **testi agent yazar**.

- **Kırmızı test kuralı:** hiçbir fix, kusuru yakalayan test KIRMIZI verdiği gösterilmeden yapılmaz. Kırmızıyı
  gösteremiyorsan test yanlıştır — testi düzelt, kuralı esnetme.
- **Davranış değişince testi de değişir.** Bir kural bilerek değiştiyse onu pinleyen eski test sessizce
  silinmez ya da gevşetilmez: YENİ kuralı pinleyecek şekilde yeniden yazılır ve doc'una eski iddia + değişme
  gerekçesi (ölçüm) yazılır. Testi yeşile boyamak için bütçe/eşik gevşetmek YASAKTIR.
- **Realize testi:** yeni XAML kökü/şablonu ekleyen her değişiklik bir realize testi de ekler (headless süit
  XAML runtime çözümlemesini görmez). `Window.Measure/Arrange` HWND'siz içeriğe inmez — realize
  `window.Content` üzerinde yapılır.
- Bulguları **bloklayıcı → önemli → kozmetik** sırala, sırayı göster, sonra başla.
- **Belirsizlikte tahmin yürütme** — ayırt edici soru sor (her seferinde mi, pencere boyutuna bağlı mı,
  reduced-motion açık mı). Nedeni bilinmeyen kusurda `superpowers:systematic-debugging`; hipotezi doğrulamadan
  koda dokunma.
- **Doküman kodla uyuşmuyorsa: sessizce birini seçme, kullanıcıyı uyar.** ARCHITECTURE.md bir şey diyor ama kod
  başka türlü davranıyorsa, "doküman ile kod uyuşmuyor: doküman şunu diyor, kod şunu yapıyor" de ve kullanıcının
  hangisinin doğru olduğunu söylemesini bekle.
- Birden çok bulgu tek kök nedene bağlıysa tek fix, ama **her biri için ayrı test**.
- Bitişte **tam süit yeşil** (token/motion/D8 guard'ları dahil). 5'ten fazla bulgu varsa önce kısa TDD dökümü
  (`.claude/outputs/`), sonra `superpowers:subagent-driven-development` ile task-by-task.

## Doküman güncelleme

Bir hata düzeltildiğinde ya da yeni bir şey eklendiğinde, doküman artık yanlış bir şey söylüyorsa ilgili bölüm
**aynı işte** güncellenir. Doğru söylüyorsa dokunulmaz. Ayrıca **"dokümanları güncelle"** dendiğinde o ana
kadarki tüm değişiklikler dokümanlara işlenir.

- **Anlatı üslubu korunur.** Doküman projeyi ANLATIR; "şu oturumda şunu ekledik / eskiden böyleydi" YAZILMAZ.
  Değişen davranış ilgili bölümde **yerinde yeniden yazılır** — doküman changelog biriktirmez.
- **Yer:** teknik/mimari/tasarım/güven sınırı → ARCHITECTURE.md · kullanım/komut/gereksinim/kısayol →
  README.md · çalışma kuralı → CLAUDE.md. README özetler, ARCHITECTURE ayrıntılandırır; aynı şey iki yerde
  ayrıntısıyla tekrarlanmaz.
- **Her iddia kodda doğrulanır.** Doğru ifadeye dokunma; emin olamadığını sor.
- **Rakam gömme:** bayatlayacak sayı (test sayısı, sha) yazma; dayanıklı dil kullan.
- `.claude/outputs/` ve `.claude/summaries/` **tarihseldir** — geriye dönük düzeltilmez.

## Sürüm çıkarma

Sürüm notları ve sürüm numarası **yalnız** kullanıcı "sürüm çıkar", "yeni versiyon", "versiyon no oluştur" gibi
bir şey dediğinde yazılır. Sıradan işlerde `CHANGELOG.md`'ye ve `Directory.Build.props` → `Version`'a dokunulmaz.

1. **Kaynak:** son tag'den bu yana `develop`'a girenler — `git log --first-parent v<son>..develop` merge mesajları +
   ilgili `.claude/outputs/` sonuç raporları. Ayrı bir ayrıntılı log dosyası tutulmaz; merge mesajı zaten odur.
2. **Numara:** yalnız düzeltme → patch · yeni özellik → minor · büyük dönüm noktası → major (kullanıcıya sor).
3. **Not:** `CHANGELOG.md`'nin en üstüne `## [x.y.z] - yyyy-MM-dd` (sürüm günü). Kategoriler Added · Changed ·
   Fixed · Performance · Removed sırasıyla, boşu yazılmaz. Maddeler İngilizce ve düz metin (markdown işareti
   yok); kısa, genel, kullanıcının gördüğü özellik — iç terim, dosya/sınıf adı ve "şuraya şunu ekledik" yok;
   küçük işler tek genel satırda toplanır. Her madde o anki koda göre doğrulanır.
4. **Numara tek yerde:** `Version` aynı değere çekilir (guard: CHANGELOG'daki en üst sürüm = `Version`).
5. **Yayın:** `/release` (ya da elle `scripts/release.ps1 -Version X.Y.Z`), ana proje checkout'unda `develop`
   üzerinde: guard'lar (develop push'lu ve `origin/develop`'la eşit, **develop HEAD'inin `ci.yml` koşusu yeşil**,
   `origin/main` develop'un atası) → tam süit → `develop`'ta `release: vX.Y.Z` commit'i (bu commit için ayrı
   branch açılmaz — tek istisna) → `main`'e `--no-ff` merge (`merge: release vX.Y.Z`) → merge commit'ine annotated
   tag `vX.Y.Z` → `develop` `main`'e ff (develop = main) → `main`, `develop` ve tag tek atomik push. Tag'i gören
   `release.yml` derler, `scripts/package.ps1` ile paketler ve GitHub Release'i açar; senin başka bir şey yapman
   gerekmez. Paket çıktıları `artifacts/` altındadır (ignore'lu). Script durursa ne yapılacağı `/release` skill'inde.

Yayınlanmış bir sürümün notu yalnız yanlışsa düzeltilir.

## Çıktı, özet ve aşama dosyaları

Çıktılar → `.claude/outputs/` · Özetler → `.claude/summaries/` · Handoff → `.claude/handoffs/` · Geçici →
`.claude/temp/`

**İsimlendirme:** `YYYY-MM-DD-HH-mm-{baslik}.md`. Tarih/saat o anki gerçek zaman (Bash `date`). Başlık
kebab-case ve **İngilizce** (`scrollbar-restyle-plan` gibi; `plani`/`kayitlari` DEĞİL). Çıktı ve özet dosyaları
**aynı adı** taşır, sadece klasörleri farklıdır.

**Tetikleyiciler:**

- **"özet" / "özeti çıkar"** → konuşmanın özetini yalnız `summaries/`'e yaz.
- **"aşamamızı kaydet"** → önce özeti `summaries/`'e yaz, sonra `handoffs/`'a **KISA** bir aşama girişi. Amacı
  yalnızca (a) ilgili özet dosyalarını listelemek (yol + tek satır), (b) nerede kaldığımızı işaretlemek.
  "Sıradaki adımlar / detaylı durum" bölümü YAZMA.
- **"bu çalışma tamam" / "iz bırak"** → SADECE `handoffs/`'a çok kısa bir giriş (özet yazma). Dosya listesi
  **kümülatiftir**: önceki handoff'takileri taşı + bu oturumdakileri ekle. Bir-iki cümle + "Buradan devam
  edilecek." Ekstra detay ekleme.

## Git

- Repo: `sdemir60/app_build_orchestrator` (GitHub). **`develop` günlük iş branch'idir; `main` yalnız sürümleri
  taşır.**
- Bir iş için kendi çalışma branch'ini `develop`'tan aç, task başına commit at, bitince `develop`'a merge + push.
- Merge'ün geçtiğini **doğruladıktan sonra** branch'i local ve remote'tan sil.
- **`main`'e yalnız `/release` (`scripts/release.ps1`) dokunur** — elle merge, commit ya da push yok. `main`'deki
  her merge commit'i bir sürüm + tag'tir (ayrıntı "Sürüm çıkarma" adım 5; tek istisna: tag geri çekilip aynı sürüm
  yeniden çıkarılınca önceki merge tag'siz kalır — `/release` skill'i adım 6). Doğrudan atılan tek commit
  `release: vX.Y.Z`'dir; o da `develop`'ta atılır.
- **Hotfix** de aynı yoldan geçer: `fix/x` → `develop`'a merge + push → `/release` (patch). `main` her zaman
  `develop`'ın tamamını alır; yalnız düzeltmeyi taşıyan ayrı bir sürüm çıkmaz — bilinçli sınır.
- Oturum **`develop` üzerinde** bitirilir.

### Nerede çalışılır

Çalışma **her zaman ana projede** (`D:\Projects\Other\Apps\app_build_orchestrator`) yapılır. Tek istisna:
kullanıcı açıkça **"worktree'de yap"** derse — o zaman kalıcı worktree kullanılır, yenisi açılmaz.

**Adlandırma kuralı: `-ai` eki.** Kalıcı worktree'nin klasörü proje adı + `-ai`
(`D:\Projects\Other\Apps\app_build_orchestrator-ai`), boştaki branch'i günlük iş branch'inin adı + `-ai`:
**`develop-ai`**.

- `develop-ai`, `develop`'un aynasıdır — kendi commit'i olmaz. Git aynı branch'in iki worktree'de birden açık
  olmasına izin vermediği için worktree boştayken `develop` yerine onu taşır.
- İşe başlarken worktree önce `develop-ai`'yi `develop`'a ff-only günceller (`git merge --ff-only develop`), sonra
  oradan işin kendi çalışma branch'ini açar.
- İş bitince çalışma branch'i `develop`'a merge + push edilir, merge doğrulanınca branch local ve remote'tan
  silinir. Worktree `develop-ai`'ye döner ve yeniden `develop`'a ff-only güncellenir; oturum orada biter. Worktree
  yerinde kalır, silinmez; `develop-ai` remote'a push edilmez.
- `/release` worktree'de koşmaz: `develop` ana proje checkout'unda açıktır; script orada, `develop` üzerinde koşar.
