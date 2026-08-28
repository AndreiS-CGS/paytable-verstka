"""Re-exec the calling script under the interpreter that actually has the dependencies.

Imported for its side effect, and it must be the FIRST import of every script in this
directory — before any third-party import. By the time `import requests` runs it is already
too late to switch interpreters.

Why this exists rather than a line in SKILL.md telling the agent which python to use: prose is
not enforcement. The agent has been told `python3 <script>` for months, and on a machine where
`python3` is missing a dependency the failure is silent, or worse: an optional-looking
`try/except ImportError` disables a whole code path without a word.

Duplicated verbatim in each skill's scripts/ directory on purpose: every skill must stay
independently installable, so none of them may import from another.
"""

import os
import sys
from pathlib import Path

_SENTINEL = "PAYTABLE_PY_BOOTSTRAPPED"
_VENV_DIR = Path.home() / ".venvs" / "paytable-tools"


def _config_path():
    if os.name == "nt":
        base = os.environ.get("APPDATA")
        return Path(base) / "paytable-tools" / "config.json" if base else None
    base = os.environ.get("XDG_CONFIG_HOME") or (Path.home() / ".config")
    return Path(base) / "paytable-tools" / "config.json"


def config(name, default=None):
    """Read a setting: real environment first, then the config file.

    Env-first keeps `CONFLUENCE_EMAIL=... python3 script.py` working for one-off debugging, and
    lets Claude Code's injected `env` win over a stale config file.
    """
    v = os.environ.get(name)
    if v not in (None, ""):
        return v
    p = _config_path()
    if p and p.is_file():
        try:
            import json
            with open(p, encoding="utf-8") as f:
                return json.load(f).get(name, default)
        except Exception:
            pass
    return default


def _interpreter_in(venv_dir):
    p = Path(venv_dir) / ("Scripts/python.exe" if os.name == "nt" else "bin/python")
    return p if p.is_file() else None


def _candidates():
    """In order. A set-but-broken PAYTABLE_PYTHON is reported, never silently skipped —
    silently skipping it is how you get "I fixed it and nothing changed"."""
    explicit = os.environ.get("PAYTABLE_PYTHON")
    if explicit:
        if Path(explicit).is_file():
            yield Path(explicit)
        else:
            print(f"  Warning: PAYTABLE_PYTHON is set to {explicit!r}, which is not a file — "
                  f"ignoring it.", file=sys.stderr)

    venv = _interpreter_in(_VENV_DIR)
    if venv:
        yield venv

    from_cfg = config("PAYTABLE_PYTHON")
    if from_cfg and Path(from_cfg).is_file():
        yield Path(from_cfg)


def _switch():
    # The sentinel is mandatory: without it a PAYTABLE_PYTHON pointing back at this same
    # interpreter would exec forever, which on Windows spawns processes until the machine dies.
    if os.environ.get(_SENTINEL):
        return
    running = Path(sys.executable).resolve()
    for cand in _candidates():
        # Compare resolved paths, not strings: .venv/bin/python and .venv/bin/python3.13 are
        # the same interpreter and comparing raw strings would re-exec forever.
        if cand.resolve() == running:
            return
        os.environ[_SENTINEL] = "1"
        argv = [str(cand), os.path.abspath(sys.argv[0]), *sys.argv[1:]]
        if os.name == "nt":
            # os.execv has odd parent-handle semantics on Windows.
            import subprocess
            sys.exit(subprocess.run(argv).returncode)
        os.execv(str(cand), argv)
    # Nothing configured to switch to — carry on under the current interpreter and let
    # require() below produce a useful message if something is actually missing.


def require(*modules):
    """Fail loudly, naming the interpreter and the fix.

    Never fall through to a different interpreter here: falling back to whatever `python3`
    happens to be is exactly what produced the silent half-working state this module exists
    to end.
    """
    import importlib
    missing = []
    for m in modules:
        try:
            importlib.import_module(m)
        except BaseException:
            # BaseException, not Exception: a mismatched-ABI .so can raise SystemError.
            missing.append(m)
    if not missing:
        return
    venv = _interpreter_in(_VENV_DIR)
    sys.exit(
        f"\nMissing Python package(s): {', '.join(missing)}\n"
        f"  interpreter in use: {sys.executable}\n"
        f"  expected venv:      {_VENV_DIR}"
        f"{'' if venv else '  (does not exist)'}\n\n"
        f"Fix it in Unity: PlayStudios > Slot Tools > Paytable Tool > Setup.\n"
        f"Or by hand:\n"
        f"  python3 -m venv {_VENV_DIR}\n"
        f"  {_VENV_DIR}/bin/python -m pip install -r <repo>/requirements.txt\n"
    )


_switch()
