# Gözle kontrol — A13 sonrası kalan artık liste

**Bu belge nedir:** 228 alt kalemlik gözle-kontrol gezintisinden geriye kalan, **otomatikleştirilemeyen**
kalemler. Testle pinlenmiş hiçbir kalem burada yok — uygulamayı açıp yalnız bu 20 satıra bakman yeterli.

## Künye (ölçüldü)

| Ne | Sayı | Nasıl ölçüldü |
|---|---|---|
| Gezintinin toplam alt kalemi | **228** | `2026-07-30-18-45-a13-inventory-appendix.md` tablosu (BÖLÜM 1+2+3) |
| A13 girişindeki durum | 162 pinli · 56 testsiz · 10 göz-ister | aynı tablo |
| **Artık testle pinli** | **217** | 162 + A13'ün 11 task'ında kapatılan 55 testsiz kalem (SDD ledger'ın task-başına `complete` satırları) |
| **Hâlâ göz isteyen** | **20** (bu belge) | aşağıdaki bölümler; her satırın kaynağı belgenin sonunda |
| Ne testle ne gözle kapanan | **1** | şeridin canlı `· N warnings` beslemesi — IPC sözleşmesinde derleyici-uyarı alanı yok, bu bir **özellik** boşluğu (A14'e önerildi) |
| **Yeni test** | **+201 test metodu** (1260 → 1461) | `tests/` altındaki `[Fact]/[StaFact]/[Theory]/[StaTheory]` sayımı, `8e6ebbe` ağacı vs HEAD (`dcf740f`) |
| Diff hacmi | 64 dosya · +6793 / −292 satır | `git diff --stat 8e6ebbe HEAD -- tests` |
| Koşan test sayısı | 1433 → **1647** | +201 metot, bunun 7'si theory (+23 `InlineData`) → +217 vaka; 1433 + 217 = 1650, eksi 3 `Category=Acceptance` (B1'de benimsenen README §83 filtresi) = **1647** — rakam birebir kapanıyor |

> **Neden 217 + 20 toplamı 228 vermiyor:** aşağıdaki 20 satırın **9'u**, envanterde "pinli" sayılan kalemlerin
> **OS/makine tarafına bakan yarısıdır** (komut, argüman ya da karar testte pinli; gerçekleşen OS davranışı
> değil). Bu 9 kalem iki kovaya birden düşüyor.

---

## 1) Pencere / açılış — 3 kalem

- **`--font-ab` kabuğu (§0.4):** `--font-ab` ile başlat → font A/B penceresi gerçekten açılmalı, Supervisor
  spawn edilmemeli. *(argüman kararı pinli; pencerenin `Show()` edilmesi makine-global)*
- **Title bar dikey ortalama (A13/B0'da çıktı):** Logoya ve başlığa bak → 40px bandın içinde optik olarak
  ortalı durmalı, hairline'a doğru kaymamalı. *(iç kutuda ortalı ile bantta ortalı hiçbir DPI'da ölçüyle
  ayırt edilemiyor — fark yuvarlama zemininin altında)*
- **Reduced-motion (§11.1, çapraz):** Windows → Erişilebilirlik → Görsel efektler → Animasyon efektleri'ni
  uygulama açıkken **KAPAT** → nefes/spinner/dash akışı/daktilo/kamera/pop-in anında durmalı; geri aç →
  hepsi dönmeli. *(canlı OS sinyali; makine-global ayarı çeviren test yazılmıyor)*

## 2) Sol panel — Projects listesi — 5 kalem

- **İlk hover gecikmesi (§3.6-1):** Uzun listede (191 satır) **daha önce hover etmediğin** bir satıra gel →
  klasör + VS ikonları anında çıkmalı, gözle görülür takılma hissedilmemeli. *(his; ölçülebilir eşiği yok)*
- **Hover taraması (§3.6-3):** Fareyi satırlar boyunca hızlıca sürükle → biriken bir yavaşlama olmamalı.
  *(zamansal his)*
- **Listenin ilk dolması (§3.7-1):** Repo seç → liste ilk kez dolarken rahatsız edici bir bekleme var mı?
  *(ölçüm 487,5 ms/191 satır; kabul kapısı değil, kararın gözle teyidi — "kabul edilemez" hissediyorsan bildir)*
- **Explorer (§9.1):** Satır hover → klasör ikonuna tıkla → Explorer **o dosya seçili** açılmalı.
  *(komut+argüman ve konsol notu pinli; Explorer penceresinin gerçekten açılması OS yüzeyi)*
- **Visual Studio (§9.2):** VS ikonuna tıkla → bağlı solution VS'te açılmalı; >1 sln varsa seçim popover'ı,
  seçince açılmalı. *(aynı gerekçe)*

## 3) Graf — 3 kalem

- **Düğüm biçimi kararı (§2.3-2):** Prototiple yan yana bak → prototipte daire, uygulamada 4px radius kare.
  **Kabul mü, sapma mı — karar ver.** *(ölçüm değil kullanıcı kabulü; 26px + 4px radius zaten pinli)*
- **Pan akıcılığı (§3.2-1):** Grafta kaydır / pan yap, scrollbar'la uzağa atla → hareket akıcı olmalı;
  boş kalan, geç gelen, yarım çizilmiş düğüm-kenar olmamalı. *(kare-hızı/jank ölçümü yok)*
- **Etiketi düşen düğümün tooltip'i (§3.2-5):** 150+ düğümlü grafta etiketi düşmüş bir düğümün üzerine gel →
  tooltip açılmalı ve **tam proje adını** vermeli. *(tooltip metni string olarak pinli; WPF ToolTip kontrolü
  ancak gösterimde kurulduğu için "hover'da gerçekten açılıyor mu" headless doğrulanamıyor)*

## 4) Konsol — 0 kalem

- Kalem kalmadı: daktilo, imleç, kaskat, mod geçişi, `⌄ latest` pill ve Copy log'un tamamı A13'te
  testle pinlendi.

## 5) Aksiyon çubuğu — 2 kalem

- **Perf mode'un gerçek etkisi (§3.1-5):** Koşu sırasında `perf` chip'ine basıp `Light`'a geç → Task
  Manager'da `MSBuild.exe` toplam CPU'su gözle görülür şekilde düşmeli, **ama uygulama (Sync/IPC) akıcı
  kalmalı**. *(Task Manager gözlemi; assert edilebilir eşik yok — cap'in job'a yazıldığı pinli)*
- **Tooltip'in üzerine gitmek (§7.11):** Bir butonun tooltip'i açıkken fareyi tooltip'in üzerine götür →
  tooltip **kaybolmamalı**. *(ayrı HWND/Popup hit-test davranışı; `ShowDuration` sonsuzluğu pinli ama yetmiyor)*

## 6) Popover — 1 kalem

- **Gölge yumuşaklığı (§2.8-1):** Branch/worktree popover'ını aç, prototiple yan yana bak → gölgenin
  konumu/opaklığı/rengi eşleşmeli; **yumuşaklığı** eşleşmeyebilir, kabul edilebilir mi karar ver.
  *(WPF `DropShadowEffect`'te spread parametresi yok — A13.1 madde 2)*

## 7) Ayarlar — 2 kalem

- **Klasör seçici (§8.5):** `Change…` → klasör seçici açılmalı; yeni kök seçince otomatik Sync başlamalı.
  *(OS penceresi uygulama temasına boyanamaz — A13.1 madde 6)*
- **Dialog zemin tonu (§2.9-1, açık karar):** Settings'i prototiple yan yana aç → uygulama `surface-raised`
  (#1a1a1e) kullanıyor, design-v1 README §2.9 `surface-overlay` (#202024) diyor. **Gözle fark ediliyor mu —
  düzeltilecek mi, kayıtlı sapma mı, karar ver.** *(A13'te doğrulanmadan kaldı)*

## 8) Tepsi / oturum — 4 kalem

- **Tray ikonu onayı (§0.3b):** Tepsi ikonuna bak → artık eski "D" letterform değil, quarter-disc mark.
  **Bu görsel kimlik değişimini onaylıyor musun?** *(bir marka onayı; türetim pinlenebilir, onay pinlenemez)*
- **Alt+B (§10.6):** Pencereyi tepsiye gizle → **Alt+B** ile geri gelmeli. *(gerçek `RegisterHotKey` kaydı +
  başka uygulamayla çakışma; parse/kayıt-ömrü pinli)*
- **İkinci instance (§12.3):** Uygulama açıkken ikinci kez başlat → mevcut pencere öne gelmeli; gelemezse
  tray balloon çıkmalı (sessiz değil). *(karar + çıkış kodu 3 pinli; gerçek pencere aktivasyonu makine-global)*
- **Autostart (§12.4):** Autostart'ı aç, oturumu kapat-aç → tepside temiz **Idle** görünmeli, otomatik Sync
  **olmamalı**. *(registry uzlaşması ve rota kararı pinli; tepsi ikonunun gerçekten kurulması makine-global)*

---

## Bu 20 satır nereden geldi

- **10'u** envanter ekinin "GÖZ İSTER" listesinden (§0.3b · §7.11 · §10.6 · §2.3-2 · §2.8-1 · §3.1-5 ·
  §3.2-1 · §3.6-1 · §3.6-3 · §3.7-1).
- **5'i** envanterin "ek olarak artık listeye girmesi gerekenler" notundan: §11.1 reduced-motion · §3.2-5
  tooltip yarısı · §9.1 Explorer · §9.2 VS · §8.5 klasör seçici.
- **5'i** A13 ölçümlerinde çıktı: §0.4 `--font-ab` · §12.3 ikinci instance · §12.4 autostart tepsi (üçü de
  T6'nın "makine-global, test yazılmayacak" kararından) · title bar dikey ortalama (B0'ın ölçümü) ·
  §2.9-1 dialog zemin tonu (envanter eki bunu "doğrulanmadı, final review'da karara bağlanacak" diye bırakmıştı).

## Listeden düşen kalemler (sebepleriyle)

- **§12.2 "banner/toast YOK"** — envanter "guard yazılmazsa göz pasına" demişti; T4'te guard yazıldı
  (`AntiSlopTests.No_toast_or_banner_component_exists`), göz istemiyor.
- **§8.7 "RootPathBox DS Input görünümünde"** — `RootPathBox` diye bir eleman yok; REPOSITORY satırı
  salt-okunur mono metin + `Change…` olarak yeniden tasarlanmış. Kalem **bayat**.
- **§2.5 "canlı `· N warnings`"** — göz kalemi değil: IPC sözleşmesinde derleyici-uyarı alanı yok.
  **Özellik** boşluğu, A14'e önerildi.

## Nasıl kullanılır

1. Uygulamayı aç: `dotnet run --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj`
   (bir repo kökü hazır olsun, ör. `D:\Projects\Delta\OSYS`; §2.x kalemleri için prototip
   `prototype\Build Orchestrator (standalone).html` yanda açık dursun).
2. Bölümleri **yukarıdan aşağı, yazıldıkları sırayla** gez — sıra uygulamayı dolaşma sıranla aynı.
3. Sapma bulursan satırın yanına kısa not düş; sapmaların nereye gideceği (fix wave ya da A13.1
   "algısal eşdeğer" listesi) `.claude/outputs/2026-07-26-10-17-visual-check-walkthrough.md` sonundaki
   **Sapma kaydı** bölümünde anlatılıyor.
