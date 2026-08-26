# Tepsi Build Animasyonu — Main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. İş bittiğinde `main` üzerinde açılan yeni bir session'ın ilk
> mesajına aşağıdaki "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır.

| | |
|---|---|
| **Branch** | `feat/tray-build-animation` |
| **Plan** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-opus-prompt.md` |
| **Uygulama kaydı** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-results.md` |
| **Merge hedefi** | `main` |
| **Merge commit mesajı** | `merge: tepsi build animasyonu` |

---

## Yapıştırılacak prompt

`feat/tray-build-animation` branch'inde uygulama tepsideyken koşan derleme için ekranın sağ alt köşesinde
penceresiz, arka plansız, tıklama-geçirgen ve canlı sayaçlı logo animasyonu ile bitişte OS balloon bildirimi
geliştirildi. Geliştirme şu iki dosyaya göre yapıldı:

- Plan: `.claude/outputs/2026-08-20-12-40-tray-build-animation-plan.md`
- Uygulama promptu: `.claude/outputs/2026-08-20-12-40-tray-build-animation-opus-prompt.md`

Ne yapıldığı, plandan **nerede ve neden sapıldığı** ve hangi kararın ölçülerek verildiği şurada:

- Uygulama kaydı: `.claude/outputs/2026-08-20-12-40-tray-build-animation-results.md`

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra yukarıdaki plan, uygulama promptu **ve uygulama kaydı**. Planın
   bağlayıcı kararları (K-1…K-14) ve tasarım kaynağı tablosu merge'ün ölçütüdür; kayıt dosyası da plandan
   bilinçli sapmaları listeler — onları "plan dışına taşma" sanma, gerekçeleri orada yazılı.
2. **Branch'i incele.** `git log --oneline main..feat/tray-build-animation` ve
   `git diff --stat main...feat/tray-build-animation` ile ne geldiğini çıkar. Planın task listesiyle
   karşılaştır: **eksik kalan task var mı**, plan dışına taşan değişiklik var mı? Varsa merge etmeden önce
   bana bildir.
   - **Bilinen kirlilik:** branch'in tepesinde bu işe AİT OLMAYAN iki commit var (`f8f052c`, `014a664`) —
     başka bir oturum clean/optimize dokümanlarını yanlışlıkla bu branch'e commit etmiş. İçerikleri yalnız
     `.claude/outputs/` dosyalarıdır. Merge'den önce bunların `main`'e taşınıp branch'ten düşürülmüş olması
     gerekir; hâlâ duruyorlarsa bana sor (körlemesine düşürme — clean/optimize sonuç kayıtlarının tek kopyası
     onlarda olabilir).
3. **Doğrula, iddia etme.** `git switch feat/tray-build-animation` sonrası:
   ```powershell
   dotnet build BuildOrchestrator.slnx
   dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
   ```
   Tam süit (token / motion / kaynak guard'ları dahil) **yeşil görülmeden** merge yok. Uygulama açıksa
   kapat — çalışan Supervisor kendi binary'lerini kilitler. Kırmızı varsa merge etme, bana raporla.
4. **Doküman kontrolü.** Planın doküman güncelleme listesi uygulanmış mı; ARCHITECTURE.md §13-§14 (UI ve
   design system) ile README.md artık yanlış bir şey söylüyor mu? Eksik varsa merge'den ÖNCE aynı branch'te
   tamamla.
5. **Merge.** `main`'e geç, `--no-ff` ile merge et; commit mesajı: `merge: tepsi build animasyonu`. Sonra
   push.
6. **Doğrula ve temizle.** Merge'ün `main`'e geçtiğini doğruladıktan **sonra** branch'i local ve remote'tan
   sil. Oturum `main` üzerinde bitsin.

**Bu işe özel dikkat:**

- ⚠️ **TASK 6 — UÇTAN UCA GÖZLE DOĞRULAMA HENÜZ YAPILMADI.** Süit yeşil (2166 test) ama tıkla-aç, şeffaf
  alanın geçirgenliği, bitiş koreografisinin ritmi ve reduced-motion karesi ancak gerçek bir HWND'de gözle
  görülür. **Merge'den ÖNCE aşağıdaki senaryo listesi koşulmalıdır** (bkz. plan Task 6 ve uygulama kaydının
  "Açık kalan" bölümü).
- Bu iş **tamamen App tarafıdır** — Supervisor / Core / Contracts'ta değişiklik OLMAMALI. Doğrulandı:
  `git diff --name-only main...feat/tray-build-animation` bu üç projede tek dosya göstermiyor.
- **Guard'lara dar istisnalar eklendi ve hepsi gerekçelidir** — bunlar "testi yeşile boyamak için gevşetme"
  DEĞİLDİR, her biri kaynak-sanat ya da interop kaynaklı ve yanına bayatlama testi konmuştur. Listesi
  uygulama kaydındadır; incelerken oradan doğrula:
  motion XAML süre istisnası (tek dosya) · AntiSlop gradient muafiyetinin `BrandGeometry.xaml`'e taşınması ·
  gölge allowlist'ine overlay · Win32 stil bitlerinin renk guard'ında değer bazında izni · D8'de nefesin
  üretim varsayılanı · merge zincirinin dörtten beşe çıkması.
- **Plandan üç yapısal sapma var** (gerekçeleri kayıtta): şerit satırı VM'e taşındı
  (`RunViewModel.RibbonLine` — ikinci compose YOK, guard pinliyor) · kablaj
  `OnSourceInitialized`'dan `TrayIndicatorBinder`'a çıkarıldı (aksi halde hiçbir test göremezdi) · süpürme
  kaplaması chevron'un `Clone()`'u değil, kendi maske figürü (asset öyle).
- `ChevronShift.X` ile `SweepShift.X` birebir aynı keyframe ve KeySpline değerlerini taşımalı; süpürme
  etkisi tamamen bu senkrona bağlı.
- Geometri `BrandGeometry.xaml`'de tek kaynakta mı, `AppMark` ona geçmiş mi (kopya yasak).
- Clip ve Effect ayrı Canvas'larda mı (aynı elemanda gölge çizgisi oluşur); döngü 3.000 s, boş kare yok.
- Reduced-motion yolunda döngü hiç başlamıyor, statik işaret + canlı sayaç mı gösteriliyor.
- Balloon yalnız tepsideyken çıkıyor, metni ribbon'un terminal satırıyla tek kaynaktan mı geliyor (K-5).
- Yeni XAML kökü/şablonu eklendiği için **realize testi** zorunludur — `window.Content` üzerinde yapılmış
  olmalı.
- Görsel doğrulama derlenebilen makinede gözle yapılmış olmalı (Task 6); **yapılmadı — merge'den önce yap.**
  Sırayla: (1) Build başlat → `X` ile tepsiye in, gösterge sağ altta dönmeli ve sayaç ilerlemeli;
  (2) logonun ETRAFINDAKİ boşluğa tıkla — alttaki pencereye geçmeli, tepsi ikonu tıklanabilir kalmalı;
  (3) logonun kendisine tıkla — imleç el, tık pencereyi geri getirmeli, gösterge anında kaybolmalı;
  (4) koşunun bitişini izle — döngü çıkış evresini TAMAMLAMALI, son karede kaybolmalı, ÇOK KISA bir
  boşluktan sonra balloon gelmeli (kaybolma ile bildirim üst üste binmemeli);
  (5) balloon metni pencereyi açınca şeritte yazanla AYNI olmalı;
  (6) sayaç rakamı değişirken geçiş yumuşak, şerit genişliği/konumu SABİT;
  (7) hatalı projeyle koş → balloon Error ikonlu, "N failed" metinli;
  (8) tepsi menüsü → Stop → drain sonrası `Stopped — …` balloon'u;
  (9) koşu ortasında pencereyi geri getir → gösterge anında yok; tekrar `X` → geri gelmeli;
  (10) Windows animasyon efektleri KAPALIYKEN → statik işaret + canlı sayaç, bitişte animasyonsuz kaybolma
  + balloon yine gelmeli.
