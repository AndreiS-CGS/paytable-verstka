---
name: paytable-verstka
description: >
  Self-contained end-to-end pipeline to lay out (verstka) a slot Pay Table in the CGS/Konami Unity
  project — from a Confluence GDD link to a finished PaytableDialog prefab, by ASSEMBLING it from a
  reusable UI block library. Use when the user wants to BUILD/LAY OUT a paytable ("сверстай пейтейбл",
  "verstka paytable", "собери пейтейбл", "lay out the paytable", "доведи пейтейбл до префаба"). It
  ORCHESTRATES `paytable-pipeline` (extraction) and `cgs-atlas-builder` (sprite atlas), then assembles
  the prefab in Unity via unityMCP. For extraction only use `paytable-pipeline`; for sprites only use
  `cgs-atlas-builder`.
---

# paytable-verstka — assemble a paytable from the UI block library

Builds a finished `PaytableDialog<Game>` prefab by cloning a shell and assembling reusable blocks —
NOT by cloning a whole donor prefab and mutating it (the old, brittle model).

## Portability (this skill is SHARED with colleagues — no per-machine hardcoding)
- **Unity project root:** detect at runtime (search upward for the `Konami-Slots` Unity folder / git
  root, or via Spotlight/`mdfind` if the project lives on an external volume), or take it from a
  config var. Never hardcode `/Users/<someone>/…`.
- **Working/intermediate artifacts** (`_verstka/…`): put under a repo-relative or configurable working
  dir inside the Unity project (e.g. `<UnityRoot>/_verstka/<Game>/`). Don't assume a personal Obsidian
  vault.
- **Confluence auth:** the API token (PAT) path is unreliable (often 403, or a huge-page render
  timeout on first try — retry once before assuming failure); use the BROWSER COOKIE session
  instead (each colleague's own logged-in profile). See `paytable-pipeline`.
- **Art:** locate per game inside the bundle — naming conventions VARY per game (some use
  `S_Symbol_<NAME>`/`HP_*`, others use letter-coded names like `S_PicA`/`S_CardK`). Never assume one
  convention; open candidate files and visually confirm before mapping. Never a personal Desktop folder.
- **The block library itself is a separate, shared asset** — see "Library architecture" below. Never
  hardcode its path as literally `Assets/PaytableLibrary/`; resolve by package name.

## Context (CGS/Konami)
- TWO slot systems, BOTH supported and BOTH alive (the system is PER-GAME, detect don't assume):
  **GEL** (root `PlayStudios.GEL.UI.SliderDialog`, bundles under `Assets/Bundles/_gel/_games/<slot>/`)
  and **MCF** ("обычные"; root `KonamiPortraitPaytable`/`KonamiPaytable`, bundles under
  `Assets/Bundles/_games/<slot>/`).
- A new slot is cloned from a similar slot, so its paytable prefab ALREADY exists at
  `<bundle>/Prefabs/Paytable/PaytableDialog<Game>.prefab` — **read its root component to detect the
  system, and ONLY for that.** This file may carry a STALE NAME from whatever slot it was donor-cloned
  from (e.g. a `digginbroscache` bundle containing a file literally named
  `PaytableDialogRedFortune.prefab`) — it is not necessarily even about the right game. Never open,
  edit, rename, or delete it. Build the real thing as a brand-new file next to it (see Phase 0).
- **Library architecture:** the reusable block library (shells + blocks) is its own Unity Package,
  today embedded at `Packages/com.cgs.paytablelibrary/` inside Konami-Slots (same pattern as
  `Packages/com.coplaydev.unity-mcp` — Unity auto-discovers any folder under `Packages/` that has a
  `package.json`, no `manifest.json` entry needed). Long-term this becomes a real external git repo
  referenced via a git URL in `manifest.json`; the internal folder layout (`Shells/`, `Blocks/`,
  `BLOCKS.md`) stays the same either way — always resolve paths by walking to the package folder by
  name, never hardcode an absolute path.
  - `Shells/PaytableDialog_GEL.prefab`, `Shells/PaytableDialog_MCF.prefab` — empty dialog shells
    (slider + indicator + nav, `cards: []`, no pages). Don't rename these without being asked — a
    `.prefab` renamed without its `.meta` desyncs the GUID; if renaming is ever needed, do it inside
    Unity (or rename both files together), never a bare shell `mv`.
  - `Blocks/` — atomic block prefabs (`Page_1`, `Text`, `ImageContainer`, `GoldBox`, `GoldBoxRow`,
    `PayBlock` — see "Block library reference" below). `BLOCKS.md` in the package root = the
    per-block content-slot manifest.
  - Common assets (page background, fonts) are NOT duplicated into the library or into each game's
    own bundle — blocks reference whatever already exists in the project (e.g. shared UI assets), to
    avoid cross-bundle asset duplication in the AssetBundle/Addressables pipeline.

## Core mental model — READ THIS FIRST
- **The unit of work is a LOGICAL BLOCK, not a page.** A logical block = one GDD section
  (`## Page N` + bold header). In the GDD each block starts on a new page; our output must too.
- **In-game page count is NOT known up front and won't match the GDD.** GDD screenshots are often an
  ALREADY-RENDERED real in-game paytable (labeled "PAGE X/N" in the corner) — treat their numbers as
  ground truth, but their PAGE BOUNDARIES and layout are not a target to copy pixel-for-pixel; you
  discover the final page count by filling pages and checking overflow.
- **PIC pays and Card pays are ALWAYS their own dedicated pages** — even when the GDD reference shows
  them combined with Scatter/Trigger on one image. We deliberately split them for consistency.
- **Symbol rendering — two techniques, picked by role, not habit:**
  - Symbol is the "hero" of a block (stands alone, not part of a sentence — e.g. the pay-grid icon in
    a `GoldBox`) → a plain UI `Image` with its own Sprite sub-asset (`ImageContainer`).
  - Symbol is mentioned INSIDE a sentence (rules text, "X AND Y ARE EQUIVALENT.") → inline TMP
    `<sprite name="X">` in that same text block, never a separate Image.
  Both need the symbol present in the atlas either way — see Phase 4/7.
- **"1 CREDIT" is ALWAYS added to a pay column**, even when the reference screenshot doesn't show it —
  this is our convention, not a literal reproduction of the source.
- **Title color is taken from the reference screenshot of THIS game**, never hardcoded yellow. When a
  Title combines 2+ named features, each feature's own established color gets its own `<color>` tag;
  connecting words ("AND") get a neutral/white color.
- **GRAND/MAJOR/MINOR/MINI = behavioral jackpot designations, NEVER numeric.** No numeric jackpot
  ladder exists in the corpus; "Available Jackpots" = badges stacked in a panel, no numbers.
- **All paytables are English only.**
- **Never mutate a donor.** The old game's existing prefab is read-only input for system detection.
  The block library's shells/blocks are read-only input for cloning. The ONLY thing you ever write to
  is the brand-new `PaytableDialog<Game>.prefab` you're building.

## Phases
Each phase writes an artifact under `_verstka/` so work survives context compaction.

### Phase 0 — Setup, inputs, SYSTEM DETECTION
1. Inputs (ask only what's missing): game name, Confluence GDD URL, sprite/asset prefix.
2. Locate the Unity project root and the game's bundle folder.
3. Locate the game's EXISTING paytable prefab (`<bundle>/Prefabs/Paytable/PaytableDialog*.prefab`).
   **Read its root component ONLY** → GEL (`SliderDialog`) or MCF (`KonamiPortraitPaytable`). Note its
   possibly-stale filename as a curiosity, not a target — never open/edit/rename/delete it.
4. Pick the matching shell: `Shells/PaytableDialog_{GEL,MCF}.prefab` from the block library package.
5. **Unity availability gate.** If unityMCP is not connected, don't block the whole run — proceed
   through Phases 1–4 (filesystem-only work), then stop before Phase 5/8 and report status.
6. Plan to create the real output as a brand-new file: `PaytableDialog<CorrectGameName>.prefab`, next
   to (never overwriting) the existing donor file. Updating whatever config/reference should point at
   the new prefab is out of scope for this skill (frontend/build owns that).

### Phase 1 — Extract (delegates to `paytable-pipeline`)
Produces `<Game> Paytable.md`, `… Clean.md`, `… Symbols.md`, `Paytable Images/PageN.jpg`. Uses the
browser cookie session (not PAT). A "Pay Table Pages section not found" / tiny-HTML-length result on
the first try can be a transient Confluence render timeout on a large page — retry once before
treating it as a real failure.
**`Symbols.md`'s regex is not exhaustive for complex games** — it misses `DARK_*` variants, `2_*`
doubled variants, `X&Y`-combo tokens, `+N_SPIN`-style tokens. Always additionally regex-sweep the
full raw text yourself for every `[...]`/`<...>` token and reconcile against `Symbols.md` — don't
trust it alone.

### Phase 2 — Normalize & Review  ⟵ user-confirmation gates
Write `_verstka/blocks.yaml`, `win_tables.yaml`, `review.md`.
1. **Symbol normalization** — every bracketed token (single/multi-word, variant/prefixed, cards) is a
   symbol needing a sprite. Dedup true aliases; ask the user on ambiguous merges (e.g. a "WIN_*"
   variant of a symbol shown as equivalent in the rules — confirm whether it needs its own sprite).
2. **Reasoning review** of the rules text — flag contradictions, leftover struck-out fragments,
   copy-paste errors → `review.md`. Show the user.
3. **Win-table extraction (vision)** → `win_tables.yaml`. The reference image is sometimes a REAL
   already-built in-game paytable render (labeled "PAGE X/N") rather than a mockup — its numbers are
   strong ground truth, but vision still misreads digits sometimes; show the user to confirm. Capture
   the EXACT row set per symbol (don't assume a fixed template — PICs may pay 5/4/3/2 while cards pay
   only 5/4/3, or vice versa).
4. **Segmentation → `blocks.yaml`** — each GDD `## Page N` + header = one logical block, 1:1, even if
   two headers share one physical GDD page (that's one block with two subblocks, don't merge across
   `## Page N` boundaries and don't split within one).

### Phase 3 — Map logical blocks → library blocks
For each block in `blocks.yaml`, pick how it gets built (see "Block library reference" below):

| Logical block | How it's built |
|---|---|
| Rules text (any — basic/feature) | Its own page: `Text` block(s) in Body, + `ImageContainer` if the reference shows a diagram/image (see Phase 5 sizing method) |
| PIC pays | ALWAYS its own page. Dynamic grid: `GoldBoxRow` + `GoldBox` (see Phase 5) |
| Card pays | ALWAYS its own page, same grid mechanism as PIC |
| Substitute / Scatter / Trigger / jackpot-badge panel | `GoldBox` used AS A PANEL (not a grid cell) — stack of PayRow (image(s)+`PayBlock`) + extra blocks (equivalence lines, descriptive text), see Phase 5 "unique complex page" pattern |
| Standalone image (e.g. paylines diagram) | One `ImageContainer` sized to ~fill Body |

Write the mapping to `_verstka/block_mapping.md`.

### Phase 4 — Assets (delegates to `cgs-atlas-builder`)
1. From the full symbol list (Phase 1's regex sweep, not just `Symbols.md`), produce the sprite-name
   list — every symbol goes in the atlas regardless of whether it'll be used as a hero `Image` or an
   inline `<sprite>` tag; that choice only affects Phase 5 filling, not this list.
2. Ask the user to provide/confirm PNGs — never fabricate art. Art file-naming conventions vary per
   game (see Portability) — visually confirm each mapping (open the file, compare to the reference),
   don't assume from filename alone.
3. Run `cgs-atlas-builder` → atlas PNG/JSON, TMP Sprite Asset, material, hashes from Unity runtime,
   `UpdateLookupTables()`. Verify: `GetSpriteIndexFromName ≥ 0`, `spriteCharacterTable.Count > 0`,
   `spriteSheet != null`, `material.mainTexture != null`.
4. **New step (proven in practice):** build `TextureImporter.spritesheet` (a `SpriteMetaData[]`) from
   the SAME rectangles already written into the TMP `spriteGlyphTable` (no separate JSON needed — the
   glyph table already has `glyphRect` per symbol name), set `spriteImportMode = Multiple`, then
   `SaveAndReimport()`. This slices the SAME atlas texture into individually-addressable Sprite
   sub-assets — usable in a plain `Image.sprite` — with zero texture duplication. Verify by
   `AssetDatabase.LoadAllAssetsAtPath` returning one `Sprite` per symbol name.

### Phase 5 — Assemble  (CORE — sequential, single Unity instance, inline QA)
> **CRITICAL: ALL prefab stage operations — open, add pages, fill content, register slider, save — MUST
> happen in a SINGLE `execute_code` call.** The prefab stage does NOT persist between separate calls;
> splitting across calls loses everything from the first call. Always end with
> `PrefabUtility.SaveAsPrefabAsset(root, path)` BEFORE `GoToMainStage()`.

1. Clone the matching shell (`Shells/PaytableDialog_{GEL,MCF}.prefab`) to the new
   `PaytableDialog<Game>.prefab` path — never the game's existing donor file.
2. For each logical block, build per its mapped type:

   **a) Rules text page:** instantiate `Page_1` as a page; in its `Body`, instantiate `Text` block(s)
   (bulleted, font untouched — the prefab auto-sizes its own height) and, if the reference shows a
   diagram/image, an `ImageContainer` too. Multiple pieces just get added as siblings in order — `Body`
   is a Vertical Layout Group and stacks them automatically, no manual positioning.
   `ImageContainer.Height` sizing method (general — use for ANY one-off image content, not just this
   case): on the reference screenshot, measure `image_height / body_area_height` (Body-equivalent
   area only, not the whole card/screenshot with background) and multiply by 1440. Recompute per
   image, never reuse a number from a different page.

   **b) PIC/Card pays page:** count symbols `n` → `rows = ceil(n/3)`, distribute `n` across rows as
   evenly as possible with the larger remainder in earlier rows, never an orphan single-item last row
   (4→2+2, 5→3+2, 6→3+3 — this generalizes past 6 too). Do NOT use a `GridLayoutGroup` — with a fixed
   column cap it wraps the remainder onto its own row (4 at cap-3 gives 3+1, not the 2+2 we want);
   instead build explicit `GoldBoxRow` containers, one per row, and put the exact planned cell count
   into each. Body is 1410×1440; sizing (symmetric — 50 at every edge and between cells, whether width
   or height):
   ```
   width_cell  = 1360/maxCols - 50            // maxCols = widest row on this page
   height_cell = (1340 - 50×(rows-1)) / rows
   row_height  = height_cell + 50
   ```
   Set each `GoldBoxRow`'s height to `row_height`; set each `GoldBox`'s size to
   `(width_cell, height_cell)`. Row width needs no manual sizing — `Body`'s own layout force-expands
   it. Inside each `GoldBox`: `ImageContainer` (height = `height_cell/2`, hero symbol as an `Image`
   with its sliced sub-sprite) + `PayBlock` (height = `height_cell/2` — always exactly half, never a
   fixed number). `PayBlock.Count` = one multi-line TMP text, first line BLANK (aligns with "1 credit"
   on the Pay side), remaining lines are the count numbers, all wrapped in one `<color=green>` tag
   (not the component's default font color). `PayBlock.Pay` = one multi-line TMP text, first line
   ALWAYS `"1 credit"` (we add it — see Core mental model), remaining lines are the pay values (with
   any bonus suffix like `"(+N FREE GAMES)"` kept in the SAME line, same column) wrapped in one
   `<color=yellow>` tag spanning all of them. The row count per symbol comes from `win_tables.yaml`
   — never a fixed template (a symbol paying 5/4/3/2 next to one paying only 5/4/3 is normal).

   **c) Substitute/Scatter/Trigger/jackpot-badge page — "unique complex page" pattern:** don't build a
   clever horizontal composite; stack independent blocks vertically in `Body`, same as any other page:
   - `PayRow`: the hero image(s) + a `PayBlock`, ALWAYS one horizontal row (even with 2-3 hero images
     side by side, e.g. green-wild/WILD-burst/red-wild sharing one pay table).
   - If the reference shows a visible bordered panel around this content (e.g. "SCATTER"/"TRIGGER"
     boxes) — wrap the PayRow + any extra text in a `GoldBox` used purely as a decorative panel (not a
     grid cell), with its own internal label `Text` object (e.g. "SCATTER") acting as a mini-header.
     **When `GoldBox` is used this way (panel, not grid cell) give it 50 padding on all four sides —
     on that specific instance only, never change the shared `GoldBox` prefab's own default (0
     padding) or it breaks the PIC/Cards grid math above, which assumes 0.**
   - Any sentence mentioning a symbol inline ("X AND Y ARE EQUIVALENT.") is ONE `Text` block with
     inline `<sprite name="X">` tags — never separate Image objects for that. (Remember to assign
     `TMP_Text.spriteAsset` explicitly; it's `NULL` by default on `Text.prefab`.)
   - Size any one-off block by the same "fraction of the Body-equivalent area on the reference ×
     1440" method as 5a — never hardcode a number from a previous page.
   - If two or more panels stack on one page and the reference shows a gap between them, do NOT
     change `Body`'s own spacing (that's shared/general-purpose) — wrap just this page's panels in
     their own container `GameObject` with its own `VerticalLayoutGroup(spacing=50)`.
   - `GoldBox`'s `VerticalLayoutGroup.childAlignment = MiddleCenter` IS a permanent fix on the shared
     `GoldBox` prefab itself (its content should never hug the top) — this one is general, unlike the
     panel padding above.
3. **Title/Header filling (every page type):**
   - `Header` (top-left label) = a fixed category taken from the reference screenshot (e.g. "Basic
     Game Rules") — it is NOT unique per page; it repeats unchanged across consecutive pages in the
     same section.
   - `Title` = the bold header text from the GDD section. `™`/`TM` → inline
     `<color=white><size=20><voffset=600>TM</color></size></voffset>` (delete any leftover
     `TrademarkText`/`TrademarkText_1` child objects first — don't split the string on `™`). **Color
     is taken from the reference screenshot of THIS game** (never hardcoded yellow) — when the Title
     combines 2+ feature names, color each piece with its own established `<color>` tag, connecting
     words neutral/white.
4. **Page positioning in the slider:** move the page's ROOT object (`transform.localPosition.x = slot
   × CARD_OFFSET`, slot 0 → x=0) — never touch `PageParent`'s own local position inside the page; it
   stays identical regardless of slot.
5. **QA render inline** after each page (see Phase 7 for the technical render setup) — compare to the
   reference: full title, every symbol resolved (not literal `<sprite…>` text), numbers match
   `win_tables.yaml`, nothing clipped. Fix and re-render until it matches, then complete that page's
   task before moving to the next.

### Phase 6 — Finalize
1. Order pages by slider slot (root `localPosition.x`, per 5.4). Rename page GameObjects `Page_1..Page_N`.
2. Set `Page X / N` labels with the final N.
3. Register the slider on the dialog root: `cards[]` = the pages in order, `CARD_OFFSET` — drives
   paging AND the indicator dots.
4. Verify sprite hashes (`GetSpriteIndexFromName ≥ 0`). Full-pass QA: render every page vs the GDD set.
5. **New final step:** move the whole new prefab's root AND every child object, recursively, onto the
   **"Dialog"** layer.

### Phase 7 — Overflow & visual validation  (MANDATORY auto-fix loop)
TMP `overflowMode = Overflow` lets text spill past its RectTransform silently — validate by the
rendered text MESH (`textBounds` vs the page `Frame`'s world rect), not the rect, exactly as before:
```csharp
frame.GetWorldCorners(cor); float fBot=cor[0].y, fTop=cor[1].y;
var b = tm.textBounds;
float meshBot = tm.rectTransform.TransformPoint(new Vector3(0, b.center.y-b.extents.y, 0)).y;
float meshTop = tm.rectTransform.TransformPoint(new Vector3(0, b.center.y+b.extents.y, 0)).y;
bool overflow = (fBot - meshBot > 8) || (meshTop - fTop > 8);
```
**Fix (loop until every page passes):** text overflow → split into a continuation page (never cut a
bullet mid-way, re-measure, split again if still overflowing); grid overflow/empty cells → fewer
categories per page or resize the grid to the symbol count. Re-run Phase 6 after any split (slots,
numbering, `cards[]`).

**Rendering the QA screenshot — technical setup (hard-won, don't relearn this):**
- Use **Game View / Play Mode**, not Scene View, for anything you're actually judging visually. Scene
  View has repeatedly failed to render `Image`/Canvas UI content (and once rendered a Title vertically
  letter-by-letter) with no real underlying bug — both times a Play Mode screenshot of the exact same
  state was correct. Scene View is fine ONLY for coarse geometry checks done by reading world
  coordinates in code, never by eyeballing its screenshot.
- Position the QA camera well behind ALL page content on the Z axis (e.g. `z = -60`), not just in
  front of the `Frame`. Elements nested inside `Body`/`Frame` accumulate their own per-layer Z offset
  (observed as deep as `z = -14`); a camera sitting at `z = -10` can end up in FRONT of content that's
  therefore invisible to it despite every other property (enabled, color, fontSize) being correct.
- Any `TextMeshPro` you create ad hoc in test/QA code (via `AddComponent`, not from a library prefab)
  must have its `font` explicitly set to the project's font asset (copy it from any nearby working
  text, e.g. `Title`). Left unset, Unity substitutes the default `LiberationSans SDF`, which renders
  microscopically small at this canvas's world-unit scale.

## Block library reference
Lives in the block library package (`Blocks/`), documented per-block in that package's `BLOCKS.md`:
- `Page_1.prefab` — the base clean page template: `PageParent > {Background, Top{Header, Page},
  Frame > Inner_Group{Title, Body}, Bottom}`. `Body` starts empty; content is instantiated into it.
- `Text.prefab` — a bulleted multi-line TMP block; grows/shrinks its own height automatically.
  `spriteAsset` is `NULL` by default — assign explicitly whenever using inline `<sprite>` tags.
- `ImageContainer.prefab` — `Image` with `preserveAspect = true` (rule for EVERY `Image` you create,
  anywhere) inside a sizeable container; only its Height needs setting, width follows from aspect.
- `GoldBox.prefab` — a bordered panel/cell with its own `VerticalLayoutGroup` (`childAlignment =
  MiddleCenter`, padding 0 by default — bump to 50-all-sides only on a specific panel-use instance,
  never the shared prefab). Used both as a PIC/Card grid cell and as a Substitute/Scatter/Trigger
  panel.
- `GoldBoxRow.prefab` — one row of `GoldBox` cells; width auto-expands to fill `Body`, height is set
  explicitly per the grid formula.
- `PayBlock.prefab` — `Count` + `Pay`, two independent multi-line TMP texts (not one shared text),
  colored via inline `<color>` tags on the whole run of lines, not per-component default color.

## Known gotchas
| Problem | Fix |
|---|---|
| `execute_code` times out → assume failure | Timeout ≠ failure. Unity keeps executing C# after the MCP timeout; follow up with a diagnostic call before retrying. |
| Retry after timeout → duplicate pages | Same instantiation code run twice = 2× pages. Detect via childCount, fix by deleting extras in reverse and re-indexing. |
| Changes lost between execute_code calls | PrefabStage does not persist. One call: open, build, `SaveAsPrefabAsset`, THEN `GoToMainStage`. |
| Page order wrong | Page order = GDD TEXT order (`## Page N` headings), NOT the order of screenshot images in the GDD. |
| Picked wrong dialog system | Detect from the existing prefab's ROOT COMPONENT (GEL vs MCF) — never assume. |
| Existing prefab has a stale/wrong-game filename | Normal — it's a donor leftover. Read its root component only; build the real thing as a new file. |
| `.prefab` renamed without its `.meta` | Desyncs the GUID. Rename both together, or better, only inside Unity — never a bare shell `mv`. |
| Copying an asset for experimentation creates a GUID collision | Drop the copied `.meta` file(s) before Unity imports the copy, so it gets a fresh GUID. |
| `<sprite name="X">` shows as literal text | Sprite asset lacks that name, or `TMP_Text.spriteAsset` was never assigned on that text object. |
| `<sprite name="X">` renders BLANK (tables OK) | Atlas texture not assigned — check `spriteSheet != null` AND `material.mainTexture != null`. |
| Hero symbol needs to be an `Image`, not inline sprite | Slice the atlas texture via `TextureImporter.spritesheet` (Phase 4 step 4) — TMP glyph table alone doesn't produce usable `Image.sprite` sub-assets. |
| Jackpot badges show invented numbers | Wrong — behavioral only, no numeric ladder exists in the corpus. |
| `GridLayoutGroup` used for a PIC/Card grid | Wraps the remainder onto its own row (4-at-cap-3 → 3+1). Use explicit `GoldBoxRow` containers with the planned per-row count instead. |
| Grid cell/row sizing math giving wrong spacing | Check whether you need it symmetric (50 everywhere, the default) — an asymmetric edge/gap split usually requires a dedicated spacer, not just shrinking rows unevenly; a uniform symmetric budget sidesteps the whole problem. |
| `Body`'s own spacing/padding changed to fix a one-page issue | Don't — it's shared across every page type. Wrap that page's specific content in its own container with its own `VerticalLayoutGroup`. |
| Page repositioned but content jumps to the wrong place | Move the page's ROOT object's `localPosition`, never `PageParent`'s — `PageParent`'s local position is constant regardless of slot. |
| `Symbols.md` missing symbols the rules text clearly uses | Its regex isn't exhaustive for `DARK_*`/`2_*`/combo/`+N_SPIN` tokens — always cross-check with your own full-text regex sweep. |
| Feature names not colored consistently | Color comes from the reference screenshot per-game/per-feature, not a hardcoded yellow — check the actual image. |
| Scatter/bonus symbol pay shows only the credit number | Keep the full award in the same Pay-column line, e.g. `"1000 (+10 FREE GAMES)"` — don't drop the suffix or split it into another column. |
| QA screenshot shows nothing / garbage | If it's Scene View, that's a known rendering gap for Canvas UI — use Game View/Play Mode instead. If it's Game View and still blank, check camera Z depth against the content's actual world Z (can be well negative from nested layer offsets). |
| Ad-hoc QA text renders as tiny illegible dots | Default `TextMeshPro` font (`LiberationSans SDF`) at this canvas's scale — assign the project's font asset explicitly. |
| Text fits by RectTransform but visibly runs off the page | `overflowMode=Overflow` spills the MESH past the rect — validate via `textBounds` vs the Frame (Phase 7), then split the page. |
| Multi-word/combo symbol not picked up from GDD text | Check both `Symbols.md` AND your own full regex sweep (see Phase 1) — normalize spaces/`&`/`+` to `_` for the sprite name, confirm the convention with the user if ambiguous. |

## Delegation summary
| Phase | Task | Delegate? | Why |
|---|---|---|---|
| 1 | Extraction | to `paytable-pipeline` | dedicated script |
| 4 | Sprite atlas (+ sub-sprite slicing) | to `cgs-atlas-builder` | dedicated skill |
| 5 | Assemble loop + QA | **No (inline)** | sequential, single Unity instance, tight QA loop |

## Reference docs (in this skill's `reference/`)
- `legacy_page_taxonomy.md` — historical page-type catalog (24-game corpus). Superseded in structure
  by the dynamic block system above, but still useful background on what page TYPES recur across games.
- `donor_catalog.md` / `guardiansofgiza_catalog.md` — historical GEL/MCF donor structures from the old
  clone-and-mutate model. Kept for historical reference only — do not use as a basis for new work.
- The block library itself (`Shells/`, `Blocks/`, `BLOCKS.md`) lives in the `com.cgs.paytablelibrary`
  package, not in this skill folder.
