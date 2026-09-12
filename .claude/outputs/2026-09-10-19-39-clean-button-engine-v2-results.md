# Clean Butonu Motoru — Güncel main Üzerine Taşıma Sonucu (v2)

> Bu dosya bir **kayıttır**, prompt değil. 2026-08-19 dörtlüsünün (plan / uygulama promptu / merge promptu /
> sonuç kaydı) devamıdır: orijinal `feat/clean-button-engine` branch'i `main`'in 162 commit gerisinde kaldığı
> için iş, güncel `main` üzerine yeniden oturtuldu.

| | |
|---|---|
| **Plan** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-opus-prompt.md` |
| **Orijinal sonuç kaydı** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-results.md` |
| **Güncel merge promptu** | `.claude/outputs/2026-09-10-19-39-clean-button-engine-v2-merge-prompt.md` |
| **Branch** | `feat/clean-button-engine-v2` (yalnız LOCAL — remote'a push EDİLMEDİ) |
| **Taban** | `main` @ `a0b0ae5` |
| **Eski branch** | `feat/clean-button-engine` — DOKUNULMADI; içeriği v2'ye taşındı, v2 merge edilince silinebilir |
| **Durum** | Tamamlandı, tam süit yeşil, **merge EDİLMEDİ** — kullanıcı kararı: "localda bir branch olsun" |

---

## Yöntem

`main`'den yeni branch açıldı; orijinal 7 commit task sırasıyla (T1 ‖ T2 → T3 → … → T7) cherry-pick edildi.
Her task'ta çakışma güncel koda uyarlandı, derlendi ve o task'ın süiti koşuldu; sonda tam süit. Yeni
davranış getiren her uyarlama için önce KIRMIZI test gösterildi (mutasyonla), sonra yeşil.

## Commit'ler (task → commit → not)

| Task | Commit | Not |
|---|---|---|
| T1 | `a4b2374` | `IpcMessages`: Clean komut/event'leri `main`'in `PullCompletedEvent`/`Behind` eklemeleriyle yan yana |
| T2 | `4dd1f80` | çakışmasız |
| T2+ | `659a70a` | `RemoveUnderRoot` defterin tek yazma yolunu (`Write(mutate)`, `main`'de yeni) kullanır — kopya yasak |
| T3 | `76c020d` | çakışmasız; `WorkspaceScanner` API'si hâlâ uyumlu |
| T3+ | `5f9b4a3` | D8 izin satırı (`NoSleepPollTests`) — orijinalde docs commit'indeydi, sleep'i getiren task'ın yanına alındı |
| T4 | `5b1dc1d` | `SupervisorHost`: `PullRepositoryAsync` + `CleanWorkspaceAsync` ikisi de; `IsRunActive` doc'u `TryRequestStop`'un doc'undan ayrıldı; `CleanDispatchTests` yeni `SourceHashCache` parametresini verir |
| T5 | `42efbb4` | en çok uyarlanan parça — bkz. sapmalar 6–9 |
| T6 | `a11b3d6` | çakışmasız |
| T6+ | `42fa76d` | `CleanSolutionTooltip` yorumu bakım kutusu Clean'inin gerçek kapsamını söyler |
| T7 | `3bbccf0` | ARCHITECTURE + README (ultracode: yazım → 2 eleştirmen → düzeltme döngüsü) + `OperationLabel.Clean` yorumu |

## Plandan / orijinal branch'ten sapmalar (kümülatif)

Orijinal kayıttaki dördü aynen geçerli (branch adı, `NoSleepPollTests` satırı, `FormatBytes` serviste `public`,
junction testi koşuyor). Bu taşımada eklenenler:

5. **`RemoveUnderRoot` → `Write(mutate)`.** `main` bu arada `Upsert`/`Remove`'u ortak `Write` yardımcısına
   çıkarmıştı; cherry-pick'lenen metot aynı load→temp→rename döngüsünü inline tekrar ediyordu.
6. **`ClearConsoleBuffers` alınmadı.** `main`'de aynı iş `ClearConsoleForNewOperation()` +
   `ClearStreamForNewOperation()` olarak zaten var; `CleanAsync` bu ikisini `SyncCoreAsync(clearBuffers:true)`
   ile aynı sırada çağırır.
7. **K-8 "event stream korunur" kararı değişti.** Gerekçesi "mevcut sözleşme" idi; o sözleşme design v1.13.2
   §9 ile "konsol + stream her işlemde temizlenir" oldu. Clean de bu kurala uyar — test:
   `Clean_clears_the_stream_left_over_from_the_previous_operation`.
8. **İşlem pill'i.** `CleanAsync` tıklamada `CurrentOperation = OperationLabel.DeepClean` yazar (menüdeki
   `CLEAN`'den ayrı sözcük) — test: `Clean_writes_the_deep_clean_operation_pill_at_click_time`.
   `OperationLabel.DeepClean`/`Clean` yorumları gerçek kapsama çekildi (artifacts/NuGet iddiası kalktı).
9. **`N behind` chip'i de bakım kilidine tabi.** `CanPullRepository` `!CleanBusy` görür ve
   `PullRepositoryCommand` `NotifySyncGatedCommands` listesindedir (zaten `SyncBusy`'ye bağlıydı) — test:
   `The_pull_chip_is_disabled_while_a_clean_is_in_flight_and_reopens_on_completion`.

## Açık karar (kullanıcıya)

Şeritteki pill Clean sırasında `DEEP CLEAN` yazar ama **amber yanmaz**: `StickyRibbon.RefreshOpPill`'in
"canlı" ölçütü `IsMidRunLocked || Phase == Syncing`'dir ve K-7 gereği Clean yeni faz açmaz. Doküman kodu
anlatır (pill kimliği taşır, canlı durum konsol transkriptindedir). Amber istenirse: VM'de `CleanBusy`'yi
dışarı açan bildirimli bir özellik + ribbon'da canlı ölçütüne dahil etme + realize/ribbon testi — küçük iş,
planın "görsel tasarım kapsam dışı" kararı gereği bu turda yapılmadı.

## Doğrulama

- **Tam süit (final, `Category!=Acceptance`):** Başarısız 0 · Başarılı 2611 · Atlanan 1 · Toplam 2612.
  Atlanan `DragReorderTests.Reorder_uses_mouse_capture_…` — ortam kaynaklı bilinen skip. İlk geçişte
  `JobCpuRateTests.Cpu_hard_cap_holds_under_a_saturating_child` de atlanmıştı (ajanlar koşarken makine yükü);
  final geçişte koştu. `Category=Acceptance` süiti koşulmadı.
- **Karar doğrulaması (ultracode, salt-okur, 3 ajan):** K-1…K-10 + ek kontroller → 29 bulgu, **hepsi
  "holds"** (Core 9 · Supervisor-IPC 9 · App 11); kanıtlar dosya:satır olarak workflow journal'ında.
- **Doküman:** yazım ajanı 13 bölüm; iki eleştirmen (iddia-kod / üslup) 16 sorun (bayat "still waits for its
  own engine", yanlış "third icon", pill'in Clean'de amber yandığı iddiası, README'nin ARCHITECTURE'ı
  tekrarlaması, tekrarlanan `cleanRejected` gerekçesi…); düzeltme döngüsü iki tur: 1. tur sonrası 1 önemli
  ("today" zaman dili) + kozmetikler, 2. tur sonrası iddia-kod eleştirmeni sıfır ciddi sorun, üslup
  eleştirmeni 1 önemli (§5.2'nin §13.2'deki tanımla örtüşmesi — bilinçli bırakıldı: §5.2 motor sözleşmesini,
  §13.2 UI davranışını anlatır, aralarında karşılıklı gönderme var) + kozmetikler; kozmetikler elle uygulandı
  (`-t:Clean` yazımı, virgül eklemi, "(above)" göndermesi, "between them"). Bu işten ÖNCE var olan boşluk,
  dokunulmadı: §5.2/§5.3 listelerinde `pullRepository`/`pullCompleted` yok.

## `feat/optimize-button-engine` ile çakışma yüzeyi

Orijinal kayıttaki liste geçerli (`IpcMessages` whitelist'leri, `WorkspaceServices` record'u + dispatch,
`RunViewModel*.cs` bildirim zincirleri, `MaintenanceBox.xaml.cs` foreach, `AccessibilityNames`,
`NoSleepPollTests.AllowedSleeps`). Bu taşımayla eklenenler: `NotifySyncGatedCommands`'taki Pull satırı,
`CanPullRepository`, `OperationLabel` yorumları, `CleanDispatchTests`/`SyncStreamingTests`'in
`SourceHashCache` parametresi. Optimize branch'i de `main`'in aynı 162 commit gerisindedir; aynı yöntemle
(`main`'den v2 branch + task sırasıyla cherry-pick) taşınmalıdır.
