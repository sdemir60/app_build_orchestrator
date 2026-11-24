# Tasarım

Build Orchestrator'ın UI tasarımı **yaşayan bir prototiptir**: uygulamanın bütün ekranları, durumları, kopya metinleri ve hareketleri tarayıcıda çalışan bir HTML/React prototipinde ve onun spesifikasyonunda durur, [design system](../design-system/README.md) ile çizilir. Bu dosya neyin nerede olduğunu, prototipin nasıl açıldığını, bir sürümün nasıl yapılıp koda geçirildiğini söyler. Her sürümün getirdiği [VERSIONS.md](VERSIONS.md)'dedir.

**2.0.0 milattır.** Tasarım ve design system uygulamanın kodundan (uygulama 1.15.0) sıfırdan yeniden kuruldu; prototip o gün yayındaki uygulamayı gösterir. Bundan sonra tasarım elle değişir: her değişiklik yeni bir tasarım sürümüdür ve kod tarafı sürümleri sırayla uygular. Önceki 1.x sürümleri başka bir Claude hesabında üretildi ve yalnız tarihtir: `.claude/outputs/<tarih>-design-v1.x/` altında arşivde durur, 2.x onların numarasını sürdürmez.

| Ne | Nerede |
|---|---|
| Spesifikasyon | [SPEC.md](SPEC.md): her zaman güncel tasarım |
| Prototipin kaynağı | [prototype/](prototype/README.md): `app/BuildApp.jsx` bütün UI davranışının referans kodudur |
| Prototip, kullanmak için | tek bir HTML dosyası, [prototype/](prototype/README.md) klasöründe lokalde üretilir; Claude artifact'i *Build Orchestrator - Design* en güncelini gösterir |
| Sürümler ve uygulamaya geçip geçmedikleri | [VERSIONS.md](VERSIONS.md), en yenisi en üstte; her sürüm `develop`'ta bir commit'tir |
| Çizildiği design system | [docs/design-system](../design-system/README.md), sürümleri [VERSIONS.md](../design-system/VERSIONS.md)'de; Claude artifact'i *Build Orchestrator - Design System* gezilebilir kopyasıdır |
| Repo dışında yapılan bir değişiklik | [requests/](requests/README.md) |

Örneklerdeki proje ve yol adları temsilidir (`ACME.*`, `Acme.sln`, `D:\src\ACME`): repo herkese açıktır, iç ürün ve müşteri adı yazılmaz. Firma logosu örnek olarak durur.

## Açmak

- **Bir sürümü görmek:** [prototype/](prototype/README.md) klasöründe `npm install`, sonra `npm run build`; çıkan `release/BuildOrchestrator-prototype-<sürüm>.html` dosyasına çift tıkla (Edge veya Chrome). Kurulum, sunucu ve ağ gerekmez; React, design system, örnek veri ve logolar dosyanın içindedir (Geist fontları çevrimdışıyken sistem fontuna düşer). Eski bir sürüm, VERSIONS.md'deki commit açılıp aynı komutla üretilir. Üstteki sahne şeridi (Hero, Detail, Failure, Up to date, First run, Ready, Graph focus, Cycle) ve Demo anahtarları prototip iskelesidir, uygulamada yoktur. Yalnız-demo durumlara (motorun düşmesi, kirli çalışma ağacı vb.) tarayıcı konsolundan `boDemo` ile ulaşılır.
- **Kaynağı açmak:** `prototype/Main.dc.html` Claude'un tasarım tuvalinde çalışır; `prototype/app/BuildApp.jsx` bütün UI davranışının referans kodudur.

## Sürüm yapmak

1. Değişikliği prototipte (`prototype/app/BuildApp.jsx`, simülasyon `prototype/app/build-data.js`) ve [SPEC.md](SPEC.md)'de (ilgili bölümler yerinde yeniden yazılır) yap. Görünüş değişiyorsa önce [design system](../design-system/README.md)'de bir sürüm yapılır.
2. Sürümü artır: yeni özellik, davranış veya ekran **minor**; küçük düzeltme **patch**; **major** yalnız proje sahibinin kararıyla (milat). Prototipteki `DESIGN_VERSION` aynı değere çekilir (UI'da görünmez; About ve What's new uygulamanın sürümünü gösterir).
3. [VERSIONS.md](VERSIONS.md)'nin en üstüne kaydı yaz: başlık; commit · design system satırı; neden; ne değişti (§ referanslarıyla); **uygulamaya ne düşüyor**; altına `Uygulandı: —`. Tablonun en üstüne de satırını ekle (`Uygulandı` hücresi `—`). Sürümün commit'i kayda bir sonraki commit'le yazılır.
4. Tek dosyayı üret, aç ve sahneleri gez.
5. Claude'daki iki artifact'i (Design, Design System) aynı sürümle güncelle.

## Koda geçirmek

- Kod tarafı (Claude Code, `develop`'tan açılan branch) [VERSIONS.md](VERSIONS.md)'de `Uygulandı: —` olan en eski sürümden başlar; o sürümün commit'indeki SPEC.md ve `prototype/` kaynağını referans alır (değişen, o commit'in farkıdır), sürümleri sırayla uygular.
- Commit ve merge mesajları uygulanan sürümü anar (`design v2.1.0 §2.4`). Uygulayan branch kaydın `Uygulandı:` satırını ve tablodaki hücresini `develop` yapar: sonraki kod oturumu `—` olanlardan devam eder. Sürüm çıkarılırken release script'i `develop`'ları uygulamanın sürümüne çevirir (`Uygulandı: app 1.16.0`).
- Uygulama tasarımda olmayan bir şey eklerse ya da değiştirirse (sahada düzeltme, yeni ayar), bir sonraki tasarım sürümü önce prototipi koddan eşitler; kaynak her zaman koddur, tasarım geride kalmaz.
- Repo dışında yapılan bir tasarım değişikliği (başka bir hesaptaki artifact, bir taslak, kullanım sonrası not) [requests/](requests/README.md) altına girer.
