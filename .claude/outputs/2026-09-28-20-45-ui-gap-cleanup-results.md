# UI eksikleri temizlik turu (P1) — sonuç

- **Branch:** `chore/ui-gap-cleanup` · taban `main` `35ddc41` · son commit `e14a45b`
- **Plan:** `.claude/outputs/2026-09-28-15-36-ui-gap-cleanup-tdd-plan.md`

## Ne yapıldı

| # | Madde | Sonuç | Commit |
|---|---|---|---|
| 1 | Proje listesindeki `⌄ latest` pill'i | Kaldırıldı (hiç görünmüyor, tıklaması bağlı değildi). Çağrılmayan `StickyLayerList.ResumeFollow` gitti. Konsol ve event stream pill'leri aynı. Erişilebilirlik testleri iki pill'e göre yeniden yazıldı | `b954981`, `296d6c8`, `f82eae5` |
| 2 | `RunViewModel.ChangeRepositoryAsync` | Kaldırıldı. 4 test Settings → Save yoluna taşındı, her biri geçici bir kod bozmayla kırmızıya dönerek ayırt edici olduğunu gösterdi. 1 test zaten başka testlerde kapsandığı için silindi | `c6986d3` |
| 3 | Tepsi menüsündeki Stop | Artık `RunViewModel.StopCommand`'a bağlı: derleme yokken gri/pasif, derleme varken etkin, durdurulurken yine pasif. Alt bardaki Stop düğmesiyle aynı kaynak. Önce davranışı koruyan ara adım, sonra iki test kırmızı, sonra düzeltme | `97ae286`, `82569d3` |
| 4 | Bayat kod yorumları | Listedekilerin hepsi düzeltildi. Aynı türden ek olarak `RebuildProjectAsync` özeti ve Exit özetindeki `dotnet build` → `MSBuild.exe` | `442479c` |
| 5 | README General paragrafı | Dört grup (Startup, Build, Branches, Notifications), çalışan iki anahtar (Pull before build, Stash and switch branches), bağlı olmayan dört anahtar. ARCHITECTURE §13.3 zaten doğruydu | `eb5812d` |
| 6 | Test projesindeki 11 derleme uyarısı | Kod düzeltilerek giderildi, susturma yok | `d0d9a58` |
| + | 5 bozuk test literali | `\r` ve `\a` görünmez karakterlere dönüşmüştü (Eylül'den kalma). Test davranışını etkilemiyorlardı | `203f6bc` |
| + | Son review notları | Kopya yasağı (hiç çalışmayan Stop komutu ve "Stop" başlık metni tek yerde) ve birkaç yorum düzeltmesi | `e14a45b` |

## Doğrulama

- Sıfırdan Release build: **0 uyarı, 0 hata** (başta 11 uyarı vardı).
- Her task ayrı review'dan geçti; tüm branch için son review sonucu "merge'e hazır".
- Tam süit `203f6bc`'de: **3613 başarılı, 0 başarısız, 6 atlanan** (atlananlar önceden de atlanan ortam kapılı ölçüm testleri).
- Tam süit `e14a45b`'de: 3 koşuda 3, 2 ve 2 **süre testi** kırmızı. Kırılanlar: `UiResponsivenessBudgetTests`, `ListRealizationPerfTests`, `PopoverTests` pop-in süresi. Başka hiçbir test kırılmadı.
  - Koşular sırasında `-ai` worktree'deki P2 oturumu da test başlattı. Süreç izleyicisi bunu yakaladı: son koşuda 3 kez.
  - Aynı testler, bu branch'in hiçbir değişikliği olmayan `main`'de (`35ddc41`) de aynı koşullarda tek başına koşunca kırmızı.
  - Branch ile `main` dönüşümlü ölçüldü: liste kurulumu ~96 ms'ye ~99 ms. Branch yavaşlatmıyor.
  - `203f6bc` ile `e14a45b` arasında yalnız yorum, bir sabit ve bir paylaşılan komut değişti; bu testlerin kodlarına dokunulmadı.
- Acceptance testleri çalıştırılmadı, çünkü gerçek OSYS çalışma kopyasını derliyor. Oradaki değişiklik yalnız derlenerek doğrulandı.

## Verdiğim kararlar

- `Continue` taraması App tarafıyla sınırlı tutuldu. Motor ve Core tarafındaki Continue/segment anlatılarına dokunulmadı; doğru yazılabilmeleri için o kodun hâlâ kullanılıp kullanılmadığının incelenmesi gerekiyor.
- İkinci instance'ın geçici tepsisine her zaman pasif bir Stop komutu verildi, çünkü o process'te durdurulacak bir koşu yok.
- Tepsi Stop'u için doküman güncellenmedi; ARCHITECTURE ve README tepsi menüsünü madde madde anlatmıyor.
- İki bağlam çıktısı (envanter, karar/prompt dosyası) plan commit'ine eklendi.

## Açık soru (senin kararın)

- **Doküman ile kod uyuşmuyor (bu branch'ten önce de vardı):** ARCHITECTURE §13.3 ve `ApplyRepositoryRoot` içindeki bir yorum, "kök sonradan değişince hiçbir şey sıfırlanmaz, kullanıcı Sync'ler" diyor (`ARCHITECTURE.md` 3048-3049). Kod ise satırları sıfırlıyor, listeyi boşaltıyor ve Save'de tek Sync gönderiyor. Aynı bölümün 3016-3018. satırları ve testler kodla uyumlu. Önerim: kod doğru, doküman ve yorum düzeltilsin.

## Takip önerileri (dokunulmadı)

- Motor/Core'daki Continue kalıntıları: ölü kod incelemesi.
- `BeginRunAsync`'in `clearBuffers` parametresi artık her çağıranda `true`, yani işlevsiz.
- Test sınıflarında `NeverTickingBatcher()` yardımcısının kopyaları; ortak olanı zaten var.
- Süre testleri paralel çalışan bir oturumun yüküne duyarlı. İki oturum aynı anda test koştuğunda kırmızı verebiliyorlar.
- P2 (`feat/build-menu-clean-all`), P1'in üstüne diske dokunmadan denendi ve çakışmasız birleşiyor.
