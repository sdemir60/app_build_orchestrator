# Cycle güveni ve yüzey kapısı — Uygulama Planı

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build/Rebuild'in cycle gruplarını tur mekanizmasıyla derlemesi KALIR; üç aşırı-temkinli kural düzeltilir
(VS'nin derlediği üye güvenilmez sayılıyordu; tek başarısız üye 17 kaydı zehirliyordu; ilerleme çubuğu grup boyunca
durukdu) ve "bağımlılığın API yüzeyi değişmediyse derleme" kuralı sıradan projelere ve cycle üyelerinin grup dışı
upstream'lerine de uygulanır. Kullanıcının günlük akışı (Business/Types'ta gövde değişikliği → Build) dakikalar
yerine saniyeler sürer.

**Architecture:** Kararlar Core'da saf fonksiyonlardır (`CycleMemberNeed`, yeni `SurfaceGate`, `CycleRoundPolicy`
değişmez); Supervisor yalnız uygular; App yalnız olayları sayar. Yüzey kanıtı tek primitiftir (`ApiSurfaceHash`):
cycle içinde bugün olduğu gibi, cycle dışında yeni bir defter alanıyla (`DependencySurfaces`). Hiçbir yeni IPC alanı,
yeni UI öğesi, yeni skip gerekçesi yok. İki bölüm, her biri kendi başına merge edilebilir.

**Tech Stack:** .NET 10 (`net10.0` / `net10.0-windows`), WPF, xUnit. Supervisor process + NDJSON IPC, `MSBuild.exe`
shell-out. Derleme/test: `dotnet build BuildOrchestrator.slnx` ·
`dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`.

**Spec:** Bu dosyanın **A bölümü**. Ayrı spec dosyası yok. Kanıt: kullanıcının 2026-10-08 ~14:10 koşusu
(`%LOCALAPPDATA%\BuildOrchestrator\logs\` altındaki o güne ait `decision.log`: UI grubunun 16 üyesi
`output built outside this tool`, 1 üyesi `own inputs changed`; önceki Rebuild'de tek MSB3073 kilidi NoProgress
verip 17 kaydı geçersizlemişti), araştırma raporları (altı keşif + üç hakem; özetleri A.6'da) ve önceki plan
`.claude/outputs/2026-10-08-05-16-build-compiles-dirty-cycles-plan.md`.

---

## A. Tasarım kararları (spec)

### A.1 Sorun (tek paragraf)

Build artık kirli cycle grubunu turlarla derliyor (doğru). Ama tur 1'in "kim derlenecek" kararı iki yerde fazla
temkinli: (1) VS'nin derlediği üye ("zaman kipi") hiç sorgulanmadan gerekli sayılıyor — 17 üyeli UI grubunda 16 üye
yalnız bu yüzden derleniyor; (2) grup yakınsamazsa (tek üyede copy-lock → NoProgress) yeşil biten 16 üyenin kaydı da
geçersizleniyor ve bir sonraki Build 17'sini yeniden derliyor. Üstüne, grup derlenirken n/m sayacı ve çubuk 4-6 dakika
boyunca kıpırdamıyor. Son olarak "yüzey değişmediyse derleme" kuralı yalnız cycle içinde var: Business'ta gövde
değişince bütün bağımlıları (UI, Container…) ve UI grubunun 17 üyesi (terim grup dışı upstream imzasını taşıdığı
için) boşuna derleniyor.

### A.2 Kararlar

**Değişmeyenler (kapalı konular, kullanıcı kararı):** tur mekanizması ve tavan 3 (`CycleRoundPolicy`); Resolve cycles
koşusu (dar kapsam, strict); Rebuild "her üye tur 1'de" kalır; bekleyen (root-waiting) grubun bağımlılarının fazladan
derlenmesi kalır; copy-lock'ta otomatik process öldürme yok; bellek kırpması kalır; etiket/renk/gri standardı
değişmez; yeni UI öğesi yok; Contracts (IPC) değişmez.

| # | Karar | Gerekçe |
|---|---|---|
| **D1** | Turlar aynen kalır. | Yukarıda. |
| **D2** | **Kural (v) daralır.** `CycleMemberNeed` zaman kipindeki (VS'nin derlediği) üyeyi artık kip yüzünden gerekli saymaz; yalnız KENDİ girdisi çıktısından yeniyse (`TimeVerdict.OwnNewer`) gerekli olur — çıktı mevcut kaynaktan üretilmemiş olabilir (VS derlemesinden sonra düzenleme ya da branch değişimi: git dosyaları yeniden yazar, içerik defterdeki terime döner ama diskteki DLL başka branch'in gövdesidir). `Fresh` ve `DependencyNewer` üye kalan kurallarla sınanır: (iii) kayıt, (vi) motor, (i) kendi terimi, (iv) kanıt var + beslenen kopya sağlam, (ii) okuduğu kardeş yüzeyleri diskle aynı ⇒ taşınır. `OutputBuiltOutsideReason` → `OutputOlderThanInputsReason` ("output older than its inputs"). | Zaman damgası tek başına güncellik kanıtı DEĞİLDİR; taşıma dayanağı içerik terimi + kayıttaki okuma yüzeylerinin diskle karşılaştırmasıdır. Ama "kendi girdisi çıktıdan yeni" bayatlık KANITIDIR (CLAUDE.md: başkasının derlediği çıktıda tarihlere bakılır) ve terim onu göremez — terim yalnız "kaynak, aracın son derlediği kaynakla aynı" der, diskteki yabancı çıktının o kaynaktan üretildiğini söylemez. `ApiSurfaceHash` gövde/MVID saymadığı için VS'nin aynı kaynaktan ürettiği kardeş aynı özeti verir; API değişen kardeşi (ii) yakalar. Kullanıcının koşusundaki 16 üye `Fresh`/`DependencyNewer` idi: kazanç korunur. |
| **D2-a** | Taşınan üyenin kaydı bugünkü gibi yenilenir (`BuiltSignature`, `LastRunAt=now`): üye defter kipine döner. | Sıradan "built outside" projesiyle asimetri bilinçli: üyenin kanıtı içerik + yüzeydir, zaman değil; bir sonraki koşuda (ii) yine korur. §7.5/§8.8'e yazılır. |
| **D3** | **Yakınsamayan grup, oturmuş başarıları korur.** NoProgress ve CapReached'te: son turun `staleNow` kümesinde OLMAYAN başarılı üye güvenilir persist edilir (bileşik imza + üç döngü alanı); `staleNow`'daki başarılı üye bugünkü gibi geçersizlenir; hopeless (kanıtlı) hata bugünkü gibi; diğer hatalar kanıtsız. Yüzey kanıtı yoksa (`staleNow == null`) hiçbir şey değişmez (bugünkü kural). Oturmuş taşınan üye `ReportCarriedCycleMember` yolundan geçer (kayıt yenilenir). | Yeşil + oturmuş üye nihai yüzeylere bağlandı; kaydı doğrudur. Bayat üye bir kuşak geride olabilir; geçersizlenince grup kirli kalır ve bir sonraki Build tur 1'de yalnız onu derler (`CycleMemberNeed`: "no trusted record"). Pre-skip tuzağı (hakemlerin kritik bulgusu) böyle kapanır. |
| **D3-a** | Grup içi başarısız kardeş, oturmuş yeşil üyeye dep-issue notu YAZMAZ. | Kardeş düzelince yüzeyi değişirse (ii) üyeyi zaten derletir; not yalnız her üyeyi gereksiz "needed" yapardı. Kullanıcı görünürlüğü: X'in kırmızı satırı + stream'deki "no progress" satırı. |
| **D3-b** | App: `OnCycleCompleted(NoProgress)` yalnız `State != Skipped && WillBuild != false` olan satırları `CycleUnconverged` işaretler (güvenilir başarı `UpToDate` yazıldığı için, oturmuş taşınan üye `skipped — up to date` olduğu için işaretlenmez); `CapReached` yeşil üyesinde `CycleUnsettled` yalnız bayat olanlara gider. | "N stuck in a cycle" dürüst sayar; yeşil + "did not fully settle" yalnız gerçekten bayat üyede. |
| **D4** | **İlerleme üye başına.** App'te koşu başına `_heldCycleMembers` kümesi: `CycleMemberHeldEvent` (Started guard içinde, `_willBuildIds` üyesiyse) ekler; `ProjectStartedEvent` (tur ≥2 yeniden derleme) çıkarır; `FinishedOfWillBuild` = terminal ∪ held. Koşu başında temizlenir. `RunCounters`, `RibbonText.Progress`, tur satırı metni DEĞİŞMEZ. | Çubuk tur 1 boyunca üye bitince ilerler; tur 2 başında yeniden derlenen üyeler kadar geri adım atar (dürüst). ETA eki turlar sürerken zaten yazılmaz (§8.4). |
| **D5** | **Yüzey-kapılı adaylar (Part 2).** Supervisor planı Safe'e ek Fast(kanıtlı) bağlama da yapar; aday = Safe `WillBuild==true && Reason==SignatureChanged` ∧ Fast `WillBuild==false && Reason==UpToDate` ∧ doğrudan bağımlılığı var ∧ `!InCycle`. Küme `IncrementalPlan.SurfaceCandidateIds`'e girer; yalnız `IncrementalModes.Includes(mode) && !scopedRun` iken okunur. | Fast imzası = cfg + kendi içeriği + upstream'lerin DEFTERDEKİ imzası: configuration değişince Fast de değişir (hakemlerin Debug→Release tuzağı kapanır); Fast `UpToDate` defter kipi + kanıt yerinde + beslenen kopya sağlam demektir (kanıt sağlığı). Bilinen sınır: upstream daha önceki bir koşuda derlenmişse (Stop sonrası, satırdan Build) Fast "değişti" der ve bağımlı bir kez koşulsuz derlenir — güvenli yön, §20'ye yazılır. |
| **D6** | `BuildState`'e SONA, default null `IReadOnlyList<CycleReadSurface>? DependencySurfaces` eklenir (üretici id, kanıt dosyası, yüzey özeti; kanonik sıra; `Equals`/`GetHashCode` elle). Sıradan projenin her güvenilir başarısında doğrudan bağımlılıklarının o anki yüzeyiyle yazılır; cycle üyesinde grup DIŞI doğrudan bağımlılıklar için yazılır (grup içi `CycleReadSurfaces`'ta kalır). `ApiSurfaceHash.Absent` ve null deftere YAZILMAZ (o bağımlılık listede olmaz → kapı o projeyi derler). | Karşılaştırma tabanı BAĞIMLI tarafında olmalı (hakemlerin kritik bulgusu: bağımlılık satırdan derlenince kendi kaydı değişir, bağımlının neye bağlandığını kimse bilmezdi). |
| **D7** | **Kapı sırası geldiğinde.** Core'da saf `SurfaceGate.Decide(directDeps, completed, recordedSurfaces, surfaceOf) → Build \| Unchanged`: her doğrudan bağımlılık için — bu koşuda Failed ⇒ Build; kayıtta yok ⇒ Build; şimdiki yüzeyi (`surfaceOf`: koşu seviyesinde tembel önbellek, diskteki kanıt dosyasından `ApiSurfaceHash`; Succeeded/Skipped fark etmez) null/Absent ⇒ Build; kayıttakinden farklı ⇒ Build; hepsi eşit ⇒ Unchanged. Unchanged ⇒ `skipped — up to date (no dependency surface changed)`, kayıt yenilenir (`RefreshBuildStateOnSkip`: yeni bileşik imza, zaman, revizyon, dep-issue notu; süre/içerik/yüzeyler aynen), `Complete(Skipped)`. | Atlanan bağımlılığın yüzeyi de diskten OKUNUR ("skipped ⇒ unchanged" varsayımı yok). Hash yalnız metadata okur (ms). Karar Core'da (planlama Core'da). |
| **D7-b** | **Cycle üyesinin grup dışı upstream'i (Part 2, son task).** Üye terimi (`MemberTermById`) yalnız KENDİ terimi olur (cfg + içerik; tüm upstream'ler NullMarker) — bileşik imza DEĞİŞMEZ (eski terimle hesaplanmaya devam eder). `CycleMemberNeed`'e yeni kural **(i-b)**: kayıttaki `DependencySurfaces` üyenin her grup dışı doğrudan bağımlılığını kapsamalı ve her biri grup başında okunan yüzeyle aynı olmalı; değilse `dependency surface moved: <dosya>`. Grup başı hash'i grup dışı üreticilerin YALNIZ kanıt dosyasını okur (beslenen kopyaları değil — `DependencySurfaces` ile aynı dosya); okunamayan ya da kanıt yolu türetilemeyen grup dışı üretici hashMode'u düşürmez, `surfaceState`'e girmez → okuyanı gerekli (güvenli yön; kanıt yolu olmayan SDK-style upstream'in okuyucusu grup her kirlendiğinde derlenir — §20'ye bilinen sınır). | Kullanıcının en pahalı akışı: Business gövde değişikliği → bugün 17 UI üyesi "own inputs changed". Eski terimli kayıtlar yeni terimle eşleşmez → bir kez "own inputs changed" ile derlenir, sonra yeni kayıt (güvenli geçiş). |
| **D8** | **Aday satır gri ve SAYILIDIR.** `Conditional` bayrağı adaylar için YAZILMAZ: satır bugünkü gibi gri `affected`, dalgada yanar, kuyrukta, `N to build`'de ve n/m paydasında; sırası gelince hızla atlanır (terminal `Skipped` ⇒ n ilerler). `OnProjectSkipped`: koşu sürerken, planın derleyecek dediği (`row.WillBuild == true`) satıra `SkipReasons.UpToDate` gelince `NextPreview.AfterUpToDateSkip()` (= `(false, UpToDate, false)`) uygulanır — satır hemen yeşile döner, `OwnFilesChanged=false`; pre-skip satırları (`WillBuild` zaten false, gerekçesi `BuiltOutside` olabilir) DOKUNULMAZ. Proje sayfası metni DEĞİŞMEZ (App adayı ayırt edemez: cfg değişimi, Rebuild, Fast kipi aynı olguyu verir; yanlış cümle yazmaktansa bugünkü "the signature changed" doğru kalır). Sync değişmez. | Kullanıcı: "bağımlılıklarına göre gri oluyor sağolsun" — standart gri korunur, yeni bayrak/öğe/IPC alanı yok; Task 4 C1 titremesi riski yok. Bilinen sınır: kapıdan atlanan kaydın bağımlılık notu (miras kök) varsa bir sonraki Sync `WaitingForDependency` der, satır o Sync'e kadar `UpToDate` okur — olay kök taşımaz, Contracts değişmez; §8.3'e yazılır. |
| **D9** | Yüzey hash'i sıradan projede ayrıca HESAPLANMAZ: bağımlı, sırası gelince bağımlılığının kanıt dosyasını diskten okur ve koşu seviyesindeki `ConcurrentDictionary<string,string?>` önbelleğine koyar (`GetOrAdd`). Cycle grubunda grup başı hash'i zaten var. | "Complete'ten önce yaz" sıralama tuzağı ve slot sözleşmesi tartışması tamamen düşer; bağımlılık dispatch anında terminaldir, çıktısı diskte nihaidir. |
| **D10** | Transitiflik koşu zamanı özelliğidir: her bağımlı yalnız DOĞRUDAN bağımlılıklarına bakar; kapıyla atlanan projenin yüzeyi tanım gereği değişmemiştir, onun bağımlıları onu "unchanged" görür. Planlayıcının fazla işaretlemesi (bütün aşağı akış `WillBuild=true`) güvenli yöndür ve değişmez. | — |
| **D11** | Dokümanlar yerinde yeniden yazılır (anlatı); eski kuralı pinleyen test eski iddia + değişme gerekçesiyle yeniden yazılır, silinmez/gevşetilmez. | CLAUDE.md. |
| **D12** | Ölçüm: her bölümün sonunda gerçek OSYS'te kullanıcı senaryosu (VS'de UI üyesinde gövde değişikliği + Build; Business'ta gövde değişikliği + Build) koşulur, derlenen üye/proje sayısı ve süre `.claude/outputs/` raporuna ve ilgili test sınıfının doc'una yazılır. ARCHITECTURE'a rakam gömülmez. | — |

### A.3 Kullanıcının göreceği akış

**Bölüm 1'den sonra** — VS'de `UI.Service.WorkOrder` gövdesini değiştirip VS'de derledi, sonra Build:
1. Dalga: UI grubunun 17 üyesi yanar (bileşik imza değişti), bağımlıları gri.
2. Grup sırası gelince: tur 1 yalnız `UI.Service.WorkOrder`'ı derler ("own inputs changed"); 16 üye taşınır
   (decision.log: `skipped — up to date (carried: …)`). API değişmediyse grup tur 1'de yakınsar.
3. Çubuk üye bitince ilerler (n/m 1/17 → …); 16 taşınan üye grup hükmünde birlikte terminal olur.
4. Tek üyede copy-lock → NoProgress: o üye kırmızı, 16 kardeş yeşil (güvenilir); bir sonraki Build yalnız o üyeyi
   derler.

**Bölüm 2'den sonra** — `Business.Service.WorkOrder`'da gövde değişikliği, Build:
1. Dalga: Business + bütün aşağı akışı (UI üyeleri, Container…) gri/yanık, `N to build` hepsini sayar.
2. Business derlenir. Sıradan bağımlıları sırası gelince Business'ın yüzeyini okur, kayıtlarındakiyle aynı →
   `skipped — up to date (no dependency surface changed)`; satır yeşile döner, çubuk ilerler.
3. UI grubu sırası gelince: üyelerin kendi terimi aynı, grup dışı Business yüzeyi aynı → 17 üye taşınır, grup tur
   1'de 0 derlemeyle yakınsar.
4. "Completed — 1 succeeded · N skipped".

### A.4 Değişmeyenler

`CycleRoundPolicy`, `CycleRoundLevels`, `CycleReadFiles`, `ApiSurfaceHash`, `OutputEvidence.ApplyCycleGroups` (§5.6
zaman kipi yayılımı WillBuild için kalır), `ConditionalRebuild` (root-waiting), `IncrementalModes`,
`CycleCompilation`, `ReadySetScheduler`, Contracts/IPC, `SkipReasons` (yeni gerekçe YOK — `UpToDate` + detay),
`WorkerBudget`, perf profilleri, Sync'in sayaçları.

### A.5 Kullanıcı cevapları (2026-10-09) — açık soru KALMADI

1. **Uygulama paylaşılan çıktı klasöründen çalışıyor** (`C:\OSYS\…\Client`, post-build kopyalarıyla). Araç genel kalır,
   OSYS'e özgü kural YOK: kapıyla atlanan projenin kendi `bin`indeki copy-local kopyaların tazelenmemesi §8.8'deki
   mevcut "known limit" paragrafıyla aynı genel sınırdır (kendi klasöründen çalışan bir düzen, o proje derlenene dek
   bağımlılığın eski gövdesini görür) ve §20'ye aynı dille yazılır. Exe projeleri aday dışı BIRAKILMAZ.
2. `OutputPath` sorusu bilgi içindi: D2-a'nın bedeli bugünkü "built outside" kuralıyla aynı sınırdır, §20'ye yazılır.
3. VS'de solution build de proje build de yapılıyor: ölçüm adımı (Bölüm 1 Task 4) iki akışı da ölçer ve ikisini de
   rapora yazar.
4. Copy-lock hataları "kanıtlı hata" sayılmaya devam eder (kullanıcı: "hata verirse versin") — kapsam dışı.

### A.6 Araştırmadan alınan düzeltmeler (özet)

- CapReached'te tüm yeşilleri güvenmek pre-skip tuzağı açar → D3 "yalnız oturmuş" kuralı.
- D2 için kardeş zaman karşılaştırması `OutputCheck.Time`'a konamaz (`ApplyCycleGroups`/`BehindDirtyUpstream`
  `Fresh`/`FedBroken`'ı yeniden yazar) → kardeş zamanı hiç okunmuyor; üyenin KENDİ `OwnNewer` hükmü ise her iki
  yerde korunur ve kural (v) ona daraltılır (plan incelemesi, kritik bulgu: branch değişimi senaryosu).
- D5 yüklemi `SignatureChanged && OwnFilesChanged==false` olamaz (cfg tuzağı) → Fast eşitliği.
- D7 taban bağımlı tarafında olmalı → `DependencySurfaces`; atlanan bağımlılığın yüzeyi diskten okunur.
- D8 "koşullu satır derlenmeye başlayınca paydaya katılır" cümlesi kodda karşılıksızdı → düşürüldü; adaylar zaten
  sayılı.
- D4 çift sayım / kesilen grup → küme `_willBuildIds` ile kapılı, koşu başında sıfırlanır, Started'da düşer.
- D7-b: üye terimi grup dışı upstream imzasını taşıdığı için Part 2'nin UI grubuna kazancı sıfırdı → terim kendi
  terimi olur, upstream yüzeyle denetlenir.

---

## Global Constraints

- **Branch:** Bölüm 1 `feat/cycle-trust`, Bölüm 2 `feat/surface-gate`; ikisi de `develop`'tan (Bölüm 2, Bölüm 1
  merge edildikten sonra). Ana checkout `D:\Projects\Other\Apps\app_build_orchestrator` (worktree YOK). Her task
  sonunda commit; mesajlar Türkçe, `feat:`/`fix:`/`test:`/`docs:`/`refactor:` önekli; **attribution satırı
  yazılmaz** (`Co-Authored-By`, `Generated with` YOK — CLAUDE.md). Bitişte `develop`'a merge + push, branch'i
  local+remote sil, CI'ı kontrol et (düşen ortam testi varsa yeniden üret, `LocalOnly`), oturum `develop`'ta biter.
- **Kırmızı test kuralı:** her adımda önce test, kırmızı gösterilir (`dotnet test … --filter "FullyQualifiedName~…"`),
  sonra kod. Eski kuralı pinleyen test silinmez/gevşetilmez: yeni kuralı pinleyecek biçimde yeniden yazılır ve XML
  doc'una **eski iddia + değişme gerekçesi** yazılır (gerekçe metinleri aşağıda her task'ta hazır).
- **Kopya YASAK:** "bu koşu kapı uygular mı" = `IncrementalModes.Includes(mode) && !scopedRun` tek yerde
  (`SurfaceGate.AppliesTo`); atlama detayı Core const; kayıt yenileme TEK gövde (`RefreshBuildStateOnSkip`); yüzey
  hash'i tek seam (`_apiSurface`).
- **Katman:** karar Core (saf, I/O yok), uygulama Supervisor, sayım App. IPC'ye alan EKLENMEZ.
- **Ortak fixture tek yerde:** `AllSucceed()` `RunCoordinatorTests`'e taşınır (`ConditionalRebuildRunTests`'teki özel
  kopya silinir); `CycleRoundsTests.ResolveAsync` `internal` olur ve `RunMode mode = RunMode.Cycles` parametresi alır —
  yüzey kapısı testleri onu `mode: RunMode.Build` ile çağırır, ikinci bir koşturucu YAZILMAZ.
- **Dil:** kod/UI/log İngilizce; yorumlar ve `.claude/` Türkçe; README/ARCHITECTURE İngilizce, anlatı üslubu, yerinde
  yeniden yazım, "eskiden/şimdi" anlatısı ve bayatlayacak rakam YOK.
- **Dokunulmaz:** `CHANGELOG.md`, `Directory.Build.props` → `Version`, IPC mesajları (`Contracts/Ipc/IpcMessages.cs`
  — yeni alan/olay yok), OutDir/bin/obj kuralları, git mutasyon yüzeyi. `BuildState` (`Contracts/Model/ProjectModels.cs`)
  telde taşınmayan defter modelidir; D6 ona SONA, default'lu alan ekler — eski JSON null okur.
- **Açık uygulama:** Build Orchestrator (tray dahil) ve `BuildOrchestrator.Supervisor.exe` açıkken Debug bin
  kilitlidir. Kapat; kapanmıyorsa `-c Release` ile derle/test et, `--no-build` KULLANMA, yeni testlerin koştuğunu
  `--list-tests` ile doğrula.
- **Süit:** task sonunda ilgili sınıf(lar); bölüm sonunda tam süit `--filter "Category!=Acceptance"` yeşil (çıktıyı
  dosyaya al: `> .claude/temp/suite.txt 2>&1`; `Select-String` hata gövdesini yutar). Zamanlama testleri yük altında
  düşerse eşik gevşetilmez; tek başına sakin tekrar. Yeni test pencere açmaz, CPU yakmaz.
- **İlerleme panosu:** uygulayan oturum başında adım listesini
  `& 'C:\Users\Delta\.claude\scripts\status.ps1' -Event steps -Sid <session_id> -Steps ...` ile gönderir; task başına
  bir adım, boyut etiketi dürüst (`[kisa]`/`[orta]`), aynı anda tek `in_progress`.
- **Belirsizlik:** plan kodla uyuşmuyorsa (satır kaydı, ad) kodu esas al, plandaki niyeti koru, kararı ledger'a
  `Ruling:` olarak yaz; doküman kodla uyuşmuyorsa kullanıcıya sor.

## Review Focus

1. **VS'nin API'si değişmiş üyeyi derlediği, okuyanını ESKİ API'yle paralel derlediği durum (üye çıktısı yeni,
   kardeş yüzeyi kayıttan farklı):** okuyan üye tur 1'de derlenmeli (kural ii) — Bölüm 1 Task 1 Adım 6
   (`a_time_mode_member_whose_read_surface_moved_is_compiled`).
2. **CapReached'te son turda bayat kalan yeşil üye:** geçersizlenir, bir sonraki Build yalnız onu derler — Bölüm 1
   Task 2 Adım 5 ("cap reached" dalı).
3. **Kanıt (hashMode) düşmüş NoProgress/CapReached:** hiçbir yeşil güvenilmez (bugünkü kural) — Bölüm 1 Task 2 Adım 7.
4. **Fast imzası defterdeki upstream imzasıyla hesaplanır:** upstream ÖNCEKİ koşuda derlenmişse (Stop sonrası,
   satırdan Build) Fast "değişti" der, bağımlı aday değildir ve koşulsuz derlenir — Bölüm 2 Task 2 Adım 1
   (`other_reasons_are_not_candidates`, son InlineData satırı; güvenli yön pinlenir).
5. **Bağımlılığın yüzeyi okunamıyor (kilitli dosya), diskte yok ya da kayıtta yok:** kapı Build der — Bölüm 2
   Task 3 Adım 1 (`an_unreadable_or_absent_surface_builds`, `a_dependency_missing_from_the_record_builds`).

## Dosya haritası

**Bölüm 1 — Create:** yok.
**Bölüm 1 — Modify:** `Core/Planning/CycleMemberNeed.cs` (kural v silinir, doc), `Supervisor/RunCoordinator.cs`
(`staleAtEnd` hoist, finally raporlama, `ReportCycleMember` imzası), `App/ViewModels/RunViewModel.cs`
(`_heldCycleMembers`, `OnProjectStarted`, `OnCycleMemberHeld`, `OnCycleCompleted`, `RecomputeWillBuildSurface`,
`ClearPreviewSets`), `ARCHITECTURE.md` (§5.3, §7.5, §8.4, §8.8, §13.2, §14.3, §22), `README.md` (cycle bölümü).
**Bölüm 1 — Tests:** `Planning/CycleMemberNeedTests.cs`, `Supervisor/CycleRoundsTests.cs`,
`App/RunViewModelStateTests.cs`, `App/RunViewModelTests.cs`.

**Bölüm 2 — Create:** `Core/Planning/SurfaceGate.cs`, `tests/…/Planning/SurfaceGateTests.cs`,
`tests/…/Supervisor/SurfaceGateRunTests.cs`.
**Bölüm 2 — Modify:** `Contracts/Model/ProjectModels.cs` (`BuildState.DependencySurfaces`),
`Core/Planning/NextPreview.cs` (`AfterUpToDateSkip`), `Core/Incremental/IncrementalPlanner.cs` (üye terimi
kendi terimi), `Core/Planning/CycleMemberNeed.cs` (kural i-b), `Supervisor/Program.cs` (Fast bağlama, adaylar),
`Supervisor/RunCoordinator.cs` (`IncrementalPlan.SurfaceCandidateIds`, yüzey önbelleği, `TrySkipWhileDependency
SurfacesUnchanged`, `RefreshBuildStateOnSkip`, `PersistBuildStateOnSuccess`'e `DependencySurfaces`, grup başı
üreticiler), `App/ViewModels/RunViewModel.cs` (`OnProjectSkipped`),
`ARCHITECTURE.md` (§7.3, §7.5, §8.1, §8.3 yeni alt başlık, §8.8, §13.2, §20, §22), `README.md`.
**Bölüm 2 — Tests:** `State/BuildStateStoreTests.cs`, `Planning/NextPreviewTests.cs`,
`Incremental/IncrementalPlannerTests.cs`, `Planning/CycleMemberNeedTests.cs`, `Supervisor/CycleRoundsTests.cs`
(`ResolveAsync` internal + mod), `Supervisor/RunCoordinatorTests.cs` (`AllSucceed`), `Supervisor/ConditionalRebuildRunTests.cs`
(özel `AllSucceed` silinir), `App/RunViewModelStateTests.cs`.

**Fixture notları (uygulayan için):**
- `CycleMemberNeedTests`: `Intact` (ledger kipi sağlam çıktı), `Reads` (B'nin iki dosyası), `Member(term, surfaces,
  output)`, `Disk(params CycleReadSurface[])`, `Decide(disk, params (id, evidence))`, `AssertNeeded`, `AssertCarried`.
- `CycleRoundsTests` (`using static RunCoordinatorTests`): `RoundRecorder` (`rec.Invoker((name, round) => …)`,
  `rec.Calls` → `"A#1"`), `TwoMemberCycle()`, `TwoMembers(sig, termA, termB)`, `WithCheck(plan, name, check)`,
  `IntactOutput`, `ChainPlan(sig, termN)` (X→M, N→X, M↔R, M,R→N), `SurfaceDisk` (`Set(name, api)`,
  `PathOf(name)`), `ResolveAsync(store, disk, plan, invoker)` (Cycles modu, tek işçi), `ConvergeOnceAsync`,
  `ConvergedTwoMemberCycleAsync(cacheRoot)`, `InCacheRootAsync`, `HashModePlan(plan, names)`, `Ok()`, `Exit(1)`,
  `Id(name)`, `h.DecisionLog`, `h.Events`.
- `RunViewModelStateTests`: `CycleTopology()` (D sıradan, A↔B↔C cycle), `StartCycleGroup(vm)`, sabitler `A,B,C,D`,
  `NeverTickingBatcher()`, `new RunViewModel(engine, batcher, () => "r1")`, `BuildPreviewItem(id, name, willBuild,
  Reason:, Conditional:)`.
- `ConsoleModesTests.Row(state, skipReason, willBuild, willBuildReason, …)`; `ConsoleEmptyState.ForEmptyLog(row)`.
- `BuildStateStoreTests.Cycle_fields_round_trip_and_an_old_record_reads_null` eski-JSON deseni.
- `ConditionalRebuildRunTests`: `SeededStore`, `ChainPlan`, `RunAsync(h, Start(RunMode.Build))`, `AllSucceed()`.

---

# BÖLÜM 1 — Cycle güveni (`feat/cycle-trust`)

### Task 1: Kural (v) daralır — VS'nin derlediği üye içerik ve yüzeyle taşınır (D2)

**Files:**
- Modify: `src/BuildOrchestrator.Core/Planning/CycleMemberNeed.cs` (sınıf doc 15-20, const 42-43, kural 135-146)
- Test: `tests/BuildOrchestrator.Tests/Planning/CycleMemberNeedTests.cs` (~433-441, ~485-497),
  `tests/BuildOrchestrator.Tests/Supervisor/CycleRoundsTests.cs` (~2632-2649 K2/K3 testi)

**Interfaces:**
- Consumes: `CycleMemberNeed.Decide`, `OutputCheck` (değişmez).
- Produces: `CycleMemberNeed.OutputBuiltOutsideReason` → `OutputOlderThanInputsReason = "output older than its inputs"`;
  zaman kipindeki üye yalnız `TimeVerdict.OwnNewer` ile gerekli olur, aksi hâlde kalan kurallara (iv, ii) düşer.

- [ ] **Step 1: Branch aç ve panoyu kur**

```powershell
git switch develop; git pull --ff-only; git switch -c feat/cycle-trust
& 'C:\Users\Delta\.claude\scripts\status.ps1' -Event steps -Sid <session_id> -Steps 'B1-T1 kural v daralir [orta]|in_progress','B1-T2 yakinsamayan grup guven [orta]|pending','B1-T3 ilerleme uye basina [orta]|pending','B1-T4 docs + olcum [orta]|pending'
```

- [ ] **Step 2: Birim testlerini yeniden yaz**

`CycleMemberNeedTests.an_output_built_outside_this_tool_makes_the_member_needed` yerine üç test:

```csharp
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — D2]</b> Eski iddia (<c>an_output_built_outside_this_tool_makes_the_member_needed</c>): zaman
    /// kipindeki (bu araç dışında derlenmiş) çıktı, hükmü ne olursa olsun üyeyi "output built outside this tool" ile
    /// gerekli yapardı — çıktının kimin olduğu bilinmediği için.
    /// <para><b>Değişme gerekçesi (ölçüm, kullanıcının 2026-10-08 ~14:10 koşusu):</b> VS'de derlenen 17 üyeli UI
    /// grubunda 16 üye yalnız bu kuralla derlendi (6 dk). Çıktı kendi girdilerinden YENİYSE (Fresh ya da yalnız bir
    /// bağımlılık yüzünden DependencyNewer) kaynaktan sonra üretilmiştir; üyenin kendi terimi (içerik + cfg + grup dışı
    /// upstream) ve kayıttaki okuma yüzeylerinin diskle karşılaştırması "aynı girdilerden derlendi" kanıtını tamamlar —
    /// <c>ApiSurfaceHash</c> gövde/MVID saymadığı için VS'nin aynı kaynaktan ürettiği kardeş aynı özeti verir, API'si
    /// değişen kardeşi kural (ii) yakalar. Kural (v) yalnız KENDİ girdisi çıktıdan yeni olan üyeye daraldı (aşağıdaki
    /// test): o çıktının mevcut kaynaktan üretildiği bilinemez.</para>
    /// </summary>
    [Fact]
    public void an_output_built_outside_this_tool_is_carried_when_term_and_read_surfaces_are_unchanged()
    {
        var output = Intact with { Mode = EvidenceMode.Time, Time = TimeVerdict.Fresh };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, output)));

        AssertCarried(decision, "A");
    }

    /// <summary>(v) Kendi girdisi çıktıdan yeni: VS derlemesinden sonra düzenleme, ya da branch değişimi (git dosyaları yeniden
    /// yazar — içerik defterdeki terime döner, diskteki DLL ise başka branch'in gövdesidir). Terim bunu göremez; zaman hükmü
    /// görür. Üye gerekli.</summary>
    [Fact]
    public void an_output_older_than_its_own_inputs_makes_the_member_needed()
    {
        var output = Intact with { Mode = EvidenceMode.Time, Time = TimeVerdict.OwnNewer };

        var decision = Decide(Disk(Reads), ("A", Member("t1", Reads, output)));

        AssertNeeded(decision, "A", "output older than its inputs");
    }

    [Fact] // zaman kipinde de okunan yüzey oynadıysa üye gerekli — VS paralel derlemesinde eski API'ye bağlanan okuyucu
    public void an_output_built_outside_this_tool_is_needed_when_a_read_surface_moved()
    {
        var output = Intact with { Mode = EvidenceMode.Time, Time = TimeVerdict.DependencyNewer };
        var disk = Disk(Read("B", B1, "h1-moved"), Read("B", B2, "h2"));

        var decision = Decide(disk, ("A", Member("t1", Reads, output)));

        AssertNeeded(decision, "A", "read surface moved: " + B1);
    }
```

`the_first_matching_rule_names_the_reason` içindeki iki satır (`"output built outside this tool"` bekleyenler; oradaki
`outside` değişkeni `Time/OwnNewer`'dır) şöyle değişir:

```csharp
        // terim aynı; çıktı kendi girdisinden eski ve yüzey yanlış ⇒ çıktı; beslenen kopya da bozuksa bu neden önde kalır
        Assert.Equal("output older than its inputs", ReasonOf(Member("t1", Reads, outside)));
        Assert.Equal("output older than its inputs", ReasonOf(Member("t1", Reads, outside with { FedIntact = false })));
```

- [ ] **Step 3: Koordinatör testini yeniden yaz**

`CycleRoundsTests.a_member_whose_output_is_in_time_mode_is_compiled` yerine (bu, `OutputBuiltOutsideReason`'ın test
projesindeki TEK referansıdır — Step 5'te sabit yeniden adlandırılınca derleme kırılmasın diye ÖNCE yazılır):

```csharp
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — D2]</b> Eski iddia (<c>a_member_whose_output_is_in_time_mode_is_compiled</c>, K2/K3):
    /// zaman kipindeki üye tur 1'de derlenir, kardeşi taşınır. Değişme gerekçesi <see cref="CycleMemberNeed"/>'in
    /// testinde (ölçüm: VS'de derlenen UI grubunda 16/17 üye yalnız bu kuralla derleniyordu). Çıktısı kendi girdilerinden
    /// yeni (Fresh) üye kalan kurallarla sınanır: terim ve okuduğu yüzeyler aynıysa taşınır, grup derlemeden yakınsar.
    /// </summary>
    [Fact]
    public Task a_member_whose_output_is_in_time_mode_is_carried_when_its_term_and_surfaces_are_unchanged() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk,
            WithCheck(TwoMembers("sig1", "a1", "b1"), "B", IntactOutput with { Mode = EvidenceMode.Time, Time = TimeVerdict.Fresh }),
            rec.Invoker((_, _) => Ok()));

        Assert.Empty(rec.Calls);
        Assert.Equal(2, h.Events.OfType<ProjectSkippedEvent>().Count(e => e.Reason == SkipReasons.UpToDate));
        var completed = Assert.Single(h.Events.OfType<CycleCompletedEvent>());
        Assert.Equal((CycleOutcome.Converged, 0), (completed.Outcome, completed.CompiledCount));
        Assert.Equal(BuildResult.Succeeded, store.Load()[Id("B")].LastResult);
    });

    [Fact] // (v) daraltılmış hâli: kendi girdisi çıktıdan yeni üye yine derlenir, kardeşi taşınır
    public Task a_member_whose_output_is_older_than_its_inputs_is_compiled() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk,
            WithCheck(TwoMembers("sig1", "a1", "b1"), "B", IntactOutput with { Mode = EvidenceMode.Time, Time = TimeVerdict.OwnNewer }),
            rec.Invoker((_, _) => Ok()));

        Assert.Equal(["B#1"], rec.Calls);
        Assert.Contains(CycleDecisionLines.RoundOneNeed("B", "output older than its inputs"), h.DecisionLog, StringComparison.Ordinal);
    });

    /// <summary>[Review Focus 1] VS paralel derlemesi: B'nin çıktısı yeni (zaman kipi) ama okuduğu A'nın yüzeyi kayıttan
    /// farklı — B eski API'ye bağlanmış olabilir ⇒ tur 1 B'yi derler (kural ii), zaman kipi onu kurtarmaz.</summary>
    [Fact]
    public Task a_time_mode_member_whose_read_surface_moved_is_compiled() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        disk.Set("A", "a2"); // kardeşin API'si koşular arasında değişti (VS derledi)
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk,
            WithCheck(TwoMembers("sig1", "a1", "b1"), "B", IntactOutput with { Mode = EvidenceMode.Time, Time = TimeVerdict.Fresh }),
            rec.Invoker((_, _) => Ok()));

        Assert.Contains("B#1", rec.Calls);
        Assert.Contains(CycleDecisionLines.RoundOneNeed("B", CycleMemberNeed.ReadSurfaceMovedPrefix + SurfaceDisk.PathOf("A")),
            h.DecisionLog, StringComparison.Ordinal);
    });
```

(Mevcut `a_recorded_surface_that_differs_from_the_disk_at_group_start_compiles_the_reader` testi ile ÇAKIŞMAZ: o
ledger kipindedir, bu zaman kipini pinler.)

- [ ] **Step 4: Kırmızıyı gör**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "FullyQualifiedName~CycleMemberNeedTests|FullyQualifiedName~CycleRoundsTests"`
Expected: FAIL — `an_output_built_outside_this_tool_is_carried…` ("A" ToBuild'de), `an_output_older_than_its_own_inputs…`
ve `the_first_matching_rule…` (neden `"output built outside this tool"`), `a_member_whose_output_is_in_time_mode_is_carried…`
(B derlenir), `a_member_whose_output_is_older…` (neden metni eski), `a_time_mode_member_whose_read_surface_moved…` (neden
metni eski). Derleme hatası OLMAMALI (yeni testler sabit yerine literal kullanır).

- [ ] **Step 5: Kuralı daralt**

`CycleMemberNeed.cs`: `OutputBuiltOutsideReason` → `OutputOlderThanInputsReason = "output older than its inputs"`
(doc: "Zaman kipinde üyenin KENDİ girdisi çıktıdan yeni (karar 2 v): çıktı mevcut kaynaktan üretilmemiş olabilir —
VS derlemesinden sonra düzenleme, branch değişimi."); satır 140-142'deki blok:

```csharp
            // (v) Zaman kipinde kendi girdisi çıktıdan yeni: çıktının mevcut kaynaktan üretildiği bilinemez. Kip tek başına
            // neden DEĞİLDİR — Fresh/DependencyNewer çıktı kaynaktan sonra üretilmiştir ve kalan kurallarla sınanır.
            if (output.Mode == EvidenceMode.Time && output.Time == TimeVerdict.OwnNewer)
            { Need(OutputOlderThanInputsReason); continue; }
```

Sınıf doc'undaki kural sırası cümlesi: "… → çıktı kanıtı eksik (iv) → çıktı kendi girdisinden eski (v) → beslenen kopyası
bozuk (iv) → okuma kaydının bütünlüğü (iii) → kayıtlı okuduğu bir kardeş yüzeyi artık farklı (ii). Çıktının KİPİ karara
girmez: bu araç dışında derlenmiş ama girdilerinden yeni bir çıktı, üyenin terimi aynı ve okuduğu yüzeyler diskle aynıysa
aynı girdilerden üretilmiştir." Kural (iv)'ün yorumu (135-136) "Zaman kipinde kanıt dosyası yoksa 'araç dışında
derlendi' yanlış olurdu" → "… 'kendi girdisinden eski' yanlış olurdu; bu yüzden kanıt eksikliği önce gelir".

- [ ] **Step 6: Yeşili gör**

Run: Step 4 filtresi + `FullyQualifiedName~CycleDecisionLogTests`. Expected: PASS (tümü). Grep: `OutputBuiltOutsideReason`
ve `"output built outside this tool"` artık `src/` ve `tests/` altında yok (ARCHITECTURE/README metni Task 4'te).

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "fix(core): tur 1 zaman kipindeki uyeyi kip yuzunden degil yalniz kendi girdisi ciktidan yeniyse gerekli sayar (D2)"
```

---

### Task 2: Yakınsamayan grup oturmuş başarıları korur (D3, D3-a, D3-b)

**Files:**
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (`provenHopeless` yanına `staleAtEnd` ~1690; round
  loop ~1950-1965; finally ~2004-2037; `ReportCycleMember` ~2256-2270; `CycleMemberState` doc),
  `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (`OnCycleCompleted` ~2293-2300)
- Test: `tests/…/Supervisor/CycleRoundsTests.cs` (yeni testler + `non_converged_and_stopped_groups_write_no_cycle_fields`
  ~2724-2790 yeniden yazılır), `tests/…/App/RunViewModelTests.cs` (~1469-1489)

**Interfaces:**
- Consumes: Task 1 (zaman kipi artık neden değil).
- Produces: `ReportCycleMember(run, id, result, totalDurationMs, failReason, depIssues, bool successIsTrusted, bool
  failureIsEvidence, bool cycleUnsettled, CycleMemberRecord? cycle)`.

- [ ] **Step 1: Ana senaryo testi (kırmızı)** — `CycleRoundsTests`'e, bölüm 15'in sonuna:

```csharp
    /// <summary>
    /// [D3] Kullanıcı senaryosu: 16 üye yeşil, tek üyede copy-lock ⇒ NoProgress. Eski kural her üyeyi geçersizliyordu ve
    /// bir sonraki Build 17'sini derliyordu. Yeni kural: yüzeyleri oturmuş (son turun bayat kümesinde olmayan) yeşil ya
    /// da taşınan üye GÜVENİLİR persist edilir; yalnız patlayan üye kanıtlı hata alır; takip koşusu yalnız onu derler.
    /// </summary>
    [Fact]
    public Task a_hopeless_member_does_not_poison_its_settled_siblings() => InCacheRootAsync(async cacheRoot =>
    {
        var (store, disk) = await ConvergedTwoMemberCycleAsync(cacheRoot);
        var bBefore = store.Load()[Id("B")];
        var rec = new RoundRecorder();
        // Yalnız A değişik; A patlar (girdileri oturmuş ⇒ tur 1'de NoProgress). B taşınır ve oturmuştur.
        using (var h = await ResolveAsync(store, disk, TwoMembers("sig2", "a2", "b1"), rec.Invoker((name, _) => name == "A" ? Exit(1) : Ok())))
        {
            Assert.Equal(["A#1"], rec.Calls);
            Assert.Equal(CycleOutcome.NoProgress, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
            var failed = Assert.Single(h.Events.OfType<ProjectFailedEvent>());
            Assert.Equal((Id("A"), true), (failed.ProjectId, failed.Evidence));
            var skipped = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
            Assert.Equal((Id("B"), SkipReasons.UpToDate), (skipped.ProjectId, skipped.Reason));
        }
        var ledger = store.Load();
        Assert.Equal((BuildResult.Failed, "sig2"), (ledger[Id("A")].LastResult, ledger[Id("A")].FailedSignature));
        Assert.Equal((BuildResult.Succeeded, "sig2"), (ledger[Id("B")].LastResult, ledger[Id("B")].BuiltSignature));
        Assert.Equal(bBefore.CycleReadSurfaces, ledger[Id("B")].CycleReadSurfaces); // taşınan üyenin kanıtı aynen

        // Takip koşusu (kaynak değişmedi): yalnız A derlenir, B yine taşınır.
        var follow = new RoundRecorder();
        using var next = await ResolveAsync(store, disk, TwoMembers("sig2", "a2", "b1"), follow.Invoker((_, _) => Ok()));
        Assert.Equal(["A#1"], follow.Calls);
        Assert.Contains(CycleDecisionLines.RoundOneNeed("A", CycleMemberNeed.NoTrustedRecordReason), next.DecisionLog, StringComparison.Ordinal);
        Assert.DoesNotContain(CycleDecisionLines.RoundOneNeed("B", ""), next.DecisionLog, StringComparison.Ordinal);
    });
```

- [ ] **Step 2: Kırmızıyı gör**

Run: `dotnet test … --filter "FullyQualifiedName~a_hopeless_member_does_not_poison"`
Expected: FAIL — B `Failed` ("sig1" kalır), `ProjectSkippedEvent` yok (B `ProjectSucceededEvent Trusted=false` alır).

- [ ] **Step 3: Koordinatörü değiştir**

`RunCoordinator.cs`, `BuildCycleGroupAsync`:

(a) `HashSet<string>? provenHopeless = null;` satırının altına:

```csharp
        // [D3] Son turun bayat kümesi: yakınsamayan grupta hangi başarının arkasında durulabileceğini söyler. Yüzey
        // kanıtı yokken null kalır ve hiçbir yeşil güvenilmez (bugünkü kural).
        HashSet<string>? staleAtEnd = null;
```

(b) round loop'ta `decision = CycleRoundPolicy.Decide(round, failed, previousFailed, staleNow);` satırının hemen
altına: `staleAtEnd = staleNow;`

(c) finally'deki üye döngüsü:

```csharp
            foreach (string id in members)
            {
                var member = state[id];
                // [D3] Oturmuş üye: grup gerçek bir hükme vardı ve üyenin okuduğu hiçbir kardeş yüzeyi son tur sonunda
                // bayat değildi — nihai API'lere bağlandı, sonucu güvenilir. Converged'de herkes oturmuştur.
                bool settled = decision == CycleRoundDecision.Converged
                    || (decision != CycleRoundDecision.Continue && staleAtEnd is not null && !staleAtEnd.Contains(id));
                try
                {
                    if (member.Carried && settled)
                        ReportCarriedCycleMember(run, id, member.DepIssues);
                    else
                        ReportCycleMember(run, id, member.Result, member.DurationMs, member.FailReason, member.DepIssues,
                            successIsTrusted: settled,
                            failureIsEvidence: provenHopeless?.Contains(id) == true,
                            // Tavana dayanmış grubun BAYAT yeşil üyesi: çıktı bir kuşak geride olabilir.
                            cycleUnsettled: decision == CycleRoundDecision.CapReached
                                && member.Result == BuildResult.Succeeded && !settled,
                            // Döngü kanıtı güvenilir her başarıya yazılır (oturmuş üye dahil); hashMode düşmüşse yüzeyler null.
                            cycle: settled ? CycleRecordOf(run, id, hashMode ? member.ReadStates : null) : null);
                }
                catch (Exception ex) { reportFailure ??= ex; }
            }
```

(d) `ReportCycleMember`:

```csharp
    private void ReportCycleMember(RunContext run, string projectId, BuildResult result, long totalDurationMs,
                                   string? failReason, DepIssueResult depIssues, bool successIsTrusted,
                                   bool failureIsEvidence, bool cycleUnsettled, CycleMemberRecord? cycle) =>
        ReportProjectResult(run, projectId, result, totalDurationMs, failReason, depIssues,
            // [D3] Güven sonuca göre AYRIŞIR: başarı yalnız oturmuşsa (Converged, ya da yakınsamayan grupta son turda
            // bayat olmayan üye), hata yalnız kanıtlı-umutsuzsa. İkisi tek ifadeden okunsaydı bayat yüzeyle patlayan
            // üye kanıtlı sayılır ya da oturmuş yeşil üye geçersizlenirdi.
            trustedResult: result == BuildResult.Succeeded ? successIsTrusted : failureIsEvidence,
            cycleUnsettled, failLogTail: null, cycle: cycle);
```

Doc'ları güncelle: `ReportProjectResult`'ın `trustedResult` paragrafı ("SCC'de grup YAKINSADIYSA…") → "SCC'de üye
OTURMUŞSA (grup yakınsadı, ya da yakınsamadı ama üyenin okuduğu hiçbir yüzey son turda bayat değildi) ya da kanıtlı-
umutsuz hatadır"; `CycleMemberState.Carried` doc'u ("grup yakınsadığında hâlâ taşınıyorsa" → "grup gerçek bir hükme
vardığında oturmuş ve hâlâ taşınıyorsa"); `UpdateCycleNonConvergenceMemory` doc'undaki "Üye raporlamasından SONRA:
yukarıdaki döngü zaten her üyeyi invalidate etmiştir" cümlesi → "oturmamış üyeleri invalidate etmiştir; oturmuş üyenin
taze kaydı hafızayı da taşır (yalnız raporlar)".

- [ ] **Step 4: Yeşili gör**

Run: Step 2 filtresi. Expected: PASS.

- [ ] **Step 5: Eski teoriyi yeniden yaz (kırmızı → yeşil)**

`non_converged_and_stopped_groups_write_no_cycle_fields` yerine (doc'a eski iddia + gerekçe):

```csharp
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — D3]</b> Eski iddia (<c>non_converged_and_stopped_groups_write_no_cycle_fields</c>, karar 4):
    /// yakınsamayan (NoProgress, CapReached) ya da kesilen koşu HİÇ KİMSEYİ persist etmez — taşınan üye dahil her üye
    /// geçersizlenir, takip koşusu herkesi derler.
    /// <para><b>Değişme gerekçesi (kullanıcı senaryosu, 2026-10-08):</b> 17 üyeli UI grubunda tek üyenin copy-lock'u
    /// NoProgress verip 16 yeşil kaydı geçersizledi; bir sonraki Build 17 üyeyi yeniden derledi (~6 dk). Yüzey kanıtı
    /// varken oturmuş (son turda bayat olmayan) yeşil ya da taşınan üyenin çıktısı nihai API'lere bağlıdır ve kaydı
    /// doğrudur; yalnız bayat üye geçersizlenir, böylece grup kirli kalır ve takip koşusu yalnız onu derler. Kesilen
    /// (Stop) koşu bugünkü gibi hiçbir şey persist etmez.</para>
    /// Senaryo (ChainPlan X→M, N→X, M↔R, M,R→N; yalnız N değişik, tur 1 yalnız N'yi derler, X/M/R taşınır):
    /// · no progress: N oturmuşken patlar (tur 1) — X, M, R taşınmış ve oturmuş ⇒ üçü de sig2 ile yenilenir, N kanıtlı hata.
    /// · stopped: hiçbir şey yenilenmez (herkes Failed, sig1).
    /// · cap reached: tur 1 N (yüzeyi oynar ⇒ M ve R bayat); tur 2 M (R'nin eski yüzeyini okur, patlar) ve R (yüzeyi oynar ⇒
    ///   M bayat); tur 3 M (yüzeyi oynar ⇒ X ve R bayat) ⇒ tavan. Son turda X ve R bayattır ⇒ geçersizlenir (sig1, Failed);
    ///   N ve M oturmuş ⇒ güvenilir (sig2, Succeeded); takip koşusu X ve R'yi derler.
    /// </summary>
    [Theory]
    [InlineData("no progress")]
    [InlineData("stopped")]
    [InlineData("cap reached")]
    public Task a_group_without_a_verdict_keeps_only_its_settled_members(string outcome) => InCacheRootAsync(async cacheRoot =>
    {
        string[] names = ["X", "N", "M", "R"];
        var store = new BuildStateStore(cacheRoot);
        var disk = new SurfaceDisk();
        foreach (string name in names) disk.Set(name, name.ToLowerInvariant() + "1");
        await ConvergeOnceAsync(store, disk, ChainPlan("sig1", "n1"));
        using var cts = new CancellationTokenSource();
        var rec = new RoundRecorder();
        var invoker = rec.Invoker((name, round, ct) =>
        {
            if (outcome == "stopped") { cts.Cancel(); ct.ThrowIfCancellationRequested(); }
            if (outcome == "no progress" || (name == "M" && round == 1)) return Task.FromResult(Exit(1));
            disk.Set(name, name.ToLowerInvariant() + "2");
            return Task.FromResult(Ok());
        });
        using (var h = await ResolveAsync(store, disk, ChainPlan("sig2", "n2"), invoker, ct: cts.Token))
        {
            if (outcome == "no progress")
            {
                Assert.Equal(["N#1"], rec.Calls);
                Assert.Equal(3, h.Events.OfType<ProjectSkippedEvent>().Count(e => e.Reason == SkipReasons.UpToDate));
                Assert.True(Assert.Single(h.Events.OfType<ProjectFailedEvent>()).Evidence);
            }
            if (outcome == "cap reached")
            {
                // RoundRecorder üye BAŞINA sayar: M'nin ilk derlemesi grup turu 2'de, ikincisi turu 3'tedir.
                Assert.Equal(["N#1", "M#1", "R#1", "M#2"], rec.Calls);
                var succeeded = h.Events.OfType<ProjectSucceededEvent>().ToDictionary(e => NameOf(e.ProjectId));
                Assert.True(succeeded["N"].Trusted); Assert.False(succeeded["N"].CycleUnsettled);
                Assert.True(succeeded["M"].Trusted);
                Assert.False(succeeded["R"].Trusted); Assert.True(succeeded["R"].CycleUnsettled);
                Assert.False(succeeded["X"].Trusted);
            }
        }
        var ledger = store.Load();
        var expected = outcome switch
        {
            "no progress" => new[] { ("X", "sig2", BuildResult.Succeeded), ("N", "sig1", BuildResult.Failed), ("M", "sig2", BuildResult.Succeeded), ("R", "sig2", BuildResult.Succeeded) },
            "cap reached" => new[] { ("X", "sig1", BuildResult.Failed), ("N", "sig2", BuildResult.Succeeded), ("M", "sig2", BuildResult.Succeeded), ("R", "sig1", BuildResult.Failed) },
            _ => names.Select(n => (n, "sig1", BuildResult.Failed)).ToArray(),
        };
        foreach (var (name, sig, result) in expected)
            Assert.Equal((sig, result), (ledger[Id(name)].BuiltSignature, ledger[Id(name)].LastResult));
        if (outcome == "no progress") Assert.Equal("sig2", ledger[Id("N")].FailedSignature);
        if (outcome == "cap reached") Assert.Equal("m1", ledger[Id("M")].CycleMemberTerm); // derlenen oturmuş üyenin kanıtı yazıldı

        var follow = new RoundRecorder();
        using var next = await ResolveAsync(store, disk, ChainPlan("sig2", "n2"), follow.Invoker((_, _) => Ok()));
        string[] expectedFollow = outcome switch
        {
            "no progress" => ["N#1"],
            "cap reached" => ["R#1", "X#1"],
            _ => ["M#1", "N#1", "R#1", "X#1"],
        };
        Assert.Equal(expectedFollow, follow.Calls.Order(StringComparer.Ordinal));
    });
```

> Uygulayan not: "cap reached" dalındaki `rec.Calls` ve bayat kümesi iddiaları ChainPlan'ın gerçek seviye
> sırasından türetilmiştir (M önce girer, R'nin eski yüzeyini okur; M'nin yüzeyi tur 2 ve 3'te oynar). Koşup
> gözlemlenen diziyle uyuşmuyorsa önce decision.log'daki `stale=` alanını oku; iddiayı KODA göre değil, kuralın
> tanımına göre düzelt ("son turun bayat kümesinde olmayan yeşil üye güvenilir") ve ledger'a `Ruling:` yaz.

Run: `--filter "FullyQualifiedName~a_group_without_a_verdict_keeps_only_its_settled_members"` → önce üç dal kırmızı
olmalıydı (Step 3 uygulandıysa yeşil; o zaman Step 3'ü geçici `git stash` ile kaldırıp kırmızıyı gör, geri al).
Expected: PASS (3/3).

- [ ] **Step 5b: Eski kuralı pinleyen iki hash-mode testini yeniden yaz (kırmızı → yeşil)**

`CycleRoundsTests.a_failure_whose_inputs_are_settled_is_no_progress_after_one_round` (~1417): senaryo aynı kalır (A yeşil,
B oturmuşken patlar, NoProgress tek turda); doc'una eklenir: "**[DEĞİŞEN KURAL — D3]** Eski iddia: yakınsamayan grup persist
ETMEZ — A `Failed`/`old` kalır, olay `Trusted=false`. Değişme gerekçesi: A'nın okuduğu yüzeyler son turda oturmuştu;
kaydı doğrudur ve tek kardeşinin hatası onu zehirlemez (kullanıcı senaryosu 2026-10-08: tek copy-lock 16 kaydı
düşürüyordu)." Assert'ler:

```csharp
            // [D3] Oturmuş yeşil üye güvenilir persist edilir; hafıza yine yazılır (yalnız raporlar) — iki alan bağımsız.
            Assert.Equal(BuildResult.Succeeded, store.Load()[Id("A")].LastResult);
            Assert.Equal("sig", store.Load()[Id("A")].BuiltSignature);
            Assert.Equal("sig", store.Load()[Id("A")].NonConvergentSignature);
            Assert.Equal("sig", store.Load()[Id("B")].NonConvergentSignature);
            var succeeded = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
            Assert.True(succeeded.Trusted);
```

`a_hopeless_members_failure_is_evidence_and_its_green_sibling_stays_unevidenced` (~1808) →
`a_hopeless_members_failure_is_evidence_and_its_settled_green_sibling_is_trusted`; doc'undaki son cümle ("Suçsuz yeşil eş
AYNEN eskisi gibi kanıtsız invalidate edilir: gri never built") → "**[DEĞİŞEN KURAL — D3]** Eski iddia: suçsuz yeşil eş
kanıtsız invalidate edilirdi (gri never built, tek Resolve ile geri gelirdi). Değişme gerekçesi: eşin okuduğu yüzeyler
oturmuştu — kaydı doğrudur, yeşil kalır; bir sonraki koşu yalnız suçluyu derler." Suçlu assert'leri aynen; eş için:

```csharp
            // Suçsuz eş: yüzeyleri oturmuş yeşil üye — güvenilir başarı, taze kayıt, üç döngü alanı yazılı.
            var green = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
            Assert.Equal(Id("A"), green.ProjectId);
            Assert.True(green.Trusted);
            var sibling = store.Load()[Id("A")];
            Assert.Equal((BuildResult.Succeeded, "sig"), (sibling.LastResult, sibling.BuiltSignature));
            Assert.Null(sibling.FailedSignature);
            Assert.NotNull(sibling.CycleReadSurfaces);
```

Run: `--filter "FullyQualifiedName~a_failure_whose_inputs_are_settled|FullyQualifiedName~a_hopeless_members_failure"` → Step 3
uygulandıysa PASS; uygulanmadan önce FAIL olduğunu Step 2'nin kırmızısı zaten gösterdi (aynı kural).

- [ ] **Step 6: Tavan testinin doc'u** — `a_capped_group_is_invoked_again_by_the_next_Build_at_the_same_signature`
davranışı DEĞİŞMEZ (kanıtsız plan → `staleAtEnd == null` → kimse güvenilmez); doc'una tek cümle: "Kanıtsız grupta
(Incremental.OutputsById yok) D3 devreye girmez: hiçbir yeşil üye güvenilmez, grup bütünüyle yeniden derlenir."
`a_capped_group…` ile aynı dosyadaki ~900-922 bloğundaki `Assert.All(succeeded, e => Assert.False(e.Trusted))` da
aynı sebeple geçerli kalır — doc'a aynı cümle.

- [ ] **Step 7: Review Focus 2 — hashMode düşmüş NoProgress** (kırmızı yok; davranış pinlenir):

```csharp
    /// <summary>[D3 · Review Focus 3] Kanıt koşu ortasında düşerse (derlenen üyenin yüzeyi okunamadı) son turun bayat kümesi
    /// yoktur ⇒ yakınsamayan grupta hiçbir yeşil güvenilmez — bugünkü kural aynen.</summary>
    [Fact]
    public Task without_surface_evidence_a_non_converged_group_trusts_no_success() => InCacheRootAsync(async cacheRoot =>
    {
        var store = new BuildStateStore(cacheRoot);
        SeedGreen(store, "A"); SeedGreen(store, "B");
        var plan = TwoMemberCycle() with { Incremental = RunCoordinatorTests.Incremental("A", "B") }; // kanıt yok
        var rec = new RoundRecorder();
        using var h = new Harness(plan, rec.Invoker((name, _) => name == "B" ? Exit(1) : Ok()), stateStore: store);
        await h.Sut.StartAsync(Start(RunMode.Build), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(CycleOutcome.NoProgress, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
        Assert.False(Assert.Single(h.Events.OfType<ProjectSucceededEvent>()).Trusted);
        Assert.Equal((BuildResult.Failed, "old"), (store.Load()[Id("A")].LastResult, store.Load()[Id("A")].BuiltSignature));
    });
```

Run: PASS beklenir (davranış değişmedi); FAIL ederse Step 3'ün `staleAtEnd is not null` kapısı eksiktir.

- [ ] **Step 8: App — NoProgress işareti yalnız güvenilmeyen üyeye (kırmızı)**

`RunViewModelTests.A_cycle_that_ends_without_progress_marks_all_its_members_as_unconverged` yeniden yazılır:

```csharp
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — D3-b]</b> Eski iddia (<c>…marks_all_its_members_as_unconverged</c>): NoProgress GRUBU sıkıştırır,
    /// yeşil biten üyenin çıktısı da bayattır, herkes işaretlenir. Değişme gerekçesi: motor artık oturmuş yeşil üyeyi
    /// güvenilir persist eder (<c>Trusted=true</c>, satır <c>UpToDate</c>); onu "stuck" saymak yanlış olurdu. İşaret yalnız
    /// motorun arkasında durmadığı üyelere gider: patlayan ve güvenilmeyen (WillBuild != false) satırlar.
    /// </summary>
    [Fact]
    public async Task A_cycle_that_ends_without_progress_marks_only_its_untrusted_members_as_unconverged()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        const string a = @"C:\p\a.csproj", b = @"C:\p\b.csproj", c = @"C:\p\c.csproj", d = @"C:\p\d.csproj";
        vm.OnEvent(new WorkspaceTopologyEvent(
            [new ProjectNode(a, "A", a, [], [], 0, null, null, true, null),
             new ProjectNode(b, "B", b, [], [], 0, null, null, true, null),
             new ProjectNode(c, "C", c, [], [], 0, null, null, true, null),
             new ProjectNode(d, "D", d, [], [], 0, null, null, true, null)],
            [[a, b, c, d]], [], []));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 4, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(a, "A", true), new BuildPreviewItem(b, "B", true),
            new BuildPreviewItem(c, "C", true), new BuildPreviewItem(d, "D", true)]));

        vm.OnEvent(new ProjectStartedEvent("r1", a, "A"));
        vm.OnEvent(new ProjectSucceededEvent("r1", a, 100, null, false, Trusted: true));   // oturmuş
        vm.OnEvent(new ProjectStartedEvent("r1", b, "B"));
        vm.OnEvent(new ProjectFailedEvent("r1", b, 100, "exit 1", null, Evidence: true));
        vm.OnEvent(new ProjectStartedEvent("r1", c, "C"));
        vm.OnEvent(new ProjectSucceededEvent("r1", c, 100, null, false, Trusted: false));  // bayat
        vm.OnEvent(new ProjectSkippedEvent("r1", d, SkipReasons.UpToDate));                 // oturmuş, taşınan
        vm.OnEvent(new CycleCompletedEvent("r1", a, CycleOutcome.NoProgress, 4, 1, 1, 300));

        Assert.False(vm.Projects.Single(p => p.Id == a).CycleUnconverged);
        Assert.True(vm.Projects.Single(p => p.Id == b).CycleUnconverged);
        Assert.True(vm.Projects.Single(p => p.Id == c).CycleUnconverged);
        Assert.False(vm.Projects.Single(p => p.Id == d).CycleUnconverged);
        Assert.Equal(2, vm.Counters.StuckCycles);
    }
```

Run: FAIL (A ve D işaretli). `OnCycleCompleted`:

```csharp
    private void OnCycleCompleted(CycleCompletedEvent e)
    {
        if (e.Outcome != CycleOutcome.NoProgress) return;
        // [D3-b] Yalnız motorun arkasında durmadığı üyeler sıkışmıştır: oturmuş yeşil üye güvenilir persist edildi ve
        // satırı az önce UpToDate yazıldı (OnProjectDone → NextPreview.AfterSuccess); oturmuş taşınan üye "skipped — up
        // to date" aldı (Bölüm 1'de plan bayrağına dokunulmaz, bu yüzden State kapısı AYRICA gerekir); bayat ya da
        // patlayan üye kirli kalır.
        foreach (string member in _cycleGroups?.MembersOf(e.ProjectId) ?? [e.ProjectId])
            if (FindRow(member) is { State: not ProjectRowState.Skipped, WillBuild: not false } row)
                row.CycleUnconverged = true;
        RefreshRunSurface();
    }
```

Run: PASS. `ProjectSucceededEvent`/`ProjectFailedEvent` kurucu parametre adları `IpcMessages.cs:340-356`'dan
doğrulanır (Trusted/Evidence named argümanları).

- [ ] **Step 9: Sınıf ve süit**

Run: `--filter "FullyQualifiedName~CycleRoundsTests|FullyQualifiedName~RunViewModelTests|FullyQualifiedName~CycleDecisionLogTests|FullyQualifiedName~ConditionalRebuildRunTests"`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add -A && git commit -m "fix(supervisor): yakinsamayan grupta oturmus basarilar guvenilir persist edilir, yalniz bayat uye gecersizlenir; App stuck isaretini yalniz guvenilmeyen uyeye koyar (D3)"
```

---

### Task 3: İlerleme üye başına (D4)

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (`_willBuildIds` ~491 yanına alan;
  `ClearPreviewSets` ~505; `RecomputeWillBuildSurface` ~1844; `OnProjectStarted` ~2218; `OnCycleMemberHeld` ~2238)
- Test: `tests/…/App/RunViewModelStateTests.cs` (cycle sayaç bölümü ~1650-1810)

**Interfaces:** `FinishedOfWillBuild` anlamı: "KESİN kümeden bitmiş ya da turdaki derlemesini bitirip grubunu bekleyen".

- [ ] **Step 1: Test (kırmızı)**

```csharp
    /// <summary>
    /// [D4] Şeridin n/m'si ve çubuk grup boyunca KIPIRDAMIYORDU: ara tur sonucu yayılmadığı için üye grubun hükmüne kadar
    /// terminal olmaz, n yalnız terminal satırı sayardı (17 üyeli grupta 4-6 dk hareketsiz). Artık turdaki derlemesi
    /// biten üye (<c>CycleMemberHeldEvent</c>) sayılır; sonraki turda yeniden derlenmeye başlayan üye sayımdan düşer
    /// (çubuk yeniden derlenen üyeler kadar geri adım atar — dürüst); hüküm gelince terminal sayım devralır.
    /// Sayım yalnız kesin kümedeki (<c>_willBuildIds</c>) üyeler için: n asla m'yi aşmaz.
    /// </summary>
    [Fact]
    public async Task A_cycle_member_counts_as_finished_once_held_and_steps_back_when_a_later_round_recompiles_it()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartCycleGroup(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, TotalProjects: 4, Parallelism: 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([
            new BuildPreviewItem(A, "A", true), new BuildPreviewItem(B, "B", true),
            new BuildPreviewItem(C, "C", true), new BuildPreviewItem(D, "D", false)]));
        Assert.Equal((3, 0), (vm.WillBuildCount, vm.FinishedOfWillBuild));

        vm.OnEvent(new CycleRoundStartedEvent("r1", A, 1, 3, 3));
        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new CycleMemberHeldEvent("r1", A));
        Assert.Equal(1, vm.FinishedOfWillBuild);                      // tur 1: A bitti, grubunu bekliyor
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));
        vm.OnEvent(new CycleMemberHeldEvent("r1", B));
        vm.OnEvent(new ProjectStartedEvent("r1", C, "C"));
        vm.OnEvent(new CycleMemberHeldEvent("r1", C));
        Assert.Equal(3, vm.FinishedOfWillBuild);

        vm.OnEvent(new CycleRoundStartedEvent("r1", A, 2, 3, 1));
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));            // tur 2: yalnız B yeniden derlenir
        Assert.Equal(2, vm.FinishedOfWillBuild);                      // geri adım
        vm.OnEvent(new CycleMemberHeldEvent("r1", B));
        Assert.Equal(3, vm.FinishedOfWillBuild);

        vm.OnEvent(new ProjectSucceededEvent("r1", A, 10)); vm.OnEvent(new ProjectSucceededEvent("r1", B, 10));
        vm.OnEvent(new ProjectSucceededEvent("r1", C, 10));
        vm.OnEvent(new CycleCompletedEvent("r1", A, CycleOutcome.Converged, 3, 2, 0, 30));
        Assert.Equal((3, 3), (vm.WillBuildCount, vm.FinishedOfWillBuild)); // çift sayım yok
        Assert.Equal(100.0, RibbonText.Progress(vm.Phase, vm.AllClean, vm.Counters, vm.WillBuildCount, vm.FinishedOfWillBuild, vm.Counters.Total));
    }

    [Fact] // held üye yalnız kesin kümedeyse sayılır; ikinci koşu temiz başlar
    public async Task A_held_member_outside_the_fixed_set_is_not_counted_and_the_count_resets_per_run()
    {
        await using var engine = new EngineHost(TestPaths.SupervisorExe);
        var vm = new RunViewModel(engine, NeverTickingBatcher(), () => "r1");
        StartCycleGroup(vm);
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 4, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "A", true), new BuildPreviewItem(B, "B", false),
            new BuildPreviewItem(C, "C", false), new BuildPreviewItem(D, "D", false)]));
        vm.OnEvent(new ProjectStartedEvent("r1", B, "B"));
        vm.OnEvent(new CycleMemberHeldEvent("r1", B));
        Assert.Equal((1, 0), (vm.WillBuildCount, vm.FinishedOfWillBuild));
        vm.OnEvent(new ProjectStartedEvent("r1", A, "A"));
        vm.OnEvent(new CycleMemberHeldEvent("r1", A));
        Assert.Equal(1, vm.FinishedOfWillBuild);

        vm.OnEvent(new RunCompletedEvent("r1", RunOutcome.Stopped, 0, 0, 0, 2, 100));
        vm.OnEvent(new RunStartedEvent("r2", RunMode.Build, 4, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([new BuildPreviewItem(A, "A", true), new BuildPreviewItem(B, "B", true),
            new BuildPreviewItem(C, "C", true), new BuildPreviewItem(D, "D", false)]));
        Assert.Equal((3, 0), (vm.WillBuildCount, vm.FinishedOfWillBuild));
    }
```

`RunCompletedEvent`'in kurucu parametre sırası `IpcMessages.cs`'ten doğrulanır (mevcut testlerdeki
`new RunCompletedEvent("r1", RunOutcome.Completed, Succeeded: 1, Failed: 0, Skipped: 0, Queued: 0, DurationMs: 1200)`
kalıbı kullanılır).

- [ ] **Step 2: Kırmızıyı gör** — `--filter "FullyQualifiedName~A_cycle_member_counts_as_finished|FullyQualifiedName~A_held_member_outside"`. Expected: FAIL (`FinishedOfWillBuild` 0 kalır).

- [ ] **Step 3: Uygula**

```csharp
    // [D4] Turdaki derlemesi bitip grubunu bekleyen KESİN kümedeki üyeler — ara tur sonucu yayılmadığı için satır
    // terminal olmaz, ama kullanıcı için o üyenin işi bu tur için bitmiştir: n/m ve çubuk onu sayar. Sonraki turda
    // yeniden derlenmeye başlayan üye düşer (çubuk yeniden derlenen üyeler kadar geri adım atar); hüküm gelince
    // terminal sayım devralır (bir satır ya terminal ya held sayılır — çift sayım yok). Koşu başına sıfırlanır.
    private readonly HashSet<string> _heldCycleMembers = new(StringComparer.OrdinalIgnoreCase);
```

`ClearPreviewSets` → `_heldCycleMembers.Clear();` eklenir. `RecomputeWillBuildSurface`:

```csharp
        foreach (var row in Projects)
            if (_willBuildIds.Contains(row.Id) &&
                (row.State is ProjectRowState.Succeeded or ProjectRowState.Failed or ProjectRowState.Skipped
                 || _heldCycleMembers.Contains(row.Id)))
                fin++;
```

`OnProjectStarted`: `row.CycleWaiting = false;` satırının altına `_heldCycleMembers.Remove(e.ProjectId); // [D4] tur ≥2`.
`OnCycleMemberHeld`: `row.CycleWaiting = true;` altına
`if (_willBuildIds.Contains(e.ProjectId)) _heldCycleMembers.Add(e.ProjectId); // [D4]`.

- [ ] **Step 4: Yeşil + sınıf** — `--filter "FullyQualifiedName~RunViewModelStateTests|FullyQualifiedName~StickyRibbonTests|FullyQualifiedName~RibbonTextTests"`. Expected: PASS. (`StickyRibbonTests:424` held→started byte-eşit sayaç testi `RunCounters`'a dokunulmadığı için geçer.)

- [ ] **Step 5: Commit** — `git commit -am "feat(app): ilerleme sayaci turdaki derlemesi biten cycle uyesini sayar, sonraki turda yeniden derlenen uye geri duser (D4)"`

---

### Task 4: Dokümanlar, tam süit, ölçüm, merge

**Files:** `ARCHITECTURE.md`, `README.md`, `tests/…/Supervisor/CycleRoundsTests.cs` (sınıf doc ölçüm paragrafı),
`.claude/outputs/<ts>-cycle-trust-measurement.md`.

- [ ] **Step 1: ARCHITECTURE.md yerinde yeniden yaz** (anlatı, "eskiden" yok):
  - §8.8 "Round one compiles only the members that need it" listesi: "Output evidence missing, output built outside
    this tool" maddesi → "**Output evidence missing, output older than its inputs.** The member has no output check,
    no derivable evidence path or an output file that is gone, or fed copies that are not intact (§7.6); or its output
    is in time mode and older than one of its own inputs — an output that may not have been compiled from the sources
    on disk (an edit after a Visual Studio build, a branch switch that rewrote the files). The mode alone plays no
    part: an output Visual Studio compiled after the last edit, read against the same sibling surfaces, is the same
    answer, and is carried." Ardından "A member that matches none of the rules is carried" paragrafına bir cümle:
    "…its record is refreshed like any carried member's even when the output was compiled outside this tool; the
    next run judges it by its term and surfaces, not by the output's time."
  - §8.8 "**A group that did not converge persists no success.**" paragrafı → "**A group that did not converge keeps
    only its settled members.** On no progress and on the ceiling, a member that came back green — or was carried —
    with none of its read surfaces stale at the end of the last round compiled against final APIs, and its record is
    written exactly as a converged member's, cycle fields included; a green member that was stale at the end is
    invalidated, which keeps the group dirty so that the next run's round one compiles that member alone. Without
    surface evidence nothing is trusted. A settled green member carries no dependency note for a failed sibling: a
    sibling that later compiles with a changed surface is caught by the read-surface rule, and one whose surface
    did not change leaves the member's output correct. A stop, a cancellation and an unexpected exception invalidate
    everyone and report every member as failed…" (kanıtlı hata cümleleri aynen kalır).
  - §8.8 kanıt kapısı paragrafı (~1588-1590) `trusted` cümlesi: "A green member of a cycle group that did not converge
    arrives with `trusted: false` only when its read surfaces were still stale at the end; a settled one is trusted."
    §5.3 (~533) `cycleUnsettled` tanımı: "marks a green member of a group that ran out of rounds while its read
    surfaces were still stale".
  - §7.5 (~958-962): "They are written only when the member's group converges" → "They are written when the member's
    result is trusted — its group converged, or it was settled when the group stopped without a verdict (§8.8)";
    "…whose success is not trusted, which includes every member of a group that did not converge" → "…which includes
    the members of a group that did not converge whose read surfaces were still stale".
  - §8.8 (~1666-1667) "When a group does not converge or is cut short, a carried member is handled like every other
    member of it (below): invalidated…" → "When a group is cut short a carried member is invalidated with the rest;
    when it stops without converging, a carried member whose recorded surfaces were still final is refreshed exactly
    as on convergence, and one whose surfaces had moved is invalidated (below)."
  - §13.2 (~3073-3077) "A member whose group did not converge is different: the engine does not stand behind its
    green round…" → "A member whose group did not converge and whose read surfaces were still stale at the end is
    different: …"; §14.3 (~5014-5015) "a cycle member whose group did not converge and whose success the engine
    therefore does not keep" → "…did not converge while its read surfaces were still stale, so the engine does not
    keep its success".
  - §8.4 son paragraf / §13.2 şerit: "`n/m` counts a member of a group in rounds from the moment its compile in the
    round ends (`cycleMemberHeld`); a member a later round recompiles leaves the count until it is held again, so the
    bar steps back by the members that round recompiles and never claims more than the group has finished."
    §8.8 "Publishing per round would send progress backwards" cümlesi korunur, yanına "the held announcement is what
    moves the ribbon's count instead" eklenir.
  - §14.3 (~5139) "The group ran out of rounds and this member is green" satırı → "…and this member is green but was
    still stale in the last round". §13.2 "stuck" sayımı: "members the engine did not trust".
  - §22 code map satırları (6298, 6307): "when the group converges" → "when the member's result is trusted".
- [ ] **Step 2: README.md** (411-414, 446-452, 476-492): "except a member of a group that did not settle, which stays
  grey (to build) whatever its last round said" → "except a member of a group that did not settle whose output was
  still stale in the last round, which stays grey; a member whose surfaces had settled keeps its green and its
  record"; "a cycle member compiled outside this tool is carried like any other when its inputs and the surfaces it
  read are unchanged, unless its sources are newer than that output"; şerit cümlesine "the count advances as each
  member's compile in the round ends".
- [ ] **Step 3: Tam süit** — `dotnet test … --filter "Category!=Acceptance" > .claude/temp/suite-part1.txt 2>&1`;
  sonu oku. Expected: 0 failed. Düşen zamanlama testi varsa tek başına tekrar; token/motion guard'ları dahil yeşil.
- [ ] **Step 4: Ölçüm (gerçek OSYS, uygulama kapalı, Release ile derlenmiş App)** — kullanıcı senaryosu: (a) VS'de
  `UI.Service.WorkOrder` gövde değişikliği + VS build, sonra araçta Build: decision.log'da `round 1 — own inputs
  changed` sayısı ve `carried` sayısı, grup süresi; (b) aynı grupta tek üyeyi copy-lock'la patlat (Container açıkken),
  NoProgress sonrası ikinci Build'de kaç üye derlendi. Sonuçlar `.claude/outputs/<ts>-cycle-trust-measurement.md`'ye
  (zaman damgası PowerShell `Get-Date -Format 'yyyy-MM-dd-HH-mm'`) ve `CycleRoundsTests` sınıf doc'una bir cümle.
- [ ] **Step 5: Commit, merge, push**

```bash
git commit -am "docs: cycle guveni — tur 1 kip kurali, yakinsamayan grupta oturmus uyeler, uye basina ilerleme"
git switch develop && git merge --no-ff feat/cycle-trust -m "merge: cycle guveni — VS ciktisi tasinir, yakinsamayan grup oturmus uyeleri korur, ilerleme uye basina (feat/cycle-trust)" && git push origin develop
git branch -d feat/cycle-trust && git push origin --delete feat/cycle-trust   # remote'a push edildiyse
```

CI yeşil kontrolü (curl ile `api.github.com/repos/sdemir60/app_build_orchestrator/actions/runs?branch=develop`).

---

# BÖLÜM 2 — Yüzey kapısı (`feat/surface-gate`)

### Task 1: `BuildState.DependencySurfaces` ve `NextPreview.AfterUpToDateSkip` (D6, D8)

**Files:**
- Modify: `src/BuildOrchestrator.Contracts/Model/ProjectModels.cs` (`BuildState` kayıt ~136-213, `Equals` ~219,
  `GetHashCode` ~245), `src/BuildOrchestrator.Core/Planning/NextPreview.cs`
- Test: `tests/…/State/BuildStateStoreTests.cs`, `tests/…/Planning/NextPreviewTests.cs`

**Interfaces — Produces:**
- `BuildState(…, IReadOnlyList<CycleReadSurface>? DependencySurfaces = null)` (son parametre).
- `NextPreview.AfterUpToDateSkip() => (false, WillBuildReason.UpToDate, false)`.

- [ ] **Step 1: Store testi (kırmızı)** — `Cycle_fields_round_trip_and_an_old_record_reads_null` deseniyle:

```csharp
    /// <summary>[D6] Sıradan projenin son güvenilir derlemesinde bağlandığı doğrudan bağımlılık yüzeyleri — yüzey kapısının
    /// (<c>SurfaceGate</c>) karşılaştırma tabanı. Alan SONA ve default'lu: eski kayıt null okur (kapı o projeyi derler —
    /// güvenli yön). Eşitlik sıraya duyarlı, kanonik sıra yazan tarafın (DepIssueRoots/CycleReadSurfaces deseni).</summary>
    [Fact]
    public void Dependency_surfaces_round_trip_and_an_old_record_reads_null()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(StatePath,
            """{"C:\\r\\Old.csproj":{"ProjectId":"C:\\r\\Old.csproj","BuiltSignature":"s","BuiltCommit":null,"LastResult":0,"LastRunAt":null,"LastBranch":null,"LastDurationMs":null,"NonConvergentSignature":null,"BuiltContent":null,"DepIssue":false,"DepIssueRoots":null,"FailedSignature":null,"FailedAt":null,"FedOutputs":null,"PackagesConfigHash":null,"CycleMemberTerm":null,"CycleReadSurfaces":null,"CycleEngineFingerprint":null}}""");
        var store = new BuildStateStore(_root);
        Assert.Null(Assert.Contains(@"C:\r\Old.csproj", store.Load()).DependencySurfaces);

        var surface = new CycleReadSurface(@"C:\r\U.csproj", @"C:\r\U\bin\Debug\U.dll", "SURF1");
        var fresh = new BuildState(@"C:\r\New.csproj", "s", LastResult: BuildResult.Succeeded, DependencySurfaces: [surface]);
        store.Upsert(fresh);

        Assert.Contains("\"DependencySurfaces\":[{\"Producer\":", File.ReadAllText(StatePath));
        var back = Assert.Contains(@"C:\r\New.csproj", store.Load());
        Assert.Equal(fresh, back);
        Assert.Equal(fresh.GetHashCode(), back.GetHashCode());
        Assert.NotEqual(fresh, fresh with { DependencySurfaces = [surface with { Hash = "SURF2" }] });
        Assert.NotEqual(fresh, fresh with { DependencySurfaces = null });
    }
```

- [ ] **Step 2: Kırmızı** (derlenmez: parametre yok) → **Uygula:** `BuildState`'e son parametre:

```csharp
    // [D6 — yüzey kapısı] Bu projenin son GÜVENİLİR derlemesinde bağlandığı DOĞRUDAN bağımlılık yüzeyleri: üretici id,
    // üreticinin kanıt dosyası ve o anki API yüzeyi özeti (ApiSurfaceHash). Sıradan projede her doğrudan bağımlılık,
    // döngü üyesinde yalnız GRUP DIŞI bağımlılıklar (grup içi CycleReadSurfaces'tadır). Yüzeyi okunamayan ya da dosyası
    // olmayan bağımlılık listeye GİRMEZ — kapı o projeyi derler (güvenli yön). Kanonik sıra ve eşitlik CycleReadSurfaces
    // ile aynı. Alan SONA ve default'lu: eski kayıtlar null çözülür.
    IReadOnlyList<CycleReadSurface>? DependencySurfaces = null)
```

`Equals`'a `&& (DependencySurfaces is null ? other.DependencySurfaces is null : other.DependencySurfaces is not null && DependencySurfaces.SequenceEqual(other.DependencySurfaces))`;
`GetHashCode`'a `foreach (var surface in DependencySurfaces ?? []) hash.Add(surface);`. `CycleReadSurface` doc'una
"<see cref="BuildState.DependencySurfaces"/>'ın da öğesidir" eklenir. Run: PASS.

- [ ] **Step 3: NextPreview testi (kırmızı → yeşil)**

```csharp
    /// <summary>[D8] Koşu sürerken yüzey kapısıyla ("up to date") atlanan satır: defter az önce yeni imzayla yenilendi, bir
    /// sonraki Sync UpToDate der — satır o cevabı HEMEN verir (gri "affected"ta kalmaz). Koşullu değildir.</summary>
    [Fact]
    public void a_project_skipped_as_up_to_date_during_a_run_is_up_to_date()
        => Assert.Equal((false, WillBuildReason.UpToDate, false), NextPreview.AfterUpToDateSkip());
```

```csharp
    /// <summary>[D8] Proje BU KOŞUDA sırası gelince <c>up to date</c> ile atlandı (yüzey kapısı — hiçbir doğrudan bağımlılığının
    /// API yüzeyi değişmemiş): defteri yeni bileşik imzayla yenilendi, bir sonraki Sync <see cref="WillBuildReason.UpToDate"/>
    /// der. Pre-skip'ler buraya gelmez (satır zaten WillBuild=false); kök bekleyen atlama (DependencyStillFailing) defterine
    /// dokunmaz ve buraya gelmez.</summary>
    public static (bool WillBuild, WillBuildReason Reason, bool Conditional) AfterUpToDateSkip() =>
        (false, WillBuildReason.UpToDate, false);
```

- [ ] **Step 4: Commit** — `git commit -am "feat(contracts,core): BuildState.DependencySurfaces ve NextPreview.AfterUpToDateSkip (D6, D8)"`

---

### Task 2: Adaylar — Fast bağlama ve `IncrementalPlan.SurfaceCandidateIds` (D5)

**Files:**
- Create: `src/BuildOrchestrator.Core/Planning/SurfaceGate.cs` (bu task'ta yalnız `AppliesTo` + `CandidateIds`;
  `Decide` Task 3'te), `tests/…/Planning/SurfaceGateTests.cs`
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (`IncrementalPlan` kaydı ~62-70),
  `src/BuildOrchestrator.Supervisor/Program.cs` (`ComputeIncremental` ~203-214)

**Interfaces — Produces:**
- `SurfaceGate.AppliesTo(RunMode mode, bool scopedRun) => IncrementalModes.Includes(mode) && !scopedRun`
- `SurfaceGate.CandidateIds(BuildPlan safe, BuildPlan fast) : IReadOnlySet<string>`
- `IncrementalPlan(…, IReadOnlySet<string>? SurfaceCandidateIds = null)` (son parametre).

- [ ] **Step 1: Birim testi (kırmızı)**

```csharp
namespace BuildOrchestrator.Tests.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using Xunit;

/// <summary>[D5/D7] Yüzey kapısı: "bağımlılığının API yüzeyi değişmediyse derleme". Aday seçimi iki plandan okunur —
/// Safe (bu koşunun kararı) ve Fast (frozen-upstream: upstream'lerin DEFTERDEKİ imzasıyla hesaplanan, cascade'siz
/// karar). Fast "güncel" diyorsa projenin kendi terimi (içerik + configuration) değişmemiştir ve kanıtı sağlamdır;
/// Safe "imza değişti" diyorsa kirlilik yalnız bir upstream'dendir. Böyle proje sırası gelince kapıdan geçer.</summary>
public class SurfaceGateTests
{
    private static ProjectNode Node(string id, bool? willBuild, WillBuildReason? reason, bool inCycle = false, params string[] deps) =>
        new(id, id, id, [], deps, 0, null, null, inCycle, willBuild) with { WillBuildReason = reason };

    private static BuildPlan Plan(params ProjectNode[] nodes) => new(nodes, Cycles: [], Configuration: "Debug");

    [Theory]
    [InlineData(RunMode.Build, false, true)]
    [InlineData(RunMode.Cycles, false, true)]
    [InlineData(RunMode.Rebuild, false, false)]
    [InlineData(RunMode.Clean, false, false)]
    [InlineData(RunMode.Build, true, false)]
    public void the_gate_applies_to_full_runs_that_follow_the_ledger(RunMode mode, bool scoped, bool expected)
        => Assert.Equal(expected, SurfaceGate.AppliesTo(mode, scoped));

    [Fact]
    public void a_project_dirty_only_through_an_upstream_is_a_candidate()
    {
        var safe = Plan(Node("U", true, WillBuildReason.SignatureChanged), Node("D", true, WillBuildReason.SignatureChanged, deps: ["U"]));
        var fast = Plan(Node("U", true, WillBuildReason.SignatureChanged), Node("D", false, WillBuildReason.UpToDate, deps: ["U"]));

        Assert.Equal(["D"], SurfaceGate.CandidateIds(safe, fast).Order());
    }

    [Theory]
    [InlineData(WillBuildReason.SignatureChanged, false, WillBuildReason.BuiltOutside)] // kanıt zaman kipinde: Fast UpToDate demedi
    [InlineData(WillBuildReason.NeverBuilt, true, WillBuildReason.NeverBuilt)]           // kendi sebebiyle kirli
    [InlineData(WillBuildReason.WaitingForDependency, true, WillBuildReason.WaitingForDependency)] // kök bekleyen: ConditionalRebuild'in işi
    // Fast de "değişti" dedi: configuration ya da kendi içeriği değişti — YA DA upstream daha önceki bir koşuda
    // derlendi ve defterdeki imzası yeni (Review Focus 4: bağımlı bir kez koşulsuz derlenir, güvenli yön).
    [InlineData(WillBuildReason.SignatureChanged, true, WillBuildReason.SignatureChanged)]
    public void other_reasons_are_not_candidates(WillBuildReason safeReason, bool? fastWillBuild, WillBuildReason fastReason)
    {
        var safe = Plan(Node("U", true, WillBuildReason.SignatureChanged), Node("D", true, safeReason, deps: ["U"]));
        var fast = Plan(Node("U", true, WillBuildReason.SignatureChanged), Node("D", fastWillBuild, fastReason, deps: ["U"]));

        Assert.Empty(SurfaceGate.CandidateIds(safe, fast));
    }

    [Fact] // doğrudan bağımlılığı olmayan proje ve döngü üyesi aday değildir (üyenin yolu CycleMemberNeed'dir)
    public void roots_and_cycle_members_are_never_candidates()
    {
        var safe = Plan(Node("R", true, WillBuildReason.SignatureChanged),
                        Node("M", true, WillBuildReason.SignatureChanged, inCycle: true, deps: ["R"]));
        var fast = Plan(Node("R", false, WillBuildReason.UpToDate),
                        Node("M", false, WillBuildReason.UpToDate, inCycle: true, deps: ["R"]));

        Assert.Empty(SurfaceGate.CandidateIds(safe, fast));
    }
}
```

`ProjectNode` kurucu sırası `Contracts/Model/ProjectModels.cs`'ten doğrulanır (RunViewModelStateTests'teki
`Node` yardımcısı: `new(id, name, id, ["Osys"], deps, buildOrder, layerIndex, layerName, inCycle, willBuild)`).

- [ ] **Step 2: Uygula** — `SurfaceGate.cs`:

```csharp
namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [D5/D7] Yüzey kapısı — "bir bağımlılığın API yüzeyi değişmediyse bağımlısını derleme". Döngü içinde aynı soruyu
/// <see cref="CycleMemberNeed"/> sorar; bu sınıf sıradan projeler içindir. SAF: I/O, process, saat YOK; yüzeyleri
/// çağıran okur. Koordinatör yalnız uygular (<c>RunCoordinator.TrySkipWhileDependencySurfacesUnchanged</c>).
///
/// <para><b>Aday.</b> Safe plan projeyi "imza değişti" ile kirli görür, Fast plan (frozen-upstream: upstream'lerin
/// defterdeki imzası, cascade yok — <c>IncrementalPlanner</c>) ise "güncel" der: kendi terimi (içerik + configuration)
/// değişmemiş, kanıtı yerinde, beslenen kopyası sağlam, son sonucu başarı; kirlilik yalnız bir upstream'den gelir.
/// Configuration değişimi Fast imzasını da değiştirdiği için aday olmaz. Doğrudan bağımlılığı olmayan proje ve döngü
/// üyesi aday değildir. <b>Bilinen sınır:</b> upstream daha önceki bir koşuda derlenmişse (Stop sonrası, satırdan Build)
/// Fast imzası defterdeki yeni upstream imzasını görür ve "değişti" der — bağımlı bir kez koşulsuz derlenir (güvenli yön).</para>
///
/// <para><b>Karar (sırası gelince).</b> Her doğrudan bağımlılık için: bu koşuda patladıysa, kayıtta yüzeyi yoksa,
/// şimdiki yüzeyi okunamıyorsa ya da kayıttakinden farklıysa DERLENİR; hepsi aynıysa atlanır. Atlanan ya da güncel
/// bağımlılığın yüzeyi de DİSKTEN okunur — "atlandı ⇒ değişmedi" varsayımı yoktur: bağımlılık satırdan derlenmiş ya da
/// kesilmiş bir koşuda yenilenmiş olabilir.</para>
/// </summary>
public static class SurfaceGate
{
    /// <summary>Kapı hangi koşuda uygulanır: defteri dinleyen (<see cref="IncrementalModes"/>) TAM koşular. Satırdan tetiklenen
    /// koşunun hedefi koşulsuz derlenir, Rebuild her şeyi derler.</summary>
    public static bool AppliesTo(RunMode mode, bool scopedRun) => IncrementalModes.Includes(mode) && !scopedRun;

    /// <summary>Kapıdan geçecek projeler (bkz. sınıf özeti "Aday"). <paramref name="fast"/> düğümleri <paramref name="safe"/>
    /// ile aynı id kümesidir (aynı plan, iki bağlama).</summary>
    public static IReadOnlySet<string> CandidateIds(BuildPlan safe, BuildPlan fast)
    {
        ArgumentNullException.ThrowIfNull(safe);
        ArgumentNullException.ThrowIfNull(fast);
        var fastById = fast.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        return safe.Nodes
            .Where(n => n is { WillBuild: true, WillBuildReason: WillBuildReason.SignatureChanged, InCycle: false }
                        && n.Dependencies.Count > 0
                        && fastById.GetValueOrDefault(n.Id) is { WillBuild: false, WillBuildReason: WillBuildReason.UpToDate })
            .Select(n => n.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
```

Run: `--filter "FullyQualifiedName~SurfaceGateTests"` → PASS.

- [ ] **Step 3: Supervisor planına taşı** — `IncrementalPlan`'a son parametre `IReadOnlySet<string>? SurfaceCandidateIds = null`
(doc: "[D5] kapıdan geçecek projeler; null ⇒ kapı yok"). `Program.ComputeIncremental`:

```csharp
            var checks = binder.ChecksFor(state);
            var (bound, signatures, memberTerms) = binder.Bind(state, CycleCompilation.CompilesCycles(cmd.Mode), cmd.DependentMode, checks);
            // [D5] Yüzey kapısının adayları: Safe (bu koşunun kararı) ile Fast'in (frozen-upstream, kanıtlı) karşılaştırması —
            // Sync'in iki geçişiyle aynı binder, parmak izleri önbellekten (ikinci bağlama diske inmez). Yalnız defteri
            // dinleyen tam koşuda (SurfaceGate.AppliesTo); karar Core'da.
            IReadOnlySet<string>? candidates = null;
            if (SurfaceGate.AppliesTo(cmd.Mode, scopedRun: cmd.ScopeProjectId is not null) && cmd.DependentMode == DependentMode.Safe)
            {
                var (fast, _) = binder.Bind(state, CycleCompilation.CompilesCycles(cmd.Mode), DependentMode.Fast, checks);
                candidates = SurfaceGate.CandidateIds(bound, fast);
            }
            hashes.Flush();
            return (bound, new IncrementalPlan(signatures, head, branch, externalCommits, binder.ContentById,
                binder.OutputsById, checks, memberTerms, candidates));
```

`StartRunCommand.ScopeProjectId` alan adını `IpcMessages.cs:145`'ten doğrula. `cmd.DependentMode == Fast` ise
(kullanıcı ayarı) kapı yok — Fast koşuda bağımlı zaten "güncel" sayılıp atlanır.

- [ ] **Step 4: Koordinatör testi — adaylar önizlemede koşullu DEĞİL (D8) (kırmızı yok, pin)**
Yeni dosya `tests/BuildOrchestrator.Tests/Supervisor/SurfaceGateRunTests.cs` (fixture deseni
`ConditionalRebuildRunTests`; Task 3 bu sınıfa beş test daha ekler):

```csharp
namespace BuildOrchestrator.Tests.Supervisor;

using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

/// <summary>[D7] Yüzey kapısının koşu içi davranışı. Fixture: U → D (D aday). Yüzeyler <see cref="CycleRoundsTests.SurfaceDisk"/>'ten
/// (apiSurface seam), kanıt yolları <see cref="CycleRoundsTests.HashModePlan"/> ile — döngü testleriyle AYNI sahte disk (kopya YASAK).</summary>
public class SurfaceGateRunTests : IDisposable
{
    private readonly string _cacheRoot = NewCacheRoot();
    public void Dispose() { if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true); }

    /// <summary>U → D, ikisi de "imza değişti" ile kirli; <paramref name="candidate"/> D'yi kapı adayı yapar.</summary>
    private static RunPlan UpDown(bool candidate = true)
    {
        var plan = new RunPlan(new BuildPlan(
            [Node("U", willBuild: true) with { BuildOrder = 0, WillBuildReason = WillBuildReason.SignatureChanged },
             Node("D", deps: ["U"], willBuild: true) with { BuildOrder = 1, WillBuildReason = WillBuildReason.SignatureChanged }],
            Cycles: [], Configuration: "Debug"), EmptyRefs());
        plan = CycleRoundsTests.HashModePlan(plan, "U", "D");
        return candidate
            ? plan with { Incremental = plan.Incremental! with { SurfaceCandidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Id("D") } } }
            : plan;
    }

    /// <summary>Aynı defter ve sahte disk üzerinde bir Build koşusu — döngü testlerinin koşturucusu (<see cref="CycleRoundsTests.ResolveAsync"/>)
    /// mod parametresiyle; ikinci bir koşturucu yazılmaz.</summary>
    private static Task<Harness> BuildAsync(BuildStateStore store, CycleRoundsTests.SurfaceDisk disk, RunPlan plan,
        FakeInvoker invoker, RunMode mode = RunMode.Build) =>
        CycleRoundsTests.ResolveAsync(store, disk, plan, invoker, mode: mode);

    /// <summary>[D8] Aday satır sıradan kirli satırdır: önizlemede Conditional=false, dalga/kuyruk/payda onu sayar; karar
    /// sırası gelince verilir ve sonucu (skipped — up to date) sayımı ilerletir.</summary>
    [Fact]
    public async Task a_surface_candidate_is_previewed_as_a_plain_dirty_project()
    {
        var disk = new CycleRoundsTests.SurfaceDisk();
        using var h = await BuildAsync(new BuildStateStore(_cacheRoot), disk, UpDown(), AllSucceed());
        var d = Assert.Single(h.Events.OfType<BuildPreviewEvent>()).Items.Single(i => i.ProjectId == Id("D"));
        Assert.Equal((true, false), (d.WillBuild, d.Conditional));
    }
}
```

Fixture düzenlemesi (kopya YASAK): `RunCoordinatorTests`'e `internal static FakeInvoker AllSucceed() => new((_, _, _) =>
Task.FromResult(Ok()));` eklenir ve `ConditionalRebuildRunTests`'teki özel kopyası silinir (`using static` çözer);
`CycleRoundsTests.ResolveAsync` `internal static` olur ve sonuna `RunMode mode = RunMode.Cycles` parametresi alır
(`Start(mode, parallelism: 1)`) — mevcut çağrılar değişmez. `CycleRoundsTests.SurfaceDisk` ve `HashModePlan` zaten
`internal`.

- [ ] **Step 5: Commit** — `git commit -am "feat(core,supervisor): yuzey kapisi adaylari — Safe/Fast karsilastirmasi, IncrementalPlan.SurfaceCandidateIds (D5, D8)"`

---

### Task 3: Kapı sırası gelince — `SurfaceGate.Decide`, koordinatör uygulaması, kayıt yazımı (D7, D9)

**Files:**
- Modify: `src/BuildOrchestrator.Core/Planning/SurfaceGate.cs` (`Decide`, `UnchangedDetail`),
  `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (`RunContext` yüzey önbelleği; `BuildProjectAsync` ~1237;
  yeni `TrySkipWhileDependencySurfacesUnchanged`; `PersistBuildStateOnCarriedMember` → `RefreshBuildStateOnSkip`;
  `PersistBuildStateOnSuccess` `DependencySurfaces`)
- Test: `tests/…/Planning/SurfaceGateTests.cs`, `tests/…/Supervisor/SurfaceGateRunTests.cs`

**Interfaces — Produces:**
- `SurfaceGate.Decide(IReadOnlyList<string> directDependencies, IReadOnlyDictionary<string,BuildResult> completed, IReadOnlyList<CycleReadSurface>? recorded, Func<string,(string File, string? Hash)?> surfaceOf) : SurfaceGateVerdict { Build, Unchanged }`
- `SurfaceGate.UnchangedDetail = "no dependency surface changed"`
- `SurfaceGate.Persistable(string? hash) => hash is null or ApiSurfaceHash.Absent ? null : hash` (deftere yazım normalizasyonu, tek yer)
- `RunContext.SurfaceById : ConcurrentDictionary<string,string?>` (tembel önbellek) ve `RunCoordinator.SurfaceOf(run, id)`.
- `RefreshBuildStateOnSkip(run, id, depIssues)` (= eski `PersistBuildStateOnCarriedMember`, yeniden adlandırılır; tek gövde).

- [ ] **Step 1: Birim testleri (kırmızı)** — `SurfaceGateTests`'e:

```csharp
    private static CycleReadSurface Rec(string dep, string hash) => new(dep, dep + ".dll", hash);
    private static readonly IReadOnlyDictionary<string, BuildResult> Done = new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase)
        { ["U"] = BuildResult.Succeeded, ["S"] = BuildResult.Skipped, ["F"] = BuildResult.Failed };
    private static (string, string?)? Disk(string dep) => dep switch { "U" => ("U.dll", "u1"), "S" => ("S.dll", "s1"), "F" => ("F.dll", "f1"), "L" => ("L.dll", null), _ => null };

    [Fact] public void unchanged_when_every_direct_dependency_surface_matches_the_record()
        => Assert.Equal(SurfaceGateVerdict.Unchanged, SurfaceGate.Decide(["U", "S"], Done, [Rec("S", "s1"), Rec("U", "u1")], Disk));
    [Fact] public void a_dependency_that_failed_this_run_builds()
        => Assert.Equal(SurfaceGateVerdict.Build, SurfaceGate.Decide(["U", "F"], Done, [Rec("F", "f1"), Rec("U", "u1")], Disk));
    [Fact] public void a_moved_surface_builds()
        => Assert.Equal(SurfaceGateVerdict.Build, SurfaceGate.Decide(["U"], Done, [Rec("U", "u0")], Disk));
    [Fact] public void a_dependency_missing_from_the_record_builds()
        => Assert.Equal(SurfaceGateVerdict.Build, SurfaceGate.Decide(["U", "S"], Done, [Rec("U", "u1")], Disk));
    [Fact] public void no_record_builds()
    {
        Assert.Equal(SurfaceGateVerdict.Build, SurfaceGate.Decide(["U"], Done, null, Disk));
        Assert.Equal(SurfaceGateVerdict.Build, SurfaceGate.Decide(["U"], Done, [], Disk));
    }
    [Fact] public void an_unreadable_or_absent_surface_builds() // Review Focus 5
    {
        Assert.Equal(SurfaceGateVerdict.Build, SurfaceGate.Decide(["L"], new Dictionary<string, BuildResult> { ["L"] = BuildResult.Succeeded }, [Rec("L", "l1")], Disk));
        Assert.Equal(SurfaceGateVerdict.Build, SurfaceGate.Decide(["U"], Done, [Rec("U", ApiSurfaceHash.Absent)], _ => ("U.dll", ApiSurfaceHash.Absent)));
    }
    [Fact] public void a_dependency_not_completed_builds()
        => Assert.Equal(SurfaceGateVerdict.Build, SurfaceGate.Decide(["Z"], Done, [Rec("Z", "z1")], Disk));
    [Fact] public void persistable_drops_null_and_absent()
    {
        Assert.Null(SurfaceGate.Persistable(null)); Assert.Null(SurfaceGate.Persistable(ApiSurfaceHash.Absent)); Assert.Equal("h", SurfaceGate.Persistable("h"));
    }
```

(`Rec("F", "f1")` üstteki `Done` ile tutarlı — `Decide` hash'e bakmadan Failed'da Build der.)

- [ ] **Step 2: Uygula**

```csharp
public enum SurfaceGateVerdict { Build, Unchanged }

    /// <summary>Atlama satırının ayrıntısı: <c>D: skipped — up to date (no dependency surface changed)</c>.</summary>
    public const string UnchangedDetail = "no dependency surface changed";

    /// <summary>Deftere yazılabilir yüzey: okunamayan (null) ve olmayan (<see cref="ApiSurfaceHash.Absent"/>) dosya YAZILMAZ —
    /// "yok == yok" eşleşip çıktı yokken bağımlıyı atlatırdı. TEK normalizasyon yeri.</summary>
    public static string? Persistable(string? hash) => hash is null || hash == ApiSurfaceHash.Absent ? null : hash;

    /// <param name="surfaceOf">Bağımlılık → (kanıt dosyası, ŞİMDİKİ yüzey özeti); dosya türetilemiyorsa null, okunamıyorsa
    /// hash null. Çağıran diskten okur ve koşu boyunca önbellekler.</param>
    public static SurfaceGateVerdict Decide(IReadOnlyList<string> directDependencies,
        IReadOnlyDictionary<string, BuildResult> completed, IReadOnlyList<CycleReadSurface>? recorded,
        Func<string, (string File, string? Hash)?> surfaceOf)
    {
        ArgumentNullException.ThrowIfNull(directDependencies); ArgumentNullException.ThrowIfNull(completed); ArgumentNullException.ThrowIfNull(surfaceOf);
        if (recorded is not { Count: > 0 }) return SurfaceGateVerdict.Build;
        var recordedByDep = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in recorded)
        {
            if (r?.Producer is null || r.Hash is null) return SurfaceGateVerdict.Build; // bozuk kayıt
            if (!recordedByDep.TryAdd(r.Producer, r.Hash)) return SurfaceGateVerdict.Build;
        }
        foreach (string dep in directDependencies)
        {
            if (!completed.TryGetValue(dep, out var result) || result == BuildResult.Failed) return SurfaceGateVerdict.Build;
            if (!recordedByDep.TryGetValue(dep, out string? seen) || Persistable(seen) is null) return SurfaceGateVerdict.Build;
            if (surfaceOf(dep) is not { } now || Persistable(now.Hash) is not { } current) return SurfaceGateVerdict.Build;
            if (!string.Equals(seen, current, StringComparison.Ordinal)) return SurfaceGateVerdict.Build;
        }
        return SurfaceGateVerdict.Unchanged;
    }
```

(`using BuildOrchestrator.Core.Incremental;` eklenir.) Run: PASS.

- [ ] **Step 3: Koordinatör uçtan uca testi (kırmızı)** — Task 2'de açılan `SurfaceGateRunTests` sınıfına (aynı
`UpDown`/`BuildAsync`/`AllSucceed` yardımcıları) beş test:

```csharp
    /// <summary>Koşu 1: ikisi de derlenir, D'nin kaydı U'nun yüzeyini taşır. Koşu 2 (U yine kirli, yüzeyi AYNI): U derlenir, D sırası
    /// gelince atlanır — "skipped — up to date (no dependency surface changed)", kaydı yeni imzayla yenilenir, yüzeyler aynen.</summary>
    [Fact]
    public async Task a_dependent_is_skipped_when_its_dependency_recompiled_with_the_same_surface()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        var first = AllSucceed();
        using (var h = await BuildAsync(store, disk, UpDown(candidate: false), first))
            Assert.Equal([Id("U"), Id("D")], first.Requests.Select(r => r.ProjectId));
        var dBefore = store.Load()[Id("D")];
        Assert.Equal([new CycleReadSurface(Id("U"), CycleRoundsTests.SurfaceDisk.PathOf("U"), "u1")], dBefore.DependencySurfaces);

        var second = AllSucceed();
        var plan = UpDown() with { Incremental = UpDown().Incremental! with { SignatureById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [Id("U")] = "sig2", [Id("D")] = "sig2" } } };
        using var run = await BuildAsync(store, disk, plan, second);
        Assert.Equal([Id("U")], second.Requests.Select(r => r.ProjectId));
        var skipped = Assert.Single(run.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal((Id("D"), SkipReasons.UpToDate), (skipped.ProjectId, skipped.Reason));
        Assert.Contains($"D: skipped — {SkipReasons.UpToDate} ({SurfaceGate.UnchangedDetail})", run.DecisionLog, StringComparison.Ordinal);
        var d = store.Load()[Id("D")];
        Assert.Equal(("sig2", BuildResult.Succeeded, dBefore.LastDurationMs), (d.BuiltSignature, d.LastResult, d.LastDurationMs));
        Assert.Equal(dBefore.DependencySurfaces, d.DependencySurfaces);
        Assert.Equal(1, Assert.Single(run.Events.OfType<RunCompletedEvent>()).Skipped);
    }

    [Fact] // yüzey oynadı ⇒ D derlenir, kaydı yeni yüzeyi taşır
    public async Task a_dependent_is_built_when_its_dependency_surface_moved()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        var invoker = new FakeInvoker((req, _, _) => { if (NameOf(req.ProjectId) == "U") disk.Set("U", "u2"); return Task.FromResult(Ok()); });
        using var run = await BuildAsync(store, disk, UpDown(), invoker);
        Assert.Equal([Id("U"), Id("D")], invoker.Requests.Select(r => r.ProjectId));
        Assert.Empty(run.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal("u2", Assert.Single(store.Load()[Id("D")].DependencySurfaces!).Hash);
    }

    [Fact] // bağımlılık bu koşuda patladı ⇒ D derlenir (dep-issue yolu, bugünkü gibi)
    public async Task a_dependent_is_built_with_a_dependency_issue_when_its_dependency_failed()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        var invoker = new FakeInvoker((req, _, _) => Task.FromResult(NameOf(req.ProjectId) == "U" ? Exit(1) : Ok()));
        using var run = await BuildAsync(store, disk, UpDown(), invoker);
        Assert.Equal([Id("U"), Id("D")], invoker.Requests.Select(r => r.ProjectId));
        Assert.NotNull(Assert.Single(run.Events.OfType<ProjectSucceededEvent>()).DepIssues);
    }

    [Fact] // bağımlılık güncel diye pre-skip edildi ama satırdan derlenip yüzeyi değişmişti ⇒ diskten okunur ⇒ D derlenir
    public async Task a_skipped_dependency_surface_is_read_from_disk_not_assumed_unchanged()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        disk.Set("U", "u2"); // koşular arasında U'nun çıktısı değişti (satırdan derlendi)
        var plan = UpDown() with { Plan = UpDown().Plan with { Nodes = [.. UpDown().Plan.Nodes.Select(n => n.Name == "U" ? n with { WillBuild = false, WillBuildReason = WillBuildReason.UpToDate } : n)] } };
        var invoker = AllSucceed();
        using var run = await BuildAsync(store, disk, plan, invoker);
        Assert.Equal([Id("D")], invoker.Requests.Select(r => r.ProjectId));
    }

    [Fact] // Rebuild kapıyı uygulamaz; kapı yalnız defteri dinleyen koşularda
    public async Task a_rebuild_ignores_the_gate()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        var invoker = AllSucceed();
        using var run = await BuildAsync(store, disk, UpDown(), invoker, RunMode.Rebuild);
        Assert.Equal([Id("U"), Id("D")], invoker.Requests.Select(r => r.ProjectId));
    }
```

(Son test üretimde `Program.ComputeIncremental`'ın Rebuild'de aday üretmemesine dayanmaz — adaylar plana elle
konur; koordinatörün kendi kapısı (`SurfaceGate.AppliesTo`, Step 4-a) onları Rebuild'de yok sayar. Mod kuralı yine
TEK fonksiyondur, iki çağıran vardır.)

`FakeInvoker`'ın kurucu imzası (`(req, onLine, ct)`) ve `Requests` özelliği `RunCoordinatorTests.cs:113`'ten
doğrulanır; `BuildPlan`'ın `with { Nodes = … }` desteklediği `ProjectModels.cs`'ten doğrulanır (record ise evet).
`SurfaceDisk` ve `HashModePlan` `internal static` — `CycleRoundsTests` içinde `internal sealed class` olarak erişilebilir;
değilse erişilebilirliği `internal`'a çek (tek fixture, kopya yok).

Run: `--filter "FullyQualifiedName~SurfaceGateRunTests"` → FAIL (D her koşuda derlenir; `DependencySurfaces` null).

- [ ] **Step 4: Koordinatörü uygula**

(a) `RunContext`'e özellik ve SON (default'lu) parametre:

```csharp
        /// <summary>[D9] Bağımlılık → bu koşuda okunan ŞİMDİKİ yüzey özeti (kanıt dosyasından, ApiSurfaceHash); tembel ve koşu
        /// boyunca sabit: bağımlılık dispatch anında terminaldir, çıktısı diskte nihaidir. Yüzey yoksa/okunamıyorsa null.</summary>
        public ConcurrentDictionary<string, string?> SurfaceById { get; } = new(StringComparer.OrdinalIgnoreCase);
```

```csharp
        // [D7] Sırası gelince yüzey kapısından geçecek projeler (IncrementalPlan.SurfaceCandidateIds) — yalnız kapının
        // uygulandığı koşuda dolu (SurfaceGate.AppliesTo: defteri dinleyen tam koşu); null ⇒ kapı yok. Mode gibi SONDA ve
        // default'lu: eski kuruluş yerleri (testler) değişmez.
        IReadOnlySet<string>? SurfaceCandidateIds = null)
```

Kuruluşta (`new RunContext(…, Mode: cmd.Mode, …)`):
`SurfaceCandidateIds: SurfaceGate.AppliesTo(cmd.Mode, scopedRun: cmd.ScopeProjectId is not null) ? runPlan.Incremental?.SurfaceCandidateIds : null`.

(b) yardımcı:

```csharp
    /// <summary>[D9] Bir projenin ŞİMDİKİ çıktı yüzeyi: kanıt dosyası (IncrementalPlan.OutputsById) + yüzey özeti; koşu başına
    /// bir okuma (RunContext.SurfaceById). Kanıt yolu türetilemiyorsa null.</summary>
    private (string File, string? Hash)? SurfaceOf(RunContext run, string projectId)
    {
        if (run.Incremental?.OutputsById?.GetValueOrDefault(projectId) is not { } outputs) return null;
        return (outputs.Evidence, run.SurfaceById.GetOrAdd(projectId, _ => _apiSurface(outputs.Evidence)));
    }

    /// <summary>[D6] Bu projenin doğrudan bağımlılıklarının (döngü üyesinde grup DIŞI olanların) şimdiki yüzeyleri, defterin
    /// kanonik listesi olarak; yüzeyi olmayan/okunamayan bağımlılık listeye girmez (SurfaceGate.Persistable). HİÇ FIRLATMAZ
    /// (warn-only, boş liste): çağıranlar sonuç raporlamasının hemen yanındadır ve buradan kaçan bir istisna Complete'i atlatıp
    /// koşuyu asardı — üretimdeki ApiSurfaceHash.OfFile zaten fırlatmaz, bu kapı sahte seam'ler ve gelecekteki yazımlar içindir.</summary>
    private IReadOnlyList<CycleReadSurface> DependencySurfacesOf(RunContext run, string projectId, IReadOnlyList<string>? excludedDeps = null)
    {
        try
        {
            var list = new List<CycleReadSurface>();
            foreach (string dep in run.NodeById.GetValueOrDefault(projectId)?.Dependencies ?? [])
            {
                if (excludedDeps is not null && excludedDeps.Contains(dep, StringComparer.OrdinalIgnoreCase)) continue;
                if (SurfaceOf(run, dep) is { } s && SurfaceGate.Persistable(s.Hash) is { } hash)
                    list.Add(new CycleReadSurface(dep, s.File, hash));
            }
            return [.. list.OrderBy(s => s.Producer, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            console("warning: dependency surfaces could not be read (" + NameOf(run, projectId) + "): " + ex.Message);
            return [];
        }
    }
```

(c) `BuildProjectAsync` başı:

```csharp
        if (run.ConditionalIds.Contains(projectId) && TrySkipWhileDependencyStillFails(run, projectId)) return;
        // [D7] Yüzey kapısı: aday projenin hiçbir doğrudan bağımlılığının API yüzeyi değişmediyse derlenmez.
        if (run.SurfaceCandidateIds?.Contains(projectId) == true && TrySkipWhileDependencySurfacesUnchanged(run, projectId)) return;
```

(d) yeni metot (TrySkipWhileDependencyStillFails'in kardeşi, aynı yapı):

```csharp
    /// <summary>
    /// [D7] Yüzey kapısını UYGULAR (karar <see cref="SurfaceGate.Decide"/>'da): hiçbir doğrudan bağımlılığın yüzeyi değişmediyse
    /// proje derlenmeden <see cref="SkipReasons.UpToDate"/> + <see cref="SurfaceGate.UnchangedDetail"/> ile atlanır, defteri
    /// yenilenir (<see cref="RefreshBuildStateOnSkip"/> — taşınan döngü üyesiyle AYNI gövde) ve <c>true</c> döner; aksi hâlde
    /// <c>false</c>. Dep-issue'lar atlanan projede de hesaplanır: bağımlılarına miras kalır, nota yazılır. Beklenmedik hata ⇒
    /// derlenir. Complete <c>finally</c>'de.
    /// </summary>
    private bool TrySkipWhileDependencySurfacesUnchanged(RunContext run, string projectId)
    {
        try
        {
            var deps = run.NodeById.GetValueOrDefault(projectId)?.Dependencies ?? [];
            var recorded = run.LedgerAtStart?.GetValueOrDefault(projectId)?.DependencySurfaces;
            if (SurfaceGate.Decide(deps, run.Scheduler.Completed, recorded, d => SurfaceOf(run, d)) != SurfaceGateVerdict.Unchanged)
                return false;
        }
        catch (Exception ex)
        {
            console("warning: surface gate check failed (" + NameOf(run, projectId) + ") — building: " + ex.Message);
            return false;
        }
        try
        {
            var depIssues = ComputeDepIssues(run, projectId);
            ReportSkipped(run.Events, run.Logs, run.RunId, projectId, NameOf(run, projectId), SkipReasons.UpToDate,
                cycleUnconverged: false, detail: SurfaceGate.UnchangedDetail);
            bool interrupted;
            lock (_gate) interrupted = _interrupted;
            if (!interrupted) RefreshBuildStateOnSkip(run, projectId, depIssues);
        }
        finally
        {
            run.Scheduler.Complete(projectId, BuildResult.Skipped);
        }
        return true;
    }
```

(e) `PersistBuildStateOnCarriedMember` → `RefreshBuildStateOnSkip` olarak yeniden adlandırılır (gövde aynı; doc'a
"taşınan döngü üyesi ve yüzey kapısıyla atlanan proje aynı gövdeden geçer"); `ReportCarriedCycleMember` çağrısı
güncellenir.

(f) `PersistBuildStateOnSuccess`'e SON parametre `IReadOnlyList<CycleReadSurface>? dependencySurfaces = null` ve
kayda `DependencySurfaces: dependencySurfaces`. `ReportProjectResult`'a da SON parametre `IReadOnlyList<CycleReadSurface>?
dependencySurfaces = null` (mevcut `cycle` parametresinden sonra); `BuildProjectAsync` finally'si
`dependencySurfaces: dependencySurfaces` geçer — yerel değişken `depIssues` gibi invoke'tan ÖNCE hesaplanır
(`var dependencySurfaces = DependencySurfacesOf(run, projectId);`): bağımlılıklar o anda terminaldir ve derleme bu
yüzeylere karşı yapılır. `ReportCycleMember` bu task'ta geçmez (Task 5 doldurur).

(g) `InvalidateBuildStateOnFailure` `with` kopyaladığı için alan korunur (güvenli: başarısız kayıt güvenilmez).

Run: `SurfaceGateRunTests` → PASS. Ardından `--filter "FullyQualifiedName~CycleRoundsTests|FullyQualifiedName~ConditionalRebuildRunTests|FullyQualifiedName~RunCoordinatorTests"` → PASS.

- [ ] **Step 5: Commit** — `git commit -am "feat(supervisor): yuzey kapisi — aday proje sirasi gelince bagimliliklarinin yuzeyini diskten okur, degismediyse up to date atlanir ve kaydi yenilenir; basari DependencySurfaces yazar (D7, D9)"`

---

### Task 4: App — atlanan satır yeşile döner (D8)

**Files:**
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (`OnProjectSkipped` ~2270-2277)
- Test: `tests/…/App/RunViewModelStateTests.cs`

- [ ] **Step 1: VM testleri (kırmızı)**

```csharp
    /// <summary>[D8] Koşu sürerken "up to date" ile atlanan satır (yüzey kapısı, taşınan döngü üyesi): motor defteri yeni imzayla
    /// yeniledi, bir sonraki Sync UpToDate diyecek — satır o cevabı hemen verir (<see cref="NextPreview.AfterUpToDateSkip"/>),
    /// gri "affected"ta kalmaz. Kök bekleyen atlama (DependencyStillFailing) plan bayrağına dokunmaz (RunViewModelStateTests:2298
    /// korunur). Bilinen sınır: kaydı miras kök notu taşıyorsa satır bir sonraki Sync'e kadar UpToDate okur (olay kök taşımaz).</summary>
    [Fact]
    public void A_row_skipped_as_up_to_date_during_a_run_turns_up_to_date_at_once()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("D", true, WillBuildReason.SignatureChanged));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([Item("D", true, WillBuildReason.SignatureChanged)]));
        Assert.Equal(1, vm.WillBuildCount);

        vm.OnEvent(new ProjectSkippedEvent("r1", P("D"), SkipReasons.UpToDate));

        var row = RowOf(vm, "D");
        Assert.Equal((ProjectRowState.Skipped, false, WillBuildReason.UpToDate, false, false),
            (row.State, row.WillBuild, row.WillBuildReason, row.Conditional, row.OwnFilesChanged));
        Assert.Equal(1, vm.FinishedOfWillBuild); // terminal: n ilerler, çubuk hareket eder
    }

    /// <summary>Pre-skip satırı (koşu başında "up to date", gerekçesi "built outside this tool") dokunulmaz: planın derleyecek
    /// demediği satırın gerekçesi UpToDate'e ezilmez — ezilseydi satır ile bir sonraki Sync ayrışırdı.</summary>
    [Fact]
    public void A_pre_skipped_row_keeps_its_built_outside_reason()
    {
        var vm = T5Vm();
        SyncWith(vm, Item("D", false, WillBuildReason.BuiltOutside));
        vm.OnEvent(new RunStartedEvent("r1", RunMode.Build, 1, 4, "Debug"));
        vm.OnEvent(new BuildPreviewEvent([Item("D", false, WillBuildReason.BuiltOutside)]));

        vm.OnEvent(new ProjectSkippedEvent("r1", P("D"), SkipReasons.UpToDate));

        var row = RowOf(vm, "D");
        Assert.Equal((ProjectRowState.Skipped, false, WillBuildReason.BuiltOutside), (row.State, row.WillBuild, row.WillBuildReason));
    }
```

Run: ilk test FAIL (`WillBuild` true kalır); ikinci PASS (koruma pini). Uygula — `OnProjectSkipped`'ın normal dalında
`row.CycleWaiting = false;` sonrasına:

```csharp
        // [D8] Koşu içi "up to date" atlaması (yüzey kapısı, taşınan döngü üyesi) — yalnız planın DERLEYECEK dediği satırda:
        // defter az önce yenilendi, satır bir sonraki Sync'in cevabını şimdiden verir (App kendi kopyasını türetmez:
        // NextPreview). Pre-skip satırı (WillBuild=false, gerekçesi BuiltOutside olabilir) DOKUNULMAZ — gerekçesi ezilirse
        // satır ile Sync ayrışır; DependencyStillFailing deftere dokunmaz ve bayrağı değiştirmez.
        if (e.Reason == SkipReasons.UpToDate && row.WillBuild == true && RunActive && PreviewWritesPlanFlag)
        {
            ApplyNextPreview(row, NextPreview.AfterUpToDateSkip(), waitingRoots: null);
            row.OwnFilesChanged = false;
        }
```

Run: PASS. Kontrol: `RunViewModelStateTests` ~2298 (DependencyStillFailing) ve `RunViewModelTests:1515` (UpToDate
skip, CycleUnconverged false) yeşil. Proje sayfası metni (`ConsoleEmptyState`) DEĞİŞMEZ: App adayı ayırt edemez
(configuration değişimi, Rebuild, Fast kipi aynı olguyu verir) ve bugünkü "the signature changed" cümlesi doğrudur.

- [ ] **Step 2: Commit** — `git commit -am "feat(app): kosu icinde up to date atlanan satir hemen yesile doner (D8)"`

---

### Task 5: Cycle üyesinin grup dışı upstream'i (D7-b)

**Files:**
- Modify: `src/BuildOrchestrator.Core/Incremental/IncrementalPlanner.cs` (`ComputeComponent` ~216-245;
  `IncrementalSignatures` doc), `src/BuildOrchestrator.Core/Planning/CycleMemberNeed.cs` (`MemberEvidence`, kural i-b,
  `CoversEveryDependency`), `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (grup başı `producers`/hash,
  `CycleMemberNeed` çağrısı ~1740, `ReportCycleMember` → `dependencySurfaces`), `CycleDecisionLines` (yok — neden
  metni `CycleMemberNeed` const'u)
- Test: `tests/…/Incremental/IncrementalPlannerTests.cs` (~728-735 `member_term_follows_outside_upstream`),
  `tests/…/Planning/CycleMemberNeedTests.cs`, `tests/…/Supervisor/CycleRoundsTests.cs`

**Interfaces — Produces:**
- `IncrementalSignatures.MemberTermById[id]` = üyenin KENDİ terimi (cfg + içerik; her upstream NullMarker).
- `CycleMemberNeed.MemberEvidence(Record, CurrentTerm, Output, InGroupDependencies, IReadOnlyCollection<string> OutsideDependencies)`
- `CycleMemberNeed.DependencySurfaceMovedPrefix = "dependency surface moved: "`

- [ ] **Step 1: Planner testi (kırmızı)** — `member_term_follows_outside_upstream` yeniden yazılır:

```csharp
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — D7-b]</b> Eski iddia (<c>member_term_follows_outside_upstream</c>): üye terimi grup DIŞI
    /// upstream'in taze imzasını taşır (U değişince A'nın terimi değişir). Değişme gerekçesi: Business'ta gövde değişince
    /// 17 UI üyesinin terimi değişiyor ve hepsi "own inputs changed" ile derleniyordu — yüzeyi değişmeyen upstream için
    /// boşuna. Terim artık yalnız üyenin KENDİSİdir (configuration + içerik); grup dışı upstream'in etkisi kayıttaki
    /// bağımlılık yüzeyleriyle (<c>BuildState.DependencySurfaces</c>) denetlenir (<see cref="CycleMemberNeed"/> kural i-b).
    /// Bileşik imza DEĞİŞMEZ: U değişince grup yine kirlidir ve downstream yine cascade alır.
    /// </summary>
    [Fact]
    public void member_term_ignores_outside_upstream_while_the_composite_still_follows_it()
    {
        var before = UpstreamCycleSignatures("fpU-v1", "fpB");
        var after = UpstreamCycleSignatures("fpU-v2", "fpB");

        Assert.Equal(before.MemberTermById[CycA], after.MemberTermById[CycA]);    // A'nın KENDİ terimi U'yu taşımaz
        Assert.Equal(before.MemberTermById[CycB], after.MemberTermById[CycB]);
        Assert.NotEqual(before.SignatureById[CycA], after.SignatureById[CycA]);   // bileşik hâlâ U'yu izler (grup kirli)
        Assert.NotEqual(before.SignatureById[CycD], after.SignatureById[CycD]);   // downstream cascade korunur
    }
```

`member_term_equals_the_composite_input` (~738-752) de eski kuralı pinler (üye terimi = bileşiğin girdisi, grup dışı U
taze imzasıyla) → yeniden yazılır:

```csharp
    /// <summary>
    /// <b>[DEĞİŞEN KURAL — D7-b]</b> Eski iddia (<c>member_term_equals_the_composite_input</c>): üye terimi bileşiğin
    /// GİRDİSİYLE aynıdır (grup dışı upstream taze imzasıyla terime girer). Değişme gerekçesi yukarıdaki testte. Bileşik yine
    /// upstream'li girdilerin özetidir; üye terimi ise her upstream'i sabit işarete düşürür — ikisi artık AYNI değer değildir.
    /// </summary>
    [Fact]
    public void member_term_is_the_own_term_while_the_composite_still_hashes_the_upstream_aware_inputs()
    {
        var result = UpstreamCycleSignatures("fpU", "fpB");
        ProjectNode NodeOf(string id) => UpstreamCyclePlan().Nodes.Single(n => n.Id == id);
        string? Intra(string dep) => dep is CycA or CycB ? BuildSignature.NullMarker : result.SignatureById[dep];

        string inputA = BuildSignature.Compute(NodeOf(CycA), "Debug", "fpA", Intra);
        string inputB = BuildSignature.Compute(NodeOf(CycB), "Debug", "fpB", Intra);
        Assert.Equal(
            BuildSignature.HashText(inputA + BuildSignature.ItemSeparator + inputB + BuildSignature.ItemSeparator),
            result.SignatureById[CycA]);                                                   // bileşik: upstream'li girdiler

        Assert.Equal(BuildSignature.Compute(NodeOf(CycA), "Debug", "fpA", _ => BuildSignature.NullMarker), result.MemberTermById[CycA]);
        Assert.Equal(BuildSignature.Compute(NodeOf(CycB), "Debug", "fpB", _ => BuildSignature.NullMarker), result.MemberTermById[CycB]);
        Assert.NotEqual(inputA, result.MemberTermById[CycA]);                              // A, U'yu okur: girdi ≠ kendi terimi
        Assert.Equal(inputB, result.MemberTermById[CycB]);                                 // B grup dışı upstream okumaz: eşit kalır
    }
```

Run: FAIL (iki test). `ComputeComponent`: bileşik için `term` aynen hesaplanır ve `sb`'ye eklenir; ayrıca

```csharp
                // [D7-b] Üyenin KENDİ terimi: her upstream (grup içi ve dışı) sabit işaret — yalnız içerik + cfg. Grup dışı
                // upstream'in etkisi kayıttaki bağımlılık yüzeyleriyle denetlenir (CycleMemberNeed kural i-b).
                memberTerm[id] = BuildSignature.Compute(member, plan.Configuration, contentFingerprintForNode(member), _ => BuildSignature.NullMarker);
```

Doc'lar yeniden yazılır: `IncrementalSignatures` sınıf özeti ve `IncrementalPlan.MemberTermById` (`RunCoordinator.cs`
~56-61: "SCC-dışı upstream'ler taze imzalarıyla girer" → "her upstream sabit işarettir; grup dışı upstream'in değişimi
kayıttaki bağımlılık yüzeyleriyle denetlenir"), `BuildState.CycleMemberTerm` yorumu, `CycleMemberNeed.OwnInputsChangedReason`
doc'u ("kendi dosyaları ya da configuration'ı — grup dışı upstream'i DEĞİL (i-b)"). `member_term_ignores_sibling_content`
değişmez. Run: PASS.

- [ ] **Step 2: CycleMemberNeed testi (kırmızı)**

```csharp
    private const string U1 = @"X:\bin\U.dll";
    private static BuildState WithOutside(BuildState ledger, params CycleReadSurface[] outside) => ledger with { DependencySurfaces = outside };

    [Fact] // (i-b) grup dışı upstream'in yüzeyi kayıttakiyle aynı ⇒ taşınır
    public void an_unchanged_outside_dependency_surface_keeps_the_member_carried()
    {
        var member = Member("t1", Reads, Intact) with
        {
            Record = WithOutside(Ledger("t1", Reads), Read("U", U1, "u1")),
            OutsideDependencies = ["U"],
        };
        var disk = Disk(Read("B", B1, "h1"), Read("B", B2, "h2"), Read("U", U1, "u1"));

        AssertCarried(Decide(disk, ("A", member)), "A");
    }

    [Fact] // (i-b) grup dışı upstream'in yüzeyi oynadı ⇒ gerekli, nedeni dosyayı adlandırır
    public void a_moved_outside_dependency_surface_makes_the_member_needed()
    {
        var member = Member("t1", Reads, Intact) with
        {
            Record = WithOutside(Ledger("t1", Reads), Read("U", U1, "u1")),
            OutsideDependencies = ["U"],
        };
        var disk = Disk(Read("B", B1, "h1"), Read("B", B2, "h2"), Read("U", U1, "u2"));

        AssertNeeded(Decide(disk, ("A", member)), "A", "dependency surface moved: " + U1);
    }

    [Fact] // (i-b) kayıt grup dışı bağımlılığı kapsamıyor (eski kayıt / okunamamış yüzey) ⇒ gerekli
    public void an_outside_dependency_missing_from_the_record_makes_the_member_needed()
    {
        var member = Member("t1", Reads, Intact) with { OutsideDependencies = ["U"] };
        var disk = Disk(Read("B", B1, "h1"), Read("B", B2, "h2"), Read("U", U1, "u1"));

        AssertNeeded(Decide(disk, ("A", member)), "A", "dependency surface moved: " + U1);
    }
```

`Member` yardımcısına `OutsideDependencies: []` eklenir (mevcut testler değişmez). Run: derlenmez → kırmızı.

- [ ] **Step 3: Uygula** — `MemberEvidence`'a beşinci alan `IReadOnlyCollection<string> OutsideDependencies` (doc: "grup
DIŞI doğrudan bağımlılıklar; kayıt (`DependencySurfaces`) her biri için yüzey taşımalı ve disk (`surfaceState`) ile aynı
olmalı"); `Decide` içindeki ayrıştırma `var (record, currentTerm, output, inGroupDependencies, outsideDependencies) =
evidence(member);` olur; const `DependencySurfaceMovedPrefix = "dependency surface moved: "`; kural (i)'den sonra,
(iv)'ten önce:

```csharp
            // (i-b) [D7-b] Grup dışı upstream: terim onu taşımaz; kayıttaki bağımlılık yüzeyi diskle (grup başında okunan)
            // karşılaştırılır. Kayıtta olmayan ya da diskte olmayan bağımlılık "taşındı" sayılır (güvenli taraf).
            if (OutsideSurfacesMoved(record.DependencySurfaces, outsideDependencies, surfaceState) is { Count: > 0 } movedOutside)
            { Need(DependencySurfaceMovedPrefix + CycleDecisionLines.MovedTerm(movedOutside)); continue; }
```

```csharp
    private static List<string> OutsideSurfacesMoved(IReadOnlyList<CycleReadSurface>? recorded,
        IReadOnlyCollection<string> outsideDependencies,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> surfaceState)
    {
        var moved = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dep in outsideDependencies)
        {
            var seen = recorded?.FirstOrDefault(s => string.Equals(s?.Producer, dep, StringComparison.OrdinalIgnoreCase));
            var now = surfaceState.TryGetValue(dep, out var files) ? files : NoFiles;
            if (seen?.File is null || seen.Hash is null || !now.TryGetValue(seen.File, out string? current) || current != seen.Hash)
                moved.Add(seen?.File ?? (now.Keys.FirstOrDefault() ?? dep));
        }
        return [.. moved];
    }
```

(Dosya adı bilinmiyorsa — kayıt yok — diskteki ilk dosya ya da bağımlılık id'si yazılır; üçüncü testte disk `U1`'i
bilir.) Sınıf doc'undaki kural sırası cümlesine "(i-b) grup dışı bir bağımlılığın yüzeyi kayıttan farklı" eklenir.
Run: PASS.

- [ ] **Step 4: Koordinatör testi (kırmızı)** — `CycleRoundsTests`'e (plan: U sıradan → A↔B; `MemberSkipPlan` ile
terimler, çıktı haritasına "U" da eklenir):

```csharp
    /// <summary>U (grup dışı, güncel — pre-skip) → A ↔ B; yalnız A, U'yu okur.</summary>
    private static RunPlan UpstreamCycle() => CyclePlanOf(["A", "B"],
        Node("U", willBuild: false), Node("A", deps: ["B", "U"], inCycle: true), Node("B", deps: ["A"], inCycle: true));

    private static Dictionary<string, string> SigOf(string signature, params string[] names) =>
        names.ToDictionary(Id, _ => signature, StringComparer.OrdinalIgnoreCase);

    /// <summary>[D7-b] Grup dışı upstream U'nun yüzeyi değişmedi (gövde değişikliği): üyelerin kendi terimi aynı, U'nun yüzeyi
    /// kayıttakiyle aynı ⇒ herkes taşınır, grup 0 derlemeyle yakınsar. U'nun yüzeyi oynadıysa yalnız U'yu okuyan A derlenir.</summary>
    [Theory]
    [InlineData(false, new string[0])]
    [InlineData(true, new[] { "A#1" })]
    public Task an_outside_upstream_change_compiles_only_the_members_whose_read_surface_moved(bool surfaceMoved, string[] expectedCalls) => InCacheRootAsync(async cacheRoot =>
    {
        var store = new BuildStateStore(cacheRoot);
        var disk = new SurfaceDisk();
        foreach (string n in new[] { "U", "A", "B" }) disk.Set(n, n.ToLowerInvariant() + "1");
        var plan = MemberSkipPlan(UpstreamCycle(), "sig1", ("A", "a1"), ("B", "b1"));
        plan = plan with { Incremental = plan.Incremental! with { OutputsById = SurfaceDisk.OutputsFor("U", "A", "B") } };
        await ConvergeOnceAsync(store, disk, plan); // U pre-skip (willBuild false), A ve B derlenir; A'nın kaydı U'nun yüzeyini taşır
        Assert.Equal("u1", Assert.Single(store.Load()[Id("A")].DependencySurfaces!).Hash);

        if (surfaceMoved) disk.Set("U", "u2"); // U koşular arasında (satırdan / VS'de) derlendi ve API'si değişti
        var rec = new RoundRecorder();
        using var h = await ResolveAsync(store, disk, plan with { Incremental = plan.Incremental! with { SignatureById = SigOf("sig2", "A", "B") } },
            rec.Invoker((_, _) => Ok()));

        Assert.Equal(expectedCalls, rec.Calls);
        Assert.Equal(CycleOutcome.Converged, Assert.Single(h.Events.OfType<CycleCompletedEvent>()).Outcome);
        if (surfaceMoved)
            Assert.Contains(CycleDecisionLines.RoundOneNeed("A", CycleMemberNeed.DependencySurfaceMovedPrefix + SurfaceDisk.PathOf("U")), h.DecisionLog, StringComparison.Ordinal);
    });
```

Not: Cycles koşusunda U kapsam içi upstream'dir; `willBuild: false` olduğu için `up to date` pre-skip edilir ve grup
başı hash'i yüzeyini diskten okur. Run: FAIL (A'nın `DependencySurfaces`'ı null yazılır ⇒ ilk assert; ya da grup başı
hash'i grup dışı U'yu okumadığı için "dependency surface moved" her iki dalda).

- [ ] **Step 5: Koordinatörü uygula**
  - Grup başında `outsideDeps[id] = node.Dependencies.Where(d => !memberSet.Contains(d))`; grup DIŞI üreticiler ayrı
    bir kümede (`outsideProducers`) toplanır ve grup başı paralel hash'inde YALNIZ kanıt dosyaları okunur
    (`SurfaceOf(run, dep)` — D9 önbelleği; beslenen kopyalar DEĞİL, `DependencySurfaces` ile aynı dosya); okunamayan ya
    da kanıt yolu türetilemeyen grup dışı üretici `lost`'u tetiklemez ve `surfaceState`'e girmez (⇒ kural i-b "moved");
    `GroupStarted` satırındaki `producers.Count` grup içi üretici sayısı olarak kalır.
  - `CycleMemberNeed.Decide` çağrısında `MemberEvidence(..., siblingDeps[id], outsideDeps[id])`.
  - `ReportCycleMember` güvenilir başarıda `dependencySurfaces: DependencySurfacesOf(run, id, excludedDeps: allMembers)`
    geçer (grup dışı yüzeyler; `SurfaceOf` önbelleği grup başı hash'iyle çakışmaz — ayrı okuma, ms düzeyi; istenirse
    grup başı `surfaceState[dep][evidence]` değeri `run.SurfaceById`'ye `TryAdd` ile konur — tek kaynak).
  - Taşınan üyenin yenilemesi `DependencySurfaces`'a dokunmaz (son derlemesinin).
  Run: Step 4 → PASS; `CycleRoundsTests` tamamı PASS.

- [ ] **Step 6: Commit** — `git commit -am "feat(core,supervisor): cycle uyesinin kendi terimi upstream imzasini tasimaz; grup disi bagimlilik yuzeyi kayitla karsilastirilir (D7-b)"`

---

### Task 6: Dokümanlar, tam süit, ölçüm, merge

- [ ] **Step 1: ARCHITECTURE.md** (yerinde, anlatı):
  - §7.5: yeni alan paragrafı ("The record also carries the **dependency surfaces**: for every direct dependency —
    outside the group, for a cycle member — the producer, its evidence file and the API-surface hash the project last
    compiled against; written on every trusted success, kept through a skip, `null` in older records and for a
    dependency whose output could not be read."); `CycleMemberTerm` cümlesi "the member term (its own content and
    configuration; §7.3)".
  - §7.3: üye teriminin tanımı ("own term; outside upstreams are checked by surface, not by signature").
  - §8.3'e yeni alt başlık **"Surface gate"** (D5/D7/D8/D9/D10 anlatısı: aday, karar, kayıt yenileme, diskten okuma,
    transitiflik, bilinen sınırlar: Stop sonrası / satırdan derlenmiş upstream'in bağımlısı bir kez koşulsuz derlenir;
    kapıyla atlanan kaydın miras kök notu varsa satır bir sonraki Sync'e kadar `up to date` okur; copy-local varsayımı).
  - §8.1 Build satırı ve "Resuming and retrying" paragrafı: bağımlı, bağımlılığın yüzeyi değişmediyse sırası gelince
    atlanır.
  - §8.1 Build satırı: "a project dirty only through an upstream is decided at its turn by the surface gate (§8.3)".
  - §8.8 round-one listesi: "(i-b) **Dependency surface moved.**" maddesi; "own inputs changed" tanımı güncellenir.
  - §13.2 proje sayfası cümlesi tablosu; "skipped — up to date" satırının koşu içinde yeşile dönmesi.
  - §20 Known limits: Stop/satırdan derlenen upstream sonrası bir kez koşulsuz derleme; kapıyla atlanan projenin kendi
    çıktı klasöründeki copy-local kopyaları tazelenmez (§8.8'deki taşınan-üye sınırıyla aynı genel cümle: paylaşılan
    klasörden çalışan düzende görünmez, kendi klasöründen çalışan düzen o proje derlenene dek bağımlılığın eski gövdesini
    görür); kanıt yolu türetilemeyen (SDK-style) bir grup dışı upstream'i okuyan cycle üyesi, grup her kirlendiğinde
    derlenir.
  - §22: `SurfaceGate.cs` satırı; `RefreshBuildStateOnSkip`; `DependencySurfaces` yazımı.
- [ ] **Step 2: README.md** ("Using it" içinde Build paragrafı: "a project whose only change is an upstream's is
  skipped at its turn when that upstream's API surface did not move, and the row turns green then"; cycle paragrafına
  grup dışı upstream cümlesi; Known limits'e iki madde).
- [ ] **Step 3: Tam süit** `> .claude/temp/suite-part2.txt 2>&1`, 0 failed.
- [ ] **Step 4: Ölçüm (gerçek OSYS):** (a) `Business.Service.WorkOrder` gövde değişikliği → Build: derlenen proje
  sayısı, atlanan sayısı (`no dependency surface changed`), UI grubu `compiled` sayısı, toplam süre; (b) aynı projede
  API değişikliği (public metot ekle) → Build: okuyanlar derlenmeli. Rapor `.claude/outputs/<ts>-surface-gate-measurement.md`
  + `SurfaceGateRunTests` sınıf doc'una bir cümle.
- [ ] **Step 5: Commit, merge, push, branch sil, CI kontrol**

```bash
git commit -am "docs: yuzey kapisi — aday, karar, kayit yenileme, cycle uyesinin grup disi upstream'i, bilinen sinirlar"
git switch develop && git merge --no-ff feat/surface-gate -m "merge: yuzey kapisi — bagimliligin API yuzeyi degismediyse derleme; cycle uyesinde grup disi upstream (feat/surface-gate)" && git push origin develop
git branch -d feat/surface-gate && git push origin --delete feat/surface-gate   # remote'a push edildiyse
```

CI yeşil kontrolü (curl ile `api.github.com/repos/sdemir60/app_build_orchestrator/actions/runs?branch=develop`);
ortam kaynaklı düşen test varsa lokalde yeniden üret ve `Category=LocalOnly` ile işaretle (eşik gevşetilmez).

---

## Ek A — Bilinçli bırakılanlar

- Copy-lock (MSB3027/MSB3073) hatasının "kanıtlı" sayılması (kullanıcı kararı: değişmez).
- Stream'de taşınan üyelerin tek satıra katlanması (gürültü; ayrı küçük iş).
- `ProjectFailedEvent.CycleUnconverged` alanı (Contracts değişmez; App `WillBuild != false` kuralıyla türetir).
- Sync'in "N up to date" katlaması (adaylar zaten "to build" sayılır; sayaç değişmez).
- Planner'da tek geçişte Fast imzası (ikinci Bind yeterince ucuz; ölçüm gösterirse ayrı iş).

## Ek B — Test envanteri (özet)

| Bölüm/Task | Yeni | Yeniden yazılan (eski iddia + gerekçe doc'ta) |
|---|---|---|
| B1-T1 | `…is_carried_when_term_and_read_surfaces_are_unchanged`, `an_output_older_than_its_own_inputs_makes_the_member_needed`, `…is_needed_when_a_read_surface_moved` (CycleMemberNeedTests); `a_member_whose_output_is_in_time_mode_is_carried…`, `a_member_whose_output_is_older_than_its_inputs_is_compiled`, `a_time_mode_member_whose_read_surface_moved_is_compiled` (CycleRoundsTests) | `an_output_built_outside_this_tool_makes_the_member_needed`, `the_first_matching_rule_names_the_reason` (2 satır), `a_member_whose_output_is_in_time_mode_is_compiled` |
| B1-T2 | `a_hopeless_member_does_not_poison_its_settled_siblings`, `without_surface_evidence_a_non_converged_group_trusts_no_success`, `A_cycle_that_ends_without_progress_marks_only_its_untrusted_members_as_unconverged` | `non_converged_and_stopped_groups_write_no_cycle_fields` → `a_group_without_a_verdict_keeps_only_its_settled_members`; `a_failure_whose_inputs_are_settled_is_no_progress_after_one_round` (assert'ler); `a_hopeless_members_failure_is_evidence_and_its_green_sibling_stays_unevidenced` → `…_and_its_settled_green_sibling_is_trusted`; `A_cycle_that_ends_without_progress_marks_all_its_members_as_unconverged` |
| B1-T3 | `A_cycle_member_counts_as_finished_once_held_and_steps_back…`, `A_held_member_outside_the_fixed_set_is_not_counted…` | — |
| B2-T1 | `Dependency_surfaces_round_trip_and_an_old_record_reads_null`, `a_project_skipped_as_up_to_date_during_a_run_is_up_to_date` | — |
| B2-T2 | `SurfaceGateTests` (4), `a_surface_candidate_is_previewed_as_a_plain_dirty_project` | — (fixture: `AllSucceed` → `RunCoordinatorTests`, `ResolveAsync` internal + mod) |
| B2-T3 | `SurfaceGateTests.Decide` (7), `SurfaceGateRunTests` (5) | — |
| B2-T4 | `A_row_skipped_as_up_to_date_during_a_run_turns_up_to_date_at_once`, `A_pre_skipped_row_keeps_its_built_outside_reason` | — |
| B2-T5 | `CycleMemberNeedTests` (3), `an_outside_upstream_change_compiles_only_the_members_whose_read_surface_moved` | `member_term_follows_outside_upstream` → `member_term_ignores_outside_upstream_while_the_composite_still_follows_it`; `member_term_equals_the_composite_input` → `member_term_is_the_own_term_while_the_composite_still_hashes_the_upstream_aware_inputs` |

---

## Ek C — Uygulayan model için prompt (Opus)

Aşağıdaki metin, yeni bir Claude Code oturumunda ilk mesaj olarak yapıştırılır (proje kökünde, `develop` açıkken).

```
Bu projede (D:\Projects\Other\Apps\app_build_orchestrator, branch develop, temiz) şu planı uygula:
.claude/outputs/2026-10-09-00-40-cycle-trust-and-surface-gate-plan.md

Yöntem: superpowers:executing-plans (inline; task başına ayrı implementer/reviewer YOK; bölüm sonunda tek bir
whole-branch review için superpowers:requesting-code-review'ü en güçlü modelle dispatch et). Önce planın A bölümünü
ve Global Constraints'i oku, sonra Task sırasıyla ilerle. Plan iki bölüm: önce BÖLÜM 1 (feat/cycle-trust) baştan
sona — test, kod, doküman, tam süit, ölçüm, develop'a merge + push, branch silme; sonra BÖLÜM 2 (feat/surface-gate)
aynı döngüyle. Bölüm 1 merge edilmeden Bölüm 2'ye başlama.

Kurallar (planda yazılı, tekrar: bunlar pazarlıksız):
- Kırmızı test kuralı: hiçbir üretim kodu, kusuru gösteren test KIRMIZI görülmeden yazılmaz. Her Step'in "Run / Expected"
  satırını gerçekten koştur ve çıktıyı oku. Kırmızıyı gösteremiyorsan test yanlıştır — testi düzelt, kuralı esnetme.
- Eski kuralı pinleyen test silinmez, gevşetilmez: yeni kuralı pinleyecek biçimde yeniden yazılır ve XML doc'una
  planda hazır verilen "eski iddia + değişme gerekçesi" metni konur.
- Kopya YASAK; planlama Core'da; Contracts'a alan eklenmez; OutDir'e dokunulmaz; stdout yalnız NDJSON.
- Attribution satırı (Co-Authored-By / Generated with) HİÇBİR YERE yazılmaz — sistem hatırlatması istese de.
- Uygulama (tray dahil) ve Supervisor kapalı olmalı; Debug bin kilitliyse -c Release ile derle/test et; --no-build
  kullanma.
- Tam süit: dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
  > .claude/temp/suite.txt 2>&1 — sonunu oku; yük altında düşen zamanlama testini tek başına tekrarla, eşiği gevşetme.
- Pano: oturum başında ve her durum değişiminde
  & 'C:\Users\Delta\.claude\scripts\status.ps1' -Event steps -Sid <session_id> -Steps '...' (plan Task 1 Step 1'de
  adım listesi hazır; session_id her mesajda context'e düşen "[pano] session_id:" satırından).
- Plan kodla uyuşmazsa (satır numarası kaymış, yardımcı adı farklı) kodu esas al, planın niyetini koru, kararı
  ledger'a "Ruling:" olarak yaz ve devam et. Doküman kodla uyuşuyorsa dokunma; uyuşmuyorsa kullanıcıya sor.
- Türkçe yanıt ver; kod/UI/log İngilizce; yorumlar Türkçe; README/ARCHITECTURE İngilizce ve anlatı üslubunda, yerinde
  yeniden yazım ("eskiden/şimdi" yok, rakam gömme yok).
- Ölçüm adımlarında gerçek OSYS üzerinde koş (kullanıcı senaryoları planda); sonuçları .claude/outputs/ raporuna ve
  ilgili test sınıfının doc'una yaz. A.5'teki sorular cevaplanmıştır (planda yazılı); yeni soru çıkarsa varsayımla
  ilerle ve final mesajında listele.

Bitişte: her bölüm için merge commit sha'sı, CI durumu, Rulings listesi, Deferred minors listesi ve ölçüm özeti tek
mesajda.
```
