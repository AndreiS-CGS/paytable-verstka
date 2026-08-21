#!/usr/bin/env python3
"""
Paytable Create Pipeline — Confluence API version
No browser, no SingleFile. Works directly via Atlassian REST API.

Config: ~/.confluence_pat  (API token, one line)
Email:  andrei.syr@customgamesstudio.com

Usage:
  python3 paytable_from_confluence.py <confluence_url> <GameName> <output_dir>

Example:
  python3 paytable_from_confluence.py \
    "https://playstudios.atlassian.net/wiki/spaces/MYK/pages/17490313941/..." \
    "Goat" \
    "~/Library/Mobile Documents/iCloud~md~obsidian/Documents/_brsk/CGS/Goats/"
"""

import re
import sys
import os
import requests
from base64 import b64encode

# browser_cookie3 may be vendored under ~/.local/lib/python-extra (optional)
sys.path.insert(0, os.path.expanduser('~/.local/lib/python-extra'))
try:
    import browser_cookie3
    COOKIES_AVAILABLE = True
except ImportError:
    COOKIES_AVAILABLE = False

# ── Config — all env-overridable so the skill is shareable across machines ──
# Confluence identity (only needed for PAT/Basic auth; cookie auth needs neither).
EMAIL      = os.environ.get('CONFLUENCE_EMAIL', 'andrei.syr@customgamesstudio.com')
TOKEN_FILE = os.path.expanduser(os.environ.get('CONFLUENCE_PAT_FILE', '~/.confluence_pat'))

# Chrome profile cookies = the RELIABLE auth path (the Atlassian PAT often returns 403).
# Set CHROME_COOKIE_FILE to a full path, OR set CHROME_PROFILE (e.g. "Profile 3") to build the
# default macOS path. Each colleague points this at their own logged-in Atlassian profile.
def _default_cookie_file():
    explicit = os.environ.get('CHROME_COOKIE_FILE')
    if explicit:
        return os.path.expanduser(explicit)
    profile = os.environ.get('CHROME_PROFILE', 'Profile 3')
    base = os.path.expanduser('~/Library/Application Support/Google/Chrome')  # macOS
    cand = os.path.join(base, profile, 'Cookies')
    if os.path.exists(cand):
        return cand
    # fall back: scan profiles for an existing Cookies DB
    if os.path.isdir(base):
        for d in ['Default'] + sorted(p for p in os.listdir(base) if p.startswith('Profile ')):
            c = os.path.join(base, d, 'Cookies')
            if os.path.exists(c):
                return c
    return cand

CHROME_COOKIE_FILE = _default_cookie_file()

OPENROUTER_API_KEY = os.environ.get('OPENROUTER_API_KEY')
CLEAN_MODEL        = os.environ.get('OPENROUTER_CLEAN_MODEL', 'google/gemini-2.0-flash-lite')

SECTION_END_MARKER = 'Task types for Live Ops'

EDITORIAL_PATTERNS = [
    r'^\*{0,2}Please,?\s*add\s*this\s*text:?\*{0,2}\s*$',
    r'^\*{0,2}Note:.*$',
    r'^\*{0,2}Comment:.*$',
]

# ── Auth ────────────────────────────────────────────────────────────────────

def get_headers():
    with open(TOKEN_FILE) as f:
        token = f.read().strip()
    creds = b64encode(f'{EMAIL}:{token}'.encode()).decode()
    return {
        'Authorization': f'Basic {creds}',
        'Accept': 'application/json',
        '_token': token,  # kept for download calls that need Bearer
    }


def get_download_headers(headers):
    """Attachment downloads require Bearer token, not Basic auth."""
    return {'Authorization': f'Bearer {headers["_token"]}'}

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
        cookies=get_chrome_cookies(base_url),
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

    found.sort(key=lambda x: x[0])
    filenames = [f for _, f in found]
    print(f"  Images by position: {filenames}")
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
            cookies=get_chrome_cookies(base_url),
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


def get_chrome_cookies(base_url):
    """Load Chrome session cookies for the Confluence domain."""
    if not COOKIES_AVAILABLE:
        return None
    from urllib.parse import urlparse
    domain = urlparse(base_url).netloc
    return browser_cookie3.chrome(cookie_file=CHROME_COOKIE_FILE, domain_name=domain)


def download_images(attachments, output_dir, headers, base_url):
    """Download paytable images to <output_dir>/Paytable Images/.
    Uses Chrome session cookies (more reliable than API token for downloads)."""
    images_dir = os.path.join(output_dir, 'Paytable Images')
    os.makedirs(images_dir, exist_ok=True)

    # Try Chrome cookies first (required for Atlassian Cloud attachment downloads)
    cookies = get_chrome_cookies(base_url)
    if not cookies:
        print("  Warning: browser_cookie3 not available — image downloads may fail")

    saved = 0
    failed = []
    for att in attachments:
        try:
            resp = requests.get(
                att['url'],
                cookies=cookies,
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
                resp = requests.get(att['url'], cookies=cookies, allow_redirects=True, timeout=90)
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
    """Strip HTML tags but preserve game symbol tokens like <MARK>, <5X>."""
    return re.sub(r'<(?![A-Z0-9][A-Z0-9 +]*>)[^>]+>', ' ', text)


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
    html_fragment = re.sub(r'<([A-Z0-9][A-Z0-9 +]*)>', r'[\1]', html_fragment)

    # Restore strikethrough placeholders as <del>...</del>
    html_fragment = html_fragment.replace('\x00DEL\x01', '<del>').replace('\x00/DEL\x01', '</del>')

    # Whitespace — keep double newlines between paragraphs
    html_fragment = re.sub(r'[ \t]+', ' ', html_fragment)
    html_fragment = re.sub(r' \n', '\n', html_fragment)
    html_fragment = re.sub(r'\n ', '\n', html_fragment)
    html_fragment = re.sub(r'\n{3,}', '\n\n', html_fragment)

    return html_fragment.strip()


def find_paytable_section(html):
    """Extract just the Pay Table Pages section from the full page HTML.

    In export_view format the heading has id="...-PayTablePages" in the actual
    body (not the ToC). We find by anchor id to avoid matching the ToC links.
    Section ends at the next same-level (h1/h2) heading.
    """
    # Find the heading by its anchor id attribute (skips ToC occurrences)
    start_m = re.search(r'<h(\d)[^>]*id="[^"]*PayTablePages[^"]*"', html, re.IGNORECASE)
    if not start_m:
        # Fallback: find LAST occurrence of Pay Table Pages text (body, not ToC)
        positions = [m.start() for m in re.finditer(r'Pay Table Pages', html, re.IGNORECASE)]
        if not positions:
            print("Warning: Pay Table Pages section not found, using full HTML")
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
        line = re.sub(r'\s*<del>[^<]+</del>\s*', ' ', line, flags=re.IGNORECASE).strip()
        if not line:
            continue

        result.append(line)

    text = '\n'.join(result)
    return re.sub(r'\n{3,}', '\n\n', text).strip()


def make_clean_llm(text: str) -> str:
    """LLM-based clean via OpenRouter — smarter than regex."""
    import json

    system = (
        "You are cleaning a slot machine paytable document extracted from Confluence. "
        "Remove ALL editorial noise while preserving ALL game content exactly as-is.\n\n"
        "REMOVE:\n"
        "- Lines with editorial instructions: 'Note:', 'Please add this text:', 'Comment:', 'Please,' etc.\n"
        "- All strikethrough text wrapped in <del>...</del> tags (both inline and whole-line)\n"
        "- Any designer/writer comments or internal notes\n"
        "- Blank lines left by removed content (collapse to single blank line)\n\n"
        "PRESERVE exactly:\n"
        "- All payout values, multipliers, and symbol names\n"
        "- All markdown tables (keep table structure intact)\n"
        "- All headings (## Page N, etc.)\n"
        "- All game feature descriptions and rules\n"
        "- Symbol tokens like [WILD], [SCATTER], [MARK], [5X], etc.\n\n"
        "Return only the cleaned markdown. No explanation, no preamble."
    )

    resp = requests.post(
        "https://openrouter.ai/api/v1/chat/completions",
        headers={
            "Authorization": f"Bearer {OPENROUTER_API_KEY}",
            "Content-Type": "application/json",
        },
        json={"model": CLEAN_MODEL, "messages": [
            {"role": "system", "content": system},
            {"role": "user",   "content": text},
        ]},
        timeout=120,
    )
    resp.raise_for_status()
    return resp.json()["choices"][0]["message"]["content"]


def extract_symbols(text):
    # Match [SYMBOL] and <SYMBOL> formats used in output
    bracket = re.findall(r'\[([A-Z0-9][A-Z0-9 +]*)\]', text)
    angle = re.findall(r'<([A-Z0-9][A-Z0-9 &+]*)>', text)
    symbols = {'[' + s.strip() + ']' for s in bracket}
    symbols |= {'<' + s.strip() + '>' for s in angle}
    return sorted(symbols)

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

    if OPENROUTER_API_KEY:
        print(f"Cleaning with LLM ({CLEAN_MODEL})...")
        try:
            md_clean = make_clean_llm(md_full)
        except Exception as e:
            print(f"  LLM clean failed ({e}), falling back to regex")
            md_clean = make_clean(md_full)
    else:
        print("  OPENROUTER_API_KEY not set — using regex clean")
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
