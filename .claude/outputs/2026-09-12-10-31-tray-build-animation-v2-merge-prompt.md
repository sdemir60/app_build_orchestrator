# Tepsi Build Animasyonu (v2) — main'e Merge Promptu

> Bu dosya bir **prompt**tur, rapor değil. `main` üzerinde açılan yeni bir session'ın ilk mesajına aşağıdaki
> "Yapıştırılacak prompt" bloğunu olduğu gibi yapıştır. 2026-08-20 tarihli merge promptunun yerini alır
> (o, artık bayat olan `feat/tray-build-animation` branch'ini adreslerdi); oradaki **Task 6 senaryo listesi**
> hâlâ geçerlidir ve burada tekrarlanmaz.

| | |
|---|---|
| **Branch** | `feat/tray-build-animation-v2` (LOCAL + `origin`) |
| **Plan** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-opus-prompt.md` |
| **Sonuç kaydı (orijinal)** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-results.md` |
| **Sonuç kaydı (v2)** | `.claude/outputs/2026-09-12-10-31-tray-build-animation-v2-results.md` |
| **Task 6 senaryoları** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-merge-prompt.md` → "Bu işe özel dikkat" |
| **Merge hedefi** | `main` |
| **Merge commit mesajı** | `merge: tepsi build animasyonu` |

---

## Yapıştırılacak prompt

`feat/tray-build-animation-v2` branch'inde uygulama tepsideyken koşan derleme için ekranın sağ alt köşesinde
penceresiz, arka plansız, tıklama-geçirgen ve canlı sayaçlı logo animasyonu ile bitişte OS balloon bildirimi
var. Branch, eski `feat/tray-build-animation`'ın yedi iş commit'inin güncel `main` (`a0b0ae5`) üzerine
rebase'iyle kuruldu; sonuç kaydı `.claude/outputs/2026-09-12-10-31-tray-build-animation-v2-results.md` iki
çakışmanın çözümünü, derleme düzeltmesini, eklenen sürüm notunu ve süit koşulmadan yapılan statik guard
kontrollerini anlatır. Plan: `.claude/outputs/2026-08-20-12-40-tray-build-animation-plan.md` (K-1…K-14);
orijinal sapmalar (S-1…S-7): `.claude/outputs/2026-08-20-12-40-tray-build-animation-results.md`.

Bu iş artık `main`'e merge edilecek. Şu sırayla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md`, sonra plan, orijinal sonuç kaydı ve v2 kaydı. S-1…S-7 ve v2'deki dört
   uyarlama BİLİNENDİR — yeniden sorma; kayıtlarda olmayan bir fark bulursan merge etmeden önce bana bildir.
2. **Branch'i incele.** `git log --oneline main..feat/tray-build-animation-v2` ve
   `git diff --stat main...feat/tray-build-animation-v2`. Supervisor/Core/Contracts'ta tek dosya OLMAMALI.
   `main` bu arada ilerlediyse önce `git merge main` ile branch'i güncelle, çakışma varsa çöz ve süiti
   yeniden koş.
3. **Doğrula, iddia etme.** `git switch feat/tray-build-animation-v2` sonrası:
   ```powershell
   dotnet build BuildOrchestrator.slnx
   dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
   ```
   Süit bu branch'te bir kez yeşil görüldü (2026-09-12, worktree — v2 kaydının "Doğrulama" bölümü); `main`
   ilerlediyse ya da branch'e dokunulduysa yeniden koş. Kırmızı varsa merge etme, bana raporla. Uygulama
   açıksa kapat — çalışan Supervisor kendi binary'lerini kilitler.
4. **Task 6 — gözle doğrulama.** Orijinal merge promptundaki on senaryoyu benimle birlikte koş: uygulamayı
   sen başlat, senaryoyu söyle, ben bakarım. Bu adım yapılmadan merge yok.
5. **Doküman kontrolü.** ARCHITECTURE §12.2, §12.3, §14.4, §14.5, §22 ve README'nin ilgili paragrafı kodla
   uyuşuyor mu; `main` bu arada bu bölümlere dokunduysa anlatı bütünlüğü bozulmuş mu.
6. **Merge.** `main`'e geç, `--no-ff` ile merge et; commit mesajı: `merge: tepsi build animasyonu`. Sonra push.
7. **Doğrula ve temizle.** Merge'ün `main`'e geçtiğini doğruladıktan SONRA: `feat/tray-build-animation-v2`'yi
   local (`git branch -d`) ve remote'tan (`git push origin --delete feat/tray-build-animation-v2`) sil; içeriği
   v2'ye taşınmış eski `feat/tray-build-animation`'ı `git branch -D` ile sil (rebase'lendiği için `-d`
   reddeder; yalnız LOCAL'dir). Oturum `main` üzerinde bitsin.

**Bu işe özel dikkat:** orijinal merge promptundaki liste aynen geçerli (ChevronShift/SweepShift senkronu,
geometri tek kaynak, Clip/Effect ayrı Canvas, tek-iterasyon döngü, reduced-motion'da döngü yok, balloon yalnız
tepsideyken ve metni `RunViewModel.RibbonLine`'dan, realize testleri). v2'ye özgü tek ek: sürüm notu maddesi
`Services/ReleaseNotes.cs`'te — istenmiyorsa tek satır.

**Ortak yüzey / merge sırası:** `feat/clean-button-engine-v2` de merge bekliyor; dört ortak dosya v2 sonuç
kaydının "çakışma yüzeyi" bölümünde. Sırayı bana sor.
