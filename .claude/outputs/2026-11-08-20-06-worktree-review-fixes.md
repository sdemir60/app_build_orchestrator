# Worktree branch'i — inceleme bulgularının TDD dökümü

Kaynak: bağımsız inceleme (feat/build-in-worktree, 12eda105..15c91ba6). Kritik bulgu yok. Her madde kodla doğrulandı;
her düzeltme önce kırmızı testle. Tek tur, sıra aşağıdaki gibi.

## Önemli

| # | Kusur | Kırmızı test | Düzeltme |
|---|---|---|---|
| Ö1 | Detached worktree'ye geçince branch chip'i ve Sync'in yedek branch'i repo kopyasının branch'ini taşıyor | geçişten sonra `vm.Branch == ""` ve `SyncWorkspaceCommand.Branch == ""` | aktif kopya değişiminde git yüzeyi unutulur (D1) |
| Ö2 | Kartın "diğer kopya" checkout'u git-işlem kapısını aktif kökte soruyor | diğer kopyada `rebase-merge` varken checkout gönderilmez, konsol uyarır | gönderimden önce hedef kopyanın kökü yoklanır |
| Ö3 | Worktree alanı doluyken kökü boşaltıp workspace kapatılamıyor (Save kapalı) | kök "" + klasör dolu → `CanSave`; Save sonrası klasör de düşer | kök boşken worktree denetimi yok; kökü olmayan worktree saklanmaz |
| Ö4 | Uzun `worktree add` 90 sn'de "Restart engine" önerdiriyor | kurulum sürerken 90 sn+ sessizlik uyarı üretmez; kurulumun tavanı + 90 sn aşılınca üretir | kurulum sürerken bekçinin eşiği motorun kendi tavanı + normal eşik |
| Ö5 | `createWorktree`'nin motor tarafı testsiz | Supervisor: boşta kurar ve `worktreeCreated`; koşu uçuşta `worktreeRejected`, klasör yok | yalnız test |

## Küçük

| # | Kusur | Test / düzeltme |
|---|---|---|
| K1 | Kart kararları aktif kopyaya, içerik kartın kopyasına bakıyor; kopya/kök değişince kart açık kalıyor | kopya değişince kart kapanır (D1); kararlar kartın kopyasından |
| K2 | `worktreeFailed` Save'in ertelenen Sync'ini ve plan temizliğini düşürüyor | `worktreeFailed` → başarısız kurulum gibi: satır + Sync; `worktreeRejected` değişmez (test satırı yeniden yazılır) |
| K3 | Save worktree'ye geçince aynı Save'in konsol notları siliniyor | Save'in notları geçiş bölümünün ilk satırları olur |
| K4 | Klasör değişiminde repo'ya dönüş son Sync'i unutmuyor | D1 ile kapanır, testli |
| K5 | "Worktree ready … — detached at <sha>" her durumda doğru değil | metin sha'ya göre, "detached" iddiası yok |
| K6 | Klasör repoyu kapsadığında "must be outside" yanıltıcı | tek doğru metin ("next to the repository, not inside it or around it") |
| K7 | Save kapısı aynı değer için diski iki kez okuyor | (kök, klasör) anahtarlı önbellek |
| K8 | Klasör diskte yoksa ilgisiz her Save worktree'yi yeniden kuruyor | kurulum yalnız alan bu Save'de değiştiyse ya da Settings anahtardan/karttan açıldıysa |
| K9 | Yorum ve kod haritası kayması | yorumlar ve §22 satırı (karar sorguları yazımla birlikte durur — `merge-base` emsali) |
| K10 | `_checkoutRequest` hata ve motor kaybında temizlenmiyor | iki yolda da temizlenir |
| K11 | Porcelain ayrıştırıcı ve kurucunun "zaten worktree" yolu testsiz | saf ayrıştırıcı testi (boşluklu/Türkçe yol, prunable/locked, CRLF); kurucu testi |

## Organizasyon

D1 aktif kök değişiminin yan etkileri tek yerde (`OnActiveCopyChanged`) · D2 yol karşılaştırması `RootScope` ile · D3 yazım
dosyasının zaman aşımları tek tanım · D4 porcelain ayrıştırıcısı saf bir tipe (`WorktreeList`) · D5 `WorkingCopy.Other()` ·
D6 `CanSwitchCopy` = `CanSwitchConfiguration`, yol eşitliği tek yardımcı.

## Yapılmayanlar

- K12 (iki kopya da kirliyken kartın gidip gelmesi): kullanıcının tarif ettiği akış; Cancel her zaman var.
- D7 ("Repository change deferred" iki anlam): doküman böyle anlatıyor, kozmetik.
- K7'nin UNC debounce'u: nadir; önbellek tekrar okumayı keser.
- K11'in `Unreadable` testi: ACL oynatmadan üretilemiyor.
