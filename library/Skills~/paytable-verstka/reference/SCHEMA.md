# `_verstka/*.yaml` — schema

Format spec for the intermediate files written in Phase 2 and consumed in Phase 5.

Three files:

| File | Holds | Source of truth |
|---|---|---|
| `blocks.yaml` | page structure and text content | **GDD text** |
| `win_tables.yaml` | payout numbers | **reference screenshot** |
| `symbols.yaml` | token → sprite name, and which art is still missing | GDD tokens + atlas |

## Division of authority — the rule everything else follows

**Content comes from the GDD text. Only payouts and colours come from the screenshot.**

Reference screenshots lag the document. A GDD routinely strikes out copy the render still shows, and
a value already present in the text can still be blank in the image. So build from the **Clean**
text, and treat the screenshot as a layout reference, not a content source.

Two deliberate exceptions, because the text does not contain the data at all:

* **payout numbers** — the Pay Table Pages section carries no numbers, only the Substitute / Scatter /
  Trigger panels and rules copy. Read them from the image → `win_tables.yaml`.
* **colours** — titles are plain `<strong>` with no styling in the GDD. Sample from the image.

## Page order

Pages are numbered by the GDD's own `## Page N` headings, and **that ordering is the authority**.
Two other orderings exist and are *not* authorities: attachment filenames (often `Help_NN_*`) are
legacy asset names, and the `PAGE X/Y` counter printed inside a screenshot belongs to the same stale
render that still shows struck-out copy. Extraction already numbers `PageN.jpg` by GDD order, so the
files line up as-is.

---

## `blocks.yaml`

```yaml
game: <Game Name>
slot: <slotid>                 # bundle folder name
shell: GEL                     # GEL | MCF
orientation: Portrait          # from GDD Summary -> Layout. Summary WINS over screenshot pixels.

pages:
  # ── specials: an oversized panel gets a page to itself ────────────────────
  - page: 1
    header: Pay Table
    title:
      text: "<GAME NAME>™"
      runs: [{t: "<GAME NAME>™", colour: yellow}]
    template: stack            # 1..3 wide cells, height divided evenly
    cells:
      - label: SUBSTITUTE
        icons: [WILD_LEFT, WILD, WILD_RIGHT]
        pays: null             # this panel carries no numbers
        paragraphs:
          - runs: [{t: "SUBSTITUTES FOR ALL PAY TABLE SYMBOLS EXCEPT "},
                   {sprite: SCATTER}, {t: " AND "}, {sprite: BONUS}, {t: "."}]
          # … a panel with a dozen rule lines is exactly why it takes a page alone

  # ── specials: two small panels share a page ───────────────────────────────
  - page: 2
    header: Pay Table
    title: {text: "<GAME NAME>™", runs: [{t: "<GAME NAME>™", colour: yellow}]}
    template: stack
    cells:
      - label: SCATTER
        icons: [SCATTER]
        pays: SCATTER          # key into win_tables.yaml
        paragraphs:
          - runs: [{t: "PAYS IN ANY POSITION."}]
      - label: TRIGGER
        icons: [BONUS, BONUS_2]
        pays: null             # trigger panels have no payout column
        paragraphs:
          - runs: [{t: "APPEARS ONLY ON THE CENTER REELS."}]

  # ── majors ────────────────────────────────────────────────────────────────
  - page: 3
    header: Pay Table
    title: {text: "<GAME NAME>™", runs: [{t: "<GAME NAME>™", colour: yellow}]}
    template: grid             # 6 frames in 3+3; enable 4, 5 or 6
    tier: major
    cells:
      - {icons: [PIC_A], pays: PIC_A}
      - {icons: [PIC_B], pays: PIC_B}
      - {icons: [PIC_C], pays: PIC_C}
      - {icons: [PIC_D], pays: PIC_D}
      - {icons: [PIC_E], pays: PIC_E}
      # 5 cells -> row 2 holds 2 and centres itself

  # ── minors ────────────────────────────────────────────────────────────────
  - page: 4
    header: Pay Table
    title: {text: "<GAME NAME>™", runs: [{t: "<GAME NAME>™", colour: yellow}]}
    template: grid
    tier: minor
    cells:
      - {icons: [ACE],   pays: ACE}
      - {icons: [KING],  pays: KING}
      - {icons: [QUEEN], pays: QUEEN}
      - {icons: [JACK],  pays: JACK}
      - {icons: [TEN],   pays: TEN}
      - {icons: [NINE],  pays: NINE}

  # ── rules text, with a multi-coloured title ───────────────────────────────
  - page: 5
    header: Feature Game Rules
    title:
      text: "FEATURE ONE AND FEATURE TWO"
      runs:                    # colour is per word-run, sampled from the image
        - {t: "FEATURE ONE", colour: green}
        - {t: "AND",         colour: yellow}
        - {t: "FEATURE TWO", colour: red}
    template: text
    paragraphs:
      - runs: [{t: "THE "}, {feature: "FEATURE ONE", colour: green},
               {t: " AND "}, {feature: "FEATURE TWO", colour: red},
               {t: " CAN TRIGGER AT THE SAME TIME."}]

  # ── a manual slot inside a text page ──────────────────────────────────────
  - page: 6
    header: Basic Game Rules
    title: {text: "<GAME NAME>™", runs: [{t: "<GAME NAME>™", colour: yellow}]}
    template: text
    paragraphs:
      - slot:
          note: "Reel-layout map with LEFT / CENTER / RIGHT callouts"
          height_fraction: 0.25
      - runs: [{t: "WHEN "}, {sprite: WILD_LEFT}, {t: " OR "}, {sprite: WILD_RIGHT},
               {t: " APPEARS, "}, {sprite: GRAND_JACKPOT}, {t: " MAY BE WON RANDOMLY."}]
```

### `template`

Four values, mapping onto the library blocks:

| `template` | Library blocks | Enabled count |
|---|---|---|
| `text` | `TextBlock` (+ `ManualSlot` for any `slot`) | — |
| `stack` | `SpecialPanel` (label + `PanelRow` + rules copy), one per cell | 1–3, height split evenly |
| `grid` | grid rows + `GridCell` | 4 (2+2) · 5 (3+2) · 6 (3+3) |
| `image` | one `ManualSlot` filling Body | — |

`grid` is a fixed 6-frame, two-row template: switch cells off and the layout re-centres the short
row. Six frames is the ceiling — a symbol tier that needs more than six is outside what this
template covers, and should be raised rather than crammed in.

**Orientation follows the template, and is never a per-cell choice:**

* `grid` cells are **always vertical** — symbol image on top, pay rows underneath. Older hand-built
  paytables sometimes mix in an icon-on-the-left variant inside one grid; we normalise that away.
* `stack` panels are **always horizontal** — a full-row cell is wide and short, so the icon(s) sit on
  the left and the pay rows to their right. The panel's label sits above that row and any rules copy
  below it.

So there is no orientation field in the schema: the template determines it.

`stack` covers the specials, and **which specials share a page is decided by text volume, not by
count.** A panel carrying a dozen rule lines cannot share a page with anything; several one- or
two-line panels together take less room than that one panel alone. A panel that overflows its cell
takes a page on its own, the rest pair up.

### `runs` — the paragraph model

One GDD paragraph = one entry, rendered with **one bullet per paragraph, not per visual line** (a
wrapped paragraph carries no bullet on its continuation). Body copy on a page is **one uniform
size** — there is no small-print variant. Inside a `SpecialPanel` two other fixed sizes apply (label
larger, rules copy tighter); see `library/BLOCKS.md`. Size is never chosen per page.

**There is no footer concept.** Boilerplate pay-rules copy is not a special block and gets no special
handling: if the GDD writes such text under a page, it is paragraphs on that page like any other; if
it isn't there, it isn't there. Don't hunt for it elsewhere in the document and don't replicate it
across pages.

Three run kinds, and they are genuinely different things:

| Run | Renders as | Note |
|---|---|---|
| `{t: "..."}` | plain text | |
| `{sprite: NAME}` | `<sprite name="NAME">` | includes the bevelled jackpot logos — those are **sprites, not coloured text** |
| `{feature: "...", colour: c}` | flat coloured text | a feature named mid-sentence; colour matches that feature's own Title colour |

Variant tokens are **separate art, not modifiers**: a doubled-symbol token is its own sprite, not the
base symbol with a badge applied.

### Title

`title` is the blueprint's one conditional field. The header and page counter are always present.

**A continuation page repeats the previous page's title verbatim** — same text, same colours. When a
block overflows and is split, the reader is still inside the same section, so the heading has to stay
with it. Omit `title` only where a page genuinely has no heading of its own, such as a full-page
Line Configuration image.

Text comes from the first bold line after `## Page N`. Colour is sampled from the image band where
the title sits, which is chrome-level layout and constant across pages of a game — locate it once,
then reuse. Colour is **per word-run**: one title can carry several, with connector words falling
back to the default.

### Splitting a GDD page

If one GDD page carries two `Title + text` sections, split it into **two logical pages** — do not
model it as one block with subblocks. This keeps `title` a single blueprint field instead of
promoting it to a body-level block. Such a page shows two title bands at different heights in the
reference image, each opening a complete section.

---

## `win_tables.yaml`

Payouts read from the reference image. **The row set is per symbol — never a fixed template.** An
extra low-count row (e.g. a 2-of-a-kind pay) belongs only to the symbols that actually have one, and
that varies *within* a single grid.

```yaml
PIC_A:
  rows: [{count: 5, value: 100}, {count: 4, value: 60},
         {count: 3, value: 30},  {count: 2, value: 2}]     # 4 rows: this symbol pays on 2
PIC_B:
  rows: [{count: 5, value: 80}, {count: 4, value: 50}, {count: 3, value: 25}]
ACE:
  rows: [{count: 5, value: 25}, {count: 4, value: 15}, {count: 3, value: 5}]
SCATTER:
  rows:
    - {count: 5, value: 1000, note: "(+10 FREE GAMES)"}    # annotation stays on the
    - {count: 4, value: 500,  note: "(+8 FREE GAMES)"}     # same line, same column
    - {count: 3, value: 250,  note: "(+6 FREE GAMES)"}
```

Count green, value yellow-gold, ordered by count descending. No third column beyond `note`.

**`1 credit` is always the first line of the `Pay` column** — a universal rule, not a per-game
option, and we add it ourselves rather than reading it from the reference (which is why most
reference screenshots don't show it). `Count`'s first line is blank so the two columns stay aligned;
the label itself is never coloured. It is therefore not part of `win_tables.yaml` at all — the rows
below hold only real payouts.

> **Some games are a different family, not a parameter of this one.** If a game packs two symbols
> into one cell under a shared footer, labels every cell, inverts the colour coding, or draws one
> continuous ornate frame instead of separate cells — don't bend `grid` to cover it. Treat it as its
> own layout and raise it.

---

## `symbols.yaml`

```yaml
sprites:
  - {name: PIC_A,      art: sprites/_PaytableAtlas/PIC_A.png}
  - {name: PLUS1_SPIN, art: null}          # art missing -> report to the user
atlas_size: 1280               # by symbol count, not a fixed number
```

`Symbols.md` from `paytable-pipeline` is exhaustive and pre-normalised to sprite names, so it can be
used directly. Token → sprite name is two rules:

```
' '  ->  '_'          [DARK ACE] -> DARK_ACE,  [MINI BONUS] -> MINI_BONUS
'+'  ->  'PLUS'       [+1 SPIN]  -> PLUS1_SPIN
```

Check one way only: **every token must have a sprite, not the reverse.** An atlas legitimately
contains sprites absent from the rules text — grid symbols (card ranks, PICs) come from the Pay Grid
and may never appear as a token.

---

## Manual slots

Diagrams, reference tables and legends are one-offs: sometimes a picture, sometimes a table, and a
table is frequently a picture anyway. A large share of what a GDD shows in these positions is an
authoring artifact — empty labelled frames, annotated placeholder rectangles — rather than shippable
art.

So the tool **reserves the space and reports it**: a `ManualSlot` sized from the reference region,
filled with a **magenta `#FF00FF` placeholder**, and the run ends with the list of slots awaiting a
person.

> Magenta can also appear as a diagram-annotation colour, so any colour-sampling pass must **skip
> placeholder rects**. A placeholder is always a large solid fill and never thin glyphs, so area
> separates them — but the exclusion has to be explicit, or a bright fill will dominate the
> sampler's statistics.
