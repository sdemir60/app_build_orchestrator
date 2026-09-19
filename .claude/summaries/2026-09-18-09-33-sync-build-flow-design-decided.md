# Sync / Build akışı yeniden tasarımı — oturum özeti

Tarih: 2026-09-17 / 2026-09-18 · Tür: tasarım ve karar oturumu, **kod yazılmadı** · Çatı branch: `sync-build-flow`

## Ne yapıldı

- Kullanıcının isteği: Sync sonrası neyin güncel / derlenecek / bozuk olduğu renkten görünsün; Build ve satırdan Build
  listeyi sıfırlamasın, toplam hep görünsün; worktree / in-place / mod karmaşası kalksın; branch değişimi ve dışarıdan
  (VS) derleme kendiliğinden yansısın. Animasyonlar korunacak.
- Claude web'de (koda erişimsiz) yazılan v2 raporu kodla doğrulandı: 28 iddianın 24'ü satırıyla tuttu; dört boşluk
  (Sync sonrası üçgen yok, `OSYS-Build` zaten var, tasarım §8 kararı kapalı, timeout kanıt değil) ve bir doküman/kod
  uyumsuzluğu (CLAUDE.md "reset hiçbir akışta çalıştırılmaz" — kod havuz worktree'sinde reset atıyor) bulundu.
- İstek sıfırdan, tarafsız analiz edildi ve üç tur revize edildi. Koddan netleşen kritik olgu: **karar kaynak
  dosyaların içerik hash'idir**; çıktı klasörüne ve commit'e bakılmaz. Bu, "hangi ağaç" sorusunu doğruluktan tercihe
  çevirdi.
- Ağaç tartışması: worktree (committed hâl) ile ana proje (çalışma ağacı) karşılaştırıldı; kullanıcı önce worktree'yi
  seçti, sonra artı/eksi tablosuyla **ana proje** kararı verildi (dirty bağımlılık sorununu yalnız o çözüyor).
- Branch: anlık izleme (`.git\logs\HEAD`), araçtan tıklamayla checkout, kirli ağaç için ayar (Stop / Stash and
  switch), VS'de derlenen projeye kredi ("defter yalnız kendi ürettiği çıktı için konuşur" + zaman kipi) tasarlandı.
- 55 senaryoluk kaçak analizi yapıldı; kalan dört sınır bugünkü modelde de var.
- Son tur: konsol bölüm kuralı (kendiliğinden Sync sessiz, branch değişimi yeni bölüm), git işlemi yarıdayken kapı,
  pull'un kirli ağaçta reddi kararlaştırıldı.
- Bağlayıcı spec ve Faz 1 TDD planı yazıldı; çatı branch açılıp tüm kayıtlarla commit'lendi ve push'landı.

## Temel kararlar

Ana projede derleme, worktree yok · renk = çıktının durumu (yeşil güncel, gri derlenecek, kırmızı kanıtlı bozuk),
kümülatif, hiçbir işlem sıfırlamaz · kırmızı yalnız aynı imzada derleyici hatası, timeout gri · üçgen kümülatif · küp
döngüde hep amber · etiketler `up to date · 2h` / `modified` / `modified · local` / `affected` / `never built` /
`failed · 2h`, yuva 134 px · atlandı chip'i kalkar · animasyonlar aynen · VS'de derlenen proje çıktısı tazeyse yeşil ·
`C:\OSYS` bağımlılığı yok · fazlar: 1 renk modeli → 2 tek ağaç ve branch → 3 dışarıdan derleme kredisi. Tam liste
spec §1'de (24 karar).

## Dosyalar

- **Spec (bağlayıcı):** `.claude/outputs/2026-09-18-07-56-sync-build-flow-spec.md`
- **Faz 1 planı (T0-T9):** `.claude/outputs/2026-09-18-07-56-sync-build-flow-phase1-plan.md`
- **Kod kanıtları:** `.claude/outputs/2026-09-17-19-41-cumulative-status-analysis-verification.md`
- **Tarihsel (karar süreci):** `2026-09-17-18-16-…-analysis-v2.md` (Claude web), `2026-09-17-20-36-…-analysis.md`,
  `2026-09-18-05-39-…-analysis-v2.md`, `2026-09-18-06-27-…-decided-model.md`, `2026-09-18-07-37-…-final-model.md`

## Branch

Çatı `sync-build-flow` (`main` = `ea3ab62`'den). Her faz kendi branch'inde (`sync-build-flow-phase1` …), çatıya
`--no-ff` merge; `main`'e yalnız kullanıcı çatıyı test edip onaylayınca. Kullanıcı bunu CLAUDE.md'ye kalıcı kural
olarak yazdırmadı; kural planın Global Constraints bölümünde.

## Açık kalanlar

- Faz 1 uygulanmadı. CLAUDE.md gereği `superpowers:subagent-driven-development` ile task-by-task.
- Faz 2 ve Faz 3 dökümleri kendi sıraları gelince, o günkü koda göre yazılacak.
- CLAUDE.md'nin "reset" cümlesi ve "iki istisna" ifadesi Faz 2'de yeniden yazılacak.
