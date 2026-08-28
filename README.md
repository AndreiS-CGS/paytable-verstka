# paytable-verstka

The CGS/Konami slot-paytable pipeline: three Claude Code skills that take a Confluence GDD link to a
finished `PaytableDialog<Game>` Unity prefab, the reusable block library they assemble it from, and
a Unity editor window that sets up the whole thing.

All of it ships as one Unity package. Pull the package, open the window, work through what it
flags.

## Getting set up

1. Add the package to your Unity project's `Packages/manifest.json`:
   ```json
   "com.cgs.paytablelibrary": "https://github.com/AndreiS-CGS/paytable-verstka.git?path=library#main"
   ```
   The repo is private, so your machine's git credentials need read access to it. Ask for a
   collaborator invite if `git ls-remote https://github.com/AndreiS-CGS/paytable-verstka.git`
   does not list refs.

2. Open **PlayStudios > Slot Tools > Paytable Tool** and press **Re-check all**.

The Setup tab checks the package, the Python environment, the skills, Confluence access and
unityMCP, and fixes what can be fixed from a button — including **Check for updates**, which
compares your resolved commit against the remote and offers to pull a newer one. What it cannot do — creating your Atlassian
API token, `gh auth login`, granting repo access — it lists separately so a permanently amber row
does not read as a bug.

Every row shows the exact command it ran and the full output. Statuses are four-valued rather than
pass/fail: a probe that timed out or could not find its tool reports **Blocked**, never Ok. That
distinction is the point of the tool — the procedure it replaced kept reporting success nobody had
verified.

[SETUP.md](SETUP.md) covers the same ground for an agent doing it without the window, and documents
the steps that stay manual.

## Using it

The **Run** tab takes the game name, slot id, GDD URL and sprite prefix, derives the bundle path,
validates everything, and composes a prompt to paste into Claude Code. It does not launch the run:
the skill has deliberate human checkpoints — the art gate, the review steps — that a headless run
would stall on.

You can also just talk to the skills directly. Each is independently invocable: asking to pull a
paytable from Confluence runs only `paytable-pipeline`, asking to pack an atlas runs only
`cgs-atlas-builder`, and `paytable-verstka` orchestrates both plus the assembly.

## Contents

```
library/                      the UPM package, com.cgs.paytablelibrary
├── package.json
├── BLOCKS.md                 per-block content-slot manifest — the authority on sizes and layout
├── Shells/                   empty dialog shells (GEL + MCF)
├── Blocks/                   Page_1, GridPage, StackPage, GridCell, SpecialPanel, PanelRow,
│                             PayRows, IconSlot, ManualSlot, TextBlock
├── Editor/                   C# the skills call rather than re-implement each run:
│   │                         grid math, PayRows text formatting, the atlas builder's Unity steps
│   └── Tooling/              the Paytable Tool window
└── Skills~/                  the three skills
    ├── paytable-pipeline/    extracts text, images and symbols from a Confluence GDD
    ├── cgs-atlas-builder/    packs symbol PNGs into an atlas + TMP Sprite Asset
    └── paytable-verstka/     orchestrates both, then assembles the prefab

requirements.txt              the four Python packages, installed into ~/.venvs/paytable-tools
```

`Skills~` is named for the tilde: Unity ignores any folder ending in `~`, so the skills ship with
the package without becoming Unity assets and without needing `.meta` files. Before they lived here
the package only carried `library/`, which is why setup used to require a separate clone and
`gh auth login`.

The skills find their own Python. Each script re-execs itself into `~/.venvs/paytable-tools` before
its first third-party import, so it does not matter which `python3` invokes them — and if nothing
resolves they fail loudly instead of running on a half-built environment.

## Working on the tooling itself

Clone the repo and point the project at your clone instead of the git URL:

```json
"com.cgs.paytablelibrary": "file:/absolute/path/to/paytable-verstka/library"
```

Edits are then live. For the skills, uncheck the window's install option and symlink them from the
clone:

```bash
ln -s "/path/to/paytable-verstka/library/Skills~/paytable-pipeline" ~/.claude/skills/paytable-pipeline
ln -s "/path/to/paytable-verstka/library/Skills~/cgs-atlas-builder"  ~/.claude/skills/cgs-atlas-builder
ln -s "/path/to/paytable-verstka/library/Skills~/paytable-verstka"   ~/.claude/skills/paytable-verstka
```

The window will not silently replace a symlink with a copy — it names the link target and asks.

**Copy, never symlink, from a git-resolved package.** It lives read-only under
`Library/PackageCache/` and is wiped on every re-resolve, so a link into it dangles and the skill
disappears mid-project.

**A git reference pins the commit it first resolved,** so pushing changes nothing for Unity until
the pin and the cached copy are both removed. `Client.Resolve()` alone only re-reads the lock.

For anyone consuming the package, the **Check for updates / Update** button in the Setup tab is the
way to do this — it shows which commit you are on, compares it against the remote, and pulls a newer
one. The Package row also prints the commit beside the version, because the version string does not
change per commit: two people on very different code both read `0.2.0`.

For working *on* the package, `tools/package.sh` covers the same ground from a shell, plus the
symlink mode that makes the whole question go away:

```bash
./tools/package.sh link    <unity-project>   # dev: symlink to this clone, edits are live
./tools/package.sh unlink  <unity-project>   # back to manifest.json
./tools/package.sh refresh <unity-project>   # git mode: forget the pin and re-fetch
./tools/package.sh status  <unity-project>   # which mode, which commit, which cache folder
```

Pass the project once via `$PAYTABLE_UNITY_PROJECT` and you can drop the argument. Unity re-resolves
on **window focus**, not on request, so click into it afterwards.

Use `link` while working on the package. Waiting for a fix you pushed minutes ago, and watching the
old bug fire again because Unity still holds the previous commit, is a bad way to spend an afternoon.

**New files under `library/` need a `.meta`.** A file that never passed through a writable Unity
`Assets/` folder has none, and Unity silently ignores un-metaed assets inside a git-resolved
package — no error, the file simply does not exist as far as Unity is concerned. The easy way is to
create it through a `file:`/embedded install, where the package path is writable and Unity writes
the `.meta` straight into the repo.

**Do not add `Library/` to `.gitignore`.** macOS git runs with `core.ignorecase=true`, so that rule
matches `library/` — the package itself. Already-tracked files keep working while every new file
silently stops being tracked, and `git commit` reports a clean tree.

## Status

Live at **https://github.com/AndreiS-CGS/paytable-verstka** (private, under the CGS work account).
Konami-Slots resolves the package and the `library/Editor/` C# loads and runs. Giving a colleague
access is one collaborator invite — the `manifest.json` entry needs no per-machine change, and the
window handles the rest of their setup.
