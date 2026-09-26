# Sync/durum kapsam kampanyası — sonuç raporu

Branch: `test/sync-state-coverage` (df18450 → 91f8f43, 24 commit). Plan:
`.claude/outputs/2026-09-26-12-01-sync-state-coverage-tdd-plan.md`. Her task bağımsız implementer +
bağımsız reviewer + gerekirse fix turundan geçti; sonda tüm-branch final review yapıldı ve bulguları
kapatıldı. Filtreli tam süit branch'te yeşil: 3523 başarılı / 0 başarısız / 6 atlanan (kapılı problar).
21 Eylül rehberinin 73 ekran maddesi + etiket semantiği beş keşif ajanıyla mevcut testlere eşleştirildi;
boşluklar ~50 yeni pin testiyle kapatıldı.

## Bulgular (bloklayıcı → önemli → kozmetik)

### Bloklayıcı (düzeltildi)

- **B1 — `.vs`/`.git`/`node_modules` proje girdi taramasına sızıyordu.**
  Konum: `Core/Incremental/ProjectInputs.cs` (eski ~148-150; yalnız bin/obj atlanıyordu).
  Etki: zaman yolundaki (VS'de derlenmiş) projede karar klasör tarihlerini okur; klasöründe `.vs` olan
  (yanında .sln taşıyan) ya da repo kökünde oturan proje **VS açıkken durduk yere `modified`** olabilirdi —
  tam senin "VS ile yan yana" senaryonun ortası.
  Düzeltme: kırmızı test önce görüldü (3 test, doğru sebepten) → atlama adları tek kaynağa toplandı
  (`WorkspaceScanner.BuildOutputFolderNames`/`ExternalToolingFolderNames`/`IsSkippedFolder`);
  `ProjectInputs` ve `CsprojEvaluator` oradan okuyor (üçüncü kopya liste de kaldırıldı). ARCHITECTURE §7.1
  aynı işte güncellendi. Commit'ler: 766559e, 1897dc4 (T22 kuyruğu).

### Önemli (bilgi/karar — davranışa dokunulmadı)

- **Ö1 — StickyLayerHeaderClickTests flaky.** Tam süit koşusunda 6 test kırmızı düştü (36→84, 108→0,
  108→288, 0→50 gibi kaotik ofsetler), aynı testler iki ayrı izole koşudan birinde kırmızı birinde
  **27/27 yeşil**; temiz-ortam tam süitte de yeşil. Kod nedeni yok (branch'in App tarafı yorum-only).
  Teşhis: bu testler gerçek HWND + `CaptureMouse` + gerçek fare basışı kullanıyor
  (`StickyLayerHeaderClickTests.cs:379` yorumu) ve makinede eşzamanlı iş varken tıklamalar yanlış yere
  gidiyor. Dokunmadım (senin yeni alanın). Karar senin: seri koleksiyona almak / girdi sentezini
  RaiseEvent'e çevirmek / böyle bırakmak.
- **Ö2 — Bekleyen (yeşil+⚠) satırın sayımı tutarsız.** Ribbon onu "up to date" sayıyor
  (`RibbonText.cs:143-144`), Sync akış satırı/konsol iki sayıya da katmıyor
  (`SyncWorkspaceService.cs:363-364`). Hangisi doğruysa öbürü ona çekilmeli — karar senin.
- **Ö3 — Harici kök ana repo İÇİNİ gösterebiliyor.** `ExternalWorkspaceResolver` reddetmiyor; o durumda
  harici projede `local` rozeti çıkabilir (`LocalEdits.cs:11-14` niyetiyle çelişir). Reddetmek mi,
  belgelemek mi — karar senin.

### Kozmetik

- Önceden var olan derleme uyarıları 9 eski test dosyasında duruyor (bu branch'ten bağımsız).
- Ledger'da küçük ertelenmişler listesi var (test adı "copies", OpenRowMenu yardımcı adayı, eşik sabitinin
  SourceGuard'a taşınması vb.) — hiçbiri merge engeli değil; final review da öyle triage etti.

## Rehber düzeltmeleri (21 Eylül rehberi tarihsel — düzeltmeler BURADA geçerli)

1. **Madde 23 + §1 failed çıkış 4 YANLIŞTI:** kırmızı A'yı VS'de **kod değiştirerek** düzeltirsen B/C yeşil
   kalmaz — imzaları oynadığı için aynı Sync'te `affected` olur ve ⚠ o anda düşer; sonraki Build onları
   koşulsuz derler. Yeşil+⚠ yalnız içerik değişmeden düzelmede (ör. eksik DLL yerine kondu).
2. **Madde 33 çıkarımı YANLIŞTI:** bakım Clean'i `obj`'yi sildiği için klasör tarihi ilerler → paylaşılan
   OutputPath'li proje yeşil değil **gri `modified`** görünür. Yeşil ancak projede silinecek bin/obj yokken;
   kilitli DLL durumunda da `modified`.
3. **Madde 50/51:** sessiz Sync'in 5 sn eşiği **son Sync anından** ölçülür, pencereden uzak kalma
   süresinden değil (5000 ms sınırı artık testle pinli; tam 5000'de Sync GİDER).
4. **Madde 10:** dolaylı bağımlının (C) logundaki uyarı `warning: failure in dependency chain (A)…`;
   "A failed in this run" yalnız doğrudan bağımlıda.
5. **§3 tek üye ▶:** satırın ⚠'si her zaman `In a dependency cycle` (öncelik sırası); döngü kardeşleri
   ledger notu + log satırında görünür.
6. **Resolve cycles:** kirli upstream turlardan önce TEK sefer derlenir; ribbon'da
   `preparing dependencies` evresi vardır; "did not converge" yalnız ilerlemesiz grupta, "did not fully
   settle" tur tavanında yeşil bitenlerde; ikisi de sonraki görünür işlemde temizlenir.
7. **Küçükler:** zaman yolunda HERHANGİ dosya ekleme/silme/yeniden adlandırma (.md dahil) klasör tarihi
   yüzünden `modified` yapar (yerinde .md düzenlemek yapmaz); madde 22'de C de dalgayla `affected` olur;
   madde 24 yalnız derleyici hatasında geçerli; madde 42'de aynı-boyut+yeni-tarih kopya bilerek fark
   edilmez; madde 62'de chip mid-merge devre dışı; madde 16 sayfası yalnız motorda log yokken görünür ve
   hiç başarı görmemiş kırmızıda iki satırdır; SDK-style projede elle DLL silmek görünmez; F5 koşarken
   Stop'tur; hover-echo yüzünden graf düğümüne gelmek liste satırının etiketini ▶/⋯ ile değiştirir
   (madde 1'i test ederken imleci liste/graf dışında tut); arama kutusunda dışarı tıklayınca odak çıkar
   ama sorgu KALIR — Esc'in temizlemesi için önce Ctrl+F.

## Ne pinlendi (özet)

- **Liste ↔ graf tek doğruluk kaynağı** mimaride doğrulandı (ikisi de `VisualStatus` okur) ve koşu
  SONUÇLARINDAN sonra gerçek pencerede pinlendi: başarı yeşili, kanıtlı hata kırmızısı, kanıtsız hata
  grisi, döngü küpü amber + şerit gerçek renk (T2).
- **VS ile dış derleme:** araç-derledi→VS-rebuild geçişi Sync seviyesinde (içerik aynı → built outside;
  içerik değişti → bağımlı `affected`); HintPath DLL yenilenmesi → `affected` + arkadaki projeye dalga;
  ledger yolunda paylaşılan kopya kırıkları; VS'de düzeltilen kırmızının gerçek geçişi (T3/T4/T14/T18).
- **Clean→Sync zinciri** beş varyantla (default/paylaşılan/obj'suz/kilitli/SDK) (T5).
- **Etiket geçiş serisi** gerçek git ile: `modified · local`→`modified`→revert→`up to date`; untracked;
  .md/.config etkisiz; Directory.Build.props dalgası (T6).
- **Hata yüzeyi:** failed kaydından SignatureChanged; BuiltContent failure yazımını atlatır; dependency
  issue akış satırı; `dependency still failing` sonrası yeşil+⚠; üç halkalı bekleyen zincir (T7/T15).
- **⚠ öncelik tablosu** (unconverged>unsettled>in-cycle>dep) + CycleUnsettled metni ilk kez (T8).
- **Kilitler/akış:** satır menüsü koşuda gerçekten kilitli; Stop akış satırı; graceful stop sonrası geç
  başarı Trusted (T9/T10).
- **Sync tetikleyicileri:** dosya kaydı sync TETİKLEMEZ + tek-FileSystemWatcher kaynak guard'ı (üç yazım
  biçimini yakalar); aktivasyon 4999/5000 sınırı (T11/T13).
- **Resolve cycles ribbon** metinleri + VM beslemesi (T12).
- **Proje sayfası** boş-durum metinleri; arama kutusu Esc/✕/click-away VM bağı (T16/T17).
- **Git chip'leri:** behind→tek pull komutu→tek fetch'li Sync; mid-merge notları amber DEĞİL (T19/T20).

## Sadece gözle doğrulanabilir kalanlar (bilgisayara dönünce, kısa el listesi)

1. Satır sallanması (shake) ve nefes animasyonunun CANLI görünümü (yapısal pinler var; canlı pencere ≠
   ekran dışı kare).
2. Bakım Clean'inde spinner'ın gerçekten döndüğü (varlığı/tipi pinli, dönüşü değil).
3. Graf bitiş koreografisinin akıcılığı ve filtrenin finalden sonra dönüşünün GÖRSEL hissi (motion-on
   gerçek pencere pinleri var; akıcılık gözle).
4. Tooltip'lerin gerçek fare beklemesiyle görünmesi (metinler pinli).
5. Rehber §5'teki C bölümünü canlı VS ile bir tur koşmak istersen: VS'in AYNI konfigürasyonda (Debug)
   derlediğinden ve gerçekten DLL yazdığından emin ol (up-to-date atlaması VS'i görünmez kılar);
   madde 25'te dosyayı gerçekten yeniden kaydettir (değiştir-geri al-kaydet).

## Sana bekleyen kararlar

- Ö1 sticky flake stratejisi · Ö2 bekleyen satır sayımı (ribbon mu akış satırı mı doğru) · Ö3 harici kök
  ana repo içini gösterebilsin mi.
