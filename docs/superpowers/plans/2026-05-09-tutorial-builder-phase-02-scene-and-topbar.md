# TutorialBuilder Phase 2: `tutorial_builder.tscn` scene + 3-mode topbar

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the TutorialBuilder scene shell. New `scenes/tutorial_builder.tscn` rooted on `TutorialBuilderScene : Node2D`, with a 3-mode topbar (Map Edit / Build / Preview, kbd 1/2/3), Save Tutorial / Load Tutorial / Exit chrome (Save/Load disabled — they wire up in Phase 3), Map Edit mode hosts the existing `MapEditorPanel` (from Phase 1) plus its palette HUD, Build/Preview modes show non-intrusive "Coming soon" placeholders. `MainMenuScene` gets a debug-only "Tutorial Builder" button.

**Architecture:** `TutorialBuilderScene` is the orchestrator. It instantiates `MapEditorPanel` once and never tears it down — mode switching toggles `panel.PaintingEnabled` and shows/hides the per-mode chrome (`MapEditorHudView` for Map Edit, `BuildPane` placeholder for Build, `PreviewPane` placeholder for Preview). The painted draft survives every mode transition because the panel is shared. `MapEditorHudView` gains two surgical configuration properties (`ShowSceneRootChrome`, `TopOffsetPx`) so the same HUD class works in both the standalone `MapEditorScene` (default chrome on, no offset) and inside TutorialBuilder (chrome off, offset 60px to sit below the topbar). No changes to `MapEditorPanel`, `HexMapView`, `GameController`, or the rules layer.

**Tech stack:** Godot 4.6.1 (.NET) + C# (net8.0).

**Spec reference:** [`docs/superpowers/specs/2026-05-09-tutorial-builder-design.md`](../specs/2026-05-09-tutorial-builder-design.md) §"TutorialBuilder scene" (lines 415–446) and §"Dev-mode gating" (lines 649–656).

**Master plan reference:** [`2026-05-09-tutorial-builder-master.md`](2026-05-09-tutorial-builder-master.md) — Phase 2 entry.

**Verification policy:** Phase 2 adds new UI scaffolding (a scene root, a topbar, two placeholder panes, a menu button) and surgical UI-only edits to `MapEditorHudView`. Every new file derives from `Node2D` / `CanvasLayer` / `Control` and is excluded from the test assembly per `CLAUDE.md` §"Project Structure". There is no pure-logic class to TDD here. Per the same rationale used for Phase 1, **Phase 2 adds no new unit tests**. Verification = existing test suite stays green + a manual regression checklist (Task 9) covering both the standalone Map Editor (proves the `MapEditorHudView` edit didn't regress it) and the new TutorialBuilder scene end-to-end.

---

## File structure

| File | Status | Responsibility |
|---|---|---|
| `scripts/TutorialBuilderScene.cs` | **Create** | Scene root. Owns `TutorialMode` state, instantiates `MapEditorPanel` + `MapEditorHudView` + `TutorialBuilderTopBar` + `BuildPane` + `PreviewPane`, wires topbar events to mode-switch logic, handles keyboard 1/2/3, ESC ladder, Exit → main menu. |
| `scripts/TutorialBuilderScene.cs.uid` | **Create (Godot generates)** | Stable resource ID. Generated automatically when Godot imports the new script; commit it. |
| `scripts/TutorialBuilderTopBar.cs` | **Create** | Top strip (`CanvasLayer`, 60px). 3 mode buttons (left), Save Tutorial / Load Tutorial / Exit (right). Emits events; owns its own visual "current mode" indication. |
| `scripts/TutorialBuilderTopBar.cs.uid` | **Create (Godot generates)** | Stable resource ID. |
| `scripts/BuildPane.cs` | **Create** | Placeholder `Control` for Build mode. Bottom-center label "Build mode — Coming soon (Phase 3)". Visible only when `TutorialMode == Build`. Phase 3 grows this into the real timeline + inspector. |
| `scripts/BuildPane.cs.uid` | **Create (Godot generates)** | Stable resource ID. |
| `scripts/PreviewPane.cs` | **Create** | Placeholder `Control` for Preview mode. Bottom-center label "Preview mode — Coming soon (Phase 3)". Visible only when `TutorialMode == Preview`. Phase 3 grows this into the transient `GameController` + `TutorialPlayer` host. |
| `scripts/PreviewPane.cs.uid` | **Create (Godot generates)** | Stable resource ID. |
| `scripts/MapEditorHudView.cs` | **Modify** (surgical) | Add two new public properties — `ShowSceneRootChrome` (default true) and `TopOffsetPx` (default 0). Adjust positions in `_Ready` to honor `TopOffsetPx`; wrap Save Map / Load Map / Exit creation in `if (ShowSceneRootChrome)`. |
| `scripts/MainMenuScene.cs` | **Modify** (surgical) | Add a debug-only "Tutorial Builder" button to the landing panel beneath "Map Editor", gated on `OS.IsDebugBuild()`. Clicked → `ChangeSceneToFile("res://scenes/tutorial_builder.tscn")`. |
| `scenes/tutorial_builder.tscn` | **Create** | Tiny scene file mirroring `scenes/map_editor.tscn`'s shape — one root node typed `Node2D`, script set to `TutorialBuilderScene.cs`. |
| `tests/FourExHex.Tests.csproj` | **No change** | New `Tutorial*` files derive from `Node` / `Control` / `CanvasLayer` and depend on `SceneTree`; they stay out of the test assembly (same exclusion as `MapEditorScene.cs`, `MapEditorPanel.cs`, etc., per `CLAUDE.md` §"Project Structure"). |

**Files NOT touched in Phase 2 (deferred to later phases):**
- `MapEditorPanel.cs` — already exposes `PaintingEnabled` / `SnapshotDraft` / `RestoreDraft` / `BuildLiveState` from Phase 1. Phase 2 only flips `PaintingEnabled` on mode switch; no panel API changes needed.
- `HexMapView.cs` — pan math reserves `HudView.HudHeight = 60` at the top. In TutorialBuilder's Map Edit mode, the topbar (60px) plus Map Edit HUD (60px) total 120px of chrome, so the top 60px of the map's available pan area sits behind the Map Edit HUD strip. This is acceptable for Phase 2 — Phase 14 polish will tune it if needed.
- All `Tutorial*` POCOs (`Beat`, `Tutorial`, `AnchorRef`, `DismissOn`, `TutorialPlayer`, `TutorialValidator`, gated views) — Phase 3.
- The Tutorial title field shown on the topbar in the spec's wireframes — defers to Phase 3 alongside the `Tutorial` POCO it binds to.
- The Save Tutorial / Load Tutorial dialogs — buttons render disabled in Phase 2; the dialog plumbing arrives in Phase 3 along with `SaveStore.WriteTutorial` / `LoadTutorial` / `ListTutorials`.

---

## Public API the new files expose

These are the contracts later phases depend on. Phase 2 ships all of them in their minimal form, even where Phase 2 itself doesn't exercise them (e.g., `BuildPane.SetTutorial` is a no-op stub in Phase 2 but the call site is wired so Phase 3 only has to fill in the body).

```csharp
// In TutorialBuilderScene.cs (top of file, before the class):
public enum TutorialMode { MapEdit, Build, Preview }

public sealed partial class TutorialBuilderScene : Node2D
{
    // No public API surface. _Ready does all the wiring; the scene is the
    // root of its own scene tree. Mode-switch happens via the topbar's
    // events (which fire from button clicks or kbd 1/2/3) — there's no
    // external programmatic entry point in Phase 2.
}

public sealed partial class TutorialBuilderTopBar : CanvasLayer
{
    public event Action<TutorialMode>? ModeRequested;
    public event Action? SaveTutorialPressed;
    public event Action? LoadTutorialPressed;
    public event Action? ExitPressed;

    // Updates the "current mode" visual indication (current button is
    // disabled / greyed). Phase 2's scene calls this after every mode
    // change to keep the visuals in sync.
    public void SetCurrentMode(TutorialMode mode);

    // Phase 3 enables Save/Load Tutorial via these. Phase 2 leaves them
    // false (the buttons render Disabled = true).
    public bool SaveEnabled  { get; set; }   // default false
    public bool LoadEnabled  { get; set; }   // default false
}

public sealed partial class BuildPane : Control
{
    // Phase 2 stub — does nothing. Phase 3 fills in the body to bind
    // the BuildPane to the in-memory Tutorial POCO and the panel.
    public void SetPanel(MapEditorPanel panel);
}

public sealed partial class PreviewPane : Control
{
    // Phase 2 stub — does nothing. Phase 3 fills in the body to clone
    // the panel's draft into a transient GameController + TutorialPlayer.
    public void SetPanel(MapEditorPanel panel);
    public void Pause();   // Phase 3 hooks the transient controller; no-op in Phase 2.
}

// In MapEditorHudView.cs (added near the existing public surface):
public bool ShowSceneRootChrome { get; set; } = true;   // Save Map / Load Map / Exit
public int  TopOffsetPx         { get; set; } = 0;      // y-offset for the whole strip
```

The `SetPanel` methods exist now (taking the panel reference at construction time) so Phase 3's BuildPane/PreviewPane upgrades only have to fill in the body — the call sites in `TutorialBuilderScene._Ready` don't need to be re-wired.

---

## Layout (TutorialBuilder scene)

```
y=0                                                                  viewport_x
┌─────────────────────────────────────────────────────────────────────┐
│ [Map Edit] [Build] [Preview]   ...   [Save Tutorial][Load Tutorial][Exit]  │  ← TutorialBuilderTopBar (60px)
├─────────────────────────────────────────────────────────────────────┤
│ Seed [..] [Generate]  [Hand][r][g][b][y][o][p][water][tree][cap][tower]   │
│                                              [Undo All][Last][Last][All]  │  ← MapEditorHudView
y=120                                                                       │     (60px, Map Edit mode only,
│                                                                          │      TopOffsetPx=60, ShowSceneRootChrome=false)
│                       ▒▒▒  HexMapView (panel)  ▒▒▒                        │
│                                                                          │
│                                                                          │
│   ┌──────────────────────────────────────────────────────────┐           │
│   │     "Build mode — Coming soon (Phase 3)"                  │  ← BuildPane (Build mode only)
│   └──────────────────────────────────────────────────────────┘           │
│   (PreviewPane shows the same placeholder centered when in Preview mode)  │
└─────────────────────────────────────────────────────────────────────┘
```

In Build / Preview mode the Map Edit HUD is hidden (its `Visible` toggles), so only the topbar (60px) sits above the map. The painted draft is always visible underneath the placeholder labels — they're small bottom-anchored strips, not full-screen overlays. The map-share / pan-preserve invariant from the spec holds in Phase 2 because the panel and its `HexMapView` are never re-instantiated.

---

## Tasks

10 tasks total. Builds compile after each script-creating task; the manual test runs at the end.

---

### Task 1: Read existing source so the integration is precise

**Files:**
- Read: `scripts/MainMenuScene.cs:120-156` (the landing-panel layout — where the new Tutorial Builder button slots in, and how the existing Map Editor button is built)
- Read: `scripts/MapEditorHudView.cs:56-234` (the body of `_Ready` — every position/offset that needs to honor `TopOffsetPx`, plus the Save Map / Load Map / Exit blocks that need to wrap in `if (ShowSceneRootChrome)`)
- Read: `scripts/MapEditorScene.cs` (the host pattern — how it wires `MapEditorHudView` events to `MapEditorPanel` methods; Phase 2's TutorialBuilderScene mirrors this exactly for the Map Edit mode HUD)
- Read: `scenes/map_editor.tscn` (the structural template for the new `tutorial_builder.tscn`)
- Read: `scripts/MapEditorPanel.cs:42-49` (confirm `PaintingEnabled` is a settable bool and that `DraftChanged` / `UndoStateChanged` are nullary `Action`s — the scene wires these the same way `MapEditorScene` does)

- [ ] **Step 1: Read all five files**

Use the `Read` tool for each (not bash `cat`).

Confirm before proceeding:
- The exact y / Position / OffsetTop / OffsetBottom values in `MapEditorHudView._Ready` that need a `+ TopOffsetPx` term.
- That the Save Map (lines 205–213), Load Map (lines 215–223), Exit (lines 225–233) blocks are contiguous and can be wrapped in a single `if (ShowSceneRootChrome) { ... }`.
- That `MainMenuScene.BuildLandingPanel` builds buttons at y = `firstButtonY + (buttonH + buttonGap) * N` for N = 0,1,2,3 — the new Tutorial Builder button slots in at N = 4 (panel height also needs a small bump).
- That `map_editor.tscn` is a 7-line file with a single `Node2D` root and a script `ext_resource`; the new `tutorial_builder.tscn` follows the same shape.

---

### Task 2: Make `MapEditorHudView` configurable for use inside TutorialBuilder

**Files:**
- Modify: `scripts/MapEditorHudView.cs`

**Why this comes first:** The new `TutorialBuilderScene` creates a `MapEditorHudView` with `TopOffsetPx = 60` and `ShowSceneRootChrome = false`. Those properties have to exist before the scene can compile. This task ships only the HUD changes; nothing user-visible changes (the standalone Map Editor scene still constructs the HUD with no property overrides, hitting the defaults `0` / `true`).

- [ ] **Step 1: Add the two properties to `MapEditorHudView`**

Open `scripts/MapEditorHudView.cs`. Find the existing public-property cluster (around lines 36–46, with `event Action? ExitClicked` etc. and `public int SelectedPaletteIndex`). Insert these two new properties immediately above `SelectedPaletteIndex`:

```csharp
    /// <summary>
    /// When false, the right-side Save Map / Load Map / Exit buttons are
    /// not built. Hosts that supply their own scene-root chrome (e.g.
    /// TutorialBuilderScene's topbar) set this to false. The standalone
    /// Map Editor scene leaves it at the default true.
    /// </summary>
    public bool ShowSceneRootChrome { get; set; } = true;

    /// <summary>
    /// Vertical offset (in pixels) for the entire HUD strip. Default 0
    /// (the standalone editor sits at the top). TutorialBuilderScene
    /// sets this to 60 so the strip renders below its topbar.
    ///
    /// Must be set before <see cref="_Ready"/> runs (i.e. before the
    /// host calls <c>AddChild(hud)</c>) — the layout is laid out once
    /// in _Ready and not re-laid-out on property change.
    /// </summary>
    public int TopOffsetPx { get; set; } = 0;
```

- [ ] **Step 2: Honor `TopOffsetPx` in the strip's positions**

Still in `_Ready`, make these three edits (each is a one-line replacement):

**Edit A (background ColorRect, near line 60):**

```csharp
        var background = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.8f),
            Position = new Vector2(0, TopOffsetPx),
            Size = new Vector2(viewport.X, HudView.HudHeight),
        };
```

**Edit B (leftHbox, near line 68):**

```csharp
        var leftHbox = new HBoxContainer
        {
            Position = new Vector2(16, 12 + TopOffsetPx),
        };
```

**Edit C (rightHbox, near line 178):**

```csharp
        var rightHbox = new HBoxContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 0f,
            OffsetRight = -16f,
            OffsetTop = 12f + TopOffsetPx,
            OffsetBottom = 48f + TopOffsetPx,
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
```

- [ ] **Step 3: Wrap Save Map / Load Map / Exit construction in `if (ShowSceneRootChrome)`**

Still in `_Ready`. The Save Map button (current lines ~205–213), Load Map button (~215–223), and Exit button (~225–233) are contiguous and all add to `rightHbox`. Wrap the three blocks in a single conditional. The shape becomes:

```csharp
        // Undo / redo cluster — same order and labels as the play HUD's
        // right strip so the muscle memory carries over.
        _undoAllButton = MakeUndoButton("Undo All", () => UndoAllClicked?.Invoke());
        rightHbox.AddChild(_undoAllButton);
        _undoLastButton = MakeUndoButton("Undo Last", () => UndoLastClicked?.Invoke());
        rightHbox.AddChild(_undoLastButton);
        _redoLastButton = MakeUndoButton("Redo Last", () => RedoLastClicked?.Invoke());
        rightHbox.AddChild(_redoLastButton);
        _redoAllButton = MakeUndoButton("Redo All", () => RedoAllClicked?.Invoke());
        rightHbox.AddChild(_redoAllButton);

        if (ShowSceneRootChrome)
        {
            var saveMapButton = new Button
            {
                Text = "Save Map",
                FocusMode = Control.FocusModeEnum.None,
            };
            saveMapButton.AddThemeFontSizeOverride("font_size", 18);
            saveMapButton.Pressed += () => SaveMapClicked?.Invoke();
            AudioBus.AttachClick(saveMapButton);
            rightHbox.AddChild(saveMapButton);

            var loadMapButton = new Button
            {
                Text = "Load Map",
                FocusMode = Control.FocusModeEnum.None,
            };
            loadMapButton.AddThemeFontSizeOverride("font_size", 18);
            loadMapButton.Pressed += () => LoadMapClicked?.Invoke();
            AudioBus.AttachClick(loadMapButton);
            rightHbox.AddChild(loadMapButton);

            var exitButton = new Button
            {
                Text = "Exit",
                FocusMode = Control.FocusModeEnum.None,
            };
            exitButton.AddThemeFontSizeOverride("font_size", 18);
            exitButton.Pressed += () => ExitClicked?.Invoke();
            AudioBus.AttachClick(exitButton);
            rightHbox.AddChild(exitButton);
        }
```

The events `SaveMapClicked` / `LoadMapClicked` / `ExitClicked` stay declared on the class regardless of `ShowSceneRootChrome` — when chrome is off, they simply never fire (no buttons to fire them). That's fine; no subscriber crashes if the event never raises.

- [ ] **Step 4: Build to confirm it compiles**

```bash
dotnet build FourExHex.csproj
```

Expected: build succeeds, zero errors, zero warnings.

- [ ] **Step 5: Run the test suite (regression check)**

```bash
dotnet test
```

Expected: all green. `MapEditorHudView` is not in the test assembly, but its rules-layer / serializer / snapshot dependencies must still pass — which they will, since this task touches only UI layout.

---

### Task 3: Create `TutorialBuilderTopBar.cs`

**Files:**
- Create: `scripts/TutorialBuilderTopBar.cs`

- [ ] **Step 1: Write the new file**

Create `scripts/TutorialBuilderTopBar.cs` with the following content:

```csharp
using System;
using Godot;

/// <summary>
/// Top strip for the TutorialBuilder scene. 60px tall, anchored to the
/// top of the viewport. Hosts the 3-mode segmented control (Map Edit /
/// Build / Preview) on the left and the Save Tutorial / Load Tutorial /
/// Exit buttons on the right. Emits events for every interaction; owns
/// only the visual "current mode" indication (the current mode's button
/// is rendered Disabled = true).
///
/// Phase 2 leaves Save Tutorial and Load Tutorial disabled (the button
/// plumbing arrives in Phase 3 with the Tutorial POCO + SaveStore
/// extensions). Setting <see cref="SaveEnabled"/> / <see cref="LoadEnabled"/>
/// to true at any time after _Ready re-enables those buttons.
/// </summary>
public sealed partial class TutorialBuilderTopBar : CanvasLayer
{
    public event Action<TutorialMode>? ModeRequested;
    public event Action? SaveTutorialPressed;
    public event Action? LoadTutorialPressed;
    public event Action? ExitPressed;

    public bool SaveEnabled
    {
        get => _saveEnabled;
        set
        {
            _saveEnabled = value;
            if (_saveButton != null) _saveButton.Disabled = !value;
        }
    }
    private bool _saveEnabled;

    public bool LoadEnabled
    {
        get => _loadEnabled;
        set
        {
            _loadEnabled = value;
            if (_loadButton != null) _loadButton.Disabled = !value;
        }
    }
    private bool _loadEnabled;

    private Button _mapEditButton = null!;
    private Button _buildButton = null!;
    private Button _previewButton = null!;
    private Button _saveButton = null!;
    private Button _loadButton = null!;

    private TutorialMode _currentMode = TutorialMode.MapEdit;

    public override void _Ready()
    {
        Vector2 viewport = GetViewport().GetVisibleRect().Size;

        // Same dark-strip styling and height as MapEditorHudView so the
        // two stacked HUDs read as a single 120px chrome zone.
        var background = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.85f),
            Position = Vector2.Zero,
            Size = new Vector2(viewport.X, HudView.HudHeight),
        };
        AddChild(background);

        // Left cluster: 3 mode buttons.
        var leftHbox = new HBoxContainer
        {
            Position = new Vector2(16, 12),
        };
        leftHbox.AddThemeConstantOverride("separation", 8);
        AddChild(leftHbox);

        _mapEditButton = MakeModeButton("Map Edit  (1)", TutorialMode.MapEdit);
        leftHbox.AddChild(_mapEditButton);
        _buildButton = MakeModeButton("Build  (2)", TutorialMode.Build);
        leftHbox.AddChild(_buildButton);
        _previewButton = MakeModeButton("Preview  (3)", TutorialMode.Preview);
        leftHbox.AddChild(_previewButton);

        // Right cluster: Save Tutorial / Load Tutorial / Exit.
        var rightHbox = new HBoxContainer
        {
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0f,
            AnchorBottom = 0f,
            OffsetLeft = 0f,
            OffsetRight = -16f,
            OffsetTop = 12f,
            OffsetBottom = 48f,
            Alignment = BoxContainer.AlignmentMode.End,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        rightHbox.AddThemeConstantOverride("separation", 8);
        AddChild(rightHbox);

        _saveButton = new Button
        {
            Text = "Save Tutorial",
            FocusMode = Control.FocusModeEnum.None,
            Disabled = !_saveEnabled,
        };
        _saveButton.AddThemeFontSizeOverride("font_size", 18);
        _saveButton.Pressed += () => SaveTutorialPressed?.Invoke();
        AudioBus.AttachClick(_saveButton);
        rightHbox.AddChild(_saveButton);

        _loadButton = new Button
        {
            Text = "Load Tutorial",
            FocusMode = Control.FocusModeEnum.None,
            Disabled = !_loadEnabled,
        };
        _loadButton.AddThemeFontSizeOverride("font_size", 18);
        _loadButton.Pressed += () => LoadTutorialPressed?.Invoke();
        AudioBus.AttachClick(_loadButton);
        rightHbox.AddChild(_loadButton);

        var exitButton = new Button
        {
            Text = "Exit",
            FocusMode = Control.FocusModeEnum.None,
        };
        exitButton.AddThemeFontSizeOverride("font_size", 18);
        exitButton.Pressed += () => ExitPressed?.Invoke();
        AudioBus.AttachClick(exitButton);
        rightHbox.AddChild(exitButton);

        SetCurrentMode(_currentMode);
    }

    /// <summary>
    /// Update the visual "current mode" indication. The current mode's
    /// button is Disabled = true (greyed out, not clickable — clicking
    /// the already-current mode is a no-op anyway). Other mode buttons
    /// re-enable.
    /// </summary>
    public void SetCurrentMode(TutorialMode mode)
    {
        _currentMode = mode;
        if (_mapEditButton == null) return;   // _Ready hasn't run yet
        _mapEditButton.Disabled = mode == TutorialMode.MapEdit;
        _buildButton.Disabled   = mode == TutorialMode.Build;
        _previewButton.Disabled = mode == TutorialMode.Preview;
    }

    private Button MakeModeButton(string label, TutorialMode mode)
    {
        var b = new Button
        {
            Text = label,
            FocusMode = Control.FocusModeEnum.None,
        };
        b.AddThemeFontSizeOverride("font_size", 18);
        b.Pressed += () => ModeRequested?.Invoke(mode);
        AudioBus.AttachClick(b);
        return b;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build FourExHex.csproj
```

Expected: **Will fail** — `TutorialMode` is referenced but not yet defined (Task 5 creates it inside `TutorialBuilderScene.cs`). This is expected. Move to Task 4.

If you want a build that actually succeeds at this step instead, you can defer Task 3's build check to after Task 5. Document the choice and continue.

---

### Task 4: Create placeholder `BuildPane.cs` and `PreviewPane.cs`

**Files:**
- Create: `scripts/BuildPane.cs`
- Create: `scripts/PreviewPane.cs`

These are minimal stubs so Phase 2's mode-switch wiring is structurally complete and Phase 3 only has to fill in their bodies.

- [ ] **Step 1: Write `scripts/BuildPane.cs`**

```csharp
using Godot;

/// <summary>
/// Build mode chrome. Phase 2 stub — bottom-anchored "Coming soon" label.
/// Phase 3 grows this into the timeline + inspector + add-beat palette
/// described in spec §"Build mode".
/// </summary>
public sealed partial class BuildPane : Control
{
    public override void _Ready()
    {
        // Stretch to fill the viewport so the placeholder label can be
        // anchored relative to it, but keep MouseFilter = Ignore so the
        // pane doesn't swallow clicks meant for the panel's HexMapView
        // underneath.
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Ignore;

        var label = new Label
        {
            Text = "Build mode — Coming soon (Phase 3)",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -56f,
            OffsetBottom = -16f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
        AddChild(label);
    }

    /// <summary>
    /// Called once by <see cref="TutorialBuilderScene._Ready"/> to hand the
    /// pane the panel it should bind to. Phase 2 ignores the reference;
    /// Phase 3 stores it for the state-after-beat-N cache.
    /// </summary>
    public void SetPanel(MapEditorPanel panel)
    {
        // Intentionally empty in Phase 2.
        _ = panel;
    }
}
```

- [ ] **Step 2: Write `scripts/PreviewPane.cs`**

```csharp
using Godot;

/// <summary>
/// Preview mode chrome. Phase 2 stub — bottom-anchored "Coming soon"
/// label. Phase 3 grows this into the transient GameController +
/// TutorialPlayer host described in spec §"Preview mode".
/// </summary>
public sealed partial class PreviewPane : Control
{
    public override void _Ready()
    {
        AnchorLeft = 0f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 1f;
        MouseFilter = MouseFilterEnum.Ignore;

        var label = new Label
        {
            Text = "Preview mode — Coming soon (Phase 3)",
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetTop = -56f,
            OffsetBottom = -16f,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", 22);
        label.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f, 0.85f));
        AddChild(label);
    }

    /// <summary>
    /// Called once by <see cref="TutorialBuilderScene._Ready"/> to hand the
    /// pane the panel it should bind to. Phase 3 uses this to clone the
    /// panel's draft into a transient GameController.
    /// </summary>
    public void SetPanel(MapEditorPanel panel)
    {
        // Intentionally empty in Phase 2.
        _ = panel;
    }

    /// <summary>
    /// Called by the scene when leaving Preview mode. Phase 3 disposes
    /// the transient controller and clears the scrubber state.
    /// </summary>
    public void Pause()
    {
        // Intentionally empty in Phase 2.
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build FourExHex.csproj
```

Expected: **Still fails** — `TutorialMode` isn't defined yet. Move to Task 5.

---

### Task 5: Create `TutorialBuilderScene.cs`

**Files:**
- Create: `scripts/TutorialBuilderScene.cs`

This is the orchestrator. Owns the mode state machine, instantiates and mounts every other piece, wires events.

- [ ] **Step 1: Write the file**

```csharp
using System.Collections.Generic;
using Godot;

/// <summary>
/// Top-level mode for the TutorialBuilder scene. The 3-mode topbar
/// switches between these; the scene's <see cref="TutorialBuilderScene"/>
/// owns the state machine.
/// </summary>
public enum TutorialMode { MapEdit, Build, Preview }

/// <summary>
/// TutorialBuilder scene root. Mirrors <see cref="MapEditorScene"/>'s
/// shape — instantiates the reusable <see cref="MapEditorPanel"/> from
/// Phase 1 plus its palette HUD — and adds a top-level mode switcher
/// (<see cref="TutorialBuilderTopBar"/>) and per-mode chrome
/// (<see cref="BuildPane"/> placeholder, <see cref="PreviewPane"/>
/// placeholder). The panel is created once and never torn down; mode
/// switching toggles <see cref="MapEditorPanel.PaintingEnabled"/> and
/// the per-mode chrome's Visible flag, so the painted draft survives
/// every mode transition.
///
/// Phase 2 ships only the scaffolding. Phase 3 wires Save Tutorial /
/// Load Tutorial and grows BuildPane / PreviewPane into real chrome.
/// </summary>
public partial class TutorialBuilderScene : Node2D
{
    private MapEditorPanel _panel = null!;
    private MapEditorHudView _mapEditHud = null!;
    private TutorialBuilderTopBar _topBar = null!;
    private BuildPane _buildPane = null!;
    private PreviewPane _previewPane = null!;
    private List<Player> _players = null!;

    private TutorialMode _currentMode = TutorialMode.MapEdit;

    public override void _Ready()
    {
        _players = BuildPlayers();

        // 1. The reusable Map Editor panel (Phase 1). Owns the map +
        //    draft state + paint stroke machine + undo. Painting starts
        //    enabled because Phase 2 lands in Map Edit mode.
        _panel = new MapEditorPanel { Players = _players };
        AddChild(_panel);

        // 2. The Map Edit palette HUD. TopOffsetPx = 60 puts it below
        //    the topbar; ShowSceneRootChrome = false hides Save Map /
        //    Load Map / Exit (those are TutorialBuilder-level concerns
        //    and live on the topbar instead). The scene wires its
        //    palette/seed/undo events to panel methods exactly the way
        //    MapEditorScene does.
        _mapEditHud = new MapEditorHudView
        {
            TopOffsetPx = (int)HudView.HudHeight,
            ShowSceneRootChrome = false,
        };
        AddChild(_mapEditHud);
        _mapEditHud.GenerateRequested += _panel.GenerateMap;
        _mapEditHud.PaletteSelectionChanged += _panel.SetSelectedPalette;
        _mapEditHud.UndoLastClicked += _panel.UndoLast;
        _mapEditHud.UndoAllClicked += _panel.UndoAll;
        _mapEditHud.RedoLastClicked += _panel.RedoLast;
        _mapEditHud.RedoAllClicked += _panel.RedoAll;
        _panel.UndoStateChanged += () =>
            _mapEditHud.SetUndoState(_panel.CanUndo, _panel.CanRedo);
        _mapEditHud.SetUndoState(canUndo: false, canRedo: false);

        // 3. The 3-mode topbar. Owns its own visual current-mode
        //    indication; Save / Load Tutorial start disabled and stay
        //    disabled until Phase 3 wires them up.
        _topBar = new TutorialBuilderTopBar();
        AddChild(_topBar);
        _topBar.ModeRequested += SetMode;
        _topBar.ExitPressed += ReturnToMainMenu;
        // SaveTutorialPressed / LoadTutorialPressed are wired in Phase 3.
        // Their buttons are Disabled = true in Phase 2 so they can't fire
        // — the events will simply have no subscriber.

        // 4. Per-mode placeholder chrome. Both start Visible = false;
        //    SetMode flips visibility on transitions.
        _buildPane = new BuildPane { Visible = false };
        _buildPane.SetPanel(_panel);
        AddChild(_buildPane);

        _previewPane = new PreviewPane { Visible = false };
        _previewPane.SetPanel(_panel);
        AddChild(_previewPane);

        // 5. Initial mode. Map Edit on entry; topbar's SetCurrentMode
        //    syncs the visual indicator.
        SetMode(TutorialMode.MapEdit);
    }

    /// <summary>
    /// Mode-switch state machine. Toggles painting on the panel,
    /// shows/hides each mode's chrome, and updates the topbar's visual
    /// indicator. Idempotent — calling SetMode(currentMode) is a no-op
    /// (returns early).
    /// </summary>
    private void SetMode(TutorialMode mode)
    {
        if (mode == _currentMode) return;
        TutorialMode previous = _currentMode;
        _currentMode = mode;

        // Painting is only enabled in Map Edit. The panel's
        // PaintingEnabled flag gates every paint event at the source.
        _panel.PaintingEnabled = mode == TutorialMode.MapEdit;

        // Show/hide each mode's chrome. The panel's HexMapView is shared
        // across modes and stays visible; the painted draft persists
        // because we never tear down the panel.
        _mapEditHud.Visible = mode == TutorialMode.MapEdit;
        _buildPane.Visible = mode == TutorialMode.Build;
        _previewPane.Visible = mode == TutorialMode.Preview;

        // Phase 3+ uses Pause() to dispose the transient controller when
        // leaving Preview. Phase 2's Pause is a no-op but the call site
        // is wired now.
        if (previous == TutorialMode.Preview)
        {
            _previewPane.Pause();
        }

        _topBar.SetCurrentMode(mode);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo) return;

        switch (keyEvent.Keycode)
        {
            case Key.Key1:
                SetMode(TutorialMode.MapEdit);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key2:
                SetMode(TutorialMode.Build);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Key3:
                SetMode(TutorialMode.Preview);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Escape:
                HandleEscape();
                GetViewport().SetInputAsHandled();
                break;
        }
        // Note: 1 / 2 / 3 reach _UnhandledInput only when no focused
        // Control consumed the keypress first — LineEdit (the seed
        // field in MapEditorHudView) consumes digit keys via its
        // GuiInput, so typing in the seed field doesn't trigger mode
        // switches. That's the desired behavior.
    }

    /// <summary>
    /// ESC ladders out:
    ///   1. If in Build or Preview → drop to Map Edit.
    ///   2. Else if a non-hand palette is selected → drop to hand.
    ///   3. Else exit to the main menu.
    ///
    /// Phase 14 polish may extend this (cancel current placement first
    /// in Build mode, etc.); Phase 2's three-step ladder is sufficient
    /// for the manual-test checklist.
    /// </summary>
    private void HandleEscape()
    {
        if (_currentMode != TutorialMode.MapEdit)
        {
            SetMode(TutorialMode.MapEdit);
            return;
        }
        if (_mapEditHud.SelectedPaletteIndex != MapEditorHudView.HandPaletteIndex)
        {
            _mapEditHud.SelectHand();
            return;
        }
        ReturnToMainMenu();
    }

    private static List<Player> BuildPlayers()
    {
        // Same roster MapEditorScene builds — every slot Human (the
        // panel doesn't care about kind, it just needs Players for
        // map generation).
        var players = new List<Player>();
        for (int i = 0; i < GameSettings.PlayerConfig.Length; i++)
        {
            (string name, string hex) = GameSettings.PlayerConfig[i];
            players.Add(new Player(name, new Color(hex), AiKind.Human));
        }
        return players;
    }

    private void ReturnToMainMenu()
    {
        GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build FourExHex.csproj
```

Expected: build succeeds, zero errors. Two-or-three of the new files reference each other but `TutorialMode` is now defined.

- [ ] **Step 3: Run the test suite**

```bash
dotnet test
```

Expected: all green. New scripts are excluded from the test assembly; the production assembly compiles cleanly.

---

### Task 6: Create `scenes/tutorial_builder.tscn`

**Files:**
- Create: `scenes/tutorial_builder.tscn`

- [ ] **Step 1: Write the scene file**

Mirror `scenes/map_editor.tscn`'s shape — single `Node2D` root with the new script attached.

```
[gd_scene load_steps=2 format=3 uid="uid://b0fourexhextutbuild"]

[ext_resource type="Script" path="res://scripts/TutorialBuilderScene.cs" id="1_tb"]

[node name="TutorialBuilder" type="Node2D"]
script = ExtResource("1_tb")
```

The `uid://b0fourexhextutbuild` is a fresh unique ID — Godot will accept any string starting with `uid://` and matching its format. This one doesn't collide with `b0fourexheditor`, `b0fourexhexmenu`, or any other existing UID in the project.

- [ ] **Step 2: Verify the file**

```bash
ls -la scenes/tutorial_builder.tscn
```

Expected: file exists, ~5 lines.

---

### Task 7: Add the debug-only "Tutorial Builder" button to `MainMenuScene`

**Files:**
- Modify: `scripts/MainMenuScene.cs`

- [ ] **Step 1: Bump the panel height to fit a fifth button**

In `BuildLandingPanel` (around line 93 of `MainMenuScene.cs`), the panel height is `panelH = 500f` and there are currently 4 buttons (Play / Tutorial / Load / Map Editor). Add a fifth button slot — extend the panel by `(buttonH + buttonGap) = 80px` so it goes to `580f`:

Find this block:
```csharp
        const float panelW = 520f;
        const float panelH = 500f;
```

Replace with:
```csharp
        const float panelW = 520f;
        // 580f instead of 500f to accommodate the debug-only Tutorial
        // Builder button below Map Editor (debug builds only — release
        // builds render the same 4-button stack against a panel that's
        // 80px taller than necessary; not enough to be worth a runtime
        // resize since OS.IsDebugBuild() is compile-time-stable for any
        // given binary).
        const float panelH = 580f;
```

- [ ] **Step 2: Add the Tutorial Builder button**

Find the Map Editor button block (around lines 147–153):

```csharp
        var mapEditorButton = new Button { Text = "Map Editor" };
        mapEditorButton.AddThemeFontSizeOverride("font_size", 26);
        mapEditorButton.Position = new Vector2(buttonInset, firstButtonY + (buttonH + buttonGap) * 3);
        mapEditorButton.Size = new Vector2(buttonW, buttonH);
        mapEditorButton.Pressed += OnMapEditorPressed;
        AudioBus.AttachClick(mapEditorButton);
        panel.AddChild(mapEditorButton);
```

Insert this block immediately AFTER it, just before the `return panel;`:

```csharp
        // Debug-only entry point into the new authoring tool. Per spec
        // §"Dev-mode gating", this button is gated on OS.IsDebugBuild()
        // — release exports never see it.
        if (OS.IsDebugBuild())
        {
            var tutorialBuilderButton = new Button { Text = "Tutorial Builder" };
            tutorialBuilderButton.AddThemeFontSizeOverride("font_size", 26);
            tutorialBuilderButton.Position = new Vector2(
                buttonInset, firstButtonY + (buttonH + buttonGap) * 4);
            tutorialBuilderButton.Size = new Vector2(buttonW, buttonH);
            tutorialBuilderButton.Pressed += OnTutorialBuilderPressed;
            AudioBus.AttachClick(tutorialBuilderButton);
            panel.AddChild(tutorialBuilderButton);
        }
```

- [ ] **Step 3: Add the OnTutorialBuilderPressed handler**

Find the existing `OnMapEditorPressed` handler (around line 444):

```csharp
    private void OnMapEditorPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/map_editor.tscn");
    }
```

Insert this method immediately after it:

```csharp
    private void OnTutorialBuilderPressed()
    {
        GetTree().ChangeSceneToFile("res://scenes/tutorial_builder.tscn");
    }
```

- [ ] **Step 4: Build**

```bash
dotnet build FourExHex.csproj
```

Expected: build succeeds, zero errors, zero warnings.

- [ ] **Step 5: Run the test suite (regression check)**

```bash
dotnet test
```

Expected: all green.

---

### Task 8: Headless import to generate `.uid` files

**Files:**
- Will appear after import: `scripts/TutorialBuilderScene.cs.uid`, `scripts/TutorialBuilderTopBar.cs.uid`, `scripts/BuildPane.cs.uid`, `scripts/PreviewPane.cs.uid`

- [ ] **Step 1: Refresh the Godot asset cache**

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path . --import
```

Expected: completes with no errors. Four new `.uid` files appear in `scripts/` (one per new `.cs` file). The new `scenes/tutorial_builder.tscn` is also imported.

- [ ] **Step 2: Verify the `.uid` files exist**

```bash
ls -la scripts/TutorialBuilderScene.cs.uid scripts/TutorialBuilderTopBar.cs.uid scripts/BuildPane.cs.uid scripts/PreviewPane.cs.uid
```

Expected: all four files present, each a single-line file.

- [ ] **Step 3: Stage the `.uid` files**

```bash
git add scripts/TutorialBuilderScene.cs.uid scripts/TutorialBuilderTopBar.cs.uid scripts/BuildPane.cs.uid scripts/PreviewPane.cs.uid
```

---

### Task 9: Manual regression test

This is the load-bearing verification. New chrome + a non-trivial config knob on `MapEditorHudView` means we must exercise both the standalone Map Editor (regression check) and the new TutorialBuilder scene (feature check).

**Files:** None modified — verification only.

- [ ] **Step 1: Launch the game**

```bash
/Applications/Godot_mono.app/Contents/MacOS/Godot --path .
```

Per `CLAUDE.md`'s "Rebuild-before-launch rule", `dotnet build` from Task 7 was already done — Godot will pick up the fresh DLL.

- [ ] **Step 2: Standalone Map Editor regression checklist**

Click "Map Editor" from the main menu. Walk through this — every item must work identically to today (proves the `MapEditorHudView` configuration changes didn't break the default-property path):

| # | Action | Expected |
|---|---|---|
| 1 | Editor opens | Water-only map, hand swatch selected, top strip shows seed + Generate + palette + undo bar + Save Map + Load Map + Exit, all in their original positions (y=0..60) |
| 2 | Type a seed, click Generate | Map regenerates |
| 3 | Click the red swatch, drag-paint over water | Tiles paint red |
| 4 | Click Save Map, save a slot | Save dialog opens, save succeeds |
| 5 | Click Load Map, click a slot | Slot loads |
| 6 | Press Z, Y | Undo/redo work |
| 7 | Press Escape twice | Drops palette to hand, then exits to main menu |

If anything regressed, do not commit — diagnose and fix.

- [ ] **Step 3: Verify the Tutorial Builder menu button is visible (debug build)**

Back at the main menu, confirm there are now 5 buttons: Play Game / Play Tutorial / Load Game / Map Editor / **Tutorial Builder**. The new button must be present (this is a debug build — `OS.IsDebugBuild()` is true when launching via the editor or a debug export).

- [ ] **Step 4: Open the Tutorial Builder and run the feature checklist**

Click "Tutorial Builder". Walk through this checklist — each item is the load-bearing verification of Phase 2's contract:

| # | Action | Expected |
|---|---|---|
| 1 | Scene opens | Two stacked HUD strips at the top: y=0..60 is the topbar (Map Edit / Build / Preview / ... / Save Tutorial / Load Tutorial / Exit), y=60..120 is the Map Edit HUD (seed + Generate + palette + undo bar — NO Save Map / Load Map / Exit). Below: water-only map. Map Edit button shows as disabled (current mode); Build / Preview enabled. |
| 2 | Save Tutorial / Load Tutorial buttons | Both visible but rendered Disabled = true (greyed). Phase 2 deliberately leaves them off. |
| 3 | Type a seed, click Generate | Map regenerates with that seed (proves the panel + Map Edit HUD wiring works) |
| 4 | Click red swatch, drag-paint a few hexes | Hexes paint red (proves PaintingEnabled is true in Map Edit) |
| 5 | Click "Build" topbar button | Mode switches to Build. Map Edit HUD strip disappears (only topbar visible at top). Painted hexes still visible. Build button disabled, Map Edit re-enabled. Bottom-center label "Build mode — Coming soon (Phase 3)" |
| 6 | Click on the painted hex | Nothing happens (PaintingEnabled is false in Build) — no paint, no exception |
| 7 | Press kbd 3 | Switches to Preview. BuildPane hides, PreviewPane shows. Bottom-center label "Preview mode — Coming soon (Phase 3)" |
| 8 | Press kbd 1 | Switches back to Map Edit. Map Edit HUD strip reappears. Painted hexes still there (panel is shared). |
| 9 | Press kbd 2 | Build mode (mirror of action 5) |
| 10 | Click "Map Edit" topbar button | Switches back. Painted hexes still there. |
| 11 | Press Escape (in Map Edit, non-hand palette selected) | Drops palette to hand |
| 12 | Press Escape again (in Map Edit, hand selected) | Exits to main menu |
| 13 | From main menu, click "Tutorial Builder" again, switch to Build, press Escape | Drops to Map Edit (not main menu) |
| 14 | Press Escape twice more | Drops palette to hand (no-op if already hand), then exits to main menu |
| 15 | Click the seed LineEdit, type "5" | Seed field updates to "5"; mode does NOT switch (kbd 1/2/3 are suppressed when LineEdit has focus) |
| 16 | Click "Exit" on the topbar | Returns to main menu |

If anything misbehaves, **do not commit** — diagnose first.

- [ ] **Step 5: Confirm the user signs off on the manual test**

Per `CLAUDE.md`'s "Manual-test-after-every-change rule", ask the user explicitly: "Phase 2 manual test passed?" Wait for confirmation before committing.

---

### Task 10: Commit + mark Phase 2 complete

**Files:** None additionally modified.

- [ ] **Step 1: Stage everything**

```bash
git add scripts/TutorialBuilderScene.cs scripts/TutorialBuilderScene.cs.uid \
        scripts/TutorialBuilderTopBar.cs scripts/TutorialBuilderTopBar.cs.uid \
        scripts/BuildPane.cs scripts/BuildPane.cs.uid \
        scripts/PreviewPane.cs scripts/PreviewPane.cs.uid \
        scripts/MapEditorHudView.cs \
        scripts/MainMenuScene.cs \
        scenes/tutorial_builder.tscn
```

- [ ] **Step 2: Verify the staged diff**

```bash
git diff --cached --stat
```

Expected: ~11 files changed.
- 4 new `.cs` scripts (TutorialBuilderScene, TutorialBuilderTopBar, BuildPane, PreviewPane)
- 4 new `.cs.uid` files
- 1 new scene (`tutorial_builder.tscn`)
- 2 modified files (MapEditorHudView.cs, MainMenuScene.cs)

- [ ] **Step 3: Commit**

```bash
git commit -m "$(cat <<'EOF'
Add tutorial_builder.tscn + 3-mode topbar (TutorialBuilder Phase 2)

New scene rooted on TutorialBuilderScene : Node2D. Topbar (Map Edit /
Build / Preview, kbd 1/2/3, plus Save Tutorial / Load Tutorial / Exit)
sits at y=0..60; Map Edit mode also shows a palette HUD at y=60..120.
Map Edit hosts the reusable MapEditorPanel from Phase 1; Build / Preview
show non-intrusive "Coming soon" placeholders. The panel is shared
across modes — painted draft survives every mode transition.

MapEditorHudView gains two configuration properties (TopOffsetPx,
ShowSceneRootChrome) so the same HUD class works in both the standalone
Map Editor (defaults) and inside TutorialBuilder (offset 60, chrome
hidden — topbar owns Save / Load / Exit).

MainMenuScene grows a debug-only "Tutorial Builder" button gated on
OS.IsDebugBuild(). Save Tutorial / Load Tutorial render disabled in
Phase 2; they wire up in Phase 3 with the Tutorial POCO + SaveStore
extensions.

See docs/superpowers/specs/2026-05-09-tutorial-builder-design.md
§"TutorialBuilder scene" and §"Dev-mode gating".

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Verify the commit**

```bash
git log -1 --stat
```

Expected: shows the commit with ~11 files changed.

- [ ] **Step 5: Mark Phase 2 complete in the master plan**

Edit `docs/superpowers/plans/2026-05-09-tutorial-builder-master.md`. In the Phase 2 entry, change `**Status:** ⏳ Not yet expanded` to `**Status:** ✅ Complete` and update the `**Plan file:**` line to point at this Phase 2 plan file. Commit:

```bash
git add docs/superpowers/plans/2026-05-09-tutorial-builder-master.md
git commit -m "Mark TutorialBuilder Phase 2 complete

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Definition of done

Phase 2 is complete when **all of these are true**:

1. `git status` is clean.
2. `dotnet build FourExHex.csproj` succeeds with zero warnings.
3. `dotnet test` passes (every existing test green).
4. Both manual regression checklists (Task 9 Steps 2 and 4) pass in the live game.
5. The user has explicitly confirmed both manual tests passed.
6. All four new `.cs.uid` files are committed.
7. `scenes/tutorial_builder.tscn` is committed.
8. The master plan's Phase 2 status is `✅ Complete` and links to this plan file.

## What Phase 2 does NOT do

Per the master plan's strict-sequential constraint, Phase 2 is scaffolding only. The following are **out of scope** and arrive in later phases:

- **No `Tutorial` POCO, no `Beat` types, no JSON v3, no `SaveSerializer` bump** — Phase 3.
- **No `TutorialPlayer`, no gated views, no `TutorialValidator`** — Phase 3.
- **Save Tutorial / Load Tutorial dialogs** — Phase 3 wires the buttons.
- **No real Build mode UI** (timeline, inspector, add-beat palette) — Phase 3+.
- **No real Preview mode** (transient `GameController`, scrubber) — Phase 3+, scrubber Phase 13.
- **Tutorial title field on the topbar** — defers to Phase 3 alongside the `Tutorial` POCO it binds to.
- **No `HexMapView` reserved-top-height adjustment** — accept that the top of the map sits behind the Map Edit HUD strip in Map Edit mode (Phase 14 polish if needed).
- **No new unit tests** — the new files are all view-layer (Node-derived, excluded from the test assembly). Manual regression is the safety net, same rationale as Phase 1.

## Self-review notes

Self-reviewed against the spec section "TutorialBuilder scene" (lines 415–446) + "Dev-mode gating" (lines 649–656) and the master plan's Phase 2 entry. Coverage check:

- ✅ `tutorial_builder.tscn` rooted on `TutorialBuilderScene : Node2D` — Task 5 + Task 6.
- ✅ 3-mode topbar (Map Edit / Build / Preview), kbd 1/2/3 — Task 3 (topbar buttons + ModeRequested events) + Task 5 (`_UnhandledInput` keycode handlers).
- ✅ Save Tutorial / Load Tutorial / Exit chrome (Save/Load disabled until Phase 3) — Task 3 (`SaveEnabled` / `LoadEnabled` default false; topbar leaves them disabled).
- ✅ Map Edit mode hosts `MapEditorPanel` — Task 5 instantiates the panel and a chrome-trimmed `MapEditorHudView`.
- ✅ Build / Preview show "Coming soon" placeholders — Task 4.
- ✅ `MainMenuScene` adds debug-only "Tutorial Builder" button — Task 7.
- ✅ Painted hex persists when switching modes — Task 5's `SetMode` only toggles `PaintingEnabled` and chrome visibility; the panel and its draft state are never torn down. Task 9 manual-test items #5, #8, #10 verify this.
- ✅ Mode transitions: Map Edit → Build/Preview disables paint, Build/Preview → Map Edit enables paint — Task 5's `SetMode` sets `_panel.PaintingEnabled = mode == TutorialMode.MapEdit`.
- ✅ Dev-mode gating on the menu entry — Task 7 wraps the new button in `if (OS.IsDebugBuild())`.

Spec deviations noted:
- The spec wireframe shows a **Tutorial title field** on the topbar — Phase 2 omits it. Rationale: the `Tutorial` POCO doesn't exist until Phase 3, so a title field would have nothing to bind to. Phase 3 adds both the POCO and the field together.
- The spec mentions a warning dialog ("Map edits may invalidate beats. Continue?") on Build → Map Edit — Phase 2 omits it because there are no beats to invalidate yet. Phase 3+ adds the warning when `Tutorial.Beats.Count > 0`.

No placeholders in any task body; all type signatures used (`TutorialMode`, `MapEditorPanel.PaintingEnabled`, `TopOffsetPx`, `ShowSceneRootChrome`, `SetCurrentMode`, `SetPanel`, `Pause`, `OnTutorialBuilderPressed`) are defined exactly once across the tasks. The `IReadOnlyList<Player>` / `Player` constructor used in `BuildPlayers` matches the existing pattern in `MapEditorScene.BuildPlayers`.
