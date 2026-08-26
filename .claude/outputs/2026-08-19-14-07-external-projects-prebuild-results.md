# Harici Projeler Ön-Derleme — Uygulama Sonucu

> Bu dosya bir **kayıt**tır: planla kodun ayrıldığı yerleri, merge edecek oturumun bilmesi gereken kararları
> ve öteki branch'lerle çakışma yüzeyini tutar. Plan ve uygulama promptu ile aynı adı taşır
> (`2026-08-19-14-07-external-projects-prebuild-*`).

| | |
|---|---|
| **Branch** | `feat/external-projects-prebuild` (yalnız LOCAL — remote'a push edilmedi) |
| **Taban** | `main` @ `49ed712` |
| **Commit** | 18 |
| **Diff** | 68 dosya, +4467 / −182 · 38 yeni dosya · 29'u test |
| **Plan** | `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-opus-prompt.md` |
| **Merge promptu** | `.claude/outputs/2026-08-19-14-07-external-projects-prebuild-merge-prompt.md` |

## Kapsam: planın beş fazı da uygulandı

| Faz | Commit'ler | Ne geldi |
|---|---|---|
| 1 — Core | `51545bb` `672ff66` `018d321` `f22ddc9` `7b07187` `5583083` | `ExternalProject`/`VcsKind`, kök keşfi, katman sabitleri, hedef önerisi, imza, will-build kararı, ff-only git güncelleyici, TFVC yüzeyi + `TfResolver`, paylaşılan `VsWhereLocator` |
| 2 — IPC + planlayıcılar | `aa16219` `b7dd895` `f32dd74` `0a6fe6e` | Komutlara harici liste, node'a VCS rozeti, `ExternalSyncInspector`, `ExternalNodeBuilder`, Sync entegrasyonu, `ExternalRunPlanner` + `ExternalPreparationException`, metin sözlüğü |
| 3 — Supervisor | `c3cda01` `4b4c059` `c3b435f` `6dba28a` | Kaynak guard'ı, harici MSBuild argümanları + `MsBuildArguments.PlanFor`, koşuda seri harici faz, giriş noktalarına bağlama |
| 4 — App | `c57d09c` `65206b9` | Kalıcılık, komut taşıma, Ayarlar taslağı, EXTERNAL PROJECTS bölümü |
| 5 — App | `af2fd22` `17a41cb` | Harici satırlar, sha yuvası, grup pinleri, dokümanlar |

Bağlayıcı kararların (D1–D13) hepsi uygulandı; hiçbiri esnetilmedi.

## Plandan sapmalar — dördü de bilinçli

Merge eden oturum bunları "eksik task" ya da "plan dışına taşma" diye raporlamamalı; gerekçeleri burada.

### S1. `GitCommandExecutor` → `Core/Processes/CommandLineTool.cs` (commit `7b07187`)

**Plan ne diyordu:** TFVC yüzeyinde "süreç başlatma hataları `GitCommandExecutor` gibi sarılır" (Faz 1.5, D8).

**Ne yapıldı:** sarmalayıcı `Core/Git/GitCommandExecutor.cs`'ten `Core/Processes/CommandLineTool.cs`'e taşındı
ve hata metnindeki araç adı parametreleşti (`CommandLineTool.Git` / `CommandLineTool.Tf` sabitleri).

**Neden:** plan "gibi" diyor, yani ikinci bir kopya ima ediyordu — CLAUDE.md kopya yasağı buna izin vermez.
Kopyalamamanın tek alternatifi mevcut sarmalayıcıyı olduğu gibi kullanmaktı, ama o zaman TFVC hatası
kullanıcıya `the git command could not be started ('C:\...\TF.exe')` derdi: yanlış bilgi.

**Merge açısından önemi — EN BÜYÜK ÇAKIŞMA KAYNAĞI BUDUR.** `GitService.cs` ve `WorktreeManager.cs`'te
sarmalayıcıyı çağıran HER satır değişti (23 çağrı yeri). O iki dosyaya dokunan başka bir branch'le kesin
çakışır. Çakışmayı çözerken: çağrı biçimi
`CommandLineTool.RunAsync(_runner, CommandLineTool.Git, _gitExecutable, [...], root, timeout, ct)` ve hata
metni `CommandLineTool.DescribeFailure(CommandLineTool.Git, r)` olmalı. Eski `GitCommandExecutor` adını geri
getirmek bu branch'i kırar (`TfvcService` onu kullanıyor).

### S2. Sha yuvası davranışı değişti — iki eski test yeniden yazıldı (commit `af2fd22`)

**Plan ne diyordu:** "TargetSha == null iken tek değer, çift-ok yok (mevcut tek-taraf render'ı doğrula;
yalnız null-sol destekleniyorsa null-sağ burada eklenir)" (Faz 5.2).

**Ne bulundu:** kural asimetrikti. Sol yarı yokken ok üretilmiyordu, ama sağ yarı yokken yarım bir ok
basılıyordu (`a3f81c2 → `). Harici satırlara ana reponun hedef sha'sı itilmediği için her harici satır
kalıcı olarak o yarım oku gösterirdi.

**Ne yapıldı:** kural simetrik hale getirildi (`ProjectRow.ShaSlotText`) ve kısaltma yalnız 40-hex git
sha'sına uygulanır oldu (`RunViewModel.ShortSha`) — TFVC changeset'i `C48213` kırpılmaz.

**Etkilenen mevcut testler:** `ProjectRowTests` içinde iki test eski yarım-ok davranışını pinliyordu.
Gevşetilmediler, **yeni kuralı pinleyecek şekilde yeniden yazıldılar** ve doc'larına eski iddia + değişme
gerekçesi işlendi:
- `Sha_text_interpolates_current_and_target_with_an_arrow` → `Sha_shows_the_current_half_alone_when_the_target_is_not_known`
- `A_late_arriving_target_sha_refreshes_an_already_rendered_row` (asıl iddiası korundu, ilk assertion değişti)

### S3. `SettingsDraftViewModel` ctor parametre sırası

**Plan ne diyordu:** `(initialLayers, initialExternals, repositoryRoot)` (Faz 4.2).

**Ne yapıldı:** `(initial, repositoryRoot, initialExternals = null)` — harici parametresi SONA ve varsayılan
değerli.

**Neden:** planın sırası mevcut 12 çağrı yerinin (11'i test) hepsini değiştirmeyi gerektiriyordu. Opus
uygulama promptu "görev içi detaylar güncel koda göre esnetilebilir" diyor ve plan boyunca "mevcut testler
DEĞİŞMEDEN yeşil kalmalı" vurgusu var. Bu sıra ikisini birden korur.

**Not:** `RunViewModel.ApplySettingsAsync` için AYNI şey yapılMADI — orada harici parametresi ZORUNLU
(`(patterns, externals, repositoryRoot)`) ve `SettingsDialogTests`'teki 7 çağrı `[]` eklenerek güncellendi.
Gerekçe: opsiyonel bir `externals = null` "Save harici listesini de yazar" sözleşmesini bulanıklaştırırdı —
çağıran unutursa liste sessizce yazılmamış olurdu.

### S4. Kaynak guard'ı Faz 3.4 yerine Faz 2 sonunda yazıldı (commit `c3cda01`)

**Neden:** Faz 2'nin doküman adımında ARCHITECTURE §10.1'e "bir kaynak guard'ı bunu çitler" cümlesi girdi.
Guard Faz 3'e bırakılsaydı doküman iki faz boyunca var olmayan bir şeyi anlatıyor olurdu.

**Guard'ın kapsamı:** `NoGitMutationOutsideExternalsTests` — mutasyon yapan git fiilleri (`merge`,
`checkout`, `switch`, `pull`, `rebase`, `cherry-pick`, `stash`, `clean`, `reset`, `commit`, `push`) yalnız
iki dosyada geçebilir: `Core/Externals/ExternalGitUpdater.cs` ve `Core/Git/WorktreeManager.cs`. `fetch`
listede DEĞİL (salt `refs/remotes/*` günceller).

## Öteki branch'lerle çakışma yüzeyi

Dört branch de `main`'e merge bekliyor ve dördü de `RunViewModel.cs` ile iki dokümana dokunuyor.
**Merge sırası bu branch için serbesttir** — ama ikinci ve sonraki merge'lerde aşağıdaki dosyalar elle
çözülecektir.

| Karşı branch | Ortak dosyalar |
|---|---|
| `feat/clean-button-engine` | `ARCHITECTURE.md`, `README.md`, `AccessibilityNames.cs`, `RunViewModel.cs`, `RunViewModel.Workspace.cs`, `IpcMessages.cs`, `RunCoordinator.cs`, `SupervisorHost.cs` |
| `feat/optimize-button-engine` | yukarıdakilerin hepsi + `MsBuildInvoker.cs`, `Program.cs` |
| `feat/tray-build-animation` | `ARCHITECTURE.md`, `README.md`, `RunViewModel.cs`, `MainWindow.xaml.cs`, `ProjectRow.xaml.cs` |

Kolayca gözden kaçacak beş nokta:

1. **`CommandLineTool` taşıması (S1).** `GitService.cs`/`WorktreeManager.cs` bu branch'te satır satır
   değişti. Çakışırsa yeni çağrı biçimi korunmalı.
2. **`MsBuildInvokeRequest` kuyruk alanı.** `bool ExternalTarget = false` eklendi ve argüman seçimi
   invoker'dan `MsBuildArguments.PlanFor`'a taşındı — `MsBuildInvoker.InvokeAsync` gövdesi kısaldı. Optimize
   branch'i de `MsBuildInvoker.cs`'e dokunuyor.
3. **`RunCoordinator` üç noktada genişledi:** `RunPlan`'a `Externals` kuyruk alanı, planner catch filtresine
   `ExternalPreparationException`, ve worker spawn'dan ÖNCE harici faz + `externalAborted` kapısı. Clean ve
   Optimize de bu dosyaya dokunuyor (muhtemelen `IsRunActive` çevresinde) — bloklar farklı yerlerde, ama
   kapanış sayaçları (`succeeded`/`failed`/`skipped`) bu branch'te `+ externalSucceeded` gibi terimler aldı.
4. **`WorkspaceServices.Default`** bu branch'te `SyncWorkspaceService`'e 6. bir ctor argümanı
   (`ExternalSyncInspector`) veriyor. Clean branch'i AYNI record'a 4. bir parametre eklemiş
   (`Func<string, CleanWorkspaceService> Clean`) — ikisi de korunmalı.
5. **`ProjectRow.xaml.cs`** hem bu branch'te (sha yuvası, S2) hem tray branch'inde değişti.

## Doğrulama

| Ne | Sonuç |
|---|---|
| `dotnet build BuildOrchestrator.slnx` | 0 hata, 0 uyarı |
| Tam süit (`Category!=Acceptance`) | **2258 başarılı**, 1 atlanan (mevcut skip), 0 başarısız |
| Acceptance süiti (`Category=Acceptance`) | **3/3 yeşil** — gerçek OSYS reposunu derleyerek |
| Guard'lar | token / motion / D8 sleep / NoTurkishUserText / AntiSlop / erişilebilirlik — hepsi yeşil |
| Yeni XAML şablonu için realize testi | var (`ExternalSettingsSectionTests`, 4 `[StaFact]`) |

Kritik iki kapının testlerle gerçekten tutulduğu **mutasyon denemesiyle** doğrulandı: `externalAborted`
kapısı ve Cycles kapısı tek tek kaldırıldığında süit kırmızıya döndü, geri alınınca yeşile.

## Bilinen açıklar

1. **Manuel duman testi YAPILMADI.** Planın kabul senaryolarından 3–8 gerçek bir git harici projesi ve bir
   TFVC local workspace'i ister; bu makinede yoktu. Kod ve testler hazır, ama "kullanıcının kendi harici
   projeleriyle uçtan uca çalışıyor mu" sorusunun cevabı yok. **Merge'den önce bu koşulmalı.**
2. **Bir kez flaky test görüldü.** Faz 2 sonundaki İLK tam süit koşusunda 1 test kırmızı verdi; hangisi
   olduğu yakalanamadı (çıktı kesilmişti). Aynı süit hemen ardından ve sonraki üç koşuda yeşil oldu, bir daha
   tekrarlamadı. Bu branch'in ürettiği bir kırılganlık mı yoksa mevcut bir flake mi olduğu **bilinmiyor**.
3. **`ExternalSyncInspector` git haricilerde fetch YAPMAZ** — bu bir eksik değil, D5 kararı: Sync hızlı ve
   çevrimdışı-toleranslı kalmalı. Sonuç olarak Sync'in harici önizlemesi YEREL HEAD'i anlatır; remote'ta
   yeni commit varsa bunu ancak Build söyler. Plan bunu böyle istiyor, ama kullanıcıya "Sync harici için de
   ağa çıkıyor" izlenimi verilmemeli.
