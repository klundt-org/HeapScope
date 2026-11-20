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
# opened by 'summary', which counts the 2,000 StaticCacheEntry objects the StaticCache scenario plants; then watched by
# 'monitor' and recorded by 'trace', whose allocations trace 'allocations' reads and must name the PricedOrder objects the
# SteadyAllocation scenario allocates without end (review M7). CI's Linux job runs it; it runs on Windows too. Everything HeapScope keeps goes to a scratch data folder (review M10), never the user's own, and the
# work folder is removed when the script made it. The files hold the sample's data only.
# Usage: linux-capture-check.sh <heapscope-cli path> [work folder]
set -euo pipefail
[ $# -ge 1 ] || { echo "Usage: linux-capture-check.sh <heapscope-cli path> [work folder]" >&2; exit 64; }
cli="$1"
made_work=
if [ $# -ge 2 ]; then work="$2"; else work=$(mktemp -d); made_work=1; fi
mkdir -p "$work"
export HEAPSCOPE_DATA_FOLDER="${HEAPSCOPE_DATA_FOLDER:-$work/data}"
fail() { echo "Linux capture check: $1" >&2; exit 1; }

out="$work/sample.out"
"$cli" sample start --scenarios StaticCache,SteadyAllocation > "$out" 2>&1 &
starter=$!
pid=
cleanup() {
  [ -n "$pid" ] && "$cli" sample stop --pid "$pid" > /dev/null 2>&1 || true
  kill "$starter" 2> /dev/null || true
  rm -rf "$work"/sample.dmp* "$work"/sample.gcdump* "$work"/sample.nettrace* "$work"/dac
  [ -n "$made_work" ] && rm -rf "$work" || true
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

list=$("$cli" list | tr -d '\r') || fail "'list' failed"
echo "$list"
grep -qE "^ *$pid " <<< "$list" || fail "'list' did not show the sample (pid $pid)"

"$cli" capture --pid "$pid" --out "$work/sample.dmp" || fail "the full dump of the sample failed"
"$cli" capture --pid "$pid" --kind gcdump --out "$work/sample.gcdump" || fail "the gcdump of the sample failed"

for snapshot in sample.dmp sample.gcdump; do
  summary=$("$cli" summary "$work/$snapshot" --filter StaticCacheEntry | tr -d '\r') || fail "'summary' could not open $snapshot"
  grep -qE "^ +2,000 .* Fixtures\.Leaks\.StaticCacheEntry$" <<< "$summary" || { echo "$summary"; fail "$snapshot does not count 2,000 StaticCacheEntry objects"; }
done

# Live counters (review M7): a header and at least two samples in three seconds.
samples=$("$cli" monitor --pid "$pid" --duration 3 --format csv | tr -d '\r') || fail "'monitor' of the sample failed"
echo "$samples"
[ "$(grep -c . <<< "$samples")" -ge 3 ] || fail "'monitor' printed fewer than two samples in three seconds"

# Allocations (review M7): a trace is recorded, and 'allocations' reads it.
"$cli" trace --pid "$pid" --profile allocations --duration 3 --out "$work/sample.nettrace" || fail "'trace' of the sample failed"
[ -s "$work/sample.nettrace" ] || fail "'trace' wrote no allocations trace"
allocations=$("$cli" allocations "$work/sample.nettrace" --types 5 --stacks 1 | tr -d '\r') || fail "'allocations' could not read the trace"
echo "$allocations"
grep -q "HeapScope.Sample.PricedOrder" <<< "$allocations" || fail "'allocations' did not name the type the SteadyAllocation scenario allocates"

"$cli" sample stop --pid "$pid" || fail "'sample stop' did not stop the sample"
pid=
echo "Linux capture check passed: the sample was listed, captured as a full dump and a gcdump (both counted its 2,000 entries), monitored and traced."
