#!/usr/bin/env python3
"""
Paytable Create Pipeline — Confluence API version
No browser, no SingleFile. Works directly via Atlassian REST API.

Auth: an Atlassian API token at ~/.confluence_pat plus CONFLUENCE_EMAIL. Both are required.

      Browser cookies used to be the primary path. They are gone: a token fetches page text, the
      attachment list AND the images (measured — the image download 302s to a signed media URL
      that needs no credential of its own), so cookies bought nothing and cost a macOS Keychain
      prompt, a native dependency, and support for exactly one browser.

Usage:
  python3 paytable_from_confluence.py <confluence_url> <GameName> <output_dir>

Example:
  python3 paytable_from_confluence.py \
    "https://playstudios.atlassian.net/wiki/spaces/MYK/pages/17490313941/..." \
    "<GameName>" \
    "<output_dir>"
"""

import re
import sys
import os

import _bootstrap                       # MUST precede every third-party import
_bootstrap.require('requests')

import requests
from base64 import b64encode


# ── Config ──────────────────────────────────────────────────────────────────
# Read through _bootstrap.config (real env first, then the config file the Unity Setup tab
# writes), and read it INSIDE the functions that use it: at module scope these froze before
# main() ran, so a config file could never affect them.
def _email():
    return _bootstrap.config('CONFLUENCE_EMAIL', '') or ''


def _token_file():
    return os.path.expanduser(_bootstrap.config('CONFLUENCE_PAT_FILE', '~/.confluence_pat'))


SECTION_END_MARKER = 'Task types for Live Ops'

# ── Symbol tokens ───────────────────────────────────────────────────────────
# Rules text refers to game symbols as <NAME> / [NAME]. Real GDDs use far more
# than caps and spaces: DARK_ACE, 2_WILD&SIGNBOARD, FULL_FUSE_RED_DYNAMITE,
# +1_SPIN, TROLLEY_2. Underscore in particular was missing from every class
# below, which silently dropped a large share of the vocabulary on variant-rich
# games — and a token missing from Symbols.md makes TMP render a default emoji
# instead of <sprite name="…">. Keep all token regexes derived from this one
# character class so they can never drift apart again.
TOKEN_BODY = r'[A-Z0-9][A-Z0-9_&+.\- ]*'
TOKEN_ANGLE_RE  = re.compile(r'<\s*(\+?%s)\s*>' % TOKEN_BODY)
TOKEN_SQUARE_RE = re.compile(r'\[\s*(\+?%s)\s*\]' % TOKEN_BODY)

EDITORIAL_PATTERNS = [
    # "Add:" / "Add this text:" / "Please, add these text:" / "Please also add:" /
    # "**Add:**" — authors mark inserted copy in many wordings, and matching only
    # "Please add this text:" leaves the marker in the Clean output of most GDDs.
    # Deliberately narrow: the tail after "add" only accepts filler words and the
    # line must end in a colon, so real rules copy survives — "ALL WINS ARE
    # ADDED.", "ADD 1 EXTRA SPIN:", "ADDED SYMBOLS:" are all kept.
    r'^\*{0,2}\s*(?:please,?\s*)?(?:also\s+)?add'
    r'(?:\s+(?:also|this|these|those|the\s+following))?'
    r'(?:\s+(?:text|texts|line|lines|wording))?\s*:\s*\*{0,2}\s*$',
    r'^\*{0,2}Note:.*$',
    r'^\*{0,2}Comment:.*$',
]


def get_headers():
    """Basic-auth headers from the API token. Required — there is no other auth path.

    The token and the email must both be present: with a token and no identity the header comes
    out as Basic base64(":token"), which Confluence answers with
    "403 Current user not permitted to use Confluence" — indistinguishable from a real permissions
    problem, and the reason the token path was believed to be flaky for months.
    """
    token_file = _token_file()
    try:
        with open(token_file) as f:
            token = f.read().strip()
    except OSError:
        sys.exit(
            f"\nNo Confluence API token at {token_file}.\n"
            f"  Create one (the plain kind, not \"with scopes\") at\n"
            f"  https://id.atlassian.com/manage-profile/security/api-tokens\n"
            f"  then paste it into the Unity window: PlayStudios > Slot Tools > Paytable Tool.\n")
    if not token:
        sys.exit(f"\n{token_file} is empty.\n")
    email = _email()
    if not email:
        sys.exit(
            "\nCONFLUENCE_EMAIL is not set, and Basic auth needs it alongside the token.\n"
            "  Set it in the Unity window, or in ~/.config/paytable-tools/config.json.\n")
    creds = b64encode(f'{email}:{token}'.encode()).decode()
    return {'Accept': 'application/json', 'Authorization': f'Basic {creds}'}


# ── URL parsing ─────────────────────────────────────────────────────────────

def parse_url(url):
    base = re.match(r'(https://[^/]+)', url).group(1)
    page_id = re.search(r'/pages/(\d+)', url)
    if not page_id:
        raise ValueError(f'Cannot extract page ID from: {url}')
    return base, page_id.group(1)

# ── Confluence API calls ────────────────────────────────────────────────────

def fetch_page_html(base_url, page_id, headers):
    """Get page body in export_view HTML format (preserves strikethrough etc.)."""
    resp = requests.get(
        f'{base_url}/wiki/rest/api/content/{page_id}',
        params={'expand': 'body.export_view'},
        headers=headers,
        timeout=30,
    )
    resp.raise_for_status()
    return resp.json()['body']['export_view']['value']


def extract_image_filenames_from_html(section_html):
    """Find the first image after each 'Page N' heading in the Pay Table section.

    In export_view HTML, images are <img src="/wiki/download/attachments/{id}/{filename}?...">.
    The heading position in the document determines the page number order.
    Returns a list of filenames in page order (index 0 = Page 1).
    """
    heading_re = re.compile(r'<h\d[^>]*>(.*?)</h\d>', re.DOTALL | re.IGNORECASE)
    img_re = re.compile(
        r'<img[^>]+src=["\'][^"\']*?/([^/"\'?#]+\.(?:jpg|jpeg|png))(?:[?#][^"\']*)?["\']',
        re.IGNORECASE,
    )

    headings = [
        (m.start(), m.end(), re.sub(r'<[^>]+>', '', m.group(1)).strip())
        for m in heading_re.finditer(section_html)
    ]

    found = []
    for i, (h_start, h_end, h_text) in enumerate(headings):
        page_m = re.match(r'Page\s+(\d+)', h_text, re.IGNORECASE)
        if not page_m:
            continue
        page_num = int(page_m.group(1))
        next_h = headings[i + 1][0] if i + 1 < len(headings) else len(section_html)
        chunk = section_html[h_end:next_h]
        img_m = img_re.search(chunk)
        if img_m:
            found.append((page_num, img_m.group(1)))
        else:
            print(f"  Warning: no image found after Page {page_num} heading")

    if found:
        found.sort(key=lambda x: x[0])
        filenames = [f for _, f in found]
        print(f"  Images by position: {filenames}")
        return filenames

    # Fallback: some GDDs list the screenshots under one flat "Pay Table"
    # heading with no "Page N" sub-headings at all. Without this the whole
    # extraction silently produced zero images. Document order is then the only
    # ordering signal available — attachment filename numbering is not reliable,
    # since those prefixes are legacy asset names, not the screen order.
    filenames = img_re.findall(section_html)
    print(f"  No Page N sub-headings — using document order: {filenames}")
    return filenames


def fetch_paytable_attachments(base_url, page_id, headers, section_html):
    """Return ordered list of attachment info derived from Pay Table HTML structure."""
    paytable_files = extract_image_filenames_from_html(section_html)
    if not paytable_files:
        print("  Warning: no images found in HTML structure")
        return []

    all_attachments = []
    start = 0
    while True:
        resp = requests.get(
            f'{base_url}/wiki/rest/api/content/{page_id}/child/attachment',
            params={'limit': 100, 'start': start},
            headers=headers,
            timeout=30,
        )
        resp.raise_for_status()
        data = resp.json()
        all_attachments.extend(data['results'])
        if len(data['results']) < 100:
            break
        start += 100

    by_name = {att['title']: att for att in all_attachments}

    result = []
    for i, filename in enumerate(paytable_files):
        att = by_name.get(filename)
        if att:
            result.append({
                'name': filename,
                'page': i + 1,
                'save_as': f'Page{i + 1}.jpg',
                'url': base_url + '/wiki' + att['_links']['download'],
            })
        else:
            print(f"  Warning: {filename} not found in attachments")
    return result


def download_images(attachments, output_dir, headers, base_url):
    """Download paytable images to <output_dir>/Paytable Images/.
    The header used to be omitted here, on the assumption that only cookies work for Atlassian
    Cloud attachments. Measured otherwise: the download 302-redirects to api.media.atlassian.com,
    requests drops Authorization across the host change, and the redirect target is already a
    signed URL — so it succeeds on the token alone."""
    images_dir = os.path.join(output_dir, 'Paytable Images')
    os.makedirs(images_dir, exist_ok=True)

    saved = 0
    failed = []
    for att in attachments:
        try:
            resp = requests.get(
                att['url'],
                headers=headers,
                allow_redirects=True,
                timeout=60,
            )
            if resp.status_code == 200 and 'image' in resp.headers.get('content-type', ''):
                path = os.path.join(images_dir, att['save_as'])
                with open(path, 'wb') as f:
                    f.write(resp.content)
                print(f"  Page{att['page']}.jpg  ←  {att['name']}  ({len(resp.content)//1024} KB)")
                saved += 1
            else:
                print(f"  Failed: {att['name']} HTTP {resp.status_code}")
                failed.append(att['save_as'])
        except Exception as e:
            print(f"  Timeout/error: {att['name']} — {e.__class__.__name__}")
            failed.append(att['save_as'])

    if failed:
        print(f"  Retrying {len(failed)} failed images...")
        for att in [a for a in attachments if a['save_as'] in failed]:
            try:
                resp = requests.get(att['url'], headers=headers,
                                    allow_redirects=True, timeout=90)
                if resp.status_code == 200 and 'image' in resp.headers.get('content-type', ''):
                    path = os.path.join(images_dir, att['save_as'])
                    with open(path, 'wb') as f:
                        f.write(resp.content)
                    print(f"  ✓ Retry OK: Page{att['page']}.jpg ({len(resp.content)//1024} KB)")
                    saved += 1
                else:
                    print(f"  ✗ Retry failed: {att['name']} HTTP {resp.status_code}")
            except Exception as e:
                print(f"  ✗ Retry error: {att['name']} — {e.__class__.__name__}")
    return saved

# ── HTML → Markdown conversion ──────────────────────────────────────────────

def strip_html_tags(text):
    """Strip HTML tags but preserve game symbol tokens like <MARK>, <DARK_ACE>."""
    return re.sub(r'<(?!\+?%s>)[^>]+>' % TOKEN_BODY, ' ', text)


def cell_text(html_chunk):
    chunk = html_chunk
    for tag in [r'<del[^>]*>(.*?)</del>', r'<s>(.*?)</s>',
                r'<span[^>]*text-decoration:\s*line-through[^>]*>(.*?)</span>']:
        chunk = re.sub(tag,
            lambda m: ' ~~' + strip_html_tags(m.group(1)).strip() + '~~ ',
            chunk, flags=re.DOTALL)
    t = strip_html_tags(chunk)
    t = t.replace('&nbsp;', ' ').replace('&lt;', '<').replace('&gt;', '>').replace('&amp;', '&')
    t = re.sub(r'&#\d+;', '', t)
    t = re.sub(r'\s+', ' ', t).strip()
    return t


def parse_table(table_html):
    """Parse a Confluence table (no closing </td></tr>) into Markdown."""
    row_chunks = re.split(r'<tr[^>]*>', table_html)[1:]
    md_rows = []
    for i, row in enumerate(row_chunks):
        row = re.split(r'</table', row)[0]
        cell_chunks = re.split(r'<t[dh][^>]*>', row)[1:]
        if not cell_chunks:
            continue
        texts = [cell_text(c) for c in cell_chunks]
        md_rows.append('| ' + ' | '.join(texts) + ' |')
        if i == 0:
            md_rows.append('|' + '---|' * len(texts))
    return '\n'.join(md_rows)


def html_to_markdown(html_fragment):
    # Tables first
    html_fragment = re.sub(
        r'<table[^>]*>.*?</table>',
        lambda m: '\n\n' + parse_table(m.group(0)) + '\n\n',
        html_fragment, flags=re.DOTALL,
    )

    # Strikethrough — convert to placeholder now, replace with <del> after stripping
    # (so <del> tags survive strip_html_tags which would otherwise remove them)
    def make_del_placeholder(m):
        inner = strip_html_tags(m.group(1)).strip()
        return f'\x00DEL\x01{inner}\x00/DEL\x01'
    for tag in [r'<del[^>]*>(.*?)</del>', r'<s>(.*?)</s>',
                r'<span[^>]*text-decoration:\s*line-through[^>]*>(.*?)</span>']:
        html_fragment = re.sub(tag, make_del_placeholder, html_fragment, flags=re.DOTALL)

    # Bold
    html_fragment = re.sub(
        r'<strong[^>]*>(.*?)</strong>',
        lambda m: '**' + re.sub(r'<[^>]+>', '', m.group(1)).strip() + '**',
        html_fragment, flags=re.DOTALL,
    )

    # Superscript
    html_fragment = re.sub(r'<sup[^>]*>(.*?)</sup>', r' \1', html_fragment, flags=re.DOTALL)

    # Headings
    html_fragment = re.sub(
        r'<h[2-4][^>]*>(.*?)</h[2-4]>',
        lambda m: '\n## ' + re.sub(r'<[^>]+>', '', m.group(1)).strip() + '\n',
        html_fragment, flags=re.DOTALL,
    )

    # Block elements
    for tag in ['p', 'div', 'li', 'ul', 'ol', 'blockquote', 'figure']:
        html_fragment = re.sub(rf'<{tag}[^>]*>', '\n', html_fragment)
        html_fragment = re.sub(rf'</{tag}>', '\n', html_fragment)
    html_fragment = re.sub(r'<br[^>]*/?>', '\n', html_fragment)

    # Strip remaining tags (preserve symbols)
    html_fragment = strip_html_tags(html_fragment)

    # Entities
    html_fragment = html_fragment.replace('&nbsp;', ' ')
    html_fragment = html_fragment.replace('&lt;', '<')
    html_fragment = html_fragment.replace('&gt;', '>')
    html_fragment = html_fragment.replace('&amp;', '&')
    html_fragment = re.sub(r'&#\d+;', '', html_fragment)
    html_fragment = re.sub(r'&[a-z]+;', '', html_fragment)

    # Normalize symbol tokens
    html_fragment = re.sub(
        r'<\s*([^>]+?)\s*>',
        lambda m: '<' + m.group(1).strip() + '>',
        html_fragment,
    )

    # Convert game symbol tokens to [SYMBOL] format: <MARK> → [MARK]
    # Square brackets work everywhere in Obsidian without breaking any parsing.
    # Uses the shared token class, so underscored/combo names convert too —
    # previously <GRAND JACKPOT> became [GRAND JACKPOT] while
    # <GREEN_STACKED_WILD> stayed angle-bracketed, leaving two conventions in
    # one file for every downstream consumer to trip over.
    html_fragment = TOKEN_ANGLE_RE.sub(lambda m: '[' + m.group(1) + ']', html_fragment)

    # One spelling per symbol, document-wide (see canonicalise_tokens).
    html_fragment = canonicalise_tokens(html_fragment)

    # Restore strikethrough placeholders as <del>...</del>
    html_fragment = html_fragment.replace('\x00DEL\x01', '<del>').replace('\x00/DEL\x01', '</del>')

    # Whitespace — keep double newlines between paragraphs
    html_fragment = re.sub(r'[ \t]+', ' ', html_fragment)
    html_fragment = re.sub(r' \n', '\n', html_fragment)
    html_fragment = re.sub(r'\n ', '\n', html_fragment)
    html_fragment = re.sub(r'\n{3,}', '\n\n', html_fragment)

    return html_fragment.strip()


def find_paytable_section(html):
    """Extract just the Pay Table section from the full page HTML.

    In export_view format the heading carries an anchor id in the actual body
    (not the ToC), so we match on that id to avoid the ToC links.

    The heading is NOT always "Pay Table Pages": GDDs also head this section
    "Pay Table" or "Paytable" (3 of 9 surveyed games did), and matching the
    longer string alone silently fell through to the whole page — which then
    pulled math tables and unrelated sections into the extraction.
    Section ends at the next same-level (h1/h2) heading.
    """
    # Find the heading by its anchor id attribute (skips ToC occurrences)
    start_m = re.search(r'<h(\d)[^>]*id="[^"]*Pay ?Table[^"]*"', html, re.IGNORECASE)
    if not start_m:
        # Fallback: find LAST occurrence of the heading text (body, not ToC)
        positions = [m.start() for m in re.finditer(r'Pay ?Table', html, re.IGNORECASE)]
        if not positions:
            print("Warning: Pay Table section not found, using full HTML")
            return html
        idx = positions[-1]
        start_m = re.search(r'<h\d', html[:idx][::-1])  # find nearest h tag backwards
        section_start = idx - start_m.start() if start_m else idx
        level = '1'
    else:
        section_start = start_m.start()
        level = start_m.group(1)

    rest = html[section_start:]

    # End at the next heading of same or higher level (h1 if section is h1, etc.)
    end_pattern = rf'<h[1-{level}][\s>]'
    end_m = re.search(end_pattern, rest[200:])  # skip 200 chars to avoid self-match
    section_end = section_start + 200 + end_m.start() if end_m else len(html)

    section = html[section_start:section_end]
    print(f"  Section: chars {section_start}–{section_end} ({len(section)} chars)")
    return section


def make_clean(text):
    lines = text.split('\n')
    result = []
    prev_blank = False
    for line in lines:
        stripped = line.strip()

        # Preserve blank lines (paragraph separators) — but collapse multiple blanks
        if not stripped:
            if not prev_blank:
                result.append('')
            prev_blank = True
            continue
        prev_blank = False

        # Skip lines that are entirely strikethrough: <del>SOME TEXT</del>
        if re.match(r'^<del>.+</del>$', stripped, re.IGNORECASE):
            continue

        # Skip editorial comment lines
        if any(re.match(p, stripped, re.IGNORECASE) for p in EDITORIAL_PATTERNS):
            continue

        # Remove inline strikethrough fragments: <del>word</del> → (removed)
        # `.*?` rather than `[^<]+`: struck text often contains a stray `<` from a
        # mangled token (e.g. "<del>EXCEPT [GRAND JACKPOT] … AND <MI</del>"), and a
        # class that stops at `<` left the whole struck fragment in the Clean file —
        # exactly the version we build content from.
        line = re.sub(r'\s*<del>.*?</del>\s*', ' ', line,
                      flags=re.IGNORECASE | re.DOTALL).strip()
        if not line:
            continue

        result.append(line)

    text = '\n'.join(result)
    return re.sub(r'\n{3,}', '\n\n', text).strip()


def sprite_name(token):
    """GDD token -> TMP sprite name.

    These two rules are what makes GDD tokens line up with the names in a built
    atlas — sprite names there are already underscore-normalised.
        ' ' -> '_'      [DARK ACE]  -> DARK_ACE,  [MINI BONUS] -> MINI_BONUS
        '+' -> 'PLUS'   [+1 SPIN]   -> PLUS1_SPIN
    """
    return re.sub(r'\s+', '_', token.strip()).replace('+', 'PLUS')


def canonicalise_tokens(text):
    """Rewrite every symbol token into its canonical sprite-name form.

    Applied to ALL tokens, not just ones spelled two ways. An earlier version was
    deliberately narrow, on the assumption that renaming spaced tokens would break
    the match against art filenames. The opposite is true: sprite names in a built
    atlas are already underscore-normalised ([DARK ACE] is DARK_ACE there), so
    normalising everything is what actually makes the two sides agree.

    Collapsing duplicate spellings then falls out for free: [GRAND JACKPOT] and
    [GRAND_JACKPOT] become the same token instead of two atlas entries, one of
    which would resolve to nothing at runtime.
    """
    names = {re.sub(r'\s+', ' ', m).strip() for m in TOKEN_SQUARE_RE.findall(text)}
    merged, renamed = [], 0
    for name in sorted(names):
        canon = sprite_name(name)
        if canon == name:
            continue
        text = re.sub(r'\[\s*%s\s*\]' % re.escape(name), '[' + canon + ']', text)
        renamed += 1
        if canon in names:                       # two spellings collapsed into one
            merged.append((name, canon))
    if renamed:
        print(f"  Canonicalised {renamed} token spelling(s) to sprite-name form")
    for was, now in merged:
        print(f"    merged duplicate: [{was}] -> [{now}]")
    return text


def extract_symbols(text):
    """Every symbol token in the text, as canonical [SPRITE_NAME] entries.

    Both bracket styles are accepted so the sweep is complete even if some
    angle-bracketed token survived conversion, and every name is passed through
    sprite_name() so the list can be handed straight to the atlas builder —
    downstream consumers (atlas builder, TMP sprite asset) need one spelling per
    symbol, and the old mixed output produced duplicates like [GRAND JACKPOT]
    plus <GRAND_JACKPOT>.
    """
    names = set()
    for rx in (TOKEN_SQUARE_RE, TOKEN_ANGLE_RE):
        for s in rx.findall(text):
            name = sprite_name(s)
            if name:
                names.add(name)
    return ['[' + n + ']' for n in sorted(names)]

# ── Main ────────────────────────────────────────────────────────────────────

def main():
    if len(sys.argv) < 4:
        print(__doc__)
        sys.exit(1)

    url        = sys.argv[1]
    game_name  = sys.argv[2]
    output_dir = os.path.expanduser(sys.argv[3])

    headers = get_headers()
    base_url, page_id = parse_url(url)
    print(f"Page ID: {page_id}  Base: {base_url}")

    print("Fetching page HTML...")
    html = fetch_page_html(base_url, page_id, headers)
    print(f"  Got {len(html)} chars")

    print("Extracting Pay Table section...")
    section = find_paytable_section(html)
    print(f"  Section: {len(section)} chars")

    md_full = f"# {game_name} — Pay Table Pages\n\n" + html_to_markdown(section)

    # Regex clean only. The agent running this skill does the real cleaning pass itself on top
    # of this output — nothing is sent to any third-party service.
    md_clean = make_clean(md_full)

    symbols = extract_symbols(md_full)

    os.makedirs(output_dir, exist_ok=True)

    full_path    = os.path.join(output_dir, f"{game_name} Paytable.md")
    clean_path   = os.path.join(output_dir, f"{game_name} Paytable Clean.md")
    symbols_path = os.path.join(output_dir, f"{game_name} Symbols.md")

    with open(full_path,    'w', encoding='utf-8') as f: f.write(md_full)
    with open(clean_path,   'w', encoding='utf-8') as f: f.write(md_clean)
    with open(symbols_path, 'w', encoding='utf-8') as f:
        f.write(f"# {game_name} — Symbol List\n\n" + '\n'.join(symbols) + '\n')

    print(f"Written: {full_path}")
    print(f"Written: {clean_path}")
    print(f"Written: {symbols_path}  ({len(symbols)} symbols)")

    print("\nFetching attachment list...")
    attachments = fetch_paytable_attachments(base_url, page_id, headers, section)
    print(f"  Found {len(attachments)} paytable images")

    print("Downloading images...")
    saved = download_images(attachments, output_dir, headers, base_url)
    print(f"  Saved {saved} images → {output_dir}/Paytable Images/")


if __name__ == '__main__':
    main()
