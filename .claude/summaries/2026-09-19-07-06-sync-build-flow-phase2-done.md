# Sync / Build akışı — Faz 2 (tek ağaç ve branch) oturum özeti

Tarih: 2026-09-18 / 2026-09-19 · Tür: plan + uygulama · Çatı branch: `sync-build-flow` (Faz 2 merge'ü `75d1d9f`,
`--no-ff`) · `main`'e merge YOK (kullanıcı çatıyı test edip onaylayacak). Kullanıcı bilgisayar başında değildi;
"en doğru kararları ver, sonda raporla" dedi — plan onayı beklenmedi, kararlar aşağıda.

## Ne yapıldı

- Faz 2 TDD dökümü yazıldı: `.claude/outputs/2026-09-18-23-49-sync-build-flow-phase2-plan.md` (T0-T13, preflight
  tablosu P1-P10, Claude kararları).
- `superpowers:subagent-driven-development` ile T0-T12 task-by-task uygulandı; her task ayrı review, bulgular fix
  round'larında kapatıldı. Bütün branch final review'dan geçti; bulgular tek fix dalgasında düzeltildi, yeniden
  review "Ready to merge".
- Tam süit (`Category!=Acceptance`): 3270 geçti, 6 atlandı, 6 başarısız. Başarısızların hepsi
  `StickyLayerHeaderClickTests`; faz öncesi commit'te (`5eeaef6`) de aynı şekilde düşüyor — gerçek pencere ve
  fare yakalama istiyor, ekran kilitliyken çalışmıyor (ortam). Acceptance 3/3 yeşil (gerçek OSYS).
- Tasarım paketi v1.21.0: `.claude/outputs/2026-09-18-23-55-design-v1.21.0/README.md` (yalnız metin).

## Ne değişti (kısa)

- Worktree modu tamamen kalktı (motor, IPC, UI, ayar, testler). Her koşu çalışma ağacında; obj izolasyonu yok.
- Branch chip'i gerçek `git checkout` yapar. Settings → General → BRANCHES → "Stash and switch branches"
  (kapalı = Stop, varsayılan). Git yazımı tek dosyada: `Core/Git/RepositoryWriter.cs` (ff-only pull, checkout,
  stash); guard `NoGitMutationOutsideTheWriterTests`.
- Sync kipleri: Manual (Sync butonu), Appended (açılış, restart, pull, Clean/Optimize, Settings Save), BranchChange
  (yeni bölüm, fetch yok), Silent (commit, pencereye dönüş; konsol/şerit/pill değişmez, akışa tek satır). Liste ve
  graf yalnız yapı değişince baştan kurulur (reveal).
- `.git\logs\HEAD` izleyicisi (1,5 s sessizlik), pencereye dönüşte 5 s eşiği, çift Sync kontrolü, meşgulken tek
  bekleyen tetik. Pull ve checkout uçuştayken workspace komutları kilitli.
- Koşu sırasında branch değişimi: `StopKind.Interrupt`; kesmeden sonra biten projeler güvenilmez (gri
  `never built`); yeni bölüm özet satırıyla açılır ("Run interrupted by a branch change — N built, M not built ·
  logs: …").
- Git işlemi yarıdayken (merge/rebase/cherry-pick/revert/index.lock): kendiliğinden Sync bekler, 2 s yoklama,
  branch chip'inde amber nokta, checkout ve pull kilitli, Build tek uyarı satırıyla serbest, 30 s takılı kilit uyarısı.
- Çökme kurtarma: `run-inflight.json`; açılış/restart'ta uçuştaki projeler kanıtsız hataya çekilir, konsola
  "previous run was interrupted; N projects will rebuild", ardından Sync.
- Eski worktree havuzu klasörü duruyorsa ilk açılışta tek satır ipucu (araç silmez).
- Testlerde gerçek motor her zaman izole `SupervisorSandbox` içinde başlar (kullanıcının gerçek önbelleğine
  dokunmaz); guard `SupervisorIsolationGuardTests`.
- ARCHITECTURE.md, README.md, CLAUDE.md değişmezleri (üç yazım istisnası) güncellendi; ARCHITECTURE §10.4/§10.5
  (havuz, path sanitization) kalktı, sonraki bölümler yeniden numaralandı.

## Claude kararları (itiraz edilebilir)

- Kirli ağaç seçimi iki seçenekli ayar yerine tek anahtar (mevcut Settings yalnız anahtar destekliyor).
- Git dizini `git rev-parse` yerine `.git` dosyası okunarak bulunur (process açılmaz, sonuç aynı).
- "Uçuştakilerin sonucu deftere yazılmaz" = başarı olarak yazılmaz: kanıtsız hata (çökme kurtarmasıyla aynı hâl).
  Sınır: kesme motora ulaşana kadar (1,5 s + IPC) biten projeler güvenilir kalır (ARCHITECTURE §20).
- Sessiz Sync fazı değiştirmez, run hata özetini silmez, satırları nötrlemez; ama Sync süresince komutlar kilitli
  ve Sync butonu meşgul görünür (motor tek komut işler).
- Detached HEAD'de `N behind` gösterilmez.
- Pull uçuştayken Build/Sync/Clean/Optimize/branch chip kilitli (checkout gibi).
- Motor her hazır oluşta (ilk açılış ve restart) workspace varsa Sync koşar; eski havuz ipucu yalnız ilk açılışta.
- HEAD izleyicisi sessizlik penceresindeki tüm reflog satırlarını sınıflar, en güçlüsünü bildirir.
- Park edilenler (davranış etkisi yok): Supervisor yazıcılarının `WorkspaceServices` fabrikasından gelmemesi, tek
  `AppDataRoot` olmaması, `Short7` tekrarı, hata kodu literal'lerinin iki tarafta durması, bazı küçük test boşlukları.

## Açık kalanlar

- `StickyLayerHeaderClickTests` ekran açıkken yeniden koşulmalı (bu oturumda ortam yüzünden kırmızı).
- Kullanıcı çatıyı canlı uygulamada test etmedi (deneme listesi oturum sonunda verildi).
- Faz 3 (dışarıdan derleme kredisi) dökümü yazılmadı.
- Faz 1'den kalan açıklar hâlâ açık: satırdan Build'de proje sayfası cümlesi; iki Acceptance sınıfının paralel
  derlemesi (MSB3026); porcelain v1 rename uç durumu. (`ParseLsTreeBlobHashes` Türkçe yol sorunu, ölü kod
  silindiği için kalktı.)
