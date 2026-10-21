# Resolve follows the profile — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "Resolve cycles at full priority" ayarını ve arkasındaki kuralı kaldırmak: bir `Cycles` koşusu profilin cap'ini ve priority'sini aynen uygular (Build/Rebuild/Clean'den farkı yok).

**Architecture:** Kural tek saf fonksiyondu (`PerfProfile.ForRun`); Supervisor onu koşu başında ve her canlı `setPerfMode`'da uyguluyor, App aynı dönüşümle konsol notu yazıyor, ayar `startRun` ile motora gidiyor. Kaldırma bütün katmanlarda tek derleme birimidir (Contracts alanı kalkınca App derlenmez), bu yüzden kod tek görevdir; doküman ikinci görevdir.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit (`Category!=Acceptance` süiti).

**Spec:** `.claude/outputs/2026-10-17-10-44-resource-strip-and-resolve-priority-opus-prompt.md` (İş 1) + gerekçe `.claude/outputs/2026-10-17-10-06-resolve-priority-and-resource-strip-decisions.md` §3-§4 (K1, K2). Brief ile rapor çelişirse brief geçerlidir.

## Global Constraints

- Branch: `fix/resolve-follows-profile` (`develop`'tan). Ana proje checkout'unda çalışılır.
- Commit mesajı Türkçe/ASCII, repo üslubu (`fix(core): …`, `test: …`, `docs: …`); **Claude attribution satırı YOK** (`Co-Authored-By`, `Generated with`). Çok satırlı mesaj dosyaya yazılıp `git commit -F` ile verilir.
- Kırmızı test kuralı: davranış değişikliğinden önce yeni kuralı pinleyen test bugünkü kodda KIRMIZI gösterilir.
- Eski test silinmez; yeni kuralı pinleyecek biçimde yeniden yazılır ve doc'una eski iddia + gerekçe yazılır: "2026-10-17 ölçümü (gerçek OSYS, Balanced 4 işçi, 25 proje): sakin makinede kural kazandırmadı (56,5 sn / 52,4 sn), yüklü makinede %5 kazandırdı ama 250 ms üstü 23 kare boşluğu, en uzunu 5,2 sn (cap'li: 2 boşluk, en uzun 1,6 sn)".
- Kopya YASAK: test fixture'ı ve beklenen değerler tek yerde; mevcut yardımcıları (`Applied`, `Released`, `AssertNoteOnlyInDecisionLog`, `PerfContextVm`) kullan.
- Değişmeyenler: perf çipi (Full/Balanced/Light döngüsü), `WorkerBudget.Clamp`, copy floor ve drain kuralı (§11.3), `memory:` satırı, `CHANGELOG.md`, `Directory.Build.props` → `Version`.
- Göç kodu YAZILMAZ: eski `ui-state.json` / settings dosyasındaki alan System.Text.Json'ın bilinmeyen alanı atmasıyla düşer (kodda `UnmappedMemberHandling` yok).
- Build/test: `dotnet build BuildOrchestrator.slnx -c Release` ve `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --filter "<...>"`. Uygulama açıkken Debug bin kilitlidir; Release kullan. Tam süit çıktısı dosyaya alınır (`*> <scratchpad>/suite.txt`), `Select-String` ile süzülmez.

## Review Focus

- Eski bir App'in (v1.10.1) gönderdiği `startRun` JSON'u `"resolveAtFullPriority":true` taşır → yeni Supervisor onu hatasız okur ve Cycles koşusunu profille koşar (IPC testi pinler).
- Eski `ui-state.json` (`"ResolveAtFullPriority": false` alanlı) → açılış hatasız, diğer alanlar (PerfMode vb.) yerinde (UiStateStore testi pinler).
- Eski dışa aktarılmış settings dosyası (`"resolveAtFullPriority": true`) → Import hatasız, diğer anahtarlar uygulanır (SettingsFile/draft testi pinler).
- Koşu ortasında Resolve'da çip Full→Light → motor Light'ın cap 40 + Idle'ını yazar, konsol notu `parallelism: <n> · cpu cap 40%` (Supervisor + VM testleri pinler).
- Light'ta Resolve + takılan kopya → copy floor Build'deki gibi açılır (Balanced 70 + BelowNormal), sonra Light'a döner (Supervisor testi pinler).

---

### Task 1: Kuralı ve ayarı bütün katmanlardan kaldır

**Files:**
- Modify: `src/BuildOrchestrator.Core/ProcessControl/PerfProfile.cs` (`ForRun` kalkar)
- Modify: `src/BuildOrchestrator.Core/ProcessControl/PerfNoteText.cs` (`ResolveNote`, `Note(RunMode, PerfProfile, bool)`, `PriorityValue` kalkar — grep ile başka kullanıcısı olmadığını doğrula)
- Modify: `src/BuildOrchestrator.Contracts/Ipc/IpcMessages.cs:~150-160` (`StartRunCommand.ResolveAtFullPriority` parametresi + doc'u)
- Modify: `src/BuildOrchestrator.Supervisor/RunCoordinator.cs` (`_perfRunMode`, `_perfResolveAtFullPriority` alanları ~230-235; `ApplyPerfLocked` ~517-526 profili doğrudan `_activePerf`'e yazar; ~1051-1052 atamaları; ~1134-1142 Resolve decision.log satırı)
- Modify: `src/BuildOrchestrator.App/ViewModels/GeneralSettings.cs` (enum üyesi `ResolveAtFullPriority`, katalog satırı; sınıf doc'undaki "Dokuz anahtar … üç iş akışı tercihi" sayıları)
- Modify: `src/BuildOrchestrator.App/ViewModels/SettingsDraftViewModel.cs` (özellik, ctor parametresi, Apply/Import/Export/State yolları, `_generalRows` aboneliği ~434-436, doc'lardaki "beş/dokuz" sayıları)
- Modify: `src/BuildOrchestrator.App/ViewModels/SettingsFile.cs` (`resolveAtFullPriority` alanı + fabrika parametresi)
- Modify: `src/BuildOrchestrator.App/Shell/UiStateStore.cs:~110-114` (`ResolveAtFullPriority`)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.cs` (`_resolveAtFullPriority` ~820-825; `runPerf`/`_runPerf` ~964-968, ~1025, ~1777-1785; `CyclePerfAsync` ~1812-1818 → `AppendRunLine(PerfNoteText.Note(profile))`; `OnRunStarted` ~2021-2028 Resolve notu dalı; yorumlardaki "Resolve notundan ÖNCE" sıra cümleleri)
- Modify: `src/BuildOrchestrator.App/ViewModels/RunViewModel.ActionBar.cs` (`ApplyResolveAtFullPriority` ~195-211, `ApplySettingsAsync` parametresi + doc'u, "sıra: Pull → Stash → Resolve → …" cümlesi)
- Modify: `src/BuildOrchestrator.App/MainWindow.xaml.cs` (~211 yükleme, ~1310-1332 kaydetme), `src/BuildOrchestrator.App/Views/SettingsDialog.xaml.cs:~143` (ctor argümanı)
- Test (yeniden yaz): `tests/BuildOrchestrator.Tests/ProcessControl/PerfProfileTests.cs:~55-112`, `Supervisor/RunCoordinatorTests.cs:~2698-2830, ~2962-2986, ~3434-3456`, `Ipc/IpcMessagesTests.cs:~225-247`, `App/RunViewModelStateTests.cs:~750-1150`, `App/GeneralSettingChains.cs:~56-64`, `App/GeneralSettingChainTests.cs`, `App/ResolveAtFullPrioritySettingsTests.cs`, `App/SettingsGeneralPageTests.cs`

**Interfaces:**
- Consumes: —
- Produces: `StartRunCommand` artık `ResolveAtFullPriority` taşımaz. `PerfNoteText.Note(PerfProfile)` koşu içi çip notunun TEK biçimidir (Resolve dahil). `GeneralSettingsCatalog` BUILD grubu yalnız `PullBeforeBuild` satırını taşır. `RunViewModel` artık `ResolveAtFullPriority` özelliği ve `_runPerf` alanı taşımaz (İş 2 koşu başındaki perf modunu kendi alanıyla yakalayacak).

- [ ] **Step 1: Dalı aç, tasarım paketini ve brief'i ilk commit olarak ekle**

```bash
git switch -c fix/resolve-follows-profile develop
git add ".claude/outputs/2026-10-15-16-54-design-v1.25.0" ".claude/outputs/2026-10-17-10-06-resolve-priority-and-resource-strip-decisions.md" ".claude/outputs/2026-10-17-10-44-resource-strip-and-resolve-priority-opus-prompt.md" ".claude/outputs/2026-10-17-11-40-resolve-follows-profile-plan.md"
git commit -m "docs: tasarim paketi v1.25.0 (kaynak seridi), Resolve oncelik olcumu ve kararlar, uygulama brief'i ve Is 1 plani"
```

- [ ] **Step 2: Yeni kuralı pinleyen KIRMIZI testleri yaz (bugünkü kodla derlenir)**

`RunCoordinatorTests` — mevcut `A_resolve_cycles_run_writes_no_cap_and_normal_priority_under_balanced` yeniden yazılır (ad: `A_resolve_cycles_run_applies_the_profile_cap_and_priority`), Theory: `Balanced` ve `Light`:
```csharp
await h.Sut.StartAsync(Start(RunMode.Cycles, parallelism: 4) with { PerfMode = mode.ToString() }, default);
await h.Sut.RunCompletion.WaitAsync(Limit);
Assert.Equal([.. Applied(mode), .. Released()], governor.Calls);
Assert.Equal(PerfProfile.For(mode).CpuCapPercent, Assert.Single(h.Events.OfType<RunStartedEvent>()).CpuCapPercent);
Assert.DoesNotContain("priority", h.DecisionLog, StringComparison.Ordinal);
```
Bugün KIRMIZI: governor `cap:off, prio:Normal` görür (`ResolveAtFullPriority` varsayılanı `true`).

`IpcMessagesTests` — mevcut iki test (~225-247) yeniden yazılır:
- `StartRun_json_carries_no_resolve_priority_field`: `IpcJson.Serialize(new StartRunCommand("r1", RunMode.Cycles, @"D:\repo", "Debug", 4))` çıktısı `resolveAtFullPriority` içermez. Bugün KIRMIZI (alan `true` ile yazılır).
- `An_old_apps_startRun_with_the_resolve_priority_field_still_parses`: alanı (`"resolveAtFullPriority":true`) içeren elle yazılmış bir startRun JSON'u deserialize olur; `RunId`, `Mode == Cycles`, `Parallelism` doğru. (Bugün de yeşil — silme sonrası toleransı pinler.)

`ResolveAtFullPrioritySettingsTests.cs` → sınıf `ResolveFollowsProfileSettingsTests` olarak yeniden yazılır (dosya adı da):
- `The_general_catalog_has_no_resolve_priority_row`: `GeneralSettingsCatalog.Groups` içinde etiketi `"Resolve cycles at full priority"` olan satır yok; `BUILD` grubunun satırları yalnız `GeneralSetting.PullBeforeBuild`. Bugün KIRMIZI. (Enum üyesine adla değil etiketle bakar, kaldırmadan önce de derlenir.)
- `An_old_ui_state_with_the_resolve_priority_field_still_loads`: geçici klasöre `{"PerfMode":"Light","ResolveAtFullPriority":false}` içerikli `ui-state.json` yazılır; `UiStateStore` ile `Load()` hata atmaz ve `PerfMode == "Light"`.
- `An_old_settings_file_with_the_resolve_priority_field_still_imports`: `resolveAtFullPriority` alanı olan bir settings JSON'u mevcut import yolundan (`SettingsFile` okuyucusu + `SettingsDraftViewModel.LoadFrom`) hatasız geçer ve dosyadaki başka bir anahtar (ör. `stashOnBranchSwitch: true`) taslağa uygulanır.

- [ ] **Step 3: Kırmızıyı göster**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --filter "FullyQualifiedName~A_resolve_cycles_run_applies_the_profile_cap_and_priority|FullyQualifiedName~StartRun_json_carries_no_resolve_priority_field|FullyQualifiedName~The_general_catalog_has_no_resolve_priority_row"`
Expected: üçü de FAIL (governor `cap:off`; JSON'da `resolveAtFullPriority`; katalogda satır var). Çıktının ilgili satırlarını rapora koy.

- [ ] **Step 4: Kaldırmayı yap**

Files listesindeki her nokta. Supervisor'da `ApplyPerfLocked(profile, …)` profili dönüştürmeden `_activePerf`'e yazar ve `WritePerfLocked`'a verir; Resolve decision.log satırı ve `_perfRunMode`/`_perfResolveAtFullPriority` kalkar. App'te koşu içi çip notu her modda `PerfNoteText.Note(profile)`; `OnRunStarted`'da kırpma notu ve uyarılar kalır, Resolve notu dalı kalkar. Yorumlardaki "Resolve notu" sıra cümleleri yeni gerçeğe göre düzeltilir. `grep -rn -E "ResolveAtFullPriority|resolveAtFullPriority|ForRun\(|ResolveNote\(|PriorityValue|full priority" src` boş dönmeli.

- [ ] **Step 5: Eski testleri yeni kurala göre yeniden yaz**

Her biri silinmez; adı ve iddiası yeni kurala döner, doc'una eski iddia + ölçüm gerekçesi yazılır. Tekrar eden "Resolve aynası" testlerinde Build testini `[Theory]` ile `RunMode.Cycles`'ı da kapsayacak biçimde genişletmek tercih edilir (plan: Cycles için `ResolvePlan()`, Build için mevcut plan):
- `PerfProfileTests` (~55-112): `ForRun`/`ResolveNote` testleri → `PerfNoteText.Note(PerfProfile.For(mode))` her mod için `parallelism: <n> · cpu cap <p>%` / Full'de `cpu cap off`; Resolve'a özgü metin yok.
- `RunCoordinatorTests`: `A_resolve_cycles_note_names_the_clamped_worker_count` → kırpılmış Resolve koşusunda decision.log'da yalnız `workers reduced to 2 (1 logical processor)` satırı, `priority` geçmez (`AssertNoteOnlyInDecisionLog` workers satırı için). `A_resolve_cycles_run_follows_the_profile_when_the_setting_is_off` → Step 2'deki Theory'ye katılır. `A_build_run_keeps_the_profile_even_with_the_setting_on` → `A_build_run_keeps_the_profile`. `A_mid_run_switch_in_a_resolve_cycles_run_stays_at_full_priority` → `A_mid_run_switch_in_a_resolve_cycles_run_applies_the_new_profile`: Full'de başlar, koşu içinde Light → `[.. Applied(Full), .. Applied(Light), .. Released()]`. `A_copy_contention_in_a_resolve_cycles_run_never_lowers_it` → Light'ta takılan kopya Build'deki copy-floor izini verir (mevcut Light + contention Build testinin beklenen izinin aynısı; o testi Theory'ye genişletmek tercih). `A_perf_change_arriving_while_a_resolve_run_is_planned_still_runs_at_full_priority` → planlama penceresinde gelen Light Cycles'ta da komuttakini ezer: `[.. Applied(Light), .. Released()]`, `runStarted.CpuCapPercent == 40`. `A_perf_change_during_the_graceful_drain_of_a_resolve_run_keeps_it_at_full_priority` → hemen üstündeki Build drain testinin aynı izi (`[.. Applied(Balanced), CapOff, CapOff, FloorPrio, .. Released()]`).
- `RunViewModelStateTests` (~750-1150): `PerfContextVm(engine, fullPriority)` parametresi kalkar. `A_resolve_run_at_full_priority_says_so_once_when_it_starts` → `A_resolve_run_writes_no_priority_note_when_it_starts` (Cycles ve Build: konsolda `priority` ve `(Resolve cycles)` yok). `A_perf_change_during_a_run_describes_what_the_engine_applies_to_it` → Cycles ve Build ikisi de `"parallelism: 2 · cpu cap 40%"`. `A_perf_change_while_a_resolve_run_is_opening_describes_that_run` → açılış koreografisi sırasında çip değişimi sıradan notu yazar. `A_perf_change_while_a_build_opens_after_a_resolve_start_writes_the_plain_note` → aynı iddia kalır (sıradan not), bağlam sızıntısı artık yok — doc'u güncellenir. `A_setting_changed_while_a_resolve_run_opens_does_not_reach_that_run` → `A_resolve_run_sends_the_chips_perf_mode_like_any_run`: gönderilen `StartRunCommand`'in `PerfMode`'u ve `Parallelism`'i çipin değeri. `A_clamped_resolve_run_names_the_reduction_before_its_full_priority_note` → kırpma satırı tek, ardından priority notu yok. `Run_start_warnings_follow_the_reduction_and_resolve_notes_in_the_console` → uyarılar kırpma notundan sonra gelir.
- `GeneralSettingChains.cs`: Resolve satırı kalkar; `GeneralSettingChainTests` ve `SettingsGeneralPageTests`'teki satır listeleri, UIA adları, hairline/indeks kuralları ve "beş/dokuz anahtar" sayıları yeni kataloğa göre (BUILD tek satır; toplam sekiz anahtar).

- [ ] **Step 6: Hedefli testleri koş**

Run: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj -c Release --filter "FullyQualifiedName~PerfProfileTests|FullyQualifiedName~RunCoordinatorTests|FullyQualifiedName~IpcMessagesTests|FullyQualifiedName~RunViewModelStateTests|FullyQualifiedName~GeneralSetting|FullyQualifiedName~ResolveFollowsProfileSettingsTests|FullyQualifiedName~SettingsGeneralPageTests|FullyQualifiedName~SettingsDraft|FullyQualifiedName~UiStateStore"`
Expected: PASS. Yeni testlerin gerçekten koştuğunu `--list-tests` ya da sonuç sayısıyla doğrula (bayat DLL tuzağı).

- [ ] **Step 7: Commit**

```bash
git add -A src tests
git commit -F <mesaj-dosyasi>   # "fix: Resolve cycles profilin cap ve priority'sini aynen uygular - 'Resolve cycles at full priority' ayari ve kurali kalkti (PerfProfile.ForRun, PerfNoteText.ResolveNote, startRun alani, General satiri); eski testler yeni kurali pinler, eski ui-state/settings/startRun alanlari yok sayilir"
```

### Task 2: Dokümanlar

**Files:**
- Modify: `ARCHITECTURE.md` — §5.2 (~556 `startRun` paragrafındaki `resolveAtFullPriority` cümlesi), §8.1 (~1398 "A `Cycles` run is also the one run that does not take…" paragrafı), §11.1 (~2859-2879: tablo altı Resolve notu, mid-run switch cümlesindeki Resolve istisnası, "A Resolve cycles run takes full priority" paragrafı → yeni kural + ölçüm gerekçesi; "Memory, not cores" paragrafına K2 CPU cümlesi), §11.3 (~2938 "a run with no cap — Full"), §13.3 (~4273, ~4285, ~4419-4424, ~4452 General satırları ve settings dosyası anahtar listesi), §16 (~6289 ui-state alan listesi), §17.5 ("under a real, full-priority run" → "under a run with no cap (Full)"), §22 (~7377 kod haritası satırı: perf tablosu + copy floor; Resolve kuralı çıkar)
- Modify: `README.md` — ~209 ayarlar listesindeki *Resolve cycles at full priority*, ~725-745 "Performance modes" (Resolve paragrafı kalkar, Full cümlesi eklenir)

**Interfaces:**
- Consumes: Task 1'in kodu (her cümle koda karşı doğrulanır).
- Produces: —

- [ ] **Step 1: ARCHITECTURE.md bölümlerini yerinde yeniden yaz**

Anlatı üslubu; "eskiden/şimdi" ve "bu oturumda" YOK. §11.1'deki yeni kural metni (İngilizce): bir `Cycles` run, Build/Rebuild/Clean gibi profilin cap'ini ve priority'sini aynen uygular; Resolve'u hızlandırmak isteyen Full'ü seçer. Gerekçe (dayanıklı dil, rakam gömmeden ya da yalnız ölçümün kendi sayılarıyla): gerçek workspace'te ölçüldü — sakin makinede cap'siz Normal Resolve kazandırmadı, yüklü makinede birkaç yüzde kazandırdı ve bedeli arayüzün saniyelerce kare verememesiydi. K2 cümlesi ("Memory, not cores" paragrafının sonuna): Full'de derleyiciler arayüzle aynı öncelikte koşar; yüklü makinede saniyelik kare boşlukları ölçüldü — Balanced'ın varsayılan olmasının CPU tarafındaki gerekçesi; arayüz uyarı göstermez.

- [ ] **Step 2: README.md'yi güncelle**

Ayar listesinden maddeyi çıkar; Performance modes'tan Resolve paragrafını çıkar, Full için tek cümle ekle (yoğun makinede arayüz takılabilir; varsayılan Balanced).

- [ ] **Step 3: Doğrula**

Run: `grep -n -i -E "full priority|resolveAtFullPriority|ForRun|ResolveNote" ARCHITECTURE.md README.md CLAUDE.md`
Expected: boş. Her değişen cümleyi koda karşı oku.

- [ ] **Step 4: Commit**

```bash
git add ARCHITECTURE.md README.md
git commit -m "docs: Resolve profili izler - ARCHITECTURE 5.2, 8.1, 11.1 (yeni kural + olcum gerekcesi, Full CPU cumlesi), 11.3, 13.3, 16, 17.5, 22; README Performance modes ve ayar listesi"
```

---

## Bitiş (controller)

1. Tam süit (`--filter "Category!=Acceptance"`, Release) çıktısı dosyaya; kırmızı varsa tek başına tekrar + değişmemiş develop'la kıyas (UpdatePillTests DPI, StickyLayerHeaderClickTests ortam etkisi bilinen kararsızlıklardır).
2. `develop`'a `--no-ff` merge (`merge: …` mesajı), push; merge doğrulanınca branch local + remote silinir.
3. CI (`ci.yml`) sonucu kontrol edilir.
