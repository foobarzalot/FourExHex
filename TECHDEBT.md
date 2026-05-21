# Tech Debt

Running list of known issues, flaky tests, and shortcuts that should eventually be cleaned up. Add new entries at the top.

## Godot crashes on exit with `mutex lock failed: Invalid argument`

**Where:** seen on the macOS Godot 4.6.1 mono build closing the
`FourExHex (DEBUG)` window after a normal play / map-editor session.
Tail of the launch log:

```
libc++abi: terminating due to uncaught exception of type
std::__1::system_error: mutex lock failed: Invalid argument
```

**Symptom:** the game runs fine, the user closes the window, and the
process aborts (exit code 0 from the parent shell because the shell
wrapper still reports clean exit, but the engine itself is killed by
the libc++ uncaught-exception path).

**Suspected cause:** a Godot-internal thread (likely the audio or
resource loader subsystem) trying to lock a mutex on a destroyed
object during the engine shutdown ordering. The C# stack traces
right before the abort are benign — `SaveStore.TryReadHeader` warnings
for old v1-format saves on disk that have nothing to do with the
crash. No script of ours appears on the abort's call path.

**Severity:** cosmetic for now — the game completes its session and
the user doesn't lose state. No data corruption observed.

**Candidate fixes / next steps:** reproduce under a `--verbose`
Godot launch to capture the offending thread; check if it's gated on
specific subsystems we use (HexHoverTooltip's `_Process` GuiGetHoveredControl
polling, the `FlashPress` tween, or the AudioBus autoload's tear-down
order); search Godot 4.6.1 mono issues for matching reports — this may
already be a known engine bug with an upgrade path.
