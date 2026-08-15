# Will-Build Tutarlılığı — uygulama sonuçları

Branch: `fix/will-build-consistency` (main'den, 5 commit). **`main`'e merge EDİLMEDİ.**
Plan: `.claude/outputs/2026-08-15-03-05-will-build-consistency-plan.md`

## Yapılanlar

| # | İş | Sonuç |
|---|---|---|
| G1 | depIssue'lu başarı deftere **notla** yazılır; karar `WillBuildEvaluator`'a taşındı | Defter artık ilerliyor; yeniden derlenecek küme değişmedi |
| G4 | `BuildPreviewApplied` sinyali + `GraphBinder` plan fallback'i | Listede amber olanın graf küpü de amber |
| G5 | `WillBuildReason` zinciri (evaluator → IPC → tooltip) | Nokta artık NEDEN derleneceğini söylüyor |
| G2+G3 | `ProjectIdentityRebase` — kimlik imza hesabından ÖNCE ana köke taşınır | Worktree ayrışması kapandı |

## Plandan iki sapma (gerekçeli)

1. **G2 ayrı bir iş olarak yapılmadı, G3'e katıldı.** Plan imza terimlerini kök-bağımsız yapmak için
   `BuildSignature.Compute`'a `canonicalId` parametresi eklemeyi öngörüyordu (68 çağrı yeri). Rebase'i imza
   hesabından ÖNCE yapınca terimler zaten ana-kök id'lerle hesaplanıyor — imza formatı hiç değişmedi.
   **Sonuç kullanıcı lehine: "bir kerelik tam derleme" bedeli DÜŞTÜ**, kayıtlı imzalar geçerli kaldı. Bu
   yüzden `build-state.json` v2 zarfı da gereksizleşti ve yapılmadı (gereksiz risk).
2. **Sıra G1 → G4 → G5 → G3 oldu** (plan G2→G3→G4→G5 diyordu). Bağımlılıklar korundu; görünen kusurlar
   (graf küpü, gerekçe) önce bitirildi ki en riskli iş onları bloklamasın.

## Kırmızı kanıtlar

- Evaluator DepIssue kuralı ve koordinatör persist'i: assertion-red gösterildi.
- `BuildAfterFailureTests`: eski davranış geçici geri konarak kırmızı doğrulandı.
- Graf fallback (birim) ve `GraphWillBuildFeedTests` (uçtan uca, çizilmiş düğümün glyph rengi): assertion-red.
- Worktree kimlik testi: invoke seam'i geçici olarak eski hâline döndürülerek kırmızı doğrulandı.

## [DEĞİŞEN KURAL] ile yeniden yazılan pinler

`RunCoordinatorTests` persist testi · `BuildAfterFailureTests` (state kurulumu artık taze imza + not; test
böylece AYIRT EDİCİ oldu — imzalar eşleşiyor, projeleri listede tutan tek şey not) ·
`OsysIncrementalAcceptanceTests` (persist formülü + pre-skip eşitliği artık NOTSUZ satırları sayıyor) ·
`CycleRoundsTests`'teki A2 atfı.

## Doğrulama

- Derleme temiz. Süit: **1986-1987 geçti, 2 atlandı, 1 dönüşümlü zamanlama kırılganı**
  (`PopoverTests` / `KillMidBuildTests` — ikisi de İZOLE geçiyor, ikisi de bu işin dokunmadığı yolları
  ölçüyor; `PopoverTests` main'de de kırılgandı).
- Acceptance koşulmadı (gerçek OSYS'i derler, ~2 dk + bu turda repo hatalı durumda). `OsysIncremental`
  testinin iddiaları yeni kurala göre güncellendi ama **canlı doğrulaması kullanıcıya kalıyor**.

## Kullanıcının göz kontrolü

1. Sync → listede amber olan HER projenin graf küpü de amber.
2. Amber noktaya hover → gerekçe ("never built" / "last build failed" / "built against a failed dependency" /
   "changed").
3. Hatalar düzeltilmeden Build → depIssue'lu projeler artık sha çiftini ilerletiyor; üçgen + amber nokta +
   amber küp tutarlı.
4. Hatasız bir alt ağaçta Build → ikinci Sync'te o projeler GRİ (defter ilerledi). **Asıl kazanç budur.**
5. Farklı branch'e build (worktree) → sonrasında Sync'te kopya satır YOK ve her şey dirty DEĞİL.

## Açık konu

Kullanıcının OSYS reposunda 24 gerçek derleme hatası var. Bu iş onları düzeltmez — düzeltmesi de gerekmez.
Ama o hatalar durdukça, onlara bağımlı ~96 proje her koşuda yeniden derlenmeye devam edecek (artık
gerekçesi tooltip'te görünür: "built against a failed dependency"). Incremental derlemenin tam faydası
ancak hatalar düzelince ortaya çıkar.
