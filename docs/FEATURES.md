English | [Türkçe](FEATURES.tr.md)

# Features

What Build Orchestrator does and how it does it, most important first: the entries you feel on a normal day come
first. Each entry is a short statement, the mechanism behind it and a few tags naming the technique. Why each of
these matters is in [WHY.md](WHY.md); how to use them step by step is in the [User guide](USER-GUIDE.md); the full
technical reference is [ARCHITECTURE.md](../ARCHITECTURE.md).

## Builds only what changed

**What:** Change one file and Build compiles that project and the projects that use it, nothing else; everything
else is skipped as up to date, and when nothing changed the run ends almost at once with nothing to build.\
**How:** Each project's inputs on disk (the .csproj, its items, the nearest Directory.Build files) are hashed and
compared with its last successful build. Git is never consulted, an output's timestamp alone never counts as up to
date, and a deleted or overwritten output is rebuilt even when the sources are the same.\
`content hash of sources` · `digest cache keyed by size and time` · `output evidence to catch deleted DLLs` · `build ledger across sessions`

## Builds in order, in parallel

**What:** Projects build in their real dependency order, and projects that do not wait on each other build at the
same time. A failure does not stall the run: the projects that depend on it build against its last good output and
are flagged, and the rest carry on.\
**How:** The graph is sorted topologically with a stable tie-break, so the same repository always yields the same
plan. Each free worker takes the next project whose dependencies have finished, and each project runs in its own
MSBuild.exe.\
`ready-set scheduler for parallel builds` · `Kahn topological sort for the build order` · `deterministic order for repeatable runs`

## Resolves dependency cycles

**What:** Circular references do not break the plan. Projects that reference each other's output build together as
one unit, in as few rounds as it takes for the APIs they read to settle, and the result says plainly whether the
cycle converged or which project stopped it.\
**How:** Cycles are found as strongly connected components and share one signature. Build settles them inside the
whole run, while Resolve cycles, the third button in the maintenance box, runs only the cycles and the stale projects
they need. The first round builds only the members that need it, a member builds again only if a sibling API it reads
has changed, up to a fixed cap, and a group whose broken root still fails is skipped instead of paying for its rounds
again.\
`Tarjan SCC for cycle detection` · `barriered rounds, no DLL read mid-write` · `API surface hash as proof of convergence` · `Resolve cycles for a cycle-only run`

## Maps dependencies on its own

**What:** Point it at the repository root and it finds every project and how they depend on each other. There is
no project list to keep by hand and no build order to maintain in a script: a new project or a new reference is in
the plan at the next Sync.\
**How:** Every .csproj under the repository root is read as raw XML and every .sln for the projects it lists,
together with the projects your external cards point to; MSBuild is never evaluated for discovery. Dependencies come
from HintPaths, matched to the project that produces the DLL, and from ProjectReferences. A DLL built in another
repository becomes a real dependency once that repository is added as an external card, and a DLL that two projects
claim is reported by name instead of guessed.\
`raw XML discovery in seconds` · `HintPath map for the real graph` · `ProjectReference as a second signal` · `evaluation cache keyed by length and time`

## Whole solution on one screen

**What:** The dependency graph, the project list, the console and the event stream share one calm, dark window, so
the whole solution and the run in progress stay in view: every project's state, what is building now, what failed
and why. A build stops being a wall of scrolling text and becomes something you watch play out, row by row and node
by node.\
**How:** Title bar and status ribbon on top, graph and list on the left, console and event stream on the right, the
action bar at the bottom; three layouts (Quad, List, Focus) and draggable splitters are remembered. Every panel is
painted from the same project states, one click selects a project in all of them, and a hidden graph does not draw.\
`one window for graph, list, console and events` · `one selection across every panel` · `layout modes to choose a focus` · `remembered splitters`

## Row actions: build, open, reveal

**What:** Hovering a row shows Build this project, a menu with Build, Rebuild and Clean, Reveal in Explorer and
Open in Visual Studio; right-click opens the same menu, and a run started from a row builds that project alone,
even when nothing changed.\
**How:** The run is planned in full and cut down to the one project; its dependencies are not built, and a stale
one is reported on the row and in the log. Open in Visual Studio opens the project's solution in a new Visual
Studio and asks which one when the project belongs to several; Reveal in Explorer shows the .csproj selected.\
`single-project runs for quick tries` · `stale-dependency note against hidden DLL risk` · `vswhere to find devenv` · `solution map to open the right .sln`

## Always at hand

**What:** The app can start with Windows and wait in the tray, hidden if you like; from anywhere, Shift+Space shows
or hides the window and Ctrl+Shift+Space starts a Build without bringing it up. A small animated mark in the screen
corner shows a build is running, a Windows notification reports the result, and closing the window only hides it in
the tray.\
**How:** Start with Windows uses the per-user Run key, so no admin rights are needed. The global shortcuts are
registered with Windows and work while the window is behind Visual Studio or in the tray. Close to tray, Start with
Windows, Start minimized to tray and Show notifications live in Settings → General; the tray menu has Stop and Exit,
clicking the icon brings the window back, and a second launch brings the running window forward.\
`global hotkeys to build from the background` · `tray icon to work in the background` · `per-user Run key, no admin` · `Windows notification for the result`

## Syncs on its own

**What:** Sync scans the repository and external roots, builds the graph, previews which projects will build and
measures the distance to the remote, without compiling anything; commits, checkouts, pulls, merges and rebases
made in a terminal or in Visual Studio refresh it on their own.\
**How:** A manual Sync fetches refs only, then scans and decides, and carries on with the local HEAD when offline.
The repository's HEAD is watched with file notifications instead of polling; once git has settled a Sync runs
by itself, and unless the branch changed it is a quiet one that keeps the console, the selection and the scroll
position.\
`ref-only fetch, working tree untouched` · `HEAD watch so Sync is rarely pressed` · `quiet Sync that keeps the screen` · `waits during a half-done merge`

## Keeps the machine usable

**What:** A build does not take the whole machine: by default it leaves processor time and memory for the editor,
the browser and everything else you have open. The perf chip switches between Full, Balanced (the default) and
Light, and the resource strip in the middle of the title bar shows what the tool costs right now: CPU, memory and
parallelism with the profile.\
**How:** A profile sets the parallelism, and puts a priority class and, except on Full, a hard CPU cap on the job
that holds only the compilers, so the window, git and Sync stay at full speed. At run start the worker count is
fitted to the processors and the free memory, leaving a reserve for the rest of the machine. The strip reads the
processor time from the job's own accounting and the memory as private bytes, without listing the machine's
processes.\
`JOB_OBJECT_CPU_RATE_CONTROL hard cap to limit the machine share` · `worker budget by cores and free memory against paging` · `QueryInformationJobObject accounting for the live CPU figure` · `GetProcessMemoryInfo private bytes for the live RAM figure`

## Calm, accessible design

**What:** The interface uses one dark theme with amber as its only accent; colour speaks only about state and every
state is also said with a glyph and a word, transitions turn instant when Windows animations are off, and screen
readers and the keyboard are supported.\
**How:** Colours, sizes, type and motion come from one token system, and the Geist fonts ship with the app. Motion
follows the Windows animation setting live, and every control reports one accessible name and a real role.\
`design tokens for a consistent look` · `OS reduced-motion signal for accessible motion` · `UI Automation names for screen readers` · `embedded fonts, same look everywhere`

## A native Windows app

**What:** Build Orchestrator is a Windows desktop application written in WPF, chosen over a WebView2 hybrid,
Avalonia and WinUI 3. There is no browser runtime inside, and the tray, the notifications, the global shortcuts and
the control over every compiler it starts are Windows' own.\
**How:** The interface runs on WPF and .NET 10, and the build engine runs in a separate process, so the window stays
responsive however much output MSBuild produces. Process control goes straight to Win32: Job Objects and
CreateProcessW for the compilers, RegisterHotKey for the global shortcuts. The app installs per user with Velopack.\
`WPF on .NET 10, no browser runtime` · `Win32 P/Invoke for process control` · `engine and UI in separate processes`

## Light idle, smooth under load

**What:** The engine shuts down after a few idle minutes and starts again on the next job; with the window in the
tray or behind others the app spends next to no CPU, memory is handed back after each operation, and the window
does not freeze even in runs of hundreds of projects.\
**How:** Stop the engine when idle (on by default) puts the engine to sleep, and the ribbon says Starting the
engine when it wakes. Animations nobody can see stop, events are processed in small time slices, and the list
builds the visible rows first.\
`idle engine sleep to save memory` · `visibility-bound animation, near-zero idle CPU` · `memory trim after each operation` · `time-sliced events, no frozen UI`

## Says why each project builds

**What:** No more guessing why something rebuilds: before a run starts, every row says why its project will build
(modified, modified · local, affected, never built, failed) or that it is up to date, and the tooltip gives the
detail.\
**How:** Sync previews the decision a full Build would make: modified when the project's own files changed,
affected when only a dependency or the configuration did, local when the change is also uncommitted in git. Red only ever means a
compiler error proven for today's source.\
`content fingerprint for modified vs affected` · `git status for the local mark` · `evidence-based red, no false alarms`

## Flags stale dependencies

**What:** When a project was built against the last good output of a dependency that has since failed, an amber
triangle on its row says so; it is not rebuilt for nothing on every Build, and it builds once the root is fixed.\
**How:** The names of failing root projects travel down the graph and are noted in the build ledger. The next Build
takes a noted project conditionally: it builds when the root is fixed and is skipped while the root still fails.\
`root-cause note for targeted rebuilds` · `conditional rebuild, no wasted rounds` · `one amber warning channel`

## Skips when the API holds

**What:** When only method bodies changed in a dependency, the projects above it are not rebuilt; when its public
API changed, they are.\
**How:** Just before a dependent would build, the declaration surface of each direct dependency's compiled output
is hashed and compared with the surface the project was last built against. If all match, the project is skipped
and turns green at once.\
`API surface hash of compiled output` · `cascade cut for shared libraries` · `skip reason in the event stream`

## Works alongside Visual Studio

**What:** A project you already built in Visual Studio or on the command line is not rebuilt for nothing: it shows
green as built outside this tool, unless a dependency changes in this Build, in which case it builds as affected.\
**How:** When a project's output is newer than this tool's last run, the output belongs to someone else, and the
project counts as up to date if none of its inputs is newer than that output.\
`foreign output detection` · `input-vs-output dates against double builds` · `upstream cascade to catch stale copies`

## Builds the way VS builds

**What:** Every project is compiled by the same MSBuild.exe that Visual Studio uses, in a process of its own; legacy
.NET Framework projects build as they do in the IDE, and a missing MSBuild is a clear error, not a silent hang.\
**How:** MSBuild.exe is located with vswhere, and each project runs as a child process without building its
references again; there is no dotnet build and no in-process MSBuild. packages.config projects are restored
through MSBuild before they build.\
`vswhere to find MSBuild` · `one MSBuild.exe process per project` · `BuildProjectReferences=false against double builds` · `MSBuild restore, no nuget.exe`

## Build, Rebuild and Clean

**What:** Build is the main button; its menu adds Rebuild, which compiles every project ignoring the cache, and
Clean, which runs MSBuild's own clean on every project like Visual Studio's Clean Solution; a Debug | Release
switch sets the configuration.\
**How:** Every run re-plans the working tree that is checked out, with the selected configuration. The
configuration is part of each project's signature, so switching dirties only what was built for the other one.
While a Sync, Deep Clean, Optimize, checkout or pull runs, build commands are refused rather than queued.\
`split button for every build command` · `-t:Clean, same as Clean Solution` · `configuration in the signature` · `one work gate, no clashing jobs`

## Leaves no orphaned processes

**What:** Whether the app is closed, crashes or is killed from Task Manager, no MSBuild.exe or child of it is left
behind.\
**How:** The build engine and every MSBuild.exe live in nested Windows Job Objects that the app owns; when the app's
last handle closes, Windows ends the whole tree. Each child starts suspended, joins the job and only then runs, so
nothing escapes in between.\
`nested Job Objects against orphans` · `kill-on-close for cascading shutdown` · `suspended start to close the gap`

## Stops safely, then continues

**What:** While a run is going, the Build button becomes Stop: the first press starts nothing new and lets the
projects in flight finish, a second press (Stop now, or Esc) ends them at once, and the next Build compiles only
what is left.\
**How:** A graceful stop waits at the project boundary, post-build copies included, and records what finished. A
hard stop terminates the job that holds the compilers and invalidates the records of the projects it cut short,
so the next Build compiles them from scratch.\
`drain at the project boundary for consistent output` · `job termination for an instant stop` · `record invalidation for a correct restart`

## Recovers interrupted work

**What:** If the engine dies mid-run (a crash, Task Manager, signing out) or the branch changes during a run,
half-built projects are not counted as built and the next Build rebuilds them; settings and ledgers are never left
half-written.\
**How:** Projects being built are written to an in-flight ledger, and anything still in it at the next start is
invalidated. A moving HEAD cuts the run short, and every state file is written atomically through a temporary file
and a rename.\
`in-flight ledger to find half-built output` · `HEAD watch to stop on a branch change` · `atomic writes against corrupt files`

## Leaves output folders alone

**What:** Build output lands exactly where Visual Studio would put it: the tool changes no output path and never
writes, copies or deletes anything in OutDir itself, and every run builds the one working tree that is checked
out.\
**How:** No OutDir, OutputPath or obj path is passed to MSBuild, and outputs are only read to make decisions; the
projects' own post-build steps distribute output as they do in Visual Studio. The one exception is a Clean you ask
for, where MSBuild removes the outputs the project itself recorded.\
`untouched output paths, same layout as VS` · `one working tree, no copies` · `read-only use of outputs`

## Never writes to git uninvited

**What:** Sync, Build and background work never write to the repository: pull, reset, switch, rebase, commit and
push never run on their own, and each of the three moves that do write to git is your call: a click on a chip, or
a setting you leave on.\
**How:** The three write paths (a fast-forward from the N behind chip, a checkout from the branch chip, and updating
external working copies when you leave that on) live in one file, and a source test forbids git write commands
anywhere else. Reads such as status, refs and fetch go through a separate path.\
`single git writer for auditability` · `source guard against accidental writes` · `fast-forward only, history kept` · `ref-only fetch, working tree untouched`

## Watches its own engine

**What:** If the build engine dies, hangs or fails to wake, the ribbon names the problem and offers Restart engine;
when the engine is back it says Engine ready and runs the startup Sync.\
**How:** The app watches the engine process and, in the moments when an answer is due (run start, stop, Sync,
maintenance, checkout, pull), shows an amber warning if the engine goes silent. Nothing unlocks on its own; the
decision is yours.\
`process watch for engine death` · `answer-due windows against false alarms` · `one-click restart`

## Exits without loose ends

**What:** The power button in the title bar, Ctrl+Q, Exit in the tray and, with Close to tray off, the window's
close button quit the app: with nothing running it closes at once, otherwise a card says what is running, which
processes will end and what will be lost.\
**How:** Stop and exit ends a run with a hard stop and cuts a Sync, Deep Clean or Optimize short, but waits for a
branch switch or pull so git is never left half-way. A closing screen shows the steps, and the app leaves together
with its engine and compilers.\
`one definition of busy for the right choice` · `job process count to show what ends` · `waits for git writes` · `in-flight ledger so the next Build recovers`

## Project list at a glance

**What:** Each row shows the project's state with a colour strip, a dot, a glyph and a word, next to its name,
decision, warning and duration: green is up to date, plain grey will build, red is a compiler error, and a
building row breathes amber.\
**How:** Strip, dot, glyph and graph node are all painted from one state value. During a run the list keeps the
building edge in view until you scroll and returns to it after a few idle seconds.\
`colour + glyph + text, colour-blind safe` · `one state source for every surface` · `follow mode to track the run` · `sticky layer headers`

## Filters by status or name

**What:** The counter chips in the action bar (all, building, up to date, failed, warnings) are also filters that
combine, and the box in the PROJECTS header (Ctrl+F) narrows the list by name; the graph dims to match.\
**How:** Chips combine with OR among themselves and with AND with the name box, and the name search is a
case-insensitive substring. The failed-project chips in the ribbon open the failed filter in one click, and the
filter survives builds and Syncs.\
`counter chips as one-click filters` · `OR/AND combination for a targeted list` · `name filter to find a project fast`

## Shows the graph, live

**What:** All projects are drawn as small nodes in build-order bands that always fit the panel. Select a node and it
is fitted with its direct dependencies and dependents and the links between them; during a run the graph plays out
as a calm play of light, so you see where the build is without reading a line.\
**How:** The bands match the list's groups, and there is no permanent web of edges. Every node is painted from the
same state as its row, so failed projects stay red and projects in a cycle show as amber cubes. During a run
waiting nodes dim, building ones glow with amber beads around them, and at the end the projects built in this run
light up one by one.\
`always-fit layout, no scrollbars` · `focus-and-fit to inspect dependencies` · `opacity story to follow the run` · `shared hover with the list`

## Shows errors, not noise

**What:** The console tells the story of the run as coloured, selectable plain text, and Compact console keeps only
what is useful from each project: errors in full, the first distinct warnings, the rest counted by code. Clicking a
project turns the console into that project's full log, opened at the top with the first error in front of you.\
**How:** Project logs and the console narrative are written to a folder per run on disk. The console keeps a
bounded number of lines on screen and reloads from disk when you scroll up, so nothing is lost and the console's
memory stays bounded however long the run.\
`disk-first logs, nothing lost` · `Compact console to cut warning noise` · `project page to read an error from the top` · `fixed memory budget for long runs`

## Says where the build stands

**What:** The ribbon under the title bar always shows what was asked last, the current phase, progress, the time
left and the error state; the event stream lists the run's events one line each, in order.\
**How:** A persistent operation label (SYNC, BUILD, REBUILD, CLEAN, DEEP CLEAN, OPTIMIZE, RESOLVE, SWITCHING BRANCH)
is amber while work runs and neutral after. Next to it sit the phase line, chips for the projects building now
(and, once the run ends, for the failed ones) and a thin progress bar; urgent warnings take the phase line's
place.\
`persistent label for what was asked last` · `remaining-time estimate` · `failure chips, one click to the failed` · `event stream as the run's timeline`

## Shortcuts like Visual Studio

**What:** In the window, F5 builds, F6 rebuilds, F7 cleans, Ctrl+F filters, F1 opens About, Esc closes the top
layer, stops the build or — with nothing left to do — closes the window like X, and Ctrl+Q exits.\
**How:** Window and global shortcuts come from one catalogue that also draws About → Shortcuts, the menu badges and
the tooltips.\
`F5/F6/F7 like Visual Studio` · `one shortcut catalogue for consistent text` · `Esc chain for a predictable close`

## Branch switch and fast-forward pull

**What:** The branch chip in the action bar shows the checked-out branch and switches it for real from a searchable
list; when you fall behind, an N behind chip updates the current branch by fast-forward only.\
**How:** A remote branch gets a local tracking branch. A switch on a dirty tree is refused by default; with Stash
and switch branches on, changes (untracked ones included) are stashed and named in the console, and the tool never
pops them. The pull is a fetch plus merge --ff-only: no merge commits, no rebases, refused on a dirty or diverged
tree.\
`git checkout from the tool` · `stash push -u to protect a dirty tree` · `merge --ff-only, history kept` · `cheap behind count`

## External projects, one graph

**What:** Folders, solutions or project files outside the repository are added as cards in Settings; they join the
same graph, the repository's projects link to them as real dependencies and they build first, and if you like each
git working copy is fast-forwarded before every Build.\
**How:** A card is only a path: a folder means every project under it, a .sln the projects it lists, a .csproj
itself, and the git working copy is found from the path. With Pull before build on, each copy is updated in card
order with fetch plus merge --ff-only, and uncommitted work stops the run before it starts.\
`multi-root scan into one graph` · `HintPath links for the right order` · `fast-forward update before each Build`

## Repairs a broken workspace

**What:** When the workspace will not build for reasons that have nothing to do with the code, two buttons next to
Sync help fix it. Deep Clean (the eraser, whose tooltip says Clean) deletes every project's bin and obj folders and
the build record, so the next Build starts from scratch, free of leftovers from another framework (an SDK-style
project then needs an Optimize before it builds). Optimize brings back missing packages and names the references it
cannot fix. And every run start already warns when a project's obj holds NuGet leftovers from another framework.\
**How:** Deep Clean does not call MSBuild: it deletes from the file system, external roots included, and runs a
Sync when done. Optimize restores old-style packages.config projects whose NuGet HintPath targets are missing and
every SDK-style project, which cures NETSDK1004 after a fresh clone or a Deep Clean; it then removes NuGet leftovers
(project.assets.json, *.nuget.g.props, *.nuget.g.targets) from a stale obj in old-style projects only, prunes dead
ledger entries and names a locked file instead of retrying it. The run-start warning deletes nothing.\
`bin/obj wipe for a clean workspace` · `NuGet restore to fix "doesn't build here"` · `stale-obj sweep for NuGet leftovers` · `run-start warning, nothing deleted`

## Sets up in one screen

**What:** On first launch the list invites you to set up the workspace; Settings has four sections (General,
Workspace, External projects, Layers), nothing applies until Save, and the whole setup can be exported to a JSON
file and imported on another machine.\
**How:** Workspace takes the repository root, and on first setup the button reads Save and sync; Save applies the
draft and starts a Sync. General holds the switches for startup, build, branches, console, engine and
notifications.\
`draft and Save for safe changes` · `JSON export and import for one setup across a team` · `first-run invitation`

## Groups projects into layers

**What:** Regex patterns on project names group the list and the graph into named layers, with non-matching
projects under Other, and each layer builds before the next one starts.\
**How:** Settings → Layers holds name and pattern cards; patterns are tried top to bottom and the first match wins,
and by default there are no layers. If the layer order would put a project before one of its dependencies, the run
starts with a warning instead of blocking.\
`regex grouping for long project lists` · `first-match rule for predictable layers` · `layer phases for a controlled order` · `reverse-layer warning`

## Installs and updates itself

**What:** The installer on GitHub Releases installs for the current user without admin rights; an installed copy
downloads new versions quietly and shows an Update pill when one is ready, What's new shows the release notes,
and About shows the version, the environment and the shortcuts.\
**How:** Velopack installs per user and applies full or delta packages; Restart to update installs now and Later
installs on exit. Every release is tested, packaged, tagged and published by GitHub Actions, and the release notes
come from the CHANGELOG built into the app.\
`Velopack for admin-free install and delta updates` · `GitHub Actions to test, package and publish` · `install on exit, no interrupted work` · `CHANGELOG as the single source of notes`

---

What the tool deliberately does not do, and its known limits, are listed in
[ARCHITECTURE.md § Known limits](../ARCHITECTURE.md#20-known-limits).
