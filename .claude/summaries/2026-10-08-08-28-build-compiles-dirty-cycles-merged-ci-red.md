# Build kirli cycle gruplarını derler — merge edildi, develop CI kırmızı (duraklatıldı)

## Konu

Kullanıcının OSYS Service WorkOrder projelerinin araçta kırmızı olması incelendi: kök neden, düz Build'in değişmiş
cycle üyelerini atlaması ve bağımlılarının eski DLL'e karşı derlenmesiydi. Çözüm (seçenek A): Build kirli cycle
gruplarını turlarla derler, Rebuild her grubu (tur 1'de her üye), Resolve cycles isteğe bağlı dar biçim olarak kalır.

## Yapılanlar

- Plan: `.claude/outputs/2026-10-08-05-16-build-compiles-dirty-cycles-plan.md` — satır içi yürütüldü (Task 0-6),
  Task 2 sonrası ara inceleme (I1, I2 düzeltildi), sonda bağımsız final inceleme.
- Final inceleme düzeltmeleri: kökünü bekleyen grup Build dalgasında ve "N to build" sayısında artık kesin sayılmıyor
  (grupla koşullu — `ConditionalRebuild.ConditionalIds`, `NextPreview.AfterSuccess`); Contracts `RunMode` doc'u,
  ARCHITECTURE §8.3 örneği ve App yorumları düzeltildi; tur sayacı sıfırlaması ve Sync test fixture'ları tek yerde.
- Doğrulama (lokal): tam süit 4679 geçti, 8 atlandı; kırmızı yalnız 6 `StickyLayerHeaderClickTests` (imleç (0,0),
  değişmemiş develop'ta da aynı). Gerçek OSYS acceptance 6/6 yeşil.
- Ölçüm (arayüzsüz, kullanıcının taban ayarları): Clean sonrası tek Build 193,8 sn (taban: Resolve 197,4 sn + Build
  186,5 sn); değişiklik olmadan Build 1,2 sn. Ayrıntı: `.claude/outputs/2026-10-08-08-02-build-compiles-dirty-cycles-measurement.md`.
- Sonuç raporu ve verilen 24 kararın tam listesi: `.claude/outputs/2026-10-08-08-09-build-compiles-dirty-cycles-result.md`.
- `develop`'a `--no-ff` merge + push: `81d2f513` (`merge: build kirli cycle gruplarini da derler ...`). Çalışma
  branch'i lokalde silindi (remote'a hiç push edilmemişti). Plan çalışma alanı (`.superpowers/sdd/...`) silindi.
- Yan etki: acceptance ve ölçüm koşuları OSYS çıktılarını yerinde yeniden derledi. `OSYS.Orchestration.Accounting.Common`
  her koşuda CS1061 ile düşüyor (derlenmiş ETransformation tipiyle kaynak uyuşmuyor) — araçtan bağımsız, tabanda da aynı.

## Kaldığımız yer

**develop CI kırmızı:** `ci` koşusu `37731072772` (head `81d2f513`), test adımı 6 dk 46 sn sürüp başarısız bitti
(asılma değil, test hatası). Bir önceki develop koşusu (`37c98faf`) yeşildi.

- CI'ın filtresi lokalde Release ile aynen koşuldu (`Category!=Acceptance&Category!=LocalOnly`): 4674 geçti, kırmızı
  yalnız 6 Sticky testi — CI hatası lokalde **üretilemedi**.
- Log ve TRX artifact'ı kimlik istiyor; hafızadaki yol (`git credential fill` + `curl -u`) **kullanıcı onayı** ister,
  onay alınmadı. Hangi testin düştüğü bilinmiyor.
- Şüpheli (doğrulanmadı): bu branch'le gerçek `MSBuild.exe` doğurmaya başlayan
  `RunViewModelTests.Rebuild_wires_through_the_real_engine_and_populates_rows` (60 sn guard). Aynı iş yükündeki
  `RunCoordinatorTests.real_supervisor_process_wires_start_run_and_keeps_stdout_ndjson_only` (Cycles) CI'da hep yeşildi.
- `/release` develop CI'ı yeşil olmadan çalışmaz; sürüm çıkarılmadan önce bu kapanmalı.

## Açık kalanlar

- CI kırmızısı: onayla TRX'i indir, düşen testi bul; ortam kaynaklıysa `Category=LocalOnly` + gerekçe, değilse
  kırmızı test kuralıyla düzelt (bir `fix/` branch'i develop'tan).
- Kullanıcının arayüzde gözle kontrolü (Clean → Build: dalga, şerit tur satırı, "N to build") ve plan Adım 2.4 (bir
  Types cycle üyesinde public üye değişikliği).
- Açık soru M1: bekleyen grup atlanınca zaman kipindeki bağımlıların bir kez boşuna derlenmesi kabul mü, yoksa
  `IncrementalPlanner.ProducesNewOutput` bekleyen grubu tohum saymasın mı.
- Ertelenen küçük bulgular: iki grubun turu çakışınca şeridin kısa süre `Building` yazması; "(last known failure)"
  ekinin uçtan uca pinli olmaması; Build'de tur ortası graceful Stop'un ayrı pinli olmaması.
- Organizasyon notu: "Build veya Cycles" mod kuralı `ConditionalRebuild` ve `RunCoordinator`'da iki yerde (branch
  öncesinden).
