# Build Orchestrator

A Windows desktop application that builds a multi-project .NET solution incrementally. It scans a repository
for projects, derives the dependency graph, decides which projects actually changed (from git, never from
output timestamps), and builds only those — in parallel, under a supervisor process it owns.

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
stopped (in-flight projects are allowed to finish their post-build copy) or retried for just the failed
projects and their dependents. Building a branch other than the checked-out one
happens in a detached git worktree from a pool — your working tree is never checked out, reset or switched.

## Architecture

| Project | Target | Responsibility |
|---|---|---|
| `src/BuildOrchestrator.App` | net10.0-windows (WPF) | UI, MVVM, DI, tray icon, single instance. Owns the **outer Job Object** and spawns the Supervisor. |
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
  compiler server survives outside the job.
- The shared `OutDir` is never touched: no `-p:OutDir` / `-p:OutputPath` is ever passed, so output lands
  exactly where Visual Studio would put it. Only `BaseIntermediateOutputPath` (`obj`) is isolated, and only in
  worktree mode, keyed by the project's full path.
- "Did it change?" is answered only from source signals: the committed blob SHAs of the tree
  (`git ls-tree -r HEAD`), the current commit (`git rev-parse HEAD`) and the dirty list
  (`git status --porcelain`). No DLL or `bin` timestamp is ever read.

## Requirements

- **Windows.** WPF, Job Objects and the Win32 process control are not portable.
- **.NET 10 SDK** — to build and run this repository.
- **Visual Studio 2022 or Build Tools** with the `Microsoft.Component.MSBuild` component. The engine resolves
  `MSBuild.exe` at run time through
  `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe`; without it, builds fail with a resolve
  error (the Supervisor itself still starts). "Open in Visual Studio" additionally needs a full VS IDE install.
- **`git` on `PATH`** — the engine invokes `git` by name.

## Build, test, run

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test  tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
dotnet run   --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj
```

Close any running instance of the app before building — a running Supervisor keeps its own binaries locked.
The test suite is expected to be fully green. The filter above excludes the three acceptance tests, which
build a real large repository (~2 min) and are run separately with `--filter "Category=Acceptance"`.

## Publish

Framework-dependent, folder-based publish:

```powershell
dotnet publish src\BuildOrchestrator.App\BuildOrchestrator.App.csproj `
  -c Release -r win-x64 --self-contained false -o <output-folder>
```

**The `supervisor\` subfolder next to the published `.exe` is mandatory.** It is not an optional extra: it
*is* the build engine. The App resolves `<app folder>\supervisor\BuildOrchestrator.Supervisor.exe` at startup;
if it is missing, no build can run and the app says so instead of failing silently. The publish target adds
that folder to the publish list, and the build fails outright rather than producing an engine-less package.
`Assets\GEIST-LICENSE.txt` also ships with the output.

Not supported:

- **`PublishSingleFile`** — rejected by an MSBuild target with an explicit error. `AppContext.BaseDirectory`
  would point at the extraction directory and the `supervisor\` subfolder cannot go into the bundle.
- **Self-contained publish** is not verified. The only publish mode that is exercised end to end is
  `-c Release -r win-x64 --self-contained false`.

To verify a publish output end to end:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\verify-publish.ps1
```

It first refuses to measure anything if an instance is already running (the app is single-instance), then
publishes to a temp folder and checks the whole chain: publish exit code, layout, an NDJSON round trip against
the published Supervisor binary, a full Sync + Build driven through it against a throwaway workspace (proving
the published binary really compiles and writes the DLL), launching the published `.exe` and confirming via
WMI that the Supervisor child was spawned from that same folder, reading the console boot line and the ribbon
state out of the live window through UI Automation, and finally killing only the App and proving the Supervisor
dies *by itself* through the job cascade. Exit code `0` = pass, `1` = fail, `2` = precondition not met (close
the running instance first — tray icon → Exit).

## Using it

1. **Pick a repository** — before a repository is selected, the *Choose Folder* button applies the folder
   immediately: project states reset and a Sync starts. Repository *Change…* inside Settings only stages the
   folder in the dialog; *Save* is what applies it, resetting project states and starting a Sync.
2. **Sync** — scans, builds the graph, and marks which projects would build. Nothing is compiled here. Until it
   has run, *Build*, *Rebuild* and *Retry failed* are disabled: a run before the first Sync would compile for
   real while the list and the graph stayed empty.
3. **Branch / worktree** — picking a branch other than the checked-out one forces worktree mode: the build runs
   in a detached worktree from the pool. Project rows reset to pending, the ribbon goes back to
   *"▸ Waiting for Sync — project states appear after Sync"* and the console gets a
   `Branch changed: <branch> — Sync required` line. Worktrees are created with `--detach` and live under
   `%LOCALAPPDATA%\BuildOrchestrator\worktrees\`.
4. **Build / Rebuild / Retry failed** — from the split button and its menu:
   - *Build* — only changed projects.
   - *Rebuild* — all projects, cached state ignored.
   - *Retry failed* — appears when there are failures; rebuilds them and their dependents.

   Nothing compiles the moment you click. The engine first works out what to build — preparing the worktree if
   one is in use, scanning, building the graph, then computing what changed — which on a large repository takes
   seconds. The ribbon reads *"▸ Starting — resolving what to build"* and the console lists each step as it
   completes; the button becomes *Stop* right away, so a stop pressed during this window is honoured and no
   project is compiled at all.
5. **Stop** — nothing new is dispatched and the in-flight `MSBuild.exe` children finish, including their
   post-build copy, so no half-written DLL is left behind and their work is kept. Until they do, the button
   reads *Stopping…* and is disabled and the ribbon reports how many are still finishing. There is no
   *Continue*: press *Build* again and the run starts from the top, skipping everything that already
   succeeded.

If the engine ever stops answering — no event at all while a run start or a stop is still pending — the ribbon
says so in amber and offers *Restart engine*. Nothing unlocks by itself, because a drain can legitimately take
minutes; the action is there for the case where waiting is no longer the answer. Restarting kills the engine
and every `MSBuild.exe` under it, then brings a fresh engine up.

### Keyboard shortcuts

| Key | Action |
|---|---|
| `F5` | Build — or Stop while a run is in flight |
| `Ctrl+F5` / `Shift+F5` | Rebuild |
| `Ctrl+F` | Focus the project filter |
| `Esc` | Close the topmost open layer: dialog → popover/menu → selection |
| `Alt+B` | Global hotkey: bring the window back from the tray |

The global hotkey defaults to `Alt+B` and is read from `ui-state.json`; there is no UI for changing it yet
(Settings only has LAYERS and REPOSITORY). If it cannot be registered — another application already owns that
combination — it is silently disabled; the tray icon still restores the window.

Disabled commands stay disabled when triggered by a shortcut — the key never bypasses the button's state.

### State on disk

Everything the app persists lives under `%LOCALAPPDATA%\BuildOrchestrator\`: `logs\run-<timestamp>\` (per-run
and per-project logs), `build-state.json`, `evaluation-cache.json`, `ui-state.json` and the `worktrees\` pool
(capped at 20 GiB with LRU pruning). Autostart, when enabled, writes to
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — no admin rights, no HKLM, no service.

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

- **One repository at a time.**
- **No build-output isolation for worktrees.** Only `obj` is isolated (per project id, in worktree mode); the
  shared `OutDir` is intentionally left alone for Visual Studio parity, so builds of different branches write
  their output to the same place.
- **`UseSharedCompilation=false` and `nodeReuse:false` are kept**, and they cost real time — roughly 2.9× the
  flags-on build, essentially all of it from shared compilation. They stay because with a compiler server the
  emit happens outside the job, which brings back the risk of a torn DLL when a run is stopped.
- **Filling a viewport of project rows costs what it costs.** The list is virtualized, so the work is bounded
  by the visible window rather than by the size of the repository — but that window is still built from
  scratch whenever the entries are replaced, which a topology change or a filter change both do.
- **The graph view is full detail only up to 150 nodes.** Above that, off-screen nodes and edges are culled and
  labels drop out by level of detail.
- **The IPC has no field-level schema validation.** A malformed *JSON* line is recoverable — the Supervisor
  answers `error(badCommand)` and keeps going — but a **framing** error (over-long or truncated line) is treated
  as unrecoverable: it writes `error(framing)` and exits with code 2, and the App reports the engine as dead.
  A structurally valid command with a missing field is not rejected at all; it surfaces as a
  `planFailed`/`runFailed` at the point of use.
- **Symlinks/junctions are not followed or detected** during the workspace scan, and a `.csproj` may reference
  files outside the repository root. Both are accepted risks — the repository is trusted by definition.
- **Graph nodes are not keyboard-navigable** and carry no automation name.

The measured numbers behind these are in [`ARCHITECTURE.md` §20](ARCHITECTURE.md#20-known-limits).

## Documentation

Three files carry everything: this one, [`ARCHITECTURE.md`](ARCHITECTURE.md) — every architectural, technical
and design decision the implementation rests on, plus a code map of which file owns which behaviour — and
[`CLAUDE.md`](CLAUDE.md), the working conventions for this repository.

## Licence

There is **no licence file for this project** — the repository ships no `LICENSE`, so no licence is granted
here by default.

The one third-party licence that is included and redistributed is the **Geist** and **Geist Mono** fonts,
licensed under the **SIL Open Font License 1.1**: `src/BuildOrchestrator.App/Assets/GEIST-LICENSE.txt`, which
is copied into the publish output as `Assets\GEIST-LICENSE.txt`.
