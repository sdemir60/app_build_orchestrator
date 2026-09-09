# İçerikten Karar — Uygulama Promptu

> Bu dosya bir **prompt**tur, rapor değil. Aşağıdaki "Yapıştırılacak prompt" bloğunu yeni bir session'ın ilk
> mesajına olduğu gibi yapıştır. Plan: `2026-09-10-01-25-content-based-incremental-plan.md`.

| | |
|---|---|
| **Branch** | `feat/content-based-incremental` (yeni; main'den) |
| **Merge / push** | YAPILMAZ — kullanıcı branch'te test eder, sonra karar verir |
| **Kapsam** | Plan Faz 0–4 ve Faz 6. **Faz 5 (gösterim) HARİÇ** — tasarım paketini bekliyor |

---

## Yapıştırılacak prompt

`.claude/outputs/2026-09-10-01-25-content-based-incremental-plan.md` dosyasındaki planı uygulayacaksın:
"derlenecek mi" kararı git'ten değil diskteki dosya içeriğinden verilecek; ana repo ve harici kökler tek
yoldan geçecek; xaml/resx/props açığı kapanacak. Şu sırayla ve şu kurallarla yürüt:

1. **Bağlamı oku.** Önce `CLAUDE.md` (çalışma kuralları — kırmızı test kuralı, "davranış değişince test de
   değişir", kopya yasağı, doküman güncelleme), sonra planın tamamı (bağlayıcı kararlar D1–D10, faz listesi,
   kabul senaryoları, riskler), sonra önceki turun kaydı
   `2026-09-09-18-47-externals-as-workspace-roots-results.md` (harici köklerin bugünkü hâli). Plandaki
   kararları sorgulama; belirsiz bir nokta bulursan bana sor, tahmin yürütme.

2. **Branch aç.** `main`'den `feat/content-based-incremental`. Task başına commit at. **`main`'e merge ETME,
   push ETME** — ben branch'te elle test edeceğim, sonra birlikte karar vereceğiz. Başka hiçbir branch'e
   dokunma. İş bittiğinde branch checkout'lu kalsın.

3. **Faz 0 — ÖLÇÜM, kod yazmadan önce.** Planın Faz 0 bölümündeki dört ölçümü (M0 bugünkü taban, M1 ilk tam
   okuma, M2 sıcak tam okuma, M3 yalnız stat) gerçek OSYS reposunda yap. Ölçüm aracı: test projesinde
   `[Trait("Category", "Measurement")]` etiketli, OSYS kökünü `BO_MEASURE_ROOT` ortam değişkeninden okuyan,
   değişken yoksa **Skip** olan tek bir test — varsayılan süit bundan etkilenmemeli. Girdi kümesini planın
   D2 kararına göre kur (Compile + Page/ApplicationDefinition/EmbeddedResource/Resource + klasör altı
   cs/xaml/resx/props/targets + yukarı doğru Directory.Build.*), yani ölçüm gerçek girdi kümesini ölçsün,
   yalnız `.cs`'i değil. Her ölçümü en az 3 kez koş, medyanı al. Sayıları planın "Faz 0 sonucu" bölümüne
   YAZ ve bana raporla.

   **Kapı:** M3 ≤ 500 ms ve M3 ≤ 1,5×M0; M2 ≤ 2 s; M1 ≤ 6 s. **Üçü birden tutmazsa DUR:** sonucu yaz, Faz
   1'e geçme, bana sor. Plan B (mevcut git yolunda yalnız girdi kümesini genişletmek) benim onayım olmadan
   uygulanmaz.

4. **Kapı geçtiyse Faz 1 → 2 → 3 → 4 sırasıyla.** Her davranış için ÖNCE test, kırmızıyı gör, sonra kod.
   Kırmızıyı gösteremiyorsan test yanlıştır, kuralı esnetme. Mevcut committed-fingerprint ve "worktree'de
   dirty imzayı değiştirmez" pinleri sessizce silinmez: YENİ kuralı pinleyecek şekilde yeniden yazılır,
   doc'una eski iddia + değişme gerekçesi işlenir. Planın kabul senaryoları 2–6 için birer test olsun
   (xaml commit, çevrimdışı, in-place = worktree imzası, git + TFVC harici aynı karar, yükseltmede tek
   seferlik tam derleme). Kopya YASAK: özet primitifleri, ayraçlar ve girdi kümesi tek yerde.

5. **Faz 5'e girme.** Satır gösterimi, External grup başlığı, tel üzerine yeni gösterim alanları — hepsi
   tasarım paketini bekliyor. Satır bugünkü gibi commit çiftini göstermeye devam etsin. `ExternalRevisionReader`
   ve `BuildState.BuiltCommit` yerinde kalır (tanı ve ileride başlık için).

6. **Değişmezleri koru.** Git salt-okur (ana repoda checkout/pull/merge/reset yok; harici ff-only yalnız
   `Core/Externals`), OutDir'e dokunulmaz, planlama Core'da, stdout yalnız NDJSON. §4'teki "DLL/bin/obj
   timestamp asla okunmaz" kuralı aynen kalır; D3'teki stat-anahtarlı özet önbelleği KAYNAK dosyalar içindir
   ve csproj değerlendirme önbelleğiyle aynı desendir — bunu kodda ve dokümanda açıkça ayır.

7. **Faz 6 — dokümanlar aynı işte.** ARCHITECTURE §4/§7/§8.6/§10.1/§10.6/§16/§22, README (geçişte tek
   seferlik tam derleme), CLAUDE.md değişmezine stat-önbellek notu, sürüm notu. Anlatı üslubu: doküman
   projeyi ANLATIR, "eskiden böyleydi" yazmaz; değişen davranış yerinde yeniden yazılır.

8. **Bitişte.** `dotnet build BuildOrchestrator.slnx` 0 hata; tam süit (`Category!=Acceptance`) ve Acceptance
   süiti (`Category=Acceptance`, ~2 dk, gerçek OSYS) yeşil — guard'lar dahil. Uygulama açıksa build alma.
   `.claude/outputs/2026-09-10-01-25-content-based-incremental-results.md` yaz: Faz 0 sayıları, kapı kararı,
   plandan sapmalar (gerekçeli), silinen/eklenen dosyalar, doğrulama tablosu, bilinen açıklar ve **benim elle
   test etmem için adım adım senaryo** (xaml değişikliği, çevrimdışı Sync, harici kök, pull kapalı).

**Bu işe özel dikkat:** ölçüm gerçekten D2 girdi kümesini ölçmeli — yalnız `.cs` ölçüp geçmek kapıyı
anlamsız kılar. Worktree koşusunda içerik FİZİKSEL yoldan okunur ama zincirdeki yol terimi köke GÖRELİ kalır
(D5); bunu in-place = worktree eşitlik testiyle pinle. Önbellek dosyası bozulunca hesap düşmemeli, yalnız
yavaşlamalı.
