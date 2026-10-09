# SDK-style çıktı yolu — gerçek OSYS ölçümü

Plan: `.claude/outputs/2026-10-09-17-06-sdk-output-layout-and-honest-skip-plan.md` (Task 3 Step 2). Branch
`feat/sdk-output-layout`, motor `db21a0b4` (SDK-style projeye SDK varsayılan çıktı yolu, evaluation cache şeması 2; üstünde
`92eaa0ea` — atılan taşınan üye `skipped` raporlanır). Karşılaştırma tabanı: `.claude/outputs/2026-10-09-12-42-surface-gate-measurement.md` (T2f).

## Ortam

| | |
|---|---|
| Makine | Intel Core Ultra 7 258V (4P + 4LPE, 8 thread), 32 GB |
| OSYS | `D:\Projects\Delta\OSYS`, branch `feature/work-order-line-price-rules`, HEAD `7c3db1683`. Önceki ölçümden (12:41) bu yana kullanıcı 12 commit attı; bunları s0a eski kodla derledi. Ölçüm boyunca HEAD değişmedi |
| Açık uygulama | Visual Studio, `OSYS.UI.Container` ve Build Orchestrator kapalıydı (betiğin ön kontrolü) |
| Supervisor | Release, arayüzsüz (`.claude/temp/cycle-resolve-perf-2026-10-02/harness.py`). s0a/s0: develop `5e619897`'nin Release Supervisor'ı (eski kural: SDK-style proje kanıtsız). s1-s3: branch'in Release Supervisor'ı |
| Ayar | Balanced profil, paralellik 1, Safe kip, `ui-state.json`'daki katman desenleri ve harici kökler |
| Defter | Yalıtılmış (`.claude/temp/cycle-trust-measure-2026-10-09/state`), önceki ölçümlerin devamı. Kullanıcının defterine yazılmadı |
| Gövde değişikliği | `touchsrc2.py`, `OSYS.Types.General\General\WebServiceLogContract.cs`: sonuna yorum satırı (`mark`), sonra orijinal baytlar yeni mtime ile (`revert`), en sonda bayt ve mtime birlikte (`final`). Ölçüm sonunda OSYS git status temizdi |

Yeniden üretim: `PYTHONIOENCODING=utf-8 python .claude/temp/cycle-trust-measure-2026-10-09/measure5.py all` (konsol
cp1254'tü ve özet satırındaki bir karakteri basamıyordu; ilk deneme s0'dan sonra bu yüzden durdu — kaynağa dokunulmamıştı).
Tablo: `table.py s0-settle-develop-catchup s0-settle-develop s1-types-body-change s2-types-body-change s3-no-op-build`.

## Sonuçlar

| Koşu | Plan (kirli) | Derlenen | Kapı atlaması | Taşınan üye | Patlayan (repo) | Koşu |
|---|---|---|---|---|---|---|
| s0a: yetişme — 12 OSYS commit'i (eski kod) | 113 | 37 | 65 | 8 | 3 | 129,4 sn |
| s0: yerleşme kontrolü (eski kod) | 4 | 0 | 0 | 0 | 3 | 2,6 sn |
| s1: ilk dokunuş, geçiş (yeni kod) | 143 | 7 | 102 | 31 | 3 | 18,1 sn |
| **s2: ikinci gövde değişikliği (yeni kod)** | 143 | **2** | 106 | 32 | 3 | **7,7 sn** |
| s3: değişiklik yok | 4 | 0 | 0 | 0 | 3 | 2,7 sn |
| *T2f (önceki ölçüm, SDK-style kanıtsız)* | *143* | *7* | *102* | *31* | *3* | *19,0 sn* |

Sütunlar önceki raporla aynıdır. Satır toplamı: derlenen + kapı atlaması + taşınan + patlayan = plan (s2: 2 + 106 + 32 + 3 = 143).
Patlayan 3 proje her koşuda aynıdır: `OSYS.Orchestration.{NewSales.Sales, SparePart.Common, Accounting.Common}` — dalın repo
kaynaklı derleme hataları.

**s2'de derlenen 2 proje:**

| Proje | Neden |
|---|---|
| `Types.General` | Kendi değişikliği; grubu `Types.Accounting.Common` tur 1'de yalnız onu derledi, kardeşi taşındı |
| `Orchestration.SparePart.Finance` | Bağımlılığı `Orchestration.SparePart.Common` her koşuda patlıyor, kapı derler |

**T2f'deki 5 PRM kaynaklı derlemenin yerine:** `Business.PRM`, `Orchestration.PRM`, `UI.PRM` ve `UI.Shared` kapıdan atlandı
(`no dependency surface changed`); `UI.General`, `UI.DMS` grubunda taşındı — grup `converged (17 members, 0 compiled)`, yani
17/17 taşındı. s2'nin `decision.log`'unda hiç `dependency surface moved` satırı yok. `Types.PRM`'in kendisi her iki koşuda da
kapıdan atlandı: `Types.General`'daki gövde değişikliği onun okuduğu yüzeyi oynatmadı.

**Geçiş (s1, bir kez — karar A5):** s1, SDK-style projelere kanıt yolu türeten motorun ilk dokunuşudur. Evaluation cache şeması
2'ye çıktığı için girdiler bir kez yeniden değerlendirildi. PRM üreticilerinin bağımlılarının kayıtlarında PRM yüzeyi yoktu
(üretici kanıtsızdı); bu yüzden `Business.PRM`, `Orchestration.PRM`, `UI.PRM`, `UI.Shared` bir kez derlendi ve yüzeylerini yazdı,
`UI.General` da tur 1'de `dependency surface moved: …\OSYS.Types.PRM\bin\Debug\net46\OSYS.Types.PRM.dll` ile derlendi — neden
satırı artık türetilen gerçek dosyayı adlandırıyor. s1'in derleme sayısı (7) T2f'ninkiyle aynı ama içeriği farklıdır: T2f'de bu
5 proje her gövde değişikliğinde derleniyordu, s1'de bir kez derlenip yüzeylerini yazdılar.

**Plan beklentisinden sapma:** plan s2 için "kapı atlaması ≥ 107" diyordu; ölçülen 106. Fark `UI.General`'dır: o bir döngü
üyesidir, kapıdan değil grubu içinde taşınarak atlanır (taşınan 31 → 32). Toplam beklenen biçimde tutuyor. Süre beklentisi
(≈ 10-12 sn) aşağıda kaldı: 7,7 sn.

## Sonuç

- **Types gövde değişikliği:** derleme 7 → 2, koşu 19,0 → 7,7 sn. Kalan iki derlemenin biri asıl değişiklik, diğeri repodaki
  patlayan bir bağımlılığın sonucu. 4 SDK-style PRM projesinin kör noktası kapandı: bağımlıları kapıdan atlanıyor, `UI.DMS`
  grubu 17/17 taşınıyor.
- **Geçiş:** yükseltmeden sonraki ilk Build'de bir kez (s1: 7 derleme, 18,1 sn).
- **Değişiklik yokken** Build 2,7 sn sürüyor.
