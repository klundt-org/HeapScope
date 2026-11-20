#!/usr/bin/env bash
# HeapScope
# Copyright (C) 2026 Ray Klundt
# https://github.com/klundt-org/HeapScope
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU Affero General Public License as published
# by the Free Software Foundation, version 3 only, with additional terms
# under Section 7 of that license.
#
# Additional terms, including attribution requirements, apply.
# See the ADDITIONAL-TERMS.md file distributed with this program.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
# GNU Affero General Public License for more details.
#
# You should have received a copy of the GNU Affero General Public License
# along with this program (see the LICENSE file). If not, see
# <https://www.gnu.org/licenses/>.
#
# Commercial licenses are available. See COMMERCIAL.md.

# Runs an unpacked release build as a user would, on a machine without .NET (sprint 3.9, ADR-039): checks the folder,
# then the CLI (its version, its worker, a summary of the fixture dump) and the app without a display (it opens the dump
# and drills down). On Windows it hides the installed .NET first; on Linux CI runs it in a plain Ubuntu container
# that has only the libraries INSTALL.md lists. The dump is the LeakyConsole fixture's (build/fixture-dump.sh).
# Usage: check-install.sh <unpacked build folder> <fixture dump>
set -euo pipefail
[ $# -eq 2 ] || { echo "Usage: check-install.sh <unpacked build folder> <fixture dump>" >&2; exit 64; }
dir="$1"
dump="$2"
bash "$(dirname "$0")/check-package.sh" "$dir"

# No .NET to fall back on: off the path, and no DOTNET_ROOT.
PATH=$(tr ':' '\n' <<< "$PATH" | grep -vi dotnet | paste -sd: -)
unset DOTNET_ROOT DOTNET_ROOT_X64 DOTNET_HOST_PATH
! command -v dotnet > /dev/null || { echo "dotnet is still on the path: $(command -v dotnet)"; exit 1; }
export HEAPSCOPE_DATA_FOLDER="${HEAPSCOPE_DATA_FOLDER:-$(mktemp -d)}"

ext=""
case "$(uname -s)" in MINGW*|MSYS*|CYGWIN*) ext=".exe" ;; esac
# On failure, the end of each HeapScope log (CI sets Logging__File__Directory; otherwise the data folder's logs).
fail() { echo "$1" >&2; for f in "${Logging__File__Directory:-$HEAPSCOPE_DATA_FOLDER/logs}"/*.log; do [ -f "$f" ] && { echo "== $f" >&2; tail -40 "$f" >&2; }; done; exit 1; }

# Self-contained, not only .NET hidden: Windows finds an installed .NET through the registry too (3.9 review M6). Each
# executable carries its runtime (includedFrameworks, no frameworks) and its host resolver beside it.
for config in "$dir/HeapScope.runtimeconfig.json" "$dir/heapscope-cli.runtimeconfig.json" "$dir/worker/HeapScope.Worker.runtimeconfig.json"   "$dir/sample/heapscope-sample.runtimeconfig.json"; do
  grep -q '"includedFrameworks"' "$config" && ! grep -q '"frameworks\?"' "$config" \
    || fail "$config does not describe a self-contained build"
  ls "$(dirname "$config")" | grep -qiE '^(lib)?hostfxr\.(dll|so)$' || fail "No hostfxr beside $config"
done
grep -q '"System.GC.Server": true' "$dir/worker/HeapScope.Worker.runtimeconfig.json" || fail "The worker's runtimeconfig does not turn Server GC on"

"$dir/heapscope-cli$ext" --version || fail "heapscope-cli --version failed"
info=$("$dir/heapscope-cli$ext" worker info 2>&1 | tr -d '\r') || { echo "$info" >&2; fail "heapscope-cli worker info failed: the worker did not start"; }
echo "$info"
grep -qE '^Server GC: +True$' <<< "$info" || fail "The worker does not run Server GC"
summary=$("$dir/heapscope-cli$ext" summary "$dump" --filter StaticCacheEntry | tr -d '\r') || fail "heapscope-cli summary failed"
grep -qE "^ +2,000 .* Fixtures\.Leaks\.StaticCacheEntry$" <<< "$summary" || { echo "$summary"; fail "The CLI did not count 2,000 StaticCacheEntry objects"; }

# The tutorial's sample runs without .NET too (sprint 3.13 review M6): its catalog, from the sample itself, then through the
# CLI, which lists it, or refuses with the reason where HeapScope runs elevated (CI's Windows runner, the Linux container as
# root): the sample would run elevated too.
catalog=$("$dir/sample/heapscope-sample$ext" --list | tr -d '\r') || fail "The sample did not run"
grep -q '"Name":"StaticCache"' <<< "$catalog" || { echo "$catalog"; fail "The sample did not list its scenarios"; }
if scenarios=$("$dir/heapscope-cli$ext" sample list 2>&1 | tr -d '\r'); then
  grep -qE '^StaticCache +yes ' <<< "$scenarios" || { echo "$scenarios"; fail "heapscope-cli sample list did not list StaticCache"; }
else
  grep -qE "HeapScope runs as (administrator|root), so the sample would too." <<< "$scenarios" || { echo "$scenarios"; fail "heapscope-cli sample list failed"; }
  echo "heapscope-cli sample list refused the sample, as it must when HeapScope runs elevated."
fi

log=$(mktemp -d)
"$dir/HeapScope$ext" --headless --smoke-open "$dump" --Logging:File:Directory "$log" || { cat "$log"/*.log; exit 1; }
grep -q "Smoke open types: .*Fixtures.Leaks.StaticCacheEntry 2000" "$log"/*.log || { cat "$log"/*.log; echo "The app did not show 2000 StaticCacheEntry objects"; exit 1; }
echo "Installed build $dir runs without .NET: the CLI, its worker and the app opened the fixture dump, and the sample listed its scenarios."
