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

## Autonomy contract — how this skill runs

**Run the whole pipeline without stopping.** Everything below is decidable from the GDD, the
reference screenshots and the project; make the call and keep going. Do not pause to show
intermediate results for approval.

**Stop and ask in exactly two situations, both at a fixed point in the run:**
1. **A required input is missing** — game name or Confluence GDD URL. Ask once, for everything
   missing at once, at the start.
2. **Symbol art is missing — ask at Phase 4, before building the atlas.** By then the symbol list is
   final, so this is the one moment where the full picture is known. Post the complete list of
   symbols with the art file mapped to each, and name every symbol you could not find art for. Then
   wait. Never invent, substitute or re-use another symbol's art.

   **Do not defer this to the end of the run.** Discovering at the finish that two symbols were
   placeholders the whole time is a failure of this contract, not a report. If the answer is "build
   anyway", placeholders are fine — but that is the user's call to make at Phase 4, not yours to make
   silently.

**Everything else is reported, not asked.** Write findings as you go and deliver one report at the
end covering: contradictions and leftover struck-out fragments found in the GDD (`review.md`),
manual slots awaiting a picture, and any page you could not make fit. A finding never blocks
assembly — a page with a manual-slot placeholder is a finished page for this purpose.

**When a judgement is genuinely ambiguous, choose the option that is visible and reversible** — a
magenta placeholder, a reported mismatch, an extra page — over one that silently guesses.

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
  from (a bundle for one game can contain a paytable prefab literally named after a different
  game) — it is not necessarily even about the right game. Never open,
  edit, rename, or delete it. Build the real thing as a brand-new file next to it (see Phase 0).
- **Repo & library architecture:** everything for this pipeline lives in ONE repo,
  `paytable-verstka` (`Documents/Unity/paytable-verstka/` on this machine — resolve by walking to it,
  never hardcode that this exact path exists on every machine):
  ```
  paytable-verstka/
  ├── skills/
  │   ├── paytable-pipeline/    (extraction — its own SKILL.md + scripts/)
  │   ├── cgs-atlas-builder/    (sprite atlas — its own SKILL.md + scripts/)
  │   └── paytable-verstka/     (this skill's own SKILL.md + reference/)
  └── library/                   (the Unity Package, com.cgs.paytablelibrary — see below)
  ```
  Each `skills/<name>/` is independently symlinked into wherever Claude Code loads skills from on
  each machine, so all three stay separately invocable ("скачай пейтейбл" still only runs
  extraction) even though they share one repo and one library.
  `library/` is a real Unity Package (`package.json` at its root, name `com.cgs.paytablelibrary`).
  A consuming Unity project references it via a `file:` entry in that project's
  `Packages/manifest.json`:
  ```json
  "com.cgs.paytablelibrary": "file:/absolute/path/to/paytable-verstka/library"
  ```
  — resolved live from the repo, no copy step, no drift between what you edit and what Unity uses.
  (An embedded-copy-inside-`Packages/` variant also works if a project prefers that — same
  `package.json` discovery Unity already does for e.g. `com.coplaydev.unity-mcp` — but the `file:`
  form is what this pipeline assumes unless told otherwise.) Internal layout:
  - `Shells/PaytableDialog_GEL.prefab`, `Shells/PaytableDialog_MCF.prefab` — empty dialog shells
    (slider + indicator + nav, `cards: []`, no pages). Don't rename these without being asked — a
    `.prefab` renamed without its `.meta` desyncs the GUID; if renaming is ever needed, do it inside
    Unity (or rename both files together), never a bare shell `mv`.
  - `Blocks/` — the block prefabs (`Page_1`, `GridPage`, `StackPage`, `GridCell`, `SpecialPanel`,
    `PanelRow`, `PayRows`, `IconSlot`, `ManualSlot`, `TextBlock` — see "Block library reference"
    below). `BLOCKS.md` in the package root = the per-block content-slot manifest, with the real
    sizes and layout settings read off the prefabs.
  - `Editor/` — real C# utilities (`PaytableGridMath`, `PaytablePayBlock`, `PaytableAtlasBuilder`),
    not prose to re-derive each run — call these, don't reimplement them. See "Block library
    reference" below.
  - Common assets (page background, fonts) are NOT duplicated into the library or into each game's
    own bundle — blocks reference whatever already exists in the project (e.g. shared UI assets), to
    avoid cross-bundle asset duplication in the AssetBundle/Addressables pipeline.
    **`Page_1` and its background ARE the standard.** A reference screenshot with a different-coloured
    background is not a defect to match — don't recolour the shared prefab, don't add a per-game
    background, and don't raise it as a finding.

## Core mental model — READ THIS FIRST
- **Division of authority: content comes from the GDD TEXT; only payouts and colours come from the
  screenshot.** Reference screenshots lag the document — a GDD routinely strikes out copy the render
  still shows, and a value already present in the text can be blank in the image. Build from the
  **Clean** text and treat the screenshot as a layout reference, not a content source. Two
  exceptions, because the text simply doesn't hold the data: **payout numbers** (the Pay Table Pages
  section carries none) and **colours** (titles are plain `<strong>` with no styling). Sample colours
  with `paytable-pipeline`'s `scripts/title_color.py` and `scripts/feature_color.py` — don't eyeball
  them, and don't re-implement the sampling.
- **The unit of work is a LOGICAL BLOCK, not a page.** A logical block = one GDD section
  (`## Page N` + bold header). In the GDD each block starts on a new page; our output must too.
  **If one GDD page carries two `Title + text` sections, split it into TWO logical pages** — that
  keeps `Title` a single page-chrome field instead of promoting it to a body-level block. Such a page
  shows two title bands at different heights in the reference image.
- **Page order = the GDD's `## Page N` headings, and nothing else.** The `PAGE X/Y` counter printed
  inside a screenshot is part of that same stale render — informative, never authoritative. Neither
  are attachment filenames (`Help_NN_*`), which are legacy asset names. Extraction already numbers
  `PageN.jpg` by GDD order, so the files line up as-is.
- **In-game page count is NOT known up front and won't match the GDD** — you discover the final page
  count by filling pages and checking overflow.
- **Symbols are split across THREE pages: specials, majors, minors** — even when the GDD reference
  shows them combined on one image. Specials (substitute / scatter / trigger) go on `StackPage`;
  majors and minors each get their own `GridPage`. Split them even when the reference does not.
- **Symbol rendering — two techniques, picked by role, not habit:**
  - Symbol is the "hero" of a block (stands alone, not part of a sentence — e.g. the pay-grid icon in
    a `GridCell` or a `PanelRow`) → a plain UI `Image` with its own Sprite sub-asset (`IconSlot`).
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
- **Every inline sprite carries a size tag AND a voffset. A bare `<sprite name="X">` is a bug.**
  Without a tag `P` is 100, which renders the sprite exactly one cap-height tall — three to five
  times smaller than any reference, and sitting on the baseline instead of centred on the line. Emit:
  ```
  <voffset=5.11em><size=340%><sprite name="SYMBOL"></size></voffset>
  ```
  **`voffset` goes OUTSIDE `size`** — inside, TMP multiplies it by the *scaled* font size and the
  correction moves with `P`.
  **Two size classes, measured off the references** (cap height 11 px there):
  | Class | In the reference | P at fontSize 32 | Renders |
  |---|---|---|---|
  | Symbol art — the normal case | 48–60 px, ≈5× cap | **340%** | 108.8 units, 3.2× cap |
  | Jackpot badge / logo — wide and flat | 24 px, ≈2.2× cap | **150%** | 48 units, 1.4× cap |

  `P = 100 × R × capLine_em / 1.5`, where `R` is the ratio measured on the GDD render. **The final
  division is required** — the GDD ratio does not carry over one-to-one and feeding `R` in straight
  comes out too large. The divisor is 1.5, not 2: at 2 a wide badge (`GRAND_JACKPOT` is 1423×128,
  an 11:1 aspect) shrank to 28% of its atlas size and the lettering inside stopped being legible.

  Height holds within a class regardless of the art's proportions: a narrow tall stick of dynamite
  and a wide flat sign render the same height. Apparent size differences between them come from the
  art's own aspect, not from a different tag — only the two classes above differ by tag.

  `voffset` is one constant per font — `capLine × faceScale / (2 × pointSize)`, **5.11em** for the
  project font — and does not change with `P`: `bearingY = 64` centres the sprite on the baseline,
  and this lifts it onto the text's optical middle. Measured: it lands within 0.1 unit of centre.
  Derivation, and the general form `P = 100 × R × capLine_em / 1.5`:
  `skills/cgs-atlas-builder/SKILL.md` → "Формулы".

  Never "fix" a soft, oversized or misaligned sprite by editing the sprite asset — change `P`.
- **Every TextBlock's text opens with a `<line-height=N%>` tag.** A sprite grows the line box of only
  the line it sits on — 125.2 units at `P=340%` against a plain line's 52 — so TMP leaves the leading
  ragged. Measured steps in a real mixed block came out 84.7 / 97.7 / 114.9 depending on whether a
  line and its neighbour carry sprites: a 36% spread that reads as broken layout. `lineSpacing`
  cannot fix it — it adds the same constant to every line and preserves the unevenness. Only the
  `line-height` tag pins all lines to one height.

  **`N` is per block, from the tallest sprite in that block** — not one project-wide constant. At
  `P=340%` the required value is 180%, and forcing that onto a text-only page would inflate it 49%
  for nothing.
  | Tallest sprite in the block | Line box | **N** |
  |---|---|---|
  | none, or badges only (`150%`) | 52.0 / 66.3 | **100%** |
  | symbol art (`340%`) | 125.2 | **180%** |

  `N = 100%` is not a no-op: without any tag the steps go uneven, with it they are all 84.7, which
  already clears a badge's 66.3.

  **Derive `N` by measuring, not by formula.** Set the text, `ForceMeshUpdate()`, read the largest
  `textInfo.lineInfo[i].lineHeight`, then require `52 × N/100 + lineSpacing × 0.001 × fontSize ≥` that
  box, and rewrite the string with the tag. The closed form depends on font descent, `voffset` and
  which of the two exceeds the other, so measuring is both shorter and correct when any of `P`, the
  font size or `lineSpacing` moves. At `P=340%`: 178% is exactly flush, 175% still overlaps by 1.5,
  180% leaves 1.1 of clearance.

  The tag must be in the string **before** height is measured for pagination — it changes
  `preferredHeight` substantially (the jackpot sample went 799.7 → 1126.2 of an available 1385), so
  measuring first and prepending after would mis-page. Expect sprite-heavy pages to split more often
  than they did at smaller `P`.
- **`paragraphSpacing` separates paragraphs; `line-height` must not be asked to.** With every line
  step equal, a wrapped continuation line and a new bullet sit exactly the same distance apart and
  the paragraph structure disappears. `paragraphSpacing` moves only the `\n` boundaries and leaves
  wraps alone: at **500** a paragraph break steps 113.7 against a wrap's 97.7. Units are
  `value × 0.001 × fontSize`, so 500 buys 16 units — half a cap height — for +7% page height.
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
**`Symbols.md` is exhaustive and pre-normalised** — the sweep covers `DARK_*`, `2_*`, `X&Y` combos
and `+N_SPIN` forms, and every name is already in sprite-name form (`' '`→`'_'`, `'+'`→`'PLUS'`).
Use it directly; no manual re-sweep. Check one way only: every token must have a sprite, not the
reverse — the atlas legitimately holds grid symbols that never appear as a token.

### Phase 2 — Normalize & Review
Write `_verstka/blocks.yaml`, `win_tables.yaml`, `review.md`. Run straight through — none of this
waits on a human.

1. **Symbol normalization** — every bracketed token (single/multi-word, variant/prefixed, cards) is a
   symbol needing a sprite. Names arrive already canonical from `Symbols.md`, so there is nothing to
   merge by judgement. **Never collapse two tokens that differ by more than spelling** — a `WIN_*` or
   `2_*` variant is its own sprite even when the rules call it equivalent. If a variant turns out to
   have no art of its own, that is a missing-art finding, not a licence to merge it into the base
   symbol.
2. **Reasoning review** of the rules text — flag contradictions, leftover struck-out fragments and
   copy-paste errors into `review.md`. Keep building; these surface in the final report.
3. **Win-table extraction (vision)** → `win_tables.yaml`. Capture the EXACT row set per symbol —
   never assume a fixed template, since one symbol may pay 5/4/3/2 while its neighbour pays only
   5/4/3.
   **Verify the numbers yourself instead of asking someone to check them.** Vision misreads digits,
   so read each symbol's column a second time, independently, and compare the two passes. Where they
   agree, take the value. Where they disagree, re-read that one cell zoomed before deciding, and
   record the disagreement in `review.md`. Two sanity rules catch most misreads without a second
   look: within one symbol the values must **decrease** as the count decreases, and across a tier the
   ordering of symbols is usually consistent — a value that breaks either is a suspected misread.
4. **Segmentation → `blocks.yaml`** (format: `reference/SCHEMA.md`) — each GDD `## Page N` + header =
   one logical block. Never merge across `## Page N` boundaries. **Do split within one** when a GDD
   page carries two `Title + text` sections: that becomes two logical pages, not one block with
   subblocks — see Core mental model.

### Phase 3 — Map logical blocks → library blocks
For each block in `blocks.yaml`, pick how it gets built (see "Block library reference" below):

| Logical block | How it's built |
|---|---|
| Rules text (any — basic/feature) | Its own page: `TextBlock`(s) in `Body`, + a `ManualSlot` wherever the reference shows a diagram/table/legend (see Phase 5 sizing method) |
| Major (PIC) pays | ALWAYS its own page. `GridPage` — switch cells off to the symbol count |
| Minor (card) pays | ALWAYS its own page, same `GridPage` |
| Substitute / Scatter / Trigger / jackpot-badge panels | `StackPage` — one `SpecialPanel` per panel, 1–3 per page. **Which panels share a page is decided by text volume, not count**: a panel with a dozen rule lines takes a page alone, small ones pair up |
| Standalone image (e.g. paylines diagram) | One `ManualSlot` sized to ~fill `Body` |

Diagrams, reference tables and legends are all **manual slots**: too individual to compose, and a
large share of what a GDD shows in those positions is an authoring artifact (empty labelled frames,
annotated placeholders) rather than shippable art. Reserve, place the magenta placeholder, and report
them at the end of the run for a person to fill.

Write the mapping to `_verstka/block_mapping.md`.

### Phase 4 — Assets (delegates to `cgs-atlas-builder`)
1. `Symbols.md` IS the sprite-name list — hand it straight to `cgs-atlas-builder`. Every symbol goes
   in the atlas regardless of whether it'll be used as a hero `Image` or an inline `<sprite>` tag;
   that choice only affects Phase 5 filling, not this list.
2. **Find the art yourself before asking for any of it.** Search the game's bundle for each symbol.
   Naming conventions vary per game (see Portability), so match by opening candidate files and
   comparing them to the reference screenshot — a filename alone is not evidence. Verify every
   mapping visually, including ones that look obvious.

3. **THE ART GATE — the run stops here, once.** Post the full symbol list with the art file mapped to
   each, and name every symbol you found no art for. Then wait for an answer. This is the only
   moment in the run where the symbol list is final and nothing has been built on it yet, which is
   exactly why the gate sits here and not at the end.
   - Say the count plainly: "N symbols, M without art", and list the M.
   - **Never fabricate art, and never substitute another symbol's art.**
   - If told to proceed anyway, missing symbols keep their magenta `IconSlot` placeholder — but that
     is a decision taken here, out loud, not one you make by carrying on.
   - Finishing a run and only then revealing that some symbols were placeholders throughout is a
     contract failure, not a report.
4. Run `cgs-atlas-builder` — its Unity-side steps are real code now, not re-typed each run: the
   `CGS.PaytableLibrary.PaytableAtlasBuilder` static class (`library/Editor/PaytableAtlasBuilder.cs`,
   same package as everything else in "Block library reference" below) does material creation,
   correct-hash lookup, direct-YAML table writing, final import + the 4-point verification
   (`GetSpriteIndexFromName ≥ 0`, `spriteCharacterTable.Count > 0`, `spriteSheet != null`,
   `material.mainTexture != null` — it throws if any fails), and sub-sprite slicing, all as callable
   methods. See `skills/cgs-atlas-builder/SKILL.md` for the exact call sequence.
5. Sub-sprite slicing (`PaytableAtlasBuilder.SliceIntoSubSprites`) — builds a `TextureImporter`
   sprite sheet from the SAME rectangles already in the TMP `spriteGlyphTable`, giving
   individually-addressable Sprite sub-assets usable in a plain `Image.sprite`, with zero texture
   duplication.

### Phase 5 — Assemble  (CORE — sequential, single Unity instance, inline QA)
> **CRITICAL: ALL prefab stage operations — open, add pages, fill content, register slider, save — MUST
> happen in a SINGLE `execute_code` call.** The prefab stage does NOT persist between separate calls;
> splitting across calls loses everything from the first call. Always end with
> `PrefabUtility.SaveAsPrefabAsset(root, path)` BEFORE `GoToMainStage()`.

1. Clone the matching shell (`Shells/PaytableDialog_{GEL,MCF}.prefab`) to the new
   `PaytableDialog<Game>.prefab` path — never the game's existing donor file.
2. For each logical block, build per its mapped type:

   The page-level blocks are pre-built — **you assemble by instantiating one of them into `Body` and
   switching off what you don't need, not by constructing rows and cells from scratch.** Real sizes
   and layout settings live in `library/BLOCKS.md`.

   **a) Rules text page:** instantiate `Page_1`; in its `Body`, instantiate `TextBlock`(s) (bulleted,
   font and size untouched — the prefab auto-sizes its own height) and a `ManualSlot` wherever the
   reference shows a diagram, table or legend. Multiple pieces are just siblings in display order —
   `Body` is a Vertical Layout Group and stacks them, no manual positioning.
   `ManualSlot` height sizing (general — use for ANY one-off image content): on the reference
   screenshot measure `image_height / body_area_height` (the Body-equivalent area only, not the whole
   card with background) and multiply by `Body`'s height. Recompute per image, never reuse a number
   from another page. Leave the magenta placeholder in — it's reported at the end of the run.

   **b) Major/minor pays page:** instantiate `GridPage`. It already contains `Row_1` and `Row_2` with
   three `GridCell`s each. **Switch cells off for the symbol count** — 6 → 3+3, 5 → 3+2, 4 → 2+2; the
   row re-centres itself. Do not build rows by hand and do not use a `GridLayoutGroup` (with a fixed
   column cap it wraps the remainder onto its own row: 4-at-cap-3 gives 3+1, not 2+2).
   Each `GridCell` is already `IconSlot` over `PayRows` — vertical, symbol image on top. Assign the
   hero symbol into `IconSlot` as an `Image` with its sliced sub-sprite, then fill `PayRows` via
   `CGS.PaytableLibrary.PaytablePayBlock` (`library/Editor/PaytablePayBlock.cs`):
   ```csharp
   countText.text = PaytablePayBlock.FormatCount(new[]{"5","4","3","2"});
   payText.text   = PaytablePayBlock.FormatPay(new[]{"100","60","30","2"});
   // or the int overload: PaytablePayBlock.FormatPay(new[]{100,60,30,2})
   ```
   That call applies the blank-first-Count-line / always-"1 credit"-first-Pay-line / single-color-tag
   rules. Do not format those strings by hand; `library/BLOCKS.md` documents why. The row
   count per symbol comes from `win_tables.yaml` — never a fixed template (a symbol paying 5/4/3/2
   next to one paying only 5/4/3 is normal; bonus suffixes like `"(+N FREE GAMES)"` stay on the
   same line/column as their number, just include them in the string you pass in).

   **c) Specials page (substitute / scatter / trigger / jackpot-badge panels):** instantiate
   `StackPage`. It already contains three `SpecialPanel`s — switch off what you don't need and the
   rest divide the height between them.
   Each `SpecialPanel` is `Label` + `PanelRow` + `OptionalTextBlock`:
   - `Label` — the panel's mini-header ("SUBSTITUTE" / "SCATTER" / "TRIGGER").
   - `PanelRow` — **horizontal**: `ImageContainer_1..4` (three inactive by default; enable as many as
     the panel shows — one panel can carry several wild variants or trigger symbols) then `PayRows`.
     Fill `PayRows` via `PaytablePayBlock` exactly as in 5b — same convention in a panel as in a grid
     cell. **Switch `PayRows` off entirely for a trigger panel**, which carries no payout column.
   - `OptionalTextBlock` — the panel's rules copy; switch it off when there is none. Any sentence
     mentioning a symbol inline ("X AND Y ARE EQUIVALENT.") belongs here as inline
     `<sprite name="X">` tags, never as separate Image objects. Assign
     `TMP_Text.spriteAsset` on every text object that uses them — it is `NULL` by default on
     `TextBlock`, and an unassigned one renders the tag as literal text.

   **Which panels share a page is decided by text volume, not by count.** A panel carrying a dozen
   rule lines cannot share a page with anything; several one- or two-line panels together take less
   room than that one panel alone. A panel that overflows its cell takes a page on its own.

   Padding, spacing and alignment are already set on every prefab — **don't tune them per instance.**
   If one page needs a gap the layout doesn't give, wrap that page's content in its own container
   with its own `VerticalLayoutGroup`; never change `Body`'s shared spacing.
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
   `win_tables.yaml`, nothing clipped.
   **Check the inline sprites specifically**, since they are the easiest thing to get silently wrong:
   are they roughly 3.2× the height of a capital letter (about 1.4× for jackpot badges), and is the
   text sitting at their vertical middle rather than at their top or bottom edge? A sprite the same
   height as the text means the size tag is missing. Check too that no line with a sprite touches its
   neighbour — that means `N` in the `line-height` tag is too small for this block. Fix and re-render until it matches, then complete that page's
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
**Fix — loop until every page passes:**
- **Text overflow** → split into a continuation page. Never cut a bullet mid-way; re-measure and
  split again if it still overflows. **Carry the heading across:** the continuation page gets the
  SAME `Title` as the page it was split from — identical text and colours, since it is still the same
  section — and the same `Header` category. Only the page counter differs.
- **Grid overflow or empty cells** → fewer symbols per page, or switch the grid down to the count you
  actually have (6→3+3, 5→3+2, 4→2+2).

Re-run Phase 6 after any split — slots, numbering and `cards[]` all shift.

**Rendering the QA screenshot — technical setup:**
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
Lives in the block library package (`Blocks/`). **`BLOCKS.md` in that package is the authority** —
it carries the real sizes and layout settings, read off the prefabs. Summary only:

*Page chrome*
- `Page_1.prefab` — base page: `PageParent > {Background, Top{Header, Page}, Frame >
  Inner_Group{Title, Body}, Bottom}`. `Body` starts empty; exactly one page-level block goes in.
  `Title` is the only conditional field. A continuation page REPEATS the previous page's title
  verbatim; switch it off only where a page genuinely has no heading, e.g. a full-page image.

*Page-level — one of these into `Body`*
- `GridPage.prefab` — `Row_1`/`Row_2`, three `GridCell`s each. Switch cells off: 6→3+3, 5→3+2, 4→2+2.
- `StackPage.prefab` — three `SpecialPanel`s; switch off what you don't need, the rest split the height.

*Cell-level*
- `GridCell.prefab` — `IconSlot` over `PayRows`. **Always vertical**, symbol image on top.
- `SpecialPanel.prefab` — `Label` + `PanelRow` + `OptionalTextBlock`, all prefab instances.
- `PanelRow.prefab` — **horizontal**: `ImageContainer_1..4` (3 inactive) + `PayRows`.

*Leaf*
- `PayRows.prefab` — `Count` + `Pay`, two independent multi-line TMP texts (not one shared text),
  coloured via inline `<color>` tags spanning the whole run of lines, not the component's default.
- `IconSlot.prefab` — `Image`, `preserveAspect = true` (the rule for EVERY `Image` you create,
  anywhere). Only Height is set; width follows from aspect. Ships magenta-tinted until assigned.
- `ManualSlot.prefab` — reserved space for one-off art, magenta placeholder, reported at run end.
- `TextBlock.prefab` — bulleted multi-line TMP; auto-height. `spriteAsset` is `NULL` by default —
  assign explicitly whenever using inline `<sprite>` tags. **Three fixed font sizes, never per page:**
  32 for page copy, 40 for `SpecialPanel/Label`, 25 for `SpecialPanel/OptionalTextBlock`.

**`Editor/` (real C# utilities, not prose — call these instead of re-deriving anything):**
- `PaytableGridMath.cs` (`CGS.PaytableLibrary.PaytableGridMath`) — `DistributeRows(n)` tells you how
  many cells to leave enabled per row, and matches the template exactly (6→[3,3], 5→[3,2], 4→[2,2]).
  **Do not call `ComputeCellSize`** — cell and row sizes are baked into `GridPage`/`GridCell`, and a
  computed size overrides the prefab's own with a different number.
- `PaytablePayBlock.cs` (`CGS.PaytableLibrary.PaytablePayBlock`) — `FormatCount(...)`,
  `FormatPay(...)`. The `PayBlock.Count`/`Pay` filling rules above.
- `PaytableAtlasBuilder.cs` (`CGS.PaytableLibrary.PaytableAtlasBuilder`) — the Unity-side half of
  `cgs-atlas-builder` (material, hashes, YAML table writing, import+verify, sub-sprite slicing).
  See `skills/cgs-atlas-builder/SKILL.md`.

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
| Tried to build the symbol grid by hand | Don't — `GridPage` already has `Row_1`/`Row_2` with three `GridCell`s each. Switch cells OFF for the count you need (6→3+3, 5→3+2, 4→2+2); the row re-centres itself. |
| Grid cell/row sizing math giving wrong spacing | Check whether you need it symmetric (50 everywhere, the default) — an asymmetric edge/gap split usually requires a dedicated spacer, not just shrinking rows unevenly; a uniform symmetric budget sidesteps the whole problem. |
| `Body`'s own spacing/padding changed to fix a one-page issue | Don't — it's shared across every page type. Wrap that page's specific content in its own container with its own `VerticalLayoutGroup`. |
| Page repositioned but content jumps to the wrong place | Move the page's ROOT object's `localPosition`, never `PageParent`'s — `PageParent`'s local position is constant regardless of slot. |
| `Symbols.md` missing symbols the rules text clearly uses | No longer expected — the sweep covers `DARK_*`/`2_*`/combo/`+N_SPIN` forms and normalises to sprite names. If something IS missing, that's a real bug in `paytable-pipeline`, not a reason to re-sweep by hand. |
| Feature names not colored consistently | Color comes from the reference screenshot per-game/per-feature, not a hardcoded yellow — check the actual image. |
| Scatter/bonus symbol pay shows only the credit number | Keep the full award in the same Pay-column line, e.g. `"1000 (+10 FREE GAMES)"` — don't drop the suffix or split it into another column. |
| QA screenshot shows nothing / garbage | If it's Scene View, that's a known rendering gap for Canvas UI — use Game View/Play Mode instead. If it's Game View and still blank, check camera Z depth against the content's actual world Z (can be well negative from nested layer offsets). |
| Ad-hoc QA text renders as tiny illegible dots | Default `TextMeshPro` font (`LiberationSans SDF`) at this canvas's scale — assign the project's font asset explicitly. |
| Text fits by RectTransform but visibly runs off the page | `overflowMode=Overflow` spills the MESH past the rect — validate via `textBounds` vs the Frame (Phase 7), then split the page. |
| Multi-word/combo symbol not picked up from GDD text | `Symbols.md` is already normalised to sprite names (`' '`→`'_'`, `'+'`→`'PLUS'`). Use it directly; check one way only — every token needs a sprite, not the reverse. |

## Delegation summary
| Phase | Task | Delegate? | Why |
|---|---|---|---|
| 1 | Extraction | to `paytable-pipeline` | dedicated script |
| 4 | Sprite atlas (+ sub-sprite slicing) | to `cgs-atlas-builder` | dedicated skill |
| 5 | Assemble loop + QA | **No (inline)** | sequential, single Unity instance, tight QA loop |

## Reference docs (in this skill's `reference/`)
- `SCHEMA.md` — **the format of `blocks.yaml` / `win_tables.yaml` / `symbols.yaml`**, the files
  Phase 2 writes and Phase 5 consumes. Read it before writing any of them.
- `legacy_page_taxonomy.md` — historical page-type catalog (24-game corpus). Superseded in structure
  by the dynamic block system above, but still useful background on what page TYPES recur across games.
- `donor_catalog.md` / `guardiansofgiza_catalog.md` — historical GEL/MCF donor structures from the old
  clone-and-mutate model. Kept for historical reference only — do not use as a basis for new work.
- The block library itself (`Shells/`, `Blocks/`, `BLOCKS.md`) lives in the `com.cgs.paytablelibrary`
  package, not in this skill folder.
