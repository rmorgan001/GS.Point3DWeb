# Tests

None of these talk to a real mount: they use `GS.Point3DWeb.FakeAlpaca`, a fake Alpaca telescope server that
can be told to be slow, go down, refuse to connect, need a login, or lack `devicestate`.

| Folder | What |
| --- | --- |
| `GS.Point3DWeb.Tests/` | xUnit tests: coordinates and model maths, formatting, versions, feature keys, addresses and the edit dialog, `devicestate` parsing, translations (every `L.T("…")` text in every language, placeholders kept), the log reader, the Alpaca client and connector, the 3D view's polling loop, `servers.json` (load, save, passwords, upgrades, damaged files, many tabs at once) and the home page's logic. |
| `GS.Point3DWeb.FakeAlpaca/` | The fake server; also runs on its own for the browser and load tests. |
| `GS.Point3DWeb.Perf/` | `bench` (BenchmarkDotNet: what one poll costs), `http <url> [seconds]` (HTTP load), `poll [views…]` (many 3D views polling). |
| `e2e/` | Browser tests (`e2e.mjs`), a load test with real browser tabs (`circuits.mjs`) and an idle-drawing check (`render-idle.mjs`), with Playwright and the installed Chrome. |

## Running the tests

The settings tests use the app's real settings folder, so they only run where it is a throw-away one:

- **Linux / WSL** (everything): `tests/run-tests.sh`. It points `XDG_CONFIG_HOME` at a temporary folder. From
  WSL with the repository on the Windows drive, it builds in a copy (`~/gsp3w`), so it never writes into the
  Windows `bin` / `obj` folders, or a copy of the app running from them.
- **Windows**: `dotnet test tests/GS.Point3DWeb.Tests`. The settings tests are skipped (they would use your real
  `%APPDATA%\GS.Point3DWeb`). Don't build while the app is running from the same `bin` folder.

## Browser and load tests (WSL + Windows)

```shell
# In WSL: publish the app and the fake server, start both with a throw-away settings folder
tests/e2e/start-stack.sh            # app on :5300, fake mounts on :5400

# In Windows (or anywhere with Chrome and Node), from tests/e2e
npm install                         # Playwright only; it uses the installed Chrome
node e2e.mjs                        # browser tests
node circuits.mjs 1,5,10,20         # tabs of four live views: CPU, memory, mount requests, cleanup
node render-idle.mjs                # does the 3D view stop drawing when nothing moves?

# In WSL
tests/e2e/stop-stack.sh
```

Restart the stack between runs of `e2e.mjs`: the app keeps its connections while it runs.
