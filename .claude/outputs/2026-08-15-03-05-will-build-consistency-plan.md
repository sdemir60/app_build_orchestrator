# Will-Build Tutarlılığı — TDD Dökümü

Onaylanan planın proje içi kopyası (kural: plan `~/.claude/plans/`'da doğar, iş başlarken buraya taşınır).
Branch: `fix/will-build-consistency` (main'den). **Merge kararı kullanıcının.**

## Neden

Gerçek OSYS reposunda (177 proje) beş tutarsızlık: listede amber "derlenecek" varken graf küpü nötr;
Sync az gösterirken Build neredeyse her şeyi derliyor; Build'den hemen sonra Sync yine her şeyi
"derlenecek" sayıyor; amber nokta + tek sha çelişkili görünüyor. Hedef: dört yüzeyin (nokta, sha, üçgen,
graf küpü) aynı gerçeği söylemesi ve "neden derlenecek"in görünür olması.

## Kanıtlanmış kök nedenler

| # | Kusur | Kök neden |
|---|---|---|
| K1 | Liste amber, graf nötr | `BuildPreviewEvent` satırı günceller ama grafa sinyal gitmez: graf yalnız `TopologyChanged` + `PropertyChanged(Counters/IsRunning/IsStarting)` ile beslenir; `RunCounters.From` WillBuild'i okumaz, record-struct eşitliği bildirimi yutar. `GraphBinder` WillBuild için topoloji fallback'i de yok (InCycle'da var). |
| K2 | Build sonrası Sync yine "hepsi derlenecek" | `RunCoordinator` persist kapısı depIssue taşıyan başarıyı hiç persist etmiyor. **Ölçüldü:** succeeded=74 failed=24 depIssues=96 koşusunda 74 başarının 0'ı persist edildi; defter hiç ilerlemiyor. |
| K3 | Gerekçe görünmüyor | Evaluator gerekçeyi biliyor, `BuildPreviewItem` yalnız `bool?` taşıyor. |
| K4 | Worktree (latent) | İmza `up=`/`diff=` terimleri MUTLAK yol hash'liyor; BuildState anahtarı ScanRoot yolu → worktree build'inden sonra in-place Sync hiçbir kaydı bulamaz; run-preview worktree id'leriyle listede kopya satır üretir. |

Sync vs Build girdileri in-place'te **birebir aynı** — sorun persist + gösterim, tahmin ayrışması değil.

## Kullanıcı kararları

1. depIssue'lu başarı deftere **DepIssue notuyla YAZILIR** (yeniden-derleme seti değişmez; kayıt/sha gerçeği gösterir).
2. Worktree kimlik ayrışması **bu işte düzeltilir** (bedel: bir kerelik tam derleme).

## Görevler

`G2 → G3` zorunlu sıra · `G1` atomik · `G4` bağımsız · `G5` G1'e bağlı.

- **G1** depIssue'lu başarı DepIssue notuyla persist edilir + evaluator bayrağı okur (AYNI commit).
- **G2** İmza terimleri kök-bağımsız (repo-göreli canonical id) + `build-state.json` v2 zarfı.
- **G3** Worktree kimlik rebase'i — App ve state worktree yolu hiç görmez.
- **G4** `BuildPreviewApplied` sinyali + `GraphBinder` WillBuild fallback.
- **G5** `WillBuildReason` zinciri → gerekçeli tooltip.

Her görev: kırmızı test → fix → yeşil → commit. Değişen kuralı pinleyen eski testler `[DEĞİŞEN KURAL]`
notuyla yeniden yazılır (silinmez, gevşetilmez). Ayrıntılı kırmızı-test senaryoları, dosya listeleri ve
riskler onaylanan planda (`~/.claude/plans/lexical-baking-penguin.md`).

## Doğrulama

Tam süit `Category!=Acceptance` yeşil · Acceptance ayrı · kullanıcı göz kontrolü (Sync'te amber olan her
projenin küpü de amber; depIssue'lu projeler tek sha + üçgen + amber nokta + amber küp; failure'suz alt
ağaçta ikinci Sync GRİ; worktree sonrası kopya satır yok; amber noktada gerekçeli tooltip).

**Not:** imza formatı değiştiği için kullanıcının ilk gerçek Build'i DOLU koşacak.
