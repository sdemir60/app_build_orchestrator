# It-4b Faz A TAMAM — MUST-DO-FIRST motor kalemleri (A1–A5)

**Branch `it4b-ui` @ `1e367dc`** (main `903e503`'ten, **12 commit**). Build **0 uyarı / 0 hata** ·
Suite **825 geçti / 1 atlandı / 0 hata** (baseline 763'tü) · **Acceptance 3/3 PASS** (gerçek OSYS, ~2 dk).
`main` ve `origin`'e DOKUNULMADI.

Plan: [.claude/outputs/2026-07-21-05-46-it4b-tdd-plan.md](../outputs/2026-07-21-05-46-it4b-tdd-plan.md) — 24 task, 106 adım.
Ledger (yerel, gitignore'lu): `.superpowers/sdd/progress.md` — task başına commit SHA + review sonuçları + devredilen Minor'lar.
Task brief'leri (yerel): `.claude/temp/it4b/task-*-brief.md` (24 adet) + `global-constraints.md`.
Yöntem: `superpowers:subagent-driven-development` — task başına taze implementer → review → fix → re-review.

---

## Neden Faz A önce geldi

It-3 ve It-4a'nın final review'ları beş motor kalemini "It-4'ten ÖNCE kapat" diye devretmişti. Üçü **sessiz
under-build** üretiyordu: kullanıcıya "skipped — up to date" denen proje aslında derlenmesi gereken projeydi.
UI bunların üzerine kurulsaydı, hatalı veriyi güzelce gösteren bir arayüz çıkardı.

---

## Teslim edilenler

| Task | İçerik | Kanıt |
|---|---|---|
| **A1** | LayerEngine wiring + **sıra-bağımsız propagation** | Katman pattern'leri artık `StartRunCommand` ile Core'a ulaşıyor, ters-katman uyarıları `BuildPlan.LayerWarnings` üzerinden konsola çıkıyor (eskiden yutuluyordu). `IncrementalPlanner` dizi sırası yerine **DFS-memo + on-stack guard**, `RetryPlanning` **sabit-nokta** ile çalışıyor → katman reorder'ı artık under-build üretemez. Review **Approved**, düzeltme gerekmedi. |
| **A2** | depIssue-persist penceresi **+ başarısız proje invalidasyonu** | depIssue taşıyan success artık `BuildState` yazmıyor. Review daha büyük bir deliği buldu: **başarısız proje build-state'te hiç güncellenmiyordu** — bir kez yeşil olmuş proje sonradan bozulsa bile sonsuza dek "up to date" görünüyordu. Artık `LastResult=Failed` ile invalidate ediliyor (`BuiltSignature` Fast modu için, `LastDurationMs` ETA için korunarak). Ayrıca `RunMode.Build` pre-skip yolu ilk kez acceptance dışında, deterministik test ediliyor. |
| **A3** | **SCC-aware propagation** | Dependency cycle'ı artık imza kara deliği değil: her SCC tek bir **kompozit imza** alıyor, içindeki kaynak değişimi cycle-**dışı** downstream'e kaskat ediyor. It-3'ün "kapatılamaz" diye kaydettiği sınır kapandı. Mutasyon testleriyle kanıtlandı. |
| **A4** | Worktree **uçtan uca BUILD wiring** | `WorktreeManager` src'de hiç new'lenmiyordu, obj izolasyonu ölü koddu, `inPlace` **tahmin** ediliyordu. Artık Build anında gerçek worktree hazırlanıyor, tarama oradan yapılıyor ve `inPlace` **çözülmüş köke** göre türetiliyor. Review 1 Critical + 3 Important buldu: havuz cap'i **hiçbir şeyi tahliye edemiyordu**, hiçbir worktree **yeniden kullanılmıyordu** (obj cache asla ısınmıyordu), ve farklı-branch hazırlığı başarısız olunca **sessizce aktif branch derleniyordu**. Hepsi kapandı. |
| **A5** | Sync / branch / worktree / **topoloji** IPC'si | `syncWorkspace` `unknownCommand` cevabı veriyordu; dört Sync/branch event'i hiç üretilmiyordu. Artık uçtan uca çalışıyor + yeni `WorkspaceTopologyEvent` graf/katman/sln verisini UI'a taşıyor. **Mid-task amendment:** Sync artık gerçek will-build pass'i koşuyor (plan üç ayrı yerde bunu gerektiriyor), mevcut `BuildPreviewEvent` kanalı yeniden kullanılarak. |

---

## Canlı OSYS acceptance koşusu (2026-07-21) — A1–A3'ün tek uçtan uca kanıtı

```
Run 1 (state YOK):      177 proje · 118 başarılı · 27 hata · 32 skip · 30.2s
Run 2 (kaynak aynı):    47 "up to date" pre-skip · 98 yeniden derlendi · 24.6s
A2 çapraz kontrolleri:  açıklanamayan 0 / açıklanamayan 0
A3 TAM cascade:         24/24 transitive cycle-dışı dependent flip etti   ← It-3'ün "ulaşılamaz" dediği
K1 read-only:           HEAD ve branch koşu öncesi/sonrası AYNI
```

Koşu ayrıca **bayat bir acceptance tabanını** (A2 öncesinden kalma sabit `>= 100`) ortaya çıkardı; türetilmiş ve
daha sıkı bir sınırla (`run2UpToDate.Count == stateAfterRun1.Count`) değiştirildi.

---

## Kullanıcı kararı bekleyen tek konu — A2'nin bedeli

OSYS'de şu an **27 kalıcı hatalı proje** var. Bunlar 118 başarılı projenin **71'ini** depIssue ile zehirliyor, ve
A2'nin guard'ı yüzünden o 71 proje build-state yazmıyor → her incremental Build **98/177 projeyi** yeniden derliyor.

Bu **doğru**: o 71 binary, hatalı upstream'lerin bayat çıktısına link'li. Ama upstream **kaynağı değişmeden**
hata veriyorsa (buradaki yaygın durum), 71 projeyi yeniden derlemek birebir aynı binary'yi üretiyor = saf israf.

Üç seçenek: **(a)** böyle kalsın (güvenli, repo bozukken yavaş) · **(b)** taşıyıcıyı upstream'in son **başarılı**
jenerasyonuna göre persist et — yalnız upstream gerçekten yeşile dönünce yeniden derlensin (planın "fazla karmaşık"
diye reddettiği marker şeması) · **(c)** taşıyıcı guard'ını kaldır, yalnız failure-invalidation'a güven (hızlı, ama
kaynağı-değişip-sonra-hata-veren deliği geri açar).

Hiçbir sonraki iş bu seçime bağlı değil; istediğin zaman dönülebilir.

---

## Sıradaki adım

**Task A5'in fix dalgası (`38398c0..1e367dc`) henüz re-review edilmedi** — Faz B'ye başlamadan önceki tek kapı.
Ardından plandaki sırayla B1 → E6 (19 task): token seti, font/ikon hattı, DS kontrol kütüphanesi, tooltip,
2×2 layout, VM omurgası, yedi panel task'ı, ve altı çapraz-kesen task.
