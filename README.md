# paytable-verstka

Self-contained repo for the `paytable-verstka` Claude Code skill: assembles a slot Pay Table
`PaytableDialog<Game>` prefab in a CGS/Konami Unity project from a Confluence GDD, by cloning a
shell and assembling reusable UI blocks — never by mutating a donor prefab in place.

## Contents
- `SKILL.md` — the skill itself (phases, core mental model, gotchas).
- `reference/` — historical background docs (page-type taxonomy, old donor structures — superseded
  by the dynamic block system in `SKILL.md`, kept for context only).
- `scripts/` — reserved for verstka-specific helper scripts (none yet — assembly currently runs via
  inline unityMCP `execute_code` calls per the skill's Phase 5).
- `library/` — the reusable Unity block library (shells + blocks + `BLOCKS.md` content-slot
  manifest). This is a real Unity Package (`package.json` at its root, name
  `com.cgs.paytablelibrary`).

## Installing `library/` into a Unity project
Any of these work; pick based on how much you want to iterate on the library from inside that
specific project vs. treat this repo as the single source of truth:

**A — embedded package (copy, editable in-project):**
Copy `library/` into that project's `Packages/com.cgs.paytablelibrary/` folder. Unity auto-discovers
it (any folder under `Packages/` with a `package.json`, no `manifest.json` entry needed) — same
pattern as `Packages/com.coplaydev.unity-mcp` in Konami-Slots.

**B — local package via `file:` reference (single source of truth, no duplication):**
Add to that project's `Packages/manifest.json`:
```json
"com.cgs.paytablelibrary": "file:/Users/andreibarsuk/Documents/Unity/paytable-verstka/library"
```
Unity resolves it directly from this repo — edits here are picked up without a copy step. Use an
absolute path if the two are on different volumes (as with Konami-Slots on `/Volumes/Second`).

**C — git package (future, once this repo has a remote):**
```json
"com.cgs.paytablelibrary": "https://.../paytable-verstka.git?path=library#<branch-or-tag>"
```

Konami-Slots currently has this library as an **embedded copy** at
`Packages/com.cgs.paytablelibrary/` (option A) — this repo is the portable, installable source that
copy was taken from. When editing the library going forward, prefer editing here and re-copying (or
switch Konami-Slots to option B) rather than letting the two drift apart.
