# A13 — B3 ve B4 tamamlandı, final review yarıda kaldı (2026-07-31 23:02)

Branch **`a13-visual-debt-automation`** @ **`0b72803`** · BASE `8e6ebbe` (= `main`) · **main'e merge EDİLMEDİ**
Süit **1647 passed / 2 skipped / 0 failed** · guard **69/69** · build **0/0** · ağaç **temiz**
Ölçü: **99 commit · 133 dosya · +9463/−533** (src +1387/−182 · tests +6793/−292 · docs +57/−26 · diğer +1226/−33)

**11 uygulama task'ının hepsi tamam:** T1 · T2 · T3 · T4 · B0 · B1 · T5 · T6 · B2 · **B3** · **B4**.

---

## 1. Bu oturumda ne yapıldı

Önceki oturum B3 dispatch edilip **hiç çalışmadan** kesilmişti (ajan 77 dk boyunca tek bayt yazmadı). Bu oturum
B3'ü sıfırdan başlattı, tamamladı, sonra B4'ü tamamladı ve final tüm-branch review'ını dispatch etti.

### B3 — E5 · E4 · E6 + k1/k2/k3 (commits `e2e50aa..1459a33`, 9 commit)

Süit **1634 → 1644**. Bir ajan fix round + bir kontrolcü düzeltmesi. 3-lens review → iki fix dalgası →
iki bağımsız re-review, ikisi de APPROVE.

**Turun asıl kalemi E5 idi ve ilk hâli yanlıştı.** Uygulayıcı "kırmızı ölçüldü" diye raporladı; **iki lens
birbirinden bağımsız iki yöntemle** bunun doğru olmadığını ölçtü:
- lens3 `StickyLayerList.xaml.cs`'i `e2e50aa`'ya döndürdü → yeni E5 testi **11/11 yeşil** kaldı.
- lens1 izole bir kopyada (`git archive HEAD`, kullanıcının ağacına yazmadan) aynısını yaptı → **4/4 yeşil**,
  üstelik probe `PlayRevealStagger`'ın üç çağrısında da `rowsCollected=4` gösterdi — yani **hiçbir satır
  düşmüyordu**.
- Ayrıca `ArmRevealCatchUp()` içine `throw` konulduğunda **tam süit 1643/1643 yeşil** kaldı: ikinci kademe
  telafi hiçbir test tarafından koşulmuyordu.

**Fix dalgasında (b) yolu seçildi.** Uygulayıcı kendi S9 okumasının hatalı olduğunu buldu
(`pendingRelease`/`animated` göstergeleri reduced-motion altında anlamsız). Sonuç:
- Yanlış "kırmızı ölçüldü" iddiası **koddan ve rapordan kaldırıldı**; gerekçe "kusur düzeltmesi" değil
  **sertleştirme** olarak yeniden yazıldı.
- İkinci kademe telafi **tümüyle söküldü** (`ArmRevealCatchUp`/`DisarmRevealCatchUp`/`_revealCatchUp`/
  `RowEntryCount`/`LayoutUpdated` → src+tests'te 0 eşleşme) — ulaşılamaz savunma koduydu ve lens2'nin
  şikâyet ettiği sessiz-düşürme yolunu da o üretiyordu.
- **Gerçek bir kırmızıyla** pinlendi: `A_reveal_driven_while_layout_is_dirty_still_reaches_every_row`,
  dosya `e2e50aa`'ya döndürülünce kırmızı (4/4 satır `Opacity=1`), **üretim bozulmadan**, 12 testin yalnız o biri.

**"Ulaşılamaz" ile "geri alınca kırmızı" gerilimi re-review'da çözüldü — çerçeve DOĞRU.** Üç bağımsız kanıt:
(a) `PlayRevealStagger`'ın tek üretim çağıranı `StickyLayerList.xaml.cs:257`'deki `Loaded` ertelemesi (tam ağaç
grep'i); (b) probe ölçümü `Loaded=6 < Render=7` — otomatik layout turu **önce** koşuyor; (c) davranışsal:
B3 öncesi üretim dosyasıyla bile üretim yolundan sürülen uçtan uca test yeşil kalıyor.

**lens2'nin ölçümle elediği dört Kritik adayı** (izole net10.0-windows WPF probe, gerçek HWND): iç içe
`UpdateLayout()` `LayoutUpdated`'ı yeniden ateşlemiyor (recursion yok) ama measure'ı koşturuyor (telafi
körelmiyor) · boştaki uygulamada kanca 1,5 s'de 0 kez koşuyor (busy-loop yok) · handler içinden `-=` güvenli
ve gerçekten tek-atım · koleksiyon reset / token fırçası mutasyonu / template `Storyboard` yok. Perf de
ölçüldü: n=500'de **0,225 ms** → uygulayıcının C3 concern'ü kapatıldı.

**E4 (karar kaydı):** kayıt "otorite de aynı yöndedir (`BuildApp.jsx:1378`)" diyordu; ölçüm aksini gösterdi —
`doSync()` (`:1186-1193`, artış `:1190`) `revealKey`'i **her Sync'te koşulsuz** artırıyor, `:1378` ise
`pickFolder()`. Karar **değiştirilmedi**, kayıt gerçeğe uyduruldu, ayrışma `## Concerns C8`'e yazıldı.

**E6:** `SystemParametersMotionSignal` için filtre seam'e çıkarıldı, getter + KABLO pinlendi. Kullanıcı kararı
gereği makine-global erişilebilirlik ayarını çeviren test **yazılmadı**; artık liste C1'de.

**k1/k2/k3 kapandı.** k2'de brief'in tarifi ölçümle ayrıştı: `GraphCullTests`'te `TryFindResource` **çağrısı
yok**, tüm assert'ler `FindResource` ile ve bulamazsa fırlatıyor → `Assert.Equal(null,null)` vakumu **hiç
mevcut değildi**; kazanç yalnız hata mesajı.

**Kontrolcü düzeltmesi (`1459a33`)** — kalan üç kalem saf metindi ve gerçekleri iki lens doğrulamıştı,
ayrı bir ajan turu açılmadı:
1. `StickyLayerList` doc'undaki `revealKey` atfı ölçülen gerçeğe çevrildi (iki doc **zıt** anlatıyordu).
2. `RunViewModel._lastTopologySignature` doc'u repo'nun **dar okumasını otoritenin metniymiş gibi**
   aktarıyordu; otoritenin metni niteliksiz ("koleksiyon reset'i YASAK", plan v7 A13.2 Motion) — dar okuma
   artık **repoya ait** diye etiketli. Karar değişmedi. Kopyası `ProjectListFilterTests`'te de düzeltildi.
3. `StickyRevealTriggerTests` assert mesajı hâlâ "CollectRows onu sessizce düşürdü" diyordu; sebep iddia
   etmeyen bir mesajla değiştirildi.

### B4 — `debugSpawnChildren` bayrak arkasına (commits `1459a33..0b72803`, 8 commit)

Süit **1644 → 1647**. 2-lens review (diff küçük olduğu için üç değil iki) → bir fix dalgası → iki re-review,
ikisi de APPROVE.

Kapı **Supervisor tarafında**, varsayılan kapalı, `--debug-hooks` ile açılıyor, `badPerfMode` desenini birebir
izliyor, IPC sözleşmesi bozulmadı (`DebugSpawnChildrenCommand` tipi ve JSON ayrımcısı **korundu** — komut
tanınıyor, yalnız reddediliyor).

**Kontrolcü kararı doğrulandı:** App'e `--debug-hooks` **eklenmedi**; ölçümle gerekçelendirildi — komutu
kullanan testlerin hiçbiri Supervisor'ı App üzerinden spawn etmiyor.

**Sweep nihayet doğru yapıldı** (üç eksen: tip adı · JSON ayrımcısı · yanıt event'i) ve **brief'i iki yerden
düzeltti**: `KillMidBuildTests` bu komutu **kullanmıyormuş** (gerçek `startRun`), `CascadeKillTests` **iki**
test barındırıyormuş, ve brief'te olmayan `IpcMessagesTests` bulundu (yalnız round-trip). lens A bağımsız
doğruladı: liste **tam**, kaçan yok.

**lensB'nin tek Önemli bulgusu turun en değerlisiydi:** kapı spawn döngüsünden **sonraya** taşındığında —
yani üretim ikilisi `--debug-hooks` olmadan gerçekten `cmd.exe`+`powershell` doğururken — **1646 testin
tamamı yeşil kaldı**. İlan edilen kazanç hiçbir testle pinli değildi; negatif test yalnız `Code` + NDJSON +
host-ayakta assert ediyordu. Fix: yeni test
`Rejected_debugSpawnChildren_spawns_no_cmd_or_powershell_child` (`SupervisorIpcTests.cs:209-256`),
`CascadeKillTests`'in IOCP `JOB_OBJECT_MSG_NEW_PROCESS` desenini **yeniden kullanarak** (kopyalamadan).
Re-review'da lens B kendi mutasyonunu tekrar koştu: yeni test kırmızı **ve** eski negatif test aynı mutasyonda
yeşil — kapatılan boşluk tam olarak bu farkta; mutant altında 5/5 kırmızı.

**İki tasarım kararı ölçümle bulundu** (ikisi de ilk denemede kırmızı düştü ve re-review'da doğrulandı):
randevu noktası Supervisor'ın **çıkışı** değil ikinci bir Supervisor'ın **doğumu** olmalı (çıkış randevusunda
`NameOfProcess` `"(exited)"` döndürüp iddiayı **yanlışlıkla yeşile** düşürürdü) · iddia ham doğum sayısı değil
**isim süzgeci** olmalı (`JobProcessLauncher.cs:40` fiilen `CREATE_NO_WINDOW` kullanıyor, her koşumda 3 conhost).

**B4'ün belge senkronu kendi kuyruğunu yedi:** kendi +7 satırı `TRUST-BOUNDARY.md:124-125`'teki **dört tam
isabetli** `Program.cs` refini bozdu (bu turun regresyonu, "eskiden bayat" değil). Fix'te dördü de yeniden
**çıpalandı** (mekanik kaydırma değil) ve lens A'nın ilk turda kaçırdığı **dört ref daha** bulundu.
"Belgedeki tüm atıflar doğrulandı" iddiası sınandı: **30 atıfın 30'u doğru, kaçan yok.** §1/§3/§9 artık aynı
şeyi söylüyor, provenance tablosu dürüst, belge churn'ü yok. Kontrolcü kararı: **tam tazeleme yapılmadı**
(ayrı task).

---

## 2. Yöntem: bu oturumda neyin işe yaradığı

- **3-lens (B3) / 2-lens (B4) paralel review**, her lens bulgularını **kendi dosyasına** yazdı. Lens sayısı
  diff'in boyutuna göre ölçeklendi.
- **Ağaç sahipliği açıkça bölüştürüldü:** mutasyon koşan lens ağacın sahibi, diğerleri salt okunur ve
  okumalarını `git show <sha>:<path>` ile commit'ten yapıyor. Önceki turda eşzamanlı mutasyon bir lens'e
  **iki sahte kırmızı** göstermişti; bölüşümden sonra tekrarlamadı.
- **Ayırt ediciliği reviewer'ın kendisi üretti** — beyan kabul edilmedi. İki turda da ilan edilen ölçümün
  yanlış olduğu **bu sayede** ortaya çıktı.
- **Ajana "rapor dosyasını işe başlar başlamaz iskeletle oluştur"** demek, önceki oturumdaki "ajan sessizce
  düştü" sorununu görünür kıldı.
- Saf metin düzeltmeleri için **ajan turu açılmadı**; kontrolcü kendi uyguladı, süiti kendi koştu.

---

## 3. Nerede kalındı — final review yarıda

Tüm-branch diff'i **948 KB** olduğu için tek ajana sığmadı; **üç pakete bölündü** ve üç lens dispatch edildi.
**Üçü de kullanıcı kesintisiyle durdu, çıktı dosyalarını yazamadan.** Girdi paketleri hazır ve geçerli:

| Paket | Boyut | Lens |
|---|---|---|
| `final-review-src.md` | 4763 satır / 316 KB | A — üretim kodu |
| `final-review-tests.md` | 9362 satır / 545 KB | B — testler |
| `final-review-docs.md` | 1829 satır / 134 KB | C — belgeler |

Değerlendirilecek artık **yok**; üçü de sıfırdan yeniden dispatch edilebilir. Lens görevlerinin ayrıntısı
ledger'ın son bloğunda yazılı.

### Final aşamasında kalan işler
1. Üç lens'i yeniden dispatch + triyaj; merge engeli varsa tek fix dalgası + tek scoped re-review.
2. `visual-check-residue.md` — yalnız göz-ister kalemler, uygulama gezinti sırasıyla.
3. `parked-items-triage.md` — üç kovalı tablo + kapatılan kalemlerin commit'leri.
4. "X yeni test, gezintinin 228 alt kaleminin Y'sini pinliyor" raporu.
5. Doküman senkronu (lens C'nin reçeteleri) — **kontrolcü uygular**.
6. main'e merge + push + doğrula + branch'i local ve remote'tan sil + oturumu main'de bırak.

### Kullanıcı kararı bekleyen iki kalem
- **Süit komutu politikası:** README'nin filtreli formu benimsendi (filtresiz koşum kullanıcının **gerçek OSYS
  reposunu** derliyor). `CLAUDE.md:45` ona göre düzeltilecek — itiraz varsa ters yöne dönülür.
- **Ribbon'daki canlı `· N warnings`:** test boşluğu **değil**, **özellik** boşluğu (IPC sözleşmesinde
  derleyici-uyarı alanı yok). A13 kapsamı dışı; **A14'e taşınması öneriliyor**.

### Park edilen bulgular (final triyaja)
Ledger'da task task yazılı, ayrıca: `review-B3-lens{1,2,3}.md` · `review-B3-fix1-lens{1,3}.md` ·
`review-B4-lens{A,B}.md` · `review-B4-fix1-lens{A,B}.md` · `task-B3-report.md` (C1-C9) ·
`task-B4-report.md` (concerns 1-5).

**En dikkat çekeni (A13 öncesinden var, merge engeli mi diye değerlendirilecek):** bazı testler kullanıcının
**gerçek `%LOCALAPPDATA%\BuildOrchestrator`** logs/worktree köklerini kullanıyor —
`SupervisorIpcTests.cs:67/:93/:117` + `CascadeKillTests.cs:23/:65` argümansız `Psi()` /
`DebugHooksCommandLine()` çağırıyor ve `Program.cs:62-64/73` `--logs`/`--worktrees` verilmezse gerçek köklere
düşüp dizinleri koşulsuz yaratıyor.
