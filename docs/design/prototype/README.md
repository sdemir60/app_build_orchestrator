# Prototip

Prototipin kaynağı bu klasördedir; tek dosyalık hâli buradan, lokalde üretilir ve repoya girmez.

| Dosya | Ne |
|---|---|
| `app/BuildApp.jsx` | bütün UI davranışının referans kodu; `DESIGN_VERSION` tasarım sürümüdür |
| `app/build-data.js` | simülasyon motoru ve örnek graf (`ACME.*`) |
| `app/release-notes.js` | What's new verisi; `npm run build` uygulamanın `CHANGELOG.md`'sinden üretir, elle değiştirilmez |
| `Main.dc.html` | Claude'un tasarım tuvalindeki giriş sayfası (sahne şeridi, Demo anahtarları); sahne listesi tek dosyaya da buradan gider |
| `standalone.html`, `build.mjs`, `package.json` | tek dosyanın kabuğu ve onu üreten betik |

## Üretmek

Node gerekir. Bu klasörde:

```powershell
npm install
npm run build
```

Build önce `app/release-notes.js`'i `CHANGELOG.md`'den yeniler (değiştiyse commit'lenir; tuval de onu yükler). Çıkan `release/BuildOrchestrator-prototype-<sürüm>.html` (git-ignore'lu) çift tıklanınca açılır (Edge veya Chrome): kurulum, sunucu ve ağ gerekmez; React, [design system](../../design-system/README.md) (doğrudan `docs/design-system`'dan), simülasyon, örnek graf ve logolar dosyanın içindedir (Geist fontları çevrimdışıyken sistem fontuna düşer). Üstte sahne şeridi ve Demo anahtarları (auto-play, update pill, reduced parallelism) durur. Eski bir sürüm, [VERSIONS.md](../VERSIONS.md)'deki commit açılıp aynı komutla üretilir.
