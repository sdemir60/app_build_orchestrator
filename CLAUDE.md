# App Build Orchestrator — Claude Talimatları

Bu proje, çok projeli bir .NET çözümünü (solution) akıllıca derleyen bir **build orchestrator** masaüstü uygulamasıdır. Dependency graph çıkarır, sadece değişen projeleri incremental olarak derler ve derlemeyi ayrı bir **supervisor process** üzerinden yönetir; her projeyi **shell-out** (`MSBuild.exe` ayrı child process) ile derler.

---

## Ana Dokümanlar (kalıcı — ilk okunacak yer)

| Doküman | Ne için |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | **Teknik referans.** Mimari, process topolojisi, IPC sözleşmesi, incremental karar, build motoru, git yüzeyi, UI mimarisi, design system, bilinçli kararlar ve bilinen sınırlar. |
| [README.md](README.md) | Giriş: ne yapar, gereksinimler, build/test/run/publish, kullanım, kısayollar. |
| [docs/TRUST-BOUNDARY.md](docs/TRUST-BOUNDARY.md) | Güven sınırları (process/IPC/dosya sistemi/git/CPU), her iddia `dosya:satır` atıflı. |

**Bir kusur, davranış sorusu veya değişiklik isteği geldiğinde önce bu üçünü oku.** Bir davranışın "kusur mu,
bilinçli karar mı" olduğunun cevabı çoğunlukla ARCHITECTURE.md'dedir (§19 kabul edilen yapısal farklar, §20
bilinen sınırlar). Orada yoksa kaynak plan v7'dir (aşağıda).

> **DURUM:** Kod mevcut ve olgun — plan v7'nin kodlu iterasyonları (It-0→It-5) ve kapanış adımları tamamlandı;
> suite yeşil, publish hattı çalışıyor. Güncel rakamlar (test sayısı, ölçümler, park listesi) ledger'dadır:
> [.superpowers/sdd/progress.md](.superpowers/sdd/progress.md). Bundan sonraki çalışma **test-düzelt
> dalgalarıdır** (aşağıya bak) — playbook'un kalan adımları artık yürütülmüyor.
>
> **PLAN OF RECORD = plan v7:** [.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md](.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md)
> (+ içindeki `[SPIKE-AMEND 2026-07-16]`). Yasaklar, kabul ölçütleri ve A13 (WPF fidelity/teknik kararlar) orada
> bağlayıcıdır. **Görsel otorite:** [design-v1](.claude/outputs/2026-07-15-19-00-design-v1/README.md) —
> renk/ölçü/süre/kopya metinleri birebir. v2→…→v6 planları **tarihseldir**, referans alınmaz.

---

## Proje Yapısı / Mimari

Solution: `BuildOrchestrator.slnx` (kökte). Ana git kökü: bu dizin.

| Proje | Target | Sorumluluk |
|---|---|---|
| `src/BuildOrchestrator.App` | net10.0-windows (WPF) | UI katmanı. MVVM (CommunityToolkit.Mvvm), DI, system tray, single-instance, supervisor ile IPC client. **Outer Job Object** sahibi. |
| `src/BuildOrchestrator.Core` | net10.0 | Saf çekirdek mantık: discovery + evaluation cache, dependency graph (`GraphBuilder`/`ProducerMap`/`TopoSort`), incremental karar (`BuildSignature`/`IncrementalPlanner`/`WillBuildEvaluator`), scheduler (`ReadySetScheduler`/`DepIssueTracker`), git & worktree, MSBuild argüman/çağrı sözleşmesi, job object primitifleri, run log ve state persistence. UI/process bağımsız, test edilebilir. |
| `src/BuildOrchestrator.Supervisor` | net10.0-windows | Derlemeyi yöneten ayrı process: build kuyruğu, **inner Job Object**, her projeyi `MSBuild.exe` ile **shell-out**, log parse, IPC server (stdio). App tarafından spawn edilir. Planlamaz, yalnız yürütür. |
| `src/BuildOrchestrator.Contracts` | net10.0 | App ↔ Supervisor IPC sözleşmesi: DTO'lar, enum'lar, command/event, JSON serialization, NDJSON framing. |
| `tests/BuildOrchestrator.Tests` | net10.0-windows (xUnit, `UseWPF`) | Core unit + process-control + IPC + integration testleri; ayrıca WPF realize / STA thread testleri ve kaynak guard'ları (bu yüzden `-windows` + `UseWPF`). |

**Mimari ilkeler:**
- App, Supervisor'ın assembly'sine referans vermez; sadece çıktısını yanına kopyalar ve runtime'da process olarak başlatır. İletişim tamamen IPC (Contracts) üzerinden, **stdio newline-delimited JSON**; **stdout yalnız NDJSON**.
- **Derleme shell-out ile:** in-process MSBuild (BuildManager) kullanılmaz; her proje, `vswhere` ile resolve edilen **`MSBuild.exe`** child process'i olarak derlenir — **`dotnet build` DEĞİL** (hedef repo ağırlıkla legacy .NET Framework) (`-p:UseSharedCompilation=false -nodeReuse:false -p:BuildProjectReferences=false`).
- **§6.1 process kontrolü = nested Job Object:** App outer Job (`KILL_ON_JOB_CLOSE`) sahibi; Supervisor onun içinde doğar; Supervisor inner Job'da `MSBuild.exe` child'larını tutar. App ölünce kaskat halinde her şey ölür. Managed parent-watcher veya PID-heuristik süpürme **kullanılmaz**.
- **§4 OutDir'e dokunulmaz:** sadece `BaseIntermediateOutputPath` (obj) worktree modunda, proje **Id (tam yol)** anahtarıyla izole edilir. "Değişti mi" kararı yalnız kaynak sinyaline (commit + git diff) dayanır; DLL/bin timestamp asla okunmaz.
- **Git salt-okurdur:** `checkout`/`switch`/`pull`/`reset` ana repoda hiç çalıştırılmaz.
- Core, UI'dan ve Supervisor'dan bağımsız test edilebilir olmalıdır; iş mantığını App/Supervisor'a sızdırma. DI baştan kurulu.
- **Kopya YASAK / tek doğruluk kaynağı:** aynı değer, metin veya primitif iki yerde tanımlanmaz — ne kodda (ör. perf tablosu, konsol not metni, supervisor klasör adı) ne testlerde (ortak fixture/host'lar tek yerde toplanır). Bir sabiti ikinci kez yazma ihtiyacı duyuyorsan, birincisini paylaşılabilir hâle getir.

> Ayrıntı (algoritmalar, sabitler, sözleşmeler) ARCHITECTURE.md'dedir — burada tekrarlanmaz.

---

## Dil ve Üslup

- Yanıtları **Türkçe** ver.
- Teknik terimleri (transaction, handler, orchestration, dependency graph, incremental, process, IPC, build, worker, constructor vb.) İngilizce bırak, çevirme.
- Sade ve öz yaz. Gereksiz cümleyle uzatma.
- Sadece verilen bilgiye ve koddaki gerçeğe dayan; belirtilmeyen veya emin olunmayan şeyi yazma, varsayım/uydurma ekleme.
- **Kod, UI metinleri ve loglar İngilizce**; kod yorumları ve `.claude/` kayıtları Türkçe. README ve ARCHITECTURE.md İngilizce, TRUST-BOUNDARY.md Türkçe.

---

## Build / Test Komutları

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test  tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
dotnet run   --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj
```

> Doğrulama süiti **filtrelidir**. Filtresiz koşum `Category=Acceptance` üç testi de alır ve kullanıcının
> gerçek OSYS reposunu derler (~2 dk + ara sıra kırmızı). Kabul koşumu ayrıdır: `--filter "Category=Acceptance"`.
> Uygulama açıkken build alma — çalışan Supervisor kendi binary'lerini kilitler.

---

## Test-Düzelt Döngüsü (güncel çalışma modu)

Kullanıcı uygulamayı kullanır, kusuru tarif eder; **testi agent yazar**. Bir dalga şu kurallarla ilerler:

- **Kırmızı test kuralı (bağlayıcı):** hiçbir fix, kusuru yakalayan test KIRMIZI verdiği gösterilmeden yapılmaz.
  Kırmızıyı gösteremiyorsan test yanlıştır — testi düzelt, kuralı esnetme. (1430 test yeşilken animasyonların
  ölmüş olması bu kuralın gerekçesidir.)
- **Realize testi (zorunlu):** yeni XAML kökü/şablonu ekleyen her değişiklik bir realize testi de ekler.
  Headless suite XAML runtime çözümlemesini görmez. `Window.Measure/Arrange` HWND'siz içeriğe inmez — realize
  `window.Content` üzerinde yapılır.
- **Şiddet sırası:** bulguları önce bloklayıcı → önemli → kozmetik diye sırala ve sırayı kullanıcıya göster.
- **Belirsiz bulguda tahmin yürütme** — ayırt edici soru sor (her seferinde mi, pencere boyutuna bağlı mı,
  reduced-motion açık mı). Nedeni belirsiz bulgularda `superpowers:systematic-debugging` kullan; hipotezi
  doğrulamadan koda dokunma.
- **Kusur değil de kabul edilebilir bir sapmaysa:** plan v7 A13.1 "algısal eşdeğer" sınıfına GEREKÇESİYLE yaz,
  düzeltme. Gerekçesiz "eşdeğer" deme.
- Birden çok bulgu tek kök nedene bağlıysa tek fix'le kapat, ama **her biri için ayrı test** yaz.
- Dalga sonunda **tam süit yeşil** olacak; repo'daki token/motion/D8 guard'ları da koşar.
- Bulgu sayısı 5'ten fazlaysa: önce kısa TDD dökümü (`.claude/outputs/`), sonra
  `superpowers:subagent-driven-development` ile task-by-task.

---

## Doküman Güncelleme

Ana dokümanlar (ARCHITECTURE.md · README.md · docs/TRUST-BOUNDARY.md · CLAUDE.md) projenin **kalıcı** anlatısıdır.

**Tetikleyici — "dokümanları güncelle" (ve benzeri ifadeler):** o ana kadar yapılan değişiklikleri bu
dokümanlara işle. Kural:

- **Anlatı üslubu korunur.** Doküman projeyi ANLATIR; "şu oturumda şunu ekledik", "şu prompt'ta şu karar
  alındı", "eskiden böyleydi" gibi ifadeler YAZILMAZ. Değişen davranış, ilgili bölümde **yerinde yeniden
  yazılır** — doküman changelog biriktirmez.
- **Yer:** hangi bilgi hangi dokümana ait belli — teknik/mimari/tasarım → ARCHITECTURE.md; kullanım, komut,
  gereksinim, kısayol → README.md; process/IPC/dosya/git/CPU sınırı → TRUST-BOUNDARY.md; çalışma kuralı →
  CLAUDE.md. Aynı bilgi iki yerde ayrıntısıyla tekrarlanmaz; README özetler, ARCHITECTURE ayrıntılandırır.
- **Her iddia kodda doğrulanır.** Doğru olan ifadeye DOKUNMA. Emin olamadığın bir ifade varsa tahmin yürütme,
  kullanıcıya sor.
- **Rakam gömme.** Bir dalgada bayatlayacak sayılar (test sayısı, commit sha) dokümana yazılmaz; dayanıklı dil
  + ledger'a işaret.
- **`.claude/outputs/` ve `.claude/summaries/` tarihseldir** — geriye dönük düzeltilmez.

**Dalga içi senkron:** ayrıca her test-düzelt dalgasında, yapılan fix bir dokümandaki *olgusal* bir ifadeyi
yalanlıyorsa (mimari/akış değişikliği · yeni ya da kaldırılan komut/kısayol/script · TFM veya bağımlılık
değişikliği · IPC komutu, dosya yolu, process/job davranışı · README'nin "Using it"/"Keyboard shortcuts"/
"Performance modes"/"Known limits" bölümlerini yalanlayan davranış) o ifade **aynı dalgada** düzeltilir.
Yalanlamıyorsa dokunulmaz.

---

## Çıktı ve Özet Dosyaları

Kullanıcı "çıktıyı md dosyasına yaz", "bu çıktıları özetle", "aşamamızı kaydet" dediğinde aşağıdaki dizinler kullanılır (proje kökündeki `.claude/` altında):

- Çıktılar → `.claude/outputs/`
- Özetler → `.claude/summaries/`
- Aşama girişleri (handoff) → `.claude/handoffs/`
- Geçici dosyalar → `.claude/temp/`

### Dosya İsimlendirme

Format: `YYYY-MM-DD-HH-mm-{baslik}.md`

- Tarih ve saat her zaman o anki gerçek zamandır (Bash `date` ile al).
- Başlık: yaptığımız işlemi kısaca özetleyen kebab-case ifade (Türkçe karakter kullanılmaz).
- **Başlık İNGİLİZCE olur** (2026-07-16'dan itibaren geçerli kural): `it0-tdd-plan`, `spike-results`, `it0-records` gibi — Türkçe kelime kullanılmaz (`plani`, `kayitlari`, `karari` DEĞİL → `plan`, `records`, `decision`). Bir dosyaya referans veren linkler/kod da İngilizce ada göre yazılır. **Eski (Türkçe adlı) dosyalar oldukları gibi kalır** — geriye dönük yeniden adlandırma yapılmaz.
- Çıktı ve özet dosyaları **aynı dosya adını** taşır, sadece klasörleri farklıdır — bu sayede ikisi kolayca eşleştirilir.

### Özet vs Aşama Kaydı (Tetikleyiciler)

- **"özet" / "özeti çıkar"** → Bu konuşmanın özetini `summaries/`'e yaz (yalnızca özet).
- **"aşamamızı kaydet"** → İKİSİNİ birden yap:
  1. Önce bu konuşmanın özetini `summaries/`'e yaz (yukarıdaki gibi).
  2. Sonra `handoffs/`'a **KISA** bir aşama girişi (handoff) yaz. Bu, kullanıcının yeni session'da mesajının başına yapıştıracağı devir belgesidir. **AMACI YALNIZCA:** (a) o ana kadarki ilgili özet dosyalarını listelemek (yol + tek satır; yeni oluşturulan bu konuşmanın özeti dahil), (b) en son nerede kaldığımızı işaretlemek.
     - **Kısa tut.** "Sıradaki adımlar / ne yapılacak / detaylı durum / çalışma ortamı" gibi bölümler YAZMA — kullanıcı devamını kendi mesajıyla yazar.
- **"bu çalışma tamam" / "iz bırak" / "bir sonraki aşamaya geçeceğim" (ve benzeri ifadeler)** → SADECE `handoffs/`'a **çok kısa** bir handoff yaz (özet yazma). Dosya listesi **KÜMÜLATİFTİR**: bir önceki handoff'taki ilgili dosyaları taşı + bu session'da oluşturulan/güncellenenleri ekle (her biri yol + tek satır). Ayrıca ne yaptığımızı tek-iki cümle + "Buradan devam edilecek." de. Ekstra yorum / sıradaki adım / detay EKLEME — dosyalar zaten var.
- Handoff dosyası da aynı isim formatını (`YYYY-MM-DD-HH-mm-...`) kullanır.

---

## Git

- Repo **GitHub**'da: `sdemir60/app_build_orchestrator`. Ana branch: `main`.
- **Commit/merge serbest** (2026-07-21 kullanıcı kararı): bir iterasyon/dalga için kendi çalışma branch'ini aç, task başına commit at, iş bitince `main`'e merge et ve push'la.
- Merge'ün gerçekten geçtiğini **doğruladıktan sonra** çalışma branch'ini hem local'den hem remote'tan sil.
- Süreç sonunda çalışma dizini **`main` üzerinde** bırakılır (kullanıcı oturumu `main`'de görmek istiyor).
