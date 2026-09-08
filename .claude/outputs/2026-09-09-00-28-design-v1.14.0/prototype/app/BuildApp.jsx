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
    .bo-rot { animation: bo-rot 1.4s linear infinite; transform-origin: center; transform-box: fill-box; }
    .bo-shake { animation: bo-shake .36s var(--ease-standard) 1; }
    .bo-glow-once { animation: bo-glow-once 1.1s var(--ease-out) 1; }
    .bo-edge-flow { stroke-dasharray: 4 7; animation: bo-dash .9s linear infinite; }
    .bo-pop-in { animation: bo-pop-in .14s var(--ease-out) both; }
    .bo-tilt-in { animation: bo-tilt-in .34s var(--ease-out) both; transform-origin: 50% 100%; }
    .bo-breath { animation: bo-breath 3.8s var(--ease-in-out) infinite; }
  }
  @media (prefers-reduced-motion: reduce) {
    .bo-edge-flow { stroke-dasharray: 4 7; }
  }
  @keyframes bo-gbeads { to { stroke-dashoffset: calc(var(--p) * -1); } }
  .bo-gbeads { animation: bo-gbeads 4200ms linear infinite; }
  @keyframes bo-reveal { from { opacity: 0; transform: translateY(-5px); } to { opacity: 1; transform: none; } }
  @keyframes bo-blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.1; } }
  /* imleç renk sıçraması (1.12.1): renk YALNIZ kırpmanın dibinde değişir (-0.55s faz), her yanışta konsol satır paletinden
     sıradaki renk — cmd · info · success · warn · error · dim (NARR_COLORS ile aynı). Geçiş yok, kesikli ritim. */
  @keyframes bo-hop { 0%, 16.66% { background: var(--text-primary); } 16.67%, 33.33% { background: var(--text-secondary); } 33.34%, 50% { background: var(--status-success-text); } 50.01%, 66.66% { background: var(--amber-text); } 66.67%, 83.33% { background: var(--status-fail-text); } 83.34%, 100% { background: var(--text-faint); } }
  @keyframes bo-rot { to { transform: rotate(360deg); } }
  @keyframes bo-shake { 10%, 90% { transform: translateX(-2px); } 25%, 75% { transform: translateX(3px); } 50% { transform: translateX(-3px); } }
  @keyframes bo-glow-once { 0% { background: var(--status-success-soft); } 100% { background: transparent; } }
  @keyframes bo-dash { to { stroke-dashoffset: -22; } }
  @keyframes bo-pop-in { from { opacity: 0; transform: translateY(4px) scale(.985); } to { opacity: 1; transform: none; } }
  @keyframes bo-tilt-in { from { opacity: 0; transform: perspective(900px) rotateX(7deg) translateY(14px); } to { opacity: 1; transform: perspective(900px) rotateX(0) translateY(0); } }
  @keyframes bo-breath { 0%, 100% { opacity: 0; } 50% { opacity: 0.32; } }
  /* bitiş koreografisi — neon tutuşma (floresan lamba gibi düzensiz titreyerek yanar) */
  @keyframes bo-neon { 0% { opacity: .12; } 8% { opacity: 1; } 16% { opacity: .2; } 24% { opacity: .9; } 32% { opacity: .14; } 46% { opacity: 1; } 56% { opacity: .5; } 70% { opacity: 1; } 100% { opacity: 1; } }
  .bo-scroll { scrollbar-width: thin; scrollbar-color: var(--neutral-700) transparent; }
  .bo-scroll::-webkit-scrollbar { width: 10px; height: 10px; }
  .bo-scroll::-webkit-scrollbar-track { background: transparent; }
  .bo-scroll::-webkit-scrollbar-thumb { background: var(--neutral-700); border: 3px solid transparent; background-clip: padding-box; border-radius: 5px; }
  /* yatayda görünmez scroll — uzun yollar fare tekerleğiyle sağa kaydırılır */
  .bo-xscroll { overflow-x: auto; overflow-y: hidden; scrollbar-width: none; -ms-overflow-style: none; overscroll-behavior-x: contain; }
  .bo-xscroll::-webkit-scrollbar { height: 0; width: 0; }
  `;
  document.head.appendChild(s);
})();

/* ---------- ikonlar (Lucide geometrisi, currentColor) ---------- */
const I = {
  branch: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="6" x2="6" y1="3" y2="15"/><circle cx="18" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M18 9a9 9 0 0 1-9 9"/></svg>,
  tree: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M8 3v4a2 2 0 0 0 2 2h8"/><path d="M8 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h2"/><path d="M8 13h6a2 2 0 0 1 2 2v6"/></svg>,
  play: () => <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor" stroke="none"><polygon points="6 3 20 12 6 21 6 3"/></svg>,
  stop: () => <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor" stroke="none"><rect x="5" y="5" width="14" height="14" rx="1"/></svg>,
  playRow: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" stroke="none"><polygon points="6 3 20 12 6 21 6 3"/></svg>,
  stopRow: () => <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor" stroke="none"><rect x="5" y="5" width="14" height="14" rx="1"/></svg>,
  trash: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M3 6h18"/><path d="M19 6l-.8 14a2 2 0 0 1-2 2H7.8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6"/><path d="M14 11v6"/><path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2"/></svg>,
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
  gear: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/></svg>,
  sparkle: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M12 2.8l2.1 5.8a2 2 0 0 0 1.3 1.3l5.8 2.1-5.8 2.1a2 2 0 0 0-1.3 1.3L12 21.2l-2.1-5.8a2 2 0 0 0-1.3-1.3L2.8 12l5.8-2.1a2 2 0 0 0 1.3-1.3z"/></svg>,
  info: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 11v5"/><path d="M12 7.6h.01"/></svg>,
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
  layQuad: () => <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.1"><rect x="1.7" y="2.7" width="5.5" height="4.5" rx="1"/><rect x="8.8" y="2.7" width="5.5" height="4.5" rx="1"/><rect x="1.7" y="8.8" width="5.5" height="4.5" rx="1"/><rect x="8.8" y="8.8" width="5.5" height="4.5" rx="1"/></svg>,
  layList: () => <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.1"><rect x="1.7" y="2.7" width="5.5" height="10.6" rx="1"/><rect x="8.8" y="2.7" width="5.5" height="4.5" rx="1"/><rect x="8.8" y="8.8" width="5.5" height="4.5" rx="1"/></svg>,
  layFocus: () => <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.1"><rect x="1.7" y="2.7" width="5.5" height="10.6" rx="1"/><rect x="8.8" y="2.7" width="5.5" height="7.1" rx="1"/><rect x="8.8" y="11.4" width="5.5" height="1.9" rx="0.95"/></svg>,
};

const MONO = 'var(--font-mono)';
/* Logo yolları LİTERAL tutulur (concat değil) — standalone paketleyicisi ancak böyle gömebilir. */
const LOGO = { mark: 'assets/app-mark.svg', company: 'assets/delta-logo-dark.svg' };
const logoUrl = (base, key) => (!base || base === 'assets' ? LOGO[key] : base + '/' + LOGO[key].slice(7));const fmtElapsed = (ms) => {
  const s = Math.max(0, Math.floor(ms / 1000));
  if (s < 60) return s + 's';
  return Math.floor(s / 60) + 'm ' + String(s % 60).padStart(2, '0') + 's';
};

/* ---------- imleç + daktilo satırı ---------- */
/* Cursor: 7×13 blok, 1.1s kırpma; rengi .bo-cursor'daki bo-hop animasyonu sürür (satır paleti, kırpma dibinde sıçrar).
   Reduced-motion'da animasyon yok → currentColor. Konsol prompt'u ve stream canlı satırı aynı bileşeni kullanır. */
function Cursor({ style }) {
  return <span className="bo-cursor" style={{ display: 'inline-block', width: 7, height: 13, background: 'currentColor', verticalAlign: -2, flex: 'none', ...style }}></span>;
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
      <button type="button" onClick={onClick} onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
        className={REDUCED ? undefined : 'bo-pop-in'}
        style={{
          pointerEvents: 'auto', display: 'inline-flex', alignItems: 'center', gap: 5, height: 22, padding: '0 9px 0 7px',
          background: hover ? 'var(--surface-raised)' : 'var(--surface-overlay)',
          border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-md)',
          color: hover ? 'var(--text-primary)' : 'var(--text-secondary)',
          fontFamily: MONO, fontSize: 'var(--text-2xs)', lineHeight: 1, cursor: 'pointer', userSelect: 'none',
          boxShadow: 'var(--elevation-popover)',
          transition: 'background var(--duration-fast) var(--ease-standard), color var(--duration-fast) var(--ease-standard)',
        }}>
        <I.down /> latest
      </button>
    </span>
  );
}

/* ---------- konsol satırı: yalnız metin — ikon kolonu yok, satırlar imleçle aynı sol hizada ---------- */
const NARR_COLORS = {
  info: 'var(--text-secondary)', success: 'var(--status-success-text)', warn: 'var(--amber-text)',
  error: 'var(--status-fail-text)', cmd: 'var(--text-primary)', dim: 'var(--text-faint)',
};
function NarrLine({ type = 'info', cursor, children, style }) {
  return (
    <div style={{
      display: 'flex', gap: cursor ? 8 : 0, alignItems: 'baseline',
      fontFamily: MONO, fontSize: 'var(--text-xs)', lineHeight: 'var(--leading-mono)',
      color: NARR_COLORS[type] || NARR_COLORS.info,
      whiteSpace: 'pre-wrap', wordBreak: 'break-word', fontVariantNumeric: 'tabular-nums',
      ...style,
    }}>
      {cursor && <Cursor />}
      <span style={{ minWidth: 0 }}>{children}</span>
    </div>
  );
}

/* ---------- görünüm değişiminde konsol içeriği tek parça "tilt in" ile oturur:
   alt kenardan menteşeli — perspective 900px, rotateX 7° + 14px aşağıdan, 340ms ease-out.
   Canlı gelen satırlar anında basılır (animasyon yalnız mount'ta bir kez). ---------- */
function RevealLog({ lines }) {
  const body = lines.map((l, i) => <NarrLine key={l.id != null ? l.id : i} type={l.type}>{l.text}</NarrLine>);
  return REDUCED ? <>{body}</> : <div className="bo-tilt-in">{body}</div>;
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
  discovered: { bd: 'var(--border-strong)', bg: 'var(--surface-raised)' },
  fresh: { bd: 'var(--border-strong)', bg: 'var(--surface-raised)', dash: true }, // başlangıç modu: kesikli border (1.12.0'da düz+soluk denendi, kullanıcı kesikliyi istedi)
  cyc: { bd: 'var(--border-strong)', bg: 'var(--surface-raised)' },      // cycle üyesi, bu işlemde derlenmiyor: gri border + AMBER küp
  cycskip: { bd: 'var(--status-skipped-border)', bg: 'var(--status-skipped-soft)' },
  marked: { bd: 'var(--amber)', bg: 'var(--amber-soft)' },
  queued: { bd: 'var(--amber)', bg: 'var(--amber-soft)' },
  building: { bd: 'var(--amber)', bg: 'var(--amber-soft)' },
  succeeded: { bd: 'var(--status-success)', bg: 'var(--status-success-soft)' },
  failed: { bd: 'var(--status-fail)', bg: 'var(--status-fail-soft)' },
  skipped: { bd: 'var(--status-skipped-border)', bg: 'var(--status-skipped-soft)' },
};
/* tek statü kanalı: node border'ı ve içindeki küp AYNI rengi taşır. Tek istisna (1.12.0): bu işlemde
   derlenmeyen cycle üyesinde küp AMBER — satırdaki uyarı üçgeninin grafik vekili; işlem nötr anında
   yanar, koşu ve finalde kalır, sonraki Sync/işlemle düşer. Resolve'da üye derlenir → normal sonuç rengi. */
const GCORE = {
  discovered: 'var(--text-faint)', fresh: 'var(--text-faint)', cyc: 'var(--amber-text)', cycskip: 'var(--amber-text)',
  marked: 'var(--amber-text)', queued: 'var(--amber-text)', building: 'var(--amber-text)',
  succeeded: 'var(--status-success-text)', failed: 'var(--status-fail-text)', skipped: 'var(--status-skipped-text)',
};
/* görsel durum = tek kanal: başlangıç (fresh) · işaretli (marked) · statü.
   Koreografinin nötr anında (markStep 'neutral') kapsam da DÜZ GRİ çizilir — amber dalgayla gelir. */
function vstate(eng, name) {
  const st = eng.p[name];
  const cyc = eng._isCycle(name);
  if (st.status === 'discovered') {
    if (eng.isMarked(name) && eng.markStep && eng.markStep !== 'neutral') return 'marked';
    if (st.fresh) return 'fresh';
    return cyc ? 'cyc' : 'discovered'; // nötr anda cycle küpü amber'a döner (kapsamdaysa dalgada tam amber olur)
  }
  if (st.status === 'skipped' && cyc) return 'cycskip';
  return st.status;
}
const G_HOLD = 2400, G_FADE = 700; // biten node parlak kalma / sönme
/* reveal sırası = derleme sırası. Harici projeler ayarlarla değiştiği için haritalar
   yeniden kurulabilir: BO.setExternal() sonrası syncGraphMaps() çağrılır. */
const G_ORDER_IX = {}, G_DEPS = {}, G_DEPENDENTS = {};
function syncGraphMaps() {
  [G_ORDER_IX, G_DEPS, G_DEPENDENTS].forEach((m) => Object.keys(m).forEach((k) => { delete m[k]; }));
  BO.ORDER.forEach((n, i) => { G_ORDER_IX[n] = i; });
  BO.PROJECTS.forEach((p) => { G_DEPS[p.name] = p.deps || []; (p.deps || []).forEach((d) => { (G_DEPENDENTS[d] = G_DEPENDENTS[d] || []).push(p.name); }); });
}
syncGraphMaps();

/* katman bantları: panele tam sığan pitch aranır; eksik son satır ortalanır */
function graphLayout(W, H) {
  // bantlar derlenme sırasına göre: layer 0 en üstte; bant içinde build-order (soldan sağa)
  const byLayer = {}, bands = [];
  BO.ORDER.forEach((n) => {
    const L = BO.byName[n].layer;
    if (!byLayer[L]) { byLayer[L] = []; bands.push(L); }
    byLayer[L].push(n);
  });
  const groups = bands.map((L) => byLayer[L]); // BO.ORDER katman sıralı → bantlar da sıralı (harici -1 en üstte)
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
  return { pos, size: Math.max(8, Math.min(24, pitch * 0.6)) };
}

/* statü filtresi eşleşmesi — liste ve graf aynı kuralı paylaşır. filter = Set (çoklu, VEYA) */
function filterMatch(eng, name, filter) {
  if (!filter || !filter.size) return true;
  const st = eng.p[name];
  let hit = false;
  filter.forEach((f) => {
    if (hit) return;
    if (f === 'warn') hit = !!st.depIssue || eng._isCycle(name);
    else if (f === 'building') hit = st.status === 'building' || st.status === 'queued';
    else hit = st.status === f;
  });
  return hit;
}
function GraphPanel({ eng, selected, onSelect, revealKey, workspace, filter }) {
  const boxRef = React.useRef(null);
  const [dim, setDim] = React.useState({ w: 0, h: 0 }); // 0 = henüz ölçülmedi (uydurma ilk boyutla çizim YOK)
  const [view, setView] = React.useState({ z: 1, x: 0, y: 0, m: 'none' });
  const [hover, setHover] = React.useState(null);
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
  const W = Math.max(240, dim.w), H = Math.max(160, dim.h), OX = 12, OY = 12;
  // alt sağdaki mono ipucu satırı için 18px rezerv (node'lara değmesin)
  /* imza proje kümesini de içerir: harici projeler değişince yerleşim yeniden kurulur
     (yoksa pos yeni adları tanımıyor ve node çizimi patlıyordu). */
  const projSig = BO.ORDER.length + ':' + BO.ORDER[0];
  const lay = React.useMemo(() => graphLayout(W - 24, H - 24 - 18), [W, H, projSig]);
  const pos = lay.pos, size = lay.size;

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
    setHover(null);
    if (selected && !finale && pos[selected]) {
      const set = [selected].concat(G_DEPS[selected] || [], G_DEPENDENTS[selected] || []);
      let x0 = 1e9, y0 = 1e9, x1 = -1e9, y1 = -1e9;
      set.forEach((n) => { const P = pos[n]; if (!P) return; x0 = Math.min(x0, P.x); x1 = Math.max(x1, P.x); y0 = Math.min(y0, P.y); y1 = Math.max(y1, P.y); });
      const pad = size * 3 + 48;
      const z = Math.max(0.7, Math.min(2.6, Math.min(W / ((x1 - x0) + pad), H / ((y1 - y0) + pad))));
      setView({ z, x: W / 2 - ((x0 + x1) / 2 + OX) * z, y: H / 2 - ((y0 + y1) / 2 + OY) * z, m: 'glide' });
    } else setView({ z: 1, x: 0, y: 0, m: 'glide' });
  }, [selected, lay, workspace, filter, finale]);

  if (!workspace) {
    return (
      <div style={{ flex: 1, minHeight: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--surface-base)' }}>
        <div style={{ border: '1px dashed var(--border)', borderRadius: 'var(--radius-md)', padding: '18px 26px', color: 'var(--text-faint)', fontSize: 'var(--text-xs)' }}>
          Graph appears after Sync
        </div>
      </div>
    );
  }

  // koşu başında bitiş kayıtlarını sıfırla; building→bitti anını işaretle (simT)
  if (eng.runCount !== marks.current.run) marks.current = { st: {}, done: {}, run: eng.runCount };
  BO.PROJECTS.forEach((p) => {
    const s = eng.p[p.name].status, ps = marks.current.st[p.name];
    if (s !== ps) { if (ps === 'building') marks.current.done[p.name] = eng.simT; marks.current.st[p.name] = s; }
  });

  const running = eng.phase === 'running';
  const focus = new Set();
  if (selected && !finale) { focus.add(selected); (G_DEPS[selected] || []).forEach((d) => focus.add(d)); (G_DEPENDENTS[selected] || []).forEach((d) => focus.add(d)); }

  // beads geometrisi: node dışında 2.8px yörünge, noktalar çevreye tam bölünür
  const pad6 = 6, svgS = size + pad6 * 2, ri = pad6 - 2.8;
  const rw = svgS - ri * 2, rx = Math.min(rw / 2, 6.8);
  const per = 4 * rw - 8 * rx + 2 * Math.PI * rx;
  const step = per / Math.max(8, Math.round(per / 3.4));
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
          const tone = GTONE[vs] || GTONE.discovered;
          const core = GCORE[vs] || GCORE.discovered;
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
          else if (filter && filter.size) op = filterMatch(eng, p.name, filter) ? 1 : 0.1; // filtre = graf üzerinde de vurgu
          else if (eng.phase === 'marking') { // 4a: dalga → sarı-gri an → örtüşen veda (griler 1120ms; sarılar 440ms, ~120ms önce biter — algıda eşzamanlı)
            const ms = eng.markStep || 'neutral';
            const late = ms === 'settle' || ms === 'wait' || ms === 'wait2';
            if (eng.isMarked(p.name)) { if (late) { op = 0.45; glide = 'opacity 440ms var(--ease-in-out)'; } }
            else if (late || ms === 'dimenv') { op = 0.18; glide = 'opacity 1120ms var(--ease-in-out)'; }
          }
          else if (running) {
            if (live) op = 1;
            else if (s === 'queued' || s === 'discovered') op = 0.13;
            else { op = 0.2; if (marks.current.done[p.name] != null) glide = 'opacity ' + G_FADE + 'ms var(--ease-standard) ' + G_HOLD + 'ms'; }
          }
          const hov = hover === p.name;
          if (hov) op = 1;
          const P = pos[p.name];
          if (!P) return null; // yerleşim henüz bu adı tanımıyor (proje kümesi değişti)
          return (
            <div key={p.name} data-n={p.name}
              className={REDUCED ? undefined : 'bo-reveal'}
              onClick={(e) => { e.stopPropagation(); onSelect(selected === p.name ? null : p.name); }}
              onMouseEnter={() => setHover(p.name)} onMouseLeave={() => setHover(null)}
              style={{ position: 'absolute', left: OX + P.x, top: OY + P.y, width: 0, height: 0, cursor: 'pointer', zIndex: hov ? 5 : 1,
                animationDelay: REDUCED ? undefined : Math.min((G_ORDER_IX[p.name] || 0) * 9, 520) + 'ms' }}>
              <div style={{
                position: 'absolute', left: -size / 2, top: -size / 2, width: size, height: size, boxSizing: 'border-box',
                borderRadius: 'var(--radius-sm)', background: tone.bg,
                border: (hov || (selected === p.name && !finale) ? 2 : 1.5) + 'px ' + (tone.dash ? 'dashed ' : 'solid ') + tone.bd,
                opacity: op, transform: hov ? 'scale(1.7)' : 'scale(1)',
                animation: neonAnim || undefined,
                outline: selected === p.name && !finale ? '2px solid var(--focus-ring)' : 'none', outlineOffset: 2,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
                transition: glide + ', transform 120ms var(--ease-out), background ' + (eng.markStep === 'wave' && eng.isMarked(p.name) ? '200ms var(--ease-standard) ' + ((eng.markOrder[p.name] || 0) * eng.markStagger) + 'ms' : '380ms var(--ease-standard)') + ', border-color ' + (eng.markStep === 'wave' && eng.isMarked(p.name) ? '200ms var(--ease-standard) ' + ((eng.markOrder[p.name] || 0) * eng.markStagger) + 'ms' : '380ms var(--ease-standard)'),
              }}>
                <svg viewBox="0 0 24 24" fill="none" stroke={core} strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round"
                  style={{ width: size * 0.52, height: size * 0.52, display: 'block',
                    transition: 'stroke ' + (eng.markStep === 'wave' && eng.isMarked(p.name) ? '200ms var(--ease-standard) ' + ((eng.markOrder[p.name] || 0) * eng.markStagger) + 'ms' : '380ms var(--ease-standard)') }} aria-hidden="true">
                  <path d="M21 8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16Z" />
                  <path d="m3.3 7 8.7 5 8.7-5" /><path d="M12 22V12" />
                </svg>
              </div>
              {!REDUCED && (
                <svg viewBox={'0 0 ' + svgS + ' ' + svgS} style={{
                  position: 'absolute', left: -svgS / 2, top: -svgS / 2, width: svgS, height: svgS,
                  overflow: 'visible', pointerEvents: 'none', opacity: live ? 1 : 0,
                  transform: hov ? 'scale(1.7)' : 'scale(1)',
                  transition: 'opacity ' + (live ? 420 : 640) + 'ms var(--ease-out), transform 120ms var(--ease-out)',
                }} aria-hidden="true">
                  <rect x={ri} y={ri} width={rw} height={rw} rx={rx} fill="none" stroke="var(--amber-text)" strokeWidth={1} strokeLinecap="round"
                    className={live || justDone ? 'bo-gbeads' : undefined} style={runStyle} />
                </svg>
              )}
            </div>
          );
        })}
      </div>}
      {hover && pos[hover] && (
        <div style={{ position: 'absolute', zIndex: 20, pointerEvents: 'none',
          left: Math.max(6, Math.min(W - 6, (OX + pos[hover].x) * view.z + view.x)),
          top: (OY + pos[hover].y) * view.z + view.y - size * 0.9 * view.z - 8,
          transform: 'translate(-50%,-100%)', padding: '4px 8px', background: 'var(--surface-overlay)',
          border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-md)', boxShadow: 'var(--elevation-popover)',
          whiteSpace: 'nowrap', fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--text-primary)' }}>{hover}</div>
      )}
      {selected && !finale && pos[selected] && (() => {
        const half = selected.length * 3.1 + 8;
        return (
          <div style={{ position: 'absolute', zIndex: 12, pointerEvents: 'none', transform: 'translateX(-50%)',
            left: Math.max(half + 6, Math.min(W - half - 6, (OX + pos[selected].x) * view.z + view.x)),
            top: Math.max(6, Math.min(H - 26, (OY + pos[selected].y) * view.z + view.y + size * 0.95 * view.z + 6)),
            padding: '2px 6px', background: 'var(--surface-overlay)', border: '1px solid var(--amber-border)',
            borderRadius: 'var(--radius-sm)', whiteSpace: 'nowrap', fontFamily: MONO, fontSize: 10, color: 'var(--amber-text)' }}>{selected}</div>
        );
      })()}
      <div style={{ position: 'absolute', right: 10, bottom: 6, pointerEvents: 'none', fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>
        {selected ? 'click again to release' : 'scroll = zoom · drag = pan'}
      </div>
    </div>
  );
}
/* ================= PROJE LİSTESİ ================= */
const FILTER_LABELS = { building: 'Building', succeeded: 'Succeeded', failed: 'Failed', skipped: 'Skipped', warn: 'Warnings' };
const EN_STATUS = { discovered: 'Not built in this run', queued: 'Queued', building: 'Building', succeeded: 'Succeeded', failed: 'Failed', skipped: 'Skipped' };
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
function Row({ p, eng, selected, onSelect, onNote, onAction, onStop, busy, isTarget, revealIndex, menuOpen, setMenu }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [hover, setHover] = React.useState(false);
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
  const STRIP = {
    fresh: 'var(--status-skipped-border)', discovered: 'var(--status-skipped-border)', cyc: 'var(--status-skipped-border)', cycskip: 'var(--status-skipped-border)', marked: 'var(--amber)',
    queued: 'var(--amber)', building: 'var(--amber)',
    succeeded: 'var(--status-success)', failed: 'var(--status-fail)', skipped: 'var(--status-skipped-border)',
  };
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
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
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
        width: isSel ? 3 : 2, alignSelf: 'stretch', flex: 'none', margin: '1px 0',
        background: STRIP[vs], opacity: 1,
        transition: 'width var(--duration-instant) var(--ease-standard), opacity 380ms var(--ease-standard), background ' + (mstep === 'wave' && eng.isMarked(p.name) ? '200ms var(--ease-standard) ' + ((eng.markOrder[p.name] || 0) * eng.markStagger) + 'ms' : '180ms var(--ease-standard)'),
      }}></span>
      <span style={{
        display: 'flex', alignItems: 'center', gap: 8, minWidth: 0, flex: 1, paddingLeft: 8,
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
      {/* sağ blok: hover'da aksiyonlar, değilse commit mini metni */}
      <span style={{ display: 'flex', alignItems: 'center', gap: 2, flex: 'none', minWidth: 118, justifyContent: 'flex-end' }}>
        {hover || isTarget || menuOpen ? (
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
            <DS.IconButton size="sm" title="Open in Visual Studio" onClick={(e) => { e.stopPropagation(); onNote(p.name + ' opened in Visual Studio'); }}><I.vs /></DS.IconButton>
          </span>
        ) : st.will === 'dirty' ? (
          <span style={{ fontFamily: MONO, fontSize: 10.5, color: 'var(--text-secondary)', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>
            {st.curSha} → {eng.targetSha}
          </span>
        ) : st.will === 'clean' ? (
          <span style={{ fontFamily: MONO, fontSize: 10.5, color: 'var(--text-faint)', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>
            {st.curSha}
          </span>
        ) : null}
      </span>
      <Glyph status={st.status} size={14} />
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

function ListPanel({ eng, groups, selected, onSelect, onNote, onRowAction, onStopRun, filter, query, follow, revealKey, workspace, layerCount, onOpenSettings, onImportSettings }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const boxRef = React.useRef(null);
  const rowRefs = React.useRef({});
  const lastScrollT = React.useRef(0);
  const [menu, setMenu] = React.useState(null);
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
              out.push(
                <div key={'h' + gi} style={{
                  position: 'sticky', top: stick * 24, zIndex: 3, display: 'flex', alignItems: 'center', gap: 8,
                  height: 24, boxSizing: 'border-box', padding: '0 10px', background: 'var(--surface)', borderBottom: '1px solid var(--border-subtle)',
                  fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)',
                  textTransform: 'uppercase', color: 'var(--text-faint)', userSelect: 'none',
                }}>
                  {g.name}
                  <span style={{ fontFamily: MONO, textTransform: 'none', letterSpacing: 0 }}>{rows.length}</span>
                </div>
              );
              stick++;
            }
            rows.forEach((p) => {
              out.push(
                <div key={p.name} data-row-wrap="1" ref={(el) => { rowRefs.current[p.name] = el; }}>
                  <Row p={p} eng={eng} selected={selected} onSelect={onSelect} onNote={onNote}
                    onAction={onRowAction} onStop={onStopRun} busy={eng.busy()} isTarget={eng.phase === 'running' && eng.scopeName === p.name && !!eng.scope}
                    menuOpen={!!menu && menu.name === p.name} setMenu={setMenu}
                    revealIndex={revealIndex++} />
                </div>
              );
            });
          });
          return out;
        })()}
        {(filter || q) && !BO.PROJECTS.some((p) => match(p.name)) && (
          <div style={{ padding: 20, fontSize: 'var(--text-xs)', color: 'var(--text-dim)' }}>{q ? 'No projects match “' + query.trim() + '”.' : 'No projects match this filter.'}</div>
        )}
      </div>
      {menu && <RowMenu pos={menu} busy={eng.busy()} onPick={onRowAction} onClose={() => setMenu(null)} />}
    </div>
  );
}

/* ================= KONSOL ================= */
function ConsolePanel({ eng, selected, workspace }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const boxRef = React.useRef(null);
  const stick = React.useRef(true);

  const sel = selected ? eng.p[selected] : null;
  const selBuilding = sel && sel.status === 'building';
  const lines = sel
    ? (sel.log && sel.log.length ? sel.log : null)
    : eng.narrative.slice(-200);

  /* "⌄ latest" pill — kullanıcı dipten uzaklaşınca (altta içerik varken) görünür.
     Koşu bitmiş de olsa yukarı kayınca çıkar — klasik dip afordansı. */
  const [away, setAway] = React.useState(false);
  const jumping = React.useRef(false); // programatik smooth-scroll penceresi
  React.useEffect(() => { stick.current = true; setAway(false); }, [selected]); // konsol↔proje log geçişinde dibe sabitle

  React.useEffect(() => {
    const c = boxRef.current;
    if (c && stick.current && !jumping.current) { c.scrollTop = c.scrollHeight; if (away) setAway(false); }
  });

  const onScroll = () => {
    const c = boxRef.current;
    if (!c || jumping.current) return; // smooth-scroll sürerken pozisyonu okuma
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

  let body;
  if (!workspace) {
    body = (
      <div style={{ color: 'var(--text-faint)', display: 'flex', alignItems: 'center', gap: 8 }}>
        <Cursor /> Waiting for a workspace
      </div>
    );
  } else if (sel) {
    body = (
      <>
        {(!lines || !lines.length) && (
          <div className={REDUCED ? undefined : 'bo-tilt-in'}>
          <DS.ConsoleLine type="dim">
            {sel.status === 'skipped' ? 'Skipped — up to date; not built in this run. Last successful build: yesterday 18:42 (' + sel.curSha + ')'
              : sel.status === 'queued' ? 'Queued — waiting for dependencies: ' + sel.def.deps.filter((d) => eng.p[d].status !== 'succeeded' && eng.p[d].status !== 'skipped').map(BO.shortName).join(', ')
              : 'No log yet — output streams here once the build starts.'}
          </DS.ConsoleLine>
          </div>
        )}
        {lines && <RevealLog key={selected} lines={lines} />}
        {selBuilding && (
          <div style={{ color: 'var(--amber-text)', fontFamily: MONO, fontSize: 'var(--text-xs)', lineHeight: 'var(--leading-mono)', display: 'flex', gap: 8, alignItems: 'baseline' }}>
            <span>build in progress</span><Cursor />
          </div>
        )}
      </>
    );
  } else {
    body = (
      <>
        <RevealLog key="narrative" lines={lines} />
        <NarrLine type="dim" cursor>{eng.phase === 'idle' || eng.phase === 'boot' ? 'ready' : ''}</NarrLine>
      </>
    );
  }

  return (
    <div style={{ flex: 1, minHeight: 0, position: 'relative', display: 'flex', flexDirection: 'column' }}>
      <div ref={boxRef} onScroll={onScroll} className="bo-scroll" style={{
        flex: 1, minHeight: 0, overflowY: 'auto', background: 'var(--console-bg)',
        borderRadius: 0, padding: '8px 12px', boxSizing: 'border-box',
        fontWeight: 300, /* yoğun metin: Geist Mono Light — ince ama okunur */
      }}>
        {body}
      </div>
      <LatestPill show={away} onClick={jump} />
    </div>
  );
}

/* ================= ÖZET STREAM ================= */
function StreamRow({ ev, eng, selected, onSelect, typing, onTypeDone }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [hover, setHover] = React.useState(false);
  const c = eng.counts();
  const glyph = ev.kind === 'ok' ? 'succeeded' : ev.kind === 'fail' ? 'failed' : ev.kind === 'skip' ? 'skipped'
    : ev.kind === 'taskdone' ? 'succeeded'
    : ev.kind === 'done' ? (c.failed ? 'failed' : 'succeeded') : null;
  const isSel = ev.project && ev.project === selected;
  const clickable = !!ev.project;
  const color = ev.kind === 'fail' ? 'var(--status-fail-text)'
    : ev.kind === 'skip' ? 'var(--text-dim)'
    : ev.kind === 'taskdone' ? 'var(--status-success-text)'
    : ev.kind === 'done' ? (c.failed ? 'var(--status-fail-text)' : 'var(--status-success-text)')
    : ev.kind === 'sync' || ev.kind === 'info' || ev.kind === 'task' ? 'var(--text-dim)' : 'var(--text-secondary)';
  return (
    <div
      onClick={clickable ? () => onSelect(isSel ? null : ev.project) : undefined}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      className={((ev.kind === 'done' && !c.failed) || ev.kind === 'taskdone') && !REDUCED ? 'bo-glow-once' : undefined}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, minHeight: 24, padding: '0 10px', position: 'relative',
        fontFamily: MONO, fontSize: 'var(--text-xs)', fontVariantNumeric: 'tabular-nums', lineHeight: 'var(--leading-mono)',
        color, cursor: clickable ? 'pointer' : 'default', userSelect: 'none',
        background: isSel ? 'var(--surface-raised)' : hover && clickable ? 'var(--surface-hover)' : 'transparent',
        transition: 'background var(--duration-fast) var(--ease-standard)', whiteSpace: 'nowrap',
      }}>
      {isSel && <span style={{ position: 'absolute', left: 0, top: 0, bottom: 0, width: 2, background: 'var(--amber)' }}></span>}
      <span style={{ color: 'var(--text-faint)', flex: 'none' }}>{ev.time}</span>
      {glyph ? <DS.StatusGlyph status={glyph} size={12} /> : <span style={{ color: 'var(--amber-text)', flex: 'none', width: 12, textAlign: 'center' }}>▸</span>}
      <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>
        {/* hata ve bitiş özetleri animasyonsuz basılır — kötü haber ve sonuç beklemez */}
        {typing ? <TypingLine key={'t' + ev.id} text={ev.text} instant={ev.kind === 'fail' || ev.kind === 'done' || ev.kind === 'taskdone'} onDone={onTypeDone} /> : ev.text}
      </span>
    </div>
  );
}

function StreamPanel({ eng, selected, onSelect, workspace }) {
  const boxRef = React.useRef(null);
  const stick = React.useRef(true);
  const [away, setAway] = React.useState(false);
  const jumping = React.useRef(false); // programatik smooth-scroll penceresi
  const evs = eng.stream.slice(-150);
  const active = eng.activeLine;
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
        {!workspace && (
          <div style={{ padding: '6px 10px', fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--text-faint)' }}>No events yet.</div>
        )}
        {evs.map((ev) => (
          <StreamRow key={ev.id} ev={ev} eng={eng} selected={selected} onSelect={onSelect}
            typing={ev.id === pendingId}
            onTypeDone={() => setPendingId((cur) => (cur === ev.id ? null : cur))} />
        ))}
        {active && (
          <div
            onClick={() => onSelect(selected === activeProject ? null : activeProject)}
            style={{
              display: 'flex', alignItems: 'center', gap: 8, minHeight: 24, padding: '0 10px',
              fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--amber-text)', cursor: 'pointer', userSelect: 'none',
              whiteSpace: 'nowrap',
            }}>
            <span style={{ color: 'var(--text-faint)' }}>{eng.wall()}</span>
            <span style={{ width: 12, flex: 'none', display: 'inline-flex', justifyContent: 'center' }}><Cursor /></span>
            <TypingLine key={active.id} text={active.text} instant={false} />
          </div>
        )}
        {eng.task && (
          <div style={{
            display: 'flex', alignItems: 'center', gap: 8, minHeight: 24, padding: '0 10px',
            fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--amber-text)', whiteSpace: 'nowrap', userSelect: 'none',
          }}>
            <span style={{ color: 'var(--text-faint)' }}>{eng.wall()}</span>
            <span style={{ width: 12, flex: 'none', display: 'inline-flex', justifyContent: 'center' }}><Cursor /></span>
            <TypingLine key={eng.task.kind + eng.task.i} text={eng.task.stream} instant={false} />
          </div>
        )}
        {workspace && !active && !eng.task && pendingId == null && (
          <div style={{
            display: 'flex', alignItems: 'center', gap: 8, minHeight: 24, padding: '0 10px',
            fontFamily: MONO, fontSize: 'var(--text-xs)', fontVariantNumeric: 'tabular-nums',
            color: 'var(--text-faint)', whiteSpace: 'nowrap', userSelect: 'none',
          }}>
            <span>{eng.wall()}</span>
            <span style={{ width: 12, flex: 'none', display: 'inline-flex', justifyContent: 'center' }}><Cursor /></span>
          </div>
        )}
      </div>
      <LatestPill show={away} onClick={jump} />
    </div>
  );
}

/* ================= STICKY ŞERİT + GLOBAL PROGRESS =================
   Sol başta kalıcı işlem pill'i (BUILD · REBUILD — Sales.Core · CLEAN…): koşarken amber,
   bitince nötr; bir sonraki işleme dek durur. Hata kümesi sadeleşti: ilk 3 çip + "+N more". */
function StickyStrip({ eng, onSelect, onFilterFailed, workspace }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const c = eng.counts();
  const di = eng.depIssueCount();
  const wb = eng.willBuild.size;
  const fin = eng.finishedOfWB();
  const failed = eng.failedList();
  const wn = eng.phase === 'done' ? eng.warnings() : 0;

  let text, color = 'var(--text-secondary)', glyph = null, spin = false;
  if (!workspace) { text = 'Not configured — repository root not set'; color = 'var(--text-faint)'; }
  else if (eng.task) {
    const T = eng.task;
    text = `▸ ${T.title} ${Math.min(T.total, T.i + 1)}/${T.total} · ${T.label} · ${fmtElapsed(eng.taskElapsed())}`;
    color = 'var(--amber-text)'; spin = true;
  }
  else if (eng.taskResult) { text = eng.taskResult.text; color = 'var(--status-success-text)'; glyph = 'succeeded'; }
  else if (eng.resolveRun) {
    const R = eng.resolveRun;
    text = `▸ Resolving cycles · pass ${R.pass}/2 · ${R.fin}/${R.total} · ${fmtElapsed(eng.simT - R.startT)}`;
    color = 'var(--amber-text)'; spin = true;
  }
  else if (eng.phase === 'marking') { const n = eng.marked ? eng.marked.size : 0; text = '▸ Preparing — ' + n + (n === 1 ? ' project' : ' projects') + ' marked'; color = 'var(--amber-text)'; }
  else if (eng.phase === 'boot') { text = '▸ Waiting for Sync — project states appear after Sync'; color = 'var(--text-dim)'; }
  else if (eng.phase === 'syncing') { text = '▸ Sync — git fetch origin…'; }
  else if (eng.phase === 'idle') {
    text = eng._fastCheck() ? '▸ Ready — everything looks up to date' : `▸ Ready — ${wb} changed · ${BO.PROJECTS.length - wb} up to date`;
  } else if (eng.phase === 'running') {
    if (eng._fastCheck()) { text = '▸ Checking — scanning for changes…'; }
    else {
      const eta = eng.eta();
      const etaTxt = eta != null && c.building + c.queued > 0 ? (eta < 4000 ? ' · almost done' : ' · ~' + Math.max(5, Math.round(eta / 5000) * 5) + 's left') : '';
      text = `▸ Building ${fin}/${wb} · ${fmtElapsed(eng.elapsed())}${etaTxt}`;
    }
  } else if (eng.phase === 'stopped') { text = `▸ Stopped — ${fin}/${wb} · rest queued`; color = 'var(--text-dim)'; }
  else if (eng.phase === 'done') {
    if (eng._fastCheck()) { text = `Everything up to date — ${BO.PROJECTS.length} projects checked in ${BO.fmtDur(eng.checkDur)}, nothing to build`; color = 'var(--status-success-text)'; glyph = 'succeeded'; }
    else if (c.failed) { text = `Completed — ${c.failed} failed · ${c.succeeded} succeeded${di ? ` (${di} dependency-affected)` : ''} · ${c.skipped} skipped${wn ? ` · ${wn} warnings` : ''} · ${fmtElapsed(eng.elapsed())}`; color = 'var(--status-fail-text)'; glyph = 'failed'; }
    else { text = `Completed — ${c.succeeded} succeeded · ${c.skipped} skipped${wn ? ` · ${wn} warnings` : ''} · ${fmtElapsed(eng.elapsed())}`; color = 'var(--status-success-text)'; glyph = 'succeeded'; }
  }

  const opLabel = eng.opLabel();
  const opLive = eng.busy() || eng.phase === 'marking';
  const building = eng.buildingList();
  const progress = eng.task ? (eng.task.i / eng.task.total) * 100
    : eng.resolveRun ? (eng.resolveRun.fin / eng.resolveRun.total) * 100
    : eng._fastCheck()
    ? (eng.phase === 'done' ? 100 : (c.skipped / BO.PROJECTS.length) * 100)
    : wb ? (fin / wb) * 100 : 0;
  const pStatus = eng.task || eng.resolveRun ? 'building'
    : eng.taskResult ? 'succeeded'
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
        indeterminate={eng.phase === 'syncing' || eng.phase === 'marking'}
        style={{ borderRadius: 0, background: 'var(--surface)' }} />
    </div>
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

function BranchPopover({ open, branches, current, activeBranch, onPick }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [q, setQ] = React.useState('');
  React.useEffect(() => { if (!open) setQ(''); }, [open]);
  const list = branches.filter((b) => b.name.toLowerCase().includes(q.toLowerCase()));
  return (
    <Popover open={open} width={272}>
      <div style={{ fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)', padding: '2px 4px 8px' }}>Switch branch</div>
      <DS.Input placeholder="Search branches…" prefix={<I.search />} value={q} onChange={(e) => setQ(e.target.value)} />
      <div className="bo-scroll" style={{ maxHeight: 196, overflowY: 'auto', marginTop: 6 }}>
        {list.map((b) => {
          const sel = b.name === current;
          return (
            <BranchRow key={b.name} b={b} sel={sel} activeBranch={activeBranch} onPick={onPick} />
          );
        })}
        {!list.length && <div style={{ padding: 10, fontSize: 'var(--text-xs)', color: 'var(--text-dim)' }}>No branches match “{q}”.</div>}
      </div>
      <div style={{ padding: '8px 4px 2px', fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', lineHeight: 'var(--leading-snug)', borderTop: '1px solid var(--border-subtle)', marginTop: 6 }}>
        Picking a non-active branch requires a worktree; the active branch stays untouched.
      </div>
    </Popover>
  );
}
function BranchRow({ b, sel, activeBranch, onPick }) {
  const [hover, setHover] = React.useState(false);
  return (
    <div onClick={() => onPick(b)} onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, height: 28, padding: '0 6px', borderRadius: 'var(--radius-sm)',
        cursor: 'pointer', background: hover ? 'var(--surface-raised)' : 'transparent', userSelect: 'none',
        transition: 'background var(--duration-fast) var(--ease-standard)',
      }}>
      <span style={{ display: 'inline-flex', color: sel ? 'var(--amber-text)' : 'var(--text-dim)', width: 12 }}>{sel ? <I.check /> : <I.branch />}</span>
      <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', color: sel ? 'var(--text-primary)' : 'var(--text-secondary)', flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{b.name}</span>
      {b.name === activeBranch
        ? <span style={{ fontSize: 'var(--text-2xs)', color: 'var(--amber-text)', background: 'var(--amber-soft)', border: '1px solid var(--amber-border)', borderRadius: 'var(--radius-xs)', padding: '1px 5px' }}>active</span>
        : <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>{b.sha}</span>}
    </div>
  );
}

function WorktreePopover({ open, align, forced, on, setOn, autoName, worktrees, chosen, setChosen, onDelete, source }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  return (
    <Popover open={open} width={300} align={align}>
      <div style={{ fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)', padding: '2px 4px 8px' }}>Worktree</div>
      <div style={{ padding: '0 4px' }}>
        <DS.Switch checked={on} disabled={forced} onChange={setOn} label="Build in worktree" />
        <div style={{ fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', margin: '6px 0 8px', lineHeight: 'var(--leading-snug)' }}>
          {forced
            ? 'Different branch selected — worktree required. The committed HEAD is built; active branch and local changes stay untouched.'
            : on
              ? 'The committed HEAD builds in a separate worktree; local changes excluded.'
              : 'Off: in-place build — local changes included.'}
        </div>
      </div>
      {on && (
        <div style={{ borderTop: '1px solid var(--border-subtle)', paddingTop: 6 }}>
          <div style={{ fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)', padding: '2px 4px 4px' }}>Target worktree</div>
          <WtRow name={autoName + ' (new)'} note="auto" sel={chosen === null} onPick={() => setChosen(null)} />
          {worktrees.map((w) => (
            <WtRow key={w.name} name={w.name} note={w.note} sel={chosen === w.name} onPick={() => setChosen(w.name)} onDelete={() => onDelete(w.name)} />
          ))}
        </div>
      )}
      <div style={{ padding: '8px 4px 2px', borderTop: '1px solid var(--border-subtle)', marginTop: 6, display: 'flex', gap: 6, alignItems: 'baseline', minWidth: 0 }}>
        <span style={{ fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', flex: 'none' }}>source</span>
        <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-secondary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{source}</span>
      </div>
    </Popover>
  );
}
function WtRow({ name, note, sel, onPick, onDelete }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [hover, setHover] = React.useState(false);
  return (
    <div onClick={onPick} onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, height: 28, padding: '0 6px', borderRadius: 'var(--radius-sm)',
        cursor: 'pointer', background: hover ? 'var(--surface-raised)' : 'transparent', userSelect: 'none',
        transition: 'background var(--duration-fast) var(--ease-standard)',
      }}>
      <span style={{ display: 'inline-flex', color: sel ? 'var(--amber-text)' : 'var(--text-faint)', width: 12 }}>{sel ? <I.check /> : <I.tree />}</span>
      <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', color: sel ? 'var(--text-primary)' : 'var(--text-secondary)', flex: 1, whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{name}</span>
      <span style={{ fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', whiteSpace: 'nowrap' }}>{note}</span>
      {onDelete && hover && (
        <DS.IconButton size="sm" title="Delete worktree" onClick={(e) => { e.stopPropagation(); onDelete(); }}><I.trash /></DS.IconButton>
      )}
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
const SCENES = [
  { id: 1, label: 'Hero', desc: 'Live parallel build — the frontier flows in sync across graph and list; finishes clean.' },
  { id: 2, label: 'Detail', desc: 'A card selected: full log, [← Back], node centered in the graph; sim keeps running.' },
  { id: 3, label: 'Failure', desc: 'Five failures — Sales.Core mid-chain, dependents still build; failed cluster overflows to “+2 more”.' },
  { id: 4, label: 'Up to date', desc: 'Everything current — quick check, nothing to build; calm green tone.' },
  { id: 5, label: 'First run', desc: 'Empty state: nothing configured — setup checklist routes to Settings.' },
  { id: 6, label: 'Ready', desc: 'Initial state after Sync — no colours yet; branch and worktree pickers open.' },
  { id: 7, label: 'Graph focus', desc: 'Graph enlarged, node selected. Reduced-motion: static layout, color only.' },
  { id: 8, label: 'Cycle', desc: 'Three projects in a loop — Build skips them (one project fails mid-chain), Resolve cycles builds them in two passes.' },
];

/* ================= AYARLAR — katman tanımları ================= */
const DEFAULT_LAYER_CFG = []; // varsayılan: katman yok — projeler tek liste
const SAMPLE_LAYER_CFG = [
  { name: 'Layer 0 — Core', rx: '^OSYS\\.(Base$|Common\\.)' },
  { name: 'Layer 1 — Infrastructure', rx: '^OSYS\\.(Data\\.|Security$|Shared\\.UI$|Integration\\.Core$)' },
  { name: 'Layer 2 — Domain', rx: '^OSYS\\.Domain\\.' },
  { name: 'Layer 3 — Services', rx: '\\.(Scheduling|Workshop|Catalog|Invoicing|Accounting|Inventory)$|^OSYS\\.(Sales|UsedCars|Reporting)\\.Core$' },
  { name: 'Layer 4 — API', rx: '^OSYS\\.(?!Mobile\\.).*\\.Api$' },
  { name: 'Layer 5 — Client', rx: '^OSYS\\.(Web|Client|Mobile)\\.' },
];

/* ada uygulanan regex'lerle ilk-eşleşme gruplaması; eşleşmeyen → Diğer */
function compileGroups(cfg) {
  /* harici projeler kendi katmanı gibi en ÜSTTE toplanır (derleme sırasının başında);
     katman regex'lerine hiç girmezler. */
  const extRows = BO.PROJECTS.filter((p) => p.ext);
  const repo = BO.PROJECTS.filter((p) => !p.ext);
  const head = extRows.length ? [{ name: 'External projects', rows: extRows }] : [];
  if (!cfg || !cfg.length) {
    return head.length ? head.concat([{ name: 'Repository', rows: repo }]) : [{ name: null, rows: repo }];
  }
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
const ABOUT_VERSION = '1.14.0';
const ABOUT_SHORTCUTS = [
  ['Build — or Stop while a run is in flight', ['F5']],
  ['Rebuild — all projects, cache ignored', ['Ctrl+F5', 'Shift+F5']],
  ['Focus the project filter', ['Ctrl+F']],
  ['About — version, shortcuts and diagnostics', ['F1']],
  ['What’s new — release notes for this version', ['Ctrl+F1']],
  ['Close the topmost open layer: dialog → popover/menu → selection', ['Esc']],
  ['Global — bring the window back from the tray', ['Alt+B']],
];
const ABOUT_ENV = [
  ['App version', ABOUT_VERSION],
  ['Engine version', ABOUT_VERSION],
  ['Engine PID', '41124'],
  ['.NET runtime', '.NET 10.0.10'],
  ['OS', 'Microsoft Windows 10.0.26200'],
];
/* Yollar tam gösterilir — ellipsis yok, satır sonu her karakterde kırılabilir */
const ABOUT_PATHS = [
  ['MSBuild', 'C:\\Program Files\\Microsoft Visual Studio\\18\\Enterprise\\MSBuild\\Current\\Bin\\MSBuild.exe'],
  ['Repository root', 'D:\\Projects\\Delta\\OSYS'],
  ['State file', 'C:\\Users\\Delta\\AppData\\Local\\BuildOrchestrator\\ui-state.json'],
  ['Logs', 'C:\\Users\\Delta\\AppData\\Local\\BuildOrchestrator\\logs'],
  ['Worktree pool', 'C:\\Users\\Delta\\AppData\\Local\\BuildOrchestrator\\worktrees'],
];
/* NOT: gerçek listeyle değiştirilecek — uygulamada paketlenen bağımlılıkların çıktısı */
const ABOUT_THIRD = [
  ['MSBuild (Microsoft.Build)', '18.0', 'MIT'],
  ['LibGit2Sharp', '0.30.0', 'MIT'],
  ['.NET runtime', '10.0.10', 'MIT'],
  ['Geist · Geist Mono', '1.4', 'SIL OFL 1.1'],
  ['Lucide icons', '0.54', 'ISC'],
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

function WhatsNew() {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const all = window.DELTA_BO_NOTES || [];
  const [open, setOpen] = React.useState(false);
  const shown = open ? all : all.slice(0, 3);
  const rest = all.length - shown.length;
  const caps = { fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-dim)' };
  return (
    <>
      {shown.map((rel, ri) => (
        <div key={rel.v} style={{ paddingTop: ri ? 14 : 0, marginTop: ri ? 14 : 0, borderTop: ri ? '1px solid var(--border-subtle)' : 'none' }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 10 }}>
            <span style={{ fontFamily: MONO, fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)' }}>{rel.v}</span>
            {rel.v === ABOUT_VERSION && (
              <span style={{
                display: 'inline-flex', alignItems: 'center', height: 17, padding: '0 6px',
                border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-xs)',
                background: 'var(--surface-raised)', fontSize: 10, fontWeight: 500,
                letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-dim)',
              }}>Installed</span>
            )}
            <span style={{ flex: 1 }}></span>
            <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>{rel.date}</span>
          </div>
          {KIND_ORDER.map((k) => {
            const items = rel.items.filter((it) => it[0] === k);
            if (!items.length) return null;
            const K = NOTE_KINDS[k];
            return (
              <div key={k} style={{ marginBottom: 10 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 7, marginBottom: 5 }}>
                  <span aria-hidden="true" style={{ width: 6, height: 6, flex: 'none', borderRadius: 1, background: K.color }}></span>
                  <span style={caps}>{K.label}</span>
                </div>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 4, paddingLeft: 13 }}>
                  {items.map(([, text], i) => (
                    <span key={i} style={{ fontSize: 'var(--text-sm)', color: 'var(--text-secondary)', lineHeight: 'var(--leading-snug)' }}>{text}</span>
                  ))}
                </div>
              </div>
            );
          })}
        </div>
      ))}
      {rest > 0 && (
        <div style={{ marginTop: 6, paddingTop: 12, borderTop: '1px solid var(--border-subtle)', marginLeft: -10 }}>
          <DS.Button size="sm" variant="ghost" icon={<I.down />} onClick={() => setOpen(true)}>Earlier versions ({rest})</DS.Button>
        </div>
      )}
    </>
  );
}

/* What's new artık About'un sekmesi değil, kendi dialogu (v1.13.0) */
function NotesDialog({ open, onSeen, onClose }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  React.useEffect(() => { if (open && onSeen) onSeen(); }, [open]); // eslint-disable-line
  if (!open) return null;
  return (
    <div className="ds-scrim-in" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}
      style={{ position: 'absolute', inset: 0, zIndex: 101, background: 'var(--scrim)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      <div role="dialog" aria-modal="true" aria-label="What’s new" className="ds-dialog-in"
        style={{
          width: 620, maxWidth: 'calc(100% - 48px)', maxHeight: 'calc(100% - 48px)', display: 'flex', flexDirection: 'column',
          background: 'var(--surface-raised)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--elevation-overlay)', fontFamily: 'var(--font-sans)',
        }}>
        <div style={{ display: 'flex', alignItems: 'flex-end', gap: 12, padding: '16px 28px 12px 18px', borderBottom: '1px solid var(--border-subtle)' }}>
          <div style={{ minWidth: 0, flex: 1 }}>
            <div style={{ fontSize: 'var(--text-md)', fontWeight: 600, color: 'var(--text-primary)' }}>What’s new</div>
            <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', marginTop: 2 }}>Release notes for Build Orchestrator.</div>
          </div>
          <span style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 2, flex: 'none', marginBottom: 3 }}>
            <span style={{
              fontSize: 10, fontWeight: 500, letterSpacing: 'var(--tracking-caps)',
              textTransform: 'uppercase', color: 'var(--text-faint)',
            }}>Installed version</span>
            <span style={{ fontFamily: MONO, fontSize: 'var(--text-md)', fontWeight: 500, color: 'var(--text-primary)', lineHeight: 1 }}>{ABOUT_VERSION}</span>
          </span>
        </div>
        <div className="bo-scroll" style={{ padding: '14px 18px 18px', overflowY: 'auto', height: 400 }}><WhatsNew /></div>
        <div style={{ display: 'flex', alignItems: 'center', padding: '12px 16px', borderTop: '1px solid var(--border-subtle)' }}>
          <span style={{ flex: 1 }}></span>
          <DS.Button variant="secondary" onClick={onClose}>Close</DS.Button>
        </div>
      </div>
    </div>
  );
}

function AboutDialog({ open, onClose, logoSrc, companyLogoSrc }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [tab, setTab] = React.useState('Shortcuts');
  const [copied, setCopied] = React.useState(false);
  const tRef = React.useRef(null);
  React.useEffect(() => { if (open) { setTab('Shortcuts'); setCopied(false); } }, [open]); // eslint-disable-line
  React.useEffect(() => () => { if (tRef.current) clearTimeout(tRef.current); }, []);
  if (!open) return null;

  const diagnostics = () => ['Build Orchestrator ' + ABOUT_VERSION]
    .concat(ABOUT_ENV.concat(ABOUT_PATHS).map((r) => r[0] + ': ' + r[1])).join('\n');
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

  const rowBase = { display: 'flex', alignItems: 'center', gap: 12, minHeight: 26 };
  const monoVal = { fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--text-secondary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' };

  return (
    <div className="ds-scrim-in" onMouseDown={(e) => { if (e.target === e.currentTarget) onClose(); }}
      style={{ position: 'absolute', inset: 0, zIndex: 100, background: 'var(--scrim)', display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      <div role="dialog" aria-modal="true" aria-label="About Build Orchestrator" className="ds-dialog-in"
        style={{
          width: 660, maxWidth: 'calc(100% - 48px)', maxHeight: 'calc(100% - 48px)', display: 'flex', flexDirection: 'column',
          background: 'var(--surface-raised)', border: '1px solid var(--border-strong)', borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--elevation-overlay)', fontFamily: 'var(--font-sans)',
        }}>
        <div style={{ display: 'flex', alignItems: 'flex-start', gap: 14, padding: '18px 18px 0' }}>
          <img src={logoSrc} alt="Build Orchestrator" style={{ height: 30, marginTop: 2, flex: 'none' }} />
          <div style={{ minWidth: 0, flex: 1 }}>
            <div style={{ fontSize: 'var(--text-lg)', fontWeight: 600, color: 'var(--text-primary)' }}>Build Orchestrator</div>
            <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', marginTop: 2 }}>Ordered, incremental builds for a multi-project .NET solution.</div>
            <div style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', marginTop: 8 }}>{ABOUT_VERSION} · © 2026 Delta</div>
          </div>
          {companyLogoSrc && (
            <div style={{ display: 'flex', alignItems: 'center', gap: 12, flex: 'none', paddingTop: 2 }}>
              <span aria-hidden="true" style={{ width: 1, height: 30, background: 'var(--border-subtle)' }}></span>
              <span style={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 5 }}>
                <span style={{ fontSize: 'var(--text-2xs)', letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)' }}>Licensed to</span>
                <img src={companyLogoSrc} alt="Delta" style={{ height: 13, display: 'block', opacity: 0.8 }} />
              </span>
            </div>
          )}
        </div>
        <div style={{ padding: '14px 18px 14px', borderBottom: '1px solid var(--border-subtle)' }}>
          <DS.Segment size="sm" options={['Shortcuts', 'Environment', 'Third-party']} value={tab} onChange={setTab} />
        </div>
        <div style={{ padding: '14px 18px 18px', overflowY: 'auto', minHeight: 236 }}>
          {tab === 'Shortcuts' && ABOUT_SHORTCUTS.map(([label, keys]) => (
            <div key={label} style={rowBase}>
              <span style={{ flex: 1, fontSize: 'var(--text-sm)', color: 'var(--text-secondary)' }}>{label}</span>
              <span style={{ display: 'flex', gap: 4, flex: 'none' }}>{keys.map((k) => <DS.Kbd key={k}>{k}</DS.Kbd>)}</span>
            </div>
          ))}
          {tab === 'Environment' && ABOUT_ENV.concat(ABOUT_PATHS).map(([label, val]) => (
            <div key={label} style={rowBase}>
              <span style={{ width: 130, flex: 'none', fontSize: 'var(--text-xs)', color: 'var(--text-dim)' }}>{label}</span>
              <div className="bo-xscroll" style={{ flex: 1, minWidth: 0 }}
                onWheel={(e) => { const el = e.currentTarget; const d = e.deltaY || e.deltaX; if (d && el.scrollWidth > el.clientWidth) el.scrollLeft += d; }}>
                <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--text-secondary)', whiteSpace: 'nowrap' }}>{val}</span>
              </div>
            </div>
          ))}
          {tab === 'Third-party' && (
            <>
              <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', marginBottom: 10 }}>Bundled components and their licenses.</div>
              {ABOUT_THIRD.map(([name, ver, lic]) => (
                <div key={name} style={rowBase}>
                  <span style={{ flex: 1, fontSize: 'var(--text-sm)', color: 'var(--text-secondary)' }}>{name}</span>
                  <span style={{ ...monoVal, width: 70, flex: 'none' }}>{ver}</span>
                  <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', width: 92, flex: 'none', textAlign: 'right' }}>{lic}</span>
                </div>
              ))}
            </>
          )}
        </div>
        <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '12px 16px', borderTop: '1px solid var(--border-subtle)' }}>
          <span style={{ display: 'inline-flex', color: copied ? 'var(--status-success-text)' : undefined }}>
            <DS.Button size="sm" variant="ghost" icon={copied ? <I.check /> : <I.copy />} onClick={copyDiag}>{copied ? 'Copied' : 'Copy diagnostics'}</DS.Button>
          </span>
          <span style={{ flex: 1 }}></span>
          <DS.Button variant="secondary" onClick={onClose}>Close</DS.Button>
        </div>
      </div>
    </div>
  );
}

function SettingsDialog({ open, cfg, extCfg, root, setup, importOnOpen, onClose, onSave }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [draft, setDraft] = React.useState(cfg);
  const [rootDraft, setRootDraft] = React.useState(root || '');
  const [msg, setMsg] = React.useState(null); // {ok, text} — export/import geri bildirimi
  const [clearArmed, setClearArmed] = React.useState(false); // sil: iki aşamalı onay (dialog yok)
  const fileRef = React.useRef(null);
  const msgT = React.useRef(null);
  const dragRef = React.useRef(null);
  const [dragIdx, setDragIdx] = React.useState(null);
  const [dragOff, setDragOff] = React.useState(0);
  /* harici projeler: katmanlardan ÖNCE derlenir, kart sırası derleme sırasıdır */
  const [ext, setExt] = React.useState(extCfg || []);
  const extRef = React.useRef(null);
  const [extIdx, setExtIdx] = React.useState(null);
  const [extOff, setExtOff] = React.useState(0);
  React.useEffect(() => { if (open) { setDraft(cfg.map((l) => ({ ...l }))); setExt((extCfg || []).map((x) => ({ ...x }))); setRootDraft(root || ''); setMsg(null); setClearArmed(false); dragRef.current = null; setDragIdx(null); setDragOff(0); extRef.current = null; setExtIdx(null); setExtOff(0); if (importOnOpen) setTimeout(() => { if (fileRef.current) fileRef.current.click(); }, 120); } }, [open]); // eslint-disable-line
  React.useEffect(() => () => { if (msgT.current) clearTimeout(msgT.current); }, []);
  if (!open) return null;
  const compiled = draft.map((l) => { try { return l.rx && l.rx.trim() ? new RegExp(l.rx, 'i') : null; } catch (e) { return undefined; } });
  const upd = (i, patch) => setDraft((d) => d.map((l, k) => (k === i ? { ...l, ...patch } : l)));
  const swap = (arr, a, b) => { const nd = arr.slice(); const t = nd[a]; nd[a] = nd[b]; nd[b] = t; return nd; };

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

  const valid = draft.every((l, i) => l.name.trim() && compiled[i] !== undefined) && ext.every((x) => x.path.trim());
  const flash = (ok, text) => { setMsg({ ok, text }); if (msgT.current) clearTimeout(msgT.current); msgT.current = setTimeout(() => setMsg(null), 2400); };
  /* dışa aktar: root + katmanlar tek JSON dosyası — makineler arası taşınabilir ayar */
  const doExport = () => {
    const data = {
      app: 'Build Orchestrator', version: ABOUT_VERSION,
      repositoryRoot: rootDraft.trim(),
      externalProjects: ext.filter((x) => x.path.trim()).map((x) => ({ path: x.path.trim(), vcs: x.vcs === 'tfvc' ? 'tfvc' : 'git' })),
      layers: draft.map((l) => ({ name: l.name.trim(), pattern: l.rx })),
    };
    try {
      const url = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' }));
      const a = document.createElement('a');
      a.href = url; a.download = 'build-orchestrator-settings.json';
      document.body.appendChild(a); a.click(); a.remove();
      setTimeout(() => URL.revokeObjectURL(url), 2000);
      flash(true, 'Exported — settings JSON');
    } catch (e) { flash(false, 'Export failed'); }
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
          ? d.externalProjects.map((x) => ({
            path: String(typeof x === 'string' ? x : (x && x.path) || ''),
            vcs: String((x && x.vcs) === 'tfvc' ? 'tfvc' : 'git'),
          })).filter((x) => x.path)
          : null;
        const hasRoot = typeof d.repositoryRoot === 'string';
        if (!layers && !hasRoot && !exts) throw new Error('shape');
        if (hasRoot) setRootDraft(d.repositoryRoot);
        if (layers) setDraft(layers);
        if (exts) setExt(exts);
        flash(true, 'Imported — ' + (layers ? layers.length : 0) + ' layers' + (exts ? ' · ' + exts.length + ' external' : '') + (hasRoot ? ' · root set' : ''));
      } catch (err) { flash(false, 'Invalid settings file'); }
    };
    rd.onerror = () => flash(false, 'Could not read the file');
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
    flash(true, 'Cleared — save to apply');
  };
  const caps = { fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)' };
  return (
    <DS.Dialog open title="Settings" width={760} onClose={onClose} footer={
      <>
        <DS.Button variant="ghost" onClick={() => setDraft(SAMPLE_LAYER_CFG.map((l) => ({ ...l })))}>Load sample layers</DS.Button>
        <span aria-hidden="true" style={{ width: 1, alignSelf: 'stretch', margin: '5px 3px', background: 'var(--border-subtle)' }}></span>
        <DS.Tooltip content="Export settings — repository root and layers as a JSON file" side="top">
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
        <span style={{ flex: 1 }}></span>
        <DS.Button variant="secondary" onClick={onClose}>Cancel</DS.Button>
        <DS.Button variant="primary" disabled={!valid || !rootDraft.trim()}
          onClick={() => onSave(rootDraft.trim(), draft.map((l) => ({ name: l.name.trim(), rx: l.rx })), ext.map((x) => ({ path: x.path.trim(), vcs: x.vcs === 'tfvc' ? 'tfvc' : 'git' })).filter((x) => x.path))}>{setup ? 'Save and sync' : 'Save'}</DS.Button>
      </>
    }>
      <div className="bo-scroll" style={{ minHeight: 300, maxHeight: 'min(56vh, 460px)', overflowY: 'auto', paddingRight: 10, marginRight: -10, display: 'flex', flexDirection: 'column' }}>
      <div style={{ ...caps, marginBottom: 6 }}>Workspace</div>
      <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 'var(--leading-snug)', marginBottom: 10 }}>
        Repository root — the folder that holds the OSYS solutions. Projects and the dependency graph are discovered from it on Sync.
      </div>
      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
        <DS.Input mono value={rootDraft} placeholder="D:\src\osys" spellCheck={false}
          onChange={(e) => setRootDraft(e.target.value)} style={{ width: 'auto', flex: 1 }} />
        <DS.Button variant="secondary" icon={<I.folder />} onClick={() => setRootDraft('D:\\src\\osys')} style={{ flex: 'none' }}>Browse…</DS.Button>
      </div>
      <div style={{ height: 1, background: 'var(--border-subtle)', margin: '18px 0 16px' }}></div>
      <div style={{ ...caps, marginBottom: 6 }}>External projects</div>
      <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 'var(--leading-snug)', marginBottom: 12 }}>
        Projects outside the repository root — a folder, a solution or a project file, and whether it comes from Git or TFVC. The working copy root is found from the path upwards.
        They are built <strong style={{ color: 'var(--text-secondary)', fontWeight: 500 }}>before</strong> everything else, in this order; the rest follows the layers below.
      </div>
      {!ext.length && (
        <div style={{ border: '1px dashed var(--border)', borderRadius: 'var(--radius-md)', padding: '14px 16px', fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 'var(--leading-snug)' }}>
          No external projects — only what is discovered under the repository root is built.
        </div>
      )}
      {ext.length > 0 && (
        <div style={{ display: 'flex', gap: 6, padding: '0 34px 4px 30px', boxSizing: 'border-box' }}>
          <span style={{ ...caps, flex: 1 }}>Project path</span>
          <span style={{ ...caps, width: 96, flex: 'none' }}>Source</span>
        </div>
      )}
      {ext.map((x, i) => (
        <div key={i} style={{
          display: 'flex', alignItems: 'center', gap: 6, height: 36, boxSizing: 'border-box', padding: '0 6px 0 2px', marginBottom: 6,
          background: extIdx === i ? 'var(--surface-raised)' : 'var(--surface)',
          border: '1px solid ' + (extIdx === i ? 'var(--border-strong)' : 'var(--border)'),
          borderRadius: 'var(--radius-md)', position: 'relative', zIndex: extIdx === i ? 5 : 1,
          transform: extIdx === i ? 'translateY(' + extOff + 'px)' : 'none',
        }}>
          <span
            onPointerDown={(e) => extDrag.start(e, i)} onPointerMove={extDrag.move} onPointerUp={extDrag.end} onPointerCancel={extDrag.end}
            title="Drag to reorder" aria-label="Drag to reorder"
            style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 20, alignSelf: 'stretch', flex: 'none', color: 'var(--text-faint)', cursor: extIdx === i ? 'grabbing' : 'grab', touchAction: 'none' }}>
            <I.grip />
          </span>
          <DS.Input mono value={x.path} placeholder="C:\src\shared\Delta.Common\Delta.Common.csproj" spellCheck={false}
            onChange={(e) => setExt((arr) => arr.map((v, k) => (k === i ? { ...v, path: e.target.value } : v)))} style={{ width: 'auto', flex: 1 }} />
          <DS.Select value={x.vcs === 'tfvc' ? 'tfvc' : 'git'} aria-label="Version control"
            onChange={(e) => setExt((arr) => arr.map((v, k) => (k === i ? { ...v, vcs: e.target.value } : v)))} style={{ width: 96, flex: 'none' }}>
            <option value="git">Git</option>
            <option value="tfvc">TFVC</option>
          </DS.Select>
          <DS.IconButton size="sm" title="Remove external project" onClick={() => setExt((arr) => arr.filter((_, k) => k !== i))}><I.trash /></DS.IconButton>
        </div>
      ))}
      <div style={{ marginTop: 10 }}>
        <DS.Button size="sm" variant="ghost" icon={<I.plus />} onClick={() => setExt((arr) => [...arr, { path: '', vcs: 'git' }])}>Add external project</DS.Button>
      </div>
      <div style={{ height: 1, background: 'var(--border-subtle)', margin: '18px 0 16px' }}></div>
      <div style={{ ...caps, marginBottom: 6 }}>Layers</div>
      <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 'var(--leading-snug)', marginBottom: 12 }}>
        Projects are grouped by the first matching pattern (regex on the project name), top to bottom; card order is the layer order in the list.
        Non-matching projects fall under <span style={{ fontFamily: MONO }}>Other</span>.
      </div>
      {!draft.length && (
        <div style={{ border: '1px dashed var(--border)', borderRadius: 'var(--radius-md)', padding: '14px 16px', fontSize: 'var(--text-xs)', color: 'var(--text-dim)', lineHeight: 'var(--leading-snug)' }}>
          No layers yet — projects show as a single list in build order.
        </div>
      )}
      {draft.length > 0 && (
        <div style={{ display: 'flex', gap: 6, padding: '0 34px 4px 30px', boxSizing: 'border-box' }}>
          <span style={{ ...caps, width: 170, flex: 'none' }}>Layer name</span>
          <span style={{ ...caps, flex: 1 }}>Pattern</span>
        </div>
      )}
      {draft.map((l, i) => (
        <div key={i} style={{
          display: 'flex', alignItems: 'center', gap: 6, height: 36, boxSizing: 'border-box', padding: '0 6px 0 2px', marginBottom: 6,
          background: dragIdx === i ? 'var(--surface-raised)' : 'var(--surface)',
          border: '1px solid ' + (dragIdx === i ? 'var(--border-strong)' : 'var(--border)'),
          borderRadius: 'var(--radius-md)', position: 'relative', zIndex: dragIdx === i ? 5 : 1,
          transform: dragIdx === i ? 'translateY(' + dragOff + 'px)' : 'none',
        }}>
          <span
            onPointerDown={(e) => layerDrag.start(e, i)} onPointerMove={layerDrag.move} onPointerUp={layerDrag.end} onPointerCancel={layerDrag.end}
            title="Drag to reorder" aria-label="Drag to reorder"
            style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 20, alignSelf: 'stretch', flex: 'none', color: 'var(--text-faint)', cursor: dragIdx === i ? 'grabbing' : 'grab', touchAction: 'none' }}>
            <I.grip />
          </span>
          <DS.Input value={l.name} placeholder="Layer name" onChange={(e) => upd(i, { name: e.target.value })} style={{ width: 170, flex: 'none' }} />
          <DS.Input mono value={l.rx} invalid={compiled[i] === undefined} placeholder="^OSYS\.Domain\." spellCheck={false}
            onChange={(e) => upd(i, { rx: e.target.value })} style={{ width: 'auto', flex: 1 }} />
          <DS.IconButton size="sm" title="Delete layer" onClick={() => setDraft((d) => d.filter((_, k) => k !== i))}><I.trash /></DS.IconButton>
        </div>
      ))}
      <div style={{ marginTop: 10 }}>
        <DS.Button size="sm" variant="ghost" icon={<I.plus />} onClick={() => setDraft((d) => [...d, { name: 'Layer ' + (d.length + 1), rx: '' }])}>Add layer</DS.Button>
      </div>
      </div>
    </DS.Dialog>
  );
}

/* ================= BUILD MENÜSÜ ================= */
function BuildMenuItem({ icon, title, desc, kbd, onPick }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [hover, setHover] = React.useState(false);
  return (
    <div onClick={onPick} onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      style={{
        display: 'flex', alignItems: 'center', gap: 10, padding: '7px 8px', borderRadius: 'var(--radius-sm)',
        cursor: 'pointer', background: hover ? 'var(--surface-raised)' : 'transparent', userSelect: 'none',
        transition: 'background var(--duration-fast) var(--ease-standard)',
      }}>
      <span style={{ display: 'inline-flex', color: 'var(--text-secondary)', width: 14, justifyContent: 'center', flex: 'none' }}>{icon}</span>
      <span style={{ flex: 1, minWidth: 0 }}>
        <span style={{ display: 'block', fontSize: 'var(--text-sm)', fontWeight: 500, color: 'var(--text-primary)' }}>{title}</span>
        <span style={{ display: 'block', fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', marginTop: 1 }}>{desc}</span>
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

function BuildAppInner({ scene = 1, autoPlay = true, simSpeed = 1, logoBase = 'assets' }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
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
  const [repoRoot, setRepoRoot] = React.useState('D:\\src\\osys');
  const [selected, setSelected] = React.useState(null);
  const [filter, setFilter] = React.useState(() => new Set()); // çoklu statü filtresi (VEYA)
  const [query, setQuery] = React.useState('');
  const [revealKey, setRevealKey] = React.useState(0);
  const [cfg, setCfg] = React.useState('Debug');
  const [perf, setPerf] = React.useState('Balanced');
  const allDirtyRef = React.useRef(false);
  const cycleRef = React.useRef(false); // Scene 8: workspace'te bilinen döngü — engine reset'leri arasında korunur
  const oneFailRef = React.useRef(false); // Scene 8: tek kasıtlı hata (Sales.Core) — kırmızı sonucu da aynı sahnede göster

  const activeBranch = 'main';
  const [branchSel, setBranchSel] = React.useState('main');
  const [branchPop, setBranchPop] = React.useState(false);
  const [wtPop, setWtPop] = React.useState(false);
  const [wtOn, setWtOn] = React.useState(false);
  const [wtChosen, setWtChosen] = React.useState(null); // null = otomatik
  const [worktrees, setWorktrees] = React.useState(BO.WORKTREES.slice());

  const [settingsOpen, setSettingsOpen] = React.useState(false);
  const [settingsImport, setSettingsImport] = React.useState(false); // ilk açılışta dosya seçiciyi tetikle
  const [aboutOpen, setAboutOpen] = React.useState(false);
  const [notesOpen, setNotesOpen] = React.useState(false); // What's new — kendi dialogu
  const [unseenNotes, setUnseenNotes] = React.useState(notesUnseen);
  const [buildMenu, setBuildMenu] = React.useState(false);
  const [layerCfg, setLayerCfg] = React.useState(() => {
    try { const v = JSON.parse(localStorage.getItem('delta-bo-layers-v2')); if (Array.isArray(v)) return v; } catch (e) { /* yok */ }
    return DEFAULT_LAYER_CFG;
  });
  const [extCfg, setExtCfg] = React.useState(() => {
    try { const v = JSON.parse(localStorage.getItem('delta-bo-external-v1')); if (Array.isArray(v)) return v; } catch (e) { /* yok */ }
    return [];
  });
  const groups = React.useMemo(() => compileGroups(layerCfg), [layerCfg]);

  const defLayout = { mode: 'quad', col: 50, left: 50, right: 50 };
  const [layout, setLayout] = React.useState(() => {
    try { return { ...defLayout, ...(JSON.parse(localStorage.getItem('delta-bo-layout-v1')) || {}) }; } catch (e) { return defLayout; }
  });
  const savedLayout = React.useRef(layout);
  const bodyRef = React.useRef(null);
  const saveLayout = (l) => { try { localStorage.setItem('delta-bo-layout-v1', JSON.stringify(l)); } catch (e) { /* yok */ } };

  /* ---- worktree modeli ---- */
  const forced = branchSel !== activeBranch;
  const wtActive = forced || wtOn;
  const slug = branchSel.replace(/[/]/g, '-');
  const autoName = slug + '-' + (worktrees.filter((w) => w.name.startsWith(slug)).length + 1);
  const wtName = wtChosen || autoName;
  const srcText = !wtActive ? 'working directory — local changes included' : 'committed HEAD (' + branchSel + ') → ' + wtName;

  /* ---- saat: motor ilerletici (gerçek zamana bağlı — throttle'a dayanıklı) ---- */
  React.useEffect(() => {
    let raf = 0, iv = 0, last = Date.now(), alive = true;
    const tick = () => {
      if (!alive) return;
      const now = Date.now();
      let dt = Math.min(2000, now - last); // dönüşte dev adım yok: en çok 2s telafi
      last = now;
      const e = engRef.current;
      const live = () => e.phase === 'syncing' || e.phase === 'running' || e.todo.length > 0;
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
  const bootLine = (e) => e.say('dim', 'Build Orchestrator 2.4.1 — Osys.sln loaded (' + BO.PROJECTS.length + ' projects) · ' + branchSel);
  const resetEngine = (opts) => {
    eng.reset({ seed: ++seedRef.current, cfg, maxPar: { Full: 6, Balanced: 4, Light: 2 }[perf], targetSha: (BO.BRANCHES.find((b) => b.name === branchSel) || {}).sha, cycle: cycleRef.current, oneFail: oneFailRef.current, ...opts });
    eng.branchName = branchSel;
    if (!opts || !opts.empty) bootLine(eng);
  };
  const doSync = () => {
    if (eng.busy() || !workspace) return;
    resetEngine({ allDirty: allDirtyRef.current });
    setSelected(null);
    setFilter(new Set());
    setRevealKey((k) => k + 1);
    eng.startSync();
    bump();
  };
  /* Build: koreografiyle durumdan koşar — konsol/stream temizlenir, herkes nötre döner,
     kapsam ×2 blink, sonra derleme. Rebuild aynı yol, kapsam = tüm projeler. */
  const doBuild = () => {
    if (!workspace || eng.busy()) return;
    setBuildMenu(false);
    setSelected(null);
    setFilter(new Set());
    if (eng.phase === 'boot') {
      resetEngine({ allDirty: allDirtyRef.current });
      allDirtyRef.current = false;
      setRevealKey((k) => k + 1);
      eng.startSync();
      eng.at(1250, () => eng.beginBuild());
    } else if (allDirtyRef.current) {
      allDirtyRef.current = false;
      eng.beginRebuild();
    } else {
      eng.beginBuild();
    }
    bump();
  };
  const doRebuild = () => {
    if (!workspace || eng.busy()) return;
    setBuildMenu(false);
    setSelected(null);
    setFilter(new Set());
    allDirtyRef.current = false;
    if (eng.phase === 'boot') {
      resetEngine({ allDirty: true });
      setRevealKey((k) => k + 1);
      eng.startSync();
      eng.at(1250, () => eng.beginRebuild());
    } else eng.beginRebuild();
    bump();
  };
  /* Build menüsündeki Clean (VS Clean Solution) — yalnız çıktılar; derin Clean bakım kutusunda */
  const doCleanSln = () => {
    if (!workspace || eng.busy()) return;
    setBuildMenu(false); setSelected(null); setFilter(new Set());
    if (!eng.startTask('cleansln')) return;
    allDirtyRef.current = true;
    bump();
  };
  /* satır aksiyonları: build / rebuild / clean — YALNIZ o proje.
     Satıra tıklanmış gibi davranmaz: seçim ve filtre sıfırlanır, graf odağı açılmaz — süreç
     Build/Rebuild/Resolve ile BİREBİR aynı. Reveal'a dokunulmaz: liste yerinden oynamaz. */
  const doRowAction = (kind, name) => {
    if (!workspace || eng.busy()) return;
    setBuildMenu(false);
    const ok = kind === 'clean' ? eng.beginProjectClean(name) : eng.beginProject(name, kind);
    if (!ok) return;
    setSelected(null);
    setFilter(new Set());
    bump();
  };
  const doResolve = () => {
    if (!workspace || eng.busy() || !eng.cycle) return;
    setBuildMenu(false);
    setSelected(null);
    setFilter(new Set());
    eng.startResolve();
    bump();
  };

  /* ---- bakım görevleri: Clean (çıktıları sil) / Optimize (restore + prune + index) ---- */
  const doClean = () => {
    if (!workspace || eng.busy()) return;
    setBuildMenu(false); setSelected(null); setFilter(new Set());
    if (!eng.startTask('clean')) return;
    allDirtyRef.current = true; // çıktılar gitti — sonraki Build tam derleme
    bump();
  };
  const doOptimize = () => {
    if (!workspace || eng.busy()) return;
    setBuildMenu(false); setSelected(null);
    eng.startTask('optimize');
    bump();
  };

  /* ---- sahneler ---- */
  const applyScene = React.useCallback((n) => {
    const e = engRef.current;
    setBranchPop(false); setWtPop(false); setBuildMenu(false); setSettingsOpen(false); setAboutOpen(false); setNotesOpen(false); setFilter(new Set()); setSelected(null);
    setQuery('');
    setRepoRoot(n === 5 ? '' : 'D:\\src\\osys');
    setBranchSel('main'); setWtOn(false); setWtChosen(null);
    allDirtyRef.current = false;
    cycleRef.current = n === 8;
    oneFailRef.current = n === 8;
    if (n !== 7) setLayout(savedLayout.current);
    const start = (opts, after) => {
      setWorkspace(true);
      eng.reset({ seed: ++seedRef.current, cfg, maxPar: { Full: 6, Balanced: 4, Light: 2 }[perf], ...opts });
      eng.branchName = 'main';
      bootLine(eng);
      setRevealKey((k) => k + 1);
      eng.startSync();
      eng.at(1250, () => eng.beginBuild());
      if (after) after(eng);
      bump();
    };
    if (n === 1) {
      if (autoPlay === false || autoPlay === 'false') {
        setWorkspace(true);
        eng.reset({ seed: ++seedRef.current, synced: true });
        bootLine(eng);
        setRevealKey((k) => k + 1);
        bump();
      } else start({});
    } else if (n === 2) {
      start({}, (e2) => {
        e2.fastForwardUntil((x) => x.finishedOfWB() >= 7 && x.buildingList().length > 0, 40000);
        const b = e2.buildingList();
        setSelected(b.indexOf('OSYS.Service.Api') >= 0 ? 'OSYS.Service.Api' : b[0] || 'OSYS.Sales.Api');
      });
    } else if (n === 3) {
      start({ multiFail: true }, (e2) => {
        e2.fastForwardUntil((x) => x.counts().failed >= 4, 120000);
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
      bootLine(eng);
      eng.say('info', 'Sync complete — 7 changed projects, ' + eng.willBuild.size + ' to build');
      setRevealKey((k) => k + 1);
      setWtOn(true);
      setBranchPop(true); setWtPop(true);
      bump();
    } else if (n === 8) {
      // Cycle sahnesi: sync sonrası idle — Resolve cycles / Build akışı kullanıcıya bırakılır
      setWorkspace(true);
      eng.reset({ seed: ++seedRef.current, cfg, maxPar: { Full: 6, Balanced: 4, Light: 2 }[perf], cycle: true, oneFail: true });
      eng.branchName = 'main';
      bootLine(eng);
      setRevealKey((k) => k + 1);
      eng.startSync();
      bump();
    } else if (n === 7) {
      savedLayout.current = layout;
      setLayout({ mode: 'quad', col: 60, left: 74, right: 54 });
      start({}, (e2) => {
        e2.fastForwardUntil((x) => x.finishedOfWB() >= 5 && x.buildingList().length > 0, 40000);
        const b = e2.buildingList();
        setSelected(b.indexOf('OSYS.Service.Api') >= 0 ? 'OSYS.Service.Api' : b[0]);
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

  /* ---- klavye ---- */
  React.useEffect(() => {
    const h = (e) => {
      if (e.key === 'F5') { e.preventDefault(); if (eng.task) return; if (e.ctrlKey || e.shiftKey) doRebuild(); else if (eng.phase === 'running' || eng.phase === 'marking') { eng.stop(); bump(); } else doBuild(); }
      if (e.key === 'F1') { e.preventDefault(); if (e.ctrlKey || e.metaKey) setNotesOpen((v) => !v); else setAboutOpen((v) => !v); }
      if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) {
        e.preventDefault();
        const el = document.getElementById('bo-proj-search');
        if (el) el.focus();
      }
      if (e.key === 'Escape') {
        if (notesOpen) setNotesOpen(false);
        else if (aboutOpen) setAboutOpen(false);
        else if (settingsOpen) setSettingsOpen(false);
        else if (branchPop || wtPop || buildMenu) { setBranchPop(false); setWtPop(false); setBuildMenu(false); }
        else setSelected(null);
      }
    };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  });

  /* ---- dışarı tıkla → popover kapat ---- */
  React.useEffect(() => {
    if (!branchPop && !wtPop && !buildMenu) return;
    const h = (e) => {
      if (e.target.closest && (e.target.closest('[data-bo-popover]') || e.target.closest('[data-bo-chip]'))) return;
      setBranchPop(false); setWtPop(false); setBuildMenu(false);
    };
    document.addEventListener('mousedown', h);
    return () => document.removeEventListener('mousedown', h);
  }, [branchPop, wtPop, buildMenu]);

  /* ---- eylemler ---- */
  const note = (t) => { eng.say('dim', t); bump(); };
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
  const pickBranch = (b) => {
    setBranchPop(false);
    if (b.name === branchSel) return;
    setBranchSel(b.name);
    setWtChosen(null);
    const frc = b.name !== activeBranch;
    if (frc) setWtOn(true);
    eng.targetSha = b.sha;
    eng.branchName = b.name;
    Object.keys(eng.p).forEach((n) => { eng.p[n].status = 'discovered'; eng.p[n].will = 'unknown'; });
    eng.willBuild = new Set();
    eng.phase = 'boot';
    eng.activeLine = null;
    eng.say('cmd', 'git switch --detach ' + b.sha + '  # ' + b.name + (frc ? ' (worktree required)' : ''));
    eng.say('info', 'Branch changed: ' + b.name + ' — Sync required');
    setSelected(null);
    bump();
  };
  const pickCfg = (v) => {
    if (eng.busy() || v === cfg) return;
    setCfg(v);
    eng.cfg = v;
    if (workspace && eng.phase !== 'boot' && eng.phase !== 'empty') {
      allDirtyRef.current = true;
      eng.allDirty = true;
      if (eng.willBuild.size) { eng._applySync(); }
      eng.say('warn', 'Configuration → ' + v + ' — all projects will rebuild');
    }
    bump();
  };
  const cyclePerf = () => {
    const order = ['Full', 'Balanced', 'Light'];
    const nx = order[(order.indexOf(perf) + 1) % 3];
    setPerf(nx);
    eng.maxPar = { Full: 6, Balanced: 4, Light: 2 }[nx];
    if (eng.phase === 'running') eng.say('dim', 'parallelism: ' + eng.maxPar + ' (' + nx + ')');
    bump();
  };
  const openWorkspace = (root) => {
    setWorkspace(true);
    resetEngine({});
    eng.say('cmd', 'workspace: ' + root + ' — ' + BO.PROJECTS.length + ' projects discovered');
    setRevealKey((k) => k + 1);
    eng.startSync();
    bump();
  };

  const c = eng.counts();
  const di = eng.depIssueCount();
  /* uyarı çipi: üçgen taşıyan satırlar (cycle ∪ dep sorunu) — tek birleşik kanal */
  const warnN = BO.PROJECTS.filter((p) => !!eng.p[p.name].depIssue || eng._isCycle(p.name)).length;
  const running = eng.phase === 'running';
  const stopped = eng.phase === 'stopped';
  const busy = eng.busy();
  const taskKind = eng.task ? eng.task.kind : null;
  const resolving = !!eng.resolveRun;
  const remainN = eng.willBuild.size - eng.finishedOfWB();
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
  const saveSettings = (root, cfg2, ext2) => {
    setLayerCfg(cfg2);
    setExtCfg(ext2 || []);
    try {
      localStorage.setItem('delta-bo-layers-v2', JSON.stringify(cfg2));
      localStorage.setItem('delta-bo-external-v1', JSON.stringify(ext2 || []));
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
    /* first run: ayarlar kaydedilince workspace açılır ve sync başlar */
    if (!workspace) { openWorkspace(root); return; }
    if (rootChanged) note('Repository root → ' + root + ' — Sync required');
    note(cfg2.length ? 'Layer definitions updated — ' + cfg2.length + ' layers' : 'Layers removed — single project list');
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
      <DS.TitleBar logoSrc={null} title="">
        <span style={{ marginRight: 'auto', display: 'inline-flex', alignItems: 'center', gap: 9, minWidth: 0 }}>
          <img src={logoUrl(logoBase, 'mark')} alt="Build Orchestrator" style={{ height: 19, display: 'block', flex: 'none' }} />
          <span style={{ fontSize: 'var(--text-xs)', fontWeight: 500, color: 'var(--text-secondary)', whiteSpace: 'nowrap' }}>Build Orchestrator</span>
          <span aria-hidden="true" style={{ width: 1, height: 13, background: 'var(--border)', flex: 'none' }}></span>
          <img src={logoUrl(logoBase, 'company')} alt="Delta" title="Delta" style={{ height: 10, display: 'block', opacity: 0.55, flex: 'none' }} />
        </span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: 2, paddingRight: 8, flex: 'none' }}>
          {[
            ['quad', 'Default layout — graph + list · console + stream (splits reset)', I.layQuad],
            ['list', 'List layout — graph hidden · console + stream equal', I.layList],
            ['focus', 'Console focus — graph hidden · compact stream', I.layFocus],
          ].map(([id, tip, Ic]) => (
            <DS.Tooltip key={id} content={tip} side="bottom">
              <DS.IconButton size="sm" aria-label={tip} active={layout.mode === id} onClick={() => setMode(id)}><Ic /></DS.IconButton>
            </DS.Tooltip>
          ))}
          <span aria-hidden="true" style={{ width: 1, height: 14, background: 'var(--border)', margin: '0 5px' }}></span>
          <DS.Tooltip content="Settings — repository root and layer definitions" side="bottom">
            <DS.IconButton size="sm" aria-label="Settings" active={settingsOpen} onClick={() => setSettingsOpen(true)}><I.gear /></DS.IconButton>
          </DS.Tooltip>
          <DS.Tooltip content={unseenNotes ? "What’s new in " + ABOUT_VERSION + " (Ctrl+F1)" : 'What’s new — release notes (Ctrl+F1)'} side="bottom">
            <span style={{ position: 'relative', display: 'inline-flex' }}>
              <DS.IconButton size="sm" aria-label="What’s new" active={notesOpen}
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
        </span>
      </DS.TitleBar>

      {/* ---- STICKY ŞERİT ---- */}
      <StickyStrip eng={eng} workspace={workspace} onSelect={select} onFilterFailed={() => { setSelected(null); setFilter(new Set(['failed'])); }} />

      {/* ---- GÖVDE: 2 kolon × 2 satır + splitters ---- */}
      <div ref={bodyRef} style={{ flex: 1, minHeight: 0, display: 'flex', alignItems: 'stretch' }}>
        {/* SOL KOLON */}
        <div style={{ width: layout.col + '%', minWidth: 0, display: 'flex', flexDirection: 'column' }}>
          {showGraph && (
            <>
              <div style={{ height: layout.left + '%', minHeight: 0, display: 'flex', flexDirection: 'column' }}>
                <PanelHead label="Dependency graph" right={workspace ? (
                  <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>
                    {BO.PROJECTS.length} projects · {BO.EDGE_N} dependencies
                  </span>
                ) : null} />
                <GraphPanel eng={eng} selected={selected} onSelect={select} revealKey={revealKey} workspace={workspace} filter={filter} />
              </div>
              <Splitter dir="h" onDrag={dragLeft} onEnd={endDrag} />
            </>
          )}
          <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column' }}>
            <PanelHead label="Projects" right={workspace ? (
              <DS.Input id="bo-proj-search" placeholder="Filter…" prefix={<I.search />} value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={(e) => { if (e.key === 'Escape') { setQuery(''); e.currentTarget.blur(); e.stopPropagation(); } }}
                style={{ width: 150, height: 20, fontSize: 'var(--text-xs)' }} />
            ) : null}>
              {workspace && <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', textTransform: 'none', letterSpacing: 0 }}>build-order</span>}
              {filter.size > 0 && (
                <DS.Chip active label={Array.from(filter).map((f) => FILTER_LABELS[f] || f).join(' + ')} onRemove={() => setFilter(new Set())} style={{ height: 20, padding: '0 6px', fontSize: 'var(--text-2xs)' }} />
              )}
            </PanelHead>
            <ListPanel eng={eng} groups={groups} selected={selected} onSelect={select} onNote={note}
              onRowAction={doRowAction} onStopRun={() => { eng.stop(); bump(); }}
              onImportSettings={() => { setSettingsImport(true); setSettingsOpen(true); }}
              filter={filter} query={query} follow={follow} revealKey={revealKey} workspace={workspace} layerCount={layerCfg.length} onOpenSettings={() => setSettingsOpen(true)} />
          </div>
        </div>

        <Splitter dir="v" onDrag={dragCol} onEnd={endDrag} />

        {/* SAĞ KOLON */}
        <div style={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column' }}>
          <div style={{ height: layout.right + '%', minHeight: 0, display: 'flex', flexDirection: 'column' }}>
            <PanelHead label={selected ? '' : 'Console'} right={workspace ? (
              <>
                {selected && selSt.log && selSt.log.length > 0 && (
                  <CopyLogBtn getText={() => selSt.log.map((l) => l.text).join('\n')} />
                )}
                <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>
                  {selected ? (selSt.log ? selSt.log.length + ' lines' : '') : eng.narrative.length + ' lines'}
                </span>
              </>
            ) : null}>
              {selected && (
                <>
                  <DS.Button size="sm" variant="ghost" icon={<I.back />} onClick={() => setSelected(null)} style={{ marginLeft: -6 }}>Back</DS.Button>
                  <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', color: 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{selected}</span>
                  <Glyph status={selSt.status} size={13} />
                  <span style={{ fontSize: 'var(--text-2xs)', color: DS.STATUS_META[selSt.status].color }}>{EN_STATUS[selSt.status] || selSt.status}</span>
                  {selSt.depIssue && (
                    <DS.Tooltip content={'Dependency issue: ' + selSt.depIssue.map(BO.shortName).join(', ') + ' — last successful output referenced'} side="bottom">
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, color: 'var(--amber-text)', fontSize: 'var(--text-2xs)', whiteSpace: 'nowrap', cursor: 'default' }}>
                        <I.alertTri /> dependency issue
                      </span>
                    </DS.Tooltip>
                  )}
                  {eng._isCycle(selected) && (
                    <DS.Tooltip content="In a dependency cycle" side="bottom">
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, color: 'var(--amber-text)', fontSize: 'var(--text-2xs)', whiteSpace: 'nowrap', cursor: 'default' }}>
                        <I.alertTri /> dependency cycle
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
      <div style={{
        flex: 'none', display: 'flex', alignItems: 'center', gap: 8, height: 42, padding: '0 10px',
        background: 'var(--surface)', borderTop: '1px solid var(--border)', position: 'relative', zIndex: 20,
      }}>
        <DS.Button size="sm" variant="secondary" icon={<I.sync />} onClick={doSync} disabled={!workspace || busy} style={{ flex: 'none' }}>Sync</DS.Button>
        {/* bakım grubu: Clean + Optimize — chip ağırlığında tek kutu, ikon butonlar */}
        <span style={{
          display: 'inline-flex', alignItems: 'center', height: 24, flex: 'none', overflow: 'hidden',
          border: '1px solid var(--border)', borderRadius: 'var(--radius-xs)', background: 'var(--surface-raised)',
        }}>
          <DS.Tooltip content="Clean — /t:Clean on every solution, then remove bin/, obj/, artifacts/" side="top">
            <DS.IconButton size="sm" aria-label="Clean" active={taskKind === 'clean'}
              disabled={!workspace || (busy && taskKind !== 'clean')} onClick={doClean}
              style={{ width: 28, height: 22, borderRadius: 0, border: 'none' }}>
              {taskKind === 'clean' ? <BuildingSpin size={12} /> : <I.eraser />}
            </DS.IconButton>
          </DS.Tooltip>
          <span aria-hidden="true" style={{ width: 1, height: 14, background: 'var(--border)', flex: 'none' }}></span>
          <DS.Tooltip content="Optimize — restore packages, prune the cache, rebuild the dependency index" side="top">
            <DS.IconButton size="sm" aria-label="Optimize" active={taskKind === 'optimize'}
              disabled={!workspace || (busy && taskKind !== 'optimize')} onClick={doOptimize}
              style={{ width: 28, height: 22, borderRadius: 0, border: 'none' }}>
              {taskKind === 'optimize' ? <BuildingSpin size={12} /> : <I.gauge />}
            </DS.IconButton>
          </DS.Tooltip>
          <span aria-hidden="true" style={{ width: 1, height: 14, background: 'var(--border)', flex: 'none' }}></span>
          {/* Resolve cycles — döngü üyeleri + bayat bağımlılıkları, iki geçişli ardışık derleme */}
          <DS.Tooltip side="top" content={c.cycle > 0
            ? 'Resolve cycles — build the ' + c.cycle + ' cycle projects in two passes: stale references first, then rebuild until they converge'
            : 'Resolve cycles — no dependency cycles detected'}>
            <DS.IconButton size="sm" aria-label="Resolve cycles" active={resolving} onClick={doResolve}
              disabled={!workspace || (busy && !resolving) || c.cycle === 0}
              style={{ width: 28, height: 22, borderRadius: 0, border: 'none' }}>
              {resolving ? <BuildingSpin size={12} /> : <I.unlink />}
            </DS.IconButton>
          </DS.Tooltip>
        </span>
        <span style={{ width: 1, alignSelf: 'stretch', margin: '10px 2px', background: 'var(--border-subtle)' }}></span>
        {/* filtre çipleri: bağımsız açılır-kapanır (VEYA); aktif çip kendi statü renginde yanar */}
        <DS.Tooltip content="All projects — clear filters" side="top">
          <DS.Chip icon={<I.sigma />} value={c.total} onClick={() => setFilter(new Set())} data-bo-chip="1" />
        </DS.Tooltip>
        <DS.Tooltip content="Building now — filter" side="top">
          <DS.Chip icon={c.building ? <BuildingSpin size={12} /> : <span style={{ width: 8, height: 8, borderRadius: '50%', background: 'var(--neutral-600)', display: 'inline-block' }}></span>}
            value={c.building} active={filter.has('building')} onClick={() => toggleFilter('building')} data-bo-chip="1"
            style={filter.has('building') ? { color: 'var(--amber-text)' } : undefined} />
        </DS.Tooltip>
        <DS.Tooltip content="Succeeded — filter" side="top">
          <DS.Chip icon={<DS.StatusGlyph status="succeeded" size={12} />} value={c.succeeded} active={filter.has('succeeded')} onClick={() => toggleFilter('succeeded')} data-bo-chip="1"
            style={filter.has('succeeded') ? { color: 'var(--status-success-text)' } : undefined} />
        </DS.Tooltip>
        <DS.Tooltip content="Failed — filter" side="top">
          <DS.Chip icon={<DS.StatusGlyph status="failed" size={12} />} value={c.failed} active={filter.has('failed')} onClick={() => toggleFilter('failed')} data-bo-chip="1"
            style={filter.has('failed') ? { color: 'var(--status-fail-text)' } : undefined} />
        </DS.Tooltip>
        <DS.Tooltip content="Skipped — filter" side="top">
          <DS.Chip icon={<DS.StatusGlyph status="skipped" size={12} />} value={c.skipped} active={filter.has('skipped')} onClick={() => toggleFilter('skipped')} data-bo-chip="1"
            style={filter.has('skipped') ? { color: 'var(--status-skipped-text)' } : undefined} />
        </DS.Tooltip>
        {warnN > 0 && (
          <DS.Tooltip content="Warnings — dependency cycle or dependency issue" side="top">
            <DS.Chip icon={<span style={{ display: 'inline-flex', color: 'var(--amber-text)' }}><I.alertTri size={12} /></span>}
              value={warnN} active={filter.has('warn')} onClick={() => toggleFilter('warn')} data-bo-chip="1"
              style={filter.has('warn') ? { color: 'var(--amber-text)' } : undefined} />
          </DS.Tooltip>
        )}

        <span style={{ flex: 1 }}></span>

        {workspace && (
          <DS.Tooltip content={repoRoot ? 'Workspace — ' + repoRoot : 'Workspace'} side="top">
            <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-dim)', whiteSpace: 'nowrap', cursor: 'default', marginRight: 2 }}>OSYS</span>
          </DS.Tooltip>
        )}
        <span style={{ position: 'relative' }} data-bo-chip="1">
          <DS.Chip icon={<I.branch />} label="branch" value={branchSel} chevron active={branchPop} disabled={!workspace || !!eng.task}
            onClick={() => { setBranchPop(!branchPop); setWtPop(false); }} />
          <BranchPopover open={branchPop} branches={BO.BRANCHES} current={branchSel} activeBranch={activeBranch} onPick={pickBranch} />
        </span>
        <span style={{ position: 'relative' }} data-bo-chip="1">
          <DS.Chip icon={<I.tree />} label="worktree" value={wtActive ? wtName : 'off'} chevron active={wtPop} disabled={!workspace || !!eng.task}
            onClick={() => { setWtPop(!wtPop); setBranchPop(false); }} />
          <WorktreePopover open={wtPop} align="left" forced={forced} on={wtActive} setOn={setWtOn} autoName={autoName}
            worktrees={worktrees} chosen={wtChosen} setChosen={setWtChosen}
            onDelete={(nm) => { setWorktrees((ws) => ws.filter((w) => w.name !== nm)); if (wtChosen === nm) setWtChosen(null); note('worktree removed: ' + nm); }}
            source={srcText} />
        </span>
        <DS.Segment size="sm" options={['Debug', 'Release']} value={cfg} onChange={pickCfg} />
        <DS.Chip label="perf" value={perf} onClick={cyclePerf} disabled={!workspace} data-bo-chip="1" />
        <span style={{ width: 1, alignSelf: 'stretch', margin: '10px 2px', background: 'var(--border-subtle)' }}></span>
        {running || eng.phase === 'marking' ? (
          <DS.Button variant="danger" size="md" icon={<I.stop />} onClick={() => { eng.stop(); bump(); }} style={{ flex: 'none' }}>Stop</DS.Button>
        ) : (
          <span style={{ position: 'relative', display: 'inline-flex', flex: 'none' }} data-bo-chip="1">
            <DS.Button variant="primary" size="md" icon={<I.play />} onClick={doBuild} disabled={!workspace || eng.phase === 'syncing' || !!eng.task}
              style={{ flex: 'none', borderTopRightRadius: 0, borderBottomRightRadius: 0 }}>Build</DS.Button>
            <DS.Button variant="primary" size="md" aria-label="Build options" disabled={!workspace || eng.phase === 'syncing' || !!eng.task}
              onClick={() => { setBuildMenu(!buildMenu); setBranchPop(false); setWtPop(false); }}
              style={{ flex: 'none', borderTopLeftRadius: 0, borderBottomLeftRadius: 0, padding: '0 5px', borderLeft: '1px solid var(--amber-dim)' }}>
              <I.chevUp />
            </DS.Button>
            <Popover open={buildMenu} width={276}>
              <BuildMenuItem icon={<I.play />} title="Build" desc={stopped ? remainN + ' stale projects — resumes where it stopped' : 'Only stale projects — changed, failed or never built'} kbd="F5"
                onPick={() => { setBuildMenu(false); doBuild(); }} />
              <BuildMenuItem icon={<I.rebuild />} title="Rebuild" desc={'All ' + BO.PROJECTS.length + ' projects — cache ignored'} kbd="Ctrl+F5"
                onPick={() => { setBuildMenu(false); doRebuild(); }} />
              <BuildMenuItem icon={<I.brush />} title="Clean" desc="Remove build outputs — next build is full"
                onPick={() => { setBuildMenu(false); doCleanSln(); }} />
            </Popover>
          </span>
        )}
      </div>

      {/* ---- AYARLAR ---- */}
      <SettingsDialog open={settingsOpen} cfg={layerCfg} extCfg={extCfg} root={repoRoot} setup={!workspace} importOnOpen={settingsImport}
        onClose={() => { setSettingsOpen(false); setSettingsImport(false); }} onSave={saveSettings} />

      {/* ---- ABOUT ---- */}
      <AboutDialog open={aboutOpen}
        onClose={() => setAboutOpen(false)} logoSrc={logoUrl(logoBase, 'mark')} companyLogoSrc={logoUrl(logoBase, 'company')} />

      {/* ---- WHAT'S NEW ---- */}
      <NotesDialog open={notesOpen}
        onSeen={() => { setUnseenNotes(false); try { localStorage.setItem(NOTES_SEEN_KEY, ABOUT_VERSION); } catch (e) { /* yok */ } }}
        onClose={() => setNotesOpen(false)} />
    </div>
  );
}

window.BuildApp = BuildApp;
