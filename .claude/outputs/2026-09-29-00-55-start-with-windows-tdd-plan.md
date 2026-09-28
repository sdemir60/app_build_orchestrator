# Start with Windows · Start minimized to tray — TDD dökümü

- **Tarih:** 2026-09-29 · **Branch:** `feat/start-with-windows` (geçici worktree `app_build_orchestrator-start-with-windows`,
  taban `main` `6224eeb`)
- **Kaynak (spec):** kullanıcının P4 promptu — `.claude/outputs/2026-09-28-15-12-gap-decisions-and-prompts.md` §P4;
  bağlam `.claude/outputs/2026-09-28-12-33-ui-function-inventory.md` (§10 #62-#63, §13 #101, §14-4).
- **Kullanıcı kararları (bu oturum):**
  - Branch main'den açılır. Close-to-tray işi (`feat/close-to-tray-and-notifications`, P3) henüz main'de değil ve
    kalıcılık desenini (`ShellSwitches`) o kurdu. Bu branch aynı deseni **aynı ad ve biçimle** yalnız iki başlangıç
    anahtarı için kurar; Close to tray / Show notifications burada taslakta kalır (varsayılan açık kabul edilir).
    Birleşme noktaları `TODO(close-to-tray merge)` ile işaretlenir; merge'de iki tablo tek tabloya toplanır.
  - Görev Yöneticisi'nde devre dışı bırakılmış kayıt + Settings'te anahtar açılıp Save → **Seçenek 1**: Save,
    Görev Yöneticisi'nin "devre dışı" işaretini kaldırır; uygulama yeniden Windows ile açılır.

## Tasarım

1. **Tablo** (`App/Shell/ShellSwitches.cs`, P3'ün sözleşmesinin birebiri): `StartWithWindows` (`UiState.Autostart`,
   dosyada `startWithWindows`) ve `StartMinimizedToTray` (`UiState.StartMinimizedToTray`, dosyada
   `startMinimizedToTray`). `IsOn`, `Commit`, `Note` P3 ile aynı. `UiState.Autostart` → `bool?` (null token'ı yerleşimi
   silmesin); okuma yalnız `ShellSwitches` üzerinden.
2. **Gerçek durum** (`AutostartService.State`): Run değeri yok → `Off`; var ve StartupApproved\Run'da "devre dışı"
   (ilk bayt tek: Task Manager `03 + zaman` yazar, etkin `02`) → `DisabledInStartupApps`; aksi → `On`. Diyalog
   Start with Windows'u bundan tohumlar; devre dışıysa satır açıklaması Görev Yöneticisi notuna döner.
3. **Save** — Start with Windows YALNIZ diyaloğun açıldığı değerden farklıysa uygulanır: açık → Run değeri yazılır +
   StartupApproved işareti silinir (Seçenek 1); kapalı → Run değeri silinir. Anahtara dokunulmadıysa ne kayıt ne
   `UiState.Autostart` değişir (Görev Yöneticisi'nin kararı sessizce ezilmez). Kayıt yazılamazsa uygulama düşmez:
   konsola tek satır, tercih değişmez. Değişen anahtarın notu P3 biçiminde (`<Label> on — …`).
4. **Açılış yolu** (`StartupArgs.Decide(args, startMinimizedToTray)`, saf): `--autostart` = "Windows ile açıldım"
   işareti; gizli/görünür kararı `ui-state.json`'daki ayar. `--font-ab` her şeyi ezer; elle açılış pencereyi gösterir.
5. **Kablo:** `AutostartService` DI singleton'ı (App); açılış uzlaştırması ve MainWindow → SettingsDialog aynı
   örneği kullanır. `RegistryAutostartRegistry` yalnız `App.xaml.cs`'te kurulur (testler gerçek registry'ye ulaşamaz).
6. **Görev Yöneticisi adı:** exe'nin `FileDescription`'ı `BuildOrchestrator.App` idi → App csproj'da
   `<AssemblyTitle>$(Product)</AssemblyTitle>` ("Build Orchestrator", tek kaynak `Directory.Build.props`).

## Task'lar (her biri önce KIRMIZI)

| # | Task | Testler |
|---|---|---|
| 1 | Anahtar kalıcılığı | `StartWithWindowsTests`: varsayılan/kayıtlı, JSON + null token, taslak açılışı, Save notları, dosya round-trip, anahtarsız dosya; `SettingsGeneralPageTests` iki testi `[DEĞİŞEN KURAL]` ile yeniden yazılır |
| 2 | Kayıt Save anında | Save açık → Run değeri (tırnaklı exe + `--autostart`); kapalı → silinir; dokunulmadı → kayıt ve tercih aynı; yazım hatası → düşmez + satır |
| 3 | Task Manager durumu | `StartupApproval.IsDisabled` bayt tablosu; `State` üç hâl; devre dışıyken diyalog kapalı + not (realize); açıp Save → işaret silinir |
| 4 | Açılış yolu kararı | `StartupPathTests`: `--autostart` + ayar açık → tepsi, kapalı → pencere; kaynak guard'ı `Decide(e.Args, …)` |
| 5 | Görev Yöneticisi adı | exe `FileDescription` = `AppIdentity.Product` |
| 6 | Dokümanlar | ARCHITECTURE §12.1, §12.3, §13.3, §16, §20, §22; README General + State on disk |
