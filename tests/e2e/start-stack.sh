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

# Starts a published copy of the app and the fake Alpaca server, for the browser and load tests, with a
# throw-away settings folder holding four fake mounts (one per view). Linux / WSL.
#
#   tests/e2e/start-stack.sh [app-port=5300] [fake-port=5400] [mounts=4]
#
# Prints the app's process id; stop both with tests/e2e/stop-stack.sh.
set -euo pipefail

app_port="${1:-5300}"
fake_port="${2:-5400}"
mounts="${3:-4}"
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
dotnet="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
# The published apps find the runtime here when it isn't installed system-wide (~/.dotnet).
export DOTNET_ROOT="${DOTNET_ROOT:-$(dirname "$(readlink -f "$dotnet")")}"

if [[ "$repo" == /mnt/* ]]; then
  work="$HOME/gsp3w"
  mkdir -p "$work"
  rsync -a --delete --exclude bin --exclude obj --exclude .vs --exclude node_modules --exclude TestResults "$repo/" "$work/"
  repo="$work"
fi

run="$HOME/gsp3w-run"
mkdir -p "$run"
"$dotnet" publish "$repo/GS.Point3DWeb/GS.Point3DWeb.csproj" -c Release -o "$run/app" --nologo -v q
"$dotnet" publish "$repo/tests/GS.Point3DWeb.FakeAlpaca/GS.Point3DWeb.FakeAlpaca.csproj" -c Release -o "$run/fake" --nologo -v q

# Fresh settings: four fake mounts, one in each view.
export XDG_CONFIG_HOME="$run/config"
rm -rf "$XDG_CONFIG_HOME"
mkdir -p "$XDG_CONFIG_HOME/GS.Point3DWeb"
servers=""
quadrants=""
for ((n = 0; n < mounts; n++)); do
  id="00000000-0000-0000-0000-$(printf '%012d' "$n")"
  [[ -n "$servers" ]] && servers+=","
  servers+="{\"IdGuid\":\"$id\",\"ServerName\":\"Fake mount $n\",\"Url\":\"http://127.0.0.1:$fake_port/api/v1/telescope/$n/\",\"UserName\":null,\"Password\":null,\"PollSeconds\":0.5}"
  if (( n < 4 )); then
    [[ -n "$quadrants" ]] && quadrants+=","
    quadrants+="\"$id\""
  fi
done
cat > "$XDG_CONFIG_HOME/GS.Point3DWeb/servers.json" <<JSON
{"SelectedId":null,"Servers":[$servers],"Quadrants":[$quadrants],"ViewCount":4,"Version":2}
JSON

pkill -f "$run/fake/GS.Point3DWeb.FakeAlpaca" 2>/dev/null || true
pkill -f "$run/app/GS.Point3DWeb" 2>/dev/null || true
sleep 0.5

nohup "$run/fake/GS.Point3DWeb.FakeAlpaca" --port "$fake_port" --mounts "$mounts" --moving true > "$run/fake.log" 2>&1 &
echo $! > "$run/fake.pid"

start=$(date +%s%N)
ASPNETCORE_ENVIRONMENT=Production nohup "$run/app/GS.Point3DWeb" --urls "http://localhost:$app_port" --OpenBrowser=false > "$run/app.log" 2>&1 &
echo $! > "$run/app.pid"
for ((i = 0; i < 600; i++)); do
  curl -sf -o /dev/null "http://localhost:$app_port/" && break
  if ! kill -0 "$(cat "$run/app.pid")" 2>/dev/null; then echo "The app stopped:"; tail -20 "$run/app.log"; exit 1; fi
  sleep 0.05
done
end=$(date +%s%N)
echo "app pid $(cat "$run/app.pid"), first page after $(( (end - start) / 1000000 )) ms; fake pid $(cat "$run/fake.pid")"
