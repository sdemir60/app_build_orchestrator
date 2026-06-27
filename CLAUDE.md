# App Build Orchestrator — Claude Talimatları

Bu proje, çok projeli bir .NET çözümünü (solution) akıllıca derleyen bir **build orchestrator** masaüstü uygulamasıdır. Dependency graph çıkarır, sadece değişen projeleri incremental olarak derler ve derlemeyi ayrı bir worker process üzerinde IPC ile yönetir.

---

## Proje Yapısı / Mimari

Solution: `BuildOrchestrator.slnx` (kökte). Ana git kökü: bu dizin.

| Proje | Target | Sorumluluk |
|---|---|---|
| `src/BuildOrchestrator.App` | net8.0-windows (WPF) | UI katmanı. MVVM (CommunityToolkit.Mvvm), DI, system tray, single-instance, worker ile IPC client. |
| `src/BuildOrchestrator.Core` | net8.0 | Çekirdek mantık: project discovery, dependency graph, git servisi, incremental planlama (DiffAnalyzer/IncrementalPlanner), state & config persistence. |
| `src/BuildOrchestrator.Worker` | net8.0-windows | Derlemeyi yürüten ayrı process: MSBuild engine, IPC channel, process control (Job Object, sweeper). App tarafından spawn edilir. |
| `src/BuildOrchestrator.Contracts` | netstandard2.0 / net8.0 | App ↔ Worker arası IPC sözleşmeleri: DTO'lar, enum'lar, WorkerCommand/WorkerEvent, JSON serialization. |
| `tests/BuildOrchestrator.Tests` | net8.0 (xUnit) | Core testleri: dependency graph, diff analyzer, incremental planner. |

> Not: `src/BuildOrchestrator.Native` solution'a dahil değildir (orphan/artık); aktif değildir, dikkate alma.

**Mimari ilkeler:**
- App, Worker'ın assembly'sine referans vermez; sadece çıktısını `Worker/` alt klasörüne kopyalar ve runtime'da process olarak başlatır. İletişim tamamen IPC (Contracts) üzerinden.
- Core, UI'dan ve Worker'dan bağımsız test edilebilir olmalıdır; iş mantığını App/Worker'a sızdırma.

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
- Çıktı ve özet dosyaları **aynı dosya adını** taşır, sadece klasörleri farklıdır — bu sayede ikisi kolayca eşleştirilir.

### Özet vs Aşama Kaydı (Tetikleyiciler)

- **"özet" / "özeti çıkar"** → Bu konuşmanın özetini `summaries/`'e yaz (yalnızca özet).
- **"aşamamızı kaydet"** → İKİSİNİ birden yap:
  1. Önce bu konuşmanın özetini `summaries/`'e yaz (yukarıdaki gibi).
  2. Sonra `handoffs/`'a **KISA** bir aşama girişi (handoff) yaz. Bu, kullanıcının yeni session'da mesajının başına yapıştıracağı devir belgesidir. **AMACI YALNIZCA:** (a) o ana kadarki ilgili özet dosyalarını listelemek (yol + tek satır; yeni oluşturulan bu konuşmanın özeti dahil), (b) en son nerede kaldığımızı işaretlemek.
     - **Kısa tut.** "Sıradaki adımlar / ne yapılacak / detaylı durum / çalışma ortamı" gibi bölümler YAZMA — kullanıcı devamını kendi mesajıyla yazar.
- Handoff dosyası da aynı isim formatını (`YYYY-MM-DD-HH-mm-...`) kullanır.

---

## Git

- Repo **GitHub**'da: `sdemir60/app_build_orchestrator`. Ana branch: `main`.
- Commit/push işlemlerini yalnızca senden istediğimde yap.
