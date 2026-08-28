#!/usr/bin/env python3
"""Report the state of the paytable tooling environment as one JSON object.

Consumed by the Unity Setup tab (PlayStudios > Slot Tools > Paytable Tool), and useful on its own:

    python3 env_doctor.py            # JSON
    python3 env_doctor.py --human    # readable

STDLIB ONLY, and deliberately does NOT import _bootstrap. This is the one script that has to run
*before* the venv exists, under whatever interpreter happens to be around. Adding a dependency here
would mean the diagnostic tool needs the thing it is diagnosing.

Two things it refuses to do, both on purpose:

  * It never reads, prints or returns the Confluence token. It builds the auth header to ask
    Confluence who the token belongs to, and reports only the answer.
  * It never touches a browser. Cookie auth is gone from this pipeline entirely — a token fetches
    page text, the attachment list and the images, so cookies bought nothing and cost a macOS
    Keychain prompt, a native dependency, and support for exactly one browser.
"""

import argparse
import base64
import glob
import json
import os
import re
import subprocess
import sys
import shutil
import ssl
import urllib.error
import urllib.request
from pathlib import Path

REQUIRED = ("requests", "PIL", "numpy", "scipy")
VENV_DIR = Path.home() / ".venvs" / "paytable-tools"
DEFAULT_CONFLUENCE_BASE = "https://playstudios.atlassian.net"

# Reported so the window can show what is actually in effect. No secrets: the PAT lives in a file
# and OPENROUTER_* is gone from this codebase entirely.
TRACKED_ENV = (
    "PAYTABLE_PYTHON", "PAYTABLE_OUT", "CONFLUENCE_EMAIL", "CONFLUENCE_PAT_FILE",
    "CONFLUENCE_BASE_URL", "PYTHONPATH", "PYTHONHOME", "VIRTUAL_ENV",
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


def _config_path():
    if os.name == "nt":
        base = os.environ.get("APPDATA")
        return os.path.join(base, "paytable-tools", "config.json") if base else None
    base = os.environ.get("XDG_CONFIG_HOME") or os.path.join(os.path.expanduser("~"), ".config")
    return os.path.join(base, "paytable-tools", "config.json")


_CONFIG_CACHE = None


def _config():
    global _CONFIG_CACHE
    if _CONFIG_CACHE is None:
        _CONFIG_CACHE = {}
        p = _config_path()
        if p and os.path.isfile(p):
            try:
                with open(p, encoding="utf-8") as f:
                    _CONFIG_CACHE = json.load(f)
            except Exception:
                pass
    return _CONFIG_CACHE


def setting(name, default=""):
    """Same precedence the scripts use: real environment first, then the config file.

    The doctor MUST read the config file too. Reading only os.environ made it report a correctly
    configured machine as broken — the settings were saved, the extraction script would have used
    them, and this said CONFLUENCE_EMAIL was unset. A checker that lies about state is worse than
    no checker.
    """
    v = os.environ.get(name)
    if v not in (None, ""):
        return v, "env"
    v = _config().get(name)
    if v not in (None, ""):
        return v, "config"
    return default, "unset"


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


# ── confluence config ───────────────────────────────────────────────────────

def probe_token(email, base_url, timeout=12):
    """Ask Confluence who this token belongs to.

    The only check that proves a token works. Presence of ~/.confluence_pat proves nothing: the
    previous token here sat on disk for three months after expiring, and every check that looked
    only at the file reported it as configured.

    Reads the token to build the header and never returns, prints or logs it.
    """
    pat_setting, _ = setting("CONFLUENCE_PAT_FILE", "~/.confluence_pat")
    path = os.path.expanduser(pat_setting)
    if not os.path.isfile(path):
        return {"state": "absent"}
    if not email:
        return {"state": "no_email"}
    try:
        with open(path, encoding="utf-8") as f:
            token = f.read().strip()
    except Exception as e:
        return {"state": "unreadable", "detail": f"{type(e).__name__}: {e}"}
    if not token:
        return {"state": "empty"}

    cred = base64.b64encode(f"{email}:{token}".encode()).decode()
    req = urllib.request.Request(
        base_url.rstrip("/") + "/wiki/rest/api/user/current",
        headers={"Accept": "application/json", "Authorization": "Basic " + cred})

    # macOS python.org builds ship without a wired-up CA store, so plain urllib fails TLS with
    # CERTIFICATE_VERIFY_FAILED until someone runs Install Certificates.command. requests never
    # hits this because it bundles certifi — so borrow certifi when it happens to be importable
    # (it is, in the venv, as a requests dependency) and stay stdlib-only when it is not.
    ctx = None
    try:
        import certifi
        ctx = ssl.create_default_context(cafile=certifi.where())
    except Exception:
        ctx = None

    try:
        with urllib.request.urlopen(req, timeout=timeout, context=ctx) as r:
            body = json.loads(r.read().decode("utf-8", "replace"))
            return {"state": "ok",
                    "account": body.get("email") or body.get("displayName") or "(unnamed)"}
    except urllib.error.HTTPError as e:
        # 401 means the pair was parsed and rejected — expired, revoked, or a different account.
        # 403 means it was not accepted as credentials at all and the request fell through to
        # anonymous. The distinction is worth keeping: it says whether to re-issue or to look
        # at the header construction.
        return {"state": "rejected", "http": e.code}
    except urllib.error.URLError as e:
        # A TLS trust failure is NOT "offline", and calling it that sends people to check their
        # network instead of their Python install. Report it as its own thing, with the fix.
        text = str(e)
        if "CERTIFICATE_VERIFY_FAILED" in text or isinstance(
                getattr(e, "reason", None), ssl.SSLCertVerificationError):
            return {"state": "tls_untrusted", "detail": text[:200]}
        return {"state": "unreachable", "detail": f"{type(e).__name__}: {text[:160]}"}
    except Exception as e:
        # Offline is not the same as unauthorised, and must never be reported as a failed token.
        return {"state": "unreachable", "detail": f"{type(e).__name__}: {e}"}


def scan_confluence():
    pat_setting, _ = setting("CONFLUENCE_PAT_FILE", "~/.confluence_pat")
    pat = os.path.expanduser(pat_setting)
    exists = os.path.isfile(pat)
    email, email_source = setting("CONFLUENCE_EMAIL")
    info = {
        "pat_file": pat,
        "pat_file_exists": exists,
        "pat_file_mode": oct(os.stat(pat).st_mode & 0o777) if exists else None,
        "pat_file_size": os.path.getsize(pat) if exists else None,
        "email_set": bool(email),
        "email": email or None,
        "email_source": email_source,
    }
    info["pat_without_email"] = bool(exists and not email)
    base, _ = setting("CONFLUENCE_BASE_URL", DEFAULT_CONFLUENCE_BASE)
    info["base_url"] = base
    info["token"] = probe_token(email, base)
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
    c = d["confluence"]
    out.append("")
    out.append(f"confluence: PAT file {'present' if c['pat_file_exists'] else 'absent'}"
               f"{' mode ' + c['pat_file_mode'] if c['pat_file_mode'] else ''}, "
               f"email {'set (' + c['email_source'] + ')' if c['email_set'] else 'NOT SET'}"
)
    t = c["token"]
    if t["state"] == "ok":
        out.append(f"  token: VALID, authenticated as {t['account']}")
    elif t["state"] == "rejected":
        out.append(f"  token: REJECTED (HTTP {t['http']}) — expired, revoked, or issued under a "
                   f"different account. Create a new one at "
                   f"id.atlassian.com/manage-profile/security/api-tokens")
    elif t["state"] == "tls_untrusted":
        out.append("  token: could not be checked — this Python cannot verify TLS certificates.")
        out.append("         Fix: pip install certifi into the venv, or run"
                   " /Applications/Python 3.x/Install Certificates.command")
    elif t["state"] == "unreachable":
        out.append(f"  token: could not be checked ({t.get('detail')}) — offline?")
    elif t["state"] != "absent":
        out.append(f"  token: {t['state']}")
    if c["pat_without_email"]:
        out.append("  WARNING: token present with no CONFLUENCE_EMAIL — Basic auth needs both, "
                   "so nothing can authenticate.")
    if d["python_extra_present"]:
        out.append("  NOTE: ~/.local/lib/python-extra exists. It is unversioned and used to "
                   "shadow the venv; remove it once the venv is healthy.")
    return "\n".join(out)


def kv(d):
    """Flat key=value lines.

    This is what the Unity Setup tab parses. Flat on purpose: writing a JSON parser in C# to read
    a diagnostic is a poor trade, and a format this dumb cannot be misparsed.
    """
    out = []
    v = d["venv"]
    out.append(f"venv.exists={int(bool(v['exists']))}")
    out.append(f"venv.python={v['python']}")

    deps = d["deps"]
    mods = deps.get("mods", {})
    missing = [m for m, r in mods.items() if not r["ok"]]
    shadowed = [m for m, r in mods.items() if r["ok"] and not r.get("inside_venv")]
    out.append(f"deps.total={len(REQUIRED)}")
    out.append(f"deps.ok={len([m for m, r in mods.items() if r['ok']])}")
    out.append(f"deps.missing={','.join(missing)}")
    out.append(f"deps.shadowed={','.join(shadowed)}")
    for m, r in mods.items():
        out.append(f"dep.{m}={'ok' if r['ok'] else 'missing'}"
                   f"{'' if r['ok'] else ':' + r.get('error', '')}")

    usable = [i for i in d["interpreters"] if i.get("ok") and not i.get("disqualified")]
    out.append(f"interp.usable={len(usable)}")
    for i in usable:
        out.append(f"interp.candidate={i['version']}|{i['invoked_as']}")
    for i in d["interpreters"]:
        if i.get("ok") and i.get("disqualified"):
            out.append(f"interp.rejected={i['version']}|{i['invoked_as']}|{i['disqualified']}")

    c = d["confluence"]
    out.append(f"confluence.pat_exists={int(c['pat_file_exists'])}")
    out.append(f"confluence.pat_mode={c['pat_file_mode'] or ''}")
    out.append(f"confluence.email_set={int(c['email_set'])}")
    out.append(f"confluence.email_source={c['email_source']}")
    out.append(f"config.file={_config_path() or ''}")
    out.append(f"config.exists={int(bool(_config()))}")
    out.append(f"confluence.pat_without_email={int(c['pat_without_email'])}")
    t = c["token"]
    out.append(f"confluence.token_state={t['state']}")
    out.append(f"confluence.token_account={t.get('account', '')}")
    out.append(f"confluence.token_http={t.get('http', '')}")
    out.append(f"confluence.token_detail={(t.get('detail') or '').replace(chr(10), ' ')[:160]}")
    out.append(f"confluence.base_url={c['base_url']}")
    out.append(f"python_extra_present={int(d['python_extra_present'])}")
    return "\n".join(out)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--human", action="store_true", help="readable text instead of JSON")
    ap.add_argument("--kv", action="store_true", help="flat key=value lines (for the Unity window)")
    args = ap.parse_args()
    data = collect()
    if args.kv:
        print(kv(data))
    elif args.human:
        print(human(data))
    else:
        print(json.dumps(data, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
