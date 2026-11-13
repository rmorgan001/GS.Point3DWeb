#!/usr/bin/env bash
# Stops what start-stack.sh started.
run="$HOME/gsp3w-run"
for name in app fake; do
  if [[ -f "$run/$name.pid" ]]; then
    kill "$(cat "$run/$name.pid")" 2>/dev/null || true
    rm -f "$run/$name.pid"
  fi
done
