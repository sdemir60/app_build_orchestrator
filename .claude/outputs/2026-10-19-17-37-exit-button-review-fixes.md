# Exit butonu — inceleme düzeltmeleri (TDD dökümü)

Kaynak: `feat/exit-button` (326e30e3..57ab18b5) bağımsız inceleme turu. Kritik bulgu yok. Tek düzeltme turu; her madde
için önce kırmızı test, sonra düzeltme.

## Önemli

| # | Bulgu | Konum | Kırmızı test |
|---|---|---|---|
| Ö1 | × / tepsi → Exit'in ertelenen ana dalı (pencere öne → kart açılır) testsiz | `MainWindow.Exit.cs` `ConfirmExitInFrontNow` | Gösterme dikişi sahte; `ConfirmExitInFrontNow` + pompa → kart açık, `stopRun` yok |
| Ö2 | `ExitWork` meşguliyet listesini ikinci kez yazıyor; "None ⇔ WorkspaceIdle" korunmuyor | `RunViewModel.Exit.cs` `ExitWork` | Kaynak guard: `NonSyncWorkspaceBusy`'nin her üyesi `ExitWork`'te; `None` yalnız `WorkspaceIdle`'dan; eşlenmeyen iş sessiz "None" değil, yüksek sesle düşer |
| Ö3 | "Çıkış neyi bekler" iki yerde (`GitWriteBusy` VM'de, `ExitText.Waits` metin sınıfında) | `RunViewModel.Exit.cs`, `ExitText.cs` | Kural `ExitWork`'ün kendi dosyasında tek yerde; VM ve metin onu okur (davranış aynı, mevcut testler + kaynak guard) |

## Küçük

| # | Bulgu | Kırmızı test |
|---|---|---|
| K1 | Kart dialog açıkken kapanınca odak scrim altındaki butona gidiyor | Dialog açık + kart + Esc → odak butonda değil |
| K2 | Hard stop kararı `StopCommand.CanExecute`'tan okunuyor (gönderim sürerken false) | Graceful gönderim askıdayken çıkış → hard gider |
| K3 | Esc'in kullanıcıya görünen açıklaması kart katmanını anmıyor (katalog, README) | Katalog cümlesi kartı anar |
| K4 | Restart kipinde çubuk dolarken başlamış Sync'in kesilmesi testsiz | Restart + Sync uçuşta + çubuk doldu → kapanış Sync'i beklemez |
| K5 | Kart kapalıyken de tazeleniyor | BIRAKILDI — aşağıya bak |
| K6 | Bayat yorumlar (RibbonText, RunViewModel, Workspace, AppTrayIcon, GeneralSettings, Update, ClosingScreenTime) | — (yorum) |

## Organizasyon

| # | Bulgu | Doğrulama |
|---|---|---|
| O1 | `ProcessImage.NameOf` ile `MemoryLine.PrivateBytesOf` aynı "canlı süreci PID ile aç" iskeleti | Ortak yardımcı; mevcut testler yeşil |
| O2 | `JobProcessCensus` ile `JobResourceProbe` aynı job PID okuma iskeleti | Ortak okuyucu; sıfır ayırma pini yeşil |
| O3 | `"Closing " + Product` iki yerde (`ExitText`, `UpdateText`) | Biri diğerinden türer |
| O4 | Tepsi menüsünün `"Exit"` literali | `ExitText.ExitLabel` |
| O5 | Kart Esc zincirinde iki kez sayılıyor (`AnyPopoverOpen` + `exitCardOpen`) | Biri kalkar; Esc testleri yeşil |
| O6 | Test fixture kopyası (süreç listesi, çıkış dikişlerinin sahtesi üç rig'de) | Ortak yardımcı |

## Ek bulgu — tam süitte ortaya çıktı

| # | Bulgu | Kırmızı test |
|---|---|---|
| M1 | Motor yarışı: koordinatör projeyi "derleniyor" ilan edip slotu aldıktan SONRA `MSBuild.exe`'yi başlatır; hard stop o aralıkta gelirse inner job'u boşken sonlandırır, ardından başlayan derleme sonuna kadar koşar ("Stop now" ve "Stop and exit" bir projeyi kaçırır). Ölçüm: `SafeExitProcessTests` yük altında motorun cevabını 3003 ms'de aldı — fixture'ın ~3 sn'lik adımı kadar | `RunCoordinatorTests.A_hard_stop_cancels_the_invoke_in_flight`, `..._during_a_retry_backoff_reports_the_project_stopped` — düzeltmeden önce ikisi de zaman aşımı |

## Bırakılanlar (gerekçeli)

- Koşu modunun pill etiketinden okunması: tıklama anındaki modun tek kaynağı odur; motorun modu `runStarted`'a dek yoktur.
  Ayrı bir alan iki kaynak doğururdu.
- Süreç satırlarının code-behind'da kurulması: güncelleme kartının `ReleaseNoteBlocks` deseniyle aynı; `ProcessTally`
  internal bir kayıttır, XAML bağlaması public tip ister.
- K5 (kart kapalıyken tazelenmesin): gösterilmeyen pencerede popup'ın `IsOpen`'ı zorla false kalır; `IsOpen`'a bakan bir kapı
  kartın açık durumunu butondan okuyan kabuk testlerini körleştirirdi. Bedeli proje olayı başına birkaç özellik yazımı.
- CI'da `SafeExitProcessTests` 3 sn iddiası: düşerse eşik gevşetilmez, yeniden üretilip `LocalOnly`'ye alınır.
