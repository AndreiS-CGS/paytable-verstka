# Setup instructions — for an AI agent (Claude Code) to execute

> **Try the window first.** Most of what follows is now automated: add the package to
> `Packages/manifest.json` (step 4), open **PlayStudios > Slot Tools > Paytable Tool**, and press
> **Re-check all**. It probes each item, fixes what it safely can, and shows the exact command and
> output behind every row. Come back here for the steps it cannot do — those are marked **ASK** —
> or when there is no working Unity project to open a window in.

You (the agent) are setting up the `paytable-verstka` pipeline on a new machine for a colleague.
This is not documentation to summarize — follow it as an ordered checklist, asking the human only
what's explicitly marked "ASK". Verify each step actually worked before moving to the next.

**Never report a step as done on an exit code alone where a real check is listed.** Every stale
failure this document has caused came from exactly that: pip reporting success while the package
resolved from somewhere else, a token file sitting on disk three months after it expired, a skill installed
but silently ignored because its frontmatter was malformed.

## 0. What you're setting up
Three Claude Code skills (`paytable-pipeline`, `cgs-atlas-builder`, `paytable-verstka`) plus the
Unity Package that carries them (`com.cgs.paytablelibrary`), consumed by the target Unity project
via a git URL in its `Packages/manifest.json`. The skills live INSIDE the package, under
`library/Skills~/` — Unity ignores `~`-suffixed folders, so they ship without becoming assets.
That is why there is no separate clone step for a normal install. Everything is in one repo:
**https://github.com/AndreiS-CGS/paytable-verstka** (private).

## 1. GitHub access
- **ASK** the human for their GitHub username, and tell them: someone with admin on the repo needs
  to add that username as a collaborator (Settings → Collaborators on the repo page, or
  `gh repo add-collaborator AndreiS-CGS/paytable-verstka <username>` run by whoever owns it — not
  you, you don't have that access).
- Check the human's own git/gh auth: `gh auth status`. If not logged in, or the active account
  doesn't have repo access, walk them through `gh auth login -h github.com` — this is interactive
  (opens a browser device-code flow), you cannot complete it for them. Wait for them to confirm
  before continuing.
- Verify access actually works before proceeding: `git ls-remote https://github.com/AndreiS-CGS/paytable-verstka.git`
  should list refs, not error. If it 403s/404s, access isn't granted yet — stop and say so plainly,
  don't guess around it.

## 2. Clone the repo locally — only if you are working ON the tooling
**Most people can skip this.** The skills ship inside the Unity package at `library/Skills~/`, so
step 4 delivers them along with the block library; there is nothing to clone separately.

Clone only to edit the skills or the library themselves:
```bash
git clone https://github.com/AndreiS-CGS/paytable-verstka.git <chosen-path>
```
Verify: `ls "<chosen-path>/library/Skills~"` shows all three skill folders.

## 3. Install the three skills into Claude Code
Do this with the Unity window — **PlayStudios > Slot Tools > Paytable Tool > Setup** — which copies
each skill out of the resolved package into `<git repo root>/.claude/skills/<name>` and stamps it
so it can tell you later whether it has drifted.

By hand, for each of `paytable-pipeline`, `cgs-atlas-builder`, `paytable-verstka`:
```bash
cp -R "<package>/Skills~/<name>" "<wherever-skills-load-from>/<name>"
```
Where `<wherever-skills-load-from>` is `~/.claude/skills` or a project-level `.claude/skills`
directory. Check for a plugin-managed skills directory too, and for the same skill installed in two
places — that is a silent version-skew source.

**Copy, do not symlink, when the package came from the git URL.** It resolves read-only under
`Library/PackageCache/` and is wiped on every re-resolve, so a symlink into it dangles silently and
the skill simply disappears mid-project. Symlink only from a local clone, when you are editing the
skills and want the edits live.

Verify by comparing file contents, not by resolving one symlink level, and confirm each installed
`SKILL.md` still has a valid YAML frontmatter block with `name` and `description` — a skill whose
frontmatter is malformed is silently ignored while looking perfectly installed.

## 4. Point the target Unity project at the library
- **ASK** which Unity project (path) this is for, if not already obvious from context.
- Edit that project's `Packages/manifest.json`, add:
  ```json
  "com.cgs.paytablelibrary": "https://github.com/AndreiS-CGS/paytable-verstka.git?path=library#main"
  ```
  Validate the JSON parses after editing (e.g. `python3 -c "import json; json.load(open(path))"`)
  before moving on — a broken manifest.json will stop Unity from opening the project at all.
- This step needs the SAME git access as step 1, but exercised by Unity's own git invocation — it
  should just work once step 1 is genuinely done, since it uses the same credential helper. If
  unityMCP is connected, verify with:
  ```csharp
  var list = UnityEditor.PackageManager.Client.List(true, true);
  while (!list.IsCompleted) System.Threading.Thread.Sleep(100);
  foreach (var p in list.Result) if (p.name == "com.cgs.paytablelibrary") return p.source + " " + p.resolvedPath;
  ```
  Expect `source=Git` and a `resolvedPath` under that project's `Library/PackageCache/`. If unityMCP
  isn't connected yet, tell the human to open the project in Unity once and check back — Package
  Manager resolution happens on project load / manifest change, not on demand.

## 5. unityMCP (separate prerequisite, not part of this repo)
`paytable-verstka`'s assembly phase needs a live Unity ↔ Claude Code bridge (the `unityMCP` tools you
are presumably already using to do all of the above). If this machine doesn't have it yet, that's a
separate install (the `com.coplaydev.unity-mcp` Unity package + whatever MCP server config Claude
Code needs) — outside this repo's scope. Check whether unityMCP tools are already available to you on
this machine before assuming you need to set that up too.

## 6. Python environment (needed by paytable-pipeline and cgs-atlas-builder)
One venv at a fixed location. The scripts find it themselves — each one re-execs into it on
startup — so nothing needs activating and it does not matter which `python3` invokes them.

```bash
python3 -m venv ~/.venvs/paytable-tools
~/.venvs/paytable-tools/bin/python -m pip install --only-binary=:all: -r requirements.txt
```

All four packages are required: `requests`, `pillow`, `numpy`, `scipy`. An earlier version of this
document listed only two, which is why installs kept ending up half-working. (`browser-cookie3`
was a fifth until cookie auth was removed.)

`--only-binary=:all:` matters: without a wheel, pip attempts a source build that runs for minutes
and fails with a wall of compiler output. With it you get an immediate, readable "no matching
distribution", and the fix is to build the venv from a different base interpreter. Prefer a
python.org or pyenv 3.11-3.13 base over the newest minor release — wheels for `scipy` and `pillow`
lag a new Python by weeks.

**Do not use `pip install --target ~/.local/lib/python-extra`.** Earlier instructions recommended
it; the script used to put that directory *ahead* of the venv on `sys.path`, so packages installed
there silently shadowed the venv's. It now comes after the venv, but the directory remains an
unversioned trap — remove it if it exists.

Verify by importing, not by trusting pip's exit code:
```bash
~/.venvs/paytable-tools/bin/python -c "import requests, PIL, numpy, scipy; print('ok')"
```

### When the window cannot find Python at all

Discovery is a pile of guesses about where an installer put things, so it can lose. The escape
hatch is the **Interpreter** field on the Python row: point it at the interpreter and both the
window and the skills' scripts use it. It writes `PAYTABLE_PYTHON` into the config file
(`%APPDATA%\paytable-tools\config.json`, or `~/.config/paytable-tools/config.json`), which
`_bootstrap.py` already reads — one setting, not two. The environment variable of the same name
still wins if it is set.

On **Windows** the usual cause is not a missing Python but a stale environment: Unity reads `PATH`
once, when it launches, so an interpreter installed while Unity was open is invisible to it even
though a fresh terminal finds it fine. Restarting Unity fixes that; the field fixes it without a
restart.

Two Windows traps worth knowing, both of which the tool now handles:
- The **all-users** install goes to `C:\Program Files\Python3xx`. Anything that splits `py -0p`
  output on whitespace mangles that path at the space and drops the install silently.
- A zero-length `python.exe` under `WindowsApps` is the Microsoft Store **alias stub**. It exists,
  it is on `PATH`, and running it opens the Store — so `File.Exists` is a guaranteed false pass.

## 7. Confluence access (for paytable-pipeline)
- **ASK** the human to create an Atlassian API token: the plain **Create API token** button at
  `id.atlassian.com/manage-profile/security/api-tokens`, **not** "Create API token with scopes".
  Scoped tokens are addressed through `api.atlassian.com/ex/confluence/<cloudId>/...`; this
  pipeline talks to the site URL directly. Tell them to pick the longest expiry offered.
- Store it at `~/.confluence_pat`, mode 600, and set `CONFLUENCE_EMAIL` to the account that
  created it. Both go in through the Unity window, or by hand into `~/.confluence_pat` and
  `~/.config/paytable-tools/config.json` — never `~/.zshrc`, which is shell-specific, does nothing
  on Windows, and is invisible to a GUI-launched Unity.
- **Verify against the server, not the filesystem.** `GET /wiki/rest/api/user/current` with the
  Basic header must return 200 and the expected account. The previous token here sat on disk for
  three months after expiring, and every check that looked only at the file called it configured.
  `env_doctor.py --kv` reports this as `confluence.token_state`.
- Both settings are mandatory: **401** means the pair was read and rejected (expired, revoked, or
  a different account); **403** means it was not accepted as credentials at all, which is what
  `CONFLUENCE_EMAIL` being empty produces.
- There is no browser step. Cookie auth was removed once a token was measured to fetch page text,
  the attachment list and the images — the image download redirects to a pre-signed media URL.

## 8. Final smoke test
Once steps 1-7 are done, confirm the whole chain works end to end:
1. A skill listing shows all three skills.
2. `git ls-remote` on the repo succeeds (access is real, not just locally cached).
3. Unity resolves `com.cgs.paytablelibrary` with `source=Git` (step 4's check).
4. This compiles and runs cleanly (proves the C# utilities actually loaded, not just the prefabs):
   ```csharp
   var dist = CGS.PaytableLibrary.PaytableGridMath.DistributeRows(5);
   return string.Join(",", dist); // expect "3,2"
   ```
If (4) fails with "the name 'CGS' does not exist" but the package resolved fine in (3), check the
Unity console for compile errors — they may be pre-existing and unrelated to this package (that
happened during the original setup: some other broken script blocked ALL new assemblies from
loading, and the actual fix was fixing that unrelated compile error, not this package).

Report back to the human plainly which of these actually passed — don't report success on steps you
didn't verify.
