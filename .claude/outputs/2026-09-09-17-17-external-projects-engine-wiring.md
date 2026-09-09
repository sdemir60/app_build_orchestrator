# Harici Projeler — Motorun Yeni Ayarlar Modeline Bağlanması (kayıt)

> `feat/external-projects-prebuild` branch'i (18 commit, `main@49ed712` tabanlı, yalnız local) main'e alındı ve
> motor kodu, main'de bu arada yapılan Ayarlar tasarımına (design v1.14.0 §9) uyarlandı. Önceki kayıtlar:
> `2026-08-19-14-07-external-projects-prebuild-{plan,results,merge-prompt}.md`.

| | |
|---|---|
| **Çalışma branch'i** | `feat/externals-engine-wiring` (main'e `--no-ff` merge edildi, `5b411ca`, push edildi, silindi) |
| **Silinen branch** | `feat/external-projects-prebuild` (local, hiç push edilmemişti) |
| **Süit** | `Category!=Acceptance`: 2472 başarılı, 1 atlanan, 0 başarısız · `Category=Acceptance`: 3/3 |
| **Build** | 0 hata, branch'in getirdiği 1 uyarı (CS8604) giderildi; main'in mevcut test uyarıları aynen |

## Modelin çatışması ve karar

İki taraf harici projeyi farklı tanımlıyordu:

| | Branch (Ağustos) | Main (design v1.14.0 §9) |
|---|---|---|
| Kalıcı biçim | `ExternalProject(Name, ProjectPath, TargetPath)` (Contracts) | `ExternalProjectRef(Path, Vcs)` (App-yerel) |
| VCS türü | diskten **tespit** (`git`/`tfvc`/`unknown` rozeti) | kullanıcı **seçer** (Git/TFVC `Ds.Select`) |
| Hedef | klasör + ayrı "Target…" seçici (.sln) | tek yol: klasör / `.sln` / `.csproj` |
| Ad | kullanıcı yazar | yok |

**Tasarım (main) kazandı.** Contracts tipi `ExternalProject(Path, Vcs)` + iki değerli `VcsKind { Git, Tfvc }`
oldu; App'teki kopya tipler silindi (`VcsKinds.Label/Parse` tek eşleme). Core her koşuda yoldan çözer:

- `ExternalTargetResolver.Resolve(path)`: dosya → kendisi; klasör → tek `.sln`, yoksa tek `.csproj`; aksi
  (yok / birden çok / uygunsuz dosya) → **sorun cümlesi** — Sync'te uyarı + hollow satır, Build'de
  `ExternalPreparationException` (koşu hiç başlamaz). Tahmin yok.
- `VcsDetector.FindRoot(dir, kind)`: yalnız **seçilen** türün işareti aranır (`.git` dizin/dosya · `$tf`).
  Bulunamazsa uyarı + olduğu gibi derleme (revizyon null → asla "up to date" olmaz). `Unknown` kavramı kalktı.
- Kimlik = çözülen hedef dosya (build-state anahtarı, IPC ProjectId, satır Id). Çözülemeyen yolun hollow
  satırı yolun son parçasıyla adlanır, kimliği yolun kendisidir.

App tarafı: `RunViewModel.ExternalProjects` main'deki gibi null olmayan liste; tel üzerine boş liste `null`
olarak çıkar (eski NDJSON şekli bayt-bayt aynı). Sync ve StartRun komutları listeyi taşır; Settings Save
listeyi tek Sync'ten önce uygular. Konsol notları main'inki (`External projects → N — built before the
repository projects` / `External projects cleared`).

## Merge çözümü

- Ayarlar UI dosyaları (`SettingsDraftViewModel`, `SettingsDialog.xaml(.cs)`, `AccessibilityNames`,
  `MainWindow`, `RunViewModel.ActionBar`, `SettingsDialogTests`): **main** alındı.
- `RunViewModel.cs` / `RunViewModel.Workspace.cs`: iki taraf birleştirildi (main'in `Fresh` + branch'in
  `IsExternal` ve hedef-sha'yı harici satıra itmeme kuralı).
- `ProjectRow.xaml.cs` (sha yuvası, `ShaSlotText`/`ShortSha`), `RunCoordinator`, `SyncWorkspaceService`,
  `Program.cs`, Core/Externals: branch'ten, sonra modele uyarlandı.
- Dokümanlar: ARCHITECTURE §5/§10.6/§13.3/§16/§21.2/§22, README adım 1 ve 4, CLAUDE.md değişmezleri —
  "UI + kalıcılık only" notu kalktı, anlatı yol + kaynak modeline göre yerinde yazıldı.

## Testler

- Yeniden yazıldı (yeni model): `VcsDetectorTests`, `ExternalTargetResolverTests`, `ExternalNodeBuilderTests`,
  `ExternalSyncInspectorTests`, `ExternalRunPlannerTests`, `ExternalSyncIntegrationTests`,
  `ExternalWireShapeTests`, `ExternalLinesTests`, `ExternalProjectsSettingsTests`,
  `ExternalProjectsWiringTests`, `ExternalRowsTests`, `ExternalRunTests`, `ProjectModelsTests`.
- Silindi (eski tasarımı pinliyordu, karşılıkları main'in `SettingsDialogTests`/`SettingsPortabilityTests`'inde):
  `ExternalDraftTests`, `ExternalSettingsSectionTests`.
- **Guard değişikliği:** `NoGitMutationOutsideExternalsTests` herhangi bir `"clean"` literalini ihlal
  sayıyordu; main'in Build menüsü öğe türleri (`new("clean", …)`) yüzünden yanlış kırmızı verdi. Kural artık
  argüman listesinin başındaki fiile bakar (`["merge", …]`); iki kanıt testi (sahte ihlal yakalanır, UI
  literali yakalanmaz) eklendi. Gerekçe test doc'unda.

## Bilinen açıklar

1. **Manuel duman testi hâlâ YAPILMADI.** Planın kabul senaryoları 3–8 (gerçek git harici, dirty, TFVC local
   workspace, harici derleme hatası, Rebuild/Cycles) gerçek bir harici projeyle uçtan uca denenmedi — bu
   makinede böyle bir proje yok. Süit yeşil, ama "kullanıcının kendi projesiyle çalışıyor mu" sorusu açık.
2. Ayarlar diyaloğu yolu **taramaz**: geçersiz yol Save'de kırmızı değildir; Sync'te uyarı, Build'de iptal
   olarak görünür (ARCHITECTURE §13.3'te böyle yazıldı — tasarım brief'i bir doğrulama istemiyor).
3. Uygulama gerçek pencerede açılıp gözle doğrulanmadı (realize testleri yeşil).
