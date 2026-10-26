English | [Türkçe](WHY.tr.md)

# Why Build Orchestrator

What the tool changes in a working day, most important first. Each heading names a feature and, after the dash, what
it gives you; the entries follow [FEATURES.md](FEATURES.md), where the mechanism behind each one is described. Under
every heading: how this usually goes in a multi-project .NET solution without such a tool — by hand in Visual Studio,
with a home-made script, or with each developer's own routine; a general picture, not the record of any particular
team — and what happens in Build Orchestrator instead. Building a large solution is usually something you wait out;
here it is something you watch.

## Builds only what changed — less waiting

**Common practice:** you fix one class in a shared library, press Build and watch the whole chain compile, because
nobody can tell which projects really need it; or you build only the one you think matters, and an old DLL turns up
at run time.\
**In Build Orchestrator:** before you press anything, the list already says which projects will build and why;
Build takes those, skips the rest, and says so when there is nothing to build at all. A one-file change costs the
projects it really touches, not a coffee break.\
`content hash of sources` · `build ledger across sessions`

## Builds in order, in parallel — the same build, sooner

**Common practice:** a script builds the solutions one after another in a fixed order; nothing starts until the
previous one ends, most cores wait, and one broken project in the middle stops everything behind it, related or
not.\
**In Build Orchestrator:** independent projects compile side by side on the cores that would otherwise sit idle,
each one starting as soon as its dependencies have finished and a worker is free. A broken project does not stop the
build: what depends on it still compiles against its last good output and is flagged amber, and the rest carries on.\
`ready-set scheduler for parallel builds` · `deterministic order for repeatable runs`

## Resolves dependency cycles — no manual rounds

**Common practice:** when projects reference each other's DLLs, the same projects are built several times in an
order someone once worked out; a step gets skipped now and then, and it is never quite clear which project was
compiled against an old DLL.\
**In Build Orchestrator:** a cycle settles inside the same Build, or on its own with Resolve cycles when that is all
you need; a member whose inputs have not changed is not compiled again, and the result says plainly whether the
cycle settled or which project stopped it.\
`Tarjan SCC for cycle detection` · `API surface hash as proof of convergence` · `Resolve cycles for a cycle-only run`

## Maps dependencies on its own — no build script to maintain

**Common practice:** the build order lives in a script, a wiki page or people's memory; add a project or a
reference and the list is out of date, and the build breaks, or quietly picks up an old DLL, until someone notices.\
**In Build Orchestrator:** the order is read from the repository itself, every time: add a project or a reference,
and the next Sync already places it. There is nothing to maintain, and a DLL that two projects both produce is
named instead of guessed.\
`HintPath map for the real graph` · `raw XML discovery in seconds`

## Whole solution on one screen — command of every project

**Common practice:** a large build is a console scrolling past; which projects built, which one broke and what is
left is pieced together from the text afterwards, and the wait is dead time spent somewhere else.\
**In Build Orchestrator:** every project is on screen with its state, and the run plays out across the list and the
graph as it happens: what is building glows, what finished turns green, what broke turns red in every panel at once.
You know where the build is without reading a line of log, and waiting for a build turns into something you follow
and steer.\
`one window for graph, list, console and events` · `one selection across every panel`

## Row actions: build, open, reveal — one click to the fix

**Common practice:** you work out which solution the project lives in, open it in the IDE, find the project in
Solution Explorer and build it; or you hunt for its folder on disk.\
**In Build Orchestrator:** the button on the row builds that project alone, without waiting for a whole run, and the
same row opens its solution in Visual Studio or its folder in Explorer.\
`single-project runs for quick tries` · `solution map to open the right .sln`

## Always at hand — a build is one key away

**Common practice:** a build script is started from a terminal every time, and finding out whether it finished
means going back to that terminal to look; a separate build tool means switching from the editor to its window,
running the command and switching back.\
**In Build Orchestrator:** the app can start with Windows and wait in the tray; while you work in Visual Studio, one
shortcut starts a build in the background and another brings the window up when you want to look. A small mark in
the screen corner shows a build is running, and a notification tells you how it ended.\
`global hotkeys to build from the background` · `tray icon to work in the background`

## Syncs on its own — switch branches, build the difference

**Common practice:** after switching branches or pulling, it is not clear which projects are affected, so either
everything is rebuilt or only the ones that come to mind.\
**In Build Orchestrator:** after a commit, a pull or a branch switch in Visual Studio or a terminal, the list updates
itself within seconds and the next Build compiles only what differs; the Sync button is rarely needed.\
`HEAD watch so Sync is rarely pressed` · `quiet Sync that keeps the screen`

## Keeps the machine usable — keep working while it builds

**Common practice:** a large solution built on every core at once can lock the machine up: the editor stutters, the
browser freezes and the call you are on breaks up, so long builds wait for lunch or for the end of the day.\
**In Build Orchestrator:** the build takes only the share of the machine you give it with one click on the chip,
and fits itself to the cores and free memory it finds; on the default profile the editor and your other apps stay
usable, and the title bar shows what the tool is using at that moment.\
`JOB_OBJECT_CPU_RATE_CONTROL hard cap to limit the machine share` · `worker budget by cores and free memory against paging` · `QueryInformationJobObject accounting for the live CPU figure`

## Calm, accessible design — pleasant to watch, easy to read

**Common practice:** a build is followed in a scrolling console where nothing stands out until the end; many tools
tell success from failure by red and green alone, which not everyone can tell apart, and their animations run
whatever the system's motion setting says.\
**In Build Orchestrator:** every state is said with a colour, a symbol and a word, so it reads without the colour,
and the run plays out calmly across the list and the graph; motion follows the Windows animation setting, and a
screen reader gets a name and a real role for every control. Watching a build stops being a chore.\
`design tokens for a consistent look` · `OS reduced-motion signal for accessible motion` · `UI Automation names for screen readers`

## A native Windows app — responsive, compilers under control

**Common practice:** build helpers are usually command-line scripts, which can build in order but show no graph, no
reasons and no live state; a desktop tool built on web technology can show all of that, but brings a browser
runtime along and still needs native code to control the compilers it starts.\
**In Build Orchestrator:** the window stays responsive however much the build prints; the app waits in the tray,
answers a global shortcut, and when it closes, every compiler it started closes with it.\
`WPF on .NET 10, no browser runtime` · `Win32 P/Invoke for process control`

## Light idle, smooth under load — leave it open all day

**Common practice:** developer tools left open in the background slowly hold on to memory and a constant trickle
of CPU, so they are opened when needed and closed afterwards.\
**In Build Orchestrator:** the app can stay in the tray: with no work it uses next to no CPU, hands most of its
memory back and is ready for the next build in a few seconds.\
`idle engine sleep to save memory` · `visibility-bound animation, near-zero idle CPU`

## Says why each project builds — no guessing

**Common practice:** the IDE does not say why it rebuilds a project or skips it; in doubt, everything is cleaned and
rebuilt, or the build output is searched for a reason.\
**In Build Orchestrator:** every project carries a one-word reason next to its name before the build starts, and
red appears only for code that really does not compile, so a red row is always worth your attention.\
`content fingerprint for modified vs affected` · `evidence-based red, no false alarms`

## Flags stale dependencies — stale DLLs made visible

**Common practice:** when a library fails, the projects above it keep building against its old DLL; the
application seems to run and the problem surfaces much later, or the whole chain is rebuilt on every attempt to be
safe.\
**In Build Orchestrator:** the projects waiting behind a broken dependency carry an amber mark you see at a glance,
and they join the next Build on their own once it is fixed.\
`root-cause note for targeted rebuilds` · `one amber warning channel`

## Skips when the API holds — cheap fixes in shared code

**Common practice:** a one-line fix in a shared library rebuilds every project that depends on it; in a layered
solution, that single change becomes a long wait.\
**In Build Orchestrator:** for an internal change only the library itself builds and the projects above it stay
green; when a public signature changes, they build too.\
`API surface hash of compiled output` · `cascade cut for shared libraries`

## Works alongside Visual Studio — IDE builds count

**Common practice:** a separate build tool rebuilds what the IDE has already built, so developers end up picking one
and avoiding the other.\
**In Build Orchestrator:** projects you built in Visual Studio show green and are skipped; they build again only
when a dependency changed that Visual Studio did not see.\
`foreign output detection` · `upstream cascade to catch stale copies`

## Builds the way VS builds — same MSBuild, same folders

**Common practice:** scripts that call a different build command or pass different arguments produce "builds in VS,
fails in the script" differences, more often with older .NET Framework projects.\
**In Build Orchestrator:** every project is compiled by the MSBuild.exe Visual Studio uses, into the same folders,
and old-style packages.config projects are restored on the way, with no extra package tool on the machine.\
`vswhere to find MSBuild` · `one MSBuild.exe process per project`

## Leaves no orphaned processes — no locked files left behind

**Common practice:** after an interrupted build, leftover compiler processes keep files locked; they are hunted
down in Task Manager, or the machine is restarted.\
**In Build Orchestrator:** however the app closes, even in a crash, its compilers close with it within seconds, and
the next build does not trip over a locked file.\
`nested Job Objects against orphans` · `kill-on-close for cascading shutdown`

## Stops safely, then continues — stop at any moment

**Common practice:** cutting a long build short leaves half-written outputs and no clear idea of which projects
finished, so everything is rebuilt afterwards to be safe.\
**In Build Orchestrator:** Stop lets the projects in progress finish cleanly, and pressing Build again compiles only
what did not finish.\
`drain at the project boundary for consistent output` · `record invalidation for a correct restart`

## Recovers interrupted work — crashes cost no rebuild

**Common practice:** if the machine hangs or the build is killed half-way, half-written outputs remain, and the
only safe move is to clean and rebuild everything.\
**In Build Orchestrator:** on the next start the console says which projects will rebuild, and only those do.\
`in-flight ledger to find half-built output` · `atomic writes against corrupt files`

## Leaves output folders alone — adopt without changing anything

**Common practice:** some build scripts redirect or copy output into a folder of their own; when the IDE and the
script write to different places, nobody is sure which DLL is current.\
**In Build Orchestrator:** output is always where Visual Studio puts it; starting or stopping to use the tool
changes nothing in the output layout, so you can try it and stop at any time with nothing to undo.\
`untouched output paths, same layout as VS` · `read-only use of outputs`

## Never writes to git uninvited — your branch stays yours

**Common practice:** scripts that pull or reset before building, to be up to date, now and then overwrite local
changes or leave unexpected merges behind, so teams hesitate to put git steps in a build script at all.\
**In Build Orchestrator:** your branch moves only when you click a chip, and only in the safe way; Build and Sync
never touch your working tree, and external copies are fast-forwarded before a Build only while Pull before build
is on.\
`single git writer for auditability` · `fast-forward only, history kept`

## Filters by status or name — the failed ones in one click

**Common practice:** in a long build output, finding which projects failed means scrolling or searching the text,
and telling what has not been built yet means comparing by hand.\
**In Build Orchestrator:** one click on a counter shows only the failed or only the up-to-date projects, and typing
a few letters finds a project by name; the graph dims everything else.\
`counter chips as one-click filters` · `name filter to find a project fast`

## Shows the graph, live — dependencies in one click

**Common practice:** finding what a project depends on, and what depends on it, means opening project files one by
one or asking whoever has been on the team longest.\
**In Build Orchestrator:** click a project and its direct dependencies and dependents come forward, with the links
drawn, and the projects caught in a cycle stand out.\
`focus-and-fit to inspect dependencies` · `always-fit layout, no scrollbars`

## Shows errors, not noise — no searching the log

**Common practice:** the build output of a large solution runs to page after page of warnings; the one error is
found by scrolling, searching or pasting the output into an editor.\
**In Build Orchestrator:** the console shows errors in full and warnings as a short summary; click the red project
and its log opens from the top, with the first error in front of you.\
`Compact console to cut warning noise` · `project page to read an error from the top`

## Says where the build stands — time left, always visible

**Common practice:** a command-line build prints a stream of text, and how far it has got, or how long it will
take, is guessed from the scrolling.\
**In Build Orchestrator:** one line at the top always gives the phase, the progress and the time left; chips there
name the projects building now and, once the run ends, the failed ones, one click away.\
`remaining-time estimate` · `failure chips, one click to the failed`

## External projects, one graph — shared repositories, right order

**Common practice:** when common libraries live in a separate repository, that repository is updated and built
first and the main solution second; forget a step, and you work against an old library without knowing it.\
**In Build Orchestrator:** projects from other repositories appear at the top of the same list and build first,
updated beforehand if you leave that on; if they hold uncommitted work, the tool stops instead of updating them.\
`multi-root scan into one graph` · `fast-forward update before each Build`

## Repairs a broken workspace — no re-clone

**Common practice:** after a branch switch, a .NET version change or a half-finished package download, the build
fails with errors that point nowhere near the cause, such as another framework's leftovers in obj or packages missing
from disk; bin and obj are deleted by hand, packages are fetched again, and sometimes the repository is cloned from
scratch.\
**In Build Orchestrator:** Deep Clean wipes every project's bin and obj folders in one click, and Optimize brings
back the missing packages and names what is still broken; the list refreshes on its own and the next Build starts
clean. NuGet leftovers from another framework in obj are flagged at run start, before they cost you an afternoon.\
`bin/obj wipe for a clean workspace` · `NuGet restore to fix "doesn't build here"` · `run-start warning, nothing deleted`
