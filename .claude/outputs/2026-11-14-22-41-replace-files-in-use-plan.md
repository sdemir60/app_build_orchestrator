# Replace files in use — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bir projenin post-build `copy`/`xcopy` komutu hedefte başka bir programın açık tuttuğu dosyaya çarptığında araç bunu
fark etsin; ayar açıksa yalnız o dosyayı hedef klasördeki gizli `.in-use` alt klasörüne taşıyıp aynı derlemeyi bir kez daha
çalıştırsın, kapalıysa dosyayı ve tutan programı konsolda söylesin.

**Architecture:** Core'da üç saf parça (komut okuma, hedef/kanıt/kilit, tutan program) + tek dosya taşıyıcısı (`InUseStash`,
çıktı klasörlerindeki TEK mutasyon yüzeyi) + bir `IMsBuildInvoker` decorator'ı (`InUseCopyInvoker`). Supervisor decorator'ı
`RetryingMsBuildInvoker`'ın DIŞINA sarar, koşu başında taşınanları süpürür, notu `decision.log`'a ve yeni `ProjectNoteEvent`
ile App'e yazar. App ayarı General → BUILD satırı olarak taşır ve notu konsola/akışa basar.

**Tech Stack:** .NET 10, WPF (App), xUnit, Win32 (Restart Manager, `QueryFullProcessImageName`), gerçek `MSBuild.exe` (yalnız Acceptance).

**Spec:** sohbette onaylanan plan (2026-11-14, "olur"); ölçümler `.claude/temp/exe-open-build-2026-11-14/` (git-ignore'lu).

## Global Constraints

- Yalnız `cmd` `copy`/`xcopy` komutları: proje çıktısında yankılanan komut satırı (normal verbosity, `<Target>:` başlığının
  altında, trim'li satır `^(copy|xcopy)\s`) ve yedek olarak `MSB3073` satırı. MSBuild'in Copy görevi (`MSB302x`) kapsam dışı.
- Dokunulan dosya = komutun kopyaladığı dosyanın hedefteki adı (`<hedef>\<kaynak dosya adı>`), YALNIZ (a) hedefe
  ulaşmamışsa (yok / boyut farklı / tarih farkı > 2 sn) VE (b) yazmak için açılamıyorsa (sharing violation `0x80070020`/`0x80070021`).
  Adı benzeyen başka dosyaya dokunulmaz.
- Taşıma hedefi: `<hedef klasör>\.in-use\` (Hidden). Ad: `<dosya adı>~<16 küçük hex>.pending` → sonuç belli olunca `.old`.
  Araç kopyalamaz; yalnız taşır, geri koyar ve kendi taşıdığını siler.
- Invoke başına tek yeniden deneme. Yeniden deneme başarısızsa (exit≠0, TimedOut, Killed ya da dosya yine ulaşmadı) yuvası
  boş kalan dosya geri konur.
- Ayar: Settings → General → BUILD, etiket `Replace files in use`, varsayılan **kapalı**. Kapalıyken yalnız not.
- Etiket TEK yerde: `Core/MsBuild/FilesInUseNotes.SettingLabel`; kataloğun satırı onu okur (kopya YASAK).
- Kullanıcıya giden metin İngilizce; kod yorumları Türkçe; test verisinde iç ürün/müşteri adı ya da iç yol yok (Acme).
- Testte `Thread.Sleep` ve sleep-poll yok. Her task'ın testi önce KIRMIZI gösterilir.
- Lokal test: yalnız ilgili sınıflar, `.claude/temp/run-tests.cmd` ile
  `start "bo" /b /wait /belownormal /affinity F0 dotnet test … --no-build --filter "…" -- xUnit.MaxParallelThreads=3`.

## Review Focus

1. Aynı hedefte önek paylaşan başka proje çıktısı (`Acme.Types.dll` derlenirken kilitli `Acme.Types.Common.dll`) → dokunulmaz (Task 2, Task 5).
2. UNC hedef (`"\\files\share\bin\"`) → komut okunur, aynı paylaşım içinde taşınır; tutan bulunamazsa "another program" (Task 1, Task 3).
3. Yeniden deneme sırasında Stop (Killed) → yuvası boş dosya geri konur (Task 5).
4. Motor taşıma ile yeniden deneme arasında ölürse → sonraki koşunun süpürmesi `.pending` + boş yuvayı geri koyar, `.old`'u asla geri koymaz (Task 4).
5. Salt okunur hedef dosya (kilit değil, erişim reddi) → taşınmaz, not yok (Task 2, Task 5).

---

### Task 1: Komut okuma

**Files:**
- Create: `src/BuildOrchestrator.Core/MsBuild/CopyCommands.cs`
- Test: `tests/BuildOrchestrator.Tests/MsBuild/CopyCommandsTests.cs`

**Interfaces:**
- Produces: `public sealed record CopyCommand(IReadOnlyList<string> Sources, string Destination);`
  `public static class CopyCommandReader { public static CopyCommand? FromEcho(string line); public static CopyCommand? FromMsb3073(string line); }`
  `public sealed class CopyCommandCollector { public void Observe(string line); public IReadOnlyList<CopyCommand> Commands { get; } }`
  (thread-safe; sıra korunur; aynı komut bir kez).

- [ ] **Step 1: Failing tests** — `[Trait("Category","MsBuild")]`:
  - `FromEcho` `  copy /y "C:\src\Acme.Types\bin\Release\Acme.Types.*" "C:\Acme\Client\bin\"` → Sources `[C:\src\…\Acme.Types.*]`, Destination `C:\Acme\Client\bin\`.
  - `xcopy /y /i "C:\a\x.dll" "C:\b\"` aynı; unquoted `copy /y C:\a\x.dll C:\b\` aynı; UNC hedef `"\\files\share\bin\"`.
  - null: `Copying file from "a" to "b".`, `1 file(s) copied.`, `copy "a"+"b" "c"`, tek yollu `copy /y "C:\a\x.dll"`, `robocopy …`.
  - `FromMsb3073` İngilizce: `…(6155,5): error MSB3073: The command "copy /y "C:\src\…\Acme.Types.*" "C:\Acme\Client\bin\"" exited with code 1. [C:\src\…\Acme.Types.csproj]`
    ve Türkçe kuyruk `… "C:\Acme\Client\bin\"" komutundan 1 koduyla çıkıldı. [….csproj]`; iki satırlı olayın ilk satırı
    (`error MSB3073: "copy /y "…\Acme.Types.*" "C:\Acme\Server\bin\" […csproj]`) ve son satırı ayrı ayrı okunur;
    tırnaksız yollu MSB3073 → null.
  - Collector: yankı + MSB3073 aynı komutu taşıyınca `Commands.Count == 1`; iki farklı hedef → 2.
- [ ] **Step 2: Run** — `CopyCommandsTests` → derleme hatası/kırmızı.
- [ ] **Step 3: Implement** — `FromEcho`: cmd kuralıyla tokenize (`"` aç/kapa, ters bölü kaçış DEĞİL), `/` ile başlayan token'lar
  atılır, `+` varsa null, ≥2 yol → son = hedef. `FromMsb3073`: `MSB3073:` sonrası, `\bx?copy\b` fiilinden itibaren yalnız
  TIRNAKLI mutlak yollar (`"([A-Za-z]:\\[^"]*|\\\\[^"]+)"`), ≥2 → son = hedef (yerelleştirilmiş sarmalayıcı/kuyruk okunmaz).
- [ ] **Step 4: Run** → yeşil. **Step 5: Commit** `feat(core): post-build copy komutlarını çıktıdan okur`.

### Task 2: Hedefler, ulaştı mı, kilit

**Files:**
- Create: `src/BuildOrchestrator.Core/MsBuild/CopyTargets.cs`
- Test: `tests/BuildOrchestrator.Tests/MsBuild/CopyTargetsTests.cs`

**Interfaces:**
- Consumes: `CopyCommand`.
- Produces: `public sealed record CopyTarget(string Source, string Destination);`
  `public static class CopyTargets { public static IReadOnlyList<CopyTarget> Resolve(IEnumerable<CopyCommand> commands); public static bool Landed(CopyTarget target); }`
  `public enum FileLockState { Free, Locked, Missing, Denied }` · `public static class FileLock { public static FileLockState Probe(string path); }`

- [ ] **Step 1: Failing tests** (TempDir):
  - Joker kaynak `src\Acme.Types.*` (dll, pdb, xml) + hedef klasör → 3 hedef `dst\Acme.Types.{dll,pdb,xml}`; `src`'de
    `Acme.Types.Common.dll` varken desen `Acme.Types.*` onu DA eşler (komut onu da kopyalar) ama hedefteki
    `Acme.Types.Common.dll` ayrı kaynaktan geliyorsa listede yok; hedef dosya yolu (`dst\renamed.dll`, tek kaynak) → tek hedef.
  - Var olmayan kaynak / dizin kaynak → hedef üretmez; aynı hedef iki komuttan → bir kez.
  - `Landed`: kopya sonrası (`File.Copy` + aynı LastWriteTime) true; boyut farkı false; tarih 3 sn eski false; hedef yok false; kaynak yok true.
  - `Probe`: tutulmayan → Free; `FileStream(FileShare.Read | FileShare.Delete)` ile tutulan → Locked; yok → Missing;
    salt okunur → Denied.
- [ ] **Step 2: Run** → kırmızı. **Step 3: Implement** (`Directory.EnumerateFiles(dir, pattern, TopDirectoryOnly)`;
  `Probe` = `FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete`, hemen kapat; HResult ile ayır).
- [ ] **Step 4: Run** → yeşil. **Step 5: Commit** `feat(core): kopya hedeflerini, ulaşıp ulaşmadığını ve kilidi okur`.

### Task 3: Tutan program

**Files:**
- Create: `src/BuildOrchestrator.Core/MsBuild/FileHolders.cs`
- Modify: `src/BuildOrchestrator.Core/ProcessControl/NativeMethods.cs` (Restart Manager + `QueryFullProcessImageName` bildirimleri)
- Test: `tests/BuildOrchestrator.Tests/MsBuild/FileHoldersTests.cs`

**Interfaces:**
- Produces: `public sealed record FileHolder(int ProcessId, string Name);`
  `public static class FileHolders { public static readonly TimeSpan DefaultTimeout; public static IReadOnlyList<FileHolder> Find(IReadOnlyList<string> paths, TimeSpan timeout); }`
  (`Name` = exe dosya adı, alınamazsa RM'in `strAppName`'i; hata/zaman aşımı → boş liste, ASLA fırlatmaz).

- [ ] **Step 1: Failing tests**: test sürecinin tuttuğu dosya → `ProcessId == Environment.ProcessId`, `Name` `.exe` ile biter;
  tutulmayan → boş; olmayan yol → boş; `TimeSpan.Zero` zaman aşımı → boş (fırlatmaz).
- [ ] **Step 2-4** kırmızı → implement (RmStartSession/RmRegisterResources/RmGetList/RmEndSession, `Task.Run(...).Wait(timeout)`) → yeşil.
- [ ] **Step 5: Commit** `feat(core): kilitli dosyayı tutan programı Restart Manager ile bulur`.

### Task 4: Taşıyıcı (`InUseStash`)

**Files:**
- Create: `src/BuildOrchestrator.Core/MsBuild/InUseStash.cs`
- Test: `tests/BuildOrchestrator.Tests/MsBuild/InUseStashTests.cs`

**Interfaces:**
- Produces: `public sealed record StashedFile(string OriginalPath, string StashPath);`
  `public sealed record StashSweep(int Removed, int Restored, int Kept);`
  `public sealed class InUseStash(string recordPath)` → `public const string FolderName = ".in-use"; public const string RecordFileName = "in-use-folders.json";`
  `public StashedFile? MoveAside(string path)` (taşınamazsa null) · `public void Settle(StashedFile file)` ·
  `public bool Restore(StashedFile file)` (yuva boşsa geri koyar → true; doluysa Settle → false) · `public StashSweep Sweep()`.

- [ ] **Step 1: Failing tests** (TempDir; kayıt dosyası da TempDir'de):
  - `MoveAside` tutulan (`FileShare.Read|Delete`) dosyayı `dst\.in-use\x.dll~<16hex>.pending`'e taşır, klasör Hidden, yuva boş,
    kayıt `dst`'yi listeler; delete paylaşmayan tutamaçla tutulan → null, dosya yerinde.
  - `Settle` → `.old`; `Restore` boş yuvada geri koyar (true), dolu yuvada `.old` yapar (false).
  - `Sweep`: kilidi kalkmış `.old` silinir; tutulan `.old` kalır (Kept); `.pending` + boş yuva → geri konur (Restored);
    `.pending` + dolu yuva → silinir; `.old` + boş yuva → silinir, GERİ KONMAZ; boşalan `.in-use` klasörü ve kayıttaki
    satırı kalkar; bozuk kayıt → boş sayılır, fırlatmaz; klasörde desene uymayan dosyaya dokunulmaz.
  - Eşzamanlı iki `MoveAside` (iki görev, aynı klasör) → iki ayrı ad, kayıtta klasör bir kez.
- [ ] **Step 2-4** kırmızı → implement (kayıt `AtomicFile.WriteAllText`, kilit nesnesi; ad deseni
  `^(?<name>.+)~(?<id>[0-9a-f]{16})\.(?<state>pending|old)$`) → yeşil.
- [ ] **Step 5: Commit** `feat(core): kilitli kopya hedefini yan klasöre taşır, geri koyar, süpürür`.

### Task 5: Decorator ve not metni

**Files:**
- Create: `src/BuildOrchestrator.Core/MsBuild/InUseCopyInvoker.cs`, `src/BuildOrchestrator.Core/MsBuild/FilesInUseNotes.cs`
- Test: `tests/BuildOrchestrator.Tests/MsBuild/InUseCopyInvokerTests.cs`

**Interfaces:**
- Consumes: Task 1-4.
- Produces: `public enum FilesInUseOutcome { Held, Replaced, CannotMove, FailedAgain }`
  `public sealed record FilesInUseReport(string Folder, IReadOnlyList<string> FileNames, IReadOnlyList<FileHolder> Holders, FilesInUseOutcome Outcome);`
  `public sealed class InUseCopyInvoker(IMsBuildInvoker inner, InUseStash stash, bool replace, Action<MsBuildInvokeRequest, FilesInUseReport> onReport, Func<IReadOnlyList<string>, IReadOnlyList<FileHolder>>? findHolders = null) : IMsBuildInvoker`
  `public static class FilesInUseNotes { public const string SettingLabel = "Replace files in use"; public static string Line(string projectName, FilesInUseReport report); }`
  (`Held`/`CannotMove`/`FailedAgain` satırı `warning: ` ile başlar).

- [ ] **Step 1: Failing tests** — sahte iç invoker GERÇEK kopyayı TempDir'de yapar (`File.Copy(..., overwrite:true)` +
  LastWriteTime kopyala; `IOException` → `  <kaynak>` + `The process cannot access the file because it is being used by another process.`
  satırı ve sonuçta exit 1 + MSB3073 satırı; çok satırlı senaryoda son komut başarılıysa exit 0):
  - replace açık, hedef `FileShare.Read|Delete` ile tutulu, exit 1 → 2 çağrı, ikinci başarı, hedef yeni içerik, `.in-use`'da
    `.old`, tek rapor `Replaced`, `DurationMs` = iki denemenin toplamı.
  - Aynı, ilk satırı başarısız son satırı başarılı iki satırlı olay (exit 0) → yine 2 çağrı ve `Replaced`.
  - replace kapalı → 1 çağrı, dosyaya dokunulmaz, rapor `Held`, sonuç aynen döner.
  - Derleme hatası (kopya satırı yok) / hedef ulaşmış / kilit yok (salt okunur) → 1 çağrı, rapor yok.
  - Delete paylaşmayan kilit → `CannotMove`, yeniden deneme yok.
  - İkinci deneme exit 1 / Killed → `FailedAgain`, eski dosya yerinde (yuva boştu).
  - Önek paylaşan kilitli `Acme.Types.Common.dll` hedefte → dokunulmaz.
  - `TimedOut`/`Killed` ilk deneme → dokunulmaz; `Target == Clean` → dokunulmaz; `RestoreAsync` doğrudan iç invoker'a gider.
  - `FilesInUseNotes.Line` dört sonucun metni (dosyalar, klasör, `Acme.Host.exe (PID 42)`, tutan yoksa `another program`,
    `Held` satırı `SettingLabel`'ı anar).
- [ ] **Step 2-4** kırmızı → implement → yeşil. **Step 5: Commit** `feat(core): kilitli kopyada taşı ve bir kez yeniden dene`.

### Task 6: Sözleşme ve motor

**Files:**
- Modify: `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs` (`StartRunCommand` + `bool ReplaceFilesInUse = false`;
  `ProjectNoteEvent(string RunId, string ProjectId, string Text)` + `[JsonDerivedType(..., "projectNote")]`)
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (ctor `InUseStash? inUseStash = null`; koşu başında `Sweep` →
  bir şey olduysa `decision.log` satırı; invoker = `InUseCopyInvoker(Retrying…, stash, cmd.ReplaceFilesInUse, report => { Decide; Events.TryWrite(ProjectNoteEvent) })`; stash null ⇒ bugünkü zincir)
- Modify: `src/BuildOrchestrator.Supervisor/Program.cs` (`inUseStash: new InUseStash(Path.Combine(cacheRoot, InUseStash.RecordFileName))`)
- Test: `tests/BuildOrchestrator.Tests/Supervisor/RunCoordinatorFilesInUseTests.cs`, IPC round-trip testine iki satır

- [ ] **Step 1: Failing tests**: NDJSON round-trip (`replaceFilesInUse` alansız satır → false; `projectNote` olayı);
  koordinatör + sahte toolset (Task 5'in kopyalayan sahte invoker'ı) → açıkken proje başarılı ve `projectNote` metni
  `FilesInUseNotes.Line` ile aynı; kapalıyken proje başarısız + `warning:` notu; stash yokken bugünkü davranış; koşu başında
  `.pending`+boş yuva geri konur.
- [ ] **Step 2-4** kırmızı → implement → yeşil. **Step 5: Commit** `feat(supervisor): kilitli kopya kararını koşuya bağlar`.

### Task 7: App — ayar ve not

**Files:**
- Modify: `App/ViewModels/GeneralSettings.cs` (enum `ReplaceFilesInUse`; BUILD satırı: `Label: FilesInUseNotes.SettingLabel`,
  açıklama "When a post-build copy finds its file held by a running program, move the old file aside and copy again. The program keeps running the old build until it restarts. Off: the copy stops there and the console names the program.", `Default: false`)
- Modify: `RunViewModel.cs` (alan + `StartRunCommand`'a), `RunViewModel.ActionBar.cs` (`ApplyReplaceFilesInUse` + `ApplySettingsAsync(…, bool? replaceFilesInUse = null)`),
  `SettingsDraftViewModel.cs`, `SettingsFile.cs` (`replaceFilesInUse`), `Shell/UiStateStore.cs`, `MainWindow.xaml.cs` (tohum + persist),
  `Views/SettingsDialog.xaml.cs` (taslağa canlı değer), `ProjectNoteEvent` → konsol (`AppendRunLine`) + akış (`PushStream`, `warning:` → Warn)
- Test: `GeneralSettingChains.cs` satırı (notlar: on "Replace files in use on — a file a running program holds is moved aside and copied again",
  off "Replace files in use off — a copy stops at a file a running program holds"), `GeneralSettingChainTests.GuardedNames`,
  `StartRunCommand` bayrak testi, `ProjectNoteEvent` konsol/akış testi

- [ ] **Step 1: Failing tests** → **Step 2** kırmızı → **Step 3** implement (StashOnBranchSwitch desenini izle) → **Step 4** yeşil
  (zincir teorileri + guard + ilgili App sınıfları) → **Step 5: Commit** `feat(app): Replace files in use ayarı ve kilitli dosya notu`.

### Task 8: Gerçek MSBuild ile kabul testi

**Files:**
- Create: `tests/BuildOrchestrator.Tests/Integration/FilesInUseAcceptanceTests.cs` (`[Trait("Category","Acceptance")]`, `WpfMiniFixture.ResolveMsBuildOrSkipAsync`)

- [ ] SDK-style mini kütüphane + `AfterTargets="Build"` `Exec copy` paylaşılan klasöre → derle → paylaşılan DLL'i
  `LoadLibraryEx(LOAD_LIBRARY_AS_IMAGE_RESOURCE)` ile eşle (gerçek görüntü kilidi) → kaynağı değiştir → gerçek
  `MsBuildInvoker` + `InUseCopyInvoker(replace:true)`: başarı, paylaşılan DLL yeni, `.old` var; `FreeLibrary` → `Sweep` → klasör yok.
  Kapalıyken: exit≠0 + `Held`. Önce kilit olmadan derleme düşmeli (kırmızı ön koşulu: kilit gerçekten kopyayı düşürüyor).
- [ ] Lokalde koş → yeşil → commit `test: kilitli kopyayı gerçek MSBuild ile pinler`.

### Task 9: Dokümanlar

- [ ] ARCHITECTURE: §9.4 istisna, yeni §9.7 *Files in use*, §5.2 `startRun` alanı, §5.3 `projectNote`, §13.3 General satırı,
  §16 kayıt dosyası + `.in-use` klasörü, §17.2 (guard eklenirse), §20 bilinen sınırlar (Copy görevi; çalışan program eski kodla
  sürer), §22 kod haritası.
- [ ] USER-GUIDE: Settings → General, "Building while your app is running", diskte durum, bilinen sınırlar.
- [ ] FEATURES + WHY (+ `.tr.md`) aynı sırada yeni madde; README (+tr) öne çıkanlar; CLAUDE.md "OutDir'e dokunulmaz" istisnası.
- [ ] `DocumentationTests` lokalde yeşil → commit `docs: Replace files in use`.

### Task 10: Kapı

- [ ] develop'a rebase → push → branch CI yeşil (düşen testler `scripts/ci-failed-tests.ps1`) → develop'a merge + push → develop CI yeşil → branch'i local/remote sil.
