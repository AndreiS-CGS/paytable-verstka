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
  Atlassian API token at `~/.confluence_pat` plus `CONFLUENCE_EMAIL`, both set through the Unity
  window. Browser cookie auth has been removed — a token fetches text, attachments and images
  alike. A **401** means the token is expired or revoked; a **403** means `CONFLUENCE_EMAIL` is
  missing. See `paytable-pipeline`.
- **Art:** locate per game inside the bundle — naming conventions VARY per game (some use
  `S_Symbol_<NAME>`/`HP_*`, others use letter-coded names like `S_PicA`/`S_CardK`). Never assume one
  convention; open candidate files and visually confirm before mapping. Never a personal Desktop folder.
- **The block library itself is a separate, shared asset** — see "Library architecture" below. Never
  hardcode its path as literally `Assets/PaytableLibrary/`; resolve by package name.

## Context (CGS/Konami)
- TWO slot systems, BOTH supported and BOTH alive (the system is PER-GAME, detect don't assume):
  **GEL** (root `PlayStudios.GameEngineLua.UI.SliderDialog` — note the namespace does NOT match
  its `GEL/UI` folder; bundles under `Assets/Bundles/_gel/_games/<slot>/`)
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
  └── library/                   (the Unity Package, com.cgs.paytablelibrary)
      └── Skills~/
          ├── paytable-pipeline/    (extraction — its own SKILL.md + scripts/)
          ├── cgs-atlas-builder/    (sprite atlas — its own SKILL.md + scripts/)
          └── paytable-verstka/     (this skill's own SKILL.md + reference/)
  ```
  The skills live INSIDE the package so that pulling the package delivers them too. Unity ignores
  any folder whose name ends in `~`, so `Skills~/` never becomes Unity assets and needs no `.meta`
  files, while git and the Package Manager still ship it.

  Each skill is installed independently into wherever Claude Code loads skills from, so all three
  stay separately invocable ("скачай пейтейбл" still only runs extraction) even though they share
  one repo and one library.

  A consuming Unity project references the package one of three ways, and they behave differently —
  never assume which one is active, and never assume the package is writable:
  ```json
  "com.cgs.paytablelibrary": "https://github.com/AndreiS-CGS/paytable-verstka.git?path=library#main"
  "com.cgs.paytablelibrary": "file:/absolute/path/to/paytable-verstka/library"
  ```
  The **git** form is what the team uses: it resolves READ-ONLY under `Library/PackageCache/` and is
  wiped and re-fetched on every re-resolve, so nothing may be written there and nothing may be
  symlinked into it. The **`file:`** form resolves live from a local clone and is writable — the
  form to use when iterating on the library itself. An embedded copy or symlink inside `Packages/`
  behaves like `file:`. Internal layout:
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
- **Page order = the GDD's `## Page N` headings read top to bottom, and nothing else.** The
  `PAGE X/Y` counter printed inside a screenshot is part of that same stale render — informative,
  never authoritative. Neither are attachment filenames (`Help_NN_*`), which are legacy asset names.
  Extraction already numbers `PageN.jpg` by GDD order, so the files line up as-is.

  Confirmed as a hard rule, so do not weigh the counter against the text and do not ask:
  - The counter is measurably from a different paytable. Its `/Y` total has been seen to disagree
    with the built page count by a wide margin — a different page inventory entirely, so the
    numbering cannot transfer. Compare the two before trusting any of it.
  - There is no house convention to fall back on. Across 60 shipped paytable prefabs, 42 put the
    rules pages first and 18 put Pay Table first. Neither order is "how we do it", which is exactly
    why the GDD text is the only authority.
  - Where a screenshot and the GDD text disagree about anything structural, the text wins — the same
    call already made for portrait/landscape, where cropped screenshots forced Summary to be
    authoritative.
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
  Without a tag `P` is 100, which renders the sprite 0.93 cap-heights tall — 3.4× smaller than symbol
  art should be — and sitting on the baseline instead of centred on the line. Emit:
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
  comes out too large. The divisor is 1.5, not 2: at 2 a wide jackpot logo — these run to about 11:1
  once normalised to the atlas height — shrank to under a third of its atlas size and the lettering
  inside stopped being legible.

  **Both classes take the same 1.5 — settled on rendered pages, do not re-derive it.** Divisor 2 was
  tried first and rejected on legibility; 1.5 was then checked on a badge page and a symbol-art page
  together and accepted for both. `R` is the only thing to measure per game.

  Height holds within a class regardless of the art's proportions: a narrow tall stick of dynamite
  and a wide flat sign render the same height. Apparent size differences between them come from the
  art's own aspect, not from a different tag — only the two classes above differ by tag.

  `voffset` is one constant per font — `capLine × faceScale / (2 × pointSize)`, **5.11em** for the
  project font — and does not change with `P`: `bearingY = 64` centres the sprite on the baseline,
  and this lifts it onto the text's optical middle. Measured lift is 16.4 units, identical at `P=150`
  and `P=340`, leaving the sprite 0.8 units below the centre of the capitals — 2% of cap height. For
  comparison, the same tag placed *inside* `size` lifts 40.9 (23.7 too high), and `0.51em` outside
  lifts 1.6 (15.5 too low).

  > When checking this yourself, do not measure a sprite's offset against its own
  > `characterInfo[].baseLine` — TMP has already folded the `voffset` into that field, so the
  > difference cancels and every variant looks identical. Take the baseline from a plain capital on
  > the same line.
  Derivation, and the general form `P = 100 × R × capLine_em / 1.5`:
  the `cgs-atlas-builder` skill's SKILL.md → "Формулы".

  Never "fix" a soft, oversized or misaligned sprite by editing the sprite asset — change `P`.
- **Every TextBlock's text opens with its OWN `<line-height=N%>` tag**, derived from the tallest
  sprite in that paragraph — one object per paragraph, so the tag is per paragraph too. A sprite grows the line box of only
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
   **Read its root component ONLY** → GEL (`PlayStudios.GameEngineLua.UI.SliderDialog`) or MCF
   (`KonamiPortraitPaytable`/`KonamiPaytable`, which derive from a DIFFERENT, un-namespaced
   `SliderDialog` — see Phase 5 step 4). Note its
   possibly-stale filename as a curiosity, not a target — never open/edit/rename/delete it.
4. Pick the matching shell: `Shells/PaytableDialog_{GEL,MCF}.prefab` from the block library package.
5. **Unity availability gate.** If unityMCP is not connected, don't block the whole run — proceed
   through Phases 1–4 (filesystem-only work), then stop before Phase 5/8 and report status.
6. Plan to create the real output as a brand-new file: `PaytableDialog<CorrectGameName>.prefab`, next
   to (never overwriting) the existing donor file. Updating whatever config/reference should point at
   the new prefab is out of scope for this skill (frontend/build owns that).

### Phase 1 — Extract (delegates to `paytable-pipeline`)
Produces `<Game> Paytable.md`, `… Clean.md`, `… Symbols.md`, `Paytable Images/PageN.jpg`. Uses the
API token. A "Pay Table Pages section not found" / tiny-HTML-length result on
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
   methods. See the `cgs-atlas-builder` skill for the exact call sequence.
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

   **a) Rules text page:** instantiate `Page_1`; in its `Body`, instantiate **one `TextBlock` per
   paragraph** (bulleted, font and size untouched — the prefab auto-sizes its own height) and a
   `ManualSlot` wherever the reference shows a diagram, table or legend. Multiple pieces are just
   siblings in display order — `Body` is a Vertical Layout Group and stacks them, no manual
   positioning. Name them `TextBlock_1..N` so the reading order is legible in the hierarchy.

   **One paragraph = one text object.** Not one object holding every paragraph of the page. `Body`
   stacks them either way, so the rendering is the same, and the difference shows up the moment
   anything has to change:
   - **Splitting an overflowing page becomes moving objects, not cutting a string** on a paragraph
     boundary and re-measuring. That split is not rare — it happened twice in one game.
   - **Moving a paragraph between pages by hand** is a drag in the hierarchy. With one object per
     page it is a copy-paste of substring, done wrong easily.
   - **The `line-height` tag gets tighter.** `N` is derived from the tallest sprite in the block, so
     one object spanning the whole page forces every paragraph to the height demanded by its single
     tallest sprite. Per paragraph, a sprite-free paragraph keeps a plain line height.
   - Per-paragraph overflow measurement tells you exactly where to cut, instead of "somewhere in
     this block".

   **Mind the paragraph gap — it does NOT come along for free.** Today the space between paragraphs
   is `paragraphSpacing = 500` on the TMP component, which TMP applies only at a paragraph break
   *inside* one text object. Split the paragraphs into separate objects and that setting goes inert:
   there are no internal breaks left, and `Body`'s Vertical Layout Group ships `spacing = 0`, so the
   paragraphs will sit flush against each other.

   The gap has to move to the container's `spacing`. **Measure the value, do not derive it.** The
   arithmetic is `paragraphSpacing × fontSize × 0.01 × orthographicMultiplier`, and
   `orthographicMultiplier` is 1 or 0.1 depending on `m_isOrthographic` — which is serialized as `0`
   in every prefab but set to `true` in `TextMeshProUGUI.Awake`. That gives 16 or 160, and neither
   reconciles cleanly with the line steps measured in this skill, so the honest answer is to render
   one page both ways and match the gap by eye against the existing single-object pages. Do this
   once, on the first page, then reuse the number for the run.
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
     `<color=white><size=20><voffset=600> TM</color></size></voffset>` — note the space **inside**
     the tags, immediately before `TM`. Outside them it renders at title size and reads as a word
     break; inside it inherits `size=20` and gives the superscript the hair of air it needs not to
     collide with the last letter. (Delete any leftover `TrademarkText`/`TrademarkText_1` child
     objects first — don't split the string on `™`.) **Color
     is taken from the reference screenshot of THIS game** (never hardcoded yellow) — when the Title
     combines 2+ feature names, color each piece with its own established `<color>` tag, connecting
     words neutral/white.
4. **Page positioning in the slider:** move the page's ROOT object
   (`transform.localPosition.x = slot × offset`, slot 0 → x=0) — never touch `PageParent`'s own local
   position inside the page; it stays identical regardless of slot.

   **Read `offset` off the shell you instantiated. Never pick a number, and never take it from a
   `class SliderDialog` you found by grep** — there are TWO different classes with that name in this
   project, and they disagree about both the spelling and the default:

   | System | Base class | Field | Default | Serialized? |
   |---|---|---|---|---|
   | GEL | `PlayStudios.GameEngineLua.UI.SliderDialog` (package `com.playstudios.gel`) | `public float cardOffset` | 2500 | yes — read it off the component |
   | MCF | plain `SliderDialog` in `Assets/Scripts/Widgets/SliderDialog.cs`, no namespace | `protected float CARD_OFFSET` | **1750** | **no** — not `[SerializeField]`, so the component cannot show it |

   Note the GEL namespace: the folder is `PlayStudios/GEL/UI`, the namespace is
   `PlayStudios.GameEngineLua.UI`. They do not match, and the shell's root script GUID
   (`3c0d24c881e494f56bc39a8e57101f27`) is the way to settle it.

   This is where the 1750 came from in the failure below — it was not invented, it was read off the
   wrong class. A run on a GEL game grepped for `CARD_OFFSET`, landed in
   `Assets/Scripts/Widgets/SliderDialog.cs`, took its `1750` default, and laid 46 pages out 1750
   apart while the actual component said 2500. Nothing errored, and every page rendered correctly on
   its own; they simply did not line up when swiped.

   So: resolve the field **through the instantiated component**, not through a source file. On MCF
   `CARD_OFFSET` is not serialized at all, so there is nothing to read and 1750 is the real value
   unless a subclass assigns it in `Awake` — several do (`CARD_OFFSET = winnerObjectWidth`), which is
   another reason not to trust a grepped literal.
5. **QA render inline** after each page (see Phase 7 for the technical render setup) — compare to the
   reference: full title, every symbol resolved (not literal `<sprite…>` text), numbers match
   `win_tables.yaml`, nothing clipped.

   **A wrong sprite name does not show up as literal text — it shows up as the wrong picture.** TMP
   substitutes a fallback glyph for a name it cannot find, silently and with nothing in the console,
   so the page looks populated and only a reader who knows the symbols can tell. One run rendered the
   same fallback icon thirteen times on a single page this way. Eyeballing cannot be the check, so
   compare the two sets mechanically before trusting any render:

   ```
   names used in text  =  every <sprite name="X"> across all page TextBlocks
   names available     =  spriteCharacterTable of the game's TMP Sprite Asset
   used - available must be EMPTY
   ```

   One direction only: the atlas legitimately holds sprites no rules text mentions (grid symbols come
   from the Pay Grid), so `available - used` being non-empty is normal.
   **Check the inline sprites specifically**, since they are the easiest thing to get silently wrong:
   are they roughly 3.2× the height of a capital letter (about 1.4× for jackpot badges), and is the
   text sitting at their vertical middle rather than at their top or bottom edge? A sprite the same
   height as the text means the size tag is missing. Check too that no line with a sprite touches its
   neighbour — that means `N` in the `line-height` tag is too small for this block. Fix and re-render until it matches, then complete that page's
   task before moving to the next.

### Phase 6 — Finalize
1. Order pages by slider slot (root `localPosition.x`, per 5.4). Rename page GameObjects `Page_1..Page_N`.
2. Set `Page X / N` labels with the final N.
3. Register the slider on the dialog root: `cards[]` = the pages in order. Leave the offset field
   (`cardOffset` on GEL, `CARD_OFFSET` on MCF) at whatever the shell already carries — it drives
   paging AND the indicator dots, and the page positions in 5.4 were derived from it.

   **Then verify the geometry, because nothing else does.** For every page `i`, assert
   `root.localPosition.x == i × offset`, and that `offset` still matches the field on the component.
   The per-page render QA cannot catch a mismatch: each page looks perfect alone, and only swiping
   shows they are spaced wrongly.
4. Verify sprite hashes (`GetSpriteIndexFromName ≥ 0`). Full-pass QA: render every page vs the GDD set.
5. Move the whole new prefab's root AND every child object, recursively, onto the **"Dialog"** layer.
6. **Assert the prefab references nothing outside its own bundle.** A paytable that quietly depends on
   another game's bundle still builds, still renders, and still passes every visual check — the wrong
   atlas is a valid atlas. Only a dependency audit finds it.

   The concrete failure: the library's `PayRows` block shipped `Count`/`Pay` with
   `Goat_PaytableSpriteAsset` assigned, so **41 TMP components** in one finished game pointed at the
   `crazystuffedcoinsgoat` bundle. Assembly had set `spriteAsset` only on the 7 texts carrying inline
   sprite tags and left the rest on the block default. Fixed in the library on 2026-09-02, but audit
   anyway — the same thing happens the moment anyone assigns an asset into a block while debugging.

   **Audit by intersection, not by name.** Collect every GUID the prefab references, then intersect
   that set against the `.meta` files of the other game bundles. Checking the three asset names you
   happen to suspect proves nothing: here the texture and material were never referenced directly —
   they arrived transitively through the sprite asset's own `m_Material` and `spriteSheet`, so a
   name-based check on them returns a clean 0 while the dependency is fully intact.

   Allowed reference targets: this game's own bundle, the `com.cgs.paytablelibrary` package,
   `Bundles/_gel/_common/*`, and `ExternalTextures/Bundles/_games/commonkonami/*` (shared Konami
   paytable chrome — `frame.png`, `goldBox.png`). Anything under another `_games/<slot>/` is a defect.

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

**Rebuild every layout group parents-first, and measure from `Inner_Group` — never from `Body`.**
`Body` in `Page_1` has `sizeDelta.x = 0` with anchors `(0,0)/(0,0)`: its width is literally zero
until `Inner_Group`'s layout group assigns it. Rebuild starting from `Body` — or from `PageParent`,
which has no layout controller at all — and the width stays 0, every word wraps onto its own line,
and heights come back roughly 10x too large. That reads exactly like catastrophic overflow and is
pure measurement artefact: splitting on it produces a dozen phantom pages. If a page reports absurd
overflow, re-measure before you split.

**The page-level check is necessary and not sufficient.** It compares content against the page
`Frame`, so it cannot see two panels overlapping *inside* a `StackPage` — that overlap sits entirely
within the `Frame` and reports clean. Check three things separately, and treat any of them as a
failure:
1. text mesh past the page `Frame` (above),
2. content past its own panel/cell edge,
3. **overlapping siblings inside a panel or cell.**

Item 3 has already caught a real defect the page check missed: two panels sharing one `StackPage`
overlapped by 81 and 113 units, invisible to every `Frame`-based measurement, and the fix was one
panel per page.

**Fix — loop until every page passes:**
- **Text overflow** → split into a continuation page. Never cut a bullet mid-way; re-measure and
  split again if it still overflows. **Carry the heading across:** the continuation page gets the
  SAME `Title` as the page it was split from — identical text and colours, since it is still the same
  section — and the same `Header` category. Only the page counter differs.
- **Grid overflow or empty cells** → fewer symbols per page, or switch the grid down to the count you
  actually have (6→3+3, 5→3+2, 4→2+2).

Re-run Phase 6 after any split — slots, numbering and `cards[]` all shift.

**Freeze the layout once the loop has converged. This is MANDATORY, not an optimisation** —
without it the prefab is correct in the editor and collapses the first time it is shown at runtime.

`ContentSizeFitter` (PreferredSize) asks TMP for `preferredHeight`, and **TMP returns 0 until it has
generated its text mesh.** The first layout pass after a page is enabled therefore measures 0, the
parent `VerticalLayoutGroup` places the block against its top padding, and nothing schedules a second
pass. Symptom: every `TextBlock` sits at `Pos Y = -25` in Game Mode against `-336.995` in the prefab,
and toggling the object off and on by hand "fixes" it. The prefab value was never wrong — the runtime
measurement was. This is inherent to the fitter-driven block design, so **every game hits it.**

**Do not hand-roll this. Call the library:**
```csharp
var report = CGS.PaytableLibrary.PaytableLayoutBake.BakeAll(dialogRoot, out var perPage);
// then, per page, the gate:
string problems = CGS.PaytableLibrary.PaytableLayoutBake.Verify(pageRoot);
```
`Verify` returns `""` when nothing under the root can re-measure itself at runtime, and a list of
offenders otherwise. **A non-empty result is a blocking failure of the run, not a note** — a prefab
that fails it looks completely correct in the editor.

The procedure has three steps and two traps, each trap producing a plausible-looking wrong answer
rather than an error: baking while `childForceExpandHeight` is on **diverges** (measured
130 → 264 → 309 on one label across three runs), and clearing that flag is not enough because
`SpecialPanel` also hands out spare height through `flexibleHeight = 1`. That is exactly why this is
code and not a paragraph — `Bake` is idempotent by construction and touches only the texts a fitter
actually sizes, leaving alone the values the library sets on purpose (`PanelRow`'s own
`flexibleHeight`, the baked grid cell sizes). Read the class's header comment before changing
anything about it.

**Re-bake whenever what the height derives from changes:** text, font asset or its metrics,
`fontSize`/`lineSpacing`/`paragraphSpacing`, or a sprite asset swapped for one with different glyph
heights.

**Confirm idempotence by comparing the baked NUMBERS across two runs, not by reading `MaxDelta`.**
`MaxDelta` is the pass-to-pass settle delta inside a single run, so it reads ~0 on any run that
succeeded and proves nothing about two runs agreeing — it would not catch the divergence it looks
like it is guarding against. Snapshot each owned text's rect size and `preferredWidth`/`Height`, run
`BakeAll` again, and diff: acceptance is drift exactly 0, `Verify` empty, and both runs converged.

**Switched-off blocks are skipped, deliberately.** The library's layout vocabulary is "switch off
what you don't need" — unused `GridCell_3`, `SpecialPanel_2`/`_3`, `ImageContainer_2..4`, `PayRows`
on a trigger panel, `Title` on a full-page image. Nothing under them is laid out, so they measure 0,
and baking that 0 while disabling the fitter is worse than leaving them alone: the page looks
identical today, and the moment someone enables that panel its text is pinned to zero height with
nothing left to re-measure it. `Bake` reports the count as `skipped (switched off)`. Do not read a
non-zero number there as work left undone.

**One case baking cannot cover:** a page whose text is rewritten at runtime, e.g.
`paragraph.text.Replace("$$$", value)`. A baked height is a height for one specific string. Leave
such a page's fitter live and force a second layout pass at runtime instead. Only one game in the
project does this today and no current prefab carries the placeholder.

**Rendering the QA screenshot — technical setup:**
- **Frame the whole page, never the content you just built.** Page chrome lives OUTSIDE `Frame`:
  `PageParent` is 1690x2100, `Frame` is only 1410x1740, and `Top` (which holds `Header` and the
  `Page N/M` counter) is anchored to `PageParent`'s TOP edge — above `Frame` entirely, as is `Bottom`
  below it. `Title` is a sibling of `Body` inside `Inner_Group`, so it is above `Body` too. A camera
  framed on `Body` — the natural choice, since that is where the content went — silently crops
  `Header`, the page counter, `Title` and `Bottom`. This has already produced a full QA pass on
  pages whose header and counter nobody ever saw.

  Do not name an object to frame. Derive the frame from the union of everything under the page root,
  so nothing can be outside it by construction:
  ```csharp
  var corners = new Vector3[4];
  Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = -min;
  foreach (var rt in pageRoot.GetComponentsInChildren<RectTransform>(true))
  {
      if (!rt.gameObject.activeInHierarchy) continue;   // switched-off cells are deliberate
      rt.GetWorldCorners(corners);
      foreach (var c in corners) { min = Vector2.Min(min, c); max = Vector2.Max(max, c); }
  }
  var centre = (min + max) * 0.5f;
  cam.orthographic = true;
  cam.orthographicSize = Mathf.Max((max.y - min.y) * 0.5f,
                                   (max.x - min.x) * 0.5f / cam.aspect) * 1.06f;  // 6% air
  cam.transform.position = new Vector3(centre.x, centre.y, -60f);
  ```
- **Then prove nothing was cropped, before reading the screenshot.** The union fits by construction,
  so what remains to check is that the chrome was IN the union — an inactive or missing `Header` gives
  a picture that looks complete and is not. Assert per page, and treat a failure as a failed render
  rather than a failed page:
  ```csharp
  foreach (var name in new[] { "Header", "Page", "Title" })
  {
      Transform t = null;
      // A plain loop, not Linq: this may run through execute_code, which compiles as a method
      // body with no using directives.
      foreach (var x in pageRoot.GetComponentsInChildren<Transform>(true))
          if (x.name == name) { t = x; break; }
      if (t == null || !t.gameObject.activeInHierarchy)
          throw new System.Exception($"{pageRoot.name}: {name} missing or inactive — the render " +
                                     "cannot show it, so this page is not QA'd.");
      var rt = (RectTransform)t; rt.GetWorldCorners(corners);
      foreach (var c in corners)
      {
          var v = cam.WorldToViewportPoint(c);
          if (v.x < 0 || v.x > 1 || v.y < 0 || v.y > 1)
              throw new System.Exception($"{pageRoot.name}: {name} is outside the camera frame.");
      }
  }
  ```
  `Title` is legitimately switched off on a full-page-image page — allow that case explicitly rather
  than by letting the check pass silently.
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
  See the `cgs-atlas-builder` skill.
- `PaytableLayoutBake.cs` (`CGS.PaytableLibrary.PaytableLayoutBake`) — `Bake` / `BakeAll` freeze the
  text layout so it cannot collapse at runtime; `Verify` is the gate that says whether it held. See
  Phase 7. Editor-only assembly, so nothing ships in the player.

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
| QA render is missing `Header` / the page counter / `Title` | Camera framed on `Body` or `Frame`. `Top` and `Bottom` are anchored to `PageParent`'s edges, OUTSIDE `Frame`; `Title` sits above `Body` inside `Inner_Group`. Frame the union of everything under the page root, then assert the chrome is inside the viewport (Phase 7). |
| Ad-hoc QA text renders as tiny illegible dots | Default `TextMeshPro` font (`LiberationSans SDF`) at this canvas's scale — assign the project's font asset explicitly. |
| Text fits by RectTransform but visibly runs off the page | `overflowMode=Overflow` spills the MESH past the rect — validate via `textBounds` vs the Frame (Phase 7), then split the page. |
| Every page reports huge overflow; heights ~10x too big | Layout rebuilt from `Body` or `PageParent`. `Body`'s width is 0 until `Inner_Group`'s layout group sets it, so every word wrapped. Rebuild parents-first, measure again (Phase 7). |
| Everything correct in the editor, `TextBlock`s jump to `Pos Y = -25` in Game Mode | `ContentSizeFitter` measured TMP's `preferredHeight` before the mesh existed, so it got 0. Toggling the object fixes it only for that session. Bake the layout (Phase 7). |
| Baked heights grow every time the bake is run (130 → 264 → 309) | `childForceExpandHeight` is on, so the group inflates the child and the next bake freezes the inflated value. Reset fitters and clear baked values before measuring. |
| Bake done, `childForceExpandHeight` off, sizes still not frozen | `SpecialPanel`'s `Label`/`OptionalTextBlock` carry `flexibleHeight = 1`; the group gives them spare height regardless of the flag. Clear `flexible` to `-1`. |
| Prefab depends on another game's bundle | A block prefab had a `spriteAsset` assigned. Library blocks must ship `spriteAsset = NULL`; assign the game's asset per text at assembly. |
| Paragraphs sit flush with no gap between them | The gap used to come from `paragraphSpacing` inside one text object; with one paragraph per object that setting is inert. Put the gap on the container's `spacing` — measured, not derived (Phase 5a). |
| Panels visibly overlap but the overflow check is clean | The check measures against the page `Frame`; an overlap inside a `StackPage` is within the Frame. Check siblings inside the panel too (Phase 7). |
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
- The block library itself (`Shells/`, `Blocks/`, `BLOCKS.md`) lives in the `com.cgs.paytablelibrary`
  package, not in this skill folder.

The three donor/taxonomy catalogues that used to sit here were deleted once the block library
replaced the clone-and-mutate model — their block vocabularies (`GoldBox_Special`, `PayBox1`,
`SymbolBox_<NAME>`, `line1`, `Frame_1`) described other games' prefabs and had started to mislead.
Recover them from git history if ever needed. What was still true is kept below.

## Salvaged from the donor corpus
Surveyed 24 recent games, ~380 GDD paytable images, across every mechanic family in the catalogue
(lines, ways, hold&spin/bank, link-jackpot, cluster and others).

**There is one canonical paytable structure.** The page TYPES are the same ~10 in every family; a
different mechanic shows up as different feature-rules TEXT, not as a new page layout. This is why a
small finite block library covers essentially every game — and why an unfamiliar mechanic is not a
reason to invent a block.

**Three blocks that look necessary and are not.** Each was proposed, then disproved against the
corpus:
- *Numeric jackpot ladder* — does not exist. "Available Jackpots" is badges stacked in a panel with
  no numbers, because jackpot values scale with bet and are never printed.
- *Hold&spin coin-value grid* — not a block. Coin prizes are ordinary feature-rules text, and the
  numbers themselves are baked into the coin symbol ART.
- *Feature mini-grid* (ingot / collection) — a plain two-column table, just with more rows.

**Art sourcing priority.** `HP_` stands for Help Page: purpose-built paytable renders (`HP_Wild`,
`HP_Scatter`, `HP_JPGrand`) and the preferred source for the sprite atlas. PIC and card symbols
usually have no `HP_` render — fall back to the reel-symbol static frames (`S_PicA`, `S_CardK`,
`S_Symbol_<NAME>` and similar). Conventions vary per game, so confirm visually either way.

**The calibrated constants are font-specific.** `voffset=5.11em`, the 52-unit line base, the 34.3-unit
cap height and therefore every `line-height` value assume `Comfortaa-Bold Edited SDF`
(guid `354741b32dc5f4b0fa48c8b1e39ea7e8`), which is what the library's `TextBlock` uses. A different
font invalidates all of them — re-derive rather than carry them over.

**Landscape is not covered.** Both shells host the same 1410×1440 portrait page blueprint; no
landscape page geometry exists in the library. Deferred until a landscape game actually needs it.
