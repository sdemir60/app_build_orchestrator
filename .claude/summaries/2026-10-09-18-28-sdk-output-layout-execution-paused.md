# SDK-style çıktı yolu + dürüst atlama satırı — uygulama (ara verildi)

Plan: `.claude/outputs/2026-10-09-17-06-sdk-output-layout-and-honest-skip-plan.md` — `superpowers:executing-plans` ile inline
uygulanıyor. Branch `feat/sdk-output-layout` (develop `5e619897`'den), ana checkout, worktree yok. Ayrıntılı kayıt (ledger,
Ruling'ler, kanıtlar): `.superpowers/sdd/2026-10-09-17-06-sdk-output-layout-and-honest-skip-plan/progress.md` (git-ignored).

## Task 1 — tamam (`92eaa0ea`, filtreli 435/435 yeşil)

Hükmü verilmiş (NoProgress/CapReached) grupta bayat kalan taşınan üye artık `succeeded (0ms)` değil
`skipped — cycle did not converge at this signature (carried record discarded: …)`; defteri kanıtsız geçersizlenir,
`CycleUnconverged` yalnız NoProgress'te true. App satırı hemen `never built` okur (`NextPreview.AfterUntrustedResult`),
sayfa metni "was not compiled and its record was discarded". Önemli Ruling: planın dalı kesilen (Stop) gruptaki taşınan
üyeyi de "skipped" yapardı — kapı `decision != Continue`; kesilen kola decision.log pini eklendi, planın literal koduyla
kırmızı görüldü. ARCHITECTURE §5.3, §7.5, §8.8, §13.2, §14.3, §22 ve README döngü paragrafı güncellendi.

## Task 2 — kod + test + doküman yazıldı, COMMIT YOK

SDK-style projeye SDK varsayılan çıktı yolu (`bin\<Cfg>\<tfm>\<Asm>.dll`), yalnız düzeni oynatan hiçbir şey yokken; evaluation
cache şeması 2. Plana eklenen Ruling'ler: A2 listesi genişledi (Platform, TargetName, TargetExt, TargetFrameworkVersion;
props/targets'ta AssemblyName/OutputType/TargetFramework; csproj Import/Sdk elemanı; Choose/koşullu gruplar), yalnız .NET SDK
ailesi, TFM klasörü küçük harf, ve önbellek açığı: EvaluationCache artık bakılan Directory.Build.* dosyalarının damgasını
(`FileStamp`, `EvaluatedProject.LayoutInputs`) tutar — props sonradan değişir/belirirse yeniden değerlendirir. Her biri için
kırmızı test ya da mutasyon görüldü.

**Açık:** tam süit (yarım kaldı) 15 test düşürdü — `SyncWorkspaceServiceTests` (13) ve `ExternalSyncIntegrationTests` (2);
hepsi SDK-style varsayılan düzenli fixture projesinin artık kanıt yolu alıp diskte çıktısı olmamasından (OutputMissing).
İkisi eski kuralı açıkça pinliyor (`A_project_without_derivable_output_is_decided_as_today`,
`An_sdk_style_project_reads_never_built_after_clean_with_no_derivable_output_path`) — yeni kuralı pinleyecek biçimde yeniden
yazılacak; geri kalanı için hazırlama yardımcısının çıktıyı da yazması (Y) ya da fixture'ın kanıt dışı kalması (X) arasında
karar verilecek.

## Kalan

Task 3 (acceptance + gerçek OSYS ölçümü), Task 4 (dep-issue koruması), Task 5 (doküman kontrolü, tam süit + acceptance,
develop'a merge + push, branch silme, CI), en güçlü modelle whole-branch review, final mesaj.
