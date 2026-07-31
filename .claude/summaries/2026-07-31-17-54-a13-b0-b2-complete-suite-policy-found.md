# A13 — Oturum özeti · 2026-07-31 (bilgisayar yeniden açıldı → 17:54)

**Branch:** `a13-visual-debt-automation` · **`main`'e merge EDİLMEDİ** · BASE `8e6ebbe`
**Süit:** oturum başı `1588 passed / 2 skipped / 2 failed` → şimdi **`1634 passed / 2 skipped / 0 failed`**
· guard'lar `69/69` · build `0/0` · `2697d6a` sonrası **23 commit**

---

## 1. Bu oturumda tamamlananlar

| Task | Sonuç | Test |
|---|---|---|
| **B0** DPI-kırılgan assert | complete — 1 fix round, review clean | — |
| **B1** E1 flake üçlüsü | complete — 2 fix round, review clean | +4 |
| **T5** a11y adları + kapsam testi | complete — 1 fix round, review clean | +7 |
| **T6** kalan altı kalem | complete — **üç lens de APPROVE, fix round gerekmedi** | +6 |
| **B2** Türkçe süpürmesi + guard | complete — 2 fix round, review clean | +21 |

Her task: kontrolcünün yazdığı brief → taze implementer → review package → **3-lens paralel review** →
fix dalgaları → scoped re-review → ledger. Her yeni test için **ayırt edicilik** kanıtlandı.

### Devralınan yarım işin değerlendirilmesi (ilk iş)
Oturum, ağaçtaki **commit edilmemiş ve doğrulanmamış 5 dosya** ile açıldı. Ledger'daki 5 adımlı yöntemle
sıfırdan doğrulandı: build 0/0 · guard 69/69 · **art arda 3 tam süit koşumu, üçünde de aynı sonuç**.
Taban 1588 passed / 2 failed → 1589 passed olunca B1'in üç flake'inin de yeşile döndüğü kanıtlandı; diff
F1/F2/F3'e karşı okundu, üçü de doğru eksende bulundu → `c35ea06` olarak commit'lendi.

### B0 — araya giren deterministik kırmızı (DPI)
`MainWindowRealizeTests`'in title bar merkez assert'i **üç koşumda ve izole koşumda da** kırmızıydı.
5 dosya `git stash`'lenip yeniden derlendiğinde de aynı → **regresyon değil**. Kök neden: **makinenin ekran
ölçeği 150% → 125%'e düşmüş** (hairline 1,333 → 0,8 dip); testin `precision: 1` toleransı ±0,05 dip, yani
bir cihaz pikselinin 1/16'sı — hiçbir DPI'da güvenilir tutmaz, 150%'de yeşil kalması şanstı.
Implementer ölçtü: **(A) test kırılgandı, üretim doğru** — `MainWindow.xaml`'de tek satır değişmedi.
Sapma artıksız yuvarlamayla açıklandı (iki hizalama adımı, ikisi de tam yarım cihaz pikselinde yukarı
yuvarlanıyor). Tolerans DPI'dan türetildi; `drift == budget == 1,0`, yani **tam sınırda, gevşetilmemiş**.

### B1 — "yük altında koşum" şartı bir hatayı gerçekten yakaladı
Yük altındaki koşum, **F1'in eksik uygulandığını** ortaya çıkardı: gerçek Supervisor başlatan **11 test
site'ından yalnız 5'i** yamalıydı. Hepsi yamalandı ve `TestPaths.WideStartupTimeout`'ta toplandı.
Ardından review, F2'nin sweep'inin de eksik olduğunu buldu; iki tur sonunda sweep **95 site / 14 dosya**
olarak tükendi (4 yamalı + 91 gerekçeli), ve re-reviewer bunu **iki bağımsız sayım yöntemiyle** doğruladı.
**Üretim varsayılanı 5 sn'de kaldı** (donmuş supervisor'da asılı kalma riski korundu).
G3 kararı (Supervisor **çıkışını** bekleyen 8 site yamalanmadı) mekanizma ayrımıyla gerekçelendirildi:
kırılan şey **cold start**tı (CreateProcess + CLR init + JIT); G3 ise **ısınmış** bir process'in çıkışını
bekliyor. Re-reviewer bunu `SupervisorHost.cs` + `Program.cs` okuyarak doğruladı.

### T5 — n1'in kökü ad eksikliği değildi
Düz `StackPanel`/`Border`'ın **automation peer'ı yok**, dolayısıyla graf düğümüne `AutomationProperties.Name`
yazmak ekran okuyucuya **hiç ulaşmazdı**. Düğüm gövdesi peer taşıyan bir tipe çevrildi; **görünüm
değişmedi** (lens1 bunu sayısal regresyon testleriyle çapraz doğruladı: `Focusable`/hit-test/`Padding`/
`Background`/`ControlTemplate` hiçbiri değişmemiş). Fix round'da rol/pattern uyumsuzluğu kapatıldı
(`IInvokeProvider`, farenin gittiği **aynı** `Toggle()`'a bağlı).
**Yan bulgu:** kapsam testinin `ProjectRow`'u sessizce dışlaması kaldırılınca ortaya çıktı ki veri-sonrası
bir layout turu olmadan `ItemsControl` kapları **hiç üretilmiyor** (0 → 2 kart) — kapsam testi
`ItemsControl` barındıran yüzeyler için **kısmen vakumdu**. Vakum guard'ı eklendi.
`ProjectRow`'un peer'ı olmadığı şüphesi **ölçümle çürütüldü** (peer var, ad ulaşıyor).

### T6 — beş kalem kapandı, biri dürüstçe açık bırakıldı
`t5` (şeridin canlı `· N warnings` beslemesi) **kapanamadı**: `StickyRibbon.xaml.cs` sabit `warnings: 0`
yazıyor ve **IPC sözleşmesinde derleyici-warning alanı hiç yok**. Bu bir test eksiği değil, **özellik**
eksiği; bugünkü dürüst davranış pinlendi, kablo bağlanınca test kırılacak. → **kullanıcı kararı**.
`verify-publish.ps1`'de **gerçek bir hata** bulundu ve düzeltildi: PS 5.1'de child'ın stdin'ine **BOM**
yazılıyordu → gönderilen **ilk komut** `badCommand` ile reddediliyordu; mevcut adım 3'ün `shutdown`'ı da
bu yüzden sessizce yutuluyormuş.

### B2 — Türkçe süpürmesi ve tokenizer guard'ı
62 metin / 20 dosya çevrildi; `ErrorEvent.Code` değerlerine dokunulmadı. **Sweep dört eksende yapıldı ve
her eksen bir öncekinin kaçırdığını buldu** (karakter → 47, ASCII kelime → 3 yeni, gözle tarama → 1 daha,
kelime dökümü → kapanış).
**Ham metin taraması kullanılamadı**: bu projenin yorumları tasarım gereği Türkçe (174 dosyada 6346 ham
isabet). Bu yüzden `SourceGuard` bir **tokenizer**'la genişletildi (C# / XML / PowerShell).
Review iki **Critical** buldu — en önemlisi `$"...{x ?? "y"}..."` biçiminde iç literalin hiç çıkarılmaması;
canlı kanıt `GitService.cs:207`'de gösterildi (oraya Türkçe koyulsa guard'ın iki testi de yeşil kalıyordu).
Kök neden: tokenizer'ın **kendi sınır testleri yoktu**. Testler önce yazıldı, 8'i kırmızı düştü, sonra
düzeltildi. İkinci turda fix'in kendi açtığı iki yeni Important daha kapatıldı (biri **canlı**: MSBuild
`Condition="'$(X)' == ''"` deyiminde içerik kesiliyordu). Üçüncü turda **yeni kör nokta çıkmadı**.

---

## 2. Kayda değer kontrolcü bulgusu — SÜİT POLİTİKASI UYUMSUZLUĞU

`README.md:83` kabul testlerinin **`--filter "Category!=Acceptance"`** ile hariç tutulduğunu yazar;
`CLAUDE.md:45`'teki komut ise **filtresiz**. Sonuç: bu çalışmadaki her tam süit koşumu kullanıcının
**gerçek OSYS reposunu** (`D:\Projects\Delta\OSYS`) gerçek `MSBuild.exe` ile derliyordu.

- Ölçüldü: **filtresiz ~3 dk 30 sn · filtreli 1 dk 33 sn**.
- `Category=Acceptance` üç test: `Osys_incremental_build_skips_all_up_to_date…` ·
  `Osys_full_rebuild_parallel…` · `Dispatch_order_is_deterministic…`
- `Osys_incremental…` **izole koşumda geçiyor** (1 dk 18 sn) ama tam süit içinde ara sıra kırmızı →
  zamanlama flake'i değil, **durum/çekişme bağımlı**.
- **Karar (repo'nun kendi yazılı politikasına dayanarak):** doğrulama süiti = filtreli komut.
  **CLAUDE.md ↔ README senkronu final doküman işine eklendi.**

B1'in kabul ölçütü bu politikayla karşılandı: **üç ardışık filtreli koşum, 0/0/0 failed**, üçüncüsü
8 CPU spinner altında (1 dk 34 sn → 2 dk 58 sn, +%93 wall).

---

## 3. Kapanan gerçek üretim kusurları (bu oturumda +1, toplam 10)

Önceki oturumda 9 (T2'de 6, T3'te 3). Bu oturumda: **`verify-publish.ps1` stdin BOM hatası** — script
bugüne kadar iddia ettiği doğrulamayı tam yapmıyordu.
Ayrıca T5'in graf düğümü değişikliği bir **erişilebilirlik** kusurunu kapattı (grafın tamamı ekran
okuyucuya görünmezdi) ve B2 kullanıcıya sızan **62 Türkçe metni** kaldırdı.

---

## 4. Kontrolcü adjudikasyonları (ledger'da gerekçeleriyle)

- **B1/C6 ratifiye:** F1'i 11 site'a tamamlamak "başka bir kusuru düzeltmek" değil, F1'in kendisini
  tamamlamaktı; üç yeşil koşum şartı onsuz sağlanamazdı.
- **T6/C3 ratifiye:** `verify-publish.ps1` BOM düzeltmesi, t6'nın **kendi teslimatını blokluyordu** —
  kalemin önkoşulu, kapsam genişletmesi değil.
- **B1 kabul ölçütü:** repo'nun yazılı politikasıyla yeniden koşuldu (yukarıda).

---

## 5. Metodolojik ders — sweep'in eksik kalması

Bu branch'te **aynı hata dört kez** tekrarladı (B1'de üç tur, B2'de bir tur): tarama **tek eksende**
yapılıp erken durduruldu. Kök neden her seferinde aynıydı: **API ekseninde aranıyor, davranış ekseninde
değil.** Alınan önlem: brief'ler artık "en az iki eksende ara ve **hangi eksenlerde aradığını raporda
yaz**" diyor; B2'nin raporu bu listeyi taşıyor.

İkinci tekrar eden kusur: **yorum/belge ile kodun çelişmesi** (bu branch'te üç kez yakalandı).

---

## 6. Kesinti anı — **ağaç TEMİZ, yarım iş YOK**

Kullanıcı bilgisayarı kapatacağı için duruldu. **B3 dispatch edildi ama HİÇ ÇALIŞMADI** — sessizce düştü:

- Ajanın çıktı dosyası **0 bayt**, son değişiklik `16:49:35` (oluşturulduğu an); durum 18:06'da kontrol edildi
  → **77 dakika boyunca tek bayt yazılmamış**.
- `task-B3-report.md` **yok** · `src/` ve `tests/` altında **değişen dosya yok** · `16:36`'dan (B2'nin son
  commit'i) sonra **yeni commit yok**.

Yani T2'nin kesintisindeki gibi **doğrulanmamış yarım dosya bırakılmadı**; çalışma ağacı `1be66b8`'de temiz
(yalnız iki untracked doküman: bu özet ve A13 dışı `scrollbar-restyle-plan.md`). **B3 sıfırdan başlatılabilir.**

**B3 kapsamı (brief hazır — `task-B3-brief.md`):** **E5** (`CollectRows()` satırı sessizce düşürüyor —
doc comment'in *"bir sonraki reveal onu yakalar"* gerekçesi **yanlış**, çünkü `PlayRevealStagger` yalnız
`SetGroups`'tan sürülüyor ve `SetGroups` yalnız topoloji değişiminde koşuyor) · **E4** (Sync "no changes"
kararının kaydı yok) · **E6** (`SystemParametersMotionSignal` sıfır testli; **makine-global ayarı çeviren
test YAZILMAZ** — kullanıcı kararı) · **k1/k2/k3** üç küçük borç.

**B4'ün brief'i de yazıldı, dispatch EDİLMEDİ** (`task-B4-brief.md`). Final aşama hiç başlamadı.
