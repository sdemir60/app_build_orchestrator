# A13 TAMAMLANDI ve `main`'e merge edildi (2026-08-03 10:44)

`main` @ **`d99cc38`** · `origin/main` ile **birebir** · çalışma branch'i silindi · oturum `main`'de.
Süit **1650 passed / 2 skipped / 0 failed** · guard **69/69** · build **0 Uyarı / 0 Hata**.

---

## 1. A13 ne yaptı

**11 task** (T1-T6 · B0-B4) + **final tüm-branch review** (3 lens) + **final fix dalgası** + kontrolcü turları.
**107 commit**, 133+ dosya, süit **1433 → 1650** (+201 test metodu, 1260 → 1461).

| Ölçü | Değer |
|---|---|
| Gezintinin toplam alt kalemi | **228** |
| Testle pinli | **217** |
| Göz isteyen | **20** |
| Ne test ne göz (özellik boşluğu) | **1** — ribbon `· N warnings` |

Aritmetik bağımsız kapandı: +201 metot, 7'si theory (+23 `InlineData`) = +217 vaka; 1433 + 217 = 1650,
eksi 3 `Category=Acceptance` = 1647 (fix dalgasından önce).

### Kapatılan gerçek üretim kusurları
- **`ConsoleView.OnMotionChanged`** uçuştaki imleç fade'ini bayrağı temizlemeden söküyordu → `fade.Completed`
  hiç ateşlenmiyor, **run'ın son satırı kalıcı olarak kayboluyordu**.
- **`debugSpawnChildren`** (`cmd.exe` çocuğu doğuran test kancası) **üretim ikilisinde de dinleniyordu** →
  artık `--debug-hooks` olmadan reddediliyor; IPC sözleşmesi korundu (komut tanınıyor, yalnız reddediliyor).
- **`StickyLayerList` reveal kapsamı** sertleştirildi; ulaşılamaz ikinci kademe telafi (`LayoutUpdated` kancası)
  tümüyle söküldü.
- Kullanıcıya sızan **62 Türkçe metin** İngilizceye çevrildi + kalıcı tokenizer guard'ı eklendi.

### Bu oturumda üretilen iki final çıktısı
- `.claude/outputs/2026-08-03-09-44-visual-check-residue.md` — 20 göz-ister kalem, **uygulama gezinti
  sırasıyla** (pencere 3 · sol panel 5 · graf 3 · konsol 0 · aksiyon çubuğu 2 · popover 1 · ayarlar 2 · tepsi 4),
  her biri tek satır "ne yap / ne görmelisin".
- `.claude/outputs/2026-08-03-09-44-parked-items-triage.md` — **106 karar satırı**: KAPANDI 14 (commit
  doğrulandı) · TAŞINIYOR 80 · KAPATILDI-GEÇERSİZ 12 · +2 doğrulanamayan kapanış beyanı.

---

## 2. Final tüm-branch review (3 lens, 948 KB diff üç pakete bölündü)

| Lens | Verdict | Sonuç |
|---|---|---|
| A — üretim kodu | Kritik 0 · Önemli 4 | 2 tek-satırlık düzeltme istendi |
| B — testler | Kritik 1 · Önemli 2 | **merge engeli yok** |
| C — belgeler | Kritik 1 · Önemli 5 | 4 belge engeli (hepsi düzeltildi) |

**Lens A:** A13.2'nin dört kuralı tek tek denetlendi, dördü de temiz; §6.1 nested Job zinciri dokunulmamış;
`Contracts`'ta **sıfır** dosya değişmiş (IPC geriye uyumlu); `LayoutUpdated` kancasından kalıntı yok.

**Lens B:** beş testi örnekledi, dördü ayırt edici çıktı. Beşincisi çıkmadı ve bu turun en değerli bulgusuydu
(aşağıda). Süit 3 bağımsız koşumda 1647/2/0, **flake sıfır** (7 süit-ölçeği koşum).

**Lens C:** aralıkta `README.md` ve `CLAUDE.md` **hiç değişmemiş**; değişen tek gerçek belge
`docs/TRUST-BOUNDARY.md`. Belgenin güvenilirliğini de ölçtü: 137 benzersiz ref / 38 dosya, elle okunan 34
ref'in 26'sı doğru (%76), hataların tamamı `RunCoordinator`/`RunViewModel*`/`EngineHost` ekseninde,
`Core/*` örneklemede %100. Belgenin kendi verdiği "bilinen bayat örnekler" gerçekten bayat → **belge dürüst**,
tam tazeleme ayrı task (A14).

### Kapatılan dört belge engeli (kontrolcü uyguladı, `b1ab4d0`)
1. **Süit komutu üç uçlu çelişki:** `README.md:77` komutu filtresizdi ama `:83` metni filtreli diyordu;
   `CLAUDE.md:45` de filtresizdi. **Filtreli yön kazandı** — kodun kendi politikası aynı yönde
   (`OsysRebuildAcceptanceTests.cs:26-27`), acceptance testi sayısı ölçüldü: **tam 3**.
   *(Kullanıcı kararı: "en sağlıklı en doğru çözümü uygula".)*
2. **`README.md:114` "16 checks" bayatlamıştı** → bugün 22 (23 `Check` çağrı yeri, biri yalnız hata yolunda).
   Aradaki 7 check A13/T6'nın Sync+Build adımıydı ve **anlatıda hiç geçmiyordu** → yazıldı.
3. **Bu branch'in kendi kırdığı üç `TRUST-BOUNDARY` referansı** (B4 aynı sınıfı `Program.cs` için düzeltti ama
   sweep dar kaldı). Üçü de `8e6ebbe`'de doğruydu; kontrolcü üçünü de koda karşı doğruladı. Sonuncusu bir
   **güvenlik argümanının** iki ayağını taşıyordu — ikisi de yanlış yere gösterince argüman doğrulanamaz
   hâle geliyordu.
4. Provenance çıpası `3243d4a` → `0b72803`.

`it5-records.md` §2'de satırlar **silinmedi** (kapalı bir iterasyonun kabul kaydı); tablonun üstüne 6 geçersiz
satırı commit'leriyle sayan uyarı bloğu eklendi.

---

## 3. Yöntemin verdiği asıl ders

**Üç kez** bir uygulayıcı "kırmızı ölçüldü" diye raporladı ve review lens'lerinin mutasyonları bunun **doğru
olmadığını** gösterdi:

1. **E5 testi düzeltmeyi pinlemiyordu.** İki lens iki ayrı yöntemle ölçtü (biri dosyayı `e2e50aa`'ya döndürdü
   → 11/11 yeşil; diğeri izole kopyada aynısını yaptı → 4/4 yeşil + probe `rowsCollected=4`). Sonuçta düşme
   penceresinin **üretimden ulaşılamaz** olduğu kanıtlandı, iddia "kusur düzeltmesi"nden **sertleştirme**'ye
   indirildi ve ulaşılamaz ikinci kademe telafi söküldü.
2. **`debugSpawnChildren`'ın ilan edilen kazancı pinsizdi.** Kapı spawn döngüsünden sonraya taşınınca —
   üretim ikilisi gerçekten `cmd.exe` doğururken — **1646 testin tamamı yeşil kaldı**. Negatif test yalnız
   "cevap doğru"yu pinliyordu, "davranış doğru"yu değil.
3. **`CursorHoldMs` testinin kanıtı hold'un değil fade'in süresiyle karşılanıyordu.** Üç üretim tüketim noktası
   birden silindiğinde süit yeşil kalıyordu.

Üçü de **kontrol grubu koşularak** kapatıldı. Ders ledger'da **C9** olarak kayıtlı: *"ölçüldü" cümlesi rapora
girmeden önce kontrol grubu koşulmalı.*

**İşleyen diğer yöntem kalemleri:** çok-lens paralel review, her lens kendi dosyasına yazıyor · **ağaç
sahipliğinin açıkça bölüştürülmesi** (mutasyon koşan lens ağacın sahibi, diğerleri `git show` ile commit'ten
okuyor — bölüşümden önce eşzamanlı mutasyon bir lens'e iki **sahte kırmızı** göstermişti) · ajana "rapor
dosyasını işe başlar başlamaz iskeletle oluştur" demek · saf metin düzeltmelerinde ajan turu açmayıp
kontrolcünün kendi uygulaması.

---

## 4. Merge ve doğrulama

- `--no-ff` merge (repo konvansiyonu; önceki iterasyonlar da merge commit'iyle girmiş). Fast-forward mümkündü,
  konvansiyona uyuldu.
- `git diff --stat main a13-visual-debt-automation` → **boş** (içerik birebir).
- **Merge sonrası `main` üzerinde** doğrulandı: build 0/0 · süit 1650/2/0.
- Push: `8e6ebbe..d99cc38`. Doğrulandı: `local main == origin/main == d99cc38`, ahead/behind yok.
- Branch **local'den silindi**; remote'ta **hiç yoktu** (`git ls-remote --heads origin` → 0 eşleşme).
- Ağaçta yalnız kullanıcının A13 dışı takipsiz planı duruyor — **kullanıcı kararıyla** öyle bırakıldı.
  *(Not: bir `git add .claude/outputs` onu yanlışlıkla commit'e almıştı; `git rm --cached` + amend ile geri
  alındı, dosya diskte 33075 bayt olarak doğrulandı.)*

---

## 5. A14'e devredilenler (öncelik sırasıyla)

1. **Beş test kullanıcının GERÇEK `%LOCALAPPDATA%\BuildOrchestrator` köklerini kullanıyor** — A13 öncesinden var,
   o yüzden merge engeli sayılmadı. Ölçüldü: `evaluation-cache.json` **3,2 MB / 1021 girdinin 844'ü (%83) ölü
   test fixture'ı**; `logs/` altında **460 `run-*` dizini**, koşum başına +1. Çözüm deseni (`IsolatedPsi()`)
   zaten repoda; ayrıca `EngineHost.StartAsync()` için `--logs` geçirecek **hiç dikiş yok** (11 test daha).
   Registry · tepsi · mutex · hotkey · `ui-state.json` temiz.
2. **Ribbon `· N warnings`** — özellik boşluğu (IPC sözleşmesinde derleyici-uyarı alanı yok).
3. **`docs/TRUST-BOUNDARY.md` tam tazeleme** (53 doğrulanmamış atıf).
4. **a11y ikinci turu** (SplitButton sol yarımı · peer'siz 5 tıklanabilir yüzey · graf düğümleri klavyeden
   erişilemiyor · `ISelectionItemProvider` yok).
5. **DPI taşınabilirliği** (`precision: 1` üç dosyada daha).
6. **Daktilo sahiplerinin reduced-motion sözleşmesi** tek yere indirilsin (3 sahip, 1'i uyuyor).
7. Kalan park kalemleri — tamamı triyaj belgesinde `Konum:`'larıyla.
