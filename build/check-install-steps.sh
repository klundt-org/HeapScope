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

# Runs INSTALL.md's Linux steps as written (sprint 4.8, peer review 4.5 M13), in the plain Ubuntu container CI's package job runs
# check-install.sh in: its apt line (without sudo, as root), then its shell blocks with the release's name in place of
# HeapScope-vX.Y.Z-linux-x64, then the commands they put on the path, with a fixture dump. A step INSTALL.md changes is checked
# as changed; one that no longer works fails here.
# Usage: check-install-steps.sh <INSTALL.md> <linux archive (.tar.gz)> <fixture dump>
set -euo pipefail
[ $# -eq 3 ] || { echo "Usage: check-install-steps.sh <INSTALL.md> <linux archive> <fixture dump>" >&2; exit 64; }
install_md="$1"
archive="$2"
dump="$3"
name="$(basename "$archive" .tar.gz)"
section=$(awk '/^## Linux$/ { on = 1; next } /^## / { on = 0 } on' "$install_md")
[ -n "$section" ] || { echo "INSTALL.md has no \"## Linux\" section" >&2; exit 1; }

# Step 1: the libraries, from the Debian and Ubuntu line.
apt_line=$(grep -o 'sudo apt install [^`]*' <<< "$section" | head -1)
[ -n "$apt_line" ] || { echo "INSTALL.md's Linux section names no apt install line" >&2; exit 1; }
echo "Step 1: ${apt_line#sudo }"
apt-get install -y -qq --no-install-recommends ${apt_line#sudo apt install } > /dev/null

# Steps 2 and 3: every shell block, from the archive's folder, with this release's name.
steps=$(awk '/^ *```bash$/ { on = 1; next } /^ *```$/ { on = 0 } on' <<< "$section" | sed "s/HeapScope-vX\.Y\.Z-linux-x64/$name/g")
[ -n "$steps" ] || { echo "INSTALL.md's Linux section has no shell block" >&2; exit 1; }
echo "Steps 2 and 3:"
echo "$steps"
(cd "$(dirname "$archive")" && bash -euo pipefail -c "$steps")

# Step 4: the commands on the path (~/.local/bin, which a login shell on Ubuntu adds once it exists).
export PATH="$HOME/.local/bin:$PATH"
command -v heapscope > /dev/null || { echo "Step 4: heapscope is not on the path" >&2; exit 1; }
heapscope-cli --version
work=$(mktemp -d)
cp "$dump" "$work/steps.dmp"
summary=$(HEAPSCOPE_DATA_FOLDER="$work/data" heapscope-cli summary "$work/steps.dmp" --filter StaticCacheEntry | tr -d '\r') \
  || { echo "Step 4: heapscope-cli summary failed" >&2; exit 1; }
grep -qE "^ +2,000 .* Fixtures\.Leaks\.StaticCacheEntry$" <<< "$summary" || { echo "$summary"; echo "Step 4: the CLI did not count 2,000 StaticCacheEntry objects" >&2; exit 1; }
echo "INSTALL.md's Linux steps work as written on a clean $(. /etc/os-release && echo "$PRETTY_NAME"): installed from $name, the CLI on the path opened the fixture dump."
