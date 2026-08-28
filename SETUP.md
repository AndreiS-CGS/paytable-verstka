# Setup instructions — for an AI agent (Claude Code) to execute

You (the agent) are setting up the `paytable-verstka` pipeline on a new machine for a colleague.
This is not documentation to summarize — follow it as an ordered checklist, asking the human only
what's explicitly marked "ASK". Verify each step actually worked before moving to the next; don't
assume success from a command's exit code alone where a real check is listed.

## 0. What you're setting up
Three Claude Code skills (`paytable-pipeline`, `cgs-atlas-builder`, `paytable-verstka`) plus a Unity
Package (`com.cgs.paytablelibrary`) that a target Unity project consumes via a git URL in its
`Packages/manifest.json`. All four live in one repo:
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

## 2. Clone the repo locally
Skills need an actual local clone (symlinks can't point at GitHub directly). Pick a sensible path —
**ASK** where, or default to `~/Documents/Unity/paytable-verstka` if they have no preference:
```bash
git clone https://github.com/AndreiS-CGS/paytable-verstka.git <chosen-path>
```
Verify: `ls <chosen-path>/skills` shows all three skill folders.

## 3. Symlink the three skills into Claude Code
Find where THIS machine's Claude Code actually loads skills from — don't assume `~/.claude/skills`
is the only place; check for a plugin-managed skills directory too (look at how any already-working
skill on this machine is set up, or check config). Then, for each of `paytable-pipeline`,
`cgs-atlas-builder`, `paytable-verstka`:
```bash
cp -R "<chosen-path>/library/Skills~/<name>" "<wherever-skills-load-from>/<name>"
```
Verify: after creating them, confirm the skill list picks up all three (a fresh skill listing should
show them, and compare file contents against `<chosen-path>/library/Skills~/<name>` — resolving
one symlink level is not proof).

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
Both need real Python packages that aren't vendored in this repo. Use a venv (portable, no
system-Python pollution) unless the human has their own preferred way:
```bash
python3 -m venv ~/.venvs/paytable-tools
source ~/.venvs/paytable-tools/bin/activate
pip install browser-cookie3 pillow
```
`paytable-pipeline`'s script auto-adds `~/.local/lib/python-extra` to its path as a fallback lookup
location — if the human prefers that over a venv, `pip install browser-cookie3 --target ~/.local/lib/python-extra`
works too. Either is fine; just make sure whichever Python actually runs the script can `import
browser_cookie3` and `import PIL`.

## 7. Confluence access (for paytable-pipeline)
- **ASK** which Chrome profile on this machine is logged into `playstudios.atlassian.net` (their own
  CGS Confluence account — not yours, not a shared one).
- Set that as an env var the script will read — `CHROME_PROFILE="Profile N"` (matching Chrome's own
  profile directory name) or `CHROME_COOKIE_FILE=/full/path/to/that/profile/Cookies`. Persist it in
  their shell profile (`~/.zshrc` etc.) if they'll use this regularly, not just export it for one
  session.
- Verify by actually running an extraction against a real GDD URL once everything else is set up —
  don't declare this step done on config alone.

## 8. OPENROUTER_API_KEY — not needed, skip this entirely
`paytable-pipeline`'s script has an LLM-cleaning step that calls OpenRouter if this key is set,
otherwise it falls back to a cruder regex clean. But since YOU (the agent running the skill) are
already an LLM in the loop, just do that cleaning pass yourself when the skill calls for it — see
`skills/paytable-pipeline/SKILL.md` Step 2. Don't ask the human for this key; there's no reason to
pay for a second LLM call for something you can already do inline.

## 9. Final smoke test
Once steps 1-6 are done, confirm the whole chain works end to end:
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
