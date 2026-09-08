/* Build Orchestrator — release notes (About → What's new).
   Kinds: feature · change · fix · perf · removed. En yeni sürüm en üstte.
   Yeni sürüm çıkarken buraya da madde eklenir (bkz. CLAUDE.md sürümleme). */
window.DELTA_BO_NOTES = [
  {
    v: '1.11.0', date: '2026-08-27', items: [
      ['change', 'Colour now tells one story only: the last operation. A project row and its graph node carry a single status colour — strip, dot, glyph, node border and cube move together.'],
      ['change', 'Sync and startup colour nothing — every project waits in an initial state with a dashed strip and a dashed node border until an operation runs.'],
      ['feature', 'Every operation opens the same way: console and event stream clear, everything returns to neutral grey, the scope lights up amber in a random wave, greys and ambers fade out together, then the build starts.'],
      ['feature', 'A finished run closes the same way: everything holds dim, only the projects that were built flicker on like neon in random order, then the remaining greys rise together in one long, soft reveal.'],
      ['feature', 'Project rows build on their own — the play icon builds that project, and a row menu (also on right-click) offers Build, Rebuild and Clean.'],
      ['feature', 'The Build menu gained Clean — Visual Studio’s Clean Solution: build outputs are removed, caches are kept, and the next build is full.'],
      ['feature', 'The status strip keeps a label of the last operation — BUILD, REBUILD — Sales.Core, CLEAN, RESOLVE — amber while it runs, neutral once it ends.'],
      ['feature', 'Status filters combine — pick Succeeded and Failed together to see everything this run touched; each active chip glows in its own status colour.'],
      ['change', 'A row build covers that project only; a stale dependency left behind raises the warning triangle and says so in the log.'],
      ['change', 'One warning triangle per row, always amber, with a single-line tooltip — the reasoning moved into the project log.'],
      ['change', 'The title bar carries the brand alone; the workspace name moved next to the branch chip in the status bar.'],
      ['fix', 'Building again after a finished run replays the full sequence instead of jumping straight to amber.'],
      ['removed', 'The separate will-build channel and every orange cycle mark — dots, node cores, chips, filters and tooltips.'],
      ['removed', 'The failure counters and the cycle chip in the status strip — the first three failed chips and “+N more” remain.'],
    ],
  },
  {
    v: '1.10.0', date: '2026-08-13', items: [
      ['feature', 'Every project row can start its own build — the play icon builds that project with its stale dependencies.'],
      ['feature', 'Starting a build from a row selects that project, so the console and the graph follow it.'],
      ['feature', 'Settings exports, imports and clears its configuration — repository root and layer definitions travel between machines as a JSON file.'],
      ['feature', 'The first-run panel offers Import settings next to Open settings, so a prepared file gets you started in one step.'],
      ['change', 'A single-project build leaves everything outside its scope untouched, and its row icon turns into Stop while it runs.'],
      ['change', 'Any project can be built from its row — skipped, failed or caught in a cycle.'],
      ['fix', 'Building from a row no longer reloads the project list, so the row under the cursor stays put.'],
      ['fix', 'Stopping a single-project or cycle-resolve run returns the app to idle instead of leaving projects queued.'],
    ],
  },
  {
    v: '1.9.0', date: '2026-08-13', items: [
      ['feature', 'Release notes moved into this window — What\u2019s new lists every version, typed by change kind.'],
      ['feature', 'An unseen release marks the About button with an amber dot and opens straight on the notes.'],
    ],
  },
  {
    v: '1.8.0', date: '2026-08-13', items: [
      ['change', 'First run opens Settings instead of a folder picker \u2014 several settings are needed before a build.'],
      ['feature', 'Settings gained a Workspace section: repository root with Browse, committed by Save and sync.'],
      ['removed', 'The Choose Folder button on the empty project list.'],
    ],
  },
  {
    v: '1.7.0', date: '2026-08-13', items: [
      ['feature', 'Resolve cycles rebuilds projects caught in a dependency loop, in two passes.'],
      ['change', 'Cycle membership is a permanent orange mark, kept apart from build status.'],
      ['change', 'Build always runs the stale set from the current state, so Continue and Retry failed became redundant.'],
      ['fix', 'One warning triangle per row \u2014 its color names the heaviest reason, cycle before dependency issue.'],
      ['removed', 'Continue and Retry failed buttons.'],
    ],
  },
  {
    v: '1.6.0', date: '2026-08-09', items: [
      ['change', 'The console prints real output: no clock column, no icon column, no typing effect.'],
      ['feature', 'Opening a project log tilts in as one piece; the console body is Geist Mono Light for dense text.'],
      ['feature', 'A \u2304 latest pill appears when you scroll away from the bottom of the console or event stream.'],
    ],
  },
  {
    v: '1.5.2', date: '2026-08-09', items: [
      ['change', 'The dependency-affected chip only appears while the list actually holds one.'],
    ],
  },
  {
    v: '1.5.1', date: '2026-08-09', items: [
      ['fix', 'The dependency-affected counter now agrees with the list.'],
    ],
  },
  {
    v: '1.5.0', date: '2026-08-09', items: [
      ['feature', 'Cycle and dependency warnings explain themselves in tooltips instead of a bare badge.'],
    ],
  },
  {
    v: '1.4.0', date: '2026-08-09', items: [
      ['feature', 'Maintenance tasks: Clean removes build outputs, Optimize restores packages and rebuilds the index.'],
      ['perf', 'Optimize prunes the global package cache and warms the compiler server.'],
    ],
  },
  {
    v: '1.3.0', date: '2026-08-06', items: [
      ['change', 'The dependency graph was redesigned \u2014 unnamed nodes in layer bands, quiet until they build.'],
      ['perf', 'The graph always fits its panel and animates only the project being compiled.'],
    ],
  },
  {
    v: '1.2.1', date: '2026-08-05', items: [
      ['change', 'The product logo moved to the corporate palette: near-black tile, neutral bars, amber chevron.'],
    ],
  },
  {
    v: '1.2.0', date: '2026-08-05', items: [
      ['feature', 'Build Orchestrator got its own brand mark, with app icon, title bar and tray variants.'],
    ],
  },
  {
    v: '1.1.0', date: '2026-08-04', items: [
      ['feature', 'About window (F1) with shortcuts, environment and Copy diagnostics.'],
    ],
  },
  {
    v: '1.0.0', date: '\u2014', items: [
      ['feature', 'First package: title bar, status strip, dependency graph, project list, console and event stream.'],
    ],
  },
];
