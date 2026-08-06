# Quiet Graph — uygulama planı (design v1.3.0 §2.3)

> **Otorite:** `.claude/outputs/2026-08-05-01-26-design-v1.3.0/README.md` §2.3 + §3.3 + §8.
> Prototip: aynı klasörde `prototype/` ve `Build Orchestrator (standalone).html`.
> **Çelişkide tasarım kazanır.** Bu belge "koddan oraya nasıl gidilir"i anlatır, tasarımı tekrarlamaz.
>
> **Öncülü:** `main` @ `c6535eb` (v1 "sinema modu" merge edildi). Bu plan onun büyük kısmını **söker**.

---

## 0. Tasarımın önceki kararlarımızı EZDİĞİ üç yer

Bu oturumda konuşurken verdiğimiz üç karar, v1.3.0 ile değişiyor. Kayda geçsin:

| Konuştuğumuz | v1.3.0 §2.3/§3.3 | Geçerli olan |
|---|---|---|
| Koşarken kamera **hiç** hareket etmez | Seçim her yerde senkron; graf düğümü komşularıyla panele **sığdırıp ortalar** — faz ayrımı yok | **Tasarım.** Kamera yalnız *kendiliğinden* durgun; kullanıcı seçince koşarken de hareket eder |
| Tıklama = yalnız **bağımlılıklar** | Odak kümesi = node + **deps + dependents** | **Tasarım** |
| Koşarken hiçbir hareket yok | **Beads** (node çevresinde dolanan amber noktalar) + açılış dalgası | **Tasarım.** Hareket var ama *yerel* ve kameraya dokunmuyor |

Kalan her şeyde iki belge aynı yöne bakıyor: kalıcı çizgi ağı yok, node üstü ad yok, panel kalıyor.

---

## 1. Ne sökülüyor, ne kalıyor

**Sökülüyor (v1'den):**
- Kenar sisi (`EdgeStyleResolver.FogFinishedOpacity`, `fogged` parametresi ve kablajı)
- Cepheyi izleyen kamera (`FrontierScale`, `FollowMinScale/MaxScale`, `FrontierMarginX/Y`, `ShouldRescale`, `_previousScale`)
- Takip dönüşü + `FOLLOW PAUSED` pili (`FollowResumeDelayMs`, timer, `TryResumeFollow`, `ResumeFollowNow`, pil XAML'i, `InteractionText.GraphFollowPaused`, `AccessibilityNames.GraphFollowPill`)
- Node üstü ad etiketleri ve etiket LOD'u (`LabelsFit`, `EnsureLabel`, `ApplyLabelVisibility`, `_labelWidths`, odak muafiyeti)
- Kalıcı kenar ağı (kurulumda 1214 kenarın inşası)
- Graf içi dep-issue rozeti (`EnsureBadge` — dep bilgisi kartlarda yaşıyor)
- Sabit yerleşim sabitleri (`NodeSize` 26, `NodeCellWidth`, `MinNodeSpacing`, `CanvasWidth` 880 tabanı)

**Kalıyor:**
- Kamera transform'u, `ClampPan`, `Pan`, `ZoomAt`, manuel mod, sürükleme/wheel jestleri, imleç
- Culling (yakınlaşınca hâlâ işe yarar; zoom 1'de her şey ekranda olduğu için no-op)
- Tooltip altyapısı (**artık ana isim yolu**), erişilebilirlik adları, seçim kablosu
  (`MainWindow.xaml.cs:214` / `:584` — liste ↔ graf, yeniden kurulmayacak)
- `GraphRealizationPerfTests` ölçüm altyapısı

---

## 2. Fazlar

### F0 — Görünmez panel bug'ı (bağımsız, önce)
`MainWindow.xaml.cs:552` `SetGraph`'ı view mode'a bakmadan çağırıyor; `ShellRoot.xaml.cs:191` paneli yalnız
`Collapsed` yapıyor. Sonuç: panel gizliyken de `UpdateStatuses` → `ApplyEdgeStyles` her 200 ms'de 1214
kenar üzerinde koşuyor. **Bu planın hiçbir kararını beklemez**, ölçümü de kirletiyor. Panel görünmezken
tick başına iş atlanır + regresyon testi.

### F1 — Yerleşim: otomatik pitch + isimsiz mini node
**En büyük tek değişiklik; tek başına "her şey sığıyor + sakin" hedefini teslim eder.**
- Pitch taraması 44→5 px, 0.5 adım; tüm bantlar + bant boşlukları (0.7×pitch) panel yüksekliğine sığan
  İLK değer. Graf her panel boyutunda tam sığar, scrollbar yok.
- **Yapısal sonuç:** yerleşim artık panel boyutuna bağlı ⇒ `SizeChanged`'de yeniden hesaplanmalı. Bugün
  yerleşim `SetGraph`'ta bir kez hesaplanıyor. Bu, `GraphLayout`'un sözleşmesini değiştirir.
- Bant sırası **build-order**: layer 0 üstte, bant içi de build-order (soldan sağa).
- Eksik son satır yatay ortalanır; blok panelde ortalanır (12 px kenar payı, hesap alanı W−24 × H−24).
- Node = kare, boyut = pitch×0.6, **8–24 px kelepçe**, radius-sm, 1.5 px border, içinde `box` glyph
  (node'un %52'si, 1.8 px stroke).
- Node üstü adlar, kalıcı çizgi ağı, dep-issue rozeti **kaldırılır**.

### F2 — Koşu yaşam döngüsü (opaklık sistemi)
- idle/boot/sync: tümü tam opak
- running: queued/discovered **0.13**; yalnız derlenenler tam opak
- biten: sonuç renginde **2400 ms tam opak kalır**, sonra **700 ms'de 0.2'ye** söner
  (tasarımın hilesi: değer anında yazılır, bekleme `transition-delay` ile taşınır — WPF karşılığı
  `BeginTime` + `KeyTime`, **timer yok**)
- done/stopped: tümü sonuç renginde tam opak
- Renk geçişleri 380 ms, opaklık 280 ms (hold-fade hariç)

### F3 — Beads (building animasyonu)
Node'un 2.8 px dışında, eş-merkezli yuvarlatılmış-kare yörüngede dolanan sık amber noktalar; dash deseni
çevreye tam bölünür, 4200 ms linear sonsuz. Giriş 420 ms / çıkış 640 ms opaklık — noktalar **dönerken**
söner. `prefers-reduced-motion`: tamamen kapalı. Mevcut "nabız" bunun yerine geçer.

### F4 — Hover + seçim (odakla & sığdır)
- Hover: node scale **1.7** (120 ms), border 2 px, opacity 1 (soluk moddayken bile), öne gelir.
  Tooltip **gecikmesiz**, TAM proje adı, panel kenarına 6 px kelepçeli, **ekran koordinatında**
  (zoom/pan transform'undan bağımsız).
- Seçim: node + deps + **dependents** sınır kutusu panele sığdırılır → zoom = min(W/bw, H/bh),
  **0.7–2.6** kelepçe (padding = 3×node + 48 px), 460 ms ease-in-out. Pan **ve** zoom ayarlanır.
- Odak dışı her şey opacity **0.1**; seçilide 2 px focus-ring (offset 2).
- **Bağımlılık çizgileri YALNIZ burada:** deps→node ve node→dependents, dikey kübik bezier, amber akan
  kesikler (dasharray 4 8 → offset −24, 640 ms linear, 1.2 px, opacity 0.75).
- Seçili node altında 6 px boşlukla ad etiketi (mono 10, amber, kelepçeli, ekran koordinatında).
- Aynı node'a tekrar / boş alana tıkla → bırakılır, varsayılana döner (zoom 1, pan 0, 460 ms).
  Seçim değişince hover temizlenir.
- Sağ altta mono ipucu: `scroll = zoom · drag = pan`, seçiliyken `click again to release`.

### F5 — Serbest gezinme + açılış dalgası
- Wheel zoom **0.7–5.0**, çarpan 1.14/adım, imleç altındaki nokta sabit, 160 ms
- Boş alanda sürükle = pan, grab/grabbing imleç, ≤3 px tıklama sayılır, sürüklerken kamera transition
  kapalı (birebir takip)
- Açılış (Sync sonrası): build-order index × 9 ms gecikme (max 520 ms), fade + 5 px yukarıdan; dalga
  üstten alta / soldan sağa

### F6 — Doküman + süit + gözle doğrulama
`ARCHITECTURE.md` §13.6/§14.5/§20/§22 + `README.md`. Tam süit. Gerçek OSYS'te gözle kontrol.

---

## 3. İlk iş: TEST ENVANTERİ

~4.600 satır test, sökülecek davranışı pinliyor. CLAUDE.md gereği hiçbiri sessizce silinmez.
**Uygulama başlamadan önce** her test dosyası üç kutudan birine konur:

| Kutu | Ne olur |
|---|---|
| **Yaşar** | Kural değişmedi: culling, tooltip, erişilebilirlik, seçim kablosu, `ClampPan`/`Pan`/`ZoomAt` |
| **Yeniden yazılır** | Kural bilerek değişti: yerleşim, kamera hedefi, kenar yaşam döngüsü — doc'una eski iddia + gerekçe (bu belge + design v1.3.0 §9) |
| **Gerekçesiyle silinir** | Özellik tamamen kalktı: takip dönüşü, `FOLLOW PAUSED` pili, sis, etiket LOD'u, dep rozeti — gerekçe commit mesajına |

Bu envanter yapılmadan iş büyüklüğü bilinemez.

---

## 4. Riskler

1. **Yerleşim artık panel boyutuna bağlı.** `SizeChanged` → yeniden pitch → yeniden konum. Bugünkü
   "yerleşim `SetGraph`'ta bir kez" varsayımını kıran tek değişiklik bu; culling ve kamera kelepçesi de
   ona bağlı. **En yüksek regresyon riski burada.**
2. **8 px node.** 100+ projede pitch tabana yaklaşır. Üç opaklık kademesi (0.13 / tam / 0.2) 8 px'lik bir
   karede ayırt edilebilir mi — **gözle bakılacak**.
3. **Beads maliyeti.** Paralel derlemede N adet sonsuz `stroke-dashoffset` animasyonu. WPF'te bu
   `DoubleAnimation` + `StrokeDashOffset`; N büyükse ölçülmeli. Tek ortak `ClockGroup` (v1'de kurulan
   desen) burada da kullanılmalı.
4. **Hold-fade WPF karşılığı.** CSS `transition-delay` hilesinin WPF eşi `BeginTime`'lı bir animasyon;
   her biten proje için bir animasyon ⇒ 177 proje = 177 animasyon. Ölçülmeli; gerekirse tek bir
   zamanlayıcı yerine storyboard havuzu.
5. **Ekran-koordinatlı tooltip/etiket.** Zoom/pan transform'unun dışında konumlanmalı — WPF'te bu
   `Popup` ya da transform'suz bir overlay katmanı demek.

## 5. Başarı ölçütü

| Ölçüt | Bugün | Hedef |
|---|---|---|
| Kadraj dışında kalan graf | ~1/3 | **0** — her panel boyutunda tam sığar |
| Boştaki kenar görseli | 1214 | **0** |
| Panel görünmezken tick başına iş | 1214 kenar × 200 ms | **0** |
| Koşarken kendiliğinden kamera hareketi | var | **yok** |
| Süit | 1904 passed / 0 failed | Aynı veya daha iyi, envanter uygulanmış |

## 6. Kapsam dışı

Diğer paneller (§2.4–§2.10 değişmedi) · seçim modelinin kendisi (§3.3 aynı) · panel başlığı ·
graf düğümlerinin klavye erişilebilirliği · süit hijyeni (ayrı iş) · v1 backlog'unun kalanı.
