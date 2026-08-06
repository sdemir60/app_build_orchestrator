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

  /* build-order: katman, sonra tanım sırası */
  const ORDER = P.slice().sort((a, b) => a.layer - b.layer || P.indexOf(a) - P.indexOf(b)).map((p) => p.name);

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
    this.checkDur = null;      // allClean koşusunun süresi
    this.instantMode = false;
    this._id = 0;
    this._lastEmitT = -9999;
    this._building = [];
    this._finishedWB = 0;
    this._eta = null;
    this.lastFail = null;      // {name, t} — shake için
    this.targetSha = opts.targetSha || 'b7e91d4';
    // multiFail: Failure sahnesi için ekstra hata üreten projeler (will-build zinciri içinden)
    // baseFails: def.fails yalnız multiFail koşusunda etkili — Hero ve manuel koşular temiz biter
    this.baseFails = !!opts.multiFail;
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
  SimEngine.prototype._applySync = function () {
    const wb = this.allClean ? new Set() : computeWillBuild(this.allDirty);
    this.willBuild = wb;
    P.forEach((pd) => {
      const st = this.p[pd.name];
      st.status = 'discovered';
      st.will = wb.has(pd.name) ? 'dirty' : 'clean';
    });
    this.phase = 'idle';
  };
  SimEngine.prototype.at = function (delay, fn) { this.todo.push({ at: this.simT + delay, fn }); };
  SimEngine.prototype.nid = function () { return ++this._id; };
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
    if (this.phase === 'running') return;
    const self = this;
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
        self.say('dim', (36 - n) + ' projects up to date (will skip)');
      }
      self.emit('sync', null, 'Sync — ' + (self.allClean ? '36 projects up to date, nothing to build' : self.willBuild.size + ' to build, ' + (36 - self.willBuild.size) + ' up to date'));
    });
  };

  SimEngine.prototype.startRun = function () {
    if (this.phase === 'running') return;
    const self = this;
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
    if (this.allClean) {
      this.say('cmd', 'osys-build --changed-only — checking 36 projects');
      return; // processRun hepsini hızla atlar
    }
    this.say('cmd', 'msbuild Osys.sln /m:' + this.maxPar + ' /p:Configuration=' + this.cfg + ' — ' + this.willBuild.size + ' projects, ' + (36 - this.willBuild.size) + ' skipped');
    this.emit('info', null, 'Build started — ' + this.willBuild.size + ' projects, parallelism ' + this.maxPar);
  };

  SimEngine.prototype.stop = function () {
    if (this.phase !== 'running') return;
    const self = this;
    (this._building || []).forEach(function (n) { self.p[n].status = 'queued'; self.p[n].startAt = null; });
    this._building = [];
    this.phase = 'stopped';
    this.stoppedAt = this.simT;
    this.activeLine = null;
    this.say('warn', 'Build stopped — ' + this._finishedWB + '/' + this.willBuild.size + ' completed');
    this.emit('info', null, 'Stopped — ' + (this.willBuild.size - this._finishedWB) + ' remaining projects queued');
  };

  /* stopped → kuyruktan devam; elapsed korunur (simT stopped sırasında donuktur) */
  SimEngine.prototype.continueRun = function () {
    if (this.phase !== 'stopped') return;
    const remain = this.willBuild.size - this._finishedWB;
    this.phase = 'running';
    this.say('cmd', 'msbuild — continue, ' + remain + ' remaining projects');
    this.emit('info', null, 'Continue — ' + remain + ' remaining, parallelism ' + this.maxPar);
  };

  /* yalnız hatalılar + bağımlıları yeniden koşar; konsol/stream sıfırlanmaz.
     baseFails kapatılır — düzeltme uygulandı varsayılır, retry temiz geçer. */
  SimEngine.prototype.startRetry = function () {
    if (this.phase === 'running' || this.phase === 'syncing') return 0;
    const self = this;
    const failed = this.failedList();
    if (!failed.length) return 0;
    const set = new Set(failed);
    let grew = true;
    while (grew) {
      grew = false;
      P.forEach(function (pd) {
        if (!set.has(pd.name) && pd.deps.some(function (d) { return set.has(d); })) { set.add(pd.name); grew = true; }
      });
    }
    this.baseFails = false;
    this.extraFails = null;
    this.lastFail = null;
    this.willBuild = set;
    P.forEach(function (pd) { self.p[pd.name].will = set.has(pd.name) ? 'dirty' : 'clean'; });
    this.phase = 'idle';
    this.say('cmd', 'retry — ' + failed.length + ' failed + ' + (set.size - failed.length) + ' dependents');
    this.startRun();
    return set.size;
  };

  /* -------- ilerletme -------- */
  SimEngine.prototype.advance = function (dt) {
    this.simT += dt;
    const due = this.todo.filter((t) => t.at <= this.simT);
    this.todo = this.todo.filter((t) => t.at > this.simT);
    due.sort((a, b) => a.at - b.at).forEach((t) => t.fn());
    if (this.phase === 'running') this._processRun();
  };

  SimEngine.prototype._resolved = function (name) {
    const s = this.p[name].status;
    return s === 'succeeded' || s === 'failed' || s === 'skipped';
  };
  SimEngine.prototype._isFail = function (name) {
    if (!this.baseFails) return false;
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
    let skipBudget = this.instantMode ? 99 : (this.allClean ? 12 : 3);
    for (const name of ORDER) {
      if (skipBudget <= 0) break;
      const st = this.p[name];
      if (st.status !== 'discovered' || this.willBuild.has(name)) continue;
      if (!st.def.deps.every((d) => self._resolved(d))) continue;
      st.status = 'skipped';
      st.doneDur = null;
      this.emit('skip', name, name + ' skipped — up to date');
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
        (dp.depIssue || []).forEach(function (r) { if (roots.indexOf(r) < 0) roots.push(r); });
      });
      st.depIssue = roots.length ? roots : null;
      st.status = 'building';
      st.startAt = t;
      st.endAt = t + st.dur;
      st.logPlan = makeLog(st.def, this.cfg, mulberry32(name.length * 977 + this.runCount), this._isFail(name));
      if (st.depIssue) {
        const direct = st.def.deps.filter(function (d) { return self.p[d].status === 'failed'; });
        const wl = direct.length
          ? direct.map(function (d) { return { at: 0.03, type: 'warn', dep: true, text: 'warning: ' + d + ' failed in this run — last successful output referenced (yesterday 18:42)' }; })
          : [{ at: 0.03, type: 'warn', dep: true, text: 'warning: failure in dependency chain (' + st.depIssue.map(shortName).join(', ') + ') — referenced outputs may be stale' }];
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
    if (!this.allClean) {
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
      const wtxt = w ? ' · ' + w + ' warnings' : '';
      if (this.allClean) {
        this.checkDur = t - this.runStartT;
        const txt = 'Everything up to date — 36 projects checked in ' + fmtDur(this.checkDur) + ', nothing to build';
        this.say('success', txt);
        this.emit('done', null, txt);
      } else if (c.failed) {
        const di = this.depIssueCount();
        this.say('error', 'Build failed — ' + c.failed + ' errors, ' + c.succeeded + ' succeeded, ' + c.skipped + ' skipped' + (w ? ', ' + w + ' warnings' : '') + ' (' + fmtDur(t - this.runStartT) + ')');
        if (di) this.say('warn', di + ' projects built with failed dependencies — last successful outputs referenced');
        this.emit('done', null, 'Completed — ' + c.failed + ' failed · ' + c.succeeded + ' succeeded · ' + c.skipped + ' skipped · ' + fmtDur(t - this.runStartT) + (di ? ' · ' + di + ' dependency-affected' : '') + wtxt);
      } else {
        this.say('success', 'Build complete — ' + c.succeeded + ' projects' + wtxt + ' (' + fmtDur(t - this.runStartT) + ')');
        this.emit('done', null, 'Completed — ' + c.succeeded + ' succeeded · ' + c.skipped + ' skipped' + wtxt + ' · ' + fmtDur(t - this.runStartT));
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
    return { total: P.length, building, succeeded, failed, skipped, queued };
  };
  SimEngine.prototype.depIssueCount = function () {
    let n = 0;
    for (const k in this.p) if (this.p[k].depIssue && this.p[k].status === 'succeeded') n++;
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
  SimEngine.prototype.buildingList = function () { return (this._building || []).slice(); };
  SimEngine.prototype.finishedOfWB = function () { return this._finishedWB || 0; };
  SimEngine.prototype.elapsed = function () {
    if (this.runStartT == null) return 0;
    return (this.phase === 'done' ? this.doneAt : this.phase === 'stopped' ? this.stoppedAt : this.simT) - this.runStartT;
  };
  SimEngine.prototype.eta = function () { return this._eta; };

  window.DELTA_BO = {
    LAYERS, PROJECTS: P, byName, ORDER, GRAPH, BRANCHES, WORKTREES,
    SimEngine, fmtDur, fmtClock, shortName, mulberry32,
  };
})();
