# Tepsi Build Animasyonu — Main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. İş bittiğinde `main` üzerinde açılan yeni bir session'ın ilk
> mesajına aşağıdaki "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır.

| | |
|---|---|
| **Branch** | `feat/tray-build-animation` |
| **Plan** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-opus-prompt.md` |
| **Merge hedefi** | `main` |
| **Merge commit mesajı** | `merge: tepsi build animasyonu` |

---

## Yapıştırılacak prompt

`feat/tray-build-animation` branch'inde uygulama tepsideyken koşan derleme için ekranın sağ alt köşesinde
penceresiz, arka plansız, tıklama-geçirgen ve canlı sayaçlı logo animasyonu ile bitişte OS balloon bildirimi
geliştirildi. Geliştirme şu iki dosyaya göre yapıldı:

- Plan: `.claude/outputs/2026-08-20-12-40-tray-build-animation-plan.md`
- Uygulama promptu: `.claude/outputs/2026-08-20-12-40-tray-build-animation-opus-prompt.md`

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra yukarıdaki plan ve uygulama promptu. Planın bağlayıcı kararları
   (K-1…K-14) ve tasarım kaynağı tablosu merge'ün ölçütüdür.
2. **Branch'i incele.** `git log --oneline main..feat/tray-build-animation` ve
   `git diff --stat main...feat/tray-build-animation` ile ne geldiğini çıkar. Planın task listesiyle
   karşılaştır: **eksik kalan task var mı**, plan dışına taşan değişiklik var mı? Varsa merge etmeden önce
   bana bildir.
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

- Bu iş **tamamen App tarafıdır** — Supervisor / Core / Contracts'ta değişiklik OLMAMALI. Diff bunu
  gösteriyorsa merge etme, sor.
- `ChevronShift.X` ile `SweepShift.X` birebir aynı keyframe ve KeySpline değerlerini taşımalı; süpürme
  etkisi tamamen bu senkrona bağlı.
- Geometri `BrandGeometry.xaml`'de tek kaynakta mı, `AppMark` ona geçmiş mi (kopya yasak).
- Clip ve Effect ayrı Canvas'larda mı (aynı elemanda gölge çizgisi oluşur); döngü 3.000 s, boş kare yok.
- Reduced-motion yolunda döngü hiç başlamıyor, statik işaret + canlı sayaç mı gösteriliyor.
- Balloon yalnız tepsideyken çıkıyor, metni ribbon'un terminal satırıyla tek kaynaktan mı geliyor (K-5).
- Yeni XAML kökü/şablonu eklendiği için **realize testi** zorunludur — `window.Content` üzerinde yapılmış
  olmalı.
- Görsel doğrulama derlenebilen makinede gözle yapılmış olmalı (Task 6); yapılmadıysa merge'den önce yap.
