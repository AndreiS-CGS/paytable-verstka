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

**No `OPENROUTER_API_KEY`? Do the cleaning pass yourself instead of settling for the regex fallback.**
The script's LLM-clean step is just: strip `<del>...</del>` (struck-out) text and editorial notes
("Note:", "Please add this text:", etc.) out of the full `Paytable.md`, collapsing leftover blank
lines, while preserving every payout value/symbol/heading/table exactly. You (the agent running this
skill) can do that same pass directly — read `<GameName> Paytable.md`, produce
`<GameName> Paytable Clean.md` with those rules applied, and skip calling out to OpenRouter entirely.
There's no reason to pay for a second LLM call for a job you can already do inline; only fall back to
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

Auth uses the **browser cookie session** as the primary path — the Atlassian PAT/API token often
returns `403 "Current user not permitted to use Confluence"`, while a logged-in Chrome session works
for both page HTML and attachment downloads. Key requirement: **be logged into the CGS Confluence in a
Chrome profile.**

One-time setup per machine (no code edits — everything is env-overridable):
1. Install `browser_cookie3` (a venv, or `pip install browser-cookie3 --target ~/.local/lib/python-extra`).
2. Be logged into `playstudios.atlassian.net` in some Chrome profile.
3. Optionally pin a profile via env — normally not needed:
   - `CHROME_PROFILE="<profile name>"`, OR `CHROME_COOKIE_FILE=/full/path/to/Cookies`.
     With neither set, the script finds the right profile by itself (see below).
   - Optional PAT auth: `CONFLUENCE_EMAIL=you@…` + token at `~/.confluence_pat` (or `CONFLUENCE_PAT_FILE`).
   - Optional `OPENROUTER_API_KEY` for the script's own LLM cleaning; otherwise it falls back to
     regex. Since this skill runs inside an agent loop anyway, prefer having the agent do the
     cleaning pass itself instead (see Step 2) — no key needed for that.

Chrome user-data locations are resolved per platform (macOS / Windows `%LOCALAPPDATA%` / Linux
`~/.config`), including the newer `Network/Cookies` sub-path. No profile name or personal path is
hardcoded — with no env set, the script enumerates every profile and picks whichever one actually
holds cookies for the Confluence domain, printing which it used.

---

## Common issues

**"Pay Table section not found"** — the section is matched by an anchor id containing `Pay Table` / `Paytable`, which covers the common heading names. If a GDD heads the section something else entirely, the fallback takes the last occurrence of that text; check the page's heading structure.

**Images failed / timeout** — Atlassian CDN occasionally times out. The script retries automatically. If images still fail, check that the configured Chrome profile (`CHROME_PROFILE`/`CHROME_COOKIE_FILE`) is logged into the CGS Confluence account.

**`403 Current user not permitted to use Confluence`** — the PAT/API identity lacks access. Use cookie auth instead: log into Confluence in Chrome and set `CHROME_PROFILE`/`CHROME_COOKIE_FILE`.

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
| `'+'` → `'PLUS'` | `[+1 SPIN]` → `PLUS1_SPIN` |

Check one way only — **every token must have a sprite, not the reverse.** An atlas legitimately
contains sprites absent from the rules text: grid symbols (card ranks, PICs) come from the Pay Grid
and may never appear as a token.
