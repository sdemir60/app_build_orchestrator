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
  function sha7(rng) {
    const c = '0123456789abcdef'; let s = '';
    for (let i = 0; i < 7; i++) s += c[Math.floor(rng() * 16)];
    return s;
  }
  const shortName = (n) => n.replace(/^OSYS\./, '');

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
    { name: 'OSYS.Base',              sln: 'Osys.sln',        layer: 0, deps: [], dur: 1600 },
    { name: 'OSYS.Common.Contracts',  sln: 'Osys.sln',        layer: 0, deps: ['OSYS.Base'], dur: 1300 },
    { name: 'OSYS.Common.Utils',      sln: 'Osys.sln',        layer: 0, deps: ['OSYS.Base'], dur: 1500 },
    // L1
    { name: 'OSYS.Data.Core',         sln: 'Osys.sln',        layer: 1, deps: ['OSYS.Base', 'OSYS.Common.Contracts'], dur: 2600 },
    { name: 'OSYS.Data.Migrations',   sln: 'Osys.sln',        layer: 1, deps: ['OSYS.Data.Core'], dur: 1400 },
    { name: 'OSYS.Security',          sln: 'Osys.sln',        layer: 1, deps: ['OSYS.Base', 'OSYS.Common.Utils'], dur: 1700 },
    { name: 'OSYS.Shared.UI',          sln: 'Osys.sln',        layer: 1, deps: ['OSYS.Common.Utils'], dur: 2100 },
    { name: 'OSYS.Integration.Core',  sln: 'Osys.sln',        layer: 1, deps: ['OSYS.Common.Contracts'], dur: 1800 },
    // L2
    { name: 'OSYS.Domain.Vehicle',       sln: 'Osys.sln',        layer: 2, deps: ['OSYS.Data.Core'], dur: 2200 },
    { name: 'OSYS.Domain.Customer',    sln: 'Osys.sln',        layer: 2, deps: ['OSYS.Data.Core'], dur: 2000 },
    { name: 'OSYS.Domain.Inventory',       sln: 'Osys.sln',        layer: 2, deps: ['OSYS.Data.Core'], dur: 1900 },
    { name: 'OSYS.Domain.Service',     sln: 'Osys.sln',        layer: 2, deps: ['OSYS.Data.Core', 'OSYS.Domain.Vehicle'], dur: 2400, dirty: true },
    { name: 'OSYS.Domain.Parts',      sln: 'Osys.sln',        layer: 2, deps: ['OSYS.Data.Core', 'OSYS.Domain.Inventory'], dur: 2100, dirty: true },
    { name: 'OSYS.Domain.Finance',     sln: 'Osys.sln',        layer: 2, deps: ['OSYS.Data.Core'], dur: 2300 },
    // L3
    { name: 'OSYS.Sales.Core',        sln: 'Osys.Sales.sln',  layer: 3, deps: ['OSYS.Domain.Vehicle', 'OSYS.Domain.Customer'], dur: 2800, dirty: true, fails: true },
    { name: 'OSYS.UsedCars.Core',     sln: 'Osys.Sales.sln',  layer: 3, deps: ['OSYS.Domain.Vehicle', 'OSYS.Sales.Core'], dur: 2200 },
    { name: 'OSYS.Service.Scheduling',    sln: 'Osys.Service.sln', layer: 3, deps: ['OSYS.Domain.Service', 'OSYS.Domain.Customer'], dur: 1900 },
    { name: 'OSYS.Service.Workshop',     sln: 'Osys.Service.sln', layer: 3, deps: ['OSYS.Domain.Service', 'OSYS.Domain.Parts'], dur: 2600 },
    { name: 'OSYS.Parts.Inventory',        sln: 'Osys.Parts.sln',  layer: 3, deps: ['OSYS.Domain.Parts'], dur: 1700 },
    { name: 'OSYS.Parts.Catalog',     sln: 'Osys.Parts.sln',  layer: 3, deps: ['OSYS.Domain.Parts'], dur: 2000 },
    { name: 'OSYS.Finance.Invoicing',     sln: 'Osys.sln',        layer: 3, deps: ['OSYS.Domain.Finance'], dur: 2400 },
    { name: 'OSYS.Finance.Accounting',   sln: 'Osys.sln',        layer: 3, deps: ['OSYS.Domain.Finance', 'OSYS.Finance.Invoicing'], dur: 2600 },
    { name: 'OSYS.Reporting.Core',        sln: 'Osys.sln',        layer: 3, deps: ['OSYS.Data.Core', 'OSYS.Domain.Finance'], dur: 2200, dirty: true },
    // L4
    { name: 'OSYS.Server.Api',        sln: 'Osys.sln',        layer: 4, deps: ['OSYS.Sales.Core', 'OSYS.Service.Scheduling', 'OSYS.Parts.Inventory', 'OSYS.Security'], dur: 3400, dirty: true },
    { name: 'OSYS.Sales.Api',         sln: 'Osys.Sales.sln',  layer: 4, deps: ['OSYS.Sales.Core', 'OSYS.Security'], dur: 2600 },
    { name: 'OSYS.Service.Api',        sln: 'Osys.Service.sln', layer: 4, deps: ['OSYS.Service.Scheduling', 'OSYS.Service.Workshop', 'OSYS.Security'], dur: 2900 },
    { name: 'OSYS.Parts.Api',         sln: 'Osys.Parts.sln',  layer: 4, deps: ['OSYS.Parts.Catalog', 'OSYS.Parts.Inventory', 'OSYS.Security'], dur: 3100 },
    { name: 'OSYS.Reporting.Api',         sln: 'Osys.sln',        layer: 4, deps: ['OSYS.Reporting.Core', 'OSYS.Security'], dur: 2300 },
    { name: 'OSYS.Notifications.Api',      sln: 'Osys.sln',        layer: 4, deps: ['OSYS.Common.Utils', 'OSYS.Security'], dur: 1600 },
    { name: 'OSYS.Integration.Api',   sln: 'Osys.sln',        layer: 4, deps: ['OSYS.Integration.Core', 'OSYS.Security'], dur: 1900 },
    // L5
    { name: 'OSYS.Web.Portal',        sln: 'Osys.Web.sln',    layer: 5, deps: ['OSYS.Sales.Api', 'OSYS.Reporting.Api', 'OSYS.Shared.UI'], dur: 4200, dirty: true, fails: true },
    { name: 'OSYS.Web.DealerPortal',    sln: 'Osys.Web.sln',    layer: 5, deps: ['OSYS.Sales.Api', 'OSYS.Shared.UI'], dur: 3600 },
    { name: 'OSYS.Client.Core',       sln: 'Osys.Client.sln', layer: 5, deps: ['OSYS.Shared.UI', 'OSYS.Common.Contracts'], dur: 2400 },
    { name: 'OSYS.Client.Sales',      sln: 'Osys.Client.sln', layer: 5, deps: ['OSYS.Client.Core', 'OSYS.Sales.Api'], dur: 3200, dirty: true },
    { name: 'OSYS.Client.Service',     sln: 'Osys.Client.sln', layer: 5, deps: ['OSYS.Client.Core', 'OSYS.Service.Api'], dur: 2800 },
    { name: 'OSYS.Mobile.Api',         sln: 'Osys.Mobile.sln',  layer: 5, deps: ['OSYS.Service.Api', 'OSYS.Parts.Api', 'OSYS.Security'], dur: 2600 },
  ];
  const byName = {}; P.forEach((p) => { byName[p.name] = p; });

  /* ---------- bağımlılık döngüsü (Cycle sahnesi) ----------
     Parts.Api → Domain.Parts geri-kenarı bu üç projeyi tek SCC'ye sokar. Döngüdekiler HİÇ
     derlenmez: scheduler kurulurken 'in dependency cycle' gerekçesiyle dışarıda bırakılır,
     "N to build" sayacına girmezler. İmzaları yine de akar, dışarıdaki bağımlıları derlenir. */
  const CYCLE_MEMBERS = ['OSYS.Domain.Parts', 'OSYS.Parts.Inventory', 'OSYS.Parts.Api'];
  const CYCLE_PATH = CYCLE_MEMBERS.concat(CYCLE_MEMBERS[0]);

  /* build-order: katman, sonra tanım sırası */
  const ORDER = P.slice().sort((a, b) => a.layer - b.layer || P.indexOf(a) - P.indexOf(b)).map((p) => p.name);

  /* bakım görevleri solution granülaritesinde çalışır (msbuild /t:Clean, nuget restore) */
  const SOLUTIONS = (function () {
    const order = [], n = {};
    P.forEach(function (p) { if (order.indexOf(p.sln) < 0) order.push(p.sln); n[p.sln] = (n[p.sln] || 0) + 1; });
    return order.map(function (s) { return { name: s, n: n[s] }; });
  })();
  const EDGE_N = P.reduce(function (n, p) { return n + p.deps.length; }, 0);

  /* ---------- graf yerleşimi (katman düzeni, DAG) ---------- */
  const GRAPH = (function () {
    const counts = {}; P.forEach((p) => { counts[p.layer] = (counts[p.layer] || 0) + 1; });
    const W = 880, rowH = 96, top = 46;
    const idx = {};
    const nodes = {};
    P.forEach((p) => {
      const i = (idx[p.layer] = (idx[p.layer] || 0));
      idx[p.layer]++;
      const n = counts[p.layer];
      const spacing = Math.min(96, (W - 70) / Math.max(1, n - 0.5));
      nodes[p.name] = { x: W / 2 + (i - (n - 1) / 2) * spacing, y: top + p.layer * rowH };
    });
    const edges = [];
    P.forEach((p) => p.deps.forEach((d) => edges.push([d, p.name])));
    return { nodes, edges, W, H: top + 5 * rowH + 58 };
  })();

  /* ---------- branch / worktree ---------- */
  const BRANCHES = [
    { name: 'main', sha: 'b7e91d4', active: true, note: 'active' },
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
  function computeWillBuild(allDirty) {
    const dirty = new Set(P.filter((p) => p.dirty).map((p) => p.name));
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
    "NU1603: package 'Delta.Osys.Cache 3.1.0' was not found, resolved 3.1.2 instead",
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
      else if (r < 0.5) L.push({ at, type: 'dim', text: 'Analyzer: Delta.CodeRules ' + (1 + Math.floor(rng() * 3)) + ' rules, 0 violations' });
      else L.push({ at, type: 'dim', text: 'GenerateTargetFrameworkMonikerAttribute → obj/' + cfg + '/.NETCoreApp,Version=v8.0.cs' });
    }
      if (willFail) {
      if (p.name === 'OSYS.Sales.Core') {
        L.push({ at: 0.86, type: 'error', text: "SalesContract.cs(129,17): error CS0246: the type or namespace name 'ICampaignService' could not be found" });
        L.push({ at: 0.93, type: 'error', text: "PriceCalculator.cs(57,23): error CS1061: 'VehiclePriceDto' does not contain a definition for 'NetPrice'" });
        L.push({ at: 1, type: 'error', text: 'Build FAILED — 2 errors, 0 warnings (' + fmtDur(p.dur) + ')' });
      } else {
        L.push({ at: 0.90, type: 'error', text: "VehicleDetail.cshtml.g.cs(210,44): error CS1061: 'VehicleViewModel' does not contain a definition for 'PolicyNo'" });
        L.push({ at: 1, type: 'error', text: 'Build FAILED — 1 error, 0 warnings (' + fmtDur(p.dur) + ')' });
      }
    } else {
      L.push({ at: 0.90, type: 'dim', text: 'CopyFilesToOutputDirectory: bin/' + cfg + '/net8.0/' });
      const w = L.filter((x) => x.type === 'warn').length;
      L.push({ at: 1, type: 'success', text: 'Build succeeded — 0 errors, ' + w + ' warnings (' + fmtDur(p.dur) + ')' });
    }
    return L;
  }

  /* ================== SİMÜLASYON MOTORU ================== */
  function SimEngine() {
    this.runCount = 0;
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
    this.stream = [];
    this.narrative = [];
    this.activeLine = null;
    this.todo = [];
    this.revealAt = null;      // sync reveal zaman damgası (UI stagger için)
    this.runStartT = null;
    this.doneAt = null;
    this.stoppedAt = null;
    this.willBuild = new Set();
    this.checkDur = null;      // hızlı kontrol koşusunun süresi
    this.checkOnly = false;    // yalnız BU koşu için: derlenecek bayat iş yok
    this.instantMode = false;
    this._id = 0;
    this._lastEmitT = -9999;
    this._building = [];
    this._finishedWB = 0;
    this._eta = null;
    this.lastFail = null;      // {name, t} — shake için
    this.task = null;          // yürüyen bakım görevi (clean/optimize)
    this.taskResult = null;    // son biten bakım görevinin özeti (şeritte kalır)
    this.resolveRun = null;    // yürüyen Resolve cycles koşusu
    this.prevPhase = null;
    this.targetSha = opts.targetSha || 'b7e91d4';
    // multiFail: Failure sahnesi için ekstra hata üreten projeler (will-build zinciri içinden)
    // baseFails: def.fails yalnız multiFail koşusunda etkili — Hero ve manuel koşular temiz biter
    this.baseFails = !!opts.multiFail || !!opts.oneFail;
    // oneFail: Cycle sahnesi — tek kasıtlı hata (zincir ortasında), bağımlıları depIssue alır
    this.failOnly = opts.oneFail ? new Set(['OSYS.Sales.Core']) : null;
    this.cycle = opts.cycle ? new Set(CYCLE_MEMBERS) : null; // döngüdeki projeler (Cycle sahnesi)
    this.extraFails = opts.multiFail ? new Set(['OSYS.Parts.Catalog', 'OSYS.Service.Workshop', 'OSYS.Reporting.Core']) : null;
    this.p = {};
    const rng = this.rng;
    P.forEach((pd) => {
      this.p[pd.name] = {
        def: pd,
        status: 'discovered',
        will: 'unknown',           // dirty | clean | unknown
        curSha: sha7(rng),
        dur: Math.round(pd.dur * (1.05 + rng() * 0.5)),
        startAt: null, endAt: null, doneDur: null, depIssue: null,
        log: [], logPlan: null,
      };
    });
    if (opts.synced) this._applySync();
  };
  /* bu koşu hızlı kontrol mü: workspace tamamen temiz (allClean) ya da stale set boş (checkOnly) */
  SimEngine.prototype._fastCheck = function () { return this.allClean || this.checkOnly; };
  SimEngine.prototype._isCycle = function (name) { return !!(this.cycle && this.cycle.has(name)); };
  SimEngine.prototype._applySync = function () {
    const wb = this.allClean ? new Set() : computeWillBuild(this.allDirty);
    if (this.cycle) this.cycle.forEach((n) => wb.delete(n)); // standart plan döngü üyelerine dokunmaz
    this.willBuild = wb;
    P.forEach((pd) => {
      const st = this.p[pd.name];
      const cyc = this._isCycle(pd.name);
      st.status = 'discovered'; // cycle üyeliği statü DEĞİL — kalıcı C kanalı (nokta/çekirdek turuncu)
      st.will = cyc ? (this.allClean ? 'clean' : 'dirty') : wb.has(pd.name) ? 'dirty' : 'clean';
    });
    this.phase = 'idle';
  };
  SimEngine.prototype.at = function (delay, fn) { this.todo.push({ at: this.simT + delay, fn }); };  SimEngine.prototype.nid = function () { return ++this._id; };
  SimEngine.prototype.wall = function () { return fmtClock(this.simT); };

  SimEngine.prototype.say = function (type, text) {
    this.narrative.push({ id: this.nid(), type, time: this.wall(), text });
    if (this.narrative.length > 240) this.narrative.splice(0, this.narrative.length - 240);
  };
  /* stream olayı: sakinse daktilo, fırtınada/hatada anında */
  SimEngine.prototype.emit = function (kind, project, text) {
    const burst = (this.simT - this._lastEmitT) < 340;
    this._lastEmitT = this.simT;
    const instant = this.instantMode || burst || kind === 'fail';
    this.stream.push({ id: this.nid(), time: this.wall(), kind, project, text, instant });
    if (this.stream.length > 260) this.stream.splice(0, this.stream.length - 260);
  };

  /* -------- akış başlatıcılar -------- */
  SimEngine.prototype.startSync = function () {
    if (this.busy()) return;
    const self = this;
    this.taskResult = null;
    this.checkOnly = false;
    this.phase = 'syncing';
    this.say('cmd', 'git fetch origin ' + (this.branchName || 'main'));
    this.at(260, function () {
      self.say('dim', 'HEAD ' + self.targetSha + ' — computing osys-state diff');
      self.revealAt = self.simT;
    });
    this.at(700, function () {
      self._applySync();
      const n = self.willBuild.size;
      if (self.allClean) {
        self.say('info', 'Sync complete — no changes, 36 projects up to date');
      } else {
        const roots = P.filter(function (p) { return p.dirty; }).length;
        self.say('info', 'Sync complete — ' + (self.allDirty ? 'config changed' : roots + ' changed projects') + ', ' + n + ' to build');
        self.say('dim', (36 - n - (self.cycle ? self.cycle.size : 0)) + ' projects up to date (will skip)');
      }
      if (self.cycle) {
        self.say('warn', self.cycle.size + ' projects in a dependency cycle — standard builds skip them (use Resolve cycles)');
        self.say('dim', 'cycle: ' + CYCLE_PATH.join(' → '));
      }
      const cyc = self.cycle ? self.cycle.size : 0;
      self.emit('sync', null, 'Sync — ' + (self.allClean ? '36 projects up to date, nothing to build'
        : self.willBuild.size + ' to build, ' + (36 - self.willBuild.size - cyc) + ' up to date' + (cyc ? ', ' + cyc + ' in a dependency cycle' : '')));
    });
  };

  SimEngine.prototype.startRun = function () {
    if (this.phase === 'running' || this.task) return;
    const self = this;
    this.taskResult = null;
    this.runCount++;
    if (this.phase === 'boot' || this.willBuild.size === 0 && !this.allClean && this.phase !== 'idle') this._applySync();
    this.phase = 'running';
    this.runStartT = this.simT;
    this.doneAt = null;
    this._building = [];
    this._finishedWB = 0;
    this._eta = null;
    P.forEach(function (pd) {
      const st = self.p[pd.name];
      st.status = self.willBuild.has(pd.name) ? 'queued' : 'discovered';
      st.startAt = null; st.endAt = null; st.doneDur = null; st.log = []; st.logPlan = null; st.depIssue = null;
    });
    if (this._fastCheck()) {
      this.say('cmd', 'osys-build --changed-only — checking 36 projects');
      return; // processRun hepsini hızla atlar
    }
    this.say('cmd', 'msbuild Osys.sln /m:' + this.maxPar + ' /p:Configuration=' + this.cfg + ' — ' + this.willBuild.size + ' projects, ' + (36 - this.willBuild.size) + ' skipped');
    this.emit('info', null, 'Build started — ' + this.willBuild.size + ' projects, parallelism ' + this.maxPar);
  };

  SimEngine.prototype.stop = function () {
    if (this.phase !== 'running') return;
    const self = this;
    if (this.resolveRun) { // Resolve iptali: scope discovered'a döner, faz idle — Build/Resolve yeniden kullanılabilir
      (this._building || []).forEach(function (n) { self.p[n].status = 'discovered'; self.p[n].startAt = null; });
      this._building = [];
      P.forEach(function (pd) { if (self.p[pd.name].status === 'queued') self.p[pd.name].status = 'discovered'; });
      this.resolveRun = null;
      this.phase = 'idle';
      this.activeLine = null;
      this.say('warn', 'Resolve stopped — cycle projects remain unresolved');
      this.emit('info', null, 'Resolve stopped — cycle projects unresolved');
      return;
    }
    (this._building || []).forEach(function (n) { self.p[n].status = 'queued'; self.p[n].startAt = null; });
    this._building = [];
    this.phase = 'stopped';
    this.stoppedAt = this.simT;
    this.activeLine = null;
    this.say('warn', 'Build stopped — ' + this._finishedWB + '/' + this.willBuild.size + ' completed');
    this.emit('info', null, 'Stopped — ' + (this.willBuild.size - this._finishedWB) + ' remaining projects queued');
  };

  /* Build (durumdan): stale set = will dirty + hatalıların bağımlıları. Sync/reset YOK —
     Stop sonrası "Build" doğal devamdır (Continue/Retry ayrı akış olarak kaldırıldı, 1.7.0).
     Önceki koşuda hata varsa düzeltme uygulandı varsayılır (baseFails kapanır → temiz geçer). */
  SimEngine.prototype.startRunFromState = function () {
    if (this.busy() || this.phase === 'empty' || this.phase === 'boot') return;
    const self = this;
    if (this.failedList().length) { this.baseFails = false; this.extraFails = null; this.failOnly = null; this.lastFail = null; }
    const dirty = new Set(P.filter((pd) => self.p[pd.name].will === 'dirty' && !self._isCycle(pd.name)).map((pd) => pd.name));
    let grew = true;
    while (grew) {
      grew = false;
      P.forEach((pd) => {
        if (!dirty.has(pd.name) && !self._isCycle(pd.name) && pd.deps.some((d) => dirty.has(d))) { dirty.add(pd.name); grew = true; }
      });
    }
    this.willBuild = dirty;
    P.forEach((pd) => {
      const st = self.p[pd.name];
      if (!self._isCycle(pd.name)) st.will = dirty.has(pd.name) ? 'dirty' : 'clean';
    });
    this.checkOnly = dirty.size === 0; // bayat iş yoksa hızlı kontrol koşusu (allClean'e DOKUNMAZ)
    this.startRun();
  };

  /* ---- Resolve cycles: döngü üyeleri + bayat bağımlılıkları, iki geçiş ----
     Pass 1: bağımlılıklar + üyeler son bilinen (bayat) referanslarla derlenir.
     Pass 2: yalnız üyeler taze referanslarla yeniden derlenir → yakınsar, temiz biter.
     Ardışık (paralellik 1) koşar — döngü çözümü sıraya duyarlıdır. Statü normal akar
     (queued/building/succeeded); turuncu C kanalı (nokta/çekirdek) kalıcıdır. */
  SimEngine.prototype.startResolve = function () {
    if (!this.cycle || this.busy() || this.phase === 'empty' || this.phase === 'boot') return false;
    const self = this;
    this.taskResult = null;
    this.runCount++;
    this.lastFail = null;
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
    const list = depList.map((n) => ({ name: n, pass: 1, cyc: false }))
      .concat(members.map((n) => ({ name: n, pass: 1, cyc: true })))
      .concat(members.map((n) => ({ name: n, pass: 2, cyc: true })));
    inScope.forEach((n) => {
      const st = self.p[n];
      st.status = 'queued';
      st.startAt = null; st.endAt = null; st.doneDur = null; st.depIssue = null;
      st.log = []; st.logPlan = null;
    });
    this.phase = 'running';
    this.resolveRun = { list, i: 0, fin: 0, total: list.length, pass: 1, startT: this.simT, cycleN: members.length, depN: depList.length, curName: null, curCyc: false, curPass: 1 };
    this.say('cmd', 'osys-resolve-cycles — ' + members.length + ' projects in cycle' + (depList.length ? ' + ' + depList.length + ' stale dependencies' : '') + ' · 2 passes');
    this.say('dim', 'cycle: ' + CYCLE_PATH.map(shortName).join(' → '));
    this.say('info', 'pass 1/2 — building with last known references');
    this.emit('task', null, 'Resolve cycles — pass 1/2: building with stale references');
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
        return;
      }
      st.doneDur = st.endAt - st.startAt;
      st.log = st.logPlan.map((l) => ({ type: l.type, text: l.text }));
      st.status = 'succeeded';
      if (!R.curCyc || R.curPass === 2) { st.will = 'clean'; st.curSha = this.targetSha; } // pass 1 çıktısı bayat — üye ancak pass 2'de temizlenir
      R.fin++;
      const tag = R.curCyc ? ' — pass ' + R.curPass + '/2' : '';
      this.emit('ok', R.curName, R.curName + ' built' + tag + ' (' + fmtDur(st.doneDur) + ')');
      this.say('success', R.curName + ' → bin/' + this.cfg + '/net8.0 (' + fmtDur(st.doneDur) + ')' + (R.curCyc && R.curPass === 1 ? ' — stale references' : ''));
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
      this.say('success', 'Cycles resolved — ' + R.cycleN + ' projects converged in 2 passes' + depTxt + ' (' + fmtDur(el) + ')');
      this.say('dim', 'note: the circular reference remains in the sources — projects stay marked until it is removed');
      this.emit('taskdone', null, 'Cycles resolved — ' + R.cycleN + ' projects · 2 passes' + depTxt + ' · ' + fmtDur(el));
      this.taskResult = { kind: 'resolve', text: 'Cycles resolved — ' + R.cycleN + ' projects converged in 2 passes · outputs now current' };
      return;
    }
    const it = R.list[R.i++];
    if (it.pass === 2 && R.pass === 1) {
      R.pass = 2;
      this.say('info', 'pass 2/2 — rebuilding cycle projects to converge');
      this.emit('task', null, 'Resolve cycles — pass 2/2: rebuilding to converge');
    }
    const st = this.p[it.name];
    st.status = 'building';
    st.startAt = t;
    st.endAt = t + Math.max(600, Math.round(st.dur * (it.pass === 2 ? 0.45 : 1)));
    let plan = makeLog(st.def, this.cfg, mulberry32(it.name.length * 977 + this.runCount + it.pass * 13), false);
    if (it.cyc) {
      const others = CYCLE_MEMBERS.filter((n) => n !== it.name).map(shortName).join(', ');
      plan = [plan[0]].concat(
        it.pass === 1
          ? [{ at: 0.03, type: 'warn', dep: true, text: 'warning: circular reference (' + others + ') — building against last known outputs' }]
          : [{ at: 0.03, type: 'dim', text: 'pass 2/2 — references refreshed from pass 1 outputs' }],
        plan.slice(1));
    }
    st.logPlan = plan;
    st.log = [];
    this._building = [it.name];
    R.curName = it.name; R.curCyc = it.cyc; R.curPass = it.pass;
    this.activeLine = { id: this.nid(), text: it.name + ' building…' };
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
    return this.phase === 'running' || this.phase === 'syncing' || !!this.task;
  };

  SimEngine.prototype.startTask = function (kind) {
    if (!TASK_TITLE[kind] || this.busy() || this.phase === 'empty') return false;
    const plan = kind === 'clean' ? this._cleanPlan() : this._optimizePlan();
    this.taskResult = null;
    this.prevPhase = this.phase;
    this.phase = 'task';
    this.activeLine = null;
    this.task = {
      kind, title: TASK_TITLE[kind], steps: plan.steps, finish: plan.finish,
      i: 0, total: plan.steps.length, startT: this.simT,
      label: plan.steps[0].label, stream: plan.steps[0].stream,
    };
    this.say('cmd', plan.head);
    this.emit('task', null, plan.headStream);
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
    this.taskResult = { kind: t.kind, text: res.text };
  };

  SimEngine.prototype.taskElapsed = function () { return this.task ? this.simT - this.task.startT : 0; };

  /* bir solution'ın çıktıları silindi → projeleri keşfedilmiş + kirli hâle döner */
  SimEngine.prototype._cleanSolution = function (sln) {
    const self = this;
    P.forEach(function (pd) {
      if (pd.sln !== sln) return;
      const st = self.p[pd.name];
      st.status = 'discovered'; st.will = 'dirty';
      st.startAt = null; st.endAt = null; st.doneDur = null; st.depIssue = null;
      st.log = []; st.logPlan = null;
      if (!self._isCycle(pd.name)) self.willBuild.add(pd.name); // döngü üyesi standart plana girmez ama çıktısı silindi → dirty
    });
  };

  SimEngine.prototype._cleanPlan = function () {
    const rng = mulberry32(9173 + this.runCount * 31);
    const steps = [];
    const tally = { dirs: 0, mb: 0 };
    SOLUTIONS.forEach(function (s) {
      const dirs = s.n * (5 + Math.floor(rng() * 5)) + 2;
      const mb = Math.round(s.n * (60 + rng() * 110));
      steps.push({
        dur: 380 + Math.round(rng() * 220),
        label: s.name,
        stream: 'cleaning ' + s.name + '…',
        run: function (e) {
          tally.dirs += dirs; tally.mb += mb;
          e._cleanSolution(s.name);
          e.say('dim', 'msbuild ' + s.name + ' /t:Clean /m — ' + s.n + ' projects · ' + dirs + ' directories · ' + fmtSize(mb));
          e.emit('task', null, s.name + ' cleaned — ' + s.n + ' projects · ' + fmtSize(mb));
        },
      });
    });
    steps.push({
      dur: 540, label: 'artifacts', stream: 'removing artifacts/ and TestResults/…',
      run: function (e) {
        tally.dirs += 14; tally.mb += 486;
        e.say('dim', 'removing artifacts/ · TestResults/ · .vs/ — 14 directories · ' + fmtSize(486));
        e.emit('task', null, 'artifacts/ · TestResults/ · .vs/ removed — ' + fmtSize(486));
      },
    });
    steps.push({
      dur: 420, label: 'restore state', stream: 'clearing restore state…',
      run: function (e) {
        e.say('warn', 'obj/project.assets.json removed — NuGet restore required on next build');
      },
    });
    return {
      head: 'osys-clean --all --outputs bin,obj,artifacts — ' + SOLUTIONS.length + ' solutions, ' + P.length + ' projects',
      headStream: 'Clean started — ' + SOLUTIONS.length + ' solutions, ' + P.length + ' projects',
      steps,
      finish: function (e, el) {
        e.allDirty = true;
        e.allClean = false;
        e.willBuild = new Set(P.filter(function (pd) { return !e._isCycle(pd.name); }).map(function (pd) { return pd.name; }));
        P.forEach(function (pd) { e.p[pd.name].will = 'dirty'; });
        e.runStartT = null; e.doneAt = null; e.stoppedAt = null; e._finishedWB = 0;
        e._building = []; e._eta = null; e.lastFail = null; e.checkDur = null;
        e.say('success', 'Clean complete — ' + P.length + ' projects · ' + tally.dirs + ' directories removed · ' + fmtSize(tally.mb) + ' reclaimed (' + fmtDur(el) + ')');
        e.say('warn', 'All ' + P.length + ' projects will rebuild — outputs removed');
        e.emit('taskdone', null, 'Clean complete — ' + tally.dirs + ' directories · ' + fmtSize(tally.mb) + ' reclaimed · ' + fmtDur(el));
        return { phase: 'idle', text: 'Clean complete — ' + fmtSize(tally.mb) + ' reclaimed · all ' + P.length + ' projects will rebuild' };
      },
    };
  };

  SimEngine.prototype._optimizePlan = function () {
    const rng = mulberry32(5501 + this.runCount * 17);
    const steps = [];
    const tally = { packages: 0, downloaded: 0, mb: 1180 };
    SOLUTIONS.forEach(function (s) {
      const packs = 18 + s.n * (4 + Math.floor(rng() * 4));
      const down = Math.floor(rng() * 6);
      steps.push({
        dur: 360 + Math.round(rng() * 200),
        label: s.name,
        stream: 'restoring ' + s.name + '…',
        run: function (e) {
          tally.packages += packs; tally.downloaded += down;
          e.say('dim', 'nuget restore ' + s.name + ' — ' + packs + ' packages, ' + (down ? down + ' downloaded' : 'all cached'));
          e.emit('task', null, s.name + ' restored — ' + packs + ' packages');
        },
      });
    });
    steps.push({
      dur: 660, label: 'package cache', stream: 'pruning package cache…',
      run: function (e) {
        e.say('dim', 'pruning global package cache — 38 orphaned packages · ' + fmtSize(tally.mb) + ' reclaimed');
        e.emit('task', null, 'Package cache pruned — 38 orphaned · ' + fmtSize(tally.mb));
      },
    });
    steps.push({
      dur: 580, label: 'dependency index', stream: 'rebuilding dependency index…',
      run: function (e) {
        e.say('info', 'dependency index rebuilt — ' + P.length + ' projects · ' + EDGE_N + ' references · 0 cycles');
        e.emit('task', null, 'Dependency index rebuilt — ' + EDGE_N + ' references, 0 cycles');
      },
    });
    steps.push({
      dur: 500, label: 'build cache', stream: 'warming build cache…',
      run: function (e) {
        e.say('dim', 'compiler server warmed — ' + e.maxPar + ' msbuild nodes · incremental cache primed');
      },
    });
    const prev = this.prevPhase;
    return {
      head: 'osys-optimize --restore --prune --index — ' + SOLUTIONS.length + ' solutions',
      headStream: 'Optimize started — restore, prune, index',
      steps,
      finish: function (e, el) {
        e.say('success', 'Optimize complete — ' + SOLUTIONS.length + ' solutions restored · ' + tally.packages + ' packages · ' + fmtSize(tally.mb) + ' reclaimed (' + fmtDur(el) + ')');
        e.say('dim', 'Build state unchanged — no projects marked dirty');
        e.emit('taskdone', null, 'Optimize complete — ' + tally.packages + ' packages · index rebuilt · ' + fmtDur(el));
        return {
          phase: prev === 'task' || prev == null ? 'idle' : prev,
          text: 'Optimize complete — ' + tally.packages + ' packages restored · ' + fmtSize(tally.mb) + ' reclaimed',
        };
      },
    };
  };

  /* -------- ilerletme -------- */
  SimEngine.prototype.advance = function (dt) {
    this.simT += dt;
    const due = this.todo.filter((t) => t.at <= this.simT);
    this.todo = this.todo.filter((t) => t.at > this.simT);
    due.sort((a, b) => a.at - b.at).forEach((t) => t.fn());
    if (this.phase === 'running') { if (this.resolveRun) this._processResolve(); else this._processRun(); }
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
      self._finishedWB++;
      if (self._isFail(name)) {
        st.status = 'failed';
        self.lastFail = { name, t };
        self.emit('fail', name, name + ' failed — ' + (name === 'OSYS.Sales.Core' ? '2 errors' : '1 error') + ' (' + fmtDur(st.doneDur) + ')');
        const err = st.logPlan.filter((l) => l.type === 'error')[0];
        self.say('error', (err ? err.text.replace(/^.*error /, '') : 'build error') + ' (' + name + ')');
      } else {
        st.status = 'succeeded';
        st.will = 'clean'; // artık güncel — nokta griye döner
        st.curSha = self.targetSha;
        if (st.depIssue) {
          self.emit('ok', name, name + ' built — dependency issue (' + fmtDur(st.doneDur) + ')');
          self.say('warn', name + ' → bin/' + self.cfg + '/net8.0 (' + fmtDur(st.doneDur) + ') — failed dependency: ' + st.depIssue.map(shortName).join(', '));
        } else {
          self.emit('ok', name, name + ' built (' + fmtDur(st.doneDur) + ')');
          self.say('success', name + ' → bin/' + self.cfg + '/net8.0 (' + fmtDur(st.doneDur) + ')');
        }
      }
    });

    /* 2 — atlanacaklar (bağımlılıkları çözülen temiz projeler) — dalga halinde */
    let skipBudget = this.instantMode ? 99 : (this._fastCheck() ? 12 : 3);
    for (const name of ORDER) {
      if (skipBudget <= 0) break;
      const st = this.p[name];
      if (st.status !== 'discovered' || this.willBuild.has(name)) continue;
      if (!st.def.deps.every((d) => self._resolved(d))) continue;
      st.status = 'skipped';
      st.doneDur = null;
      if (this._isCycle(name) && st.will === 'dirty') this.emit('skip', name, name + ' skipped — in a dependency cycle, not rebuilt');
      else this.emit('skip', name, name + ' skipped — up to date');
      skipBudget--;
    }

    /* 3 — yeni build başlat. Başarısız bağımlılık BLOKLAMAZ: proje son başarılı
       çıktıyla yine derlenir, kök hata(lar) depIssue olarak zincir boyunca taşınır. */
    for (const name of ORDER) {
      if (this._building.length >= this.maxPar) break;
      const st = this.p[name];
      if (st.status !== 'queued') continue;
      const ok = st.def.deps.every(function (d) { return self._resolved(d); });
      if (!ok) break; // sıradaki hazır değilse İLERİ ATLAMA — liste sırasıyla derle
      const roots = [];
      st.def.deps.forEach(function (d) {
        const dp = self.p[d];
        if (dp.status === 'failed' && roots.indexOf(d) < 0) roots.push(d);
        if (self._isCycle(d) && dp.will === 'dirty' && roots.indexOf(d) < 0) roots.push(d); // derlenmemiş döngü üyesi = bayat referans
        (dp.depIssue || []).forEach(function (r) { if (roots.indexOf(r) < 0) roots.push(r); });
      });
      st.depIssue = roots.length ? roots : null;
      st.status = 'building';
      st.startAt = t;
      st.endAt = t + st.dur;
      st.logPlan = makeLog(st.def, this.cfg, mulberry32(name.length * 977 + this.runCount), this._isFail(name));
      if (st.depIssue) {
        const direct = st.def.deps.filter(function (d) { return self.p[d].status === 'failed'; });
        const cycDirect = st.def.deps.filter(function (d) { return self._isCycle(d) && self.p[d].will === 'dirty'; });
        let wl = direct.map(function (d) { return { at: 0.03, type: 'warn', dep: true, text: 'warning: ' + d + ' failed in this run — last successful output referenced (yesterday 18:42)' }; })
          .concat(cycDirect.map(function (d) { return { at: 0.03, type: 'warn', dep: true, text: 'warning: ' + d + ' is in a dependency cycle and was not rebuilt — last known output referenced' }; }));
        if (!wl.length) wl = [{ at: 0.03, type: 'warn', dep: true, text: 'warning: issue in dependency chain (' + st.depIssue.map(shortName).join(', ') + ') — referenced outputs may be stale' }];
        st.logPlan = [st.logPlan[0]].concat(wl, st.logPlan.slice(1));
      }
      st.log = [];
      this._building.push(name);
      this.activeLine = { id: this.nid(), text: name + ' building…' };
    }
    if (this.activeLine && this._building.length && this._building.indexOf(this.activeLine.text.split(' ')[0]) < 0) {
      const nw = this._building[this._building.length - 1];
      this.activeLine = { id: this.nid(), text: nw + ' building…' };
    }

    /* 4 — canlı log satırları */
    this._building.forEach(function (name) {
      const st = self.p[name];
      const frac = Math.min(1, (t - st.startAt) / st.dur);
      while (st.log.length < st.logPlan.length - 1 && st.logPlan[st.log.length].at <= frac) {
        const l = st.logPlan[st.log.length];
        st.log.push({ type: l.type, text: l.text });
      }
    });

    /* 5 — ETA */
    if (!this._fastCheck()) {
      let remain = 0;
      for (const name of this.willBuild) {
        const st = this.p[name];
        if (st.status === 'queued') remain += st.dur;
        else if (st.status === 'building') remain += Math.max(0, st.endAt - t);
      }
      const est = remain / Math.max(1, this.maxPar) + (this._building.length ? 400 : 0);
      this._eta = this._eta == null ? est : 0.75 * this._eta + 0.25 * est;
    }

    /* 6 — bitti mi? */
    const unresolved = P.some((pd) => !this._resolved(pd.name));
    if (!unresolved) {
      this.phase = 'done';
      this.doneAt = t;
      this.activeLine = null;
      const c = this.counts();
      const w = this.warnings();
      const wtxt = w ? ' · ' + plural(w, 'warning') : '';
      const cs = this.cycle ? this.cycleList().filter((n) => this.p[n].will === 'dirty').length : 0;
      const ctxt = cs ? ' · ' + plural(cs, 'cycle project') + ' skipped' : '';
      if (this._fastCheck()) {
        this.checkDur = t - this.runStartT;
        const txt = 'Everything up to date — 36 projects checked in ' + fmtDur(this.checkDur) + ', nothing to build';
        this.say('success', txt);
        this.emit('done', null, txt);
      } else if (c.failed) {
        const di = this.depIssueCount();
        this.say('error', 'Build failed — ' + plural(c.failed, 'error') + ', ' + c.succeeded + ' succeeded, ' + c.skipped + ' skipped' + (w ? ', ' + plural(w, 'warning') : '') + ctxt + ' (' + fmtDur(t - this.runStartT) + ')');
        if (di) this.say('warn', plural(di, 'project') + ' built with failed or unbuilt dependencies — last successful outputs referenced');
        this.emit('done', null, 'Completed — ' + c.failed + ' failed · ' + c.succeeded + ' succeeded · ' + c.skipped + ' skipped · ' + fmtDur(t - this.runStartT) + (di ? ' · ' + di + ' dependency-affected' : '') + wtxt + ctxt);
      } else {
        this.say('success', 'Build complete — ' + c.succeeded + ' projects' + wtxt + ctxt + ' (' + fmtDur(t - this.runStartT) + ')');
        this.emit('done', null, 'Completed — ' + c.succeeded + ' succeeded · ' + c.skipped + ' skipped' + wtxt + ctxt + ' · ' + fmtDur(t - this.runStartT));
      }
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
  SimEngine.prototype.finishedOfWB = function () { return this._finishedWB || 0; };
  SimEngine.prototype.elapsed = function () {
    if (this.runStartT == null) return 0;
    return (this.phase === 'done' ? this.doneAt : this.phase === 'stopped' ? this.stoppedAt : this.simT) - this.runStartT;
  };
  SimEngine.prototype.eta = function () { return this._eta; };

  window.DELTA_BO = {
    LAYERS, PROJECTS: P, byName, ORDER, GRAPH, BRANCHES, WORKTREES, CYCLE_PATH,
    SimEngine, fmtDur, fmtClock, shortName, mulberry32,
  };
})();
