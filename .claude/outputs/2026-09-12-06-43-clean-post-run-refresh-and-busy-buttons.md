# Clean Sonrası Tazeleme ve Koşan Bakım Butonu — Uygulama Sonucu

> Bu dosya bir **kayıttır**, prompt değil. `feat/clean-button-engine-v2` üzerindeki Clean motorunun ÜSTÜNE
> gelen ikinci turdur; motorun kendi kaydı `.claude/outputs/2026-09-10-19-39-clean-button-engine-v2-results.md`.

| | |
|---|---|
| **Branch** | `feat/clean-button-engine-v2` (yalnız LOCAL) |
| **Taban** | birinci turun son commit'i (`12fefdc`) |
| **İsteyen** | kullanıcı, iki kusur/eksik tarifi: (a) Clean sonrası ekran bayat kalıyor, (b) tasarımdaki koşan-buton hali yok |
| **Durum** | Tamamlandı, tam süit yeşil, merge EDİLMEDİ |

---

## Ne değişti

| Task | Commit | İçerik |
|---|---|---|
| A | `1cc76ba` | `cleanStarted` satırların kararlarını düşürür (hollow reset); `ResetRowsToHollow` üçlü bloğun tek sahibi |
| B | `9c14151` | `cleanCompleted` konsol korunarak Sync zincirler; hata yolunda zincir yok |
| C | `2214802` | Clean ve Resolve düğmeleri koşarken amber zemin + spinner |
| D | (doküman commit'i) | ARCHITECTURE §5.3/§13.2/§14.4/§22 + README bakım kutusu paragrafı |

## Kararlar ve gerekçeleri

**K-11. Liste BOŞALTILMAZ, kararları boşaltılır.** Kullanıcının ilk önerisi "graf ve proje listesi temizlensin"
idi. Koleksiyonu gerçekten boşaltmak panelin `No projects found under this folder.` demesine yol açardı ve bu
YANLIŞ olurdu — klasörde projeler var. Clean tek bir csproj'a dokunmadığı için topoloji de geçerliliğini
korur. Uygulanan: branch ve repo değişiminin kullandığı hollow reset (durum `Pending`, karar yok, süre yok,
dep uyarısı yok, şeritteki `to build` sayacı sıfır).

**K-12. Reset'in tetikleyicisi tıklama DEĞİL, motorun kabulü (`cleanStarted`).** Gönderim senkron düşerse ya da
Supervisor `cleanRejected` derse hiçbir şey silinmemiştir; o yollarda ekranın bozulması bedelsiz bir kayıp
olurdu. Konsolun tıklama anında temizlenmesinden farkı milisaniyelerdir. İki test bunu pinler ve mutasyonla
(reset'i tıklamaya taşıyarak) kırmızı gösterildi.

**K-13. Bitişte otomatik Sync, konsol korunarak.** `N behind` chip'indeki pull'un birebir deseni
(`SyncCoreAsync(clearBuffers: false)`). Gerekçe ekranın doğruyu söylemesidir, motorun ihtiyacı değil: bir
sonraki Build zaten sıfırdan planlar. Ama K-11 kararları düşürdüğü için onları geri getirecek tek yer motorun
analizidir; kullanıcıya elle Sync bastırmak, uygulamanın kendi yapabildiği işi ona yüklemek olurdu. Sıra önemli:
`CleanBusy` açıkken Sync'in kapısı kapalı olduğu için ÖNCE yüzey bırakılır.

**K-14. Hata yolunda zincir yok.** `cleanFailed`/`cleanRejected` sonrası Sync koşmaz: hata zaten konsolda,
ikinci bir hata satırı yalnız gürültü olurdu. Satırlar hollow kalır, Sync kullanıcıya kalır.

**K-15. Koşan düğme amber + spinner; komut kapısı DEĞİŞMEZ.** Tasarım (BuildApp.jsx:2619-2622/2639-2641) koşan
bakım düğmesini DS'in `active` haline alıp ikonun yerine spinner koyar ve koşan düğmeyi disabled kümesinin
DIŞINDA tutar. Bizde düğme komut kapısı yüzünden pasiftir, o yüzden yalnız BOYAMA değiştirildi: amber-soft
zemin (`Ds.IconButton.Toggle`'ın `IsChecked` tetikleyicisiyle aynı token çifti), `BuildingSpinner` (kendi
varsayılan stili amber boyar ve azaltılmış harekette döndürmez) ve opaklık 1 — `Ds.Button.Base` pasif düğmeyi
0.45'e söndürüyor, koşan iş sönük görünmemeli. İş bitince yerel değerler `ClearValue` ile temizlenir. Optimize'ın
gösterecek bir işi yok.

## Test notu — yanlış testin düzeltilmesi

"Hata yolunda zincir yok" testinin ilk hâli mutasyonla KIRMIZI VERMEDİ: kurulumu gönderimi senkron düşen bir
Clean'di, orada `CleanBusy` false olduğu için `TryConsumeCleanFailure` hiç çalışmıyor ve test hiçbir şeyi
pinlemiyordu. Kural gereği (kırmızıyı gösteremiyorsan test yanlıştır) kurulum uçuştaki `cleanStarted`'a
çevrildi; ardından iki kod için de kırmızı görüldü.

## Dokümanlar

- **ARCHITECTURE §5.3** — `cleanStarted`'ın satırları hollow'a aldığı, `cleanCompleted`'ın Sync'i zincirlediği.
- **ARCHITECTURE §13.2** — "Sync gerekmez" ifadesi kalktı (artık kendiliğinden koşuyor); yeni paragraf kararların
  neden düştüğünü, listenin neden boşaltılmadığını, tetikleyicinin neden kabul olduğunu ve hata yolunda zincir
  olmadığını anlatıyor. Sync-kapısı paragrafına koşan düğmenin amber + spinner hâli eklendi. Pill paragrafındaki
  "canlı durumu konsol söyler" cümlesi düzeltildi: artık düğmenin kendisi de söylüyor.
- **ARCHITECTURE §14.4** — bakım kutusunun spinner'ı ikonun yerine geçiyor.
- **ARCHITECTURE §22** — kod haritasına hollow reset satırı; bakım kutusu ve Clean komutu satırları genişletildi.
- **README** — bakım kutusu paragrafı: buton amber + spinner, bitişte kendiliğinden Sync, konsol korunur.

## Açık kalan

Şeritteki pill Clean sırasında `DEEP CLEAN` yazar ama amber yanmaz (`StickyRibbon.RefreshOpPill`'in canlı
ölçütü `IsMidRunLocked || Phase == Syncing`; K-7 Clean için yeni faz açmayı yasaklar). Bu tur bakım kutusundaki
düğmeyi canlı hale getirdiği için kullanıcı için sorun büyük ölçüde kapandı; pill'in de amber yanması istenirse
ayrı bir iştir.
