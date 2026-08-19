# Opus Prompt — Clean Butonuna Motor Takma

> Aşağıdaki metni olduğu gibi Opus'a yapıştır (derlemenin çalıştığı makinede, repo kökünde).

---

Clean butonuna motor takacağız. Detaylı uygulama planı şurada:
`.claude/outputs/2026-08-19-17-33-clean-button-engine-plan.md` — önce bu planı OKU, sonra uygula.

## Önemli: plan başka makinede, koddan bağımsız bir oturumda hazırlandı

- Plandaki **satır numaraları YAKLAŞIKTIR** ve kod bu arada değişmiş olabilir. Dosya adları, kalıplar ve
  kararlar (K-1…K-10) bağlayıcıdır; her task'a başlamadan önce ilgili dosyaları güncel haliyle oku ve
  konumları tazele.
- Plan ile güncel kod ARASINDA gerçek bir çelişki bulursan (örn. anılan bir üye/kalıp artık yok, davranış
  değişmiş) sessizce kendi kararını verme: durumu söyle ve bana sor. Küçük konum/isim kaymalarını ise
  sormadan güncel koda uyarla.

## İşin özü

`MaintenanceBox`'taki `PART_Clean` (sync'in sağındaki bakım kutusu, silgi ikonu, bugün kalıcı disabled)
gerçek bir `cleanWorkspace` IPC komutuna bağlanacak:

- Tıklar tıklamaz çalışır, **onay dialogu yok**. Console sıfırlanır, ilerleme satır satır console'a akar,
  event stream'e tek bitiş özeti düşer. Görsel tasarım/animasyon bu işin kapsamı DIŞINDA — yalnız işlevsel
  akış.
- Silinen: aktif workspace'in keşfedilmiş projelerinin `bin\` ve `obj\` klasörleri + o workspace'in
  `build-state.json` kayıtları (workspace-scoped, RootPath prefix; sonraki Build her şeyi NeverBuilt olarak
  derler, tekrar Sync gerekmez).
- Silinmeyen: `packages\`, ortak OutDir, worktree havuzu (`_obj` dahil), run logları, `.tmp`'ler,
  `evaluation-cache.json`, `ui-state.json`. **`/t:Clean` çağrılmaz** (gerekçe planda K-1 — bilinçli karar,
  dokümana da işlenecek).
- Kilitli dosya hata değildir: dosya başına atla + warn, akış durmaz, sonda özet + `LockedFileCount`.
- Karşılıklı dışlama: Build/Sync sürerken Clean disable; Clean sürerken Build/Sync/Rebuild/Cycles disable.
  Supervisor tarafında da run aktifken `cleanRejected` reddi (çift katman).

## Proje kuralları (CLAUDE.md geçerli — özellikle şunlar)

- **Kırmızı test kuralı:** her task'ta önce kusuru/davranışı pinleyen test KIRMIZI gösterilir, sonra
  implementasyon. Kırmızıyı gösteremiyorsan test yanlıştır.
- **Davranış değişince test yeniden yazılır:** `MaintenanceBoxTests`'teki "Clean disabled + not available
  yet" pinleri sessizce silinmez/gevşetilmez — YENİ davranışı pinleyecek şekilde yeniden yazılır.
- Değişmezler: planlama/iş mantığı Core'da; stdout yalnız NDJSON; exception IPC sınırını geçmez; OutDir'e
  dokunulmaz; git salt-okur; kopya YASAK (console sıfırlama bloğu `ClearConsoleBuffers()` helper'ına
  çıkarılıp iki yerden çağrılır; metinler tek yerde).
- Kullanıcıya görünen tüm metinler (tooltip, console/stream satırları, hata mesajları) İNGİLİZCE.
- Yeni XAML kökü eklenmediği için realize testi gerekmez; eklersen gerekir.
- Doküman aynı işte güncellenir (plan T7: ARCHITECTURE §5.2/§5.3/bakım kutusu/§16 + README; anlatı üslubu,
  changelog dili yok, bayatlayacak rakam yok).

## Çalışma şekli

1. İş branch'i aç (`feature/clean-engine` gibi).
2. Task sırası: **T1 ‖ T2 → T3 → T4 → T5 → T6 → T7** (plandaki bağımlılık şeması). Task başına commit.
3. Her task: önce kırmızı test(ler) → implementasyon → o task'ın süiti yeşil → commit.
4. Sonda tam süit:
   `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"`
   (uygulama açıkken build alma — çalışan Supervisor kendi binary'lerini kilitler).
5. main'e merge + push; merge doğrulandıktan sonra branch'i local + remote'tan sil; oturumu main'de bitir.

## Sapma noktaları (bana sormadan değiştirme)

- Kapsam listesi (silinen/silinmeyen küme) ve `/t:Clean` kararı.
- "Önce state, sonra klasörler" silme sırası (K-9 — güvenlik gerekçeli).
- Onay dialogu eklememe kararı.
- Tooltip metni planda önerildi; birebir metni iyileştirmek serbest ama kapsamı doğru anlatmalı ve
  `/t:Clean` / `artifacts/` iddiaları içermemeli.
