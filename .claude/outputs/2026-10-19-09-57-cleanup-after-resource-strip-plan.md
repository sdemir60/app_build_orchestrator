# Kaynak şeridi sonrası temizlik — uygulama planı

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans (kullanıcı kararı: native, sonda tek bütün-dal incelemesi).

**Goal:** Kaynak şeridi işinden kalan altı küçük borcu davranış değiştirmeden kapatmak.
**Architecture:** Test/guard düzenlemesi + üç üretim satırının tek kaynağa bağlanması + doküman; motor ve UI davranışı aynı.
**Tech Stack:** .NET 10, WPF, xUnit (`StaFact`/`StaTheory`), kaynak guard'ları (`SourceGuard`).
**Spec:** `.claude/outputs/2026-10-19-09-37-cleanup-after-resource-strip-prompt.md`

## Global Constraints

- Davranış değişikliği yok; tek istisna 1. maddedeki iki testin ölçüm biçimi (eski iddia + ölçüm doc'a).
- `CHANGELOG.md` ve `Directory.Build.props → Version` dokunulmaz. Kaynak şeridinin görünümü/davranışı, `memory:` satırı, motor aynı.
- Eşik/bütçe yeşile boyamak için gevşetilmez. Her fix önce kırmızı gösterilir; yeni yardımcı en az iki yerden kullanılır.
- Uygulama Debug'dan açık: derleme/test `-c Release`. Tam süit çıktısı dosyaya.

## Review Focus

1. Zorlanan DPI'ın şablon çocuklarına gerçekten indiği (realize'dan SONRA verilmezse etiket/ikon 1.0'da kalır — ölçüldü).
2. Opt-out guard'ın istisna listesi bayatlamaz: her istisna hâlâ adı taşıyor olmalı.
3. `—` guard'ı cümle içindeki uzun tireyi (ör. `"CPU used by … — app"`) yakalamamalı; yalnız tam `"—"` literali.
4. `MsBuildInvokerTests` döngüleri: bildirim başına zaman aşımı ve mesajlar aynı kalmalı.
5. `NotesDialog` alt başlığı metin olarak birebir aynı kalmalı (`Release notes for <ürün>.`).

---

### Task 1: UpdatePillTests ekran ölçeğinden bağımsız

**Files:** Modify `tests/.../App/UpdatePillTests.cs` (iki test), `tests/.../App/MainWindowHost.cs` (`NewRealizedWithOffer`), `tests/.../App/DsResources.cs` (+`InDevicePixels`), `tests/.../App/MainWindowRealizeTests.cs` (yerel kopya kalkar).

**Ölçüm (deneme testi, makine %100'de):** DPI `VisualTreeHelper.SetRootDpi` ile zorlanır. Başsız süitte içerik pencerenin görsel çocuğu değildir: DPI pencereye (MaximizeFix dolgusu) realize'dan ÖNCE, içerik köküne realize'dan SONRA verilir ve tüm ağaç yeniden ölçülür. 1.25'te makinedeki iki kırmızı birebir çıkar: hap yüksekliği `22.4`; LayFocus sağ boşluğu hapla `231.99999999999977`, hapsız `232`. 1.75'te etiket metni yuvasından 1 px taşar (yuva 83 px, sol ofset 11, genişlik 73) → kenardan kenara etiket→sürüm boşluğu 9 px (5.14 dip).

- [ ] **1.1 Kırmızı:** `NewRealizedWithOffer(TempDir, DpiScale? dpi = null)` eklenir; iki test `[StaTheory]` + `InlineData(1.0, 1.25, 1.5, 1.75, 2.0)` olur, ESKİ iddialarla → 1.25'te kırmızı (22.4 · 231.99…).
- [ ] **1.2 Yeşil:** `DsResources.InDevicePixels(double dips, double scale)` (MainWindowRealizeTests'ten taşınır, oradaki çağrılar ona bağlanır).
  - Test 1 (yol a): her düğme için `InDevicePixels(hapla − hapsız, scale) == 0`.
  - Test 2 (yol b): `InDevicePixels(gerçek − tasarım, scale) <= 0.5` → hap yüksekliği 22; boşluklar `sonraki.X − önceki yerleşim yuvası.Right` (tek yuvarlama adımı) = 6. Özellik iddiaları (Padding, Border, radius, renk, font) aynen kalır.
- [ ] **1.3 Mutasyon (geri alınır):** hap Height 23 · etiket/sürüm Margin 7 · Padding sol 7 → test 2 her ölçekte kırmızı; sağ kümeye hap görünürken +1 dip sağ boşluk → test 1 kırmızı. Sonra yeşil.
- [ ] **1.4** Doc: eski iddia + ölçek/değer tablosu. Commit.

### Task 2: Ürün adı tek kaynaktan + opt-out guard

**Files:** `ViewModels/SettingsFile.cs` (`FileFilter` → `static string { get; } = AppIdentity.Product + " settings (*.json)|*.json"`), `Views/NotesDialog.xaml` (`Binding Source={x:Static AppIdentity.Product}, StringFormat='Release notes for {0}.'`), `Spikes/FontAbWindow.xaml.cs` (`AppIdentity.Product + " — OSYS workspace"`); `AppIdentityTests.cs`, `NotesDialogTests.cs` (beklenen metin kimlikten).

- [ ] **2.1 Kırmızı:** `NameFreeSurfaceFiles` (opt-in) → `EmbeddedNameExemptions` (opt-out): `ScanSrcLiterals("*.cs"|"*.xaml", EmbeddedNameRule, allowedFiles)`; tek istisna `Core/MsBuild/WpfTemporaryAssemblyTargets.cs` (motorun önbelleğine yazılan MSBuild dosyasındaki yorum; Core kimliği göremez, içerik değişirse dosya her makinede yeniden yazılır). Kırmızı: SettingsFile.cs:28, NotesDialog.xaml:34, FontAbWindow.xaml.cs:85.
- [ ] **2.2** Kanıt testleri: (i) istisna hâlâ adı taşır (bayat istisna yasak), (ii) istisnasız tarama tam olarak istisnayı raporlar, (iii) yeni bir dosyaya gömülü sahte ad yakalanır, yorum yakalanmaz; `AssertScansEveryProductionProject`.
- [ ] **2.3 Yeşil:** üç yer kimlikten okur. Commit.

### Task 3: `—` tek sabit

**Files:** Create `src/BuildOrchestrator.Core/Formatting/MissingValue.cs` (`public const string Mark = "—";`); bağla: `DurationFormat.Duration(null)`, `ResourceStripText.Unreadable`, `DiagnosticsReport.Unknown`. Test: `tests/.../Formatting/MissingValueTests.cs`.

- [ ] **3.1 Kırmızı:** guard — tam `"—"` literali (`^—$`) `src`'de yalnız `MissingValue.cs`'te; istisna spike örneği (`Spikes/FontAbWindow.xaml.cs`, örnek satır metni). Kırmızı: DurationFormat.cs:17, DiagnosticsReport.cs:82, ResourceStripText.cs:29. Kanıt: dikilen tam literal yakalanır, cümle içindeki tire yakalanmaz.
- [ ] **3.2 Yeşil:** üç tanım `MissingValue.Mark`'a bağlanır; bağların eşitliği pinlenir. Commit.

### Task 4: Test kopyaları ve iki eksik pin

- [ ] **4.1** `ProcessControl/GatedMemberJob.cs`: `static GatedMemberJob Start()` → `Job`, `Port`, `Child`, `More` (ters sırada dispose; kurulum düşerse kurulanlar dispose). Kullanan: ResourceSamplerTests ×3, JobResourceQueryTests ×1.
- [ ] **4.2** `tests/.../ByteUnits.cs`: `Mb`, `Gb` (`using static`); sekiz `Mb` + iki `Gb` kopyası kalkar.
- [ ] **4.3** `JobBirths.UntilExit(JobCompletionPort port, int pid, TimeSpan timeout, string what)` (`Pump` üstünde); MsBuildInvokerTests ~108 `Until`'e, ~120/~257 `UntilExit`'e (süre ve mesajlar aynı).
- [ ] **4.4** `MainWindowHost.TitleBarGrid(window)` / `TitleBarDock(window)`; MainWindowRealizeTests ~210/225, ResourceStripRealizeTests ~183/247/251.
- [ ] **4.5** `SourceGuard.UnseenProductionProjects(IEnumerable<string>)` saf çekirdek; `AssertScansEveryProductionProject` onu kullanır; kanıt testi (eksik proje + önek tuzağı `BuildOrchestrator.CoreX`) — çekirdek bozulunca kırmızı gösterilir.
- [ ] **4.6** `ResourceStripGuardTests` yerel çağrı listesine `Win32.WM_LBUTTONUP` (satır silinince kırmızı gösterilir).
- [ ] Önce/sonra ilgili sınıflar yeşil. Commit.

### Task 5: Doküman

- [ ] ARCHITECTURE §14.4 amber cümlesi What's new noktasını (`UnseenNotesDot`) tek istisna olarak anar; §14 girişine şeridin dört ölçüsünün `Tokens.xaml`'de olduğu tek cümle; §3.3 guard anlatımı opt-out; §17.2 satırları (ürün adı guard'ı, yeni `—` guard'ı, şerit guard'ına WM_LBUTTONUP); §17.4/§17.3 değişen yardımcılar (UpdatePill zorlanan DPI). Commit.

### Task 6: About → Environment — yalnız ölç ve sor

- [ ] Realize testinde Environment sekmesinin kaydırıcısı: içerik yüksekliği, görünen yükseklik, dikey çubuk görünürlüğü (1.0 ve 1.25/1.5); ekran dışı PNG. Kullanıcıya sayı + görüntü; DEĞİŞİKLİK YOK.

### Bitiş

Tam süit (Release, `Category!=Acceptance`, çıktı dosyaya) → bağımsız bütün-dal incelemesi (en güçlü model, tek tur) → `develop`'a `--no-ff` merge + push → dal silinir → CI kontrol → `.claude/summaries/` kısa özet.
