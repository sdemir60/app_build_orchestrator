# Yüzey kapısı (Bölüm 2) — gerçek OSYS ölçümü

Plan: `.claude/outputs/2026-10-09-00-40-cycle-trust-and-surface-gate-plan.md` (Bölüm 2 Task 6 Step 4).
Branch: `feat/surface-gate`. İlk tur `da46547d` motoruyla koşuldu (P0, G1-G5, T1-T3). Final review'ın düzeltmesi (I1)
kapının kararını değiştirdiği için asıl senaryo (Types) son kodla, `294fb1ce` ile tekrarlandı (T1f-T3f).

## Ortam

| | |
|---|---|
| Makine | Intel Core Ultra 7 258V (4P + 4LPE, 8 thread), 32 GB |
| OSYS | `D:\Projects\Delta\OSYS`, branch `feature/work-order-line-price-rules`, HEAD `966d0e8b9`. Kullanıcı Bölüm 1 ölçümünden sonra iki commit attı (`cc5e1cb30` 11:48, `966d0e8b9` 12:00); bunları P0 derledi |
| Açık uygulama | Bütün ölçümlerde Visual Studio, `OSYS.UI.Container` ve Build Orchestrator kapalıydı |
| Supervisor | Release, arayüzsüz (`.claude/temp/cycle-resolve-perf-2026-10-02/harness.py`). Son kod için Release yeniden derlendi |
| Ayar | Balanced profil, paralellik 1 (yalnız P0 yerleşmesi 4), Safe kip, `ui-state.json`'daki katman desenleri ve harici kökler |
| Defter | Yalıtılmış (`.claude/temp/cycle-trust-measure-2026-10-09/state`), Bölüm 1 ölçümünün defterinin devamı. Kullanıcının defterine yazılmadı |
| Gövde değişikliği | `touchsrc2.py`: dosyanın sonuna yorum satırı (`mark`, API değişmez) ya da public bir tip (`markapi`, API değişir). `revert` orijinal baytları yeni mtime ile geri koyar; `final` bayt ile mtime'ı birlikte geri koyar. Script, dosya kendi yazdığından farklıysa yazmayı reddeder. Her koşudan sonra OSYS git status temizdi |

Yeniden üretim: `python .claude/temp/cycle-trust-measure-2026-10-09/measure2.py all` (P0, G1-G5), `measure3.py all`
(T1-T3), `measure4.py all` (T1f-T3f). Her koşunun özeti kendi klasöründeki `summary.json` dosyasındadır; tabloyu `table.py <etiket>…` basar.

## Senaryolar

- **Types (asıl ölçüm):** `OSYS.Types.General` içindeki `General\WebServiceLogContract.cs` dosyasına gövde değişikliği.
  Bu proje 88 projenin doğrudan bağımlılığıdır (41'i UI) ve 2 üyeli `OSYS.Types.Accounting.Common` grubunun
  üyesidir. Planın hedeflediği pahalı akış budur (Goal/A.1, "Business/Types").
- **Business:** `OSYS.Business.Service.WorkOrder` içindeki `ApprovalRequest\WorkOrderApproval.cs` dosyasına gövde
  değişikliği (G1, G2), sonra API değişikliği (G3) ve geri alınması (G4). Kullanıcının günlük iş emri akışıdır, aşağı
  akışı küçüktür.

## Sonuçlar

### (a) Types — gövde değişikliği

| Koşu | Plan (kirli) | Derlenen | Kapı atlaması | Taşınan üye | Patlayan (repo) | Koşu |
|---|---|---|---|---|---|---|
| T1: ilk dokunuş, geçiş (`da46547d`) | 143 | 34 | 79 | 27 | 3 | 65,4 sn |
| T2: ikinci gövde değişikliği (`da46547d`) | 143 | 7 | 102 | 31 | 3 | 22,7 sn |
| T1f: son kod (`294fb1ce`) | 143 | 7 | 102 | 31 | 3 | 18,9 sn |
| T2f: son kod | 143 | 7 | 102 | 31 | 3 | 19,0 sn |
| T3 / T3f: değişiklik yok | 4 | 0 | 0 | 0 | 3 | 3,3 / 3,6 sn |

Sütunların anlamı:

- **Kapı atlaması:** `skipped — up to date (no dependency surface changed)`.
- **Taşınan üye:** cycle grubunun tur 1'de `up to date (carried)` atladığı üye.
- **Koşu:** motorun ölçtüğü koşu süresi.
- **Satır toplamı:** derlenen + patlayan + kapı atlaması + taşınan = plan.

**Kapı olmasaydı** (Bölüm 1 kuralı: kapı yok, üye terimi grup dışı upstream'i de taşır) bu 143 projenin hepsi
derlenirdi. Kayıtlı son derleme sürelerinin toplamı 520 sn eder; paralellik 1'de bu yaklaşık 9 dakikadır. Bu kaba bir
üst tahmindir: sürelerin çoğu P0'ın paralellik 4'lü, çekişmeli koşusundan gelir.

**T2'deki 7 derlemenin nedeni:**

| Proje | Neden |
|---|---|
| `Types.General` | Kendi değişikliği. Grubu `Types.Accounting.Common`; tur 1'de `own inputs changed`, kardeşi taşındı |
| `Business.PRM`, `Orchestration.PRM`, `UI.PRM` | Doğrudan bağımlılıkları olan `Types.PRM` SDK-style bir proje. SDK-style projede kanıt yolu türetilmediği için yüzey okunamaz ve kapı derler (§8.3, §20) |
| `UI.Shared` | `HintPath` ile `Types.PRM`'e bağlı (aynı neden) |
| `UI.General` | `UI.DMS` grubunun (17 üye) üyesi. Tur 1'de `dependency surface moved: …OSYS.Types.PRM.csproj` (kural i-b: kanıtsız üretici "oynadı" sayılır). 16 kardeşi taşındı |
| `Orchestration.SparePart.Finance` | Bağımlılığı `Orchestration.SparePart.Common` her koşuda patlıyor, bu yüzden kapı derler |

Yani asıl derleme 1. Kalan 6'nın 5'i, OSYS'teki 4 SDK-style PRM projesinin (`Types/Business/Orchestration/UI.PRM`)
yüzeyinin okunamamasından geliyor.

**Cycle grupları (T2):**

| Grup | Sonuç |
|---|---|
| `Types.Accounting.Common` | 1 derleme + 1 taşınan |
| `Types.General.Common` | 4/4 taşındı. Grup dışı upstream'leri `Types.General` yalnız gövdeden değişti (D7-b) |
| `Types.Service.Common` | 4/4 taşındı |
| `Business.Accounting.Common`, `Business.NewSales`, `Business.NewSales.Sales` | 2/2'şer taşındı |
| `UI.DMS` | 16/17 taşındı |

**Geçiş (T1, bir kez):** T1, Bölüm 2 motorunun bu projelere ilk dokunuşudur. 34 derlemenin kaynakları şunlar:

- **Bağımlılık yüzeyi olmayan kayıtlar (D6):** Kaydında henüz bağımlılık yüzeyi olmayan bağımlılar bir kez derlendi ve
  yüzeylerini yazdı. Bu kayıtları önceki motor yazmıştı ve P0 bu projelere dokunmamıştı.
- **Üye teriminin yeni biçimi (D7-b):** `Types.General.Common` grubunun 4 üyesi bir kez `own inputs changed` ile
  derlendi. Aynı geçiş P0'da kirli grupların 27 üyesinde görüldü.

Sonraki her gövde değişikliği T2 gibidir.

**Son kod (I1 düzeltmesi):** T1f/T2f sayıları T2 ile birebir aynı. Ölçülen hiçbir adayın son sonucu başarısız değildi,
bu yüzden düzeltme bu senaryoda karar değiştirmedi. Süre farkı (22,7 → 19,0 sn) aynı işin makine gürültüsüdür.

### (b) Business — gövde ve API değişikliği

| Koşu | Plan (kirli) | Derlenen | Kapı atlaması | Patlayan (repo) | Koşu |
|---|---|---|---|---|---|
| P0: yerleşme (paralellik 4) | 113 | 110 | 0 | 3 | 204,9 sn |
| G1: gövde değişikliği | 6 | 2 | 1 | 3 | 8,5 sn |
| G2: ikinci gövde değişikliği | 6 | 2 | 1 | 3 | 8,7 sn |
| G3: API değişikliği (public tip eklendi) | 6 | 3 | 0 | 3 | 10,2 sn |
| G4: API geri alındı | 6 | 3 | 0 | 3 | 10,5 sn |
| G5: değişiklik yok | 4 | 0 | 0 | 3 | 4,0 sn |

- **P0:** Kullanıcının iki commit'ini ve Bölüm 2 motorunun geçişini derledi. Kirli grupların 27 üyesinin hepsi tur
  1'de `own inputs changed` ile derlendi; üye teriminin biçimi değiştiği için (D7-b) bu bir kez olur. Derlenen
  projelerin bağımlılık yüzeyleri ilk kez yazıldı.
- **G1/G2:** `Orchestration.Service.WorkOrder` kapıdan atlandı. Derlenen ikinci proje `Orchestration.SparePart.Finance`'tir
  (patlayan bağımlılık, yukarıdaki gibi). Bu senaryonun aşağı akışı tek projedir, kazanç 1,4-1,5 sn'lik bir derlemedir.
- **G3/G4 (under-build denetimi):** API yüzeyi değişince okuyan proje (`Orchestration.Service.WorkOrder`) derlendi ve
  kapı atlaması olmadı. Geri almada da yüzey eski hâline döndüğü için yeniden derlendi. Kapı, yüzeyi oynayan bağımlılığın
  okuyucusunu atlamıyor.

### Yan gözlemler

- **Her koşuda patlayan 3 proje:** `OSYS.Orchestration.{NewSales.Sales, SparePart.Common, Accounting.Common}`. Bunlar
  dalın repo kaynaklı CS hatalarıdır (Bölüm 1 raporundaki aynı üç proje) ve her koşuda yeniden denenir (proje başına
  0,6-1,6 sn). Değişiklik olmayan koşunun planındaki 4 kirli projenin 3'ü bunlardır.
- **SDK-style kör noktası:** Kapı, kanıt yolu türetilemeyen bir bağımlılığın bağımlısını hiç atlayamaz (bilinen sınır,
  §20). OSYS'te bunun bedeli Types akışında 5 derleme, 11-13,5 sn (T2f / T2). Kanıt yolunu SDK-style için türetmek
  (`bin\<Configuration>\<TFM>\`) ayrı bir karardır; §7.6 "yaklaşım yok" der.

## Sonuç

- **Types gövde değişikliği:** Plan 143 projeyi kirli görüyor ve 7'si derleniyor: 1 asıl değişiklik, SDK-style PRM
  ailesinden 5, patlayan bağımlılık yüzünden 1. 102 proje kapıdan atlanıyor, 31 cycle üyesi taşınıyor. Koşu 19-23 sn;
  kapısız Bölüm 1 kuralının kayıtlı sürelerle kaba tahmini 520 sn.
- **Geçiş:** Bir kez, ilk dokunulan koşuda olur (T1: 34 derleme, 65 sn).
- **API değişikliği:** Okuyan proje derlenir (G3/G4), yani under-build yok. Değişiklik yokken Build 3,3-4,0 sn sürer.
