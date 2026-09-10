# Design v1.16.0 — Tasarım Promptu (SON HÂLİ)

> Bu dosya bir PROMPT'tur. Aşağıdaki bloğu tasarım session'ına olduğu gibi yapıştır. Çıkan paket
> (`…-design-v1.16.0/`) sonra bu session'a verilir; motor ve UI bağlama işi (plan Faz 1–7) o pakete göre
> burada yapılır. Karar geçmişi: `2026-09-10-01-25-content-based-incremental-plan.md` ("Karar güncellemesi").

---

## Yapıştırılacak prompt

Build Orchestrator tasarım paketinin yeni sürümünü (v1.16.0) hazırlayacaksın. Taban:
`.claude/outputs/2026-09-09-19-19-design-v1.15.0` — önce README'nin tamamını, sonra `prototype/app/build-data.js`
ve `prototype/app/BuildApp.jsx`'i oku. Aynı klasör yapısıyla yeni paket üret:
`.claude/outputs/<YYYY-MM-DD-HH-mm>-design-v1.16.0/` — README (sürüm geçmişi satırı + `## v1.16.0` bölümü +
ilgili §'ler YERİNDE yeniden yazılmış), prototype (`BuildApp.jsx`, `build-data.js`, `release-notes.js`),
standalone HTML yeniden üretilmiş, `ABOUT_VERSION` 1.16.0.

**Neden bu sürüm.** Motor değişiyor: bir projenin derlenip derlenmeyeceğine artık commit değil, projenin
kendi dosyalarının DİSKTEKİ içeriği karar veriyor; ana repo ve harici kökler için tek kural. Sonuç: satırdaki
commit çifti (`a3f81c2 → b7e91d4`, §2.4) kararı anlatmıyor ve yanıltıyor (sağ taraf pull yapılmamış bir uzak
commit, sol taraf projeye değil repoya ait). Bu sürüm üç şey yapar: satır kararı söyler, "geridesin" bilgisi
alt barda tıklanabilir bir chip olur, bir metin düzeltilir.

### 1. Satır sha yuvası → karar etiketi (§2.4 sağ blok, mono 10.5px, min 118px)

Commit çifti KALKAR. Yerine kararın gerekçesi. **Sözcükler kararlaştırıldı, değiştirme** (git + MSBuild'in ortak
dili; kullanıcı onayı var); tipografi, renk tonu, tooltip ve varsa ikon senin:

| Durum | Etiket | Motorun verdiği olgu |
|---|---|---|
| Kendi dosyaları değişmiş, derlenecek | `modified` | `will=build`, `reason=changed` (kendi dosyası) |
| Kendi dosyaları aynı, bağımlılığı değişmiş, derlenecek | `affected` | `will=build`, `reason=dependency` |
| Hiç derlenmemiş, derlenecek | `never built` | `will=build`, `reason=neverBuilt` |
| Son derlemesi hata vermişti, derlenecek | `failed · retry` | `will=build`, `reason=lastFailed` |
| Güncel, atlanacak | `up to date · 2h` | `will=skip`, `lastBuiltAt` (göreli zaman: `2h`, `3d`, `just now`) |

Kısıtlar: aynı yuvaya sığsın — hover'da 4 ikon butonla yer değiştiriyor, satırlar arası sıçrama yok (v1.7.0
kuralı); yeni token/renk açma — derlenecek satırlarda `text-secondary`, güncelde `text-faint` ayrımını koru ya
da gerekçesiyle değiştir; **başlangıç modu** (§3.1, "değişen projelerde çift SHA korunur" cümlesi) renk olmadan
da neyin bayat olduğunu OKUTMALI — o kuralı yeni etiketle yerinde yeniden yaz. Etiket ana proje ve harici
proje için AYNI (harici projeler zaten listede `External` grubunda, v1.14.0; rozet yok). Prototip modelinde
`curSha`/`targetSha` kalkar; `will`/`reason`/`lastBuiltAt` gelir. Proje logu başlığındaki
`Last successful build: yesterday 18:42 (a3f81c2)` satırı (§2.5) DOĞRU ve kalır.

### 2. "Geridesin" chip'i (§2.7 action bar, branch chip'inin yanı)

Sync'in fetch'i uzak ucu çözüyor; motor artık "yerel HEAD uzak uçtan kaç commit geride" sayısını da veriyor.
Alt barda branch chip'inin hemen yanında, **yalnız geride kalınca** görünen küçük bir chip: `3 behind`.
Kurallar:

- Görünme: geride ve çevrimiçi iken. Çevrimdışı (fetch degraded) → sayı bilinmez → chip yok. Güncel → chip yok.
- Başka bir branch seçiliyken (worktree modu) chip YOK: derleme worktree'den yapılıyor, ana ağacı güncellemenin
  anlamı yok.
- Koşu sırasında pasif (disabled), diğer bar kontrolleriyle aynı mid-run kilidi.
- **Tıklama = ff-only pull** (fetch + "yalnız geride miyim" kontrolü + `merge --ff-only`; asla merge commit'i,
  asla rebase, kirli ağaca dokunmaz). Sonuç konsola yazılır, prototip simüle etsin:
  - başarı: `Pulled origin/main — fast-forward a3f81c2..b7e91d4` → chip kaybolur → otomatik Sync
  - kirli ağaç: `Pull refused — uncommitted changes in the working tree; commit or stash them first`
  - ayrışmış: `Pull refused — local branch has diverged from origin/main; reconcile it manually`
  - ağ: `Pull failed — <sebep>` (chip kalır)
- Tooltip: `Fetch found N new commits on origin/main. Click to fast-forward; nothing is merged or rewritten.`
- Konsoldaki Sync satırı da açık yazılır (prototipteki `HEAD b7e91d4 — computing osys-state diff` yerine):
  `HEAD a3f81c2 · 3 commits behind origin/main` / `HEAD b7e91d4 · up to date with origin/main`; çevrimdışıysa
  mevcut degrade uyarısı, bu satır yok.
- Bu, "araç ana repoda pull yapmaz" kuralının BİLİNÇLİ bir güncellemesi: araç kendiliğinden asla; kullanıcı
  chip'e basarsa yalnız ff-only, yalnız aktif branch'te. README'de bu gerekçeyi yaz. Denenen/bırakılan
  alternatifleri (pasif uyarı, şeritte metin, chip menüsü) listele.

### 3. §2.9 v1.14.0 açıklama metni düzeltmesi

"They are built before everything else, **in this order**" — "in this order" DÜŞER: kart sırası yalnız çalışma
kopyalarının güncellenme sırasıdır, hariciler arasındaki derleme sırası bağımlılıktan gelir. "before" doğru,
kalır (hariciler ayrı `External` katmanında, en üstte). Uygulamadaki mevcut metin:
"Everything found under a card joins the same project list and graph, grouped at the top and built before the
repository's own projects." — bunu al ya da daha iyisini yaz; 3-Run yapısı ve "before" vurgusu kalsın.

### Paket standardı

Önceki sürümlerle aynı: sürüm geçmişi tablosuna satır; `## v1.16.0` bölümünde karar, gerekçe, **denenen ve
bırakılan alternatifler**, değişen dosyalar; §2.4 / §2.5 / §2.7 / §2.9 / §3.1 ilgili paragrafları YERİNDE
yeniden yazılır (changelog biriktirilmez); DS token'larına dokunulmaz, gerekiyorsa gerekçesiyle kayıtlı sapma;
reduced-motion ve erişilebilirlik adları; **"Gerçek uygulama notu"** bölümünde her yeni UI öğesinin yukarıdaki
hangi motor olgusundan beslendiğini tek tek yaz — bağlama bu eşlemeye birebir yapılacak. UI metinleri
İngilizce, README Türkçe.

**Dokunma:** Settings'teki External projects bölümü ve Pull before build switch'i (v1.14.0/v1.15.0); harici
projelerin listede en üstte ayrı `External` grubunda durması ve önce derlenmesi; satır ikon butonları, satır
menüsü, koreografiler; External grup başlığına revizyon EKLENMEZ (o bilgi konsola gidiyor:
`Updated external 'DoganTrend' → a1b2c3d`, motor işi).
