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

# Packs the CLI as a dotnet tool (sprint 3.9, ADR-039): one framework-dependent package for every system, HeapScope.Cli,
# command heapscope-cli, with the worker's assembly (no executable; the front end starts it through the dotnet host),
# HeapScope's license files and the third-party licenses. Then checks the package.
# The package's own license file (the one nuget.org shows) is LICENSE.txt: build/package-license-header.txt, then
# ADDITIONAL-TERMS.md, COMMERCIAL.md and LICENSE, so the additional terms are shown with the AGPL (2026-11-18, 3.13.1).
# Its readme is src/HeapScope.Cli/README.md.
# Usage: pack-tool.sh <output folder>
set -euo pipefail
[ $# -eq 1 ] || { echo "Usage: pack-tool.sh <output folder>" >&2; exit 64; }
out="$1"
mkdir -p "$out"
staging=$(mktemp -d)
publish=$(mktemp -d)
package_files=$(mktemp -d)
trap 'rm -rf "$staging" "$publish" "$package_files"' EXIT
root="$(cd "$(dirname "$0")/.." && pwd)"
separator=$(printf '=%.0s' $(seq 1 100))
{
  cat "$root/build/package-license-header.txt"
  for file in ADDITIONAL-TERMS.md COMMERCIAL.md LICENSE; do
    printf '\r\n%s\r\n%s\r\n%s\r\n\r\n' "$separator" "$file" "$separator"
    cat "$root/$file"
  done
} > "$package_files/LICENSE.txt"

# HeapScopePublish leaves TraceEvent's native binaries out (Directory.Build.targets), as in a published build. The pack
# publishes into a fresh folder: the tool takes every file there.
dotnet restore src/HeapScope.Cli -p:HeapScopeTool=true -p:HeapScopePublish=true
dotnet run build/collect-licenses.cs -- "$staging" any src/HeapScope.Cli src/HeapScope.Worker
dotnet pack src/HeapScope.Cli -c Release --no-restore -p:HeapScopeTool=true -p:HeapScopePublish=true \
  -p:HeapScopeToolLicenses="$staging" -p:HeapScopePackageFiles="$package_files" -p:PublishDir="$publish/" -o "$out"

version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props)
package="$out/HeapScope.Cli.$version.nupkg"
[ -f "$package" ] || { echo "Missing $package"; exit 1; }
# A .nupkg is a zip; dotnet runs the listing so no unzip is needed.
listing=$(dotnet run build/zip.cs -- list "$package")
failed=0
tools="tools/net10.0/any"
for file in heapscope-cli.dll DotnetToolSettings.xml worker/HeapScope.Worker.dll worker/HeapScope.Worker.runtimeconfig.json   sample/heapscope-sample.dll sample/heapscope-sample.runtimeconfig.json \
  LICENSE ADDITIONAL-TERMS.md COMMERCIAL.md THIRD-PARTY-NOTICES.md licenses/README.md; do
  grep -qx "$tools/$file" <<< "$listing" || { echo "Missing $tools/$file"; failed=1; }
done
for file in LICENSE.txt README.md; do
  grep -qx "$file" <<< "$listing" || { echo "Missing the package's $file"; failed=1; }
done
# No worker executable and no runtime at any depth (3.9 review M5), no unreviewed binaries, at least one third-party
# license folder.
unwanted=$(grep -iE "/HeapScope\.Worker(\.exe)?$|/heapscope-sample(\.exe)?$|(^|/)(lib)?(hostfxr|coreclr)\.(dll|so|dylib)$|msdia140\.dll|KernelTraceControl\.dll" <<< "$listing" || true)
grep -qE "^$tools/licenses/[^/]+/.+" <<< "$listing" || { echo "No third-party license files under $tools/licenses/"; failed=1; }
[ -z "$unwanted" ] || { echo "Files a tool must not carry:"; echo "$unwanted"; failed=1; }
[ "$failed" -eq 0 ] && echo "Packed $package: the worker's and the sample's assemblies, the license files, no executable worker or sample, no unreviewed binaries."
exit "$failed"
