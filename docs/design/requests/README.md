# Tasarım istekleri

Repo dışında yapılan bir tasarım değişikliği (başka bir Claude hesabındaki tasarım artifact'i, bir taslak, bir çizim, bir günlük kullanımdan sonra alınan not) buraya tek bir dosya olarak girer; böylece kodda çalışan onu bulur, anlar ve yapar. Uygulamada tasarımda olmayan bir UI değişikliği yapıldığında da (sahada düzeltme, yeni ayar) kod tarafı buraya bir kayıt düşer; bir sonraki tasarım sürümü prototipi buna göre eşitler. Tasarımın kendi sürümleri isteğe gerek duymaz: [VERSIONS.md](../VERSIONS.md)'deki kaydı ne yaptığını söyler.

## Bir istek

`YYYY-MM-DD-<başlık>.md`, başlık kebab-case; yanında aynı adlı bir klasörde değişikliği gösteren her şey: ekran görüntüleri, dışa aktarılmış bir sayfa, bir design system dosyası. Dosya, başlık kullanmadan, şunları söyler:

- **Durum:** `açık`, `sürüyor`, `<sürüm>'de yapıldı` ya da `reddedildi: <neden>`
- **Kaynak:** değişikliğin nerede yapıldığı ya da görüldüğü, kim tarafından
- **Ne değişir:** ekranlar ve durumlar, sözle; ne olduğu gibi kalır
- **Neden:** neyi düzeltir ya da neyi mümkün kılar
- **Gösterildiği yer:** yanındaki dosyalar, her biri bir satır

## Uygulama tarafı nasıl alır

1. Açık istekleri en eskiden başlayarak okur; bir kararı açık bırakan istek için işe başlamadan soru sorar.
2. Tasarımın yaşadığı yerde yapar: görünüş [docs/design-system](../../design-system/README.md)'de, ekranlar ve davranışlar prototipte ve spesifikasyonda.
3. [Tasarım paketinin](../README.md#tasarım-üzerinde-çalışmak) söylediği gibi bir sürüm yapar; VERSIONS.md kaydı isteği anar, isteğin durumu `<sürüm>'de yapıldı` olur.
4. Değişikliğin yanlış bıraktığını günceller (ARCHITECTURE, docs/FEATURES.md) ve davranışı sabitleyen testleri, repo kurallarıyla (CLAUDE.md), `develop`'tan açılan bir branch'te.
