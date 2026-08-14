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
(`Version`, `InformationalVersion`, `Product`, `Company`, `Copyright`). The informational version carries a
delivery tag so that the value observed at runtime proves the property file is actually wired: the Supervisor
reads it from its own assembly and reports it in `engineReady`, and the App prints it in the console boot line.

That identity is also what the UI displays. `Services/AppIdentity` reads the product name, informational
version and copyright back off the App assembly, and the window title, the title bar caption, the tray tooltip,
the tray balloons and the About hero all draw from it — a guard forbids the product name appearing as a literal
in any App source file. The copyright is read as one string rather than composed from a year and a company,
because a copyright year is not a runtime value.

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
pressing *Build* — there is no separate resume — banking the work is the cheaper trade.

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

Building dependency cycles is not a field but a **mode** — `Cycles` (§8.1). It is written to the wire as
camelCase text like every other enum, so adding a value never shifts the meaning of an older line.
`syncWorkspace` carries no cycle decision at all: its preview always describes a `Build`, and `Build` never
compiles a cycle.

### 5.3 Events

Lifecycle: `engineReady` · `pong` · `error`.
Sync: `syncStarted` · `syncProgress` · `workspaceTopology` · `buildPreview` · `syncCompleted`.
Run: `planProgress` · `runStarted` · `projectStarted` · `projectLog` · `projectSucceeded` · `projectFailed` ·
`projectSkipped` · `cycleRoundStarted` · `cycleCompleted` · `runStopped` · `runCompleted`.
Queries: `branchList` · `worktreeList` · `projectLogChunk`.

`planProgress` is the only run event that precedes `runStarted`; it carries the planning steps of a fresh
segment (§8.6). It stays separate from `syncProgress` because the App treats that one as part of a Sync
transcript, and a run's planning window is not a Sync.

`cycleRoundStarted` is run-level rather than per-project, and it names the group's leader, the round, the cap
and the member count. A strongly-connected component is one build unit whose per-round results are never
published (§8.8), so the round number is the only progress the group itself emits; its members still emit
their own `projectStarted` on every round, because on every round they really are compiling.

`cycleCompleted` follows once a group has an actual verdict — converged, no progress (the same members failed
twice in a row) or the round cap reached — carrying that outcome as camelCase text, the leader's id (the same
representative `cycleRoundStarted` used, so the line stays clickable), the member count, the rounds run, the
last round's failure count and the summed duration of every member across every round. It is never published
for a group cut short by a stop or an unexpected error: neither is evidence that the group cannot converge, and
a later run deserves a real attempt rather than one that starts from a false verdict.

Two per-project results carry a cycle flag of their own, as typed fields rather than as text the App would
have to match: `projectSucceeded.cycleUnsettled` marks a member of a group that ran out of rounds, and
`projectSkipped.cycleUnconverged` is the wire form of the same idea for a skip. The `depIssues` list is
not reused for either — it answers "which dependency failed", and a second meaning would make the `▲ N`
counter and its filter chip count the wrong rows.

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
same plan. Cycle members remain in the plan, flagged `InCycle`. Kahn runs over the *condensation*, so a
component is ordered as a unit and lands where its dependencies put it; nothing needs a phase of its own.
What happens to it at run time depends on the run's mode: a `Cycles` run dispatches the component as a single
work item and compiles it in rounds (§8.2, §8.8); every other mode leaves its members to be pre-skipped by the
scheduler, which is also what keeps the run from deadlocking — their dependents could otherwise never become
ready.

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

A strongly-connected component is one unit of change: each SCC gets **one composite hash** over all members'
own terms plus their outside-the-SCC upstreams, with intra-SCC edges collapsed to a fixed marker to break the
recursion. Members and downstream nodes all read that same value. Without this, a real source change inside a
cycle could fail to reach a dependent outside it, and that dependent would be silently skipped on the next
build.

The composite is also what decides the members themselves. Since they all carry the same value, a component is
either wholly dirty or wholly up to date — members never disagree, and a group whose every member is up to date
is skipped as a group rather than rebuilt on every run.

### 7.4 Will-build tri-state

Before a run — and after every Sync — each project carries `WillBuild` as a tri-state:

| Value | Meaning | Dot |
|---|---|---|
| `true` | dirty; will be built | filled amber |
| `false` | up to date; will be skipped | filled grey |
| `null` | no meaningful baseline yet (pre-Sync, or the signature could not be computed) | hollow ring |

If there is no usable HEAD, *every* node is hollow and the counters are not reported at all — printing zeros
would assert "everything is up to date", which is a different and false claim.

A cycle member is evaluated by the same three rules; what it evaluates is the component's composite signature
(§7.3), so a group's dots move together. The run's scope is the one short circuit: outside a `Cycles` run every
member reads `false`, which is the truth — nothing in that run will compile them. Idle dots, which come from
Sync, therefore always describe a `Build`.

During a run the dot is live: the moment a project succeeds, its dot turns grey.

### 7.5 Build state

`build-state.json` is **global**, keyed by project id (the full csproj path), holding the built signature, the
built commit, the last result, the last run timestamp, the last branch, the last duration and the signature at
which this project's cycle last failed to converge (§8.8). That last field is deliberately *not* folded into
the built signature: the built signature means "this was compiled successfully", and Fast mode reads it as a
frozen upstream baseline — a signature that was never built would be taken for a clean one. It is written by
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
| `Cycles` | the projects in a dependency cycle **and their transitive upstream**, the cycles compiled in rounds (§8.8); everything else is pre-skipped as `skipped — not needed by a dependency cycle` |

`Cycles` is not a degree of difference from the others but a separate job: `Build` and `Rebuild` never compile
a cycle, `Cycles` compiles the cycles. It is the third icon of the maintenance box in the action bar (§13.2)
and is meant to be run before a build, not instead of one.

**Resuming and retrying are not modes.** A stopped run is not resumed and a failed run is not retried by a
separate command: in both cases the user presses *Build* again, and the incremental decision produces exactly
the set the old modes produced. Projects that finished green persisted their signature and are skipped as up
to date; projects that were killed or failed had their stored state invalidated (§7.5) and stay dirty; the
dependents of a failure succeeded carrying a dependency issue, so their signature was never persisted (§7.6)
and they come along too. The one deliberate difference is the elapsed clock: the new run counts from zero,
because it is a new run.

The projects that fall out of scope this way are not announced one at a time in the event stream — a
workspace with hundreds of unrelated projects would turn a `Cycles` run into scope-only noise — they collapse
into a single line, `N outside cycle scope — skipped`. `decision.log` still records each one under its own
name; only the live stream collapses them.

**Why the scope reaches upstream.** A member compiled against a *dirty* dependency's previous-generation DLL
comes back green while its output is stale — and the run then persists that member's signature. Because the
signature already contains the upstream's source term, the next `Build` reads the member as up to date and
never recompiles it: the project stays linked to a stale binary, permanently, with no second mechanism to
catch it since no DLL or `bin` timestamp is ever read (§4). Pulling the transitive upstream into scope closes
that: the run is self-consistent, compiling everything it compiles against fresh inputs. Inside the scope the
ordinary incremental rule applies, so a clean upstream is still skipped as `skipped — up to date`.

**Why the scope stops there.** Downstream is deliberately excluded. A cycle's dependents may well need
recompiling once the group has moved, but that is `Build`'s job and `Build` is the next thing the user
presses. Including them would quietly widen the scope to the whole repository — the dependent set of a core
library is, in practice, everything — which is exactly the cost the separate button exists to keep visible.

Why it is separate rather than folded into `Build`: a group's cost is members × rounds, which next to an
ordinary incremental build is unbounded. Folded in, the user waited behind work they had not asked for and
could not see — a two-minute build measured fifteen. As its own button the decision is theirs: when, and how
much.

Like `Build`, a `Cycles` run is incremental — a group whose composite signature is already clean is skipped as
`skipped — up to date`, so pressing the button again after a group has converged costs nothing. It is also the
only mode that reads the non-convergence memory (§8.8).

### 8.2 Ready-set scheduler

When a worker slot frees, the scheduler dispatches the ready project that comes **first in build order** —
never a random or hash-ordered one — and *skips forward* over projects whose dependencies are not yet resolved
rather than waiting on them. The same graph and the same completion order always produce the same dispatch
sequence.

A dependency counts as resolved when it is `Succeeded`, `Failed` **or** `Skipped`. Failure does not block
dependents; a single failure must not stall a run forever. Clean projects are skipped in waves as their
dependencies resolve, not all at once.

**A strongly-connected component is one work item.** Given the component map, the scheduler stops treating
members individually: readiness is asked of the group — every member's dependencies *outside* the group must
be resolved, and the intra-group edges, circular by definition, are excluded, since waiting on them would mean
waiting forever. One dispatch then hands back a single id — the first member still to be built, which stands
for the group — and marks every remaining member in flight at once. The caller owes exactly one `Complete` per
member it was given, on every path including stop and cancellation, or the run's in-flight count never returns
to zero. Driving the rounds from here rather than beside the scheduler is what keeps "are the dependencies
terminal?" in one place instead of two.

Without the component map — which is how the scheduler is built in every mode but `Cycles` — members are marked
`Skipped` at construction with the reason `in dependency cycle`. Nothing else distinguishes the two modes:
there is no code path written for cycles being out of scope, the mode only chooses between passing the map and
passing nothing.

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

That slot has three other tenants, all about cycles: a member of a group that ran out of rounds borrows the
same triangle with its own tooltip, a member of a group this run could not converge replaces it with an orange
cycle badge, and plain membership of a cycle takes that same badge once the row's status glyph has stopped
carrying it (§14.3). A row shows exactly one of the four, never two; membership is the weakest of them and
loses to all three. Membership also names the loop itself: the tooltip's second line is the cycle path,
`Domain.Parts → Parts.Inventory → Parts.Api → Domain.Parts`, closed back on its first member so it reads as a
cycle rather than a chain. The dot carries the same two lines, and the ribbon's cycle cluster carries one line
per cycle; all of them compose from a single place, so no surface can drift into its own wording.

### 8.4 ETA

`(sum of duration estimates for queued projects + remaining time of in-flight projects) / parallelism`, plus
400 ms when anything is building, plus the cycle members' estimates multiplied by the baseline round count.
The result is exponentially smoothed (`0.75 × previous + 0.25 × new`), displayed rounded to 5 s, and
replaced by `· almost done` below 4 s. The per-project estimate comes from `BuildState.LastDurationMs`; with
no history the ribbon shows progress and elapsed time without an estimate.

Cycle members are the one term that is **not** divided by parallelism: their work is sequential by
construction and the group runs at least twice (§8.8), so both assumptions the division encodes are false for
them. A member counts in that term from the moment it is planned until its group is finished — while the group
runs as well, not only while it is queued — because intermediate rounds are never published (§8.8) and a
member's elapsed time within one round says nothing about how much of the group is left. Entering a third
round shifts the estimate once more, which is accepted — the ceiling is low enough that the drift is bounded.

Every component lands in that single undivided term, even though independent components genuinely do run on
different workers at the same time. The estimate is therefore pessimistic in exactly one direction whenever a
run contains more than one cycle. That is deliberate: cycles are rare and small next to the rest of a run, and
an ETA that runs long is a better failure than one that promises an early finish.

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

**Planning reports itself.** The planner takes a progress channel and emits a line per step; the coordinator
turns each into a `planProgress` event on the same FIFO channel as everything else, so they all reach the App
before `runStarted`. Lines that mark work about to begin — worktree preparation, the incremental pass — are
written *before* it, because those are the long steps and they produce no count of their own; lines that report
a count are written after the step that produced it. Every run plans, so every run has these lines.

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

**Cycle rounds.** These run in one mode only — `Cycles` (§8.1), the third icon of the maintenance box. While
such a run is in flight the ribbon reads `▸ Resolving cycles · round R/K · n/m · elapsed` with the amber
building glyph, and before the first round starts (while the cycle's stale upstream compiles) it says
`preparing dependencies` instead. The numbers are the engine's: the round policy decides how many rounds a
group needs, and the interface reports that rather than promising a fixed count. A worker that is
handed a strongly-connected component runs the whole group. Every member is
invoked in build order and **one at a time** — never concurrently, because one member reads the DLL another is
in the middle of writing — and then the whole set is invoked again. Each member's log file is opened once and
kept open for every round: opening it per round would truncate the previous rounds away and restart the line
numbers.

The stopping rule is a pure function in Core, given the round number, this round's failing members and the
previous round's. Two consecutive all-clean rounds mean **converged**; the identical failure *set* twice means
**no progress**; the round ceiling means **cap reached**; anything else means another round. The baseline is
two rounds because the source does not change between them: the first round settles every member's public API,
the second recompiles everyone against those settled APIs. One clean round is not evidence — a member compiled
in the first round was compiled against the previous generation's DLL, so it can bind to a method that no
longer exists and fail at run time rather than at compile time; breaking that silence is the point of the
second round. The comparison is on the set and not its size, since `{A,C}` followed by `{B,D}` is oscillation,
not progress. The ceiling is three, which is what the rule needs: a group that is clean twice converges at
two, a group that fails identically twice stops at two, and only the "failed, then recovered" branch reaches a
third. A low ceiling loses nothing, because rounds are idempotent against what is on disk and the next `Build`
picks up where this one left off.

**Intermediate rounds are not published.** A member gets no `projectSucceeded`/`projectFailed` until the group
is finished, and then exactly one, carrying the **sum** of its rounds as the duration — the real cost, not the
last round's. Publishing per round would send progress backwards, a project going from succeeded back to
building, and would give the same project two result lines in the event stream. `projectStarted` is still
emitted every round, because the project really is compiling, and `cycleRoundStarted` announces the round
itself (§5.3). Those starts accumulate — with no intermediate results, a member stays started for the whole
life of the group — so the App reads only the most recent start *within a component* as actually compiling and
counts the rest of the component as still queued. Without that, a 32-member component would report 32
projects building on a four-worker run.

**A group that did not converge persists nothing.** Only `Converged` is trusted: on no-progress, on the
ceiling, on a stop, on cancellation and on an unexpected exception, every member is invalidated — including
members that came back green — and a group cut short reports every member as failed rather than carrying an
intermediate round's verdict out. Stop is deliberately asymmetric here: a single in-flight project is allowed
to finish and persist, but a group is cut at the end of the round it is in, because the unit of work is all of
the rounds, and continuing them after a stop would mean dozens more invocations.

**Non-convergence memory.** A group that ends in **no progress** records the composite signature it gave up
at, per member, beside that member's build state (§7.5). A stop or an unexpected error never writes this —
neither is evidence that a cycle cannot converge. This is the same principle as every other incremental
decision, driven by the source signature; no DLL or `bin` timestamp is consulted. A member with no state row
at all gets one created for the purpose, otherwise the very case this solves — a component that has never been
built successfully — would never accumulate a memory. Failing to write it warns and nothing more.

**The memory reports; it does not block.** A later `Cycles` run that computes the same signature writes
`cycle {leader}: retrying — did not converge at this signature` to the decision log and then gives the group a
full attempt from round one. It once pre-skipped the whole group instead, to avoid spending rounds on a
guaranteed red, and that was wrong for a single reason: the only way into a `Cycles` run is the user pressing
**Resolve cycles**, so the saving could only ever be taken by swallowing an explicit command, and the button
appeared to do nothing. The signature also covers sources alone — a package restore, an output from outside
the cycle or the environment may well have changed — so refusing a retry on an unchanged source signature
claims more than the evidence supports. Hitting the ceiling is not recorded at all, by the same standard of
evidence: no progress means the identical set failed twice, which is proof that more rounds cannot help, while
the ceiling means the group was still moving when the budget ran out.

Reaching any real verdict clears the memory, at the same place that writes it — convergence and the ceiling
alike, so a stale record from an earlier stuck run cannot outlive the evidence for it. Converged members would
lose it anyway as a side effect of persisting a fresh build state; the explicit clear is what keeps that from
being load-bearing. Within a `Cycles` run no member can reach
that state — a dependency issue needs a *failed* dependency (§8.3) and nothing outside the group is built —
but the clear belongs to the memory's own writer either way rather than to a side effect somewhere else.

Which member's signature stands for the group is decided in one place for both the writing and the reading
side, since the two hold the component in different orders. In the mode the App actually sends every member
carries the same composite signature, so the choice is immaterial there; under frozen-upstream evaluation
members share no composite at all and this memory is simply inert.

Whichever verdict a group ends on, it is named in `decision.log` — the per-member failure lines alone cannot
tell an operator whether a group hit the ceiling, stopped making progress or converged, and the line carries
the remembered signature when one was written. The same verdict also reaches the App, as `cycleCompleted`
(§5.3), so it shows up in the event stream instead of only on disk.

Members that survive to the ceiling are reported as succeeded but flagged as unsettled, because two clean
rounds were never observed and their output may be one generation stale (§14.3).

**Stop bookkeeping.** If a stop was acknowledged, writing `runStopped` is a debt that must be paid even when
the run never reached `runStarted` (a stop pressed during a multi-second planning window) — otherwise the App
would wait for an event that never comes. The run slot, the stop state and the whole perf state (applied cap,
pending intent, copy-floor depth, drain flag) are reset in one critical section, because the IPC loop runs on
another thread and `setPerfMode` has no run-state precondition: an intent arriving in that window would
otherwise leak into the next run.

Nothing survives the run: the plan, the log writer, the resolved worktree obj root and the dependency-issue
tally are all cleared when it ends. There is no second segment to hand them to — every run plans for itself
and reads what it needs from the persisted build state.

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
  edge when maximized. It is driven by a `WindowState` dependency-property watcher rather than the `StateChanged`
  event, for the same reason as the caption glyph below: the window is *born* maximized, and WPF never raises
  `StateChanged` for a state set before the HWND exists — an event-driven correction would simply never run on
  the first launch. The same single application point is re-run on `DpiChanged`, because the correction is a
  device-pixel frame width whose DIP value depends on the scale, and moving a maximized window to a differently
  scaled monitor changes the scale without changing the state.
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
conflict disables it silently; the tray icon still restores the window. There is no UI for changing it yet,
but the loss is no longer invisible: the About screen marks that shortcut row *unavailable* when the
registration did not take.

Autostart writes to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. No admin rights, no HKLM, no service.

### 12.4 Layout modes and persistence

The title bar opens with a **logo lock**: the product mark at 19 px in full colour, the product name, a
hairline, then the company logo at 10 px and 55 % opacity, and finally the mono repository context. The
hierarchy is the point — product ahead and vivid, company behind and quiet. Its application commands sit at
the other end, between the context text and the caption buttons, in decreasing order of use: the three
view-mode toggles, a hairline separator, then the gear (Settings) and the `i` (About).

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
│ TITLE BAR 40px — logo · title · OSYS · main [· main-2]  ⊞ ≡ ▣ ⚙ i — □ ×│
├──────────────────────────────────────────────────────────────────────┤
│ STICKY RIBBON 32px — phase · building chips · failure cluster        │
│ global progress 2px                                                  │
├────────────────────────────────┬─────────────────────────────────────┤
│ DEPENDENCY GRAPH               │ CONSOLE                             │
│ ═══════ horizontal splitter ═══│═══════ horizontal splitter ═════════│
│ PROJECTS                       │ EVENT STREAM                        │
├────────────────────────────────┴─────────────────────────────────────┤
│ ACTION BAR 42px — Sync · maintenance box · counters · branch ·        │
│                   cfg · perf ·                            Build ▴     │
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

**Projects list.** 36 px rows: a 2 px status stripe (3 px and amber when selected) running the row's full
height, the 8 px will-build dot,
the project name with the solution name beside it, then a right-aligned block — on hover, *Reveal in Explorer*
and *Open in Visual Studio* icons; without hover, `curSha → targetSha` for dirty projects — then the status
glyph, the fixed dependency-issue slot, and a 46 px duration column. The building row carries a motionless
amber "breath" (an `amber-soft` layer at 0 → 0.32 → 0 opacity over 3.8 s); a sweep or a shine was tried and
rejected. A failing row shakes once, ±3 px over 360 ms.

The stripe has **no vertical inset**, which is a deliberate departure from §2.4. The design insets it by 1 px
so that adjacent rows cannot fuse into one unbroken rail; looked at on screen, the break made the same 2 px
read lighter and the row read thin. Separation is left to the horizontal divider instead — every row already
carries a `border-subtle` line along its bottom, and that line crosses the stripe and breaks it on its own.

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
cost more than it saves here — each row owns animation state (a done line glows
once), and recycling containers would swap the model underneath a running animation. Events arriving less than
340 ms apart and all error events print instantly. Rows for projects are clickable and
participate in the shared selection. A run that finishes with zero failures glows its done line once
(`success-soft` → transparent over 1.1 s) — that is the *entire* success flourish; there is no green wave
through the list or the graph.

**Action bar.** Sync; the maintenance box; the counter chips, each a filter toggle. Five of them are always
there (`Σ`, building, `✓`, `✗`, `—`); two more appear **only when the list actually holds one** — `⚠` for cycle
membership and `▲` for dependency-affected. Both describe exceptional situations, and carrying them permanently
as empty grey chips weakened the signal. Pressing a filter also drops the selection: a selection locks the
graph camera onto one node, a filter says "look at this set", and the two fought each other. A filter reaches
the **graph** too — nodes outside the visible set fade to the same 0.1 the unfocused set uses. The matching
rule lives in one place (`ProjectFilter.Matches`): the graph is handed the list's visible names and never
writes a second matcher, so the chip, the list and the graph can never disagree.
The remaining bar carries;
the branch
chip (searchable popover); the worktree chip; the `Debug | Release` segment; the perf chip; and the Build
split-button, whose menu carries exactly two items in every phase: *Build — Only stale projects* and
*Rebuild — All N projects — cache ignored*. There is no *Continue* and no *Retry failed*: a stopped run is
started again and a failed one is built again, and *Build* already covers both sets (§8.1). While a run is in flight the primary button becomes *Stop*, and the
branch, worktree and configuration controls lock; the perf chip stays live.

**The maintenance box.** Three icon buttons in one chip-weight box — *Clean* (eraser), *Optimize* (gauge) and
*Resolve cycles* (unlink) — 24px tall, `surface-raised`, one hairline border, `radius-xs`, clipped, with a
1px×14 divider between the buttons. The buttons carry no label: three labelled buttons overflow the bar at its
1240px minimum and crush the Build split-button, so the meaning lives in the tooltip. *Clean* and *Optimize*
have no engine behind them yet; they stay visibly disabled and their tooltips say so rather than doing nothing
when pressed. *Resolve cycles* is the cycle run, disabled while the topology has no cycle and drawn in the
cycle orange when it has one — the same orange the list and the graph use for the structural channel.

The box sits next to Sync rather than next to Build, and the placement carries the meaning: these are things
you do *before* a build, and the separator on their right belongs to the counters. Beside Build it would read
as a variant of the primary action, which it is not — it is a run of its own (§8.1) with the same icon the
rows and the graph use for "this project is in a cycle". It is disabled unless the workspace actually has one,
because in a workspace without cycles that run would skip every project and do nothing; a disabled button says
so before the click rather than after. Its tooltip carries the same fact in numbers once there is one to
report — `Build dependency cycles — N cycles · M projects` — and falls back to the plain label when the
workspace has none. The accessible name is unaffected either way: it stays the plain label, since a screen
reader announces what the control does, not a count that moves under it on every Sync.

**No run without a topology.** *Build*, *Rebuild* and *Resolve cycles* stay disabled until a Sync has published a
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
*Change…*). Building dependency cycles is **not** a setting: it is a run of its own, reached from the Cycles
button beside Sync (§8.1, §13.2). A preference would have been the wrong shape — the question is not "should
this tool ever build cycles" but "do I want to pay for it right now", and that is answered per run.

Layer cards are 36 px and reordered by dragging the grip with `Mouse.Capture` and a half-row swap
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
then the pending repository root (which resets the project rows to
hollow), then exactly one Sync is sent. The order is load-bearing, because the Sync command carries the
layer patterns — sent before they were applied, it would carry stale ones and the idle dots
would be wrong for a whole Sync. The Sync itself is unconditional: Save does not compare old and new state to
decide whether to run it.

Three gates hold. While a run is in flight the layer patterns are applied but the repository
root is left alone and no Sync is sent, since pulling the root out from under a running build would be wrong;
because the dialog's label has already confirmed the picked folder, a root change this gate drops is announced
in the console as `Repository change deferred — run in flight`, while a Save that carries no root change stays
silent.
If no repository has ever been selected, there is nothing to Sync — that gate sits *after* the root is
applied, since the headline journey (a new user opens Settings, picks the root, saves) fills the root right
there. And when the engine is unavailable — the supervisor was never found, or would not launch — the layers
and the root are applied but nothing is sent: each send would fail and print an error line contradicting the
permanent ribbon message, the same reason Sync, Build and Rebuild are disabled in
that state. The root is still applied because it is local state that persists, and the first Sync after the
engine returns carries it.

The shell's own *Choose Folder* invitation, shown before any repository is selected, does not go through this
dialog: it has no Save step, so the folder it picks applies immediately — the root changes, the project rows
reset to hollow, and a Sync starts right away.

The About dialog is the second modal and reuses that shell: the same full-bleed scrim, the same 620 px
`Ds.Dialog`, the same focus trap, the same Esc-and-scrim dismissal. It adds an entrance the Settings dialog
does not have — a 180 ms fade with a 6 px rise, the duration read from the `Duration.Base` token, snapping to
the end state under reduced motion.

It has no title row. In its place is an identity block that holds both marks in one composition: the product
mark at 30 px, the product name, the one-line description, and a single mono line carrying the application
version and the copyright. The company lock sits opposite — a hairline, a tracked `LICENSED TO` label, and the
company logo — and drops out entirely when there is no company logo. The version appears **once**; the engine's
version belongs to the Environment tab, and repeating it in the heading was noise.

The body is tabbed rather than one long scroll, because the things it carries — keyboard shortcuts,
environment, third-party notices — have nothing to say to each other. The tab switch is `Ds.Segment`, the same
component the action bar uses for Debug/Release, so no new interaction pattern enters the design system. The
content area carries a **minimum** height: switching tabs must not move the footer, and an Auto row would make
the dialog jump between a six-row and a ten-row tab.

Everything the dialog shows is bound from somewhere else — identity from the assembly, the shortcut rows from
the same table the window binds its keys from, the environment rows from the diagnostics model, the notices
from the third-party table. It composes no text of its own. `MSBuild.exe` resolution is the one asynchronous
value: `vswhere` is a child process, so it runs when the Environment tab is first selected, not when the
dialog opens, and the row reads `resolving…` until it lands. *Copy diagnostics* prepends the product and
version to those rows so a pasted report says what it came from, and confirms with the check icon and the
success tone for the same 1.4 s the console's copy button uses.

**Both modals can be open at once, and About is always the upper one.** It is declared after Settings, so the
z-order follows the markup. `F1` toggles it and does so even while Settings is open: Esc closes the topmost
layer first, which means About goes and the Settings draft stays untouched. An earlier rule deafened `F1`
whenever any dialog was open — the key is a window-level `InputBinding` and fires regardless of the Settings
focus trap, so the fear was that it would discard an unsaved draft. Layering answers that better than silence
did. The gear still no-ops while anything is open, which costs nothing: under the scrim it cannot be clicked
anyway.

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
- **A line is only text.** There is no wall-clock column and no `▸` marker: every line starts at the same left
  edge as the caret and the line's kind is carried by colour alone. A real run streams hundreds of lines a
  second and a stamp on each of them carried no information; time lives in one place, the event stream and the
  ribbon's elapsed counter.
- **Nothing is typed.** Live lines print immediately. The only live thing in the console is the prompt line at
  the bottom: a 7 × 13 px rectangle blinking at 1.1 s (not a font glyph), amber like the event stream's active
  line, with `ready` beside it while idle. The line is unconditional — output empties its text, not the line —
  so the caret stays put and new lines pile up above it. The editor reserves one full line of bottom padding,
  measured from the text view's own line height, so the caret sits below the last line instead of on top of
  it; it hides while the reader is scrolled away from the bottom, alongside the `⌄ latest` pill, since it is
  pinned to the panel rather than to the document.
- **A panel that was scrolled away comes back on its own.** Three seconds after the last scroll — the same
  idle window the list's frontier following uses, and the same constant — the console and the event stream
  return to the bottom and resume following. The console does not do this in project-log mode: there is no
  live stream to follow there and the reader is looking at a log.
- **Panel transitions are one piece.** Opening a project log and coming back with `← Back` both settle the
  content down from its bottom edge over 340 ms — a hinge, not a per-line cascade — so a three-line log and a
  two-hundred-line narrative open at the same rhythm. `perspective`/`rotateX` do not exist in WPF; the nearest
  native equivalent (a bottom-anchored Y scale plus a translate) carries the same gesture.
- The console body is drawn at **Geist Mono 300**; dense output scans more easily at the lighter weight. Every
  other mono surface stays at 400.
- The console formats text in **Ideal** mode, overriding the window's `Display` (§14.2). Display rounds every
  glyph advance to a whole pixel; Geist Mono advances 7.2 px at 12 px, so it rounds to 7 and the line comes out
  2.8 % narrow with the rounding error spread unevenly between characters. On a monospace grid the cost is not
  only width but alignment. The bottom padding is wider than the top so the caret, which sits on the document's
  last line, is not flush against the horizontal scrollbar when one appears.

### 13.6 Graph renderer

The panel is a **quiet graph**: unnamed mini nodes in layer bands, no permanent edge network, and a camera
that only ever moves when you select something. The point is that a 100-project workspace should read at a
glance instead of demanding to be studied.

**Layout is a function of the panel.** Nodes sit in horizontal bands ordered by build sequence — layer 0 on
top, and inside a band the first project to build sits leftmost — so reading top-down and left-to-right is
reading the build order. The node pitch is searched, not fixed: `QuietGraphLayout` walks from 44 px down to
5 px in half-pixel steps and takes the first value where every band, plus a `0.7 × pitch` gap between bands,
fits the panel height. A band whose last row is short is centred against the rows above it, and the whole
block is centred in the content box — a symmetric 36 px inset on every side, which is what makes the graph
read as a picture with a margin rather than as a panel that has been filled to the edges. That inset is a
single source: the overlay layer clamps to it as well, so a label never ends up hugging a corner. The consequence is
that the graph always fits — there is no scrollbar, and no canvas larger than the panel. A node is a square
of `pitch × 0.6`, clamped to 8–24 px, with a 4 px radius, a 1.5 px border and a Lucide `box` glyph at 52 % of
its edge; discovered nodes get a dashed frame, drawn as a `Rectangle` because a WPF `Border` cannot be dashed.
Under the status square sits an opaque base in the panel's own colour: the status fill is only 12 % alpha, so
without it a selection edge passing behind a node would show straight through it.

**Cycle membership does not get its own status colour here.** A node whose project sits in a
strongly-connected component paints its status square exactly like any other node at that status —
discovered, queued, building or a result — and carries a small persistent corner badge instead
(`Icon.StatusCycle`, `Brush.StatusCycleText`, 40 % of the node edge, top-right, living inside `Body` so it
inherits the run-phase opacity and the hover scale with no wiring of its own). The badge is built once, on
demand, the first time a node is seen in a cycle, and after that it is only ever hidden, never torn down — the
same lazy-build, never-teardown pattern the bead orbit already uses. It shows through every status a run
carries a member across, which is the point: painting the whole square in the cycle's own orange family
(`Brush.StatusCycle`/…Soft/…Border, §14.1) only while the node's status itself was `Cycle` made membership
disappear the instant a run gave the node any other status — visible before a run and invisible during and
after the one a viewer most wants to see it in. The list row keeps its own separate cycle glyph and
dependency-slot badge (§8.3, §14.3); this corner mark is graph-only.

The node's cell is deliberately **larger than the node** — by whichever overhangs further, the selection ring
or the bead orbit. WPF clips a child to its arrange slot, and everything that reaches outside the square lives
in that cell: with a cell exactly the size of the node the ring's straight edges were clipped away entirely
and only the corner arcs, which curve back inside the clip rectangle, survived. The clickable body stays at
node size, because growing it would put the hit area over the neighbours at a tight pitch. The ring itself is
the CSS `outline: 2px solid; outline-offset: 2` translated honestly: WPF draws a stroke *inside* the
rectangle, so the rectangle is a full pen wider than the offset alone would suggest. Because the layout depends on the panel, `SizeChanged` recomputes it and
updates the visuals **in place** — a splitter drag delivers dozens of size events per second, and rebuilding
hundreds of nodes on each one would freeze the panel it is resizing.

The event stream keeps a prompt of its own. When there is nothing left to write the active line does not
disappear: it stays as a wall-clock stamp and a blinking dim caret — the console prompt's twin, and the
stream's way of saying it is still here. It is the same row, recoloured, rather than a second one; an empty
stream shows the empty-state text instead.

**The node's core is the plan channel.** The glyph inside the square answers "what will happen to this
project" while the border answers "what happened in this run": amber when it will be built, grey when it is up
to date, and permanently orange for a member of a cycle. Being **queued is not a result** and does not take the
core over — it used to, and the cost showed at the moment of pressing Build: the amber cores of everything
planned turned grey at once (colour changes are instant here) while the graph dimmed, so the only coloured
thing on screen vanished in the same frame and read as a flash. Only a real outcome — building, succeeded,
failed, skipped — takes the core.

**Entering a run dims before it repaints.** Colour and border changes are instant here (measured deviation,
below), so pressing Build used to land the dashed-to-solid switch of every planned node in the same frame the
graph began to fade — the change was seen at full brightness and the fade arrived after it, which read as
"the ones about to build appeared, then everything went out". Status pushes are therefore held for the length
of the fade and applied once it finishes; only the last one is kept, since the intermediate states were never
visible anyway. Planning takes seconds, so nothing real is delayed by it.

**The run is told with opacity, not with edges.** Idle, everything is fully opaque. Once a run starts the
graph quietens: queued and discovered nodes drop to 0.13 and only the projects actually building stay
bright. A project that reaches a result returns to its result colour, holds bright for 1400 ms, then fades to
0.2 over 700 ms — in CSS that is a delayed transition, and the WPF equivalent is one shot of an animation with
three key frames — bright, still bright, then the result value — so there is no timer and no extra render
pass. The hold is written out rather than left to the node's previous value, because a node can arrive *dim*:
status pushes land every 200 ms, so a fast project can appear as `queued → succeeded` inside one tick and
something has to lift it to bright. That is also why the hold starts on the edge *into* a **work result**
rather than on the edge out of `Building`. Being skipped is not a work result: a skipped node stays at the
same 0.13 as the queue and so makes no move at all. It used to land on the finished value of 0.2, which reads
as a 54 % *brighten* from where a queued node sits — starting a `Cycles` run, the projects outside the cycle's
scope dimmed to 0.13 and then flared back to 0.2 as their pre-skips arrived, and dozens of nodes doing that in
sequence read as a flicker. Later ticks find the value already settled and start nothing, which matters because status pushes
arrive several times a second. When the run ends every node comes back to full opacity in its result
colour. The decision itself is pure (`GraphNodeOpacity.Resolve`) and its precedence is fixed: selection beats
a filter, a filter beats the run, and hover beats all three.

A filter (or a search) in the list dims the graph the same way a selection does — the names that survive the
list's own `ProjectFilter.Matches` stay opaque and everything else drops to 0.1, with the matching set handed
to the graph rather than recomputed there, so the two surfaces can never disagree about what matches. That
fade runs at 420 ms rather than the 280 ms a run tick uses, in both directions. The difference is deliberate:
a run transition reports a state change and happens several times a second, while a filter is the user's own
one-off gesture that dims half the graph at once and wants to be followed by eye.

*One rule of the design is deliberately not implemented:* colour changes are instant rather than a 380 ms
transition. A brush property cannot be interpolated in WPF, so the transition needs a local
`SolidColorBrush` per surface per node. That was built and measured: with 177 projects changing status in a
single tick it costs three brushes and three colour animations each, taking the tick from 11 ms to 51 ms and
breaking the UI event budget. Spending most of that budget on a colour glide across an 8–24 px square whose
opacity is already animating is not defensible, and the budget is not negotiable.

**Building is a bead orbit.** A project under construction carries dense amber dots circling a rounded-square
track 2.8 px outside its node. The dots are a stroke dash pattern whose step divides the perimeter a whole
number of times, so the pattern does not overlap itself where it closes; the orbit turns once every 4200 ms.
Every orbit in the graph hangs off **one** shared animation clock — the node size is graph-wide, so the
perimeter is too, and N parallel builds would otherwise mean N infinite animations. The orbit fades in over
420 ms and out over 640 ms, and the clock is released 700 ms after the last node stops building, so the dots
fade *while still turning* rather than freezing in place. Resizing the panel changes the perimeter, so the
pattern and the clock are rebuilt.

**A skipped project is silent.** No orbit, no bright hold, no wave — it settles into its result colour and
stays exactly as dim as the queue around it. An earlier version gave skipping the full announcement (a brief
orbit, a short hold, and a 45 ms-per-node wave in build order) on the argument that the incremental check did
run and found the project current. Looking at it settled the question the other way: a grey node with an amber
orbit around it says *working* and *skipped* at once, and the most common run in this tool is the one where
nothing changed, so the whole graph stirred for seconds on every press. A quiet graph reports what *changed*,
and being skipped is precisely nothing changing; the fact is already in the row's will-build dot, the ribbon
counter and the console. Repainting the square amber for a moment would be worse still — it would state a
status the project never had, and the colour transition has a measured price of its own (above).

**Names live in an overlay, not on the nodes.** There are no labels under the squares. Hovering a node scales
it 1.5× over 120 ms, thickens its border, pulls it to full opacity even in the quiet run state, pulls it to
the front, and shows a tooltip with the full project name — no delay. The scale is one transform shared by the
square, its ring and its bead orbit, so they can never drift apart. §2.3 asks for 1.7×, and the arithmetic
says that is exactly one cell: a node is `0.6 × pitch`, so `1.7 × 0.6 = 1.02` of the pitch — an enlarged node
fills its cell and touches its neighbour. At 1.5 it takes 0.9 of the pitch and the gap stays visible. The tooltip and the selection's name label share
one overlay `Canvas` that is a *sibling* of the camera's world, carrying no transform of its own: living under
the camera would scale the text along with the graph and blur it at 5× zoom. Their positions come from the
node's world point projected through the camera's **live** transform, refreshed on every frame the transform
changes — reading the camera's *target* instead would leave the label parked where the camera has not arrived
yet for the whole 460 ms of a selection glide. The box is centred on its node and the clamp applies to the
**anchor**, not to the box: clamping the whole box was tried and measured badly, because real project names
are long — a 30-character name is a ~215 px box, so in a 500 px panel every node near an edge dragged its
tooltip tens of pixels away from the node it belonged to. Staying centred beats staying whole; the anchor is
pulled into the graph's own inset, which is what keeps a label off the corner when the focus camera has zoomed
in.

Both boxes sit the same distance from the node — one number, not the design's two — but they measure from
different edges: the tooltip from the square, since a merely hovered node has no ring, and the name label from
the ring's outer edge. The distance itself is the node's *painted* half height plus that gap, never a fraction
of the node edge: the prototype's 0.9 and 0.95 coefficients were calibrated for a node that does not grow when
selected and whose ring is a CSS outline, and with those numbers the label landed inside its own amber ring.

The **name label is always below the node** — no clamp, no flip. Both of those were tried and both made the
label unpredictable: the clamp slid it onto the node it names, the flip threw it to a side the eye was not
looking at. Making room is the *camera's* job instead, which is the natural place for it because selecting a
node already moves the camera: the focus transform is nudged by the smallest translation that brings the
label's box inside the panel's inset, horizontally and vertically. Only the translation changes, so the
focus-and-fit scale is untouched. The tooltip keeps its flip, because hovering moves nothing and a node in the
top band has nowhere else to put it.

Both boxes are single reused elements, and each is explicitly invalidated before it is measured. That is not
defensive coding: changing a `TextBlock`'s text marks only the `TextBlock` dirty, and the walk up to its
ancestors happens during a layout pass. A `Measure` call from outside a pass therefore found the border clean
and returned early, leaving `DesiredSize` at the *previous* name's width — so a long name was centred on a
short name's box and sat well to the left of its node. Nothing is built per node. Changing the selection clears the hover, because the camera is about to move somewhere else
and the pointer is no longer over what it was.

**Selection focuses and fits.** Clicking a node — or a list row, or a stream line — fits the bounding box of
the selection plus its direct dependencies and dependents into the panel: scale is `min(W/bw, H/bh)` clamped
to 0.7–2.6 with a padding of `3 × node + 48 px`, and the camera glides there over 460 ms. Everything outside
that focus set drops to 0.1, and the selected node holds the hover treatment: it stays at 1.7×, keeps its
thicker border, is pulled to the front so nothing can cover its ring, and gains a 2 px amber focus ring. (The
main prototype does not enlarge a selected node — that came from the Graph Lab study and is a deliberate
departure from §2.3.) Pulling it forward is a fix rather than a flourish: the ring extends past the node, and
at a tight pitch a neighbour drawn later would cover all but its corners. **Only then are dependency lines
drawn** — from each dependency down to the node and from the node down to each dependent, as vertical cubic
beziers whose control points sit at the mid-height of their two ends, in amber dashes that flow along one
shared clock. Clearing the selection tears them down again. Because WPF measures dash arrays in multiples of
stroke thickness rather than pixels, the design's absolute 4/8 px pattern and 24 px travel are divided by the
1.2 px thickness so the drawn result matches the design.

**Navigation, and why the pan is unclamped.** The wheel zooms at the cursor — the world point under the
pointer stays under it — by a multiplicative 1.14 per notch inside 0.7–5.0. Pressing empty ground and moving
more than 3 px is a pan; releasing under that threshold is a click, which drops the selection if there is one
and otherwise returns the view to its default. Losing capture instead (Alt+Tab, a popup) is a *cancel* and
leaves the selection alone. There is no pan clamp, and that is a conclusion rather than an omission: the world
canvas *is* the panel, so a clamp of the "an axis that fits is centred" kind would force the translation to
the graph's centre on every selection whose fit scale falls below 1, overriding focus-and-fit entirely. The
design supplies its own recovery instead: clicking empty ground with nothing selected returns the view to its
default. (§2.3 puts a mono hint line in the bottom-right corner announcing the two gestures; it was removed —
the panel reads more quietly without it.)

**Opening.** After a Sync the nodes appear in build order — each one delayed by `index × 9 ms`, capped at
520 ms, rising 5 px over 300 ms. The wave therefore runs top-down and left-to-right, the same direction the
bands are read in. It is a hero (`sync-reveal`, shared with the project list), so it yields if another hero
is already playing, and reduced motion places everything instantly.

**Nothing is drawn while the panel is hidden.** In the `list` and `focus` layout modes the graph is
collapsed, and the status stream keeps arriving every 200 ms. Both feeding methods gate on the panel's own
`Visibility`: the latest feed is stored and replayed — topology first, then statuses — when the panel comes
back. The gate lives in the view rather than in `MainWindow` so that no caller has to carry the catch-up
logic.

**No culling, and no threshold that changes character.** An earlier version culled off-screen nodes and
switched behaviour above 150 nodes. Neither survives: since the graph fits the panel at every size, every node
is on screen in the default view and there is nothing to cull — and materialization was one-way, so zooming in
afterwards could not have saved anything either. Every node is built at `SetGraph`. That is a cost rather than
a saving on a very large workspace — it is paid once, at Sync — and it is what the design asks for, because a
node that was never built could not be part of a graph that claims to show everything at once.

All animations read the reduced-motion setting **fresh at start**; durations and easings come from
`Duration.*`/`KeySpline.*` resources or from named constants on their owning type, and colours from `Brush.*`
resources — no hex, no milliseconds inline.

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
| Segment | An `ItemsControl` of `RadioButton`s — the `Debug｜Release` control, and the About dialog's tab switch |
| Input | A `TextBox` style with watermark, prefix and invalid states |
| Tooltips | Open with **no delay** and stay until the pointer leaves, on disabled elements too. All three are `ToolTipService` attached properties that WPF reads from the tooltip's *owner*, not from the tooltip — set on the `ToolTip` style they are dead, which is how every tooltip in the app ended up on WPF's ~1 s default and looked like it never appeared. The defaults are overridden once, on `FrameworkElement`'s metadata (`AppTooltipDefaults`) |
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
| `F1` | About — version, shortcuts and diagnostics |
| `Esc` | Close the topmost layer (see above) |
| `Alt+B` | Global hotkey: restore the window from the tray |

The key → intent table is a pure, tested structure that `MainWindow` merely wires into `InputBinding`s, and
every dispatch honours the command's `CanExecute` — a shortcut never bypasses a disabled button. `F1` carries
an extra gate of its own: it does nothing while any modal is open (§13.3). Double-Shift and `Ctrl+P` are
*negatively pinned*: a test asserts they are **not** bound, so they cannot reappear by accident.

The table above is not written twice. A **shortcut catalog** derives each gesture's display text from that
same key → intent table — and the global hotkey's from the hotkey default — and pairs it with the one
sentence that describes it. The About screen's shortcut rows, the Build menu's `Ds.Kbd` badges and the info
button's tooltip all read from it, and a source guard forbids any production file from writing a gesture as a
literal. The badges used to be hand-typed strings living next to a binding table that could change
underneath them.

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
(`LineHeight.Snug13` = 1.35 × 13, and so on). The console's 1.55 line height comes from a `CompositeFont`
`LineSpacing` wrapper and does hold: 20.15 DIP at 13 px, measured in a realized window. A `CompositeFont` is
parsed only under its own XML namespace; written under the presentation namespace the file is rejected whole
at its root element and the family silently falls back to a proportional system face, which is what the
console rendered for a long time. Nothing about the family name or the weight is checked at load, so the guard
measures glyphs instead of identity — `i` and `M` must come out the same width.

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

**Cycle membership is not a status.** Being in a dependency cycle is a permanent structural fact, not something
that happened during a run, so it has its own channel and never occupies the status one: the row's dot and the
graph node's core stay orange whatever the run did — even after the member builds green, because the source is
still circular — and the row's fixed 14 px warning slot carries a single triangle whose colour names the
heaviest reason (orange for the cycle, amber for a dependency issue on its own; red is never used here, since
red means "built and blew up"). The status glyph always shows the real status; the warning never replaces it,
and while the row is building the slot is empty so nothing competes with the spinner. A `Build` will not
compile a cycle; *Resolve cycles* will (§8.1).

**`Cycle` is a pre-run statement.** It holds while nothing has been said about the row in this run — after a
Sync, or in a run that has not planned it. The moment the engine speaks about the row (started, finished,
skipped, or planned for this run) the status glyph carries the engine's answer instead, and membership of the
cycle moves to the badge in the dependency slot. Otherwise the two facts overwrite each other: with the glyph
holding `Cycle` through `Skipped`, every cycle row looked identical after a `Build` to how it looked straight
after a Sync — "this run skipped them" and "these are in a cycle" were indistinguishable — and a `Cycles` run
would have hidden its own results the same way. Now the left slot answers *what happened in this run* and the
right slot answers *where this project sits*, and neither hides the other.

A member waiting its turn inside a running group reads `Queued` (clock glyph), not `Building`. Members are
invoked one at a time and intermediate rounds are never published (§8.8), so the whole group sits in the
engine's `Started` state for the group's whole life while exactly one member is really compiling. Painting them
all as building made a 15-member group show fifteen spinners on the list and fifteen orbiting nodes on the
graph while the counter chip said one — the screen claiming fifteen things were happening when one was. Six
surfaces ask that same question — the row glyph, the counter chip, the ribbon's building chips, the row's own
breath layer, its live duration column and the list's frontier following — and all six now read one predicate
(`IsCompiling`: `Started` and not waiting its turn); written separately, they had drifted into disagreeing,
sometimes on the number, sometimes on whether anything was happening at all. Following was the last to join:
reading the raw engine state, it pinned the frontier to the first member of a group and, with the dead-band,
never moved again — during a `Cycles` run the list simply stopped following the build. A waiting member's row does not breathe, and its duration column
reads `—` instead of a running clock: it is not compiling, so a live count that reset every round it waited
through would have reported noise, not progress. The terminal line, once the group has a result, carries the
sum of every round instead (§8.8).

Two channels are **orthogonal** to status and must not be conflated with it: the will-build dot (§7.4) and the
dependency-issue triangle (§8.3).

Three cycle facts speak through that second channel rather than through status. The first two are about how
much a result can be trusted rather than about what the result was; the third is the membership the glyph
has just handed over:

| Outcome | Slot | Tooltip |
|---|---|---|
| The group ran out of rounds and this member is green | the dependency triangle | `Cycle did not fully settle — output may be one generation stale` |
| This run's rounds could not converge the group | the orange cycle badge, same slot and same 12 px | `Cycle did not converge — its projects are still out of date` |
| The row is in a cycle and its glyph now shows a result | the same orange badge | `In a dependency cycle` |

The first shares the triangle deliberately: it says the same sentence the dependency-issue triangle says —
*this compiled, but something upstream is unresolved, do not fully trust the output* — and only the wording
differs. The second may not: what it reports is the group's verdict, not this row's, so "last successful
output referenced" would be a claim about the wrong thing. It therefore takes the badge and outranks anything
stale left on the row. Its source is the run's own `cycleCompleted` verdict rather than a remembered one from
an earlier run, so it appears in the very run that proved it and regardless of how the individual member
ended — a member that went green inside a group that never converged is still holding a stale output. The
counter reads it the same way, without a status gate. The third is the
weakest of them and loses to all three: it asserts nothing about the output, only about the graph, and it is
drawn only when the status glyph has stopped carrying `Cycle` itself — the alternative would be saying the
same thing twice on one row. All of them also extend the
status glyph's own tooltip, since the slot collapses to nothing when it is empty and the glyph is the row's
one always-visible surface.

The run summary carries the same news at run level: `(N stuck in a cycle)` beside the skipped count, on the
completion line and on the *everything up to date* line alike. Without it a run whose only casualty is a cycle
that would not converge reads as an unqualified success, and the up-to-date line would go further still and
imply the cycle is not there.

### 14.4 Iconography

Lucide geometry, 1.5–2 px stroke, single colour, 12–16 px, authored as XAML geometries. **Never emoji.** The
building spinner is not a separate drawing — it is the discovered node's dashed ring, in amber, rotating
linearly over 1.4 s. The application icon is a multi-size ICO with the 16 and 24 px rasters hand-corrected;
carets and chevrons are drawn, not typed.

Two icons have no literal counterpart in the design source and are marked *derived* in the dictionary, with
the reasoning written beside them: the caption restore glyph, and the `info` circle in the title bar. Both are
drawn on the same grid and at the same stroke weight as the neighbour they sit next to — the info icon shares
`Icon.Gear`'s 1.7 px so the two buttons carry equal optical weight.

**Two marks, one hierarchy.** The application carries its own brand — five pill strips and a gradient chevron —
and the company logo sits behind it. Both are controls, not fragments of markup: `Controls/AppMark.xaml` draws
the product mark (title bar 19 px, About hero 30 px) and `Controls/BrandLogo.xaml` the company wordmark (title
bar 10 px at 55 % opacity, About 13 px at 80 %). Guards assert each geometry appears in exactly one source
file. The company logo is optional; where it is absent, the hairline separating it goes too.

The chevron is the one gradient in the application. Flat surfaces are the rule and a guard enforces it, with a
single file-scoped exemption for the mark: flattening a logo would mean redrawing it, and source artwork is
transferred verbatim. The chevron is amber — the same accent the interface uses — which is deliberate: the
brand speaks the interface's palette. The cost is that the mark carries accent weight in the title bar, so no
other amber element belongs in that region.

The mark's palette comes from the neutral ramp and the amber family, except two intermediate tones that exist
only in the artwork; those are declared in `Tokens.xaml` beside the rest, with their reasoning, exactly like
the other values the design source does not name. Two of them are also exposed as raw `Color` resources
because a gradient stop takes a colour rather than a brush — the brushes are derived from those colours, so no
hex is written twice.

**Raster icons** (`.exe`, taskbar, tray) are generated from the same artwork by `Assets/generate-app-icons.ps1`
into a multi-size ICO. They ship **without a background**: the mark sits on a transparent canvas and is fitted
to it. The design's asset matrix reserves the tiled artwork for exactly these surfaces, so this is a deliberate
departure, with a cost worth knowing — the mark's two dark strips nearly vanish against a dark taskbar, leaving
the amber, white and silver strips plus the chevron to carry it. The tile is still one switch away in the
generator.

**Every size carries the whole mark, 16 px included.** That is only possible because the strips are snapped to
the pixel grid below 32 px: a strip lands on roughly 1.9 px at 16, and drawn unsnapped it bleeds across two
rows at half opacity until the five of them read as one grey blur. Rounding each strip's edges to whole pixels
makes them solid bars with real gaps between them — the ordinary answer for small raster icons. The chevron is
curved and cannot be snapped, but it never touches the strips so nothing misaligns.

The mark is 1.48∶1, so a square canvas constrains it by width; it fills the width edge to edge and leaves the
canvas about a third empty top and bottom. That is the proportion, not a margin — stretching it to fill would
distort the logo. The padding is a hair above zero only so the antialiased edge is not clipped.

The fit and the pixel snapping are derivations the design does not specify; the generator's header records
them, and it can dump any size as ASCII so the judgement can be re-made against pixels rather than opinion.

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
timing-sensitive sequences (the event stream's typewriter) are `Stopwatch`-based rather than trusting the ~15.6 ms
`DispatcherTimer` resolution. Resetting an observable collection is prohibited — it destroys running
animations.

**An infinite animation must stop being visible before it stops running.** WPF's timing engine keeps the whole
render loop awake while *any* clock is active, so one forgotten `Forever` costs far more than itself: an idle
application was measured burning 133 % of a core, with a single thread at 92 %. Being collapsed is not being
unloaded — a hidden control stays in the tree and its own property never changes again — so every infinite
animation is gated on `IsVisible` as well as on its own state, and re-evaluated from `IsVisibleChanged`. The
same discipline applies to periodic work: a one-shot `DispatcherTimer` stops itself in its own tick (the
dispatcher roots it, so an unstopped one ticks forever and can never be collected), and anything called from
the 200 ms tick writes only when the value actually changed, since assigning the same string still invalidates
measure and draw five times a second.

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

Known gap: graph nodes are not keyboard-navigable. They are not silent, though — each node body is a `Button`
in the automation tree, named with the project and its status from the same central table and refreshed by the
status tick, and it answers `Invoke` through the exact activation path a click takes. What is missing is the
way in: there is no tab order and no arrow-key route into the canvas, so a pointer or a screen reader's invoke
is the only way to reach a node. The name earns its keep above the full-detail gate, where the label level of
detail leaves squares unlabelled — a sighted user gets the tooltip there, a screen reader gets the name.

---

## 16. State on disk

Everything the application persists lives under `%LOCALAPPDATA%\BuildOrchestrator\`:

| Path | Content | Corruption behaviour |
|---|---|---|
| `logs\run-<timestamp>\` | per-run and per-project logs | — |
| `build-state.json` | per-project signature, commit, result, duration, non-convergent cycle signature | falls back to empty |
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
`AboutDialogHost`, `SplitterHost`, `GraphTestView`), shared assertions (`FocusTrap`, the modal focus-trap
proof both dialogs use), dispatcher pumping and animation hosting (`DispatcherPump`, `AnimationHost`,
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
| Shortcut literals | no gesture text (`"F5"`, `"Ctrl+F5"`, …) is written outside the shortcut catalog, and the file the guard exempts still exists |
| Product name literal | the product name never appears as a literal; it is read from the assembly |
| Brand marks | the product mark's and the company wordmark's path data each live in exactly one source file |
| Gradient prohibition | no XAML declares a gradient except the product mark — and that exemption still points at a file that really carries one |
| App icon provenance | the multi-size ICO is rendered from the product mark, not the company icon |
| App icon background | every ICO frame's corners are transparent — the tile has not come back |
| Third-party attribution | every `PackageReference` has an entry in the notices table, and each entry resolves a real assembly version |

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
4. **No per-line transform and no CSS perspective inside AvalonEdit.** The console's panel transition is played
   on the editor as a whole (bottom-anchored Y scale + translate + fade) rather than as a 3-D hinge.
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
- **A large graph costs what it costs to open.** The graph fits the panel at every size (§13.6), so every
  node is on screen and every node is built — there is no threshold above which the panel changes character
  and nothing is culled. The price is paid once, at Sync: on the reference machine a 500-node graph realizes
  in roughly 130 ms and a 1000-node one in roughly 300 ms. Below that the pitch keeps shrinking until nodes
  reach their 8 px floor, at which point a very large workspace is legible as a shape rather than as
  individual projects.
- **The will-build preview can under-promise on cycles.** A run's own preview is projected through what that
  run has actually pre-skipped, so it never promises work it will not do. The remaining gap is the other
  direction and lives inside a `Cycles` run: the preview is computed per node from signatures alone, while the
  group's up-to-date gate is per group, so in a component whose members are only *partly* up to date — in
  practice, one member with no state row — the gate does not hold and members the preview drew grey are built.
  It errs safely: more work happens than promised, and nothing broken can look healthy. Closing it means
  computing the preview per component, which is a larger change than the divergence costs.
- **No field-level IPC schema validation** (§5.4).
- **Symlinks/junctions are not followed or detected** during the scan, and a `.csproj` may reference files
  outside the repository root. Both are accepted risks — the repository is trusted by definition.
- **Graph nodes are not keyboard-accessible, and the quiet graph does not change that.** A node is a mouse
  target: the pointer names it on hover and a click selects it. Each node does reach the automation tree as an
  invokable element carrying its project name and status, so a screen reader can find and activate one — but
  there is no tab stop, no arrow-key traversal and no keyboard-driven focus visual, and the design
  deliberately does not add one. The keyboard route to any project is the projects list, which is fully
  traversable and drives the same selection everywhere (§13.7); the graph reflects that selection rather than
  being a second way to reach it.
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
| Shortcut display text and descriptions (single source) | `App/Shell/ShortcutCatalog.cs` |
| Product identity, diagnostics report, third-party notices | `App/Services/AppIdentity.cs`, `DiagnosticsReport.cs`, `ThirdPartyNotices.cs` |
| Default layer definitions (Settings draft + *Restore default layers*) | `App/Shell/LayerDefaults.cs` |
| Title bar context text (`OSYS · main · main-2`) | `App/ViewModels/TitleBarContext.cs` |

**Engine and IPC**

| Behaviour | File |
|---|---|
| Command/event records, JSON options | `Contracts/Ipc/IpcMessages.cs` |
| Skip reason literals — single source read by Core, Supervisor and App | `Contracts/Ipc/SkipReasons.cs` |
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
| ETA formula (raw estimate, smoothing, rounding, cycle term) | `Core/Incremental/EtaCalculator.cs` |
| Build state store, duration persistence, non-convergence lookup | `Core/State/BuildStateStore.cs`, `BuildDurationPersister.cs` |

**Scheduling and run execution**

| Behaviour | File |
|---|---|
| Ready-set dispatch, resolved semantics, cycle group dispatch and pre-skip | `Core/Scheduling/ReadySetScheduler.cs` |
| SCC membership in build order (scheduler and coordinator read one instance) | `Core/Scheduling/CycleGroups.cs` |
| Cycle round stopping rule (converged / no progress / cap) | `Core/Planning/CycleRoundPolicy.cs` |
| Scope of a `Cycles` run (members + transitive upstream) | `Core/Planning/CycleRunScope.cs` |
| Dependency-issue propagation | `Core/Scheduling/DepIssueTracker.cs` |
| Run snapshot and elapsed clock across segments | `Core/Scheduling/RunSnapshot.cs`, `RunClock.cs` |
| Bounded synchronous retry (used by state store and clipboard) | `Core/Scheduling/SyncRetry.cs` |
| Worker loop, event pump, stop bookkeeping, perf lifecycle, cycle round loop and non-convergence memory | `Supervisor/RunCoordinator.cs` |
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
| Build menu (Build / Rebuild) | `App/Views/BuildMenu.xaml(.cs)` |
| Maintenance box (Clean / Optimize / Resolve cycles) | `App/Views/MaintenanceBox.xaml(.cs)` |
| Branch and worktree popovers, shared base | `App/Views/BranchPopover.xaml(.cs)`, `WorktreePopover.xaml(.cs)`, `PopoverBase.cs` |
| Branch popover row (virtualized item container) | `App/Views/BranchRow.cs` |
| Settings dialog, layer drag-reorder | `App/Views/SettingsDialog.xaml(.cs)`, `App/Controls/DragReorderBehavior.cs` |
| About dialog (identity, shortcuts, environment, notices) | `App/Views/AboutDialog.xaml(.cs)` |
| Product mark · company wordmark | `App/Controls/AppMark.xaml(.cs)`, `BrandLogo.xaml(.cs)` |
| Raster icon generation (.exe, taskbar, tray) | `App/Assets/generate-app-icons.ps1` |
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
| Node visuals, status tick, opening wave, hover, hidden-panel gate | `App/Graph/GraphView.xaml(.cs)`, `GraphNodeVisual.cs` |
| Persistent cycle-membership corner badge (built once, hidden not torn down) | `App/Graph/GraphView.xaml.cs` (`ApplyCycleBadge`/`EnsureCycleBadge`), `GraphNodeVisual.cs` (`CycleBadge`) |
| Automatic pitch, layer bands, node size | `App/Graph/QuietGraphLayout.cs` |
| Run lifecycle opacity and its hold/fade timings | `App/Graph/GraphNodeOpacity.cs` |
| Bead orbit geometry and timings | `App/Graph/GraphBeads.cs` |
| Selection edge style and its bezier | `App/Graph/SelectionEdgeStyle.cs` |
| Screen-space overlay placement (tooltip, name label) | `App/Graph/GraphOverlay.cs` |
| Focus-and-fit, wheel/pan arithmetic | `App/Graph/GraphCamera.cs` |
| Overlay chrome (tooltip, selection label) | `App/Resources/Controls.xaml` |
| Feed models | `App/Graph/GraphModels.cs`, `App/Controls/GraphStatus.cs` |

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
wording, filter rules, scroll arbitration, graph layout and camera, typewriter cadence, keyboard intent and
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
