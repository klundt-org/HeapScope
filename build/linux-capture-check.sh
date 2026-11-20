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

# Capture on Linux end to end (sprint 4.1 task 7), through HeapScope's CLI, which runs the same application layer as the
# app: the tutorial's sample started by 'sample start', found by 'list', captured as a full dump and as a gcdump, and both
# opened by 'summary', which counts the 2,000 StaticCacheEntry objects the StaticCache scenario plants. CI's Linux job runs
# it; it runs on Windows too. The dumps hold the sample's data only and are deleted.
# Usage: linux-capture-check.sh <heapscope-cli path> [work folder]
set -euo pipefail
[ $# -ge 1 ] || { echo "Usage: linux-capture-check.sh <heapscope-cli path> [work folder]" >&2; exit 64; }
cli="$1"
work="${2:-$(mktemp -d)}"
mkdir -p "$work"
fail() { echo "Linux capture check: $1" >&2; exit 1; }

out="$work/sample.out"
"$cli" sample start --scenarios StaticCache > "$out" 2>&1 &
starter=$!
pid=
cleanup() {
  [ -n "$pid" ] && "$cli" sample stop --pid "$pid" > /dev/null 2>&1 || true
  kill "$starter" 2> /dev/null || true
  rm -f "$work"/sample.dmp* "$work"/sample.gcdump*
}
trap cleanup EXIT

for _ in $(seq 1 60); do
  pid=$(sed -n 's/^Sample \([0-9]*\) runs.*/\1/p' "$out")
  [ -n "$pid" ] && break
  kill -0 "$starter" 2> /dev/null || { cat "$out"; fail "'sample start' ended before the sample ran"; }
  sleep 1
done
[ -n "$pid" ] || { cat "$out"; fail "the sample did not start within 60 s"; }
cat "$out"

list=$("$cli" list | tr -d '\r')
echo "$list"
grep -qE "^ *$pid " <<< "$list" || fail "'list' did not show the sample (pid $pid)"

"$cli" capture --pid "$pid" --out "$work/sample.dmp" || fail "the full dump of the sample failed"
"$cli" capture --pid "$pid" --kind gcdump --out "$work/sample.gcdump" || fail "the gcdump of the sample failed"

for snapshot in sample.dmp sample.gcdump; do
  summary=$("$cli" summary "$work/$snapshot" --filter StaticCacheEntry | tr -d '\r') || fail "'summary' could not open $snapshot"
  grep -qE "^ +2,000 .* Fixtures\.Leaks\.StaticCacheEntry$" <<< "$summary" || { echo "$summary"; fail "$snapshot does not count 2,000 StaticCacheEntry objects"; }
done

"$cli" sample stop --pid "$pid" || fail "'sample stop' did not stop the sample"
pid=
echo "Linux capture check passed: the sample was listed, captured as a full dump and a gcdump, and both counted its 2,000 entries."
