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
| The tool never writes to git on its own | Every write is a user action with its own gate: a fast-forward pull from the `N behind` chip (§10.5), a checkout from the branch chip — with a stash only when the user turned that on (§10.3) — and the update of an external root the user left on (§10.4). `reset`, `rebase` and plain `pull` never run; all writes live in one file (§10.1) |
| Output lands exactly where Visual Studio would put it | `OutDir`/`OutputPath` are never passed to MSBuild (§9.4) |
| "Changed?" is decided from source; an output's date alone never makes it current | For an output this tool built, the decision is the signature over source **content** on disk (§7.1), and the output files can only veto it — never grant it; for an output built elsewhere, no input may be newer than it (§7.6) |
| Killing the app kills the whole build tree | Nested job objects with `KILL_ON_JOB_CLOSE`, no breakaway (§4) |
| Stopping a run never leaves a torn DLL | Graceful stop drains at project boundaries; no compiler server lives outside the job (§4.5) |
| The build order is deterministic | Order-preserving ready-set scheduler; no hashing, no randomness (§8.2) |
| An external working copy is never updated over the user's uncommitted work | The dirty gate cancels the run before it starts; updates are `--ff-only` and never `pull`, and they only run when the user leaves them on (§10.4) |

### 1.3 Non-goals (v1)

Multi-repo *history* — one repository's git history drives the branch, the HEAD watcher and the `N behind`
distance. Additional **external roots** (§10.4) are scanned into the same graph and built alongside, but they
contribute no branch and no remote distance of their own.
Headless/CLI operation. Light theme. Command palette. Onboarding flow. MSIX packaging.
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
| `src/BuildOrchestrator.Core` | `net10.0` | All decision-making, pure and testable: discovery, evaluation cache, dependency graph, layers, signature and incremental planning, scheduler, git service and the single git writer, HEAD watcher, MSBuild argument/invocation contract, job-object primitives, run logs, state persistence. |
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
the tray balloons and the About dialog all draw from it — a guard forbids the product name appearing as a literal
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
working, so the branch chip and the configuration stay locked and the Build split-button does not come back.

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
`starting` and then on `stopping` forever. So the App also watches for silence, but only inside the windows
where an answer is owed: a run has been requested and `runStarted` has not arrived, a stop has been requested
and `runStopped` has not, a Sync has been requested and `syncCompleted` has not, a maintenance job has been
requested and its own completion — `cleanCompleted` or `optimizeCompleted` — has not, or a checkout or a pull
has been sent and `checkoutCompleted` or `pullCompleted` has not. Any event from the engine
resets the clock; crossing the threshold with no event at all raises an amber ribbon line and reveals the same
*Restart engine* action.

Sync earns its place in that list twice over. It is a wait like the others — an engine wedged mid-Sync leaves
the ribbon on `▸ Sync — git fetch origin…` with no way out — and it is now the only way out, because the Sync
button is itself disabled while a Sync is in flight (§13.2). Nor does the window produce false alarms: every
read-only git call is capped at 30 seconds, so an engine that crosses the silence threshold is not slow, it
has stopped answering.

The two maintenance jobs are in the list for the same reason, and Optimize most of all. While either is in
flight the App closes every surface that could rescue the user (§13.2), so *Restart engine* is the only door
out. Neither of them has a cancel command, and on Optimize that bites: a Clean is over in seconds, while a
restore sequence can run for minutes. That window does not produce false alarms either. A single package
restore can go a long time without a word, which would look exactly like a wedged engine, so while the engine
waits on a restore child it prints a heartbeat line well inside the silence window — and since any event
resets the clock, a heartbeat included, the alarm can only fire on an engine that has genuinely stopped
talking.

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

`ping` · `shutdown` · `syncWorkspace` · `cleanWorkspace` · `optimizeWorkspace` · `startRun` · `stopRun` ·
`pullRepository` · `checkoutBranch` · `getProjectLog` · `listBranches` · `setPerfMode` · `debugSpawnChildren`.

`debugSpawnChildren` is a test hook for the breakaway probe. It remains part of the contract but is **rejected
by default** with `error(debugHooksDisabled)`; only a Supervisor started with `--debug-hooks` executes it, and
the App never passes that flag. Every other command in the list executes in the shipped pair.

`startRun` carries the run id, the mode, the repository root, the configuration, the parallelism, the
dependent-propagation mode, the layer patterns, the perf mode name and the external project list. It carries no
branch: a run always builds the working tree at the repository root, whatever is checked out there (§10.3).
Unknown fields on an incoming line are ignored. Parallelism and
perf mode are separate fields on purpose: the Supervisor derives cap and priority from the perf name but never
recomputes the worker count, which the App has already resolved from the same table.

`stopRun` names one of three kinds: `graceful`, `hard` (§4.5) and `interrupt`. The App sends `interrupt` when
the checked-out branch or HEAD moves under a run in flight (§8.8): the engine drains exactly as on a graceful
stop, and in addition it stops standing behind any result that arrives after the request.

`checkoutBranch` carries the repository root, the target — a local branch, or `origin/<name>` with an
`isRemote` flag — and `stashIfDirty`, the user's *Stash and switch branches* setting (§10.3). The engine runs
the checkout and answers with `checkoutCompleted`; it writes no console line of its own, because a successful
checkout opens a new console section and the App has to clear the console before the section's first lines are
written. While a run holds the slot the command is rejected with `error(checkoutRejected)`; an unexpected
exception becomes `error(checkoutFailed)`. Like `syncWorkspace` it blocks the command loop until it finishes,
so a `startRun` that arrives right behind it cannot begin on a half-switched tree.

`syncWorkspace` carries a **`fetch`** flag. It defaults to true, so an older line fetches as it always did; the
Syncs the App starts on its own and the Sync that follows a branch change send false, and the engine then
measures the `N behind` distance against the last remote state it already has (§10.2).

The **external root list** (§10.4) rides on every command that walks the workspace — `startRun`,
`syncWorkspace`, `cleanWorkspace` and `optimizeWorkspace` — and carries the roots the user listed in Settings.
Each entry is exactly what a Settings card holds: a path — a folder, a solution or a project file. Everything
else about a root (which projects it contains, their names, the working-copy root above them) is resolved from
that path on every run and therefore can never go stale. The field defaults to null, so lines written before
external roots existed still parse; the App sends null rather than an empty list, so a setup without them
writes the same line it always did. One list, one resolver, one merged workspace: an external project is an
ordinary project, so it is planned, cleaned and repaired with the rest.

`startRun` additionally carries **`updateExternals`**, which says whether this run may touch those working
copies at all — the Settings switch described in §13.3. It defaults to **true**, so a line written before the
flag existed keeps updating; when it is false the engine runs no version-control command and applies no dirty
gate.

`startRun` may also carry **`scopeProjectId`** — the identity of one project, sent when a run is started from
a row (§13.2). It is the last field and defaults to null, so a full run writes the same line it always did.
When it is set the engine plans in full and then cuts the plan down to that one project (§8.1): dependencies
are not compiled and nothing outside the scope enters the run.

Building dependency cycles is not a field but a **mode** — `Cycles` (§8.1). It is written to the wire as
camelCase text like every other enum, so adding a value never shifts the meaning of an older line.
`syncWorkspace` carries no cycle decision at all: its preview always describes a `Build`, and `Build` never
compiles a cycle.

`cleanWorkspace` carries the workspace root and the registered external cards — the same list, resolved by the
same merger. It resets the build output of that workspace on disk: the `bin` and `obj` folders of every project
the merged scan discovers, plus their entries in `build-state.json` (§16) — the state first, the folders
second, so that the worst outcome of a Clean cut short is an extra compile, never a project whose signature
still reads as current while its output is gone. A card that resolves to nothing is a warning, not a stop: the
rest is still cleaned. It never invokes MSBuild's `-t:Clean`. On the old-style projects this tool targets, the
set that target removes — the paths recorded in `FileListAbsolute.txt` — is a subset of `bin` and `obj`;
deleting `obj` takes that record with it, so the target could not run after the folders are gone, and running
it first would only remove a part of what the folder deletion removes anyway; and where tracked outputs were
copied to the shared `OutDir`, `-t:Clean` would delete from there too, which the "`OutDir` is never touched"
invariant forbids. `packages`, the shared `OutDir`, the run logs, the evaluation cache and the UI state are
all left alone. Like `syncWorkspace`, it blocks the command
loop until it finishes rather than running on a background task, and a `cleanWorkspace` that arrives while a
run holds the slot is rejected with `error(cleanRejected)` (§5.4).

`optimizeWorkspace` carries the same two things and nothing more — the workspace root and the registered
external cards. No configuration rides with it, because not one of its steps looks at one. Where
`cleanWorkspace` removes build output, this one puts back what is missing and removes only what breaks a
build: it restores the projects whose NuGet packages are missing from disk (§9.3), names the references a
restore cannot fix, deletes the stale NuGet residue from the `obj` of old-style projects (§9.4), and prunes
the three ledgers of entries whose file is gone, sweeping the temp files their atomic writes left behind
(§16). Everything else is left alone — the global NuGet caches, `NuGet.config`, `bin` and the shared `OutDir`,
the run logs, the UI state, and git, since Optimize runs no version-control command at all.
Its permission to write in the workspace is bounded by the resolved project set: a folder no card resolves to
is never touched, and the only files it changes anywhere else are its own three ledgers (§16). **It changes no
build decision** of a project this tool built — the signature is computed from source (§7.1) and Optimize
deletes no build output, so neither a restore nor a deleted leftover makes such a project dirty; for an output
built elsewhere, a `HintPath` target a restore writes is an input like any other (§7.6). The App still runs a
Sync when it ends, the same hand-over a Clean uses: the list was emptied at the click, and that Sync is what
brings the decisions back (§13.2).

Like `syncWorkspace` and `cleanWorkspace` it blocks the command loop until it finishes, and an
`optimizeWorkspace` that arrives while a run holds the slot is rejected with `error(optimizeRejected)` (§5.4).
**Neither maintenance job has a cancel command:** the work runs to its end or the engine is restarted (§4.6),
and the contract offers no third answer. Failure does not need one either — a restore that exits non-zero (an
unreachable package source, the offline case) is counted, named and followed by the next project, a file that
cannot be deleted is counted and skipped, and if `MSBuild.exe` cannot be resolved at all (§9.1) only the
restore step drops out. The repair degrades; it does not stop.

### 5.3 Events

Lifecycle: `engineReady` · `pong` · `error` · `debugChildrenSpawned` (the test hook's answer, §5.2).
Sync: `syncStarted` · `syncProgress` · `workspaceTopology` · `buildPreview` · `syncCompleted` · `pullCompleted` ·
`checkoutCompleted`.
Clean: `cleanStarted` · `cleanProgress` · `cleanCompleted`.
Optimize: `optimizeStarted` · `optimizeProgress` · `optimizeCompleted`.
Run: `planProgress` · `runStarted` · `projectStarted` · `projectLog` · `projectSucceeded` · `projectFailed` ·
`projectSkipped` · `cycleRoundStarted` · `cycleCompleted` · `runStopped` · `runCompleted`.
Queries: `branchList` · `projectLogChunk`.

`planProgress` is the only run event that precedes `runStarted`; it carries the planning steps of a fresh
segment (§8.6). It stays separate from `syncProgress` because the App treats that one as part of a Sync
transcript, and a run's planning window is not a Sync.

`cleanProgress` shares its shape with `syncProgress` — a line and a level — but is a channel of its own for
the same reason: the App's Sync gate keys off Sync events, and a Clean transcript is not a Sync. `cleanStarted`
is what moves the App's Clean gate from *requested* to *in flight*; the decisions on the rows went at the click
(§13.2), which is why a rejected command leaves the screen empty until a Sync. `cleanCompleted`
closes the window with its counters — project folders visited, folders removed, bytes freed, files that were in
use, state entries cleared; the App's one-line stream summary uses the first four, the state count appears only
in the console line, and the App answers the event with a Sync of its own (§13.2). A file that could not be
deleted is not an error — it is skipped, counted there, and the deletion carries on; only a missing root or an
unexpected exception becomes `error(cleanFailed)`.

`optimizeProgress` is the third channel of that same shape — a repair transcript is neither a Sync nor a Clean,
and folding all three onto one discriminator would blur both the console history and the diagnosis.
`optimizeStarted` moves the App's Optimize gate from *requested* to *in flight*; as with a Clean, the rows were
already cleared at the click, and the App answers `optimizeCompleted` with a Sync of its own that brings them
back (§13.2). `optimizeCompleted` closes the window with one counter per
step: projects scanned, projects restored, restores that failed, references restore could not resolve, projects
whose `obj` was cleaned, the three ledger prunes kept apart, temp files swept, files that were in use and bytes
reclaimed. Nothing the App can derive is put on the wire, and the three prune counts stay apart because the
source-hash ledger is keyed by source file rather than by project and fills up far faster. The console's closing
line and the App's one-line stream summary are worded from **one list of terms** that names only what happened —
restores that succeeded and that failed, unresolved references, cleaned `obj` folders, pruned entries, swept
temp files, bytes reclaimed — so a count is never worded two ways and a zero is never spelled out; with nothing
to name, both say there was nothing to fix. The console carries the detail, where the per-reference lines stop
at a cap and end with an "and N more" tail while the counter still reports the true total. Every line carries its
meaning in its own prefix — a warning starts with `warning:`, an error message with `[error]` — because the
console colours a line from its text, never from the `Level` field (§13.5). As with Clean, a locked file and a
failed restore are results rather than errors: only a missing
root or an unexpected exception becomes `error(optimizeFailed)`.

`pullRepository` has no channel of its own. Its reasons flow as `syncProgress` lines — a fast-forward is a git
transcript — and `pullCompleted` answers the only questions the App has left: did the fast-forward succeed, and
if it was refused, why — a dirty tree, a diverged branch or a detached HEAD (`PullRefusalReason`; empty on
success and on an unexpected failure). The reason is structured so the event stream's short line never has to
be parsed out of the console's text (§10.5).

`checkoutCompleted` is the whole answer to `checkoutBranch`: a status (`switched`, `alreadyOn`, `dirty`,
`stashFailed`, `failed`), the branch before and after, the new HEAD on success, the number of dirty paths the
gate saw, the stash message when a stash was actually made — kept even when the checkout after it failed — and
git's own detail on failure. The App builds every console line of a branch switch from these fields (§10.3).

`engineReady` carries `interruptedProjects`: how many projects the engine found in flight in
`run-inflight.json` when it started, and invalidated before accepting any command (§8.7). Zero means the last
run ended normally.

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
  populated before anything starts. Each item also carries the reason (§7.4), and for a project waiting on a
  failed dependency the display names of its recorded roots — the row's tooltip prints them, the App never
  composes them. `conditional` marks a project **this run** evaluates only when its turn comes (§8.3): it may
  still compile, so `willBuild` stays `true`, but it is not part of the queue. A `Rebuild`, a row's target and a
  cycle-group member never carry it — those compile unconditionally. Sync's own preview does carry it, computed
  the same way, because Sync answers what a plain `Build` would decide (§10.2). Each item also carries
  `failedAt` — the same evidence-based failure moment §7.5 stores, read through the one lookup both a Sync's
  preview and a run's own preview share — and `localEdits`, which is `true` only on **Sync's** preview, for a
  project whose input set overlaps a path `git status --porcelain` reports dirty (§10.2). A run's own preview
  never sets it; it always carries `false`, because that flag describes what Sync last saw in the working tree,
  and a run does not repeat that read. `outputBuiltAt` is the time of the build evidence of a project built
  outside this tool (§7.6), set for that reason only; both previews write it through the same helper.
  The three times an item can carry — `lastBuiltAt`, `failedAt` and `outputBuiltAt` — have **no reader on the
  App side today**: no row, label, tooltip or project page prints a time (§13.2). They stay on the wire and in
  the ledger because they are cheap facts the engine already holds and the decision surfaces may want again;
  nothing downstream is allowed to grow a second meaning for them in the meantime.
- **`syncCompleted`** carries the target SHA, the degrade flag and three counters that are *not* derivable
  from one another: directly-changed projects (Fast semantics, no cascade — for an output built elsewhere, own
  inputs newer than the output, §7.6), the will-build set size (Safe
  semantics, dirty plus transitive dependents, minus any project a plain `Build` would only evaluate
  conditionally, §8.3 — the same subtraction the queue colour and the wave apply), and the up-to-date count. A
  project excluded from the will-build set only for being conditional does not fall into neither counter: it is
  a green row (built against a dependency's last healthy output), so the up-to-date count folds it back in — the
  same split the ribbon already draws from the rows themselves (`totalProjects - willBuild`, `RibbonText`). The
  two counters therefore always add up to the project count.
  It also carries the branch that was checked out when the Sync measured (`activeBranch`, null on a detached
  HEAD) and the local HEAD commit (`headSha`, null in a repository with no commit yet). The App keeps the pair
  as "the HEAD of the last Sync", which is what lets a trigger that finds HEAD unchanged skip its Sync (§10.2),
  and it aligns the branch chip with `activeBranch` the moment the Sync ends.

`runStarted.cpuCapPercent` reports the cap that was **actually** written to the job, not the one that was
requested — a Win32 failure surfaces here as `null` plus a warning line, and does not fail the run.
`runStarted.logDirectory` is the run's log folder on disk; the summary line of a run interrupted by a branch
change names it (§8.8).

### 5.4 Error handling

| Failure | Supervisor | App |
|---|---|---|
| Malformed JSON / unknown schema | `error(badCommand)`, loop continues | — |
| Unknown command type | `error(unknownCommand)` | — |
| Framing violation (over-long or truncated line) | `error(framing)`, exit 2 | engine killed, one `EngineExited` |
| Unresolvable perf mode | `error(badPerfMode)` | chip stays on the previous value |
| Bad root path / planning failure | `error(planFailed)` | run ends, ribbon shows the error |
| `startRun` while a run is active | `error(runInProgress)` | the *rejected* request drops its own pending flag; the live run is untouched |
| `cleanWorkspace` while a run holds the slot | `error(cleanRejected)` | the Clean gate reopens; nothing was deleted, the run is untouched |
| Clean failure (missing root or unexpected exception) | `error(cleanFailed)` | the Clean gate reopens; run and Sync state are untouched |
| `optimizeWorkspace` while a run holds the slot | `error(optimizeRejected)` | the Optimize gate reopens; nothing was repaired, the run is untouched |
| Optimize failure (missing root or unexpected exception) | `error(optimizeFailed)` | the Optimize gate reopens; run and Sync state are untouched |

The `runInProgress` row is a rejection, not a failure, and the distinction matters: the coordinator releases
its run slot only after every event has been written, so for a short window after `runCompleted` reaches the
App the slot is still held — and that is exactly when the buttons come back and a fast click lands. Treating
the rejection as run-ending would tear down the run that is still going; ignoring it entirely would leave the
pending flag set forever, locking the UI with nothing behind it to stop. `cleanRejected` and `optimizeRejected`
are the same kind of answer from the other direction: the App's own gates already close both maintenance
buttons while a run is in flight, so each rejection exists for the request that was already on the wire when a
run began, and — like `cleanFailed` and `optimizeFailed` — it releases only its own gate. Each maintenance job
owns a disjoint set of codes, shared with no run-ending code, so none of them can end a run or a Sync.

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
`HintPath`s and `ProjectReference`s — and, for the output evidence of §7.6, the `OutputType`, the default
`Platform` and every `<OutputPath>` with its condition.

**The output path is read, never guessed.** `OutputFileFor(configuration)` derives the full path of the
project's own build output: `<OutputPath>\<AssemblyName>` with `.dll` for a `Library` and `.exe` for an `Exe`
or `WinExe`. Two condition shapes are recognised — `'$(Configuration)|$(Platform)' == 'C|P'` and
`'$(Configuration)' == 'C'` — and an unconditional `<OutputPath>` matches every configuration; the platform is
the project's declared default (`'$(Platform)' == ''`), otherwise `AnyCPU`. As in MSBuild the last matching
entry in document order wins, and with no match the path is `bin\<configuration>\`. Where the path cannot be
derived with confidence the answer is *none*, never an approximation: an SDK-style project, a missing or
unrecognised `OutputType`, an `AssemblyName` or chosen path that still contains `$(`, or an `<OutputPath>` under
any other condition shape — that last one makes the whole project undecidable, whatever configuration is asked,
because picking among entries the evaluator cannot read would produce inconsistent evidence. `HintPathTargets()`
resolves the raw `HintPath`s the same way: absolute paths as they are, relative ones against the project's
folder, and any path containing `$(` skipped.

Results are cached in `evaluation-cache.json`, keyed by path with an mtime **and file-length** fingerprint. The
length term is not decoration: an edit that preserves the modification timestamp is otherwise invisible, and
the cache would serve a stale evaluation. Each entry also carries the cache **schema** it was written under; an
entry from an older schema is never a hit, so a field the evaluator learned to extract is never served empty
from a record that predates it — the project is simply evaluated again the first time it is met.

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

**A DLL claimed by two projects produces no edge, and that is said out loud.** When two projects share an
`AssemblyName` there is no way to know which one a `HintPath` meant, and guessing would mean guessing a build
order; the entry is dropped from the map instead. The drop is silent in its consequences — every project
linking against that DLL quietly loses its dependency and may compile before its producer — so the plan
carries a warning line naming the DLL and both projects, and both surfaces that show a plan print it: the
Sync transcript and the run console. The usual cause is two roots contributing the same solution — an
external card (§10.4) pointing at a second copy of something already under the repository root — and the fix
is the user's: rename one `AssemblyName`, or drop one of the roots.

Not every `HintPath` resolves inside the repository, so each one is classified into one of four buckets:

| Class | Meaning |
|---|---|
| `Edge` | Resolved to a producing project in this repository — becomes a graph edge |
| `ExternalThirdParty` | Path rule identifies a package or an installed product |
| `ExternalPlatformBin` | A platform binary under a `bin` folder with no producer here — typically a sibling repository's. Registering that sibling as an external root (§10.4) turns these into real edges, because the producer map then spans both roots |
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

Projects from an external root (§10.4) are the one exception: they occupy a **reserved layer** named
`External` at index −1, which is why they sit above every configured layer and above `Other`, and why they
lead the build order. That layer is not configurable and no regex produces it — membership is read from the
node's own source badge. User patterns are not applied to these projects at all: a pattern that happens to
match one does not move it out, and a project that matches nothing does not fall into `Other`, which is the
bucket for the repository's own unclassified projects. Because the assignment is independent of patterns, it
also runs when none are configured — that is the only case in which an empty pattern list still reorders the
list.

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

A project's signature is a SHA-256 over three terms:

1. the configuration string (`Debug`/`Release`),
2. the **content fingerprint** — a hash over this project's input files as they are **on disk**,
3. the signatures of its direct upstream producers.

Byte stability is a tested property: the same inputs always produce the same hex string, and the order of the
input lists never matters (they are sorted `OrdinalIgnoreCase` internally). Every variable-length component
(a path, a project id) is itself hashed before being concatenated, so a separator character inside a path
cannot make two different input sets collapse to the same pre-hash string.

**Version control is not part of the decision.** git is never consulted to decide what to build:
the repository's own projects, projects contributed by external roots (§10.4) and a folder under no version
control at all take the same path. A repository with no commits, or a machine where git is broken, still gets
a complete answer.

The fingerprint pairs a **path term** with a **content term**. The path term is the file's path relative to
the workspace root, `/`-normalised — so the same tree produces the same signature in any clone, and moving a
clone to another folder changes nothing. Files outside the root (an external root's projects, a
`Directory.Build.props` above the root) carry their full path instead. The content term is read from that same
file, which is the one that will be compiled: every run builds the working tree it was planned from.

#### The input set

A project's inputs are the union of four sources, de-duplicated and sorted:

- the `.csproj` itself;
- the items it declares — `Compile`, plus `Page`, `ApplicationDefinition`, `EmbeddedResource` and `Resource`
  (this is what catches a `.xaml` or `.resx` **linked from outside** the project folder);
- every build-affecting file under the project folder, `obj/`, `bin/`, `.git/`, `.vs/` and `node_modules/`
  excluded — this is what catches files that are not declared, not committed, or ignored by version control;
  the excluded names are the same ones the workspace scan skips (§6.1), read from one shared list so the two
  scans cannot silently drift apart;
- the nearest `Directory.Build.props`, `Directory.Build.targets` and `Directory.Packages.props` found walking
  up from the project folder (MSBuild's own rule: the first hit for each name wins, and the walk stops at the
  workspace root).

Only these extensions participate: `.cs`, `.xaml`, `.resx`, `.csproj`, `.props`, `.targets`. A changed `.md`
rebuilds nothing. WPF's transient `*_wpftmp.csproj` is excluded — it exists only during a build and would make
the signature jitter between runs.

A folder sweep sees nested projects: if one project's folder contains another, the outer project's set
includes the inner one's files. The outer project is then rebuilt more often than strictly necessary — the
safe direction, and the same behaviour as MSBuild's SDK glob.

#### Why disk and not commits

The commit-based formula had three holes. Its file list was built from `Compile` items only, so a committed
`.xaml` or `.resx` change was invisible and the project was silently skipped. Files that git never saw —
untracked, or ignored — were in neither the blob table nor the dirty list. And the same question had two
answers: the repository read the blob table while external roots already read the disk, so the two could drift
apart. Reading content closes all three, works offline, and needs no version control at all.

The cost is reading files, and it is paid once: `source-hash-cache.json` (§16) keys each hash by the file's
size and modification time, so a steady-state run only stats the input set. Measured end to end on the real
OSYS repository (177 projects, 22,982 input files, 288 MB), from the scan through both binding passes: **303 ms
per run** with a warm cache, against ~213 ms for the two git commands the old formula ran. With the cache empty
but the files in the OS cache it is ~670 ms. Everything on that path that is IO — collecting each project's
inputs, scanning the cache for misses, reading the misses — runs 16-way parallel; the values do not depend on
thread order (input lists are sorted, fingerprint terms are sorted), and leaving those loops serial measured
544 ms instead of 303.

The first pass on a **cold** disk is the one-time exception: ~8.9 ms per file sequentially, ~1.9 ms with 16-way
parallel reads — the dominant cost is per-file open overhead (on-access scanning), not throughput — so a full
first index of that repository takes about 40 s and is announced on the console. Upgrading to this formula
rebuilds everything once, because every stored signature was computed by the old one.

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
configuration's binaries are simply gone. The output evidence is the one qualifier (§7.6): when the new
configuration's output was built elsewhere after the tool's last run of the project, that project is in time
mode and can read up to date without compiling.

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

| Value | Meaning |
|---|---|
| `true` | dirty; will be built |
| `false` | up to date; will be skipped |
| `null` | no meaningful baseline yet (pre-Sync, or the signature could not be computed) |

**The plan has no colour of its own.** A separate amber/grey/hollow dot on the row and the core of the graph
node for "what will this run do" would only duplicate what the row's own status already paints (§14.3). What
the user sees of the plan is the row's **decision label** — `modified`, `modified · local`, `affected`,
`never built`, `failed`, or `up to date`
(§13.2) — and the scope of the marking wave when an operation actually begins. The tri-state itself is
unchanged: it still decides what a run compiles, and it still feeds the counters.

If the decision pass fails outright (an I/O or parse error) the counters are not reported at all — printing
zeros would assert "everything is up to date", which is a different and false claim.

A cycle member is evaluated by the same three rules; what it evaluates is the component's composite signature
(§7.3), so a group's members move together. The run's scope is the one short circuit: outside a `Cycles` run
every member reads `false`, which is the truth — nothing in that run will compile them.

During a run the value is live: the moment a project succeeds it turns `false`.

**`true` is not always a promise.** A project whose last success was linked against a failed dependency, whose
signature has not moved and whose ledger note names the root dependencies reads `true` with the reason
*waiting for dependency*: a `Build` or `Cycles` run does not pre-skip it, but compiles it only if one of those
roots is now successful (§8.3). The order of the checks matters — a moved signature wins over the note, because
the project's own change compiles it regardless, and a note without recorded roots stays an unconditional
*built against a failed dependency*, because nothing could tell the run when to stop waiting. An output built
outside this tool reads the same note, without the signature test, when its time verdict is fresh and a
recorded root is still in trouble (§7.6).

**The evaluator also returns why** — never built, last build failed, the signature changed, waiting for a
failed dependency, or built against a failed dependency whose roots are unknown; and, from the output evidence
(§7.6), built outside this tool, output older than its inputs, output missing, or a fed copy that no longer
matches — and it returns it even for a project the run will not compile, such as a cycle member outside a
`Cycles` run. Apart from that scope short circuit, `WillBuild` is `false` for exactly two reasons, up to date
and built outside this tool; every other reason reads `true`. That reason travels on the preview and is what the
row's decision label reads (§13.2),
together with one fact: whether the project's **own** files changed (stored content fingerprint versus
today's, or — for an output built elsewhere — whether its own inputs are newer than that output). The preview
also carries the moments behind those verdicts — when the project was last built successfully, when its last
proven failure was recorded and, for an output built elsewhere, that output's time — but no surface reads them
any more (§13.2); they travel because the ledger records them.

**Last build failed is evidence-based.** `LastFailed` is returned only when the ledger's failed signature — the
composite signature captured at the moment this project's own MSBuild invocation last exited non-zero (§7.5) —
equals today's signature; that equality is the proof the row is red about *today's* sources, not some earlier
ones. A last result that is not `Succeeded` but carries no failed signature — an attempt interrupted before the
compiler could report failure cleanly, killed, timed out, or stopped by an environment error rather than a
compile error — is not evidence, and the project reads `NeverBuilt` instead of `LastFailed`. `WillBuild` is
`true` either way, so this changes only the label a run's preview shows, never whether the project compiles.

### 7.5 Build state

`build-state.json` is **global**, keyed by project id (the full csproj path), holding the built signature, the
**built
content fingerprint**, the built commit, the last result, the last run timestamp, the last branch, the last
duration, a flag marking that this success was linked against a failed dependency together with the project
ids of that dependency issue's roots (§8.3), the signature at which this project's cycle last failed to
converge (§8.8), and the signature at which this project's *own* MSBuild invocation last exited non-zero
together with the moment that failure was recorded. This failed signature is the evidence §7.4's `LastFailed`
reason checks against, and it is kept apart from the last-run timestamp because the two can drift: a project
can sit unbuilt after a failure while an upstream change still moves its signature, so the failure's own
timestamp — not the last-run one — is the moment that failure actually happened. Both fields go back to `null` on
the next success, and answer `null` for a record that predates them or whose last result never named the
signature it broke at (an interrupted attempt, not a compile failure). The content fingerprint is stored *next
to* the signature rather than folded into it because it answers a different question — "did this project's own
files change?" — and the row's `modified` ↔ `affected` split is the only thing that reads it (§13.2). That last
field is deliberately *not* folded into the built signature: the built signature means "this was compiled
successfully", and Fast mode reads it as a frozen upstream baseline — a signature that was never built would be
taken for a clean one. A project from an external root (§10.4) has the same record under the same key shape, and
its built-commit slot means the same thing — except that the revision written there is **its own** working
copy's, not the repository's, because the repository's HEAD describes a different repository. The last-branch
slot stays empty for the same reason, and so does the commit when the working copy has no readable revision at
all (§10.4). The record also carries the project's **fed outputs** — the copies of its output in dependents'
`HintPath` locations that this tool's own successful build was seen to refresh (§7.6); the list is `null` when
nothing could be learned (no derivable output path, the output file missing after the build, an older record),
empty when the path is known but no candidate matched, and it survives a failed attempt unchanged. The
built commit and the last branch feed no decision: the built commit is diagnostic, and the project log's "last
successful build" line is the only place a revision is shown. It is written by a single serialized writer,
atomically (unique temp file + `File.Move(overwrite)`), after every project completes. Readers open with
`FileShare.Delete` so they cannot block the writer's rename, and a transient sharing violation is retried a
bounded number of times. A corrupt file never throws — it falls back to defaults.

### 7.6 Output evidence

The signature answers "did the source change since *this tool* built it?". It cannot answer for an output built
somewhere else — in Visual Studio, on a command line — and it cannot see an output that was deleted or a copy
that was overwritten after the tool built it. The output evidence answers those questions, and only those: it
never enters a signature, only the decision (`OutputEvidence`, one check per project from
`IncrementalRunBinder.ChecksFor`).

**Evidence files.** The *build evidence* is the project's own output file, derived from the csproj (§6.2). When
no path can be derived the project has **no evidence** and is decided exactly as §7.1-§7.4 describe, with no
veto and no credit. The *fed outputs* are copies of that file in the shared folders dependents link against:
the candidates are the `HintPath` targets of every project that depends on this one in the graph whose file name
is the build evidence's, and which of them this project's build really refreshes is **learned**, never assumed.
After this tool compiles the project successfully (a *Clean* deletes the record instead, §8.1), a candidate
that exists, has the build evidence's length and a time within two seconds of it is written to the record's
fed outputs (§7.5). A library copy checked into the repository is never refreshed by a build, so it is never
learned and never checked. A DLL name two projects produce has no graph edge (§6.4), so it has no candidates
either — only the build evidence speaks for it.

**Whose output is it.** The build evidence's time is set against the ledger's last run of the project
(`LastRunAt`). If there is no record, no last run, or the evidence is strictly newer than the last run, the
output was built by someone else: **time mode**. Otherwise it is the tool's own: **ledger mode**. The tool's own
output is always older than its last run, because the record is written after MSBuild exits and a copy keeps
its source's time — which is also why crash recovery's "last run = now" (§8.7) keeps a half-written output out
of time mode. That holds for a project the ledger had never recorded too: recovery, and every other failure
without evidence (§8.8), opens a failed record with no built signature for it, so its next decision is never
built rather than built outside this tool.

**Ledger mode** is the decision of §7.4 with two vetoes. `LastFailed` and never built stand as they are. For
every other reason a missing build evidence reads **output missing** — also when the signature moved, because
there is nothing on disk to call current. A project that would be up to date, or waiting for a dependency, reads
**output replaced** when a learned fed copy is missing, has a different length, or is older than the build
evidence (a newer copy of the same length is sound). An output's time never grants anything here: touching a
file and undoing it, or switching branches back and forth without building, still rebuilds nothing.

**Time mode** judges the output by its time against every input, in this order: no build evidence → **output
missing**; one of the project's own inputs — a file of the §7.1 input set, or a folder the sweep visited, since
a delete or a rename moves only the folder's time — is strictly newer → **output stale**, own; one of the
project's own `HintPath` targets is strictly newer → **output stale**, from a dependency; a learned fed copy is
broken → **output replaced**; otherwise **built outside this tool** — green, and not compiled. An input exactly
as old as the output counts as current, and a missing or unreadable input or target is ignored. Time mode has
no red: the ledger's failed signature describes a build older than the output on disk and is not read. One note
is read, and never on its own: a fresh output whose record carries a dependency issue with recorded roots reads
**waiting for a dependency** — the same answer ledger mode gives — but only while at least one of those roots
is still in trouble in this plan. A fresh verdict proves no more than "no `HintPath` target of mine is newer
than my output"; it does not prove the build done elsewhere linked against the root's old copy. If the root
was fixed and rebuilt elsewhere too, and the dependent was rebuilt after it, the note is simply out of date and
is ignored — the row is plain built outside this tool. Otherwise the note is a condition, not an order to
compile: the row stays green, the warning triangle names the roots, and the run compiles the project only if a
root recovers (§8.3). Whether a root is still in trouble is the one question the run asks too, and it has one
answer: a project is *current* exactly when its reason is up to date or built outside this tool. Because that
answer needs the whole plan, the note is the one part of the decision made after every project has its own
(`BuildPreview`, not the evaluator). A note with no recorded roots is not read, since the only thing it could
say is an unconditional *compile*, which would rebuild an output refreshed elsewhere on every run. Nothing is
written back to the ledger; every Sync proves the output again.

**Behind a dependency that will build.** A time check reads only file times, so it cannot see that a dependency
is about to be rebuilt: until that build runs, the dependency's shared copy is still the old one and the
dependent's output looks current against it. So in the Safe mode (§7.2) a time-mode project behind an upstream
project in the plan — directly or transitively — whose build really refreshes that shared copy reads output
stale from a dependency and is built in the same *Build*; its row reads `affected`, since its own files did not
change, and not `built outside this tool`.

Which upstreams count is the whole rule. A project that will build because its signature moved, because it was
never built, because it was built against a failed dependency whose roots are unknown, or because its own
output is missing, replaced or older than its inputs, leaves a new copy behind, and seeds the cascade. Two
reasons do not. A proven failure (§7.4) will compile again from sources that have not moved since it failed,
so the likely outcome is the same failure and the same copy left in place — and the dependent was built
elsewhere against that very copy, so pulling it to `affected` would be a false claim of staleness; the same
failed root under a ledger-mode dependent reads green with the warning triangle, and the two modes must not
disagree. The signature only summarises sources, though, so a failure whose cause was somewhere else — a
missing assembly, a restore, a locked file — can well succeed this time. The accepted cost is one round: the
time-mode project behind it stays skipped in that run and is compiled after the next Sync, which reads it
stale from the root's new `HintPath` time. One late round beats a wrong `affected` on every run. A project
waiting for a dependency compiles only if one of its roots recovers (§8.3), so its new output is a possibility
and not a fact; when a root does recover and it is compiled, its own dependents read stale from their
`HintPath` times at the next Sync anyway. A cycle member is the exception: a group is never split, so a member
that reads waiting for a dependency compiles unconditionally in the *Cycles* run that builds it, and there it
seeds. A dependent behind both a failed root and a genuinely dirty upstream is reached from the dirty one and
reads `affected` as before.

The walk from those seeds does not discriminate: it follows the reverse edges to every transitive dependent,
because each project it pulls will be built and dirties its own downstream in turn. A project whose own verdict
comes earlier in the order above — no build evidence, or an own input newer — keeps it. The Fast mode follows
no upstream and does not cascade here either. The rule lives in the planner, after every project's own
decision, and does not depend on the plan's order.

**`modified` or `affected`.** In time mode the split comes from the evidence — own input newer means
`modified` — and elsewhere from the stored content fingerprint compared with today's (§7.5). The Sync and a
run's preview make the same call (`OutputEvidence.OwnFilesChanged` over `BuildStateStore.OwnFilesChanged`), so
a row reads the same word after a Sync as in the next *Build*. The Sync's *N changed* counter is a different
count (§5.3): the projects the Fast pass finds dirty, with the evidence's answer in time mode. The two can
differ — a project whose record carries a stale signature while its own files are untouched counts as changed
yet reads `affected`.

**Cycle groups.** If any member of a group is in time mode, every member goes through the time check. When all
of them are current, all read built outside this tool; otherwise a member that fails its own check keeps its own
verdict and a member that passes reads output stale from a dependency — the group is stale as a group. A member
with no build evidence cannot prove it is current, so it keeps such a group stale. A group with no member in
time mode is decided member by member in ledger mode.
Inside the group, a member's `HintPath` check leaves out the targets its own siblings produce. Those targets are
the cycle's edges themselves: members built one after another outside this tool always leave one sibling's
output newer than the next member's, so counting them would keep every group stale for good. Which target
belongs to a sibling is read from the plan's own cycle membership and from a producer map made by the same
builder, over the same evaluated projects, as the one the graph's edges come from (§6.4) — the plan does not
carry its map, so the binder builds it once more, lazily and once per run. A target produced outside the group still counts, so a
newer upstream output still makes the whole group stale.

**Where the evidence goes.** The Sync binds its Safe pass with the checks and the engine binds a run's plan with
the same checks, so the Sync's will-build is the next plain *Build*'s decision; the Sync's Fast pass — which
only feeds the *N changed* counter — is bound without them, or a project whose shared copy was overwritten
would count as changed. The checks also travel with the run's plan, so the run preview writes the
same `modified` ↔ `affected` answer and the same `outputBuiltAt` time (§5.3), which is set only when the
reason is *built outside this tool* — the time the foreign output was produced. No surface reads it today: the
row's decision label carries no time at all (§13.2), so the field travels and is overwritten by the next
preview, where a project this tool has since built successfully reports it empty because its output is now the
tool's own.

**Cost.** In ledger mode only the build evidence and the learned fed copies are statted. Input times are read
only by a time check — a project in time mode, or a member of a group in time mode. Checks run 16-way parallel,
as input collection does.

---

## 8. Run planning and execution

### 8.1 Run modes

| Mode | Set of projects |
|---|---|
| `Build` | the will-build set (incremental), minus any project this run only evaluates conditionally (§8.3) — the wave lights only what will *definitely* compile, the same set the queue colour and the run's fixed progress denominator use |
| `Rebuild` | all projects; cached state ignored |
| `Cycles` | the projects in a dependency cycle **and their transitive upstream**, the cycles compiled in rounds (§8.8); everything else is pre-skipped as `skipped — not needed by a dependency cycle` |
| `Clean` | the run's scope, with `-t:Clean` instead of a compile — Visual Studio's *Clean*. Sent only from a row today (§13.2) |

`Cycles` is not a degree of difference from the others but a separate job: `Build` and `Rebuild` never compile
a cycle, `Cycles` compiles the cycles. It is the third icon of the maintenance box in the action bar (§13.2)
and is meant to be run before a build, not instead of one.

**A single project is a scope, not a mode.** A run started from a row (§13.2) carries the project's identity
and keeps the mode of the item pressed — *Build* or *Rebuild*. Planning runs in full, exactly as for any run,
and the plan is then cut down to that one node (`ProjectRunScope`): its dependencies are not compiled, and the
other projects never enter the run at all — no skip line, no counter, no row changes. A cycle member can be
built from its row too: it enters the run as a plain node and compiles once, alone, against its cycle-mates'
last known outputs.

**The target always compiles, even when it is up to date.** Pressing play is not a question but an
instruction: the incremental decision is what *Build* consults across a whole workspace, and inside a scope of
one there is nothing left for it to decide — honouring it would swallow the command. It used to be honoured,
and the cost was plain: the first press compiled the project and the second did nothing at all, leaving the
row grey. The reason behind the will-build dot is dropped for a target that was already clean, because none
of the reasons is true of it. The menu items stay distinct through the MSBuild target instead: *Build*
runs `-t:Build`, *Rebuild* runs `-t:Rebuild` — MSBuild's own clean-then-build for that project. That is the
one place the two words diverge from the action bar, where *Rebuild* means "ignore the cache" and still runs
`-t:Build` per project; in a scope of one, ignoring the cache is what *Build* already does.

**Clean is the third target, and it is Visual Studio's.** *Clean* in a row menu runs `-t:Clean` on that
project alone: MSBuild deletes the outputs it knows about, nothing is compiled, and no cache — NuGet's, the
evaluation cache, another project's `obj` — is touched. It is a run like any other, so it reports a result,
writes a project log and can be stopped; the maintenance box's *Clean* is a different, wider surface — the
workspace reset of §13.2, which runs no MSBuild target at all (§5.2). Two things follow from "the outputs are
gone". The project's **build-state row is deleted**, not invalidated: the project did not fail, this tool
simply no longer knows any output of it. The output evidence (§7.6) would notice the deleted output only for a
project whose output path it can derive — for an SDK-style project it cannot — so a row left behind would let
the next `Build` skip such a project as up to date and report a green run over deleted outputs. With the row
gone, a project whose output path is known is in time mode, and its deleted output reads output missing. And the
row **reads `never built`, in its to-build grey**, after the clean succeeds: elsewhere a
success means "this is now current", here it means "its outputs are gone", which is the opposite — so the
run's success does not paint the row green (§14.3). A Clean that fails is no evidence against the source
either: `-t:Clean` never calls the compiler, so its non-zero exit (a locked file, a denied delete) invalidates
the row like a timeout would, without the failed-signature pair (§7.5). Package restore is skipped for the same
reason a compile is: there is nothing to build.

**What the target was built against is recorded.** A direct dependency that this run did not compile but
whose signature is dirty (or unknown) is a **stale** input: the target links to that dependency's previous
output. It is surfaced as a dependency issue (§8.3) — a warning line at the head of the target's log
(`X has pending changes and was not rebuilt in this run — last known output referenced`, or the cycle wording
for a cycle-mate), the triangle on the row, and the note in the build state, with that dependency's project id
recorded as a root — so a later `Build` compiles the target again once that root is healthy again, rather than
on every `Build` regardless (§8.3, `ConditionalRebuild`). Without the note the target's fresh signature, which
already contains the dependency's new source term, would read as up to date for good: the same
permanent-stale-binary hole the `Cycles` scope
closes by pulling its upstream in. The scope stays at one project by design (*build with dependencies* is
not offered); the ledger closes the hole instead. A cycle member's cycle-mates are always stale inputs, so a
member built alone can never make its group read as up to date for the next `Cycles` run. External working
copies follow the same rule: only the copy that holds the target is updated before a scoped run (§10.4).

**Resuming and retrying are not modes.** A stopped run is not resumed and a failed run is not retried by a
separate command: in both cases the user presses *Build* again, and the incremental decision produces exactly
the set the old modes produced. Projects that finished green persisted their signature and are skipped as up
to date; projects that were killed or failed had their stored state invalidated (§7.5) and stay dirty; the
dependents of a failure succeeded carrying a dependency issue, so their record is flagged with its roots
(§8.3) and they come along as soon as one of those roots is healthy again — recovered in this run, or already
recorded as successful, per the table there. The one deliberate difference is the elapsed clock: the new run
counts from zero, because it is a new run.

The projects that fall out of scope this way are not announced one at a time in the event stream — a
workspace with hundreds of unrelated projects would turn a `Cycles` run into scope-only noise — they collapse
into a single line, `N outside cycle scope — skipped`. `decision.log` still records each one under its own
name; only the live stream collapses them.

The App carries the same restraint into the row list, the counters and the ribbon: an out-of-scope project
never shows a "skipped" row and is never counted in the run's skipped total —
the engine's own pre-skip for it is not this run's business, so its status and colour do not change (§13.2).
Only a project genuinely inside the scope — a cycle member or the upstream this run pulled in — that comes
back `skipped — up to date` reads as a result. The project's own page is the one place the pre-skip does
reach: opening it states the same reason the engine gave, because every pre-skipped project's will-build flag
reads `false` for the run's whole life regardless of why (§13.2) — a page that stayed silent about the reason
would read a possibly-dirty, merely-out-of-scope project as `Up to date`, which is not the same claim.

The same containment reaches the queue colour, the run's own closing line and the two run-scoped counters
(§13.2, §14.3): a workspace with hundreds of unrelated projects leaves hundreds of grey-forever rows that must
not silently inflate *how many are still not built*, the *finishing soon* gate, or the tally the run's closing
narrative reads out loud — all three read a small, exact number for a full `Build`, where every un-started row
genuinely belongs to the run, and the distinction only bites once a run's own scope is smaller than the
workspace.

**Why the scope reaches upstream.** A member compiled against a *dirty* dependency's previous-generation DLL
comes back green while its output is stale — and the run then persists that member's signature. Because the
signature already contains the upstream's source term, the next `Build` reads the member as up to date and
never recompiles it: the project stays linked to a stale binary, permanently, with no second mechanism to
catch it — the output is the tool's own, so it is judged in ledger mode, where an output's time never overrules a
matching signature (§7.6). Pulling the transitive upstream into scope closes
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
- The row carries an amber warning triangle in a **fixed 14 px slot** that exists on every row, so alignment
  never shifts. It is never red — red means the project itself failed — and the graph draws no triangle at all:
  the node's only warning proxy is the amber cube of a cycle member (§14.3).
- The action bar's `⚠ N` chip counts it, together with cycle membership, and filters the list to `warn`.
- The event stream reads `built — dependency issue (2.4s)`, and the completion line reports
  `N dependency-affected`.
- A single-project run (§8.1) adds a second kind of root: a dependency the run deliberately did not compile
  although it is stale. The warning line says so (`X has pending changes and was not rebuilt in this run —
  last known output referenced`), and the flag, the triangle and the counter work exactly as for a failure.

That slot has three other tenants, all about cycles: a member of a group that ran out of rounds, a member of
a group this run could not converge, and plain membership. The triangle is the same in all four cases and
always amber; only the tooltip's one line differs, and the strongest claim wins (§14.3). The loop itself is
named in the **project log** rather than in the tooltip — `Domain.Parts → Parts.Inventory → Parts.Api →
Domain.Parts`, closed back on its first member so it reads as a cycle rather than a chain — because a tooltip
that has to hold a path is a tooltip doing a log's job. The path still composes from a single place, so no
surface can drift into its own wording.

**Such a success is recorded, with a note and its roots.** It is written to the build state like any other
success, but flagged, and the flag carries the **project ids** of the roots — the failures of this run, the
roots inherited down the chain, and the stale inputs of a single-project run. Ids, not names: the run looks the
roots up in its own results and in the ledger, and a name is not unique. The rule used to be *don't record it at
all*, on the reasoning that a fresh signature would let the project be skipped forever if the failed dependency
recovered without a source change. The reasoning was right and it is still enforced — just one layer later.
What was wrong was the cost: `depIssues` are inherited down the whole chain, so a handful of real failures
poisons the graph and nothing gets recorded. Measured on a real run: 74 succeeded, 24 failed, 96 carrying a
dependency issue — and not one of the 74 was written. Incremental building was effectively off and every Sync
said "everything will build".

**The note triggers a rebuild when a root recovers, not on every Build.** Recompiling a project while its root
still fails buys nothing — it links to the same last successful output again — and it meant projects the
marking wave never lit turning amber and compiling on the next `Build`. So a noted
project whose signature has not moved is *waiting for dependency* (§7.4) — as is one whose output was built
elsewhere, still reads fresh, and has a root still in trouble (§7.6) — and is evaluated when its turn comes,
because only then — every dependency terminal — is the roots' result in this run known (`ConditionalRebuild`):

| A recorded root… | reads as |
|---|---|
| compiled in this run and succeeded | recovered |
| compiled in this run and failed | still failing |
| not compiled in this run, and this run's preview found its output current — up to date, or built outside this tool | recovered |
| not compiled in this run, its reason not a current one (say a dormant cycle member reading signature changed) — last recorded result success | recovered |
| the same, and its last recorded result a failure | still failing |
| no longer in the workspace, or without a record | recovered (build — the safe direction) |

The preview comes before the ledger because it is the fresher witness: the record says what this tool last saw,
the preview says what is on disk now. A root fixed and rebuilt in Visual Studio reads built outside this tool
and is pre-skipped, while its record still says *failed* — reading the record first would keep every dependent
skipped as `dependency still failing` until the tool itself compiled the root, a state nothing in the flow
could leave. Ledger mode had the same hole, more rarely: a root whose signature matches its last success but
whose last run failed reads up to date and is pre-skipped too. One rule closes both, and it is the same
*current* the dependency note is weighed against (§7.6).

One recovered root is enough: the project compiles normally and its note is cleared or renewed by the ordinary
rule. When every root still fails the project is skipped as `skipped — dependency still failing`, with the roots
named on its `decision.log` line; its record — note, roots, built signature — is left exactly as it was, so the
next run asks the same question. Its roots still enter the inherited accumulation: a dependent that does compile
links to this project's stale output and must carry the note on, or it would read as up to date for good once the
root recovers. Such a skip is not counted among the run's dependency-affected projects — nothing was compiled.

A root named on that line is not always freshly observed. A root that is itself a dormant cycle member, for
instance, is pre-skipped in a `Build` run without ever being attempted — the table above still reads its *last
recorded* result, because that is all there is. The line says so: a root this run actually watched fail reads
by its bare name (`Up`); a root whose "still failing" verdict came only from the ledger, not this run, reads
`Up (last known failure)`. The same classification `Decide` uses to reach its verdict produces the label — one
function, not a second guess re-derived from the same data (`ConditionalRebuild.DescribeStillFailingRoots`).

The condition belongs to `Build` and to the in-scope projects of a `Cycles` run. `Rebuild` compiles everything;
a row's target compiles unconditionally (§8.1); a member of a cycle group compiles with its group, since skipping
one member would leave the group half built. A record written before roots were stored carries no roots and
compiles on every `Build` as it always did.

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
update external working copies (§10.4)            ← before everything: a fast-forward can bring new
                                                     project files the scan must see
  → scan (once, main root + every external root)
  → evaluate (cached) → producer map
  → edges → solution map → topological order → BuildPlan
  → (Build only) incremental pass: per-project signature + willBuild
  → RunPlan { plan, solutionRefs, incremental }
```

The update step is skipped when the user has turned it off and by a `Cycles` run, which is a repair pass over
existing strongly connected components and has no business updating anyone's working copy. The *scan* still
covers the external roots in every mode, so the graph a Cycles run repairs is the same graph a Build sees.

**One tree, one identity.** A run always builds the working tree at the repository root — whatever branch is
checked out there — so a project's id, its full csproj path, is also where it is compiled. Everything flows by
that one value: the scheduler, events, the preview, persistence, `decision.log`, log file naming and the MSBuild
invocation.

Two details that are easy to get wrong and are pinned:

- **One scan, not two.** The `.sln` *paths* needed for `-p:SolutionDir` come from the same scan result as the
  projects; `ProjectNode` carries only solution *names*, so a second walk of the workspace would be needed
  otherwise.
- **Planning runs on the run's background task**, not on the IPC dispatch loop. Planning a large repository
  takes seconds; blocking the loop would freeze command handling for that whole window.

**Planning reports itself.** The planner takes a progress channel and emits a line per step; the coordinator
turns each into a `planProgress` event on the same FIFO channel as everything else, so they all reach the App
before `runStarted`. Lines that mark work about to begin — an external update, the incremental pass — are
written *before* it, because those are the long steps and they produce no count of their own; lines that report
a count are written after the step that produced it. Every run plans, so every run has these lines.

This repeats the work Sync already did, and that is correct: the working tree may have changed since, and
the external update and MSBuild resolution only exist on this path. What was wrong was doing it *silently* —
the App clears the console on a run request, so a multi-second planning window left the screen with nothing on
it at all. The step texts therefore live in one place in Core and both callers read them; the same work must
not acquire two names.

### 8.7 Crash recovery

An engine that dies mid-run — a crash, Task Manager, a closed session — leaves projects whose `MSBuild.exe` was
killed halfway. Their output may be freshly written and incomplete, and nothing in the ledger says so. The
in-flight ledger closes that hole.

- **Dispatch writes, the result erases.** The coordinator adds a project to `run-inflight.json` (§16) the moment
  it dispatches it — every round of a cycle group included — removes it when its result is reported, and empties
  the file on every way out of a run: normal end, stop, planning failure, unexpected error. The set lives in
  memory and the file mirrors it, rewritten whole and atomically under one lock on each change; the list is never
  longer than the parallelism.
- **Startup invalidates what is left.** Before the host accepts a single command, the engine reads the file and
  marks every listed project as a failure without evidence (`LastResult = Failed`, the run timestamp set to now,
  the built signature kept, no failed signature written — §7.5); a listed project without a record gets one in
  that same shape, with no built signature, so its half-written output cannot enter time mode (§7.6) either. The
  next `Build` compiles them, and their row reads grey `never built` rather than green: the App runs a Sync every
  time the engine reports ready, after a restart too (§12.1), so the recovered decisions reach the screen. The count rides on `engineReady` (§5.3) and
  the App prints `previous run was interrupted; N projects will rebuild`.
- **Failure to recover never blocks the engine.** A file whose content cannot be parsed is not trusted — nobody
  knows who was in flight — so it is deleted and nothing is invented. A file that cannot be *read* (a lock, a
  permission) is a different case: a warning goes to stderr, the file stays, and the next engine start tries
  again. When the file was read but invalidating a listed project fails, the ids that could not be invalidated
  are kept in memory — and in the file — and retried at the start of the next run, before planning, so a
  half-written output can never be planned as up to date. A ledger write that fails during a run never stops
  the run either: it becomes a warning line on the engine's stderr, and only that project's recovery is lost.

The same ledger state — a failure without evidence — is what a result arriving after a branch-change interrupt
leaves behind (§8.8). The two cases are the same fact: the engine cannot stand behind that output.

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
that this project's own dependents will inherit). A project the run evaluates conditionally is decided just
before that, and skipped there when every recorded root still fails (§8.3). The invocation request carries the
solution directory and a restore flag derived from the presence of `packages.config`.
The project's log file is opened before and closed after the
invocation, so a late line cannot be silently dropped. The first line written is the real MSBuild command
line. On success the build state is persisted with the signature computed during planning; on failure the
stored state is invalidated so the next run does not consider the project up to date at the source it failed
at. The built signature survives the invalidation, so a source reverted to the signature of its last success
is up to date again — that success still describes it (§7.5). What gets written depends on whether the failure
is itself evidence of a broken source, not just on the fact that it failed. Only a trusted result of a
compiling target (Build or Rebuild) whose reason is the compiler's own non-zero exit *and* whose planning
signature is known counts: for that one case the invalidation also writes the planning signature and the
moment into the failed-signature pair (§7.5), opening a fresh record when the project has never been seen
before, so a first-ever compile failure is not lost. Every other case — a timeout, a stop, an invoke error, a
failed Clean (which never calls the compiler), or a result the run does not trust at all, such as a
non-converged cycle's member that came back green — is not proof the sources are broken, only that this
attempt's output cannot be, and it clears any failed signature a past success has since invalidated rather
than writing one. For a project the ledger has never heard of it opens a failed record with no built signature:
without one the project would stay in time mode (§7.6) and a half-written output newer than its inputs would read
built outside this tool. Either way only `LastResult` and the run timestamp change beyond that — the built
signature, commit, branch and duration stay exactly as a past success left them.

The verdict is taken once, in one gate (`FailureEvidenceSignature`: a trusted result of a compiling target, a
compiler exit, a known planning signature and a ledger to write to), and the same answer goes two ways: into
the ledger and onto the `projectFailed` event as `evidence`. The application paints the row from that flag and
never re-reads the reason text — a non-converged group's member that fails with `exit N` looks like evidence
from its text alone, but it is not, and the row would otherwise turn red only for the next Sync to turn it
grey. An event without the field (an older engine) reads as no evidence. A success is handled the same way:
whether the ledger keeps it as a success travels on the `projectSucceeded` event as `trusted`, decided where
the invalidation is. A green member of a cycle group that did not converge (no progress, or the round ceiling)
is invalidated like a failure without evidence and arrives with `trusted: false`; a group cut short reports
every member as failed instead (§8.8, cycle rounds). An event without the field reads as trusted.

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
intermediate round's verdict out.

**A stop cuts the group where it lands, not at the end of the round.** The member already compiling drains, as
everywhere else; the members after it in the round are never invoked at all. A group runs its own loop rather
than going back to the scheduler for each member, so the scheduler's stop gate does not cover it and the gate
has to be repeated inside the loop — without it a stop kept spawning a fresh `MSBuild.exe` for every remaining
member, which is the one place the application broke §4.5's promise that nothing new is dispatched. Cutting
mid-round costs nothing, because an interrupted group discards every member's result anyway: the round that
used to be carried to completion was thrown away when it ended.

A round cut short is also **never put to the round policy**. Feeding it a partial round is the sharp edge here:
in a second round whose members had all been clean so far, the policy would answer *converged* while some
members had not been compiled at all, and a stopped run would persist a fresh signature — telling the next
`Build` that the component is up to date. The decision therefore stays at *continue*, which is exactly the
state the invalidate-everything path above keys on.

**Non-convergence memory.** A group that ends in **no progress** records the composite signature it gave up
at, per member, beside that member's build state (§7.5). A stop or an unexpected error never writes this —
neither is evidence that a cycle cannot converge. The memory is keyed by the source signature alone; the output
evidence (§7.6) plays no part in it. A member with no state row
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

**Interrupt.** A `stopRun` of kind `interrupt` (§5.2) arrives when the checked-out branch or HEAD moves under
a run in flight (§10.3). Dispatch and drain follow the graceful rule exactly — nothing new starts, the
projects already compiling finish, the CPU cap is lifted — and a flag records that the run was interrupted. It
is kept apart from the stop kind because it is a fact about the results, not a way of stopping: a later hard
stop still wins as a stop, and the run stays interrupted. From the moment the flag is set, every result goes
through the one reporting gate as untrusted: the tree those projects were compiled from is no longer the tree
on disk. A success is written as a failure without evidence and arrives with `trusted: false`, a failure is not
evidence — the same ledger state crash recovery leaves (§8.7). Cycle members pass through the same gate.

Nothing survives the run: the plan, the log writer and the dependency-issue
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
<project> -t:Build|-t:Rebuild -p:Configuration=<cfg>
          -p:UseSharedCompilation=false -nodeReuse:false -p:BuildProjectReferences=false
          -clp:Summary -nologo
```

- The target is `-t:Build` everywhere except one case: *Rebuild* pressed in a **row menu** (§8.1), which runs
  `-t:Rebuild`. Nothing else about the list changes with it, so the two targets share one contract.
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
- No intermediate path is passed either: every project compiles into its own default `obj`, exactly as Visual
  Studio would (§9.4).
- Projects from an external root (§10.4) get **exactly this list**. They are ordinary nodes whose
  dependencies this tool builds itself, so nothing about the contract changes.

Arguments are passed through `ProcessRunner`'s `ArgumentList` — manual string concatenation is prohibited — and
`UseShellExecute` is false everywhere. `cmd.exe`/PowerShell is never used as an intermediary. Where a command
line must be assembled by hand, escaping follows the `CreateProcessW`/MSVCRT rules including the
backslash-before-quote counting.

### 9.3 Restore

`msbuild -t:restore -p:RestorePackagesConfig=true -p:SolutionDir=<dir>\`. There is **no dependency on
`nuget.exe`** — it is not on `PATH` on the target machines. The `SolutionDir` property is required: without a
solution context, restoring a `packages.config` project fails outright. This is why the solution map of §6.3 is
an input to restore and not only to the UI.

That argument list has **two callers and one source**. The build path runs it as a prologue: a project that
carries a `packages.config` next to its `.csproj` gets a restore child before its build child, and a non-zero
restore exit means the build child is never started. A `-t:Clean` target gets no restore — there is nothing to
restore for. *Optimize* (§13.2) calls the same list through a **restore-only entry point** on the invoker;
`-t:Build` is never appended there, so that path cannot compile anything. It exists because a restore is not
always a build's prologue: Optimize repairs what a build would otherwise have failed on. Both callers share the
same invoker core — inner-job assignment, line pumping, the per-project timeout and the kill on timeout or
cancel — so a restore child is governed exactly like a build child.

Optimize restores only what a restore can actually fix: a project that carries a `packages.config` beside its
`.csproj` — the **old-style** family this tool targets, which takes its packages that way rather than through
`PackageReference` — and at least one of whose NuGet `packages` `HintPath` targets is missing from disk. The
`packages.config` itself is never parsed: NuGet's `repositoryPath` can move the store anywhere, so the
`HintPath` is the only trustworthy witness of where the packages are expected. A `HintPath` still carrying an
unexpanded MSBuild property is counted in nothing at all, because this service does no MSBuild evaluation and
staying silent beats a wrong diagnosis. A non-zero exit is **not an error**: the project is named on the
console and the sweep moves to the next one, which is how an offline machine behaves. The restore child's own
output is **collected, not streamed**. Even a restore with nothing to fetch prints a banner, certificate-chain
notes and a timing footer, and a failed one repeats every error in a closing summary; across a workspace of
needy projects that would bury the console, and the build path keeps the same output in the per-project logs
rather than in the narrative. What reaches the console is what the user acts on: the command line, and for a
failed restore MSBuild's error messages — each once, without the target path in front or the project path
behind, up to a cap.
If `MSBuild.exe` cannot
be resolved at all, only this step is skipped and the rest of Optimize still runs — the toolset is resolved
lazily, so a workspace with nothing to restore never pays for a `vswhere` search.

### 9.4 `OutDir` and `obj`

**`OutDir` is never touched and never passed to MSBuild.** The tool reads build output in three places, all
for the output evidence (§7.6), and only times and lengths: the file at the project's own output path as its
csproj declares it, together with the fed copies it has learned; after a successful build, every fed-output
candidate, to learn which ones that build refreshed; and, in time mode, the times of the project's own
`HintPath` targets — other projects' outputs and their copies. Build output lands exactly where Visual Studio
would put it: in
the solution's own shared output folder, produced by the projects' own post-build copy events. The orchestrator
copies nothing.

The intermediate directory is not redirected either. Every run builds the user's working tree, and every
project keeps its default `obj` for Visual Studio parity — no output path of any kind is changed. That default
`obj` can carry residue that breaks a build: a deleted sibling project's leftover `netstandard2.0` artefacts
(`project.assets.json`, `*.nuget.g.props`) were measured breaking otherwise-healthy builds. What happens to that
residue depends on **who asked**, and the split is deliberate.

*At the start of a run*, foreign-TFM residue in a default `obj` is detected and reported as a console warning,
and **nothing is deleted or modified**. A run is not the moment to take a decision the user did not ask for,
and the detector is warn-only throughout: it never throws, and an unreadable or ambiguous `project.assets.json`
leaves it silent rather than guessing.

*A user-triggered Optimize* (§13.2) is that moment. For a project the same detector calls stale, it removes the
NuGet-generated leftovers — `project.assets.json`, `*.nuget.g.props`, `*.nuget.g.targets` — from that `obj`.
The `obj` folder itself, everything else inside it, `bin` and `OutDir` are untouched, and a locked file is
reported by name instead of retried: the lock's owner is a running IDE or application, and "close it and run
Optimize again" is the honest answer. The removal is confined to **old-style** projects, and that limit cannot
be relaxed: in an SDK-style project `project.assets.json` is legitimate, and nothing would bring it back — such
a project has no `packages.config`, so Optimize's restore step (§9.3) never reaches it and the next build would
fail on a missing assets file. Deleting without a restore behind it breaks the build.

The symmetric half of this rule is §7.1 and §7.6: "did it change?" is answered from source, and an output's
time is consulted only to tell whose output it is and, for one built elsewhere, whether any input is newer — it
never makes the tool's own output current on its own.

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

### 10.1 The git surface

The complete set of git invocations in the codebase:

| Command | Mutates? |
|---|---|
| `rev-parse --verify -q HEAD` · `symbolic-ref --short -q HEAD` · `status --porcelain -z` · `rev-parse --is-shallow-repository` · `for-each-ref …` · `rev-parse --verify -q refs/{heads,remotes/origin}/<branch>` · `rev-list --count HEAD..<sha>` | no |
| `fetch origin <branch> --no-tags` | only `refs/remotes/*` |
| `merge-base --is-ancestor` · `merge --ff-only` | external working copies when updates are on (§10.4), and the **main repository only when the user clicks the `N behind` chip** (§10.5) |
| `stash push -u -m <message>` | the main repository, **only** when the user picks a branch on the branch chip, the tree is dirty and *Stash and switch branches* is on (§10.3) |
| `checkout <branch>` · `checkout --track origin/<name>` | the main repository, **only** when the user picks a branch on the branch chip (§10.3) |

Every write in that table lives in one file, `Core/Git/RepositoryWriter.cs`: the fast-forward
(`FastForwardUpdater`) and the branch switch (`BranchSwitcher`). Each is a user action with its own gate, and
none of them runs on its own — not in Sync, not in Build, not from a background task. `reset`, `switch`,
`pull`, `rebase`, `cherry-pick`, `clean`, `commit`, `push` and `worktree` do not appear anywhere. Two source
guards pin this (§17.2): one allows mutating verbs in that single file and nowhere else, the other keeps the
`worktree` verb and every worktree type out of the code and the contract.

Some reads need no process at all. The HEAD watcher and the git-operation gate (§10.3) read the git directory
as files: `.git` resolved to the real git directory (a folder, or a `gitdir:` file for a linked worktree or a
submodule), `HEAD` and the ref it names (loose or packed), the last lines of `logs/HEAD`, and the presence of
the operation markers. They run on every trigger, so they cost a file read rather than a `git` child.

`ls-tree` is not called: the signature does not read git (§7.1).

### 10.2 Sync

Sync runs the whole analysis, in Core:

```
git fetch origin <branch> --no-tags   (ref-only; skipped when the Sync does not fetch)
  → scan → evaluate (cached) → producer map → edges → SCC/topo → layers
  → will-build pass
  → workspaceTopology + buildPreview + syncCompleted
```

Everything in it describes the working tree at the repository root, and the branch it fetches and measures is
the one checked out there. The command's branch name is only a fallback for a detached HEAD, where the fetch
still uses it but no distance is measured — HEAD is not on that branch, and a count against it would be a lie.

Full analysis is Sync's job; a Build repeats its planning part (§8.6), which is cheap because of the evaluation
cache. Because of that, Sync's own `willBuild` pass is not a separate opinion — it is what a plain
Build, pressed right now, would decide, and the preview says so directly: a project this run would only
evaluate conditionally (§8.3) carries `Conditional=true` in Sync's own preview too, computed the same way
(`ConditionalRebuild.AppliesTo`, simulating `Build`). This matters because the row's wave and queue colour are
read at the moment *Build* is clicked, before the new run's own preview has arrived — at that instant Sync's
preview is the only opinion the App has, so it has to already carry the answer a conditional project's row will
need a moment later, or the row lights amber for one frame and drops grey as soon as the real preview lands.

If the remote is unreachable, the fetch failure is swallowed: a warning line is printed, the target SHA falls
back to the local HEAD, and the flow continues. The degraded path does **not** skip topology or the will-build
pass — offline still produces a complete, usable Sync. A Sync that does not fetch never goes to the network: it
reads the last known remote tip (`refs/remotes/origin/<branch>`) and measures against that; with no remote ref
the target is the local HEAD and the distance is unknown, which is not a degraded fetch.

**Who starts a Sync, and what it does to the screen.** The console tells one *section* at a time. A new
section is opened by an operation the user started, or by the world under the list changing — the branch.
Refreshes that happen on their own in the same world open nothing. Every Sync is one of four kinds, and the
rules of each kind live in one place (`SyncMode` / `SyncModeRules`); callers never compare kinds themselves:

| Kind | Started by | Console and event stream | Fetch | Visible as an operation |
|---|---|---|---|---|
| Manual | the Sync button | cleared at the click; full transcript | yes | yes |
| Appended | application start and an engine restart, a successful pull, the hand-over after Clean/Optimize, Settings Save and a repository change | kept; transcript appended below the note or transcript that is already there | yes | yes |
| BranchChange | a checkout from the branch chip, or a branch change seen by the HEAD watcher | cleared; the new section's first lines are the caller's (the stash and switch lines), then the transcript | no | yes |
| Silent | a commit, a return to the window, any other HEAD movement on the same branch | untouched; the transcript is hidden, but `warn` and `error` lines are still written; one line in the event stream at the end | no | no |

*Visible as an operation* means the operation pill, the `Syncing` phase on the ribbon, the dropped selection
and the clearing of the previous run's error text and overlay. A silent Sync does none of it: bad news on the
screen is not erased by a refresh nobody asked for, and the phase a finished run left behind stays. It is still
a Sync, though: the engine takes one command at a time, so while it runs the workspace commands (Build, Sync,
Clean, Optimize, pull, the branch chip) stay locked and the Sync button shows its busy state, exactly as for any
other Sync — no pill, no ribbon change and no console clear come with it. Its one
stream line is `synced after commit` for a commit, and `synced · N projects changed` otherwise — written only
when N, counted as the rows whose output status or decision label moved between the request and the answer, is
above zero.

**The Sync button and a branch change start the screen over; the other kinds refresh it in place.** A Manual or
BranchChange Sync (`SyncModeRules.RestartsPlanSurface`) blanks the project list and the graph at the request, in
the same moment as the console and the event stream. Only the screen is blanked: the rows, their decisions and
the topology stay in the view model, so the phase does not drop to `Boot`, the list shows no invite and the graph
no *appears after Sync* label, and its header stays empty rather than counting zero projects. When the Sync's
topology arrives the list and the graph come back with the reveal and the graph fitted to the panel, even when
the structure is the same as before. A Sync that brings no topology — the send failed, planning failed, the
engine was lost, or it completed without one — puts the previous surface back. An Appended or Silent Sync
rebuilds nothing unless the structure changed: a topology with the same structural signature as the last one —
node ids, names, layers and edges — reconciles the rows in place, leaves the graph and its camera where they
are, and only a project added or removed, a moved layer or a changed edge replays the reveal (§13.2). The click
of Clean and Optimize empties the plan itself, and a real repository change does too; each also forgets the
signature, so the Sync chained behind it reveals even the same structure.

Every preview that arrives outside a run — every Sync's, the silent one included — rewrites the decision of every
row, including a row the last run finished, so a project that changed in the background after a run goes grey
without a Sync from the button. Only while a run is in flight does a preview leave a finished row's decision
alone (§14.3).

Sync also asks git one more, purely read-only question: `git status --porcelain -z` on the repository root —
never `-uall`, so a new untracked folder comes back as a single directory entry rather than one entry per file
inside it. `-z` is what keeps the paths usable: without it git writes every non-ASCII byte as a quoted octal
escape (`core.quotepath`), so a file named with Turkish letters would match no path on disk and its project
would never be marked.
The paths it reports are intersected, project by project, against each project's own input set (the same set the
signature is built from) to produce the preview's `localEdits` flag — `true` for a project with an uncommitted
change of its own, computed by `LocalEdits.ProjectsWithLocalEdits` so the intersection is written once and Sync
only calls it. A directory line marks every input beneath it, because that is the only file-level detail
`--porcelain` gives for an untracked folder. If the query itself fails — no repository, a git error — the
failure is swallowed the same way an unreachable remote is: no project is marked, rather than guessing every
project dirty. A project from an external root (§10.4) never carries the flag in this phase; its inputs live
under a different working copy that this query never sees, and extending the read to every external root's own
`status --porcelain` is left for later.

### 10.3 Branch switching

There is one tree. A run always builds the working tree at the repository root, and the branch chip shows the
branch checked out there — a fact read from git, never a preference the tool stores. Picking another branch on
the chip is a real checkout.

**Checkout from the chip.** Choosing a local branch runs `git checkout <branch>`; choosing `origin/<name>`
checks out the local `<name>` when one exists and `git checkout --track origin/<name>` when it does not.
`origin/HEAD` is not offered — it is a pointer, not a branch. Nothing changes on screen until the engine
answers: the chip is locked from the click, the operation pill reads `SWITCHING BRANCH`, and the console is not
cleared
at the click. A successful checkout opens a new section through a BranchChange Sync (§10.2) whose first lines
are the stash line when there was one and `Switched to <branch> (<sha>) — from <previous>`. A checkout that
fails opens nothing: its line — `warning: switch failed — <git's reason>`, amber like every `warning:` line
(§13.5) — is appended under the console as it stands.

**A dirty tree is the user's setting to decide.** Settings → General → *Stash and switch branches* (off by
default):

- **Off** — nothing is done. The console says, in amber, `warning: N files have uncommitted changes — commit or
  stash them first`, and the event stream adds a short amber line, `branch switch refused — N uncommitted files`
  (§13.2).
- **On** — the changes, untracked files included, are stashed with `git stash push -u -m "build-orchestrator:
  leaving <branch> for <target>"` before the checkout, and the section's first line names that stash and says
  `restore them with git stash pop`. The tool never shows, applies or pops a stash; getting the work back is
  the user's. If the checkout fails after the stash, the stash line is still written, so the changes do not
  look lost, and because the tree did change a silent Sync refreshes the decisions — no section opens.

The stash setting applies to the branch chip only, never to a pull (§10.5): a pull stays on the branch, and a
stash nobody restores would look like lost work.

**Changes made outside the tool are reflected.** The HEAD watcher follows `logs/HEAD` in the git directory
with a Windows file notification — no polling, no work while idle; the repository, the sources and `bin`/`obj`
are not watched. Git's successive writes within one operation settle into one trigger after 1.5 s of quiet, so
a long rebase produces one Sync. The reflog lines appended in that window are classified — commit, checkout,
anything else (pull, merge, reset, rebase) — and the strongest wins. A line git is still writing when the window
closes is not read in half: it waits, and the window opens once more on its own so the whole line is read even if
its end brings no notification of its own. Visual Studio's background `fetch` and
`status` do not write this file and trigger nothing. A return to the window is the second trigger: when more
than five seconds have passed since the last Sync started or ended, it runs a silent Sync even when HEAD has not
moved, because files may have been edited elsewhere.

Each trigger is weighed against the HEAD of the last Sync (§5.3, `syncCompleted`), read from disk:

- the branch name differs → a new section (BranchChange), first line `Switched to …`;
- branch and commit are the same and the trigger is a HEAD movement → nothing: the tool's own checkout and the
  Sync a pull chains are not followed by a second Sync;
- otherwise → a silent Sync.

Before the first Sync completes nothing is compared — the Sync at application start opens the first section.
A trigger that arrives while a Sync, Clean, Optimize, checkout or pull is in flight is not lost: one pending
trigger is kept, the stronger replacing the weaker, and it is weighed again when the work ends. The kept
trigger remembers the HEAD of the last Sync at the moment it was first kept: when the work that ended was a Sync
that opened no section of its own (a silent or appended Sync that already measured the new branch), the branch
is compared against that earlier HEAD, so the switch still opens its `Switched to …` section; after a Sync that
did open a section — the tool's own checkout, the Sync button — it is not told twice. A trigger whose Sync could
not be sent — the gate was closed, or the send failed because the engine is gone — stays pending the same way
and is weighed at the next change of the gate, the engine coming back included. If the watcher
cannot start (a network drive, a permission, a repository with no reflog yet) the console says so once per
root and a return to the window keeps working as the safety net; the watcher is retried on each return.

**A branch change during a run interrupts it.** A commit during a run does nothing. Any other HEAD movement —
checkout, pull, merge, reset, rebase — asks the engine to stop with kind `interrupt`, once per run (§8.8): no new
project starts, the ones compiling finish, and from that moment their results are not trusted. The event
stream says `interrupted by branch change`. When the run ends the pending trigger is weighed; if the branch
changed, the new section's first line is the run's summary — `Run interrupted by a branch change — N built,
M not built · logs: <folder>` — followed by the switch line; if the branch did not change, the summary goes to
the event stream and a silent Sync runs. A run the user already stopped — the engine has acknowledged the Stop and
only the run's end is still to come — is not interrupted: no interrupt is sent, no stream line or summary is
written, and the trigger waits for the run's end like any other. As a safety net the end of every run is itself
a trigger, so a HEAD movement the watcher missed is still caught when the run finishes. The run logs on disk are
never deleted; clearing is for the screen only.

**While git is mid-operation, the tool waits.** The git directory's markers say what is in progress:
`MERGE_HEAD` (a merge waiting for conflict resolution), `rebase-merge\` or `rebase-apply\` (a rebase),
`CHERRY_PICK_HEAD` and `REVERT_HEAD`, and `index.lock` (a git command running right now); a named operation
wins over the lock. While one stands:

- an automatic Sync waits, and the event stream says `waiting for git — <reason>` once per operation;
- the branch chip carries an amber dot, and its tooltip names the operation (`Merge in progress — finish or
  abort it in git`);
- checkout and pull are locked, with the same reason in the tooltip;
- only then does a poll run: the markers' existence is checked every two seconds, and when they are gone the
  pending trigger is weighed;
- an `index.lock` that stands for 30 s is reported once as possibly left behind by a crashed git process — the
  tool never deletes it. That line stays a **plain** console line while the git refusals around it are amber:
  the amber ones are refusals the user can act on (commit, stash, reconcile), and this one refuses nothing —
  it is a diagnostic about something the tool merely noticed and will not touch;
- Build is not blocked: right under the run request the console warns that files with conflict markers will
  not compile;
- the Sync button always works, and its section's first line says the tree is mid-operation.

### 10.4 External roots

Some projects an OSYS build depends on live **outside** the repository root — customer-specific components
kept in their own git repositories. The user lists them in Settings; every Sync scans them
into the same graph as the repository's own projects, and every Build updates their working copies before
compiling.

**A card is a path.** The path may be a folder, a `.sln` or a `.csproj`. A folder is scanned recursively
exactly the way the repository root is; a solution contributes only the projects it lists, because scanning
its folder would drag in siblings it deliberately excludes; a project file contributes itself. Anything that
resolves to no project at all —
a path that is gone, an empty folder, a file that is neither — is reported: Sync warns and carries on, Build
refuses to start. Letting a configured root silently vanish would produce a green build linked against
whatever stale DLLs were lying around.

**A card cannot point at the main workspace itself.** A path equal to the repository root, or anywhere under
it, is rejected the same way — Sync warns and carries on, Build refuses to start — because the main root is
already scanned; treating it as an external root too would scan the same tree twice and the same project would
carry both an ordinary and an external identity at once. The comparison is on fully-resolved,
separator-normalised paths (`RootScope`, the same helper the ledger's root-scoped pruning uses), so a case
difference, a trailing separator or a forward slash cannot let a card slip through.

**Everything else is derived, nothing is stored.** The project set, the display names and the working-copy
root are resolved from the path on every run, so moving a project or recreating its working copy needs no
settings change. The working-copy root is found by walking up from the path to the first `.git` — a directory
in a normal clone, a file in a linked worktree — so the nearest clone wins when clones are nested. If no
working copy sits above the path, the projects are still built, after a warning saying so.

**git is the only source, and the card does not ask.** There is no source picker, because a second
version-control arm cost the user a decision on every card while depending on a Visual Studio component the
build itself does not need, and on any working copy without local metadata on disk it silently did nothing —
a picker promising an update that could not happen. A `vcs` key in a settings file or in persisted UI state
is read and ignored, so a file written by an older version still loads; its cards are ordinary git cards.

**Once scanned, an external project is an ordinary project** in everything that matters to the engine: the
same scheduler, the same parallelism, the same MSBuild argument contract, the same incremental decision, and a
failure that propagates through the same dependency-issue rule. Its edges come from the same
HintPath-to-producer map, which now spans every root, so a repository project referencing an external DLL gets
a **real edge** — the correctness of the order rests on that edge, not on a phase.

What it does not share is the layer: externals occupy the reserved `External` layer at index −1 (§6.6), so
they head the list, the graph and the build order. That is what the user asks for and what the dependency
direction says — the repository's projects consume the external output, not the other way round. It is a
*preference*, not a barrier: a repository project with no dependency on any external can still start in
parallel with them, which is safe precisely because a project that does depend on one waits for its edge.

The earlier design instead ran externals as a separate serial phase before any worker was spawned and aborted
the whole run when one failed. That phase is gone: with real edges the graph enforces the same guarantee, and
a failure now costs only the projects that actually depend on it.

**Updating is a separate step, and optional.** Before anything is scanned, each external working copy is
brought up to date with `fetch` + `merge --ff-only`. It runs first because a
fast-forward can bring new project files that the scan must see, and a run that a dirty external will cancel
should not pay for a scan. `pull` is never used: it would produce a
merge commit or a rebase depending on configuration, and either one rewrites the user's repository on the
tool's behalf. The flow is three typed steps instead — the dirty gate, a ref-only fetch, and a fast-forward
taken only when `merge-base --is-ancestor` says one is genuinely possible. If it is not, the working copy is
left exactly as it was. A run scoped to one project (§8.1) updates only the working copy that holds that
project; every other card is left alone and writes no line — not even the missing-working-copy warning — and
a target inside the repository updates nothing at all.

The `updateExternals` flag (§5) turns the whole step off. With it off no version-control command runs at all
and **there is no dirty gate either**: nothing is going to overwrite the user's files, so their working copy is
compiled exactly as it stands, the same way the repository's own working copy always is. Two run modes skip
the step for the same reason: `Cycles`, which repairs strongly connected components, and `Clean`, which only
deletes output. Neither has any business moving the user's source. Both still **scan**, so the graph they
work against is the one Build would see.

Two error classes are kept apart. Something the user has to resolve — a path that resolves to no project,
uncommitted changes, a diverged branch, a detached HEAD — **cancels the run before it starts**; a half-finished run helps nobody. A transient network or credential failure only warns and the local
version is built, which is the same posture the repository's degraded fetch takes.

**Sync only looks.** It runs no version-control command against an external root whatsoever — it scans files,
and that is all. This is what keeps Sync fast and offline-tolerant, and it is why the dirty gate lives in
Build, where the user has already decided to compile.

**Each root's revision is read where reading it is free.** `rev-parse HEAD` is local, so it happens at plan
time whether or not updates are on; a root with no working copy above it simply has no revision. When an
update does run, the console says where the copy landed: `Updated external 'DoganTrend' → a1b2c3d`, and that
reading — taken right after the fast-forward — is preferred over the plan-time one. A failure to read is
never fatal: the revision is diagnostic, no
decision depends on it, and rows do not display it — they display the decision (§13.2).

The incremental decision needs no special case at all. Since the signature is hashed from file content on disk
(§7.1), an external project and a repository project take the identical path: same input set, same hash
primitive, same separators, same comparison against `build-state.json`. Uncommitted work in an external copy is
captured naturally, and the answer does not depend on whether a git clone is behind the folder at all.

### 10.5 Distance from the remote, and the one pull

Sync's ref-only fetch already resolves the remote tip, so the distance is a local question: `rev-list --count
HEAD..<tip>`. It is measured whenever a branch is checked out and its remote tip is known — after a successful
fetch, or, for a Sync that does not fetch, against the last known `refs/remotes/origin/<branch>` (§10.2). A
detached HEAD and a degraded fetch leave it unknown. Since every run builds the working tree, the distance
always describes what the next Build compiles. The number reaches two places: the Sync line (`HEAD a3f81c2 ·
3 commits behind origin/main`, or `· up to date with origin/main`) and the `N behind` chip next to the branch
chip. When the distance is unknown
the line drops the clause and the chip is not drawn at all — an invented number would be worse than silence.

**The chip is a deliberate write, and the user's own.** The rule "the tool never pulls" still holds for the
tool: nothing — not Sync, not Build, not a background task — fast-forwards the main repository on its own. The
chip is one of the two ways the tool writes to the main repository, both of them a click (§10.1); the other is
the branch chip's checkout (§10.3). But once the fetch has said "you are three commits behind", sending the
user to a terminal is leaving the job half done. Fast-forward is the one git write a build tool can honestly own: it
rewrites no history, makes no merge decision, refuses to touch a dirty tree, and is trivially undone.

Clicking it runs the same principled sequence the external roots use (`FastForwardUpdater` in
`Core/Git/RepositoryWriter.cs`): dirty gate → ref-only fetch → "am I strictly behind?" → `merge --ff-only`.
What is advanced is always the branch checked out in the working tree, and the lines name the branch the engine
reads there — the name the command carries is only what the App last knew. Every outcome is a console line — the
fast-forward range on success, and on
refusal an amber `warning: pull refused — …` line with the reason and the fix (`uncommitted changes … commit or
stash them first`, `local branch has diverged … reconcile it manually`, `HEAD is not on a branch`). A refusal
also adds a short amber line to the event stream (`pull refused — uncommitted changes`, `— branch diverged`,
`— not on a branch`), built from the reason `pullCompleted` carries (§5.3). An unexpected failure — the
network, credentials — stays a plain `Pull failed — …` console line with nothing in the stream. A refusal is not
an error state: on a dirty or diverged tree, doing nothing is the correct behaviour. After a successful
fast-forward the chip drops and a Sync runs automatically — with the console **kept**, because the user needs
to see the result of the action they just took. The HEAD movement the pull causes is the same HEAD that Sync
then records, so the watcher's trigger finds nothing new (§10.3).

The chip is visible whenever the distance is known and above zero, but it is not always clickable. It is locked
while a run is in flight or being planned, while any other workspace operation — Sync, Clean, Optimize, a
checkout, a pull already sent — is in flight, while the engine is unavailable, and while git is mid-operation
(§10.3), in which case its tooltip says why. A dirty tree is refused with a line on the console, whatever the
stash setting says.

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

**Memory, not cores, is usually the first limit.** Each worker is an `MSBuild.exe` that starts a fresh,
multi-threaded compiler process for its project (`UseSharedCompilation=false`, §9.2, so nothing is shared
between workers), and a large project's compiler holds on the order of a gigabyte while it runs. On a developer machine that already has an IDE, browsers and other tools
open, Full can exhaust physical memory: the operating system then pages other applications out and back in,
and the whole desktop stalls — not just this tool. Measured on such a machine with real compilers, the
interface thread itself stayed responsive under every profile, while free physical memory under Full fell to
a fraction of a gigabyte and committed memory passed the machine's limit; Balanced kept a working margin.
That is why Balanced is the default, and why a build that freezes the machine is answered by a lower profile.
The measurement is kept as an opt-in test (§17.5).

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
§3.3), the console batcher (a ~50 ms flush window, opened by the first waiting line), the OS actions service and the view models. Two application-wide
singletons are exposed statically because their owners have no constructor seam: the reduced-motion settings and
the hero-motion coordinator.

The shell also enables the automatic Sync (§10.3) once: the HEAD watcher attaches to the repository root and
follows it when the root changes, and window activation is wired to the same coordinator.

Every time the engine reports ready — at startup and after **Restart engine** — the App takes the same ready
path. It prints `Engine ready — v<version>` and records the engine's version and PID. The first time in a
session only, if the pool folder that older versions kept at `%LOCALAPPDATA%\BuildOrchestrator\worktrees`
still exists, it prints one line saying the pool is no longer used and can be deleted, followed by
`git worktree prune` in the repository — the tool neither creates nor deletes that folder. Then, when a
workspace is open and a Sync is allowed, it starts a Sync (Appended, §10.2): at startup that opens the first
console section with a full transcript and a reveal; after a restart it refreshes the decisions a recovered
engine may have changed. A restart releases everything the old engine left waiting before the ready path runs,
so that Sync is the only operation in flight. Separately, whenever `engineReady` carries interrupted projects —
at startup or after a restart, which is exactly when an engine died mid-run — the console says `previous run was
interrupted; N projects will rebuild` (§8.7).

### 12.2 Window chrome

Custom dark title bar via `WindowChrome` (caption height 40, no Aero caption buttons) on a `SingleBorderWindow`.
**The main window never uses `AllowsTransparency`.** Exactly one surface does, and it is not this one: the tray
build overlay of §12.3 has to be a layered window, because that is what makes the desktop show through where it
is empty and what lets clicks fall through the same pixels. Consequences that had to be handled explicitly:

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

**A build that runs while the window is away is not invisible.** When the main window is hidden *and* a build is
in flight (`Starting` / `Running` / `Stopping` — `Syncing` is deliberately out of scope), the product mark
animates in the bottom-right corner of the primary work area. It appears if the user drops to the tray mid-run
and disappears the instant the window comes back. The surface is its own top-level window: it must stay visible
while the main window is hidden, so it cannot be a popup inside it.

Three properties make it a good citizen rather than a box parked on the desktop. It never takes focus and never
appears in Alt-Tab (`ShowActivated=false` plus `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`). Clicking the drawn logo
restores the window through the *same* path as clicking the tray icon, while clicks on the transparent area
around it pass through to whatever is underneath — that separation is free, because a layered window is
hit-tested per pixel by the OS against the alpha channel. `WS_EX_TRANSPARENT` is therefore deliberately absent:
it would make the whole window click-through and kill the click-to-restore.

When the run ends the overlay does **not** cut off. It finishes the exit phase of the loop it is in — the pieces
slide away and the last strip dissolves — and the window closes on that frame. After a short breath, so the two
events do not land on top of each other, an **OS balloon** reports the result, carrying the application's own
icon. That icon is loaded at the one size the notification API accepts — any other size is rejected only when the
balloon is shown, and because the notification is sent from an unawaited continuation the rejection would
vanish without a trace, so a test pins the size headlessly. Its text is not composed a second time: it is the ribbon's own terminal line, split once
at the separator the ribbon itself writes — the head becomes the title (`Completed`, `▸ Stopped`, `Run failed`)
and everything after it becomes the body. Opening the window afterwards shows that same sentence whole, in the
ribbon. A line that carries no separator — today only the one an unexpected engine stop writes; the engine
failures that name a reason keep their heads — falls back to the product name over the whole line. Clicking the
notification restores the window through the *same* path as the tray icon and the overlay. A run that ends while
the window is *visible* produces no balloon at all — the ribbon is already on screen.

The overlay sits closer to the right edge of the work area than to the taskbar: at rest the mark occupies the
left of its band and the right is reserved for the chevron's exit path, so the edge margins are separate and
the right one is narrow, but never zero — the band already contains the outermost exit frame and its shadow.

The overlay always sits on the primary screen, because that is where the tray is; on a multi-monitor desk the
user may be working elsewhere and the indicator still appears next to the tray, which is the intent. It is also
phase-driven rather than window-driven, so if starting a build from the tray without opening the window is ever
added, the indicator needs no further work.

The global hotkey (`Alt+B` by default, read from `ui-state.json`) is registered with `RegisterHotKey`. A
conflict disables it silently; the tray icon still restores the window. There is no UI for changing it yet,
but the loss is no longer invisible: the About screen marks that shortcut row *unavailable* when the
registration did not take.

Autostart writes to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. No admin rights, no HKLM, no service.

Coming back to the window — from the tray, the overlay, a notification or any other window — raises the window's
activation, and activation is a trigger for the automatic Sync: when more than five seconds have passed since
the last Sync, a silent Sync refreshes the decisions (§10.3). The same activation re-reads the git directory's
operation markers, so the branch chip's amber dot and the git locks are current the moment the user looks.

### 12.4 Layout modes and persistence

The title bar opens with a **logo lock**: the product mark at 19 px in full colour, the product name, a
hairline, and finally the company logo at 10 px and 55 % opacity. The hierarchy is the point — product ahead
and vivid, company behind and quiet. The lock ends there, and the title bar names no repository: the action
bar below says it once, with the workspace name and the branch chip. The window's application
commands sit at the other end, ahead of the caption buttons, in decreasing order of use: the three view-mode
toggles, a hairline separator, then the gear (Settings), the sparkle (What's new) and the `i` (About).

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
│ TITLE BAR 40px — product mark · title · company mark   ⊞ ≡ ▣ ⚙ i — □ ×│
├──────────────────────────────────────────────────────────────────────┤
│ STICKY RIBBON 32px — operation pill · phase · building chips ·        │
│                      failure chips        · global progress 2px      │
├────────────────────────────────┬─────────────────────────────────────┤
│ DEPENDENCY GRAPH               │ CONSOLE                             │
│ ═══════ horizontal splitter ═══│═══════ horizontal splitter ═════════│
│ PROJECTS                       │ EVENT STREAM                        │
├────────────────────────────────┴─────────────────────────────────────┤
│ ACTION BAR 42px — Sync · maintenance box · counters · workspace ·     │
│                   branch · cfg · perf ·                   Build ▴     │
└──────────────────────────────────────────────────────────────────────┘
```

**Title bar.** The brand alone: the product mark, the product name, a hairline, the company mark. It carries
no repository or branch context — the branch already has a chip in the action bar, and the one remaining fact,
*which workspace is open*, sits next to it as a mono label whose tooltip is the repository root.

**Sticky ribbon.** On the left a **persistent operation pill** — `SYNC` · `BUILD` · `REBUILD` · `CLEAN` ·
`DEEP CLEAN` · `OPTIMIZE` · `RESOLVE` — mono, caps, 19 px, one-pixel border. `CLEAN` is the `-t:Clean` run the
row menu starts on one project, and `DEEP CLEAN` the maintenance box's workspace reset — two words because they
are two different operations. It lights amber while a run or a Sync is in flight and goes neutral when they
finish; the two maintenance jobs write their word at the click and leave the pill neutral, because neither
opens a phase of its own — their live state is told by their own button in the maintenance box and by the
console transcript. The `SYNC` a maintenance job chains overwrites `DEEP CLEAN` or `OPTIMIZE`. A branch
switch from the branch chip writes `SWITCHING BRANCH` at the click; the Sync a successful checkout chains
overwrites it with `SYNC`, and a refused or failed one drops it. A pull from the `N behind` chip writes `SYNC`
from the start, since the pull and its Sync are one operation. A Sync the application starts on its own — a
commit, a return to the window (§10.2) — writes nothing: the pill keeps the last thing the user asked for. It
*stays* until the next operation begins: the phase
line is momentary, the pill is the identity of what was last asked for. The progress indicator lives inside
it, six pixels right of the text — a spinner while live, the result glyph when done; the phase line does not
draw a second one.

Then one mono line describing the phase, plus 20 px chips for the projects currently building (at most four,
then `+N`), plus — only when there are failures — the failing chips on the right: the first three, and a
`+N more` chip that applies the `failed` filter. The cluster is run story and lists every failure of this
run, but the filter it opens is the `✗` state filter: it lists the rows shown red. A failure without evidence
(a timeout, a stop, an invocation error) leaves its output stale, so its row is grey and sits in the to-build
bucket, not in that list. The cluster carries **no counter text**: the same numbers are
already in the completion line, and the ribbon should not say a number twice. Glyphs are 13 px in the phase
line and 10 px inside chips. There is no dismissible banner: a failure summary that can be dismissed is a
failure summary that will be missed. Underneath, a 2 px progress bar,
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

**Projects list.** 36 px rows: a 2 px status stripe (3 px when selected) running the row's full height, the
8 px **status dot** — the same colour as the stripe — the project name with the solution name beside it, then
a right-aligned block (min 134 px): on hover four icon buttons (*build this project*, a **⋯** menu, *Reveal in
Explorer*, *Open in Visual Studio*), and without hover the **decision label** — a row also takes that hover
while its project's node is hovered in the graph (§13.6, "The list and the graph share one hover"). Then the status glyph, the fixed
warning slot, and a 46 px duration column. The stripe, the dot and the glyph paint one value — the state of the
project's output with the running operation laid over it (§14.3) — so after a Sync every row already wears its
colour, and after a run each keeps the colour the run left it in. The warning slot is cumulative in the same
way: it shows this run's dependency issue if there is one, and otherwise the ledger's note that the project was
last built against a broken dependency, for as long as that note stands.

The decision label is what a row says about its own state, in five fixed words — the shared vocabulary of git
and MSBuild, not invented terms:

| Label | What the engine found |
|---|---|
| `modified` | its own input files changed since the last build — or, for an output built elsewhere, are newer than that output |
| `modified · local` | same, and at least one of its input files is also dirty in `git status` |
| `affected` | its own files are unchanged; a dependency changed — for a cycle member that dependency can be a sibling in the same cycle — or its copy in the shared folder no longer matches its build output |
| `never built` | no build output known to this tool: never built successfully, or its output file is missing |
| `failed` | it failed at this source |
| `up to date` | it is current — either because this tool's last build still matches, or because an output built outside it does |

Every word maps from the engine's reason (§7.4, §7.6), and the tooltip keeps apart what the word folds together:

| Reason | Label | Tooltip |
|---|---|---|
| never built, output missing | `never built` | `No build output known to this tool` |
| last build failed | `failed` | `Failed at this source — Build will retry it` (for a cycle member, `Resolve cycles will retry it`) |
| up to date, waiting for a dependency | `up to date` | `Up to date` |
| built outside this tool | `up to date` | `Up to date — built outside this tool` |
| output replaced | `affected` | `Its copy in the shared folder does not match its build output` |
| output stale, own inputs newer | `modified` or `modified · local` | `Its own files are newer than its build output` |
| signature changed, dependency issue — own files changed | `modified` | `Its own files changed since the last build` (with `local`: `— includes uncommitted edits`) |
| signature changed, dependency issue, output stale — own files unchanged | `affected` | `Its own files are unchanged — a dependency changed` |

**No label and no tooltip carries a time.** The slot names a fact, never a clock: the label reads the plan and
four facts only — the reason, whether the project's own files changed, whether any of them is dirty, and
whether the project sits in a dependency cycle (which only picks the retry clause of a `failed` row's tooltip,
*Resolve cycles* instead of *Build*). `local` is the one tail there is, and a timestamp reaching the row
changes nothing on screen. Showing the age of the evidence behind the word was considered and rejected: for an
output built outside this tool there is no age of *this* tool's making, so the row would have shown how long
ago *this* tool last built the project while claiming to describe someone else's output — and for every other
reason the minute count never changed the answer to the question the row exists for, "what does this
project's output need?".

A project waiting on a dependency (`WaitingForDependency`) reads the same `up to date` as a project whose
signature simply matches — both are read from the same fact, that the output is current — because *which*
dependency it is waiting on is the warning triangle's question, not the label's: the label itself no longer
carries the recorded roots at all. The triangle and the project page do not say the same sentence, though: the
triangle's tooltip is built for a narrow slot and abbreviates (`Dependency issue: Sales.Core +2`, the first root
plus a count), while the page has room and spells out every root, then adds the wait clause
(`RowWarning.WaitingForDependencyText`, the page's own single source). What the two do share is only the root's
own vocabulary — the `Dependency issue: ` prefix and the same short-name rule — not the surrounding sentence.
Scope does not change the label's wording either — a genuinely-waiting row, a forced one (triggered straight
from itself, a Rebuild, an SCC member), and a cycle member all read the identical `up to date`. The slot is
134 px, which holds the longest label (`modified · local`) with room to spare; the width is also the hover icon
block's, so it is not cut to the text.

**The word is a fact, never a promise.** A failed row's tooltip names who will retry it: *Build* ordinarily, or
*Resolve cycles* for a cycle member, because a plain Build never compiles a dependency cycle. The word is never
paired with a fixed retry verb such as `failed · retry`, because that would read as a promise ("the next Build
will try this again") that a cycle member cannot keep — on a real workspace most `failed` rows were cycle
members for whom that promise would never come. The word does not change with scope either way — `failed`
states what happened, the tooltip states who acts on it.

`modified` and `affected` are separated by a fact of its own: the content fingerprint written into
`build-state.json` on the last successful build, compared against today's (§7.5). Not by the signature — the
signature also carries upstream terms, so a project whose dependency failed would claim *its own* files
changed. Measured on a real workspace: six projects the user had never touched read `modified` for exactly
that reason. When the stored fingerprint is missing (an older record), the row shows the more cautious
`affected`. For an output built outside this tool there is no stored fingerprint that describes it, so the fact
comes from the output evidence instead: `modified` when one of the project's own inputs is newer than the output,
`affected` when only a `HintPath` target is (§7.6). `modified` gains its own `local` tail when at least one of the
project's input files is also dirty
in `git status` — a fact this tool cannot see any other way, since a dirty working copy has no signature of its
own yet.

**Scope does not silence the label.** A cycle member is not compiled by a plain Build, but if its files changed
it still reads `modified` — that is true, and the warning triangle is what says *Resolve cycles* is the thing
that will compile it. The same principle runs the other way: a Rebuild compiles everything, yet a row whose
content is current keeps saying `up to date`. The label is a disk fact, never the run's scope. Hiding it was
measured too: on that same workspace 33 of 184 rows — every SCC member — showed nothing at all.

The slot carries no status colour — green and red belong to the glyph and the stripe (§14.3). The leading word
is `text-secondary` when work is pending and `text-faint` when the project is current; whatever follows the `·`
is always faint, so the word reads first. The longer sentence (`Its own files changed since the last build`,
`Up to date — built outside this tool`) is a plain tooltip, in the same language as the icon buttons. The slot
is **empty** only when the decision is genuinely unknown — no Sync yet, or the engine produced no reason.

The label also follows the run live: the moment a project succeeds its row reads `up to date`, and a
failure the engine counts as evidence reads `failed`. A failure that is not evidence — a timeout, a
stop, an invoke error, a failed Clean, or a compiler failure inside a cycle group that did not converge — reads
`never built` at once, because that is what the ledger records for it (§7.5) and what the next Sync will say.
The verdict travels with the failure event (`Evidence`) and is decided by the same gate that writes the ledger;
the application never re-reads the reason text. It does not wait for the engine's next preview, which may not
arrive until the next Sync. A success that still carries a dependency issue reads exactly the same `up to date`
as any other success — its own signature is genuinely current, taken straight from that success's own
event — even though it still drops out of the run's definite queue (`Conditional`, §10.2, unaffected by any of
this: the label stopped reading that flag, the run's own scope bookkeeping did not) rather than being counted a
plain success.

A cycle member is its own case, because its signature is never gated the way a plain project's is (§8.3): a
converged member's dep-issue note is genuinely recorded, but the member is never individually gated on it —
Build never compiles it and Cycles compiles it with its whole group — so its live row reads `up to date`,
exactly matching what the next Sync will say (`WaitingForDependency`, `WillBuild=false`,
`Conditional=false` — read no differently by the label than `UpToDate` would be). A member whose group did not
converge is different: the engine does not stand behind its green round, the ledger records it as a failure
without evidence, and the success event says so (`trusted: false`, §8.8). Its row reads `never built` in the
to-build grey at once — what the next Sync will say — rather than a green tick the next Sync would take back.
A cleaned project reads the same `never built`, because its ledger row is gone (§8.1), and so does a project
whose output file is missing from disk (§7.6).

**The slot is not a result column.** The state of the output is carried by the stripe, the dot and the glyph,
and what this run did by the duration and by the run-story surfaces (the ribbon, the console header, the event
stream); the slot always answers the same question — *what does this project's output need?* After a Sync that
answer comes from comparing the stored signature with today's — or, for an output built elsewhere, its time with
its inputs' (§7.6); after a build it comes from what just happened
to that project. Both are the same fact at different moments, which is why the wording does not change between
them.

The label replaced a commit pair (`a3f81c2 → b7e91d4`). That pair could not answer the question it appeared to
answer: its right half was a remote commit the user had not pulled, and its left half described the repository,
not the project. The revision is not shown anywhere else either: the decision is made from content, and a
commit next to it would suggest otherwise. A project page without a log says who produced the output only when
it was not this tool (`Never built by this tool`, `Built outside this tool`).

The **⋯** menu — also opened by right-clicking the row, as in Solution Explorer — offers *Build · Rebuild ·
Clean* scoped to that one project. It is anchored to the **row**, not to the ⋯ button: its right edge sits 8 px
inside the row's, it overlaps the row's bottom by 3 px, and it slides up to stay inside the list's viewport when
a row near the bottom opens it. Anchoring to the button would have meant a fixed offset standing in for the
width of the icons that follow it, and that number goes stale the moment the icon row changes.

*Build* and the play button start a **single-project run** (§8.1) — the project alone, its dependencies
untouched, and always compiled even when it is up to date — while *Rebuild* runs MSBuild's own clean-then-build
for it. Starting from a row is not selecting the row:
the selection drops and the filter stays, exactly as for a full run (§13.7), so a graph that was focused on some
node glides back to the fitted view and the console returns to the run log; the opening choreography marks just
that one row, and the ribbon pill reads `BUILD` or `REBUILD` with no target name — the target is named in the
console (`build requested — X (single project)`) and in the stream's opening line. Colour follows the same cut:
the engine's own preview for this run names only the target, so only the target's row turns queued-amber —
every other row, however dirty a stale Sync left it, keeps its standing colour for the run's whole life (§14.3).
While the run is in flight the target row's play button turns into a red **Stop** that stays visible without
hover and drives the same stop command as the action bar; every other row's play button is disabled and its
tooltip says why (`Build in progress — wait or stop it first`), and the menu's *Build* and *Rebuild* go the same
way. *Clean* is Visual Studio's project clean — `-t:Clean` on that project — and it locks with the other two
while a run is in flight. It is not the maintenance box's *Clean* — the workspace reset described under the
maintenance box below — and neither is the *Clean* in the Build split menu.

The row's icon buttons are the one place the shared icon-button style is overridden: they hover to
`surface-overlay` rather than `surface-raised`. The icons only appear while the row is hovered, and a hovered
row is already painted `surface-hover` — which is the same colour as `surface-raised`, in the design's own
tokens as much as here. Left shared, the hover was invisible exactly where it was needed. The rule the
override keeps is the one the action bar gets for free: the hover surface is one step above whatever the
button sits on.

Only one element in the row carries a design-system tooltip: the warning triangle. The status glyph carries
none — colour, glyph and the duration column were all saying the same thing — and announces its status through
its automation name instead. The icon buttons keep a plain, OS-delayed tooltip (the closest thing WPF has to
an HTML `title`) so that a mouse crossing the row does not trail balloons behind it. The building row carries a motionless
amber "breath" (an `amber-soft` layer at 0 → 0.32 → 0 opacity over 3.8 s) — that layer belongs to the row's
background, not to the glyph, which only turns; a sweep or a shine was tried and
rejected. It runs the row's full width, exactly as far as the bottom line and the hover band do: the row
itself has no padding, and the 10 px of air on the right is a margin on the content inside it. A failing row
shakes once, ±3 px over 360 ms.

The stripe has **no vertical inset**, which is a deliberate departure from §2.4. The design insets it by 1 px
so that adjacent rows cannot fuse into one unbroken rail; looked at on screen, the break made the same 2 px
read lighter and the row read thin. Separation is left to the horizontal divider instead — every row already
carries a `border-subtle` line along its bottom, and that line crosses the stripe and breaks it on its own.

Layer headers are 24 px and stick **cumulatively**: the *i*-th visible header pins at `i × 24 px` and stays
there as the ones below it pile up underneath.

**A layer header is a navigation control, not just a label.** Hovering it opens one surface step — background to
`surface-raised`, the bottom rule to `border`, the caps name and mono row count to `text-secondary` — over the
existing 120 ms transition, with a hand cursor and a native `Jump to <layer>` tooltip that, like the row's icon
buttons, opens after the OS hover delay (`AppTooltipDefaults.NativeDelayMs`, set on the header style) rather than
instantly. Clicking it (in-flow or the stuck overlay copy — both share the one `HeaderTemplate`, so the wiring is one
handler) scrolls the group's first visible row to sit just beneath the stacked headers above it; the target is pure
arithmetic (`LayoutMetrics.JumpTargetForHeader`, §13.4), the motion is the same smooth scroll the list already uses
elsewhere, instant under reduced motion. A click only counts if the press that started it landed on that same header
(the header captures the mouse on press and checks it still holds capture on release) — pressing a row and dragging
onto a header before releasing must not jump. Capture routes the release back to the header wherever the pointer is,
so the release must also land inside the header's own bounds (press, drag away, release cancels, as a native click
does), and the header must still be bound to the slot it was pressed on — a recycled in-flow container can carry the
capture over to another layer's data. It never touches selection, the filter, the console or the graph — only the
scroll position moves, and there is no collapse. The jump is a user scroll like any other, so it pauses
follow-mode (below): the smooth scroll it starts would otherwise clear the pause the way any programmatic move does,
and the next follow tick would pull the user straight back to the frontier. The header is deliberately **mouse-only**: the design prototype asks
for `role="button" tabIndex={0}` plus Enter/Space, but this list's existing keyboard model (§13.9) already owns the
arrow keys — rows are the only focusable stops, and `DirectionalNavigation="Contained"` walks exactly the focusable
elements inside the list, headers included, the moment any of them becomes one. Keeping the header's root a `Border`
rather than a `Control` keeps it `Focusable=false` for free, so it never enters the Tab order or the arrow-key
traversal; making it a focus stop to answer Enter/Space would put headers in the path of "arrow keys move between
rows," which the list's keyboard model does not allow. The stuck overlay copy is hit-test-visible for the same reason
a header is clickable at all — most clicks land there, since it is the one users actually see. Because it sits beside
the `ScrollViewer` rather than above it in the visual tree, a wheel notch over a stacked header would otherwise never
reach the list *and* would skip the bookkeeping every other user-scroll already gets (cancelling an in-flight smooth
scroll, pausing follow-mode, resetting the idle-resume window). The band's wheel is therefore re-raised on the
`ScrollViewer` so it actually scrolls; the bookkeeping needs no forwarding of its own, because the list wires that
signal at its own root (§13.4), which is an ancestor of both the overlay and the `ScrollViewer` — so scrolling over
the stack behaves identically to scrolling anywhere else in the list.

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

**A row waiting for its reveal is never painted.** The row surface is closed the moment the items are handed
over and reopened by the reveal itself, so no frame can show the rows at full opacity before the stagger hides
them. Without that the order was paint, hide, fade: the reveal is queued at a priority *below* render, so
rendering ran first. The graph never had the problem because it gives birth to each node already transparent;
the list cannot, because WPF owns its containers, so it closes the surface instead. A silent refresh — the
filter path — leaves the surface alone, or every keystroke would cost a blank frame. A reveal that is refused,
under reduced motion or while another hero holds the stage, still reopens it.

**The reveal plays when the structure changes, or when the user starts over.** A topology whose structural signature — node ids,
names, layers and edges — matches the last one is reconciled in place: rows stay where they are, the scroll
does not move and the graph is not rebuilt; the new decisions simply arrive on the existing rows. Only a
different structure (a project added or removed, a moved layer, a changed edge) rebuilds the list with the
staggered reveal and — when nothing is selected — scrolls it back to the top, while the graph replays its own
reveal in the same moment, so the two read together as "listed from scratch". The rule holds for the Syncs that
run on their own and the appended ones (§10.2), because a list that jumped back to the top on every commit or
return to the window would take the user's place away from them. The Sync button and a branch change are the
user saying "start over", and they get exactly that: the list and the graph blank with the console at the
request and come back with the reveal, back at the top, even when the structure is unchanged — or, if the Sync
brings no topology, the previous surface comes back. While the surface is blank, previews and row
updates do not quietly refill the list; only the topology's reveal (or the restore) does. The click of Clean
and Optimize, and a real repository change, empty the surface and forget the signature, so the Sync that fills
it again always reveals.
Build, Rebuild, Clean and Resolve leave the scroll where it is: the opening choreography already tells their
story, and the row under the pointer must not run away.

Follow-mode keeps the frontier visible while a run is in flight and nothing is selected: at most one scroll
animation every 550 ms, and none at all if the target is within 54 px.

Three things stop it, and the first two are the same statement in different words: *I am looking at this*.
Selecting a row stops it, and clearing the selection resumes it. Filtering the list stops it too — under a
filter the user is inspecting a subset, and the frontier may not even be in it — and clearing the filter
resumes it. Neither gate is permanent.

Scrolling the list also stops it — the user's scroll always wins. What counts as scrolling is the raw input and
not the movement: a wheel notch, a drag of the scrollbar thumb or a click in its trough, a navigation key. All
three arrive through the one signal every scrolling panel here shares (§13.4), which is why dragging the bar pauses
follow exactly as the wheel does; reading the movement instead would be unable to tell the user's drag from
follow's own animation. Clicking a layer header to jump counts too.

That pause is not permanent, and it lifts by exactly one route: leaving the list untouched for five seconds.
Every further scroll restarts that window, so follow cannot cut in while the user is still scrolling, and
where the list happens to be — the frontier row on screen, off screen, the bottom — plays no part. Position
cannot tell "I came back to watch" from "I am reading here": resuming whenever the frontier row was near the
viewport meant that a user scrolling on the same screen as the compiling row had the pause cleared on every
follow tick and the viewport taken back continuously, while the same gesture away from the frontier waited
politely. One gesture cannot behave two ways depending on where it happens, so the resume has one gate.

**Console.** See §13.5.

**Event stream.** A capped list of chronological one-line events, cleared — like the console — whenever a new
section begins (§10.2): everything on screen belongs to the operation that is running. What decides the clear
is not the operation's kind but whether it opens a section — Sync from the ribbon button clears both panels,
since nothing precedes it, and so does a branch change, whose section starts with the switch line; a Sync that
Settings' Save sends does not, because Save already wrote the console's first line (the new layer count, or
the new root, §13.3) an instant earlier, and that line belongs to the run about to start rather than to the one
before it. An automatic Sync clears nothing and adds at most one line of its own (`synced after commit`,
`synced · N projects changed`), as do the branch-change interrupt (`interrupted by branch change`) and the wait
for a git operation (`waiting for git — …`). A git refusal adds one short `warn` line — a branch switch refused
on a dirty tree, a pull refused (§10.3, §10.5): no glyph (the amber `▸`, like `sync` and `info`), text in the
same amber the console gives a `warning:` line, and typed like `info` rather than printed at once like a
failure; it carries no project, so it is not clickable.
The **plan surface** — rows, graph nodes, the cycle map, the *to build* count — follows its own rule. No Sync
empties the plan: the Sync button and a branch change only blank the list and the graph on screen and bring
them back with the reveal (§10.2), and the other kinds reconcile the rows in place, replaying the reveal only
when the structure changed; only the click of Clean or Optimize and a real repository change empty the plan
itself. The stream is not virtualized and does not need to
be: the buffer is trimmed from the front to a render slice, so the panel is bounded by construction, and rows
are inserted and removed one at a time as events arrive rather than rebuilt in bulk. Virtualization would also
cost more than it saves here — each row owns animation state (a done line glows
once), and recycling containers would swap the model underneath a running animation. Events arriving less than
340 ms apart and all error events print instantly. Rows for projects are clickable and
participate in the shared selection. A run that finishes with zero failures glows its done line once
(`success-soft` → transparent over 1.1 s) — that is the *entire* success flourish; there is no green wave
through the list or the graph.

**Every row answers hover, one step apart — but the once-only flourish always wins first.** A clickable row (one
carrying a project id — `ok`/`fail`/`skip` lines, and a cycle-round `info` line) steps to `surface-hover` and swaps in
the hand cursor; a row with nothing to click — `sync`/plain `info`/`warn`/the closing `done` summary — steps to the quieter
`surface` instead and keeps the plain arrow, so long-log tracking gets the same visual foothold without implying a
click that would do nothing. The selected row's own `surface-raised` outranks both and does not move under the
pointer. A background step on a non-clickable row could in principle fight the done line's once-only flourish. The two
*can* meet — the done line is exactly the row the flourish plays on, and it is never clickable — but the flourish does
not budge for hover: it is a CSS `@keyframes` animation in the design that owns the row's background outright for its
full 1.1 s regardless of what the pointer is doing, the same way the row's own colour or the typewriter cadence cannot
be interrupted mid-flight either. `EventStreamRow` mirrors that ownership with one flag (`_glowRunning`): while the
flourish's clock is live, `ApplyBackground` does not write to the ground at all — a mouse arriving mid-glow is
*remembered*, not applied, and a mouse leaving mid-glow is forgotten the same way. Only when the flourish's own clock
completes does `ApplyBackground` run once more, this time settling on whatever the row's *current* hover/selection
state actually is — hover ground if the pointer is still there, transparent if it already left. The flourish still
plays exactly once regardless — this dance is entirely about who owns the ground while it runs, and has no bearing on
the one-shot guard in `StreamEventViewModel.GlowPlayed`. The active prompt line at the foot of the panel (§2.6, the
live `{name} building…` indicator) deliberately sits outside all of this: it carries a fixed hand cursor and never
steps its background on hover, in the design as much as here — it is a status line, not a stream row, and has nothing
of its own to select.

**Action bar.** Sync; the maintenance box; the counter chips, each a filter toggle. Four of them are always
there: `Σ` (all — clears the filters), building (a spinner while something compiles, a grey dot otherwise),
`✓` up to date and `✗` failed; one more appears **only when the list actually holds one** —
`⚠`, the combined warning chip (a dependency cycle *or* a dependency issue). It describes an exceptional
situation, and carrying it permanently as an empty grey chip weakened the signal.

The two glyph chips count **state**, not the last run's results: each counts the rows that *show* that
state — `✓` every green row (up-to-date output, whether this run skipped it, built it or never ran), `✗` every
red one. The bucket is read from the row's visual status in one place
(`VisualStatuses.StateOf`, surfaced as `ProjectFilter.StateKey`), and `RunCounters` and the filter both ask it,
so pressing a chip lists exactly as many rows as its badge says. The grey to-build bucket obeys that same one
rule — a failure without evidence (a timeout, a stop, an invocation error) leaves its output stale and the row
grey, so it counts as to-build rather than failed — but the bar carries **no chip** for it: every grey row
already says *to build* in its own decision label and glyph, and a badge in the bar was the same sentence
twice. Rows the run is queueing or compiling, rows lit by the marking wave and rows with no decision yet are
in no state bucket at all.
The counts are taken when a run event arrives, not on each step of the marking wave, so while the wave lights
the scope the badges still show the state from before it; they catch up when the run starts. The run's own
preview clears the marks first and counts second, so a marked row the run does not queue is counted in its own
bucket from that moment.
Building counts only what is compiling right now — a queued row, or a cycle member waiting its turn, is not
building. There is no skipped chip: being skipped is not a state — a skipped row keeps its standing's colour —
and the `—` glyph belongs to run-story surfaces only. The run's own tally (succeeded · failed · skipped ·
dependency-affected) stays where it is, in the ribbon's completion line.

The chips **combine**. The active filter is a set: chips toggle independently and the selected ones are OR'd
together — `✓` plus `✗` reads as "up to date or broken" — while the search box is AND'ed on top. An active chip
lights in its own status colour (green, red; amber for building and warnings), and the
removable chip in the PROJECTS header lists the selected set joined with ` + `. Pressing a filter also drops the
selection: a selection locks the graph camera onto one node, a filter says "look at this set", and the two
fought each other. A filter reaches the **graph** too — nodes outside the visible set fade to the same 0.1 the
unfocused set uses — except while a run is being told there, when the graph sets it aside (§13.6). The matching rule lives in one place (`ProjectFilter.Matches`): the graph is handed the
list's visible names and never writes a second matcher, so the chip, the list and the graph can never disagree.

**The whole bar speaks one hover language.** Every neutral control — Sync, the three maintenance icons, the
counter chips, `N behind`, the branch and perf chips — steps to the same `neutral-700` ground with a
`neutral-500` hairline on hover, and its label and icon whiten to `text-primary` together; a control that
carries its own status colour (a status glyph, the building spinner or dot, the warning triangle) keeps that
colour through the hover, because there colour is a status, not a hover state. `Σ` is the one partial exception:
its icon rests one shade dimmer than its neighbours (`text-dim`, matching the design system's own chip-icon
rule) rather than sharing the chip's own resting `text-secondary`, so it answers hover through its own channel
— dim at rest, the same `text-primary` on hover — instead of simply following the chip's foreground the way the
branch and perf icons do. A control that is already open or checked — a lit filter chip, an open
branch popover — steps instead to `amber-soft-hover` with an `amber` hairline, and its text stays the
fixed `amber-text` it already had: hover never overwrites what the state itself already said, and the two
readings are mutually exclusive by construction (an unchecked and a checked control never answer the same
trigger, so there is no race for the checked one to lose). The one control that opts out is the
`Debug | Release` segment, where only the *unselected* option answers hover (`surface-raised`, `text-secondary`)
— the selected one already sits on `surface-overlay`, and the two would blur into each other. Build and Stop
keep their own primary/danger hover; they are the bar's one loud control. A disabled control never hovers, on
top of the 0.45 dimming every control already carries. The one exception is a *running* Sync or maintenance
job: its command is closed while the work is in flight, but the button is drawn live on purpose (below) — and
WPF excludes a disabled control from hit-testing altogether (the same reason a disabled button needs
`ToolTipService.ShowOnDisabled` to show a tooltip at all), so the button's own hover would never fire. Each of
the four keeps its own always-live wrapper — an otherwise invisible `Border` occupying exactly its bounds —
whose `MouseEnter`/`MouseLeave` is what
actually answers hover in that window; the button's real `IsMouseOver` answers it everywhere else.

The remaining bar carries the **workspace label** (mono, the repository root's folder name, tooltip the root
itself); the branch chip, which names the branch checked out in the working tree and whose searchable popover
checks another one out (§10.3); the `N behind` chip (§10.5) — drawn whenever the distance is known and greater
than zero; the `Debug | Release` segment;
the perf chip; and the Build split-button, whose menu carries exactly three items in every phase: *Build — Only stale
projects*, *Rebuild — All N projects — cache ignored* and *Clean — Remove build outputs — next build is full*.
There is no *Continue* and no *Retry failed*: a stopped run is started again and a failed one is built again,
and *Build* already covers both sets (§8.1). *Clean* here is `-t:Clean` over every solution with the caches
untouched, and it is the one surface in the bar whose engine is not written: the item keeps its place, drawn
disabled, and its tooltip names the job before saying it is not available yet. It is neither the row menu's
project clean nor the box's *Clean*, a different operation described below. While a run is in flight the
primary button becomes *Stop*, and the
branch chip and the configuration control lock; the perf chip stays live.

**The branch chip is a git command, and it is gated like one.** It is enabled only when a workspace is open,
no run is in flight or being planned, no workspace operation (Sync, Clean, Optimize, a checkout, a pull) is in
flight, the engine is available and git is not mid-operation. The last case is the only one the chip explains
on its own: a 6 px amber dot sits on it and its tooltip names the operation (`Merge in progress — finish or
abort it in git`); the same tooltip replaces the `N behind` chip's while the pull is locked for the same
reason (§10.3). A checkout that is in flight holds every one of those gates for itself: until the engine
answers, Build, Sync, the maintenance jobs and the pull are closed, because a run started on the new tree would
have its console cleared by the checkout's section, and a pull would advance the wrong branch.

**The maintenance box.** Three icon buttons in one chip-weight box — *Clean* (eraser), *Optimize* (gauge) and
*Resolve cycles* (unlink) — 24px tall, `surface-raised`, one hairline border, `radius-xs`, clipped, with a
1px×14 divider between the buttons. The buttons carry no label: three labelled buttons overflow the bar at its
1240px minimum and crush the Build split-button, so the meaning lives in the tooltip, which stays readable
while the button is dim (§13.8) — a button dimmed mid-run is exactly where the reason has to be legible. None of
the three draws a hairline of its own on hover — the box's own border is the only edge the strip shows, so
hovering a button answers with ground and icon only, size and dividers untouched. All
three drive real commands, and **none of them writes its own enabled state**: that is the command's
`CanExecute` alone, so the strip can never disagree with the engine behind it. *Clean* is the workspace reset
and *Optimize* the workspace repair, both described below. *Resolve cycles* is the cycle run, disabled while
the topology has no cycle. Its icon is neutral: orange left the
interface entirely, so there is no longer a structural channel for it to echo — the presence of a cycle is
carried by the button's enabled state and its tooltip.

*Resolve cycles* drives the row list and the graph too, and its own scope (§8.1: members plus their transitive
upstream) splits the plan-coloured rows in two. The queue colour — the amber a row wears while it waits its
turn — lights only the cycle members: a stale upstream dependency this run also pulls in and compiles keeps
its standing colour until its own `projectStarted` arrives, exactly the "queued reads only the running
operation's own plan" rule §14.3 states for `WillBuild`, narrowed one step further for this one mode. A row
genuinely outside the scope never turns colour at all, and it never turns `Skipped` either: the engine's own
pre-skip for it (`skipped — not needed by a dependency cycle`, folded into the stream's one collapsed line,
§8.1) does not reach the row's status or colour, the state filters, or the run's skipped total — those read the
row exactly as a Sync left it, for the run's whole life. The one place the pre-skip does reach is the row's own
project page: it states the same reason, because the run's own preview already forced the row's will-build flag
`false` (every pre-skipped project's is, regardless of why, §8.1) and a page that said nothing would read a
possibly-dirty, merely-out-of-scope project as `Up to date`. Only a row the run actually touched — a member, or
the upstream it pulled in — can end the run coloured or counted.

The box sits next to Sync rather than next to Build, and the placement carries the meaning: these are things
you do *before* a build, and the separator on their right belongs to the counters. Beside Build it would read
as a variant of the primary action, which it is not — it is a run of its own (§8.1) with the same icon the
rows and the graph use for "this project is in a cycle". It is disabled unless the workspace actually has one,
because in a workspace without cycles that run would skip every project and do nothing; a disabled button says
so before the click rather than after. Its tooltip carries the same fact in numbers once there is one to
report — `Build dependency cycles — N cycles · M projects` — and falls back to the plain label when the
workspace has none. The accessible name is unaffected either way: it stays the plain label, since a screen
reader announces what the control does, not a count that moves under it on every Sync.

**Clean is the workspace reset.** The eraser wipes the build output of the current workspace — the `bin` and
`obj` folders of every project the engine discovers, **registered external roots included**, and the
`build-state.json` entries of all of them — so the next *Build* compiles everything as never built. An external
project is an ordinary project here as everywhere else: the same resolver that merges those roots for a Sync or
a run merges them for a Clean, and each root's ledger entries are swept on its own key prefix. It is neither
the row menu's project clean nor the Build menu's *Clean*: no MSBuild target runs, the deletion is on the file
system alone, and the reasons are in §5.2. There is no confirmation dialog — the work starts on the click,
because the only thing it removes is output the next build reproduces. The engine's progress runs through the
console line by line — a line per project whose `bin`/`obj` was removed, a warning for each project with files
in use, a warning for a card that resolved to nothing — and the event stream gets one closing summary:
projects, folders, bytes freed and, when there were any, files in use. A file held by a running application is
skipped and counted rather than treated as a failure, the flow never stops for it, and the closing warning says
to close the application and press *Clean* again. Because it removes `obj` outright, it also removes the cause
of the stale-`obj` warning a run start can raise, rather than suppressing it.

**What may be deleted is decided by the resolved project set, not by a path prefix.** A folder goes only if it
sits directly inside the folder of a csproj this workspace resolved and is named `bin` or `obj`. Anchoring the
gate to the project set rather than to "under a registered root" answers two questions the prefix rule got
wrong in opposite directions: a `bin` under the root that belongs to no project was deleted, and a project an
external `.sln` lists outside its own folder was not. The ledger follows the same set, because a surviving entry
for a project whose output is gone is the one failure this design is built to prevent — the next *Build* would
skip it as up to date and report green over deleted outputs.

**The click empties the plan surface, and the Sync that follows fills it in again.** Rows, graph nodes, the
cycle map and the *to build* count all go at the moment the button is pressed, in the same frame as the console
and the event stream: the outputs are about to be deleted, so nothing on screen answers to anything on disk any
more, and dropping the plan at some later instant would read as a second jolt in one operation. **Optimize
behaves identically**, and for the reason that generalises the rule: an operation that is about to replace the
plan takes the old one down with the click, not with the reply — and an Optimize ends in a Sync. A Sync on its
own does not take the plan down: it replaces decisions, not the plan's structure (§10.2).
The phase moves
to `Boot` for the duration, which is what makes an empty list honest — the list invite reads an empty list in
`Idle` as "no projects under this folder", which would be a lie, and the graph shows its own *appears after
Sync* empty state. A repository change does exactly this for the same reason; a branch change does not — its
rows and decisions stay in the plan, only the screen starts over until its Sync's topology brings them back
(§10.2). Because the emptying happens at the
click, a command that fails to send, or one the Supervisor rejects, leaves the list empty until the user runs a
Sync; that is the accepted cost of acting on the click rather than on the engine's acceptance.

This costs no extra waiting in practice. *Build*, *Rebuild* and *Resolve cycles* are already shut for the
whole of a Sync, a Clean or an Optimize — and of a checkout or a pull, the same busy question (`WorkspaceBusy`)
— and the plan arrives in
the same batch that clears the
in-flight flag, so both halves of their gate open together. What the emptying does change is the failure case:
a Sync that never delivers a plan — the engine is gone, planning failed — leaves the surface empty and those
three shut until a Sync succeeds, where before they stayed enabled against a list that no longer described
anything. That is the same cost Clean already accepted, and it is the safer end of it: a Build against a
surface the user cannot see would compile a set nobody chose.

**The step always plays for the same length.** On a small workspace a maintenance job finishes in milliseconds,
so the spinner would flash and the Sync's animations would land on top of it. Clean and Optimize therefore hold
their step
for the design's neutral beat measured from the click — the spinner keeps turning, the gate stays shut — then
ends the step, waits the short beat, and only then starts the Sync. The rule it follows is the one the opening
choreography already established: a choreography either always plays or never, because a step that appears only
when the engine happens to be slow makes the same click feel different every time. The timing lives in the
shell, as it does for the choreography: the view model says how long to wait, a dispatcher timer counts it, and
under reduced motion nothing is waited at all.

**The Sync is chained, not asked for.** `cleanCompleted` starts a Sync with the console preserved — the same
shape as the `N behind` chip's pull — because the engine's own analysis is the only thing that can put real
decisions back on the rows the click cleared, and the alternative is asking the user to press *Sync* for
information the application can fetch itself. A row reading `up to date` cannot go on saying so once its `bin`
is gone, and a green status answers to nothing on disk; that is why the plan surface goes at the click rather
than waiting to be corrected. An Optimize chains the same Sync for a plainer reason: its click cleared the rows
too, and the two maintenance buttons sit side by side in one box, so they answer a click with one flow. A failed
job chains nothing: the reason is already in the console, and a second error line on top of it would only be
noise.

**The gate is handed over, never dropped.** It stays shut from the click until the chained Sync has claimed it,
which covers `cleanCompleted` or `optimizeCompleted`, the held step and the beat after it — one handover shared
by both jobs: the two operations read as one busy stretch,
nothing brightens in between, and the spinner keeps turning until the handover. Releasing it before that beat
left a window where *Sync* and *Clean* were clickable and the buttons blinked from dim to live and back. The
handover is synchronous — the Sync claims the gate before its first await — so there is no instant in which
neither holds it. If the command cannot even be sent, the Sync releases its own gate and the release that
follows is then the correct answer. The gate opens on every exit, an engine death mid-job included, and the
silence watchdog (§4.6) covers the wait. Each button carries the state its tooltip
cannot: while its own work runs it takes the amber `active` ground, its icon becomes the spinner (§14.4), and
the ordinary disabled dim is suppressed so a running job never reads as a switched-off one. All three obey the
one rule, so the box always says which of its jobs is in flight while the other two sit in the dim.

**Optimize is the workspace doctor.** The gauge repairs what would stop a build before it starts and names
what it cannot repair. It restores the NuGet packages missing from disk, then reports project by project the
references a restore cannot fix — a `packages` path at a version nobody produces, a `bin` reference whose
producing solution has not been built — so a run does not die on a cryptic compile error over something that
was knowable at the click; after that it clears the build-breaking residue and the dead ledger entries behind
it. It walks the same resolved project set a Sync or a Clean walks, registered external roots included, and
§5.2 carries the full scope and the list of what it leaves alone. Like the Clean it asks for no confirmation:
nothing it removes is anything a build needs — stale residue, dead ledger entries, and the temp files an
interrupted write abandoned.

**Clean and Optimize are opposites in what they remove, and one flow on the surface.** Clean deletes `bin` and
`obj` outright so the next *Build* compiles everything as never built; Optimize deletes only the NuGet residue
*inside* `obj` and leaves the rest where it is. A row's decision is read from source content (§7.1), and neither
a restore nor a residue sweep makes a project stale, so the Sync that follows an Optimize puts back the decisions
its click took away. The surface still follows the Clean's rules above — plan cleared at the click, step held,
gate handed to the chained Sync — because two jobs in one box that answered the same click in two different ways
would read as two different kinds of thing. What the user sees is the console reporting each step's result, even
a step that found nothing — packages, references, `obj` leftovers, caches — then one closing summary in the
event stream, and a pill reading `OPTIMIZE` until the Sync takes it over.

**The two workspace jobs share one gate.** A workspace must be selected — a topology is not required, both
services scan for themselves — the engine must be alive, and no run, Sync, Clean, Optimize, checkout or pull may
be in flight.
The exclusion is mutual and complete: while either of them runs, *Build*, *Rebuild*, *Resolve cycles*, the row
actions, Sync, the `N behind` chip **and the other maintenance button** are all closed. Every pair of them is a
race on the same workspace — deleting `bin` under a compiling MSBuild, a Sync (the automatic one after a pull
included) reading folders that are disappearing, a restore writing into an `obj` a Clean is emptying.

**A running job is amber wherever its button is.** *Sync* speaks the same language as the maintenance box: while
a Sync is in flight its button takes the amber ground and its icon becomes a spinner of the same size, with the
*Sync* label left in place, and the disabled dim is suppressed so the work reads as live. The signal is the Sync
surface itself, request window included, so the Sync a Clean chains looks exactly like one the user asked for —
the indicator belongs to the work, not to whoever started it. This is a **deliberate departure from the
prototype**, which leaves the Sync button merely disabled and lets the ribbon's operation pill carry the whole
story: two neighbouring jobs on one bar, one spinning and one inert, described the same state two ways. The
pill's own narrative is unchanged; this is an addition to it. Hovering a running button deepens the same
surface once more — `amber-soft-hover` ground, and for Sync an `amber` hairline — the bar's single hover
language extended to its one control whose command is closed but whose surface must still read as live; the
*Sync* label stays out of amber either way, since the button is named, not restyled, by the work running under
it. That hover answers through the button's always-live wrapper, not the button itself — the button is
genuinely disabled in this window (its command's `CanExecute` is false), and WPF excludes a disabled control
from hit-testing altogether, so its own `IsMouseOver` never becomes true no matter where the pointer sits.

**No run without a topology.** *Build*, *Rebuild* and *Resolve cycles* stay disabled until a Sync has published a
topology, and an empty one (a folder with no projects) keeps them disabled. The reason is that the full analysis
runs only in Sync (§6): a run publishes `buildPreview` but never `workspaceTopology`, so a build started before
the first Sync would compile for real while the list, the graph and the counters stayed empty — the user would
be watching a run without being able to see what it is doing.

**Nothing starts while a Sync is in flight** — not a run, and not a second Sync. The engine's command loop
blocks for the duration of a Sync (§5.2), so anything pressed in that window is not merely queued, it lands in
the middle of someone else's transcript: a run clears the console buffers and writes its own request line while
the Sync's remaining progress lines are still arriving, and the reader is left with two interleaved stories.
A second Sync is worse value still — it re-runs the whole analysis, scan through incremental, and every press
sends three commands, so the ribbon walks `Syncing → Idle → Syncing` while the console prints the same
transcript twice.

The gate opens at the **click**, not at `syncStarted`, for the same reason the run lock does: sending takes
milliseconds and the engine may not reach the command for seconds, and a button that re-enables in between
invites exactly the second press it is there to prevent. It closes again on every exit — the answer arrives,
the send fails synchronously, the Sync fails, or the engine dies — so no path leaves a button permanently
dark. Sync remains the way out of an empty topology; what it no longer is, is a way to interrupt itself. A
Clean or an Optimize opens and closes the same gate under the same rules (see *The two workspace jobs share one
gate* above).

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
shadow, and a 140 ms pop-in (4 px up, scale .985 → 1). Outside click, Esc, or a second press on the trigger
that opened them closes them. That last one needs saying because WPF does not give it for free: a popup that
closes on outside clicks drops its `IsOpen` while the press is still travelling, and the same press then
re-checks the trigger and reopens it — the gesture cancels itself out and the popover cannot be closed by
the control that opened it. One gate (`PopoverToggle`) closes that window for all four popovers — branch, the
Build chevron, the row menu and the Open-in-VS chooser. Rows inside them are 28 px. The branch popover is
272 px wide and carries a search box; picking a row is a checkout (§10.3), picking the active branch does
nothing, and the remote `origin/HEAD` pointer is not listed.

The branch list is virtualized, and a popover only builds rows while it is open — closed, it does nothing at
all when the inventory changes. Both matter more than they sound: a real repository carries hundreds of refs
(`refs/heads` plus `refs/remotes`), and every Sync — the automatic ones included — republishes the inventory.

That is also why the inventory is a **snapshot rather than an incrementally mutated list**. `Branches` is
replaced wholesale and emits **at most one** change notification per publish — and none at all when the
content is identical, which is the common case, since every Sync asks for the inventory whether or not anything
changed. Reconciling item by item would emit two notifications per entry, and with a listener rebuilding on
every notification the cost is quadratic in the number of refs. The reset that a
wholesale replacement implies is safe here, unlike in the projects list: there is no container identity or row
selection to preserve. The branch the chip shows is not a choice the list has to keep: it is the inventory's
active entry — HEAD — read afresh on every publish, and `syncCompleted` aligns it even earlier (§5.3). On a
detached HEAD the last known name stays.

**The three modals — Settings, About and What's new — share one shell** (`ModalDialog`, with its look in the
`Ds.ModalDialog` template). It owns everything that is not content: a full-bleed scrim, the `Ds.Dialog` frame
centred on it (`surface-raised`, a `border-strong` hairline, radius 8, the overlay shadow), and inside that frame
a vertical stack of head, an optional tab strip, a body that takes the remaining height, and an optional footer
strip padded 12 px × 18 px under a `border-subtle` hairline. The frame's content is clipped to the frame's inner
rounded corner — a plain rectangular clip would let a coloured rail or footer paint over the corner. The frame
takes a design width and, optionally, a fixed height, and both are capped at the host's size minus 48 px — the
host being the dialog's own area, which the scrim stretches over the whole window — and re-capped whenever the
window is resized; the arithmetic lives in one pure function (`DialogSize`). The shell's behaviour is shared the
same way: a scrim press closes the dialog while a press inside the frame never reaches the scrim, Esc closes it
and is marked handled, the scrim is a cyclic focus scope so Tab cannot escape to the window behind, and opening
lays the dialog out before moving keyboard focus inside — to the first control of a subtree the dialog names
(Settings names the page it opens on), or else to the first control of the dialog — because before layout,
focus navigation finds nothing and focus would stay on the dialog itself. Every modal enters with a 180 ms fade and a 6 px rise, the
duration read from the `Duration.Base` token and snapping to the end state under reduced motion. The dialog's
typography (the UI font and `text-primary`) is set on the dialog rather than the frame, because the slot content
is logically parented to the dialog and WPF value inheritance follows the logical parent. No dialog file
re-implements any of this; a source guard keeps it that way.

The Settings dialog is a fixed 880 × 576 px, split into two panes under a head row that carries the title and a
close button taking the same path as *Cancel*. Down the left runs a 196 px **section rail** on the `surface`
tone — a step darker than the dialog, which is what makes the two panes read as two — listing **General**,
**Workspace**, **External projects** and **Layers**. The active row sits on `surface-overlay` in `text-primary`;
the others are transparent in `text-dim` and take `surface-raised` on hover. There is no amber here: accent is
reserved for status. The rows are radio buttons, so the rail is reachable and navigable from the keyboard.
External projects and Layers carry a mono count of the draft's cards on the right, drawn only while it is above
zero and following the draft live. The right pane scrolls on its own, so switching sections never resizes the
dialog, and its scrollbar column stays reserved whether a scrollbar is needed or not — a list crossing the
overflow threshold does not shave pixels off every input already on screen. All pages share that one scroller,
so switching sections starts the new page at the top rather than at the previous page's offset. Every page opens
with the same head: a title and a single line of description. The dialog opens on Workspace on first run — the
one setting the tool cannot run without lives there — and on General every time after that, with keyboard focus
on the first input of that page: the repository root input, or the first switch.

The rail exists because settings grow. A single column put every section under the previous one and each new
setting squeezed it further; a section list keeps each page short and gives the next settings a place to land
without widening the dialog. **General** is that place. Its rows come from one catalog
(`GeneralSettingsCatalog`) in four groups — *Startup* (*Start with Windows*, *Start minimized to tray*, *Close
to tray*), *Build* (*Pull before build*), *Branches* (*Stash and switch branches*, §10.3) and *Notifications*
(*Show notifications*) — and every row is drawn by
one template (`Ds.Settings.ToggleRow`): the name over a single line of description on the left, a switch on the
right, a hairline between rows but not above a group's first. Adding a setting is adding a catalog row; there is
no layout work. A row that depends on another (*Start minimized to tray* on *Start with Windows*) fades to the
switch's own disabled opacity and stops taking input while its parent is off, without moving anything. Only
*Pull before build* and *Stash and switch branches* drive behaviour so far; the other four switches live in the
draft alone (§20). *Stash and switch branches* follows the pull switch's rules: saved with *Save*, carried to
the engine on the next checkout, and a console note written only when its value actually changed.

**Workspace** is a mono repository-root input with *Browse…* beside it and a note underneath saying it is
required. The root is the one setting the tool cannot run without, so *Save* stays disabled while it is empty.

**External projects** come before Layers in the rail on purpose: they build *before* everything the repository
root discovers. A card is a path — a folder, a solution or a project file — in a full-width mono input, and the
git working copy is found from that path; with a single column there is no column header. Cards share the layer
card's parts byte-for-byte — the same 36 px shell with its border, radius and raised-on-drag look, the same grip,
remove button and *Add* button styles, the same `Mouse.Capture` reordering — and the two lists reorder
independently, each against its own collection. An empty path on any card disables *Save*, the same severity as
an empty layer name. The list starts **empty** and shows the same dashed empty-state box the Layers page uses.
*Add external project* appends a blank card.

*Pull before build* — whether every build refreshes these working copies first (§10.4) — is a switch on
**General**, not on this page: it is a behaviour of the build, and General is where the dialog collects those, so
the external page stays a list. The page still says where the switch went and what it is set to: under the cards
a hairline and one line read *Card order sets the order the working copies are updated. Updating them before a
build is on* (or *off*, following the draft live), followed by a ghost *Pull before build* button that moves the
rail to General. The switch is deliberately not a chip in the action bar: that bar carries per-run choices
(configuration, perf, branch), while this one follows the external list and changes rarely. It follows
the same draft rule as everything else here: nothing is applied until *Save*, it defaults to on, and *Clear*
returns it to on rather than off.
The list is written to disk on *Save* and travels with every Sync and Build command (§5, §10.4): Sync scans
each card's path and the projects it finds join the graph as ordinary rows, Build updates their working copies
first and then compiles them in dependency order. A path is only validated when it is used — the dialog does
not scan it — so a card pointing at nothing buildable is a warning in Sync and a refused run in Build, not a
red input here.

The root lives here rather than behind a folder picker because starting takes more than one setting now — a
root and, optionally, the layers — and a picker can only ask for one of them. That is also why the empty
project list invites the user *here* rather than opening a picker of its own (§13.2).

Building dependency cycles is **not** a setting: it is a run of its own, reached from the maintenance box
beside Sync (§8.1, §13.2). A preference would have been the wrong shape — the question is not "should this
tool ever build cycles" but "do I want to pay for it right now", and that is answered per run.

Layer cards are 36 px and reordered by dragging the grip with `Mouse.Capture` and a half-row swap
threshold — `DragDrop.DoDragDrop` is prohibited, because the OS ghost-drag semantics do not match the design.
Neighbours snap without animation. An invalid regex puts its input into the invalid state and disables *Save*.

When no layers have been saved, the Layers page opens **empty** — the tool carries no product-specific defaults —
with the dashed empty-state box; the *Layer name* and *Pattern* column headers appear only once a row exists.
*Add layer* appends a blank row whose inputs show placeholders rather than values, taken by row index from one
product-neutral list (`Core` / `^MyApp\.(Core|Common)\.`, then `Infrastructure`, `Domain`, `Services`, `Api`,
`Client`, wrapping after six — `LayerPlaceholders`). A placeholder belongs to the position, not the row: after a
drag every card shows the pair for its new index.

*Browse…* only writes the picked path into the draft's root input; Cancel, Esc and a scrim click discard the
draft — the pending root, external cards and all — without touching anything live.
*Save* is the single point where the draft is applied, in a fixed order: the layer patterns and the external
project list are applied first (both are app-side state; the order between the two of them does not matter),
then the pending repository root (which resets the project rows to hollow), then exactly one Sync is sent. The
order is load-bearing, because the Sync command carries the layer patterns and the external list — sent before
they were applied, it would carry stale ones: the grouping would be wrong for a whole Sync and the external rows
would describe the previous list. The Sync itself is unconditional: Save does not compare old and new state to
decide whether to run it.

The pull switch has a note of its own, and it is quieter still: it prints only when the value actually changed
*and* external projects are defined — `Pull before build on — external working copies update first`, or
`Pull before build off — external working copies are used as they are`. In a workspace with no external
projects the flag does nothing, and saying otherwise would describe work that is not happening.

The external project note is quieter than the layer one: the layer line prints on *every* Save, but the
external one prints only when the count actually changed — `External projects → 3 — built before the
repository projects`, or `External projects cleared` once it drops back to zero — so a Save that only touched
layers stays quiet about a list it did not change.

Three gates hold. While a run is in flight — or a workspace operation is: a Sync, Clean, Optimize, checkout or
pull — the layer patterns and the external project list are applied but the repository root is left alone and
no Sync is sent, since pulling the root out from under a running build or operation would be wrong and a second
Sync behind the operation's own would be a double Sync; because the dialog's label has already confirmed the
picked folder, a root change this gate drops is announced in the console as `Repository change deferred — run
in flight` (or `— operation in flight`), while a Save that carries no root change stays silent.
If no repository has ever been selected, there is nothing to Sync — that gate sits *after* the root is
applied, since the headline journey (a new user opens Settings, picks the root, saves) fills the root right
there. And when the engine is unavailable — the supervisor was never found, or would not launch — the layers,
the external projects and the root are all applied but nothing is sent: each send would fail and print an error
line contradicting the permanent ribbon message, the same reason Sync, Build and Rebuild are disabled in
that state. The root is still applied because it is local state that persists, and the first Sync after the
engine returns carries it.

A root that changes *later* announces itself in the console — `Repository root → D:\src\osys — Sync
required` — and nothing is reset: the user syncs when ready. The first setup stays silent, because a Sync
starts there anyway and the note would be noise.

**Export · Import · Clear.** The footer carries three icon buttons on its left. Export writes
`build-orchestrator-settings.json` — `{ app, version, repositoryRoot, externalProjects[{ path }],
pullExternalBeforeBuild, stashOnBranchSwitch, layers[{ name, pattern }] }`, the external array sitting
between the root and the layers (the field order the file is written in, not just a key that happens to be present) and holding only
cards with a non-blank path; import reads one back **into the form**; clear empties the root, every layer and
every external card, and returns every General switch to its default — *Pull before build* to on, *Stash and
switch branches* to off. Of General, only those two travel in the file. All
three touch the draft only: nothing is
applied until *Save*, and there is no confirmation dialog. Clear's confirmation is the button itself — the
first press turns the icon red and prints a warning, cancels itself after 2.4 s, and only a second press
empties the form. Feedback for all three sits on the same footer line for 2.4 s, green or red. A malformed
file is not an error but a result: the user picked the wrong file, and the line says `Invalid settings file`
while the form stays untouched.

While no feedback is showing and *Save* is disabled, that same footer line says why, faint and on one line,
whichever page is open: `Repository root is required`, `Every external project needs a path`, `Every layer needs a
name` or `Check the highlighted pattern`, in that order of priority. The draft derives the reason from the very
conditions that gate *Save* (`SaveBlockedReason`, with `CanSave` defined as "no reason"), so the button and the
line cannot disagree.

A file that omits `pullExternalBeforeBuild` or `stashOnBranchSwitch` leaves that switch where it is, the same
rule the external list already follows: a file cannot silently reset a setting it does not carry.

Import is tolerant on the way in: an `externalProjects` entry can be the object above or a bare path string,
the two forms the design package's own prototype accepts. Any other key on an entry is ignored — the `vcs` an
older file carries included (§10.4) — and an entry whose path is blank is skipped. A file that omits the key entirely leaves the draft's external list untouched, the
same rule the repository root already followed; a file that carries the key — an empty array included —
replaces the list outright, because the key's presence is itself a decision. The import feedback line reflects
that: `Imported — N layers · M external · root set`, with the `M external` clause appearing only when the file
carried the key at all.

The About dialog is the second modal on the shared shell; its identity block fills the head and the tab switch
sits in the shell's tab strip. It is 620 px wide and its height follows its content around a **fixed** 284 px body:
About is a static reference — who the product is, what it runs on, which keys it answers — and nothing in it grows
with use, so the narrowest of the three dialogs is the right figure. The longest line it carries, a full
`MSBuild.exe` path, still does not fit on one line at that width; rather than chase it with a wider dialog, that
line scrolls sideways (below).

It has no title row. In its place is an identity block (padded 20 px top and bottom, 18 px sides) that holds both
marks in one composition: the 28 px product mark, the product name with a mono **version chip** 9 px beside it —
19 px tall, a `border-strong` hairline on `surface`, the application version in 11 px — and the one-line tagline
4 px below. The company lock sits opposite — a 28 px hairline, a tracked `LICENSED TO` label and, 6 px below it,
the company logo — and drops out entirely when there is no company logo. The version appears **once** in the
head; the copyright and the engine's version are rows of the About tab, not a second line under the name.

The body is tabbed rather than one long scroll, because what it carries has three audiences that do not overlap:
**About · Environment · Shortcuts**, in that order, and ⓘ and `F1` always open on About. The tab switch is
`Ds.Segment` at the design system's `md` size (`Ds.Segment.Md`, 26 px outer) — the same component the action
bar uses for Debug/Release at `sm`, so no new interaction pattern enters the design system. It sits in its own
band, padded 6 px top, 18 px sides and 14 px bottom, over a full-width `border-subtle` hairline, so the segment
reads as the head of the body rather than hanging off the identity block. The body's 284 px is **fixed**:
switching tabs must not move the footer, and a pane that outgrows it scrolls inside itself; its content is
inset 14 px top, 18 px sides and 20 px bottom.

- **About** opens with a short paragraph on what the product does (13 px `text-secondary`, a 1.62 line height
  shared with What's new through the `LineHeight.Reading13` token, wrapping at 470 px), a hairline, and three
  label/value rows — `Version`, `Engine` and `Copyright`; a row is at least 27 px tall, with a 124 px label, an
  18 px gap and a mono value. `Version` and `Copyright` come off the assembly, `Engine` is the version the engine
  itself reported and reads `not started` until it has. Under the rows a ghost *What's new in {version}* button
  closes About and asks the window to open What's new through the same path as the title-bar button, so the
  unread mark clears exactly as it does there.
- **Environment** is two caps groups: **RUNTIME** — engine PID, .NET runtime, OS — and **PATHS** — the resolved
  `MSBuild.exe`, the repository root, the state file and the logs. The application and engine
  versions are not repeated here.
- **Shortcuts** is two caps groups, **BUILD** and **APPLICATION**; each row is the catalog's description and its
  key caps, and the global restore hotkey is marked `unavailable` when its registration failed.

Everything the dialog shows is bound from somewhere else — identity from the assembly, the shortcut rows and
their groups from the same catalog the window binds its keys from, and the About rows, both Environment groups
and the clipboard text from **one** diagnostics model (`DiagnosticsReport`), so no value is written twice. It
composes no text of its own. `MSBuild.exe` resolution is the one asynchronous value: `vswhere` is a child
process, so it runs when the Environment tab is first selected, not when the dialog opens, and the row reads
`resolving…` until it lands. *Copy diagnostics* sits in the footer's left corner, pulled 10 px left so its label
lines up with the 18 px gutter; it writes a title line with the product and version followed by the engine,
runtime and path rows in one aligned column, and confirms with the check icon and the success tone for the same
1.4 s the console's copy button uses. *Close* sits on the right.

There is deliberately no third-party tab: the dialog is not an attribution inventory. The one licence text the
product redistributes — the Geist fonts' — ships next to the executable as `Assets/GEIST-LICENSE.txt`.

A value that overflows its column — the resolved `MSBuild.exe` path is the usual case — is not truncated. An
ellipsis with the full path in a tooltip was tried and dropped: the row instead sits in its own horizontally
scrollable box with no visible bar, and a wheel notch over an overflowing row pans it sideways instead of
scrolling the tab. Over a row that does *not* overflow the box hands the notch on to its parent, and that
forwarding is required rather than tidy: a `ScrollViewer` swallows the wheel in its bubbling class handler
whether or not it has anything to scroll, so a row that merely sits there would otherwise kill the wheel over
most of the tab's surface. Nothing is lost by not seeing the whole path at a glance — *Copy diagnostics*
already puts the full text one click away.

**What's new is the third modal on the shared shell**, and the one with the fixed size: 720 × 600 px, without
About's identity block or tab switch — the dialog has exactly one job. The head row (padded 20 px top, 18 px
sides, 16 px bottom, over a `border-subtle` hairline) carries the title with a one-line description 3 px below
it on the left, and on the right a single mono version chip — 20 px tall, `surface` fill, a `border-strong`
hairline, the installed version in 12 px `text-secondary`. Because the dialog's height is fixed, the body simply
takes what the head and footer leave and scrolls inside itself, so an *Earlier versions* button that unfolds
the whole history never pushes the dialog past the window; the scrolling list is inset 22 px top, 18 px sides and
24 px bottom.

Each version is a **two-column block**: an 84 px identity column, a 26 px gap, and the notes. The identity column
stacks the mono version number, the date 6 px below it in 11 px `text-faint`, and — on the running version — a
16 px neutral `INSTALLED` chip (a bordered pill on `surface` with 9.5 px caps, not the quiet unbordered
`CURRENT` label an earlier pass tried), so every date sits in the same column instead of trailing off at the
far edge. That column is **sticky**: as the body scrolls, it follows the top of its block and stops at the
block's bottom. WPF has no sticky positioning, so the decision is one pure function —
`clamp(scrollTop − blockTop, 0, blockHeight − columnHeight)` (`StickyColumn`) — written into the column's
translate transform on every scroll change. The notes column holds category **blocks** 15 px apart (a 6 px
coloured swatch and a caps heading, the items 7 px below it and indented 13 px), with items 10 px apart at a
1.62 line height and wrapping at 500 px. The categories are fixed in order — Added, Changed, Fixed,
Performance, Removed — and an empty one is not drawn. Versions are separated by 22 px, a `border-subtle`
hairline and another 22 px. The three newest versions are open; the rest fold under an *Earlier versions (N)*
button with a down chevron, placed in the notes column of the same two-column grid below its own hairline and
aligned flush with the note text (its own left padding cancelled by a negative margin); the fold returns on
the next open. The footer carries only *Close* — *Copy diagnostics* stays on About, where the rest of the
diagnostics live.

This is also where the user is *sent*. When the version last read differs from the running one, a 5 px amber
dot sits on the title bar's sparkle button, its tooltip becomes `What's new in {version}`, and it stays there
even on a fresh install with no recorded version at all. Opening the dialog clears the dot and records the
version, so it does not come back until the next one ships. ⓘ's tooltip no longer varies with this state — the
routing an earlier pass sent through About is gone along with the tab it pointed at.

**All three modals can be open at once, and What's new is always the uppermost, with About above Settings.**
Each is declared after the last, so z-order follows the markup. Both `F1` and `Ctrl+F1` toggle their own
dialog and do so even while another is open: Esc closes the topmost layer first, which means the drafts
underneath survive. An earlier rule deafened `F1` whenever any dialog was open — the key is a window-level
`InputBinding` and fires regardless of the Settings focus trap, so the fear was that it would discard an
unsaved draft. Layering answers that better than silence did. The gear still no-ops while anything is open,
which costs nothing: under the scrim it cannot be clicked anyway.

### 13.4 Scroll infrastructure

WPF provides neither smooth scrolling nor horizontal wheel input, so the scrolling surface is assembled here:

| Piece | Role |
|---|---|
| `ScrollAnimator` | attached DP animating `VerticalOffset`; a wheel event cancels the animation |
| `BottomAnchorBehavior` | bottom-stick with a 48 px release threshold and a jumping window; drives the `⌄ latest` pill |
| `UserScrollSignal` | the raw "the user scrolled" input: wheel, scrollbar, navigation keys — wired at the root of all three scrolling panels |
| `FollowScrollController` | frontier following (550 ms cadence, 54 px dead-band) |
| `ScrollArbiter` | the referee |
| `HorizontalWheelScroll` | horizontal wheel / touchpad input, which WPF never delivers |

**An animated scroll always starts from where the panel really is.** The animation carries no `From` and holds
its end value, so a finished move keeps owning the property; if the panel is then moved by some other route — a
document swap, a plain `ScrollToVerticalOffset` — the seeding of the base value disappears underneath that hold,
and a fresh move to the *same* target changes no effective value at all, raises no callback, and scrolls
nothing. The previous animation is therefore released before the base value is seeded. It costs nothing for
retargeting mid-flight: an animation in flight is already driving the panel every frame, so the caller's reading
of the current offset *is* the visible position.

**Cancelling a move releases it; it never rewinds it.** The same hold that keeps a finished move owning the
property also means that dropping the animation hands the property back to a base value seeded at the point the
move *started*. Released without care, a wheel touch after a completed jump would therefore scroll the panel
back to wherever it was before the jump — on the console, riding the wheel up after `⌄ latest` resumed from the
pre-jump position instead of from the bottom. So the cancel writes the panel's current position into the base
value first: base and effective agree, releasing the animation changes nothing, and the wheel carries on from
where the panel actually is.

**Only the user stops the follow, and the signal comes from input, not from geometry.** A scroll event says
nothing about who caused it: relayout, a viewport change and our own programmatic scrolls all raise one, and
the offset they report can still be the pre-scroll value, so a distance measured then can cross the threshold
on its own. Reading "the user scrolled" out of that geometry made the stream drop its follow with nobody
touching it. `UserScrollSignal` supplies the real thing from three input channels — the scrollbar is its own
channel because dragging the thumb produces no wheel event at all, and WPF's `ScrollBar.Scroll` fires for user
interaction only, never for a programmatic offset change.

Whether to pin right now is then answered in exactly one place, `ShouldFollow`: following is on, no jump is in
flight, and the user does not have the wheel. Every panel reads that instead of interpreting the stuck flag
itself. The console had been doing the latter — its append path pinned on the raw flag and therefore ignored
the reader, which during a build made the panel impossible to scroll at all. That flag could also go stale
there for a second reason: AvalonEdit raises an *offset* event, so content added while the reader is scrolled
up moves nothing and raises nothing, and a hand-tracked extent delta computed at the next real event looked
like content growth and skipped the decision entirely. The console reports offset events as such and lets the
code that grows the document do the pinning.

The five-second idle return is armed by the input signal too, never by scroll events. Arming it on events
meant flowing content reset it continuously, so during a build the wait never once elapsed and a reader who
scrolled up stayed there forever. The pill, by contrast, follows live distance rather than state transitions:
it is a geometric affordance, and with the follow no longer changing on its own there would otherwise be
nothing to refresh it.

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
only panel that enables it: it is the only surface whose overflow (`WordWrap=False`) needs *that native
signal*. Two details are measured rather than assumed, and both are recorded on the class: the scroll must be
requested one dispatcher turn later (a request issued inside the window procedure is silently dropped), and the
target offset is accumulated across one gesture instead of being read back from the viewer each time (the
viewer publishes the new offset only after a layout pass, so reading it back loses steps). A step is the
horizontal twin of WPF's vertical one — `WheelScrollLines × 16 px` per notch — except that the delta's
*magnitude* is honoured, which is what makes a touchpad's stream of small deltas track the finger.

About's Environment values scroll sideways too (§13.3), but that is the simpler, ordinary case: an overflowing
row's value sits in its own `ScrollViewer`, and a `PreviewMouseWheel` handler redirects a plain vertical notch
— which WPF already dispatches as a routed event, no hook required — straight into `ScrollToHorizontalOffset`,
synchronously, no dispatcher turn to wait for. None of `HorizontalWheelScroll`'s machinery carries over: the
two solve different problems, one a message WPF never delivers, the other a message it delivers plenty.

`LayoutMetrics` is the shared arithmetic behind sticky headers, follow-mode and selection scrolling: one
cumulative offset table over mixed 36 px rows and 24 px headers, giving any row's absolute Y, the pinned header
set at a given offset, and a row's scroll target. Sticky headers are an **overlay** (an `ItemsControl` above the
`ScrollViewer` reading that table), not in-flow elements. A header click reads the same table through
`JumpTargetForHeader`: the group's first row, less one header-height per stacked header above it (including
itself), clamped to zero — the row lands exactly beneath the stack rather than under it. The formula never
calls `OffsetOfRow` on a row that might not exist: it derives where the first row *would* start
(`ContentTop + HeaderHeight`) so a layer emptied by the active filter still has a correct target for its
header.

### 13.5 Console host

The console is a read-only AvalonEdit. That choice is forced: text selection is non-negotiable, lines must be
individually coloured, and MSBuild-verbose volume must not stall the UI. `TextBlock` gives no selection;
`FlowDocument`/`RichTextBox` collapses under the volume; an `ItemsControl` of lines loses selection across
lines.

- **The header is one 28 px shell with two mutually exclusive contents**, never two controls. Its outer `Grid` has a
  `*` column and an `Auto` column: the right block (Copy log + `N lines`) sits in the `Auto` column and never shrinks.
  The left content's own inner `Grid` makes **every** column `Auto`, project name included — deliberately not `*`. A
  `*` column always claims the whole remainder regardless of what its content actually needs, and every `Auto` column
  after it starts at that column's *full* width rather than at the text's rendered edge; with the name in a `*`
  column, a wide panel and a short name left a visible gap before the status glyph instead of the two sitting flush
  (the prototype's name `span` is `white-space: nowrap` with no flex-grow — it shrinks, never grows, and its
  neighbours are always immediately to its right). The real shrink-on-demand behaviour is computed by hand instead:
  `ApplyProjectNameShrink` reads the *actual* rendered width of Back, the status glyph, the status name and whichever
  badges are visible (each plus its own margin), subtracts their sum from the available space, and caps the name's
  `MaxWidth` at what's left — recomputed after every `ShowProjectLog`/`RefreshStatus` call, on the right block's own
  `SizeChanged` (Copy log appearing or disappearing, or the `N lines` text widening from 999 to 1000), and on the
  header's own `SizeChanged` (a live splitter drag). Because WPF only refreshes `ActualWidth` after a layout pass, the
  method forces one (`UpdateLayout`) before reading its neighbours — the same idiom `ConsoleView`'s scroll-pin logic
  already uses for the same reason. In the narrative half only the caps `CONSOLE` label shows; in the project-log half
  `Back` is a ghost/sm button (`Ds.Button.Ghost.Sm`, 24 px) whose content — the drawn `Icon.Back` arrow plus the word
  "Back" — is built once in the constructor and bound to the button's own *animated* `Foreground`
  (`IconVisual.BoundToForeground`), so the icon tracks the same hover fade the label text does; its `-6px` left margin
  is not a clipping bug and not a full cancellation either — Ghost.Sm's own left padding is 10 px, so `-6` only takes
  back six of those ten, leaving the icon 4 px further in than the panel's own 10 px inset, not flush with it. The
  status glyph is a real `StatusGlyph` control (13 px) rather than a character — `building` draws its own spinning arc
  through the control's embedded `BuildingSpinner`, every other state draws the dashed/solid ring. The glyph reads the
  selected row's own `ProjectRowViewModel.Status`, the same value the row and the graph node draw, so the header never
  keeps a second state-to-glyph mapping: a cycle member that is `Started` but not the one actually compiling shows
  `Queued` in the row and in the header alike. The status word beside it, and its colour, read the very same table
  (`StatusGlyph.RunLabelFor`/`BrushKeyFor`) the glyph does rather than a second `ProjectRowState`-keyed
  vocabulary, so word, colour and glyph are one call and cannot disagree. A dependency-issue badge and a cycle
  badge can appear **together** (unlike the single triangle a project row shows, which picks one by priority):
  both are an 8 px `Icon.AlertTri` outline triangle in `Brush.AmberText`, declared directly in XAML as
  `{DynamicResource}` bindings so they resolve as soon as the header is rooted in a live resource scope even
  while the badge itself stays collapsed. The dependency-issue tooltip spells out every project by its short
  name (`RowWarning.DepIssueDetail`, comma-joined — the header has room a row's slot does not, so it never falls
  back to the row's "+N" abbreviation); the cycle tooltip is the same sentence the row's own triangle uses
  (`RowWarning.InCycle`), read from the one shared constant rather than retyped. Both tooltips are explicit
  `ToolTip` objects declared in XAML with `AppTooltip.Side="Bottom"`, so they open below the badge; code-behind
  only writes their content. Copy log is a plain `Ds.IconButton` (22×22, already the design's "sm" size in this
  app) with no bespoke chrome; its copied-state green tint is written straight to `Foreground` the same way
  `AboutDialog`'s Copy diagnostics button does, which means a hover during the 1.4 s window can hand control
  back to the style's own animated brush — an accepted trade-off shared by both buttons.
- **The header keeps watching the selected row, not just the moment it was selected.** `ShowProjectLog` runs once, on
  selection; a project already open can still change underneath the reader — a `Started` row reaching `Succeeded`, a
  dependency-issue list arriving, a cycle membership settling — and none of those are selection events.
  `MainWindow.TrackHeaderRow` subscribes to exactly the one selected `ProjectRowViewModel`'s `PropertyChanged`
  (swapping the subscription, never stacking two) and calls `ConsoleHeader.RefreshStatus` on
  `State`/`Status`/`DepIssues`/`InCycle` alone — `Status` is listed on its own because it can change while `State`
  does not (a cycle group handing its turn to this member flips `IsCompiling`) — every other row notification
  (`Marked`, `Fade`, …) is not the header's concern and is ignored, the same filtered `switch` idiom
  `ProjectRow.OnVmPropertyChanged` already uses for its own row. `RefreshStatus` touches only the glyph, the status
  word and the two badges; it does not re-run the project-name/copy-log/mode side of `ShowProjectLog`, so a status
  change mid-read cannot reset the reader's clipboard feedback or replay the panel's tilt transition. The 200 ms run
  tick still owns only `SetLineCount` — status changes are comparatively rare (a handful per project per run) and are
  pushed the instant they happen rather than polled, so the idle-tick stays allocation-free exactly as before.
- The document stays **plain text**, so what the user copies is meaningful. Colour comes from an offset-based
  `DocumentColorizingTransformer`, and only lines whose *format is known* get one: MSBuild's own diagnostic
  shape (`… : error CS0103: …`, `… : warning MSB3277: …`) and the prefixes the application itself prints
  (`[error]`, `warning:`, a command line). Everything else is one tone. Scanning free text for `failed` or
  `succeeded` was tried and dropped: it caught the word inside a project name just as readily, and a colour
  that is sometimes wrong is worth less than no colour at all. Warnings are amber and errors red; orange left
  the console with the rest of the interface (§14.3).
- Appends are batched: IPC → channel → ~50 ms flush → exactly one `BeginUpdate → Insert → EndUpdate`. The
  window opens when a line arrives, not on a clock, so a console with nothing to print never wakes the pump.
- The live document is capped at a render slice of 200 lines. That cap is a **window, not a limit**: scrolling
  to the top pages the previous slice back in, in either mode. The backlog behind the window is mode-independent
  and grows as the window slides — lines trimmed off the top while the panel is following are moved into it, so
  nothing that scrolled past is unreachable. Only the *source* differs: a project page is seeded from the log on
  disk (§5.5), the narrative from the view-model's full run buffer, which is what `Back` hands over anyway.
  Leaving the narrative without a backlog was measured as the console "losing" its history — a parallel build
  streams hundreds of lines a second, so the 200-line window turned over in seconds and everything older became
  unreachable even though the text was still buffered.
- **A line is only text.** There is no wall-clock column and no `▸` marker: every line starts at the same left
  edge as the caret and the line's kind is carried by colour alone. A real run streams hundreds of lines a
  second and a stamp on each of them carried no information; time lives in one place, the event stream and the
  ribbon's elapsed counter.
- **Nothing is typed.** Live lines print immediately. The only live thing in the console is the prompt line at
  the bottom: a 7 × 13 px rectangle blinking at 1.1 s (not a font glyph), stepping through the console's own
  line palette as it blinks (§14.3), with `ready` beside it while idle. The line is unconditional — output empties its text, not the line —
  so the caret stays put and new lines pile up above it. The editor reserves one full line of bottom padding,
  measured from the text view's own line height, so the caret sits below the last line instead of on top of
  it; it hides while the reader is scrolled away from the bottom, alongside the `⌄ latest` pill, since it is
  pinned to the panel rather than to the document. That prompt caret is the console's **only** live caret. The
  editor is read-only but still takes keyboard focus when clicked, and its own thin text caret would then blink
  beside the prompt's; it is painted with a transparent brush instead. Only its visibility goes — the editor stays
  focusable, and text selection and Ctrl+C go through it as usual.
- **While you are scrolling, the panel is yours.** A user gesture takes the wheel for five seconds — the same
  idle window the list's frontier following uses, and the same constant — and during it arriving content
  never pulls the view down. The 48 px threshold alone was not enough: a small scroll stayed inside it, so
  the next line to arrive threw the reader back to the bottom, which during a build happens continuously.
  When the five seconds pass with no gesture, the panel returns to the bottom and resumes following. The
  console does not do this in project-log mode: there is no live stream to follow there and the reader is
  looking at a log. This is a deliberate departure from §2.5, which says a reader's position is never touched
  once they scroll away; without the return the panel simply stopped following and never came back.
- **While the panel is yours, nothing is deleted from the top of it either.** The render slice trims the
  document from the beginning, and a trim moves the text out from under a reader whose scroll offset is an
  absolute pixel — the position holds still while the content races upward past it. Trimming therefore waits
  for following to resume, and then catches up in one step while the panel is already pinned to the bottom,
  where it is invisible. The event stream has the same hazard with its 150-row buffer and solves it the other
  way: the rows do leave, and the offset is reduced by exactly the height that left, so the reader's content
  does not move. Both are the mirror of the chunk loader's prepend compensation.
- **Panel transitions are one piece, and the hinge is real.** Opening a project log and coming back with
  `Back` both settle the content **up from 14 px below**, hinged at its bottom edge, over 340 ms — a hinge,
  not a per-line cascade — so a three-line log and a two-hundred-line narrative open at the same rhythm. The
  prototype's `perspective(900px) rotateX(7deg)` is a genuine perspective projection: the receding top edge
  narrows, the advancing bottom edge widens. WPF's 2-D transforms are affine and cannot produce that
  trapezoid, so §2.4's second option is taken — the block is a textured plane in a `Viewport3D` with a
  `PerspectiveCamera` 900 px away, rotated 7° → 0 about an axis through its bottom edge. A scale-and-translate
  approximation was tried first and read as a slide rather than a hinge; the missing cue is the horizontal
  one. **The return is the mirror of the opening**: opening rises from 14 px below hinged at its bottom edge,
  going back settles from 14 px above hinged at its top edge — same duration, same curve, same angle, only the
  direction and the hinged edge change, so the two read as one gesture and its reverse rather than as the same
  thing twice. The plane's texture is a still of the block taken as the transition starts — a live visual brush
  cannot work, since hiding the real block would hide it in the brush too. The camera's field of view is
  derived so that the plane at zero rotation covers the viewport exactly, which is what makes the hand-off
  back to the real editor invisible. Only the log block moves: the prompt line stays out of it by design
  (§1.3, §4) — what settles is the content, and the caret is the panel's fixed point. The `⌄ latest` pill stays
  out too, being an affordance rather than content.
- **Every project has a page, and a page with no log still says something.** Clicking a card always switches
  the console to that project, including when the engine answers that there is no log — which is not the
  exceptional case but the common one, since a skipped project never writes a log file at all and its reason
  goes only to `decision.log`. Leaving the console on the run narrative made the click look like it had done
  nothing. What the body then shows is composed from the row: a first line saying **why** the project is in
  that state, and a second saying **what we have** when the output is not this tool's own — that it has never
  been built. A project last built by this tool gets no second line: the decision is made from content, so its
  commit is not evidence and is not shown. A project built outside this tool (§7.6) reads `Up to date — built outside this tool.`
  over `Built outside this tool`, since the evidence is someone else's output rather than a build of this
  tool's; the output reasons read `its build output is missing`, `its files are newer than its build output`
  (or `a dependency's output is newer than its build output`) and `its copy in the shared folder does not match
  its build output`, and a missing output does not repeat itself on the evidence line, just as never built does
  not. The status word is not repeated, because the header is already showing it. A project
  that is compiling right now gets one line instead of two: there is no evidence yet, and its output is about
  to arrive. The reason comes from the engine's own vocabulary where there is one — the skip reasons are a
  single shared source, so the page, the event stream and `decision.log` cannot drift apart — and from the
  will-build verdict and its reason (§7.4) where the project has not been spoken about in this run yet. Cycle
  membership is checked before that verdict, since Sync gives every cycle member `false` and reading that as
  "up to date" would be a lie.
- **There is no `build in progress` marker at the end of a project log.** There used to be an amber, blinking
  one. It was set when the page opened and never updated, so a project that finished while its log was on
  screen kept claiming to be building. Two surfaces already answer that question and stay in sync — the
  header's status glyph and the row itself — and a third, unsynchronised channel is worth less than the times
  it is wrong. It was removed rather than wired up.
- **Each mode is pinned to the end you read it from.** The order is fixed — change the content, pin, then
  animate — and the pin forces a measure on **both** sides of the scroll. Before, because the editor's scroll
  geometry is stale immediately after a document swap and a pin computed against it lands on the wrong end.
  After, because scrolling is a *request*: the viewer forwards it to the text view only on the next measure
  pass, so without a second one the offset is still the old value when the method returns and the request
  instead lands in the middle of the transition. That was visible in two ways at once, since the transition's
  texture is taken from the frame right after the pin: the beginning of the narrative showed for the whole
  340 ms and the panel then jumped to the end in a single frame, and the caret — which is skipped whenever the
  document's last line is not on screen — stayed at its previous position, sitting over the text as if the
  panel were already at the end.

  One extra measure is not quite enough either, because a measure can *grow* the extent: the text view builds
  its visual lines only at the offset it is scrolled to, and their real heights can differ from the first
  estimate. The pin therefore repeats until the extent stops moving, and the transition re-settles the bottom
  once more when it lands — the later layout passes that position the caret and finish the visual lines arrive
  during those 340 ms. Left alone they parked the panel one line short of the end: a small gap underneath and,
  because that is more than the 48 px threshold, an occasional `⌄ latest`.

  **Reclaiming the follow happens after the pin, not before**, for the same reason and it is the whole of the other
  half of that pill. The pill's visibility reads distance-from-bottom alone, so announcing "we are stuck to the bottom
  again" while the editor still holds the *previous* document — at its top — measured a huge distance and showed the
  pill for exactly as long as the pin took to run. It appeared and vanished on every `Back`. Ordered after the pin,
  the geometry is already right and the distance is zero. The run narrative pins to the **bottom**: the interesting
  thing is the latest line and the panel goes on following the stream. A project log pins to the **top** and opens
  **not following**: what you are looking for in a build log is the first error, and following would have thrown you
  to the bottom on the next live line. Scrolling down yourself hands following back, by the same rule as any other
  user scroll. This is a deliberate departure from §5.1, which pins both directions to the bottom.
- **A new section empties the narrative in place.** Not every operation opens one: a run, the Sync button,
  the click of Clean or Optimize and a branch change do (§10.2); a pull, the Sync a maintenance job chains and
  every automatic Sync append to what is already there, and a refused or failed checkout only adds its line.
  The disk logs are never
  touched — clearing is for the screen. The view-model clears its buffer and says so
  (`ConsoleCleared`); the shell resets the document at once, without a tilt — the tilt belongs to the mode
  switch, this is the same panel starting over — and leaves a project log that is on screen alone, since
  `Back` seeds the fresh narrative anyway. Batches of the previous operation still in the pump are dropped
  by the same reseed generation a mode switch uses, so nothing from before the clear can land after it.
- The console body is drawn at **Geist Mono 300**; dense output scans more easily at the lighter weight. Every
  other mono surface stays at 400.
- The console formats text in **Ideal** mode, overriding the window's `Display` (§14.2). Display rounds every
  glyph advance to a whole pixel; Geist Mono advances 7.2 px at 12 px, so it rounds to 7 and the line comes out
  2.8 % narrow with the rounding error spread unevenly between characters. On a monospace grid the cost is not
  only width but alignment. The bottom padding is wider than the top so the caret, which sits on the document's
  last line, is not flush against the horizontal scrollbar when one appears.
- **The body's cursor is a plain arrow, and the row under it gets a full-width band.** A hand is reserved for
  things that can be clicked; this panel has none, and AvalonEdit's own text I-beam was tried and dropped for
  the same reason the event stream drops a third cursor language. The arrow is not a simple property assignment:
  AvalonEdit's `SelectionMouseHandler` forces the I-beam (and, mid drag, an arrow over the current selection)
  from inside the `QueryCursor` routed event, not from the static `Cursor` property — neither `TextArea` nor
  `TextView` ever sets one. `ConsoleView` re-catches the same event one level up, on `TextEditor` itself, with
  `handledEventsToo: true`: the bubble reaches AvalonEdit's handler first and reaches this one after. Only an
  I-beam (or a position AvalonEdit never claimed at all) is overwritten with the arrow — AvalonEdit's own
  `EnableHyperlinks` is on by default and resolves a real `Hand` over a link under Ctrl, and that decision is
  left exactly as AvalonEdit made it, so a link in a build log still reads as clickable.

  The row band is a `Rectangle` sitting behind the editor in the same cell — `TextEditor.Background` stays
  transparent, so a rectangle drawn first shows through everywhere a glyph is not — filled with a local,
  unfrozen brush that `MotionTokens.TransitionColor` steps between `Brush.Surface` and transparent, the same
  primitive every other hover surface in the app uses. The colour only transitions when the band appears or
  disappears; while it is already showing, the band moves instantly from line to line and only its geometry
  changes. That state is tracked by the view itself rather than left to the primitive's own "already at the
  target" guard, because that guard only short-circuits a brush that has never been animated: WPF keeps
  `HasAnimatedProperties` set after an animation completes, so every call on a once-animated brush would build
  and start a fresh animation. Because the rectangle is stretched to the width of the tilt host rather than the
  editor's own content area, it reaches past the editor's padding to the panel's true edges, the full-bleed row
  the design asks for — which is also why the mouse wiring lives on the editor control itself rather than on
  the text view nested inside it: the text view sits *inside* that padding, and listening there alone would
  have made the band vanish in exactly the strip it is supposed to cover. Which line is under the pointer is
  answered by a pure helper (`ConsoleHoverBand.LineAt`, tested without any live editor) fed from the real
  `TextView.VisualLines` on every `MouseMove`; a line only partially inside the viewport still gets a full
  band, but that band is clipped to the text view's own rendered bounds so it cannot spill into the padding
  above it or the horizontal scrollbar below. A resting pointer does not go stale, either — a scroll, a live
  append, or a mode switch all replay the pointer's last known screen position through the same lookup, so the
  band keeps following the line actually underneath it without needing its own clock; leaving the editor
  (padding included) clears it. Nothing here opens a clock in the idle sense — the band only recomputes in
  response to a real mouse or scroll event, per §14.5's idle rule. A pointer moving inside the banded line
  returns before any lookup, and a refresh while the band is showing updates its position without starting a
  colour animation.

### 13.6 Graph renderer

The panel is a **quiet graph**: unnamed mini nodes in layer bands, no permanent edge network, and a camera
that stays on the fitted view unless you select something or move it yourself, and returns to it whenever an
operation starts. The point is that a 100-project workspace should read at a
glance instead of demanding to be studied.

**Layout is a function of the panel.** Nodes sit in horizontal bands ordered by build sequence — layer 0 on
top, and inside a band the first project to build sits leftmost — so reading top-down and left-to-right is
reading the build order. The node pitch is searched, not fixed: `QuietGraphLayout` walks from 44 px down to
5 px in half-pixel steps and takes the first value where every band, plus a `0.7 × pitch` gap between bands,
fits the panel height. A band whose last row is short is centred against the rows above it, and the whole
block is centred in the content box — a symmetric 36 px inset on every side, which is what makes the graph
read as a picture with a margin rather than as a panel that has been filled to the edges. That inset is a
single source: the overlay layer clamps to it as well, so a label never ends up hugging a corner. The consequence is
that the graph always fits — there is no scrollbar, and no canvas larger than the panel.

**A node is identified by its project id, never by its name.** Positions, the slot map, edge endpoints,
selection, hover, the filter set and the marking set all key on the full `.csproj` path; the display name is
only a label, used for the tooltip, the selection caption and the screen-reader name. The distinction is not
academic: two projects can produce the same `AssemblyName` — an external card (§10.4) pointing at a second
copy of a solution that already sits under the repository root is the ordinary way it happens. Keying on the
name would have the band reserve a cell for each of them and then write both positions into one entry: the
pair lands on a single point, one of them never receives a status or a click again, and the cell that was
reserved stays empty — a hole in the band, with projects that look missing. Names carry no decision elsewhere
either: the same collision makes the DLL ambiguous in the producer map, which drops the edge rather than guess
(§6.4).

A node is a square of `pitch × 0.6`, clamped to 8–24 px, with a 4 px radius, a 1.5 px border and a Lucide `box` glyph at 52 % of
its edge; nodes in the **start mode** get a dashed frame, drawn as a `Rectangle` because a WPF `Border` cannot
be dashed. A project that is `stale` (to build) is plain grey — the dash belongs to the start mode alone, so
"nothing is known yet" and "known, and waiting to be built" stay distinguishable.
Under the status square sits an opaque base in the panel's own colour: the status fill is only 12 % alpha, so
without it a selection edge passing behind a node would show straight through it.

**One colour channel.** The node's border and its fill are painted from the same visual status as the list
row (§14.3) — the state of the project's output, with the running operation laid over it — and there is no
separate "plan" core. The cube inside follows the frame, with a single exception: in a **cycle member the cube
is always amber**, whatever the frame says — unknown, to build, building, a result. Membership is structural,
not the outcome of a run: a Sync does not end it, and neither does Resolve cycles compiling the member, so the
cube does not either; it is the graphical proxy of the list row's warning triangle. It reaches the node as its
own field (`GraphNode.InCycle`) rather than as a status, because it never changes what the frame reports.
Earlier versions carried membership here as its own colour, first as an orange square and then as a persistent
corner badge; both were part of a three-channel model — result, plan, structure — that put three meanings on
the same 8–24 px surface and made amber and orange compete. The cube borrows the warning's amber rather than
opening a channel of its own, so the frame still tells one story: the state of the output.

The node's cell is deliberately **larger than the node** — by whichever overhangs further, the selection ring
or the bead orbit. WPF clips a child to its arrange slot, and everything that reaches outside the square lives
in that cell: with a cell exactly the size of the node the ring's straight edges were clipped away entirely
and only the corner arcs, which curve back inside the clip rectangle, survived. The clickable body stays at
node size, because growing it would put the hit area over the neighbours at a tight pitch. The ring itself is
the CSS `outline: 2px solid; outline-offset: 2` translated honestly: WPF draws a stroke *inside* the
rectangle, so the rectangle is a full pen wider than the offset alone would suggest. Because the layout depends on the panel, `SizeChanged` recomputes it and
updates the visuals **in place** — a splitter drag delivers dozens of size events per second, and rebuilding
hundreds of nodes on each one would freeze the panel it is resizing.

**An event is written at the prompt line, then released into the buffer.** A new event does not appear in the
buffer and open there; it is written beside the caret on the bottom line, left to right, and when the writing
ends the row is released upward with its own colour and its own glyph. The buffer itself never animates —
whatever is above the prompt line is settled.

**The text does not type; it locks in.** A read head crosses the line from left to right: everything behind it
is the real text, a five-character window ahead of it jitters through a small set of mono glyphs, and the tail
beyond that window is printed **transparent**. Printing the tail rather than leaving it out is the point — the
line has its final width from the first frame, so nothing ever reflows, which is what made the old letter-by-
letter reveal restless on a monospace grid. The head crosses any line in the same fifteen ticks, so a long line
and a short one both settle in about 360 ms and the stream keeps an even rhythm; a fixed step would have made
the duration a function of length. Only letters jitter: a token containing a digit — `1.4s`, `a3f81c2`,
`14/38`, `09:41:02` — is exempt as a whole, and punctuation never jitters, because those figures are tabular
and a random glyph would both bounce them on the grid and make a sha read like corrupted data. Failures,
bursts, the closing `done` line and reduced motion all print at once, as before.

The row is released the instant the lock-in ends. It used to be held for a further 420 ms, the caret-hold
window the old typewriter carried, and while the writing happened inside the buffer that window was
indistinguishable from anything else. Once the writing moved to the prompt line it became plain: a finished
line sat at the bottom for half a second and only then jumped up. The gesture is one piece now — write,
release, next.

The row exists from the first moment but stays hidden until it is released. Showing it while the same event is
being written below would put the event on screen twice, and it would also grow the buffer — pushing
everything above it up — before the writing had even started. Hiding it defers that step to the release, so
the movement happens in one place and ends there. The timing is still the row's own: the same cadence, the
same single-writer rule, the same "a burst or a failure prints at once" decision; the panel only mirrors the
row's text onto the prompt line, and no second clock exists.

This model was tried, abandoned and brought back, and the reason it failed the first time is worth keeping:
at the moment of release the 12 px caret column turned into a status glyph, and although the text never
changed the eye read it as a colour change. What removes that seam is the caret itself — it now wears the
event's own icon colour while the event is being written, so the glyph that replaces it is already the colour
the reader was looking at.

The row tones follow the prototype with one deliberate exception: a **skipped** line is `text-dim` rather
than `text-faint`, so that it stays the quietest thing in the stream while remaining readable.

Only the newest row types at a time, and the panel is its only starter. A row's own `Loaded` used to start it
too, on the assumption that the play-once flag made the second attempt harmless; it was not, because that
flag's early exit also assigns the full text — and WPF raises `Loaded` deferred, so it landed in the middle of
the typing and made the line flash complete for a frame before opening again. Being printed instantly now
counts as having played, as well: a row that passed while motion was off used to keep the flag clear, so
turning the signal back on and rebuilding the panel had several old rows opening leftward at once, which is
precisely the retroactive animation the motion contract forbids. A new row completes the previous one
instantly. That rule lives in the panel rather than in the row, since a row does not know its siblings. Without it — one timer per row — a fast
run had two or three lines opening leftward at once, which is the defect that started this whole detour. Burst
and failure events skip the typewriter entirely, as does reduced motion, and each row types exactly once, so a
recycled container does not replay it. A row counts as "typing" for 420 ms after its text completes, matching
§6, which is also how long it keeps the single-writer slot.

**The prompt line is an indicator, not a surface.** It has two states and its text is amber in both: the
project being compiled (`X building…`) or nothing at all, a wall-clock stamp and a blinking caret. Its *text*
never types and never takes an event's colour, so the line itself is the one thing on the panel that always
reads the same.

The prompt is there from the first frame, before any event, and the stream has no empty-state text — the
console shows a blinking caret the moment it opens and the two panels should say the same thing. Its presence
is unconditional. Gating it on the active project changing was a real defect: a Sync starts no project, so the
generation never moved and the caret never appeared until a second Sync happened to reset the gate as a side
effect.

**The caret changes colour on every blink.** Both panels share one caret component, so both share the rhythm:
the 1.1 s blink is untouched, and a second clock steps the colour through the console's own line palette in
order — command white, info grey, success green, warning amber, error red, dim grey. The step is *discrete* and
phased half a blink back, so the colour swaps at the bottom of the blink, while the caret is dark: there is no
visible transition, the caret simply comes back a different colour. The palette is the source of the sequence
(`ConsolePalette.Keys`), which is what guarantees the caret never wears a colour a console line could not carry.
The wall-clock stamp stays dim, which keeps the waiting row quiet.

**With reduced motion the caret stands still in the line's colour**, and that is where the stream's tone
channel is still visible: while an event is in hand the caret carries that event's *icon* colour — green for a
success, red for a failure, grey for a skip — and it returns to amber, the resting tone, when the writing is
over. An event that prints instantly is never written, so it holds the caret for a short window instead —
420 ms, the same figure the prompt's own caret hold uses. When the colour cycle is running it takes precedence:
the tone would otherwise cut the cycle short on the first event and freeze the caret on one colour.

Both extremes were tried and measured. Colouring only for the exact duration of the typing left the caret
amber most of the time and green was almost never seen; holding the colour indefinitely left it stale — a run
finished, nothing happening, and a red caret still sitting there. The prompt *text* is not part of this: while
an event is being written the text is the event, in amber, and it takes its own colour only once it is
released into the buffer. The caret used to be bound to that text, which is exactly why it could never say
anything of its own; the two are separate channels now.

**The node's core is not a channel of its own.** The glyph inside the square is painted from the same visual
status as the border, from one table (§14.3) — a second, separately-coloured answer to "what will happen to
this project" (amber, grey, a permanent orange for cycle members) would only say twice what the border already
says once. `queued` is amber rather than grey for the same single-channel reason: pressing Build must not drain
the only colour on screen in the same frame the graph dims.

**Entering a run dims before it repaints.** Outside the marking wave, colour and border changes are instant
here (measured deviation, below). A status push that landed in the same frame the graph began to fade would be
seen at full brightness with the fade arriving after it, which reads as "the ones about to build changed,
then everything went out". Status pushes are therefore held for the length of the fade and applied once it
finishes; only the last one is kept, since the intermediate states were never visible anyway. Planning takes
seconds, so nothing real is delayed by it.

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
a filter, a filter beats the run, and hover beats all three — though a filter and the run never actually meet,
because the graph sets the filter aside for the length of a run (below).

A filter (or a search) in the list dims the graph the same way a selection does — the names that survive the
list's own `ProjectFilter.Matches` stay opaque and everything else drops to 0.1, with the matching set handed
to the graph rather than recomputed there, so the two surfaces can never disagree about what matches. That
fade runs at 420 ms rather than the 280 ms a run tick uses, in both directions. The difference is deliberate:
a run transition reports a state change and happens several times a second, while a filter is the user's own
one-off gesture that dims half the graph at once and wants to be followed by eye.

**A run ignores the filter in the graph, and only there.** Starting a run keeps the filter (§13.7), and the list
stays filtered throughout. The graph, though, tells the run across the whole workspace: from the click — before
the opening wave — through the run and its ending finale, opacity is decided as if no filter were set
(`GraphView.IsFilterSuspended`; the pure `Resolve` is simply not handed one), so the wave, the run's dimming and
the neon play in their standard form. Once the finale has played the graph holds its final look for one more
short beat (`EndFinale.FilterReturnAtMs`, the finale's length plus the design's short `LightMs`) and then
fades back to the filtered look at the filter's own 420 ms. A stop and the engine dying end the run in the
`Stopped` phase, which plays the finale too when something was built, so they follow the same rule. When there
is no finale — nothing was built, reduced motion, a stop during the opening sequence, a command that never went
out — the filter returns as soon as the run is over. A restart of the plan surface (a Sync click or a branch
change, §10.2) cuts a finale still playing and brings the filter back at once (`GraphView.CancelEndFinale`), so
the new graph's reveal plays with the filtered look. The two end signals — the phase that starts the finale
and the run lock falling — arrive in different orders on different paths, and either order lands on the same
result. A filter changed during the run is kept and is what the graph returns to. Without a filter none of this
has a visible effect.

*One rule of the design is deliberately not implemented:* colour changes are instant rather than a 380 ms
transition. A brush property cannot be interpolated in WPF, so the transition needs a local
`SolidColorBrush` per surface per node. That was built and measured: with 177 projects changing status in a
single tick it costs three brushes and three colour animations each, taking the tick from 11 ms to 51 ms and
breaking the UI event budget. Spending most of that budget on a colour glide across an 8–24 px square whose
opacity is already animating is not defensible, and the budget is not negotiable.

**Building is a bead orbit.** A project under construction carries dense amber dots circling a rounded-square
track around its node. The distance to that track is not a fixed number: it is solved backward from the
cell's own pitch, `(pitch − size − stroke thickness − 2) / 2`, clamped to 0.8–2.8 px — a dense graph pulls the
track in toward the node, and a roomy one lets it drift out to the clamp's 2.8 px ceiling. The target on the
far side of that formula is a 2 px gap between one node's dots and its neighbour's;
so long as the clamp does not hit its floor, two orbits that would otherwise touch stay apart without the
pitch search itself ever knowing beads exist. The dots are a stroke dash pattern whose step divides the
perimeter a whole number of times, so the pattern does not overlap itself where it closes; the orbit turns once
every 4000 ms. Spacing and pace are a deliberate departure from the design's numbers, chosen by the user after
watching the orbit run: the dots aim for a 4.4 px step, so the visible gap between two of them is well wider than
a dot, and the pace is bounded by the frame rate the orbit is drawn at (the 30 fps decorative rate) — on the
largest node a dot travels at most a fifth of a step per frame, since near half a step the eye can no longer
tell which way the dots move and they seem to run into each other. The pen is 1.6 px, and because WPF measures
a dash pattern and a dash offset in multiples of stroke thickness rather than in pixels, both are divided by it
to land on the intended absolute geometry. The same pen also draws *inside* the rectangle it is given — the same rule the selection ring
follows (above) — so the rectangle handed to WPF is a full pen wider than the track it is meant to trace, or
the drawn path would fall a whole stroke short of the perimeter the dash pattern was computed for. Every orbit
in the graph hangs off **one** shared animation clock — the node size is graph-wide, so the perimeter is too,
and N parallel builds would otherwise mean N infinite animations. The orbit fades in over 420 ms and out over
640 ms, and the clock is released 700 ms after the last node stops building, so the dots fade *while still
turning* rather than freezing in place. Resizing the panel changes the perimeter, so the pattern and the clock
are rebuilt.

**A skipped project is silent.** No orbit, no bright hold, no wave — it settles into its result colour and
stays exactly as dim as the queue around it. An earlier version gave skipping the full announcement (a brief
orbit, a short hold, and a 45 ms-per-node wave in build order) on the argument that the incremental check did
run and found the project current. Looking at it settled the question the other way: a grey node with an amber
orbit around it says *working* and *skipped* at once, and the most common run in this tool is the one where
nothing changed, so the whole graph stirred for seconds on every press. A quiet graph reports what *changed*,
and being skipped is precisely nothing changing; the fact is already in the row's status, the ribbon counter
and the console. Repainting the square amber for a moment would be worse still — it would state a
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

**The list and the graph share one hover.** Pointing at a project on either surface shows the other surface's
*standard* hover for the same project — there is no second, quieter look. A hovered node lights its list row
(the hover ground, the icon buttons in place of the decision label); a hovered row gives its node the full node
hover described above, tooltip included, whatever the camera is doing — default view, zoom or focus-and-fit.
Nothing moves to make that happen: the list does not scroll and the camera does not glide. If the counterpart
is not in view the echo simply does nothing — a row that is not realized has nothing to paint, and a node whose
projected centre lies outside the panel (`GraphOverlay.IsOnScreen`) is left alone, because the tooltip's anchor
clamp would otherwise draw it at the panel's edge pointing at nothing. The one shared value is
`RunViewModel.HoveredProjectId`. Only real pointer hover writes it: a row's enter writes its project and its
leave clears the value only if it still names that project, and the graph reports its pointer hover through
`GraphView.HoveredNodeChanged` (`GraphHoverEcho` wires the two). The echo reads the value and never reports
back (`GraphView.EchoHover`), so no loop can form. Hover on a row lives on the project's view model
(`ProjectRowViewModel.IsHovered`), not on the recycled container, and a change touches only the previous and
the new row — the pointer sweeping across the graph produces dozens of changes a second. The selection's
clearing of the node hover (above) counts as the pointer's hover changing, so it clears the shared value too:
clicking a row leaves the node in its selected look with its name label, without a tooltip on top.

**Selection focuses and fits.** Clicking a node — or a list row, or a stream line — fits the bounding box of
the selection plus its direct dependencies and dependents into the panel: scale is `min(W/bw, H/bh)` clamped
to 0.7–2.6 with a padding of `3 × node + 48 px`, and the camera glides there over 460 ms. Everything outside
that focus set drops to 0.1 — except a project the run is actively building, which stays fully lit even
outside it, because a dim body under a live amber orbit reads as *not building* rather than *in progress*. The
selected node also holds the hover treatment: it stays at that same 1.5×, keeps its
thicker border, is pulled to the front so nothing can cover its ring, and gains a 2 px amber focus ring. (The
main prototype does not enlarge a selected node — that came from the Graph Lab study and is a deliberate
departure from §2.3.) Pulling it forward is a fix rather than a flourish: the ring extends past the node, and
at a tight pitch a neighbour drawn later would cover all but its corners. **Only then are dependency lines
drawn** — from each dependency down to the node and from the node down to each dependent, as vertical cubic
beziers whose control points sit at the mid-height of their two ends, in amber dashes that flow along one
shared clock. Clearing the selection tears them down again. Because WPF measures dash arrays in multiples of
stroke thickness rather than pixels, the design's absolute 4/8 px pattern and 24 px travel are divided by the
1.2 px thickness so the drawn result matches the design.

**The end-of-run finale releases focus, not the selection.** When the graph's closing choreography begins
(§14.5) the camera abandons any focus-and-fit and glides to the default view instead, taking the dependency
lines, the focus ring and the name label down with it — the finale is meant to read across the whole graph,
not a corner of it zoomed in. The hover treatment the selected node was holding goes with them: it falls back
to its plain size, its plain border and its normal z-order. That is one release rather than three, because
those effects come from a single flag, and leaving it set would keep one square enlarged and thick-bordered —
still lit as *the* node — after everything else that named it had already been taken away. The selection
itself is untouched; the console still shows that project's log. None of it returns when the choreography
ends, either: the graph remembers the selection it released focus from and stays on the default view, with
that node drawn like any other, for as long as it remains selected. What reopens it is the user picking a
different node (or clearing the selection and picking the same one again) — nothing brings it back on its
own.

**Navigation, and why the pan is unclamped.** The wheel zooms at the cursor — the world point under the
pointer stays under it — by a multiplicative 1.14 per notch inside 0.7–5.0. Pressing empty ground and moving
more than 3 px is a pan; releasing under that threshold is a click, which drops the selection if there is one
and otherwise returns the view to its default. Losing capture instead (Alt+Tab, a popup) is a *cancel* and
leaves the selection alone. There is no pan clamp, and that is a conclusion rather than an omission: the world
canvas *is* the panel, so a clamp of the "an axis that fits is centred" kind would force the translation to
the graph's centre on every selection whose fit scale falls below 1, overriding focus-and-fit entirely. The
design supplies its own recovery instead: clicking empty ground with nothing selected returns the view to its
default. Rebuilding the graph — a new structure, or a Sync that starts the screen over (§10.2) — also puts the
camera back at its default, on screen as well as in its target (`SnapCameraTo`): the camera's target and its live
transform are set together on every path, so no zoom or pan from the previous graph survives into the new one,
and what the screen shows never disagrees with what the camera believes it shows. Starting an operation
(`BeginOperation` — a Build, Rebuild, Resolve cycles or a row run) glides the camera back to its default too,
so the opening wave and the run play on the whole graph rather than on whatever part was zoomed into; during
the run itself the camera stays still. (§2.3 puts a mono hint line in the bottom-right corner announcing the two gestures; it was removed —
the panel reads more quietly without it.)

**Opening.** Whenever the graph is built — a Sync with a new structure, a Sync that starts the screen over, the
Sync that follows a Clean — the nodes appear in build order, each one delayed by `index × 9 ms`, capped at
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
panel header switches to its project-log half with the `Back` button. Clicking the same element again, or
`Back`, or Esc, clears it and follow-mode resumes. Text selection inside the console never clears the project
selection.

Esc is a chain and only ever closes the topmost layer: dialog → popover/menu → selection. Right-clicking a
row is not a selection gesture — it opens the row menu and leaves the selection alone.

**Starting a run drops the selection and keeps the filter.** Build, Rebuild, Resolve cycles and a row's own
Build, Rebuild and Clean all go through the same start: the selection is cleared, so the graph glides back to
the fitted view and the console returns to the run log, while the chips and the search stay exactly as they
were and the list stays filtered for the whole run. The graph sets the filter aside while it tells the run and
takes it back after the finale (§13.6).

**Neither is a click inside the row's own action block** — the hover icons, the ⋯ menu and the Open-in-Visual
Studio chooser. The row has to say so explicitly, because a mouse event raised inside a `Popup` continues out
of the popup to its *logical* parent, which is the row, and `MouseLeftButtonUp` is a **direct** event that the
input system raises separately on every element along that route. Left implicit, picking *Build* from the row
menu selected the row a moment after the run had cleared the selection: the graph focused that node instead of
returning to the fitted view, and the console switched to the project's log rather than the run's narrative.
The gate reads the event's source rather than listing elements, so anything that grows in that block is
covered by it.

**Clicking away from a text box releases its focus.** WPF moves keyboard focus only to an element that can take
it; a panel background, a non-focusable button or a row takes the click and leaves the caret — and the amber
ring — in the filter box. `ClickAwayBlur`, enabled on the main window, listens to `MouseDown` at the end of the
route, handled events included: if the clicked element took focus itself, nothing happens; if focus is still in
a text box and the click landed outside it, focus goes to the nearest focusable ancestor of the click — which
keeps it inside a modal's focus trap — or is cleared when there is none. Clicks inside a popup are left alone:
their visual route never reaches the window, and the popup manages its own focus.

### 13.8 Design-system control library

WPF ships almost none of the design's vocabulary, so `Resources/Controls.xaml` defines it as templates and
styles, and `Controls/` holds the custom elements that a template cannot express.

| Element | Form |
|---|---|
| Buttons | One shared `ControlTemplate` over four variants (primary / secondary / ghost / danger) × three sizes, differing only in brushes and metrics |
| Split button | A custom control: two halves sharing the primary template, joined by per-corner radius — visually one body, semantically two buttons. The seam is the menu half's own 1 px left border, not a line between them: WPF paints a `Border`'s background *inside* its border, the inverse of CSS, so two halves each carrying a transparent 1 px edge leave a pixel of gap on either side of any separate line. For the same reason the two halves share one enabled state — the chevron reads the primary half's, which already folds in the command's `CanExecute`, so a divider and a chevron can never stay bright beside a greyed-out button |
| Chip | A `ToggleButton` style plus a counter text style |
| Icon button | Its own compact template, with a toggle variant for the layout-mode icons |
| Switch | A `CheckBox` template — WPF has no toggle switch |
| Segment | An `ItemsControl` of `RadioButton`s — the `Debug｜Release` control, and the About dialog's tab switch |
| Input | A `TextBox` style with watermark, prefix and invalid states, in two heights: the default one, and a shorter variant for the 28 px panel-header strip, where the default would fill the strip edge to edge and push its focus ring outside. The template deliberately leaves `PART_ContentHost` without a margin: WPF applies `Padding` to the content host itself, so a template that also binds the padding to a margin indents the caret and the typed text by two paddings instead of one. `DsChrome.IsClearable` is opt-in and only the two search boxes set it (the project filter and the branch search): while the box holds text a 16 px `✕` sits inside the right padding — which is reserved whether or not the `✕` shows, so typing never narrows the text area — and clicking it empties the box through `DsChrome.ClearTextCommand`, a normal edit that the filter binding and `TextChanged` both see. The `✕` is not focusable, so the caret stays in the box |
| Select | A `ComboBox` template, ported from the design system's `<select>`. It is the library's one component with no live consumer — external-project cards carry no source picker (§10.4) — and is kept so the port does not have to be redone. Same input shell and focus ring as `Ds.Input`; the dropdown carries the same overlay chrome as the popovers, at a smaller radius. The chevron reuses the chip dropdown's existing glyph rather than adding a second copy of the same geometry, and the row hover runs through the same `DsTransition` gate as every other 120 ms colour change in the library — no bespoke entrance animation was added for the popup itself |
| Tooltips | Open with **no delay** and stay until the pointer leaves, on disabled elements too. All three are `ToolTipService` attached properties that WPF reads from the tooltip's *owner*, not from the tooltip — set on the `ToolTip` style they are dead, which is how every tooltip in the app ended up on WPF's ~1 s default and looked like it never appeared. The defaults are overridden once, on `FrameworkElement`'s metadata (`AppTooltipDefaults`) |
| Scrollbar | An implicit `ScrollBar` style — a 10 px transparent rail, no arrow buttons, and a neutral thumb pill inset by 3 px. The pill reacts to the *rail*, not to itself: a 4 px pill is a poor grab target, so as soon as the pointer enters the 10 px rail the inset flows from 3 px to 1 px — an 8 px pill — and the fill steps once up the neutral ramp; dragging steps once more. Only the pill grows, never the rail, so hovering never re-lays out the content beside it. Being implicit the style crosses template boundaries, so stock and third-party viewers alike (the console editor included) wear it without their XAML knowing; the stock corner square between two bars is neutralised app-wide |
| Kbd · ProgressBar · Popover · Dialog · Focus visual | Styles over stock elements. A focus ring is a rectangle pushed outside its element by `-(offset + stroke/2)` and rounded by the same amount so it follows the corner — arithmetic XAML cannot do, so `DsChrome.FocusRingOffset` derives both. Its default is `NaN`, not zero: zero is a real offset (the input's ring hugs the edge with no gap) and WPF skips a property's change callback when the assigned value equals the default, which would leave that ring flat against the box and square-cornered |
| Status glyph · building spinner · status dot | Custom controls drawing rings and dots — the spinner is the glyph's dashed ring, rotating, so the dash pattern has one source and is converted to WPF's stroke-relative unit per stroke width. Rotation is the *only* thing that moves there: the glyph itself holds no animation clock, so it is not a motion owner and carries no motion seam |
| Tracked text | Custom element for letter-spaced caps labels (§14.2) |

The action bar's chip, secondary-button, icon-button and segment-item styles each carry a `Ds.Bar.*` sibling
(`Ds.Bar.Chip`, `Ds.Bar.Chip.Action`, `Ds.Bar.Button.Secondary.Sm`, `Ds.Bar.IconButton`, `Ds.Bar.Segment.Item`) —
`BasedOn` the shared style, adding only the bar's hover triggers (§13.2 "The whole bar speaks one hover
language") so the base styles the rest of the app uses (the ShellRoot filter chip, row icons, dialogs) are
untouched. `Ds.Bar.Chip`'s neutral-hover trigger and its checked-hover trigger key off opposite values of
`IsChecked` (`False` and `True`), so exactly one of them ever matches a given chip and there is no ordering
between them to reason about. A checked, hovered chip — a lit filter chip, an open branch popover chip
— answers only the checked-hover trigger: ground and hairline step to `amber-soft-hover`/`amber`, and its text
stays whatever `Ds.Chip`'s own `IsChecked` trigger already set (`amber-text`), because the checked-hover trigger
never touches `Foreground`. An unchecked, hovered chip answers only the neutral trigger.

A running Sync or maintenance button is not a `ToggleButton`, so it has no `IsChecked` to key a hover trigger
off; `DsChrome.IsActive` is the attached stand-in, set the moment the job starts and cleared the moment it ends,
read by the resting trigger the same way an open chip's `IsChecked` is. Its *hover* trigger, though, cannot key
off the button's own `IsMouseOver` at all — the button is genuinely disabled for the run of the job (its
command's `CanExecute` is false), and a disabled control is excluded from WPF's hit-testing outright, so its
`IsMouseOver` never becomes true regardless of where the pointer sits. `DsChrome.IsHoverProxy` is the answer:
each of the four buttons (Sync, Clean, Optimize, Resolve) sits inside its own always-enabled `Border`, sized to
its exact bounds and otherwise invisible, and `DsChrome.WireHoverProxy` wires that Border's `MouseEnter`/
`MouseLeave` straight onto the button's `IsHoverProxy` — WPF routes mouse-over to the nearest *enabled* ancestor
when the element the pointer is over is disabled, which is exactly the wrapping Border, so the Border (not the
button) is what actually receives `MouseEnter`/`MouseLeave` for as long as the button stays disabled. The
active-hover trigger reads `IsHoverProxy`, not `IsMouseOver`, and asks nothing of `IsEnabled` either, since the
button is deliberately drawn live while its own command is closed.

Σ's icon answers hover through a third, narrower channel of the same shape: `DsChrome.IconForeground`, set by
`Ds.Bar.Chip`'s own Setter (`text-dim`, resting) and by its neutral-hover trigger (`text-primary`) exactly the
way `AnimatedForeground` carries the rest of the chip. It exists because Σ's chip shares `Ds.Chip`'s resting
`Foreground` (`text-secondary`) with every other chip that has a label, but the design system draws a chip's
*icon* one shade dimmer than its label at rest — binding Σ's icon straight to the chip's `Foreground` (the
pattern the branch and perf icons use, where the icon's resting shade already equals the label's) would
have raised Σ's icon to `text-secondary` at rest, losing that shade instead of merely failing to animate it.

Three pieces of shared machinery keep the copies from multiplying:

- **`DsTransition`** implements the design's 120 ms state transitions. A template's state trigger points an
  attached property at a *token brush*; the class then installs a template-local, unfrozen brush on the real
  property and animates that copy. This is the standing answer to the frozen-brush rule of §14.5 — a shared
  resource brush cannot be animated, and animating one would drive every consumer at once. Colour is not the
  only axis: the same class flows a translation (the switch thumb) and an inset (the scrollbar pill) through
  one shared gate, so every 120 ms transition reads the same duration, the same curve and the same
  reduced-motion signal. What WPF cannot animate is worked around rather than dropped — the scrollbar pill's
  corner radius has no animation type at all, so it is *bound* to the animating inset and follows it frame by
  frame, which also keeps the pill a true capsule at both widths (a fixed radius would be clipped
  horizontally but not vertically, turning the ends into ellipses).
- **`PopIn`** is the single 140 ms entrance animation, shared by both popovers and the Build menu. There is no
  exit animation; overlays hide immediately.
- **`RevealStagger`** owns the hero acquisition, generation stamping and guarded release of the opening
  reveal. The *cadence* is deliberately not shared — the graph staggers by layer, the list by row (§13.2).

The branch popover derives from a common base that owns the open state, the refresh-then-animate-then-focus
sequence, the Esc handling (a popover is a separate HWND, so the window-level Esc chain does not reach it) and
outside click; only the branch search filter is its own. The width belongs to
the shell `Border` alone — each body stretches into whatever the shell's padding leaves rather than restating a
number, since a restated width silently drops the shell's border thickness and WPF then clips the overflowing
edge of the body.

Filtering is a free-text query (case-insensitive substring on the project *name* only — never the path) ANDed
with the selected state chips — `building` (compiling right now), `current`, `failed`, `warn` — which
are OR'd among themselves (§13.2, "The chips combine"). `stale` is a bucket of the same rule and the counters
still fill it, but no chip offers it, so nothing can switch it on; `Σ` empties the set whatever is in it. The
active set appears as a removable chip in the panel header.

### 13.9 Keyboard

| Key | Action |
|---|---|
| `F5` | Build — or Stop while a run is in flight |
| `Ctrl+F5` / `Shift+F5` | Rebuild |
| `Ctrl+F` | Focus the project filter |
| `F1` | About — version, shortcuts and diagnostics |
| `Ctrl+F1` | What's new — release notes (toggle) |
| `Esc` | Close the topmost layer (see above) |
| `Alt+B` | Global hotkey: restore the window from the tray |

The key → intent table is a pure, tested structure that `MainWindow` merely wires into `InputBinding`s, and
every dispatch honours the command's `CanExecute` — a shortcut never bypasses a disabled button. `F1` and
`Ctrl+F1` are ungated: each toggles its own dialog and fires even while another modal is open, because
layering the three answers the unsaved-draft worry better than deafening a key would (§13.3). Double-Shift
and `Ctrl+P` are *negatively pinned*: a test asserts they are **not** bound, so they cannot reappear by
accident.

The table above is not written twice. A **shortcut catalog** derives each gesture's display text from that
same key → intent table — and the global hotkey's from the hotkey default — and pairs it with the one
sentence that describes it. The About screen's shortcut rows, the Build menu's `Ds.Kbd` badges and the info
button's and the What's new button's tooltips all read from it, and a source guard forbids any production file
from writing a gesture as a literal. The badges used to be hand-typed strings living next to a binding table
that could change
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
card/panel 6, overlay 8, pill 999, **console 0**. The pill value is CSS-only: a WPF `Border` clips an oversized
radius horizontally and vertically on its own, so 999 draws an ellipse — WPF capsules (the switch track, the
scrollbar pill) use half their height instead. **Elevation** exists only on floating overlays — two shadow
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
| Unknown — no decision yet | dashed circle | Not synced |
| To build | dashed circle | To build |
| Marked — lit by the marking wave | dashed circle | Marked to build |
| Up to date | ✓ in a ring | Up to date |
| Queued | clock | Queued |
| Building | rotating dashed ring | Building |
| Succeeded — built by this run | ✓ in a ring | Up to date · *Succeeded* in the console header |
| Failed — a compiler failure, in this run or proven by the ledger | ✗ in a ring | Failed |
| Skipped — run-story surfaces only | — in a ring | Skipped |

Glyph and text are both drawn from the visual status. On the state surfaces — the row glyph's and the graph
node's screen-reader names — the word names what the surface *shows*, with the same words the filter chips use
(`StatusGlyph.LabelFor`, which reads the same state bucket as the counters): a skipped up-to-date row is
announced *Up to date*, never *Skipped*, and a row this run built is *Up to date* too. The console header is a
run-story surface: its status word names what the engine last said about the project in this run
(`StatusGlyph.RunLabelFor` — *Succeeded*, *Skipped*, *Discovered* for a project the run has not touched), and
shares the words it has in common (*Queued*, *Building*, *Failed*) with the state table rather than spelling
them again. The `—` is a run-story mark: it appears on the event stream's skip line and in the console
header's run result, never on a list row, a graph node or a counter — a skipped project keeps the colour and
glyph of its standing.

**One colour channel — the visual status.** The row's stripe, the dot beside the name, the status glyph, the
graph node's border and the cube inside it are all painted from a single value (`VisualStatus`), and colour
therefore tells exactly one story: *the state of the project's output*. It is built in two layers. The base is
the **standing** (`StandingStatus`), read from the preview's decision alone — `unknown` when there is no
decision, `current` (green) for `UpToDate`, for a project waiting on a dependency (its own output is sound;
the waiting is the triangle's to say) and for one built outside this tool (its output is current, only not
this tool's — §7.6), `stale` (plain grey) for a changed, never-built or dependency-tainted project and for an
output that is stale, missing or replaced, and `failed` (red) for `LastFailed`, which the engine reports only when
the ledger can prove the
failure (§7.5). Over it lies the **run**: `marked`
(this operation's scope), `queued`, `building`, and the results `succeeded` (the same green as `current`, kept
apart so the run can still say "just built") and `failed`. A result does not outrank the standing it wrote:
`succeeded` shows only over a current standing (or where there is no decision at all); a success that leaves
the output to build — a Clean, or a cycle member whose group did not converge and whose success the engine
therefore does not keep (`trusted: false`) — shows that grey. Red is evidence and nothing else, and on a state
surface it comes only from the standing: a failure the engine counts as evidence writes `LastFailed` into the
standing, while one it does not — a timeout, a stop, an invoke error, a failed Clean, a compiler failure inside
a cycle group that did not converge — writes `NeverBuilt`, and over that stale standing the run's `failed` gives
way to the grey (only a row with no decision at all keeps the run's red, since the result is then the one thing
known). The verdict is the engine's: one gate in the Supervisor — a trusted result of a compiling target, a
compiler failure (`FailureClassification`) and a known signature — decides it once, writes the ledger with it
and sends it on the failure event (`Evidence`); the same place decides whether a success is kept and sends that
on the success event (`Trusted`), so the row and the next Sync never disagree. The run-story surfaces still say
`failed` for every failure and `succeeded` for every success. Being skipped is not a colour — a skipped project
falls back to its standing, although the run story (the ribbon's `N skipped`, the console's *nothing to compile
in this run*) keeps counting it until the next operation begins. A result is written into the standing as the
project finishes and stays there until a later decision changes it, so colour is cumulative rather than the
story of the last operation alone. The next preview that arrives outside a run — any Sync's, the silent one a
return to the window starts included — decides every row afresh, a row the last run finished included, so an
output that went stale in the background after the run does not stay green; only while a run is still in flight
does a preview leave a finished row's decision alone, since a preview arriving mid-run describes the run's plan
from before that row's result. Switching the configuration moves the standing
ahead of the next preview as well: the configuration is part of every signature, so every decided row drops to
`stale` at once, with the reason the next preview will give — `SignatureChanged` when the project has ever built
successfully, `never built` when it has not or when its output was missing (for a row that last failed, the
built commit the preview carries is the trace of a past success); the next Sync reads the new configuration's
own output evidence — while a row with no decision stays unknown. The change also neutralises the
previous run's fields, so a row that just succeeded does not keep the run's green, and it closes the previous
run's story: a finished run's summary and a stopped run's `Stopped — n/m · k not built` both give way to the new
plan (`Ready — N to build`). A stopped run's plan belongs to the old configuration, so there is nothing under
the new one it could be resumed as; the next *Build* starts from the new plan. `queued` is amber, not grey:
being in the queue is not a result, it is the scope of the operation that is running, and the amber the marking
wave lit must not go out when the run begins. The mapping lives in one place (`VisualStatuses.For`) and every
surface reads it; the run-story surfaces map the engine's status on their own through `VisualStatuses.OfRun`,
which is the only mapping that still yields `skipped`.

**Queued reads only the running operation's own plan.** A row is not amber merely because it is dirty
(`WillBuild`) — `WillBuild` is a standing fact about the project, decided fresh after every Sync and unaware of
which run is in flight. The queue flag is cleared in exactly two places — at the start of every run, before that
run's own preview has had a chance to say anything, and when the run ends — and in between it is written only by
that run's own preview, never re-derived from the standing `WillBuild`. A preview that arrives while no run is
in flight — a Sync's — never raises it. The distinction matters exactly when the
two disagree: a single-project run cuts the engine's plan to the one target (§8.1), so its preview names only
that project, and every other row — however dirty a stale Sync left it — is never handed the flag and keeps
its standing colour for the run's whole life. Between the run starting and that preview arriving, the marking
wave (above) carries the target's amber on its own — the two channels hand off without the colour going out.

A project this run only evaluates conditionally (§8.3) is dirty (`WillBuild=true`) but is not handed the queue
flag either — it may still be skipped once its turn comes, if its recorded root has not recovered, so amber at
the start of the run would be a promise the run might not keep. The engine's own preview says so directly
(`Conditional`); the row, the marking wave and the run's fixed progress denominator all read that one flag,
never re-derive it.

**The one exception: a cycle member's cube.** In a cycle member the cube inside the node is **always amber** —
the graphical proxy of the row's amber warning triangle — while the frame carries the member's own state like
any other node. Nowhere else do the frame and the cube part company. Membership is not a status (it is passed to
the node separately and never changes the frame), so neither a Sync nor Resolve cycles compiling the member puts
the cube out. Membership never reaches the list's colour: there the stripe and the dot follow the standing like
every other row, because the triangle already says it. This is not the orange channel returning — the tone is
the warning's own amber.

**The start mode** is the `unknown` standing: a row that has no decision yet — the application has started but
no Sync has run, or the repository root has just changed and the decisions the rows carried no longer describe
what will be built; the list and the graph drop back together. A branch change does not drop them: the rows
keep their decisions while the screen starts over (§10.2), and its Sync's preview recolours them with the new
branch's decisions. Nothing is known, so nothing is
coloured: the row draws a plain grey stripe at full opacity and a **four-arc ring** in place of the filled dot,
the glyph is a dashed circle, and the graph node carries a dashed border. The mode drops the moment a decision
arrives — a Sync's preview colours every row with its standing — and the ring cross-fades into the filled dot,
380 ms, same element, same size, so nothing shifts. Starting an operation does not drop it; a decision does. The
stripe and the ring draw at the full opacity of every other row rather than a fainter shade (half and 0.85
opacity): a fainter start mode would imply a plan before one exists, and reads as the list looking washed-out
right after a Sync rather than simply waiting. The cross-fade survives only because the ring still has a real
transition — from arcs to a filled circle — to make.

The row is drawn without dashes on purpose. A dashed 2 px stripe does not land on the pixel grid and an 8 px
dashed circle renders ragged; opacity and an arc ring say the same thing cleanly. The node border stays dashed
because a stroked rectangle has neither problem, and the glyph's dashed circle stays for the same reason —
the building spinner is that ring, rotating.

**Two channels were removed, and their information did not go with them.** Until v1.11 the interface carried
three orthogonal channels: the result (stripe, glyph, node border), the plan (an amber/grey will-build dot and
the node's core), and the structure (an orange mark for cycle membership). Three meanings shared the same few
pixels, and amber and orange were not reliably distinguishable side by side. The plan moved to the decision
label described above; the structure moved to a **single amber warning triangle** in the row's fixed 14 px slot.
Orange left the interface entirely.

**The warning triangle.** Statusless, always amber, one line of tooltip: `In a dependency cycle`, or
`Dependency issue: Sales.Core +2` — the first name and a count. Red is never used here: red means "built and
blew up". The *reason* — the cycle path, the full member list, why a project was skipped — lives in the
project log, where there is room for it. The status glyph always shows the real status, the warning never
replaces it, and while the row is building the slot is empty so nothing competes with the spinner. A `Build`
will not compile a cycle; *Resolve cycles* will (§8.1). The graph carries no triangle at all.

The dependency triangle is **cumulative**. Its roots come from one place on the row (`WarningRoots`): this
run's dependency list when the run produced one, otherwise the ledger's note — a project whose last success was
built against a broken dependency (`WaitingForDependency`) carries the recorded roots from the preview, so the
triangle is there right after a Sync, survives the next operation's neutralising, and goes only when the note
does (the preview stops reporting it) or when the decisions are dropped with a repository change.
Two questions are kept apart on purpose: the `⚠` chip and the `warn` filter count the cumulative triangle,
while the ribbon's run summary — `(N dependency-affected)` — counts only the projects this run found a
dependency issue on, because it is the story of the run.

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

Four facts share the warning slot, and only the strongest is shown, because the tooltip is one line:

| Outcome | Tooltip |
|---|---|
| This run's rounds could not converge the group | `Cycle did not converge — its projects are still out of date` |
| The group ran out of rounds and this member is green | `Cycle did not fully settle — output may be one generation stale` |
| The row is in a cycle | `In a dependency cycle` |
| A dependency failed or was not rebuilt | `Dependency issue: Sales.Core +2` |

The order runs from the most specific claim to the most general. The first two are about how much a result can
be trusted rather than about what the result was; the convergence verdict comes from the run's own
`cycleCompleted` rather than a memory of an earlier one, so it appears in the very run that proved it and
regardless of how the individual member ended — a member that went green inside a group that never converged
is still holding a stale output, and the counter reads it the same way, without a status gate. Membership is
the weakest and loses to all of them: it asserts nothing about the output, only about the graph. Dependency
issues come last because they are about someone else's output: they last as long as the ledger's note, but a
fact about the row's own cycle is always the more precise thing to say.

The run summary carries the same news at run level: `(N stuck in a cycle)` beside the skipped count, on the
completion line and on the *everything up to date* line alike. Without it a run whose only casualty is a cycle
that would not converge reads as an unqualified success, and the up-to-date line would go further still and
imply the cycle is not there.

### 14.4 Iconography

Lucide geometry, 1.5–2 px stroke, single colour, 12–16 px, authored as XAML geometries. **Never emoji.** The
building spinner is not a separate drawing — it is the start-mode dashed ring, in amber, rotating linearly
over 1.4 s. The maintenance box borrows it in place of an icon: whichever of the three buttons is running swaps
its eraser, gauge or unlink mark for the spinner, in the same 12 px box, so the strip needs no second indicator
(§13.2).

The Build split menu and the project row menu share **one icon family** on a single grid: play, rotate-cw,
brush. The mapping lives in one place so the two menus cannot drift; the ⋯ that opens the row menu belongs to
the same family of filled marks as the drag grip. The application icon is a multi-size ICO with the 16 and 24 px rasters hand-corrected;
carets and chevrons are drawn, not typed.

Two icons have no literal counterpart in the design source and are marked *derived* in the dictionary, with
the reasoning written beside them: the caption restore glyph, and the `info` circle in the title bar. Both are
drawn on the same grid and at the same stroke weight as the neighbour they sit next to — the info icon shares
`Icon.Gear`'s 1.7 px so the two buttons carry equal optical weight. The What's new icon between them (a
four-point star, keyed `Icon.WhatsNew` rather than the design's own name for it — that name collides with an
unrelated source guard protecting the event stream's own celebration vocabulary) is drawn at the same 1.7 px
for the same reason; all three title-bar icon buttons read as one family.

**Two marks, one hierarchy.** The application carries its own brand — five pill strips and a gradient chevron —
and the company logo sits behind it. Both are controls, not fragments of markup: `Controls/AppMark.xaml` draws
the product mark (title bar 19 px, About hero 30 px) and `Controls/BrandLogo.xaml` the company wordmark (title
bar 10 px at 55 % opacity, About 13 px at 80 %). The company logo is optional; where it is absent, the hairline
separating it goes too.

**The product mark is drawn once and consumed twice.** The five pills and the chevron live in
`Resources/BrandGeometry.xaml`; `AppMark` and the animated tray indicator (§12.3) both ask for them by key. A
second drawing would be a second truth: one gets corrected, the other does not, and the brand quietly becomes
two different shapes. A guard asserts the geometry appears in exactly one source file — the assertion is
unchanged, only the file moved. The shared dictionary holds the source SVG's own coordinates rather than the
folded-in ones the mark used to carry; each consumer shifts its own canvas instead, which is why a test measures
the drawn box and not just the figure count.

The chevron is the one gradient in the application, and it too is a single shared brush. Flat surfaces are the
rule and a guard enforces it, with a single file-scoped exemption for the mark's dictionary: flattening a logo
would mean redrawing it, and source artwork is transferred verbatim. The chevron is amber — the same accent the
interface uses — which is deliberate: the brand speaks the interface's palette. The cost is that the mark
carries accent weight in the title bar, so no other amber element belongs in that region.

The mark's palette comes from the neutral ramp and the amber family, except a few intermediate tones that exist
only in the artwork; those are declared in `Tokens.xaml` beside the rest, with their reasoning, exactly like
the other values the design source does not name. The chevron's three gradient stops are also exposed as raw
`Color` resources — `Color.Amber`, `Color.AmberBright` and `Color.Brand.ChevronDeep` — because a gradient stop
takes a colour rather than a brush; the brushes are derived from those colours, so no hex is written twice.

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

Five contract rules, each enforced by a test:

1. **One hero at a time.** `MotionCoordinator` is the single gate; the graph frontier and the list frontier
   share one key and therefore count as *one* hero and play together, while any other hero is refused and its
   owner jumps to the end state.
2. **Reduced motion is an OS signal, not an app toggle.** `SystemParameters.ClientAreaAnimation` is tracked
   live; the four `Duration.*` resources are zeroed and restored in place. Pure-XAML storyboards must use
   `DynamicResource` — a `StaticResource` resolves once and would never see the change — and code-driven
   animations must read the setting *at animation start*.
3. **No literals.** Hardcoded hex or millisecond values in animation code fail a guard test. There is one
   file-scoped exemption on the XAML side, and it is the motion counterpart of the verbatim-artwork rule that
   already exempts the mark's gradient: the tray indicator's three-second brand loop is a delivered timeline —
   an entrance, a hold, and an exit, each piece with its own delay, curve and travel distance — not a member of
   the 80–280 ms interface ramp. Binding it to a duration token would not shorten it, it would destroy it.
   Reduced motion is honoured there by never starting the loop rather than by collapsing its durations, and the
   exemption is paired with a test that fails if the exempt file stops carrying a timeline, so it cannot decay
   into a dead line that someone later reads as permission.
4. **Frozen brushes cannot be animated.** Shared/frozen resources are copied per instance before being driven;
   `ContainerVisual.Opacity` cannot be animated at all, which is why graph layer hosts are `UIElement`s.
5. **WPF does not premultiply, CSS does.** WPF interpolates a colour's channels straight, so whenever the two
   ends of a fade carry *different* alpha the RGB races ahead of the alpha and the midpoint lands outside both
   endpoints. `Colors.Transparent` is the loud case — it is `#00FFFFFF`, so a fade to a dark surface walks its
   RGB through white and flashes light grey — but the same thing happens between any opaque colour and a
   translucent one: a chip going from its hover grey to `amber-soft` composited to (84,63,25) halfway,
   roughly twice as bright as either end, so the colour left and came back within one click. Every colour
   timeline is therefore built by one shared factory (`MotionTokens.SplineColorTo`) which declares *both*
   endpoints and, when their alphas differ, walks the **premultiplied** path a browser walks — sampled along
   the easing curve's own parameter, since that path is not a single keyframe. Equal alphas are left alone:
   there the common factor cancels and straight interpolation is already the premultiplied one. This is why
   no consumer may hand-roll a colour keyframe.

**Two choreographies frame an operation.** They are the largest pieces of motion in the application, and both
are driven by one `DispatcherTimer` apiece (`StepPlayer`) with their numbers in pure cores
(`MarkingChoreography`, `EndFinale`).

The **opening** plays the same way for every operation — Build, Rebuild, Clean, a row action, Resolve. It
begins by **neutralising**: the console and the event stream are cleared, and every row drops the previous
run's fields and nothing else — status (back to pending), duration, this run's dependency list, the cycle-round
flags, the skip reason and the marks. What describes the output is not touched: the decision and its reason
(the scope is read from them, and so is the standing colour each row keeps showing), the last-built and
failed-at times, the own-files and local-edits facts, the ledger's dependency note — so the warning triangle a
note holds up stays up — and everything structural: cycle membership, the decision label, the layer. A Sync
neutralises through the same method and lands on the same ground. No result of the previous run is on screen
as a run result when the wave starts; what that run achieved remains only as the standing it wrote. Rebuild
neutralises in place rather than emptying the list — clearing it would destroy the very rows the wave is
marking.

Then a neutral moment of 440 ms, in which even the scope still wears its standing colour; then the **wave**, in
which the scope lights amber one project at a time in *random* order (110 ms per node, the chain capped at 1.1
s, so 36 projects take no longer than four); then a moment with the plan standing on screen; then the
**overlapping farewell** — every graph node outside the scope starts fading to 0.18 over 1120 ms, and 560 ms
later the scope begins its own **sequential handover**: each node dims straight to the run's own dim level (0.13
— the same value a queued node gets once the run actually begins), not all at once but in the order it lit, the
first to light the first to dim, up to 40 ms apart per node (capped at a 700 ms tail) and 400 ms per glide.
Landing on the run's own opacity rather than an intermediate amber is the point: a project that is already
dimming when its own build starts does not visibly change again, so the handover from marking into running reads
as one continuous motion instead of two. The farewell lives only in the graph — the list's own opacity holds at
1 through the whole choreography, because a run has visibly already begun by the time the farewell plays, and a
second fade there did not read as new information, only as noise (measured). The wave itself is random rather
than in build order by explicit decision, and the handover reuses that same order — a project settles in the
sequence it lit, not a freshly drawn one.

Keeping the two surfaces together takes one deliberate wire. A row repaints itself from its own binding the
instant it is marked, but the graph is a pushed channel: it is handed statuses, and if the wave does not hand
them over it repaints only when the run tick next comes round, a fifth of a second later. At the wave's tempo
that is six or seven nodes arriving at once against a list that is flowing, so the wave pushes the graph at
its own pace rather than leaving it to the tick. For the same reason the first step of a choreography runs
synchronously instead of waiting for the sequencer's first tick: requesting a run puts the graph into its run
phase, which starts dimming every node, and the choreography only overrides that decision from its first step
— one frame of nothing in between is one frame of the graph going out and coming back.

**The run command goes out when the choreography ends**, not when the button is pressed. Overlapping the two
was tried — send immediately, play the choreography over the engine's planning window (external update, scan,
graph, topology, incremental) and let `runStarted` end it — and the cost was that the animation became
conditional on how long planning took: warm repository, and `runStarted` arrived before the wave finished;
cold, and it did not. The same click was sometimes animated and sometimes instant. A choreography
either always plays or never does. The operation itself still begins on the first frame — the pill lights,
the button becomes *Stop*, the console records the request — and only the command waits. The view-model owns
the scope and awaits a gate; the shell owns the timing and closes it.

**The choreography's last frame holds until the run takes over.** When the sequence ends on its own the driver
releases the gate but keeps its final step: the settled opacities — 0.13 on the scope, the very value the
run's own opacity system gives a queued node, and 0.18 on the rest — stay on the graph while the engine plans.
Landing exactly on the run's own dim level is what makes the hold invisible: nothing about the frame has to
change when the run actually starts, because marking and running already agree on what a not-yet-building
node looks like — there is no second value for the graph to step through on the way in.
`runStarted` is what drops the hold — the shell pushes the run phase and the fresh statuses first
and only then cancels the choreography, so the graph moves from the farewell straight into the run's own
opacities with no step left to take. The prototype starts the run in the same instant the sequence ends; under
a real engine, holding the frame is the equivalent. Letting the sequence fall back to full brightness and
dimming again seconds later, when the run began, read as a double fade.

Because nothing has been sent yet, **Stop during the choreography cancels the run rather than stopping it**:
no `startRun`, no `stopRun`, and the console says `Cancelled — build not started`.

The wait is also why `queued` is derived from a run that is *live*, not from one that has merely been
requested. Were the request counted as a run, every project in the plan would turn queued-amber on the click
itself and the neutral moment and the wave would both be invisible. No information is lost by waiting: the
wave lights exactly the set the queue would have, only progressively — and when the choreography is skipped
(reduced motion, or an empty scope) the scope is marked in one step, so the amber still appears at once. If
the run never starts — the command fails, or the engine never answers — the marks are cleared, because an
operation that did not happen may not leave its colour behind.

**The scope fades into amber; it does not snap.** Every surface the wave touches — the node's border, its
fill and the cube inside it, the row's stripe, its dot and its name — crosses to the new colour over 200 ms
on the standard curve, and all of them go through one function (`MotionTokens.TransitionTokenBrush`); the
name used to jump straight to its emphasised colour the moment its row was marked instead of easing there
like the stripe and dot, so the name and the rest of the row told two different stories about the same wave.
It draws from the same delayed call the stripe and dot already get — a row's own turn in the wave, not a
second timer. Colour
transitions are otherwise instant here, and that deviation is measured and deliberate: WPF cannot interpolate
a brush property, so a transition means a local `SolidColorBrush` per surface plus a `ColorAnimation`, and
when 177 projects change status in a single tick — which is exactly what the start of a run does — 531 of
each push the tick from 11 ms to 51 ms and break the 50 ms UI event budget. The wave is the opposite case:
its tempo is 110 ms per node (about 31 ms across 36 projects), so one or two nodes change per tick. The
transition is therefore open only while a marking choreography is playing; a surface hands over to a local
brush for its duration and is given the shared token brush back afterwards, so it never loses the reference
permanently.

The **ending** — the neon ignition — lives only in the graph; the list stays still. Everything holds dim for
900 ms, then the projects this run actually built (succeeded ∪ failed) ignite in random order like fluorescent
tubes, flickering irregularly over 1150 ms with a chain of at most 1.5 s; a 700 ms breath; then every
remaining grey — skipped and untouched alike — comes up **together** over 980 ms. The graph also releases any
selection focus for the same span, gliding to the default view so the whole finale stays in frame (§13.6).
With nothing built it does not play at all, and a new operation cuts it instantly. When the list is filtered,
the finale — like the wave and the run before it — plays across the whole graph as if no filter were set; its
own last step, one short `LightMs` beat after the finale ends, fades the graph back to the filter (§13.6). A new
operation that cuts the finale cancels that step too and keeps the filter set aside for its own run.

Under reduced motion neither choreography runs: the scope is marked and the run proceeds.

**A loop that has to finish gracefully cannot be infinite.** The tray indicator must complete the exit phase of
whichever pass it is in when the build ends, and an endless storyboard has no boundary at which to ask that
question — stopping it would mean cutting it in half. So it runs a single iteration and decides at each
`Completed` whether to begin another; "finish" is then just a flag, with no seeking and no rate changes. The
seam is invisible because the artwork was authored with no empty frame: the last strip dissolves exactly as the
chevron re-enters. Two consequences are easy to get wrong and are pinned by tests — `Stop()` leaves the
animations attached to their elements (and itself raises `Completed`, which would revive the loop), so tearing
down means `Remove()` behind a re-entry guard; and a pending finish is honoured even when the indicator is
dismissed early, or a run would end with no notification at all.

The strips are revealed by a chevron-shaped mask that slides with the chevron, and two rules keep that mask
honest in the live layered window, where frames rendered offscreen do not show either failure. The mask
geometry never moves: the sweep is a render transform on the masked canvas, undone by an equal and opposite
transform on the strips inside it, because an animated transform on the clip geometry itself was not repainted
— strips stayed hidden on the first entrance and one was cut off along a straight edge on the way out. And the
mask belongs to the entrance alone: every pass sets it explicitly at its first frame and releases it on the
keyframe where the sweep comes to rest, so the hold and the exit run with no clip over the strips. The explicit
first frame matters, since a restarted storyboard takes a missing start value from where the previous pass
ended — with the clip already released. Nothing visible changes at rest: the mask only ever clipped what the
chevron already covers, and the title-bar mark draws the same frame with no mask.

Decorative infinite animations run at `DesiredFrameRate=30` — one shared constant, not a number repeated per
owner; all counters tick from one `DispatcherTimer`;
timing-sensitive sequences (the event stream's typewriter) are `Stopwatch`-based rather than trusting the ~15.6 ms
`DispatcherTimer` resolution. Resetting an observable collection is prohibited — it destroys running
animations.

**An infinite animation must stop being visible before it stops running.** WPF's timing engine keeps the whole
render loop awake while *any* clock is active, so one forgotten `Forever` costs far more than itself: an idle
application was measured burning 133 % of a core, with a single thread at 92 %. Being collapsed is not being
unloaded — a hidden control stays in the tree and its own property never changes again — so every infinite
animation is gated on `IsVisible` as well as on its own state, and re-evaluated from `IsVisibleChanged`. The
same is true of the whole window: closing it to the tray hides it and unloads nothing. The gate lives inside the
method that starts the clock, not at its callers, because the callers keep running while the window is hidden —
the console prompt is refreshed on every visual-line change and the event stream's active line on every event —
and a clock stopped only from `IsVisibleChanged` came straight back on the next one. The same discipline applies
to periodic work: a one-shot `DispatcherTimer` stops itself in its own tick (the dispatcher roots it, so an
unstopped one ticks forever and can never be collected), anything called from the 200 ms tick writes only when
the value actually changed, since assigning the same string still invalidates measure and draw five times a
second, and the console's append pump opens its batching window only once a line is waiting instead of waking on
a timer for the life of the application. Measured with CPU cycle counters on an idle application in the tray,
the cursor gate and the sleeping pump together took the process from roughly 81 to 5 million cycles a second;
either one alone removed barely a sixth of it.

**One seam in the tray indicator is deliberately not instant, and it carries no number in code.** The
overlay's disappearance and the balloon would otherwise land on the same frame and read as one abrupt event, so
a short breath separates them; its length is `Duration.Slow`, which means reduced motion collapses it to zero on
its own — a user who asked for no animation is not made to wait. The breath is an injectable seam, so the suite
proves the ordering without spending real time.

### 14.6 Copy and tone

All interface text, project names and logs are **English**; code comments and the decision records are Turkish.
The tone is calm, precise, engineering: no exclamation marks, no jokes, exact numbers and exact state —
`Completed — 3 failed · 24 succeeded · 9 skipped · 1m 12s`. A guard test fails if Turkish text reaches a
user-visible string. A proper noun is not language: the company's registered name in the copyright
(`Delta Yazılım`) is the guard's one named exception, and it exempts only the name, never the text around it.

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
| `build-state.json` | per-project signature, commit, result, duration, dependency-issue note with its root project ids, non-convergent cycle signature, the fed outputs learned from the last success (§7.6); projects from external roots share the file under the same key shape, without a commit or branch (§7.5). A record written before a field existed loads with that field empty | falls back to empty |
| `evaluation-cache.json` | csproj evaluation cache; each entry records the schema it was written under, and an entry from an older schema is re-evaluated rather than served (§6.2) | falls back to empty |
| `source-hash-cache.json` | source content hashes keyed by path, size and modification time (§7.1) — this is what turns the content decision into one stat pass per run | falls back to empty (the next run re-reads and rebuilds it) |
| `run-inflight.json` | the ids of the projects the engine has dispatched and not yet reported — written at dispatch, erased at the result, emptied at the end of every run; left non-empty only by an engine that died mid-run, and read once at the next engine start (§8.7). Absent while no run is in flight | an unparsable file is deleted and nothing is recovered; an unreadable one stays for the next start |
| `ui-state.json` | layout mode + three splits, repository root, configuration, perf mode, layer patterns, external roots (path) and whether to update them (§10.4), whether to stash before a branch switch (§10.3), hotkey, autostart, tray-balloon-shown, last-seen release-notes version. The branch is not stored: it is whatever is checked out. Fields older versions wrote and this one no longer reads are ignored | falls back to defaults; a field whose *type* changed between versions is tolerated rather than taking the whole file down |

Autostart additionally writes one `HKCU\...\Run` value.

The three ledgers are **shared by every workspace**, so neither maintenance operation deletes a file; both work
by key.

*Clean* (§13.2) **resets** `build-state.json`: it removes the entries under each root it cleaned — the
workspace root and every registered external root, a scoped reset that also sweeps the leftovers of projects
since deleted or renamed — plus the entry of any resolved project that falls outside all of them, and leaves
the rest untouched. It touches neither of the other two: they describe what the csproj files and the sources
say, which a Clean does not change.

*Optimize* (§13.2) **prunes** instead, and it prunes all three: an entry whose key points at a file that is
gone from disk is dead, and goes. The first two are keyed by `.csproj` path, the third by *source file* path —
which is why the third is the one that accumulates, and why it is counted on its own. Live entries are never
touched, so no project's build decision moves. Both operations scope by root through one shared prefix
normaliser: a root is resolved and compared with a trailing separator, so `C:\repo` cannot claim
`C:\repo2\...`, and each root gets its own pass, because an external root is not under the main root's prefix.

Optimize also sweeps the ledgers' **orphaned temp files**. An atomic write killed between its temp write and
its rename leaves a `<ledger>.<guid>.tmp` behind; each ledger sweeps only the pattern of its own name, and only
files old enough that no write still in flight could own them.

When an operation finds nothing to change in a ledger, that file is not rewritten at all — no write, no rename
race.

`build-state.json` and `run-inflight.json` share one atomic write path (`AtomicFile`): a unique
temp file, then a rename over the target with a bounded retry for a transient sharing violation.

The Supervisor accepts `--logs` to relocate the log root; the cache and state files, `run-inflight.json`
included, live next to it, in its parent folder. The App never passes it; it exists so the test suite never
touches the user's real data — a test that starts a real engine must isolate its cache this way, and a source
guard pins that (§17.2), because an engine reads `run-inflight.json` the moment it starts.

A pool folder at `worktrees\` may still exist from older versions. Nothing reads or writes it any more; the
console points it out once per session (§12.1).

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
proof both dialogs use), input synthesis (`MouseInput`, the one place a real mouse press is raised, both
halves of the gesture), dispatcher pumping and animation hosting (`DispatcherPump`, `AnimationHost`,
`MotionScope`), a manual STA thread for tests that must skip dynamically (`StaThread` — the STA runner does not
recognise a skip), fixtures (`GitTestRepo`, `LegacyFixture`, `SyntheticGraph`, `JobTestChildren`, `VmTopology`,
`FakeMotionSignal`, `FakeMotionSettings`) and measurement
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
| No hand-rolled colour keyframe | every colour timeline comes from the shared factory, so no surface can miss the premultiplied-alpha rule of §14.5 |
| No sleep-poll | no `Thread.Sleep`-based waiting in tests — synchronization is by handle or signal |
| No Turkish user text | no Turkish string reaches a user-visible surface; named proper nouns (the company's registered name) are the only exemption, and each must still occur |
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
| Modal shell | no dialog file (Settings, About, What's new) carries its own copy of the shared shell's behaviour — scrim and in-dialog clicks, Esc, focus trap, entrance, focus move, the `Ds.Dialog` frame |
| "What's new in" sentence | the versioned What's new sentence is composed only by `ReleaseNotes` — the title-bar tooltip and About's button both read it |
| Git mutation surface (`NoGitMutationOutsideTheWriterTests`) | a mutating git verb (`merge`, `checkout`, `switch`, `pull`, `rebase`, `cherry-pick`, `stash`, `clean`, `reset`, `commit`, `push`) at the head of an argument list appears only in `Core/Git/RepositoryWriter.cs` (§10.1) |
| No worktree surface (`NoWorktreeSurfaceTests`) | no `worktree` git verb and no `BaseIntermediateOutputPath` in the source, no branch or worktree field on `startRun`, and no worktree type or discriminator in the contract |
| No product name in code (`NoProductNameInCodeTests`) | no identifier under `src` — type, member, enum value, parameter or local — carries the name of the product the tool was first built for; comments and string literals are exempt, and the code inside an interpolation hole is still scanned |
| Isolated test engines (`SupervisorIsolationGuardTests`) | every test that starts a real Supervisor gives it an isolated cache (`--logs`, or the shared sandbox), so no test reads or recovers the user's own `run-inflight.json` (§16) |

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

A second group carries the `Measurement` category: probes and measurements that read numbers rather than
assert rules — the tray overlay's own cost, rendered frames of its loop, the notification call, UI latency and
memory under each perf profile, and the content-decision timings. The filter above does **not** exclude them
(`!=` admits every other category value). The tray and perf probes open real windows, show balloons or
saturate every core, so each is gated on an environment variable — `BO_PROBE_TRAY`, `BO_MEASURE_OVERLAY`,
`BO_MEASURE_PERF` — and reports itself as skipped unless it is set. The content-decision measurements are gated
differently: they read a real repository whose root comes from `BO_MEASURE_ROOT`, `BO_MEASURE_COLD_ROOT` or
`BO_CACHE_ROOT` with a local default, and skip only when that root is absent — on a machine where the default
root exists they run with the normal suite.

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
4. **2-D transforms are affine, so a perspective trapezoid is impossible.** The console's panel transition
   needs one — the prototype's `perspective(900px) rotateX(7deg)` narrows the receding top edge and widens
   the advancing bottom one. It is played through a real `Viewport3D` instead (§13.5); a scale-and-translate
   approximation reads as a slide, not as a hinge.
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

- **One repository's history at a time.** External roots (§10.4) are scanned into the same graph and built
  with everything else, but the branch, the target sha, the HEAD watcher and the `N behind` distance all
  describe the repository root alone. Switching branches does not move an external working copy, and an
  external root's own branch is whatever the user left checked out there.
- **One tree, no build-output isolation between branches.** Every run builds the working tree, and no output
  path is changed — neither `OutDir` nor `obj` — for Visual Studio parity, so builds of different branches write
  to the same place. Building another branch means checking it out (§10.3).
- **An interrupt draws its line where it reaches the engine.** A branch change is noticed after git's writes
  settle (1.5 s) and the request then travels over the IPC. A project that finishes inside that window —
  compiled from the old tree, reported before the interrupt landed — is still trusted. The window is short and
  the next Sync reads the new tree, but it is not zero.
- **A rebase interrupts a run in flight.** A rebase moves HEAD once per replayed commit; the watcher folds those
  writes into one trigger, but a run in flight is interrupted by it like any other HEAD movement that is not a
  plain commit.
- **The HEAD watcher needs a reflog.** It follows `logs/HEAD`; a repository that has no commit yet has no
  reflog folder, so the watcher cannot start and says so once. Returning to the window retries it, and the
  window-activation Sync covers changes in the meantime.
- **An automatic Sync does not fetch.** Its `N behind` distance is measured against the last remote state the
  repository already has; the Sync button and a pull refresh it from the network.
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
- **The output evidence trusts file times and lengths** (§7.6), the same assumption the source-hash cache makes
  (§7.1). A DLL copied into a shared folder by hand with the same length as the build output can pass for a fed
  copy; a different length is caught. A file restored with its old timestamp — from a backup, say — does not
  read as newer than the output it predates; git and Visual Studio do not do this. A clock moved back can make a
  new output look older than the ledger's last run, or an input older than the output. These are accepted.
- **Visual Studio and the tool must not build the same project at once.** Both write the same `obj` and the
  same output; neither can tell, and nothing arbitrates between them.
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
- **Four General switches are not wired yet.** *Start with Windows*, *Start minimized to tray*, *Close to tray*
  and *Show notifications* live only in the Settings draft: they are not saved, exported or imported, and change
  no behaviour — every time the dialog opens they are back at their defaults.

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
| Branch name | git's own branch inventory (`for-each-ref`) and the checked-out HEAD; never stored | argv elements of `git fetch origin <branch>`, `git checkout <branch>` / `checkout --track origin/<name>`, and inside the `stash push -m` message | theoretical (below) |
| Perf mode | perf chip | ordinal whitelist | none |
| Layer regex | Settings editor | `Regex` constructor with a 100 ms match timeout | ReDoS closed |
| Solution to open | row icon | `devenv "<sln>"` — hand-quoted | theoretical (below) |
| External root path | Settings editor, or `ui-state.json` | resolved on every run (§10.4): the project files found under it become MSBuild arguments, escaped per MSVCRT rules; the working-copy root becomes the working directory of `git`/`tf` — never an argument | none |

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
- **Atomic state writes:** `build-state.json`, `run-inflight.json`, `evaluation-cache.json` and
  `source-hash-cache.json` are written
  to a unique temp name and moved into place; readers open with `FileShare.Delete` so they cannot block the
  rename, which is retried a bounded number of times on a transient sharing violation. The temp file a killed
  write leaves behind is collected by *Optimize* (§16).
- **Git writes are user actions, each with its own gate, in one file.** The fast-forward is `--ff-only` (so it
  can neither rewrite history nor create a merge), refuses a dirty or diverged tree, and runs only from the
  user's click on the `N behind` chip or for an external root the user left on (§10.5, §10.4). The checkout
  runs only from the user's pick on the branch chip, refuses a dirty tree unless the user turned *Stash and
  switch branches* on — and then stashes with untracked files included, so nothing is overwritten — and is
  locked while a run is in flight (in the App and again in the engine, `checkoutRejected`) and while git is
  mid-operation (§10.3). Nothing writes on its own, and `reset` runs in no flow at all. A source guard keeps
  every mutating git verb inside `Core/Git/RepositoryWriter.cs` (§17.2).
- **Racy-file rule in the hash cache:** an entry whose file was modified within two seconds of the cache being
  written is not persisted, so a file rewritten in the same second at the same size cannot be mistaken for
  unchanged on the next run (git's own index rule).
- **Corrupt-JSON tolerance:** `build-state.json`, `evaluation-cache.json`, `source-hash-cache.json`,
  `ui-state.json` and a project's `project.assets.json` all fall back to defaults instead of throwing. `ui-state.json` additionally tolerates a
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
   junction could produce deep recursion.
3. **`explorer` and `devenv` arguments are hand-quoted** rather than going through the MSVCRT escaper. A path
   containing a quote would break the escaping; unreachable in practice, since Windows file names cannot
   contain one and the paths come from a disk scan.
4. **A branch name reaches git's argv without a `--` separator or pre-validation.** It comes only from git
   itself — the inventory and HEAD — and is never stored; git refuses to create a branch
   whose name begins with `-`, so an option-shaped name would need a hand-edited ref inside the repository.
   Reaching it requires already being inside the user's account.
5. **Supervisor arguments are not validated.** `--logs` and `--debug-hooks` are taken raw. The App passes
   neither; only tests do.
6. **There is no field-level IPC schema validation** (§5.4). A missing field binds to `null` and surfaces at
   the point of use as `planFailed`/`runFailed`.
7. **The cascade guarantee has two documented exceptions** (§4.2, §4.4): processes the App starts on the user's
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
| Tray build indicator — when it shows, exit choreography, one balloon | `App/Services/TrayBuildIndicatorController.cs` |
| …its wiring to the view model (line, phase) | `App/Services/TrayIndicatorBinder.cs` |
| …the animated mark itself (loop, static frame) | `App/Controls/TrayBuildIndicator.xaml(.cs)` |
| …the frameless, non-activating overlay window that carries it | `App/Views/TrayBuildOverlayWindow.xaml(.cs)` |
| Extended window styles for that overlay (`WS_EX_*`) | `App/Shell/Win32.cs` |
| View mode + splitter persistence | `App/Shell/LayoutState.cs`, `App/Shell/UiStateStore.cs`, `App/Controls/DsSplitter.cs` |
| Keyboard semantics (key → intent, Esc chain) | `App/Shell/KeyboardShortcuts.cs` |
| Shortcut display text, descriptions and About groups (single source) | `App/Shell/ShortcutCatalog.cs` |
| Product identity (name, version, copyright, tagline, About overview) and the grouped diagnostics model | `App/Services/AppIdentity.cs`, `DiagnosticsReport.cs` |
| Layer row placeholders (Settings, by row index) | `App/Shell/LayerPlaceholders.cs` |
| Workspace label text (the repository root's folder name) | `App/ViewModels/TitleBarContext.cs` |
| Release notes (What's new data, categories, fold rule) | `App/Services/ReleaseNotes.cs` |

**Engine and IPC**

| Behaviour | File |
|---|---|
| Command/event records, JSON options | `Contracts/Ipc/IpcMessages.cs` |
| Skip reason literals — single source read by Core, Supervisor and App | `Contracts/Ipc/SkipReasons.cs` |
| NDJSON framing, line limit, writer serialization | `Contracts/Ipc/NdjsonFraming.cs` |
| Domain DTOs (`ProjectNode`, `BuildPlan`, `BuildState`, `LayerPattern`…) | `Contracts/Model/ProjectModels.cs` |
| Spawning the engine, generation guard, engine-died signal | `App/Services/EngineHost.cs` |
| Supervisor entry, argument handling, stdout redirect, planner wiring, crash recovery before the host starts | `Supervisor/Program.cs` |
| Command dispatch, per-command input gates; the `checkoutBranch` handler and its run-active rejection | `Supervisor/SupervisorHost.cs` |
| Supervisor path resolution from assembly metadata | `App/Services/SupervisorLayout.cs` |

**Discovery, graph and layers**

| Behaviour | File |
|---|---|
| Workspace scan, ignore list | `Core/Discovery/WorkspaceScanner.cs` |
| Raw csproj XML evaluation; the output path (`OutputFileFor`) and resolved `HintPath` targets | `Core/Discovery/CsprojEvaluator.cs` |
| Evaluation cache (mtime + length fingerprint, schema) | `Core/Discovery/EvaluationCache.cs` |
| `.sln` parsing, project↔solution map | `Core/Discovery/SolutionMapper.cs` |
| Stale-`obj` diagnosis (warn-only, two consumers: the run-start warner and Optimize's removal step), TFM derivation | `Core/Discovery/StaleObjDetector.cs`, `TargetFrameworkMonikerDeriver.cs`, `Supervisor/StaleObjRunStartWarner.cs` |
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
| Propagation, Safe/Fast, SCC composite hash, content fingerprint | `Core/Incremental/IncrementalPlanner.cs` |
| The input set of a project (declared items, folder sweep, `Directory.Build.*`) | `Core/Incremental/ProjectInputs.cs` |
| Content-hash cache keyed by size and mtime, parallel first fill | `Core/Incremental/SourceHashCache.cs` |
| Input collection (files and swept folders), path terms, the two binding passes, output checks per node (`ChecksFor`, `OutputsById`), a cycle member's time check leaving out its siblings' outputs (`ExcludingSameCycleSiblings`) | `Core/Incremental/IncrementalRunBinder.cs` |
| Output evidence: evidence paths and fed candidates, ledger/time mode, time verdict, cycle groups, learning fed outputs, `modified` ↔ `affected` and `outputBuiltAt` helpers | `Core/Incremental/OutputEvidence.cs` |
| Will-build tri-state decision and its reason, the ledger-mode vetoes and the time-mode reasons; the plan-wide pass that weighs a dependency note against its roots | `Core/Planning/WillBuildEvaluator.cs`, `Core/Planning/BuildPreview.cs` |
| Local-edit flag behind `modified · local` (git status ∩ project inputs, main repo root only) | `Core/Workspace/LocalEdits.cs` |

| ETA formula (raw estimate, smoothing, rounding, cycle term) | `Core/Incremental/EtaCalculator.cs` |
| Build state store, duration persistence, non-convergence lookup, invalidation without evidence | `Core/State/BuildStateStore.cs`, `BuildDurationPersister.cs` |
| The in-flight ledger (`run-inflight.json`): dispatch/result bookkeeping, startup recovery and its retry | `Core/State/InFlightLedger.cs` |
| The one atomic write path shared by the build state and the in-flight ledger | `Core/State/AtomicFile.cs` |

**Scheduling and run execution**

| Behaviour | File |
|---|---|
| Ready-set dispatch, resolved semantics, cycle group dispatch and pre-skip | `Core/Scheduling/ReadySetScheduler.cs` |
| SCC membership in build order (scheduler and coordinator read one instance) | `Core/Scheduling/CycleGroups.cs` |
| Cycle round stopping rule (converged / no progress / cap) | `Core/Planning/CycleRoundPolicy.cs` |
| Scope of a `Cycles` run (members + transitive upstream) | `Core/Planning/CycleRunScope.cs` |
| Scope of a single-project run (plan cut to one node, stale inputs) | `Core/Planning/ProjectRunScope.cs` |
| Dependency-issue propagation (failed roots, stale inputs of a scoped run; names and root ids) | `Core/Scheduling/DepIssueTracker.cs` |
| Conditional rebuild of a project waiting for a failed dependency (which runs apply it, the verdict at its turn, root names) | `Core/Planning/ConditionalRebuild.cs` |
| What a row reads the moment a result lands, before the next preview (success, trusted or not · failure · Clean · configuration change) | `Core/Planning/NextPreview.cs` |
| Run snapshot and elapsed clock across segments | `Core/Scheduling/RunSnapshot.cs`, `RunClock.cs` |
| Bounded synchronous retry (used by state store and clipboard) | `Core/Scheduling/SyncRetry.cs` |
| Worker loop, event pump, stop bookkeeping, perf lifecycle, cycle round loop and non-convergence memory; the interrupt flag and the one reporting gate that stops trusting results after it; in-flight ledger calls | `Supervisor/RunCoordinator.cs` |
| Failure-evidence classification (compiler exit vs. timeout/stop/invoke error) — the one clause the evidence gate reads | `Core/State/FailureClassification.cs` |
| Per-run and per-project logs, decision log | `Core/Logs/RunLogWriter.cs`, `RunLogPaths.cs`, `ProjectLogNaming.cs` |
| Log chunking for the UI | `Core/Logs/LogChunker.cs` |

**Build execution**

| Behaviour | File |
|---|---|
| `MSBuild.exe` resolution via `vswhere` | `Core/MsBuild/MsBuildResolver.cs` |
| The `vswhere` search itself | `Core/MsBuild/VsWhereLocator.cs` |
| Duplicate `AssemblyName` detection and the warning it produces | `Core/Graph/ProducerMap.cs`, `Core/Planning/PlanProgressLines.cs` |
| Argument contract (build and restore), MSBuild target selection | `Core/MsBuild/MsBuildArguments.cs` |
| Invocation, output pumping, per-project kill; the restore-only entry point Optimize uses | `Core/MsBuild/MsBuildInvoker.cs` (`InvokeAsync`, `RestoreAsync`) |
| Copy-contention detection and retry decorator | `Core/MsBuild/CopyContention.cs`, `RetryingMsBuildInvoker.cs` |
| `SolutionDir` resolution for restore | `Core/MsBuild/SolutionDirResolver.cs` |

| Output encoding | `Core/MsBuild/MsBuildOutputEncoding.cs` |
| Process launching, argument list discipline, command-line escaping | `Core/Processes/ProcessRunner.cs`, `WindowsCommandLine.cs` |

**Git**

| Behaviour | File |
|---|---|
| All read-only git invocations (HEAD, status, refs, distance, fetch) | `Core/Git/GitService.cs` |
| The only mutating git surface: the fast-forward (dirty gate → fetch → is-ancestor → `merge --ff-only`, `FastForwardUpdater`) and the branch switch (dirty gate → optional `stash push -u` → `checkout`, `BranchSwitcher`) | `Core/Git/RepositoryWriter.cs` |
| Git directory resolution (`.git` folder or `gitdir:` file) | `Core/Git/GitDirectory.cs` |
| HEAD and its ref read from files (loose, packed, linked-worktree common dir) | `Core/Git/HeadReader.cs` |
| Operation markers (merge, rebase, cherry-pick, revert, `index.lock`) | `Core/Git/GitOperationProbe.cs` |
| Tooltip, Build warning, Sync warning and stuck-lock texts for an operation in progress, and the 30 s threshold | `Core/Git/GitOperationText.cs` |
| Reflog line classification (commit, checkout, other) and the strength order | `Core/Git/ReflogEntry.cs` |
| The HEAD watcher on `logs/HEAD` and its 1.5 s settle window | `Core/Git/HeadWatcher.cs`, `Core/Git/SettleDebouncer.cs` |
| Revision text shortening (only a full 40-hex sha is cut to 7) | `Core/Git/RevisionText.cs` |
| The `N behind` chip's command handler (main repository fast-forward) | `Supervisor/SupervisorHost.cs` |
| Automatic Sync decision: triggers (HEAD watcher, window activation, end of run), the one pending trigger, the double-Sync check, the interrupt request | `App/Services/AutoSyncCoordinator.cs` |
| The view model's side of it: the port, the interrupted run's summary | `App/ViewModels/RunViewModel.AutoSync.cs` |
| Git-operation gate: the chip's dot and tooltip, the checkout and pull locks, the 2 s poll, the stuck-lock line | `App/ViewModels/RunViewModel.GitOperation.cs`, `App/Services/IPollTimer.cs` |
| Sync kinds and their rules (clearing, fetch, transcript, visibility, restarting the plan surface) | `App/ViewModels/SyncMode.cs` |
| Plan-surface restart on the Sync button and a branch change: the flag, blanking the list and the graph, the replay on topology, the restore when no topology comes, cutting a playing finale | `App/ViewModels/RunViewModel.cs` (`SyncCoreAsync` — the trigger), `RunViewModel.ActionBar.cs` (`PlanSurfaceRestarting`), `RunViewModel.Workspace.cs` (`OnWorkspaceTopology`), `MainWindow.xaml.cs` (`BlankPlanSurface`), `App/Graph/GraphView.xaml.cs` (`CancelEndFinale`) |
| Branch chip checkout, its gate, the stash setting; the checkout's answer and the pull, with their stream `warn` lines | `App/ViewModels/RunViewModel.ActionBar.cs` (`SelectBranch`, `CanSwitchBranch`), `RunViewModel.Workspace.cs` (`OnCheckoutCompletedAsync`, `OnPullCompletedAsync`, `PullRepositoryAsync`) |
| The legacy pool folder and its one-line hint | `Core/Paths/LegacyWorktreePool.cs` |
| Command execution wrapper and result shape | `Core/Processes/CommandLineTool.cs`, `Core/Git/GitMessages.cs` |
| Sync flow (fetch or last known remote → analysis → events) | `Core/Workspace/SyncWorkspaceService.cs` |
| Clean flow (merged scan incl. external roots → per-root state reset → `bin`/`obj` deletion → summary), the delete permission gate | `Core/Workspace/CleanWorkspaceService.cs` |
| Optimize flow (merged scan → per-project restore → unresolved-reference report → old-style stale-`obj` removal → ledger prune → temp sweep → summary), the restore heartbeat, the collected restore output and its error extraction, the summary terms shared with the stream line | `Core/Workspace/OptimizeWorkspaceService.cs` |
| Workspace-scoped build-state removal (every key under the root) | `Core/State/BuildStateStore.cs` (`RemoveUnderRoot`) |
| Dead-entry pruning (only keys whose file is gone), in all three ledgers | `Core/State/BuildStateStore.cs`, `Core/Discovery/EvaluationCache.cs`, `Core/Incremental/SourceHashCache.cs` (`PruneMissingUnderRoot`) |
| Root normalization and the `C:\repo` / `C:\repo2` prefix trap — one gate for both the reset and the prune | `Core/Paths/RootScope.cs` |
| Orphaned atomic-write `.tmp` sweep (per-target pattern, age threshold) | `Core/Paths/TempFileSweeper.cs`, the three ledgers' `SweepOrphanTempFiles` |
| Human-readable byte sizes (console summaries and the stream) | `Core/Formatting/ByteFormat.cs` |
| `cleanWorkspace` handler and its run-active rejection | `Supervisor/SupervisorHost.cs` (`CleanWorkspaceAsync`), `Supervisor/RunCoordinator.cs` (`IsRunActive`) |
| `optimizeWorkspace` handler: run-active rejection, event bridge, the catch-all that keeps exceptions off the wire | `Supervisor/SupervisorHost.cs` (`OptimizeWorkspaceAsync`) |
| Root-scoped Core service factories, including Optimize's lazily resolved MSBuild toolset | `Supervisor/SupervisorHost.cs` (`WorkspaceServices`) |
| Planning step texts (shared by Sync and the run planner) | `Core/Planning/PlanProgressLines.cs` |

**External projects**

| Behaviour | File |
|---|---|
| Path → scannable root (folder, `.sln` or `.csproj`) merged into one workspace | `Core/Externals/ExternalWorkspaceResolver.cs` |
| Reserved layer name and index for external projects (single source) | `Core/Externals/ExternalProjectsConventions.cs` |
| Working-copy root discovery (`.git` file or directory) | `Core/Externals/VcsDetector.cs` |
| The update step, its gate and its two error classes | `Core/Externals/ExternalUpdater.cs` |
| Per-root revision read, spread over the projects it produced | `Core/Externals/ExternalRevisionReader.cs` |

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
| Filter rule, chip labels and active-chip colours (multi-select set) | `App/ViewModels/ProjectFilter.cs` |
| Warning-triangle text (one line, strongest reason wins) | `App/ViewModels/RowWarning.cs` |
| Operation pill wording (`SYNC` · `BUILD` · `REBUILD` · `CLEAN` · `DEEP CLEAN` · `OPTIMIZE` · `RESOLVE` · `SWITCHING BRANCH`) | `App/ViewModels/OperationLabel.cs` |
| Status counters | `App/ViewModels/RunCounters.cs` |
| Layer grouping (from topology only — no regex in the App) | `App/ViewModels/LayerGrouping.cs` |
| Graph feed construction | `App/ViewModels/GraphBinder.cs` |
| Interaction copy (console notes, empty states) | `App/ViewModels/InteractionText.cs` |
| Settings draft state (layers, external roots + pending root, Save gate and its footer reason) | `App/ViewModels/SettingsDraftViewModel.cs` |
| Settings General page catalog (groups, rows, defaults, dependencies — *Stash and switch branches* included) and its row state | `App/ViewModels/GeneralSettings.cs`, `App/Resources/Controls.xaml` (`Ds.Settings.ToggleRow`) |
| Settings export/import file format | `App/ViewModels/SettingsFile.cs` |
| Inventory publishing (one notification per publish, none when unchanged) | `App/ViewModels/SnapshotCollection.cs` |

**Views and controls**

| Behaviour | File |
|---|---|
| Sticky ribbon: phase, building chips, failure cluster, progress | `App/Views/StickyRibbon.xaml(.cs)` |
| Project row: stripe, dot, decision label, hover icons, play/Stop wiring, breath, shake | `App/Views/ProjectRow.xaml(.cs)`, `ProjectRowActions.xaml(.cs)`; label wording `App/ViewModels/DecisionLabel.cs` |
| Row menu (Build · Rebuild · Clean; ⋯ and right-click) | `App/Views/ProjectRowMenu.xaml(.cs)` |
| Single-project run commands (build · rebuild · clean), run target and lock pushed to rows | `App/ViewModels/RunViewModel.cs` (`BuildProjectCommand`, `RunTargetId`) |
| Build-state row removal after a clean | `Core/State/BuildStateStore.cs` (`Remove`) |
| Row menu placement (row-right inset, row overlap, viewport clamp) | `App/Controls/RowMenuPlacement.cs` |
| Second press on a popover trigger closes it | `App/Controls/PopoverToggle.cs` |
| List with cumulative sticky headers and reveal | `App/Controls/StickyLayerList.xaml(.cs)` |
| Row virtualization with an exact (never estimated) extent | `App/Controls/FixedHeightVirtualizingPanel.cs` |
| Event stream rows, glow-once | `App/Views/EventStreamView.xaml(.cs)` |
| Action bar: sync, counters, chips (the branch chip's amber git-operation dot included), segment, build split button | `App/Views/ActionBar.xaml(.cs)` |
| Build menu (Build / Rebuild / Clean) and the shared icon family | `App/Views/BuildMenu.xaml(.cs)` |
| Maintenance box (Clean / Optimize / Resolve cycles), amber-plus-spinner on the running job | `App/Views/MaintenanceBox.xaml(.cs)` |
| Amber-plus-spinner on a running Sync (same treatment, action bar) | `App/Views/ActionBar.xaml.cs` (`RefreshSyncBusy`) |
| Maintenance-box Clean command, its gate, the request/in-flight guard, the Clean error codes and the Sync chained on completion | `App/ViewModels/RunViewModel.cs` (`CleanCommand`), `RunViewModel.Workspace.cs` |
| Maintenance-box Optimize command, the shared workspace-job gate (mutually exclusive with Clean), its request/in-flight guard and its error codes — the plan surface cleared at the click and the Sync chained on completion, through the handover it shares with Clean | `App/ViewModels/RunViewModel.cs` (`OptimizeCommand`), `RunViewModel.Workspace.cs` |
| Hollow reset of rows and the will-build surface (repository change) | `App/ViewModels/RunViewModel.ActionBar.cs` (`ResetRowsToHollow`) |
| Emptying rows, graph and the will-build surface at a Clean or Optimize click and on a real repository change | `App/ViewModels/RunViewModel.ActionBar.cs` (`ClearPlanSurface`) |
| Step hold between an operation and the next (dispatcher timer, zero under reduced motion) | `App/Services/StepHold.cs`, `App/ViewModels/RunViewModel.cs` (`OperationHold`) |
| Branch popover and its base | `App/Views/BranchPopover.xaml(.cs)`, `PopoverBase.cs` |
| Branch popover row (virtualized item container) | `App/Views/BranchRow.cs` |
| Settings dialog (section rail + pages), layer/external-project drag-reorder | `App/Views/SettingsDialog.xaml(.cs)`, `App/Controls/DragReorderBehavior.cs` |
| Shared modal shell (scrim, frame, head/tabs/body/footer slots, rounded clip, host clamp, entrance, focus trap, Esc and scrim dismissal) | `App/Controls/ModalDialog.cs`, `DialogSize.cs`, `App/Resources/Controls.xaml` (`Ds.ModalDialog`) |
| About dialog (identity block, About / Environment / Shortcuts tabs, What's new hand-off) | `App/Views/AboutDialog.xaml(.cs)` |
| What's new dialog (release-note list, two-column version blocks, sticky identity column, version and installed chips) | `App/Views/NotesDialog.xaml(.cs)`, `App/Controls/StickyColumn.cs` |
| Product mark · company wordmark | `App/Controls/AppMark.xaml(.cs)`, `BrandLogo.xaml(.cs)` |
| Brand geometry and chevron gradient — one source, two consumers | `App/Resources/BrandGeometry.xaml` |
| Raster icon generation (.exe, taskbar, tray) | `App/Assets/generate-app-icons.ps1` |
| DS templates and styles | `App/Resources/Controls.xaml` |
| Clear button in search boxes · releasing a text box's focus on a click elsewhere | `App/Controls/DsChrome.cs` (`IsClearable`, `ClearTextCommand`), `App/Resources/Controls.xaml` (`Ds.Input`, `Ds.Input.Clear`), `App/Controls/ClickAwayBlur.cs` |
| Status glyph, spinner, status dot, split button, chips, tooltip, panel header, pill | `App/Controls/StatusGlyph.cs`, `BuildingSpinner.cs`, `StatusDot.cs`, `SplitButton.cs`, `DsChipFactory.cs`, `AppTooltip.cs`, `PanelHeader.xaml(.cs)`, `LatestPill.xaml(.cs)` |
| Visual status (the single colour channel) and its token table; the standing it is built on | `App/Controls/VisualStatus.cs`, `App/Controls/StandingStatus.cs` |
| Start-mode drawing constants (stripe/ring opacity, four-arc ring, cross-fade) | `App/Controls/StartMode.cs` |
| The caret's colour cycle (palette order, step, phase) | `App/Controls/CursorHop.cs` |
| App-wide tooltip defaults (no delay, no timeout, on disabled too) | `App/Controls/AppTooltipDefaults.cs` |
| Cycle wording: membership line, cycle path | `App/ViewModels/CycleText.cs` |
| Opening choreography: step timeline, wave tempo and order | `App/Controls/MarkingChoreography.cs` |
| Ending choreography: neon timings and keyframes, the moment the filter returns (`FilterReturnAtMs`) | `App/Controls/EndFinale.cs` |
| Choreography sequencer (one timer per choreography) | `App/Controls/StepPlayer.cs` |
| Choreography driver (rows + graph) | `App/Services/OperationChoreographer.cs` |
| Gate the run command waits on while the opening choreography plays | `App/ViewModels/RunViewModel.cs` (`OperationChoreography`), `MainWindow.xaml.cs` |
| Wave repaint of the graph (marking step + node colours in one push) | `MainWindow.xaml.cs` (`ApplyMarkingToGraph`) |
| Colour transition onto a token brush (the wave's amber) | `App/Controls/MotionTokens.cs` (`TransitionTokenBrush`) |
| Letter-spaced caps text | `App/Controls/TrackedTextBlock.cs`, `TrackedGlyphs.cs` |
| Icon geometries | `App/Resources/Icons.xaml`, `App/Controls/IconVisual.cs`, `IconPaint.cs` |

**Console**

| Behaviour | File |
|---|---|
| AvalonEdit host, batching, active line, cascade, chunk paging, cursor + row hover band, the hidden native caret | `App/Console/ConsoleView.xaml(.cs)` |
| Line colouring | `App/Console/ConsoleColorizer.cs`, `ConsolePalette.cs`, `ConsoleLine.cs` |
| Row hover band target-line geometry (pure) | `App/Console/ConsoleHoverBand.cs` |
| Typewriter timing for the active stream line (pure) | `App/Console/TypewriterScheduler.cs` |
| Batching, routing, render slice | `App/Console/ConsoleBatcher.cs`, `ConsoleBatchRouter.cs`, `ConsoleRenderSlice.cs` |
| Chunk stitch and scroll compensation | `App/Console/ChunkStitch.cs` |
| Header, empty states, copy log, timestamps | `App/Console/ConsoleHeader.xaml(.cs)`, `ConsoleEmptyState.cs`, `CopyLogFeedback.cs`, `ClipboardRetry.cs`, `WallClockFormat.cs` |

**Graph rendering**

| Behaviour | File |
|---|---|
| Node visuals, status tick, opening wave, hover, hidden-panel gate | `App/Graph/GraphView.xaml(.cs)`, `GraphNodeVisual.cs` |
| Shared list ↔ graph hover (the shared value, its wiring, the on-screen gate) | `App/Graph/GraphHoverEcho.cs`, `App/ViewModels/RunViewModel.cs` (`HoveredProjectId`), `App/Graph/GraphOverlay.cs` (`IsOnScreen`) |
| Graph node identity (project id, not name) and the label that is the name | `App/Graph/GraphModels.cs`, `QuietGraphLayout.cs` |
| Opening/ending choreography on the graph (marking opacity, neon flicker) | `App/Graph/GraphView.xaml.cs` (`SetMarking`/`PlayEndFinale`) |
| The filter set aside for a run and its return; the camera reset on rebuild and at the start of an operation | `App/Graph/GraphView.xaml.cs` (`BeginOperation`/`EndOperation`, `IsFilterSuspended`, `SnapCameraTo`), `MainWindow.xaml.cs` |
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
| Smooth scrolling, bottom anchor, follow mode | `App/Controls/ScrollAnimator.cs`, `BottomAnchorBehavior.cs`, `BottomAnchorDecision.cs`, `UserScrollSignal.cs`, `FollowScrollController.cs`, `FollowScrollDecision.cs` |
| Horizontal wheel / touchpad routing and step | `App/Controls/HorizontalWheelScroll.cs`, `App/Shell/Win32.cs` |
| Cross-panel scroll arbitration | `App/Services/ScrollArbiter.cs` |
| Reduced-motion signal and live zeroing | `App/Services/MotionSettings.cs`, `SystemParametersMotionSignal.cs`, `IMotionSettings.cs`, `IMotionSignal.cs` |
| One-hero budget | `App/Services/MotionCoordinator.cs`, `App/Controls/MotionGate.cs` |
| Shared entrance/reveal animations, 120 ms transitions | `App/Controls/PopIn.cs`, `RevealStagger.cs`, `DsTransition.cs`, `MotionTokens.cs`, `PillRadius.cs` |
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
