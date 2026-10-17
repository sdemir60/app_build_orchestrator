# Resolve önceliği kuralının kaldırılması + title bar kaynak şeridi (tasarım v1.25.0) — özet

Tarih: 2026-10-17 · Brief: `.claude/outputs/2026-10-17-10-44-resource-strip-and-resolve-priority-opus-prompt.md` · Planlar:
`.claude/outputs/2026-10-17-11-40-resolve-follows-profile-plan.md`, `.claude/outputs/2026-10-17-12-30-resource-strip-plan.md`

## İş 1 — `fix/resolve-follows-profile` (develop'a merge: `7542cdd9`, CI yeşil)

**Kaldırılanlar:** `PerfProfile.ForRun`; `PerfNoteText.ResolveNote`, üç parametreli `Note(mode, profile, bool)` ve `PriorityValue`;
`StartRunCommand.ResolveAtFullPriority`; Supervisor'daki `_perfRunMode`/`_perfResolveAtFullPriority` ve Resolve decision.log satırı
(`ApplyPerfLocked` profili dönüştürmeden uygular); General → BUILD'deki *Resolve cycles at full priority* satırı ve enum üyesi;
taslak/ayar dosyası/ui-state/VM alanları, `ApplyResolveAtFullPriority` ve konsol notları, App'in Resolve notu. Yeni kural: `Cycles`
koşusu profilin cap ve priority'sini Build/Rebuild/Clean gibi aynen uygular; cap'siz koşu yalnız Full. Göç kodu yok — eski
`ui-state.json`, settings dosyası ve eski App'in `startRun` alanı bilinmeyen alan olarak yok sayılır (üçü de testle pinli).

**Yeniden yazılan testler:** Supervisor'ın Resolve aynaları Build testleriyle `Theory`'de birleşti (`Build_and_resolve_cycles_runs_apply_the_profile_cap_and_priority`
— Cycles satırları kırmızıydı), koşu ortası değişim, kopya tabanı, planlama penceresi ve drain aynaları yeni kurala; `PerfProfileTests`,
`IpcMessagesTests` (tel alanı yok + eski alan okunur), `RunViewModelStateTests`'in perf bağlamı testleri, General katalog/zincir/sayfa testleri;
`ResolveAtFullPrioritySettingsTests` → `ResolveFollowsProfileSettingsTests`. `PriorityValue`'nun tek testi fonksiyonla birlikte kalktı (gerekçe doc'ta).

**Doküman:** ARCHITECTURE §5.2, §8.1, §11.1 (yeni kural + ölçüm gerekçesi dayanıklı dille, Full'ün CPU tarafı cümlesi), §11.3, §13.3, §16,
§17.5, §22; README Performance modes ve ayar listesi.

## İş 2 — `feature/resource-strip`

**Ne:** Title bar'ın tam ortasında CPU · RAM · paralellik + mod şeridi. Veri App'te: outer job accounting + PID listesi ve App'in kendi
işlemci zamanı; RAM = App + motor + derleyiciler private bytes; motora IPC yok; ilk örnek 0,5 sn, sonra 3 sn; gizli/küçültülmüşken durur,
dönüşte anında örnek. Paralellik hücresi VM'den (pay canlı, koşuda koşu başındaki mod, tek proje koşusu çipten, kısılmışsa tooltip'te
`reduced: <reason>`). About → Environment'taki üç bellek satırı yalnız kopyalanan tanı metninde.

**Kararlar (kullanıcıya sorulanlar dahil):** R1 — private bytes'ın tek okuma yolu `MemoryLine` içinde `GetProcessMemoryInfo`; `MemoryLine.Read()`
da onu kullanır (eski `PrivateMemorySize64` her çağrıda makinedeki tüm süreçlerin anlık görüntüsünü alıyordu: ~3,4 ms + ~3,5 KB). `memory:`
satırının biçimi ve anlamı aynı, değer ~1 MB daha düşük (eski sayaç kendi tamponunu sayıyordu). Kullanıcıya A/B/C olarak soruldu, cevap
gelmedi; A ile ilerlendi. Diğerleri: pay koşu yokken 0; tooltip'teki cap hücrenin modu; şerit yalnız hücrelerde tıklama alanı, hücreden
basış Windows'un kendi başlık sürüklemesine devredilir (WM_NCLBUTTONDOWN/HTCAPTION — WPF `DragMove` büyütülmüş pencerede çalışmıyor);
değerler XAML bağıyla değil gizli yüzey kapısından yazılır; `BO_PROBE_STRIP` sondası.

**Ölçülen maliyet:** gerçek okuma yolu, 6 üyeli job, 10 000 tekrar: örnek başına **25,8 µs, 0 bayt** tahsis (testte de sıfır tahsis pini).

**Gözle doğrulama (Release kopyası, deneme reposu, `BO_PROBE_STRIP`):** açılışta hemen gerçek değer ve 3 sn ritim; boşta `1% · 216 MB ·
0/4 Balanced`; Build'de pay `0→3→4→3→4→3→1→0/4`, CPU %42-44, RAM 390 MB; küçültülmüşken ve tepsideyken 9 sn boyunca sonda satırı yok,
dönüşte anında satır; hücre konumları değer değişince kaymıyor. Kullanıcının `ui-state.json`'ı yedeklenip bayt bayt geri yüklendi.
**Elle bakılmadı (fare ister):** tooltip'lerin aşağı açılması, büyütülmüş pencerede hücreden sürükleme, çift tık, sağ tık menüsü, şeridin
üst kenarından boyutlandırma; Update hapı yalnız kurulu kopyada görünür (1240 + hap durumu realize testiyle pinli).

**Doküman:** ARCHITECTURE §3.3, yeni §11.5 "Resource strip", §11.1, §11.2, §11.4, §12.2-12.4, §13.1-13.3, §14.1, §14.4, §15, §16, §17,
§22; README title bar, About/Environment ve Performance modes.

**Takip (ertelenen küçükler):** `—` üç ayrı yerde tanımlı; ürün adı gömülü-ad guard'ı opt-in liste (4 eski gömülü kullanım borç);
ResourceSamplerTests'te canlı job kurulumu tekrarı; `MsBuildInvokerTests`'in kendi IOCP döngüleri; About → Environment içeriği gövdeyi
birkaç piksel aşıyor olabilir (değişiklikten önce de vardı). Dokümanda önceden kalma çelişki: §14.4 "title bar'da başka amber yok" ↔
§13.3 What's new'deki amber nokta.

**Testler:** tam süit (Release, `Category!=Acceptance`) son durumda 5730 geçti, 11 atlandı, 4 düştü — iki `UpdatePillTests` (bu makinenin
DPI'ı; develop'un değişmemiş kopyasında da düşüyor), `SafeExitProcessTests` ve `ConsoleReaderNavigationTests` (yük altında zamanlama;
tek başına 1/1 ve 59/59 geçti; ikisi de bu dalda değişmedi).

## İsteğe bağlı doğrulama ölçümü (İş 1 sonrası, brief)

Makine boşken ve VS kapalıyken yapılmalıydı; oturum boyunca makine kullanımdaydı ve VS açıktı — yapılmadı, kullanıcıya soruldu.
