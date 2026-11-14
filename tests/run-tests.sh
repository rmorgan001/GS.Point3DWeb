#!/usr/bin/env bash
# Runs the whole test suite on Linux (or WSL) with a throw-away settings folder, so the settings tests
# (servers.json, feature.key, backups) run too. They are skipped on Windows, where the app always uses the
# real %APPDATA%\GS.Point3DWeb.
#
#   tests/run-tests.sh                 # all tests
#   tests/run-tests.sh --filter X      # any dotnet test arguments
#
# From WSL with the repository on the Windows drive, the build runs in a copy (~/gsp3w) so it never writes
# into the Windows bin/obj folders (or a copy of the app that is running from them).
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${DOTNET:-$(command -v dotnet || echo "$HOME/.dotnet/dotnet")}"

if [[ "$repo" == /mnt/* ]]; then
  work="$HOME/gsp3w"
  mkdir -p "$work"
  rsync -a --delete --exclude bin --exclude obj --exclude .vs --exclude node_modules --exclude TestResults "$repo/" "$work/"
  repo="$work"
fi

sandbox="$(mktemp -d -t gsp3w-test-XXXXXX)"
trap 'rm -rf "$sandbox"' EXIT

export XDG_CONFIG_HOME="$sandbox/config"
mkdir -p "$XDG_CONFIG_HOME"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

"$dotnet" test "$repo/tests/GS.Point3DWeb.Tests/GS.Point3DWeb.Tests.csproj" "$@"
