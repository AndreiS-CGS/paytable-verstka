#!/usr/bin/env python3
"""Report the state of the paytable tooling environment as one JSON object.

Consumed by the Unity Setup tab (PlayStudios > Slot Tools > Paytable Tool), and useful on its own:

    python3 env_doctor.py            # JSON
    python3 env_doctor.py --human    # readable

STDLIB ONLY, and deliberately does NOT import _bootstrap. This is the one script that has to run
*before* the venv exists, under whatever interpreter happens to be around. Adding a dependency here
would mean the diagnostic tool needs the thing it is diagnosing.

Two things it refuses to do, both on purpose:

  * It never decrypts a cookie. Chrome's cookie values are encrypted and reading them triggers a
    macOS Keychain prompt — a terrible surprise to get from merely opening a window. `host_key` is
    stored in plaintext, so a read-only SQLite query answers "is this profile logged into Confluence"
    without touching a single encrypted byte.
  * It never reads, prints or returns the Confluence token. Existence, size and permission bits only.
"""

import argparse
import glob
import json
import os
import re
import sqlite3
import subprocess
import sys
import tempfile
import shutil
from pathlib import Path

REQUIRED = ("requests", "browser_cookie3", "PIL", "numpy", "scipy")
VENV_DIR = Path.home() / ".venvs" / "paytable-tools"
CONFLUENCE_HOST_SUFFIX = "atlassian.net"

# Reported so the window can show what is actually in effect. No secrets: the PAT lives in a file
# and OPENROUTER_* is gone from this codebase entirely.
TRACKED_ENV = (
    "PAYTABLE_PYTHON", "PAYTABLE_OUT", "CHROME_PROFILE", "CHROME_COOKIE_FILE",
    "CONFLUENCE_EMAIL", "CONFLUENCE_PAT_FILE", "PYTHONPATH", "PYTHONHOME", "VIRTUAL_ENV",
)

# What each interpreter is asked about itself. -I (isolated) is important: it ignores PYTHONPATH
# and user site-packages, so the answer describes the interpreter rather than the caller's shell.
INTERROGATE = (
    "import sys,json,sysconfig,struct,importlib.util as u;"
    "print(json.dumps({"
    "'executable':sys.executable,"
    "'base_prefix':sys.base_prefix,"
    "'version':'.'.join(map(str,sys.version_info[:3])),"
    "'version_info':list(sys.version_info[:2]),"
    "'is_venv':sys.prefix!=sys.base_prefix,"
    "'has_venv':u.find_spec('venv') is not None,"
    "'has_ensurepip':u.find_spec('ensurepip') is not None,"
    "'bits':8*struct.calcsize('P'),"
    "'platform':sysconfig.get_platform()}))"
)


def _run(argv, timeout=15):
    try:
        p = subprocess.run(argv, capture_output=True, text=True, timeout=timeout)
        return p.returncode, p.stdout, p.stderr
    except Exception as e:
        return -1, "", f"{type(e).__name__}: {e}"


# ── interpreters ────────────────────────────────────────────────────────────

def _candidate_interpreters():
    out = []
    if os.name == "nt":
        rc, so, _ = _run(["py", "-0p"])
        if rc == 0:
            out += re.findall(r"(\S+python\.exe)", so, re.I)
        for pat in (r"%LOCALAPPDATA%\Programs\Python\Python3*\python.exe", r"C:\Python3*\python.exe"):
            out += glob.glob(os.path.expandvars(pat))
    else:
        out += ["/opt/homebrew/bin/python3", "/usr/local/bin/python3", "/usr/bin/python3"]
        out += sorted(glob.glob("/Library/Frameworks/Python.framework/Versions/*/bin/python3"))
        # ~/.pyenv/versions, never ~/.pyenv/shims: a shim is a shell script, not an interpreter,
        # and reports whatever pyenv currently points at.
        out += sorted(glob.glob(os.path.expanduser("~/.pyenv/versions/*/bin/python3")))
    w = shutil.which("python3") or shutil.which("python")
    if w:
        out.append(w)
    return out


def _disqualify(info, path):
    # A zero-byte python.exe under WindowsApps is a Microsoft Store alias stub: it exists, it is
    # on PATH, and running it opens the Store. Guaranteed false pass for any File.Exists check.
    if os.name == "nt" and "windowsapps" in path.lower():
        return "Microsoft Store alias stub, not a real interpreter"
    if tuple(info["version_info"]) < (3, 10):
        return f"Python {info['version']} is too old (numpy>=2 and scipy need 3.10+)"
    if info["is_venv"]:
        return "is itself a venv — never build a venv from a venv"
    if not info["has_venv"] or not info["has_ensurepip"]:
        return "missing the venv/ensurepip modules (on Debian/Ubuntu: apt install python3-venv)"
    if info["bits"] != 64:
        return "not 64-bit"
    return None


def scan_interpreters():
    seen, results = {}, []
    for path in _candidate_interpreters():
        if not os.path.isfile(path):
            continue
        rc, so, se = _run([path, "-I", "-c", INTERROGATE])
        if rc != 0 or not so.strip():
            results.append({"invoked_as": path, "ok": False,
                            "error": (se or so).strip()[:200] or f"exit {rc}"})
            continue
        try:
            info = json.loads(so.strip().splitlines()[-1])
        except Exception as e:
            results.append({"invoked_as": path, "ok": False, "error": f"unparseable: {e}"})
            continue
        # Dedupe on the CHILD-REPORTED base_prefix, never the path invoked. That is what collapses
        # a pyenv shim onto its real interpreter and turns "six pythons" into the three or four
        # that actually exist.
        key = info["base_prefix"]
        if key in seen:
            seen[key]["also_invocable_as"].append(path)
            continue
        info.update({"invoked_as": path, "ok": True, "also_invocable_as": [],
                     "disqualified": _disqualify(info, path)})
        seen[key] = info
        results.append(info)
    return results


# ── dependencies ────────────────────────────────────────────────────────────

DEP_PROBE = (
    "import sys,os,json,importlib\n"
    # Mirror the real script's own sys.path mutation, or the probe measures a different program
    # than the one that runs.
    "sys.path.append(os.path.expanduser('~/.local/lib/python-extra'))\n"
    "out={'executable':sys.executable,'prefix':sys.prefix,'base_prefix':sys.base_prefix,'mods':{}}\n"
    "for m in %r:\n"
    "    try:\n"
    "        mod=importlib.import_module(m)\n"
    "        out['mods'][m]={'ok':True,'file':getattr(mod,'__file__',None),"
    "'version':getattr(mod,'__version__',None)}\n"
    # BaseException, not Exception: a mismatched-ABI .so can raise SystemError or worse.
    "    except BaseException as e:\n"
    "        out['mods'][m]={'ok':False,'error':type(e).__name__+': '+str(e)[:160]}\n"
    "print('@@'+json.dumps(out)+'@@')\n"
) % (list(REQUIRED),)


def probe_deps(python_path):
    if not python_path or not os.path.isfile(str(python_path)):
        return {"interpreter": str(python_path) if python_path else None, "exists": False}
    rc, so, se = _run([str(python_path), "-c", DEP_PROBE], timeout=60)
    # Sentinel-delimited: pip shims, site hooks and dyld warnings all write to stdout.
    m = re.search(r"@@(.*?)@@", so, re.S)
    if not m:
        return {"interpreter": str(python_path), "exists": True, "ok": False,
                "error": (se or so).strip()[:300] or f"exit {rc}"}
    data = json.loads(m.group(1))
    data["exists"] = True
    # sys.prefix as reported by the child IS the venv root. Do not derive it by resolving the
    # interpreter path: venv/bin/python is a symlink to the base interpreter, so .resolve() walks
    # out of the venv entirely and every module then looks shadowed.
    venv_root = data.get("prefix") or str(Path(python_path).parent.parent)
    for name, r in data["mods"].items():
        f = r.get("file") or ""
        # Importable but resolved from outside the venv is a WARNING, not a pass. That is the
        # ~/.local/lib/python-extra case, which used to make every check look green.
        r["inside_venv"] = bool(f) and f.startswith(venv_root)
        if r["ok"] and not r["inside_venv"]:
            r["shadowed_by"] = os.path.dirname(f)
    data["ok"] = all(r["ok"] for r in data["mods"].values())
    data["all_inside_venv"] = all(r.get("inside_venv") for r in data["mods"].values() if r["ok"])
    return data


# ── chrome ──────────────────────────────────────────────────────────────────

def _chrome_base_dirs():
    if sys.platform == "darwin":
        return [os.path.expanduser("~/Library/Application Support/Google/Chrome")]
    if os.name == "nt":
        local = os.environ.get("LOCALAPPDATA")
        return [os.path.join(local, "Google", "Chrome", "User Data")] if local else []
    return [os.path.expanduser("~/.config/google-chrome"),
            os.path.expanduser("~/.config/chromium")]


def _natural_key(name):
    return [int(t) if t.isdigit() else t.lower() for t in re.split(r"(\d+)", name)]


def _display_names(base):
    """Human profile names, so the window can offer "CGS_" instead of "Profile 3"."""
    names = {}
    try:
        with open(os.path.join(base, "Local State"), encoding="utf-8") as f:
            cache = json.load(f).get("profile", {}).get("info_cache", {})
        for d, meta in cache.items():
            n = meta.get("name")
            if n:
                names[d] = n
    except Exception:
        pass
    return names


def _count_confluence_cookies(cookie_file):
    """Count Confluence host entries WITHOUT decrypting anything (host_key is plaintext).

    Copies the DB first: Chrome holds a lock while running, which is the normal state.
    """
    tmp = None
    try:
        fd, tmp = tempfile.mkstemp(suffix=".sqlite")
        os.close(fd)
        shutil.copyfile(cookie_file, tmp)
        con = sqlite3.connect(f"file:{tmp}?mode=ro", uri=True)
        try:
            rows = con.execute(
                "SELECT COUNT(DISTINCT host_key) FROM cookies WHERE host_key LIKE ?",
                ("%" + CONFLUENCE_HOST_SUFFIX,)).fetchone()
            return int(rows[0]) if rows else 0, None
        finally:
            con.close()
    except Exception as e:
        return None, f"{type(e).__name__}: {e}"
    finally:
        if tmp and os.path.exists(tmp):
            try:
                os.remove(tmp)
            except OSError:
                pass


def scan_chrome():
    profiles = []
    for base in _chrome_base_dirs():
        if not os.path.isdir(base):
            continue
        names = _display_names(base)
        try:
            dirs = sorted(os.listdir(base), key=_natural_key)
        except OSError:
            continue
        for d in dirs:
            if d != "Default" and not d.startswith("Profile "):
                continue
            for parts in (("Network", "Cookies"), ("Cookies",)):
                cand = os.path.join(base, d, *parts)
                if not os.path.exists(cand):
                    continue
                n, err = _count_confluence_cookies(cand)
                profiles.append({
                    "dir": d,
                    "display_name": names.get(d),
                    "cookie_file": cand,
                    "confluence_hosts": n,
                    "error": err,
                    "mtime": os.path.getmtime(cand),
                })
                break
    return profiles


# ── confluence config ───────────────────────────────────────────────────────

def scan_confluence():
    pat = os.path.expanduser(os.environ.get("CONFLUENCE_PAT_FILE", "~/.confluence_pat"))
    exists = os.path.isfile(pat)
    email = os.environ.get("CONFLUENCE_EMAIL", "")
    info = {
        "pat_file": pat,
        "pat_file_exists": exists,
        "pat_file_mode": oct(os.stat(pat).st_mode & 0o777) if exists else None,
        "pat_file_size": os.path.getsize(pat) if exists else None,
        "email_set": bool(email),
        "email": email or None,
    }
    # The live 403 trap: with a token and no identity the header becomes base64(":token"), and
    # Confluence answers "Current user not permitted to use Confluence" with no further clue.
    info["pat_without_email"] = bool(exists and not email)
    return info


# ── main ────────────────────────────────────────────────────────────────────

def collect():
    venv_python = VENV_DIR / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    return {
        "schema": 1,
        "platform": {"sys_platform": sys.platform, "os_name": os.name,
                     "running_under": sys.executable},
        "venv": {"dir": str(VENV_DIR), "python": str(venv_python),
                 "exists": venv_python.is_file()},
        "interpreters": scan_interpreters(),
        "deps": probe_deps(venv_python),
        "chrome": scan_chrome(),
        "confluence": scan_confluence(),
        "env": {k: os.environ.get(k) for k in TRACKED_ENV},
        "python_extra_present": os.path.isdir(os.path.expanduser("~/.local/lib/python-extra")),
    }


def human(d):
    out = []
    v = d["venv"]
    out.append(f"venv: {v['python']}  {'OK' if v['exists'] else 'MISSING'}")
    deps = d["deps"]
    if deps.get("exists") and "mods" in deps:
        for m, r in deps["mods"].items():
            if not r["ok"]:
                out.append(f"  {m:18} MISSING  {r['error']}")
            elif not r["inside_venv"]:
                out.append(f"  {m:18} SHADOWED by {r.get('shadowed_by')}")
            else:
                out.append(f"  {m:18} ok {r.get('version') or ''}")
    out.append("")
    out.append("interpreters:")
    for i in d["interpreters"]:
        if not i.get("ok"):
            out.append(f"  {i['invoked_as']}  -> {i.get('error')}")
        else:
            note = i["disqualified"] or "usable"
            out.append(f"  {i['version']:8} {i['invoked_as']}  [{note}]")
    out.append("")
    out.append("chrome profiles:")
    for p in d["chrome"]:
        label = p["display_name"] or p["dir"]
        n = p["confluence_hosts"]
        out.append(f"  {p['dir']:12} {label:28} confluence hosts: "
                   f"{n if n is not None else 'unreadable (' + str(p['error']) + ')'}")
    c = d["confluence"]
    out.append("")
    out.append(f"confluence: PAT file {'present' if c['pat_file_exists'] else 'absent'}"
               f"{' mode ' + c['pat_file_mode'] if c['pat_file_mode'] else ''}, "
               f"email {'set' if c['email_set'] else 'NOT SET'}")
    if c["pat_without_email"]:
        out.append("  WARNING: PAT present with no CONFLUENCE_EMAIL — Basic auth cannot work "
                   "and would 403; the script falls back to cookies.")
    if d["python_extra_present"]:
        out.append("  NOTE: ~/.local/lib/python-extra exists. It is unversioned and used to "
                   "shadow the venv; remove it once the venv is healthy.")
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--human", action="store_true", help="readable text instead of JSON")
    args = ap.parse_args()
    data = collect()
    print(human(data) if args.human else json.dumps(data, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
