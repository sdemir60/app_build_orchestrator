# Tepsi Build Animasyonu — Güncel main Üzerine Taşıma Sonucu (v2)

> Bu dosya bir **kayıttır**, prompt değil. 2026-08-20 dörtlüsünün (plan / uygulama promptu / merge promptu /
> sonuç kaydı) devamıdır: orijinal `feat/tray-build-animation` branch'i `main`'in 162 commit gerisinde kaldığı
> için iş, güncel `main` üzerine yeniden oturtuldu.

| | |
|---|---|
| **Plan** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-plan.md` |
| **Uygulama promptu** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-opus-prompt.md` |
| **Orijinal sonuç kaydı** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-results.md` (sapmalar S-1…S-7, ölçümler) |
| **Orijinal merge promptu** | `.claude/outputs/2026-08-20-12-40-tray-build-animation-merge-prompt.md` (Task 6 senaryo listesi oradadır) |
| **Güncel merge promptu** | `.claude/outputs/2026-09-12-10-31-tray-build-animation-v2-merge-prompt.md` |
| **Branch** | `feat/tray-build-animation-v2` — `origin`'e push EDİLDİ |
| **Taban** | `main` @ `a0b0ae5` |
| **Eski branch** | `feat/tray-build-animation` — DOKUNULMADI; v2 merge edilince `git branch -D` ile silinebilir |
| **Durum** | Derleniyor (0 hata); **tam süit KOŞULMADI**, **Task 6 gözle doğrulama YAPILMADI** — ikisi de kullanıcı kararıyla ana projede yapılacak |

---

## Yöntem

Eski branch'in son İŞ commit'inden (`6b9f6ef`) yeni branch açıldı ve `git rebase main` ile yedi commit task
sırasıyla güncel `main` üzerine taşındı. Eski branch'in tepesindeki üç docs commit'i (`f8f052c`, `014a664`,
`a74720c`) BİLEREK alınmadı: başka bir oturumun yanlışlıkla bu branch'e yazdığı clean/optimize kayıtlarıydı ve
dokundukları dosyalar `main`'dekiyle birebir aynı içerikteydi (`git diff` boş) — taşınacak bir şey yoktu.

İş sabit worktree'de (`app_build_orchestrator-ai`) yapıldı. Ana projede başka bir iş sürdüğü için süit
koşulmadı — kullanıcı talimatı; derleme worktree'de alındı.

## Commit'ler (task → commit → not)

| Task | Commit | Not |
|---|---|---|
| T1 | `c432c26` | çakışmasız |
| T2 | `0c3b205` | çakışmasız |
| T2+ | `429f2bc` | çakışmasız |
| T3 | `d083382` | **çakışma:** `StatusGlyph.cs` — bkz. uyarlama 1 |
| T4 | `45ca64e` | çakışmasız |
| T5 | `f26b142` | **çakışma:** `MotionTokens.cs` — bkz. uyarlama 2 |
| T7 | `a4c5e8c` | çakışmasız; ARCHITECTURE/README hunk'ları yerine oturdu, birleşmiş diff gözle okundu |
| — | `c68a62f` | derleme düzeltmesi — bkz. uyarlama 3 |
| — | `dd2030b` | sürüm notu — bkz. uyarlama 4 |

## Güncel koda uyarlamalar

1. **`StatusGlyph.cs` → main tarafı alındı.** Branch burada yalnız sınıfın `DecorativeFrameRate` kopyasını
   silip `MotionTokens`'a bağlıyordu. `main` bu arada glyph'in building nabzını tamamen kaldırdı
   (`MotionOwnerHygieneTests` doc'u: glyph bir motion sahibi olmaktan çıktı); dosyada artık ne nabız ne kare
   hızı var. Branch'in değişikliğinin hedefi yok olduğu için main'in hali aynen kaldı.
2. **`MotionTokens.cs` → iki taraf da kaldı.** Aynı bölgeye `main` `DiscreteColorCycle`'ı, branch
   `ResolveSlow`'u eklemişti. İkisi de kendi `<summary>` bloğuyla art arda duruyor; davranış değişmedi.
3. **`GraphView.xaml.cs:520` → `MotionTokens.DecorativeFrameRate`.** Branch `GraphView.DecorativeFrameRate`
   sabitini kaldırmıştı; `main` aynı sabiti okuyan yeni bir dekoratif animasyon (`flicker`) eklemişti →
   `CS0103`. Yeni kullanım da tek kaynağa bağlandı. Uygulamada kare hızının tek tanımı var
   (`MotionTokens.cs:32`); hiçbir dosyada ikinci bir tanım ya da literal `30` kare hızı kalmadı.
4. **Sürüm notu eklendi.** What's new sekmesi (`Services/ReleaseNotes.cs`) bu branch `main`'den ayrıldıktan
   SONRA geldi; `main`'deki her kullanıcıya görünen özellik çalışan sürümün girdisine madde ekliyor (örn.
   `11066a3`). Tepsi göstergesi + bitiş bildirimi için bir `Added` maddesi yazıldı. Plan bunu öngöremezdi;
   istenmezse tek satır geri alınır.

Plandan sapma listesi orijinal kayıttaki S-1…S-7 ile aynıdır; bu taşıma yeni sapma getirmedi.

## Statik doğrulama (süit koşulmadan yapılabilenler)

`main`'e bu arada gelen ya da değişen kaynak-tarayan guard'lar kural kural okundu:

| Guard | Kural | Sonuç |
|---|---|---|
| `ColorTransitionFlashTests` | `.cs`'te `new *ColorKeyFrame` yalnız `MotionTokens.cs` | yeni dosyalarda renk keyframe/animasyonu YOK (yalnız transform + opacity) |
| `IconGeometryTests` (main'de değişti) | değişiklik yalnız ikon anahtar listeleri | tarama kuralları aynı; branch merge-base'de bunları geçiyordu |
| `MotionOwnerHygieneTests` (main'de değişti) | StatusGlyph satırları silindi, spinner 1400 ms; owner listesi açık `InlineData` | yansıma yok, yeni kontrol listeye girmiyor |
| `ShortcutCatalogTests` (main'de değişti) | `KeyboardShortcuts.WindowBindings` + jest literal taraması | tray dosyalarında jest literal'i yok |
| `SourceGuardScanRaceTests` (yeni) | `RepoPaths` `_wpftmp.csproj` eler | yalnız yardımcıyı değiştiriyor; test projesi derlendi |
| `NoSleepPollTests` | `MainWindow.xaml.cs` izni 1 | dosyada tek `Task.Delay` (nefes dikişi) |
| `NoTurkishUserTextTests` | kullanıcıya görünen metin İngilizce | tray kaynaklarında Türkçe karakterli string literal yok |
| `AppResourcesMergeTests` | merge zinciri beş sözlük | `App.xaml`: Motion · Tokens · Icons · BrandGeometry · Controls |

Ayrıca: test projesinde ad çakışması yok (`FakeMotionSettings` main'in; tray testlerinin sahteleri private
nested); değişiklik yüzeyi yalnız `src/BuildOrchestrator.App`, `tests`, `ARCHITECTURE.md`, `README.md` —
Core/Supervisor/Contracts'ta sıfır dosya.

**Bunlar derleme ve okuma kanıtıdır, koşu kanıtı değil.** Realize testleri (STA), `NoHardcodedMotionTests`'in
istisna-bayatlama testi ve `AppMarkTests`'in ölçüm pini ancak süitte görülür.

## Açık kalanlar (ana projede, kullanıcı onayıyla)

1. **Tam süit:** `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
   — uygulama kapalıyken.
2. **Task 6 — gözle doğrulama:** on senaryo orijinal merge promptunda. Hiç yapılmadı; per-pixel geçirgenlik,
   bitiş ritmi ve reduced-motion karesi ancak gerçek HWND'de görülür.

## `feat/clean-button-engine-v2` ile çakışma yüzeyi

İki branch de `main`'de değil. Ortak dosyalar: `MainWindow.xaml.cs` (tray: kurulum + `OnClosed`; clean: bakım
kutusu kablosu), `ViewModels/RunViewModel*.cs` (tray: `RibbonLine` property'si `RunViewModel.cs`'te; clean:
`RunViewModel.Workspace.cs`), `Services/ReleaseNotes.cs` (ikisi de aynı listeye madde ekliyorsa komşu-satır
çakışması — ikisi de kalır), `tests/App/NoSleepPollTests.cs` (tray: `MainWindow` izni; clean: D8 izin satırı).
Hangisi önce merge edilirse diğeri `git merge main` sonrası bu dört dosyada küçük bir birleştirme yapar.
