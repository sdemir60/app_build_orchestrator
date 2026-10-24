English | [Türkçe](WHY.tr.md)

# Why Build Orchestrator

What the tool changes in a working day, entry by entry, in the same order and under the same titles as
[FEATURES.md](FEATURES.md), where the mechanism behind each entry is described.

**In short, for the team:**

- **Less waiting.** A change builds what it touches, in parallel, and nothing more.
- **Fewer stale-DLL surprises.** What will build, and why, is on screen before Build is pressed, and a project
  built against a broken dependency is flagged.
- **No build script to maintain.** The order comes from the repository itself, cycles included.
- **A machine that stays usable.** A build takes only the share of the machine it is given.
- **Nothing touched behind your back.** Your branches and working tree change only on your click or through a
  setting you leave on, and output stays exactly where Visual Studio puts it.

Each entry gives the goal, the common practice and what happens in Build Orchestrator instead. The *Common
practice* lines describe how a multi-project .NET solution is usually built without a tool like this: by hand in
Visual Studio, with a home-made script, or with each developer's own routine. They are a general picture, not the
record of any particular team.

## At a glance

### Whole solution on one screen

**Goal:** have the whole solution in front of you, and turn waiting for a build into something you follow and
steer.\
**Common practice:** a large build is a console scrolling past; which projects built, which one broke and what is
left is pieced together from the text afterwards, and the wait is dead time spent somewhere else.\
**In Build Orchestrator:** every project is on screen with its state, and the run plays out across the list and the
graph as it happens: what is building glows, what finished turns green, what broke turns red in every panel at once.
You know where the build is without reading a line of log.\
`one window for graph, list, console and events` · `one selection across every panel`

### Maps dependencies on its own

**Goal:** never write down, or keep up to date, which project depends on which.\
**Common practice:** the build order lives in a script, a wiki page or people's memory; add a project or a
reference and the list is out of date, and the build breaks, or quietly picks up an old DLL, until someone notices.\
**In Build Orchestrator:** the order is read from the repository itself, every time: add a project or a reference,
and the next Sync already places it. There is nothing to maintain, and a DLL that two projects both produce is
named instead of guessed.\
`HintPath map for the real graph` · `raw XML discovery in seconds`

### Builds only what changed

**Goal:** a one-file change should cost a one-project build, not a coffee break.\
**Common practice:** you fix one class in a shared library, press Build and watch the whole chain compile, because
nobody can tell which projects really need it; or you build only the one you think matters, and an old DLL turns up
at run time.\
**In Build Orchestrator:** before you press anything, the list already says which projects will build and why;
Build takes those, skips the rest, and says so when there is nothing to build at all.\
`content hash of sources` · `build ledger across sessions`

### Builds in order, in parallel

**Goal:** finish the same build sooner, on the cores that would otherwise sit idle, without ever getting the order
wrong.\
**Common practice:** a script builds the solutions one after another in a fixed order; nothing starts until the
previous one ends, most cores wait, and one broken project in the middle stops everything behind it, related or
not.\
**In Build Orchestrator:** independent projects compile side by side, each one starting as soon as its dependencies
have finished and a worker is free. A broken project does not stop the build: what depends on it still compiles
against its last good output and is flagged amber, and the rest carries on.\
`ready-set scheduler for parallel builds` · `deterministic order for repeatable runs`

### Resolves dependency cycles

**Goal:** keep a solution with circular references buildable in one go, without a ritual.\
**Common practice:** when projects reference each other's DLLs, the same projects are built several times in an
order someone once worked out; a step gets skipped now and then, and it is never quite clear which project was
compiled against an old DLL.\
**In Build Orchestrator:** a cycle settles inside the same Build, or on its own with Resolve cycles when that is all
you need; a member whose inputs have not changed is not compiled again, and the result says plainly whether the
cycle settled or which project stopped it.\
`Tarjan SCC for cycle detection` · `API surface hash as proof of convergence` · `Resolve cycles for a cycle-only run`

### Keeps the machine usable

**Goal:** keep working while it builds.\
**Common practice:** a large solution built on every core at once can lock the machine up: the editor stutters, the
browser freezes and the call you are on breaks up, so long builds wait for lunch or for the end of the day.\
**In Build Orchestrator:** the build takes only the share of the machine you give it with one click on the chip,
and fits itself to the cores and free memory it finds; on the default profile the editor and your other apps stay usable,
and the title bar shows what the tool is using at that moment.\
`JOB_OBJECT_CPU_RATE_CONTROL hard cap to limit the machine share` · `worker budget by cores and free memory against paging` · `QueryInformationJobObject accounting for the live CPU figure`

### A native Windows app

**Goal:** a desktop tool that can stay open all day and keep a firm hand on every compiler it starts.\
**Common practice:** build helpers are usually command-line scripts, which can build in order but show no graph, no
reasons and no live state; a desktop tool built on web technology can show all of that, but brings a browser
runtime along and still needs native code to control the compilers it starts.\
**In Build Orchestrator:** the window stays responsive however much the build prints; the app waits in the tray,
answers a global shortcut, and when it closes, every compiler it started closes with it.\
`WPF on .NET 10, no browser runtime` · `Win32 P/Invoke for process control`

## In detail

### Says why each project builds

**Goal:** know why a project will build, or why it will not, before the build starts.\
**Common practice:** the IDE does not say why it rebuilds a project or skips it; in doubt, everything is cleaned and
rebuilt, or the build output is searched for a reason.\
**In Build Orchestrator:** every project carries a one-word reason next to its name, and red appears only for code
that really does not compile, so a red row is always worth your attention.\
`content fingerprint for modified vs affected` · `evidence-based red, no false alarms`

### Flags stale dependencies

**Goal:** see at once which projects were built against a broken dependency, without rebuilding them for nothing.\
**Common practice:** when a library fails, the projects above it keep building against its old DLL; the
application seems to run and the problem surfaces much later, or the whole chain is rebuilt on every attempt to be
safe.\
**In Build Orchestrator:** the projects waiting behind a broken dependency carry an amber mark you see at a glance,
and they join the next Build on their own once it is fixed.\
`root-cause note for targeted rebuilds` · `one amber warning channel`

### Skips when the API holds

**Goal:** a small fix deep in a shared library should not rebuild everything above it.\
**Common practice:** a one-line fix in a shared library rebuilds every project that depends on it; in a layered
solution, that single change becomes a long wait.\
**In Build Orchestrator:** for an internal change only the library itself builds and the projects above it stay
green; when a public signature changes, they build too.\
`API surface hash of compiled output` · `cascade cut for shared libraries`

### Works alongside Visual Studio

**Goal:** keep building in Visual Studio when you like, without that work being thrown away.\
**Common practice:** a separate build tool rebuilds what the IDE has already built, so developers end up picking one
and avoiding the other.\
**In Build Orchestrator:** projects you built in Visual Studio show green and are skipped; they build again only
when a dependency changed that Visual Studio did not see.\
`foreign output detection` · `upstream cascade to catch stale copies`

### Builds the way VS builds

**Goal:** get the result Visual Studio would give.\
**Common practice:** scripts that call a different build command or pass different arguments produce "builds in VS,
fails in the script" differences, more often with older .NET Framework projects.\
**In Build Orchestrator:** every project is compiled by the MSBuild.exe Visual Studio uses, into the same folders,
and old-style packages.config projects are restored on the way, with no extra package tool on the machine.\
`vswhere to find MSBuild` · `one MSBuild.exe process per project`

### Leaves no orphaned processes

**Goal:** when a build stops or the tool closes, no compiler keeps running behind it.\
**Common practice:** after an interrupted build, leftover compiler processes keep files locked; they are hunted
down in Task Manager, or the machine is restarted.\
**In Build Orchestrator:** however the app closes, even in a crash, its compilers close with it within seconds, and
the next build does not trip over a locked file.\
`nested Job Objects against orphans` · `kill-on-close for cascading shutdown`

### Stops safely, then continues

**Goal:** stop a build at any moment without leaving broken output, and carry on from there later.\
**Common practice:** cutting a long build short leaves half-written outputs and no clear idea of which projects
finished, so everything is rebuilt afterwards to be safe.\
**In Build Orchestrator:** Stop lets the projects in progress finish cleanly, and pressing Build again compiles only
what did not finish.\
`drain at the project boundary for consistent output` · `record invalidation for a correct restart`

### Recovers interrupted work

**Goal:** no doubt about what was built after a crash, a forced sign-out or a killed process.\
**Common practice:** if the machine hangs or the build is killed half-way, half-written outputs remain, and the
only safe move is to clean and rebuild everything.\
**In Build Orchestrator:** on the next start the console says which projects will rebuild, and only those do.\
`in-flight ledger to find half-built output` · `atomic writes against corrupt files`

### Leaves output folders alone

**Goal:** adopt the tool without changing where anything is built or deployed.\
**Common practice:** some build scripts redirect or copy output into a folder of their own; when the IDE and the
script write to different places, nobody is sure which DLL is current.\
**In Build Orchestrator:** output is always where Visual Studio puts it; starting or stopping to use the tool
changes nothing in the output layout, so you can try it and stop at any time with nothing to undo.\
`untouched output paths, same layout as VS` · `read-only use of outputs`

### Never writes to git uninvited

**Goal:** a build tool that never touches your history or your uncommitted work.\
**Common practice:** scripts that pull or reset before building, to be up to date, now and then overwrite local
changes or leave unexpected merges behind, so teams hesitate to put git steps in a build script at all.\
**In Build Orchestrator:** your branch moves only when you click a chip, and only in the safe way; Build and Sync
never touch your working tree, and external copies are fast-forwarded before a Build only while Pull before build
is on.\
`single git writer for auditability` · `fast-forward only, history kept`

### Filters by status or name

**Goal:** reach the failed projects, the ones building, or one by name, in a single click.\
**Common practice:** in a long build output, finding which projects failed means scrolling or searching the text,
and telling what has not been built yet means comparing by hand.\
**In Build Orchestrator:** one click on a counter shows only the failed or only the up-to-date projects, and typing
a few letters finds a project by name; the graph dims everything else.\
`counter chips as one-click filters` · `name filter to find a project fast`

### Row actions: build, open, reveal

**Goal:** go from a red row to its code, or to a build of that one project, in one click.\
**Common practice:** you work out which solution the project lives in, open it in the IDE, find the project in
Solution Explorer and build it; or you hunt for its folder on disk.\
**In Build Orchestrator:** the button on the row builds that project alone, without waiting for a whole run, and the
same row opens its solution in Visual Studio or its folder in Explorer.\
`single-project runs for quick tries` · `solution map to open the right .sln`

### Shows the graph, live

**Goal:** answer "what does this project use, and what uses it?" in one click.\
**Common practice:** finding what a project depends on, and what depends on it, means opening project files one by
one or asking whoever has been on the team longest.\
**In Build Orchestrator:** click a project and its direct dependencies and dependents come forward, with the links
drawn, and the projects caught in a cycle stand out.\
`focus-and-fit to inspect dependencies` · `always-fit layout, no scrollbars`

### Shows errors, not noise

**Goal:** get to the error that broke the build without wading through warnings.\
**Common practice:** the build output of a large solution runs to page after page of warnings; the one error is
found by scrolling, searching or pasting the output into an editor.\
**In Build Orchestrator:** the console shows errors in full and warnings as a short summary; click the red project
and its log opens from the top, with the first error in front of you.\
`Compact console to cut warning noise` · `project page to read an error from the top`

### Says where the build stands

**Goal:** know at any moment what the build is doing and how long is left.\
**Common practice:** a command-line build prints a stream of text, and how far it has got, or how long it will
take, is guessed from the scrolling.\
**In Build Orchestrator:** one line at the top always gives the phase, the progress and the time left; chips there
name the projects building now and, once the run ends, the failed ones, one click away.\
`remaining-time estimate` · `failure chips, one click to the failed`

### Shortcuts that work from anywhere

**Goal:** start a build without leaving the editor.\
**Common practice:** with a separate build tool or script, every build means switching from the editor to that
window, running the command and switching back.\
**In Build Orchestrator:** while you work in Visual Studio, one shortcut starts a build in the background; there is
no window to switch to, and the result arrives as a notification.\
`global hotkeys to build from the background` · `F5/F6/F7 like Visual Studio`

### Lives in the tray

**Goal:** have the build tool ready all day without it taking a window or your attention.\
**Common practice:** a build script is started from a terminal every time, and finding out whether it finished
means going back to that terminal to look.\
**In Build Orchestrator:** the app can start with Windows and wait in the tray; a small mark in the screen corner
shows a build is running, and a notification tells you how it ended.\
`tray icon to work in the background` · `Windows notification for the result`

### Syncs on its own

**Goal:** keep "what will build" in step with the repository at all times, without asking.\
**Common practice:** after switching branches or pulling, it is not clear which projects are affected, so either
everything is rebuilt or only the ones that come to mind.\
**In Build Orchestrator:** after a commit, a pull or a branch switch in Visual Studio or a terminal, the list updates
itself within seconds; the Sync button is rarely needed.\
`HEAD watch so Sync is rarely pressed` · `quiet Sync that keeps the screen`

### External projects, one graph

**Goal:** build shared libraries kept in other repositories together with the main solution, in the right order.\
**Common practice:** when common libraries live in a separate repository, that repository is updated and built
first and the main solution second; forget a step, and you work against an old library without knowing it.\
**In Build Orchestrator:** projects from other repositories appear at the top of the same list and build first,
updated beforehand if you leave that on; if they hold uncommitted work, the tool stops instead of updating them.\
`multi-root scan into one graph` · `fast-forward update before each Build`

### Repairs a broken workspace

**Goal:** get a workspace that "doesn't build here" building again in a click, without cloning it again.\
**Common practice:** after a branch switch, a .NET version change or a half-finished package download, the build
fails with errors that point nowhere near the cause, such as another framework's leftovers in obj or packages missing
from disk; bin and obj are deleted by hand, packages are fetched again, and sometimes the repository is cloned from
scratch.\
**In Build Orchestrator:** Deep Clean wipes every project's bin and obj folders in one click, and Optimize brings
back the missing packages and names what is still broken; the list refreshes on its own and the next Build starts
clean. NuGet leftovers from another framework in obj are flagged at run start, before they cost you an afternoon.\
`bin/obj wipe for a clean workspace` · `NuGet restore to fix "doesn't build here"` · `run-start warning, nothing deleted`

### Light idle, smooth under load

**Goal:** a tool that stays open all day without weighing on the machine.\
**Common practice:** developer tools left open in the background slowly hold on to memory and a constant trickle
of CPU, so they are opened when needed and closed afterwards.\
**In Build Orchestrator:** the app can stay in the tray: with no work it uses next to no CPU, hands most of its
memory back and is ready for the next build in a few seconds.\
`idle engine sleep to save memory` · `visibility-bound animation, near-zero idle CPU`

### Calm, accessible design

**Goal:** a screen that reads without telling red from green, follows the Windows motion setting and works with a
screen reader.\
**Common practice:** many tools tell success from failure by red and green alone, which not everyone can tell apart,
and their animations run whatever the system's motion setting says.\
**In Build Orchestrator:** every state is said with a colour, a symbol and a word, so it reads without the colour;
motion follows the Windows animation setting, and a screen reader gets a name and a real role for every control.\
`OS reduced-motion signal for accessible motion` · `UI Automation names for screen readers`
