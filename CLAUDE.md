# App Build Orchestrator — Claude Talimatları

Bu proje, çok projeli bir .NET çözümünü (solution) akıllıca derleyen bir **build orchestrator** masaüstü uygulamasıdır. Dependency graph çıkarır, sadece değişen projeleri incremental olarak derler ve derlemeyi ayrı bir **supervisor process** üzerinden yönetir; her projeyi **shell-out** (`MSBuild.exe` ayrı child process) ile derler.

---

## Proje Yapısı / Mimari

> **DURUM:** Kod mevcut ve olgun — walking-skeleton **It-0→It-5 tamamlandı**; suite yeşil, publish hattı çalışıyor. Güncel durum/rakamlar ledger'da: [.superpowers/sdd/progress.md](.superpowers/sdd/progress.md).
>
> **PLAN OF RECORD = plan v7:** [.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md](.claude/outputs/2026-07-16-08-39-build-orchestrator-plan-v7-implementation.md) (+ içindeki `[SPIKE-AMEND 2026-07-16]`). Aşağıdaki mimari ona dayanır. UI/görsel otorite v7 A7 üzerinden [design-v1](.claude/outputs/2026-07-15-19-00-design-v1/README.md); WPF fidelity kararları v7 A13'tedir. v2→…→v6 planları **tarihseldir** (zincirin ilk halkası [v2](.claude/outputs/2026-06-27-22-46-build-orchestrator-yeni-plan.md)) — referans alınmaz.

Solution: `BuildOrchestrator.slnx` (kökte). Ana git kökü: bu dizin.

| Proje | Target | Sorumluluk |
|---|---|---|
| `src/BuildOrchestrator.App` | net10.0-windows (WPF) | UI katmanı. MVVM (CommunityToolkit.Mvvm), DI, system tray, single-instance, supervisor ile IPC client. **Outer Job Object** sahibi. |
| `src/BuildOrchestrator.Core` | net10.0 | Saf çekirdek mantık: project discovery, dependency graph, git servisi, incremental planlama (DiffAnalyzer/IncrementalPlanner), state & config persistence. UI/process bağımsız, test edilebilir. |
| `src/BuildOrchestrator.Supervisor` | net10.0-windows | Derlemeyi yöneten ayrı process: build kuyruğu, **inner Job Object**, her projeyi `MSBuild.exe` ile **shell-out**, log parse, IPC server (stdio). App tarafından spawn edilir. |
| `src/BuildOrchestrator.Contracts` | net10.0 | App ↔ Supervisor IPC sözleşmeleri: DTO'lar, enum'lar, command/event, JSON serialization. |
| `tests/BuildOrchestrator.Tests` | net10.0-windows (xUnit, `UseWPF`) | Core unit + process-control + integration testleri; ayrıca WPF realize / STA thread testleri (bu yüzden `-windows` + `UseWPF`). |

**Mimari ilkeler:**
- App, Supervisor'ın assembly'sine referans vermez; sadece çıktısını yanına kopyalar ve runtime'da process olarak başlatır. İletişim tamamen IPC (Contracts) üzerinden, **stdio newline-delimited JSON**.
- **Derleme shell-out ile:** in-process MSBuild (BuildManager) kullanılmaz; her proje, `vswhere` ile resolve edilen **`MSBuild.exe`** child process'i olarak derlenir — **`dotnet build` DEĞİL** (hedef repo ağırlıkla legacy .NET Framework) (`-p:UseSharedCompilation=false -nodeReuse:false`).
- **§6.1 process kontrolü = nested Job Object:** App outer Job (`KILL_ON_JOB_CLOSE`) sahibi; Supervisor onun içinde doğar; Supervisor inner Job'da `MSBuild.exe` child'larını tutar. App ölünce kaskat halinde her şey ölür. Managed parent-watcher veya PID-heuristik süpürme **kullanılmaz**.
- **§4 OutDir'e dokunulmaz:** sadece `BaseIntermediateOutputPath` (obj) worktree altında, proje **Id (tam yol)** anahtarıyla izole edilir. "Değişti mi" kararı yalnız kaynak sinyaline (commit + git diff) dayanır; DLL/bin timestamp asla okunmaz.
- Core, UI'dan ve Supervisor'dan bağımsız test edilebilir olmalıdır; iş mantığını App/Supervisor'a sızdırma. DI baştan kurulu.

---

## Dil ve Üslup

- Yanıtları **Türkçe** ver.
- Teknik terimleri (transaction, handler, orchestration, dependency graph, incremental, process, IPC, build, worker, constructor vb.) İngilizce bırak, çevirme.
- Sade ve öz yaz. Gereksiz cümleyle uzatma.
- Sadece verilen bilgiye ve koddaki gerçeğe dayan; belirtilmeyen veya emin olunmayan şeyi yazma, varsayım/uydurma ekleme.

---

## Build / Test Komutları

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test  tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj
dotnet run   --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj
```

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
- **Başlık İNGİLİZCE olur** (2026-07-16'dan itibaren geçerli yeni kural): `it0-tdd-plan`, `spike-results`, `it0-records` gibi — Türkçe kelime kullanılmaz (`plani`, `kayitlari`, `karari` DEĞİL → `plan`, `records`, `decision`). Bir dosyaya referans veren linkler/kod da İngilizce ada göre yazılır. **Eski (Türkçe adlı) dosyalar oldukları gibi kalır** — geriye dönük yeniden adlandırma yapılmaz, yalnız yeni dosyalar bu kurala tabidir.
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
- **Commit/merge serbest** (2026-07-21 kullanıcı kararı): bir iterasyon için kendi çalışma branch'ini aç, task başına commit at, iş bitince `main`'e merge et ve push'la.
- Merge'ün gerçekten geçtiğini **doğruladıktan sonra** çalışma branch'ini hem local'den hem remote'tan sil.
- Süreç sonunda çalışma dizini **`main` üzerinde** bırakılır (kullanıcı oturumu `main`'de görmek istiyor).
