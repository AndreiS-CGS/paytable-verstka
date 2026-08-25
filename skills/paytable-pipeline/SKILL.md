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
1. **Confluence URL** — the full URL of the GDD page (e.g. `https://playstudios.atlassian.net/wiki/spaces/MYK/pages/12345/Goat+GDD`)
2. **Game name** — used for file names (e.g. `Goat`, `Dragon`, `CrazyCoins`)
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
3. Point the script at that profile via env:
   - `CHROME_PROFILE="Profile 3"` (your profile name), OR `CHROME_COOKIE_FILE=/full/path/to/Cookies`.
     If unset, the script auto-detects the first Chrome profile that has a Cookies DB.
   - Optional PAT auth: `CONFLUENCE_EMAIL=you@…` + token at `~/.confluence_pat` (or `CONFLUENCE_PAT_FILE`).
   - Optional `OPENROUTER_API_KEY` for the script's own LLM cleaning; otherwise it falls back to
     regex. Since this skill runs inside an agent loop anyway, prefer having the agent do the
     cleaning pass itself instead (see Step 2) — no key needed for that.

Notes: macOS Chrome paths by default — set `CHROME_COOKIE_FILE` explicitly on other OSes/browsers.
No `/Users/<name>/…` is hardcoded; defaults fall back to Andrei's values only when env is unset, so his
setup keeps working and colleagues just set their own env.

---

## Common issues

**"Pay Table Pages section not found"** — The GDD page may use a different heading name. Check that the page has an anchor with `PayTablePages` in its id, or that the heading text contains "Pay Table Pages".

**Images failed / timeout** — Atlassian CDN occasionally times out. The script retries automatically. If images still fail, check that the configured Chrome profile (`CHROME_PROFILE`/`CHROME_COOKIE_FILE`) is logged into the CGS Confluence account.

**`403 Current user not permitted to use Confluence`** — the PAT/API identity lacks access. Use cookie auth instead: log into Confluence in Chrome and set `CHROME_PROFILE`/`CHROME_COOKIE_FILE`.

**Wrong section extracted** — The script finds the section by `id="...-PayTablePages"` anchor in the body HTML (not the ToC). If the page structure is unusual, the fallback uses the last occurrence of "Pay Table Pages" text.

**Tiny/empty result on the first try** — Confluence sometimes returns a rendering-timeout page instead of real content for large GDD pages. This isn't a real error — just retry the script once before troubleshooting further.

**`Symbols.md` undercounts on complex games** — its regex catches `[SYMBOL]`/`<SYMBOL>` but misses variant/combo shapes some games use: `DARK_*` prefixes, `2_*` doubled variants, `X&Y` two-symbol combos, `+N_SPIN`-style tokens. Downstream consumers (e.g. `paytable-verstka`) should always additionally regex-sweep the raw `Paytable.md` for every `[...]`/`<...>` token themselves and reconcile against `Symbols.md`, rather than trusting it alone for symbol-heavy games.
