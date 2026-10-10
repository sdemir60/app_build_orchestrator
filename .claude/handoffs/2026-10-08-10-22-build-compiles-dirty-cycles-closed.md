# Aşama girişi — Build kirli cycle gruplarını derler (kapandı)

## Özet dosyaları

- `.claude/summaries/2026-10-08-08-28-build-compiles-dirty-cycles-merged-ci-red.md` — iş, ölçüm, ilk merge ve develop CI kırmızısı.
- `.claude/outputs/2026-10-08-08-09-build-compiles-dirty-cycles-result.md` — sonuç raporu ve verilen kararlar.
- `.claude/outputs/2026-10-08-08-02-build-compiles-dirty-cycles-measurement.md` — gerçek OSYS ölçümü.
- `.claude/outputs/2026-10-08-05-16-build-compiles-dirty-cycles-plan.md` — uygulanan plan.

## Kaldığımız yer

Açık maddelerin hepsi kapandı ve `fix/cycle-followups-ci` ile develop'a merge edildi: CI'da düşen tur başı paralel hash
testi kanıtıyla LocalOnly'ye alındı, iki grup aynı anda turdayken şerit düzeltildi, iki eksik test eklendi, "defteri
dinleyen modlar" kuralı tek yerde toplandı. Bekleyen grubun bağımlısının bir kez boşuna derlenmesi kullanıcı kararıyla
olduğu gibi kaldı. Gerçek motorla Rebuild testi yüklü makinede bir kez 60 sn bekçisine takıldı (tek başına ve tek
çekirdekte hep geçiyor); teste teşhis eklendi, tekrar koşuda tam süit yeşil. Kullanıcıda: arayüz kontrolü ve Types
üyesi senaryosu. Buradan devam edilecek.
