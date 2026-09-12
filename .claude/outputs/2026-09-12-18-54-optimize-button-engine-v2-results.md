# Optimize Butonu Motoru — Güncel main Üzerine Taşıma Sonucu (v2)

> Bu dosya bir **kayıttır**, prompt değil. 2026-08-19 dörtlüsünün (plan / uygulama promptu / merge promptu /
> sonuç kaydı) devamıdır: orijinal `feat/optimize-button-engine` branch'i `main`'in 186 commit gerisinde
> kaldığı için iş, güncel `main` üzerine yeniden oturtuldu. Yöntem `feat/clean-button-engine-v2` ile aynıdır.

| | |
|---|---|
| **Plan** | `.claude/outputs/2026-08-19-22-18-optimize-button-engine-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-22-18-optimize-button-engine-opus-prompt.md` |
| **Orijinal sonuç kaydı** | `.claude/outputs/2026-08-19-22-18-optimize-button-engine-results.md` |
| **Branch** | `feat/optimize-button-engine-v2` |
| **Taban** | `main` @ `5100d2f` |
| **Eski branch** | `feat/optimize-button-engine` — DOKUNULMADI; içeriği v2'ye taşındı |
| **Durum** | Tamamlandı, tam süit yeşil, **merge EDİLMEDİ** — kullanıcı kararı: branch'te kalsın |

---

## Yöntem

`main`'den yeni branch açıldı; orijinal 8 commit task sırasıyla taşındı (T1 ‖ T2 ‖ T3 → T4 → … → T8). Her
task'ta çakışma güncel koda uyarlandı, o task'ın süiti koşuldu ve commit atıldı; sonda tam süit. Yeni davranış
getiren her uyarlama için önce KIRMIZI gösterildi (mutasyonla ya da derleme hatasıyla), sonra yeşile geçildi.

Taşımadan önce salt-okur bir **keşif turu** koşuldu (11 ajan: 8 commit + App/Core/test yüzey haritaları). İki
sessiz kırılma o turda bulundu ve ikisi de gerçekleşti — aşağıdaki 1 ve 2 numaralı maddeler.

## Commit'ler (task → commit)

| Task | Commit | Not |
|---|---|---|
| T1 | `3f477eb` | IPC sözleşmesi; komut harici kök listesini de taşır, completed event üçüncü defteri de sayar |
| T2 | `2d6b3e2` | `RootScope` + `TempFileSweeper`; üç defterde prune + sweep; `RemoveUnderRoot` ortak kapıya bağlandı |
| T3 | `8c2f7f9` | `RestoreAsync` + ortak `OpenScope`; **hedef düşüren cherry-pick tuzağı** kapatıldı + guard |
| T4 | `06afb2c` | `OptimizeWorkspaceService`; harici kökler, üçüncü defter, `ByteFormat` terfisi |
| T5 | `5dc1496` | Supervisor wiring; **ikinci `IsRunActive` tuzağı** temizlendi; test harness'i ortaklaştırıldı |
| T6 | `b7967d5` | App VM: komut, kapılar, karşılıklı dışlama, konsol/stream, pill |
| T7 | `7b9de96` | Bakım kutusunda Optimize canlı + amber/spinner |
| T8 | `85fe7cd` | ARCHITECTURE + README; iki mevcut çelişki ve iki kod-doc uyuşmazlığı da giderildi |

---

## Cherry-pick'in iki sessiz tuzağı

Bunlar bu taşımanın en önemli bulgularıdır: ikisinde de git "başarılı" der, biri derlemeyi patlatır, öteki
**tam süiti yeşil bırakarak** bir davranışı sessizce bozardı.

**1. Build hedefi düşüyordu (T3).** Ağustos'taki `InvokeAsync` gövdesi
`MsBuildArguments.Build(projectId, configuration, baseIntermediateOutputPath)` çağırıyordu; `Target`
parametresi main'e o commit'ten SONRA geldi. Otomatik birleşim bu gövdeyi çakışma bildirmeden uyguladı. Sonuç:
satır menüsünün **Rebuild** ve **Clean** maddeleri sessizce `-t:Build` koşacaktı ve **hiçbir mevcut test
kırmızı olmayacaktı** — tek proje koşusunun iddiaları kaydedilen request + `PlanFor` üzerinden kurulur,
invoker'ın ürettiği komut satırını gözleyen bir dikiş yoktur. Gövde elle yazıldı (`PlanFor` korundu) ve boşluk
kalıcı bir guard'la kapatıldı: `The_invoker_never_picks_the_build_target_itself`. Guard mutasyonla doğrulandı.

**2. İkinci `IsRunActive` (T5).** `RunCoordinator` hunk'ının bağlam satırları main'de birebir aynı olduğu için
3-way merge onu çakışmasız uyguladı ve tip ikinci bir `IsRunActive` kazandı (CS0102). Dosya geri alındı;
yalnız doc'u iki tüketiciyi (Clean + Optimize) anlatacak şekilde güncellendi.

---

## Plandan / orijinal branch'ten sapmalar

Ağustos kaydındaki altı sapma aynen geçerlidir (sahte-exe harness'i yok, `IsUnderBin` public, dedupe testi
yazılmadı, iki test kurgusu farklı, D8 allow-list satırı, birleşik `MaintenanceBoxTests` pini bölündü). Bu
taşımada eklenenler:

**7. Harici kökler kapsama alındı (kullanıcı kararı).** Ağustos'ta harici kök özelliği yoktu. Bugün Sync ve
Clean kayıtlı harici kökleri kapsıyor ve CLAUDE.md "harici köklerden gelen projeler sıradan projelerdir"
diyor. Optimize da kapsıyor: komut `ExternalProjects` taşır, servis `ExternalWorkspaceResolver.Resolve` ile
birleşik çalışma alanını tarar, çözülemeyen kart isimli bir uyarı yazar ve ana kök yine onarılır. Defter
budaması kök BAŞINA yürür (defter anahtarı tam dosya yoludur, ana kökün öneki harici kökü kapsamaz).
Testler: `External_projects_are_scanned_and_restored_like_any_other_project`,
`An_external_card_that_resolves_to_nothing_warns_and_the_main_root_is_still_repaired`. Mutasyonla kırmızı
gösterildi.

**8. Üçüncü defter hijyene girdi (kullanıcı kararı).** `source-hash-cache.json` branch yazıldıktan sonra
geldi, aynı geçici-ad desenini yazıyor ve KAYNAK DOSYA yollarıyla anahtarlı olduğu için en hızlı biriken
defter o. Prune + sweep ona da uygulandı; `OptimizeCompletedEvent` ayrı bir `PrunedSourceHashEntries` sayacı
taşır (sayısı ötekilerle aynı büyüklükte değildir).

**9. `ByteFormat` terfi etti, ama Clean'in görünen metni korundu.** Plan terfiyi söylüyordu; Ağustos'taki
`ByteFormat.Size` birim üstünde HER ZAMAN tek ondalık yazıyordu (`15.0 KB`), main'in `FormatBytes`'ı ise
10'un üstünde ondalık yazmıyor (`15 KB`). Terfide main'in kuralı korundu (çalışan bir metni sessizce
değiştirmemek), negatif girdinin `0 B`'ye clamp'lenmesi Optimize'dan devralındı. `CleanWorkspaceService.FormatBytes`
silindi, üç çağıran tek kaynağa bağlandı. Yeni kural teste pinlendi ve eski iddia doc'a yazıldı.

**10. `NotAvailableSuffix` SİLİNMEDİ.** Ağustos kaydı "son kullanıcısı Optimize'dır, const silinir" diyordu.
Bugün ikinci bir kullanıcısı var: Build menüsünün **Clean Solution** maddesi ve onun arka ucu hâlâ yok. Const
kaldı; doc'u bakım kutusuna değil o maddeye işaret edecek şekilde düzeltildi.

**11. Bakım kutusunda amber + spinner.** Ağustos'taki T7 bunu yapmıyordu çünkü o tarihte kutunun meşgul
boyaması yoktu. Main'de var ve kuralı "koşan iş, düğmesinin olduğu yerde amber yanar". Optimize da bağlandı;
bu yüzden `OptimizeBusy` VM'de **public + bildirimli** yazıldı (Ağustos'ta private'tı). Kutunun doc'undaki
"Optimize'ın gösterecek bir işi yoktur" cümlesi ve onu pinleyen test yeni davranışa göre yeniden yazıldı.

**12. Karşılıklı dışlama ve konsol/stream ortaklığı.** Clean main'e önce girdiği için koordinasyon tablosunun
"ikinci gelenin işi" maddeleri Optimize'a düştü: `CanClean` Optimize'ı görür, `CanSync`/`CanRebuildOrRetry` ve
`N behind` chip'i de öyle; çift kapı testi yazıldı. Optimize'ın kendi `ClearConsoleBuffers` helper'ı
ALINMADI — main'de aynı iş `ClearConsoleForNewOperation` + `ClearStreamForNewOperation` ikilisiyle yapılıyor.
Stream tonu Clean'inkiyle aynı (`StreamKind.Sync`): iki bakım işi aynı kefededir.

**13. Optimize bitişte Sync ZİNCİRLEMEZ ve listeyi boşaltmaz.** Clean ikisini de yapar çünkü çıktıları siler
ve ekrandaki kararlar geçersizleşir. Optimize hiçbir projeyi dirty yapmaz (imza kaynak-tabanlıdır), bu yüzden
yenilenecek bir karar yoktur. Karar VM'de doc'landı.

**14. Test harness'i ortaklaştırıldı (kopya yasağı).** `CollectingStream`, `StdinWith`, süre sınırı ve klasör
temizliği `Supervisor/SupervisorHostHarness.cs`'e; NDJSON ayrıştırması `Supervisor/NdjsonWire.cs`'e çıktı.
`CleanDispatchTests`'teki ikinci `ParseWire` kopyası da silindi. `SupervisorHost.Default`'ta defter yolları
tek yerel değişkene alındı.

**15. Kök normalizasyonunun never-throw sözleşmesi pinlendi.** Mantık `RemoveUnderRoot`'un içinden
`RootScope`'a taşınırken catch kümesi daralsaydı sözleşme sessizce kırılırdı ve bunu tutan tek bir test yoktu.
`A_root_that_cannot_be_resolved_touches_nothing_and_throws_nowhere` eklendi; mutasyonla (try/catch kaldırılıp)
kırmızı gösterildi. `RootScope` main'in `Path.TrimEndingDirectorySeparator` semantiğini kullanır — Clean'in
sürücü-kökü davranışı değişmedi.

---

## Bilinçli olarak YAPILMAYANLAR

- **Üç workspace handler'ının ortak kabuğa çıkarılması.** `SyncWorkspaceAsync`, `CleanWorkspaceAsync` ve
  `OptimizeWorkspaceAsync` benzer bir iskelet taşır (kapı → `Emit` köprüsü → tanımlı hataya çeviren catch-all).
  CLAUDE.md'nin kopya yasağı **değer, metin ve primitif** içindir; bunlar farklı kodlar ve farklı gövdeler
  taşıyan üç metottur ve Sync'te kapı hiç yoktur. Soyutlama bu işin kapsamını aşar ve shipped iki akışa
  dokunurdu — ayrı bir iş olarak durur.
- Ağustos planının v1-sonrası adayları: run log yaşlandırma, SDK-style projeler için düz `-t:restore`,
  HintPath↔packages.config sürüm-drift onarımı, paralel restore, worktree havuzunda orphan dizin tespiti,
  Optimize'ın iptal komutu.

## Doğrulama

- **Tam süit (final, `Category!=Acceptance`):** Başarısız 0 · Başarılı 2715 · Atlanan 1 · Toplam 2716.
  Atlanan `DragReorderTests.Reorder_uses_mouse_capture_…` — ortam kaynaklı bilinen skip, bu işten önce de
  atlanıyordu. `Category=Acceptance` süiti koşulmadı.
- Her task'ta önce kırmızı gösterildi: T1/T2/T4/T6 derleme hatası ya da assert ile, T2 (never-throw),
  T3 (hedef guard'ı) ve T4 (harici kökler) ayrıca **mutasyonla**.
- D8 sleep guard'ı T4'ten sonra gerçekten kırmızı verdi; gerekçeli allow-list satırı ondan sonra eklendi.
- **Yapılmayan doğrulama:** uygulama gerçek pencerede açılıp Optimize'a basılmadı. Doğrulama test düzeyindedir.

## `feat/tray-build-animation-v2` ile çakışma yüzeyi

O branch de merge bekliyor ve `NoSleepPollTests.AllowedSleeps` ile `RunViewModel`'e dokunuyor. İkinci merge
olan taraf o iki küçük çakışmayı çözer; bu işi etkilemez.
