# Build Orchestrator — Architecture and Technical Reference

This document describes what the application is made of and why it is made that way: the process topology, the
IPC contract, the incremental build decision, the build engine, the git surface, the UI architecture and the
design system. It is written to be read by someone who has never opened the planning documents.

**Scope.** Everything here is a statement about the code as it stands, and it is written to be sufficient on
its own — no historical document needs to be consulted to understand or change this system. Where a decision
was taken deliberately against an obvious alternative, the rationale is given in one or two sentences, not the
history of how it was reached. Chronological records (iteration plans, review outputs, decision logs) live
under `.claude/outputs/` and are not repeated here.

**Reading order.** [`README.md`](README.md) is the entry point — what the tool does and how to run it. This
document is the technical reference behind it, including the security boundary and threat model (§21) and a
code map (§22) that says which file owns which behaviour.

---

## 1. Product definition

### 1.1 The problem

A single git repository holds hundreds of interdependent .NET projects — in the reference workload, 177
`.csproj` under 44 `.sln`, the overwhelming majority of them legacy .NET Framework (4.6/4.8) with a handful of
SDK-style projects, wired together by ~1850 `HintPath` references rather than by `ProjectReference`. Opening
the right solution in Visual Studio and pressing Build is slow, and building "everything" is slower still,
because neither the solution files nor MSBuild's own incrementality reflect the true dependency structure of
the repository.

The orchestrator answers one question well: **given the current source, which projects actually need to be
rebuilt, in what order, and how do we run that safely.**

### 1.2 What the tool guarantees

| Guarantee | Mechanism |
|---|---|
| The user's working tree is never checked out, switched or reset | Git surface is read-only; building another branch happens in a detached worktree (§10) |
| Output lands exactly where Visual Studio would put it | `OutDir`/`OutputPath` are never passed to MSBuild (§9.4) |
| "Changed?" is decided from source, never from build output | Signature is computed from git blobs + dirty files; no DLL/`bin` timestamp is ever read (§7.1) |
| Killing the app kills the whole build tree | Nested job objects with `KILL_ON_JOB_CLOSE`, no breakaway (§4) |
| Stopping a run never leaves a torn DLL | Graceful stop drains at project boundaries; no compiler server lives outside the job (§4.5) |
| The build order is deterministic | Order-preserving ready-set scheduler; no hashing, no randomness (§8.2) |

### 1.3 Non-goals (v1)

Multi-repo. Headless/CLI operation. Light theme. Command palette. Onboarding flow. MSIX packaging.
`packages.config` migration. Build-output isolation per branch. Graph editing. Attaching to an already-running
Visual Studio instance via ROT/DTE (the "Open in Visual Studio" action resolves `devenv.exe` through `vswhere`
— once per session, off the UI thread, since the query can take seconds and its timeout is 30 — and opens the
solution fresh).

---

## 2. Technology stack

| Concern | Choice | Note |
|---|---|---|
| Runtime | .NET 10 | All five projects |
| UI framework | WPF | Validated against WebView2-hybrid, Avalonia and WinUI 3 before commitment (§19) |
| MVVM | CommunityToolkit.Mvvm 8.4 | Source-generated observable properties and relay commands |
| DI | Microsoft.Extensions.DependencyInjection 10 | Composition root in `App.OnStartup` |
| Console host | AvalonEdit 6.3 | The only WPF control that gives text selection + per-line colouring + MSBuild-verbose volume together |
| Tray icon | H.NotifyIcon.Wpf 2.4 | Notify icon + OS balloon |
| Test framework | xUnit 2.9 + StaFact + SkippableFact | STA tests are required for WPF realization |
| Build engine | `MSBuild.exe`, located via `vswhere` | **Not** `dotnet build` — the target repository is predominantly legacy .NET Framework |
| Process control | Win32 job objects, `CreateProcessW`, `RegisterHotKey`, `WindowChrome`, DWM | P/Invoke in `Core/ProcessControl` and `App/Shell` |
| Fonts | Geist / Geist Mono, static OTF, embedded | SIL OFL 1.1; variable fonts are not usable by WPF |

---

## 3. Solution layout

Solution file: `BuildOrchestrator.slnx` at the repository root.

### 3.1 Projects and responsibilities

| Project | Target | Responsibility |
|---|---|---|
| `src/BuildOrchestrator.Contracts` | `net10.0` | The App↔Supervisor contract: command and event records, domain DTOs, polymorphic JSON options, NDJSON framing. No logic. |
| `src/BuildOrchestrator.Core` | `net10.0` | All decision-making, pure and testable: discovery, evaluation cache, dependency graph, layers, signature and incremental planning, scheduler, git service, worktree pool, MSBuild argument/invocation contract, job-object primitives, run logs, state persistence. |
| `src/BuildOrchestrator.Supervisor` | `net10.0-windows` | The engine process. Owns the inner job object, runs the plan Core produced, shells out one `MSBuild.exe` per project, writes per-run logs, serves the IPC. Executes; does not plan. |
| `src/BuildOrchestrator.App` | `net10.0-windows` (WPF) | The interface. MVVM, DI, window shell, tray, single instance, global hotkey, all rendering and motion. Owns the outer job object and spawns the Supervisor. |
| `tests/BuildOrchestrator.Tests` | `net10.0-windows` (`UseWPF`) | One suite for everything: Core unit tests, process-control tests, IPC tests, WPF realization/STA tests, source guards, integration and acceptance tests. |

### 3.2 Reference rules

- **The App never references the Supervisor assembly.** It takes a `ProjectReference` with
  `ReferenceOutputAssembly="false" Private="false"` purely for build ordering, then copies the Supervisor's
  output into a `supervisor\` subfolder next to itself and starts it as a process. All communication is IPC.
- `Core` may reference `Contracts`; it may not reference `App` or `Supervisor`.
- Business logic does not leak into `App` or `Supervisor`. If a rule can be expressed without a window handle
  or a process, it belongs in `Core` and gets a unit test there.
- `Core` targets plain `net10.0` deliberately, including the job-object code: the perf table carries a
  platform-neutral priority enum and the Win32 translation happens in exactly one place.

### 3.3 Shared build properties

`Directory.Build.props` holds `Nullable`, `ImplicitUsings`, `LangVersion` and the distribution identity
(`Version`, `InformationalVersion`, `Product`, `Company`). The informational version carries a delivery tag so
that the value observed at runtime proves the property file is actually wired: the Supervisor reads it from its
own assembly and reports it in `engineReady`, and the App prints it in the console boot line.

The supervisor folder name is declared **once**, as the `SupervisorFolderName` MSBuild property in the App's
`.csproj`, and travels to runtime as an `AssemblyMetadata` attribute that `Services/SupervisorLayout` reads
back. It is never spelled again in C#.

---

## 4. Process topology

### 4.1 The two processes

```
BuildOrchestrator.App.exe   (WPF — owns the outer job, is NOT a member of it)
  │  stdio, newline-delimited JSON
  ▼
[ outer job object — KILL_ON_JOB_CLOSE ]
  BuildOrchestrator.Supervisor.exe
    ├── git.exe / vswhere.exe          (plain child processes — outer job only)
    └── [ inner job object — KILL_ON_JOB_CLOSE + CPU rate cap + priority class ]
          MSBuild.exe (one per project) + whatever its targets spawn
```

The split exists for one reason: **the UI thread must never be the thing that holds the build together.** WPF
animations tick on the UI thread (there is no compositor), so any long synchronous work in the App would stall
both the interface and the engine if they shared a process. Separating them makes the interface's frame budget
independent of MSBuild's output volume.

### 4.2 Nested job objects

The App creates the outer job and assigns the Supervisor to it. The Supervisor creates the inner job and
assigns every `MSBuild.exe` to it; because job membership is inherited, those children are members of both.
Neither job is created with `JOB_OBJECT_LIMIT_BREAKAWAY_OK`, so a child asking for
`CREATE_BREAKAWAY_FROM_JOB` receives `ERROR_ACCESS_DENIED`.

The App is deliberately **not** a member of its own outer job. Two consequences follow, both wanted: the CPU
cap written to the inner job can never throttle the interface, and processes the App starts on the user's
behalf (`explorer.exe`, `devenv.exe`) survive the app closing.

There is no managed parent-watcher and no PID heuristic. The guarantee is the OS handle semantics: when the
last handle on a job closes, `KILL_ON_JOB_CLOSE` terminates its members.

### 4.3 Launch protocol

Every job-managed child goes through the same sequence, with no window in which it could run unmanaged:

1. Create the pipes (only on the redirected path).
2. `CreateProcessW` with `CREATE_SUSPENDED`.
3. `AssignProcessToJobObject` while still suspended. If the assignment fails, the child is terminated
   immediately.
4. `ResumeThread`.

Handle inheritance is restricted with `PROC_THREAD_ATTRIBUTE_HANDLE_LIST` to exactly the three pipe ends of
that launch. Without this, parallel redirected launches leak sibling pipe ends into each other and EOF never
arrives — the symptom is a build that hangs forever rather than one that fails.

**Every redirected pipe must have a consumer.** A redirected child gets three pipes, and the parent owes each
of them an active end: it writes stdin, and it must *read* stdout and stderr for as long as the child lives. A
pipe nobody reads fills its buffer — a few kilobytes — and then the next write from the child blocks forever,
inside whatever the child happened to be doing. The App drains the Supervisor's stderr for exactly this
reason and discards the bytes: the engine's diagnostics already reach disk through `decision.log`, and
anything the user must see travels as an IPC event. The drain exists to keep the pipe moving, not to collect
anything.

This is not theoretical. The engine writes a diagnostic line per stale-obj project at the start of planning; a
177-project workspace produces tens of kilobytes. With nothing reading stderr, planning froze partway through
that loop, so `runStarted` never arrived and the App sat in its mid-run lock. Worse, the *stop* that followed
could not complete either: the coordinator takes ownership of a stop while a run is active and owes the
`runStopped` acknowledgement to the run task's `finally`, which a frozen planner never reaches — so the phase
stayed on `stopping` indefinitely. One unread pipe, both symptoms.

### 4.4 Termination matrix

| Actor | Target | Mechanism |
|---|---|---|
| App | everything | Disposing the outer job closes the last handle → cascade |
| App | Supervisor only | `Process.Kill(entireProcessTree: true)` |
| Supervisor | the whole MSBuild tree | `TerminateJobObject(inner)` — the hard stop |
| Supervisor | one project | `Kill(entireProcessTree: true)` on that `MSBuild.exe` |
| Supervisor | App | not possible — the App's death reaches it as stdin EOF and it exits cleanly |

If the App crashes or is killed from Task Manager, the OS closes its handles and the cascade runs anyway. The
measured bound is under two seconds with no orphan.

### 4.5 Stop semantics

**Graceful stop** is what the Stop button and `F5` request. Nothing new is dispatched; the in-flight
`MSBuild.exe` children finish, *including their post-build copy events*. This is also why the
shared-compilation flags stay off (§9.2): with a compiler server the emit happens in a long-lived process
outside the job, where a stop could catch a DLL mid-write.

**Hard stop** terminates the inner job outright. It exists in the contract and in the engine, but the App
never sends it.

The choice between them is not about the wait — it is about how much work a stop throws away. A drained
project *succeeds*, so its `BuildState` is persisted and the next Build skips it as up to date. A terminated
project is reported `failed("stopped")`, which invalidates its stored state, so the next Build compiles it
again from scratch — up to `parallelism` half-finished compiles discarded, and a row the user's own Stop
turned red. Draining costs the remaining time of the slowest in-flight project and banks the work; terminating
returns the machine sooner and bills the difference to the next Build. Since a stopped run is resumed by
pressing *Build* — there is no *Continue* — banking the work is the cheaper trade.

`runStopped` and `runCompleted` each fire exactly once, and the elapsed clock is preserved.

Because a drain can take as long as the slowest in-flight project, the App has to show that the click landed.
Requesting a stop moves the phase to `stopping` **before the command is even sent** — waiting on a slow engine
would leave the button reading *Stop* and invite a second click. The button stays visible but reads
*Stopping…* and goes disabled, the ribbon drops its ETA and reports how many projects are still finishing, and
a line goes into the run document. The mid-run lock is deliberately *not* released: the engine is still
working, so branch, worktree and configuration stay locked and the Build split-button does not come back.

Leaving `stopping` cannot deadlock, because `runStopped` settles it unconditionally: phase `stopped`, run
state released. The coordinator only writes that event once every in-flight result has been reported, so by
the time the App sees it nothing is running — there is no ordering assumption left to violate. A trailing
`runCompleted` writes the same phase. A run-ending error and an engine death settle it too, and if the command
cannot even be sent the phase is put back.

Once a drain begins the CPU cap is removed for the rest of that run, and the priority class can no longer be
lowered past the Balanced floor. The "no torn DLL" guarantee is not negotiated against a resource setting.

### 4.6 Engine failure and restart

The App watches the Supervisor process. If the engine dies, the sticky ribbon enters a persistent error state
with a *Restart engine* action — there is no banner and no toast. A framing error on either side is treated as
unrecoverable: the Supervisor writes `error(framing)` and exits with code 2; the App kills the engine, bumps an
internal generation counter and raises exactly one `EngineExited` signal, so a late read from a dead generation
cannot produce a second one.

A process that dies announces itself. A process that *hangs* does not, and the App would otherwise wait for an
event that is never coming — the failure mode §4.3 describes, where a wedged planner leaves the phase on
`starting` and then on `stopping` forever. So the App also watches for silence, but only inside the two windows
where an answer is owed: a run has been requested and `runStarted` has not arrived, or a stop has been
requested and `runStopped` has not. Any event from the engine resets the clock; crossing the threshold with no
event at all raises an amber ribbon line and reveals the same *Restart engine* action.

A liveness ping would not have caught the measured case. The coordinator runs a run on a background task and
`startRun` returns immediately, so the command loop stayed responsive while the run task was frozen — a ping
would have been answered. The useful question is not whether the engine is alive but whether it is answering
what was asked.

Nothing unlocks on its own. A graceful drain can legitimately outlast any threshold, and releasing the lock
would let a second run start against an engine that is still compiling. The watchdog only offers the door;
walking through it is the user's decision. Restarting does release the run state, and it has to: the host
silences the old exit watcher *before* killing the child, so restarting a live-but-wedged engine raises no
`EngineExited` — the path that used to unlock everything never ran in exactly the case the action exists for.

---

## 5. IPC

### 5.1 Transport and framing

Newline-delimited JSON over stdio. Commands go App → stdin; events come Supervisor → stdout.

**The Supervisor's stdout carries NDJSON and nothing else.** The first thing `Program.Main` does is
`Console.SetOut(Console.Error)`, so a stray `Console.WriteLine` anywhere in the process lands on stderr instead
of corrupting the stream. All diagnostics use `Console.Error`. A single writer instance is shared and line
integrity is protected by its own semaphore.

Line limit is 1 MiB, enforced on both write and read. Log chunks are capped at 64 K characters, so legitimate
traffic sits far below the ceiling.

Serialization uses camelCase properties, camelCase enum names, `WhenWritingNull` omission and a `type`
discriminator whitelist on both hierarchies. An unknown discriminator does not deserialize.

### 5.2 Commands

`ping` · `shutdown` · `syncWorkspace` · `startRun` · `stopRun` · `getProjectLog` · `listBranches` ·
`listWorktrees` · `deleteWorktree` · `setPerfMode` · `debugSpawnChildren`.

The last one is a test hook for the breakaway probe. It remains part of the contract but is **rejected by
default** with `error(debugHooksDisabled)`; only a Supervisor started with `--debug-hooks` executes it, and the
App never passes that flag. In the shipped pair, ten commands execute.

`startRun` carries the run id, the mode, the repository root, the configuration, the parallelism, the branch,
the worktree intent, the dependent-propagation mode, the layer patterns and the perf mode name. Parallelism and
perf mode are separate fields on purpose: the Supervisor derives cap and priority from the perf name but never
recomputes the worker count, which the App has already resolved from the same table.

### 5.3 Events

Lifecycle: `engineReady` · `pong` · `error`.
Sync: `syncStarted` · `syncProgress` · `workspaceTopology` · `buildPreview` · `syncCompleted`.
Run: `planProgress` · `runStarted` · `projectStarted` · `projectLog` · `projectSucceeded` · `projectFailed` ·
`projectSkipped` · `runStopped` · `runCompleted`.
Queries: `branchList` · `worktreeList` · `projectLogChunk`.

`planProgress` is the only run event that precedes `runStarted`; it carries the planning steps of a fresh
segment (§8.6). It stays separate from `syncProgress` because the App treats that one as part of a Sync
transcript, and a run's planning window is not a Sync.

Three of these carry the whole model:

- **`workspaceTopology`** delivers every node in build order (cycle members included) with its dependencies,
  layer assignment, solution names and will-build tri-state, plus the strongly-connected components, the
  name→path map of solutions and the reverse-layer warnings. The graph panel, the layer grouping and
  "Open in Visual Studio" all read from this one event.
- **`buildPreview`** delivers the will-build set before the first per-project event of a run, so rows are
  populated before anything starts.
- **`syncCompleted`** carries the target SHA, the degrade flag and three counters that are *not* derivable
  from one another: directly-changed projects (Fast semantics, no cascade), the will-build set size (Safe
  semantics, dirty plus transitive dependents), and the up-to-date count.

`runStarted.cpuCapPercent` reports the cap that was **actually** written to the job, not the one that was
requested — a Win32 failure surfaces here as `null` plus a warning line, and does not fail the run.

### 5.4 Error handling

| Failure | Supervisor | App |
|---|---|---|
| Malformed JSON / unknown schema | `error(badCommand)`, loop continues | — |
| Unknown command type | `error(unknownCommand)` | — |
| Framing violation (over-long or truncated line) | `error(framing)`, exit 2 | engine killed, one `EngineExited` |
| Unresolvable perf mode | `error(badPerfMode)` | chip stays on the previous value |
| Bad root path / planning failure | `error(planFailed)` | run ends, ribbon shows the error |
| `startRun` while a run is active | `error(runInProgress)` | the *rejected* request drops its own pending flag; the live run is untouched |

The last row is a rejection, not a failure, and the distinction matters: the coordinator releases its run slot
only after every event has been written, so for a short window after `runCompleted` reaches the App the slot is
still held — and that is exactly when the buttons come back and a fast click lands. Treating the rejection as
run-ending would tear down the run that is still going; ignoring it entirely would leave the pending flag set
forever, locking the UI with nothing behind it to stop.

IPC records are positional and not `required`. A structurally valid command with a missing field binds to
`null` and surfaces at the point of use as `planFailed`/`runFailed`. No malformed command takes the Supervisor
down, but there is no field-level schema validation either.

### 5.5 Log delivery

Live output arrives as `projectLog` lines carrying a per-project line number. When a project is selected, the
App requests `getProjectLog` and receives `projectLogChunk` events from disk, each stamped with the last line
number that had been persisted at snapshot time. The App stitches the two streams on that number: live lines at
or below it are already in the chunk and are dropped. Scrolling to the top of a long log prepends the previous
chunk with scroll compensation, so the viewport does not jump.

---

## 6. Core — discovery and topology

### 6.1 Workspace scan

Recursive scan of the repository root for `*.csproj` and `*.sln`, skipping `.git`, `bin`, `obj`,
`node_modules` and `.vs`. WPF's temporary `_wpftmp.csproj` files are filtered out. Symlinks and junctions are
neither followed deliberately nor detected — an accepted risk, since the repository is trusted by definition.

### 6.2 csproj evaluation and cache

Project files are read as **raw XML**. MSBuild is never evaluated for discovery. The evaluator extracts the
assembly name, the target framework moniker, `Compile` items (including recursive `**` globs), raw `Reference`
`HintPath`s and `ProjectReference`s.

Results are cached in `evaluation-cache.json`, keyed by path with an mtime **and file-length** fingerprint. The
length term is not decoration: an edit that preserves the modification timestamp is otherwise invisible, and
the cache would serve a stale evaluation.

`file → project` mapping comes from the evaluated `Compile` items, never from a path prefix. A file that sits
inside a project's directory but is not compiled by it does not make it dirty.

### 6.3 Solution mapping

`.sln` files are parsed for their `Project(...)` lines to produce the project→solutions map. A project may
belong to zero or several solutions. This map has two consumers: "Open in Visual Studio" (which asks the user
to pick when there is more than one) and `packages.config` restore, which requires a solution directory (§9.3).

### 6.4 Producer map and HintPath classification

The primary edge signal is **HintPath basename → producing project**. A map from output DLL name to the project
that produces it is built from the evaluated assembly names; every raw `HintPath` is then looked up in it.
`ProjectReference` is the *secondary* signal — it produces edges too, deduplicated against the HintPath ones.

Not every `HintPath` resolves inside the repository, so each one is classified into one of four buckets:

| Class | Meaning |
|---|---|
| `Edge` | Resolved to a producing project in this repository — becomes a graph edge |
| `ExternalThirdParty` | Path rule identifies a package or an installed product |
| `ExternalOsysPlatform` | A sibling-repository platform binary — legitimate external input, not tracked (v1 is single-repo) |
| `Unclassified` | Neither — emitted as a warning line |

The reported health metric is `Edge / (Edge + Unclassified)`: external classes are legitimate inputs and are
excluded from the denominator, so the number measures what it claims to — how much of the *repository-internal*
reference surface the strategy resolves.

### 6.5 Edges, cycles, build order

Tarjan's algorithm finds strongly-connected components; Kahn's algorithm produces the topological order.
Iteration order is stabilized (`OrdinalIgnoreCase` on the project path) so the same repository always yields the
same plan. Cycle members remain in the plan, flagged `InCycle`, and are pre-skipped by the scheduler — otherwise
their dependents could never become ready and the run would deadlock.

### 6.6 Layers

Layers are optional and **empty by default** — with no patterns configured the list is a single flat list in
build order.

A layer definition is an ordered `(Order, Regex, Name)` triple. `Order` does double duty: it is the match
priority (lowest first, first match wins) and it is the assigned layer index. The regex is matched against the
project *name* (the assembly-name-derived short name), not the path, because that is how people think about
these patterns. Non-matching projects fall into `Other`, whose index is always one past the highest configured
order.

Assignment imposes a **hard phase barrier**: the plan is re-sorted by `(layerIndex, original build order)`,
using the stability of the sort to preserve topological order within a layer. This can legitimately place a
project before one of its own dependencies; that case is detected and reported as a warn-only reverse-layer
warning. Nothing is blocked or reordered on the basis of those warnings, so every algorithm that consumes the
plan must be order-independent.

User regexes are compiled with a 100 ms match timeout. A pattern that times out is treated as a non-match and
skipped for the remaining nodes, with a warning. An empty or whitespace pattern is made inert rather than
being allowed to match everything.

---

## 7. Core — the incremental decision

### 7.1 Signature

A project's signature is a SHA-256 over four terms:

1. the configuration string (`Debug`/`Release`),
2. the **per-project committed fingerprint** — a hash of the committed blob contents of exactly the files that
   affect this project at `HEAD`,
3. **only in in-place mode**, a hash of the working-tree changes to those files,
4. the signatures of its direct upstream producers.

Byte stability is a tested property: the same inputs always produce the same hex string, and the order of the
input lists never matters (they are sorted `OrdinalIgnoreCase` internally). Every variable-length component
(a path, a project id) is itself hashed before being concatenated, so a separator character inside a path
cannot make two different input sets collapse to the same pre-hash string.

Only these extensions participate in the working-tree term: `.cs`, `.xaml`, `.resx`, `.csproj`, `.props`,
`.targets`. A changed `.md` does not rebuild anything.

Two deliberate refinements:

- **The commit term is per-project, not the repository HEAD.** Using the global HEAD meant that any commit or
  branch bounce marked every project dirty, including projects that commit did not touch.
- **In worktree mode the working-tree term is omitted entirely.** That worktree's source is fully described by
  the committed fingerprint; the user's local edits are not in it and must not influence its signature.

Transitivity is not coded separately. Because upstream signatures are produced by a memoized DFS, each already
contains its own upstreams recursively.

### 7.2 Propagation modes

| Mode | Upstream term | Effect |
|---|---|---|
| **Safe** (default) | the upstream's *fresh* signature from this run | dirty + all transitive dependents rebuild |
| **Fast** | the upstream's *stored* signature | only directly dirty projects rebuild; no cascade |

Fast requires no extra persisted field. When project *X* was last built successfully, its stored signature
already embedded the then-stored signature of upstream *Y*; so recomputing *X* with a frozen upstream matches
its stored signature exactly unless *X*'s own inputs changed.

Configuration is *not* an upstream term — it enters every node's own signature — so Fast's upstream suppression
cannot mask a `Debug ↔ Release` switch. Changing configuration makes every project dirty in both modes. This is
a direct consequence of §9.4: output is config-agnostic in a single shared folder, so the previous
configuration's binaries are simply gone.

### 7.3 Cycles in the signature

Members of a strongly-connected component are never built, but their signatures still feed dependents outside
the component. Each SCC therefore gets **one composite hash** over all members' own terms plus their
outside-the-SCC upstreams, with intra-SCC edges collapsed to a fixed marker to break the recursion. Members and
downstream nodes all read that same value. Without this, a real source change inside a cycle could fail to
reach a dependent outside it, and that dependent would be silently skipped on the next build.

### 7.4 Will-build tri-state

Before a run — and after every Sync — each project carries `WillBuild` as a tri-state:

| Value | Meaning | Dot |
|---|---|---|
| `true` | dirty; will be built | filled amber |
| `false` | up to date; will be skipped | filled grey |
| `null` | no meaningful baseline yet (pre-Sync, or the signature could not be computed) | hollow ring |

If there is no usable HEAD, *every* node is hollow and the counters are not reported at all — printing zeros
would assert "everything is up to date", which is a different and false claim.

During a run the dot is live: the moment a project succeeds, its dot turns grey.

### 7.5 Build state

`build-state.json` is **global**, keyed by project id (the full csproj path), holding the built signature, the
built commit, the last result, the last run timestamp, the last branch and the last duration. It is written by
a single serialized writer, atomically (unique temp file + `File.Move(overwrite)`), after every project
completes. Readers open with `FileShare.Delete` so they cannot block the writer's rename, and a transient
sharing violation is retried a bounded number of times. A corrupt file never throws — it falls back to
defaults.

---

## 8. Run planning and execution

### 8.1 Run modes

| Mode | Set of projects |
|---|---|
| `Build` | the will-build set (incremental) |
| `Rebuild` | all projects; cached state ignored |
| `Continue` | the remaining queued projects of the stopped run, from the existing plan, with the elapsed clock preserved — engine capability only; the App has no surface that sends it |
| `RetryFailed` | the failed projects plus **all** their transitive dependents — independent of the Safe/Fast setting; console and event stream are not reset |

### 8.2 Ready-set scheduler

When a worker slot frees, the scheduler dispatches the ready project that comes **first in build order** —
never a random or hash-ordered one — and *skips forward* over projects whose dependencies are not yet resolved
rather than waiting on them. The same graph and the same completion order always produce the same dispatch
sequence.

A dependency counts as resolved when it is `Succeeded`, `Failed` **or** `Skipped`. Failure does not block
dependents; a single failure must not stall a run forever. Clean projects are skipped in waves as their
dependencies resolve, not all at once.

The scheduler is pure state: no I/O, no processes, no async, no logging. Its mutable state is guarded by one
lock — with a few hundred projects and a handful of calls per second, finer-grained locking would be
complexity without benefit.

### 8.3 Dependency issues

Because a failed dependency does not block its dependents, those dependents are built **against the last
successful output** of the failed project. That is a real hazard and it is surfaced explicitly rather than
hidden:

- The root failed-project names are propagated down the chain as `depIssues` (direct failures and inherited
  roots merged).
- The project's log opens with a warning line naming the root, distinguishing a direct dependency failure from
  an inherited one.
- The row and the graph node carry a filled red triangle in a **fixed 14 px slot** that exists on every row, so
  alignment never shifts.
- The action bar gets a `▲ N` counter chip that filters the list to `dep`.
- The event stream reads `built — dependency issue (2.4s)`, and the completion line reports
  `N dependency-affected`.

### 8.4 ETA

`(sum of duration estimates for queued projects + remaining time of in-flight projects) / parallelism`, plus
400 ms when anything is building. The result is exponentially smoothed (`0.75 × previous + 0.25 × new`),
displayed rounded to 5 s, and replaced by `· almost done` below 4 s. The per-project estimate comes from
`BuildState.LastDurationMs`; with no history the ribbon shows progress and elapsed time without an estimate.

### 8.5 Run logs

Every run writes to `%LOCALAPPDATA%\BuildOrchestrator\logs\run-<timestamp>\`, one file per project named by the
first 16 hex characters of the SHA-256 of the project id, plus a `decision.log` for orchestration decisions
(retries, skips, warnings). There is no in-memory ring buffer — the disk is the log. A project's log is written
by exactly one worker (the scheduler guarantees it); `decision.log` is written from all of them. Embedded CR/LF
inside a single MSBuild output line is normalized to a space so that one appended line is always one physical
line, and a strange line stitch in MSBuild output cannot desynchronize the chunk reader.

### 8.6 Planning pipeline

Planning is entirely Core's work; the Supervisor's composition root only wires it. For a fresh run
(`Build`/`Rebuild`) the sequence is:

```
prepare workspace (in-place or worktree)          ← must be first: the scan and the signature
  → scan (once) → evaluate (cached) → producer map    must see the same resolved root
  → edges → solution map → topological order → BuildPlan
  → (Build only) incremental pass: per-project signature + willBuild
  → RunPlan { plan, solutionRefs, incremental }
```

Two details that are easy to get wrong and are pinned:

- **One scan, not two.** The `.sln` *paths* needed for `-p:SolutionDir` come from the same scan result as the
  projects; `ProjectNode` carries only solution *names*, so a second walk of the workspace would be needed
  otherwise.
- **Planning runs on the run's background task**, not on the IPC dispatch loop. Planning a large repository
  takes seconds; blocking the loop would freeze command handling for that whole window.

`Continue` and `RetryFailed` never call the planner: they resume from the plan, the log writer and the clock of
the original run.

**Planning reports itself.** The planner takes a progress channel and emits a line per step; the coordinator
turns each into a `planProgress` event on the same FIFO channel as everything else, so they all reach the App
before `runStarted`. Lines that mark work about to begin — worktree preparation, the incremental pass — are
written *before* it, because those are the long steps and they produce no count of their own; lines that report
a count are written after the step that produced it. The window a resumed segment skips has no lines, because
it does no planning.

This repeats the work Sync already did, and that is correct: the working tree may have changed since, and
worktree preparation and MSBuild resolution only exist on this path. What was wrong was doing it *silently* —
the App clears the console on a run request, so a multi-second planning window left the screen with nothing on
it at all. The step texts therefore live in one place in Core and both callers read them; the same work must
not acquire two names.

### 8.7 Workspace preparation

Worktrees are prepared at **Build** time, not at branch selection — branch selection is intent only (§10.3).

- If no worktree is requested and no branch is selected, the in-place path returns without invoking git at all.
- If a pool worktree for the selected branch already exists it is **reused** (`reset --hard` inside it); a new
  one is created only when there is no candidate or reuse fails. Reuse is the reason the pool is persistent:
  same directory, same `obj`, warm cache.
- The pool cap is applied **before** the new worktree is added — the classic cache-eviction placement. Pruning
  is best-effort; its failure warns but does not block the build.
- **Failure handling is asymmetric on purpose.** If the selected branch *is* the active branch, a preparation
  failure warns and falls back to in-place. If the selected branch is *different*, worktree is mandatory
  (§10.3) and any failure raises a preparation error that surfaces as `planFailed` — the run never starts.
  Falling back there would silently compile the user's dirty working tree instead of the branch they asked for.
- The resulting "in-place" flag must reflect **reality**, not intent: it can only be false when a worktree was
  genuinely created. Deriving it from the request would make the signature omit its working-tree term while the
  build actually ran on a dirty tree, persisting a signature that claims a clean commit was built.

### 8.8 Run coordination

The Supervisor's coordinator owns one run at a time; a `startRun` while one is active answers
`error(runInProgress)`.

**Worker loop.** N workers drive one scheduler instance. `TryDispatch == false` does **not** mean "the run is
over" — it means "no ready work right now", because dependencies may still be compiling. A worker that gets
nothing parks on a wake signal instead of returning, and every completion (and every stop) wakes all parked
workers. The signal to wait on is captured *before* the condition is checked, so a wakeup arriving between the
check and the park cannot be lost. There is no polling anywhere in this loop.

**Exactly-once completion.** Everything between dispatch and `Complete` sits inside a `try`/`finally`. An
exception escaping that region would leave the project in flight forever, `IsDone` would never become true and
the run would hang — so even the display-name lookup is written not to throw.

**Event ordering.** All events go through a single unbounded FIFO channel drained by one pump task. MSBuild's
output callback is invoked *synchronously* from its stdout/stderr pump threads while IPC writing is
asynchronous; the channel both guarantees the order (`runStarted` → `projectStarted`\* → results →
`runCompleted`) and keeps those threads unblocked. The pump is deliberately tolerant: a single over-long
message is skipped without breaking the stream, and if stdout dies entirely the run **continues** — the disk
log is the real record, and the channel is still drained to completion so no writer ever blocks.

**Per project.** At dispatch time all dependencies are already terminal, so `depIssues` can be computed before
invoking and used for all three consumers at once (the log's warning lines, the event, and the accumulation
that this project's own dependents will inherit). The invocation request carries the solution directory, a
restore flag derived from the presence of `packages.config`, and — in worktree mode only — the isolated
intermediate path. The project's log file is opened before and closed after the invocation, so a late line
cannot be silently dropped. The first line written is the real MSBuild command line. On success the build state
is persisted with the signature computed during planning; on failure the stored state is invalidated so the
next run does not consider the project up to date.

**Stop bookkeeping.** If a stop was acknowledged, writing `runStopped` is a debt that must be paid even when
the run never reached `runStarted` (a stop pressed during a multi-second planning window) — otherwise the App
would wait for an event that never comes. The run slot, the stop state and the whole perf state (applied cap,
pending intent, copy-floor depth, drain flag) are reset in one critical section, because the IPC loop runs on
another thread and `setPerfMode` has no run-state precondition: an intent arriving in that window would
otherwise leak into the next run.

**Continue and RetryFailed** transform the snapshot rather than replanning: *Continue* requeues only the
projects that failed with a stop reason (the torn-DLL guard) — genuine failures stay failed — while
*RetryFailed* requeues every failed project plus its transitive dependents. Neither resets the elapsed clock,
the console or the log writer.

---

## 9. Build execution

### 9.1 MSBuild resolution

`vswhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe"`, run from the fixed
installer location under `%ProgramFiles(x86)%`. Full MSBuild is required — Visual Studio or Build Tools.
Without it the Supervisor still starts and the failure surfaces as a resolve error rather than a silent hang.

### 9.2 Argument contract

```
<project> -t:Build -p:Configuration=<cfg>
          -p:UseSharedCompilation=false -nodeReuse:false -p:BuildProjectReferences=false
          -clp:Summary -nologo
          [-p:BaseIntermediateOutputPath=<isolated obj>\]
```

- `-p:BuildProjectReferences=false` is **mandatory**. The orchestrator already builds every dependency as its
  own node; letting MSBuild walk the `ProjectReference` chain again re-enters sibling projects and hits their
  `obj` state.
- The two v1 flags cost real time — measurements put the flags-off build at roughly **2.9×** the flags-on
  build, essentially all of it from shared compilation (node reuse alone measures at ~0, because per-project
  shell-out does not spawn extra nodes). They stay on because with a compiler server the emit happens in a
  long-lived `VBCSCompiler` **outside** the job, which reintroduces the torn-DLL risk that §4.5 exists to
  eliminate. Correctness was chosen over the 2.9×; revisiting it requires a mechanism that closes the emit
  window, not just a faster number.
- No `-p:OutDir` and no `-p:OutputPath` is ever passed (§9.4).

Arguments are passed through `ProcessRunner`'s `ArgumentList` — manual string concatenation is prohibited — and
`UseShellExecute` is false everywhere. `cmd.exe`/PowerShell is never used as an intermediary. Where a command
line must be assembled by hand, escaping follows the `CreateProcessW`/MSVCRT rules including the
backslash-before-quote counting.

### 9.3 Restore

`msbuild -t:restore -p:RestorePackagesConfig=true -p:SolutionDir=<dir>\`. There is **no dependency on
`nuget.exe`** — it is not on `PATH` on the target machines. The `SolutionDir` property is required: without a
solution context, restoring a `packages.config` project fails outright. This is why the solution map of §6.3 is
an input to restore and not only to the UI.

### 9.4 `obj` isolation and `OutDir`

**`OutDir` is never touched and never read.** Build output lands exactly where Visual Studio would put it: in
the solution's own shared output folder, produced by the projects' own post-build copy events. The orchestrator
copies nothing.

Only the intermediate directory is isolated, and only in worktree mode:
`<worktreeRoot>\_obj\<first 8 bytes of SHA-256(lowercased project id)>`. Keying by project id makes it
structurally impossible for two projects to share an `obj` folder. This is not cosmetic — a deleted sibling
project's leftover `netstandard2.0` artefacts in a shared `obj` (`project.assets.json`, `*.nuget.g.props`)
were measured breaking otherwise-healthy builds. In-place builds keep the default `obj` for Visual Studio
parity, and get a **diagnostic instead**: before such a build, foreign-TFM residue is detected and reported as
a console warning. Nothing is deleted or modified.

The symmetric half of this rule is §7.1: since output is never inspected, "did it change?" can only be
answered from source.

### 9.5 Copy contention

Parallel post-build copies to a shared output folder contend. `MSB302x` sharing violations are retried with
backoff, and while a copy window is stuck the CPU cap and priority are temporarily raised to the Balanced floor
(§11.3) — starving a stuck copy is worse than briefly exceeding a cap.

### 9.6 Output encoding

MSBuild's console output encoding is resolved explicitly so that non-ASCII output is not mangled on a Turkish
Windows install. All number and duration formatting in the domain and the view models uses
`InvariantCulture` — `4.2s`, never `4,2s`.

---

## 10. Git integration

### 10.1 The read-only surface

The complete set of git invocations in the codebase:

| Command | Mutates? |
|---|---|
| `rev-parse --verify -q HEAD` · `symbolic-ref --short -q HEAD` · `status --porcelain` · `rev-parse --is-shallow-repository` · `for-each-ref …` · `rev-parse --verify -q refs/{heads,remotes/origin}/<branch>` · `ls-tree -r HEAD` · `worktree list --porcelain` | no |
| `fetch origin <branch> --no-tags` | only `refs/remotes/*` |
| `worktree add --detach <path> <sha>` · `worktree remove --force <path>` | pool worktrees only |
| `reset --hard <sha>` | **cwd is a pool worktree**, never the main repository |

`checkout`, `switch`, `pull`, `merge`, `rebase`, `cherry-pick`, `stash` and `clean` do not appear anywhere.

`reset --hard` passes three gates: the candidate path comes from git's own `worktree list --porcelain` and must
be under the pool root; it is rejected if it equals the main repository root (junction defence); and it is
rejected unless that worktree's HEAD is detached, because resetting an attached HEAD would move a branch ref.

### 10.2 Sync

Sync runs the whole analysis, in Core:

```
git fetch origin <branch> --no-tags   (ref-only)
  → scan → evaluate (cached) → producer map → edges → SCC/topo → layers
  → will-build pass
  → workspaceTopology + buildPreview + syncCompleted
```

Full analysis happens **only** in Sync; the implicit Sync that precedes a Build is cheap because of the
evaluation cache.

If the remote is unreachable, the fetch failure is swallowed: a warning line is printed, the target SHA falls
back to the local HEAD, and the flow continues. The degraded path does **not** skip topology or the will-build
pass — offline still produces a complete, usable Sync.

**A known seam, stated rather than implied:** the scan and the will-build pass always run against the *active*
working tree, because K1 forbids checking anything out. If the selected branch differs from the active one, the
fetch and the target SHA refer to that branch while the topology, the preview and the counters still describe
the active tree. The code reports only what it actually computed.

### 10.3 Branch and worktree model

Branch selection is *intent*: nothing happens in git until Build is pressed. There is no separate "include
local changes" toggle — the branch choice determines the mode:

| Selected branch | Worktree | Result |
|---|---|---|
| active | off (default) | in-place build, local changes **included** |
| active | on | committed HEAD builds in a pool worktree, local changes excluded |
| any other | forced on | committed HEAD of that branch builds in a worktree; the active branch is never touched |

Choosing a different branch resets project states to pending, returns the phase to boot and writes an intent
line to the console (`branch target: … — worktree will be used at Build`). The actual `git worktree add` is
logged when Build runs, as the command it really is.

If a worktree cannot be prepared and the selected branch differs from the active one, the run **does not
start**. Silently building the wrong (dirty) tree is worse than stopping.

### 10.4 Worktree pool

Pool root: `%LOCALAPPDATA%\BuildOrchestrator\worktrees\`. Worktrees are persistent — that is the point, since
they carry a warm `obj` cache — always created with `--detach`, and individually deletable from the UI. The
pool is capped at 20 GiB with LRU pruning applied *before* a new worktree is added. Auto-naming slugs the
branch (`/` → `-`) and appends the next free ordinal.

The pool is the application's scratch space. A worktree edited by hand will have those edits discarded by the
`reset --hard` on its next reuse; this is the one data-loss risk the tool carries and it is documented at the
call site.

### 10.5 Path sanitization

Branch slugs replace `/`, `\` and `: * ? " < > |` and control characters with `-`, collapse repeated dashes,
and **throw rather than fall back** if the result is empty or `.`/`..`. A separate validator rejects absolute
paths, separators and `..` for any name that will become a directory segment.

---

## 11. Resource governance

### 11.1 Perf profiles

One chip cycles three fixed profiles. This is the single source of truth for all three values:

| Mode | Parallelism | Priority class | Inner-job hard CPU cap |
|---|---|---|---|
| Full | 6 | Normal | none |
| Balanced (default) | 4 | BelowNormal | 70 % |
| Light | 2 | Idle | 40 % |

Switching mid-run changes the cap and the priority live and writes a console note whose body is exactly
`parallelism: <n> · cpu cap <p>%` (`cpu cap off` for Full). **Parallelism does not change mid-run** — workers
are created once at the start of a run — so the new worker count applies to the next run. The note text has a
single owner in Core, called by both the App and the Supervisor.

The perf intent is also honoured during the planning window: a change made while a run is starting is held and
applied when the run begins, rather than being silently dropped.

### 11.2 What the cap does and does not cover

The cap is written with `JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | HARD_CAP` and applies to the **sum** of the inner
job's processes as a percentage of the whole machine. Priority tops out the priority class of every process in
the job. Both are written **only to the inner job**:

- `git.exe` and `vswhere.exe` start as plain child processes and are never assigned to the inner job. Sync,
  branch listing and MSBuild resolution therefore run at full speed even in Light mode. The cascade still
  covers them, because they inherit outer-job membership.
- The App is in no job at all — the UI, rendering and console batching are never throttled.
- No memory, disk, network, process-count or thread limit is written. The job carries exactly three things:
  `KILL_ON_JOB_CLOSE`, the priority class and the CPU rate.

Writing the cap is an optimization: a Win32 failure logs a warning and the run proceeds. The priority write
goes Query → OR → Set, because it shares the extended-limit structure with `KILL_ON_JOB_CLOSE` and writing a
fresh structure would silently clear the kill flag.

### 11.3 Copy floor and drain rule

While a post-build copy is stuck on contention, the cap and priority are raised to the Balanced values
(70 % / BelowNormal) for the duration of that window, which is reference-counted. Light's 40 % is therefore not
an absolute ceiling. The floor is *defined as* Balanced's values rather than as separate constants, so the two
cannot drift apart.

Once a graceful stop starts draining, the cap is never re-applied and the priority cannot go below the same
floor (§4.5).

---

## 12. Application shell

### 12.1 Startup routes and composition

Argument parsing has one owner and three routes, in priority order: `--font-ab` (a developer shell for the font
comparison — no DI, no engine, deliberately outside the single-instance gate), `--autostart` (start hidden in
the tray), normal. An unrecognized argument is swallowed.

The composition root registers the `EngineHost` (resolving the Supervisor path from the assembly metadata of
§3.3), the console batcher (~50 ms flush tick), the OS actions service and the view models. Two application-wide
singletons are exposed statically because their owners have no constructor seam: the reduced-motion settings and
the hero-motion coordinator.

### 12.2 Window chrome

Custom dark title bar via `WindowChrome` (caption height 40, no Aero caption buttons) on a `SingleBorderWindow`.
`AllowsTransparency` is never used. Consequences that had to be handled explicitly:

- **Maximize padding correction is mandatory** (`dotnet/wpf#3887`): without it the content overflows the screen
  edge when maximized.
- Windows 11 rounded corners come from `DWMWA_WINDOW_CORNER_PREFERENCE`; the 1 px frame from
  `DWMWA_BORDER_COLOR`.
- The maximize glyph swaps to a restore glyph when the window is maximized.
- The Windows 11 **Snap Layouts** flyout is deliberately absent. Windows opens it only for a window that answers
  `WM_NCHITTEST` with `HTMAXBUTTON`, and that answer turns the button into a non-client region — the WPF button
  then receives no `Click` and no `IsMouseOver`, so both would have to be re-synthesized from `WM_NCLBUTTONDOWN`
  / `WM_NCLBUTTONUP`. There is no middle ground between "flyout on" and "ordinary button", and the flyout is not
  wanted here: the caption buttons are plain WPF buttons, hovered by their own template trigger.
- The window opens **maximized**; `Width`/`Height` (1400 × 800) remain as the restore size. Minimum size
  1240 × 620. DPI awareness is PerMonitorV2 (px → DIP 1:1), with `UseLayoutRounding` at the root so hairlines
  stay hairlines.

### 12.3 Single instance, tray, hotkey, autostart

Single instance is a session-local mutex plus a named pipe whose name includes the session id; a busy pipe backs
off instead of spinning. A second instance calls `AllowSetForegroundWindow` before signalling, then exits
silently. If it cannot bring the existing window forward it does **not** go silent: it shows a one-line tray
balloon and exits with a distinct exit code.

Closing the window with `X` minimizes to the tray. The first time this happens, an **OS tray balloon** explains
it, once — in-app toasts are prohibited by the design.

The global hotkey (`Alt+B` by default, read from `ui-state.json`) is registered with `RegisterHotKey`. A
conflict disables it silently; the tray icon still restores the window. There is no UI for changing it yet.

Autostart writes to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. No admin rights, no HKLM, no service.

### 12.4 Layout modes and persistence

Three view modes from the title bar: **quad** (default; returning to the preset resets all three splits to
50/50/50), **list** (graph hidden, left column is the project list), **focus** (graph hidden, console takes
76 % of the right column).

Splitters have a 7 px grab area over a 1 px visible line that turns amber while dragging. Bounds: columns
28–72 %, rows 18–82 %. Mode and all three split positions persist.

---

## 13. UI architecture

### 13.1 MVVM

`RunViewModel` is the single run-facing view model, split across partial files by surface (core, action bar,
event stream, workspace). It owns the project rows, the counters, the phase, the selection, the filter and the
command set. Rows are `ProjectRowViewModel` — observable state only; every visual decision (colour, glyph,
badge) is made in XAML from that state.

Text that the design specifies literally is produced by **pure, testable static classes**, not by controls:
`RibbonText` (one line per ribbon phase), `StreamText`, `InteractionText`, `ProjectFilter`, `RunCounters`,
`LayerGrouping`. A control that also decided its own wording would be a second source of truth.

### 13.2 Panels

```
┌──────────────────────────────────────────────────────────────────────┐
│ TITLE BAR 40px — logo · title · OSYS · main [· main-2]   ⊞ ≡ ▣ ⚙ — □ ×│
├──────────────────────────────────────────────────────────────────────┤
│ STICKY RIBBON 32px — phase · building chips · failure cluster        │
│ global progress 2px                                                  │
├────────────────────────────────┬─────────────────────────────────────┤
│ DEPENDENCY GRAPH               │ CONSOLE                             │
│ ═══════ horizontal splitter ═══│═══════ horizontal splitter ═════════│
│ PROJECTS                       │ EVENT STREAM                        │
├────────────────────────────────┴─────────────────────────────────────┤
│ ACTION BAR 42px — Sync · counters · branch · worktree · cfg · perf ·  │
│                                                          Build ▴     │
└──────────────────────────────────────────────────────────────────────┘
```

**Sticky ribbon.** One mono line describing the phase, plus 20 px chips for the projects currently building (at
most four, then `+N`), plus — only when there are failures — a failure cluster on the right: `✗ 5 failed`,
`· 4 dependency-affected` dimmed, the first three failing chips, and a `+N more` chip that applies the `failed`
filter. Glyphs are 13 px in the phase line and 10 px inside chips. There is no dismissible banner: a failure
summary that can be dismissed is a failure summary that will be missed. Underneath, a 2 px progress bar,
radius 0, coloured by phase. It runs indeterminate whenever the engine is working without a measurable
denominator — during Sync, and during `starting`, where there is no plan yet and a determinate bar would sit
frozen at zero while the line above it says work is under way.

Four texts can pre-empt the phase line, in this order: an engine death (with the *Restart engine* action), an
engine that has gone silent, a failed Sync, then a failed run. The three failures are red, carry the reason,
and persist until the user starts something new — a Sync clears the run failure, a run start clears both. Their
order is the order of how much is unknown: with no engine nothing can be retried, and with no Sync the project
states themselves are stale. The silence line sits below the death and above the failures because it is the
only one describing the *present*: the others are facts about something that already finished, and all of them
assume a working engine. It is amber rather than red and carries no glyph — a drain that is merely slow is not
a failure — and it clears itself the moment the engine speaks or the wait ends. A rejected request is not a
failure and does not take this path — declining a request with nothing to resume leaves the `stopped` line
standing, because that line is still true.

**Projects list.** 36 px rows: a 2 px status stripe (3 px and amber when selected), the 8 px will-build dot,
the project name with the solution name beside it, then a right-aligned block — on hover, *Reveal in Explorer*
and *Open in Visual Studio* icons; without hover, `curSha → targetSha` for dirty projects — then the status
glyph, the fixed dependency-issue slot, and a 46 px duration column. The building row carries a motionless
amber "breath" (an `amber-soft` layer at 0 → 0.32 → 0 opacity over 3.8 s); a sweep or a shine was tried and
rejected. A failing row shakes once, ±3 px over 360 ms.

Layer headers are 24 px and stick **cumulatively**: the *i*-th visible header pins at `i × 24 px` and stays
there as the ones below it pile up underneath.

The list is **virtualized**, and by a panel of its own rather than WPF's. `VirtualizingStackPanel` estimates
the height of unrealized items from the average of the realized ones; with 36 px rows interleaved with 24 px
headers that estimate drifts, and the scroll axis would no longer agree with the cumulative table that sticky
headers, follow-mode and selection scrolling all read. `FixedHeightVirtualizingPanel` never estimates — it
asks for each entry's height and builds the same table — so the extent is exact by construction. It does not
implement `IScrollInfo`: the enclosing `ScrollViewer` still owns the scrolling and receives the true total
height, which leaves smooth scrolling, the bottom anchor and follow-mode untouched. Containers are recycled,
so a row control is reused with a new view model rather than rebuilt. On the first measure pass the viewport
is not yet known; the panel realizes nothing at all that pass — its reported height comes from the table, not
from realized children, so the `ScrollViewer` still computes a correct viewport and the real window is
realized in the same layout round.

One consequence is deliberate: the staggered reveal reaches the rows that exist, which is the visible window.
Rows scrolled into view later simply appear.

Follow-mode keeps the frontier visible while a run is in flight and nothing is selected: at most one scroll
animation every 550 ms, and none at all if the target is within 54 px.

Three things stop it, and the first two are the same statement in different words: *I am looking at this*.
Selecting a row stops it, and clearing the selection resumes it. Filtering the list stops it too — under a
filter the user is inspecting a subset, and the frontier may not even be in it — and clearing the filter
resumes it. Neither gate is permanent.

Scrolling the list with the wheel also stops it — the user's scroll always wins — but that pause is not
permanent either: it lifts as soon as the user can be considered to be watching again, by either of two routes. Bringing the list back to the
**frontier row** (within 48 px of the viewport) resumes it, which reads the intent directly. Leaving the list
untouched for three seconds also resumes it, which closes a pause the user has simply forgotten about; every
wheel notch restarts that window, so follow cannot cut in while scrolling is still going on. Returning to the
**bottom** of the list resumes it too, the same 48 px threshold the console and the stream use for their bottom
anchor — kept for symmetry, though for this panel the bottom is rarely where the action is.

Only routing the resume through the bottom was the original design, and it was wrong in practice: during a run
the frontier sits in the middle of the list, so a single wheel notch parked follow for the rest of the build.

**Console.** See §13.5.

**Event stream.** A capped list of chronological one-line events. It is not virtualized and does not need to
be: the buffer is trimmed from the front to a render slice, so the panel is bounded by construction, and rows
are inserted and removed one at a time as events arrive rather than rebuilt in bulk. Virtualization would also
cost more than it saves here — each row owns animation state (the newest line is typed out, a done line glows
once), and recycling containers would swap the model underneath a running animation. Events arriving less than
340 ms apart and all error events print instantly. Rows for projects are clickable and
participate in the shared selection. A run that finishes with zero failures glows its done line once
(`success-soft` → transparent over 1.1 s) — that is the *entire* success flourish; there is no green wave
through the list or the graph.

**Action bar.** Sync; the counter chips (`Σ`, building, `✓`, `✗`, `—`, `▲`), each a filter toggle; the branch
chip (searchable popover); the worktree chip; the `Debug | Release` segment; the perf chip; and the Build
split-button, whose menu carries *Build*, *Rebuild*, and — when something failed — *Retry failed — N failed +
dependents*. There is no *Continue*: a stopped run is not resumed, it is started again. While a run is in flight the primary button becomes *Stop*, and the
branch, worktree and configuration controls lock; the perf chip stays live.

**No run without a topology.** *Build*, *Rebuild* and *Retry failed* stay disabled until a Sync has published a
topology, and an empty one (a folder with no projects) keeps them disabled. The reason is that the full analysis
runs only in Sync (§6): a run publishes `buildPreview` but never `workspaceTopology`, so a build started before
the first Sync would compile for real while the list, the graph and the counters stayed empty — the user would
be watching a run without being able to see what it is doing. Sync itself is never gated; it is the way out.

The lock — and the *Stop* button with it — begins at the **click**, not at `runStarted`. The phase moves to
`starting` and a line goes into the run document before the command is even written, mirroring what a stop
request does. Anything less leaves a gap the width of a planning window, during which the user has pressed a
button and the screen still describes the world as it was; on a 177-project workspace that gap is seconds long
and the console has just been cleared, so nothing on screen contradicts "my click did nothing". The phase
leaves `starting` by every exit that can happen: `runStarted` moves it on, a run-ending error or a send that
fails synchronously puts it back, and an engine death drops it to the resting phase rather than to `stopped` —
nothing was built, and a "stopped" line describing a run that never began would be a fiction.

### 13.3 Popovers and dialogs

Popovers open 8 px above their chip on `surface-overlay` with a `border-strong` hairline, radius 8, the overlay
shadow, and a 140 ms pop-in (4 px up, scale .985 → 1). Outside click or Esc closes them. Rows inside them are
28 px. The branch popover is 272 px wide and carries a search box; the worktree popover is 300 px and carries
the switch, the target list and the `source` line.

The branch list is virtualized, and a popover only builds rows while it is open — closed, it does nothing at
all when the inventory changes. Both matter more than they sound: a real repository carries hundreds of refs
(`refs/heads` plus `refs/remotes`), every Sync republishes the inventory, and four surfaces listen to it.

That is also why the inventories are **snapshots rather than incrementally mutated lists**. `Branches` and
`Worktrees` are replaced wholesale and emit **at most one** change notification per publish — and none at all
when the content is identical, which is the common case, since Sync asks for the inventory every time whether
or not anything changed. Reconciling item by item would emit two notifications per entry, and with several
listeners each rebuilding on every notification the cost is quadratic in the number of refs. The reset that a
wholesale replacement implies is safe here, unlike in the projects list: there is no container identity or row
selection to preserve — the selected branch is a value, reconciled separately against the new inventory.

The Settings dialog is 620 px and carries the LAYERS editor and the REPOSITORY row (current root plus
*Change…*). Layer cards are 36 px and reordered by dragging the grip with `Mouse.Capture` and a half-row swap
threshold — `DragDrop.DoDragDrop` is prohibited, because the OS ghost-drag semantics do not match the design.
Neighbours snap without animation. An invalid regex puts its input into the invalid state and disables *Save*.

When no layers have been saved yet, the editor opens pre-filled with four OSYS defaults, in match order:
`OSYS.Types`, `OSYS.Business`, `OSYS.Orchestration`, `OSYS.UI` — each an anchored regex that matches the
layer's name as a prefix of the project name (`^OSYS\.Types\.` and so on). The footer's *Restore default
layers* button re-fills the editor from that same list at any time. Neither the initial fill nor the restore
is a startup seed: the defaults live only in this dialog's draft, and nothing reaches disk or the engine until
*Save* is pressed.

*Change…* on the REPOSITORY row only writes the picked path into the draft and refreshes the label beside it;
Cancel, Esc and a scrim click discard the draft — the pending root included — without touching anything live.
*Save* is the single point where the draft is applied, in a fixed order: the layer patterns are applied first,
then the pending repository root (which resets the project rows to hollow), then exactly one Sync is sent. The
order is load-bearing, because the Sync command carries the current layer patterns — sent before they were
applied, it would carry stale ones. The Sync itself is unconditional: Save does not compare old and new state
to decide whether to run it.

Three gates hold. While a run is in flight the layer patterns are applied but the repository root is left
alone and no Sync is sent, since pulling the root out from under a running build would be wrong; because the
dialog's label has already confirmed the picked folder, a root change this gate drops is announced in the
console as `Repository change deferred — run in flight`, while a Save that carries no root change stays
silent.
If no repository has ever been selected, there is nothing to Sync — that gate sits *after* the root is
applied, since the headline journey (a new user opens Settings, picks the root, saves) fills the root right
there. And when the engine is unavailable — the supervisor was never found, or would not launch — the layers
and the root are applied but nothing is sent: each send would fail and print an error line contradicting the
permanent ribbon message, the same reason Sync, Build, Rebuild and Retry failed are disabled in
that state. The root is still applied because it is local state that persists, and the first Sync after the
engine returns carries it.

The shell's own *Choose Folder* invitation, shown before any repository is selected, does not go through this
dialog: it has no Save step, so the folder it picks applies immediately — the root changes, the project rows
reset to hollow, and a Sync starts right away.

### 13.4 Scroll infrastructure

WPF provides neither smooth scrolling nor horizontal wheel input, so the scrolling surface is assembled here:

| Piece | Role |
|---|---|
| `ScrollAnimator` | attached DP animating `VerticalOffset`; a wheel event cancels the animation |
| `BottomAnchorBehavior` | bottom-stick with a 48 px release threshold and a jumping window; drives the `⌄ latest` pill |
| `FollowScrollController` | frontier following (550 ms cadence, 54 px dead-band) |
| `ScrollArbiter` | the referee |
| `HorizontalWheelScroll` | horizontal wheel / touchpad input, which WPF never delivers |

`ScrollArbiter` is a pure decision core. Rules: a user scroll suppresses **only** that panel; a panel receives
at most one grant per frame and each grant bumps that panel's epoch so an in-flight animation from an earlier
epoch is discarded (no yo-yo); an explicit selection or jump always wins and re-enables the panel; automatic
follow loses to an active selection **or an active filter**; and across panels the priority is
frontier > console > stream. Keeping both intent gates in the arbiter rather than at the call site is the point:
"may follow run right now" is answered in exactly one place.

Horizontal scrolling is a separate story, because WPF's input stack turns only `WM_MOUSEWHEEL` into a routed
event — `WM_MOUSEHWHEEL` is never dispatched, so neither a precision touchpad's two-finger pan nor a tilt wheel
reaches any element. `HorizontalWheelScroll.Enable` puts a hook on the window's message path; the panel that
enabled it tests the message's screen point against its own bounds and drives the first horizontally scrollable
viewer inside it — template included, since the console's viewer lives inside AvalonEdit's. The console is the
only panel that enables it: it is the only surface with horizontal overflow (`WordWrap=False`). Two details are
measured rather than assumed, and both are recorded on the class: the scroll must be requested one dispatcher
turn later (a request issued inside the window procedure is silently dropped), and the target offset is
accumulated across one gesture instead of being read back from the viewer each time (the viewer publishes the
new offset only after a layout pass, so reading it back loses steps). A step is the horizontal twin of WPF's
vertical one — `WheelScrollLines × 16 px` per notch — except that the delta's *magnitude* is honoured, which is
what makes a touchpad's stream of small deltas track the finger.

`LayoutMetrics` is the shared arithmetic behind sticky headers, follow-mode and selection scrolling: one
cumulative offset table over mixed 36 px rows and 24 px headers, giving any row's absolute Y, the pinned header
set at a given offset, and a row's scroll target. Sticky headers are an **overlay** (an `ItemsControl` above the
`ScrollViewer` reading that table), not in-flow elements.

### 13.5 Console host

The console is a read-only AvalonEdit. That choice is forced: text selection is non-negotiable, lines must be
individually coloured, and MSBuild-verbose volume must not stall the UI. `TextBlock` gives no selection;
`FlowDocument`/`RichTextBox` collapses under the volume; an `ItemsControl` of lines loses selection across
lines.

- The document stays **plain text** (`HH:MM:SS ▸ message`), so what the user copies is meaningful. Colour comes
  from an offset-based `DocumentColorizingTransformer`.
- Appends are batched: IPC → channel → ~50 ms flush → exactly one `BeginUpdate → Insert → EndUpdate`.
- The live document is capped at a render slice of 200 lines; the full log is on disk and is paged in on
  demand (§5.5).
- **Hybrid active line:** the newest line is typed in a `TextBlock` overlaid beneath the document and committed
  when finished. The cost is that the active line is not selectable for roughly 250 ms; that is the cleanest
  available trade. The caret is a 7 × 13 px rectangle blinking at 1.1 s, not a font glyph, and fades out
  ~420 ms after typing stops.
- Selecting a project switches the panel header to `← Back` + project name + status, and the log opens as a
  cascade: three lines every 26 ms, each fading in over 140 ms.
- Typing degrades under load: a burst suspends typing and prints instantly, errors always skip typing, and raw
  MSBuild output is never typed character by character.

### 13.6 Graph renderer

Layered DAG: one horizontal row per layer at 96 px spacing, cubic bezier edges top to bottom. A node is a 26 px
square with 4 px radius plus a short label below it (the `OSYS.` prefix is stripped). Discovered nodes use a
dashed outline — drawn as a `Rectangle` with `StrokeDashArray`, because a WPF `Border` cannot be dashed.

Edge styling encodes the run: default hairline; amber with a flowing dash toward a building target; green or
red toward a finished one; a static red dash along a branch carrying a failure; amber (or red) at 1.6 px and
full opacity when it touches the selection. Selecting dims everything else — nodes to 25 %, edges to 16 %.

The camera follows automatically: the selected node, else the centre of gravity of the building frontier, else
the centre; 460 ms ease-in-out, scale fitted to the panel and clamped to 0.68–1.08. A small-deviation threshold
prevents jitter.

Rendering stays on the **Shapes path** — every node and edge is a `UIElement`, so hit-testing and tooltips are
native. A migration to `DrawingVisual` layers was prepared and then **not performed**, because measurement
showed the bottleneck is visual-tree *construction* (64–72 %) and WPF's measure/arrange of that tree (28–36 %),
with pure layout arithmetic at 0.03 % — a drawing-cost optimization would have targeted the wrong thing.
What was done instead: viewport culling (off-screen nodes and edges are never constructed), lazy badges and
label level-of-detail (17 → 9 objects per node), and a status fast-path with shared frozen dash collections so
the 200 ms status tick does not touch unchanged nodes. Full detail holds up to 150 nodes; above that, culling
and LOD carry it.

All animations read the reduced-motion setting **fresh at start**; durations and easings come from
`Duration.*`/`KeySpline.*` resources and colours from `Brush.*` resources — no hex, no milliseconds inline.

### 13.7 Selection and filtering

One canonical gesture: clicking a project row, a graph node or a stream line selects that project **everywhere**
— the graph pans to the node, the list scrolls to the row, the console switches to that project's log, the
panel header enters `← Back` mode. Clicking the same element again, or `Back`, or Esc, clears it and follow-mode
resumes. Text selection inside the console never clears the project selection.

Esc is a chain and only ever closes the topmost layer: dialog → popover/menu → selection.

### 13.8 Design-system control library

WPF ships almost none of the design's vocabulary, so `Resources/Controls.xaml` defines it as templates and
styles, and `Controls/` holds the custom elements that a template cannot express.

| Element | Form |
|---|---|
| Buttons | One shared `ControlTemplate` over four variants (primary / secondary / ghost / danger) × three sizes, differing only in brushes and metrics |
| Split button | A custom control: two halves sharing the primary template, joined by per-corner radius and a 1 px divider — visually one body, semantically two buttons |
| Chip | A `ToggleButton` style plus a counter text style |
| Icon button | Its own compact template, with a toggle variant for the layout-mode icons |
| Switch | A `CheckBox` template — WPF has no toggle switch |
| Segment | An `ItemsControl` of `RadioButton`s (the `Debug｜Release` control) |
| Input | A `TextBox` style with watermark, prefix and invalid states |
| Scrollbar | An implicit `ScrollBar` style — a 10 px transparent rail, no arrow buttons, and a neutral thumb pill inset by 3 px. Being implicit it crosses template boundaries, so stock and third-party viewers alike (the console editor included) wear it without their XAML knowing; the stock corner square between two bars is neutralised app-wide |
| Kbd · ProgressBar · Popover · Dialog · Focus visual | Styles over stock elements |
| Status glyph · building spinner · will-build dot | Custom controls drawing rings, arcs and dots |
| Tracked text | Custom element for letter-spaced caps labels (§14.2) |

Three pieces of shared machinery keep the copies from multiplying:

- **`DsTransition`** implements the design's 120 ms colour transitions. A template's state trigger points an
  attached property at a *token brush*; the class then installs a template-local, unfrozen brush on the real
  property and animates that copy. This is the standing answer to the frozen-brush rule of §14.5 — a shared
  resource brush cannot be animated, and animating one would drive every consumer at once.
- **`PopIn`** is the single 140 ms entrance animation, shared by both popovers and the Build menu. There is no
  exit animation; overlays hide immediately.
- **`RevealStagger`** owns the hero acquisition, generation stamping and guarded release of the opening
  reveal. The *cadence* is deliberately not shared — the graph staggers by layer, the list by row (§13.2).

Both popovers derive from a common base that owns the open state, the refresh-then-animate-then-focus sequence,
the Esc handling (a popover is a separate HWND, so the window-level Esc chain does not reach it) and outside
click; only the branch search filter and the worktree three-state text remain per-popover.

Filtering is a free-text query (case-insensitive substring on the project *name* only — never the path) ANDed
with one status chip (`building` — which includes queued — `succeeded`, `failed`, `skipped`, `dep`). The active
filter appears as a removable chip in the panel header.

### 13.9 Keyboard

| Key | Action |
|---|---|
| `F5` | Build — or Stop while a run is in flight |
| `Ctrl+F5` / `Shift+F5` | Rebuild |
| `Ctrl+F` | Focus the project filter |
| `Esc` | Close the topmost layer (see above) |
| `Alt+B` | Global hotkey: restore the window from the tray |

The key → intent table is a pure, tested structure that `MainWindow` merely wires into `InputBinding`s, and
every dispatch honours the command's `CanExecute` — a shortcut never bypasses a disabled button. Double-Shift
and `Ctrl+P` are *negatively pinned*: a test asserts they are **not** bound, so they cannot reappear by
accident.

---

## 14. Design system

This section is the design system. There is no external style sheet, no runtime theming and no second
authority: the values below are declared once in `Resources/Tokens.xaml` and `Resources/Motion.xaml` so the
application can consume them, and component-specific measurements sit as named constants on the control that
draws them. Section §13 gives those per-component values in context.

### 14.1 Tokens

`Resources/Tokens.xaml` is the **only** file in the application allowed to contain a raw colour or size
literal; a source guard fails the build's test suite if a hex appears in any other XAML.

**Surfaces** (near-black, slightly warm): `console-bg #060608` · `surface-sunken #0a0a0c` ·
`surface-base #0e0e10` · `surface #141417` · `surface-raised #1a1a1e` · `surface-overlay #202024`. Hover is one
surface step up. Scrim is flat `rgba(4,4,6,.60)` — no blur.

**Borders** carry the structure, not shadows: `border-subtle #1c1c20` · `border #2a2a30` ·
`border-strong #3a3a42`.

**Text:** `#ededee` / `#a9a9b0` / `#76767e` / `#54545c`, plus `#1c1304` on amber.

**Brand — one accent, amber:** `#eda10f` with bright/dim/text variants and soft/border alphas. No second
decorative colour exists. Colour carries *status*, never decoration; hierarchy is built from weight, size and
position.

**Status palette,** four tones each (core / `-text` / `-soft` 10–12 % / `-border` 24–32 %): success `#43b16b`,
fail `#ee5a52`, building = the amber family, skipped `#6a6a73`, cycle `#df6f2b`, queued `#7c7c84`.

**Spacing** is a 4 px grid (4/8/12/16/20/24/32/40/48/64). **Radius** is restrained: chip 3, control 4,
card/panel 6, overlay 8, pill 999, **console 0**. **Elevation** exists only on floating overlays — two shadow
tokens, nothing else; panels and cards are flat with a 1 px border.

Fixed heights: title bar 40, ribbon 32, progress 2, action bar 42, panel header 28, row 36 (compact 30), layer
header 24.

### 14.2 Typography and fonts

Geist for the interface, Geist Mono for everything the machine produced — console output, durations, SHAs,
counters, paths — always with tabular figures. Scale: 11 / 12 / **13 (base)** / 14 / 16 / 20 / 26 / 34.
Weights: 400 body, 500 emphasis, 600 headings. Caps labels (panel headers) are 11 px / 500 / uppercase with
0.07 em tracking in `text-faint`.

Fonts are **embedded static OTF** (Regular / Medium / SemiBold in both families) taken from the upstream GitHub
release. Variable fonts are unusable — WPF has no axis support — and the CDN build was rejected because its
OpenType tables can be subset. The application therefore works air-gapped. Letter spacing does not exist in
WPF, so 0.07 em tracking is implemented by `TrackedTextBlock`, which lays out a `GlyphRun` with explicit
advance widths; inserting hair spaces is prohibited.

`LineHeight` in WPF is absolute, not a ratio, so the CSS ratios are pre-multiplied into named tokens
(`LineHeight.Snug13` = 1.35 × 13, and so on). The console's 1.55 line height was attempted through a
`CompositeFont` `LineSpacing` wrapper; it was measured not to hold (15.96 DIP at 13 px against a 20.15 target,
~21 % off), so the console keeps the default line height. That measurement is recorded, and the skipped test
that carries it is a legitimate record rather than a gap.

### 14.3 Status vocabulary

Status is always **colour + glyph + text** together — colourblind-safe, and every text tone including dim
meets 4.5:1.

| Status | Glyph | Text |
|---|---|---|
| Discovered | dashed circle | Discovered |
| Queued | clock | Queued |
| Building | rotating dashed ring + breath | Building |
| Succeeded | ✓ in a ring | Succeeded |
| Failed | ✗ in a ring | Failed |
| Skipped | — in a ring | Skipped |
| Cycle | warning triangle + red badge | in dependency cycle |

Two channels are **orthogonal** to status and must not be conflated with it: the will-build dot (§7.4) and the
dependency-issue triangle (§8.3).

### 14.4 Iconography

Lucide geometry, 1.5–2 px stroke, single colour, 12–16 px, authored as XAML geometries. **Never emoji.** The
building spinner is not a separate drawing — it is the discovered node's dashed ring, in amber, rotating
linearly over 1.4 s. The application icon is a multi-size ICO with the 16 and 24 px rasters hand-corrected;
carets and chevrons are drawn, not typed.

### 14.5 Motion

Durations 80 / 120 / 180 / 280 ms; three easings — ease-out for entrances, ease-standard for state changes,
ease-in-out for displacement. All three CSS curves are reproduced exactly as `KeySpline`s. No bounce, no
overshoot; only transform and opacity are animated, never layout.

Four contract rules, each enforced by a test:

1. **One hero at a time.** `MotionCoordinator` is the single gate; the graph frontier and the list frontier
   share one key and therefore count as *one* hero and play together, while any other hero is refused and its
   owner jumps to the end state.
2. **Reduced motion is an OS signal, not an app toggle.** `SystemParameters.ClientAreaAnimation` is tracked
   live; the four `Duration.*` resources are zeroed and restored in place. Pure-XAML storyboards must use
   `DynamicResource` — a `StaticResource` resolves once and would never see the change — and code-driven
   animations must read the setting *at animation start*.
3. **No literals.** Hardcoded hex or millisecond values in animation code fail a guard test.
4. **Frozen brushes cannot be animated.** Shared/frozen resources are copied per instance before being driven;
   `ContainerVisual.Opacity` cannot be animated at all, which is why graph layer hosts are `UIElement`s.

Decorative infinite animations run at `DesiredFrameRate=30`; all counters tick from one `DispatcherTimer`;
timing-sensitive sequences (typewriter, cascade) are `Stopwatch`-based rather than trusting the ~15.6 ms
`DispatcherTimer` resolution. Resetting an observable collection is prohibited — it destroys running
animations.

### 14.6 Copy and tone

All interface text, project names and logs are **English**; code comments and the decision records are Turkish.
The tone is calm, precise, engineering: no exclamation marks, no jokes, exact numbers and exact state —
`Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s`. A guard test fails if Turkish text reaches a
user-visible string.

### 14.7 Prohibitions

Generic SaaS card grids, three-column feature grids, purple/indigo gradients, inflated radii, decorative blobs,
marketing heroes, centred-everything, decorative shadows, emoji as design elements, filled multi-coloured badge
icons, and a rotating decorative globe. Deliberate, thin-ringed, status-coloured glyphs inside circles are the
opposite of that and are encouraged. Toasts and in-app popups do not exist.

---

## 15. Accessibility

Rows are focusable with a tab index; Enter toggles selection; arrow keys navigate. The focus ring is 2 px amber
at 50 % with a 1 px offset. Dialogs trap focus; popovers manage it explicitly. `AutomationProperties.Name` is
set from one central name table so the same element cannot be named two ways, and the ribbon acts as a live
region. Contrast is asserted by test for every text token, including the dim ones.

Known gap: graph nodes are not keyboard-navigable and carry no automation name, so the label level-of-detail's
only fallback is a mouse-hover tooltip.

---

## 16. State on disk

Everything the application persists lives under `%LOCALAPPDATA%\BuildOrchestrator\`:

| Path | Content | Corruption behaviour |
|---|---|---|
| `logs\run-<timestamp>\` | per-run and per-project logs | — |
| `build-state.json` | per-project signature, commit, result, duration | falls back to empty |
| `evaluation-cache.json` | csproj evaluation cache | falls back to empty |
| `ui-state.json` | layout mode + three splits, repository root, configuration, perf mode, branch, worktree choice, layer patterns, hotkey, autostart, tray-balloon-shown | falls back to defaults; a field whose *type* changed between versions is tolerated rather than taking the whole file down |
| `worktrees\` | the worktree pool | LRU pruned to 20 GiB |

Autostart additionally writes one `HKCU\...\Run` value.

The Supervisor accepts `--logs` and `--worktrees` to relocate the log/cache/state root and the pool root. The
App never passes them; they exist so the test suite never touches the user's real data.

---

## 17. Testing

### 17.1 Composition

One test project covers everything, and its folders mirror the source namespaces — `Discovery/`, `Graph/`,
`Incremental/`, `Planning/`, `Scheduling/`, `MsBuild/`, `Git/`, `Logs/`, `State/`, `ProcessControl/`,
`Processes/`, `Ipc/`, `Workspace/`, `Supervisor/`, `Contracts/`, `Integration/` — plus `App/`, which holds
everything WPF: view models, controls, realization, motion, layout, keyboard, accessibility and the source
guards. It targets `net10.0-windows` with `UseWPF` because a meaningful portion realizes real WPF trees on an
STA thread.

Shared test infrastructure lives in one place per concern rather than being copied: resource realization
(`DsResources`, `IconResources`), window and dialog hosts (`MainWindowHost`, `SettingsDialogHost`,
`SplitterHost`, `GraphTestView`), dispatcher pumping and animation hosting (`DispatcherPump`, `AnimationHost`,
`MotionScope`), fixtures (`GitTestRepo`, `LegacyFixture`, `SyntheticGraph`, `JobTestChildren`, `VmTopology`) and measurement
(`PerfMeasure`). Tests that cannot run concurrently declare it explicitly through serial collections — the
CPU-saturating job tests, the console UI tests and the build-state store tests.

Font and resource assets are copied into the test output so that headless tests can load them from disk;
`pack://` URIs do not resolve without an `Application` instance. `App.xaml` itself is copied too, so a test can
assert structurally that it really merges the token and motion dictionaries.

### 17.2 Source guards

A category of tests that assert properties of the *source*, not of a run:

| Guard | Asserts |
|---|---|
| No hardcoded colour | no hex outside `Tokens.xaml` |
| No hardcoded motion | no inline durations/easings outside `Motion.xaml` |
| No sleep-poll | no `Thread.Sleep`-based waiting in tests — synchronization is by handle or signal |
| No Turkish user text | no Turkish string reaches a user-visible surface |
| Token realize coverage | every declared token actually resolves when the resource dictionaries are realized |
| Publish layout | the single-file publish rejection and the supervisor-folder wiring stay in place |
| Anti-slop | the prohibited visual patterns of §14.7 |
| Design token scale | duplicated size tokens stay equal to their single authority |

### 17.3 Determinism

Process-control tests are deterministic by contract: they wait on handles and completion ports, never on
elapsed time. The cascade-kill bound is measured, not assumed. Scheduler tests assert dispatch *sequences*, not
just outcomes.

Responsiveness is a tested contract, not an aspiration. A set of budget tests drives the production surfaces at
the scale of the real repository — a 177-project topology, an inventory of several hundred refs — and asserts
that **no single step blocks the UI thread past its budget**: a Sync step, a project event during a run, the
mid-run graph tick, an inventory publish, opening a popover, resolving Visual Studio. Two of them pin the shape
of the fix rather than the number: the cost of building the list must not scale with the row count, and
republishing an unchanged inventory must emit no notification at all. Wall-clock numbers vary by machine, so
the budgets carry the derivation that produced them and sit far enough above the measurement to survive noise
while still failing an order-of-magnitude regression.

### 17.4 WPF realization tests

A headless suite does not resolve XAML at runtime — a full suite once passed green while the application could
not open at all, because a `Double` token was being fed to a `GridLength` in the shell root. The rule that came
out of that: **any task adding a new XAML root or template also adds a realization test.** A `Window`'s
`Measure`/`Arrange` does not descend into content without an HWND, so realization tests run against
`window.Content`.

Animation behaviour is measured the same way: the harness can drive the live window, take frames through
`PrintWindow(PW_RENDERFULLCONTENT)`, read state through UI Automation and diff pixels between frames — so
"an animation plays" is a testable claim, not a visual impression.

### 17.5 Acceptance

Three tests carry the `Acceptance` category and build the user's real repository end to end (roughly two
minutes). They are excluded from the normal verification run and executed separately:

```powershell
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
dotnet test tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category=Acceptance"
```

Test counts are deliberately not recorded here — run the suite for the current number.

---

## 18. Build, run, publish

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test  tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
dotnet run   --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj
```

Close any running instance before building — a live Supervisor keeps its own binaries locked.

**Publish** is framework-dependent and folder-based:

```powershell
dotnet publish src\BuildOrchestrator.App\BuildOrchestrator.App.csproj `
  -c Release -r win-x64 --self-contained false -o <output-folder>
```

The `supervisor\` subfolder next to the published executable **is** the build engine, not an optional extra.
The App resolves `<app folder>\supervisor\BuildOrchestrator.Supervisor.exe` at startup. Three MSBuild targets
protect this: the supervisor output directory is resolved from the Supervisor project's own `TargetPath`
(never guessed from a TFM/RID glob), the build **fails outright** if that file set is empty rather than
producing an engine-less package, and the same file set is injected into the publish list — because a plain
`Copy` writes only to `OutDir` and would leave publish silently engine-less.

`PublishSingleFile` is rejected by an explicit MSBuild error: `AppContext.BaseDirectory` would point at the
extraction directory and the `supervisor\` subfolder cannot enter the bundle. Self-contained publish is not
verified.

`scripts/verify-publish.ps1` validates a publish output end to end. It refuses to measure anything while an
instance is running, then publishes to a temp folder and runs a series of checks: publish exit code, layout,
an NDJSON round trip against the published Supervisor binary, a full Sync + Build driven through it against a
throwaway workspace (proving the published binary really compiles and writes a DLL), launching the published
executable and confirming through WMI that the Supervisor child came from that same folder, reading the console
boot line and the ribbon state out of the live window via UI Automation, and finally killing only the App and
proving the Supervisor dies by itself through the job cascade. Exit code 0 = pass, 1 = fail, 2 = precondition
not met.

---

## 19. Platform constraints

WPF was chosen over a WebView2 hybrid, Avalonia and WinUI 3. These are the things the platform genuinely cannot
do, and how the interface works around each — useful to know before attempting a change in these areas:

1. **No letter spacing.** Tracked caps labels are drawn as `GlyphRun`s with explicit advance widths (§14.2).
2. **No shadow spread.** `DropShadowEffect` offers offset and blur only, so a two-layer shadow is approximated
   with one.
3. **No compositor.** Animations tick on the UI thread, so "the interface keeps animating while it is busy"
   cannot be guaranteed. The countermeasures are the process split (§4.1) and a hard rule against synchronous
   work on the UI thread.
4. **No per-line transform inside AvalonEdit.** The console cascade keeps its exact tempo but fades lines in
   rather than translating and scaling them.
5. **Frozen resources cannot be animated.** Shared brushes and effects must be copied per instance before being
   driven (§13.8, §14.5).
6. **No native smooth scrolling.** It is built from an attached property, an animator and an arbiter (§13.4).
7. **No toggle switch, no split button, no dashed border.** These are custom templates and controls (§13.8).
8. **OS surfaces cannot be themed** — the folder picker, Explorer and Visual Studio appear in the system's own
   styling.
9. **Tooltips are separate windows** and may extend past the main window or flip at a screen edge.
10. **Text rasterization is DirectWrite's**, so small text will never be bit-identical to a browser's. Rendering
    mode and anti-aliasing were compared on the target monitor before the current settings were fixed.

---

## 20. Known limits

- **One repository at a time.**
- **No build-output isolation between branches.** Only `obj` is isolated, and only in worktree mode; the shared
  `OutDir` is intentionally left alone for Visual Studio parity, so builds of different branches write to the
  same place.
- **The shared-compilation flags cost ~2.9×** and stay off for correctness (§9.2).
- **Filling a viewport of rows costs what it costs.** Virtualization bounds the work to the visible window,
  but that window still has to be built: a screenful of project rows is a few dozen row controls, tens of
  milliseconds on the reference machine. That price is paid again whenever the entry list is replaced — a
  topology change or a filter change — because replacing the items source discards the containers.
- **The graph is full-detail up to 150 nodes.** Above that, off-screen nodes and edges are culled and labels
  drop out by level of detail. On the reference machine, synthetic graphs of 500 and 1000 nodes open in roughly
  90 ms and 136 ms; panning across the whole graph materializes the rest, for roughly 206 ms and 469 ms in
  total.
- **No field-level IPC schema validation** (§5.4).
- **Symlinks/junctions are not followed or detected** during the scan, and a `.csproj` may reference files
  outside the repository root. Both are accepted risks — the repository is trusted by definition.
- **Graph nodes are not keyboard-accessible** (§15).
- **The global hotkey has no settings UI** (§12.3).

---

## 21. Security boundary and threat model

### 21.1 The core statement

**The orchestrator builds a repository the user would open in Visual Studio anyway; the trust boundary is the
repository itself.**

The direct consequence: an arbitrary MSBuild target, `<Exec>` task or pre/post-build event in a `.csproj`
**runs code with the user's own privileges**. `MSBuild.exe` is started as a child process and executes
everything inside that file. This is not a vulnerability, it is the definition of the product — opening the
same repository in Visual Studio would run the same code. The application does not try to restrict that
execution; it only **contains** it (job object) and **throttles** it (CPU cap).

### 21.2 Input surface

| Input | Source | Where it goes | Injection risk |
|---|---|---|---|
| Repository root | folder picker or Settings | working directory of the child process — not an argument | none |
| Project / solution paths | disk scan | MSBuild command line, escaped per MSVCRT rules | none |
| Branch name | branch list, or `ui-state.json` | `git fetch origin <branch>` argv element | theoretical (below) |
| Perf mode | perf chip | ordinal whitelist | none |
| Worktree name | UI / `ui-state.json` | validated as a single safe path segment | none |
| Layer regex | Settings editor | `Regex` constructor with a 100 ms match timeout | ReDoS closed |
| Solution to open | row icon | `devenv "<sln>"` — hand-quoted | theoretical (below) |

Shell injection is structurally absent: arguments are added individually to `ProcessSpec`/`ArgumentList` —
manual string concatenation is prohibited — `UseShellExecute` is false everywhere, and neither `cmd.exe` nor
PowerShell is ever used as an intermediary. With no shell in the path, `&`, `|`, `;` and backticks carry no
meaning. The one place a command line is assembled by hand (MSBuild) escapes according to the
`CreateProcessW`/MSVCRT rules, including the backslash-before-quote counting.

### 21.3 Hardened surfaces

- **User regex** compiles with a 100 ms match timeout; a timing-out pattern is treated as a non-match and
  skipped for the remaining nodes with a warning. An empty or whitespace pattern is made inert rather than
  matching everything.
- **NDJSON line limit** (1 MiB) is enforced on both write and read; log chunks are 64 K, far below it.
- **Branch slug sanitization** replaces path-hostile characters, collapses repeated dashes, and **throws
  rather than falling back** if the result is empty or `.`/`..`. A separate validator rejects absolute paths,
  separators and `..` for any name that becomes a directory segment.
- **Atomic state writes:** `build-state.json` and `evaluation-cache.json` are written to a unique temp name and
  moved into place; readers open with `FileShare.Delete` so they cannot block the rename, which is retried a
  bounded number of times on a transient sharing violation.
- **Corrupt-JSON tolerance:** `build-state.json`, `evaluation-cache.json`, `ui-state.json` and a project's
  `project.assets.json` all fall back to defaults instead of throwing. `ui-state.json` additionally tolerates a
  field whose *type* changed between versions, so one stale token cannot wipe the whole file.
- **Log line normalization:** embedded CR/LF inside one MSBuild line becomes a space, so one appended line is
  always one physical line.
- **Single-instance channel** is a session-scoped mutex plus a named pipe carrying the session id; a busy pipe
  backs off rather than spinning.
- **`debugSpawnChildren` is rejected by default** and only executes when the Supervisor is started with
  `--debug-hooks`, which the App never passes. This is a surface reduction, not a boundary — reaching that
  command already requires holding the Supervisor's stdin.

### 21.4 What the code does not verify

An honest list, kept because omitting it would make the guarantees above read wider than they are:

1. **A `.csproj` `Include` may point outside the repository root.** `ProjectReference`/`Compile` values are
   resolved to full paths without a containment check, so the graph and the signature may treat a file outside
   the repository as a source. Accepted — the repository is trusted (§21.1).
2. **Symlinks and junctions are neither followed deliberately nor detected** during the scan. A self-referential
   junction could produce deep recursion. (The worktree pool does have a separate junction gate before
   `reset --hard`.)
3. **`explorer` and `devenv` arguments are hand-quoted** rather than going through the MSVCRT escaper. A path
   containing a quote would break the escaping; unreachable in practice, since Windows file names cannot
   contain one and the paths come from a disk scan.
4. **A branch name could reach git's argv as an option.** In the UI it can only be chosen from a list, but it is
   also loaded from `ui-state.json`; a hand-edited value beginning with `-` would be passed through as-is,
   with no `--` separator and no pre-validation. Reaching it requires already being inside the user's account.
5. **Supervisor arguments are not validated.** `--logs`, `--worktrees` and `--debug-hooks` are taken raw. The
   App passes none of them; only tests do.
6. **There is no field-level IPC schema validation** (§5.4). A missing field binds to `null` and surfaces at
   the point of use as `planFailed`/`runFailed`.
7. **Manual edits inside a pool worktree are not preserved** — the next reuse resets it. The pool is the
   application's scratch space.
8. **The cascade guarantee has two documented exceptions** (§4.2, §4.4): processes the App starts on the user's
   behalf are in no job by design, and a build step that delegates work to another parent through COM/WMI/task
   scheduler creates a process that never enters the job at all.

### 21.5 Explicitly out of the threat model

This is a developer tool that builds the user's own code on the user's own machine with the user's own
privileges. The following are not defended against, deliberately:

- **A malicious `.csproj`, MSBuild target, `<Exec>` or pre/post-build event** (§21.1). Not sandboxed, not
  inspected, no prompt.
- **Escaping the job object** through COM/DCOM/WMI/task scheduler.
- **A malicious `.sln`, `packages.config` or NuGet package** — `-t:restore` downloads packages and runs their
  build targets; package contents are not inspected.
- **An attacker with local file-system access.** `ui-state.json`, `build-state.json`, `evaluation-cache.json`
  and the autostart registry value are plain text and unsigned. The same person could edit the `.csproj`.
- **Whoever can write to the Supervisor's stdin.** The IPC has no authentication — anonymous pipes inherited
  parent-to-child — so that position is equivalent to being the App.
- **Local privilege escalation.** No admin rights are requested, nothing is written to HKLM, no service is
  installed.
- **The network.** The only network touch is `git fetch`; authentication, TLS and host verification are
  entirely git's own configuration.
- **Multi-user or multi-tenant isolation.** The single-instance gate is per user and session; isolation between
  users is the operating system's job.

---

## 22. Code map

Where a behaviour lives. Paths are relative to `src/`; `Core`, `App`, `Supervisor` and `Contracts` stand for
`BuildOrchestrator.*`.

**Startup and window shell**

| Behaviour | File |
|---|---|
| Composition root, startup routes, second-instance handling | `App/App.xaml.cs` |
| Argument parsing (`--font-ab`, `--autostart`) | `App/Shell/StartupArgs.cs`, `App/Shell/SecondInstanceGate.cs` |
| Window shell, layout wiring, shortcut binding | `App/MainWindow.xaml(.cs)`, `App/ShellRoot.xaml(.cs)` |
| Maximize overflow fix · DWM corners/border · caption glyphs | `App/Shell/MaximizeFix.cs`, `Dwm.cs`, `CaptionGlyphs.cs` |
| Single instance, tray icon, global hotkey, autostart, shutdown | `App/Shell/SingleInstance.cs`, `AppTrayIcon.cs`, `Hotkey.cs`, `App/Services/AutostartService.cs`, `App/Shell/AppShutdown.cs` |
| View mode + splitter persistence | `App/Shell/LayoutState.cs`, `App/Shell/UiStateStore.cs`, `App/Controls/DsSplitter.cs` |
| Keyboard semantics (key → intent, Esc chain) | `App/Shell/KeyboardShortcuts.cs` |
| Default layer definitions (Settings draft + *Restore default layers*) | `App/Shell/LayerDefaults.cs` |
| Title bar context text (`OSYS · main · main-2`) | `App/ViewModels/TitleBarContext.cs` |

**Engine and IPC**

| Behaviour | File |
|---|---|
| Command/event records, JSON options | `Contracts/Ipc/IpcMessages.cs` |
| NDJSON framing, line limit, writer serialization | `Contracts/Ipc/NdjsonFraming.cs` |
| Domain DTOs (`ProjectNode`, `BuildPlan`, `BuildState`, `LayerPattern`…) | `Contracts/Model/ProjectModels.cs` |
| Spawning the engine, generation guard, engine-died signal | `App/Services/EngineHost.cs` |
| Supervisor entry, argument handling, stdout redirect, planner wiring, workspace preparation | `Supervisor/Program.cs` |
| Command dispatch, per-command input gates | `Supervisor/SupervisorHost.cs` |
| Supervisor path resolution from assembly metadata | `App/Services/SupervisorLayout.cs` |

**Discovery, graph and layers**

| Behaviour | File |
|---|---|
| Workspace scan, ignore list | `Core/Discovery/WorkspaceScanner.cs` |
| Raw csproj XML evaluation | `Core/Discovery/CsprojEvaluator.cs` |
| Evaluation cache (mtime + length fingerprint) | `Core/Discovery/EvaluationCache.cs` |
| `.sln` parsing, project↔solution map | `Core/Discovery/SolutionMapper.cs` |
| Stale-`obj` diagnosis, TFM derivation | `Core/Discovery/StaleObjDetector.cs`, `TargetFrameworkMonikerDeriver.cs`, `Supervisor/StaleObjRunStartWarner.cs` |
| DLL name → producing project | `Core/Graph/ProducerMap.cs` |
| Edges (HintPath primary, ProjectReference secondary) | `Core/Graph/GraphBuilder.cs` |
| HintPath four-way classification and metric | `Core/Graph/HintPathClassifier.cs` |
| SCC + topological order | `Core/Graph/TopoSort.cs` |
| Layer assignment, phase barrier, reverse-layer warnings | `Core/Planning/LayerEngine.cs` |
| Full planning pipeline assembly | `Core/Planning/BuildPlanBuilder.cs` |

**Incremental decision**

| Behaviour | File |
|---|---|
| Signature computation | `Core/Incremental/BuildSignature.cs` |
| Propagation, Safe/Fast, SCC composite hash, committed fingerprint | `Core/Incremental/IncrementalPlanner.cs` |
| Path normalization glue for the fingerprint | `Core/Incremental/IncrementalRunBinder.cs` |
| Will-build tri-state decision | `Core/Planning/WillBuildEvaluator.cs`, `Core/Planning/BuildPreview.cs` |
| ETA formula (raw estimate, smoothing, rounding) | `Core/Incremental/EtaCalculator.cs` |
| Build state store, duration persistence | `Core/State/BuildStateStore.cs`, `BuildDurationPersister.cs` |

**Scheduling and run execution**

| Behaviour | File |
|---|---|
| Ready-set dispatch, resolved semantics, cycle pre-skip | `Core/Scheduling/ReadySetScheduler.cs` |
| Dependency-issue propagation | `Core/Scheduling/DepIssueTracker.cs` |
| Continue / RetryFailed set transformation | `Core/Scheduling/RetryPlanning.cs` |
| Run snapshot and elapsed clock across segments | `Core/Scheduling/RunSnapshot.cs`, `RunClock.cs` |
| Bounded synchronous retry (used by state store and clipboard) | `Core/Scheduling/SyncRetry.cs` |
| Worker loop, event pump, stop bookkeeping, perf lifecycle | `Supervisor/RunCoordinator.cs` |
| Per-run and per-project logs, decision log | `Core/Logs/RunLogWriter.cs`, `RunLogPaths.cs`, `ProjectLogNaming.cs` |
| Log chunking for the UI | `Core/Logs/LogChunker.cs` |

**Build execution**

| Behaviour | File |
|---|---|
| `MSBuild.exe` resolution via `vswhere` | `Core/MsBuild/MsBuildResolver.cs` |
| Argument contract (build and restore) | `Core/MsBuild/MsBuildArguments.cs` |
| Invocation, output pumping, per-project kill | `Core/MsBuild/MsBuildInvoker.cs` |
| Copy-contention detection and retry decorator | `Core/MsBuild/CopyContention.cs`, `RetryingMsBuildInvoker.cs` |
| `SolutionDir` resolution for restore | `Core/MsBuild/SolutionDirResolver.cs` |
| Isolated `obj` path derivation | `Core/MsBuild/WorktreeObjPathResolver.cs` |
| Output encoding | `Core/MsBuild/MsBuildOutputEncoding.cs` |
| Process launching, argument list discipline, command-line escaping | `Core/Processes/ProcessRunner.cs`, `WindowsCommandLine.cs` |

**Git and worktrees**

| Behaviour | File |
|---|---|
| All git invocations (HEAD, status, refs, ls-tree, fetch) | `Core/Git/GitService.cs` |
| Command execution wrapper and result shape | `Core/Git/GitCommandExecutor.cs`, `GitMessages.cs` |
| Worktree pool: create, reuse, prune, delete, gates | `Core/Git/WorktreeManager.cs` |
| Branch slug and path segment sanitization | `Core/Git/PathSanitizer.cs` |
| Sync flow (fetch → analysis → events) | `Core/Workspace/SyncWorkspaceService.cs` |
| Planning step texts (shared by Sync and the run planner) | `Core/Planning/PlanProgressLines.cs` |

**Process control and resource governance**

| Behaviour | File |
|---|---|
| Job object: creation, assignment, CPU rate, priority, terminate | `Core/ProcessControl/JobObject.cs`, `NativeMethods.cs` |
| Suspended launch + handle-list inheritance | `Core/ProcessControl/JobProcessLauncher.cs`, `ProcThreadAttributeList.cs`, `JobChildProcess.cs` |
| Job completion port notifications | `Core/ProcessControl/JobCompletionPort.cs` |
| Perf table, copy-phase floor | `Core/ProcessControl/PerfProfile.cs`, `PerfNoteText.cs`, `ICpuGovernor.cs`, `ICopyPhaseCpuFloor.cs` |

**View models — the pure decision cores**

| Behaviour | File |
|---|---|
| Run state, rows, counters, commands | `App/ViewModels/RunViewModel*.cs` |
| Ribbon phase lines and ETA display | `App/ViewModels/RibbonText.cs` |
| Event stream composition and wording | `App/ViewModels/StreamComposer.cs`, `StreamText.cs`, `StreamEventViewModel.cs` |
| Filter rule and chip labels | `App/ViewModels/ProjectFilter.cs` |
| Status counters | `App/ViewModels/RunCounters.cs` |
| Layer grouping (from topology only — no regex in the App) | `App/ViewModels/LayerGrouping.cs` |
| Graph feed construction | `App/ViewModels/GraphBinder.cs` |
| Interaction copy (console notes, empty states) | `App/ViewModels/InteractionText.cs` |
| Settings draft state (layers + pending root) | `App/ViewModels/SettingsDraftViewModel.cs` |
| Inventory publishing (one notification per publish, none when unchanged) | `App/ViewModels/SnapshotCollection.cs` |

**Views and controls**

| Behaviour | File |
|---|---|
| Sticky ribbon: phase, building chips, failure cluster, progress | `App/Views/StickyRibbon.xaml(.cs)` |
| Project row: stripe, dot, sha pair, hover icons, breath, shake | `App/Views/ProjectRow.xaml(.cs)`, `ProjectRowActions.xaml(.cs)` |
| List with cumulative sticky headers and reveal | `App/Controls/StickyLayerList.xaml(.cs)` |
| Row virtualization with an exact (never estimated) extent | `App/Controls/FixedHeightVirtualizingPanel.cs` |
| Event stream rows, glow-once | `App/Views/EventStreamView.xaml(.cs)` |
| Action bar: sync, counters, chips, segment, build split button | `App/Views/ActionBar.xaml(.cs)` |
| Build menu (Build / Rebuild / Retry failed) | `App/Views/BuildMenu.xaml(.cs)` |
| Branch and worktree popovers, shared base | `App/Views/BranchPopover.xaml(.cs)`, `WorktreePopover.xaml(.cs)`, `PopoverBase.cs` |
| Branch popover row (virtualized item container) | `App/Views/BranchRow.cs` |
| Settings dialog, layer drag-reorder | `App/Views/SettingsDialog.xaml(.cs)`, `App/Controls/DragReorderBehavior.cs` |
| DS templates and styles | `App/Resources/Controls.xaml` |
| Status glyph, spinner, will-build dot, split button, chips, tooltip, panel header, pill | `App/Controls/StatusGlyph.cs`, `BuildingSpinner.cs`, `WillBuildDot.cs`, `SplitButton.cs`, `DsChipFactory.cs`, `AppTooltip.cs`, `PanelHeader.xaml(.cs)`, `LatestPill.xaml(.cs)` |
| Letter-spaced caps text | `App/Controls/TrackedTextBlock.cs`, `TrackedGlyphs.cs` |
| Icon geometries | `App/Resources/Icons.xaml`, `App/Controls/IconVisual.cs`, `IconPaint.cs` |

**Console**

| Behaviour | File |
|---|---|
| AvalonEdit host, batching, active line, cascade, chunk paging | `App/Console/ConsoleView.xaml(.cs)` |
| Line colouring | `App/Console/ConsoleColorizer.cs`, `ConsolePalette.cs`, `ConsoleLine.cs` |
| Typewriter and cascade timing (pure) | `App/Console/TypewriterScheduler.cs`, `CascadeScheduler.cs`, `CascadeFadeTransformer.cs` |
| Batching, routing, render slice | `App/Console/ConsoleBatcher.cs`, `ConsoleBatchRouter.cs`, `ConsoleRenderSlice.cs` |
| Chunk stitch and scroll compensation | `App/Console/ChunkStitch.cs` |
| Typing suspension under load | `App/Console/ConsoleTypingGate.cs` |
| Header, empty states, copy log, timestamps | `App/Console/ConsoleHeader.xaml(.cs)`, `ConsoleEmptyState.cs`, `CopyLogFeedback.cs`, `ClipboardRetry.cs`, `WallClockFormat.cs` |

**Graph rendering**

| Behaviour | File |
|---|---|
| Shapes rendering, status tick, selection dimming | `App/Graph/GraphView.xaml(.cs)`, `GraphNodeVisual.cs` |
| Layered layout and label fit | `App/Graph/GraphLayout.cs`, `GraphLabelMetrics.cs` |
| Viewport culling | `App/Graph/GraphCulling.cs` |
| Camera follow and clamping | `App/Graph/GraphCamera.cs` |
| Edge style resolution (colour, dash, flow) | `App/Graph/EdgeStyleResolver.cs` |
| Feed models | `App/Graph/GraphModels.cs`, `GraphStatus.cs` |

**Scroll, motion and tokens**

| Behaviour | File |
|---|---|
| Cumulative layout arithmetic (rows, headers, scroll targets) | `App/Controls/LayoutMetrics.cs` |
| Smooth scrolling, bottom anchor, follow mode | `App/Controls/ScrollAnimator.cs`, `BottomAnchorBehavior.cs`, `BottomAnchorDecision.cs`, `FollowScrollController.cs`, `FollowScrollDecision.cs` |
| Horizontal wheel / touchpad routing and step | `App/Controls/HorizontalWheelScroll.cs`, `App/Shell/Win32.cs` |
| Cross-panel scroll arbitration | `App/Services/ScrollArbiter.cs` |
| Reduced-motion signal and live zeroing | `App/Services/MotionSettings.cs`, `SystemParametersMotionSignal.cs`, `IMotionSettings.cs`, `IMotionSignal.cs` |
| One-hero budget | `App/Services/MotionCoordinator.cs`, `App/Controls/MotionGate.cs` |
| Shared entrance/reveal animations, 120 ms transitions | `App/Controls/PopIn.cs`, `RevealStagger.cs`, `DsTransition.cs`, `MotionTokens.cs` |
| Colour, size, typography tokens · duration and easing tokens | `App/Resources/Tokens.xaml` · `App/Resources/Motion.xaml` |
| OS actions (Explorer, Visual Studio, folder picker) | `App/Services/OsActions.cs` |
| Accessibility names | `App/AccessibilityNames.cs` |

**Reading the map.** A rule of thumb that holds across the code base: where a behaviour has both a *decision*
and its *WPF wiring*, the decision lives in a pure, testable class and the control only applies it. Ribbon
wording, filter rules, scroll arbitration, graph layout and culling, typewriter cadence, keyboard intent and
layer grouping are all decisions; the views are consumers. When a defect concerns *what* the application
decided, look at the pure class; when it concerns *how* it was drawn or animated, look at the view.

---

## 23. Document map

| Document | Role |
|---|---|
| [`README.md`](README.md) | Entry point: what the tool does, requirements, how to build/run/publish, how to use it |
| **`ARCHITECTURE.md`** (this file) | Technical reference: architecture, processes, contracts, algorithms, UI, design system, security boundary, code map |
| [`CLAUDE.md`](CLAUDE.md) | Working conventions for this repository |
| `.claude/` · `.superpowers/` | Historical record of the delivery, kept as written. Superseded by the three documents above; not an authority for the current system |

**Maintenance.** This document describes the system as it is. When behaviour changes, the affected section is
rewritten in place, in the same voice — it does not accumulate a change log, and it does not record which
session made a change. Volatile numbers such as test counts and commit hashes do not belong here at all.
