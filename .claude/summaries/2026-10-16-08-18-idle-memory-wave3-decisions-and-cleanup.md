# Boşta bellek azaltma — Dalga 3: sahip kararları, hijyen ve son toparlama (2026-10-15 15:00 → 2026-10-16 08:18)

## Durum
- Branch `fix/idle-memory-wave3`, HEAD `4ddc82be`, ağaç temiz.
- Oturum `develop`'ta bırakıldı. Branch'e `git switch fix/idle-memory-wave3` ile dönülür.
- `stash@{0}`: son toparlamanın yarım ve **doğrulanmamış** işi. Kapsamı madde 6, 7, 15/19 ve 20: KeyInput yerleri, MsBuildLines toplama, Invoke ikizleri, CommandPress.Press.
- Üç parti kapandı. Her birinin son incelemesinde kritik ve önemli bulgu sayısı 0.
  - **Motor kararları** (A, B, D, 40, M-1, M-4/42a): 4 tur.
  - **Konsol kararları** (44–46): 3 tur.
  - **Hijyen** (47 + O2 adım 2): 1 tur.
- Son toparlama (`final-cleanup-brief.md`, 22 madde):
  - Yapılan: 1, 2–5, 9–14, 16, 17 (`4271dc94`, `71a25ca7`, `bdc63a60`, `c8298266`, `4ddc82be`).
  - Kalan: 6, 7, 8, 15/19, 18, 20, 21, 22. Bunların bir kısmı stash'te.

## Bu bölümde yapılanlar
1. **Motor:**
   - Uyanan motor önceki koşunun proje logunu verir: istek koşu klasörünü taşır.
   - Uyanırken şerit `▸ Starting the engine…` gösterir.
   - Hakkında penceresinde PID "asleep" görünür.
   - Uyanma başlatılamazsa Restart sunulur.
   - Yarım koşuyu kurtaran uyanmadan sonra sessiz bir Sync koşar. Bu Sync, uyanmayı bekleyen her istek motora teslim edildikten sonra gider.
   - Okunamayan proje logu tek yerde "log yok"a çevrilir ve tanı satırı bırakır.
   - Kapı durumu `EngineGate` sınıfında toplandı.
   - Kapı girişini koruyan bir guard eklendi.
   - Motor kaybında her uyandıran komut türü sınıflandırıldı; bunu bir sürüklenme testi koruyor.
2. **Konsol:**
   - Proje sayfası logun tamamıdır. İlk satırdan açılır ve hiç takip etmez.
   - PageUp/PageDown bir sayfa kaydırır. Gezinme tuşları imleci değil görünümü kaydırır.
   - Pill, içerik büyüyünce yeniden okunur. İmleç yalnız canlı dipte görünür.
   - Her dikey kullanıcı jesti pill animasyonunu tek yerden durdurur.
   - Yana jestler (←/→, yatay tekerlek, yatay çubuk) direksiyonu almaz. Okuyucu paneli tutarken 5 sn'lik dibe dönüşü uzatırlar.
   - Takip isteği sonlu ve dikeydir: yana okuma yeni satırlarda korunur, End tam dibe iner.
   - Ctrl+End ölçüldü. Gerçekçi satırlarla 10 bin satırda yaklaşık 6 ms, 100 bin satırda yaklaşık 11 ms sürüyor; kod değişmedi.
3. **Hijyen:**
   - General ayarlarının varsayılanı yalnız katalogdadır (`DefaultOf`).
   - Zincir testleri tek teori sınıfında toplandı.
   - Test yardımcı kopyaları tek yerde toplandı.
   - Konsolun ölü backlog listesi kaldırıldı.
4. **Süreç dersi** (belleğe yazıldı): her inceleme bulgusunu yeni tura çevirmek saatler ve kullanıcının limitini yedi.
   - Artık parti başına tek düzeltme turu var.
   - Küçük bulgular tek son toparlamaya ya da sahibin takip listesine gider.
   - Tahmin tur sayısıyla birlikte verilir.
   - Gece iki oturum limiti (01:50 ve ~06:50) beklemeye yol açtı.

## Devam planı
1. Branch'e dön ve stash'i aç: `git switch fix/idle-memory-wave3`, ardından `git stash pop`.
   - Derle (`-m:2`), etkilenen sınıfları odaklı koş, doğrula ve commit et.
   - Doğrulanamazsa stash'i at ve o maddeleri brief'ten baştan yap.
2. Son toparlamanın kalanını tamamla:
   - 8: guard'ı `SourceLiterals.CodeOnly`'ye geçir.
   - 18: literal varsayılan guard'ı.
   - 21: varsayılan değerleri tekrarlayan doküman nesri.
   - 22: tablo erişimci adları.
   - Talimat `final-cleanup-brief.md`, rapor `final-cleanup-report.md`. Ardından tek bakışlık son kontrol yapılır, yeni tur açılmaz.
3. T22'yi sahip başlatır; plan `task-22-plan.md`'de.
   - Dokümanlar: ~1,5–2 sa.
   - Tam süit: ~20 dk.
   - Ölçüm: ~1 sa, makine boş olmalı.
   - Sonuç raporu, merge ve CI: ~40 dk.
   - Son özet ve bellek göstergesi hatırlatması.

## Dosyalar (hepsi `.superpowers/sdd/2026-10-13-14-38-idle-memory-reduction-plan/` altında)
- Kurtarma haritası: `progress.md`. En son giriş "SESSION PAUSED 2026-10-16 08:18".
- Kararlar: `task-18-22-decisions.md` (37–48, 42a).
- Talimatlar:
  - `b1-round3-brief.md`, `b1-round4-brief.md`
  - `b2-round3-brief.md`
  - `b3-hygiene-brief.md`
  - `final-cleanup-brief.md`
- Raporlar:
  - `w3-b1-report.md`, `w3-b2-report.md`, `w3-b3-report.md`
  - `final-cleanup-report.md` (yarım olabilir)
- T22 planı: `task-22-plan.md`. Tüm doküman maddeleri ve sahibin takip listesi burada.
