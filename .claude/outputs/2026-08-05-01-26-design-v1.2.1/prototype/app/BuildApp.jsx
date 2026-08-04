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
    .bo-cursor { animation: bo-blink 1.1s var(--ease-in-out) infinite; }
    .bo-rot { animation: bo-rot 1.4s linear infinite; transform-origin: center; transform-box: fill-box; }
    .bo-shake { animation: bo-shake .36s var(--ease-standard) 1; }
    .bo-glow-once { animation: bo-glow-once 1.1s var(--ease-out) 1; }
    .bo-edge-flow { stroke-dasharray: 4 7; animation: bo-dash .9s linear infinite; }
    .bo-pop-in { animation: bo-pop-in .14s var(--ease-out) both; }
    .bo-breath { animation: bo-breath 3.8s var(--ease-in-out) infinite; }
  }
  @media (prefers-reduced-motion: reduce) {
    .bo-edge-flow { stroke-dasharray: 4 7; }
  }
  @keyframes bo-reveal { from { opacity: 0; transform: translateY(-5px); } to { opacity: 1; transform: none; } }
  @keyframes bo-blink { 0%, 100% { opacity: 1; } 50% { opacity: 0.1; } }
  @keyframes bo-rot { to { transform: rotate(360deg); } }
  @keyframes bo-shake { 10%, 90% { transform: translateX(-2px); } 25%, 75% { transform: translateX(3px); } 50% { transform: translateX(-3px); } }
  @keyframes bo-glow-once { 0% { background: var(--status-success-soft); } 100% { background: transparent; } }
  @keyframes bo-dash { to { stroke-dashoffset: -22; } }
  @keyframes bo-pop-in { from { opacity: 0; transform: translateY(4px) scale(.985); } to { opacity: 1; transform: none; } }
  @keyframes bo-breath { 0%, 100% { opacity: 0; } 50% { opacity: 0.32; } }
  .bo-scroll { scrollbar-width: thin; scrollbar-color: var(--neutral-700) transparent; }
  .bo-scroll::-webkit-scrollbar { width: 10px; height: 10px; }
  .bo-scroll::-webkit-scrollbar-track { background: transparent; }
  .bo-scroll::-webkit-scrollbar-thumb { background: var(--neutral-700); border: 3px solid transparent; background-clip: padding-box; border-radius: 5px; }
  `;
  document.head.appendChild(s);
})();

/* ---------- ikonlar (Lucide geometrisi, currentColor) ---------- */
const I = {
  branch: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="6" x2="6" y1="3" y2="15"/><circle cx="18" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M18 9a9 9 0 0 1-9 9"/></svg>,
  tree: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M8 3v4a2 2 0 0 0 2 2h8"/><path d="M8 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h2"/><path d="M8 13h6a2 2 0 0 1 2 2v6"/></svg>,
  play: () => <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor" stroke="none"><polygon points="6 3 20 12 6 21 6 3"/></svg>,
  stop: () => <svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor" stroke="none"><rect x="5" y="5" width="14" height="14" rx="1"/></svg>,
  sync: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M21 12a9 9 0 0 0-9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M3 12a9 9 0 0 0 9 9 9.75 9.75 0 0 0 6.74-2.74L21 16"/><path d="M21 21v-5h-5"/></svg>,
  search: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/></svg>,
  folder: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/></svg>,
  folderOpen: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2"/></svg>,
  vs: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><polyline points="16 18 22 12 16 6"/><polyline points="8 6 2 12 8 18"/></svg>,
  back: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m12 19-7-7 7-7"/><path d="M19 12H5"/></svg>,
  trash: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M3 6h18"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>,
  check: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M20 6 9 17l-5-5"/></svg>,
  goto: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/></svg>,
  depWarn: () => <svg width="8" height="8" viewBox="0 0 24 24" fill="currentColor" stroke="none" aria-hidden="true"><path d="M12 3 23 21H1Z"/></svg>,
  alertTri: ({ size = 13 } = {}) => <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 20h16a2 2 0 0 0 1.73-2Z"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>,
  gear: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/></svg>,
  info: () => <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round"><circle cx="12" cy="12" r="9"/><path d="M12 11v5"/><path d="M12 7.6h.01"/></svg>,
  sigma: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><path d="M18 7V4H6l6 8-6 8h12v-3"/></svg>,
  chevUp: () => <svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m18 15-6-6-6 6"/></svg>,
  up: () => <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m18 15-6-6-6 6"/></svg>,
  down: () => <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="m6 9 6 6 6-6"/></svg>,
  plus: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M5 12h14"/><path d="M12 5v14"/></svg>,
  rot: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/></svg>,
  grip: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" stroke="none"><circle cx="9" cy="6" r="1.4"/><circle cx="9" cy="12" r="1.4"/><circle cx="9" cy="18" r="1.4"/><circle cx="15" cy="6" r="1.4"/><circle cx="15" cy="12" r="1.4"/><circle cx="15" cy="18" r="1.4"/></svg>,
  copy: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round"><rect width="14" height="14" x="8" y="8" rx="2" ry="2"/><path d="M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2"/></svg>,
  redo: () => <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><path d="M21 12a9 9 0 1 1-9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/></svg>,
  layQuad: () => <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.1"><rect x="1.7" y="2.7" width="5.5" height="4.5" rx="1"/><rect x="8.8" y="2.7" width="5.5" height="4.5" rx="1"/><rect x="1.7" y="8.8" width="5.5" height="4.5" rx="1"/><rect x="8.8" y="8.8" width="5.5" height="4.5" rx="1"/></svg>,
  layList: () => <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.1"><rect x="1.7" y="2.7" width="5.5" height="10.6" rx="1"/><rect x="8.8" y="2.7" width="5.5" height="4.5" rx="1"/><rect x="8.8" y="8.8" width="5.5" height="4.5" rx="1"/></svg>,
  layFocus: () => <svg width="15" height="15" viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.1"><rect x="1.7" y="2.7" width="5.5" height="10.6" rx="1"/><rect x="8.8" y="2.7" width="5.5" height="7.1" rx="1"/><rect x="8.8" y="11.4" width="5.5" height="1.9" rx="0.95"/></svg>,
};

const MONO = 'var(--font-mono)';
const fmtElapsed = (ms) => {
  const s = Math.max(0, Math.floor(ms / 1000));
  if (s < 60) return s + 's';
  return Math.floor(s / 60) + 'm ' + String(s % 60).padStart(2, '0') + 's';
};

/* ---------- imleç + daktilo satırı ---------- */
function Cursor({ style }) {
  return <span className="bo-cursor" style={{ display: 'inline-block', width: 7, height: 13, background: 'currentColor', verticalAlign: -2, flex: 'none', ...style }}></span>;
}

function TypingLine({ text, instant, cursor, style, onDone }) {
  const [n, setN] = React.useState(instant || REDUCED ? text.length : 0);
  React.useEffect(() => {
    let doneT = null;
    const fire = () => { if (onDone) doneT = setTimeout(onDone, 420); };
    if (instant || REDUCED) { setN(text.length); fire(); return () => { if (doneT) clearTimeout(doneT); }; }
    setN(0);
    const step = Math.max(1, Math.ceil(text.length / 22)); // satır ≤ ~250ms
    const iv = setInterval(() => setN((k) => {
      const nk = Math.min(text.length, k + step);
      if (nk >= text.length) { clearInterval(iv); fire(); }
      return nk;
    }), 11);
    return () => { clearInterval(iv); if (doneT) clearTimeout(doneT); };
  }, [text, instant]); // eslint-disable-line
  return <span style={style}>{text.slice(0, n)}{cursor && <Cursor style={{ marginLeft: 3 }} />}</span>;
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

/* ---------- konsol anlatı satırı: saat + ikon kolonu (▸ / imleç) + metin ---------- */
const NARR_COLORS = {
  info: 'var(--text-secondary)', success: 'var(--status-success-text)', warn: 'var(--status-cycle-text)',
  error: 'var(--status-fail-text)', cmd: 'var(--text-primary)', dim: 'var(--text-faint)',
};
function NarrLine({ type = 'info', time, cursor, children, style }) {
  return (
    <div style={{
      display: 'flex', gap: 10, alignItems: 'baseline',
      fontFamily: MONO, fontSize: 'var(--text-xs)', lineHeight: 'var(--leading-mono)',
      color: NARR_COLORS[type] || NARR_COLORS.info,
      whiteSpace: 'pre-wrap', wordBreak: 'break-word', fontVariantNumeric: 'tabular-nums',
      ...style,
    }}>
      {time && <span style={{ color: 'var(--text-faint)', flex: 'none' }}>{time}</span>}
      <span style={{ width: 10, flex: 'none', display: 'inline-flex', justifyContent: 'center' }}>
        {cursor ? <Cursor /> : type === 'cmd' ? <span style={{ color: 'var(--amber-text)' }}>▸</span> : null}
      </span>
      <span style={{ minWidth: 0 }}>{children}</span>
    </div>
  );
}

/* ---------- seçilen proje logu: remount ile sıfırdan kaskatla açılır (flash yok) ---------- */
function RevealLog({ lines }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [n, setN] = React.useState(REDUCED ? 9999 : 2);
  React.useEffect(() => {
    if (REDUCED) return;
    const iv = setInterval(() => setN((k) => {
      if (k > 900) { clearInterval(iv); return k; }
      return k + 3;
    }), 26);
    return () => clearInterval(iv);
  }, []);
  const shown = n >= lines.length ? lines : lines.slice(0, n);
  return (
    <>
      {shown.map((l, i) => (
        <div key={i} className={REDUCED ? undefined : 'bo-pop-in'}>
          <DS.ConsoleLine type={l.type}>{l.text}</DS.ConsoleLine>
        </div>
      ))}
    </>
  );
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
function GraphPanel({ eng, selected, onSelect, revealKey, workspace }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const G = BO.GRAPH;
  const boxRef = React.useRef(null);
  const [dim, setDim] = React.useState({ w: 640, h: 360 });
  React.useEffect(() => {
    const el = boxRef.current; if (!el) return;
    const ro = new ResizeObserver(() => setDim({ w: el.clientWidth, h: el.clientHeight }));
    ro.observe(el);
    setDim({ w: el.clientWidth, h: el.clientHeight });
    return () => ro.disconnect();
  }, []);

  if (!workspace) {
    return (
      <div ref={boxRef} style={{ flex: 1, minHeight: 0, display: 'flex', alignItems: 'center', justifyContent: 'center', background: 'var(--surface-base)' }}>
        <div style={{ border: '1px dashed var(--border)', borderRadius: 'var(--radius-md)', padding: '18px 26px', color: 'var(--text-faint)', fontSize: 'var(--text-xs)' }}>
          Graph appears after Sync
        </div>
      </div>
    );
  }

  const building = eng.buildingList();
  let cx = G.W / 2, cy = G.H * 0.3;
  if (selected && G.nodes[selected]) { cx = G.nodes[selected].x; cy = G.nodes[selected].y; }
  else if (building.length) {
    cx = building.reduce((a, n) => a + G.nodes[n].x, 0) / building.length;
    cy = building.reduce((a, n) => a + G.nodes[n].y, 0) / building.length;
  } else if (eng.phase === 'done' || eng.phase === 'stopped') { cx = G.W / 2; cy = G.H / 2; }

  const s = Math.max(0.68, Math.min(1.08, Math.min(dim.w / (G.W + 30), dim.h / (G.H + 30))));
  let tx = dim.w / 2 - cx * s, ty = dim.h / 2 - cy * s;
  if (G.W * s <= dim.w) tx = (dim.w - G.W * s) / 2; else tx = Math.min(12, Math.max(dim.w - G.W * s - 12, tx));
  if (G.H * s <= dim.h) ty = (dim.h - G.H * s) / 2; else ty = Math.min(12, Math.max(dim.h - G.H * s - 12, ty));

  const neigh = new Set();
  if (selected) {
    neigh.add(selected);
    G.edges.forEach(([a, b]) => { if (a === selected) neigh.add(b); if (b === selected) neigh.add(a); });
  }
  const st = (n) => eng.p[n].status;

  return (
    <div ref={boxRef} onClick={(e) => { if (e.target === boxRef.current) onSelect(null); }}
      style={{ flex: 1, minHeight: 0, overflow: 'hidden', position: 'relative', background: 'var(--surface-base)' }}>
      <div key={revealKey} style={{
        position: 'absolute', left: 0, top: 0, width: G.W, height: G.H,
        transform: `translate(${Math.round(tx)}px,${Math.round(ty)}px) scale(${s})`, transformOrigin: '0 0',
        transition: REDUCED ? 'none' : 'transform 460ms var(--ease-in-out)', pointerEvents: 'none',
      }}>
        <svg width={G.W} height={G.H} style={{ position: 'absolute', inset: 0, overflow: 'visible' }} aria-hidden="true">
          {G.edges.map(([a, b]) => {
            const A = G.nodes[a], Bp = G.nodes[b];
            const flow = st(b) === 'building';
            const bad = st(a) === 'failed' || !!eng.p[a].depIssue; // hata bu daldan aşağı taşınıyor
            const hot = selected && (a === selected || b === selected);
            let stroke = 'var(--border)', w = 1, op = selected ? 0.16 : 0.8, cls, dash;
            if (flow) { stroke = 'var(--amber)'; op = selected ? 0.2 : 0.85; cls = 'bo-edge-flow'; }
            else if (st(b) === 'succeeded') { stroke = 'var(--status-success-border)'; }
            else if (st(b) === 'failed') { stroke = 'var(--status-fail-border)'; }
            if (bad) { stroke = 'var(--status-fail-border)'; op = selected ? 0.3 : 0.95; if (!flow) { cls = undefined; dash = '3 4'; } }
            if (hot) { stroke = bad ? 'var(--status-fail-border)' : 'var(--amber-border)'; w = 1.6; op = 1; if (!bad) cls = undefined; }
            const d = `M ${A.x} ${A.y + 15} C ${A.x} ${A.y + 54}, ${Bp.x} ${Bp.y - 54}, ${Bp.x} ${Bp.y - 15}`;
            return <path key={a + '>' + b} d={d} fill="none" stroke={stroke} strokeWidth={w} opacity={op} strokeDasharray={dash} className={cls} />;
          })}
        </svg>
        {BO.PROJECTS.map((p, i) => {
          const dimmed = selected && !neigh.has(p.name);
          return (
            <div key={p.name}
              className={REDUCED ? undefined : 'bo-reveal'}
              style={{
                position: 'absolute', left: G.nodes[p.name].x, top: G.nodes[p.name].y - 13,
                transform: 'translateX(-50%)', pointerEvents: 'auto',
                opacity: dimmed ? 0.25 : 1, transition: 'opacity var(--duration-base) var(--ease-standard)',
                animationDelay: REDUCED ? undefined : Math.min(p.layer * 55, 330) + 'ms',
              }}>
              <DS.DependencyGraphNode
                label={BO.shortName(p.name)}
                status={st(p.name)}
                selected={selected === p.name}
                size={26}
                onClick={() => onSelect(selected === p.name ? null : p.name)}
              />
              {eng.p[p.name].depIssue && (
                <span aria-hidden="true" style={{
                  position: 'absolute', top: -6, left: 'calc(50% + 7px)', width: 13, height: 13, boxSizing: 'border-box',
                  borderRadius: '50%', background: 'var(--surface-base)', border: '1px solid var(--status-fail-border)',
                  display: 'flex', alignItems: 'center', justifyContent: 'center', color: 'var(--status-fail-text)',
                  pointerEvents: 'none', zIndex: 2,
                }}><I.depWarn /></span>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

/* ================= PROJE LİSTESİ ================= */
const FILTER_LABELS = { building: 'Building', succeeded: 'Succeeded', failed: 'Failed', skipped: 'Skipped', dep: 'Dependency issues' };
const EN_STATUS = { discovered: 'Discovered', queued: 'Queued', building: 'Building', succeeded: 'Succeeded', failed: 'Failed', skipped: 'Skipped', cycle: 'Cycle' };
function Row({ p, eng, selected, onSelect, onNote, revealIndex }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [hover, setHover] = React.useState(false);
  const st = eng.p[p.name];
  const meta = DS.STATUS_META[st.status] || DS.STATUS_META.discovered;
  const dim = st.status === 'skipped' || st.status === 'discovered';
  const isSel = selected === p.name;
  const shake = eng.lastFail && eng.lastFail.name === p.name && (eng.simT - eng.lastFail.t) < 700;
  const STRIP = {
    discovered: 'transparent', queued: 'var(--status-queued)', building: 'var(--amber)',
    succeeded: 'var(--status-success)', failed: 'var(--status-fail)', skipped: 'var(--status-skipped)', cycle: 'var(--status-cycle)',
  };
  return (
    <div role="row" tabIndex={0}
      onClick={() => onSelect(isSel ? null : p.name)}
      onKeyDown={(e) => { if (e.key === 'Enter') onSelect(isSel ? null : p.name); }}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      className={[REDUCED ? '' : 'bo-reveal', shake && !REDUCED ? 'bo-shake' : ''].join(' ').trim() || undefined}
      style={{
        display: 'flex', alignItems: 'center', gap: 8, height: 'var(--row-height)', boxSizing: 'border-box',
        padding: '0 10px 0 0', position: 'relative', cursor: 'pointer', userSelect: 'none',
        background: isSel ? 'var(--surface-raised)' : hover ? 'var(--surface-hover)' : 'transparent',
        borderBottom: '1px solid var(--border-subtle)',
        transition: 'background var(--duration-fast) var(--ease-standard)',
        animationDelay: REDUCED ? undefined : Math.min(revealIndex * 10, 380) + 'ms',
      }}>
      {st.status === 'building' && !REDUCED && (
        <span aria-hidden="true" className="bo-breath" style={{ position: 'absolute', inset: 0, background: 'var(--amber-soft)', opacity: 0, pointerEvents: 'none' }}></span>
      )}
      <span aria-hidden="true" style={{
        width: isSel ? 3 : 2, alignSelf: 'stretch', flex: 'none',
        background: isSel && st.status === 'discovered' ? 'var(--amber)' : STRIP[st.status],
        transition: 'width var(--duration-instant) var(--ease-standard)',
      }}></span>
      <span style={{
        display: 'flex', alignItems: 'center', gap: 8, minWidth: 0, flex: 1,
        transform: isSel ? 'translateX(4px)' : 'none', transition: 'transform var(--duration-fast) var(--ease-out)',
      }}>
        <DS.WillBuildDot state={st.will} style={{ marginLeft: 4 }} />
        <span style={{ display: 'flex', alignItems: 'baseline', gap: 8, minWidth: 0, flex: 1 }}>
          <span style={{ fontSize: 'var(--text-sm)', fontWeight: 500, color: dim ? 'var(--text-dim)' : 'var(--text-primary)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{p.name}</span>
          <span style={{ fontSize: 'var(--text-xs)', color: 'var(--text-faint)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>{p.sln}</span>
        </span>
      </span>
      {/* sağ blok: hover'da aç-ikonları, değilse commit mini metni */}
      <span style={{ display: 'flex', alignItems: 'center', gap: 2, flex: 'none', minWidth: 118, justifyContent: 'flex-end' }}>
        {hover ? (
          <span style={{ display: 'flex', gap: 2 }}>
            <DS.Tooltip content="Reveal in Explorer" side="left">
              <DS.IconButton size="sm" title="Reveal in Explorer" onClick={(e) => { e.stopPropagation(); onNote(p.name + '.csproj revealed in Explorer'); }}><I.folderOpen /></DS.IconButton>
            </DS.Tooltip>
            <DS.Tooltip content="Open in Visual Studio" side="left">
              <DS.IconButton size="sm" title="Open in Visual Studio" onClick={(e) => { e.stopPropagation(); onNote(p.name + ' opened in Visual Studio'); }}><I.vs /></DS.IconButton>
            </DS.Tooltip>
          </span>
        ) : st.will === 'dirty' ? (
          <span style={{ fontFamily: MONO, fontSize: 10.5, color: 'var(--text-faint)', whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' }}>
            {st.curSha} → {eng.targetSha}
          </span>
        ) : null}
      </span>
      <DS.Tooltip content={(EN_STATUS[st.status] || st.status) + (st.status === 'building' ? ' — ' + fmtElapsed(eng.simT - st.startAt) : st.depIssue ? ' — dependency issue' : '')} side="left">
        <Glyph status={st.status} size={14} />
      </DS.Tooltip>
      <span style={{ width: 14, display: 'inline-flex', justifyContent: 'center', flex: 'none' }}>
        {st.depIssue && (
          <DS.Tooltip content={'Failed dependency: ' + st.depIssue.map(BO.shortName).join(', ') + ' — last successful output referenced'} side="left">
            <span aria-label="dependency issue" style={{ display: 'inline-flex', color: 'var(--status-fail-text)', cursor: 'default' }}><I.alertTri size={12} /></span>
          </DS.Tooltip>
        )}
      </span>
      <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', fontVariantNumeric: 'tabular-nums', color: st.status === 'failed' ? 'var(--status-fail-text)' : 'var(--text-dim)', minWidth: 46, textAlign: 'right', whiteSpace: 'nowrap' }}>
        {st.status === 'building' ? fmtElapsed(eng.simT - st.startAt) : st.doneDur ? BO.fmtDur(st.doneDur) : '—'}
      </span>
    </div>
  );
}

function ListPanel({ eng, groups, selected, onSelect, onNote, filter, query, follow, revealKey, workspace, onPickFolder }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const boxRef = React.useRef(null);
  const rowRefs = React.useRef({});
  const lastScrollT = React.useRef(0);

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

  if (!workspace) {
    return (
      <div style={{ flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', gap: 10, background: 'var(--surface-base)', padding: 24 }}>
        <div style={{ fontSize: 'var(--text-md)', fontWeight: 600, color: 'var(--text-primary)' }}>Pick a repository to get started</div>
        <div style={{ fontSize: 'var(--text-xs)', color: 'var(--text-dim)', textAlign: 'center', maxWidth: 300, lineHeight: 'var(--leading-snug)' }}>
          Point to the OSYS solution root — projects and the dependency graph are discovered automatically.
        </div>
        <DS.Button variant="primary" icon={<I.folder />} onClick={onPickFolder} style={{ marginTop: 6 }}>Choose Folder</DS.Button>
      </div>
    );
  }

  const q = (query || '').trim().toLowerCase();
  const match = (name) => {
    if (q && name.toLowerCase().indexOf(q) < 0) return false;
    if (!filter) return true;
    const st2 = eng.p[name];
    if (filter === 'dep') return !!st2.depIssue;
    if (filter === 'building') return st2.status === 'building' || st2.status === 'queued';
    return st2.status === filter;
  };
  let revealIndex = 0;

  return (
    <div ref={boxRef} className="bo-scroll" style={{ flex: 1, minHeight: 0, overflowY: 'auto', position: 'relative', background: 'var(--surface-base)' }}>
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
                <div key={p.name} ref={(el) => { rowRefs.current[p.name] = el; }}>
                  <Row p={p} eng={eng} selected={selected} onSelect={onSelect} onNote={onNote} revealIndex={revealIndex++} />
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

  /* en yeni anlatı satırı daktilo gibi yazılır — imleç yazıyla birlikte akar */
  const [typingId, setTypingId] = React.useState(null);
  const prevLastId = React.useRef(null);
  const lastNarrId = !sel && lines && lines.length ? lines[lines.length - 1].id : null;
  React.useEffect(() => {
    if (sel) { prevLastId.current = null; setTypingId(null); return; }
    if (lastNarrId == null) { prevLastId.current = null; setTypingId(null); return; }
    if (prevLastId.current != null && lastNarrId !== prevLastId.current && !REDUCED) setTypingId(lastNarrId);
    prevLastId.current = lastNarrId;
  }, [lastNarrId, sel]);
  /* bayat typingId (önceki koşudan) prompt'u bastırmasın — yalnız listede var olan id geçerli */
  const typingLive = typingId != null && !sel && lines && lines.some((l) => l.id === typingId) ? typingId : null;

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
          <DS.ConsoleLine type="dim">
            {sel.status === 'skipped' ? 'Skipped — up to date; not built in this run. Last successful build: yesterday 18:42 (' + sel.curSha + ')'
              : sel.status === 'queued' ? 'Queued — waiting for dependencies: ' + sel.def.deps.filter((d) => eng.p[d].status !== 'succeeded' && eng.p[d].status !== 'skipped').map(BO.shortName).join(', ')
              : 'No log yet — output streams here once the build starts.'}
          </DS.ConsoleLine>
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
        {lines.map((l) => (
          <NarrLine key={l.id} type={l.type} time={l.time} cursor={l.id === typingLive}>
            {l.id === typingLive
              ? <TypingLine key={'t' + l.id} text={l.text} onDone={() => setTypingId((cur) => (cur === l.id ? null : cur))} />
              : l.text}
          </NarrLine>
        ))}
        {typingLive == null && (
          <NarrLine type="dim" time={eng.wall()} cursor>{eng.phase === 'idle' || eng.phase === 'boot' ? 'ready' : ''}</NarrLine>
        )}
      </>
    );
  }

  return (
    <div style={{ flex: 1, minHeight: 0, position: 'relative', display: 'flex', flexDirection: 'column' }}>
      <div ref={boxRef} onScroll={onScroll} className="bo-scroll" style={{
        flex: 1, minHeight: 0, overflowY: 'auto', background: 'var(--console-bg)',
        borderRadius: 0, padding: '8px 12px', boxSizing: 'border-box',
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
    : ev.kind === 'done' ? (c.failed ? 'failed' : 'succeeded') : null;
  const isSel = ev.project && ev.project === selected;
  const clickable = !!ev.project;
  const color = ev.kind === 'fail' ? 'var(--status-fail-text)'
    : ev.kind === 'skip' ? 'var(--text-faint)'
    : ev.kind === 'done' ? (c.failed ? 'var(--status-fail-text)' : 'var(--status-success-text)')
    : ev.kind === 'sync' || ev.kind === 'info' ? 'var(--text-dim)' : 'var(--text-secondary)';
  return (
    <div
      onClick={clickable ? () => onSelect(isSel ? null : ev.project) : undefined}
      onMouseEnter={() => setHover(true)} onMouseLeave={() => setHover(false)}
      className={ev.kind === 'done' && !c.failed && !REDUCED ? 'bo-glow-once' : undefined}
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
        {typing ? <TypingLine key={'t' + ev.id} text={ev.text} onDone={onTypeDone} /> : ev.text}
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
        {workspace && !active && pendingId == null && (
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

/* ================= STICKY ŞERİT + GLOBAL PROGRESS ================= */
function StickyStrip({ eng, onSelect, onFilterFailed, workspace }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const c = eng.counts();
  const di = eng.depIssueCount();
  const wb = eng.willBuild.size;
  const fin = eng.finishedOfWB();
  const failed = eng.failedList();
  const wn = eng.phase === 'done' ? eng.warnings() : 0;

  let text, color = 'var(--text-secondary)', glyph = null;
  if (!workspace) { text = 'Not ready — no repository selected'; color = 'var(--text-faint)'; }
  else if (eng.phase === 'boot') { text = '▸ Waiting for Sync — project states appear after Sync'; color = 'var(--text-dim)'; }
  else if (eng.phase === 'syncing') { text = '▸ Sync — git fetch origin…'; }
  else if (eng.phase === 'idle') {
    text = eng.allClean ? '▸ Ready — everything looks up to date' : `▸ Ready — ${wb} to build · ${36 - wb} up to date`;
  } else if (eng.phase === 'running') {
    if (eng.allClean) { text = '▸ Checking — scanning for changes…'; }
    else {
      const eta = eng.eta();
      const etaTxt = eta != null && c.building + c.queued > 0 ? (eta < 4000 ? ' · almost done' : ' · ~' + Math.max(5, Math.round(eta / 5000) * 5) + 's left') : '';
      text = `▸ Building ${fin}/${wb} · ${fmtElapsed(eng.elapsed())}${etaTxt}`;
    }
  } else if (eng.phase === 'stopped') { text = `▸ Stopped — ${fin}/${wb} · rest queued`; color = 'var(--text-dim)'; }
  else if (eng.phase === 'done') {
    if (eng.allClean) { text = `Everything up to date — 36 projects checked in ${BO.fmtDur(eng.checkDur)}, nothing to build`; color = 'var(--status-success-text)'; glyph = 'succeeded'; }
    else if (c.failed) { text = `Completed — ${c.failed} failed · ${c.succeeded} succeeded${di ? ` (${di} dependency-affected)` : ''} · ${c.skipped} skipped${wn ? ` · ${wn} warnings` : ''} · ${fmtElapsed(eng.elapsed())}`; color = 'var(--status-fail-text)'; glyph = 'failed'; }
    else { text = `Completed — ${c.succeeded} succeeded · ${c.skipped} skipped${wn ? ` · ${wn} warnings` : ''} · ${fmtElapsed(eng.elapsed())}`; color = 'var(--status-success-text)'; glyph = 'succeeded'; }
  }

  const building = eng.buildingList();
  const progress = eng.allClean
    ? (eng.phase === 'done' ? 100 : (c.skipped / 36) * 100)
    : wb ? (fin / wb) * 100 : 0;
  const pStatus = c.failed ? 'failed' : (eng.phase === 'done' ? 'succeeded' : 'building');

  return (
    <div style={{ flex: 'none', background: 'var(--surface-base)', borderBottom: '1px solid var(--border-subtle)', userSelect: 'none' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, height: 32, padding: '0 12px' }}>
        {glyph && <DS.StatusGlyph status={glyph} size={13} />}
        <span style={{ fontFamily: MONO, fontSize: 'var(--text-xs)', fontVariantNumeric: 'tabular-nums', color, whiteSpace: 'nowrap' }}>{text}</span>
        <span style={{ display: 'flex', gap: 4, alignItems: 'center', minWidth: 0, overflow: 'hidden' }}>
          {building.slice(0, 4).map((n) => (
            <DS.Chip key={n} icon={<BuildingSpin size={10} />} label={BO.shortName(n)}
              onClick={() => onSelect(n)} style={{ height: 20, padding: '0 6px', fontSize: 'var(--text-2xs)' }} />
          ))}
          {building.length > 4 && <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>+{building.length - 4}</span>}
        </span>
        <span style={{ flex: 1 }}></span>
        {failed.length > 0 && (
          <span style={{ display: 'flex', alignItems: 'center', gap: 6, whiteSpace: 'nowrap', minWidth: 0 }}>
            <DS.StatusGlyph status="failed" size={13} />
            <span style={{ fontSize: 'var(--text-xs)', color: 'var(--status-fail-text)', fontWeight: 500 }}>{failed.length} failed</span>
            {di > 0 && <span style={{ fontSize: 'var(--text-2xs)', color: 'var(--text-dim)' }}>· {di} dependency-affected</span>}
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
          </span>
        )}
      </div>
      <DS.ProgressBar value={progress} status={pStatus} height={2}
        indeterminate={eng.phase === 'syncing'}
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
  { id: 5, label: 'First run', desc: 'Empty state: no repo selected; warm invite + Choose Folder.' },
  { id: 6, label: 'Ready', desc: 'Idle + will-build dots; branch and worktree pickers open.' },
  { id: 7, label: 'Graph focus', desc: 'Graph enlarged, node selected. Reduced-motion: static layout, color only.' },
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
  if (!cfg || !cfg.length) return [{ name: null, rows: BO.PROJECTS.slice() }]; // katman yok → başlıksız tek liste
  const compiled = cfg.map((l) => { try { return l.rx && l.rx.trim() ? new RegExp(l.rx, 'i') : null; } catch (e) { return null; } });
  const groups = cfg.map((l) => ({ name: l.name, rows: [] }));
  const rest = { name: 'Other', rows: [] };
  BO.PROJECTS.forEach((p) => {
    const i = compiled.findIndex((r) => r && r.test(p.name));
    (i >= 0 ? groups[i] : rest).rows.push(p);
  });
  if (rest.rows.length) groups.push(rest);
  return groups.filter((g) => g.rows.length);
}

/* ---- ABOUT (F1) ---- */
const ABOUT_VERSION = '1.2.1';
const ABOUT_SHORTCUTS = [
  ['Build — or Stop while a run is in flight', ['F5']],
  ['Rebuild — all projects, cache ignored', ['Ctrl+F5', 'Shift+F5']],
  ['Focus the project filter', ['Ctrl+F']],
  ['About — version, shortcuts and diagnostics', ['F1']],
  ['Close the topmost open layer: dialog → popover/menu → selection', ['Esc']],
  ['Global — bring the window back from the tray', ['Alt+B']],
];
const ABOUT_ENV = [
  ['App version', ABOUT_VERSION],
  ['Engine version', ABOUT_VERSION],
  ['Engine PID', '41124'],
  ['.NET runtime', '.NET 10.0.10'],
  ['OS', 'Microsoft Windows 10.0.26200'],
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

function AboutDialog({ open, onClose, logoSrc, companyLogoSrc }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [tab, setTab] = React.useState('Shortcuts');
  const [copied, setCopied] = React.useState(false);
  const tRef = React.useRef(null);
  React.useEffect(() => { if (open) { setTab('Shortcuts'); setCopied(false); } }, [open]);
  React.useEffect(() => () => { if (tRef.current) clearTimeout(tRef.current); }, []);
  if (!open) return null;

  const diagnostics = () => ['Build Orchestrator ' + ABOUT_VERSION]
    .concat(ABOUT_ENV.map((r) => r[0] + ': ' + r[1])).join('\n');
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
          width: 620, maxWidth: 'calc(100% - 48px)', maxHeight: 'calc(100% - 48px)', display: 'flex', flexDirection: 'column',
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
        <div style={{ padding: '14px 18px 0' }}>
          <DS.Segment size="sm" options={['Shortcuts', 'Environment', 'Third-party']} value={tab} onChange={setTab} />
        </div>
        <div style={{ padding: '14px 18px 18px', overflowY: 'auto', minHeight: 236 }}>
          {tab === 'Shortcuts' && ABOUT_SHORTCUTS.map(([label, keys]) => (
            <div key={label} style={rowBase}>
              <span style={{ flex: 1, fontSize: 'var(--text-sm)', color: 'var(--text-secondary)' }}>{label}</span>
              <span style={{ display: 'flex', gap: 4, flex: 'none' }}>{keys.map((k) => <DS.Kbd key={k}>{k}</DS.Kbd>)}</span>
            </div>
          ))}
          {tab === 'Environment' && ABOUT_ENV.map(([label, val]) => (
            <div key={label} style={rowBase}>
              <span style={{ width: 130, flex: 'none', fontSize: 'var(--text-xs)', color: 'var(--text-dim)' }}>{label}</span>
              <span style={{ ...monoVal, flex: 1 }} title={val}>{val}</span>
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

function SettingsDialog({ open, cfg, onClose, onSave }) {
  const DS = window.DeltaBuildOrchestratorDS_eb0bd1;
  const [draft, setDraft] = React.useState(cfg);
  const dragRef = React.useRef(null);
  const [dragIdx, setDragIdx] = React.useState(null);
  const [dragOff, setDragOff] = React.useState(0);
  React.useEffect(() => { if (open) { setDraft(cfg.map((l) => ({ ...l }))); dragRef.current = null; setDragIdx(null); setDragOff(0); } }, [open]); // eslint-disable-line
  if (!open) return null;
  const compiled = draft.map((l) => { try { return l.rx && l.rx.trim() ? new RegExp(l.rx, 'i') : null; } catch (e) { return undefined; } });
  const upd = (i, patch) => setDraft((d) => d.map((l, k) => (k === i ? { ...l, ...patch } : l)));
  const swap = (arr, a, b) => { const nd = arr.slice(); const t = nd[a]; nd[a] = nd[b]; nd[b] = t; return nd; };

  /* kart sürükle-bırak sıralama */
  const ROWH = 42; // 36 kart + 6 boşluk
  const startDrag = (e, i) => {
    e.preventDefault();
    e.currentTarget.setPointerCapture(e.pointerId);
    dragRef.current = { idx: i, startY: e.clientY };
    setDragIdx(i); setDragOff(0);
  };
  const moveDrag = (e) => {
    const d = dragRef.current; if (!d) return;
    let off = e.clientY - d.startY;
    while (off > ROWH / 2 && d.idx < draft.length - 1) { const i = d.idx; setDraft((arr) => swap(arr, i, i + 1)); d.idx++; d.startY += ROWH; off -= ROWH; }
    while (off < -ROWH / 2 && d.idx > 0) { const i = d.idx; setDraft((arr) => swap(arr, i, i - 1)); d.idx--; d.startY -= ROWH; off += ROWH; }
    setDragIdx(d.idx); setDragOff(off);
  };
  const endDragRow = () => { dragRef.current = null; setDragIdx(null); setDragOff(0); };

  const valid = draft.every((l, i) => l.name.trim() && compiled[i] !== undefined);
  const caps = { fontSize: 'var(--text-2xs)', fontWeight: 500, letterSpacing: 'var(--tracking-caps)', textTransform: 'uppercase', color: 'var(--text-faint)' };
  return (
    <DS.Dialog open title="Settings" width={620} onClose={onClose} footer={
      <>
        <DS.Button variant="ghost" onClick={() => setDraft(SAMPLE_LAYER_CFG.map((l) => ({ ...l })))}>Load sample layers</DS.Button>
        <span style={{ flex: 1 }}></span>
        <DS.Button variant="secondary" onClick={onClose}>Cancel</DS.Button>
        <DS.Button variant="primary" disabled={!valid} onClick={() => onSave(draft.map((l) => ({ name: l.name.trim(), rx: l.rx })))}>Save</DS.Button>
      </>
    }>
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
            onPointerDown={(e) => startDrag(e, i)} onPointerMove={moveDrag} onPointerUp={endDragRow} onPointerCancel={endDragRow}
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
  if (!engRef.current) engRef.current = new BO.SimEngine();
  const eng = engRef.current;

  const [, setV] = React.useState(0);
  const bump = () => setV((v) => v + 1);
  const seedRef = React.useRef(0);
  const speedRef = React.useRef(simSpeed);
  speedRef.current = simSpeed || 1;

  const [workspace, setWorkspace] = React.useState(true);
  const [selected, setSelected] = React.useState(null);
  const [filter, setFilter] = React.useState(null);
  const [query, setQuery] = React.useState('');
  const [revealKey, setRevealKey] = React.useState(0);
  const [cfg, setCfg] = React.useState('Debug');
  const [perf, setPerf] = React.useState('Balanced');
  const allDirtyRef = React.useRef(false);

  const activeBranch = 'main';
  const [branchSel, setBranchSel] = React.useState('main');
  const [branchPop, setBranchPop] = React.useState(false);
  const [wtPop, setWtPop] = React.useState(false);
  const [wtOn, setWtOn] = React.useState(false);
  const [wtChosen, setWtChosen] = React.useState(null); // null = otomatik
  const [worktrees, setWorktrees] = React.useState(BO.WORKTREES.slice());

  const [settingsOpen, setSettingsOpen] = React.useState(false);
  const [aboutOpen, setAboutOpen] = React.useState(false);
  const [buildMenu, setBuildMenu] = React.useState(false);
  const [layerCfg, setLayerCfg] = React.useState(() => {
    try { const v = JSON.parse(localStorage.getItem('delta-bo-layers-v2')); if (Array.isArray(v)) return v; } catch (e) { /* yok */ }
    return DEFAULT_LAYER_CFG;
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
    const last = { t: Date.now() };
    const iv = setInterval(() => {
      const now = Date.now();
      let dt = Math.min(20000, now - last.t);
      last.t = now;
      const e = engRef.current;
      if (!(e.phase === 'syncing' || e.phase === 'running' || e.todo.length)) return;
      const sp = speedRef.current;
      while (dt > 0 && (e.phase === 'syncing' || e.phase === 'running' || e.todo.length)) {
        const step = Math.min(150, dt);
        e.advance(step * sp);
        dt -= step;
      }
      bump();
    }, 120);
    return () => clearInterval(iv);
  }, []);

  /* ---- ortak akışlar ---- */
  const bootLine = (e) => e.say('dim', 'Build Orchestrator 2.4.1 — Osys.sln loaded (36 projects) · ' + branchSel);
  const resetEngine = (opts) => {
    eng.reset({ seed: ++seedRef.current, cfg, maxPar: { Full: 6, Balanced: 4, Light: 2 }[perf], targetSha: (BO.BRANCHES.find((b) => b.name === branchSel) || {}).sha, ...opts });
    eng.branchName = branchSel;
    if (!opts || !opts.empty) bootLine(eng);
  };
  const doSync = () => {
    if (eng.phase === 'running' || !workspace) return;
    resetEngine({ allDirty: allDirtyRef.current });
    setSelected(null);
    setRevealKey((k) => k + 1);
    eng.startSync();
    bump();
  };
  const doBuild = () => {
    if (!workspace) return;
    setBuildMenu(false);
    resetEngine({ allDirty: allDirtyRef.current, allClean: eng.allClean && eng.phase === 'done' ? false : false });
    allDirtyRef.current = false;
    setSelected(null);
    setFilter(null);
    setRevealKey((k) => k + 1);
    eng.startSync();
    eng.at(1250, () => eng.startRun());
    bump();
  };
  const doRebuild = () => {
    if (!workspace || eng.phase === 'running' || eng.phase === 'syncing') return;
    allDirtyRef.current = true;
    doBuild();
    eng.say('warn', 'Rebuild — cache ignored, all 36 projects queued');
  };
  const doContinue = () => {
    if (!workspace || eng.phase !== 'stopped') return;
    setBuildMenu(false);
    eng.continueRun();
    bump();
  };
  const doRetry = () => {
    if (!workspace || eng.phase === 'running' || eng.phase === 'syncing') return;
    setBuildMenu(false);
    setSelected(null);
    setFilter(null);
    eng.startRetry();
    bump();
  };

  /* ---- sahneler ---- */
  const applyScene = React.useCallback((n) => {
    const e = engRef.current;
    setBranchPop(false); setWtPop(false); setBuildMenu(false); setSettingsOpen(false); setAboutOpen(false); setFilter(null); setSelected(null);
    setQuery('');
    setBranchSel('main'); setWtOn(false); setWtChosen(null);
    allDirtyRef.current = false;
    if (n !== 7) setLayout(savedLayout.current);
    const start = (opts, after) => {
      setWorkspace(true);
      eng.reset({ seed: ++seedRef.current, cfg, maxPar: { Full: 6, Balanced: 4, Light: 2 }[perf], ...opts });
      eng.branchName = 'main';
      bootLine(eng);
      setRevealKey((k) => k + 1);
      eng.startSync();
      eng.at(1250, () => eng.startRun());
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
      if (e.key === 'F5') { e.preventDefault(); if (e.ctrlKey || e.shiftKey) doRebuild(); else if (eng.phase === 'stopped') doContinue(); else doBuild(); }
      if (e.key === 'F1') { e.preventDefault(); setAboutOpen((v) => !v); }
      if ((e.ctrlKey || e.metaKey) && (e.key === 'f' || e.key === 'F')) {
        e.preventDefault();
        const el = document.getElementById('bo-proj-search');
        if (el) el.focus();
      }
      if (e.key === 'Escape') {
        if (aboutOpen) setAboutOpen(false);
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
  const toggleFilter = (f) => setFilter((cur) => (cur === f ? null : f));
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
    if (eng.phase === 'running' || v === cfg) return;
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
  const pickFolder = () => {
    setWorkspace(true);
    resetEngine({});
    eng.say('cmd', 'workspace: D:\\src\\osys — 36 projects discovered');
    setRevealKey((k) => k + 1);
    eng.startSync();
    bump();
  };

  const c = eng.counts();
  const di = eng.depIssueCount();
  const running = eng.phase === 'running';
  const stopped = eng.phase === 'stopped';
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
  const saveSettings = (cfg2) => {
    setLayerCfg(cfg2);
    try { localStorage.setItem('delta-bo-layers-v2', JSON.stringify(cfg2)); } catch (e) { /* yok */ }
    setSettingsOpen(false);
    note(cfg2.length ? 'Layer definitions updated — ' + cfg2.length + ' layers' : 'Layers removed — single project list');
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
          <img src={logoBase + '/app-mark.svg'} alt="Build Orchestrator" style={{ height: 19, display: 'block', flex: 'none' }} />
          <span style={{ fontSize: 'var(--text-xs)', fontWeight: 500, color: 'var(--text-secondary)', whiteSpace: 'nowrap' }}>Build Orchestrator</span>
          <span aria-hidden="true" style={{ width: 1, height: 13, background: 'var(--border)', flex: 'none' }}></span>
          <img src={logoBase + '/delta-logo-dark.svg'} alt="Delta" title="Delta" style={{ height: 10, display: 'block', opacity: 0.55, flex: 'none' }} />
          <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-dim)', whiteSpace: 'nowrap' }}>
            {workspace ? 'OSYS · ' + branchSel : 'no repository'}
          </span>
          {workspace && wtActive && (
            <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)', whiteSpace: 'nowrap', overflow: 'hidden', textOverflow: 'ellipsis' }}>
              · {wtName}
            </span>
          )}
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
          <DS.Tooltip content="Settings — layer definitions" side="bottom">
            <DS.IconButton size="sm" aria-label="Settings" active={settingsOpen} onClick={() => setSettingsOpen(true)}><I.gear /></DS.IconButton>
          </DS.Tooltip>
          <DS.Tooltip content="About — version, shortcuts and diagnostics (F1)" side="bottom">
            <DS.IconButton size="sm" aria-label="About" active={aboutOpen} onClick={() => setAboutOpen(true)}><I.info /></DS.IconButton>
          </DS.Tooltip>
        </span>
      </DS.TitleBar>

      {/* ---- STICKY ŞERİT ---- */}
      <StickyStrip eng={eng} workspace={workspace} onSelect={select} onFilterFailed={() => setFilter('failed')} />

      {/* ---- GÖVDE: 2 kolon × 2 satır + splitters ---- */}
      <div ref={bodyRef} style={{ flex: 1, minHeight: 0, display: 'flex', alignItems: 'stretch' }}>
        {/* SOL KOLON */}
        <div style={{ width: layout.col + '%', minWidth: 0, display: 'flex', flexDirection: 'column' }}>
          {showGraph && (
            <>
              <div style={{ height: layout.left + '%', minHeight: 0, display: 'flex', flexDirection: 'column' }}>
                <PanelHead label="Dependency graph" right={workspace ? (
                  <span style={{ fontFamily: MONO, fontSize: 'var(--text-2xs)', color: 'var(--text-faint)' }}>
                    {BO.PROJECTS.length} projects · {BO.GRAPH.edges.length} dependencies
                  </span>
                ) : null} />
                <GraphPanel eng={eng} selected={selected} onSelect={select} revealKey={revealKey} workspace={workspace} />
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
              {filter && (
                <DS.Chip active label={FILTER_LABELS[filter] || filter} onRemove={() => setFilter(null)} style={{ height: 20, padding: '0 6px', fontSize: 'var(--text-2xs)' }} />
              )}
            </PanelHead>
            <ListPanel eng={eng} groups={groups} selected={selected} onSelect={select} onNote={note} filter={filter} query={query} follow={follow} revealKey={revealKey} workspace={workspace} onPickFolder={pickFolder} />
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
                    <DS.Tooltip content={'Failed dependency: ' + selSt.depIssue.map(BO.shortName).join(', ') + ' — last successful output referenced'} side="bottom">
                      <span style={{ display: 'inline-flex', alignItems: 'center', gap: 5, color: 'var(--status-fail-text)', fontSize: 'var(--text-2xs)', whiteSpace: 'nowrap', cursor: 'default' }}>
                        <I.alertTri /> dependency issue
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
        <DS.Button size="sm" variant="secondary" icon={<I.sync />} onClick={doSync} disabled={!workspace || running}>Sync</DS.Button>
        <span style={{ width: 1, alignSelf: 'stretch', margin: '10px 2px', background: 'var(--border-subtle)' }}></span>
        <DS.Tooltip content="All projects — clear filter" side="top">
          <DS.Chip icon={<I.sigma />} value={c.total} onClick={() => setFilter(null)} data-bo-chip="1" />
        </DS.Tooltip>
        <DS.Tooltip content="Building now — filter" side="top">
          <DS.Chip icon={c.building ? <BuildingSpin size={12} /> : <span style={{ width: 8, height: 8, borderRadius: '50%', background: 'var(--neutral-600)', display: 'inline-block' }}></span>}
            value={c.building} active={filter === 'building'} onClick={() => toggleFilter('building')} data-bo-chip="1" />
        </DS.Tooltip>
        <DS.Tooltip content="Succeeded — filter" side="top">
          <DS.Chip icon={<DS.StatusGlyph status="succeeded" size={12} />} value={c.succeeded} active={filter === 'succeeded'} onClick={() => toggleFilter('succeeded')} data-bo-chip="1" />
        </DS.Tooltip>
        <DS.Tooltip content="Failed — filter" side="top">
          <DS.Chip icon={<DS.StatusGlyph status="failed" size={12} />} value={c.failed} active={filter === 'failed'} onClick={() => toggleFilter('failed')} data-bo-chip="1" />
        </DS.Tooltip>
        <DS.Tooltip content="Skipped — filter" side="top">
          <DS.Chip icon={<DS.StatusGlyph status="skipped" size={12} />} value={c.skipped} active={filter === 'skipped'} onClick={() => toggleFilter('skipped')} data-bo-chip="1" />
        </DS.Tooltip>
        <DS.Tooltip content="Dependency-affected — filter" side="top">
          <DS.Chip icon={<span style={{ display: 'inline-flex', color: di ? 'var(--status-fail-text)' : 'var(--text-faint)' }}><I.alertTri size={12} /></span>}
            value={di} active={filter === 'dep'} onClick={() => toggleFilter('dep')} data-bo-chip="1" />
        </DS.Tooltip>

        <span style={{ flex: 1 }}></span>

        <span style={{ position: 'relative' }} data-bo-chip="1">
          <DS.Chip icon={<I.branch />} label="branch" value={branchSel} chevron active={branchPop} disabled={!workspace}
            onClick={() => { setBranchPop(!branchPop); setWtPop(false); }} />
          <BranchPopover open={branchPop} branches={BO.BRANCHES} current={branchSel} activeBranch={activeBranch} onPick={pickBranch} />
        </span>
        <span style={{ position: 'relative' }} data-bo-chip="1">
          <DS.Chip icon={<I.tree />} label="worktree" value={wtActive ? wtName : 'off'} chevron active={wtPop} disabled={!workspace}
            onClick={() => { setWtPop(!wtPop); setBranchPop(false); }} />
          <WorktreePopover open={wtPop} align="left" forced={forced} on={wtActive} setOn={setWtOn} autoName={autoName}
            worktrees={worktrees} chosen={wtChosen} setChosen={setWtChosen}
            onDelete={(nm) => { setWorktrees((ws) => ws.filter((w) => w.name !== nm)); if (wtChosen === nm) setWtChosen(null); note('worktree removed: ' + nm); }}
            source={srcText} />
        </span>
        <DS.Segment size="sm" options={['Debug', 'Release']} value={cfg} onChange={pickCfg} />
        <DS.Chip label="perf" value={perf} onClick={cyclePerf} disabled={!workspace} data-bo-chip="1" />
        <span style={{ width: 1, alignSelf: 'stretch', margin: '10px 2px', background: 'var(--border-subtle)' }}></span>
        {running ? (
          <DS.Button variant="danger" size="md" icon={<I.stop />} onClick={() => { eng.stop(); bump(); }}>Stop</DS.Button>
        ) : (
          <span style={{ position: 'relative', display: 'inline-flex' }} data-bo-chip="1">
            <DS.Button variant="primary" size="md" icon={<I.play />} onClick={stopped ? doContinue : doBuild} disabled={!workspace || eng.phase === 'syncing'}
              style={{ borderTopRightRadius: 0, borderBottomRightRadius: 0 }}>{stopped ? 'Continue' : 'Build'}</DS.Button>
            <DS.Button variant="primary" size="md" aria-label="Build options" disabled={!workspace || eng.phase === 'syncing'}
              onClick={() => { setBuildMenu(!buildMenu); setBranchPop(false); setWtPop(false); }}
              style={{ borderTopLeftRadius: 0, borderBottomLeftRadius: 0, padding: '0 5px', borderLeft: '1px solid var(--amber-dim)' }}>
              <I.chevUp />
            </DS.Button>
            <Popover open={buildMenu} width={276}>
              {stopped && (
                <BuildMenuItem icon={<I.play />} title="Continue" desc={remainN + ' queued projects resume'} kbd="F5"
                  onPick={() => { setBuildMenu(false); doContinue(); }} />
              )}
              <BuildMenuItem icon={<I.play />} title="Build" desc={stopped ? 'Start over — only changed projects' : 'Only changed projects'} kbd={stopped ? undefined : 'F5'}
                onPick={() => { setBuildMenu(false); doBuild(); }} />
              <BuildMenuItem icon={<I.rot />} title="Rebuild" desc="All 36 projects — cache ignored" kbd="Ctrl+F5"
                onPick={() => { setBuildMenu(false); doRebuild(); }} />
              {c.failed > 0 && (
                <BuildMenuItem icon={<I.redo />} title="Retry failed" desc={c.failed + ' failed + dependents'}
                  onPick={() => { setBuildMenu(false); doRetry(); }} />
              )}
            </Popover>
          </span>
        )}
      </div>

      {/* ---- AYARLAR ---- */}
      <SettingsDialog open={settingsOpen} cfg={layerCfg} onClose={() => setSettingsOpen(false)} onSave={saveSettings} />

      {/* ---- ABOUT ---- */}
      <AboutDialog open={aboutOpen} onClose={() => setAboutOpen(false)} logoSrc={logoBase + '/app-mark.svg'} companyLogoSrc={logoBase + '/delta-logo-dark.svg'} />
    </div>
  );
}

window.BuildApp = BuildApp;
