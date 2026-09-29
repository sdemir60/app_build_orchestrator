# Close to tray · güvenli tam çıkış · Show notifications — sonuç raporu

- **Tarih:** 2026-09-29 · **Branch:** `feat/close-to-tray-and-notifications` (worktree `app_build_orchestrator-ai`)
- **Plan:** `.claude/outputs/2026-09-28-23-32-close-to-tray-and-notifications-tdd-plan.md`
- **Spec:** `.claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md` §4 P3

## Ne değişti (kullanıcının gördüğü)

| Konu | Eskiden | Şimdi |
|---|---|---|
| Close to tray | Yalnız taslak; × her zaman tepsiye | Kaydedilir. Açık: × tepsiye. Kapalı: × (Alt+F4, sistem menüsü Kapat da) uygulamayı kapatır |
| Tam çıkış (× kapalıyken, tepsi → Exit) | Tepsi Exit MSBuild'leri anında öldürüyordu | Derleme graceful durur, uçuştakiler (post-build copy dahil) biter, sonra çıkar. Sync/Clean/Optimize/checkout/pull bitmesi beklenir |
| Beklerken | — | Pencere görünür kalır (gizliyse ya da küçültülmüşse öne gelir), şerit `▸ Stopping — …`, konsola tek satır. İkinci × / Exit ikinci stop üretmez. Bekleyişte yeni Sync başlamaz (zincirli Sync dahil) |
| Motor yanıtsız / öldü | — | Beklenmez: sessizlik bekçisi (90 sn) ya da ölüm çıkışı serbest bırakır, Job Object kalanı kapatır |
| Windows oturum kapanışı | Anında çıkış | Aynen korunuyor |
| Show notifications | Yalnız taslak; balonlar hep çıkıyordu | Kaydedilir. Kapalıyken üç OS balonu da çıkmaz (koşu bitti, ilk ×, ikinci instance); ilk-× bayrağı harcanmaz; animasyonlu tepsi logosu etkilenmez |
| Kalıcılık | — | Save → ui-state.json; diyalog kayıtlı değerle açılır; Export/Import taşır; değişince konsola tek not |

## Yapı

- `App/Shell/ShellSwitches.cs` — dört kabuk anahtarının tek tablosu (main'deki P4 ile birleşti: Start with Windows,
  Start minimized to tray, Close to tray, Show notifications).
- `App/ViewModels/RunViewModel.Exit.cs` — çıkış bekleyişi (`RequestExit`, `ExitPending`, `ExitReady`); "uçuşta iş"
  sorusu mevcut `WorkspaceIdle`.
- `App/Shell/WindowCloseRule.cs` — × kararı (Close / Stay / HideToTray / RequestExit) ve öne getirme kuralı.
- Üç balon kapısı: `FirstCloseBalloonGate`, `TrayBuildIndicatorController`, `SecondInstanceGate`.

## Doğrulama

- Her davranış önce kırmızı (yeni testler ya da geri alınan mutasyonlar), sonra yeşil.
- Gerçek Supervisor + gerçek MSBuild testi (`SafeExitProcessTests`): çıkış sırasında derleme graceful durur,
  uçuştaki projeler başarıyla biter, çıkıştan sonra Supervisor/MSBuild process'i kalmaz. App process'inin kendisi
  `AppShutdownTests` (disposal bekleyişi kilitlenmez) ve `CloseToTrayTests` (ExitReady → kapanış) ile kapsanır.
- Tam süit (`Category!=Acceptance`): 3738 geçti, 0 başarısız, 6 ortam-kapılı atlandı. Build 0 uyarı.

## Kararlar (ruling'ler)

- Tepsi Exit pencereyi yalnız bekleyiş sürüyor ve çıkış henüz başlamadıysa öne getirir (koreografi anında anında
  biten bekleyişte pencere yanıp sönmesin).
- Bekleyişte hiçbir Sync başlamaz; Clean/Optimize/checkout/pull sonrası zincirli Sync atlanır (yalnız kapanan ekranı
  tazeliyordu; sonraki açılış zaten Sync'ler).
- Motor sessizliği: kullanıcının seçtiği mevcut bekçi korundu; sınır dokümana yazıldı (aşağıda).
- Gerçek uygulamada canlı deneme kullanıcının profiline dokunmamak için yapılmadı (aşağıdaki elle kontrol listesi).
- `SafeExitProcessTests` ortam değişkeni kapısı olmadan koşar (KillMidBuildTests gibi regresyon testi, ~4 sn).

## Bilinen sınırlar / kullanıcı kararı bekleyenler

- **Sessizlik bekçisi çıkışı kesebilir:** bekleyiş sırasında motor 90 sn hiç olay göndermezse (ör. uzun bir
  checkout/pull git yazımı ya da çıktı basmayan tek bir derleme adımı) çıkış o işi keser. Eskiden tepsi Exit her
  şeyi anında öldürüyordu; yani daha kötü değil. İstenirse takip işi: motorun bu işlerde "hâlâ çalışıyorum" olayı
  göndermesi.
- **Show notifications açıklaması dar:** UI metni yalnız "build bittiğinde bildirim" diyor, anahtar üç balonu da
  kapatıyor. Metni değiştirmek kullanıcı kararı.
- **Doküman/kod uyuşmazlığı (bu işten bağımsız, dokunulmadı):** ARCHITECTURE §21.2 hâlâ `tf` (TFVC) diyor; kodda tf
  çalıştırılmıyor.
- README'deki sessizlik bekçisi cümlesi koda göre düzeltildi (bekçi Sync/Clean/Optimize/checkout/pull'u da kapsıyor;
  ARCHITECTURE §4.6 zaten böyle diyordu).

## Elle kontrol listesi (canlı pencere — headless testin göremediği)

1. Close to tray kapalı + derleme sürerken × → şerit "Stopping", derleme bitince uygulama kapanır.
2. Pencere tepsideyken tepsi → Exit (derleme sürerken) → pencere öne gelir, iş bitince kapanır.
3. Pencere küçültülmüşken görev çubuğundan kapat (derleme sürerken) → pencere öne gelir.
4. Kapanıştan sonra Görev Yöneticisi'nde BuildOrchestrator / Supervisor / MSBuild kalmaz.

## Süreç notları

- Çalışma sırasında main'e paralel bir oturumdan Start with Windows (P4) girdi; aynı `ShellSwitches` sözleşmesiyle
  kurulmuştu. Branch'e main alındı, iki tablo tek tabloda birleşti; iki taraftaki "bu anahtar kaydedilmez"
  testleri yeni kurala göre yeniden yazıldı (`[DEĞİŞEN KURAL]`).
- Park edilen küçük notlar: paylaşılan `StartBuild` iki projelik eski koşu sayılarını anlatan iki test yorumunu
  bayatlattı (assert yok); öne getirme ertelemesindeki kural yeniden kontrolü yalnız kaynak guard'ıyla dolaylı
  pinli. İkisi de davranışı etkilemez.
