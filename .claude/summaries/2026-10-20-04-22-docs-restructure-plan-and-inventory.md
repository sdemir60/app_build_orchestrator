# Doküman yeniden yapılandırma — plan onaylandı, envanter taraması yarıda

**Tarih:** 2026-10-19 → 2026-10-20 04:22 · **Yer:** worktree `D:\Projects\Other\Apps\app_build_orchestrator-ai`, branch `docs/restructure` (develop e9c6a84c üstünde; henüz commit yok) · **Durum:** kullanıcı limiti bitti, ajanlar durduruldu.

## Karar verilen plan (kesin)

**Dil:** İngilizce esas, Türkçe aynası `.tr.md` ekiyle yanında; her dosyanın üstünde `English | Türkçe` satırı. Türkçe dosyada teknoloji adları İngilizce, açıklayıcı etiketler Türkçe. EN–TR madde başlıkları sayıca eşit; küçük bir guard testi pinleyecek (RepoHygieneTests kalıbı: `RepoPaths.RepoRoot`'tan dosya oku, iddia kur).

| Dosya | Ne iş yapar |
|---|---|
| `README.md` + `README.tr.md` | Vitrin ≤120 satır: paragraf, 3 görsel, 6-8 satır öne çıkanlar, hızlı başlangıç, doküman tablosu, lisans. Rozet satırı (`release.yml` badge `?branch=main`) korunur — RepoHygieneTests pinli. |
| `docs/FEATURES.md` + `.tr.md` | "Ne yapar" kataloğu; düz liste, önem sırası, 25-35 madde, eksiksiz (yüzler birleştirilir, atlanmaz). |
| `docs/WHY.md` + `.tr.md` | "Bize ne kattı"; düz liste, yalnız gerçek kazanımlar; hiçbir script'e/kişiye atıf yok. Girişte tek cümle çerçeve: "Yaygın pratik satırları böyle bir araç yokken çok projeli çözümün genelde nasıl derlendiğini anlatır; belirli bir ekibin kaydı değildir." |
| `docs/USER-GUIDE.md` | README'nin 531 satırlık "Using it" gövdesi + Performance modes + kısayollar + güncelleme taşınır. Yalnız EN. |
| `docs/images/` | 3 PNG: Sync sonrası pencere, Build sırasında (paralel + şerit + konsol), hata/graf. Ben çekerim (Release kopyası, PrintWindow, sabit boyut, %100 ölçek). |
| `ARCHITECTURE.md` §23, `CLAUDE.md` | Doküman haritası + tablo + dil kuralı ("README/ARCHITECTURE İngilizce" → "EN esas, TR ayna"). |
| `CHANGELOG.md` | Dokunulmaz. |

README'den çıkan Architecture / Build-test-run / Package-release / Known limits bölümleri yeni dosyaya gitmez: 3 komut + link kalır (ayrıntı ARCHITECTURE §18/§20'de zaten var).

**Madde kalıbı (ikisinde aynı iskelet, bölüm başlığı YOK, etiketler kalın):**

```md
### Paralel derler                                   ← FEATURES
**Ne yapar:** birbirine bağlı olmayan projeleri aynı anda derler.
**Nasıl:** her proje ayrı bir MSBuild.exe sürecinde koşar, referans verdiği her
şey yeşil olunca başlar; aynı anda kaç proje koşacağını perf profili belirler.
`bağımlılık grafına göre paralel zamanlama` · `proje başına ayrı MSBuild.exe süreci` · `vswhere ile MSBuild'i bulma`

### Paralel derler                                   ← WHY
**Amaç:** aynı işi daha kısa sürede bitirmek; boş duran çekirdekleri çalıştırmak.
**Yaygın pratik:** projeler elle tek tek ya da bir script'le sırayla derlenir;
biri bitmeden ötekine geçilmez, çekirdeklerin çoğu boş bekler.
**Build Orchestrator'da:** bağımsız projeler aynı anda derlenir; kaç tanesinin
koşacağını perf profili ve makinenin çekirdeği belirler, şeritte görürsün.
`bağımlılık grafına göre paralel zamanlama` · `perf profiliyle paralellik sınırı`
```

İngilizce etiketler: **What · How** ve **Goal · Common practice · In Build Orchestrator**. Etiket = teknoloji/mekanizma + ne için (çıplak `vswhere` değil, `vswhere ile MSBuild'i bulma`), madde başına 3-4. Aynı özellik iki dosyada aynı başlık ve aynı sıra; WHY yalnız kazanım olan maddeleri alır. Mekanizma sözcükleri FEATURES'ın Nasıl satırında; WHY'ın üçüncü satırı yalnız kullanıcının yaşadığı sonucu söyler. "Yaygın pratik" satırı geniş zaman, abartı yok; eski script'in (OSYS `rebuild.ps1`) satır satır bulguları yalnız bu `.claude/` kayıtlarında kalır, dokümana girmez.

**Adımlar:** 1 Özellik envanteri [orta] → 2 Kazanım envanteri [kisa] → KN1 (kullanıcı: sıra, eksik, ekibin dertleri) → 4 FEATURES EN+TR → 5 WHY EN+TR → 6 USER-GUIDE → 7 README EN+TR + görseller → KN2 → 9 Kapanış (CLAUDE.md, §23, guard testi, link kontrolü, tam süit; merge + push develop; branch sil; worktree develop-ai'ye döner).

## Bu oturumda yapılanlar

- Ana checkout'taki `.claude/outputs/2026-10-19-12-07-idle-memory-regression-check.md` develop'a commit (e9c6a84c) + push edildi.
- Worktree `develop-ai` → develop ff; `docs/restructure` branch'i açıldı. Henüz dosya değişikliği/commit yok.
- Pano adımları kayıtlı (sid eac24b12…): "Worktree + branch" completed, "Ozellik envanteri" in_progress, diğerleri pending.
- **Envanter taraması (adım 1) 7 alt ajana bölündü; 5'i bitti, ham çıktılar diskte** (`<worktree>\.claude\temp\`):
  - `inventory-arch-1.md` — ARCHITECTURE §1–§7, 88 girdi, 12 belirsiz
  - `inventory-arch-2.md` — §8–§12, 78 girdi, 11 belirsiz
  - `inventory-arch-3.md` — §13 UI, 85 girdi, 14 belirsiz
  - `inventory-arch-4.md` — §14–§23, 46 girdi + 55 bilinen sınır, 16 belirsiz
  - `inventory-readme.md` — README kullanım bölümleri, 73 girdi + 14 sınır, 15 belirsiz
  Girdi biçimi hepsinde aynı: `### Başlık` + Ne yapar / Nasıl / Etiketler / Yüzler / Görünür yer / Kaynak / Önem.
- **Durdurulan (hiç çıktı yazmadı, yeniden başlatılmalı):** (a) CHANGELOG + 134 first-parent merge mesajı taraması → `inventory-history.md`; (b) App kodu katalog taraması (Settings sekmeleri/anahtarları, menüler, kısayollar, balonlar, dialoglar; perf profili adları, Clean/Deep Clean, düzen modları, Open in VS seçicisi gibi belirsizlikleri koddan netleştirme) → `inventory-code.md`; (c) dört ARCHITECTURE envanterini tekrarsız tek listeye indiren birleştirme → `inventory-arch-merged.md` (eşleme tablosuyla, hiçbir ham girdi kaybolmasın).

## Ortak belirsizlikler (kod taramasında netleşecek)

Clean (Build menüsü/satır, `-t:Clean`) ile bakım kutusundaki Deep Clean adlandırması; perf profili adlarının tam listesi (Full/Balanced/Light?); "Open in Visual Studio"nun menüdeki yeri ve seçici açıp açmadığı; düzen modlarının (⊞ ≡ ▣) adları; Resolve cycles ve Restart engine'in yeri; harici kökler ayarının UI adı; Safe/Fast yayılım modunun UI adı; hotkey'ler yalnız `ui-state.json`'da (UI yok).

## Güncelleme 05:01 — tarama BİTTİ (Fable medium + Opus alt ajanlar)

Durdurulan üç ajan Opus ile yeniden koştu ve bitti: `inventory-history.md` (65 girdi, 16 sürüm), `inventory-code.md` (Settings/menü/kısayol/bildirim/dialog katalogları + belirsizlik cevapları), `inventory-arch-merged.md` (308 ham → 137 birleşik, 61 sınır, eşleme tam). Ardından nihai birleştirme:

**NİHAİ ENVANTER:** `.claude/outputs/2026-10-20-04-46-docs-restructure-inventory.md` — 35 ana madde önem sırasıyla (her birinde Ne yapar / Nasıl / Etiketler / Yüzler / Görünür yer / Kaynaklar / WHY taslağı), 22 WHY adayı, 14 küçük özellik, 66 bilinen sınır, kaldırılanlar (Resolve öncelik ayarı, Continue, worktree derleme, Alt+B/Ctrl+F5/Ctrl+F1, 3B konsol), 17 açık belirsizlik, 13 kullanıcı kararı, tam eşleme. Pano: adım 1-2 completed.

Koddan netleşenler: perf profilleri Full/Balanced/Light (Settings'te seçim yok; alt bar chip + şerit mod sözcüğü); UI'da "Deep Clean" düğmesi yok, iki ayrı "Clean" (Build/satır menüsü `-t:Clean`; bakım kutusu bin/obj + state sıfırlama + Sync, şerit etiketi DEEP CLEAN); Open in VS / Reveal in Explorer satır hover ikonları (çoklu .sln seçici); düzen modları Quad/List/Focus; Layers regex gruplama; branch chip / N behind / perf chip alt barda (README title bar diyor — düzeltilecek); Safe/Fast UI yok (hep Safe); hotkey'ler yalnız ui-state.json.

**Kullanıcı kararı bekleyen 13 nokta** envanterin §6'sında; en kritikleri: iki Clean'in adı; ARCHITECTURE §6.6 "Layers derleme sırasını yeniden dizer" ↔ kod "yalnız gruplar" (doküman-kod uyuşmazlığı, kullanıcı söyleyecek); *Pull before build* UI metni "git pull" ↔ ARCHITECTURE "fetch + ff-only"; WHY'da ölçüm rakamı ve OSYS/Delta adı geçsin mi; geliştirici yüzeyleri (CI, /release) FEATURES'a girsin mi.

## Devam reçetesi (yeni oturum, farklı model)

0. Kullanıcı envanteri okur (KN1): sıra, eksik, ekibin dertleri, 13 karar. Sonra adım 4 (FEATURES EN+TR) başlar.

## Eski devam reçetesi (tamamlandı)

1. Üç durdurulan ajanı aynı brief'lerle yeniden başlat (brief'ler bu oturumun geçmişinde; özü yukarıda). Eşzamanlı ajan 3-4'ü geçmesin.
2. Birleşik ARCHITECTURE listesi + readme + history + code → tek envanter: `.claude/outputs/<ts>-docs-restructure-inventory.md` (Türkçe): 25-35 ana madde önem sırasıyla, WHY adayları (Amaç/Yaygın pratik/Build Orchestrator'da taslağı), bilinen sınırlar, belirsizlikler. Editöryal seçim ben; kullanıcı KN1'de sırayı ve eksikleri verir.
3. Sonra adım 4'ten devam.
