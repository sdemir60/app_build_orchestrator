# App Build Orchestrator — Claude Talimatları

Çok projeli bir .NET çözümünü, bağımlılık sırasına göre ve yalnız değişenleri derleyen WPF masaüstü aracı.

## Dokümanlar

| Doküman | Ne için |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | **Teknik referans.** Mimari, process topolojisi, IPC, incremental karar, build motoru, git yüzeyi, UI, design system, güven sınırı, bilinçli kararlar, bilinen sınırlar. |
| [README.md](README.md) | Giriş: ne yapar, gereksinimler, build/test/run/publish, kullanım, kısayollar. |

**Bir kusur veya davranış sorusu geldiğinde önce bunları oku.** ARCHITECTURE.md §22 kod haritasıdır (hangi
davranış hangi dosyada), §13-§14 UI ve design system'i — renk, ölçü, tipografi, motion ve bileşen davranışları
oradadır.

## Proje yapısı

Solution: `BuildOrchestrator.slnx` (kökte).

| Proje | Target | Sorumluluk |
|---|---|---|
| `src/BuildOrchestrator.App` | net10.0-windows (WPF) | UI, MVVM, DI, tray, single-instance, IPC client. **Outer Job Object** sahibi. |
| `src/BuildOrchestrator.Core` | net10.0 | Saf çekirdek: discovery, graph, incremental karar, scheduler, git/worktree, MSBuild sözleşmesi, job primitifleri, state. |
| `src/BuildOrchestrator.Supervisor` | net10.0-windows | Motor process: build kuyruğu, **inner Job Object**, per-project `MSBuild.exe`, IPC server. Planlamaz, yürütür. |
| `src/BuildOrchestrator.Contracts` | net10.0 | App ↔ Supervisor sözleşmesi: command/event, DTO, JSON, NDJSON framing. |
| `tests/BuildOrchestrator.Tests` | net10.0-windows (xUnit, `UseWPF`) | Core + process-control + IPC + integration + WPF realize/STA testleri + kaynak guard'ları. |

### Değişmezler (ihlal edilemez)

- **Shell-out:** in-process MSBuild (BuildManager) yok; her proje `vswhere` ile resolve edilen `MSBuild.exe`
  child process'i — `dotnet build` DEĞİL.
- **Nested Job Object:** App outer job sahibi, Supervisor içinde, `MSBuild.exe` inner job'da. Managed
  parent-watcher / PID heuristiği yok.
- **OutDir'e dokunulmaz.** Yalnız `obj` (worktree modunda, proje Id anahtarıyla) izole edilir. "Değişti mi"
  kararı sadece kaynak sinyalinden; DLL/bin timestamp asla okunmaz.
- **Git salt-okur:** `checkout`/`switch`/`pull`/`reset` ana repoda hiç çalıştırılmaz.
- **stdout yalnız NDJSON;** tüm log/tanı stderr'e.
- **Planlama Core'da.** İş mantığını App/Supervisor'a sızdırma; Core UI ve process bağımsız test edilebilir kalır.
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

- Repo: `sdemir60/app_build_orchestrator` (GitHub). Ana branch: `main`.
- Bir iş için kendi çalışma branch'ini aç, task başına commit at, bitince `main`'e merge + push.
- Merge'ün geçtiğini **doğruladıktan sonra** branch'i local ve remote'tan sil.
- Oturum **`main` üzerinde** bitirilir.
