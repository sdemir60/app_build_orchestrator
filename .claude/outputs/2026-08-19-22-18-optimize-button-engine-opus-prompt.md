# Opus Prompt — Optimize Butonuna Motor Takma

> Aşağıdaki metni olduğu gibi Opus'a yapıştır (derlemenin çalıştığı makinede, repo kökünde).

---

Optimize butonuna motor takacağız. Detaylı uygulama planı şurada:
`.claude/outputs/2026-08-19-22-18-optimize-button-engine-plan.md` — önce bu planı OKU, sonra uygula.

## Önemli: plan başka makinede, koddan bağımsız bir oturumda hazırlandı

- Plandaki **satır numaraları YAKLAŞIKTIR** ve kod bu arada değişmiş olabilir. Dosya adları, kalıplar ve
  kararlar (K-1…K-14) bağlayıcıdır; her task'a başlamadan önce ilgili dosyaları güncel haliyle oku ve
  konumları tazele.
- Plan ile güncel kod arasında gerçek bir çelişki bulursan (anılan üye/kalıp yok, davranış değişmiş)
  sessizce kendi kararını verme: durumu söyle ve bana sor. Küçük konum/isim kaymalarını sormadan güncel
  koda uyarla.
- **Clean planı (`2026-08-19-17-33-clean-button-engine-plan.md`) paralel yürüyor** — merge edilmiş de
  olabilir, edilmemiş de. İlk işin: Clean'in izlerini kontrol et (`CleanWorkspaceService`, `IsRunActive`,
  `ClearConsoleBuffers`, `RemoveUnderRoot`, MaintenanceBox foreach'inin hali). Planın "Clean planıyla
  paralel yürütme koordinasyonu" tablosu ikinci gelenin ne yapacağını tek tek söylüyor — ortak primitifler
  (IsRunActive, ClearConsoleBuffers, ByteFormat, prefix-normalizasyon helper'ı, NotAvailableSuffix
  kaldırımı, doküman cümleleri) İKİ KEZ TANIMLANMAZ: varsa kullan, yoksa planın tarifiyle sen kur.
  Clean merge edilmişse karşılıklı dışlamayı da sen kurarsın (CanClean += !OptimizeBusy,
  CanOptimize += !CleanBusy + çift kapı testi).

## İşin özü

`MaintenanceBox`'taki `PART_Optimize` (gauge ikonu, bugün kalıcı disabled) gerçek bir `optimizeWorkspace`
IPC komutuna bağlanacak. Optimize bir **workspace doktoru**: tıklandığı anda tarar, düzeltebildiğini
düzeltir, düzeltemediğini isim isim raporlar:

1. **Eksik NuGet paketleri:** packages.config'li ve `\packages\` HintPath hedefi diskte olmayan projeler,
   mevcut kanıtlanmış per-proje `-t:restore -p:RestorePackagesConfig=true -p:SolutionDir=...` sözleşmesiyle
   restore edilir (MSBuild.exe child, inner job'da; nuget.exe YOK; sln-level restore YOK).
2. **Kırık referans teşhisi:** restore SONRASI hâlâ eksik HintPath hedefleri (sürüm drift'i, eksik platform
   DLL'i) proje+dosya adıyla warn listelenir (detay 30 satırla sınırlı, sayaç tam sayıyı taşır).
3. **Stale obj artıkları:** StaleObjDetector'ın stale dediği LEGACY projelerde `obj\project.assets.json` +
   `*.nuget.g.props` + `*.nuget.g.targets` silinir. **SDK-style projede ASLA silinmez** (restore'suz
   silmek build'i kırar — K-7, bloklayıcı kural). Koşu başındaki tespit salt-teşhis KALIR.
4. **Cache hijyeni:** build-state.json + evaluation-cache.json'da RootPath altındaki, csproj'u artık
   var olmayan girdiler budanır; cacheRoot'taki 1 saatten eski `.tmp` artıkları süpürülür.

- Tıklar tıklamaz çalışır, **onay dialogu yok**. Console sıfırlanır, ilerleme satır satır akar, stream'e
  tek özet düşer. Görsel tasarım/animasyon kapsam DIŞI — yalnız işlevsel akış.
- **Dokunulmayanlar:** global NuGet cache'leri, NuGet.config, git (hiçbir git komutu koşmaz), worktree
  havuzu + `_obj`, bin/OutDir, run logları. Log yaşlandırma v1'de YOK (bilinçli).
- MSBuild resolve edilemezse Optimize düşmez: restore adımı warn ile atlanır, kalan adımlar koşar.
- Run uçuşta `optimizeRejected` (App kapısı + Supervisor `IsRunActive` çift katman); kilitli dosya hata
  değil (warn + LockedFileCount, devam); restore exit≠0 hata değil (warn + FailedRestores, devam).
- Restore beklerken 30 sn'de bir heartbeat satırı (`still restoring ...`) — 90 sn watchdog'u yanlış alarm
  vermesin (K-13; bekleme dikişi enjekte edilebilir, testte gerçek bekleme yok).

## Proje kuralları (CLAUDE.md geçerli — özellikle şunlar)

- **Kırmızı test kuralı:** her task'ta önce davranışı pinleyen test KIRMIZI gösterilir, sonra
  implementasyon. Kırmızıyı gösteremiyorsan test yanlıştır.
- **Davranış değişince test yeniden yazılır:** `MaintenanceBoxTests`'teki "Optimize disabled + not
  available yet" pinleri sessizce silinmez/gevşetilmez — yeni davranışı pinleyecek şekilde yeniden yazılır.
- Değişmezler: iş mantığı Core'da; stdout yalnız NDJSON; exception IPC sınırını geçmez; OutDir'e
  dokunulmaz; git salt-okur; nested job object (restore child'ı da inner job'da — MsBuildInvoker'ın mevcut
  çekirdeği); kopya YASAK (`\packages\` literal'i HintPathClassifier'da tek kaynak — public
  `IsNuGetPackagesPath` olarak açılır; restore child makinesi kopyalanmaz, `RunChildAsync` yeniden
  kullanılır; `.tmp` deseni store'ların kendi metodunda kalır).
- Kullanıcıya görünen tüm metinler (tooltip, console/stream satırları, hatalar) İNGİLİZCE.
- Yeni XAML kökü yok → realize testi gerekmez; eklersen gerekir.
- Doküman aynı işte güncellenir (plan T8: ARCHITECTURE §5.2/§5.3/§4.6/§9.3-9.4/§13.2/§16/§22 + README;
  anlatı üslubu, changelog dili yok, bayatlayacak rakam gömme — "ten commands execute" cümlesini dayanıklı
  ifadeyle değiştir).

## Çalışma şekli

1. İş branch'i aç (`feature/optimize-engine` gibi).
2. Task sırası: **T1 ‖ T2 ‖ T3 → T4 → T5 → T6 → T7 → T8** (plandaki bağımlılık şeması). Task başına commit.
3. Her task: önce kırmızı test(ler) → implementasyon → o task'ın süiti yeşil → commit.
4. Sonda tam süit:
   `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
   (uygulama açıkken build alma — çalışan Supervisor kendi binary'lerini kilitler).
5. main'e merge + push; merge doğrulandıktan sonra branch'i local + remote'tan sil; oturumu main'de bitir.

## Sapma noktaları (bana sormadan değiştirme)

- Düzeltilen/dokunulmayan küme (özellikle: global NuGet cache'lerine ve NuGet.config'e dokunmama; log
  yaşlandırmanın v1'de olmaması; git'e hiç dokunmama).
- SDK-style projede stale-obj SİLMEME kuralı (K-7 — build kırma gerekçeli).
- Needy tespitinin HintPath-varlık tabanlı olması, packages.config içeriğinin parse edilmemesi (K-1).
- Per-proje restore (sln-level değil) ve iptal komutunun olmaması (K-4, K-12).
- Onay dialogu eklememe kararı.
- `TryConsumeOptimizeFailure`'ın `OnError`'da `RunEndingErrorCodes` erken-dönüşünden ÖNCE çağrılması
  (sonra konursa hiç çalışmaz — plandaki tuzak notu).
- Tooltip metni planda önerildi; birebir metni iyileştirmek serbest ama kapsamı doğru anlatmalı —
  "rebuild the dependency index" ve "not available yet" ibareleri içermemeli.
