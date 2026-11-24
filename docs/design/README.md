# Tasarım paketi

Build Orchestrator'ın UI tasarımı **yaşayan bir prototiptir**: uygulamanın bütün ekranları, durumları, kopya metinleri ve hareketleri tarayıcıda çalışan bir HTML/React prototipinde ve onun spesifikasyonunda (handoff README) durur, [design system](../design-system/README.md) ile çizilir. Bu dosya paketin nerede olduğunu, nasıl açıldığını, bir sürümün nasıl yapılıp koda geçirildiğini söyler. Her sürümün getirdiği [VERSIONS.md](VERSIONS.md)'dedir.

**2.0.0 milattır.** Tasarım ve design system uygulamanın kodundan (uygulama 1.15.0) sıfırdan yeniden kuruldu; prototip o gün yayındaki uygulamayı gösterir. Bundan sonra tasarım elle değişir: her değişiklik yeni bir tasarım sürümüdür (2.1.0 …) ve kod tarafı sürümleri sırayla uygular. Önceki 1.x sürümleri başka bir Claude hesabında üretildi ve yalnız tarihtir: `.claude/outputs/<tarih>-design-v1.x/` altında arşivde durur, 2.x onların numarasını sürdürmez.

| Ne | Nerede |
|---|---|
| Prototip, kullanmak için | her sürüm diskten açılan tek bir dosya olarak [prototype/](prototype/README.md) altında; Claude artifact'i *Build Orchestrator - Design* en güncelini gösterir |
| Güncel spesifikasyon | son paketin README'si: [packages/2026-11-23-16-07-design-v2.0.0/README.md](packages/2026-11-23-16-07-design-v2.0.0/README.md) |
| Sürüm sürüm paketler | [packages/](packages/README.md): her sürümün spesifikasyonu (README) ve prototip kaynağı |
| Çizildiği design system | [docs/design-system](../design-system/README.md); Claude artifact'i *Build Orchestrator - Design System* gezilebilir kopyasıdır |
| Her sürümün getirdiği ve uygulamaya geçip geçmediği | [VERSIONS.md](VERSIONS.md), en yenisi en üstte |
| Design system'in sürümleri | [docs/design-system/VERSIONS.md](../design-system/VERSIONS.md) |
| Repo dışında yapılan bir değişiklik | [requests/](requests/README.md) |

Örneklerdeki proje ve yol adları temsilidir (`ACME.*`, `Acme.sln`, `D:\src\ACME`): repo herkese açıktır, iç ürün ve müşteri adı yazılmaz. Firma logosu örnek olarak durur.

## Açmak

- **Bir sürümü görmek:** [prototype/](prototype/README.md) altındaki `BuildOrchestrator-prototype-<sürüm>.html` dosyasına çift tıkla (Edge veya Chrome). Kurulum, sunucu ve ağ gerekmez; React, design system, örnek veri ve logolar dosyanın içindedir (Geist fontları çevrimdışıyken sistem fontuna düşer). Üstteki sahne şeridi (Hero, Detail, Failure, Up to date, First run, Ready, Graph focus, Cycle) ve Demo anahtarları prototip iskelesidir, uygulamada yoktur. Yalnız-demo durumlara (motorun düşmesi, kirli çalışma ağacı vb.) tarayıcı konsolundan `boDemo` ile ulaşılır.
- **Kaynağı açmak:** bir paketin `prototype/` klasörü; `Build Orchestrator.dc.html` Claude'un tasarım tuvalinde çalışır, `app/BuildApp.jsx` bütün UI davranışının referans kodudur.

## Tasarım sürümü yapmak

1. Değişikliği prototipte (`app/BuildApp.jsx`, simülasyon `app/build-data.js`) ve spesifikasyonda (ilgili bölümler yerinde yeniden yazılır) yap. Görünüş değişiyorsa önce [design system](../design-system/README.md)'de bir sürüm yapılır.
2. Sürümü artır: yeni özellik, davranış veya ekran **minor**; küçük düzeltme **patch**; **major** yalnız proje sahibinin kararıyla (milat). Prototipteki `DESIGN_VERSION` aynı değere çekilir (UI'da görünmez; About ve What's new uygulamanın sürümünü gösterir).
3. Paketi yaz: `packages/<YYYY-MM-DD-HH-MM>-design-v<sürüm>/` (`README.md` = spesifikasyon, `prototype/` = kaynak) ve `prototype/BuildOrchestrator-prototype-<sürüm>.html` (tek dosya). Yazılmış bir paket değiştirilmez; düzeltme yeni bir sürümdür.
4. [VERSIONS.md](VERSIONS.md)'nin en üstüne kaydı yaz: başlık; paket · prototip satırı; neden; ne değişti (§ referanslarıyla); **uygulamaya ne düşüyor**; altına `Uygulandı: —`.
5. Claude'daki iki artifact'i (Design, Design System) aynı sürümle güncelle.

## Koda geçirmek

- Kod tarafı (Claude Code, `develop`'tan açılan branch) [VERSIONS.md](VERSIONS.md)'de `Uygulandı: —` olan en eski sürümden başlar; o sürümün paketindeki README'yi ve `prototype/` kaynağını referans alır, sürümleri sırayla uygular.
- Commit ve merge mesajları uygulanan sürümü anar (`design v2.1.0 §2.4`); bitince kaydın `Uygulandı:` satırı uygulamanın sürümüyle doldurulur (`Uygulandı: app 1.16.0`).
- Uygulama tasarımda olmayan bir şey eklerse ya da değiştirirse (sahada düzeltme, yeni ayar), bir sonraki tasarım sürümü önce prototipi koddan eşitler; kaynak her zaman koddur, tasarım geride kalmaz.
- Repo dışında yapılan bir tasarım değişikliği (başka bir hesaptaki artifact, bir taslak, kullanım sonrası not) [requests/](requests/README.md) altına girer.
