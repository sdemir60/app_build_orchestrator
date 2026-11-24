/* Delta Build Orchestrator — demo verisi + simülasyon motoru (saf JS, UI'sız)
   window.DELTA_BO altında yayınlanır; BuildApp.jsx tüketir. */
(function () {
  'use strict';

  /* ---------- yardımcılar ---------- */
  function mulberry32(seed) {
    let a = seed >>> 0;
    return function () {
      a |= 0; a = (a + 0x6D2B79F5) | 0;
      let t = Math.imul(a ^ (a >>> 15), 1 | a);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }
  function fmtDur(ms) {
    if (ms == null) return '—';
    if (ms < 9950) return (ms / 1000).toFixed(1) + 's';
    const s = Math.round(ms / 1000);
    if (s < 60) return s + 's';
    const m = Math.floor(s / 60), r = s % 60;
    return m + 'm ' + String(r).padStart(2, '0') + 's';
  }
  function fmtClock(ms) { // 12:04:07 tabanlı sahte duvar saati
    const base = ((12 * 60 + 4) * 60 + 7) * 1000;
    const t = Math.floor((base + ms) / 1000);
    const h = Math.floor(t / 3600) % 24, m = Math.floor(t / 60) % 60, s = t % 60;
    return [h, m, s].map((x) => String(x).padStart(2, '0')).join(':');
  }
  function fmtSize(mb) { return mb >= 1024 ? (mb / 1024).toFixed(1) + ' GB' : Math.round(mb) + ' MB'; }
  /* göreli yaş — satırdaki 'up to date · 2h' etiketinin biçimi (mono, tabular) */
  function fmtAge(ms) {
    if (ms == null) return null;
    const s = Math.max(0, Math.round(ms / 1000));
    if (s < 60) return 'just now';
    const m = Math.floor(s / 60);
    if (m < 60) return m + 'm';
    const h = Math.floor(m / 60);
    if (h < 24) return h + 'h';
    return Math.floor(h / 24) + 'd';
  }
  function sha7(rng) {
    const c = '0123456789abcdef'; let s = '';
    for (let i = 0; i < 7; i++) s += c[Math.floor(rng() * 16)];
    return s;
  }
  const shortName = (n) => n.replace(/^ACME\./, '');

  /* konsol satır sınıflandırıcısı (uygulama ConsoleLineClassifier): yalnız biçimi BİLİNEN satır renklenir —
     [error] / warning: önekleri, kompakt özet, MSBuild tanı biçimi (… error CS0103: / … warning MSB3277:), komut başları.
     Gerisi tek ton (info). 'success' türü yok. */
  const CMD_HEADS = ['git ', 'msbuild ', 'nuget ', 'dotnet '];
  const COMPACT_SUMMARY = /^.+: \d+ more warnings? — .+ — see the project log$/;
  function classifyLine(text) {
    const t = String(text || '').replace(/^\s+/, '');
    if (/^\[error\]/i.test(t)) return { type: 'error', code: null };
    if (/^warning:/i.test(t)) return { type: 'warn', code: null, own: true };
    if (COMPACT_SUMMARY.test(t)) return { type: 'warn', code: null };
    /* kanonik tanı: kökenli (`A.cs(1,2): error CS0246:`, `MSBUILD : warning MSB3277:`) ya da kökensiz (`warning CS8618:`) */
    const m = /(?:^|:\s*)(error|warning)(?:\s+([A-Za-z]+\d+))?\s*:/i.exec(t);
    if (m) return { type: m[1].toLowerCase() === 'error' ? 'error' : 'warn', code: m[2] || null };
    const low = t.toLowerCase();
    for (let i = 0; i < CMD_HEADS.length; i++) if (low.indexOf(CMD_HEADS[i]) === 0) return { type: 'cmd', code: null };
    return { type: 'info', code: null };
  }

  /* ---------- katmanlar ---------- */
  const LAYERS = [
    { id: 0, name: 'Layer 0 — Core' },
    { id: 1, name: 'Layer 1 — Infrastructure' },
    { id: 2, name: 'Layer 2 — Domain' },
    { id: 3, name: 'Layer 3 — Services' },
    { id: 4, name: 'Layer 4 — API' },
    { id: 5, name: 'Layer 5 — Client' },
  ];

  /* ---------- projeler (36) ----------
     dirty: sync'te "değişmiş" bulunan kökler. fails: kasıtlı hata.
     Sales.Core zincirin ORTASINDA başarısız olur — hata aşağıyı bloklamaz:
     bağımlılar son başarılı çıktıyla yine derlenir, 'depIssue' işareti taşır.
     Web.Portal sink + kendi hatası. */
  const P = [
    // L0
    { name: 'ACME.Base',              sln: 'Acme.sln',        layer: 0, deps: [], dur: 1600 },
    { name: 'ACME.Common.Contracts',  sln: 'Acme.sln',        layer: 0, deps: ['ACME.Base'], dur: 1300 },
    { name: 'ACME.Common.Utils',      sln: 'Acme.sln',        layer: 0, deps: ['ACME.Base'], dur: 1500 },
    // L1
    { name: 'ACME.Data.Core',         sln: 'Acme.sln',        layer: 1, deps: ['ACME.Base', 'ACME.Common.Contracts'], dur: 2600 },
    { name: 'ACME.Data.Migrations',   sln: 'Acme.sln',        layer: 1, deps: ['ACME.Data.Core'], dur: 1400 },
    // slns: projeyi listeleyen solution'lar (birden çoksa VS düğmesi seçim popover'ı açar — ProjectRowActions PART_VsChooser)
    { name: 'ACME.Security',          sln: 'Acme.sln',        layer: 1, deps: ['ACME.Base', 'ACME.Common.Utils'], dur: 1700, slns: ['Acme.sln', 'Acme.Sales.sln', 'Acme.Service.sln', 'Acme.Parts.sln'] },
    { name: 'ACME.Shared.UI',          sln: 'Acme.sln',        layer: 1, deps: ['ACME.Common.Utils'], dur: 2100 },
    { name: 'ACME.Integration.Core',  sln: 'Acme.sln',        layer: 1, deps: ['ACME.Common.Contracts'], dur: 1800 },
    // L2
    { name: 'ACME.Domain.Vehicle',       sln: 'Acme.sln',        layer: 2, deps: ['ACME.Data.Core'], dur: 2200 },
    { name: 'ACME.Domain.Customer',    sln: 'Acme.sln',        layer: 2, deps: ['ACME.Data.Core'], dur: 2000 },
    { name: 'ACME.Domain.Inventory',       sln: 'Acme.sln',        layer: 2, deps: ['ACME.Data.Core'], dur: 1900 },
    { name: 'ACME.Domain.Service',     sln: 'Acme.sln',        layer: 2, deps: ['ACME.Data.Core', 'ACME.Domain.Vehicle'], dur: 2400, dirty: true },
    { name: 'ACME.Domain.Parts',      sln: 'Acme.sln',        layer: 2, deps: ['ACME.Data.Core', 'ACME.Domain.Inventory'], dur: 2100, dirty: true },
    { name: 'ACME.Domain.Finance',     sln: 'Acme.sln',        layer: 2, deps: ['ACME.Data.Core'], dur: 2300 },
    // L3
    { name: 'ACME.Sales.Core',        sln: 'Acme.Sales.sln',  layer: 3, deps: ['ACME.Domain.Vehicle', 'ACME.Domain.Customer'], dur: 2800, dirty: true, fails: true, slns: ['Acme.sln', 'Acme.Sales.sln'] },
    { name: 'ACME.UsedCars.Core',     sln: 'Acme.Sales.sln',  layer: 3, deps: ['ACME.Domain.Vehicle', 'ACME.Sales.Core'], dur: 2200 },
    { name: 'ACME.Service.Scheduling',    sln: 'Acme.Service.sln', layer: 3, deps: ['ACME.Domain.Service', 'ACME.Domain.Customer'], dur: 1900 },
    { name: 'ACME.Service.Workshop',     sln: 'Acme.Service.sln', layer: 3, deps: ['ACME.Domain.Service', 'ACME.Domain.Parts'], dur: 2600 },
    { name: 'ACME.Parts.Inventory',        sln: 'Acme.Parts.sln',  layer: 3, deps: ['ACME.Domain.Parts'], dur: 1700 },
    { name: 'ACME.Parts.Catalog',     sln: 'Acme.Parts.sln',  layer: 3, deps: ['ACME.Domain.Parts'], dur: 2000 },
    { name: 'ACME.Finance.Invoicing',     sln: 'Acme.sln',        layer: 3, deps: ['ACME.Domain.Finance'], dur: 2400 },
    { name: 'ACME.Finance.Accounting',   sln: 'Acme.sln',        layer: 3, deps: ['ACME.Domain.Finance', 'ACME.Finance.Invoicing'], dur: 2600 },
    { name: 'ACME.Reporting.Core',        sln: 'Acme.sln',        layer: 3, deps: ['ACME.Data.Core', 'ACME.Domain.Finance'], dur: 2200, dirty: true, burst: true },
    // L4
    { name: 'ACME.Server.Api',        sln: 'Acme.sln',        layer: 4, deps: ['ACME.Sales.Core', 'ACME.Service.Scheduling', 'ACME.Parts.Inventory', 'ACME.Security'], dur: 3400, dirty: true },
    { name: 'ACME.Sales.Api',         sln: 'Acme.Sales.sln',  layer: 4, deps: ['ACME.Sales.Core', 'ACME.Security'], dur: 2600, outside: true },
    { name: 'ACME.Service.Api',        sln: 'Acme.Service.sln', layer: 4, deps: ['ACME.Service.Scheduling', 'ACME.Service.Workshop', 'ACME.Security'], dur: 2900 },
    { name: 'ACME.Parts.Api',         sln: 'Acme.Parts.sln',  layer: 4, deps: ['ACME.Parts.Catalog', 'ACME.Parts.Inventory', 'ACME.Security'], dur: 3100 },
    { name: 'ACME.Reporting.Api',         sln: 'Acme.sln',        layer: 4, deps: ['ACME.Reporting.Core', 'ACME.Security'], dur: 2300 },
    { name: 'ACME.Notifications.Api',      sln: 'Acme.sln',        layer: 4, deps: ['ACME.Common.Utils', 'ACME.Security'], dur: 1600, outside: true },
    { name: 'ACME.Integration.Api',   sln: 'Acme.sln',        layer: 4, deps: ['ACME.Integration.Core', 'ACME.Security'], dur: 1900 },
    // L5
    { name: 'ACME.Web.Portal',        sln: 'Acme.Web.sln',    layer: 5, deps: ['ACME.Sales.Api', 'ACME.Reporting.Api', 'ACME.Shared.UI'], dur: 4200, dirty: true, fails: true },
    { name: 'ACME.Web.DealerPortal',    sln: 'Acme.Web.sln',    layer: 5, deps: ['ACME.Sales.Api', 'ACME.Shared.UI'], dur: 3600 },
    { name: 'ACME.Client.Core',       sln: 'Acme.Client.sln', layer: 5, deps: ['ACME.Shared.UI', 'ACME.Common.Contracts'], dur: 2400 },
    { name: 'ACME.Client.Sales',      sln: 'Acme.Client.sln', layer: 5, deps: ['ACME.Client.Core', 'ACME.Sales.Api'], dur: 3200, dirty: true },
    { name: 'ACME.Client.Service',     sln: 'Acme.Client.sln', layer: 5, deps: ['ACME.Client.Core', 'ACME.Service.Api'], dur: 2800 },
    { name: 'ACME.Mobile.Api',         sln: 'Acme.Mobile.sln',  layer: 5, deps: ['ACME.Service.Api', 'ACME.Parts.Api', 'ACME.Security'], dur: 2600 },
  ];
  const byName = {};
  function rebuildIndex() {
    Object.keys(byName).forEach(function (k) { delete byName[k]; });
    P.forEach(function (p) { byName[p.name] = p; });
  }
  rebuildIndex();

  /* ---------- bağımlılık döngüsü (Cycle sahnesi) ----------
     Parts.Api → Domain.Parts geri-kenarı bu üç projeyi tek SCC'ye sokar. Grup tek değişim birimidir: ya hep kirli
     ya hep güncel. Düz Build kirli grubu turlarla derler (§8.1 / §8.8): grup dış upstream'i bitince koşar, üyeler
     turda derlenir, ara sonuç yayılmaz, grup bitince her üye tek sonucunu alır. */
  const CYCLE_MEMBERS = ['ACME.Domain.Parts', 'ACME.Parts.Inventory', 'ACME.Parts.Api'];
  const CYCLE_PATH = CYCLE_MEMBERS.concat(CYCLE_MEMBERS[0]);

  /* build-order: katman, sonra tanım sırası. Harici projeler layer -1 taşır → en başta derlenir. */
  let ORDER = [];
  function rebuildOrder() {
    ORDER = P.slice().sort(function (a, b) { return a.layer - b.layer || P.indexOf(a) - P.indexOf(b); }).map(function (p) { return p.name; });
  }
  rebuildOrder();

  /* bakım görevleri solution granülaritesinde çalışır (msbuild /t:Clean, nuget restore) */
  let SOLUTIONS = [], EDGE_N = 0;
  function rebuildSolutions() {
    const order = [], n = {};
    P.forEach(function (p) { if (order.indexOf(p.sln) < 0) order.push(p.sln); n[p.sln] = (n[p.sln] || 0) + 1; });
    SOLUTIONS = order.map(function (s) { return { name: s, n: n[s] }; });
    EDGE_N = P.reduce(function (k, p) { return k + p.deps.length; }, 0);
  }
  rebuildSolutions();

  /* ---------- graf yerleşimi (katman düzeni, DAG) ---------- */
  let GRAPH = null;
  function rebuildGraph() { GRAPH = buildGraph(); }
  function buildGraph() {
    const counts = {}; P.forEach((p) => { counts[p.layer] = (counts[p.layer] || 0) + 1; });
    const W = 880, rowH = 96, top = 46;
    const idx = {};
    const nodes = {};
    P.forEach((p) => {
      const i = (idx[p.layer] = (idx[p.layer] || 0));
      idx[p.layer]++;
      const n = counts[p.layer];
      const spacing = Math.min(96, (W - 70) / Math.max(1, n - 0.5));
      nodes[p.name] = { x: W / 2 + (i - (n - 1) / 2) * spacing, y: top + (p.layer + 1) * rowH };
    });
    const edges = [];
    P.forEach((p) => p.deps.forEach((d) => edges.push([d, p.name])));
    return { nodes, edges, W, H: top + 5 * rowH + 58 };
  }
  rebuildGraph();

  /* ---------- harici projeler ----------
     Kullanıcı ayarlarda bir KLASÖR ya da .sln/.csproj yolu verir; gerçek uygulama o yolu tarayıp
     içindeki tüm .csproj'leri listeye katar. Simülasyonda tarama deterministik taklit edilir:
     yolun son parçası taban ad olur, path hash'i 2–4 proje üretir, adlandırma standardı korunur
     (<Taban>.Core/.Data/.Api/.Client). Harici projeler layer -1 taşır → derleme sırasının başında,
     listede ve grafta tek katman gibi durur; süreç geri kalanla BİREBİR aynıdır. */
  const EXT_SUFFIX = ['Core', 'Data', 'Api', 'Client'];
  function extBase(path) {
    const parts = String(path || '').split(/[\\/]+/).filter(Boolean);
    let last = parts[parts.length - 1] || 'External';
    last = last.replace(/\.(csproj|sln|vbproj|fsproj)$/i, '');
    return last.replace(/[^A-Za-z0-9._]/g, '') || 'External';
  }
  function hash32(s) {
    let h = 2166136261;
    for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); }
    return h >>> 0;
  }
  function scanExternal(path, seen, vcs) {
    const h = hash32(String(path));
    let base = extBase(path);
    if (seen[base]) base = base + '.' + (seen[base] + 1);
    seen[base] = (seen[base] || 0) + 1;
    const n = 2 + (h % 3); // 2–4 proje
    const out = [];
    for (let i = 0; i < n; i++) {
      const nm = base + '.' + EXT_SUFFIX[i];
      out.push({
        name: nm, sln: base + '.sln', layer: -1,
        deps: i ? [base + '.' + EXT_SUFFIX[i - 1]] : [],
        dur: 1200 + ((h >>> (i * 3)) % 9) * 180,
        ext: true, path: String(path), vcs: vcs || 'git',
        dirty: i === 0, // ilk proje kök: zinciri bağımlılık kapanışı derler
      });
    }
    return out;
  }
  /* Ayarlar kaydedilince çağrılır: P'nin başındaki harici projeler yenilenir, türetilmiş
     yapılar (index · build-order · solution listesi · graf) yeniden kurulur. */
  function setExternal(paths) {
    for (let i = P.length - 1; i >= 0; i--) if (P[i].ext) P.splice(i, 1);
    const seen = {}, added = [];
    (paths || []).forEach(function (p) {
      const path = typeof p === 'string' ? p : (p && p.path) || '';
      if (!path.trim()) return;
      const vcs = (typeof p === 'object' && p && p.vcs) || 'git';
      scanExternal(path.trim(), seen, vcs).forEach(function (proj) { added.push(proj); });
    });
    if (added.length) P.unshift.apply(P, added);
    rebuildIndex(); rebuildOrder(); rebuildSolutions(); rebuildGraph();
    return added.map(function (p) { return p.name; });
  }

  /* ---------- branch / worktree ---------- */
  /* origin/main'in fetch'le çözülen ucu: yerel HEAD (a3f81c2) bunun 3 commit gerisinde —
     alt bardaki '3 behind' çipi (v1.16.0) bu mesafeyi gösterir, tıklanınca ff-only pull eder. */
  const REMOTE_SHA = 'b7e91d4';
  const BRANCHES = [
    { name: 'main', sha: 'a3f81c2', active: true, note: 'active' },
    { name: 'develop', sha: '8c214af', note: '2h ago' },
    { name: 'release/2026.06', sha: 'f3a02c8', note: 'yesterday' },
    { name: 'feature/parts-api-v2', sha: '21d9e0b', note: '3d ago' },
    { name: 'feature/scheduling-sms', sha: '9e51b23', note: '4d ago' },
    { name: 'hotfix/inventory-count', sha: '4ab7f19', note: '1w ago' },
  ];
  const WORKTREES = [
    { name: 'main-1', note: '2 days ago · clean' },
    { name: 'feature-parts-api-v2-1', note: 'yesterday · clean' },
  ];

  /* ---------- will-build hesabı ---------- */
  function computeWillBuild(allDirty, forced) {
    const dirty = new Set(P.filter((p) => p.dirty).map((p) => p.name));
    if (forced) forced.forEach((n) => { if (byName[n]) dirty.add(n); }); // hiç derlenmemiş / son derlemesi patlamış
    if (allDirty) P.forEach((p) => dirty.add(p.name));
    let grew = true;
    while (grew) {
      grew = false;
      P.forEach((p) => {
        if (!dirty.has(p.name) && p.deps.some((d) => dirty.has(d))) { dirty.add(p.name); grew = true; }
      });
    }
    return dirty;
  }

  /* ---------- log üretici ---------- */
  const WARNS = [
    "CS8618: non-nullable field '_ctx' may be null when exiting constructor",
    "CS0618: 'InventoryService.Legacy()' is obsolete",
    "NU1603: package 'Acme.Cache 3.1.0' was not found, resolved 3.1.2 instead",
    "CS0168: the variable 'ex' is declared but never used",
  ];
  function makeLog(p, cfg, rng, willFail) {
    const L = [];
    const files = 24 + Math.floor(rng() * 140);
    L.push({ at: 0.00, type: 'cmd', text: 'msbuild ' + p.name + '.csproj /t:Build /p:Configuration=' + cfg + ' /m /nr:false' });
    L.push({ at: 0.05, type: 'dim', text: 'ResolveReferences: ' + p.deps.length + ' project references resolved' });
    L.push({ at: 0.10, type: 'dim', text: 'Restore: packages up to date (' + (0.2 + rng() * 0.5).toFixed(1) + 's)' });
    L.push({ at: 0.18, type: 'info', text: 'CoreCompile: ' + files + ' files → obj/' + cfg + '/net8.0/' + p.name + '.dll' });
    const steps = 3 + Math.floor(rng() * 4);
    for (let i = 0; i < steps; i++) {
      const at = 0.24 + (0.58 * (i + 1)) / (steps + 1);
      const r = rng();
      if (r < 0.22 && p.warns !== 0) L.push({ at, type: 'warn', text: 'warning ' + WARNS[Math.floor(rng() * WARNS.length)] });
      else if (r < 0.5) L.push({ at, type: 'dim', text: 'Analyzer: Acme.CodeRules ' + (1 + Math.floor(rng() * 3)) + ' rules, 0 violations' });
      else L.push({ at, type: 'dim', text: 'GenerateTargetFrameworkMonikerAttribute → obj/' + cfg + '/.NETCoreApp,Version=v8.0.cs' });
    }
      if (willFail) {
      if (p.name === 'ACME.Sales.Core') {
        L.push({ at: 0.86, type: 'error', text: "SalesContract.cs(129,17): error CS0246: the type or namespace name 'ICampaignService' could not be found" });
        L.push({ at: 0.93, type: 'error', text: "PriceCalculator.cs(57,23): error CS1061: 'VehiclePriceDto' does not contain a definition for 'NetPrice'" });
        L.push({ at: 1, type: 'error', text: 'Build FAILED — 2 errors, 0 warnings (' + fmtDur(p.dur) + ')' });
      } else {
        L.push({ at: 0.90, type: 'error', text: "VehicleDetail.cshtml.g.cs(210,44): error CS1061: 'VehicleViewModel' does not contain a definition for 'PolicyNo'" });
        L.push({ at: 1, type: 'error', text: 'Build FAILED — 1 error, 0 warnings (' + fmtDur(p.dur) + ')' });
      }
    } else {
      /* uyarı fırtınası (kompakt konsol örneği): ilk 3 farklı uyarı görünür, kalanı tek özet satırında sayılır */
      if (p.burst) {
        const members = ['ReportBuilder', 'ReportFilter', 'ExportJob', 'ChartSeries', 'PivotTable', 'KpiCard', 'DateRange', 'Scheduler'];
        for (let i = 0; i < 22; i++) {
          const m = members[i % members.length];
          L.push({ at: 0.6 + i * 0.012, type: 'warn', text: m + '.cs(' + (14 + i * 7) + ',16): warning CS1591: Missing XML comment for publicly visible type or member \'' + m + '.Item' + i + '\'' });
        }
        ['System.Text.Json', 'Microsoft.Extensions.Logging', 'System.Memory', 'Newtonsoft.Json', 'System.Buffers'].forEach(function (a, i) {
          L.push({ at: 0.88 + i * 0.002, type: 'warn', text: 'Microsoft.Common.CurrentVersion.targets(2413,5): warning MSB3277: Found conflicts between different versions of "' + a + '" that could not be resolved.' });
        });
      }
      L.push({ at: 0.90, type: 'dim', text: 'CopyFilesToOutputDirectory: bin/' + cfg + '/net8.0/' });
      const w = L.filter((x) => x.type === 'warn').length;
      L.push({ at: 1, type: 'info', text: 'Build succeeded — 0 errors, ' + w + ' warnings (' + fmtDur(p.dur) + ')' });
    }
    return L;
  }

  /* ================== SİMÜLASYON MOTORU ================== */
  /* işlem koreografisi (4a — Dalga + örtüşen veda): griye dön + bekleyiş → kapsam derleme
     sırasıyla dalga gibi amber'a yanar → sarı-gri an → griler uzun sönüşe başlar (1120ms),
     ortasında sarılar katılır (560ms) — ikisi aynı anda biter → nefes → derleme */
  function SimEngine() {
    this.runCount = 0;
    /* harici working copy'ler her build öncesi pull edilir — alt bardaki 'pull' çipi;
       ayar reset'ten etkilenmez, sahne değişse de kullanıcının seçimi kalır */
    this.pullExternal = true;
    /* motor süreci (1.15.0 uygulaması): ok · dead · silent · waking — reset'ten etkilenmez (motor ölümü koşudan bağımsız) */
    this.engine = 'ok';
    this.engineMsg = null;
    /* kaynak indeksi kopya başına bir kez kurulur: ilk Sync 'indexing source files · d/t' adımını koşar */
    this.indexedRoots = new Set();
    this.rootKey = 'repo';
    /* defter (build state) — reset'ten sağ çıkar: projenin bu araçla son başarılı derlemesinin configuration'ı.
       Kaydı olmayan proje varsayılan olarak Debug'da derlenmiştir; `outside` örnekleri kayıtsızdır (zaman kipi, §7.6). */
    this.ledger = {};
    this.reset({});
  }
  SimEngine.prototype.reset = function (opts) {
    opts = opts || {};
    this.rng = mulberry32(1237 + (opts.seed || 0) * 101);
    this.simT = 0;
    this.phase = opts.empty ? 'empty' : 'boot'; // empty | boot | syncing | idle | running | done | stopped
    this.allClean = !!opts.allClean;
    this.allDirty = !!opts.allDirty;
    this.cfg = opts.cfg || 'Debug';
    this.maxPar = opts.maxPar || 4;
    /* pull sonrası otomatik Sync'te konsol/stream KORUNUR (keepLog) — kullanıcı pull satırını görür */
    const keepN = opts.keepLog ? this.narrative : null;
    const keepS = opts.keepLog ? this.stream : null;
    this.stream = keepS || [];
    this.narrative = keepN || [];
    this.activeLine = null;
    this.todo = [];
    if (this._markTimers) this._clearMarkTimers(); else this._markTimers = [];
    if (this._endTimers) this._clearEndTimers(); else this._endTimers = [];
    this.endStep = null;
    this.endOrder = {};
    this.endStagger = 0;
    this.settleStagger = 0;
    this.marked = null;
    this.markStep = null;
    this.revealAt = null;      // sync reveal zaman damgası (UI stagger için)
    this.discover = null;      // Sync keşfi sürerken {repo, ext, hasExt} — sol paneller ikon + metin gösterir (1.24.0)
    this.syncMs = 0;           // son startSync'in toplam süresi (sonrasına zincirlenen Build için)
    this.runStartT = null;
    this.doneAt = null;
    this.stoppedAt = null;
    this.willBuild = new Set();
    this.checkDur = null;      // hızlı kontrol koşusunun süresi
    this.checkOnly = false;    // yalnız BU koşu için: derlenecek bayat iş yok
    this.instantMode = false;
    this._id = opts.keepLog ? (this._id || 0) : 0;
    this._lastEmitT = -9999;
    this._building = [];
    this._finishedWB = 0;
    this._eta = null;
    this.lastFail = null;      // {name, t} — shake için
    this.op = null;            // kalıcı son işlem etiketi {label, target} — şerit pill'i
    this.marked = null;        // koreografi kapsamı (Set) — phase 'marking'
    this.task = null;          // yürüyen bakım görevi (clean/optimize)
    this.taskResult = null;    // son biten bakım görevinin özeti (şeritte kalır)
    this.resolveRun = null;    // yürüyen Resolve cycles koşusu
    this.work = null;          // uçuştaki workspace işi {kind: checkout|pull|worktree, target} — pill + şerit satırı
    this.syncStep = null;      // görünür Sync'in adımı {step: start|fetching|analyzing|indexing, found, done, total}
    this.stopStage = null;     // null · 'now' (drain sürüyor → Stop now) · 'terminating'
    this.runPar = null;        // koşunun oturtulmuş paralelliği (runStarted.Parallelism) — koşu dışında null
    this.runMode = null;       // koşunun başladığı perf modu — şerit koşu boyunca bunu yazar
    this.cleanRun = false;     // Build menüsünün Clean'i: her projede -t:Clean koşusu
    this.scope = null;         // tek proje koşusunun kapsamı (Set|null)
    this.scopeName = null;     // o koşunun hedef projesi
    this.prevPhase = null;
    this.targetSha = opts.targetSha || 'a3f81c2';   // yerel HEAD
    this.remoteSha = opts.remoteSha || REMOTE_SHA;  // origin/<branch> ucu — Sync'in fetch'i çözer
    /* yerel HEAD uzak uçtan kaç commit geride: alt bardaki '<N> behind' çipinin tek kaynağı.
       null = bilinmiyor (fetch degraded / çevrimdışı) → chip GÖRÜNMEZ, konsola degrade uyarısı yazılır. */
    this.behind = opts.behind != null ? opts.behind : 3;
    this.pullResult = 'ok';   // prototipte ff-only pull sonucu: ok | dirty | diverged | error
    // multiFail: Failure sahnesi için ekstra hata üreten projeler (will-build zinciri içinden)
    // baseFails: def.fails yalnız multiFail koşusunda etkili — Hero ve manuel koşular temiz biter
    this.baseFails = !!opts.multiFail || !!opts.oneFail;
    // oneFail: Cycle sahnesi — tek kasıtlı hata (zincir ortasında), bağımlıları depIssue alır
    this.failOnly = opts.oneFail ? new Set(['ACME.Sales.Core']) : null;
    this.cycle = opts.cycle ? new Set(CYCLE_MEMBERS) : null; // döngüdeki projeler (Cycle sahnesi)
    this.inUse = opts.inUse || null; // demo: post-build kopyası çalışan bir programın tuttuğu dosyaya çarpan proje
    this.extraFails = opts.multiFail ? new Set(['ACME.Parts.Catalog', 'ACME.Service.Workshop', 'ACME.Reporting.Core']) : null;
    this.p = {};
    const rng = this.rng;
    P.forEach((pd) => {
      const st = this.p[pd.name] = this._blankState(pd, rng);
      /* araç dışında derlenmiş örnek: çıktısı diskte, defterde kaydı yok → zaman kipi */
      st.timeMode = this._timeMode(pd.name);
      if (st.timeMode) { st.lastFailed = false; if (st.lastBuiltAt == null) st.lastBuiltAt = -3 * 3600000; }
      st.builtCfg = this._builtCfg(pd.name);
    });
    /* allClean sahnesi: her şey güncel — 'hiç derlenmemiş' ya da 'hatalı' proje olamaz */
    if (this.allClean) P.forEach((pd) => {
      const st = this.p[pd.name];
      st.lastFailed = false;
      if (st.lastBuiltAt == null) st.lastBuiltAt = -Math.round((2 + rng() * 5) * 3600000);
    });
    if (opts.synced) this._applySync();
  };
  SimEngine.prototype._blankState = function (pd, rng) {
    const r = rng || this.rng;
    /* Son BAŞARILI derlemenin yaşı — satırdaki 'up to date · 2h' etiketini besler. simT 0'dan
       ilerlediği için geçmiş negatif zamanda tutulur; ~%7 proje hiç derlenmemiş olarak doğar. */
    const roll = r();
    const age = roll < 0.07 ? null
      : roll < 0.34 ? 12 * 60000 + r() * 78 * 60000        // 12–90 dk
        : roll < 0.86 ? 2 * 3600000 + r() * 7 * 3600000    // 2–9 saat
          : 26 * 3600000 + r() * 70 * 3600000;             // 1–4 gün
    return {
      def: pd,
      status: 'discovered',
      fresh: true,               // başlangıç modu (düz şerit + 4 yaylı nokta) — ilk derleme işlemiyle düşer
      will: 'unknown',           // dirty | clean | unknown (plan kanalı)
      reason: null,              // changed | dependency | neverBuilt | lastFailed — satırdaki karar etiketi
      lastBuiltAt: age == null ? null : -Math.round(age), // null = hiç derlenmemiş (çıktı yok)
      lastFailed: false,         // son derleme denemesi hata verdi mi
      builtSha: sha7(r),         // o çıktının commit'i — YALNIZ proje logu başlığında görünür
      dur: Math.round(pd.dur * (1.05 + r() * 0.5)),
      startAt: null, endAt: null, doneDur: null, depIssue: null,
      cleaned: false,            // -t:Clean koşusunda temizlendi: satır gri kalır, 'never built' yazar
      log: [], logPlan: null,
    };
  };
  /* Proje kümesi çalışma anında değişebilir (ayarlarda harici proje eklenir/çıkarılır).
     Tam reset beklemeden state haritasını P ile hizalar — eksik projeler başlangıç modunda doğar,
     kalkanlar düşer. UI hiçbir karede tanımsız state okumaz. */
  SimEngine.prototype.syncProjects = function () {
    const self = this, live = {};
    P.forEach(function (pd) {
      live[pd.name] = true;
      if (!self.p[pd.name]) self.p[pd.name] = self._blankState(pd);
      else self.p[pd.name].def = pd;
    });
    Object.keys(this.p).forEach(function (k) { if (!live[k]) delete self.p[k]; });
  };
  /* bu koşu hızlı kontrol mü: workspace tamamen temiz (allClean) ya da stale set boş (checkOnly) */
  SimEngine.prototype._fastCheck = function () { return this.allClean || this.checkOnly; };
  SimEngine.prototype._isCycle = function (name) { return !!(this.cycle && this.cycle.has(name)); };

  /* ---- satırın karar etiketi (v1.16.0) ----
     Bir projenin derlenip derlenmeyeceğine commit değil, kendi dosyalarının DİSKTEKİ içeriği karar
     verir; etiket o kararın gerekçesini söyler. Öncelik: hiç derlenmemiş > son derleme hata verdi >
     kendi dosyası değişti > bağımlılığı değişti. Ana repo ve harici kökler için tek kural. */
  const OUTSIDE_CFG = 'Debug'; // demo: dışarıdaki derlemenin configuration'ı
  SimEngine.prototype._builtCfg = function (name) {
    if (Object.prototype.hasOwnProperty.call(this.ledger, name)) return this.ledger[name];
    return byName[name] && byName[name].outside ? null : 'Debug';
  };
  SimEngine.prototype._timeMode = function (name) {
    return !!(byName[name] && byName[name].outside) && !Object.prototype.hasOwnProperty.call(this.ledger, name);
  };
  /* başarılı derleme deftere yazılır: configuration kaydı, zaman kipi biter */
  SimEngine.prototype._recordBuilt = function (name) {
    this.ledger[name] = this.cfg;
    const st = this.p[name];
    st.timeMode = false; st.builtCfg = this.cfg;
  };
  /* gerekçe (uygulama WillBuildReason + DecisionLabel): defter kipinde changed · config (imza yalnız configuration'la
     değişti — 'last built in Debug') · dependency; zaman kipinde outputMissing (bu configuration'ın çıktısı yok) ·
     newer (kendi dosyası çıktıdan yeni) · behind (yeniden derlenecek bir bağımlılığın arkasında, §7.6) */
  SimEngine.prototype._reasonFor = function (name, roots) {
    const st = this.p[name];
    if (st.lastBuiltAt == null) return 'neverBuilt';
    if (st.lastFailed) return 'lastFailed';
    const tm = this._timeMode(name);
    if (tm && this.cfg !== OUTSIDE_CFG) return 'outputMissing';
    if (roots && roots.has(name)) return tm ? 'newer' : 'changed';
    const b = this._builtCfg(name);
    if (!tm && b && b !== this.cfg) return 'config';
    return tm ? 'behind' : 'dependency';
  };
  /* roots = kendi dosyaları değişmiş projeler; kalan bayat projeler bağımlılık kapanışından gelir */
  SimEngine.prototype._setReasons = function (roots) {
    const self = this;
    P.forEach(function (pd) {
      const st = self.p[pd.name];
      st.reason = st.will === 'dirty' ? self._reasonFor(pd.name, roots) : null;
    });
  };
  /* kapsam bağımlılık kapanışıyla büyüdüğünde: yalnız etiketsiz kalanları doldurur */
  SimEngine.prototype._fillReasons = function () {
    const self = this;
    P.forEach(function (pd) {
      const st = self.p[pd.name];
      if (st.will !== 'dirty') { st.reason = null; return; }
      if (!st.reason) st.reason = self._reasonFor(pd.name, null);
    });
  };
  /* Zorlamalı kapsam (Rebuild): disk olgusu EZİLMEZ — içeriği güncel satır 'up to date · 2h'
     yazmaya devam eder, kapsamı şerit/nokta anlatır. Yalnız disk olguları işlenir. */
  SimEngine.prototype._factReasons = function () {
    const self = this;
    P.forEach(function (pd) {
      const st = self.p[pd.name];
      if (st.will !== 'dirty') { st.reason = null; return; }
      if (['changed', 'dependency', 'config', 'behind', 'newer', 'outputMissing'].indexOf(st.reason) >= 0) return;
      st.reason = st.lastBuiltAt == null ? 'neverBuilt' : st.lastFailed ? 'lastFailed' : null;
    });
  };

  SimEngine.prototype._applySync = function () {
    const self = this;
    /* Konfigürasyon değiştiyse (allDirty) her projenin bu konfigürasyondaki çıktısı bayatır → hepsi 'modified'. */
    const roots = this.allDirty ? new Set(P.map((pd) => pd.name)) : new Set(P.filter((pd) => pd.dirty).map((pd) => pd.name));
    /* tohum: hiç derlenmemiş · hatalı · son derlemesi başka configuration'da (defter) · bu configuration'ın çıktısı yok (zaman kipi) */
    const forced = new Set(P.filter((pd) => {
      const st = self.p[pd.name], tm = self._timeMode(pd.name), b = self._builtCfg(pd.name);
      st.timeMode = tm; st.builtCfg = b;
      return st.lastBuiltAt == null || st.lastFailed || (tm ? self.cfg !== OUTSIDE_CFG : !!b && b !== self.cfg);
    }).map((pd) => pd.name));
    const wb = this.allClean ? new Set() : computeWillBuild(this.allDirty, forced);
    /* döngü grubu tek birim: bir üye kirliyse hepsi kirli (bileşik imza, §7.3) — Build grubu turlarla derler */
    if (this.cycle && CYCLE_MEMBERS.some((n) => wb.has(n))) CYCLE_MEMBERS.forEach((n) => wb.add(n));
    this.willBuild = wb;
    P.forEach((pd) => {
      const st = this.p[pd.name];
      st.status = 'discovered';
      st.fresh = true; // sync sonrası başlangıç modu — renk yok, plan gösterimi yok
      st.will = wb.has(pd.name) ? 'dirty' : 'clean';
    });
    this._setReasons(roots); // satırların karar etiketi — renk olmadan da neyin bayat olduğunu okutur
    if (!this.op) this.op = { label: 'SYNC', target: null };
    this.phase = 'idle';
  };
  SimEngine.prototype.at = function (delay, fn) { this.todo.push({ at: this.simT + delay, fn }); };  SimEngine.prototype.nid = function () { return ++this._id; };
  SimEngine.prototype.wall = function () { return fmtClock(this.simT); };

  SimEngine.prototype.say = function (type, text) {
    this.narrative.push({ id: this.nid(), type, time: this.wall(), text });
    if (this.narrative.length > 2000) this.narrative.splice(0, this.narrative.length - 2000); // bellek penceresi (uygulama 2 000)
  };
  /* stream olayı: sakinse daktilo, fırtınada/hatada anında */
  SimEngine.prototype.emit = function (kind, project, text) {
    const burst = (this.simT - this._lastEmitT) < 340;
    this._lastEmitT = this.simT;
    const instant = this.instantMode || burst || kind === 'fail';
    this.stream.push({ id: this.nid(), time: this.wall(), kind, project, text, instant });
    if (this.stream.length > 260) this.stream.splice(0, this.stream.length - 260);
  };

  /* ---- proje logu → anlatı (uygulama OnProjectLog + CompactNarrative) ----
     Kompakt kip işlem başında dondurulur: projenin ilk satırından önce 'building <ad>' başlığı, sonra yalnız hatalar ve
     ilk 3 farklı uyarı (motorun kendi 'warning:' notları hep geçer); gizlenen uyarılar proje bitince tek özet satırıdır.
     Kapalıyken her satır olduğu gibi anlatıya akar. Proje sayfası her zaman tamdır. */
  SimEngine.prototype._routeLog = function (name) {
    const st = this.p[name];
    for (let i = st._nIx || 0; i < st.log.length; i++) this._narrLine(name, st.log[i]);
    st._nIx = st.log.length;
  };
  SimEngine.prototype._narrLine = function (name, l) {
    if (!this.compactRun) { this.say(l.type, l.text); return; }
    const C = this._compactW || (this._compactW = {});
    let w = C[name];
    if (!w) { w = C[name] = { order: Object.keys(C).length, shown: 0, hidden: 0, codes: {}, seen: {} }; this.say('info', 'building ' + name); }
    const k = classifyLine(l.text);
    if (k.type === 'error') { this.say('error', l.text); return; }
    if (k.type !== 'warn') return;
    if (k.own) { this.say('warn', l.text); return; }
    if (w.seen[l.text]) return; // tekrar: ne gösterilir ne sayılır
    w.seen[l.text] = 1;
    if (w.shown < 3) { w.shown++; this.say('warn', l.text); return; }
    const c = k.code || 'other';
    w.codes[c] = (w.codes[c] || 0) + 1;
    w.hidden++;
  };
  /* kilitli kopya notu (uygulama InUseCopyInvoker.Brief / Line): Replace files in use kapalıysa dosya yerinde kalır (warn),
     açıksa kenara taşınıp yeniden kopyalanır (info). Proje sonucu değişmez — çıkış kodu MSBuild'indir. */
  SimEngine.prototype._inUseNote = function (name) {
    if (this.inUse !== name || this.cleanRun) return;
    const holder = 'ACME.Client.exe', subject = name + ': ' + name + '.dll in D:\\src\\ACME\\bin\\' + this.cfg;
    if (this.replaceInUse) {
      this.say('info', subject + ' was held by ' + holder + ' (PID 14872) — moved aside and copied again; the running program keeps the previous build until it restarts');
      this.emit('info', name, name + ' in use by ' + holder + ' — replaced');
    } else {
      this.say('warn', 'warning: ' + subject + ' is held by ' + holder + ' (PID 14872) — the copy kept the previous build. Turn on Replace files in use in Settings, then build this project again from its row');
      this.emit('warn', name, name + ' in use by ' + holder + ' — not replaced; turn on Replace files in use');
    }
  };
  SimEngine.prototype._compactDone = function (name) {
    const w = this._compactW && this._compactW[name];
    if (!w || !w.hidden) return;
    const codes = Object.keys(w.codes).sort(function (a, b) { return w.codes[b] - w.codes[a] || (a < b ? -1 : a > b ? 1 : 0); })
      .slice(0, 3).map(function (c) { return c + ' ×' + w.codes[c]; }).join(', ');
    this.say('warn', name + ': ' + plural(w.hidden, 'more warning') + ' — ' + codes + ' — see the project log');
    w.hidden = 0; w.codes = {};
  };

  /* ---- işlem koreografisi: her işlem aynı borudan geçer ----
     _beginOp: konsol + stream temizlenir, kalıcı işlem etiketi yazılır.
     _neutralize: önceki koşunun tüm izleri silinir — herkes düz gri (fresh düşer).
     _mark: kapsam rastgele sırayla amber'a yanar, griler veda eder, sonra kapsam aynı sırayla koşu
     seviyesine iner (sıralı teslim dalgası) — koşu bu kuyrukla örtüşerek başlar. */
  SimEngine.prototype._beginOp = function (label, target) {
    this.op = { label, target: target ? shortName(target) : null };
    this.cleanRun = false;
    this.compactRun = this.compact !== false; // kompakt kip işlem başında oturur
    this._compactW = {};
    if (this._endTimers) this._clearEndTimers();
    this.endStep = null;
    this.endOrder = {};
    this.narrative = [];
    this.stream = [];
    this.taskResult = null;
    this.lastFail = null;
    this.checkDur = null;
  };
  /* tıklamanın kaydı (RunViewModel.RunRequestedLine): 'build requested' · tek projede '— X (single project)' */
  SimEngine.prototype._requested = function (mode, target) {
    this.say('info', mode + ' requested' + (target ? ' — ' + target + ' (single project)' : ''));
  };
  SimEngine.prototype._planLines = function () {
    this.say('info', 'Scanning solutions (' + SOLUTIONS.length + ')');
    this.say('info', 'Reading HintPath/Compile items (' + P.length + ' projects)');
    this.say('info', 'Dependency graph — ' + (this.cycle ? 1 : 0) + ' cycles');
    this.say('info', 'Build order resolved (' + P.length + ')');
    this.say('info', 'Computing incremental state (' + P.length + ' projects)');
  };
  SimEngine.prototype._neutralize = function () {
    const self = this;
    this.markStep = null;
    P.forEach(function (pd) {
      const st = self.p[pd.name];
      st.status = 'discovered'; st.fresh = false; st.cleaned = false;
      st.startAt = null; st.endAt = null; st.doneDur = null; st.depIssue = null;
      st.log = []; st.logPlan = null; st._nIx = 0;
    });
    this.stopStage = null;
    this.activeLine = null; this.doneAt = null; this.stoppedAt = null; this.runStartT = null;
    this._building = []; this._finishedWB = 0; this._eta = null;
  };
  SimEngine.prototype._clearMarkTimers = function () {
    (this._markTimers || []).forEach(function (h) { clearTimeout(h); });
    this._markTimers = [];
  };
  SimEngine.prototype._mark = function (scope, fn) {
    const self = this;
    if (this.noMark || !scope.size) { fn(); return; }
    this.marked = scope;
    const n = scope.size;
    /* dalga temposu: 110ms/node, ama toplam ~1.1s'i aşmaz (36 projede sıkışır) */
    this.markStagger = n > 1 ? Math.min(110, Math.round(1100 / (n - 1))) : 0;
    this.markOrder = {};
    /* dalga RANDOM belirir (kullanıcı kararı): sıra karışık, derleme sırasından bağımsız */
    const names = [];
    P.forEach(function (pd) { if (scope.has(pd.name)) names.push(pd.name); });
    const rng = mulberry32(7331 + this.runCount * 17);
    for (let i = names.length - 1; i > 0; i--) { const j = Math.floor(rng() * (i + 1)); const t = names[i]; names[i] = names[j]; names[j] = t; }
    names.forEach((nm, ix) => { this.markOrder[nm] = ix; });
    /* teslim dalgası (1.18.0): kapsam yandığı sırayla söner — 40ms/node, kuyruk ≤700ms'e sıkışır */
    this.settleStagger = n > 1 ? Math.min(40, Math.round(700 / (n - 1))) : 0;
    const W = this.markStagger * (n - 1) + 380; // son node'un yanması + tampon
    this.phase = 'marking';
    this.markStep = 'neutral'; // 0–440: düz gri anı (geçiş + küçük bekleyiş)
    /* Koreografi GERÇEK ZAMANDA koşar (CSS geçişleriyle aynı saat): sim saati yalnız koşu/sync'te
       ilerlediği ve rAF durduğunda (arka plan sekme) hiç tıklamadığı için işaretleme asılmıyor. */
    this._clearMarkTimers();
    const at = function (ms, f) { self._markTimers.push(setTimeout(f, ms)); };
    [['wave', 440], ['hold', 440 + W], ['dimenv', 740 + W], ['settle', 1300 + W]]
      .forEach(function (s) { at(s[1], function () { if (self.phase === 'marking') { self.markStep = s[0]; if (self.onFrame) self.onFrame(); } }); });
    /* koşu teslim kuyruğuyla ÖRTÜŞEREK başlar: ölü bekleyiş yok, koşu başında ikinci ani sönüm yok */
    at(2060 + W, function () {
      if (self.phase !== 'marking') return;
      self.marked = null; self.markStep = null; self._markTimers = [];
      fn();
      if (self.onFrame) self.onFrame();
    });
  };

  /* -------- akış başlatıcılar -------- */
  SimEngine.prototype.startSync = function () {
    if (this.busy()) return;
    const self = this;
    this.op = { label: 'SYNC', target: null };
    this.taskResult = null;
    this.checkOnly = false;
    this.phase = 'syncing';
    /* keşif (1.24.0): kaynaklar tek tek okunur — önce harici projeler (derleme sırasının başı), sonra
       repository solution'ları. Bitene kadar proje kümesi bilinmez → iki panel ikon + metin gösterir;
       liste yalnız ARTAN sayıları yazar (repo / external), toplam/payda yok. */
    const DSTEP = 170;
    const sl = SOLUTIONS.map(function (s) { return { n: s.n, ext: P.some(function (p) { return p.sln === s.name && p.ext; }) }; });
    /* ilk Sync (kopya başına): kaynak dosyalar indekslenir — şerit 'indexing source files · d/t', çubuk amber dolar */
    const idx = !this.indexedRoots.has(this.rootKey);
    this.indexedRoots.add(this.rootKey);
    const A0 = 260 + sl.length * DSTEP + 60, IDX_STEPS = 12, IDX_MS = idx ? IDX_STEPS * 90 + 60 : 0;
    const T = 260 + sl.length * DSTEP + 100 + IDX_MS;
    this.syncMs = T;
    this.discover = { repo: 0, ext: 0, hasExt: P.some(function (p) { return p.ext; }) };
    /* şerit adımları (RibbonText.SyncLine): Sync… → fetching origin/<branch>… → analyzing N projects… → indexing d/t */
    this.syncStep = { step: 'start' };
    this.at(70, function () { if (self.syncStep) self.syncStep = { step: 'fetching' }; });
    this.at(260, function () { if (self.syncStep) self.syncStep = { step: 'analyzing', found: 0 }; });
    this.say('cmd', 'git fetch origin ' + (this.branchName || 'main'));
    sl.forEach(function (s, k) {
      self.at(260 + (k + 1) * DSTEP, function () {
        const d = self.discover; if (!d) return;
        self.discover = { repo: d.repo + (s.ext ? 0 : s.n), ext: d.ext + (s.ext ? s.n : 0), hasExt: d.hasExt };
        if (self.syncStep && self.syncStep.step === 'analyzing') self.syncStep = { step: 'analyzing', found: self.discover.repo + self.discover.ext };
      });
    });
    if (idx) {
      const total = P.length * 137 + 23;
      for (let i = 0; i <= IDX_STEPS; i++) {
        self.at(A0 + i * 90, function () { if (self.syncStep) self.syncStep = { step: 'indexing', done: Math.round(total * i / IDX_STEPS), total }; });
      }
    }
    this.at(260, function () {
      /* fetch uzak ucu çözdü: HEAD satırı info tonunda (PlanProgressLines.HeadDistance). Çevrimdışıysa
         fetch uyarısı yazılır ve HEAD satırı mesafesiz kalır (SyncWorkspaceService.MeasureRemoteAsync). */
      const rem = 'origin/' + (self.branchName || 'main');
      if (self.behind == null) {
        self.say('warn', 'warning: git fetch failed — continuing against the local HEAD; distance from ' + rem + ' is unknown (could not resolve host git.acme.local)');
        self.say('info', 'HEAD ' + self.targetSha);
      } else self.say('info', 'HEAD ' + self.targetSha + ' · ' + (self.behind > 0 ? plural(self.behind, 'commit') + ' behind ' + rem : 'up to date with ' + rem));
    });
    /* tarama + plan satırları keşiften sonra (SyncWorkspaceService: Scanning → Reading → Dependency graph → Build order) */
    this.at(A0 - 30, function () {
      self.say('dim', 'Scanning solutions (' + SOLUTIONS.length + ')');
      self.say('dim', 'Reading HintPath/Compile items (' + P.length + ' projects)');
      self.say('dim', 'Dependency graph — ' + (self.cycle ? 1 : 0) + ' cycles');
      self.say('info', 'Build order resolved (' + P.length + ')');
      /* ilk indeksleme (IndexingReport): açılış ve kapanış satırı — eşik 500 dosya */
      if (idx) self.say('info', 'Indexing ' + (P.length * 137 + 23) + ' source files — later runs reuse the index');
    });
    if (idx) this.at(A0 + IDX_STEPS * 90 + 20, function () {
      self.say('info', 'Indexed ' + (P.length * 137 + 23) + ' source files in ' + Math.max(1, Math.floor(IDX_STEPS * 90 / 1000)) + 's');
    });
    this.at(T, function () {
      self.discover = null;
      self.syncStep = null;
      self.revealAt = self.simT;
      self._applySync();
      const n = self.willBuild.size;
      if (self.allClean) {
        self.say('info', 'Sync complete — no changes, ' + P.length + ' projects up to date');
      } else {
        const roots = P.filter(function (p) { return p.dirty; }).length;
        /* uygulamada 'config changed' biçimi yok: her zaman 'N changed projects, M to build' */
        self.say('info', 'Sync complete — ' + roots + ' changed projects' + ', ' + n + ' to build');
        self.say('dim', (P.length - n) + ' projects up to date (will skip)');
      }
      /* akış satırı (StreamText.Sync): döngü eki yok — kirli grup Build'in planındadır */
      self.emit('sync', null, 'Sync — ' + self.willBuild.size + ' to build, ' + (P.length - self.willBuild.size) + ' up to date'); // StreamText.Sync — hep aynı biçim
    });
  };

  /* ---- bitmiş bir Sync'in konsol dökümü (sahneler 'synced' açılır): uygulama açılışta Sync koşar ve konsol
     onun satırlarını taşır (SyncWorkspaceService) — uydurma bir açılış satırı yoktur ---- */
  SimEngine.prototype.sayIdleSync = function (changed) {
    const rem = 'origin/' + (this.branchName || 'main'), n = this.willBuild.size;
    this.say('cmd', 'git fetch origin ' + (this.branchName || 'main'));
    this.say('info', 'HEAD ' + this.targetSha + ' · ' + (this.behind > 0 ? plural(this.behind, 'commit') + ' behind ' + rem : 'up to date with ' + rem));
    this.say('dim', 'Scanning solutions (' + SOLUTIONS.length + ')');
    this.say('dim', 'Reading HintPath/Compile items (' + P.length + ' projects)');
    this.say('dim', 'Dependency graph — ' + (this.cycle ? 1 : 0) + ' cycles');
    this.say('info', 'Build order resolved (' + P.length + ')');
    if (n === 0) { this.say('info', 'Sync complete — no changes, ' + P.length + ' projects up to date'); return; }
    this.say('info', 'Sync complete — ' + (changed != null ? changed : P.filter(function (p) { return p.dirty; }).length) + ' changed projects, ' + n + ' to build');
    this.say('dim', (P.length - n) + ' projects up to date (will skip)');
  };

  /* ---- ff-only pull (alt bardaki '<N> behind' çipi, v1.16.0) ----
     fetch → 'yalnız geride miyim' kontrolü → merge --ff-only. Asla merge commit'i, asla rebase,
     kirli ağaca dokunmaz. Araç kendiliğinden pull ETMEZ; yalnız kullanıcı çipe basarsa ve yalnız
     aktif branch'te çalışır. Prototipte sonuç `pullResult` ile seçilir; varsayılan başarı. */
  SimEngine.prototype.pullRemote = function () {
    if (this.busy() || this.behind == null || this.behind <= 0) return null;
    const branch = this.branchName || 'main';
    const rem = 'origin/' + branch;
    /* uygulama (SupervisorHost.PullRepositoryAsync): tek komut satırı, sonra sonucun satırı */
    this.say('cmd', 'git merge --ff-only ' + rem);
    const res = this.pullResult || 'ok';
    if (res === 'dirty') {
      this.say('warn', 'warning: pull refused — uncommitted changes in the working tree; commit or stash them first');
      this.emit('warn', null, 'pull refused — uncommitted changes');
      return 'dirty';
    }
    if (res === 'diverged') {
      this.say('warn', 'warning: pull refused — local branch has diverged from ' + rem + '; reconcile it manually');
      this.emit('warn', null, 'pull refused — branch diverged');
      return 'diverged';
    }
    if (res === 'error') {
      this.say('error', 'Pull failed — could not resolve host git.acme.local');
      return 'error';
    }
    this.say('info', 'Pulled ' + rem + ' — fast-forward ' + this.targetSha + '..' + this.remoteSha);
    this.targetSha = this.remoteSha;
    this.behind = 0;
    return 'ok';
  };

  /* ---- build öncesi harici pull (Settings → External projects) ----
     Yol başına TEK working copy: aynı path'ten doğan projeler tek kopyadan gelir.
     Sonuç uydurulmaz — o kopyanın projelerinden biri bu koşuda derleniyorsa güncelleme
     inmiş demektir (Fast-forward), değilse kopya zaten güncel. */
  SimEngine.prototype._externalRoots = function (limit) {
    const seen = {}, out = [];
    P.forEach(function (pd) {
      if (!pd.ext || !pd.path) return;
      if (limit && !limit.has(pd.name)) return;
      if (!seen[pd.path]) {
        seen[pd.path] = { path: pd.path, vcs: pd.vcs === 'tfvc' ? 'tfvc' : 'git', base: extBase(pd.path), names: [] };
        out.push(seen[pd.path]);
      }
      seen[pd.path].names.push(pd.name);
    });
    return out;
  };
  /* ExternalUpdater: kopya başına 'Updating external …' → (ff-only) → 'Updated external … → rev'. Bayrak kapalıyken
     hiçbir VCS komutu çalışmaz ve satır da yazılmaz; git çalışma kopyası bulunamayan kart uyarıyla olduğu gibi derlenir. */
  SimEngine.prototype._pullExternals = function (limit) {
    const self = this, roots = this._externalRoots(limit);
    if (!roots.length || !this.pullExternal) return;
    const hex7 = function (v) { return ('0000000' + (v >>> 0).toString(16)).slice(-7); };
    roots.forEach(function (r) {
      if (r.vcs !== 'git') { self.say('warn', "warning: external '" + r.base + "': no git working copy found above its path — building as-is"); return; }
      const h = hash32(r.path + '|' + self.runCount);
      self.say('info', "Updating external '" + r.base + "'");
      self.say('info', "Updated external '" + r.base + "' → " + hex7(h));
    });
  };

  SimEngine.prototype.startRun = function () {
    if (this.phase === 'running' || this.task) return;
    const self = this;
    this.taskResult = null;
    this.scope = null; this.scopeName = null; // tam koşu: kapsam sınırı yok
    this.runCount++;
    if (this.phase === 'boot' || this.willBuild.size === 0 && !this.allClean && this.phase !== 'idle') this._applySync();
    this.phase = 'running';
    this.runPar = this.maxPar; this.runMode = this.perfMode || null; // koşu kendi paralelliğini ve modunu dondurur
    this.runStartT = this.simT;
    this.doneAt = null;
    this.stoppedAt = null;
    this._building = [];
    this._finishedWB = 0;
    this._eta = null;
    P.forEach(function (pd) {
      const st = self.p[pd.name];
      st.status = self.willBuild.has(pd.name) ? 'queued' : 'discovered';
      st.startAt = null; st.endAt = null; st.doneDur = null; st.log = []; st.logPlan = null; st.depIssue = null; st._nIx = 0;
    });
    /* motorun planlama satırları (Program.cs planner): harici güncelleme → tarama → graf → sıra → incremental.
       Koşunun sonucu konsola yazılmaz — özet şeritte ve akıştadır (StreamText.Completed). */
    if (this.cleanRun) {
      this._planLines();
      this.emit('info', null, 'Clean started — ' + this.willBuild.size + ' projects, parallelism ' + this.maxPar);
      return;
    }
    this._initCycleGroup();
    this._pullExternals();
    this._planLines();
    this.emit('info', null, 'Build started — ' + this.willBuild.size + ' projects, parallelism ' + this.maxPar);
  };

  /* ---- düz Build / Rebuild'de kirli döngü grubu (§8.1, §8.8 — CycleCompilation) ----
     Grup dış upstream'i çözülünce koşar; üyeler turda sırayla derlenir (bir MSBuild yuvası), diğer projeler paralel akar.
     Tur 1: kirli üyeler (Rebuild: hepsi); tur 2: okuduğu kardeş yüzeyi kayan üye; sonra yakınsar. Ara tur sonucu yayılmaz:
     turu biten üye grubunu bekler (kuyruk rengi), grup bitince her üye tek sonucunu alır, ardından grubun hükmü. */
  const CYCLE_CAP = 3; // CycleRoundPolicy.RoundCap
  SimEngine.prototype._initCycleGroup = function () {
    this.cyc = null;
    if (!this.cycle || this.cleanRun || this.scope) return;
    const members = CYCLE_MEMBERS.filter((n) => this.cycle.has(n) && this.willBuild.has(n));
    if (!members.length) return;
    const inGroup = new Set(members);
    /* grubun altındakiler (üyelere geçişli bağımlı olanlar) upstream sayılmaz — grup bitince onlar koşar */
    const down = new Set(members);
    let grew = true;
    while (grew) {
      grew = false;
      P.forEach((pd) => { if (!down.has(pd.name) && pd.deps.some((d) => down.has(d))) { down.add(pd.name); grew = true; } });
    }
    const upstream = new Set();
    members.forEach((n) => byName[n].deps.forEach((d) => { if (!inGroup.has(d) && !down.has(d)) upstream.add(d); }));
    /* tur planı: tur 1 her kirli üye; tur 2 yalnız ikinci üye (okuduğu yüzey tur 1'de kaydı) */
    const rounds = [members.slice()];
    if (members.length > 1) rounds.push([members[1]]);
    this.cyc = { members, upstream, rounds, state: 'waiting', round: 0, queue: [], ix: 0, cur: null, startT: null, compiled: new Set(), spent: {}, leader: members[0] };
  };
  SimEngine.prototype._cycleRoundLine = function () {
    const G = this.cyc;
    return G && G.state === 'running' && G.round > 0 ? { round: G.round, cap: CYCLE_CAP } : null;
  };
  SimEngine.prototype._stepCycleGroup = function (t, par) {
    const G = this.cyc, self = this;
    if (!G || G.state === 'done') return;
    if (G.state === 'waiting') {
      if (this.phase === 'stopping') return;
      if (!Array.from(G.upstream).every((d) => self._resolved(d))) return;
      G.state = 'running'; G.startT = t;
    }
    if (G.cur) return; // üye derleniyor
    if (!G.queue.length && G.round < G.rounds.length) {
      if (this.phase === 'stopping') return; // graceful stop: yeni tur başlamaz
      G.round++; G.queue = G.rounds[G.round - 1].slice(); G.ix = 0;
      /* akış (StreamText.CycleRound): tur başına tek satır, lidere tıklatır */
      this.emit('info', G.leader, 'cycle round ' + G.round + '/' + CYCLE_CAP + ' — ' + plural(G.queue.length, 'member'));
    }
    if (G.queue.length) {
      if (this.phase === 'stopping' || this._building.length >= par) return;
      const name = G.queue.shift();
      G.ix++;
      this._startBuild(name, t);
      G.cur = name; G.compiled.add(name);
      /* aktif satır grup ilerlemesini taşır (StreamText.CycleMemberDetail) */
      this.activeLine = { id: this.nid(), text: name + ' building… · member ' + G.ix + '/' + G.rounds[G.round - 1].length + ' · round ' + G.round + '/' + CYCLE_CAP };
      return;
    }
    /* son tur bitti: üyelerin tek sonucu, sonra grubun hükmü (CycleCompletedEvent — compiled sayılı biçim) */
    G.state = 'done';
    G.members.forEach(function (name) {
      const st = self.p[name];
      st.status = 'succeeded';
      st.doneDur = G.spent[name] || st.dur;
      st.will = 'clean'; st.reason = null; st.lastFailed = false;
      st.lastBuiltAt = t; st.builtSha = self.targetSha;
      self._recordBuilt(name);
      self._finishedWB++;
      self.emit('ok', name, name + ' built (' + fmtDur(st.doneDur) + ')');
      self._compactDone(name);
    });
    this.emit('ok', G.leader, 'cycle converged — ' + plural(G.members.length, 'member') + ', ' + G.compiled.size + ' compiled, '
      + plural(G.rounds.length, 'round') + ' · ' + fmtDur(t - G.startT));
  };

  SimEngine.prototype.stop = function () {
    if (this.phase === 'marking') { // koreografi iptali — koşu hiç başlamadı
      this._clearMarkTimers();
      this.todo = [];
      this.marked = null;
      this.markStep = null;
      this.scope = null; this.scopeName = null;
      this.phase = 'idle';
      this.say('warn', 'Cancelled — build not started');
      return;
    }
    if (this.phase !== 'running' && this.phase !== 'stopping') return;
    const self = this;
    this.stopStage = null;
    if (this.scope) { // tek proje koşusu iptali: kapsam discovered'a döner, faz idle
      const nm = this.scopeName;
      this.scope.forEach(function (n) {
        const st = self.p[n];
        if (st.status === 'building' || st.status === 'queued') { st.status = 'discovered'; st.startAt = null; }
      });
      this._building = [];
      this.scope = null; this.scopeName = null;
      this.phase = 'idle';
      this.activeLine = null;
      this.emit('info', null, 'Stopped — 0 remaining projects queued'); // StreamText.Stopped
      return;
    }
    if (this.resolveRun) { // Resolve iptali: scope discovered'a döner, faz idle — Build/Resolve yeniden kullanılabilir
      const R0total = this.resolveRun.total, R0fin = this.resolveRun.fin;
      (this._building || []).forEach(function (n) { self.p[n].status = 'discovered'; self.p[n].startAt = null; });
      this._building = [];
      P.forEach(function (pd) { if (self.p[pd.name].status === 'queued') self.p[pd.name].status = 'discovered'; });
      this.resolveRun = null;
      this.phase = 'idle';
      this.activeLine = null;
      this.emit('info', null, 'Stopped — ' + Math.max(0, R0total - R0fin) + ' remaining projects queued');
      return;
    }
    (this._building || []).forEach(function (n) { self.p[n].status = 'queued'; self.p[n].startAt = null; });
    this._building = [];
    this.phase = 'stopped';
    this.stoppedAt = this.simT;
    this.activeLine = null;
    if (this._hardN) this.say('info', 'stopped — ' + plural(this._hardN, 'in-flight compile') + ' terminated'); // HardStoppedLine
    this._hardN = 0;
    this.emit('info', null, 'Stopped — ' + (this.willBuild.size - this._finishedWB) + ' remaining projects queued');
  };

  /* Stop'un üç aşaması (StopText): Stop → graceful (kuyruk başlamaz, uçuştakiler biter: 'Stop now') →
     Stop now → hard ('Terminating…', uçuştakiler kesilir). Tek proje ve Resolve koşusu prototipte anında durur. */
  /* konsol: Stop basışı 'stop requested — …', Stop now 'stop now requested — …' (RunViewModel.StopAsync) */
  SimEngine.prototype._stopRequestedLine = function () {
    this.say('info', 'stop requested — no new projects will start; ' + (this._building || []).length + ' in flight will finish');
  };
  SimEngine.prototype.requestStop = function () {
    if (this.phase === 'marking') { this.stop(); return; }
    if (this.scope || this.resolveRun) { this._stopRequestedLine(); this.stop(); return; }
    if (this.phase === 'stopping') { this.stopNow(); return; }
    if (this.phase !== 'running') return;
    this._stopRequestedLine();
    if (!this._building.length) { this.stop(); return; }
    this.phase = 'stopping';
    this.stopStage = 'now';
  };
  SimEngine.prototype.stopNow = function () {
    if (this.phase !== 'stopping' || this.stopStage !== 'now') return;
    const self = this;
    this.stopStage = 'terminating';
    this.say('info', 'stop now requested — in-flight compiles will be terminated');
    this._hardN = this._building.length;
    this.at(450, function () { if (self.phase === 'stopping') self.stop(); });
  };
  /* Build menüsünün Clean'i (VS Clean Solution): grafın HER projesinde -t:Clean — harici ve döngü üyeleri dahil, kenarsız.
     Koşudur: CLEAN pill'i, açılış dalgası, '▸ Cleaning n/m', Stop durdurur; temizlenen satır gri kalır ve 'never built' yazar. */
  SimEngine.prototype.beginCleanRun = function () {
    if (this.busy() || this.phase === 'empty' || this.phase === 'boot') return false;
    const self = this;
    const all = new Set(P.map(function (pd) { return pd.name; }));
    this.checkOnly = false;
    this._beginOp('CLEAN');
    this._requested('clean');
    this._neutralize();
    this.cleanRun = true;
    this.willBuild = all;
    this._mark(all, function () { self.startRun(); });
    return true;
  };

  /* Build (durumdan): stale set = will dirty + hatalıların bağımlıları. Sync/reset YOK.
     Önce koreografi: konsol/stream temizlenir, herkes nötr griye döner, kapsam ×2 blink,
     sonra koşu başlar. Hata sonrası Build'de baseFails kapanır (fix varsayılır). */
  /* Ardışık Build: bir önceki koşu her şeyi güncellediğinde stale set boş kalır. Simulasyonda
     geliştirici çalışmaya devam etmiş sayılır: yeni bir HEAD gelir ve bir alt küme bayatlar.
     Kapanış (bağımlılar) hesaba katılır: çekirdek katmana dokunup tüm workspace'i bayatlatmamak
     için kapsam ~%45'in altında kalacak şekilde alt katmanlardan seçilir — gri kalan hep olur. */
  SimEngine.prototype._reseedChanges = function () {
    const self = this;
    const closure = function (seed) {
      const s = new Set(seed);
      let grew = true;
      while (grew) {
        grew = false;
        P.forEach(function (pd) {
          if (!s.has(pd.name) && pd.deps.some(function (d) { return s.has(d); })) { s.add(pd.name); grew = true; }
        });
      }
      return s;
    };
    const pool = P.filter(function (pd) { return !self._isCycle(pd.name) && (pd.deps || []).length > 0; }); // çekirdek katman hariç
    const cap = Math.round(P.length * 0.45);
    let picked = null;
    for (let attempt = 0; attempt < 24 && !picked; attempt++) {
      const rng = mulberry32(9173 + this.runCount * 41 + attempt * 7);
      const n = 2 + Math.floor(rng() * 3); // 2–4 doğrudan değişen
      const seed = new Set();
      let guard = 0;
      while (seed.size < n && guard++ < 60) seed.add(pool[Math.floor(rng() * pool.length)].name);
      const cl = closure(seed);
      if (cl.size <= cap) picked = { seed, cl, rng };
    }
    if (!picked) { // güvenli düşüş: en alt katmandan tek proje
      const rng = mulberry32(4001 + this.runCount);
      const leaf = pool[pool.length - 1 - Math.floor(rng() * Math.min(6, pool.length))].name;
      picked = { seed: new Set([leaf]), cl: closure(new Set([leaf])), rng };
    }
    this.targetSha = sha7(picked.rng);
    picked.cl.forEach(function (nm) { self.p[nm].will = 'dirty'; });
    this._setReasons(picked.seed); // doğrudan değişenler 'modified', kapanış 'affected'
    this.allClean = false;
    const affected = picked.cl.size - picked.seed.size;
    this.say('dim', 'HEAD ' + this.targetSha + ' — ' + plural(picked.seed.size, 'project') + ' changed'
      + (affected ? ' · ' + affected + ' affected' : '') + ' since last build');
  };

  SimEngine.prototype.beginBuild = function () {
    if (this.busy() || this.phase === 'empty' || this.phase === 'boot') return false;
    const self = this;
    if (this.failedList().length) { this.baseFails = false; this.extraFails = null; this.failOnly = null; this.lastFail = null; }
    const stale = function () { return new Set(P.filter((pd) => self.p[pd.name].will === 'dirty').map((pd) => pd.name)); };
    let dirty = stale();
    this._beginOp('BUILD');
    this._requested('build');
    if (!dirty.size && (this.phase === 'done' || this.phase === 'stopped')) { // tekrar Build — yeni değişiklikler gelir
      this._reseedChanges();
      dirty = stale();
    }
    let grew = true;
    while (grew) {
      grew = false;
      P.forEach((pd) => {
        if (!dirty.has(pd.name) && pd.deps.some((d) => dirty.has(d))) { dirty.add(pd.name); grew = true; }
      });
      if (self.cycle && CYCLE_MEMBERS.some((n) => dirty.has(n)) && CYCLE_MEMBERS.some((n) => !dirty.has(n))) { CYCLE_MEMBERS.forEach((n) => dirty.add(n)); grew = true; }
    }
    this.willBuild = dirty;
    P.forEach((pd) => { self.p[pd.name].will = dirty.has(pd.name) ? 'dirty' : 'clean'; });
    this._fillReasons(); // kapanışla eklenenler 'affected'; önceki gerekçeler korunur
    this.checkOnly = dirty.size === 0; // bayat iş yoksa hızlı kontrol koşusu
    this._neutralize(); // her işlem başta başa döner: önceki koşunun renkleri silinir
    if (this.checkOnly) { // hızlı kontrol: idle'a düş — startRun'daki _applySync guard'ı tetiklenmesin
      const s2 = this;
      this.phase = 'idle';
      this.doneAt = null; this.stoppedAt = null; this.runStartT = null; this.checkDur = null;
      this.at(420, function () { s2.startRun(); });
      return true;
    }
    this._mark(dirty, function () { self.startRun(); });
    return true;
  };

  /* Rebuild: tüm projeler, cache yok sayılır; döngü grubu tur 1'de her üyesini derler. */
  SimEngine.prototype.beginRebuild = function () {
    if (this.busy() || this.phase === 'empty' || this.phase === 'boot') return false;
    const self = this;
    if (this.failedList().length) { this.baseFails = false; this.extraFails = null; this.failOnly = null; this.lastFail = null; }
    this.allClean = false;
    this.allDirty = true;
    const all = new Set(P.map((pd) => pd.name));
    this.willBuild = all;
    P.forEach((pd) => { self.p[pd.name].will = 'dirty'; });
    this._factReasons(); // zorlamalı kapsam: içerik gerekçesi uydurulmaz
    this.checkOnly = false;
    this._beginOp('REBUILD');
    this._requested('rebuild');
    this._neutralize();
    this._mark(all, function () { self.startRun(); });
    return true;
  };

  /* ---- Satırdan tek proje: Build / Rebuild — koşulsuz, YALNIZ o proje ----
     Atlanmış, hatalı ya da döngü üyesi bir proje de tek başına derlenir; kapsam dışına
     dokunulmaz. Bayat bağımlılıklar derlenmez — son bilinen çıktı referans alınır (uyarı üçgeni). */
  SimEngine.prototype.beginProject = function (name, mode) {
    if (this.busy() || this.phase === 'empty' || this.phase === 'boot' || !byName[name]) return false;
    const self = this;
    if (this.p[name].status === 'failed' || this.failedList().length) { this.baseFails = false; this.extraFails = null; this.failOnly = null; this.lastFail = null; }
    this.checkOnly = false;
    const scope = new Set([name]);
    this.willBuild = new Set(scope);
    this.scope = scope;
    this.scopeName = name;
    this._beginOp(mode === 'rebuild' ? 'REBUILD' : 'BUILD', name);
    this._requested(mode === 'rebuild' ? 'rebuild' : 'build', name);
    this._neutralize();
    this._mark(scope, function () { self._startProjectNow(name, mode); });
    return true;
  };
  SimEngine.prototype._startProjectNow = function (name, mode) {
    this.runCount++;
    this.phase = 'running';
    this.runPar = this.maxPar; this.runMode = this.perfMode || null;
    this.runStartT = this.simT;
    this.doneAt = null; this.stoppedAt = null;
    this._building = []; this._finishedWB = 0; this._eta = null;
    this.p[name].status = 'queued';
    this._pullExternals(new Set([name])); // tek proje koşusu: yalnız o projenin kendi working copy'si
    this._planLines();
    const t = mode === 'rebuild' ? 'Rebuild' : 'Build';
    this.emit('info', name, t + ' started — ' + name + ' (single project)'); // StreamText.SingleProjectStarted
  };
  /* Satırdan Clean: yalnız o projenin çıktıları silinir; satır başlangıç moduna döner. Anında biter. */
  SimEngine.prototype.beginProjectClean = function (name) {
    if (this.busy() || this.phase === 'empty' || this.phase === 'boot' || !byName[name]) return false;
    this._beginOp('CLEAN', name);
    this._neutralize(); // diğer işlemlerle aynı kural: herkes varsayılan görünüme döner
    const st = this.p[name];
    st.status = 'discovered'; st.fresh = true; st.will = 'dirty';
    st.lastBuiltAt = null; st.reason = 'neverBuilt'; st.lastFailed = false; // çıktı silindi → 'never built'
    this.ledger[name] = null; st.timeMode = false; st.builtCfg = null; // defter kaydı silinir (§8.1)
    st.startAt = null; st.endAt = null; st.doneDur = null; st.depIssue = null;
    st.logPlan = null;
    st.log = [
      { type: 'cmd', text: 'msbuild ' + name + '.csproj /t:Clean /p:Configuration=' + this.cfg },
      { type: 'dim', text: 'removing bin/' + this.cfg + '/net8.0 · obj/' + this.cfg },
      { type: 'info', text: 'Clean complete — outputs removed; next build of ' + shortName(name) + ' is full' },
    ];
    this.willBuild.add(name);
    this.phase = 'idle';
    this.activeLine = null;
    /* uygulamada kapsamlı bir Clean koşusudur (CleanProjectAsync → RunMode.Clean): istek + planlama satırları, akışta başlangıç ve özet */
    this._requested('clean', name);
    this._planLines();
    this.emit('info', name, 'Clean started — ' + name + ' (single project)');
    this.emit('done', null, 'Completed — 1 succeeded · 0 skipped · ' + fmtDur(400));
    return true;
  };

  /* ---- Resolve cycles: döngü üyeleri + bayat bağımlılıkları, iki geçiş ----
     Pass 1: bağımlılıklar + üyeler son bilinen (bayat) referanslarla derlenir.
     Pass 2: yalnız üyeler taze referanslarla yeniden derlenir → yakınsar, temiz biter.
     Ardışık (paralellik 1) koşar — döngü çözümü sıraya duyarlıdır. Statü normal akar
     (queued/building/succeeded); derlenen üye finalde yalnız sonuç renginde (amber küp yok). */
  /* Resolve kapsamının kirli upstream'i (üyelerin dışı, önce derlenir) — Resolve düğmesinin tooltip'i de bunu sayar */
  SimEngine.prototype.resolveDeps = function () {
    const self = this;
    if (!this.cycle) return [];
    const members = CYCLE_MEMBERS.filter((n) => this.cycle.has(n));
    const inScope = new Set(members);
    const depList = [];
    const addDeps = (n) => {
      (byName[n].deps || []).forEach((d) => {
        if (inScope.has(d)) return;
        const ds = self.p[d];
        if (ds.will === 'dirty' || ds.status === 'failed') { inScope.add(d); depList.push(d); addDeps(d); }
      });
    };
    members.forEach(addDeps);
    depList.sort((a, b) => ORDER.indexOf(a) - ORDER.indexOf(b));
    return depList;
  };
  SimEngine.prototype.startResolve = function () {
    if (!this.cycle || this.busy() || this.phase === 'empty' || this.phase === 'boot') return false;
    const self = this;
    this.taskResult = null;
    this.runCount++;
    this.lastFail = null;
    const members = CYCLE_MEMBERS.filter((n) => this.cycle.has(n));
    const depList = this.resolveDeps();
    /* Resolve de artımlıdır (§8.1): bileşik imzası temiz grup (ör. Build az önce derledi) 'skipped — up to date' olur */
    const groupDirty = members.some((n) => self.p[n].will === 'dirty');
    const list = !groupDirty ? [] : depList.map((n) => ({ name: n, pass: 1, cyc: false }))
      .concat(members.map((n) => ({ name: n, pass: 1, cyc: true })))
      .concat(members.map((n) => ({ name: n, pass: 2, cyc: true })));
    const dirtyN = groupDirty ? members.length : 0, preN = groupDirty ? depList.length : 0;
    this._beginOp('RESOLVE');
    this._requested('cycles');
    this._neutralize();
    /* 1.12.0: dalgada yalnız üyeler amber'a yanar (nötr anda küpleri zaten amber). Bayat bağımlılık kapanışı
       gri kalır — sırası gelince building → sonuç rengi; finale onlar da katılır. (kullanıcı kararı) */
    this._mark(new Set(members), function () {
      members.forEach(function (n) { self.p[n].status = 'queued'; });
      self.phase = 'running';
      self.runPar = 1; self.runMode = self.perfMode || null; // döngü çözümü ardışık koşar
      self.resolveRun = { list, i: 0, fin: 0, total: dirtyN + preN, pass: 1, startT: self.simT, cycleN: members.length, depN: preN, curName: null, curCyc: false, curPass: 1, round: 0, member: 0, leader: members[0], spent: {}, skipped: !groupDirty };
      self._planLines(); // Cycles harici kopyaları güncellemez (D12)
      /* akış (StreamText.CyclesStarted / CycleRound / CycleCompleted): ara tur sonucu yazılmaz, grubun hükmü tek satır.
         Kırılım bu koşunun kirli kümesinden: üye ∩ kirli · kalan upstream */
      self.emit('info', null, 'Cycles started — ' + dirtyN + ' cycle members · ' + preN + ' prerequisites · up to 3 rounds');
      if (!groupDirty) members.forEach(function (n) { self.p[n].status = 'skipped'; self.emit('skip', n, n + ' skipped — up to date'); });
    });
    return true;
  };

  SimEngine.prototype._processResolve = function () {
    const self = this, t = this.simT, R = this.resolveRun;
    if (!R) return;
    if (R.curName) {
      const st = this.p[R.curName];
      if (t < st.endAt) { // canlı log satırları
        const frac = Math.min(1, (t - st.startAt) / (st.endAt - st.startAt));
        while (st.log.length < st.logPlan.length - 1 && st.logPlan[st.log.length].at <= frac) {
          const l = st.logPlan[st.log.length];
          st.log.push({ type: l.type, text: l.text });
        }
        this._routeLog(R.curName);
        return;
      }
      st.doneDur = st.endAt - st.startAt;
      st.log = st.logPlan.map((l) => ({ type: l.type, text: l.text }));
      this._routeLog(R.curName);
      if (R.curCyc) { // üyenin tur sonucu tutulur: grubunu bekler (kuyruk), sonucu grup bitince
        st.status = 'queued';
        R.spent[R.curName] = (R.spent[R.curName] || 0) + st.doneDur;
      } else {
        this._compactDone(R.curName);
        st.status = 'succeeded';
        st.will = 'clean'; st.reason = null; st.lastFailed = false;
        st.lastBuiltAt = this.simT; st.builtSha = this.targetSha;
        this._recordBuilt(R.curName);
        R.fin++;
        this.emit('ok', R.curName, R.curName + ' built (' + fmtDur(st.doneDur) + ')');
      }
      this._building = [];
      R.curName = null;
      this.activeLine = null;
    }
    if (R.i >= R.list.length) {
      const el = t - R.startT;
      this.phase = 'idle';
      this.resolveRun = null;
      this.activeLine = null;
      const depTxt = R.depN ? ' (+' + R.depN + ' stale dependencies)' : '';
      if (!R.skipped) {
        /* grup bitti: her üye tek sonucunu alır, ardından grubun hükmü (CycleCompletedEvent — compiled sayılı biçim) */
        CYCLE_MEMBERS.filter((n) => self.cycle.has(n)).forEach(function (n) {
          const ms = self.p[n];
          ms.status = 'succeeded'; ms.doneDur = R.spent[n] || ms.doneDur;
          ms.will = 'clean'; ms.reason = null; ms.lastFailed = false;
          ms.lastBuiltAt = t; ms.builtSha = self.targetSha;
          self._recordBuilt(n);
          R.fin++;
          self.emit('ok', n, n + ' built (' + fmtDur(ms.doneDur) + ')');
          self._compactDone(n);
        });
        this.emit('ok', R.leader, 'cycle converged — ' + plural(R.cycleN, 'member') + ', ' + R.cycleN + ' compiled, 2 rounds · ' + fmtDur(el));
      }
      const cc = this.counts();
      this.emit('done', null, 'Completed — ' + cc.succeeded + ' succeeded · ' + cc.skipped + ' skipped · ' + fmtDur(el));
      this.taskResult = { kind: 'resolve', text: 'Cycles resolved — ' + R.cycleN + ' projects converged in 2 passes · outputs now current' };
      this._runEndFinale(); // 1.12.0: Resolve de her koşu gibi neon bitişle kapanır
      return;
    }
    const it = R.list[R.i++];
    if (it.cyc && it.pass !== R.round) { // yeni tur: tek akış satırı (cycle round r/cap — N members)
      R.round = it.pass; R.pass = it.pass; R.member = 0;
      this.emit('info', R.leader, 'cycle round ' + it.pass + '/3 — ' + plural(R.cycleN, 'member'));
    }
    const st = this.p[it.name];
    st.status = 'building';
    st.startAt = t;
    st.endAt = t + Math.max(600, Math.round(st.dur * (it.pass === 2 ? 0.45 : 1)));
    const plan = makeLog(st.def, this.cfg, mulberry32(it.name.length * 977 + this.runCount + it.pass * 13), false);
    /* üye logu MSBuild'in kendi çıktısıdır — araç içine döngü satırı eklemez */
    st.logPlan = plan;
    st.log = []; st._nIx = 0;
    this._building = [it.name];
    R.curName = it.name; R.curCyc = it.cyc; R.curPass = it.pass;
    if (it.cyc) R.member++;
    this.activeLine = { id: this.nid(), text: it.name + ' building…' + (it.cyc ? ' · member ' + R.member + '/' + R.cycleN + ' · round ' + it.pass + '/3' : '') };
  };

  /* ================== BAKIM GÖREVLERİ (Clean / Optimize) ==================
     Clean    : her solution için msbuild /t:Clean + bin/obj/artifacts silme. Çıktılar gittiği
                için tüm projeler discovered+dirty'ye döner — sonraki koşu tam derlemedir.
                Solution bittikçe o solution'ın projeleri anında sıfırlanır (liste + graf canlı).
     Optimize : NuGet restore (solution başına) + paket cache prune + bağımlılık indeksi +
                derleyici sunucusu ısıtma. Derleme durumunu DEĞİŞTİRMEZ.
     Ortak model: {dur,label,stream,run} adımları; her adım bitince konsola satır + stream'e
     olay basar. this.task UI'ın ilerleme kaynağıdır (şerit, buton spinner'ı, stream canlı satırı). */
  const TASK_TITLE = { clean: 'Cleaning', optimize: 'Optimizing' };

  SimEngine.prototype.busy = function () {
    return this.phase === 'running' || this.phase === 'stopping' || this.phase === 'syncing' || this.phase === 'marking' || !!this.task || !!this.work;
  };

  SimEngine.prototype.startTask = function (kind) {
    if (!TASK_TITLE[kind] || this.busy() || this.phase === 'empty') return false;
    const plan = kind === 'clean' ? this._cleanPlan() : this._optimizePlan();
    this._beginOp(kind === 'optimize' ? 'OPTIMIZE' : 'DEEP CLEAN');
    this.prevPhase = this.phase;
    this.phase = 'task';
    this.activeLine = null;
    this.task = {
      kind, title: TASK_TITLE[kind], steps: plan.steps, finish: plan.finish,
      i: 0, total: plan.steps.length, startT: this.simT,
      label: plan.steps[0].label, stream: plan.steps[0].stream,
    };
    this.say('info', plan.head); // 'clean requested' / 'optimize requested'
    this._runStep();
    return true;
  };

  SimEngine.prototype._runStep = function () {
    const self = this, t = this.task;
    if (!t || t.i >= t.steps.length) return;
    const st = t.steps[t.i];
    t.label = st.label; t.stream = st.stream;
    this.at(st.dur, function () {
      if (self.task !== t) return;
      st.run(self);
      t.i++;
      if (t.i >= t.steps.length) self._endTask();
      else self._runStep();
    });
  };

  SimEngine.prototype._endTask = function () {
    const t = this.task;
    if (!t) return;
    const el = this.simT - t.startT;
    this.task = null;
    const res = t.finish(this, el);
    this.phase = res.phase;
    /* uygulama: şeritte sonuç satırı yok — bakım işinin ardından zincirlenen Sync şeridi devralır */
    this.taskResult = null;
    this.startSync();
  };

  SimEngine.prototype.taskElapsed = function () { return this.task ? this.simT - this.task.startT : 0; };

  /* bir solution'ın çıktıları silindi → projeleri keşfedilmiş + kirli hâle döner */
  SimEngine.prototype._cleanSolution = function (sln) {
    const self = this;
    P.forEach(function (pd) {
      if (pd.sln !== sln) return;
      const st = self.p[pd.name];
      st.status = 'discovered'; st.will = 'dirty'; st.fresh = true;
      st.lastBuiltAt = null; st.reason = 'neverBuilt'; st.lastFailed = false; // çıktılar silindi
      st.startAt = null; st.endAt = null; st.doneDur = null; st.depIssue = null;
      st.log = []; st.logPlan = null;
      self.willBuild.add(pd.name); // çıktı silindi → dirty (döngü üyesi de: Build grubunu derler)
    });
  };

  /* Bakım işleri (uygulama CleanWorkspaceService / OptimizeWorkspaceService): ilerleme yalnız konsolda akar,
     akışa iş başına TEK kapanış satırı düşer (Sync tonu) — adım ya da canlı yazım satırı yok. */
  const byteSize = function (mb) { const b = mb * 1048576; if (b <= 0) return '0 B'; const u = ['B', 'KB', 'MB', 'GB', 'TB']; let v = b, i = 0; while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; } return i === 0 ? b + ' B' : (v < 10 ? v.toFixed(1) : String(Math.round(v))) + ' ' + u[i]; };
  SimEngine.prototype._cleanPlan = function () {
    const rng = mulberry32(9173 + this.runCount * 31);
    const tally = { dirs: 0, mb: 0 };
    SOLUTIONS.forEach(function (s) { tally.dirs += s.n * 2 + Math.floor(rng() * 4); tally.mb += Math.round(s.n * (60 + rng() * 110)); });
    const steps = [
      { dur: 900, run: function (e) { e.say('info', 'build state reset — ' + P.length + ' entries cleared, the next build compiles from scratch'); } },
      { dur: 1500, run: function () {} },
    ];
    return {
      head: 'clean requested',
      steps,
      finish: function (e) {
        e.allDirty = true;
        e.allClean = false;
        P.forEach(function (pd) { const st = e.p[pd.name]; st.will = 'dirty'; st.lastBuiltAt = null; st.reason = 'neverBuilt'; st.lastFailed = false; });
        e.runStartT = null; e.doneAt = null; e.stoppedAt = null; e._finishedWB = 0;
        e._building = []; e._eta = null; e.lastFail = null; e.checkDur = null;
        e.say('info', 'Clean complete — ' + P.length + ' projects · ' + tally.dirs + ' folders · ' + byteSize(tally.mb) + ' removed');
        e.emit('sync', null, 'Clean — ' + P.length + ' projects, ' + tally.dirs + ' folders, ' + byteSize(tally.mb) + ' freed');
        return { phase: 'idle' };
      },
    };
  };

  SimEngine.prototype._optimizePlan = function () {
    const rng = mulberry32(5501 + this.runCount * 17);
    const needy = 2 + Math.floor(rng() * 4), objN = 1 + Math.floor(rng() * 3), pruned = 20 + Math.floor(rng() * 40), mb = 300 + Math.round(rng() * 900);
    const steps = [
      { dur: 500, run: function (e) { e.say('info', 'checking NuGet packages — ' + P.length + ' projects, ' + needy + ' need restore'); } },
      { dur: 700, run: function (e) { e.say('cmd', 'msbuild -t:Restore -p:RestorePackagesConfig=true Acme.sln'); } },
      { dur: 400, run: function (e) { e.say('info', 'checking references — all resolved'); } },
      { dur: 400, run: function (e) { e.say('info', 'checking obj leftovers — ' + objN + (objN === 1 ? ' old-style project' : ' old-style projects') + ' with stale NuGet files'); } },
      { dur: 400, run: function (e) { e.say('info', 'checking caches — pruned ' + pruned + ' entries'); } },
    ];
    const prev = this.prevPhase;
    return {
      head: 'optimize requested',
      steps,
      finish: function (e) {
        const terms = [needy + ' restored', objN + ' obj cleaned', pruned + ' cache entries pruned', byteSize(mb) + ' reclaimed'];
        e.say('info', 'Optimize complete — ' + terms.join(' · '));
        e.emit('sync', null, 'Optimize — ' + terms.join(', '));
        return { phase: prev === 'task' || prev == null ? 'idle' : prev };
      },
    };
  };

  /* -------- ilerletme -------- */
  /* ---- bitiş koreografisi (6d — Neon tutuşma) ----
     Son proje bitince hepsi soluk kalır ve bekler → YALNIZ derlenenler random sırayla neon gibi
     titreyerek tutuşur → en sonda kalan tüm griler hep birlikte yumuşakça belirginleşir.
     Adımlar `eng.endStep`: hold · neon · bwait · grey · null (final). Gerçek zamanda koşar. */
  SimEngine.prototype._clearEndTimers = function () {
    (this._endTimers || []).forEach(function (h) { clearTimeout(h); });
    this._endTimers = [];
  };
  SimEngine.prototype._runEndFinale = function () {
    const self = this;
    const built = P.filter(function (pd) { const s = self.p[pd.name].status; return s === 'succeeded' || s === 'failed'; }).map(function (pd) { return pd.name; });
    this._clearEndTimers();
    this.endOrder = {};
    this.endStep = null;
    if (!built.length) return; // derlenen yok — koreografi yok
    const rng = mulberry32(5501 + this.runCount * 29);
    const sh = built.slice();
    for (let i = sh.length - 1; i > 0; i--) { const j = Math.floor(rng() * (i + 1)); const t = sh[i]; sh[i] = sh[j]; sh[j] = t; }
    sh.forEach(function (nm, ix) { self.endOrder[nm] = ix; });
    const n = sh.length;
    this.endStagger = n > 1 ? Math.min(150, Math.round(1500 / (n - 1))) : 0; // zincir en çok ~1.5s
    const chain = this.endStagger * (n - 1);
    const frame = function () { if (self.onFrame) self.onFrame(); };
    const at = function (ms, f) { self._endTimers.push(setTimeout(function () { f(); frame(); }, ms)); };
    this.endStep = 'hold'; // sarılar gitti, hepsi soluk bekler
    frame();
    at(900, function () { self.endStep = 'neon'; });
    at(900 + chain + 1150, function () { self.endStep = 'bwait'; });
    at(900 + chain + 1850, function () { self.endStep = 'grey'; });
    at(900 + chain + 3250, function () { self.endStep = null; self.endOrder = {}; });
  };

  SimEngine.prototype.advance = function (dt) {
    if (this.engine === 'silent') return; // motor cevap vermiyor: hiçbir olay gelmez, her şey donar
    this.simT += dt;
    const due = this.todo.filter((t) => t.at <= this.simT);
    this.todo = this.todo.filter((t) => t.at > this.simT);
    due.sort((a, b) => a.at - b.at).forEach((t) => t.fn());
    if (this.phase === 'running' || this.phase === 'stopping') { if (this.resolveRun) this._processResolve(); else this._processRun(); }
  };

  /* ---- workspace işi (checkout · pull · worktree kurulumu): pill + şerit satırı, süre dolunca done() ---- */
  const WORK_LABEL = { checkout: 'SWITCHING BRANCH', pull: 'SYNC', worktree: 'CREATING WORKTREE' };
  SimEngine.prototype.beginWork = function (kind, target, ms, done) {
    if (this.busy() || !WORK_LABEL[kind]) return false;
    const self = this;
    this.op = { label: WORK_LABEL[kind], target: null };
    this.taskResult = null;
    const w = { kind, target: target || null };
    this.work = w;
    this.at(ms, function () { if (self.work !== w) return; self.work = null; if (done) done(); });
    return true;
  };

  /* ---- motor ölümü / sessizliği / yeniden başlatma (prototip demo kancası: window.boDemo) ---- */
  SimEngine.prototype.engineDied = function (cause) {
    if (this.phase === 'running' || this.phase === 'stopping' || this.phase === 'marking') this.stop();
    if (this.phase === 'syncing') { this.todo = []; this.discover = null; this.syncStep = null; this.phase = 'boot'; }
    if (this.task) { this.task = null; this.todo = []; this.phase = this.phase === 'task' ? 'idle' : this.phase; }
    if (this.work) { this.work = null; this.todo = []; }
    this.engine = 'dead';
    this.engineMsg = 'Engine stopped unexpectedly (' + (cause || 'exit -1073741819') + ')';
  };
  SimEngine.prototype.engineSilent = function () {
    if (this.engine !== 'ok') return;
    this.engine = 'silent';
    this.engineMsg = 'Engine has stopped responding — no reply for 1m 30s · you can restart it';
  };
  SimEngine.prototype.restartEngine = function (version, onReady) {
    if (this.engine !== 'dead' && this.engine !== 'silent') return;
    const self = this;
    if (this.engine === 'silent') { // uçuştaki iş kaybedildi: koşu durur, Sync baştan
      this.engine = 'ok';
      if (this.phase === 'running' || this.phase === 'stopping' || this.phase === 'marking') this.stop();
      if (this.phase === 'syncing') { this.todo = []; this.discover = null; this.syncStep = null; this.phase = 'boot'; }
    }
    this.engine = 'waking';
    this.engineMsg = null;
    /* hazır yolu açılışla aynı (RunViewModel.OnEngineReady): 'Engine ready — v…' satırı, sonra Appended Sync */
    this.at(900, function () {
      self.engine = 'ok';
      if (version) self.say('info', 'Engine ready — v' + version);
      if (onReady) onReady();
    });
  };

  SimEngine.prototype._resolved = function (name) {
    const s = this.p[name].status;
    return s === 'succeeded' || s === 'failed' || s === 'skipped';
  };
  SimEngine.prototype._isFail = function (name) {
    if (!this.baseFails) return false;
    if (this.failOnly) return this.failOnly.has(name);
    return !!(this.p[name].def.fails || (this.extraFails && this.extraFails.has(name)));
  };

  /* Tek bir projeyi derlemeye al. Statü, depIssue, log planı ve aktif satır kuralları
     tam koşuda ve tek proje koşusunda BİREBİR aynı — tek giriş noktası. */
  SimEngine.prototype._startBuild = function (name, t) {
    const self = this;
    const st = this.p[name];
    if (this.cleanRun) { // -t:Clean: kenarsız, hatasız, kısa
      st.depIssue = null; st.status = 'building'; st.startAt = t;
      st.dur = Math.max(320, Math.round(st.def.dur * 0.18));
      st.endAt = t + st.dur;
      st.logPlan = [
        { at: 0, type: 'cmd', text: 'msbuild ' + name + '.csproj /t:Clean /p:Configuration=' + this.cfg },
        { at: 0.4, type: 'dim', text: 'removing bin/' + this.cfg + '/net8.0 · obj/' + this.cfg },
        { at: 1, type: 'dim', text: shortName(name) + ' cleaned — next build is full' },
      ];
      st.log = []; st._nIx = 0;
      this._building.push(name);
      this.activeLine = { id: this.nid(), text: name + ' cleaning…' };
      return;
    }
    const roots = [];
    st.def.deps.forEach(function (d) {
      const dp = self.p[d];
      if (dp.status === 'failed' && roots.indexOf(d) < 0) roots.push(d);
      /* bu koşuda derlenmeyecek bayat bağımlılık (döngü üyesi ya da kapsam dışı değişmiş proje) = son bilinen çıktı */
      if (dp.will === 'dirty' && dp.status !== 'succeeded' && !self.willBuild.has(d) && roots.indexOf(d) < 0) roots.push(d);
      (dp.depIssue || []).forEach(function (r) { if (roots.indexOf(r) < 0) roots.push(r); });
    });
    st.depIssue = roots.length ? roots : null;
    st.status = 'building';
    st.startAt = t;
    st.endAt = t + st.dur;
    st.logPlan = makeLog(st.def, this.cfg, mulberry32(name.length * 977 + this.runCount), this._isFail(name));
    if (st.depIssue) {
      const direct = st.def.deps.filter(function (d) { return self.p[d].status === 'failed'; });
      const staleDirect = st.def.deps.filter(function (d) { const dp = self.p[d]; return dp.will === 'dirty' && dp.status !== 'succeeded' && !self.willBuild.has(d); });
      let wl = direct.map(function (d) { return { at: 0.03, type: 'warn', dep: true, text: 'warning: ' + d + ' failed in this run — last successful output referenced (' + d + ')' }; })
        .concat(staleDirect.map(function (d) {
          return self._isCycle(d)
            ? { at: 0.03, type: 'warn', dep: true, text: 'warning: ' + d + ' is in a dependency cycle and was not rebuilt — last known output referenced' }
            : { at: 0.03, type: 'warn', dep: true, text: 'warning: ' + d + ' has pending changes and was not rebuilt in this run — last known output referenced' };
        }));
      /* RunCoordinator.DepIssueWarnLines: doğrudan olmayan kökler tek satırda, tam adlarıyla */
      const indirect = st.depIssue.filter(function (r) { return direct.indexOf(r) < 0 && staleDirect.indexOf(r) < 0; });
      if (indirect.length) wl = wl.concat([{ at: 0.03, type: 'warn', dep: true, text: 'warning: failure in dependency chain (' + indirect.join(', ') + ') — referenced outputs may be stale' }]);
      st.logPlan = [st.logPlan[0]].concat(wl, st.logPlan.slice(1));
    }
    st.log = []; st._nIx = 0;
    this._building.push(name);
    this.activeLine = { id: this.nid(), text: name + ' building…' };
  };

  SimEngine.prototype._processRun = function () {
    const self = this;
    const t = this.simT;

    /* 1 — biten build'ler */
    this._building.slice().sort((a, b) => self.p[a].endAt - self.p[b].endAt).forEach(function (name) {
      const st = self.p[name];
      if (t < st.endAt) return;
      self._building = self._building.filter((x) => x !== name);
      st.doneDur = st.endAt - st.startAt;
      st.log = st.logPlan.map((l) => ({ type: l.type, text: l.text })); // tam log
      self._routeLog(name); // kalan satırlar anlatıya (kompakt süzgeçten)
      const G = self.cyc;
      if (G && G.cur === name) { // turdaki derlemesi bitti: sonuç tutulur, üye grubunu bekler (CycleMemberHeldEvent → kuyruk)
        G.spent[name] = (G.spent[name] || 0) + st.doneDur;
        G.cur = null;
        st.status = 'queued';
        if (self.activeLine && self.activeLine.text.split(' ')[0] === name) self.activeLine = null;
        return;
      }
      self._finishedWB++;
      if (self.cleanRun) {
        st.status = 'succeeded'; st.cleaned = true;
        st.will = 'dirty'; st.reason = 'neverBuilt'; st.lastBuiltAt = null; st.lastFailed = false; st.fresh = false;
        self.ledger[name] = null; st.timeMode = false; st.builtCfg = null; // kayıt silindi (§8.1)
        self.emit('ok', name, name + ' cleaned (' + fmtDur(st.doneDur) + ')');
      } else if (self._isFail(name)) {
        st.status = 'failed';
        st.lastFailed = true; st.reason = 'lastFailed'; // satır 'failed · retry' yazar
        self.lastFail = { name, t };
        self.emit('fail', name, name + ' failed — ' + (name === 'ACME.Sales.Core' ? '2 errors' : '1 error') + ' (' + fmtDur(st.doneDur) + ')');
      } else {
        st.status = 'succeeded';
        st.will = 'clean'; // artık güncel — nokta griye döner
        st.reason = null; st.lastFailed = false;
        st.lastBuiltAt = t; st.builtSha = self.targetSha; // satır 'up to date · just now' yazar
        self._recordBuilt(name);
        self._inUseNote(name); // kilitli post-build kopyası (demo): akışta kısa not, konsolda ayrıntı
        if (st.depIssue) self.emit('ok', name, name + ' built — dependency issue (' + fmtDur(st.doneDur) + ')');
        else self.emit('ok', name, name + ' built (' + fmtDur(st.doneDur) + ')');
      }
      self._compactDone(name);
    });

    /* 2 — atlanacaklar (bağımlılıkları çözülen temiz projeler) — dalga halinde.
       Tek proje koşusunda kapsam dışına HİÇ dokunulmaz: onlar nötr gri kalır. */
    let skipBudget = this.scope || this.phase === 'stopping' ? 0 : this.instantMode ? 99 : (this._fastCheck() ? 12 : 3);
    for (const name of ORDER) {
      if (skipBudget <= 0) break;
      const st = this.p[name];
      if (this.scope && !this.scope.has(name)) continue; // tek proje koşusu: kapsam dışına dokunma
      if (st.status !== 'discovered' || this.willBuild.has(name)) continue;
      if (!st.def.deps.every((d) => self._resolved(d))) continue;
      st.status = 'skipped';
      st.doneDur = null;
      if (this._isCycle(name) && st.will === 'dirty') this.emit('skip', name, name + ' skipped — in dependency cycle'); // SkipReasons.InDependencyCycle
      else this.emit('skip', name, name + ' skipped — up to date');
      skipBudget--;
    }

    /* 3 — yeni build başlat. Başarısız bağımlılık BLOKLAMAZ: proje son başarılı
       çıktıyla yine derlenir, kök hata(lar) depIssue olarak zincir boyunca taşınır. */
    const par = this.runPar || this.maxPar; // koşu başladığı paralellikle sürer (mod çipi sonraki koşuya)
    if (this.phase === 'stopping') {
      /* graceful stop: kuyruk başlamaz — uçuştakiler bitince koşu durur */
    } else if (this.cleanRun) {
      for (const pd of P) {
        if (this._building.length >= par) break;
        if (this.p[pd.name].status === 'queued') this._startBuild(pd.name, t);
      }
    } else if (this.scope) {
      /* kapsamlı (tek proje) koşu: sıra YALNIZ kapsam üyeleri; kapsam dışı bağımlılık
         "çözülmüş" sayılır (son çıktısı kullanılır) — döngü üyesi bir hedef de derlenir.
         `continue` kullanılır: kapsam dışı hiçbir satır sırayı bloklamaz. */
      const inScope = ORDER.filter(function (n) { return self.scope.has(n); })
        .concat(Array.from(this.scope).filter(function (n) { return ORDER.indexOf(n) < 0; }));
      for (const name of inScope) {
        if (this._building.length >= par) break;
        const st = this.p[name];
        if (st.status !== 'queued') continue;
        const ready = st.def.deps.every(function (d) { return !self.scope.has(d) || self._resolved(d); });
        if (ready) this._startBuild(name, t);
      }
    } else {
      this._stepCycleGroup(t, par);
      for (const name of ORDER) {
        if (this._building.length >= par) break;
        const st = this.p[name];
        if (this.cyc && this.cyc.members.indexOf(name) >= 0) { // grup kendi adımında koşar; beklerken sırayı tutar
          if (this.cyc.state === 'waiting') break;
          continue;
        }
        if (st.status !== 'queued') continue;
        const ok = st.def.deps.every(function (d) { return self._resolved(d); });
        if (!ok) break; // sıradaki hazır değilse İLERİ ATLAMA — liste sırasıyla derle
        this._startBuild(name, t);
      }
    }
    if (this.activeLine && this._building.length && this._building.indexOf(this.activeLine.text.split(' ')[0]) < 0) {
      const nw = this._building[this._building.length - 1];
      this.activeLine = { id: this.nid(), text: nw + (this.cleanRun ? ' cleaning…' : ' building…') };
    }

    /* 4 — canlı log satırları */
    this._building.forEach(function (name) {
      const st = self.p[name];
      const frac = Math.min(1, (t - st.startAt) / st.dur);
      while (st.log.length < st.logPlan.length - 1 && st.logPlan[st.log.length].at <= frac) {
        const l = st.logPlan[st.log.length];
        st.log.push({ type: l.type, text: l.text });
      }
      self._routeLog(name);
    });

    /* 5 — ETA */
    if (!this._fastCheck()) {
      let remain = 0;
      for (const name of this.willBuild) {
        const st = this.p[name];
        if (st.status === 'queued') remain += st.dur;
        else if (st.status === 'building') remain += Math.max(0, st.endAt - t);
      }
      const est = remain / Math.max(1, par) + (this._building.length ? 400 : 0);
      this._eta = this._eta == null ? est : 0.75 * this._eta + 0.25 * est;
    }

    /* 6 — bitti mi? (graceful stop: uçuştakiler bitti → koşu durur) */
    if (this.phase === 'stopping') {
      if (!this._building.length) this.stop();
      return;
    }
    const unresolved = this.scope
      ? Array.from(this.scope).some((n) => !this._resolved(n))
      : P.some((pd) => !this._resolved(pd.name));
    if (!unresolved && this.scope) { // tek proje koşusu özeti
      const nm = this.scopeName, dur = t - this.runStartT;
      const fails = Array.from(this.scope).filter((n) => this.p[n].status === 'failed');
      this.phase = 'done';
      this.doneAt = t;
      this.activeLine = null;
      this.scope = null;
      this._runEndFinale();
      /* akış özeti tam koşununkiyle aynı biçim (StreamText.Completed): kapsam tek proje */
      if (fails.length) this.emit('done', null, 'Completed — 1 failed · 0 succeeded · 0 skipped · ' + fmtDur(dur));
      else this.emit('done', null, 'Completed — 1 succeeded · 0 skipped · ' + fmtDur(dur));
      void nm;
      return;
    }
    if (!unresolved) {
      this.phase = 'done';
      this.doneAt = t;
      this.activeLine = null;
      if (this.cleanRun) {
        const dur = t - this.runStartT;
        this.allDirty = true; this.allClean = false;
        this.emit('done', null, 'Completed — ' + P.length + ' succeeded · 0 skipped · ' + fmtDur(dur));
        return;
      }
      if (!this._fastCheck()) this._runEndFinale();
      const c = this.counts();
      const w = this.warnings();
      const wtxt = w ? ' · ' + plural(w, 'warning') : '';
      const ctxt = ''; // kirli grup bu koşuda derlendi — 'stuck in a cycle' eki yalnız yakınsamayan grupta (demo yok)
      if (this._fastCheck()) {
        this.checkDur = t - this.runStartT;
        this.emit('done', null, 'Completed — 0 succeeded · ' + P.length + ' skipped · ' + fmtDur(this.checkDur));
      } else if (c.failed) {
        const di = this.depIssueCount();
        this.emit('done', null, 'Completed — ' + c.failed + ' failed · ' + c.succeeded + ' succeeded · ' + c.skipped + ' skipped · ' + fmtDur(t - this.runStartT) + (di ? ' · ' + di + ' dependency-affected' : ''));
      } else {
        this.emit('done', null, 'Completed — ' + c.succeeded + ' succeeded · ' + c.skipped + ' skipped · ' + fmtDur(t - this.runStartT));
      }
      void w; void wtxt; void ctxt;
    }
  };

  /* hızlı sarma: koşulu sağlayana dek sessizce ilerlet */
  SimEngine.prototype.fastForwardUntil = function (pred, maxMs) {
    this.instantMode = true;
    let guard = maxMs || 60000;
    while (!pred(this) && guard > 0) { this.advance(100); guard -= 100; }
    this.instantMode = false;
  };

  const plural = (n, word) => n + ' ' + word + (n === 1 ? '' : 's');
  SimEngine.prototype.counts = function () {
    let building = 0, succeeded = 0, failed = 0, skipped = 0, queued = 0;
    for (const n in this.p) {
      const s = this.p[n].status;
      if (s === 'building') building++;
      else if (s === 'succeeded') succeeded++;
      else if (s === 'failed') failed++;
      else if (s === 'skipped') skipped++;
      else if (s === 'queued') queued++;
    }
    // cycle = üyelik sayısı (kalıcı C kanalı) — statü değil
    return { total: P.length, building, succeeded, failed, skipped, queued, cycle: this.cycle ? this.cycle.size : 0 };
  };
  SimEngine.prototype.depIssueCount = function () {
    let n = 0;
    /* listedeki ▲ ile aynı küme: dep'i patlamış her proje — kendi derlemesi başarılı da olsa
       başarısız da olsa son başarılı çıktıya karşı derlendi (chip sayısı = filtre sonucu). */
    for (const k in this.p) if (this.p[k].depIssue) n++;
    return n;
  };
  /* derleyici warning sayısı (dep-uyarı satırları hariç) — yalnız bu koşuda derlenenler */
  SimEngine.prototype.warnings = function () {
    let n = 0;
    for (const k in this.p) {
      const st = this.p[k];
      if (!st.logPlan) continue;
      if (st.status === 'succeeded' || st.status === 'failed') n += st.logPlan.filter((l) => l.type === 'warn' && !l.dep).length;
    }
    return n;
  };
  SimEngine.prototype.failedList = function () {
    return P.filter((pd) => this.p[pd.name].status === 'failed').map((pd) => pd.name);
  };
  SimEngine.prototype.cycleList = function () {
    if (!this.cycle) return [];
    return CYCLE_MEMBERS.filter((n) => this.cycle.has(n));
  };
  SimEngine.prototype.buildingList = function () { return (this._building || []).slice(); };
  /* koreografi kapsamı (marking fazı) — UI blink'i buradan okur */
  /* statü geçişleri renkle olduğu kadar opaklıkla da okunur; amber geçişi nefese bırakılır */
  SimEngine.prototype.isMarked = function (name) { return !!(this.marked && this.marked.has(name)); };
  SimEngine.prototype.opLabel = function () {
    if (!this.op) return null;
    /* Satırdan tetiklenen işlem şeritte tam koşudan farksız görünür: hedef adı pill'e YAZILMAZ
       (kullanıcı kararı — tek proje de "normal build gibi"; hedef zaten konsolda ve grafta belli). */
    return this.op.label;
  };
  SimEngine.prototype.finishedOfWB = function () { return this._finishedWB || 0; };
  SimEngine.prototype.elapsed = function () {
    if (this.runStartT == null) return 0;
    return (this.phase === 'done' ? this.doneAt : this.phase === 'stopped' ? this.stoppedAt : this.simT) - this.runStartT;
  };
  SimEngine.prototype.eta = function () { return this._eta; };

  window.DELTA_BO = {
    LAYERS, PROJECTS: P, byName, BRANCHES, WORKTREES, CYCLE_PATH,
    SimEngine, fmtDur, fmtClock, fmtAge, shortName, mulberry32, setExternal, REMOTE_SHA, classifyLine,
    get ORDER() { return ORDER; },
    get GRAPH() { return GRAPH; },
    get SOLUTIONS() { return SOLUTIONS; },
    get EDGE_N() { return EDGE_N; },
  };
})();
