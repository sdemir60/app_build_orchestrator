# Cycle güveni (Bölüm 1) — gerçek OSYS ölçümü

Plan: `.claude/outputs/2026-10-09-00-40-cycle-trust-and-surface-gate-plan.md` (Bölüm 1 Task 4 Step 4, karar D12).
Branch: `feat/cycle-trust`, ölçülen motor `72a7b0cd` (Release Supervisor). Final review'ın düzeltmesi yalnız App'in
"stuck" işaretine dokunur; motorun kararlarını değiştirmez, ölçüm geçerli kalır.

## Ortam

| | |
|---|---|
| Makine | Intel Core Ultra 7 258V (4P + 4LPE, 8 thread), 32 GB |
| OSYS | `D:\Projects\Delta\OSYS`, branch `feature/work-order-line-price-rules`, HEAD `b239d72f5`; ölçüm sırasında kullanıcının commit'i `77b254f84` (10:54, Types/Utils dosyaları — M4'e denk geldi) |
| Açık uygulama | Visual Studio açık (kullanıcı eşzamanlı çalışıyordu); `OSYS.UI.Container` ve Build Orchestrator kapalı |
| Supervisor | Release, arayüzsüz (`.claude/temp/cycle-resolve-perf-2026-10-02/harness.py`) |
| Ayar | Balanced profil, paralellik 1 (taban koşuyla aynı; yalnız M0 yerleşmesi 4), `ui-state.json`'daki katman desenleri ve harici kökler (`updateExternals=false`) |
| Defter | Yalıtılmış (`.claude/temp/cycle-trust-measure-2026-10-09/state`), kullanıcının 2026-10-08 14:16 defterinden tohum — kullanıcının defterine yazılmadı |
| "VS build" | VS'nin kendi MSBuild'i (`VS 18 Enterprise\MSBuild\Current\Bin\MSBuild.exe`), araç dışında: proje build'i = `OSYS.UI.Service.WorkOrder.csproj`, solution build'i = `Service.WorkOrder.sln` |
| Gövde değişikliği | `OSYS.UI.Service.WorkOrder`'da bir dosyanın sonuna tek yorum satırı (`touchsrc.py`: bayt + mtime yedeği; her yolda geri yüklendi, sonda git status temiz). API yüzeyi değişmez |
| Kopya kilidi | Container yerine paylaşılan kopyaya (`C:\OSYS\Client\Bin\OSYS.UI.Service.WorkOrder.dll`) okumaya açık, yazmaya kapalı tanıtıcı (`lock.ps1`) — post-build `copy /y` exit 1 (MSB3073) ile düşer |

Yeniden üretim: `python .claude/temp/cycle-trust-measure-2026-10-09/measure.py all` (M0-M5) ve `… copylock` (M6);
özetler her koşunun klasöründe `summary.json`.

## Taban (eski kurallar, kullanıcının 2026-10-08 koşuları)

- **VS'nin derlediği üye:** `run-20261008-141022-528` (Build, paralellik 1) — UI grubunun 16 üyesi
  `output built outside this tool`, 1'i `own inputs changed`: 17/17 derlendi; grup turu 234,0 sn, koşu 358,1 sn.
- **Kopya kilidi:** `run-20261008-122817-784` (Rebuild, paralellik 4) — `OSYS.UI.Service.Common` exit 1 ⇒ tur 1'de
  NoProgress, 17 kaydın hepsi geçersizlendi; takip koşusu `run-20261008-123754-718` (Build) tur 1'de 17 üyeyi
  derledi (16 `no trusted record`), tur 152,8 sn.

## Sonuçlar

### (a) VS'de gövde değişikliği + VS build → araçta Build (UI grubu, 17 üye)

| | Taban (eski kural) | Proje build'i (M1) | Solution build'i (M2) |
|---|---|---|---|
| Tur 1'de derlenen | 17 (16 `output built outside this tool` + 1 `own inputs changed`) | 1 (`own inputs changed`) | 1 (`own inputs changed`) |
| Taşınan (`skipped — up to date (carried …)`) | 0 | 16 | 16 |
| Grup | converged, 17 compiled; tur 234,0 sn | converged, 1 compiled; tur 0,8 sn | converged, 1 compiled; tur 0,8 sn |
| Koşu | 358,1 sn | 23,5 sn | 18,1 sn |

Grup dışında derlenen 20 proje (UI.Accounting.Report, UI.Workshop, UI.CRM, UI.NewSales.* …) grubun aşağı akışıdır:
grubun bileşik imzası değiştiği için Safe kipte "imza değişti" ile derlenirler — Bölüm 2'nin yüzey kapısının hedefi.

### (b) Kopya kilidi → NoProgress → takip Build'i (M6)

| | Eski kural | Yeni |
|---|---|---|
| Kilitli Build | patlayan üye NoProgress; 17 kaydın hepsi geçersizlenir | WorkOrder derlenir, kopyası düşer (exit 1); tur 1'de NoProgress (`stale=0`); 16 kardeş taşınmış ve oturmuş ⇒ güvenilir; koşu 29,6 sn |
| Takip Build'i (kilit kalktı) | 17 üye yeniden derlenir (tur 152,8 sn) | yalnız WorkOrder (`no trusted record`), 16 taşınan; converged (1 compiled), tur 1,1 sn; koşu 8,5 sn |

Geri yükleme Build'i (değişiklik geri alındı): 1 derleme + 16 taşınan, 35,6 sn. Değişiklik olmadan Build: 3,4 sn (dalın
üç repo kaynaklı hatası — `OSYS.Orchestration.{Accounting.Common, SparePart.Common, NewSales.Sales}`, CS hataları —
her koşuda yeniden denenir).

### Yan gözlemler

- **Okumayı da engelleyen kilit (M3, ilk deneme):** kilit `FileShare.None` ile tutulunca grup başı yüzey hash'i
  paylaşılan kopyayı okuyamadı (`evidence off`) ve referansı okuyan kardeşler de düştü. Grup kanıtsız tam turlara
  döndü (tur 1 ve 2'de 17'şer derleme, 248,7 sn), NoProgress, hiçbir yeşil güvenilmedi — Review Focus 3'ün (kanıtsız
  grupta D3 devreye girmez) gerçek OSYS karşılığı. Gerçek copy-lock (çalışan uygulamanın yüklediği DLL) okumaya
  açıktır; senaryo (b) bu yüzden M6'da okumaya açık kilitle yeniden ölçüldü.
- **M3 takip koşusu:** 17 üye `no trusted record` ile derlendi; `OSYS.UI.SparePart.Finance` yüzeyleri oturmuşken
  exit 1 ile düştü (neden ayrıca incelenmedi; kullanıcı aynı anda VS'de çalışıyordu) ⇒ tur 1'de NoProgress ve D3
  sayesinde diğer 16 yeşil üye güvenilir persist edildi.
- **M4 (geri yükleme, 394,9 sn, 110 başarılı)** kullanıcının 10:54 commit'ini (Types.Service.WorkOrder) ve M3'ün
  bıraktığı dep-issue notlarını da derledi; ölçülen senaryo değildir.
- M0 (yerleşme, paralellik 4): yalıtılmış kökte motor parmak izi farklı olduğu için (WPF temporary-assembly targets
  yolu) yedi grubun 33 üyesi `engine changed` ile bir kez derlendi; 236,9 sn, 143 başarılı.
