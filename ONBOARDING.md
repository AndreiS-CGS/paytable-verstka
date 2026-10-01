# Getting started — for a person setting this up on their own machine

You are about to install a pipeline that turns a Confluence GDD link into a finished
`PaytableDialog<Game>` Unity prefab. It is three Claude Code skills, a library of UI blocks, and a
Unity window that checks your setup. All of it is one Unity package, so one install gets everything.

Budget about half an hour, most of it waiting on other people for access.

> **Which document do I read?**
> - **This one** if you are setting the pipeline up to use it.
> - [README.md](README.md) if you want to know what it is and how it is put together.
> - [SETUP.md](SETUP.md) if you are a Claude Code agent doing the install for someone — it is an
>   ordered checklist with verification steps, not prose.

---

## Start these two first — they block everything and depend on other people

**1. Repo access.** The library lives at `https://github.com/AndreiS-CGS/paytable-verstka` and it is
private. Ask whoever owns it to add your GitHub username as a collaborator. You will know it worked
when this lists refs instead of failing:

```bash
git ls-remote https://github.com/AndreiS-CGS/paytable-verstka.git
```

**2. An Atlassian API token.** Create it at
`id.atlassian.com/manage-profile/security/api-tokens` with the plain **Create API token** button —
**not** "Create API token with scopes". Scoped tokens are addressed through a different host than
this pipeline talks to, and they fail in a way that looks like a wrong password. Pick the longest
expiry offered. Keep the token on screen; you will paste it in step 4.

While you wait for the invite, do step 3.

---

## 3. Python

You need **3.11, 3.12 or 3.13**. The hard floor is 3.10; prefer not to take the newest release the
day it lands, because `scipy` and `pillow` wheels lag a new Python by weeks and without a wheel the
install fails in a wall of compiler output.

- **macOS**: `brew install python@3.13`, or python.org. The system Python is 3.9 and too old.
- **Windows**: python.org, and tick both **"Add python.exe to PATH"** and the **py launcher**.

Nothing to configure after this — the window builds a virtual environment at
`~/.venvs/paytable-tools` for you, and the skills' scripts re-exec themselves into it, so it does
not matter which `python3` ends up calling them.

**If you install Python while Unity is already open, restart Unity.** Unity reads `PATH` once, at
launch, so an interpreter installed afterwards is invisible to it even though a fresh terminal finds
it fine. This is the single most common "the tool cannot see my Python" report.

## 4. The Unity project and the window

Add one line to your Unity project's `Packages/manifest.json`, inside `"dependencies"`:

```json
"com.cgs.paytablelibrary": "https://github.com/AndreiS-CGS/paytable-verstka.git?path=library#main"
```

Check the file still parses before you switch to Unity — a broken `manifest.json` stops the project
opening at all:

```bash
python3 -c "import json; json.load(open('Packages/manifest.json')); print('ok')"
```

Click into Unity. It re-resolves packages **on window focus**, not on demand, so nothing happens
until you do. Then open **PlayStudios → Slot Tools → Paytable Tool** and press **Re-check all**.

Work down the Setup tab. Each row says what it probed, shows the exact command and its full output
behind a foldout, and offers a button where there is something safe to press:

| Row | What to do |
|---|---|
| Package resolved | Should be green once Unity has fetched it. |
| Python environment | **Create venv**, then it installs the four packages itself. |
| Skills installed | **Install / update** — copies the three skills into `.claude/skills/`. |
| Confluence access | Paste your email and the token from step 2, **Save settings**. |
| unityMCP | See step 5. |

Statuses are four-valued, not pass/fail. A probe that timed out or could not find its tool reports
**Blocked**, never Ok — that distinction is the whole point of the window, because the procedure it
replaced kept reporting success nobody had verified.

The Confluence row checks your token **against the server**, not against the filesystem. A previous
token here sat on disk for three months after expiring while every file-based check called it
configured.

## 5. unityMCP

The assembly phase drives Unity from Claude Code, which needs the `com.coplaydev.unity-mcp` package
plus its MCP server registered in Claude Code. That is a separate install and outside this repo.

**You can work without it.** Phases 1–4 — pulling the GDD, extracting symbols, building the atlas —
are plain filesystem work. The skill notices unityMCP is absent, runs those, and stops before
assembly with a report, instead of failing halfway.

## 6. Check it actually works

The window going green is necessary, not sufficient. Ask Claude Code to run this in Unity:

```csharp
var dist = CGS.PaytableLibrary.PaytableGridMath.DistributeRows(5);
return string.Join(",", dist);   // expect "3,2"
```

That proves the package's C# compiled and loaded, which the Package row alone does not. If it fails
with *"the name 'CGS' does not exist"* while the package resolved fine, look in the Unity console
for **unrelated** compile errors — one broken script anywhere blocks every new assembly from
loading, and that is the thing to fix, not this package.

---

## Running your first paytable

Open the **Run** tab, fill in:

- **Game name** — the human-readable title, as it should read on the page. Not the slot id.
- **Slot id** — the bundle folder name, lowercase. The bundle path derives itself from this; press
  **Reset** next to the field if it looks wrong.
- **Confluence GDD URL** — copy it from Confluence's own share button. The address bar sometimes
  gives a form with no `/pages/<number>/` segment, which the extractor cannot use.
- **Sprite/asset prefix** — e.g. `S_Symbol_`.
- **Art path** — leave empty. Phase 4 searches the bundle itself.

**Compose prompt**, **Copy**, paste into Claude Code. The window deliberately does not launch the
run: the skill has human checkpoints — the art gate, the review steps — that a headless run would
stall on.

You can also just talk to the skills. Each works alone: ask to pull a paytable from Confluence and
only `paytable-pipeline` runs; ask to pack an atlas and only `cgs-atlas-builder` does.

## Keeping it current

A git package **pins the commit it first resolved** and never looks again. Pushing to the library
changes nothing for you until you ask.

1. **Check for updates → Update** in the Setup tab.
2. Then **Install / update** on the Skills row as well.

Step 2 is not optional. The skills were *copied* out of the package, so updating the package leaves
your copies on the old version. Watch the Skills row: it turns amber when they have drifted.

---

## When something is wrong

**"No Python interpreter found" and Python is definitely installed.** Restart Unity first — see
step 3. If it persists, open the row's `details`: it lists every place it looked and whether each
existed. Last resort, the **Interpreter** field on that row: point it at your `python.exe` or
`python3` and press **Use**. It writes `PAYTABLE_PYTHON` to the config file the scripts already
read, so one setting covers the window and the skills.

**Windows, and it still cannot find it.** Two traps, both of which the tool now handles, but worth
recognising:
- An **all-users** install goes to `C:\Program Files\Python3xx`, not your user folder.
- A zero-length `python.exe` under `WindowsApps` is the Microsoft Store **alias stub**. It exists,
  it is on `PATH`, and running it opens the Store.

**Confluence says 401 or 403.** 401 means the pair was read and rejected — expired, revoked, or a
different account. 403 usually means `CONFLUENCE_EMAIL` is empty, so the token was not accepted as
credentials at all. Both settings are mandatory.

**A skill behaves like an older version.** Check you do not have it installed in two places — a
user-level `~/.claude/skills/<name>` and a project-level `.claude/skills/<name>` both exist, and the
stale one wins silently. Check for leftover empty folders there too: an interrupted install can
leave a directory with no `SKILL.md`, which is not a valid skill but does clutter the listing.

**The Unity console shows a compile error in the package.** Report it rather than editing the
package: a git-resolved package lives read-only under `Library/PackageCache/` and is **wiped on
every re-resolve**, so any fix you make there disappears without warning.

---

## If you are going to work on the library itself

Don't consume it by git URL. Point the project at your clone so edits are live, and symlink the
skills instead of copying them — [README.md](README.md), "Working on the tooling itself", has the
commands and the handful of things that bite (`.meta` files, the `Library/` vs `library/` gitignore
trap, `tools/package.sh`).
