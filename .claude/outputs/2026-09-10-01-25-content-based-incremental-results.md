# İçerikten Karar — Sonuçlar

> Planın (`2026-09-10-01-25-content-based-incremental-plan.md`) uygulanmış hâli. Tasarım paketi:
> `.claude/outputs/2026-09-10-10-23-design-v1.15.0/` — **klasör adı yanlış yazılmış, içeriği v1.16.0'dır.**
> Branch: `feat/content-based-incremental`.

## Kapı: Faz 0 ölçümü

Gerçek OSYS (177 proje, 22.982 girdi dosyası, 288 MB):

| Ölçüm | Sonuç | Kapı | Karar |
|---|---|---|---|
| M0 · bugünkü git tabanı | 213 ms | — | — |
| M3 · stat geçişi (her koşuda ödenen) | **156 ms** | ≤ 500 ms ve ≤ 320 ms | geçti |
| M2 · önbellek boş, dosyalar sıcak | 1.020 ms | ≤ 2 s | geçti |
| M1 · soğuk ilk geçiş, sıralı | 243.031 ms | ≤ 6 s | **kaldı** |

M1'in nedeni ikinci bir ölçümle ayrıştırıldı (soğuk ikiz ağaç, dönüşümlü yarılar): disk NVMe olduğu hâlde
dosya başına 8,89 ms — yani bedel IO değil per-dosya açılış gideri (on-access tarama). 16 kanallı paralel
okumada 1,86 ms; tam ağaç için ~43 s.

**Kullanıcı kararı: DEVAM.** Gerekçe: kapının koruduğu bedel (her koşuda ödenen M3) rahat geçti ve bugünkü
tabandan ucuz; kalan tek şey makine başına bir kez ödenen indeksleme. Karşılığında D1'in üç kazancı alındı.
Bağlayıcı iki sonuç: ilk geçiş paralel okunur, ve konsolda kendi satırı olur.

## Yapılanlar

### Motor (Faz 1–4)

- **İmza sadeleşti:** `cfg + içerik fingerprint + upstream`. Git blob tablosu (`ls-tree`), ayrı local-diff
  terimi ve in-place/worktree mod ayrımı kalktı. Sürüm kontrolü karara HİÇ girmiyor.
- **`ProjectInputs` (yeni):** csproj + bildirilen öğeler (`Compile` + `Page`/`ApplicationDefinition`/
  `EmbeddedResource`/`Resource`) + klasör taraması (obj/bin hariç) + yukarı yürüyerek `Directory.Build.*`.
- **`SourceHashCache` (yeni):** `yol → (boyut, mtime, sha256)`, atomik yazım, bozuk dosya toleransı, git'in
  racy-2sn kuralı, paralel (16) ilk doldurma.
- **`IncrementalRunBinder` artık bir nesne:** girdiler bir kez toplanır, iki bağlama geçişi (Safe/Fast) ve SCC
  kompoziti aynı fingerprint'i paylaşır.
- **Hollow kapısı kalktı:** commit'i olmayan repo, bozuk git, sürüm kontrolsüz klasör — hepsi tam karar üretir.
- **Harici kökler:** ayrı dal kalmadı. TFVC changeset (`CurrentChangesetAsync`) geri geldi ve YALNIZ `tf vc get`
  sonrası okunuyor; konsola `Updated external '<ad>' → <revizyon>` satırı yazılıyor.

### Gösterim (Faz 5, tasarım v1.16.0)

- Satırın sağ yuvası: commit çifti → **karar etiketi** (`modified` · `affected` · `never built` ·
  `failed · retry` · `up to date · 2h`), iki renkli, yuva 118 → 134 px, gerekçe native tooltip'te.
- Proje logu kanıtı: `Last successful build: 2h ago (a3f81c2)` — yaş biçimi satırla ortak (`AgeFormat`).
- Settings'teki External projects metni v1.16.0'in son hâliyle.

### Uzak uç ve pull (Faz 7)

- `GitService.CountBehindAsync` + Sync satırı `HEAD <sha> · N commits behind origin/<branch>`.
- Alt barda `N behind` chip'i: yalnız geride, yalnız mesafe biliniyorken, yalnız aktif branch seçiliyken;
  koşuda pasif. Tıklama → yeni `pullRepository` komutu → ff-only; başarıda chip düşer ve konsol KORUNARAK Sync.
- Mutasyon yüzeyi genelleşti: `Core/Externals/ExternalGitUpdater` → `Core/Git/FastForwardUpdater`. Kaynak
  guard'ı artık klasöre değil TEK DOSYAYA çitliyor; değişmez metni bilinçli olarak yeniden yazıldı.

### Dokümanlar (Faz 6)

ARCHITECTURE §1 (değişmezler), §7.1/7.4/7.5, §10.1, §10.6, **§10.7 (yeni)**, §13.2 (satır etiketi + chip),
§14.3, §16 (yeni önbellek dosyası), §21.3, §22; README (karar kaynağı, satır etiketi, behind chip'i, geçişte
tek seferlik tam derleme); CLAUDE.md değişmezleri; sürüm notları.

## Kanıt

- **Süit:** 2561 test yeşil (Acceptance ve Measurement hariç).
- **Kabul koşusu (gerçek OSYS, canlı):** Run 1 → 177 proje, 138 başarılı, 6 hata, 69 s. Run 2 (kaynak
  değişmeden) → 132 satır `up to date` atlandı; derlenen 12 projenin tamamı meşru kümede (hata + depIssue).
  Tek dosya değişimi → hedef + 23/23 transitif bağımlı derlemeye girdi, 114 ilgisiz proje atlandı.
  HEAD ve branch koşu öncesi/sonrası aynı (K1).
- **Kırmızıdan yeşile geçen iki kusur:** commit'lenmiş `.xaml` değişikliği ve sürüm kontrolünün hiç görmediği
  kaynak dosya artık projeyi derletiyor (ikisi de eski motorda kırmızı koşturuldu).

## Bilinen sınırlar

- **Yükseltmede tek seferlik tam derleme:** imza formülü değiştiği için kayıtlı imzalar karşılaştırılamaz.
  README'de ve sürüm notunda yazılı.
- **İlk indeksleme:** makine başına bir kez ~43 s (OSYS ölçeğinde, paralel). Repo klasörünü Defender
  istisnasına eklemek bunu birkaç saniyeye indirir — kullanıcı tarafı, araç bunu yapmaz.
- **Yuvalanmış projeler:** bir projenin klasörü başkasını içeriyorsa üstteki fazladan derlenir (güvenli taraf,
  MSBuild'in SDK glob'uyla aynı davranış).
- **TFVC ana repo hâlâ yok:** karar hattında git bağımlılığı kalmadı, ama Sync kapısı/fetch/branch/worktree ve
  pull git'e özgü. İleride oralarda bir kök-türü ayrımı açmak yeterli.
