# Optimize Butonu Motoru — Uygulama Sonucu

> Bu dosya bir **kayıttır**, prompt değil. Plan/opus-prompt/merge-prompt üçlüsüyle aynı tarih-saat önekini
> taşır çünkü aynı işin parçasıdır; gerçek uygulama tarihi 2026-08-26'dır.
>
> **Merge session'ı bunu planla birlikte okur:** aşağıdaki sapmalar plandan bilinçli ayrılmalardır, eksik
> task DEĞİLDİR.

| | |
|---|---|
| **Plan** | `.claude/outputs/2026-08-19-22-18-optimize-button-engine-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-22-18-optimize-button-engine-opus-prompt.md` |
| **Merge promptu** | `.claude/outputs/2026-08-19-22-18-optimize-button-engine-merge-prompt.md` |
| **Branch** | `feat/optimize-button-engine` (yalnız LOCAL — remote'a push EDİLMEDİ) |
| **Taban** | `main` @ `49ed712` |
| **Durum** | Tüm task'lar (T1–T8) tamam, tam süit yeşil, **merge EDİLMEDİ** — branch bilinçli olarak duruyor |

## Commit'ler (task başına bir tane)

```
82e44ec feat(optimize): optimizeWorkspace komutu ve optimize event ucu          (T1)
4be3342 feat(optimize): defter hijyen primitifleri - prune ve tmp supurme        (T2)
694bea4 feat(optimize): restore-only invoker ucu ve NuGet packages yardimcisi    (T3)
a1a7c43 feat(optimize): Core da OptimizeWorkspaceService                         (T4)
072d25d feat(optimize): Supervisor wiring ve run-aktif reddi                     (T5)
fdcee84 feat(optimize): App VM - OptimizeCommand, kapilar ve konsol/stream       (T6)
eac1c0c feat(optimize): bakim kutusunda Optimize artik canli                     (T7)
463050c docs: Optimize butonu motoru dokumanlara islendi                         (T8)
```

## Doğrulama

`dotnet test --filter "Category!=Acceptance"` → **2157 geçti · 0 kırmızı · 1 atlanan** (atlanan, bu işten
önce de atlanıyordu). Her task'ta önce kusuru yakalayan test KIRMIZI gösterildi, sonra implementasyon yazıldı.

**Yapılmayan doğrulama:** uygulama gerçek pencerede açılıp Optimize'a basılmadı — doğrulama tamamen test
düzeyindedir. Gerçek bir OSYS workspace'inde tık denemesi yapılmadı.

## Plandan sapmalar

### 1. T3'ün varsaydığı "sahte-exe harness'i" repoda YOK

Plan `MsBuildInvokerTests`'te child'a giden argümanları okuyabilen bir harness olduğunu varsayıyordu;
oradaki testler gerçek MSBuild ile (`SkippableFact`) koşuyor ve komut satırını gözlemleyen bir dikiş yok.
Argüman setini metin olarak pinlemek yerine **davranışsal pin** yazıldı:

- `RestoreAsync_runs_restore_only_and_never_builds_the_project` — legacy fixture'da `RestoreAsync` DLL
  ÜRETMEZ (aynı fixture `InvokeAsync` ile derlenince üretiyor, mevcut test (a) bunu pinliyor). Bu,
  `-t:Build`'in argüman setinde olmadığının kanıtıdır; restore yolu build argümanlarına kayarsa kırmızıya döner.
- `RestoreAsync_cancellation_kills_the_child_like_the_build_path` — önceden iptal edilmiş token ile
  `Killed=true / TimedOut=false` (build yolundaki (c) testinin ikizi). Plandaki "per-project timeout" testi
  yerine bu yazıldı: 10 dakikalık timeout'u gerçek zamanda beklemek D8'e aykırı olurdu, ikisi de AYNI
  `PerProjectTimeout` kurulumundan geçiyor (ortak `OpenScope` prologu).

Argüman listesinin tek kaynaktan geldiği ayrıca servis düzeyinde görülüyor: konsola basılan `msbuild …`
satırı `MsBuildArguments.RestorePackagesConfig`'ten string.Join ile üretiliyor.

### 2. `HintPathClassifier.IsUnderBin` de public açıldı

Plan K-2 yalnız `IsNuGetPackagesPath`'i söylüyordu. Ama adım 2'nin (kırık referans teşhisi)
`ExternalOsysPlatform` ayağı `\bin\` literalini de kullanıyor — kopya yasağı gereği o da aynı gerekçeyle
açıldı. Üretici (producer) kontrolü çağıranda kaldı: o bir GRAF sorusudur, yol sorusu değil.

### 3. `Two_csproj_in_the_same_folder_are_deduplicated` yazılmadı

Servis `scan.CsprojPaths`'i `GetFullPath` + `Distinct(OrdinalIgnoreCase)` ile tekilleştiriyor, ama
`WorkspaceScanner` zaten tekil yol döndüğü için testin anlamlı bir KIRMIZISI yoktu — her koşulda yeşil
kalacak bir test yazmak kırmızı-test kuralına aykırı olurdu.

### 4. İki test kurgusu plandan farklı

- **Kilitli artık:** plan `FileShare.None` diyordu; kilit `project.assets.json`'a konursa
  `StaleObjDetector.Inspect` (never-throw, belirsizde "temiz") projeyi stale bile SAYMIYOR ve silme adımına
  hiç girilmiyor. Kilit silinecek artıklardan birine (`*.nuget.g.props`) taşındı — sınanmak istenen yol
  (tespit edildi, silinemedi) ancak böyle kuruluyor.
- **Motor kapısı:** plan "motor yok" diyordu; gerçek kural `IsEngineUnavailable`, yani YENİDEN
  BAŞLATILAMAYAN motor. Ölü ama restartable bir motorda `CanSync` gibi `CanOptimize` de AÇIK kalır
  (kullanıcı "Restart engine" dedikten sonra basabilmeli). Test `Optimize_is_disabled_only_when_the_engine_
  cannot_be_restarted` olarak yazıldı.

### 5. D8 sleep guard'ının allow-list'ine bir satır eklendi

`OptimizeWorkspaceService`'in restore kalp atışı (`Task.Delay(30s)`, K-13) `NoSleepPollTests`'i kırdı.
Guard'ın kendi sözleşmesine uygun şekilde **gerekçeli** eklendi: bu, `HeartbeatDelay` dikişinin üretim
varsayılanıdır ve POLL DEĞİLDİR — bekleme `Task.WhenAny`'nin öteki ayağıyla (restore child'ının task'ı)
yarışır, restore biter bitmez döngü anında çıkar. Eşik/bütçe GEVŞETİLMEDİ.

### 6. Birleşik `MaintenanceBoxTests` pini bölündü, silinmedi

`Clean_and_optimize_are_disabled_...` iki teste ayrıldı: `Clean_stays_disabled_...` (o iş hâlâ yapılmadı,
iddia aynen duruyor) ve `Optimize_is_wired_to_the_optimize_command_and_its_tooltip_names_the_job`. Eski
test doc'unda değişme gerekçesi yazılı.

## Clean planıyla ortak primitifler — BU BRANCH KURDU

`feat/clean-button-engine` merge edilmemiş olduğu için koordinasyon tablosundaki ortak primitifleri bu iş
kurdu. **Clean merge edilirken bunlar YENİDEN TANIMLANMAZ, var olanlar kullanılır:**

| Primitif | Nerede |
|---|---|
| `RunCoordinator.IsRunActive` (salt-okur probe) | `Supervisor/RunCoordinator.cs` |
| `ClearConsoleBuffers()` | `App/ViewModels/RunViewModel.cs` (`BeginRunAsync` da artık onu çağırıyor) |
| `ByteFormat.Size` | `Core/Formatting/ByteFormat.cs` (`DurationFormat` kardeşi) |
| Kök-prefix normalizasyonu | `Core/Paths/RootScope.cs` |
| `.tmp` deseni süpürücüsü | `Core/Paths/TempFileSweeper.cs` |
| Defter budama | `BuildStateStore.PruneMissingUnderRoot` / `EvaluationCache.PruneMissingUnderRoot` |
| `BuildStateStore.WriteAtomic` | `Upsert` ve prune ortak yazıcısı |
| NDJSON tel ayrıştırması (test) | `tests/…/Supervisor/NdjsonWire.cs` (`SyncStreamingTests` de buna bağlandı) |

Clean'in `RemoveUnderRoot`'u ayrı bir semantiktir (kök altındaki HER kaydı siler); `PruneMissingUnderRoot`
varlık-tabanlıdır. İkisi bir arada yaşayabilir, ama prefix mantığı `RootScope`'tan gelmelidir.

**Clean merge'ünde ayrıca yapılacaklar** (koordinasyon tablosu gereği ikinci gelenin işi):

- Karşılıklı dışlama: `CanClean() += !OptimizeBusy` ve `CanOptimize() += !CleanBusy` + çift kapı testi.
- `NotifySyncGatedCommands()` listesine `CleanCommand` (Optimize zaten ekli).
- `AccessibilityNames.NotAvailableSuffix`: son kullanıcısı Clean'dir → Clean canlanınca const + karar doc'u SİLİNİR.
- `MaintenanceBox.Build()`: `PART_Clean.IsEnabled = false` satırı kalkar; sınıf doc paragrafı yeniden yazılır.
- `MaintenanceBoxTests.Clean_stays_disabled_...` yeni davranışa çevrilir.
- Doküman cümleleri "ikisi de canlı" hâline BİRLEŞTİRİLİR: ARCHITECTURE §5.2 komut listesi, §5.3, §4.6,
  §13.2 bakım kutusu, §16 tablosu, §22 haritası; README'nin bakım kutusu paragrafı.
- `WorkspaceServices` kaydına Clean fabrikası eklenirken `Default(cacheRoot, poolRoot, msbuildInvoker)`
  imzası KORUNUR (Optimize'ın lazy invoker'ı oradan geliyor); ctor kuran testler (`SyncStreamingTests`
  doğrudan ctor, `ProjectLogStreamTests` `Default`, `OptimizeDispatchTests` `ServicesWith`) TEK seferde
  güncellenir.

## Bilinçli sınırlar (v1'de yok, doküman notu bile gerekmez)

Run log yaşlandırma · SDK-style projeler için düz `-t:restore` · HintPath↔packages.config sürüm-drift
onarımı · paralel restore · worktree havuzunda orphan dizin tespiti · Optimize'ın iptal komutu.
