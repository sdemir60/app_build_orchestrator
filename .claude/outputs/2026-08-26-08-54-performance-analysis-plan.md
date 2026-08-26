# Performans Analizi Planı — App Build Orchestrator

> **Yürütücü için:** Bu bir ANALİZ planıdır, implementation planı değildir — **kod değiştirilmez**, çıktı
> yalnızca rapordur. Plan, sıfır bağlamlı bir oturumun kaldığı yerden devam edebilmesi için checkpoint
> protokolüyle yürütülür. Adımlar checkbox (`- [ ]`) ile izlenir; işaretleme bu dosyada DEĞİL,
> checkpoint dosyasında yapılır (bu dosya tarihseldir, değiştirilmez).

**Amaç:** Uygulamanın tüm yaşam döngüsünü (açılış → boşta → build → stop → exit) kapsayan, doğrulanmış
bulgular + gerçek ölçümler + öncelik sıralı öneriler içeren tek bir performans raporu üretmek.

**Yaklaşım:** Önce keşif (kod haritası + dokümandaki perf iddiaları), sonra 9 boyutlu çok ajanlı statik
analiz (kullanıcı onayı bu planla verilmiştir), ardından gerçek ölçüm turu, adversarial doğrulama ve tek
rapor. Her ara sonuç diske anında yazılır; limit/kesinti hangi anda gelirse gelsin kaybolan iş en fazla
yarım kalan tek adımdır.

**Rapor çıktısı:** `.claude/outputs/YYYY-MM-DD-HH-mm-performance-analysis-report.md` (zaman, rapor
yazıldığı andaki gerçek zaman; içerik Türkçe).

---

## Global kısıtlar

- **Yalnız `main`.** Başka branch'a bakılmaz, dokunulmaz. Kod değişikliği yok; repo salt-okur kullanılır
  (ölçüm turundaki `dotnet build/test` çıktıları hariç — onlar `bin`/`obj`'a yazar, kaynağa değil).
- **Uygulama kapalıyken çalışılır.** Her build/ölçüm öncesi process kontrolü yapılır (Görev 0 ve Görev 3);
  `BuildOrchestrator.App` / `BuildOrchestrator.Supervisor` / `MSBuild` çalışıyorsa dokunulmaz, kullanıcıya
  söylenir.
- **Çok ajanlı yürütme onaylı:** Görev 2 ve Görev 4, Workflow/subagent fan-out ile koşulur — kullanıcı bu
  planı onaylayarak buna onay vermiş sayılır.
- **Her bulguda `Konum:` zorunlu** (dosya + satır no/aralığı). Kanıt için anılan ama sorunlu olmayan
  satırlar "mevcut kod, değişmez" diye etiketlenir. Konumu gösterilemeyen bulgu rapora giremez.
- **Doküman-kod uyuşmazlığı** görülürse sessizce taraf seçilmez; rapora "Doküman ile kod uyuşmuyor"
  bölümü olarak yazılır, karar kullanıcıya bırakılır.
- Rapor bulguları **bloklayıcı → önemli → kozmetik** sıralı; **dağınıklık/organizasyon** bulguları
  (tekrar, yanlış katman, eksik soyutlama) bug'lardan AYRI başlıkta.
- Commit **yapılmaz**; rapor bitince commit kararı kullanıcıya sorulur. Oturum `main` üzerinde biter.

## Kesinti / devam protokolü (checkpoint)

Çalışma dizini: `.claude/temp/perf-analysis/`

| Dosya | İçerik |
|---|---|
| `checkpoint.md` | Görev tablosu (bekliyor / yapılıyor / bitti), yarım kalan adım için "sıradaki iş" tarifi, üretilen dosyaların listesi |
| `scope-map.md` | Görev 1 çıktısı: boyut → dosya listesi + doküman perf iddiaları envanteri |
| `findings-A.md`, `findings-B.md`, `findings-C.md` | Görev 2 batch çıktıları (ham bulgular) |
| `measurements.md` | Görev 3 ölçüm sonuçları |
| `verified-findings.md` | Görev 4 çıktısı: doğrulanan + elenen bulgular (elenenler "elendi: gerekçe" ile kalır) |

Kurallar:

1. Her göreve başlarken `checkpoint.md`'de görev "yapılıyor" işaretlenir, bitince "bitti" + çıktı dosyası
   yazılır. Todo listesi de aynı anda güncellenir (`in_progress` → `completed`; aynı anda tek `in_progress`).
2. **Hiçbir sonuç bellekte bekletilmez.** Her ajan sonucu / ölçüm değeri döner dönmez ilgili dosyaya yazılır.
3. Görev 2'nin batch'leri ayrı ayrı koşulur (tek dev workflow DEĞİL) — böylece kesinti en fazla bir
   batch'in yarısını götürür; workflow resume aynı oturumda `resumeFromRunId` ile, oturumlar arası ise
   findings dosyalarından yapılır.
4. **Devam akışı** ("devam et" / "kaldığımız yerden devam et" dendiğinde): önce bu plan dosyası, sonra
   `checkpoint.md` okunur → "bitti" görevler atlanır → "yapılıyor" görevin "sıradaki iş" tarifinden
   sürülür. Bitmiş analiz tekrar koşulmaz.

Todo madde metinleri sabittir, birebir şunlar kullanılır (yeniden adlandırma yasak):

```
Hazırlık + checkpoint [kisa]
Keşif + iddia envanteri [kisa]
Statik analiz: UI grubu [orta]
Statik analiz: motor grubu [orta]
Statik analiz: yaşam döngüsü [orta]
Ölçüm turu [orta]
Adversarial doğrulama [orta]
Rapor yazımı [orta]
```

---

## Görev 0: Hazırlık + checkpoint

- [ ] Uygulamanın kapalı olduğunu doğrula:
  `Get-Process BuildOrchestrator.App, BuildOrchestrator.Supervisor, MSBuild -ErrorAction SilentlyContinue`
  → çıktı boş olmalı; değilse DUR, kullanıcıya bildir.
- [ ] `git status` + `git branch --show-current` → `main` ve temiz olduğunu doğrula; değilse DUR, bildir.
- [ ] `.claude/temp/perf-analysis/checkpoint.md` dosyasını yukarıdaki görev tablosuyla oluştur (tüm
  görevler "bekliyor").
- [ ] Todo listesini yukarıdaki sabit metinlerle oluştur.

## Görev 1: Keşif + iddia envanteri

Amaç: Görev 2 ajanlarına verilecek kesin dosya listeleri ve dokümandaki perf iddialarının envanteri.

- [ ] `ARCHITECTURE.md`'yi TAMAMEN oku — özellikle §13-§14 (UI + design system + motion), §22 (kod
  haritası) ve performansa dair her iddia (süre, eşik, batching, sanallaştırma, "X hızlıdır" türü cümleler).
- [ ] `README.md`'yi oku (çalıştırma/publish yolu — Görev 3'te hangi exe'nin koşulacağı buradan çıkar).
- [ ] `scope-map.md`'ye yaz:
  - **İddia envanteri:** doküman bölümü + iddia metni (rapordaki "iddia vs gerçek" tablosunun sol sütunu).
  - **Boyut → dosya listesi:** aşağıdaki 9 boyutun her biri için incelenecek dosyalar. Başlangıç noktaları
    Görev 2'de verilmiştir; §22 kod haritasıyla doğrulanıp eksikler tamamlanır (ör. git yüzeyi ve state
    dosyalarının tam listesi §22'den çıkar).
- [ ] Checkpoint güncelle.

## Görev 2: Statik analiz — 9 boyut, 3 batch

Her batch = 3 paralel ajan (Workflow ya da tek mesajda 3 Agent). **Bir batch bitmeden sonuçları
`findings-<batch>.md`'ye yazılır, sonra sıradaki batch başlar.** Ajanlar salt-okurdur (Explore tipi ya da
"kod değiştirme" talimatlı general-purpose).

**Ortak ajan prompt şablonu** (her ajana boyut adı + odak soruları + `scope-map.md`'deki dosya listesi
verilir):

```
Salt-okur performans incelemesi yapıyorsun; kod DEĞİŞTİRME. Proje: WPF + .NET 10 build orchestrator
(d:\Projects\Other\Apps\app_build_orchestrator, branch main). Önce ARCHITECTURE.md'nin ilgili bölümlerini,
sonra sana verilen dosyaları satır satır oku. Boyutun: <boyut>. Odak soruların: <sorular>.
Her bulgu için ŞUNLARI döndür (JSON): file, lines (yeni-taraf satır aralığı), severity
(bloklayici|onemli|kozmetik|organizasyon), claim (tek cümle sorun), evidence (koddan kanıt, satır
referanslı), impact (kullanıcı ne hisseder / ne kaybedilir), suggestion (net öneri; varsa yeniden
kullanılacak mevcut metot adı + satırı). Emin olmadığını bulgu yazma; şüpheni "soru" tipiyle ayrıca listele.
Bilinçli tasarım kararlarını (ARCHITECTURE.md "bilinçli kararlar" bölümünde gerekçelenmiş) bulgu sayma.
```

### Batch A — UI grubu (`findings-A.md`)

- [ ] **D1 Açılış:** `App.xaml`/`App.xaml.cs`, `Shell/SecondInstanceGate.cs`, `Services/EngineHost.cs`,
  `Shell/LayoutState.cs`, `MainWindow.xaml`, `ShellRoot.xaml`, DI kurulumu, tray init.
  Odak: ilk pencereye kadar hangi işler senkron yapılıyor; disk/registry/process I/O UI thread'de mi;
  Supervisor spawn açılışı bekletiyor mu; state/config okuma maliyeti; splash/ilk render sırası.
- [ ] **D2 UI thread:** `ViewModels/RunViewModel*.cs`, `StreamComposer.cs`, `SnapshotCollection.cs`,
  `LayerGrouping.cs`, `GraphBinder.cs`, `Services/ScrollArbiter.cs`.
  Odak: `.Result`/`.Wait()`/senkron I/O taraması (tüm App projesi genelinde grep); dispatcher'a satır
  başına mı batch'le mi geliniyor; `INotifyPropertyChanged` fırtınaları; build sırasında ana pencerenin
  donma riski; `ObservableCollection` toplu güncelleme deseni.
- [ ] **D3 Render/XAML:** `Views/*.xaml`, `Console/ConsoleView.xaml` + `Console/*.cs` (render tarafı:
  `ConsoleRenderSlice`, `TrackedTextBlock`, `TrackedGlyphs`), `Controls/*`, `Resources/Tokens.xaml`,
  `Resources/Motion.xaml`, `Graph/GraphView.xaml`.
  Odak: virtualization (konsol + proje listesi + event stream); layout derinliği ve ölçüm maliyeti;
  freeze edilmemiş brush/geometry; animasyonların CPU/GPU maliyeti ve reduced-motion yolu; resource
  lookup / DynamicResource yoğunluğu; her tick yeniden ölçüm tetikleyen binding'ler.

### Batch B — Motor grubu (`findings-B.md`)

- [ ] **D4 IPC:** `Contracts/Ipc/NdjsonFraming.cs`, `Contracts/Ipc/IpcMessages.cs`,
  `App/Console/ConsoleBatcher.cs`, `ConsoleBatchRouter.cs`, `ChunkStitch.cs`, `Services/EngineHost.cs`,
  `Supervisor/SupervisorHost.cs`.
  Odak: serialize/deserialize maliyeti (allocation, string churn); MSBuild çıktısının satır hacmi altında
  backpressure; batching pencereleri; UI thread'e marshaling sıklığı; büyük tek satırların (uzun MSBuild
  satırı) davranışı.
- [ ] **D5 Build motoru:** `Core/Planning/*`, `Core/Incremental/BuildSignature.cs`, `Core/Scheduling/*`,
  `Core/Discovery/*` (`WorkspaceScanner`, `CsprojEvaluator`, `SolutionMapper`),
  `Supervisor/RunCoordinator.cs`, `Core/MsBuild/*`.
  Odak: kaynak sinyali taramasının maliyeti (dosya sayısıyla ölçeklenme, gereksiz yeniden tarama);
  scheduler'ın gerçek paralellik derecesi ve katman bariyerleri; MSBuild.exe spawn başına sabit maliyet
  (node reuse, ortam, gereksiz argüman/restore); art arda build'de tekrar yapılan işler.
- [ ] **D6 Git/worktree:** git yüzeyi dosyaları (`scope-map.md`'den; §22'de listeli) +
  `Core/MsBuild/WorktreeObjPathResolver.cs`, `Supervisor/StaleObjRunStartWarner.cs`.
  Odak: bir kullanıcı eylemi başına koşan git process sayısı; aynı bilginin tekrar tekrar sorgulanması
  (cache yokluğu); worktree modunda ek maliyet; git çağrılarının UI'ı bekletip bekletmediği.

### Batch C — Yaşam döngüsü grubu (`findings-C.md`)

- [ ] **D7 Boşta davranış:** tüm `src/` genelinde `DispatcherTimer`, `System.Timers`, `Task.Delay` döngüsü,
  `FileSystemWatcher`, polling deseni taraması + `Core/Scheduling/RunClock.cs`, `Services/MotionCoordinator.cs`.
  Odak: uygulama boştayken (build yok, pencere açık ya da tray'de) periyodik ÇALIŞAN her şey; tray'e
  küçültülünce durması gerekenler; boşta CPU'yu sıfırdan ayıran ne var.
- [ ] **D8 Stop/Exit/kill zinciri:** `Shell/AppShutdown.cs`, `Shell/SecondInstanceGate.cs`,
  `Supervisor/Program.cs`, `SupervisorHost.cs`, `RunCoordinator.cs`, `Core/ProcessControl/*`
  (`JobProcessLauncher`, `JobChildProcess`, `JobCompletionPort`, `ProcThreadAttributeList`).
  Odak: stop komutu inner job'daki MSBuild ağacını gerçekten ve hızla öldürüyor mu; pencere kapatma /
  tray exit / crash yollarının HER BİRİNDE Supervisor + çocuklarının öldüğü garanti mi (nested job object
  zinciri kopabilir mi); kapanışta bekleyen/asılı kalan await, flush, IPC drain; zombie process ve handle
  leak pencereleri.
- [ ] **D9 Bellek/kaynak:** App + Supervisor genelinde event aboneliği (`+=`) / `Dispose` eşleşmesi,
  `CancellationTokenSource` yaşam döngüsü, sınırsız büyüyen koleksiyonlar (konsol log birikimi,
  `SnapshotCollection`, event stream), `Core/Logs/*` (`LogChunker`, `RunLogWriter`) tamponları,
  `State/BuildDurationPersister.cs` yazma sıklığı.
  Odak: uzun oturumda (10+ build) bellek büyümesi; kapatılmayan handle/stream; ısrarlı string birleştirme.

Her batch sonrası: sonuçlar dosyaya, checkpoint + todo güncelle.

## Görev 3: Ölçüm turu

Sonuçlar `measurements.md`'ye; her ölçümün komutu ve ham çıktısı da kaydedilir.

- [ ] Process kontrolü (Görev 0'daki komut) — temiz değilse DUR.
- [ ] **Cold build:** `dotnet clean BuildOrchestrator.slnx` sonrası
  `Measure-Command { dotnet build BuildOrchestrator.slnx }` → süre.
- [ ] **No-change build:** hemen ardından aynı build komutu bir daha → süre (incremental tavan noktası).
- [ ] **Test süiti:** `Measure-Command { dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance" }`
  → süre + test sayısı.
- [ ] **Acceptance:** OSYS reposu erişilebilirse `--filter "Category=Acceptance"` koş (~2 dk beklenir) →
  süre. Erişilemiyorsa "koşulmadı: neden" yaz, uydurma.
- [ ] **Uygulama koşusu (Release):** `dotnet build BuildOrchestrator.slnx -c Release` → App'in Release
  exe'sini `Start-Process` ile başlat; başlangıç ölçümü: process start damgası → `MainWindowHandle != 0`
  olana kadar 100 ms poll (kaba ama karşılaştırılabilir). Ardından 60 sn boşta bırak, 10 sn arayla
  `Get-Process` CPU-time delta + WorkingSet örnekle (App + Supervisor ayrı ayrı) → boşta CPU ~%0 mı,
  bellek sabit mi.
- [ ] **Exit temizliği:** pencereyi kapat (`CloseMainWindow`; kapanmazsa nedenini not et — bu başlı başına
  bulgudur, `Stop-Process`'e geçmeden 10 sn bekle). Kapanıştan 5 sn sonra process listesi:
  `BuildOrchestrator.*`, `MSBuild` kalıntısı var mı → zombie kontrolü.
- [ ] İddia envanterindeki her ölçülebilir iddianın karşısına ölçülen değeri yaz.
- [ ] Checkpoint güncelle.

## Görev 4: Adversarial doğrulama

- [ ] `findings-A/B/C.md`'yi birleştir, aynı kök nedene bağlı olanları tekilleştir (tek bulgu + çoklu konum).
- [ ] Her **bloklayıcı/önemli** bulgu için 3 bağımsız "çürüt" ajanı (farklı mercek: doğruluk / gerçekten
  ölçülebilir etki / bilinçli-karar-mı), en az 2/3 "gerçek" derse kalır. **Kozmetik/organizasyon** için 1
  ajan yeter. Çürütücü prompt'u: "Şu bulguyu ÇÜRÜTMEYE çalış: <bulgu+konum+kanıt>. Kodu kendin oku.
  Emin değilsen refuted=true döndür."
- [ ] Ölçüm sonuçlarıyla çelişen bulgular elenir (ör. "boşta CPU yiyor" iddiası ölçümde %0 çıktıysa).
- [ ] `verified-findings.md`: kalanlar + elenenler ("elendi: gerekçe" ile) + checkpoint güncelle.

## Görev 5: Rapor yazımı

- [ ] Zamanı al (`date "+%Y-%m-%d-%H-%M"`), raporu
  `.claude/outputs/<zaman>-performance-analysis-report.md` olarak yaz. Yapı:
  1. **Yönetici özeti** — en kritik 3-5 bulgu + genel sağlık hükmü, birkaç paragraf düz yazı.
  2. **Ölçüm sonuçları** — tablo: ölçüm, değer, yorum; ardından **iddia vs gerçek** tablosu (doküman
     iddiası → ölçülen/gözlenen → uyum).
  3. **Bulgular** — bloklayıcı → önemli → kozmetik; her madde: başlık, `Konum:` (dosya + satır),
     kanıt (kanıt-ama-değişmez satırlar etiketli), etki, öneri, tahmini kazanım.
  4. **Dağınıklık / organizasyon** — ayrı başlık, aynı madde formatı.
  5. **Doküman ile kod uyuşmayanlar** — varsa; karar kullanıcıya.
  6. **Yapılacaklar listesi** — öncelik sıralı; her satır: iş, dokunacağı dosyalar, beklenen kazanım,
     boyut etiketi (`[kisa]`/`[orta]`/`[uzun]`).
- [ ] Rakam gömme kuralı rapor için GEÇERLİ DEĞİL — rapor tarihsel kayıttır, ölçülen tüm sayılar yazılır.
- [ ] Checkpoint'te tüm görevleri "bitti" yap; `.claude/temp/perf-analysis/` dosyaları SİLİNMEZ (kanıt).

## Görev 6: Kapanış

- [ ] Kullanıcıya sun: yönetici özeti + rapor yolu + "commit edilsin mi?" sorusu.
- [ ] Commit onayı gelirse: `docs:` öneki + rapor dosyası, `main`'e (analiz kod değiştirmediği için branch
  gerekmez); onay gelmezse dosya commitsiz kalır.
- [ ] Düzeltmelere GEÇİLMEZ — fix'ler ayrı bir işin konusudur, kullanıcı raporu okuyup seçer.

---

## Başlatma

Kullanıcı "başla" / "plana göre başla" dediğinde: bu dosya + (varsa) `checkpoint.md` okunur, Görev 0'dan
(ya da kaldığı görevden) sürülür.
