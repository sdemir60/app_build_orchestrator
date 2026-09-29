# Changelog

Release notes for Build Orchestrator, newest version first. The What's new window in the app reads this file.

## [1.7.0] - 2026-09-29

### Added

- Clean in the Build menu cleans every project at once, external projects and dependency cycles included.
- Start with Windows, optionally hidden in the tray. Task Manager's startup apps list the app by name.

### Changed

- What's new covers every version, with the older ones folded.

### Fixed

- Smaller fixes to the tray menu, graph tooltips and the build wave after Resolve cycles.

### Performance

- Resolve cycles is faster: members that need no further pass are skipped, and independent members build in parallel.

## [1.6.0] - 2026-09-21

### Added

- The branch chip switches branches. With uncommitted changes it stops, or stashes them first if Settings says so.
- Sync runs on its own after a commit, a pull or a branch change made elsewhere, and when you return to the window.
- Projects you built yourself, for example in Visual Studio, count as up to date while their output is current.
- Hovering a project in the list highlights it in the graph, and the other way round.
- The filter box has a clear button.

### Changed

- Row colour shows the state of each project's output: green up to date, grey to build, red failed.
- Row labels are shorter and no longer carry a time.

### Removed

- Separate worktrees for other branches: every build runs in your working copy.

## [1.5.0] - 2026-09-16

### Added

- Clean and Optimize in the maintenance box: Clean deletes bin and obj for a fresh start, Optimize restores packages and clears stale leftovers.
- While the window is in the tray, an animated mark shows a build is running, and Windows reports the result when it ends.

### Changed

- Settings, About and What's new were redesigned, and Settings is split into sections.
- One hover style across the window; clicking a layer heading jumps to that group.

### Fixed

- Building a single project no longer colours the rest of the list.
- A project built against a failing dependency is no longer rebuilt on every Build; it waits until that dependency is fixed.

### Performance

- Hidden in the tray and idle, the app uses almost no CPU.

## [1.4.0] - 2026-09-10

### Added

- External projects: git working copies outside the repository join the same graph and build first, updated before each build if you choose.
- Build, rebuild or clean a single project from its row or its right-click menu.
- A behind chip shows how far the branch trails its remote; clicking it fast-forwards.
- Settings can be exported, imported and cleared, and the first run opens Settings.
- What's new, with a dot on its title bar button when a new version arrives.

### Changed

- Whether a project needs building is read from its source files on disk, so it also works offline and outside version control.
- Every operation opens with a wave over the projects it touches, and a run ends with a finale in the graph.
- Status chips filter together, and the last operation stays visible in the ribbon.

## [1.3.1] - 2026-08-26

### Fixed

- Colour changes on hover no longer flash white.
- Turning the mouse wheel during a scroll animation stops it in place instead of snapping back.

## [1.3.0] - 2026-08-16

### Added

- A project page in the console: pick a project to read its own log and why it will or will not build.
- Status chips filter the project list and the graph.
- Scrollbars widen and light up when the mouse reaches them.

### Changed

- The action bar, the console and the event stream were simplified.

## [1.2.0] - 2026-08-11

### Added

- A Cycles button next to Sync builds the projects caught in a dependency cycle.

### Changed

- The dependency graph was redrawn: quieter, with its own camera, zoom and gestures.

## [1.1.0] - 2026-08-05

### Added

- About, from the title bar or F1: version, environment and keyboard shortcuts.
- The project list follows the running build and lets go when you scroll.

### Changed

- Stop stays visible while a run winds down.
- A new product mark and icons, and the window opens maximised.

### Fixed

- A run could hang while stopping; it no longer does.

### Performance

- The window stays responsive on large solutions.

### Removed

- Continue: after a stop, Build picks up what is left.

## [1.0.0] - 2026-07-26

### Added

- Three performance modes, Full, Balanced and Light, set how many projects build at once and how much CPU they may take.

### Performance

- The graph and the project list stay fast with a thousand projects.

## [0.2.0] - 2026-07-25

### Added

- The main window: dependency graph, project list grouped by layer, live console and event stream, in three layouts.
- Settings for the repository root and project layers.
- A progress ribbon with the phase, the counts and the time left.
- Tray icon, a single running instance and the global Alt+B shortcut to bring the window back.
- Open a project in Visual Studio or reveal it in Explorer.
- Build another branch in a separate worktree, leaving your working copy untouched.

## [0.1.0] - 2026-07-18

### Added

- Finds every project under the repository root and works out the build order from their references.
- Builds in that order with one MSBuild process per project, in parallel where the graph allows.
- Builds only what changed and what depends on it.
- Stop a run and continue it later; no build process is left behind.
