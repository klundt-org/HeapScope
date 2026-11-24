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

# Capture end to end (sprint 4.1 task 7, for Linux; since 4.2 for whoever tries macOS too), through HeapScope's CLI, which
# runs the same application layer as the app: the tutorial's sample started by 'sample start', found by 'list', captured as
# a full dump and as a gcdump, and both opened by 'summary', which counts the 2,000 StaticCacheEntry objects the StaticCache
# scenario plants; then watched by 'monitor' and recorded by 'trace', whose allocations trace 'allocations' reads and must
# name the PricedOrder objects the SteadyAllocation scenario allocates without end (4.1 review M7). Every step runs, also
# after one failed, and the end says which failed. CI's Linux job runs it; it runs on Windows too, and is written to run on
# macOS (bash 3.2), where nobody has run it (INSTALL.md says how).
# The output may go into a public issue, so it never shows another process's command line or name: of the process list it
# prints the sample's row and how many rows there are (4.2 review H6). Everything HeapScope keeps goes to a scratch data
# folder, never the user's own. The dumps and traces are removed; unless every step passed (a run that was stopped too),
# HeapScope's log stays and its folder is printed (it names processes and paths, so it is for someone you trust, not for the issue).
# Usage: capture-check.sh <heapscope-cli path> [work folder]
set -euo pipefail
[ $# -ge 1 ] || { echo "Usage: capture-check.sh <heapscope-cli path> [work folder]" >&2; exit 64; }
cli="$1"
made_work=
if [ $# -ge 2 ]; then work="$2"; else work=$(mktemp -d); made_work=1; fi
mkdir -p "$work"
export HEAPSCOPE_DATA_FOLDER="${HEAPSCOPE_DATA_FOLDER:-$work/data}"
failures=
passed=
problem() { echo "FAILED: $1" >&2; failures="$failures
  $1"; }
stop() { problem "$1"; exit 1; }
report() {
  [ -n "$passed" ] && return 0
  echo "Capture check failed:${failures:-
  stopped before the end}" >&2
  if [ -d "$HEAPSCOPE_DATA_FOLDER/logs" ]; then
    echo "HeapScope's log says more: $HEAPSCOPE_DATA_FOLDER/logs (it names your processes and folders: read it before you share it)" >&2
  fi
}

echo "$(uname -s) $(uname -r) $(uname -m)"
"$cli" --version || true

out="$work/sample.out"
: > "$out"
"$cli" sample start --scenarios StaticCache,SteadyAllocation > "$out" 2>&1 &
starter=$!
pid=
cleanup() {
  [ -n "$pid" ] && "$cli" sample stop --pid "$pid" > /dev/null 2>&1 || true
  kill "$starter" 2> /dev/null || true
  rm -rf "$work"/sample.dmp* "$work"/sample.gcdump* "$work"/sample.nettrace* "$work"/sample.out "$work"/dac || true
  report || true
  if [ -n "$made_work" ] && [ -n "$passed" ]; then rm -rf "$work" || true; fi
}
trap cleanup EXIT
trap 'exit 130' INT TERM

for _ in $(seq 1 60); do
  pid=$(sed -n 's/^Sample \([0-9]*\) runs.*/\1/p' "$out")
  [ -n "$pid" ] && break
  kill -0 "$starter" 2> /dev/null || { cat "$out"; stop "'sample start' ended before the sample ran"; }
  sleep 1
done
[ -n "$pid" ] || { cat "$out"; stop "the sample did not start within 60 s"; }
cat "$out"

if list=$("$cli" list | tr -d '\r'); then
  # The sample's own row, whose command line is HeapScope's, and the count of all rows.
  grep -E "^ *$pid " <<< "$list" || problem "'list' did not show the sample (pid $pid)"
  echo "The list has $(grep -cE '^ *[0-9]+ ' <<< "$list" || true) processes."
else
  problem "'list'"
fi

check_snapshot() {
  local summary
  if summary=$("$cli" summary "$work/$1" --filter StaticCacheEntry | tr -d '\r'); then
    grep -qE "^ +2,000 .* Fixtures\.Leaks\.StaticCacheEntry$" <<< "$summary" || { echo "$summary"; problem "$1 does not count 2,000 StaticCacheEntry objects"; }
  else
    problem "'summary' could not open $1"
  fi
}

if "$cli" capture --pid "$pid" --out "$work/sample.dmp"; then check_snapshot sample.dmp; else problem "the full dump of the sample"; fi
if "$cli" capture --pid "$pid" --kind gcdump --out "$work/sample.gcdump"; then check_snapshot sample.gcdump; else problem "the gcdump of the sample"; fi

# Live counters (4.1 review M7): a header and at least two samples in three seconds.
if samples=$("$cli" monitor --pid "$pid" --duration 3 --format csv | tr -d '\r'); then
  echo "$samples"
  [ "$(grep -c . <<< "$samples")" -ge 3 ] || problem "'monitor' printed fewer than two samples in three seconds"
else
  problem "'monitor' of the sample"
fi

# Allocations (4.1 review M7): a trace is recorded, and 'allocations' reads it.
if "$cli" trace --pid "$pid" --profile allocations --duration 3 --out "$work/sample.nettrace" && [ -s "$work/sample.nettrace" ]; then
  if allocations=$("$cli" allocations "$work/sample.nettrace" --types 5 --stacks 1 | tr -d '\r'); then
    echo "$allocations"
    grep -q "HeapScope.Sample.PricedOrder" <<< "$allocations" || problem "'allocations' did not name the type the SteadyAllocation scenario allocates"
  else
    problem "'allocations' could not read the trace"
  fi
else
  problem "'trace' of the sample"
fi

if "$cli" sample stop --pid "$pid"; then pid=; else problem "'sample stop' did not stop the sample"; fi

# What a first report on macOS needs from the log: how each read did on HeapScope's own process, and the estimate beside
# the size written (pids, sizes and folders only).
grep -hoE "(macOS reads of HeapScope's own process|The full dump of pid|Captured heapscope-sample \(pid).*" "$HEAPSCOPE_DATA_FOLDER"/logs/* 2> /dev/null || true

[ -z "$failures" ] || exit 1
passed=1
echo "Capture check passed: the sample was listed, captured as a full dump and a gcdump (both counted its 2,000 entries), monitored and traced."
