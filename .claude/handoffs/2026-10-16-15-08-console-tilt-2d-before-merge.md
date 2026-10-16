# Aşama girişi — konsol geçişi 2B (1,1 GB bellek düzeltmesi), merge öncesinde ara verildi

**Özetler**
- `.claude/summaries/2026-10-16-13-42-idle-memory-reduction-summary.md`: üç dalga, v1.10.0 yayını, takip listesi.
- Bu iş için ayrı özet yok; kök neden ve kanıt zinciri bellek notu `viewport3d-binds-gpu-memory.md`'de ve commit mesajlarında.

**Nerede kaldık:**
- Branch `fix/console-tilt-2d` (develop `40524b10`'dan), iki commit: `6a28515c` (düzeltme: konsolun proje logu ↔ anlatı geçişi 2B,
  `Viewport3D` + doku kaldırıldı, `IsTransitioning` seam'i; 10 testin 5'i önce kırmızı) ve `dabbddbb` (`NoViewport3DGuardTests`,
  `ClickAwayBlur` yalnız `Visual`, ARCHITECTURE §13.5/§14.5/§20/§22). Ağaç temiz, stash yok.
- Kanıt (4K tam ekran, projeye tıklama): v1.10.0 1.136 MB → düzeltme 294 MB (iki tur sonra 347). Ham veri `.claude/temp/mem-2026-10-13/click*`.
- **Tam süit arka planda koşuyor:** çıktı `.claude/temp/full-suite-tilt2d-2026-10-16-15-07.txt` (sonunda `test exit=` satırı; `Başarılı!`/`Başarısız!` özeti).
- **Kalan (mekanik):** süit yeşilse `develop`'a `--no-ff` merge + push, CI kontrolü, branch silme; oturum `develop`'ta biter.
  Düşen test varsa önce tek başına tekrar (bilinen ortam düşüşleri: UpdatePill DPI, HeadWatcher yük).
- **Sürüm ÇIKARILMAYACAK:** sahip bir sonraki çalışmasıyla birlikte 1.10.1'i kendisi başlatacak. CHANGELOG'a ve Version'a dokunulmadı;
  sürüm notu o zaman yazılır (Fixed: proje logu açılınca bellek sıçraması — 4K'da 1,1 GB; geçiş artık düz kayma + solma).
- Merge mesajı taslağı: `merge: konsolun panel gecisi 2B - 3B duzlem 4K'da ~830 MB grafik bellegi bagliyordu, projeye tiklama 1,1 GB -> ~300 MB; App'te 3B yok guard'i; ARCHITECTURE (fix/console-tilt-2d)`.
- Bilgisayar kapandıktan sonra (15:36) ek doğrulama: 6 farklı proje + tepsi → 376 MB (GPU 251 MB'ta sabit). `develop`'ta bu
  branch'in merge commit'i varsa bu aşama tamamdır.

Buradan devam edilecek.
