# İçerikten Karar — Tasarım Promptu (design v1.16.0)

> Bu dosya bir **prompt**tur. Aşağıdaki "Yapıştırılacak prompt" bloğunu tasarım session'ının ilk mesajına
> olduğu gibi yapıştır. Motor tarafı ayrı session'da paralel yürüyor
> (`2026-09-10-01-25-content-based-incremental-prompt.md`); bu paket bittiğinde ikisi Faz 5'te birleşir.

| | |
|---|---|
| **Taban paket** | `.claude/outputs/2026-09-09-19-19-design-v1.15.0` |
| **Hedef** | `design-v1.16.0` — yeni klasör, aynı yapı (README + prototype + standalone) |
| **Kapsam** | satır sha yuvası → gerekçe/zaman · External grup başlığı → revizyon · uzak-fark bilgisinin yeri · sürüm notu |

---

## Yapıştırılacak prompt

Build Orchestrator tasarım paketinin yeni sürümünü (v1.16.0) hazırlayacaksın. Taban:
`.claude/outputs/2026-09-09-19-19-design-v1.15.0` — önce README'nin tamamını, sonra `prototype/app/build-data.js`
ve `prototype/app/BuildApp.jsx`'i oku. Aynı klasör yapısıyla yeni bir paket üret:
`.claude/outputs/<YYYY-MM-DD-HH-mm>-design-v1.16.0/` (README + prototype + standalone HTML yeniden üretilmiş,
`ABOUT_VERSION` 1.16.0, `release-notes.js` girdisi).

**Neden bu sürüm.** Motor değişiyor: bir projenin derlenip derlenmeyeceğine artık commit değil, projenin kendi
dosyalarının DİSKTEKİ içeriği karar veriyor (ana repo ve harici kökler için tek kural). Bunun sonucu: satırdaki
commit çifti (`a3f81c2 → b7e91d4`, §2.4) artık kararı anlatmıyor ve yanıltıyor — sağ taraf pull yapılmamış bir
uzak commit olabiliyor, sol taraf ise repo geneli bir sayı olduğu için projeye ait değil. Bu paket satırın ne
söyleyeceğini yeniden tanımlayacak ve commit bilgisini ait olduğu yere taşıyacak.

**Motorun her proje için VEREBİLDİĞİ olgular (build-data.js bunları simüle etsin, UI yalnız bunlardan beslensin):**

- `will`: derlenecek / güncel.
- `reason`: `never built` · `last failed` · `own files changed` · `dependency changed` · `up to date`. ("own" ile
  "dependency" ayrımı motorda zaten var: Sync iki geçiş yapıyor.)
- `depIssue`: başarısız bir bağımlılığa link'lenerek derlenmiş (mevcut kanal, §2.4 uyarı üçgeni).
- `lastBuiltAt`, `lastDurationMs`: son başarılı derlemenin zamanı ve süresi (kayıtta var).
- Proje logu başlığındaki `Last successful build: yesterday 18:42 (a3f81c2)` satırı (§2.5) DOĞRU ve kalır —
  o satır proje başınadır ve derlendiği commit kayıtta saklanmaya devam ediyor.

**Çalışma kopyası (kök) başına verebildikleri:**

- Ana repo: yerel HEAD sha; fetch'in çözdüğü hedef sha (çevrimdışıysa yok); istenirse "kaç commit geride"
  sayısı (ucuz, motor ekleyebilir).
- Harici kök: ad (yolun son parçası), kaynak (`git`/`tfvc`), revizyon (git: yerel HEAD, her zaman; TFVC:
  changeset, YALNIZ "Pull before build" açıkken, güncelleme adımının ardından — kapalıyken yok), son güncelleme
  zamanı, kökten çıkan proje sayısı, pull sonucu satırı (fast-forward / already up to date / degraded).

**İstenen dört şey:**

1. **Satır sha yuvası (§2.4, sağ blok, mono 10.5px, min 118px).** Commit çifti KALKAR. Yerine kararın
   gerekçesi ve zamanı — öneri sözlüğü, sen kararlaştır: derlenecekse `changed` · `dependency` · `new` · `retry`;
   güncelse `built 2h ago` gibi bir zaman ifadesi. Kısıtlar: aynı yuvaya sığsın (hover'da 4 ikon butonla yer
   değiştiriyor, satırlar arası sıçrama yok — v1.7.0 kuralı); yeni token/renk açma, `text-secondary` (derlenecek)
   ve `text-faint` (güncel) ayrımını koru ya da gerekçesiyle değiştir; **başlangıç modunda** (§3.1, satır 652:
   "değişen projelerde çift SHA korunur") renk olmadan da neyin bayat olduğu OKUNMALI — bu kuralı yeni metinle
   yerinde yeniden yaz. Tooltip/ayrıntı ister misin, sen karar ver. `curSha`/`targetSha` modeli prototipten
   kalkar; `will`/`reason`/`lastBuiltAt` gelir.

2. **External grup başlığı (proje listesi + graf bandı).** Kökün bilgisi bir kez, başlıkta: ad · kaynak ·
   revizyon · son güncelleme. Bir kökten on proje çıkıyorsa numara bir kez yazılır. TFVC'de pull kapalıyken
   revizyon YOK — o durumu dürüstçe göster (boş bırak ya da `pull off`), uydurma. Listede proje başına VCS
   rozeti yine YOK (v1.14.0 kararı). Graf bandında ne kadarı gösterilir, sen karar ver.

3. **"Uzakta yeni commit var" bilgisinin yeri.** Bugün yalnız konsol satırında (`HEAD b7e91d4 — computing
   osys-state diff`, §3.1). Satırdan kalkınca bu bilgi kaybolmasın: konsolda kalsın mı, şeride/branch chip'ine
   de bir işaret gelsin mi (ör. `3 behind`)? Kararı ver, gerekçesini yaz. Pull yapılmamışken derlemenin YEREL
   hâlden yapıldığı yanlış anlaşılmamalı — eski çiftin asıl kusuru buydu.

4. **Küçük düzeltme, §2.9 v1.14.0 açıklama metni.** "They are built before everything else, **in this order**" —
   "in this order" DÜŞMELİ: kart sırası yalnız çalışma kopyalarının güncellenme sırasıdır, hariciler arasındaki
   derleme sırası bağımlılıktan gelir. "before" doğrudur, kalır (hariciler ayrı `External` katmanında, en üstte).
   Uygulamadaki metin bugün: "Everything found under a card joins the same project list and graph, grouped at
   the top and built before the repository's own projects." — istersen onu al, istersen daha iyisini yaz.

**Paket standardı (önceki sürümlerle aynı):** sürüm geçmişi tablosuna satır; `## v1.16.0` bölümünde karar,
gerekçe, **denenen ve bırakılan alternatifler**, değişen dosyalar; §2.4 / §2.5 / §3.1 / §2.9 ilgili paragrafları
**yerinde** yeniden yazılır (changelog biriktirilmez); DS token'larına dokunulmaz, gerekiyorsa gerekçesiyle
kayıtlı sapma; reduced-motion ve erişilebilirlik adları; **"Gerçek uygulama notu"** bölümünde her yeni UI
öğesinin yukarıdaki hangi motor olgusundan beslendiğini tek tek yaz — motor tarafı bu eşlemeyi birebir
bağlayacak. Metinler İngilizce, README Türkçe.

**Dokunma:** Settings'teki External projects bölümü ve Pull before build switch'i (v1.14.0/v1.15.0) olduğu
gibi kalır; harici projelerin listede en üstte ayrı `External` grubunda durması ve önce derlenmesi değişmez;
satır ikon butonları, satır menüsü, koreografiler değişmez.
