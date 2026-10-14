# Boşta bellek azaltma — Dalga 1 düzeltme turu ara durumu

**Tarih:** 2026-10-13 20:53 – 2026-10-14 05:51. Arada 02:01'de Windows Update planlı yükseltme için bilgisayarı yeniden başlattı,
05:19'da devam edildi. **Branch:** `fix/idle-memory-wave1`, HEAD `4f1f584d`, ağaç temiz. **Ara sebebi:** kullanıcının haftalık
limitinden %4 kaldı; en uygun anda durdu. **Önceki özet:** `.claude/summaries/2026-10-13-18-05-idle-memory-wave1-progress.md`
(Task 1–5'e kadar).

## Bu bölümde yapılanlar

| Adım | Commit'ler | İnceleme |
|---|---|---|
| Task 5 düzeltme turu 1 (motor kaybında bekleyen yükleme, tek gönderim yolu, atlanan trim'in yeniden istenmesi, kapsam testleri) | `71a1819a` | temiz |
| Task 6–7 `NarrativeStore`: anlatının tek sahibi; konsol görünümü dilim ister, açılışta store'a bağlanır | `44864348`, `0a4751cc` | temiz |
| Task 8 `_liveLines` yalnız bekleyen yüklemenin projesi için; 10 test üretim sırasına çekildi | `405ce981` | temiz |
| Task 9–10: Hakkında → Environment → RUNTIME'a App private / GC committed / Managed heap; bellek bütçe testleri | `8eadeaea`, `a3bfa824` | temiz |
| Dalga 1 genel incelemesi (opus) | — | "düzeltmelerle merge" — Kritik 0, Önemli 2 (I1, I2), Küçük 5; ertelenen 35 maddeden 30'u düzeltilecek |
| Düzeltme A: proje logu isteğine istek kimliği. Motor her chunk'ta yankılar; "log yok" cevabı boş, bayraklı bir chunk ve aynı dikişten geçer | `2cdb3361`, `b6683ad1`, `cf216728`, `4eee0538` | temiz |
| Düzeltme B: konsol/anlatı | `8759fea3`, `5160ed7c`, `b35a7c83`, `ef9dcbf4`, `4f1f584d` | **incelemesi başlamıştı, limit yüzünden durduruldu** |

Düzeltme B'nin içeriği:
- akış bitince belge render dilimine döner;
- büyük batch'in boşa kopyaları kalktı;
- kırpma metin üretmez;
- `BindNarrative` ön koşulu sağlanmazsa fırlatır;
- proje sayfası tek satır kuralıyla bölünür, ölü `LastLines` silindi;
- reseed nesli sentinel'de taşınır;
- store'un `Clear`'ı satır dizisini de bırakır;
- bütçe testi (d) eklendi, ölçüm hijyeni düzeltildi.

Ölçüm testinde oran 1,167'den **1,155**'e indi (bütçe 1,2).

Ölçülen diğer rakamlar:
- Taban tam süit (değişiklik öncesi): 4915 / 0 / 9.
- Bütçe testinde konsol bağı kaldırılınca, yani ikinci kopya geri gelince, oran 2,30 çıkıyor. Bu, tek kopyanın etkisini gösteriyor.

## Sırada (bu sırayla, hiçbirini atlamadan)

1. **Düzeltme B'nin kapsamı dar yeniden incelemesi** (opus). Bulgular: m1, m2, m3, 90, 91, 92, 93, 95, (d), ölçüm hijyeni :97 ve :65.
   Paket: `review-4eee0538..4f1f584d.diff`. Rapor: `fix-b-report.md`. Bulgu çıkarsa düzeltme turu.
2. **Düzeltme C** (opus). Kapsam: `wave1-fix-groups.md` → "Group C" ve "Group C additions" (A'nın incelemesinden gelenler). Not: B,
   `MainWindowHost.NewWithProjectsAndConsole`'un imzasını değiştirdi; C'nin o2 maddesi aynı satırlara dokunacak. Ardından kapsamı dar
   yeniden inceleme.
3. **Dokümanlar** (ARCHITECTURE/README, İngilizce, anlatı üslubu, bayatlayacak rakam yok). Liste:
   - `wave1-final-review.md` → "Recommendations 3";
   - ertelenen 94 ve 103;
   - istek kimliği protokolü (§5.2, §5.3, §5.5) ve §5.3'e okuyucusu olmayan `notFound` alanı cümlesi;
   - logu olmayan karta tıklayınca artık `[error]` satırı yazılmaması;
   - I2 kararı.

   Ardından ayrı bir doküman doğrulaması.
4. **Tam süit** (Debug, ~11 dk, çıktısı dosyaya). Bilinen ortam düşüşleri:
   - `UpdatePillTests` (ekran ölçeği);
   - `StickyLayerHeaderClickTests` (kullanıcı başında değilken fare yakalanamıyor).

   İkisi de değişiklik öncesi kopyada aynen düşüyor.
5. **Ölçüm (spec §9).** Kullanıcının uygulaması kapalı olmalı: `bin\Release`'ten çalışıyor (oturum açılışında otomatik
   başlıyor) ve tek örnek kuralı var.
   - `dotnet build src\BuildOrchestrator.App\BuildOrchestrator.App.csproj -c Release`
   - `.claude\temp\mem-2026-10-13\mem-after-build.ps1 -Dir .claude\temp\mem-2026-10-13\wave1-visible`
   - `mem-tray-run3.ps1 -Dir …\wave1-tray`

   Betiğe iki ölçüm eklendi:
   - `E-postrun-trim-15s`: koşu bitişinden sonra 15 s UI gecikmesi; spec §1'deki "≤ 300 ms duraklama".
   - `W-window-return-20s`: S3'ten sonra pencereye dönüş duraklaması, I2 kararı için.

   Hedef: S1'de App + Supervisor private ≤ 500 MB (taban 845). Sonuç raporu
   `.claude/outputs/<zaman>-idle-memory-wave1-result.md`: taban → sonuç tablosu, S1 satırları, heap döküm ilk 10 satır.
6. **Merge** (`--no-ff` develop'a, push, branch sil, CI kontrol). Kullanıcıya ölçüm sonucu ve rapor yolu yazılır.
7. **Dalga 2 ve 3.** Her biri kendi ön taramasıyla başlar (plan Task 12–17 ve 18–22).

## Açık sorular (kullanıcıda)

- **I2:** Pencereye ≥5 sn sonra dönüşte otomatik bir Sync koşuyor; şu an bu Sync'in bitişi de görünür pencerede bloklayan bir GC
  tetikliyor.
  - "2: a" (öneri): sessiz Sync görünürde toplamaz.
  - "2: b": spec'in harfi, şu anki davranış.

  Cevap gelene kadar b geçerli, Dalga 1 bununla merge olabilir.
- **Task 9:** Cevap gelmediği için spec'teki üç satır uygulandı; kullanıcı isterse değiştirilir.
- **Görsel kontrol:** Hakkında → Environment sekmesinde PATHS grubunun son satırları artık kaydırma gerektiriyor.
- **Tetikleyici:** 02:05 tetikleyicisi yeniden başlatmayla gitti. Yeni limit saati bilinirse yeni tetikleyici kurulur.

## Dosyalar (SDD çalışma alanı: `.superpowers/sdd/2026-10-13-14-38-idle-memory-reduction-plan/`, git-ignored, diskte kalıcı)

- `progress.md`: ledger. Bütün kararlar (`Ruling:`), görev durumları, ertelenenler, kesinti ve devam notları, "NEXT ON RESUME" listesi.
- `wave1-final-review.md`: genel inceleme. I1/I2, küçük bulgular, ertelenen 35 maddenin kararları, doküman listesi.
- `wave1-fix-groups.md`: A/B/C düzeltme grupları ve C ekleri. `wave1-deferred.md`: ertelenenler listesi.
- `fix-a-report.md`, `fix-b-report.md`: düzeltme raporları. `task-*-report.md`, `task-*-decisions.md`, `task-*-brief.md`: görev kayıtları.
- `preflight-wave1-trim.md`, `preflight-wave1-narrative.md`: Dalga 1 ön tarama.
