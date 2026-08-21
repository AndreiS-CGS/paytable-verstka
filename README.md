# paytable-verstka

Self-contained repo for the CGS/Konami slot-paytable pipeline: three Claude Code skills that
together take a Confluence GDD link to a finished `PaytableDialog<Game>` Unity prefab, plus the
reusable Unity block library they assemble it from.

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
Any of these work; pick based on how much you want to iterate on the library from inside that
specific project vs. treat this repo as the single source of truth:

**A — local package via `file:` reference (default assumption in the skills above):**
Add to that project's `Packages/manifest.json`:
```json
"com.cgs.paytablelibrary": "file:/absolute/path/to/paytable-verstka/library"
```
Unity resolves it directly from this repo — edits here are picked up without a copy step. Use an
absolute path (works across volumes too, e.g. an external drive).

**B — embedded package (copy, editable in-project, no shared source of truth):**
Copy `library/` into that project's `Packages/com.cgs.paytablelibrary/` folder. Unity auto-discovers
it (any folder under `Packages/` with a `package.json`, no `manifest.json` entry needed). Only do
this if you specifically want an independent, disconnected copy — it will drift from this repo.

**C — git package (once this repo has a remote):**
```json
"com.cgs.paytablelibrary": "https://.../paytable-verstka.git?path=library#<branch-or-tag>"
```

## Status
This repo currently has no `git remote` — it's local-only. Distributing it to another machine or
colleague requires setting one up first (e.g. push to GitHub), then `git clone` there and pointing
that machine's own skill symlinks + `manifest.json` at the clone.
