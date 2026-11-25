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

# Checks that objects held only by GC handles are reachable in a dump of a Server GC process with fewer heaps than processors
# (peer review 4.5: the runtime's own walk of the handle tables leaves out those past the heap count). On Linux a dump does not
# record its processors, so this also runs the search for the number of tables.
# Usage: check-handles.sh <heapscope-cli, by its full path> <work folder>
set -euo pipefail
cli="$1"
work="$2"
mkdir -p "$work"
here="$(dirname "$0")"
export DOTNET_gcServer=1 DOTNET_GCHeapCount=2 DOTNET_GCDynamicAdaptationMode=0
export FIXTURE_ARGS="--leak none --handles-under-load 200"
bash "$here/fixture-dump.sh" "$work/handles.dmp" "cli=$cli"
unset DOTNET_gcServer DOTNET_GCHeapCount DOTNET_GCDynamicAdaptationMode
export Logging__File__Directory="$work/log"
expected=$(grep -oE 'HANDLES [0-9]+' "$work/fixture.log" | cut -d' ' -f2)
[ -n "$expected" ] || { echo "The fixture did not say how many handles it made"; exit 1; }
for type in HandleHeld HandleValue; do
  "$cli" instances "$work/handles.dmp" --type "Fixtures.Leaks.$type" --retained --all --format json > "$work/$type.json"
  jq -e --argjson n "$expected" '.TotalObjects == $n and (.Rows | length) == $n and all(.Rows[]; .Retained != null and .Retained > 0)' "$work/$type.json" > /dev/null \
    || { echo "Known answer failed: $expected reachable objects of $type"; jq '{TotalObjects, Reached: ([.Rows[] | select(.Retained != null)] | length)}' "$work/$type.json"; exit 1; }
done
grep -rhoE "GC handles [A-Z][a-z]+: [^;]*;[^;]*" "$work/log" | head -1 || true
rm -f "$work"/handles.dmp*
echo "All $expected objects held by GC handles are reached, and those held by dependent handles."
