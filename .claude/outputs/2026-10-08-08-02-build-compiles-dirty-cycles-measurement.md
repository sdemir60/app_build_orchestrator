# Build kirli cycle gruplarını derler — gerçek OSYS ölçümü

Plan: `.claude/outputs/2026-10-08-05-16-build-compiles-dirty-cycles-plan.md` (Task 6 Adım 1b + Adım 2).
Branch: `feat/build-compiles-dirty-cycles`. Ölçüm kodu: `ab35f6ba` (final inceleme düzeltmelerinden önce; o
düzeltmeler yalnız önizlemenin `Conditional` bayrağını etkiler ve bu ölçümde bekleyen grup yoktu).

## Ortam

| | |
|---|---|
| Makine | Intel Core Ultra 7 258V (4P + 4LPE, 8 thread), 32 GB |
| OSYS | `D:\Projects\Delta\OSYS`, branch `developer`, HEAD `7dfe4924`, çalışma ağacı temiz |
| Açık uygulama | Yok — Visual Studio ve `OSYS.UI.Container.exe` kapalı (MSB3073 kilidi yok) |
| Supervisor | Release, arayüzsüz (`.claude/temp/cycle-resolve-perf-2026-10-02/harness.py`) |
| Ayar | Balanced profil, paralellik 4, cpu cap %70, `ui-state.json`'daki katman desenleri ve harici kökler (`updateExternals=false`) |
| Defter | Yalıtılmış (`.claude/temp/build-cycles-measure-2026-10-08/state`) — kullanıcının defterine yazılmadı |

Taban, kullanıcının 2026-10-07 koşularıdır: `run-20261007-095730-736` (Clean) →
`run-20261007-095819-438` (Resolve cycles) → `run-20261007-100205-622` (Build). Ayarlar aynı (paralellik 4,
Balanced, cpu cap %70).

## Sonuç: Clean → Build → Build

| Adım | Taban (2026-10-07) | Yeni (2026-10-08) |
|---|---|---|
| Clean (187 proje) | 38,5 sn | 20,1 sn |
| Resolve cycles | 197,4 sn — 83 proje (33 üye + 50 upstream) | — (gerekmiyor) |
| Build | 186,5 sn — 103 başarılı, 1 başarısız, 83 atlandı | 193,8 sn — 186 başarılı, 1 başarısız, 0 atlandı |
| Clean sonrası toplam derleme | **383,9 sn, iki koşu** | **193,8 sn, tek koşu** |
| Değişiklik olmadan Build | — | **1,2 sn** — 186 `up to date` (7 grup dahil), 1 başarısız |

Clean süresindeki fark bu değişiklikle ilgisizdir (Clean derlemez; makine yükü/önbellek farkı).

Toplam iş aynıdır (187 proje). Fark paketlemeden gelir: tabanda Resolve cycles koşusu 17 üyeli UI grubunu
derlerken makinenin geri kalanı boş bekliyordu; tek Build'de grubun turu diğer projelerle eşzamanlı koşar.

## Cycle grupları (yeni Build)

Yedi grubun hepsi tur 1'de yakınsadı (`stale=0`, `moved=none`); her üye bir kez derlendi.

| Grup (lider) | Üye | Seviye | Tur süresi | Yüzey hash |
|---|---|---|---|---|
| OSYS.Types.Accounting.Common | 2 | 2 | 4,7 sn | 0,4 sn |
| OSYS.Types.General.Common | 4 | 3 | 8,9 sn | 0,7 sn |
| OSYS.Types.Service.Common | 4 | 4 | 7,2 sn | 0,7 sn |
| OSYS.Business.Accounting.Common | 2 | 2 | 8,4 sn | 1,3 sn |
| OSYS.Business.NewSales | 2 | 2 | 6,4 sn | 1,5 sn |
| OSYS.Business.NewSales.Sales | 2 | 2 | 7,8 sn | 1,9 sn |
| OSYS.UI.DMS | 17 | 6 | 68,6 sn | 4,1 sn |

Tabanda aynı UI grubunun turu Resolve cycles koşusunda 113,4 sn sürmüştü (yüzey hash 14,1 sn; 09:59:43 → 10:01:37).

## Acceptance testleri (gerçek OSYS, `Category=Acceptance`)

Üçü de yeşil. Paralellik 6, profil varsayılan (karşılaştırma için değil, sözleşme için).

| Test | Sonuç |
|---|---|
| `Osys_full_rebuild_parallel_is_green_with_zero_orchestrator_caused_failures` | 177 proje, 159,2 sn; 176 başarılı, 1 başarısız (repo kaynaklı); 7 grup yakınsadı (3'ü iki turda); 185 `projectStarted` (8'i ikinci tur derlemesi); en fazla 6 eşzamanlı derleme; orchestrator kaynaklı hata 0; grup içi kenar hariç sıralama ihlali 0 |
| `Dispatch_order_is_deterministic_across_two_runs_for_the_first_projects` | İlk 6 dispatch kümesi iki koşuda aynı |
| `Osys_incremental_build_skips_all_up_to_date_then_minimal_rebuild_on_a_single_dirty_project` | Rebuild 157,5 sn; değişiklik olmadan Build 0,97 sn, 176 `up to date`; açıklanamayan yeniden derleme 0; tek proje değişince cycle dışı 23/23 ve cycle üyesi 17/17 transitif bağımlı "derlenecek" okundu |

## Repo kaynaklı tek hata

`OSYS.Orchestration.Accounting.Common` her koşuda `CS1061` ile düşüyor: `InvoiceDocumentModuleTypeEnum`
içinde `INOsys`, `LogoRequestModel` içinde `InvoiceDocumentNumber`/`EDocumentTypeCode` yok. Proje, kaynağıyla
uyuşmayan derlenmiş bir ETransformation tipine karşı derleniyor. Araçtan bağımsızdır; taban koşudaki tek hata da
budur.

## Kullanıcıya kalan

- Arayüzde gözle doğrulama: Clean → Build sırasında açılış dalgasının cycle üyelerini yakması, şeridin
  `Resolving cycles · round r/k` satırına geçip grup bitince `Building` satırına dönmesi, "N to build" sayısı.
- Plan Adım 2.4 (bir Types cycle üyesinde public üye değişikliği → grup derlenir, bağımlıları yeşil) yapılmadı:
  OSYS kaynağını değiştirmek gerekiyordu.
