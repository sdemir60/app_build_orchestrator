/* Build Orchestrator — release notes (About → What's new).
   Kinds: feature · change · fix · perf · removed. En yeni sürüm en üstte.
   Yeni sürüm çıkarken buraya da madde eklenir (bkz. CLAUDE.md sürümleme). */
window.DELTA_BO_NOTES = [
  {
    v: '1.15.0', date: '2026-09-09', items: [
      ['feature', 'Settings → External projects has a Pull before build switch on the section heading line, opposite the section label: it decides whether external working copies are updated before a build. Like every other setting it applies when you save.'],
      ['change', 'With the switch on, every build starts by updating each external working copy — git pull --ff-only or tf get, whichever version control that project was added with. One working copy is pulled once, even when several projects come from it.'],
      ['change', 'The pull is written to the console before the build command, copy by copy, and the event stream carries a single line with how many copies were updated. With the switch off the console says so in one line and the build runs on the copies as they are on disk.'],
      ['change', 'Building a single project from its row pulls only that project’s own working copy.'],
      ['change', 'Exported settings carry the switch too, and clearing the form returns it to on.'],
    ],
  },
  {
    v: '1.14.0', date: '2026-09-08', items: [
      ['feature', 'Settings has an External projects section, right above Layers: paths to projects that live outside the repository root, added one by one, reordered by dragging and removed with a single click — the same cards as the layer list.'],
      ['feature', 'Each external project also carries where it comes from — Git or TFVC — chosen next to its path, so projects kept in different version control systems can sit in the same build. The working copy root is resolved from the path upwards.'],
      ['feature', 'A path can be a folder, a solution or a single project file. Saving it scans the path and adds every project found there to the list under the standard naming, exactly like a project discovered under the repository root.'],
      ['feature', 'External projects sit together as their own layer at the top of the project list and the dependency graph, and they are built first — the repository projects follow, grouped by the layers below them. Nothing else about the process changes: same statuses, same marking wave, same logs.'],
      ['change', 'Saving a change to the external project list re-runs discovery, so the list and the graph come back in sync with the new project set.'],
      ['change', 'The Settings body scrolls on its own, so both lists can grow without the dialog running past the top and bottom of the window.'],
      ['change', 'Exported settings carry the external project list too — path and source per project — and importing a file fills it into the form; clearing empties both lists. Nothing is applied until you save.'],
      ['change', 'External project cards line up with the layer cards: the path field starts in the same column as a layer name, and the card order is the build order — no numbering needed.'],
      ['change', 'Adding, removing or reordering a path while a run is in flight is safe: the run stops, the project set is realigned and discovery starts again.'],
      ['change', 'Counts in the console, the status strip and the build menu read the real number of projects, so they stay correct once external projects join the list.'],
      ['fix', 'The dependency graph is no longer drawn before its panel has been measured, so the node grid never spills past the bottom of the panel on load.'],
    ],
  },
  {
    v: '1.13.2', date: '2026-09-08', items: [
      ['change', 'A Sync lists the workspace from scratch: the project list scrolls back to the top, the graph replays its opening reveal, and both output panels start empty.'],
      ['change', 'Every other operation — Build, Rebuild, Clean, Resolve cycles, and anything started from a project row — leaves the list scrolled where it is, so the row under the pointer does not move.'],
      ['change', 'The console and the event stream are cleared at the start of every operation, and only that operation’s lines are written from then on.'],
      ['change', 'After a Sync the project list reads at full strength: the left strip and the dotted status ring sit at their normal colour instead of a faint one, so a discovered project is visible before anything is built. When an operation starts there is no colour change — only the dotted ring becoming a solid dot.'],
      ['change', 'When a build starts, a row’s name turns bright at the same moment its strip and dot turn amber: the wave moves through name, strip and dot together instead of whitening every name first.'],
      ['change', 'The project list no longer dims and returns while the marking wave runs; fading in and out stays in the dependency graph, where it reads as one motion.'],
      ['change', 'When a run finishes, the graph returns to the full view before the closing animation plays, so the chain of lights is never half off-screen — and it stays there. The full view is the final state; the focus on a project returns only when you select one again, and the selection itself is never dropped.'],
      ['change', 'Building a single project from its row reads exactly like a full build in the status strip: the operation pill says BUILD, without the project name appended.'],
      ['fix', 'While the graph is focused on a project, the project being built now stays at full strength instead of showing an amber ring around a faded node.'],
      ['fix', 'The closing animation plays even when a project is selected; the selection dimming used to hold every node faint and swallow it.'],
      ['fix', 'The dependency graph measures its panel correctly: the node grid fills the panel, follows splitter drags and layout changes, and scroll-to-zoom keeps working after the workspace is set.'],
      ['fix', 'The ⋯ menu on a project row closes again when the same button is clicked a second time.'],
    ],
  },
  {
    v: '1.13.1', date: '2026-09-08', items: [
      ['change', 'The What’s new button uses a single four-point sparkle — the same optical weight as the gear and the info icon next to it.'],
      ['change', 'Each dialog is sized for what it holds and how it will grow: Settings is the widest at 760px, About sits at 660px, and What’s new is 620px with a fixed, scrolling body.'],
      ['change', 'About → Environment keeps its two-column rows but no longer cuts long paths off: the value scrolls sideways under the mouse wheel, with no visible scrollbar, so MSBuild and AppData paths can be read end to end.'],
      ['change', 'What’s new reads better: the installed version sits in the header as a labelled mono number, the current release carries an Installed chip, and Earlier versions lines up with the content column.'],
      ['change', 'What’s new has a fixed body height and scrolls inside itself — opening Earlier versions no longer stretches the dialog to the top and bottom of the screen.'],
      ['change', 'Clearing settings confirms with three words — “Click again to clear” — instead of a sentence that did not fit the footer; the export and clear messages are shorter too.'],
      ['change', 'The unread dot follows one rule: it appears with every new version and on a fresh install, and clears the moment What’s new is opened — for that version only.'],
    ],
  },
  {
    v: '1.13.0', date: '2026-09-08', items: [
      ['feature', 'What’s new is its own dialog with its own button in the title bar, between Settings and About — a sparkle icon, or Ctrl+F1.'],
      ['feature', 'An amber dot sits on that button whenever this version’s notes have not been read: it appears with every new version — and on a fresh install — and clears the moment the dialog is opened, for that version only. No startup pop-up, no toast.'],
      ['change', 'About dropped the What’s new tab; it now covers version, shortcuts, environment and third-party components only. F1 still opens it on Shortcuts.'],
      ['change', 'Escape closes what is on top, in order: What’s new, About, Settings, an open popover, then the current selection.'],
    ],
  },
  {
    v: '1.12.1', date: '2026-09-08', items: [
      ['change', 'The console cursor changes colour on every blink — the new colour arrives only while the cursor is dark, so there is no visible transition, just a steady rhythm.'],
      ['change', 'It cycles through the console’s own line palette in order: command white, info grey, success green, warning amber, error red, dim grey — no colour that a console line could not carry.'],
      ['change', 'The same cursor lives in the event stream’s live line, so both panels share the rhythm; with reduced motion enabled it stays still in the line colour.'],
    ],
  },
  {
    v: '1.12.0', date: '2026-09-08', items: [
      ['feature', 'Projects caught in a dependency cycle show an amber cube inside a grey node from the moment an operation starts — the graph now says “not built, in a cycle” while you inspect a finished run.'],
      ['feature', 'Resolve cycles lights only the cycle members in the opening wave; stale dependencies stay grey until their turn, and the run closes with the same neon finale as a build.'],
      ['change', 'The initial state after Sync draws a solid, faint strip and a four-arc ring in the project list instead of dashed lines — same sizes, no jagged edges, and the ring cross-fades into the status dot when an operation begins. Graph nodes keep their dashed border.'],
    ],
  },
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
