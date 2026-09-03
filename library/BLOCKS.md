# com.cgs.paytablelibrary — block content-slot manifest

Companion to `paytable-verstka`'s SKILL.md. This file documents WHAT is inside each block prefab and
WHERE to write content into it. It does not repeat the assembly logic (page math, filling rules) —
see the skill for that. Data format: the `paytable-verstka` skill's `reference/SCHEMA.md`.

Values below are read off the actual prefabs, not a spec. If you change a prefab, update this file.

## Shells/PaytableDialog_GEL.prefab
Root component: `PlayStudios.GameEngineLua.UI.SliderDialog` (script GUID
`3c0d24c881e494f56bc39a8e57101f27`) — the namespace does NOT match its `GEL/UI` folder, and a
second, unrelated `SliderDialog` with no namespace exists in `Assets/Scripts/Widgets/`.
`cards: []` (empty — fill on assembly). Page
container path: `Body/Cards/anchor/ui`.

**Page spacing comes from `cardOffset` on that component (2500).** Page `i` sits at
`localPosition.x = i × cardOffset`. Read the field; never hardcode the number — MCF spells the same
field `CARD_OFFSET` **on a different, un-namespaced `SliderDialog` class** whose default is 1750 and
which is not `[SerializeField]`. That is exactly how one run laid a GEL game's pages out 1750 apart:
it grepped `CARD_OFFSET` and read the wrong class's default rather than the component in front of it.

## Shells/PaytableDialog_MCF.prefab
Root component: `KonamiPortraitPaytable` (`Assets/MCF/Scripts/Dialog/KonamiPortraitPaytable.cs`).
`cards: []` (empty — fill on assembly). Page spacing field is `CARD_OFFSET` (2500) — note the
different spelling from GEL's `cardOffset`.

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
**Page geometry — the chrome is OUTSIDE `Frame`.** `PageParent` is 1690 × 2100; `Frame` is only
1410 × 1740 and sits at y +24. `Top` (h 122, holding `Header` 1000 × 90 and `Page` 400 × 80) is
anchored to `PageParent`'s TOP edge, above `Frame`; `Bottom` (h 182) to the bottom edge, below it.
`Title` (h 300) is a sibling of `Body` inside `Inner_Group`, so it is above `Body`.

Consequence for any measuring or rendering code: **`Body`, `Frame` and `Inner_Group` are all smaller
than the page.** A QA camera framed on `Body` crops `Header`, the page counter, `Title` and `Bottom`
— and the screenshot still looks like a complete page. Frame on the union of everything under the
`Page_1` root instead.

Also note `Body`'s `sizeDelta.x` is **0** with anchors (0,0)/(0,0): its width is assigned at runtime
by `Inner_Group`'s layout group. Rebuild layout groups parents-first or every measurement off `Body`
is taken at width 0.

`Body` fixed size: 1410 × 1440 (once laid out). Its `VerticalLayoutGroup`: spacing 0, padding l:50 r:50 t:0 b:0,
`childControlWidth/childForceExpandWidth=true` (children auto-stretch to width),
`childControlHeight=false` (children's height is never touched — always set it yourself).
**Usable width inside Body is therefore 1310.**

`Title` is the only conditional field in the page chrome; Header and page counter are always
present. A **continuation page repeats the previous page's title verbatim** — same text, same
colours — because the reader is still inside the same section. Switch `Title` off only where a page
genuinely has no heading of its own, such as a full-page Line Configuration image.

---

# Where the blocks are instantiated FROM

**Not from this package.** Before assembly, `Editor/PaytableBlockImport.cs` copies `Blocks/` into the
game's own `<bundle>/Prefabs/Paytable/Nested/`, and the paytable nests those copies.

A git-resolved package sits in `Library/PackageCache/` — read-only and wiped on every re-resolve.
A prefab nesting instances from there cannot have overrides applied back, makes the asset bundle
depend on something outside itself, and shows as missing references to anyone without the package.
The old workaround was unpacking the finished prefab, which inlines everything and severs the link
to this library permanently.

The copy is not a plain file copy: the blocks reference each other
(`GridPage → GridCell → IconSlot, PayRows`, `StackPage → SpecialPanel → PanelRow → IconSlot,
PayRows`, `SpecialPanel → TextBlock`), so copies must be repointed at each other or the tree ends up
half local and half packaged. `Import` does that and verifies no package reference survives.

Re-import to pick up a change made here — an existing copy is overwritten in place and keeps its
GUID, so assembled paytables stay linked. Until someone re-imports, a game keeps the snapshot it
took.

# Known design constraints of these blocks

**1. First-frame height collapse — every game hits this.** Text blocks size themselves with
`ContentSizeFitter` = PreferredSize, which asks `TMP_Text` for `preferredHeight`. TMP returns **0**
until it has generated its text mesh, so the first layout pass after a page is enabled measures 0 and
the parent `VerticalLayoutGroup` parks the block against its top padding. In Game Mode a `TextBlock`
lands at `Pos Y = -25` where the prefab says `-336.995`; toggling the object off and on "fixes" it for
that session only.

This is inherent to the fitter-driven design, not a bug in any one game. Assembly must **bake** the
layout: write resolved heights into the `RectTransform`s, disable the fitters, and fill
`LayoutElement.preferredHeight` (its `layoutPriority: 1` outranks `TMP_Text`'s own `ILayoutElement` at
0, so `LayoutUtility` stops consulting the mesh).

**Do not do this by hand.** `Editor/PaytableLayoutBake.cs` does it — `BakeAll(dialogRoot, out perPage)`
to freeze, `Verify(pageRoot)` as the gate that refuses to call a prefab finished while anything can
still re-measure. It is idempotent, and it touches only the texts a `ContentSizeFitter` actually
sizes, so the values below that the library sets deliberately survive.

**2. Two layout settings fight the bake.** `SpecialPanel`'s `VerticalLayoutGroup` ships
`childForceExpandHeight = 1` *and* `childControlHeight = 1`, and its `Label` /
`OptionalTextBlock` `LayoutElement`s ship `flexibleHeight = 1`. Either one alone is enough to keep
handing children spare panel height, so a bake taken while they are active freezes an inflated value
and grows on every re-run. Clearing `flexible` back to `-1` is what actually freezes a child;
`MiddleCenter` then centres it in the remaining room.

Whether these two settings should keep their current values in the library, or be changed so the bake
needs no corrective step, is an open decision — changing them alters runtime layout for every game
still consuming these blocks as nested prefab instances.

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
`ContentSizeFitter` on both texts is what makes constraint 1 above (first-frame height collapse) apply
here too. `spriteAsset` on both must be NULL in the prefab — see the invariant under `TextBlock`.

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

**One instance per paragraph, one bullet per instance.** A page's rules copy is N `TextBlock`
siblings in `Body`, not one instance holding N paragraphs — `Body` stacks them identically either
way, and separate objects are what make a page split (or a hand-move of one paragraph to another
page) a drag in the hierarchy instead of a string edit. A wrapped paragraph still carries no bullet
on its continuation lines.

**Consequence: `m_paragraphSpacing` (500) is inert under this convention.** TMP applies it only at a
paragraph break inside a single text object, and single-paragraph instances have none. The gap
between paragraphs is now the container's Vertical Layout Group `spacing` — and `Body` ships
`spacing = 0`, so it must be set, or the paragraphs render flush.

**`Body`'s `spacing` is therefore 60**, not the 0 it was before this convention.

60 is not `paragraphSpacing`'s contribution. That measures 16 — the extra TMP adds at a hard newline
on top of a normal line advance — and it is the wrong quantity, because between two paragraphs
inside one object there was also a whole line advance, which splitting removes as well. The
container replaces both, less the two objects' own ascender+descender:
`hardBreakAdvance − (asc+desc) = 100.66 − 42.24 ≈ 58`, rounded to 60.

Measured, not reasoned: a real run at `spacing = 16` lost 96–312 units of page height; at 58 the
residual was ±94. One value cannot be exact everywhere, because the advance being replaced scales
with the block's `line-height` (100% plain, 180% with inline sprites). 60 suits plain text and is
slightly tight where sprites are — do not fix that by tuning `Body` per page.

Spacing on `Body` is harmless for the other page types: a Vertical Layout Group applies spacing only
*between* children, and `GridPage`/`StackPage` are a single child.

**`spriteAsset` is NULL on every text object in this library, and must stay that way.** Assign the
game's TMP Sprite Asset explicitly, per text, before using inline `<sprite name="X">` tags. NULL is
deliberately the default because it fails LOUDLY: an unassigned sprite asset renders the tag as
literal `<sprite name="X">` text, which nobody can miss. A *wrong* sprite asset fails silently — TMP
substitutes a fallback glyph, so the page looks populated and only a reader who knows the symbols
can tell.

This was broken until 2026-09-02: `PayRows.prefab` shipped `Count` and `Pay` with
`Goat_PaytableSpriteAsset` assigned, so every game assembled from this library inherited a reference
into the `crazystuffedcoinsgoat` bundle. One game shipped with 41 such references. If you assign a
sprite asset into a block prefab while debugging, clear it before committing.

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
