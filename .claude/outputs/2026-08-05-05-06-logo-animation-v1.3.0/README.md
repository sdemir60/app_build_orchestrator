# Build Orchestrator — Logo Animasyonu

Tray ikon modunda, uygulama gizli/minimize iken sağ alt köşede çalışacak logo
animasyonu. İki sürüm var: sade logo ve derleme sayacı taşıyan sürüm.

## Dosyalar

| Dosya | Ne işe yarar |
|---|---|
| `BuildOrchestratorIcon.xaml` | **Sade sürüm.** WPF UserControl + Storyboard. Projeye eklenecek asıl dosya. |
| `BuildOrchestratorIconCounter.xaml` | **Sayaçlı sürüm.** Aynı animasyon, beyaz şeritte `139/248` biçiminde derlenen proje sayacı. |
| `Build Orchestrator Icon Loop.dc.html` | Sade sürümün tarayıcı önizlemesi (referans / onay içindir, projeye gitmez). |
| `Build Orchestrator Tray Indicator.dc.html` | Sayaçlı sürümün önizlemesi; sağ alt köşe bildirim penceresi bağlamıyla birlikte. |
| `uploads/build_orchestrator_icon_ds_amber.svg` | Kaynak ikon (statik). |

## Animasyon — 3 saniye, kesintisiz döngü

**Giriş (0 – 1.34s)**
Amber şevron en solda (X = −170) soluk olarak belirir ve sağa süzülür.
Şevronun siluetini birebir takip eden bir kaplama, geçtiği yerde şeritleri
*silerek* açar — şevron onları seriyormuş gibi görünür. Beş şerit aynı anda
kendi gecikmeleriyle sağa kayar: hepsi şevronun başladığı noktada (x = 30.5)
üst üste durur, oradan yayılırlar. Şevron yerine oturduktan 0.03–0.19 sn sonra
sırayla onlar da oturur.

**Duruş (1.34 – 2.10s)**
Orijinal logo karesi. Sayaçlı sürümde rakamlar bu aralıkta okunur.

**Çıkış (2.10 – 3.00s)**
Şevron ilk hareket eder ve hızlanarak uzaklaşır (+110). Şeritler farklı hız ve
mesafelerle peşinden gider; hepsinin **sağ ucu aynı noktada (x = 250.5)**, yani
şevronun kaybolduğu hizada yok olur. Her parçanın solması 340 ms sürer ve tam
kendi hareketinin bittiği anda 0 olur — damla damla kaybolurlar.

Son şerit 3.000s'de kaybolur, storyboard tam o anda başa döner: boş kare yoktur.

### Zaman çizelgesi (saniye)

| parça | giriş baş. | yerine oturma | çıkış baş. | çıkış sonu | solma | çıkış X |
|---|---|---|---|---|---|---|
| Şevron | 0.100 | 1.150 | 2.100 | 2.800 | 2.46–2.80 | +110 |
| Amber | 0.200 | 1.180 | 2.130 | 2.780 | 2.44–2.78 | +80 |
| Beyaz | 0.260 | 1.220 | 2.175 | 2.840 | 2.50–2.84 | +88 (sayaçlı: +82) |
| Üst koyu | 0.300 | 1.260 | 2.225 | 2.890 | 2.55–2.89 | +142 |
| Gümüş | 0.360 | 1.300 | 2.275 | 2.940 | 2.60–2.94 | +128 |
| Orta koyu | 0.400 | 1.340 | 2.325 | 3.000 | 2.66–3.00 | +160 |

Giriş başlangıç X: Amber −89, Beyaz −72, Üst koyu −43, Gümüş −35, Orta koyu −21.

## Entegrasyon notları

- **Sahne 430 × 286.** Logo ortada, iki yanda hareket payı. Hiçbir yerde kırpma
  sınırı yok; `Viewbox` istediğiniz boyuta ölçekler. Zemin şeffaftır.
- **`ChevronShift.X` ile `SweepShift.X` birebir aynı keyframe ve KeySpline
  değerlerine sahip olmalı.** "Şevron şeritleri siliyor" etkisi tamamen bu
  senkrona bağlıdır; birini değiştirirseniz diğerini de değiştirin.
- Çıkışta kaplama şevrondan daha uzağa gider (260). Şerit yolları uzun olduğu
  için, aksi halde bir şerit kaplama kenarına takılıp kesilirdi.
- Şeritlerin gölgesi kırpmadan **önce** hesaplanmalı: Clip ve Effect ayrı
  `Canvas`'larda duruyor. Aynı elemana konursa süpürme kenarında gölge çizgisi
  oluşur.
- Gölge opaklığı 0.30. Orijinal SVG'de siyah tile üzerine göre 0.92 idi; zemin
  kalktığı için düşürüldü, kendi arka planınıza göre ayarlayın.
- Girişteki hafif yay etkisi `BackEase (Amplitude 0.12)` ile verildi; WPF
  `KeySpline` 0–1 aralığı dışına çıkamadığı için CSS'teki karşılığı birebir
  aktarılamıyor, görsel olarak eşdeğerdir.
- **WinUI 3 / UWP:** `PathGeometry.Transform` animasyonu desteklenmez. O tarafta
  kaplamayı `CompositionGeometricClip` + `ScalarKeyFrameAnimation` ile kurmak
  gerekir; zamanlama tablosu aynen geçerlidir.

### Sayaç (yalnız `BuildOrchestratorIconCounter.xaml`)

```csharp
CountText.Text = $"{done}/{total}";   // ör. "139/248"
```

Beyaz şerit 3 haneli sayılar için 60 → 66 birime genişletildi, çıkış mesafesi
buna göre 88 → 82 olarak yeniden hesaplandı. Yazı `#5A5A63` %72 opaklıkta,
kabartma görünümü tek bir beyaz `DropShadowEffect` ile (aşağı 0.7 px, blur 0).

## Sürüm geçmişi

**v1.3** — Sayaçlı sürüm ayrı dosya olarak eklendi. Diğer şeritlerdeki yazılar
(süre, başarılı/başarısız sayısı) kaldırıldı; yalnız beyaz şeritte proje sayacı
kaldı, yazı yumuşatıldı.

**v1.2** — Döngü 3.4s → 3s. Sondaki boş kare kaldırıldı: son şerit kaybolduğu
anda şevron soldan tekrar giriyor. Hareket zamanlamaları değişmedi.

**v1.1** — Çıkış mesafeleri, her parçanın sağ ucu aynı noktada (x = 250.5) yok
olacak şekilde yeniden hesaplandı. Her parçanın solması kendi bitiş anına
sabitlendi (340 ms, farklı başlangıçlar).

**v1.0** — Zemin/tile kaldırıldı, kırpma sınırları kaldırıldı. Giriş şevron
siluetli silme kaplamasıyla, çıkış sürükleme hissiyatıyla kuruldu.
