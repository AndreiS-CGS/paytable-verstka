#!/usr/bin/env bash
#
# Switch a Unity project between the two ways of consuming com.cgs.paytablelibrary, and
# re-fetch it when on the git one.
#
#   ./tools/package.sh status  <unity-project>
#   ./tools/package.sh link    <unity-project>   # dev: symlink to this clone, edits are live
#   ./tools/package.sh unlink  <unity-project>   # back to whatever manifest.json says
#   ./tools/package.sh refresh <unity-project>   # git mode: forget the pinned commit, re-fetch
#
# <unity-project> is the folder containing Assets/ and Packages/. It may also be given as
# $PAYTABLE_UNITY_PROJECT so you can drop the argument.
#
# Why `refresh` needs to exist at all: a git dependency pins the commit it first resolved into
# Packages/packages-lock.json, so pushing to the branch changes nothing for Unity. Client.Resolve()
# alone only re-reads that lock. The pin and the cached copy both have to go.
#
# While iterating on the package itself, prefer `link`. Watching a fix you pushed minutes ago fail
# to arrive, and the old bug fire again, is a bad way to spend an afternoon.

set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LIB="$REPO/library"
PKG_NAME="com.cgs.paytablelibrary"

cmd="${1:-status}"
project="${2:-${PAYTABLE_UNITY_PROJECT:-}}"

if [ -z "$project" ]; then
    echo "error: no Unity project given (argument or \$PAYTABLE_UNITY_PROJECT)" >&2
    exit 2
fi
project="${project%/}"
if [ ! -d "$project/Packages" ]; then
    echo "error: $project does not look like a Unity project (no Packages/)" >&2
    exit 2
fi

PKGS="$project/Packages"
LOCK="$PKGS/packages-lock.json"
EMBEDDED="$PKGS/$PKG_NAME"
CACHE_GLOB="$project/Library/PackageCache/$PKG_NAME@"

unpin() {
    [ -f "$LOCK" ] || return 0
    python3 - "$LOCK" "$PKG_NAME" <<'PY'
import collections, json, shutil, sys
lock, name = sys.argv[1], sys.argv[2]
with open(lock) as f:
    d = json.load(f, object_pairs_hook=collections.OrderedDict)
entry = d.get("dependencies", {}).pop(name, None)
if entry is None:
    print("  lock: no entry to remove")
    sys.exit()
shutil.copyfile(lock, lock + ".bak")
with open(lock, "w") as f:
    f.write(json.dumps(d, indent=2) + "\n")
print(f"  lock: removed {entry.get('source','?')} pin {entry.get('hash','')[:12]} (backup: .bak)")
PY
}

drop_cache() {
    local found=0
    for d in "$CACHE_GLOB"*; do
        [ -e "$d" ] || continue
        rm -rf "$d"
        echo "  cache: removed $(basename "$d")"
        found=1
    done
    [ "$found" = 1 ] || echo "  cache: nothing to remove"
}

case "$cmd" in
status)
    echo "project: $project"
    if [ -L "$EMBEDDED" ]; then
        echo "  mode:   LINK -> $(readlink "$EMBEDDED")"
    elif [ -d "$EMBEDDED" ]; then
        echo "  mode:   embedded copy in Packages/"
    else
        echo "  mode:   from manifest.json"
    fi
    grep -o "\"$PKG_NAME\": \"[^\"]*\"" "$PKGS/manifest.json" 2>/dev/null \
        | sed 's/^/  manifest: /' || echo "  manifest: no entry"
    python3 - "$LOCK" "$PKG_NAME" <<'PY' 2>/dev/null || echo "  lock: unreadable"
import json, sys
d = json.load(open(sys.argv[1]))["dependencies"].get(sys.argv[2])
print(f"  lock:     {d['source']} {d.get('hash','')[:12]}" if d else "  lock:     no entry")
PY
    for d in "$CACHE_GLOB"*; do
        [ -e "$d" ] && echo "  cache:    $(basename "$d")"
    done
    ;;

link)
    [ -d "$LIB" ] || { echo "error: $LIB not found" >&2; exit 1; }
    if [ -e "$EMBEDDED" ] && [ ! -L "$EMBEDDED" ]; then
        echo "error: $EMBEDDED exists and is not a symlink — refusing to replace it" >&2
        exit 1
    fi
    rm -f "$EMBEDDED"
    ln -s "$LIB" "$EMBEDDED"
    echo "  link:  $EMBEDDED -> $LIB"
    unpin
    drop_cache
    echo
    echo "Edits in the clone are now live. Focus Unity to let it reimport."
    ;;

unlink)
    if [ -L "$EMBEDDED" ]; then
        rm -f "$EMBEDDED"
        echo "  link:  removed"
    else
        echo "  link:  none to remove"
    fi
    unpin
    drop_cache
    echo
    echo "Back to manifest.json. Focus Unity to let it resolve."
    ;;

refresh)
    if [ -L "$EMBEDDED" ]; then
        echo "note: this project is symlinked, so there is nothing to fetch."
        echo "      Edits are already live. Use 'unlink' first to go back to git."
        exit 0
    fi
    unpin
    drop_cache
    echo
    echo "Focus Unity — it re-resolves on window focus, not on request."
    ;;

*)
    sed -n '3,20p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
    exit 2
    ;;
esac
