# Sync / Build akışı — Faz 1 (kümülatif renk modeli) oturum özeti

Tarih: 2026-09-18 · Tür: uygulama · Çatı branch: `sync-build-flow` (Faz 1 merge'ü `521f473`, `--no-ff`) · `main`'e
merge YOK (kullanıcı çatıyı test edip onaylayacak).

## Ne yapıldı

- Faz 1 planı (`.claude/outputs/2026-09-18-07-56-sync-build-flow-phase1-plan.md`, T0-T9)
  `superpowers:subagent-driven-development` ile task-by-task uygulandı. Her task için ayrı review yapıldı,
  bulgular fix round'larda kapatıldı. Sonunda bütün branch final review'dan geçti, bulgular tek bir fix
  dispatch'inde düzeltildi.
- Tam süit yeşil (`Category!=Acceptance`); üç Acceptance testi gerçek OSYS üzerinde birlikte yeşil.
- Faz branch'i `sync-build-flow-phase1` çatıya merge edildi, local ve remote'tan silindi.
- Tasarım paketi v1.20.0: `.claude/outputs/2026-09-18-10-16-design-v1.20.0/README.md`. Prototip v1.19.0 olarak
  kaldı, bu sürümde yalnız metin değişti.

## Ne değişti (kısa)

- Renk artık çıktının durumunu anlatıyor ve kümülatif: yeşil güncel, gri derlenecek, kırmızı kanıtlı bozuk,
  kesikli çerçeve bilinmiyor. Hiçbir işlem listeyi sıfırlamıyor. Döngü küpü her zaman amber.
- Defter kanıtı tutuyor: `BuildState.FailedSignature/FailedAt`.
  - Kanıtlı kırmızı yalnız derleyici hatasında (exit ≠ 0, güvenilir sonuç, imza biliniyor, Build/Rebuild hedefi).
  - Timeout, Stop, invoke hatası ve Clean hatası gri `never built`.
- Motor kararını olayla gönderiyor: `ProjectFailedEvent.Evidence`, `ProjectSucceededEvent.Trusted`. Uygulama
  kendisi tahmin yürütmüyor; "sonraki preview ne der" eşlemeleri `Core/Planning/NextPreview.cs`'te.
- Önizleme `FailedAt` ve `LocalEdits` taşıyor. `local` bilgisi `git status --porcelain -z`'den okunuyor,
  Türkçe yollar doğru çözülüyor.
- App tarafı:
  - `StandingStatus` ve `VisualStatuses.For/OfRun/StateOf`.
  - Etiketler `up to date · 2h` · `modified` · `modified · local` · `affected` · `never built` · `failed · 2h`.
  - Yuva 134 px.
  - Chip'ler `Σ · building · ✓ · ○ · ✗ · ⚠`, yalnız ikon ve sayı.
  - Filtreler `building/current/stale/failed/warn`; ekran okuyucu görüneni söylüyor.
  - Nötrleme yalnız koşu alanlarını siliyor.
  - Configuration değişimi koşu izlerini siliyor ve Stopped'u kapatıyor.
  - Branch değişiminde liste ve graf birlikte başlangıç moduna dönüyor (`RowDecisionsChanged`).

## Kullanıcı kararları (bu oturum)

- `—` yalnız run-story yüzeylerinde görünür (olay akışı, konsol başlığı); satırda, grafta ve chip'te yok.
  Tutarlı olsun diye karar Claude'a bırakıldı.
- Atlanan satır koşu sonunda sıfırlanmaz; şerit "N skipped" saymaya devam eder.
- `local` bilgisi yalnız koşu dışındaki Sync'te güncellenir.
- Timeout ve Stop hemen gri olur.
- Üçgen amber, yalnız satırda; grafta döngü küpü var. Dokümandaki "kırmızı üçgen" notu eskiydi.

## Claude kararları (itiraz edilebilir)

- Kanıt ve güven kararı motordan olayla gelir.
- Configuration değişimi Stopped'u kapatır.
- Chip'lerde kelime yok, kelimeler tooltip'te.
- "+N more" `failed` durum filtresini açar.
- İşaretleme dalgası boyunca sayaç gecikmesi kabul edildi.
- ⚠ kalıcı üçgeni sayar; şerit yalnız bu koşunun dependency-affected sayısını.
- `Brush.StatusSkippedSoft` ve `Icon.StatusCycle` silindi.
- `OsysIncrementalAcceptanceTests` yeni kurala göre yeniden yazıldı; alt sınırı güçlendi.
- Kabul edilen boşluk: commit kaydı olmadan başarılı olup sonra patlayan proje bir Sync boyunca `never built`
  okunur.

## Açık kalanlar (kullanıcı kararı bekliyor, Faz 1 dışı)

- Satırdan Build/Rebuild yapılan projede proje sayfası "dependency healthy olunca derlenecek" diyor; oysa koşu
  projeyi zaten derliyor.
- `GitService.ParseLsTreeBlobHashes` Türkçe yolları hâlâ escape'li okuyor; imza anahtarlarını etkileyebilir.
- İki OSYS Acceptance sınıfı aynı ağacı paralel derliyor (MSB3026 uyarısı); ortak `[Collection]` adayı.
- Porcelain v1'de worktree tarafındaki rename (`git add -N`) nadir bir uç durum.
- Kullanıcı çatıyı canlı uygulamada henüz test etmedi (test listesi oturum sonunda verildi).
