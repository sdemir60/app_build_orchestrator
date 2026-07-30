# A13 — Gözle-kontrol borcunun otomatikleştirilmesi + park triyajı (oturum özeti, YARIM)

**Tarih:** 2026-07-30 · **Branch:** `a13-visual-debt-automation` (BASE `8e6ebbe` = `main` = `origin/main`)
**Durum:** YARIM — 2/11 task tamam, T2 fix dalgası uçuşta. `main`'e merge EDİLMEDİ.

---

## 1. Ne yapılması planlandı

İki iş vardı:

**(A)** `visual-check-walkthrough.md`'nin **tüm** kalemlerini (BÖLÜM 1+2+3) sınıflandır: pinlenebilir → test yaz,
göz-ister → kısa artık listeye. **(B)** `it5-records.md` §2'deki 18 park edilmiş kalemi + A11/A12'de ölçülen
6 yeni kalemi (E1–E6) üç kovaya ayır: gerçek kusur (kapat) / kabul edilen borç / artık geçersiz.

Plan: [.claude/outputs/2026-07-30-14-59-visual-check-automation-tdd-plan.md](../outputs/2026-07-30-14-59-visual-check-automation-tdd-plan.md)

---

## 2. Envanter (bitti) — ölçülen zemin

Walkthrough'un tamamı 6 paralel ajanla kalem kalem tarandı; her kalem assert edilebilir alt-iddialara bölündü
ve mevcut süitte **testin gövdesi okunarak** pin durumu ölçüldü.

| Bölüm | Alt-kalem | PINLI | Pinlenebilir-ama-testsiz | GÖZ İSTER |
|---|---|---|---|---|
| §0–§3 | 53 | 32 | 20 | 1 |
| §4–§6 | 26 | 22 | 4 | 0 |
| §7–§12 | 42 | 35 | 5 | 2 |
| BÖLÜM 2 | 78 | 51 | 25 | 2 |
| BÖLÜM 3 | 29 | 22 | 2 | 5 |
| **TOPLAM** | **228** | **162** | **56** | **10** |

Ayrıca **~12 PINLI kalemde "tetikleyici sınanmıyor"** bayrağı — A12'nin kusurunun saklandığı sınıf.

**Triyaj envanteri de bitti:** 18 park kaleminin dördünün dayanağı **geçersiz** çıktı (#2 P3 asimetrisi kodda
kapanmış, #17 CLAUDE.md A11'de kapanmış, #8b yalın-ok kapanmış, #4b tavan 40 değil 41). E1–E6'nın altısı da
`dosya:satır` düzeyinde ölçüldü.

---

## 3. Kullanıcı kararları (bu oturumda alındı)

1. **E6** (`SystemParametersMotionSignal` sıfır testli) → **yalnız risksiz kısım** test edilecek; makine-global
   erişilebilirlik ayarını çeviren test YAZILMAYACAK.
2. **Üç üretim boşluğu** (title bar bağlamı · `Failed ✕` chip'i · no-match metni) → **üçü de kapatılacak**.
3. **E3 Türkçe süpürmesi** → **kullanıcıya ulaşan her şey** (UI + git/worktree + planlama + run/decision log +
   exception mesajları, ~40 string). Kapsam dışı yalnız `Debug.WriteLine` ve GUI'de yutulan stderr.
4. **Liste filtresi** (oturum ortasında çıkan 5. boşluk) → **hem statü chip'leri hem metin araması çalışacak**;
   chip filtresi "olmazsa olmaz".

---

## 4. Bulunan GERÇEK ÜRETİM KUSURLARI (envanterin/walkthrough'un göremediği)

Bu, A13'ün asıl değeri: suite 1433 test ile yeşilken bulunanlar.

1. **Proje listesi HİÇ filtrelenmiyordu.** `VisibleProjects` (`RunViewModel.cs:411`) üretimde **sıfır
   tüketicili**; liste `BuildLayerGroups()` → `LayerGrouping.Build(**Projects**, Topology)` ile besleniyordu ve
   `RefreshProjectGroups` yalnız ctor + `TopologyChanged`'den çağrılıyordu. Sayaç chip'leri ve Ctrl+F kutusu
   listede **görsel olarak hiçbir şey yapmıyordu.** Walkthrough'un dört kalemini birden çürütüyor
   (§7.1 · §10.3 · §2.4-1 · §2.4-8) ve "No projects match this filter." metninin neden hiç var olmadığını
   açıklıyor. — **kapatıldı (T2.5)**
2. **Title bar bağlam metni hiç yazılmıyordu.** `ContextText` sabit `"no repository"` literaliydi ve hiçbir C#
   dosyası ona yazmıyordu (`rg ContextText` → tek satır). **E2'nin kök nedeni buydu — `git fetch` DEĞİL.**
   — **kapatıldı (T2.1)**
3. **App `ListBranchesCommand`'ı hiç göndermiyordu** → `Branches` hep boş → branch chip'i boş ve
   `IsWorktreeForced` her zaman `false`. — **kapatıldı (T2.2)**
4. **`ConsoleView` OS reduced-motion'ı canlı dinlemiyordu** ve `Unloaded` teardown'ı yoktu; sonsuz blink saati
   sahibi olduğu için ayar koşu sırasında açılırsa imleç sonsuza dek dönüyordu. Kardeşi `EventStreamView`'de bu
   açıkça kapatılmıştı. — **kapatıldı (T1 fix round 1)**
5. **`ListWorktreesCommand` de hiç gönderilmiyor** → worktree havuzu boş, `AutoWorktreeName` çakışan ad
   üretebiliyor. — **T2 fix round 1 kapsamına alındı, sonucu alınmadı**

---

## 5. Tamamlanan task'lar

**T1 — Tetikleyici zincirleri (A12 sınıfı) · TAMAM** (`8e6ebbe..214e290`)
+26 test; 12/12 kalem kapandı; **15/15 mutasyon kırmızı**. 3-lens review → 1 Critical + 7 Important →
tek fix dalgası → scoped re-review **12/12 ADDRESSED**.
- Critical'ı: layout tıklama testleri kullanıcının **gerçek `%LOCALAPPDATA%\BuildOrchestrator\ui-state.json`**
  dosyasını yazıyordu. `MainWindow`'a `IUiStateStore? uiState` ctor seam'i eklendi; nüks artık bir
  assertion'la (mtime+sha256 parmak izi) engelleniyor.
- Kayda değer hakemlik: `DsSplitter` "üretim kusuru" iddiası **çürütüldü** — üretimde `Application.Resources`
  fallback'i `Brush.Border`'ı çözüyor; `null` yalnız headless host'ta. `LogicalChildren` override'ı doğru bir
  düzeltme olarak kaldı, anlatı düşürüldü.

**T2 — Üretim boşlukları · implementer TAMAM, fix dalgası UÇUŞTA** (`214e290..5699fb3`)
+47 test (süit **1509/2/0**); brief 4 kalemdi, 5'e çıktı. 3-lens review sonucu:
- **Critical C1:** branch seed `Branch`'i **kalıcı çiviliyor**. Harici `git checkout` sonrası uygulama kendini
  düzeltemiyor → bayat branch derleniyor, `IsWorktreeForced=true` ama `UseWorktree=false` olduğu için Supervisor
  worktree'yi **zorunlu açıyor** (`Program.cs:216`), chip "off" diyor, popover switch **disabled**.
  T2 öncesi bu durum erişilemezdi.
- 7 Important: filtre chip'i ilk gerçek tıklamadan sonra pasif-griye düşüyor (test sentetik
  `RaiseEvent(ClickEvent)` kullandığı için göremiyor) · topolojide **çift tam liste reset'i** · `SetGroups` her
  çağrıda `FollowScrollController`'ı yeniden kurup **550ms throttle'ı sıfırlıyor** · A12 guard'ının taban
  çizgisi assert'siz (`0 == 0` trivial yeşil riski) · `IconVisual.Make` inline kopyalanmış ve ✕ chip'in
  amber'ını izlemiyor · fixture kopyaları şimdiden ayrışmış · `ListWorktreesCommand` boşluğu.

---

## 6. Kalan iş (9 task)

`T3` kopya metinleri + ölçü/geometri/tipografi pinleri · `T4` motion sabitleri + negatif guard'lar ·
`T5` a11y `AutomationProperties.Name` + kapsam testi · `T6` kalanlar (autostart, arg dalları, kontrast 4.28,
karışık-katman LOD, warnings wire, publish Sync+Build) · `B1` E1 flaky üçlüsü · `B2` E3 Türkçe süpürmesi ·
`B3` E5 + E6 + küçük kapatmalar · `B4` `debugSpawnChildren` kapısı + TRUST-BOUNDARY senkronu ·
**Final:** whole-branch review + `visual-check-residue.md` + `parked-items-triage.md` + merge/push.

---

## 7. Kullanıcıya bildirilen yan etki

T1'in ilk turundaki testler kullanıcının gerçek `ui-state.json` dosyasını bir kez yazdı — **yalnız pencere
yerleşimi tercihi** değişti (`LayoutMode=Focus, 60/74/76`); repo kökü, perf modu, hotkey, tepsi bayrağı
korundu. Title bar'daki quad ikonuna bir tık varsayılana döndürür. Nüks engellendi.

---

## 8. Süit durumu

Baseline `8e6ebbe`: **1433 passed / 2 skipped / 0 failed** · T1 sonrası **1462** · T2 implementer sonrası
**1509**. Build 0/0, guard'lar 69/69 (renk/motion/D8/token). Süit maliyeti henüz sessiz makinede ölçülmedi
(yük altında 5 dk 59 sn).
