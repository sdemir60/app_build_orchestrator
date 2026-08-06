# Quiet Graph — gözle doğrulama listesi

Kodla kapanamayan maddeler. Yan yana bakılır: bir yanda
`.claude/outputs/2026-08-05-01-26-design-v1.3.0/Build Orchestrator (standalone).html` (çift tıkla, tarayıcıda
çalışır), diğer yanda gerçek OSYS reposuyla açılmış uygulama.

```powershell
dotnet run --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj
```

> Uygulama açıkken build alma — çalışan Supervisor kendi binary'lerini kilitler.

---

## 1. Ölçülmüş sapmalar — KARAR gerektiren

Bunlar hata değil, bilinçli ve gerekçesi ölçülmüş tercihlerdir. Gözle bakıp "kabul" ya da "başka bir yol
bulalım" demek gerekiyor.

| # | Sapma | Gerekçe (kodda da yazılı) | Bakılacak |
|---|---|---|---|
| S1 | **Renk geçişi yok.** §2.3 "zemin/kenar/glyph 380ms ease-standard" der; renkler ANINDA uygulanıyor. | WPF'te fırça interpolate edilemez ⇒ düğüm başına 3 yerel fırça + 3 ColorAnimation. Ölçüldü: 177 proje aynı tick'te statü değiştirdiğinde tick 11 ms → 51 ms, UI olay bütçesini (50 ms) aşıyor. | Koşu başlarken (hepsi Discovered → Queued) ve biterken renk sıçraması rahatsız edici mi? |
| S2 | **Pan kelepçesiz.** Grafı panelin dışına sürükleyebiliyorsun. | Tuval = panel olduğu için bir öteleme kelepçesi, ölçek 1'in altındaki her seçimde odakla-sığdırı eziyordu. Tasarımın kurtarma jesti var: boş alana tıkla → varsayılan görünüm. | Grafı uzağa sürükleyip boş alana tıkla — gerçekten geri geliyor mu? "Kayboldum" hissi veriyor mu? |
| S3 | **Cull kaldırıldı** — 500/1000 düğümlü graf Sync'te TAMAMEN kuruluyor (~130 / ~300 ms). | Graf panele tam sığdığı için eleyecek bir şey yok; materyalizasyon tek yönlüydü, yakınlaşmak da bir şey kazandırmıyordu. | Gerçek OSYS (177 proje) Sync'inde açılış takılıyor mu? |

---

## 2. Kodla kapanmayan görsel maddeler

1. **8px düğümde üç opaklık kademesi (RİSK #2).** 177 projelik bir koşuda 0.13 (soluk) / 1.0 (derlenen) /
   0.2 (biten) birbirinden ayırt edilebiliyor mu? Edilemiyorsa bu bir TASARIM sorunudur — eşik kodda
   sessizce değiştirilmez, bildirilir.
2. **8px karede %52 glyph** (≈4px, 1.8px stroke) görünür mü, yoksa gürültü mü? Pitch tabana indiğinde
   (çok büyük workspace) kutu neye benziyor?
3. **Beads.** 8–24px düğümün 2.8px dışında gerçekten "sık noktalar" gibi mi görünüyor, yoksa kesintisiz bir
   halka mı? Ek yerinde bindirme/boşluk var mı? 4.2 saniyelik tur çok mu yavaş?
4. **Beads giriş/çıkış.** Bir proje bitince noktalar DÖNERKEN mi sönüyor, donup mu kayboluyor?
5. **Hold-fade ritmi.** Biten proje 2.4sn parlak kalıp 0.7sn'de sönüyor mu? Paralel 6 projede göz yoruluyor mu?
6. **Splitter sürüklerken** yerleşim yeniden hesabı akıcı mı — takılma, zıplama, düğümlerin "kaynaması" var mı?
7. **Tooltip.** Her zoom'da net mi (ölçeklenmiyor mu)? Panel kenarındaki bir düğümde tamamen okunuyor mu?
   Gecikmesiz açılışı sinir bozucu mu?
8. **Seçim odağı.** Çok bağımlısı olan bir proje seçildiğinde (geniş odak kümesi) zoom 0.7 tabanına iniyor —
   okunabilir kalıyor mu? Kenardaki bir düğüm seçildiğinde çerçeveleme doğru hissettiriyor mu?
9. **Seçim çizgileri.** Amber akan kesikler 640ms'de çok mu hızlı? 1.2px çizgi near-black zeminde görünüyor mu?
10. **Açılış dalgası.** Üstten alta / soldan sağa akıyor mu? 177 projede 520ms tavanı doğru hissettiriyor mu,
    yoksa son 120 proje tek blok gibi mi patlıyor?
11. **Reduced-motion.** (Windows → Ayarlar → Erişilebilirlik → Görsel efektler → Animasyon efektleri KAPALI)
    beads, akan çizgiler, açılış dalgası ve kamera geçişi TAMAMEN kapalı mı?
12. **`list` / `focus` moduna geçince** koşu sürerken CPU düşüyor mu (gizli panel kapısının gözle karşılığı)?

---

## 3. Uygulanan kapsam

design v1.3.0 §2.3'ün tamamı, S1 dışında:

- Otomatik pitch + katman bantları, panele tam sığma, eksik satır ve blok ortalaması
- İsimsiz mini node (pitch×0.6, 8–24), radius-sm, 1.5px border, %52 box glyph, discovered kesikli
- Koşu opaklık sistemi (0.13 / tam / 0.2) + hold-fade (2400/700, BeginTime'lı tek atım)
- Beads (2.8px dışta, çevreye tam bölünen desen, 4200ms, tek paylaşımlı saat, 420/640 giriş-çıkış, 700ms spin-down)
- Hover (1.7× / 120ms / 2px border / opaklık 1 / z-order) + gecikmesiz ekran-koordinatlı tooltip
- Seçimde odakla-sığdır (0.7–2.6, pad 3×node+48, 460ms) + odak dışı 0.1 + focus ring
- Seçim kenarları (dikey bezier, amber akan 4/8 kesik, 640ms, tek saat) + ad etiketi
- Serbest gezinme (wheel 0.7–5.0 ×1.14, 3px sürükleme eşiği, boş-alan tıklaması)
- Açılış dalgası (build-order index × 9ms, tavan 520ms)
- Sağ alt ipucu satırı
- Kaldırılanlar: node üstü etiketler, etiket LOD'u, kalıcı kenar ağı, kenar sisi, graf içi dep-issue rozeti,
  frontier kamerası, takip dönüşü, FOLLOW PAUSED pili, viewport cull, `FullDetailMaxNodes` kapısı, ClampPan
