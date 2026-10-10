# Build kirli cycle gruplarını derler — sonuç raporu

Plan: `.claude/outputs/2026-10-08-05-16-build-compiles-dirty-cycles-plan.md` ·
Ölçüm: `.claude/outputs/2026-10-08-08-02-build-compiles-dirty-cycles-measurement.md` ·
Branch: `feat/build-compiles-dirty-cycles` (taban `37c98faf`).

## Ne değişti

Düz **Build** artık kirli bir dependency-cycle grubunu turlarla derler; temiz grubu `up to date` atlar.
**Rebuild** her grubu derler, tur 1'de her üyeyi. **Resolve cycles** isteğe bağlı, dar kapsamlı biçim olarak kalır
(yalnız gruplar + bayat upstream). Hangi modların grup derlediği tek yerdedir (`Core/Planning/CycleCompilation.cs`);
Sync önizlemesi, motorun planı, koordinatörün grup kapısı ve App'in tur takibi oradan okur. Kökünü bekleyen grup
koşullu değerlendirilir ve açılış dalgasında kesin sayılmaz — sıradan bekleyen proje gibi.

Kullanıcının ilk şikâyeti (Service WorkOrder projelerinin araçta kırmızı olması) bu kök nedene dayanıyordu: Build
değişmiş cycle üyelerini atlıyor, bağımlıları eski DLL'e karşı derleniyordu.

## Doğrulama

| Kontrol | Sonuç |
|---|---|
| Tam süit (`Category!=Acceptance`) | 4679 geçti, 8 atlandı; kırmızı yalnız 6 `StickyLayerHeaderClickTests` — imleç (0,0)'da park, değişmemiş develop'ta da aynı 6 kırmızı |
| Acceptance (gerçek OSYS) | 6/6 yeşil (tam Rebuild, dispatch determinizmi, incremental, 3 WPF geçici assembly testi) — final düzeltmelerden önce koştu; o düzeltmeler yalnız önizlemenin `Conditional` bayrağını değiştirir, motorun derleme kararına dokunmaz |
| Ölçüm | Clean sonrası tek Build 193,8 sn (taban: Resolve 197,4 sn + Build 186,5 sn); değişiklik olmadan Build 1,2 sn |
| Ara inceleme (Task 1-2 sonrası) | I1 ve I2 düzeltildi, kırmızı → yeşil |
| Final inceleme | Kritik 0; önemli bulgular düzeltildi (aşağıda) |

## Verdiğim kararlar

Her biri: ne karar verdim — neden — yanlışsa bedeli.

1. **Ana checkout + özellik branch'i, worktree yok** — proje CLAUDE.md'si ana projede çalışmayı söyler — bedel yok.
2. **Satır içi yürütme (task başına alt ajan yok)** — görev başına ayrı ajan turları geçmişte 8-9 saat sürdü; ikinci göz
   olarak bir ara ve bir final inceleme — bedel: task başına ayrı inceleme yok.
3. **Commit'lerde Claude attribution satırı yok** — global CLAUDE.md kuralı — bedel yok.
4. **NextPreview üyenin `WillBuild`'ini sabit `true` yerine aynı karardan türetir** — tek kaynak, bugün aynı değer — bedel yok.
5. **Kapanış ipucu satırlarının kaldırılması Task 4'ten Task 1'e çekildi** — Task 1'in Clean değişikliği ipucunu yanlış
   yerde tetikleyip bir testi kırıyordu; plan ipuçlarını zaten kaldırıyordu — bedel yok.
6. **Yakınsamama hafızası raporu Rebuild'de de yazılır** — "grup derleyen her koşu" kararına uyar — bedel: Rebuild'de bir
   bilgi satırı.
7. **"(last known failure)" eki koordinatör düzeyinde değil, saf testte pinli** — o durumu Build'de üreten plan artık
   oluşmuyor — bedel: ekin uçtan uca kablolaması pinli değil.
8. **Silinen bir test geri getirildi** (`convergence_clears_the_memory_even_for_a_member_whose_success_carries_a_dep_issue`)
   — senaryo Build'de üretilebiliyor — bedel yok.
9. **Tek proje koşusunda kirli cycle üyesi bağımlılık bayat sayılır** (yeni test) — plan artık üyeye gerçek
   `WillBuild` veriyor — bedel yok.
10. **`a_capped_group_is_invoked_again_by_the_next_Build_at_the_same_signature` adını korudu** — yeni kuralda da doğru — bedel yok.
11. **Yapılandırma geçişi testi `["C"]` beklentisiyle yeniden yazıldı** — planın beklentisi doğruydu, benim ilk denemem
    yanlıştı ve test yakaladı — bedel yok.
12. **`DecisionLabel.For` `inCycle` parametresini kaybetti** — kırmızı önce davranışla alındı — bedel yok.
13. **Ara inceleme M1 doküman düzeltmesiyle kapandı, davranış değişmedi** — bekleyen grup atlanırsa zaman kipindeki
    bağımlıları bir kez boşuna derlenir (güvenli yön) — bedel: o durumda bir fazla derleme. **Açık soru, aşağıda.**
14. **Doküman metni planın tablosundan, kodun dediği yerde ayrıldı** (bakım kutusunda Sync yok vb.) — doküman için
    otorite koddur — bedel: bir cümle.
15. **Incremental acceptance: alt sınır yalnız güvenilir başarıları sayar; meşru yeniden derleme kümesine güvenilmez
    başarılar eklendi** — motorun kendi `Trusted` sözü tek kaynak — bedel: yanlışlıkla güvenilir raporlanan üye kırmızı
    görünür (güvenli yön).
16. **Incremental acceptance'ta cycle üyesi cascade'i iddia değil kanıt kaydı** — satırsız üye zaman kipinde, sentetik
    içerik değişikliği onu çeviremez — bedel: cycle cascade gerilemesi iddia edilmez, kanıtta görünür. (Ölçümde 17/17
    çevrildi.)
17. **Tanı testi bağlamaları `CycleCompilation`'dan okur** (plan yalnız yorum demişti) — imzalar bayraktan bağımsız,
    çıktı değişmez — bedel yok.
18. **Acceptance `OverallBudget` 30 dk kaldı** — ölçülen süre (2 dk 41 sn) içinde — bedel: gerekirse bir yeniden koşu.
19. **Plan Adım 2 ölçümü arayüzsüz yapıldı** — VS ve istemci kapalıydı, sen yoktun; senin taban ayarlarınla (Balanced,
    paralellik 4, cpu cap %70, harici kökler, yalıtılmış defter) — bedel: yalnız arayüzde görünen bir kusur merge
    sonrası çıkabilir. Arayüz kontrolü ve Types üyesi düzenleme senaryosu sende.
20. **Final M-2 önemliye yükseltildi ve düzeltildi** — kökü kırıkken Build'e yeniden basınca dalga ve "N to build"
    bekleyen grubu kesin sayıyor, koşu grubu atlıyordu; sıradan bekleyen proje bu sözü hiç vermez — bedel: kök aynı
    koşuda düzelirse grup dalgada yanmadan derlenir (sıradan bekleyen projeyle aynı).
21. **Contracts `RunMode` doc'u, §8.3 tablo örneği ve App yorumları testsiz düzeltildi** — yalnız yorum/doküman — bedel yok.
22. **Tur sayacı sıfırlama demeti ve Sync test fixture'ları tek yere toplandı** — "kopya YASAK" — bedel yok.
23. **"Build veya Cycles" mod kuralı iki yerde kaldı** (`ConditionalRebuild` ve koordinatörün güncel tohum kapısı) — iki
    kopya da bu branch'ten önce vardı — bedel: ileride mod değişirse iki yer. Organizasyon notu olarak aşağıda.
24. **Final reviewer'ın kenara koyduğu maddeler aynen kaldı** — Rebuild önizlemesinin kuyruk paydası, şerit metni,
    Build turlarının profil sınırı, harici kök güncellemesi, Fast modu grup kapısı, legacy fixture kopyası, eski plan
    cümleleri, gerçek motor Rebuild testinin süresi, turda restore-once, Sticky testleri — hepsi bu branch'ten önce
    vardı ya da plan kararıdır — bedel: yeni bir bedel yok.

## Ertelenen küçük bulgular

- Bir Build'de iki grubun turu çakışıp ekrandaki grup önce biterse şerit, ikinci grubun kalan turu boyunca `Building` yazar.
- "(last known failure)" eki yalnız saf testte pinli, uçtan uca bir Build koşusunda değil.
- Graceful Stop'un tur ortasında Build için ayrı pinli olmaması (kod yolu moddan bağımsız).
- Ledger Task 3'ün kırmızı koşusunu kaydetmemiş (süreç notu).

## Açık soru (ara inceleme M1)

Yalnız kökünü bekleyen bir grup Build'de `dependency still failing` ile atlandığında, zaman kipindeki (defter satırı
olmayan) bağımlıları bir kez boşuna derlenir. Davranışı değiştirmek (`IncrementalPlanner.ProducesNewOutput`'ta bekleyen
grubu cascade tohumu saymamak) mümkün; bedeli, kök aynı koşuda düzelirse o bağımlıların bir sonraki Sync'e kadar geç
derlenmesi. Şu an güvenli yön (fazla derleme) seçili.

## Organizasyon notu

`ConditionalRebuild.ModeEvaluatesConditionally` ile `RunCoordinator`'daki `cmd.Mode == RunMode.Build || cyclesRun`
aynı kavramı ("defteri dinleyen modlar") iki yerde söylüyor. Bu branch'ten önce de böyleydi; tek bir Core predicate'ine
toplanabilir.
