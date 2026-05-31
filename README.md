# Build Orchestrator

A Windows desktop application that builds **hundreds of interdependent C#/WPF projects** —
spread across many solutions — from a single window: in dependency order, in parallel, and
(in incremental mode) building **only what changed**.

Core flow: **Sync → pick a branch → Build/Rebuild → live output.**

> The UI is Windows-only (WPF, .NET 8). The engine libraries (`Core`, `Worker`) are plain
> .NET 8 and are unit-tested cross-platform.

## Architecture

The solution is split into four projects plus tests, so the build workload never touches the UI
thread and orphaned compiler processes are impossible (see *Process control* below).

| Project | Target | Role |
| --- | --- | --- |
| `BuildOrchestrator.Contracts` | `net8.0` | Shared types and the JSON message protocol (Section 8). |
| `BuildOrchestrator.Core` | `net8.0` | Engine: workspace scan, dependency graph, topological order, cycle detection, incremental planner, git, JSON storage. |
| `BuildOrchestrator.Worker` | `net8.0` | Out-of-process build host: MSBuild engine, parallel scheduler, **Windows Job Object** process control. |
| `BuildOrchestrator.App` | `net8.0-windows` | WPF/MVVM single-window UI, tray icon, single-instance, autostart. |

The **App** launches the **Worker** as a child process and talks to it over **stdio** using
newline-delimited JSON (`MessageKind` = `Command`/`Event`). If the Worker crashes, the UI stays up
and restarts it.

```
┌────────────┐  commands (stdin JSON)   ┌──────────────┐  MSBuild   ┌───────────────┐
│  App (WPF) │ ───────────────────────▶ │   Worker     │ ─────────▶ │  child procs  │
│  MVVM UI   │ ◀─────────────────────── │  (.NET 8)    │            │  (Job Object) │
└────────────┘   events  (stdout JSON)  └──────────────┘            └───────────────┘
```

## Key design decisions

- **Shared OutDir is preserved (Section 4).** Final DLLs always land in the common Visual Studio
  `OutDir`; the container picks them up automatically. Because the output is shared, DLL timestamps
  are *not* trustworthy for "did it change?" — that decision is based **only** on the source signal
  (commit + local diff). Only intermediate output (`obj` / `BaseIntermediateOutputPath`) is isolated
  per worktree for incremental correctness.
- **Process control is mandatory (Section 6.1).** The Worker and every process it spawns
  (MSBuild nodes, `VBCSCompiler.exe`, pre/post-build events) are bound to a single Windows **Job
  Object** created with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`. If the app closes, crashes, or is
  killed, Windows tears down the whole process tree automatically. Builds also run with
  `-p:UseSharedCompilation=false` so the Roslyn server never lingers, and a PID sweep acts as a
  second safety net. **Stop** is graceful first (`BuildManager.CancelAllSubmissions()`), then
  escalates to closing the Job Object after a timeout.
- **Branches build in an isolated worktree pool** under
  `%LOCALAPPDATA%\BuildOrchestrator\worktrees\<branch>\`, so the user's working tree in Visual
  Studio is never disturbed.
- **The dependency graph is cached** (`dependency-graph.json`); build order is read from cache and
  only recomputed on an explicit Sync.

## Incremental build

A project is rebuilt when (Section 6):

1. the branch's current commit differs from the last successfully-built commit, **or**
2. the working tree has local changes affecting that project, **or**
3. it was never built successfully.

Otherwise it is **Skipped**. In **Safe** mode (default) the transitive dependents of dirty projects
are also rebuilt; in **Fast** mode only the dirty projects are.

## Data locations

Everything is stored under `%LOCALAPPDATA%\BuildOrchestrator\`:

- `config.json` — user configuration
- `dependency-graph.json` — cached graph / topological order
- `build-state.json` — per `(project, branch)` last-built commit + result
- `worktrees/` — per-branch worktree pool

## Build & test

```bash
# Engine + tests (cross-platform)
dotnet test tests/BuildOrchestrator.Core.Tests/BuildOrchestrator.Core.Tests.csproj
dotnet test tests/BuildOrchestrator.Worker.Tests/BuildOrchestrator.Worker.Tests.csproj

# Full solution (on a non-Windows host, add -p:EnableWindowsTargeting=true to compile the WPF app)
dotnet build BuildOrchestrator.sln
```

The WPF app must be **run** on Windows.

## Configuration (Section 3)

Root directory, Debug/Release, performance mode (Full Power / Balanced / Light), branch mode
(worktree / in-place), log level (Errors-only / Full), dependent mode (Safe / Fast), cache location,
reduced motion, and opt-in autostart.

## Phase map (Section 9)

- **Phase 0** — WPF shell, single window, tray, autostart, config screen.
- **Phase 1** — Virtualized card UI, transform/opacity-only animations, filters, console, scroll rules.
- **Phase 2** — Sync engine: scan, `ProjectReference` graph, topological order, cache, cycle detection.
- **Phase 3** — Rebuild + parallel MSBuild + live log + Stop + **Job Object** process control.
- **Phase 4** — Incremental build: commit/diff/status analysis, worktree, obj isolation, Safe/Fast.
- **Phase 5** — Debug/Release, performance modes, packaging.
