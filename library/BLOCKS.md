# com.cgs.paytablelibrary — block content-slot manifest

Companion to `paytable-verstka`'s SKILL.md. This file documents WHAT is inside each block prefab and
WHERE to write content into it. It does not repeat the assembly logic (page math, filling rules) —
see the skill for that.

## Shells/PaytableDialog_GEL.prefab
Root component: `PlayStudios.GEL.UI.SliderDialog`. `cards: []` (empty — fill on assembly). Page
container path: `Body/Cards/anchor/ui`.

## Shells/PaytableDialog_MCF.prefab
Root component: `KonamiPortraitPaytable` (`Assets/MCF/Scripts/Dialog/KonamiPortraitPaytable.cs`).
`cards: []` (empty — fill on assembly).

## Blocks/Page_1.prefab — base page template
```
Page_1                              ← ROOT, move THIS for slider slot positioning (localPosition.x)
  PageParent                        ← never move this — always the same local position
    Background                     (decorative, no text slot)
    Top
      Header   [TMP]                ← fixed category label from the reference screenshot
      Page     [TMP]                ← "Page X/N", fill in Phase 6
    Frame
      Inner_Group
        Title  [TMP]                ← bold GDD header, TM handling, color from reference
        Body                        ← EMPTY. Instantiate Text/ImageContainer/GoldBoxRow/GoldBox
                                       /any content block as children here, in display order.
    Bottom                          (decorative, no text slot)
```
`Body` fixed size: 1410 (width) × 1440 (height). Its `VerticalLayoutGroup`: spacing 0, padding
l:50 r:50 t:0 b:0, `childControlWidth=true/childForceExpandWidth=true` (children auto-stretch to
fill width), `childControlHeight=false` (children's height is never touched — always set it
yourself).

## Blocks/Text.prefab
Single TMP text object, root name `Text`. Bulleted placeholder content, auto-grows/shrinks height
to fit (`ContentSizeFitter`). `spriteAsset` is `NULL` by default — assign the game's TMP Sprite
Asset explicitly before using inline `<sprite name="X">` tags. Font is fixed on the prefab — never
override per-page.

## Blocks/ImageContainer.prefab
```
ImageContainer
  Image   [UI Image, preserveAspect=true, placeholder sprite]
```
Only `ImageContainer`'s own Height needs setting (per the fraction-of-Body method in the skill);
width follows automatically from the image's aspect ratio.

## Blocks/GoldBox.prefab — dual-purpose: grid cell AND standalone panel
Root: `Image` (bordered panel background) + `VerticalLayoutGroup` (`childAlignment=MiddleCenter`,
padding 0/0/0/0 by default, spacing 0). No children by default — content instantiated per use:
- **As a PIC/Card grid cell:** children = `ImageContainer` (height = cell height / 2) +
  `PayBlock` (height = cell height / 2). Padding stays 0 — the grid-cell sizing formulas assume it.
- **As a Substitute/Scatter/Trigger/jackpot panel:** children = an internal label `Text` (e.g.
  "SCATTER"), a `PayRow` (custom `HorizontalLayoutGroup` container holding hero `ImageContainer`(s) +
  a `PayBlock`), and optionally more `Text` blocks below. **Set padding to 50 on all four sides on
  THIS SPECIFIC INSTANCE only** — never edit the shared prefab's default for this, it would break the
  grid-cell math above.

## Blocks/GoldBoxRow.prefab — one row of a PIC/Card grid
Root: `HorizontalLayoutGroup`, spacing 50, padding l:0 r:0 t:25 b:25, `childControlWidth=false`,
`childControlHeight=false` — cell sizes are always set explicitly by the caller, never auto-derived.
Width auto-expands to fill `Body`'s available width (1310) via `Body`'s own force-expand; height must
be set explicitly to `height_cell + 50` per row.

## Blocks/PayBlock.prefab — two-column count/value pay display
```
PayBlock  [HorizontalLayoutGroup]
  Count   [TMP, one multi-line text]   ← e.g. "<color=green>\n5\n4\n3</color>"
  Pay     [TMP, one multi-line text]   ← e.g. "1 credit\n<color=yellow>100\n60\n30</color>\n"
```
`Count`'s first line is always BLANK (aligns with Pay's "1 credit" label line). `Pay`'s first line is
ALWAYS the literal string `"1 credit"` (we add it ourselves — see skill's Core mental model), never
colored. Remaining lines in each column are wrapped in ONE `<color>` tag spanning all of them (green
for Count, yellow for Pay) — not the component's own default font color. Any bonus suffix on a pay
value (e.g. `"(+10 FREE GAMES)"`) stays on the SAME line as its number, same column — never split out.
Row count is per-symbol from `win_tables.yaml`, never a fixed template.
