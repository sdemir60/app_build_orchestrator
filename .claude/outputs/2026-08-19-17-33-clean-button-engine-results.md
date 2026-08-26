# Clean Butonu Motoru — Uygulama Sonucu

> Bu dosya bir **kayıttır**, prompt değil. Plan/opus-prompt/merge-prompt üçlüsüyle aynı tarih-saat önekini
> taşır çünkü aynı işin parçasıdır; gerçek uygulama tarihi 2026-08-26'dır.

| | |
|---|---|
| **Plan** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-opus-prompt.md` |
| **Merge promptu** | `.claude/outputs/2026-08-19-17-33-clean-button-engine-merge-prompt.md` |
| **Branch** | `feat/clean-button-engine` (yalnız LOCAL — remote'a push EDİLMEDİ) |
| **Taban** | `main` @ `49ed712` |
| **Durum** | Tamamlandı, tam süit yeşil, **merge EDİLMEDİ** — branch bilinçli olarak duruyor |

---

## Commit'ler (task başına bir commit; her birinde önce kırmızı test gösterildi)

| Task | Commit | İçerik |
|---|---|---|
| T1 | `973162f` | Contracts: `cleanWorkspace` komutu + `cleanStarted`/`cleanProgress`/`cleanCompleted` event'leri |
| T2 | `ce99626` | `BuildStateStore.RemoveUnderRoot` — workspace-scoped state temizliği |
| T3 | `0896b2c` | `Core/Workspace/CleanWorkspaceService.cs` — akışın tamamı |
| T4 | `1c660c1` | Supervisor dispatch + `RunCoordinator.IsRunActive` → `cleanRejected` |
| T5 | `50e2413` | App VM: `CleanCommand`, kapılar, `ClearConsoleBuffers()`, stream özeti |
| T6 | `8398ffa` | `MaintenanceBox`: `PART_Clean` komuta bağlandı, tooltip yeniden yazıldı |
| T7 | `9a8c6ed` | ARCHITECTURE + README |

Plandaki bağımlılık sırası (T1‖T2 → T3 → T4 → T5 → T6 → T7) aynen izlendi. **Eksik task yok.**

## Bağlayıcı kararlar nereye düştü

Merge promptunun "Bu işe özel dikkat" listesinin doğrulama karşılıkları:

| Karar | Nerede |
|---|---|
| K-1 — `/t:Clean` YOK, yalnız FS silme | `CleanWorkspaceService`'te MSBuild referansı hiç yok; gerekçe `CleanWorkspaceCommand` XML-doc'unda ve ARCHITECTURE §5.2'de |
| K-2 — build-state workspace-scoped | `BuildStateStore.RemoveUnderRoot`; `evaluation-cache.json`'a dokunulmuyor |
| K-3 — taze `WorkspaceScanner.Scan` | `CleanWorkspaceService.Run`; `CsprojEvaluator`/`EvaluationCache` bağımlılığı ALINMADI |
| K-4 — komut döngüsü bloklanır + `cleanRejected` | `SupervisorHost.CleanWorkspaceAsync` (senkron `Run`), `RunCoordinator.IsRunActive` (`_finishing` dahil değil) |
| K-5 — ayrı event üçlüsü | `syncProgress` yeniden kullanılmadı; App'te ayrı bayrak (`_cleanInFlight`) |
| K-6 — kilitli dosya hata değil | `CleanWorkspaceService.DeleteFile` → `LockedFileCount`; exception IPC sınırını geçmiyor |
| K-7 — App kapıları | `CanClean` + `CleanBusy`; `CanSync`/`CanRebuildOrRetry`'a `&& !CleanBusy`; yeni `AppPhase` AÇILMADI |
| K-8 — konsol sıfırlama tek yerde | `RunViewModel.ClearConsoleBuffers()`; `BeginRunAsync` ve `CleanAsync` ondan çağırıyor |
| K-9 — ÖNCE state SONRA klasör | `CleanWorkspaceService.Run` adım sırası; `Clean_resets_the_build_state_before_deleting_folders` emit anında diski gözleyerek pinliyor |
| K-10 — yalnız bin/obj | `IsSafeOutputFolder` savunma kapısı (kök altı + son segment bin/obj); worktree havuzu ve `_obj` kök dışında kaldığı için dokunulmuyor |

## Plandan sapmalar

1. **Branch adı.** Plan T0 `feature/clean-engine` öneriyordu; merge promptu `feat/clean-button-engine` diye
   sabitlemiş. Merge promptu esas alındı.
2. **`NoSleepPollTests` izin listesine bir satır eklendi** (`CleanWorkspaceService.cs` = 1). D8 guard'ı
   `DefaultDeleteRetryDelay`'in üretim `Thread.Sleep`'ini yakaladı. Gerekçe `BuildStateStore.RenameRetryDelay`
   ile birebir aynı: beklenecek handle/TCS yok (başka bir process'in dosya handle'ını kapatması bekleniyor),
   gecikme enjekte edilebilir (`DeleteRetryDelay`) ve testlerde anında dönüyor. **Eşik gevşetme değil** —
   guard'ın kendi sözleşmesi gereği yeni bir gerekçeli kullanımın kayda geçirilmesi.
3. **`CleanWorkspaceService.FormatBytes` `public`.** Plan "Core'da mevcut byte-formatlayıcıyı ARA ve yeniden
   kullan" diyordu; **projede böyle bir formatlayıcı yoktu** (`WorktreeManager` yalnız ham `SizeBytes`
   taşıyor, `Worktree.DiskSizeBytes`'ın hiç tüketicisi yok). Planın "yoksa serviste tek statik" dalı
   uygulandı; App'in `StreamText.CleanCompleted`'ı da onu çağırdığı için `internal` yetmedi.
4. **Junction testi atlanmadı.** `Clean_deletes_a_reparse_point_without_recursing_into_its_target` bu
   makinede gerçekten koştu (`mklink /J` çalışıyor); `Skip.IfNot` kapısı yine de duruyor.

## Öteki branch'lerle çakışma yüzeyi

`feat/optimize-button-engine` merge edilirken çakışması BEKLENEN yerler:

- `IpcMessages.cs` — komut/event `JsonDerivedType` whitelist'leri (ikisi de sona ekliyor).
- `SupervisorHost.cs` — `WorkspaceServices` record'u (Clean 4. parametreyi ekledi) ve dispatch switch'i.
- `RunViewModel.cs` / `.Workspace.cs` — `NotifySyncGatedCommands()` listesi ve
  `[NotifyCanExecuteChangedFor]` zincirleri (Clean her ikisine de bir satır ekledi).
- `MaintenanceBox.xaml.cs` — kalıcı-disabled foreach'ten Clean çıkarıldı, geriye yalnız `PART_Optimize`
  kaldı; Optimize motoru geldiğinde o satır tamamen kalkar.
- `AccessibilityNames.cs` — `NotAvailableSuffix` artık YALNIZ `OptimizeTooltip`'te kullanılıyor.
- `NoSleepPollTests.AllowedSleeps` — Optimize da bir retry backoff'u getirirse aynı sözlüğe satır ekler.

## Doğrulama (2026-08-26)

```
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
→ Başarısız: 0 · Başarılı: 2131 · Atlanan: 1 · Toplam: 2132
```

Atlanan tek test `DragReorderTests.Reorder_uses_mouse_capture_...` — bu işten BAĞIMSIZ, önceden de atlanan
ortam kaynaklı skip (`Mouse.PrimaryDevice.ActiveSource` null). `Category=Acceptance` süiti koşulmadı.
