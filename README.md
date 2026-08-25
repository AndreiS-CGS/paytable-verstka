# paytable-verstka

Self-contained repo for the CGS/Konami slot-paytable pipeline: three Claude Code skills that
together take a Confluence GDD link to a finished `PaytableDialog<Game>` Unity prefab, plus the
reusable Unity block library they assemble it from.

**Setting this up on a new machine?** Point your Claude Code agent at **[SETUP.md](SETUP.md)** and
have it work through that checklist — it's written as instructions for an agent to execute, not a
human to read and translate into commands.

## Contents
```
skills/
├── paytable-pipeline/    — extracts Pay Table text/images/symbols from a Confluence GDD
│   └── scripts/paytable_from_confluence.py
├── cgs-atlas-builder/    — packs symbol PNGs into a TMP Sprite Asset + atlas texture
│   └── scripts/{process_pngs,pack_atlas}.py
└── paytable-verstka/     — orchestrates both of the above, then assembles the prefab
    └── reference/        (historical background docs)

library/                  — the reusable Unity Package the assembly step clones/instantiates from
├── package.json          (com.cgs.paytablelibrary)
├── BLOCKS.md              — per-block content-slot manifest
├── Shells/                — empty dialog shells (GEL + MCF)
├── Blocks/                — atomic content block prefabs (Page_1, Text, ImageContainer, GoldBox,
│                            GoldBoxRow, PayBlock)
└── Editor/                — real C# utilities the skills call (grid math, PayBlock text
                             formatting, atlas-builder Unity-side steps) — not prose to
                             re-implement each run.
```

Each skill is independently invocable — asking to just pull a paytable from Confluence only runs
`paytable-pipeline`; asking to just pack an atlas only runs `cgs-atlas-builder`. `paytable-verstka`
orchestrates both plus the assembly logic itself. All three share this one repo and one library so
nothing drifts out of sync between them.

## Installing the skills (Claude Code)
Each skill folder needs its own symlink from wherever Claude Code loads skills from on that
machine, e.g.:
```bash
ln -s "/path/to/paytable-verstka/skills/paytable-pipeline" ~/.claude/skills/paytable-pipeline
ln -s "/path/to/paytable-verstka/skills/cgs-atlas-builder" ~/.claude/skills/cgs-atlas-builder
ln -s "/path/to/paytable-verstka/skills/paytable-verstka" ~/.claude/skills/paytable-verstka
```
(Adjust the target if your Claude Code setup loads skills from a different folder — some setups use
a plugin-managed skills directory instead of `~/.claude/skills`.)

## Installing `library/` into a Unity project

**A — git package (current setup, used by Konami-Slots today):**
Add to that project's `Packages/manifest.json`:
```json
"com.cgs.paytablelibrary": "https://github.com/AndreiS-CGS/paytable-verstka.git?path=library#main"
```
Unity clones the repo (private — the machine's `git`/`gh` credentials need read access) and resolves
the package from its own cache. This is a `#main` reference, so Unity pins to whatever commit was
current at first resolve — after pushing new commits to `library/`, force a re-pull with:
```csharp
// remove the stale entry from Packages/packages-lock.json and the matching folder under
// Library/PackageCache/, then:
UnityEditor.PackageManager.Client.Resolve();
```
(`Resolve()` alone is not enough if the package was already resolved once — it only re-reads the
existing lock, it doesn't check `main` for new commits on its own.)

**B — local package via `file:` reference (for iterating without pushing every change):**
```json
"com.cgs.paytablelibrary": "file:/absolute/path/to/paytable-verstka/library"
```
Unity resolves it directly from your local clone — edits are picked up without a copy or a push.
Good for active development on the library; switch back to option A once changes are pushed, so the
whole team stays on the same source.

**C — embedded package (copy, no shared source of truth):**
Copy `library/` into that project's `Packages/com.cgs.paytablelibrary/` folder. Unity auto-discovers
it (any folder under `Packages/` with a `package.json`, no `manifest.json` entry needed). Only do
this if you specifically want an independent, disconnected copy — it will drift from this repo.

**Gotcha when adding new files under `library/`:** files that only ever existed inside this git
repo (never imported into a real, writable Unity `Assets/` folder first) have no `.meta` file, and
Unity silently ignores un-metaed assets inside a git-resolved (read-only) package — no error, the
file just doesn't exist as far as Unity's concerned. Before committing a new file there: copy it
into any ordinary `Assets/` folder in a local Unity project, let Unity generate the `.meta` on
import/refresh, then copy the `.meta` back next to the file in this repo (and delete the temporary
copy from `Assets/`). This bit us once already with `library/Editor/*.cs` — see git history.

## Installing the skills (Claude Code)
Each skill folder needs its own symlink from wherever Claude Code loads skills from on that
machine, e.g.:
```bash
ln -s "/path/to/paytable-verstka/skills/paytable-pipeline" ~/.claude/skills/paytable-pipeline
ln -s "/path/to/paytable-verstka/skills/cgs-atlas-builder" ~/.claude/skills/cgs-atlas-builder
ln -s "/path/to/paytable-verstka/skills/paytable-verstka" ~/.claude/skills/paytable-verstka
```
(Adjust the target if your Claude Code setup loads skills from a different folder — some setups use
a plugin-managed skills directory instead of `~/.claude/skills`.)

## Status
Live at **https://github.com/AndreiS-CGS/paytable-verstka** (private repo, under the CGS work
account). Konami-Slots' `Packages/manifest.json` currently uses option A above, pointed at this
exact URL — confirmed resolving (`source=Git`) and the `library/Editor/` C# utilities load and run.
To give a colleague access: they need read access to this private repo (add them as a collaborator,
or move it under the `myKonami` org once its GitHub SSO/SAML step is sorted out), then they set up
their own machine's skill symlinks per above — the `manifest.json` git URL itself needs no
per-machine change.
