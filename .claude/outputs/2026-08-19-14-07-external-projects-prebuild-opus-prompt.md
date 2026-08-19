# Opus Uygulama Prompt'u — Harici Projeler (External Projects) Ön-Derleme

> Bu dosyanın içeriğini olduğu gibi Opus'a (derleme yapılabilen makinede) mesaj olarak yapıştır.

---

Projeye "harici projeler ön-derleme" özelliğini ekleyeceksin. Detaylı uygulama planı repo içinde hazır:

**`.claude/outputs/2026-08-19-14-07-external-projects-prebuild-plan.md`**

Önce bu planı BAŞTAN SONA oku. Özellik özeti: Ayarlar'a LAYERS'ın yanına HARİCİ PROJELER listesi eklenir
(kullanıcı doğrudan proje dizinini verir; VCS kökü yukarı yürünerek bulunur, git/tfvc rozeti otomatik);
Build'de önce bu projeler liste sırasıyla ardışık olarak VCS'ten güncellenir (git: fetch + merge --ff-only,
TFVC: tf vc get) ve değişenler derlenir; dirty olan varsa run hiç başlamaz (console'a net uyarı); sonra
normal ana repo akışı aynen devam eder. Hariciler node panelinde en üstte "External" katman grubu olarak
görünür. Liste boşken davranış bayt-bayt bugünkü gibidir.

## Kod drift uyarısı (önemli)

Plan, bu koddan daha eski bir anlık görüntüye (`d5943c1`) göre yazıldı ve kod o günden beri değişmiş
olabilir. Bu yüzden:

1. **Her faza başlamadan önce o fazın çapalarını güncel kodda doğrula** — plandaki satır numaraları yalnız
   ipucudur; sınıf/metot adına ve tarif edilen davranışa güven. Bir çapa taşınmış/yeniden adlanmışsa planı
   güncel koda uyarlayarak uygula; **davranış spesifikasyonu değişmez**.
2. Plandaki bir varsayım güncel kodla ÇELİŞİYORSA (ör. bahsedilen mekanizma artık farklı çalışıyorsa)
   sessizce doğaçlama yapma: çelişkiyi "plan şunu diyor, kod şunu yapıyor" netliğinde raporla ve kullanıcıya
   sor.
3. Plan tasarım kararlarını (D1–D13) veriyor; bunlar bağlayıcıdır. Görev içi detaylar (yardımcı metot adı,
   dosya bölme vb.) güncel koda göre esnetilebilir.

## Çalışma kuralları (CLAUDE.md geçerli; kritik olanlar)

- **TDD / kırmızı test kuralı:** her davranış için önce KIRMIZI veren test, sonra kod. Plan her görev için
  test adlarını ve neyi pinlediklerini veriyor.
- **Değişmezler:** shell-out MSBuild.exe; OutDir'e asla dokunma; planlama Core'da; stdout yalnız NDJSON;
  **kopya yasak** (yeni literal/metin tek kaynakta — `ExternalProjectsConventions`, `PlanProgressLines`,
  `VsWhereLocator` bunun için var). Ana repo git açısından salt-okur kalır; yeni mutasyon yüzeyi YALNIZ
  `Core/Externals` içinde yaşar ve kaynak guard'ı ile çitlenir (Faz 3.4).
- **UI metinleri/kod İngilizce, kod yorumları Türkçe.** Yeni renk/ikon/token ekleme; mevcut Ds.* stilleri
  ve token'ları kullan (guard testleri kırmızıya döner yoksa).
- Yeni XAML kökü/şablonu → realize testi. Davranış pinleyen eski bir test bilerek değişiyorsa gevşetme,
  yeni kuralı pinleyecek şekilde yeniden yaz.
- **Doküman aynı işte güncellenir** — planın "Doküman güncelleme listesi" bölümünü ilgili fazın
  commit'lerinde işle (anlatı üslubu; changelog dili yazma).

## Yürütme düzeni

1. `main`'den bir çalışma branch'i aç (ör. `feature/external-projects`).
2. Fazları sırayla uygula (1 → 5); **görev başına commit** at. Fazlar bilinçli olarak bağımsız
   commit'lenebilir görevlere bölündü — `superpowers:executing-plans` / `superpowers:subagent-driven-development`
   akışına uygun.
3. Her fazın sonunda tam süiti koş: `dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj
   --filter "Category!=Acceptance"` — mevcut testler DEĞİŞMEDEN yeşil kalmalı (plan hangi testlerin
   dokunulmadan yeşil kalması gerektiğini belirtiyor). Uygulama açıkken build alma (Supervisor kendi
   binary'lerini kilitler).
4. İş bitince planın "Kabul / doğrulama senaryoları" bölümünü uygula (gerçek bir git harici + mümkünse bir
   TFVC harici ile manuel duman testi dahil), tam süit yeşilken `main`'e merge + push et; merge'ü
   doğruladıktan sonra branch'i local + remote sil. Oturum `main` üzerinde biter.

## Oturum ve token ekonomisi

- **Her fazı ayrı oturumda uygula.** Faz bitince CLAUDE.md'deki "aşamamızı kaydet" tetikleyicisiyle
  özet + handoff bırak; sonraki oturumu "kaldığımız yerden devam et" ile aç. Tek dev oturumda context
  şişirme.
- **Görev sırasında yalnız ilgili test sınıfını filtreli koş**
  (`dotnet test ... --filter "FullyQualifiedName~<TestSınıfı>"`); tam süit yalnız faz sonunda koşulur.
- **Görevleri subagent'lara delege et** (superpowers:subagent-driven-development) — ana context'i plan
  ve koordinasyon için yalın tut; dosya keşfini subagent'lar yapsın.

## Kapsam sınırı

- Plandaki işin dışına çıkma (gold-plating yok): per-harici configuration override, server workspace
  desteği, harici node'a özel görsel süsleme vb. YOK — plan ne diyorsa o.
- Belirsizlik ya da plan-kod çelişkisi görürsen durup sor; tahminle ilerleme.
