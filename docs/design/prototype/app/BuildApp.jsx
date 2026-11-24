/* Delta Build Orchestrator — ana uygulama (DS bileşenleriyle kompoze edilir)
   window.BuildApp olarak yayınlanır; Build Orchestrator.dc.html mount eder. */
/* global React */

let BO = window.DELTA_BO;
const REDUCED = typeof matchMedia !== 'undefined' && matchMedia('(prefers-reduced-motion: reduce)').matches;

/* ---------- bir kez enjekte edilen keyframe/scrollbar CSS ---------- */
(function () {
  if (document.getElementById('bo-app-css')) return;
  const s = document.createElement('style');
  s.id = 'bo-app-css';
  s.textContent = `
  @media (prefers-reduced-motion: no-preference) {
    .bo-reveal { animation: bo-reveal .3s var(--ease-out) both; }
    .bo-cursor { animation: bo-blink 1.1s var(--ease-in-out) infinite, bo-hop 6.6s linear infinite; animation-delay: 0s, -0.55s; }
    .bo-cursor-still { animation: bo-blink 1.1s var(--ease-in-out) infinite; }
    .bo-rot { animation: bo-rot 1.4s linear infinite; transform-origin: center; transform-box: fill-box; }
    .bo-shake { animation: bo-shake .36s var(--ease-standard) 1; }
    .bo-glow-once { animation: bo-glow-once 1.1s var(--ease-out) 1; }
    .bo-edge-flow { stroke-dasharray: 4 8; animation: bo-dash .64s linear infinite; }
    .bo-pop-in { animation: bo-pop-in .14s var(--ease-out) both; }
    .bo-slide-up { animation: bo-slide-up .34s var(--ease-out) both; }
    .bo-slide-down { animation: bo-slide-down .34s var(--ease-out) both; }
    .bo-breath { animation: bo-breath 3.8s var(--ease-in-out) infinite; }
    .bo-upd-in { animation: bo-upd-in .28s var(--ease-out) both; }
    .bo-drop-in { animation: bo-drop-in .14s var(--ease-out) both; }
    .bo-fade-in { animation: bo-fade-in .18s var(--ease-out) both; }
  }
  @media (prefers-reduced-motion: reduce) {
    .bo-edge-flow { stroke-dasharray: 4 8; }
  }
  /* tepsi build göstergesi (uygulama Controls/TrayBuildIndicator.xaml, TrayLoop 3s) — KeyTime → %, KeySpline → cubic-bezier (WPF'te eğri
     BİTTİĞİ keyframe'e, CSS'te BAŞLADIĞI keyframe'e yazılır). Reduced-motion'da sınıflar animasyon taşımaz: taban değerler = duruş karesi. */
  @media (prefers-reduced-motion: no-preference) {
    .tbi-chev { animation: tbi-chev-x 3s infinite, tbi-chev-o 3s infinite; }
    .tbi-sweep { animation: tbi-sweep-x 3s infinite; }
    .tbi-unmask { animation: tbi-unmask 3s infinite; }
    .tbi-amber { animation: tbi-amber-x 3s infinite, tbi-amber-o 3s infinite; }
    .tbi-white { animation: tbi-white-x 3s infinite, tbi-white-o 3s infinite; }
    .tbi-top { animation: tbi-top-x 3s infinite, tbi-top-o 3s infinite; }
    .tbi-silver { animation: tbi-silver-x 3s infinite, tbi-silver-o 3s infinite; }
    .tbi-mid { animation: tbi-mid-x 3s infinite, tbi-mid-o 3s infinite; }
  }
  @keyframes tbi-chev-x { 0% { transform: translateX(-170px); animation-timing-function: linear; } 3.333% { transform: translateX(-170px); animation-timing-function: cubic-bezier(.14, .62, .2, 1); } 38.333% { transform: translateX(0px); animation-timing-function: linear; } 70% { transform: translateX(0px); animation-timing-function: cubic-bezier(.34, 0, .68, .6); } 93.333% { transform: translateX(110px); } 100% { transform: translateX(110px); } }
  @keyframes tbi-chev-o { 0% { opacity: 0; animation-timing-function: cubic-bezier(.32, .5, .4, 1); } 10% { opacity: 1; animation-timing-function: linear; } 82% { opacity: 1; animation-timing-function: cubic-bezier(.45, .15, .6, 1); } 93.333% { opacity: 0; } 100% { opacity: 0; } }
  @keyframes tbi-sweep-x { 0% { transform: translateX(-170px); animation-timing-function: linear; } 3.333% { transform: translateX(-170px); animation-timing-function: cubic-bezier(.14, .62, .2, 1); } 38.333% { transform: translateX(0px); animation-timing-function: linear; } 70% { transform: translateX(0px); animation-timing-function: cubic-bezier(.34, 0, .68, .6); } 93.333% { transform: translateX(260px); } 100% { transform: translateX(260px); } }
  @keyframes tbi-amber-x { 0% { transform: translateX(-89px); animation-timing-function: linear; } 6.667% { transform: translateX(-89px); animation-timing-function: cubic-bezier(.16, .7, .22, 1); } 39.333% { transform: translateX(0px); animation-timing-function: linear; } 71% { transform: translateX(0px); animation-timing-function: cubic-bezier(.42, 0, .75, .5); } 92.667% { transform: translateX(80px); } 100% { transform: translateX(80px); } }
  @keyframes tbi-amber-o { 0% { opacity: 1; animation-timing-function: linear; } 81.333% { opacity: 1; animation-timing-function: cubic-bezier(.45, .15, .6, 1); } 92.667% { opacity: 0; } 100% { opacity: 0; } }
  @keyframes tbi-white-x { 0% { transform: translateX(-72px); animation-timing-function: linear; } 8.667% { transform: translateX(-72px); animation-timing-function: cubic-bezier(.12, .66, .18, 1); } 40.667% { transform: translateX(0px); animation-timing-function: linear; } 72.5% { transform: translateX(0px); animation-timing-function: cubic-bezier(.42, 0, .75, .5); } 94.667% { transform: translateX(88px); } 100% { transform: translateX(88px); } }
  @keyframes tbi-white-o { 0% { opacity: 1; animation-timing-function: linear; } 83.333% { opacity: 1; animation-timing-function: cubic-bezier(.45, .15, .6, 1); } 94.667% { opacity: 0; } 100% { opacity: 0; } }
  @keyframes tbi-top-x { 0% { transform: translateX(-43px); animation-timing-function: linear; } 10% { transform: translateX(-43px); animation-timing-function: cubic-bezier(.16, .7, .22, 1); } 42% { transform: translateX(0px); animation-timing-function: linear; } 74.167% { transform: translateX(0px); animation-timing-function: cubic-bezier(.45, 0, .78, .52); } 96.333% { transform: translateX(142px); } 100% { transform: translateX(142px); } }
  @keyframes tbi-top-o { 0% { opacity: 1; animation-timing-function: linear; } 85% { opacity: 1; animation-timing-function: cubic-bezier(.45, .15, .6, 1); } 96.333% { opacity: 0; } 100% { opacity: 0; } }
  @keyframes tbi-silver-x { 0% { transform: translateX(-35px); animation-timing-function: linear; } 12% { transform: translateX(-35px); animation-timing-function: cubic-bezier(.12, .66, .18, 1); } 43.333% { transform: translateX(0px); animation-timing-function: linear; } 75.833% { transform: translateX(0px); animation-timing-function: cubic-bezier(.45, 0, .78, .52); } 98% { transform: translateX(128px); } 100% { transform: translateX(128px); } }
  @keyframes tbi-silver-o { 0% { opacity: 1; animation-timing-function: linear; } 86.667% { opacity: 1; animation-timing-function: cubic-bezier(.45, .15, .6, 1); } 98% { opacity: 0; } 100% { opacity: 0; } }
  @keyframes tbi-mid-x { 0% { transform: translateX(-21px); animation-timing-function: linear; } 13.333% { transform: translateX(-21px); animation-timing-function: cubic-bezier(.16, .7, .22, 1); } 44.667% { transform: translateX(0px); animation-timing-function: linear; } 77.5% { transform: translateX(0px); animation-timing-function: cubic-bezier(.42, 0, .75, .5); } 100% { transform: translateX(160px); } }
  @keyframes tbi-mid-o { 0% { opacity: 1; animation-timing-function: linear; } 88.667% { opacity: 1; animation-timing-function: cubic-bezier(.45, .15, .6, 1); } 100% { opacity: 0; } }
  @keyframes tbi-unmask { 0% { transform: translateX(-100000px); animation-timing-function: step-end; } 38.333% { transform: none; } 100% { transform: none; } }
  @keyframes bo-gbeads { to { stroke-dashoffset: calc(var(--p) * -1); } }
  .bo-gbeads { animation: bo-gbeads 4000ms linear infinite; }
  @keyframes bo-reveal { from { opacity: 0; transform: translateY(-5px); } to { opacity: 1; transform: none; } }
  @keyframes bo-blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.1; } }
  /* imleç renk sıçraması (1.12.1): renk YALNIZ kırpmanın dibinde değişir (-0.55s faz), her yanışta konsol satır paletinden
     sıradaki renk — cmd · info · success · warn · error · dim (NARR_COLORS ile aynı). Geçiş yok, kesikli ritim. */
  @keyframes bo-hop { 0%, 16.66% { background: var(--text-primary); } 16.67%, 33.33% { background: var(--text-secondary); } 33.34%, 50% { background: var(--status-success-text); } 50.01%, 66.66% { background: var(--amber-text); } 66.67%, 83.33% { background: var(--status-fail-text); } 83.34%, 100% { background: var(--text-faint); } }
  @keyframes bo-rot { to { transform: rotate(360deg); } }
  @keyframes bo-shake { 10%, 90% { transform: translateX(-2px); } 25%, 75% { transform: translateX(3px); } 50% { transform: translateX(-3px); } }
  @keyframes bo-glow-once { 0% { background: var(--status-success-soft); } 100% { background: transparent; } }
  @keyframes bo-dash { to { stroke-dashoffset: -24; } }
  @keyframes bo-pop-in { from { opacity: 0; transform: translateY(4px) scale(.985); } to { opacity: 1; transform: none; } }
  /* konsol mod geçişi (uygulama PlayTiltIn): düz kayma + opaklık, 340ms ease-out, 14px — açılış aşağıdan, Back yukarıdan (ayna) */
  @keyframes bo-slide-up { from { opacity: 0; transform: translateY(14px); } to { opacity: 1; transform: none; } }
  @keyframes bo-slide-down { from { opacity: 0; transform: translateY(-14px); } to { opacity: 1; transform: none; } }
  @keyframes bo-breath { 0%, 100% { opacity: 0; } 50% { opacity: 0.32; } }
  /* güncelleme (1.20.0): hap title bar'a yukarıdan oturur; kartı aşağı açılır (alt bar popover'larının aynası) */
  @keyframes bo-upd-in { from { opacity: 0; transform: translateY(-4px); } to { opacity: 1; transform: none; } }
  @keyframes bo-drop-in { from { opacity: 0; transform: translateY(-4px) scale(.985); } to { opacity: 1; transform: none; } }
  @keyframes bo-fade-in { from { opacity: 0; } to { opacity: 1; } }
  /* bitiş koreografisi — neon tutuşma (floresan lamba gibi düzensiz titreyerek yanar) */
  @keyframes bo-neon { 0% { opacity: .12; } 8% { opacity: 1; } 16% { opacity: .2; } 24% { opacity: .9; } 32% { opacity: .14; } 46% { opacity: 1; } 56% { opacity: .5; } 70% { opacity: 1; } 100% { opacity: 1; } }
  .bo-scroll { scrollbar-width: thin; scrollbar-color: var(--neutral-700) transparent; }
  .bo-scroll::-webkit-scrollbar { width: 10px; height: 10px; }
  .bo-scroll::-webkit-scrollbar-track { background: transparent; }
  .bo-scroll::-webkit-scrollbar-thumb { background: var(--neutral-700); border: 3px solid transparent; background-clip: padding-box; border-radius: 5px; }
  /* yatayda görünmez scroll — uzun yollar fare tekerleğiyle sağa kaydırılır */
  .bo-xscroll { overflow-x: auto; overflow-y: hidden; scrollbar-width: none; -ms-overflow-style: none; overscroll-behavior-x: contain; }
  .bo-xscroll::-webkit-scrollbar { height: 0; width: 0; }
  /* ---- alt bar: TEK hover dili (1.17.0) ----
     Sync · bakım ikonları · filtre çipleri · branch/worktree/perf · Debug-Release aynı adımı atar:
     zemin bir yüzey basamağı açılır, hairline border'a döner, metin primary olur. Boyut ve renk tonu sabit.
     DS bileşenleri hover'ı inline yazdığı için !important şart; Build/Stop kendi variant hover'ında kalır. */
  [data-bo-actions] [data-bo-hov]:not(:disabled) { transition: background var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard), color var(--duration-fast) var(--ease-standard) !important; }
  [data-bo-actions] [data-bo-hov="off"]:not(:disabled):hover { background: var(--neutral-700) !important; border-color: var(--neutral-500) !important; color: var(--text-primary) !important; }
  /* ikon da metinle birlikte beyazlar (çip ikon sarmalayıcısı — statu glyph'i kendi rengini inline taşıdığı için etkilenmez) */
  [data-bo-actions] [data-bo-hov="off"]:not(:disabled):hover > span { color: var(--text-primary) !important; }
  [data-bo-actions] [data-bo-hov="on"]:not(:disabled):hover { background: var(--amber-soft-hover) !important; border-color: var(--amber) !important; }
  [data-bo-actions] [role="radiogroup"] > button[aria-checked="false"]:hover { background: var(--surface-raised) !important; color: var(--text-secondary) !important; }
  /* konsol satırı: okunan satır imleçle izlenir — bir yüzey adımı, tam genişlik bant (gövde padding'i 12px) */
  .bo-cline { margin: 0 -12px; padding: 0 12px; transition: background var(--duration-fast) var(--ease-standard); }
  .bo-cscroll { overflow: auto; }
  .bo-cline:hover { background: var(--surface); }
  `;
  document.head.appendChild(s);
})();

/* ---------- ikonlar (Lucide geometrisi, currentColor) ---------- */
const I = {
  close: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M18 6 6 18"/><path d="m6 6 12 12"/></svg>,
  branch: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="6" x2="6" y1="3" y2="15"/><circle cx="18" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M18 9a9 9 0 0 1-9 9"/></svg>,
  tree: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M8 3v4a2 2 0 0 0 2 2h8"/><path d="M8 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h2"/><path d="M8 13h6a2 2 0 0 1 2 2v6"/></svg>,
  pullDown: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M12 17V3"/><path d="m6 11 6 6 6-6"/><path d="M19 21H5"/></svg>,
  play: () => <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor" stroke="none"><polygon points="6 3 20 12 6 21 6 3"/></svg>,
  stop: () => <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor" stroke="none"><rect x="5" y="5" width="14" height="14" rx="1"/></svg>,
  playRow: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" stroke="none"><polygon points="6 3 20 12 6 21 6 3"/></svg>,
  stopRow: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" stroke="none"><rect x="5" y="5" width="14" height="14" rx="1"/></svg>,
  sync: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16"/><path d="M21 21v-5h-5"/></svg>,
  search: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>,
  folder: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/></svg>,
  folderOpen: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2"/></svg>,
  vs: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>,
  back: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m12 19-7-7 7-7"/><path d="M19 12H5"/></svg>,
  eraser: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="m7 21-4.3-4.3c-1-1-1-2.5 0-3.4l9.6-9.6c1-1 2.5-1 3.4 0l5.6 5.6c1 1 1 2.5 0 3.4L13 21"/><path d="M22 21H7"/><path d="m5 11 9 9"/></svg>,
  gauge: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="m12 14 4-4"/><path d="M3.34 19a10 10 0 1 1 17.32 0"/></svg>,
  unlink: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="m18.84 12.25 1.72-1.71a4.5 4.5 0 0 0-6.36-6.37l-1.72 1.72"/><path d="m5.17 11.75-1.71 1.71a4.5 4.5 0 0 0 6.36 6.37l1.72-1.72"/><path d="M8 2v3"/><path d="M2 8h3"/><path d="M16 19v3"/><path d="M19 16h3"/></svg>,
  trash: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M3 6h18"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>,
  check: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M20 6 9 17l-5-5"/></svg>,
  goto: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/></svg>,
  depWarn: () => <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor" stroke="none" aria-hidden="true"><path d="M12 3 23 21H1Z"/></svg>,
  alertTri: ({ size = 13 } = {}) => <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 20h16a2 2 0 0 0 1.73-2Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>,
  gear: () => <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/></svg>,
  sparkle: () => <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 2.8l2.1 5.8a2 2 0 0 0 1.3 1.3l5.8 2.1-5.8 2.1a2 2 0 0 0-1.3 1.3L12 21.2l-2.1-5.8a2 2 0 0 0-1.3-1.3L2.8 12l5.8-2.1a2 2 0 0 0 1.3-1.3z"/></svg>,
  info: () => <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 11v5"/><path d="M12 7.6h.01"/></svg>,
  /* güncelleme: Lucide circle-arrow-up, ⓘ ile aynı 9px yarıçap ve stroke */
  update: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="m15.5 11.5-3.5-3.5-3.5 3.5"/><path d="M12 16V8"/></svg>,
  arrowRight: ({ size = 12 } = {}) => <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M5 12h14"/><path d="m12 5 7 7-7 7"/></svg>,
  sigma: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M18 7V4H6l6 8-6 8h12v-3"/></svg>,
  chevUp: () => <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m18 15-6-6-6 6"/></svg>,
  up: () => <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m18 15-6-6-6 6"/></svg>,
  down: () => <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m6 9 6 6 6-6"/></svg>,
  plus: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 12h14"/><path d="M12 5v14"/></svg>,
  download: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" x2="12" y1="15" y2="3"/></svg>,
  upload: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="17 8 12 3 7 8"/><line x1="12" x2="12" y1="3" y2="15"/></svg>,
  rot: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/></svg>,
  /* Build menüsü + satır menüsü ortak ailesi: play · rotate-cw · brush (aynı grid ve stroke) */
  rebuild: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round"><path d="M21 12a9 9 0 1 1-3.5-7.1"/><path d="M21 4v5h-5"/></svg>,
  brush: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="m9.06 11.9 8.07-8.06a2.85 2.85 0 1 1 4.03 4.03l-8.06 8.08"/><path d="M7.07 14.94c-1.66 0-3 1.35-3 3.02 0 1.33-2.5 1.52-2 2.02 1.08 1.1 2.49 2.02 4 2.02 2.2 0 4-1.8 4-4.04a3.01 3.01 0 0 0-3-3.02z"/></svg>,
  more: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor" stroke="none"><circle cx="5" cy="12" r="1.6"/><circle cx="12" cy="12" r="1.6"/><circle cx="19" cy="12" r="1.6"/></svg>,
  grip: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" stroke="none"><circle cx="9" cy="6" r="1.4"/><circle cx="9" cy="12" r="1.4"/><circle cx="9" cy="18" r="1.4"/><circle cx="15" cy="6" r="1.4"/><circle cx="15" cy="12" r="1.4"/><circle cx="15" cy="18" r="1.4"/></svg>,
  copy: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>,
  redo: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M21 12a9 9 0 1 1-9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/></svg>,
  layQuad: () => <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.1"><rect x="1.7" y="2.7" width="5.5" height="4.5" rx="1"/><rect x="8.8" y="2.7" width="5.5" height="4.5" rx="1"/><rect x="1.7" y="8.8" width="5.5" height="4.5" rx="1"/><rect x="8.8" y="8.8" width="5.5" height="4.5" rx="1"/></svg>,
  layList: () => <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.1"><rect x="1.7" y="2.7" width="5.5" height="10.6" rx="1"/><rect x="8.8" y="2.7" width="5.5" height="4.5" rx="1"/><rect x="8.8" y="8.8" width="5.5" height="4.5" rx="1"/></svg>,
  layFocus: () => <svg width="16" height="16" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.1"><rect x="1.7" y="2.7" width="5.5" height="10.6" rx="1"/><rect x="8.8" y="2.7" width="5.5" height="7.1" rx="1"/><rect x="8.8" y="11.4" width="5.5" height="1.9" rx="0.95"/></svg>,
  /* Exit (uygulama 1.11.0): Lucide power — title bar kümesinin sonu (16px), kartın birincil düğmesinde 12px */
  power: ({ size = 16 } = {}) => <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 2v10"/><path d="M18.4 6.6a9 9 0 1 1-12.77.04"/></svg>,
  /* worktree anahtarı: Lucide folders (iki klasör) */
  folders: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M20 17a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3.9a2 2 0 0 1-1.69-.9l-.81-1.2a2 2 0 0 0-1.67-.9H8a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2Z"/><path d="M2 8v11a2 2 0 0 0 2 2h14"/></svg>,
  /* kaynak şeridi (1.25.0): Lucide cpu · memory-stick · üç paralel çizgi (tally) — 12px, tek renk */
  cpu: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><rect x="5" y="5" width="14" height="14" rx="2"/><rect x="9.5" y="9.5" width="5" height="5" rx="0.8"/><path d="M9 2.5V5"/><path d="M15 2.5V5"/><path d="M9 19v2.5"/><path d="M15 19v2.5"/><path d="M2.5 9H5"/><path d="M2.5 15H5"/><path d="M19 9h2.5"/><path d="M19 15h2.5"/></svg>,
  memory: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M3 7.5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2V12a1.8 1.8 0 0 0 0 3.4V17a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 17v-1.6a1.8 1.8 0 0 0 0-3.4Z"/><path d="M3 14.5h18"/><path d="M8 9v3"/><path d="M12 9v3"/><path d="M16 9v3"/><path d="M7 18.5v-4"/><path d="M12 18.5v-4"/><path d="M17 18.5v-4"/></svg>,
  parallel: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M6 5v14"/><path d="M12 5v14"/><path d="M18 5v14"/></svg>,
};

const MONO = 'var(--font-mono)';
/* Logo yolları LİTERAL tutulur (concat değil) — standalone paketleyicisi ancak böyle gömebilir. */
const LOGO = { mark: '/_blob/f81c3335c39584339205d287c715e6ea', company: '/_blob/7a5b1cea6b3e1b5582286a4b534181ee' };
const logoUrl = (base, key) => LOGO[key];const fmtElapsed = (ms) => {
  const s = Math.max(0, Math.floor(ms / 1000));
  if (s < 60) return s + 's';
  return Math.floor(s / 60) + 'm ' + String(s % 60).padStart(2, '0') + 's';
};

/* ---------- imleç + daktilo satırı ---------- */
/* Cursor: 7×13 blok, 1.1s kırpma; rengi .bo-cursor'daki bo-hop animasyonu sürür (satır paleti, kırpma dibinde sıçrar).
   Reduced-motion'da animasyon yok → currentColor. Konsol prompt'u ve stream canlı satırı aynı bileşeni kullanır. */
function Cursor({ style, still }) {
  /* still: renk turu yok (akışın imleci) — yalnız kırpma, rengi çağıran verir */
  return <span className={still ? 'bo-cursor-still' : 'bo-cursor'} style={{ display: 'inline-block', width: 7, height: 13, background: 'currentColor', verticalAlign: -2, flex: 'none', ...style }}></span>;
}

/* karakter kilitlenmesi: okuma başı soldan sağa ilerler, önündeki 5 karakterlik pencere
   rastgele mono glyph'lerle titrer, gerisi gerçek metin. Sayı/SHA taşıyan token'lar
   (1.4s, a3f81c2, 14/38) hiç scramble edilmez; kuyruk şeffaf basılır → satır genişliği sabit. */
const SCRAMBLE_GLYPHS = '#$%&*+=<>';
const SCRAMBLE_WIN = 5;
function scrambleMask(text) {
  const mask = new Array(text.length).fill(true);
  let i = 0;
  while (i < text.length) {
    let j = i;
    while (j < text.length && text[j] !== ' ') j++;
    const tok = text.slice(i, j);
    if (/\d/.test(tok)) for (let k = i; k < j; k++) mask[k] = false;
    for (let k = i; k < j; k++) if (!/[A-Za-z]/.test(text[k])) mask[k] = false;
    i = j + 1;
  }
  return mask;
}

function TypingLine({ text, instant, cursor, style, onDone }) {
  const [n, setN] = React.useState(instant || REDUCED ? text.length : 0);
  const mask = React.useMemo(() => scrambleMask(text), [text]);
  React.useEffect(() => {
    let doneT = null;
    const fire = () => { if (onDone) doneT = setTimeout(onDone, 420); };
    if (instant || REDUCED) { setN(text.length); fire(); return () => { if (doneT) clearTimeout(doneT); }; }
    setN(0);
    const step = Math.max(1, Math.ceil(text.length / 15)); // satır ≈ 360ms, uzunluktan bağımsız
    const iv = setInterval(() => setN((k) => {
      const nk = Math.min(text.length, k + step);
      if (nk >= text.length) { clearInterval(iv); fire(); }
      return nk;
    }), 24);
    return () => { clearInterval(iv); if (doneT) clearTimeout(doneT); };
  }, [text, instant]); // eslint-disable-line
  const end = Math.min(text.length, n + SCRAMBLE_WIN);
  let win = '';
  for (let i = n; i < end; i++) {
    win += mask[i] ? SCRAMBLE_GLYPHS[(Math.random() * SCRAMBLE_GLYPHS.length) | 0] : text[i];
  }
  return (
    <span style={style}>
      {text.slice(0, n)}{win}
      {end < text.length && <span style={{ color: 'transparent' }}>{text.slice(end)}</span>}
      {cursor && <Cursor style={{ marginLeft: 3 }} />}
    </span>
  );
}

/* ---------- building spinner: keşif halkasının dönen amber hali ---------- */
function BuildingSpin({ size = 14, style }) {
  return (
    <span role="img" aria-label="Building" style={{ display: 'inline-flex', color: 'var(--amber-text)', flex: 'none', ...style }}>
      <svg className={REDUCED ? undefined : 'bo-rot'} width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round">
        <circle cx="8" cy="8" r="6.7" strokeDasharray="2.3 2.5" opacity="0.9" />
      </svg>
    </span>
  );
}
function Glyph({ status, size = 14, style }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  return status === 'building' ? <BuildingSpin size={size} style={style} /> : <DS.StatusGlyph status={status} size={size} style={style} />;
}

/* ---------- "⌄ latest" pill — yukarı scroll'dayken yeni satır gelirse alt-ortada ---------- */
function LatestPill({ show, onClick }) {
  const [hover, setHover] = React.useState(false);
  if (!show) return null;
  return (
    <span style={{ position: 'absolute', left: 0, right: 0, bottom: 10, display: 'flex', justifyContent: 'center', pointerEvents: 'none', zIndex: 6 }}>
      {/* uygulama LatestPill: köşe 8 (Radius.Overlay), ⌄ metin glifi, giriş animasyonu yok (yalnız görünürlük) */}
      <button type="button" onClick={onClick} onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
        style={{
          pointerEvents: 'auto', display: 'inline-flex', alignItems: 'center', gap: 5, height: 22, padding: '0 9px 0 7px',
          background: hover ? 'var(--surface-raised)' : 'var(--surface-overlay)',
          border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
          color: hover ? 'var(--text-primary)' : 'var(--text-secondary)',
          fontFamily: MONO, fontSize: 'var(--text-2xs)', lineHeight: 1, cursor: 'pointer', userSelect: 'none',
          boxShadow: 'var(--elevation-popover)',
          transition: 'background var(--duration-fast) var(--ease-standard), color var(--duration-fast) var(--ease-standard)',
        }}>
        <span aria-hidden="true" style={{ fontFamily: MONO, lineHeight: 1 }}>{'\u2304'}</span> latest
      </button>
    </span>
  );
}

/* ---------- konsol satırı: yalnız metin — ikon kolonu yok, satırlar imleçle aynı sol hizada ----------
   Renk satırın BİÇİMİNDEN gelir (uygulama ConsoleLineClassifier, tek kaynak build-data.js classifyLine): Cmd · Info · Dim · Warn · Error.
   Yeşil 'success' satırı yok. Satırlar sarmaz (WordWrap=False) — uzun satır yatay kayar. */
const NARR_COLORS = {
  info: 'var(--text-secondary)', warn: 'var(--amber-text)',
  error: 'var(--status-fail-text)', cmd: 'var(--text-primary)', dim: 'var(--text-faint)',
};
function NarrLine({ type, text, cursor, children, style }) {
  const t = type === 'dim' && text == null ? 'dim' : BO.classifyLine(text != null ? text : '').type;
  return (
    <div className="bo-cline" style={{
      display: 'flex', gap: cursor ? 8 : 0, alignItems: 'baseline',
      fontFamily: MONO, fontSize: 'var(--text-xs)', lineHeight: 'var(--leading-mono)',
      color: NARR_COLORS[t] || NARR_COLORS.info,
      whiteSpace: 'pre', fontVariantNumeric: 'tabular-nums',
      ...style,
    }}>
      {cursor && <Cursor />}
      <span style={{ minWidth: 0 }}>{children}</span>
    </div>
  );
}

/* ---------- görünüm değişiminde konsol içeriği tek parça kayarak oturur (uygulama PlayTiltIn):
   düz 14px kayma + opaklık, 340ms ease-out; açılış aşağıdan, Back dönüşü yukarıdan. 3B eğim yok.
   Canlı gelen satırlar anında basılır (animasyon yalnız mount'ta bir kez). ---------- */
function RevealLog({ lines, fromAbove }) {
  const body = lines.map((l, i) => <NarrLine key={l.id != null ? l.id : i} text={l.text}>{l.text}</NarrLine>);
  return REDUCED ? <>{body}</> : <div className={fromAbove ? 'bo-slide-down' : 'bo-slide-up'}>{body}</div>;
}

/* ---------- seçili log için panoya kopyalama ---------- */
function CopyLogBtn({ getText }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [ok, setOk] = React.useState(false);
  const tRef = React.useRef(null);
  React.useEffect(() => () => { if (tRef.current) clearTimeout(tRef.current); }, []);
  const doCopy = async () => {
    const text = getText();
    let good = true;
    try { await navigator.clipboard.writeText(text); }
    catch (e) {
      try {
        const ta = document.createElement('textarea');
        ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
        document.body.appendChild(ta); ta.select();
        good = document.execCommand('copy'); ta.remove();
      } catch (e2) { good = false; }
    }
    if (good) {
      setOk(true);
      if (tRef.current) clearTimeout(tRef.current);
      tRef.current = setTimeout(() => setOk(false), 1400);
    }
  };
  return (
    <DS.Tooltip content={ok ? 'Copied' : 'Copy log'} side="bottom">
      <span style={{ display: 'inline-flex', color: ok ? 'var(--status-success-text)' : undefined }}>
        <DS.IconButton size="sm" title="Copy log" onClick={doCopy}>{ok ? <I.check /> : <I.copy />}</DS.IconButton>
      </span>
    </DS.Tooltip>
  );
}

/* ---------- panel başlığı ---------- */
function PanelHead({ label, children, right }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 8, height: 28, padding: '0 10px', flex: 'none',
      background: 'var(--surface)', borderBottom: '1px solid var(--border-subtle)', userSelect: 'none',
    }}>
      <span style={{ fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)', whiteSpace: 'nowrap' }}>{label}</span>
      {children}
      <span style={{ flex: 1 }}></span>
      {right}
    </div>
  );
}

/* ================= GRAF ================= */
/* v1.3.0 "quiet graph": isimsiz mini node'lar, katman bantları, soluk/parlak koşu
   sistemi, beads building animasyonu, hover tooltip, tıkla→odakla-sığdır,
   wheel zoom + drag pan. Ayrıntılı spec: handoff README §2.3. */
const GTONE = {
  fresh: { bd: 'var(--border-strong)', bg: 'var(--surface-raised)', dash: true }, // bilinmiyor: Sync yokken, kesikli border
  stale: { bd: 'var(--border-strong)', bg: 'var(--surface-raised)' },             // derlenecek: düz gri
  current: { bd: 'var(--status-success)', bg: 'var(--status-success-soft)' },     // güncel: yeşil
  marked: { bd: 'var(--amber)', bg: 'var(--amber-soft)' },
  queued: { bd: 'var(--amber)', bg: 'var(--amber-soft)' },
  building: { bd: 'var(--amber)', bg: 'var(--amber-soft)' },
  succeeded: { bd: 'var(--status-success)', bg: 'var(--status-success-soft)' },
  failed: { bd: 'var(--status-fail)', bg: 'var(--status-fail-soft)' },
};
/* tek statü kanalı: border ve küp aynı renk; döngü üyesinin küpü HER durumda amber (uygulama NodeCoreBrushKey) */
const GCORE = {
  fresh: 'var(--text-faint)', stale: 'var(--text-faint)', current: 'var(--status-success-text)',
  marked: 'var(--amber-text)', queued: 'var(--amber-text)', building: 'var(--amber-text)',
  succeeded: 'var(--status-success-text)', failed: 'var(--status-fail-text)',
};
/* çıktının durumu (uygulama StandingStatuses.From): karar yok → bilinmiyor · son derleme hata → bozuk · bayat → gri · değilse güncel */
function standingOf(st) {
  if (st.will === 'unknown') return 'fresh';
  if (st.reason === 'lastFailed') return 'failed';
  /* renk yalnız gerekçeden okunur: Rebuild'in zorladığı ama içeriği güncel satır yeşil kalır */
  return st.reason || (st.will === 'dirty' && st.lastBuiltAt == null) ? 'stale' : 'current';
}
/* görsel durum (uygulama VisualStatuses.For, renk modeli v1.20): çıktı durumu tabandır, koşu üstüne biner;
   atlanmak hiçbir şey bindirmez (güncelse yeşil kalır). Kesikli 'bilinmiyor' yalnız Sync yokken. */
function vstate(eng, name) {
  const st = eng.p[name];
  const sd = standingOf(st);
  switch (st.status) {
    case 'skipped': return sd;
    case 'failed': return sd === 'fresh' ? 'failed' : sd;
    case 'succeeded': return sd === 'stale' || sd === 'failed' ? sd : 'succeeded';
    case 'discovered': return eng.isMarked(name) && eng.markStep && eng.markStep !== 'neutral' ? 'marked' : sd;
    default: return st.status; // queued · building
  }
}
/* satır glyph'i görsel durumu çizer: güncel ✓ · bozuk ✗ · kuyruk saat · derleniyor spinner · diğerleri kesikli halka */
const VS_GLYPH = { current: 'succeeded', succeeded: 'succeeded', failed: 'failed', queued: 'queued', building: 'building' };
const glyphOf = (vs) => VS_GLYPH[vs] || 'discovered';
/* action bar sayaçları satırın GÖRSEL durumundan okunur (uygulama VisualStatuses.StateOf): ✓ yeşil, ✗ kırmızı satır */
function stateCounts(eng) {
  let building = 0, current = 0, broken = 0;
  BO.PROJECTS.forEach((p) => {
    const v = vstate(eng, p.name);
    if (v === 'building') building++;
    else if (v === 'succeeded' || v === 'current') current++;
    else if (v === 'failed') broken++;
  });
  return { total: BO.PROJECTS.length, building, current, broken };
}
const G_HOLD = 1400, G_FADE = 700; // biten node parlak kalma / sönme (GraphNodeOpacity.HoldMs/FadeMs)
const G_INSET = 36;                // içerik insetı, her kenarda (QuietGraphLayout.ContentInset)
const G_HOVER = 1.5;               // hover ve seçim büyütmesi (GraphView.HoverScale)
/* reveal sırası = derleme sırası. Harici projeler ayarlarla değiştiği için haritalar
   yeniden kurulabilir: BO.setExternal() sonrası syncGraphMaps() çağrılır. */
const G_ORDER_IX = {}, G_DEPS = {}, G_DEPENDENTS = {};
function syncGraphMaps() {
  [G_ORDER_IX, G_DEPS, G_DEPENDENTS].forEach((m) => Object.keys(m).forEach((k) => { delete m[k]; }));
  BO.ORDER.forEach((n, i) => { G_ORDER_IX[n] = i; });
  BO.PROJECTS.forEach((p) => { G_DEPS[p.name] = p.deps || []; (p.deps || []).forEach((d) => { (G_DEPENDENTS[d] = G_DEPENDENTS[d] || []).push(p.name); }); });
}
syncGraphMaps();

/* bantlar = listenin grupları (uygulama LayerGrouping.BandOf): katman tanımı yoksa tek bant, harici projeler varsa
   en üstte External bandı. Bant içi sıra build-order. Panele tam sığan pitch aranır; eksik son satır ortalanır. */
function graphLayout(W, H, bandGroups) {
  const groups = bandGroups.map((g) => g.rows.map((p) => p.name).sort((a, b) => (G_ORDER_IX[a] || 0) - (G_ORDER_IX[b] || 0)))
    .filter((g) => g.length);
  // cols asla en kalabalık bandı aşmaz — yoksa satır-içi ortalama + blok ortalaması çakışır
  const widest = groups.reduce((m, g) => Math.max(m, g.length), 1);
  let pitch = 5, cols = 1;
  for (let q = 44; q >= 5; q -= 0.5) {
    const c = Math.max(1, Math.min(widest, Math.floor(W / q)));
    let rows = 0; groups.forEach((g) => { rows += Math.ceil(g.length / c); });
    if ((rows + (groups.length - 1) * 0.7) * q <= H) { pitch = q; cols = c; break; }
  }
  const pos = {};
  let rowCursor = 0;
  groups.forEach((g, gi) => {
    const rows = Math.ceil(g.length / cols);
    for (let r = 0; r < rows; r++) {
      const start = r * cols, n = Math.min(cols, g.length - start);
      const offX = ((cols - n) / 2) * pitch;
      for (let c = 0; c < n; c++) pos[g[start + c]] = { x: offX + (c + 0.5) * pitch, y: (rowCursor + r + 0.5) * pitch };
    }
    rowCursor += rows + (gi < groups.length - 1 ? 0.7 : 0);
  });
  let x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
  Object.keys(pos).forEach((k) => { x0 = Math.min(x0, pos[k].x); x1 = Math.max(x1, pos[k].x); y0 = Math.min(y0, pos[k].y); y1 = Math.max(y1, pos[k].y); });
  const ox = W / 2 - (x0 + x1) / 2, oy = H / 2 - (y0 + y1) / 2;
  Object.keys(pos).forEach((k) => { pos[k].x += ox; pos[k].y += oy; });
  return { pos, size: Math.max(8, Math.min(24, pitch * 0.6)), pitch };
}

/* statü filtresi eşleşmesi — liste ve graf aynı kuralı paylaşır. filter = Set (çoklu, VEYA); kova görsel durumdan */
function filterMatch(eng, name, filter) {
  if (!filter || !filter.size) return true;
  const st = eng.p[name];
  const v = vstate(eng, name);
  let hit = false;
  filter.forEach((f) => {
    if (hit) return;
    if (f === 'warn') hit = !!st.depIssue || eng._isCycle(name);
    else if (f === 'building') hit = v === 'building'; // yalnız şu an derlenen (kuyruk sayılmaz)
    else if (f === 'succeeded') hit = v === 'succeeded' || v === 'current'; // yeşil satır
    else if (f === 'failed') hit = v === 'failed'; // kırmızı satır
  });
  return hit;
}
/* graf ve listenin boş görünümü (uygulama InteractionText.GraphEmpty / ListEmpty): ikon 28px + 10px + 12px faint metin */
function EmptyBlock({ icon, text }) {
  return (
    <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 10, background: 'var(--surface-base)' }}>
      {icon}
      <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-faint)' }}>{text}</span>
    </div>
  );
}
function GraphPanel({ eng, selected, onSelect, revealKey, workspace, filtering, match, groups, hovered, onHover }) {
  const boxRef = React.useRef(null);
  const [dim, setDim] = React.useState({ w: 0, h: 0 }); // 0 = henüz ölçülmedi (uydurma ilk boyutla çizim YOK)
  const [view, setView] = React.useState({ z: 1, x: 0, y: 0, m: 'none' });
  const hover = hovered; // liste ↔ graf ortak hover (uygulama GraphHoverEcho)
  const setHover = onHover;
  const [dragging, setDragging] = React.useState(false);
  const drag = React.useRef(null);
  const movedRef = React.useRef(false);
  const marks = React.useRef({ st: {}, done: {}, run: -1 });

  /* Ölçüm + wheel bağlantısı CALLBACK REF ile kurulur: gözlemci daima MONTE olan node'u izler.
     (Efekt + paylaşılan ref kullanılıyordu; workspace flip'inde early-return dalının node'u kopunca
     ResizeObserver bir daha ateşlemiyor, dim 240×160'ta donuyordu.) 0×0 ölçümler yok sayılır. */
  const cleanRef = React.useRef(null), dimRef = React.useRef({ w: 0, h: 0 });
  const setBox = React.useCallback((el) => {
    if (cleanRef.current) { cleanRef.current(); cleanRef.current = null; }
    boxRef.current = el;
    if (!el) return;
    /* ResizeObserver bazı gömülü ortamlarda hiç ateşlemiyor; ölçüm birden çok kaynaktan beslenir:
       sync + rAF + 120ms (ilk layout), window resize, ve 300ms'lik hafif nöbet (splitter/layout modu). */
    const measure = () => {
      const r = el.getBoundingClientRect();
      if (!r.width || !r.height) return;
      const d = dimRef.current;
      if (Math.abs(d.w - r.width) < 0.5 && Math.abs(d.h - r.height) < 0.5) return;
      dimRef.current = { w: r.width, h: r.height };
      setDim({ w: r.width, h: r.height });
    };
    let ro = null;
    if (typeof ResizeObserver !== 'undefined') { ro = new ResizeObserver(measure); ro.observe(el); }
    let alive = true, rafId = 0;
    const poll = () => { if (!alive) return; measure(); rafId = requestAnimationFrame(poll); };
    rafId = requestAnimationFrame(poll);
    const t0 = setTimeout(measure, 120);
    const iv = setInterval(measure, 300);
    window.addEventListener('resize', measure);
    measure();
    /* wheel = zoom (imlecin altındaki nokta sabit) — native listener, passive:false */
    const fn = (e) => {
      e.preventDefault();
      const r = el.getBoundingClientRect();
      const px = e.clientX - r.left, py = e.clientY - r.top;
      setView((v) => {
        const z = Math.max(0.7, Math.min(5, v.z * (e.deltaY < 0 ? 1.14 : 1 / 1.14)));
        const k = z / v.z;
        return { z, x: px - (px - v.x) * k, y: py - (py - v.y) * k, m: 'fast' };
      });
    };
    el.addEventListener('wheel', fn, { passive: false });
    cleanRef.current = () => {
      if (ro) ro.disconnect();
      alive = false; cancelAnimationFrame(rafId); clearTimeout(t0); clearInterval(iv);
      window.removeEventListener('resize', measure);
      el.removeEventListener('wheel', fn);
    };
  }, []);

  // boş alanda sürükle = pan
  React.useEffect(() => {
    const mv = (e) => {
      const d = drag.current; if (!d) return;
      const dx = e.clientX - d.sx, dy = e.clientY - d.sy;
      if (Math.abs(dx) + Math.abs(dy) > 3) d.moved = true;
      setView({ z: d.v.z, x: d.v.x + dx, y: d.v.y + dy, m: 'none' });
    };
    const up = () => { const d = drag.current; drag.current = null; if (d) { movedRef.current = d.moved; setDragging(false); } };
    document.addEventListener('mousemove', mv); document.addEventListener('mouseup', up);
    return () => { document.removeEventListener('mousemove', mv); document.removeEventListener('mouseup', up); };
  }, []);

  const measured = dim.w > 0 && dim.h > 0;
  const W = Math.max(240, dim.w), H = Math.max(160, dim.h), OX = G_INSET, OY = G_INSET;
  /* imza proje kümesini ve grupları da içerir: harici projeler ya da katmanlar değişince yerleşim yeniden kurulur */
  const projSig = BO.ORDER.length + ':' + BO.ORDER[0] + ':' + groups.map((g) => (g.name || '') + g.rows.length).join('|');
  const lay = React.useMemo(() => graphLayout(W - 2 * G_INSET, H - 2 * G_INSET, groups), [W, H, projSig]); // eslint-disable-line
  const pos = lay.pos, size = lay.size, pitch = lay.pitch;

  // seçim değişince (liste/graf/stream fark etmez) odakla-sığdır; bırakınca tam görünüm
  /* Bitiş koreografisi başlarken graf TAM GÖRÜNÜME döner (neon zinciri ekran dışında kalmasın) ve
     finale bitince odağa GERİ DÖNMEZ — bırakılan seçim hatırlanır, fit görünüm final hâldir.
     Kullanıcı başka bir proje seçince (ya da aynısını yeniden seçince) odak yeniden açılır. */
  const [focusOff, setFocusOff] = React.useState(null);
  const endStep = eng.endStep;
  React.useEffect(() => {
    if (endStep && selected) setFocusOff(selected);
  }, [endStep, selected]);
  React.useEffect(() => {
    if (focusOff && selected !== focusOff) setFocusOff(null);
  }, [selected, focusOff]);
  const finale = !!endStep || (!!selected && selected === focusOff);
  React.useEffect(() => {
    if (!workspace) return;
    if (selected && !finale && pos[selected]) {
      const set = [selected].concat(G_DEPS[selected] || [], G_DEPENDENTS[selected] || []);
      let x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
      set.forEach((n) => { const P = pos[n]; if (!P) return; x0 = Math.min(x0, P.x); x1 = Math.max(x1, P.x); y0 = Math.min(y0, P.y); y1 = Math.max(y1, P.y); });
      const pad = size * 3 + 48;
      const z = Math.max(0.7, Math.min(2.6, Math.min(W / ((x1 - x0) + pad), H / ((y1 - y0) + pad))));
      setView({ z, x: W / 2 - ((x0 + x1) / 2 + OX) * z, y: H / 2 - ((y0 + y1) / 2 + OY) * z, m: 'glide' });
    } else setView({ z: 1, x: 0, y: 0, m: 'glide' });
  }, [selected, lay, workspace, finale]); // eslint-disable-line

  /* boş görünüm: workspace yok, bakım işi (Clean/Optimize) listeyi ve grafı boşalttı, ya da proje yok */
  if (!workspace || eng.task || !BO.PROJECTS.length) return <EmptyBlock icon={I_NETWORK} text="No graph" />;
  if (eng.discover) return <DiscoverGraph />;

  // koşu başında bitiş kayıtlarını sıfırla; building→bitti anını işaretle (simT)
  if (eng.runCount !== marks.current.run) marks.current = { st: {}, done: {}, run: eng.runCount };
  BO.PROJECTS.forEach((p) => {
    const s = eng.p[p.name].status, ps = marks.current.st[p.name];
    if (s !== ps) { if (ps === 'building') marks.current.done[p.name] = eng.simT; marks.current.st[p.name] = s; }
  });

  const running = eng.phase === 'running' || eng.phase === 'stopping';
  /* koşu grafta filtreyi yok sayar (tıklamadan finalin sonuna dek); liste filtreli kalır */
  const filterOn = filtering && !(eng.phase === 'marking' || running || !!eng.endStep);
  const focus = new Set();
  if (selected && !finale) { focus.add(selected); (G_DEPS[selected] || []).forEach((d) => focus.add(d)); (G_DEPENDENTS[selected] || []).forEach((d) => focus.add(d)); }

  /* beads geometrisi (uygulama GraphBeads): nokta adımı hedefi 4.4px, tur 4000ms, stroke 1.6.
     Yörünge hücreye kelepçeli: mürekkep dahil komşu şeritle arada ≥2px boşluk kalır. */
  const bsw = 1.6;
  const bgap = Math.max(0.8, Math.min(2.8, (pitch - size - bsw - 2) / 2));
  const ri = 3.2, rw = size + bgap * 2, svgS = rw + ri * 2, rx = Math.min(rw / 2, 6.8);
  const per = 4 * rw - 8 * rx + 2 * Math.PI * rx;
  const step = per / Math.max(8, Math.round(per / 4.4));
  const runStyle = { '--p': per + 'px', strokeDasharray: '0.01px ' + (step - 0.01) + 'px' };

  let gEdges = null;
  if (selected && !finale && pos[selected]) {
    gEdges = [];
    const mk = (a, b) => {
      const A = pos[a], B = pos[b]; if (!A || !B) return;
      const x1 = OX + A.x, y1 = OY + A.y, x2 = OX + B.x, y2 = OY + B.y, my = (y1 + y2) / 2;
      gEdges.push({ k: a + '>' + b, d: 'M ' + x1 + ' ' + y1 + ' C ' + x1 + ' ' + my + ', ' + x2 + ' ' + my + ', ' + x2 + ' ' + y2 });
    };
    (G_DEPS[selected] || []).forEach((d) => mk(d, selected));
    (G_DEPENDENTS[selected] || []).forEach((d) => mk(selected, d));
  }

  const vtrans = REDUCED || dragging || view.m === 'none' ? 'none'
    : view.m === 'fast' ? 'transform 160ms var(--ease-out)' : 'transform 460ms var(--ease-in-out)';
  const selOn = selected && !finale;

  return (
    <div ref={setBox}
      onMouseDown={(e) => { if (e.target.closest && e.target.closest('[data-n]')) return; drag.current = { sx: e.clientX, sy: e.clientY, v: view, moved: false }; setDragging(true); }}
      onClick={(e) => { if (e.target.closest && e.target.closest('[data-n]')) return; if (movedRef.current) return; if (selected) onSelect(null); else setView({ z: 1, x: 0, y: 0, m: 'glide' }); }}
      style={{ flex: 1, minHeight: 0, overflow: 'hidden', position: 'relative', background: 'var(--surface-base)', cursor: dragging ? 'grabbing' : 'grab', userSelect: 'none' }}>
      {measured && <div key={revealKey} style={{
        position: 'absolute', left: 0, top: 0, width: W, height: H,
        transform: 'translate(' + view.x + 'px,' + view.y + 'px) scale(' + view.z + ')', transformOrigin: '0 0',
        transition: vtrans,
      }}>
        <svg width={W} height={H} style={{ position: 'absolute', left: 0, top: 0, overflow: 'visible', pointerEvents: 'none' }} aria-hidden="true">
          {gEdges && gEdges.map((e) => (
            <path key={e.k} d={e.d} fill="none" stroke="var(--amber)" strokeWidth={1.2} opacity={0.75} className={REDUCED ? undefined : 'bo-edge-flow'} />
          ))}
        </svg>
        {BO.PROJECTS.map((p) => {
          const vs = vstate(eng, p.name);
          const tone = GTONE[vs] || GTONE.stale;
          const core = eng._isCycle(p.name) ? 'var(--amber-text)' : (GCORE[vs] || GCORE.stale);
          const s = eng.p[p.name].status;
          const live = running && s === 'building';
          const justDone = !live && running && marks.current.done[p.name] != null && (eng.simT - marks.current.done[p.name]) < G_FADE;
          let op = 1, glide = 'opacity 280ms var(--ease-standard)';
          const es = eng.endStep; // bitiş koreografisi: hold → neon (zincir) → bwait → grey
          const isBuilt = s === 'succeeded' || s === 'failed';
          let neonAnim = null;
          if (es) { // finale seçim/filtre dimlemesini EZER — yoksa neon zinciri 0.1'de kalıyordu
            if (isBuilt) {
              const lit = es === 'neon' || es === 'bwait' || es === 'grey';
              op = lit ? 1 : 0.16;
              glide = 'opacity 300ms var(--ease-standard)';
              if (lit && !REDUCED) neonAnim = 'bo-neon 1150ms var(--ease-standard) ' + ((eng.endOrder[p.name] || 0) * eng.endStagger) + 'ms both';
            } else {
              op = es === 'grey' ? 1 : 0.16;
              glide = 'opacity 980ms var(--ease-in-out)';
            }
          }
          else if (selected) op = focus.has(p.name) || live ? 1 : 0.1; // derlenen node odak dışında da tam opak
          else if (filterOn) { op = match(p.name) ? 1 : 0.1; glide = 'opacity 420ms var(--ease-standard)'; } // filtre = graf üzerinde de vurgu
          else if (eng.phase === 'marking') {
            /* 1c "sıralı teslim" (1.18.0): dalga → sarı-gri an → grilerin vedası → kapsam YANDIĞI RASTGELE
               SIRAYLA koşu seviyesine iner; koşu bu kuyrukla örtüşerek başlar (ölü bekleyiş / ikinci sönüm yok) */
            const ms = eng.markStep || 'neutral';
            if (eng.isMarked(p.name)) {
              if (ms === 'settle') { op = 0.13; glide = 'opacity 400ms var(--ease-in-out) ' + ((eng.markOrder[p.name] || 0) * eng.markStagger) + 'ms'; }
            } else if (ms === 'settle' || ms === 'dimenv') { op = 0.18; glide = 'opacity 1120ms var(--ease-in-out)'; }
          }
          else if (running) {
            if (live) op = 1;
            else if (s === 'queued' || s === 'discovered' || s === 'skipped') op = 0.13; // atlanan sessiz: kuyrukla aynı seviyede kalır
            else { op = 0.2; if (marks.current.done[p.name] != null) glide = 'opacity ' + G_FADE + 'ms var(--ease-standard) ' + G_HOLD + 'ms'; }
          }
          const isSel = selOn && selected === p.name;
          const hov = hover === p.name;
          const big = hov || isSel; // seçili node hover görünümünü tutar (1.5×, 2px border, önde)
          if (hov) op = 1;
          const P = pos[p.name];
          if (!P) return null; // yerleşim henüz bu adı tanımıyor (proje kümesi değişti)
          const lightT = eng.markStep === 'wave' && eng.isMarked(p.name) ? '200ms var(--ease-standard) ' + ((eng.markOrder[p.name] || 0) * eng.markStagger) + 'ms' : '0ms';
          return (
            <div key={p.name} data-n={p.name}
              className={REDUCED ? undefined : 'bo-reveal'}
              onClick={(e) => { e.stopPropagation(); onSelect(selected === p.name ? null : p.name); }}
              onMouseEnter={() => setHover(p.name)} onMouseLeave={() => setHover(null)}
              style={{ position: 'absolute', left: OX + P.x, top: OY + P.y, width: 0, height: 0, cursor: 'pointer', zIndex: big ? 5 : 1,
                animationDelay: REDUCED ? undefined : Math.min((G_ORDER_IX[p.name] || 0) * 9, 520) + 'ms' }}>
              {/* opak taban: yarı saydam dolgunun arkasından seçim kenarı görünmesin */}
              <div aria-hidden="true" style={{ position: 'absolute', left: -size / 2, top: -size / 2, width: size, height: size, borderRadius: 'var(--radius-sm)', background: 'var(--surface-base)',
                opacity: op, transform: big ? 'scale(' + G_HOVER + ')' : 'scale(1)', transition: glide + ', transform 120ms var(--ease-out)' }}></div>
              <div style={{
                position: 'absolute', left: -size / 2, top: -size / 2, width: size, height: size, boxSizing: 'border-box',
                borderRadius: 'var(--radius-sm)', background: tone.bg,
                border: (big ? 2 : 1.5) + 'px ' + (tone.dash ? 'dashed ' : 'solid ') + tone.bd,
                opacity: op, transform: big ? 'scale(' + G_HOVER + ')' : 'scale(1)',
                animation: neonAnim || undefined,
                outline: isSel ? '2px solid var(--focus-ring)' : 'none', outlineOffset: 2,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                /* renk/border geçişleri anında; yalnız işaretleme dalgasında 200ms (LightMs) */
                transition: glide + ', transform 120ms var(--ease-out), background ' + lightT + ', border-color ' + lightT,
              }}>
                <svg viewBox="0 0 24 24" fill="none" stroke={core} strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round"
                  style={{ width: size * 0.52, height: size * 0.52, display: 'block', transition: 'stroke ' + lightT }} aria-hidden="true">
                  <path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z" />
                  <path d="m3.3 7 8.7 5 8.7-5" /><path d="M12 22V12" />
                </svg>
              </div>
              {!REDUCED && (
                <svg viewBox={'0 0 ' + svgS + ' ' + svgS} style={{
                  position: 'absolute', left: -svgS / 2, top: -svgS / 2, width: svgS, height: svgS,
                  overflow: 'visible', pointerEvents: 'none', opacity: live ? 1 : 0,
                  transform: big ? 'scale(' + G_HOVER + ')' : 'scale(1)',
                  transition: 'opacity ' + (live ? 420 : 640) + 'ms var(--ease-out), transform 120ms var(--ease-out)',
                }} aria-hidden="true">
                  <rect x={ri} y={ri} width={rw} height={rw} rx={rx} fill="none" stroke="var(--amber-text)" strokeWidth={bsw} strokeLinecap="round"
                    className={live || justDone ? 'bo-gbeads' : undefined} style={runStyle} />
                </svg>
              )}
            </div>
          );
        })}
      </div>}
      {/* hover tooltip: mono 11, boyalı yarım yükseklik + 6px üstte; kutusu panele sığar (yatay kaydırma) */}
      {hover && pos[hover] && (() => {
        const cx = (OX + pos[hover].x) * view.z + view.x, cy = (OY + pos[hover].y) * view.z + view.y;
        const half = (size * G_HOVER * view.z) / 2;
        const tw = hover.length * 6.7 + 18;
        const left = Math.max(6 + tw / 2, Math.min(W - 6 - tw / 2, cx));
        const above = cy - half - 6 - 22 >= 4; // üst bantta dikey flip
        return (
          <div style={{ position: 'absolute', zIndex: 20, pointerEvents: 'none', left,
            top: above ? cy - half - 6 : cy + half + 6,
            transform: above ? 'translate(-50%,-100%)' : 'translate(-50%,0)', padding: '4px 8px', background: 'var(--surface-overlay)',
            border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-md)', boxShadow: 'var(--elevation-popover)',
            whiteSpace: 'nowrap', fontFamily: MONO, fontSize: 11, color: 'var(--text-primary)' }}>{hover}</div>
        );
      })()}
      {/* seçim etiketi: hep node'un altında, halkanın dış kenarından 6px */}
      {selOn && pos[selected] && (
        <div style={{ position: 'absolute', zIndex: 12, pointerEvents: 'none', transform: 'translateX(-50%)',
          left: (OX + pos[selected].x) * view.z + view.x,
          top: (OY + pos[selected].y) * view.z + view.y + (size * G_HOVER * view.z) / 2 + 4 + 6,
          padding: '2px 6px', background: 'var(--surface-overlay)', border: '1px solid var(--amber-border)',
          borderRadius: 'var(--radius-sm)', whiteSpace: 'nowrap', fontFamily: MONO, fontSize: 10, color: 'var(--amber-text)' }}>{selected}</div>
      )}
    </div>
  );
}
/* ================= PROJE LİSTESİ ================= */
/* chip etiketleri uygulamanın ProjectFilter.ChipLabel'ı: ✓ = yeşil satırlar 'Up to date' */
const FILTER_LABELS = { building: 'Building', succeeded: 'Up to date', failed: 'Failed', warn: 'Warnings' };
/* birleşik etiket chip'lerin bar sırasını izler (ProjectFilter.Order) — tıklama sırasını değil */
const FILTER_ORDER = ['building', 'succeeded', 'failed', 'warn'];
/* ---- satırın karar etiketi (uygulama DecisionLabel) ----
   Beş sabit sözcük: modified · affected · never built · failed · up to date. Yaş kuyruğu YOK; tek kuyruk `local`
   (girdilerinden biri git status'ta kirli). Uzun gerekçe native title. Karar yoksa yuva boş. */
/* gerekçe → sözcük: config/behind 'affected', newer 'modified' (zaman kipi), outputMissing 'never built' (OutputMissing = NeverBuilt) */
const DECISION_WORDS = { changed: 'modified', dependency: 'affected', config: 'affected', behind: 'affected', newer: 'modified', neverBuilt: 'never built', outputMissing: 'never built', lastFailed: 'failed' };
const DECISION_TITLES = {
  changed: 'Its own files changed since the last build',
  dependency: 'Its own files are unchanged — a dependency changed',
  behind: 'Its own files are unchanged — a dependency changed',
  newer: 'Its own files are newer than its build output',
  neverBuilt: 'No build output known to this tool',
  outputMissing: 'No build output known to this tool',
  lastFailed: 'Failed at this source — Build will retry it',
};
function decisionLabel(st, local) {
  if (st.will === 'unknown') return null; // Sync yapılmadı / branch değişti — karar henüz yok
  const r = st.reason || (st.will === 'dirty' && st.lastBuiltAt == null ? 'neverBuilt' : null);
  if (r === 'changed' && local) return { text: 'modified', tail: 'local', build: true, title: 'Its own files changed since the last build — includes uncommitted edits' };
  if (r === 'newer' && local) return { text: 'modified', tail: 'local', build: true, title: DECISION_TITLES.newer };
  /* imza yalnız configuration'la değişti: gerekçe son derlemenin configuration'ını adlandırır (CL 1.11.3) */
  if (r === 'config') return { text: 'affected', tail: null, build: true, title: 'Its own files are unchanged — last built in ' + st.builtCfg };
  if (r) return { text: DECISION_WORDS[r], tail: null, build: true, title: DECISION_TITLES[r] };
  if (st.timeMode) return { text: 'up to date', tail: null, build: false, title: 'Up to date — built outside this tool' };
  return { text: 'up to date', tail: null, build: false, title: 'Up to date' };
}
/* tek uyarı üçgeni — tek satır tooltip; ayrıntı (üye listesi, gerekçe) proje logunda */
function warnText(eng, name) {
  const st = eng.p[name];
  if (eng._isCycle(name)) return 'In a dependency cycle';
  if (st.depIssue) return 'Dependency issue: ' + BO.shortName(st.depIssue[0]) + (st.depIssue.length > 1 ? ' +' + (st.depIssue.length - 1) : '');
  return null;
}
/* satır menüsü: Build · Rebuild · Clean — sağ tık ve ⋯ aynı menüyü açar.
   Konumu fixed: satırın rect'ine çakılır — liste scroll'u ve satIr yığını menüyü kesmez. */
function RowMenu({ pos, onPick, onClose, busy }) {
  const items = [
    ['build', 'Build', <I.play />],
    ['rebuild', 'Rebuild', <I.rebuild />],
    ['clean', 'Clean', <I.brush />],
  ];
  const [hi, setHi] = React.useState(null);
  return (
    <div data-bo-popover="1" className={REDUCED ? undefined : 'bo-pop-in'} onClick={(e) => e.stopPropagation()}
      style={{
        position: 'absolute', top: pos.top, right: 8, zIndex: 200, width: 154, padding: 4, boxSizing: 'border-box',
        background: 'var(--surface-overlay)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--elevation-overlay)',
      }}>
      <div style={{ padding: '3px 7px 5px', fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{BO.shortName(pos.name)}</div>
      {items.map(([id, label, icon]) => (
        <div key={id} onMouseEnter={() => setHi(id)} onMouseLeave={() => setHi(null)}
          title={busy ? 'Build in progress — wait or stop it first' : undefined} /* pasif madde nedenini söyler (ProjectRowMenu) */
          onClick={() => { onClose(); if (!busy) onPick(id, pos.name); }}
          style={{
            display: 'flex', alignItems: 'center', gap: 8, height: 26, padding: '0 7px', borderRadius: 'var(--radius-sm)',
            cursor: busy ? 'default' : 'pointer', opacity: busy ? 0.45 : 1, userSelect: 'none',
            background: hi === id && !busy ? 'var(--surface-raised)' : 'transparent',
            transition: 'background var(--duration-fast) var(--ease-standard)',
          }}>
          <span style={{ display: 'inline-flex', width: 13, justifyContent: 'center', color: 'var(--text-dim)', flex: 'none' }}>{icon}</span>
          <span style={{ fontSize: 'var(--text-sm)', color: 'var(--text-secondary)' }}>{label}</span>
        </div>
      ))}
    </div>
  );
}
/* Open in Visual Studio seçici (uygulama ProjectRowActions PART_VsChooser): proje birden çok solution'da listeliyse VS düğmesinin
   4 px altında açılır, sol kenarı düğmeyle hizalı. Ds.Popover kabuğu (surface-overlay · border-strong · radius-lg · 8 iç pay ·
   overlay gölgesi), min 220 / max 360; caps başlık 11/500 faint (4,2,4,8); satır 28 px, 0 6 iç pay, radius-sm, hover raised;
   ad mono 12 secondary, uzantısız (SolutionRef.Name), taşan ad üç noktayla kesilir. Seçim kapatır ve notu yazar; dışarı tık kapatır. */
const slnName = (f) => f.replace(/\.sln$/i, '');
function VsChooser({ pos, onPick, onClose }) {
  const [hi, setHi] = React.useState(null);
  React.useEffect(() => {
    const h = (e) => { if (!e.target.closest || !e.target.closest('[data-bo-vs]')) onClose(); };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, [onClose]);
  /* WPF popup'ı ekranda tutar: sağ kenar pencereyi aşarsa sola kayar */
  const left = Math.max(8, Math.min(pos.left, window.innerWidth - 8 - 220));
  return (
    <div data-bo-popover="1" data-bo-vs="1" className={REDUCED ? undefined : 'bo-pop-in'} onClick={(e) => e.stopPropagation()}
      style={{
        position: 'fixed', top: pos.top, left, zIndex: 400, minWidth: 220, maxWidth: Math.min(360, window.innerWidth - 8 - left),
        width: 'max-content', padding: 8, boxSizing: 'border-box',
        background: 'var(--surface-overlay)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--elevation-overlay)', color: 'var(--text-primary)',
      }}>
      <div style={{ margin: '2px 4px 8px', fontSize: 'var(--text-2xs)', fontWeight: 500, color: 'var(--text-faint)', whiteSpace: 'nowrap' }}>OPEN IN VISUAL STUDIO</div>
      {pos.slns.map((f) => (
        <div key={f} onMouseEnter={() => setHi(f)} onMouseLeave={() => setHi(null)}
          onClick={() => { onClose(); onPick(pos.name); }}
          style={{
            display: 'flex', alignItems: 'center', height: 28, padding: '0 6px', borderRadius: 'var(--radius-sm)', cursor: 'pointer',
            background: hi === f ? 'var(--surface-raised)' : 'transparent', transition: 'background var(--duration-fast) var(--ease-standard)',
          }}>
          <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--text-secondary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', minWidth: 0 }}>{slnName(f)}</span>
        </div>
      ))}
    </div>
  );
}
function Row({ p, eng, selected, onSelect, onNote, onAction, onStop, busy, isTarget, revealIndex, menuOpen, setMenu, hovered, onHover, local, vsOpen, setVs }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const hover = hovered === p.name; // liste ↔ graf ortak hover: node'a gelince satır da hover zeminini ve ikonlarını gösterir
  const st = eng.p[p.name];
  const vs = vstate(eng, p.name);
  const warn = warnText(eng, p.name);
  /* koreografi adımı — satırlar graf node'larıyla senkron söner/yerleşir */
  const mstep = eng.phase === 'marking' ? (eng.markStep || 'neutral') : null;
  const noReveal = React.useRef(false);
  if (mstep) noReveal.current = true; // reveal animasyonunun fill kilidini bırak — opaklık geçişleri uygulansın
  let rowOp = 1;
  const rowGlide = '360ms var(--ease-in-out)';
  /* Sönme/geri gelme (veda + neon finali) YALNIZ graf node'larında — proje listesi sabit kalır:
     satırlarda dalga rengi taşınır, opaklık oynamaz (1.13.2). */
  /* isim vurgusu: bu koşuda işi olan satır primary */
  const waveDelay = mstep === 'wave' && eng.isMarked(p.name) ? (eng.markOrder[p.name] || 0) * eng.markStagger : 0;
  const emph = vs === 'marked' || vs === 'queued' || vs === 'building' || vs === 'failed' || vs === 'succeeded';
  const isSel = selected === p.name;
  const shake = eng.lastFail && eng.lastFail.name === p.name && (eng.simT - eng.lastFail.t) < 700;
  /* şerit + nokta görsel durumu taşır (VisualStatuses.StripeBrushKey): bilinmiyor/derlenecek gri, güncel yeşil */
  const STRIP = {
    fresh: 'var(--status-skipped-border)', stale: 'var(--status-skipped-border)', current: 'var(--status-success)',
    marked: 'var(--amber)', queued: 'var(--amber)', building: 'var(--amber)',
    succeeded: 'var(--status-success)', failed: 'var(--status-fail)',
  };
  const dec = decisionLabel(st, local); // sağ blok: kararın gerekçesi (hover'da ikonlarla yer değiştirir)
  const openMenu = (e) => {
    e.preventDefault(); e.stopPropagation();
    if (menuOpen) { setMenu(null); return; }
    const wrap = e.currentTarget.closest('[data-row-wrap]');
    const cont = wrap && wrap.closest('[data-list-scroll]');
    const MH = 108;
    let top = wrap ? wrap.offsetTop + wrap.offsetHeight - 3 : 0;
    if (cont) { // men\u00fcy\u00fc panelin g\u00f6r\u00fcn\u00fcr alan\u0131 i\u00e7inde tut (dipteki sat\u0131rlarda yukar\u0131 kayar)
      const maxTop = cont.scrollTop + cont.clientHeight - MH - 4;
      const minTop = cont.scrollTop + 4;
      top = Math.max(minTop, Math.min(top, Math.max(minTop, maxTop)));
    }
    setMenu({ name: p.name, top });
  };
  return (
    <div role="row" tabIndex={0}
      onClick={() => onSelect(isSel ? null : p.name)}
      onContextMenu={openMenu}
      onKeyDown={(e) => { if (e.key === 'Enter') onSelect(isSel ? null : p.name); }}
      onMouseEnter={() => onHover(p.name)} onMouseLeave={() => onHover(null)}
      className={[REDUCED || noReveal.current ? '' : 'bo-reveal', shake && !REDUCED ? 'bo-shake' : ''].join(' ').trim() || undefined}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, height: 'var(--row-height)', boxSizing: 'border-box',
        padding: '0 10px 0 0', position: 'relative', cursor: 'pointer', userSelect: 'none', zIndex: menuOpen ? 40 : undefined,
        opacity: rowOp,
        background: isSel ? 'var(--surface-raised)' : hover || menuOpen ? 'var(--surface-hover)' : 'transparent',
        borderBottom: '1px solid var(--border-subtle)',
        transition: 'background var(--duration-fast) var(--ease-standard), opacity ' + rowGlide,
        animationDelay: REDUCED ? undefined : Math.min(revealIndex * 10, 380) + 'ms',
      }}>
      {st.status === 'building' && !REDUCED && (
        <span aria-hidden="true" className="bo-breath" style={{ position: 'absolute', inset: 0, background: 'var(--amber-soft)', opacity: 0, pointerEvents: 'none' }}></span>
      )}
      {/* sol şerit: başlangıç modunda düz ve soluk (0.5), işlem başlayınca tam — node'larla senkron (dalgada sıralı gecikme). 1.12.0: kesikli gradient kalktı (tırtık) */}
      <span aria-hidden="true" style={{
        width: isSel ? 3 : 2, alignSelf: 'stretch', flex: 'none',
        background: STRIP[vs], opacity: 1,
        transition: 'width var(--duration-instant) var(--ease-standard), opacity 380ms var(--ease-standard), background ' + (mstep === 'wave' && eng.isMarked(p.name) ? '200ms var(--ease-standard) ' + ((eng.markOrder[p.name] || 0) * eng.markStagger) + 'ms' : '180ms var(--ease-standard)'),
      }}></span>
      <span style={{
        display: 'flex', alignItems: 'center', gap: 8, minWidth: 0, flex: 1, paddingLeft: 12,
        transform: isSel ? 'translateX(4px)' : 'none', transition: 'transform var(--duration-fast) var(--ease-out)',
      }}>
        {/* statü noktası: şeritle AYNI rengi taşır (ayrı plan kanalı değil). 1.12.0: tek 8px SVG — başlangıç modunda
            4 yaylı halka, işlem başlayınca dolu noktaya çapraz-söner (aynı eleman/boyut, kayma yok) */}
        <svg aria-hidden="true" viewBox="0 0 8 8" width="8" height="8" style={{ display: 'block', flex: 'none' }}>
          <circle cx="4" cy="4" r="3.2" fill="none" stroke="var(--status-skipped-border)" strokeWidth="1.1" strokeDasharray="2.93 2.1"
            style={{ opacity: vs === 'fresh' ? 1 : 0, transition: 'opacity 380ms var(--ease-standard)' }} />
          <circle cx="4" cy="4" r="4" fill={STRIP[vs]}
            style={{ opacity: vs === 'fresh' ? 0 : 1, transition: 'opacity 380ms var(--ease-standard), fill ' + (mstep === 'wave' && eng.isMarked(p.name) ? '200ms var(--ease-standard) ' + ((eng.markOrder[p.name] || 0) * eng.markStagger) + 'ms' : '180ms var(--ease-standard)') }} />
        </svg>
        <span style={{ display: 'flex', alignItems: 'baseline', gap: 8, minWidth: 0, flex: 1 }}>
          <span style={{ fontSize: 'var(--text-sm)', fontWeight: 500, color: emph ? 'var(--text-primary)' : 'var(--text-secondary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', transition: 'color 200ms var(--ease-standard) ' + waveDelay + 'ms' }}>{p.name}</span>
          <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-faint)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{p.sln}</span>
        </span>
      </span>
      {/* sağ blok: hover'da aksiyonlar, değilse kararın gerekçesi (v1.16.0 — commit çifti kalktı) */}
      <span style={{ display: 'flex', alignItems: 'center', gap: 2, flex: 'none', minWidth: 134, justifyContent: 'flex-end' }}>
        {hover || isTarget || menuOpen || vsOpen ? (
          <span style={{ display: 'flex', gap: 2 }}>
            {isTarget ? (
              <DS.IconButton size="sm" aria-label="Stop build" title="Stop — cancel this build" onClick={(e) => { e.stopPropagation(); onStop(); }}>
                <span style={{ display: 'inline-flex', color: 'var(--status-fail-text)' }}><I.stopRow /></span>
              </DS.IconButton>
            ) : (
              <DS.IconButton size="sm" aria-label="Build this project" title={busy ? 'Build in progress — wait or stop it first' : 'Build this project'} disabled={busy}
                onClick={(e) => { e.stopPropagation(); onAction('build', p.name); }}><I.playRow /></DS.IconButton>
            )}
            <DS.IconButton size="sm" aria-label="More build actions" title="More — Rebuild, Clean" active={menuOpen} onClick={openMenu}><I.more /></DS.IconButton>
            <DS.IconButton size="sm" title="Reveal in Explorer" onClick={(e) => { e.stopPropagation(); onNote(p.name + '.csproj revealed in Explorer'); }}><I.folderOpen /></DS.IconButton>
            <DS.IconButton size="sm" title="Open in Visual Studio" active={vsOpen} onClick={(e) => {
              e.stopPropagation();
              /* tek solution: doğrudan açılır; birden çoksa seçici (OpenProjectInVisualStudioAsync → NeedsChoice, not yazılmaz) */
              if (p.slns && p.slns.length > 1) {
                if (vsOpen) { setVs(null); return; }
                const r = e.currentTarget.getBoundingClientRect();
                const slns = BO.SOLUTIONS.map((x) => x.name).filter((x) => p.slns.indexOf(x) >= 0);
                /* WPF Placement=Bottom: altta yer yoksa düğmenin üstüne çevrilir */
                const h = 2 + 16 + 25 + slns.length * 28;
                const top = r.bottom + 4 + h > window.innerHeight - 8 ? r.top - 4 - h : r.bottom + 4;
                setVs({ name: p.name, left: r.left, top, slns });
                return;
              }
              onNote(p.name + ' opened in Visual Studio');
            }}><I.vs /></DS.IconButton>
          </span>
        ) : dec ? (
          <span title={dec.title} style={{ fontFamily: MONO, fontSize: 10.5, color: dec.build ? 'var(--text-secondary)' : 'var(--text-faint)', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums', cursor: 'default' }}>
            {dec.text}{dec.tail ? <span style={{ color: 'var(--text-faint)' }}>{' · ' + dec.tail}</span> : null}
          </span>
        ) : null}
      </span>
      <Glyph status={glyphOf(vs)} size={14} />
      <span style={{ width: 14, display: 'inline-flex', justifyContent: 'center', flex: 'none' }}>
        {st.status !== 'building' && warn && (
          <DS.Tooltip side="left" content={warn}>
            <span aria-label="warning" style={{ display: 'inline-flex', color: 'var(--amber-text)', cursor: 'default' }}><I.alertTri size={12} /></span>
          </DS.Tooltip>
        )}
      </span>
      <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', fontVariantNumeric: 'tabular-nums', color: st.status === 'failed' ? 'var(--status-fail-text)' : 'var(--text-dim)', minWidth: 46, textAlign: 'right', whiteSpace: 'nowrap' }}>
        {st.status === 'building' ? fmtElapsed(eng.simT - st.startAt) : st.doneDur ? BO.fmtDur(st.doneDur) : '—'}
      </span>
    </div>
  );
}

/* Sync keşfi sürerken (1.24.0): proje kümesi henüz bilinmiyor — iki panel ikon + metin gösterir.
   Liste satırı kaynak adı YAZMAZ; yalnız artan sayılar: toplam + (harici proje tanımlıysa) repository/external kırılımı.
   Toplam/payda yok (tarama bitmeden bilinmez). Döngüsel animasyon yok; sayılar kaynak okundukça değişir.
   Hayalet iskelet (nefes / süpürme / sabit) ve solution adı + paydalı sayaç DENENDİ, İSTENMEDİ. */
const I_NETWORK = (
  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="var(--text-faint)" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{ opacity: 0.7 }}>
    <rect x="16" y="16" width="6" height="6" rx="1" /><rect x="2" y="16" width="6" height="6" rx="1" /><rect x="9" y="2" width="6" height="6" rx="1" />
    <path d="M5 16v-3a1 1 0 0 1 1-1h12a1 1 0 0 1 1 1v3" /><path d="M12 12V8" />
  </svg>
);
const I_LIST = (
  <svg width="28" height="28" viewBox="0 0 24 24" fill="none" stroke="var(--text-faint)" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{ opacity: 0.7, marginBottom: 2 }}>
    <path d="M3 6h.01" /><path d="M3 12h.01" /><path d="M3 18h.01" /><path d="M8 6h13" /><path d="M8 12h13" /><path d="M8 18h13" />
  </svg>
);
function DiscoverGraph() {
  return (
    <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 10, background: 'var(--surface-base)' }}>
      {I_NETWORK}
      <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-faint)' }}>Graph appears once projects are discovered</span>
    </div>
  );
}
function DiscoverList({ d }) {
  const found = d.repo + d.ext;
  return (
    <div role="status" aria-live="polite" style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 8, background: 'var(--surface-base)', padding: '0 24px' }}>
      {I_LIST}
      <span style={{ fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-secondary)' }}>Discovering projects</span>
      <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>
        <span style={{ color: 'var(--text-dim)' }}>{found}</span> found
        {d.hasExt ? ' · ' + d.repo + ' repository · ' + d.ext + ' external' : ''}
      </span>
    </div>
  );
}

/* katman başlığı (yapışkan şerit): hover'da bir yüzey adımı açılır, tıklama o grubun başını
   yığılmış başlıkların hemen altına getirir. Seçim/filtre/konsola dokunmaz — yalnız gezinme. */
function GroupHead({ name, count, top, onJump }) {
  const [hov, setHov] = React.useState(false);
  return (
    <div role="button" tabIndex={0} title={'Jump to ' + name} onClick={onJump}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onJump(); } }}
      onMouseEnter={() => setHov(true)} onMouseLeave={() => setHov(false)}
      style={{
        position: 'sticky', top, zIndex: 3, display: 'flex', alignItems: 'center', gap: 8,
        height: 24, boxSizing: 'border-box', padding: '0 10px', cursor: 'pointer',
        background: hov ? 'var(--surface-raised)' : 'var(--surface)',
        borderBottom: '1px solid ' + (hov ? 'var(--border)' : 'var(--border-subtle)'),
        fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)',
        textTransform: 'uppercase', color: hov ? 'var(--text-secondary)' : 'var(--text-faint)', userSelect: 'none',
        transition: 'background var(--duration-fast) var(--ease-standard), color var(--duration-fast) var(--ease-standard)',
      }}>
      {name}
      <span style={{ fontFamily: MONO, textTransform: 'none', letterSpacing: 0 }}>{count}</span>
    </div>
  );
}

function ListPanel({ eng, groups, selected, onSelect, onNote, onRowAction, onStopRun, filter, query, follow, revealKey, workspace, layerCount, onOpenSettings, onImportSettings, hovered, onHover, localEdits }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const boxRef = React.useRef(null);
  const rowRefs = React.useRef({});
  const lastScrollT = React.useRef(0);
  const [menu, setMenu] = React.useState(null);
  const [vs, setVs] = React.useState(null); // açık Visual Studio seçicisi { name, left, top, slns }
  const closeVs = React.useCallback(() => setVs(null), []);
  React.useEffect(() => {
    if (!menu) return;
    const h = (e) => {
      if (!e.target.closest) return;
      /* ⋯ tetikleyicisi hariç: mousedown menüyü kapatıp click yeniden açıyordu (toggle çalışmıyordu) */
      if (e.target.closest('[data-bo-popover]') || e.target.closest('[aria-label="More build actions"]')) return;
      setMenu(null);
    };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, [menu]);

  const firstBuilding = eng.buildingList()[0];
  /* frontier'i yumuşak takip (yalnız koşarken + seçim yokken) */
  React.useEffect(() => {
    if (!follow || !firstBuilding) return;
    const c = boxRef.current, el = rowRefs.current[firstBuilding];
    if (!c || !el) return;
    const now = Date.now();
    if (now - lastScrollT.current < 550) return;
    const target = Math.max(0, el.offsetTop - Math.max(150, c.clientHeight * 0.3));
    if (Math.abs(c.scrollTop - target) < 54) return;
    lastScrollT.current = now;
    c.scrollTo({ top: target, behavior: REDUCED ? 'auto' : 'smooth' });
  });
  /* seçilen karta git */
  React.useEffect(() => {
    if (!selected) return;
    const t = setTimeout(() => {
      const c = boxRef.current, el = rowRefs.current[selected];
      if (!c || !el) return;
      const target = Math.max(0, el.offsetTop - Math.max(150, c.clientHeight * 0.35));
      c.scrollTo({ top: target, behavior: REDUCED ? 'auto' : 'smooth' });
    }, 90);
    return () => clearTimeout(t);
  }, [selected, revealKey]);

  /* Sync (ve workspace/senaryo yenilemeleri) — liste sıfırdan listelenir: scroll başa döner.
     Build/Rebuild/Clean/Resolve ve satırdan tetiklenenler scroll'a DOKUNMAZ (kullanıcı kararı). */
  React.useEffect(() => {
    const c = boxRef.current;
    if (!c || !revealKey || selected) return;
    c.scrollTo({ top: 0, behavior: REDUCED ? 'auto' : 'smooth' });
  }, [revealKey]); // eslint-disable-line

  /* başlığa tıklama: o grubun ilk satırı yığılmış başlıkların hemen altına oturur (başlık kendi yapışma yerinde) */
  const jumpGroup = (firstRow, stickIx) => {
    const c = boxRef.current, el = rowRefs.current[firstRow];
    if (!c || !el) return;
    c.scrollTo({ top: Math.max(0, el.offsetTop - (stickIx + 1) * 24), behavior: REDUCED ? 'auto' : 'smooth' });
  };

  /* first run: dosya seçtirmek yerine ayarlara yönlendir — başlamak için birden çok ayar gerekiyor */
  if (!workspace) {
    const checklist = [
      ['Repository root', 'Not set', true],
      ['Layers', layerCount ? layerCount + ' defined' : 'Optional', false],
    ];
    return (
      <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 10, background: 'var(--surface-base)', padding: 24 }}>
        <div style={{ fontSize: 'var(--text-md)', fontWeight: 600, color: 'var(--text-primary)' }}>Configure the workspace</div>
        <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', textAlign: 'center', maxWidth: 310, lineHeight: 'var(--leading-snug)' }}>
          Set the repository root — and, if projects should be grouped, the layers. Discovery starts right after.
        </div>
        <div style={{ width: 292, border: '1px solid var(--border)', borderRadius: 'var(--radius-md)', background: 'var(--surface)', marginTop: 4 }}>
          {checklist.map(([label, value, req], i) => (
            <div key={label} style={{
              display: 'flex', alignItems: 'center', gap: 8, height: 30, padding: '0 10px',
              borderTop: i ? '1px solid var(--border-subtle)' : 'none',
            }}>
              <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-secondary)', flex: 1 }}>{label}</span>
              <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: req ? 'var(--text-secondary)' : 'var(--text-faint)' }}>{value}</span>
            </div>
          ))}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 6 }}>
          <DS.Button variant="primary" icon={<I.gear />} onClick={onOpenSettings}>Open settings</DS.Button>
          <DS.Button variant="secondary" icon={<I.upload />} onClick={onImportSettings}>Import settings…</DS.Button>
        </div>
        <div style={{ fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', marginTop: 2 }}>Import fills the form from a settings file — nothing is applied until you save.</div>
      </div>
    );
  }

  if (eng.discover) return <DiscoverList d={eng.discover} />;
  /* boş liste (uygulama InteractionText.ListEmpty): bakım işi listeyi boşalttı ya da Sync proje bulmadı */
  if (eng.task || !BO.PROJECTS.length) return <EmptyBlock icon={I_LIST} text="No projects" />;

  const q = (query || '').trim().toLowerCase();
  const match = (name) => {
    if (q && name.toLowerCase().indexOf(q) < 0) return false;
    return filterMatch(eng, name, filter);
  };
  let revealIndex = 0;

  return (
    <div ref={boxRef} className="bo-scroll" data-list-scroll="1" style={{ flex: 1, minHeight: 0, overflowY: 'auto', position: 'relative', background: 'var(--surface-base)' }}>
      <div key={revealKey}>
        {(() => {
          /* başlıklar birikerek yapışır: görünen i. katman başlığı top = i×24px'te sabitlenir;
             başlık + satırlar aynı scroll kökünün kardeşleri — böylece önceki başlıklar asılı kalır */
          const out = [];
          let stick = 0;
          groups.forEach((g, gi) => {
            const rows = g.rows.filter((p) => match(p.name));
            if (!rows.length) return;
            if (g.name) {
              const myStick = stick, firstRow = rows[0].name;
              out.push(
                <GroupHead key={'h' + gi} name={g.name} count={rows.length} top={myStick * 24}
                  onJump={() => jumpGroup(firstRow, myStick)} />
              );
              stick++;
            }
            rows.forEach((p) => {
              out.push(
                <div key={p.name} data-row-wrap="1" ref={(el) => { rowRefs.current[p.name] = el; }}>
                  <Row p={p} eng={eng} selected={selected} onSelect={onSelect} onNote={onNote}
                    onAction={onRowAction} onStop={onStopRun} busy={eng.busy()} isTarget={eng.phase === 'running' && eng.scopeName === p.name && !!eng.scope}
                    menuOpen={!!menu && menu.name === p.name} setMenu={setMenu}
                    vsOpen={!!vs && vs.name === p.name} setVs={setVs}
                    hovered={hovered} onHover={onHover} local={localEdits && !p.ext}
                    revealIndex={revealIndex++} />
                </div>
              );
            });
          });
          return out;
        })()}
        {(filter || q) && !BO.PROJECTS.some((p) => match(p.name)) && (
          <div style={{ padding: 20, fontSize: 'var(--text-xs)', color: 'var(--text-dim)' }}>No projects match this filter.</div>
        )}
      </div>
      {menu && <RowMenu pos={menu} busy={eng.busy()} onPick={onRowAction} onClose={() => setMenu(null)} />}
      {vs && <VsChooser pos={vs} onClose={closeVs} onPick={(nm) => onNote(nm + ' opened in Visual Studio')} />}
    </div>
  );
}

/* ================= KONSOL ================= */
/* boş proje sayfası (uygulama ConsoleEmptyState): satır durumundan 1–2 satır — gerekçe + kanıt; statü, yaş, sha yazılmaz */
function emptyPageLines(eng, name) {
  const st = eng.p[name];
  if (st.status === 'building') return ['No log yet — output streams here once the build starts.'];
  let reason;
  const outsideOk = st.timeMode && st.will !== 'dirty'; // zaman kipi, güncel: WillBuildReason.BuiltOutside
  if (st.status === 'skipped') {
    reason = eng._isCycle(name) && st.will === 'dirty' ? 'In a dependency cycle — this run did not compile it.' : 'Up to date — nothing to compile in this run.';
  } else if (st.status === 'succeeded') reason = 'Built in this run — its log is no longer on disk.';
  else if (st.status === 'failed') reason = 'Failed in this run — its log is no longer on disk.';
  else if (st.will === 'unknown') reason = 'Not analysed yet — run Sync to see what this project will do.';
  else {
    const queued = st.status === 'queued';
    if (st.will !== 'dirty' && !queued) reason = outsideOk ? 'Up to date — built outside this tool.' : 'Up to date — nothing to compile.';
    else {
      const head = queued ? 'Queued' : 'Will build';
      const r = st.reason || (st.lastBuiltAt == null ? 'neverBuilt' : null);
      /* uygulama ConsoleEmptyState.Pending: SignatureChanged configuration'ı adlandırır; OutputStale'de bağımlılığın arkasındaki proje */
      reason = r === 'neverBuilt' ? head + ' — this tool has never built it.'
        : r === 'lastFailed' ? head + ' — it failed at this source.'
        : r === 'config' ? head + ' — it was last built in ' + st.builtCfg + '.'
        : r === 'changed' || r === 'dependency' ? head + ' — the signature changed since the last successful build.'
        : r === 'behind' ? head + ' — one of its dependencies will be rebuilt first.'
        : r === 'newer' ? head + ' — its files are newer than its build output.'
        : r === 'outputMissing' ? head + ' — its build output is missing.'
        : head + ' in this run.';
    }
  }
  /* kanıt satırı (Evidence): çıktı dışarıda üretilmiş ve güncel → 'Built outside this tool'; defterde kaydı yok → 'Never built by this tool'.
     Gerekçe aynı şeyi söylüyorsa tekrar edilmez (RepeatsReason) */
  if (/built outside this tool\.$/.test(reason) || /never built it\.$/.test(reason) || /build output is missing\.$/.test(reason)) return [reason];
  if (outsideOk) return [reason, 'Built outside this tool'];
  if (st.timeMode || st.lastBuiltAt == null) return [reason, 'Never built by this tool'];
  return [reason];
}

const CONSOLE_LINE_PX = 18.6;   // mono 12 × 1.55 (LineHeight.Mono12)
const CONSOLE_IDLE_RESUME = 5000; // okuyucu jesti paneli 5 sn tutar, sonra dibe döner (BottomAnchorDecision.IdleResumeMs)
const CONSOLE_SLICE = 200;        // anlatının render dilimi (ConsoleView.RenderSliceLines)
const CONSOLE_CHUNK_TOP = 48;     // tepeye bu kadar yaklaşınca önceki dilim yüklenir (ChunkTopThresholdPx)
function ConsolePanel({ eng, selected, workspace }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const boxRef = React.useRef(null);
  const stick = React.useRef(true);
  const readerT = React.useRef(0); // son okuyucu jesti (dikey)
  const [away, setAway] = React.useState(false);
  const [, setGen] = React.useState(0);

  const sel = selected ? eng.p[selected] : null;
  /* anlatı 200 satırlık bir penceredir, sınır değil: takipteyken son 200 satır; okuyucu tepeye varınca (ya da Ctrl+Home / Home)
     önceki 200'lük dilim bellekteki geçmişten eklenir ve görünüm yerinde kalır. Proje sayfası penceresizdir — logun tamamı. */
  const fromId = React.useRef(null);   // belgede yüklü ilk anlatı satırının id'si
  const armed = React.useRef(false);   // yükleyici tepeden uzaklaşınca silahlanır
  const prependFix = React.useRef(null); // ekleme öncesi scrollHeight — görünüm telafisi
  const N = eng.narrative;
  let start = 0;
  if (!sel) {
    const ix = fromId.current == null || stick.current ? -1 : N.findIndex((l) => l.id === fromId.current);
    start = ix >= 0 ? ix : Math.max(0, N.length - CONSOLE_SLICE);
    fromId.current = N.length ? N[start].id : null;
  }
  const lines = sel ? sel.log : N.slice(start);
  const loadPrevious = () => {
    const c = boxRef.current;
    if (!c || sel || start <= 0) return false;
    const from = Math.max(0, start - CONSOLE_SLICE);
    fromId.current = N[from].id;
    prependFix.current = { h: c.scrollHeight, top: c.scrollTop };
    armed.current = false;
    stick.current = false;
    setAway(true); // yeniden çizim
    return true;
  };

  /* her mod okunduğu uçta sabitlenir: anlatı dipte (takip eder), proje sayfası tepede (asla takip etmez — ilk hata aranır) */
  const jumping = React.useRef(false); // programatik kaydırma penceresi
  /* geçiş yönü mod değişiminde bir kez oturur: Back dönüşü yukarıdan kayar (ayna), açılış aşağıdan */
  const prevSel = React.useRef(selected), dirRef = React.useRef(false);
  if (prevSel.current !== selected) { dirRef.current = !!prevSel.current && !selected; prevSel.current = selected; }
  const fromAbove = dirRef.current;
  React.useLayoutEffect(() => {
    const c = boxRef.current;
    stick.current = !selected;
    readerT.current = 0;
    if (c) { c.scrollTop = selected ? 0 : c.scrollHeight; c.scrollLeft = 0; }
    armed.current = !!c && c.scrollTop > CONSOLE_CHUNK_TOP; // takas sonrası: tepeden uzaksa silahlı
  }, [selected]);
  /* eklenen dilim kadar aşağı kaydır: okuyucunun gördüğü satır yerinde kalır (ChunkStitch.CompensatedOffset) */
  React.useLayoutEffect(() => {
    const c = boxRef.current, f = prependFix.current;
    if (!c || !f) return;
    prependFix.current = null;
    c.scrollTop = f.top + (c.scrollHeight - f.h);
    armed.current = c.scrollTop > CONSOLE_CHUNK_TOP;
  });

  const measureAway = () => {
    const c = boxRef.current;
    if (!c) return;
    const at = c.scrollHeight - c.scrollTop - c.clientHeight < 48;
    if (away !== !at) setAway(!at);
  };
  React.useEffect(() => {
    const c = boxRef.current;
    if (!c) return;
    if (!selected && stick.current && !jumping.current) c.scrollTop = c.scrollHeight;
    measureAway();
  });
  /* 5 sn boşta dönüş: anlatıda okuyucu bıraktıysa süre dolunca dibe dönüp takibe geçer */
  React.useEffect(() => {
    if (selected) return undefined;
    const iv = setInterval(() => {
      const c = boxRef.current;
      if (!c || stick.current || jumping.current || !readerT.current) return;
      if (Date.now() - readerT.current >= CONSOLE_IDLE_RESUME) { stick.current = true; readerT.current = 0; c.scrollTop = c.scrollHeight; measureAway(); }
    }, 250);
    return () => clearInterval(iv);
  }); // eslint-disable-line react-hooks/exhaustive-deps

  const onScroll = () => {
    const c = boxRef.current;
    if (!c || jumping.current) return; // programatik kaydırma sürerken pozisyonu okuma
    const at = c.scrollHeight - c.scrollTop - c.clientHeight < 48;
    if (!selected) { stick.current = at; if (!at) readerT.current = Date.now(); }
    setAway(!at);
    /* yükleyici: tepeden uzakta silahlanır, silahlıyken tepeye varış önceki dilimi getirir */
    if (!selected) {
      if (c.scrollTop > CONSOLE_CHUNK_TOP) armed.current = true;
      else if (armed.current && start > 0) { loadPrevious(); setGen((g) => g + 1); }
    }
  };
  /* ⌄ latest bir kez dibe atlar; proje sayfasında orada bırakır (takip açılmaz) */
  const jump = () => {
    const c = boxRef.current;
    if (!c) return;
    setAway(false);
    if (!selected) { stick.current = true; setGen((g) => g + 1); } // dibe dönüş takibi açar: pencere yeniden son 200 satır
    if (REDUCED) { stick.current = !selected; c.scrollTop = c.scrollHeight; return; }
    jumping.current = true;
    c.scrollTo({ top: c.scrollHeight, behavior: 'smooth' });
    setTimeout(() => { jumping.current = false; stick.current = !selected; readerT.current = 0; }, 280);
  };
  /* gezinme tuşları görünümü kaydırır: PageUp/Down bir viewport, ↑/↓ bir satır, ←/→ yana, Home tepe + sol, End dip */
  const onKey = (e) => {
    const c = boxRef.current;
    if (!c) return;
    const step = { PageUp: -c.clientHeight, PageDown: c.clientHeight, ArrowUp: -CONSOLE_LINE_PX, ArrowDown: CONSOLE_LINE_PX }[e.key];
    if (step != null) { e.preventDefault(); c.scrollTop += step; }
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') { e.preventDefault(); c.scrollLeft += (e.key === 'ArrowLeft' ? -1 : 1) * (e.ctrlKey ? 160 : 40); return; }
    else if (e.key === 'Home') { e.preventDefault(); c.scrollTop = 0; c.scrollLeft = 0; }
    else if (e.key === 'End') { e.preventDefault(); c.scrollTop = c.scrollHeight; }
    else return;
    if (!selected) readerT.current = Date.now();
  };

  let body;
  if (!workspace) {
    body = (
      <div style={{ color: 'var(--text-faint)', display: 'flex', alignItems: 'center', gap: 8 }}>
        <Cursor /> Waiting for a workspace
      </div>
    );
  } else if (sel) {
    body = lines.length
      ? <RevealLog key={selected} lines={lines} />
      : (
        <div key={'e' + selected} className={REDUCED ? undefined : 'bo-slide-up'}>
          {emptyPageLines(eng, selected).map((t) => <DS.ConsoleLine key={t} type="dim">{t}</DS.ConsoleLine>)}
        </div>
      );
  } else {
    body = (
      <>
        <RevealLog key="narrative" lines={lines} fromAbove={fromAbove} />
        {/* 'ready' yalnız anlatı boşken (ConsoleView.ShowReady/ClearReadyText): ilk satır gelince yalnız imleç kalır */}
        <NarrLine type="dim" cursor>{lines.length === 0 && (eng.phase === 'idle' || eng.phase === 'boot') ? 'ready' : ''}</NarrLine>
      </>
    );
  }

  return (
    <div style={{ flex: 1, minHeight: 0, position: 'relative', display: 'flex', flexDirection: 'column' }}>
      <div ref={boxRef} onScroll={onScroll} onKeyDown={onKey} tabIndex={0} className="bo-scroll" style={{
        flex: 1, minHeight: 0, overflow: 'auto', background: 'var(--console-bg)', outline: 'none',
        borderRadius: 0, padding: '8px 12px 14px', boxSizing: 'border-box', cursor: 'default',
        fontWeight: 300, /* yoğun metin: Geist Mono Light — ince ama okunur */
      }}>
        {/* satırlar sarmaz: içerik en uzun satır kadar genişler, panel yatay kayar */}
        <div style={{ width: 'max-content', minWidth: '100%' }}>{body}</div>
      </div>
      <LatestPill show={away} onClick={jump} />
    </div>
  );
}

/* ================= ÖZET STREAM ================= */
/* satır üç kolon: saat · 12px glyph · metin. Metin kendi kolonunda sarar (devamı metnin başının altında);
   saat ve glyph ilk satırın 24px bandında kalır. Sığan satır 24px. Türler (uygulama StreamKind):
   ok · fail · skip · sync · info · done · warn — warn amber ▸ + amber metin (git reddi, kilitli kopya notu). */
const STREAM_BAND = 24;
function StreamCols({ time, timeColor, icon, children, style, ...rest }) {
  return (
    <div {...rest} style={{
      display: 'flex', alignItems: 'flex-start', gap: 8, minHeight: STREAM_BAND, padding: '0 10px', position: 'relative',
      fontFamily: MONO, fontSize: 'var(--text-xs)', fontVariantNumeric: 'tabular-nums', userSelect: 'none', ...style,
    }}>
      <span style={{ color: timeColor || 'var(--text-faint)', flex: 'none', height: STREAM_BAND, lineHeight: STREAM_BAND + 'px', whiteSpace: 'nowrap' }}>{time}</span>
      <span style={{ width: 12, flex: 'none', height: STREAM_BAND, display: 'inline-flex', alignItems: 'center', justifyContent: 'center' }}>{icon}</span>
      <span style={{ flex: 1, minWidth: 0, padding: '3px 0', lineHeight: '18px', whiteSpace: 'normal', overflowWrap: 'anywhere' }}>{children}</span>
    </div>
  );
}
function StreamRow({ ev, eng, selected, onSelect, typing, onTypeDone }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [hover, setHover] = React.useState(false);
  const c = eng.counts();
  const glyph = ev.kind === 'ok' ? 'succeeded' : ev.kind === 'fail' ? 'failed' : ev.kind === 'skip' ? 'skipped'
    : ev.kind === 'done' ? (c.failed ? 'failed' : 'succeeded') : null;
  const isSel = ev.project && ev.project === selected;
  const clickable = !!ev.project;
  const color = ev.kind === 'fail' ? 'var(--status-fail-text)'
    : ev.kind === 'skip' ? 'var(--text-dim)'
    : ev.kind === 'warn' ? 'var(--amber-text)'
    : ev.kind === 'done' ? (c.failed ? 'var(--status-fail-text)' : 'var(--status-success-text)')
    : ev.kind === 'sync' || ev.kind === 'info' ? 'var(--text-dim)' : 'var(--text-secondary)';
  return (
    <StreamCols time={ev.time}
      icon={glyph ? <DS.StatusGlyph status={glyph} size={12} /> : <span style={{ color: 'var(--amber-text)', lineHeight: 1 }}>▸</span>}
      onClick={clickable ? () => onSelect(isSel ? null : ev.project) : undefined}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      className={ev.kind === 'done' && !c.failed && !REDUCED ? 'bo-glow-once' : undefined}
      style={{
        color, cursor: clickable ? 'pointer' : 'default',
        /* hover her satırda var (izleme kolaylığı); tıklanabilir satır bir adım daha açılır */
        background: isSel ? 'var(--surface-raised)' : hover ? (clickable ? 'var(--surface-hover)' : 'var(--surface)') : 'transparent',
        transition: 'background var(--duration-fast) var(--ease-standard)',
      }}>
      {isSel && <span style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: 2, background: 'var(--amber)' }}></span>}
      {/* hata ve bitiş özetleri animasyonsuz basılır — kötü haber ve sonuç beklemez */}
      {typing ? <TypingLine key={'t' + ev.id} text={ev.text} instant={ev.kind === 'fail' || ev.kind === 'done'} onDone={onTypeDone} /> : ev.text}
    </StreamCols>
  );
}

function StreamPanel({ eng, selected, onSelect, workspace }) {
  const boxRef = React.useRef(null);
  const stick = React.useRef(true);
  const [away, setAway] = React.useState(false);
  const jumping = React.useRef(false); // programatik smooth-scroll penceresi
  const evs = workspace ? eng.stream.slice(-150) : [];
  const active = workspace ? eng.activeLine : null;
  const activeProject = active ? active.text.split(' ')[0] : null;

  /* en yeni satırın daktilosu bitene dek prompt gizli — konsolla aynı yaşam döngüsü */
  const [pendingId, setPendingId] = React.useState(null);
  const prevNewest = React.useRef(null);
  const newestId = evs.length ? evs[evs.length - 1].id : null;
  const newestInstant = evs.length ? evs[evs.length - 1].instant : true;
  React.useEffect(() => {
    if (newestId == null) { prevNewest.current = null; return; }
    if (prevNewest.current != null && newestId !== prevNewest.current && !newestInstant && !REDUCED) setPendingId(newestId);
    prevNewest.current = newestId;
  }, [newestId]); // eslint-disable-line

  React.useEffect(() => {
    const c = boxRef.current;
    if (c && stick.current && !jumping.current) { c.scrollTop = c.scrollHeight; if (away) setAway(false); }
  });
  const onScroll = () => {
    const c = boxRef.current;
    if (!c || jumping.current) return;
    const at = c.scrollHeight - c.scrollTop - c.clientHeight < 48;
    stick.current = at;
    setAway(!at);
  };
  const jump = () => {
    const c = boxRef.current;
    if (!c) return;
    setAway(false);
    if (REDUCED) { stick.current = true; c.scrollTop = c.scrollHeight; return; }
    jumping.current = true;
    c.scrollTo({ top: c.scrollHeight, behavior: 'smooth' });
    setTimeout(() => { jumping.current = false; stick.current = true; }, 560);
  };

  return (
    <div style={{ flex: 1, minHeight: 0, position: 'relative', display: 'flex', flexDirection: 'column' }}>
      <div ref={boxRef} onScroll={onScroll} className="bo-scroll" style={{ flex: 1, minHeight: 0, overflowY: 'auto', background: 'var(--surface-base)', padding: '4px 0' }}>
        {evs.map((ev) => (
          <StreamRow key={ev.id} ev={ev} eng={eng} selected={selected} onSelect={onSelect}
            typing={ev.id === pendingId}
            onTypeDone={() => setPendingId((cur) => (cur === ev.id ? null : cur))} />
        ))}
        {active && (
          <StreamCols time={eng.wall()} icon={<Cursor still style={{ background: 'var(--amber-text)' }} />}
            onClick={() => onSelect(selected === activeProject ? null : activeProject)}
            style={{ color: 'var(--amber-text)', cursor: 'pointer' }}>
            <TypingLine key={active.id} text={active.text} instant={false} />
          </StreamCols>
        )}
        {/* bekleyen prompt: saat + imleç (amber dinlenme tonu) — workspace yokken de; 'No events yet.' yok */}
        {!active && pendingId == null && (
          <StreamCols time={eng.wall()} icon={<Cursor still style={{ background: 'var(--amber-text)' }} />} style={{ whiteSpace: 'nowrap' }} />
        )}
      </div>
      <LatestPill show={away} onClick={jump} />
    </div>
  );
}

/* ================= STICKY ŞERİT + GLOBAL PROGRESS =================
   Sol başta kalıcı işlem pill'i (BUILD · REBUILD — Sales.Core · CLEAN…): koşarken amber,
   bitince nötr; bir sonraki işleme dek durur. Hata kümesi sadeleşti: ilk 3 çip + "+N more". */
function StickyStrip({ eng, onSelect, onFilterFailed, workspace, exitPending }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const c = eng.counts();
  const di = eng.depIssueCount();
  const wb = eng.willBuild.size;
  const fin = eng.finishedOfWB();
  const failed = eng.failedList();
  const wn = eng.phase === 'done' ? eng.warnings() : 0;

  /* Satır önceliği uygulamanın RibbonText.Compose'u: motor ölümü → sessizlik → çıkış bekleyişi → uyanma →
     workspace yok → uçuştaki workspace işi → faz satırı. İş satırları ikincil metin, glyph yok. */
  let text, color = 'var(--text-secondary)', glyph = null, spin = false;
  const S = eng.syncStep;
  const syncLine = !S || S.step === 'start' ? '▸ Sync…'
    : S.step === 'fetching' ? '▸ Sync — fetching origin/' + (eng.branchName || 'main') + '…'
    : S.step === 'analyzing' ? (S.found > 1 ? '▸ Sync — analyzing ' + S.found + ' projects…' : S.found === 1 ? '▸ Sync — analyzing 1 project…' : '▸ Sync — analyzing the workspace…')
    : '▸ Sync — indexing source files · ' + S.done + '/' + S.total;
  const W = eng.work;
  const workLine = W ? (W.kind === 'checkout' ? '▸ Switching to ' + W.target + '…'
      : W.kind === 'pull' ? '▸ Pulling origin/' + W.target + '…'
      : '▸ Creating the worktree…')
    : eng.task ? (eng.task.kind === 'optimize' ? '▸ Optimize — repairing the workspace…' : '▸ Deep clean — removing bin and obj…')
    : null;
  const inFlight = c.building;
  const stoppingLine = (n) => n > 0 ? `▸ Stopping — ${fin}/${wb} · finishing ${n} in flight` : '▸ Stopping — wrapping up';
  if (eng.engine === 'dead') { text = eng.engineMsg; color = 'var(--status-fail-text)'; glyph = 'failed'; }
  else if (eng.engine === 'silent') { text = eng.engineMsg; color = 'var(--amber-text)'; }
  else if (exitPending) { text = stoppingLine(eng.phase === 'stopping' ? inFlight : 0); }
  else if (eng.engine === 'waking') { text = '▸ Starting the engine…'; }
  else if (!workspace) { text = 'Not configured — repository root not set'; color = 'var(--text-faint)'; }
  else if (workLine) { text = workLine; }
  else if (eng.resolveRun) {
    const R = eng.resolveRun;
    const t = fmtElapsed(eng.simT - R.startT);
    /* tur sayısını motor belirler (tavan 3); döngü üyeleri koşarken tur, öncesinde bayat upstream hazırlığı */
    text = R.curCyc || R.pass > 1 ? `▸ Resolving cycles · round ${R.curPass || R.pass}/3 · ${R.fin}/${R.total} · ${t}`
      : `▸ Resolving cycles · preparing dependencies · ${R.fin}/${R.total} · ${t}`;
    spin = true;
  }
  else if (eng.phase === 'marking') { text = '▸ Starting — resolving what to build'; }
  else if (eng.phase === 'boot') { text = '▸ Waiting for Sync — project states appear after Sync'; color = 'var(--text-dim)'; }
  else if (eng.phase === 'syncing') { text = syncLine; }
  else if (eng.phase === 'idle') {
    text = eng._fastCheck() ? '▸ Ready — everything looks up to date' : `▸ Ready — ${wb} to build · ${BO.PROJECTS.length - wb} up to date`;
  } else if (eng.phase === 'running') {
    if (eng._fastCheck() && !eng.cleanRun) { text = '▸ Checking — scanning for changes…'; }
    else if (eng._cycleRoundLine()) {
      /* düz Build/Rebuild bir döngü grubunu turlarla derlerken şerit turu yazar (RibbonText: cycleRound > 0) */
      const R = eng._cycleRoundLine();
      text = `▸ Resolving cycles · round ${R.round}/${R.cap} · ${fin}/${wb} · ${fmtElapsed(eng.elapsed())}`;
      spin = true;
    } else {
      const eta = eng.eta();
      const etaTxt = eta != null && c.building + c.queued > 0 ? (eta < 4000 ? ' · almost done' : ' · ~' + Math.max(5, Math.round(eta / 5000) * 5) + 's left') : '';
      text = `▸ ${eng.cleanRun ? 'Cleaning' : 'Building'} ${fin}/${wb} · ${fmtElapsed(eng.elapsed())}${etaTxt}`;
    }
  } else if (eng.phase === 'stopping') { text = stoppingLine(inFlight); }
  else if (eng.phase === 'stopped') { text = `▸ Stopped — ${fin}/${wb} · ${wb - fin} not built`; color = 'var(--text-dim)'; }
  else if (eng.phase === 'done') {
    if (eng._fastCheck() && !eng.cleanRun) { text = `Everything up to date — ${BO.PROJECTS.length} projects checked in ${BO.fmtDur(eng.checkDur)}, nothing to build`; color = 'var(--status-success-text)'; glyph = 'succeeded'; }
    else if (c.failed) { text = `Completed — ${c.failed} failed · ${c.succeeded} succeeded${di ? ` (${di} dependency-affected)` : ''} · ${c.skipped} skipped${wn ? ` · ${wn} warnings` : ''} · ${fmtElapsed(eng.elapsed())}`; color = 'var(--status-fail-text)'; glyph = 'failed'; }
    else { text = `Completed — ${c.succeeded} succeeded · ${c.skipped} skipped${wn ? ` · ${wn} warnings` : ''} · ${fmtElapsed(eng.elapsed())}`; color = 'var(--status-success-text)'; glyph = 'succeeded'; }
  }

  const opLabel = eng.opLabel();
  const opLive = eng.busy();
  const building = eng.buildingList();
  /* çubuk: görünür Sync, Starting ve workspace işi boyunca belirsiz; ilk indekste amber done/total dolar */
  const indexing = eng.phase === 'syncing' && S && S.step === 'indexing';
  const sweep = !indexing && (eng.phase === 'syncing' || eng.phase === 'marking' || !!workLine || eng.engine === 'waking');
  const progress = indexing ? (S.done / S.total) * 100
    : eng.resolveRun ? (eng.resolveRun.fin / eng.resolveRun.total) * 100
    : eng._fastCheck() && !eng.cleanRun
    ? (eng.phase === 'done' ? 100 : (c.skipped / BO.PROJECTS.length) * 100)
    : wb ? (fin / wb) * 100 : 0;
  const pStatus = indexing || eng.resolveRun ? 'building'
    : c.failed ? 'failed' : (eng.phase === 'done' ? 'succeeded' : 'building');

  return (
    <div style={{ flex: 'none', background: 'var(--surface-base)', borderBottom: '1px solid var(--border-subtle)', userSelect: 'none' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, height: 32, padding: '0 12px' }}>
        {workspace && opLabel && (
          <span style={{
            display: 'inline-flex', alignItems: 'center', gap: 6, height: 19, padding: '0 7px', flex: 'none', maxWidth: 280,
            borderRadius: 'var(--radius-xs)', fontFamily: MONO, fontSize: 'var(--text-2xs)',
            letterSpacing: '0.04em', whiteSpace: 'nowrap',
            border: '1px solid ' + (opLive ? 'var(--amber-border)' : 'var(--border-strong)'),
            background: opLive ? 'var(--amber-soft)' : 'transparent',
            color: opLive ? 'var(--amber-text)' : 'var(--text-dim)',
            transition: 'background 280ms var(--ease-standard), color 280ms var(--ease-standard), border-color 280ms var(--ease-standard)',
          }}>
            <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{opLabel}</span>
            {opLive ? <BuildingSpin size={11} /> : glyph ? <DS.StatusGlyph status={glyph} size={11} /> : null}
          </span>
        )}
        {!opLabel && glyph && <DS.StatusGlyph status={glyph} size={13} />}
        {!opLabel && spin && <BuildingSpin size={13} />}
        <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', fontVariantNumeric: 'tabular-nums', color, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{text}</span>
        <span style={{ display: 'flex', gap: 4, alignItems: 'center', minWidth: 0, overflow: 'hidden' }}>
          {building.slice(0, 4).map((n) => (
            <DS.Chip key={n} icon={<BuildingSpin size={10} />} label={BO.shortName(n)}
              onClick={() => onSelect(n)} style={{ height: 20, padding: '0 6px', fontSize: 'var(--text-2xs)' }} />
          ))}
          {building.length > 4 && <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>+{building.length - 4}</span>}
        </span>
        <span style={{ flex: 1 }}></span>
        {failed.length > 0 && (
          <span style={{ display: 'flex', gap: 4, alignItems: 'center', overflow: 'hidden' }}>
            {failed.slice(0, 3).map((n) => (
              <DS.Chip key={n} icon={<DS.StatusGlyph status="failed" size={10} />} label={BO.shortName(n)}
                onClick={() => onSelect(n)} style={{ height: 20, padding: '0 6px', fontSize: 'var(--text-2xs)' }} />
            ))}
            {failed.length > 3 && (
              <DS.Chip label={'+' + (failed.length - 3) + ' more'} onClick={onFilterFailed}
                style={{ height: 20, padding: '0 6px', fontSize: 'var(--text-2xs)', color: 'var(--status-fail-text)' }} />
            )}
          </span>
        )}
      </div>
      <DS.ProgressBar value={progress} status={pStatus} height={2}
        indeterminate={sweep}
        style={{ borderRadius: 0, background: 'var(--surface)' }} />
    </div>
  );
}

/* ================= KAYNAK ŞERİDİ (1.25.0) =================
   Title bar'ın TAM ORTASINDA, aracın makineye o anki maliyeti: CPU · RAM · paralellik + mod.
   Üç hücre; her hücre 12px ikon (text-faint) + mono 11px değer (text-dim). Üçüncü hücre
   "o an koşan / modun izin verdiği" paralelliği ve mod sözcüğünü birlikte taşır: boşta `0/4 Balanced`,
   derlerken canlı `4/4 Balanced` → `1/4 Balanced`; motor belleğe göre kısmışsa en çok `2/4 Balanced`.
   SABİT ÇERÇEVE: her hücre en geniş değer kadar yer ayırır (görünmez "hayalet" metin — "100%",
   "12.3 GB", "12/12 Balanced"); değer ikonun yanında sola hizalı, değişince hiçbir şey kaymaz,
   geçiş animasyonu yok. Ölçüm 3 sn'de bir örneklenir (gerçek uygulamada perf sayaçları);
   paralellik + mod uygulama durumudur, anında güncellenir. Okunamayan değer "—".
   Şerit yalnız OKUR: tıklanmaz, hover zemini yok; tooltip'ler aşağı açılır. Mod çipi (alt bar) değişmedi. */
const PAR_DEFAULT = { Full: 6, Balanced: 4, Light: 2 };
const CPU_CAP = { Full: null, Balanced: 70, Light: 40 }; // Full: cap yok
/* motor sayıyı makineye göre kısmışsa (düşük bellek): varsayılanın yarısı — Balanced 4 → 2 */
const fittedPar = (mode, reduced) => reduced ? Math.max(1, Math.floor(PAR_DEFAULT[mode] / 2)) : PAR_DEFAULT[mode];
const fmtMem = (mb) => mb == null ? '—' : mb < 1024 ? Math.round(mb) + ' MB' : (mb / 1024).toFixed(1) + ' GB';

/* Prototip örnekleyicisi — gerçek uygulamada Process sayaçlarından okunur (app + engine + derleyici süreçleri).
   Rakamlar brief'teki durumlara kalibre: boşta 1% · 310 MB; 4 paralel 69% · 4.8 GB; kısılmış 2/4 52% · 3.1 GB. */
function sampleResources(eng, mode, reduced) {
  const c = eng.counts();
  const live = eng.busy() || eng.phase === 'marking';
  const n = Math.min(c.building || 0, fittedPar(mode, reduced)); // o an koşan derleyici sayısı
  const perCpu = reduced ? 25.5 : 17, perMem = reduced ? 1380 : 1120; // kısılmışken projeler daha büyük/ağır
  const app = live ? 310 : 225, engine = 85, compilers = Math.round(n * perMem);
  const cap = CPU_CAP[mode];
  const cpu = n === 0 ? 1 : Math.min(cap || 97, Math.round(1 + n * perCpu));
  return { cpu, app, engine, compilers, total: app + engine + compilers, cap };
}

function ResCell({ icon, ghost, tip, children }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  return (
    <DS.Tooltip content={tip} side="bottom">
      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, cursor: 'default', pointerEvents: 'auto' }}>
        <span aria-hidden="true" style={{ display: 'inline-flex', color: 'var(--text-faint)', flex: 'none' }}>{icon}</span>
        <span style={{ display: 'inline-grid', fontFamily: MONO, fontSize: 'var(--text-2xs)', lineHeight: 1, fontVariantNumeric: 'tabular-nums', color: 'var(--text-dim)', whiteSpace: 'nowrap' }}>
          <span aria-hidden="true" style={{ gridArea: '1 / 1', visibility: 'hidden' }}>{ghost}</span>
          <span style={{ gridArea: '1 / 1', justifySelf: 'start' }}>{children}</span>
        </span>
      </span>
    </DS.Tooltip>
  );
}

function ResourceStrip({ eng, mode, reduced }) {
  const [s, setS] = React.useState(null); // null = henüz örnek yok → "—"
  React.useEffect(() => {
    const take = () => setS(sampleResources(eng, mode, reduced));
    /* uygulama: ilk örnek 0.5 sn sonra, ardından 3 sn'de bir */
    const t0 = setTimeout(take, 500);
    const iv = setInterval(take, 3000);
    return () => { clearTimeout(t0); clearInterval(iv); };
  }, [eng, mode, reduced]);
  /* paralellik hücresi (uygulama 1.11.2, ResourceStripText): `derlenen/koşunun paralelliği Mod · modun sınırı`.
     Orta sayı koşunun oturtulmuş paralelliği (koşu dışında 0); koşu sürerken mod sözcüğü koşunun başladığı moddur. */
  const inRun = (eng.phase === 'running' || eng.phase === 'stopping') && eng.runPar != null;
  const runPar = inRun ? eng.runPar : 0;
  const shownMode = inRun && eng.runMode ? eng.runMode : mode;
  const limit = PAR_DEFAULT[shownMode];
  const now = inRun ? Math.min(eng.counts().building || 0, runPar) : 0;
  const isFit = inRun && reduced && runPar < limit;
  const cap = CPU_CAP[shownMode];
  const faint = { color: 'var(--text-faint)' };
  const modeWord = (txt) => <span style={{ fontFamily: 'var(--font-sans)', color: 'var(--text-faint)', marginLeft: 5 }}>{txt}</span>;
  const limitPart = (n) => <><span style={{ fontFamily: 'var(--font-sans)', ...faint, whiteSpace: 'pre' }}>{' · '}</span><span style={faint}>{n}</span></>;
  return (
    <span data-bo-resources="1" aria-label="Resource usage" style={{
      position: 'absolute', left: '50%', top: 0, bottom: 0, transform: 'translateX(-50%)',
      display: 'inline-flex', alignItems: 'center', gap: 14, whiteSpace: 'nowrap', pointerEvents: 'none',
    }}>
      <ResCell icon={<I.cpu />} ghost="100%" tip={'CPU used by Build Orchestrator — app, engine and compilers' + (cap ? ' · cap ' + cap + '%' : '')}>
        {s ? s.cpu + '%' : '—'}
      </ResCell>
      <ResCell icon={<I.memory />} ghost="12.3 GB" tip={'Memory used by Build Orchestrator · app ' + fmtMem(s && s.app) + ' · engine ' + fmtMem(s && s.engine) + ' · compilers ' + fmtMem(s && s.compilers)}>
        {s ? fmtMem(s.total) : '—'}
      </ResCell>
      <ResCell icon={<I.parallel />} ghost={<>12/12{modeWord('Balanced')}{limitPart('12')}</>}
        tip={!inRun ? shownMode + ' — up to ' + limit + ' projects build in parallel'
          : isFit ? now + ' of ' + runPar + ' — reduced: 3 GB free memory'
          : now + ' of ' + runPar + ' projects building in parallel'}>
        {now}/{runPar}
        {modeWord(shownMode)}
        {limitPart(limit)}
      </ResCell>
    </span>
  );
}

/* ================= POPOVER'LAR ================= */
function Popover({ open, width, children, align }) {
  if (!open) return null;
  return (
    <div className={REDUCED ? undefined : 'bo-pop-in'} data-bo-popover="1" style={{
      position: 'absolute', bottom: 'calc(100% + 8px)', [align === 'left' ? 'left' : 'right']: 0, width,
      background: 'var(--surface-overlay)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
      boxShadow: 'var(--elevation-overlay)', zIndex: 70, padding: 8, boxSizing: 'border-box',
    }}>
      {children}
    </div>
  );
}

/* branch popover (uygulama BranchPopover, ActionBar.xaml): 480 px, 12 satır (336 px) görünür, yazarak süzer.
   Klavye: aramadan ↓ ilk satırı işaretler, ↓/↑ taşır (sonda durur, ilkte ↑ işareti kaldırır), Enter işaretliyi seçer, yazmak işareti siler. */
const BRANCH_ROW_H = 28;
function BranchPopover({ open, branches, current, onPick }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [q, setQ] = React.useState('');
  const [mark, setMark] = React.useState(-1);
  const listRef = React.useRef(null);
  const inRef = React.useRef(null);
  React.useEffect(() => {
    if (!open) { setQ(''); setMark(-1); return; }
    const t = setTimeout(() => { const el = inRef.current && inRef.current.querySelector('input'); if (el) el.focus(); }, 0);
    return () => clearTimeout(t);
  }, [open]);
  const list = branches.filter((b) => b.name.toLowerCase().includes(q.toLowerCase()));
  React.useEffect(() => {
    const box = listRef.current; if (!box || mark < 0) return;
    const top = mark * BRANCH_ROW_H, bot = top + BRANCH_ROW_H;
    if (top < box.scrollTop) box.scrollTop = top;
    else if (bot > box.scrollTop + box.clientHeight) box.scrollTop = bot - box.clientHeight;
  }, [mark]);
  const onKey = (e) => {
    if (e.key === 'ArrowDown') { e.preventDefault(); setMark((m) => Math.min(list.length - 1, m + 1)); }
    else if (e.key === 'ArrowUp') { e.preventDefault(); setMark((m) => (m <= 0 ? -1 : m - 1)); }
    else if (e.key === 'Enter') { e.preventDefault(); if (mark >= 0 && list[mark]) onPick(list[mark]); }
  };
  return (
    <Popover open={open} width={480}>
      <div style={{ fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)', padding: '2px 4px 8px' }}>Switch branch</div>
      <span ref={inRef} style={{ position: 'relative', display: 'flex' }}>
        <DS.Input placeholder="Search branches…" prefix={<I.search />} value={q} spellCheck={false}
          onChange={(e) => { setQ(e.target.value); setMark(-1); }} onKeyDown={onKey} style={{ paddingRight: q ? 28 : 8 }} />
        {q && (
          <button type="button" aria-label="Clear search" onMouseDown={(e) => e.preventDefault()}
            onClick={() => { setQ(''); setMark(-1); }}
            style={{
              position: 'absolute', right: 4, top: '50%', transform: 'translateY(-50%)', width: 20, height: 20, padding: 0,
              display: 'inline-flex', alignItems: 'center', justifyContent: 'center', border: 'none', borderRadius: 'var(--radius-xs)',
              background: 'transparent', color: 'var(--text-dim)', cursor: 'pointer',
            }}><I.close /></button>
        )}
      </span>
      <div ref={listRef} className="bo-scroll" style={{ maxHeight: 12 * BRANCH_ROW_H, overflowY: 'auto', marginTop: 6 }}>
        {list.map((b, i) => (
          <BranchRow key={b.name} b={b} sel={b.name === current} marked={i === mark} onPick={onPick} />
        ))}
        {!list.length && <div style={{ padding: 10, fontSize: 'var(--text-xs)', color: 'var(--text-dim)' }}>No branches match “{q}”.</div>}
      </div>
      <div style={{ marginTop: 6, borderTop: '1px solid var(--border-subtle)', padding: '8px 4px 2px', fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', lineHeight: 'var(--leading-snug)' }}>
        Switching checks the branch out in your working tree.
      </div>
    </Popover>
  );
}
/* satır: ikon · ad (kalan genişlik, sığmazsa …) · sha ya da 'active' rozeti; ✓ satırı = aktif branch (chip = HEAD) */
function BranchRow({ b, sel, marked, onPick }) {
  const [hover, setHover] = React.useState(false);
  return (
    <div onClick={() => onPick(b)} onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, height: BRANCH_ROW_H, padding: '0 6px', borderRadius: 'var(--radius-sm)',
        cursor: 'pointer', background: hover || marked ? 'var(--surface-raised)' : 'transparent', userSelect: 'none',
        transition: 'background var(--duration-fast) var(--ease-standard)',
      }}>
      <span style={{ display: 'inline-flex', color: sel ? 'var(--amber-text)' : 'var(--text-dim)', width: 12, flex: 'none' }}>{sel ? <I.check /> : <I.branch />}</span>
      <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', color: sel ? 'var(--text-primary)' : 'var(--text-secondary)', flex: 1, minWidth: 0, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{b.name}</span>
      {sel
        ? <span style={{ flex: 'none', fontSize: 'var(--text-2xs)', color: 'var(--amber-text)', background: 'var(--amber-soft)', border: '1px solid var(--amber-border)', borderRadius: 'var(--radius-xs)', padding: '1px 5px' }}>active</span>
        : <span style={{ flex: 'none', fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>{b.sha}</span>}
    </div>
  );
}

/* kirli ağaç kartı (uygulama BranchSwitchCard, BranchSwitchText): 360 px, dolgusuz, chip'in 8 px üstünde. Dışarı tık = Cancel. */
function BranchCard({ card, worktreeSet, onDecide }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  if (!card) return null;
  const name = (c) => (c === 'worktree' ? 'the worktree' : 'the repository');
  const other = card.copy === 'worktree' ? 'repo' : 'worktree';
  const otherLabel = card.copy === 'worktree' ? 'Use repository' : worktreeSet ? 'Use worktree' : 'Set up worktree…';
  return (
    <div role="dialog" aria-label="Uncommitted changes" data-bo-popover="1" className={REDUCED ? undefined : 'bo-pop-in'} style={{
      position: 'absolute', bottom: 'calc(100% + 8px)', right: 0, width: 360, boxSizing: 'border-box', zIndex: 70,
      background: 'var(--surface-overlay)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
      boxShadow: 'var(--elevation-overlay)', fontFamily: 'var(--font-sans)', cursor: 'default',
    }}>
      <div style={{ padding: '13px 16px 14px' }}>
        <div style={{ fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)' }}>Uncommitted changes</div>
        <div style={{ marginTop: 8, fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)', lineHeight: 'var(--leading-snug)' }}>
          {card.files === 1 ? '1 file has' : card.files + ' files have'} uncommitted changes in {name(card.copy)}.
        </div>
      </div>
      <div style={{ padding: '12px 16px 14px', borderTop: '1px solid var(--border)' }}>
        <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 1.5, textWrap: 'pretty' }}>
          Stash them and switch here, or open <span style={{ fontFamily: MONO }}>{card.branch}</span> in {name(other)} and leave this copy as it is.
        </div>
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 12 }}>
          <DS.Button size="sm" variant="secondary" onClick={() => onDecide('cancel')}>Cancel</DS.Button>
          <DS.Button size="sm" variant="secondary" onClick={() => onDecide('other')}>{otherLabel}</DS.Button>
          <DS.Button size="sm" variant="primary" onClick={() => onDecide('stash')}>Stash and switch</DS.Button>
        </div>
      </div>
    </div>
  );
}

/* ================= SPLITTER ================= */
function Splitter({ dir, onDrag, onEnd }) {
  const [active, setActive] = React.useState(false);
  const vert = dir === 'v'; // dikey çizgi (kolonlar arası)
  return (
    <div
      onPointerDown={(e) => {
        e.preventDefault();
        setActive(true);
        e.currentTarget.setPointerCapture(e.pointerId);
      }}
      onPointerMove={(e) => { if (active) onDrag(e); }}
      onPointerUp={(e) => { setActive(false); e.currentTarget.releasePointerCapture(e.pointerId); onEnd && onEnd(); }}
      style={{
        flex: 'none', position: 'relative', zIndex: 5,
        width: vert ? 7 : 'auto', height: vert ? 'auto' : 7, alignSelf: 'stretch',
        cursor: vert ? 'col-resize' : 'row-resize', touchAction: 'none',
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        margin: vert ? '0 -3px' : '-3px 0',
      }}>
      <span style={{
        display: 'block', background: active ? 'var(--amber-border)' : 'var(--border)',
        width: vert ? 1 : '100%', height: vert ? '100%' : 1,
        transition: 'background var(--duration-fast) var(--ease-standard)',
      }}></span>
    </div>
  );
}

/* ================= SAHNE TANIMLARI ================= */
// Yalnız ekran etiketi için ad; sahnelerin açıklaması tek yerde, Main.dc.html'in scenes() listesinde.
const SCENES = [
  { id: 1, label: 'Hero' },
  { id: 2, label: 'Detail' },
  { id: 3, label: 'Failure' },
  { id: 4, label: 'Up to date' },
  { id: 5, label: 'First run' },
  { id: 6, label: 'Ready' },
  { id: 7, label: 'Graph focus' },
  { id: 8, label: 'Cycle' },
];

/* ================= AYARLAR — katman tanımları ================= */
const DEFAULT_LAYER_CFG = []; // varsayılan: katman yok — projeler tek liste
/* Yeni katman satırlarında gösterilen ÖRNEKLER (1.19.0) — ürüne özel ad yok, standart katman iskeleti.
   Değer değil placeholder: Add layer boş satır ekler, kullanıcı kendi adını/desenini yazar. */
const LAYER_PLACEHOLDERS = [
  ['Core', '^MyApp\\.(Core|Common)\\.'],
  ['Infrastructure', '^MyApp\\.(Data|Infrastructure)\\.'],
  ['Domain', '^MyApp\\.Domain\\.'],
  ['Services', '^MyApp\\.Services\\.'],
  ['Api', '\\.Api$'],
  ['Client', '^MyApp\\.(Web|Client|Mobile)\\.'],
];
const layerPh = (i) => LAYER_PLACEHOLDERS[i % LAYER_PLACEHOLDERS.length];

/* ---- Ayarlar → General: checkbox/switch ağırlığındaki ayarların tek evi (1.19.0).
   Yeni ayar eklemek = bu listeye bir satır; 'pull' kendi kalıcılık anahtarında yaşar (dep = üstteki anahtara bağlı). ---- */
const SET_SECTIONS = [
  { id: 'general', label: 'General' },
  { id: 'workspace', label: 'Workspace' },
  { id: 'external', label: 'External projects' },
  { id: 'layers', label: 'Layers' },
];
/* uygulama GeneralSettingsCatalog: altı grup, satır = etiket · açıklama · varsayılan (exp = Export/Import anahtarı) */
const GENERAL_GROUPS = [
  { title: 'Startup', rows: [
    { key: 'startWin', exp: 'startWithWindows', label: 'Start with Windows', desc: 'Launch when you sign in to Windows.', def: false,
      on: 'Start with Windows on — the app starts when you sign in to Windows', off: 'Start with Windows off — signing in to Windows no longer starts the app' },
    { key: 'startTray', exp: 'startMinimizedToTray', label: 'Start minimized to tray', desc: 'No window on start — the tray icon brings it back.', dep: 'startWin', def: false,
      on: 'Start minimized to tray on — signing in to Windows starts the app in the tray, without a window', off: 'Start minimized to tray off — signing in to Windows opens the window' },
    { key: 'closeTray', exp: 'closeToTray', label: 'Close to tray', desc: 'Closing the window leaves the app running in the tray.', def: true,
      on: 'Close to tray on — closing the window keeps the app running in the tray', off: 'Close to tray off — closing the window quits the app' },
  ] },
  { title: 'Build', rows: [
    { key: 'pull', exp: 'pullExternalBeforeBuild', label: 'Pull before build', desc: 'Update every external working copy first — a fast-forward-only git pull, one per copy.', def: true },
    { key: 'replace', exp: 'replaceFilesInUse', label: 'Replace files in use', desc: 'When a post-build copy finds its file held by a running program, move the old file aside and copy again; the program keeps the old build until it restarts. Off: the copy stops there and the console names the program.', def: false,
      on: 'Replace files in use on — a file a running program holds is moved aside and copied again', off: 'Replace files in use off — a copy stops at a file a running program holds' },
  ] },
  { title: 'Branches', rows: [
    { key: 'stash', exp: 'stashOnBranchSwitch', label: 'Stash and switch branches', desc: 'When the working tree has uncommitted changes, stash them (including untracked files) and switch. Off: switching stops and asks you to commit or stash first.', def: false,
      on: 'Stash and switch branches on — uncommitted changes are stashed before a branch switch', off: 'Stash and switch branches off — a branch switch stops while there are uncommitted changes' },
  ] },
  { title: 'Console', rows: [
    { key: 'compact', exp: 'compactConsole', label: 'Compact console', desc: 'Errors in full, the first warnings of each project and a count of the rest; every other MSBuild line stays in the project log. Off: every line.', def: true,
      on: 'Compact console on — errors, the first warnings of each project and a warning count from the next run', off: 'Compact console off — every MSBuild line is shown from the next run' },
  ] },
  { title: 'Engine', rows: [
    { key: 'idleStop', exp: 'stopEngineWhenIdle', label: 'Stop the engine when idle', desc: 'After five minutes without work the engine process exits. It starts again in a moment when you build or sync.', def: true,
      on: 'Stop the engine when idle on — the engine exits after five idle minutes', off: 'Stop the engine when idle off — the engine is no longer stopped when idle' },
  ] },
  { title: 'Notifications', rows: [
    { key: 'notify', exp: 'showNotifications', label: 'Show notifications', desc: 'Tray notifications: a build\'s result, the first close to the tray, a Build shortcut that could not start, and a second launch that could not bring the window forward.', def: true,
      on: 'Show notifications on — tray notifications are shown', off: 'Show notifications off — no tray notifications are shown' },
  ] },
];
const GENERAL_ROWS = GENERAL_GROUPS.reduce((a, g) => a.concat(g.rows), []);
/* varsayılanın tek tanımı satırdadır; 'pull' kendi kalıcılık anahtarında yaşar */
const DEFAULT_GENERAL = GENERAL_ROWS.reduce((o, r) => { if (r.key !== 'pull') o[r.key] = r.def; return o; }, {});
/* Export sırası (uygulama SettingsFile): pull · replace · stash · compact · idle · startup · close · notify */
const EXPORT_ORDER = ['pull', 'replace', 'stash', 'compact', 'idleStop', 'startWin', 'startTray', 'closeTray', 'notify'];

/* ada uygulanan regex'lerle ilk-eşleşme gruplaması; eşleşmeyen → Diğer */
function compileGroups(cfg) {
  /* harici projeler kendi katmanı gibi en ÜSTTE toplanır (derleme sırasının başında);
     katman regex'lerine hiç girmezler. */
  /* uygulama LayerGrouping: harici projelerin grubu `External`; katman deseni yoksa ana projeler başlıksız tek grup */
  const extRows = BO.PROJECTS.filter((p) => p.ext);
  const repo = BO.PROJECTS.filter((p) => !p.ext);
  const head = extRows.length ? [{ name: 'External', rows: extRows }] : [];
  if (!cfg || !cfg.length) return head.concat([{ name: null, rows: repo }]).filter((g) => g.rows.length);
  const compiled = cfg.map((l) => { try { return l.rx && l.rx.trim() ? new RegExp(l.rx, 'i') : null; } catch (e) { return null; } });
  const groups = cfg.map((l) => ({ name: l.name, rows: [] }));
  const rest = { name: 'Other', rows: [] };
  repo.forEach((p) => {
    const i = compiled.findIndex((r) => r && r.test(p.name));
    (i >= 0 ? groups[i] : rest).rows.push(p);
  });
  if (rest.rows.length) groups.push(rest);
  return head.concat(groups).filter((g) => g.rows.length);
}

/* ---- ABOUT (F1) ---- */
/* gösterilen sürüm = UYGULAMA sürümü (AppIdentity.Version). Tasarım sürümü yalnız spec/VERSIONS'ta yaşar; UI'da görünmez. */
const ABOUT_VERSION = '1.15.0';
const DESIGN_VERSION = '2.0.1'; // eslint-disable-line no-unused-vars
/* uygulama ShortcutCatalog: GLOBAL · BUILD · APPLICATION, en sık kullanılandan en seyreğe */
const ABOUT_SHORTCUTS = [
  ['Global', [
    ['Show or hide the window', ['Shift+Space']],
    ['Build without bringing the window up', ['Ctrl+Shift+Space']],
  ]],
  ['Build', [
    ['Build — only stale projects', ['F5']],
    ['Rebuild — all projects, cache ignored', ['F6']],
    ['Clean — remove build outputs', ['F7']],
  ]],
  ['Application', [
    ['Focus the project filter', ['Ctrl+F']],
    ['About — version, shortcuts and diagnostics', ['F1']],
    ['Close the topmost layer: exit card → dialog → popover/menu → selection; otherwise stop the running build, or close the window like ×', ['Esc']],
    ['Exit — quit and end every process', ['Ctrl+Q']],
  ]],
];
const ABOUT_TABS = [
  { id: 'about', label: 'About' },
  { id: 'env', label: 'Environment' },
  { id: 'keys', label: 'Shortcuts' },
];
/* About sekmesi: kimlik bilgisi. Sürüm/engine burada, tanı bilgisi Environment'ta — tekrar yok. */
const ABOUT_ID = [
  ['Version', ABOUT_VERSION],
  ['Engine', ABOUT_VERSION],
  ['Copyright', '© 2026 Delta Yazılım'],
];
const ABOUT_ENV = [
  ['Engine PID', '41124'],
  ['.NET runtime', '.NET 10.0.10'],
  ['OS', 'Microsoft Windows 10.0.26200'],
];
/* Yollar kesilmez — hücre içinde yatay kaydırılır (bo-xscroll) */
const ABOUT_PATHS = [
  ['MSBuild', 'C:\\Program Files\\Microsoft Visual Studio\\18\\Enterprise\\MSBuild\\Current\\Bin\\MSBuild.exe'],
  ['Repository root', ''], // aktif kökten doldurulur
  ['State file', 'C:\\Users\\Delta\\AppData\\Local\\BuildOrchestrator\\ui-state.json'],
  ['Logs', 'C:\\Users\\Delta\\AppData\\Local\\BuildOrchestrator\\logs'],
];
/* yalnız Copy diagnostics'e giren bellek satırları (DiagnosticsSnapshot.CopyOnly) — hiçbir sekmede çizilmez */
const ABOUT_COPY_ONLY = [
  ['App private', '214 MB'],
  ['GC committed', '96 MB'],
  ['Managed heap', '61 MB'],
];

/* ---- What's new: sürüm notları (app/release-notes.js) ----
   Kategori satır başına değil BLOĞA yazılır (Keep a Changelog mantIğı): 6px renkli kare + caps başlık,
   altında işaretsiz maddeler. Satır başı ikonu/sigili yok. */
const NOTE_KINDS = {
  feature: { label: 'Added', color: 'var(--amber)' },
  change: { label: 'Changed', color: 'var(--text-dim)' },
  fix: { label: 'Fixed', color: 'var(--status-success)' },
  perf: { label: 'Performance', color: 'var(--status-cycle)' },
  removed: { label: 'Removed', color: 'var(--status-fail)' },
};
const KIND_ORDER = ['feature', 'change', 'fix', 'perf', 'removed'];
const NOTES_SEEN_KEY = 'delta-bo-seen-version-v1';
/* Prototipte nokta HER açılışta görünür — mekanizma görülebilsin diye. Gerçek uygulamada kural:
   localStorage'daki sürüm ≠ ABOUT_VERSION ise nokta durur, dialog açılınca o sürüm için düşer
   (bkz. release-notes 1.13.0 — davranış sürüm notlarında tanımlı). */
const notesUnseen = () => true;
const notesUnseenReal = () => {
  try { return localStorage.getItem(NOTES_SEEN_KEY) !== ABOUT_VERSION; } catch (e) { return false; }
};

/* ================= GÜNCELLEME (1.20.0) =================
   Title bar'da YALNIZ yeni sürüm arka planda inip kuruluma hazırken görünen hap + tıklayınca açılan kart.
   Gerçek uygulama: açılıştan kısa süre sonra ve periyodik sessiz kontrol, paket arka planda iner; hap ancak
   paket hazırsa çıkar (indirme sürerken UI'da iz yok). Prototipte besleme kaydı ÖRNEK: sürüm = kurulu
   sürümün bir sonraki minor'ı, boyut ve maddeler örnek metin. */
const nextMinor = (v) => { const p = String(v).split('.').map((n) => parseInt(n, 10) || 0); return p[0] + '.' + ((p[1] || 0) + 1) + '.0'; };
const UPDATE_FEED = {
  v: nextMinor(ABOUT_VERSION),
  size: '18.4 MB',
  /* ÖRNEK teklif: gelen sürümün CHANGELOG bölümü (kategori sırasıyla). Kart en çok 5 madde gösterir (UpdateOffer.MaxHighlights). */
  items: [
    ['feature', 'The branch picker remembers the last branches you switched to and lists them first.'],
    ['feature', 'Copy log on a project row can copy only the errors and warnings.'],
    ['change', 'The event stream groups the projects that finished in the same second into one line.'],
    ['fix', 'A project renamed on disk is picked up by the next Sync.'],
    ['fix', 'Copy log keeps its line breaks when pasted into another app.'],
    ['perf', 'Sync reads project files in parallel and finishes sooner on large solutions.'],
    ['perf', 'The first window appears sooner on a cold start.'],
  ],
};
const UPDATE_MAX_HIGHLIGHTS = 5;
/* restart ekranı (UpdateRestartTimeline): tek adım 'Closing Build Orchestrator…', 800 ms'de 0→100 %; sonra süreç çıkar */
const UPDATE_STEPS = [[800, 100]];
const UPDATE_TOTAL = UPDATE_STEPS.reduce((s, x) => s + x[0], 0);

/* hap: NÖTR — title bar'da chevron dışında amber yok (1.2.1). Vurguyu ağırlık taşır: ikonlu şeritte tek yazılı öğe. */
function UpdatePill({ version, open, onClick }) {
  const [hov, setHov] = React.useState(false);
  const lit = open || hov;
  return (
    <button type="button" onClick={onClick} onMouseEnter={() => setHov(true)} onMouseLeave={() => setHov(false)}
      aria-label={'Update to ' + version} aria-haspopup="dialog" aria-expanded={open}
      className={REDUCED ? undefined : 'bo-upd-in'}
      style={{
        display: 'inline-flex', alignItems: 'center', gap: 6, height: 22, padding: '0 8px 0 6px', flex: 'none', boxSizing: 'border-box',
        background: lit ? 'var(--surface-overlay)' : 'var(--surface-raised)',
        border: '1px solid ' + (lit ? 'var(--neutral-500)' : 'var(--border-strong)'), borderRadius: 'var(--radius-sm)',
        color: 'var(--text-primary)', fontFamily: 'var(--font-sans)', fontSize: 'var(--text-xs)', fontWeight: 500, lineHeight: 1,
        cursor: 'pointer', whiteSpace: 'nowrap',
        transition: 'background var(--duration-fast) var(--ease-standard), border-color var(--duration-fast) var(--ease-standard)',
      }}>
      <I.update />
      <span>Update</span>
      <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', fontWeight: 400, color: lit ? 'var(--text-secondary)' : 'var(--text-dim)', fontVariantNumeric: 'tabular-nums' }}>{version}</span>
    </button>
  );
}

/* kart: hapın altında, sol kenarı hapla hizalı. Üstte sürüm geçişi (kurulu → gelen), ortada öne çıkanlar
   (What's new blok dili), altta ne olacağı + Later / Restart to update. Koşu sürerken Restart kilitli, sebep tek satır. */
function UpdateCard({ from, feed, blocked, onRestart, onLater }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const caps = { fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase' };
  const line = '1px solid var(--border)';
  /* KindOrder sırasıyla ilk 5 madde; kalan tek soluk satır (tıklanmaz) */
  const ordered = KIND_ORDER.reduce((a, k) => a.concat(feed.items.filter((it) => it[0] === k)), []);
  const shown = ordered.slice(0, UPDATE_MAX_HIGHLIGHTS);
  const more = ordered.length - shown.length;
  return (
    <div role="dialog" aria-label={'Update to ' + feed.v} className={REDUCED ? undefined : 'bo-drop-in'}
      style={{
        position: 'absolute', top: 'calc(100% + 9px)', left: 0, zIndex: 90, width: 344, boxSizing: 'border-box',
        background: 'var(--surface-overlay)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--elevation-overlay)', fontFamily: 'var(--font-sans)', cursor: 'default', transformOrigin: '0 0',
      }}>
      <div style={{ padding: '13px 16px 15px' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{ ...caps, flex: 1, color: 'var(--text-faint)' }}>Update ready</span>
          <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', fontVariantNumeric: 'tabular-nums' }}>{feed.size}</span>
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 9, marginTop: 11 }}>
          <span style={{ fontFamily: MONO, fontSize: 'var(--text-sm)', color: 'var(--text-dim)' }}>{from}</span>
          <span style={{ display: 'inline-flex', color: 'var(--text-faint)' }}><I.arrowRight /></span>
          <span style={{ fontFamily: MONO, fontSize: 'var(--text-lg)', fontWeight: 500, color: 'var(--text-primary)' }}>{feed.v}</span>
        </div>
      </div>
      {shown.length > 0 && (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 12, padding: '13px 16px 14px', borderTop: line }}>
        {KIND_ORDER.map((k) => {
          const items = shown.filter((it) => it[0] === k);
          if (!items.length) return null;
          const K = NOTE_KINDS[k];
          return (
            <div key={k}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 7, marginBottom: 6 }}>
                <span aria-hidden="true" style={{ width: 6, height: 6, flex: 'none', borderRadius: 1, background: K.color }}></span>
                <span style={{ ...caps, color: 'var(--text-dim)' }}>{K.label}</span>
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: 5, paddingLeft: 13 }}>
                {items.map(([, text], i) => (
                  <span key={i} style={{ fontSize: 'var(--text-xs)', color: 'var(--text-secondary)', lineHeight: 1.5, textWrap: 'pretty' }}>{text}</span>
                ))}
              </div>
            </div>
          );
        })}
        {more > 0 && (
          <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-faint)', marginTop: -4 }}>+{more} more in What's new after restart</span>
        )}
      </div>
      )}
      <div style={{ padding: '12px 16px 14px', borderTop: line }}>
        <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 1.5, textWrap: 'pretty' }}>
          {blocked || 'Restart takes a few seconds and reopens the workspace. If you wait, it installs on the next start.'}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'flex-end', gap: 8, marginTop: 12 }}>
          <DS.Button size="sm" variant="secondary" onClick={onLater}>Later</DS.Button>
          <DS.Button size="sm" variant="primary" icon={<I.rebuild />} disabled={!!blocked} onClick={onRestart}>Restart to update</DS.Button>
        </div>
      </div>
    </div>
  );
}

/* Restart to update: pencere kapanıp yeni sürümle açılana kadarki kısa ekran. Prototipte uygulama kutusunu örter,
   bitince uygulama baştan açılır (Sync) ve hap düşer; gerçekte güncelleyicinin splash'i bu tasarımı taşır. */
function UpdateRestart({ run, from, to, logoSrc }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [, setF] = React.useState(0);
  const live = !!run && !run.out;
  React.useEffect(() => {
    if (!live) return undefined;
    let raf = 0;
    const loop = () => { setF((f) => f + 1); raf = requestAnimationFrame(loop); };
    raf = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(raf);
  }, [live]);
  if (!run) return null;
  const el = Math.min(UPDATE_TOTAL, Date.now() - run.t0);
  let acc = 0, prev = 0, pct = 100;
  for (let i = 0; i < UPDATE_STEPS.length; i++) {
    const [ms, end] = UPDATE_STEPS[i];
    if (el < acc + ms) { pct = prev + (end - prev) * ((el - acc) / ms); break; }
    acc += ms; prev = end;
  }
  const label = 'Closing Build Orchestrator'; // UpdateText.RestartStepLabel — tek adım
  return (
    <div role="status" aria-live="polite"
      style={{
        position: 'absolute', inset: 0, zIndex: 300, display: 'flex', alignItems: 'center', justifyContent: 'center',
        background: 'var(--surface-base)', fontFamily: 'var(--font-sans)',
      }}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: 232 }}>
        <img src={logoSrc} alt="" style={{ height: 30, display: 'block' }} />
        <div style={{ fontSize: 'var(--text-sm)', fontWeight: 600, color: 'var(--text-primary)', marginTop: 16 }}>Updating Build Orchestrator</div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 7, marginTop: 6, fontFamily: MONO, fontSize: 'var(--text-2xs)' }}>
          <span style={{ color: 'var(--text-dim)' }}>{from}</span>
          <span style={{ display: 'inline-flex', color: 'var(--text-faint)' }}><I.arrowRight size={11} /></span>
          <span style={{ color: 'var(--text-secondary)' }}>{to}</span>
        </div>
        <DS.ProgressBar value={pct} status="building" height={2} style={{ width: '100%', marginTop: 20 }} />
        <div style={{ fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', marginTop: 9 }}>{label}…</div>
      </div>
    </div>
  );
}

/* ---- What's new listesi (1.19.0): sürüm kimliği SOLDA sabit sütun, maddeler sağda ölçülü kolonda.
   Tarih artık scrollbar'ın dibinde değil, her sürümde aynı sütunda; blok scroll boyunca sticky kalır. ---- */
function WhatsNew() {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const all = window.DELTA_BO_NOTES || [];
  const [open, setOpen] = React.useState(false);
  const shown = open ? all : all.slice(0, 3);
  const rest = all.length - shown.length;
  const caps = { fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-dim)' };
  const grid = { display: 'grid', gridTemplateColumns: '84px minmax(0, 1fr)', columnGap: 26 };
  return (
    <>
      {shown.map((rel, ri) => (
        <div key={rel.v} style={{ ...grid, paddingTop: ri ? 22 : 0, marginTop: ri ? 22 : 0, borderTop: ri ? '1px solid var(--border-subtle)' : 'none' }}>
          <div style={{ position: 'sticky', top: 0, alignSelf: 'start', display: 'flex', flexDirection: 'column', gap: 6, paddingTop: 1 }}>
            <span style={{ fontFamily: MONO, fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)', lineHeight: 1 }}>{rel.v}</span>
            <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', lineHeight: 1 }}>{rel.date}</span>
            {rel.v === ABOUT_VERSION && (
              <span style={{
                display: 'inline-flex', alignItems: 'center', alignSelf: 'flex-start', height: 16, padding: '0 5px', marginTop: 2,
                border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-xs)', background: 'var(--surface)',
                fontSize: 9.5, fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-dim)',
              }}>Installed</span>
            )}
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 15, minWidth: 0 }}>
            {KIND_ORDER.map((k) => {
              const items = rel.items.filter((it) => it[0] === k);
              if (!items.length) return null;
              const K = NOTE_KINDS[k];
              return (
                <div key={k}>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 7, marginBottom: 7 }}>
                    <span aria-hidden="true" style={{ width: 6, height: 6, flex: 'none', borderRadius: 1, background: K.color }}></span>
                    <span style={caps}>{K.label}</span>
                  </div>
                  <div style={{ display: 'flex', flexDirection: 'column', gap: 10, paddingLeft: 13 }}>
                    {items.map(([, text], i) => (
                      <span key={i} style={{ fontSize: 'var(--text-sm)', color: 'var(--text-secondary)', lineHeight: 1.62, textWrap: 'pretty', maxWidth: 500 }}>{text}</span>
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      ))}
      {rest > 0 && (
        <div style={{ ...grid, marginTop: 22, paddingTop: 14, borderTop: '1px solid var(--border-subtle)' }}>
          <span></span>
          <div style={{ marginLeft: -10 }}>
            <DS.Button size="sm" variant="ghost" icon={<I.down />} onClick={() => setOpen(true)}>Earlier versions ({rest})</DS.Button>
          </div>
        </div>
      )}
    </>
  );
}

/* ---- Üç dialogun ORTAK KABUĞU (1.19.0): aynı 18px gutter, aynı başlık/gövde/footer ritmi.
   Gövde flex:1 — dialog yüksekliği sabit, içerik kendi içinde kayar (tab/bölüm değişince zıplamaz). ---- */
function DialogShell({ label, width, height, zIndex = 100, onClose, head, tabs, footer, bodyStyle, children }) {
  return (
    <div className="ds-scrim-in" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}
      style={{ position: 'absolute', inset: 0, zIndex, background: 'var(--scrim)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      <div role="dialog" aria-modal="true" aria-label={label} className="ds-dialog-in"
        style={{
          width, height, maxWidth: 'calc(100% - 48px)', maxHeight: 'calc(100% - 48px)', display: 'flex', flexDirection: 'column',
          background: 'var(--surface-raised)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--elevation-overlay)', fontFamily: 'var(--font-sans)', overflow: 'hidden',
        }}>
        {head}
        {tabs}
        {children}
        {footer && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, flex: 'none', padding: '12px 18px', borderTop: '1px solid var(--border-subtle)' }}>{footer}</div>
        )}
      </div>
    </div>
  );
}

/* kenar solması (uygulama EdgeFade.Insets, 1.14.2/1.14.3): içerik payı kadar şerit, kenarda zemin rengi → şeffaf.
   Şeritler kaydırıcının içerik hücresinde durur, scrollbar'ı örtmez; durgunken yalnız boş payı örttükleri için görünmezler. */
function FadeScroll({ top, bottom, padding, alwaysBar, outerStyle, scrollRef, children }) {
  const ref = React.useRef(null);
  const innerRef = React.useRef(null);
  const [sb, setSb] = React.useState(0);
  React.useLayoutEffect(() => {
    const el = ref.current; if (!el) return undefined;
    if (scrollRef) scrollRef.current = el;
    const m = () => setSb(el.offsetWidth - el.clientWidth);
    m();
    if (typeof ResizeObserver === 'undefined') return undefined;
    const ro = new ResizeObserver(m);
    ro.observe(el); if (innerRef.current) ro.observe(innerRef.current);
    return () => ro.disconnect();
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const strip = (edge, h) => ({
    position: 'absolute', left: 0, right: sb, [edge]: 0, height: h, pointerEvents: 'none', zIndex: 2,
    background: 'linear-gradient(to ' + (edge === 'top' ? 'bottom' : 'top') + ', var(--surface-raised), transparent)',
  });
  return (
    <div style={{ position: 'relative', display: 'flex', flexDirection: 'column', minHeight: 0, ...outerStyle }}>
      <div ref={ref} className="bo-scroll" style={{ flex: 1, minHeight: 0, overflowY: alwaysBar ? 'scroll' : 'auto', padding }}>
        <div ref={innerRef}>{children}</div>
      </div>
      <span aria-hidden="true" style={strip('top', top)}></span>
      <span aria-hidden="true" style={strip('bottom', bottom)}></span>
    </div>
  );
}

/* What's new kendi dialogu (v1.13.0); düzeni 1.19.0'da ferahlatıldı */
function NotesDialog({ open, onSeen, onClose }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  React.useEffect(() => { if (open && onSeen) onSeen(); }, [open]); // eslint-disable-line
  if (!open) return null;
  return (
    <DialogShell label="What's new" width={720} height={600} zIndex={101} onClose={onClose}
      head={(
        <div style={{ display: 'flex', alignItems: 'center', gap: 12, flex: 'none', padding: '20px 18px 16px', borderBottom: '1px solid var(--border-subtle)' }}>
          <div style={{ minWidth: 0, flex: 1 }}>
            <div style={{ fontSize: 'var(--text-md)', fontWeight: 600, color: 'var(--text-primary)' }}>What's new</div>
            <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', marginTop: 3 }}>Release notes for Build Orchestrator.</div>
          </div>
          <span style={{
            flex: 'none', display: 'inline-flex', alignItems: 'center', height: 20, padding: '0 7px',
            border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-xs)', background: 'var(--surface)',
            fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--text-secondary)',
          }}>{ABOUT_VERSION}</span>
        </div>
      )}
      footer={<><span style={{ flex: 1 }}></span><DS.Button variant="secondary" onClick={onClose}>Close</DS.Button></>}>
      <FadeScroll top={22} bottom={24} padding="22px 18px 24px" outerStyle={{ flex: 1 }}><WhatsNew /></FadeScroll>
    </DialogShell>
  );
}

/* About (F1) — 1.19.0: Third-party sekmesi kaldırıldı, sıra About → Environment → Shortcuts,
   Segment yerine alt çizgili DS.Tabs (etiket gövde metniyle aynı 18px hizasına oturur). */
function AboutDialog({ open, root, onClose, onOpenNotes, logoSrc, companyLogoSrc }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [tab, setTab] = React.useState('about');
  const [copied, setCopied] = React.useState(false);
  const tRef = React.useRef(null);
  React.useEffect(() => { if (open) { setTab('about'); setCopied(false); } }, [open]); // eslint-disable-line
  React.useEffect(() => () => { if (tRef.current) clearTimeout(tRef.current); }, []);
  if (!open) return null;

  /* Repository root aktif ayardan; boşsa 'no repository' (DiagnosticsReport.NoRepository) */
  const paths = ABOUT_PATHS.map(([k, v]) => (k === 'Repository root' ? [k, root || 'no repository'] : [k, v]));
  /* uygulama DiagnosticsReport.ToText: başlık satırı, sonra Engine · Runtime · bellek · Paths — değerler tek kolonda */
  const diagnostics = () => {
    const lines = [['Engine', ABOUT_VERSION]].concat(ABOUT_ENV, ABOUT_COPY_ONLY, paths);
    const w = Math.max(...lines.map((l) => l[0].length));
    return ['Build Orchestrator ' + ABOUT_VERSION].concat(lines.map(([k, v]) => k.padEnd(w) + '  ' + v)).join('\n') + '\n';
  };
  const copyDiag = async () => {
    const text = diagnostics();
    let good = true;
    try { await navigator.clipboard.writeText(text); }
    catch (e) {
      try {
        const ta = document.createElement('textarea');
        ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
        document.body.appendChild(ta); ta.select();
        good = document.execCommand('copy'); ta.remove();
      } catch (e2) { good = false; }
    }
    if (good) { setCopied(true); if (tRef.current) clearTimeout(tRef.current); tRef.current = setTimeout(() => setCopied(false), 1400); }
  };

  const caps = { fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-dim)' };
  const row = { display: 'flex', alignItems: 'center', gap: 18, minHeight: 27 };
  const lab = { width: 124, flex: 'none', fontSize: 'var(--text-xs)', color: 'var(--text-dim)' };
  const mono = { fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--text-secondary)' };
  const group = (title, rows) => (
    <div key={title} style={{ marginBottom: 16 }}>
      <div style={{ ...caps, marginBottom: 7 }}>{title}</div>
      {rows}
    </div>
  );

  return (
    <DialogShell label="About Build Orchestrator" width={620} onClose={onClose}
      head={(
        <div style={{ display: 'flex', alignItems: 'flex-start', gap: 13, flex: 'none', padding: '20px 18px 20px' }}>
          <img src={logoSrc} alt="" style={{ height: 28, marginTop: 1, flex: 'none' }} />
          <div style={{ minWidth: 0, flex: 1 }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: 9 }}>
              <span style={{ fontSize: 'var(--text-lg)', fontWeight: 600, color: 'var(--text-primary)' }}>Build Orchestrator</span>
              <span style={{
                display: 'inline-flex', alignItems: 'center', height: 19, padding: '0 6px',
                border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-xs)', background: 'var(--surface)',
                fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-secondary)',
              }}>{ABOUT_VERSION}</span>
            </div>
            <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', marginTop: 4 }}>Ordered, incremental builds for a multi-project .NET solution.</div>
          </div>
          {companyLogoSrc && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 13, flex: 'none', paddingTop: 1 }}>
              <span aria-hidden="true" style={{ width: 1, height: 28, background: 'var(--border-subtle)' }}></span>
              <span style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 6 }}>
                <span style={{ fontSize: 'var(--text-2xs)', letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)' }}>Licensed to</span>
                <img src={companyLogoSrc} alt="Delta" style={{ height: 13, display: 'block', opacity: 0.8 }} />
              </span>
            </div>
          )}
        </div>
      )}
      tabs={(
        <div style={{ flex: 'none', padding: '6px 18px 14px', borderBottom: '1px solid var(--border-subtle)' }}>
          <DS.Segment options={ABOUT_TABS.map((t) => ({ value: t.id, label: t.label }))} value={tab} onChange={setTab} />
        </div>
      )}
      footer={(
        <>
          <span style={{ display: 'inline-flex', marginLeft: -10, color: copied ? 'var(--status-success-text)' : undefined }}>
            <DS.Button size="sm" variant="ghost" icon={copied ? <I.check /> : <I.copy />} onClick={copyDiag}>{copied ? 'Copied' : 'Copy diagnostics'}</DS.Button>
          </span>
          <span style={{ flex: 1 }}></span>
          <DS.Button variant="secondary" onClick={onClose}>Close</DS.Button>
        </>
      )}>
      <FadeScroll key={tab} top={14} bottom={20} padding="14px 18px 20px" outerStyle={{ height: 284, flex: 'none' }}>
        {tab === 'about' && (
          <>
            <div style={{ fontSize: 'var(--text-sm)', color: 'var(--text-secondary)', lineHeight: 1.62, textWrap: 'pretty', maxWidth: 470 }}>
              Build Orchestrator discovers the projects under the repository root, works out the dependency graph and builds in that order — only what changed, in parallel where the graph allows. The plan, the running build and its result stay visible while it works.
            </div>
            <div style={{ height: 1, background: 'var(--border-subtle)', margin: '18px 0 14px' }}></div>
            {ABOUT_ID.map(([k, v]) => (
              <div key={k} style={row}>
                <span style={lab}>{k}</span>
                <span style={mono}>{v}</span>
              </div>
            ))}
            <div style={{ marginTop: 14, marginLeft: -10 }}>
              <DS.Button size="sm" variant="ghost" icon={<I.sparkle />} onClick={onOpenNotes}>What's new in {ABOUT_VERSION}</DS.Button>
            </div>
          </>
        )}
        {tab === 'env' && (
          <>
            {group('Runtime', ABOUT_ENV.map(([k, v]) => (
              <div key={k} style={row}><span style={lab}>{k}</span><span style={mono}>{v}</span></div>
            )))}
            {group('Paths', paths.map(([k, v]) => (
              <div key={k} style={row}>
                <span style={lab}>{k}</span>
                <div className="bo-xscroll" style={{ flex: 1, minWidth: 0 }}
                  onWheel={(e) => { const el = e.currentTarget; const d = e.deltaY || e.deltaX; if (d && el.scrollWidth > el.clientWidth) el.scrollLeft += d; }}>
                  <span style={{ ...mono, whiteSpace: 'nowrap' }}>{v}</span>
                </div>
              </div>
            )))}
          </>
        )}
        {tab === 'keys' && ABOUT_SHORTCUTS.map(([title, rows]) => group(title, rows.map(([label, keys]) => (
          <div key={label} style={row}>
            <span style={{ flex: 1, fontSize: 'var(--text-sm)', color: 'var(--text-secondary)' }}>{label}</span>
            <span style={{ display: 'flex', gap: 4, flex: 'none' }}>{keys.map((k) => <DS.Kbd key={k}>{k}</DS.Kbd>)}</span>
          </div>
        ))))}
      </FadeScroll>
    </DialogShell>
  );
}

/* ================= AYARLAR (1.19.0: sol raylı iki panel) ================= */
function RailItem({ label, count, active, onClick }) {
  const [hov, setHov] = React.useState(false);
  return (
    <button type="button" onClick={onClick} onMouseEnter={() => setHov(true)} onMouseLeave={() => setHov(false)}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, height: 30, padding: '0 8px', flex: 'none',
        background: active ? 'var(--surface-overlay)' : (hov ? 'var(--surface-raised)' : 'transparent'),
        border: 'none', borderRadius: 'var(--radius-sm)', textAlign: 'left', cursor: 'pointer',
        fontFamily: 'var(--font-sans)', fontSize: 'var(--text-sm)', fontWeight: 500,
        color: active ? 'var(--text-primary)' : 'var(--text-dim)',
        transition: 'background var(--duration-fast) var(--ease-standard), color var(--duration-fast) var(--ease-standard)',
      }}>
      <span style={{ flex: 1, minWidth: 0, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{label}</span>
      {count > 0 && (
        <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', fontVariantNumeric: 'tabular-nums', color: active ? 'var(--text-secondary)' : 'var(--text-faint)' }}>{count}</span>
      )}
    </button>
  );
}

/* bölüm başı: tek başlık + TEK satırlık açıklama (uzun paragraflar 1.19.0'da kaldırıldı) */
function PaneHead({ title, children }) {
  return (
    <div style={{ marginBottom: 18 }}>
      <div style={{ fontSize: 'var(--text-md)', fontWeight: 600, color: 'var(--text-primary)' }}>{title}</div>
      {children && (
        <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 'var(--leading-snug)', marginTop: 5, maxWidth: 520, textWrap: 'pretty' }}>{children}</div>
      )}
    </div>
  );
}

/* standart ayar satırı: etiket + tek satır açıklama solda, kontrol sağda, aralarında hairline.
   Yeni bir ayar eklemek = GENERAL_GROUPS'a bir satır daha — düzen işi yok. */
function ToggleRow({ label, desc, checked, onChange, disabled, first }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  return (
    <div style={{
      display: 'flex', alignItems: 'flex-start', gap: 20, padding: '13px 0',
      borderTop: first ? 'none' : '1px solid var(--border-subtle)', opacity: disabled ? 0.45 : 1,
    }}>
      <span style={{ flex: 1, minWidth: 0 }}>
        <span style={{ display: 'block', fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)' }}>{label}</span>
        <span style={{ display: 'block', fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 'var(--leading-snug)', marginTop: 3, maxWidth: 430 }}>{desc}</span>
      </span>
      <span style={{ flex: 'none', paddingTop: 1, pointerEvents: disabled ? 'none' : undefined }}>
        <DS.Switch checked={checked} onChange={onChange} />
      </span>
    </div>
  );
}

/* worktree klasörü denetimi (Core WorktreeFolder.Inspect + WorkingCopyText.FolderProblem). Disk prototipte örnek:
   linked = bu reponun worktree'leri, DEMO_FOREIGN = dolu ve başka şey olan klasörler, DEMO_NOT_GIT = git olmayan kökler. */
const DEMO_FOREIGN = ['D:\\src\\shared', 'D:\\src\\tools'];
const DEMO_NOT_GIT = ['D:\\src\\notes'];
const normDir = (x) => x.trim().replace(/[\\/]+$/, '').toLowerCase();
const isFullPath = (x) => /^[A-Za-z]:[\\/]/.test(x) || /^\\\\[^\\]+\\[^\\]+/.test(x);
function worktreeProblem(root, folder, linked) {
  const r = root.trim(), f = folder.trim();
  if (!r || !f) return null; // boş alan ya da kök yokken denetlenmez
  if (!isFullPath(f) || !isFullPath(r)) return 'The worktree folder needs a full path';
  const a = normDir(r), b = normDir(f);
  if (a === b || b.startsWith(a + '\\') || a.startsWith(b + '\\')) return 'The worktree folder must sit next to the repository, not inside it or around it';
  if (DEMO_NOT_GIT.some((x) => normDir(x) === a)) return 'The repository root is not a git repository';
  if ((linked || []).some((x) => normDir(x) === b)) return null;
  if (DEMO_FOREIGN.some((x) => normDir(x) === b)) return 'The worktree folder is not empty and is not a worktree of this repository';
  return null;
}

function SettingsDialog({ open, cfg, extCfg, pull, gen, root, wt, linked, setup, importOnOpen, focusWorktree, onClose, onSave }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [sec, setSec] = React.useState('general');
  const [draft, setDraft] = React.useState(cfg);
  const [rootDraft, setRootDraft] = React.useState(root || '');
  const [msg, setMsg] = React.useState(null); // {ok, text} — export/import geri bildirimi
  const [clearArmed, setClearArmed] = React.useState(false); // sil: iki aşamalı onay (dialog yok)
  const fileRef = React.useRef(null);
  const msgT = React.useRef(null);
  const dragRef = React.useRef(null);
  const [dragIdx, setDragIdx] = React.useState(null);
  const [dragOff, setDragOff] = React.useState(0);
  /* harici projeler: katmanlardan ÖNCE derlenir, kart sırası çalışma kopyalarının güncellenme sırasıdır */
  const [ext, setExt] = React.useState(extCfg || []);
  /* build öncesi pull — 1.19.0'da General bölümünde, diğer anahtarlarla aynı listede */
  const [pullDraft, setPullDraft] = React.useState(pull !== false);
  const [genDraft, setGenDraft] = React.useState(gen || DEFAULT_GENERAL);
  const [wtDraft, setWtDraft] = React.useState(wt || '');
  const scrollRef = React.useRef(null);
  const wtInputRef = React.useRef(null);
  const extRef = React.useRef(null);
  const [extIdx, setExtIdx] = React.useState(null);
  const [extOff, setExtOff] = React.useState(0);
  React.useEffect(() => { if (open) { setSec(setup ? 'workspace' : 'general'); setDraft(cfg.map((l) => ({ ...l }))); setExt((extCfg || []).map((x) => ({ ...x }))); setPullDraft(pull !== false); setGenDraft({ ...DEFAULT_GENERAL, ...(gen || {}) }); setRootDraft(root || ''); setWtDraft(wt || ''); setMsg(null); setClearArmed(false); dragRef.current = null; setDragIdx(null); setDragOff(0); extRef.current = null; setExtIdx(null); setExtOff(0); if (importOnOpen) setTimeout(() => { if (fileRef.current) fileRef.current.click(); }, 120);
    if (focusWorktree) { setSec('workspace'); setTimeout(() => { const el = wtInputRef.current && wtInputRef.current.querySelector('input'); if (el) el.focus(); }, 60); } } }, [open]); // eslint-disable-line
  /* bütün sayfalar tek kaydırıcıyı paylaşır: bölüm değişince yeni sayfa en üstten başlar */
  React.useEffect(() => { if (scrollRef.current) scrollRef.current.scrollTop = 0; }, [sec]);
  React.useEffect(() => () => { if (msgT.current) clearTimeout(msgT.current); }, []);
  if (!open) return null;
  const compiled = draft.map((l) => { try { return l.rx && l.rx.trim() ? new RegExp(l.rx, 'i') : null; } catch (e) { return undefined; } });
  const upd = (i, patch) => setDraft((d) => d.map((l, k) => (k === i ? { ...l, ...patch } : l)));
  const swap = (arr, a, b) => { const nd = arr.slice(); const t = nd[a]; nd[a] = nd[b]; nd[b] = t; return nd; };
  /* pull kendi kalıcılık anahtarında yaşar; General'ın diğer anahtarları tek nesnede */
  const getG = (k) => (k === 'pull' ? pullDraft : !!genDraft[k]);
  const setG = (k, v) => { if (k === 'pull') setPullDraft(v); else setGenDraft((g) => ({ ...g, [k]: v })); };

  /* kart sürükle-bırak sıralama — iki liste (katmanlar · harici projeler) aynı mekanizmayı paylaşır */
  const ROWH = 42; // 36 kart + 6 boşluk
  const mkDrag = (ref, setIdx, setOff, setList, getLen) => ({
    start: (e, i) => {
      e.preventDefault();
      e.currentTarget.setPointerCapture(e.pointerId);
      ref.current = { idx: i, startY: e.clientY };
      setIdx(i); setOff(0);
    },
    move: (e) => {
      const d = ref.current; if (!d) return;
      let off = e.clientY - d.startY;
      while (off > ROWH / 2 && d.idx < getLen() - 1) { const i = d.idx; setList((arr) => swap(arr, i, i + 1)); d.idx++; d.startY += ROWH; off -= ROWH; }
      while (off < -ROWH / 2 && d.idx > 0) { const i = d.idx; setList((arr) => swap(arr, i, i - 1)); d.idx--; d.startY -= ROWH; off += ROWH; }
      setIdx(d.idx); setOff(off);
    },
    end: () => { ref.current = null; setIdx(null); setOff(0); },
  });
  const layerDrag = mkDrag(dragRef, setDragIdx, setDragOff, setDraft, () => draft.length);
  const extDrag = mkDrag(extRef, setExtIdx, setExtOff, setExt, () => ext.length);

  const layersOk = draft.every((l, i) => l.name.trim() && compiled[i] !== undefined);
  const extOk = ext.every((x) => x.path.trim());
  /* Save neden kapalı (SaveBlockedReason): worktree → harici → katman adı → desen. Boş kök kaydedilebilir (workspace yok). */
  const wtProblem = worktreeProblem(rootDraft, wtDraft, linked);
  const hint = wtProblem || (!extOk ? 'Every external project needs a path'
    : (draft.some((l) => !l.name.trim()) ? 'Every layer needs a name'
      : (!layersOk ? 'Check the highlighted pattern' : null)));
  const valid = !hint;
  const flash = (ok, text) => { setMsg({ ok, text }); if (msgT.current) clearTimeout(msgT.current); msgT.current = setTimeout(() => setMsg(null), 2400); };
  /* dışa aktar: root + harici + katmanlar + genel ayarlar tek JSON — makineler arası taşınabilir */
  const doExport = () => {
    const data = {
      app: 'Build Orchestrator', version: ABOUT_VERSION,
      repositoryRoot: rootDraft.trim(),
      externalProjects: ext.filter((x) => x.path.trim()).map((x) => ({ path: x.path.trim() })),
    };
    /* düz anahtarlar, SettingsFile sırasıyla; worktree klasörü dışa aktarılmaz */
    EXPORT_ORDER.forEach((k) => { const r = GENERAL_ROWS.find((x) => x.key === k); data[r.exp] = getG(k); });
    data.layers = draft.map((l) => ({ name: l.name.trim(), pattern: l.rx }));
    try {
      const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }));
      const a = document.createElement('a');
      a.href = url; a.download = 'build-orchestrator-settings.json';
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 2000);
      flash(true, 'Exported — settings JSON');
    } catch (e) { flash(false, 'Could not write the settings file'); }
  };
  /* içe aktar: dosyadan gelen değerler forma yüklenir — Save'e kadar hiçbir şey uygulanmaz */
  const onFile = (e) => {
    const f = e.target.files && e.target.files[0];
    e.target.value = '';
    if (!f) return;
    const rd = new FileReader();
    rd.onload = () => {
      try {
        const d = JSON.parse(String(rd.result));
        const layers = Array.isArray(d.layers)
          ? d.layers.map((l) => ({ name: String(l && l.name || ''), rx: String(l && (l.pattern != null ? l.pattern : l.rx) || '') }))
          : null;
        const exts = Array.isArray(d.externalProjects)
          ? d.externalProjects.map((x) => ({ path: String(typeof x === 'string' ? x : (x && x.path) || '') })).filter((x) => x.path)
          : null;
        const hasRoot = typeof d.repositoryRoot === 'string';
        const flags = GENERAL_ROWS.filter((r) => typeof d[r.exp] === 'boolean');
        if (!layers && !hasRoot && !exts && !flags.length) throw new Error('shape');
        if (hasRoot) setRootDraft(d.repositoryRoot);
        if (layers) setDraft(layers);
        if (exts) setExt(exts);
        /* düz anahtarlar; eksik anahtar anahtarı olduğu gibi bırakır, bilinmeyen (vcs, eski general) yok sayılır */
        flags.forEach((r) => setG(r.key, d[r.exp]));
        flash(true, 'Imported — ' + (layers ? layers.length : 0) + ' layers' + (exts ? ' · ' + exts.length + ' external' : '') + (hasRoot ? ' · root set' : ''));
      } catch (err) { flash(false, 'Invalid settings file'); }
    };
    rd.onerror = () => flash(false, 'Invalid settings file'); // okuma hatası da geçersiz dosya (SettingsDialog.OnImport)
    rd.readAsText(f);
  };
  /* sil: formu sıfırlar — ilk tık silahlandırır, ikincisi boşaltır (Save'e kadar uygulanmaz) */
  const doClear = () => {
    if (!clearArmed) {
      setClearArmed(true);
      flash(false, 'Click again to clear');
      setTimeout(() => setClearArmed(false), 2400);
      return;
    }
    setClearArmed(false);
    setRootDraft('');
    setDraft([]);
    setExt([]);
    setWtDraft(''); // kökü olmayan worktree yoktur
    setPullDraft(GENERAL_ROWS.find((r) => r.key === 'pull').def); // katalog varsayılanına döner
    setGenDraft({ ...DEFAULT_GENERAL });
    flash(true, 'Cleared — save to apply');
  };
  const caps = { fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-dim)' };
  const colCap = { ...caps, color: 'var(--text-faint)' };
  const empty = { border: '1px dashed var(--border)', borderRadius: 'var(--radius-md)', padding: '14px 16px', fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 'var(--leading-snug)' };
  const card = (activeDrag, off) => ({
    display: 'flex', alignItems: 'center', gap: 6, height: 36, boxSizing: 'border-box', padding: '0 6px 0 2px', marginBottom: 6,
    background: activeDrag ? 'var(--surface-raised)' : 'var(--surface)',
    border: '1px solid ' + (activeDrag ? 'var(--border-strong)' : 'var(--border)'),
    borderRadius: 'var(--radius-md)', position: 'relative', zIndex: activeDrag ? 5 : 1,
    transform: activeDrag ? 'translateY(' + off + 'px)' : 'none',
  });
  const gripStyle = (grabbing) => ({ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 20, alignSelf: 'stretch', flex: 'none', color: 'var(--text-faint)', cursor: grabbing ? 'grabbing' : 'grab', touchAction: 'none' });

  return (
    <DialogShell label="Settings" width={880} height={576} onClose={onClose}
      head={(
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, flex: 'none', padding: '12px 12px 12px 18px', borderBottom: '1px solid var(--border-subtle)' }}>
          <span style={{ flex: 1, fontSize: 'var(--text-md)', fontWeight: 600, color: 'var(--text-primary)' }}>Settings</span>
          <DS.IconButton size="sm" aria-label="Close settings" title="Close" onClick={onClose}><I.close /></DS.IconButton>
        </div>
      )}
      footer={(
        <>
          <DS.Tooltip content="Export settings — the whole form as a JSON file" side="top">
            <DS.IconButton size="sm" aria-label="Export settings" onClick={doExport}><I.download /></DS.IconButton>
          </DS.Tooltip>
          <DS.Tooltip content="Import settings — fill this form from a JSON file" side="top">
            <DS.IconButton size="sm" aria-label="Import settings" onClick={() => fileRef.current && fileRef.current.click()}><I.upload /></DS.IconButton>
          </DS.Tooltip>
          <DS.Tooltip content={clearArmed ? 'Click again to clear' : 'Clear settings — empty the form'} side="top">
            <DS.IconButton size="sm" aria-label="Clear settings" onClick={doClear}>
              <span style={{ display: 'inline-flex', color: clearArmed ? 'var(--status-fail-text)' : 'inherit' }}><I.trash /></span>
            </DS.IconButton>
          </DS.Tooltip>
          <input ref={fileRef} type="file" accept="application/json,.json" onChange={onFile} style={{ display: 'none' }} />
          {msg && (
            <span style={{ fontSize: 'var(--text-xs)', color: msg.ok ? 'var(--status-success-text)' : 'var(--status-fail-text)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{msg.text}</span>
          )}
          {!msg && hint && (
            <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-faint)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{hint}</span>
          )}
          <span style={{ flex: 1 }}></span>
          <DS.Button variant="secondary" onClick={onClose}>Cancel</DS.Button>
          <DS.Button variant="primary" disabled={!valid}
            onClick={() => onSave(rootDraft.trim(), draft.map((l) => ({ name: l.name.trim(), rx: l.rx })), ext.map((x) => ({ path: x.path.trim() })).filter((x) => x.path), pullDraft, { ...genDraft }, rootDraft.trim() ? wtDraft.trim() : '')}>{setup && rootDraft.trim() ? 'Save and sync' : 'Save'}</DS.Button>
        </>
      )}>
      <div style={{ flex: 1, minHeight: 0, display: 'flex' }}>
        {/* bölüm rayı — ayarlar çoğaldıkça buraya satır eklenir, sayfalar sıkışmaz */}
        <div style={{ width: 196, flex: 'none', boxSizing: 'border-box', borderRight: '1px solid var(--border-subtle)', background: 'var(--surface)', padding: '10px 8px', display: 'flex', flexDirection: 'column', gap: 2 }}>
          {SET_SECTIONS.map((s) => (
            <RailItem key={s.id} label={s.label} active={sec === s.id} onClick={() => setSec(s.id)}
              count={s.id === 'layers' ? draft.length : (s.id === 'external' ? ext.length : 0)} />
          ))}
        </div>
        {/* scrollbar sütunu hep ayrılmış (VerticalScrollBarVisibility=Visible): liste taşınca girdiler kaymaz */}
        <FadeScroll top={20} bottom={24} padding="20px 20px 24px" alwaysBar scrollRef={scrollRef} outerStyle={{ flex: 1, minWidth: 0 }}>
          {sec === 'general' && (
            <>
              <PaneHead title="General">How the app starts and behaves. Every setting applies when you save.</PaneHead>
              {GENERAL_GROUPS.map((g, gi) => (
                <div key={g.title} style={{ marginTop: gi ? 22 : 0 }}>
                  <div style={caps}>{g.title}</div>
                  <div style={{ marginTop: 5 }}>
                    {g.rows.map((r, i) => (
                      <ToggleRow key={r.key} first={!i} label={r.label} desc={r.desc}
                        checked={getG(r.key)} onChange={(v) => setG(r.key, v)}
                        disabled={!!r.dep && !getG(r.dep)} />
                    ))}
                  </div>
                </div>
              ))}
            </>
          )}

          {sec === 'workspace' && (
            <>
              <PaneHead title="Workspace">The folder that holds the solutions. Projects and the dependency graph are discovered from it on Sync.</PaneHead>
              <div style={{ ...colCap, marginBottom: 7 }}>Repository root</div>
              <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <DS.Input mono value={rootDraft} placeholder="D:\src\myapp" spellCheck={false}
                  onChange={(e) => setRootDraft(e.target.value)} style={{ width: 'auto', flex: 1 }} />
                <DS.Button variant="secondary" icon={<I.folder />} onClick={() => setRootDraft('D:\\src\\ACME')} style={{ flex: 'none' }}>Browse…</DS.Button>
              </div>
              <div style={{ fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', marginTop: 9 }}>Nothing is discovered without it.</div>
              {/* worktree klasörü (uygulama SettingsDialog WORKTREE FOLDER): reponun yanında tek linked worktree */}
              <div style={{ ...colCap, marginTop: 22, marginBottom: 7 }}>Worktree folder</div>
              <div ref={wtInputRef} style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <DS.Input mono value={wtDraft} placeholder="D:\src\myapp-worktree" spellCheck={false} invalid={!!wtProblem}
                  onChange={(e) => setWtDraft(e.target.value)} style={{ width: 'auto', flex: 1 }} />
                <DS.Button variant="secondary" icon={<I.folder />} style={{ flex: 'none' }}
                  onClick={() => { const r = rootDraft.trim(); if (r) setWtDraft(r.replace(/[\\/]+$/, '') + '-worktree'); }}>Browse…</DS.Button>
              </div>
              <div style={{ fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', marginTop: 9, lineHeight: 'var(--leading-snug)', textWrap: 'pretty' }}>
                Optional — a second working copy of the repository, next to it, for another branch. Save creates it when the folder does not exist; switch between the two in the bottom bar.
              </div>
            </>
          )}

          {sec === 'external' && (
            <>
              <PaneHead title="External projects">
                Projects outside the repository root — a folder, a solution or a project file; the git working copy is found from the path. They are built before everything else.
              </PaneHead>
              {!ext.length && <div style={empty}>No external projects — only what is discovered under the repository root is built.</div>}
              {ext.map((x, i) => (
                <div key={i} style={card(extIdx === i, extOff)}>
                  <span
                    onPointerDown={(e) => extDrag.start(e, i)} onPointerMove={extDrag.move} onPointerUp={extDrag.end} onPointerCancel={extDrag.end}
                    title="Drag to reorder" aria-label="Drag to reorder" style={gripStyle(extIdx === i)}>
                    <I.grip />
                  </span>
                  <DS.Input mono value={x.path} placeholder="C:\src\shared\MyApp.Common\MyApp.Common.csproj" spellCheck={false}
                    onChange={(e) => setExt((arr) => arr.map((v, k) => (k === i ? { ...v, path: e.target.value } : v)))} style={{ width: 'auto', flex: 1 }} />
                  <DS.IconButton size="sm" title="Remove external project" onClick={() => setExt((arr) => arr.filter((_, k) => k !== i))}><I.trash /></DS.IconButton>
                </div>
              ))}
              <div style={{ marginTop: 10, marginLeft: -10 }}>
                <DS.Button size="sm" variant="ghost" icon={<I.plus />} onClick={() => setExt((arr) => [...arr, { path: '' }])}>Add external project</DS.Button>
              </div>
              {/* pull anahtarı artık General'da — nereye taşındığı burada yazılı */}
              <div style={{ marginTop: 20, paddingTop: 14, borderTop: '1px solid var(--border-subtle)', display: 'flex', alignItems: 'center', gap: 6, flexWrap: 'wrap' }}>
                <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)' }}>
                  Card order sets the order the working copies are updated. Updating them before a build is {getG('pull') ? 'on' : 'off'}.
                </span>
                <span style={{ marginLeft: -6 }}>
                  <DS.Button size="sm" variant="ghost" onClick={() => setSec('general')}>Pull before build</DS.Button>
                </span>
              </div>
            </>
          )}

          {sec === 'layers' && (
            <>
              <PaneHead title="Layers">
                Projects are grouped by the first matching pattern (regex on the project name), top to bottom. Non-matching projects fall under <span style={{ fontFamily: MONO }}>Other</span>.
              </PaneHead>
              {!draft.length && <div style={empty}>No layers yet — projects show as a single list in build order.</div>}
              {draft.length > 0 && (
                <div style={{ display: 'flex', gap: 6, padding: '0 34px 5px 30px', boxSizing: 'border-box' }}>
                  <span style={{ ...colCap, width: 170, flex: 'none' }}>Layer name</span>
                  <span style={{ ...colCap, flex: 1 }}>Pattern</span>
                </div>
              )}
              {draft.map((l, i) => (
                <div key={i} style={card(dragIdx === i, dragOff)}>
                  <span
                    onPointerDown={(e) => layerDrag.start(e, i)} onPointerMove={layerDrag.move} onPointerUp={layerDrag.end} onPointerCancel={layerDrag.end}
                    title="Drag to reorder" aria-label="Drag to reorder" style={gripStyle(dragIdx === i)}>
                    <I.grip />
                  </span>
                  <DS.Input value={l.name} placeholder={layerPh(i)[0]} onChange={(e) => upd(i, { name: e.target.value })} style={{ width: 170, flex: 'none' }} />
                  <DS.Input mono value={l.rx} invalid={compiled[i] === undefined} placeholder={layerPh(i)[1]} spellCheck={false}
                    onChange={(e) => upd(i, { rx: e.target.value })} style={{ width: 'auto', flex: 1 }} />
                  <DS.IconButton size="sm" title="Delete layer" onClick={() => setDraft((d) => d.filter((_, k) => k !== i))}><I.trash /></DS.IconButton>
                </div>
              ))}
              <div style={{ marginTop: 10, marginLeft: -10 }}>
                <DS.Button size="sm" variant="ghost" icon={<I.plus />} onClick={() => setDraft((d) => [...d, { name: '', rx: '' }])}>Add layer</DS.Button>
              </div>
            </>
          )}
        </FadeScroll>
      </div>
    </DialogShell>
  );
}

/* ================= EXIT (uygulama 1.11.0) =================
   Kart: 320px Ds.Popover, dolgu 0; butonun 9px altında, SAĞ kenarı butonla hizalı, sağ üst kökenden düşer.
   Dialogların ÜSTÜNDE durur. Üç blok, aralarında 1px border: durum · ENDS ON EXIT · karar. Metinler ExitText. */
const TB_SEP = { width: 1, height: 14, background: 'var(--border)', margin: '0 8px', flex: 'none' };
const EXIT_OUTCOME = {
  Clean: 'Projects cleaned so far stay clean; the rest keep their outputs.',
  'Deep clean': 'Part of the workspace may already be reset; the next Build compiles what it needs.',
  Optimize: 'Packages restored so far stay; the next Optimize does the rest.',
  Sync: 'Nothing is lost; the next start syncs again.',
  'Branch switch': 'Cutting git off mid-write could leave the working tree half-switched.',
  Pull: 'Cutting git off mid-write could leave the working tree half-updated.',
  'Worktree setup': 'Cutting git off mid-write could leave the worktree half-created.',
};
const EXIT_WAITS = { 'Branch switch': 1, Pull: 1, 'Worktree setup': 1 };
function exitOutcome(work, inFlight) {
  if (!work) return null;
  if (work === 'Build' || work === 'Rebuild' || work === 'Resolve') {
    const keep = 'Finished projects keep their results; the next Build picks up the rest.';
    return inFlight === 0 ? keep : inFlight === 1 ? '1 project in flight stays unbuilt. ' + keep : inFlight + ' projects in flight stay unbuilt. ' + keep;
  }
  return EXIT_OUTCOME[work];
}
function ExitCard({ work, progress, procs, inFlight, onCancel, onConfirm }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const caps = { fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)' };
  const line = '1px solid var(--border)';
  const waits = !!(work && EXIT_WAITS[work]);
  const inSentence = work ? work.toLowerCase() : '';
  const sentence = !work ? 'Nothing is running. Exit quits right away.'
    : waits ? 'Exit waits for the ' + inSentence + ', then quits.' : 'Exit stops the ' + inSentence + ', then quits.';
  const outcome = exitOutcome(work, inFlight);
  const action = !work ? 'Exit' : waits ? 'Wait and exit' : 'Stop and exit';
  return (
    <div role="dialog" aria-label="Exit" className={REDUCED ? undefined : 'bo-drop-in'}
      style={{
        position: 'absolute', top: 'calc(100% + 9px)', right: 0, zIndex: 260, width: 320, boxSizing: 'border-box',
        background: 'var(--surface-overlay)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--elevation-overlay)', fontFamily: 'var(--font-sans)', cursor: 'default', transformOrigin: '100% 0',
      }}>
      <div style={{ padding: '13px 16px 14px' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
          <span style={{ ...caps, flex: 1 }}>{work ? work + ' in progress' : 'Exit'}</span>
          {work && progress && <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', fontVariantNumeric: 'tabular-nums' }}>{progress}</span>}
        </div>
        <div style={{ marginTop: 8, fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)', textWrap: 'pretty' }}>{sentence}</div>
      </div>
      {procs.length > 0 && (
        <div style={{ padding: '12px 16px 13px', borderTop: line }}>
          <div style={caps}>Ends on exit</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4, marginTop: 8 }}>
            {procs.map(([nm, n]) => (
              <div key={nm} style={{ display: 'flex', justifyContent: 'space-between', fontFamily: MONO, fontSize: 'var(--text-2xs)' }}>
                <span style={{ color: 'var(--text-secondary)' }}>{nm}</span>
                <span style={{ color: 'var(--text-dim)', fontVariantNumeric: 'tabular-nums' }}>×{n}</span>
              </div>
            ))}
          </div>
        </div>
      )}
      <div style={{ padding: '12px 16px 14px', borderTop: line }}>
        {outcome && <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 1.5, textWrap: 'pretty' }}>{outcome}</div>}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: outcome ? 12 : 0 }}>
          <DS.Button size="sm" variant="secondary" onClick={onCancel}>Cancel</DS.Button>
          <DS.Button size="sm" variant="danger" icon={<I.power size={12} />} onClick={onConfirm}>{action}</DS.Button>
        </div>
      </div>
    </div>
  );
}
/* Kapanış ekranı, Exit kipi (ExitClosingTimeline): güncelleme ekranıyla aynı 232px sütun. Adım 1 'Stopping build…'
   (git yazımında 'Finishing pull…') en az 500ms, çubuk 0→50; çıkış hazırken adım 2 'Ending N processes…' 500ms, 50→100.
   Fade yok; ardından pencere kapanır. */
function ExitClosing({ run, logoSrc, onDone }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [, setF] = React.useState(0);
  React.useEffect(() => {
    if (!run) return undefined;
    let raf = 0;
    const loop = () => { setF((f) => f + 1); raf = requestAnimationFrame(loop); };
    raf = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(raf);
  }, [run]);
  const at = run ? Date.now() - run.t0 : 0;
  const endStart = run && run.readyAt != null ? Math.max(500, run.readyAt - run.t0) : Infinity;
  const ending = at >= endStart;
  const pct = !ending ? 50 * Math.min(at / 500, 1) : 50 + 50 * Math.min((at - endStart) / 500, 1);
  const done = ending && at >= endStart + 500;
  React.useEffect(() => { if (run && done) onDone(); }, [done]); // eslint-disable-line react-hooks/exhaustive-deps
  if (!run) return null;
  const n = run.procs.reduce((a, x) => a + x[1], 0);
  const waits = !!EXIT_WAITS[run.work];
  const label = !ending ? (waits ? 'Finishing ' : 'Stopping ') + run.work.toLowerCase() + '…'
    : n === 0 ? 'Quitting…' : n === 1 ? 'Ending 1 process…' : 'Ending ' + n + ' processes…';
  return (
    <div role="status" aria-live="polite" style={{
      position: 'absolute', inset: 0, zIndex: 300, display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'var(--surface-base)', fontFamily: 'var(--font-sans)',
    }}>
      <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: 232 }}>
        <img src={logoSrc} alt="" style={{ height: 30, display: 'block' }} />
        <div style={{ fontSize: 'var(--text-sm)', fontWeight: 600, color: 'var(--text-primary)', marginTop: 16 }}>Closing Build Orchestrator</div>
        <div style={{ marginTop: 6, fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-dim)' }}>{run.work + (run.progress ? ' · ' + run.progress : '')}</div>
        <DS.ProgressBar value={pct} status="building" height={2} style={{ width: '100%', marginTop: 20 }} />
        <div style={{ fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', marginTop: 9 }}>{label}</div>
      </div>
    </div>
  );
}
/* tepsi build göstergesi — uygulama Controls/TrayBuildIndicator (işaret ve döngü) + Services/TrayBuildIndicatorController (ne zaman,
   nasıl biter) + Views/TrayBuildOverlayWindow (187.5 × 67 DIP, çalışma alanının sağ alt köşesi, sağ 4 / alt 12). Görünür: pencere gizli
   VE faz Starting/Running/Stopping (Sync değil). Koşu bitince yarıda KESİLMEZ: yeni tur başlatmaz, içinde bulunduğu turu sonuna
   kadar oynatır ve son karede gizlenir (CSS'te tur sınırı = animationiteration). Reduced-motion: döngü hiç başlamaz (duruş karesi),
   bitişte anında gizlenir. Pencere geri gelince WindowGone ile birlikte anında söner (HideNow). Geometri app-mark.svg / BrandGeometry. */
function TrayBuildIndicator({ active }) {
  const [shown, setShown] = React.useState(!!active);
  const finishingRef = React.useRef(false);
  React.useEffect(() => {
    if (active) { if (!shown) setShown(true); return; } // ekrandaysa zaten koşuyor (controller: _shown → dokunma)
    if (!shown) return;
    if (REDUCED) setShown(false); // oynatılacak çıkış evresi yok
    else finishingRef.current = true; // RequestFinish: yalnız bayrak
  }, [active]); // eslint-disable-line react-hooks/exhaustive-deps
  const onIteration = (e) => {
    if (e.animationName !== 'tbi-mid-x' || !finishingRef.current) return; // son parça (orta koyu) 3.000'de biter
    finishingRef.current = false;
    setShown(false);
  };
  if (!shown) return null;
  return (
    <svg width={187.5} height={67} viewBox="-30 76 375 134" role="img" aria-label="Build Orchestrator — build running"
      onAnimationIteration={onIteration} style={{ display: 'block', overflow: 'hidden' }}>
      <defs>
        <linearGradient id="tbi-g" gradientUnits="userSpaceOnUse" x1="161.5" y1="82" x2="182.5" y2="204">
          <stop offset="0" stopColor="var(--amber-bright)" /><stop offset=".5" stopColor="var(--amber)" /><stop offset="1" stopColor="var(--brand-chevron-deep)" />
        </linearGradient>
        <filter id="tbi-s" x="-20%" y="-40%" width="140%" height="200%"><feDropShadow dx="0" dy="2" stdDeviation="1.6" floodColor="#000" floodOpacity=".30" /></filter>
        <filter id="tbi-c" x="-30%" y="-20%" width="160%" height="150%"><feDropShadow dx="0" dy="2.6" stdDeviation="1.87" floodColor="#000" floodOpacity=".32" /></filter>
        {/* silme maskesi: şevronun siluetini izler, şevronla aynı keyframe'lerle kayar; 1.150'de düşer (tbi-unmask) */}
        <clipPath id="tbi-m" clipPathUnits="userSpaceOnUse">
          <path className="tbi-sweep" d="M -600 -30 L 158.5 -30 L 158.5 83 L 147 95 L 152 104 L 186 140 L 186 148 L 151 184 L 148 196 L 158.5 203 L 158.5 320 L -600 320 Z" />
          <rect className="tbi-unmask" x="-1000" y="-1000" width="3000" height="3000" />
        </clipPath>
      </defs>
      {/* gölge kırpmadan ÖNCE: filtre içteki grupta, kırpma dıştakinde */}
      <g clipPath="url(#tbi-m)"><g filter="url(#tbi-s)">
        <rect className="tbi-top" x="73.5" y="83" width="35" height="21" rx="10.5" fill="var(--brand-strip-dim)" />
        <rect className="tbi-amber" x="119.5" y="83" width="51" height="21" rx="10.5" fill="var(--amber)" />
        <rect className="tbi-mid" x="51.5" y="131" width="39" height="21" rx="10.5" fill="var(--neutral-700)" />
        <rect className="tbi-white" x="102.5" y="131" width="60" height="22" rx="11" fill="var(--text-primary)" />
        <rect className="tbi-silver" x="65.5" y="178" width="57" height="21" rx="10.5" fill="var(--text-secondary)" />
      </g></g>
      <path className="tbi-chev" filter="url(#tbi-c)" fill="url(#tbi-g)"
        d="M156.5 83 L177 83 C181.6 83 184.7 85.1 187.8 88.5 L225.3 131.5 C228.2 134.9 229.5 138.3 229.5 142.4 C229.5 146.5 228.2 149.8 225.3 153.2 L184.6 198.3 C181.5 201.6 177.8 203 173.5 203 L156.5 203 C151.3 203 147.6 200.4 146 196.4 C144.3 192.1 145.7 187.8 149 184.2 L183.3 146.2 C186.1 143.2 186.3 140.8 183.7 137.8 L150.2 103.7 C147.3 100.7 145.9 96.9 145.9 93 C145.9 87.5 150.6 83 156.5 83 Z" />
    </svg>
  );
}
/* pencere gitti — prototipin demo yüzeyi (uygulamada pencere kapanır ya da tepsiye iner). Tıklama geri getirir.
   Tepsi kipinde altta sahte bir görev çubuğu (OS yüzeyi — yalnız demo) ve onun üstünde, sağ altta tepsi build göstergesi. */
const DEMO_TASKBAR_H = 40;
function WindowGone({ mode, buildLive, onBack }) {
  if (!mode) return null;
  return (
    <div onClick={onBack} style={{
      position: 'absolute', inset: 0, zIndex: 310, display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'var(--surface-base)', cursor: 'pointer', fontFamily: 'var(--font-sans)',
    }}>
      <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-faint)' }}>
        {mode === 'tray' ? 'Demo: the window is hidden in the tray — click to bring it back'
          : mode === 'update' ? 'Demo: Build Orchestrator has quit — the update installs without a window and starts the new version. Click to start it'
          : 'Demo: Build Orchestrator has quit — click to start it again'}
      </span>
      {mode === 'tray' && (
        <>
          <div aria-hidden="true" title="Demo: taskbar" style={{
            position: 'absolute', left: 0, right: 0, bottom: 0, height: DEMO_TASKBAR_H,
            background: 'var(--surface-sunken)', borderTop: '1px solid var(--border-subtle)',
          }} />
          <div style={{ position: 'absolute', right: 4, bottom: DEMO_TASKBAR_H + 12 }}>
            <TrayBuildIndicator active={buildLive} />
          </div>
        </>
      )}
    </div>
  );
}

/* ================= BUILD MENÜSÜ ================= */
function BuildMenuItem({ icon, title, desc, kbd, tip, onPick }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [hover, setHover] = React.useState(false);
  /* Clean satırının tooltip'i (AccessibilityNames.CleanSolutionTooltip) — derin Clean'den ayrımı söyler */
  return (
    <div onClick={onPick} title={tip} onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      style={{
        display: 'flex', alignItems: 'center', gap: 10, padding: '7px 8px', borderRadius: 'var(--radius-sm)',
        cursor: 'pointer', background: hover ? 'var(--surface-raised)' : 'transparent', userSelect: 'none',
        transition: 'background var(--duration-fast) var(--ease-standard)',
      }}>
      <span style={{ display: 'inline-flex', color: 'var(--text-secondary)', width: 14, justifyContent: 'center', flex: 'none' }}>{icon}</span>
      <span style={{ flex: 1, minWidth: 0 }}>
        <span style={{ display: 'block', fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)' }}>{title}</span>
        <span style={{ display: 'block', fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', marginTop: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{desc}</span>
      </span>
      {kbd ? <DS.Kbd>{kbd}</DS.Kbd> : null}
    </div>
  );
}

/* ================= ANA UYGULAMA ================= */
function BuildApp(props) {
  const ok = () => !!(window.DeltaBuildOrchestratorDS_eb0bd1 && window.DELTA_BO);
  const [dsReady, setDsReady] = React.useState(ok());
  React.useEffect(() => {
    if (dsReady) return;
    const iv = setInterval(() => { if (ok()) { setDsReady(true); clearInterval(iv); } }, 40);
    return () => clearInterval(iv);
  }, [dsReady]);
  if (!dsReady) return <div style={{ height: '100%' }}></div>;
  BO = window.DELTA_BO;
  return <BuildAppInner {...props} />;
}

function BuildAppInner({ scene = 1, autoPlay = true, simSpeed = 1, logoBase = 'assets', updateAvailable = true, reducedParallelism = false }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  /* demo (1.25.0): motor paralelliği makineye göre kısmış (düşük bellek) — kaynak şeridi derlerken `2/4` gösterir */
  const reduced = reducedParallelism === true || reducedParallelism === 'true';
  const engRef = React.useRef(null);
  if (!engRef.current) {
    try { const v = JSON.parse(localStorage.getItem('delta-bo-external-v1')); if (Array.isArray(v)) { BO.setExternal(v); syncGraphMaps(); } } catch (e) { /* yok */ }
    engRef.current = new BO.SimEngine();
  }
  const eng = engRef.current;
  eng.onFrame = () => bumpRef.current && bumpRef.current();

  const [, setV] = React.useState(0);
  const bump = () => setV((v) => v + 1);
  const bumpRef = React.useRef(null);
  bumpRef.current = bump;
  const seedRef = React.useRef(0);
  const speedRef = React.useRef(simSpeed);
  speedRef.current = simSpeed || 1;

  const [workspace, setWorkspace] = React.useState(true);
  const [repoRoot, setRepoRoot] = React.useState('D:\\src\\ACME');
  const [selected, setSelected] = React.useState(null);
  const [filter, setFilter] = React.useState(() => new Set()); // çoklu statü filtresi (VEYA)
  const [query, setQuery] = React.useState('');
  const [hovered, setHovered] = React.useState(null); // liste ↔ graf ortak hover (RunViewModel.HoveredProjectId)
  const [revealKey, setRevealKey] = React.useState(0);
  const [cfg, setCfg] = React.useState('Debug');
  const [perf, setPerf] = React.useState('Balanced');
  const allDirtyRef = React.useRef(false);
  const cycleRef = React.useRef(false); // Scene 8: workspace'te bilinen döngü — engine reset'leri arasında korunur
  const oneFailRef = React.useRef(false); // Scene 8: tek kasıtlı hata (Sales.Core) — kırmızı sonucu da aynı sahnede göster

  /* branch chip'i aktif kopyanın checkout'unu gösterir; seçim gerçek checkout'tur (SWITCHING BRANCH → Sync) */
  const [branchSel, setBranchSel] = React.useState('main');
  const activeBranch = branchSel;
  const [branchPop, setBranchPop] = React.useState(false);
  /* worktree (uygulama §10.6): iki kopya — repository ve Settings'teki worktree klasörü. Anahtar kopyalar arasında geçer
     (yeni kökte Sync). Demo: worktree klasörü ayarlı ve kurulu; kendi checkout'u develop. */
  const [copy, setCopy] = React.useState('repo'); // repo | worktree
  const [wtFolder, setWtFolder] = React.useState('D:\\src\\ACME-wt');
  const otherBranchRef = React.useRef('develop'); // diğer kopyanın checkout'u
  const [wtLinked, setWtLinked] = React.useState(['D:\\src\\ACME-wt']); // bu reponun linked worktree'leri (demo disk)
  /* kirli ağaç (demo): kopya başına commit'lenmemiş dosya sayısı; branch kartı (BranchSwitchCard) bekleyen geçişi taşır */
  const [dirty, setDirty] = React.useState({ repo: 0, worktree: 0 });
  const [branchCard, setBranchCard] = React.useState(null); // { branch, copy, files }
  const pendingWtBranchRef = React.useRef(null); // 'Set up worktree…' → başarılı Save worktree'ye bu branch'le geçer
  /* Exit (uygulama 1.11.0): onay kartı · kapanış ekranı · pencere gitti (tepsi ya da çıkış — prototip demo yüzeyi) */
  const [exitOpen, setExitOpen] = React.useState(false);
  const [closing, setClosing] = React.useState(null); // { t0, work, progress, procs, readyAt }
  const closingRef = React.useRef(null);
  closingRef.current = closing;
  const [gone, setGone] = React.useState(null); // 'tray' | 'exit'
  const [gitOp, setGitOp] = React.useState(null); // yarıdaki git işlemi (Merge…) — demo kancası
  const escNotedRef = React.useRef(false); // Esc'in 'can't be stopped' satırı iş başına bir kez

  const [settingsOpen, setSettingsOpen] = React.useState(false);
  const [settingsImport, setSettingsImport] = React.useState(false); // ilk açılışta dosya seçiciyi tetikle
  const [settingsWt, setSettingsWt] = React.useState(false); // worktree anahtarından/karttan açıldı: odak klasörde, Save worktree'ye geçer
  const [aboutOpen, setAboutOpen] = React.useState(false);
  const [notesOpen, setNotesOpen] = React.useState(false); // What's new — kendi dialogu
  const [unseenNotes, setUnseenNotes] = React.useState(notesUnseen);
  const [buildMenu, setBuildMenu] = React.useState(false);
  /* güncelleme (1.20.0): paket hazır → hap · kart · restart ekranı */
  const updOn = !(updateAvailable === false || updateAvailable === 'false');
  const [updReady, setUpdReady] = React.useState(false);
  const [updPop, setUpdPop] = React.useState(false);
  const [updRun, setUpdRun] = React.useState(null); // { t0, out } — restart ekranı
  const updRunRef = React.useRef(null);
  updRunRef.current = updRun;
  const updTimers = React.useRef([]);
  React.useEffect(() => {
    if (!updOn) { setUpdReady(false); setUpdPop(false); return undefined; }
    const t = setTimeout(() => setUpdReady(true), 1400); // açılıştaki sessiz kontrol bitti, paket inmiş
    return () => clearTimeout(t);
  }, [updOn]);
  React.useEffect(() => () => updTimers.current.forEach(clearTimeout), []);
  const [layerCfg, setLayerCfg] = React.useState(() => {
    try { const v = JSON.parse(localStorage.getItem('delta-bo-layers-v2')); if (Array.isArray(v)) return v; } catch (e) { /* yok */ }
    return DEFAULT_LAYER_CFG;
  });
  const [extCfg, setExtCfg] = React.useState(() => {
    try { const v = JSON.parse(localStorage.getItem('delta-bo-external-v1')); if (Array.isArray(v)) return v; } catch (e) { /* yok */ }
    return [];
  });
  /* harici working copy'ler build öncesi pull edilsin mi — alt bardaki '↓ pull' çipi */
  const [pullExt, setPullExt] = React.useState(() => {
    try { const v = localStorage.getItem('delta-bo-pull-external-v1'); if (v != null) return v === '1'; } catch (e) { /* yok */ }
    return true;
  });
  /* Ayarlar → General: başlangıç/tray/bildirim anahtarları tek nesnede (1.19.0) */
  const [general, setGeneral] = React.useState(() => {
    try {
      const v = JSON.parse(localStorage.getItem('delta-bo-general-v1'));
      if (v && typeof v === 'object') {
        const g = { ...DEFAULT_GENERAL, ...v };
        if (typeof v.notify !== 'boolean' && typeof v.notifyFail === 'boolean') g.notify = v.notifyFail; // eski anahtar
        delete g.notifyFail;
        return g;
      }
    } catch (e) { /* yok */ }
    return { ...DEFAULT_GENERAL };
  });
  const groups = React.useMemo(() => compileGroups(layerCfg), [layerCfg]);

  const defLayout = { mode: 'quad', col: 50, left: 50, right: 50 };
  const [layout, setLayout] = React.useState(() => {
    try { return { ...defLayout, ...(JSON.parse(localStorage.getItem('delta-bo-layout-v1')) || {}) }; } catch (e) { return defLayout; }
  });
  const savedLayout = React.useRef(layout);
  const bodyRef = React.useRef(null);
  const saveLayout = (l) => { try { localStorage.setItem('delta-bo-layout-v1', JSON.stringify(l)); } catch (e) { /* yok */ } };

  /* ---- saat: motor ilerletici (gerçek zamana bağlı — throttle'a dayanıklı) ---- */
  React.useEffect(() => {
    let raf = 0, iv = 0, last = Date.now(), alive = true;
    const tick = () => {
      if (!alive) return;
      const now = Date.now();
      let dt = Math.min(2000, now - last); // dönüşte dev adım yok: en çok 2s telafi
      last = now;
      const e = engRef.current;
      const live = () => e.phase === 'syncing' || e.phase === 'running' || e.phase === 'stopping' || e.todo.length > 0;
      if (live() && dt > 0) {
        const sp = speedRef.current;
        while (dt > 0 && live()) {
          const step = Math.min(150, dt);
          e.advance(step * sp);
          dt -= step;
        }
        bump();
      }
    };
    const loop = () => { tick(); raf = requestAnimationFrame(loop); };
    raf = requestAnimationFrame(loop);
    iv = setInterval(tick, 250); // rAF donduğunda (arka plan sekme) yedek saat
    const onVis = () => { last = Date.now(); tick(); };
    document.addEventListener('visibilitychange', onVis);
    return () => { alive = false; cancelAnimationFrame(raf); clearInterval(iv); document.removeEventListener('visibilitychange', onVis); };
  }, []);

  /* ---- ortak akışlar ---- */
  /* git uzak-uç durumu motor reset'inden sağ çıkar: pull sonrası HEAD ve 'behind' korunur (v1.16.0) */
  const gitRef = React.useRef({ behind: 3, head: null });
  const branchSha = (nm) => (BO.BRANCHES.find((b) => b.name === nm) || {}).sha;
  /* uzak mesafe yalnız repository kopyasının main'inde örneklenir (demo); diğer checkout'larda 0 — chip görünmez */
  const resetEngine = (opts, br) => {
    const g = gitRef.current, b = br || branchSel, onMain = b === 'main' && copy === 'repo';
    eng.reset({
      seed: ++seedRef.current, cfg, maxPar: fittedPar(perf, reduced),
      targetSha: (onMain && g.head) || branchSha(b),
      behind: onMain ? g.behind : 0,
      cycle: cycleRef.current, oneFail: oneFailRef.current, ...opts,
    });
    eng.branchName = b;
  };
  /* kapılar: motor hazır, workspace açık, iş yok (uygulama CanStartSync / CanSwitchConfiguration / CanSwitchCopy) */
  const engineOk = eng.engine === 'ok';
  const canWork = () => workspace && engineOk && !eng.busy() && !closingRef.current;
  const doSync = (o) => {
    if (!canWork()) return;
    resetEngine({ allDirty: allDirtyRef.current, keepLog: !!(o && o.keepLog === true) });
    setSelected(null);
    setFilter(new Set());
    setRevealKey((k) => k + 1);
    eng.startSync();
    bump();
  };
  /* '<N> behind' çipi: ff-only pull — başarılıysa çip düşer ve otomatik Sync koşar
     (yeni HEAD'in diffi hesaplanır). Konsol pull satırları Sync'te korunur (keepLog). */
  const doPull = () => {
    if (!canWork() || gitOp || !(eng.behind > 0)) return;
    /* pull pill'e baştan SYNC yazar; şerit '▸ Pulling origin/<branch>…' — ff-only biter, Sync zincirlenir */
    eng.beginWork('pull', branchSel, 700, () => {
      const res = eng.pullRemote();
      if (res === 'ok') {
        gitRef.current = { behind: 0, head: eng.targetSha };
        resetEngine({ allDirty: allDirtyRef.current, keepLog: true });
        setSelected(null); setFilter(new Set()); setRevealKey((k) => k + 1);
        eng.startSync();
      } else eng.op = null; // ret: pill düşer
      bump();
    });
    bump();
  };
  /* Build: koreografiyle durumdan koşar — konsol/stream temizlenir, herkes nötre döner,
     kapsam ×2 blink, sonra derleme. Rebuild aynı yol, kapsam = tüm projeler. */
  const doBuild = () => {
    if (!canWork()) return;
    setBuildMenu(false);
    setSelected(null); // koşu seçimi bırakır, filtreyi KORUR (grafta koşu boyunca filtre askıda)
    if (eng.phase === 'boot') {
      resetEngine({ allDirty: allDirtyRef.current });
      allDirtyRef.current = false;
      setRevealKey((k) => k + 1);
      eng.startSync();
      eng.at(eng.syncMs + 550, () => eng.beginBuild());
    } else if (allDirtyRef.current) {
      allDirtyRef.current = false;
      eng.beginRebuild();
    } else {
      eng.beginBuild();
    }
    bump();
  };
  const doRebuild = () => {
    if (!canWork()) return;
    setBuildMenu(false);
    setSelected(null);
    allDirtyRef.current = false;
    if (eng.phase === 'boot') {
      resetEngine({ allDirty: true });
      setRevealKey((k) => k + 1);
      eng.startSync();
      eng.at(eng.syncMs + 550, () => eng.beginRebuild());
    } else eng.beginRebuild();
    bump();
  };
  /* Build menüsündeki Clean (VS Clean Solution) — her projede -t:Clean KOŞUSU (F7); derin Clean bakım kutusunda */
  const doCleanSln = () => {
    if (!canWork()) return;
    setBuildMenu(false); setSelected(null);
    if (!eng.beginCleanRun()) return;
    bump();
  };
  /* satır aksiyonları: build / rebuild / clean — YALNIZ o proje.
     Satıra tıklanmış gibi davranmaz: seçim ve filtre sıfırlanır, graf odağı açılmaz — süreç
     Build/Rebuild/Resolve ile BİREBİR aynı. Reveal'a dokunulmaz: liste yerinden oynamaz. */
  const doRowAction = (kind, name) => {
    if (!canWork()) return;
    setBuildMenu(false);
    const ok = kind === 'clean' ? eng.beginProjectClean(name) : eng.beginProject(name, kind);
    if (!ok) return;
    setSelected(null);
    bump();
  };
  const doResolve = () => {
    if (!canWork() || !eng.cycle) return;
    setBuildMenu(false);
    setSelected(null);
    eng.startResolve();
    bump();
  };

  /* ---- bakım görevleri: Clean (çıktıları sil) / Optimize (restore + prune + index) ---- */
  const doClean = () => {
    if (!canWork()) return;
    setBuildMenu(false); setSelected(null); setFilter(new Set());
    if (!eng.startTask('clean')) return;
    allDirtyRef.current = true; // çıktılar gitti — sonraki Build tam derleme
    bump();
  };
  const doOptimize = () => {
    if (!canWork()) return;
    setBuildMenu(false); setSelected(null);
    eng.startTask('optimize');
    bump();
  };

  /* ---- güncelleme: Restart to update → restart ekranı → uygulama baştan açılır (Sync), hap düşer ---- */
  const rebootApp = () => {
    setBranchPop(false); setBuildMenu(false); setSettingsOpen(false); setAboutOpen(false); setNotesOpen(false);
    setSelected(null); setFilter(new Set()); setQuery('');
    setUnseenNotes(true); // yeni sürümün notları henüz görülmedi — sparkle noktası geri gelir
    if (!workspace) { eng.reset({ empty: true, seed: ++seedRef.current }); bump(); return; }
    resetEngine({ allDirty: allDirtyRef.current });
    setRevealKey((k) => k + 1);
    eng.startSync();
    bump();
  };
  const restartToUpdate = () => {
    if (eng.busy() || updRunRef.current) return;
    setUpdPop(false);
    setUpdRun({ t0: Date.now(), out: false });
    /* süreç çıkar; Update.exe penceresiz kurar ve yeni sürümü sıradan bir açılış gibi başlatır — prototipte 'pencere gitti' yüzeyi */
    updTimers.current.push(setTimeout(() => { setUpdRun(null); setGone('update'); }, UPDATE_TOTAL + 60));
  };
  /* Restart kilidi: yarım kalan build/görev olmasın — sebep kartta tek satır */
  const updBlocked = eng.task || eng.work ? 'Available once the running task finishes.'
    : eng.phase === 'syncing' ? 'Available once Sync finishes.'
    : eng.phase === 'stopping' ? (eng.stopStage === 'terminating' ? 'Available once the build stops.' : 'Available once the build stops — Esc stops it now.')
    : (eng.phase === 'running' || eng.phase === 'marking') ? 'Available once the build finishes — Esc stops it.'
    : null;

  /* ---- sahneler ---- */
  const applyScene = React.useCallback((n) => {
    const e = engRef.current;
    setBranchPop(false); setBuildMenu(false); setSettingsOpen(false); setAboutOpen(false); setNotesOpen(false); setFilter(new Set()); setSelected(null);
    setBranchCard(null); setSettingsWt(false); setDirty({ repo: 0, worktree: 0 });
    setQuery('');
    setRepoRoot(n === 5 ? '' : 'D:\\src\\ACME');
    setBranchSel('main'); setCopy('repo'); otherBranchRef.current = 'develop';
    setExitOpen(false); setClosing(null); setGone(null); setGitOp(null); escNotedRef.current = false;
    e.engine = 'ok'; e.engineMsg = null; e.rootKey = 'repo';
    gitRef.current = { behind: 3, head: null }; // sahne sıfırlanınca uzak uç yeniden 3 commit önde
    allDirtyRef.current = false;
    cycleRef.current = n === 8;
    oneFailRef.current = n === 8;
    if (n !== 7) setLayout(savedLayout.current);
    const start = (opts, after) => {
      setWorkspace(true);
      eng.reset({ seed: ++seedRef.current, cfg, maxPar: fittedPar(perf, reduced), ...opts });
      eng.branchName = 'main';
      eng.say('info', 'Engine ready — v' + ABOUT_VERSION); // açılış: motor hazır satırı, Sync altına akar
      setRevealKey((k) => k + 1);
      eng.startSync();
      eng.at(eng.syncMs + 550, () => eng.beginBuild());
      if (after) after(eng);
      bump();
    };
    if (n === 1) {
      if (autoPlay === false || autoPlay === 'false') {
        setWorkspace(true);
        eng.reset({ seed: ++seedRef.current, synced: true });
        eng.branchName = 'main';
        eng.say('info', 'Engine ready — v' + ABOUT_VERSION);
        eng.sayIdleSync();
        setRevealKey((k) => k + 1);
        bump();
      } else start({ inUse: 'ACME.Client.Sales' }); // demo: post-build kopyası çalışan programın tuttuğu dosyaya çarpar
    } else if (n === 2) {
      start({}, (e2) => {
        e2.noMark = true; // hızlı sarma gerçek zamanlı işaretleme dalgasını bekleyemez — koşu doğrudan başlar
        e2.fastForwardUntil((x) => x.finishedOfWB() >= 7 && x.buildingList().length > 0, 40000);
        e2.noMark = false;
        const b = e2.buildingList();
        setSelected(b.indexOf('ACME.Service.Api') >= 0 ? 'ACME.Service.Api' : b[0] || 'ACME.Sales.Api');
      });
    } else if (n === 3) {
      start({ multiFail: true }, (e2) => {
        e2.noMark = true;
        e2.fastForwardUntil((x) => x.counts().failed >= 4, 120000);
        e2.noMark = false;
      });
    } else if (n === 4) {
      start({ allClean: true });
    } else if (n === 5) {
      setWorkspace(false);
      eng.reset({ empty: true, seed: ++seedRef.current });
      bump();
    } else if (n === 6) {
      setWorkspace(true);
      eng.reset({ seed: ++seedRef.current, synced: true });
      eng.branchName = 'main';
      eng.say('info', 'Engine ready — v' + ABOUT_VERSION);
      eng.sayIdleSync(7);
      setRevealKey((k) => k + 1);
      setBranchPop(true);
      bump();
    } else if (n === 8) {
      // Cycle sahnesi: sync sonrası idle — Resolve cycles / Build akışı kullanıcıya bırakılır
      setWorkspace(true);
      eng.reset({ seed: ++seedRef.current, cfg, maxPar: fittedPar(perf, reduced), cycle: true, oneFail: true });
      eng.branchName = 'main';
      eng.say('info', 'Engine ready — v' + ABOUT_VERSION);
      setRevealKey((k) => k + 1);
      eng.startSync();
      bump();
    } else if (n === 7) {
      savedLayout.current = layout;
      setLayout({ mode: 'quad', col: 60, left: 74, right: 54 });
      start({}, (e2) => {
        e2.noMark = true;
        e2.fastForwardUntil((x) => x.finishedOfWB() >= 5 && x.buildingList().length > 0, 40000);
        e2.noMark = false;
        const b = e2.buildingList();
        setSelected(b.indexOf('ACME.Service.Api') >= 0 ? 'ACME.Service.Api' : b[0]);
      });
    }
  }, [autoPlay]); // eslint-disable-line

  const firstScene = React.useRef(true);
  React.useEffect(() => {
    const n = Number(scene) || 1;
    if (firstScene.current) {
      firstScene.current = false;
      const t = setTimeout(() => applyScene(n), 320);
      return () => clearTimeout(t);
    }
    applyScene(n);
  }, [scene, applyScene]);

  /* ---- Exit (uygulama 1.11.0, MainWindow.Exit.cs · ExitText) ----
     Uçuşta iş yoksa hemen çıkar; varsa 320px onay kartı açılır. Kartın birincil düğmesi işi durdurur (git yazımında bekler),
     kapanış ekranı iki adımı oynatır, pencere kapanır. Tepsi menüsünün Exit'i, × (Close to tray kapalıyken) ve Esc'in son
     halkası aynı karara gider. */
  const WORK_NAMES = { REBUILD: 'Rebuild', CLEAN: 'Clean', RESOLVE: 'Resolve' };
  const exitWork = () => {
    if (eng.work) return { checkout: 'Branch switch', pull: 'Pull', worktree: 'Worktree setup' }[eng.work.kind];
    if (eng.task) return eng.task.kind === 'optimize' ? 'Optimize' : 'Deep clean';
    if (eng.resolveRun) return 'Resolve';
    if (eng.phase === 'running' || eng.phase === 'stopping' || eng.phase === 'marking') return WORK_NAMES[eng.opLabel()] || 'Build';
    if (eng.phase === 'syncing') return 'Sync';
    return null;
  };
  const exitWaits = (w) => w === 'Branch switch' || w === 'Pull' || w === 'Worktree setup';
  const exitProgress = () => (eng.phase === 'running' || eng.phase === 'stopping') && !eng._fastCheck()
    ? (eng.resolveRun ? eng.resolveRun.fin + '/' + eng.resolveRun.total : eng.finishedOfWB() + '/' + eng.willBuild.size) : null;
  /* dış job'un süreçleri (motor ve conhost hariç) — prototip örneği; sayıya göre azalan, sonra ada göre */
  const exitProcesses = () => {
    const out = [];
    const nb = eng.buildingList().length;
    if (eng.phase === 'running' || eng.phase === 'stopping') {
      if (nb) out.push(['MSBuild.exe', nb]);
      if (!eng.cleanRun) out.push(['VBCSCompiler.exe', 1]);
    }
    if (eng.phase === 'syncing' || eng.work) out.push(['git.exe', 1]);
    if (eng.task && eng.task.kind === 'optimize') out.push(['dotnet.exe', 1]);
    return out.sort((x, y) => y[1] - x[1] || (x[0] < y[0] ? -1 : 1));
  };
  const quitNow = () => { setExitOpen(false); setBranchPop(false); setBuildMenu(false); setUpdPop(false); setGone('exit'); };
  const requestExit = () => {
    if (closingRef.current || gone || updRunRef.current) return;
    if (exitOpen) { setExitOpen(false); return; } // ikinci basış kartı kapatır
    if (!exitWork()) { quitNow(); return; }
    setBranchPop(false); setBuildMenu(false); setUpdPop(false);
    setExitOpen(true);
  };
  const confirmExit = () => {
    const w = exitWork();
    if (!w) { quitNow(); return; }
    const procs = exitProcesses(), progress = exitProgress();
    eng.say('dim', 'exit requested — ending the work in flight, then closing');
    const waits = exitWaits(w);
    if (!waits) { // iş kesilir: koşu durur, Sync/bakım işi süreçleriyle biter
      if (eng.phase === 'running' || eng.phase === 'stopping' || eng.phase === 'marking') eng.stop();
      else { eng.todo = []; eng.task = null; eng.syncStep = null; eng.discover = null; }
    }
    setExitOpen(false); setBranchPop(false); setBuildMenu(false); setUpdPop(false);
    setSettingsOpen(false); setAboutOpen(false); setNotesOpen(false);
    setClosing({ t0: Date.now(), work: w, progress, procs, readyAt: waits ? null : Date.now() });
    bump();
  };
  /* git yazımı bitince çıkış hazırdır (ardından zincirlenen Sync beklenmez) */
  React.useEffect(() => {
    if (closing && closing.readyAt == null && !eng.work) setClosing((c2) => c2 && { ...c2, readyAt: Date.now() });
  });
  /* × ve Esc'in son halkası: Close to tray açıksa tepsiye, kapalıysa Exit (iş varsa kartla) */
  const closeWindow = () => {
    if (closingRef.current || gone) return;
    if (general.closeTray) { setExitOpen(false); setGone('tray'); return; }
    if (!exitWork()) quitNow(); else { setExitOpen(true); setBranchPop(false); setBuildMenu(false); setUpdPop(false); }
  };
  /* Esc'in 'durdurulamaz' satırı işin pill sözcüğüyle (OperationLabel.ForWork) — iş başına bir kez */
  const unstoppableLabel = () => eng.work ? eng.opLabel() : eng.task ? eng.opLabel() : eng.phase === 'syncing' ? 'SYNC' : null;
  React.useEffect(() => { if (!eng.busy()) escNotedRef.current = false; });

  /* ---- klavye (uygulama KeyboardShortcuts.WindowBindings): F5 Build · F6 Rebuild · F7 Clean · Ctrl+F · F1 · Esc · Ctrl+Q ---- */
  React.useEffect(() => {
    const h = (e) => {
      if (updRunRef.current || closingRef.current) return; // kapanış ekranı açıkken klavye yok sayılır
      if (gone) return;
      if (e.key === 'F5' && !e.ctrlKey && !e.shiftKey && !e.altKey) { e.preventDefault(); doBuild(); } // yalnız başlatır
      if (e.key === 'F6' && !e.ctrlKey && !e.shiftKey && !e.altKey) { e.preventDefault(); doRebuild(); }
      if (e.key === 'F7' && !e.ctrlKey && !e.shiftKey && !e.altKey) { e.preventDefault(); doCleanSln(); }
      if (e.key === 'F1' && !e.ctrlKey && !e.metaKey) { e.preventDefault(); setAboutOpen((v) => !v); }
      if ((e.ctrlKey || e.metaKey) && (e.key === 'q' || e.key === 'Q')) { e.preventDefault(); requestExit(); }
      if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) {
        e.preventDefault();
        const el = document.getElementById('bo-proj-search');
        if (el) el.focus();
      }
      if (e.key === 'Escape') {
        /* zincir: exit kartı → dialog → popover/menü → seçim → koşan build'i durdur (Stop → Stop now) →
           durdurulamayan iş için tek satır → pencereyi × gibi kapat */
        const runLive = eng.phase === 'running' || eng.phase === 'marking' || eng.phase === 'stopping';
        if (exitOpen) setExitOpen(false);
        else if (notesOpen) setNotesOpen(false);
        else if (aboutOpen) setAboutOpen(false);
        else if (settingsOpen) setSettingsOpen(false);
        else if (branchPop || buildMenu || updPop || branchCard) { setBranchPop(false); setBuildMenu(false); setUpdPop(false); setBranchCard(null); }
        else if (selected) setSelected(null);
        else if (runLive && eng.stopStage !== 'terminating') { eng.requestStop(); bump(); }
        else if (!runLive && unstoppableLabel() && !escNotedRef.current) {
          escNotedRef.current = true;
          eng.say('dim', unstoppableLabel().toLowerCase() + " can't be stopped — it will finish on its own");
          bump();
        } else closeWindow();
      }
    };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  });
  /* güncelleme kartı bir dialog açılınca kapanır (uygulama: Popup kendi HWND'si, dialogun altında duramaz) */
  React.useEffect(() => { if (settingsOpen || aboutOpen || notesOpen) setUpdPop(false); }, [settingsOpen, aboutOpen, notesOpen]);
  React.useEffect(() => {
    if (!exitOpen) return undefined;
    const h = (e) => { if (e.target.closest && e.target.closest('[data-bo-exit]')) return; setExitOpen(false); };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, [exitOpen]);

  /* ---- dışarı tıkla → popover kapat ---- */
  React.useEffect(() => {
    if (!branchPop && !buildMenu) return;
    const h = (e) => {
      if (e.target.closest && (e.target.closest('[data-bo-popover]') || e.target.closest('[data-bo-chip]'))) return;
      setBranchPop(false); setBuildMenu(false);
    };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, [branchPop, buildMenu]);
  React.useEffect(() => {
    if (!branchCard) return undefined;
    const h = (e) => { if (e.target.closest && e.target.closest('[data-bo-popover]')) return; setBranchCard(null); };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, [branchCard]);
  React.useEffect(() => {
    if (!updPop) return undefined;
    const h = (e) => { if (e.target.closest && e.target.closest('[data-bo-upd]')) return; setUpdPop(false); };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, [updPop]);

  /* ---- eylemler ---- */
  const note = (t) => { eng.say('dim', t); bump(); };
  React.useEffect(() => { eng.pullExternal = pullExt; }, [pullExt]); // eslint-disable-line react-hooks/exhaustive-deps
  const select = (name) => { setSelected(name); };
  /* çoklu filtre: çipler bağımsız açılır-kapanır, seçili küme VEYA ile birleşir */
  const toggleFilter = (f) => {
    setSelected(null);
    setFilter((cur) => {
      const nx = new Set(cur);
      if (nx.has(f)) nx.delete(f); else nx.add(f);
      return nx;
    });
  };
  /* branch seçimi = gerçek checkout (uygulama SelectBranch): pill SWITCHING BRANCH, şerit '▸ Switching to <branch>…',
     ardından Sync. Kirli ağaç: Stash and switch açıksa sessizce stash'lenir; kapalıysa motor reddeder ve branch kartı açılır. */
  const pickBranch = (b) => {
    setBranchPop(false);
    if (b.name === branchSel || !canWork() || gitOp) return;
    checkoutHere(b.name, !!general.stash);
  };
  const checkoutHere = (name, stash) => {
    const n = dirty[copy] || 0;
    const from = branchSel, here = copy;
    setSelected(null);
    /* hedef diğer kopyada açık (RepositoryWriter: kir kapısından ve stash'ten ÖNCE): git aynı branch'i iki kopyada açtırmaz,
       hiçbir şey yapılmaz — pill düşer, konsol o kopyanın kökünü adlandırır (PlanProgressLines.SwitchRefusedElsewhere).
       Akışa satır düşmez (uygulama OnCheckoutCompletedAsync yalnız kirli ağaç reddine Warn yazar). */
    const holder = here === 'repo' ? (wtFolder && wtLinked.indexOf(wtFolder) >= 0 ? wtFolder : null) : repoRoot;
    if (holder && otherBranchRef.current === name) {
      eng.beginWork('checkout', name, 450, () => {
        eng.op = null;
        eng.say('warn', 'warning: ' + name + ' is already checked out in ' + holder + ' — a branch can be open in one working copy at a time');
        bump();
      });
      bump();
      return;
    }
    eng.say('cmd', 'git checkout ' + name);
    if (n > 0 && !stash) {
      /* motorun cevabı Dirty: hiçbir şey yapılmadı — konsol amber satır, akışa kısa satır, kart (RunViewModel.OnCheckoutCompletedAsync) */
      eng.beginWork('checkout', name, 450, () => {
        eng.op = null; // ret: pill düşer (CurrentOperation = null)
        eng.say('warn', n === 1 ? 'warning: 1 file has uncommitted changes — commit or stash them first'
          : 'warning: ' + n + ' files have uncommitted changes — commit or stash them first');
        eng.emit('warn', null, n === 1 ? 'branch switch refused — 1 uncommitted file' : 'branch switch refused — ' + n + ' uncommitted files');
        setBranchCard({ branch: name, copy: here, files: n });
        bump();
      });
      bump();
      return;
    }
    eng.beginWork('checkout', name, 900, () => {
      setBranchSel(name);
      if (n > 0) setDirty((d) => ({ ...d, [here]: 0 }));
      resetEngine({ allDirty: allDirtyRef.current, keepLog: true }, name);
      if (n > 0) eng.say('dim', 'Stashed uncommitted changes: "On ' + from + ': before switching to ' + name + '" — restore them with git stash pop');
      eng.say('dim', 'Switched to ' + name + ' (' + branchSha(name) + ') — from ' + from);
      setFilter(new Set()); setRevealKey((k) => k + 1);
      eng.startSync();
      bump();
    });
    bump();
  };
  /* branch kartının kararı (DecideBranchSwitchAsync): Cancel · diğer kopya · Stash and switch */
  const decideBranch = (d) => {
    const c = branchCard;
    setBranchCard(null);
    if (!c || d === 'cancel') return;
    if (!canWork()) return;
    if (d === 'stash') { checkoutHere(c.branch, true); return; }
    const other = c.copy === 'repo' ? 'worktree' : 'repo';
    if (other === 'worktree' && !wtFolder) {
      pendingWtBranchRef.current = c.branch;
      setSettingsWt(true); setSettingsOpen(true);
      return;
    }
    openInOtherCopy(c.branch, other);
  };
  /* diğer kopyada checkout, ardından uygulama o kopyaya geçer (OnOtherCopyCheckoutAsync); o kopya da kirliyse kart orada yeniden açılır */
  const openInOtherCopy = (name, other) => {
    const already = otherBranchRef.current === name;
    const otherDirty = dirty[other] || 0;
    eng.say('cmd', 'git checkout ' + name + '   (' + (other === 'worktree' ? wtFolder : repoRoot) + ')');
    eng.beginWork('checkout', name, already ? 300 : 900, () => {
      const from = otherBranchRef.current;
      if (!already && !otherDirty) otherBranchRef.current = name;
      const trailing = already ? [] : otherDirty
        ? [['warn', otherDirty === 1 ? 'warning: 1 file has uncommitted changes — commit or stash them first' : 'warning: ' + otherDirty + ' files have uncommitted changes — commit or stash them first']]
        : [['dim', 'Switched to ' + name + ' (' + branchSha(name) + ') — from ' + from]];
      switchCopyCore(other, trailing);
      if (otherDirty && !already) setBranchCard({ branch: name, copy: other, files: otherDirty });
    });
    bump();
  };
  /* Debug | Release bir Sync başlatır (ConfigurationChange): konsol + stream temizlenir, ilk satır 'Configuration → X' */
  const pickCfg = (v) => {
    if (v === cfg || !canWork()) return;
    setCfg(v);
    eng.cfg = v;
    /* hepsini kirli saymaz: defter her projenin son configuration'ını bilir — kendi dosyası değişmeyen 'last built in Debug' der */
    resetEngine({ cfg: v });
    eng.say('info', 'Configuration → ' + v);
    setSelected(null); setFilter(new Set()); setRevealKey((k) => k + 1);
    eng.startSync();
    bump();
  };
  /* mod çipi bir sonraki koşuyu belirler: koşan iş başladığı paralellikle sürer (şerit de onu yazar) */
  const cyclePerf = () => {
    const order = ['Full', 'Balanced', 'Light'];
    const nx = order[(order.indexOf(perf) + 1) % 3];
    setPerf(nx);
    eng.maxPar = fittedPar(nx, reduced);
    bump();
  };
  /* worktree anahtarı (uygulama PART_WorktreeToggle): klasör ayarlı değilse Settings açılır; ayarlıysa kopya geçişi =
     kök değişimi — yeni bölüm 'Switched to …', liste ve graf baştan (keşif → reveal), ilk Sync'te indeks */
  const switchCopy = () => {
    if (!wtFolder) { setSettingsWt(true); setSettingsOpen(true); return; }
    if (!canWork()) return;
    switchCopyCore(copy === 'repo' ? 'worktree' : 'repo');
  };
  const switchCopyCore = (next, trailing, folder) => {
    const nb = otherBranchRef.current;
    otherBranchRef.current = branchSel;
    const g = gitRef.current, onMain = next === 'repo' && nb === 'main';
    setCopy(next); setBranchSel(nb);
    resetEngine({ allDirty: allDirtyRef.current, behind: onMain ? g.behind : 0, targetSha: (onMain && g.head) || branchSha(nb) }, nb);
    eng.rootKey = next;
    eng.say('info', next === 'worktree' ? 'Switched to the worktree — ' + (folder || wtFolder) : 'Switched to the repository — ' + repoRoot);
    (trailing || []).forEach(([t, line]) => eng.say(t, line));
    setSelected(null); setFilter(new Set()); setRevealKey((k) => k + 1);
    eng.startSync();
    bump();
  };
  /* worktree kurulumu — yalnız Settings Save'de, klasör yoksa ya da boşsa (git worktree add --detach). then: kurulumdan sonra ne olacak */
  const createWorktree = (folder, then) => {
    if (!canWork()) return;
    const f = folder || 'D:\\src\\ACME-wt';
    eng.say('dim', 'Creating the worktree at ' + f + '…');
    eng.beginWork('worktree', null, 1400, () => {
      setWtFolder(f);
      setWtLinked((l) => (l.some((x) => normDir(x) === normDir(f)) ? l : l.concat([f])));
      otherBranchRef.current = branchSel; // detached HEAD: chip son bilinen branch'te kalır (RunViewModel.Worktree)
      eng.say('dim', 'Worktree ready at ' + f + ' (' + eng.targetSha + ')');
      if (then) { then(f); return; }
      resetEngine({ allDirty: allDirtyRef.current, keepLog: true });
      setRevealKey((k) => k + 1);
      eng.startSync();
      bump();
    });
    bump();
  };
  const restartEngine = () => { eng.restartEngine(ABOUT_VERSION, () => { if (workspace) doSync({ keepLog: true }); }); bump(); };
  /* prototip demo kancaları — motor ölümü/sessizliği, yarıdaki git işlemi, worktree kurulumu (konsoldan ya da ekran testinden) */
  React.useEffect(() => {
    window.boDemo = {
      engineDied: (cause) => { eng.engineDied(cause); setBranchPop(false); setBuildMenu(false); bump(); },
      engineSilent: () => { eng.engineSilent(); bump(); },
      restartEngine,
      gitOperation: (op) => setGitOp(op === undefined ? 'Merge' : op),
      createWorktree,
      clearWorktree: () => { if (copy === 'repo') setWtFolder(''); },
      /* kirli ağaç: aktif kopyada n commit'lenmemiş dosya (0 = temiz) */
      dirtyTree: (n, which) => { setDirty((d) => ({ ...d, [which || copy]: n === undefined ? 3 : n })); },
      stash: (on) => setGeneral((g) => ({ ...g, stash: on !== false })),
      exit: requestExit,
      /* ekran testi kolaylıkları: Debug/Release seçimi, proje sayfası, motor */
      cfg: (v) => pickCfg(v),
      select: (n) => setSelected(n),
      eng,
    };
  });
  /* demo anahtarı çalışırken değişirse motor sınırı da değişir (1.25.0) */
  React.useEffect(() => { eng.maxPar = fittedPar(perf, reduced); bump(); }, [reduced]); // eslint-disable-line react-hooks/exhaustive-deps
  const openWorkspace = (root) => {
    setWorkspace(true);
    resetEngine({});
    eng.say('cmd', 'workspace: ' + root + ' — ' + BO.PROJECTS.length + ' projects discovered');
    setRevealKey((k) => k + 1);
    eng.startSync();
    bump();
  };

  eng.perfMode = perf; // koşu başlarken dondurulur (runMode)
  eng.compact = general.compact !== false; // kompakt kip bir sonraki işlemde oturur (compactRun)
  eng.replaceInUse = !!general.replace;
  const compactTag = eng.compactRun != null ? eng.compactRun : eng.compact; // etkin kip (CompactConsoleInEffect)
  /* liste ve grafın ortak eşleşmesi (ProjectFilter.Matches): sorgu VE statü çipleri */
  const qLow = query.trim().toLowerCase();
  const matchFn = (name) => (!qLow || name.toLowerCase().indexOf(qLow) >= 0) && filterMatch(eng, name, filter);
  const filtering = filter.size > 0 || !!qLow;
  const c = eng.counts();
  const sc = stateCounts(eng);
  if (!workspace || eng.task) Object.keys(sc).forEach((k) => { sc[k] = 0; }); // liste boşken (workspace yok, bakım işi) sayaçlar da boş — Σ satırları sayar
  /* uyarı çipi: üçgen taşıyan satırlar (cycle ∪ dep sorunu) — tek birleşik kanal */
  const warnN = BO.PROJECTS.filter((p) => !!eng.p[p.name].depIssue || eng._isCycle(p.name)).length;
  const resolveUp = eng.cycle ? eng.resolveDeps().length : 0; // Resolve tooltip: önce derlenecek kirli upstream
  const running = eng.phase === 'running';
  const busy = eng.busy();
  /* Stop tüm kilit penceresi boyunca görünür (Starting · Running · Stopping); etiket aşamayı söyler (StopText) */
  const runLocked = running || eng.phase === 'marking' || eng.phase === 'stopping';
  const stopLabel = eng.stopStage === 'terminating' ? 'Terminating…' : eng.stopStage === 'now' ? 'Stop now' : 'Stop';
  const syncing = eng.phase === 'syncing';
  const engineDown = eng.engine === 'dead' || eng.engine === 'silent';
  const canGo = workspace && engineOk && !busy && !closing; // Build · Sync · Debug|Release · worktree kapısı
  const activeRoot = copy === 'worktree' ? wtFolder : repoRoot;
  const repoName = (repoRoot || '').split(/[\\/]/).filter(Boolean).pop() || '';
  const taskKind = eng.task ? eng.task.kind : null;
  const resolving = !!eng.resolveRun;
  const follow = running && !selected;
  const selSt = selected ? eng.p[selected] : null;

  /* ---- layout sürükleme ---- */
  const dragCol = (e) => {
    const r = bodyRef.current.getBoundingClientRect();
    const pct = Math.min(72, Math.max(28, ((e.clientX - r.left) / r.width) * 100));
    setLayout((l) => ({ ...l, col: pct }));
  };
  const dragLeft = (e) => {
    const r = bodyRef.current.getBoundingClientRect();
    const pct = Math.min(82, Math.max(18, ((e.clientY - r.top) / r.height) * 100));
    setLayout((l) => ({ ...l, left: pct }));
  };
  const dragRight = (e) => {
    const r = bodyRef.current.getBoundingClientRect();
    const pct = Math.min(82, Math.max(18, ((e.clientY - r.top) / r.height) * 100));
    setLayout((l) => ({ ...l, right: pct }));
  };
  const endDrag = () => { savedLayout.current = layout; saveLayout(layout); };

  /* ---- görünüm modları ---- */
  const setMode = (id) => {
    const preset = id === 'quad' ? { col: 50, left: 50, right: 50 } : id === 'list' ? { right: 50 } : { right: 76 };
    setLayout((l) => {
      const nl = { ...l, ...preset, mode: id };
      savedLayout.current = nl; saveLayout(nl);
      return nl;
    });
  };
  const showGraph = !(layout.mode === 'list' || layout.mode === 'focus');
  const saveSettings = (root, cfg2, ext2, pull2, gen2, wt2) => {
    const toWt = settingsWt;
    setSettingsWt(false);
    const pendBranch = pendingWtBranchRef.current;
    pendingWtBranchRef.current = null;
    const genNext = { ...DEFAULT_GENERAL, ...general, ...(gen2 || {}) };
    setGeneral(genNext);
    if (!eng.busy()) eng.compactRun = genNext.compact !== false; // boştayken etkin kip hemen yeniden yakalanır (Recapture)
    setLayerCfg(cfg2);
    setExtCfg(ext2 || []);
    const pullChanged = (pull2 !== false) !== pullExt;
    setPullExt(pull2 !== false);
    eng.pullExternal = pull2 !== false;
    try {
      localStorage.setItem('delta-bo-layers-v2', JSON.stringify(cfg2));
      localStorage.setItem('delta-bo-external-v1', JSON.stringify(ext2 || []));
      localStorage.setItem('delta-bo-pull-external-v1', pull2 !== false ? '1' : '0');
      localStorage.setItem('delta-bo-general-v1', JSON.stringify(genNext));
    } catch (e) { /* yok */ }
    setSettingsOpen(false);
    setSettingsImport(false);
    /* harici proje listesi değiştiyse proje kümesi değişir: motorun P'si yenilenir ve
       keşif baştan koşar (Sync) — harici projeler listenin/grafın başında tek katman olur. */
    const extChanged = JSON.stringify((ext2 || []).map((x) => x.path)) !== JSON.stringify((extCfg || []).map((x) => x.path));
    let added = [];
    if (extChanged) {
      added = BO.setExternal(ext2 || []);
      syncGraphMaps();
      eng.syncProjects(); // proje kümesi anında hizalanır — koşu sürüyorsa bile tanımsız state kalmaz
      if (eng.busy()) eng.stop(); // proje kümesi değişti: koşan işlem geçersiz
    }
    const rootChanged = root !== repoRoot;
    setRepoRoot(root);
    /* boş kök kaydedilebilir: ilk açılışta davet kalır; açık workspace'in üstünde workspace'i kapatır */
    if (!root) {
      setWtFolder('');
      if (workspace) { setWorkspace(false); setCopy('repo'); eng.reset({ empty: true, seed: ++seedRef.current }); bump(); }
      return;
    }
    /* first run: ayarlar kaydedilince workspace açılır ve sync başlar */
    if (!workspace) { setWtFolder(wt2 || ''); openWorkspace(root); return; }
    if (rootChanged) note('Repository root → ' + root + ' — Sync required');
    /* worktree klasörü: değiştiyse not; klasör bu reponun worktree'si değilse Save onu kurar; anahtardan/karttan açıldıysa geçer */
    const wtNext = wt2 || '';
    const wtChanged = normDir(wtNext) !== normDir(wtFolder || '');
    if (wtChanged) note(wtNext ? 'Worktree folder → ' + wtNext : 'Worktree folder cleared');
    const goWt = (f) => {
      if (!pendBranch) { switchCopyCore('worktree', [], f); return; }
      const from = otherBranchRef.current;
      otherBranchRef.current = pendBranch; // kartın branch'i worktree'de açılır
      switchCopyCore('worktree', [['dim', 'Switched to ' + pendBranch + ' (' + branchSha(pendBranch) + ') — from ' + from]], f);
    };
    if (wtNext && wtChanged && !wtLinked.some((x) => normDir(x) === normDir(wtNext))) {
      createWorktree(wtNext, toWt && copy === 'repo' ? goWt : null);
    } else {
      if (wtChanged) setWtFolder(wtNext);
      if (toWt && wtNext && copy === 'repo' && canWork()) setTimeout(() => goWt(wtNext), 0);
    }
    note(cfg2.length ? 'Layer definitions updated — ' + cfg2.length + ' layers' : 'Layers removed — single project list');
    if (pullChanged && (ext2 || []).length) {
      note(pull2 !== false
        ? 'Pull before build on — external working copies update first'
        : 'Pull before build off — external working copies are used as they are');
    }
    /* General anahtarları: değişen her biri tek satır not düşer */
    GENERAL_ROWS.forEach((r) => {
      if (r.key !== 'pull' && !!genNext[r.key] !== !!general[r.key] && r.on) note(genNext[r.key] ? r.on : r.off);
    });
    if (extChanged) {
      note((ext2 || []).length
        ? 'External projects → ' + (ext2 || []).length + ' paths, ' + added.length + ' projects — built first'
        : 'External projects cleared');
      doSync();
    }
  };

  const sceneMeta = SCENES[(Number(scene) || 1) - 1] || SCENES[0];

  return (
    <div data-screen-label={'Scene ' + sceneMeta.id + ' — ' + sceneMeta.label} style={{
      display: 'flex', flexDirection: 'column', height: '100%', position: 'relative',
      background: 'var(--surface-base)', border: '1px solid var(--border)', borderRadius: 'var(--radius-lg)',
      overflow: 'hidden', fontFamily: 'var(--font-sans)', fontSize: 'var(--text-sm)', color: 'var(--text-primary)',
    }}>
      {/* ---- TITLE BAR ---- */}
      <DS.TitleBar logoSrc={null} title="" style={{ position: 'relative' }} onClose={closeWindow}>
        {/* kaynak şeridi (1.25.0): pencerenin TAM ORTASINA absolute — sol/sağ kümeler ve Update hapı onu oynatmaz */}
        <ResourceStrip eng={eng} mode={perf} reduced={reduced} />
        <span style={{ marginRight: 'auto', display: 'inline-flex', alignItems: 'center', gap: 9, minWidth: 0 }}>
          <img src={logoUrl(logoBase, 'mark')} alt="Build Orchestrator" style={{ height: 19, display: 'block', flex: 'none' }} />
          <span style={{ fontSize: 'var(--text-xs)', fontWeight: 500, color: 'var(--text-secondary)', whiteSpace: 'nowrap' }}>Build Orchestrator</span>
          <span aria-hidden="true" style={{ width: 1, height: 13, background: 'var(--border)', flex: 'none' }}></span>
          <img src={logoUrl(logoBase, 'company')} alt="Delta" title="Delta" style={{ height: 10, display: 'block', opacity: 0.55, flex: 'none' }} />
        </span>
        {/* sağ küme: öğeler arası 1px (TitleBarClusterGap), ayraçlar 1×14 ve iki yanda 8px (TitleBarSeparator) */}
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 1, paddingRight: 8, flex: 'none' }}>
          {/* güncelleme hapı: kümenin SOLUNA eklenir — boş alana doğru büyür, mevcut ikonlar yerinden oynamaz */}
          {updReady && (
            <>
              <span data-bo-upd="1" style={{ position: 'relative', display: 'inline-flex' }}>
                <UpdatePill version={UPDATE_FEED.v} open={updPop}
                  onClick={() => { setUpdPop((v) => !v); setBranchPop(false); setBuildMenu(false); setExitOpen(false); }} />
                {updPop && (
                  <UpdateCard from={ABOUT_VERSION} feed={UPDATE_FEED} blocked={updBlocked}
                    onLater={() => setUpdPop(false)} onRestart={restartToUpdate} />
                )}
              </span>
              <span aria-hidden="true" className={REDUCED ? undefined : 'bo-upd-in'} style={TB_SEP}></span>
            </>
          )}
          {[
            ['quad', 'Default layout — graph + list · console + stream (splits reset)', I.layQuad],
            ['list', 'List layout — graph hidden · console + stream equal', I.layList],
            ['focus', 'Console focus — graph hidden · compact stream', I.layFocus],
          ].map(([id, tip, Ic]) => (
            <DS.Tooltip key={id} content={tip} side="bottom">
              <DS.IconButton size="sm" aria-label={tip} active={layout.mode === id} onClick={() => setMode(id)}><Ic /></DS.IconButton>
            </DS.Tooltip>
          ))}
          <span aria-hidden="true" style={TB_SEP}></span>
          <DS.Tooltip content="Settings — general, workspace, external projects and layers" side="bottom">
            <DS.IconButton size="sm" aria-label="Settings" active={settingsOpen} onClick={() => setSettingsOpen(true)}><I.gear /></DS.IconButton>
          </DS.Tooltip>
          <DS.Tooltip content={unseenNotes ? "What's new in " + ABOUT_VERSION : "What's new — release notes"} side="bottom">
            <span style={{ position: 'relative', display: 'inline-flex' }}>
              <DS.IconButton size="sm" aria-label="What's new" active={notesOpen}
                onClick={() => setNotesOpen(true)}><I.sparkle /></DS.IconButton>
              {unseenNotes && !notesOpen && (
                <span aria-hidden="true" style={{
                  position: 'absolute', top: 2, right: 2, width: 5, height: 5, borderRadius: '50%',
                  background: 'var(--amber)', pointerEvents: 'none',
                }}></span>
              )}
            </span>
          </DS.Tooltip>
          <DS.Tooltip content="About — version, shortcuts and diagnostics (F1)" side="bottom">
            <DS.IconButton size="sm" aria-label="About" active={aboutOpen} onClick={() => setAboutOpen(true)}><I.info /></DS.IconButton>
          </DS.Tooltip>
          {/* Exit: kümenin sonu, kendi ayracıyla; pencere kontrolleri (— □ ×) ondan sonra gelir */}
          <span aria-hidden="true" style={TB_SEP}></span>
          <span data-bo-exit="1" style={{ position: 'relative', display: 'inline-flex' }}>
            {exitOpen ? (
              <DS.IconButton size="sm" aria-label="Exit" aria-expanded="true" onClick={requestExit}
                style={{ background: 'var(--surface-overlay)', color: 'var(--text-primary)' }}><I.power /></DS.IconButton>
            ) : (
              <DS.Tooltip content="Exit — quit and end every process (Ctrl+Q)" side="bottom">
                <DS.IconButton size="sm" aria-label="Exit" onClick={requestExit}><I.power /></DS.IconButton>
              </DS.Tooltip>
            )}
            {exitOpen && (
              <ExitCard work={exitWork()} progress={exitProgress()} procs={exitProcesses()}
                inFlight={eng.buildingList().length}
                onCancel={() => setExitOpen(false)} onConfirm={confirmExit} />
            )}
          </span>
        </span>
      </DS.TitleBar>

      {/* ---- STICKY ŞERİT ---- */}
      <StickyStrip eng={eng} workspace={workspace} exitPending={!!closing} onSelect={select} onFilterFailed={() => { setSelected(null); setFilter(new Set(['failed'])); }} />

      {/* ---- GÖVDE: 2 kolon × 2 satır + splitters ---- */}
      <div ref={bodyRef} style={{ flex: 1, minHeight: 0, display: 'flex', alignItems: 'stretch' }}>
        {/* SOL KOLON */}
        <div style={{ width: layout.col + '%', minWidth: 0, display: 'flex', flexDirection: 'column' }}>
          {showGraph && (
            <>
              <div style={{ height: layout.left + '%', minHeight: 0, display: 'flex', flexDirection: 'column' }}>
                <PanelHead label="Dependency graph" right={workspace && !eng.discover && !eng.task && BO.PROJECTS.length ? (
                  <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>
                    {BO.PROJECTS.length} projects · {BO.EDGE_N} dependencies
                  </span>
                ) : null} />
                <GraphPanel eng={eng} selected={selected} onSelect={select} revealKey={revealKey} workspace={workspace}
                  filtering={filtering} match={matchFn} groups={groups} hovered={hovered} onHover={setHovered} />
              </div>
              <Splitter dir="h" onDrag={dragLeft} onEnd={endDrag} />
            </>
          )}
          <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
            <PanelHead label="Projects" right={workspace && !eng.discover && !eng.task ? (
              <DS.Input id="bo-proj-search" placeholder="Filter…" prefix={<I.search />} value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Escape') { setQuery(''); e.currentTarget.blur(); e.stopPropagation(); } }}
                style={{ width: 160, height: 20, fontSize: 'var(--text-xs)' }} />
            ) : null}>
              {workspace && !eng.discover && !eng.task && <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', textTransform: 'none', letterSpacing: 0 }}>build-order</span>}
              {filter.size > 0 && (
                <DS.Chip active title="Clear the active filter" label={FILTER_ORDER.filter((f) => filter.has(f)).map((f) => FILTER_LABELS[f]).join(' + ')} onRemove={() => setFilter(new Set())} style={{ height: 20, padding: '0 6px', fontSize: 'var(--text-2xs)' }} />
              )}
            </PanelHead>
            <ListPanel eng={eng} groups={groups} selected={selected} onSelect={select} onNote={note}
              onRowAction={doRowAction} onStopRun={() => { eng.stop(); bump(); }}
              onImportSettings={() => { setSettingsImport(true); setSettingsOpen(true); }}
              filter={filter} query={query} follow={follow} revealKey={revealKey} workspace={workspace} layerCount={layerCfg.length} onOpenSettings={() => setSettingsOpen(true)}
              hovered={hovered} onHover={setHovered} localEdits={(dirty[copy] || 0) > 0} />
          </div>
        </div>

        <Splitter dir="v" onDrag={dragCol} onEnd={endDrag} />

        {/* SAĞ KOLON */}
        <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
          <div style={{ height: layout.right + '%', minHeight: 0, display: 'flex', flexDirection: 'column' }}>
            {/* konsol başlığı (uygulama ConsoleHeader): anlatıda caps CONSOLE + N lines (+ compact); proje sayfasında
                Back · ad · rozetler, sağda Copy log + N lines. Statü glyph'i ve sözcüğü yok. */}
            <PanelHead label={selected ? '' : 'Console'} right={workspace ? (
              <>
                {selected && selSt.log && selSt.log.length > 0 && (
                  <CopyLogBtn getText={() => selSt.log.map((l) => l.text).join('\n')} />
                )}
                <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>
                  {(selected ? (selSt.log ? selSt.log.length : 0) : eng.narrative.length) + ' lines'}
                </span>
                {!selected && compactTag && (
                  <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>compact</span>
                )}
              </>
            ) : null}>
              {selected && (
                <>
                  <DS.Button size="sm" variant="ghost" icon={<I.back />} onClick={() => setSelected(null)} style={{ marginLeft: -6 }}>Back</DS.Button>
                  <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{selected}</span>
                  {selSt.depIssue && (
                    <DS.Tooltip content={'Dependency issue: ' + selSt.depIssue.map(BO.shortName).join(', ') + ' — last successful output referenced'} side="bottom">
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, color: 'var(--amber-text)', fontSize: 'var(--text-2xs)', whiteSpace: 'nowrap', cursor: 'default' }}>
                        <I.alertTri size={8} /> dependency issue
                      </span>
                    </DS.Tooltip>
                  )}
                  {eng._isCycle(selected) && (
                    <DS.Tooltip content="In a dependency cycle" side="bottom">
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, color: 'var(--amber-text)', fontSize: 'var(--text-2xs)', whiteSpace: 'nowrap', cursor: 'default' }}>
                        <I.alertTri size={8} /> dependency cycle
                      </span>
                    </DS.Tooltip>
                  )}
                </>
              )}
            </PanelHead>
            <ConsolePanel eng={eng} selected={selected} workspace={workspace} />
          </div>
          <Splitter dir="h" onDrag={dragRight} onEnd={endDrag} />
          <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
            <PanelHead label="Event stream" right={workspace ? (
              <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>{eng.stream.length} events</span>
            ) : null} />
            <StreamPanel eng={eng} selected={selected} onSelect={select} workspace={workspace} />
          </div>
        </div>
      </div>

      {/* ---- ACTION BAR ---- */}
      <div data-bo-actions="1" style={{
        flex: 'none', display: 'flex', alignItems: 'center', gap: 8, height: 42, padding: '0 10px',
        background: 'var(--surface)', borderTop: '1px solid var(--border)', position: 'relative', zIndex: 20,
      }}>
        {/* Sync işini gösterir: uçuştayken amber zemin + spinner (uygulama: "koşan iş butonu neredeyse amber") */}
        <DS.Button size="sm" variant="secondary" icon={syncing ? <BuildingSpin size={12} /> : <I.sync />}
          onClick={doSync} disabled={!syncing && !canGo} data-bo-hov={syncing ? 'on' : 'off'}
          style={syncing ? { flex: 'none', background: 'var(--amber-soft)', borderColor: 'var(--amber-border)', color: 'var(--amber-text)', cursor: 'default' } : { flex: 'none' }}>Sync</DS.Button>
        {/* bakım grubu: Clean + Optimize + Resolve cycles — chip ağırlığında tek kutu, ikon butonlar */}
        <span style={{
          display: 'inline-flex', alignItems: 'center', height: 24, flex: 'none', overflow: 'hidden',
          border: '1px solid var(--border)', borderRadius: 'var(--radius-xs)', background: 'var(--surface-raised)',
        }}>
          <DS.Tooltip content="Clean — remove every project's bin/ and obj/ and reset the build state; the next build compiles everything from scratch" side="top">
            <DS.IconButton size="sm" aria-label="Clean" active={taskKind === 'clean'} data-bo-hov={taskKind === 'clean' ? 'on' : 'off'}
              disabled={taskKind !== 'clean' && !canGo} onClick={doClean}
              style={{ width: 28, height: 22, borderRadius: 0, border: 'none' }}>
              {taskKind === 'clean' ? <BuildingSpin size={12} /> : <I.eraser />}
            </DS.IconButton>
          </DS.Tooltip>
          <span aria-hidden="true" style={{ width: 1, height: 14, background: 'var(--border)', flex: 'none' }}></span>
          <DS.Tooltip content="Optimize — restore missing NuGet packages, report references that restore cannot fix, clean stale obj leftovers and prune dead cache entries" side="top">
            <DS.IconButton size="sm" aria-label="Optimize" active={taskKind === 'optimize'} data-bo-hov={taskKind === 'optimize' ? 'on' : 'off'}
              disabled={taskKind !== 'optimize' && !canGo} onClick={doOptimize}
              style={{ width: 28, height: 22, borderRadius: 0, border: 'none' }}>
              {taskKind === 'optimize' ? <BuildingSpin size={12} /> : <I.gauge />}
            </DS.IconButton>
          </DS.Tooltip>
          <span aria-hidden="true" style={{ width: 1, height: 14, background: 'var(--border)', flex: 'none' }}></span>
          {/* Resolve cycles — döngü üyeleri tekrar eden turlarla: önce bayat referanslar, sonra yakınsayana dek */}
          {/* AccessibilityNames.ResolveCyclesTooltip: kirli upstream varsa ' · N upstream to build first' eki */}
          <DS.Tooltip side="top" content={c.cycle > 0
            ? 'Resolve cycles — build the ' + c.cycle + ' cycle projects in repeated rounds: stale references first, then rebuild until they converge'
              + (resolveUp > 0 ? ' · ' + resolveUp + ' upstream to build first' : '')
            : 'Resolve cycles — no dependency cycles detected'}>
            <DS.IconButton size="sm" aria-label="Resolve cycles" active={resolving} onClick={doResolve} data-bo-hov={resolving ? 'on' : 'off'}
              disabled={(!resolving && !canGo) || c.cycle === 0}
              style={{ width: 28, height: 22, borderRadius: 0, border: 'none' }}>
              {resolving ? <BuildingSpin size={12} /> : <I.unlink />}
            </DS.IconButton>
          </DS.Tooltip>
        </span>
        <span style={{ width: 1, alignSelf: 'stretch', margin: '10px 2px', background: 'var(--border-subtle)' }}></span>
        {/* sayaç çipleri (uygulama 1.20.0 durum seti): Σ · building · ✓ · ✗ · ⚠ (yalnız >0) — satırın görsel durumundan */}
        <DS.Tooltip content="All projects — clear filters" side="top">
          <DS.Chip icon={<I.sigma />} value={<span style={{ color: 'var(--text-primary)' }}>{sc.total}</span>} onClick={() => setFilter(new Set())} data-bo-chip="1" data-bo-hov="off" />
        </DS.Tooltip>
        <DS.Tooltip content="Building — filter" side="top">
          <DS.Chip icon={sc.building ? <BuildingSpin size={12} /> : <span style={{ width: 8, height: 8, borderRadius: '50%', background: 'var(--neutral-600)', display: 'inline-block' }}></span>}
            value={sc.building} active={filter.has('building')} onClick={() => toggleFilter('building')} data-bo-chip="1" data-bo-hov={filter.has('building') ? 'on' : 'off'}
            style={filter.has('building') ? { color: 'var(--amber-text)' } : undefined} />
        </DS.Tooltip>
        <DS.Tooltip content="Up to date — filter" side="top">
          <DS.Chip icon={<DS.StatusGlyph status="succeeded" size={12} />} value={sc.current} active={filter.has('succeeded')} onClick={() => toggleFilter('succeeded')} data-bo-chip="1" data-bo-hov={filter.has('succeeded') ? 'on' : 'off'}
            style={filter.has('succeeded') ? { color: 'var(--status-success-text)' } : undefined} />
        </DS.Tooltip>
        <DS.Tooltip content="Failed — filter" side="top">
          <DS.Chip icon={<DS.StatusGlyph status="failed" size={12} />} value={sc.broken} active={filter.has('failed')} onClick={() => toggleFilter('failed')} data-bo-chip="1" data-bo-hov={filter.has('failed') ? 'on' : 'off'}
            style={filter.has('failed') ? { color: 'var(--status-fail-text)' } : undefined} />
        </DS.Tooltip>
        {warnN > 0 && (
          <DS.Tooltip content="In a dependency cycle or waiting on a dependency — filter" side="top">
            <DS.Chip icon={<span style={{ display: 'inline-flex', color: 'var(--amber-text)' }}><I.alertTri size={12} /></span>}
              value={warnN} active={filter.has('warn')} onClick={() => toggleFilter('warn')} data-bo-chip="1" data-bo-hov={filter.has('warn') ? 'on' : 'off'}
              style={filter.has('warn') ? { color: 'var(--amber-text)' } : undefined} />
          </DS.Tooltip>
        )}

        <span style={{ flex: 1 }}></span>

        {/* workspace etiketi: repository kökünün klasör adı; tooltip aktif kök (worktree açıkken worktree yolu) */}
        {workspace && (
          <DS.Tooltip content={activeRoot} side="top">
            <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-dim)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis', maxWidth: 160, cursor: 'default', marginRight: 2 }}>{repoName}</span>
          </DS.Tooltip>
        )}
        {/* worktree anahtarı: 24px kare, iki klasör; worktree açıkken amber. Restart engine görünürken gizlenir (1240px sığma) */}
        {workspace && !engineDown && (
          <DS.Tooltip side="top" content={copy === 'worktree' ? 'Back to the repository — ' + repoRoot
            : !wtFolder ? 'Set up a worktree — a second working copy for another branch'
            : 'Switch to the worktree — ' + wtFolder}>
            <DS.Chip icon={<I.folders />} aria-label="Worktree" aria-pressed={copy === 'worktree'} active={copy === 'worktree'}
              disabled={!!wtFolder && !canGo} onClick={switchCopy} data-bo-chip="1" data-bo-hov={copy === 'worktree' ? 'on' : 'off'}
              style={{ width: 24, padding: 0, justifyContent: 'center' }} />
          </DS.Tooltip>
        )}
        <span style={{ position: 'relative' }} data-bo-chip="1">
          {(() => {
            const gitTip = gitOp ? gitOp + ' in progress — finish or abort it in git' : null;
            const chip = (
              <DS.Chip icon={<I.branch />} label="branch" chevron active={branchPop} disabled={!canGo || !!gitOp} data-bo-hov={branchPop ? 'on' : 'off'}
                value={<span style={{ display: 'inline-flex', alignItems: 'center' }}>{branchSel}
                  {gitOp && <span aria-hidden="true" style={{ width: 6, height: 6, borderRadius: '50%', background: 'var(--amber)', marginLeft: 6, flex: 'none' }}></span>}
                </span>}
                onClick={() => { setBranchPop(!branchPop); setBuildMenu(false); }} />
            );
            return gitTip ? <DS.Tooltip content={gitTip} side="top">{chip}</DS.Tooltip> : chip;
          })()}
          <BranchPopover open={branchPop} branches={BO.BRANCHES} current={activeBranch} onPick={pickBranch} />
          <BranchCard card={branchCard} worktreeSet={!!wtFolder} onDecide={decideBranch} />
        </span>
        {/* '<N> behind' — fetch sayıyı bildiği sürece görünür; tıklama = ff-only pull (SYNC pill'i, ardından Sync) */}
        {workspace && eng.behind > 0 && (
          <DS.Tooltip side="top" content={gitOp ? gitOp + ' in progress — finish or abort it in git' : 'Fetch found ' + eng.behind + (eng.behind === 1 ? ' new commit' : ' new commits') + ' on origin/' + branchSel + '. Click to fast-forward; nothing is merged or rewritten.'}>
            <DS.Chip icon={<I.pullDown />} disabled={!canGo || !!gitOp} onClick={doPull} data-bo-chip="1" data-bo-hov="off" aria-label={eng.behind + ' behind'}
              label={<span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>
                <span style={{ fontFamily: MONO, fontWeight: 400, fontVariantNumeric: 'tabular-nums' }}>{eng.behind}</span>behind
              </span>} />
          </DS.Tooltip>
        )}
        {/* Debug | Release bir Sync başlatır — yalnız Sync başlayabiliyorken açık */}
        <DS.Segment size="sm" options={['Debug', 'Release']} value={cfg} onChange={pickCfg}
          style={canGo ? undefined : { opacity: 0.45, pointerEvents: 'none' }} />
        <DS.Chip label="perf" value={perf} onClick={cyclePerf} disabled={!workspace} data-bo-chip="1" data-bo-hov="off" />
        <span style={{ width: 1, alignSelf: 'stretch', margin: '10px 2px', background: 'var(--border-subtle)' }}></span>
        {/* Restart engine: yalnız motor öldüğünde (kırmızı) ya da sustuğunda (amber); etiketsiz, anlamı tooltip'te */}
        {workspace && engineDown && (
          <DS.Tooltip content="Restart engine — start a fresh build engine; the current one stopped or is not answering" side="top">
            <DS.Button size="sm" variant="secondary" aria-label="Restart engine" onClick={restartEngine}
              icon={<span style={{ display: 'inline-flex', color: eng.engine === 'dead' ? 'var(--status-fail-text)' : 'var(--amber-text)' }}><I.rot /></span>}
              style={{ flex: 'none', width: 24, padding: 0 }} />
          </DS.Tooltip>
        )}
        {runLocked ? (
          <DS.Button variant="danger" size="md" icon={<I.stop />} disabled={eng.stopStage === 'terminating'}
            aria-label={eng.stopStage ? stopLabel : 'Stop build'}
            onClick={() => { eng.requestStop(); bump(); }} style={{ flex: 'none' }}>{stopLabel}</DS.Button>
        ) : (
          <span style={{ position: 'relative', display: 'inline-flex', flex: 'none' }} data-bo-chip="1">
            <DS.Button variant="primary" size="md" icon={<I.play />} onClick={doBuild} disabled={!canGo}
              style={{ flex: 'none', borderTopRightRadius: 0, borderBottomRightRadius: 0 }}>Build</DS.Button>
            <DS.Button variant="primary" size="md" aria-label="Build options" disabled={!canGo}
              onClick={() => { setBuildMenu(!buildMenu); setBranchPop(false); }}
              style={{ flex: 'none', borderTopLeftRadius: 0, borderBottomLeftRadius: 0, padding: '0 5px', borderLeft: '1px solid var(--amber-dim)' }}>
              <I.chevUp />
            </DS.Button>
            <Popover open={buildMenu} width={276}>
              <BuildMenuItem icon={<I.play />} title="Build" desc="Only stale projects" kbd="F5"
                onPick={() => { setBuildMenu(false); doBuild(); }} />
              <BuildMenuItem icon={<I.rebuild />} title="Rebuild" desc={'All ' + BO.PROJECTS.length + ' projects — cache ignored'} kbd="F6"
                onPick={() => { setBuildMenu(false); doRebuild(); }} />
              <BuildMenuItem icon={<I.brush />} title="Clean" desc="Remove build outputs — next build is full" kbd="F7"
                tip="Clean — msbuild /t:Clean on every project; caches are untouched"
                onPick={() => { setBuildMenu(false); doCleanSln(); }} />
            </Popover>
          </span>
        )}
      </div>

      {/* ---- AYARLAR ---- */}
      <SettingsDialog open={settingsOpen} cfg={layerCfg} extCfg={extCfg} pull={pullExt} gen={general} root={repoRoot} setup={!workspace} importOnOpen={settingsImport}
        wt={wtFolder} linked={wtLinked} focusWorktree={settingsWt}
        onClose={() => { setSettingsOpen(false); setSettingsImport(false); setSettingsWt(false); pendingWtBranchRef.current = null; }} onSave={saveSettings} />

      {/* ---- ABOUT ---- */}
      <AboutDialog open={aboutOpen} root={repoRoot} onOpenNotes={() => { setAboutOpen(false); setNotesOpen(true); }}
        onClose={() => setAboutOpen(false)} logoSrc={logoUrl(logoBase, 'mark')} companyLogoSrc={logoUrl(logoBase, 'company')} />

      {/* ---- WHAT'S NEW ---- */}
      <NotesDialog open={notesOpen}
        onSeen={() => { setUnseenNotes(false); try { localStorage.setItem(NOTES_SEEN_KEY, ABOUT_VERSION); } catch (e) { /* yok */ } }}
        onClose={() => setNotesOpen(false)} />

      {/* ---- GÜNCELLEME: restart ekranı ---- */}
      <UpdateRestart run={updRun} from={ABOUT_VERSION} to={UPDATE_FEED.v} logoSrc={logoUrl(logoBase, 'mark')} />

      {/* ---- EXIT: kapanış ekranı · pencere gitti (demo) ---- */}
      <ExitClosing run={closing} logoSrc={logoUrl(logoBase, 'mark')} onDone={() => { setClosing(null); setGone('exit'); }} />
      <WindowGone mode={gone} buildLive={eng.phase === 'marking' || eng.phase === 'running' || eng.phase === 'stopping'} onBack={() => { const m = gone; setGone(null); if (m === 'exit') applyScene(Number(scene) || 1); if (m === 'update') { setUpdReady(false); rebootApp(); } }} />
    </div>
  );
}

window.BuildApp = BuildApp;
