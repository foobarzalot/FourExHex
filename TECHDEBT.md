# Tech Debt

Running list of known issues, flaky tests, and shortcuts that should eventually be cleaned up. Add new entries at the top.

## Window-based modal dialogs render with Godot's default blue inner panel

**Where:** `scripts/SlotPickerDialog.cs` (Load Game / Load Map / Load
Tutorial pickers across the main menu, map editor, and tutorial
builder); any other `Window`-subclass modal (e.g. the `AcceptDialog`
sibling created inside `SlotPickerDialog` for load errors). Anything
opened via `PopupCentered()` from a host scene rather than via the
`SettingsPanel`-style overlay.

**Symptom:** With the new project theme applied, the dialog's title
bar and close button render correctly (Godot defaults), but the
content area shows Godot's default bright-blue StyleBox instead of
the slate `bg-panel` we use everywhere else. Tried setting
`Window/styles/embedded_border` (and `embedded_unfocused_border`) to a
slate `StyleBoxFlat`, including with `content_margin_top` bumped to
clear the 36-px title height — neither was picked up; Godot 4
appears to draw the bright-blue inner panel from a separate theme
key on top of (or instead of) the embedded border in some embedded-
subwindow paths. No console errors, the override just silently has
no effect.

**Fix plan (the dialog-polish task #8 in the visual-redesign branch
plan):** Don't fight Godot's `Window` theming. Rebuild
`SlotPickerDialog` (and any sibling `AcceptDialog`) on the
`CanvasLayer + ColorRect backdrop + PanelContainer` pattern that
`SettingsPanel` already uses successfully — the `PanelContainer`
picks up the regular `PanelContainer/styles/panel` from the theme
and the backdrop dims the menu behind. The Window class can be
replaced with a plain `Control` root since we don't need OS-window
behavior. While we're there, restyle save rows as 8-px-radius
`bg-elev` panels per the redesign spec instead of the current
`Button` rows.

**Stop-gap:** The theme intentionally omits all `Window/...` and
`AcceptDialog/...` entries (see the comment block in
`theme/fourexhex_theme.tres`) so dialogs fall back to Godot defaults
rather than render half-styled. Buttons / labels / row contents
*inside* the dialogs still get the slate theme; only the outer
chrome and inner content panel are default-blue.
