# Sync/durum kapsam taraması — TDD planı

Kaynak: 21 Eylül rehberinin (73 ekran maddesi + etiket semantiği) mevcut otomatik testlerle eşleştirilmesi.
Beş keşif ajanı dilim dilim taradı; bu döküm birleşik ve önceliklendirilmiş sonuçtur. HEAD `df18450`.

Kurallar: her task kendi commit'i; fix'ler kırmızı test görmeden yapılmaz; "pin" testleri önce koşulur —
kırmızı çıkarsa ya iddia yanlıştır ya gerçek kusurdur (systematic-debugging'e geçilir). Build/test koşuları
serileşir (tek çalışma ağacı).

## A. Kusur adayı (kırmızı test + fix)

- **K1 — `.vs`/`.git` tarama dışı değil.** `ProjectInputs.cs:148-150` yalnız `bin`/`obj` atlar;
  `WorkspaceScanner.cs:16` ise `.git`/`.vs`/`node_modules` atlar (iki ayrı liste — kopya yasak ihlali).
  Zaman yolu her taranan klasörün tarihini okur (`OutputEvidence.cs:115`) → klasöründe `.vs` olan (yanında
  .sln taşıyan) ya da repo kökünde oturan proje, VS açıkken durduk yere `modified` olur. Kırmızı test:
  ProjectInputs `.vs`/`.git` altındaki dosya/klasörü girdi saymaz; fix: atlama listesi tek kaynağa bağlanır.

## B. Karar bekleyen tutarsızlıklar (davranışa DOKUNULMAZ, rapora)

- **Q1 — Bekleyen satır sayımı:** ribbon bekleyen (yeşil+⚠) satırı "up to date" sayar
  (`RibbonText.cs:143-144`), Sync akış satırı iki sayıya da katmaz (`SyncWorkspaceService.cs:363-364`).
- **Q2 — Harici kök ana repo içini gösterebilir:** o zaman harici projede `local` çıkabilir
  (`LocalEdits.cs:11-14` niyetiyle çelişir; `ExternalWorkspaceResolver` reddetmiyor).

## C. Rehber düzeltmeleri (kod doğru, rehber yanlış — final rapora işlenecek)

- **G1 — Madde 23 + §1 failed çıkış 4:** VS'deki düzeltme A'nın kaynağını değiştirdiyse B/C yeşil kalmaz;
  imzaları oynadığı için aynı Sync'te `affected` olur ve ⚠ o anda düşer. Yeşil+⚠ yalnız içerik değişmeden
  düzelme (ör. eksik DLL) durumunda.
- **G2 — Madde 33 + never-built istisnası:** bakım Clean'i `obj`'yi sildiği için klasör tarihi ilerler →
  paylaşılan OutputPath'li proje yeşil değil `modified` görünür. Yeşil ancak satır-Clean + kilitli DLL
  kombinasyonunda mümkün.
- **G3 — Madde 50/51:** 5 sn eşiği son Sync'in başlangıç/bitişinden ölçülür; pencereden uzak kalma
  süresinden değil.
- **G4 — Madde 10:** dolaylı bağımlının (C) logundaki uyarı `warning: failure in dependency chain (A)…`;
  "A failed in this run" yalnız doğrudan bağımlının logunda.
- **G5 — §3 tek üye ▶:** satırın ⚠'si hep `In a dependency cycle` (öncelik sırası); döngü kardeşleri yalnız
  ledger notu + proje log satırında.
- **G6 — Resolve cycles:** kirli upstream turlardan önce TEK sefer derlenir; turlar yalnız üyeler içindir;
  ribbon'da `preparing dependencies` evresi vardır. "did not converge" yalnız ilerlemesiz grupta, "did not
  fully settle" yalnız tur tavanında yeşil bitenlerde; ikisi de bir sonraki görünür işlemde temizlenir.
- **G7 — Küçükler:** zaman yolunda dosya ekleme/silme/yeniden adlandırma (uzantı fark etmez, .md dahil)
  klasör tarihi yüzünden `modified` yapar; madde 22'de C de `affected` olur; madde 24 yalnız derleyici
  hatasında geçerli (post-build copy hatası "built outside"/`affected` bırakabilir); madde 62'de chip
  mid-merge devre dışıdır; madde 16 sayfası hiç başarı görmemiş kırmızıda iki satırdır ve yalnız motorda log
  yokken görünür; SDK-style projede elle DLL silme görünmez (madde 32 kaydı); F5 koşarken Stop'tur;
  hover-echo yüzünden graf düğümüne gelmek liste satırının etiketini ▶/⋯ ile değiştirir (madde 1 testinde
  imleç listeden/graftan uzak tutulur); madde 42'de aynı boyut + yeni tarihli kopya tasarım gereği fark
  edilmez.

## D. Test task'ları (öncelik sırasıyla; B/Ö/K = bloklayıcı/önemli/kozmetik)

| # | Ö | Task | Yer / yeniden kullanılan altyapı |
|---|---|---|---|
| T1 | B | K1 kırmızı test + fix (`.vs`/`.git` dışlama, tek liste) | `Incremental\ProjectInputsTests`, `OutputEvidenceTests`; fix `ProjectInputs.cs` + `WorkspaceScanner.cs` ortak sabit |
| T2 | B | Gerçek pencerede liste↔graf renk eşitliği: success + kanıtlı fail + kanıtsız fail + cycle üyesi sonrası satır stripe/glyph ve düğüm border/core token eşleşir | `App\GraphWillBuildFeedTests` yanı; `MainWindowHost`, `RunViewModelStateTests.A_second_build_keeps_...` event script'i |
| T3 | B | G1 pinleri: F fixed+VS (time fresh) & P ledger dep-note + sig moved → SignatureChanged/affected; kontrol: içerik aynı → WaitingForDependency; B/C affected + ⚠ düşer | `Incremental\IncrementalPlannerTests` (FailedUpstreamFixture varyantı), `Incremental\BuildAfterFailureTests` |
| T4 | B | Araç derledi → VS yeniden derledi e2e: X built outside; Y affected (X değiştiyse) / yeşil (değişmediyse) | `Workspace\SyncWorkspaceServiceTests`; `CommitLegacyWorkspace`, `WriteBuiltOutput`, `PrimeBuildStateAsUpToDateAsync` (+`BuiltContent` upsert) |
| T5 | B | Clean→Sync compose (G2 gerçeği): default → never built; paylaşılan OutputPath+obj → modified; obj'suz → built outside; kilitli DLL → modified; SDK → never built | `Workspace\SyncWorkspaceServiceTests` + `CleanWorkspaceServiceTests.NewService` kalıbı |
| T6 | B | Sync-seviyesi etiket serisi: dirty edit → `modified · local`; commit → `modified`; revert → `up to date`; untracked .cs; .md/.config etkisiz; Directory.Build.props altı `modified` | `Workspace\SyncWorkspaceServiceTests` (gerçek git; C→B referansı eklenir) |
| T7 | B | Failed kaydıyla evaluator: {sig1, Failed, FailedSignature} + sig2 → SignatureChanged; failure yazımı `BuiltContent`i korur | `Planning\WillBuildTests`; `Supervisor\RunCoordinatorTests.A_failure_keeps_the_recorded_copies` |
| T8 | Ö | RowWarning öncelik teorisi (unconverged>unsettled>InCycle>dep; `CycleUnsettled` metni) + satır tooltip'i | `App\RowWarningTests`, `App\ProjectRowTests` |
| T9 | Ö | Satır menüsü koşu sırasında kilitli (IsEnabled/opacity/tooltip; tıklama komut göndermez) | `App\ProjectRowInputTests` (DispatcherPump kalıbı) |
| T10 | Ö | Stop: akış satırı `Stopped — N remaining projects queued`; graceful stop sonrası geç başarı Trusted; queued satır etiketi değişmez | `App\EventStreamTests`, `Supervisor\RunCoordinatorTests` (InterruptWhileAIsInFlight klonu, Graceful) |
| T11 | Ö | HeadWatcher: commit'siz dosya kaydı hiçbir sync tetiklemez (+ `new FileSystemWatcher(` yalnız HeadWatcher.cs guard'ı) | `Git\HeadWatcherTests` (`GitTestRepo`, `Recorder`) |
| T12 | Ö | Ribbon resolve-cycles: `round r/cap` + `preparing dependencies` + VM feed | `App\RibbonTextTests`, `App\EventStreamTests` |
| T13 | Ö | AutoSync aktivasyon glue: 4999 ms yok / 5000 ms var, `Fetch=false`; `ActivationQuietMs==5000` pini | `App\RunViewModelTests` (injected clock kalıbı), `App\AutoSyncCoordinatorTests` |
| T14 | Ö | Ledger yolu paylaşılan kopya: silinmiş + eski kopya → broken; ledger HintPath DLL zaman görmez; BST/PIT `.dll`/`.config`/`.json`/`packages.config` dışlama | `Incremental\OutputEvidenceTests` (Touch/Record/Check), `BuildSignatureTests`, `ProjectInputsTests` |
| T15 | Ö | Madde 10/12 VM zinciri: success+depIssue akışta `built — dependency issue`; `dependency still failing` sonrası satır yeşil+⚠; C (kalıtsal kök) de atlanır | `App\EventStreamTests`, `App\RunViewModelStateTests`, `Supervisor\ConditionalRebuildRunTests` (ChainPlan) |
| T16 | K | Proje sayfası: `Up to date — nothing to compile.`; hiç başarısız + sha'sız iki satır; skipped built-outside `…in this run.` + `Built outside this tool` | `App\ConsoleModesTests` (Row helper) |
| T17 | K | Arama kutusu VM'e bağlı: Esc → query boş + liste döner; ✕ → boş + odak kalır; click-away → odak gider + query kalır | yeni `App\SearchInputTests` (`RealizeFilterBox` kalıbı) |
| T18 | K | Madde 22 e2e: HintPath DLL yenilendi → Dep `DependencyNewer`/`affected`; arkasındaki C dalgayla gelir | `Incremental\IncrementalRunBinderTests` (`TwoLegacyProjects` + ET) |
| T19 | K | GitOperationText: Build/Sync mid-merge notları Info sınıfında (amber değil) + SyncWarning metin teorisi | `Git\GitOperationTextTests` |
| T20 | K | Behind chip → `PullRepositoryCommand`; `PullCompleted(ok)` → Behind=0 + tek `SyncWorkspaceCommand(Fetch=true)` | `App\ActionBarTests`, `App\RunViewModelTests` |
| T21 | K | Bayat yorum temizliği: `ProjectRow.xaml.cs:123`, `ProjectRowMenu.xaml.cs:42`, `ProjectRowInputTests.cs:239-240,265-266` | yorum düzeltmesi (davranış yok) |

Kapsam haritasının tam ayrıntısı (madde madde verdict tabloları) beş ajan raporundadır; bu döküm uygulanacak
işi taşır. T2-T21 pin testleridir (yeşil doğması beklenir; kırmızı = yeni kusur). MANUAL-ONLY kalanlar final
rapordaki el listesine girer.
