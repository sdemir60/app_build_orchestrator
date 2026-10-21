# Changelog

Release notes for Build Orchestrator, newest version first. The What's new window in the app reads this file.

## [1.11.0] - 2026-10-21

### Added

- The middle of the title bar shows what the tool costs the machine while it works: CPU, memory, and the projects building now against the number set for the run and the number the mode allows, followed by the mode. Hover a cell for its meaning; it pauses while the window is in the tray or minimized.
- A power button at the right end of the title bar, or Ctrl+Q, quits the app. With work running a card asks first and says what will end; Stop and exit stops the work and closes the app, letting a branch switch or a pull finish first. Exit from the tray, and closing the window with Close to tray off, ask the same way.

### Changed

- Esc closes the window like the X button once it has nothing left to close or stop: to the tray when Close to tray is on, otherwise it quits, asking first if work is still running.
- About no longer lists the app's memory, which the title bar now shows; the copied diagnostics still include it.

### Fixed

- A click on Build, or on the Debug or Release item not selected, that brings the window back from the background now does its job instead of being lost.
- The project list no longer stays blank when it has no projects, for example while Clean or Optimize runs or when Sync finds none: an empty list reads No projects and an empty graph reads No graph.

### Removed

- The Resolve cycles at full priority setting. Resolve cycles follows the performance mode like a build; choose Full to run it faster.

## [1.10.1] - 2026-10-16

### Fixed

- Opening a project's log no longer raises the app's memory use above 1 GB on large screens. The console now switches between a project's log and the run with a flat slide and fade instead of a 3D tilt.

## [1.10.0] - 2026-10-16

### Added

- Stop the engine when idle, a new switch in Settings, General, on by default: after five minutes without work the engine process exits, and the next build, Sync, Clean, Optimize, branch switch, pull or project log starts it again in a moment while the ribbon reads Starting the engine. About shows the engine as asleep meanwhile.
- Compact console, a new switch in Settings, General, on by default: the console shows each project's errors in full, its first warnings and a count of the rest, and every line stays in the project's log.

### Changed

- A project's log opens at its first line with the whole log on the page, and it never follows the output; the latest pill takes you to the end once.
- In the console PageUp and PageDown move one screen and the arrow keys, Home and End scroll the view. Reading sideways keeps its place while new lines arrive, and scrolling with the wheel, the scrollbar or the keys stops a slide to the latest line.
- Without layer definitions the graph is one band like the project list instead of a narrow funnel, using the full width of the panel.

### Fixed

- Clicking between projects quickly no longer opens the wrong project's log or loses lines that arrive while a log loads.
- In the console, Ctrl+Home brings back the earlier lines of a long run, and scrolling up with Ctrl+Home, PageUp or the wheel always lets go of the live output and shows the latest pill.
- A git, vswhere or MSBuild step that had just finished could rarely be reported as an error; its result is now read only once the process has ended.

### Performance

- The app uses much less memory while it sits idle after a build: it gives memory back after every run, Sync, Clean and Optimize, keeps a run's console text on disk with only the latest lines in memory, and stops an idle engine.

## [1.9.0] - 2026-10-13

### Added

- Repositories in git's newer reftable format are fully supported. Returning to the window refreshes quietly, a branch switch made outside the tool opens a Switched to section in the console, a commit made during a build is picked up when the build ends, and an unfinished cherry-pick or revert holds back automatic refreshes as it does elsewhere.

### Fixed

- When a tag has the same name as the checked-out branch, returning to the window or finishing a build no longer starts the console over as if the branch had changed, and the branch chip, the branch list, the N behind count and pull work as they should.
- What's new opens at the top with the newest version instead of scrolled to the bottom of the list.

## [1.8.1] - 2026-10-12

### Fixed

- The notification shown when a run finishes now appears at display scaling above 100%, for example 125%, where it was silently dropped. If its icon cannot be shown, the notification appears without it instead of being lost.
- During a Rebuild, projects waiting to build stay marked until they are built instead of turning green the moment the run starts, and the opening line, progress and run summary count every project of the plan.
- Smaller fixes to queued and single-project runs: a queued project that is up to date says Queued on its page, a project run from its row keeps its reason label while it builds, and stopping that run before the project starts leaves the row as it was.

## [1.8.0] - 2026-10-10

### Added

- Automatic updates: an installed copy checks for a newer version in the background, downloads it and offers it from an Update pill in the title bar. Restart to update applies it at once, or it installs when you quit.
- Shift+Space shows or hides the window from anywhere, and Ctrl+Shift+Space starts a Build without bringing the window up. F6 rebuilds and F7 cleans.
- Esc stops a running build; a second Esc ends the projects still in flight at once.
- While Sync finds the projects, the list shows Discovering projects with a running count, and the graph says it appears once they are found.
- Resolve cycles runs at full priority so it finishes sooner; a switch in Settings turns that off.
- The console and the event stream show the warnings raised when a run starts (stale obj leftovers, reversed layers) and say when the number of workers was reduced to fit the machine.

### Changed

- Build also compiles dependency cycle groups that are out of date, in rounds, so projects that depend on a cycle no longer compile against a stale DLL. Resolve cycles stays as the on-demand form.
- Build, Rebuild and the row actions are disabled while a Sync, Clean, Optimize, branch switch or pull runs, so nothing waits behind it. F5 only starts a Build; it no longer doubles as Stop.
- Saving Settings with an empty repository root closes the workspace and returns to the first-run screen.

### Fixed

- Close to tray and Show notifications in Settings now take effect, and quitting while a build runs stops it gracefully and closes when the work ends.
- Switching Debug or Release runs a Sync in the new configuration, so rows are never coloured by a guess.
- Optimize restores every SDK-style project, which resolves the missing package assets after a fresh clone or a Clean.
- A cycle member built outside the tool, for example in Visual Studio, is no longer compiled again while its output is current, and a group that does not settle keeps the members that did.
- Rebuild no longer drops frames late in a run, and the start of a run no longer holds up input for a moment.
- Smaller fixes to the Export and Import icons and to how API changes are detected.

### Performance

- Changing the inside of a shared project no longer rebuilds everything above it: a dependent is skipped when the public API it uses did not change.
- Resolve cycles hashes in parallel and skips package restores when nothing changed, and WPF projects compile a metadata-only temporary assembly.
- The number of workers a run starts is capped by the machine's logical processors and free memory.
- A build running while the window is in the tray does no screen work; the window catches up in one pass when it returns.
- A lighter window: the project list keeps its rows instead of rebuilding them while scrolling, the console keeps less memory, carets blink only while the window is active, and run logs are kept for three days.

### Removed

- The Alt+B, Ctrl+F5, Shift+F5 and Ctrl+F1 shortcuts. Shift+Space replaces Alt+B, and F6 replaces the two rebuild keys.

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
