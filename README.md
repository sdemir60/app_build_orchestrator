# Build Orchestrator

![ci](https://github.com/sdemir60/app_build_orchestrator/actions/workflows/ci.yml/badge.svg?branch=main)

A Windows desktop application that builds a multi-project .NET solution incrementally. It scans a repository
for projects, derives the dependency graph, decides which projects actually changed (from source content on
disk — an output's date alone never makes it current), and builds only those — in parallel, under a supervisor
process it owns.

It is a developer tool for your own machine: it builds a repository you would otherwise open in Visual Studio,
with your own privileges.

[`ARCHITECTURE.md`](ARCHITECTURE.md) is the technical reference behind this file: process topology, IPC
contract, the incremental decision, the build engine, the git surface, the UI architecture, the design system,
and the security boundary.

## What it does

You point it at a git repository. **Sync** scans the working tree for `*.csproj`/`*.sln`, reads the project
files as raw XML (MSBuild is never evaluated for this), builds the dependency graph, and compares the current
source signature against the stored build state to mark each project as "will build" or "up to date". **Build**
then runs the plan: each project is shelled out to a separate `MSBuild.exe` child process, ordered by the graph,
N at a time. Progress streams back to a live project list, a dependency graph view and a console. A run can be
stopped (in-flight projects are allowed to finish their post-build copy); after a stop or a failure, *Build*
picks up what is left, skipping everything that already finished green. There is one tree: every build
compiles your working tree as it is, on whatever branch is checked out. Picking another branch on the branch
chip checks it out for real; the tool never writes to git on its own, and changes you make in git outside the
tool are picked up automatically.

## Architecture

| Project | Target | Responsibility |
|---|---|---|
| `src/BuildOrchestrator.App` | net10.0-windows (WPF) | UI, MVVM, DI, tray icon, single instance, self-update. Owns the **outer Job Object** and spawns the Supervisor. |
| `src/BuildOrchestrator.Core` | net10.0 | Pure logic: project discovery, dependency graph, git service, incremental planning, state/config persistence, Job Object + process control primitives. |
| `src/BuildOrchestrator.Supervisor` | net10.0-windows | Separate engine process: run queue, **inner Job Object**, one `MSBuild.exe` child per project, log parsing, IPC server over stdio. |
| `src/BuildOrchestrator.Contracts` | net10.0 | App ↔ Supervisor IPC contracts: commands, events, JSON serialization, NDJSON framing. |
| `tests/BuildOrchestrator.Tests` | net10.0-windows (xUnit) | Unit, process-control, WPF and integration tests. |

Process layout:

```
BuildOrchestrator.App.exe   (WPF; owns the outer job, but is not a member of it)
  │  stdio, newline-delimited JSON
  ▼
[ outer Job Object — KILL_ON_JOB_CLOSE ]
  BuildOrchestrator.Supervisor.exe
    ├── git.exe / vswhere.exe        (plain child processes — outer job only)
    └── [ inner Job Object — KILL_ON_JOB_CLOSE + CPU rate cap + priority ]
          MSBuild.exe (one per project) + whatever its targets spawn
```

Key consequences of that layout:

- The App never references the Supervisor assembly. It copies the Supervisor's output next to itself and
  starts it as a process; all communication is IPC over stdio (`Contracts`). The Supervisor's **stdout carries
  NDJSON only** — diagnostics go to stderr.
- If the App dies for any reason (including being killed from Task Manager), the last handle on the outer job
  closes and the whole tree dies with it. There is no managed parent-watcher and no PID heuristics.
- Builds are **shelled out**, never done in-process. `MSBuild.exe` is located through `vswhere` (VS or Build
  Tools), and every project is invoked with `-p:UseSharedCompilation=false -nodeReuse:false` so that no
  compiler server survives outside the job. The temporary assembly WPF compiles for a project's own XAML types is
  built as metadata only, which shortens WPF compiles and leaves the output unchanged.
- No output path is ever changed: no `-p:OutDir` / `-p:OutputPath` and no intermediate-path redirect is passed,
  so output — and `obj` — lands exactly where Visual Studio would put it.
- "Did it change?" is answered from the **content of the source files on disk** — the project file, the items
  it declares, everything build-affecting under its folder, and the `Directory.Build.*` files above it. No
  version-control command takes part: git is used for fetching, branches, checkout and updating external
  working copies, never for deciding. Hashes are cached by size and modification time, so a normal run only
  stats those files. An output's date never makes the tool's own output current; it is compared with the
  inputs only for an output built elsewhere — in Visual Studio, say — which counts as current when no input is
  newer than it (ARCHITECTURE.md §7.6).

## Requirements

**To use it:**

- **Windows.** WPF, Job Objects and the Win32 process control are not portable.
- **.NET 10 Desktop Runtime** — the installer sets it up when it is missing.
- **Visual Studio 2022 or Build Tools** with the `Microsoft.Component.MSBuild` component. The engine resolves
  `MSBuild.exe` at run time through
  `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe`; without it, builds fail with a resolve
  error (the Supervisor itself still starts). "Open in Visual Studio" additionally needs a full VS IDE install.
- **`git` on `PATH`** — the engine invokes `git` by name.

**To build it**, additionally:

- **.NET 10 SDK** — the band `global.json` names, or a later 10.0 feature band. CI builds with the same file.

## Install

Download the installer from the latest release — the link always points at the newest version:

<https://github.com/sdemir60/app_build_orchestrator/releases/latest/download/BuildOrchestrator.App-win-Setup.exe>

- It installs for the current user only, without admin rights, into `%LOCALAPPDATA%\BuildOrchestrator.App\`, with
  Start menu and desktop shortcuts.
- The installer is not code-signed yet, so Windows SmartScreen may stop it the first time: choose
  **More info → Run anyway**.
- From then on the app keeps itself up to date ([Update](#update)).

**Moving from a copied folder.** Close the old copy first — tray icon → *Exit*; the app is single-instance, and a
running old copy would answer the new one's start by coming to the front instead. Run the installer, then delete
the old folder. Settings, caches and logs live in `%LOCALAPPDATA%\BuildOrchestrator\`, which belongs to neither
copy, so they carry over; *Start with Windows*, if it was on, is re-pointed to the installed copy on its first
start.

**Uninstall** from Windows Settings → Apps → Installed apps. It removes the installation, its shortcuts and the
*Start with Windows* value; your data in `%LOCALAPPDATA%\BuildOrchestrator\` stays — delete that folder by hand to
remove it too.

## Build, test, run

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test  tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
dotnet run   --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj
```

Close any running instance of the app before building — a running Supervisor keeps its own binaries locked.
The test suite is expected to be fully green. The filter above excludes the acceptance tests — some build a real
large repository (about two minutes), the others run the real MSBuild on small throw-away projects — which are
run separately with `--filter "Category=Acceptance"`.
Measurement tests are part of the run; the ones that open windows or load the machine report as skipped unless
their environment variable is set (ARCHITECTURE.md §17.5).

CI (`.github/workflows/ci.yml`) runs the same build and suite on every push to `develop` and `main` and every pull
request. A test that cannot run on the hosted runner carries `Category=LocalOnly` and is excluded there only; the
local full run stays the gate.

## Package and release

Packaging has one owner, `scripts/package.ps1`: the publish command, the release-note cut and the installer build
are written there and nowhere else, and a local try-out, `verify-publish.ps1` and the release workflow all run it.

```powershell
dotnet tool restore                                                  # once: the pinned Velopack CLI (vpk)
powershell -ExecutionPolicy Bypass -File scripts\package.ps1         # publish + notes + installer
powershell -ExecutionPolicy Bypass -File scripts\package.ps1 -PublishOnly -PublishDir <folder>
```

Version, product name and company are read from `Directory.Build.props`. The publish is framework-dependent and
folder-based (`Release`, `win-x64`) and lands in `artifacts\publish\`; that version's section of `CHANGELOG.md` is
cut into `artifacts\notes.md`, beside Velopack's own folder; and Velopack packs the installer,
`BuildOrchestrator.App-win-Setup.exe`, and the update packages into `artifacts\velopack\` — with a delta package
too when the previous release's package is there (`-DownloadPrevious` fetches it; the release workflow does).
`-PublishOnly` stops after the publish; `-WhatIf` prints what would run and touches nothing, the network
included; `artifacts\` is ignored by git.

**The `supervisor\` subfolder next to the published `.exe` is mandatory.** It is not an optional extra: it
*is* the build engine. The App resolves `<app folder>\supervisor\BuildOrchestrator.Supervisor.exe` at startup;
if it is missing, no build can run and the app says so instead of failing silently. The publish target adds
that folder to the publish list, and the build fails outright rather than producing an engine-less package.
`Assets\GEIST-LICENSE.txt` also ships with the output.

Not supported:

- **`PublishSingleFile`** — rejected by an MSBuild target with an explicit error. `AppContext.BaseDirectory`
  would point at the extraction directory and the `supervisor\` subfolder cannot go into the bundle.
- **Self-contained publish** is not verified. The only publish mode that is exercised end to end is the
  framework-dependent one `package.ps1` runs.

To verify a publish output end to end:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify-publish.ps1
```

It first refuses to measure anything if an instance is already running (the app is single-instance), then
publishes to a temp folder through `package.ps1 -PublishOnly` and checks the whole chain: publish exit code,
layout, an NDJSON round trip against the published Supervisor binary, a full Sync + Build driven through it
against a throwaway workspace (proving the published binary really compiles and writes the DLL), launching the
published `.exe` and confirming via WMI that the Supervisor child was spawned from that same folder, reading the
console boot line and the ribbon state out of the live window through UI Automation, and finally killing only
the App and proving the Supervisor dies *by itself* through the job cascade. Exit code `0` = pass, `1` = fail,
`2` = precondition not met (close the running instance first — tray icon → Exit).

**Releasing** is one request in a Claude Code session: `/release` (the project skill in `.claude/skills/release/`).
Daily work lands on `develop`; `main` carries releases only, and the release script is the only thing that moves
it. Claude writes the new version's `CHANGELOG.md` section — the rules are in [`CLAUDE.md`](CLAUDE.md) — and runs
`scripts\release.ps1 -Version X.Y.Z` on `develop`. The script stops before touching anything unless `develop` is
clean and level with `origin/develop`, `main` holds nothing `develop` lacks, the section is on top and dated today,
the tag exists neither locally nor on `origin`, no copy of the app is running from this checkout (it keeps the
checkout's files locked — tray icon → Exit; an installed copy elsewhere is no obstacle), and `develop`'s CI run is
green (`-SkipCiCheck` skips that check when offline or in an emergency); then it writes `Version`, builds, runs the
full suite, commits `release: vX.Y.Z` on `develop`, merges `develop` into `main`, tags the merge, moves `develop`
up to `main` and pushes `main`, `develop` and the tag atomically — all three reach GitHub or none does. Should it
stop after the release commit, its last line prints the commands that undo the local commit, merge and tag — unless
a push that reported an error still left the tag on GitHub, which it then reports instead. `-DryRun` runs every
check and stops there. The tag starts `.github/workflows/release.yml`: it checks that the
tag, `Version` and the top `CHANGELOG.md` section agree and that the tagged commit is on `main`, runs the CI build
and suite, packages with `package.ps1` and publishes the GitHub Release — the installer, the update packages and the
version's notes as its text. Installed copies pick it up on their next check.

## Using it

1. **Configure the workspace** — on first run the project list invites you into Settings rather than opening
   a folder picker: starting takes more than one setting now. Settings is split into sections down a left
   rail — **General**, **Workspace**, **External projects**, **Layers** — and on first run it opens on
   Workspace, where the repository root lives (nothing is discovered without it); afterwards it opens on
   General. External projects and layers are optional. *Browse…* only stages the folder in the dialog; *Save*
   is what applies it, and on first run, once a root is entered, it reads *Save and sync*. If you already have a
   settings file, *Import settings…* on the invitation opens the dialog and, once it has settled, the file
   picker, centred over the window.

   **General** holds switches in four groups — Startup, Build, Branches and Notifications. *Pull before build*
   (see step 4), *Stash and switch branches* (see step 3), *Start with Windows*, *Start minimized to tray*, *Close
   to tray* and *Show notifications* all work, and *Save* keeps them. *Close to tray* and *Show notifications* are
   on by default; what they change is described with the tray, further down.

   *Start with Windows* starts the app when you sign in to Windows. *Save* applies it at once, and the switch
   always shows what Windows will actually do. *Start minimized to tray* (available while *Start with Windows* is
   on) decides how that start looks: on, the app starts hidden in the tray; off, the window opens. Starting the
   app yourself always opens the window. If you turn the app off in Task Manager's *Startup apps*, the switch
   reads off and says so; switching it back on and saving turns it back on there too. Both switches travel in an
   exported settings file, so saving an imported file that has *Start with Windows* on turns it on for that
   machine.

   **External projects** are extra roots outside the repository — each card is a path (a folder, a solution
   or a project file). Everything found under a card joins the same project list and
   the same graph as the repository's own projects, in an *External* group at the top, and is built first.
   Cards reorder the same way layer cards do, by dragging the grip; that order is the order their working
   copies are refreshed in. What Build does with them is step 4 below.

   **Layers** start empty. *Add layer* appends a blank row; its inputs show example names and patterns as
   placeholders, never as values.

   Settings can also be exported, imported and cleared from the dialog's footer. All three only change the
   form — nothing is applied until you press *Save*. Saving with the repository root empty — after *Clear*, or
   with the root deleted — closes the workspace: the app returns to the first-run screen, with the invitation
   in the project list.
2. **Sync** — scans, builds the graph, and marks which projects would build. Nothing is compiled here. The
   first Sync runs by itself when the application starts. No build starts before it has run: a run before the
   first Sync would compile for real while the list and the graph stayed empty. While a Sync is *running* — the
   one the application starts by itself included — the engine handles one thing at a time: *Sync* shows its busy
   state and *Build*, *Rebuild*, *Resolve cycles*, the row actions and `F5` are disabled until it finishes, so
   nothing waits behind it. A Clean, an Optimize, a branch switch or a pull closes them the same way.

   If two projects produce the same assembly name, Sync warns and names both: a reference to that DLL cannot be
   resolved to one producer, so its dependency edge is dropped and nothing waits for it. Rename one of them, or
   drop one of the roots that contributes it.

   **You rarely need to press Sync yourself.** The tool watches the repository's HEAD: a commit, a pull, a
   reset or a branch change made in Visual Studio or a terminal is picked up about a second and a half after
   git finishes, and switching back to the window refreshes the decisions too (at most once every five seconds).
   These automatic Syncs are quiet: they do not clear the console, do not scroll the list, and add at most one
   line to the event stream (`synced after commit`, `synced · 3 projects changed`). They do not go to the
   network either, so the `N behind` count they show is against the last remote state you fetched; the *Sync*
   button fetches. The list and the graph are only rebuilt with their opening animation when a project was
   added or removed — otherwise the rows simply change colour in place, the graph keeps its zoom, and rows the
   last build finished are re-decided too, so a project that changed in the background turns grey again.
   Pressing *Sync* yourself, changing branch or switching *Debug*/*Release* starts the screen over instead: the
   console and the event stream empty, and while the Sync finds the projects the list reads *Discovering
   projects* with a running count (`31 found`, or `31 found · 29 repository · 2 external` when external roots are
   registered) and the graph says it appears once they are found; then both come back with their opening
   animation and the graph fitted to the panel. If that Sync fails, the previous list and graph come back. The
   Sync that runs at start-up, after a *Clean* or an *Optimize* and after a repository change shows the same
   count while the list is empty. A
   *Debug*/*Release* switch runs that Sync in the new configuration, and its rows stay uncoloured until the
   answer arrives; like the branch chip, the switch is locked while a build, a Sync or another action is running.

   **Sync colours every row with the state of its output:** green when it is up to date, plain grey when it
   will be built, red when its last build failed with a compiler error. A project you built in Visual Studio is
   green too, as long as none of its inputs is newer than that output. In a cycle member the graph node's cube
   is always amber. Before the first Sync nothing is known, so every row sits in the start mode — a four-arc
   ring in place of the dot, a dashed glyph — and every graph node draws a dashed border. Why a row will build
   is readable from the **decision label** at the right end of each row: `modified` (its own files changed,
   `modified · local` when one of them is also dirty in `git status`), `affected` (only a dependency changed,
   or its copy in a shared folder no longer matches its output), `never built` (never built by this tool, or
   its output file is gone), `failed` (failed at this source), or `up to date` — whether because this tool's
   last build still matches or because an output you built elsewhere does; the tooltip says which. The label
   never carries a time: a row answers what the project's output needs, and the minutes behind that answer
   never changed it. A project that built successfully against a dependency that was
   failing, and has not changed since, reads the same `up to date` — a later Build leaves it
   alone until that dependency is healthy again, and the warning triangle's tooltip names which one.

   The Sync line in the console also says where you stand against the remote:
   `HEAD a3f81c2 · 3 commits behind origin/main`. When you are behind, a small **`3 behind`** chip appears next
   to the branch chip; clicking it fast-forwards the repository (`merge --ff-only` — never a merge commit, never
   a rebase, and never on a dirty or diverged tree; stashing does not apply here) and runs a Sync afterwards. A
   refused pull says why in an amber console line and a short line in the event stream. The
   chip is locked while a build, a Sync or another git action is running, and while git itself is in the middle
   of an operation. Offline, the distance is unknown and the chip is not drawn. The tool writes to your
   repository only when you click: this chip, the branch chip below, and the external working copies of step 4.
3. **Branch** — the branch chip shows the branch checked out in your working tree. Picking another branch from
   its list checks it out (`git checkout`; a remote branch gets a local tracking branch), then the console
   starts over with a `Switched to <branch> (<sha>) — from <previous>` line and a Sync follows. If the working
   tree has uncommitted changes, *Settings → General → Stash and switch branches* decides:
   - **off** (default) — nothing happens: the console says, in amber, `warning: N files have uncommitted
     changes — commit or stash them first`, and the event stream adds `branch switch refused — N uncommitted
     files`;
   - **on** — the changes, untracked files included, are stashed first (`build-orchestrator: leaving <branch>
     for <target>`) and the console tells you to restore them with `git stash pop`. The tool never pops a
     stash for you.

   Branch changes you make outside the tool are reflected the same way: the chip follows HEAD and the console
   opens a new section. If one happens while a build is running, the build stops gracefully — nothing new
   starts, what is compiling finishes, and those results are not trusted — and the new section's first line
   says how many projects were built, how many were not, and where the run's logs are. While git is in the
   middle of a merge, rebase, cherry-pick or revert, the branch chip carries an amber dot, checkout and pull
   are locked, and automatic Syncs wait until git is done; *Build* and *Sync* still work, with a warning line.
4. **External projects** *(optional)* — some projects a build depends on may live outside the repository. Add
   them under *Settings → External projects*: type or paste the path — a folder, a `.sln` or a `.csproj`.
   That path is the whole card; the git working copy above it is found for you.

   Sync then scans that path the same way it scans the repository root. A folder contributes every project
   under it; a solution contributes the projects it lists; a project file contributes itself. They appear in
   the project list and the graph in their own **External** group at the very top, and they are built before
   the repository's own projects — your layer patterns are not applied to them, and they never fall into
   *Other*. A repository project that references one of their DLLs also gets a **real dependency edge**, so
   that order is enforced by the graph and not only by the grouping. Everything else is worked out from the
   path each time, so moving a project or recreating its working copy needs no edit here. A path that resolves
   to no project at all is called out: Sync warns, Build refuses to start.

   Before each Build their working copies are refreshed, in card order — unless you turn **Pull before build**
   off, under *Settings → General* (the line at the bottom of the External projects page says whether it is on
   and takes you there). Each root gets a fetch and a fast-forward — never a `pull`, so
   nothing is rewritten on your behalf.
   **Uncommitted changes stop the run before it starts**, with a line naming the project and its folder:
   commit or stash them and press Build again. A branch that has diverged from its remote stops the
   run the same way. A remote that cannot be reached only warns and the local version is built; so does a path
   with no `.git` above it, which is simply built as it stands.

   With the switch off **nothing** is fetched, merged or blocked — external projects are compiled exactly as
   they sit on disk, the way the repository's own working copy always is. Whether they changed is still worked
   out correctly: for external roots it is read from the files themselves rather than from git, so an
   uncommitted edit marks the project stale just as a commit would.

   When a copy is refreshed the console says where it landed — `Updated external 'DoganTrend' → a1b2c3d`, a
   short sha. Reading a revision is local (`rev-parse HEAD`), so a root reports one even with the switch off;
   a path with no `.git` above it reports none. The revision is only ever shown for information — no build
   decision reads it.

   **Upgrading from an older version rebuilds everything once.** The way a signature is computed changed, so
   the signatures already on record cannot be compared against the new ones. The first Build after the upgrade
   compiles the whole workspace; the second is incremental again.

5. **Build / Rebuild** — from the split button and its menu:
   - *Build* — only stale projects: what changed, what failed, what was never built, and whatever depends on
     one of those — except a project that already built successfully against a dependency that was failing,
     which waits until that dependency recovers instead of being retried every time.
   - *Rebuild* — all projects, cached state ignored.
   - *Clean* — `msbuild /t:Clean` on every project, external projects and cycle members included; nothing is
     compiled and the caches are untouched. Like Visual Studio's *Clean Solution*, it deletes every output
     MSBuild recorded for a project, wherever it was written — a shared output folder included. Each cleaned
     project reads *never built* until the next *Build* compiles it — *Resolve cycles*, for a cycle member, and
     when a Clean cleaned any, the event stream says so as it ends. No confirmation; *Stop* stops it.

   **Every operation opens the same way.** A short neutral moment, then the projects this operation will touch
   light amber one at a time in random order, then everything else fades back and the run begins. The run
   starts when that sequence ends, so it always plays in full; the operation itself begins on the first frame,
   though — the pill lights, the button becomes *Stop*, the console records the request — and only the command
   to the engine waits. When the run ends, the graph — and only the graph — lights the projects it actually
   built, one by one like fluorescent tubes, then brings the rest back together. With reduced motion turned on
   in Windows, neither plays and the run starts at once.

   There is no *Continue* and no *Retry failed*. *Build* already covers both: a project that was killed or that
   failed had its recorded state invalidated, so it is stale again, while everything that finished green is
   skipped as up to date.

   Nothing compiles the moment you click, and *Stop* is available throughout. While the opening sequence is
   playing the engine has not been asked for anything yet, so a stop there cancels the run outright — the
   console says `Cancelled — build not started` and nothing is sent. Once the sequence ends the engine works
   out what to build — updating external working copies if that is on, scanning, building the graph, then
   computing what changed — which on a large repository takes seconds; the ribbon reads
   *"▸ Starting — resolving what to build"* and the console lists each step as it completes. A stop in that
   window is a real stop, and it still compiles nothing.
6. **Stop** — nothing new is dispatched and the in-flight `MSBuild.exe` children finish, including their
   post-build copy, so no half-written DLL is left behind and their work is kept. Until they do, the button — and
   the tray menu's Stop item — reads *Stop now* and the ribbon reports how many are still finishing. Pressing
   *Stop now* — or `Esc` — does not wait, whether you asked for the stop or a branch switch did: the in-flight
   compiles are terminated at once, the console says how many, and the button reads *Terminating…* and is
   disabled. Those projects count as failed, so the next Build compiles them again.
   To carry on, press *Build* again: everything that already succeeded is skipped as up to date, so only the
   remaining work runs. The elapsed clock starts from zero — it is a new run.

**Reading the list.** One colour tells one story: the stripe on the left, the dot beside the name, the status
glyph and the graph node all carry the same status, so there is nothing to cross-reference. Green does not
say who built the output: hover the decision label and its tooltip says `built outside this tool` when it was
not this tool. A single amber triangle in the fixed slot on the right means something is off with this project's
dependencies — a cycle, or
a dependency that failed or was not rebuilt — and its one-line tooltip says which; the details are in the
project log. The counter chips in the bottom bar count state — `✓` up to date, `✗` failed, plus what is
building right now — and each is a filter; they **combine**: press the tick and the cross together to
see everything that is green or red, and type in the filter box (`Ctrl+F`) to narrow that further. A skipped
project is not a state of its own: it keeps its colour and is counted under the chip it shows, while the run's
*N skipped* stays in the ribbon's summary. Each active chip lights in its own colour, and the chip in the
PROJECTS header lists what is on.

**Per-project actions** live on the row: hover it for a play button and a ⋯ menu — Build and Rebuild for
that one project — and right-clicking the row opens the same menu. A run started this way compiles that
project alone, and it compiles it even when nothing changed: pressing play is an instruction, not a question.
*Rebuild* runs MSBuild's own clean-then-build for it, and *Clean* is Visual Studio's project clean —
`msbuild /t:Clean` on that project, nothing compiled and no caches touched; afterwards the project is marked
for building again, because its outputs are gone. Dependencies are never rebuilt, and one that is stale is
reported as a dependency issue on the row and in the project log, so a later *Build* compiles the project
again once that dependency is healthy again — not on every *Build* regardless.
Starting from a row clears the selection, so a graph focused on some node returns to the fitted view; the
filter stays, as it does for every build. While
the run is in flight the row's play button becomes a red Stop and the other rows' actions wait.

**Layer headings jump.** Hover one and it lifts a step; click it (mouse only — it is not a Tab stop) and the
list scrolls that group's first row to sit just under the stacked headings above it. It only moves the scroll
position — selection, the filter and the console are untouched.

Projects that reference each other's output form a dependency cycle. *Build* never compiles them — it skips
them with the reason `in dependency cycle`. **Resolve cycles** — the
third icon (unlink) of the maintenance box next to *Sync* — is what compiles them, and it is the only thing
that does. It is enabled only when the workspace actually has a cycle, and its tooltip says what it will do
once a Sync has found one: `Resolve cycles — build the N cycle projects in repeated rounds: stale references
first, then rebuild until they converge` — with ` · N upstream to build first` appended when the run's scope
must first compile stale prerequisites, so the bill is visible before the click. While it runs the ribbon reports the engine's own count —
`Resolving cycles · round 2/3 · 5/7 · 12s` — rather than promising a fixed number of passes. It is meant to be pressed **before** a build, not instead of one: it compiles the cycles,
then *Build* takes care of everything else, including whatever depends on them.

*Clean* — the eraser in that box — is the workspace reset: it deletes the `bin` and `obj` folders of every
project it finds, external roots included, along with their build state, so the next *Build* compiles
everything from scratch. It starts on the click, with no confirmation dialog: the project list and the graph
empty out, the button turns amber with a spinner, and when the deletion is done a *Sync* runs by itself and
fills them in again — counting the projects it finds in the meantime. The console keeps the whole story. It is not the per-project *Clean* in the row menu and
not the Build menu's *Clean*: no `msbuild /t:Clean` runs. Files held by a running application are skipped and
reported rather than failing the Clean. An SDK-style project loses its restored package assets with `obj`, and
a build does not restore them: run *Optimize* before building it again.

*Optimize* — the gauge in the middle of the box — is the workspace doctor: it restores missing NuGet packages
and every SDK-style project's package assets, names the broken references a restore cannot fix, clears stale NuGet leftovers out of `obj` and prunes dead
cache entries, over the same projects a build sees, external roots included (cache entries written under a
schema other than the current one go too, wherever they point). It changes no build decision —
nothing it does makes a project stale. Its flow is the same as *Clean*'s: the project list and the graph empty at
the click, its button turns amber with a spinner, and when it finishes a *Sync* runs on its own to put the plan
back. The console reports each step's result; a failed restore shows MSBuild's error messages, not its whole
output.

Why cycles are a button and not something *Build* does for you: a cycle is built as one unit — the members
compile in barriered waves (members that don't reference each other directly share a wave and compile in
parallel, up to the run's parallelism; direct neighbours never overlap; the members most others reference go
first), and a member compiles again only when the **API surface** of the sibling file it actually built
against has changed. A body-only change settles in a single round, right after a *Clean* too; an API change
costs a second round only for the members that read the old API; three rounds is the ceiling, and a
member that fails while its inputs are provably settled stops the run at once — an identical compile cannot
end differently. Even so the worst case is members × rounds of compiling, which next to an ordinary
incremental build is a large and unpredictable bill. Behind a button you decide when to pay it.

Such a run compiles the cycles **and whatever they depend on that is out of date** — otherwise a member would
be compiled against a stale DLL, come back green, and then be recorded as up to date so that no later build
ever fixed it. The event stream opens with `Cycles started — N cycle members · P prerequisites · up to K
rounds`, so the split between the cycle itself and what it needs first is visible before anything compiles.
Everything past that scope collapses into a single line, `N outside cycle scope — skipped`, rather than one
line per project — those are Build's job, and Build is what you press next.

The run reads like any other beyond that: each round prints its own line, `cycle round R/K — N members`, and
while members are actually compiling the active line names the latest of them and its place in the group,
`member I/N · round R/K`. The members of one wave compile at the same time, each with its own spinner, and the
count of projects shown compiling never exceeds the run's parallelism. A member whose compile in the round has
finished waits for its group: it shows the clock glyph, no breathing highlight, and a duration column that
stays at `—`, because its result is only settled when the whole group is — every member gets its result at the
same moment, when the group's verdict arrives. The event stream then says which verdict it
got — converged, no progress (repeating the failed compiles could only repeat their result), or hit the round
cap — with how many rounds it took. Cycle rows show the normal
build icons — green, red, the spinner — and carry a single amber warning triangle to say where they sit. Its
tooltip is one line (`In a dependency cycle`); the loop itself is named in the project log,
`Domain.Parts → Parts.Inventory → Parts.Api → Domain.Parts`. In the graph a member the operation did not build
keeps its grey frame but shows an **amber cube** inside it — the triangle's proxy, so a finished run still
answers "why was this one not built?". A member the run actually compiled wears its result colour alone —
except a member of a group that did not settle, which stays grey (to build) whatever its last round said,
because nothing it produced is kept. One member escapes that grey: the one whose compile failed while every
sibling output it read was already final is the proven culprit — it turns red like any failed build, reads
`failed` with *Resolve cycles will retry it*, and keeps that verdict across Sync, so the project that actually
breaks the cycle is visible at a glance while its innocent siblings wait in grey.

Pressing the button again is always a real attempt. A cycle that has settled is skipped as up to date, so the
press costs nothing when nothing changed; a cycle whose only reason to rebuild is a **broken prerequisite** is
skipped too (`dependency still failing`, with the culprit named) until that root recovers — rebuilding it
would only relink every member to the same stale output; a cycle that did *not* settle is tried again from round one, and the
run log says why it is worth the rounds (`retrying — did not converge at this signature`). The engine
remembers a failed convergence, but only to report it — refusing to retry would mean the button silently doing
nothing, and the signature covers sources alone, so a package restore or anything outside the cycle may well
have changed since. The summary line says how many projects are stuck in one, so a run whose only casualty is
a cycle that would not converge never reads as an unqualified success — those rows keep the amber warning triangle
with a tooltip saying their projects are still out of date, and rows that compiled but never saw two clean
rounds carry the same triangle with a tooltip saying their output may be one generation stale. And when an ordinary *Build* finishes with cycle members
still dirty, the event stream adds a closing line pointing at *Resolve cycles* as the next step.

The console keeps long MSBuild lines on one line rather than wrapping them, so it scrolls sideways as well as
down: a horizontal wheel or a touchpad's two-finger sideways pan moves it, not only dragging the bar. At the
bottom sits a blinking amber block caret on a line of its own; output piles up above it and it stays put, with
`ready` beside it while nothing is running. Scroll away from the bottom and it steps aside with the
`⌄ latest` pill. Touch the panel — wheel, scrollbar or the navigation keys — and it is yours: arriving lines
will not pull it down, and about five seconds after you stop, it returns to the bottom and picks the stream
back up. Nothing is deleted from the top while you are reading, either, so the text you are looking at holds
still. The event stream behaves the same way and keeps a caret of its own on a timestamped line from the
moment you open the app, whether or not anything has happened yet.

A project's log opens **at the beginning** and stays there — what you are looking for in a build log is the
first error, not the last line — so it does not follow the output and does not return on its own. Scroll to
the bottom yourself and it starts following again. The header's `Back` button (an arrow icon and the word
"Back") returns you to the run narrative at its end.

**Every project has a page, log or not.** A skipped project never writes a log file at all — the reason goes
to the run's decision log — so clicking its card used to look like it did nothing. The page now opens either
way and answers in two lines: why the project is in the state it is in, and what the tool has on it — the
commit it was last successfully built at, or that it has never been built.

The dependency graph fits the panel at every size: nodes are unnamed mini squares laid out in build order,
and the spacing shrinks until they do fit — so there is never a scrollbar and never a part of the graph you
have to go looking for. Hover a node to see its full project name; its list row lights up with it, and
hovering a row lights its node the same way (when the counterpart is in view). Click one — or a list row, or a stream
line — and the graph zooms to that project with its direct dependencies and dependents, draws the amber
dependency lines for that neighbourhood only, and fades everything else back. Click it again, or click empty
background, to let go.

While a build runs the graph quietens rather than moving: untouched projects fade back, the ones compiling
stay bright and carry a ring of circling amber dots, and each project that reaches a result holds its colour
bright for a moment before settling. Projects that are skipped settle quietly with no moment of their own —
they stay exactly as dim as the queue and never move at all, because the graph is there to show what changed
and they did not. The camera does not follow the run either: pressing Build brings it back to the fitted view
once, at the start, and it then holds still for the rest of the run. Filtering the list dims the
graph to match: the projects still in the list stay bright, everything else fades back, and the fade is slower
than the ones a run makes so it can be followed by eye. Building keeps your filter — the list stays filtered —
but the graph shows the whole run unfiltered, from the opening sequence to the closing lights, and returns to
the filtered view a moment after they end — a stopped run too, when it built something (at once if nothing was
built).
Drag the empty background to pan (the cursor turns into a hand) and the mouse wheel zooms at the pointer;
clicking empty background with nothing selected returns the view to its default, and both a graph rebuilt by
a Sync and a build that starts bring it back to that default view.

You do not have to keep the window open to watch a build — or to start one: `Ctrl+Shift+Space` builds from
anywhere and `Shift+Space` shows or hides the window (see *Keyboard shortcuts*). With *Close to tray* on (the
default), closing it with `X` drops the app to the tray, and if a build is running the product mark animates in the bottom-right corner of
the screen — click it to bring the window back, or click straight through the empty space around it to whatever
is underneath. While it is in the tray the window does not redraw itself as the run progresses, and bringing it back
brings it up to date in a single pass. When the run finishes the mark plays out its last turn, fades, and Windows shows a notification
with the result — click it to bring the window back too — and the same sentence is waiting in the ribbon when
you open the window again. A run that finishes while the window is open shows no notification — the ribbon
already says it. Turn *Show notifications* off and the app shows no Windows notification at all — not the
result, not the one-time *still running in the tray* note, not the note a Build hotkey press leaves when something
keeps it from starting, not the warning a second copy of the app gives when it cannot bring the window forward;
the corner mark is not a notification and still appears.

To quit, choose *Exit* from the tray icon's menu — or, with *Close to tray* off, just close the window.
Quitting waits for the work in flight: with nothing running the app closes at once; otherwise a running build is
stopped the way *Stop* stops it — its in-flight projects finish — and a Sync, Clean, Optimize, branch switch or
pull is left to finish. Meanwhile the window stays on screen and comes forward (out of the tray, or restored if
you closed it while minimized), the ribbon reads *▸ Stopping — …* and closing again changes nothing. Only an
engine that stops answering, or dies, does not hold the exit up: the app then closes and takes the engine with
it, even if a long branch switch, pull or build step was only quiet.

If the engine ever stops answering — no event at all while a run start, a stop, a Sync, a Clean, an Optimize, a
branch switch or a pull is still waiting for its answer — the ribbon says so in amber and offers *Restart
engine*. Nothing unlocks by itself, because a drain can legitimately take minutes; the action is there for the
case where waiting is no longer the answer. Restarting kills the engine and every `MSBuild.exe` under it, then
brings a fresh engine up.

### Keyboard shortcuts

| Key | Where | Action |
|---|---|---|
| `Shift+Space` | anywhere | Show or hide the window |
| `Ctrl+Shift+Space` | anywhere | Build without bringing the window up |
| `F5` | window | Build — only starts; while a run is in flight or a Sync, Clean, Optimize, branch switch or pull runs, it does nothing |
| `F6` | window | Rebuild |
| `F7` | window | Clean — the Build menu's Clean, not the maintenance box's Deep Clean |
| `Ctrl+F` | window | Focus the project filter |
| `F1` | window | About — version, shortcuts and diagnostics |
| `Esc` | window | Close the topmost open layer: dialog → popover/menu → selection; with none open, stop the running build |

The two global hotkeys work whether the window is in front, behind Visual Studio or in the tray. `Shift+Space`
hides the window only when it is in front; from the tray, minimized or behind another window it brings it
forward. `Ctrl+Shift+Space` starts the same Build as the button — nothing happens while a run is in flight or while
a Sync, Clean, Optimize, branch switch or pull runs (with the window hidden in the tray, a balloon says why) — and
with the window hidden the tray indicator and the result balloon report a build it does start.
Both are read from `ui-state.json` (`ShowHideHotkey`, `BuildHotkey`); there is no UI for changing them
(Settings has General, Workspace, External projects and Layers). An older `Hotkey` entry (`Alt+B`) is ignored.
If one cannot be registered — another application already owns that combination — it is silently disabled; the
tray icon still restores the window, and the About screen marks that row *unavailable* so the loss is visible
rather than mysterious.

`Esc` stops a Build, Rebuild or Clean the way *Stop* does — the projects in flight finish and the next Build
carries on from there. Pressing it while a stop drains — yours, or one a branch switch started — is *Stop now*: the
in-flight compiles are terminated at once, and a further `Esc` does nothing. A Sync, Deep Clean, Optimize, branch
switch or pull cannot be stopped;
`Esc` during one writes a single console line saying so.

Disabled commands stay disabled when triggered by a shortcut — the key never bypasses the button's state.
`F1` toggles About and works even while another dialog is open: About opens on top, and Esc closes the topmost
layer first, so a lower one (an unsaved Settings draft, say) survives underneath.

### About

The `i` button sits at the right end of the title bar's command group, and `F1` toggles the same screen. Its
heading holds both marks in one composition — the product mark, the product name with a small version chip and
the tagline on the left; a *licensed to* block with the company logo on the right. Three tabs follow, and
ⓘ/`F1` always land on the first one:

- **About** — a short description of what the app does, then the application version, the engine version (or
  `not started`) and the copyright, and a *What's new in {version}* button that opens the release notes.
- **Environment** — two groups: *Runtime* (engine PID, .NET runtime, OS) and *Paths* (the resolved
  `MSBuild.exe` and its version, the repository root, and the state and log paths).
- **Shortcuts** — the table above in three groups, *Global*, *Build* and *Application*, rendered from the same
  source the app binds its keys from, so a rebound key can never drift from what the screen claims.

*Copy diagnostics* in the footer puts the product and version, the engine version and every Environment row on
the clipboard as one aligned block, to paste into a support request.

`MSBuild.exe` is located through `vswhere`, which costs a child process, so it resolves the first time the
Environment tab is opened rather than when the screen appears.

### What's new

A dedicated 720 × 600 px dialog, opened from its own title-bar button — a four-point star between the gear and
`i` — or from About's *What's new in {version}* button; it has no keyboard shortcut. It carries no identity block and no
tabs; the header shows the installed version in a small mono chip, and the body is release notes only, newest
version first. Each version keeps its number, date and — on the running version — a neutral `INSTALLED` chip
in a left column that stays in view while its notes scroll; the notes sit on the right, grouped into Added /
Changed / Fixed / Performance / Removed. The three newest versions are open and the rest fold under an
*Earlier versions* button. There is no pop-up on launch. The notes are [CHANGELOG.md](CHANGELOG.md), built into
the app.

When the version you last opened this dialog on differs from the running one — including on a fresh install,
where nothing has been opened yet — a small amber dot sits on the star button and its tooltip names the new
version. Opening the dialog clears the dot for good; it does not return until the next version ships. About's
`i` button no longer takes part in this: its tooltip is fixed, and `F1` always opens on the About tab.

Esc closes whichever dialog is on top first — What's new, then About, then Settings — so a lower one's state
survives a stray keypress.

### Update

An installed copy keeps itself up to date. Five seconds after it starts, and every four hours after that, it
asks the project's GitHub Releases whether there is a newer version — silently: no progress, no balloon, and a
check or download that fails (offline, rate-limited, a package that does not verify) simply waits for the next
round. A newer version downloads in the background, and only once it is on disk does the title bar's command
group start with an *Update {version}* pill; the icons to its right never move for it.

Clicking the pill opens a card: the installed and incoming versions with the download size, the new version's
highlights from its release notes grouped like What's new — at most five, with a `+N more in What's new after
restart` line when there are more — and *Later* / *Restart to update*. While a build, a Sync or a maintenance task
is running, *Restart to update* is disabled and the line above it says what it is waiting for — `Esc stops it`
for a build, `Esc stops it now` once a stop is already draining (the next Esc is the hard stop), and no key once the
hard stop has gone; it comes back on its own when the work ends. *Later*, a second click on the pill, a click elsewhere,
Esc or opening a dialog closes the card; the pill stays.

*Restart to update* closes the card and covers the whole window, title bar included, with the restart screen:
the product mark, `Updating Build Orchestrator`, the version change, a progress bar and
`Closing Build Orchestrator…`. When the bar fills, the app exits the way tray → *Exit* does; the installer then
replaces the files without a window of its own, and the new version opens — with the dot on What's new's star.
While the restart screen shows, keys and the global hotkeys do nothing.

Not restarting is fine too: after *Later*, or with the pill simply left alone, the downloaded version installs
silently when the app exits, and the next start is the new version. A copy that is not installed — run from
`bin\` or from a publish folder — never checks and never shows the pill.

Two environment variables exist for development and testing: `BO_UPDATE_SOURCE` points the check at a local
folder of packages or another URL instead of GitHub Releases (ARCHITECTURE.md §17.6 walks through a local
rehearsal), and `BO_UPDATE_PRERELEASE=1` also offers pre-releases.

### State on disk

Everything the app persists lives under `%LOCALAPPDATA%\BuildOrchestrator\`: `logs\run-<timestamp>\` (per-run
and per-project logs, removed at the first engine start more than three days after their run — the latest run's
folder always stays), `build-state.json`, `evaluation-cache.json`, `source-hash-cache.json`, `ui-state.json`
and, only while a build is running, `run-inflight.json` — the projects being compiled right now. If the engine
dies mid-build (a crash, Task Manager, a closed session), the next start finds that file, marks those projects
as not built and prints `previous run was interrupted; N projects will rebuild`, so a half-written output is
never taken for a finished one. *Start with Windows*, when on, writes one value to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — no admin rights, no HKLM, no service — and turning it on
also clears Task Manager's *disabled* mark for that value, if it has one.

The installation itself lives apart, in `%LOCALAPPDATA%\BuildOrchestrator.App\`: an update replaces only the
program there, and uninstalling removes that folder and the *Start with Windows* value but never touches
`%LOCALAPPDATA%\BuildOrchestrator\`.

Older versions kept a pool of git worktrees under `worktrees\`. Nothing uses it any more; if the folder is
still there, the console says so once per session. Delete it to reclaim the space, then run
`git worktree prune` in the repository.

## Performance modes

One chip in the UI cycles three fixed profiles (default: Balanced):

| Mode | Parallelism | Process priority | Inner-job hard CPU cap |
|---|---|---|---|
| Full | 6 | Normal | none |
| Balanced | 4 | BelowNormal | 70% |
| Light | 2 | Idle | 40% |

Switching **while a run is in flight** writes a console note and sends the new profile to the engine; switching
while idle changes only the chip, because the profile travels with the next run anyway. The note is a timestamped
narrative line — `14:02:31 parallelism: 4 · cpu cap 70%` — whose body is exactly `parallelism: <n> · cpu cap <p>%`
(`cpu cap off` for Full).

If the whole machine freezes during a build, lower the profile. The limit is usually memory rather than CPU:
every parallel project runs its own compiler, and with an IDE and browsers already open, Full can use up the
physical memory and make Windows page other applications (ARCHITECTURE.md §11.1).

Three qualifications worth knowing:

- **The cap only limits the build.** It is written to the inner Job Object, which holds `MSBuild.exe` children
  and nothing else. `git` and `vswhere` are never assigned to it, and the App is in no job at all — so Sync,
  branch listing and the interface itself run at full speed even in Light mode.
- **Light's 40% is not an absolute ceiling.** While a post-build copy is stuck on contention, the cap and
  priority are deliberately raised to the Balanced floor, and once a graceful stop starts draining the cap is
  removed for the rest of the run.
- **Parallelism is fixed at the start of a run.** Switching mid-run changes only the CPU cap and the priority;
  the new parallelism takes effect on the next run.

The reasoning behind all three is in [`ARCHITECTURE.md` §11](ARCHITECTURE.md#11-resource-governance).

## Known limits (v1)

- **One repository's history at a time.** External projects join the same graph — real dependency edges,
  the same incremental decision — but the branch, the HEAD watcher and the `N behind` count describe the
  repository root alone. Switching branches does not move an external working copy.
- **One tree, no build-output isolation between branches.** Every build compiles the working tree, and no
  output path is changed — neither `OutDir` nor `obj` — for Visual Studio parity, so builds of different
  branches write their output to the same place. To build another branch, check it out.
- **Automatic Syncs do not fetch.** Their `N behind` count is against the last remote state you fetched; press
  *Sync* or pull to refresh it.
- **A brand-new repository is not watched until its first commit.** The HEAD watcher needs git's reflog, which
  appears with the first commit; until then, switching back to the window keeps the list current.
- **`UseSharedCompilation=false` and `nodeReuse:false` are kept.** A compiler server with a private pipe could
  live inside the job, but measured on a real repository it saves about a tenth of a run while holding
  gigabytes of memory, so the flags stay off; a server *outside* the job would also bring back the risk of a
  torn DLL when a run is stopped.
- **Filling a viewport of project rows costs what it costs.** The list is virtualized, so the work is bounded
  by the visible window rather than by the size of the repository — but that window is still built from
  scratch whenever the entries are replaced, which a topology change or a filter change both do.
- **A large graph costs what it costs to open.** Every node is drawn — nothing is culled and no threshold
  changes the panel's behaviour — so a very large workspace pays for its whole graph once, at Sync (a few
  hundred milliseconds at a thousand projects). Past a few hundred projects the nodes reach their minimum
  size and the graph reads as a shape rather than as individual squares.
- **The IPC has no field-level schema validation.** A malformed *JSON* line is recoverable — the Supervisor
  answers `error(badCommand)` and keeps going — but a **framing** error (over-long or truncated line) is treated
  as unrecoverable: it writes `error(framing)` and exits with code 2, and the App reports the engine as dead.
  A structurally valid command with a missing field is not rejected at all; it surfaces as a
  `planFailed`/`runFailed` at the point of use.
- **Symlinks/junctions are not followed or detected** during the workspace scan, and a `.csproj` may reference
  files outside the repository root. Both are accepted risks — the repository is trusted by definition.
- **Graph nodes are not keyboard-navigable.** A screen reader can read and invoke them — each node is named
  with its project and status — but there is no keyboard route into the canvas.

The measured numbers behind these are in [`ARCHITECTURE.md` §20](ARCHITECTURE.md#20-known-limits).

## Documentation

Three files carry everything: this one, [`ARCHITECTURE.md`](ARCHITECTURE.md) — every architectural, technical
and design decision the implementation rests on, plus a code map of which file owns which behaviour — and
[`CLAUDE.md`](CLAUDE.md), the working conventions for this repository.

## Licence

The project is licensed under the **MIT License** — see [`LICENSE`](LICENSE).

The one third-party licence *text* that is included and redistributed is the **Geist** and **Geist Mono**
fonts, licensed under the **SIL Open Font License 1.1**: `src/BuildOrchestrator.App/Assets/GEIST-LICENSE.txt`,
which is copied into the publish output as `Assets\GEIST-LICENSE.txt`.
