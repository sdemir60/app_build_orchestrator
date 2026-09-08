# Tasarım v1.8.0 → v1.11.0 uygulama raporu

**Branch:** `feat/design-v1.11` (13 commit) · **merge EDİLMEDİ** — birlikte bakılacak.
**Kaynak:** `.claude/outputs/2026-08-05-01-26-design-v1.11.0` (README + prototip).
**Süit:** 2152 geçti / 0 kaldı / 1 atlandı (filtreli) · Acceptance 3/3.

Bu belge, "kararsız kaldığın noktalarda en doğru yöntemi uygula, sonra rapor et" talimatının karşılığıdır:
**karar verdiğim yerler** ve **bilinçli sapmalar** aşağıdadır. Uygulanan özelliklerin tam listesi değildir —
onlar ARCHITECTURE.md ve README.md'ye işlendi.

---

## 1. Kapsam kararı: hangi sürümler uygulandı

Uygulamanın v1.7.0'da olduğu **varsayılmadı**, özellik işaretleriyle doğrulandı (title bar bağlam metni vardı,
What's new sekmesi yoktu, satır aksiyonları yoktu, filtre tek stringdi). Kapsam bu yüzden **v1.8.0 → v1.11.0**,
dört sürüm oldu.

Uygulanmayan tek madde, prototipin **simülasyon** parçasıdır:

| Madde | Neden uygulanmadı |
|---|---|
| v1.11 §12 `_reseedChanges()` | Prototipte "ardışık Build hep boş kalıyor" sorununu çözmek için sahte HEAD üretip 2-4 proje bayatlatan simülasyon kodu. Gerçek uygulamada bayatlık `git` ve dosya damgalarından gelir — üretecek bir şey yok. |

---

## 2. Boş bırakılan işlevler (talimat gereği)

"Yeni bir işlev eklenmişse butonu/tasarımı ekle, işlev eklenmeyecek, boş duracak." Aşağıdakiler **tasarımdaki
yerinde, tasarımdaki görünümde** duruyor ama pasif; her birinin tooltip'i nedenini söylüyor
(`— not available yet`), tıklanınca sessizce hiçbir şey yapmıyor değil:

| Kontrol | Yer | Durum |
|---|---|---|
| Satır **▶ play** ("Build this project") | Proje satırı hover ikon grubu, 14px | Disabled · `Build this project — not available yet` |
| Satır **⋯ menüsü** (Build · Rebuild · Clean) | Satır hover + **sağ tık** | Menü açılıyor, üç madde de disabled |
| Build menüsünde **Clean** | Build split-button menüsü, 3. madde | Disabled · `Clean — msbuild /t:Clean on every solution; caches are untouched — not available yet` |

Menü **açılıyor ve doğru görünüyor** (sağ tık dâhil) — kapalı bir menü tasarımı taşımaz. İkon ailesi tasarımın
istediği gibi tek grid/stroke'ta yeniden çizildi: **play · rotate-cw · brush**; Build menüsü ve satır menüsü
aynı seti kullanır (eski Rebuild ikonu değişti).

Bakım kutusundaki **Clean / Optimize** zaten pasifti — dokunulmadı. **Resolve cycles** çalışan bir akış, yalnız
ikonu nötrleşti (turuncu UI'dan çıktı).

### Bilinçli istisna: Settings Export / Import / Clear tam çalışıyor

v1.10.0'ın bu üç düğmesi **tam işlevsel** yazıldı — "işlev ekleme" kuralına karşı verdiğim tek karar. Gerekçe:
üçü de **yalnız formu** değiştirir, motora hiç dokunmaz (`Save`'e kadar hiçbir şey uygulanmaz — tasarımın kendi
kuralı). Boş bir Export düğmesi test edilebilir bir şey bırakmaz; JSON şeması (`app`, `version`,
`repositoryRoot`, `layers[{name,pattern}]`) tasarımın bir parçası ve ancak yazılıp okunarak doğrulanabilir.
Dosya seçiciler test edilebilir bir seam'in (`PickExportPath` / `PickImportPath` / `ReadFile` / `WriteFile`)
arkasında. **Yanlış bulursan geri almak kolay** — tek dosyada toplandı.

---

## 3. Bilinçli sapmalar (tasarımdan ayrıldığım yerler)

### 3.1 Koreografi koşuyu GECİKTİRMİYOR, planlama penceresiyle örtüşüyor ★

**En önemli karar bu.** Prototipte açılış koreografisi ~2.5-3.6 s sürer ve koşu **onun sonunda** başlar (motor
simüledir, beklemenin bedeli yok). Gerçek derlemeyi bir animasyon için 3 saniye geciktirmek savunulabilir
değil.

Yaptığım: komut **anında** gönderilir, koreografi motorun zaten var olan **planlama penceresinin** (`Starting`
fazı: worktree hazırlığı → tarama → graf → topoloji → incremental karar) **üstünde** oynar ve `runStarted`
geldiğinde biter. Görsel sıra birebir korunur, maliyeti sıfırdır. Kod: `OperationChoreographer` sınıf doc'u.

**Sonucu:** planlama koreografiden kısa sürerse dalga tam bitmeden koşu devralır. Kapsam amber kalır (statü
kanalı devralır), yani görüntü tutarlıdır — ama tasarımın "sarı-gri an → veda → nefes" tam dizisini her zaman
göremeyebilirsin. Bunu görmek istersen tersine çevirmek tek satır; söyle, değiştiririm.

### 3.2 Marking fazında Stop → `Cancelled — build not started` yazmıyor

Tasarım, marking fazında Stop'a basınca **koşunun hiç başlamadığını** ve konsola `Cancelled — build not
started` düştüğünü söyler. §3.1'deki karar yüzünden bu durum gerçek uygulamada **yok**: o anda koşu zaten
istenmiş ve motor planlıyor. Stop, gerçek bir koşuyu durduran mevcut yoldan geçer ve mevcut `Build stopped …`
satırını basar. Yeni bir "iptal" yolu **açmadım** — olmayan bir durumu anlatan bir konsol satırı yalan olurdu.

### 3.3 Satır aksiyonlarının kapsamı — motor tarafı yok

Tasarım §3.8 "yalnız o proje derlenir, bağımlılık bayat kalırsa uyarı üçgeni doğar ve loga `last known output
referenced` düşer" der. Bu bir **motor kuralıdır** (scheduler + incremental), UI değil. İşlev boş bırakıldığı
için bu kural da uygulanmadı. Düğmeler açıldığında Core tarafında ayrıca ele alınması gerekir.

### 3.4 Şeridin 1px dikey iç boşluğu

Tasarım §2.4-1 sol statü şeridinde 1px dikey iç boşluk ister. Uygulamada bu boşluk **yok** ve bu senin daha
önceki kararındı (bitişik satırlarda tek çizgiye kaynamama sorunu WPF'te satır ayracıyla zaten çözülü).
**Dokunmadım** — kesikli/düz/renk kuralları uygulandı, geometri korundu.

### 3.5 Settings hâlâ örnek katmanlarla açılıyor

Tasarım §2.9, katman listesinin varsayılan olarak **boş** gelmesini ister (`Optional`). Uygulama ilk açılışta
`LayerDefaults`'u doldurur — bu da önceki bir karardı. **Değiştirmedim**, yalnız düğmenin adı tasarımdaki gibi
`Restore default layers` → **`Load sample layers`** oldu (artık "geri yükleme" değil, "örnek doldurma" işi
yapıyor ve adı bunu söylüyor). Boş başlaması gerekiyorsa ayrı bir iş.

### 3.6 About gövdesi `MinHeight` → sabit `Height`

What's new sekmesi uzun bir liste taşıyor ve `MinHeight`'la dialog sekme değiştikçe **boyut atlıyordu**. Gövde
sabit 236px oldu, uzun pane kendi içinde kayıyor. Bunu pinleyen eski test silinmedi, **yeni kuralı pinleyecek
şekilde yeniden yazıldı** (`The_body_height_is_fixed_so_a_long_pane_scrolls_instead_of_growing_it`).

### 3.7 İkon butonlarında tooltip kaldı — ama OS gecikmesiyle

Tasarım §13, satır ikon butonlarının DS tooltip'i taşımamasını, yalnız native HTML `title` + `aria-label`
taşımasını ister. WPF'te `title`'ın karşılığı yok; en yakın şey **ToolTip'in OS-native gecikmesi**
(`AppTooltipDefaults.UseNativeDelay`). Uygulama tooltip'i kaldırılırsa erişilebilirlik ve keşfedilebilirlik
birlikte gider. Yaptığım: DS'in hızlı gecikmesi yerine native gecikme + `AutomationProperties.Name`. Statü
glyph'inin tooltip'i tasarımın istediği gibi **tamamen kaldırıldı**.

### 3.8 Uyarı tooltip'i iki uygulamaya özgü varyantı koruyor

Tasarım tek satırlık iki metin verir: `In a dependency cycle` / `Dependency issue: Sales.Core +2`. Uygulamada
döngünün iki ek durumu var (`CycleUnsettled`, `CycleUnconverged`) — prototipte karşılığı yok çünkü orada döngü
tek turda çözülür. Bu iki metin **tek satır** kuralına uydurularak korundu; silmek gerçek bilgiyi atmak olurdu.

---

## 4. Tasarımın kendi içinde çeliştiği yerler

Bu üç noktada tasarım belgesi kendisiyle ya da prototiple çelişiyor. Belgenin **"Fidelity: prototip
bağlayıcıdır"** kuralı gereği her seferinde **prototipi** izledim.

| # | Çelişki | Karar |
|---|---|---|
| 1 | README §3.1, boş fazda title bar'da `not configured` yazdığını söyler; §2.1 ve prototip (`BuildApp.jsx:2186-2192`) title bar'da **yalnız marka** olduğunu söyler | Prototip. Title bar marka taşır; `Not configured — repository root not set` **sticky şeride** yazıldı. |
| 2 | README §2.7-4 hâlâ **iki** istisnai çip anlatır (`⚠` turuncu cycle + `▲` kırmızı dep); aynı satırın başlığı ve §1.1/§2.4-6/§9-10 **tek amber ⚠** der | Prototip (`BuildApp.jsx:2373` — tek `warn` çipi, amber). Tek çip, tek filtre. |
| 3 | README §2.7-2 ve §3.7 Resolve tooltip'i "in two passes" der; motorun turu sabit değil (`CycleRoundPolicy`) | Mevcut uygulama metni korundu ("repeated rounds… until they converge") — sabit tur sayısı vaat edilmiyor. Bu zaten önceki bir karardı. |

---

## 5. Davranış değiştiren, dolayısıyla testi yeniden yazılan kurallar

CLAUDE.md gereği hiçbiri **silinmedi**; her biri YENİ kuralı pinleyecek şekilde yeniden yazıldı ve doc'una
`[DEĞİŞEN KURAL]` bloğuyla eski iddia + gerekçe yazıldı. Öne çıkanlar:

- **Event stream artık her işlemde temizleniyor** (§9-4 `_beginOp`). Eskiden anlatı koşular boyu kümülatifti ve
  `{n} events` sayacı hiç sıfırlanmazdı. **Sync bu kapıdan geçmez** — Sync bir işlem değil, işlemlerin
  zeminidir (prototipte de `startSync` `_beginOp` çağırmaz). Silme `Clear()` ile değil sondan `RemoveAt` ile
  yapılıyor: `Clear()` bir `Reset` bildirimidir ve koşan satır animasyonlarını yıkar.
- **Konsolda `warn` satırları amber** (§1.1). Eskiden cycle turuncusuydu. `--status-cycle*` token'ları dosyada
  duruyor ama **bir statü kanalı olarak hiçbir yerde okunmuyor** — tasarımın istediği bu.
- **Will-build kanalı ve turuncu cycle işareti kaldırıldı**; şerit + nokta + glyph + node border + küp tek
  `VisualStatus`'tan besleniyor.
- **Filtre bir küme oldu** (VEYA ile birleşir); `dep` ve `cycle` filtreleri tek `warn`'a indi.
- **Title bar bağlam metni kaldırıldı**; workspace adı alt bara, branch çipinin soluna taşındı.

Toplam 22 test dosyası bu şekilde yeniden yazıldı.

---

## 6. Yol üstünde bulup düzelttiklerim

- **`ARCHITECTURE.md` §22 kod haritası olmayan bir şeye işaret ediyordu** (`ApplyCycleBadge` / `CycleBadge`).
  Düzeltildi.
- **Bitiş koreografisi, kapsamı boş bir işlemde kesilmiyordu.** Kesme işaretleme dalgasına bağlıydı; dalga
  oynamayan bir işlemde (Sync'in hemen ardından "Everything up to date" ile biten Build) önceki koşunun neon'u
  yanmaya devam ediyordu. Kesme ayrı bir kapıya alındı (`GraphView.BeginOperation()` = `_beginOp`); önce kırmızı
  test, sonra fix.
- **Σ çipinin tooltip'i çoğullaştı** (`clear filters`) — filtre artık çoklu seçim.

---

## 7. Bilinmesi gerekenler

- **`GraphStatus.Cycle` artık üretimde hiç atanmıyor** (yalnız `StatusGlyph` eşlemesinde ve bir testte duruyor).
  Ölü ama zararsız; enum üyesini silmek App ↔ Supervisor sözleşmesine dokunacağı için **elleyemedim** —
  temizlik istersen ayrı bir iş.
- **`PopoverTests.Opening_a_popover_plays_a_real_140ms_pop_…` ara sıra kırmızı veriyor**, tek başına koşunca hep
  yeşil. Bu **benim değişikliklerimden önce de böyleydi** (baseline'da doğruladım) — zamanlamaya duyarlı bir
  test, ayrı ele alınmalı.
- **Branch `main`'e merge edilmedi** ve silinmedi. Oturumun `main` üzerinde bitmesi kuralı bu talimatın
  altında; branch yerinde duruyor.

---

## 8. Commit'ler

```
475db97  Build menusune Clean maddesi + yeni ikon ailesi
d24488e  title bar sadelesti, workspace adi alt bara tasindi
796638c  coklu filtre + seritte kalici islem pill'i
2f11f51  tek statu kanali, baslangic modu ve tek uyari ucgeni
5ee2aca  satir aksiyonlari — play, ellipsis menusu ve sag tik
510bb86  acilis (isaretleme dalgasi) ve bitis (neon) koreografileri
166c73d  first run Settings'e yonlendiriyor + export/import/clear
f510bc2  About'a What's new sekmesi + gorulmemis surum isareti
ae87bc2  serit/gear metinleri ve notr Resolve ikonu
f31731a  ARCHITECTURE ve README design v1.8-v1.11'e gore guncellendi
d699167  yeni islem event stream'i de temizliyor
71f769a  konsol warn satirlari amber, turuncu tamamen cikti
a393d4d  bitis koreografisi isaretleme dalgasindan bagimsiz kesilir
```
