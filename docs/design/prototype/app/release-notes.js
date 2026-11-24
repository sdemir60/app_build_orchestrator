/* Build Orchestrator — sürüm notları (What's new). Uygulamanın CHANGELOG.md'sinden üretilir (uygulama sürümleri);
   Added→feature · Changed→change · Fixed→fix · Performance→perf · Removed→removed. En yeni sürüm en üstte. */
window.DELTA_BO_NOTES = [
  {
    v: "1.15.0", date: "2026-11-16", items: [
      ["feature", "New setting Replace files in use (Settings, General, Build; off by default): when a running app holds a DLL that a project's post-build step copies, the old file is moved aside and the copy runs again, so the project ends green and the projects that use it build against the new copy. The running app keeps the previous build until you restart it, and a later build removes the old files once they are free."],
      ["feature", "When a copy hits a file in use, the console names the file and the program holding it, and the event stream shows a short line saying whether it was replaced. With the setting off nothing is moved, and the line says to turn the setting on."],
      ["fix", "Event stream lines too long for the panel wrap onto the next line instead of being cut off at the right edge; the wrapped part lines up under the start of the text, and the time and icon stay on the first line."],
    ],
  },
  {
    v: "1.14.3", date: "2026-11-11", items: [
      ["fix", "In About, Settings and What's new, text that scrolls toward the top or bottom edge fades out gently instead of being cut off at the line there."],
    ],
  },
  {
    v: "1.14.2", date: "2026-11-10", items: [
      ["fix", "In What's new, notes that scroll up fade out gently from the line where the list begins, instead of running up to the header's edge at full strength and being cut off there."],
    ],
  },
  {
    v: "1.14.1", date: "2026-11-10", items: [
      ["fix", "On two monitors, a maximised window no longer shows a thin edge, a rounded corner and a shadow on the neighbouring screen, which came over that screen's windows each time the app was clicked."],
    ],
  },
  {
    v: "1.14.0", date: "2026-11-09", items: [
      ["feature", "The ribbon says what a Sync is doing: fetching from the remote, analyzing the projects with their count, or indexing source files with a running count, and the thin bar under it fills while the files are indexed."],
      ["feature", "On a large repository, the first Sync of a new worktree or a fresh clone reads every source file once; the console reports its progress about every ten seconds and ends with how long it took. Its work is saved as it goes, so closing the app halfway through resumes from the last save instead of starting over."],
      ["change", "The operation label at the left of the ribbon lights up with its spinner for every action while it runs, Deep Clean, Optimize, branch switches, worktree creation and pulls included, and the line beside it says what that action is doing instead of showing the previous action's result."],
      ["change", "A Clean from the Build menu or a project's menu counts the projects it cleans instead of saying Building, and Esc during worktree creation explains that it cannot be stopped, as it does for a branch switch or a pull."],
    ],
  },
  {
    v: "1.13.1", date: "2026-11-09", items: [
      ["change", "The branch picker is wider and taller: most branch names show in full instead of ending in an ellipsis, and twelve branches are in view before the list scrolls."],
    ],
  },
  {
    v: "1.13.0", date: "2026-11-09", items: [
      ["feature", "The branch picker works from the keyboard: type to narrow the list, press Down to highlight the first match and the arrow keys to move between branches, and Enter switches to the highlighted branch as a click would. Enter does nothing while no branch is highlighted."],
      ["change", "Right-clicking the tray icon opens its menu where you clicked again, drawn in front of the taskbar, instead of lifting it above the taskbar's edge."],
    ],
  },
  {
    v: "1.12.1", date: "2026-11-09", items: [
      ["fix", "Right-clicking the tray icon opens its menu above the taskbar instead of partly behind it, so Exit is always within reach, whatever the display's resolution or scale."],
      ["fix", "The card that opens above the branch chip when the working tree has uncommitted changes shows all three of its buttons; Cancel is no longer cut off at the card's edge."],
      ["fix", "A branch name too long for its row in the branch picker ends in an ellipsis before the commit or the active badge instead of running over them."],
    ],
  },
  {
    v: "1.12.0", date: "2026-11-08", items: [
      ["feature", "A worktree lets you build another branch while the repository, with your changes and an open Visual Studio, stays as it is. Name its folder on the Workspace page of Settings: an existing worktree of the repository is recorded, and a folder that does not exist yet is created when you save, at the repository's current commit with no branch checked out. The toggle between the repository name and the branch chip moves every Sync, build, Clean, Optimize, pull and branch switch there and back, and the app remembers which copy was open."],
      ["feature", "With Stash and switch branches off, picking a branch while the working tree has uncommitted changes opens a card above the branch chip: stash the changes this once and switch, switch that branch in the other copy and go there, or cancel. A branch is checked out in one copy at a time; picking the one the other copy holds changes nothing, and the console names that copy."],
      ["change", "Browse… and Export settings open their pickers centred over the window, as Import settings already did."],
    ],
  },
  {
    v: "1.11.7", date: "2026-11-02", items: [
      ["change", "The window opens full screen on the monitor it was on when the app last closed, after Exit, Restart to update or a Windows restart, instead of on the main display, and fills that monitor at whatever resolution or scale it has now. If that monitor is not connected, Windows picks the screen as before. The monitor is remembered from the first time this version closes."],
    ],
  },
  {
    v: "1.11.6", date: "2026-11-02", items: [
      ["change", "The notification shown when a run finishes tells the result on its icon before you read it: a green tick on its corner for success, a red cross for a failure, a grey square for a stopped run."],
      ["change", "Restart engine sits right beside Build as a square button with a circular arrow, instead of in the ribbon. It appears only while the engine has stopped or is not answering, and the ribbon names the problem."],
      ["fix", "The animated product mark that shows a build running from the tray stays in the bottom-right corner of the screen, even after the display scale or resolution changed while the app was running."],
    ],
  },
  {
    v: "1.11.5", date: "2026-10-26", items: [
      ["fix", "When the window is in the background, the first click on Sync, Clean or Optimize runs it right away, as it already did for Build, instead of only bringing the window forward. Exit and the close button act at once too when nothing is running, without asking for confirmation. Buttons whose work refreshes only part of the screen, such as Resolve cycles, N behind, the Build menu arrow, the branch picker and a project row's own buttons, still spend that first click on refreshing the screen."],
      ["fix", "A project row's menu that is open when a Sync starts or ends now locks and unlocks its items with it, instead of keeping the state it opened with."],
    ],
  },
  {
    v: "1.11.4", date: "2026-10-26", items: [
      ["fix", "A project's page in the console shows its Back button and the dependency issue and dependency cycle badges again. 1.11.3 removed them together with the status beside the project's name; only that status stays off the page, since the list and the graph already show it."],
    ],
  },
  {
    v: "1.11.3", date: "2026-10-26", items: [
      ["change", "A project's page in the console shows only the project's name at the top: the back button, the status word and the dependency badges are gone, and the list and the graph still show the status. Click the same row or node again, click an empty part of the graph, or press Esc to return to the run."],
      ["change", "The tray icon's menu looks like the app's other menus: Stop carries the red square and Exit the power sign."],
      ["fix", "After switching between Debug and Release, a project whose own files are unchanged no longer claims a dependency changed: its row and its page say which configuration it was last built in. A project waiting behind a dependency that will be rebuilt says that on its page."],
      ["fix", "The tray icon's menu opens where you clicked, even after the display scale changed while the app was running."],
    ],
  },
  {
    v: "1.11.2", date: "2026-10-21", items: [
      ["change", "The parallelism cell in the title bar names the mode before the number it allows: it reads 0/0 Balanced · 4 instead of 0/0/4 Balanced, the run's own numbers first and the mode after them."],
    ],
  },
  {
    v: "1.11.1", date: "2026-10-21", items: [
      ["fix", "With no run in flight, the parallelism cell in the title bar shows 0 for the run's number instead of a dash: it reads 0/0/4 Balanced while idle."],
    ],
  },
  {
    v: "1.11.0", date: "2026-10-21", items: [
      ["feature", "The middle of the title bar shows what the tool costs the machine while it works: CPU, memory, and the projects building now against the number set for the run and the number the mode allows, followed by the mode. Hover a cell for its meaning; it pauses while the window is in the tray or minimized."],
      ["feature", "A power button at the right end of the title bar, or Ctrl+Q, quits the app. With work running a card asks first and says what will end; Stop and exit stops the work and closes the app, letting a branch switch or a pull finish first. Exit from the tray, and closing the window with Close to tray off, ask the same way."],
      ["change", "Esc closes the window like the X button once it has nothing left to close or stop: to the tray when Close to tray is on, otherwise it quits, asking first if work is still running."],
      ["change", "About no longer lists the app's memory, which the title bar now shows; the copied diagnostics still include it."],
      ["fix", "A click on Build, or on the Debug or Release item not selected, that brings the window back from the background now does its job instead of being lost."],
      ["fix", "The project list no longer stays blank when it has no projects, for example while Clean or Optimize runs or when Sync finds none: an empty list reads No projects and an empty graph reads No graph."],
      ["removed", "The Resolve cycles at full priority setting. Resolve cycles follows the performance mode like a build; choose Full to run it faster."],
    ],
  },
  {
    v: "1.10.1", date: "2026-10-16", items: [
      ["fix", "Opening a project's log no longer raises the app's memory use above 1 GB on large screens. The console now switches between a project's log and the run with a flat slide and fade instead of a 3D tilt."],
    ],
  },
  {
    v: "1.10.0", date: "2026-10-16", items: [
      ["feature", "Stop the engine when idle, a new switch in Settings, General, on by default: after five minutes without work the engine process exits, and the next build, Sync, Clean, Optimize, branch switch, pull or project log starts it again in a moment while the ribbon reads Starting the engine. About shows the engine as asleep meanwhile."],
      ["feature", "Compact console, a new switch in Settings, General, on by default: the console shows each project's errors in full, its first warnings and a count of the rest, and every line stays in the project's log."],
      ["change", "A project's log opens at its first line with the whole log on the page, and it never follows the output; the latest pill takes you to the end once."],
      ["change", "In the console PageUp and PageDown move one screen and the arrow keys, Home and End scroll the view. Reading sideways keeps its place while new lines arrive, and scrolling with the wheel, the scrollbar or the keys stops a slide to the latest line."],
      ["change", "Without layer definitions the graph is one band like the project list instead of a narrow funnel, using the full width of the panel."],
      ["fix", "Clicking between projects quickly no longer opens the wrong project's log or loses lines that arrive while a log loads."],
      ["fix", "In the console, Ctrl+Home brings back the earlier lines of a long run, and scrolling up with Ctrl+Home, PageUp or the wheel always lets go of the live output and shows the latest pill."],
      ["fix", "A git, vswhere or MSBuild step that had just finished could rarely be reported as an error; its result is now read only once the process has ended."],
      ["perf", "The app uses much less memory while it sits idle after a build: it gives memory back after every run, Sync, Clean and Optimize, keeps a run's console text on disk with only the latest lines in memory, and stops an idle engine."],
    ],
  },
  {
    v: "1.9.0", date: "2026-10-13", items: [
      ["feature", "Repositories in git's newer reftable format are fully supported. Returning to the window refreshes quietly, a branch switch made outside the tool opens a Switched to section in the console, a commit made during a build is picked up when the build ends, and an unfinished cherry-pick or revert holds back automatic refreshes as it does elsewhere."],
      ["fix", "When a tag has the same name as the checked-out branch, returning to the window or finishing a build no longer starts the console over as if the branch had changed, and the branch chip, the branch list, the N behind count and pull work as they should."],
      ["fix", "What's new opens at the top with the newest version instead of scrolled to the bottom of the list."],
    ],
  },
  {
    v: "1.8.1", date: "2026-10-12", items: [
      ["fix", "The notification shown when a run finishes now appears at display scaling above 100%, for example 125%, where it was silently dropped. If its icon cannot be shown, the notification appears without it instead of being lost."],
      ["fix", "During a Rebuild, projects waiting to build stay marked until they are built instead of turning green the moment the run starts, and the opening line, progress and run summary count every project of the plan."],
      ["fix", "Smaller fixes to queued and single-project runs: a queued project that is up to date says Queued on its page, a project run from its row keeps its reason label while it builds, and stopping that run before the project starts leaves the row as it was."],
    ],
  },
  {
    v: "1.8.0", date: "2026-10-10", items: [
      ["feature", "Automatic updates: an installed copy checks for a newer version in the background, downloads it and offers it from an Update pill in the title bar. Restart to update applies it at once, or it installs when you quit."],
      ["feature", "Shift+Space shows or hides the window from anywhere, and Ctrl+Shift+Space starts a Build without bringing the window up. F6 rebuilds and F7 cleans."],
      ["feature", "Esc stops a running build; a second Esc ends the projects still in flight at once."],
      ["feature", "While Sync finds the projects, the list shows Discovering projects with a running count, and the graph says it appears once they are found."],
      ["feature", "Resolve cycles runs at full priority so it finishes sooner; a switch in Settings turns that off."],
      ["feature", "The console and the event stream show the warnings raised when a run starts (stale obj leftovers, reversed layers) and say when the number of workers was reduced to fit the machine."],
      ["change", "Build also compiles dependency cycle groups that are out of date, in rounds, so projects that depend on a cycle no longer compile against a stale DLL. Resolve cycles stays as the on-demand form."],
      ["change", "Build, Rebuild and the row actions are disabled while a Sync, Clean, Optimize, branch switch or pull runs, so nothing waits behind it. F5 only starts a Build; it no longer doubles as Stop."],
      ["change", "Saving Settings with an empty repository root closes the workspace and returns to the first-run screen."],
      ["fix", "Close to tray and Show notifications in Settings now take effect, and quitting while a build runs stops it gracefully and closes when the work ends."],
      ["fix", "Switching Debug or Release runs a Sync in the new configuration, so rows are never coloured by a guess."],
      ["fix", "Optimize restores every SDK-style project, which resolves the missing package assets after a fresh clone or a Clean."],
      ["fix", "A cycle member built outside the tool, for example in Visual Studio, is no longer compiled again while its output is current, and a group that does not settle keeps the members that did."],
      ["fix", "Rebuild no longer drops frames late in a run, and the start of a run no longer holds up input for a moment."],
      ["fix", "Smaller fixes to the Export and Import icons and to how API changes are detected."],
      ["perf", "Changing the inside of a shared project no longer rebuilds everything above it: a dependent is skipped when the public API it uses did not change."],
      ["perf", "Resolve cycles hashes in parallel and skips package restores when nothing changed, and WPF projects compile a metadata-only temporary assembly."],
      ["perf", "The number of workers a run starts is capped by the machine's logical processors and free memory."],
      ["perf", "A build running while the window is in the tray does no screen work; the window catches up in one pass when it returns."],
      ["perf", "A lighter window: the project list keeps its rows instead of rebuilding them while scrolling, the console keeps less memory, carets blink only while the window is active, and run logs are kept for three days."],
      ["removed", "The Alt+B, Ctrl+F5, Shift+F5 and Ctrl+F1 shortcuts. Shift+Space replaces Alt+B, and F6 replaces the two rebuild keys."],
    ],
  },
  {
    v: "1.7.0", date: "2026-09-29", items: [
      ["feature", "Clean in the Build menu cleans every project at once, external projects and dependency cycles included."],
      ["feature", "Start with Windows, optionally hidden in the tray. Task Manager's startup apps list the app by name."],
      ["change", "What's new covers every version, with the older ones folded."],
      ["fix", "Smaller fixes to the tray menu, graph tooltips and the build wave after Resolve cycles."],
      ["perf", "Resolve cycles is faster: members that need no further pass are skipped, and independent members build in parallel."],
    ],
  },
  {
    v: "1.6.0", date: "2026-09-21", items: [
      ["feature", "The branch chip switches branches. With uncommitted changes it stops, or stashes them first if Settings says so."],
      ["feature", "Sync runs on its own after a commit, a pull or a branch change made elsewhere, and when you return to the window."],
      ["feature", "Projects you built yourself, for example in Visual Studio, count as up to date while their output is current."],
      ["feature", "Hovering a project in the list highlights it in the graph, and the other way round."],
      ["feature", "The filter box has a clear button."],
      ["change", "Row colour shows the state of each project's output: green up to date, grey to build, red failed."],
      ["change", "Row labels are shorter and no longer carry a time."],
      ["removed", "Separate worktrees for other branches: every build runs in your working copy."],
    ],
  },
  {
    v: "1.5.0", date: "2026-09-16", items: [
      ["feature", "Clean and Optimize in the maintenance box: Clean deletes bin and obj for a fresh start, Optimize restores packages and clears stale leftovers."],
      ["feature", "While the window is in the tray, an animated mark shows a build is running, and Windows reports the result when it ends."],
      ["change", "Settings, About and What's new were redesigned, and Settings is split into sections."],
      ["change", "One hover style across the window; clicking a layer heading jumps to that group."],
      ["fix", "Building a single project no longer colours the rest of the list."],
      ["fix", "A project built against a failing dependency is no longer rebuilt on every Build; it waits until that dependency is fixed."],
      ["perf", "Hidden in the tray and idle, the app uses almost no CPU."],
    ],
  },
  {
    v: "1.4.0", date: "2026-09-10", items: [
      ["feature", "External projects: git working copies outside the repository join the same graph and build first, updated before each build if you choose."],
      ["feature", "Build, rebuild or clean a single project from its row or its right-click menu."],
      ["feature", "A behind chip shows how far the branch trails its remote; clicking it fast-forwards."],
      ["feature", "Settings can be exported, imported and cleared, and the first run opens Settings."],
      ["feature", "What's new, with a dot on its title bar button when a new version arrives."],
      ["change", "Whether a project needs building is read from its source files on disk, so it also works offline and outside version control."],
      ["change", "Every operation opens with a wave over the projects it touches, and a run ends with a finale in the graph."],
      ["change", "Status chips filter together, and the last operation stays visible in the ribbon."],
    ],
  },
  {
    v: "1.3.1", date: "2026-08-26", items: [
      ["fix", "Colour changes on hover no longer flash white."],
      ["fix", "Turning the mouse wheel during a scroll animation stops it in place instead of snapping back."],
    ],
  },
  {
    v: "1.3.0", date: "2026-08-16", items: [
      ["feature", "A project page in the console: pick a project to read its own log and why it will or will not build."],
      ["feature", "Status chips filter the project list and the graph."],
      ["feature", "Scrollbars widen and light up when the mouse reaches them."],
      ["change", "The action bar, the console and the event stream were simplified."],
    ],
  },
  {
    v: "1.2.0", date: "2026-08-11", items: [
      ["feature", "A Cycles button next to Sync builds the projects caught in a dependency cycle."],
      ["change", "The dependency graph was redrawn: quieter, with its own camera, zoom and gestures."],
    ],
  },
  {
    v: "1.1.0", date: "2026-08-05", items: [
      ["feature", "About, from the title bar or F1: version, environment and keyboard shortcuts."],
      ["feature", "The project list follows the running build and lets go when you scroll."],
      ["change", "Stop stays visible while a run winds down."],
      ["change", "A new product mark and icons, and the window opens maximised."],
      ["fix", "A run could hang while stopping; it no longer does."],
      ["perf", "The window stays responsive on large solutions."],
      ["removed", "Continue: after a stop, Build picks up what is left."],
    ],
  },
  {
    v: "1.0.0", date: "2026-07-26", items: [
      ["feature", "Three performance modes, Full, Balanced and Light, set how many projects build at once and how much CPU they may take."],
      ["perf", "The graph and the project list stay fast with a thousand projects."],
    ],
  },
  {
    v: "0.2.0", date: "2026-07-25", items: [
      ["feature", "The main window: dependency graph, project list grouped by layer, live console and event stream, in three layouts."],
      ["feature", "Settings for the repository root and project layers."],
      ["feature", "A progress ribbon with the phase, the counts and the time left."],
      ["feature", "Tray icon, a single running instance and the global Alt+B shortcut to bring the window back."],
      ["feature", "Open a project in Visual Studio or reveal it in Explorer."],
      ["feature", "Build another branch in a separate worktree, leaving your working copy untouched."],
    ],
  },
  {
    v: "0.1.0", date: "2026-07-18", items: [
      ["feature", "Finds every project under the repository root and works out the build order from their references."],
      ["feature", "Builds in that order with one MSBuild process per project, in parallel where the graph allows."],
      ["feature", "Builds only what changed and what depends on it."],
      ["feature", "Stop a run and continue it later; no build process is left behind."],
    ],
  },
];
