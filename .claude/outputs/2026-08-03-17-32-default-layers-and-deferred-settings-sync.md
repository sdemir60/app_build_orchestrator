# Varsayılan katmanlar + Save'e ertelenmiş Settings senkronizasyonu — tasarım

Tarih: 2026-08-03 · Branch: `feat/default-layers-and-deferred-settings-sync`

## Amaç

İki bağımsız kusur/eksik tek işte kapanır:

1. **Katman tanımları sıfırdan yazılıyor.** Settings'teki LAYERS editörü boş açılıyor; footer'daki
   "Load sample layers" butonu prototipten kalma 6 jenerik örneği yüklüyor
   (`LayerEditorViewModel.SampleLayers`). Araç ekiple paylaşıldığında herkesin OSYS katmanlarını elle
   tanımlaması gerekiyor.
2. **Settings'te repo yolu Save beklemiyor.** "Change…" ile klasör seçilir seçilmez
   `RunViewModel.ChangeRepositoryAsync` çalışıyor: kök değişiyor, satırlar hollow'a sıfırlanıyor ve Sync
   gidiyor. Cancel/Esc bunu geri almıyor — Save/Cancel'ın repo üzerinde hiçbir etkisi yok.

## Kapsam dışı

- MainWindow'daki **Choose Folder** yolu (boş-durum daveti) değişmez: orada Save yoktur, seçim anında
  uygulanır.
- Katman eşleştirmesinin kendisi (`LayerEngine`), IPC sözleşmesi, `UiState` şeması değişmez.

---

## 1. Varsayılan katmanlar — tek doğruluk kaynağı

Yeni dosya: `src/BuildOrchestrator.App/Shell/LayerDefaults.cs`.

| Order | Name | Regex |
|---|---|---|
| 0 | `OSYS.Types` | `^OSYS\.Types\.` |
| 1 | `OSYS.Business` | `^OSYS\.Business\.` |
| 2 | `OSYS.Orchestration` | `^OSYS\.Orchestration\.` |
| 3 | `OSYS.UI` | `^OSYS\.UI\.` |

Proje adları `OSYS.<Katman>.<Proje…>` biçimindedir (ör. `OSYS.Types.Service.WorkOrder`); önek sabit,
sonrası proje adıdır. Regex bu yapıya birebir uyar: önek + nokta. Tek başına `OSYS.Types` adında bir proje
varsa eşleşmez, `Other` katmanına düşer — bilinçli seçim.

`LayerEngine.CompileUserPattern` kullanıcı pattern'lerini `RegexOptions.IgnoreCase` ile derler, bu yüzden
`OSYS.UI` / `OSYS.Ui` ayrımı sorun değildir. Eşleşmesi `ProjectNode.Name` (assembly kısa adı) üzerindedir.

Bu liste **tek yerde** durur; Settings taslağı ve "Restore default layers" butonu aynı listeden okur
(CLAUDE.md kopya yasağı). Mevcut `LayerEditorViewModel.SampleLayers` (6 örnek, `BuildApp.jsx:965-972`)
**silinir** — yerini bu alır.

## 2. Varsayılan yalnızca Settings taslağında görünür — açılışta seed YOK

Uygulama açılışı hiç değişmez: `UiState` şemasına dokunulmaz, `MainWindow.xaml.cs:137`'deki
`if (saved.LayerPatterns is { Count: > 0 })` seed'i olduğu gibi kalır. Repo ve ayar yokken ekranda katman
grubu görünmez, proje listesi tek liste olarak kalır.

Tek kural: **Settings diyaloğu açıldığında kayıtlı katman yoksa taslak 4 varsayılanla dolu gelir.** Kayıtlı
katman varsa taslak onların kopyasıyla kurulur — kullanıcının tanımları asla ezilmez.

Save'e basılmadıkça hiçbir şey kalıcı olmaz ve ekran değişmez.

**Bilinen ve kabul edilen sonuç:** kullanıcı tüm katmanları silip Save derse (ekranda katman kalmaz —
doğru davranış), Settings'i tekrar açtığında taslak yine varsayılanlarla dolu gelir. "Hiç kaydetmedim" ile
"bilerek boşalttım" ayrımını tutacak kalıcı bir bayrak eklenmez: bu ayrım yalnız diyalog taslağını
etkiler, ekrana veya motora yansımaz, ve şema alanı eklemeye değmez.

## 3. UI — "Restore default layers"

Footer'daki mevcut ghost buton (`SettingsDialog.xaml:200-201`, `Ds.Button.Ghost.Md`) yerinde kalır; yalnız
etiketi ve davranışı değişir:

- Etiket: `Load sample layers` → **`Restore default layers`**
- Davranış: taslağı §1'deki 4 katmanla değiştirir (mevcut `LoadSampleLayers` ile aynı mekanik —
  A13.2 reset yasağı gereği `Clear()` yerine sondan sil + ekle).

Yeni kontrol, yeni ölçü, yeni token yok; yerleşim ve tasarım dili aynen korunur. Etiket "default
**layers**" der çünkü buton LAYERS bölümüne aittir, tüm ayarları sıfırlamaz.

## 4. Repo yolu Save'e ertelenir

`LayerEditorViewModel` → **`SettingsDraftViewModel`** olarak yeniden adlandırılır (dosya adı da). Artık
yalnız katmanları değil, seçilmiş-ama-uygulanmamış repo yolunu da tutan bir taslaktır; eski ad yanıltıcı
kalırdı. `LayerRowViewModel` aynı dosyada ve aynı isimde kalır. VM saf (WPF'siz) kalır — testler Window
olmadan sürer.

Taslağa eklenen alan: bekleyen repo kökü (diyalog açılırken `RunViewModel.RootPath` ile başlar).

Diyalog davranışı:

- **Change…** → klasör seçici sonucunu taslağa yazar ve yalnız diyalogdaki yol etiketini günceller. Kök
  değişmez, satırlar sıfırlanmaz, Sync gitmez, konsola not düşmez.
- **Cancel / Esc / scrim** → taslak atılır; katman ve repo tarafında hiçbir iz kalmaz.
- **Save** → tek yolda uygulanır (aşağıda).

## 5. Save akışı — tek Sync

`SettingsDraftViewModel.CommitAsync(run, store)`:

1. Taslaktan pattern'leri üret (`Order` = satır indeksi, ad trim'li) ve `UiState.LayerPatterns`'a persist et.
2. `RunViewModel`'in tek giriş noktasını çağır: `ApplySettingsAsync(patterns, pendingRoot)`.

`RunViewModel.ApplySettingsAsync(patterns, root)`:

1. `ApplyLayerPatterns(patterns)` — `LayerPatterns` set edilir + mevcut BİREBİR konsol notu yazılır
   (`Layer definitions updated — {n} layers` / `Layers removed — single project list`).
2. Koşu sürüyorsa (`IsMidRunLocked`) burada **durur**: kök değişmez, Sync gitmez. Katmanlar yine
   kaydedilmiştir ve bir sonraki Sync/Build'e gider. Gerekçe: koşan bir build'in kökünü altından çekmek
   doğru değildir; mevcut `ChangeRepositoryAsync` de mid-run'da no-op'tur.
3. Kök taslakta değiştiyse (`OrdinalIgnoreCase` — Windows yolları) uygula: `RootPath` set, satırlar
   hollow'a sıfırla, `_willBuildIds` temizle, run yüzeyini tazele.
4. `RootPath` boş değilse **tek bir Sync** gönder.

**Sıra zorunludur:** katmanlar Sync'ten önce uygulanır, aksi halde `SyncWorkspaceCommand` eski
pattern'lerle giderdi (`RunViewModel.cs:520` komutu `LayerPatterns`'i taşır).

**Sync koşulsuzdur:** repo mu katman mı değişti diye ayrım yapılmaz. Save'e basmak = senkronize et. Sync
salt-okurdur ve tekrarı zararsızdır (`RunViewModel.cs:524`). Tek istisna, yukarıdaki iki kapıdır: mid-run
ve boş `RootPath`.

**Kopya yasağı:** kök değiştirme adımı (3) ortak bir private metoda çıkarılır;
`ChangeRepositoryAsync` (Choose Folder yolu) de aynı metodu kullanır ve mevcut davranışını korur
(kök değişti → hemen Sync). `ApplySettingsAsync` ile `ChangeRepositoryAsync` arasında kopyalanmış blok
kalmaz.

---

## Test planı

Kırmızı test kuralı: her davranış için ayrı test, her biri fix'ten önce kırmızı gösterilir.

**Varsayılan katmanlar**

- Varsayılan liste birebir pinlenir: 4 katman, `Order` 0..3, adlar ve regex'ler.
- `LayerEngine.AssignLayers` ile gerçek OSYS adlarına karşı: `OSYS.Types.Service.WorkOrder` → `OSYS.Types`,
  `OSYS.UI.Service.WorkOrder` → `OSYS.UI`, eşleşmeyen bir ad → `Other`.

**Taslak varsayılanı**

- Kayıtlı katman yokken kurulan taslak 4 varsayılanla dolu gelir.
- Kayıtlı katman varken taslak **onların** kopyasıdır (varsayılan ezmez).
- Taslağın dolu gelmesi tek başına hiçbir şey kaydetmez/uygulamaz (Save yoksa `LayerPatterns` ve
  `UiState` dokunulmamış).

**Restore default layers**

- Buton taslağı 4 varsayılanla değiştirir; Save'siz kalıcı değildir.

**Repo ertelemesi**

- Change… tek başına: kök değişmez, satırlar sıfırlanmaz, hiçbir komut gönderilmez.
- Cancel sonrası kök eskisidir.
- Save sonrası: kök yeni, satırlar hollow, **bir tane** `SyncWorkspaceCommand` ve yeni kökte.

**Save → Sync**

- Yalnız katman değişip Save → Sync gider ve komut YENİ pattern'leri taşır.
- Hiçbir şey değişmeden Save → Sync yine gider (ayrım yok kuralının pini).
- `RootPath` boşken Save → Sync gitmez, katmanlar yine kaydedilir.
- Mid-run Save → kök değişmez, Sync gitmez, katmanlar kaydedilir.

**Güncellenen mevcut testler** (silinmez — yeni kuralı pinleyecek şekilde yeniden yazılır, doc'una eski
iddia + değişme gerekçesi işlenir)

- `SettingsDialogTests.Saving_layers_writes_the_exact_console_note_and_persists_the_patterns` — 6 örnek
  katmanı ve `Layer 0 — Core` / `^OSYS\.(Base$|Common\.)` değerlerini pinliyor.
- `SettingsDialogTests.Changing_the_repository_resets_state_and_starts_a_sync_at_the_new_root` — bu
  `ChangeRepositoryAsync` yolunun (Choose Folder) testi olarak KALIR; Settings→Change… artık bu yola
  girmediği için testin doc'u bunu açıkça söyler.
- `SettingsDialogViewTests` boş-durum kutusu testi — kutu artık "taze diyalog"da değil, kullanıcı tüm
  satırları silince görünür.
- Buton etiketini/erişilebilirlik adını okuyan realize ve accessibility testleri.

Bitişte tam süit yeşil (`Category!=Acceptance`), token/motion/D8 guard'ları dahil.

## Doküman

- **ARCHITECTURE.md** — Settings/LAYERS ve repo değiştirme davranışını anlatan bölümler yerinde yeniden
  yazılır: varsayılan katmanlar, "Restore default layers", Save'e ertelenmiş repo + koşulsuz Sync. Anlatı
  üslubu korunur, changelog yazılmaz.
- **README.md** — kullanım tarafında Settings'in Save davranışı yanlış bir şey söylüyorsa düzeltilir.

## Dokunulan dosyalar

| Dosya | Değişiklik |
|---|---|
| `src/BuildOrchestrator.App/Shell/LayerDefaults.cs` | **yeni** — 4 varsayılan katman, tek doğruluk kaynağı |
| `src/BuildOrchestrator.App/ViewModels/LayerEditorViewModel.cs` | → `SettingsDraftViewModel.cs`; `SampleLayers` silinir, `RestoreDefaults` + bekleyen repo kökü + `CommitAsync` |
| `src/BuildOrchestrator.App/Views/SettingsDialog.xaml` | footer buton etiketi |
| `src/BuildOrchestrator.App/Views/SettingsDialog.xaml.cs` | Change… taslağa yazar; Save `CommitAsync` çağırır |
| `src/BuildOrchestrator.App/ViewModels/RunViewModel.ActionBar.cs` | `ApplySettingsAsync` + ortak kök-uygulama metodu; `ChangeRepositoryAsync` onu kullanır |
| `tests/BuildOrchestrator.Tests/App/SettingsDialogTests.cs` + kardeşleri | yeni testler + güncellenen mevcut testler |
| `ARCHITECTURE.md`, `README.md` | ilgili bölümler yerinde güncellenir |
