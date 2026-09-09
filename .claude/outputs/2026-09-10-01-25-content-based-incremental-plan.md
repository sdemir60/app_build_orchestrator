# İçerikten Karar: Ana Repo ve Harici Kökler İçin Tek Incremental Yol — Plan

> Bu bir PLANDIR, kod değişikliği içermez. Kodlama ancak **Faz 0 ölçümü geçerse** başlar; ölçüm eşiği tutmazsa
> plan uygulanmaz ve yalnız Plan B işlenir (aşağıda). Dosya adı bu plan ve sonucu için ortak kalır.

| | |
|---|---|
| **Taban** | `main` @ `aedf47f` |
| **Önceki turlar** | `2026-09-09-18-06-externals-as-workspace-roots.md`, `-18-47-…-results.md` |
| **Ölçüm hedefi** | gerçek OSYS reposu: 177 proje, 22.982 derleme-etkileyen dosya, 282 MB |

## Neden

Bugün "derlenecek mi" kararı iki farklı kaynaktan besleniyor: ana repoda git'in blob tablosu (`ls-tree`) +
ayrı bir kirli-dosya terimi, harici köklerde diskten okunan içerik. Bu ikilik üç somut soruna yol açıyor:

1. **xaml/resx açığı.** İmzaya giren dosya listesi yalnız `Compile` öğelerinden kuruluyor. Commit ile gelen
   bir `.xaml`/`.resx` değişikliği imzayı değiştirmiyor → proje "güncel" sayılıp atlanıyor (under-build).
   Aynı açık haricilerde de var.
2. **Git'e bağımlı kör noktalar.** Git'e eklenmemiş bir klasör ve gitignore'lanmış kaynak dosyalar blob
   tablosunda yok, kirli listede de yok → görünmez.
3. **İki kod yolu.** Aynı soruya iki cevap makinesi; biri değişince diğeri sessizce ayrışabiliyor.

Karar diskten verildiğinde üçü de kapanır, git/TFVC/sürüm-kontrolsüz kök ve çevrimdışı çalışma tek yoldan
geçer. Bedeli dosya okumaktır; bu yüzden plan ölçümle başlar.

## Bağlayıcı kararlar

- **D1 — Karar diskten.** Her proje için içerik özeti diskteki dosyalardan hesaplanır. Git ve TFVC KARARA
  GİRMEZ; sürüm kontrolü yalnız (a) harici kökü güncelleme adımında, (b) çalışma kopyası başlığındaki revizyon
  etiketinde, (c) worktree hazırlığında kullanılır.
- **D2 — Girdi kümesi TAM.** Bir projenin girdileri: `.csproj`'un kendisi; csproj'un bildirdiği öğeler
  (`Compile`, `Page`, `ApplicationDefinition`, `EmbeddedResource`, `Resource`; wildcard'lar açılır, klasör
  DIŞINA link verilen dosyalar dahil); proje klasörü altındaki derleme-etkileyen uzantılı tüm dosyalar
  (cs · xaml · resx · csproj · props · targets; `obj`/`bin` hariç); ve proje klasöründen çalışma alanı köküne
  yukarı yürürken bulunan `Directory.Build.props` / `Directory.Build.targets` / `Directory.Packages.props`.
  Tekilleştirilir, sıralanır.
- **D3 — Özet önbelleği.** `yol → (boyut, mtimeUtc, sha256)` haritası; `evaluation-cache.json`'un yanında
  kalıcı. Boyut VE mtime aynıysa özet yeniden hesaplanmaz. **§4 değişmezi dokunulmaz kalır:** o kural DLL/bin/obj
  ÇIKTI timestamp'ları içindir; kaynak dosyanın stat bilgisini özet-önbellek anahtarı olarak kullanmak
  csproj değerlendirme önbelleğinin bugün zaten yaptığı şeydir. "Racy" koruması: mtime'ı önbellek yazımının
  son 2 saniyesine düşen dosya her seferinde yeniden özetlenir (git'in index'te yaptığı önlemin aynısı).
- **D4 — Tek yol.** Ana repo ve harici kökler AYNI binder'dan geçer. `ComputeCommittedFingerprint`, blob
  tablosu (`GetTrackedBlobHashesAsync`'in imza kullanımı) ve imzadaki ayrı local-diff terimi KALDIRILIR.
  `BuildSignature.Compute` imzası sadeleşir: cfg + içerik + upstream.
- **D5 — Yol terimi köke göreli, içerik fiziksel yoldan.** Zincirdeki yol terimi çalışma alanı köküne göreli
  ve `/`-normalize kalır (bugünkü gibi); içerik ise GERÇEK dosyadan okunur. Worktree koşusunda kimlikler ana
  köke taşınmış olsa da okuma worktree'deki fiziksel yoldan yapılır. Böylece aynı içerik in-place ve worktree
  koşusunda AYNI imzayı üretir. Kök dışına link verilmiş dosya için yol terimi tam yol olur (nadir, belgelenir).
- **D6 — Sync/Build sözleşmesi değişmez.** Sync aynı hesabı yapar, hiçbir şey yazmaz; Build hesaplar, derler,
  başarılı projede imzayı persist eder. Rebuild karşılaştırmayı atlar. Cycles kapsamı değişmez.
- **D7 — Satır gösterimi tasarımın işi, ama motor veriyi hazırlar.** Satırda commit çifti yerine gerekçe ve
  zaman gösterilecek (öneri sözlüğü: `changed` · `dependency` · `new` · `retry` · `built 2h ago`). Bunun için
  tel üzerine "kendi dosyası değişti mi" bilgisi eklenir (Sync'in Fast geçişinden zaten çıkıyor). Nihai metin
  ve yerleşim tasarım paketinden gelir; bu planın Faz 5'i o paketi bekler.
- **D8 — Revizyon çalışma kopyası seviyesinde.** Ana repo: konsol/şerit (bugün var). Harici kök: External
  grubunun başlığında ad + revizyon + son güncelleme. Git'te yerel `rev-parse` (bedava); TFVC'de changeset
  YALNIZ pull açıkken, `tf get`'in hemen ardından okunur; pull kapalıysa yazılmaz. `BuildState.BuiltCommit`
  tanı için saklanmaya devam eder, satırda ÇİZİLMEZ.
- **D9 — Geçiş bedeli tek seferlik.** İmza formülü değiştiği için yükseltmeden sonraki ilk Build her projeyi
  bir kez derler. Sürüm notunda ve README'de yazılır. build-state şeması değişmez (imza opak string).
- **D10 — Git salt-okur değişmezleri aynen.** Kaldırılan yalnız imza hesabındaki git OKUMALARIDIR; fetch,
  worktree, harici ff-only ve kaynak guard'ı olduğu gibi kalır.

## Faz 0 — Ölçüm ve KAPI (kod yazılmadan önce, tek başına)

**Ne ölçülür.** Gerçek OSYS reposunda, D2'ye göre kurulmuş girdi kümesiyle:

| Ölçüm | Nasıl |
|---|---|
| M1 · Tam okuma + özet, ilk geçiş | 177 projenin tüm girdileri diskten okunur, SHA256 alınır; duvar saati |
| M2 · Tam okuma + özet, ikinci geçiş | aynı işlem hemen ardından (OS dosya önbelleği sıcak) |
| M3 · Yalnız stat geçişi | D3 önbelleğinin sıcak durumunu temsil eder: 22.982 dosya için boyut+mtime okuma |
| M0 · Bugünkü taban | `ls-tree -r HEAD` + `status --porcelain` + kirli dosya okumaları; duvar saati |

Ölçüm aracı: test projesinde `[Trait("Category","Measurement")]` etiketli, varsayılan süitten hariç, OSYS
kökünü ortam değişkeninden alan tek bir test. Sonuçlar bu dosyanın "Faz 0 sonucu" bölümüne YAZILIR.

**Kapı (geçme koşulu, hepsi birden):**

- M3 ≤ 500 ms **ve** M3 ≤ 1,5 × M0 — her Sync/Build'de ödenecek gerçek bedel budur.
- M2 ≤ 2 s — önbellek boş/bozuk düştüğünde yaşanacak en kötü sıcak durum.
- M1 ≤ 6 s — ilk kurulum / önbellek yokken tek seferlik.

**Kapı tutmazsa plan DURUR.** Kod yazılmaz. Yalnız **Plan B** uygulanır: mevcut git-tabanlı yolda girdi
kümesini D2'ye göre genişletmek (xaml/resx/props açığını kapatmak) ve harici yolu olduğu gibi bırakmak.
Kararı kullanıcı verir; ben sayıları yazıp beklerim.

## Faz 1 — Girdi kümesi (`ProjectInputs`)

- `Core/Incremental/ProjectInputs.cs`: `Collect(EvaluatedProject, workspaceRoot) → IReadOnlyList<string>`
  (mutlak yollar, tekil, sıralı). `CsprojEvaluator` `Page`/`ApplicationDefinition`/`EmbeddedResource`/
  `Resource` öğelerini de toplayacak şekilde genişler (bugün yalnız `Compile`).
- **Kırmızı testler (önce yazılır):** commit'lenmiş `.xaml` değişikliği imzayı değiştirir; `.resx` aynı;
  klasör dışına link verilen `.cs` dahil; `Directory.Build.props` değişince altındaki HER proje etkilenir;
  `obj`/`bin` altındaki dosya sayılmaz; `.txt`/`.md` sayılmaz; sıra deterministik.

## Faz 2 — İçerik özeti + önbellek (`SourceHashCache`)

- `Core/Incremental/SourceHashCache.cs`: yükle / `HashOf(path)` / kaydet. Bozuk dosya sessizce boş kabul
  edilir (evaluation-cache ile aynı tutum). Kaydetme atomik (`temp + File.Move`), `BuildStateStore` deseni.
- `IncrementalPlanner.ComputeContentFingerprint` D5'e göre genelleşir: (köke göreli yol, içerik özeti) çiftleri.
- **Kırmızı testler:** aynı stat → özet yeniden hesaplanmaz (okuma sayacı ile kanıt); boyut ya da mtime
  değişince yeniden hesaplanır; son 2 sn'de değişen dosya önbelleğe güvenmez; bozuk önbellek dosyası
  hesaplamayı düşürmez; in-place ve worktree kökünden aynı içerik → aynı imza (D5).

## Faz 3 — Tek yol

- `IncrementalRunBinder.Bind` yalnız D1 yolunu bilir: `trackedBlobHashes`, `dirtyRepoRelativePaths`,
  `inPlace`, `externalProjectIds` parametreleri KALKAR. Sync ve Program aynı çağrıyı yapar.
- `BuildSignature.Compute` local-diff terimini kaybeder; determinism/boundary-shift testleri korunur.
- `ProjectIdentityRebase` sonrası içerik okuması fiziksel yoldan (D5): binder'a `physicalPathOf(id)` verilir.
- **Davranış değişince test de değişir:** `IncrementalPlannerTests`'teki committed-fingerprint pinleri ve
  `BuildSignatureTests`'teki "worktree modunda dirty imzayı değiştirmez" pini YENİ kuralı pinleyecek şekilde
  yeniden yazılır; eski iddia ve gerekçe test doc'una işlenir.
- **Kırmızı testler:** git'e eklenmemiş dosya derletir; gitignore'lu kaynak dosya derletir; commit'lenmemiş
  düzenleme derletir (mevcut davranış korunur); ilgisiz commit ilgisiz projeyi derletmez (mevcut davranış
  korunur); ana proje ve harici proje aynı işlemden aynı sonucu alır.

## Faz 4 — Harici kökler

- Harici projeler otomatik olarak aynı yola girer (ayrı `IsExternal` dalı binder'dan kalkar).
- TFVC changeset: `TfvcService.CurrentChangesetAsync` geri gelir; `ExternalUpdater` `tf get` sonrası okur ve
  kök başına revizyonu döner. `ExternalRevisionReader` git için kalır. İkisi tek bir `kök → revizyon`
  haritasında birleşir (D8).
- **Kırmızı testler:** git kökü pull kapalıyken de revizyon verir; TFVC kökü pull kapalıyken revizyonsuz,
  açıkken changeset'li; harici projede xaml commit senaryosu.

## Faz 5 — Gösterim (TASARIM PAKETİNİ BEKLER)

- Tel: `BuildPreviewItem`'a "kendi dosyası değişti" bilgisi (kuyruk, nullable); `WorkspaceTopologyEvent`'e
  harici kök özeti (`ad · vcs · revizyon · son güncelleme`) — kuyruk alanlar, eski satırlar parse olur.
- App: satır sha yuvası → gerekçe/zaman (tasarımın sözlüğüyle); External grup başlığında revizyon; ana repo
  HEAD şeritte kalır. `ShaSlotText`/`ShortSha` ve `IsExternal` sha-itme kuralı yerini yeni kurala bırakır.
- Realize testleri (headless süit XAML'ı görmez), AccessibilityNames, NoTurkishUserText/AntiSlop guard'ları.
- Bu faz tasarım paketi gelmeden BAŞLAMAZ; Faz 1–4 ondan bağımsız tamamlanabilir ve tek başına merge
  edilebilir (satır o ana kadar bugünkü gibi commit çiftini göstermeye devam eder).

## Faz 6 — Dokümanlar

ARCHITECTURE §4 (kaynak sinyali ve stat-önbellek ayrımı), §7 (imza), §8.6 (planlama hattı), §10.1 (git
tablosundan imza okumalarının çıkışı), §10.6, §16 (yeni önbellek dosyası), §22; README (geçişte tek seferlik
tam derleme, satır anlamı); CLAUDE.md değişmezine stat-önbellek notu; sürüm notu.

## Kabul senaryoları

1. Tam süit + Acceptance yeşil; guard'lar yeşil.
2. **Xaml:** bir ekranın xaml'ı başka bir makinede commit'lenip pull edildiğinde proje derlenir.
3. **Çevrimdışı:** ağ kapalıyken Sync uyarıyla tamamlanır, Build hiç ağa çıkmaz (pull kapalı) ya da uyarıyla
   devam eder (pull açık).
4. **Worktree:** aynı branch'i in-place ve worktree ile derlemek aynı imzaları üretir; mod değiştirmek tam
   yeniden derleme tetiklemez.
5. **Harici git + TFVC:** ikisi de aynı kararı alır; başlıkta revizyon (TFVC yalnız pull açıkken).
6. **Yükseltme:** ilk Build her şeyi derler, ikinci Build hiçbir şeyi derlemez.
7. **Ölçüm:** Faz 0 sayıları bu dosyada yazılı ve kapıyı geçmiş.

## Riskler

- **Performans** — Faz 0 kapısıyla yönetilir; geçmezse Plan B.
- **Önbellek yanlış pozitif** (aynı boyut + aynı mtime, farklı içerik) — mtime NTFS'te 100 ns çözünürlüklü;
  "racy" 2 sn kuralı ile kapatılır; ayrıca Rebuild her zaman çıkış yoludur.
- **Worktree yol eşlemesi** (D5) — in-place/worktree eşitlik testi pinler.
- **Kök dışı link dosyaları** — tam yol terimi; belgelenir.
- **Geçişte tek seferlik tam derleme** — kullanıcıya önceden söylenir.

## Faz 0 sonucu

_(ölçüm koşulduğunda doldurulacak: M0, M1, M2, M3 ve kapı kararı)_
