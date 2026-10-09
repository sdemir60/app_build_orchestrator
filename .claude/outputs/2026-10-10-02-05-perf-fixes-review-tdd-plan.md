# TDD dökümü — gece taraması düzeltmelerinin inceleme bulguları

**Kaynak:** `541f16fa..7e056259` için bağımsız kod incelemesi (2026-10-10 ~02:00). Hüküm: "with fixes". Sıra bloklayıcı → önemli → kozmetik.

| # | Öncelik | Bulgu | Kırmızı test | Düzeltme |
|---|---|---|---|---|
| R1 | önemli | `EngineEventPump`: bayrak `Volatile.Write(0)` ile sıfırlanıp kuyruk yeniden okunuyor — x64'te mağaza-yükleme sıralaması kayıp uyandırmaya izin verir (olay kuyrukta, boşaltıcı yok) | **Deterministik kırmızı yok** (işlemci bellek modeli yarışı). Pompa birim testleri gözlenen sözleşmeyi pinler | `Interlocked.Exchange(ref flag, 0)` (tam bariyer) + gerekçe yorumu |
| R2 | önemli | `EngineEventBurstTests` ortak fikstürü (`NewWithProjects`) ve `OsysProjectCount`'u kopyalıyor | — (test düzeni) | fikstür + sabit kullanılır; `UiResponsivenessBudgetTests.Topology` eski görünürlüğüne döner |
| R3 | önemli | Motor çıkışı (`Dispatcher.Invoke`, Send) kuyruktaki olayların ÖNÜNE geçer; geç gelen `runStarted` çıkıştan sonra uygulanırsa UI "Running"de kilitlenir (eski yarış, pompa pencereyi uzattı) | çıkış, ondan önce gönderilmiş olaylardan sonra uygulanmalı (bugün kırmızı) | çıkış işleyicisi önce pompayı boşaltır (`DrainNow`) |
| R4 | kozmetik | Beads: SnapshotAndReplace ile değiştirilen eski sönüşün `Completed`'ı hâlâ ateşlenir → bitir–başla–bitir 640 ms içinde olursa ikinci sönüş yarıda kesilir | ilk sönüşün sonu geçtikten sonra ikinci sönüş sürerken yörünge görünür kalmalı | düğüm başına sönüş nesli; yalnız son sönüş emekliye ayırır |
| R5 | kozmetik (eski) | Spin-down penceresinde panel boyutu değişirse saat yeniden kurulur ama zamanlayıcı kurulmaz → boş saat bir sonraki koşuya dek döner | boyut değişiminden sonra saat kendiliğinden bırakılmalı | canlı yörünge yoksa spin-down yeniden kurulur |
| R6 | test | `Assert.False(Spins(a))` tik gelmezse boşuna geçer | — | a ve b aynı pompa penceresinde ölçülür (b'nin dönmesi tikin geldiğini kanıtlar) |
| R7 | test | Patlama testi 177 olayın 8 ms'yi aşmasına dayanıyor | — | deterministik pompa birim testleri: yavaş işleyici + girdi işareti, fırlatan işleyici, çok üretici sırası |
| R8 | doküman | §12.1 "en fazla 8 ms" ve "tek olay asla gecikmez" kesin değil; pompa yorumu istisnanın dispatcher'a yükseldiğini söylüyor (InvokeAsync'te göreve düşer); kod haritasında `DecorativeClock.Detach` yok; üç yorum eski mekanizmayı anlatıyor | — | yerinde yeniden yazım |

Düşünülüp uygulanmayan: Background devamının girdi altında uzun süre beklemesine karşı öncelik yükseltme — sürekli sürüklemede bile fare hareketleri arasında boşluk var, ölçülmüş bir sorun yok (YAGNI). Atlanan satırların birkaç karede uygulanmasının "dalga" gibi görünmesi — graf statüsü 200 ms'lik tick'le itilir, 184 atlanma ~200 ms içinde biter; canlı ekranda kontrol edilecek.
