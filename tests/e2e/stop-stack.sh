#!/usr/bin/env bash
# Copyright(C) 2026  Rob Morgan (robert.morgan.e@gmail.com)
#
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published
# by the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
# GNU General Public License for more details.
#
# You should have received a copy of the GNU General Public License
# along with this program.  If not, see <https://www.gnu.org/licenses/>.

# Stops what start-stack.sh started.
run="$HOME/gsp3w-run"
for name in app fake; do
  if [[ -f "$run/$name.pid" ]]; then
    kill "$(cat "$run/$name.pid")" 2>/dev/null || true
    rm -f "$run/$name.pid"
  fi
done
