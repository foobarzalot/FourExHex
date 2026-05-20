# Tech Debt

Running list of known issues, flaky tests, and shortcuts that should eventually be cleaned up. Add new entries at the top.

## Flaky tests

### `FourExHex.Tests.LogTests.EmittedMessages_RouteVerbatimInOrder`

- **Location**: `tests/LogTests.cs:192` (assertion at line 203)
- **Symptom**: Test occasionally fails when run alongside the rest of the suite.
- **Suspected cause**: `Log.Sink` is a process-wide static; even though the test body runs under `RunIsolated`, sink/level state can leak in from concurrent xUnit test execution in other classes that touch `Log` without going through `RunIsolated`. Order of `seen.Add` calls is also vulnerable if anything else writes to the sink during the test window.
- **Possible fixes**: serialize `Log`-touching tests with an xUnit collection fixture; or make `Log.Sink` AsyncLocal so each test's sink is isolated; or audit non-`RunIsolated` callers of `Log` in the test suite.
