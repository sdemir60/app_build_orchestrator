# Continue kalıntıları temizliği + §13.3 — sonuç

- **Branch:** `chore/continue-remnants-and-root-doc` · taban `main` `6224eeb` · sonra `main` (`8aa7d01`, P4) alındı
- **Plan:** `.claude/outputs/2026-09-28-22-30-continue-remnants-cleanup-tdd-plan.md`
- **İnceleme (kanıtlar):** `.claude/outputs/2026-09-28-22-30-continue-remnants-investigation.md`

## Ne yapıldı

| Task | Sonuç | Commit |
|---|---|---|
| 0 | ARCHITECTURE §13.3 kök değişimi paragrafı ve `ApplyRepositoryRoot` yorumu koda göre yazıldı (satırlar hollow'a döner, plan yüzeyi boşalır, son Sync HEAD'i unutulur, Save'in tek Sync'i yeni kökte) | `92dc893` |
| 1 | Motordaki ölü koşu alanları silindi: torn-DLL izi (`StoppedFailedIds`), `_plan`, `_root`/`SameRootLocked`/`Canonical`, `_depIssuesById` alanı | `a70122f` |
| 2 | `RunSnapshot` kalktı: scheduler koşu başı pre-skip tohum sözlüğü alıyor, bitişte `UnfinishedCount` (in-flight dahil) veriyor; 2 yeni pin | `e9b3ba7` |
| 3 | `RunClock`'tan Continue tohumu (`accumulatedMs`) kalktı, doc koşu süre saatine göre | `6d83f55` |
| 4 | `BeginRunAsync`'in hep `true` geçilen `clearBuffers` parametresi ve `OnBuildPreview`'daki erişilemeyen segment guard'ı kalktı; iki test yeni kurala göre yeniden yazıldı | `cc84001` |
| 5 | Hep 0 olan `RunStartedEvent.ElapsedMsAtStart` kontrattan, motordan ve App'ten kalktı; 175 test çağrısı betikle güncellendi; eski NDJSON satırının çözüldüğü pinlendi | `3cbc1a0` |
| 6 | Kalan yanlış yorumlar (32 madde) ve ARCHITECTURE D1-D5 koda göre | `34547e7` |
| 7 | Adlar: `RunSegmentAsync` → `PlanAndRunAsync`, `CanRebuildOrRetry` → `CanStartRunOnIdleWorkspace`, Continue/segment geçen test adları | `ac5ecfa` |
| Final review | Süre pini ayırt edici yapıldı; assert'siz kalmış test yeniden yazıldı; yakınsamama hafızası yorumları (Build değil Cycles; artık bloklamaz, yalnız raporlanır); ölü bir `seed.ContainsKey` koşulu silindi; tohum çağrısı teke indi | `057d3bc` |
| Kalan dokümanlar | ARCHITECTURE §8.8 (Cycles kapsamı upstream dahil), CycleUnconverged doc'ları, test doc'u, iki ad | `7224a21` |

## Doğrulama

- Sıfırdan Release build: 0 uyarı, 0 hata.
- Tam süit (`7224a21`): 3636 başarılı, 0 başarısız, 6 atlanan (ortam kapılı ölçüm testleri).
  Bir koşuda `StickyLayerHeaderClickTests`'ten 6 test kırmızı düştü; tek başına 19/19 yeşil, tekrar koşuda tam süit yeşil —
  aynı 6 test aynı değerlerle `2026-09-26` sonuç raporunda "kararsız (gerçek pencere/fare; ekran kilitliyken ya da eşzamanlı
  işte)" olarak kayıtlı.
- Acceptance (gerçek OSYS): `main`'de ve branch'te 3/3 geçti. OSYS'teki commit'lenmemiş iki dosyaya dokunulmadı.
- Her task ayrı review; branch için final review + düzeltme dalgası + yeniden review.

## Kullanıcı adına verilen kararlar

- ARCHITECTURE'da iki cümle koda göre düzeltildi, çünkü dokümanın kendi başka bölümleri de kodla aynı şeyi söylüyordu:
  "the next `Build` picks up where this one left off" → sonraki **Cycles** koşusu; §8.6'da dış kopya güncellemesini
  atlayan modlara **Clean** eklendi.
- İkinci instance'ın geçici tepsisi gibi durumlar ilk turda karara bağlanmıştı; bu turda yeni ürün kararı yok.

## Dokunulmayanlar (ayrı karar ister)

- `ProjectSkippedEvent.CycleUnconverged` alanı ve `SkipReasons.CycleNonConvergent` sabiti: motor artık hiç `true`
  göndermiyor (yakınsamama pre-skip'i daha önce kalktı); doc'ları gerçeğe göre yazıldı, alan/sabit ve App'teki okuyucusu
  duruyor — kaldırmak ayrı bir temizlik.
- Tohum hesabının Supervisor'dan Core'a taşınması (katman kuralı), iki "boşta workspace" kapı formülünün teke indirilmesi.
