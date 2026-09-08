# Açılış koreografisi: "pat diye yanıyor, bazen yanmıyor"

Şikâyet: *"build deyince çok estetik geçiş yaptık, pat diye bazen yanıyor bazen yanmıyor. Orada animasyonlu
amber derlenecek node'lar geliyor, sonra kararma geçişi var, çok estetik yumuşak."*

İki ayrı kusur vardı ve ikisi de gerçekti.

---

## 1. "Pat diye" — renk geçişi hiç yoktu ★

Prototipte dalga sırasında her yüzey **200 ms `ease-standard`** ile geçer ve gecikme düğüm başınadır
(`BuildApp.jsx:529` node border+zemin, `:533` küp, `:682` satır şeridi, `:693` nokta). Uygulamada bu geçiş
**hiç yoktu** — renkler anında oturuyordu, yani dalga bir animasyon değil bir sıçrama dizisiydi.

Sebebi bir *ölçülmüş sapma*ydı ve gerekçesi doğruydu ama **kapsamı yanlıştı**: WPF'te fırça DP'si
interpolate edilemez, geçiş yüzey başına yerel bir `SolidColorBrush` + `ColorAnimation` demek, ve 177
projenin statüsünün **tek tick'te** değiştiği durumda (koşu başlangıcı) 531 fırça + 531 animasyon UI olay
bütçesini (50 ms) aşıyor.

Dalga tam **tersi** durumdur: tempo 110 ms/node (36 projede ~31 ms), yani tick başına bir-iki düğüm.

**Düzeltme:** geçiş yalnız koreografi oynarken açık. Dalganın dokunduğu her yüzey — node çerçevesi, zemini,
içindeki küp, satırın şeridi ve noktası — tek bir fonksiyondan geçiyor
(`MotionTokens.TransitionTokenBrush`); yüzey dalga boyunca kendi kopyasına devrediyor ve dalga bitince
**paylaşılan token fırçasına geri veriliyor**, yani referansı kalıcı kaybetmiyor. Bütçenin ölçüldüğü toplu
yol hiç değişmedi.

**Test:** `The_wave_fades_a_node_into_amber_instead_of_snapping_it` ·
`Outside_the_choreography_a_node_keeps_the_shared_token_brush` ·
`The_wave_fades_the_row_stripe_into_amber_too`

---

## 2. "Bazen yanıyor bazen yanmıyor" — koreografi planlama süresine bağlıydı ★

Önceki raporda §3.1'de kendi kararım olarak bildirmiştim: koreografiyi motorun **planlama penceresiyle
örtüştürmüştüm** (komut anında gider, koreografi `Starting` üzerinde oynar, `runStarted` onu keser).
Gerekçem "gerçek bir derlemeyi animasyon için 3 sn geciktirmek savunulabilir değil" idi.

Ölçülen bedel senin gördüğün şey: **planlama koreografiden kısa sürdüğünde** (sıcak repo, worktree yok)
`runStarted` dalgayı ortasında kesiyor, uzun sürdüğünde kesmiyor. Aynı tıklama bazen animasyonlu, bazen
anlık. Bir koreografi ya **her zaman** oynar ya hiç.

**Düzeltme:** koşu komutu koreografi **bittikten sonra** gidiyor — prototipin borusundaki son adımın ta
kendisi (`_mark(scope, () => startRun())`). İşlem yine de ilk karede başlıyor: pill amber yanıyor, buton
`Stop`'a dönüyor, konsola istek satırı düşüyor; bekleyen **yalnız komut**. Kapsamı VM biliyor, zamanlamayı
kabuk sürüyor — VM bir kapıyı bekliyor, o kadar.

**Bedeli:** gerçek derleme koreografi kadar geç başlıyor (kapsama göre ~2.9–4.0 sn). Tasarımın kendi kabulü
bu; senin "çok estetik geçiş yaptık" dediğin dizi ancak böyle her seferinde görünüyor.

**Yan kazanç:** marking fazında Stop artık tasarımın dediğini gerçekten yapıyor — koşu **hiç başlamıyor**,
motora ne `startRun` ne `stopRun` gidiyor, konsola `Cancelled — build not started` düşüyor. Önceki
uygulamada bu imkânsızdı çünkü komut çoktan gitmişti (o raporda §3.2 olarak bildirmiştim).

**Test:** `The_run_command_waits_until_the_opening_choreography_has_finished` ·
`Stopping_during_the_choreography_cancels_the_run_before_it_is_sent`

---

## 3. Bir kare kaçağı

Koşu istendiği anda graf koşu fazına girip her düğümü 0.13'e söndürmeye başlıyor. Koreografi opaklık
kararını **ezer** — ama ilk adımı bir tick sonra başladığı için o tek karede ezecek kimse yoktu: ekran önce
sönmeye başlıyor, sonra koreografi geri çekiyordu. Küçük ama gerçek bir kırpış.

**Düzeltme:** `t=0`'a düşen adımlar senkron koşuyor. Aynı gerekçe bitiş koreografisi için de geçerli (aynı
oynatıcı).

**Test:** `The_first_step_runs_synchronously_so_no_frame_escapes_the_choreography`

---

## Doğrulanan, dokunulmayanlar

Prototiple birebir eşleştiği doğrulandı, değiştirilmedi:

| Ne | Değer |
|---|---|
| Adım çizelgesi | `wave 440` · `hold 440+W` · `dimenv 740+W` · `settle 1300+W` · `wait 1860+W` · `wait2 2280+W` · koşu `2520+W` |
| Dalga temposu | `min(110, 1100/(n-1))` ms/node, sıra **random** (mulberry32) |
| İşaretli node/satır | opaklık **0.45**, 440 ms `ease-in-out` |
| Kapsam dışı node | opaklık **0.18**, 1120 ms · kapsam dışı **satır** 0.3, 1120 ms |
| Boşta | 360 ms `ease-in-out` |
| Bitiş (neon) | `hold 900` → `neon 1150` (zincir ≤1.5 s) → `bwait 700` → `grey 980`, yalnız grafta |

> Tasarım README'sindeki nesir "griler 0.13'e, sarılar 0.55'e" diyor; **prototip** 0.18 / 0.45 / 0.3
> kullanıyor. Belgenin "Fidelity: prototip bağlayıcıdır" kuralı gereği prototip alındı.

---

## Sende kontrol edilecek tek şey

Uygulamanın **tüm** animasyonu Windows'un "animasyon efektleri" ayarına bağlı
(`App.Motion.AnimationsEnabled` → *Ayarlar › Erişilebilirlik › Görsel efektler › Animasyon efektleri*).
Kapalıysa koreografi hiç oynamaz — tasarımın `prefers-reduced-motion` karşılığı budur. Açık olduğundan emin
ol; kapalıysa gördüğün "hiç yanmıyor" bundandır.

## Hâlâ karara bağlı

Bitiş koreografisi reduced-motion'da **hiç** oynamıyor. Tasarım §2.3 orada *"flicker kapalı, opaklık
kademeleri kalır"* diyor, ama uygulamanın genel kuralı (§1.3, guard testleriyle pinli) "tüm süreler 0" —
ve o kademeler ~3.7 sn'ye yayılan zamanlanmış bir dizidir. İstisna açmamı istersen açarım.
