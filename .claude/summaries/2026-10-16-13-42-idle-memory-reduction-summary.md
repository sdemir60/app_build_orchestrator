# Boşta bellek azaltma — son özet (üç dalga, 2026-10-13 → 2026-10-16)

## Sonuç

Uygulama büyük bir derlemeden sonra boşta çok daha az bellek tutuyor. İş üç dalgada `develop`'a girdi ve **v1.10.0** olarak
yayınlandı.

| Dalga | Ne getirdi | Boşta App + motor (koşu sonrası) |
|---|---|---|
| Taban (2026-10-13) | — | 845 MB |
| 1 (`a51203fe`) | İşlem sonu bellek trimi; anlatının tek sahibi; canlı satır tamponu daraltıldı; proje logu istek kimliği | 349 MB |
| 2 (`f50478dd`) | Anlatı diske yazılır (`narrative.log`), bellekte son 2 000 satır kalır; Compact console ayarı | 276 MB (gece ölçümü) |
| 3 (`dfe7924f`) | İşsiz motor 5 dk sonra uyur, ilk komutta uyanır (Stop the engine when idle) | **236 MB** (motor 0); tepside **230 MB** |

- Dalga 3 aynı gün, aynı ekran durumunda Dalga 2 programıyla kıyaslandı.
  - Dalga 2 boşta 285–312 MB tutuyor, çünkü motoru açık kalıyor.
  - Dalga 3'te 236 MB tutuyor.
  - Rapor: `.claude/outputs/2026-10-16-13-25-idle-memory-wave3-result.md`.
- **Ölçüm dersi:** harici 4K monitör açıkken App yaklaşık +120 MB yerel çizim belleği tutuyor. Bu fark programdan değil ekrandan geliyor; ABAB ölçümüyle gösterildi. Bellek ölçümleri aynı ekran durumunda kıyaslanmalı (bellekteki not).

## Dalga 3'te yapılanlar

- **Motor uykusu.**
  - Tek yaşam döngüsü kapısı; uyku ve uyanma; çıkış her ikisinin de önüne geçer.
  - Uyandırma kapısı ve tek "uyandırır mı" tablosu.
  - Uyanırken şerit `▸ Starting the engine…` yazar; Hakkında'da PID "asleep" görünür.
  - Ölüm metinleri: "(idle shutdown failed)" ve "(wake failed)", ikisi de Restart sunar.
  - Uyanan motor önceki koşunun proje logunu verir.
  - Yarım koşuyu kurtaran uyanmadan sonra sessiz bir Sync koşar.
  - Ölçüm: uyanma 96 ms; ilk Build'e ek maliyeti ~1,3 s; UI donması yok.
- **Konsol (karar 44–46).**
  - Proje sayfası logun tamamıdır, ilk satırdan açılır ve hiç takip etmez.
  - PageUp/PageDown bir sayfa kaydırır; gezinme tuşları görünümü kaydırır.
  - Dikey jestler paneli alır ve kayma animasyonunu durdurur. Yana jestler paneli almaz; panel okuyucudayken 5 sn'lik dönüşü uzatır.
  - Takip isteği sonlu ve dikeydir: yana okuma yeni satırlarda korunur, End tam dibe iner.
  - İmleç yalnız canlı dipte görünür.
- **Hijyen (karar 47).**
  - General ayarlarının varsayılanı yalnız katalogda; bunu bir literal guard'ı koruyor.
  - Zincir testleri tek teoride toplandı; test yardımcısı kopyaları tek yerde.
  - Ölü backlog listesi kaldırıldı.
- **Son toparlama.** 22 madde; incelemelerin küçük bulgularının hepsi kapandı.
- **Doğrulama ve dokümanlar.**
  - Tam süit: 5 521 geçti; tek düşüş yükten kaynaklı, tek başına geçti. CI yeşil.
  - Dokümanlar: ARCHITECTURE §4.7 yeni, yaklaşık 25 bölüm güncellendi; README güncellendi.

## Süreç

- Motor kararları 4, konsol kararları 3 tur sürdü. Her bulgu yeni bir tura dönünce saatler ve limit gitti; kullanıcı bunu haklı olarak eleştirdi.
- Ders belleğe yazıldı: parti başına tek düzeltme turu; küçük bulgular tek son toparlamaya ya da takip listesine.
- Gece ve sabah iki oturum limiti doldu, bir de haftalık Opus alt ajan limiti. Her kesintiden sonra iş kaldığı yerden tamamlandı; atlanan konu yok.

## Yayın

- `release: v1.10.0` (`8151879b`), `main`'e `merge: release v1.10.0` (`48dc5db3`). `main` ve `develop` birlikte gönderildi.
- GitHub `release` akışının üç adımı da yeşil: kontrol, testler, yayın.
- Tag `v1.10.0` `48dc5db3`'te. **Yayınlandı** 2026-10-16 13:54: https://github.com/sdemir60/app_build_orchestrator/releases/tag/v1.10.0
  - Kurulum `BuildOrchestrator.App-win-Setup.exe`.
  - Tam ve fark paketleri `-full`/`-delta.nupkg`.
- Kurulu kopyalar kendi güncelleme denetimiyle 1.10.0'ı alır.
- CHANGELOG 1.10.0 bölümü:
  - Added: Stop the engine when idle, Compact console.
  - Changed: proje sayfası, konsol tuşları ve jestleri, katmansız graf.
  - Fixed: proje logu karışması, konsol kaydırma, nadir süreç çıkış kodu hatası.
  - Performance: boşta bellek.
- Hakkında'daki bellek satırları bilerek duyurulmadı: sahip onları orada istememişti.

## Sahibin takip listesi (bu işin dışında kalanlar)

1. Proje sayfası açıkken log iki kopya tutuluyor: VM'deki metin ve editörün belgesi.
2. Yol normalleştirme 8 ayrı yerde kopyalanmış.
3. Motorun komutları için ortak bir hata sınırı yok.
4. Testlerde yaklaşık 35 görünüm modeli fabrikası kopyası var.
5. Beş ayar 7 yerde elle sayılıyor (tasarım kararı).
6. Koşu dışındaki birkaç tanı satırı, motorun hata kanalı kapalıysa istisna fırlatabilir.
7. Motor öldükten sonra Hakkında ölü sürecin PID'ini gösteriyor.
8. Harici 4K monitörde yaklaşık +120 MB yerel çizim belleği tutuluyor.

## Bellek göstergesi (sahip istemişti: sağ alttaki performans hapında, minimal)

Aday ölçüler:
- App private
- motor private
- toplam
- GC heap / GC committed
- CPU %
- çalışan MSBuild işçisi sayısı
- motorun durumu (çalışıyor / uyuyor / başlatılıyor)

Hakkında'daki üç bellek satırının kalıp kalmayacağına sahip karar verecek. Tasarımı sahip yaptıracak.
