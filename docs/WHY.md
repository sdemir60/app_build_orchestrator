English | [Türkçe](WHY.tr.md)

# Why Build Orchestrator

What each part of the tool is for and what it changes in a working day, in the same order as
[FEATURES.md](FEATURES.md), where the mechanism behind each entry is described. Every entry gives the goal, the
common practice and what happens in Build Orchestrator instead.

The *Common practice* lines describe how a multi-project .NET solution is usually built without a tool like this:
by hand in Visual Studio, with a home-made script, or with each developer's own routine. They are a general
picture, not the record of any particular team.

## Builds only what changed

**Goal:** wait only for the part that actually changed.\
**Common practice:** in a multi-project solution it is hard to see which projects a change touches, so the whole
solution or a large part of it is rebuilt; or a project is left out and an old DLL turns up at run time.\
**In Build Orchestrator:** Build compiles only the changed projects and the ones that use them; when nothing has
changed, the run ends almost at once with nothing to build.\
`content hash of sources` · `build ledger across sessions`

## Dependency order, in parallel

**Goal:** finish the same work sooner, in the right order, with the cores that would otherwise sit idle.\
**Common practice:** projects are built one at a time, in an order kept in someone's head or in a script; nothing
starts before the previous one ends, most cores wait, and a wrong order shows up only on the next attempt.\
**In Build Orchestrator:** the order comes from the repository every time; projects that do not depend on each
other build at the same time, and one broken project does not stop the unrelated ones.\
`ready-set scheduler for parallel builds` · `HintPath map for the real graph`

## Resolves dependency cycles

**Goal:** keep solutions with circular references buildable, quickly and without a ritual.\
**Common practice:** when projects reference each other's DLLs, the same projects are built several times in a
particular order, and it is never quite clear which one was compiled against an old DLL; the order becomes team
folklore.\
**In Build Orchestrator:** the projects in a cycle settle within one Build, or on their own with Resolve cycles, in
as few compiles as it takes: a member whose inputs have not changed is not compiled again, and the result says
plainly whether the cycle settled or which project stopped it.\
`Tarjan SCC for cycle detection` · `API surface hash as proof of convergence` · `Resolve cycles for a cycle-only run`

## Says why each project builds

**Goal:** answer "why is it building this?" before the build starts.\
**Common practice:** the IDE does not say why a project is rebuilt or skipped; in doubt, everything is cleaned and
rebuilt, or the output is searched for a reason.\
**In Build Orchestrator:** every project carries a one-word reason next to its name, and red appears only for code
that really does not compile.\
`content fingerprint for modified vs affected` · `evidence-based red, no false alarms`

## Flags stale dependencies

**Goal:** make a project built against a broken dependency visible, and stop rebuilding it for nothing.\
**Common practice:** when a library fails, the projects above it keep building against its old DLL, the
application seems to run and the problem surfaces late; or the whole chain is rebuilt on every attempt.\
**In Build Orchestrator:** the projects waiting behind a broken dependency are visible at a glance, and they join
the next Build on their own once it is fixed.\
`root-cause note for targeted rebuilds` · `one amber warning channel`

## Skips when the API holds

**Goal:** stop a small internal change in a low-level library from rebuilding everything above it.\
**Common practice:** a one-line fix in a shared library rebuilds every project that depends on it; in a layered
solution that single change becomes a long wait.\
**In Build Orchestrator:** for an internal change only the library itself builds and the projects above it stay
green; when a public signature changes, they build too.\
`API surface hash of compiled output` · `cascade cut for shared libraries`

## Works alongside Visual Studio

**Goal:** count the builds made in the IDE instead of throwing them away.\
**Common practice:** a separate build tool rebuilds what the IDE has already built, so developers end up choosing
one or the other.\
**In Build Orchestrator:** projects built in Visual Studio show green and are skipped; they build again only when a
dependency changed that Visual Studio did not see.\
`foreign output detection` · `upstream cascade to catch stale copies`

## Builds the way VS builds

**Goal:** get the same result Visual Studio would.\
**Common practice:** scripts that use a different build command or different arguments produce "builds in VS,
fails in the script" differences, more often with older .NET Framework projects.\
**In Build Orchestrator:** projects build the way Visual Studio builds them, and missing packages are restored on
the way, with no extra package tool on the machine.\
`vswhere to find MSBuild` · `one MSBuild.exe process per project`

## Leaves no orphaned processes

**Goal:** make sure no compiler keeps running once a build stops or the tool closes.\
**Common practice:** after an interrupted build, leftover compiler processes keep files locked, and they are
hunted down in Task Manager or the machine is restarted.\
**In Build Orchestrator:** however the app closes, the compilers close with it within seconds, and the next build
does not trip over a locked file.\
`nested Job Objects against orphans` · `kill-on-close for cascading shutdown`

## Stops safely, then continues

**Goal:** stop a build at any moment without leaving broken output, and carry on from there.\
**Common practice:** cutting a long build short leaves half-written outputs and no clear idea of which projects
finished, so everything is rebuilt afterwards to be safe.\
**In Build Orchestrator:** Stop lets the projects in progress finish cleanly, and pressing Build again compiles
only what did not finish.\
`drain at the project boundary for consistent output` · `record invalidation for a correct restart`

## Recovers interrupted work

**Goal:** remove the doubt about what was built after an unexpected interruption.\
**Common practice:** if the machine hangs or the build is killed half-way, half-written outputs remain, and the
safe move is to clean and rebuild everything.\
**In Build Orchestrator:** on the next start the console says which projects will rebuild, and only those do.\
`in-flight ledger to find half-built output` · `atomic writes against corrupt files`

## Leaves output folders alone

**Goal:** adopt the tool without changing where anything is built or deployed.\
**Common practice:** some build scripts redirect or copy output into a folder of their own; when the IDE and the
script write to different places, it is unclear which DLL is current.\
**In Build Orchestrator:** output is always where Visual Studio puts it; starting or stopping to use the tool
changes nothing in the output layout.\
`untouched output paths, same layout as VS` · `read-only use of outputs`

## Never writes to git uninvited

**Goal:** a build tool that never touches your history or your uncommitted work.\
**Common practice:** scripts that pull or reset before building, to be up to date, now and then overwrite local
changes or leave unexpected merges behind, so teams hesitate to put git steps in a build script.\
**In Build Orchestrator:** the repository is written only when you click a chip, and only in the safe way; Build
and Sync only read it.\
`single git writer for auditability` · `fast-forward only, history kept`

## Filters by status or name

**Goal:** reach the failed projects, the ones building or one by name in a single click.\
**Common practice:** in a long build output, finding which projects failed means scrolling or searching the text,
and telling what has not been built yet means comparing by hand.\
**In Build Orchestrator:** one click on a counter shows only the failed or only the up-to-date projects, and typing
a few letters finds a project by name.\
`counter chips as one-click filters` · `name filter to find a project fast`

## Row actions: build, open, reveal

**Goal:** build one project without waiting for a whole run, and get to its code in one click.\
**Common practice:** to try a single project, the right solution is opened in the IDE and the project is found in
Solution Explorer and built; when it belongs to several solutions, the first step is remembering which one.\
**In Build Orchestrator:** the button next to a project builds that project alone, and the same row opens its
solution in Visual Studio or its folder in Explorer.\
`single-project runs for quick tries` · `solution map to open the right .sln`

## The dependency graph

**Goal:** see at a glance how hundreds of projects depend on each other, and where a build stands.\
**Common practice:** finding out what a project depends on and what it affects means opening project files one by
one, or asking whoever has been on the team longest.\
**In Build Orchestrator:** the whole solution stays in view: clicking a project brings forward what it uses and what
uses it, and during and after a build the graph shows which projects are compiling, which have failed and which sit
in a cycle.\
`focus-and-fit to inspect dependencies` · `opacity story to follow the run`

## Console and project logs

**Goal:** find a build error without wading through hundreds of warning lines.\
**Common practice:** the build output of a large solution holds thousands of warning lines; the error is found by
scrolling, searching or pasting the output into a file.\
**In Build Orchestrator:** the console shows errors in full and warnings as a summary, and clicking a project opens
its log from the top with the first error in front of you.\
`Compact console to cut warning noise` · `project page to read an error from the top`

## Status ribbon and event stream

**Goal:** know at any moment what the build is doing and how long is left.\
**Common practice:** a console build prints a stream of text, and how far it has got or how long it will take is
guessed from the scrolling.\
**In Build Orchestrator:** a line at the top always gives the phase, the progress and the time left; chips there
name the projects building now and, once the run ends, the failed ones, one click away.\
`remaining-time estimate` · `failure chips, one click to the failed`

## Shortcuts that work from anywhere

**Goal:** start a build without leaving the editor.\
**Common practice:** with a separate build tool or script, every build means switching from the editor to that
window, running the command and switching back.\
**In Build Orchestrator:** while you work in Visual Studio, one shortcut starts a build in the background; there is
no window to switch to, and the result arrives as a notification.\
`global hotkeys to build from the background` · `F5/F6/F7 like Visual Studio`

## Tray, startup and notifications

**Goal:** have the build tool ready all day without it getting in the way.\
**Common practice:** a build script is started from a terminal every time, and finding out whether it finished
means going back to that terminal.\
**In Build Orchestrator:** the app can start with Windows and wait in the tray; a small mark in the screen corner
shows a build is running, and a notification reports the result.\
`tray icon to work in the background` · `Windows notification for the result`

## Syncs on its own

**Goal:** keep "what will build" in step with the repository at all times.\
**Common practice:** after switching branches or pulling, it is not clear which projects are affected, so either
everything is rebuilt or only the ones that come to mind.\
**In Build Orchestrator:** after a commit or a branch switch in Visual Studio or a terminal, the list updates itself
within seconds; the Sync button is rarely needed.\
`HEAD watch so Sync is rarely pressed` · `quiet Sync that keeps the screen`

## External projects, one graph

**Goal:** build shared libraries kept in other repositories together with the main solution, in the right order.\
**Common practice:** when common libraries live in a separate repository, that repository is updated and built
first and the main solution second; when a step is forgotten, work goes on against an old library.\
**In Build Orchestrator:** projects from other repositories appear at the top of the same list and are updated and
built first; if they hold uncommitted work, the tool stops instead of updating them.\
`multi-root scan into one graph` · `fast-forward update before each Build`

## Maintenance: Deep Clean, Optimize, cycles

**Goal:** fix "it doesn't build here" in one click.\
**Common practice:** after a fresh clone, a half-finished package download or mixed .NET versions, builds fail in
puzzling ways; bin and obj folders are deleted by hand, packages are downloaded again, and sometimes the repository
is cloned from scratch.\
**In Build Orchestrator:** one button resets the workspace, another brings the missing packages and names the broken
references, and the list refreshes on its own afterwards.\
`bin/obj wipe for a clean workspace` · `NuGet restore to fix "doesn't build here"`

## Performance profiles, live cost

**Goal:** keep the machine usable while it builds.\
**Common practice:** a large solution built on every core at once can lock the machine up: the editor, the browser
and the meeting app stutter or freeze, so long builds wait for a break.\
**In Build Orchestrator:** parallelism fits itself to the machine's cores and free memory, and how much of the
machine a build may take is one click away; on the default profile the window and other apps stay smooth, and what
the build uses is shown in the title bar.\
`Job Object CPU cap to limit the machine share` · `worker budget against paging` · `job accounting for the live strip`

## Light idle, smooth under load

**Goal:** a tool that stays open all day without weighing on the machine.\
**Common practice:** developer tools left open in the background slowly hold hundreds of megabytes and a constant
trickle of CPU, so they are opened when needed and closed afterwards.\
**In Build Orchestrator:** the app can stay in the tray: with no work it uses next to no CPU, hands most of its
memory back and is ready for the next build in a few seconds.\
`idle engine sleep to save memory` · `visibility-bound animation, near-zero idle CPU`

## Calm, accessible design

**Goal:** make following a build pleasant instead of a wall of console text.\
**Common practice:** a build is followed in a scrolling console where nothing stands out until the end, and the
result has to be read out of the text.\
**In Build Orchestrator:** each project's state is shown with a colour, a symbol and a word, the run plays out
calmly across the list and the graph, and the interface follows the Windows animation and accessibility settings.\
`design tokens for a consistent look` · `OS reduced-motion signal for accessible motion`
