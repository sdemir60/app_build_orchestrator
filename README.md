# Build Orchestrator

![release](https://github.com/sdemir60/app_build_orchestrator/actions/workflows/release.yml/badge.svg?branch=main)

English | [Türkçe](README.tr.md)

A Windows desktop tool that builds a multi-project .NET solution the way you would by hand, only faster and safer.
It works out the dependency order from the repository itself, builds only the projects that changed and the ones
that depend on them, runs independent projects in parallel with the same MSBuild that Visual Studio uses, and lets
you follow the whole run live.

![Build Orchestrator during a build: the dependency graph, the project list, the console and the event stream](docs/images/building.png)

## Highlights

- **Builds only what changed.** The decision comes from source content on disk, dependents are included, every
  project says why it will build, and builds made in Visual Studio count.
- **Builds in order, in parallel.** The order is read from the repository itself; independent projects build at the
  same time, each in its own MSBuild.exe, and a failure does not stall the run.
- **Resolves dependency cycles.** Projects that reference each other build in rounds until their APIs settle.
- **Whole solution on one screen.** Graph, list, console and events follow the run live, and filters bring the failed
  projects forward in one click.
- **One click to the fix.** A row builds its project alone, opens its solution in Visual Studio or reveals it in
  Explorer.
- **Always at hand.** Starts with Windows, waits in the tray, and a global shortcut builds without bringing the window
  up.
- **Syncs on its own.** A commit, pull or branch switch refreshes the list within seconds; the next Build compiles only
  what differs.
- **Keeps the machine usable.** Performance profiles set how much of the machine a build may take, and the title bar
  shows what the tool costs right now.
- **Safe by design.** No orphaned MSBuild processes, no git writes uninvited, output exactly where VS puts it, and a
  stopped run never counts a half-built project as built.

Every feature, with how it works: [FEATURES.md](docs/FEATURES.md). What each one changes in a working day:
[WHY.md](docs/WHY.md).

## Screenshots

| Before a build: why each project will build | After it: the failure, and the projects built against it |
|---|---|
| ![Rows marked modified and affected after a commit, with the reason in a tooltip](docs/images/decisions.png) | ![One failed project and its flagged dependents, filtered to failures and warnings](docs/images/result.png) |
| **Resolve cycles: rounds until the cycle settles** | **Row actions: build, rebuild or clean one project** |
| ![Resolve cycles building a dependency cycle in rounds](docs/images/cycles.png) | ![The row menu of a project with Build, Rebuild and Clean](docs/images/row-actions.png) |
| **A failed project: its log, and its links on the graph** | **Built from Visual Studio: the result arrives as a notification** |
| ![The project page of a failed project, with its dependencies highlighted on the graph](docs/images/error-page.png) | ![A Windows notification reporting a finished build over the code editor](docs/images/tray.png) |

## Quick start

1. **Check the requirements.** Windows, Visual Studio 2022 or Build Tools with MSBuild, and git on `PATH`. The
   installer adds the .NET 10 Desktop Runtime when it is missing.
2. **Install.** Download the
   [installer](https://github.com/sdemir60/app_build_orchestrator/releases/latest/download/BuildOrchestrator.App-win-Setup.exe).
   It installs for the current user without admin rights and keeps itself up to date. It is not code-signed yet,
   so SmartScreen may ask once: choose **More info → Run anyway**.
3. **Point it at your repository.** On first launch, open Settings, enter the repository root and press
   **Save and sync**, then press **Build** (F5).

The full walk-through, every screen and every shortcut are in the [User guide](docs/USER-GUIDE.md).

## For developers

```powershell
dotnet build BuildOrchestrator.slnx
dotnet test  tests/BuildOrchestrator.Tests/BuildOrchestrator.Tests.csproj --filter "Category!=Acceptance"
dotnet run   --project src/BuildOrchestrator.App/BuildOrchestrator.App.csproj
```

Building needs the .NET 10 SDK band that `global.json` names. Close a running copy of the app first, because its
engine keeps its own binaries locked. Tests, CI, packaging and releases are described in
[ARCHITECTURE.md § Testing](ARCHITECTURE.md#17-testing) and
[§ Build, run, package, release](ARCHITECTURE.md#18-build-run-package-release); the working conventions for this
repository are in [CLAUDE.md](CLAUDE.md).

## Documentation

| Document | What it holds |
|---|---|
| [FEATURES.md](docs/FEATURES.md) | Every feature, most important first, with how it works |
| [WHY.md](docs/WHY.md) | What each feature changes compared with building by hand or by script |
| [User guide](docs/USER-GUIDE.md) | Installing and using the app, screen by screen, shortcuts, updates, known limits |
| [ARCHITECTURE.md](ARCHITECTURE.md) | The technical reference: processes, IPC, the incremental decision, UI, design system, code map |
| [CHANGELOG.md](CHANGELOG.md) | Release notes, also shown in the app's What's new |

FEATURES, WHY and this page also come in Turkish; the user guide and the technical reference are in English only.

## Licence

The project is licensed under the **MIT License** — see [`LICENSE`](LICENSE).

The one third-party licence *text* that is included and redistributed is the **Geist** and **Geist Mono**
fonts, licensed under the **SIL Open Font License 1.1**: `src/BuildOrchestrator.App/Assets/GEIST-LICENSE.txt`,
which is copied into the publish output as `Assets\GEIST-LICENSE.txt`.
