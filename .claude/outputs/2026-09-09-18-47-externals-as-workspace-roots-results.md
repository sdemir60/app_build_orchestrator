# Harici Projeler = İkinci Çalışma Alanı Kökü — Uygulama Sonucu

> Plan: `.claude/outputs/2026-09-09-18-06-externals-as-workspace-roots.md`. Bu dosya planla kodun ayrıldığı
> yerleri ve kullanıcıya görünen davranış değişimlerini tutar.

| | |
|---|---|
| **Branch** | `feat/externals-as-workspace-roots` |
| **Taban** | `main` @ `35f4593` |
| **Önceki tur** | `2026-09-09-17-17-external-projects-engine-wiring.md` (yol + kaynak modeli, tek solution hedefi) |

## Ne değişti

Harici proje artık **taranan ikinci bir kök**. Ayarlar'daki yolun altındaki projeler bulunur, ana taramayla
birleşir, tek bir grafa girer ve sıradan projeler gibi derlenir. Tek işaretleri `ProjectNode.ExternalVcs`
rozetidir ve o rozet yalnız iki şeye karar verir: worktree modunda obj izolasyonu almamaları ve build-state
kayıtlarında ana reponun commit/branch'ini taşımamaları.

Kaldırılan mekanizmalar: ayrı harici faz, "önce hariciler" kuralı, zorlanan `External` katmanı (index −1),
harici MSBuild argüman varyantı, revizyon tabanlı harici imza, harici koşu iptali kapısı,
`ExternalTargetResolver` / `ExternalNodeBuilder` / `ExternalSyncInspector` / `ExternalSignature` /
`ExternalWillBuild` / `ExternalProjectsConventions` / `ExternalRunPlanner`.

Korunanlar: `ExternalProject(Path, Vcs)` sözleşmesi, Ayarlar UI'ı ve kalıcılığı, `ExternalGitUpdater`
(ff-only), `TfvcService`, `TfResolver`, `VcsDetector`, kir kapısı metinleri, kaynak guard'ı.

## Plandan sapmalar

### S1. Ayarlar kart sırası artık build sırası DEĞİL

Plan D2 sırayı graftan almayı zaten söylüyordu, ama sonucu şu: sürükle-bırak sırası yalnız **çalışma
kopyalarının tazelenme sırasıdır**. Design v1.14.0 §9 "sıra kasıtlı / önce derlenir" diyordu; müşteri projesi
tipik olarak platform DLL'lerine bağımlı olduğu için o iddia zaten yanlıştı.

### S2. İki kullanıcıya görünen metin değişti

Eski hâllerini yanlış yapan şey bu turun kendisi olduğu için ikisi de yerinde yeniden yazıldı; pinler yeni
kuralı pinler, eski iddia ve gerekçe testlerin doc'una işlendi.

| Yer | Eski | Yeni |
|---|---|---|
| Ayarlar açıklaması | "They are built **before** everything else, in this order" | "Everything found under a card joins the **same** project list and graph…" |
| Save konsol notu | `External projects → N — built before the repository projects` | `External projects → N — scanned with the repository projects` |

Vurgulanan sözcük `before` → `same` oldu; §9'un 3-Run yapısı ve tipografisi korundu.

### S3. Sync sayaçları haricileri de sayar

Eskiden "sayaçlar ana workspace'i anlatır" bir istisnaydı. Hariciler sıradan proje olunca istisna anlamsızlaştı.

### S4. Kapsam dışı bir kusur düzeltildi: guard taramasının yarışı

Tam süitin ilk koşusunda `NoTurkishUserTextTests` bir kez kırmızı verdi ve tek başına yeşildi. Sebep:
guard repo ağacını **listeleyip sonra okuyor**; WPF `MarkupCompilePass`'in ürettiği
`<Ad>_<8hex>_wpftmp.csproj` iki adım arasında silinince okuma patlıyor. Karar zaten üretim taramasında vardı,
tek kaynağa (`WorkspaceScanner.IsTransientBuildArtifact`) çıkarıldı ve `RepoPaths`'in dört taraması da onu
kullanıyor. Kırmızı, gerçek bir artefakt dosyasıyla gösterildi (`SourceGuardScanRaceTests`).

Bu, önceki turun "Bilinen açıklar 2" maddesindeki adı konmamış flake'in de büyük olasılıkla açıklamasıdır.

## Doğrulama

| Ne | Sonuç |
|---|---|
| `dotnet build BuildOrchestrator.slnx` | 0 hata; yalnız mevcut (bu turdan önce de olan) test uyarıları |
| Tam süit (`Category!=Acceptance`) | yeşil |
| Acceptance süiti | 3/3 — gerçek OSYS reposunu derleyerek |

## Bilinen açıklar

1. **Gerçek bir harici projeyle elle denenmedi.** `D:\Projects\Delta\CustomerProject\DoganTrend` gibi bir kök
   uçtan uca koşulmadı: klasörün taranıp listeye/grafa girmesi, ana projelerden ona kenar çıkması ve
   ff-only güncelleme yalnız testlerle doğrulandı.
2. **Bayrağın UI'ı yok.** `UpdateExternals` bugün yalnız kalıcı durumda ve komutta yaşıyor; aç/kapa kontrolü
   ayrı bir tasarım turunda bağlanacak (alan `[ObservableProperty]`, doğrudan bağlanabilir).
3. **Harici satırın görsel işareti yok.** `ExternalVcs` telde ve `ProjectRowViewModel.IsExternal`'da duruyor
   ama satırda çizilen bir rozet yok — satır sıradan görünür.
4. **İçerik fingerprint'inin maliyeti ölçülmedi.** Harici kökün build-etkileyen dosyaları her Sync ve Build'de
   okunuyor. Küçük köklerde görünmez; büyük bir harici kökte ölçülmeli.
