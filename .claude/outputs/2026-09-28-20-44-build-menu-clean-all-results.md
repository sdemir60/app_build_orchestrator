# Build ▾ → Clean (tüm projeler) — sonuç

- **Tarih:** 2026-09-28 · **Branch:** `feat/build-menu-clean-all` (worktree `-ai`), taban `main` `35ddc41`
- **Plan:** `.claude/outputs/2026-09-28-19-48-build-menu-clean-all-tdd-plan.md`
- **Commit'ler:** `dea7e7d` motor · `4e0e740` app · `25a10bc` docs · `f14ff7e` review düzeltmeleri

## Ne değişti

**Motor (Core + Supervisor)**
- `Core/Planning/CleanRunScope.cs` (yeni): Clean'in bağımlılık anlamı yok. Plan kenarsız, döngüsüz
  (`InCycle=false`, `Cycles=[]`), `WillBuild=true` (önizleme = bu koşunun işi), gerekçe korunur, katman/üretici
  uyarıları düşer.
- `RunCoordinator.RunSegmentAsync`: `Mode==Clean` ise bu plan, `ProjectRunScope`'tan ÖNCE uygulanır.
- Sonuç: döngü üyesi "in dependency cycle" diye atlanmıyor, A6 kilitlenmesi yapısal olarak yok, sıra beklenmiyor,
  hatalı Clean dependent'a dep-issue yapıştırmıyor. Satır Clean'inin bayat bağımlılık uyarısı (▲) da kalktı.
- Harici çalışma kopyaları Clean'de güncellenmiyor — `ExternalUpdater.ShouldUpdate` zaten hariç tutuyordu (mevcut test).

**App**
- `RunViewModel.CleanAllCommand`: `StartRunCommand(Mode=Clean, ScopeProjectId=null)`; kapı `CanRebuildOrRetry`;
  Build'in 6 bildirim noktasının hepsinde.
- `BuildMenu`: Clean maddesi etkin, hover alır, tıklama komutu çalıştırır. Tooltip
  `Clean — msbuild /t:Clean on every project; caches are untouched` ("not available yet" ve "solution" kalktı).
  `NotAvailableSuffix` silindi; `DisabledOpacity` tek kullanıcısı `ProjectRowMenu`'ye taşındı.
- `ScopeFor(Clean)` = tüm satırlar → açılış dalgası hepsini işaretler; kuyruk rengi motor önizlemesinden (hepsi).
- Clean önizlemesi plan bayrağına yazmaz (`PreviewWritesPlanFlag`, Resolve kuralının genellemesi); bayrağı sonuç
  yazar: `NextPreview.AfterClean(inCycle)` = (`!inCycle`, NeverBuilt, false). Temizlenen döngü üyesi ve durdurulan
  Clean'in ulaşmadığı satır sonraki Build dalgasında yanmaz.
- Akış açılış satırı: `Clean started — N projects, parallelism P`.
- ETA: döngü kovası (tur bütçesi) yalnız Cycles koşusunda; Clean-all'da üyeler sıradan paralel iş.

**Doküman:** ARCHITECTURE §5.2 (OutDir gerekçesi yalnız bakım Clean'inin), §7.4, §8.1 (tablo + "Clean has no
dependency meaning"), §8.4, §9.4 (the tool never touches OutDir; `-t:Clean` MSBuild'in kaydettiği çıktıyı siler),
§13.2 Build menüsü, pill, etiket canlı geçişi, §22 kod haritası; README Build/Rebuild adımı; Contracts
`RunMode.Clean` ve `CleanWorkspaceCommand` dokümanları. `ReleaseNotes.cs`'e dokunulmadı.

## Testler (hepsi önce kırmızı gösterildi)

| Test | Dosya |
|---|---|
| Kenarsız/döngüsüz plan, WillBuild=true, gerekçe korunur, uyarılar düşer (3) | `Planning/CleanRunScopeTests.cs` |
| Grafın her projesi temizlenir (harici + döngü üyesi); defter kayıtları silinir; bağımlılık beklenmez; dep-issue yok; önizleme hepsini sayar (5) | `Supervisor/FullCleanRunTests.cs` |
| Satır Clean'i bayat bağımlılığı dep-issue yapmaz | `Supervisor/SingleProjectRunTests.cs` |
| Madde canlı + tooltip (eski pasif testi DEĞİŞEN KURAL ile yeniden yazıldı); tıklama kapsamsız Clean gönderir, pill CLEAN | `App/BuildMenuTests.cs` |
| Kapı + CanExecuteChanged Build ile aynı | `App/CleanAllCommandTests.cs` |
| Dalga tüm satırlar; Clean sonrası Build dalgası döngü üyesini yakmaz; durdurulan Clean dokunmadığı satırı bozmaz | `App/ChoreographyTests.cs` |
| "Clean started — N projects" | `App/EventStreamTests.cs` |
| `AfterClean` üçlüsü evaluator'la birebir (DEĞİŞEN KURAL) | `Planning/NextPreviewTests.cs` |
| Clean-all ETA'sı döngü üyesini tur bütçesiyle saymaz; çarpan testi Cycles'a taşındı (DEĞİŞEN KURAL) | `App/RunViewModelStateTests.cs` |

## Kod incelemesi (bağımsız inceleyici)

Kritik yok. Düzeltilenler: test yardımcısı kopyası (`LogTextsFor` → tek yer), ETA döngü kovası, doküman
kesinliği (§7.4, README, §9.4, ProjectRunScope notu), literal kopyası, adlandırılmış yüklemlerin yeniden kullanımı,
`Verb(RunMode)`, `ApplyNextPreview`. Bilerek bırakılanlar aşağıda.

## Süit

Dört tam koşu (`Category!=Acceptance`), hepsinde **işlevsel testlerin tamamı geçti**; her koşuda 1-3 **zamanlama
bütçesi** testi kaldı, her seferinde farklısı: `UiResponsivenessBudgetTests` (topoloji layout 125-135 ms / 120),
`PopoverTests` pop-in (BranchPopover animasyonu 56 ms < 60 alt sınır), `ListRealizationPerfTests`. Son koşular:
Debug 3626 geçti / 1 kaldı (UI bütçe), Release 3626 geçti / 1 kaldı (popover), 6 atlanan (ortam kapılı).

Neden değişiklikle ilgisiz: makine pildeydi (CPU kısılıyor, 2200 MHz sabit); kalan testler tek başına geçiyor ya da
kararsız; **aynı testler `main` (`35ddc41`) üzerinde de aynı koşulda kaldı** (UI bütçe 3'te 1, liste realize Debug
ve Release); ölçtükleri yollara (grafın ilk layout'u, BranchPopover, liste realize) bu iş dokunmuyor. Şarjda tekrar
koşulması önerilir.

## Açık kalanlar (kullanıcı kararı)

1. Clean-all döngü üyelerini de temizler; sonraki Build onları derlemez (Resolve cycles gerekir). Satırlarda
   `never built` + döngü üçgeni var, ama akışta ayrıca bir yönlendirme satırı yok. İstenirse Clean bitince
   "N cycle projects were cleaned — run Resolve cycles" gibi bir satır eklenebilir.
2. Clean koşusunda moda bakmayan metinler "build" diyor (akışta "X built", şerit "Building X/N", konsol notu) —
   satır Clean'inde de böyleydi; ayrı bir metin işi olabilir.
3. CLAUDE.md "OutDir'e dokunulmaz" maddesi: araç OutDir'e yazmıyor, ama `-t:Clean` (kullanıcı kararıyla) MSBuild
   eliyle orada kaydedilmiş çıktıları siliyor. ARCHITECTURE §9.4 bunu netleştirdi; CLAUDE.md'ye dokunulmadı.
