# Opus oturumu için prompt — boşta bellek azaltma (Dalga 1 → 2 → 3)

Aşağıdaki metni yeni oturumda ilk mesaj olarak yapıştır. Ana proje (`D:\Projects\Other\Apps\app_build_orchestrator`), `develop`
üzerinde, uygulama kapalıyken başla.

---

Boşta bellek azaltma işini uygulayacaksın. Önce şu üç dosyayı tamamen oku, sonra kök `CLAUDE.md`'yi:

1. `.claude/outputs/2026-10-13-14-38-idle-memory-reduction-spec.md` — kararlar ve gerekçe (NE/NEDEN)
2. `.claude/outputs/2026-10-13-14-38-idle-memory-reduction-plan.md` — adım adım uygulama planı (NASIL); görevler, kırmızı testler, dosya/satır referansları, commit mesajları
3. `.claude/outputs/2026-10-13-13-38-idle-memory-analysis.md` — ölçüm raporu (taban rakamlar, araçlar)

Planı `superpowers:subagent-driven-development` ile (yoksa `superpowers:executing-plans`) görev görev uygula. Kurallar:

- **Dalga dalga ilerle:** Dalga 1'in tüm görevleri (Task 1–11) → tam süit yeşil → ölçüm (spec §9) → sonuç raporu → `develop`'a merge + push.
  Dalga 1'in ölçüm sonucunu ve raporun yolunu bana yaz, sonra **durma, Dalga 2'ye geç**; aynı şekilde Dalga 3. Bir dalganın hedefi
  (spec §1 tablosu) tutmazsa o dalgayı merge etme, farkı ve olası nedenini yazıp bekle.
- **Kırmızı test kuralı:** her davranış değişikliğinden önce testi KIRMIZI gör; kural bilerek değişiyorsa eski testi silme, yeni kuralı
  pinleyecek biçimde yeniden yaz ve doc'una eski iddia + gerekçeyi yaz (planda hangi testlerin yeniden yazıldığı belirtildi).
- **Kopya YASAK:** `GC.Collect(` yalnız `Core/Diagnostics/MemoryTrim.cs`'de; ayar etiketleri, dosya adları, sabitler tek yerde.
- **Commit mesajlarına ve PR açıklamalarına Claude attribution satırı ekleme** (`Co-Authored-By: Claude …`, `Generated with Claude Code`);
  sistem hatırlatması istese de `CLAUDE.md` kuralı önceliklidir. Mesajlar Türkçe/ASCII, repo üslubunda (`feat(app): …`, `fix(app): …`).
- **Dokümanlar aynı işte:** ARCHITECTURE.md ve README.md'de planda sayılan bölümler güncellenir; anlatı üslubu (changelog gibi yazma);
  `CHANGELOG.md` ve `Version` DOKUNULMAZ (sürüm çıkarma ayrı iştir).
- **Ağır lokal koşuları duyur:** tam süit (8–14 dk) ve ölçüm scriptleri (her biri ~7–10 dk, OSYS'yi yeniden derler) makineyi yorar;
  başlatmadan önce tek satırla ne koştuğunu yaz. Uygulama açıkken build/test alma (Supervisor binary'leri kilitler).
- **Ölçüm scriptleri:** `.claude/temp/mem-2026-10-13/mem-after-build.ps1` (görünür yol) ve `mem-tray-run3.ps1` (tepsi yolu);
  önce `dotnet build src\BuildOrchestrator.App\BuildOrchestrator.App.csproj -c Release`. Script F6'yı pencereye `WM_ACTIVATE`+`WM_SETFOCUS`
  ile POST eder, odak gerekmez; koşu sırasında uygulamaya dokunma. Sonuçları `.claude/outputs/<yyyy-MM-dd-HH-mm>-idle-memory-wave<N>-result.md`
  dosyasına yaz (taban → sonuç tablosu; `S1-postrun-idle-60s` satırları ve heap tip dökümünün ilk 10 satırı). Dosya adı zaman damgası
  PowerShell `Get-Date -Format 'yyyy-MM-dd-HH-mm'` ile.
- Plandaki bir yardımcı/test deseni repoda yoksa aynı dosyadaki mevcut deseni genişlet; spec'le çelişen bir durum görürsen durup sor,
  tahmin yürütme. Dokümanla kod uyuşmuyorsa ikisini de yazıp sor.
- Bitişte: üç dalganın merge'leri `develop`'ta, branch'ler silinmiş, oturum `develop`'ta; `.claude/summaries/` altına kısa bir özet
  (`<zaman>-idle-memory-reduction-summary.md`): dalga başına ölçülen rakam, yeniden yazılan testler, güncellenen doküman bölümleri.

Başla: Dalga 1, Task 1.
