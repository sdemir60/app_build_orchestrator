# Kaynak şeridi sonrası temizlik — özet

Tarih: 2026-10-19 · Prompt: `.claude/outputs/2026-10-19-09-37-cleanup-after-resource-strip-prompt.md` · Plan:
`.claude/outputs/2026-10-19-09-57-cleanup-after-resource-strip-plan.md` · Dal: `chore/cleanup-after-resource-strip` → `develop`

Davranış değişikliği yok; tek istisna iki `UpdatePillTests` testinin ölçüm biçimi. Sürüm çıkarılmadı.

## Yapılanlar

1. **UpdatePillTests ölçekten bağımsız.** Makine bugün %100'de olduğu için kırmızı, DPI testte zorlanarak üretildi
   (`VisualTreeHelper.SetRootDpi`: pencereye realize'dan önce, içerik köküne sonra + bütün ağaç yeniden ölçülür; başsız süitte içerik
   pencerenin görsel çocuğu değil, sonradan doğan görsel ölçeği almıyor). 125%'te makinedeki iki kırmızı birebir çıktı: hap 22,4;
   LayFocus 231,99999999999977 ↔ 232. İki test artık %100–%200 beş ölçekte koşar: konumlar cihaz pikseli ızgarasında, tasarım
   sayıları tek yuvarlama adımıyla yarım piksel payla (boşluk önceki öğenin yerleşim yuvasından; 175%'te etiket metni yuvasından 1 px
   taşıyor), Margin'ler tam değeriyle. Mutasyon: yükseklik 23, iki boşluk 7, padding 7, ikon/etiket sağ +1, sağ kümeye +1 → her
   ölçekte kırmızı. `InDevicePixels` → `DsResources`.
2. **Ürün adı tek kaynaktan.** Ayar dosyası filtresi, What's new alt başlığı (Binding + StringFormat) ve font spike'ı
   `AppIdentity.Product` okur. Gömülü-ad guard'ı opt-out: bütün `src` (.cs + .xaml) taranır; tek gerekçeli istisna
   `WpfTemporaryAssemblyTargets.cs`'teki MSBuild yorumu (hiçbir yüzeyde görünmez) ve muafiyet o tek literale ait.
3. **`—` tek sabit:** `Core/Formatting/MissingValue.Mark`; DurationFormat, `ResourceStripText.Unreadable`, `DiagnosticsReport.Unknown`
   ona bağlı; guard tam `"—"` literalini başka yerde yasaklar (`MissingValueTests`).
4. **Test kopyaları:** `GatedMemberJob`, `ByteUnits` (Kb/Mb/Gb), `JobBirths.UntilExit`, `MainWindowHost.TitleBarGrid/TitleBarDock`,
   `SourceGuard.UnseenProductionProjects` (+ `SourceGuardTests`), `SourceGuard.ScanSrcCodeAndMarkupLiterals`; şerit guard'ı
   `WM_LBUTTONUP`'u pinler. `NewRealized` DPI'lı tek kurulum yolu.
5. **Doküman:** ARCHITECTURE §3.3, §14 girişi, §14.4 (title bar'daki tek amber istisnası: What's new noktası), §17.1, §17.2, §17.4, §22.
6. **About → Environment (yalnız ölçüldü):** içerik 297,6 / gövde 284 → 13,6 px taşma, dikey çubuk görünüyor; son satırın altı 261,6
   — bütün satırlar görünür, taşan yalnız boşluk (PATHS grubunun alt 16 + içerik kutusunun alt 20). Görüntü:
   `.claude/temp/2026-10-19-about-environment-scrollbar.png`. Kullanıcı kararı bekleniyor.

## İnceleme

Bağımsız bütün-dal incelemesi (opus, tek tur): kritik yok; I1 (yuvadan ölçülen boşluk önceki öğenin sağ boşluğunu görmüyordu — Margin
pinleriyle kapandı, kırmızı→yeşil), I2 (§17.2'deki genel kapsam cümlesi kodda yanlıştı — daraltıldı) ve küçükler aynı turda kapandı.

## Testler

Tam süit (Release, `Category!=Acceptance`) iki kez: 5747/11/1 ve 5745/11/3. Düşenler iki koşuda ayrık, dala dokunulmamış dosyalarda ve
tek başına yeşil (HeadWatcher reftable, RunViewModel gerçek motor Rebuild, SafeExit, ConsoleReaderNavigation) — yük kaynaklı.
`UpdatePillTests` iki koşuda da yeşil.

## Takip

- Kalan tüm-`src` guard'ları (MemoryTrim, MemoryRead, NoGitMutation, NoSleepPoll, NoProductNameInCode) kapsam meta-assert'ine bağlı değil.
- About → Environment kaydırma çubuğu: kullanıcı kararı.
- İsteğe bağlı: iki theory'yi bir kez gerçek %125/%150 ekranda koşmak.
