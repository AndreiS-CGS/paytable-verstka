---
name: paytable-pipeline
description: >
  Extracts Pay Table data from a Confluence GDD page and saves it to Obsidian as structured Markdown files.
  Use this skill whenever the user shares a Confluence link and wants to pull the Pay Table — even if they
  just say "скачай пэйтейбл", "перенеси паблицу из гдд", "grab the paytable from confluence", or similar.
  The skill produces: full Paytable.md (with strikethroughs preserved), Clean version, Symbols list,
  and Paytable Images/Page1.jpg–PageN.jpg — all without needing a browser.
---

## What this skill does

Runs `paytable_from_confluence.py` — a script that talks directly to the Confluence REST API.
It extracts the Pay Table Pages section from any GDD page and produces:

| File | Contents |
|---|---|
| `<GameName> Paytable.md` | Full version — strikethroughs as `<del>text</del>`, symbols as `[SYMBOL]` |
| `<GameName> Paytable Clean.md` | No strikethroughs, no editorial notes (Note:, Please add this text:) |
| `<GameName> Symbols.md` | All unique `[SYMBOL]` tokens in a list |
| `Paytable Images/Page1.jpg … PageN.jpg` | Page images in Pay Table order, named by position |

Images are detected by HTML structure (first `<img>` after each "Page N" heading), so it works for any game without hardcoding filenames.

---

## How to run

### Step 1 — Collect inputs

Ask the user for:
1. **Confluence URL** — the full URL of the GDD page
2. **Game name** — used for file names, short and without spaces
3. **Output folder** — where to save. Default: env `PAYTABLE_OUT` if set, else a repo-relative working
   dir (e.g. `<repo>/_verstka/<GameName>/`). Do NOT assume a personal Obsidian vault — only use one if
   the user explicitly points there.

If the user already provided these in their message, don't ask again — just proceed.

### Step 2 — Run the script

The script is bundled at `scripts/paytable_from_confluence.py` in this skill's directory.
Run it with:

```bash
python3 "<skill_dir>/scripts/paytable_from_confluence.py" \
  "<confluence_url>" \
  "<GameName>" \
  "<output_dir>"
```

Where `<skill_dir>` is the directory containing this SKILL.md file.

Output directory should be expanded (`~` → full path). Use `os.path.expanduser` or pass the full path.

**You do the cleaning pass yourself. Always — the script only does a regex first pass.**
Strip `<del>...</del>` (struck-out) text and editorial notes ("Note:", "Please add this text:",
etc.) out of the full `Paytable.md`, collapsing leftover blank lines, while preserving every payout
value/symbol/heading/table exactly. Read `<GameName> Paytable.md`, apply those rules, and write
`<GameName> Paytable Clean.md`.

The script used to be able to call a remote LLM for this. That path is gone: it sent the entire
paytable body to a third-party endpoint, which is not something a GDD should do quietly, and this
skill only ever runs inside an agent loop that can do the job itself. Only fall back to
the script's regex clean if you're running this step in a context where you can't read/write the
files yourself (e.g. a detached background script with no agent loop attached).

### Step 3 — Report results

After the script finishes, tell the user:
- How many files were written (Paytable.md, Clean, Symbols)
- How many images were downloaded (e.g. "12/12")
- The output folder path

If there were warnings (missing images, section not found), surface them clearly.

---

## Prerequisites & setup (shareable — each colleague configures their own)

Auth is an **Atlassian API token**, and nothing else. Two settings are required:

1. `CONFLUENCE_EMAIL` — the account the token was issued under.
2. The token itself at `~/.confluence_pat` (override with `CONFLUENCE_PAT_FILE`), mode 600.

Both go in through the Unity window (**PlayStudios > Slot Tools > Paytable Tool > Setup**), which
also verifies the token against `/rest/api/user/current` and shows which account it belongs to.
By hand they live in `~/.config/paytable-tools/config.json` and `~/.confluence_pat`.

Create the token with the plain **Create API token** button at
`id.atlassian.com/manage-profile/security/api-tokens` — *not* "Create API token with scopes".
Scoped tokens are addressed through `api.atlassian.com/ex/confluence/<cloudId>/...`, while this
script talks to the site URL directly.

> **Tokens expire.** Atlassian now puts an expiry date on them, and an expired token is
> indistinguishable from a working one by looking at the file. A wrong or expired pair answers
> **401**; a malformed one (token with no email, so the header becomes `base64(":token")`) answers
> **403 Current user not permitted to use Confluence**, which reads like a permissions problem and
> is not. The window's live check is what tells these apart.

Browser cookie auth used to be the primary path and has been removed. Measured: a token fetches
the page text, the attachment list *and* the images — the image download 302-redirects to a
pre-signed `api.media.atlassian.com` URL that carries its own authorisation. Cookies therefore
bought nothing, while costing a macOS Keychain prompt, the native `browser_cookie3` dependency,
and support for exactly one browser.

One-time setup per machine:
1. Install the Python dependencies into the shared venv — `requests`, `pillow`, `numpy`, `scipy`
   (see `requirements.txt` at the repo root). The Unity window does this.
   Do NOT use `pip install --target ~/.local/lib/python-extra`: that directory is not versioned
   and used to be placed ahead of the venv on `sys.path`, so it shadowed the properly installed
   packages while every check still reported success.
2. Create the API token and paste it into the window along with your email.

---

## Common issues

**"Pay Table section not found"** — the section is matched by an anchor id containing `Pay Table` / `Paytable`, which covers the common heading names. If a GDD heads the section something else entirely, the fallback takes the last occurrence of that text; check the page's heading structure.

**Images failed / timeout** — Atlassian's media CDN occasionally times out. The script retries automatically. If images still fail, re-check the token in the Unity window: an expired token fails the page fetch first, so images failing alone usually means a genuine CDN hiccup.

**`403 Current user not permitted to use Confluence`** — the header was not accepted as credentials at all, almost always because `CONFLUENCE_EMAIL` is missing so it was built as `base64(":token")`. **`401`** is different: the pair *was* read and rejected — expired, revoked, or a different account. Re-issue the token.

**Wrong section extracted** — the section is found by anchor id in the body HTML, not in the ToC, so ToC links never match by accident. An unusual page structure falls through to the text fallback, which can over-capture; check the reported section length looks sane.

**Tiny/empty result on the first try** — Confluence sometimes returns a rendering-timeout page instead of real content for large GDD pages. This isn't a real error — just retry the script once before troubleshooting further.

**Token sweep is exhaustive.** All token regexes derive from one shared `TOKEN_BODY` class that
includes `_ & + . -` and spaces, so plain names, `DARK_*`-style variants, `2_*` doubled variants,
`X&Y` combos and `+N_SPIN` forms are all captured. Games that use no underscored tokens are
unaffected by this; games with rich variant vocabularies would otherwise lose a large share of their
symbols, which is how such a gap can sit unnoticed for a long time.

Why it matters: per `cgs-atlas-builder`, a token missing from the atlas makes TMP render a default
emoji instead of `<sprite name="…">`.

**Alias collisions.** The same symbol is sometimes spelled two ways in one GDD, which would put two
entries in the atlas for one sprite. Three distinct causes, and the pipeline handles all three:

1. **Most aren't real.** The "losing" spelling usually occurs *only inside `<del>`* — old wording
   already marked for deletion. It disappears as soon as you build from the Clean version, which is
   the correct source anyway. It stays visible in the full `Paytable.md`, which faithfully preserves
   struck text — that is intended.
2. **A `make_clean` bug used to hide some.** The inline strikethrough regex used `[^<]+`, which
   cannot span a stray `<`, and struck text often contains a mangled token. The whole struck fragment
   then survived into the Clean file. Now `.*?` with `DOTALL`.
3. **Some are genuine** — both spellings live in unstruck text. `canonicalise_tokens()` rewrites
   **every** token into its canonical sprite-name form, so duplicates collapse for free.

Normalising everything, rather than only names that appear in both spellings, is what makes the text
side and the atlas side agree: **sprite names in a built atlas are already underscore-normalised**,
so a spaced token would otherwise never match its own sprite.

**Token → sprite name.** `sprite_name()` applies these two rules and `extract_symbols()` runs every
name through it, so `Symbols.md` can be handed straight to `cgs-atlas-builder`:

| rule | example |
|---|---|
| `' '` → `'_'` | `[DARK ACE]` → `DARK_ACE`, `[MINI BONUS]` → `MINI_BONUS` |
| `'&'` → `'_'` | `[DIAMOND&ACE]` → `DIAMOND_ACE`, `[WILD&SIGNBOARD]` → `WILD_SIGNBOARD` |
| `'+'` → `'PLUS'` | `[+1 SPIN]` → `PLUS1_SPIN` |

**This exact rule is duplicated in `cgs-atlas-builder`'s `pack_atlas.py`, which applies it to PNG
filenames. Change one, change the other.** The two ends had no shared rule until a run emitted 27
names the atlas did not contain — the GDD writes combined symbols as `DIAMOND&ACE` while the art is
filed as `DIAMOND_ACE`. Nothing errored: TMP substitutes a fallback glyph for an unknown sprite
name, so one page rendered the same wrong icon thirteen times with an empty console.

Check one way only — **every token must have a sprite, not the reverse.** An atlas legitimately
contains sprites absent from the rules text: grid symbols (card ranks, PICs) come from the Pay Grid
and may never appear as a token.

---

## Colour sampling (`scripts/title_color.py`, `scripts/feature_color.py`)

Companion tools for reading the one thing the GDD text does not carry: **colour**. Titles are plain
`<strong>` with no styling, so title and feature-mention colours have to come off the screenshots.
Text content never does — see the division of authority in `paytable-verstka`.

Dependencies: `pillow`, `numpy`, `scipy`. Install in a venv, never into system Python. All three are
cross-platform; do not shell out to `sips` or any other OS-specific tool.

### `title_color.py <image>` — title colour

Titles sit in a fixed band on every page of a game — chrome-level layout, so locate it once and
reuse. Glyphs are pure saturated primaries, so the sampler filters to `max ≥ 200 && min ≤ 80`, which
also rejects the gold frame chrome that otherwise dominates that band.

- Colour is **per word-run**, not per title: one title can carry several, with connector words
  falling back to the default.
- Pages with no title (continuations, line-configuration pages) correctly return nothing.
- `--all-bands` disables the title-shape filter. That filter is geometric, so wide centred body rows
  and diagram annotation labels can still slip through — the GDD text says how many titles a page
  really has, and that count is the authority.

### `feature_color.py <image>` — coloured feature mentions in body copy

Body copy is small and fully anti-aliased, so the pure primaries a title gives never survive there;
the same hue measures far darker with no dominant value. Detection therefore classifies by **hue
family** and snaps to the title palette. Coloured pixels appear on nearly every row (inline sprites),
so glyphs are isolated as connected components and separated from sprite art by height rather than by
row-banding.

`--crops DIR` writes one PNG per run for reading. **The string match is not optional:** three
different things in the body share the same size and colours, and only their text separates them —
feature mentions (colour them), jackpot logos (bevelled sprite art, already a sprite, skip), and
diagram annotation labels (neither a title nor a token, ignore).

Any colour-sampling pass must also **skip placeholder rects** — an unfilled slot is a large solid
magenta fill, and counting it as content dominates the statistics.

## Интерпретатор Python

Запускай скрипты как `python3 "<skill_dir>/scripts/<name>.py"` и не подбирай интерпретатор вручную.
Каждый скрипт сам перепрыгивает на нужный: первой строкой он импортирует `_bootstrap`, который
находит правильный интерпретатор (`PAYTABLE_PYTHON` → `~/.venvs/paytable-tools` → конфиг) и
пере-запускает себя под ним через `os.execv`. Вывод и код возврата проходят насквозь, разницы не
видно.

Если ни один не найден и зависимостей нет, скрипт **падает с внятным сообщением**, называя
интерпретатор и чего в нём не хватает. Он никогда не продолжает работу молча на неполном окружении —
раньше именно так и было: необязательный на вид `try/except ImportError` отключал целую ветку
авторизации, и никто об этом не узнавал.
