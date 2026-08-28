# com.cgs.paytablelibrary — block content-slot manifest

Companion to `paytable-verstka`'s SKILL.md. This file documents WHAT is inside each block prefab and
WHERE to write content into it. It does not repeat the assembly logic (page math, filling rules) —
see the skill for that. Data format: the `paytable-verstka` skill's `reference/SCHEMA.md`.

Values below are read off the actual prefabs, not a spec. If you change a prefab, update this file.

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
        Title  [TMP]                ← bold GDD header, TM handling, colour from reference
        Body                        ← EMPTY. Instantiate one page-level block here.
    Bottom                          (decorative, no text slot)
```
`Body` fixed size: 1410 × 1440. Its `VerticalLayoutGroup`: spacing 0, padding l:50 r:50 t:0 b:0,
`childControlWidth/childForceExpandWidth=true` (children auto-stretch to width),
`childControlHeight=false` (children's height is never touched — always set it yourself).
**Usable width inside Body is therefore 1310.**

`Title` is the only conditional field in the page chrome; Header and page counter are always
present. A **continuation page repeats the previous page's title verbatim** — same text, same
colours — because the reader is still inside the same section. Switch `Title` off only where a page
genuinely has no heading of its own, such as a full-page Line Configuration image.

---

# Page-level blocks — one of these goes into `Body`

## Blocks/GridPage.prefab — symbol grid (majors, minors)
```
GridPage        size 1310 × 1385   [VerticalLayoutGroup: padding 0, spacing 0, UpperLeft, all control/expand ON]
  Row_1         [HorizontalLayoutGroup: padding t25 b25, spacing 50, MiddleCenter, no child control/expand]
    GridCell_1  GridCell_2  GridCell_3
  Row_2         [same settings as Row_1]
    GridCell_1  GridCell_2  GridCell_3
```
Six cells, all active in the prefab. **Switch cells off to get the count you need — the row
re-centres itself:** 6 → 3+3, 5 → 3+2, 4 → 2+2. Rows carry no child control, so cell size is always
set explicitly by the caller.

## Blocks/StackPage.prefab — special-symbol panels (substitute, scatter, trigger…)
```
StackPage       height 1365   [VerticalLayoutGroup: padding 0, spacing 25, UpperLeft, all control/expand ON]
  SpecialPanel_1   SpecialPanel_2   SpecialPanel_3
```
Three panels, all active in the prefab. Switch off what you don't need — with control/expand on,
the remaining panels divide the height between them.

**Which specials share a page is decided by text volume, not by count.** A panel carrying a dozen
rule lines cannot share a page with anything; several one- or two-line panels together take less
room than that one panel alone.

---

# Cell-level blocks

## Blocks/GridCell.prefab — one symbol + its pays, vertical
```
GridCell        size 400 × 638.2   [Image = frame sprite]
                [VerticalLayoutGroup: padding 25 all round, spacing 0, MiddleCenter,
                 childControlWidth=true, forceExpandWidth/Height=true, childControlHeight=false]
  IconSlot
  PayRows
```
**Always vertical — symbol image on top, pay rows underneath.** There is no icon-on-the-left variant
for grid cells; older hand-built paytables mix the two and we normalise that away.

## Blocks/SpecialPanel.prefab — label + icons + pays + rules copy
```
SpecialPanel    [Image = same frame sprite as GridCell]
                [VerticalLayoutGroup: padding 25 all round, spacing 25, MiddleCenter, all control/expand ON]
  Label              [TextBlock instance, fontSize 40]   ← "SUBSTITUTE" / "SCATTER" / "TRIGGER" / …
  PanelRow           [PanelRow instance — see below]
  OptionalTextBlock  [TextBlock instance, fontSize 25]   ← rules copy; switch off when there is none
```
All three children are prefab instances, so edits to `TextBlock` and `PanelRow` propagate here.

## Blocks/PanelRow.prefab — horizontal icon+pays row
```
PanelRow        [HorizontalLayoutGroup: padding 25, spacing 25, MiddleCenter, all control/expand ON]
  ImageContainer_1 … ImageContainer_4    ← IconSlot instances; 3 INACTIVE by default
  PayRows
```
**The icon row is horizontal** — a full-width panel is wide and short, so icons sit left of the pay
rows. Four icon slots are provided because one panel can show several symbols at once (wild
variants, multiple trigger symbols); enable as many as the panel needs.

`PayRows` can be switched off entirely — trigger panels carry no payout column.

---

# Leaf blocks

## Blocks/PayRows.prefab — two-column count/value pay display
```
PayRows   height 322.5  [HorizontalLayoutGroup: padding t25, spacing 50, UpperCenter, no child control/expand]
  Count   [TMP, one multi-line text, fontSize 33.46, ContentSizeFitter = PreferredSize both axes]
  Pay     [TMP, one multi-line text, fontSize 33.46, ContentSizeFitter = PreferredSize both axes]
```
`Pay`'s first line is **ALWAYS** the literal string `"1 credit"` — a universal rule, added by the
pipeline rather than read from the reference (which is why most reference screenshots don't show
it), and never coloured. `Count`'s first line is correspondingly BLANK so the two columns stay
aligned.

Remaining lines in each column are wrapped in ONE `<color>` tag spanning all of them — green for
`Count`, yellow-gold for `Pay` — not the component's own default font colour. Rows run by descending
count. Any bonus suffix on a pay value (e.g. `"(+10 FREE GAMES)"`) stays on the SAME line as its
number, same column — never split out.

**Row count is per symbol, from `win_tables.yaml` — never a fixed template.** One symbol may pay on
5/4/3/2 while its neighbours in the same grid pay only 5/4/3.

## Blocks/IconSlot.prefab — one symbol image
```
IconSlot   height 460
  Image    [preserveAspect=true, inset 32 px from the slot edges]
```
Ships with a placeholder sprite tinted **magenta** so an unassigned symbol is impossible to miss.
Assign the real sprite and clear the tint to white on use. Only height is ever set — width follows
from the aspect ratio. Allow for slight overhang: some symbol art bleeds past the frame.

## Blocks/ManualSlot.prefab — reserved space for one-off art
```
ManualSlot   height 460
  Image      [placeholder sprite, magenta tint]
```
For the one-offs — mechanic diagrams, reference tables, legends. The tool sizes it from the
reference region, leaves the placeholder in, and reports it at the end of the run so a person can
fill it. Magenta so an unfilled slot is impossible to miss on screen.

> Magenta can also occur as a diagram-annotation colour, so any colour-sampling pass must **skip
> placeholder rects**. A placeholder is always a large solid fill and never thin glyphs, so area
> separates them — but the exclusion has to be explicit.

## Blocks/TextBlock.prefab — rules copy
Single TMP object, width 1310, fontSize **32**, left-aligned, `ContentSizeFitter` = PreferredSize on
the vertical axis so height follows the content. Ships with bulleted placeholder text.

**One bullet per paragraph, not per visual line** — a wrapped paragraph carries no bullet on its
continuation. `spriteAsset` is NULL by default: assign the game's TMP Sprite Asset explicitly before
using inline `<sprite name="X">` tags.

`lineSpacing` is 1020 (≈ one extra font size of leading) and `paragraphSpacing` is **500**, which
separates a new bullet from a wrapped continuation line. Every text string written into this block
must **open with a `<line-height=N%>` tag** — without it TMP grows only the lines that contain an
inline sprite and the leading comes out ragged. `N` is per block, from the tallest sprite in it:
**100%** with no sprites or badges only, **180%** with symbol art at `P=340%`. See the skill's
sprite-tag section for how to derive it.

### Font size — three fixed values, never per page
| Where | Size |
|---|---|
| Rules copy on a text page | **32** (the prefab default) |
| `SpecialPanel/Label` | **40** |
| `SpecialPanel/OptionalTextBlock` | **25** |

Body copy on a page is one uniform size — there is no small-print variant. The two panel sizes are a
deliberate exception:
inside a panel the label reads as a heading and the rules copy sits tighter. Those three values live
on the prefab and its instances — never override the size per page.
